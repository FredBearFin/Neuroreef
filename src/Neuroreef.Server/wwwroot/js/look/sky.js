// The sky and the air (display only): an analytic sky dome (a gradient from the zenith to a pale horizon, the sun's glow
// and disc, and clouds as many as the weather gives), and aerial perspective: the haze that turns far ground blue and
// at the fog's far end matches the sky behind it, so the edge of the drawn land never shows.
//
// The same colour model runs here on the CPU (for the fog colour, the background and the lights) and in the dome's
// shader, so ground and sky meet without a seam. All colours are linear (the frame is tone mapped in post.js).

import * as THREE from 'three';

const ZENITH_CLEAR = new THREE.Color(0.075, 0.24, 0.62), ZENITH_GREY = new THREE.Color(0.30, 0.34, 0.40);
const HORIZON_CLEAR = new THREE.Color(0.44, 0.62, 0.88), HORIZON_GREY = new THREE.Color(0.46, 0.50, 0.55);
const GLOW = new THREE.Color(1.0, 0.78, 0.52);

/**
 * Aerial perspective in every material that has fog: three.js's linear fog is replaced by an exponential haze whose
 * e-folding length is fog.near (look/look.js sets it each frame from the view), which reaches the fog colour at fog.far
 * (the distance world3d.js draws the ground and plants to). Near and middle distances take a deeper blue than the sky's
 * horizon (the air in front of dark ground scatters blue), the far end the fog colour itself.
 * Installed once, before any material compiles.
 */
export function installAerialFog() {
  if (THREE.ShaderChunk.fog_fragment.includes('aerial perspective')) return;
  THREE.ShaderChunk.fog_fragment = `
#ifdef USE_FOG
  // aerial perspective (look/sky.js)
  #ifdef FOG_EXP2
    float fogFactor = 1.0 - exp( - fogDensity * fogDensity * vFogDepth * vFogDepth );
    gl_FragColor.rgb = mix( gl_FragColor.rgb, fogColor, fogFactor );
  #else
    float aHaze = 1.0 - exp( - vFogDepth / max( fogNear, 1.0 ) );
    float aEdge = smoothstep( 0.55, 1.0, vFogDepth / fogFar );
    vec3 aCol = mix( fogColor * vec3( 0.50, 0.66, 1.0 ), fogColor, max( aEdge, aHaze * aHaze * aHaze ) );
    gl_FragColor.rgb = mix( gl_FragColor.rgb, aCol, max( aHaze * 0.75, aEdge ) );
  #endif
#endif
`;
}

/** The sky's colour model, shared by the CPU and the dome's shader. */
export class Atmosphere {
  constructor() {
    this.sun = new THREE.Vector3(0.5, 0.6, 0.3).normalize();
    this.cloud = 0.25;              // cloud cover 0..1
    this.zenith = new THREE.Color(); this.horizon = new THREE.Color(); this.sunColor = new THREE.Color();
    this.update();
  }

  update() {
    const c = this.cloud, el = Math.max(0, this.sun.y);
    this.zenith.copy(ZENITH_CLEAR).lerp(ZENITH_GREY, c);
    this.horizon.copy(HORIZON_CLEAR).lerp(HORIZON_GREY, c);
    // the sun's light reddens as it sinks (a longer path through the air)
    const warm = Math.min(1, Math.max(0, 1 - el / 0.6));
    this.sunColor.setRGB(1.0, 0.93 - 0.18 * warm, 0.84 - 0.36 * warm);
  }

  /** Sky radiance at the horizon in a horizontal direction (dx, dz): the fog colour when looking that way. */
  horizonAt(dx, dz, out) {
    const l = Math.hypot(dx, dz) || 1, mu = Math.max(0, (dx * this.sun.x + dz * this.sun.z) / l);
    const g = (Math.pow(mu, 6) * 0.30 + Math.pow(mu, 40) * 0.25) * (1 - 0.75 * this.cloud);
    out.copy(this.horizon);
    out.r += GLOW.r * g; out.g += GLOW.g * g; out.b += GLOW.b * g;
    return out;
  }
}

