// The body material: a Lambert material whose vertex shader builds each organism's body from its genes and
// animates it. The geometry (body.js) holds only surface coordinates; per instance the shader reads
//   aGeneA = (legs, fins, wings, sight)        organ sizes 0..1
//   aGeneB = (armour, strike, fur, appendages) organ sizes 0..1
//   aMorphA/B/C/D = the sixteen normalized, inherited appearance genes in wire order
//   aAnim  = (gait 0..1, phase 0..1, action bits, resting)
//   aClade = (clade colour as linear RGB, packed counts): w = meat * 512 + loco * 64 + (fin pairs - 1) * 16
//            + (eye pairs - 1) * 4 + (leg pairs - 1); meat 0..15, loco 0 walk, 1 wade, 2 swim, 3 fly, 4 settled
//
// How the genes are drawn (display only; every visible part is some gene's expression):
//  - the trunk is ONE continuous tube along a curved spine, tail tip -> rump -> shoulders -> neck -> head -> nose,
//    so tail, body, neck and head blend into one another. Body length / width / height set the trunk, the neck
//    gene the neck's length and how upright it rises, the tail gene the tail's length and carriage, the head gene
//    the skull. The spine's pitch follows the body plan: a biped stands sloped over its hips (more upright the
//    shorter its tail), a tall long-necked quadruped slopes up to the shoulders, a swimmer lies level.
//  - diet and strike shape the head: a meat-eater has a broad skull, forward eyes, a longer muzzle with teeth as
//    long as its strike; a plant-eater a deep blunt muzzle and eyes on the sides.
//  - legs are tapered tubes over three bones (thigh, shin, foot) placed by two-bone IK, so they bend at the knee
//    and plant their feet. Limb position runs from upright legs under the body to a sprawl out to the sides; limb
//    shape from stocky plantigrade to slender digitigrade legs. Leg length and thickness follow mass (McMahon).
//  - wings are membranes on a jointed bony leading edge (arm, forearm, hand) that folds against the flank at rest;
//    fins are membranes with rays: paired side fins, a dorsal sail or fin, a tail fan.
//  - armour is a domed carapace with scutes and a row of plates along the spine (no spikes); fur is volume:
//    a fuller, shaggier outline, a bushy tail, soft sheen; appendages are radiators: ears, a longer tail and limbs.
//  - pigment is countershaded (darker back, pale belly) and the pattern genes lay stripes across the spine,
//    spots or reticulated patches over the back and flanks, in coordinates that follow the body.
// Normals: near bodies take them from the surface itself (finite differences of the same function), far ones
// from its cross-section; no skeletons and no per-clade meshes: every organism is one instance of the same mesh.

import * as THREE from 'three';

