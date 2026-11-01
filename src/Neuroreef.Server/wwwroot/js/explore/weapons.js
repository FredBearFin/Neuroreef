// Explore mode weapons, on the console side: aiming against the creatures as they are drawn (world3d.creatureNow),
// the flight of thrown spears and arrows under gravity, and the meshes (the weapon in hand, projectiles in the air).
// The console only proposes a hit; the server checks it and applies the damage (Player.Hit). The weapon table itself
// (ranges, damage, speeds) comes from the server (meta.explore.weapons).

import * as THREE from 'three';

const G_MS2 = 9.81;
const tmp = { x: 0, z: 0, gy: 0, cy: 0, r: 0 };

/** The creature nearest along a ray (origin, unit dir, both THREE.Vector3) within maxT units, or null. */
export function rayCreatures(world, o, d, maxT, skip) {
  let best = null;
  for (const [serial, e] of world.agents) {
    if (world.hidden.has(e.sp) || world.dead.has(serial) || serial === skip) continue;
    // quick reject on the map distance before the exact test
    const W = world.W; let qx = e.x - o.x, qz = e.y - o.z; qx -= Math.round(qx / W) * W; qz -= Math.round(qz / W) * W;
    if (qx * qx + qz * qz > (maxT + 12) * (maxT + 12)) continue;
    world.creatureNow(serial, e, tmp);
    const t = raySphere(o, d, tmp.x, tmp.cy, tmp.z, tmp.r);
    if (t !== null && t <= maxT && (!best || t < best.t))
      best = { serial, e, t, x: o.x + d.x * t, y: o.y + d.y * t, z: o.z + d.z * t, d: Math.hypot(tmp.x - o.x, tmp.cy - o.y, tmp.z - o.z) };
  }
  return best;
}

function raySphere(o, d, cx, cy, cz, r) {
  const ox = o.x - cx, oy = o.y - cy, oz = o.z - cz;
  const b = ox * d.x + oy * d.y + oz * d.z, c = ox * ox + oy * oy + oz * oz - r * r;
  if (c <= 0) return 0;                 // inside the body: touching it
  const disc = b * b - c;
  if (disc < 0) return null;
  const t = -b - Math.sqrt(disc);
  return t >= 0 ? t : null;
}

/** Distance along a ray to where it meets the ground (Infinity within maxT if it does not). */
export function rayGround(world, o, d, maxT) {
  let t = 0, prev = 0;
  for (let i = 0; i < 400 && t <= maxT; i++) {
    const x = o.x + d.x * t, y = o.y + d.y * t, z = o.z + d.z * t;
    const g = world.groundWater(x, z)[0];
    if (y <= g) {
      // refine between the last step above and this one
      let a = prev, b = t;
      for (let k = 0; k < 10; k++) { const m = (a + b) / 2; if (o.y + d.y * m <= world.groundWater(o.x + d.x * m, o.z + d.z * m)[0]) b = m; else a = m; }
      return b;
    }
    prev = t;
    t += Math.max(0.01, Math.min(2, (y - g) * 0.5));
  }
  return Infinity;
}

/** A projectile in flight: a thrown spear or an arrow. Positions in units; it falls under gravity in units (g x upm). */
export class Projectile {
  constructor(scene, mode, power, pos, dir, upm, mesh) {
    this.mode = mode; this.power = power; this.upm = upm;
    this.pos = pos.clone(); this.start = pos.clone();
    this.vel = dir.clone().multiplyScalar(mode.speedMps * (mode.kind === 'bow' ? 0.35 + 0.65 * power : 1) * upm);
    this.mesh = mesh; this.scene = scene; scene.add(mesh);
    this.alive = true; this.stuckT = 0; this.flown = 0;
    this._orient();
  }

  _orient() {
    const v = this.vel.clone().normalize();
    this.mesh.position.copy(this.pos);
    this.mesh.quaternion.setFromUnitVectors(new THREE.Vector3(0, 0, 1), v);
  }

  /** Advance dt seconds; returns a hit {serial, e, x, y, z} when it strikes a creature, else null. */
  step(world, dt, now) {
    if (this.stuckT) { if (now - this.stuckT > 20000) this.dispose(); return null; }
    const maxRange = this.mode.rangeM * this.upm * 1.3;
    const n = 4, h = dt / n;
    for (let k = 0; k < n; k++) {
      const p0 = this.pos.clone();
      this.vel.y -= G_MS2 * this.upm * h;
      this.pos.addScaledVector(this.vel, h);
      const seg = this.pos.clone().sub(p0), len = seg.length();
      if (len <= 0) continue;
      seg.divideScalar(len);
      this.flown += len;
      const hit = rayCreatures(world, p0, seg, len);
      if (hit) { this.dispose(); return hit; }
      const g = world.groundWater(this.pos.x, this.pos.z)[0];
      if (this.pos.y <= g) { this.pos.y = g + 0.002; this.stuckT = now; this._orient(); return null; }   // stuck in the ground
      if (this.flown > maxRange) { this.dispose(); return null; }
    }
    this._orient();
    return null;
  }

