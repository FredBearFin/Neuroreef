// The Prehistory tab (DESIGN §7.6): progress in symbolic years, the evolution settings as sliders,
// one card per branch with every species' standing, and the button that begins the world.
// The settings change how genomes vary and are selected; they never change a law of the world.

import uPlot from '../lib/uPlot.esm.js';

const $ = (id) => document.getElementById(id);
const STAND = ['gone', 'propped up by colonists', 'breeding', 'self-sustaining'];
const STAND_C = ['#ff5d73', '#ff9a3d', '#e6c35a', '#4fd18b'];
const BRANCH_C = ['#4fd1c5', '#a77bff', '#ffb547', '#ff5d8f', '#7bd46b', '#5ab0ff'];

// [key, label, min, max, step, format, help]
const KNOBS = [
  ['totalSteps', 'length of the prehistory', 10000, 2000000, 10000, (v) => myr(v), 'when the world begins on its own'],
  ['colonistSteps', 'colonists arrive until', 0, 2000000, 10000, (v) => myr(v), 'after this, each branch must live on births alone'],
  ['floor', 'colonist floor', 0, 1, 0.01, (v) => Math.round(v * 100) + '%', 'colonists top a species up to this share of its starting count (fading to zero)'],
  ['mutRate', 'mutation rate', 0.001, 0.2, 0.001, (v) => (v * 100).toFixed(1) + '%', 'chance that each gene changes in a child'],
  ['mutSmall', 'small mutation size', 0.5, 12, 0.5, (v) => v.toFixed(1), 'typical size of an ordinary change'],
  ['mutBigShare', 'big jumps', 0, 0.5, 0.01, (v) => Math.round(v * 100) + '%', 'share of changes that are large jumps'],
  ['mutBig', 'big jump size', 1, 64, 1, (v) => v.toFixed(0), ''],
  ['archiveShare', 'colonists bred from the best', 0, 1, 0.05, (v) => Math.round(v * 100) + '%', 'the rest are fresh random brains'],
  ['tournament', 'selection pressure', 1, 16, 1, (v) => v.toFixed(0), 'parents are the best of this many archive genomes'],
  ['migrateEvery', 'migration every', 1000, 200000, 1000, (v) => myr(v), 'best genomes move to the next branch'],
  ['migrants', 'migrants per species', 0, 16, 1, (v) => v.toFixed(0), ''],
];

let yps = 1000;
function myr(steps) { const y = steps * yps / 1e6; return (y >= 10 ? Math.round(y) : y.toFixed(1)) + ' Myr'; }

export class PrehistoryTab {
  constructor(meta, link, host) {
    this.meta = meta; this.link = link; this.host = host;
    this.last = null; this.view = 0; this.series = []; this.editing = {};
    host.innerHTML = `
      <h3>Prehistory</h3>
      <p class="note">Before the world begins, life evolves here at full speed in separate branches on the same land.
        Colonists keep species from vanishing while their brains are still bad; the best genomes are bred and
        swapped between branches. When it ends, each species takes its brains from the branch where it did best,
        and the world starts from those animals: births and deaths only, no colonists ever.</p>
      <div id="pre-prog"><div class="pre-bar"><div id="pre-fill"></div><div id="pre-col"></div></div>
        <div class="pre-line"><b id="pre-years">–</b><span id="pre-eta"></span></div></div>
      <div class="pre-btns"><button id="pre-pause">Pause</button><button id="pre-begin" class="go">Begin the world now</button></div>
      <h3>Branches <span class="dim">· tap one to watch it</span></h3>
      <div id="pre-branches"></div>
      <div class="pre-key">${STAND.map((s, i) => `<span><i style="background:${STAND_C[i]}"></i>${s}</span>`).join('')}</div>
      <h3>Self-sustaining species per branch</h3>
      <div id="pre-chart"></div>
      <h3>Evolution settings</h3>
      <p class="note">These steer the search, not the world. Changes apply to every branch within a moment.</p>
      <div id="pre-knobs"></div>`;
    const box = $('pre-knobs');
    for (const [key, label, min, max, step, f, help] of KNOBS) {
      const row = document.createElement('div');
      row.className = 'knob';
      row.innerHTML = `<label><span>${label}</span><b id="kv-${key}">–</b></label>
        <input type="range" id="k-${key}" min="${min}" max="${max}" step="${step}">${help ? `<div class="help">${help}</div>` : ''}`;
      box.append(row);
      const inp = row.querySelector('input');
      inp.oninput = () => { this.editing[key] = performance.now(); $('kv-' + key).textContent = f(+inp.value); };
      inp.onchange = () => { this.editing[key] = performance.now(); link.send({ cmd: 'evo', set: { [key]: +inp.value } }); };
    }
    $('pre-pause').onclick = () => { const p = !(this.last && this.last.paused); link.send({ cmd: p ? 'pause' : 'resume' }); };
    $('pre-begin').onclick = () => {
      if (confirm('End the prehistory now and begin the world from the brains evolved so far?')) link.send({ cmd: 'begin' });
    };
  }