const GLSL_COMMON = `
uniform float uTime;
uniform float uSpeciesOverlay;
attribute vec3 aAux;
attribute vec4 aAnim; attribute vec4 aGeneA; attribute vec4 aGeneB; attribute vec4 aClade;
attribute vec4 aMorphA; attribute vec4 aMorphB; attribute vec4 aMorphC; attribute vec4 aMorphD;
varying vec3 vCol; varying vec3 vMark; varying vec4 vSkin; varying vec4 vPattern;

vec3 pigment(float h, float s, float l) {
  vec3 rgb = clamp(abs(mod(h * 6.0 + vec3(0.0, 4.0, 2.0), 6.0) - 3.0) - 1.0, 0.0, 1.0);
  rgb = l + s * (rgb - 0.5) * (1.0 - abs(2.0 * l - 1.0));
  return mix(rgb / 12.92, pow((rgb + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), rgb));
}
float bit(float v, float b) { return mod(floor(v / b), 2.0); }
vec3 rotA(vec3 p, vec3 c, vec3 ax, float a) { p -= c; float s = sin(a), k = cos(a); return c + p * k + cross(ax, p) * s + ax * dot(ax, p) * (1.0 - k); }

// ---- per-instance state, set once at the top of main and read by the part surfaces below
float bT, bPh, bGait, bRest, bAir, bFish, bWalk, bSwim, bSess, bEat, bStrikeA, bBiped;
float gLegs, gFins, gWings, gSight, gArmour, gStrike, gFur, gAppend, gMeat;
float nLegPairs, nFinPairs, nEyePairs;
float rX, rY, rZ, hR, furV, cY;
vec3 kT, kH, kS, kO, kN, dT, dH, dS, dO, dN;
float lT, lB, lK, lF;
float legLen, legR, legE, sprawl, footAng, footLen, legExt, hipDrop, stride, liftH, gaitW;
float jawOpen, breath, swimW, tailSway, finE, finFold, wingE, wingF, shellE, plateE;
// ---- the vertex's part
float bPart, bSide, bIdx;
// ---- skin coordinates of the first evaluation (along, around from the dorsal line, up, class)
vec4 oSkin;

vec3 herm(vec3 p0, vec3 m0, vec3 p1, vec3 m1, float u) {
  float u2 = u * u, u3 = u2 * u;
  return (2.0 * u3 - 3.0 * u2 + 1.0) * p0 + (u3 - 2.0 * u2 + u) * m0 + (3.0 * u2 - 2.0 * u3) * p1 + (u3 - u2) * m1;
}
vec3 hermD(vec3 p0, vec3 m0, vec3 p1, vec3 m1, float u) {
  float u2 = u * u;
  return (6.0 * u2 - 6.0 * u) * p0 + (3.0 * u2 - 4.0 * u + 1.0) * m0 + (6.0 * u - 6.0 * u2) * p1 + (3.0 * u2 - 2.0 * u) * m1;
}

/** The spine at g (0 tail tip, 1 rump, 2 shoulders, 3 occiput, 4 nose): point and forward tangent. */
void spine(float g, out vec3 P, out vec3 T) {
  float seg = clamp(floor(g), 0.0, 3.0), u = g - seg;
  vec3 p0 = kO, p1 = kN, m0 = dO * lF, m1 = dN * lF;
  if (seg < 0.5) { p0 = kT; p1 = kH; m0 = dT * lT; m1 = dH * lT; }
  else if (seg < 1.5) { p0 = kH; p1 = kS; m0 = dH * lB; m1 = dS * lB; }
  else if (seg < 2.5) { p0 = kS; p1 = kO; m0 = dS * lK; m1 = dO * lK; }
  P = herm(p0, m0, p1, m1, u);
  T = normalize(hermD(p0, m0, p1, m1, u) + vec3(1e-5, 0.0, 0.0));
  // a swimmer's body is a travelling wave (head steady); a walker's tail sways
  float wave = bFish * swimW * sin(g * 2.4 - bT * (5.0 + 5.0 * bGait) - bPh) * pow(clamp((3.3 - g) / 3.3, 0.0, 1.0), 1.6);
  float sway = tailSway * sin(bT * 2.1 + bPh + g * 2.2) * pow(clamp(1.0 - g, 0.0, 1.0), 1.5);
  P.z += wave + sway;
}

/** Distance along the spine from the tail tip (for markings that follow the body). */
float alongOf(float g) {
  float seg = clamp(floor(g), 0.0, 3.0), u = g - seg;
  return seg < 0.5 ? u * lT : seg < 1.5 ? lT + u * lB : seg < 2.5 ? lT + lB + u * lK : lT + lB + lK + u * lF;
}

/** The trunk's cross-section at g: (half width, half height, centre offset along the spine normal). */
vec3 section(float g) {
  float seg = clamp(floor(g), 0.0, 3.0), u = g - seg;
  vec2 hip = vec2(rZ * 0.8, rY * 0.84) * furV, sh = vec2(rZ * 0.8, rY * 0.9) * furV;
  vec2 occ = vec2(max(hR * 0.66, rZ * 0.32), max(hR * 0.74, rY * 0.36)) * mix(1.0, furV, 0.6);
  vec2 r; float dy = 0.0;
  if (seg < 0.5) {
    // tail, tip (u = 0) to rump: tapering, bushy with fur, flattened sideways into a swimmer's tail stalk
    r = hip * pow(u, mix(0.5, 0.8, smoothstep(0.3, 1.2, lT / rX)));
    r *= 1.0 + 1.4 * gFur * smoothstep(0.0, 0.45, u) * (1.0 - smoothstep(0.45, 1.0, u));
    r.x *= mix(1.0, 0.55, bFish * (1.0 - u));
  } else if (seg < 1.5) {
    // rump to shoulders: a full belly between narrower hips and chest, hanging below the back on legs
    float bump = sin(3.14159 * u);
    r = mix(hip, sh, u) + vec2(rZ, rY) * furV * 0.2 * pow(bump, 0.8);
    r *= 1.0 + breath * bump;
    dy = -rY * 0.12 * bump * legE * bWalk;
  } else if (seg < 2.5) {
    // the neck narrows from the shoulders to the skull; a short one is just the body flowing into the head
    r = mix(sh, occ, smoothstep(0.0, 0.8, u));
    r *= 1.0 + 0.18 * gFur * exp(-u * u * 6.0);      // a ruff where fur is thick
  } else {
    // the head: occiput, a cranium (wider over a meat-eater's jaw muscles), the muzzle, a rounded nose
    float cr = smoothstep(0.0, 0.26, u), mz = smoothstep(0.32, 0.74, u);
    vec2 cran = vec2(hR * (0.9 + 0.28 * gMeat), hR * 0.9);
    vec2 muz = vec2(hR * mix(0.52, 0.36, gMeat), hR * mix(0.62, 0.4, gMeat));
    r = mix(mix(occ, cran, cr), muz, mz);
    r *= sqrt(clamp((1.0 - u) / 0.15, 0.0, 1.0));
    dy = -hR * 0.16 * mz;
  }
  return vec3(r, dy);
}

/** The skin at (g, angle from the dorsal line, +z to the left); hint = the outward cross-section normal. */
vec3 skin(float g, float th, out vec3 hint, out vec3 T, out vec3 N, out vec3 B) {
  vec3 P; spine(g, P, T);
  N = normalize(cross(vec3(0.0, 0.0, 1.0), T)); B = cross(T, N);
  vec3 S = section(g);
  float seg = clamp(floor(g), 0.0, 3.0), u = g - seg;
  float c = cos(th), s = sin(th), rw = S.x, rh = S.y;
  // a flatter belly on a standing body; the head's underside makes room for the jaw
  if (c < 0.0) {
    if (seg > 0.5 && seg < 1.5) rh *= mix(1.0, 0.86, legE * bWalk);
    if (seg > 2.5) rh *= mix(1.0, 0.5, smoothstep(0.3, 0.62, u));
  }
  // armour: a domed carapace over the trunk, with a rim at the flank
  float shell = shellE * (seg > 0.5 && seg < 1.5 ? pow(sin(3.14159 * u), 0.5) : 0.0);
  rh *= 1.0 + 0.34 * shell * smoothstep(-0.2, 0.8, c);
  rw *= 1.0 + 0.18 * shell * smoothstep(-0.75, -0.05, c);
  // fur: a shaggy outline
  float shag = 1.0 + 0.07 * gFur * sin(g * 23.0 + th * 3.0) * sin(g * 14.0 - th * 5.0) * step(seg, 2.5);
  rw *= shag; rh *= shag;
  hint = normalize(N * c / max(rh, 1e-4) + B * s / max(rw, 1e-4));
  if (g < 0.004) hint = -T; else if (g > 3.996) hint = T;
  return P + N * (S.z + c * rh) + B * (s * rw);
}

// ------------------------------------------------------------------ parts (L = surface coordinates)

vec3 trunk(vec3 L, out vec3 hint) {
  vec3 T, N, B;
  float th = L.y * 6.28318;
  vec3 p = skin(L.x, th, hint, T, N, B);
  float r = section(L.x).x;
  oSkin = vec4(alongOf(L.x), acos(clamp(cos(th), -1.0, 1.0)) * (r + section(L.x).y) * 0.5, cos(th), 0.0);
  return p;
}

/** Leg j (bIdx), side bSide: hip -> knee -> ankle -> toe, L.x in bones (0 hip, 1 knee, 2 ankle, 3 toe). */
vec3 leg(vec3 L, out vec3 hint) {
  float j = bIdx, has = step(j + 0.5, nLegPairs);
  float fb = nLegPairs < 1.5 ? 0.0 : 1.0 - 2.0 * j / (nLegPairs - 1.0);    // +1 front .. -1 back
  float tt = bBiped > 0.5 ? 0.24 : 0.5 + fb * mix(0.34, 0.42, aMorphB.z);
  vec3 h0, T, N, B; vec3 hip = skin(1.0 + tt, 3.14159 - bSide * mix(0.75, 1.45, sprawl), h0, T, N, B);
  hip -= h0 * legR * 2.0;                                   // the joint sits inside the flank, under a haunch
  float ab = max(legLen - footLen, 1e-4);
  // the stride: neighbouring legs alternate (a trot on four, a tripod on six)
  float off = mod(j + (bSide > 0.0 ? 0.0 : 1.0), 2.0) * 3.14159;
  float phl = bT * gaitW + bPh + off;
  float act = bGait * bWalk * (1.0 - bRest);
  float sw = sin(phl) * act, lift = max(0.0, cos(phl)) * act;
  // the foot: under the hip (upright) or out to the side (sprawling); toes forward
  vec3 fwd = normalize(vec3(1.0, 0.0, bSide * 0.7 * sprawl));
  vec3 toe = vec3(hip.x + footLen * 0.45 + fb * legLen * 0.06 + stride * sw, liftH * lift,
                  hip.z + bSide * (ab * 0.4 * sprawl + legLen * 0.04));
  vec3 ankle = toe - fwd * footLen * cos(footAng) + vec3(0.0, footLen * sin(footAng), 0.0);
  // tucked back in flight; folded under a body lying at rest
  vec3 tuck = hip + vec3(-ab * 0.62, -ab * 0.32, bSide * rZ * 0.1);
  vec3 lie = vec3(hip.x + fb * ab * 0.35, footLen * 0.5, hip.z + bSide * (rZ * 0.25 + ab * 0.2 * sprawl));
  float tk = max(bAir, bFish);
  ankle = mix(mix(ankle, lie, bRest * (1.0 - bAir)), tuck, tk);
  toe = mix(mix(toe, lie + vec3(footLen * 0.9, -footLen * 0.4, 0.0), bRest * (1.0 - bAir)), tuck + vec3(-footLen, -footLen * 0.3, 0.0), tk);
  // two-bone IK: hip -> knee -> ankle; front knees (elbows) bend back, hind and biped knees forward, a
  // sprawler's out and up
  vec3 v = ankle - hip; float d = max(length(v), 1e-5); vec3 dir = v / d;
  float a = max(ab * 0.5 * mix(1.0, legExt / 0.97, step(0.01, fb) * (1.0 - sprawl) * 0.7), d * 0.505);   // front legs stand straighter
  float h = sqrt(max(a * a - 0.25 * d * d, 0.0));
  vec3 hintB = normalize(mix(vec3(fb > 0.01 ? -1.0 : 1.0, 0.0, 0.0), vec3(0.0, 0.7, bSide), sprawl));
  vec3 bend = normalize(hintB - dir * dot(hintB, dir) + vec3(0.0, 1e-4, 0.0));
  vec3 knee = hip + dir * d * 0.5 + bend * h;
  // the ring at L.x on the bone chain
  vec3 s0 = normalize(knee - hip), s1 = normalize(ankle - knee + 1e-6), s2 = normalize(toe - ankle + 1e-6);
  float c = L.x; vec3 C, Tl;
  if (c < 1.0) { C = mix(hip, knee, c); Tl = c > 0.99 ? normalize(s0 + s1) : s0; }
  else if (c < 2.0) { C = mix(knee, ankle, c - 1.0); Tl = c > 1.99 ? normalize(s1 + s2) : s1; }
  else { C = mix(ankle, toe, c - 2.0); Tl = s2; }
  // radius: muscle at the thigh, a narrow knee, the calf, a thin ankle, a flat broad foot
  float haunch = max(2.0, min(rY * 0.55 * furV / max(legR, 1e-4), 3.5));
  float thigh = mix(haunch, 1.6, 0.5);
  float r = c < 0.45 ? mix(haunch, thigh, c / 0.45) : c < 1.0 ? mix(thigh, 0.8, (c - 0.45) / 0.55)
          : c < 2.0 ? mix(0.8, 0.5, c - 1.0) : mix(0.5, 0.68, c - 2.0);
  r *= legR * has * mix(1.0, 1.25, gFur * step(c, 1.2));
  vec3 Pn = normalize(cross(dir, bend));
  vec3 Nl = normalize(cross(Pn, Tl)), Bl = cross(Tl, Nl);
  float th = L.y * 6.28318, cs = cos(th), sn = sin(th);
  float flatF = smoothstep(2.0, 2.6, c);
  vec3 rad = Nl * cs * mix(1.0, 0.55, flatF) + Bl * sn * mix(1.0, 1.35, flatF);
  hint = normalize(Nl * cs + Bl * sn);
  oSkin = vec4(c / 3.0, acos(clamp(cs, -1.0, 1.0)) * r, 0.2 - 0.4 * c, 1.0);
  return C + rad * r;
}

/** Eye pair bIdx: a ball set in a socket of the head, L.x from the pupil's pole. */
vec3 eye(vec3 L, out vec3 hint) {
  float j = bIdx, has = step(j + 0.5, nEyePairs);
  float er = hR * (0.12 + 0.2 * gSight) / (1.0 + 0.5 * (nEyePairs - 1.0)) * (1.0 - 0.18 * j) * has;
  float ue = mix(0.36, 0.44, gMeat) - 0.075 * j, the = mix(1.2, 0.85, gMeat) + 0.12 * j;
  vec3 hn, T, N, B; vec3 sp = skin(3.0 + ue, bSide * the, hn, T, N, B);
  vec3 o = normalize(hn + T * mix(0.12, 0.6, gMeat) + N * 0.1);   // a hunter's eyes look forward
  vec3 c = sp - o * er * 0.5;
  vec3 U = normalize(cross(o, N + vec3(0.0, 0.0, 1e-3))), V = cross(o, U);
  float a = 3.14159 * L.x, b = 6.28318 * L.y;
  vec3 d = o * cos(a) + (U * cos(b) + V * sin(b)) * sin(a);
  hint = d;
  oSkin = vec4(L.x, 0.0, 0.0, 3.0);
  return c + d * er;
}

/** The lower jaw: under the muzzle from the hinge to the chin; opens to bite and chew. */
vec3 jaw(vec3 L, out vec3 hint) {
  float u = mix(0.24, 0.97, L.x);
  vec3 h0, T, N, B; vec3 top = skin(3.0 + u, 0.0, h0, T, N, B);
  vec3 S = section(3.0 + u), P; spine(3.0 + u, P, T);
  vec3 cen = P + N * (S.z - S.y * 0.55);
  vec3 Ph, Th; spine(3.24, Ph, Th); vec3 Sh = section(3.24);
  vec3 Nh = normalize(cross(vec3(0.0, 0.0, 1.0), Th));
  vec3 hinge = Ph + Nh * (Sh.z - Sh.y * 0.4);
  float rw = S.x * 0.86, rh = S.y * 0.36 * sqrt(clamp((1.0 - L.x) / 0.12, 0.0, 1.0)) + 1e-4;
  rw *= sqrt(clamp((1.0 - L.x) / 0.12, 0.0, 1.0));
  float th = L.y * 6.28318;
  vec3 p = cen + N * cos(th) * rh + B * sin(th) * rw;
  hint = normalize(N * cos(th) / rh + B * sin(th) / max(rw, 1e-4));
  oSkin = vec4(alongOf(3.0 + u), acos(clamp(cos(th), -1.0, 1.0)) * rw, -0.4 - 0.5 * max(0.0, -cos(th)), 0.0);
  p = rotA(p, hinge, B, -jawOpen); hint = rotA(hint, vec3(0.0), B, -jawOpen);
  return p;
}

/** Teeth along the upper jaw (k = 0 the canine), as long as the strike gene, pointing down. */
vec3 tooth(vec3 L, out vec3 hint) {
  float k = bIdx;
  float tl = smoothstep(0.2, 0.55, gStrike) * hR * (0.34 - 0.08 * k) * mix(0.7, 1.15, gMeat);
  float u = 0.88 - 0.12 * k;
  vec3 h0, T, N, B; vec3 base = skin(3.0 + u, bSide * 2.05, h0, T, N, B);
  vec3 dir = normalize(-N + T * 0.15);
  float th = L.y * 6.28318, r = tl * 0.24 * (1.0 - L.x);
  hint = normalize(T * cos(th) + B * sin(th));
  oSkin = vec4(L.x, 0.0, 0.0, 4.0);
  return base - h0 * tl * 0.15 + dir * tl * L.x + (T * cos(th) + B * sin(th)) * r;
}

/** A wing (side bSide) at span s, chord t: a jointed bony leading edge (arm, forearm, hand) with a membrane
 *  behind it, scalloped between the fingers; spread and beating in flight, folded along the flank at rest. */
vec3 wingAt(float s, float t, float bone, float th, out vec3 hint) {
  float fold = 1.0 - bAir;
  float span = (0.25 + 1.55 * gWings) * wingE * mix(0.55, 1.75, aMorphC.y);
  span = mix(span, min(span * 0.8, rX * 3.2), fold);       // folded, each bone lies within the trunk's length
  float chord = (0.3 + 0.55 * gWings) * wingE * mix(1.45, 0.5, aMorphC.y);
  float tt = 0.64 + 0.18 * (aMorphB.z - 0.5);
  vec3 h0, T, N, B; vec3 sh = skin(1.0 + tt, bSide * 0.75, h0, T, N, B);
  sh -= h0 * rZ * 0.08;
  float a0 = mix(0.05 + 0.25 * aMorphC.y, 1.42, fold), a1 = a0 + mix(0.08, -2.85, fold), a2 = a1 + mix(0.32, 2.78, fold);
  float l0 = 0.3, l1 = 0.32;
  vec2 e0 = vec2(-sin(a0), cos(a0)), e1 = vec2(-sin(a1), cos(a1)), e2 = vec2(-sin(a2), cos(a2));
  vec2 J1 = e0 * l0 * span, J2 = J1 + e1 * l1 * span, q, e;
  if (s < l0) { q = e0 * s * span; e = e0; }
  else if (s < l0 + l1) { q = J1 + e1 * (s - l0) * span; e = e1; }
  else { q = J2 + e2 * (s - l0 - l1) * span; e = e2; }
  vec2 cd = normalize(mix(vec2(-e.y, e.x), vec2(-0.3, -1.0), fold));   // backward across the bone; tucked in, folded
  float c = chord * (1.0 - 0.85 * pow(s, 1.3)) * (1.0 - 0.28 * abs(sin(s * 3.14159 * 2.0))) * mix(1.0, 0.3, fold);
  vec3 w = vec3(q.x + cd.x * c * t, c * t * (1.0 - t) * 0.45 * (1.0 - fold), q.y + cd.y * c * t);
  vec3 wn = vec3(0.0, 1.0, 0.0);
  if (bone > 0.5) {
    float r = (0.035 - 0.022 * s) * wingE * (1.0 + 0.5 * exp(-pow((s - l0) * 14.0, 2.0)) + 0.4 * exp(-pow((s - l0 - l1) * 14.0, 2.0)));
    wn = vec3(0.0, cos(th), 0.0) + vec3(cd.x, 0.0, cd.y) * sin(th);
    w += wn * r;
  }
  // beating: the outer wing lags the arm; folded, the whole wing hangs down the flank
  float beat = sin(bT * wingF + bPh);
  float phi = bAir * (0.75 * beat + 0.12 + 0.35 * sin(bT * wingF + bPh - 1.0) * smoothstep(0.35, 1.0, s)) - fold * 1.15;
  float cp = cos(phi), sp = sin(phi);
  w = vec3(w.x, w.y * cp + w.z * sp, w.z * cp - w.y * sp);
  wn = vec3(wn.x, wn.y * cp + wn.z * sp, wn.z * cp - wn.y * sp);
  hint = normalize(T * wn.x + N * wn.y + B * bSide * wn.z);
  oSkin = vec4(s, t, 0.3, bone > 0.5 ? 1.0 : 6.0);
  return sh + T * w.x + N * w.y + B * bSide * w.z;
}

/** Side fin pair bIdx: a membrane with rays from the lower flank; spread and paddling in water, laid back
 *  against the body out of it. L.x root -> tip, L.y across. */
vec3 sideFin(vec3 L, out vec3 hint) {
  float j = bIdx, has = step(j + 0.5, nFinPairs);
  float fs = (0.1 + 0.42 * gFins) * finE * has * (1.0 - 0.12 * j) * mix(1.0, 0.75, finFold);
  float tt = nFinPairs < 1.5 ? 0.78 : mix(0.84, 0.22, j / (nFinPairs - 1.0));
  vec3 h0, T, N, B; vec3 root = skin(1.0 + tt, bSide * 1.95, h0, T, N, B);
  root -= h0 * 0.01;
  float fsh = aMorphC.x;
  float len = fs * mix(0.75, 1.45, fsh), wid = fs * mix(0.9, 0.4, fsh);
  vec3 outw = B * bSide;
  vec3 dSpread = normalize(outw + (-T) * mix(0.25, 0.85, fsh) - N * 0.35);
  vec3 dFold = normalize(-T + outw * 0.22 - N * 0.25);
  vec3 dir = normalize(mix(dSpread, dFold, finFold));
  float pad = sin(bT * 5.0 + bPh - j * 1.2) * 0.4 * (0.3 + bGait) * (1.0 - 0.85 * finFold);
  dir = normalize(rotA(dir, vec3(0.0), T, -bSide * pad));
  vec3 cref = normalize(mix(T, N, finFold));                // spread: chord along the body; folded: flat on the flank
  vec3 cdir = normalize(cref - dir * dot(cref, dir));
  float s = L.x, t = L.y - 0.5;
  float w = wid * (1.0 - 0.7 * s * s) * t * 2.0 + wid * 0.25 * s;   // leading edge toward the front
  vec3 fn = cross(dir, cdir);
  hint = fn;
  oSkin = vec4(s, L.y, 0.0, 2.0);
  return root + dir * len * s * (1.0 - 0.15 * abs(t)) + cdir * w + fn * 0.06 * len * s * (1.0 - 4.0 * t * t);
}

/** The dorsal fin along the back: a long low sail (fin shape 0) to a tall swept fin (1). L.x along, L.y up. */
vec3 dorsal(vec3 L, out vec3 hint) {
  float fsh = aMorphC.x;
  float fd = smoothstep(0.1, 0.35, gFins) * (0.25 + 0.8 * gFins) * mix(1.0, 0.55, finFold);
  float g = 1.0 + mix(mix(0.1, 0.4, fsh), mix(0.95, 0.72, fsh), L.x);
  vec3 h0, T, N, B; vec3 base = skin(g, 0.0, h0, T, N, B);
  float prof = mix(pow(sin(3.14159 * L.x), 0.7), L.x < 0.6 ? L.x / 0.6 : pow((1.0 - L.x) / 0.4, 0.6), fsh);
  float h = fd * rY * mix(1.3, 2.6, fsh) * prof * L.y;
  hint = B;
  oSkin = vec4(L.x * 3.0, L.y, 0.5, 2.0);
  return base - h0 * 0.015 + N * h - T * h * mix(0.1, 0.7, fsh) * L.y;
}

/** The tail fan: a forked (fin shape 1) or rounded (0) caudal fin at the tail tip. L.x root -> tip, L.y lobe. */
vec3 tailFin(vec3 L, out vec3 hint) {
  float fsh = aMorphC.x;
  float tfs = (0.1 + 0.6 * gFins) * finE * mix(1.0, 0.55, finFold) * mix(0.8, 1.3, rY / 0.3);
  vec3 P, T; spine(0.08, P, T);
  vec3 N = normalize(cross(vec3(0.0, 0.0, 1.0), T));
  float t = L.y * 2.0 - 1.0;
  float psi = t * mix(0.95, 0.7, fsh);
  float ell = tfs * mix(1.0 - 0.3 * t * t, 0.5 + 0.65 * pow(abs(t), 0.8), fsh);
  vec3 d = -T * cos(psi) + N * sin(psi);
  hint = normalize(cross(T, N));
  oSkin = vec4(L.x, L.y * 2.0, 0.0, 2.0);
  return P + d * ell * L.x;
}

/** An ear (appendages: a radiator), cupped, up and back from the skull; a swimmer's are gill frills. */
vec3 ear(vec3 L, out vec3 hint) {
  float eL = hR * gAppend * (0.45 + 1.6 * gAppend), eW = hR * (0.22 + 0.4 * gAppend) * smoothstep(0.0, 0.12, gAppend);
  vec3 h0, T, N, B; vec3 base = skin(3.0 + 0.14 - 0.12 * bFish, bSide * mix(0.85, 1.5, bFish), h0, T, N, B);
  vec3 dir = normalize(mix(N * 0.8 - T * 0.45 + B * bSide * 0.42, -T * 0.7 + B * bSide * 0.6 + N * 0.15, bFish));
  float tw = 0.12 * pow(max(0.0, sin(bT * 0.7 + bPh * 3.0)), 12.0);
  dir = normalize(rotA(dir, vec3(0.0), B, tw));
  vec3 acr = normalize(T - dir * dot(T, dir));
  vec3 cup = cross(dir, acr) * bSide;
  float s = L.x, t = L.y * 2.0 - 1.0;
  float w = eW * pow(sin(3.14159 * (0.15 + 0.85 * s)), 0.8) * (1.0 + 0.3 * s * (1.0 - s));
  hint = cup;
  oSkin = vec4(s, L.y, 0.5, 2.0);
  return base - h0 * eW * 0.2 + dir * eL * s + acr * w * t + cup * eW * 0.45 * (1.0 - t * t) * sin(3.14159 * s);
}

/** Armour plates in a row along the spine, tallest over the hips, alternating a little left and right. */
vec3 plate(vec3 L, out vec3 hint) {
  float k = bIdx, g = mix(0.6, 1.9, k / 3.0);
  vec3 h0, T, N, B; vec3 base = skin(g, 0.0, h0, T, N, B);
  float h = rY * (0.3 + 0.45 * sin(3.14159 * (k + 0.5) / 4.0)) * plateE;
  float t = L.y * 2.0 - 1.0, s = L.x;
  float w = h * 0.62 * sin(3.14159 * (0.28 + 0.72 * s));
  hint = B;
  oSkin = vec4(s, L.y, 1.0, 5.0);
  vec3 tilt = B * (mod(k, 2.0) - 0.5) * 0.3;
  return base - N * h * 0.25 + normalize(N + tilt - T * 0.25) * h * s * (1.0 - 0.3 * t * t) + T * w * t;
}

vec3 bodyPart(vec3 L, out vec3 hint) {
  if (bPart < 0.5) return trunk(L, hint);
  if (bPart < 1.5) return leg(L, hint);
  if (bPart < 2.5) return eye(L, hint);
  if (bPart < 3.5) return jaw(L, hint);
  if (bPart < 4.5) return tooth(L, hint);
  if (bPart < 5.5) return wingAt(L.x, 0.0, 1.0, L.y * 6.28318, hint);
  if (bPart < 6.5) return wingAt(L.x, L.y, 0.0, 0.0, hint);
  if (bPart < 7.5) return sideFin(L, hint);
  if (bPart < 8.5) return dorsal(L, hint);
  if (bPart < 9.5) return tailFin(L, hint);
  if (bPart < 10.5) return ear(L, hint);
  return plate(L, hint);
}
vec3 gBodyP; vec3 gBodyN;
`;

