// Explore vehicles: bombs and torpedoes in flight, on the console. Where one explodes is reported to the server ('blast'),
// which checks it against the vehicle's recent path, the weapon's range and its flight time, and applies the damage to
// every animal in the blast radius (Player.Blast). Distances in metres; T is the terrain sampler of physics.js.

import * as THREE from 'three';

const G = 9.81;
const tmp = { x: 0, z: 0, gy: 0, cy: 0, r: 0 };

/** An animal's depth under the water (m), when the AGENTS records carry it (the respiration work, docs/ICD.md: a
 *  decoded `depthM` or `depth` on the agent); until then every swimmer is at the surface. */
export function animalDepth(e) {
  if (typeof e.depthM === 'number') return e.depthM;
  if (typeof e.depth === 'number') return e.depth;
  return 0;
}

/** Body half-length of an animal in metres, as the server counts it (Player.Blast: 0.25 M^(1/3)). */
const bodyM = (e) => 0.25 * Math.cbrt(Math.max(0.1, e.mass || 1));

export class Bomb {
  /** pos: view units; vel: m/s (the plane's velocity when it let go). */
  constructor(scene, mode, mesh, pos, vel, T, u) {
    this.mode = mode; this.T = T; this.u = u;
    this.pos = pos.clone(); this.vel = vel.clone(); this.t = 0; this.alive = true;
    this.launch = { x: T.wrap(pos.x), z: T.wrap(pos.z) };
    this.mesh = mesh; mesh.scale.setScalar(u * 1.6); mesh.position.copy(pos); scene.add(mesh); this.scene = scene;
  }

  /** Advance; returns the impact { x, y, z, water } (view units) once it meets the ground or the water. */
  step(dt) {
    const T = this.T, n = 4, h = dt / n;
    for (let k = 0; k < n; k++) {
      this.t += h;
      this.vel.y -= G * h;
      this.vel.multiplyScalar(1 - 0.0004 * this.vel.length() * h);   // a little drag
      this.pos.x += this.vel.x * h * T.uh; this.pos.z += this.vel.z * h * T.uh; this.pos.y += this.vel.y * h * T.uv;
      const a = T.at(this.pos.x, this.pos.z), floor = a.water ? a.s : a.g;
      if (this.pos.y <= floor || this.t > this.mode.maxTimeS) {
        this.dispose();
        return { x: T.wrap(this.pos.x), y: floor, z: T.wrap(this.pos.z), water: a.water && a.depthM > 0.5, depthM: 0 };
      }
    }
    this.mesh.position.copy(this.pos);
    const d = this.vel.clone().normalize();
    this.mesh.quaternion.setFromUnitVectors(new THREE.Vector3(1, 0, 0), new THREE.Vector3(d.x, d.y, d.z));
    return null;
  }

  dispose() { if (!this.alive) return; this.alive = false; this.scene.remove(this.mesh); }
}

export class Torpedo {
  /** Launched at (x, z) heading yaw, to run at setDepth m below the surface, homing weakly or straight. */
  constructor(scene, fx, mode, mesh, x, z, yaw, setDepth, homing, T, u) {
    this.mode = mode; this.fx = fx; this.T = T; this.u = u;
    this.x = x; this.z = z; this.yaw = yaw; this.depth = 0.3; this.setDepth = setDepth; this.homing = homing;
    this.t = 0; this.run = 0; this.alive = true; this.bubbleT = 0; this.seekT = 0; this.target = 0;
    this.launch = { x: T.wrap(x), z: T.wrap(z) };
    this.mesh = mesh; mesh.scale.setScalar(u); scene.add(mesh); this.scene = scene;
  }

