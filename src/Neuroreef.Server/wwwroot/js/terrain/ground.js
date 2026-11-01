// The ground (2026-11-01): one heightfield drawn with a continuous level of detail, and its materials from the data.
//
// Geometry (CDLOD, Strugar 2009). The ground is the sim's own surface: one height per cell (WORLD elevation at the true
// scale), split into triangles along each quad's water diagonal, exactly as before. It lives in a float texture, and a
// single flat grid patch (GRID x GRID quads with a skirt) is drawn once per node of a quadtree laid over the 3x3 tiling
// around the camera: nodes split while the camera is closer than SPLIT x their size, so a node's quads are about the
// same size on screen everywhere: 1 unit (a quarter of a cell) near the camera, 1 cell a few hundred units away, several
// cells at the horizon. All nodes are one instanced draw call. Each vertex reads the surface from the texture (so
// the copies of the map, the wrap and every level agree to the bit), and morphs to its parent's grid as it nears the
// distance at which the parent takes over, so neighbouring levels meet without cracks (the skirts catch the rest).
//
// Detail, bounded by the data. Near the camera (within DISP_NEAR..DISP_FAR) bare rock gets crags: ridged noise up to
// DISP_MAX units (~50 m at 1 unit = 54.5 m), zero-mean, scaled by the cell's rockiness (steep, high or ridge ground; 0 on
// water and its shores), so the drawn ground never strays more than DISP_MAX from the sim's. River channels, sinkholes
// and cave notches are cut by a carve texture (a cap on the height, CR texels per cell, rivers.js and caves.js).
// heightAt() computes the same function on the CPU (same texture values, same noise table, same octaves).
//
// Materials (fragment shader), all from the data:
// - cover: the FIELDS colour of soil, grass, shrubs, trees, flowers, ponds, floods, fire (world3d.js _paint);
// - rock where the drawn slope is steep (above ~35 degrees) or the cell is rocky, unless plants cover it; coloured by its
//   kind (FIELDS 'volcanic', 'sedimentary' and 'limestone', the rest crystalline): speckled granite, dark basalt, banded
//   beds, pale limestone;
//   triplanar procedural grain and a bump, faceted (crisp) normals on rock;
// - scree: rubble on the moderate slopes around rock;
// - snow from the snowpack (FIELDS 'snow'; without it, where the air is below freezing), not on cliffs;
// - alluvium on the flats beside rivers (FIELDS 'alluvium' and the channel band), wet beaches along standing water,
//   dark wet river beds, and far away (where a river ribbon is smaller than a pixel) the river's water itself;
// - cave mouths, sinkhole floors and springs (caves.js).

import * as THREE from 'three';
import { NOISE_GLSL, noiseTexture, ridged, RIDGE_MEAN } from './noise.js';

export const LEAF = 32;          // the smallest node (world units): GRID quads of 1 unit
export const GRID = 32;          // quads per node side
export const SPLIT = 2.8;        // a node splits while the camera is within SPLIT x its size
export const DISP_MAX = 0.9, DISP_NEAR = 55, DISP_FAR = 95;   // crag displacement: amplitude (units), fade with distance
const NONE = 1e4;                // carve: no cap

export const GLSL_COMMON = /* glsl */`
uniform highp sampler2D tH;      // per cell: ground height, diagonal, level of standing water nearby, rockiness
uniform highp sampler2D tCarve;  // CR texels per cell: the highest the ground may be (channels, sinkholes, caves)
uniform sampler2D tNoise;
uniform float uFw, uS, uCR;
vec4 cellAt(vec2 c) { return texelFetch(tH, ivec2(mod(c, uFw)), 0); }
// the drawn triangle's height at a world position (x) and the cell rockiness there (y), as World3D._ground
vec2 groundTri(vec2 wp) {
  vec2 g = wp / uS, c0 = floor(g), t = g - c0;
  vec4 A = cellAt(c0), B = cellAt(c0 + vec2(1.0, 0.0)), C = cellAt(c0 + vec2(0.0, 1.0)), D = cellAt(c0 + vec2(1.0, 1.0));
  float a = A.r, b = B.r, c = C.r, d = D.r, h;
  if (A.g > 0.5) h = t.y >= t.x ? a + (d - c) * t.x + (c - a) * t.y : a + (b - a) * t.x + (d - b) * t.y;
  else h = t.x + t.y <= 1.0 ? a + (b - a) * t.x + (c - a) * t.y : d + (c - d) * (1.0 - t.x) + (b - d) * (1.0 - t.y);
  float r = mix(mix(A.a, B.a, t.x), mix(C.a, D.a, t.x), t.y);
  return vec2(h, r);
}
float carveCap(vec2 wp) {
  float N = uFw * uCR;
  vec2 g = wp / uS * uCR, c0 = floor(g), t = g - c0;
  float a = texelFetch(tCarve, ivec2(mod(c0, N)), 0).r, b = texelFetch(tCarve, ivec2(mod(c0 + vec2(1.0, 0.0), N)), 0).r;
  float c = texelFetch(tCarve, ivec2(mod(c0 + vec2(0.0, 1.0), N)), 0).r, d = texelFetch(tCarve, ivec2(mod(c0 + vec2(1.0, 1.0), N)), 0).r;
  return mix(mix(a, b, t.x), mix(c, d, t.x), t.y);
}
${NOISE_GLSL}
`;