const GLSL_BODY = `
{
  gLegs = aGeneA.x; gFins = aGeneA.y; gWings = aGeneA.z; gSight = aGeneA.w;
  gArmour = aGeneB.x; gStrike = aGeneB.y; gFur = aGeneB.z; gAppend = aGeneB.w;
  bT = uTime; bGait = aAnim.x; bPh = aAnim.y * 6.28318; bRest = aAnim.w;
  float acts = aAnim.z;
  bEat = max(bit(acts, 1.0), bit(acts, 4.0)); bStrikeA = bit(acts, 2.0);
  float pk = floor(aClade.w + 0.5);
  gMeat = floor(pk / 512.0) / 15.0; pk = mod(pk, 512.0);
  float loco = floor(pk / 64.0);
  nFinPairs = mod(floor(pk / 16.0), 4.0) + 1.0; nEyePairs = mod(floor(pk / 4.0), 4.0) + 1.0; nLegPairs = mod(pk, 4.0) + 1.0;
  float fl = 1.0 - step(0.5, abs(loco - 3.0));
  bWalk = step(loco, 1.5) * (1.0 - fl);
  bSwim = (1.0 - step(0.5, abs(loco - 1.0)) + 1.0 - step(0.5, abs(loco - 2.0))) * (1.0 - fl);
  bFish = bSwim * (1.0 - bWalk);
  bSess = (1.0 - fl) * (1.0 - bWalk) * (1.0 - bSwim);
  bAir = fl * (1.0 - bRest);
  // the instance's scale: a resting body is drawn squashed (0.7) and a corpse flattened (under 0.5); the body
  // lies down here instead, and the squash is undone at the end
  float sx = length(instanceMatrix[0].xyz), sy = length(instanceMatrix[1].xyz);
  float squash = sy / max(sx, 1e-6), corpse = step(squash, 0.5);
  float unsq = (bRest > 0.5 && fl < 0.5) || corpse > 0.5 ? 1.0 / squash : 1.0;
  float massI = pow(max(sx / (corpse > 0.5 ? 1.725 : 1.5), 0.05), 3.0);
  float big = clamp(log(massI) / 5.99 + 0.38, 0.0, 1.0);    // 0 at M = 0.1 .. 1 at M = 40

  // ---- proportions: each axis 0.5..2 before volume normalization; mass remains the world-space scale
  vec3 axes = exp(0.69314718 * (2.0 * aMorphA.xyz - 1.0));
  axes /= pow(axes.x * axes.y * axes.z, 1.0 / 3.0);
  rX = 0.5 * axes.x; rY = 0.28 * axes.z; rZ = 0.27 * axes.y;
  hR = 0.16 * mix(0.55, 1.9, aMorphA.w);
  furV = 1.0 + 0.2 * gFur;
  float neckG = aMorphB.x, tailG = aMorphB.y, limbShape = aMorphB.w;
  shellE = smoothstep(0.35, 0.85, gArmour); plateE = smoothstep(0.5, 0.85, gArmour);
  finE = smoothstep(0.06, 0.3, gFins); wingE = smoothstep(0.06, 0.3, gWings);
  finFold = (1.0 - bSwim * (1.0 - 0.7 * bWalk)) * (1.0 - bSess);   // fins lie back out of water (a wader's mostly)

  // ---- legs: length and thickness from the legs gene; bone scales with mass as it must (McMahon 1975: length
  // ~ M^1/4, diameter ~ M^3/8; relative to the M^1/3 model scale, M^-1/12 and M^+1/24), thicker on armour;
  // longer with appendages (Allen's rule). Small bodies crouch, big ones stand on near-columnar legs (Biewener)
  legE = smoothstep(0.05, 0.3, gLegs);
  bBiped = step(nLegPairs, 1.5);
  legLen = (0.18 + 0.66 * gLegs) * legE * mix(0.7, 1.3, limbShape) * pow(massI, -1.0 / 12.0) * (1.0 + 0.2 * gAppend);
  legR = (0.034 + 0.04 * gLegs + 0.028 * gArmour) * legE * mix(1.45, 0.7, limbShape) * pow(massI, 1.0 / 8.0) * sqrt(2.0 / nLegPairs);
  sprawl = smoothstep(0.2, 0.9, aMorphB.z) * mix(1.0, 0.25, bBiped);
  float crouch = mix(0.55, 0.12, big) * mix(1.3, 0.6, limbShape);
  legExt = clamp(1.0 - crouch * 0.6, 0.5, 0.97);
  footAng = mix(0.3, 1.2, limbShape);
  footLen = legLen * mix(0.17, 0.28, limbShape);
  float ab = legLen - footLen;
  float lieY = rY * 0.9 * furV;
  gaitW = 6.28318 * (0.9 / sqrt(max(legLen * sx, 0.08))) * (0.6 + 0.8 * bGait);
  stride = legLen * 0.5 * min(1.0, bGait * 1.3);
  liftH = legLen * 0.14;
  wingF = 6.0 + 9.0 * pow(massI, -0.17);

  // ---- the spine: pitch of the trunk, neck and head angles, tail carriage
  float pitch = mix(0.06 + 0.18 * (aMorphA.z - 0.5) + 0.14 * smoothstep(0.4, 1.0, neckG) * (1.0 - sprawl),
                    mix(0.62, 0.2, tailG), bBiped * legE) * bWalk;
  pitch += bRest * fl * mix(0.15, 0.45, bBiped);    // a perched flyer sits up
  pitch *= 1.0 - bRest * (1.0 - fl) * 0.7;
  float neckUp = mix(mix(0.3, 1.15, smoothstep(0.1, 0.8, neckG)), 0.08, max(bFish, bAir * 0.85));
  neckUp = max(neckUp, pitch + 0.15) - bEat * (0.9 + 0.6 * neckUp) * (1.0 - bFish) - bStrikeA * 0.5 * neckUp;
  float headA = mix(pitch * 0.4 - 0.12, -0.35, smoothstep(0.2, 1.0, neckUp - pitch * 0.5)) - bEat * 0.7;
  float tailDroop = mix(mix(0.15, 0.95, tailG), 0.25 * tailG, bBiped) * (1.0 - bFish) * (1.0 - 0.7 * bAir);
  tailDroop -= 0.35 * gFur * (1.0 - tailG);           // a short furry tail is carried up
  lB = 1.7 * rX;
  lK = rX * mix(0.15, 1.9, pow(neckG, 1.3));
  float sn = mix(0.75, 0.45 + 1.15 * gStrike, gMeat);  // muzzle length: diet and strike
  lF = hR * (1.3 + 1.25 * sn);
  lT = max(rX * mix(0.08, 2.5, tailG) * (1.0 + 0.5 * gAppend), rY * 0.8 * furV);   // no tail still has a rounded rump
  vec3 C = vec3(0.0), dB = vec3(cos(pitch), sin(pitch), 0.0);   // built at height 0, raised onto its legs below
  float xa = (-0.85 + 1.7 * 0.24) * rX * bBiped;      // a biped pivots over its hips
  vec3 shift = vec3(xa, 0.0, 0.0) - dB * xa;
  kH = C - dB * 0.85 * rX + shift; kS = C + dB * 0.85 * rX + shift;
  dH = dB;
  vec3 nDir = vec3(cos(neckUp), sin(neckUp), 0.0), hDir = vec3(cos(headA), sin(headA), 0.0);
  dS = normalize(dB + nDir * (0.3 + 0.7 * smoothstep(0.0, 0.5 * rX, lK)));
  kO = kS + normalize(nDir * 0.8 + dS * 0.2) * lK;
  dO = normalize(nDir * 0.6 + hDir);
  kN = kO + hDir * lF; dN = hDir;
  // the head looks around when idle
  float yaw = 0.4 * sin(bT * 0.37 + bPh * 3.0) * sin(bT * 0.23 + bPh) * (1.0 - bGait) * (1.0 - bFish) * (1.0 - corpse);
  kO = rotA(kO, kS, vec3(0.0, 1.0, 0.0), yaw * 0.5); kN = rotA(kN, kS, vec3(0.0, 1.0, 0.0), yaw);
  dO = rotA(dO, vec3(0.0), vec3(0.0, 1.0, 0.0), yaw * 0.75); dN = rotA(dN, vec3(0.0), vec3(0.0, 1.0, 0.0), yaw);
  float d1 = pitch + tailDroop;
  vec3 b0 = -dB, b1 = -vec3(cos(d1), sin(d1), 0.0);
  kT = kH + lT * normalize(b0 + b1 * 1.3);
  dT = -b1;
  // stand on the legs: the body's height is what puts the hind hip one bent leg's reach above the ground
  // (a sprawler's thigh runs out to the side, so it stands lower); on its belly without legs or at rest
  vec3 hh0, hT, hN, hB;
  float ttRef = bBiped > 0.5 ? 0.24 : 0.5 - mix(0.34, 0.42, aMorphB.z);
  vec3 hipRef = skin(1.0 + ttRef, 3.14159 - mix(0.75, 1.45, sprawl), hh0, hT, hN, hB) - hh0 * legR * 2.0;
  float reach = ab * legExt * mix(1.0, 0.5, sprawl) + footLen * sin(footAng);
  cY = max(lieY, reach - hipRef.y);
  cY = mix(cY, lieY, bRest * (1.0 - fl));
  cY = mix(cY, rY * 0.3, bFish);                     // a swimmer rides low in the water
  float act = bGait * bWalk * (1.0 - bRest);
  cY += legLen * 0.035 * act * cos(2.0 * (bT * gaitW + bPh));
  vec3 up = vec3(0.0, cY, 0.0);
  kT += up; kH += up; kS += up; kO += up; kN += up;
  jawOpen = bStrikeA * (0.35 + 0.5 * gStrike) * abs(sin(bT * 9.0 + bPh)) + bEat * 0.22 * abs(sin(bT * 10.0 + bPh));
  breath = (0.018 + 0.03 * bSess) * sin(bT * mix(2.6, 1.2, big) + bPh) * (1.0 - corpse);
  swimW = rX * 0.22 * (0.4 + bGait);
  tailSway = lT * (0.1 + 0.18 * bGait) * (1.0 - bFish) * (1.0 - corpse);

  // ---- evaluate the vertex's part; near bodies take the normal from the surface itself
  bPart = mod(aAux.z, 32.0); bSide = aAux.x; bIdx = aAux.y;
  vec3 hint, h2;
  vec3 p = bodyPart(position, hint);
  vec4 skinC = oSkin;
  vec3 n = hint;
  if (aAux.z < 31.5 && bPart < 0.5) {
    // the trunk tube's normal from its own surface (it tapers and bulges; the cross-section alone is not enough)
    float es = 0.008, et = 0.02;
    es = position.x + es <= 4.0 ? es : -es;
    vec3 a = (trunk(position + vec3(es, 0.0, 0.0), h2) - p) * sign(es);
    vec3 b = trunk(position + vec3(0.0, et, 0.0), h2) - p;
    vec3 cr = cross(a, b);
    if (dot(cr, cr) > 1e-16) { cr = normalize(cr); n = dot(cr, hint) < 0.0 ? -cr : cr; }
  }
  // a corpse lies on its side
  if (corpse > 0.5) {
    vec3 c0 = vec3(0.0, cY, 0.0);
    p = rotA(p, c0, vec3(1.0, 0.0, 0.0), 1.45); n = rotA(n, vec3(0.0), vec3(1.0, 0.0, 0.0), 1.45);
    p.y -= cY - rZ * furV * 0.9;
  }
  // strike lunges forward
  p.x += bStrikeA * 0.08 * abs(sin(bT * 7.0 + bPh));
  // the instance squash undone (normals through the inverse transpose)
  p.y *= unsq; n.y /= unsq;
  gBodyP = p; gBodyN = n;

  // ---- pigment: hue, saturation (muted unless the gene is high), lightness; the pattern's second colour
  float hue = aMorphC.z, sat = 0.04 + 0.78 * pow(aMorphC.w, 1.4), light = 0.12 + 0.62 * aMorphD.x;
  float contrast = smoothstep(0.0, 0.2, aMorphD.y);
  vec3 col = pigment(hue, sat, light);
  // the markings' hue: within a third of the wheel of the ground colour (natural pairs, not complements)
  vec3 mk = pigment(fract(hue + 0.6 * (aMorphD.w - 0.5)), sat * 0.9, mix(light, 1.0 - light * 0.8, contrast) * 0.85);
  vCol = mix(col, aClade.rgb, uSpeciesOverlay) * mix(1.0, 0.45, corpse);
  vMark = mix(mk, aClade.rgb * 0.6, uSpeciesOverlay) * mix(1.0, 0.45, corpse);
  vSkin = skinC;
  vPattern = vec4(aMorphD.y, aMorphD.z, gFur, gArmour);
}
`;

