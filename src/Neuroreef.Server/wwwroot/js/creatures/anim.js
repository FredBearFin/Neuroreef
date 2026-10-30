// The body material: a Lambert material whose vertex shader builds each organism's body from its genes and
// animates it. The geometry (body.js) holds every part in its own unit frame; per instance the shader reads
//   aGeneA = (legs, fins, wings, sight)        organ sizes 0..1
//   aGeneB = (armour, strike, fur, appendages) organ sizes 0..1
//   aMorphA/B/C/D = the sixteen normalized, inherited appearance genes in wire order
//   aAnim  = (gait 0..1, phase 0..1, action bits, resting)
//   aClade = (clade colour as linear RGB, packed counts): w = loco * 64 + (fin pairs - 1) * 16 + (eye pairs - 1) * 4
//            + (leg pairs - 1); loco 0 walk, 1 wade, 2 swim, 3 fly, 4 settled; each count 1..4
// and from them places, sizes and colours the parts: legs grow from the hips and lift the body, fins sprout
// along the sides, back and tail, wings from the shoulders, a long toothed snout from the strike gene,
// plates along the back from armour. Locomotion follows the sim's rule (Body.cs): fly if the wings can lift the body
// (wings * 1.6 >= mass^1/3, wing loading), else walk, swim or wade by speed, settled without organs; the server sends
// the result. Wings and fins not in use fold back against the body.
// The number of legs (2 to 8, in pairs) is a gene too: the pairs spread along the trunk. No skeletons and no per-clade
// meshes: every organism is one instance of the same mesh.

import * as THREE from 'three';

const GLSL_COMMON = `
uniform float uTime;
uniform float uSpeciesOverlay;
attribute vec3 aAux;
attribute vec4 aAnim; attribute vec4 aGeneA; attribute vec4 aGeneB; attribute vec4 aClade;
attribute vec4 aMorphA; attribute vec4 aMorphB; attribute vec4 aMorphC; attribute vec4 aMorphD;
varying vec3 vCol; varying vec3 vMark; varying vec3 vBody; varying vec4 vPattern;
vec3 pigment(float h, float s, float l) {
  vec3 rgb = clamp(abs(mod(h * 6.0 + vec3(0.0, 4.0, 2.0), 6.0) - 3.0) - 1.0, 0.0, 1.0);
  rgb = l + s * (rgb - 0.5) * (1.0 - abs(2.0 * l - 1.0));
  return mix(rgb / 12.92, pow((rgb + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), rgb));
}
vec3 rz(vec3 p, vec3 c, float a) { p -= c; float s = sin(a), k = cos(a); return vec3(p.x * k - p.y * s, p.x * s + p.y * k, p.z) + c; }
vec3 rx(vec3 p, vec3 c, float a) { p -= c; float s = sin(a), k = cos(a); return vec3(p.x, p.y * k - p.z * s, p.y * s + p.z * k) + c; }
vec3 ry(vec3 p, vec3 c, float a) { p -= c; float s = sin(a), k = cos(a); return vec3(p.x * k + p.z * s, p.y, -p.x * s + p.z * k) + c; }
float bit(float v, float b) { return mod(floor(v / b), 2.0); }
`;

