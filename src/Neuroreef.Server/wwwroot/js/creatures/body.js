// One parametric body for every organism. There are no designed species: the geometry below holds only the
// surface coordinates of each part (one continuous trunk tube from tail tip to nose, legs, eyes, jaw, teeth,
// membranes), indexed; the shader (anim.js) builds the surfaces from the genes. Appearance genes determine
// proportions, posture, outlines and pigment; organs determine their presence, while locomotion selects pose and
// animation. Near body ~1.5k triangles, far body ~260 (no eyes, jaw, teeth, plates); both share the shader.

import * as THREE from 'three';
import { P, FAR, grid, steps } from './kit.js';
import { bodyMaterial } from './anim.js';
import { locoOf } from '../net.js';

export { bodyMaterial, locoOf };

/** Body mass from the mass gene (-127..127), as the sim expresses it. */
export function massOf(g) { return 0.1 * Math.pow(40 / 0.1, (g + 128) / 255); }   // MASS_LO 0.1 .. MASS_HI 40 (byte 12 carries the body's actual size)

/** View scale of an organism of this mass (model units to world units): length grows as mass^(1/3). */
export function bodySize(mass) { return 1.5 * Math.cbrt(mass); }

/** Most pairs of legs, of side fins and of eyes a body can have (their genes give 1..4 each). */
export const MAX_PAIRS = 4;
const pairsOf = (n) => Math.min(4, Math.max(1, n | 0 || 1));
const LOCO_CODE = { Walk: 0, Wade: 1, Swim: 2, Fly: 3, Sessile: 4 };

// Surface coordinates of each part at the two detail levels. The trunk's s runs 0..4 over four spans: tail
// (tip to rump), trunk (rump to shoulders), neck and head (occiput to nose); rings are denser where it curves.
// A leg's s runs over its bones: hip 0, knee 1, ankle 2, toe 3.
const LOD = {
  near: {
    trunk: [[0, 0.12, 0.3, 0.5, 0.7, 0.86, 1, 1.15, 1.3, 1.45, 1.6, 1.75, 1.9, 2, 2.33, 2.66, 3, 3.18, 3.36, 3.54, 3.7, 3.84, 3.94, 4], steps(12)],
    leg: [[0, 0.45, 1, 2, 3], steps(6)],
    eye: [[0, 0.3, 0.62], steps(5)],
    jaw: [[0, 0.3, 0.6, 0.85, 1], steps(5)],
    tooth: [[0, 1], steps(3)],
    bone: [[0, 0.15, 0.3, 0.62, 1], steps(3)],
    wing: [steps(6), [0, 0.5, 1]],
    fin: [[0, 0.5, 1], [0, 1]],
    dorsal: [steps(3), [0, 0.5, 1]],
    tailFin: [[0, 0.5, 1], steps(3)],
    ear: [[0, 0.5, 1], [0, 1]],
    plate: [[0, 0.5, 1], [0, 0.5, 1]],
  },
  far: {
    trunk: [[0, 0.45, 1, 1.5, 2, 2.6, 3, 3.5, 3.85, 4], steps(5)],
    leg: [[0, 1, 3], steps(3)],
    wing: [[0, 0.5, 1], [0, 1]],
    fin: [[0, 1], [0, 1]],
    dorsal: [[0, 0.5, 1], [0, 1]],
    tailFin: [[0, 1], [0, 0.5, 1]],
    ear: [[0, 1], [0, 1]],
  },
};
// LENGTH WIDTH HEIGHT HEAD NECK TAIL LIMB_POS LIMB_SHAPE FIN_SHAPE WING_SHAPE
// HUE SAT LIGHT PATTERN PATTERN_SCALE PATTERN_HUE. Previews without genes stay neutral.
export const DEFAULT_APPEARANCE = new Float32Array([0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0, 0.5, 0, 0.5, 0.5]);
export function appearanceColor(b, color) {
  const a = b.appearance || DEFAULT_APPEARANCE;
  return color.setHSL(a[10], 0.04 + 0.78 * Math.pow(a[11], 1.4), 0.12 + 0.62 * a[12], THREE.SRGBColorSpace);   // as the shader's pigment
}

