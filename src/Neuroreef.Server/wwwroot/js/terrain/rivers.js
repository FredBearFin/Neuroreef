// Rivers as smooth curves (2026-11-01): their courses, the channels they cut into the drawn ground, and their water.
//
// A river path is a polyline in world units, unwrapped (it may run past the map's edge; everything that uses it wraps):
//   { x: number[], z: number[], w: number[] (water width, units), lvl: number[] (water surface, units),
//     depth: number[] (channel depth, units), flow: number[] (0..1, size class / 31), steep: number[] (0..1),
//     cell: number[] (the map cell each point colours from) }
// sampled about every STEP units. Two sources:
// - the hydrology's rivers as lines, if the WORLD frame carries them (w.riverPaths, see fromWire below): used as given;
// - otherwise the WORLD frame's river plane (one drain direction and size per cell): each run of cells between sources,
//   confluences and mouths becomes a chain through the cell centres, smoothed (the D8 staircase removed), given a gentle
//   meander on the flats (at most MEANDER x a cell off its cells, none in steep reaches or at the ends, so it stays in
//   the sim's river cells and joins its tributaries exactly), and resampled with a Catmull-Rom spline.
// Its water level falls monotonically downstream; its steepness (the fall per unit length) makes rapids and foam.

import * as THREE from 'three';
import { noise } from './noise.js';

export const STEP = 1.0;          // resampling step (units)
const MEANDER = 0.32;             // largest meander offset, in cells
const DIRX = [1, 1, 0, -1, -1, -1, 0, 1], DIRY = [0, 1, 1, 1, 0, -1, -1, -1];

/** Water width (units) of a river size class (1..31): width ~ Q^0.5 (Leopold & Maddock 1953), widened to read. */
export const riverWidth = (cls, w0, wmax) => Math.min(wmax, w0 * Math.pow(2, ((cls || 1) - 1) / 8));

/**
 * The rivers as lines, when the WORLD frame carries its RIVERS section (ICD; Hydrology.Lines; decoded by net.js as
 *   w.riverPaths = [{ x, y: Float32Array (field cells, cell (i, j) spanning [i, i+1) x [j, j+1); unwrapped along the
 *   river, head first), width: Float32Array (cells), depth: Float32Array (normalised elevation), joins (the river it
 *   joins, -1: none), order (Strahler), end (0 a river, 1 a lake, 2 the sea) }, ...]):
 * drawn as given, through a Catmull-Rom spline, each ending where its last point lies (on the river it joins, or in its
 * lake or the sea). A cell's centre (i + 0.5) is where this view draws that cell's vertex (i x S). The water level at
 * each point is the surface of the cell under it. Returns null without the section.
 */
export function fromWire(w, ctx) {
  const list = w.riverPaths;
  if (!Array.isArray(list) || !list.length) return null;
  const { S, U, SF, cellOf } = ctx, out = [];
  for (const p of list) {
    const n = Math.min(p.x?.length || 0, p.y?.length || 0); if (n < 2) continue;
    const r = { x: [], z: [], w: [], lvl: [], depth: [], flow: [], cell: [], spline: true };
    for (let k = 0; k < n; k++) {
      const x = (p.x[k] - 0.5) * S, z = (p.y[k] - 0.5) * S, i = cellOf(x, z);
      const wc = p.width ? p.width[k] : 1;
      r.x.push(x); r.z.push(z); r.cell.push(i);
      r.w.push(wc * S);
      r.lvl.push(SF[i] === SF[i] ? SF[i] : ctx.ground(x, z) + 0.02);
      r.depth.push(p.depth ? Math.min(1.5, Math.max(0.15, p.depth[k] * U)) : ctx.depthOf(i));
      r.flow.push(Math.min(1, Math.max((wc - 1) / 3, (p.order || 1) / 8)));
    }
    // a river ends where it reaches standing water (its line goes on to the lake cell's centre: not drawn over the lake)
    if (ctx.isLake) {
      const k = r.cell.findIndex((c) => ctx.isLake[c]);
      if (k >= 1) {
        for (const key of ['x', 'z', 'w', 'lvl', 'depth', 'flow', 'cell']) r[key].length = k + 1;
        // ... at the lake cell's edge, not its centre (every river into a lake ran on to that one point: a star of
        // slivers on the water, 2026-11-01 QA)
        r.x[k] = (r.x[k - 1] + r.x[k]) / 2; r.z[k] = (r.z[k - 1] + r.z[k]) / 2;
      }
      if (r.x.length < 2) continue;
    }
    r.w[0] *= 0.6;   // every river starts at its head (it ends on another, or in a lake or the sea): narrower there
    out.push(finish(resample(r)));
  }
  return out;
}

