// Body Lab: one evolved body (a LabBody from BodyLabData.cs) as a three.js group, drawn only from its physical
// parameters: segments by length, width, height (their taper is in those sizes); appendage pairs by length, chord and
// thickness, droop, joints (one visible link per joint, with a knuckle), mineral and outer tube (colour and shine);
// porous surface as gill-like fringes, folded-in exchange as dark openings; a mineralised integument as a shiny plated
// shell over the back, a soft one as matt skin. Nothing is shaped by a name: the role colours come from the physics.

import * as THREE from 'three';

export const ROLE_COLORS = {
  thrust: 0x3fc1c9, steering: 0x7ea6ff, support: 0xf4a340, crawling: 0xb9d36b, crushing: 0xff4d3d, striking: 0xff7ab8,
  gas: 0xd86cff, protection: 0x9aa7bf, housing: 0xc8b49e, passive: 0x59636e, skeleton: 0xe9dcb8,
};
export const ROLE_NAMES = {
  thrust: 'thrust', steering: 'steering', support: 'support', skeleton: 'support (internal skeleton)', crawling: 'crawling', crushing: 'handling · crushing',
  striking: 'handling · striking', gas: 'gas exchange', protection: 'protection', housing: 'housing (gut, muscle)', passive: 'passive',
};

const SPHERE = new THREE.SphereGeometry(1, 40, 28);
const SPHERE_LO = new THREE.SphereGeometry(1, 16, 12);
// the detail levels (opts.detail): the lab's own, and the console's near and far module bodies (js/creatures/modular.js
// merges a body into one geometry per clade and draws thousands of it, so its spheres and lofts are coarse)
// (near and far: each limb link's sphere as fine as a budget of limb triangles over its links allows, and at most
// `fringes` gill filaments in all)
const DETAIL = {
  lab: { sphere: SPHERE, small: SPHERE_LO, nx: 64, na: 40, extras: true },
  near: { limbTris: 9000, small: new THREE.SphereGeometry(1, 6, 4), nx: 28, na: 16, extras: true, fringes: 240 },
  far: { limbTris: 1000, small: null, nx: 10, na: 8, extras: false },
};
const LINK_SPHERES = [[12, 8], [8, 6], [6, 4], [4, 3]].map(([w, h]) => { const g = new THREE.SphereGeometry(1, w, h); return [g, g.index.count / 3]; });
/** The detail for this body: the sphere for its limb links within the budget, and the share of its fringes drawn. */
function detailFor(b, D) {
  if (!D.limbTris) return { ...D, fringeK: 1 };
  const pairs = b.pairs || [], segs = b.segs || [];
  let links = 0, fr = 0;
  for (const p of pairs) { const n = 2 * ((p.joints || 0) + 1), ev = (p.pore || 0) * (1 - (p.invag || 0)); links += n; if (ev > 0.005) fr += n * (3 + 14 * ev); }
  for (const s of segs) { const ev = (s.pore || 0) * (1 - (s.invag || 0)); if (ev > 0.005) fr += 8 + 90 * Math.sqrt(ev); }
  const per = D.limbTris / Math.max(1, links);
  const sphere = (LINK_SPHERES.find(([, t]) => t <= per) || LINK_SPHERES[LINK_SPHERES.length - 1])[0];
  return { ...D, sphere, fringeK: D.fringes ? Math.min(1, D.fringes / Math.max(1, fr)) : 1 };
}
const FRINGE = (() => { const g = new THREE.CylinderGeometry(0.12, 0.5, 1, 5, 1); g.translate(0, 0.5, 0); return g; })();   // a filament from its base

const SOFT = new THREE.Color(0xe3a596), CHITIN = new THREE.Color(0xb7853f), CALC = new THREE.Color(0xefe2c4);
const GILL = new THREE.Color(0xc4344c), PORE = new THREE.Color(0x3a0d16);

