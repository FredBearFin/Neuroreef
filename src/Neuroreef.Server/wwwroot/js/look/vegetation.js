// Plants and rocks (display only), drawn from the FIELDS frame and nothing else: a tree where the sim has tree cover,
// a bush where it has shrubs, grass tufts where it has grass, never a plant where it has none.
//
// - Trees: six instanced models (spruce, pine, round broadleaf, tall broadleaf, umbrella, bare broadleaf), each with a
//   cheap far model. Which one stands in a field cell follows the sim's plants there: the founding lineage of the
//   plant with most cover ('flora': each lineage keeps its own growth form), the air temperature (cold: conifers),
//   drought tolerance (dry and warm: umbrella trees), the leafy share of the cover ('leaf-on': a broadleaf drops its
//   leaves, yellowing on the way), the tallest plant ('plant height', 5 cm..40 m: the trees' size), frost (snow on
//   the crowns) and fire (charred).
// - Bushes: round, berried (where the sim's fruit is fleshy and there is fruit) or thorny scrub (thorns).
// - Grass tufts: only round the camera, as dense as the grass layer, yellowing as the soil dries; reeds instead where
//   the hydrology's 'wetland' layer (when sent) has marsh or swamp (the ground paints its peat and pools).
// - Rocks: on steep slopes and the high bare ground, sparse where plants cover the ground.
// Sizes (2026-11-01): every plant at its true size in metres (the sim's plant height; a shrub at most 4 m, a grass
// tuft half a metre, boulders a few metres; no magnification since 2026-11-04), and as many to a field cell as its cover fits crowns of that size; the
// thinning with distance within a budget keeps the near ones and fewer, a little bigger, far away.

import * as THREE from 'three';

const TREE_NEAR = 70, BUSH_NEAR = 60, TREE_BUDGET = 40000, SHRUB_BUDGET = 14000;   // near: within this many plant heights
const NEAR_CAP = 9000, FAR_CAP = 24000, SHRUB_CAP = 16000, ROCK_CAP = 6000, GRASS_CAP = 22000, FLAME_CAP = 1500;
const TREES_MAX = 600, SHRUBS_MAX = 800;    // most trees and shrubs drawn in one field cell (full cover of small crowns)
const GRASS_M = 0.8, GRASS_STEP_M = 0.7;    // a grass tuft model is 0.8 m tall; tufts stand ~0.7 m apart ...
const GRASS_FINE_M = 9;                     // ... and twice as densely within this many metres of the camera (eye level)
const ROCK_M = 1.6;                         // a boulder's mean size (m); drawn within ROCK_REACH of them
const ROCK_REACH = 250, FLAME_M = 2.5;
const H_LO = -1.3, H_SPAN = 2.9;             // plant height layer: log10 m = H_LO + H_SPAN x value (Constants.cs)
const FOUNDERS_GUESS = 16;
const RELIEF_U = 8000 * 4 / 218;            // world3d.js RELIEF: H_RELIEF at true scale (units)

// growth forms
const SPRUCE = 0, PINE = 1, OAK = 2, POPLAR = 3, UMBRELLA = 4, BARE = 5, NFORMS = 6;
const BUSH = 0, BERRY = 1, THORN = 2;

// ------------------------------------------------------------------ geometry
//
// Each model: position, normal, color, aFol = (foliage 0/1, sway weight 0..1). Foliage normals point away from the
// crown's centre (a soft, rounded crown in the light instead of facets).

function part(geo, color, foliage, centre) {
  const g = geo.index ? geo.toNonIndexed() : geo;
  const n = g.attributes.position.count, pos = g.attributes.position.array;
  g.computeVertexNormals();
  if (centre) {
    const nor = g.attributes.normal.array;
    for (let i = 0; i < n; i++) {
      let x = pos[i * 3] - centre[0], y = (pos[i * 3 + 1] - centre[1]) * 1.3, z = pos[i * 3 + 2] - centre[2];
      const l = Math.hypot(x, y, z) || 1;
      // a blend of the face's own normal and the crown's: soft but not without form
      x = 0.65 * x / l + 0.35 * nor[i * 3]; y = 0.65 * y / l + 0.35 * nor[i * 3 + 1]; z = 0.65 * z / l + 0.35 * nor[i * 3 + 2];
      const m = Math.hypot(x, y, z) || 1; nor[i * 3] = x / m; nor[i * 3 + 1] = y / m; nor[i * 3 + 2] = z / m;
    }
  }
  const col = new Float32Array(n * 3), fol = new Float32Array(n * 2), c = new THREE.Color(color);
  for (let i = 0; i < n; i++) {
    // a little variation in shade over the crown (lighter at the top), so it is not one flat colour
    const sh = foliage ? 0.82 + 0.3 * Math.min(1, Math.max(0, pos[i * 3 + 1])) + ((i * 7919) % 13) / 130 : 1;
    col[i * 3] = c.r * sh; col[i * 3 + 1] = c.g * sh; col[i * 3 + 2] = c.b * sh;
    fol[i * 2] = foliage ? 1 : 0; fol[i * 2 + 1] = Math.max(0, pos[i * 3 + 1]);
  }
  g.setAttribute('color', new THREE.BufferAttribute(col, 3));
  g.setAttribute('aFol', new THREE.BufferAttribute(fol, 2));
  return g;
}

function mergeParts(parts) {
  let n = 0; for (const p of parts) n += p.attributes.position.count;
  const out = new THREE.BufferGeometry();
  for (const [name, size] of [['position', 3], ['normal', 3], ['color', 3], ['aFol', 2]]) {
    const a = new Float32Array(n * size); let o = 0;
    for (const p of parts) { a.set(p.attributes[name].array, o); o += p.attributes[name].array.length; }
    out.setAttribute(name, new THREE.BufferAttribute(a, size));
  }
  out.computeBoundingSphere();
  return out;
}

function rng(seed) { let s = seed >>> 0; return () => { s = hash1(s + 0x9e3779b9); return (s & 0xffffff) / 0x1000000; }; }

/** A lumpy ball (an icosahedron with its vertices pushed in and out). */
function blob(r, detail, seed, sx = 1, sy = 1, sz = 1, rough = 0.18) {
  const g = new THREE.IcosahedronGeometry(r, detail), p = g.attributes.position, R = rng(seed), cache = new Map();
  for (let i = 0; i < p.count; i++) {
    const key = `${p.getX(i).toFixed(4)},${p.getY(i).toFixed(4)},${p.getZ(i).toFixed(4)}`;
    let k = cache.get(key); if (k === undefined) { k = 1 + (R() - 0.5) * 2 * rough; cache.set(key, k); }
    p.setXYZ(i, p.getX(i) * k * sx, p.getY(i) * k * sy, p.getZ(i) * k * sz);
  }
  return g;
}

