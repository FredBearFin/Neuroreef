// Spatial chunks of a map-wide geometry (2026-11-01), so what only matters close up (river ribbons, karst rocks) is
// drawn only near the camera: the map is cut into n x n squares, and each triangle goes to the square holding its
// centroid. Each chunk keeps the original index of each of its vertices (orig), so per-vertex colours painted on the
// whole map's array can be copied into it (copyColors).

import * as THREE from 'three';

/** Split g into chunks of a W x W map cut n x n. Returns [{ geo, orig, x0, z0, size }] (empty chunks left out). */
export function splitChunks(g, W, n) {
  const P = g.attributes.position.array, idx = g.index ? g.index.array : null;
  const nt = (idx ? idx.length : P.length / 3) / 3, cs = W / n;
  const vi = (t, k) => idx ? idx[t * 3 + k] : t * 3 + k;
  const bucket = new Map();
  for (let t = 0; t < nt; t++) {
    let cx = 0, cz = 0;
    for (let k = 0; k < 3; k++) { const v = vi(t, k); cx += P[v * 3]; cz += P[v * 3 + 2]; }
    const bx = ((Math.floor(cx / 3 / cs) % n) + n) % n, bz = ((Math.floor(cz / 3 / cs) % n) + n) % n, key = bz * n + bx;
    (bucket.get(key) || bucket.set(key, []).get(key)).push(t);
  }
  const out = [];
  for (const [key, tris] of bucket) {
    const map = new Map(), orig = [], I = [];
    for (const t of tris) for (let k = 0; k < 3; k++) {
      const v = vi(t, k); let j = map.get(v);
      if (j === undefined) { j = orig.length; map.set(v, j); orig.push(v); }
      I.push(j);
    }
    const c = new THREE.BufferGeometry();
    for (const [name, a] of Object.entries(g.attributes)) {
      const s = a.itemSize, src = a.array, dst = new src.constructor(orig.length * s);
      for (let j = 0; j < orig.length; j++) for (let q = 0; q < s; q++) dst[j * s + q] = src[orig[j] * s + q];
      c.setAttribute(name, new THREE.BufferAttribute(dst, s));
    }
    c.setIndex(orig.length > 65535 ? new THREE.BufferAttribute(new Uint32Array(I), 1) : I);
    c.computeBoundingSphere();
    out.push({ geo: c, orig: Int32Array.from(orig), x0: (key % n) * cs, z0: Math.floor(key / n) * cs, size: cs });
  }
  return out;
}

/** Copy per-vertex colours from the whole map's array into each chunk's. */
export function copyColors(chunks, src, size) {
  for (const ch of chunks) {
    const a = ch.geo.attributes.color, dst = a.array, o = ch.orig;
    for (let j = 0; j < o.length; j++) for (let q = 0; q < size; q++) dst[j * size + q] = src[o[j] * size + q];
    a.needsUpdate = true;
  }
}