const VERT_DECL = /* glsl */`
attribute vec3 iNode;            // node origin x, z and size (world units)
uniform float uSplit, uGrid, uDisp, uDispNear, uDispFar, uRidgeMean;
uniform vec3 uCam;
varying vec3 vWP;
varying float vDetail, vDist;
${GLSL_COMMON}
`;

const VERT_MAIN = /* glsl */`
  vec2 lp = position.xz;
  vec2 wp = iNode.xy + lp * iNode.z;
  // morph towards the parent's grid (every other vertex) as the distance nears where the parent takes over
  float d0 = distance(uCam, vec3(wp.x, cellAt(floor(wp / uS + 0.5)).r, wp.y));   // one fetch: any fixed function of wp will do
  float R = uSplit * 2.0 * iNode.z;
  float mk = clamp((d0 - 0.78 * R) / (0.18 * R), 0.0, 1.0);
  lp -= fract(lp * uGrid * 0.5) * 2.0 / uGrid * mk;
  wp = iNode.xy + lp * iNode.z;
  vec2 gt = groundTri(wp);
  float dist = distance(uCam, vec3(wp.x, gt.x, wp.y));
  float fade = 1.0 - smoothstep(uDispNear, uDispFar, dist);
  float h = gt.x;
  if (gt.y * fade > 0.0) h += uDisp * gt.y * fade * (ridgedNoise(wp) - uRidgeMean) * 2.0;
  float cap = carveCap(wp);
  float carved = smoothstep(0.0, 0.25, h - cap);
  h = min(h, cap);
  h -= position.y * (0.25 + 0.01 * iNode.z);       // skirt vertices hang below the edge
  vec3 transformed = vec3(wp.x, h, wp.y);
  vWP = transformed; vDist = dist;
  vDetail = clamp(gt.y * (0.4 + 0.6 * fade) + carved * (1.0 - smoothstep(90.0, 160.0, dist)), 0.0, 1.0);
`;

const FRAG_DECL = /* glsl */`
uniform sampler2D tCover, tMat, tMat2, tNrm, tChan, tFeat;
uniform float uPlain;
varying vec3 vWP;
varying float vDetail, vDist;
vec3 gTerrN;
${GLSL_COMMON}
float tri3(vec3 p, vec3 w, float sc) {
  return texture(tNoise, p.zy / sc).r * w.x + texture(tNoise, p.xz / sc).r * w.y + texture(tNoise, p.xy / sc).r * w.z;
}
float shoreAt(vec2 wp) {
  vec2 g = wp / uS, c0 = floor(g), t = g - c0;
  return mix(mix(cellAt(c0).b, cellAt(c0 + vec2(1.0, 0.0)).b, t.x), mix(cellAt(c0 + vec2(0.0, 1.0)).b, cellAt(c0 + vec2(1.0, 1.0)).b, t.x), t.y);
}
vec3 riverWater(float flow) { return mix(vec3(0.07, 0.24, 0.30), vec3(0.03, 0.10, 0.17), flow); }
`;