const SKY_VERT = `
varying vec3 vDir;
void main() {
  vDir = position;
  vec4 p = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
  gl_Position = p.xyww;   // on the far plane
}`;

const SKY_FRAG = `
uniform vec3 uSun, uZenith, uHorizon, uGlow, uSunCol, uFog;
uniform float uCloud, uTime;
varying vec3 vDir;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
  vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
  return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}
float fbm(vec2 p) {
  float v = 0.0, a = 0.5;
  for (int i = 0; i < 5; i++) { v += a * noise(p); p = p * 2.03 + vec2(17.1, 3.7); a *= 0.5; }
  return v;
}
void main() {
  vec3 d = normalize(vDir);
  float h = max(d.y, 0.0);
  vec3 col = mix(uZenith, uHorizon, pow(1.0 - h, 4.0));
  float mu = max(dot(d, uSun), 0.0);
  float clear = 1.0 - 0.75 * uCloud;
  // Mie glow round the sun, wide and narrow, stronger near the horizon
  col += uGlow * (pow(mu, 6.0) * 0.30 + pow(mu, 40.0) * 0.25) * clear * (0.5 + 0.5 * pow(1.0 - h, 2.0));
  col += uGlow * pow(mu, 600.0) * 3.0 * clear;
  // the disc (bright enough to bloom)
  col += uSunCol * smoothstep(0.99955, 0.99975, mu) * 40.0 * (1.0 - 0.9 * uCloud);
  // clouds: a layer of fbm seen through the dome, as much of it as the weather gives, thinning to the horizon
  if (d.y > 0.0) {
    vec2 p = d.xz / (d.y + 0.12) * 1.6 + vec2(uTime * 0.006, uTime * 0.0025);
    float n = fbm(p);
    float cover = smoothstep(0.62 - 0.38 * uCloud, 0.95 - 0.25 * uCloud, n);
    cover *= smoothstep(0.0, 0.22, d.y);
    float n2 = fbm(p + uSun.xz * 0.06);   // towards the sun: thinner there, so lit from behind
    float lit = clamp(0.55 + (n - n2) * 4.0, 0.25, 1.15);
    vec3 cc = mix(vec3(0.58, 0.62, 0.68), vec3(1.05, 1.02, 0.98) * uSunCol, lit) * mix(1.2, 0.75, uCloud);
    cc += uGlow * pow(mu, 8.0) * 0.6 * clear;
    col = mix(col, cc, cover * 0.92);
  }
  // below and at the horizon: the fog colour, so the haze on the ground meets the sky without a seam
  col = mix(col, uFog, smoothstep(0.06, -0.01, d.y));
  gl_FragColor = vec4(col, 1.0);
}`;

/** The dome: drawn first, behind everything, centred on the camera. */
export class SkyDome {
  constructor(scene, atm) {
    this.atm = atm;
    this.u = {
      uSun: { value: atm.sun }, uZenith: { value: atm.zenith }, uHorizon: { value: atm.horizon }, uGlow: { value: GLOW },
      uSunCol: { value: atm.sunColor }, uFog: { value: new THREE.Color() }, uCloud: { value: 0 }, uTime: { value: 0 },
    };
    const m = new THREE.ShaderMaterial({ uniforms: this.u, vertexShader: SKY_VERT, fragmentShader: SKY_FRAG, side: THREE.BackSide,
                                         depthWrite: false, depthTest: false, fog: false });
    this.mesh = new THREE.Mesh(new THREE.SphereGeometry(1, 48, 24), m);
    this.mesh.frustumCulled = false; this.mesh.renderOrder = -1000;
    scene.add(this.mesh);
  }

  update(camera, fogColor, time) {
    this.mesh.position.copy(camera.position);
    this.mesh.scale.setScalar(camera.far * 0.9);
    this.u.uFog.value.copy(fogColor); this.u.uCloud.value = this.atm.cloud; this.u.uTime.value = time;
  }
}
