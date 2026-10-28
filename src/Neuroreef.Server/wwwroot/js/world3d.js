// The 3D view: terrain, water, vegetation, fire, and every animal as a per-species low-poly model.
// World x maps to three.js x, world y maps to three.js z, height is three.js y. The world is a torus:
// the terrain is drawn as a 3x3 tiling around the camera, and every animal is drawn at its copy nearest
// to the camera.

import * as THREE from 'three';
import { OrbitControls } from '../lib/OrbitControls.js';
import { agentAt } from './net.js';

const RELIEF = 55;           // world units of height between lake level and the highest peak

export class World3D {
  constructor(canvas, meta) {
    this.meta = meta;
    this.W = meta.world;
    this.canvas = canvas;
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, matchMedia('(max-width: 900px)').matches ? 1.25 : 2));
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    this.scene = new THREE.Scene();
    this.scene.background = new THREE.Color(0x0b1622);
    this.scene.fog = new THREE.Fog(0x0b1622, 250, 900);
    this.camera = new THREE.PerspectiveCamera(55, 1, 0.3, 4000);
    this.camera.position.set(this.W / 2 - 260, 260, this.W / 2 + 260);
    this.controls = new OrbitControls(this.camera, canvas);
    this.controls.target.set(this.W / 2, 0, this.W / 2);
    this.controls.enableDamping = true;
    this.controls.maxPolarAngle = Math.PI * 0.47;
    this.controls.minDistance = 8; this.controls.maxDistance = 1400;

    const hemi = new THREE.HemisphereLight(0xcfe6ff, 0x3a2f22, 1.1);
    const sun = new THREE.DirectionalLight(0xfff1dd, 1.6);
    sun.position.set(0.4, 1, 0.25);
    this.scene.add(hemi, sun);
    this.sun = sun;

    this.mode = 'overview';
    this.agents = new Map();          // serial -> state for interpolation
    this.frameT = 0; this.frameDt = 50;
    this.hidden = new Set();          // species hidden by the legend
    this.selected = 0;
    this.perceived = [];
    this.tmp = {};
    this._buildSpecies();
    this._buildMarkers();
    this.resize();
    addEventListener('resize', () => this.resize());
  }

  resize() {
    const r = this.canvas.getBoundingClientRect();
    this.renderer.setSize(r.width, r.height, false);
    this.camera.aspect = r.width / Math.max(1, r.height);
    this.camera.updateProjectionMatrix();
  }

  // ------------------------------------------------------------------ terrain and water

  setWorld(w) {
    this.world = w;
    const fw = w.fw, N = fw + 1, S = this.W / fw;
    this.cellH = new Float32Array(fw * fw);
    for (let i = 0; i < fw * fw; i++) {
      const e = w.elev[i] / 255, isWater = w.cell[i] !== 1;
      let h = (e - w.sea) / (1 - w.sea) * RELIEF;
      if (isWater) h = w.sea > e ? (e - w.sea) * RELIEF * 1.6 - 0.6 : h - 1.4;   // lake bed / river bed
      this.cellH[i] = h;
    }
    const pos = new Float32Array(N * N * 3), wpos = new Float32Array(N * N * 3);
    for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) {
      const i = (y % fw) * fw + (x % fw), k = (y * N + x) * 3;
      pos[k] = x * S; pos[k + 1] = this.cellH[i]; pos[k + 2] = y * S;
      const water = w.cell[i] !== 1;
      const surf = water ? (w.elev[i] / 255 < w.sea ? 0 : this.cellH[i] + 1.0) : this.cellH[i] - 2;
      wpos[k] = x * S; wpos[k + 1] = surf; wpos[k + 2] = y * S;
    }
    const idx = [];
    for (let y = 0; y < fw; y++) for (let x = 0; x < fw; x++) {
      const a = y * N + x, b = a + 1, c = a + N, d = c + 1;
      idx.push(a, c, b, b, c, d);
    }
    const tg = new THREE.BufferGeometry();
    tg.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    tg.setIndex(idx);
    tg.computeVertexNormals();
    this.tColor = new Float32Array(N * N * 3).fill(0.4);
    tg.setAttribute('color', new THREE.BufferAttribute(this.tColor, 3));
    const wg = new THREE.BufferGeometry();
    wg.setAttribute('position', new THREE.BufferAttribute(wpos, 3));
    wg.setIndex(idx);
    wg.computeVertexNormals();
    this.wColor = new Float32Array(N * N * 4);
    wg.setAttribute('color', new THREE.BufferAttribute(this.wColor, 4));
    const tm = new THREE.MeshLambertMaterial({ vertexColors: true });
    const wm = new THREE.MeshPhongMaterial({ vertexColors: true, transparent: true, shininess: 90, specular: 0x6688aa, depthWrite: false });
    this.tiles = [];
    for (let k = 0; k < 9; k++) {
      const t = new THREE.Mesh(tg, tm), wt = new THREE.Mesh(wg, wm);
      wt.renderOrder = 2;
      this.scene.add(t, wt);
      this.tiles.push([t, wt]);
    }
    this.N = N;
    this._paint(null);
  }

  /** Ground or water surface height at a world position (bilinear on cell heights). */
  heightAt(x, y, swim) {
    const w = this.world; if (!w) return 0;
    const fw = w.fw, S = this.W / fw;
    const gx = ((x / S) % fw + fw) % fw, gy = ((y / S) % fw + fw) % fw;
    const x0 = Math.floor(gx), y0 = Math.floor(gy), tx = gx - x0, ty = gy - y0;
    const x1 = (x0 + 1) % fw, y1 = (y0 + 1) % fw;
    const H = this.cellH;
    const h = (H[y0 * fw + x0] * (1 - tx) + H[y0 * fw + x1] * tx) * (1 - ty) + (H[y1 * fw + x0] * (1 - tx) + H[y1 * fw + x1] * tx) * ty;
    const c = w.cell[y0 * fw + x0];
    if (c !== 1) {
      const surf = w.elev[y0 * fw + x0] / 255 < w.sea ? 0 : H[y0 * fw + x0] + 1.0;
      return swim ? surf - 0.25 : Math.max(h, surf - 0.6);
    }
    return h;
  }

  setFields(f) { this.fields = f; this._paint(f); this._plants(true); }

  _layer(name) { return this.meta.fieldLayers.indexOf(name); }

  _paint(f) {
    const w = this.world; if (!w) return;
    const fw = w.fw, N = this.N, C = this.tColor, WC = this.wColor;
    const d = f ? f.d : 0, L = f ? (n) => f.data.subarray(this._layer(n) * d * d, (this._layer(n) + 1) * d * d) : null;
    const grass = f && L('grass'), shrub = f && L('shrub'), tree = f && L('tree'), fire = f && L('fire'), moist = f && L('moisture'),
          carrion = f && L('carrion'), algae = f && L('algae'), phyto = f && L('phyto'), nectar = f && L('nectar');
    for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) {
      const cx = x % fw, cy = y % fw, i = cy * fw + cx, k = y * N + x;
      const fi = f ? (cy >> 1) * d + (cx >> 1) : 0;
      const e = w.elev[i] / 255;
      let r, g, b;
      if (!f) { r = 0.35 + e * 0.2; g = 0.33 + e * 0.15; b = 0.25; }
      else {
        const m = moist[fi] / 255, G = grass[fi] / 255, S = shrub[fi] / 255, T = tree[fi] / 255;
        // bare soil: dry tan to moist dark earth; rock on high ground
        r = 0.50 - 0.20 * m; g = 0.42 - 0.17 * m; b = 0.30 - 0.12 * m;
        const rock = Math.max(0, (e - 0.82) * 4); r += (0.55 - r) * rock; g += (0.55 - g) * rock; b += (0.56 - b) * rock;
        const dryG = 1 - m;
        const gr = 0.30 + 0.30 * dryG, gg = 0.58 - 0.12 * dryG, gb = 0.18;      // green grass, yellowing when dry
        r += (gr - r) * G; g += (gg - g) * G; b += (gb - b) * G;
        r += (0.30 - r) * S; g += (0.40 - g) * S; b += (0.16 - b) * S;
        r += (0.10 - r) * T * 0.85; g += (0.26 - g) * T * 0.85; b += (0.12 - b) * T * 0.85;
        const nc = nectar[fi] / 255; r += nc * 0.25; b += nc * 0.18;          // flowers
        const ca = carrion[fi] / 255; r += (0.35 - r) * ca * 0.5; g *= 1 - ca * 0.4; b *= 1 - ca * 0.4;
        if (fire[fi]) { r = 1.0; g = 0.42; b = 0.08; }
      }
      C[k * 3] = r; C[k * 3 + 1] = g; C[k * 3 + 2] = b;
      const water = w.cell[i] !== 1;
      const depth = w.depth[i] / 255;
      const al = f ? algae[fi] / 255 : 0, ph = f ? phyto[fi] / 255 : 0;
      // shallow water is turquoise, deep water dark blue; algae and plankton tint it green
      const dd = Math.min(1, depth * 1.5);
      WC[k * 4] = 0.10 - 0.07 * dd + 0.06 * al; WC[k * 4 + 1] = 0.38 - 0.24 * dd + 0.18 * al + 0.10 * ph; WC[k * 4 + 2] = 0.44 - 0.16 * dd + 0.02 * ph;
      WC[k * 4 + 3] = water ? 0.72 + 0.2 * dd : 0;
    }
    this.tiles[0][0].geometry.attributes.color.needsUpdate = true;
    this.tiles[0][1].geometry.attributes.color.needsUpdate = true;
  }

  // ------------------------------------------------------------------ vegetation instances near the camera

  _plants(force) {
    const f = this.fields; if (!f || !this.world) return;
    const c = this.controls.target, R = this.mode === 'overview' ? 380 : 220;
    if (!force && this._plantC && Math.hypot(c.x - this._plantC.x, c.z - this._plantC.z) < 50) return;
    this._plantC = c.clone();
    if (!this.trees) {
      this.trees = new THREE.InstancedMesh(treeGeometry(), new THREE.MeshLambertMaterial({ vertexColors: true }), 14000);
      this.shrubs = new THREE.InstancedMesh(new THREE.IcosahedronGeometry(1, 0), new THREE.MeshLambertMaterial({ color: 0x5a6e2a, flatShading: true }), 9000);
      this.flames = new THREE.InstancedMesh(new THREE.ConeGeometry(1, 3, 5), new THREE.MeshBasicMaterial({ color: 0xff7a1a, transparent: true, opacity: 0.85 }), 1500);
      this.scene.add(this.trees, this.shrubs, this.flames);
    }
    const d = f.d, cs = this.W / d;
    const T = f.data.subarray(this._layer('tree') * d * d), S = f.data.subarray(this._layer('shrub') * d * d), F = f.data.subarray(this._layer('fire') * d * d);
    const m = new THREE.Matrix4(), q = new THREE.Quaternion(), p = new THREE.Vector3(), s = new THREE.Vector3(), up = new THREE.Vector3(0, 1, 0);
    let nt = 0, ns = 0, nf = 0;
    const r0 = Math.floor(R / cs);
    const ccx = Math.floor(c.x / cs), ccy = Math.floor(c.z / cs);
    for (let dy = -r0; dy <= r0; dy++) for (let dx = -r0; dx <= r0; dx++) {
      if (dx * dx + dy * dy > r0 * r0) continue;
      const gx = ccx + dx, gy = ccy + dy, fx = ((gx % d) + d) % d, fy = ((gy % d) + d) % d, fi = fy * d + fx;
      const t = T[fi] / 255, sh = S[fi] / 255;
      let hsh = hash2(fx, fy);
      const ntree = Math.floor(t * 3.2 + (hsh & 255) / 256);
      for (let k = 0; k < ntree && nt < 14000; k++) {
        hsh = hash1(hsh);
        const x = (gx + ((hsh & 1023) / 1024)) * cs, y = (gy + (((hsh >> 10) & 1023) / 1024)) * cs;
        const h = this.heightAt(x, y);
        if (this.world.cell[this._cellOf(x, y)] !== 1) continue;
        const sc = 1.6 + ((hsh >> 20) & 15) / 8;
        p.set(x, h, y); q.setFromAxisAngle(up, (hsh & 63) / 10); s.set(sc, sc * (0.9 + ((hsh >> 24) & 7) / 14), sc);
        this.trees.setMatrixAt(nt++, m.compose(p, q, s));
      }
      const nsh = Math.floor(sh * 4 + ((hsh >> 8) & 255) / 256);
      for (let k = 0; k < nsh && ns < 9000; k++) {
        hsh = hash1(hsh + 17);
        const x = (gx + ((hsh & 1023) / 1024)) * cs, y = (gy + (((hsh >> 10) & 1023) / 1024)) * cs;
        if (this.world.cell[this._cellOf(x, y)] !== 1) continue;
        const sc = 0.7 + ((hsh >> 20) & 15) / 20;
        p.set(x, this.heightAt(x, y) + sc * 0.4, y); q.identity(); s.set(sc * 1.3, sc, sc * 1.3);
        this.shrubs.setMatrixAt(ns++, m.compose(p, q, s));
      }
      if (F[fi] && nf < 1500) {
        for (let k = 0; k < 3 && nf < 1500; k++) {
          hsh = hash1(hsh + 99);
          const x = (gx + ((hsh & 1023) / 1024)) * cs, y = (gy + (((hsh >> 10) & 1023) / 1024)) * cs;
          p.set(x, this.heightAt(x, y) + 1.2, y); q.identity(); s.set(1, 1, 1);
          this.flames.setMatrixAt(nf++, m.compose(p, q, s));
        }
      }
    }
    this.trees.count = nt; this.shrubs.count = ns; this.flames.count = nf;
    this.trees.instanceMatrix.needsUpdate = true; this.shrubs.instanceMatrix.needsUpdate = true; this.flames.instanceMatrix.needsUpdate = true;
  }

  _cellOf(x, y) {
    const fw = this.world.fw, S = this.W / fw;
    const cx = ((Math.floor(x / S) % fw) + fw) % fw, cy = ((Math.floor(y / S) % fw) + fw) % fw;
    return cy * fw + cx;
  }

  // ------------------------------------------------------------------ animals

  _buildSpecies() {
    this.spMeshes = [];
    for (const sp of this.meta.species) {
      const geo = creatureGeometry(sp.shape);
      const mat = new THREE.MeshLambertMaterial({ color: new THREE.Color(sp.color), flatShading: true,
        transparent: sp.shape === 15, opacity: sp.shape === 15 ? 0.7 : 1, emissive: new THREE.Color(sp.color).multiplyScalar(0.18) });
      const mesh = new THREE.InstancedMesh(geo, mat, 512);
      mesh.count = 0; mesh.frustumCulled = false;
      this.scene.add(mesh);
      this.spMeshes.push({ mesh, cap: 512, geo, mat, size: sp.swarm ? 1.6 : 1.5 * Math.cbrt(sp.mass), fly: sp.loco === 'Fly', swim: sp.loco === 'Swim', juvSwim: sp.juvLoco === 'Swim' });
    }
    this.eggs = new THREE.InstancedMesh(new THREE.SphereGeometry(0.35, 6, 4), new THREE.MeshLambertMaterial({ color: 0xffffff }), 4096);
    this.eggs.count = 0; this.eggs.frustumCulled = false;
    this.eggs.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(4096 * 3), 3);
    this.scene.add(this.eggs);
  }

  _grow(k, need) {
    const s = this.spMeshes[k];
    let cap = s.cap; while (cap < need) cap *= 2;
    this.scene.remove(s.mesh); s.mesh.dispose();
    s.mesh = new THREE.InstancedMesh(s.geo, s.mat, cap); s.mesh.frustumCulled = false; s.cap = cap;
    this.scene.add(s.mesh);
  }

  setAgents(a) {
    const now = performance.now();
    this.frameDt = Math.min(250, Math.max(16, now - this.frameT)); this.frameT = now;
    const gen = (this._gen = (this._gen || 0) + 1), W = this.W, o = this.tmp;
    for (let k = 0; k < a.n; k++) {
      agentAt(a, k, W, o);
      let e = this.agents.get(o.serial);
      if (!e) { e = { x: o.x, y: o.y, h: o.h, px: o.x, py: o.y, ph: o.h }; this.agents.set(o.serial, e); }
      else { e.px = e.x; e.py = e.y; e.ph = e.h; e.x = o.x; e.y = o.y; e.h = o.h; }
      e.sp = o.sp; e.stage = o.stage; e.acts = o.acts; e.scan = o.scan; e.rest = o.rest; e.energy = o.energy; e.gen = gen; e.carry = o.carrying;
    }
    for (const [s, e] of this.agents) if (e.gen !== gen) this.agents.delete(s);
    this.count = a.n;
  }

  /** Interpolated, camera-relative position of an agent. */
  _pos(e, alpha, cx, cz) {
    const W = this.W;
    let dx = e.x - e.px, dy = e.y - e.py;
    if (dx > W / 2) dx -= W; else if (dx < -W / 2) dx += W;
    if (dy > W / 2) dy -= W; else if (dy < -W / 2) dy += W;
    let x = e.px + dx * alpha, y = e.py + dy * alpha;
    x += Math.round((cx - x) / W) * W; y += Math.round((cz - y) / W) * W;
    let dh = e.h - e.ph; if (dh > Math.PI) dh -= 2 * Math.PI; else if (dh < -Math.PI) dh += 2 * Math.PI;
    return [x, y, e.ph + dh * alpha];
  }

  _buildMarkers() {
    const ring = new THREE.RingGeometry(1.0, 1.25, 32); ring.rotateX(-Math.PI / 2);
    this.selRing = new THREE.Mesh(ring, new THREE.MeshBasicMaterial({ color: 0xffffff, transparent: true, opacity: 0.9, depthTest: false }));
    this.selRing.renderOrder = 5; this.selRing.visible = false;
    const beam = new THREE.CylinderGeometry(0.06, 0.06, 30, 6); beam.translate(0, 15, 0);
    this.selBeam = new THREE.Mesh(beam, new THREE.MeshBasicMaterial({ color: 0xffffff, transparent: true, opacity: 0.35 }));
    this.selBeam.visible = false;
    this.fov = new THREE.Mesh(new THREE.CircleGeometry(1, 48, 0, 1), new THREE.MeshBasicMaterial({ color: 0x9fe8ff, transparent: true, opacity: 0.10, side: THREE.DoubleSide, depthWrite: false }));
    this.fov.visible = false; this.fov.renderOrder = 3;
    const pr = new THREE.RingGeometry(0.9, 1.15, 20); pr.rotateX(-Math.PI / 2);
    this.percMesh = new THREE.InstancedMesh(pr, new THREE.MeshBasicMaterial({ transparent: true, opacity: 0.85, depthTest: false }), 128);
    this.percMesh.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(128 * 3), 3);
    this.percMesh.count = 0; this.percMesh.renderOrder = 6; this.percMesh.frustumCulled = false;
    this.scene.add(this.selRing, this.selBeam, this.fov, this.percMesh);
  }

  setMode(m) {
    this.mode = m;
    this.controls.enabled = m === 'overview';
    this.camera.fov = m === 'ride' ? 78 : 55; this.camera.updateProjectionMatrix();
    this.scene.fog.near = m === 'ride' ? 60 : 250; this.scene.fog.far = m === 'ride' ? 380 : 900;
    if (m === 'overview' && this._selPos) this.controls.target.set(this._selPos[0], 0, this._selPos[1]);
    this._plants(true);
  }

  /** Pick the animal nearest to where the screen point meets the ground. */
  pick(clientX, clientY) {
    const r = this.canvas.getBoundingClientRect();
    const ndc = new THREE.Vector2(((clientX - r.left) / r.width) * 2 - 1, -((clientY - r.top) / r.height) * 2 + 1);
    const ray = new THREE.Raycaster(); ray.setFromCamera(ndc, this.camera);
    // march the ray to the ground
    const o = ray.ray.origin, d = ray.ray.direction;
    let t = 0, hit = null;
    for (let i = 0; i < 600; i++) {
      const p = o.clone().addScaledVector(d, t);
      if (p.y <= this.heightAt(p.x, p.z) + 1.5) { hit = p; break; }
      t += Math.max(0.5, (p.y - this.heightAt(p.x, p.z)) * 0.5);
    }
    if (!hit) return 0;
    let best = 0, bd = 1e9; const W = this.W;
    for (const [s, e] of this.agents) {
      if (e.stage === 0 || this.hidden.has(e.sp)) continue;
      let dx = e.x - hit.x, dy = e.y - hit.z;
      dx -= Math.round(dx / W) * W; dy -= Math.round(dy / W) * W;
      const dd = dx * dx + dy * dy;
      if (dd < bd) { bd = dd; best = s; }
    }
    const reach = Math.max(6, t * 0.03);
    return bd < reach * reach ? best : 0;
  }

  render(probe) {
    if (!this.tiles) { this.renderer.render(this.scene, this.camera); return; }
    const now = performance.now();
    const alpha = Math.min(1, (now - this.frameT) / this.frameDt);
    const W = this.W;
    // camera anchor for wrapping
    const sel = this.selected ? this.agents.get(this.selected) : null;
    let selP = null;
    const c = this.controls.target;
    if (sel) selP = this._pos(sel, alpha, c.x, c.z);
    this._selPos = selP;

    if (this.mode !== 'overview' && selP) {
      const sp = this.meta.species[sel.sp], sz = this.spMeshes[sel.sp].size * (sel.stage === 1 ? 0.6 : 1);
      const gh = this._height(sel, selP[0], selP[1]);
      const fx = Math.cos(selP[2]), fz = Math.sin(selP[2]);
      this._camH = this._camH === undefined ? selP[2] : lerpAngle(this._camH, selP[2], 0.08);
      const hx = Math.cos(this._camH), hz = Math.sin(this._camH);
      if (this.mode === 'follow') {
        const dist = 10 + sz * 5, up = 5 + sz * 2.5;
        const want = new THREE.Vector3(selP[0] - hx * dist, gh + up, selP[1] - hz * dist);
        want.y = Math.max(want.y, this.heightAt(want.x, want.z) + 2);
        this.camera.position.lerp(want, 0.12);
        this.camera.lookAt(selP[0], gh + sz * 0.6, selP[1]);
      } else {
        const eye = new THREE.Vector3(selP[0] + fx * sz * 0.6, gh + Math.max(0.9, sz * 0.9), selP[1] + fz * sz * 0.6);
        this.camera.position.lerp(eye, 0.35);
        this.camera.lookAt(eye.x + hx * 20, eye.y - 1.5, eye.z + hz * 20);
      }
      c.set(selP[0], 0, selP[1]);
    } else {
      // keep the orbit target inside the base tile; move the camera with it
      if (c.x < 0 || c.x >= W || c.z < 0 || c.z >= W) {
        const sx = -Math.floor(c.x / W) * W, sz = -Math.floor(c.z / W) * W;
        c.x += sx; c.z += sz; this.camera.position.x += sx; this.camera.position.z += sz;
      }
      this.controls.update();
    }
    const cx = c.x, cz = c.z;

    // terrain tiles around the anchor
    const tx = Math.floor(cx / W), tz = Math.floor(cz / W);
    let k = 0;
    for (let dz = -1; dz <= 1; dz++) for (let dx = -1; dx <= 1; dx++) {
      const [t, wt] = this.tiles[k++] || [];
      if (!t) continue;
      t.position.set((tx + dx) * W, 0, (tz + dz) * W); wt.position.copy(t.position);
      // draw a tile only if some of it is within fog distance of the camera
      const qx = Math.max(t.position.x, Math.min(this.camera.position.x, t.position.x + W));
      const qz = Math.max(t.position.z, Math.min(this.camera.position.z, t.position.z + W));
      const vis = Math.hypot(qx - this.camera.position.x, qz - this.camera.position.z) < this.scene.fog.far;
      t.visible = wt.visible = vis;
    }
    this._plants(false);
    if (this.flames) { this.flames.material.opacity = 0.65 + 0.3 * Math.sin(now / 90); }

    // animals
    const m = new THREE.Matrix4(), q = new THREE.Quaternion(), p = new THREE.Vector3(), s = new THREE.Vector3(), up = new THREE.Vector3(0, 1, 0);
    const counts = new Int32Array(this.spMeshes.length);
    for (const e of this.agents.values()) if (e.stage !== 0) counts[e.sp]++;
    for (let i = 0; i < this.spMeshes.length; i++) if (counts[i] > this.spMeshes[i].cap) this._grow(i, counts[i]);
    counts.fill(0);
    let ne = 0;
    const eggCol = new THREE.Color();
    const t = now / 1000;
    for (const [serial, e] of this.agents) {
      if (this.hidden.has(e.sp)) continue;
      const [x, y, h] = this._pos(e, alpha, cx, cz);
      const S = this.spMeshes[e.sp];
      if (e.stage === 0) {
        if (ne >= 4096) continue;
        p.set(x, this.heightAt(x, y) + 0.3, y); q.identity(); s.set(1, 0.8, 1);
        this.eggs.setMatrixAt(ne, m.compose(p, q, s));
        this.eggs.setColorAt(ne, eggCol.set(this.meta.species[e.sp].color).lerp(new THREE.Color(1, 1, 1), 0.6));
        ne++; continue;
      }
      const sc = S.size * (e.stage === 1 ? 0.55 : 1);
      let gy = this._height(e, x, y);
      if (S.fly && !(e.rest)) gy += 3.5 + Math.sin(t * 3 + serial) * 0.6;
      p.set(x, gy, y);
      q.setFromAxisAngle(up, -h);
      s.set(sc, sc * (e.rest ? 0.7 : 1), sc);
      S.mesh.setMatrixAt(counts[e.sp]++, m.compose(p, q, s));
    }
    for (let i = 0; i < this.spMeshes.length; i++) {
      const M = this.spMeshes[i].mesh; M.count = counts[i]; M.instanceMatrix.needsUpdate = true;
    }
    this.eggs.count = ne; this.eggs.instanceMatrix.needsUpdate = true; if (this.eggs.instanceColor) this.eggs.instanceColor.needsUpdate = true;

    // selection marker, field of view and perceived animals
    if (sel && selP) {
      const sz = this.spMeshes[sel.sp].size;
      const gh = this._height(sel, selP[0], selP[1]);
      this.selRing.visible = this.mode !== 'ride';
      this.selRing.position.set(selP[0], gh + 0.2, selP[1]); this.selRing.scale.setScalar(sz * 1.4 + 0.8 + 0.2 * Math.sin(now / 200));
      this.selBeam.visible = this.mode === 'overview'; this.selBeam.position.set(selP[0], gh, selP[1]);
      const sp = this.meta.species[sel.sp];
      const R = probe && probe.serial === this.selected ? probe.sight : sp.sight;
      const fov = (sel.scan ? 360 : sp.fov) * Math.PI / 180;
      this.fov.visible = this.mode !== 'overview';
      if (this._fovKey !== R + ':' + fov) {
        this._fovKey = R + ':' + fov;
        this.fov.geometry.dispose();
        this.fov.geometry = new THREE.CircleGeometry(R, 48, -fov / 2, fov); this.fov.geometry.rotateX(-Math.PI / 2);
      }
      this.fov.position.set(selP[0], gh + 0.35, selP[1]); this.fov.rotation.set(0, -selP[2], 0);
      this._perceive(sel, selP, R, fov, alpha, cx, cz);
    } else { this.selRing.visible = this.selBeam.visible = this.fov.visible = false; this.percMesh.count = 0; }

    this.renderer.render(this.scene, this.camera);
  }

  _height(e, x, y) {
    const S = this.spMeshes[e.sp];
    const swim = e.stage === 1 ? S.juvSwim : S.swim;
    return this.heightAt(x, y, swim);
  }

  /** Mark the animals the selected one can see: kin blue, smaller green, larger red, eggs white. */
  _perceive(sel, selP, R, fov, alpha, cx, cz) {
    if (this.mode === 'overview') { this.percMesh.count = 0; return; }
    const sp = this.meta.species, me = sp[sel.sp], myMass = (me.swarm ? me.unitMass : me.mass) * (sel.stage === 1 ? me.birth + 0.4 : 1);
    const m = new THREE.Matrix4(), q = new THREE.Quaternion(), p = new THREE.Vector3(), s = new THREE.Vector3(), col = new THREE.Color();
    let n = 0; const fx = Math.cos(selP[2]), fz = Math.sin(selP[2]);
    const list = [];
    for (const [ser, e] of this.agents) {
      if (ser === this.selected || n >= 128) continue;
      const [x, y] = this._pos(e, alpha, cx, cz);
      const dx = x - selP[0], dy = y - selP[1], d = Math.hypot(dx, dy);
      if (d > R || d < 0.01) continue;
      if (fov < 6.2 && (dx * fx + dy * fz) / d < Math.cos(fov / 2)) continue;
      const o = sp[e.sp]; const mj = (o.swarm ? o.unitMass : o.mass) * (e.stage === 1 ? o.birth + 0.4 : 1);
      let kind;
      if (e.stage === 0) { col.set(0xffffff); kind = 'egg'; }
      else if (e.sp === sel.sp) { col.set(0x5aa8ff); kind = 'kin'; }
      else if (mj * 1.15 < myMass) { col.set(0x5dff8a); kind = 'smaller'; }
      else if (mj > myMass * 1.15) { col.set(0xff4d5e); kind = 'larger'; }
      else { col.set(0xffd34d); kind = 'similar'; }
      const sz = this.spMeshes[e.sp].size * (e.stage === 1 ? 0.55 : 1) * 1.3 + 0.6;
      p.set(x, this._height(e, x, y) + 0.25, y); q.identity(); s.set(sz, 1, sz);
      this.percMesh.setMatrixAt(n, m.compose(p, q, s)); this.percMesh.setColorAt(n, col); n++;
      list.push({ serial: ser, sp: e.sp, kind, d });
    }
    this.percMesh.count = n; this.percMesh.instanceMatrix.needsUpdate = true; if (this.percMesh.instanceColor) this.percMesh.instanceColor.needsUpdate = true;
    this.perceived = list;
  }
}

