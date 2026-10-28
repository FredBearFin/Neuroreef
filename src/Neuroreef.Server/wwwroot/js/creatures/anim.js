// Animated creature material: a Lambert material whose vertex shader moves the tagged parts of a model
// (kit.js P) about their pivots. Per instance it reads aAnim = (gait 0..1, phase, action bits, resting) and
// aBody = the animal's own genes (speed, armour, diet drift; -1..1), so evolution shows in the bodies:
// faster lineages grow longer legs (or sleeker bodies), armoured ones bulk up and darken, and a diet
// drifting toward meat reddens the coat while one drifting toward plants greens it.
// No skeletons: every animal of a species is still one instanced draw call.

import * as THREE from 'three';

/** Default motion per locomotion; a species model may override any field. */
export function motionFor(sp, override = {}) {
  const base = { hip: 0, stride: 7, legAmp: 0.55, wingAmp: 0, wingFreq: 0, tailAmp: 0.25, wave: 0, headDip: 0.5, jitter: 0, hover: 0 };
  if (sp.loco === 'Fly') Object.assign(base, { wingAmp: 0.9, wingFreq: 16, legAmp: 0.2, hover: 0.15 });
  if (sp.loco === 'Swim') Object.assign(base, { wave: 0.12, stride: 5, legAmp: 0.3, tailAmp: 0.45 });
  if (sp.swarm) Object.assign(base, { jitter: 0.12, wingAmp: sp.loco === 'Fly' ? 1.0 : 0, wingFreq: 30 });
  return Object.assign(base, override);
}