/** River paths from the per-cell plane (riv: bits 0-2 direction, 3-7 size class). */
export function fromCells(w, riv, ctx) {
  const fw = w.fw, n = fw * fw, { S, SF } = ctx;
  const down = (i) => { const k = riv[i] & 7, x = i % fw, y = (i / fw) | 0; return ((y + DIRY[k] + fw) % fw) * fw + ((x + DIRX[k] + fw) % fw); };
  const inDeg = new Uint8Array(n);
  for (let i = 0; i < n; i++) if (riv[i]) { const j = down(i); if (riv[j]) inDeg[j] = Math.min(255, inDeg[j] + 1); }
  const seen = new Uint8Array(n), paths = [];
  const trace = (s) => {
    const cells = [s];
    let x = (s % fw) * S, z = ((s / fw) | 0) * S;
    const px = [x], pz = [z];
    seen[s] = 1;
    let cur = s;
    for (let guard = 0; guard < n; guard++) {
      const k = riv[cur] & 7, j = down(cur);
      x += DIRX[k] * S; z += DIRY[k] * S;
      cells.push(j); px.push(x); pz.push(z);
      if (!riv[j] || inDeg[j] !== 1 || seen[j]) break;
      seen[j] = 1; cur = j;
    }
    return { cells, px, pz };
  };
  const chains = [];
  for (let i = 0; i < n; i++) if (riv[i] && inDeg[i] !== 1) chains.push(trace(i));
  for (let i = 0; i < n; i++) if (riv[i] && !seen[i]) chains.push(trace(i));   // loops: none expected
  // where the river spreads into a broad sheet of river cells (ctx.broad: drawn as standing water, a flat sheet at its
  // level), its chains are cut: only the runs with a narrow end become ribbons
  if (ctx.broad) {
    const B = ctx.broad, cut = [];
    for (const c of chains) {
      let run = null;
      for (let k = 0; k < c.cells.length - 1; k++) {
        if (B[c.cells[k]] && B[c.cells[k + 1]]) { if (run) { cut.push(run); run = null; } continue; }
        if (!run) run = { cells: [c.cells[k]], px: [c.px[k]], pz: [c.pz[k]] };
        run.cells.push(c.cells[k + 1]); run.px.push(c.px[k + 1]); run.pz.push(c.pz[k + 1]);
      }
      if (run) cut.push(run);
    }
    chains.length = 0; chains.push(...cut);
  }
  let seed = 0;
  for (const c of chains) {
    const m = c.cells.length; if (m < 2) continue;
    const cls = c.cells.map((i) => (riv[i] >> 3) || 0);
    for (let k = m - 1; k >= 0; k--) if (!cls[k]) cls[k] = k + 1 < m ? cls[k + 1] : cls[k - 1] || 1;   // the mouth: as the cell before it
    // water level at each cell, never rising downstream
    const lvl = c.cells.map((i, k) => SF[i] === SF[i] ? SF[i] : ctx.H[i]);
    for (let k = 1; k < m; k++) lvl[k] = Math.min(lvl[k], lvl[k - 1]);
    // smooth the staircase: the ends stay put (they are shared with the chains that meet there)
    let X = c.px.slice(), Z = c.pz.slice();
    for (let it = 0; it < 3; it++) {
      const X2 = X.slice(), Z2 = Z.slice();
      for (let k = 1; k < m - 1; k++) { X2[k] = (X[k - 1] + 2 * X[k] + X[k + 1]) / 4; Z2[k] = (Z[k - 1] + 2 * Z[k] + Z[k + 1]) / 4; }
      X = X2; Z = Z2;
    }
    // a meander on the flats: an offset across the course, smooth along it, tapering to nothing at both ends
    seed++;
    let s = 0;
    for (let k = 1; k < m - 1; k++) {
      s += Math.hypot(X[k] - X[k - 1], Z[k] - Z[k - 1]);
      const tx = X[k + 1] - X[k - 1], tz = Z[k + 1] - Z[k - 1], tl = Math.hypot(tx, tz) || 1;
      const fall = (lvl[k - 1] - lvl[k + 1]) / tl;
      const flat = 1 - Math.min(1, Math.max(0, (fall - 0.015) / 0.05));
      const taper = Math.min(1, k / 2, (m - 1 - k) / 2);
      const off = MEANDER * S * flat * taper * (noise(s / 70, (seed * 13.7) % 256 / 256) - 0.5) * 2.4;
      X[k] += -tz / tl * off; Z[k] += tx / tl * off;
    }
    const r = { x: X, z: Z, w: cls.map((q) => ctx.widthOfClass(q)), lvl, depth: c.cells.map((i) => ctx.depthOf(i)),
                flow: cls.map((q) => Math.min(1, q / 31)), cell: c.cells, spline: true };
    r.w[0] *= 0.6;   // a source starts narrow
    paths.push(finish(resample(r)));
  }
  return paths;
}