const FRAG_MAIN = /* glsl */`
  vec2 uvC = (vWP.xz / uS + 0.5) / uFw;
  vec2 uvK = (vWP.xz / uS * uCR + 0.5) / (uFw * uCR);
  vec4 cov = texture(tCover, uvC), mat = texture(tMat, uvC), mat2 = texture(tMat2, uvC), nr = texture(tNrm, uvC), ch = texture(tChan, uvK), ft = texture(tFeat, uvK);
  vec2 nxz = nr.rg * 2.0 - 1.0;
  vec3 nS = normalize(vec3(nxz.x, sqrt(max(0.02, 1.0 - dot(nxz, nxz))), nxz.y));
  vec3 dpx = dFdx(vWP), dpy = dFdy(vWP);
  vec3 nF = normalize(cross(dpy, dpx)); if (nF.y < 0.0) nF = -nF;
  vec3 n = normalize(mix(nS, nF, vDetail));
  vec3 tw = pow(abs(n), vec3(4.0)); tw /= (tw.x + tw.y + tw.z);
  float fine = 1.0 - smoothstep(150.0, 500.0, vDist);
  float nBig = texture(tNoise, vWP.xz / 1500.0).r;
  float nMid = tri3(vWP, tw, 220.0);
  float nFine = mix(0.5, tri3(vWP, tw, 36.0), fine);
  float sl = 1.0 - n.y;                    // 0 flat; 0.18 at 35 degrees, 0.29 at 45, 0.5 at 60
  float plants = cov.a;
  vec3 col = cov.rgb;
  if (uPlain < 0.5) {
    // rock: steep drawn slopes, or rocky cells that are not flat; plants hide it
    float rockF = max(smoothstep(0.19, 0.30, sl + (nMid - 0.5) * 0.10), nr.b * smoothstep(0.02, 0.10, sl + 0.06 * (nBig - 0.5)));
    rockF = clamp(rockF * (1.0 - 0.8 * smoothstep(0.35, 0.9, plants)), 0.0, 1.0);
    float scree = nr.a * smoothstep(0.10, 0.15, sl) * (1.0 - rockF) * (1.0 - 0.85 * plants);   // talus: 30-37 degrees below the crags
    float vol = mat.g, sed = mat.b, lim = mat2.r, cry = max(0.0, 1.0 - vol - sed - lim);
    float band = mix(0.5, sin(vWP.y * 2.4 + (nMid - 0.5) * 6.0) * 0.5 + 0.5, 1.0 - smoothstep(0.4, 1.2, fwidth(vWP.y * 2.4)));
    vec3 cC = mix(vec3(0.1643, 0.1562, 0.1483), vec3(0.3619, 0.3371, 0.3017), nMid) * (0.80 + 0.40 * nFine);
    vec3 cV = mix(vec3(0.035, 0.032, 0.032), vec3(0.11, 0.075, 0.06), nMid) * (0.80 + 0.40 * nFine);
    vec3 cS = mix(vec3(0.2684, 0.1899, 0.1122), vec3(0.5004, 0.4143, 0.2793), band) * (0.85 + 0.30 * nFine);
    // limestone: pale grey
    vec3 cL = mix(vec3(0.3494, 0.3371, 0.2904), vec3(0.6121, 0.5954, 0.5156), nMid) * (0.82 + 0.36 * nFine);
    vec3 rockC = cC * cry + cV * vol + cS * sed + cL * lim;
    // ledges: rock weathers into steps along its beds and joints (dark lines where a warped height crosses a step)
    float ledgeArg = vWP.y * 0.9 + (nMid - 0.5) * 2.5 + (nBig - 0.5) * 4.0;
    float ledge = fract(ledgeArg);
    float ledgeAA = (1.0 - smoothstep(0.12, 0.35, fwidth(ledgeArg))) * smoothstep(0.5, 0.9, rockF);   // on bare rock; gone before finer than a pixel
    float ledgeLine = smoothstep(0.82, 0.97, ledge) * ledgeAA;
    rockC *= 1.0 - 0.45 * ledgeLine;
    // alluvium: silt and sand on the flats beside rivers, under what grows there
    float allu = max(ch.a, mat.a) * (1.0 - smoothstep(0.03, 0.10, sl));
    col = mix(col, vec3(0.2793, 0.2176, 0.1332) * (0.9 + 0.2 * nFine), allu * (1.0 - plants) * 0.65);
    // beaches and wet ground along standing water
    float above = vWP.y - shoreAt(vWP.xz);
    float beach = (1.0 - smoothstep(0.1, 0.9, above)) * (1.0 - smoothstep(0.05, 0.15, sl)) * (1.0 - 0.7 * plants);
    col = mix(col, vec3(0.4281, 0.3494, 0.1989) * (0.92 + 0.16 * nFine), beach * 0.65);
    col = mix(col, mix(rockC, vec3(0.1899, 0.1643, 0.1332), 0.45) * (0.65 + 0.7 * nFine), scree);
    col = mix(col, rockC, rockF);
    // river beds: dark and wet under the water
    float bed = smoothstep(0.0, 0.3, ch.r);
    col = mix(col, vec3(0.0259, 0.0230, 0.0132) * (0.85 + 0.3 * nFine), bed * 0.85);
    // cave mouths, sinkholes and springs
    col = mix(col, mix(rockC * 0.55, vec3(0.0004, 0.0004, 0.0003), smoothstep(0.75, 0.95, ft.g)), smoothstep(0.05, 0.5, ft.g) * 0.9);
    col = mix(col, col * vec3(0.5, 0.72, 0.45), ft.b * 0.8);
    col = mix(col, vec3(0.0001, 0.0000, 0.0000), smoothstep(0.35, 0.6, ft.r));
    // snow and ice where the snowpack lies, never on cliffs
    float snowF = smoothstep(0.25, 0.55, mat.r + (nMid - 0.5) * 0.35 + (nBig - 0.5) * 0.25) * (1.0 - smoothstep(0.36, 0.55, sl)) * (1.0 - bed);
    col = mix(col, mix(vec3(0.5789, 0.6814, 0.8324), vec3(0.97, 0.98, 1.0), smoothstep(0.55, 0.95, n.y) * (0.8 + 0.2 * nFine)), snowF);
    col *= 1.0 - 0.32 * (1.0 - smoothstep(0.0, 0.35, above)) * (1.0 - snowF);
    // far away the river's own ribbon is under a pixel: its water is painted here
    col = mix(col, riverWater(ch.g), smoothstep(110.0, 320.0, vDist) * smoothstep(0.0, 0.25, ch.r));
    // close up: the grain of rock and rubble as a bump (Mikkelsen's surface gradient, as three's bump maps)
    float hb = nFine * 0.7 + nMid * 0.3 + band * sed * 0.25 + ledge * 0.35 * ledgeAA;
    float bump = (smoothstep(0.4, 0.9, rockF) + 0.6 * scree) * (1.0 - plants) * fine * (1.0 - snowF) * 1.4;
    vec3 r1 = cross(dpy, n), r2 = cross(n, dpx);
    float det = dot(dpx, r1);
    vec3 grad = sign(det) * (dFdx(hb) * r1 + dFdy(hb) * r2) * bump;
    n = normalize(abs(det) * n - grad);
  }
  gTerrN = n;
  diffuseColor.rgb = col;
`;