  update(p) {
    this.last = p; yps = p.yearsPerStep;
    const e = p.settings, total = e.totalSteps;
    $('pre-fill').style.width = Math.min(100, 100 * p.step / total) + '%';
    $('pre-col').style.left = Math.min(100, 100 * e.colonistSteps / total) + '%';
    const left = (total - p.step) / Math.max(1, p.rate);
    $('pre-years').textContent = `${myr(p.step)} of ${myr(total)}`;
    $('pre-eta').textContent = p.paused ? 'paused' : p.finished ? 'finished' :
      `${Math.round(p.rate).toLocaleString()} steps/s per branch · about ${left > 5400 ? (left / 3600).toFixed(1) + ' h' : Math.ceil(left / 60) + ' min'} left` +
      (p.step < e.colonistSteps ? ' · colonists on' : ' · colonists over: births only');
    $('pre-pause').textContent = p.paused ? 'Resume' : 'Pause';
    $('pre-pause').classList.toggle('on', p.paused);
    // knobs (do not overwrite one being dragged)
    for (const [key, , , , , f] of KNOBS) {
      if (performance.now() - (this.editing[key] || 0) < 3000) continue;
      $('k-' + key).value = e[key]; $('kv-' + key).textContent = f(e[key]);
    }
    this._branches(p);
    this._chart(p);
  }

  _branches(p) {
    const box = $('pre-branches'), sp = this.meta.species;
    box.innerHTML = p.branches.map((b, i) => {
      const self = b.standing.filter((s) => s === 3).length, gone = b.standing.filter((s) => s === 0).length;
      const chips = b.standing.map((st, s) => `<span class="sc" title="${sp[s].name}: ${STAND[st]} · ${b.count[s].toLocaleString()} alive · ${Math.round(b.births[s])} births, ${Math.round(b.colonists[s])} colonists in the last ~1,000 steps · generation ${b.generation[s].toFixed(1)}" style="background:${STAND_C[st]}"></span>`).join('');
      return `<div class="branch${i === p.view ? ' on' : ''}" data-b="${i}">
        <div class="bh"><i style="background:${BRANCH_C[i % BRANCH_C.length]}"></i><b>Branch ${String.fromCharCode(65 + i)}</b>
          <span>${b.alive.toLocaleString()} alive · <b style="color:${STAND_C[3]}">${self}</b> self-sustaining · ${gone} gone${b.full ? ' · <b style="color:var(--warn)">full</b>' : ''}</span></div>
        <div class="scs">${chips}</div></div>`;
    }).join('') + `<p class="note">${p.migrations} migrations so far. Hover a square for the species.</p>`;
    for (const d of box.querySelectorAll('.branch')) d.onclick = () => this.link.send({ cmd: 'branch', index: +d.dataset.b });
  }

  _chart(p) {
    const s = this.series;
    if (!s.length || s[0][s[0].length - 1] < p.step / 1e6 * yps) {
      if (!s.length) { s.push([]); for (let i = 0; i < p.branches.length; i++) s.push([]); }
      s[0].push(p.step * yps / 1e6);
      p.branches.forEach((b, i) => s[i + 1].push(b.standing.filter((x) => x === 3).length));
      if (s[0].length > 2000) for (const a of s) a.shift();
    }
    const host = $('pre-chart');
    if (!host.offsetParent) return;
    const w = Math.max(200, host.clientWidth);
    if (!this.plot || this.plotW !== w) {
      if (this.plot) this.plot.destroy();
      this.plotW = w;
      this.plot = new uPlot({ width: w, height: 170, scales: { x: { time: false }, y: { range: [0, this.meta.species.length] } },
        axes: [{ stroke: '#6f8599', grid: { stroke: '#16212c' }, size: 30, values: (u, v) => v.map((x) => x + ' Myr') }, { stroke: '#6f8599', grid: { stroke: '#16212c' }, size: 34 }],
        cursor: { drag: { x: false, y: false } },
        series: [{ label: 'Myr' }, ...p.branches.map((b, i) => ({ label: 'Branch ' + String.fromCharCode(65 + i), stroke: BRANCH_C[i % BRANCH_C.length], width: 1.6, points: { show: false } }))] }, s, host);
    } else this.plot.setData(s);
  }
}