/** A raw path (points every few units, rivers.js format without steep) made ready: straight resampling, level, steepness. */
export const preparePath = (r) => finish(resample(r));

/** Resample a path every STEP units (Catmull-Rom through the points if r.spline, else straight). */
function resample(r) {
  const n = r.x.length, o = { x: [], z: [], w: [], lvl: [], depth: [], flow: [], cell: [] };
  const P = (k) => Math.max(0, Math.min(n - 1, k));
  for (let k = 0; k < n - 1; k++) {
    const len = Math.hypot(r.x[k + 1] - r.x[k], r.z[k + 1] - r.z[k]), m = Math.max(1, Math.ceil(len / STEP));
    for (let j = 0; j < m; j++) {
      const t = j / m;
      let x, z;
      if (r.spline) {
        const a = P(k - 1), b = k, c = k + 1, d = P(k + 2), t2 = t * t, t3 = t2 * t;
        const cr = (p0, p1, p2, p3) => 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
        x = cr(r.x[a], r.x[b], r.x[c], r.x[d]); z = cr(r.z[a], r.z[b], r.z[c], r.z[d]);
      } else { x = r.x[k] + (r.x[k + 1] - r.x[k]) * t; z = r.z[k] + (r.z[k + 1] - r.z[k]) * t; }
      o.x.push(x); o.z.push(z);
      for (const key of ['w', 'lvl', 'depth', 'flow']) o[key].push(r[key][k] + (r[key][k + 1] - r[key][k]) * t);
      o.cell.push(t < 0.5 ? r.cell[k] : r.cell[k + 1]);
    }
  }
  o.x.push(r.x[n - 1]); o.z.push(r.z[n - 1]);
  for (const key of ['w', 'lvl', 'depth', 'flow']) o[key].push(r[key][n - 1]);
  o.cell.push(r.cell[n - 1]);
  return o;
}

/** Smooth the level (still monotone) and measure the steepness: fall per unit length, 0 below 5 %, 1 above 25 %. */
function finish(o) {
  const n = o.x.length, L = o.lvl.slice();
  for (let k = 0; k < n; k++) {
    let s = 0, c = 0;
    for (let j = Math.max(0, k - 4); j <= Math.min(n - 1, k + 4); j++) { s += L[j]; c++; }
    o.lvl[k] = s / c;
  }
  for (let k = 1; k < n; k++) o.lvl[k] = Math.min(o.lvl[k], o.lvl[k - 1]);
  o.steep = [];
  for (let k = 0; k < n; k++) {
    const a = Math.max(0, k - 3), b = Math.min(n - 1, k + 3);
    let len = 0; for (let j = a; j < b; j++) len += Math.hypot(o.x[j + 1] - o.x[j], o.z[j + 1] - o.z[j]);
    const fall = len > 0 ? (o.lvl[a] - o.lvl[b]) / len : 0;
    const t = Math.min(1, Math.max(0, (fall - 0.05) / 0.2));
    o.steep.push(t * t * (3 - 2 * t));
  }
  return o;
}