  /** Advance; returns the explosion { x, y (the surface), z, depthM, why } or { lost: true } at the end of its run. */
  step(dt, world) {
    const T = this.T, m = this.mode, sp = m.speedMps;
    this.t += dt;
    if (this.t > m.maxTimeS || this.run > m.rangeM) { this.dispose(); return { lost: true }; }
    // dive to the set depth (2.5 m/s)
    this.depth += Math.max(-2.5 * dt, Math.min(2.5 * dt, this.setDepth - this.depth));
    // weak homing: a few times a second, the nearest animal ahead whose depth is within the blast's reach of its own
    if (this.homing && (this.seekT -= dt) <= 0) { this.seekT = 0.3; this.target = this._seek(world); }
    if (this.homing && this.target) {
      const e = world.agents.get(this.target);
      if (e) {
        world.creatureNow(this.target, e, tmp);
        let dx = tmp.x - this.x, dz = tmp.z - this.z; dx -= Math.round(dx / T.W) * T.W; dz -= Math.round(dz / T.W) * T.W;
        let da = Math.atan2(dz, dx) - this.yaw; da = Math.atan2(Math.sin(da), Math.cos(da));
        this.yaw += Math.max(-0.3 * dt, Math.min(0.3 * dt, da));   // 0.3 rad/s: it turns, but a fast animal can slip it
      } else this.target = 0;
    }
    const n = 3, h = dt / n;
    for (let k = 0; k < n; k++) {
      this.x = T.wrap(this.x + Math.cos(this.yaw) * sp * h * T.uh);
      this.z = T.wrap(this.z + Math.sin(this.yaw) * sp * h * T.uh);
      this.run += sp * h;
      const a = T.at(this.x, this.z);
      // the shore or the bottom rising above its depth: it hits it
      if (!a.water || a.depthM < this.depth) {
        this.dispose();
        return { x: this.x, z: this.z, y: a.water ? a.s : a.g, depthM: Math.max(m.depthMinM || 0.5, Math.min(this.depth, a.depthM || 0)), why: 'the shore' };
      }
      // an animal its depth overlaps, within reach of its nose
      const hit = this._contact(world);
      if (hit) { this.dispose(); return { x: this.x, z: this.z, y: a.s, depthM: this.depth, why: 'contact', serial: hit }; }
    }
    const a = T.at(this.x, this.z);
    this.mesh.position.set(this.x, a.s - this.depth * T.uv, this.z);
    this.mesh.rotation.set(0, -this.yaw, 0);
    // a line of bubbles on the surface over it
    if ((this.bubbleT -= dt) <= 0) { this.bubbleT = 0.1; this.fx.wake(new THREE.Vector3(this.x, a.s + 0.03 * T.uv, this.z), this.yaw, 0.9, 2.5); }
    return null;
  }

  _near(world, maxM, fn) {
    const T = this.T;
    for (const [serial, e] of world.agents) {
      if (world.dead.has(serial) || e.stage === undefined) continue;
      let dx = e.x - this.x, dz = e.y - this.z; dx -= Math.round(dx / T.W) * T.W; dz -= Math.round(dz / T.W) * T.W;
      const dm = Math.hypot(dx, dz) / T.uh;
      if (dm > maxM) continue;
      fn(serial, e, dx, dz, dm);
    }
  }

  _contact(world) {
    let got = 0;
    this._near(world, 12, (serial, e, dx, dz, dm) => {
      if (got) return;
      const a = this.T.at(e.x, e.y);
      if (!a.water) return;                               // an animal on land: the shore stops the torpedo first
      const reach = 2 + bodyM(e);
      if (dm <= reach && Math.abs(animalDepth(e) - this.depth) <= 2 + bodyM(e)) got = serial;
    });
    return got;
  }

  _seek(world) {
    let best = 0, bd = 1e9;
    const R = this.mode.radiusM || 14, fx = Math.cos(this.yaw), fz = Math.sin(this.yaw);
    this._near(world, 300, (serial, e, dx, dz, dm) => {
      if (dm < 1e-3) return;
      if ((dx * fx + dz * fz) / (dm * this.T.uh) < 0.6) return;   // ahead, within ~53 degrees
      if (!this.T.at(e.x, e.y).water) return;
      if (Math.abs(animalDepth(e) - this.setDepth) > R) return;
      if (dm < bd) { bd = dm; best = serial; }
    });
    return best;
  }

  dispose() { if (!this.alive) return; this.alive = false; this.scene.remove(this.mesh); }
}
