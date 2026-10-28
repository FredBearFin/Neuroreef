// The Prehistory tab (DESIGN §7.6): progress in symbolic years, the evolution settings as sliders,
// one card per branch with the standing of each of its clades, and the button that begins the world.
// The settings change how genomes vary and are selected; they never change a law of the world.

import uPlot from '../lib/uPlot.esm.js';

const $ = (id) => document.getElementById(id);
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const STAND = ['gone (extinct here)', 'dying (no births)', 'breeding, not keeping up', 'self-sustaining'];
const STAND_C = ['#ff5d73', '#ff9a3d', '#e6c35a', '#4fd18b'];
const BRANCH_C = ['#4fd1c5', '#a77bff', '#ffb547', '#ff5d8f', '#7bd46b', '#5ab0ff'];

// [key, label, min, max, step, format, help]
const KNOBS = [
  ['totalSteps', 'length of the prehistory', 10000, 2000000, 10000, (v) => myr(v), 'when the world begins on its own'],
  ['mutRate', 'brain mutation rate', 0.0001, 0.005, 0.0001, (v) => (v * 2096).toFixed(1) + ' per birth', 'expected brain-gene changes in each newborn (nature: about one)'],
  ['mutBody', 'body mutation rate', 0, 0.05, 0.001, (v) => (v * 38).toFixed(2) + ' per birth', 'expected body-gene changes in each newborn (each body gene is a whole polygenic trait)'],
  ['mutSmall', 'small mutation size', 0.5, 12, 0.5, (v) => v.toFixed(1), 'typical size of an ordinary change'],
  ['mutBigShare', 'big jumps', 0, 0.5, 0.01, (v) => Math.round(v * 100) + '%', 'share of changes that are large jumps'],
  ['mutBig', 'big jump size', 1, 64, 1, (v) => v.toFixed(0), ''],
];

let yps = 1000;
function myr(steps) { const y = steps * yps / 1e6; return (y >= 10 ? Math.round(y) : y.toFixed(1)) + ' Myr'; }

