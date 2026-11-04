// How big things are drawn (2026-11-01, the owner: "animals and plants look gigantic and crowded").
//
// The land is drawn at true scale: a unit is the sim's own metres per unit (meta.units / status.units, ICD §2.1; 4 m on
// the 1 km map). Everything else is drawn at true size too, always:
//
// - an animal's body length (snout to rump) is lengthK x M^(1/3) m (M in kg, on the world's own mass range, net.js;
//   lengthK = 0.1 m kg^-1/3 unless the server sends its own: units.bodyLenM1kg; and each animal's own length,
//   status.bodyLenM, sets a juvenile's);
// - a plant is as tall as the sim's 'plant height' layer says (5 cm .. 40 m).
//
// 2026-11-04, the owner: "why do we still need the creature size slider?? just the camera needs to be WAY more zoomed
// in ... think of it more as a 3d engine". The creature-size multiplier (x3 .. x300) is gone: the view opens down among
// the animals (world3d.js: a third-person camera beside a large animal, a free fly camera at ground level), and from
// far away an animal too small to see is shown as a dot in its clade's colour instead of a blown-up body.
//
// The body model (creatures/body.js, anim.js) is built with a view size of 1.5 x M^(1/3) model units and is
// MODEL_LEN of them long at view size 1; the shader reads the body's mass back from that view size, so the true
// scale is applied as one factor (bodyK, world units per view-size unit) on the instance matrix and divided out in
// the shader (uBodyK).

export const DEFAULT_MPU = 16 / 4;      // metres per world unit (GEO_CELL_M / FCELL; the server's meta overrides it)
export const LENGTH_K = 0.1;            // body length, m per kg^(1/3)  (1 kg: 10 cm; 40 kg: 34 cm; 0.1 kg: 4.6 cm)
export const MODEL_LEN = 1.25;          // snout-to-rump length of the body model at view size 1 (model units)
export const VIEW_SIZE_K = 1.5;         // the model's view size per kg^(1/3) (bodySize in creatures/body.js)
// (the old creature-size settings, removed with the slider: forgotten so they never come back)
try { for (const k of ['nr-creature-mag', 'nr-creature-mag-explore', 'nr-creature-mag-v2', 'nr-creature-mag-explore-v2']) localStorage.removeItem(k); } catch { /* private mode */ }

export class Scale {
  constructor(mpu) {
    this.mpu = mpu > 0 ? mpu : DEFAULT_MPU;
    this.lengthK = LENGTH_K;
  }

  /** The server's real units (meta.units, status.units; ICD §2.1): metres per unit, body length of 1 kg. */
  fromServer(u) {
    if (!u) return false;
    const mpu = +u.metresPerUnit, lk = +u.bodyLenM1kg;
    let changed = false;
    if (mpu > 0 && Math.abs(mpu - this.mpu) > 1e-6) { this.mpu = mpu; changed = true; }
    if (lk > 0 && Math.abs(lk - this.lengthK) > 1e-6) { this.lengthK = lk; changed = true; }
    return changed;
  }

  get upm() { return 1 / this.mpu; }                         // world units per metre
  /** World units per view-size unit of the body model at TRUE size (bodySize(M) = 1.5 M^1/3 view units). The shader's
   *  uBodyK. */
  get bodyK() { return this.lengthK * this.upm / (VIEW_SIZE_K * MODEL_LEN); }
  /** The scale of the marks drawn with the bodies (fights, blood, framing): a 1 kg body's (world units per view-size unit). */
  get markK() { return this.bodyK; }
  /** True body length of a mass, in metres (snout to rump). */
  lengthM(mass) { return this.lengthK * Math.cbrt(Math.max(1e-3, mass)); }

}
