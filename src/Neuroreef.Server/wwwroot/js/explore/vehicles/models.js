// Explore vehicles: low-poly models built in code, in metres (scaled to the view's units by the caller). Local frame:
// +x forward, +y up, +z to the right; the origin is where the vehicle meets the ground (the waterline for the boat).
// Named parts the controller moves: jeep 'wheel' x4, 'turret', 'gun', 'muzzle', 'flash'; plane 'prop', 'flash' (none),
// 'bombL'/'bombR'; boat 'muzzle', 'flash'.

import * as THREE from 'three';

const M = new Map();
/** A shared flat-shaded material per colour (the vehicles are few; their materials are reused). */
const mat = (c, o = {}) => {
  const k = c + JSON.stringify(o);
  if (!M.has(k)) M.set(k, new THREE.MeshLambertMaterial({ color: c, flatShading: true, ...o }));
  return M.get(k);
};
const box = (w, h, d, c, x = 0, y = 0, z = 0) => { const m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), mat(c)); m.position.set(x, y, z); return m; };
const cyl = (r0, r1, h, c, seg = 8) => new THREE.Mesh(new THREE.CylinderGeometry(r0, r1, h, seg), mat(c));

/** The muzzle flash: a star of two crossed additive cones, hidden until a shot. Points along +x. */
function flash(size) {
  const g = new THREE.Group(); g.name = 'flash'; g.visible = false;
  const m = new THREE.MeshBasicMaterial({ color: 0xffd27a, transparent: true, opacity: 0.95, blending: THREE.AdditiveBlending, depthWrite: false });
  const c1 = new THREE.Mesh(new THREE.ConeGeometry(0.22 * size, 0.9 * size, 6, 1, true), m); c1.rotation.z = -Math.PI / 2; c1.position.x = 0.45 * size;
  const c2 = new THREE.Mesh(new THREE.SphereGeometry(0.2 * size, 6, 4), m);
  g.add(c1, c2);
  return g;
}

function wheel(r, w) {
  const g = new THREE.Group(); g.name = 'wheel';
  const t = cyl(r, r, w, 0x1c1c1c, 10); t.rotation.x = Math.PI / 2;
  const hub = cyl(r * 0.5, r * 0.5, w * 1.05, 0x8a8f86, 6); hub.rotation.x = Math.PI / 2;
  g.add(t, hub);
  return g;
}

export function jeepModel() {
  const g = new THREE.Group(), olive = 0x5d6b3b, dark = 0x3c4527;
  g.add(box(3.7, 0.55, 1.8, olive, 0, 0.85, 0));            // tub
  g.add(box(1.25, 0.3, 1.7, olive, 1.15, 1.25, 0));         // bonnet
  g.add(box(0.12, 0.5, 1.5, 0x9fc2cf, 0.48, 1.62, 0));      // windscreen
  g.add(box(0.14, 0.08, 1.66, dark, 0.48, 1.9, 0));         // its frame
  g.add(box(0.5, 0.2, 1.7, dark, 1.82, 0.8, 0));            // grille and bumper
  for (const z of [-0.6, 0.6]) g.add(box(0.06, 0.14, 0.18, 0xfff3c4, 1.88, 1.05, z));   // headlights
  for (const z of [-0.95, 0.95]) g.add(box(1.1, 0.12, 0.25, dark, 1.2, 1.12, z), box(1.1, 0.12, 0.25, dark, -1.2, 1.12, z));   // wings over the wheels
  g.add(box(0.6, 0.45, 0.6, 0x4a3b2a, -0.1, 1.25, -0.42), box(0.6, 0.45, 0.6, 0x4a3b2a, -0.1, 1.25, 0.42));   // seats
  // roll bar
  for (const z of [-0.82, 0.82]) { const p = box(0.08, 0.9, 0.08, dark, -0.7, 1.55, z); g.add(p); }
  g.add(box(0.08, 0.08, 1.72, dark, -0.7, 2.0, 0));
  // spare wheel on the back
  const sp = wheel(0.38, 0.25); sp.name = 'spare'; sp.rotation.y = Math.PI / 2; sp.position.set(-1.98, 1.0, 0); g.add(sp);
  for (const [x, z] of [[1.25, -0.88], [1.25, 0.88], [-1.25, -0.88], [-1.25, 0.88]]) { const wh = wheel(0.42, 0.3); wh.position.set(x, 0.42, z); g.add(wh); }
  // the mounted rifle on a pintle behind the seats: turret (yaw) > gun (pitch) > muzzle
  const tur = new THREE.Group(); tur.name = 'turret'; tur.position.set(-1.05, 2.05, 0);
  tur.add(cyl(0.06, 0.08, 0.5, 0x2b2f33, 6).translateY(-0.25));
  const gun = new THREE.Group(); gun.name = 'gun';
  gun.add(box(0.75, 0.16, 0.14, 0x2b2f33, 0.1, 0, 0));        // receiver
  const bar = cyl(0.035, 0.035, 1.0, 0x16191c, 6); bar.rotation.z = Math.PI / 2; bar.position.x = 0.95; gun.add(bar);
  gun.add(box(0.12, 0.22, 0.2, 0x5a4a2a, 0.05, -0.15, 0.14));   // ammo box
  gun.add(box(0.3, 0.06, 0.4, 0x2b2f33, -0.05, 0.12, 0));       // shield plate's top
  gun.add(box(0.05, 0.4, 0.55, 0x3c4527, 0.42, 0.05, 0));       // shield
  const muz = new THREE.Object3D(); muz.name = 'muzzle'; muz.position.x = 1.48; gun.add(muz);
  const fl = flash(0.7); fl.position.x = 1.48; gun.add(fl);
  tur.add(gun); g.add(tur);
  return g;
}

