// Rain and snow (display only, 2026-11-01): falling streaks round the camera while it rains, from the status message.
//
// The sim has no weather cells: it has a season (-1 dry .. 1 wet) and this year's rain anomaly (status.season, .rain).
// lighting.js turns them into a wetness (0 dry .. 1 the wettest of wet seasons) that sets the cloud cover; here the
// wetter part of that range rains. It falls as snow where the air at the view centre is below freezing (FIELDS 'temp').
//
// The streaks live in a box round the camera, as big as the view (a few metres close up, a few hundred metres from
// afar), fixed in the world (they do not slide with the camera) and faded towards the box's edge, so the box never shows.
// The box is in world units; true raindrops (a few mm, falling ~8 m/s) are far below a pixel at most zooms, so the
// streaks are a veil of the right density and motion, not drops at true size. One draw call; nothing above a few km up.

import * as THREE from 'three';

const N = 4000;

const VERT = /* glsl */`
attribute vec3 aSeed;
attribute float aEnd;
uniform vec3 uCam, uWind;
uniform float uBox, uTime, uSpeed, uLen, uSnow;
varying float vA;
void main() {
  vec3 B = vec3(uBox);
  vec3 fall = vec3(uWind.x, -1.0, uWind.z) * uSpeed * uTime;
  // flakes drift and wobble as they fall
  vec3 wob = uSnow * uBox * 0.02 * vec3(sin(uTime * 1.7 + aSeed.x * 40.0), 0.0, cos(uTime * 1.3 + aSeed.z * 40.0));
  vec3 q = mod(aSeed * B + fall - uCam + 0.5 * B, B) - 0.5 * B;
  vec3 dir = normalize(vec3(uWind.x, -1.0, uWind.z));
  vec3 p = uCam + q + wob - dir * uLen * aEnd;
  float r = length(q) / (0.5 * uBox);
  vA = (1.0 - smoothstep(0.55, 1.0, r)) * smoothstep(0.02, 0.12, r) * (0.6 + 0.4 * aSeed.y);
  gl_Position = projectionMatrix * viewMatrix * vec4(p, 1.0);
}`;

const FRAG = /* glsl */`
uniform vec3 uColor;
uniform float uAlpha;
varying float vA;
void main() { gl_FragColor = vec4(uColor, uAlpha * vA); }`;

export class Weather {
  constructor(scene) {
    const seed = new Float32Array(N * 2 * 3), end = new Float32Array(N * 2), pos = new Float32Array(N * 2 * 3);
    let s = 12345;
    const rnd = () => { s = (Math.imul(s, 1664525) + 1013904223) >>> 0; return s / 4294967296; };
    for (let i = 0; i < N; i++) {
      const a = rnd(), b = rnd(), c = rnd();
      for (let k = 0; k < 2; k++) { seed.set([a, b, c], (i * 2 + k) * 3); end[i * 2 + k] = k; }
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    g.setAttribute('aSeed', new THREE.BufferAttribute(seed, 3));
    g.setAttribute('aEnd', new THREE.BufferAttribute(end, 1));
    this.u = {
      uCam: { value: new THREE.Vector3() }, uWind: { value: new THREE.Vector3(0.18, 0, 0.08) }, uBox: { value: 10 }, uTime: { value: 0 },
      uSpeed: { value: 1 }, uLen: { value: 0.3 }, uSnow: { value: 0 }, uColor: { value: new THREE.Color(0.75, 0.78, 0.82) }, uAlpha: { value: 0 },
    };
    const m = new THREE.ShaderMaterial({ uniforms: this.u, vertexShader: VERT, fragmentShader: FRAG, transparent: true, depthWrite: false, fog: false });
    this.mesh = new THREE.LineSegments(g, m);
    this.mesh.frustumCulled = false; this.mesh.renderOrder = 20; this.mesh.visible = false;
    scene.add(this.mesh);
    this.rain = 0; this.snow = 0;
  }

  /**
   * intensity 0..1 (lighting.js: the wet part of the wetness), tempC the air at the view centre (null: unknown),
   * camD the camera's distance to the view centre, alt its height above the ground (units).
   */
  update(camera, intensity, tempC, camD, alt, time) {
    const u = this.u;
    this.rain = intensity;
    this.snow = tempC !== null && tempC !== undefined ? Math.min(1, Math.max(0, (1 - tempC) / 2)) : 0;   // sleet at 0-1 °C, snow below
    // nothing from above the clouds (a cloud base ~2-3 km: ~45 units)
    const show = intensity > 0.01 && alt < 45;
    this.mesh.visible = show;
    if (!show) return;
    const box = Math.min(40, Math.max(0.25, camD * 1.3));
    u.uCam.value.copy(camera.position); u.uBox.value = box; u.uTime.value = time;
    const sn = this.snow;
    u.uSpeed.value = box * (0.9 - 0.75 * sn);           // a box height a second for rain, a sixth of that for snow
    u.uLen.value = box * (0.022 - 0.018 * sn);
    u.uSnow.value = sn;
    u.uColor.value.setRGB(0.42 + 0.5 * sn, 0.45 + 0.48 * sn, 0.50 + 0.45 * sn);
    u.uAlpha.value = Math.min(1, intensity) * (0.14 + 0.5 * sn) * (1 - Math.min(1, Math.max(0, (alt - 30) / 15)));
    this.mesh.geometry.setDrawRange(0, 2 * Math.round(N * (0.25 + 0.75 * Math.min(1, intensity))));
  }
}
