// Near models for: grazers. Each entry: shape id -> (K, sp) => list of K.part(...). See kit.js for the contract.
// MOTION: optional per-shape overrides of the motion defaults in anim.js (stride, legAmp, wingAmp, ...).
//
// To stay near 600 triangles, limbs here are open-ended tubes (their ends hide inside the body or the next
// segment, which overlaps the joint) and eyes, ears and spots are low-segment spheres.

import * as THREE from 'three';

const UP = new THREE.Vector3(0, 1, 0);

/** Open tapered tube from a to b (radius r0 at a, r1 at b), extended by ext past b to cover the joint. */
function tube(a, b, r0, r1 = r0, seg = 5, ext = 0) {
  const A = new THREE.Vector3(...a), d = new THREE.Vector3(...b).sub(A), len = d.length() + ext;
  const g = new THREE.CylinderGeometry(r1, r0, len, seg, 1, true);
  g.translate(0, len / 2, 0);
  g.applyQuaternion(new THREE.Quaternion().setFromUnitVectors(UP, d.normalize()));
  g.translate(A.x, A.y, A.z); return g;
}

/** Open cone from base a (radius r) to tip b. */
const spike = (a, b, r, seg = 4) => tube(a, b, r, 0.0001, seg);

/** Ellipsoid with explicit segments and an optional rotation [rx, ry, rz]. */
function ell(sx, sy, sz, at, rot = [0, 0, 0], w = 6, h = 4) {
  const g = new THREE.SphereGeometry(1, w, h);
  g.scale(sx, sy, sz);
  g.applyQuaternion(new THREE.Quaternion().setFromEuler(new THREE.Euler(...rot)));
  g.translate(...at); return g;
}

/** A jointed leg through pts [[x, y, dz?], ...] at side z (dz pushes a joint outward), all turning about pts[0]. */
function leg(K, pts, z, radii, roles, p, seg = 5) {
  const s = Math.sign(z) || 1;
  const P3 = pts.map(([x, y, dz = 0]) => [x, y, z + s * dz]);
  const out = [];
  for (let i = 0; i < P3.length - 1; i++) {
    const last = i === P3.length - 2;
    out.push(K.part(tube(P3[i], P3[i + 1], radii[i], radii[i + 1], i === 0 ? seg : Math.max(4, seg - 1), last ? 0 : radii[i + 1]),
      { p, pivot: P3[0], role: roles[Math.min(i, roles.length - 1)] }));
  }
  return out;
}

/** Front and back pairs of legs (front at +z is LEG_FL, so FL+BR and FR+BL step together). */
function legs(K, front, back, zf, zb, rf, rb, roles, seg = 5) {
  const { P } = K;
  return [...leg(K, front, zf, rf, roles, P.LEG_FL, seg), ...leg(K, front, -zf, rf, roles, P.LEG_FR, seg),
          ...leg(K, back, zb, rb, roles, P.LEG_BL, seg), ...leg(K, back, -zb, rb, roles, P.LEG_BR, seg)];
}

/** A flat patch (spot, stripe) lying on the side s (±1) of a spine body at (x, y), radii rx by ry. */
function patch(stations, x, y, s, rx, ry, w = 5) {
  let i = 0; while (i < stations.length - 2 && stations[i + 1][0] < x) i++;
  const [x0, y0, h0, w0] = stations[i], [x1, y1, h1, w1] = stations[i + 1], t = Math.min(1, Math.max(0, (x - x0) / (x1 - x0)));
  const yc = y0 + (y1 - y0) * t, hh = h0 + (h1 - h0) * t, hw = w0 + (w1 - w0) * t;
  const u = Math.max(-0.95, Math.min(0.95, (y - yc) / hh)), z = hw * Math.sqrt(1 - u * u);
  const th = Math.atan2(u / hh, Math.sqrt(1 - u * u) / hw);
  return ell(rx, ry, 0.02, [x, y, s * (z - 0.005)], [-s * th, 0, 0], w, 3);
}

