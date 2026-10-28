// Near models for: hunters. Each entry: shape id -> (K, sp) => list of K.part(...). See kit.js for the contract.
// MOTION: optional per-shape overrides of the motion defaults in anim.js (stride, legAmp, wingAmp, ...).
//
// Shapes: 3 Gliders, 10 Snappers, 11 Prowlers, 12 Stalkers, 13 Leviathans, 19 Herons.
// Conventions used below: every part that moves with the head (eyes, ears, teeth, nose) shares the head's
// pivot, so it stays attached when the head dips; the lower jaw hinges just below the head pivot; all the
// segments of one leg share the hip pivot.

// ------------------------------------------------------------------ local helpers

/**
 * K.spine, made to face outward. kit.js's spine currently winds its triangles inward (the body renders
 * inside out under back-face culling); this checks the signed volume and flips the winding only when it
 * is negative, so it stays correct if kit.js is fixed.
 */
function spine(K, stations, seg) {
  const g = K.spine(stations, seg), ix = g.index.array, p = g.attributes.position.array;
  let vol = 0;
  for (let i = 0; i < ix.length; i += 3) {
    const a = ix[i] * 3, b = ix[i + 1] * 3, c = ix[i + 2] * 3;
    vol += p[a] * (p[b + 1] * p[c + 2] - p[b + 2] * p[c + 1]) - p[a + 1] * (p[b] * p[c + 2] - p[b + 2] * p[c]) + p[a + 2] * (p[b] * p[c + 1] - p[b + 1] * p[c]);
  }
  if (vol < 0) for (let i = 0; i < ix.length; i += 3) { const t = ix[i + 1]; ix[i + 1] = ix[i + 2]; ix[i + 2] = t; }
  return g;
}

/** A chain of tapered limbs through points pts with radii rs (one per point); roles per segment. */
function chain(K, pts, rs, { p, pivot, roles, seg = 4 }) {
  const out = [];
  for (let i = 0; i < pts.length - 1; i++) {
    const role = Array.isArray(roles) ? roles[Math.min(i, roles.length - 1)] : roles;
    out.push(K.part(K.limb(pts[i], pts[i + 1], rs[i], rs[i + 1], Array.isArray(seg) ? seg[i] : seg), { p, pivot, role }));
  }
  return out;
}

/** A pair of cheap eyes at (x, y, ±z) that move with the head about the head pivot hp. */
function eyes(K, x, y, z, r, hp) {
  return [1, -1].map((s) => K.part(K.ellipsoid(r, r, r * 0.9, x, y, s * z, 4), { p: K.P.HEAD, pivot: hp, role: K.R.EYE }));
}

/** A tooth or claw: a thin flat triangle at (x, y, z), pointing down (dir -1) or up (dir 1). */
function tooth(K, x, y, z, len, w, dir, p, pivot, lean = 0) {
  return K.part(K.flat([[-w / 2, 0], [w / 2, 0], [lean, dir * len]], 0.025, 'xy', x, y, z), { p, pivot, role: K.R.HORN });
}

/** A pair of pointed ears facing forward, base at (x, y, ±z), height h, splayed outward. */
function ears(K, x, y, z, h, w, hp, role, splay = 0.3, back = 0.15) {
  return [1, -1].map((s) => {
    const g = K.flat([[-w / 2, 0], [w / 2, 0], [-back * h, h]], 0.025, 'xy');
    g.rotateY(Math.PI / 2); g.rotateX(-s * splay); g.translate(x, y, s * z);
    return K.part(g, { p: K.P.HEAD, pivot: hp, role });
  });
}

/** A horizontal wing from an outline [[u, v], ...] (v outward, > 0), mirrored to both sides. */
function wings(K, outline, y, pivotX, pivotZ, role, thick = 0.025) {
  return [1, -1].map((s) => K.part(K.flat(outline.map(([u, v]) => [u, s * v]), thick, 'xz', 0, y, 0),
    { p: s > 0 ? K.P.WING_L : K.P.WING_R, pivot: [pivotX, y, s * pivotZ], role }));
}

// ------------------------------------------------------------------ models

