// The creature kit: part ids of the one parametric body (body.js, anim.js) and small geometry helpers.
//
// Every part of the body is a parametric surface: a vertex stores only its surface coordinates (s, t) and
// the vertex shader (anim.js) evaluates the surface from the organism's genes. A part carries:
//   position = (s, t, 0): s along the part (the spine for the trunk, hip to toe for a leg, root to tip for a fin),
//              t across or around it
//   aAux     = (side, index, part): side = +1 left / -1 right for paired parts; index = which of a row (leg pair,
//              eye pair, tooth, plate); part = the id below, plus FAR for the low-detail body
// Display only: nothing here affects the simulation.

import * as THREE from 'three';

/** Part ids (aAux.z). The trunk is one continuous tube from the tail tip to the nose. */
export const P = {
  TRUNK: 0, LEG: 1, EYE: 2, JAW: 3, TOOTH: 4, BONE: 5,      // closed surfaces (normals from their own shape)
  WING: 6, FIN_SIDE: 7, FIN_DORSAL: 8, TAIL_FIN: 9, EAR: 10, PLATE: 11,   // membranes (two-sided)
};
/** Added to the part id of every low-detail vertex: the shader takes cheaper normals there. */
export const FAR = 32;

/**
 * A grid surface over the values ss x ts (s rows, t columns), indexed. closed: a tube or a cap the camera sees
 * only from outside (one winding, back faces culled); a membrane gets both windings over the same vertices.
 * flip reverses the winding of a closed part (its outside depends on how the shader maps (s, t)).
 */
export function grid(ss, ts, { closed = true, flip = false } = {}) {
  const ns = ss.length, nt = ts.length, pos = [], idx = [];
  for (const s of ss) for (const t of ts) pos.push(s, t, 0);
  for (let i = 0; i < ns - 1; i++) for (let j = 0; j < nt - 1; j++) {
    const a = i * nt + j, b = (i + 1) * nt + j, c = (i + 1) * nt + j + 1, d = i * nt + j + 1;
    if (!closed || flip) idx.push(a, b, c, a, c, d);
    if (!closed || !flip) idx.push(a, c, b, a, d, c);   // a membrane: both windings
  }
  return { pos, idx };
}

/** n + 1 evenly spaced values from 0 to 1 (a seam is closed by its repeated end). */
export const steps = (n) => Array.from({ length: n + 1 }, (_, i) => i / n);

/** Merge geometries into one non-indexed geometry, with an optional flat colour per part: [[geometry, colour], ...]. */
export function merge(parts) {
  let n = 0; const ps = parts.map(([g, c]) => { const ng = g.index ? g.toNonIndexed() : g; ng.computeVertexNormals(); n += ng.attributes.position.count; return [ng, c]; });
  const pos = new Float32Array(n * 3), nor = new Float32Array(n * 3), col = new Float32Array(n * 3);
  let o = 0;
  for (const [g, c] of ps) {
    const Pa = g.attributes.position.array, N = g.attributes.normal.array;
    pos.set(Pa, o * 3); nor.set(N, o * 3);
    const cc = new THREE.Color(c ?? 0xffffff);
    for (let i = 0; i < g.attributes.position.count; i++) { col[(o + i) * 3] = cc.r; col[(o + i) * 3 + 1] = cc.g; col[(o + i) * 3 + 2] = cc.b; }
    o += g.attributes.position.count;
  }
  const out = new THREE.BufferGeometry();
  out.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  out.setAttribute('normal', new THREE.BufferAttribute(nor, 3));
  out.setAttribute('color', new THREE.BufferAttribute(col, 3));
  return out;
}

export function hash1(x) { x = Math.imul(x ^ (x >>> 16), 0x45d9f3b); x = Math.imul(x ^ (x >>> 16), 0x45d9f3b); return (x ^ (x >>> 16)) >>> 0; }