function cyl(r0, r1, h, seg, x, y, z, tiltX = 0, tiltZ = 0) {
  const g = new THREE.CylinderGeometry(r1, r0, h, seg, 1, true); g.translate(0, h / 2, 0);
  if (tiltX) g.rotateX(tiltX); if (tiltZ) g.rotateZ(tiltZ);
  g.translate(x, y, z); return g;
}

const BARK = 0x5b4433, BARK_GREY = 0x8a8178, BARK_BIRCH = 0xb9b2a4;
const NEEDLE = 0x1d4a2a, NEEDLE2 = 0x27593a, PINE_G = 0x2f5a2c, LEAF = 0x376a2a, LEAF2 = 0x44782f, LEAF_DRY = 0x66733a;

/**
 * Tree models, each 1 unit tall (the base at y = 0). [near, far] per growth form. Proportions (2026-11-01 QA) from
 * forest-grown trees: crown width / height ~0.25-0.35 for a spruce, ~0.35-0.4 for a pine, ~0.55-0.65 for an oak or a
 * beech, ~0.3 for a poplar, ~1-1.3 for an acacia (open grown); trunk diameter at breast height 2-3.5 % of the height
 * (drawn a little thicker, ~4 %, to stay visible). Before, broadleaves were a ball as wide as the tree is tall on a
 * stick 12 % of the height thick: lollipops.
 */
function treeModels() {
  const near = [], far = [];
  // spruce: a narrow stack of drooping tiers, down to near the ground
  {
    const ps = [part(cyl(0.02, 0.008, 0.6, 5, 0, 0, 0), BARK, false)];
    const tiers = [[0.08, 0.16, 0.34], [0.26, 0.14, 0.30], [0.44, 0.115, 0.26], [0.61, 0.085, 0.22], [0.76, 0.055, 0.24]];
    tiers.forEach(([y, r, h], k) => { const c = new THREE.ConeGeometry(r, h, 8, 1, true); c.translate(0, y + h / 2, 0); c.rotateY(k * 0.7);
      ps.push(part(c, k % 2 ? NEEDLE2 : NEEDLE, true, [0, 0.5, 0])); });
    near[SPRUCE] = mergeParts(ps);
    const f = new THREE.ConeGeometry(0.155, 0.94, 6, 1, true); f.translate(0, 0.53, 0);
    far[SPRUCE] = mergeParts([part(f, NEEDLE, true, [0, 0.45, 0])]);
  }
  // pine: a tall bare trunk and a few flat clumps of needles in the top third
  {
    const ps = [part(cyl(0.02, 0.01, 0.86, 5, 0, 0, 0), 0x6b4a33, false)];
    const cl = [[0, 0.86, 0, 0.13], [0.08, 0.77, 0.03, 0.10], [-0.08, 0.74, -0.04, 0.10], [0.02, 0.68, -0.08, 0.085], [-0.03, 0.94, 0.04, 0.08]];
    cl.forEach(([x, y, z, r], k) => { const b = blob(r, 1, 30 + k, 1.3, 0.6, 1.3, 0.25); b.translate(x, y, z); ps.push(part(b, k % 2 ? PINE_G : NEEDLE2, true, [0, 0.8, 0])); });
    ps.push(part(cyl(0.008, 0.005, 0.12, 4, 0, 0.68, 0, 0, 1.0), 0x6b4a33, false));
    ps.push(part(cyl(0.008, 0.005, 0.11, 4, 0, 0.66, 0, 0.9, -0.3), 0x6b4a33, false));
    near[PINE] = mergeParts(ps);
    const f = blob(0.17, 0, 5, 1.1, 0.9, 1.1, 0.1); f.translate(0, 0.82, 0);
    far[PINE] = mergeParts([part(cyl(0.022, 0.012, 0.72, 4, 0, 0, 0), 0x6b4a33, false), part(f, PINE_G, true, [0, 0.82, 0])]);
  }
  // round broadleaf (an oak or a beech): a clear bole to ~0.35 of its height, limbs, a crown of overlapping clumps
  // ~0.6 of the height wide and ~0.65 deep
  {
    const ps = [part(cyl(0.022, 0.014, 0.5, 6, 0, 0, 0), BARK, false),
      part(cyl(0.012, 0.007, 0.25, 5, 0, 0.36, 0, 0.55, 0.3), BARK, false), part(cyl(0.012, 0.007, 0.25, 5, 0, 0.36, 0, -0.5, -0.4), BARK, false)];
    const cl = [[0, 0.74, 0, 0.20], [0.14, 0.62, 0.04, 0.15], [-0.13, 0.64, -0.05, 0.16], [0.03, 0.60, 0.14, 0.15], [-0.04, 0.57, -0.14, 0.14],
                [0.08, 0.84, -0.06, 0.13], [-0.07, 0.86, 0.07, 0.12], [0.0, 0.48, 0.0, 0.13]];
    cl.forEach(([x, y, z, r], k) => { const b = blob(r, 1, 50 + k, 1, 0.9, 1, 0.22); b.translate(x, y, z); ps.push(part(b, k % 2 ? LEAF2 : LEAF, true, [0, 0.68, 0])); });
    near[OAK] = mergeParts(ps);
    const f = blob(0.3, 1, 7, 1, 1.08, 1, 0.16); f.translate(0, 0.67, 0);
    far[OAK] = mergeParts([part(cyl(0.022, 0.014, 0.42, 4, 0, 0, 0), BARK, false), part(f, LEAF, true, [0, 0.67, 0])]);
  }
  // tall broadleaf (a poplar or a birch): a pale trunk and a narrow crown
  {
    const ps = [part(cyl(0.018, 0.01, 0.6, 6, 0, 0, 0), BARK_BIRCH, false)];
    const cl = [[0, 0.42, 0, 0.13], [0.02, 0.58, 0.03, 0.13], [-0.02, 0.74, -0.02, 0.11], [0.0, 0.89, 0.01, 0.08], [0.04, 0.50, -0.06, 0.10]];
    cl.forEach(([x, y, z, r], k) => { const b = blob(r, 1, 70 + k, 1, 1.35, 1, 0.2); b.translate(x, y, z); ps.push(part(b, k % 2 ? LEAF : LEAF2, true, [0, 0.66, 0])); });
    near[POPLAR] = mergeParts(ps);
    const f = blob(0.15, 0, 9, 1, 2.6, 1, 0.08); f.translate(0, 0.6, 0);
    far[POPLAR] = mergeParts([part(cyl(0.018, 0.012, 0.3, 4, 0, 0, 0), BARK_BIRCH, false), part(f, LEAF, true, [0, 0.62, 0])]);
  }
  // umbrella (an acacia): a forked trunk under a flat, wide crown
  {
    const ps = [part(cyl(0.03, 0.02, 0.45, 5, 0, 0, 0), 0x6e5641, false),
      part(cyl(0.018, 0.012, 0.45, 5, 0, 0.4, 0, 0.0, 0.55), 0x6e5641, false), part(cyl(0.018, 0.012, 0.42, 5, 0, 0.4, 0, 0.45, -0.35), 0x6e5641, false)];
    const cl = [[0, 0.88, 0, 0.36], [0.28, 0.82, 0.1, 0.24], [-0.3, 0.84, -0.06, 0.25], [0.04, 0.83, 0.3, 0.22], [-0.06, 0.82, -0.3, 0.22]];
    cl.forEach(([x, y, z, r], k) => { const b = blob(r, 1, 90 + k, 1.2, 0.28, 1.2, 0.25); b.translate(x, y, z); ps.push(part(b, k % 2 ? LEAF_DRY : 0x587a2c, true, [0, 0.8, 0])); });
    near[UMBRELLA] = mergeParts(ps);
    const f = blob(0.5, 0, 13, 1, 0.22, 1, 0.1); f.translate(0, 0.86, 0);
    far[UMBRELLA] = mergeParts([part(cyl(0.03, 0.02, 0.8, 4, 0, 0, 0), 0x6e5641, false), part(f, 0x587a2c, true, [0, 0.86, 0])]);
  }
  // bare broadleaf (leaves dropped): trunk, limbs and twigs
  {
    const ps = [part(cyl(0.022, 0.014, 0.5, 6, 0, 0, 0), BARK_GREY, false)];
    const R = rng(21);
    for (let k = 0; k < 7; k++) {
      const a = k * 2.4, t = 0.35 + 0.35 * R(), y0 = 0.35 + 0.15 * R();
      const g = cyl(0.012, 0.004, t, 4, 0, 0, 0, 0, 0.5 + 0.35 * R()); g.rotateY(a); g.translate(0, y0, 0);
      ps.push(part(g, BARK_GREY, false));
      const tw = cyl(0.006, 0.002, 0.2, 3, 0, 0, 0, 0, 0.2 + 0.4 * R()); tw.rotateY(a + 0.6); tw.translate(Math.cos(-a) * t * 0.35, y0 + t * 0.75, Math.sin(-a) * t * 0.35);
      ps.push(part(tw, BARK_GREY, false));
    }
    near[BARE] = mergeParts(ps);
    // far: a trunk and a thin fan of limbs (a few sticks read better than a solid cone)
    const fp = [part(cyl(0.022, 0.014, 0.45, 4, 0, 0, 0), 0x5e554d, false)];
    for (let k = 0; k < 4; k++) { const g = cyl(0.012, 0.004, 0.5, 3, 0, 0, 0, 0, 0.55); g.rotateY(k * 1.6); g.translate(0, 0.38, 0); fp.push(part(g, 0x5e554d, false)); }
    far[BARE] = mergeParts(fp);
  }
  return { near, far };
}

