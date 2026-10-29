// Console entry point: connects to the simulation, builds the 3D view and the panels, wires controls.

import { Link } from './net.js';
import { World3D } from './world3d.js';
import { Panels, bar } from './panels.js';
import { PrehistoryTab } from './prehistory.js';

const $ = (id) => document.getElementById(id);
const link = new Link();
let meta = null, world = null, panels = null, probe = null, lastProbeT = 0, extinct = [];
let pre = null, phase = null, phaseVersion = -1, lastPre = null, rateClicked = -1e9;
let clades = [], pendingClades = null;   // the live clade table (32 slots; unused ones have used = false)

link.listen('meta', (m) => {
  meta = m;
  if (!world) {
    world = new World3D($('gl'), meta);
    panels = new Panels(meta, link);
    panels.onSelect = select;
    pre = new PrehistoryTab(meta, link, $('tab-prehistory'));
    panels.show('inspect');
    buildControls();
    requestAnimationFrame(frame);
  }
  setClades(pendingClades || m.species, 0); pendingClades = null;
  $('connect').classList.add('hidden');
});
link.listen('clades', (c) => { if (!meta) { pendingClades = c.list; return; } setClades(c.list, c.step); });
link.listen('world', (w) => world && world.setWorld(w));
link.listen('agents', (a) => { if (!world) return; world.setAgents(a); callSounds(); });
link.listen('lexicon', (l) => { lexicon = l; drawTranslator(); });
link.listen('fights', (f) => { if (world) world.addFights(f.list); fightLog(f.list); fightSound(f.list); });
link.listen('fields', (f) => { if (world) world.setFields(f); if (panels) panels.setFields(f); });
link.listen('history', (h) => panels && panels.setHistory(h));
link.listen('telemetry', (t) => panels && panels.addSample(t));
link.listen('fingerprints', (fp) => panels && panels.setFingerprints(fp));
link.listen('explain', (e) => feed(e));
link.listen('probe', (p) => {
  probe = p;
  if (p) {
    const P = meta.probe;
    probe.sight = p.f[P.phen + P.phenotype.indexOf('sight')];
    if (world && world.selected !== p.serial) world.selected = p.serial;   // lineage follow moved to a child
  }
  const now = performance.now();
  if (now - lastProbeT > 250) { lastProbeT = now; panels.renderProbe(p, world); hud(); }
});
link.listen('status', (s) => status(s));
link.listen('prehistory', (p) => { lastPre = p; if (pre) pre.update(p); if (performance.now() - rateClicked > 2000 && p.rateSet !== undefined && window.showRate) window.showRate(p.rateSet); });
link.listen('close', () => $('connect').classList.remove('hidden'));
link.connect();

function frame() {
  world.render(probe);
  requestAnimationFrame(frame);
}

// ------------------------------------------------------------------ selection and cameras

// ------------------------------------------------------------------ the clade table

function setClades(list, step) {
  if (!list) return;
  announceSplits(list);
  clades = list;
  meta.species = list;           // everything that reads meta.species sees the live table
  if (world && typeof world.setClades === 'function') world.setClades(list);
  if (panels) panels.setClades(list, step);
  if (pre && typeof pre.setClades === 'function') pre.setClades(list);
  buildLegend();
  fillExperience();
}

const cladeOf = (s) => clades[s] && clades[s].used ? clades[s] : null;
// the clade table is resent the moment a clade dies out; it is the authority on extinction
const extinctAt = (c) => c.alive ? -1 : c.extinct;

// the legend: living clades with their colour, name and count; extinct ones greyed with the step they died out.
// An extinct clade keeps its label: nothing in this world can bring it back.
let legendSig = '';
function buildLegend() {
  const lg = $('legend'); if (!lg || !world) return;
  const used = clades.filter((c) => c.used);
  const alive = used.filter((c) => extinctAt(c) < 0).sort((a, b) => b.count - a.count || a.id - b.id);
  const dead = used.filter((c) => extinctAt(c) >= 0).sort((a, b) => extinctAt(b) - extinctAt(a));
  const sig = alive.map((c) => `${c.serial}:${c.count}:${c.color}`).join() + '|' + dead.map((c) => `${c.serial}:${extinctAt(c)}`).join() + '|' + [...world.hidden].join();
  if (sig === legendSig) return;
  legendSig = sig;
  lg.innerHTML = '';
  for (const s of alive.concat(dead)) {
    const at = extinctAt(s), it = document.createElement('span');
    it.dataset.sp = s.id;
    const par = s.parent >= 0 && clades[s.parent] ? clades[s.parent].name : '';
    it.title = `${s.name} · ${s.niche}${s.parent >= 0 ? ` · ${s.sub ? 'subspecies' : 'species'} split from ${par} at step ${s.born.toLocaleString()}${s.why ? ': ' + s.why : ''}` : ' · ancestor'}`;
    if (at >= 0) { it.className = 'ext'; it.innerHTML = `<i></i>${s.name} <b>extinct at ${at.toLocaleString()}</b>`; }
    else it.innerHTML = `<i style="background:${s.color}"></i>${s.name}${s.parent >= 0 ? `<sup>${s.sub ? 'ssp.' : 'sp.'}</sup>` : ''} <em>${s.count.toLocaleString()}</em>`;
    if (world.hidden.has(s.id)) it.classList.add('off');
    it.onclick = () => { if (world.hidden.has(s.id)) world.hidden.delete(s.id); else world.hidden.add(s.id); it.classList.toggle('off'); legendSig = ''; };
    lg.append(it);
  }
}

