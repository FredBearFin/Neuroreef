// Dashboards and the inspector. Everything shown here is read from the simulation's telemetry, probe
// and fingerprint frames; nothing is inferred by the console.

import uPlot from '../lib/uPlot.esm.js';

const $ = (id) => document.getElementById(id);
const el = (tag, attrs = {}, html = '') => { const e = document.createElement(tag); Object.assign(e, attrs); if (html) e.innerHTML = html; return e; };
const fmt = (v, d = 0) => (v === undefined || v === null || isNaN(v)) ? '–' : Number(v).toLocaleString(undefined, { maximumFractionDigits: d, minimumFractionDigits: d });
const TROPHIC = ['decomposers & scavengers', 'herbivores', 'omnivores', 'predators', 'apex predators'];
const TROPHIC_C = ['#a08a6a', '#7bd46b', '#e6c35a', '#ff5d8f', '#a77bff'];

export class Panels {
  constructor(meta, link) {
    this.meta = meta; this.link = link;
    this.rows = [];               // telemetry samples (Float32Array), oldest first
    this.maxRows = 3000;
    this.charts = {};
    this.window = 32;             // samples summed for "recent" views (~1,000 steps)
    this.sel = { prey: 0, pred: 12, species: 0, layer: 'grass', log: true };
    this.fingerprints = null;
    const T = meta.telemetry;
    this.idx = (s, f) => T.offSpecies + s * T.perSpecies + f;
    this._tabs();
    this._buildInspect(); this._buildPop(); this._buildEnergy(); this._buildWeb(); this._buildBehaviour(); this._buildPlants(); this._buildSpecies();
    addEventListener('resize', () => this._resizeCharts());
  }

  _tabs() {
    for (const b of document.querySelectorAll('#tabs button')) b.onclick = () => this.show(b.dataset.tab);
  }
  show(tab) {
    for (const b of document.querySelectorAll('#tabs button')) b.classList.toggle('on', b.dataset.tab === tab);
    for (const t of document.querySelectorAll('.tab')) t.classList.toggle('on', t.id === 'tab-' + tab);
    this.active = tab;
    this._resizeCharts(); this.refresh(true);
  }

  // ------------------------------------------------------------------ data in

  setHistory(h) { this.rows = h.rows.map((r) => Float32Array.from(r)); this.refresh(true); }
  addSample(t) {
    const r = Float32Array.from(t.s);
    if (this.rows.length && this.rows[this.rows.length - 1][0] >= r[0]) return;
    this.rows.push(r); if (this.rows.length > this.maxRows) this.rows.shift();
    this.refresh(false);
  }
  setFingerprints(fp) { this.fingerprints = fp; if (this.active === 'behaviour') this._renderBehaviour(); }
  setFields(f) { this.fields = f; if (this.active === 'plants') this._renderMinimap(); }

  /** Sum of a column over the last n samples. */
  recent(col, n = this.window) { let s = 0; const R = this.rows; for (let k = Math.max(0, R.length - n); k < R.length; k++) s += R[k][col]; return s; }
  last(col) { const R = this.rows; return R.length ? R[R.length - 1][col] : 0; }

  refresh(force) {
    const now = performance.now();
    if (!force && now - (this._lastRefresh || 0) < 900) return;
    this._lastRefresh = now;
    switch (this.active) {
      case 'pop': this._renderPop(); break;
      case 'energy': this._renderEnergy(); break;
      case 'web': this._renderWeb(); break;
      case 'behaviour': this._renderBehaviour(); break;
      case 'plants': this._renderPlants(); break;
      case 'species': this._renderSpecies(); break;
    }
  }

  // ------------------------------------------------------------------ charts helper

  _chart(key, host, series, opts = {}) {
    const w = Math.max(200, host.clientWidth || 400);
    const o = {
      width: w, height: opts.height || 200,
      scales: { x: { time: false }, y: opts.log ? { distr: 3 } : { auto: true } },
      axes: [{ stroke: '#6f8599', grid: { stroke: '#16212c' }, size: 30 }, { stroke: '#6f8599', grid: { stroke: '#16212c' }, size: 58 }],
      legend: { show: opts.legend !== false, live: true },
      cursor: { drag: { x: false, y: false } },
      series: [{ label: 'step' }, ...series.map((s) => ({ label: s.label, stroke: s.color, width: 1.4, fill: s.fill, show: s.show !== false, points: { show: false } }))],
    };
    if (this.charts[key]) this.charts[key].destroy();
    const data = [[], ...series.map(() => [])];
    this.charts[key] = new uPlot(o, data, host);
    this.charts[key]._host = host;
    return this.charts[key];
  }
  _resizeCharts() {
    for (const c of Object.values(this.charts)) { const w = c._host.clientWidth; if (w > 0) c.setSize({ width: w, height: c.height }); }
  }

  // ------------------------------------------------------------------ inspect

