// Explore mode: walk the world on foot at its own scale, and hunt (the owner's request, 2026-11-01).
//
// Scale: the land is drawn at true scale, a field cell (FCELL = 4 units) being GEO_CELL_M = 218 m and the relief's
// H_RELIEF = 8000 m being RELIEF = 146.8 units (world3d.js), so a metre is 4 / 218 = 0.018 units; the animals and plants
// are drawn at true size too (js/scale.js: Explore is always x1, the overview magnifies). The person's
// eye is 1.7 m above the ground; it walks at 1.5 m/s and runs at 5 m/s (Ctrl held, or F, multiplies by 10: the map is
// many km across). It stands on the drawn ground (world3d.groundWater), wades slowly through shallow water and swims at
// the surface of deep water, and the map wraps at its edges as the animals' does.
//
// The person is in the simulation: its place and heading go to the server ~5 times a second ('player'), where the
// animals sense it as a 70 kg armed omnivore and predators may strike it (Player.cs). Weapons aim here, against the
// creatures as drawn, and the server checks and applies each hit ('hit' -> Player.Hit). Animals move in sim steps;
// the person moves in real time, so at a few steps a second they jump while it walks.

import * as THREE from 'three';
import { RELIEF } from '../world3d.js';
import { ExploreHud } from './hud.js';
import { rayCreatures, rayGround, Projectile, projectileMesh, viewModel } from './weapons.js';
import { Vehicles } from './vehicles/index.js';   // VEHICLES: jeep, plane, boat (js/explore/vehicles/)

const EYE_M = 1.7, WALK = 1.5, RUN = 5, FAST = 10, JUMP_V = 3.4, G_MS2 = 9.81;
const SWIM_DEPTH_M = 1.3;     // deeper than this the person swims, head at the surface
const SEND_MS = 200;          // place updates to the server
const ARROWS = 30;
const LOOK = 0.0022;          // radians per pixel of mouse movement

export class Explore {
  constructor(world, link, meta, view) {
    this.world = world; this.link = link; this.meta = meta;
    const ex = meta.explore || {};
    // units per metre: the view's (world.scale, js/scale.js: the sim's metres per unit, 0.018), which is the relief's own
    // vertical scale (RELIEF / H_RELIEF). The creatures and plants are drawn at true size here (x1), so the eye height,
    // walking speed and every weapon's reach are in the same metres as the bodies they aim at
    this.upmV = RELIEF / (ex.hRelief || 8000);
    this.weapons = ex.weapons || [];
    this.maxHp = ex.maxHp || 100;
    this.hud = new ExploreHud(view);
    this.active = false; this.locked = false;
    this.keys = new Set();
    this.pos = new THREE.Vector3(); this.yaw = 0; this.pitch = 0; this.vy = 0; this.onGround = true; this.fastToggle = false;
    this.cur = 0; this.ammo = ARROWS; this.cool = 0; this.drawT = 0; this.thrustT = 0;
    this.projectiles = [];
    this.lastSend = 0; this.lastT = 0; this.state = null; this.deathsSeen = -1; this.hpSeen = this.maxHp;
    this.killed = 0; this.targetT = 0;
    this.camWas = null;
    world.explore = this;
    this._wire();
    this.vehicles = new Vehicles(this, view);   // VEHICLES hook (its keys are caught ahead of ours)
  }

  get upm() { return this.world.scale.upm; }

  // ------------------------------------------------------------------ entering and leaving
  /** Enter Explore. near: the serial of the animal that was selected or followed (0: none). */
  enter(near = 0) {
    const w = this.world, c = w.controls.target, cam = w.camera;
    this.camWas = { pos: cam.position.clone(), target: c.clone(), fov: cam.fov, near: cam.near, far: cam.far };
    // start where the overview was looking, facing the way the camera faced ...
    this.pos.set(((c.x % w.W) + w.W) % w.W, 0, ((c.z % w.W) + w.W) % w.W);
    const d = new THREE.Vector3(); cam.getWorldDirection(d);
    this.yaw = Math.atan2(d.z, d.x); this.pitch = 0;
    // ... but near life (2026-11-01, the owner: "it just drops you in a random area not near animals"): beside the
    // animal that was selected or followed, else beside the densest group of animals the overview was looking at
    // (a swimmer far out in a lake has no dry ground beside it: the next of the group is tried)
    const cands = (near && w.agents.has(near) ? [near] : []).concat(this._crowdNear(c.x, c.z));
    let placed = false;
    for (const t of cands.slice(0, 40)) {
      const p = this._poseNear(t);
      if (p) { this.pos.x = p.x; this.pos.z = p.z; this.yaw = p.yaw; this.pitch = p.pitch; placed = true; break; }
    }
    if (!placed) this._toLand();
    this._standOn(true);
    this.active = true; this.keys.clear(); this.vy = 0; this.lastT = performance.now();
    w.scene.add(cam);   // the weapon in hand is a child of the camera
    this._setWeapon(this.cur);
    this.hud.show(true);
    this.hud.message('EXPLORE · click the view to look with the mouse', false, 3000);
    this._send(true);
    this._lock();
  }