// the Experience select lists the living clades
function fillExperience() {
  const sel = $('exp-species'); if (!sel) return;
  const alive = clades.filter((c) => c.used && extinctAt(c) < 0);
  const sig = alive.map((c) => c.serial + c.name).join();
  if (sel.dataset.sig === sig) return;
  sel.dataset.sig = sig;
  const prev = sel.value;
  sel.innerHTML = '';
  for (const s of alive) sel.append(new Option(s.name, s.id));
  if (alive.some((c) => String(c.id) === prev)) sel.value = prev;
  $('exp-go').disabled = !alive.length;
}

function select(serial, follow) {
  if (!world) return;
  world.selected = serial;
  link.send({ cmd: 'select', serial, follow: !!follow || world.mode !== 'overview' });
  if (!serial) { probe = null; panels.renderProbe(null); hud(); if (world.mode !== 'overview') setCam('overview'); }
  else panels.show('inspect');
}

function setCam(m) {
  if (m !== 'overview' && !world.selected && $('exp-species').value !== '') {
    // no animal selected: experience the chosen species
    link.send({ cmd: 'pick', species: +$('exp-species').value });
  }
  world.setMode(m);
  for (const b of document.querySelectorAll('#cams button')) b.classList.toggle('on', b.dataset.cam === m);
  if (world.selected) link.send({ cmd: 'follow', on: m !== 'overview' });
  hud();
}

function buildControls() {
  for (const b of document.querySelectorAll('#cams button')) b.onclick = () => setCam(b.dataset.cam);
  const sel = $('exp-species');
  fillExperience();
  $('exp-go').onclick = () => {
    if (sel.value === '') return;
    link.send({ cmd: 'pick', species: +sel.value });
    world.selected = 0;
    if (world.mode === 'overview') { world.setMode('follow'); for (const b of document.querySelectorAll('#cams button')) b.classList.toggle('on', b.dataset.cam === 'follow'); }
  };
  // the speed buttons show the speed that is set (the server's), so the right one is lit in the prehistory too
  window.showRate = (r) => { for (const o of document.querySelectorAll('#speed button[data-rate]')) o.classList.toggle('on', +o.dataset.rate === Math.round(r)); };
  for (const b of document.querySelectorAll('#speed button')) {
    b.onclick = () => {
      if (b.dataset.cmd === 'pause') { const p = b.classList.toggle('on'); link.send({ cmd: p ? 'pause' : 'resume' }); return; }
      if (b.dataset.cmd === 'step') { link.send({ cmd: 'step' }); return; }
      link.send({ cmd: 'rate', value: +b.dataset.rate });
      rateClicked = performance.now();
      showRate(+b.dataset.rate);
    };
  }
  // tap to select (ignore drags)
  const cv = $('gl'); let down = null;
  cv.addEventListener('pointerdown', (e) => { down = [e.clientX, e.clientY]; });
  cv.addEventListener('pointerup', (e) => {
    if (!down || Math.hypot(e.clientX - down[0], e.clientY - down[1]) > 6) return;
    if (speakArmed !== null) { speakAt(e.clientX, e.clientY); return; }   // the translator is waiting for a place to speak
    const s = world.pick(e.clientX, e.clientY);
    if (s) select(s);
  });
  // the legend (toggles clade visibility) is built from the clade table: buildLegend()
  // a small screen (tablet) asks for fewer frames
  if (matchMedia('(max-width: 900px)').matches) link.send({ cmd: 'view', hz: 12, fieldsMs: 2000 });
}

// ------------------------------------------------------------------ HUD: drives, what it is doing and why