export class PrehistoryTab {
  constructor(meta, link, host) {
    this.meta = meta; this.link = link; this.host = host;
    this.last = null; this.view = 0; this.series = []; this.editing = {};
    host.innerHTML = `
      <h3>Prehistory</h3>
      <p class="note">Before the world begins, life evolves here at full speed in separate worlds on the same land.
        Each starts from the same random ancestors and is then births and deaths only: nothing is ever added, so a clade
        that dies out is gone from that branch. Clades split (speciate) on their own, so each branch grows its own tree
        of clades. When the prehistory ends, every branch's brains are saved (branch-0.nrbank, branch-1.nrbank, ...) and the world starts
        from the branch you pick below, or from the one that did best.</p>
      <div id="pre-prog"><div class="pre-bar"><div id="pre-fill"></div></div>
        <div class="pre-line"><b id="pre-years">–</b><span id="pre-eta"></span></div></div>
      <div class="pre-btns"><button id="pre-pause">Pause</button><button id="pre-begin" class="go">Begin the world now</button></div>
      <h3>Branches <span class="dim">· tap one to watch it</span></h3>
      <div id="pre-branches"></div>
      <div class="pre-key">${STAND.map((s, i) => `<span><i style="background:${STAND_C[i]}"></i>${s}</span>`).join('')}</div>
      <h3>Self-sustaining clades per branch</h3>
      <div id="pre-chart"></div>
      <h3>Evolution settings</h3>
      <p class="note">Mutation: how much each newborn's genes differ from its parents'. Changes apply to every branch within a moment.</p>
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
    const left = (total - p.step) / Math.max(1, p.rate);
    $('pre-years').textContent = `${myr(p.step)} of ${myr(total)}`;
    $('pre-eta').textContent = p.paused ? 'paused' : p.finished ? 'finished' :
      `${Math.round(p.rate).toLocaleString()} steps/s per branch · about ${left > 5400 ? (left / 3600).toFixed(1) + ' h' : Math.ceil(left / 60) + ' min'} left · births only`;
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
    const box = $('pre-branches'), names = this.meta.traitNames, NT = names.length;
    box.innerHTML = p.branches.map((b, i) => {
      // this branch's own clade table: the clades differ between branches
      const cl = b.clades || [], used = cl.filter((c) => c && c.used);
      const st = (s) => (b.standing && b.standing[s]) | 0;
      const self = used.filter((c) => st(c.id) === 3).length, gone = used.filter((c) => st(c.id) === 0).length;
      const tr = (s) => b.count[s] > 0 && b.traits ? '\nevolved: ' + names.map((n, k) => `${n} ×${(b.traits[s * NT + k] ?? 0).toFixed(2)}`).join(', ') : '';
      const order = used.slice().sort((a, c) => (st(c.id) > 0) - (st(a.id) > 0) || a.ancestor - c.ancestor || a.born - c.born);
      const chips = order.map((c) => {
        const s = c.id, k = st(s);
        const tip = `${c.name} · ${c.niche || '–'}\n${STAND[k]} · ${(b.count[s] | 0).toLocaleString()} alive · ${Math.round(b.births[s] || 0)} births in the last ~1,000 steps · generation ${(b.generation[s] || 0).toFixed(1)}` +
          `\n${c.parent >= 0 ? `split off at step ${c.born.toLocaleString()}` : 'ancestor'}${c.extinct >= 0 && !c.alive ? ` · extinct at ${c.extinct.toLocaleString()}` : ''}${tr(s)}`;
        return `<span class="sc" title="${esc(tip)}" style="background:${STAND_C[k]};border-top-color:${c.color}"></span>`;
      }).join('');
      return `<div class="branch${i === p.view ? ' on' : ''}" data-b="${i}">
        <div class="bh"><i style="background:${BRANCH_C[i % BRANCH_C.length]}"></i><b>Branch ${i}</b>
          <span>${b.alive.toLocaleString()} alive · ${used.length} clades · <b style="color:${STAND_C[3]}">${self}</b> self-sustaining · ${gone} gone · ${(b.splits || 0).toLocaleString()} speciation${b.splits === 1 ? '' : 's'}${b.full ? ' · <b style="color:var(--warn)">full</b>' : ''}</span></div>
        <div class="scs">${chips || '<span class="note">no clades</span>'}</div>
        <button class="go small" data-begin="${i}" title="End the prehistory and begin the world from this branch's clades">Begin the world from branch ${i}</button></div>`;
    }).join('') + `<p class="note">One square per clade, coloured by how it is doing (the stripe on top is the clade's own colour). Hover a square for the clade.</p>`;
    for (const d of box.querySelectorAll('.branch')) d.onclick = () => this.link.send({ cmd: 'branch', index: +d.dataset.b });
    for (const btn of box.querySelectorAll('[data-begin]')) btn.onclick = (ev) => {
      ev.stopPropagation();
      const n = btn.dataset.begin;
      if (confirm(`End the prehistory now and begin the world from branch ${n}? Every branch's brains are kept.`)) this.link.send({ cmd: 'begin', branch: +btn.dataset.begin });
    };
  }

  _chart(p) {
    const s = this.series;
    if (!s.length || s[0][s[0].length - 1] < p.step / 1e6 * yps) {
      if (!s.length) { s.push([]); for (let i = 0; i < p.branches.length; i++) s.push([]); }
      s[0].push(p.step * yps / 1e6);
      p.branches.forEach((b, i) => s[i + 1].push((b.clades || []).filter((c) => c && c.used && b.standing[c.id] === 3).length));
      if (s[0].length > 2000) for (const a of s) a.shift();
    }
    const host = $('pre-chart');
    if (!host.offsetParent) return;
    const w = Math.max(200, host.clientWidth);
    if (!this.plot || this.plotW !== w) {
      if (this.plot) this.plot.destroy();
      this.plotW = w;
      this.plot = new uPlot({ width: w, height: 170, scales: { x: { time: false }, y: { range: (u, mn, mx) => [0, Math.max(4, Math.ceil(mx * 1.15))] } },
        axes: [{ stroke: '#6f8599', grid: { stroke: '#16212c' }, size: 30, values: (u, v) => v.map((x) => x + ' Myr') }, { stroke: '#6f8599', grid: { stroke: '#16212c' }, size: 34 }],
        cursor: { drag: { x: false, y: false } },
        series: [{ label: 'Myr' }, ...p.branches.map((b, i) => ({ label: 'Branch ' + i, stroke: BRANCH_C[i % BRANCH_C.length], width: 1.6, points: { show: false } }))] }, s, host);
    } else this.plot.setData(s);
  }
}
