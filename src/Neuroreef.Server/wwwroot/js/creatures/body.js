// One parametric body for every organism. There are no designed species: the geometry below holds each
// part in a neutral unit frame. Appearance genes determine proportions, placement, outlines and pigment;
// organs determine their presence, while locomotion only selects animation. Both detail levels share the shader.

import * as THREE from 'three';
import { P, soup, flat, cone } from './kit.js';
import { bodyMaterial } from './anim.js';
import { locoOf } from '../net.js';

export { bodyMaterial, locoOf };

/** Body mass from the mass gene (-127..127), as the sim expresses it. */
export function massOf(g) { return 0.1 * Math.pow(40 / 0.1, (g + 128) / 255); }   // MASS_LO 0.1 .. MASS_HI 40 (byte 12 carries the body's actual size)

/** View scale of an organism of this mass (model units to world units): length grows as mass^(1/3). */
export function bodySize(mass) { return 1.5 * Math.cbrt(mass); }

const sphere = (w, h) => soup(new THREE.SphereGeometry(1, w, h));
/** A cone along +x: base (radius 1) at x = 0, tip at x = 1; open at the base. */
const coneX = (seg) => { const g = cone(seg); g.rotateZ(-Math.PI / 2); return soup(g); };
/** A cone along -x: base at x = 0, tip at x = -1. */
const coneNX = (seg) => { const g = cone(seg); g.rotateZ(Math.PI / 2); return soup(g); };
/** A spike: base (radius 1) at y = 0, tip at y = 1 (or y = -1 when down). */
const spike = (seg, down = false) => { const g = cone(seg); if (down) g.rotateX(Math.PI); return soup(g); };
/** A leg: from the hip (y = 0, radius 1) down to the foot (y = -1, radius 0.6). */
const legGeo = (seg, hseg) => { const g = new THREE.CylinderGeometry(1, 0.6, 1, seg, hseg, true); g.translate(0, -0.5, 0); return soup(g); };

/** Most pairs of legs, of side fins and of eyes a body can have (their genes give 1..4 each). */
export const MAX_PAIRS = 4;
const pairsOf = (n) => Math.min(4, Math.max(1, n | 0 || 1));
const LOCO_CODE = { Walk: 0, Wade: 1, Swim: 2, Fly: 3, Sessile: 4 };
// LENGTH WIDTH HEIGHT HEAD NECK TAIL LIMB_POS LIMB_SHAPE FIN_SHAPE WING_SHAPE
// HUE SAT LIGHT PATTERN PATTERN_SCALE PATTERN_HUE. Previews without genes stay neutral.
export const DEFAULT_APPEARANCE = new Float32Array([0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0, 0.5, 0, 0.5, 0.5]);
export function appearanceColor(b, color) {
  const a = b.appearance || DEFAULT_APPEARANCE;
  return color.setHSL(a[10], 0.05 + 0.9 * a[11], 0.12 + 0.72 * a[12], THREE.SRGBColorSpace);
}

const WING = [[0.5, 0], [0.38, 0.6], [0.02, 1.0], [-0.38, 0.85], [-0.5, 0]];
const SIDE_FIN = [[0.25, 0], [-0.35, 1.0], [-0.7, 0.8], [-0.45, 0]];
const DORSAL = [[-0.5, 0], [0.3, 0], [-0.05, 1.0], [-0.4, 0.85]];
const TAIL_FAN = [[0, 0.18], [-0.95, 1.0], [-0.65, 0], [-0.95, -1.0], [0, -0.18]];