export const MODELS = {
  // Gliders: a fruit-eating flying fox. Broad scalloped membranes with bony leading edges, a short foxy
  // muzzle, big round ears and wide-set eyes (prey: it watches all round), a fluffy tail, little hind feet.
  3: (K) => {
    const { P, R } = K, hp = [0.3, 0.37, 0];
    return [
      K.part(spine(K, [[-0.42, 0.3, 0.04, 0.04], [-0.3, 0.31, 0.13, 0.14], [-0.05, 0.32, 0.19, 0.19], [0.2, 0.34, 0.17, 0.16], [0.34, 0.37, 0.11, 0.11]], 8), { p: P.BODY }),
      K.part(K.ellipsoid(0.12, 0.06, 0.1, 0.06, 0.2, 0, 5), { p: P.BODY, role: R.PALE }),           // pale belly ruff
      // head
      K.part(spine(K, [[0.28, 0.41, 0.1, 0.11], [0.41, 0.43, 0.13, 0.13], [0.54, 0.4, 0.08, 0.08], [0.64, 0.37, 0.04, 0.045]], 6), { p: P.HEAD, pivot: hp }),
      K.part(K.ellipsoid(0.03, 0.025, 0.03, 0.66, 0.375, 0, 4), { p: P.HEAD, pivot: hp, role: R.DARK }),
      K.part(K.ellipsoid(0.07, 0.05, 0.08, 0.53, 0.35, 0, 4), { p: P.HEAD, pivot: hp, role: R.PALE }), // pale muzzle
      ...eyes(K, 0.5, 0.47, 0.095, 0.042, hp),
      ...ears(K, 0.38, 0.5, 0.08, 0.16, 0.12, hp, R.ACCENT, 0.5, 0.05),
      // wings: membrane plus a bony leading edge and one finger strut each
      ...wings(K, [[0.24, 0.1], [0.2, 0.45], [0.08, 0.9], [-0.04, 1.1], [-0.14, 0.94], [-0.12, 0.78], [-0.27, 0.64], [-0.24, 0.44], [-0.37, 0.3], [-0.3, 0.1]], 0.36, 0.1, 0.12, R.ACCENT),
      ...[1, -1].map((s) => K.part(K.limb([0.22, 0.37, s * 0.12], [0.08, 0.37, s * 0.9], 0.03, 0.015, 3), { p: s > 0 ? P.WING_L : P.WING_R, pivot: [0.1, 0.36, s * 0.12], role: R.DARK })),
      ...[1, -1].map((s) => K.part(K.limb([0.1, 0.37, s * 0.6], [-0.26, 0.37, s * 0.63], 0.02, 0.01, 3), { p: s > 0 ? P.WING_L : P.WING_R, pivot: [0.1, 0.36, s * 0.12], role: R.DARK })),
      // fluffy tail with a little membrane fan
      K.part(spine(K, [[-0.4, 0.31, 0.05, 0.05], [-0.55, 0.32, 0.07, 0.08], [-0.75, 0.33, 0.04, 0.05]], 5), { p: P.TAIL, pivot: [-0.38, 0.31, 0], role: R.ACCENT }),
      K.part(K.flat([[-0.34, 0.12], [-0.62, 0.2], [-0.8, 0.04], [-0.8, -0.04], [-0.62, -0.2], [-0.34, -0.12]], 0.02, 'xz', 0, 0.31, 0), { p: P.TAIL, pivot: [-0.38, 0.31, 0], role: R.BODY }),
      // hind feet
      ...[[P.LEG_BL, 1], [P.LEG_BR, -1]].flatMap(([p, s]) => chain(K, [[-0.22, 0.24, s * 0.09], [-0.18, 0.1, s * 0.1], [-0.2, 0, s * 0.1]], [0.035, 0.025, 0.02], { p, pivot: [-0.22, 0.24, s * 0.09], roles: R.DARK, seg: 3 })),
    ];
  },

  // Snappers: a swift-like insect hawker. Long sickle wings, a forked tail, a big round head with forward
  // eyes and a wide gape (the lower bill is the jaw and snaps open on a strike).
  10: (K) => {
    const { P, R } = K, hp = [0.26, 0.36, 0];
    return [
      K.part(spine(K, [[-0.34, 0.3, 0.03, 0.03], [-0.2, 0.31, 0.11, 0.11], [0.04, 0.32, 0.15, 0.14], [0.24, 0.34, 0.13, 0.13], [0.34, 0.35, 0.08, 0.08]], 8), { p: P.BODY }),
      K.part(K.ellipsoid(0.13, 0.12, 0.13, 0.34, 0.38, 0, 6), { p: P.HEAD, pivot: hp }),
      K.part(K.ellipsoid(0.08, 0.06, 0.09, 0.34, 0.29, 0, 4), { p: P.HEAD, pivot: hp, role: R.PALE }),   // pale throat
      K.part(spine(K, [[0.4, 0.37, 0.03, 0.08], [0.48, 0.355, 0.02, 0.05], [0.54, 0.34, 0.01, 0.02]], 5), { p: P.HEAD, pivot: hp, role: R.DARK }),  // upper bill
      K.part(spine(K, [[0.38, 0.33, 0.025, 0.08], [0.47, 0.33, 0.015, 0.05], [0.52, 0.33, 0.01, 0.02]], 5), { p: P.JAW, pivot: [0.36, 0.35, 0], role: R.HORN }), // lower bill (gape)
      ...eyes(K, 0.43, 0.42, 0.075, 0.04, hp),
      K.part(K.flat([[0.3, 0.5], [0.42, 0.47], [0.38, 0.43], [0.26, 0.44]], 0.06, 'xy'), { p: P.HEAD, pivot: hp, role: R.DARK }), // brow cap
      // sickle wings: dark primaries over a body-coloured arm
      ...wings(K, [[0.16, 0.06], [0.12, 0.3], [-0.06, 0.52], [-0.34, 0.68], [-0.62, 0.72], [-0.36, 0.56], [-0.16, 0.36], [-0.1, 0.06]], 0.36, 0.1, 0.08, R.ACCENT),
      ...wings(K, [[-0.1, 0.42], [-0.34, 0.66], [-0.62, 0.72], [-0.38, 0.58], [-0.2, 0.42]], 0.375, 0.1, 0.08, R.DARK, 0.02),
      // forked tail
      K.part(K.flat([[-0.22, 0.05], [-0.6, 0.15], [-0.42, 0.01], [-0.42, -0.01], [-0.6, -0.15], [-0.22, -0.05]], 0.02, 'xz', 0, 0.31, 0), { p: P.TAIL, pivot: [-0.26, 0.31, 0], role: R.DARK }),
      // tiny perching legs
      ...[[P.LEG_BL, 1], [P.LEG_BR, -1]].flatMap(([p, s]) => chain(K, [[0.02, 0.2, s * 0.05], [0.04, 0.0, s * 0.05], [0.1, 0.0, s * 0.05]], [0.022, 0.015, 0.01], { p, pivot: [0.02, 0.2, s * 0.05], roles: R.DARK, seg: 3 })),
    ];
  },

  // Prowlers: a lithe, low-slung cat. Long body, a short-faced skull with forward eyes, fangs and a jaw that
  // opens on a strike, pointed ears, powerful haunches with a hock, and a long dark-tipped tail.
  11: (K) => {
    const { P, R } = K, hp = [0.78, 0.8, 0], jp = [0.84, 0.76, 0];
    const front = (p, s) => [...chain(K, [[0.5, 0.64, s * 0.15], [0.56, 0.3, s * 0.15], [0.53, 0.05, s * 0.15]], [0.09, 0.055, 0.045], { p, pivot: [0.5, 0.64, s * 0.15], roles: [R.ACCENT, R.DARK], seg: [5, 4] }),
      K.part(K.box(0.13, 0.05, 0.09, 0.57, 0.025, s * 0.15), { p, pivot: [0.5, 0.64, s * 0.15], role: R.DARK })];
    const back = (p, s) => [...chain(K, [[-0.55, 0.66, s * 0.15], [-0.38, 0.36, s * 0.15], [-0.6, 0.13, s * 0.15], [-0.56, 0.04, s * 0.15]], [0.12, 0.06, 0.045, 0.04], { p, pivot: [-0.55, 0.66, s * 0.15], roles: [R.ACCENT, R.DARK, R.DARK], seg: [5, 4, 4] }),
      K.part(K.box(0.13, 0.05, 0.09, -0.52, 0.025, s * 0.15), { p, pivot: [-0.55, 0.66, s * 0.15], role: R.DARK })];
    return [
      K.part(spine(K, [[-0.95, 0.64, 0.05, 0.05], [-0.82, 0.66, 0.19, 0.17], [-0.5, 0.64, 0.24, 0.21], [-0.1, 0.6, 0.21, 0.19], [0.3, 0.64, 0.25, 0.21], [0.6, 0.71, 0.22, 0.18], [0.8, 0.79, 0.12, 0.11]], 8), { p: P.BODY }),
      // head
      K.part(spine(K, [[0.72, 0.84, 0.12, 0.12], [0.86, 0.87, 0.17, 0.16], [1.0, 0.84, 0.15, 0.14], [1.12, 0.78, 0.09, 0.1], [1.18, 0.76, 0.05, 0.06]], 6), { p: P.HEAD, pivot: hp }),
      K.part(K.ellipsoid(0.035, 0.03, 0.04, 1.19, 0.78, 0, 4), { p: P.HEAD, pivot: hp, role: R.DARK }),
      ...eyes(K, 1.04, 0.9, 0.085, 0.04, hp),
      ...ears(K, 0.86, 0.97, 0.1, 0.15, 0.11, hp, R.ACCENT),
      tooth(K, 1.1, 0.74, 0.045, 0.08, 0.035, -1, P.HEAD, hp), tooth(K, 1.1, 0.74, -0.045, 0.08, 0.035, -1, P.HEAD, hp),
      // lower jaw
      K.part(K.limb([0.86, 0.71, 0], [1.14, 0.69, 0], 0.07, 0.035, 5), { p: P.JAW, pivot: jp }),
      tooth(K, 1.07, 0.7, 0.04, 0.05, 0.03, 1, P.JAW, jp), tooth(K, 1.07, 0.7, -0.04, 0.05, 0.03, 1, P.JAW, jp),
      // legs
      ...front(P.LEG_FL, 1), ...front(P.LEG_FR, -1), ...back(P.LEG_BL, 1), ...back(P.LEG_BR, -1),
      // tail
      ...chain(K, [[-0.92, 0.66, 0], [-1.3, 0.72, 0], [-1.6, 0.92, 0], [-1.74, 1.14, 0]], [0.07, 0.055, 0.045, 0.03], { p: P.TAIL, pivot: [-0.92, 0.66, 0], roles: [R.BODY, R.BODY, R.DARK] }),
    ];
  },

  // Stalkers: a lean, deep-chested pack hunter (part wolf, part hyena). Sloping back with a dark spiked
  // mane, a long muzzle with a long toothed jaw, forward eyes, tall ears, long legs and a bushy tail.
  12: (K) => {
    const { P, R } = K, hp = [0.84, 1.08, 0], jp = [0.9, 1.02, 0];
    const front = (p, s) => [...chain(K, [[0.5, 0.92, s * 0.14], [0.58, 0.46, s * 0.14], [0.55, 0.05, s * 0.14]], [0.085, 0.055, 0.04], { p, pivot: [0.5, 0.92, s * 0.14], roles: [R.ACCENT, R.DARK], seg: [5, 4] }),
      K.part(K.box(0.13, 0.05, 0.08, 0.6, 0.025, s * 0.14), { p, pivot: [0.5, 0.92, s * 0.14], role: R.DARK })];
    const back = (p, s) => chain(K, [[-0.55, 0.88, s * 0.14], [-0.36, 0.5, s * 0.14], [-0.62, 0.19, s * 0.14], [-0.5, 0.02, s * 0.14]], [0.11, 0.06, 0.04, 0.045], { p, pivot: [-0.55, 0.88, s * 0.14], roles: [R.ACCENT, R.DARK, R.DARK], seg: [5, 4, 4] });
    return [
      K.part(spine(K, [[-0.9, 0.84, 0.05, 0.05], [-0.78, 0.86, 0.17, 0.15], [-0.45, 0.84, 0.2, 0.17], [-0.05, 0.86, 0.24, 0.18], [0.35, 0.93, 0.31, 0.21], [0.62, 1.0, 0.27, 0.19], [0.82, 1.06, 0.14, 0.13]], 7), { p: P.BODY }),
      K.part(K.ellipsoid(0.12, 0.17, 0.12, 0.68, 0.88, 0, 5), { p: P.BODY, role: R.PALE }),           // pale throat and chest
      // spiked mane along the neck and shoulders
      K.part(K.flat([[0.0, 1.03], [0.86, 1.12], [0.9, 1.3], [0.76, 1.23], [0.7, 1.42], [0.56, 1.3], [0.48, 1.44], [0.34, 1.28], [0.25, 1.37], [0.13, 1.18], [0.04, 1.23]], 0.06, 'xy'), { p: P.BODY, role: R.DARK }),
      // head
      K.part(spine(K, [[0.8, 1.12, 0.13, 0.12], [0.95, 1.14, 0.16, 0.14], [1.1, 1.08, 0.11, 0.1], [1.3, 1.0, 0.07, 0.07], [1.4, 0.97, 0.04, 0.05]], 6), { p: P.HEAD, pivot: hp }),
      K.part(K.ellipsoid(0.035, 0.03, 0.04, 1.41, 0.98, 0, 4), { p: P.HEAD, pivot: hp, role: R.DARK }),
      ...eyes(K, 1.07, 1.17, 0.08, 0.035, hp),
      ...ears(K, 0.9, 1.24, 0.08, 0.2, 0.1, hp, R.DARK, 0.2, 0.25),
      tooth(K, 1.31, 0.96, 0.04, 0.07, 0.03, -1, P.HEAD, hp), tooth(K, 1.31, 0.96, -0.04, 0.07, 0.03, -1, P.HEAD, hp),
      // long lower jaw
      K.part(K.limb([0.92, 0.98, 0], [1.37, 0.92, 0], 0.07, 0.03, 5), { p: P.JAW, pivot: jp }),
      tooth(K, 1.27, 0.93, 0.035, 0.05, 0.025, 1, P.JAW, jp), tooth(K, 1.27, 0.93, -0.035, 0.05, 0.025, 1, P.JAW, jp),
      // legs
      ...front(P.LEG_FL, 1), ...front(P.LEG_FR, -1), ...back(P.LEG_BL, 1), ...back(P.LEG_BR, -1),
      // bushy tail
      K.part(spine(K, [[-0.88, 0.85, 0.05, 0.05], [-1.05, 0.8, 0.09, 0.08], [-1.3, 0.66, 0.1, 0.09], [-1.5, 0.5, 0.05, 0.05]], 5), { p: P.TAIL, pivot: [-0.86, 0.85, 0], role: R.ACCENT }),
    ];
  },

  // Leviathans: the armoured apex predator. A massive barrel body under a ridge of dorsal plates, column
  // legs, a huge horned head with a toothed lower jaw that gapes on a strike, and a spiked club of a tail.
  13: (K) => {
    const { P, R } = K, hp = [1.05, 1.52, 0], jp = [1.12, 1.38, 0];
    const leg = (p, x, s, mid, foot, claws) => [...chain(K, [[x, 1.02, s * 0.46], [mid, 0.5, s * 0.48], [foot, 0.0, s * 0.48]], [0.22, 0.15, 0.2], { p, pivot: [x, 1.02, s * 0.46], roles: [R.BODY, R.DARK], seg: [5, 4] }),
      ...(claws ? [1, -1].map((c) => tooth(K, foot + 0.2, 0.1, s * 0.48 + c * 0.08, 0.1, 0.06, -1, p, [x, 1.02, s * 0.46], 0.08)) : [])];
    return [
      K.part(spine(K, [[-1.45, 1.15, 0.1, 0.1], [-1.2, 1.22, 0.46, 0.46], [-0.6, 1.3, 0.68, 0.7], [0.2, 1.35, 0.72, 0.72], [0.85, 1.38, 0.58, 0.56], [1.2, 1.44, 0.34, 0.34]], 7), { p: P.BODY }),
      // dorsal plate ridge
      K.part(K.flat([[-1.1, 1.6], [0.95, 1.8], [1.0, 1.98], [0.85, 1.92], [0.72, 2.25], [0.5, 2.02], [0.35, 2.38], [0.1, 2.08], [-0.05, 2.42], [-0.3, 2.06], [-0.45, 2.36], [-0.7, 1.96], [-0.85, 2.18], [-1.1, 1.75]], 0.09, 'xy'), { p: P.BODY, role: R.DARK }),
      // head
      K.part(spine(K, [[1.0, 1.6, 0.32, 0.34], [1.35, 1.66, 0.36, 0.38], [1.7, 1.56, 0.27, 0.3], [2.0, 1.45, 0.17, 0.22], [2.15, 1.42, 0.08, 0.13]], 6), { p: P.HEAD, pivot: hp }),
      ...[1, -1].map((s) => K.part(K.cone([1.25, 1.92, s * 0.2], [0.82, 2.32, s * 0.44], 0.1, 3), { p: P.HEAD, pivot: hp, role: R.HORN })),
      ...eyes(K, 1.72, 1.76, 0.22, 0.05, hp),
      ...[[1.78, 0.22], [2.0, 0.14]].flatMap(([x, z]) => [1, -1].map((s) => tooth(K, x, 1.33, s * z, 0.13, 0.07, -1, P.HEAD, hp))),
      // lower jaw
      K.part(spine(K, [[1.1, 1.3, 0.17, 0.3], [1.5, 1.17, 0.15, 0.28], [1.92, 1.2, 0.09, 0.19], [2.08, 1.24, 0.04, 0.1]], 5), { p: P.JAW, pivot: jp }),
      ...[[1.9, 0.17]].flatMap(([x, z]) => [1, -1].map((s) => tooth(K, x, 1.27, s * z, 0.1, 0.06, 1, P.JAW, jp))),
      // legs
      ...leg(P.LEG_FL, 0.7, 1, 0.8, 0.74, true), ...leg(P.LEG_FR, 0.7, -1, 0.8, 0.74, true),
      ...leg(P.LEG_BL, -0.75, 1, -0.62, -0.74, false), ...leg(P.LEG_BR, -0.75, -1, -0.62, -0.74, false),
      // tail with a spiked club
      K.part(spine(K, [[-1.35, 1.25, 0.32, 0.3], [-1.8, 1.05, 0.22, 0.2], [-2.3, 0.85, 0.12, 0.11], [-2.7, 0.7, 0.04, 0.04]], 5), { p: P.TAIL, pivot: [-1.3, 1.2, 0] }),
      K.part(K.flat([[-1.5, 1.4], [-2.55, 0.8], [-2.62, 0.98], [-2.38, 1.08], [-2.28, 0.96], [-2.0, 1.32], [-1.9, 1.16], [-1.62, 1.6]], 0.08, 'xy'), { p: P.TAIL, pivot: [-1.3, 1.2, 0], role: R.HORN }),
    ];
  },

  // Herons: a wading bird on stilt legs. Teardrop body, an S-curved neck that strikes and dips from its
  // base, a dagger beak, a dark crest plume, pale breast plumes, folded grey wings and long toes.
  19: (K) => {
    const { P, R } = K, np = [0.4, 1.9, 0];
    const leg = (p, s) => [...chain(K, [[0.02, 1.58, s * 0.1], [-0.07, 0.82, s * 0.1], [0.03, 0.05, s * 0.1]], [0.04, 0.03, 0.025], { p, pivot: [0.02, 1.58, s * 0.1], roles: R.ACCENT }),
      K.part(K.flat([[-0.13, 0.012], [0.0, 0.02], [0.17, 0.08], [0.03, 0.0], [0.18, 0.0], [0.03, -0.0], [0.17, -0.08], [0.0, -0.02], [-0.13, -0.012]], 0.02, 'xz', 0.03, 0.012, s * 0.1), { p, pivot: [0.02, 1.58, s * 0.1], role: R.DARK })];
    const wing = (s) => {
      const parts = [];
      for (const [outline, role, off] of [
        [[[0.18, 0], [0.05, -0.05], [-0.45, -0.24], [-0.85, -0.3], [-0.95, -0.22], [-0.5, -0.02]], R.ACCENT, 0],
        [[[-0.45, -0.2], [-0.85, -0.3], [-1.0, -0.24], [-0.6, -0.12]], R.DARK, 0.015]]) {
        const g = K.flat(outline, 0.02, 'xy');
        g.rotateX(-s * 0.3); g.translate(0.3, 1.98, s * (0.2 + off));
        parts.push(K.part(g, { p: s > 0 ? P.WING_L : P.WING_R, pivot: [0.3, 1.98, s * 0.18], role }));
      }
      return parts;
    };
    return [
      K.part(spine(K, [[-0.75, 1.58, 0.02, 0.04], [-0.55, 1.63, 0.11, 0.15], [-0.25, 1.7, 0.23, 0.23], [0.1, 1.78, 0.27, 0.25], [0.35, 1.86, 0.2, 0.19], [0.48, 1.92, 0.08, 0.08]], 8), { p: P.BODY }),
      K.part(K.flat([[0.44, 1.96], [0.56, 1.96], [0.5, 1.66], [0.42, 1.5], [0.36, 1.74]], 0.12, 'xy'), { p: P.BODY, role: R.PALE }),   // breast plumes
      // neck and head (all one part, hinged at the neck base so the whole neck dips and lunges)
      ...chain(K, [[0.4, 1.9, 0], [0.6, 2.12, 0], [0.56, 2.36, 0], [0.74, 2.56, 0]], [0.075, 0.05, 0.045, 0.045], { p: P.HEAD, pivot: np, roles: R.BODY, seg: 5 }),
      K.part(K.ellipsoid(0.12, 0.085, 0.08, 0.8, 2.6, 0, 6), { p: P.HEAD, pivot: np }),
      K.part(K.cone([0.88, 2.61, 0], [1.46, 2.55, 0], 0.04, 4), { p: P.HEAD, pivot: np, role: R.HORN }),
      K.part(K.cone([0.88, 2.575, 0], [1.4, 2.55, 0], 0.03, 4), { p: P.HEAD, pivot: np, role: R.HORN }),
      K.part(K.flat([[0.8, 2.67], [0.5, 2.62], [0.36, 2.55], [0.52, 2.6], [0.78, 2.63]], 0.03, 'xy'), { p: P.HEAD, pivot: np, role: R.DARK }), // crest plume
      ...eyes(K, 0.87, 2.63, 0.06, 0.025, np),
      ...wing(1), ...wing(-1),
      K.part(K.flat([[-0.5, 0.09], [-0.86, 0.06], [-0.9, 0.0], [-0.86, -0.06], [-0.5, -0.09]], 0.03, 'xz', 0, 1.62, 0), { p: P.TAIL, pivot: [-0.55, 1.62, 0], role: R.ACCENT }),
      // stilt legs: two-legged, so only the back-leg ids (they step in antiphase)
      ...leg(P.LEG_BL, 1), ...leg(P.LEG_BR, -1),
    ];
  },
};

export const MOTION = {
  3: { wingFreq: 7, wingAmp: 0.5, hover: 0.12, legAmp: 0.3, tailAmp: 0.3 },       // slow soaring beats
  10: { wingFreq: 22, wingAmp: 0.8, hover: 0.1, legAmp: 0.2, tailAmp: 0.35 },     // fast flickering beats
  11: { stride: 9, legAmp: 0.6, tailAmp: 0.35, headDip: 0.45 },
  12: { stride: 9.5, legAmp: 0.62, tailAmp: 0.22, headDip: 0.45 },
  13: { stride: 4.2, legAmp: 0.32, tailAmp: 0.16, headDip: 0.3 },                 // heavy, slow tread
  19: { stride: 5, legAmp: 0.3, wingFreq: 5, wingAmp: 0.45, hover: 0.08, headDip: 0.95, tailAmp: 0.1 },
};
