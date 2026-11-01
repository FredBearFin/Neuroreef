// How big things are drawn (2026-11-01, the owner: "animals and plants look gigantic and crowded").
//
// The land is drawn at true scale: a field cell (FCELL = 4 units) is GEO_CELL_M = 218 m, so a unit is 54.5 m (the
// sim's own metres per unit, meta.units / status.units, ICD §2.1). Bodies
// and plants used to be drawn ~100-400x too big (a 1 kg animal 80 m long, shrubs 50 m). Now:
//
// - an animal's body length (snout to rump) is lengthK x M^(1/3) m (M in kg; lengthK = 0.1 m kg^-1/3 unless the
//   server sends its own: units.bodyLenM1kg; and each animal's own length, status.bodyLenM, sets a juvenile's), times
//   the view's magnification;
// - a plant is as tall as the sim's 'plant height' layer says (5 cm .. 40 m), times the plants' magnification.
//
// The overview and the animal cameras magnify (the "creature size x" control, default x20, always labelled as
// magnified): the sim's animals live hundreds of metres apart, so at x1 a 1 kg animal is 10 cm on a 28 km map.
// Plants follow at the fourth root of it (x2.1 at x20, x3.2 at x100): a forest still reads as trees from afar, and a
// magnified animal is not lost under a crown ten times its own height. Explore mode is always x1: true size.
//
// The body model (creatures/body.js, anim.js) is built with a view size of 1.5 x M^(1/3) model units and is
// MODEL_LEN of them long at view size 1; the shader reads the body's mass back from that view size, so the true
// scale is applied as one factor (bodyK, world units per view-size unit) on the instance matrix and divided out in
// the shader (uBodyK).

export const DEFAULT_MPU = 218 / 4;     // metres per world unit (GEO_CELL_M / FCELL)
export const LENGTH_K = 0.1;            // body length, m per kg^(1/3)  (1 kg: 10 cm; 40 kg: 34 cm; 0.1 kg: 4.6 cm)
export const MODEL_LEN = 1.25;          // snout-to-rump length of the body model at view size 1 (model units)
export const VIEW_SIZE_K = 1.5;         // the model's view size per kg^(1/3) (bodySize in creatures/body.js)
export const MAGS = [1, 2, 5, 10, 20, 50, 100];   // the overview's creature magnification steps
export const DEFAULT_MAG = 20;

export class Scale {
  constructor(mpu) {
    this.mpu = mpu > 0 ? mpu : DEFAULT_MPU;
    this.lengthK = LENGTH_K;
    const saved = +localStorage.getItem('nr-mag');
    this.mag = MAGS.includes(saved) ? saved : DEFAULT_MAG;
    this.trueSize = false;              // Explore: x1 whatever the control says
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
  /** The creatures' magnification now (1 in Explore). */
  get creatureMag() { return this.trueSize ? 1 : this.mag; }
  /** The plants' magnification now: the fourth root of the creatures' (1 in Explore). */
  get plantMag() { return this.trueSize ? 1 : Math.pow(this.mag, 0.25); }
  /** World units per view-size unit of the body model (bodySize(M) = 1.5 M^1/3 view units). */
  get bodyK() { return this.lengthK * this.upm * this.creatureMag / (VIEW_SIZE_K * MODEL_LEN); }
  /** True body length of a mass, in metres (snout to rump). */
  lengthM(mass) { return this.lengthK * Math.cbrt(Math.max(1e-3, mass)); }

  step(dir) {
    const i = Math.max(0, Math.min(MAGS.length - 1, MAGS.indexOf(this.mag) + dir));
    this.setMag(MAGS[i]);
  }
  setMag(m) { this.mag = m; try { localStorage.setItem('nr-mag', String(m)); } catch { /* private mode */ } }
}