/** The flat grid patch: (GRID+1)² vertices at (u, 0, v), u and v in 0..1, then a skirt (y = 1) under each edge. */
function patchGeometry() {
  const G = GRID, P = [], I = [];
  for (let j = 0; j <= G; j++) for (let i = 0; i <= G; i++) P.push(i / G, 0, j / G);
  const v = (i, j) => j * (G + 1) + i;
  for (let j = 0; j < G; j++) for (let i = 0; i < G; i++) {
    const a = v(i, j), b = v(i + 1, j), c = v(i, j + 1), d = v(i + 1, j + 1);
    I.push(a, c, b, b, c, d);
  }
  // skirts: both windings, so they hide a crack seen from either side
  const edge = (list) => {
    const base = P.length / 3;
    for (const k of list) P.push(P[k * 3], 1, P[k * 3 + 2]);
    for (let m = 0; m < list.length - 1; m++) {
      const a = list[m], b = list[m + 1], c = base + m, d = base + m + 1;
      I.push(a, b, c, b, d, c, a, c, b, b, c, d);
    }
  };
  const row = (j) => Array.from({ length: G + 1 }, (_, i) => v(i, j)), colm = (i) => Array.from({ length: G + 1 }, (_, j) => v(i, j));
  edge(row(0)); edge(row(G)); edge(colm(0)); edge(colm(G));
  const g = new THREE.InstancedBufferGeometry();
  g.setAttribute('position', new THREE.BufferAttribute(new Float32Array(P), 3));
  g.setIndex(I);
  return g;
}

