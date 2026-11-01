// Explore mode vehicles (the owner's request, 2026-11-01): a jeep with a mounted automatic rifle, a plane that drops
// bombs, a boat that fires torpedoes. V opens a small menu that places one at a valid spot near the person (a jeep on
// land, a boat on the nearest water, a plane on flat ground); E gets in or out when near it.
//
// The console drives them (physics.js) as it walks the person; the server knows which vehicle the person is in
// ('player' with veh and alt), loads each vehicle's weapon when it is placed ('vehicle'), checks the jeep's hits and
// limits their rate ('hit' -> Player.Hit), and checks and applies every explosion ('blast' -> Player.Blast). Ranges, rates,
// radii and speeds come from the server's table (meta.explore.vehicles) and are in metres, turned into view units
// with the server's metresPerUnit (across) and the view's own vertical scale (up).
//
// Hooks: explore.js calls update() from drive() (true: the vehicle has the camera this frame), press() from its mouse
// buttons, leaveExplore() from exit() and eject() when the person is killed; world3d.js is not touched.

import * as THREE from 'three';
import { jeepModel, planeModel, boatModel, bombMesh, torpedoMesh, scorch } from './models.js';
import { VehicleState, stepJeep, stepBoat, stepPlane } from './physics.js';
import { Fx } from './effects.js';
import { Bomb, Torpedo, BOMB_DRAG } from './arms.js';
import { VehicleHud } from './hud.js';
import { rayCreatures, rayGround } from '../weapons.js';

const SEND_MS = 200;
const ENTER_M = 10;            // how near a vehicle the person must be to get in (m)
const SUB = 1 / 120;           // physics substep (s)
const lerpAngle = (a, b, t) => a + Math.atan2(Math.sin(b - a), Math.cos(b - a)) * t;

export class Vehicles {
  constructor(ex, view) {
    this.ex = ex; this.world = ex.world; this.link = ex.link;
    const m = ex.meta.explore || {};
    this.specs = {};
    for (const v of m.vehicles || []) this.specs[v.id] = v;
    this.mpu = m.metresPerUnit || 54.5;
    this.list = {};            // kind -> vehicle (one of each)
    this.wrecks = [];
    this.cur = null;           // the vehicle the person is in
    this.shots = [];           // bombs and torpedoes in flight
    this.firing = false; this.gunCool = 0; this.bombCool = 0; this.torpCool = 0;
    this.torpDepth = 2; this.homing = true;
    this.lastSend = 0; this.hudT = 0; this.sightT = 0; this.blastId = 0; this.pendingBlasts = new Map();
    this.camYaw = 0; this.shake = 0;
    this.stats = { shots: 0, bombs: 0, torpedoes: 0, blasts: [], kills: 0 };   // for tests and the HUD
    this.hud = new VehicleHud(view);
    this.hud.onPick = (k) => { this.hud.menu(false); this.spawn(k); };
    this._wire();
  }

  get driving() { return !!this.cur; }

  /** The terrain sampler and effects, made once the world is there. */
  _init() {
    if (this.T) return true;
    const w = this.world;
    if (!w.world) return false;
    const W = w.W, uv = this.ex.upm, uh = 1 / this.mpu, wrap = (v) => ((v % W) + W) % W;
    const iMoist = (w.meta.fieldLayers || []).indexOf('moisture');
    this.T = {
      W, uh, uv, wrap,
      at: (x, z) => {
        const r = w.groundWater(wrap(x), wrap(z)), g = r[0], s = r[1], water = s === s;
        return { g, s, water, depthM: water ? Math.max(0, s - g) / uv : 0 };
      },
      moist: (x, z) => {
        const f = w.fields; if (!f || iMoist < 0) return 0.3;
        const d = f.d, cx = Math.floor(wrap(x) / W * d) % d, cy = Math.floor(wrap(z) / W * d) % d;
        return f.data[iMoist * d * d + cy * d + cx] / 255;
      },
    };
    this.u = uv;
    this.fx = new Fx(w.scene, uv);
    // the bomb sight: where a bomb let go now would land, and its blast radius
    this.sightRing = new THREE.Mesh(new THREE.RingGeometry(0.92, 1, 40).rotateX(-Math.PI / 2),
      new THREE.MeshBasicMaterial({ color: 0xff5a4a, transparent: true, opacity: 0.75, depthWrite: false, depthTest: false }));
    this.sightRing.visible = false; this.sightRing.renderOrder = 8; this.sightRing.frustumCulled = false;
    w.scene.add(this.sightRing);
    return true;
  }

  /** The server's cell under a place: the realm bits (1 land, 2 shallow, 4 deep) of the WORLD frame. */
  _simCell(x, z) {
    const w = this.world.world, fw = w.fw, S = this.world.W / fw, T = this.T;
    const cx = Math.floor(T.wrap(x) / S) % fw, cy = Math.floor(T.wrap(z) / S) % fw;
    return w.cell[cy * fw + cx];
  }

