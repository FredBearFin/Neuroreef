// Console entry point: connects to the simulation, builds the 3D view and the panels, wires controls.

import { Link } from './net.js';
import { World3D } from './world3d.js';
import { Panels, bar } from './panels.js';

const $ = (id) => document.getElementById(id);
const link = new Link();
let meta = null, world = null, panels = null, probe = null, lastProbeT = 0, extinct = [];

link.listen('meta', (m) => {
  meta = m;
  if (!world) {
    world = new World3D($('gl'), meta);
    panels = new Panels(meta, link);
    panels.onSelect = select;
    panels.show('inspect');
    buildControls();
    requestAnimationFrame(frame);
  }
  $('connect').classList.add('hidden');
});
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
link.listen('close', () => $('connect').classList.remove('hidden'));
link.connect();

function frame() {
  world.render(probe);
  requestAnimationFrame(frame);
}

// ------------------------------------------------------------------ selection and cameras

function select(serial, follow) {
  if (!world) return;
  world.selected = serial;
  link.send({ cmd: 'select', serial, follow: !!follow || world.mode !== 'overview' });
  if (!serial) { probe = null; panels.renderProbe(null); hud(); if (world.mode !== 'overview') setCam('overview'); }
  else panels.show('inspect');
}

function setCam(m) {
  if (m !== 'overview' && !world.selected) {
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
  for (const s of meta.species) sel.append(new Option(s.name, s.id));
  $('exp-go').onclick = () => {
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
  // legend toggles species visibility
  const lg = $('legend');
  for (const s of meta.species) {
    const it = document.createElement('span');
    it.dataset.sp = s.id;
    it.innerHTML = `<i style="background:${s.color}"></i>${s.name}`;
    it.onclick = () => { if (world.hidden.has(s.id)) world.hidden.delete(s.id); else world.hidden.add(s.id); it.classList.toggle('off'); };
    lg.append(it);
  }
  // a small screen (tablet) asks for fewer frames
  if (matchMedia('(max-width: 900px)').matches) link.send({ cmd: 'view', hz: 12, fieldsMs: 2000 });
}

// ------------------------------------------------------------------ HUD: drives, what it is doing and why

function hud() {
  const box = $('hud');
  if (!probe || !world || world.mode === 'overview') { box.classList.add('hidden'); return; }
  box.classList.remove('hidden');
  const P = meta.probe, f = probe.f, sc = (n) => f[P.scal + P.scalars.indexOf(n)];
  const sp = meta.species[sc('species') | 0];
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
  $('s-step').textContent = s.step.toLocaleString();
  $('s-alive').textContent = s.alive.toLocaleString();
  $('s-rate').textContent = s.paused ? 'paused' : Math.round(s.measured).toString();
  $('s-season').textContent = s.season > 0.33 ? 'wet' : s.season < -0.33 ? 'dry' : s.season > 0 ? 'wetting' : 'drying';
  $('s-random').classList.toggle('hidden', s.bankSource !== 'random');
  extinct = s.extinct || [];
  markExtinct();
  $('s-full').classList.toggle('hidden', s.free > 0);
  if (world && s.follow && s.probe && world.selected !== s.probe && world.mode !== 'overview') world.selected = s.probe;
  if (world && world.mode !== 'overview' && !world.selected && s.probe) world.selected = s.probe;
}

// an extinct species keeps its label: nothing in this world can bring it back
function markExtinct() {
  for (const it of document.querySelectorAll('#legend span')) {
    const s = meta.species[+it.dataset.sp], at = extinct[s.id];
    it.innerHTML = `<i style="background:${s.color}"></i>${s.name}${at >= 0 ? ` <b style="color:#ff5d73">extinct at ${at.toLocaleString()}</b>` : ''}`;
  }
  if (panels) panels.extinct = extinct;
}

function feed(e) {
  const box = $('feed');
  if (!e.items.length) { box.innerHTML = '<span class="item">no large changes in the last ~1,000 steps</span>'; return; }
  box.innerHTML = e.items.slice(0, 6).map((it) => {
    const c = it.species >= 0 ? meta.species[it.species].color : '#8aa0b5';
    return `<span class="item"><i style="background:${c}"></i>${it.text}</span>`;
  }).join('');
}
