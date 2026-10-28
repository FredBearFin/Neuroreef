// Far models: the simple, unanimated shapes drawn for animals away from the camera (one flat colour).

import * as THREE from 'three';
import { hash1 } from './kit.js';

/** Merge non-indexed geometries, with an optional flat colour per part. */
export function merge(parts) {
  let n = 0; const ps = parts.map(([g, c]) => { const ng = g.index ? g.toNonIndexed() : g; ng.computeVertexNormals(); n += ng.attributes.position.count; return [ng, c]; });
  const pos = new Float32Array(n * 3), nor = new Float32Array(n * 3), col = new Float32Array(n * 3);
  let o = 0;
  for (const [g, c] of ps) {
    const P = g.attributes.position.array, N = g.attributes.normal.array;
    pos.set(P, o * 3); nor.set(N, o * 3);
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

const E = (sx, sy, sz, x = 0, y = 0, z = 0, seg = 8) => { const g = new THREE.SphereGeometry(1, seg, Math.max(4, seg - 2)); g.scale(sx, sy, sz); g.translate(x, y, z); return g; };
const Cy = (r, h, x, y, z, rx = 0, rz = 0) => { const g = new THREE.CylinderGeometry(r, r, h, 5); g.rotateX(rx); g.rotateZ(rz); g.translate(x, y, z); return g; };
const Co = (r, h, x, y, z, rz = 0, rx = 0) => { const g = new THREE.ConeGeometry(r, h, 6); g.rotateZ(rz); g.rotateX(rx); g.translate(x, y, z); return g; };
const Bx = (sx, sy, sz, x = 0, y = 0, z = 0) => { const g = new THREE.BoxGeometry(sx, sy, sz); g.translate(x, y, z); return g; };
const legs = (len, w, x, z, r = 0.06) => [Cy(r, len, x, len / 2, z), Cy(r, len, x, len / 2, -z), Cy(r, len, -x, len / 2, z), Cy(r, len, -x, len / 2, -z)];
function swarm(n, r, size, seed, flat = 1) {
  const parts = []; let h = seed;
  for (let i = 0; i < n; i++) {
    h = hash1(h + i);
    const a = (h & 1023) / 1024 * Math.PI * 2, d = ((h >> 10) & 1023) / 1024 * r, y = (((h >> 20) & 255) / 256) * r * flat;
    parts.push(E(size, size * 0.8, size, Math.cos(a) * d, y + size, Math.sin(a) * d, 5));
  }
  return parts;
}

/** One model per species (shape id from the roster), facing +x, unit height about 1. */
export function farGeometry(shape) {
  const g = (list) => merge(list.map((x) => [x, 0xffffff]));
  switch (shape) {
    case 0: return g([E(0.9, 0.38, 0.32, 0, 0.95), E(0.26, 0.22, 0.2, 0.95, 1.35), Cy(0.08, 0.5, 0.75, 1.15, 0, 0, -0.8), ...legs(0.8, 0.06, 0.5, 0.15), Co(0.05, 0.35, 1.0, 1.65, 0.08), Co(0.05, 0.35, 1.0, 1.65, -0.08)]);   // Drifters
    case 1: return g([E(1.2, 0.65, 0.6, 0, 1.6), Cy(0.18, 1.3, 1.1, 2.3, 0, 0, -0.7), E(0.35, 0.25, 0.22, 1.6, 2.9), ...legs(1.2, 0.12, 0.7, 0.35, 0.13)]);  // Browsers
    case 2: return g([E(0.9, 0.55, 0.5, 0, 0.8), Co(0.25, 0.6, 1.0, 0.75, 0, -Math.PI / 2), Co(0.05, 0.4, 1.0, 0.65, 0.2, -1.2), Co(0.05, 0.4, 1.0, 0.65, -0.2, -1.2), ...legs(0.45, 0.1, 0.5, 0.28, 0.1)]); // Tuskers
    case 3: return g([E(0.45, 0.22, 0.22, 0, 0.3), Bx(0.4, 0.04, 2.2, -0.05, 0.38, 0), Co(0.12, 0.35, 0.55, 0.32, 0, -Math.PI / 2), Bx(0.3, 0.03, 0.6, -0.5, 0.33, 0)]);   // Gliders
    case 4: return g([E(0.5, 0.42, 0.42, 0, 0.42), E(0.24, 0.22, 0.22, 0.45, 0.55), E(0.08, 0.16, 0.06, 0.45, 0.82, 0.12), E(0.08, 0.16, 0.06, 0.45, 0.82, -0.12)]);   // Burrowers
    case 5: return g(swarm(9, 1.1, 0.16, 5));                       // Hummers
    case 6: return g(swarm(12, 1.3, 0.14, 6, 0.6));                 // Chewers
    case 7: return g(swarm(10, 1.2, 0.18, 7, 0.1));                 // Delvers
    case 8: return g([E(1.2, 0.08, 1.2, 0, 0.05), Cy(0.07, 0.4, 0.2, 0.2, 0.1), E(0.3, 0.12, 0.3, 0.2, 0.45, 0.1), Cy(0.06, 0.3, -0.4, 0.15, -0.3), E(0.22, 0.1, 0.22, -0.4, 0.33, -0.3)]); // Rotmats
    case 9: return g([E(0.8, 0.45, 0.4, 0, 0.85), E(0.3, 0.24, 0.24, 0.85, 0.75), ...legs(0.7, 0.08, 0.45, 0.2, 0.08)]);   // Gleaners
    case 10: return g([E(0.35, 0.2, 0.2, 0, 0.3), Bx(0.25, 0.03, 1.2, 0, 0.36, 0), Co(0.06, 0.25, 0.42, 0.32, 0, -Math.PI / 2)]);  // Snappers
    case 11: return g([E(0.95, 0.32, 0.3, 0, 0.6), E(0.26, 0.22, 0.22, 0.95, 0.72), Cy(0.05, 0.9, -1.2, 0.75, 0, 0, 1.1), ...legs(0.45, 0.07, 0.55, 0.18, 0.07)]);   // Prowlers
    case 12: return g([E(0.95, 0.4, 0.34, 0, 0.8), E(0.3, 0.25, 0.24, 1.0, 0.95), Co(0.1, 0.45, 1.35, 0.9, 0, -Math.PI / 2), Cy(0.07, 0.7, -1.1, 0.8, 0, 0, 1.0), ...legs(0.6, 0.08, 0.55, 0.2, 0.08)]); // Stalkers
    case 13: return g([E(1.3, 0.75, 0.7, 0, 1.3), E(0.55, 0.45, 0.42, 1.35, 1.35), Co(0.15, 0.6, 0, 2.2, 0), Co(0.15, 0.55, -0.5, 2.1, 0), Co(0.15, 0.55, 0.5, 2.1, 0), Cy(0.16, 1.2, -1.5, 1.2, 0, 0, 1.2), ...legs(0.9, 0.2, 0.75, 0.42, 0.2)]); // Leviathans
    case 14: return g(swarm(10, 1.0, 0.1, 14));                     // Bloodflies
    case 15: return g(swarm(14, 1.4, 0.16, 15, 0.15));              // Siltmites
    case 16: { const tail = Co(0.25, 0.4, -0.75, 0.0, 0, Math.PI / 2); tail.scale(1, 1, 0.25); return g([E(0.7, 0.25, 0.16, 0, 0), tail]); }   // Minnows
    case 17: { const tail = Co(0.3, 0.5, -1.35, 0.0, 0, Math.PI / 2); tail.scale(1, 1, 0.25); return g([E(1.2, 0.22, 0.18, 0, 0), tail, Co(0.1, 0.35, 1.25, 0, 0, -Math.PI / 2)]); } // Pikes
    case 18: return g([E(0.5, 0.3, 0.45, 0, 0.3), E(0.12, 0.12, 0.12, 0.3, 0.55, 0.18), E(0.12, 0.12, 0.12, 0.3, 0.55, -0.18), E(0.35, 0.08, 0.12, -0.3, 0.1, 0.4), E(0.35, 0.08, 0.12, -0.3, 0.1, -0.4)]); // Croakers
    case 19: return g([E(0.55, 0.32, 0.28, 0, 1.7), Cy(0.05, 0.9, 0.55, 2.2, 0, 0, -0.5), E(0.16, 0.13, 0.12, 0.8, 2.6), Co(0.04, 0.6, 1.15, 2.58, 0, -Math.PI / 2), Cy(0.03, 1.4, 0.05, 0.7, 0.12), Cy(0.03, 1.4, 0.05, 0.7, -0.12)]); // Herons
    default: return g([E(0.6, 0.4, 0.4, 0, 0.5)]);
  }
}