  // ------------------------------------------------------------------------------------------------ input
  _wire() {
    // capture phase: ahead of explore.js's own key handler, so the menu and the vehicle get their keys first
    addEventListener('keydown', (e) => {
      const ex = this.ex;
      if (!ex.active || e.target.closest?.('input, textarea, select')) return;
      const c = e.code, stop = () => { e.preventDefault(); e.stopImmediatePropagation(); };
      if (this.hud.menuOpen) {
        const k = { Digit1: 'jeep', Numpad1: 'jeep', Digit2: 'plane', Numpad2: 'plane', Digit3: 'boat', Numpad3: 'boat' }[c];
        if (k) { this.hud.menu(false); this.spawn(k); }
        else if (c === 'KeyV' || c === 'Escape') this.hud.menu(false);
        stop(); return;
      }
      if (c === 'KeyV' && !e.repeat) { this.hud.menu(true); stop(); return; }
      if (c === 'KeyE' && !e.repeat) { this.toggleSeat(); stop(); return; }
      if (!this.cur || c === 'Escape') return;
      stop();
      ex.keys.add(c);
      if (e.repeat) return;
      const v = this.cur;
      if (c === 'KeyC') { v.cam = v.cam === 'first' ? 'third' : 'first'; ex.hud.message(`${v.cam === 'first' ? 'first' : 'third'}-person view`, false, 900); }
      if (c === 'KeyB' && v.kind === 'plane') this._bomb(v);
      if (v.kind === 'boat') {
        const w = v.spec.weapon;
        if (c === 'KeyT') this._toggleHoming();
        if (c === 'KeyR') this.torpDepth = Math.max(w.depthMinM || 0.5, this.torpDepth - 1);
        if (c === 'KeyF') this.torpDepth = Math.min(w.depthMaxM || 30, this.torpDepth + 1);
      }
    }, true);
    this.link.listen('vehicle', (r) => this._onVehicle(r));
    this.link.listen('blast', (r) => this._onBlast(r));
  }

  _toggleHoming() { this.homing = !this.homing; this.ex.hud.message(`torpedoes: ${this.homing ? 'homing' : 'run straight'}`, false, 1000); }

  /** A mouse button (from explore.press). True when a vehicle used it. */
  press(which, down) {
    const v = this.cur;
    if (!v) return false;
    if (which === 'primary') {
      if (v.kind === 'jeep') this.firing = down;
      else if (down && v.kind === 'plane') this._bomb(v);
      else if (down && v.kind === 'boat') this._torpedo(v);
    } else if (down && v.kind === 'boat') this._toggleHoming();
    return true;
  }

  // ------------------------------------------------------------------------------------------------ placing vehicles
  /** Place a vehicle of a kind at a valid spot near the person (replacing the one of that kind there was). */
  spawn(kind) {
    if (!this._init()) return false;
    const spec = this.specs[kind], ex = this.ex;
    if (!spec) { ex.hud.message(`no ${kind} on this server`, true, 1500); return false; }
    if (this.cur) { ex.hud.message('get out first (E)', true, 1200); return false; }
    const p = ex.pos;
    const spots = kind === 'jeep' ? this._findLand(p.x, p.z, ex.yaw) : kind === 'boat' ? this._findWater(p.x, p.z) : this._findRunways(p.x, p.z, ex.yaw);
    if (!spots.length) { ex.hud.message(kind === 'boat' ? 'no water deep enough near here' : kind === 'plane' ? 'no flat ground long enough near here' : 'no place for a jeep here', true, 2200); return false; }
    this._remove(kind);
    const v = new VehicleState(kind, spec);
    v.cam = 'third'; v.rounds = spec.weapon.rounds; v.heat = 0; v.overheat = false; v.confirmed = false; v.spots = spots.slice(1);
    v.mesh = kind === 'jeep' ? jeepModel() : kind === 'plane' ? planeModel() : boatModel();
    v.mesh.scale.setScalar(this.u);
    this.world.scene.add(v.mesh);
    this.list[kind] = v;
    this._placeAt(v, spots[0]);
    return true;
  }

  _placeAt(v, s) {
    const T = this.T, ex = this.ex;
    v.x = T.wrap(s.x); v.z = T.wrap(s.z); v.yaw = s.yaw; v.v = 0; v.pitch = 0; v.roll = 0; v.onGround = true;
    const a = T.at(v.x, v.z);
    v.y = v.kind === 'boat' ? a.s : a.g;
    this.link.send({ cmd: 'vehicle', kind: v.kind, x: v.x, y: v.z });
    // out of reach: the person is taken over to it (it is placed at the nearest valid spot, which may be a way off)
    const dm = this._distM(ex.pos.x, ex.pos.z, v.x, v.z), far = dm > ENTER_M * 0.8;
    if (far) {
      const side = this._besideSpot(v);
      ex.setPose(side.x, side.z, Math.atan2(v.z - side.z, v.x - side.x), -0.15);
    }
    const where = dm >= 1000 ? `${(dm / 1000).toFixed(1)} km` : `${dm.toFixed(0)} m`;
    ex.hud.message(`${v.spec.name} placed ${where} away${far ? ': you are beside it' : ''} · <b>E</b> to get in`, false, 3000);
  }

  _onVehicle(r) {
    const v = this.list[r.kind];
    if (!v) return;
    if (r.ok) {
      v.confirmed = true; v.rounds = r.rounds;
      if (this.pendingBoard === v) { this.pendingBoard = null; if (!this.cur && this._distM(this.ex.pos.x, this.ex.pos.z, v.x, v.z) < ENTER_M) this.toggleSeat(); }
      return;
    }
    if (v.spots && v.spots.length) { this._placeAt(v, v.spots.shift()); return; }   // the server saw it otherwise: the next spot
    this.ex.hud.message(`cannot place the ${r.kind}: ${r.why}`, true, 2500);
    this._remove(r.kind);
  }

