// The body material: a Lambert material whose vertex shader builds each organism's body from its genes and
// animates it. The geometry (body.js) holds every part in its own unit frame; per instance the shader reads
//   aGeneA = (legs, fins, wings, sight)        organ sizes 0..1
//   aGeneB = (armour, strike, meat, mass)      organ sizes 0..1, meat-eating 0..0.95, mass gene 0..1
//   aAnim  = (gait 0..1, phase 0..1, action bits, resting)
//   aClade = (clade colour as linear RGB, packed counts): w = loco * 64 + (fin pairs - 1) * 16 + (eye pairs - 1) * 4
//            + (leg pairs - 1); loco 0 walk, 1 wade, 2 swim, 3 fly, 4 settled; each count 1..4
// and from them places, sizes and colours the parts: legs grow from the hips and lift the body, fins sprout
// along the sides, back and tail, wings from the shoulders, a long toothed snout from the strike gene,
// plates along the back from armour. Locomotion follows the sim's rule (Body.cs): fly if wings >= 0.5, else
// walk if legs >= 0.3, swim if fins >= 0.3 (wade if both), settled if none; the server sends the result.
// The number of legs (2 to 8, in pairs) is a gene too: the pairs spread along the trunk. No skeletons and no per-clade
// meshes: every organism is one instance of the same mesh.

import * as THREE from 'three';

const GLSL_COMMON = `
uniform float uTime;
attribute float aPart; attribute vec3 aAux;
attribute vec4 aAnim; attribute vec4 aGeneA; attribute vec4 aGeneB; attribute vec4 aClade;
varying vec3 vCol;
vec3 rz(vec3 p, vec3 c, float a) { p -= c; float s = sin(a), k = cos(a); return vec3(p.x * k - p.y * s, p.x * s + p.y * k, p.z) + c; }
vec3 rx(vec3 p, vec3 c, float a) { p -= c; float s = sin(a), k = cos(a); return vec3(p.x, p.y * k - p.z * s, p.y * s + p.z * k) + c; }
vec3 ry(vec3 p, vec3 c, float a) { p -= c; float s = sin(a), k = cos(a); return vec3(p.x * k + p.z * s, p.y, -p.x * s + p.z * k) + c; }
float bit(float v, float b) { return mod(floor(v / b), 2.0); }
`;

