// Explore vehicles: their physics, on the console (as the person's walking is). Arcade but grounded: every number is in
// metres and seconds and comes from the server's vehicle table where it has one (meta.explore.vehicles: top speed,
// steepest slope, wading depth, the boat's least depth, the plane's stall speed and ceiling).
//
// T, the terrain: T.at(x, z) -> { g, s, water, depthM } (g the drawn ground and s the water surface, in view units; depthM
// the water's depth in metres), T.moist(x, z) -> 0..1 (the FIELDS moisture), T.uh / T.uv view units per metre across and
// up, T.wrap(v) onto the torus. Input: { fwd, back, left, right, up, down, yawL, yawR, brake } booleans (0/1).

const G = 9.81;
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const sgn = (v) => (v > 0 ? 1 : v < 0 ? -1 : 0);

export class VehicleState {
  constructor(kind, spec) {
    this.kind = kind; this.spec = spec;
    this.x = 0; this.z = 0; this.y = 0;          // view units
    this.yaw = 0; this.pitch = 0; this.roll = 0;  // radians (the sim's heading frame: forward = (cos yaw, sin yaw))
    this.v = 0; this.vy = 0;                      // m/s along the heading, m/s up
    this.steer = 0; this.throttle = 0; this.onGround = true; this.sink = 0;
    this.alive = true; this.blocked = ''; this.stalled = false; this.crashed = ''; this.agl = 0; this.depthM = 0; this.mud = 0;
  }
  get fx() { return Math.cos(this.yaw); }
  get fz() { return Math.sin(this.yaw); }
}

// ---------------------------------------------------------------------------------------------------------------- jeep
/** A four-wheel drive: follows the ground, cannot climb slopes steeper than spec.maxSlopeDeg, slows in mud and water,
 *  and stops at water deeper than spec.wadeDepthM. Steering as a bicycle of a 2.6 m wheelbase; full grip sideways. */
export function stepJeep(s, inp, T, h) {
  const sp = s.spec, uh = T.uh, uv = T.uv, fx = s.fx, fz = s.fz;
  const here = T.at(s.x, s.z);
  const tanMax = Math.tan((sp.maxSlopeDeg || 35) * Math.PI / 180), wade = sp.wadeDepthM || 0.9;
  // the ground's slope under the body: 2 m ahead and behind, 1 m each side
  const a = T.at(s.x + fx * 2 * uh, s.z + fz * 2 * uh), b = T.at(s.x - fx * 2 * uh, s.z - fz * 2 * uh);
  const l = T.at(s.x + fz * uh, s.z - fx * uh), r = T.at(s.x - fz * uh, s.z + fx * uh);
  const slope = (a.g - b.g) / uv / 4, side = (r.g - l.g) / uv / 2;
  const moist = T.moist(s.x, s.z);
  s.mud = here.water ? 0 : clamp((moist - 0.6) / 0.3, 0, 1);
  s.depthM = here.depthM;
  const wet = clamp(here.depthM / wade, 0, 1);
  // drive, brake, gravity along the slope, rolling and air drag, mud and water drag
  const thr = inp.fwd ? 1 : 0, rev = inp.back ? 1 : 0;
  let acc = 0;
  if (thr) acc += s.v >= -0.2 ? 7 : 12;
  if (rev) acc -= s.v > 0.5 ? 12 : 4;
  if (inp.brake) acc -= sgn(s.v) * 14;
  if (s.onGround) acc -= G * Math.sin(Math.atan(slope));
  acc -= sgn(s.v) * (0.35 + 0.0018 * s.v * s.v + 3.5 * s.mud + 5 * wet);
  const holding = !thr && !rev && Math.abs(s.v) < 0.6 && Math.abs(slope) < tanMax;   // the brakes hold it still
  s.v = holding ? 0 : s.v + acc * h;
  const vmax = sp.maxSpeedMps * (1 - 0.6 * s.mud) * (1 - 0.8 * wet);
  s.v = clamp(s.v, -7 * (1 - 0.5 * wet), vmax);
  // steering: less lock at speed
  const want = ((inp.right ? 1 : 0) - (inp.left ? 1 : 0)) * 0.55 / (1 + Math.abs(s.v) / 12);
  s.steer += (want - s.steer) * Math.min(1, h * 6);
  if (s.onGround) s.yaw += (s.v / 2.6) * Math.tan(s.steer) * h;
  // move, unless the way is too steep or too deep
  s.blocked = '';
  if (Math.abs(s.v) > 1e-3) {
    const dir = sgn(s.v), probe = T.at(s.x + dir * fx * 2.5 * uh, s.z + dir * fz * 2.5 * uh);
    const rise = (probe.g - here.g) / uv / 2.5;
    const nx = s.x + fx * s.v * h * uh, nz = s.z + fz * s.v * h * uh, next = T.at(nx, nz);
    if (s.onGround && rise > tanMax) { s.blocked = 'too steep'; s.v = -0.15 * s.v; }
    else if (next.depthM > wade) { s.blocked = 'too deep'; s.v = -0.15 * s.v; }
    else { s.x = T.wrap(nx); s.z = T.wrap(nz); }
  }
  // stay on the ground; leave it off a drop (a crest at speed), fall and land
  const g = T.at(s.x, s.z).g;
  if (s.onGround) {
    if (s.y - g > 0.5 * uv && s.v > 8) { s.onGround = false; s.vy = s.v * Math.sin(s.pitch); }
    else s.y = g;
  }
  if (!s.onGround) {
    s.vy -= G * h; s.y += s.vy * h * uv;
    if (s.y <= g) { s.y = g; s.vy = 0; s.onGround = true; }
  }
  const k = Math.min(1, h * 10);
  s.pitch += (Math.atan(slope) - s.pitch) * k;
  s.roll += (-Math.atan(side) - s.roll) * k;
  s.agl = Math.max(0, (s.y - g) / uv);
}