/** The same geometry factory applied at +z and -z. */
const both = (f) => [f(1), f(-1)];

export const MODELS = {
  // Drifters: small grazer. A slender gazelle with lyre horns, tall ears, pale rump and a flicking tail.
  0: (K) => {
    const { P, R } = K, neck = [0.45, 0.95, 0];
    const H = (g, role = R.BODY) => K.part(g, { p: P.HEAD, pivot: neck, role });
    return [
      K.part(K.spine([[-0.74, 1.0, 0.1, 0.09], [-0.6, 1.0, 0.25, 0.21], [-0.3, 0.98, 0.31, 0.26], [0.05, 0.97, 0.32, 0.27],
        [0.38, 1.02, 0.3, 0.25], [0.6, 1.08, 0.24, 0.2], [0.76, 1.14, 0.1, 0.1]], 8), { p: P.BODY }),
      K.part(ell(0.1, 0.17, 0.2, [-0.63, 0.98, 0], [0, 0, 0], 6, 4), { role: R.PALE }),
      H(tube([0.48, 1.06, 0], [0.9, 1.52, 0], 0.15, 0.09, 6, 0.06)),
      H(K.spine([[0.8, 1.54, 0.1, 0.09], [0.92, 1.57, 0.13, 0.11], [1.06, 1.51, 0.1, 0.085], [1.18, 1.44, 0.07, 0.06], [1.25, 1.41, 0.04, 0.04]], 7)),
      H(ell(0.025, 0.03, 0.03, [1.27, 1.42, 0], [0, 0, 0], 4, 3), R.DARK),
      ...both((s) => H(ell(0.035, 0.035, 0.03, [1.0, 1.58, s * 0.095], [0, 0, 0], 5, 4), R.EYE)),
      ...both((s) => H(ell(0.045, 0.13, 0.018, [0.85, 1.74, s * 0.15], [s * 0.6, 0, 0.35], 5, 3), R.ACCENT)),
      ...both((s) => H(tube([0.92, 1.64, s * 0.05], [0.86, 1.83, s * 0.09], 0.028, 0.02, 4, 0.02), R.HORN)),
      ...both((s) => H(spike([0.86, 1.83, s * 0.09], [0.92, 2.0, s * 0.07], 0.02, 4), R.HORN)),
      K.part(tube([-0.72, 1.06, 0], [-0.86, 0.86, 0], 0.045, 0.03, 4, 0.03), { p: P.TAIL, pivot: [-0.72, 1.06, 0] }),
      K.part(spike([-0.86, 0.86, 0], [-0.9, 0.74, 0], 0.035, 4), { p: P.TAIL, pivot: [-0.72, 1.06, 0], role: R.DARK }),
      ...legs(K, [[0.42, 0.98], [0.46, 0.5], [0.44, 0.07], [0.47, 0]], [[-0.46, 1.0], [-0.34, 0.6], [-0.54, 0.38], [-0.5, 0.07], [-0.47, 0]],
        0.14, 0.14, [0.09, 0.04, 0.03, 0.04], [0.12, 0.05, 0.032, 0.028, 0.04], [R.BODY, R.ACCENT, R.DARK, R.DARK]),
    ];
  },

  // Browsers: large browser of shrubs and trees. Giraffe-like: sloping back, long maned neck, ossicones,
  // patches on the flanks, long legs with hocks, tufted tail.
  1: (K) => {
    const { P, R } = K, neck = [0.82, 1.98, 0];
    const H = (g, role = R.BODY) => K.part(g, { p: P.HEAD, pivot: neck, role });
    const body = [[-1.14, 1.62, 0.12, 0.1], [-1.0, 1.62, 0.4, 0.34], [-0.6, 1.62, 0.56, 0.48], [-0.1, 1.66, 0.6, 0.52],
      [0.4, 1.78, 0.62, 0.52], [0.84, 1.92, 0.5, 0.43], [1.06, 2.0, 0.24, 0.22]];
    const spot = (x, y, s, r) => K.part(patch(body, x, y, s, r, r * 0.75), { role: R.ACCENT });
    return [
      K.part(K.spine(body, 8), { p: P.BODY }),
      ...both((s) => spot(-0.5, 1.78, s, 0.2)), ...both((s) => spot(0.05, 1.62, s, 0.22)), ...both((s) => spot(0.5, 1.98, s, 0.17)),
      H(tube([0.72, 1.86, 0], [1.46, 2.86, 0], 0.26, 0.13, 7, 0.08)),
      H(K.flat([[0.62, 2.0], [0.55, 2.12], [1.33, 3.02], [1.42, 2.92]], 0.06), R.DARK),
      H(K.spine([[1.33, 2.98, 0.12, 0.11], [1.5, 3.01, 0.16, 0.13], [1.7, 2.95, 0.13, 0.1], [1.88, 2.86, 0.09, 0.08], [1.97, 2.82, 0.05, 0.05]], 7)),
      H(ell(0.035, 0.035, 0.04, [1.99, 2.83, 0], [0, 0, 0], 4, 3), R.DARK),
      ...both((s) => H(ell(0.045, 0.045, 0.035, [1.64, 3.04, s * 0.11], [0, 0, 0], 5, 3), R.EYE)),
      ...both((s) => H(ell(0.12, 0.045, 0.03, [1.38, 3.1, s * 0.2], [0, 0, 0.4], 5, 3), R.ACCENT)),
      ...both((s) => H(tube([1.47, 3.1, s * 0.06], [1.44, 3.32, s * 0.09], 0.035, 0.03, 4, 0), R.ACCENT)),
      ...both((s) => H(ell(0.045, 0.045, 0.045, [1.44, 3.33, s * 0.09], [0, 0, 0], 4, 2), R.DARK)),
      K.part(tube([-1.1, 1.68, 0], [-1.3, 1.05, 0], 0.05, 0.03, 4, 0.03), { p: P.TAIL, pivot: [-1.1, 1.68, 0] }),
      K.part(spike([-1.3, 1.08, 0], [-1.33, 0.82, 0], 0.07, 4), { p: P.TAIL, pivot: [-1.1, 1.68, 0], role: R.DARK }),
      ...legs(K, [[0.66, 1.75], [0.72, 0.86], [0.69, 0.1], [0.72, 0]], [[-0.7, 1.72], [-0.82, 0.72], [-0.72, 0.1], [-0.7, 0]],
        0.3, 0.3, [0.17, 0.08, 0.06, 0.08], [0.2, 0.08, 0.06, 0.08], [R.BODY, R.ACCENT, R.DARK], 6),
    ];
  },

  // Tuskers: rooting omnivore. A boar: heavy forequarters, bristled crest, wedge head with a snout disc,
  // upcurved tusks on a lower jaw that opens, short legs with hooves, thin tufted tail.
  2: (K) => {
    const { P, R } = K, neck = [0.78, 0.8, 0];
    const H = (g, role = R.BODY) => K.part(g, { p: P.HEAD, pivot: neck, role });
    const J = (g, role = R.BODY) => K.part(g, { p: P.JAW, pivot: neck, role });
    return [
      K.part(K.spine([[-0.9, 0.8, 0.15, 0.13], [-0.75, 0.8, 0.38, 0.34], [-0.4, 0.82, 0.48, 0.44], [0.05, 0.86, 0.55, 0.49],
        [0.45, 0.9, 0.55, 0.47], [0.72, 0.88, 0.43, 0.37], [0.85, 0.84, 0.24, 0.23]], 9), { p: P.BODY }),
      K.part(K.flat([[-0.45, 1.2], [-0.3, 1.36], [-0.15, 1.33], [0.0, 1.48], [0.15, 1.43], [0.3, 1.54], [0.45, 1.48], [0.62, 1.48],
        [0.74, 1.24], [0.4, 1.36], [0.0, 1.32]], 0.1), { role: R.DARK }),
      H(K.spine([[0.68, 0.92, 0.3, 0.26], [0.9, 0.87, 0.27, 0.22], [1.1, 0.78, 0.18, 0.15], [1.28, 0.71, 0.12, 0.11], [1.36, 0.69, 0.11, 0.1]], 8)),
      H(ell(0.03, 0.11, 0.11, [1.38, 0.69, 0], [0, 0, 0], 7, 4), R.DARK),
      ...both((s) => H(ell(0.035, 0.035, 0.03, [1.0, 0.99, s * 0.16], [0, 0, 0], 5, 4), R.EYE)),
      ...both((s) => H(spike([0.82, 1.1, s * 0.13], [0.74, 1.3, s * 0.24], 0.075, 4), R.ACCENT)),
      J(K.spine([[0.84, 0.66, 0.08, 0.15], [1.04, 0.6, 0.07, 0.12], [1.24, 0.58, 0.05, 0.09], [1.3, 0.58, 0.03, 0.05]], 6), R.ACCENT),
      ...both((s) => J(tube([1.18, 0.6, s * 0.09], [1.32, 0.68, s * 0.15], 0.035, 0.026, 4, 0.02), R.HORN)),
      ...both((s) => J(spike([1.32, 0.68, s * 0.15], [1.29, 0.88, s * 0.13], 0.026, 4), R.HORN)),
      K.part(tube([-0.87, 0.92, 0], [-0.98, 0.62, 0], 0.03, 0.02, 4, 0.02), { p: P.TAIL, pivot: [-0.87, 0.92, 0], role: R.ACCENT }),
      K.part(spike([-0.98, 0.66, 0], [-1.0, 0.52, 0], 0.04, 4), { p: P.TAIL, pivot: [-0.87, 0.92, 0], role: R.DARK }),
      ...legs(K, [[0.5, 0.6], [0.54, 0.3], [0.5, 0.07], [0.53, 0]], [[-0.55, 0.65], [-0.62, 0.3], [-0.55, 0.07], [-0.52, 0]],
        0.25, 0.26, [0.13, 0.07, 0.055, 0.065], [0.15, 0.07, 0.055, 0.065], [R.BODY, R.ACCENT, R.DARK], 6),
    ];
  },

  // Burrowers: seed-caching granivore. A round, hamster-like rodent with bulging pale cheek pouches, upright
  // ears, big eyes, little forepaws, long hind feet and a pompom tail.
  4: (K) => {
    const { P, R } = K, neck = [0.3, 0.56, 0];
    const H = (g, role = R.BODY) => K.part(g, { p: P.HEAD, pivot: neck, role });
    return [
      K.part(K.spine([[-0.52, 0.44, 0.12, 0.12], [-0.45, 0.46, 0.3, 0.3], [-0.24, 0.47, 0.38, 0.37], [0.0, 0.47, 0.36, 0.34],
        [0.2, 0.5, 0.29, 0.28], [0.34, 0.55, 0.18, 0.2]], 9), { p: P.BODY }),
      H(K.spine([[0.24, 0.63, 0.17, 0.17], [0.37, 0.66, 0.22, 0.21], [0.52, 0.62, 0.19, 0.17], [0.64, 0.56, 0.11, 0.09], [0.7, 0.53, 0.05, 0.05]], 8)),
      H(ell(0.028, 0.025, 0.035, [0.71, 0.54, 0], [0, 0, 0], 4, 3), R.DARK),
      ...both((s) => H(ell(0.13, 0.1, 0.08, [0.5, 0.52, s * 0.15], [0, 0, 0], 6, 4), R.PALE)),
      ...both((s) => H(ell(0.045, 0.05, 0.035, [0.53, 0.69, s * 0.135], [0, 0, 0], 5, 4), R.EYE)),
      ...both((s) => H(ell(0.065, 0.15, 0.025, [0.36, 0.9, s * 0.11], [s * 0.3, 0, 0.25], 6, 3), R.BODY)),
      ...both((s) => H(ell(0.04, 0.1, 0.01, [0.375, 0.9, s * 0.13], [s * 0.3, 0, 0.25], 5, 3), R.PALE)),
      K.part(ell(0.09, 0.09, 0.09, [-0.54, 0.5, 0], [0, 0, 0], 5, 4), { p: P.TAIL, pivot: [-0.5, 0.5, 0], role: R.PALE }),
      ...legs(K, [[0.24, 0.32], [0.3, 0.12], [0.36, 0.02]], [[-0.22, 0.3], [-0.3, 0.12], [-0.28, 0.04]],
        0.14, 0.22, [0.06, 0.04, 0.035], [0.1, 0.06, 0.05], [R.ACCENT, R.DARK], 5),
      ...both((s) => K.part(ell(0.15, 0.035, 0.055, [-0.2, 0.035, s * 0.22], [0, 0, 0], 5, 3), { p: s > 0 ? P.LEG_BL : P.LEG_BR, pivot: [-0.22, 0.3, s * 0.22], role: R.DARK })),
    ];
  },

  // Rotmats: sessile fungal mat on wood and litter. A lobed mat with radiating hyphae, clusters of mushrooms
  // (caps pale underneath, like gills) and puffballs; the fruiting bodies only quiver.
  8: (K) => {
    const { P, R } = K, out = [
      K.part(ell(1.05, 0.1, 0.95, [0, 0.0, 0], [0, 0.3, 0], 12, 4), { role: R.BACK }),
      K.part(ell(0.5, 0.08, 0.4, [0.75, 0.0, 0.55], [0, 0.6, 0], 8, 3), { role: R.ACCENT }),
      K.part(ell(0.45, 0.09, 0.5, [-0.8, 0.0, 0.35], [0, -0.3, 0], 8, 3), { role: R.ACCENT }),
      K.part(ell(0.55, 0.07, 0.35, [-0.3, 0.0, -0.85], [0, 0.2, 0], 8, 3), { role: R.BACK }),
      K.part(ell(0.4, 0.08, 0.35, [0.7, 0.0, -0.6], [0, -0.5, 0], 8, 3), { role: R.DARK }),
    ];
    for (let i = 0; i < 7; i++) {      // hyphae creeping out from the edge
      const a = i * 0.9 + 0.3, r0 = 0.8, r1 = 1.35 + (i % 3) * 0.12;
      out.push(K.part(tube([Math.cos(a) * r0, 0.02, Math.sin(a) * r0], [Math.cos(a + 0.15) * r1, 0.01, Math.sin(a + 0.15) * r1], 0.02, 0.008, 3), { role: R.PALE }));
    }
    const shroom = (x, z, h, r, tilt) => {
      const top = [x + tilt, h, z];
      return [K.part(tube([x, 0.05, z], top, 0.035 + r * 0.12, 0.03 + r * 0.08, 5, 0.02), { p: P.MEMBER, pivot: top, role: R.PALE }),
              K.part(ell(r, r * 0.6, r, [top[0], h - r * 0.05, z], [0, 0, -tilt], 7, 4), { p: P.MEMBER, pivot: top, role: R.BODY })];
    };
    out.push(...shroom(0.2, 0.1, 0.48, 0.2, 0.03), ...shroom(0.42, 0.25, 0.32, 0.14, 0.06), ...shroom(0.05, 0.35, 0.26, 0.11, -0.05),
             ...shroom(-0.45, -0.3, 0.38, 0.17, -0.04), ...shroom(-0.25, -0.5, 0.22, 0.1, 0.05));
    for (const [x, z, r] of [[-0.6, 0.3, 0.1], [-0.72, 0.15, 0.07]]) {   // puffballs
      out.push(K.part(ell(r, r * 0.85, r, [x, 0.06 + r * 0.7, z], [0, 0, 0], 5, 3), { p: P.MEMBER, pivot: [x, 0.06, z], role: R.PALE }));
    }
    // shelf brackets along a buried log
    out.push(K.part(ell(0.22, 0.04, 0.12, [-0.1, 0.12, 0.6], [0, 0, 0], 6, 3), { role: R.ACCENT }),
             K.part(ell(0.16, 0.035, 0.1, [0.1, 0.17, 0.64], [0, 0.2, 0], 6, 3), { role: R.BODY }));
    return out;
  },

  // Gleaners: vertebrate scavenger. Hyena-like: high shoulders and sloping back, a dark mane, a heavy head
  // carried low with a jaw that opens, round ears, spotted flanks, long forelegs and a tufted tail.
  9: (K) => {
    const { P, R } = K, neck = [0.68, 0.86, 0];
    const H = (g, role = R.BODY) => K.part(g, { p: P.HEAD, pivot: neck, role });
    const J = (g, role = R.BODY) => K.part(g, { p: P.JAW, pivot: neck, role });
    const body = [[-0.75, 0.74, 0.12, 0.11], [-0.6, 0.77, 0.27, 0.23], [-0.3, 0.82, 0.33, 0.28], [0.05, 0.9, 0.39, 0.31],
      [0.38, 0.98, 0.41, 0.32], [0.6, 0.98, 0.32, 0.27], [0.72, 0.94, 0.15, 0.15]];
    const spot = (x, y, s, r) => K.part(patch(body, x, y, s, r, r * 0.8, 6), { role: R.DARK });
    return [
      K.part(K.spine(body, 8), { p: P.BODY }),
      K.part(K.flat([[-0.3, 1.08], [-0.15, 1.22], [0.1, 1.33], [0.35, 1.45], [0.55, 1.42], [0.68, 1.24], [0.4, 1.32], [0.1, 1.22], [-0.15, 1.08]], 0.07), { role: R.DARK }),
      ...both((s) => spot(-0.36, 0.86, s, 0.09)), ...both((s) => spot(-0.08, 0.96, s, 0.085)),
      H(K.spine([[0.6, 0.98, 0.25, 0.2], [0.8, 0.94, 0.26, 0.21], [0.96, 0.86, 0.21, 0.18], [1.08, 0.8, 0.15, 0.13], [1.16, 0.77, 0.11, 0.1]], 7)),
      H(ell(0.035, 0.04, 0.05, [1.18, 0.78, 0], [0, 0, 0], 4, 3), R.DARK),
      ...both((s) => H(ell(0.035, 0.035, 0.03, [0.97, 0.96, s * 0.13], [0, 0, 0], 5, 3), R.EYE)),
      ...both((s) => H(ell(0.08, 0.1, 0.025, [0.74, 1.2, s * 0.15], [s * 0.4, 0, -0.2], 5, 3), R.ACCENT)),
      J(K.spine([[0.8, 0.7, 0.08, 0.14], [0.98, 0.67, 0.07, 0.12], [1.1, 0.66, 0.05, 0.09], [1.15, 0.66, 0.03, 0.05]], 6), R.ACCENT),
      K.part(tube([-0.72, 0.84, 0], [-0.86, 0.56, 0], 0.04, 0.03, 4, 0.03), { p: P.TAIL, pivot: [-0.72, 0.84, 0] }),
      K.part(ell(0.05, 0.11, 0.05, [-0.88, 0.5, 0], [0, 0, -0.4], 4, 3), { p: P.TAIL, pivot: [-0.72, 0.84, 0], role: R.DARK }),
      ...legs(K, [[0.45, 0.9], [0.43, 0.5], [0.47, 0.07], [0.55, 0.02]], [[-0.5, 0.76], [-0.38, 0.45], [-0.55, 0.24], [-0.49, 0.05], [-0.42, 0.02]],
        0.19, 0.19, [0.11, 0.055, 0.04, 0.04], [0.13, 0.06, 0.045, 0.035, 0.04], [R.BODY, R.ACCENT, R.DARK, R.DARK]),
    ];
  },

  // Croakers: amphibian (adult frog; tadpoles reuse this model). Squat wide body, flat head with bulging eyes,
  // a pale throat that drops open, a spotted back, small forelegs and folded hind legs with webbed feet.
  18: (K) => {
    const { P, R } = K, neck = [0.12, 0.3, 0];
    const H = (g, role = R.BODY) => K.part(g, { p: P.HEAD, pivot: neck, role });
    const spot = (x, z, r) => K.part(ell(r, 0.025, r * 0.85, [x, 0.585 - Math.abs(z) * 0.35, z], [Math.sign(z) * 0.35, 0, 0], 5, 3), { role: R.DARK });
    const foot = (s, p) => K.part(K.flat([[0, 0], [0.16, -0.08], [0.2, 0], [0.16, 0.08]], 0.015, 'xz', -0.14, 0.02, s * 0.46), { p, pivot: [-0.3, 0.22, s * 0.3], role: R.ACCENT });
    return [
      K.part(K.spine([[-0.46, 0.3, 0.08, 0.12], [-0.36, 0.32, 0.2, 0.3], [-0.12, 0.34, 0.26, 0.38], [0.1, 0.33, 0.22, 0.36], [0.22, 0.31, 0.14, 0.28]], 9), { p: P.BODY }),
      spot(-0.2, 0.1, 0.07), spot(0.0, -0.12, 0.06), spot(-0.3, -0.16, 0.05), spot(-0.05, 0.2, 0.05),
      H(K.spine([[0.06, 0.37, 0.17, 0.3], [0.24, 0.38, 0.15, 0.29], [0.4, 0.34, 0.1, 0.23], [0.5, 0.3, 0.05, 0.13]], 9)),
      ...both((s) => H(ell(0.085, 0.08, 0.08, [0.27, 0.5, s * 0.16], [0, 0, 0], 6, 4))),
      ...both((s) => H(ell(0.055, 0.055, 0.05, [0.31, 0.53, s * 0.19], [0, 0, 0], 5, 4), R.EYE)),
      K.part(K.spine([[0.08, 0.24, 0.08, 0.26], [0.3, 0.24, 0.07, 0.24], [0.45, 0.26, 0.04, 0.14], [0.5, 0.28, 0.02, 0.06]], 8), { p: P.JAW, pivot: neck, role: R.PALE }),
      ...legs(K, [[0.15, 0.24, 0], [0.2, 0.12, 0.07], [0.28, 0.02, 0.05]], [[-0.3, 0.22, 0], [-0.04, 0.16, 0.15], [-0.34, 0.06, 0.12], [-0.16, 0.025, 0.16]],
        0.25, 0.3, [0.06, 0.035, 0.03], [0.11, 0.06, 0.04, 0.03], [R.BODY, R.ACCENT, R.ACCENT], 5),
      foot(1, P.LEG_BL), foot(-1, P.LEG_BR),
      ...both((s) => K.part(ell(0.05, 0.015, 0.05, [0.3, 0.015, s * 0.31], [0, 0, 0], 5, 3), { p: s > 0 ? P.LEG_FL : P.LEG_FR, pivot: [0.15, 0.24, s * 0.25], role: R.ACCENT })),
    ];
  },
};

export const MOTION = {
  0: { stride: 9, legAmp: 0.6, tailAmp: 0.55, headDip: 1.5 },
  1: { stride: 4.5, legAmp: 0.38, tailAmp: 0.35, headDip: 0.45 },
  2: { stride: 8.5, legAmp: 0.5, tailAmp: 0.6, headDip: 0.9 },
  4: { stride: 12, legAmp: 0.5, tailAmp: 0.3, headDip: 0.5 },
  8: { stride: 0, legAmp: 0, tailAmp: 0, headDip: 0, jitter: 0.012, wingAmp: 0, wingFreq: 0, hover: 0 },
  9: { stride: 8, legAmp: 0.55, tailAmp: 0.4, headDip: 0.75 },
  18: { stride: 6, legAmp: 0.35, tailAmp: 0, headDip: 0.25 },
};