  _buildInspect() {
    const t = $('tab-inspect');
    t.innerHTML = `<div id="ins-empty" class="empty">Tap an animal in the world, or use <b>Experience</b> to pick a species.</div><div id="ins" class="hidden"></div>`;
  }

  renderProbe(pr, world) {
    const box = $('ins'), empty = $('ins-empty');
    if (!pr) { box.classList.add('hidden'); empty.classList.remove('hidden'); return; }
    empty.classList.add('hidden'); box.classList.remove('hidden');
    const M = this.meta, P = M.probe, f = pr.f, sc = (n) => f[P.scal + P.scalars.indexOf(n)];
    const sp = M.species[sc('species') | 0];
    const stage = ['dead', 'egg', 'embryo', 'juvenile', 'adult'][sc('stage') | 0];
    if (!this._insBuilt || this._insSerial !== pr.serial) {
      this._insSerial = pr.serial; this._insBuilt = true;
      box.innerHTML = `
        <h3 style="display:flex;justify-content:space-between;align-items:center"><span id="ins-title"></span><span><button id="ins-mother">mother</button> <button id="ins-child">youngest child</button> <button id="ins-clear">×</button></span></h3>
        <div class="kv" id="ins-kv"></div>
        <h3>body (evolved phenotype vs species founder)</h3><div class="kv" id="ins-body"></div>
        <h3>diet efficiencies</h3><div id="ins-diet"></div>
        <h3>senses (30 features)</h3><div id="ins-feat"></div>
        <h3>memory (12 recurrent units)</h3><div id="ins-hid"></div>
        <h3>primitive gains — what the brain asks for</h3><div id="ins-gain"></div>
        <h3>brain: output weights (28 primitives × [12 memory, 30 senses])</h3>
        <p class="note">Colour is the weight now (genome + learning). The bar to the right of each row is how much this animal has learned on that row since birth.</p>
        <canvas id="ins-w2" class="plot" height="300"></canvas>
        <h3>reward</h3><div class="kv" id="ins-reward"></div>`;
      $('ins-clear').onclick = () => this.onSelect && this.onSelect(0);
      $('ins-mother').onclick = () => this._probe && this._probe.mother && this.onSelect && this.onSelect(this._probe.mother);
      $('ins-child').onclick = () => this._probe && this._probe.child && this.onSelect && this.onSelect(this._probe.child);
    }
    this._probe = pr;
    $('ins-title').innerHTML = `<span style="color:${sp.color}">●</span> ${sp.name} #${pr.serial}`;
    const kv = [['stage', `${stage} ${sc('sex') ? '♂' : '♀'}`], ['generation', fmt(sc('generation'))], ['age', `${fmt(sc('age'))} / ${fmt(sc('life'))} (mature ${fmt(sc('mature'))})`],
      ['energy', `${fmt(sc('energy'), 2)} / ${fmt(sc('emax'), 2)}`], ['gut', fmt(sc('full') * 100) + '%'], ['thirst', fmt(sc('thirst') * 100) + '%'],
      ['fatigue', fmt(sc('fatigue') * 100) + '%'], ['pain', fmt(sc('pain') * 100) + '%'], ['children', fmt(sc('children'))], ['niche', sp.niche], ['reproduction', `${sp.mode}, ${sp.medium}`]];
    $('ins-kv').innerHTML = kv.map(([k, v]) => `<span>${k}</span><span>${v}</span>`).join('');
    const ph = (n) => f[P.phen + P.phenotype.indexOf(n)];
    const body = [['mass', ph('mass'), sp.mass], ['top speed', ph('vmax'), null], ['sight', ph('sight'), sp.sight], ['armour', ph('armour'), sp.armour], ['strike', ph('strike'), sp.strike], ['litter', ph('litter'), sp.litter]];
    $('ins-body').innerHTML = body.map(([k, v, b]) => `<span>${k}</span><span>${fmt(v, 2)}${b !== null ? ` <span style="color:${v > b * 1.02 ? '#7bd46b' : v < b * 0.98 ? '#ff8a8a' : '#56697c'}">(${fmt(b, 2)})</span>` : ''}</span>`).join('');
    $('ins-diet').innerHTML = M.pools.map((p, i) => f[P.diet + i] > 0.005 ? bar(p, f[P.diet + i], 1, '#c9a86a') : '').join('');
    $('ins-feat').innerHTML = M.features.map((n, i) => bar(n.toLowerCase(), f[P.feat + i], 1, '#5ab0ff', true)).join('');
    $('ins-hid').innerHTML = Array.from({ length: P.nh }, (_, i) => bar('m' + i, f[P.hid + i], 1, '#b48cff', true)).join('');
    $('ins-gain').innerHTML = M.primitives.map((n, i) => bar(n.toLowerCase(), f[P.gain + i], 1, i >= 20 ? '#ffb547' : '#4fd1c5')).join('');
    $('ins-reward').innerHTML = `<span>reward this step</span><span>${fmt(sc('reward'), 3)}</span><span>running mean (baseline)</span><span>${fmt(sc('rewardMean'), 3)}</span>`;
    this._drawW2(pr);
  }