/** Integument colour by mineral share: soft skin, then amber cuticle, then ivory mineral. */
function tissueColor(mineral) {
  const c = new THREE.Color();
  return mineral < 0.5 ? c.copy(SOFT).lerp(CHITIN, mineral / 0.5) : c.copy(CHITIN).lerp(CALC, (mineral - 0.5) / 0.5);
}

// countershading and a fine mottle: a darker back and a paler belly, as most animals lit from above have; one
// texture for every body (SphereGeometry's v runs from the bottom, 0, to the top, 1; the canvas from the top)
const SKIN = (() => {
  const c = document.createElement('canvas'); c.width = 256; c.height = 128;
  const x = c.getContext('2d');
  const gr = x.createLinearGradient(0, 0, 0, 128);
  gr.addColorStop(0, '#a3958f'); gr.addColorStop(0.5, '#ebe5e1'); gr.addColorStop(1, '#ffffff');
  x.fillStyle = gr; x.fillRect(0, 0, 256, 128);
  let a = 12345; const rnd = () => ((a = (a * 1103515245 + 12345) & 0x7fffffff) / 0x7fffffff);
  for (let i = 0; i < 900; i++) {
    const y = rnd() * 128, r = 0.6 + rnd() * 2.2 * (1 - y / 160);
    x.fillStyle = `rgba(60,30,25,${0.05 + 0.12 * rnd() * (1 - y / 128)})`;
    x.beginPath(); x.ellipse(rnd() * 256, y, r * 1.6, r, 0, 0, Math.PI * 2); x.fill();
  }
  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; t.wrapS = THREE.RepeatWrapping;
  return t;
})();

function material(color, mineral, shine, role, opts, plain) {
  const roleMode = opts.roles;
  const m = new THREE.MeshPhysicalMaterial({
    map: plain ? null : SKIN,
    color: roleMode ? new THREE.Color(ROLE_COLORS[role] ?? 0x888888) : color,
    roughness: 0.78 - 0.5 * shine, metalness: 0.0,
    clearcoat: shine, clearcoatRoughness: 0.25 + 0.4 * (1 - shine),
    sheen: 1 - mineral, sheenRoughness: 0.6, sheenColor: new THREE.Color(0xffd8cc),
  });
  return m;
}

/** Whether a body is drawn lying on its side: one that would topple on rough ground (b.topple, the chance it tips,
 *  BodyPose.Topple on the server, as the live sim's Live.StandPosture weights it) and is not swimming. */
export function liesDown(b, swimming = b.mode === 'swims') { return !swimming && (b.topple || 0) > 0.5; }

/** The body as a group, centred, with its longest extent `size` scene units. Each pickable mesh carries
 *  userData.part = { kind: 'seg'|'pair', index }, and userData.rig = { kind: 'trunk'|'limb'|'gill'|'dark', pair, side,
 *  link, base (the limb's root, body frame), role } for the console's animation (js/creatures/modular.js).
 *  opts.detail: 'lab' (default), 'near' or 'far'; opts.fit = false leaves it in metres, uncentred (head at +x, back at
 *  +y, left at +z); opts.lie (default liesDown(b)) lays it on its right side (rotated about its long axis: its width
 *  becomes its height), as the sim has a body that would topple lie. Returns { group, extent (m), scale, bottom (scene
 *  y), lying }. */
