// Light (display only): the sun, a hemisphere light from the sky above and the ground below, and soft shadows from
// the sun in one shadow map that follows the view (its size follows the zoom, so close up it is sharp and from afar the
// mountains still cast theirs). Terrain, trees, bushes, rocks and the near animals cast; everything lit receives.
//
// The sim has seasons (wet and dry) but no days, so the sun stands at a fixed, low-afternoon angle that drifts a little
// with the season: higher and harsher in the dry season, lower and softer in the wet.

import * as THREE from 'three';

const WHITE = new THREE.Color(1, 1, 1);

export class Lighting {
  constructor(scene, renderer, atm, quality) {
    this.atm = atm;
    this.hemi = new THREE.HemisphereLight(0xbfd8ff, 0x3b3424, 1.0);
    this.sun = new THREE.DirectionalLight(0xffffff, 3);
    scene.add(this.hemi, this.sun, this.sun.target);
    this.shadows = quality.shadows;
    renderer.shadowMap.enabled = this.shadows;
    renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    const s = this.sun.shadow;
    s.mapSize.set(quality.shadowMap, quality.shadowMap);
    this.sun.castShadow = this.shadows;
    s.bias = -0.0004;
    this.season = 0; this.rain = 0;
    this._c = new THREE.Vector3(); this._f = new THREE.Vector3();
  }

  /** The season (-1 dry .. 1 wet) and the year's rain anomaly from the status message, smoothed in update(). */
  setClimate(season, rain) { this.season = season || 0; this.rain = rain || 0; }

  update(camera, target, mode, dt) {
    const atm = this.atm;
    // sun: azimuth fixed (from the south-west, behind and to the left of the start view), elevation by season
    const k = 1 - Math.exp(-dt / 4000);
    this._s = this._s === undefined ? this.season : this._s + (this.season - this._s) * k;
    this._r = this._r === undefined ? this.rain : this._r + (this.rain - this._r) * k;
    const el = (34 - 6 * this._s) * Math.PI / 180, az = 2.35;
    atm.sun.set(Math.cos(el) * Math.cos(az), Math.sin(el), Math.cos(el) * Math.sin(az)).normalize();
    // clouds: few in the dry season, overcast in the wet, more in a wet year and fewer in a drought (2026-11-01 QA: the
    // wet season's sky was as clear as the dry one's, 0.16-0.33). The wetter part rains (weather.js).
    this.wet = Math.min(1, Math.max(0, (this._s + 0.6 * this._r + 0.35) / 1.25));
    this.rainI = Math.min(1, Math.max(0, (this.wet - 0.62) / 0.3));
    atm.cloud = Math.min(0.85, Math.max(0.05, 0.08 + 0.72 * this.wet * this.wet));
    atm.update();
    const clear = 1 - 0.7 * atm.cloud;
    this.sun.color.copy(atm.sunColor);
    this.sun.intensity = 3.1 * clear;
    // sky light: the sky's colour, but only a little of its blue (the ground also lights the air), and bounce from the ground
    this.hemi.color.copy(atm.zenith).lerp(atm.horizon, 0.6).multiplyScalar(1.0 / Math.max(0.3, atm.horizon.g)).lerp(WHITE, 0.45);
    this.hemi.groundColor.setRGB(0.30, 0.25, 0.17);
    this.hemi.intensity = 0.8 + 0.6 * atm.cloud;
    if (!this.shadows) { this.sun.position.copy(target).addScaledVector(atm.sun, 100); this.sun.target.position.copy(target); return; }

    // the shadow map: a square round the part of the ground in view, its size from the zoom
    const camD = camera.position.distanceTo(target);
    let R, c = this._c;
    // (close up, as small as the view: the bodies and plants are drawn at true size or near it, a few metres to tens)
    if (mode === 'overview') { R = Math.min(700, Math.max(0.6, camD * 1.05)); c.copy(target); }
    else {
      R = mode === 'explore' ? 12 : Math.max(1, Math.min(60, camD * 3));
      camera.getWorldDirection(this._f); this._f.y = 0; this._f.normalize();
      c.copy(camera.position).addScaledVector(this._f, R * 0.55);
    }
    const s = this.sun.shadow, cam = s.camera, map = s.mapSize.x;
    // snap the centre to the shadow map's texels (in the light's frame), so the shadows do not crawl as the view moves
    const texel = (2 * R) / map;
    const lz = atm.sun, lx = new THREE.Vector3(0, 1, 0).cross(lz).normalize(), ly = lz.clone().cross(lx);
    const u = Math.round(c.dot(lx) / texel) * texel, v = Math.round(c.dot(ly) / texel) * texel, w = c.dot(lz);
    c.copy(lx).multiplyScalar(u).addScaledVector(ly, v).addScaledVector(lz, w);
    const back = R + 160;   // far enough towards the sun to take in mountains beside the square
    this.sun.position.copy(c).addScaledVector(lz, back);
    this.sun.target.position.copy(c);
    cam.left = -R; cam.right = R; cam.top = R; cam.bottom = -R; cam.near = 1; cam.far = back + R + 120;
    cam.updateProjectionMatrix();
    s.normalBias = texel * 1.2;
    this.R = R;
  }
}

/**
 * The shadow of an animal: its depth drawn with the same vertex shader as its body (the body's shape and pose are
 * made in the shader from its genes, so the default depth material would draw the bare template). The fragment
 * part of the body's patch is left out.
 */
export function bodyDepthMaterial(bodyMat) {
  const dm = new THREE.MeshDepthMaterial({ depthPacking: THREE.RGBADepthPacking });
  dm.onBeforeCompile = (sh) => {
    // the depth shader computes the normal only under a displacement map; the body's patch needs it to hook into
    sh.vertexShader = sh.vertexShader.replace(/#include <beginnormal_vertex>/g, '')
      .replace('#include <begin_vertex>', '#include <beginnormal_vertex>\n#include <begin_vertex>');
    const frag = sh.fragmentShader;
    bodyMat.onBeforeCompile(sh);
    sh.fragmentShader = frag;
  };
  dm.customProgramCacheKey = () => 'neuroreef-body-depth-v2';
  return dm;
}