function dataTex(data, w, h, format, type, linear, mips) {
  const t = new THREE.DataTexture(data, w, h, format, type);
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.magFilter = linear ? THREE.LinearFilter : THREE.NearestFilter;
  t.minFilter = linear ? (mips ? THREE.LinearMipmapLinearFilter : THREE.LinearFilter) : THREE.NearestFilter;
  t.generateMipmaps = !!mips;
  t.needsUpdate = true;
  return t;
}

export class Ground {
  /**
   * d: { W, fw, S, H, diag, shore, rock, halo, nx, nz (per cell), CR, carve (Float32Array, (fw CR)²), chan, feat
   * (Uint8Array RGBA, (fw CR)²) }. patch(material, key): the caller's shader additions (the wrap display).
   */
  constructor(d, patch) {
    this.d = d;
    const { fw, CR } = d, n = fw * fw, K = fw * CR;
    const h4 = new Float32Array(n * 4), nrm = new Uint8Array(n * 4);
    for (let i = 0; i < n; i++) {
      h4[i * 4] = d.H[i]; h4[i * 4 + 1] = d.diag[i]; h4[i * 4 + 2] = d.shore[i]; h4[i * 4 + 3] = d.rock[i];
      nrm[i * 4] = Math.round((d.nx[i] * 0.5 + 0.5) * 255); nrm[i * 4 + 1] = Math.round((d.nz[i] * 0.5 + 0.5) * 255);
      nrm[i * 4 + 2] = Math.round(d.rock[i] * 255); nrm[i * 4 + 3] = Math.round(d.halo[i] * 255);
    }
    this.cover = new Uint8Array(n * 4); this.mat = new Uint8Array(n * 4); this.mat2 = new Uint8Array(n * 4);
    const T = this.tex = {
      tH: dataTex(h4, fw, fw, THREE.RGBAFormat, THREE.FloatType, false),
      tCarve: dataTex(d.carve, K, K, THREE.RedFormat, THREE.FloatType, false),
      tNrm: dataTex(nrm, fw, fw, THREE.RGBAFormat, THREE.UnsignedByteType, true, true),
      tCover: dataTex(this.cover, fw, fw, THREE.RGBAFormat, THREE.UnsignedByteType, true, true),
      tMat: dataTex(this.mat, fw, fw, THREE.RGBAFormat, THREE.UnsignedByteType, true, true),
      tMat2: dataTex(this.mat2, fw, fw, THREE.RGBAFormat, THREE.UnsignedByteType, true, true),
      tChan: dataTex(d.chan, K, K, THREE.RGBAFormat, THREE.UnsignedByteType, true, true),
      tFeat: dataTex(d.feat, K, K, THREE.RGBAFormat, THREE.UnsignedByteType, true, true),
    };
    const U = this.uniforms = {
      tH: { value: T.tH }, tCarve: { value: T.tCarve }, tNoise: { value: noiseTexture() }, tNrm: { value: T.tNrm },
      tCover: { value: T.tCover }, tMat: { value: T.tMat }, tMat2: { value: T.tMat2 }, tChan: { value: T.tChan }, tFeat: { value: T.tFeat },
      uFw: { value: fw }, uS: { value: d.S }, uCR: { value: CR }, uSplit: { value: SPLIT }, uGrid: { value: GRID },
      uDisp: { value: DISP_MAX }, uDispNear: { value: DISP_NEAR }, uDispFar: { value: DISP_FAR }, uRidgeMean: { value: RIDGE_MEAN },
      uCam: { value: new THREE.Vector3() }, uPlain: { value: 0 },
    };
    const m = this.material = new THREE.MeshLambertMaterial({ color: 0xffffff });
    m.onBeforeCompile = (sh) => {
      Object.assign(sh.uniforms, U);
      sh.vertexShader = VERT_DECL + sh.vertexShader
        .replace('#include <beginnormal_vertex>', 'vec3 objectNormal = vec3(0.0, 1.0, 0.0);')
        .replace('#include <begin_vertex>', VERT_MAIN);
      sh.fragmentShader = FRAG_DECL + sh.fragmentShader
        .replace('#include <color_fragment>', FRAG_MAIN)
        .replace('#include <normal_fragment_maps>', 'normal = normalize((viewMatrix * vec4(gTerrN, 0.0)).xyz);');
    };
    m.customProgramCacheKey = () => 'nr-ground-1';
    if (patch) patch(m, 'nr-ground-1');
    // the same surface for shadow maps (mesh.customDepthMaterial): its vertices are placed here too
    const dm = this.depthMaterial = new THREE.MeshDepthMaterial({ depthPacking: THREE.RGBADepthPacking });
    dm.onBeforeCompile = (sh) => {
      Object.assign(sh.uniforms, U);
      sh.vertexShader = VERT_DECL + sh.vertexShader.replace('#include <begin_vertex>', VERT_MAIN);
    };
    dm.customProgramCacheKey = () => 'nr-ground-depth-1';
    this.geo = patchGeometry();
    this.cap = 0; this._grow(512);
    this.mesh = new THREE.Mesh(this.geo, m);
    this.mesh.frustumCulled = false;
    this._pyramid();
    this.nodes = 0;
  }