/** Bushes, 1 unit tall and about 1.4 wide. */
function bushModels() {
  const m = [];
  const clump = (seed, colors, n, flat = 0.8, spread = 0.38) => {
    const R = rng(seed), ps = [];
    for (let k = 0; k < n; k++) {
      const a = k / n * Math.PI * 2 + R(), d = spread * (0.4 + 0.6 * R()), r = 0.3 + 0.18 * R();
      const b = blob(r, 1, seed + k, 1, flat, 1, 0.22); b.translate(Math.cos(a) * d, r * flat * 0.9 + 0.05 * R(), Math.sin(a) * d);
      ps.push(part(b, colors[k % colors.length], true, [0, 0.25, 0]));
    }
    const top = blob(0.36, 1, seed + 99, 1, flat, 1, 0.2); top.translate(0, 0.5, 0); ps.push(part(top, colors[0], true, [0, 0.25, 0]));
    return ps;
  };
  m[BUSH] = mergeParts(clump(200, [0x4a6e2a, 0x55782f, 0x405f26], 3));
  const berries = clump(300, [0x3f6527, 0x4a7030], 3);
  const R = rng(5);
  for (let k = 0; k < 14; k++) {
    const a = R() * Math.PI * 2, y = 0.2 + 0.45 * R(), r = 0.42 + 0.12 * R();
    const b = new THREE.IcosahedronGeometry(0.055, 0); b.translate(Math.cos(a) * r, y, Math.sin(a) * r);
    berries.push(part(b, k % 3 ? 0xb3122a : 0x5a1a6e, false));
  }
  m[BERRY] = mergeParts(berries);
  // thorny scrub: sparse, angular, grey-green, with spines
  {
    const ps = clump(400, [0x5c6a3a, 0x66704a], 3, 0.7, 0.42);
    const R2 = rng(8);
    for (let k = 0; k < 10; k++) {
      const c = new THREE.ConeGeometry(0.035, 0.38, 3); c.translate(0, 0.19, 0);
      c.rotateZ(0.6 + 0.8 * R2()); c.rotateY(R2() * Math.PI * 2); c.translate(0, 0.25 + 0.3 * R2(), 0);
      ps.push(part(c, 0x6e5a3a, false));
    }
    m[THORN] = mergeParts(ps);
  }
  // the far bush: one lumpy ball (20 triangles)
  const f = blob(0.62, 0, 500, 1, 0.75, 1, 0.15); f.translate(0, 0.42, 0);
  m[3] = mergeParts([part(f, 0x4a6e2a, true, [0, 0.3, 0])]);
  return m;
}