  _remove(kind) {
    const v = this.list[kind];
    if (!v) return;
    if (this.cur === v) this.cur = null;
    this.world.scene.remove(v.mesh);
    delete this.list[kind];
  }

  _distM(ax, az, bx, bz) {
    const W = this.T.W; let dx = ax - bx, dz = az - bz; dx -= Math.round(dx / W) * W; dz -= Math.round(dz / W) * W;
    return Math.hypot(dx, dz) / this.T.uh;
  }

  /** Rings of points around (x, z) out to maxM, nearest first. */
  *_rings(x, z, stepM, maxM, per) {
    const uh = this.T.uh;
    yield [x, z, 0];
    for (let r = stepM; r <= maxM; r += stepM) {
      const n = Math.max(8, Math.round(per * r / stepM));
      for (let k = 0; k < n; k++) { const a = (k / n) * Math.PI * 2; yield [x + Math.cos(a) * r * uh, z + Math.sin(a) * r * uh, a]; }
    }
  }

  _dry(x, z) { return !this.T.at(x, z).water && this._simCell(x, z) === 1; }

  /** A jeep: dry, gentle ground; first 8 m ahead of the person, then outward. */
  _findLand(x, z, yaw) {
    const T = this.T, uh = T.uh;
    const ok = (px, pz) => {
      if (!this._dry(px, pz)) return false;
      for (const [ox, oz] of [[3, 0], [-3, 0], [0, 3], [0, -3]]) if (!this._dry(px + ox * uh, pz + oz * uh)) return false;
      const sl = Math.abs(T.at(px + 3 * uh, pz).g - T.at(px - 3 * uh, pz).g) / T.uv / 6 + Math.abs(T.at(px, pz + 3 * uh).g - T.at(px, pz - 3 * uh).g) / T.uv / 6;
      return sl < 0.35;
    };
    const ax = x + Math.cos(yaw) * 8 * uh, az = z + Math.sin(yaw) * 8 * uh;
    const out = [];
    if (ok(ax, az)) out.push({ x: ax, z: az, yaw });
    // a few more, should the server refuse the first (it checks the sim's own land cell)
    for (const [px, pz] of this._rings(x, z, 10, 3000, 6)) { if (out.length >= 5) break; if (ok(px, pz)) out.push({ x: px, z: pz, yaw }); }
    return out;
  }

  /** A boat: the nearest water at least 1.5 m deep (else at least its least depth), clear of the shore around it. */
  _findWater(x, z) {
    const T = this.T, uh = T.uh, minD = this.specs.boat.minDepthM || 0.4;
    const ok = (px, pz, d) => {
      const a = T.at(px, pz);
      if (!a.water || a.depthM < d || (this._simCell(px, pz) & 6) === 0) return false;
      for (const [ox, oz] of [[6, 0], [-6, 0], [0, 6], [0, -6]]) { const b = T.at(px + ox * uh, pz + oz * uh); if (!b.water || b.depthM < minD) return false; }
      return true;
    };
    // facing the longest run of open water (up to 400 m), so it does not start with its bow on the shore
    const heading = (px, pz) => {
      let best = 0, run = -1;
      for (let k = 0; k < 16; k++) {
        const h = (k / 16) * Math.PI * 2; let r = 0;
        while (r < 400) { const a = T.at(px + Math.cos(h) * (r + 10) * uh, pz + Math.sin(h) * (r + 10) * uh); if (!a.water || a.depthM < 1.5) break; r += 10; }
        if (r > run) { run = r; best = h; }
      }
      return best;
    };
    // several candidates in different field cells, the server's deep water (realm bit 4: a lake's, not a stream's) first:
    // the server measures depth its own way (Player.WaterDepthM) and may refuse a spot the drawn water calls deep enough
    // (a carved river channel, or depths in other units); each refusal tries the next (_onVehicle). Before 2026-11-01 one
    // candidate was offered, so one refusal meant no boat.
    const out = [], cells = new Set(), S = this.world.W / this.world.world.fw;
    const cellKey = (px, pz) => Math.floor(T.wrap(px) / S) + ',' + Math.floor(T.wrap(pz) / S);
    for (const deepOnly of [true, false]) {
      for (const need of [1.5, minD + 0.2]) {
        let firstM = -1;
        for (const [px, pz] of this._rings(x, z, 25, deepOnly ? 4000 : 12000, 3)) {
          if (out.length >= 8) break;
          if (firstM >= 0 && this._distM(x, z, px, pz) > firstM + 1500) break;   // the rest no farther than 1.5 km past the first
          if (deepOnly && (this._simCell(px, pz) & 4) === 0) continue;
          const key = cellKey(px, pz);
          if (cells.has(key) || !ok(px, pz, need)) continue;
          cells.add(key); out.push({ x: px, z: pz, yaw: heading(px, pz) });
          if (firstM < 0) firstM = this._distM(x, z, px, pz);
        }
      }
    }
    return out;
  }

