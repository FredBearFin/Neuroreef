// Console entry point: connects to the simulation, builds the 3D view and the panels, wires controls.

import { Link } from './net.js';
import { World3D } from './world3d.js';
import { Panels, bar } from './panels.js';
import { PrehistoryTab } from './prehistory.js';

const $ = (id) => document.getElementById(id);
const link = new Link();
let meta = null, world = null, panels = null, probe = null, lastProbeT = 0, extinct = [];
let pre = null, phase = null, phaseVersion = -1, lastPre = null;
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
link.listen('prehistory', (p) => { lastPre = p; if (pre) pre.update(p); });
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
    it.title = `${s.name} · ${s.niche}${s.parent >= 0 ? ` · split off at step ${s.born.toLocaleString()}` : ' · ancestor'}`;
    if (at >= 0) { it.className = 'ext'; it.innerHTML = `<i></i>${s.name} <b>extinct at ${at.toLocaleString()}</b>`; }
    else it.innerHTML = `<i style="background:${s.color}"></i>${s.name} <em>${s.count.toLocaleString()}</em>`;
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
  for (const b of document.querySelectorAll('#speed button')) {
    b.onclick = () => {
      if (b.dataset.cmd === 'pause') { const p = b.classList.toggle('on'); link.send({ cmd: p ? 'pause' : 'resume' }); return; }
      if (b.dataset.cmd === 'step') { link.send({ cmd: 'step' }); return; }
      link.send({ cmd: 'rate', value: +b.dataset.rate });
      for (const o of document.querySelectorAll('#speed button[data-rate]')) o.classList.toggle('on', o === b);
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
    $('s-pre').textContent = `PREHISTORY · ${Math.round(lastPre.step * lastPre.yearsPerStep / 1e6)} of ${Math.round(lastPre.settings.totalSteps * lastPre.yearsPerStep / 1e6)} Myr · branch ${String.fromCharCode(65 + s.view)}`;
  }
  $('s-step').textContent = s.step.toLocaleString();
  if (panels) panels.setStep(s.step);
  $('s-alive').textContent = s.alive.toLocaleString();
  $('s-rate').textContent = s.paused ? 'paused' : Math.round(s.measured).toString();
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