const GLSL_FRAG_COMMON = `
varying vec3 vCol; varying vec3 vMark; varying vec4 vSkin; varying vec4 vPattern;
vec2 nrHash(vec2 p) { p = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3))); return fract(sin(p) * 43758.5453); }
/** Distances to the nearest and second nearest cell point (Worley). */
vec2 nrCells(vec2 p) {
  vec2 i = floor(p), f = fract(p); float d1 = 8.0, d2 = 8.0;
  for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++) {
    vec2 o = vec2(float(x), float(y)); vec2 r = o + nrHash(i + o) * 0.85 + 0.075 - f;
    float d = dot(r, r);
    if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
  }
  return sqrt(vec2(d1, d2));
}
`;

const GLSL_COLOR = `
  float cls = floor(vSkin.w + 0.5);
  float up = vSkin.z;
  // markings in coordinates that follow the body: q.x along the spine, q.y from the dorsal line down the flank
  vec2 q = vSkin.xy * mix(3.0, 14.0, vPattern.y);
  float kind = vPattern.x, present = smoothstep(0.03, 0.16, kind);
  float wave = q.x + 1.3 * sin(q.y * 0.9 + q.x * 0.3);
  float st = sin(wave * 1.6);
  float wS = fwidth(st) + 0.02;
  float stripes = smoothstep(0.25 - wS, 0.25 + wS, st);
  vec2 qw = q * 0.55 + 0.35 * sin(q.yx * vec2(0.9, 1.3) + vec2(1.7, 0.4));   // warped, so cells are not tiles
  vec2 cel = nrCells(qw);
  float wc = fwidth(cel.x) + 0.02;
  float spots = 1.0 - smoothstep(0.36 - wc, 0.36 + wc, cel.x);
  float rosy = step(0.75, fract(kind * 5.0));                // a few lineages' spots open into rosettes
  spots = mix(spots, spots * smoothstep(0.14 - wc, 0.14 + wc, cel.x), rosy);
  float edge = cel.y - cel.x, we = fwidth(edge) + 0.02;
  float patches = smoothstep(0.16 - we, 0.22 + we, edge);
  float wStripe = 1.0 - smoothstep(0.38, 0.44, kind), wPatch = smoothstep(0.68, 0.74, kind);
  float mark = mix(mix(stripes, spots, 1.0 - wStripe), patches, wPatch) * present;
  // countershading: a darker back, a paler, less saturated belly; markings fade toward the belly
  vec3 base = vCol;
  float lum = dot(base, vec3(0.3, 0.59, 0.11));
  vec3 belly = mix(base * 1.15, vec3(lum) * 1.2 + vec3(0.03, 0.025, 0.015), 0.3);
  vec3 skinColor = mix(belly, base, smoothstep(-0.85, -0.25, up));
  skinColor = mix(skinColor, base * 0.62, smoothstep(0.45, 1.0, up));
  mark *= smoothstep(-0.8, -0.2, up);
  skinColor = mix(skinColor, vMark * mix(0.8, 1.0, smoothstep(0.0, 1.0, up)), mark);
  if (cls == 1.0) {
    // limbs: the body colour, darker toward the feet
    skinColor = mix(base * 0.9, base * 0.55, smoothstep(0.45, 0.95, vSkin.x));
  } else if (cls == 2.0) {
    // fins and ears: thin skin, paler toward the edge, with darker rays from the root
    float ray = abs(sin(vSkin.y * 3.14159 * 5.0));
    skinColor = mix(base * 0.8, mix(base, belly, 0.5), smoothstep(0.1, 1.0, vSkin.x));
    skinColor = mix(skinColor, vMark, mark * 0.25) * mix(1.0, 0.86, smoothstep(0.85, 1.0, ray) * smoothstep(0.08, 0.3, vSkin.x));
  } else if (cls == 6.0) {
    // a wing membrane: darker skin stretched between finger bones that fan from the wrist (s = 0.62)
    float s = vSkin.x, t = vSkin.y, fd = 1.0;
    for (int k = 0; k < 3; k++) { float sk = 0.62 + (0.15 + 0.13 * float(k)) * t * (0.6 + 0.4 * t); fd = min(fd, abs(s - sk)); }
    fd = min(fd, abs(s - 0.62));
    float fw = 0.012 + fwidth(s);
    skinColor = mix(base * 0.62, base * 0.85, smoothstep(0.0, 0.8, t)) * mix(1.0, 0.6, (1.0 - smoothstep(0.0, fw, fd)) * step(0.6, s + 0.0 * t));
    skinColor = mix(skinColor, vMark * 0.8, mark * 0.3);
  } else if (cls == 3.0) {
    // eyes: a dark pupil in an amber to dark iris
    vec3 iris = mix(vec3(0.5, 0.33, 0.05), vec3(0.06, 0.05, 0.03), step(0.5, fract(vPattern.x * 3.0 + vPattern.y)));
    skinColor = mix(vec3(0.015), iris, smoothstep(0.1, 0.13, vSkin.x));
    skinColor = mix(skinColor, vec3(0.04, 0.035, 0.03), smoothstep(0.28, 0.32, vSkin.x));
  } else if (cls == 4.0) {
    skinColor = vec3(0.75, 0.7, 0.58);
  } else if (cls == 5.0) {
    skinColor = mix(base * 0.55, vMark * 0.5, 0.3) * mix(0.8, 1.15, vSkin.x);
  }
  // armour: scutes over the carapace (grooves between domed cells)
  float arm = smoothstep(0.3, 0.7, vPattern.w) * step(cls, 0.5) * smoothstep(-0.45, 0.1, up);
  vec2 sc = nrCells(vSkin.xy * 7.0);
  float groove = 1.0 - smoothstep(0.03, 0.09 + fwidth(sc.y - sc.x), sc.y - sc.x);
  skinColor *= 1.0 - 0.35 * arm * groove;
  // fur: fine strands along the body
  float strands = sin(vSkin.x * 160.0 + sin(vSkin.y * 90.0) * 2.0);
  float fs = vPattern.z * (1.0 - smoothstep(0.3, 1.5, fwidth(vSkin.x * 160.0))) * step(cls, 1.5);
  skinColor *= 1.0 - 0.12 * fs * (0.5 + 0.5 * strands);
  diffuseColor.rgb *= skinColor;
`;

