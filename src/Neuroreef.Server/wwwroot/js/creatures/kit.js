// The creature kit: part ids of the one parametric body (body.js, anim.js) and small geometry helpers.
//
// Every part of the body is stored in its own local frame (a unit sphere, a unit cone, a flat outline);
// the vertex shader (anim.js) places and sizes each part from the organism's genes. A part carries:
//   aPart  which part it is (P below)
//   aAux   (side, along, 0): side = +1 left / -1 right for paired parts; along = where on the body the
//          part sits (front/back for legs, position along the jaw for teeth, along the back for plates)
// Display only: nothing here affects the simulation.

import * as THREE from 'three';

/** Part ids (the vertex attribute aPart). */
export const P = {
  BODY: 0, HEAD: 1, EYE: 2, SNOUT: 3, JAW: 4, TEETH_U: 5, TEETH_L: 6, LEG: 7,
  FIN_SIDE: 8, FIN_DORSAL: 9, TAIL: 10, TAIL_FIN: 11, WING: 12, PLATE: 13, SCUTE: 14, NECK: 15,
};

/** Non-indexed positions of a three.js geometry. */
export function soup(g) { return (g.index ? g.toNonIndexed() : g).attributes.position.array; }

/** An open cone without degenerate triangles: base ring (radius 1) at y = 0, tip at y = 1; seg triangles. */
export function cone(seg) {
  const out = [];
  for (let i = 0; i < seg; i++) {
    const a = (i / seg) * Math.PI * 2, b = ((i + 1) / seg) * Math.PI * 2;
    out.push(Math.cos(a), 0, Math.sin(a), 0, 1, 0, Math.cos(b), 0, Math.sin(b));
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(out, 3));
  return g;
}

/**
 * A two-sided flat polygon from a star-shaped 2D outline [[u, v], ...], fanned from centre [cu, cv].
 * plane 'xy' (vertical: dorsal and tail fins) or 'xz' (horizontal: wings, side fins; v is the z axis).
 */
export function flat(outline, plane = 'xy', centre = null, closed = false) {
  const pt = ([u, v]) => (plane === 'xy' ? [u, v, 0] : [u, 0, v]);
  const out = [];
  const n = outline.length;
  const tri = (a, b, c) => { out.push(...pt(a), ...pt(b), ...pt(c), ...pt(a), ...pt(c), ...pt(b)); };
  if (centre) { for (let i = 0; i < (closed ? n : n - 1); i++) tri(centre, outline[i], outline[(i + 1) % n]); }
  else for (let i = 1; i < n - 1; i++) tri(outline[0], outline[i], outline[i + 1]);
  return new Float32Array(out);
}

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