  _drawW2(pr) {
    const cv = $('ins-w2'); if (!cv) return;
    const P = this.meta.probe, f = pr.f, rows = 28, cols = P.nin;
    const w = cv.clientWidth || 400, h = 300; cv.width = w * devicePixelRatio; cv.height = h * devicePixelRatio;
    const g = cv.getContext('2d'); g.scale(devicePixelRatio, devicePixelRatio);
    const lw = 88, cw = (w - lw - 40) / cols, ch = h / rows;
    g.clearRect(0, 0, w, h); g.font = '9px ui-monospace, monospace';
    let maxL = 1e-6; for (let p = 0; p < rows; p++) maxL = Math.max(maxL, f[P.learn + p]);
    for (let p = 0; p < rows; p++) {
      g.fillStyle = p >= 20 ? '#ffb547' : '#7fa7c0'; g.fillText(this.meta.primitives[p].toLowerCase(), 0, p * ch + ch * 0.8);
      for (let j = 0; j < cols; j++) {
        const v = f[P.w2 + p * cols + j];
        const a = Math.min(1, Math.abs(v) / 3);
        g.fillStyle = v >= 0 ? `rgba(79,209,197,${a})` : `rgba(255,93,115,${a})`;
        g.fillRect(lw + j * cw, p * ch, cw - 0.5, ch - 0.5);
      }
      g.fillStyle = '#e6c35a'; g.fillRect(w - 36, p * ch + 1, 34 * f[P.learn + p] / maxL, ch - 2);
    }
    g.strokeStyle = '#2a3b4e'; g.beginPath(); g.moveTo(lw + 12 * cw, 0); g.lineTo(lw + 12 * cw, h); g.stroke();
  }

  // ------------------------------------------------------------------ populations

  _buildPop() {
    const t = $('tab-pop');
    t.innerHTML = `<h3>animals alive, by species</h3>
      <div class="chips" id="pop-chips"></div>
      <label class="note"><input type="checkbox" id="pop-log" checked> log scale</label>
      <div id="pop-chart"></div>
      <h3>biomass by trophic level</h3><p class="note">Sum of adult body mass of the living animals in each group (from diet; grouping only).</p><div id="troph-chart"></div>
      <h3>predator–prey phase plot</h3>
      <div style="display:flex;gap:6px;align-items:center"><select id="ph-prey"></select> vs <select id="ph-pred"></select></div>
      <canvas id="phase" class="plot" height="230"></canvas>
      <h3>generations</h3><div id="gen-chart"></div>`;
    const chips = $('pop-chips');
    this.popShow = new Set(this.meta.species.map((s) => s.id));
    for (const s of this.meta.species) {
      const c = el('span', { className: 'chip on' }, `<i style="background:${s.color}"></i>${s.name}`);
      c.onclick = () => { if (this.popShow.has(s.id)) this.popShow.delete(s.id); else this.popShow.add(s.id); c.classList.toggle('on'); const ch = this.charts.pop; if (ch) ch.setSeries(s.id + 1, { show: this.popShow.has(s.id) }); };
      chips.append(c);
    }
    $('pop-log').onchange = (e) => { this.sel.log = e.target.checked; delete this.charts.pop; this._renderPop(); };
    for (const id of ['ph-prey', 'ph-pred']) {
      const s = $(id);
      for (const sp of this.meta.species) s.append(el('option', { value: sp.id, textContent: sp.name }));
      s.value = id === 'ph-prey' ? 0 : 12;
      s.onchange = () => { this.sel[id === 'ph-prey' ? 'prey' : 'pred'] = +s.value; this._renderPhase(); };
    }
  }

  _renderPop() {
    const M = this.meta, R = this.rows, T = M.telemetry;
    if (!this.charts.pop) this._chart('pop', $('pop-chart'), M.species.map((s) => ({ label: s.name, color: s.color, show: this.popShow.has(s.id) })), { height: 240, log: this.sel.log, legend: false });
    const xs = R.map((r) => r[0]);
    const data = [xs, ...M.species.map((s) => R.map((r) => this.sel.log ? Math.max(0.8, r[this.idx(s.id, T.fields.count)]) : r[this.idx(s.id, T.fields.count)]))];
    this.charts.pop.setData(data);
    // trophic biomass, stacked
    if (!this.charts.troph) this._chart('troph', $('troph-chart'), TROPHIC.map((n, i) => ({ label: n, color: TROPHIC_C[i], fill: TROPHIC_C[i] + '55' })), { height: 170 });
    const groups = TROPHIC.map(() => new Array(R.length).fill(0));
    for (const s of M.species) { const col = this.idx(s.id, T.fields.count); R.forEach((r, k) => { groups[s.trophic][k] += r[col] * s.mass; }); }
    const stacked = []; let acc = new Array(R.length).fill(0);
    for (let g = 0; g < groups.length; g++) { acc = acc.map((v, k) => v + groups[g][k]); stacked.push(acc.slice()); }
    this.charts.troph.setData([xs, ...stacked]);
    if (!this.charts.gen) this._chart('gen', $('gen-chart'), M.species.map((s) => ({ label: s.name, color: s.color })), { height: 150, legend: false });
    this.charts.gen.setData([xs, ...M.species.map((s) => R.map((r) => r[this.idx(s.id, T.fields.gen)]))]);
    this._renderPhase();
  }