// ---------------------------------------------------------------------------------------------------------------- boat
/** A patrol boat: floats on the water surface (lakes, sea, rivers), stops where the water is shallower than
 *  spec.minDepthM or on land, and crawls (4 m/s at most) where it is shallower than 1.5 m. */
export function stepBoat(s, inp, T, h) {
  const sp = s.spec, uh = T.uh, uv = T.uv, fx = s.fx, fz = s.fz;
  const here = T.at(s.x, s.z), minD = sp.minDepthM || 0.4;
  s.depthM = here.depthM;
  const thr = (inp.fwd ? 1 : 0) - (inp.back ? 0.5 : 0);
  let acc = thr > 0 ? 3.2 * thr : thr < 0 ? (s.v > 0.3 ? -6 : -2) : 0;
  acc -= sgn(s.v) * (0.15 + 0.012 * s.v * s.v);
  s.v += acc * h;
  if (!thr && Math.abs(s.v) < 0.15) s.v = 0;
  const shallow = here.depthM < 1.5;
  s.v = clamp(s.v, -4, shallow ? Math.min(4, sp.maxSpeedMps) : sp.maxSpeedMps);
  // rudder: some turn at rest (twin outboards), more with speed
  const rud = (inp.right ? 1 : 0) - (inp.left ? 1 : 0);
  s.steer += (rud - s.steer) * Math.min(1, h * 4);
  s.yaw += s.steer * (0.15 + 0.3 * Math.min(1, Math.abs(s.v) / 6)) * (s.v < -0.2 ? -1 : 1) * h;
  s.blocked = '';
  if (Math.abs(s.v) > 1e-3) {
    const nx = s.x + fx * s.v * h * uh, nz = s.z + fz * s.v * h * uh;
    const dir = sgn(s.v), bow = T.at(nx + dir * fx * 4 * uh, nz + dir * fz * 4 * uh);   // the bow, 4 m ahead of the middle
    if (!bow.water) { s.blocked = 'land ahead'; s.v = -0.2 * s.v; }
    else if (bow.depthM < minD) { s.blocked = 'aground'; s.v = -0.2 * s.v; }
    else { s.x = T.wrap(nx); s.z = T.wrap(nz); }
  }
  const w = T.at(s.x, s.z);
  const t = performance.now() / 1000;
  s.y = (w.water ? w.s : w.g) + Math.sin(t * 1.7 + s.x * 40) * 0.08 * uv;
  s.pitch = Math.min(0.1, Math.max(0, s.v) * 0.007) + Math.sin(t * 1.3) * 0.015;   // bow up with speed, a little swell
  s.roll = -s.steer * Math.min(1, Math.abs(s.v) / 10) * 0.12 + Math.sin(t * 1.1 + 1) * 0.02;
  s.agl = 0;
}

// --------------------------------------------------------------------------------------------------------------- plane
/** A light plane (thrust 0.6 g, drag giving spec.maxSpeedMps flat out). Ground: rolls on its wheels (throttle, brake, steering), rotates and lifts off above stall speed with
 *  the nose up; crashes on rough ground or water at speed. Air: flies where its nose points at the speed thrust and drag
 *  and the climb leave it; banks to turn (g tan(roll) / v); below spec.stallMps it loses lift, sinks and drops its nose.
 *  Touching the ground: lands if slow to sink (< 5 m/s), wings near level, nose not down, on dry and flat ground;
 *  otherwise it crashes (s.crashed says why). inp: up / down throttle, fwd nose down, back nose up, left / right roll,
 *  yawL / yawR rudder, brake wheel brakes. */
