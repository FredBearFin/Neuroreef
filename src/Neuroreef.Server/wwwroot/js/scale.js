// How big things are drawn (2026-11-01, the owner: "animals and plants look gigantic and crowded").
//
// The land is drawn at true scale: a unit is the sim's own metres per unit (meta.units / status.units, ICD §2.1; 4 m on
// the 2048-unit, 8 km map). Bodies and plants used to be drawn ~100-400x too big. Now:
//
// - an animal's body length (snout to rump) is lengthK x M^(1/3) m (M in kg, on the world's own mass range, net.js;
//   lengthK = 0.1 m kg^-1/3 unless the server sends its own: units.bodyLenM1kg; and each animal's own length,
//   status.bodyLenM, sets a juvenile's), at true size by default, or "at least" a multiplier's size (below);
// - a plant is as tall as the sim's 'plant height' layer says (5 cm .. 40 m), times the plants' magnification.
//
// The overview and the animal cameras have one creature-size setting, Explore with the vehicles another (both true
// size by default). Plants follow the overview's at the fourth root of it; in Explore plants are true size.
// world3d.js also never draws a body shorter than a few pixels (DOT_PX), so a far animal stays a visible speck.
//
// The body model (creatures/body.js, anim.js) is built with a view size of 1.5 x M^(1/3) model units and is
// MODEL_LEN of them long at view size 1; the shader reads the body's mass back from that view size, so the true
// scale is applied as one factor (bodyK, world units per view-size unit) on the instance matrix and divided out in
// the shader (uBodyK); a body's display factor (multiplier, minimum pixels) is divided out too (world3d.js packK).

export const DEFAULT_MPU = 16 / 4;      // metres per world unit (GEO_CELL_M / FCELL; the server's meta overrides it)
export const LENGTH_K = 0.1;            // body length, m per kg^(1/3)  (1 kg: 10 cm; 40 kg: 34 cm; 0.1 kg: 4.6 cm)
export const MODEL_LEN = 1.25;          // snout-to-rump length of the body model at view size 1 (model units)
export const VIEW_SIZE_K = 1.5;         // the model's view size per kg^(1/3) (bodySize in creatures/body.js)
export const MAGS = [1, 3, 10, 30, 100, 300];   // the creature display-size steps (1: true size)
// 2026-11-02 (first), the owner: "you basically need to have it in 100x size for this to be an enjoyable program": the
// overview defaulted to x100 and Explore to x10. Then (second), the owner: "why do they need to be 100x size? maybe the
// view is WAY too far out? maybe in explore WE are too big?" The cameras were reworked instead (world3d.js: the overview
// opens low over the densest group, 30-80 m across, and zooms from ~1 m to the whole map; the follow camera keeps a few
// body lengths; Explore's person is animal-sized by default, explore.js), and TRUE SIZE is the default everywhere. The
// multiplier stays an option, under new keys so a saved x100/x10 does not override the new default.
//
// The multiplier is "at least this visible", not a plain product (the owner, of a giants world: "shouldn't the 50 t
// animals be HUGE?"): a body up to the 1 kg length (lengthK, 10 cm) is drawn mag times longer; a longer one grows past
// that only as the fourth root of its magnified length, and never below its true length. At x100 a 5 cm animal is drawn
// 5 m, a 1 kg one 10 m, a 40 kg one 14 m and a 20 t one 23 m (true 2.7 m), where a plain x100 drew the giant 270 m long;
// at x10 a giant is at its true size. The body shader is told each body's own factor (world3d.js packs it into the
// action word) so it still reads the true mass from the drawn size.
export const DEFAULT_MAG = 1;
export const DEFAULT_EX_MAG = 1;
const KEY = 'nr-creature-mag-v2', EX_KEY = 'nr-creature-mag-explore-v2';
const load = (k, d) => { try { const v = localStorage.getItem(k); return v !== null && MAGS.includes(+v) ? +v : d; } catch { return d; } };
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
  /** World units per view-size unit of the body model at TRUE size (bodySize(M) = 1.5 M^1/3 view units). The shader's
   *  uBodyK; the magnification is each body's own factor on top of it (dispK). */
  get bodyK() { return this.lengthK * this.upm / (VIEW_SIZE_K * MODEL_LEN); }
  /** The drawn length (m) of a body truly L m long, at the multiplier now ("at least this visible", above). */
  dispM(L) {
    const g = this.creatureMag; if (g <= 1) return L;
    const c = this.lengthK * g, m = L * g;
    return Math.max(L, m <= c ? m : c * Math.pow(m / c, 0.25));
  }
  /** The factor a body truly L m long is drawn larger by (1 at true size). */
  dispK(L) { return L > 0 ? this.dispM(L) / L : 1; }
  /** bodyK times the factor of a 1 kg body: the scale of the marks drawn with the bodies (fights, blood, framing). */
  get markK() { return this.bodyK * this.dispK(this.lengthK); }
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