  exit() {
    if (!this.active) return;
    this.active = false;
    this.vehicles.leaveExplore();   // VEHICLES hook
    this.link.send({ cmd: 'player', on: false });
    if (document.pointerLockElement) document.exitPointerLock();
    this.hud.show(false);
    if (this.vm) { this.world.camera.remove(this.vm); this.vm = null; }
    for (const p of this.projectiles) p.dispose();
    this.projectiles = [];
    // back to an orbit over where the person stood
    const w = this.world, cam = w.camera, g = w.heightAt(this.pos.x, this.pos.z);
    cam.fov = 55; cam.near = 0.01; cam.far = this.camWas ? this.camWas.far : cam.far; cam.updateProjectionMatrix();
    // ~100 m behind where the person stood and looked, high enough that no hill is between (world3d keeps it so)
    const bx = this.pos.x - Math.cos(this.yaw) * 1.6, bz = this.pos.z - Math.sin(this.yaw) * 1.6;
    w.controls.target.set(this.pos.x, g, this.pos.z);
    cam.position.set(bx, Math.max(g + 0.9, w.heightAt(bx, bz) + 0.4), bz);
    cam.position.y = Math.max(cam.position.y, w._sightY(w.controls.target, cam.position));
    cam.up.set(0, 1, 0);
    w.controls.update();
  }

  /** The lens and fog of a walker's view (world3d.setMode('explore')). Near plane a few mm-scale units for the hands. */
  setView() {
    const cam = this.world.camera;
    cam.fov = 72; cam.near = 0.004; cam.far = Math.max(1200, this.world.W * 1.2); cam.updateProjectionMatrix();
    this.world.scene.fog.near = 40; this.world.scene.fog.far = 420;
  }

  // ------------------------------------------------------------------ finding life
  /** Animals (serials) of the densest group around (cx, cz): the animals binned on a grid of ~300 m, each bin weighed
   *  by its count and its nearness to (cx, cz); its members nearest the bin's middle first, then the other bins'. */
  _crowdNear(cx, cz) {
    const w = this.world, W = w.W, B = Math.max(2, 300 * this.upm), bins = new Map();
    for (const [serial, e] of w.agents) {
      if (e.stage === 0 || w.dead.has(serial) || w.hidden.has(e.sp)) continue;
      const k = Math.floor(e.x / B) + ',' + Math.floor(e.y / B);
      let b = bins.get(k); if (!b) bins.set(k, b = { n: 0, x: 0, y: 0, list: [] });
      b.n++; b.x += e.x; b.y += e.y; b.list.push(serial);
    }
    const score = (b) => { let dx = b.x / b.n - cx, dz = b.y / b.n - cz; dx -= Math.round(dx / W) * W; dz -= Math.round(dz / W) * W; return b.n / (1 + Math.hypot(dx, dz) / (W * 0.15)); };
    const out = [];
    for (const b of [...bins.values()].sort((p, q) => score(q) - score(p)).slice(0, 4)) {
      const mx = b.x / b.n, my = b.y / b.n, d = (s) => { const e = w.agents.get(s); return Math.hypot(e.x - mx, e.y - my); };
      out.push(...b.list.sort((p, q) => d(p) - d(q)).slice(0, 12));
    }
    return out;
  }