  /** A plane: a 350 m strip of dry ground, no step steeper than 3.5 degrees and less than 12 m of rise along it, and the
   *  field cells around it flat enough for the server (Vehicles.SpawnCheck: within the plane's slope of each other). */
  _findRunways(x, z, yaw) {
    const T = this.T, uh = T.uh, uv = T.uv, out = [];
    const S = this.world.W / this.world.world.fw, cellM = S / uh;
    const tanStep = Math.tan(3.5 * Math.PI / 180), tanCell = Math.tan((this.specs.plane.maxSlopeDeg || 6) * Math.PI / 180) * 0.8;
    const strip = (px, pz, h) => {
      const fx = Math.cos(h), fz = Math.sin(h);
      let g0 = 0, prev = 0;
      for (let k = 0; k <= 10; k++) {
        const qx = px + fx * k * 35 * uh, qz = pz + fz * k * 35 * uh;
        if (!this._dry(qx, qz)) return false;
        const g = T.at(qx, qz).g / uv;
        if (k === 0) g0 = prev = g;
        else { if (Math.abs(g - prev) / 35 > tanStep || Math.abs(g - g0) > 12) return false; prev = g; }
      }
      const g = T.at(px, pz).g / uv;
      for (const [ox, oz] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) if (Math.abs(T.at(px + ox * S, pz + oz * S).g / uv - g) > tanCell * cellM) return false;
      return true;
    };
    for (const [px, pz] of this._rings(x, z, 60, 8000, 2)) {
      for (let k = 0; k < 8; k++) {
        const h = yaw + (k * Math.PI) / 4;
        if (strip(px, pz, h)) { out.push({ x: px, z: pz, yaw: h }); break; }
      }
      if (out.length >= 6) break;
    }
    return out;
  }

  /** Where the person stands when it gets out (or walks up to a vehicle): 3 m to its left, on dry ground if there is
   *  some within 40 m (the boat), else in the water beside it. */
  _besideSpot(v) {
    const T = this.T, uh = T.uh, fx = Math.cos(v.yaw), fz = Math.sin(v.yaw);
    const lx = v.x + fz * 3.5 * uh, lz = v.z - fx * 3.5 * uh;
    if (v.kind !== 'boat') return { x: lx, z: lz };
    for (const [px, pz] of this._rings(v.x, v.z, 4, 40, 4)) if (!T.at(px, pz).water) return { x: px, z: pz };
    return { x: lx, z: lz };
  }

  // ------------------------------------------------------------------------------------------------ getting in and out
  toggleSeat() {
    if (!this._init()) return;
    const ex = this.ex;
    if (this.cur) return this._exit();
    let best = null, bd = ENTER_M;
    for (const v of Object.values(this.list)) {
      if (!v.alive) continue;
      const d = this._distM(ex.pos.x, ex.pos.z, v.x, v.z);
      if (d < bd) { bd = d; best = v; }
    }
    if (!best) { ex.hud.message('no vehicle near · <b>V</b> to place one', false, 1500); return; }
    // not yet confirmed by the server (its answer can take a moment, longer during the prehistory, whose branch answers
    // between its steps): get in as soon as it is, instead of refusing the key (2026-11-01: E pressed at once did nothing)
    if (!best.confirmed) { this.pendingBoard = best; ex.hud.message(`getting in the ${best.spec.name} as soon as the server has placed it…`, false, 2500); return; }
    this.cur = best;
    ex.yaw = best.yaw; ex.pitch = -0.08; this.camYaw = best.yaw; this.firing = false;
    this.yaw0 = ex.yaw; this.pitch0 = ex.pitch;   // the plane's chase camera: the mouse's swing from here
    if (ex.vm) ex.vm.visible = false;
    this.hud.driving(best.kind);
    this.hud.prompt('');
    ex.hud.message(`in the ${best.spec.name}`, false, 1200);
    this._send(best, true);
  }

  _exit(force) {
    const v = this.cur, ex = this.ex;
    if (!v) return;
    if (!force) {
      if (v.kind === 'plane' && (!v.onGround || v.v > 5)) { ex.hud.message('land and stop first', true, 1500); return; }
      if (Math.abs(v.v) > 6) { ex.hud.message('slow down first', true, 1200); return; }
    }
    this.cur = null; this.firing = false; v.v = 0; v.throttle = 0;
    this.hud.driving(null); this.sightRing.visible = false;
    ex.world.camera.up.set(0, 1, 0);
    if (ex.vm) ex.vm.visible = true;
    const s = this._besideSpot(v);
    ex.setPose(s.x, s.z, v.yaw, 0);   // on foot again: the next place update has no vehicle
  }

  /** The person was killed (explore respawns it elsewhere): out of the vehicle, which stays where it was. */
  eject() { if (this.cur) { this.cur.v = 0; this.cur = null; this.firing = false; this.hud.driving(null); if (this.sightRing) this.sightRing.visible = false; this.ex.world.camera.up.set(0, 1, 0); if (this.ex.vm) this.ex.vm.visible = true; } }

  /** Explore ends: every vehicle, shot and effect goes. */
  leaveExplore() {
    this.eject();
    for (const k of Object.keys(this.list)) this._remove(k);
    for (const w of this.wrecks) this.world.scene.remove(w.mesh);
    this.wrecks = [];
    for (const s of this.shots) s.dispose();
    this.shots = [];
    this.fx?.clear();
    this.hud.show(false);
  }