function hud() {
  const box = $('hud');
  if (!probe || !world || world.mode === 'overview') { box.classList.add('hidden'); return; }
  box.classList.remove('hidden');
  const P = meta.probe, f = probe.f, sc = (n) => f[P.scal + P.scalars.indexOf(n)];
  const sp = cladeOf(sc('species') | 0) || { name: '?', color: '#8aa0b5', niche: '' };
  $('hud-name').innerHTML = `<span style="color:${sp.color}">●</span> ${sp.name} #${probe.serial}`;
  $('hud-sub').textContent = `${sp.niche} · ${['', 'egg', 'embryo', 'juvenile', 'adult'][sc('stage') | 0]} · generation ${sc('generation') | 0} · ${world.mode === 'ride' ? 'riding along' : 'following'}`;
  const feat = (n) => f[P.feat + meta.features.indexOf(n)];
  $('hud-drives').innerHTML = [['energy', sc('energy') / Math.max(1e-6, sc('emax')), '#7bd46b'], ['hunger', feat('HUNGER'), '#e6c35a'], ['thirst', feat('THIRST'), '#5ab0ff'],
    ['fatigue', feat('FATIGUE'), '#b48cff'], ['fear', feat('FEAR'), '#ff8a2a'], ['pain', feat('PAIN'), '#ff5d73']].map(([n, v, c]) => bar(n, v, 1, c)).join('');
  // top primitives
  const gains = meta.primitives.map((n, i) => [n, f[P.gain + i], i]).sort((a, b) => b[1] - a[1]).slice(0, 5);
  $('hud-gains').innerHTML = gains.map(([n, v, i]) => bar(n.toLowerCase(), v, 1, i >= 20 ? '#ffb547' : '#4fd1c5')).join('');
  // why: the inputs that push the strongest primitive up the most (weight × input)
  const p = gains[0][2], nin = P.nin, y = [];
  for (let j = 0; j < P.nh; j++) y.push(['memory ' + j, f[P.hid + j]]);
  for (let j = 0; j < meta.features.length; j++) y.push([meta.features[j].toLowerCase().replace('_', ' '), f[P.feat + j]]);
  const contrib = y.map(([n, v], j) => [n, v, f[P.w2 + p * nin + j] * v]).filter((c) => c[0] !== 'bias').sort((a, b) => b[2] - a[2]).slice(0, 3);
  $('hud-why').innerHTML = `<div class="why"><b>${meta.primitives[p].toLowerCase()}</b> is strongest, pushed up by:</div>` +
    contrib.map(([n, v, c]) => `<div class="why">· <b>${n}</b> = ${v.toFixed(2)} (contributes ${c >= 0 ? '+' : ''}${c.toFixed(2)})</div>`).join('') +
    (world.perceived.length ? `<div class="why" style="margin-top:6px">sees ${count('kin')} kin, ${count('smaller')} smaller, ${count('larger')} larger, ${count('egg')} eggs</div>` : '');
}
function count(k) { return world.perceived.filter((p) => p.kind === k).length; }

// ------------------------------------------------------------------ top bar and feed

function status(s) {
  // the world after a prehistory can be on another map (a valley): start the console afresh on it
  if (s.mapSide && meta && s.mapSide !== meta.world) { location.reload(); return; }
  if (s.phase !== phase) enterPhase(s.phase);
  if (s.phaseVersion !== phaseVersion) {
    // another simulation is on view (a branch, or the world after the prehistory): drop what we drew
    const first = phaseVersion === -1;
    phaseVersion = s.phaseVersion;
    if (!first) {
      // the history frame of the new simulation may have arrived before this status: ask for it again
      if (panels) panels.reset();
      link.send({ cmd: 'history' });
      legendSig = '';
    }
    if (world) { world.selected = 0; probe = null; if (panels) panels.renderProbe(null); hud(); }
  }
  if (s.phase === 'prehistory' && lastPre) {
    const tot = lastPre.settings.totalSteps, my = Math.round(lastPre.step * lastPre.yearsPerStep / 1e6);
    $('s-pre').textContent = tot >= 2147483647 ? `PREHISTORY · ${my} Myr · branch ${s.view}` : `PREHISTORY · ${my} of ${Math.round(tot * lastPre.yearsPerStep / 1e6)} Myr · branch ${s.view}`;
  }
  $('s-step').textContent = s.step.toLocaleString();
  if (panels) panels.setStep(s.step);
  $('s-alive').textContent = s.alive.toLocaleString();
  $('s-rate').textContent = s.paused ? 'paused' : Math.round(s.measured).toString();
  if (performance.now() - rateClicked > 2000 && window.showRate) window.showRate(s.rate);
  $('s-season').textContent = (s.season > 0.33 ? 'wet' : s.season < -0.33 ? 'dry' : s.season > 0 ? 'wetting' : 'drying') +
    (s.rain < -0.15 ? ' · drought year' : s.rain > 0.15 ? ' · wet year' : '');
  $('s-random').classList.toggle('hidden', s.bankSource !== 'random' || s.phase !== 'live');
  extinct = s.extinct || [];
  markExtinct();
  $('s-full').classList.toggle('hidden', s.free > 0);
  if (world && s.follow && s.probe && world.selected !== s.probe && world.mode !== 'overview') world.selected = s.probe;
  if (world && world.mode !== 'overview' && !world.selected && s.probe) world.selected = s.probe;
}