export function buildBody(b, opts = {}) {
  const size = opts.size ?? 2.4, D = detailFor(b, DETAIL[opts.detail] || DETAIL.lab);
  const lie = opts.lie ?? liesDown(b);
  const g = new THREE.Group();
  const segs = b.segs || [], pairs = b.pairs || [];
  const L = segs.reduce((s, x) => s + (x.l || 0), 0) || 0.01;
  const mats = [];
  const mk = (color, mineral, shine, role, plain) => { const m = material(color, mineral, shine, role, opts, plain === true); mats.push(m); return m; };

  // trunk: one lofted surface through the segments' sections (each segment its own mesh over its own length, so it
  // can be picked and coloured), pinched a little where two segments meet, rounded off at the head and tail
  const prof = trunkProfile(segs, L);
  segs.forEach((s, i) => {
    const l = s.l || 1e-4, w = s.w || 1e-4, h = s.h || 1e-4;
    const cx = L / 2 - ((s.x0 || 0) + l / 2);
    const rig = s.intMin || 0;
    const part = { kind: 'seg', index: i };
    const xHi = L / 2 - (s.x0 || 0), xLo = xHi - l;
    const soft = rig > 0.35 ? 0.05 : rig;
    const body = new THREE.Mesh(loft(prof, xLo, xHi, 1, 0, Math.PI * 2, L, false, D), mk(tissueColor(soft), soft, rig > 0.35 ? 0.1 : rig * 0.6, s.primary, opts));
    body.userData.part = part; body.userData.rig = { kind: 'trunk' }; g.add(body);
    // a mineralised integument: a plated shell over the back and flanks (a gap at each joint between plates), its
    // thickness exaggerated just enough to see
    if (rig > 0.35) {
      const t = Math.min(0.14, Math.max(0.035, 2 * (s.intT || 0) / h));
      const inset = Math.min(l * 0.08, 0.04 * L / segs.length);
      const shell = new THREE.Mesh(loft(prof, xLo + inset, xHi - inset, 1 + t, -0.15 * Math.PI, 1.3 * Math.PI, L, true, D),
        mk(tissueColor(rig), rig, Math.min(1, 0.4 + rig), s.primary, opts));
      shell.material.side = THREE.DoubleSide;   // an open shell: its underside shows at the flanks
      shell.userData.part = part; shell.userData.rig = { kind: 'trunk', twoSided: true }; g.add(shell);
    }
    // everted exchange surface: gill-like fringes along both flanks, as many and as long as the porous share
    const ev = (s.pore || 0) * (1 - (s.invag || 0));
    // (by the square root: a 3% porous share, folded tenfold, is already a third more surface)
    if (D.extras && ev > 0.005) fringes(g, mk(GILL, 0, 0.3, 'gas', true), part, Math.round((8 + 90 * Math.sqrt(ev)) * D.fringeK), cx, l, w, h, 0.35 + 2.2 * Math.sqrt(ev), prof);
    // folded-in exchange: dark openings (spiracles, a lung's pore) on the flanks
    const inn = (s.pore || 0) * (s.invag || 0);
    if (D.extras && inn > 0.005) openings(g, mk(PORE, 0, 0.2, 'gas', true), part, Math.min(6, Math.round(1 + 20 * inn)), cx, l, w, h, prof, D.small);
    // the mouth at the front of the first segment
    if (i === 0 && D.small) {
      const mouth = new THREE.Mesh(D.small, new THREE.MeshStandardMaterial({ color: 0x2a0b10, roughness: 0.6 }));
      mats.push(mouth.material);
      mouth.scale.set(l * 0.05 + 1e-5, h * 0.12, w * 0.16); mouth.position.set(L / 2 - Math.min(l * 0.12, h * 0.05), -h * 0.08, 0);
      mouth.userData.rig = { kind: 'dark' }; g.add(mouth);
    }
  });

  // appendage pairs
  const perSeg = {};
  pairs.forEach(p => { p.seg ??= 0; (perSeg[p.seg] ??= []).push(p); });   // (a file may leave zeros out)
  let span = 0, low = 0;
  segs.forEach(s => { low = Math.min(low, -(s.h || 0) / 2); });
  pairs.forEach((p, i) => {
    const s = segs[p.seg]; if (!s) return;
    const list = perSeg[p.seg], k = list.indexOf(p), n = list.length;
    const l = s.l;
    const cx0 = L / 2 - ((s.x0 || 0) + l / 2), [ry0, rz0] = prof(cx0), h = 2 * ry0, w = 2 * rz0;
    const cx = L / 2 - ((s.x0 || 0) + l / 2) + (n > 1 ? (k - (n - 1) / 2) / n * l * 0.8 : 0);
    const part = { kind: 'pair', index: i };
    const mineral = p.mineral || 0, shine = Math.min(1, mineral * (0.3 + 0.7 * (p.exo || 0)));
    const mat = mk(tissueColor(mineral), mineral, shine, p.primary, opts);
    const knuckle = mk(tissueColor(mineral).multiplyScalar(0.7), mineral, shine, p.primary, opts);
    const fr = D.extras && (p.pore || 0) * (1 - (p.invag || 0)) > 0.005 ? mk(GILL, 0, 0.3, 'gas', true) : null;
    for (const side of [1, -1]) {
      let base, dirAt;
      const th = p.theta || 0, links = (p.joints || 0) + 1, len = p.len || 1e-4;
      const bend = links > 1 ? Math.min(0.12, 0.6 / links) : 0;
      if (!p.sagittal) {
        const y = -h * 0.12;
        const z = side * (w / 2) * Math.sqrt(Math.max(0, 1 - (y / (h / 2)) ** 2)) * 0.95;
        base = new THREE.Vector3(cx, y, z);
        dirAt = j => { const t = Math.min(1.55, th + j * bend); return new THREE.Vector3(0, -Math.sin(t), side * Math.cos(t)); };
      } else {
        base = new THREE.Vector3(cx, side * h / 2 * 0.92, 0);
        dirAt = j => { const t = Math.min(1.4, th * 0.6 + j * bend * 0.5); return new THREE.Vector3(-Math.sin(t), side * Math.cos(t), 0); };
      }
      let at = base.clone();
      for (let j = 0; j < links; j++) {
        const d = dirAt(j).normalize(), ll = len / links;
        const taper = 1 - 0.35 * (j / Math.max(1, links));
        const m = new THREE.Mesh(D.sphere, mat);
        orient(m, d);
        m.scale.set((p.chord || 1e-4) / 2 * taper, ll / 2 * 1.08, (p.thick || 1e-4) / 2 * taper);
        m.position.copy(at).addScaledVector(d, ll / 2);
        const rig = { kind: 'limb', pair: i, side, link: j, base: base.clone(), role: p.role || p.primary || 'passive', sagittal: !!p.sagittal };
        m.userData.part = part; m.userData.rig = rig; g.add(m);
        if (fr) fringeAlong(g, fr, part, m, d, ll, (p.chord || 1e-4) * taper, (p.pore || 0) * (1 - (p.invag || 0)), { ...rig, kind: 'gill' }, D.fringeK);
        at.addScaledVector(d, ll);
        if (j < links - 1 && D.small) {   // a joint: a knuckle between two rigid links
          const r = Math.min(p.chord || 1e-4, p.thick || 1e-4) * 0.36 * taper;
          const kn = new THREE.Mesh(D.small, knuckle);
          kn.scale.setScalar(r); kn.position.copy(at); kn.userData.part = part; kn.userData.rig = rig; g.add(kn);
        }
      }
      span = Math.max(span, Math.abs(at.z) * 2, Math.abs(at.x - cx) + L / 2);
      low = Math.min(low, at.y);
    }
  });

  // lying on its side: turned about its long axis, its back to +z (the left) and its right flank down
  if (lie) g.rotation.x = Math.PI / 2;
  g.userData.lying = lie;
  if (opts.fit === false) {
    g.userData.dispose = () => { mats.forEach(m => m.dispose()); g.traverse(o => { if (o.geometry?.userData.own) o.geometry.dispose(); }); };
    return { group: g, extent: L, length: L, scale: 1, bottom: 0, lying: lie };
  }
  // fit it to the view
  const box = new THREE.Box3().setFromObject(g);
  const ext = Math.max(box.max.x - box.min.x, box.max.y - box.min.y, box.max.z - box.min.z, 1e-6);
  const k = size / ext;
  g.scale.setScalar(k);
  const ctr = box.getCenter(new THREE.Vector3());
  const inner = g; const outer = new THREE.Group();
  inner.position.set(-ctr.x * k, -ctr.y * k, -ctr.z * k);
  outer.add(inner);
  outer.userData.dispose = () => {
    mats.forEach(m => m.dispose());
    g.traverse(o => { if (o.geometry?.userData.own) o.geometry.dispose(); });   // the lofted trunk (the rest is shared)
  };
  return { group: outer, extent: ext, length: L, scale: k, bottom: (box.min.y - ctr.y) * k, lying: lie };
}