/**
 * Cut the channels into the carve texture (the ground's height cap: a parabolic bed `depth` below the water at the
 * centre line, the water level at the bank, rising BANK per unit beyond), and fill the channel texture:
 * r = nearness to the centre line within the water (1 at the centre, 0 at the bank), g = flow, b = steepness,
 * a = the alluvial flat beside it (fading over 2 widths + 6 units, none in steep reaches).
 */
export function stampRivers(paths, carve, chan, fw, CR, S) {
  const N = fw * CR, T = S / CR, BANK = 0.8;
  for (const p of paths) for (let k = 0; k < p.x.length; k++) {
    const x = p.x[k], z = p.z[k], hw = Math.max(0.7, p.w[k] / 2), L = p.lvl[k], dep = p.depth[k];
    const band = (2 * hw + 6) * (1 - p.steep[k]), R = Math.max(hw + 2.5, hw + band);
    const i0 = Math.floor((x - R) / T), i1 = Math.ceil((x + R) / T), j0 = Math.floor((z - R) / T), j1 = Math.ceil((z + R) / T);
    const fl = Math.round(p.flow[k] * 255), st = Math.round(p.steep[k] * 255);
    for (let j = j0; j <= j1; j++) for (let i = i0; i <= i1; i++) {
      const d = Math.hypot(i * T - x, j * T - z); if (d > R) continue;
      const t = (((j % N) + N) % N) * N + (((i % N) + N) % N);
      if (d <= hw + 2.5) {
        const cap = d < hw ? L - dep * (1 - (d / hw) ** 2) : L + BANK * (d - hw);
        if (cap < carve[t]) carve[t] = cap;
      }
      const prox = Math.max(0, 1 - d / hw), q = t * 4;
      if (prox > 0 && prox * 255 > chan[q]) { chan[q] = Math.round(prox * 255); chan[q + 1] = fl; chan[q + 2] = st; }
      if (band > 0) { const a = Math.round(Math.min(1, Math.max(0, 1 - (d - hw) / band)) * 255); if (a > chan[q + 3]) chan[q + 3] = a; }
    }
  }
}

/**
 * The ribbons: a strip along each path, 1.25 x the water's width (its edges run into the banks and fade), at the
 * water level. Attributes: aRiv = (distance along, side -1..1, steepness), and the cell per vertex (for colour).
 */
export function ribbonGeometry(paths) {
  const P = [], A = [], C = [], I = [];
  for (const p of paths) {
    const n = p.x.length; if (n < 2) continue;
    let s = 0, prev = -1;
    for (let k = 0; k < n; k++) {
      // the direction across, from the course over about a half-width each way: a bend tighter than the strip's
      // half-width otherwise folds its inner edge over itself (a fan of slivers, 2026-11-01 QA)
      const win = Math.max(1, Math.min(8, Math.round(p.w[k] * 0.5 / STEP)));
      const a = Math.max(0, k - win), b = Math.min(n - 1, k + win);
      let tx = p.x[b] - p.x[a], tz = p.z[b] - p.z[a]; const tl = Math.hypot(tx, tz) || 1; tx /= tl; tz /= tl;
      if (k > 0) s += Math.hypot(p.x[k] - p.x[k - 1], p.z[k] - p.z[k - 1]);
      const hw = Math.max(0.45, p.w[k] * 0.625), y = p.lvl[k] + 0.03, v = P.length / 3;
      P.push(p.x[k] - tz * hw, y, p.z[k] + tx * hw, p.x[k] + tz * hw, y, p.z[k] - tx * hw);
      A.push(s, -1, p.steep[k], s, 1, p.steep[k]);
      C.push(p.cell[k], p.cell[k]);
      if (prev >= 0) I.push(prev, v, prev + 1, prev + 1, v, v + 1);
      prev = v;
    }
  }
  // every triangle faces up
  for (let t = 0; t < I.length; t += 3) {
    const a = I[t] * 3, b = I[t + 1] * 3, c = I[t + 2] * 3;
    if ((P[b + 2] - P[a + 2]) * (P[c] - P[a]) - (P[b] - P[a]) * (P[c + 2] - P[a + 2]) < 0) { const q = I[t + 1]; I[t + 1] = I[t + 2]; I[t + 2] = q; }
  }
  const g = new THREE.BufferGeometry(), nv = P.length / 3, nor = new Float32Array(nv * 3);
  for (let v = 0; v < nv; v++) nor[v * 3 + 1] = 1;
  g.setAttribute('position', new THREE.BufferAttribute(new Float32Array(P), 3));
  g.setAttribute('normal', new THREE.BufferAttribute(nor, 3));
  g.setAttribute('color', new THREE.BufferAttribute(new Float32Array(nv * 3), 3));
  g.setAttribute('aRiv', new THREE.BufferAttribute(new Float32Array(A), 3));
  g.setIndex(nv > 65535 ? new THREE.BufferAttribute(new Uint32Array(I), 1) : I);
  g.userData.cells = Int32Array.from(C);
  return g;
}