/** A grass tuft: seven bent blades, 1 unit tall, a darker base and lighter tips. */
function grassModel() {
  const P = [], C = [], N = [], F = [], R = rng(77);
  for (let k = 0; k < 7; k++) {
    const a = R() * Math.PI * 2, lean = 0.15 + 0.35 * R(), h = 0.6 + 0.4 * R(), w = 0.035 + 0.02 * R();
    const ox = (R() - 0.5) * 0.18, oz = (R() - 0.5) * 0.18, ca = Math.cos(a), sa = Math.sin(a);
    const pt = (s, side) => {   // s along the blade, side -1/1
      const x = ox + ca * lean * s * s + (-sa) * w * side * (1 - s), y = h * s, z = oz + sa * lean * s * s + ca * w * side * (1 - s);
      return [x, y, z];
    };
    const segs = [[0, -1], [0, 1], [0.5, -1], [0.5, 1], [1, 0]];
    const v = segs.map(([s, sd]) => pt(s, sd));
    const tri = (i, j, l) => { for (const q of [i, j, l]) { P.push(...v[q]); const s = segs[q][0]; C.push(0.55 + 0.6 * s, 0.6 + 0.5 * s, 0.45 + 0.4 * s); N.push(ca * 0.3, 0.9, sa * 0.3); F.push(1, v[q][1]); } };
    tri(0, 1, 2); tri(1, 3, 2); tri(2, 3, 4);
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.BufferAttribute(new Float32Array(P), 3));
  g.setAttribute('normal', new THREE.BufferAttribute(new Float32Array(N), 3));
  g.setAttribute('color', new THREE.BufferAttribute(new Float32Array(C), 3));
  g.setAttribute('aFol', new THREE.BufferAttribute(new Float32Array(F), 2));
  g.computeBoundingSphere();
  return g;
}

/** Rocks: three weathered shapes, flat-shaded, about 1 unit across. */
function rockModels() {
  const out = [];
  for (let k = 0; k < 3; k++) {
    const g = blob(0.5, 1, 600 + k * 17, [1.2, 1.0, 1.5][k], [0.62, 0.8, 0.5][k], [0.9, 1.1, 0.8][k], 0.28);
    // cut flat underneath-ish and a few planar facets: push the bottom up so it sits on the ground
    const p = g.attributes.position;
    for (let i = 0; i < p.count; i++) if (p.getY(i) < -0.12) p.setY(i, -0.12 + (p.getY(i) + 0.12) * 0.3);
    const ng = g.index ? g.toNonIndexed() : g; ng.computeVertexNormals();   // non-indexed: flat facets
    const n = ng.attributes.position.count, col = new Float32Array(n * 3), fol = new Float32Array(n * 2), R = rng(k + 3);
    for (let i = 0; i < n; i++) fol[i * 2] = 1;   // tinted per instance, no sway
    for (let i = 0; i < n; i += 3) {
      const s = 0.85 + 0.25 * R(), up = ng.attributes.normal.getY(i);
      // lichen and moss on the faces turned up
      const moss = Math.max(0, up - 0.55) * 0.9 * R();
      for (let j = 0; j < 3; j++) { col[(i + j) * 3] = (0.36 - 0.14 * moss) * s; col[(i + j) * 3 + 1] = (0.345 + 0.02 * moss) * s; col[(i + j) * 3 + 2] = (0.32 - 0.12 * moss) * s; }
    }
    ng.setAttribute('color', new THREE.BufferAttribute(col, 3)); ng.setAttribute('aFol', new THREE.BufferAttribute(fol, 2));
    out.push(ng);
  }
  return out;
}

// ------------------------------------------------------------------ the material: Lambert with vertex colours, a
// per-instance tint of the foliage only (the bark keeps its colour), and wind in the crowns

const U_TIME = { value: 0 };
// the line of sight (2026-11-04): grass between the camera and the animal it follows (or just ahead of a camera at ground
// level) is pressed down, as games do, so true-size grass taller than a 10 cm animal never buries the view. A capsule
// from uSightA to uSightB of radius uSightR (0: off); a tuft whose foot is inside it is drawn flattened.
export const U_SIGHT = { uSightA: { value: new THREE.Vector3() }, uSightB: { value: new THREE.Vector3() }, uSightR: { value: 0 } };

// fade: a plant the camera is inside dissolves (a screen-door stipple), so the camera never looks out through the
// inside of a crown (their shadows stay). fade is the plant's reach from its axis, a share of its height (2026-11-02:
// it was the distance to the plant's FOOT under 0.9 of its height, so from the ground every tree within 0.9 of its
// height, 18 m for a 20 m tree, and every bush and rock within a body's length or so was stippled to a dark, dithered
// ghost, and the animals among them seemed stippled too, or vanished: docs/art/scale3/explore-default.png. Now only
// within the plant's own volume: the distance to the segment from its foot to its top under fade x its height.)
function plantMaterial(wrap, { side = THREE.FrontSide, flat = false, sway = 1, fade = 0, sight = false } = {}) {
  const mat = new THREE.MeshLambertMaterial({ vertexColors: true, side, flatShading: flat });
  wrap(mat);
  const inner = mat.onBeforeCompile;
  mat.onBeforeCompile = (sh, r) => {
    sh.uniforms.uPlantTime = U_TIME;
    if (sight) Object.assign(sh.uniforms, U_SIGHT);
    sh.vertexShader = 'attribute vec2 aFol;\nuniform float uPlantTime;\nvarying float vNearFade;\n' + (sight ? 'uniform vec3 uSightA, uSightB;\nuniform float uSightR;\n' : '') + sh.vertexShader
      .replace('#include <color_vertex>', `
        vColor = vec3(1.0);
        #ifdef USE_COLOR
          vColor *= color;
        #endif
        #ifdef USE_INSTANCING_COLOR
          vColor = mix(vColor, vColor * instanceColor, aFol.x);
        #endif`)
      .replace('#include <begin_vertex>', `#include <begin_vertex>
        {
          vec2 ip = vec2(0.0);
          #ifdef USE_INSTANCING
            ip = instanceMatrix[3].xz;
          #endif
          float ph = dot(ip, vec2(0.11, 0.07));
          float sw = aFol.y * aFol.y * ${(0.035 * sway).toFixed(4)};
          transformed.x += sw * (sin(uPlantTime * 1.3 + ph) + 0.4 * sin(uPlantTime * 3.1 + ph * 2.3));
          transformed.z += sw * 0.6 * sin(uPlantTime * 1.1 + ph * 1.7);
          vNearFade = 1.0;
          #ifdef USE_INSTANCING
            vec3 io = (modelMatrix * vec4(instanceMatrix[3].xyz, 1.0)).xyz;
            float sz = length(instanceMatrix[1].xyz);
            vec3 rel = cameraPosition - io;
            float axD = length(vec2(length(rel.xz), rel.y - clamp(rel.y, 0.0, sz)));   // to the axis, foot to top
            vNearFade = smoothstep(${(0.8 * fade).toFixed(3)} * sz + 0.0005, ${fade.toFixed(3)} * sz + 0.001, axD);
            ${sight ? `if (uSightR > 0.0) {
              vec3 ab = uSightB - uSightA, ap = io - uSightA;
              float t = clamp(dot(ap, ab) / max(dot(ab, ab), 1e-9), 0.0, 1.0);
              float d = length(ap - ab * t);
              float k = mix(0.12, 1.0, smoothstep(0.55 * uSightR, uSightR, d));
              transformed.y *= k; transformed.xz *= mix(1.0, k, 0.5);
            }` : ''}
          #endif
        }`);
    if (fade) sh.fragmentShader = 'varying float vNearFade;\n' + sh.fragmentShader.replace('#include <clipping_planes_fragment>', `#include <clipping_planes_fragment>
        if (vNearFade < 0.999) {
          float th = fract(52.9829189 * fract(dot(floor(gl_FragCoord.xy), vec2(0.06711056, 0.00583715))));
          if (vNearFade < th) discard;
        }`);
    inner(sh, r);
  };
  mat.customProgramCacheKey = () => 'neuroreef-plant-' + sway + (flat ? 'f' : '') + (fade ? 'n' + fade : '') + (sight ? 's' : '') + side;
  return mat;
}