// ------------------------------------------------------------------ procedural geometry

function lerpAngle(a, b, t) { let d = b - a; while (d > Math.PI) d -= 2 * Math.PI; while (d < -Math.PI) d += 2 * Math.PI; return a + d * t; }
function hash1(x) { x = Math.imul(x ^ (x >>> 16), 0x45d9f3b); x = Math.imul(x ^ (x >>> 16), 0x45d9f3b); return (x ^ (x >>> 16)) >>> 0; }
function hash2(x, y) { return hash1(Math.imul(x, 73856093) ^ Math.imul(y, 19349663)); }

/** Merge non-indexed geometries, with an optional flat colour per part. */
function merge(parts) {
  let n = 0; const ps = parts.map(([g, c]) => { const ng = g.index ? g.toNonIndexed() : g; ng.computeVertexNormals(); n += ng.attributes.position.count; return [ng, c]; });
  const pos = new Float32Array(n * 3), nor = new Float32Array(n * 3), col = new Float32Array(n * 3);
  let o = 0;
  for (const [g, c] of ps) {
    const P = g.attributes.position.array, N = g.attributes.normal.array;
    pos.set(P, o * 3); nor.set(N, o * 3);
    const cc = new THREE.Color(c ?? 0xffffff);
    for (let i = 0; i < g.attributes.position.count; i++) { col[(o + i) * 3] = cc.r; col[(o + i) * 3 + 1] = cc.g; col[(o + i) * 3 + 2] = cc.b; }
    o += g.attributes.position.count;
  }
  const out = new THREE.BufferGeometry();
  out.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  out.setAttribute('normal', new THREE.BufferAttribute(nor, 3));
  out.setAttribute('color', new THREE.BufferAttribute(col, 3));
  return out;
}