const UP = new THREE.Vector3(0, 1, 0), AX = new THREE.Vector3(1, 0, 0);
/** Turns a unit sphere so its local y lies along d and its local x (the chord) as near the body axis as it can. */
function orient(m, d) {
  let x = AX.clone().addScaledVector(d, -AX.dot(d));
  if (x.lengthSq() < 1e-6) x = new THREE.Vector3(0, 0, 1).addScaledVector(d, -d.z);
  x.normalize();
  const z = new THREE.Vector3().crossVectors(x, d).normalize();
  m.quaternion.setFromRotationMatrix(new THREE.Matrix4().makeBasis(x, d, z));
}

/** The trunk's half height and half width at x (head at +L/2): smooth between the segments' centres, pinched
 *  where two segments meet, rounded off over the first and last half segment. */
function trunkProfile(segs, L) {
  const c = segs.map(s => ({ x: L / 2 - ((s.x0 || 0) + (s.l || 0) / 2), ry: (s.h || 1e-4) / 2, rz: (s.w || 1e-4) / 2, l: s.l || 1e-4, front: L / 2 - (s.x0 || 0) }));
  const n = c.length;
  return x => {
    let ry, rz;
    if (x >= c[0].x) {
      const span = L / 2 - c[0].x, d = span > 0 ? Math.min(1, (x - c[0].x) / span) : 1, k = Math.sqrt(Math.max(0, 1 - d * d));
      ry = c[0].ry * k; rz = c[0].rz * k;
    } else if (x <= c[n - 1].x) {
      const span = c[n - 1].x + L / 2, d = span > 0 ? Math.min(1, (c[n - 1].x - x) / span) : 1, k = Math.sqrt(Math.max(0, 1 - d * d));
      ry = c[n - 1].ry * k; rz = c[n - 1].rz * k;
    } else {
      let i = 0; while (i < n - 2 && x < c[i + 1].x) i++;
      const t = (c[i].x - x) / Math.max(1e-9, c[i].x - c[i + 1].x), s = t * t * (3 - 2 * t);
      ry = c[i].ry + (c[i + 1].ry - c[i].ry) * s; rz = c[i].rz + (c[i + 1].rz - c[i].rz) * s;
    }
    let pinch = 0;
    for (let i = 1; i < n; i++) { const d = (x - c[i].front) / (0.22 * Math.min(c[i].l, c[i - 1].l)); pinch = Math.max(pinch, Math.exp(-d * d)); }
    const f = 1 - 0.07 * pinch;
    return [ry * f, rz * f];
  };
}