const GLSL_BODY = `
{
  vec3 L = position;
  float legs = aGeneA.x, fins = aGeneA.y, wings = aGeneA.z, sight = aGeneA.w;
  float armour = aGeneB.x, strike = aGeneB.y, fur = aGeneB.z, append = aGeneB.w;
  float gait = aAnim.x, ph = aAnim.y * 6.2832, acts = aAnim.z, rest = aAnim.w;
  float eat = max(bit(acts, 1.0), bit(acts, 4.0)), strikeA = bit(acts, 2.0);
  float t = uTime;
  float part = floor(aAux.z + 0.5), side = aAux.x, px = aAux.y;
  // locomotion from the organs (Body.cs)
  float pk = floor(aClade.w + 0.5), loco = floor(pk / 64.0);
  float finPairs = mod(floor(pk / 16.0), 4.0) + 1.0, eyePairs = mod(floor(pk / 4.0), 4.0) + 1.0, pairs = mod(pk, 4.0) + 1.0;
  float fl = 1.0 - step(0.5, abs(loco - 3.0));
  float wk = step(loco, 1.5) * (1.0 - fl);
  float sw = (1.0 - step(0.5, abs(loco - 1.0)) + 1.0 - step(0.5, abs(loco - 2.0))) * (1.0 - fl);
  float fish = sw * (1.0 - wk);                       // a pure swimmer
  float sess = (1.0 - fl) * (1.0 - wk) * (1.0 - sw);  // settled
  float air = fl * (1.0 - rest);                      // flying now
  // Each axis spans 0.5..2 before volume normalization; mass remains the world-space scale.
  vec3 axes = exp(0.69314718 * (2.0 * aMorphA.xyz - 1.0));
  axes /= pow(axes.x * axes.y * axes.z, 1.0 / 3.0);
  float rxB = 0.55 * axes.x, ryB = 0.32 * axes.z, rzB = 0.30 * axes.y;
  float limbPos = aMorphB.z, limbShape = aMorphB.w;
  float finShape = aMorphC.x, wingShape = aMorphC.y;
  // legs: length and thickness from the legs gene (thicker on heavy or armoured bodies); nothing when tiny
  float legE = smoothstep(0.05, 0.3, legs);
  float legLen = (0.12 + 1.05 * legs) * legE * mix(0.55, 1.55, limbShape);
  float legR = (0.035 + 0.045 * legs + 0.03 * armour) * legE * mix(1.7, 0.55, limbShape);
  // legs scale with body mass as bone must (elastic similarity, McMahon 1975: length ~ M^1/4, diameter ~ M^3/8), not
  // as the body does (all lengths ~ M^1/3): a heavy body stands on shorter, stouter legs, a light one on thin long ones.
  // The model is scaled by mass^1/3 (bodySize = 1.5 cbrt(M)), so relative to M = 1 legs go as M^-1/12 long, M^+1/24 thick
  float massI = pow(max(length(instanceMatrix[0].xyz) / 1.5, 0.05), 3.0);
  legLen *= pow(massI, -1.0 / 12.0);
  legR *= pow(massI, 1.0 / 24.0) * pow(massI, 1.0 / 12.0);   // thickness against length: d/L ~ M^1/8
  // appendages shed heat (Allen's rule): ears, a longer tail and longer distal limbs, not extra parts
  legLen *= 1.0 + 0.2 * append;
  // posture follows size (Biewener 1989): small bodies crouch on bent limbs, big ones stand on near-columnar ones;
  // slender limbs (limbShape) stand straighter. The leg is two segments of legLen / 2 bent at the knee.
  float big = clamp(log(massI) / 5.99 + 0.38, 0.0, 1.0);    // 0 at M = 0.1 .. 1 at M = 40
  float crouch = mix(0.45, 0.08, big) * mix(1.3, 0.6, limbShape);
  float legH = legLen * sqrt(1.0 - crouch * crouch);         // hip height above the foot
  // fur is volume: it thickens the body's outline (the tufts are short)
  float furV = 1.0 + 0.22 * fur;
  // the body stands on its legs, or lies on its belly; a swimmer is centred on the water line
  float cy = max(ryB * 0.85 * furV, 0.45 * ryB + legH * 0.97);
  // fins fold back against the body out of water (walking, flying); a wader or swimmer spreads them
  float finFold = (1.0 - sw) * (1.0 - sess);
  vec3 C = vec3(0.0, cy, 0.0);
  float finE = smoothstep(0.06, 0.3, fins);
  vec3 p = L, surface = L;

  if (part < 0.5) {
    // torso: a fuller chest tapering into the tail root, a flatter belly on a standing body, and shoulder / hip
    // mass where the legs attach (grown with the legs gene, absent without legs)
    float xf = L.x;
    vec3 Ls = L * vec3(1.0, 1.0 + 0.1 * xf - 0.22 * xf * xf * step(xf, 0.0), 1.0 + 0.08 * xf - 0.12 * xf * xf * step(xf, 0.0));
    Ls.y *= L.y < 0.0 ? mix(1.0, 0.82, legE * wk) : 1.0;
    float hs = mix(0.2, 0.85, limbPos), ho = (limbPos - 0.5) * 0.25;
    float girdle = pairs < 1.5 ? exp(-pow((xf - ho) * 3.5, 2.0))
                               : exp(-pow((xf - ho - hs) * 3.5, 2.0)) + exp(-pow((xf - ho + hs) * 3.5, 2.0));
    Ls.yz *= 1.0 + 0.13 * legE * girdle * smoothstep(0.6, -0.2, L.y);
    surface = C + Ls * vec3(rxB, ryB, rzB);
    p = C + Ls * vec3(rxB, ryB, rzB) * furV;
  } else if (part < 6.5 || (part > 14.5 && part < 15.5)) {
    float hr = 0.2 * mix(0.35, 2.0, aMorphA.w);
    float nl = rxB * 1.6 * aMorphB.x;
    vec3 neck = C + vec3(rxB * 0.65, ryB * 0.15, 0.0);
    vec3 H = C + vec3(rxB * 0.9 + hr * 0.3 + nl * 0.75, ryB * 0.25 + nl * 0.3, 0.0);
    float sl = hr * (0.45 + 1.15 * strike);                       // snout length
    vec3 So = H + vec3(hr * 0.55, -hr * 0.1, 0.0) * furV, Jo = H + vec3(hr * 0.3, -hr * 0.42, 0.0) * furV;
    float th = hr * 0.42 * smoothstep(0.15, 0.5, strike);         // tooth length
    float jawA = -strikeA * (0.3 + 0.6 * strike) * abs(sin(t * 9.0 + ph)) - eat * 0.2 * abs(sin(t * 10.0 + ph));
    if (part < 1.5) { surface = H + L * vec3(hr * 1.15, hr * 0.9, hr * 0.85); p = H + L * vec3(hr * 1.15, hr * 0.9, hr * 0.85) * furV; }
    else if (part < 2.5) {
      // eyes: as many pairs as the genes say, set back and up the head; more eyes, each smaller
      float j = px, has = step(j + 0.5, eyePairs);
      float er = hr * (0.14 + 0.3 * sight) / (1.0 + 0.3 * (eyePairs - 1.0));
      p = H + vec3(hr * (0.58 - 0.2 * j), hr * (0.36 + 0.13 * j), side * hr * (0.6 - 0.04 * j)) * furV + L * er * has;
    }
    else if (part < 3.5) p = So + L * vec3(sl, hr * 0.42, hr * 0.5);
    else if (part < 4.5) {
      surface = Jo + L * vec3(sl * 0.92, hr * 0.22, hr * 0.42);
      p = rz(surface, Jo, jawA);
    }
    else if (part > 14.5) {
      vec3 d = H - neck, n1 = normalize(vec3(-d.y, d.x, 0.0) + 1e-5);
      float tt = -L.y;
      // a continuous neck: as thick as the chest where it leaves the body, narrowing to the head
      p = neck + d * tt + (n1 * L.x + vec3(0.0, 0.0, L.z)) * mix(rzB * 0.75, hr * 0.85, tt) * furV * smoothstep(0.02, 0.15, nl);
    }
    else if (part < 5.5) {
      vec3 b = So + vec3(px * sl, -hr * 0.42 * 0.55 * (1.0 - px), side * (1.0 - px) * hr * 0.5 * 0.72);
      p = b + vec3(L.x * hr * 0.07, L.y * th, L.z * hr * 0.07);   // upper teeth point down (geometry tip at y = -1)
    } else {
      vec3 b = Jo + vec3(px * sl * 0.92, hr * 0.22 * 0.5 * (1.0 - px), side * (1.0 - px) * hr * 0.42 * 0.72);
      surface = b + vec3(L.x * hr * 0.06, L.y * th * 0.8, L.z * hr * 0.06);
      p = rz(surface, Jo, jawA);
    }
    if (part < 3.5 || part > 14.5 || (part > 4.5 && part < 5.5)) surface = p;
    p = rz(p, neck, -eat * 0.45 * (0.75 + 0.25 * sin(t * 9.0 + ph)));
  } else if (part < 7.5) {
    // legs from the hips: as many pairs as the genes say, spread along the trunk (a slot beyond them
    // collapses); neighbouring legs alternate (a trot on four, a tripod gait on six); tucked up in flight
    float has = step(px + 0.5, pairs);
    float fb = pairs < 1.5 ? 0.0 : 1.0 - 2.0 * px / (pairs - 1.0);     // +1 front .. -1 back
    vec3 hip = C + vec3((fb * mix(0.2, 0.85, limbPos) + (limbPos - 0.5) * 0.25) * rxB, -ryB * mix(0.75, 0.05, limbPos), side * rzB * 0.72);
    // two segments, hip -> knee -> foot (the near leg has a ring at the knee, L.y = -0.5): front elbows bend back,
    // hind knees forward. The thigh carries the muscle: thicker with the legs gene, and shared among fewer legs
    // the fewer pairs there are (each leg carries 1/n of the weight, so its section goes as 1/n).
    float u = -L.y;                                            // 0 hip .. 1 foot
    float kb = 1.0 - abs(2.0 * u - 1.0);                       // 0 hip, 1 knee, 0 foot
    float kx = (fb > 0.01 ? -1.0 : 1.0) * crouch * legLen * 0.5 * kb;
    float r = legR * mix(2.0, 1.0, smoothstep(0.0, 0.5, u)) * mix(1.0, 0.85, smoothstep(0.5, 1.0, u))
            * mix(0.85, 1.2, legs) * sqrt(2.0 / pairs);
    vec3 q = hip + vec3(L.x * r + kx, -u * legH, L.z * r + side * u * legLen * (1.0 - limbShape) * 0.2) * has;
    surface = q;
    // in flight the leg folds up under the body (knee drawn in, foot tucked)
    q = mix(q, hip + (q - hip) * vec3(0.4, 0.35, 1.0), air * 0.7);
    float off = mod(px + (side > 0.0 ? 0.0 : 1.0), 2.0) * 3.1416;
    float a = sin(t * 7.0 * (1.3 - 0.6 * legs) + ph + off) * 0.55 * gait * wk * (1.0 - rest);
    p = rz(q, hip, a + air * 0.6);
  } else if (part < 8.5) {
    // side fins: as many pairs as the genes say, spread from the shoulder back along the flank; paddle
    float has = step(px + 0.5, finPairs);
    float fs = (0.12 + 0.55 * fins) * finE * has * (1.0 - 0.12 * px);
    float fx = finPairs < 1.5 ? 0.3 : 0.35 - 0.85 * px / (finPairs - 1.0);
    vec3 root = C + vec3(rxB * fx, -ryB * 0.35, side * rzB * 0.8 * sqrt(max(0.2, 1.0 - fx * fx)));
    vec3 q = root + vec3((L.x * mix(1.8, 0.35, finShape) + L.z * (finShape - 0.5)) * fs, (finShape - 0.5) * L.z * fs, side * L.z * fs * mix(0.45, 1.8, finShape));
    surface = q;
    // out of water the fin lies back and down along the flank
    q = rx(ry(q, root, -side * 1.1 * finFold), root, side * 0.5 * finFold);
    p = rx(q, root, -side * sin(t * 5.0 + ph - px * 1.2) * 0.35 * (0.3 + gait) * (1.0 - 0.8 * finFold));
  } else if (part < 9.5) {
    float fd = smoothstep(0.1, 0.35, fins) * (0.25 + 0.85 * fins) * mix(1.0, 0.45, finFold);   // lowered out of water
    p = C + vec3((L.x * mix(1.8, 0.4, finShape) + L.y * (finShape - 0.5)) * rxB, ryB * 0.8 * furV + L.y * ryB * mix(0.5, 2.3, finShape) * fd, L.z);
    surface = p;
  } else if (part < 11.5) {
    // the tail (a stalk, plus a fan from the fins gene): sways, fast and wide on a swimmer
    // a continuous tail: its root as thick as the tapered rump, longer with appendages (a radiator)
    float tl = rxB * mix(0.03, 2.2, aMorphB.y) * (1.0 + 0.6 * append);
    float tt = ryB * mix(0.4, 0.6, aMorphB.y) * furV * smoothstep(0.0, 0.25, tl / rxB);
    float droop = (aMorphB.y - 0.5) * 0.35;
    vec3 root = C + vec3(-rxB * 0.8, ryB * 0.1, 0.0);
    if (part < 10.5) p = root + vec3(L.x * tl, L.y * tt + L.x * tl * droop, L.z * tt * 0.8);
    else {
      float tfs = (0.08 + 0.6 * fins) * finE;
        tfs *= mix(1.0, 0.6, finFold);
      p = root + vec3(-tl * 0.95, -tl * droop * 0.95, 0.0) + vec3((L.x * mix(1.6, 0.4, finShape) + abs(L.y) * (finShape - 0.5)) * tfs, L.y * tfs * mix(0.4, 1.8, finShape), L.z);
    }
    surface = p;
    p = ry(p, root, sin(t * (2.3 + 5.0 * fish) + ph) * (0.25 + 0.3 * fish) * (0.35 + gait));
  } else if (part < 12.5) {
    // wings from the shoulders: flap in flight, folded back along the body otherwise
    float wE = smoothstep(0.06, 0.3, wings);
    float span = (0.25 + 1.5 * wings) * wE * mix(0.5, 1.8, wingShape), chord = (0.3 + 0.6 * wings) * wE * mix(1.6, 0.45, wingShape);
    vec3 sh = C + vec3(rxB * (limbPos - 0.5), ryB * 0.6, side * rzB * 0.55);
    vec3 q = sh + vec3((L.x + L.z * (wingShape - 0.5) * 1.4) * chord, L.y, side * L.z * span);
    surface = q;
    float fold = 1.0 - air;          // folded back along the body whenever it is not flying
    q = ry(q, sh, -side * 1.25 * fold);
    float flap = air * (sin(t * 14.0 + ph) * 0.85 + 0.15);
    p = rx(q, sh, -side * (flap + 0.12 * fold));
  } else if (part < 14.5) {
    // Low armour plates and scutes, independent of diet and strike efficiency.
    float aE = smoothstep(0.15, 0.45, armour);
    float s = sqrt(max(0.0, 1.0 - px * px));
    float pw = rxB * 0.18 * aE, phh = ryB * (0.08 + 0.25 * armour) * aE;
    if (part < 13.5) p = C + vec3(px * rxB, ryB * s * 0.92, 0.0) + vec3(L.x * pw, L.y * phh, L.z * pw * 0.6);
    else {
      vec3 b = C + vec3(px * rxB, ryB * s * 0.5, side * rzB * s * 0.8);
      p = b + vec3(L.x * pw * 0.8, L.z * pw * 0.5, 0.0) + vec3(0.0, 0.55, side * 0.84) * L.y * phh * 0.5;
    }
    surface = p;
  } else if (part < 16.5) {
    float ring = sqrt(max(0.0, 1.0 - px * px));
    vec3 normal = normalize(vec3(px * 0.4, cos(side), sin(side)));
    // short tufts breaking the outline of the fur volume (the volume itself is the inflated body, furV)
    vec3 root = C + vec3(px * rxB, cos(side) * ryB * ring, sin(side) * rzB * ring) * (furV - 0.04 * fur);
    vec3 tangent = normalize(cross(normal, vec3(1.0, 0.0, 0.0)));
    float tuftLen = 0.4 * fur * (0.03 + 0.25 * fur);
    p = root + normal * L.y * tuftLen + (tangent * L.x + cross(normal, tangent) * L.z) * 0.04 * fur;
    surface = p;
  } else {
    // appendages are radiators (Allen's rule): ears on the head here; the tail and limbs lengthen with them above
    float hr = 0.2 * mix(0.35, 2.0, aMorphA.w);
    float nl = rxB * 1.6 * aMorphB.x;
    vec3 neck = C + vec3(rxB * 0.65, ryB * 0.15, 0.0);
    vec3 H = C + vec3(rxB * 0.9 + hr * 0.3 + nl * 0.75, ryB * 0.25 + nl * 0.3, 0.0);
    vec3 base = H + vec3(-hr * 0.25, hr * 0.6, side * hr * 0.45) * furV;
    float earLen = hr * append * (0.3 + 1.3 * append);
    float earW = hr * (0.25 + 0.35 * append) * smoothstep(0.0, 0.15, append);
    vec3 dir = normalize(vec3(-0.3, 0.8, side * 0.5));
    surface = base + vec3(L.x * earW, 0.0, L.z * earW * 0.3) + dir * L.y * earLen;
    p = rz(surface, neck, -eat * 0.45 * (0.75 + 0.25 * sin(t * 9.0 + ph)));
  }
  // Rest-frame coordinates: markings stay attached during articulation, never seeded by identity or time.
  vBody = (surface - C) / vec3(rxB, ryB, rzB);
  // a swimmer's body is a travelling wave; settled bodies pulse; strike lunges; everyone breathes
  p.z += sin(p.x * 3.5 - t * 7.0 - ph) * 0.1 * fish * (0.4 + gait) * clamp(0.5 - p.x * 0.8, 0.0, 1.6);
  p.x += strikeA * 0.1 * abs(sin(t * 7.0 + ph));
  p.y *= 1.0 + (0.015 + 0.05 * sess) * sin(t * 2.0 + ph);
  transformed = p;

  float hue = aMorphC.z, sat = 0.05 + 0.9 * aMorphC.w, light = 0.12 + 0.72 * aMorphD.x;
  float contrast = smoothstep(0.0, 0.2, aMorphD.y);
  vCol = mix(pigment(hue, sat, light), aClade.rgb, uSpeciesOverlay);
  vMark = mix(pigment(fract(hue + aMorphD.w), sat, mix(light, 1.0 - light * 0.8, contrast)), aClade.rgb, uSpeciesOverlay);
  vPattern = vec4(aMorphD.y, aMorphD.z, fur, armour);
}
`;

