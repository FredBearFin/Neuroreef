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
link.listen('agents', (a) => world && world.setAgents(a));
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