  _renderPhase() {
    const cv = $('phase'); if (!cv || !this.rows.length) return;
    const T = this.meta.telemetry, a = this.idx(this.sel.prey, T.fields.count), b = this.idx(this.sel.pred, T.fields.count);
    const w = cv.clientWidth || 400, h = 230; cv.width = w * devicePixelRatio; cv.height = h * devicePixelRatio;
    const g = cv.getContext('2d'); g.scale(devicePixelRatio, devicePixelRatio);
    const R = this.rows; let mx = 1, my = 1;
    for (const r of R) { mx = Math.max(mx, r[a]); my = Math.max(my, r[b]); }
    g.fillStyle = '#56697c'; g.font = '10px sans-serif';
    g.fillText(this.meta.species[this.sel.prey].name + ' →', w - 110, h - 6); g.save(); g.translate(10, 60); g.rotate(-Math.PI / 2); g.fillText(this.meta.species[this.sel.pred].name + ' →', 0, 0); g.restore();
    const X = (v) => 20 + (w - 30) * v / mx, Y = (v) => h - 18 - (h - 28) * v / my;
    for (let k = 1; k < R.length; k++) {
      const t = k / R.length;
      g.strokeStyle = `hsla(${190 - 160 * t},80%,60%,${0.25 + 0.75 * t})`;
      g.beginPath(); g.moveTo(X(R[k - 1][a]), Y(R[k - 1][b])); g.lineTo(X(R[k][a]), Y(R[k][b])); g.stroke();
    }
    const l = R[R.length - 1]; g.fillStyle = '#fff'; g.beginPath(); g.arc(X(l[a]), Y(l[b]), 3, 0, 7); g.fill();
  }

  // ------------------------------------------------------------------ energy cycle (Sankey)

  _buildEnergy() {
    $('tab-energy').innerHTML = `<h3>where the energy goes — last ~1,000 steps</h3>
      <p class="note">Left: energy pools of the cycle (plants, dead matter, animal bodies). Right: the species that ate from them. Width = energy taken in.</p>
      <svg id="sankey" width="100%" height="620"></svg>
      <h3>energy budget per species (same window)</h3><table class="t" id="budget"></table>`;
  }