  _grow(cap) {
    this.cap = cap; this.inst = new Float32Array(cap * 3);
    const a = new THREE.InstancedBufferAttribute(this.inst, 3); a.setUsage(THREE.DynamicDrawUsage);
    this.geo.setAttribute('iNode', a);
  }

  /** Height range of each quadtree node (in map coordinates), widened for the crags and the carving. */
  _pyramid() {
    const { W, fw, H } = this.d;
    let L = 0; while (W / (1 << L) > LEAF) L++;
    this.levels = L + 1;
    const P = this.pyr = [];
    { // leaves: scan their cells (and the next row and column, which their last quads reach)
      const n = 1 << L, c = fw / n, a = new Float32Array(n * n * 2);
      for (let iz = 0; iz < n; iz++) for (let ix = 0; ix < n; ix++) {
        let lo = Infinity, hi = -Infinity;
        for (let y = Math.floor(iz * c); y <= Math.ceil((iz + 1) * c); y++) for (let x = Math.floor(ix * c); x <= Math.ceil((ix + 1) * c); x++) {
          const h = H[(y % fw) * fw + (x % fw)]; if (h < lo) lo = h; if (h > hi) hi = h;
        }
        a[(iz * n + ix) * 2] = lo - 3; a[(iz * n + ix) * 2 + 1] = hi + DISP_MAX + 0.5;
      }
      P[L] = a;
    }
    for (let l = L - 1; l >= 0; l--) {
      const n = 1 << l, c = P[l + 1], a = new Float32Array(n * n * 2);
      for (let iz = 0; iz < n; iz++) for (let ix = 0; ix < n; ix++) {
        let lo = Infinity, hi = -Infinity;
        for (let k = 0; k < 4; k++) { const j = ((2 * iz + (k >> 1)) * 2 * n + 2 * ix + (k & 1)) * 2; lo = Math.min(lo, c[j]); hi = Math.max(hi, c[j + 1]); }
        a[(iz * n + ix) * 2] = lo; a[(iz * n + ix) * 2 + 1] = hi;
      }
      P[l] = a;
    }
  }

  /** Choose the nodes for this frame: the 3x3 map copies around tile (tx, tz), within `far` of the camera and in view. */
  update(camera, frustum, tx, tz, far) {
    const W = this.d.W, cam = camera.position, box = this._box ||= new THREE.Box3();
    this.uniforms.uCam.value.copy(cam);
    let k = 0;
    const emit = (x, z, s) => {
      if (k >= this.cap) { const old = this.inst; this._grow(this.cap * 2); this.inst.set(old); }
      this.inst[k * 3] = x; this.inst[k * 3 + 1] = z; this.inst[k * 3 + 2] = s; k++;
    };
    const visit = (x0, z0, s, l, ix, iz) => {
      const n = 1 << l, j = (iz * n + ix) * 2, lo = this.pyr[l][j], hi = this.pyr[l][j + 1];
      const dx = Math.max(x0 - cam.x, 0, cam.x - x0 - s), dz = Math.max(z0 - cam.z, 0, cam.z - z0 - s), dy = Math.max(lo - cam.y, 0, cam.y - hi);
      const dist = Math.hypot(dx, dy, dz);
      if (dist > far) return;
      box.min.set(x0, lo, z0); box.max.set(x0 + s, hi, z0 + s);
      if (!frustum.intersectsBox(box)) return;
      if (l + 1 < this.levels && dist < SPLIT * s) {
        const h = s / 2;
        visit(x0, z0, h, l + 1, 2 * ix, 2 * iz); visit(x0 + h, z0, h, l + 1, 2 * ix + 1, 2 * iz);
        visit(x0, z0 + h, h, l + 1, 2 * ix, 2 * iz + 1); visit(x0 + h, z0 + h, h, l + 1, 2 * ix + 1, 2 * iz + 1);
      } else emit(x0, z0, s);
    };
    for (let dz = -1; dz <= 1; dz++) for (let dx = -1; dx <= 1; dx++) visit((tx + dx) * W, (tz + dz) * W, W, 0, 0, 0);
    this.geo.instanceCount = k;
    this.geo.attributes.iNode.needsUpdate = true;
    this.geo.attributes.iNode.addUpdateRange?.(0, k * 3);
    this.nodes = k;
    return k;
  }