/** The body geometry at one detail level: position (part-local), aAux (side, along, part). */
export function bodyGeometry(detail = 'near') {
  const near = detail === 'near';
  const parts = [];
  const add = (pos, part, side = 0, along = 0) => parts.push({ pos, part, side, along });
  add(near ? sphere(10, 7) : sphere(6, 4), P.BODY);
  add(near ? sphere(7, 5) : sphere(4, 3), P.HEAD);
  // up to four pairs of eyes and of side fins (aAux.y = pair index); the genes decide how many show
  if (near) for (let j = 0; j < MAX_PAIRS; j++) for (const s of [1, -1]) add(sphere(4, 3), P.EYE, s, j);
  add(near ? legGeo(6, 1) : legGeo(3, 1), P.NECK);
  add(coneX(near ? 6 : 4), P.SNOUT);
  if (near) {
    add(coneX(6), P.JAW);
    for (const s of [1, -1]) for (const a of [0.2, 0.5, 0.8]) { add(spike(3, true), P.TEETH_U, s, a); add(spike(3), P.TEETH_L, s, a); }
  }
  for (const x of (near ? [-0.75, -0.5, -0.25, 0, 0.25, 0.5, 0.75] : [-0.6, 0, 0.6])) {
    for (let j = 0; j < (near ? 8 : 4); j++) add(spike(3), P.FUR, j * Math.PI * 2 / (near ? 8 : 4), x);
  }
  for (const x of [-0.7, -0.35, 0, 0.35, 0.7]) for (const side of [-1, 1]) add(spike(3), P.APPEND, side, x);
  // up to four pairs of legs (aAux.y = pair index, front first); the leg-pairs gene decides how many show
  const leg = near ? legGeo(4, 2) : flat([[-1, 0], [1, 0], [0, -1]], 'xy');
  for (let i = 0; i < MAX_PAIRS; i++) for (const s of [1, -1]) add(leg, P.LEG, s, i);
  const fin = near ? flat(SIDE_FIN, 'xz') : flat([[0.25, 0], [-0.5, 1], [-0.45, 0]], 'xz');
  for (let j = 0; j < MAX_PAIRS; j++) for (const s of [1, -1]) add(fin, P.FIN_SIDE, s, j);
  add(near ? flat(DORSAL, 'xy') : flat([[-0.5, 0], [0.3, 0], [-0.2, 1]], 'xy'), P.FIN_DORSAL);
  add(coneNX(near ? 6 : 3), P.TAIL);
  add(near ? flat(TAIL_FAN, 'xy', [0, 0]) : flat([[0, 0], [-0.95, 1], [-0.95, -1]], 'xy'), P.TAIL_FIN);
  const wing = near ? flat(WING, 'xz') : flat([[0.5, 0], [0.1, 1], [-0.4, 0.85], [-0.5, 0]], 'xz');
  add(wing, P.WING, 1); add(wing, P.WING, -1);
  if (near) {
    for (const a of [-0.6, -0.3, 0, 0.3, 0.6]) add(spike(4), P.PLATE, 0, a);
    for (const s of [1, -1]) for (const a of [-0.5, -0.17, 0.17, 0.5]) add(spike(3), P.SCUTE, s, a);
  }
  let n = 0; for (const q of parts) n += q.pos.length / 3;
  const pos = new Float32Array(n * 3), aux = new Float32Array(n * 3);
  let o = 0;
  for (const q of parts) {
    const m = q.pos.length / 3;
    pos.set(q.pos, o * 3);
    for (let i = 0; i < m; i++) { aux[(o + i) * 3] = q.side; aux[(o + i) * 3 + 1] = q.along; aux[(o + i) * 3 + 2] = q.part; }
    o += m;
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  g.setAttribute('aAux', new THREE.BufferAttribute(aux, 3));
  g.computeVertexNormals();      // unused (flat shading), but keeps the standard attribute present
  // Includes volume-normalized extreme proportions, extended neck/tail/wings and animation.
  g.boundingSphere = new THREE.Sphere(new THREE.Vector3(0, 0, 0), 12);
  g.boundingBox = new THREE.Box3(new THREE.Vector3(-12, -12, -12), new THREE.Vector3(12, 12, 12));
  return g;
}

/** Triangle count of a body geometry. */
export function triangles(g) { return g.attributes.position.count / 3; }

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
  return { mesh, cap, anim, geneA, geneB, clade, morph };
}

/**
 * Write organism k of a body mesh: position (x, height, z), heading h (radians, world convention: +x
 * forward, turning toward +z), scale sx/sy, the body b = { legs, fins, wings, sight, armour, strike,
 * fur, append, appearance, legPairs, finPairs, eyePairs, loco }, animation (gait, phase, acts, rest) and
 * the identification-overlay colour (linear r, g, b). Missing appearance uses stable neutral defaults.
 */
export function setBody(B, k, x, y, z, h, sx, sy, b, gait, phase, acts, rest, r, g, bl) {
  const M = B.mesh.instanceMatrix.array, o = k * 16, ca = Math.cos(h), sa = Math.sin(h);
  M[o] = ca * sx; M[o + 1] = 0; M[o + 2] = sa * sx; M[o + 3] = 0;
  M[o + 4] = 0; M[o + 5] = sy; M[o + 6] = 0; M[o + 7] = 0;
  M[o + 8] = -sa * sx; M[o + 9] = 0; M[o + 10] = ca * sx; M[o + 11] = 0;
  M[o + 12] = x; M[o + 13] = y; M[o + 14] = z; M[o + 15] = 1;
  const k4 = k * 4, A = B.anim.array, GA = B.geneA.array, GB = B.geneB.array, CL = B.clade.array;
  A[k4] = gait; A[k4 + 1] = phase; A[k4 + 2] = acts; A[k4 + 3] = rest;
  GA[k4] = b.legs; GA[k4 + 1] = b.fins; GA[k4 + 2] = b.wings; GA[k4 + 3] = b.sight;
  GB[k4] = b.armour; GB[k4 + 1] = b.strike; GB[k4 + 2] = b.fur || 0; GB[k4 + 3] = b.append || 0;
  CL[k4] = r; CL[k4 + 1] = g; CL[k4 + 2] = bl; CL[k4 + 3] = (LOCO_CODE[b.loco] ?? 0) * 64 + (pairsOf(b.finPairs) - 1) * 16 + (pairsOf(b.eyePairs) - 1) * 4 + (pairsOf(b.legPairs) - 1);
  const appearance = b.appearance || DEFAULT_APPEARANCE;
  for (let j = 0; j < 4; j++) for (let c = 0; c < 4; c++) B.morph[j].array[k4 + c] = appearance[j * 4 + c];
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