function enterPhase(p) {
  const isPre = p === 'prehistory';
  phase = p;
  $('s-pre').classList.toggle('hidden', !isPre);
  $('s-born').classList.toggle('hidden', isPre);
  $('tabbtn-pre').classList.toggle('hidden', !isPre);
  for (const b of document.querySelectorAll('#speed button[data-rate], #speed button[data-cmd=step]')) b.disabled = isPre;
  if (panels) panels.show(isPre ? 'prehistory' : 'inspect');
}

// the status frame says which clades died out (it arrives more often than the clade table)
function markExtinct() {
  if (panels) panels.extinct = extinct;
  buildLegend();
  fillExperience();
}

function feed(e) {
  const box = $('feed');
  if (!e.items.length) { box.innerHTML = '<span class="item">no large changes in the last ~1,000 steps</span>'; return; }
  box.innerHTML = e.items.slice(0, 6).map((it) => {
    const c = it.species >= 0 && cladeOf(it.species) ? cladeOf(it.species).color : '#8aa0b5';
    return `<span class="item"><i style="background:${c}"></i>${it.text}</span>`;
  }).join('');
}

// the fight log: the latest kills and wounds in words; tap one to fly there
const fightLines = []; let killsSeen = 0;
function fightLog(list) {
  const box = $('fights'); if (!box) return;
  const name = (id) => { const c = clades.find((q) => q && q.id === id); return c ? `<b style="color:${c.color}">${c.name}</b>` : 'something'; };
  const VERB = [null, 'wounded', 'killed', 'drank the blood of', 'bled to death', 'ate an egg of'];
  for (const f of list) {
    const [x, y, a, v, kind, step] = f;
    if (kind === 2 || kind === 4) killsSeen++;
    if (!VERB[kind]) continue;
    const text = kind === 4 ? `${name(v)} drained to death by ${name(a)}` : `${name(a)} ${VERB[kind]} ${a === v ? 'its own kind' : (kind === 5 ? '' : 'a ') + name(v)}`;
    fightLines.unshift({ x, y, step, text, kill: kind === 2 || kind === 4 });
  }
  if (fightLines.length > 40) fightLines.length = 40;
  // kills first, then the most recent wounds
  const show = fightLines.filter((l) => l.kill).slice(0, 5).concat(fightLines.filter((l) => !l.kill).slice(0, 2)).sort((p, q) => q.step - p.step);
  box.innerHTML = `<div class="fh">FIGHTS · ${killsSeen} kill${killsSeen === 1 ? '' : 's'} seen</div>` +
    show.map((l, i) => `<div class="fl${l.kill ? ' kill' : ''}" data-i="${fightLines.indexOf(l)}">${l.text} <em>step ${l.step.toLocaleString()}</em></div>`).join('');
  for (const d of box.querySelectorAll('.fl')) d.onclick = () => { const l = fightLines[+d.dataset.i]; if (l && world) world.lookAt(l.x, l.y); };
}