  _renderEnergy() {
    const M = this.meta, T = M.telemetry, svg = $('sankey');
    if (!this.rows.length || !svg) return;
    const flows = []; const poolTot = new Array(M.pools.length).fill(0), spTot = new Array(M.species.length).fill(0);
    for (const s of M.species) for (let p = 0; p < M.pools.length; p++) {
      const v = this.recent(this.idx(s.id, T.fields.intake + p));
      if (v > 0) { flows.push([p, s.id, v]); poolTot[p] += v; spTot[s.id] += v; }
    }
    const W = svg.clientWidth || 420, H = 620, nodeW = 10, gap = 4;
    const pools = M.pools.map((n, i) => i).filter((i) => poolTot[i] > 0);
    const spOrder = M.species.slice().sort((a, b) => a.trophic - b.trophic || a.id - b.id).map((s) => s.id).filter((i) => spTot[i] > 0);
    const total = flows.reduce((a, f) => a + f[2], 0) || 1;
    const scaleL = (H - gap * pools.length) / total, scaleR = (H - gap * spOrder.length) / total;
    const L = {}, Rr = {}; let y = 0;
    for (const p of pools) { L[p] = { y, h: poolTot[p] * scaleL, o: 0 }; y += L[p].h + gap; }
    y = 0; for (const s of spOrder) { Rr[s] = { y, h: spTot[s] * scaleR, o: 0 }; y += Rr[s].h + gap; }
    const x0 = 92, x1 = W - 96;
    const poolColor = ['#6fbf4a', '#4f8f3a', '#ff7aa8', '#d9b55a', '#ffe36b', '#b08a5a', '#3fb3a0', '#5fd7c5', '#8a3b3b', '#8a6a4a', '#9a8a5a', '#7a5a3a', '#4a6a7a', '#ff5d73', '#c02040'];
    let out = '';
    for (const [p, s, v] of flows.sort((a, b) => b[2] - a[2])) {
      const a = L[p], b = Rr[s], ha = v * scaleL, hb = v * scaleR;
      const ya = a.y + a.o + ha / 2, yb = b.y + b.o + hb / 2; a.o += ha; b.o += hb;
      out += `<path d="M${x0 + nodeW},${ya} C${(x0 + x1) / 2},${ya} ${(x0 + x1) / 2},${yb} ${x1},${yb}" stroke="${poolColor[p]}" stroke-opacity="0.45" stroke-width="${Math.max(0.6, (ha + hb) / 2)}" fill="none"><title>${M.pools[p]} → ${M.species[s].name}: ${fmt(v)} energy</title></path>`;
    }
    for (const p of pools) out += `<rect x="${x0}" y="${L[p].y}" width="${nodeW}" height="${Math.max(1, L[p].h)}" fill="${poolColor[p]}"/><text x="${x0 - 4}" y="${L[p].y + L[p].h / 2 + 3}" text-anchor="end" fill="#a9bccd" font-size="10">${M.pools[p]}</text>`;
    for (const s of spOrder) out += `<rect x="${x1}" y="${Rr[s].y}" width="${nodeW}" height="${Math.max(1, Rr[s].h)}" fill="${M.species[s].color}"/><text x="${x1 + nodeW + 4}" y="${Rr[s].y + Rr[s].h / 2 + 3}" fill="#a9bccd" font-size="10">${M.species[s].name}</text>`;
    svg.innerHTML = out;
    // budget table
    let rows = '<tr><th>species</th><th>intake</th><th>basal</th><th>movement</th><th>alive</th></tr>';
    for (const s of M.species) {
      const inT = spTot[s.id], b = this.recent(this.idx(s.id, T.fields.spend)), m = this.recent(this.idx(s.id, T.fields.spend + 1));
      rows += `<tr><td><span style="color:${s.color}">●</span> ${s.name}</td><td>${fmt(inT)}</td><td>${fmt(b)}</td><td>${fmt(m)}</td><td>${fmt(this.last(this.idx(s.id, T.fields.count)))}</td></tr>`;
    }
    $('budget').innerHTML = rows;
  }

  // ------------------------------------------------------------------ food web

  _buildWeb() {
    $('tab-web').innerHTML = `<h3>who killed whom — last ~1,000 steps</h3>
      <p class="note">Rows: the attacker. Columns: the victim. Nothing in the program says who may eat whom; this is what happened, from size, armour, diet and behaviour.</p>
      <canvas id="kill" class="plot" height="430"></canvas>
      <h3>how animals died (same window)</h3><canvas id="causes" class="plot" height="420"></canvas>
      <h3>all-time killer × victim totals</h3><p class="note">Since this console connected.</p><canvas id="killAll" class="plot" height="430"></canvas>`;
  }

  _renderWeb() {
    const M = this.meta, T = M.telemetry, n = M.species.length;
    const draw = (id, nsamp) => {
      const cv = $(id); if (!cv) return;
      const w = cv.clientWidth || 420, h = 430; cv.width = w * devicePixelRatio; cv.height = h * devicePixelRatio;
      const g = cv.getContext('2d'); g.scale(devicePixelRatio, devicePixelRatio);
      const lw = 74, top = 70, cs = Math.min((w - lw) / n, (h - top) / n);
      const m = []; let mx = 1;
      for (let a = 0; a < n; a++) for (let b = 0; b < n; b++) { const v = this.recent(T.offKill + a * n + b, nsamp); m.push(v); mx = Math.max(mx, v); }
      g.font = '9px sans-serif';
      for (let a = 0; a < n; a++) {
        g.fillStyle = M.species[a].color; g.fillText(M.species[a].name, 0, top + a * cs + cs * 0.75);
        g.save(); g.translate(lw + a * cs + cs * 0.7, top - 4); g.rotate(-Math.PI / 3); g.fillText(M.species[a].name, 0, 0); g.restore();
        for (let b = 0; b < n; b++) {
          const v = m[a * n + b];
          g.fillStyle = v > 0 ? `rgba(255,93,115,${0.15 + 0.85 * Math.sqrt(v / mx)})` : '#101924';
          g.fillRect(lw + b * cs, top + a * cs, cs - 1, cs - 1);
        }
      }
    };
    draw('kill', this.window); draw('killAll', this.rows.length);
    // causes of death
    const cv = $('causes'); if (!cv) return;
    const w = cv.clientWidth || 420, h = 420; cv.width = w * devicePixelRatio; cv.height = h * devicePixelRatio;
    const g = cv.getContext('2d'); g.scale(devicePixelRatio, devicePixelRatio);
    const cols = ['#e6c35a', '#5ab0ff', '#ff5d73', '#c02040', '#8aa0b5', '#ff8a2a', '#6a5a8a'];
    const lw = 74, bh = (h - 40) / n; let mx = 1; const D = [];
    for (let s = 0; s < n; s++) { const row = M.causes.map((_, c) => this.recent(this.idx(s, T.fields.deaths + c))); D.push(row); mx = Math.max(mx, row.reduce((a, b) => a + b, 0)); }
    g.font = '9px sans-serif';
    for (let s = 0; s < n; s++) {
      g.fillStyle = M.species[s].color; g.fillText(M.species[s].name, 0, s * bh + bh * 0.75);
      let x = lw;
      D[s].forEach((v, c) => { const ww = (w - lw - 6) * v / mx; g.fillStyle = cols[c]; g.fillRect(x, s * bh + 1, ww, bh - 2); x += ww; });
    }
    let x = 0; M.causes.forEach((c, i) => { g.fillStyle = cols[i]; g.fillRect(x, h - 14, 8, 8); g.fillStyle = '#8aa0b5'; g.fillText(c, x + 11, h - 7); x += g.measureText(c).width + 22; });
  }

