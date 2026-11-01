// Explore vehicles: cheap effects. Explosion flash and smoke, scorch marks, water columns, splashes, a boat's wake and a
// torpedo's bubbles, tracers. Shared geometries; one material per live effect (its own fading opacity); no lights (adding
// and removing lights recompiles every shader). Sizes are given in metres and drawn at u units per metre.

import * as THREE from 'three';

const SPHERE = new THREE.IcosahedronGeometry(1, 1);
const PUFF = new THREE.IcosahedronGeometry(1, 0);
const RING = new THREE.RingGeometry(0.8, 1, 24).rotateX(-Math.PI / 2);
const DISC = new THREE.CircleGeometry(1, 16).rotateX(-Math.PI / 2);
const COLUMN = new THREE.CylinderGeometry(0.55, 1, 1, 10, 1, true).translate(0, 0.5, 0);
const QUAD = new THREE.PlaneGeometry(1, 1).rotateX(-Math.PI / 2);
const MAX = 260;   // live effects at most (the oldest go first)

export class Fx {
  constructor(scene, u) {
    this.scene = scene; this.u = u;
    this.items = [];
  }

  _add(geo, matOpts, pos, scale, life, upd) {
    const mat = matOpts.lambert ? new THREE.MeshLambertMaterial({ color: matOpts.color, transparent: true, opacity: matOpts.opacity ?? 1, depthWrite: false })
      : new THREE.MeshBasicMaterial({ color: matOpts.color, transparent: true, opacity: matOpts.opacity ?? 1, depthWrite: false,
        blending: matOpts.add ? THREE.AdditiveBlending : THREE.NormalBlending, side: matOpts.side ?? THREE.FrontSide });
    const m = new THREE.Mesh(geo, mat);
    m.position.copy(pos); m.scale.setScalar(scale); m.frustumCulled = false; m.renderOrder = matOpts.order ?? 3;
    this.scene.add(m);
    const it = { m, t: 0, life, upd, o0: mat.opacity, s0: scale, v: new THREE.Vector3() };
    this.items.push(it);
    if (this.items.length > MAX) this._drop(this.items.shift());
    return it;
  }

  _drop(it) { this.scene.remove(it.m); it.m.material.dispose(); if (it.ownGeo) it.m.geometry.dispose(); }

  update(dt) {
    const keep = [];
    for (const it of this.items) {
      it.t += dt;
      const f = it.t / it.life;
      if (f >= 1) { this._drop(it); continue; }
      it.upd(it, f, dt);
      keep.push(it);
    }
    this.items = keep;
  }

  clear() { for (const it of this.items) this._drop(it); this.items = []; }

  /** A blast on land: a fireball, a shock ring, smoke rising and spreading, a scorch mark that lingers. */
  explosion(pos, radiusM) {
    const u = this.u, R = radiusM * u;
    this._add(SPHERE, { color: 0xffc061, add: true, opacity: 1, order: 6 }, pos, R * 0.15, 0.55, (it, f) => {
      it.m.scale.setScalar(R * (0.15 + 0.75 * Math.sqrt(f))); it.m.material.opacity = 1 - f; it.m.material.color.setHSL(0.1 - 0.08 * f, 1, 0.65 - 0.3 * f);
    });
    this._add(RING, { color: 0xfff1d0, add: true, opacity: 0.7, side: THREE.DoubleSide }, pos.clone().setY(pos.y + 0.3 * u), R * 0.2, 0.45, (it, f) => {
      it.m.scale.setScalar(R * (0.2 + 1.4 * f)); it.m.material.opacity = 0.7 * (1 - f);
    });
    for (let k = 0; k < 9; k++) {
      const a = (k / 9) * Math.PI * 2, r = R * 0.25 * Math.random();
      const p = pos.clone().add(new THREE.Vector3(Math.cos(a) * r, R * 0.1, Math.sin(a) * r));
      const it = this._add(PUFF, { color: k < 3 ? 0x2b2622 : 0x5d5751, lambert: true, opacity: 0.85 }, p, R * 0.18, 4 + Math.random() * 2, (q, f, dt) => {
        q.m.position.addScaledVector(q.v, dt); q.v.multiplyScalar(1 - 0.6 * dt);
        q.m.scale.setScalar(q.s0 * (1 + 2.5 * f)); q.m.material.opacity = 0.85 * (1 - f) * Math.min(1, q.t * 6);
      });
      it.v.set(Math.cos(a) * 3 * u, (5 + 4 * Math.random()) * u, Math.sin(a) * 3 * u);
      it.m.rotation.set(Math.random() * 3, Math.random() * 3, 0);
    }
    this._add(DISC, { color: 0x14100c, opacity: 0.75, order: 1 }, pos.clone().setY(pos.y + 0.15 * u), R * 0.6, 40, (it, f) => { it.m.material.opacity = 0.75 * (1 - f * f); });
  }

