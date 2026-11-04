// Module bodies in the live view (2026-11-03): every animal drawn from its real grown body, the body the Body Lab shows.
//
// The server sends each living clade's representative grown body (the 'clades' message's `morph`, docs/ICD.md: its
// segments and appendage pairs, grown at its mass by Morph.Development, each pair's role from the physics, and the chance
// the body topples). Animals of a clade differ mostly in mass, so each clade's body is built once, by the Body Lab's own
// builder (js/bodylab/build.js buildBody, at a coarse detail), merged into one geometry and drawn as one InstancedMesh:
// an instance is placed, turned to its heading and scaled by the animal's own true length over the representative's
// (status.bodyLenM; else its mass^(1/3)): true size, always.
//
// Two detail levels per clade (near: the lab's look at a few thousand triangles; far: no fringes, knuckles or mouth, a
// few hundred), each built when first needed (at most one build a frame), and a lying variant for a body that would
// topple (BodyPose.Topple, as the sim's Live.StandPosture), used when it stands on the ground; a swimmer in open water or
// a flier stays upright. Animation in the vertex shader, by each pair's role: support limbs swing fore and aft in
// alternation, swimming and flying pairs beat up and down (the two sides together), steering pairs trim, the rest sway;
// a swimmer's or crawler's trunk bends in a wave running to the tail. Display only.

import * as THREE from 'three';
import { buildBody, disposeBody } from '../bodylab/build.js';

const ROLE = { passive: 0, support: 1, swim: 2, fly: 3, steer: 4, handle: 5, breathe: 6 };
const MAX_CLADE_MESHES = 32;     // clades with a module body at once (the clade table has 32 slots)
const BUILDS_PER_FRAME = 1;      // geometries built per frame (each takes a few ms)
const TRI_BUDGET = 6e6;          // module-body triangles a frame; past it a far animal is drawn as the template
const NEAR_TRI_BUDGET = 3e6;     // ... of them at near detail; past it a near animal gets the far detail

/** A clade table `morph` (mm, degrees; ICD "clades") as the lab builder's body (metres, radians). */
export function labBodyOfMorph(mo) {
  if (!mo || !Array.isArray(mo.modules) || mo.modules.length === 0) return null;
  let x0 = 0;
  const segs = mo.modules.map((s) => {
    const o = { l: Math.max(1e-4, (s.l || 0) / 1000), w: Math.max(1e-4, (s.w || 0) / 1000), h: Math.max(1e-4, (s.h || 0) / 1000), x0,
      intMin: s.rigid || 0, intT: (s.intT ?? 0.02 * (s.h || 0)) / 1000, pore: s.pore || 0, invag: s.invag ?? 0.5 };
    x0 += o.l; return o;
  });
  const pairs = (mo.appendages || []).map((a) => {
    const len = Math.max(1e-4, (a.len || 0) / 1000), flat = Math.max(1, a.flat || 1);
    // (an older server sent the flatness only: a limb about a tenth as thick as long, by its flatness)
    const thick = a.thick !== undefined ? a.thick / 1000 : 0.12 * len / Math.sqrt(flat), chord = a.chord !== undefined ? a.chord / 1000 : thick * flat;
    return { seg: a.seg | 0, len, chord: Math.max(1e-5, chord), thick: Math.max(1e-5, thick), theta: (a.droop || 0) * Math.PI / 180, joints: a.joints | 0,
      sagittal: !!a.sagittal, mineral: a.rigid || 0, exo: a.exo ?? ((a.rigid || 0) > 0.3 ? 1 : 0), pore: a.pore || 0, invag: a.invag ?? 0.5, role: a.role || 'passive' };
  });
  return { segs, pairs, topple: mo.topple || 0, length: mo.lengthM || x0, mass: mo.massKg || 1 };
}

/** The body's box (metres, limbs and all: x along it, y up, z across), estimated from its parts before its geometry is
 *  built (then replaced by the built geometry's own, mergedBody ext). */
export function estimateExtent(b) {
  let L = 0, H = 0, W = 0, limb = 0, leg = 0;
  for (const s of b.segs) { L += s.l; H = Math.max(H, s.h); W = Math.max(W, s.w); }
  for (const p of b.pairs) { limb = Math.max(limb, p.len); if (p.role === 'support') leg = Math.max(leg, p.len * Math.cos(Math.min(1.2, Math.abs(p.theta)))); }
  return { x: Math.max(L, b.length || 0) + 0.5 * limb, y: H + leg, z: W + 1.6 * limb };
}

