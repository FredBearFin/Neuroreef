// Dashboards and the inspector. Everything shown here is read from the simulation's telemetry, probe
// and fingerprint frames; nothing is inferred by the console.
//
// The clades are not designed: they appear by speciation and vanish by extinction during a run, so every
// view here works from the live clade table (setClades) and skips unused slots. A slot can be reused by a
// new clade long after its old occupant died out; telemetry samples older than a clade's `born` step belong
// to that old occupant and are ignored for the new one.

import uPlot from '../lib/uPlot.esm.js';

const $ = (id) => document.getElementById(id);
const el = (tag, attrs = {}, html = '') => { const e = document.createElement(tag); Object.assign(e, attrs); if (html) e.innerHTML = html; return e; };
const fmt = (v, d = 0) => (v === undefined || v === null || isNaN(v)) ? '–' : Number(v).toLocaleString(undefined, { maximumFractionDigits: d, minimumFractionDigits: d });
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const TROPHIC = ['decomposers & scavengers', 'plant-eaters', 'omnivores', 'meat-eaters', 'large meat-eaters'];
const TROPHIC_C = ['#a08a6a', '#7bd46b', '#e6c35a', '#ff5d8f', '#a77bff'];
const TRAIT_C = ['#7bd46b', '#c9a86a', '#5ab0ff', '#b48cff', '#4fd1c5', '#e6c35a', '#8aa0b5', '#ff5d73', '#ff8fd0', '#ffb547', '#c02040', '#a77bff', '#5fd7a5', '#d7e3ee', '#3fb3a0', '#ff9a3d'];
const GREY = '#56697c';
// charts with one series per clade: rebuilt when the set of clades changes (uPlot series counts are fixed)
const PER_CLADE = ['pop', 'gen', 'rew', 'learn', 'evoAcross', 'evoKit', 'evoGen'];

