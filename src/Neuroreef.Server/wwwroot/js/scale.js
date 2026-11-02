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
// Every view magnifies the animals (the "creature size x" control, always labelled as magnified unless x1): the overview
// and the animal cameras (default x100), and Explore with the vehicles (its own setting, default x10; it was forced to
// x1 until 2026-11-02, which is why the animals "disappeared" there: 5-30 cm bodies in the grass 8 m away). Plants
// follow the overview's at the fourth root of it (x3.2 at x100): a forest still reads as trees from afar, and a
// magnified animal is not lost under a crown ten times its own height; in Explore plants are true size. world3d.js
// also never draws a body shorter than a few pixels (DOT_PX), so a far animal stays a visible speck at any setting.
//
// The body model (creatures/body.js, anim.js) is built with a view size of 1.5 x M^(1/3) model units and is
// MODEL_LEN of them long at view size 1; the shader reads the body's mass back from that view size, so the true
// scale is applied as one factor (bodyK, world units per view-size unit) on the instance matrix and divided out in
// the shader (uBodyK).

export const DEFAULT_MPU = 16 / 4;      // metres per world unit (GEO_CELL_M / FCELL; the server's meta overrides it)
export const LENGTH_K = 0.1;            // body length, m per kg^(1/3)  (1 kg: 10 cm; 40 kg: 34 cm; 0.1 kg: 4.6 cm)
export const MODEL_LEN = 1.25;          // snout-to-rump length of the body model at view size 1 (model units)
export const VIEW_SIZE_K = 1.5;         // the model's view size per kg^(1/3) (bodySize in creatures/body.js)
export const MAGS = [1, 3, 10, 30, 100, 300];   // the creature display-size steps (1: true size)
// 2026-11-02, the owner: "the realistic sizes, you just cannot see them at all. You basically need to have it in 100x
// size for this to be an enjoyable program." The overview and the animal cameras default to x100 (a 1 kg animal drawn
// 10 m long: a few pixels from a 1 km view of an 8 km map, a clear body from a few hundred metres). Explore and the
// vehicles have their own setting, default x10 (a 1 kg animal as big as a dog beside the 1.7 m person, a 40 kg one
// 3.4 m): x100 there would put the person inside the animal it is dropped beside. Both are display only (the sim never
// sees them) and both keep 'true size' (x1) a click away. Saved under new keys, so the old x1 default is not kept.
export const DEFAULT_MAG = 100;
export const DEFAULT_EX_MAG = 10;
const KEY = 'nr-creature-mag', EX_KEY = 'nr-creature-mag-explore';
const load = (k, d) => { try { const v = +localStorage.getItem(k); return MAGS.includes(v) ? v : d; } catch { return d; } };
const save = (k, v) => { try { localStorage.setItem(k, String(v)); } catch { /* private mode, blocked storage */ } };

export class Scale {
  constructor(mpu) {
    this.mpu = mpu > 0 ? mpu : DEFAULT_MPU;
    this.lengthK = LENGTH_K;
    this.mag = load(KEY, DEFAULT_MAG);        // overview, follow, ride-along
    this.exMag = load(EX_KEY, DEFAULT_EX_MAG); // Explore on foot and in the vehicles
    this.explore = false;                     // set by world3d.setMode: which of the two applies now
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
  /** The creatures' display multiplier now (Explore's own setting in Explore). */
  get creatureMag() { return this.explore ? this.exMag : this.mag; }
  /** The plants' magnification now: the fourth root of the creatures' (true size in Explore: the person's own world). */
  get plantMag() { return this.explore ? 1 : Math.pow(this.mag, 0.25); }
  /** World units per view-size unit of the body model (bodySize(M) = 1.5 M^1/3 view units). */
  get bodyK() { return this.lengthK * this.upm * this.creatureMag / (VIEW_SIZE_K * MODEL_LEN); }
  /** True body length of a mass, in metres (snout to rump). */
  lengthM(mass) { return this.lengthK * Math.cbrt(Math.max(1e-3, mass)); }

  /** One step up or down the list, for the setting that applies now. */
  step(dir) {
    const i = Math.max(0, Math.min(MAGS.length - 1, MAGS.indexOf(this.creatureMag) + dir));
    this.setMag(MAGS[i]);
  }
  /** Set (and remember) the multiplier of the setting that applies now. */
  setMag(m) {
    if (!MAGS.includes(m)) return;
    if (this.explore) { this.exMag = m; save(EX_KEY, m); } else { this.mag = m; save(KEY, m); }
  }
}