const GLSL_BODY = `
{
  vec3 L = position;
  float legs = aGeneA.x, fins = aGeneA.y, wings = aGeneA.z, sight = aGeneA.w;
  float armour = aGeneB.x, strike = aGeneB.y, meat = aGeneB.z, mass = aGeneB.w;
  float gait = aAnim.x, ph = aAnim.y * 6.2832, acts = aAnim.z, rest = aAnim.w;
  float eat = max(bit(acts, 1.0), bit(acts, 4.0)), strikeA = bit(acts, 2.0);
  float t = uTime;
  float part = floor(aPart + 0.5), side = aAux.x, px = aAux.y;
  // locomotion from the organs (Body.cs)
  float pk = floor(aClade.w + 0.5), loco = floor(pk / 64.0);
  float finPairs = mod(floor(pk / 16.0), 4.0) + 1.0, eyePairs = mod(floor(pk / 4.0), 4.0) + 1.0, pairs = mod(pk, 4.0) + 1.0;
  float fl = 1.0 - step(0.5, abs(loco - 3.0));
  float wk = step(loco, 1.5) * (1.0 - fl);
  float sw = (1.0 - step(0.5, abs(loco - 1.0)) + 1.0 - step(0.5, abs(loco - 2.0))) * (1.0 - fl);
  float fish = sw * (1.0 - wk);                       // a pure swimmer
  float sess = (1.0 - fl) * (1.0 - wk) * (1.0 - sw);  // settled
  float air = fl * (1.0 - rest);                      // flying now
  // the trunk: swimmers long and narrow, armour broad and deep, flyers light, settled organisms flat and round
  float rxB = 0.55 * (1.0 + 0.45 * fish * fins + 0.2 * wk * legs + 0.12 * (pairs - 2.0) * legs - 0.12 * fl + 0.3 * sess);
  float ryB = 0.32 * (1.0 + 0.25 * armour + 0.12 * mass - 0.1 * fish) * (1.0 - sess * (0.62 - 0.4 * mass));
  float rzB = 0.30 * (1.0 + 0.35 * armour + 0.1 * mass - 0.3 * fish * fins - 0.12 * fl + 0.95 * sess);
  // legs: length and thickness from the legs gene (thicker on heavy or armoured bodies); nothing when tiny
  float legE = smoothstep(0.05, 0.3, legs);
  float legLen = (0.12 + 1.05 * legs) * legE * (1.0 - 0.55 * fl);
  float legR = (0.035 + 0.045 * legs + 0.04 * mass + 0.03 * armour) * legE;
  // the body stands on its legs, or lies on its belly; a swimmer is centred on the water line
  float cy = (1.0 - fish) * max(ryB * 0.85, 0.45 * ryB + legLen * 0.97);
  vec3 C = vec3(0.0, cy, 0.0);
  float finE = smoothstep(0.06, 0.3, fins);
  vec3 p = L;

  if (part < 0.5) {
    p = C + L * vec3(rxB, ryB, rzB);
  } else if (part < 6.5 || (part > 14.5)) {
    // head, eyes, snout, jaw, teeth and neck: one group that dips to eat. Long legs carry a long neck
    float hr = 0.2 * (1.0 + 0.25 * strike + 0.12 * sight + 0.1 * armour) * (1.0 - 0.45 * sess);
    float nl = rxB * 0.75 * legs * wk;                            // neck length
    vec3 neck = C + vec3(rxB * 0.65, ryB * 0.15, 0.0);
    vec3 H = C + vec3(rxB * (0.9 - 0.14 * fish) + hr * 0.3 * (1.0 - fish) + nl * 0.55, ryB * (0.25 + 0.2 * legs) * (1.0 - fish) - 0.2 * ryB * sess + nl * 0.75, 0.0);
    float sl = hr * (0.45 + 1.15 * strike);                       // snout length
    vec3 So = H + vec3(hr * 0.55, -hr * 0.1, 0.0), Jo = H + vec3(hr * 0.3, -hr * 0.42, 0.0);
    float th = hr * 0.42 * smoothstep(0.15, 0.5, strike);         // tooth length
    float jawA = -strikeA * (0.3 + 0.6 * strike) * abs(sin(t * 9.0 + ph)) - eat * 0.2 * abs(sin(t * 10.0 + ph));
    if (part < 1.5) p = H + L * vec3(hr * 1.15, hr * 0.9, hr * 0.85);
    else if (part < 2.5) {
      // eyes: as many pairs as the genes say, set back and up the head; more eyes, each smaller
      float j = px, has = step(j + 0.5, eyePairs);
      float er = hr * (0.14 + 0.3 * sight) / (1.0 + 0.3 * (eyePairs - 1.0));
      p = H + vec3(hr * (0.58 - 0.2 * j), hr * (0.36 + 0.13 * j), side * hr * (0.6 - 0.04 * j)) + L * er * has;
    }
    else if (part < 3.5) p = So + L * vec3(sl, hr * 0.42, hr * 0.5);
    else if (part < 4.5) p = rz(Jo + L * vec3(sl * 0.92, hr * 0.22, hr * 0.42), Jo, jawA);
    else if (part > 14.5) {
      vec3 d = H - neck, n1 = normalize(vec3(-d.y, d.x, 0.0) + 1e-5);
      float tt = -L.y;
      p = neck + d * tt + (n1 * L.x + vec3(0.0, 0.0, L.z)) * hr * 0.55 * smoothstep(0.02, 0.15, nl);
    }
    else if (part < 5.5) {
      vec3 b = So + vec3(px * sl, -hr * 0.42 * 0.55 * (1.0 - px), side * (1.0 - px) * hr * 0.5 * 0.72);
      p = b + vec3(L.x * hr * 0.07, L.y * th, L.z * hr * 0.07);   // upper teeth point down (geometry tip at y = -1)
    } else {
      vec3 b = Jo + vec3(px * sl * 0.92, hr * 0.22 * 0.5 * (1.0 - px), side * (1.0 - px) * hr * 0.42 * 0.72);
      p = rz(b + vec3(L.x * hr * 0.06, L.y * th * 0.8, L.z * hr * 0.06), Jo, jawA);
    }
    p = rz(p, neck, -eat * 0.45 * (0.75 + 0.25 * sin(t * 9.0 + ph)) * (1.0 - sess));
  } else if (part < 7.5) {
    // legs from the hips: as many pairs as the genes say, spread along the trunk (a slot beyond them
    // collapses); neighbouring legs alternate (a trot on four, a tripod gait on six); tucked up in flight
    float has = step(px + 0.5, pairs);
    float fb = pairs < 1.5 ? 0.0 : 1.0 - 2.0 * px / (pairs - 1.0);     // +1 front .. -1 back
    vec3 hip = C + vec3(fb * rxB * (0.45 + 0.08 * pairs), -0.45 * ryB, side * rzB * 0.62);
    float knee = sin(-L.y * 3.1416) * 0.12 * legLen * (fb >= 0.0 ? 1.0 : -1.0);
    vec3 q = hip + vec3(L.x * legR + knee, L.y * legLen, L.z * legR) * has;
    float off = mod(px + (side > 0.0 ? 0.0 : 1.0), 2.0) * 3.1416;
    float a = sin(t * 7.0 * (1.3 - 0.6 * legs) + ph + off) * 0.55 * gait * (1.0 - rest);
    p = rz(q, hip, a + air * 0.9);
  } else if (part < 8.5) {
    // side fins: as many pairs as the genes say, spread from the shoulder back along the flank; paddle
    float has = step(px + 0.5, finPairs);
    float fs = (0.12 + 0.55 * fins) * finE * has * (1.0 - 0.12 * px);
    float fx = finPairs < 1.5 ? 0.3 : 0.35 - 0.85 * px / (finPairs - 1.0);
    vec3 root = C + vec3(rxB * fx, -ryB * 0.35, side * rzB * 0.8 * sqrt(max(0.2, 1.0 - fx * fx)));
    vec3 q = root + vec3(L.x * fs, -0.15 * L.z * fs, side * L.z * fs);
    p = rx(q, root, -side * sin(t * 5.0 + ph - px * 1.2) * 0.35 * (0.3 + gait));
  } else if (part < 9.5) {
    float fd = smoothstep(0.1, 0.35, fins) * (0.25 + 0.85 * fins);
    p = C + vec3(L.x * rxB * 1.1, ryB * 0.8 + L.y * ryB * 1.4 * fd, L.z);
  } else if (part < 11.5) {
    // the tail (a stalk, plus a fan from the fins gene): sways, fast and wide on a swimmer
    float tl = rxB * (0.4 + 0.55 * fins * fish + 0.35 * legs * (1.0 - fish)) * (1.0 - 0.75 * sess);
    float tt = ryB * (0.32 + 0.2 * fish);
    float droop = 0.35 * wk;
    vec3 root = C + vec3(-rxB * 0.8, ryB * 0.1 * (1.0 - fish), 0.0);
    if (part < 10.5) p = root + vec3(L.x * tl, L.y * tt + L.x * tl * droop, L.z * tt * 0.8);
    else {
      float tfs = (0.08 + 0.6 * fins) * finE;
      p = root + vec3(-tl * 0.95, -tl * droop * 0.95, 0.0) + vec3(L.x * tfs, L.y * tfs, L.z);
    }
    p = ry(p, root, sin(t * (2.3 + 5.0 * fish) + ph) * (0.25 + 0.3 * fish) * (0.35 + gait));
  } else if (part < 12.5) {
    // wings from the shoulders: flap in flight, folded back along the body otherwise
    float wE = smoothstep(0.06, 0.3, wings);
    float span = rxB * (0.25 + 2.4 * wings) * wE, chord = rxB * (0.45 + 0.5 * wings) * wE;
    vec3 sh = C + vec3(rxB * 0.12, ryB * 0.6, side * rzB * 0.55);
    vec3 q = sh + vec3(L.x * chord, L.y, side * L.z * span);
    float fold = 1.0 - air;
    q = ry(q, sh, -side * 1.25 * fold);
    float flap = air * (sin(t * (10.0 + 8.0 * (1.0 - mass)) + ph) * 0.85 + 0.15);
    p = rx(q, sh, -side * (flap + 0.12 * fold));
  } else {
    // armour: plates (spikes when the organism is also armed) along the back, scutes along the flanks
    float aE = smoothstep(0.15, 0.45, armour);
    float s = sqrt(max(0.0, 1.0 - px * px));
    float pw = rxB * 0.13 * aE, phh = ryB * (0.15 + 0.55 * armour + 0.6 * armour * strike) * aE;
    if (part < 13.5) p = C + vec3(px * rxB, ryB * s * 0.92, 0.0) + vec3(L.x * pw, L.y * phh, L.z * pw * 0.6);
    else {
      vec3 b = C + vec3(px * rxB, ryB * s * 0.5, side * rzB * s * 0.8);
      p = b + vec3(L.x * pw * 0.8, L.z * pw * 0.5, 0.0) + vec3(0.0, 0.55, side * 0.84) * L.y * phh * 0.5;
    }
  }
  // a swimmer's body is a travelling wave; settled bodies pulse; strike lunges; everyone breathes
  p.z += sin(p.x * 3.5 - t * 7.0 - ph) * 0.1 * fish * (0.4 + gait) * clamp(0.5 - p.x * 0.8, 0.0, 1.6);
  p.x += strikeA * 0.1 * abs(sin(t * 7.0 + ph));
  p.y *= 1.0 + (0.015 + 0.05 * sess) * sin(t * 2.0 + ph);
  transformed = p;

  // colour: the clade colour, countershaded (paler underneath); armour darkens, meat-eating reddens
  vec3 c = aClade.rgb, belly = mix(c, vec3(1.0), 0.5), horn = vec3(0.79, 0.72, 0.55);
  vec3 col = c; float skin = 1.0;
  if (part > 14.5) col = mix(c, belly, 0.3);
  else if (part < 1.5 || (part > 2.5 && part < 3.5)) col = mix(c, belly, clamp((-L.y + 0.1) * 1.6, 0.0, 1.0));
  else if (part < 2.5) { col = vec3(0.012); skin = 0.0; }
  else if (part < 4.5) col = belly;
  else if (part < 6.5) { col = horn; skin = 0.0; }
  else if (part < 7.5) col = c * mix(0.65, 0.35, -L.y);
  else if (part < 9.5 || (part > 10.5 && part < 11.5)) col = mix(c * 0.85, vec3(1.0), 0.3);
  else if (part < 10.5) col = mix(c, belly, clamp(-L.y * 1.5, 0.0, 1.0));
  else if (part < 12.5) col = mix(c * 0.8, vec3(1.0), 0.12 + 0.3 * L.z);
  else col = mix(c * 0.3, horn, 0.4);
  col *= 1.0 - 0.25 * armour * skin;
  col = mix(col, col * vec3(1.3, 0.72, 0.68), meat * 0.7 * skin);
  vCol = col;
}
`;

/** The one material all organisms share (near and far meshes). uTime in material.userData.uniforms. */
export function bodyMaterial() {
  const mat = new THREE.MeshLambertMaterial({ flatShading: true });
  const u = { uTime: { value: 0 } };
  mat.userData.uniforms = u;
  mat.onBeforeCompile = (sh) => {
    Object.assign(sh.uniforms, u);
    sh.vertexShader = sh.vertexShader
      .replace('#include <common>', '#include <common>\n' + GLSL_COMMON)
      .replace('#include <begin_vertex>', '#include <begin_vertex>\n' + GLSL_BODY);
    sh.fragmentShader = sh.fragmentShader
      .replace('#include <common>', '#include <common>\nvarying vec3 vCol;')
      .replace('#include <color_fragment>', '#include <color_fragment>\ndiffuseColor.rgb *= vCol;')
      .replace('#include <emissivemap_fragment>', '#include <emissivemap_fragment>\ntotalEmissiveRadiance += vCol * 0.12;');
  };
  mat.customProgramCacheKey = () => 'neuroreef-body-1';
  return mat;
}