  // ------------------------------------------------------------------------------------------------ every frame
  /** From explore.drive: run the vehicles, shots and effects; true when the person drives (the camera is ours). */
  update(now, dt, target) {
    if (!this._init()) return false;
    const ex = this.ex;
    this.hud.show(true);
    this.fx.update(dt);
    this._shots(dt);
    this.bombCool = Math.max(0, this.bombCool - dt); this.torpCool = Math.max(0, this.torpCool - dt);
    for (const v of Object.values(this.list)) {
      if (v === this.cur) continue;
      if (v.kind === 'boat') stepBoat(v, {}, this.T, dt);   // a parked boat rides the swell
      this._pose(v, ex.pos.x, ex.pos.z);
    }
    if (!this.cur) {
      // on foot: the nearest vehicle in reach offers itself
      let near = null, nd = ENTER_M;
      for (const v of Object.values(this.list)) { const d = this._distM(ex.pos.x, ex.pos.z, v.x, v.z); if (v.alive && d < nd) { nd = d; near = v; } }
      this.hud.prompt(near ? `<b>E</b> · get in the ${near.spec.name}` : '');
      return false;
    }
    const v = this.cur, k = ex.keys, K = (c) => k.has(c);
    const inp = { fwd: K('KeyW'), back: K('KeyS'), left: K('KeyA'), right: K('KeyD'), up: K('KeyR'), down: K('KeyF'), yawL: K('KeyZ'), yawR: K('KeyX'), brake: K('Space') };
    if (v.kind === 'plane') { inp.fwd ||= K('ArrowUp'); inp.back ||= K('ArrowDown'); inp.left ||= K('ArrowLeft'); inp.right ||= K('ArrowRight'); }
    else {
      const turn = (K('ArrowRight') ? 1 : 0) - (K('ArrowLeft') ? 1 : 0), tilt = (K('ArrowUp') ? 1 : 0) - (K('ArrowDown') ? 1 : 0);
      if (turn || tilt) ex.look(turn * 800 * dt, -tilt * 500 * dt);
    }
    const step = v.kind === 'jeep' ? stepJeep : v.kind === 'boat' ? stepBoat : stepPlane;
    const n = Math.max(1, Math.ceil(dt / SUB));
    let wasBlocked = '';
    for (let i = 0; i < n; i++) {
      step(v, inp, this.T, dt / n);
      if (v.blocked) wasBlocked = v.blocked;
      if (v.crashed) { this._crash(v); return false; }
    }
    if (wasBlocked && now - (this._blockMsgT || 0) > 1500) { this._blockMsgT = now; ex.hud.message(wasBlocked, true, 1000); }
    // the person is where the vehicle is
    ex.pos.set(v.x, v.y, v.z);
    this._pose(v, v.x, v.z);
    this._trail(v, dt);
    this._camera(v, dt);
    target.set(v.x, v.y, v.z);
    this._weapons(v, now, dt);
    this._send(v, false);
    if (now - this.hudT > 100) {
      this.hudT = now;
      this.hud.panel(v, v.spec.weapon, { heat: v.heat, overheat: v.overheat, depth: this.torpDepth, homing: this.homing, cam: v.cam, asl: v.y / this.T.uv });
      if (v.kind !== 'plane') ex._target();
    }
    return true;
  }

  /** Place a vehicle's mesh: the copy of it nearest (cx, cz), turned by yaw, pitch and roll; wheels and propeller turn. */
  _pose(v, cx, cz) {
    const W = this.T.W, m = v.mesh;
    m.position.set(v.x + Math.round((cx - v.x) / W) * W, v.y, v.z + Math.round((cz - v.z) / W) * W);
    m.rotation.set(v.roll, -v.yaw, v.pitch, 'YZX');
    if (v.kind === 'jeep') {
      v.wheelA = (v.wheelA || 0) - v.v * (1 / 60) / 0.42;
      m.children.forEach((c) => { if (c.name === 'wheel') c.rotation.z = v.wheelA; });
    } else if (v.kind === 'plane') {
      const prop = m.getObjectByName('prop'); if (prop) prop.rotation.x += 0.2 + v.throttle * 1.5;
      const disc = m.getObjectByName('disc'); if (disc) disc.material.opacity = 0.05 + 0.2 * v.throttle;
      const n = v.rounds ?? 0;
      const bl = m.getObjectByName('bombL'), br = m.getObjectByName('bombR');
      if (bl) bl.visible = n >= 1; if (br) br.visible = n >= 2;
    }
  }

  /** Dust, spray and wake. */
  _trail(v, dt) {
    const T = this.T, sp = Math.abs(v.v);
    v.trailT = (v.trailT || 0) - dt;
    if (v.trailT > 0 || sp < 2) return;
    const back = new THREE.Vector3(v.x - Math.cos(v.yaw) * 2.2 * T.uh, v.y, v.z - Math.sin(v.yaw) * 2.2 * T.uh);
    if (v.kind === 'boat') {
      v.trailT = 0.12;
      back.x = v.x - Math.cos(v.yaw) * 5 * T.uh; back.z = v.z - Math.sin(v.yaw) * 5 * T.uh;
      back.y = T.at(back.x, back.z).s + 0.04 * T.uv;
      if (back.y === back.y) this.fx.wake(back, v.yaw, 2.5 + sp * 0.15, 5);
    } else if (v.kind === 'jeep') {
      v.trailT = 0.12;
      if (v.depthM > 0.05) this.fx.splash(back.setY(T.at(back.x, back.z).s), 0.6);
      else if (v.mud > 0.2 || sp > 10) this.fx.puff(back.setY(back.y + 0.3 * T.uv), 0.7 + 0.04 * sp, v.mud > 0.2 ? 0x5a4632 : 0x9b8a6e);
    } else if (v.kind === 'plane' && v.onGround && sp > 10) { v.trailT = 0.1; this.fx.puff(back.setY(back.y + 0.2 * T.uv), 1.2, 0x9b8a6e); }
  }

