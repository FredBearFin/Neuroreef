// Near models for: water_swarms. Each entry: shape id -> (K, sp) => list of K.part(...). See kit.js for the contract.
// MOTION: optional per-shape overrides of the motion defaults in anim.js (stride, legAmp, wingAmp, ...).
//
// Fish (Minnows, Pikes) are one countershaded spine body with thin fins, eyes and markings; the tail fan is
// P.TAIL and the Pike's lower jaw is P.JAW. Swarms are clouds of tiny multi-part creatures: every part of a
// member is P.MEMBER with the member's centre as pivot, so the member jitters as one rigid body. Member wings
// cannot flap on their own, so each member's wings are posed at a different stroke angle.
//
// Note: K.spine builds outward-facing surfaces only when its stations run front to back (descending x).

import * as THREE from 'three';

// ------------------------------------------------------------------ cheap geometry

/** Double-sided polygon fan from c through rim points (zero thickness: 2 triangles per rim edge). */
function fan(c, rim) {
  const pos = [];
  for (let i = 0; i < rim.length - 1; i++) {
    const a = rim[i], b = rim[i + 1];
    pos.push(...c, ...a, ...b, ...c, ...b, ...a);
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  return g;
}
const tri = (a, b, c) => fan(a, [b, c]);
const quad = (a, b, c, d) => fan(a, [b, c, d]);

/** One-sided quad strip (faces toward +n side given by point order). */
function strip(top, bot) {
  const pos = [];
  for (let i = 0; i < top.length - 1; i++) pos.push(...top[i], ...bot[i], ...bot[i + 1], ...top[i], ...bot[i + 1], ...top[i + 1]);
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  return g;
}

/** Octahedron (8 triangles) with radii rx, ry, rz at (x, y, z): heads, eyes, tiny bodies. */
function octa(rx, ry, rz, x = 0, y = 0, z = 0) {
  const v = [[rx, 0, 0], [-rx, 0, 0], [0, ry, 0], [0, -ry, 0], [0, 0, rz], [0, 0, -rz]];
  const f = [[0, 2, 4], [4, 2, 1], [1, 2, 5], [5, 2, 0], [0, 4, 3], [4, 1, 3], [1, 5, 3], [5, 0, 3]];
  const pos = [];
  for (const t of f) for (const k of t) pos.push(v[k][0] + x, v[k][1] + y, v[k][2] + z);
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  return g;
}

/** Half-height and half-width of a spine body (stations front to back) at x. */
function bodyAt(st, x) {
  for (let i = 0; i < st.length - 1; i++) {
    const a = st[i], b = st[i + 1];
    if (x <= a[0] && x >= b[0]) { const t = (a[0] - x) / (a[0] - b[0]); return [a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t, a[3] + (b[3] - a[3]) * t]; }
  }
  return [0, 0.01, 0.01];
}

/** A band hugging both flanks of a spine body from x0 to x1 (n segments), centred at height dy, half-height w (rows strips tall). */
function flankBand(st, x0, x1, n, dy, w, rows = 1) {
  if (rows > 1) {
    const out = [], h = (2 * w) / rows;
    for (let r = 0; r < rows; r++) out.push(...flankBand(st, x0, x1, n, dy - w + h * (r + 0.5), h / 2));
    return out;
  }
  const out = [];
  for (const side of [1, -1]) {
    const top = [], bot = [];
    for (let i = 0; i <= n; i++) {
      const x = x0 + (x1 - x0) * i / n, [y, hh, hw] = bodyAt(st, x);
      const zAt = (yy) => side * (hw * Math.sqrt(Math.max(0, 1 - ((yy - y) / hh) ** 2)) + 0.006);
      top.push([x, y + dy + w, zAt(y + dy + w)]); bot.push([x, y + dy - w, zAt(y + dy - w)]);
    }
    // order the points so the faces look outward on each side
    out.push(side > 0 ? strip(bot, top) : strip(top, bot));
  }
  return out;
}

// ------------------------------------------------------------------ swarm layout

/** n member placements in a disc of radius r, heights y0..y1, kept apart; each {c, yaw, u, v}. */
function scatter(K, n, r, y0, y1, seed, minD) {
  const out = []; let h = seed >>> 0, tries = 0;
  while (out.length < n && tries < 400) {
    tries++;
    h = K.hash1(h + tries * 7919);
    const a = (h & 1023) / 1024 * Math.PI * 2, d = Math.sqrt(((h >> 10) & 1023) / 1024) * r;
    const y = y0 + (((h >> 20) & 255) / 256) * (y1 - y0);
    const c = [Math.cos(a) * d, y, Math.sin(a) * d];
    if (out.some((o) => Math.hypot(o.c[0] - c[0], (o.c[1] - c[1]) * 0.7, o.c[2] - c[2]) < minD)) continue;
    const h2 = K.hash1(h ^ 0x9e3779b9);
    out.push({ c, yaw: ((h2 & 1023) / 1024 - 0.5), u: ((h2 >> 10) & 1023) / 1024, v: ((h2 >> 20) & 1023) / 1024, i: out.length });
  }
  return out;
}

/** Place a member's parts (built at the origin facing +x) at m.c, turned by yaw (and pitch), all as P.MEMBER. */
function place(K, parts, m, yaw, pitch = 0, scale = 1) {
  return parts.map(([g, role]) => {
    g.scale(scale, scale, scale);
    if (pitch) g.rotateZ(pitch);
    g.rotateY(yaw);
    g.translate(...m.c);
    return K.part(g, { p: K.P.MEMBER, pivot: m.c, role });
  });
}

// ------------------------------------------------------------------ swarm members (local units, facing +x)

/** Hummer: a fuzzy bee-like pollinator with a dark head, a proboscis, dangling pollen legs and two wings at stroke angle w. */
function hummer(K, R, w) {
  const body = K.spine([[0.1, 0.02, 0.05, 0.05], [0.02, 0.02, 0.07, 0.07], [-0.08, 0.0, 0.075, 0.065], [-0.17, -0.02, 0.02, 0.02]], 4);
  const head = octa(0.045, 0.045, 0.05, 0.13, 0.025, 0);
  const prob = tri([0.15, 0.01, 0.008], [0.15, 0.01, -0.008], [0.26, -0.04, 0]);
  const legs = tri([-0.01, -0.04, 0.03], [-0.03, -0.04, -0.03], [-0.08, -0.14, 0]);
  const wing = (s) => {
    const ca = Math.cos(w), sa = Math.sin(w), p = (x, zz) => [x, 0.07 + sa * zz, s * ca * zz];
    return quad(p(0.05, 0.02), p(-0.02, 0.02), p(-0.12, 0.2), p(0.0, 0.24));
  };
  return [[body, R.BODY], [head, R.DARK], [prob, R.DARK], [legs, R.ACCENT], [wing(1), R.PALE], [wing(-1), R.PALE]];
}

/** Chewer: a grasshopper with big jumping hind legs and antennae; wings folded on the back or spread in a hop. */
function chewer(K, R, flying) {
  const st = [[0.12, 0.045, 0.05, 0.04], [0.0, 0.035, 0.045, 0.038], [-0.18, 0.03, 0.022, 0.02]];
  const body = K.spine(st, 4);
  const head = octa(0.04, 0.045, 0.04, 0.15, 0.05, 0);
  const parts = [[body, R.BODY], [head, R.ACCENT]];
  for (const s of [1, -1]) {
    // hind leg: thick thigh up to a high knee, thin shin back down to the ground
    parts.push([tri([0.02, 0.04, s * 0.035], [0.0, 0.015, s * 0.04], [-0.12, 0.13, s * 0.06]), R.ACCENT]);
    parts.push([tri([-0.12, 0.13, s * 0.06], [-0.11, 0.11, s * 0.06], [-0.04, 0.0, s * 0.08]), R.DARK]);
    // antenna
    parts.push([tri([0.18, 0.07, s * 0.01], [0.18, 0.06, s * 0.01], [0.24, 0.17, s * 0.09]), R.DARK]);
  }
  if (flying) for (const s of [1, -1]) parts.push([quad([0.08, 0.08, 0], [-0.05, 0.08, 0], [-0.14, 0.12, s * 0.2], [0.04, 0.12, s * 0.22]), R.PALE]);
  else parts.push([quad([0.08, 0.085, 0.0], [0.06, 0.08, 0.04], [-0.2, 0.05, 0.0], [0.06, 0.08, -0.04]), R.BACK]);
  return parts;
}

/** Delver beetle: a domed shell with a dark head and six splayed legs. */
function beetle(K, R) {
  const shell = K.spine([[0.09, 0.05, 0.035, 0.06], [0.0, 0.06, 0.06, 0.085], [-0.13, 0.045, 0.03, 0.05]], 5);
  const head = octa(0.045, 0.03, 0.045, 0.13, 0.04, 0);
  const parts = [[shell, R.BACK], [head, R.DARK]];
  for (const s of [1, -1]) {
    parts.push([tri([0.06, 0.03, s * 0.05], [0.03, 0.03, s * 0.05], [0.12, 0.0, s * 0.14]), R.DARK]);
    parts.push([tri([0.0, 0.03, s * 0.06], [-0.03, 0.03, s * 0.06], [0.0, 0.0, s * 0.16]), R.DARK]);
    parts.push([tri([-0.05, 0.03, s * 0.05], [-0.08, 0.03, s * 0.05], [-0.14, 0.0, s * 0.14]), R.DARK]);
  }
  return parts;
}

/** Delver worm: a ringed earthworm wriggling through the litter (bent sideways by phase u). */
function worm(K, R, u) {
  // square cross-section turned 45 degrees so it reads as a round tube; alternating radii suggest rings
  const g = K.spine([[0.21, 0, 0.026, 0.026], [0.11, 0, 0.042, 0.042], [0.0, 0, 0.034, 0.034], [-0.11, 0, 0.04, 0.04], [-0.22, 0, 0.022, 0.022]], 4);
  g.rotateX(Math.PI / 4); g.translate(0, 0.035, 0);
  const p = g.attributes.position;
  for (let i = 0; i < p.count; i++) p.setZ(i, p.getZ(i) + Math.sin(p.getX(i) * 14 + u * 6.28) * 0.04);
  return [[g, R.BODY]];
}

/** Bloodfly: a mosquito-like biter with a humped thorax, long proboscis, narrow wings and dangling legs. */
function bloodfly(K, R, w) {
  const body = K.spine([[0.06, 0.03, 0.045, 0.04], [-0.04, 0.015, 0.032, 0.028], [-0.2, -0.02, 0.012, 0.012]], 4);
  const head = octa(0.03, 0.03, 0.032, 0.1, 0.02, 0);
  const prob = tri([0.12, 0.012, 0.006], [0.12, 0.012, -0.006], [0.24, -0.05, 0]);
  const parts = [[body, R.BODY], [head, R.DARK], [prob, R.DARK]];
  for (const s of [1, -1]) {
    const ca = Math.cos(w), sa = Math.sin(w), q = (x, zz) => [x, 0.06 + sa * zz, s * ca * zz];
    parts.push([quad(q(0.03, 0.01), q(-0.01, 0.01), q(-0.16, 0.17), q(-0.11, 0.2)), R.PALE]);
    parts.push([tri([0.03, 0.0, s * 0.02], [0.0, 0.0, s * 0.02], [0.08, -0.16, s * 0.1]), R.DARK]);
    parts.push([tri([-0.02, 0.0, s * 0.02], [-0.05, 0.0, s * 0.02], [-0.18, -0.15, s * 0.1]), R.DARK]);
  }
  return parts;
}

/** Siltmite: a round water mite with a dark eye spot and four pairs of paddling legs. */
function siltmite(K, R) {
  const body = K.ellipsoid(0.1, 0.08, 0.09, 0, 0, 0, 5);
  const eye = tri([0.07, 0.07, 0.035], [0.07, 0.07, -0.035], [0.1, 0.04, 0]);
  const parts = [[body, R.BODY], [eye, R.DARK]];
  const legX = [0.06, 0.02, -0.02, -0.06], legDir = [0.9, 0.35, -0.25, -0.8];
  for (const s of [1, -1]) for (let k = 0; k < 4; k++) {
    const x = legX[k], a = legDir[k];
    parts.push([tri([x + 0.015, -0.02, s * 0.07], [x - 0.015, -0.02, s * 0.07], [x + Math.sin(a) * 0.14, -0.07 + (k % 2) * 0.03, s * (0.07 + Math.cos(a) * 0.13)]), R.ACCENT]);
  }
  return parts;
}

// ------------------------------------------------------------------ fish

function minnow(K, sp) {
  const { P, R } = K;
  const st = [[0.72, 0.0, 0.02, 0.02], [0.64, 0.005, 0.09, 0.065], [0.5, 0.015, 0.17, 0.11], [0.3, 0.025, 0.235, 0.145],
              [0.05, 0.02, 0.25, 0.15], [-0.22, 0.01, 0.2, 0.12], [-0.45, 0.0, 0.12, 0.07], [-0.6, 0.0, 0.065, 0.038], [-0.68, 0.0, 0.05, 0.025]];
  const parts = [K.part(K.spine(st, 8), { p: P.BODY })];
  // tail fan, forked, swaying about the peduncle
  parts.push(K.part(fan([-0.64, 0, 0], [[-0.66, 0.05, 0], [-0.86, 0.22, 0], [-0.97, 0.27, 0], [-0.88, 0.04, 0], [-0.88, -0.04, 0], [-0.97, -0.26, 0], [-0.86, -0.21, 0], [-0.66, -0.05, 0]]),
    { p: P.TAIL, pivot: [-0.64, 0, 0], role: R.ACCENT }));
  // dorsal, anal, pectoral and pelvic fins
  parts.push(K.part(fan([0.0, 0.2, 0], [[0.12, 0.24, 0], [0.06, 0.43, 0], [-0.04, 0.39, 0], [-0.2, 0.2, 0]]), { role: R.ACCENT }));
  parts.push(K.part(fan([-0.33, -0.12, 0], [[-0.22, -0.15, 0], [-0.33, -0.3, 0], [-0.44, -0.26, 0], [-0.48, -0.09, 0]]), { role: R.ACCENT }));
  for (const s of [1, -1]) {
    parts.push(K.part(quad([0.36, -0.07, s * 0.12], [0.34, -0.12, s * 0.11], [0.12, -0.2, s * 0.25], [0.16, -0.1, s * 0.24]), { role: R.ACCENT }));
    parts.push(K.part(tri([0.0, -0.22, s * 0.05], [-0.08, -0.23, s * 0.04], [-0.17, -0.3, s * 0.12]), { role: R.ACCENT }));
    // eye: a pale ring with a dark pupil
    parts.push(K.part(K.ellipsoid(0.055, 0.055, 0.03, 0.52, 0.05, s * 0.095, 6), { role: R.PALE }));
    parts.push(K.part(K.ellipsoid(0.034, 0.034, 0.02, 0.535, 0.05, s * 0.115, 5), { role: R.EYE }));
  }
  // dark lateral stripe and a gill line
  parts.push(...flankBand(st, 0.42, -0.6, 7, 0.0, 0.022).map((g) => K.part(g, { role: R.DARK })));
  parts.push(...flankBand(st, 0.42, 0.38, 1, -0.01, 0.14, 4).map((g) => K.part(g, { role: R.ACCENT })));
  return parts;
}

function pike(K, sp) {
  const { P, R } = K;
  // long torpedo body; the front is the flat, wide upper bill (duckbill snout)
  const st = [[1.46, 0.035, 0.012, 0.045], [1.34, 0.04, 0.03, 0.075], [1.12, 0.05, 0.06, 0.1], [0.92, 0.04, 0.13, 0.13],
              [0.62, 0.02, 0.19, 0.16], [0.2, 0.01, 0.22, 0.17], [-0.3, 0.0, 0.21, 0.155], [-0.8, 0.0, 0.16, 0.115],
              [-1.15, 0.0, 0.085, 0.065], [-1.3, 0.0, 0.055, 0.04], [-1.38, 0.0, 0.045, 0.03]];
  const parts = [K.part(K.spine(st, 10), { p: P.BODY })];
  // lower jaw: a flat spoon hinged under the cheek, opening downward on a strike
  const hinge = [0.88, -0.05, 0];
  parts.push(K.part(K.spine([[1.44, 0.0, 0.012, 0.04], [1.3, -0.005, 0.025, 0.065], [1.1, -0.02, 0.04, 0.085], [0.88, -0.05, 0.05, 0.1]], 6),
    { p: P.JAW, pivot: hinge, role: R.BODY }));
  // teeth along both jaws (show when the mouth opens)
  for (const s of [1, -1]) for (const x of [1.36, 1.22, 1.08]) {
    const [, , hw] = bodyAt([[1.44, 0, 0.012, 0.04], [1.3, 0, 0.025, 0.065], [1.1, 0, 0.04, 0.085], [0.88, 0, 0.05, 0.1]], x);
    parts.push(K.part(tri([x + 0.02, 0.0, s * hw * 0.85], [x - 0.02, 0.0, s * hw * 0.85], [x, 0.045, s * hw * 0.8]), { p: P.JAW, pivot: hinge, role: R.HORN }));
  }
  // tail fan (P.TAIL), dorsal and anal fins set far back like a real pike, pectorals and pelvics
  parts.push(K.part(fan([-1.34, 0, 0], [[-1.36, 0.05, 0], [-1.52, 0.2, 0], [-1.64, 0.27, 0], [-1.56, 0.03, 0], [-1.56, -0.03, 0], [-1.64, -0.26, 0], [-1.52, -0.19, 0], [-1.36, -0.05, 0]]),
    { p: P.TAIL, pivot: [-1.34, 0, 0], role: R.ACCENT }));
  parts.push(K.part(fan([-0.82, 0.14, 0], [[-0.62, 0.18, 0], [-0.74, 0.36, 0], [-0.95, 0.38, 0], [-1.12, 0.1, 0]]), { role: R.ACCENT }));
  parts.push(K.part(fan([-0.86, -0.13, 0], [[-0.68, -0.16, 0], [-0.8, -0.33, 0], [-0.98, -0.33, 0], [-1.12, -0.09, 0]]), { role: R.ACCENT }));
  for (const s of [1, -1]) {
    parts.push(K.part(quad([0.66, -0.11, s * 0.13], [0.62, -0.16, s * 0.12], [0.38, -0.22, s * 0.26], [0.42, -0.12, s * 0.25]), { role: R.ACCENT }));
    parts.push(K.part(quad([-0.3, -0.18, s * 0.07], [-0.36, -0.2, s * 0.06], [-0.55, -0.27, s * 0.15], [-0.5, -0.2, s * 0.15]), { role: R.ACCENT }));
    // eyes high on the head, looking forward-up (ambush)
    parts.push(K.part(K.ellipsoid(0.06, 0.05, 0.035, 0.96, 0.11, s * 0.095, 6), { role: R.PALE }));
    parts.push(K.part(K.ellipsoid(0.035, 0.032, 0.02, 0.975, 0.115, s * 0.118, 5), { role: R.EYE }));
  }
  // rows of pale bean-shaped spots on the flanks
  for (const [x, dy] of [[0.45, 0.06], [0.2, -0.03], [0.05, 0.08], [-0.15, -0.02], [-0.32, 0.08], [-0.5, -0.02], [-0.68, 0.06], [-0.85, -0.01]])
    parts.push(...flankBand(st, x + 0.06, x - 0.06, 1, dy, 0.025).map((g) => K.part(g, { role: R.PALE })));
  // dark gill cover line and back stripe
  parts.push(...flankBand(st, 0.76, 0.71, 1, -0.02, 0.14, 4).map((g) => K.part(g, { role: R.DARK })));
  return parts;
}

// ------------------------------------------------------------------ models

export const MODELS = {
  16: minnow,
  17: pike,
  // Hummers: bee-like pollinators hovering at different heights, wings caught at different strokes
  5: (K) => scatter(K, 8, 1.05, 0.25, 1.35, 5, 0.4).flatMap((m) =>
    place(K, hummer(K, K.R, -0.6 + m.u * 1.8), m, m.yaw * 1.6, (m.v - 0.5) * 0.4, 1.25)),
  // Chewers: grasshoppers, most on the ground among the leaves, some mid-hop with wings spread
  6: (K) => scatter(K, 8, 1.2, 0.0, 0.8, 6, 0.42).flatMap((m) => {
    const flying = m.c[1] > 0.25;
    if (!flying) m.c[1] = 0;
    return place(K, chewer(K, K.R, flying), m, m.yaw * 1.4, flying ? -0.25 : 0, 1.3);
  }),
  // Delvers: beetles and earthworms crawling on the soil
  7: (K) => scatter(K, 10, 1.15, 0, 0, 7, 0.4).flatMap((m) =>
    place(K, m.i % 2 ? beetle(K, K.R) : worm(K, K.R, m.u), m, m.yaw * 3, 0, 1.25)),
  // Bloodflies: a loose cloud of biting mosquito-like flies
  14: (K) => scatter(K, 9, 1.0, 0.25, 1.15, 14, 0.32).flatMap((m) =>
    place(K, bloodfly(K, K.R, -0.3 + m.u * 1.4), m, m.yaw * 2.5, (m.v - 0.5) * 0.5, 1.15)),
  // Siltmites: round water mites drifting in a flat cloud (drawn semi-transparent)
  15: (K) => scatter(K, 9, 1.35, 0.1, 0.38, 15, 0.4).flatMap((m) =>
    place(K, siltmite(K, K.R), m, m.yaw * 6, (m.v - 0.5) * 0.6, 1.3)),
};

export const MOTION = {
  16: { wave: 0.13, stride: 7, tailAmp: 0.5, headDip: 0 },
  17: { wave: 0.07, stride: 4, tailAmp: 0.35, headDip: 0 },
  5: { jitter: 0.1, hover: 0.08 },
  6: { jitter: 0.06, hover: 0.04 },
  7: { jitter: 0.035, hover: 0 },
  14: { jitter: 0.14, hover: 0.1 },
  15: { jitter: 0.07, wave: 0.04, hover: 0.03 },
};