/** The one material all organisms share (near and far meshes). uTime in material.userData.uniforms. */
export function bodyMaterial() {
  const mat = new THREE.MeshLambertMaterial({ flatShading: true });
  const u = { uTime: { value: 0 }, uSpeciesOverlay: { value: 0 } };
  mat.userData.uniforms = u;
  mat.onBeforeCompile = (sh) => {
    Object.assign(sh.uniforms, u);
    sh.vertexShader = sh.vertexShader
      .replace('#include <common>', '#include <common>\n' + GLSL_COMMON)
      .replace('#include <begin_vertex>', '#include <begin_vertex>\n' + GLSL_BODY);
    sh.fragmentShader = sh.fragmentShader
      .replace('#include <common>', '#include <common>\nvarying vec3 vCol; varying vec3 vMark; varying vec3 vBody; varying vec4 vPattern;')
      .replace('#include <color_fragment>', `#include <color_fragment>
        vec3 q = vBody * mix(2.0, 12.0, vPattern.y);   // coarse enough to read, not to shimmer, at a distance
        float kind = vPattern.x * 3.0;
        float stripes = sin(q.x + sin(q.y * 0.8) * 1.2);
        float spots = cos(q.x) * cos(q.y) * cos(q.z);
        float marbling = sin(q.x + sin(q.y + sin(q.z)) * 2.0);
        float pattern = mix(stripes, spots, smoothstep(0.8, 1.2, kind));
        pattern = mix(pattern, marbling, smoothstep(1.8, 2.2, kind));
        float aa = max(fwidth(pattern), 0.02);
        float mark = smoothstep(0.1 - aa, 0.1 + aa, pattern) * smoothstep(0.0, 0.2, vPattern.x);
        vec3 skinColor = mix(vCol, vMark, mark);
        float surfaceNoise = sin(q.x * 5.0) * sin(q.y * 7.0) * sin(q.z * 5.0);
        skinColor *= 1.0 - (0.08 * vPattern.z + 0.1 * vPattern.w) * (0.5 + 0.5 * surfaceNoise);
        // countershading: a slightly darker back and lighter belly, the same pigment (no new colour)
        skinColor *= mix(1.1, 0.86, smoothstep(-0.7, 0.7, vBody.y));
        diffuseColor.rgb *= skinColor;`)
      .replace('#include <emissivemap_fragment>', '#include <emissivemap_fragment>\ntotalEmissiveRadiance += skinColor * 0.12;');
  };
  mat.customProgramCacheKey = () => 'neuroreef-body-appearance-7';
  return mat;
}