  dispose() { if (!this.alive) return; this.alive = false; this.scene.remove(this.mesh); }
}

// ---- meshes. A metre is upm units (0.018): the weapons are drawn at true size, the projectiles twice it so they read
const mat = (c) => new THREE.MeshLambertMaterial({ color: c });

/** A projectile mesh pointing along +z, its tip at the origin's front. */
export function projectileMesh(kind, upm) {
  const g = new THREE.Group(), s = upm * 2;
  if (kind === 'thrown') {
    const shaft = new THREE.Mesh(new THREE.CylinderGeometry(0.02 * s, 0.02 * s, 2.2 * s, 5), mat(0x8a6a44)); shaft.rotation.x = Math.PI / 2; shaft.position.z = -1.1 * s;
    const tip = new THREE.Mesh(new THREE.ConeGeometry(0.05 * s, 0.25 * s, 5), mat(0xbfc6cc)); tip.rotation.x = Math.PI / 2; tip.position.z = 0.1 * s;
    g.add(shaft, tip);
  } else {
    const shaft = new THREE.Mesh(new THREE.CylinderGeometry(0.008 * s, 0.008 * s, 0.75 * s, 4), mat(0xc9b089)); shaft.rotation.x = Math.PI / 2; shaft.position.z = -0.37 * s;
    const fl = new THREE.Mesh(new THREE.BoxGeometry(0.06 * s, 0.002 * s, 0.12 * s), mat(0xe04848)); fl.position.z = -0.68 * s;
    const fl2 = fl.clone(); fl2.rotation.z = Math.PI / 2;
    const tip = new THREE.Mesh(new THREE.ConeGeometry(0.02 * s, 0.06 * s, 4), mat(0x9aa3aa)); tip.rotation.x = Math.PI / 2; tip.position.z = 0.02 * s;
    g.add(shaft, fl, fl2, tip);
  }
  return g;
}

/** The weapon in hand, as a child of the camera (camera looks down -z). */
export function viewModel(id, upm) {
  const g = new THREE.Group(), s = upm;
  if (id === 'spear') {
    const shaft = new THREE.Mesh(new THREE.CylinderGeometry(0.018 * s, 0.018 * s, 2.0 * s, 6), mat(0x8a6a44)); shaft.rotation.x = Math.PI / 2;
    const tip = new THREE.Mesh(new THREE.ConeGeometry(0.04 * s, 0.22 * s, 6), mat(0xc4ccd2)); tip.rotation.x = -Math.PI / 2; tip.position.z = -1.1 * s;
    g.add(shaft, tip); g.position.set(0.22 * s, -0.22 * s, -0.55 * s);
  } else if (id === 'bow') {
    // held upright to the left, its stave curving away from the string; the nocked arrow points ahead (-z)
    const R = 0.38 * s, a0 = Math.PI * 0.62, a1 = Math.PI * 0.76;
    const arc = new THREE.Mesh(new THREE.TorusGeometry(R, 0.012 * s, 5, 24, a1), mat(0x6b4a2b)); arc.rotation.z = a0;
    const tipX = R * Math.cos(a0), tipY = R * Math.sin(a0);
    const str = new THREE.Mesh(new THREE.CylinderGeometry(0.002 * s, 0.002 * s, 2 * tipY, 3), mat(0xe8e2d0)); str.name = 'string'; str.position.x = tipX;
    g.add(arc, str); g.rotation.y = 0.35; g.position.set(0.02 * s, -0.1 * s, -0.75 * s);
    const arrow = projectileMesh('bow', upm / 2); arrow.name = 'arrow'; arrow.rotation.y = Math.PI; arrow.position.set(tipX, 0, 0); g.add(arrow);
  } else {
    const body = new THREE.Mesh(new THREE.BoxGeometry(0.05 * s, 0.08 * s, 0.7 * s), mat(0x2b2f33));
    const barrel = new THREE.Mesh(new THREE.CylinderGeometry(0.012 * s, 0.012 * s, 0.5 * s, 6), mat(0x16191c)); barrel.rotation.x = Math.PI / 2; barrel.position.z = -0.55 * s;
    const stock = new THREE.Mesh(new THREE.BoxGeometry(0.045 * s, 0.12 * s, 0.3 * s), mat(0x5b3d24)); stock.position.set(0, -0.04 * s, 0.4 * s);
    g.add(body, barrel, stock); g.position.set(0.18 * s, -0.16 * s, -0.5 * s);
  }
  g.traverse((o) => { if (o.isMesh) { o.renderOrder = 20; o.frustumCulled = false; } });
  return g;
}