  _camera(v, dt) {
    const ex = this.ex, cam = ex.world.camera, T = this.T, u = this.u, m = v.mesh;
    cam.up.set(0, 1, 0);
    let pos, look;
    if (v.kind === 'plane') {
      this.camYaw = lerpAngle(this.camYaw, v.yaw, Math.min(1, dt * 2.5));
      const fwd = new THREE.Vector3(Math.cos(v.pitch) * Math.cos(v.yaw), Math.sin(v.pitch), Math.cos(v.pitch) * Math.sin(v.yaw));
      if (v.cam === 'first') {
        pos = m.localToWorld(new THREE.Vector3(0.6, 2.45, 0));
        look = pos.clone().addScaledVector(fwd, 50 * u);
        cam.up.set(0, 1, 0).applyQuaternion(m.quaternion);
      } else {
        // the chase camera trails the heading; the mouse swings it round the plane (ex.yaw/pitch since boarding)
        const oy = this.camYaw + (ex.yaw - this.yaw0), op = Math.max(-0.5, Math.min(1.2, 0.24 - (ex.pitch - this.pitch0)));
        const c = m.position.clone().add(new THREE.Vector3(0, 2 * u, 0));
        pos = c.clone().add(new THREE.Vector3(-Math.cos(oy) * Math.cos(op) * 25 * u, Math.sin(op) * 25 * u, -Math.sin(oy) * Math.cos(op) * 25 * u));
        look = Math.abs(ex.yaw - this.yaw0) < 0.05 ? c.clone().addScaledVector(fwd, 20 * u) : c;
      }
    } else {
      const cp = Math.cos(ex.pitch), dir = new THREE.Vector3(cp * Math.cos(ex.yaw), Math.sin(ex.pitch), cp * Math.sin(ex.yaw));
      if (v.cam === 'first') {
        pos = m.localToWorld(v.kind === 'jeep' ? new THREE.Vector3(-0.1, 2.0, -0.42) : new THREE.Vector3(-0.2, 3.0, 0.4));
      } else {
        const pivot = m.position.clone().add(new THREE.Vector3(0, (v.kind === 'jeep' ? 2.6 : 4.5) * u, 0));
        pos = pivot.clone().addScaledVector(dir, -(v.kind === 'jeep' ? 9 : 17) * u);
      }
      look = pos.clone().addScaledVector(dir, 50 * u);
    }
    // never below the ground or the water
    const a = T.at(pos.x, pos.z), floor = Math.max(a.g, a.water ? a.s : -1e9) + 0.6 * u;
    if (pos.y < floor) pos.y = floor;
    if (this.shake > 0.001) { pos.x += (Math.random() - 0.5) * this.shake * u; pos.y += (Math.random() - 0.5) * this.shake * u; this.shake *= Math.pow(0.02, dt); }
    cam.position.copy(pos);
    cam.lookAt(look);
  }

  _send(v, force) {
    const now = performance.now();
    if (!force && now - this.lastSend < SEND_MS) return;
    this.lastSend = now; this.ex.lastSend = now;
    this.link.send({ cmd: 'player', on: true, x: this.T.wrap(v.x), y: this.T.wrap(v.z), h: v.yaw, run: Math.abs(v.v) > 3, veh: v.kind, alt: v.agl });
  }

  // ------------------------------------------------------------------------------------------------ weapons
  _weapons(v, now, dt) {
    if (v.kind === 'jeep') {
      const w = v.spec.weapon;
      v.heat = Math.max(0, v.heat - 0.3 * dt);
      if (v.heat >= 1) v.overheat = true;
      if (v.overheat && v.heat < 0.35) v.overheat = false;
      const aim = this._aimPoint(w.rangeM);
      this._aimTurret(v, aim);
      if (this.firing && !v.overheat && v.rounds > 0) {
        this.gunCool -= dt;
        while (this.gunCool <= 0 && !v.overheat && v.rounds > 0) { this._shoot(v, w, aim); this.gunCool += w.cooldown; }
      } else {
        this.gunCool = Math.max(0, this.gunCool - dt);
        if (this.firing && v.rounds <= 0 && now - (this._ammoMsgT || 0) > 1500) { this._ammoMsgT = now; this.ex.hud.message('out of rounds: place a new jeep (V)', true, 1400); }
      }
      const fl = v.mesh.getObjectByName('flash');
      if (fl) fl.visible = now - (v.flashT || 0) < 45;
    }
    if (v.kind === 'plane') {
      // the bomb sight
      if (now - this.sightT > 100) { this.sightT = now; this._sight(v); }
    } else this.sightRing.visible = false;
  }

  /** Where the crosshair points: the first animal or ground along the camera's ray within range (view units). */
  _aimPoint(rangeM) {
    const cam = this.ex.world.camera, o = cam.position.clone(), d = new THREE.Vector3(); cam.getWorldDirection(d);
    const R = rangeM * this.T.uh, tg = rayGround(this.world, o, d, R), hit = rayCreatures(this.world, o, d, Math.min(tg, R));
    return o.addScaledVector(d, hit ? hit.t : Math.min(tg, R));
  }