  // ------------------------------------------------------------------ behaviour

  _buildBehaviour() {
    $('tab-behaviour').innerHTML = `<h3>primitive use by species (now)</h3>
      <p class="note">Mean gain each species' brains give each of the 28 shared primitives. Every species has the same primitives; only how they combine them differs.</p>
      <canvas id="primheat" class="plot" height="440"></canvas>
      <h3>behaviour fingerprints</h3>
      <p class="note">The live animals of each species grouped into up to 3 clusters by their gains, named after the primitives that set each cluster apart. These are measured, not designed.</p>
      <div id="fp"></div>
      <h3>learning: mean reward</h3><div id="rew-chart"></div>
      <h3>learning: how far brains moved from their genome in life</h3><div id="learn-chart"></div>`;
  }

  _renderBehaviour() {
    const M = this.meta, T = M.telemetry, n = M.species.length, np = M.primitives.length;
    const cv = $('primheat');
    if (cv && this.rows.length) {
      const w = cv.clientWidth || 420, h = 440; cv.width = w * devicePixelRatio; cv.height = h * devicePixelRatio;
      const g = cv.getContext('2d'); g.scale(devicePixelRatio, devicePixelRatio);
      const lw = 74, top = 86, cw = (w - lw) / np, ch = (h - top) / n;
      g.font = '9px sans-serif';
      for (let p = 0; p < np; p++) { g.save(); g.translate(lw + p * cw + cw * 0.7, top - 4); g.rotate(-Math.PI / 2.6); g.fillStyle = p >= 20 ? '#ffb547' : '#7fa7c0'; g.fillText(M.primitives[p].toLowerCase(), 0, 0); g.restore(); }
      for (let s = 0; s < n; s++) {
        g.fillStyle = M.species[s].color; g.fillText(M.species[s].name, 0, top + s * ch + ch * 0.75);
        for (let p = 0; p < np; p++) {
          const v = this.last(this.idx(s, T.fields.gains + p));
          g.fillStyle = `hsl(${180 - 160 * v},70%,${12 + 50 * v}%)`; g.fillRect(lw + p * cw, top + s * ch, cw - 1, ch - 1);
        }
      }
    }
    const fp = this.fingerprints, box = $('fp');
    if (fp && box) {
      box.innerHTML = fp.species.map((s) => {
        const sp = M.species[s.species];
        if (!s.clusters.length) return `<div class="fp"><b style="color:${sp.color}">${sp.name}</b> <span class="note">too few alive to cluster</span></div>`;
        return `<div class="fp"><b style="color:${sp.color}">${sp.name}</b> <span class="note">(${s.sampled} sampled)</span>` +
          s.clusters.map((c) => `<div class="row"><div class="share"><div style="width:${(c.share * 100).toFixed(0)}%;background:${sp.color}"></div></div>${(c.share * 100).toFixed(0)}% <span>${c.label.toLowerCase()}</span></div>`).join('') + '</div>';
      }).join('');
    }
    const R = this.rows, xs = R.map((r) => r[0]);
    if (!this.charts.rew) this._chart('rew', $('rew-chart'), M.species.map((s) => ({ label: s.name, color: s.color })), { height: 160, legend: false });
    this.charts.rew.setData([xs, ...M.species.map((s) => R.map((r) => r[this.idx(s.id, T.fields.reward)]))]);
    if (!this.charts.learn) this._chart('learn', $('learn-chart'), M.species.map((s) => ({ label: s.name, color: s.color })), { height: 160, legend: false });
    this.charts.learn.setData([xs, ...M.species.map((s) => R.map((r) => r[this.idx(s.id, T.fields.learned)]))]);
  }

  // ------------------------------------------------------------------ plants