  /** Triangles drawn this frame (for the budget). */
  triangles() { return this.nodes * (GRID * GRID * 2 + 4 * GRID * 4); }

  /** The cover colours (rgb) and plant density (a), RGBA bytes per cell. */
  coverUpdated() { this.tex.tCover.needsUpdate = true; }
  /** Snow cover (r), volcanic (g) and sedimentary (b) shares, alluvium (a), RGBA bytes per cell. */
  matUpdated() { this.tex.tMat.needsUpdate = true; this.tex.tMat2.needsUpdate = true; }

  /** The drawn ground's height at a world position (x, z), the camera at cam: as the vertex shader computes it. */
  heightAt(x, z, cam) {
    const { fw, S, H, diag, rock, carve, CR } = this.d;
    const gx = x / S, gy = z / S, fx = Math.floor(gx), fy = Math.floor(gy), tx = gx - fx, ty = gy - fy;
    const x0 = ((fx % fw) + fw) % fw, y0 = ((fy % fw) + fw) % fw, x1 = (x0 + 1) % fw, y1 = (y0 + 1) % fw;
    const ia = y0 * fw + x0, ib = y0 * fw + x1, ic = y1 * fw + x0, id = y1 * fw + x1;
    const a = H[ia], b = H[ib], c = H[ic], d = H[id];
    let h;
    if (diag[ia]) h = ty >= tx ? a + (d - c) * tx + (c - a) * ty : a + (b - a) * tx + (d - b) * ty;
    else h = tx + ty <= 1 ? a + (b - a) * tx + (c - a) * ty : d + (c - d) * (1 - tx) + (b - d) * (1 - ty);
    const r = (rock[ia] + (rock[ib] - rock[ia]) * tx) * (1 - ty) + (rock[ic] + (rock[id] - rock[ic]) * tx) * ty;
    if (r > 0 && cam) {
      const dist = Math.hypot(cam.x - x, cam.y - h, cam.z - z);
      const t = Math.min(1, Math.max(0, (dist - DISP_NEAR) / (DISP_FAR - DISP_NEAR))), fade = 1 - t * t * (3 - 2 * t);
      if (fade > 0) h += DISP_MAX * r * fade * (ridged(x, z) - RIDGE_MEAN) * 2;
    }
    // the carve cap, bilinear between its texels (as carveCap)
    const N = fw * CR, kx = x / S * CR, kz = z / S * CR, cx = Math.floor(kx), cz = Math.floor(kz), ux = kx - cx, uz = kz - cz;
    const X0 = ((cx % N) + N) % N, Z0 = ((cz % N) + N) % N, X1 = (X0 + 1) % N, Z1 = (Z0 + 1) % N;
    const A = carve[Z0 * N + X0], B = carve[Z0 * N + X1], C = carve[Z1 * N + X0], D = carve[Z1 * N + X1];
    if (A < NONE || B < NONE || C < NONE || D < NONE) {
      const cap = (A + (B - A) * ux) * (1 - uz) + (C + (D - C) * ux) * uz;
      if (cap < h) h = cap;
    }
    return h;
  }

  dispose() {
    this.geo.dispose(); this.material.dispose(); this.depthMaterial.dispose();
    for (const t of Object.values(this.tex)) t.dispose();
  }
}

export const CARVE_NONE = NONE;