  _aimTurret(v, aim) {
    const tur = v.mesh.getObjectByName('turret'), gun = v.mesh.getObjectByName('gun');
    if (!tur || !gun) return;
    const p = tur.getWorldPosition(new THREE.Vector3()), d = aim.clone().sub(p);
    const yaw = Math.atan2(d.z, d.x), pitch = Math.atan2(d.y, Math.hypot(d.x, d.z));
    tur.rotation.y = -Math.atan2(Math.sin(yaw - v.yaw), Math.cos(yaw - v.yaw));
    gun.rotation.z = Math.max(-0.35, Math.min(0.9, pitch - v.pitch));
  }

  /** One round of the jeep's gun: from its muzzle to the crosshair's point, a little spread; a tracer every other. */
  _shoot(v, w, aim) {
    const muz = v.mesh.getObjectByName('muzzle');
    const o = muz.getWorldPosition(new THREE.Vector3());
    const d = aim.clone().sub(o).normalize();
    d.x += (Math.random() - 0.5) * 0.008; d.y += (Math.random() - 0.5) * 0.008; d.z += (Math.random() - 0.5) * 0.008; d.normalize();
    const R = w.rangeM * this.T.uh, tg = rayGround(this.world, o, d, R), hit = rayCreatures(this.world, o, d, Math.min(tg, R));
    const t = hit ? hit.t : Math.min(tg, R), end = o.clone().addScaledVector(d, t);
    v.rounds--; v.heat += w.heatPerShot; v.flashT = performance.now();
    const fl = v.mesh.getObjectByName('flash'); if (fl) { fl.rotation.x = Math.random() * Math.PI; fl.scale.setScalar(0.8 + Math.random() * 0.5); }
    this.stats.shots++;
    if (this.stats.shots % 2 === 0) this.fx.tracer(o.clone().addScaledVector(d, 1.5 * this.u), end);
    if (!hit && tg <= R) this.fx.impact(end, this.T.at(end.x, end.z).water);
    this.shake = Math.max(this.shake, 0.08);
    if (hit) {
      this._send(v, true);   // the server checks the range from where the jeep is now
      this.link.send({ cmd: 'hit', weapon: w.id, serial: hit.serial, power: 1, damage: w.damage, x: hit.x, y: hit.z, z: hit.y });
      this.ex.lastReport = { weapon: w.id, serial: hit.serial };
    }
  }

  _sight(v) {
    if (v.onGround || v.rounds <= 0) { this.sightRing.visible = false; this.hud.sight(''); return; }
    const T = this.T, w = v.spec.weapon;
    let x = v.x, z = v.z, y = v.y, vx = Math.cos(v.pitch) * Math.cos(v.yaw) * v.v, vz = Math.cos(v.pitch) * Math.sin(v.yaw) * v.v, vy = v.vy;
    const h = 0.05;
    for (let t = 0; t < w.maxTimeS; t += h) {
      vy -= 9.81 * h;
      const k = 1 - BOMB_DRAG * Math.hypot(vx, vy, vz) * h; vx *= k; vy *= k; vz *= k;   // as the bomb falls (arms.js)
      x += vx * h * T.uh; z += vz * h * T.uh; y += vy * h * T.uv;
      const a = T.at(x, z), floor = a.water ? a.s : a.g;
      if (y <= floor) {
        this.sightRing.position.set(x, floor + 0.5 * this.u, z);
        this.sightRing.scale.setScalar(w.radiusM * T.uh);
        this.sightRing.visible = true;
        this.hud.sight(`bomb falls ${t.toFixed(1)} s · lands ${(this._distM(v.x, v.z, x, z)).toFixed(0)} m ahead`);
        return;
      }
    }
    this.sightRing.visible = false; this.hud.sight('');
  }

  _bomb(v) {
    const ex = this.ex, w = v.spec.weapon;
    if (this.bombCool > 0) return;
    if (v.onGround || v.agl < 15) { ex.hud.message('bombs only in the air', true, 1000); return; }
    if (v.rounds <= 0) { ex.hud.message('no bombs left: land and place a new plane (V)', true, 1500); return; }
    this.bombCool = w.cooldown;
    v.rounds--;
    const rack = v.mesh.getObjectByName(v.rounds % 2 ? 'bombL' : 'bombR') || v.mesh;
    const p = rack.getWorldPosition(new THREE.Vector3());
    const vel = new THREE.Vector3(Math.cos(v.pitch) * Math.cos(v.yaw) * v.v, v.vy, Math.cos(v.pitch) * Math.sin(v.yaw) * v.v);
    this._send(v, true);   // the launch point is on the path the server records
    const b = new Bomb(this.world.scene, w, bombMesh(), p, vel, this.T, this.u);
    b.launch = { x: this.T.wrap(v.x), z: this.T.wrap(v.z) }; b.wall0 = performance.now();
    this.shots.push(b);
    this.stats.bombs++;
  }