  /** A blast in or under water: a white column and spray, a ring of foam. depthM below the surface (pos is on it). */
  waterBlast(pos, radiusM, depthM = 0) {
    const u = this.u, R = radiusM * u, h = (depthM > 6 ? 18 : 30) * u * (radiusM / 15);
    this._add(COLUMN, { color: 0xf2f8ff, opacity: 0.9 }, pos, 1, 2.2, (it, f) => {
      const up = Math.sin(Math.min(1, f * 2.2) * Math.PI / 2) * (1 - Math.max(0, f - 0.5) * 1.4);
      it.m.scale.set(R * (0.35 + 0.4 * f), Math.max(1e-4, h * up), R * (0.35 + 0.4 * f)); it.m.material.opacity = 0.9 * (1 - f);
    });
    this._add(RING, { color: 0xffffff, opacity: 0.8, side: THREE.DoubleSide }, pos.clone().setY(pos.y + 0.1 * u), R * 0.3, 3, (it, f) => {
      it.m.scale.setScalar(R * (0.3 + 1.6 * Math.sqrt(f))); it.m.material.opacity = 0.8 * (1 - f);
    });
    for (let k = 0; k < 7; k++) {
      const a = Math.random() * Math.PI * 2;
      const it = this._add(PUFF, { color: 0xe9f1f6, lambert: true, opacity: 0.8 }, pos.clone(), R * 0.12, 1.8, (q, f, dt) => {
        q.v.y -= 9.81 * u * dt; q.m.position.addScaledVector(q.v, dt); q.m.scale.setScalar(q.s0 * (1 + f)); q.m.material.opacity = 0.8 * (1 - f);
      });
      it.v.set(Math.cos(a) * 6 * u, (14 + 8 * Math.random()) * u, Math.sin(a) * 6 * u);
    }
  }

  /** A splash: something small hitting the water (a bomb's impact adds waterBlast). */
  splash(pos, sizeM) {
    const u = this.u, s = sizeM * u;
    this._add(COLUMN, { color: 0xf4f9ff, opacity: 0.85 }, pos, 1, 0.9, (it, f) => {
      const up = Math.sin(f * Math.PI); it.m.scale.set(s * 0.6, Math.max(1e-4, s * 2.5 * up), s * 0.6); it.m.material.opacity = 0.85 * (1 - f);
    });
    this._add(RING, { color: 0xffffff, opacity: 0.7, side: THREE.DoubleSide }, pos.clone().setY(pos.y + 0.05 * u), s * 0.5, 1.6, (it, f) => {
      it.m.scale.setScalar(s * (0.5 + 2 * f)); it.m.material.opacity = 0.7 * (1 - f);
    });
  }

  /** A puff of dust (wheels in mud or dry dirt) or spray (wheels in water). */
  puff(pos, sizeM, color = 0x8a7a63) {
    const u = this.u;
    const it = this._add(PUFF, { color, lambert: true, opacity: 0.5 }, pos, sizeM * u, 1.2, (q, f, dt) => {
      q.m.position.addScaledVector(q.v, dt); q.m.scale.setScalar(q.s0 * (1 + 2 * f)); q.m.material.opacity = 0.5 * (1 - f);
    });
    it.v.set(0, 1.2 * u, 0);
  }

  /** A patch of wake foam on the water behind a boat (or a torpedo's bubbles), widening and fading. */
  wake(pos, yaw, widthM, life = 5) {
    const u = this.u, w = widthM * u;
    const it = this._add(QUAD, { color: 0xffffff, opacity: 0.55, order: 2 }, pos, 1, life, (q, f) => {
      q.m.scale.set(w * (1 + 2.5 * f), 1, w * (0.7 + 2.5 * f)); q.m.material.opacity = 0.55 * (1 - f);
    });
    it.m.rotation.y = -yaw; it.m.scale.set(w, 1, w * 0.7);
  }

  /** A tracer: a bright streak from a to b that lasts a few frames. */
  tracer(a, b) {
    const g = new THREE.BufferGeometry().setFromPoints([a, b]);
    const line = new THREE.Line(g, new THREE.LineBasicMaterial({ color: 0xffe08a, transparent: true, opacity: 0.95 }));
    line.frustumCulled = false;
    this.scene.add(line);
    this.items.push({ m: line, t: 0, life: 0.07, ownGeo: true, upd: (q, f) => { q.m.material.opacity = 0.95 * (1 - f); } });
    if (this.items.length > MAX) this._drop(this.items.shift());
  }

  /** A small impact: dust where a round hits the ground. */
  impact(pos, water) {
    if (water) this.splash(pos, 0.5); else this.puff(pos, 0.5, 0x7a6a55);
  }
}
