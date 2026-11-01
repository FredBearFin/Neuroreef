// Caves, sinkholes and springs (2026-11-01): drawn from the karst data once the server sends it; nothing without it.
//
// The data (ICD WORLD frame, decoded by net.js):
//  a) w.caves = Uint8Array(FW²), the cave-features plane (World.CaveKind bits, Caves.cs): 1 karst passage, 2 lava tube
//     (both underground: not drawn here), 4 sinkhole, 8 entrance, 16 karst spring, 32 losing river, 64 ponor; and
//     w.caveCourse, the conduit plane (bits 3-7 a size class, used for the size of springs and entrances on a conduit).
//     An entrance on a slope is drawn as a mouth facing downhill; on the flat (a lava-tube skylight) as a pit with an
//     open throat; a sinkhole with an entrance (a collapse doline) as a bowl with an open throat; a ponor as a small
//     sinkhole with an open throat;
//  b) for tests and future data, w.caves = [{ kind: 'cave' | 'sinkhole' | 'spring' (or 1 | 2 | 3), x, y (world units),
//     dir?: radians (0 = +x, pi/2 = +y; default: downhill), r?: radius in units (default 1.6) }, ...].
// Anything else (or nothing: a server without the plane) draws no karst. A feature on standing water is skipped.
//
// How each is drawn:
// - cave entrance: a notch cut into the slope (a flat floor, a steep back wall: the carve texture), the wall's arch
//   painted black (tFeat.r) and framed by an arch of rough rocks with a lintel, so it reads as a dark opening in the
//   hillside or cliff (a heightfield cannot overhang; the rocks give it the overhang);
// - sinkhole: a bowl carved below its lowest rim (carve), darkening to a black throat at the bottom (tFeat.g);
// - spring: a small basin with a pool of water at the lowest point of its rim, mossy wet ground round it (tFeat.b),
//   a few boulders, and a little stream running downhill from it until it meets a river or standing water (a river path).

import * as THREE from 'three';

const DIRX = [1, 1, 0, -1, -1, -1, 0, 1], DIRY = [0, 1, 1, 1, 0, -1, -1, -1];
const KINDS = { cave: 1, sinkhole: 2, spring: 3 };

/** The features of a WORLD frame: [{ kind 1..3, x, z, dir, r }] (empty without karst data). */
export function decodeFeatures(w, S, groundRaw) {
  const src = w.caves, out = [];
  if (!src) return out;
  const downhill = (x, z) => {
    const e = 1.5, gx = groundRaw(x + e, z) - groundRaw(x - e, z), gz = groundRaw(x, z + e) - groundRaw(x, z - e);
    return Math.atan2(-gz, -gx);
  };
  if (ArrayBuffer.isView(src)) {
    const fw = w.fw; if (src.length < fw * fw) return out;
    const course = w.caveCourse && w.caveCourse.length >= fw * fw ? w.caveCourse : null;
    for (let i = 0; i < fw * fw; i++) {
      const v = src[i]; if (!(v & (4 | 8 | 16 | 64))) continue;
      const x = (i % fw) * S, z = ((i / fw) | 0) * S, cls = course ? course[i] >> 3 : 0;
      const big = 1 + Math.min(0.8, cls / 20);      // a mouth or spring on a big conduit is bigger
      const e = 1.5, gx = groundRaw(x + e, z) - groundRaw(x - e, z), gz = groundRaw(x, z + e) - groundRaw(x, z - e);
      const slope = Math.hypot(gx, gz) / (2 * e), dir = Math.atan2(-gz, -gx);
      if (v & 8) {
        if ((v & 4) || slope < 0.12) out.push({ kind: 2, x, z, dir, r: (v & 4 ? 2.2 : 1.3) * big, open: true });   // collapse doline, skylight
        else out.push({ kind: 1, x, z, dir, r: 1.5 * big });
      } else if (v & 4) out.push({ kind: 2, x, z, dir, r: v & 64 ? 1.4 : 1.9, open: !!(v & 64) });
      if (v & 16) out.push({ kind: 3, x: x + Math.cos(dir) * 1.2, z: z + Math.sin(dir) * 1.2, dir, r: 1.4 * big });
    }
  } else if (Array.isArray(src)) {
    for (const f of src) {
      if (!f) continue;
      const kind = typeof f.kind === 'string' ? KINDS[f.kind] : f.kind | 0;
      if (!(kind >= 1 && kind <= 3) || !Number.isFinite(f.x) || !Number.isFinite(f.y)) continue;
      out.push({ kind, x: f.x, z: f.y, dir: Number.isFinite(f.dir) ? f.dir : downhill(f.x, f.y), r: Number.isFinite(f.r) ? Math.max(0.5, f.r) : 1.6 });
    }
  }
  return out;
}

/**
 * Cut the features into the carve texture and paint them into tFeat. groundRaw: the uncut ground; water(x, z): true on
 * standing water. Returns the springs' outflow streams as river paths (rivers.js format), and the placed features.
 */