/** What a clade body looks like, coarsely: a new representative of the same shape keeps the built geometry. */
function signature(b) {
  const L = b.length || 1, q = (v, k = 20) => Math.round(v / L * k);
  return [b.topple > 0.5 ? 1 : 0, b.segs.map((s) => [q(s.l), q(s.w), q(s.h), Math.round(s.intMin * 4)]),
    b.pairs.map((p) => [p.seg, q(p.len), Math.round(Math.log2(p.chord / p.thick) * 2), p.joints, Math.round(p.theta * 4), p.sagittal ? 1 : 0, p.role])].flat(3).join(',');
}

const _m = new THREE.Matrix4(), _n = new THREE.Matrix3(), _v = new THREE.Vector3(), _c = new THREE.Color();

/** The body built by the lab builder, merged into one indexed geometry in metres: centred along its length and width,
 *  standing on y = 0. Attributes: position, normal, color (its tissue's colour, linear), aRig = (the limb's root, code):
 *  code = animated + 2 tinted + 4 role + 32 left side + 64 sagittal + 128 pair index. */
export function mergedBody(b, detail, lie) {
  const built = buildBody(b, { detail, fit: false, lie });
  const g = built.group; g.updateMatrixWorld(true);
  const pos = [], nor = [], col = [], rig = [], idx = [];
  const add = (geo, M, color, r, flip) => {
    const P = geo.attributes.position, N = geo.attributes.normal, o = pos.length / 3;
    _n.getNormalMatrix(M);
    const tinted = r.kind === 'trunk' || r.kind === 'limb' ? 1 : 0, anim = r.kind === 'limb' || (r.kind === 'gill' && r.base) ? 1 : 0;
    const code = anim + 2 * tinted + 4 * (ROLE[r.role] ?? 0) + 32 * ((r.side || 0) > 0 ? 1 : 0) + 64 * (r.sagittal ? 1 : 0) + 128 * (r.pair || 0);
    const base = r.base ? _v.copy(r.base).applyMatrix4(g.matrixWorld).toArray() : [0, 0, 0];
    for (let i = 0; i < P.count; i++) {
      const p = new THREE.Vector3(P.getX(i), P.getY(i), P.getZ(i)).applyMatrix4(M);
      const n = N ? new THREE.Vector3(N.getX(i), N.getY(i), N.getZ(i)).applyMatrix3(_n).normalize() : new THREE.Vector3(0, 1, 0);
      if (flip) n.negate();
      pos.push(p.x, p.y, p.z); nor.push(n.x, n.y, n.z); col.push(color.r, color.g, color.b); rig.push(base[0], base[1], base[2], code);
    }
    if (geo.index) { const I = geo.index.array; for (let i = 0; i < I.length; i += 3) flip ? idx.push(o + I[i], o + I[i + 2], o + I[i + 1]) : idx.push(o + I[i], o + I[i + 1], o + I[i + 2]); }
    else for (let i = 0; i < P.count; i += 3) flip ? idx.push(o + i, o + i + 2, o + i + 1) : idx.push(o + i, o + i + 1, o + i + 2);
  };
  g.traverse((o) => {
    if (!o.isMesh) return;
    const r = o.userData.rig || { kind: 'trunk' }, color = (o.material && o.material.color) || _c.set(0x999999);
    if (o.isInstancedMesh) {
      for (let k = 0; k < o.count; k++) { o.getMatrixAt(k, _m); add(o.geometry, _m.clone().premultiply(o.matrixWorld), color, r, false); }
    } else {
      add(o.geometry, o.matrixWorld, color, r, false);
      if (r.twoSided) add(o.geometry, o.matrixWorld, color.clone().multiplyScalar(0.8), r, true);   // an open shell: its underside too
    }
  });
  disposeBody(g);
  // centred along its length and width, standing on y = 0
  let x0 = Infinity, x1 = -Infinity, y0 = Infinity, y1 = -Infinity, z0 = Infinity, z1 = -Infinity;
  for (let i = 0; i < pos.length; i += 3) { x0 = Math.min(x0, pos[i]); x1 = Math.max(x1, pos[i]); y0 = Math.min(y0, pos[i + 1]); y1 = Math.max(y1, pos[i + 1]); z0 = Math.min(z0, pos[i + 2]); z1 = Math.max(z1, pos[i + 2]); }
  const cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
  for (let i = 0; i < pos.length; i += 3) { pos[i] -= cx; pos[i + 1] -= y0; pos[i + 2] -= cz; }
  for (let i = 0; i < rig.length; i += 4) if (rig[i + 3] % 2 === 1) { rig[i] -= cx; rig[i + 1] -= y0; rig[i + 2] -= cz; }
  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  geo.setAttribute('normal', new THREE.Float32BufferAttribute(nor, 3));
  geo.setAttribute('color', new THREE.Float32BufferAttribute(col, 3));
  geo.setAttribute('aRig', new THREE.Float32BufferAttribute(rig, 4));
  geo.setIndex(pos.length / 3 > 65535 ? new THREE.Uint32BufferAttribute(idx, 1) : new THREE.Uint16BufferAttribute(idx, 1));
  geo.computeBoundingSphere();
  // (ext: the whole body's box, limbs and all, in metres: what the cameras frame and the culling keeps in view)
  return { geo, tris: idx.length / 3, lenX: x1 - x0, ext: { x: x1 - x0, y: y1 - y0, z: z1 - z0 } };
}