function treeGeometry() {
  const trunk = new THREE.CylinderGeometry(0.12, 0.18, 1.4, 5); trunk.translate(0, 0.7, 0);
  const crown = new THREE.ConeGeometry(0.9, 2.4, 7); crown.translate(0, 2.4, 0);
  const crown2 = new THREE.ConeGeometry(0.7, 1.6, 7); crown2.translate(0, 3.3, 0);
  return merge([[trunk, 0x5a3d26], [crown, 0x1f5a2a], [crown2, 0x2a6e32]]);
}

const E = (sx, sy, sz, x = 0, y = 0, z = 0, seg = 8) => { const g = new THREE.SphereGeometry(1, seg, Math.max(4, seg - 2)); g.scale(sx, sy, sz); g.translate(x, y, z); return g; };
const Cy = (r, h, x, y, z, rx = 0, rz = 0) => { const g = new THREE.CylinderGeometry(r, r, h, 5); g.rotateX(rx); g.rotateZ(rz); g.translate(x, y, z); return g; };
const Co = (r, h, x, y, z, rz = 0, rx = 0) => { const g = new THREE.ConeGeometry(r, h, 6); g.rotateZ(rz); g.rotateX(rx); g.translate(x, y, z); return g; };
const Bx = (sx, sy, sz, x = 0, y = 0, z = 0) => { const g = new THREE.BoxGeometry(sx, sy, sz); g.translate(x, y, z); return g; };
const legs = (len, w, x, z, r = 0.06) => [Cy(r, len, x, len / 2, z), Cy(r, len, x, len / 2, -z), Cy(r, len, -x, len / 2, z), Cy(r, len, -x, len / 2, -z)];
function swarm(n, r, size, seed, flat = 1) {
  const parts = []; let h = seed;
  for (let i = 0; i < n; i++) {
    h = hash1(h + i);
    const a = (h & 1023) / 1024 * Math.PI * 2, d = ((h >> 10) & 1023) / 1024 * r, y = (((h >> 20) & 255) / 256) * r * flat;
    parts.push(E(size, size * 0.8, size, Math.cos(a) * d, y + size, Math.sin(a) * d, 5));
  }
  return parts;
}