export function planeModel() {
  const g = new THREE.Group(), body = 0xc7cfd4, trim = 0xc0392b, dark = 0x2b2f33;
  const Y = 1.65;   // the fuselage's axis above the wheels
  const fus = cyl(0.62, 0.45, 5.4, body, 10); fus.rotation.z = -Math.PI / 2; fus.position.set(0.6, Y, 0); g.add(fus);
  const tail = cyl(0.45, 0.12, 3.6, body, 10); tail.rotation.z = -Math.PI / 2; tail.position.set(-3.9, Y + 0.12, 0); g.add(tail);
  const nose = cyl(0.62, 0.5, 0.6, 0x4a5055, 10); nose.rotation.z = -Math.PI / 2; nose.position.set(3.55, Y, 0); g.add(nose);
  const spin = new THREE.Mesh(new THREE.ConeGeometry(0.22, 0.5, 8), mat(trim)); spin.rotation.z = -Math.PI / 2; spin.position.set(4.1, Y, 0); g.add(spin);
  // propeller (spins about +x)
  const prop = new THREE.Group(); prop.name = 'prop'; prop.position.set(3.95, Y, 0);
  prop.add(box(0.08, 2.6, 0.18, dark), box(0.08, 0.18, 2.6, dark)); g.add(prop);
  const disc = new THREE.Mesh(new THREE.CircleGeometry(1.3, 20), new THREE.MeshBasicMaterial({ color: 0x999999, transparent: true, opacity: 0.12, side: THREE.DoubleSide, depthWrite: false }));
  disc.name = 'disc'; disc.rotation.y = Math.PI / 2; disc.position.set(3.97, Y, 0); g.add(disc);
  // canopy
  const can = new THREE.Mesh(new THREE.SphereGeometry(0.62, 10, 6, 0, Math.PI * 2, 0, Math.PI / 2), mat(0x7fb0c8, { transparent: true, opacity: 0.8 }));
  can.scale.set(1.9, 0.9, 0.85); can.position.set(0.9, Y + 0.38, 0); g.add(can);
  // low wing with red tips, a slight dihedral
  for (const s of [-1, 1]) {
    const w = box(1.9, 0.14, 5.6, body, 0.7, Y - 0.38, s * 3.2); w.rotation.x = s * 0.06; g.add(w);
    g.add(box(1.7, 0.15, 0.5, trim, 0.62, Y - 0.2, s * 6.05));
    // a bomb under each wing (hidden when the rack is empty)
    const b = new THREE.Group(); b.name = s < 0 ? 'bombL' : 'bombR';
    const bb = cyl(0.17, 0.17, 1.1, 0x3d4a36, 8); bb.rotation.z = Math.PI / 2; b.add(bb);
    const bn = new THREE.Mesh(new THREE.ConeGeometry(0.17, 0.35, 8), mat(0x3d4a36)); bn.rotation.z = -Math.PI / 2; bn.position.x = 0.72; b.add(bn);
    b.position.set(0.8, Y - 0.72, s * 2.2); g.add(b);
  }
  g.add(box(1.1, 0.1, 3.8, body, -5.3, Y + 0.25, 0));                 // tailplane
  const fin = box(1.3, 1.5, 0.1, body, -5.4, Y + 0.95, 0); g.add(fin);
  g.add(box(0.5, 0.6, 0.12, trim, -5.75, Y + 1.4, 0));                 // rudder's top
  // fixed gear
  for (const z of [-1.5, 1.5]) {
    g.add(box(0.12, 1.0, 0.12, dark, 0.95, 0.75, z));
    const wh = cyl(0.3, 0.3, 0.2, 0x1c1c1c, 10); wh.rotation.x = Math.PI / 2; wh.position.set(0.95, 0.3, z); g.add(wh);
  }
  g.add(box(0.1, 0.9, 0.1, dark, -5.0, Y - 0.6, 0));
  const tw = cyl(0.15, 0.15, 0.1, 0x1c1c1c, 8); tw.rotation.x = Math.PI / 2; tw.position.set(-5.0, 0.15, 0); g.add(tw);
  return g;
}

