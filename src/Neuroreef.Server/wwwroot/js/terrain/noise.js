// A tileable value-noise texture shared by the terrain shaders and the CPU (2026-11-01).
//
// The crag displacement (ground.js) moves drawn vertices, so the CPU's heightAt (where animals stand, trees grow, rays
// hit the ground) must compute the very same function: both sample this one 256 x 256 table with bilinear
// interpolation (the GPU through the texture unit, the CPU in noise()), and both sum the same octaves (ridged()).
// The table is random values blurred twice (so bilinear interpolation of it is smooth and shows no texel grid), then
// stretched back to the full 0..255 range.

import * as THREE from 'three';

export const NOISE_N = 256;
export const NOISE_UNIT = 1.5;          // world units per noise texel at the first octave

function build() {
  const N = NOISE_N, a = new Float32Array(N * N), b = new Float32Array(N * N);
  let s = 0x9e3779b9;
  for (let i = 0; i < N * N; i++) { s = Math.imul(s ^ (s >>> 15), 0x2c1b3c6d); s = Math.imul(s ^ (s >>> 12), 0x297a2d39); s ^= s >>> 15; a[i] = (s >>> 0) / 4294967296; }
  const blur = (src, dst, dx, dy) => {   // a 5-tap box along one axis, wrapping
    for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) {
      let t = 0;
      for (let k = -2; k <= 2; k++) t += src[((y + k * dy + N) % N) * N + ((x + k * dx + N) % N)];
      dst[y * N + x] = t / 5;
    }
  };
  for (let r = 0; r < 2; r++) { blur(a, b, 1, 0); blur(b, a, 0, 1); }
  let lo = 1, hi = 0; for (const v of a) { lo = Math.min(lo, v); hi = Math.max(hi, v); }
  const out = new Uint8Array(N * N);
  for (let i = 0; i < N * N; i++) out[i] = Math.round((a[i] - lo) / (hi - lo) * 255);
  return out;
}

export const NOISE = build();

let tex = null;
/** The table as a repeating, mipmapped single-channel texture (one per page). */
export function noiseTexture() {
  if (tex) return tex;
  tex = new THREE.DataTexture(NOISE, NOISE_N, NOISE_N, THREE.RedFormat, THREE.UnsignedByteType);
  tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
  tex.magFilter = THREE.LinearFilter; tex.minFilter = THREE.LinearMipmapLinearFilter;
  tex.generateMipmaps = true; tex.needsUpdate = true;
  return tex;
}

/** The texture's bilinear sample at uv (as the GPU samples it with LinearFilter and RepeatWrapping), 0..1. */
export function noise(u, v) {
  const N = NOISE_N, px = u * N - 0.5, py = v * N - 0.5;
  const x0 = Math.floor(px), y0 = Math.floor(py), fx = px - x0, fy = py - y0;
  const xa = ((x0 % N) + N) % N, ya = ((y0 % N) + N) % N, xb = (xa + 1) % N, yb = (ya + 1) % N;
  const a = NOISE[ya * N + xa], b = NOISE[ya * N + xb], c = NOISE[yb * N + xa], d = NOISE[yb * N + xb];
  return ((a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy) / 255;
}

// three octaves of ridged noise, each rotated and offset so their grids never line up
const OCT = [[1.0, 0.0, 0.0, 0.0, 0.55], [2.03, 0.6, 37.1, 11.7, 0.30], [4.11, 1.3, 71.3, 53.9, 0.15]];

/** Ridged multi-octave noise at a world position (x, z): sharp crests, 0..1, about 0.5 on average. */
export function ridged(x, z) {
  let s = 0;
  for (const [f, r, ox, oz, a] of OCT) {
    const c = Math.cos(r), si = Math.sin(r), px = (c * x - si * z) * f + ox, pz = (si * x + c * z) * f + oz;
    const n = noise(px / (NOISE_UNIT * NOISE_N), pz / (NOISE_UNIT * NOISE_N));
    s += a * (1 - Math.abs(2 * n - 1));
  }
  return s;
}

/** The mean of ridged() over the plane (measured once), so the crag displacement adds no net height. */
export const RIDGE_MEAN = (() => {
  let s = 0, n = 0;
  for (let y = 0; y < 96; y++) for (let x = 0; x < 96; x++) { s += ridged(x * 7.31 + 0.37, y * 6.97 + 0.71); n++; }
  return s / n;
})();

/** The same function in GLSL (needs `uniform sampler2D tNoise;`). Vertex shaders sample level 0. */
export const NOISE_GLSL = `
float nzL(vec2 p) { return textureLod(tNoise, p, 0.0).r; }
float ridgedNoise(vec2 w) {
  float s = 0.0; vec2 p; float n;
  ${OCT.map(([f, r, ox, oz, a]) => `
  p = vec2(${Math.cos(r).toFixed(8)} * w.x - ${Math.sin(r).toFixed(8)} * w.y, ${Math.sin(r).toFixed(8)} * w.x + ${Math.cos(r).toFixed(8)} * w.y) * ${f.toFixed(4)} + vec2(${ox.toFixed(4)}, ${oz.toFixed(4)});
  n = nzL(p / ${(NOISE_UNIT * NOISE_N).toFixed(4)});
  s += ${a.toFixed(4)} * (1.0 - abs(2.0 * n - 1.0));`).join('')}
  return s;
}
`;