export class Panels {
  constructor(meta, link) {
    this.meta = meta; this.link = link;
    this.rows = [];               // telemetry samples (Float32Array), oldest first
    this.maxRows = 3000;
    this.charts = {};
    this.window = 32;             // samples summed for "recent" views (~1,000 steps)
    this.sel = { prey: -1, pred: -1, species: -1, layer: 'grass', log: true, evoSp: -1, evoTrait: 0 };
    this.fingerprints = null;
    this.extinct = [];
    this.step = 0;
    this.archive = new Map();     // every clade seen on this simulation, by serial (keeps extinct clades whose slot was reused)
    this.popShow = new Set(); this.popHide = new Set();
    const T = meta.telemetry;
    this.NS = meta.species.length;
    this.SC = T.global.step;
    this.idx = (s, f) => T.offSpecies + s * T.perSpecies + f;
    this.clades = meta.species;
    this._sig = '';
    this._tabs();
    this._buildInspect(); this._buildPop(); this._buildEnergy(); this._buildWeb(); this._buildBehaviour(); this._buildPlants(); this._buildSpecies(); this._buildEvolution();
    this.setClades(meta.species);
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

  // ------------------------------------------------------------------ the clade table

  /** The live clade table (32 slots; unused ones have used = false). Called on every 'clades' frame. */
  setClades(list, step) {
    if (!list) return;
    this.clades = list;
    if (step > this.step) this.step = step;
    for (const c of list) {
      if (!c.used) continue;
      let a = this.archive.get(c.serial);
      if (!a) {
        // resolve the parent slot to the clade that held it when this one split off (the slot may be reused later)
        const p = c.parent >= 0 ? list[c.parent] : null;
        let ps = 0;
        if (p && p.used && p.born <= c.born) ps = p.serial;
        else if (c.parent >= 0) for (const o of this.archive.values()) if (o.slot === c.parent && o.born <= c.born && (o.extinct < 0 || o.extinct >= c.born)) ps = o.serial;
        a = { serial: c.serial, slot: c.id, parentSerial: ps };
        this.archive.set(c.serial, a);
      }
      Object.assign(a, { name: c.name, color: c.color, ancestor: c.ancestor, born: c.born, extinct: c.alive ? -1 : c.extinct, count: c.count,
        niche: c.niche, generation: c.generation, alive: c.alive });
    }
    for (const a of this.archive.values()) a.current = list[a.slot] && list[a.slot].serial === a.serial;
    const sig = list.map((c) => c.used ? `${c.serial}:${c.color}:${c.name}:${c.alive}` : '').join(',');
    if (sig !== this._sig) {
      this._sig = sig;
      for (const k of PER_CLADE) if (this.charts[k]) { this.charts[k].destroy(); delete this.charts[k]; }
      this._fillSelects();
    }
    this.refresh(true);
  }

  /** Forget everything about the previous simulation (a branch, or the world after the prehistory). */
  reset() {
    this.rows = []; this.archive.clear(); this.step = 0; this.fingerprints = null; this._sig = '';
    this.setClades(this.clades);
  }

  setStep(step) { if (step > this.step) this.step = step; }

  used() { return this.clades.filter((c) => c.used); }
  alive() { return this.clades.filter((c) => c.used && c.alive); }
  name(s) { const c = this.clades[s]; return c && c.used ? c.name : 'slot ' + s; }
  color(s) { const c = this.clades[s]; return c && c.used ? c.color : GREY; }
  born(s) { const c = this.clades[s]; return c && c.used ? c.born : Infinity; }
  // the clade table is resent the moment a clade dies out, so it is the authority (the status frame's
  // extinct[] can briefly describe the previous simulation after a branch switch)
  isExtinct(c) { return !c.alive; }
  extinctAt(c) { return c.extinct; }
  /** Value of a per-clade column in a row, or null for a sample from before the clade existed. */
  cv(r, s, f) { return r[this.SC] >= this.born(s) ? r[this.idx(s, f)] : null; }
  /** Recent sum of a per-clade column, ignoring samples from a previous occupant of the slot. */
  recentC(s, f, n = this.window) {
    let t = 0; const R = this.rows, col = this.idx(s, f), b = this.born(s);
    for (let k = Math.max(0, R.length - n); k < R.length; k++) if (R[k][this.SC] >= b) t += R[k][col];
    return t;
  }

  _fillSelects() {
    const used = this.used(), alive = used.filter((c) => !this.isExtinct(c));
    const opt = (c) => el('option', { value: c.id, textContent: c.name + (this.isExtinct(c) ? ' (extinct)' : '') });
    const fill = (id, list, key) => {
      const s = $(id); if (!s) return;
      s.innerHTML = '';
      for (const c of list) s.append(opt(c));
      if (!list.some((c) => c.id === this.sel[key])) this.sel[key] = list.length ? list[0].id : -1;
      s.value = this.sel[key];
    };
    const order = alive.concat(used.filter((c) => this.isExtinct(c)));
    fill('sp-pick', order, 'species');
    fill('evo-sp', order, 'evoSp');
    if (!used.some((c) => c.id === this.sel.pred) || this.sel.pred === this.sel.prey) this.sel.pred = -1;
    fill('ph-prey', order, 'prey');
    if (this.sel.pred < 0) { const o = order.find((c) => c.id !== this.sel.prey); this.sel.pred = o ? o.id : this.sel.prey; }
    fill('ph-pred', order, 'pred');
    // population chips
    const chips = $('pop-chips');
    if (chips) {
      chips.innerHTML = '';
      for (const c of used) {
        const on = !this.popHide.has(c.serial);
        const ch = el('span', { className: 'chip' + (on ? ' on' : '') + (this.isExtinct(c) ? ' ext' : ''), title: c.niche }, `<i style="background:${c.color}"></i>${esc(c.name)}`);
        ch.onclick = () => {
          if (this.popHide.has(c.serial)) this.popHide.delete(c.serial); else this.popHide.add(c.serial);
          ch.classList.toggle('on');
          const chart = this.charts.pop, k = used.indexOf(c);
          if (chart && k >= 0) chart.setSeries(k + 1, { show: !this.popHide.has(c.serial) });
        };
        chips.append(ch);
      }
    }
  }

  // ------------------------------------------------------------------ data in

  setHistory(h) { this.rows = h.rows.map((r) => Float32Array.from(r)); this.refresh(true); }
  addSample(t) {
    const r = Float32Array.from(t.s);
    if (this.rows.length && this.rows[this.rows.length - 1][0] >= r[0]) return;
    this.rows.push(r); if (this.rows.length > this.maxRows) this.rows.shift();
    this.setStep(r[this.SC]);
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
    try {
      switch (this.active) {
        case 'pop': this._renderPop(); break;
        case 'energy': this._renderEnergy(); break;
        case 'web': this._renderWeb(); break;
        case 'behaviour': this._renderBehaviour(); break;
        case 'plants': this._renderPlants(); break;
        case 'species': this._renderSpecies(); break;
        case 'evolution': this._renderEvolution(); break;
      }
    } catch (e) { console.error('panel', this.active, e); }
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
  /** A chart with one series per used clade, values of field f (null before the clade was born). */
  _cladeChart(key, host, f, opts = {}) {
    const used = this.used(), R = this.rows, xs = R.map((r) => r[this.SC]);
    if (!this.charts[key]) this._chart(key, host, used.map((c) => ({ label: c.name, color: c.color, show: opts.show ? opts.show(c) : true })), { legend: false, ...opts });
    const map = opts.map || ((v) => v);
    this.charts[key].setData([xs, ...used.map((c) => R.map((r) => { const v = this.cv(r, c.id, f); return v === null ? null : (opts.skipEmpty && !(r[this.idx(c.id, this.meta.telemetry.fields.count)] > 0)) ? null : map(v); }))]);
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
    const sp = this.clades[sc('species') | 0] || { name: '?', color: GREY, niche: '' };
    const stage = ['dead', 'egg', 'embryo', 'juvenile', 'adult'][sc('stage') | 0];
    if (!this._insBuilt || this._insSerial !== pr.serial) {
      this._insSerial = pr.serial; this._insBuilt = true;
      box.innerHTML = `
        <h3 style="display:flex;justify-content:space-between;align-items:center"><span id="ins-title"></span><span><button id="ins-mother">mother</button> <button id="ins-child">youngest child</button> <button id="ins-clear">×</button></span></h3>
        <div class="kv" id="ins-kv"></div>
        <h3>body (this animal; its clade's mean in brackets)</h3><div class="kv" id="ins-body"></div>
        <h3>diet efficiencies</h3><div id="ins-diet"></div>
        <h3>senses (${M.features.length} features)</h3><div id="ins-feat"></div>
        <h3>memory (12 recurrent units)</h3><div id="ins-hid"></div>
        <h3>primitive gains — what the brain asks for</h3><div id="ins-gain"></div>
        <h3>brain: output weights (primitives × [memory units, senses])</h3>
        <p class="note">Colour is the weight now (genome + learning). The bar to the right of each row is how much this animal has learned on that row since birth.</p>
        <canvas id="ins-w2" class="plot" height="300"></canvas>
        <h3>reward</h3><div class="kv" id="ins-reward"></div>`;
      $('ins-clear').onclick = () => this.onSelect && this.onSelect(0);
      $('ins-mother').onclick = () => this._probe && this._probe.mother && this.onSelect && this.onSelect(this._probe.mother);
      $('ins-child').onclick = () => this._probe && this._probe.child && this.onSelect && this.onSelect(this._probe.child);
    }
    this._probe = pr;
    $('ins-title').innerHTML = `<span style="color:${sp.color}">●</span> ${esc(sp.name)} #${pr.serial}`;
    const kv = [['stage', `${stage} ${sc('sex') ? '♂' : '♀'}`], ['generation', fmt(sc('generation'))], ['age', `${fmt(sc('age'))} / ${fmt(sc('life'))} (mature ${fmt(sc('mature'))})`],
      ['energy', `${fmt(sc('energy'), 2)} / ${fmt(sc('emax'), 2)}`], ['gut', fmt(sc('full') * 100) + '%'], ['thirst', fmt(sc('thirst') * 100) + '%'],
      ['fatigue', fmt(sc('fatigue') * 100) + '%'], ['pain', fmt(sc('pain') * 100) + '%'], ['children', fmt(sc('children'))], ['clade niche', esc(sp.niche)],
      ['clade reproduction', `${fmt((sp.sexual || 0) * 100)}% sexual · ${fmt((sp.live || 0) * 100)}% live birth`]];
    $('ins-kv').innerHTML = kv.map(([k, v]) => `<span>${k}</span><span>${v}</span>`).join('');
    const ph = (n) => { const k = P.phenotype.indexOf(n); return k >= 0 ? f[P.phen + k] : NaN; };
    const body = [['mass', ph('mass'), sp.mass], ['top speed', ph('vmax'), null], ['sight', ph('sight'), sp.sight], ['armour', ph('armour'), null], ['strike', ph('strike'), null], ['litter', ph('litter'), null]];
    $('ins-body').innerHTML = body.map(([k, v, b]) => `<span>${k}</span><span>${fmt(v, 2)}${b != null && isFinite(b) ? ` <span style="color:${GREY}">(${fmt(b, 2)})</span>` : ''}</span>`).join('');
    $('ins-diet').innerHTML = M.pools.map((p, i) => f[P.diet + i] > 0.005 ? bar(p, f[P.diet + i], 1, '#c9a86a') : '').join('');
    $('ins-feat').innerHTML = M.features.map((n, i) => bar(n.toLowerCase(), f[P.feat + i], 1, '#5ab0ff', true)).join('');
    $('ins-hid').innerHTML = Array.from({ length: P.nh }, (_, i) => bar('m' + i, f[P.hid + i], 1, '#b48cff', true)).join('');
    $('ins-gain').innerHTML = M.primitives.map((n, i) => bar(n.toLowerCase(), f[P.gain + i], 1, i >= M.firstAction ? '#ffb547' : '#4fd1c5')).join('');
    $('ins-reward').innerHTML = `<span>reward this step</span><span>${fmt(sc('reward'), 3)}</span><span>running mean (baseline)</span><span>${fmt(sc('rewardMean'), 3)}</span>`;
    this._drawW2(pr);
  }

  _drawW2(pr) {
    const cv = $('ins-w2'); if (!cv) return;
    const P = this.meta.probe, f = pr.f, rows = this.meta.primitives.length, cols = P.nin;
    const w = cv.clientWidth || 400, h = 300; cv.width = w * devicePixelRatio; cv.height = h * devicePixelRatio;
    const g = cv.getContext('2d'); g.scale(devicePixelRatio, devicePixelRatio);
    const lw = 88, cw = (w - lw - 40) / cols, ch = h / rows;
    g.clearRect(0, 0, w, h); g.font = '9px ui-monospace, monospace';
    let maxL = 1e-6; for (let p = 0; p < rows; p++) maxL = Math.max(maxL, f[P.learn + p]);
    for (let p = 0; p < rows; p++) {
      g.fillStyle = p >= this.meta.firstAction ? '#ffb547' : '#7fa7c0'; g.fillText(this.meta.primitives[p].toLowerCase(), 0, p * ch + ch * 0.8);
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
    t.innerHTML = `<h3>animals alive, by clade</h3>
      <div class="chips" id="pop-chips"></div>
      <label class="note"><input type="checkbox" id="pop-log" checked> log scale</label>
      <div id="pop-chart"></div>
      <h3>biomass by trophic level</h3><p class="note">Sum of adult body mass of the living animals in each group (each clade grouped by its members' current diet; grouping only).</p><div id="troph-chart"></div>
      <h3>predator–prey phase plot</h3>
      <div style="display:flex;gap:6px;align-items:center"><select id="ph-prey"></select> vs <select id="ph-pred"></select></div>
      <canvas id="phase" class="plot" height="230"></canvas>
      <h3>generations</h3><div id="gen-chart"></div>`;
    $('pop-log').onchange = (e) => { this.sel.log = e.target.checked; if (this.charts.pop) { this.charts.pop.destroy(); delete this.charts.pop; } this._renderPop(); };
    for (const id of ['ph-prey', 'ph-pred']) {
      const s = $(id);
      s.onchange = () => { this.sel[id === 'ph-prey' ? 'prey' : 'pred'] = +s.value; this._renderPhase(); };
    }
  }

  _renderPop() {
    const M = this.meta, R = this.rows, T = M.telemetry;
    const log = this.sel.log;
    this._cladeChart('pop', $('pop-chart'), T.fields.count, { height: 240, log, show: (c) => !this.popHide.has(c.serial), map: (v) => log ? (v > 0 ? v : null) : v });
    // trophic biomass, stacked
    const xs = R.map((r) => r[this.SC]);
    if (!this.charts.troph) this._chart('troph', $('troph-chart'), TROPHIC.map((n, i) => ({ label: n, color: TROPHIC_C[i], fill: TROPHIC_C[i] + '55' })), { height: 170 });
    const groups = TROPHIC.map(() => new Array(R.length).fill(0));
    for (const s of this.used()) {
      const g = groups[Math.max(0, Math.min(4, s.trophic | 0))], m = s.mass || 0;
      R.forEach((r, k) => { const v = this.cv(r, s.id, T.fields.count); if (v) g[k] += v * m; });
    }
    const stacked = []; let acc = new Array(R.length).fill(0);
    for (let g = 0; g < groups.length; g++) { acc = acc.map((v, k) => v + groups[g][k]); stacked.push(acc.slice()); }
    this.charts.troph.setData([xs, ...stacked]);
    this._cladeChart('gen', $('gen-chart'), T.fields.gen, { height: 150, skipEmpty: true });
    this._renderPhase();
  }

  _renderPhase() {
    const cv = $('phase'); if (!cv || !this.rows.length || this.sel.prey < 0 || this.sel.pred < 0) return;
    const T = this.meta.telemetry, f = T.fields.count, pa = this.sel.prey, pb = this.sel.pred;
    const w = cv.clientWidth || 400, h = 230; cv.width = w * devicePixelRatio; cv.height = h * devicePixelRatio;
    const g = cv.getContext('2d'); g.scale(devicePixelRatio, devicePixelRatio);
    const b0 = Math.max(this.born(pa), this.born(pb));
    const R = this.rows.filter((r) => r[this.SC] >= b0);
    const a = this.idx(pa, f), b = this.idx(pb, f);
    let mx = 1, my = 1;
    for (const r of R) { mx = Math.max(mx, r[a]); my = Math.max(my, r[b]); }
    g.fillStyle = GREY; g.font = '10px sans-serif';
    g.fillText(this.name(pa) + ' →', w - 110, h - 6); g.save(); g.translate(10, 60); g.rotate(-Math.PI / 2); g.fillText(this.name(pb) + ' →', 0, 0); g.restore();
    if (!R.length) return;
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
      <p class="note">Left: energy pools of the cycle (plants, dead matter, animal bodies). Right: the clades that ate from them. Width = energy taken in.</p>
      <svg id="sankey" width="100%" height="620"></svg>
      <h3>energy budget per clade (same window)</h3><table class="t" id="budget"></table>`;
  }

  _renderEnergy() {
    const M = this.meta, T = M.telemetry, svg = $('sankey');
    if (!this.rows.length || !svg) return;
    const used = this.used();
    const flows = []; const poolTot = new Array(M.pools.length).fill(0), spTot = new Array(this.NS).fill(0);
    for (const s of used) for (let p = 0; p < M.pools.length; p++) {
      const v = this.recentC(s.id, T.fields.intake + p);
      if (v > 0) { flows.push([p, s.id, v]); poolTot[p] += v; spTot[s.id] += v; }
    }
    const W = svg.clientWidth || 420, H = 620, nodeW = 10, gap = 4;
    const pools = M.pools.map((n, i) => i).filter((i) => poolTot[i] > 0);
    const spOrder = used.slice().sort((a, b) => a.trophic - b.trophic || a.id - b.id).map((s) => s.id).filter((i) => spTot[i] > 0);
    const total = flows.reduce((a, f) => a + f[2], 0) || 1;
    const scaleL = (H - gap * pools.length) / total, scaleR = (H - gap * spOrder.length) / total;
    const L = {}, Rr = {}; let y = 0;
    for (const p of pools) { L[p] = { y, h: poolTot[p] * scaleL, o: 0 }; y += L[p].h + gap; }
    y = 0; for (const s of spOrder) { Rr[s] = { y, h: spTot[s] * scaleR, o: 0 }; y += Rr[s].h + gap; }
    const x0 = 92, x1 = W - 96;
    const poolColor = ['#6fbf4a', '#4f8f3a', '#ff7aa8', '#d9b55a', '#ffe36b', '#b08a5a', '#3fb3a0', '#5fd7c5', '#8a3b3b', '#8a6a4a', '#9a8a5a', '#7a5a3a', '#4a6a7a', '#ff5d73', '#c02040'];
    const pc = (p) => poolColor[p % poolColor.length];
    let out = '';
    for (const [p, s, v] of flows.sort((a, b) => b[2] - a[2])) {
      const a = L[p], b = Rr[s], ha = v * scaleL, hb = v * scaleR;
      const ya = a.y + a.o + ha / 2, yb = b.y + b.o + hb / 2; a.o += ha; b.o += hb;
      out += `<path d="M${x0 + nodeW},${ya} C${(x0 + x1) / 2},${ya} ${(x0 + x1) / 2},${yb} ${x1},${yb}" stroke="${pc(p)}" stroke-opacity="0.45" stroke-width="${Math.max(0.6, (ha + hb) / 2)}" fill="none"><title>${M.pools[p]} → ${esc(this.name(s))}: ${fmt(v)} energy</title></path>`;
    }
    for (const p of pools) out += `<rect x="${x0}" y="${L[p].y}" width="${nodeW}" height="${Math.max(1, L[p].h)}" fill="${pc(p)}"/><text x="${x0 - 4}" y="${L[p].y + L[p].h / 2 + 3}" text-anchor="end" fill="#a9bccd" font-size="10">${M.pools[p]}</text>`;
    for (const s of spOrder) out += `<rect x="${x1}" y="${Rr[s].y}" width="${nodeW}" height="${Math.max(1, Rr[s].h)}" fill="${this.color(s)}"/><text x="${x1 + nodeW + 4}" y="${Rr[s].y + Rr[s].h / 2 + 3}" fill="#a9bccd" font-size="10">${esc(this.name(s))}</text>`;
    svg.innerHTML = out || `<text x="10" y="20" fill="${GREY}" font-size="11">nothing eaten in the last ~1,000 steps</text>`;
    // budget table: living clades, and any that ate in the window
    let rows = '<tr><th>clade</th><th>intake</th><th>basal</th><th>movement</th><th>alive</th></tr>';
    for (const s of used) {
      if (this.isExtinct(s) && !(spTot[s.id] > 0)) continue;
      const inT = spTot[s.id], b = this.recentC(s.id, T.fields.spend), m = this.recentC(s.id, T.fields.spend + 1);
      rows += `<tr><td><span style="color:${s.color}">●</span> ${esc(s.name)}</td><td>${fmt(inT)}</td><td>${fmt(b)}</td><td>${fmt(m)}</td><td>${fmt(this.cv(this.rows[this.rows.length - 1], s.id, T.fields.count))}</td></tr>`;
    }
    $('budget').innerHTML = rows;
  }

  // ------------------------------------------------------------------ food web

  _buildWeb() {
    $('tab-web').innerHTML = `<h3>who killed whom — last ~1,000 steps</h3>
      <p class="note">Rows: the attacker. Columns: the victim. Nothing in the program says who may eat whom; this is what happened, from size, armour, diet and behaviour. Living clades, and extinct ones that appear in the window.</p>
      <canvas id="kill" class="plot" height="430"></canvas>
      <h3>how animals died (same window)</h3><canvas id="causes" class="plot" height="420"></canvas>
      <h3>all-time killer × victim totals</h3><p class="note">Since this console connected.</p><canvas id="killAll" class="plot" height="430"></canvas>`;
  }

  _renderWeb() {
    const M = this.meta, T = M.telemetry, NS = this.NS, R = this.rows;
    const used = this.used();
    // a kill cell counts only samples after both clades existed (a reused slot has an older history)
    const kills = (a, b, nsamp) => {
      const b0 = Math.max(this.born(a), this.born(b)), col = T.offKill + a * NS + b;
      let t = 0; for (let k = Math.max(0, R.length - nsamp); k < R.length; k++) if (R[k][this.SC] >= b0) t += R[k][col];
      return t;
    };
    const draw = (id, nsamp) => {
      const cv = $(id); if (!cv) return;
      const m = new Map(); let mx = 1; const involved = new Set();
      for (const a of used) for (const b of used) { const v = kills(a.id, b.id, nsamp); if (v > 0) { m.set(a.id * NS + b.id, v); mx = Math.max(mx, v); involved.add(a.id); involved.add(b.id); } }
      const list = used.filter((c) => !this.isExtinct(c) || involved.has(c.id)), n = Math.max(1, list.length);
      const w = cv.clientWidth || 420, h = 430; cv.width = w * devicePixelRatio; cv.height = h * devicePixelRatio;
      const g = cv.getContext('2d'); g.scale(devicePixelRatio, devicePixelRatio);
      const lw = 74, top = 70, cs = Math.min(28, (w - lw) / n, (h - top) / n);
      g.font = '9px sans-serif';
      list.forEach((a, ia) => {
        g.fillStyle = this.isExtinct(a) ? GREY : a.color; g.fillText(a.name, 0, top + ia * cs + cs * 0.75);
        g.save(); g.translate(lw + ia * cs + cs * 0.7, top - 4); g.rotate(-Math.PI / 3); g.fillText(a.name, 0, 0); g.restore();
        list.forEach((b, ib) => {
          const v = m.get(a.id * NS + b.id) || 0;
          g.fillStyle = v > 0 ? `rgba(255,93,115,${0.15 + 0.85 * Math.sqrt(v / mx)})` : '#101924';
          g.fillRect(lw + ib * cs, top + ia * cs, cs - 1, cs - 1);
        });
      });
    };
    draw('kill', this.window); draw('killAll', R.length);
    // causes of death
    const cv = $('causes'); if (!cv) return;
    const D = [], list = []; let mx = 1;
    for (const s of used) {
      const row = M.causes.map((_, c) => this.recentC(s.id, T.fields.deaths + c)), tot = row.reduce((a, b) => a + b, 0);
      if (this.isExtinct(s) && !tot) continue;
      list.push(s); D.push(row); mx = Math.max(mx, tot);
    }
    const n = Math.max(1, list.length), h = Math.max(160, Math.min(420, 40 + n * 22));
    const w = cv.clientWidth || 420; cv.style.height = h + 'px'; cv.width = w * devicePixelRatio; cv.height = h * devicePixelRatio;
    const g = cv.getContext('2d'); g.scale(devicePixelRatio, devicePixelRatio);
    const cols = ['#e6c35a', '#5ab0ff', '#ff5d73', '#c02040', '#8aa0b5', '#ff8a2a', '#6a5a8a', '#7bd46b', '#4fd1c5', '#ff8fd0'];
    const lw = 74, bh = (h - 44) / n;
    g.font = '9px sans-serif';
    list.forEach((s, k) => {
      g.fillStyle = this.isExtinct(s) ? GREY : s.color; g.fillText(s.name, 0, k * bh + bh * 0.6 + 3);
      let x = lw;
      D[k].forEach((v, c) => { const ww = (w - lw - 6) * v / mx; g.fillStyle = cols[c % cols.length]; g.fillRect(x, k * bh + 1, ww, bh - 2); x += ww; });
    });
    let x = 0, yl = h - 20; M.causes.forEach((c, i) => {
      const tw = g.measureText(c).width + 22; if (x + tw > w) { x = 0; yl += 13; }
      g.fillStyle = cols[i % cols.length]; g.fillRect(x, yl - 7, 8, 8); g.fillStyle = '#8aa0b5'; g.fillText(c, x + 11, yl); x += tw;
    });
  }

  // ------------------------------------------------------------------ behaviour

  _buildBehaviour() {
    $('tab-behaviour').innerHTML = `<h3>primitive use by clade (now)</h3>
      <p class="note">Mean gain each living clade's brains give each of the shared primitives. Every clade has the same primitives; only how they combine them differs.</p>
      <canvas id="primheat" class="plot" height="440"></canvas>
      <h3>behaviour fingerprints</h3>
      <p class="note">The live animals of each clade grouped into up to 3 clusters by their gains, named after the primitives that set each cluster apart. These are measured, not designed.</p>
      <div id="fp"></div>
      <h3>learning: mean reward</h3><div id="rew-chart"></div>
      <h3>learning: how far brains moved from their genome in life</h3><div id="learn-chart"></div>`;
  }

  _renderBehaviour() {
    const M = this.meta, T = M.telemetry, np = M.primitives.length;
    const list = this.alive(), n = Math.max(1, list.length);
    const cv = $('primheat');
    if (cv && this.rows.length) {
      const h = Math.max(160, Math.min(440, 90 + n * 18));
      const w = cv.clientWidth || 420; cv.style.height = h + 'px'; cv.width = w * devicePixelRatio; cv.height = h * devicePixelRatio;
      const g = cv.getContext('2d'); g.scale(devicePixelRatio, devicePixelRatio);
      const lw = 74, top = 86, cw = (w - lw) / np, ch = (h - top) / n;
      g.font = '9px sans-serif';
      for (let p = 0; p < np; p++) { g.save(); g.translate(lw + p * cw + cw * 0.7, top - 4); g.rotate(-Math.PI / 2.6); g.fillStyle = p >= M.firstAction ? '#ffb547' : '#7fa7c0'; g.fillText(M.primitives[p].toLowerCase(), 0, 0); g.restore(); }
      list.forEach((s, k) => {
        g.fillStyle = s.color; g.fillText(s.name, 0, top + k * ch + ch * 0.6 + 3);
        for (let p = 0; p < np; p++) {
          const v = this.last(this.idx(s.id, T.fields.gains + p)) || 0;
          g.fillStyle = `hsl(${180 - 160 * v},70%,${12 + 50 * v}%)`; g.fillRect(lw + p * cw, top + k * ch, cw - 1, ch - 1);
        }
      });
    }
    const fp = this.fingerprints, box = $('fp');
    if (fp && box) {
      box.innerHTML = fp.species.filter((s) => this.clades[s.species] && this.clades[s.species].used).map((s) => {
        const sp = this.clades[s.species];
        if (!s.clusters.length) return `<div class="fp"><b style="color:${sp.color}">${esc(sp.name)}</b> <span class="note">too few alive to cluster</span></div>`;
        return `<div class="fp"><b style="color:${sp.color}">${esc(sp.name)}</b> <span class="note">(${s.sampled} sampled)</span>` +
          s.clusters.map((c) => `<div class="row"><div class="share"><div style="width:${(c.share * 100).toFixed(0)}%;background:${sp.color}"></div></div>${(c.share * 100).toFixed(0)}% <span>${esc(c.label.toLowerCase())}</span></div>`).join('') + '</div>';
      }).join('');
    }
    this._cladeChart('rew', $('rew-chart'), T.fields.reward, { height: 160, skipEmpty: true });
    this._cladeChart('learn', $('learn-chart'), T.fields.learned, { height: 160, skipEmpty: true });
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

  // ------------------------------------------------------------------ evolution: what the genes are doing (observed only)

  _buildEvolution() {
    const M = this.meta;
    $('tab-evolution').innerHTML = `
      <h3>Tree of life <span class="dim">· tap a clade to chart its traits</span></h3>
      <p class="note">Every clade from the ancestors to now. A band runs from the step a clade split off to its extinction (or now);
        its thickness follows the clade's numbers. A thin line joins each daughter to its parent at the split.</p>
      <canvas id="tree" class="plot tree"></canvas>
      <div id="tree-tip" class="note tree-tip">&nbsp;</div>
      <p class="note">What evolution is doing, measured from the living animals' genes. Nothing here steers it:
        a trait rises only if animals carrying more of it leave more offspring.</p>
      <h3>Traits of <select id="evo-sp"></select></h3><div id="evo-traits"></div>
      <h3>One trait across clades: <select id="evo-trait"></select></h3><div id="evo-across"></div>
      <h3>Starter-kit instinct strength</h3><div id="evo-kit"></div>
      <h3>Generations reached</h3><div id="evo-gen"></div>`;
    const s = $('evo-sp'), t = $('evo-trait');
    M.traitNames.forEach((n, i) => t.append(el('option', { value: i, textContent: n })));
    s.onchange = () => this.selectEvo(+s.value);
    t.onchange = () => { this.sel.evoTrait = +t.value; if (this.charts.evoAcross) { this.charts.evoAcross.destroy(); delete this.charts.evoAcross; } this._renderEvolution(); };
    const cv = $('tree');
    cv.addEventListener('mousemove', (e) => { const n = this._treeHit(e); $('tree-tip').innerHTML = n ? this._treeTip(n) : '&nbsp;'; cv.style.cursor = n && n.current ? 'pointer' : 'default'; });
    cv.addEventListener('mouseleave', () => { $('tree-tip').innerHTML = '&nbsp;'; });
    cv.addEventListener('click', (e) => { const n = this._treeHit(e); if (n) { $('tree-tip').innerHTML = this._treeTip(n); if (n.current) this.selectEvo(n.slot); } });
  }

  selectEvo(slot) {
    this.sel.evoSp = slot;
    const s = $('evo-sp'); if (s) s.value = slot;
    if (this.charts.evoTraits) { this.charts.evoTraits.destroy(); delete this.charts.evoTraits; }
    this._renderEvolution();
  }

  _renderEvolution() {
    const M = this.meta, R = this.rows, T = M.telemetry, sp = this.sel.evoSp;
    this._drawTree();
    const xs = R.map((r) => r[this.SC]);
    const ok = (r, s) => r[this.SC] >= this.born(s) && r[this.idx(s, T.fields.count)] > 0;   // no animals: no value
    if (!this.charts.evoTraits) this._chart('evoTraits', $('evo-traits'), M.traitNames.map((n, i) => ({ label: n, color: TRAIT_C[i % TRAIT_C.length] })), { height: 220 });
    this.charts.evoTraits.setData([xs, ...M.traitNames.map((_, i) => R.map((r) => sp >= 0 && ok(r, sp) ? r[this.idx(sp, T.fields.traits + i)] : null))]);
    this._cladeChart('evoAcross', $('evo-across'), T.fields.traits + this.sel.evoTrait, { height: 180, skipEmpty: true });
    this._cladeChart('evoKit', $('evo-kit'), T.fields.kit, { height: 150, skipEmpty: true });
    this._cladeChart('evoGen', $('evo-gen'), T.fields.gen, { height: 150, skipEmpty: true });
  }

  // ------------------------------------------------------------------ tree of life

  /** Clades in drawing order: ancestors stacked, each followed (depth first) by its daughters, oldest first. */
  _treeOrder() {
    const nodes = [...this.archive.values()], by = new Map(nodes.map((n) => [n.serial, n])), kids = new Map(), roots = [];
    for (const n of nodes) {
      const p = n.parentSerial ? by.get(n.parentSerial) : null;
      if (p) { if (!kids.has(p.serial)) kids.set(p.serial, []); kids.get(p.serial).push(n); } else roots.push(n);
    }
    roots.sort((a, b) => a.ancestor - b.ancestor || a.born - b.born || a.serial - b.serial);
    const out = [], visit = (n, d) => { n.depth = d; out.push(n); for (const k of (kids.get(n.serial) || []).sort((a, b) => a.born - b.born || a.serial - b.serial)) visit(k, d + 1); };
    for (const r of roots) visit(r, 0);
    return out;
  }

  _drawTree() {
    const cv = $('tree'); if (!cv) return;
    const w = cv.clientWidth; if (!w) return;
    const order = this._treeOrder(), n = order.length;
    const lane = n <= 12 ? 20 : n <= 20 ? 16 : n <= 32 ? 13 : 11, top = 6, axisH = 18;
    const h = Math.max(80, top + n * lane + axisH);
    cv.style.height = h + 'px'; cv.width = w * devicePixelRatio; cv.height = h * devicePixelRatio;
    const g = cv.getContext('2d'); g.scale(devicePixelRatio, devicePixelRatio);
    g.clearRect(0, 0, w, h);
    this._tree = { order, lane, top, n };
    if (!n) { g.fillStyle = GREY; g.font = '11px sans-serif'; g.fillText('waiting for the clade table…', 10, 30); return; }
    const R = this.rows, T = this.meta.telemetry, now = Math.max(this.step, R.length ? R[R.length - 1][this.SC] : 0, ...order.map((o) => Math.max(o.born, o.extinct)), 1);
    const LW = 92, x0 = LW, x1 = w - 10, X = (s) => x0 + (x1 - x0) * Math.max(0, Math.min(1, s / now));
    const yc = (k) => top + k * lane + lane / 2;
    // count series of each node (only samples inside its own lifetime: a reused slot has an older history)
    let cMax = 1;
    const series = order.map((o) => {
      const end = o.extinct >= 0 ? o.extinct : Infinity, col = this.idx(o.slot, T.fields.count), pts = [];
      for (const r of R) { const s = r[this.SC]; if (s >= o.born && s <= end) { pts.push([s, r[col]]); if (r[col] > cMax) cMax = r[col]; } }
      if (!pts.length && o.count > cMax) cMax = o.count;
      return pts;
    });
    const maxT = lane - 3, thick = (c) => c > 0 ? Math.max(1.4, maxT * Math.sqrt(c / cMax)) : 0.8;
    const pos = new Map(order.map((o, k) => [o.serial, k]));
    // axis grid
    g.font = '9px sans-serif'; g.textBaseline = 'alphabetic';
    const ticks = niceTicks(now, Math.max(2, Math.floor((x1 - x0) / 70)));
    for (const t of ticks) {
      const x = X(t); g.strokeStyle = '#16212c'; g.beginPath(); g.moveTo(x, top); g.lineTo(x, h - axisH); g.stroke();
      g.fillStyle = '#6f8599'; g.textAlign = t === 0 ? 'left' : 'center'; g.fillText(shortStep(t), x, h - 5);
    }
    g.textAlign = 'right'; g.fillText('step', x0 - 4, h - 5); g.textAlign = 'left';
    // selection highlight
    order.forEach((o, k) => {
      if (o.current && o.slot === this.sel.evoSp) { g.fillStyle = 'rgba(79,209,197,0.10)'; g.fillRect(0, top + k * lane, w, lane); }
    });
    // connectors daughter → parent at the split step
    order.forEach((o, k) => {
      const pk = o.parentSerial ? pos.get(o.parentSerial) : undefined;
      if (pk === undefined) return;
      const x = X(o.born);
      g.strokeStyle = o.color; g.globalAlpha = 0.7; g.lineWidth = 1;
      g.beginPath(); g.moveTo(x, yc(pk)); g.lineTo(x, yc(k)); g.stroke();
      g.globalAlpha = 1; g.fillStyle = o.color; g.beginPath(); g.arc(x, yc(pk), 2, 0, 7); g.fill();
    });
    // bands
    order.forEach((o, k) => {
      const y = yc(k), pts = series[k], end = o.extinct >= 0 ? o.extinct : now, dead = o.extinct >= 0;
      g.fillStyle = o.color; g.strokeStyle = o.color; g.globalAlpha = dead ? 0.45 : 0.9;
      if (pts.length) {
        // before the console's first sample of it: a thin line
        if (pts[0][0] > o.born) { g.lineWidth = 1.2; g.beginPath(); g.moveTo(X(o.born), y); g.lineTo(X(pts[0][0]), y); g.stroke(); }
        g.beginPath();
        g.moveTo(X(pts[0][0]), y - thick(pts[0][1]) / 2);
        for (const [s, c] of pts) g.lineTo(X(s), y - thick(c) / 2);
        if (!dead) g.lineTo(X(end), y - thick(o.count) / 2);
        const lastX = dead ? X(pts[pts.length - 1][0]) : X(end);
        g.lineTo(lastX, y + (dead ? thick(pts[pts.length - 1][1]) : thick(o.count)) / 2);
        for (let i = pts.length - 1; i >= 0; i--) g.lineTo(X(pts[i][0]), y + thick(pts[i][1]) / 2);
        g.closePath(); g.fill();
        if (dead && X(end) > lastX) { g.lineWidth = 1; g.beginPath(); g.moveTo(lastX, y); g.lineTo(X(end), y); g.stroke(); }
      } else {
        const t = thick(dead ? 0 : o.count);
        g.fillRect(X(o.born), y - t / 2, Math.max(1, X(end) - X(o.born)), t);
      }
      g.globalAlpha = 1;
      if (dead) { g.strokeStyle = '#ff5d73'; g.lineWidth = 1.2; g.beginPath(); g.moveTo(X(end), y - 4); g.lineTo(X(end), y + 4); g.stroke(); }
      // label
      const sel = o.current && o.slot === this.sel.evoSp;
      g.font = `${sel ? 'bold ' : ''}${lane >= 16 ? 10 : 9}px sans-serif`;
      g.fillStyle = dead ? GREY : o.color;
      const ind = Math.min(o.depth || 0, 6) * 5;
      let label = (dead ? '† ' : '') + o.name;
      while (label.length > 2 && g.measureText(label).width > LW - 8 - ind) label = label.slice(0, -1);
      g.fillText(label, 2 + ind, y + 3);
    });
    g.lineWidth = 1;
  }

  _treeHit(e) {
    const t = this._tree; if (!t || !t.n) return null;
    const r = e.target.getBoundingClientRect(), k = Math.floor((e.clientY - r.top - t.top) / t.lane);
    return k >= 0 && k < t.n ? t.order[k] : null;
  }

  _treeTip(o) {
    const p = o.parentSerial ? [...this.archive.values()].find((a) => a.serial === o.parentSerial) : null;
    return `<b style="color:${o.color}">${esc(o.name)}</b> · ${o.extinct >= 0 ? `extinct at step ${fmt(o.extinct)}` : `${esc(o.niche || '')} · ${fmt(o.count)} alive`}` +
      ` · ${p ? `split from <b style="color:${p.color}">${esc(p.name)}</b> at step ${fmt(o.born)} (${o.generation} split${o.generation === 1 ? '' : 's'} from its ancestor)` : `ancestor ${o.ancestor + 1}`}` +
      (o.current ? '' : ' · <i>slot since reused</i>');
  }

  // ------------------------------------------------------------------ species dossier

  _buildSpecies() {
    $('tab-species').innerHTML = `<select id="sp-pick"></select><div id="dossier"></div>`;
    const s = $('sp-pick');
    s.onchange = () => { this.sel.species = +s.value; this._renderSpecies(); };
  }

  _renderSpecies() {
    const M = this.meta, T = M.telemetry, box = $('dossier'); if (!box) return;
    const s = this.clades[this.sel.species];
    if (!s || !s.used) { box.innerHTML = '<div class="empty">No clades yet.</div>'; return; }
    const i = (f) => this.idx(s.id, f), lastR = this.rows[this.rows.length - 1];
    const L = (f) => lastR ? this.cv(lastR, s.id, f) : null, Rc = (f) => this.recentC(s.id, f);
    const intake = M.pools.map((p, k) => [p, Rc(T.fields.intake + k)]).filter(([, v]) => v > 0).sort((a, b) => b[1] - a[1]);
    const itot = intake.reduce((a, [, v]) => a + v, 0) || 1;
    const deaths = M.causes.map((c, k) => [c, Rc(T.fields.deaths + k)]).filter(([, v]) => v > 0);
    const diet = (s.diet || []).map((v, k) => [M.pools[k], v]).filter(([, v]) => v > 0.01).sort((a, b) => b[1] - a[1]);
    const a = this.archive.get(s.serial), par = a && a.parentSerial ? [...this.archive.values()].find((o) => o.serial === a.parentSerial) : null;
    const daughters = [...this.archive.values()].filter((o) => a && o.parentSerial === a.serial);
    const ext = this.isExtinct(s), at = this.extinctAt(s);
    box.innerHTML = `
      <h3><span style="color:${s.color}">●</span> ${esc(s.name)} — ${esc(s.niche)}</h3>
      ${ext ? `<p class="note" style="color:#ff5d73">Extinct since step ${fmt(at)}. Nothing in this world is spawned, so it will not return.</p>` : ''}
      <div class="kv">
        <span>origin</span><span>${par ? `split from <b style="color:${par.color}">${esc(par.name)}</b>` : s.parent >= 0 ? 'split from a clade no longer on record' : `ancestor ${s.ancestor + 1}`} at step ${fmt(s.born)}</span>
        <span>splits since the ancestor</span><span>${fmt(s.generation)}</span>
        <span>daughter clades</span><span>${daughters.length ? daughters.map((d) => `<b style="color:${d.extinct >= 0 ? GREY : d.color}">${esc(d.name)}</b>`).join(', ') : 'none yet'}</span>
        <span>alive now</span><span>${fmt(L(T.fields.count) ?? s.count)}</span>
        <span>juveniles / eggs+embryos</span><span>${fmt(L(T.fields.juv))} / ${fmt(L(T.fields.eggs))}</span>
        <span>mean generation (births)</span><span>${fmt(L(T.fields.gen), 1)}</span>
        <span>mean energy</span><span>${L(T.fields.energy) == null ? '–' : fmt(L(T.fields.energy) * 100) + '%'}</span>
        <span>births / conceptions (recent)</span><span>${fmt(Rc(T.fields.births))} / ${fmt(Rc(T.fields.conc))}</span>
        <span>strikes / successes (recent)</span><span>${fmt(Rc(T.fields.strikes))} / ${fmt(Rc(T.fields.kills))}</span>
      </div>
      <h3>what its members are now <span class="dim">· means over the living animals' genes</span></h3>
      ${ext ? '<p class="note">No living members to describe.</p>' : `<div class="kv">
        <span>niche</span><span>${esc(s.niche)}</span>
        <span>mass</span><span>${fmt(s.mass, 2)}</span>
        <span>locomotion (most members)</span><span>${esc(String(s.loco).toLowerCase())}</span>
        <span>sight / field of view</span><span>${fmt(s.sight, 1)} / ${fmt(s.fov)}°</span>
        <span>newborn size</span><span>${fmt(s.birth * 100)}% of adult mass</span>
        <span>sexual reproduction</span><span>${fmt(s.sexual * 100)}% of members (others clone)</span>
        <span>live birth</span><span>${fmt(s.live * 100)}% of members (others lay eggs)</span>
      </div>`}
      ${ext ? '' : `<h3>can digest (mean efficiency)</h3>${diet.length ? diet.map(([p, v]) => bar(p, v, 1, '#c9a86a')).join('') : '<p class="note">nothing</p>'}`}
      <h3>actually ate (recent energy share)</h3>${intake.length ? intake.map(([p, v]) => bar(p, v / itot, 1, '#7bd46b')).join('') : '<p class="note">nothing recently</p>'}
      <h3>where the life cycle breaks (deaths by age, recent)</h3>
      <p class="note">If most deaths are before birth or as juveniles, the clade is not raising its young; mean age at death ${fmt(L(T.fields.meanAge))} steps.</p>
      ${(() => { const names = ['before birth', 'juvenile', 'young adult', 'older adult']; const v = names.map((_, k) => Rc(T.fields.dage + k)); const mx = Math.max(1, ...v);
         return names.map((n, k) => bar(n, v[k], mx, ['#6a5a8a', '#e6c35a', '#4fd1c5', '#8aa0b5'][k], false, fmt(v[k]))).join(''); })()}
      <h3>died of (recent)</h3>${deaths.length ? deaths.map(([c, v]) => bar(c, v, Math.max(...deaths.map((d) => d[1])), '#ff5d73', false, fmt(v))).join('') : '<p class="note">no deaths recently</p>'}
      <h3>count history</h3><div id="sp-chart"></div>`;
    if (this.charts.sp) { this.charts.sp.destroy(); delete this.charts.sp; }
    this._chart('sp', $('sp-chart'), [{ label: 'adults+juveniles', color: s.color }, { label: 'juveniles', color: '#8aa0b5' }, { label: 'eggs+embryos', color: '#e6c35a' }], { height: 160 });
    const R = this.rows.filter((r) => r[this.SC] >= s.born);
    this.charts.sp.setData([R.map((r) => r[this.SC]), R.map((r) => r[i(T.fields.count)]), R.map((r) => r[i(T.fields.juv)]), R.map((r) => r[i(T.fields.eggs)])]);
  }
}

function niceTicks(max, n) {
  const raw = max / n, p = Math.pow(10, Math.floor(Math.log10(raw))), m = raw / p;
  const step = (m < 1.5 ? 1 : m < 3.5 ? 2 : m < 7.5 ? 5 : 10) * p, out = [];
  for (let t = 0; t <= max + 1e-9; t += step) out.push(t);
  return out;
}
function shortStep(v) { return v >= 1e6 ? (v / 1e6).toFixed(v % 1e6 ? 1 : 0) + 'M' : v >= 1e3 ? (v / 1e3).toFixed(v % 1e3 ? 1 : 0) + 'k' : String(v); }

/** A labelled horizontal bar. signed: value in -1..1 drawn from the centre. */
export function bar(label, v, max = 1, color = '#4fd1c5', signed = false, text) {
  const t = text ?? (Math.abs(v) < 10 ? v.toFixed(2) : fmt(v));
  if (signed) {
    const w = Math.min(50, Math.abs(v) / max * 50), left = v >= 0 ? 50 : 50 - w;
    return `<div class="bar"><span>${label}</span><div class="t" style="position:relative"><div class="f" style="position:absolute;left:${left}%;width:${w}%;background:${v >= 0 ? color : '#ff8a8a'}"></div></div><span class="n">${t}</span></div>`;
  }
  return `<div class="bar"><span>${label}</span><div class="t"><div class="f" style="width:${Math.max(0, Math.min(100, v / max * 100))}%;background:${color}"></div></div><span class="n">${t}</span></div>`;
}