const GLSL_NORMAL = `
  float faceDirection = gl_FrontFacing ? 1.0 : -1.0;
  vec3 normal = normalize(vNormal);
  if ((cls == 2.0 || cls == 6.0) && dot(normal, vViewPosition) < 0.0) normal = -normal;   // a membrane is lit on the side seen
  // scutes bulge between their grooves
  if (arm > 0.01) {
    float hgt = arm * (sc.y - sc.x) * 0.05;
    vec2 dh = vec2(dFdx(hgt), dFdy(hgt));
    vec3 sx = dFdx(-vViewPosition), sy = dFdy(-vViewPosition);
    vec3 r1 = cross(sy, normal), r2 = cross(normal, sx);
    float det = dot(sx, r1);
    normal = normalize(abs(det) * normal - sign(det) * (dh.x * r1 + dh.y * r2));
  }
  vec3 nonPerturbedNormal = normal;
`;

const GLSL_EMISSIVE = `
  // a little light of its own (no black sides), a soft sheen on fur at the outline, a catchlight in the eye
  float rim = pow(1.0 - abs(dot(normal, normalize(vViewPosition))), 3.0);
  totalEmissiveRadiance += skinColor * (0.07 + rim * (0.05 + 0.3 * vPattern.z) * step(cls, 1.5));
  if (cls == 2.0 || cls == 6.0) totalEmissiveRadiance += skinColor * 0.22;   // thin skin lets light through
  if (cls == 3.0) totalEmissiveRadiance += vec3(0.9) * pow(max(dot(normal, normalize(vec3(0.35, 0.55, 1.0))), 0.0), 28.0);
`;