// ------------------------------------------------------------------ placement

export class Vegetation {
  /** v: the World3D (world, fields, heightAt, camera, controls, fog); wrap(material): its wrap-display patch. */
  constructor(v, wrap, quality) {
    this.v = v; this.quality = quality;
    const scene = v.scene, shadows = quality.shadows;
    const T = treeModels(), B = bushModels();
    const mk = (geo, mat, cap, cast) => {
      const m = new THREE.InstancedMesh(geo, mat, cap);
      m.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(cap * 3).fill(1), 3);
      m.frustumCulled = false; m.count = 0; m.castShadow = shadows && cast; m.receiveShadow = shadows;
      scene.add(m); return m;
    };
    // (fade: the crown's reach from the axis as a share of the height: a tree's ~0.3, a bush's or a rock's ~0.6)
    const mTree = plantMaterial(wrap, { fade: 0.32 }), mFar = plantMaterial(wrap, { sway: 0 }), mBush = plantMaterial(wrap, { sway: 0.5, fade: 0.65 });
    const mGrass = plantMaterial(wrap, { side: THREE.DoubleSide, sway: 4, sight: true }), mRock = plantMaterial(wrap, { sway: 0, flat: true, fade: 0.6 });
    // near trees: a share of the cap per form (sized to how often each is picked, with slack)
    this.near = T.near.map((g) => mk(g, mTree, NEAR_CAP, true));
    this.far = T.far.map((g) => mk(g, mFar, FAR_CAP, true));
    this.bush = B.slice(0, 3).map((g) => mk(g, mBush, Math.ceil(SHRUB_CAP / 2), true));
    this.bushFar = mk(B[3], mFar, SHRUB_CAP, false);
    this.grass = mk(grassModel(), mGrass, GRASS_CAP, false);
    this.rocks = rockModels().map((g) => mk(g, mRock, Math.ceil(ROCK_CAP / 3), true));
    this.flames = new THREE.InstancedMesh(new THREE.ConeGeometry(1, 3, 5), new THREE.MeshBasicMaterial({ color: 0xff7a1a, transparent: true, opacity: 0.85 }), FLAME_CAP);
    this.flames.material.color.multiplyScalar(2.2);   // fire glows (and blooms) in the linear frame
    wrap(this.flames.material); this.flames.frustumCulled = false; this.flames.count = 0; scene.add(this.flames);
    this._m = new THREE.Matrix4(); this._q = new THREE.Quaternion(); this._p = new THREE.Vector3(); this._s = new THREE.Vector3(); this._c = new THREE.Color();
    this._e = new THREE.Euler();
    this.lastMs = 0; this.grassMs = 0;
  }

  setTime(t) { U_TIME.value = t; }

  _layers() {
    const v = this.v, f = v.fields, d = f.d, L = (n) => { const i = v._layer(n); return i >= 0 ? f.data.subarray(i * d * d, (i + 1) * d * d) : null; };
    return { d, T: L('tree'), S: L('shrub'), G: L('grass'), F: L('fire'), H: L('plant height'), LO: L('leaf-on'), DT: L('drought tolerance'),
             FL: L('flora'), TH: L('thorns'), FF: L('fleshy fruit'), FR: L('fruit'), TP: L('temp'), FZ: L('frozen'), M: L('moisture'), SN: L('snow'), WL: L('wetland') };
  }

  /** The trees, bushes, rocks and fires in view (as world3d.js's _plants did; the same budget and thinning). */
  plant(force) {
    const v = this.v, f = v.fields; if (!f || !v.world || !v.tiles) return;
    const c = v.controls.target, cam = v.camera.position, W = v.W;
    const camD = cam.distanceTo(c);
    const sc = v.scale, upm = sc.upm, mpu = sc.mpu;
    this._grass(force);
    // the view moved less than a share of its distance: keep the plants (and re-plant at most ~4 times a second)
    const now = performance.now();
    if (!force && this._plantC && Math.hypot(c.x - this._plantC.x, c.z - this._plantC.z) < Math.max(0.01, camD * 0.12) &&
        cam.distanceTo(this._plantCam) < Math.max(0.01, camD * 0.15)) return;
    if (!force && this._plantT && now - this._plantT < 250) return;
    this._plantT = now;
    const t0 = performance.now();
    this._plantC = c.clone(); this._plantCam = cam.clone();
    const Ly = this._layers(), d = Ly.d, cs = W / d;
    const fogFar = v.scene.fog.far;
    // pixels per unit at unit distance: a plant drawn under ~1.5 px tall is left to the ground's cover colour (2026-11-01
    // QA: the overview's sub-pixel trees were a black speckle over the whole map, flickering as they aliased)
    const pxU = v.renderer.domElement.height / (2 * Math.tan(v.camera.fov * Math.PI / 360)), MIN_PX = 1.5;
    const E = v._wrapS > 0.5 ? W / 2 : Math.min(fogFar, 0.8 * W);
    const r0 = Math.floor(E / cs), side = 2 * r0 + 1;
    const ccx = Math.floor(c.x / cs), ccy = Math.floor(c.z / cs);
    if (!this._pd || this._pd.length < side * side) { this._pd = new Float32Array(side * side); this._pt = new Float32Array(side * side); this._ps = new Float32Array(side * side); this._pi = new Int32Array(side * side * 2); }
    const PD = this._pd, PT = this._pt, PS = this._ps, PI = this._pi;
    // how tall the trees and shrubs of a field cell are drawn (units), and how many crowns of that size its cover holds
    const heightM = (fi) => Ly.H && Ly.H[fi] ? Math.pow(10, H_LO + H_SPAN * Ly.H[fi] / 255) : 12;
    const treeU = (hM) => Math.min(45, Math.max(3, hM)) * upm, shrubU = (hM) => Math.min(4, Math.max(0.4, hM)) * upm;
    const fits = (crown, max) => Math.min(max, Math.max(3, 0.7 * (cs / crown) ** 2));
    let m = 0;
    for (let dy = -r0; dy <= r0; dy++) for (let dx = -r0; dx <= r0; dx++) {
      const gx = ccx + dx, gy = ccy + dy, px = (gx + 0.5) * cs, pz = (gy + 0.5) * cs;
      if (Math.abs(px - c.x) > E || Math.abs(pz - c.z) > E) continue;
      const dist = Math.hypot(px - cam.x, v.heightAt(px, pz) - cam.y, pz - cam.z);
      if (dist > fogFar) continue;
      const fx = ((gx % d) + d) % d, fy = ((gy % d) + d) % d, fi = fy * d + fx;
      const hM = heightM(fi);
      PD[m] = Math.max(0.01, dist); PT[m] = Ly.T[fi] / 255 * fits(0.45 * treeU(hM), TREES_MAX); PS[m] = Ly.S[fi] / 255 * fits(1.2 * shrubU(hM), SHRUBS_MAX);
      PI[2 * m] = gx; PI[2 * m + 1] = gy; m++;
    }
    const D0t = fitBudget(PD, PT, m, TREE_BUDGET), D0s = fitBudget(PD, PS, m, SHRUB_BUDGET);
    const mat = this._m, q = this._q, p = this._p, s = this._s, col = this._c, eul = this._e;
    const nNear = new Int32Array(NFORMS), nFar = new Int32Array(NFORMS), nB = new Int32Array(3), nR = new Int32Array(3);
    let nBF = 0;
    let nf = 0;
    const world = v.world, fw = world.fw, S = W / fw, H = v.cellH;
    for (let k = 0; k < m; k++) {
      const gx = PI[2 * k], gy = PI[2 * k + 1], fx = ((gx % d) + d) % d, fy = ((gy % d) + d) % d, fi = fy * d + fx, dist = PD[k];
      let hsh = hash2(fx, fy);
      // the plants of this field cell, as the sim has them
      const tempC = Ly.TP ? Ly.TP[fi] / 255 * 90 - 40 : 15, cold = clamp01((8 - tempC) / 18);
      const drought = Ly.DT ? Ly.DT[fi] / 255 : 0.3, leafOn = Ly.LO ? Ly.LO[fi] / 255 : 1;
      // snow on the crowns where the snowpack lies (FIELDS 'snow'); the air below freezing alone is only a rime of frost
      const frost = Math.max(Ly.SN ? Ly.SN[fi] / 255 : 0, Ly.FZ ? 0.3 * Ly.FZ[fi] / 255 : 0);
      const fire = Ly.F[fi] > 0, moist = Ly.M ? Ly.M[fi] / 255 : 0.5;
      const hM = heightM(fi);
      const lineage = Ly.FL && Ly.FL[fi] ? Math.round(Ly.FL[fi] / 255 * (FOUNDERS_GUESS + 1)) : 0;
      const lh = hash1(lineage * 7919 + 13);
      // thinned with distance: fewer, a little bigger, trees far away (as many crowns' worth of cover)
      const keepT = Math.min(1, (D0t / dist) ** 2);
      const ntree = treeU(hM) * Math.min(2, 1 / Math.sqrt(keepT)) * pxU / dist < MIN_PX ? 0 : Math.floor(PT[k] * keepT + (hsh & 255) / 256);
      if (ntree > 0) {
        const grow = Math.min(2, 1 / Math.sqrt(keepT));
        // tree size from the tallest plant, in metres (3..45 m) x the plants' magnification
        const baseH = treeU(hM), treeNear = TREE_NEAR * baseH;
        // the lineage's growth form: each founding lineage leans conifer or broadleaf, the climate decides the rest
        const pCon = clamp01((lh & 1 ? 0.6 : 0.12) + 0.75 * cold - 0.3 * drought);
        const pUmb = (1 - cold) * clamp01((drought - 0.55) * 2.5) * (moist < 0.5 ? 1 : 0.4);
        for (let j = 0; j < ntree; j++) {
          hsh = hash1(hsh);
          const x = (gx + ((hsh & 1023) / 1024)) * cs, y = (gy + (((hsh >> 10) & 1023) / 1024)) * cs;
          if (world.cell[v._cellOf(x, y)] !== 1) continue;
          const u1 = ((hsh >>> 20) & 255) / 256, u2 = ((hsh >>> 12) & 255) / 256;
          let form;
          if (u1 < pCon) form = (lh >> 1) & 1 ? (drought > 0.5 ? PINE : SPRUCE) : (cold > 0.6 ? SPRUCE : PINE);
          else if (u1 < pCon + (1 - pCon) * pUmb) form = UMBRELLA;
          else form = ((lh >> 2) & 3) === 0 || moist > 0.8 ? POPLAR : OAK;
          // a broadleaf without leaves where the sim's cover is leafless (deciduous in the dry season or the cold)
          const deciduous = form === OAK || form === POPLAR || form === UMBRELLA;
          if (deciduous && u2 > leafOn + 0.08) form = BARE;
          const hj = baseH * (0.72 + 0.5 * ((hsh >>> 24) & 15) / 16) * (form === UMBRELLA ? 0.75 : 1);
          // thinned far away, each kept tree stands for several: drawn bigger, so the canopy covers as much ground as
          // the sim's cover, but in its own proportions (it was widened twice as much as it was raised: squat balls)
          const sh = hj * grow, sw = hj * grow * (0.9 + 0.2 * u2);
          p.set(x, v.heightAt(x, y) - 0.03 * sh, y); q.setFromAxisAngle(UP, (hsh & 63) / 10); s.set(sw, sh, sw);
          mat.compose(p, q, s);
          // foliage colour: the lineage's own shade, yellowing as the leaves go, snow in frost, charred in fire
          tint(col, lh, u2, deciduous && form !== BARE ? 1 - leafOn : 0, frost, fire, form);
          if (dist < treeNear && nNear[form] < NEAR_CAP) { const o = this.near[form]; o.setMatrixAt(nNear[form], mat); o.setColorAt(nNear[form]++, col); }
          else if (nFar[form] < FAR_CAP) { const o = this.far[form]; o.setMatrixAt(nFar[form], mat); o.setColorAt(nFar[form]++, col); }
        }
      }
      const keepS = Math.min(1, (D0s / dist) ** 2);
      hsh = hash1(hsh + 17);
      const nsh = shrubU(hM) * Math.min(2, 1 / Math.sqrt(keepS)) * pxU / dist < MIN_PX ? 0 : Math.floor(PS[k] * keepS + ((hsh >> 8) & 255) / 256);
      if (nsh > 0) {
        const grow = Math.min(2, 1 / Math.sqrt(keepS)), bushH = shrubU(hM), bushNear = BUSH_NEAR * bushH;
        const thorny = Ly.TH ? Ly.TH[fi] / 255 : 0, fleshy = Ly.FF && Ly.FR ? (Ly.FF[fi] / 255) * Math.min(1, Ly.FR[fi] / 80) : 0;
        for (let j = 0; j < nsh; j++) {
          hsh = hash1(hsh + 17);
          const x = (gx + ((hsh & 1023) / 1024)) * cs, y = (gy + (((hsh >> 10) & 1023) / 1024)) * cs;
          if (world.cell[v._cellOf(x, y)] !== 1) continue;
          const u = ((hsh >>> 20) & 255) / 256, kind = u < thorny * 0.9 ? THORN : u < thorny * 0.9 + fleshy ? BERRY : BUSH;
          const nearB = dist < bushNear && nB[kind] < this.bush[kind].instanceMatrix.count;
          if (!nearB && nBF >= SHRUB_CAP) continue;
          const sb = (0.75 + ((hsh >>> 24) & 15) / 22) * bushH;
          p.set(x, v.heightAt(x, y) - 0.05 * sb, y); q.setFromAxisAngle(UP, (hsh & 63) / 10); s.set(sb * grow, sb * Math.sqrt(grow), sb * grow);
          mat.compose(p, q, s);
          tint(col, lh, u, 1 - leafOn, frost, fire, -1);
          if (nearB) { this.bush[kind].setMatrixAt(nB[kind], mat); this.bush[kind].setColorAt(nB[kind]++, col); }
          else { if (kind === THORN) col.multiplyScalar(0.85); this.bushFar.setMatrixAt(nBF, mat); this.bushFar.setColorAt(nBF++, col); }
        }
      }
      // rocks: on steep slopes and the highest bare ground, near the camera (not a plant: the ground's own); boulders a
      // few metres across, as many to a field cell as before by the ground they cover
      const rockU = ROCK_M * upm, rockR = Math.min(320, ROCK_REACH * rockU), rockN = Math.min(15, (1 / rockU) ** 2 * 0.15);
      if (dist < rockR) {
        const cx = Math.floor((gx + 0.5) * cs / S), cy = Math.floor((gy + 0.5) * cs / S);
        const at = (x, y) => H[(((y % fw) + fw) % fw) * fw + (((x % fw) + fw) % fw)];
        const sl = Math.hypot(at(cx + 1, cy) - at(cx - 1, cy), at(cx, cy + 1) - at(cx, cy - 1)) / (2 * S);
        // high bare ground: the ground's own rockiness (world3d.js: steep, high or ridge cells; it was the cell's height over
        // the 55-unit relief of before 2026-11-01, so every cell above ~1.9 km counted as the summits)
        const hi = v.ground && v.ground.d.rock ? v.ground.d.rock[(((cy % fw) + fw) % fw) * fw + (((cx % fw) + fw) % fw)] : clamp01((at(cx, cy) / RELIEF_U - 0.62) * 3);
        const cover = Math.min(1, (Ly.T[fi] + Ly.S[fi] + 0.5 * (Ly.G ? Ly.G[fi] : 0)) / 255);
        const rocky = clamp01((sl - 0.32) * 2.2) + hi;
        let nr = Math.floor(rocky * (3.2 - 2.2 * cover) * Math.max(1, rockN) * (dist < 0.4 * rockR ? 1 : 0.5) + ((hsh >>> 9) & 255) / 256);
        for (let j = 0; j < nr; j++) {
          hsh = hash1(hsh + 31);
          const x = (gx + ((hsh & 1023) / 1024)) * cs, y = (gy + (((hsh >> 10) & 1023) / 1024)) * cs;
          if (world.cell[v._cellOf(x, y)] !== 1) continue;
          const kind = (hsh >>> 20) % 3; if (nR[kind] >= this.rocks[kind].instanceMatrix.count) continue;
          const sr = (0.3 + 2.4 * Math.pow(((hsh >>> 23) & 255) / 256, 2.4) * (1 + rocky * 0.5)) * rockU;
          eul.set((((hsh >>> 3) & 15) / 15 - 0.5) * 0.5, (hsh & 63) / 10, (((hsh >>> 7) & 15) / 15 - 0.5) * 0.5);
          p.set(x, v.heightAt(x, y) + 0.02 * sr, y); q.setFromEuler(eul); s.set(sr, sr, sr);
          mat.compose(p, q, s);
          const g = 0.9 + 0.25 * (((hsh >>> 13) & 15) / 15);
          col.setRGB(g * (1 + 0.06 * hi), g, g * (1 - 0.04 * hi));
          if (frost > 0.3) col.lerp(SNOW, 0.5 * frost);
          this.rocks[kind].setMatrixAt(nR[kind], mat); this.rocks[kind].setColorAt(nR[kind]++, col);
        }
      }
      if (fire && nf < FLAME_CAP) {
        for (let j = 0; j < 3 && nf < FLAME_CAP; j++) {
          hsh = hash1(hsh + 99);
          const x = (gx + ((hsh & 1023) / 1024)) * cs, y = (gy + (((hsh >> 10) & 1023) / 1024)) * cs;
          const fs = FLAME_M * upm;
          p.set(x, v.heightAt(x, y) + 1.2 * fs, y); q.identity(); s.set(fs, fs, fs);
          this.flames.setMatrixAt(nf++, mat.compose(p, q, s));
        }
      }
    }
    const done = (o, n) => { o.count = n; o.instanceMatrix.needsUpdate = true; if (o.instanceColor) o.instanceColor.needsUpdate = true; };
    for (let k = 0; k < NFORMS; k++) { done(this.near[k], nNear[k]); done(this.far[k], nFar[k]); }
    for (let k = 0; k < 3; k++) { done(this.bush[k], nB[k]); done(this.rocks[k], nR[k]); }
    done(this.flames, nf); done(this.bushFar, nBF);
    this.counts = { near: Array.from(nNear), far: Array.from(nFar), bush: Array.from(nB), bushFar: nBF, rocks: Array.from(nR) };
    this.lastMs = performance.now() - t0;
  }

  /** Grass tufts round the camera: a jittered grid, each point kept as often as the grass layer covers its cell. */
  _grass(force) {
    const v = this.v, cam = v.camera.position, g = this.grass;
    const ground = v.heightAt(cam.x, cam.z), alt = cam.y - ground;
    // tufts at their true size, in a disc round the camera as big as the budget allows; on a grid twice as fine (four
    // times the tufts) within GRASS_FINE_M of the camera, where an eye a few cm to a metre above the ground looks across them
    const gu = v.scale.upm, st = GRASS_STEP_M * gu, R = st * 54, tuft = GRASS_M * gu, sf = st / 2, RF = GRASS_FINE_M * gu;
    if (alt > R * 1.2) { g.count = 0; this._grassC = null; return; }
    if (!force && this._grassC && this._grassU === gu && Math.hypot(cam.x - this._grassC.x, cam.z - this._grassC.z) < st * 4) return;
    this._grassU = gu;
    const t0 = performance.now();
    this._grassC = cam.clone();
    const Ly = this._layers(), d = Ly.d, cs = v.W / d, world = v.world;
    const mat = this._m, q = this._q, p = this._p, s = this._s, col = this._c;
    const x0 = Math.floor((cam.x - R) / sf), x1 = Math.ceil((cam.x + R) / sf);
    const z0 = Math.floor((cam.z - R) / sf), z1 = Math.ceil((cam.z + R) / sf);
    let n = 0;
    for (let iz = z0; iz <= z1 && n < GRASS_CAP; iz++) for (let ix = x0; ix <= x1 && n < GRASS_CAP; ix++) {
      const coarse = !(ix & 1) && !(iz & 1);
      // (the fine grid's in-between points only near the camera: a quick test on the cell before hashing)
      if (!coarse && Math.hypot((ix + 0.5) * sf - cam.x, (iz + 0.5) * sf - cam.z) > RF + sf) continue;
      let h = hash2(ix, iz);
      const x = (ix + (h & 255) / 256) * sf, z = (iz + ((h >>> 8) & 255) / 256) * sf;
      const r = Math.hypot(x - cam.x, z - cam.z); if (r > R || (!coarse && r > RF)) continue;
      const fx = ((Math.floor(x / cs) % d) + d) % d, fz = ((Math.floor(z / cs) % d) + d) % d, fi = fz * d + fx;
      // reeds (FIELDS 'wetland', when sent): a marsh is a reed bed; they stand where its pools are not
      const wl = Ly.WL ? Ly.WL[fi] / 255 : 0, reed = wl > 0.05 && ((h >>> 26) & 63) / 64 < Math.min(0.85, wl * 1.4);
      const cover = reed ? Math.max(Ly.G[fi] / 255, 0.85 * wl) : Ly.G[fi] / 255;
      if (((h >>> 16) & 1023) / 1024 >= cover * 0.9) continue;
      if (world.cell[v._cellOf(x, z)] !== 1 && !reed) continue;
      h = hash1(h);
      const fade = 1 - smooth(R * 0.7, R, r), sc = (0.4 + 0.4 * (h & 255) / 256) * (0.6 + 0.6 * cover) * fade * tuft;
      if (sc < 0.05 * tuft) continue;
      // a reed (Phragmites, sedges) 1.5-3 m tall and slender; a grass tuft half a metre
      const tall = reed ? 2.8 : 1, thin = reed ? 0.55 : 1.2;
      p.set(x, v.heightAt(x, z) - 0.02 * sc, z); q.setFromAxisAngle(UP, ((h >>> 8) & 63) / 10); s.set(sc * thin, sc * tall, sc * thin);
      g.setMatrixAt(n, mat.compose(p, q, s));
      // as the terrain paints grass: green, yellowing where the soil is dry; frost whitens it; reeds straw and sage
      const dry = 1 - (Ly.M ? Ly.M[fi] / 255 : 0.5), j = 0.85 + 0.3 * ((h >>> 16) & 255) / 256;
      if (reed) { const lo = Ly.LO ? Ly.LO[fi] / 255 : 1; col.setRGB((0.36 + 0.28 * (1 - lo)) * j, (0.44 + 0.06 * (1 - lo)) * j, (0.24 + 0.06 * (1 - lo)) * j); }
      else col.setRGB((0.30 + 0.34 * dry) * j, (0.55 - 0.08 * dry) * j, 0.16 * j);
      if (Ly.FZ && Ly.FZ[fi]) col.lerp(SNOW, 0.6 * Ly.FZ[fi] / 255);
      g.setColorAt(n++, col);
    }
    g.count = n; g.instanceMatrix.needsUpdate = true; g.instanceColor.needsUpdate = true;
    this.grassMs = performance.now() - t0;
  }
}