  _buildPlants() {
    $('tab-plants').innerHTML = `<h3>vegetation cover (share of land)</h3><div id="veg-chart"></div>
      <h3>food and dead-matter pools (total mass)</h3><div id="pool-chart"></div>
      <h3>fire</h3><div id="fire-chart"></div>
      <h3>map</h3><select id="layer"></select><canvas id="minimap" class="plot" height="300"></canvas>`;
    const s = $('layer');
    for (const l of this.meta.fieldLayers) s.append(el('option', { value: l, textContent: l }));
    s.onchange = () => { this.sel.layer = s.value; this._renderMinimap(); };
  }

  _renderPlants() {
    const M = this.meta, G = M.telemetry.global, R = this.rows, xs = R.map((r) => r[0]), land = M.landCells;
    if (!this.charts.veg) this._chart('veg', $('veg-chart'), [{ label: 'grass', color: '#7bd46b' }, { label: 'shrub', color: '#a8b24a' }, { label: 'tree', color: '#2f8f4a' }, { label: 'sapling', color: '#5fd7a5' }], { height: 170 });
    this.charts.veg.setData([xs, R.map((r) => r[G.pools] / land), R.map((r) => r[G.shrub]), R.map((r) => r[G.tree]), R.map((r) => r[G.sapling])]);
    const pools = ['fruit', 'seed', 'nectar', 'carrion', 'dung', 'litter', 'wood', 'algae', 'phyto', 'detritus'];
    const pc = ['#ff7aa8', '#d9b55a', '#ffe36b', '#d04a4a', '#8a6a4a', '#9a8a5a', '#7a5a3a', '#3fb3a0', '#5fd7c5', '#4a8aaa'];
    if (!this.charts.pool) this._chart('pool', $('pool-chart'), pools.map((p, i) => ({ label: p, color: pc[i] })), { height: 190, log: true });
    this.charts.pool.setData([xs, ...pools.map((p) => R.map((r) => Math.max(0.01, r[G.pools + M.pools.indexOf(p)])))]);
    if (!this.charts.fire) this._chart('fire', $('fire-chart'), [{ label: 'cells ignited per sample', color: '#ff8a2a' }, { label: 'season (wet +, dry −)', color: '#5ab0ff' }], { height: 130 });
    const mxF = Math.max(1, ...R.map((r) => r[G.fire]));
    this.charts.fire.setData([xs, R.map((r) => r[G.fire] / mxF), R.map((r) => r[G.season])]);
    this._renderMinimap();
  }

  _renderMinimap() {
    const cv = $('minimap'), f = this.fields; if (!cv || !f) return;
    const d = f.d, li = this.meta.fieldLayers.indexOf(this.sel.layer), L = f.data.subarray(li * d * d, (li + 1) * d * d);
    cv.width = d; cv.height = d; cv.style.height = (cv.clientWidth || 300) + 'px';
    const g = cv.getContext('2d'), img = g.createImageData(d, d);
    for (let i = 0; i < d * d; i++) { const v = L[i]; img.data[i * 4] = v * 0.4; img.data[i * 4 + 1] = v * 0.9; img.data[i * 4 + 2] = v * 0.6 + 20; img.data[i * 4 + 3] = 255; }
    g.putImageData(img, 0, 0);
  }

  // ------------------------------------------------------------------ species dossier

  _buildSpecies() {
    $('tab-species').innerHTML = `<select id="sp-pick"></select><div id="dossier"></div>`;
    const s = $('sp-pick');
    for (const sp of this.meta.species) s.append(el('option', { value: sp.id, textContent: sp.name }));
    s.onchange = () => { this.sel.species = +s.value; this._renderSpecies(); };
  }