// ---- the material: the scene's own lit material, the body bent and its limbs swung in the vertex shader
const GLSL_VERT_HEAD = `
uniform float uTime; uniform float uFreq; uniform float uLen; uniform float uWave; uniform vec3 uUp; uniform vec3 uSide; uniform float uOverlay;
attribute vec4 aRig; attribute vec4 aInst; attribute vec3 aTint;
varying float vUnder;
vec3 mRot(vec3 p, vec3 c, vec3 ax, float a) { p -= c; float s = sin(a), k = cos(a); return c + p * k + cross(ax, p) * s + ax * dot(ax, p) * (1.0 - k); }
`;
// (aInst: gait 0..1, phase 0..1, the body wave's strength 0..1, how far under water 0..1)
const GLSL_BEGIN = `
vec3 objectNormal = vec3(normal);
vec3 animP = vec3(position);
float code = aRig.w;
float animd = mod(code, 2.0), role = mod(floor(code / 4.0), 8.0), leftS = mod(floor(code / 32.0), 2.0), sag = mod(floor(code / 64.0), 2.0), pairI = floor(code / 128.0);
float sideS = leftS * 2.0 - 1.0, gait = aInst.x, T = 6.2832 * (uTime * uFreq + aInst.y);
vec3 piv = aRig.xyz;
if (animd > 0.5) {
  vec3 ax = uUp; float a = 0.0;
  if (role == 1.0) { a = 0.5 * gait * sin(T + 3.1416 * (pairI + leftS)); }                                 // support: alternate swing fore and aft
  else if (role == 2.0) { a = (0.2 + 0.35 * max(gait, aInst.z)) * sin(1.3 * T + 0.7 * pairI); if (sag < 0.5) { ax = vec3(1.0, 0.0, 0.0); a *= sideS; } }   // swim: beat
  else if (role == 3.0) { ax = vec3(1.0, 0.0, 0.0); a = 0.75 * sideS * sin(2.6 * T); }                      // fly: flap
  else if (role == 4.0) { ax = vec3(1.0, 0.0, 0.0); a = 0.12 * sideS * sin(0.7 * T + pairI); }              // steer: trim
  else { a = 0.06 * sin(0.5 * T + pairI); }                                                                 // the rest sway
  animP = mRot(animP, piv, ax, a);
  objectNormal = mRot(objectNormal, vec3(0.0), ax, a);
}
// the body wave: lateral, growing toward the tail (x from +L/2 at the head to -L/2), one wavelength over the body
float xr = (animd > 0.5 ? piv.x : position.x) / max(uLen, 1e-6);
float env = mix(0.2, 1.0, smoothstep(0.5, -0.5, xr));
animP += uSide * (uWave * uLen * aInst.z * env * sin(6.2832 * xr - 1.5 * T));
vUnder = aInst.w;
`;
const GLSL_COLOR_VERT = `
#ifdef USE_COLOR
  float tinted = mod(floor(aRig.w / 2.0), 2.0);
  // the animal's own pigment (its appearance genes; the clade colour under the overlay) over its tissue's colour, a
  // darker back and a paler belly
  vColor.rgb = mix(vColor.rgb, aTint, tinted * mix(0.55, 0.8, uOverlay));
  vColor.rgb *= mix(1.15, 0.7, 0.5 + 0.5 * dot(normalize(normal), uUp));
#endif
`;