/** A hull: a box narrowed and lifted to a bow at +x. */
function hullGeometry(L, H, B) {
  const geo = new THREE.BoxGeometry(L, H, B, 8, 1, 2), p = geo.attributes.position;
  for (let i = 0; i < p.count; i++) {
    let x = p.getX(i), y = p.getY(i), z = p.getZ(i);
    const t = Math.max(0, (x - L * 0.1) / (L * 0.4));            // 0 aft of mid-ships .. 1 at the bow
    z *= 1 - 0.92 * Math.pow(t, 1.6);
    if (y < 0) { y += t * H * 0.7; z *= 0.55 + 0.45 * t; }          // a vee bottom that rises to the bow
    p.setXYZ(i, x, y, z);
  }
  geo.computeVertexNormals();
  return geo;
}

export function boatModel() {
  const g = new THREE.Group(), grey = 0x8e9aa3, white = 0xe8ecef, dark = 0x2e3a44;
  const hull = new THREE.Mesh(hullGeometry(10, 1.6, 3.2), mat(grey)); hull.position.y = 0.25; g.add(hull);
  g.add(box(8.6, 0.06, 2.6, 0x6b5b45, -0.6, 1.07, 0));                 // deck
  g.add(box(9.4, 0.18, 0.06, 0xb83a2e, -0.3, 0.55, 1.58), box(9.4, 0.18, 0.06, 0xb83a2e, -0.3, 0.55, -1.58));   // boot stripe
  g.add(box(2.6, 1.3, 2.1, white, -0.6, 1.75, 0));                     // wheelhouse
  g.add(box(2.62, 0.35, 2.12, dark, -0.6, 2.05, 0));                    // its windows
  g.add(box(2.9, 0.1, 2.3, white, -0.6, 2.45, 0));                      // roof
  const mast = cyl(0.05, 0.05, 2.2, dark, 6); mast.position.set(-0.9, 3.5, 0); g.add(mast);
  g.add(box(0.1, 0.1, 1.0, dark, -0.9, 4.2, 0));
  // twin torpedo tubes on the foredeck, angled out a little
  for (const s of [-1, 1]) {
    const t = cyl(0.22, 0.22, 2.4, 0x55606a, 8); t.rotation.z = -Math.PI / 2; t.rotation.y = s * 0.08; t.position.set(2.0, 1.35, s * 0.75); g.add(t);
  }
  const muz = new THREE.Object3D(); muz.name = 'muzzle'; muz.position.set(3.3, 1.35, 0); g.add(muz);
  const fl = flash(1.2); fl.position.set(3.3, 1.35, 0); g.add(fl);
  // outboard pair at the stern
  g.add(box(0.5, 0.9, 0.4, dark, -5.1, 0.6, 0.6), box(0.5, 0.9, 0.4, dark, -5.1, 0.6, -0.6));
  return g;
}

/** Bomb and torpedo meshes, pointing along +x, in metres. */
export function bombMesh() {
  const g = new THREE.Group();
  const b = cyl(0.17, 0.17, 1.1, 0x3d4a36, 8); b.rotation.z = Math.PI / 2; g.add(b);
  const n = new THREE.Mesh(new THREE.ConeGeometry(0.17, 0.35, 8), mat(0x3d4a36)); n.rotation.z = -Math.PI / 2; n.position.x = 0.72; g.add(n);
  g.add(box(0.25, 0.5, 0.04, 0x2b2f33, -0.6, 0, 0), box(0.25, 0.04, 0.5, 0x2b2f33, -0.6, 0, 0));
  return g;
}

export function torpedoMesh() {
  const g = new THREE.Group();
  const b = cyl(0.27, 0.27, 4.2, 0x3c4650, 8); b.rotation.z = Math.PI / 2; g.add(b);
  const n = new THREE.Mesh(new THREE.SphereGeometry(0.27, 8, 6), mat(0x3c4650)); n.position.x = 2.1; g.add(n);
  g.add(box(0.3, 0.7, 0.04, 0x222222, -2.2, 0, 0), box(0.3, 0.04, 0.7, 0x222222, -2.2, 0, 0));
  return g;
}

/** Turn a model's materials to burnt wreckage (a crashed plane). */
export function scorch(g) {
  const burnt = mat(0x1d1b19);
  g.traverse((o) => { if (o.isMesh) o.material = burnt; });
}