  _renderSpecies() {
    const M = this.meta, T = M.telemetry, s = M.species[this.sel.species], i = (f) => this.idx(s.id, f);
    const box = $('dossier'); if (!box) return;
    const modes = { Sexual: 'two sexes; needs a ready partner of the other sex', Hermaphrodite: 'any two ready adults mate; both conceive', Fission: 'the colony splits in two when it has the energy',
      Facultative: 'clones itself when food is rich; mates when food is poor', Cyclic: 'clones itself in the wet season; mates and lays resting eggs in the dry season', Spores: 'releases spores onto dead matter nearby' };
    const intake = M.pools.map((p, k) => [p, this.recent(i(T.fields.intake + k))]).filter(([, v]) => v > 0).sort((a, b) => b[1] - a[1]);
    const itot = intake.reduce((a, [, v]) => a + v, 0) || 1;
    const deaths = M.causes.map((c, k) => [c, this.recent(i(T.fields.deaths + k))]).filter(([, v]) => v > 0);
    const diet = M.pools.map((p, k) => [p, s.diet[k], s.juvDiet[k]]).filter(([, a, j]) => a > 0 || j > 0);
    box.innerHTML = `
      <h3><span style="color:${s.color}">●</span> ${s.name} — ${s.niche}</h3>
      ${this.extinct && this.extinct[s.id] >= 0 ? `<p class="note" style="color:#ff5d73">Extinct since step ${this.extinct[s.id].toLocaleString()}. Nothing in this world is spawned, so it will not return.</p>` : ''}
      <div class="kv">
        <span>alive now</span><span>${fmt(this.last(i(T.fields.count)))}</span>
        <span>juveniles / eggs+embryos</span><span>${fmt(this.last(i(T.fields.juv)))} / ${fmt(this.last(i(T.fields.eggs)))}</span>
        <span>mean generation</span><span>${fmt(this.last(i(T.fields.gen)), 1)}</span>
        <span>mean energy</span><span>${fmt(this.last(i(T.fields.energy)) * 100)}%</span>
        <span>births / conceptions (recent)</span><span>${fmt(this.recent(i(T.fields.births)))} / ${fmt(this.recent(i(T.fields.conc)))}</span>
        <span>strikes / successes (recent)</span><span>${fmt(this.recent(i(T.fields.strikes)))} / ${fmt(this.recent(i(T.fields.kills)))}</span>
      </div>
      <h3>founder body plan</h3>
      <div class="kv">
        <span>mass</span><span>${s.mass}${s.swarm ? ` (super-individual of ${s.unitMass}-mass organisms)` : ''}</span>
        <span>locomotion</span><span>${s.twoStage ? s.juvLoco + ' → ' : ''}${s.loco}</span>
        <span>armour / strike</span><span>${s.armour} / ${s.strike}</span>
        <span>sight / field of view</span><span>${fmt(s.sight, 1)} / ${s.fov}°</span>
        <span>lifespan / maturity</span><span>${fmt(s.life)} / ${fmt(s.mature)} steps</span>
        <span>reproduction</span><span>${s.mode}: ${modes[s.mode] || ''}</span>
        <span>offspring</span><span>${s.medium === 'Live' ? 'live birth' : s.medium === 'WaterEgg' ? 'eggs in water' : 'eggs on land'}, litter ${s.litter}, ${s.gestation} steps, newborn ${fmt(s.birth * 100)}% of adult mass</span>
      </div>
      <h3>can digest (efficiency; juvenile stage in brackets)</h3>${diet.map(([p, a, j]) => bar(p + (s.twoStage && j !== a ? ` (${fmt(j, 2)})` : ''), a, 1, '#c9a86a')).join('')}
      <h3>actually ate (recent energy share)</h3>${intake.length ? intake.map(([p, v]) => bar(p, v / itot, 1, '#7bd46b')).join('') : '<p class="note">nothing recently</p>'}
      <h3>where the life cycle breaks (deaths by age, recent)</h3>
      <p class="note">If most deaths are before birth or as juveniles, the species is not raising its young; mean age at death ${fmt(this.last(i(T.fields.meanAge)))} steps against maturity at ${fmt(s.mature)}.</p>
      ${(() => { const names = ['before birth', 'juvenile', 'young adult', 'older adult']; const v = names.map((_, k) => this.recent(i(T.fields.dage + k))); const mx = Math.max(1, ...v);
         return names.map((n, k) => bar(n, v[k], mx, ['#6a5a8a', '#e6c35a', '#4fd1c5', '#8aa0b5'][k], false, fmt(v[k]))).join(''); })()}
      <h3>died of (recent)</h3>${deaths.length ? deaths.map(([c, v]) => bar(c, v, Math.max(...deaths.map((d) => d[1])), '#ff5d73', false, fmt(v))).join('') : '<p class="note">no deaths recently</p>'}
      <h3>count history</h3><div id="sp-chart"></div>`;
    delete this.charts.sp;
    this._chart('sp', $('sp-chart'), [{ label: 'adults+juveniles', color: s.color }, { label: 'juveniles', color: '#8aa0b5' }, { label: 'eggs+embryos', color: '#e6c35a' }], { height: 160 });
    const R = this.rows; this.charts.sp.setData([R.map((r) => r[0]), R.map((r) => r[i(T.fields.count)]), R.map((r) => r[i(T.fields.juv)]), R.map((r) => r[i(T.fields.eggs)])]);
  }
}

/** A labelled horizontal bar. signed: value in -1..1 drawn from the centre. */
export function bar(label, v, max = 1, color = '#4fd1c5', signed = false, text) {
  const t = text ?? (Math.abs(v) < 10 ? v.toFixed(2) : fmt(v));
  if (signed) {
    const w = Math.min(50, Math.abs(v) / max * 50), left = v >= 0 ? 50 : 50 - w;
    return `<div class="bar"><span>${label}</span><div class="t" style="position:relative"><div class="f" style="position:absolute;left:${left}%;width:${w}%;background:${v >= 0 ? color : '#ff8a8a'}"></div></div><span class="n">${t}</span></div>`;
  }
  return `<div class="bar"><span>${label}</span><div class="t"><div class="f" style="width:${Math.max(0, Math.min(100, v / max * 100))}%;background:${color}"></div></div><span class="n">${t}</span></div>`;
}