// fight sounds, made on the spot (no files): a thud and a tearing crunch for a kill, a crack for a wound,
// a wet squelch for a bite. Off until the button is pressed (browsers allow sound only after a click).
let audio = null, soundOn = false;
document.getElementById('sound')?.addEventListener('click', (ev) => {
  soundOn = !soundOn;
  if (soundOn && !audio) audio = new (window.AudioContext || window.webkitAudioContext)();
  if (audio && audio.state === 'suspended') audio.resume();
  ev.currentTarget.textContent = soundOn ? 'sound on' : 'sound off';
  ev.currentTarget.classList.toggle('on', soundOn);
});
function noise(t, dur, gain, f0, f1, q) {
  const n = Math.ceil(audio.sampleRate * dur), buf = audio.createBuffer(1, n, audio.sampleRate), d = buf.getChannelData(0);
  for (let i = 0; i < n; i++) d[i] = (Math.random() * 2 - 1) * (1 - i / n) ** 2;
  const src = audio.createBufferSource(); src.buffer = buf;
  const bp = audio.createBiquadFilter(); bp.type = 'bandpass'; bp.Q.value = q;
  bp.frequency.setValueAtTime(f0, t); bp.frequency.exponentialRampToValueAtTime(f1, t + dur);
  const g = audio.createGain(); g.gain.value = gain;
  src.connect(bp).connect(g).connect(audio.destination); src.start(t);
}
function thud(t, gain, f) {
  const o = audio.createOscillator(), g = audio.createGain();
  o.frequency.setValueAtTime(f, t); o.frequency.exponentialRampToValueAtTime(f * 0.35, t + 0.25);
  g.gain.setValueAtTime(gain, t); g.gain.exponentialRampToValueAtTime(0.001, t + 0.3);
  o.connect(g).connect(audio.destination); o.start(t); o.stop(t + 0.32);
}
function fightSound(list) {
  if (!soundOn || !audio || !world) return;
  let made = 0; const t0 = audio.currentTime;
  for (const f of list) {
    const [x, y, , , kind] = f;
    if (kind === 0 || made >= 5) continue;
    const d = world.viewDist(x, y); if (d > 70) continue;
    const v = Math.min(1, 12 / (d + 6)), t = t0 + made * 0.06 + Math.random() * 0.05;
    if (kind === 2 || kind === 4) { thud(t, 0.9 * v, 95 + Math.random() * 30); noise(t + 0.02, 0.35, 0.7 * v, 1800, 300, 1.2); }
    else if (kind === 1) noise(t, 0.09, 0.6 * v, 2600, 900, 2.5);
    else if (kind === 3) noise(t, 0.18, 0.5 * v, 500, 180, 4);
    else if (kind === 5) noise(t, 0.07, 0.5 * v, 3200, 1500, 3);
    made++;
  }
}

// splits: a banner when a clade divides into two visibly different forms, with a button that flies to the new one
const seenSplit = new Set(); let splitsPrimed = false;
function announceSplits(list) {
  const fresh = [];
  for (const c of list) {
    if (!c || !c.used || c.parent < 0 || seenSplit.has(c.serial)) continue;
    seenSplit.add(c.serial);
    if (splitsPrimed) fresh.push(c);   // the clades that already existed when the page opened are not news
  }
  splitsPrimed = true;
  for (const c of fresh) showSplit(c, list[c.parent]);
}
function showSplit(c, p) {
  const box = $('splits'); if (!box) return;
  const d = document.createElement('div');
  d.className = 'split';
  d.innerHTML = `<div class="sh">${c.sub ? 'NEW SUBSPECIES' : 'NEW SPECIES'}</div>` +
    `<div><b style="color:${c.color}">${c.name}</b> split from <b style="color:${p ? p.color : '#aaa'}">${p ? p.name : '?'}</b>` +
    `${c.why ? `: ${c.why}` : ''}</div><button>Show me</button>`;
  d.querySelector('button').onclick = () => {
    if (!world) return;
    let sx = 0, sy = 0, n = 0, W = world.W, rx = null, ry = null;
    for (const e of world.agents.values()) {
      if (e.sp !== c.id) continue;
      if (rx === null) { rx = e.x; ry = e.y; }
      sx += e.x - W * Math.round((e.x - rx) / W); sy += e.y - W * Math.round((e.y - ry) / W); n++;   // unwrap around the first
    }
    if (n) world.lookAt(((sx / n) % W + W) % W, ((sy / n) % W + W) % W);
  };
  box.prepend(d);
  while (box.children.length > 3) box.lastChild.remove();
  setTimeout(() => d.remove(), 90000);
}

// ---- the translator (Revision M): each clade's sounds have no built-in meaning. What one seems to mean is read from
// all 42 senses: those that differ most around a caller from the clade's usual (in the clade's own spread of each sense,
// Meaning.cs on the server), and the behaviours its clade turns to on hearing it. Any meaning that evolves can show,
// not only ones designed in advance; the 7 fixed contexts are kept only as a fallback. You type a word; it is matched to
// a sense (by the plain words for it), the translator picks the sound most made when that sense stands out, and you
// click where to send it. Your call is not an animal: nothing is added to the world, and no audio is recorded.
let lexicon = null, xClade = -1, speakArmed = null, xMeaning = -1, xRaw = -1, myCallId = -1, xText = '', xTargets = [];
// fallback: a typed word -> one of the 7 fixed contexts, when no sense phrase matches
const WORDS = [
  ['danger', 'run', 'flee', 'predator', 'help', 'enemy', 'attack', 'hide', 'watch out', 'scary'],
  ['food', 'eat', 'grass', 'fruit', 'meat', 'dinner'],
  ['mate', 'love', 'partner', 'breed', 'babe'],
  ['hungry', 'starving', 'hunger'],
  ['alone', 'come', 'here', 'where', 'gather', 'follow', 'lonely', 'friend'],
  ['thirsty', 'water', 'drink', 'thirst'],
  ['baby', 'child', 'young', 'lost', 'mother', 'mom', 'kid'],
];
// words of a phrase that say nothing on their own
const XSTOP = new Set(['a', 'an', 'the', 'is', 'in', 'of', 'its', 'it', 'to', 'by', 'from', 'one', 'no', 'not', 'none', 'only', 'other', 'another',
  'animal', 'animals', 'nearest', 'biggest', 'view', 'kind', 'kinds', 'caller', 'up', 'something', 'more', 'than', 'usual', 'sound', 'hearing', 'there',
  'nearby', 'close', 'near', 'far', 'big', 'direction', 'inside', 'all', 'around', 'very', 'grown', 'or', 'and', 'way', 'same', 'good', 'here']);