/** A lofted strip of the trunk from x1 down to x0, at k x its radius, over angles a0..a1 (0 the right flank, pi/2
 *  the back). Its v texture coordinate runs from the belly (0) to the back (1), for the countershading. */
function loft(prof, x0, x1, k, a0, a1, L, _open, D = DETAIL.lab) {
  const nx = Math.max(3, Math.ceil(D.nx * (x1 - x0) / L)) + 1, na = D.na;
  const pos = new Float32Array((nx + 1) * (na + 1) * 3), uv = new Float32Array((nx + 1) * (na + 1) * 2), idx = [];
  let p = 0, q = 0;
  for (let i = 0; i <= nx; i++) {
    const x = x1 - (x1 - x0) * i / nx, [ry, rz] = prof(x);
    for (let j = 0; j <= na; j++) {
      const a = a0 + (a1 - a0) * j / na;
      pos[p++] = x; pos[p++] = ry * k * Math.sin(a); pos[p++] = rz * k * Math.cos(a);
      uv[q++] = j / na; uv[q++] = 0.5 + 0.5 * Math.sin(a);
    }
  }
  for (let i = 0; i < nx; i++)
    for (let j = 0; j < na; j++) {
      const a = i * (na + 1) + j, b = a + na + 1;
      idx.push(a, a + 1, b, b, a + 1, b + 1);
    }
  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  geo.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
  geo.setIndex(idx); geo.computeVertexNormals();
  geo.userData.own = true;
  return geo;
}

