// The finish (display only): the scene is drawn in linear light to a half-float target (multisampled), then a small
// bloom (bright pass at a quarter and an eighth of the size, blurred) for the sun, its glints on water and fire, and
// one full-screen pass that tone maps (ACES), grades (a little saturation, cool shadows and warm lights, a soft
// vignette) and encodes sRGB. About five small passes and one full-size one: a millisecond or two on a mid-range GPU.
// Without WebGL2 or float targets the scene is drawn straight to the screen with three.js's ACES tone mapping.

import * as THREE from 'three';

const VERT = `varying vec2 vUv; void main() { vUv = uv; gl_Position = vec4(position.xy, 0.0, 1.0); }`;

const BRIGHT = `
uniform sampler2D tSrc; uniform vec2 uTexel; uniform float uThreshold; varying vec2 vUv;
void main() {
  // four bilinear taps: a 4x4 box of the source
  vec3 c = texture2D(tSrc, vUv + uTexel * vec2(-1.0, -1.0)).rgb + texture2D(tSrc, vUv + uTexel * vec2(1.0, -1.0)).rgb
         + texture2D(tSrc, vUv + uTexel * vec2(-1.0, 1.0)).rgb + texture2D(tSrc, vUv + uTexel * vec2(1.0, 1.0)).rgb;
  c *= 0.25;
  c = min(c, vec3(64.0));
  float l = dot(c, vec3(0.2126, 0.7152, 0.0722));
  gl_FragColor = vec4(c * smoothstep(uThreshold, uThreshold * 2.0, l), 1.0);
}`;

const BLUR = `
uniform sampler2D tSrc; uniform vec2 uDir; varying vec2 vUv;
void main() {
  vec3 c = texture2D(tSrc, vUv).rgb * 0.2270;
  c += (texture2D(tSrc, vUv + uDir * 1.3846).rgb + texture2D(tSrc, vUv - uDir * 1.3846).rgb) * 0.3162;
  c += (texture2D(tSrc, vUv + uDir * 3.2308).rgb + texture2D(tSrc, vUv - uDir * 3.2308).rgb) * 0.0703;
  gl_FragColor = vec4(c, 1.0);
}`;

const FINAL = `
uniform sampler2D tScene, tBloomA, tBloomB;
uniform float uExposure, uBloom, uSat, uVignette, uBloomOn;
varying vec2 vUv;
vec3 RRTAndODTFit(vec3 v) { vec3 a = v * (v + 0.0245786) - 0.000090537; vec3 b = v * (0.983729 * v + 0.4329510) + 0.238081; return a / b; }
vec3 aces(vec3 c) {
  const mat3 IN = mat3(vec3(0.59719, 0.07600, 0.02840), vec3(0.35458, 0.90834, 0.13383), vec3(0.04823, 0.01566, 0.83777));
  const mat3 OUT = mat3(vec3(1.60475, -0.10208, -0.00327), vec3(-0.53108, 1.10813, -0.07276), vec3(-0.07367, -0.00605, 1.07602));
  c = OUT * RRTAndODTFit(IN * (c / 0.6));
  return clamp(c, 0.0, 1.0);
}
vec3 toSRGB(vec3 c) { return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), c)); }
float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }
void main() {
  vec3 c = texture2D(tScene, vUv).rgb;
  if (uBloomOn > 0.5) c += (texture2D(tBloomA, vUv).rgb * 0.6 + texture2D(tBloomB, vUv).rgb * 0.4) * uBloom;
  c = aces(c * uExposure);
  float l = dot(c, vec3(0.2126, 0.7152, 0.0722));
  c = max(mix(vec3(l), c, uSat), 0.0);
  c *= mix(vec3(0.98, 0.99, 1.02), vec3(1.04, 1.0, 0.95), smoothstep(0.05, 0.6, l));   // cool shadows, warm lights
  vec2 q = vUv - 0.5;
  c *= 1.0 - uVignette * dot(q, q) * 1.5;
  c = toSRGB(clamp(c, 0.0, 1.0));
  c += (hash(gl_FragCoord.xy) - 0.5) / 255.0;   // dither: no bands in the sky
  gl_FragColor = vec4(c, 1.0);
}`;