/** The ribbon material's shader additions: flowing streaks, foam on rapids, soft edges. */
export function patchRiverMaterial(m, U) {
  m.onBeforeCompile = (sh) => {
    Object.assign(sh.uniforms, U);
    sh.vertexShader = 'attribute vec3 aRiv;\nvarying vec3 vRiv;\nvarying vec2 vRivXZ;\n' + sh.vertexShader.replace('#include <begin_vertex>', '#include <begin_vertex>\nvRiv = aRiv; vRivXZ = position.xz;');
    sh.fragmentShader = 'uniform sampler2D tNoise;\nuniform float uTime;\nvarying vec3 vRiv;\nvarying vec2 vRivXZ;\n' + sh.fragmentShader.replace('#include <color_fragment>', `#include <color_fragment>
      {
        float st = vRiv.z, across = abs(vRiv.y);
        float speed = 0.35 + 1.6 * st;
        float a1 = texture(tNoise, vec2(vRiv.x * 0.045 - uTime * speed * 0.05, vRiv.y * 0.11 + 0.3)).r;
        float a2 = texture(tNoise, vec2(vRiv.x * 0.11 - uTime * speed * 0.11, vRiv.y * 0.23 + 0.7)).r;
        float streak = a1 * 0.6 + a2 * 0.4;
        diffuseColor.rgb *= 0.9 + 0.22 * streak;
        // white water (2026-11-01 QA): broken patches where the bed steps down (rapids, step pools), never a solid sheet:
        // even the steepest reach is mostly dark, fast water between them (a solid white strip read as a road). A finer
        // noise breaks each patch up; with distance the patches average to a pale tint and fade before they alias.
        float a3 = texture(tNoise, vec2(vRiv.x * 0.37 - uTime * speed * 0.3, vRiv.y * 0.61 + 0.1)).r;
        float rap = smoothstep(0.62, 0.80, a1 * 0.7 + a3 * 0.3 + 0.12 * st);
        float foam = rap * smoothstep(0.45, 0.85, a3 + 0.1 * st) * smoothstep(0.3, 0.9, st);
        foam = max(foam, smoothstep(0.8, 1.0, across) * smoothstep(0.5, 0.95, st) * smoothstep(0.55, 0.8, a2) * 0.6);
        float px = fwidth(vRiv.x);
        foam *= 1.0 - smoothstep(0.15, 0.5, px);
        diffuseColor.rgb = mix(diffuseColor.rgb, vec3(0.80, 0.86, 0.88), foam * 0.75 + 0.08 * st * smoothstep(0.15, 0.5, px));
        // the water's edge is at across = 0.8 (the strip is 1.25 x its width), where the carved bank rises through the
        // strip: faded out by then, so the depth test's cut (along two different tessellations) never shows as a comb
        diffuseColor.a = (1.0 - smoothstep(0.5, 0.8, across)) * mix(0.82, 0.95, foam);
      }`);
  };
  m.customProgramCacheKey = () => 'nr-river-1';
}