  /** A place ~8-14 m from animal serial on dry ground that is not a cliff, facing it: {x, z, yaw, pitch}, or null. */
  _poseNear(serial) {
    const w = this.world, e = w.agents.get(serial); if (!e) return null;
    const W = w.W, m = this.upm, wrap = (v) => ((v % W) + W) % W, a0 = Math.random() * Math.PI * 2;
    for (const r of [8, 11, 14, 20, 30, 45]) for (let k = 0; k < 12; k++) {
      const a = a0 + (k / 12) * Math.PI * 2, x = wrap(e.x + Math.cos(a) * r * m), z = wrap(e.y + Math.sin(a) * r * m);
      const [g, s] = w.groundWater(x, z);
      if (s === s && s > g + 0.3 * m) continue;                          // water deeper than the ankles
      const sl = Math.max(Math.abs(w.groundWater(x + 2 * m, z)[0] - w.groundWater(x - 2 * m, z)[0]), Math.abs(w.groundWater(x, z + 2 * m)[0] - w.groundWater(x, z - 2 * m)[0])) / (4 * m);
      if (sl > 1) continue;                                              // steeper than 45 degrees: a cliff
      const eye = g + EYE_M * m, tmp = {}; w.creatureNow(serial, e, tmp);
      const dx = tmp.x - x, dz = tmp.z - z, dy = tmp.cy - eye, hd = Math.hypot(dx, dz);
      return { x, z, yaw: Math.atan2(dz, dx), pitch: Math.max(-0.6, Math.min(0.6, Math.atan2(dy, hd))) };
    }
    return null;
  }

  /** Jump beside an animal and say who it is. */
  _jumpTo(serial, how) {
    const p = this._poseNear(serial);
    if (!p) { this.hud.message('no dry ground beside it', true, 1500); return false; }
    if (this.vehicles.driving) { this.hud.message('get out first (E)', true, 1200); return false; }
    this.setPose(p.x, p.z, p.yaw, p.pitch);
    const e = this.world.agents.get(serial), c = this.world.clades && this.world.clades[e.sp];
    this.hud.message(`${how}: <b style="color:${c ? c.color : '#aaa'}">${c ? c.name : 'an animal'}</b> #${serial}<br><span style="font-size:11px">N: nearest animal · Tab: next clade</span>`, false, 2500);
    return true;
  }

  /** N: the nearest animal more than 15 m away (the next one along, pressed again). */
  _jumpNearest() {
    const w = this.world, W = w.W, m = this.upm; let best = 0, bd = Infinity;
    for (const [serial, e] of w.agents) {
      if (e.stage === 0 || w.dead.has(serial) || w.hidden.has(e.sp)) continue;
      let dx = e.x - this.pos.x, dz = e.y - this.pos.z; dx -= Math.round(dx / W) * W; dz -= Math.round(dz / W) * W;
      const d = Math.hypot(dx, dz) / m;
      if (d > 15 && d < bd) { bd = d; best = serial; }
    }
    if (!best) { this.hud.message('no other animal', true, 1500); return; }
    this._jumpTo(best, `nearest animal, ${Math.round(bd)} m away`);
  }

  /** Tab: the next living clade (most numerous first), its member nearest the person. */
  _jumpClade(dir) {
    const w = this.world, W = w.W, counts = new Map();
    for (const [serial, e] of w.agents) if (e.stage !== 0 && !w.dead.has(serial) && !w.hidden.has(e.sp)) counts.set(e.sp, (counts.get(e.sp) || 0) + 1);
    const order = [...counts.keys()].sort((a, b) => counts.get(b) - counts.get(a) || a - b);
    if (!order.length) { this.hud.message('no animals', true, 1500); return; }
    const i = order.indexOf(this.cladeCur);
    this.cladeCur = order[((i < 0 ? (dir > 0 ? -1 : 0) : i) + dir + order.length) % order.length];
    let best = 0, bd = Infinity;
    for (const [serial, e] of w.agents) {
      if (e.sp !== this.cladeCur || e.stage === 0 || w.dead.has(serial)) continue;
      let dx = e.x - this.pos.x, dz = e.y - this.pos.z; dx -= Math.round(dx / W) * W; dz -= Math.round(dz / W) * W;
      const d = Math.hypot(dx, dz); if (d < bd) { bd = d; best = serial; }
    }
    if (best) this._jumpTo(best, `clade ${order.indexOf(this.cladeCur) + 1} of ${order.length} (${counts.get(this.cladeCur)} seen)`);
  }