function moduleMaterial(shared, near, body) {
  const mat = near ? new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.62, metalness: 0 }) : new THREE.MeshLambertMaterial({ vertexColors: true });
  const lie = body.lying;
  const u = {
    uTime: shared.uTime, uOverlay: shared.uOverlay, uWaterCol: shared.uWaterCol,
    uFreq: { value: body.freq }, uLen: { value: body.len }, uWave: { value: 0.07 },
    // lying on its right side (build.js turns it about x by +90 degrees): its back faces +z and its left side -y
    uUp: { value: lie ? new THREE.Vector3(0, 0, 1) : new THREE.Vector3(0, 1, 0) }, uSide: { value: lie ? new THREE.Vector3(0, -1, 0) : new THREE.Vector3(0, 0, 1) },
  };
  mat.userData.uniforms = u;
  mat.onBeforeCompile = (sh) => {
    Object.assign(sh.uniforms, u);
    sh.vertexShader = sh.vertexShader
      .replace('#include <common>', '#include <common>\n' + GLSL_VERT_HEAD)
      .replace('#include <beginnormal_vertex>', GLSL_BEGIN)
      .replace('#include <begin_vertex>', 'vec3 transformed = animP;')
      .replace('#include <color_vertex>', '#include <color_vertex>\n' + GLSL_COLOR_VERT);
    sh.fragmentShader = sh.fragmentShader
      .replace('#include <common>', '#include <common>\nuniform vec3 uWaterCol; varying float vUnder;')
      .replace('#include <fog_fragment>', 'gl_FragColor.rgb = mix(gl_FragColor.rgb, uWaterCol, vUnder);\n#include <fog_fragment>');
  };
  mat.customProgramCacheKey = () => 'neuroreef-modular-v1-' + (near ? 'n' : 'f');
  return mat;
}

/**
 * The module bodies of every living clade. setClades(list) with each clade table; per frame begin(), place() each
 * animal that should be drawn so (false: the template is drawn instead), finish().
 */
export class ModularBodies {
  constructor(scene, { shared, shadows = false } = {}) {
    this.scene = scene; this.shadows = shadows;
    this.shared = { uTime: shared?.uTime || { value: 0 }, uWaterCol: shared?.uWaterCol || { value: new THREE.Color(0.03, 0.12, 0.16) }, uOverlay: { value: 0 } };
    this.clades = new Map();   // clade id -> { sig, body, lengthM, massKg, topple, variants: Map(key -> variant) }
    this.builds = 0; this.stats = { instances: 0, tris: 0, meshes: 0, buildMs: 0 };
  }

  /** The clade table (meta.species, each 'clades' frame): a clade with a grown body gets (or keeps) its module body; an
   *  extinct one, or one whose body changed shape, loses its meshes. */
  setClades(list) {
    if (!Array.isArray(list)) return;
    const seen = new Set();
    for (const c of list) {
      if (!c || c.id === undefined || !c.morph || c.alive === false) continue;
      const body = labBodyOfMorph(c.morph); if (!body) continue;
      if (!this.clades.has(c.id) && this.clades.size >= MAX_CLADE_MESHES) continue;
      seen.add(c.id);
      const sig = signature(body), old = this.clades.get(c.id);
      // the representative's length and mass are the scale the instances are drawn at (they change with it)
      if (old && old.sig === sig) { old.lengthM = body.length; old.massKg = body.mass; continue; }
      if (old) this._drop(old);
      this.clades.set(c.id, { sig, body, lengthM: body.length, massKg: body.mass, topple: body.topple, crawler: !body.pairs.some((p) => p.role === 'support'), variants: new Map(),
        ext: estimateExtent(body), extBuilt: false });
    }
    for (const [id, e] of this.clades) if (!seen.has(id)) { this._drop(e); this.clades.delete(id); }
  }

  /** The clade's module body ({ lengthM, massKg, topple, ext: its box in metres of the representative, upright }), or
   *  undefined (it is drawn as the template). */
  get(sp) { return this.clades.get(sp); }

  begin(overlay) {
    this.builds = 0; this.tris = 0; this.nearTris = 0; this.shared.uOverlay.value = overlay ? 1 : 0;
    for (const e of this.clades.values()) for (const v of e.variants.values()) v.n = 0;
  }