export function creatureMaterial(sp, motion, opts = {}) {
  const mat = new THREE.MeshLambertMaterial({ vertexColors: true, flatShading: true, transparent: !!opts.transparent, opacity: opts.opacity ?? 1,
    emissive: new THREE.Color(sp.color).multiplyScalar(0.12) });
  const u = { uTime: { value: 0 } };
  mat.userData.uniforms = u;
  mat.onBeforeCompile = (sh) => {
    Object.assign(sh.uniforms, u);
    const M = motion, f = (v) => Number(v).toFixed(4);
    sh.vertexShader = sh.vertexShader
      .replace('#include <common>', `#include <common>
uniform float uTime;
attribute float aPart; attribute vec3 aPivot; attribute vec4 aAnim; attribute vec4 aBody;
vec3 rz(vec3 p, vec3 c, float a) { p -= c; float s = sin(a), k = cos(a); return vec3(p.x * k - p.y * s, p.x * s + p.y * k, p.z) + c; }
vec3 rx(vec3 p, vec3 c, float a) { p -= c; float s = sin(a), k = cos(a); return vec3(p.x, p.y * k - p.z * s, p.y * s + p.z * k) + c; }
vec3 ry(vec3 p, vec3 c, float a) { p -= c; float s = sin(a), k = cos(a); return vec3(p.x * k + p.z * s, p.y, -p.x * s + p.z * k) + c; }
float bit(float v, float b) { return mod(floor(v / b), 2.0); }`)
      .replace('#include <begin_vertex>', `#include <begin_vertex>
{
  float gait = aAnim.x, ph = aAnim.y * 6.2832, acts = aAnim.z, rest = aAnim.w;
  float eat = max(bit(acts, 1.0), bit(acts, 4.0)), strike = bit(acts, 2.0);
  float t = uTime;
  float part = floor(aPart + 0.5);
  float cyc = t * ${f(M.stride)} + ph;
  // ---- the body this animal's genes built
  float gs = aBody.x, ga = aBody.y;
  if (${f(M.hip)} > 0.0) {
    // legged: speed genes lengthen the legs from the hip; the whole body rises to keep the feet on the ground
    if (part >= 2.0 && part <= 5.0) transformed = aPivot + (transformed - aPivot) * vec3(1.0, 1.0 + 0.3 * gs, 1.0);
    transformed.y += ${f(M.hip)} * 0.3 * gs;
  } else {
    // legless (fish, swarms, mats): speed genes make the body longer and sleeker
    transformed.x *= 1.0 + 0.15 * gs; transformed.yz *= 1.0 - 0.07 * gs;
  }
  // armour genes: a broader, heavier body and head
  if (part <= 1.0) transformed.z *= 1.0 + 0.22 * ga;
  // legs: diagonal pairs in step (a trot); amplitude grows with speed
  if (part >= 2.0 && part <= 5.0) {
    float off = (part == 2.0 || part == 5.0) ? 0.0 : 3.1416;
    float a = sin(cyc + off) * ${f(M.legAmp)} * gait * (1.0 - rest);
    transformed = rz(transformed, aPivot, a);
  }
  // wings: flap, faster and wider in flight; folded at rest
  if (part == 6.0 || part == 7.0) {
    float side = part == 6.0 ? 1.0 : -1.0;
    float a = (sin(t * ${f(M.wingFreq)} + ph) * ${f(M.wingAmp)} + 0.15) * (1.0 - 0.8 * rest);
    transformed = rx(transformed, aPivot, side * a);
  }
  // tail: sways, more when moving
  if (part == 8.0) transformed = ry(transformed, aPivot, sin(t * 2.3 + ph) * ${f(M.tailAmp)} * (0.35 + gait));
  // head: dips to eat or drink, lunges with a strike
  if (part == 1.0 || part == 9.0) {
    transformed = rz(transformed, aPivot, -eat * ${f(M.headDip)} * (0.75 + 0.25 * sin(t * 9.0 + ph)));
  }
  if (part == 9.0) transformed = rz(transformed, aPivot, -strike * 0.5 * abs(sin(t * 14.0 + ph)) - eat * 0.25 * abs(sin(t * 10.0 + ph)));
  // swimmers: a travelling wave along the body
  transformed.z += sin(position.x * 4.0 - t * ${f(M.stride * 1.6)} - ph) * ${f(M.wave)} * (0.4 + gait) * clamp(0.6 - position.x * 0.8, 0.0, 1.6);
  // swarm members: each wanders about its own centre
  if (part == 10.0) {
    float k = dot(aPivot, vec3(12.9898, 78.233, 37.719));
    transformed += vec3(sin(t * 3.1 + k), sin(t * 4.3 + k * 1.7) * 0.6, cos(t * 2.7 + k * 0.6)) * ${f(M.jitter)};
  }
  // strike: the whole body lunges forward; flyers hover
  transformed.x += strike * 0.12 * abs(sin(t * 7.0 + ph));
  transformed.y += ${f(M.hover)} * sin(t * 3.0 + ph) * (1.0 - rest);
  // breathing
  transformed.y *= 1.0 + 0.015 * sin(t * 2.0 + ph);
}`);
  };
  const prevFrag = mat.onBeforeCompile;
  mat.onBeforeCompile = (sh) => {
    prevFrag(sh);
    // colour: armour darkens, diet drift tints (toward red for meat, green for plants)
    sh.vertexShader = sh.vertexShader.replace('#include <color_vertex>', `#include <color_vertex>
  vColor.rgb *= 1.0 - 0.28 * clamp(aBody.y, -0.5, 1.0);
  vColor.rgb = mix(vColor.rgb, vColor.rgb * vec3(1.25, 0.78, 0.74), clamp(aBody.z, 0.0, 1.0));
  vColor.rgb = mix(vColor.rgb, vColor.rgb * vec3(0.8, 1.2, 0.8), clamp(-aBody.z, 0.0, 1.0));`);
  };
  mat.customProgramCacheKey = () => 'creature:' + sp.id + ':' + JSON.stringify(motion);
  return mat;
}

/** Give a geometry (shared or cloned per mesh) its per-instance animation attribute. */
export function withAnim(geo, cap) {
  const g = geo.clone();
  g.setAttribute('aAnim', new THREE.InstancedBufferAttribute(new Float32Array(cap * 4), 4));
  g.setAttribute('aBody', new THREE.InstancedBufferAttribute(new Float32Array(cap * 4), 4));
  return g;
}
