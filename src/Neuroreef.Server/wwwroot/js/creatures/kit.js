// The creature kit: geometry helpers and the contract every species model follows.
//
// A model is a list of parts made with part(geometry, { p, pivot, role }). Each part carries:
//   p      which part it is (P below): the shader moves legs, wings, tail, head, jaw and swarm members
//   pivot  the joint it turns about (the hip, the shoulder, the neck, the tail root), in model units
//   role   its colour role (R below): colours are derived from the species colour, so a model never
//          hard-codes a colour
// Models face +x, stand on y = 0 (swimmers are centred on y = 0), and are about 1 unit tall for a
// typical species; the view scales them by body mass. Display only: nothing here affects the simulation.

import * as THREE from 'three';

/** Part ids (the vertex attribute aPart). */
export const P = { BODY: 0, HEAD: 1, LEG_FL: 2, LEG_FR: 3, LEG_BL: 4, LEG_BR: 5, WING_L: 6, WING_R: 7, TAIL: 8, JAW: 9, MEMBER: 10 };

/** Colour roles. BODY is countershaded: the species colour on top, paler underneath. */
export const R = { BODY: 0, BACK: 1, BELLY: 2, ACCENT: 3, DARK: 4, EYE: 5, HORN: 6, PALE: 7 };

export function part(geometry, { p = P.BODY, pivot = [0, 0, 0], role = R.BODY } = {}) {
  return { g: geometry, p, pivot, role };
}

// ------------------------------------------------------------------ primitives (all return BufferGeometry)

/** Ellipsoid with radii sx, sy, sz at (x, y, z). */
export function ellipsoid(sx, sy, sz, x = 0, y = 0, z = 0, seg = 10) {
  const g = new THREE.SphereGeometry(1, seg, Math.max(4, Math.round(seg * 0.7)));
  g.scale(sx, sy, sz); g.translate(x, y, z); return g;
}

/** Box with sizes sx, sy, sz centred at (x, y, z). */
export function box(sx, sy, sz, x = 0, y = 0, z = 0) { const g = new THREE.BoxGeometry(sx, sy, sz); g.translate(x, y, z); return g; }

/** A tapered limb (or neck, horn, beak) from point a to point b, radius r0 at a and r1 at b. */
export function limb(a, b, r0, r1 = r0, seg = 6) {
  const A = new THREE.Vector3(...a), B = new THREE.Vector3(...b), d = B.clone().sub(A), len = d.length();
  const g = new THREE.CylinderGeometry(r1, r0, len, seg, 1);
  g.translate(0, len / 2, 0);
  g.applyQuaternion(new THREE.Quaternion().setFromUnitVectors(new THREE.Vector3(0, 1, 0), d.normalize()));
  g.translate(A.x, A.y, A.z); return g;
}

/** A cone from base point a (radius r) to tip b: horns, beaks, claws, ears. */
export function cone(a, b, r, seg = 6) { return limb(a, b, r, 0.0001, seg); }

/**
 * A smooth body along the x axis through stations [x, y, halfHeight, halfWidth]; the ends close to a
 * point. Stations may run either way along x. Use for torsos, fish, necks with heads. seg = vertices around each ring.
 */
export function spine(stations, seg = 10) {
  const pos = [], idx = [];
  const n = stations.length;
  for (const [x, y, hh, hw] of stations) {
    for (let k = 0; k < seg; k++) {
      const a = (k / seg) * Math.PI * 2;
      pos.push(x, y + Math.cos(a) * hh, Math.sin(a) * hw);
    }
  }
  const tip0 = pos.length / 3; pos.push(stations[0][0] - 0.02, stations[0][1], 0);
  const tip1 = pos.length / 3; pos.push(stations[n - 1][0] + 0.02, stations[n - 1][1], 0);
  for (let i = 0; i < n - 1; i++) for (let k = 0; k < seg; k++) {
    const a = i * seg + k, b = i * seg + (k + 1) % seg, c = (i + 1) * seg + k, d = (i + 1) * seg + (k + 1) % seg;
    idx.push(a, c, b, b, c, d);
  }
  for (let k = 0; k < seg; k++) {
    idx.push(tip0, k, (k + 1) % seg);
    idx.push(tip1, (n - 1) * seg + (k + 1) % seg, (n - 1) * seg + k);
  }
  // faces must point outward whichever way the stations run: a negative signed volume means inside out
  let vol = 0;
  for (let i = 0; i < idx.length; i += 3) {
    const a = idx[i] * 3, b = idx[i + 1] * 3, c = idx[i + 2] * 3;
    vol += pos[a] * (pos[b + 1] * pos[c + 2] - pos[b + 2] * pos[c + 1]) - pos[a + 1] * (pos[b] * pos[c + 2] - pos[b + 2] * pos[c])
         + pos[a + 2] * (pos[b] * pos[c + 1] - pos[b + 1] * pos[c]);
  }
  if (vol < 0) for (let i = 0; i < idx.length; i += 3) { const t = idx[i + 1]; idx[i + 1] = idx[i + 2]; idx[i + 2] = t; }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  g.setIndex(idx); return g;
}

/**
 * A flat shape (fin, wing, ear, tail fan) from a 2D outline [[u, v], ...] with a small thickness.
 * plane 'xy' (vertical, like a dorsal fin or tail) or 'xz' (horizontal, like a wing), placed at (x, y, z).
 */
