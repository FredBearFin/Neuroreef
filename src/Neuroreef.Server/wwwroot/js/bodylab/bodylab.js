// Body Lab page (bodylab.html): the lineage of a body-plan run (BodyLabData.cs) in 3D. The champion of the chosen
// generation turns slowly in the middle (drag to orbit, wheel to zoom), the best of the other species below it, a
// timeline under them from the first blob to the last generation, and on the right what each part does by the
// physics evaluators' numbers. Data: a live run or a bodylab-*.json file from the lab server (Neuroreef.exe --bodylab),
// or any bodylab-*.json opened from disk (this page also works from the normal console, file only).
//   ?file=NAME  open that file of the lab server   ?frame=N  start at timeline position N (-1 the last)
//   ?roles=1    role colours                      ?still=1  no auto-rotation (screenshots)
//   ?focus=ROLE the camera on the champion's parts with that role (crushing, support, gas, ...)

import * as THREE from 'three';
import { OrbitControls } from '../../lib/OrbitControls.js';
import { buildBody, disposeBody, ROLE_COLORS, ROLE_NAMES } from './build.js';

const q = new URLSearchParams(location.search);
const $ = id => document.getElementById(id);
const canvas = $('gl'), wrap = $('glwrap');
const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, preserveDrawingBuffer: true });
renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.15;
renderer.setScissorTest(true);

const LOOKS = {
  'sea-floor': { bg: 0x0b2331, ground: 0x6b6450, fog: 0x0b2331, sky: 0x9fd6ff, gnd: 0x3a3320 },
  'open-water': { bg: 0x08263c, ground: null, fog: 0x08263c, sky: 0xa8e1ff, gnd: 0x0a2a3a },
  'land': { bg: 0x1c1e18, ground: 0x5d5034, fog: 0x1c1e18, sky: 0xfff1d8, gnd: 0x3b3020 },
};
const STRIP = 0.3;   // the species strip's share of the view's height

function lights(scene, look) {
  scene.add(new THREE.HemisphereLight(look.sky, look.gnd, 1.5));
  const key = new THREE.DirectionalLight(0xfff4e6, 2.4); key.position.set(3, 5, 4); scene.add(key);
  const rim = new THREE.DirectionalLight(0x8fd8ff, 1.2); rim.position.set(-4, 2, -5); scene.add(rim);
}

function stageScene(look) {
  const s = new THREE.Scene();
  s.background = new THREE.Color(look.bg);
  s.fog = new THREE.Fog(look.fog, 7, 22);
  lights(s, look);
  return s;
}

function groundAt(scene, look, y, r) {
  if (look.ground == null) return null;
  const g = new THREE.Mesh(new THREE.CircleGeometry(r, 64), new THREE.MeshStandardMaterial({ color: look.ground, roughness: 0.95 }));
  g.rotation.x = -Math.PI / 2; g.position.y = y; scene.add(g);
  return g;
}
function contactShadow(scene, y, r) {
  const c = document.createElement('canvas'); c.width = c.height = 128;
  const x = c.getContext('2d'), gr = x.createRadialGradient(64, 64, 4, 64, 64, 64);
  gr.addColorStop(0, 'rgba(0,0,0,0.55)'); gr.addColorStop(1, 'rgba(0,0,0,0)');
  x.fillStyle = gr; x.fillRect(0, 0, 128, 128);
  const m = new THREE.Mesh(new THREE.PlaneGeometry(r * 2, r * 2), new THREE.MeshBasicMaterial({ map: new THREE.CanvasTexture(c), transparent: true, depthWrite: false }));
  m.rotation.x = -Math.PI / 2; m.position.y = y + 0.002; scene.add(m);
  return m;
}

// ---------- state ----------
let data = null;            // the LabFile
let frames = [];            // the timeline: snapshots, the last one replaced by the final species
let frameIx = 0, live = null, playing = false, roleColors = q.get('roles') === '1';
let main = null, mainObj = null, tiles = [], hot = null;
const still = q.get('still') === '1';