function fringes(g, mat, part, count, cx, l, w, h, rel, prof) {
  const per = Math.max(2, Math.round(count / 2));
  const len = Math.min(w, h) * 0.5 * rel;
  const im = new THREE.InstancedMesh(FRINGE, mat, per * 2);
  const o = new THREE.Object3D();
  let n = 0;
  for (const side of [1, -1])
    for (let i = 0; i < per; i++) {
      const x = cx + (i / Math.max(1, per - 1) - 0.5) * l * 0.85;
      const [ry, rz] = prof(x), y = -ry * 0.1, zr = rz * Math.sqrt(1 - 0.01);
      o.position.set(x, y, side * zr * 0.97);
      o.rotation.set(side * (Math.PI / 2 + 0.5), 0, 0.25 * Math.sin(i * 1.7));
      o.scale.set(len * 0.18, len, len * 0.18);
      o.updateMatrix(); im.setMatrixAt(n++, o.matrix);
    }
  im.userData.part = part; im.userData.rig = { kind: 'gill' }; g.add(im);
}

function fringeAlong(g, mat, part, link, d, ll, chord, ev, rig, k = 1) {
  const per = Math.max(2, Math.round((3 + 14 * ev) * k));
  const im = new THREE.InstancedMesh(FRINGE, mat, per);
  const o = new THREE.Object3D();
  const back = new THREE.Vector3(-1, 0, 0);
  for (let i = 0; i < per; i++) {
    o.position.copy(link.position).addScaledVector(d, (i / (per - 1) - 0.5) * ll * 0.8).addScaledVector(back, chord * 0.4);
    o.quaternion.setFromUnitVectors(UP, back.clone().addScaledVector(d, 0.3).normalize());
    const fl = chord * (0.6 + 1.5 * ev);
    o.scale.set(fl * 0.15, fl, fl * 0.15);
    o.updateMatrix(); im.setMatrixAt(i, o.matrix);
  }
  im.userData.part = part; im.userData.rig = rig; g.add(im);
}

function openings(g, mat, part, count, cx, l, w, h, prof, sphere = SPHERE_LO) {
  for (const side of [1, -1])
    for (let i = 0; i < count; i++) {
      const x = cx + ((i + 0.5) / count - 0.5) * l * 0.7;
      const [ry, rz] = prof(x), a = 0.16;   // a little above the flank's widest line
      const m = new THREE.Mesh(sphere, mat);
      const r = Math.min(l / count, h) * 0.1;
      m.scale.set(r, r, r * 0.35); m.position.set(x, ry * Math.sin(a), side * rz * Math.cos(a) * 0.99);
      m.userData.part = part; m.userData.rig = { kind: 'dark' }; g.add(m);
    }
}

/** Frees what buildBody made (the shared geometries stay). */
export function disposeBody(obj) { obj?.userData?.dispose?.(); obj?.traverse?.(o => { if (o.isInstancedMesh) o.dispose(); }); }