const UP = new THREE.Vector3(0, 1, 0), SNOW = new THREE.Color(0.95, 0.97, 1.0), AUTUMN = new THREE.Color(1.55, 1.05, 0.35), CHAR = new THREE.Color(0.18, 0.15, 0.13);

/** Foliage tint: the lineage's shade (a little hue and lightness), dry leaves yellow, snow, char. */
function tint(col, lh, u, dryLeaves, frost, fire, form) {
  const hue = ((lh >>> 8) & 255) / 255 - 0.5, lig = 0.85 + 0.3 * (((lh >>> 16) & 255) / 255) + 0.12 * (u - 0.5);
  col.setRGB(lig * (1 + 0.25 * hue), lig, lig * (1 - 0.3 * hue));
  if (dryLeaves > 0.05) col.lerp(AUTUMN, Math.min(0.85, dryLeaves * 1.1));
  if (frost > 0.05) col.lerp(SNOW, Math.min(0.75, frost * (form === SPRUCE || form === PINE ? 0.9 : 0.7)));
  if (fire) col.copy(CHAR);
}

function clamp01(x) { return x < 0 ? 0 : x > 1 ? 1 : x; }
function smooth(a, b, x) { const t = clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }
function hash1(x) { x = Math.imul(x ^ (x >>> 16), 0x45d9f3b); x = Math.imul(x ^ (x >>> 16), 0x45d9f3b); return (x ^ (x >>> 16)) >>> 0; }
function hash2(x, y) { return hash1(Math.imul(x, 73856093) ^ Math.imul(y, 19349663)); }

/** The distance scale D0 for which the sum of v[k] x min(1, (D0 / dist[k])²) over the first m cells is about budget
 *  (Infinity when everything fits). */
function fitBudget(dist, v, m, budget) {
  let all = 0; for (let k = 0; k < m; k++) all += v[k];
  if (all <= budget) return Infinity;
  let lo = 0, hi = 0; for (let k = 0; k < m; k++) hi = Math.max(hi, dist[k]);
  for (let it = 0; it < 24; it++) {
    const D = (lo + hi) / 2, D2 = D * D;
    let sum = 0; for (let k = 0; k < m; k++) sum += v[k] * Math.min(1, D2 / (dist[k] * dist[k]));
    if (sum > budget) hi = D; else lo = D;
  }
  return lo;
}