export function stampFeatures(feats, ctx) {
  const { carve, feat, fw, CR, S, groundRaw, water, isRiver } = ctx;
  const N = fw * CR, T = S / CR, streams = [], placed = [];
  const each = (x, z, R, fn) => {
    const i0 = Math.floor((x - R) / T), i1 = Math.ceil((x + R) / T), j0 = Math.floor((z - R) / T), j1 = Math.ceil((z + R) / T);
    for (let j = j0; j <= j1; j++) for (let i = i0; i <= i1; i++) {
      const qx = i * T, qz = j * T, t = (((j % N) + N) % N) * N + (((i % N) + N) % N);
      fn(qx - x, qz - z, t, qx, qz);
    }
  };
  const lowRing = (x, z, r) => { let lo = Infinity; for (let k = 0; k < 12; k++) { const a = k / 12 * Math.PI * 2; lo = Math.min(lo, groundRaw(x + Math.cos(a) * r, z + Math.sin(a) * r)); } return lo; };
  for (const f of feats) {
    const { kind, x, z, r } = f;
    if (water(x, z)) continue;
    if (kind === 1) {
      const ux = Math.cos(f.dir), uz = Math.sin(f.dir);
      const floor = Math.min(groundRaw(x, z), groundRaw(x + ux * 0.4 * r, z + uz * 0.4 * r)) - 0.1 * r;
      each(x, z, 3 * r, (dx, dz, t, qx, qz) => {
        const a = dx * ux + dz * uz, b = Math.abs(-dx * uz + dz * ux);
        const cap = floor + 0.35 * Math.max(0, a - 0.8 * r) + 3.0 * Math.max(0, -a) + 2.5 * Math.max(0, b - 0.9 * r);
        const g = groundRaw(qx, qz);
        if (cap < carve[t]) carve[t] = cap;
        // the opening: an arch on the back wall, where the wall is cut (not on the open hillside above it)
        const hgt = 3.0 * Math.max(0, -a), e = (b / (0.85 * r)) ** 2 + (hgt / (1.3 * r)) ** 2;
        if (a < 0.1 * r && cap < g - 0.05 && e < 1.2) {
          const v = Math.round(Math.min(1, (1.2 - e) / 0.4) * 255);
          if (v > feat[t * 4]) feat[t * 4] = v;
        }
      });
      placed.push({ ...f, floor });
    } else if (kind === 2) {
      const rim = lowRing(x, z, r), dep = 0.55 * r;
      each(x, z, r + 2, (dx, dz, t) => {
        const d = Math.hypot(dx, dz);
        const cap = d < r ? rim - dep * (1 - (d / r) ** 2) : rim + 1.2 * (d - r);
        if (cap < carve[t]) carve[t] = cap;
        // the bowl darkens inward; only an open one (a ponor, a collapse into a cave) has a black throat
        if (d < r * 1.1) { const v = Math.round(Math.min(f.open ? 1 : 0.7, 1.1 - d / r) * 255); if (v > feat[t * 4 + 1]) feat[t * 4 + 1] = v; }
      });
      placed.push({ ...f, rim });
    } else {
      const pr = 0.55 * r, level = lowRing(x, z, pr);
      each(x, z, 1.8 * r, (dx, dz, t) => {
        const d = Math.hypot(dx, dz);
        const cap = d < pr ? level - 0.15 : level - 0.15 + 1.0 * (d - pr);
        if (cap < carve[t]) carve[t] = cap;
        const v = Math.round(Math.max(0, Math.min(1, (1.8 * r - d) / r)) * 255); if (v > feat[t * 4 + 2]) feat[t * 4 + 2] = v;
      });
      placed.push({ ...f, level });
      // its stream: downhill from the pool, a step at a time, until a river or standing water (at most 40 units)
      const p = { x: [x], z: [z], w: [0.5], lvl: [level], depth: [0.12], flow: [0], cell: [ctx.cellOf(x, z)] };
      let cx = x + Math.cos(f.dir) * pr, cz = z + Math.sin(f.dir) * pr, lv = level;
      for (let k = 0; k < 40; k++) {
        const g = Math.min(lv, groundRaw(cx, cz)); lv = g;
        p.x.push(cx); p.z.push(cz); p.w.push(0.5 + 0.01 * k); p.lvl.push(g + 0.02); p.depth.push(0.12); p.flow.push(0); p.cell.push(ctx.cellOf(cx, cz));
        if (water(cx, cz) || isRiver(cx, cz)) break;
        const e = 1.2, gx = groundRaw(cx + e, cz) - groundRaw(cx - e, cz), gz = groundRaw(cx, cz + e) - groundRaw(cx, cz - e), gl = Math.hypot(gx, gz);
        if (gl < 1e-4) break;
        cx -= gx / gl; cz -= gz / gl;
      }
      if (p.x.length > 2) streams.push(p);
    }
  }
  return { streams, placed };
}