export function flat(outline, thick = 0.03, plane = 'xy', x = 0, y = 0, z = 0) {
  const s = new THREE.Shape(outline.map(([u, v]) => new THREE.Vector2(u, v)));
  const g = new THREE.ExtrudeGeometry(s, { depth: thick, bevelEnabled: false, curveSegments: 4 });
  g.translate(0, 0, -thick / 2);
  if (plane === 'xz') g.rotateX(Math.PI / 2);
  g.translate(x, y, z); return g;
}

/** Four legs: front pair at x = fx, back pair at x = bx, half-width z, from hip height h to the ground. */
export function legs4(fx, bx, z, h, r0 = 0.06, r1 = 0.04, knee = 0.08) {
  const leg = (x, zz, p) => part(limb([x, h, zz], [x + knee, h * 0.45, zz], r0, (r0 + r1) / 2), { p, pivot: [x, h, zz], role: R.ACCENT });
  const shin = (x, zz, p) => part(limb([x + knee, h * 0.45, zz], [x, 0, zz], (r0 + r1) / 2, r1), { p, pivot: [x, h, zz], role: R.DARK });
  return [leg(fx, z, P.LEG_FL), shin(fx, z, P.LEG_FL), leg(fx, -z, P.LEG_FR), shin(fx, -z, P.LEG_FR),
          leg(bx, z, P.LEG_BL), shin(bx, z, P.LEG_BL), leg(bx, -z, P.LEG_BR), shin(bx, -z, P.LEG_BR)];
}

/** A pair of eyes at (x, y, ±z) that move with the head: pass the head's pivot (the neck). */
export function eyes(x, y, z, r = 0.05, pivot = [x, y, 0]) {
  return [part(ellipsoid(r, r, r, x, y, z, 6), { p: P.HEAD, pivot, role: R.EYE }),
          part(ellipsoid(r, r, r, x, y, -z, 6), { p: P.HEAD, pivot, role: R.EYE })];
}

/** A swarm cloud: n members of radius size scattered within radius r (height r·flat), each jittering on its own. */
export function swarm(n, r, size, seed, flatness = 1, seg = 6) {
  const out = []; let h = seed >>> 0;
  for (let i = 0; i < n; i++) {
    h = hash1(h + i * 7919);
    const a = (h & 1023) / 1024 * Math.PI * 2, d = Math.sqrt(((h >> 10) & 1023) / 1024) * r, y = (((h >> 20) & 255) / 256) * r * flatness;
    const c = [Math.cos(a) * d, y + size, Math.sin(a) * d];
    out.push(part(ellipsoid(size, size * 0.8, size * 0.9, ...c, seg), { p: P.MEMBER, pivot: c, role: R.BODY }));
  }
  return out;
}

export function hash1(x) { x = Math.imul(x ^ (x >>> 16), 0x45d9f3b); x = Math.imul(x ^ (x >>> 16), 0x45d9f3b); return (x ^ (x >>> 16)) >>> 0; }

// ------------------------------------------------------------------ build

/** Colours of the roles for one species colour. */
export function palette(color) {
  const c = new THREE.Color(color), white = new THREE.Color(1, 1, 1);
  return {
    [R.BODY]: c, [R.BACK]: c.clone().multiplyScalar(0.8), [R.BELLY]: c.clone().lerp(white, 0.5),
    [R.ACCENT]: c.clone().multiplyScalar(0.62), [R.DARK]: c.clone().multiplyScalar(0.35),
    [R.EYE]: new THREE.Color(0x0d0d0f), [R.HORN]: new THREE.Color(0xe6dcc2), [R.PALE]: c.clone().lerp(white, 0.75),
  };
}

/** Merge parts into one geometry with position, normal, color, aPart and aPivot. */
export function build(parts, color) {
  const pal = palette(color), belly = pal[R.BELLY];
  const ps = parts.map((q) => { const g = q.g.index ? q.g.toNonIndexed() : q.g; g.computeVertexNormals(); return [g, q]; });
  let n = 0; for (const [g] of ps) n += g.attributes.position.count;
  const pos = new Float32Array(n * 3), nor = new Float32Array(n * 3), col = new Float32Array(n * 3), apart = new Float32Array(n), piv = new Float32Array(n * 3);
  let o = 0; const tc = new THREE.Color();
  for (const [g, q] of ps) {
    const P_ = g.attributes.position.array, N = g.attributes.normal.array, m = g.attributes.position.count;
    pos.set(P_, o * 3); nor.set(N, o * 3);
    const base = pal[q.role] ?? pal[R.BODY];
    for (let i = 0; i < m; i++) {
      tc.copy(base);
      if (q.role === R.BODY) {
        // countershading: faces that point down take the belly colour
        const ny = N[i * 3 + 1];
        tc.lerp(belly, Math.min(1, Math.max(0, (-ny + 0.1) * 1.6)));
      }
      col[(o + i) * 3] = tc.r; col[(o + i) * 3 + 1] = tc.g; col[(o + i) * 3 + 2] = tc.b;
      apart[o + i] = q.p;
      piv[(o + i) * 3] = q.pivot[0]; piv[(o + i) * 3 + 1] = q.pivot[1]; piv[(o + i) * 3 + 2] = q.pivot[2];
    }
    o += m;
  }
  const out = new THREE.BufferGeometry();
  out.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  out.setAttribute('normal', new THREE.BufferAttribute(nor, 3));
  out.setAttribute('color', new THREE.BufferAttribute(col, 3));
  out.setAttribute('aPart', new THREE.BufferAttribute(apart, 1));
  out.setAttribute('aPivot', new THREE.BufferAttribute(piv, 3));
  out.computeBoundingSphere();
  return out;
}

/** Triangle count of a built geometry. */
export function triangles(g) { return g.attributes.position.count / 3; }