const xWordRe = (w) => new RegExp(`(^|[^a-z0-9])${w.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}($|[^a-z0-9])`);
// a typed text -> the sense directions it may name, best matches first: [{ f, hi, score }]
function senseTargets(t) {
  const m = meta.meaning; if (!m) return [];
  const out = [];
  for (let f = 0; f < m.high.length; f++) for (const hi of [true, false]) {
    const phrase = hi ? m.high[f] : m.low[f]; if (!phrase) continue;
    // a negated phrase ("no food here") is for a negated text ("no food"), and the other way round: a text that
    // does not agree in negation still matches, but only after every text that does
    const neg = /^(no|not|none|only|far)\b/.test(phrase), tneg = /(^|\W)(no|not|none|never|far)\b/.test(t), agree = neg === tneg;
    let score = 0;
    if (t === phrase.toLowerCase()) score = 10;
    for (const k of (hi ? m.keysHigh[f] : m.keysLow[f])) if (k && xWordRe(k).test(t)) score = Math.max(score, agree ? 3 : 0.5);
    // a word of the phrase itself
    if (score < 1 && agree) for (const w of phrase.toLowerCase().split(/[^a-z0-9-]+/)) if (w && !XSTOP.has(w) && xWordRe(w).test(t)) score = Math.max(score, 1);
    if (score) out.push({ f, hi, score });
  }
  return out.sort((a, b) => b.score - a.score);
}
function readWord() {
  const t = xText.trim().toLowerCase(); xRaw = -1; xMeaning = -1; xTargets = [];
  const m = t.match(/^sound\s*([1-8])$/); if (m) { xRaw = +m[1] - 1; return; }
  if (!t) return;
  xTargets = senseTargets(t);
  if (!xTargets.length) for (let c = 0; c < WORDS.length; c++) if (WORDS[c].some((w) => t === w || t.includes(w))) { xMeaning = c; return; }
}
const XMIN = 25;   // calls before a guess is shown (Meaning.MIN_N on the server)
const soundCss = (k) => { const c = [[1, 0.35, 0.35], [1, 0.7, 0.25], [1, 0.95, 0.3], [0.45, 1, 0.45], [0.3, 0.95, 1], [0.4, 0.55, 1], [0.8, 0.45, 1], [1, 0.5, 0.85]][k]; return `rgb(${c.map((v) => Math.round(v * 255)).join(',')})`; };
const phraseOf = (f, hi) => (meta.meaning && (hi ? meta.meaning.high[f] : meta.meaning.low[f])) || meta.features[f];
// fallback: the old 7 fixed contexts (how much more common around a caller than usual)
function guess(cl, k) {
  const s = cl.sounds[k];
  if (s.n < XMIN) return null;
  let best = -1, lift = 0;
  for (let c = 0; c < s.ctx.length; c++) { const l = s.ctx[c] / Math.max(0.01, cl.bases[c]); if (s.ctx[c] >= 0.1 && l > lift) { lift = l; best = c; } }
  return best >= 0 && lift >= 1.5 ? { c: best, lift } : null;
}
function answer(s) {
  const tot = s.resp.reduce((a, b) => a + b, 0); if (tot < 10) return '';
  let b = 0; for (let r = 1; r < s.resp.length; r++) if (s.resp[r] > s.resp[b]) b = r;
  return `${meta.respNames[b]} ${Math.round(100 * s.resp[b] / tot)}%`;
}
function bestSoundCtx(cl, c) {
  let best = -1, lift = 0;
  for (let k = 0; k < cl.sounds.length; k++) { const s = cl.sounds[k]; if (s.n < XMIN) continue; const l = s.ctx[c] / Math.max(0.01, cl.bases[c]); if (l > lift) { lift = l; best = k; } }
  return best >= 0 && lift >= 1.2 ? { k: best, lift } : null;
}
// the sound whose callers most stand out on the typed sense (highest standardised difference in that direction);
// the best-matching words are tried first, then weaker matches; the fixed contexts only when no sense matched
function bestSound(cl) {
  if (!cl) return null;
  if (xTargets.length) {
    let i = 0;
    while (i < xTargets.length) {
      const tier = xTargets[i].score; let pick = null;
      for (; i < xTargets.length && xTargets[i].score === tier; i++) {
        const t = xTargets[i];
        for (let k = 0; k < cl.sounds.length; k++) {
          const s = cl.sounds[k]; if (s.n < XMIN || !s.z) continue;
          const d = (t.hi ? 1 : -1) * s.z[t.f];
          if (d > 0 && (!pick || d > pick.d)) pick = { k, d, f: t.f, hi: t.hi };
        }
      }
      if (pick) return pick;
    }
    return null;
  }
  if (xMeaning >= 0) { const p = bestSoundCtx(cl, xMeaning); return p ? { k: p.k, lift: p.lift, c: xMeaning } : null; }
  return null;
}
const strength = (d) => `<span class="dim" title="how far from usual, in the clade's own spread of it">${Math.abs(d).toFixed(1)}</span>`;
let xBuiltFor = '';
function drawTranslator() {
  const box = $('translator'); if (!box || box.classList.contains('hidden') || !lexicon || !meta) return;
  const live = lexicon.clades.filter((c) => clades[c.id] && clades[c.id].used);
  if (!live.length) { box.innerHTML = '<div class="th">TRANSLATOR</div><div class="dim">No living clades.</div>'; xBuiltFor = ''; return; }
  // by default, the clade that calls the most; a mute clade only when none calls
  if (!live.find((c) => c.id === xClade)) xClade = live.slice().sort((a, b) => (b.calls || 0) - (a.calls || 0) || (clades[b.id].count || 0) - (clades[a.id].count || 0))[0].id;
  const cl = live.find((c) => c.id === xClade), name = clades[xClade].name;
  // the controls are built once per set of clades, so a menu you are using is never rebuilt under you
  const sig = live.map((c) => c.id + clades[c.id].name + (c.mute ? 'm' : '')).join() + '|' + xClade;
  if (sig !== xBuiltFor) {
    xBuiltFor = sig;
    const order = live.slice().sort((a, b) => (a.mute ? 1 : 0) - (b.mute ? 1 : 0));
    const opts = order.map((c) => `<option value="${c.id}"${c.id === xClade ? ' selected' : ''}>${clades[c.id].name}${c.mute ? ' (mute)' : ''}</option>`).join('');
    box.innerHTML = `<div class="th"><span>TRANSLATOR</span><select id="x-clade">${opts}</select></div>` +
      `<div class="dim" id="x-desc"></div><table id="x-table"></table>` +
      `<div class="speak"><span id="x-to"></span><input id="x-text" type="text" placeholder="type a word: food, danger, alone, hungry, coming…" autocomplete="off"><span id="x-say"></span><button id="x-go">Send</button></div><div id="x-reply" class="reply"></div>`;
    $('x-text').value = xText;
    $('x-clade').onchange = (e) => { xClade = +e.target.value; drawTranslator(); };
    $('x-text').oninput = (e) => { xText = e.target.value; readWord(); drawTranslator(); };
    $('x-text').onkeydown = (e) => { if (e.key === 'Enter') $('x-go').click(); };
    $('x-go').onclick = () => { const pk = xRaw >= 0 ? { k: xRaw } : bestSound(lexicon.clades.find((c) => c.id === xClade)); speakArmed = pk ? pk.k : null; drawTranslator(); };
  }
  if (cl.mute) {
    $('x-desc').textContent = `${name} are mute: none of them has made a sound, so there is nothing to translate. Pick a clade that calls.`;
    $('x-table').innerHTML = '';
  } else {
    $('x-desc').textContent = `What ${name}'s sounds seem to mean: what stands out most around a caller compared with usual (any of its 42 senses), and what ${name} turn to on hearing it. Nothing was given a meaning; it has to evolve. The number is how far from usual, in the clade's own spread.`;
    let rows = '';
    for (let k = 0; k < meta.nSound; k++) {
      const s = cl.sounds[k], when = (s.when || []).slice(0, 2), then = (s.then || []).slice(0, 2);
      let w;
      if (when.length) w = when.map((m) => `<span class="mean">${m.text || phraseOf(m.f, m.hi)}</span> ${strength(m.d)}`).join(', ');
      else { const g = guess(cl, k); w = g ? `<span class="mean">≈ ${meta.ctxNames[g.c]}</span> <span class="dim">×${g.lift.toFixed(1)} (rough)</span>` : `<span class="dim">${s.n < XMIN ? (s.n ? 'too rare to tell' : 'never made') : 'no clear meaning'}</span>`; }
      const a = answer(s);
      const t = then.length ? then.map((m) => `${m.text} ${strength(m.d)}`).join(', ') + (a ? ` <span class="dim">· ${a}</span>` : '') : a ? `<span class="dim">${a}</span>` : '';
      rows += `<tr><td class="snd"><i class="sw" style="background:${soundCss(k)}"></i>sound ${k + 1}<div class="dim">${s.n.toLocaleString()}×</div></td>` +
        `<td><div><span class="dim">when </span>${w}</div>${t ? `<div class="then"><span class="dim">listeners then </span>${t}</div>` : ''}</td></tr>`;
    }
    $('x-table').innerHTML = rows;
  }
  const pick = xRaw >= 0 ? { k: xRaw } : bestSound(cl);
  const want = xTargets.length ? phraseOf(xTargets[0].f, xTargets[0].hi) : xMeaning >= 0 ? meta.ctxNames[xMeaning] : '';
  $('x-to').textContent = `Tell ${name}:`;
  $('x-say').innerHTML = !xText.trim() ? '' : !xTargets.length && xMeaning < 0 && xRaw < 0 ? '<span class="dim">no word for that; try food, danger, hungry, alone, water, mate, coming, or “sound 3”</span>'
    : pick ? `→ <b style="color:${soundCss(pick.k)}">sound ${pick.k + 1}</b>${pick.d ? ` <span class="dim">(made when “${phraseOf(pick.f, pick.hi)}”, ${pick.d.toFixed(1)})</span>` : pick.lift ? ` <span class="dim">(×${pick.lift.toFixed(1)} with “${meta.ctxNames[pick.c]}”, rough)</span>` : ''}`
    : `<span class="dim">${name} have no sound for “${want}” yet; try “sound 1”…“sound 8”</span>`;
  const go = $('x-go'); go.disabled = !pick; go.textContent = speakArmed !== null ? 'click the map…' : 'Send';
  const u = lexicon.user, heard = u && u.id === myCallId ? u.resp.reduce((a, b) => a + b, 0) : 0;
  $('x-reply').textContent = myCallId >= 0 && u && u.id === myCallId ? (heard ? `${heard} heard you: ` + u.resp.map((v, r) => v ? `${meta.respNames[r]} ${v}` : '').filter(Boolean).join(', ') : 'nobody heard you yet (speak near animals)') : '';
  box.classList.toggle('armed', speakArmed !== null);
}
function speakAt(cx, cy) {
  const p = world.groundAt(cx, cy), k = speakArmed; speakArmed = null;
  if (p) {
    link.send({ cmd: 'speak', x: p[0], y: p[1], sound: k });
    world.showMyCall(p[0], p[1], k + 1);
    myCallId = lexicon ? lexicon.user.id + 1 : 0;
    tone(k, 1);
  }
  drawTranslator();
}
$('xlate')?.addEventListener('click', (ev) => { const b = $('translator'); b.classList.toggle('hidden'); ev.currentTarget.classList.toggle('on', !b.classList.contains('hidden')); drawTranslator(); });
// each sound has its own pitch when sound is on
function tone(k, v) {
  if (!soundOn || !audio) return;
  const t = audio.currentTime, o = audio.createOscillator(), g = audio.createGain();
  o.type = 'triangle'; o.frequency.setValueAtTime(330 * Math.pow(2, k / 4), t); o.frequency.exponentialRampToValueAtTime(330 * Math.pow(2, k / 4) * 1.12, t + 0.12);
  g.gain.setValueAtTime(0.0001, t); g.gain.exponentialRampToValueAtTime(0.25 * v, t + 0.02); g.gain.exponentialRampToValueAtTime(0.0001, t + 0.18);
  o.connect(g).connect(audio.destination); o.start(t); o.stop(t + 0.2);
}
let lastCallTone = 0;
function callSounds() {
  if (!soundOn || !audio || !world) return;
  const now = performance.now(); if (now - lastCallTone < 150) return;
  let made = 0;
  for (const e of world.agents.values()) {
    if (!e.call || e.callT !== world.frameT) continue;
    const d = world.viewDist(e.x, e.y); if (d > 40) continue;
    tone(e.call - 1, 0.3 * Math.min(1, 10 / (d + 5))); lastCallTone = now;
    if (++made >= 2) break;
  }
}