const mainCam = new THREE.PerspectiveCamera(36, 1, 0.01, 100);
mainCam.position.set(2.6, 1.7, 3.6);
const controls = new OrbitControls(mainCam, canvas);
controls.enableDamping = true; controls.autoRotate = !still; controls.autoRotateSpeed = 0.9;
controls.target.set(0, 0.1, 0); controls.minDistance = 1.2; controls.maxDistance = 14;
let userMoved = false;
controls.addEventListener('start', () => { userMoved = true; });
const VIEW_DIR = new THREE.Vector3(2.2, 1.0, 3.0).normalize();
/** The distance at which a sphere of radius r fills a view of this vertical fov (degrees) and aspect. */
function fitDistance(r, fov, aspect) {
  const t = Math.tan(fov * Math.PI / 360);
  if (!(aspect > 0.05) || !Number.isFinite(aspect)) aspect = 1;
  return r / Math.min(t, t * aspect) * 1.08;
}

function setupMain(look) {
  main = stageScene(look);
  groundAt(main, look, -1.0, 30);
  contactShadow(main, -1.0, 1.7);
}

function placeOnGround(obj, b, built, groundY) {
  // a body that walks or crawls rests on the floor; a swimmer hovers above it
  const swims = b.mode === 'swims';
  obj.position.y = groundY - built.bottom + (swims ? 0.45 : 0.0);
}