/**
 * The body geometry at one detail level: indexed parametric surfaces, position = surface coordinates (s, t, 0),
 * aAux = (side, index, part). No normals are stored: the shader takes them from the surface it builds.
 */
export function bodyGeometry(detail = 'near') {
  const near = detail === 'near', L = LOD[near ? 'near' : 'far'], far = near ? 0 : FAR;
  const pos = [], aux = [], idx = [];
  const add = ([ss, ts], part, side = 0, index = 0, opt = {}) => {
    const g = grid(ss, ts, opt), o = pos.length / 3;
    pos.push(...g.pos);
    for (let i = 0; i < g.pos.length / 3; i++) aux.push(side, index, part + far);
    for (const i of g.idx) idx.push(o + i);
  };
  const membrane = { closed: false };
  add(L.trunk, P.TRUNK);
  // up to four pairs of legs, of side fins and of eyes (index = pair, front first); the genes decide how many show
  for (let j = 0; j < MAX_PAIRS; j++) for (const s of [1, -1]) add(L.leg, P.LEG, s, j);
  for (let j = 0; j < MAX_PAIRS; j++) for (const s of [1, -1]) add(L.fin, P.FIN_SIDE, s, j, membrane);
  for (const s of [1, -1]) add(L.wing, P.WING, s, 0, membrane);
  add(L.dorsal, P.FIN_DORSAL, 0, 0, membrane);
  add(L.tailFin, P.TAIL_FIN, 0, 0, membrane);
  for (const s of [1, -1]) add(L.ear, P.EAR, s, 0, membrane);
  if (near) {
    for (let j = 0; j < MAX_PAIRS; j++) for (const s of [1, -1]) add(L.eye, P.EYE, s, j, { flip: true });
    add(L.jaw, P.JAW);
    for (const s of [1, -1]) for (let k = 0; k < 3; k++) add(L.tooth, P.TOOTH, s, k);
    for (const s of [1, -1]) add(L.bone, P.BONE, s);
    for (let k = 0; k < 4; k++) add(L.plate, P.PLATE, 0, k, membrane);
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  g.setAttribute('aAux', new THREE.Float32BufferAttribute(aux, 3));
  g.setIndex(idx);
  // Includes volume-normalized extreme proportions, extended neck/tail/wings and animation.
  g.boundingSphere = new THREE.Sphere(new THREE.Vector3(0, 0, 0), 12);
  g.boundingBox = new THREE.Box3(new THREE.Vector3(-12, -12, -12), new THREE.Vector3(12, 12, 12));
  return g;
}

/** Triangle count of a body geometry. */
export function triangles(g) { return (g.index ? g.index.count : g.attributes.position.count) / 3; }

/**
 * An instanced mesh with room for cap organisms. Includes animation, organ, clade and four appearance
 * attributes (four floats each per organism). Write them with setBody().
 */
export function bodyMesh(base, material, cap) {
  const g = base.clone();
  const anim = new THREE.InstancedBufferAttribute(new Float32Array(cap * 4), 4);
  const geneA = new THREE.InstancedBufferAttribute(new Float32Array(cap * 4), 4);
  const geneB = new THREE.InstancedBufferAttribute(new Float32Array(cap * 4), 4);
  const clade = new THREE.InstancedBufferAttribute(new Float32Array(cap * 4), 4);
  const morph = Array.from({ length: 4 }, () => new THREE.InstancedBufferAttribute(new Float32Array(cap * 4), 4));
  for (const a of [anim, geneA, geneB, clade, ...morph]) a.setUsage(THREE.DynamicDrawUsage);
  g.setAttribute('aAnim', anim); g.setAttribute('aGeneA', geneA); g.setAttribute('aGeneB', geneB); g.setAttribute('aClade', clade);
  for (let j = 0; j < 4; j++) g.setAttribute('aMorph' + 'ABCD'[j], morph[j]);
  const mesh = new THREE.InstancedMesh(g, material, cap);
  mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
  mesh.count = 0; mesh.frustumCulled = false;
  // stable instance slots (placeBody): key -> slot, the key in each slot, the frame each slot was last placed in,
  // free slots below the high-water mark, and the slots whose genes or colour changed this frame
  const slots = { of: new Map(), key: new Array(cap), seen: new Uint32Array(cap), free: [], hw: 0, frame: 0, dirty: [], repack: false };
  return { mesh, cap, anim, geneA, geneB, clade, morph, slots };
}

function writeMatrix(M, o, x, y, z, h, sx, sy) {
  const ca = Math.cos(h), sa = Math.sin(h);
  M[o] = ca * sx; M[o + 1] = 0; M[o + 2] = sa * sx; M[o + 3] = 0;
  M[o + 4] = 0; M[o + 5] = sy; M[o + 6] = 0; M[o + 7] = 0;
  M[o + 8] = -sa * sx; M[o + 9] = 0; M[o + 10] = ca * sx; M[o + 11] = 0;
  M[o + 12] = x; M[o + 13] = y; M[o + 14] = z; M[o + 15] = 1;
}

function writeGenes(B, k4, b) {
  const GA = B.geneA.array, GB = B.geneB.array;
  GA[k4] = b.legs; GA[k4 + 1] = b.fins; GA[k4 + 2] = b.wings; GA[k4 + 3] = b.sight;
  GB[k4] = b.armour; GB[k4 + 1] = b.strike; GB[k4 + 2] = b.fur || 0; GB[k4 + 3] = b.append || 0;
  const appearance = b.appearance || DEFAULT_APPEARANCE;
  for (let j = 0; j < 4; j++) for (let c = 0; c < 4; c++) B.morph[j].array[k4 + c] = appearance[j * 4 + c];
}

// the packed counts (aClade.w): meat-eating (0..15) * 512 + loco * 64 + (fin pairs - 1) * 16 + (eye pairs - 1) * 4 + (leg pairs - 1)
const cladeCode = (b) => Math.round(Math.min(1, Math.max(0, b.meat || 0)) * 15) * 512 +
  (LOCO_CODE[b.loco] ?? 0) * 64 + (pairsOf(b.finPairs) - 1) * 16 + (pairsOf(b.eyePairs) - 1) * 4 + (pairsOf(b.legPairs) - 1);

// ------------------------------------------------------------------ stable instance slots (the live view)
// Each organism keeps its instance slot while it stays drawn in a mesh, so its genes, appearance and colour are
// uploaded once, when it gets the slot; each frame only the matrices and the animation data are uploaded.

/** Start a frame of placeBody calls on a body mesh. */
export function beginBodies(B) {
  const S = B.slots;
  if (S.repack) {   // too many hidden slots below the high-water mark: give every organism a new slot this frame
    S.of.clear(); S.key.fill(undefined); S.free.length = 0; S.hw = 0; S.repack = false;
  }
  S.frame = (S.frame + 1) >>> 0 || 1; S.dirty.length = 0;
}

/**
 * Draw the organism with this key (its serial; any stable key) in this frame: as setBody, in the slot it had last
 * frame, or a new one (its genes written then). The genes are taken to be the same for its life. Returns false when
 * the mesh is full and the organism has no slot in it.
 */
export function placeBody(B, key, x, y, z, h, sx, sy, b, gait, phase, acts, rest, r, g, bl) {
  const S = B.slots;
  let k = S.of.get(key), fresh = false;
  if (k === undefined) {
    if (S.free.length) k = S.free.pop(); else if (S.hw < B.cap) k = S.hw++; else return false;
    S.of.set(key, k); S.key[k] = key; fresh = true;
  }
  S.seen[k] = S.frame;
  writeMatrix(B.mesh.instanceMatrix.array, k * 16, x, y, z, h, sx, sy);
  const k4 = k * 4, A = B.anim.array, CL = B.clade.array;
  A[k4] = gait; A[k4 + 1] = phase; A[k4 + 2] = acts; A[k4 + 3] = rest;
  if (fresh) writeGenes(B, k4, b);
  // the overlay colour changes (a hit flushes it, the clade table may change), so it is compared and sent when it does
  const code = cladeCode(b);
  if (fresh || CL[k4] !== r || CL[k4 + 1] !== g || CL[k4 + 2] !== bl || CL[k4 + 3] !== code) {
    CL[k4] = r; CL[k4 + 1] = g; CL[k4 + 2] = bl; CL[k4 + 3] = code;
    S.dirty.push(k);
  }
  return true;
}

/** End the frame: free (and hide) the slots not placed this frame; upload the matrices and animation of every slot
 *  in use, and the genes and colour of the slots that changed. */
export function finishBodies(B) {
  const S = B.slots, M = B.mesh.instanceMatrix.array;
  let live = 0;
  for (let k = 0; k < S.hw; k++) {
    if (S.key[k] === undefined) continue;
    if (S.seen[k] === S.frame) { live++; continue; }
    S.of.delete(S.key[k]); S.key[k] = undefined; S.free.push(k);
    for (let j = 0; j < 16; j++) M[k * 16 + j] = 0;   // a zero matrix: nothing is drawn
  }
  const hw0 = S.hw;
  while (S.hw > 0 && S.key[S.hw - 1] === undefined) S.hw--;
  if (S.hw < hw0) S.free = S.free.filter((k) => k < S.hw);
  if (S.hw > 2 * live + 256) S.repack = true;
  const n = S.hw;
  B.mesh.count = n;
  if (n === 0) return;
  for (const [a, w] of [[B.mesh.instanceMatrix, 16], [B.anim, 4]]) { a.clearUpdateRanges(); a.addUpdateRange(0, n * w); a.needsUpdate = true; }
  if (S.dirty.length === 0) return;
  // the changed slots as runs of neighbours (a few dozen uploads at most; beyond that one span over all of them)
  const d = S.dirty.sort((a, b) => a - b), runs = [];
  for (let i = 0; i < d.length;) { let j = i; while (j + 1 < d.length && d[j + 1] <= d[j] + 1) j++; runs.push(d[i], d[j]); i = j + 1; }
  const statics = [B.geneA, B.geneB, B.clade, ...B.morph];
  for (const a of statics) {
    a.clearUpdateRanges();
    if (runs.length > 64) a.addUpdateRange(d[0] * 4, (d[d.length - 1] - d[0] + 1) * 4);
    else for (let r = 0; r < runs.length; r += 2) a.addUpdateRange(runs[r] * 4, (runs[r + 1] - runs[r] + 1) * 4);
    a.needsUpdate = true;
  }
}

/**
 * Write organism k of a body mesh: position (x, height, z), heading h (radians, world convention: +x
 * forward, turning toward +z), scale sx/sy, the body b = { legs, fins, wings, sight, armour, strike,
 * fur, append, meat, appearance, legPairs, finPairs, eyePairs, loco }, animation (gait, phase, acts, rest) and
 * the identification-overlay colour (linear r, g, b). Missing appearance uses stable neutral defaults.
 */
export function setBody(B, k, x, y, z, h, sx, sy, b, gait, phase, acts, rest, r, g, bl) {
  writeMatrix(B.mesh.instanceMatrix.array, k * 16, x, y, z, h, sx, sy);
  const k4 = k * 4, A = B.anim.array, CL = B.clade.array;
  A[k4] = gait; A[k4 + 1] = phase; A[k4 + 2] = acts; A[k4 + 3] = rest;
  writeGenes(B, k4, b);
  CL[k4] = r; CL[k4 + 1] = g; CL[k4 + 2] = bl; CL[k4 + 3] = cladeCode(b);
}

/** Set the instance count and upload only the used part of each instance buffer. */
export function commitBodies(B, n) {
  B.mesh.count = n;
  if (n === 0) return;
  for (const [a, w] of [[B.mesh.instanceMatrix, 16], [B.anim, 4], [B.geneA, 4], [B.geneB, 4], [B.clade, 4]]) {
    a.clearUpdateRanges(); a.addUpdateRange(0, n * w); a.needsUpdate = true;
  }
  for (const a of B.morph) { a.clearUpdateRanges(); a.addUpdateRange(0, n * 4); a.needsUpdate = true; }
}