  _torpedo(v) {
    const ex = this.ex, w = v.spec.weapon, T = this.T;
    if (this.torpCool > 0) return;
    if (v.rounds <= 0) { ex.hud.message('no torpedoes left: place a new boat (V)', true, 1500); return; }
    // it runs the way the crosshair looks (the tubes swivel), within 70 degrees of the bow
    let da = Math.atan2(Math.sin(ex.yaw - v.yaw), Math.cos(ex.yaw - v.yaw));
    da = Math.max(-1.2, Math.min(1.2, da));
    const yaw = v.yaw + da;
    const muz = v.mesh.getObjectByName('muzzle').getWorldPosition(new THREE.Vector3());
    const depth = Math.max(w.depthMinM || 0.5, Math.min(this.torpDepth, T.at(muz.x, muz.z).depthM - 0.5));
    this.torpCool = w.cooldown;
    v.rounds--;
    v.flashT = performance.now();
    this._send(v, true);
    const t = new Torpedo(this.world.scene, this.fx, w, torpedoMesh(), T.wrap(muz.x), T.wrap(muz.z), yaw, depth, this.homing, T, this.u);
    t.launch = { x: T.wrap(v.x), z: T.wrap(v.z) }; t.wall0 = performance.now();
    this.shots.push(t);
    this.fx.splash(new THREE.Vector3(muz.x + Math.cos(yaw) * 2 * T.uh, T.at(muz.x, muz.z).s, muz.z + Math.sin(yaw) * 2 * T.uh), 1.2);
    this.stats.torpedoes++;
    ex.hud.message(`torpedo away · ${depth.toFixed(1)} m deep · ${this.homing ? 'homing' : 'straight'}`, false, 1200);
  }

  _shots(dt) {
    const keep = [];
    for (const s of this.shots) {
      const r = s instanceof Torpedo ? s.step(dt, this.world) : s.step(dt);
      if (!r) { keep.push(s); continue; }
      if (r.lost) { this.ex.hud.message('torpedo ran out', false, 1200); continue; }
      this._explode(s, r);
    }
    this.shots = keep;
  }

  /** A bomb or torpedo went off at r: show it, and tell the server (which decides what it did). */
  _explode(s, r) {
    const w = s.mode, T = this.T, ex = this.ex;
    const pos = new THREE.Vector3(r.x + Math.round((ex.pos.x - r.x) / T.W) * T.W, r.y, r.z + Math.round((ex.pos.z - r.z) / T.W) * T.W);
    if (w.underwater || r.water) this.fx.waterBlast(pos, w.radiusM, r.depthM || 0);
    if (!w.underwater) { if (r.water) this.fx.splash(pos, 3); else this.fx.explosion(pos, w.radiusM); }
    const dm = this._distM(ex.pos.x, ex.pos.z, r.x, r.z);
    this.shake = Math.max(this.shake, Math.min(2.5, 2.5 * (1 - dm / 1500)));
    const id = ++this.blastId;
    if (this.cur) this._send(this.cur, true);
    // the flight's time: the server times the person's path by its own clock, so a slow frame rate (whose frames are
    // capped at 0.1 s) must not make the flight look shorter than the wall time it took
    const t = Math.min(w.maxTimeS, Math.max(s.t, (performance.now() - s.wall0) / 1000));
    const msg = { cmd: 'blast', id, weapon: w.id, x: r.x, y: r.z, depth: r.depthM || 0, lx: s.launch.x, ly: s.launch.z, t };
    this.pendingBlasts.set(id, msg);
    (this.stats.explosions ||= []).push({ id, weapon: w.id, why: r.why || 'impact', depthM: r.depthM || 0, serial: r.serial || 0, t, x: r.x, z: r.z });
    this.link.send(msg);
  }

  _onBlast(r) {
    this.pendingBlasts.delete(r.id);
    this.stats.blasts.push(r); if (this.stats.blasts.length > 50) this.stats.blasts.shift();
    const ex = this.ex, name = r.weapon === 'torpedo' ? 'torpedo' : 'bomb';
    if (!r.ok) { ex.hud.message(`${name} not counted: ${r.result}`, true, 1500); return; }
    const v = this.list[r.weapon === 'torpedo' ? 'boat' : 'plane'];
    if (v && typeof r.rounds === 'number') v.rounds = Math.min(v.rounds, r.rounds);
    this.stats.kills += r.killed;
    if (r.killed || r.hurt) ex.hud.hitMarker(r.killed > 0);
    if (r.kills !== undefined) ex.killed = r.kills;
    ex.hud.message(r.killed || r.hurt ? `${name}: ${r.killed} killed · ${r.hurt} hurt${r.fires ? ' · fire!' : ''}` : `${name}: nothing hit${r.fires ? ' · the grass caught fire' : ''}`, false, 2200);
  }

  _crash(v) {
    const ex = this.ex, T = this.T;
    this.fx.explosion(v.mesh.position.clone(), 18);
    this.shake = 2;
    scorch(v.mesh);
    v.mesh.rotation.z += 0.4; v.mesh.rotation.x += 0.5;
    v.alive = false;
    this.wrecks.push(v); delete this.list[v.kind];
    setTimeout(() => { this.world.scene.remove(v.mesh); this.wrecks = this.wrecks.filter((q) => q !== v); }, 45000);
    const why = v.crashed;
    this.cur = null; this.firing = false;
    this.hud.driving(null); this.sightRing.visible = false;
    ex.world.camera.up.set(0, 1, 0);
    if (ex.vm) ex.vm.visible = true;
    // thrown clear, on the nearest dry ground
    let sx = v.x + 12 * T.uh, sz = v.z;
    for (const [px, pz] of this._rings(v.x, v.z, 10, 400, 4)) if (!T.at(px, pz).water) { sx = px; sz = pz; break; }
    ex.setPose(sx, sz, Math.atan2(v.z - sz, v.x - sx), -0.1);
    ex.hud.message(`CRASHED: ${why}<br><span style="font-size:12px">thrown clear of the wreck</span>`, true, 3500);
  }
}
