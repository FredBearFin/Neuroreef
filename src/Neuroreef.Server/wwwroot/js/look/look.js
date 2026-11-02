// The look of the 3D view (display only; nothing here touches the simulation): sky and aerial perspective (sky.js),
// sun, sky light and shadows (lighting.js), plants and rocks (vegetation.js), and the finish (post.js).
// world3d.js holds the scene and calls in at a few marked hooks ([look]): construction, the terrain and animal meshes
// as they are made, the plant placement, the climate from the status message, and the frame's render.
//
// Quality: ?look=high|medium|low in the URL, else medium; and when frames stay slow (over 33 ms for a few seconds)
// it steps down once at a time (shadow map 2048 -> 1024 -> off, bloom off, no MSAA), never back up in a session.

import * as THREE from 'three';
import { Atmosphere, SkyDome, installAerialFog } from './sky.js';
import { Lighting, bodyDepthMaterial } from './lighting.js';
import { Vegetation } from './vegetation.js';
import { Post } from './post.js';
import { Weather } from './weather.js';

installAerialFog();   // before any material compiles

const QUALITY = {
  high: { shadows: true, shadowMap: 4096, bloom: true, msaa: 4, adapt: false },
  medium: { shadows: true, shadowMap: 2048, bloom: true, msaa: 4, adapt: true },
  low: { shadows: false, shadowMap: 1024, bloom: false, msaa: 0, adapt: true },
};

export class Look {
  /** v: the World3D; wrap(material): its wrap-display patch for every material that draws the ground's copies. */
  constructor(v, wrap) {
    this.v = v;
    const q = (location.search.match(/[?&]look=(high|medium|low)\b/) || [])[1] || 'medium';
    this.quality = { ...QUALITY[q], name: q };
    // single switches (for measuring what each costs): lookfixed (no stepping down), lookshadow=0, lookbloom=0, lookmsaa=0, lookpost=0
    const flag = (k) => (location.search.match(new RegExp('[?&]' + k + '=?(\\w*)')) || [])[1];
    if (flag('lookfixed') !== undefined) this.quality.adapt = false;
    if (flag('lookshadow') === '0') this.quality.shadows = false;
    if (flag('lookbloom') === '0') this.quality.bloom = false;
    if (flag('lookmsaa') === '0') this.quality.msaa = 0;
    if (flag('lookpost') === '0') this.quality.post = false;
    v.scene.background = null;
    this.atm = new Atmosphere();
    this.sky = new SkyDome(v.scene, this.atm);
    this.light = new Lighting(v.scene, v.renderer, this.atm, this.quality);
    this.veg = new Vegetation(v, wrap, this.quality);
    this.post = new Post(v.renderer, this.quality);
    this.weather = new Weather(v.scene);
    this.fogColor = new THREE.Color();
    this._fwd = new THREE.Vector3();
    this._t = this._born = performance.now(); this._slow = 0; this.frameMs = 16;
    this.bodyMeshes = [];
  }

  /** The status message: season (-1 dry .. 1 wet) and the year's rain anomaly. */
  setClimate(s) { if (s) this.light.setClimate(s.season, s.rain); }

  /** The terrain meshes (each tile: [ground, standing water, rivers]): the ground casts and receives, water receives. */
  onTerrain(tiles) {
    const sh = this.quality.shadows;
    for (const set of tiles) set.forEach((m, k) => { m.castShadow = sh && k === 0; m.receiveShadow = sh; });
  }

  /** An animal mesh (near: the detailed bodies, which cast shadows; all receive them). */
  onBodies(mesh, bodyMat, near) {
    if (!this.quality.shadows) return;
    mesh.receiveShadow = true;
    if (near) { mesh.castShadow = true; mesh.customDepthMaterial = this._bodyDepth ||= bodyDepthMaterial(bodyMat); }
  }

  plants(force) { this.veg.plant(force); }

  /** Once a frame, after world3d.js has placed the camera and set fog.far, before render(). */
  update() {
    const v = this.v, now = performance.now(), dt = Math.min(250, now - this._t); this._t = now;
    this.frameMs += (dt - this.frameMs) * 0.05;
    this._adapt(dt);
    const cam = v.camera, fog = v.scene.fog;
    this.light.update(cam, v.controls.target, v.mode, dt, (v.explore && v.explore.ps) || 1);   // (the person's size, explore.js)
    // fog: the sky's horizon in the direction the camera looks; haze length from the view (looking down from high up
    // one looks through less air than along the ground)
    cam.getWorldDirection(this._fwd);
    this.atm.horizonAt(this._fwd.x, this._fwd.z, this.fogColor);
    fog.color.copy(this.fogColor);
    if (v.wrapU) v.wrapU.uWrapBg.value.copy(this.fogColor);
    v.renderer.setClearColor(this.fogColor);
    const down = Math.max(0, -this._fwd.y);
    fog.near = 0.75 * fog.far * (1 + 4 * down * down) * (1 - 0.55 * (this.light.rainI || 0));   // read as the haze's e-folding length (sky.js); rain thickens it
    this.sky.update(cam, this.fogColor, now / 1000);
    this.veg.setTime(now / 1000);
    // rain or snow round the camera (weather.js), as the season gives; snow where the air at the view centre freezes
    {
      const t = v.controls.target, camD = cam.position.distanceTo(t), alt = cam.position.y - (v.world ? v.heightAt(cam.position.x, cam.position.z) : 0);
      let tempC = null;
      const f = v.fields, k = f ? v._layer('temp') : -1;
      if (k >= 0 && v.W) { const d = f.d, cs = v.W / d, fx = ((Math.floor(t.x / cs) % d) + d) % d, fz = ((Math.floor(t.z / cs) % d) + d) % d; tempC = f.data[k * d * d + fz * d + fx] / 255 * 90 - 40; }
      this.weather.update(cam, this.light.rainI || 0, tempC, camD, alt, now / 1000);
    }
  }

  render() { this.post.render(this.v.scene, this.v.camera); }

  _adapt(dt) {
    if (!this.quality.adapt || document.hidden || performance.now() - this._born < 8000) return;
    this._slow = dt > 33 ? this._slow + dt : Math.max(0, this._slow - dt * 0.5);
    if (this._slow < 4000) return;
    this._slow = 0;
    const Q = this.quality;
    if (this.post.bloom) { this.post.bloom = false; return; }
    if (Q.shadows && this.light.sun.shadow.mapSize.x > 1024) {
      const s = this.light.sun.shadow; s.mapSize.set(1024, 1024); if (s.map) { s.map.dispose(); s.map = null; } return;
    }
    if (Q.shadows) { Q.shadows = false; this.light.shadows = false; this.light.sun.castShadow = false; return; }
    if (this.post.samples) { this.post.samples = 0; this.post.w = -1; }
  }

  stats() {
    return { quality: this.quality.name, frameMs: +this.frameMs.toFixed(1), bloom: this.post.bloom, post: this.post.ok,
             shadowMap: this.quality.shadows ? this.light.sun.shadow.mapSize.x : 0, shadowR: this.light.R, plantMs: +this.veg.lastMs.toFixed(1),
             grassMs: +this.veg.grassMs.toFixed(1), grass: this.veg.grass.count, counts: this.veg.counts, cloud: +this.atm.cloud.toFixed(2), wet: +(this.light.wet || 0).toFixed(2), rain: +(this.light.rainI || 0).toFixed(2), snow: +this.weather.snow.toFixed(2) };
  }
}