/** Merge (geometry, colour) pairs into one non-indexed geometry with vertex colours. */
function mergeColored(parts) {
  let n = 0; for (const [g] of parts) n += g.attributes.position.count;
  const P = new Float32Array(n * 3), Nn = new Float32Array(n * 3), C = new Float32Array(n * 3);
  let o = 0;
  for (const [g, c] of parts) {
    const p = g.attributes.position, q = g.attributes.normal;
    for (let v = 0; v < p.count; v++, o++) {
      P[o * 3] = p.getX(v); P[o * 3 + 1] = p.getY(v); P[o * 3 + 2] = p.getZ(v);
      Nn[o * 3] = q.getX(v); Nn[o * 3 + 1] = q.getY(v); Nn[o * 3 + 2] = q.getZ(v);
      C[o * 3] = c[0]; C[o * 3 + 1] = c[1]; C[o * 3 + 2] = c[2];
    }
    g.dispose();
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.BufferAttribute(P, 3));
  g.setAttribute('normal', new THREE.BufferAttribute(Nn, 3));
  g.setAttribute('color', new THREE.BufferAttribute(C, 3));
  return g;
}

function hashf(a, b) { let x = Math.imul((a * 73856093) ^ (b * 19349663), 0x45d9f3b); x = Math.imul(x ^ (x >>> 16), 0x45d9f3b); return ((x ^ (x >>> 16)) >>> 0) / 4294967296; }

/** The rocks (cave arches and lintels, spring boulders) and the spring pools, as two geometries (null if none). */
export function featureMeshes(placed) {
  const rocks = [], pools = [];
  const rock = (x, y, z, sx, sy, sz, rot, shade, seed) => {
    const g = new THREE.IcosahedronGeometry(1, 1);
    const p = g.attributes.position;
    for (let v = 0; v < p.count; v++) {   // lumpy: each corner pushed in or out (by its position: faces sharing it stay joined)
      const k = 0.75 + 0.5 * hashf(Math.round(p.getX(v) * 97) * 7919 + Math.round(p.getY(v) * 97) + seed * 131, Math.round(p.getZ(v) * 97));
      p.setXYZ(v, p.getX(v) * k, p.getY(v) * k, p.getZ(v) * k);
    }
    const ng = g; ng.computeVertexNormals();   // non-indexed: flat, faceted normals
    ng.scale(sx, sy, sz); ng.rotateY(rot); ng.translate(x, y, z);
    rocks.push([ng, [0.20 * shade, 0.185 * shade, 0.17 * shade]]);
  };
  let seed = 1;
  for (const f of placed) {
    seed++;
    if (f.kind === 1) {
      const { x, z, r, floor } = f, ux = Math.cos(f.dir), uz = Math.sin(f.dir), rot = -f.dir;
      // the arch: rocks along a half-ellipse on the back wall (which leans back 1 in 3), and a lintel over it
      for (let k = 0; k <= 6; k++) {
        const th = k / 6 * Math.PI, b = Math.cos(th) * 1.0 * r, hgt = Math.sin(th) * 1.35 * r, a = -hgt / 3 + 0.12 * r;
        const s = (0.32 + 0.12 * hashf(seed, k)) * r;
        rock(x + ux * a - uz * b, floor + hgt, z + uz * a + ux * b, s, s * 0.9, s, rot + k, 0.8 + 0.4 * hashf(k, seed), seed * 7 + k);
      }
      rock(x + ux * (0.05 * r), floor + 1.45 * r, z + uz * (0.05 * r), 0.55 * r, 0.3 * r, 1.25 * r, rot, 0.85, seed * 11);
    } else if (f.kind === 3) {
      const { x, z, r, level } = f, pr = 0.55 * r;
      const disc = new THREE.CircleGeometry(pr * 1.05, 18); disc.rotateX(-Math.PI / 2); disc.translate(x, level - 0.04, z);
      pools.push([disc, [0.16, 0.42, 0.48]]);
      for (let k = 0; k < 4; k++) {
        const a = f.dir + Math.PI * (0.55 + 0.3 * k), s = (0.22 + 0.15 * hashf(seed, k)) * r;
        rock(x + Math.cos(a) * pr * 1.25, level + s * 0.3, z + Math.sin(a) * pr * 1.25, s, s * 0.7, s, k, 0.9, seed * 5 + k);
      }
    } else if (f.kind === 2) {
      const { x, z, r, rim } = f;
      for (let k = 0; k < 3; k++) {   // bare rock breaking out on the rim
        const a = hashf(seed, k) * Math.PI * 2, s = (0.18 + 0.12 * hashf(k, seed)) * r;
        rock(x + Math.cos(a) * r * 0.85, rim - s * 0.2, z + Math.sin(a) * r * 0.85, s, s * 0.6, s * 1.3, a, 0.9, seed * 3 + k);
      }
    }
  }
  return { rocks: rocks.length ? mergeColored(rocks) : null, pools: pools.length ? mergeColored(pools) : null };
}