export class Post {
  constructor(renderer, quality) {
    this.renderer = renderer;
    const gl = renderer.getContext();
    renderer.info.autoReset = false;   // counted over the whole frame (scene and passes): reset in render()
    this.ok = quality.post !== false && renderer.capabilities.isWebGL2 && !!gl.getExtension('EXT_color_buffer_float') || renderer.capabilities.isWebGL2 && !!gl.getExtension('EXT_color_buffer_half_float');
    this.bloom = quality.bloom;
    this.samples = quality.msaa;
    this.exposure = 0.92;
    if (!this.ok) {
      renderer.toneMapping = THREE.ACESFilmicToneMapping; renderer.toneMappingExposure = this.exposure;
      return;
    }
    renderer.toneMapping = THREE.NoToneMapping;
    const quad = (frag, u) => { const m = new THREE.ShaderMaterial({ uniforms: u, vertexShader: VERT, fragmentShader: frag, depthTest: false, depthWrite: false }); return m; };
    this.mBright = quad(BRIGHT, { tSrc: { value: null }, uTexel: { value: new THREE.Vector2() }, uThreshold: { value: 1.6 } });
    this.mBlur = quad(BLUR, { tSrc: { value: null }, uDir: { value: new THREE.Vector2() } });
    this.mFinal = quad(FINAL, { tScene: { value: null }, tBloomA: { value: null }, tBloomB: { value: null }, uExposure: { value: 1 },
                                uBloom: { value: 0.35 }, uSat: { value: 1.15 }, uVignette: { value: 0.55 }, uBloomOn: { value: 1 } });
    this.quad = new THREE.Mesh(new THREE.PlaneGeometry(2, 2), this.mFinal); this.quad.frustumCulled = false;
    this.qScene = new THREE.Scene(); this.qScene.add(this.quad);
    this.qCam = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1);
    this.size = new THREE.Vector2();
    this.w = 0; this.h = 0;
  }

  _alloc(w, h) {
    for (const t of [this.rt, this.a1, this.a2, this.b1, this.b2]) if (t) t.dispose();
    const opt = { type: THREE.HalfFloatType, depthBuffer: false };
    this.rt = new THREE.WebGLRenderTarget(w, h, { type: THREE.HalfFloatType, samples: this.samples });
    const w4 = Math.max(1, w >> 2), h4 = Math.max(1, h >> 2), w8 = Math.max(1, w >> 3), h8 = Math.max(1, h >> 3);
    this.a1 = new THREE.WebGLRenderTarget(w4, h4, opt); this.a2 = new THREE.WebGLRenderTarget(w4, h4, opt);
    this.b1 = new THREE.WebGLRenderTarget(w8, h8, opt); this.b2 = new THREE.WebGLRenderTarget(w8, h8, opt);
    this.w = w; this.h = h;
  }

  _pass(mat, target) { this.quad.material = mat; this.renderer.setRenderTarget(target); this.renderer.render(this.qScene, this.qCam); }

  render(scene, camera) {
    const r = this.renderer;
    r.info.reset();
    if (!this.ok) { r.toneMappingExposure = this.exposure; r.render(scene, camera); return; }
    r.getDrawingBufferSize(this.size);
    if (this.size.x !== this.w || this.size.y !== this.h) this._alloc(this.size.x, this.size.y);
    r.setRenderTarget(this.rt);
    r.render(scene, camera);
    if (this.bloom) {
      const B = this.mBright.uniforms, L = this.mBlur.uniforms;
      // the scene to a quarter (a 4x4 box), its bright part; blurred; again at an eighth
      B.tSrc.value = this.rt.texture; B.uTexel.value.set(1 / this.w, 1 / this.h); this._pass(this.mBright, this.a1);
      L.tSrc.value = this.a1.texture; L.uDir.value.set(1 / this.a1.width, 0); this._pass(this.mBlur, this.a2);
      L.tSrc.value = this.a2.texture; L.uDir.value.set(0, 1 / this.a1.height); this._pass(this.mBlur, this.a1);
      L.tSrc.value = this.a1.texture; L.uDir.value.set(2 / this.a1.width, 0); this._pass(this.mBlur, this.b1);
      L.tSrc.value = this.b1.texture; L.uDir.value.set(0, 1 / this.b1.height); this._pass(this.mBlur, this.b2);
    }
    const F = this.mFinal.uniforms;
    F.tScene.value = this.rt.texture; F.tBloomA.value = this.a1.texture; F.tBloomB.value = this.b2.texture;
    F.uExposure.value = this.exposure; F.uBloomOn.value = this.bloom ? 1 : 0;
    this._pass(this.mFinal, null);
  }
}