  // ------------------------------------------------------------------ input
  _wire() {
    const cv = this.world.canvas;
    document.addEventListener('pointerlockchange', () => {
      const was = this.locked;
      this.locked = document.pointerLockElement === cv;
      if (was && !this.locked && this.active) this.onLeave && this.onLeave();   // Esc released the mouse: leave Explore
    });
    document.addEventListener('pointerlockerror', () => { this.noLock = true; if (this.active) this.hud.message('no pointer lock here: drag to look', false); });
    document.addEventListener('mousemove', (e) => {
      if (!this.active) return;
      if (this.locked || (this.noLock && this.dragging)) { this.look(e.movementX || 0, e.movementY || 0); }
    });
    cv.addEventListener('mousedown', (e) => {
      if (!this.active) return;
      e.preventDefault();
      if (!this.locked && !this.noLock) { this._lock(); return; }
      if (this.noLock && (e.button === 1 || e.altKey)) { this.dragging = true; return; }   // no pointer lock: middle or Alt drag looks
      this.press(e.button === 2 ? 'secondary' : 'primary', true);
    });
    addEventListener('mouseup', (e) => { if (!this.active) return; this.dragging = false; this.press(e.button === 2 ? 'secondary' : 'primary', false); });
    cv.addEventListener('contextmenu', (e) => { if (this.active) e.preventDefault(); });
    cv.addEventListener('wheel', (e) => { if (!this.active) return; e.preventDefault(); this._setWeapon((this.cur + (e.deltaY > 0 ? 1 : -1) + this.weapons.length) % this.weapons.length); }, { passive: false });
    addEventListener('keydown', (e) => {
      if (!this.active || e.target.closest?.('input, textarea, select')) return;
      if (e.code === 'Escape') { this.onLeave && this.onLeave(); return; }
      if (['KeyW', 'KeyA', 'KeyS', 'KeyD', 'Space', 'ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'ControlLeft', 'ControlRight', 'ShiftLeft', 'ShiftRight'].includes(e.code)) e.preventDefault();
      if (e.code === 'Space' && !e.repeat) this._jump();
      if (e.code === 'Tab') { e.preventDefault(); if (!e.repeat) this._jumpClade(e.shiftKey ? -1 : 1); return; }
      if (e.code === 'KeyN' && !e.repeat) { this._jumpNearest(); return; }
      if (e.code === 'KeyF' && !e.repeat) { this.fastToggle = !this.fastToggle; this.hud.message(this.fastToggle ? 'travel ×10' : 'travel ×1', false, 1200); }
      const wi = this.weapons.findIndex((w) => w.key === e.key);
      if (wi >= 0) this._setWeapon(wi);
      this.keys.add(e.code);
    });
    addEventListener('keyup', (e) => this.keys.delete(e.code));
    addEventListener('blur', () => this.keys.clear());
    this.link.listen('hit', (h) => this._onHit(h));
  }

  _lock() {
    const cv = this.world.canvas;
    if (this.noLock || !cv.requestPointerLock) { this.noLock = true; return; }
    try { const p = cv.requestPointerLock(); if (p && p.catch) p.catch(() => { this.noLock = true; }); } catch { this.noLock = true; }
  }

  look(dx, dy) {
    this.yaw += dx * LOOK;
    this.pitch = Math.max(-1.45, Math.min(1.45, this.pitch - dy * LOOK));
  }

  /** Put the person at a place (map units), facing yaw with pitch (radians): for tests and respawns. */
  setPose(x, z, yaw = this.yaw, pitch = this.pitch) {
    const W = this.world.W;
    // the pitch within the look's own limits (an unclamped one, past ±90°, turned the view upside down)
    this.pos.x = ((x % W) + W) % W; this.pos.z = ((z % W) + W) % W; this.yaw = yaw; this.pitch = Math.max(-1.45, Math.min(1.45, pitch));
    this.vy = 0; this._standOn(true); this._send(true);
  }

  _setWeapon(i) {
    if (!this.weapons.length) return;
    this.cur = i; this.drawT = 0;
    const cam = this.world.camera;
    if (this.vm) cam.remove(this.vm);
    this.vm = viewModel(this.weapons[i].id, this.upm);
    cam.add(this.vm);
    this.hud.weapons(this.weapons, this.cur, this.ammo);
  }

  // ------------------------------------------------------------------ the person's body
  /** Ground under the person and the water over it (y in units). */
  _support() {
    const [g, s] = this.world.groundWater(this.pos.x, this.pos.z), m = this.upm;
    const depth = s === s ? Math.max(0, s - g) : 0;
    if (depth > SWIM_DEPTH_M * m) return { feet: s - (EYE_M - 0.25) * m, swim: true, depth: depth / m, ground: g };
    return { feet: g, swim: false, depth: depth / m, ground: g };
  }

  /** Over water, move to the nearest dry ground (rings of cells outward), so Explore never starts in a lake. */
  _toLand() {
    const w = this.world, S = w.W / w.world.fw;
    const dry = (x, z) => { const s = w.groundWater(x, z)[1]; return s !== s; };
    if (dry(this.pos.x, this.pos.z)) return;
    for (let r = 1; r < 200; r++) for (let k = 0; k < 8 * r; k++) {
      const a = (k / (8 * r)) * Math.PI * 2, x = this.pos.x + Math.cos(a) * r * S, z = this.pos.z + Math.sin(a) * r * S;
      if (dry(x, z)) { this.pos.x = ((x % w.W) + w.W) % w.W; this.pos.z = ((z % w.W) + w.W) % w.W; return; }
    }
  }

  _standOn(force) { const s = this._support(); if (force || this.onGround) { this.pos.y = s.feet; this.onGround = true; } return s; }

  _jump() { const s = this._support(); if (this.onGround && !s.swim) { this.vy = JUMP_V * this.upm; this.onGround = false; } }

  /** Called by world3d.render each frame in Explore mode: move, aim, fly projectiles, place the camera and view centre. */
  drive(now, target) {
    const dt = Math.min(0.1, Math.max(0, (now - this.lastT) / 1000)); this.lastT = now;
    if (this.vehicles.update(now, dt, target)) return;   // VEHICLES hook: driving, the vehicle moves the person and the camera
    const k = this.keys, m = this.upm, W = this.world.W;
    // the arrow keys look too (and are the way to look where the mouse cannot be captured)
    const turn = (k.has('ArrowRight') ? 1 : 0) - (k.has('ArrowLeft') ? 1 : 0), tilt = (k.has('ArrowUp') ? 1 : 0) - (k.has('ArrowDown') ? 1 : 0);
    if (turn || tilt) this.look(turn * 800 * dt, -tilt * 500 * dt);
    const fwd = (k.has('KeyW') ? 1 : 0) - (k.has('KeyS') ? 1 : 0), side = (k.has('KeyD') ? 1 : 0) - (k.has('KeyA') ? 1 : 0);
    const s0 = this._support();
    let speed = (k.has('ShiftLeft') || k.has('ShiftRight') ? RUN : WALK);
    const fast = k.has('ControlLeft') || k.has('ControlRight') || this.fastToggle;
    if (s0.swim) speed *= 0.3;                                       // swimming: ~0.5 m/s, 1.5 m/s hurried
    else if (s0.depth > 0.05) speed *= 1 - 0.6 * Math.min(1, s0.depth / SWIM_DEPTH_M);   // wading slows
    if (fast) speed *= FAST;
    this.running = (fwd || side) && speed >= RUN * 0.9;
    if (fwd || side) {
      const cy = Math.cos(this.yaw), sy = Math.sin(this.yaw), l = Math.hypot(fwd, side);
      const vx = (cy * fwd - sy * side) / l, vz = (sy * fwd + cy * side) / l;
      this.pos.x += vx * speed * m * dt; this.pos.z += vz * speed * m * dt;
      this.pos.x = ((this.pos.x % W) + W) % W; this.pos.z = ((this.pos.z % W) + W) % W;   // the torus
    }
    const s = this._support();
    if (this.onGround) {
      // follow the ground down a slope without leaving it; a drop of more than a little is a fall
      if (this.pos.y - s.feet > 0.6 * m && !s.swim) { this.onGround = false; this.vy = 0; }
      else this.pos.y = s.feet;
    }
    if (!this.onGround) {
      this.vy -= G_MS2 * m * dt;
      this.pos.y += this.vy * dt;
      if (this.pos.y <= s.feet) { this.pos.y = s.feet; this.vy = 0; this.onGround = true; }
    }
    // camera at the eye
    const cam = this.world.camera, eye = this.pos.y + EYE_M * m;
    const cp = Math.cos(this.pitch);
    cam.position.set(this.pos.x, eye, this.pos.z);
    cam.lookAt(this.pos.x + Math.cos(this.yaw) * cp, eye + Math.sin(this.pitch), this.pos.z + Math.sin(this.yaw) * cp);
    target.set(this.pos.x, this.pos.y, this.pos.z);
    this._weapons(now, dt);
    if (now - this.lastSend > SEND_MS) this._send(false);
    if (now - this.targetT > 100) { this.targetT = now; this._target(); }
    // the speed the person moves at now (nothing while it stands), and how
    const gait = s.swim ? 'swimming' : s.depth > 0.05 ? 'wading' : (fast ? speed / FAST : speed) >= RUN * 0.9 ? 'running' : 'walking';
    this.hud.info(`${fwd || side ? `${gait} ${speed.toFixed(1)} m/s` : s.swim ? 'treading water' : 'standing'}${fast ? ' · <b>×10 travel</b>' : ''}<br>${this.killed} hunted · ${(this.state && this.state.deaths) || 0} deaths`);
  }

  _send(force) {
    const now = performance.now();
    if (!force && now - this.lastSend < SEND_MS) return;
    this.lastSend = now;
    this.link.send({ cmd: 'player', on: true, x: this.pos.x, y: this.pos.z, h: this.yaw, run: !!this.running });
  }

  // ------------------------------------------------------------------ weapons
  get weapon() { return this.weapons[this.cur]; }

  /** A button pressed or released: primary (left) or secondary (right). */
  press(which, down) {
    if (!this.active || this.vehicles.press(which, down) || !this.weapon) return;   // VEHICLES hook: a vehicle's weapon
    const mode = which === 'secondary' ? this.weapon.secondary : this.weapon.primary;
    if (!mode) return;
    if (mode.kind === 'bow') {
      if (down) { if (this.ammo > 0 && this.cool <= 0) this.drawT = performance.now(); else if (this.ammo <= 0) this.hud.message('no arrows left', true, 1200); }
      else if (this.drawT) { const f = Math.min(1, (performance.now() - this.drawT) / 900); this.drawT = 0; this.fire(mode, Math.max(0.15, f)); }
      return;
    }
    if (down) this.fire(mode, 1);
  }

  /** Use a weapon mode now (power 0..1, the bow's draw). */
  fire(mode, power) {
    if (this.cool > 0) return false;
    this.cool = mode.cooldown;
    const cam = this.world.camera, o = cam.position.clone(), d = new THREE.Vector3();
    cam.getWorldDirection(d);
    const m = this.upm;
    if (mode.kind === 'melee') {
      this.thrustT = performance.now();
      const hit = rayCreatures(this.world, o, d, mode.rangeM * m);
      if (hit) this._report(mode, hit, power);
      return !!hit;
    }
    if (mode.kind === 'hitscan') {
      const tg = rayGround(this.world, o, d, mode.rangeM * m);
      const hit = rayCreatures(this.world, o, d, Math.min(tg, mode.rangeM * m));
      this._tracer(o, d, hit ? hit.t : Math.min(tg, mode.rangeM * m));
      if (hit) this._report(mode, hit, power);
      return !!hit;
    }
    if (mode.kind === 'bow') { this.ammo--; this.hud.weapons(this.weapons, this.cur, this.ammo); }
    if (mode.kind === 'thrown' && this.vm) { this.vm.visible = false; setTimeout(() => { if (this.vm) this.vm.visible = true; }, mode.cooldown * 1000); }
    const start = o.clone().addScaledVector(d, 0.4 * m).add(new THREE.Vector3(0, -0.1 * m, 0));
    this.projectiles.push(new Projectile(this.world.scene, mode, power, start, d, m, projectileMesh(mode.kind, m)));
    return true;
  }

  _weapons(now, dt) {
    this.cool = Math.max(0, this.cool - dt);
    this.hud.draw(this.drawT ? (now - this.drawT) / 900 : 0);
    for (const p of this.projectiles) {
      const hit = p.step(this.world, dt, now);
      if (hit) this._report(p.mode, hit, p.power);
    }
    this.projectiles = this.projectiles.filter((p) => p.alive);
    // the weapon in hand: a thrust lunges forward, a drawn bow pulls its arrow back
    if (this.vm) {
      const a = this.thrustT ? (now - this.thrustT) / 250 : 1, m = this.upm;
      const base = this.weapon.id === 'spear' ? -0.55 : this.weapon.id === 'bow' ? -0.75 : -0.5;
      this.vm.position.z = (base - (a < 1 ? Math.sin(a * Math.PI) * 0.6 : 0)) * m;
      const arrow = this.vm.getObjectByName('arrow');
      if (arrow) { arrow.visible = this.ammo > 0 && this.cool <= 0; arrow.position.z = (this.drawT ? Math.min(1, (now - this.drawT) / 900) * 0.3 : 0) * m; }   // drawn back toward the eye
    }
  }

  _tracer(o, d, t) {
    const g = new THREE.BufferGeometry().setFromPoints([o.clone().addScaledVector(d, 0.3 * this.upm), o.clone().addScaledVector(d, t)]);
    const line = new THREE.Line(g, new THREE.LineBasicMaterial({ color: 0xfff1b0, transparent: true, opacity: 0.8 }));
    this.world.scene.add(line);
    setTimeout(() => { this.world.scene.remove(line); g.dispose(); }, 90);
  }

  /** Tell the server what this aim hit: weapon mode, serial, draw power, the damage it would do, and where. */
  _report(mode, hit, power) {
    this._send(true);   // the server checks the range from where the person is now
    const dmg = mode.damage * (mode.kind === 'bow' ? Math.max(0.2, power) : 1);
    this.link.send({ cmd: 'hit', weapon: mode.id, serial: hit.serial, power, damage: dmg, x: hit.x, y: hit.z, z: hit.y });
    this.pending = { serial: hit.serial, sp: hit.e.sp, t: performance.now() };
    this.lastReport = { weapon: mode.id, serial: hit.serial };
  }

  _onHit(h) {
    this.lastResult = h;
    (this.results ||= []).push(h); if (this.results.length > 50) this.results.shift();
    if (!this.active) return;
    const name = this._cladeName(h.clade);
    if (h.ok) {
      this.hud.hitMarker(h.killed);
      if (h.killed) { this.killed = h.kills; this.hud.message(`you killed a ${name}`, false, 2500); }
    } else if (h.result !== 'RateLimited') {   // (the jeep's gun: hits beyond its rate are not counted, quietly)
      const why = { OutOfRange: 'out of range', NoLineOfSight: 'no line of sight', NoAnimal: 'it is gone', NotPresent: 'not in the world yet' }[h.result] || h.result;
      this.hud.message(`miss: ${why}`, false, 1000);
    }
  }

  _cladeName(sp) { const c = this.world.clades && this.world.clades.find((q) => q && q.id === sp); return c ? `<b style="color:${c.color}">${c.name}</b>` : 'creature'; }

  /** The animal under the crosshair: its name, clade and distance. */
  _target() {
    const cam = this.world.camera, o = cam.position, d = new THREE.Vector3(); cam.getWorldDirection(d);
    // only an animal the person can see: the ray stops at the ground (a hill or a bank hides what is behind it)
    const R = 400 * this.upm, hit = rayCreatures(this.world, o, d, Math.min(R, rayGround(this.world, o, d, R)));
    this.aim = hit;
    if (!hit) { this.hud.target(''); return; }
    const c = this.world.clades && this.world.clades.find((q) => q && q.id === hit.e.sp);
    const stage = ['egg', 'juvenile', 'adult'][hit.e.stage] || '';
    this.hud.target(`<span style="color:${c ? c.color : '#aaa'}">●</span> ${c ? c.name : 'creature'} #${hit.serial} <span class="d">· ${stage} · ${Math.round(hit.d / this.upm)} m</span>`);
  }

  // ------------------------------------------------------------------ the server's view of the person
  /** The status frame's player object: health, deaths (a death respawns the person elsewhere), kills. */
  status(p) {
    this.state = p;
    if (!p) return;
    if (p.kills !== undefined) this.killed = p.kills;
    if (this.active) {
      this.hud.health(p.hp, p.maxHp || this.maxHp);
      if (p.hp < this.hpSeen - 0.5) this.hud.hurt();
    }
    if (this.deathsSeen >= 0 && p.deaths > this.deathsSeen && this.active) this._respawn();
    this.deathsSeen = p.deaths; this.hpSeen = p.hp;
  }

  _respawn() {
    this.vehicles.eject();   // VEHICLES hook: out of the vehicle, which stays
    this.hud.message('YOU WERE KILLED<br><span style="font-size:12px">back on your feet somewhere else</span>', true, 3500);
    const w = this.world.world, fw = w.fw, S = this.world.W / fw;
    for (let k = 0; k < 500; k++) {
      const i = Math.floor(Math.random() * fw * fw);
      if (w.cell[i] === 1) { this.setPose((i % fw + 0.5) * S, (Math.floor(i / fw) + 0.5) * S); break; }
    }
    this.ammo = ARROWS; this.hud.weapons(this.weapons, this.cur, this.ammo);
  }
}