/** The one material all organisms share (near and far meshes). uTime in material.userData.uniforms. */
export function bodyMaterial() {
  const mat = new THREE.MeshLambertMaterial();
  const u = { uTime: { value: 0 }, uSpeciesOverlay: { value: 0 } };
  mat.userData.uniforms = u;
  mat.onBeforeCompile = (sh) => {
    Object.assign(sh.uniforms, u);
    sh.vertexShader = sh.vertexShader
      .replace('#include <common>', '#include <common>\n' + GLSL_COMMON)
      .replace('#include <beginnormal_vertex>', GLSL_BODY + '\nvec3 objectNormal = gBodyN;')
      .replace('#include <begin_vertex>', 'vec3 transformed = gBodyP;');
    sh.fragmentShader = sh.fragmentShader
      .replace('#include <common>', '#include <common>\n' + GLSL_FRAG_COMMON)
      .replace('#include <color_fragment>', '#include <color_fragment>\n' + GLSL_COLOR)
      .replace('#include <normal_fragment_begin>', GLSL_NORMAL)
      .replace('#include <emissivemap_fragment>', '#include <emissivemap_fragment>\n' + GLSL_EMISSIVE);
  };
  mat.customProgramCacheKey = () => 'neuroreef-body-v2';
  return mat;
}