/** One model per species (shape id from the roster), facing +x, unit height about 1. */
function creatureGeometry(shape) {
  const g = (list) => merge(list.map((x) => [x, 0xffffff]));
  switch (shape) {
    case 0: return g([E(0.9, 0.38, 0.32, 0, 0.95), E(0.26, 0.22, 0.2, 0.95, 1.35), Cy(0.08, 0.5, 0.75, 1.15, 0, 0, -0.8), ...legs(0.8, 0.06, 0.5, 0.15), Co(0.05, 0.35, 1.0, 1.65, 0.08), Co(0.05, 0.35, 1.0, 1.65, -0.08)]);   // Drifters
    case 1: return g([E(1.2, 0.65, 0.6, 0, 1.6), Cy(0.18, 1.3, 1.1, 2.3, 0, 0, -0.7), E(0.35, 0.25, 0.22, 1.6, 2.9), ...legs(1.2, 0.12, 0.7, 0.35, 0.13)]);  // Browsers
    case 2: return g([E(0.9, 0.55, 0.5, 0, 0.8), Co(0.25, 0.6, 1.0, 0.75, 0, -Math.PI / 2), Co(0.05, 0.4, 1.0, 0.65, 0.2, -1.2), Co(0.05, 0.4, 1.0, 0.65, -0.2, -1.2), ...legs(0.45, 0.1, 0.5, 0.28, 0.1)]); // Tuskers
    case 3: return g([E(0.45, 0.22, 0.22, 0, 0.3), Bx(0.4, 0.04, 2.2, -0.05, 0.38, 0), Co(0.12, 0.35, 0.55, 0.32, 0, -Math.PI / 2), Bx(0.3, 0.03, 0.6, -0.5, 0.33, 0)]);   // Gliders
    case 4: return g([E(0.5, 0.42, 0.42, 0, 0.42), E(0.24, 0.22, 0.22, 0.45, 0.55), E(0.08, 0.16, 0.06, 0.45, 0.82, 0.12), E(0.08, 0.16, 0.06, 0.45, 0.82, -0.12)]);   // Burrowers
    case 5: return g(swarm(9, 1.1, 0.16, 5));                       // Hummers
    case 6: return g(swarm(12, 1.3, 0.14, 6, 0.6));                 // Chewers
    case 7: return g(swarm(10, 1.2, 0.18, 7, 0.1));                 // Delvers
    case 8: return g([E(1.2, 0.08, 1.2, 0, 0.05), Cy(0.07, 0.4, 0.2, 0.2, 0.1), E(0.3, 0.12, 0.3, 0.2, 0.45, 0.1), Cy(0.06, 0.3, -0.4, 0.15, -0.3), E(0.22, 0.1, 0.22, -0.4, 0.33, -0.3)]); // Rotmats
    case 9: return g([E(0.8, 0.45, 0.4, 0, 0.85), E(0.3, 0.24, 0.24, 0.85, 0.75), ...legs(0.7, 0.08, 0.45, 0.2, 0.08)]);   // Gleaners
    case 10: return g([E(0.35, 0.2, 0.2, 0, 0.3), Bx(0.25, 0.03, 1.2, 0, 0.36, 0), Co(0.06, 0.25, 0.42, 0.32, 0, -Math.PI / 2)]);  // Snappers
    case 11: return g([E(0.95, 0.32, 0.3, 0, 0.6), E(0.26, 0.22, 0.22, 0.95, 0.72), Cy(0.05, 0.9, -1.2, 0.75, 0, 0, 1.1), ...legs(0.45, 0.07, 0.55, 0.18, 0.07)]);   // Prowlers
    case 12: return g([E(0.95, 0.4, 0.34, 0, 0.8), E(0.3, 0.25, 0.24, 1.0, 0.95), Co(0.1, 0.45, 1.35, 0.9, 0, -Math.PI / 2), Cy(0.07, 0.7, -1.1, 0.8, 0, 0, 1.0), ...legs(0.6, 0.08, 0.55, 0.2, 0.08)]); // Stalkers
    case 13: return g([E(1.3, 0.75, 0.7, 0, 1.3), E(0.55, 0.45, 0.42, 1.35, 1.35), Co(0.15, 0.6, 0, 2.2, 0), Co(0.15, 0.55, -0.5, 2.1, 0), Co(0.15, 0.55, 0.5, 2.1, 0), Cy(0.16, 1.2, -1.5, 1.2, 0, 0, 1.2), ...legs(0.9, 0.2, 0.75, 0.42, 0.2)]); // Leviathans
    case 14: return g(swarm(10, 1.0, 0.1, 14));                     // Bloodflies
    case 15: return g(swarm(14, 1.4, 0.16, 15, 0.15));              // Siltmites
    case 16: { const tail = Co(0.25, 0.4, -0.75, 0.0, 0, Math.PI / 2); tail.scale(1, 1, 0.25); return g([E(0.7, 0.25, 0.16, 0, 0), tail]); }   // Minnows
    case 17: { const tail = Co(0.3, 0.5, -1.35, 0.0, 0, Math.PI / 2); tail.scale(1, 1, 0.25); return g([E(1.2, 0.22, 0.18, 0, 0), tail, Co(0.1, 0.35, 1.25, 0, 0, -Math.PI / 2)]); } // Pikes
    case 18: return g([E(0.5, 0.3, 0.45, 0, 0.3), E(0.12, 0.12, 0.12, 0.3, 0.55, 0.18), E(0.12, 0.12, 0.12, 0.3, 0.55, -0.18), E(0.35, 0.08, 0.12, -0.3, 0.1, 0.4), E(0.35, 0.08, 0.12, -0.3, 0.1, -0.4)]); // Croakers
    case 19: return g([E(0.55, 0.32, 0.28, 0, 1.7), Cy(0.05, 0.9, 0.55, 2.2, 0, 0, -0.5), E(0.16, 0.13, 0.12, 0.8, 2.6), Co(0.04, 0.6, 1.15, 2.58, 0, -Math.PI / 2), Cy(0.03, 1.4, 0.05, 0.7, 0.12), Cy(0.03, 1.4, 0.05, 0.7, -0.12)]); // Herons
    default: return g([E(0.6, 0.4, 0.4, 0, 0.5)]);
  }
}