export function stepPlane(s, inp, T, h) {
  const sp = s.spec, uh = T.uh, uv = T.uv;
  const stall = sp.stallMps || 38;
  s.throttle = clamp(s.throttle + ((inp.up ? 1 : 0) - (inp.down ? 1 : 0)) * 0.6 * h, 0, 1);
  const here = T.at(s.x, s.z), asl = s.y / uv;
  // thrust 0.6 g at full throttle (a light plane climbs at ~30 degrees, not straight up); drag sets the top speed
  const vmax = sp.maxSpeedMps || 110, T0 = 6;
  const ceil = sp.ceilingM || 3000, thrust = T0 * s.throttle * clamp(1 - (asl - ceil) / 400, 0, 1);
  const pitchIn = (inp.back ? 1 : 0) - (inp.fwd ? 1 : 0), rollIn = (inp.right ? 1 : 0) - (inp.left ? 1 : 0), yawIn = (inp.yawR ? 1 : 0) - (inp.yawL ? 1 : 0);
  const drag = (T0 / (vmax * vmax)) * s.v * s.v;
  if (s.onGround) {
    const fx = s.fx, fz = s.fz;
    const a = T.at(s.x + fx * 4 * uh, s.z + fz * 4 * uh), b = T.at(s.x - fx * 4 * uh, s.z - fz * 4 * uh);
    const slope = (a.g - b.g) / uv / 8, gp = Math.atan(slope);
    let acc = thrust - drag - 0.2 - G * Math.sin(gp) - (inp.brake ? 6 : 0);
    s.v = Math.max(0, s.v + acc * h);
    if (s.v < 0.2 && !s.throttle) s.v = 0;
    const steer = (yawIn + rollIn) * 0.5 * Math.min(1, s.v / 4 + 0.15);
    s.yaw += steer * h;
    s.roll += (0 - s.roll) * Math.min(1, h * 8);
    // rotate: the nose comes up with the stick back near flying speed; otherwise it rests on the ground's slope
    if (pitchIn > 0 && s.v > stall * 0.9) s.pitch = Math.min(gp + 0.28, s.pitch + 0.5 * h);
    else s.pitch += (gp - s.pitch) * Math.min(1, h * 8);
    s.x = T.wrap(s.x + fx * s.v * h * uh); s.z = T.wrap(s.z + fz * s.v * h * uh);
    const n = T.at(s.x, s.z);
    s.y = n.g;
    // the runway: steep or rough ground at speed, or water, wrecks it
    if (n.water && n.depthM > 0.3 && s.v > 3) { s.crashed = 'ditched in the water'; return; }
    if (Math.abs(slope) > Math.tan(20 * Math.PI / 180) && s.v > 3) { s.crashed = 'ran into a slope'; return; }
    if (Math.abs(slope) > Math.tan(12 * Math.PI / 180) && s.v > 15) { s.crashed = 'rough ground'; return; }
    if (s.pitch > gp + 0.06 && s.v >= stall) { s.onGround = false; s.sink = 0; }
    s.stalled = false;
    s.agl = 0;
    return;
  }
  // in the air
  s.pitch = clamp(s.pitch + pitchIn * 0.85 * h, -1.3, 1.3);
  s.roll = clamp(s.roll + rollIn * 1.6 * h, -1.4, 1.4);
  if (!rollIn) s.roll *= 1 - 0.5 * h;                          // it rights itself a little
  const q = Math.min(1, (s.v / stall) ** 2);                   // the share of its weight the wings carry
  s.v = Math.max(0, s.v + (thrust - drag - G * Math.sin(s.pitch)) * h);
  s.yaw += (G * Math.tan(s.roll) / Math.max(s.v, 20)) * q * h + yawIn * 0.35 * h;
  s.stalled = s.v < stall;
  if (s.stalled) { s.sink += G * (1 - q) * h; s.pitch -= (1 - q) * 0.9 * h; }
  else s.sink = Math.max(0, s.sink - G * h);
  s.sink += G * (1 - Math.cos(s.roll)) * 0.25 * h;             // a steep bank costs height
  const cp = Math.cos(s.pitch), vy = Math.sin(s.pitch) * s.v * q - s.sink;
  s.vy = vy;
  s.x = T.wrap(s.x + Math.cos(s.yaw) * cp * s.v * h * uh);
  s.z = T.wrap(s.z + Math.sin(s.yaw) * cp * s.v * h * uh);
  s.y += vy * h * uv;
  const n = T.at(s.x, s.z), floor = n.water ? n.s : n.g;
  s.agl = Math.max(0, (s.y - floor) / uv);
  if (s.y <= floor) {
    const fx = s.fx, fz = s.fz;
    const a = T.at(s.x + fx * 4 * uh, s.z + fz * 4 * uh), b = T.at(s.x - fx * 4 * uh, s.z - fz * 4 * uh);
    const slope = Math.abs((a.g - b.g) / uv / 8);
    const sinkRate = -vy;
    if (n.water && n.depthM > 0.3) s.crashed = 'crashed into the water';
    else if (sinkRate > 5) s.crashed = `hit the ground at ${sinkRate.toFixed(0)} m/s`;
    else if (Math.abs(s.roll) > 0.3) s.crashed = 'caught a wing';
    else if (s.pitch < -0.12) s.crashed = 'nosed in';
    else if (slope > Math.tan(10 * Math.PI / 180)) s.crashed = 'landed on a slope';
    s.y = floor;
    if (!s.crashed) { s.onGround = true; s.sink = 0; s.vy = 0; s.roll = 0; }
  }
}