  /** Draw one animal of clade sp: near or far detail, lying or upright; at (x, y, z) heading h (world convention), s world
   *  units per metre of the representative's body (sy vertically), sheared to the ground's slope (sf, ss); gait, phase,
   *  wave, under 0..1; its pigment (linear r, g, b). False when it cannot be drawn so this frame. */
  place(sp, near, lie, x, y, z, h, s, sy, sf, ss, gait, phase, wave, under, r, g, b) {
    const e = this.clades.get(sp); if (!e) return false;
    if (near && this.nearTris > NEAR_TRI_BUDGET) near = false;
    const key = (near ? 1 : 0) + (lie ? 2 : 0);
    let v = e.variants.get(key);
    if (!v && this.builds >= BUILDS_PER_FRAME) {   // not built yet, and this frame's build is spent: the other detail if it is there
      v = e.variants.get(key ^ 1); if (!v || !v.geo) return false;
      near = v.near;
    }
    if (!v) {
      this.builds++;
      v = this._build(e, near, lie); e.variants.set(key, v);
      if (!v) return false;
    }
    if (!v.geo || (!near && this.tris + v.tris > TRI_BUDGET)) return false;
    this.tris += v.tris; if (near) this.nearTris += v.tris;
    if (v.n >= v.cap) this._grow(v, v.cap * 2);
    const k = v.n++, M = v.mesh.instanceMatrix.array, o = k * 16, ca = Math.cos(h), sa = Math.sin(h);
    M[o] = ca * s; M[o + 1] = sf * s; M[o + 2] = sa * s; M[o + 3] = 0;
    M[o + 4] = 0; M[o + 5] = sy; M[o + 6] = 0; M[o + 7] = 0;
    M[o + 8] = -sa * s; M[o + 9] = ss * s; M[o + 10] = ca * s; M[o + 11] = 0;
    M[o + 12] = x; M[o + 13] = y; M[o + 14] = z; M[o + 15] = 1;
    const I = v.inst.array, T = v.tint.array;
    I[k * 4] = gait; I[k * 4 + 1] = phase; I[k * 4 + 2] = wave; I[k * 4 + 3] = under;
    T[k * 3] = r; T[k * 3 + 1] = g; T[k * 3 + 2] = b;
    return true;
  }

  finish() {
    let inst = 0, tris = 0, meshes = 0;
    for (const e of this.clades.values()) for (const v of e.variants.values()) {
      if (!v.mesh) continue;
      v.mesh.count = v.n; v.mesh.visible = v.n > 0;
      if (v.n === 0) continue;
      meshes++; inst += v.n; tris += v.n * v.tris;
      for (const [a, w] of [[v.mesh.instanceMatrix, 16], [v.inst, 4], [v.tint, 3]]) { a.clearUpdateRanges(); a.addUpdateRange(0, v.n * w); a.needsUpdate = true; }
    }
    this.stats.instances = inst; this.stats.tris = tris; this.stats.meshes = meshes;
  }

  _build(e, near, lie) {
    const t0 = performance.now();
    let m;
    try { m = mergedBody(e.body, near ? 'near' : 'far', lie); } catch (err) { console.warn('module body', err); return { geo: null }; }
    // the limbs' beat: about a pendulum's of the body's length (a 10 cm body ~1.6 Hz, a 1 m one ~0.5 Hz)
    const L = Math.max(0.005, e.body.length);
    const mat = moduleMaterial(this.shared, near, { lying: lie, len: m.lenX, freq: Math.min(4, 0.5 * Math.sqrt(9.8 / L) / Math.PI) });
    const v = { geo: m.geo, tris: m.tris, mat, mesh: null, cap: 0, n: 0, inst: null, tint: null, near };
    if (!lie && (!e.extBuilt || near)) { e.ext = m.ext; e.extBuilt = true; }   // the real box, once there is geometry
    this._grow(v, 64);
    this.stats.buildMs = performance.now() - t0;
    return v;
  }

  _grow(v, cap) {
    const mesh = new THREE.InstancedMesh(v.geo, v.mat, cap);
    mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
    mesh.frustumCulled = false;   // its instances cover the world
    const inst = new THREE.InstancedBufferAttribute(new Float32Array(cap * 4), 4), tint = new THREE.InstancedBufferAttribute(new Float32Array(cap * 3), 3);
    inst.setUsage(THREE.DynamicDrawUsage); tint.setUsage(THREE.DynamicDrawUsage);
    if (v.mesh) {
      mesh.instanceMatrix.array.set(v.mesh.instanceMatrix.array); inst.array.set(v.inst.array); tint.array.set(v.tint.array);
      this.scene.remove(v.mesh); v.mesh.dispose();
    }
    v.geo.setAttribute('aInst', inst); v.geo.setAttribute('aTint', tint);
    if (this.shadows && v.near) { mesh.castShadow = true; mesh.receiveShadow = true; }
    v.mesh = mesh; v.cap = cap; v.inst = inst; v.tint = tint;
    this.scene.add(mesh);
  }

  _drop(e) {
    for (const v of e.variants.values()) {
      if (v.mesh) { this.scene.remove(v.mesh); v.mesh.dispose(); }
      if (v.geo) v.geo.dispose();
      if (v.mat) v.mat.dispose();
    }
    e.variants.clear();
  }

  dispose() { for (const e of this.clades.values()) this._drop(e); this.clades.clear(); }
}