function showFrame(i) {
  if (!frames.length) return;
  frameIx = Math.max(0, Math.min(frames.length - 1, i));
  $('slider').value = frameIx;
  const f = frames[frameIx];
  const look = LOOKS[data.envKey] || LOOKS['sea-floor'];
  if (!main || main.userData.env !== data.envKey) { setupMain(look); main.userData.env = data.envKey; }
  if (mainObj) { main.remove(mainObj); disposeBody(mainObj); }
  const built = buildBody(f.champion, { size: 2.4, roles: roleColors });
  mainObj = built.group; placeOnGround(mainObj, f.champion, built, -1.0);
  main.add(mainObj);
  buildTiles(f, look);
  const b = f.champion;
  const esc = (t) => String(t ?? '').replace(/[&<>"']/g, (c) => ({'&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'}[c]));
  if (data.source === 'world') {
    // a live world's real animals (--world-bodies): each lineage's first adult; the step is the world's
    $('main-lab').innerHTML = `<b>step ${f.gen.toLocaleString()}</b> · lineage ${esc(b.lineage)} · ${b.size.toLocaleString()} alive · ${mass(b.mass)}, ${len(b.length)} long · ${b.mode}`;
    $('tl-gen').textContent = `step ${f.gen.toLocaleString()} of ${data.generations.toLocaleString()}`;
    $('tl-note').textContent = `${data.env}, seed ${data.seed} · ${f.species} lineages alive · the largest has ${Math.round(f.best).toLocaleString()} animals`;
  } else {
  $('main-lab').innerHTML = `<b>generation ${f.gen + 1}</b> · champion · ${mass(b.mass)}, ${len(b.length)} long · ${b.mode} · fitness ${fx(b.fitness)}`;
  $('tl-gen').textContent = `generation ${f.gen + 1} of ${data.generations}${live && !data.done ? ' (evolving…)' : ''}`;
  $('tl-note').textContent = `${data.env}, seed ${data.seed}, ${data.population} bodies · ${f.species} species · best ${fx(f.best)}, mean ${fx(f.mean)}`;
  }
  document.querySelectorAll('#tiles-head').forEach((e) => { e.textContent = data.source === 'world' ? 'largest lineages at this step' : 'species at this generation'; });
  sidePanel(b);
  drawSpark();
}

// ---------- species strip ----------
let tileScene = null;
function buildTiles(f, look) {
  tiles.forEach(t => { tileScene.remove(t.obj); disposeBody(t.obj); });
  tiles = [];
  tileScene = stageScene(look);
  tileScene.fog = null;
  document.querySelectorAll('.tile-lab').forEach(e => e.remove());
  const list = [f.champion, ...(f.reps || [])].slice(0, 6);
  list.forEach((b, k) => {
    const built = buildBody(b, { size: 2.0, roles: roleColors });
    const obj = built.group; obj.position.x = k * 8;
    obj.position.y = -built.bottom - 0.6 + (b.mode === 'swims' ? 0.25 : 0);
    tileScene.add(obj);
    if (look.ground != null) {
      const gnd = new THREE.Mesh(new THREE.CircleGeometry(1.8, 48), new THREE.MeshStandardMaterial({ color: look.ground, roughness: 0.95 }));
      gnd.rotation.x = -Math.PI / 2; gnd.position.set(k * 8, -0.6, 0); tileScene.add(gnd);
    }
    const cam = new THREE.PerspectiveCamera(34, 1, 0.01, 50);
    cam.userData.at = new THREE.Vector3(k * 8, -0.1, 0);
    const el = document.createElement('div');
    el.className = 'tile-lab';
    const nm = String(b.lineage ?? '').replace(/[&<>"']/g, '');
    el.innerHTML = (data.source === 'world' ? `<b>${nm}</b> · ${b.size} alive<br>` : `<b>${k === 0 ? 'champion' : 'species ' + (k + 1)}</b>${b.size ? ` · ${b.size} bodies` : ''} · fit ${fx(b.fitness)}<br>`) + `${mass(b.mass)} · ${b.segments} seg · ${b.pairCount || 0} pairs · ${b.mode}${b.rigidity > 0.5 ? ' · rigid' : ''}`;
    $('tiles').appendChild(el);
    tiles.push({ obj, cam, el, b, spin: k * 0.9 });
  });
  layoutTiles();
}
function layoutTiles() {
  const n = Math.max(1, tiles.length), w = wrap.clientWidth;
  tiles.forEach((t, k) => { t.el.style.left = ((k + 0.5) / n * w) + 'px'; t.el.style.width = (w / n - 10) + 'px'; });
}

// ---------- the side panel ----------
const fx = x => (x ?? 0).toFixed(2);
const pct = x => (100 * (x ?? 0)).toFixed((x ?? 0) < 0.1 ? 1 : 0) + '%';
function mass(kg) { kg = kg || 0; return kg >= 1 ? kg.toFixed(2) + ' kg' : (1000 * kg).toFixed(kg >= 0.01 ? 0 : 1) + ' g'; }
function len(m) { m = m || 0; return m >= 0.1 ? (100 * m).toFixed(1) + ' cm' : (1000 * m).toFixed(m >= 0.01 ? 0 : 1) + ' mm'; }
const hex = c => '#' + c.toString(16).padStart(6, '0');
const chip = r => `<span class="chip" style="background:${hex(ROLE_COLORS[r] ?? 0x888888)}">${ROLE_NAMES[r] ?? r}</span>`;

function sidePanel(b) {
  $('champ-title').textContent = `${mass(b.mass)} · ${len(b.length)} · ${b.mode}`;
  $('champ-sum').textContent = b.summary || '';
  const kv = [
    ['fitness', fx(b.fitness)], ['segments', b.segments || 0], ['pairs', b.pairCount || 0],
    ['speed', (b.speed || 0).toFixed(3) + ' m/s'], ['joints', b.jointCount || 0], ['rigidity', fx(b.rigidity)],
    ['crush', (b.crush || 0).toFixed(1) + ' N'], ['food items', pct(b.itemShare)], ['bite kills', pct(b.pKill)],
    ['escape', pct(b.escape)], ['O2 by skin', pct(b.skinO2)], ['moulting', pct(b.moult)],
  ];
  $('champ-kv').innerHTML = kv.map(([k, v]) => `<div><span class="k">${k}</span><span class="v">${v}</span></div>`).join('');
  // consecutive parts grown by the same gene that do the same thing are one line ("segments 2-5, each: ...")
  const groups = [];
  const add = (kind, list) => list.forEach((p, i) => {
    const sig = kind + '|' + p.gene + '|' + (p.roles || []).map(r => r.role).join(',');
    const last = groups[groups.length - 1];
    if (last && last.sig === sig && last.idx[last.idx.length - 1] === i - 1) last.idx.push(i);
    else groups.push({ sig, kind, idx: [i], p });
  });
  add('seg', b.segs || []); add('pair', b.pairs || []);
  $('parts').innerHTML = groups.map((g, gi) => {
    const n = g.idx.length, a = g.idx[0] + 1, z = g.idx[n - 1] + 1;
    const what = g.kind === 'seg' ? (n > 1 ? `Segments ${a}–${z}` : `Segment ${a}`) : (n > 1 ? `Appendage pairs ${a}–${z}` : `Appendage pair ${a}`);
    const p = g.p;
    const shape = g.kind === 'seg'
      ? `${len(p.l)} long, ${len(p.w)} wide, ${len(p.h)} high; integument ${(1000 * (p.intT || 0)).toFixed(2)} mm, mineral ${fx(p.intMin)}${(p.axSk || 0) > 0 ? `; internal skeleton ${(100 * p.axSk).toFixed(1)}% of the section, mineral ${fx(p.axMin)}` : ''}`
      : `on segment ${(p.seg || 0) + 1};${len(p.len)} long, ${len(p.chord)} wide, ${len(p.thick)} thick; ${p.joints || 0} joints, mineral ${fx(p.mineral)}${p.sagittal ? ', in the midplane' : ''}`;
    const roles = (p.roles || []);
    return `<div class="part" data-g="${gi}"><div class="t"><span class="dot" style="background:${hex(ROLE_COLORS[p.primary] ?? 0x888888)}"></span>${what}
      <span class="n">${shape}</span></div>
      <div>${roles.map(r => chip(r.role)).join(' ')}</div>
      ${roles.map(r => `<p>${n > 1 ? 'Each: ' : ''}${r.text}</p>`).join('')}</div>`;
  }).join('');
  document.querySelectorAll('#parts .part').forEach(el => {
    const g = groups[+el.dataset.g];
    el.onmouseenter = () => highlight(g.idx.map(i => ({ kind: g.kind, index: i })));
    el.onmouseleave = () => highlight([]);
    el.onclick = () => focusParts(g.idx.map(i => ({ kind: g.kind, index: i })));
    el.title = 'click: move the camera to it (double-click the view to see the whole body again)';
  });
  $('legend').innerHTML = Object.keys(ROLE_COLORS).map(r => `<span><span class="dot" style="background:${hex(ROLE_COLORS[r])}"></span>${ROLE_NAMES[r]}</span>`).join('');
}

function highlight(parts) {
  if (!mainObj) return;
  const want = new Set(parts.map(p => p.kind + p.index));
  mainObj.traverse(o => {
    const p = o.userData.part; if (!p || !o.material?.emissive) return;
    o.material.emissive.setHex(want.has(p.kind + p.index) ? 0x3a3a3a : 0x000000);
  });
}

/** Moves the camera to these parts (a small jaw on a long body is otherwise a speck), keeping its direction. */
function focusParts(parts) {
  if (!mainObj) return;
  const want = new Set(parts.map(p => p.kind + p.index)), box = new THREE.Box3();
  mainObj.updateMatrixWorld(true);
  mainObj.traverse(o => { const p = o.userData.part; if (p && want.has(p.kind + p.index)) box.expandByObject(o); });
  if (box.isEmpty()) return;
  const c = box.getCenter(new THREE.Vector3()), r = Math.max(0.05, box.getSize(new THREE.Vector3()).length() / 2);
  const dir = mainCam.position.clone().sub(controls.target).normalize();
  controls.target.copy(c);
  mainCam.position.copy(c).addScaledVector(dir, Math.max(controls.minDistance * 0.2, fitDistance(r * 1.6, mainCam.fov, mainCam.aspect)));
  controls.minDistance = 0.05;
  userMoved = true;
  highlight(parts);
}
function unfocus() { controls.target.set(0, 0.1, 0); controls.minDistance = 1.2; userMoved = false; }
canvas.addEventListener('dblclick', unfocus);

// ---------- hover in 3D ----------
const ray = new THREE.Raycaster(), mouse = new THREE.Vector2();
canvas.addEventListener('pointermove', ev => {
  const r = canvas.getBoundingClientRect();
  const y = ev.clientY - r.top;
  const tip = $('tip');
  if (!mainObj || y > r.height * (1 - STRIP)) { tip.style.display = 'none'; return; }
  mouse.set((ev.clientX - r.left) / r.width * 2 - 1, -(y / (r.height * (1 - STRIP))) * 2 + 1);
  ray.setFromCamera(mouse, mainCam);
  const hit = ray.intersectObject(mainObj, true).find(h => h.object.userData.part);
  const p = hit?.object.userData.part;
  const key = p ? p.kind + p.index : null;
  if (key !== hot) { hot = key; highlight(p ? [p] : []); }
  if (!p) { tip.style.display = 'none'; return; }
  const b = frames[frameIx].champion, part = p.kind === 'seg' ? b.segs[p.index] : b.pairs[p.index];
  tip.innerHTML = `<div class="t">${p.kind === 'seg' ? 'Segment ' : 'Appendage pair '}${p.index + 1}</div>${(part.roles || []).map(r => r.text).join(' ')}`;
  tip.style.display = 'block';
  tip.style.left = Math.min(r.width - 350, ev.clientX - r.left + 14) + 'px'; tip.style.top = (y + 14) + 'px';
});
canvas.addEventListener('pointerleave', () => { $('tip').style.display = 'none'; highlight([]); hot = null; });

// ---------- the fitness line over the generations ----------
function drawSpark() {
  const c = $('spark'), w = c.clientWidth, h = c.clientHeight, dpr = Math.min(window.devicePixelRatio || 1, 2);
  if (c.width !== w * dpr) { c.width = w * dpr; c.height = h * dpr; }
  const x = c.getContext('2d'); x.setTransform(dpr, 0, 0, dpr, 0, 0); x.clearRect(0, 0, w, h);
  if (frames.length < 2) return;
  const vals = frames.flatMap(f => [f.best, f.mean]).filter(Number.isFinite);
  let lo = Math.min(...vals), hi = Math.max(...vals); if (hi - lo < 1e-6) hi = lo + 1;
  lo = Math.max(lo, hi - 8);   // the founders' very low fitness would flatten the rest
  const G = data.generations - 1 || 1;
  const px = g => g / G * w, py = v => h - 2 - (Math.max(lo, v) - lo) / (hi - lo) * (h - 4);
  for (const [key, col] of [['mean', '#56697c'], ['best', '#4fd1c5']]) {
    x.beginPath(); frames.forEach((f, i) => { const X = px(f.gen), Y = py(f[key]); i ? x.lineTo(X, Y) : x.moveTo(X, Y); });
    x.strokeStyle = col; x.lineWidth = 1.5; x.stroke();
  }
  if (lo < 0 && hi > 0) { x.strokeStyle = '#2a3b4e'; x.setLineDash([3, 3]); x.beginPath(); x.moveTo(0, py(0)); x.lineTo(w, py(0)); x.stroke(); x.setLineDash([]); }
  const X = px(frames[frameIx].gen); x.fillStyle = '#ffd34d'; x.fillRect(X - 1, 0, 2, h);
  x.fillStyle = '#8aa0b5'; x.font = '10px system-ui'; x.fillText('fitness: best', 4, 10); x.fillStyle = '#56697c'; x.fillText('mean', 78, 10);
}

// ---------- data ----------
function setData(d) {
  data = d;
  frames = [...(d.snapshots || [])];
  if (d.final && frames.length && frames[frames.length - 1].gen === d.final.gen)
    frames[frames.length - 1] = { ...d.final, mean: frames[frames.length - 1].mean };
  else if (d.final) frames.push(d.final);
  $('slider').max = Math.max(0, frames.length - 1);
  $('empty').classList.toggle('hidden', frames.length > 0);
}

function empty(msg) { const e = $('empty'); e.innerHTML = msg; e.classList.remove('hidden'); }

let labServer = false;
async function listRuns(select) {
  let j;
  try { const r = await fetch('api/bodylab/runs'); if (!r.ok) throw 0; j = await r.json(); } catch { j = null; }
  const sel = $('runsel');
  if (!j?.lab) {
    sel.innerHTML = '<option>open a bodylab-*.json file</option>';
    if (!data) empty('No Body Lab server here. Start <code>Neuroreef.exe --bodylab</code> and open the address it prints,<br>or open a <code>bodylab-*.json</code> file with “open file…”.');
    return;
  }
  labServer = true;
  $('runbox').classList.remove('hidden');
  if (!$('runbox').dataset.init) {
    $('runbox').dataset.init = 1;
    $('r-gens').value = j.defaults.generations; $('r-pop').value = j.defaults.population; $('r-seed').value = j.defaults.seed;
  }
  const opts = [];
  for (const r of [...j.runs].reverse()) opts.push([`live:${r.key}`, `${r.done ? '' : '● '}${r.env}, seed ${r.seed} — ${r.done ? (r.error || 'done') : `gen ${r.gen + 1}/${r.generations}`} (this session)`]);
  for (const f of j.files) opts.push([`file:${f.name}`, `${f.name} (${f.kb} KB, ${f.modified})`]);
  const cur = sel.value;
  sel.innerHTML = opts.map(([v, t]) => `<option value="${v}">${t}</option>`).join('') || '<option value="">no runs yet: press Run live</option>';
  const running = j.runs.find(r => !r.done);
  const want = select ?? (q.get('file') ? `file:${q.get('file')}` : running ? `live:${running.key}` : cur && opts.some(o => o[0] === cur) ? cur : opts[0]?.[0]);
  if (want && opts.some(o => o[0] === want)) { sel.value = want; if (want !== cur || select) await choose(want); }
  else if (!data) empty('No runs yet. Press <b>Run live</b> to evolve one, or start <code>Neuroreef.exe --bodylab</code>.');
}

async function choose(v) {
  live = null;
  if (v.startsWith('file:')) {
    $('status').textContent = 'loading ' + v.slice(5) + '…';
    const r = await fetch('api/bodylab/file/' + encodeURIComponent(v.slice(5)));
    if (!r.ok) { $('status').textContent = 'could not load it'; return; }
    setData(await r.json());
    $('status').textContent = '';
    start();
  } else if (v.startsWith('live:')) {
    live = { key: v.slice(5), since: 0 };
    data = null; frames = [];
    await poll(true);
  }
}

function start() {
  const f = q.get('frame');
  showFrame(f == null ? frames.length - 1 : +f < 0 ? frames.length + +f : +f);
  const fo = q.get('focus');   // ?focus=crushing: the camera on the parts with that role
  if (fo) {
    const b = frames[frameIx].champion;
    const ps = [...(b.segs || []).map((s, i) => s.primary === fo ? { kind: 'seg', index: i } : null),
      ...(b.pairs || []).map((p, i) => p.primary === fo ? { kind: 'pair', index: i } : null)].filter(Boolean);
    if (ps.length) { mainCam.aspect = wrap.clientWidth / (wrap.clientHeight * (1 - STRIP)); focusParts(ps); }
  }
  ready();
}

async function poll(first) {
  if (!live) return;
  const lv = live;
  try {
    const r = await fetch(`api/bodylab/live/${encodeURIComponent(lv.key)}?since=${lv.since}`);
    if (!r.ok) throw 0;
    const j = await r.json();
    if (live !== lv) return;
    const atEnd = !frames.length || frameIx >= frames.length - 1;
    const prev = data?.snapshots || [];
    const d = j.file; d.snapshots = prev.concat(d.snapshots || []);
    lv.since = d.snapshots.length;
    setData(d);
    $('status').textContent = d.done ? `finished: ${d.env}, seed ${d.seed}` : `evolving ${d.env}, seed ${d.seed}: generation ${j.gen + 1} of ${d.generations}`;
    if (frames.length && (first || atEnd)) { showFrame(frames.length - 1); ready(); }
    else if (frames.length) { $('slider').max = frames.length - 1; drawSpark(); $('tl-gen').textContent = `generation ${frames[frameIx].gen + 1} of ${d.generations} (evolving…)`; }
    if (d.done) { live = null; listRuns(); return; }
  } catch { $('status').textContent = 'lost the lab server'; }
  setTimeout(() => poll(false), 1000);
}

$('runsel').onchange = e => choose(e.target.value);
$('file').onchange = async e => {
  const f = e.target.files[0]; if (!f) return;
  try { live = null; setData(JSON.parse(await f.text())); $('status').textContent = f.name; start(); }
  catch (err) { $('status').textContent = 'not a Body Lab file: ' + err.message; }
};
$('roles').checked = roleColors;
$('roles').onchange = e => { roleColors = e.target.checked; showFrame(frameIx); };
$('slider').oninput = e => { playing = false; $('play').textContent = '▶'; showFrame(+e.target.value); };
$('play').onclick = () => {
  playing = !playing; $('play').textContent = playing ? '❚❚' : '▶';
  if (playing && frameIx >= frames.length - 1) showFrame(0);
};
setInterval(() => { if (playing) { if (frameIx < frames.length - 1) showFrame(frameIx + 1); else { playing = false; $('play').textContent = '▶'; } } }, 350);
$('r-go').onclick = async () => {
  const body = { env: $('r-env').value, seed: +$('r-seed').value, generations: +$('r-gens').value, population: +$('r-pop').value };
  const r = await fetch('api/bodylab/start', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) });
  const j = await r.json().catch(() => ({}));
  if (!r.ok) { $('status').textContent = j.error || 'could not start'; return; }
  await listRuns(`live:${j.key}`);
};
window.addEventListener('keydown', e => {
  if (e.target.tagName === 'INPUT' && e.target.type !== 'range') return;
  if (e.key === 'ArrowRight') showFrame(frameIx + 1);
  if (e.key === 'ArrowLeft') showFrame(frameIx - 1);
  if (e.key === ' ') { e.preventDefault(); $('play').click(); }
});

// ---------- render ----------
let frameCount = 0, readyAsked = false;
function ready() { readyAsked = true; }
function resize() {
  const w = wrap.clientWidth, h = wrap.clientHeight;
  if (canvas.width !== Math.floor(w * renderer.getPixelRatio()) || canvas.height !== Math.floor(h * renderer.getPixelRatio())) { renderer.setSize(w, h, false); layoutTiles(); drawSpark(); }
  return [w, h];
}
const clock = new THREE.Clock();
function loop() {
  requestAnimationFrame(loop);
  const [w, h] = resize();
  const dt = Math.min(0.1, clock.getDelta());
  renderer.setClearColor(0x070b10, 1);
  renderer.setViewport(0, 0, w, h); renderer.setScissor(0, 0, w, h); renderer.clear();
  if (!main || !data) return;
  controls.update();
  const mh = Math.round(h * (1 - STRIP)), th = h - mh;
  mainCam.aspect = w / mh; mainCam.updateProjectionMatrix();
  if (!userMoved && mh > 10 && w > 10) {   // frame the body until the viewer moves the camera themselves
    const d = mainCam.position.clone().sub(controls.target);
    if (!(d.lengthSq() > 1e-6) || !Number.isFinite(d.x)) d.copy(VIEW_DIR);
    mainCam.position.copy(controls.target).addScaledVector(d.normalize(), fitDistance(1.3, mainCam.fov, mainCam.aspect));
  }
  renderer.setViewport(0, th, w, mh); renderer.setScissor(0, th, w, mh);
  renderer.render(main, mainCam);
  const n = tiles.length;
  tiles.forEach((t, k) => {
    if (!still) t.obj.rotation.y += dt * 0.35;
    else t.obj.rotation.y = 0.5;
    const x0 = Math.round(k / n * w), x1 = Math.round((k + 1) / n * w);
    t.cam.aspect = (x1 - x0) / th; t.cam.updateProjectionMatrix();
    t.cam.position.copy(t.cam.userData.at).addScaledVector(VIEW_DIR, fitDistance(1.15, t.cam.fov, t.cam.aspect));
    t.cam.lookAt(t.cam.userData.at);
    renderer.setViewport(x0, 0, x1 - x0, th); renderer.setScissor(x0, 0, x1 - x0, th);
    renderer.render(tileScene, t.cam);
  });
  if (readyAsked && ++frameCount >= 1) window.labReady = true;
}
loop();
listRuns();
window.bodyLab = { camera: mainCam, controls, get body() { return mainObj; }, get frames() { return frames; }, show: showFrame };
