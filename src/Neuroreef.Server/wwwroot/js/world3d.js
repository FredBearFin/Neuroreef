// The 3D view: terrain, water, vegetation, fire, and every organism drawn from its own genes (creatures/body.js):
// one instanced mesh of detailed, animated bodies near the camera and one of low-detail bodies beyond it.
// World x maps to three.js x, world y maps to three.js z, height is three.js y. The world is a torus:
// the terrain is drawn as a 3x3 tiling around the camera, and every animal is drawn at its copy nearest
// to the view centre; in the overview the copies more than half a map from the view centre are dimmed and a
// thin line marks where the map wraps (wrapPatch), so one map is shown, never a grid of repeats.

import * as THREE from 'three';
import { OrbitControls } from '../lib/OrbitControls.js';
import { agentState, agentGenes, REC, massRangeVersion } from './net.js';
import { Scale, MODEL_LEN } from './scale.js';
import { Look } from './look/look.js';
import { bodyGeometry, bodyMaterial, bodyMesh, beginBodies, placeBody, finishBodies, bodySize, appearanceColor } from './creatures/body.js';
import { Ground, CARVE_NONE } from './terrain/ground.js';
import { fromWire, fromCells, stampRivers, ribbonGeometry, patchRiverMaterial, riverWidth, preparePath } from './terrain/rivers.js';
import { decodeFeatures, stampFeatures, featureMeshes } from './terrain/caves.js';
import { patchLakeMaterial } from './terrain/water.js';
import { splitChunks, copyColors } from './terrain/chunks.js';

// Vertical scale (2026-11-01). Elevation is 0..1 with the lakes at w.sea; Climate reads (e - sea) / (1 - sea) x the map's
// vertical scale (Map.ReliefM, sent as meta.units.reliefM and meta.explore.hRelief) as the height above the lakes. Geology
// measures slopes on a field cell of GEO_CELL_M (meta.units.fieldCellM), so the land is drawn at true scale: a cell
// (FCELL = 4 units) is fieldCellM m, and the relief is RELIEF = reliefM x FCELL / fieldCellM units (no exaggeration).
// 2026-11-01, the 1 km world: a cell is 16 m and the 256 map's vertical scale 153 m, so RELIEF = 38 units (it was
// 8000 x 4 / 218 = 146.8 for 8 km on 218 m cells); 512 / 1024 / 2048 maps: 64 / 108 / 181 units. Derived from the server's
// meta (setRelief), never hard-coded. Elevation comes as 16 bits when the WORLD frame has the plane.
export let RELIEF = 153 * 4 / 16;   // until meta arrives (exported, a live binding, for Explore mode, js/explore/)
/** RELIEF from the server's units: the map's vertical scale over a field cell's metres, x FCELL units. */
export function setRelief(meta) {
  const u = (meta && meta.units) || {}, ex = (meta && meta.explore) || {};
  const h = +u.reliefM || +ex.hRelief, cell = +u.fieldCellM || +ex.geoCellM, fc = +u.fcell || 4;
  if (h > 0 && cell > 0) RELIEF = h * fc / cell;
  return RELIEF;
}
const SHADE_Z = 1;           // lighting normals are computed with the relief x this (a hillshade z-factor; the geometry is not)
const LAND_LIFT = 0.06;      // land sits this far above the level of the water beside it, so the water's flat surface never shows through it
const CHUNKS = 8;             // river ribbons and karst rocks are cut into CHUNKS x CHUNKS squares of the map ...
const RIBBON_FAR = 420, KARST_FAR = 260;   // ... and drawn only within this distance of the camera (units)
const RIVER_W0 = 1.0, RIVER_WMAX = 3.6;   // river water width (units) at the river threshold, and the widest (a cell is 4)
export const SOUND_RGB = [[1, 0.35, 0.35], [1, 0.7, 0.25], [1, 0.95, 0.3], [0.45, 1, 0.45], [0.3, 0.95, 1], [0.4, 0.55, 1], [0.8, 0.45, 1], [1, 0.5, 0.85]];   // the 8 sounds
const CALL_MAX = 768;         // call rings drawn at once (up to 3 per utterance)
const FX_MAX = 256;           // fight flashes and blood stains drawn at once
const STAIN_MAX = 1024;       // blood stains on the ground
const BLOOD_MAX = 2500;       // blood droplets in the air at once
const CORPSE_LIFE = 40000;    // a killed animal's body lies this long (ms), sinking for the last quarter
const NEAR_PX = 22;          // organisms drawn longer than this on screen (pixels) get the detailed body
const SWIM_FINS = (0.3 - 0.1) / 0.9;   // fins at which a body swims in deep water (Body.GroundRealm: 0.1 + 0.9 fins >= DEEP_SWIM 0.3)
const FLY_VM = 90;           // metres per step x M^0.17 above which a flier is taken to be in the air (a landed one walks: _air)
const DOT_PX = 3;            // no organism is drawn shorter than this on screen (a speck where it is, never a carpet; 2026-11-02:
                             // in every view, not only the overview, so a far animal never vanishes in Explore or a vehicle)
/** A body's display factor (drawn size / true size, >= 1) packed above the action bits of aAnim.z: log2 x 64 in 11 bits
 *  from 8192 up (to 2^32; a float holds the sum exactly), read back by the body shader (anim.js dispK). */
const packK = (f) => Math.min(2047, Math.max(0, Math.round(Math.log2(Math.max(1, f)) * 64))) * 8192;
const GROUP_M = 40;          // the overview's groups of animals: bins this many metres across (N, Tab, the opening view)
const VIEW_M_MIN = 30, VIEW_M_MAX = 80;   // ... framed in a view this many metres across
const MIN_DIST_M = 1;        // the overview zooms in to this distance (m): a 10 cm animal about a sixth of the view
const PAN_KEYS = ['KeyW', 'KeyA', 'KeyS', 'KeyD', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'KeyQ', 'KeyE', 'Minus', 'Equal'];
const SLOPE_SC = 0.25;       // a far body drawn longer than this (world units, ~1 m) is sheared to the slope too
const NEAR_CAP = 512;        // at most this many detailed bodies; the rest get the low-detail one (2026-10-31: 4096 cost the GPU
                             // a detailed body for every animal in a crowd, where a few hundred fill the near view)
const FAR_BUDGET = 16000;    // at most about this many low-detail bodies per frame (a sample of the far ones when there are more)
const DIRX = [1, 1, 0, -1, -1, -1, 0, 1], DIRY = [0, 1, 1, 1, 0, -1, -1, -1];   // the WORLD frame's river directions
const NCLADES = 32;

export class World3D {
  constructor(canvas, meta) {
    this.meta = meta;
    this.W = meta.world;
    setRelief(meta);                  // the relief at true scale, from the server (2026-11-01)
    // how big bodies and plants are drawn: true size x the view's magnification (js/scale.js)
    this.scale = new Scale(meta.explore && meta.explore.metresPerUnit);
    this.scale.fromServer(meta.units);
    this.lenM = new Map();            // serial -> true body length (m), from status.bodyLenM
    this.canvas = canvas;
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, matchMedia('(max-width: 900px)').matches ? 1.25 : 2));
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    this.scene = new THREE.Scene();
    this.scene.background = new THREE.Color(0x0b1622);
    this.scene.fog = new THREE.Fog(0x0b1622, 250, 900);
    this.camera = new THREE.PerspectiveCamera(55, 1, 0.3, Math.max(2000, this.W * 3));
    // the wrap display (wrapPatch): view centre, half a map, strength (0 close up .. 1 zoomed out), seam width, background
    this.wrapU = { uWrapC: { value: new THREE.Vector2() }, uWrapHalf: { value: this.W / 2 }, uWrapS: { value: 0 }, uWrapLine: { value: 1 },
                   uWrapBg: { value: new THREE.Color(0x0b1622) } };
    this._wrapS = 0;
    this.camera.position.set(this.W / 2, this.W, this.W / 2 + this.W * 0.5);
    this.controls = new OrbitControls(this.camera, canvas);
    this.controls.target.set(this.W / 2, 0, this.W / 2);
    this.controls.enableDamping = true;
    this.controls.maxPolarAngle = Math.PI * 0.47;
    // the overview: from the whole map (fitWorld, the Home key) down to a few metres over a magnified animal, the wheel
    // zooming toward the point under the cursor (so a few turns of it reach any herd) and double-click diving in
    // (since 2026-11-02 from about a metre: animals are drawn at true size by default)
    this.controls.minDistance = MIN_DIST_M * this.scale.upm; this.controls.maxDistance = this.W * 1.6;
    this.controls.addEventListener('start', () => { this._userMoved = true; });
    this._overviewKeys();
    this.controls.zoomToCursor = true; this.controls.zoomSpeed = 3;   // x0.86 a notch: the whole map to a few metres in ~60
    this._fly = null;
    canvas.addEventListener('dblclick', (e) => { if (this.mode === 'overview') { this._userMoved = true; this.diveAt(e.clientX, e.clientY); } });
    // the wheel zooms about the ground under the cursor (OrbitControls' zoomToCursor uses a flat plane at the view
    // centre's depth: over the relief the point under the cursor drifted ~650 m in 30 notches). Caught on the parent
    // ahead of OrbitControls; a pinch on a touch screen still zooms through OrbitControls.
    if (canvas.parentElement) canvas.parentElement.addEventListener('wheel', (e) => this._wheel(e), { capture: true, passive: false });

    // [look] sky, aerial perspective, sun and sky light, shadows, plants and the finish: js/look/
    this.look = new Look(this, (m) => wrapPatch(m, this.wrapU));
    this.sun = this.look.light.sun;
    this.flames = this.look.veg.flames;

    this.mode = 'overview';
    this.agents = new Map();          // serial -> state for interpolation
    this.frameT = 0; this.frameDt = 50;
    this.hidden = new Set();          // clades hidden by the legend
    this.selected = 0;
    this.perceived = [];
    this.tmp = {};
    this._buildBodies();
    this._buildMarkers();
    this.resize();
    addEventListener('resize', () => this.resize());
    if (/[?&]lookdebug\b/.test(location.search)) window.__nrWorld = this;   // [look] scripted cameras for the art screenshots
  }

  resize() {
    const r = this.canvas.getBoundingClientRect();
    this.renderer.setSize(r.width, r.height, false);
    this.camera.aspect = r.width / Math.max(1, r.height);
    this.camera.updateProjectionMatrix();
  }

  // ------------------------------------------------------------------ terrain and water
  //
  // Everything here draws what the WORLD and FIELDS frames hold, nothing else (2026-10-31, 2026-11-01):
  // - ground (terrain/ground.js): one height per cell at its elevation, RELIEF units for the sim's whole height range above
  //   the lakes, drawn with a continuous level of detail (fine near the camera, coarse far away, one draw call), crags
  //   on bare rock close up (bounded), and materials from the data (rock by kind, scree, snow, alluvium, beaches);
  // - standing water (the sea and every lake, each at its own level: the WORLD frame's surface plane, w.surf): a flat
  //   surface over the quads that touch a cell of it, at that water's level, so the shore is where the ground crosses
  //   the level and nowhere else (a server without the plane: every water cell below w.sea, at the sea level); it
  //   fades out over its last half unit of depth, with a line of foam (terrain/water.js);
  // - rivers (terrain/rivers.js): smooth curves through the river plane's cells (or the hydrology's own geometry when the
  //   frame carries it), cut into the ground as channels as deep as their depth, as wide as their flow, foaming in steep
  //   reaches;
  // - caves, sinkholes and springs (terrain/caves.js) from the cave-features plane, when the frame carries it;
  // - ponds (FIELDS 'pond', Wet = 2) and flooded floodplains (FIELDS 'flood', Wet = 3): shallow water painted on the ground.
  // The map is a torus: the ground is drawn over a 3x3 tiling so the view never runs out of land, but in the overview one
  // map's worth around the view centre is drawn in full, a thin line marks where the map wraps, the copies beyond it
  // are dimmed, and the tiling's outer edge fades into the background (no edge, skirt or underside ever shows).

  setWorld(w) {
    this._dropTerrain();
    this.world = w;
    const fw = w.fw, S = this.W / fw, n = fw * fw, sea = w.sea, U = RELIEF / (1 - sea);
    const riv = this.riv = w.river || deriveRivers(w);
    const H = this.cellH = new Float32Array(n);                // ground height at each cell's vertex
    const SF = this.surfH = new Float32Array(n).fill(NaN);     // water surface there (NaN: dry ground)
    const lake = this.isLake = new Uint8Array(n);
    // a river spread over a broad sheet of cells (4 or more of its 8 neighbours are water): drawn as standing water, a
    // flat sheet over a shallow bed, not as a tangle of ribbons
    const broad = new Uint8Array(n);
    for (let i = 0; i < n; i++) {
      if (!riv[i]) continue;
      const x = i % fw, y = (i / fw) | 0; let c = 0;
      for (let k = 0; k < 8; k++) if (w.cell[((y + DIRY[k] + fw) % fw) * fw + ((x + DIRX[k] + fw) % fw)] !== 1) c++;
      broad[i] = c >= 4 && !w.riverPaths ? 1 : 0;   // (the hydrology's lines draw wide rivers at their own width)
    }
    for (let i = 0; i < n; i++) {
      const h = (elevOf(w, i) - sea) * U;
      if (w.cell[i] === 1) H[i] = Math.max(0, h) + LAND_LIFT;   // land is never below the sea level (elevation is sent in 1/255 steps)
      else if (riv[i]) {                                          // a river: its surface at the cell's level (its channel is carved below)
        const top = Math.max(0, h) + LAND_LIFT;
        H[i] = broad[i] ? top - 0.35 : top; SF[i] = top + 0.02;
      } else {                                                    // the sea or a lake: its bed, under its own level
        let s = w.surf && w.surf[i] ? (w.surf[i] / 65535 - sea) * U : 0;
        if (Math.abs(s) < 0.01) s = 0;                            // the sea (its level sent in 1/65535 steps): exactly 0 as ever
        H[i] = Math.min(h, s - 0.05); SF[i] = s; lake[i] = 1;
      }
    }
    const at = (x, y) => (((y % fw) + fw) % fw) * fw + (((x % fw) + fw) % fw);
    // land beside standing water is never below its level (its 1/255 elevation steps could put a shore under an upland lake)
    if (w.surf) for (let i = 0; i < n; i++) {
      if (w.cell[i] !== 1) continue;
      const x = i % fw, y = (i / fw) | 0;
      for (let k = 0; k < 8; k++) {
        const j = at(x + DIRX[k], y + DIRY[k]);
        if (lake[j] && H[i] < SF[j] + LAND_LIFT) H[i] = SF[j] + LAND_LIFT;
      }
    }
    // each quad is split along the diagonal its water follows (a river running diagonally stays in one channel)
    const D = this.diag = new Uint8Array(n);
    const wet = (i) => w.cell[i] !== 1;
    for (let y = 0; y < fw; y++) for (let x = 0; x < fw; x++) {
      const a = at(x, y), b = at(x + 1, y), c = at(x, y + 1), d = at(x + 1, y + 1);
      const ra = riv[a] & 7, rb = riv[b] & 7, rc = riv[c] & 7, rd = riv[d] & 7;
      if ((riv[a] && ra === 1) || (riv[d] && rd === 5)) D[a] = 1;
      else if ((riv[b] && rb === 3) || (riv[c] && rc === 7)) D[a] = 0;
      else D[a] = wet(a) && wet(d) && !(wet(b) && wet(c)) ? 1 : 0;
    }
    // per cell: the smooth normal (shading), rockiness (steep, high or ridge ground: crags and bare rock), a halo of it
    // (scree), and the level of standing water beside it (beaches)
    const nx = new Float32Array(n), nz = new Float32Array(n), rock = new Float32Array(n), halo = new Float32Array(n), shore = new Float32Array(n);
    const nearWater = new Uint8Array(n);
    for (let i = 0; i < n; i++) if (w.cell[i] !== 1) { const x = i % fw, y = (i / fw) | 0; for (let k = 0; k < 8; k++) nearWater[at(x + DIRX[k], y + DIRY[k])] = 1; nearWater[i] = 1; }
    for (let y = 0; y < fw; y++) for (let x = 0; x < fw; x++) {
      const i = y * fw + x, h = H[i];
      const hx = (H[at(x + 1, y)] - H[at(x - 1, y)]) / (2 * S), hz = (H[at(x, y + 1)] - H[at(x, y - 1)]) / (2 * S);
      const l = Math.hypot(hx, 1, hz); nx[i] = -hx / l; nz[i] = -hz / l;
      const slope = Math.hypot(hx, hz), ea = h / RELIEF;
      const lap = (H[at(x + 1, y)] + H[at(x - 1, y)] + H[at(x, y + 1)] + H[at(x, y - 1)] - 4 * h) / (S * S);
      const r = Math.max(smooth(0.6, 1.0, slope), smooth(0.7, 0.95, ea) * smooth(0.1, 0.3, slope), smooth(0.04, 0.12, -lap) * smooth(0.2, 0.45, slope));
      rock[i] = nearWater[i] ? 0 : r;
      let s = -1e4;
      for (let k = 0; k < 8; k++) { const j = at(x + DIRX[k], y + DIRY[k]); if (lake[j] && SF[j] > s) s = SF[j]; }
      if (lake[i]) s = Math.max(s, SF[i]);
      shore[i] = s;
    }
    for (let y = 0; y < fw; y++) for (let x = 0; x < fw; x++) {
      let m = 0;
      for (let dy = -2; dy <= 2; dy++) for (let dx = -2; dx <= 2; dx++) m = Math.max(m, rock[at(x + dx, y + dy)] * (1 - 0.15 * Math.max(Math.abs(dx), Math.abs(dy))));
      halo[y * fw + x] = m;
    }
    // channels, sinkholes, cave notches: a cap on the ground's height, CR texels per cell
    const CR = fw <= 256 ? 4 : fw <= 512 ? 2 : 1, K = fw * CR;
    const carve = new Float32Array(K * K).fill(CARVE_NONE), chan = new Uint8Array(K * K * 4), feat = new Uint8Array(K * K * 4);
    const groundRaw = (x, z) => this._ground(x, z);
    const ctx = {
      S, fw, U, sea, SF, H, broad, isLake: lake, lift: LAND_LIFT, ground: groundRaw, cellOf: (x, z) => this._cellOf(x, z),
      widthOfClass: (c) => riverWidth(c, RIVER_W0, RIVER_WMAX), widthOf: (i) => riverWidth(riv[i] >> 3, RIVER_W0, RIVER_WMAX),
      depthOf: (i) => Math.min(1.2, Math.max(0.15, depthM(w, i) * RELIEF / 8000)), flowOf: (i) => Math.min(1, (riv[i] >> 3) / 31),
    };
    const paths = fromWire(w, ctx) || fromCells(w, riv, ctx);
    const feats = decodeFeatures(w, S, groundRaw);
    const karst = stampFeatures(feats, {
      carve, feat, fw, CR, S, groundRaw, cellOf: ctx.cellOf,
      water: (x, z) => !!lake[this._cellOf(x, z)], isRiver: (x, z) => !!riv[this._cellOf(x, z)],
    });
    for (const p of karst.streams) paths.push(preparePath(p));
    stampRivers(paths, carve, chan, fw, CR, S);
    this.karst = { features: karst.placed.length, streams: karst.streams.length, rivers: paths.length };
    this.ground = new Ground({ W: this.W, fw, S, H, diag: D, shore, rock, halo, nx, nz, CR, carve, chan, feat }, (m, key) => wrapPatch(m, this.wrapU, key));
    this.scene.add(this.ground.mesh);
    // standing water: the quads touching a cell of the sea, a lake or a broad river sheet, flat at its level (the highest
    // of the quad's water cells, where two waters meet); colour (by depth below that level) per corner cell
    const lp = [], lv = [], ll = [];
    for (let y = 0; y < fw; y++) for (let x = 0; x < fw; x++) {
      const cs = [at(x, y), at(x + 1, y), at(x, y + 1), at(x + 1, y + 1)];
      let L = -Infinity;
      for (const c of cs) if ((lake[c] || broad[c]) && SF[c] > L) L = SF[c];
      if (L === -Infinity) continue;
      // a lake's corners all stand at its level; a broad river's at each cell's own (its sheet slopes as the river falls)
      const Y = cs.map((c) => broad[c] && !lake[c] ? SF[c] : L);
      lp.push(x * S, Y[0], y * S, (x + 1) * S, Y[1], y * S, x * S, Y[2], (y + 1) * S, (x + 1) * S, Y[3], (y + 1) * S); lv.push(...cs); ll.push(...Y);
    }
    const lg = quadGeometry(lp, 4);
    this.lakeVC = Int32Array.from(lv); this.lakeVL = Float32Array.from(ll); this.lakeColor = lg.attributes.color.array; this.lakeGeo = lg;
    // rivers: their ribbons in chunks of the map, drawn within RIBBON_FAR of the camera (beyond it the ground paints them)
    const rg = ribbonGeometry(paths);
    this.rivVC = rg.userData.cells; this.rivColor = rg.attributes.color.array;
    { const A = rg.attributes.aRiv.array; this.rivSteep = new Float32Array(A.length / 3); for (let v = 0; v < this.rivSteep.length; v++) this.rivSteep[v] = A[v * 3 + 2]; }
    this.rivChunks = splitChunks(rg, this.W, CHUNKS); rg.dispose();
    const WU = this.waterU = { ...this.ground.uniforms, uTime: { value: 0 } };
    // the lake surface loses depth ties to the ground (polygon offset away from the camera): low shores just above the
    // lake level never flicker through it
    const lm = new THREE.MeshPhongMaterial({ vertexColors: true, transparent: true, shininess: 90, specular: 0x6688aa, depthWrite: false,
                                             polygonOffset: true, polygonOffsetFactor: 2, polygonOffsetUnits: 4 });
    patchLakeMaterial(lm, WU); wrapPatch(lm, this.wrapU, 'nr-lake-1');
    // (it writes depth: where a wide ribbon folds over itself at a tight bend, the second layer fails the depth test
    // instead of blending twice)
    const rm = new THREE.MeshPhongMaterial({ vertexColors: true, transparent: true, depthWrite: true, shininess: 70, specular: 0x557799,
                                             polygonOffset: true, polygonOffsetFactor: -1, polygonOffsetUnits: -2 });
    patchRiverMaterial(rm, WU); wrapPatch(rm, this.wrapU, 'nr-river-1');
    // the karst's rocks and spring pools, in chunks drawn within KARST_FAR
    const fm = featureMeshes(karst.placed);
    const km = new THREE.MeshLambertMaterial({ vertexColors: true, flatShading: true });
    wrapPatch(km, this.wrapU, 'nr-karst-rock');
    const pm = new THREE.MeshPhongMaterial({ vertexColors: true, shininess: 90, specular: 0x6688aa, polygonOffset: true, polygonOffsetFactor: -1, polygonOffsetUnits: -2 });
    wrapPatch(pm, this.wrapU, 'nr-karst-pool');
    const near = [[this.rivChunks, rm, RIBBON_FAR, 1]];
    if (fm.rocks) { near.push([splitChunks(fm.rocks, this.W, CHUNKS), km, KARST_FAR, 0]); fm.rocks.dispose(); }
    if (fm.pools) { near.push([splitChunks(fm.pools, this.W, CHUNKS), pm, KARST_FAR, 0]); fm.pools.dispose(); }
    // per map copy: the standing water (whole), and the near-only chunks (each with its square and reach)
    this.tiles = []; this.nearTiles = [];
    for (let k = 0; k < 9; k++) {
      const wt = new THREE.Mesh(lg, lm), set = [wt], nearSet = [];
      wt.renderOrder = 2;
      for (const [chunks, mat, far, order] of near) for (const ch of chunks) {
        const m = new THREE.Mesh(ch.geo, mat); m.renderOrder = order;
        set.push(m); nearSet.push([m, ch, far]);
      }
      this.scene.add(...set);
      this.tiles.push(set); this.nearTiles.push(nearSet);
    }
    // [look] shadows: the ground casts and receives them (through its own depth material: its vertices are placed in its
    // shader), the karst rocks cast them, the water only receives them
    this.look.onTerrain([[this.ground.mesh]]);
    this.ground.mesh.customDepthMaterial = this.ground.depthMaterial;
    for (const set of this.tiles) for (const m of set) { m.receiveShadow = this.ground.mesh.receiveShadow; m.castShadow = m.receiveShadow && m.material === km; }
    this._paint(this.fields || null);
    if (this.fields) this._plants(true);
    if (!this._fitted) { this._fitted = true; this.fitWorld(true); this._openOnLife(); }   // the map, then low over life
  }

  // ------------------------------------------------------------------ the overview camera

  /** The distance from which the whole map fits the view, looking down at the overview's usual tilt. */
  _fitDist() {
    const cam = this.camera, tV = Math.tan(cam.fov * Math.PI / 360), tH = tV * cam.aspect;
    // the map seen at a tilt of FIT_TILT: its near half looks bigger than its far half, so the vertical fit is kept tighter
    return 0.5 * this.W * 1.06 / Math.min(tH, tV * 0.86);
  }

  /** Show the whole map: centred, from above at a gentle tilt (the start view, the Home key and the button). */
  fitWorld(now) {
    if (this.mode !== 'overview') return;
    const W = this.W, d = this._fitDist(), tilt = 0.42;
    const g = this.world ? this._meanLand() : 0;
    const c1 = new THREE.Vector3(W / 2, g, W / 2), p1 = new THREE.Vector3(W / 2, g + d * Math.cos(tilt), W / 2 + d * Math.sin(tilt));
    this.controls.maxDistance = d * 1.3;
    this._flyTo(c1, p1, now ? 0 : 900);
  }

  /** Fly the overview camera to look at c from p over dur ms (0: at once). */
  _flyTo(c, p, dur) {
    if (!dur) { this._fly = null; this.controls.target.copy(c); this.camera.position.copy(p); this.controls.update(); return; }
    this._fly = { t0: performance.now(), dur, c0: this.controls.target.clone(), p0: this.camera.position.clone(), c1: c.clone(), p1: p.clone() };
  }

  /** Double-click: dive toward the ground under the pointer, to a quarter of the distance (not below a few animals' lengths). */
  diveAt(clientX, clientY) {
    const hit = this.groundAt(clientX, clientY, true); if (!hit) return;
    const c = this.controls.target, cam = this.camera.position, W = this.W;
    const x = hit[0] + W * Math.round((c.x - hit[0]) / W), z = hit[1] + W * Math.round((c.z - hit[1]) / W);
    const off = cam.clone().sub(c), d = off.length(), nd = Math.max(this.controls.minDistance * 4, d * 0.25);
    off.multiplyScalar(nd / d);
    const g = this.heightAt(x, z), c1 = new THREE.Vector3(x, g, z);
    this._flyTo(c1, c1.clone().add(off), 700);
  }

  /** A wheel notch in the overview: camera and view centre scaled about the ground point under the cursor, which so
   *  stays exactly under it (x0.83 a notch in, as OrbitControls' zoomSpeed 3), within the controls' distance limits. */
  _wheel(e) {
    if (this.mode !== 'overview' || e.target !== this.canvas || !this.world || !this.controls.enabled) return;
    e.preventDefault(); e.stopImmediatePropagation(); this._userMoved = true;
    const dy = e.deltaY * (e.deltaMode === 1 ? 40 : e.deltaMode === 2 ? 800 : 1);
    if (!dy) return;
    this._fly = null;
    const c = this.controls.target, cam = this.camera.position, d = cam.distanceTo(c);
    const s = Math.pow(0.95, this.controls.zoomSpeed * Math.min(3, Math.abs(dy) * 0.01));
    const nd = Math.max(this.controls.minDistance, Math.min(this.controls.maxDistance, dy < 0 ? d * s : d / s)), k = nd / Math.max(1e-9, d);
    const hit = this.groundAt(e.clientX, e.clientY, true);
    const P = hit ? new THREE.Vector3(hit[0], this.heightAt(hit[0], hit[1]), hit[1]) : c.clone();
    cam.sub(P).multiplyScalar(k).add(P); c.sub(P).multiplyScalar(k).add(P);
  }

  /** The least camera height at (cam.x, cam.z) from which the line to the view centre c clears the ground. */
  _sightY(c, cam) {
    const hd = Math.hypot(cam.x - c.x, cam.z - c.z), m = 0.03 * Math.max(hd, Math.abs(cam.y - c.y));
    let y = -Infinity;
    for (let i = 1; i <= 10; i++) {
      const f = i / 10, g = this.heightAt(c.x + (cam.x - c.x) * f, c.z + (cam.z - c.z) * f);
      y = Math.max(y, c.y + (g + m * f - c.y) / f);
    }
    return y;
  }

  /** The mean height of the land (where the whole map's view looks). */
  _meanLand() {
    if (this._meanH !== undefined) return this._meanH;
    let s = 0, n = 0; const H = this.cellH, w = this.world;
    for (let i = 0; i < H.length; i += 7) if (w.cell[i] === 1) { s += H[i]; n++; }
    return (this._meanH = n ? s / n : 0);
  }

  /** Change the creatures' magnification (the overview's "creature size x" control). */
  setMagnification(m) { this.scale.setMag(m); this._plants(true); if (this.onScale) this.onScale(); }

  _dropTerrain() {
    this._meanH = undefined;
    if (!this.tiles) return;
    const seen = new Set();
    for (const set of this.tiles) for (const m of set) { this.scene.remove(m); seen.add(m.geometry); seen.add(m.material); }
    for (const o of seen) o.dispose();
    this.tiles = null;
    if (this.ground) { this.scene.remove(this.ground.mesh); this.ground.dispose(); this.ground = null; }
  }
  /** Height of the sim's own surface (its triangles, before channels and crags) at a world position. */
  _ground(x, y) {
    const fw = this.world.fw, S = this.W / fw;
    const gx = ((x / S) % fw + fw) % fw, gy = ((y / S) % fw + fw) % fw;
    const x0 = Math.floor(gx) % fw, y0 = Math.floor(gy) % fw, tx = gx - Math.floor(gx), ty = gy - Math.floor(gy);
    const x1 = (x0 + 1) % fw, y1 = (y0 + 1) % fw;
    const H = this.cellH;
    const a = H[y0 * fw + x0], b = H[y0 * fw + x1], c = H[y1 * fw + x0], d = H[y1 * fw + x1];
    if (this.diag[y0 * fw + x0]) return ty >= tx ? a + (d - c) * tx + (c - a) * ty : a + (b - a) * tx + (d - b) * ty;
    return tx + ty <= 1 ? a + (b - a) * tx + (c - a) * ty : d + (c - d) * (1 - tx) + (b - d) * (1 - ty);
  }

  /** Ground or water surface height at a world position (on the drawn ground, its channels and crags; at the water
   *  surface where there is water). */
  heightAt(x, y, swim) {
    const w = this.world; if (!w) return 0;
    const h = this.ground ? this.ground.heightAt(x, y, this.camera.position) : this._ground(x, y), sf = this.surfH[this._cellOf(x, y)];
    if (sf === sf) return swim ? Math.max(h, sf - 0.25) : Math.max(h, sf - 0.6);   // sf === sf: not NaN, a water cell
    return h;
  }

  setFields(f) { this.fields = f; this._paint(f); this._plants(true); }

  _layer(name) { return this.meta.fieldLayers.indexOf(name); }

  /** The FIELDS frame onto the ground's textures (cover colour and plant density; snow, rock kinds, alluvium), the
   *  lakes' and the rivers' colours. Rock, scree, snow and the rest are laid over the cover in the ground's shader. */
  _paint(f) {
    const w = this.world; if (!w || !this.tiles || !this.ground) return;
    const fw = w.fw, C = this.ground.cover, M = this.ground.mat, M2 = this.ground.mat2;
    const d = f ? f.d : 0, L = f ? (n) => { const k = this._layer(n); return k >= 0 ? f.data.subarray(k * d * d, (k + 1) * d * d) : null; } : () => null;
    const grass = L('grass'), shrub = L('shrub'), tree = L('tree'), fire = L('fire'), moist = L('moisture'),
          carrion = L('carrion'), algae = L('algae'), phyto = L('phyto'), nectar = L('nectar'), pond = L('pond'),
          frozen = L('frozen'), temp = L('temp'), flood = L('flood'),
          snow = L('snow'), volc = L('volcanic'), sedi = L('sedimentary'), lime = L('limestone'), allu = L('alluvium'),
          wetland = L('wetland');   // marsh and swamp share (hydrology, when sent; none: no wetlands drawn)
    // the sim's snowpack when it sends one (the water cycle on); else snow where the air is below freezing
    let hasSnow = false; if (snow) for (let k = 0; k < snow.length; k++) if (snow[k]) { hasSnow = true; break; }
    const sea = w.sea, b8 = (v) => Math.max(0, Math.min(255, Math.round(v * 255)));
    for (let i = 0; i < fw * fw; i++) {
      const cx = i % fw, cy = (i / fw) | 0;
      const fi = f ? Math.min(d - 1, cy >> 1) * d + Math.min(d - 1, cx >> 1) : 0;
      const e = elevOf(w, i), ea = Math.max(0, (e - sea) / (1 - sea));   // height above the lakes, 0..1
      let r, g, b, plants = 0;
      if (!f) { r = 0.35 + ea * 0.2; g = 0.33 + ea * 0.15; b = 0.25; }
      else {
        const m = moist[fi] / 255, G = grass[fi] / 255, S = shrub[fi] / 255, T = tree[fi] / 255;
        plants = Math.min(1, 0.6 * G + 0.8 * S + T);
        // bare soil: dry tan to moist dark earth
        r = 0.50 - 0.20 * m; g = 0.42 - 0.17 * m; b = 0.30 - 0.12 * m;
        const dryG = 1 - m;
        const gr = 0.30 + 0.30 * dryG, gg = 0.58 - 0.12 * dryG, gb = 0.18;      // green grass, yellowing when dry
        r += (gr - r) * G; g += (gg - g) * G; b += (gb - b) * G;
        r += (0.30 - r) * S; g += (0.40 - g) * S; b += (0.16 - b) * S;
        r += (0.17 - r) * T * 0.85; g += (0.26 - g) * T * 0.85; b += (0.13 - b) * T * 0.85;   // forest floor and canopy (sRGB, as all the cover colours)
        const nc = nectar[fi] / 255; r += nc * 0.25; b += nc * 0.18;          // flowers
        const ca = carrion[fi] / 255; r += (0.35 - r) * ca * 0.5; g *= 1 - ca * 0.4; b *= 1 - ca * 0.4;
        // a seasonal pond holding water (Wet = 2; the layer is the share of the field cell's 4 cells): open water
        if (pond && pond[fi] && w.cell[i] === 1) { const p = Math.min(1, 0.35 + pond[fi] / 255); r += (0.12 - r) * p; g += (0.30 - g) * p; b += (0.44 - b) * p; plants *= 1 - p; }
        // a flooded floodplain (Wet = 3; the share of the field cell's 4 cells): shallow, silty water over the ground
        if (flood && flood[fi] && w.cell[i] === 1) { const p = Math.min(0.9, 0.4 + 0.5 * flood[fi] / 255); r += (0.20 - r) * p; g += (0.36 - g) * p; b += (0.42 - b) * p; plants *= 1 - p; }
        if (frozen && frozen[fi]) { const fr = 0.3 * frozen[fi] / 255; r += (0.92 - r) * fr; g += (0.94 - g) * fr; b += (0.97 - b) * fr; }   // hoar frost (Revision O); snow is the shader's
        if (fire[fi]) { r = 1.0; g = 0.42; b = 0.08; plants = 1; }
        if (this.tempOverlay && temp) {
          // air temperature: -40 °C deep blue, 5 °C white, 50 °C red
          const t = temp[fi] / 255, u = Math.min(1, Math.abs(t - 0.5) * 2);
          const tr = t < 0.5 ? 1 - 0.85 * u : 1, tg = 1 - 0.8 * u, tb = t < 0.5 ? 1 : 1 - 0.85 * u;
          r += (tr - r) * 0.7; g += (tg - g) * 0.7; b += (tb - b) * 0.7;
        }
      }
      C[i * 4] = b8(r); C[i * 4 + 1] = b8(g); C[i * 4 + 2] = b8(b); C[i * 4 + 3] = b8(plants);
      let sn = 0;
      if (f && w.cell[i] === 1) {
        if (hasSnow) sn = snow[fi] / 255;
        else if (temp) sn = Math.min(1, Math.max(0, (-(temp[fi] / 255 * 90 - 40) - 0.5) / 5));
      }
      M[i * 4] = b8(sn); M[i * 4 + 1] = volc ? volc[fi] : 0; M[i * 4 + 2] = sedi ? sedi[fi] : 0; M[i * 4 + 3] = allu ? allu[fi] : 0;
      M2[i * 4] = lime ? lime[fi] : 0; M2[i * 4 + 1] = wetland && !this.isLake[i] ? wetland[fi] : 0;
    }
    // the FIELDS grid is half the cell grid: a [1 2 1] tent across the cells (on the torus) turns its 2x2 blocks into a
    // smooth cover (2026-11-01 QA: once the colours had their contrast, the overview was a checkerboard of them)
    {
      const n4 = fw * fw * 4, tmp = this._coverTmp && this._coverTmp.length === n4 ? this._coverTmp : (this._coverTmp = new Uint8Array(n4));
      for (let pass = 0; pass < 2; pass++) {
        const src = pass ? tmp : C, dst = pass ? C : tmp;
        for (let y = 0; y < fw; y++) for (let x = 0; x < fw; x++) {
          const i = (y * fw + x) * 4;
          const a = pass ? (((y + fw - 1) % fw) * fw + x) * 4 : (y * fw + (x + fw - 1) % fw) * 4;
          const b = pass ? (((y + 1) % fw) * fw + x) * 4 : (y * fw + (x + 1) % fw) * 4;
          for (let c = 0; c < 4; c++) dst[i + c] = (src[a + c] + 2 * src[i + c] + src[b + c] + 2) >> 2;
        }
      }
    }
    this.ground.coverUpdated(); this.ground.matUpdated();
    this.ground.uniforms.uPlain.value = this.tempOverlay ? 1 : 0;
    const fieldOf = (i) => f ? Math.min(d - 1, ((i / fw) | 0) >> 1) * d + Math.min(d - 1, (i % fw) >> 1) : 0;
    // ice (2026-11-01 QA): FIELDS 'frozen' is only the air below 0 °C now; water freezes over after a cold spell, so ice
    // grows with the frost (the air temperature, 'temp'): a lake from about -1 °C, the sea (salt, mixed by waves) from
    // about -4 °C, a slow river from about -3 °C, and a fast one (steep, turbulent: frazil and anchor ice, rarely a
    // cover) not at all. Young ice is dark (black ice: the water shows through); it is white only under snow, which lies
    // on it where the snowpack lies on the shore beside it.
    const airC = (fi) => temp ? temp[fi] / 255 * 90 - 40 : frozen && frozen[fi] ? -8 : 5;
    const shoreSnow = (fi) => {
      if (!hasSnow) return 0;
      const fx = fi % d, fy = (fi / d) | 0; let m = 0;
      for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) m = Math.max(m, snow[((fy + dy + d) % d) * d + ((fx + dx + d) % d)]);
      return m / 255;
    };
    const ICE = [0.17, 0.23, 0.27], ICE_SNOW = [0.80, 0.85, 0.90];
    const iceRGB = (out, o, k, sn) => { for (let c = 0; c < 3; c++) out[o + c] += (ICE[c] + (ICE_SNOW[c] - ICE[c]) * sn - out[o + c]) * k; };
    const ramp = (x) => { const t = Math.min(1, Math.max(0, x)); return t * t * (3 - 2 * t); };
    // lakes: shallow water turquoise, deep water dark blue (depth below the water's level at each corner); algae and plankton tint it green
    const LC = this.lakeColor, LV = this.lakeVC, LL = this.lakeVL;
    for (let v = 0; v < LV.length; v++) {
      // 2026-11-01: depths are metres now (a littoral of 0-5 m, lakes 10-40 m, the sea to ~200 m): dark from 30 m
      // (RELIEF units are 8,000 m, whatever the cell size)
      const i = LV[v], fi = fieldOf(i), dd = Math.min(1, Math.max(0, LL[v] - this.cellH[i]) / (30 * RELIEF / 8000));
      const al = f ? algae[fi] / 255 : 0, ph = f ? phyto[fi] / 255 : 0;
      // (linear light: water seen from above is dark, its bed showing through where shallow; 2026-11-01 QA, it was a bright cyan)
      LC[v * 4] = 0.050 - 0.036 * dd + 0.03 * al; LC[v * 4 + 1] = 0.20 - 0.14 * dd + 0.09 * al + 0.05 * ph; LC[v * 4 + 2] = 0.25 - 0.14 * dd + 0.01 * ph;
      LC[v * 4 + 3] = 0.62 + 0.33 * dd;
      if (f && this.isLake[i] && (temp || frozen)) {
        const T = airC(fi), isSea = Math.abs(this.surfH[i]) < 0.01, ice = ramp((isSea ? -T - 4 : -T - 1) / 6);
        if (ice > 0) { iceRGB(LC, v * 4, ice, shoreSnow(fi) * ice); LC[v * 4 + 3] += (0.96 - LC[v * 4 + 3]) * ice; }
      }
    }
    // rivers: clear and light where small or shallow, deep green-blue where big and deep; algae tint them green
    const RC = this.rivColor, RV = this.rivVC, RS = this.rivSteep, U = RELIEF / (1 - sea);
    for (let v = 0; v < RV.length; v++) {
      const i = RV[v], fi = fieldOf(i), al = f ? algae[fi] / 255 : 0;
      const dd = Math.min(1, Math.max(depthM(w, i) / 5, (this.riv[i] >> 3) / 24));   // 5 m: a deep river
      RC[v * 3] = 0.08 - 0.05 * dd + 0.03 * al; RC[v * 3 + 1] = 0.22 - 0.12 * dd + 0.07 * al; RC[v * 3 + 2] = 0.25 - 0.11 * dd;
      if (f && (temp || frozen)) {
        const ice = ramp((-airC(fi) - 3) / 7) * (1 - ramp((RS[v] - 0.12) / 0.3));
        if (ice > 0) iceRGB(RC, v * 3, ice, shoreSnow(fi) * ice);
      }
    }
    this.lakeGeo.attributes.color.needsUpdate = true;
    copyColors(this.rivChunks, RC, 3);
  }

  // ------------------------------------------------------------------ vegetation instances (LOD within a budget)
  //
  // The trees, shrubs, grass tufts, rocks and fires in view, from the FIELDS cover: js/look/vegetation.js.

  _plants(force) { this.look.plants(force); }   // [look] js/look/vegetation.js

  /** The cell whose vertex is nearest to a world position. */
  _cellOf(x, y) {
    const fw = this.world.fw, S = this.W / fw;
    const cx = ((Math.round(x / S) % fw) + fw) % fw, cy = ((Math.round(y / S) % fw) + fw) % fw;
    return cy * fw + cx;
  }

  // ------------------------------------------------------------------ animals

  _buildBodies() {
    this.bodyMat = bodyMaterial();
    this.nearGeo = bodyGeometry('near'); this.farGeo = bodyGeometry('far');
    this.near = this._allocBodies(null, this.nearGeo, NEAR_CAP);
    this.far = this._allocBodies(null, this.farGeo, 4096);
    this.cladeRGB = new Float32Array(NCLADES * 3).fill(0.5);
    this.setClades(this.meta.species || []);
    this.eggs = new THREE.InstancedMesh(new THREE.SphereGeometry(0.35, 6, 4), new THREE.MeshLambertMaterial({ color: 0xffffff }), 4096);
    this.eggs.count = 0; this.eggs.frustumCulled = false;
    this.eggs.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(4096 * 3), 3);
    this.scene.add(this.eggs);
  }

  /** (Re)create an instanced body mesh with room for cap organisms. */
  _allocBodies(old, geo, cap) {
    if (old) { this.scene.remove(old.mesh); old.mesh.geometry.dispose(); old.mesh.dispose(); }
    const B = bodyMesh(geo, this.bodyMat, cap);
    this.look.onBodies(B.mesh, this.bodyMat, geo === this.nearGeo);   // [look] shadows of the near animals
    this.scene.add(B.mesh);
    return B;
  }

  /** The clade table (meta.species, then each 'clades' frame): per-clade colours, sight and field of view. */
  setClades(list) {
    if (!Array.isArray(list)) return;
    this.clades = list;
    const n = Math.max(NCLADES, list.length);
    if (this.cladeRGB.length < n * 3) this.cladeRGB = new Float32Array(n * 3).fill(0.5);
    const c = new THREE.Color();
    for (const cl of list) {
      if (!cl || cl.id === undefined || cl.id >= n) continue;
      c.set(cl.color || '#8aa0b5');
      this.cladeRGB[cl.id * 3] = c.r; this.cladeRGB[cl.id * 3 + 1] = c.g; this.cladeRGB[cl.id * 3 + 2] = c.b;
    }
  }

  setAgents(a) {
    const now = performance.now();
    // (up to 0.7 s: at a few steps a second the frames are that far apart, and the animals glide between them
    // instead of moving for a quarter second and standing still for the rest)
    // (2026-11-02) and over a whole slow step: at real time with a fine world step a step lasts a minute or more on screen,
    // so the glide may last as long as a step does (stepMs, from the speed set; never past 10 minutes)
    this.frameDt = Math.min(Math.max(700, Math.min(600000, 1.25 * (this.stepMs || 0))), Math.max(16, now - this.frameT)); this.frameT = now;
    const gen = (this._gen = (this._gen || 0) + 1), W = this.W, o = this.tmp;
    // the world's mass range changed (a world of giants after the prehistory, ICD §2.1): every body is decoded again
    const mv = massRangeVersion(), fresh = mv !== this._massVer; this._massVer = mv;
    for (let k = 0; k < a.n; k++) {
      agentState(a, k, W, o);
      let e = this.agents.get(o.serial);
      if (!e) { e = { x: o.x, y: o.y, h: o.h, px: o.x, py: o.y, ph: o.h, st: a.step, vm: 0 }; this.agents.set(o.serial, e); }
      else {
        // its speed, in metres per step (a frame may carry several steps): a flier faster than it walks is in the air
        const ds = a.step - e.st;
        if (ds > 0) { let dx = o.x - e.x, dy = o.y - e.y; dx -= Math.round(dx / W) * W; dy -= Math.round(dy / W) * W; e.vm = Math.hypot(dx, dy) * this.scale.mpu / ds; }
        e.st = a.step;
        e.px = e.x; e.py = e.y; e.ph = e.h; e.x = o.x; e.y = o.y; e.h = o.h;
      }
      e.sp = o.sp; e.stage = o.stage; e.acts = o.acts; e.scan = o.scan; e.rest = o.rest; e.energy = o.energy; e.gen = gen; e.carry = o.carrying;
      // the body its genes built is the same for its life: decoded the first time its serial is seen (and again only if
      // the first gene bytes differ, as when another world, whose serials start again at 1, is shown)
      const fp = a.dv.getUint32(k * (a.rec || REC) + 12, true);
      if (e.fp !== fp || fresh) {
        e.fp = fp;
        agentGenes(a, k, o);
        e.gMass = o.gMass; e.m01 = o.m01; e.mass = o.mass; e.size = bodySize(o.mass);
        e.legs = o.legs; e.fins = o.fins; e.wings = o.wings; e.loco = o.loco; e.legPairs = o.legPairs; e.finPairs = o.finPairs; e.eyePairs = o.eyePairs;
        e.fly = o.loco === 'Fly'; e.swim = o.loco === 'Swim';
        e.sight = o.sight; e.armour = o.armour; e.strike = o.strike; e.meat = o.meat;
        e.fur = o.fur; e.append = o.append; e.lungs = o.lungs;
        (e.appearance ||= new Float32Array(16)).set(o.appearance);
      }
      e.call = o.call; e.call2 = o.call2; e.call3 = o.call3; if (o.call) e.callT = now;
      // a swimmer's depth below the surface (m) and the share of the step it spends there (an air-breather surfaces the rest)
      e.depth = o.depth; e.atDepth = o.atDepth; e.breath = o.breath; e.serial = o.serial;
    }
    for (const [s, e] of this.agents) if (e.gen !== gen) this.agents.delete(s);
    this.count = a.n;
    this._openOnLife();
  }

  // ---- groups of animals, for the overview's opening view and its N / Tab keys (2026-11-02: the views were framed for a
  // map; at true size a 10 cm animal is a speck from a kilometre up, so the overview now opens low over the densest group)

  /** Groups of living, shown animals (of clade sp only, if given): bins of GROUP_M metres, the most numerous first, each
   *  {n, x, y (its middle, unwrapped about its first member), r (spread, m), sp (its most common clade)}. */
  groups(sp = -1) {
    const W = this.W, B = GROUP_M * this.scale.upm, nb = Math.max(1, Math.floor(W / B)), bins = new Map();
    for (const [serial, e] of this.agents) {
      if (e.stage === 0 || this.dead.has(serial) || this.hidden.has(e.sp) || (sp >= 0 && e.sp !== sp)) continue;
      const k = (Math.floor(e.y / B) % nb) * nb + (Math.floor(e.x / B) % nb);
      let b = bins.get(k); if (!b) bins.set(k, b = { n: 0, list: [] });
      b.n++; b.list.push(e);
    }
    const out = [];
    for (const b of bins.values()) {
      const e0 = b.list[0], cs = new Map(); let sx = 0, sy = 0;
      for (const e of b.list) { sx += e.x - W * Math.round((e.x - e0.x) / W); sy += e.y - W * Math.round((e.y - e0.y) / W); cs.set(e.sp, (cs.get(e.sp) || 0) + 1); }
      const x = sx / b.n, y = sy / b.n; let r2 = 0;
      for (const e of b.list) { const dx = e.x - W * Math.round((e.x - x) / W) - x, dy = e.y - W * Math.round((e.y - y) / W) - y; r2 += dx * dx + dy * dy; }
      out.push({ n: b.n, x: ((x % W) + W) % W, y: ((y % W) + W) % W, r: Math.sqrt(r2 / b.n) * this.scale.mpu, sp: [...cs].sort((p, q) => q[1] - p[1])[0][0] });
    }
    return out.sort((p, q) => q.n - p.n);
  }

  /** Frame a group from low and to one side: a view VIEW_M_MIN..VIEW_M_MAX metres across (wider for a spread-out group),
   *  ~20 degrees above the ground, from the side the camera is on now. dur: ms of flight (0: at once). */
  frameGroup(g, dur = 900) {
    if (!g || this.mode !== 'overview') return;
    const c = this.controls.target, W = this.W;
    const x = g.x + W * Math.round((c.x - g.x) / W), z = g.y + W * Math.round((c.z - g.y) / W);
    const acrossM = Math.min(VIEW_M_MAX, Math.max(VIEW_M_MIN, 2.5 * g.r + 15));
    const tH = Math.tan(this.camera.fov * Math.PI / 360) * Math.max(0.5, this.camera.aspect);
    const d = acrossM * this.scale.upm / (2 * tH), el = 0.36;
    const off = this.camera.position.clone().sub(c); off.y = 0;
    if (off.lengthSq() < 1e-12) off.set(0, 0, 1);
    off.normalize().multiplyScalar(d * Math.cos(el)); off.y = d * Math.sin(el);
    const gy = this.world ? this.heightAt(x, z) : 0, c1 = new THREE.Vector3(x, gy, z), p1 = c1.clone().add(off);
    if (this.world) p1.y = Math.max(p1.y, this._sightY(c1, p1));
    this.controls.maxDistance = Math.max(this.controls.maxDistance, d * 2);
    this._flyTo(c1, p1, dur);
  }

  /** The opening view: low over the densest group, once animals and the ground are there (unless the view was moved). */
  _openOnLife() {
    if (this._opened || !this.tiles || this.mode !== 'overview' || !this.agents.size) return;
    this._opened = true;
    if (this._userMoved) return;
    const g = this.groups()[0]; if (!g) return;
    this._groupI = 0;
    this.frameGroup(g, 0);
  }

  /** N (Shift+N back): the next group, the most numerous first. Tab (Shift+Tab back): the next clade (lineage), most
   *  numerous first, at its densest group. Says where in a note (onNote). */
  nextGroup(dir = 1) {
    const gs = this.groups(); if (!gs.length) { this.onNote?.('no animals to show'); return; }
    const n = Math.min(gs.length, 40);
    this._groupI = (((this._groupI ?? -1) + dir) % n + n) % n;
    const g = gs[this._groupI]; this.frameGroup(g);
    const cl = this.clades && this.clades[g.sp];
    this.onNote?.(`group ${this._groupI + 1} of ${n} · ${g.n} animal${g.n === 1 ? '' : 's'}${cl ? `, mostly <b style="color:${cl.color}">${cl.name}</b>` : ''}<br><span style="font-size:11px">N / Shift+N: next group · Tab: next clade · Home: whole map</span>`);
  }
  nextClade(dir = 1) {
    const cnt = new Map();
    for (const [serial, e] of this.agents) if (e.stage !== 0 && !this.dead.has(serial) && !this.hidden.has(e.sp)) cnt.set(e.sp, (cnt.get(e.sp) || 0) + 1);
    const order = [...cnt.keys()].sort((a, b) => cnt.get(b) - cnt.get(a) || a - b);
    if (!order.length) { this.onNote?.('no animals to show'); return; }
    const i = order.indexOf(this._cladeI);
    this._cladeI = order[((i < 0 ? (dir > 0 ? -1 : 0) : i) + dir + order.length) % order.length];
    const g = this.groups(this._cladeI)[0]; if (!g) return;
    this.frameGroup(g);
    const cl = this.clades && this.clades[this._cladeI];
    this.onNote?.(`clade ${order.indexOf(this._cladeI) + 1} of ${order.length}: <b style="color:${cl ? cl.color : '#aaa'}">${cl ? cl.name : 'animals'}</b> · ${cnt.get(this._cladeI)} seen, ${g.n} here<br><span style="font-size:11px">Tab / Shift+Tab: next clade · N: next group</span>`);
  }

  /** The overview's keys: N, Tab (above); W A S D or the arrows pan, Q / E (or - / =) zoom out / in, at a speed that
   *  follows the zoom (a share of the view's distance per second). */
  _overviewKeys() {
    const held = this._held = new Set();
    const skip = (e) => this.mode !== 'overview' || e.ctrlKey || e.metaKey || e.altKey || e.target.closest?.('input, textarea, select, button');
    addEventListener('keydown', (e) => {
      if (skip(e)) return;
      if (e.code === 'KeyN' && !e.repeat) { this._userMoved = true; this.nextGroup(e.shiftKey ? -1 : 1); return; }
      if (e.code === 'Tab') { e.preventDefault(); if (!e.repeat) { this._userMoved = true; this.nextClade(e.shiftKey ? -1 : 1); } return; }
      if (PAN_KEYS.includes(e.code)) { e.preventDefault(); held.add(e.code); this._userMoved = true; this._fly = null; }
    });
    addEventListener('keyup', (e) => held.delete(e.code));
    addEventListener('blur', () => held.clear());
  }

  /** Held pan and zoom keys, once a frame in the overview (dt in s). */
  _keyMove(dt) {
    const k = this._held; if (!k || !k.size) return;
    const c = this.controls.target, cam = this.camera.position, d = cam.distanceTo(c);
    const zin = (k.has('KeyE') || k.has('Equal') ? 1 : 0) - (k.has('KeyQ') || k.has('Minus') ? 1 : 0);
    if (zin) {
      const nd = Math.max(this.controls.minDistance, Math.min(this.controls.maxDistance, d * Math.exp(-1.6 * zin * dt)));
      cam.sub(c).multiplyScalar(nd / Math.max(1e-9, d)).add(c);
    }
    const fx = (k.has('KeyD') || k.has('ArrowRight') ? 1 : 0) - (k.has('KeyA') || k.has('ArrowLeft') ? 1 : 0);
    const fz = (k.has('KeyW') || k.has('ArrowUp') ? 1 : 0) - (k.has('KeyS') || k.has('ArrowDown') ? 1 : 0);
    if (fx || fz) {
      const f = c.clone().sub(cam); f.y = 0; if (f.lengthSq() < 1e-12) f.set(0, 0, -1); f.normalize();
      const v = d * 0.9 * dt, mx = (f.x * fz - f.z * fx) * v, mz = (f.z * fz + f.x * fx) * v;
      c.x += mx; c.z += mz; cam.x += mx; cam.z += mz;
    }
  }

  /** Interpolated, camera-relative position of an agent. */
  _pos(e, alpha, cx, cz, out = [0, 0, 0]) {
    const W = this.W;
    let dx = e.x - e.px, dy = e.y - e.py;
    if (dx > W / 2) dx -= W; else if (dx < -W / 2) dx += W;
    if (dy > W / 2) dy -= W; else if (dy < -W / 2) dy += W;
    let x = e.px + dx * alpha, y = e.py + dy * alpha;
    x += Math.round((cx - x) / W) * W; y += Math.round((cz - y) / W) * W;
    let dh = e.h - e.ph; if (dh > Math.PI) dh -= 2 * Math.PI; else if (dh < -Math.PI) dh += 2 * Math.PI;
    out[0] = x; out[1] = y; out[2] = e.ph + dh * alpha;
    return out;
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
    // fights: an expanding flash where a strike landed, and a blood stain where an animal was killed
    const fr = new THREE.RingGeometry(0.55, 1.0, 24); fr.rotateX(-Math.PI / 2);
    this.flashMesh = new THREE.InstancedMesh(fr, new THREE.MeshBasicMaterial({ transparent: true, opacity: 0.9, depthWrite: false }), FX_MAX);
    this.flashMesh.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(FX_MAX * 3), 3);
    this.flashMesh.count = 0; this.flashMesh.renderOrder = 7; this.flashMesh.frustumCulled = false;
    const st = new THREE.CircleGeometry(1, 14); st.rotateX(-Math.PI / 2);
    this.stainMesh = new THREE.InstancedMesh(st, new THREE.MeshBasicMaterial({ color: 0x7a0d0d, transparent: true, opacity: 0.75, depthWrite: false }), STAIN_MAX);
    this.stainMesh.count = 0; this.stainMesh.renderOrder = 2; this.stainMesh.frustumCulled = false;
    const drop = new THREE.IcosahedronGeometry(0.09, 0);
    this.bloodMesh = new THREE.InstancedMesh(drop, new THREE.MeshBasicMaterial({ color: 0xa10b0b }), BLOOD_MAX);
    this.bloodMesh.count = 0; this.bloodMesh.frustumCulled = false;
    this.scene.add(this.flashMesh, this.stainMesh, this.bloodMesh);
    // voices: a ring in the sound's colour around an animal while it calls, and the person's own call
    const cr = new THREE.RingGeometry(0.85, 1.0, 28); cr.rotateX(-Math.PI / 2);
    this.callMesh = new THREE.InstancedMesh(cr, new THREE.MeshBasicMaterial({ transparent: true, opacity: 0.8, depthWrite: false, depthTest: false }), CALL_MAX);
    this.callMesh.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(CALL_MAX * 3), 3);
    this.callMesh.count = 0; this.callMesh.renderOrder = 8; this.callMesh.frustumCulled = false;
    this.scene.add(this.callMesh);
    this.myCall = null;   // {x, y, sound, t0}
    this.fx = [];      // live flashes {x, y, t0, kind}
    this.stains = [];  // blood where animals died {x, y, t0, r}
    this.blood = [];   // droplets {x, y, z, vx, vy, vz, s}
    this.corpses = []; // the killed, lying where they fell {e, x, y, h, t0}
    this.dead = new Set();   // serials already drawn as corpses
    this.shake = 0; this._lastFx = performance.now();
  }

  /** Blood from a blow: k is the drawn scale (world units per view-size unit), so the spray is as big as the bodies. */
  _spray(x, y, z, n, speed, dx, dy, k) {
    for (let i = 0; i < n && this.blood.length < BLOOD_MAX; i++) {
      const a = Math.random() * Math.PI * 2, v = speed * (0.4 + Math.random()) * k;
      // most of it flies away from the blow
      this.blood.push({ x, y, z, vx: Math.cos(a) * v * 0.6 + dx * v, vy: Math.sin(a) * v * 0.6 + dy * v, vz: (2.5 + Math.random() * speed) * k, s: (0.6 + Math.random() * 1.2) * k, k });
    }
  }

  /** Fights from the simulation: [x, y, attacker clade, victim clade, kind, step]; kinds as Telemetry.F_*. */
  addFights(list) {
    const now = performance.now();
    const c = this.controls.target, W = this.W;
    const k = this.scale.markK;   // fight marks are as big as the bodies are drawn (a 1 kg body's)
    for (const f of list) {
      const [x, y, , , kind, , sa, sv] = f;
      if (kind === 0 && this.fx.length > FX_MAX / 2) continue;   // misses are the first to go when it is busy
      // the two animals: the attacker lunges, the victim is thrown back and bleeds
      const A = sa ? this.agents.get(sa) : null, V = sv ? this.agents.get(sv) : null;
      const vk = k * Math.max(0.6, (V && V.size || 1.5) / 1.5);   // the victim's size, as the old marks were for a 1 kg body
      this.fx.push({ x, y, t0: now, kind, k: vk });
      if (kind === 2 || kind === 4 || kind === 5) this.stains.push({ x, y, t0: now, r: (kind === 5 ? 0.5 : 1.1) * vk });
      let ux = 0, uy = 0;
      if (A && V) {
        ux = V.x - A.x; uy = V.y - A.y; ux -= Math.round(ux / W) * W; uy -= Math.round(uy / W) * W;
        const d = Math.hypot(ux, uy) || 1; ux /= d; uy /= d;
        A.lungeT = now; A.lx = ux; A.ly = uy; A.lungeD = Math.min(d * 0.8, (0.35 + 0.6 * (A.size || 1)) * k);
      }
      if (V && kind !== 0) { V.hitT = now; V.hx = ux; V.hy = uy; V.hitD = (kind === 2 ? 0.9 : 0.5) * (V.size || 1) * k; }
      if (kind === 0) continue;
      const vs = V ? V.size || 1 : 1, gx = x + W * Math.round((c.x - x) / W), gy = y + W * Math.round((c.z - y) / W);
      const z = this.heightAt(gx, gy) + vs * 0.6 * k;
      const N = [0, 14, 60, 10, 40, 8][kind] * Math.min(2.5, Math.max(0.6, vs));
      if (kind !== 5) this._spray(gx, gy, z, Math.round(N), 3 + 2 * vs, ux, uy, k);
      // the dead stay where they fell
      if ((kind === 2 || kind === 4) && V && !this.dead.has(sv)) {
        this.dead.add(sv);
        this.corpses.push({ e: { ...V }, x: V.x, y: V.y, h: V.h + (Math.random() - 0.5), t0: now, serial: sv });
        if (this.corpses.length > 120) { const old = this.corpses.shift(); this.dead.delete(old.serial); }
        // a kill close to where the overview looks shakes it (by a share of the view's distance)
        const dc = Math.hypot(gx - c.x, gy - c.z), camD = this.camera.position.distanceTo(c), R = Math.max(30 * vs * k, 0.4 * camD);
        if (this.mode === 'overview' && dc < R && camD < 200 * vs * k) this.shake = Math.max(this.shake, 0.012 * camD * (1 - dc / R) * Math.min(2, vs));
      }
    }
    if (this.fx.length > FX_MAX) this.fx.splice(0, this.fx.length - FX_MAX);
    if (this.stains.length > STAIN_MAX) this.stains.splice(0, this.stains.length - STAIN_MAX);
  }

  /** Distance on the map from the centre of the view to a point (for the fight sounds). */
  viewDist(x, y) {
    const c = this.controls.target, W = this.W;
    let dx = x - c.x, dy = y - c.z; dx -= Math.round(dx / W) * W; dy -= Math.round(dy / W) * W;
    return Math.hypot(dx, dy) + 0.5 * Math.max(0, this.camera.position.distanceTo(c) - 30);
  }

  /** Move the camera over a place on the map (a fight in the log). */
  lookAt(x, y) {
    if (this.mode !== 'overview') return;
    const c = this.controls.target, W = this.W;
    x += W * Math.round((c.x - x) / W); y += W * Math.round((c.z - y) / W);   // the copy nearest the view
    // close enough to see the fight (the animals as drawn: a few dozen body lengths), never farther than now
    const off = this.camera.position.clone().sub(c), d = off.length(), lim = Math.max(0.5, 80 * this.scale.markK * 1.5);
    if (d > lim) off.multiplyScalar(lim / d);
    const g = this.heightAt(x, y), c1 = new THREE.Vector3(x, g, y);
    this._flyTo(c1, c1.clone().add(off).setY(g + Math.max(0.02, off.y)), 600);
  }

  _drawBlood(now) {
    const dt = Math.min(0.05, (now - this._lastFx) / 1000); this._lastFx = now;
    const m = new THREE.Matrix4(), keep = [];
    let k = 0;
    for (const b of this.blood) {
      b.vz -= 18 * b.k * dt; b.x += b.vx * dt; b.y += b.vy * dt; b.z += b.vz * dt;
      const g = this.heightAt(b.x, b.y);
      if (b.z <= g) {   // it lands: a spatter on the ground
        if (Math.random() < 0.25) this.stains.push({ x: b.x, y: b.y, t0: now, r: (0.12 + 0.18 * Math.random()) * b.k });
        continue;
      }
      keep.push(b);
      m.makeScale(b.s, b.s, b.s); m.setPosition(b.x, b.z, b.y);
      this.bloodMesh.setMatrixAt(k++, m);
    }
    this.blood = keep;
    if (this.stains.length > STAIN_MAX) this.stains.splice(0, this.stains.length - STAIN_MAX);
    this.bloodMesh.count = k; this.bloodMesh.instanceMatrix.needsUpdate = true;
  }

  _drawFights(cx, cz) {
    const now = performance.now(), W = this.W, m = new THREE.Matrix4(), col = new THREE.Color();
    const near = (v, c) => v + W * Math.round((c - v) / W);
    // kinds: 0 miss (white puff), 1 wound (orange), 2 kill (red, big), 3 blood drunk (purple), 4 drained to death, 5 egg eaten
    const LIFE = [600, 1200, 2600, 1200, 2600, 1600], SIZE = [0.8, 1.4, 3.2, 1.2, 2.6, 1.2],
          RGB = [[0.9, 0.9, 0.9], [1, 0.55, 0.1], [1, 0.08, 0.05], [0.75, 0.2, 0.9], [0.85, 0.05, 0.5], [1, 0.85, 0.3]];
    let k = 0;
    this.fx = this.fx.filter((e) => now - e.t0 < LIFE[e.kind]);
    for (const e of this.fx) {
      const a = (now - e.t0) / LIFE[e.kind], x = near(e.x, cx), y = near(e.y, cz), s = SIZE[e.kind] * (0.4 + 1.6 * a) * (e.k || 1);
      m.makeScale(s, 1, s); m.setPosition(x, this.heightAt(x, y) + 0.4 * (e.k || 1), y);
      this.flashMesh.setMatrixAt(k, m);
      const f = 1 - a; col.setRGB(RGB[e.kind][0] * f, RGB[e.kind][1] * f, RGB[e.kind][2] * f);
      this.flashMesh.setColorAt(k, col);
      k++;
    }
    this.flashMesh.count = k; this.flashMesh.instanceMatrix.needsUpdate = true; if (k) this.flashMesh.instanceColor.needsUpdate = true;
    const STAIN = 45000;   // a kill's blood stays about 45 s
    this.stains = this.stains.filter((e) => now - e.t0 < STAIN);
    k = 0;
    for (const e of this.stains) {
      const a = (now - e.t0) / STAIN, x = near(e.x, cx), y = near(e.y, cz), s = e.r * (1 - 0.6 * a);
      m.makeScale(s, 1, s); m.setPosition(x, this.heightAt(x, y) + Math.min(0.08, 0.1 * e.r), y);
      this.stainMesh.setMatrixAt(k++, m);
    }
    this.stainMesh.count = k; this.stainMesh.instanceMatrix.needsUpdate = true;
  }

  setMode(m) {
    this.mode = m;
    this.controls.enabled = m === 'overview';
    this._fly = null; this._camT = 0;
    this.scale.explore = m === 'explore';    // Explore and the vehicles: their own creature size (js/scale.js)
    if (m === 'explore') { this.explore?.setView(); this._plants(true); return; }   // EXPLORE hook: js/explore/ sets the lens and fog
    this.camera.fov = m === 'ride' ? 78 : 55; if (m !== 'overview') this.camera.near = 0.005; this.camera.updateProjectionMatrix();
    this.scene.fog.near = m === 'ride' ? 60 : 250; this.scene.fog.far = m === 'ride' ? 380 : 900;
    if (m === 'overview' && this._selPos) this.controls.target.set(this._selPos[0], 0, this._selPos[1]);
    this._plants(true);
  }

  /** Where a screen point meets the ground, as map coordinates (unwrapped: the copy in view, when near), or null. */
  groundAt(clientX, clientY, unwrapped) {
    const r = this.canvas.getBoundingClientRect();
    const ndc = new THREE.Vector2(((clientX - r.left) / r.width) * 2 - 1, -((clientY - r.top) / r.height) * 2 + 1);
    const ray = new THREE.Raycaster(); ray.setFromCamera(ndc, this.camera);
    const o = ray.ray.origin, d = ray.ray.direction, W = this.W, t0 = this._marchMin();
    let t = 0;
    for (let i = 0; i < 800; i++) {
      const p = o.clone().addScaledVector(d, t), h = this.heightAt(p.x, p.z);
      if (p.y <= h + t0) return unwrapped ? [p.x, p.z, t] : [((p.x % W) + W) % W, ((p.z % W) + W) % W, t];
      t += Math.max(t0 + t * 0.002, (p.y - h) * 0.5);
    }
    return null;
  }
  /** The smallest step of a ray marched to the ground: a share of the view's distance (close up a few cm). */
  _marchMin() { return Math.max(0.002, Math.min(0.5, this.camera.position.distanceTo(this.controls.target) * 0.004)); }

  /** Show the person's call where it was made (sounds: 1..8, up to 3). */
  showMyCall(x, y, sounds) { this.myCall = { x, y, sounds: Array.isArray(sounds) ? sounds : [sounds], t0: performance.now() }; }

  _drawCalls(now, cx, cz) {
    const W = this.W, m = new THREE.Matrix4(), col = new THREE.Color(), near = (v, c) => v + W * Math.round((c - v) / W);
    let k = 0;
    // an utterance is a stack of rings, one per sound in its order: the first lowest and innermost
    const ring = (x, y, s, sound, a, p = 0) => {
      if (k >= CALL_MAX) return;
      m.makeScale(s, 1, s); m.setPosition(x, this.heightAt(x, y) + (0.5 + 0.45 * p) * Math.min(1, s), y);
      this.callMesh.setMatrixAt(k, m);
      const c = SOUND_RGB[sound - 1]; col.setRGB(c[0] * a, c[1] * a, c[2] * a); this.callMesh.setColorAt(k, col);
      k++;
    };
    if (this.myCall) {
      const a = (now - this.myCall.t0) / 2500;
      if (a > 1) this.myCall = null;
      else for (let r = 0; r < 3; r++) {
        const q = (a * 3 + r / 3) % 1;
        this.myCall.sounds.forEach((snd, p) => ring(near(this.myCall.x, cx), near(this.myCall.y, cz), 2 + 1.5 * p + q * 22, snd, 1 - q, p));
      }
    }
    const camX = this.camera.position.x, camZ = this.camera.position.z;
    for (const e of this.agents.values()) {
      if (!e.call || !e.callT || now - e.callT > 600 || this.hidden.has(e.sp)) continue;
      const x = near(e.x, cx), y = near(e.y, cz);
      if ((x - camX) ** 2 + (y - camZ) ** 2 > 140 * 140) continue;
      const q = ((now - e.callT) / 600), sz = this._size(e);
      ring(x, y, sz * (1.2 + 2.5 * q), e.call, 1 - q, 0);
      if (e.call2) ring(x, y, sz * (1.6 + 2.5 * q), e.call2, 1 - q, 1);
      if (e.call3) ring(x, y, sz * (2.0 + 2.5 * q), e.call3, 1 - q, 2);
    }
    this.callMesh.count = k; this.callMesh.instanceMatrix.needsUpdate = true; if (k) this.callMesh.instanceColor.needsUpdate = true;
  }

  /** Pick the animal nearest to where the screen point meets the ground. */
  pick(clientX, clientY) {
    const r = this.canvas.getBoundingClientRect();
    const ndc = new THREE.Vector2(((clientX - r.left) / r.width) * 2 - 1, -((clientY - r.top) / r.height) * 2 + 1);
    const ray = new THREE.Raycaster(); ray.setFromCamera(ndc, this.camera);
    // march the ray to the ground
    const o = ray.ray.origin, d = ray.ray.direction, t0 = this._marchMin();
    let t = 0, hit = null;
    for (let i = 0; i < 800; i++) {
      const p = o.clone().addScaledVector(d, t), h = this.heightAt(p.x, p.z);
      if (p.y <= h + t0 * 3) { hit = p; break; }
      t += Math.max(t0 + t * 0.002, (p.y - h) * 0.5);
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
    const reach = Math.max(0.05, t * 0.03, 3 * this.scale.markK * 1.5);   // ~25 px on screen, or a few drawn bodies
    return bd < reach * reach ? best : 0;
  }

  render(probe) {
    if (!this.tiles) { this.look.update(); this.look.render(); return; }   // [look]
    const now = performance.now();
    const alpha = Math.min(1, (now - this.frameT) / this.frameDt);
    const W = this.W;
    // camera anchor for wrapping
    const sel = this.selected ? this.agents.get(this.selected) : null;
    let selP = null;
    const c = this.controls.target;
    if (sel) selP = this._pos(sel, alpha, c.x, c.z);
    this._selPos = selP;

    if (this.mode === 'explore' && this.explore) {
      this.explore.drive(now, c);   // EXPLORE hook: the person moves the camera and the view centre (js/explore/)
    } else if (this.mode !== 'overview' && selP) {
      const sz = this._size(sel);
      const gh = this._height(sel, selP[0], selP[1]);
      const fx = Math.cos(selP[2]), fz = Math.sin(selP[2]);
      // the camera's easing per frame as at 60 frames a second, whatever the frame rate (a slow machine's camera kept up)
      const fdt = Math.min(0.5, Math.max(0, (now - (this._camT || now)) / 1000)); this._camT = now;
      const ease = (k) => 1 - Math.pow(1 - k, fdt * 60);
      this._camH = this._camH === undefined ? selP[2] : lerpAngle(this._camH, selP[2], ease(0.08));
      const hx = Math.cos(this._camH), hz = Math.sin(this._camH);
      // (distances in the animal's own drawn lengths, MODEL_LEN x sz: the view keeps its framing at any size. 2026-11-02:
      // about 6.5 lengths behind and 2.2 above, so a 20 cm animal at true size is a sixth of the view's height)
      if (this.mode === 'follow') {
        const L = sz * MODEL_LEN, dist = L * 6.5, up = L * 2.2;
        const want = new THREE.Vector3(selP[0] - hx * dist, gh + up, selP[1] - hz * dist);
        want.y = Math.max(want.y, this.heightAt(want.x, want.z) + sz * 0.8);
        this.camera.position.lerp(want, ease(0.12));
        // on its way there (from the overview, or round a hill the animal walked past) the camera never goes into the ground
        const cp = this.camera.position, floor = this.heightAt(cp.x, cp.z) + sz * 0.5;
        if (cp.y < floor) cp.y = floor;
        this.camera.lookAt(selP[0], gh + sz * 0.6, selP[1]);
      } else {
        const eye = new THREE.Vector3(selP[0] + fx * sz * 0.6, gh + sz * 0.9, selP[1] + fz * sz * 0.6);
        this.camera.position.lerp(eye, ease(0.35));
        this.camera.lookAt(eye.x + hx * sz * 20, eye.y - sz * 1.5, eye.z + hz * sz * 20);
      }
      // the near plane as far out as the framing allows (the follow camera is lengths away, the ride-along's eye is on the
      // body) and the far plane at the fog: depth precision for true-size bodies a few cm long, the shores over the lakes
      const nr = Math.max(0.0002, sz * (this.mode === 'follow' ? 0.3 : 0.06)), fr = Math.max(600, this.scene.fog.far * 1.3);
      if (Math.abs(nr - this.camera.near) > nr * 0.2 || Math.abs(fr - this.camera.far) > fr * 0.2) { this.camera.near = nr; this.camera.far = fr; this.camera.updateProjectionMatrix(); }
      c.set(selP[0], 0, selP[1]);
    } else {
      // held keys pan and zoom (W A S D, the arrows, Q / E), as fast as the zoom: a share of the distance a second
      const kdt = Math.min(0.25, Math.max(0, (now - (this._keyT || now)) / 1000)); this._keyT = now;
      this._keyMove(kdt);
      // a flight (fitWorld, a double-click, a fight in the log): eased from where the camera was
      if (this._fly) {
        const F = this._fly, u = Math.min(1, (now - F.t0) / F.dur), e = u * u * (3 - 2 * u);
        c.lerpVectors(F.c0, F.c1, e); this.camera.position.lerpVectors(F.p0, F.p1, e);
        if (u >= 1) this._fly = null;
      }
      // keep the orbit target inside the base tile; move the camera with it
      if (c.x < 0 || c.x >= W || c.z < 0 || c.z >= W) {
        const sx = -Math.floor(c.x / W) * W, sz = -Math.floor(c.z / W) * W;
        c.x += sx; c.z += sz; this.camera.position.x += sx; this.camera.position.z += sz;
      }
      // zoomed far out the camera looks down more steeply: it stays within 0.7 of a map of the view centre, over the
      // map's own copy, so the tiling's faded outer edge is never in the foreground
      {
        const dd = this.camera.position.distanceTo(c);
        this.controls.maxPolarAngle = Math.min(Math.PI * 0.47, Math.asin(Math.min(1, 0.7 * W / Math.max(1, dd))));
      }
      this.controls.update();
      // the view centre stays on the ground (zooming toward the cursor moves it over a flat plane), and the camera above it
      if (!this._fly && this.world) {
        const g = this.heightAt(c.x, c.z), dy = (g - c.y) * 0.2;
        c.y += dy; this.camera.position.y += dy;
        const cam = this.camera.position, clear = this.heightAt(cam.x, cam.z) + Math.max(0.006, 0.08 * cam.distanceTo(c));
        if (cam.y < clear) cam.y = clear;
        // and the view centre is in sight: no ridge between them (leaving Explore on a slope, or orbiting low across
        // the hills, put the camera inside a hillside looking at the inside of the ground). The line from the centre to
        // the camera must clear the ground by a share of the distance at each point along it.
        cam.y = Math.max(cam.y, this._sightY(c, cam));
      }
      // fog and the terrain tiles follow the zoom, so zooming out never blanks the ground
      const camD = this.camera.position.distanceTo(c);
      this.scene.fog.near = Math.max(250, camD * 0.7); this.scene.fog.far = Math.max(900, camD * 2.4);
      // the near plane follows the zoom too, for depth precision where the shores lie a fraction of a unit above the lakes
      const near = Math.min(4, Math.max(0.0015, camD * 0.004)), far = Math.max(1200, camD * 3 + this.W);
      if (Math.abs(near - this.camera.near) > near * 0.2 || Math.abs(far - this.camera.far) > far * 0.2) {
        this.camera.near = near; this.camera.far = far; this.camera.updateProjectionMatrix();
      }
    }
    const cx = c.x, cz = c.z;

    // the water and karst tiles around the anchor (each only if some of it is within fog distance of the camera), and
    // the ground's level of detail for this view (terrain/ground.js: nodes of the same 3x3 tiling)
    const tx = Math.floor(cx / W), tz = Math.floor(cz / W);
    let k = 0;
    for (let dz = -1; dz <= 1; dz++) for (let dx = -1; dx <= 1; dx++) {
      const set = this.tiles[k++];
      if (!set) continue;
      const ox = (tx + dx) * W, oz = (tz + dz) * W;
      const qx = Math.max(ox, Math.min(this.camera.position.x, ox + W)), qz = Math.max(oz, Math.min(this.camera.position.z, oz + W));
      const vis = Math.hypot(qx - this.camera.position.x, qz - this.camera.position.z) < this.scene.fog.far;
      for (const o of set) { o.position.set(ox, 0, oz); o.visible = vis; }
      // ribbons and karst only near the camera
      if (vis) for (const [m, ch, far] of this.nearTiles[k - 1]) {
        const x0 = ox + ch.x0, z0 = oz + ch.z0;
        const ddx = Math.max(x0 - this.camera.position.x, 0, this.camera.position.x - x0 - ch.size), ddz = Math.max(z0 - this.camera.position.z, 0, this.camera.position.z - z0 - ch.size);
        m.visible = Math.hypot(ddx, ddz) < far;
      }
    }
    if (this.ground) {
      const tf = this._tFrustum ||= new THREE.Frustum();
      this.camera.updateMatrixWorld();
      tf.setFromProjectionMatrix((this._tpv ||= new THREE.Matrix4()).multiplyMatrices(this.camera.projectionMatrix, this.camera.matrixWorldInverse));
      this.ground.update(this.camera, tf, tx, tz, this.scene.fog.far);
      this.waterU.uTime.value = now / 1000;
    }
    // the wrap: shown in the overview once the view takes in a good part of the map, invisible close up
    {
      const camD = this.camera.position.distanceTo(c), U = this.wrapU;
      const sw = this.mode === 'overview' ? smooth(0.12 * W, 0.35 * W, camD) : 0;
      if ((sw > 0.5) !== (this._wrapS > 0.5)) this._plantC = null;   // re-plant: the planted area changes with it
      this._wrapS = sw;
      U.uWrapC.value.set(cx, cz); U.uWrapS.value = sw; U.uWrapLine.value = Math.max(0.5, camD * 0.0025); U.uWrapHalf.value = W / 2;
    }
    this._plants(false);
    if (this.flames) { this.flames.material.opacity = 0.65 + 0.3 * Math.sin(now / 90); }

    // organisms: one mesh of detailed bodies near the camera, one of low-detail bodies beyond
    let total = 0;
    for (const e of this.agents.values()) if (e.stage !== 0) total++;
    if (total > this.far.cap) { let cap = this.far.cap; while (cap < total) cap *= 2; this.far = this._allocBodies(this.far, this.farGeo, cap); }
    const near = this.near, far = this.far, rgb = this.cladeRGB, P3 = [0, 0, 0], SL = [0, 0];
    const camX = this.camera.position.x, camY = this.camera.position.y, camZ = this.camera.position.z;
    // sizes: true size x the magnification (bk: world units per view-size unit); on screen, pixels per unit at distance 1
    const bk = this.scale.bodyK, ppu = this.canvas.clientHeight / (2 * Math.tan(this.camera.fov * Math.PI / 360));
    const lenPx = MODEL_LEN * ppu, nearK = lenPx / NEAR_PX;   // a body of drawn size sc is detailed within nearK x sc
    const dots = DOT_PX / lenPx;   // the smallest drawn size per unit of distance (every view)
    const U = this.bodyMat.userData.uniforms;
    U.uTime.value = now / 1000; U.uBodyK.value = bk;
    U.uSpeciesOverlay.value = this.speciesOverlay ? 1 : 0;
    let nn = 0, nf = 0, ne = 0;
    // with tens of thousands of organisms, draw every near one and a sample of the far ones (by serial, so the
    // same ones stay drawn from frame to frame): far bodies are a few pixels, and the frame time stays bounded
    const farStride = Math.max(1, Math.ceil(total / FAR_BUDGET));
    const m = new THREE.Matrix4(), q = new THREE.Quaternion(), p = new THREE.Vector3(), s = new THREE.Vector3();
    const eggCol = new THREE.Color(), white = new THREE.Color(1, 1, 1);
    const t = now / 1000, Wh = W / 2;
    // only what the camera can see is drawn (the meshes themselves are not culled: their instances cover the world)
    const fr = this._frustum ||= new THREE.Frustum(), sph = this._sphere ||= new THREE.Sphere();
    this.camera.updateMatrixWorld();
    fr.setFromProjectionMatrix((this._pv ||= new THREE.Matrix4()).multiplyMatrices(this.camera.projectionMatrix, this.camera.matrixWorldInverse));
    const inView = (x, gy, z, r) => { sph.center.set(x, gy, z); sph.radius = r; return fr.intersectsSphere(sph); };
    beginBodies(near); beginBodies(far);
    for (const [serial, e] of this.agents) {
      if (this.hidden.has(e.sp) || this.dead.has(serial)) continue;
      if (farStride > 1 && serial % farStride !== 0 && serial !== this.selected) {
        let qx = e.x - camX, qz = e.y - camZ;
        qx -= Math.round(qx / W) * W; qz -= Math.round(qz / W) * W;
        const nr = nearK * (e.size || 1.5) * this.scale.markK;
        if (qx * qx + qz * qz >= nr * nr) continue;
      }
      let [x, y, h] = this._pos(e, alpha, cx, cz, P3);
      const ci = e.sp * 3;
      let acts = e.acts, cr = rgb[ci], cg = rgb[ci + 1], cb = rgb[ci + 2];
      if (e.lungeT && now - e.lungeT < 380) { const f = Math.sin((now - e.lungeT) / 380 * Math.PI) * e.lungeD; x += e.lx * f; y += e.ly * f; acts |= 2; }
      if (e.hitT && now - e.hitT < 450) {
        const a = 1 - (now - e.hitT) / 450, f = a * a * e.hitD;
        x += e.hx * f; y += e.hy * f; cr += (1 - cr) * a; cg *= 1 - 0.8 * a; cb *= 1 - 0.8 * a;   // thrown back, flushed red
      }
      if (e.stage === 0) {
        if (ne >= 4096) continue;
        const er = this._eggR(e), ey = this.heightAt(x, y) + er * 0.8;
        if (!inView(x, ey, y, er * 2)) continue;
        const es = er / 0.35; p.set(x, ey, y); q.identity(); s.set(es, 0.8 * es, es);
        this.eggs.setMatrixAt(ne, m.compose(p, q, s));
        if (this.speciesOverlay) eggCol.setRGB(rgb[ci], rgb[ci + 1], rgb[ci + 2]);
        else appearanceColor(e, eggCol);
        this.eggs.setColorAt(ne, eggCol.lerp(white, 0.6));
        ne++; continue;
      }
      const tsc = this._trueSize(e);
      let sc = this._size(e);
      let gy = this._height(e, x, y, sc);
      // a flier in the air (eased up and down as it takes off and lands): wings out; landed it walks or perches
      let rs = e.rest ? 1 : 0;
      const air = e.fly ? this._air(e, x, y, now) : 0;
      if (air > 0.01) { gy += this._lift(sc, t, serial) * air; e.pose = 'Fly'; rs = e.rest ? 1 : 1 - air; e.grounded = false; }
      else if (e.fly) e.pose = e.rest ? 'Fly' : e.pose || 'Walk';
      // gait from the distance moved since the last frame
      let mdx = e.x - e.px, mdy = e.y - e.py;
      if (mdx > Wh) mdx -= W; else if (mdx < -Wh) mdx += W;
      if (mdy > Wh) mdy -= W; else if (mdy < -Wh) mdy += W;
      const gait = Math.min(1, Math.hypot(mdx, mdy) / (0.25 * tsc + 0.05 * bk));   // (its true size: it moves truly)
      e.gait = e.gait === undefined ? gait : e.gait + (gait - e.gait) * 0.15;
      const ddx = x - camX, ddy = gy - camY, ddz = y - camZ, dist = Math.sqrt(ddx * ddx + ddy * ddy + ddz * ddz);
      if (sc < dots * dist) { sc = dots * dist; if (air > 0.01) gy = Math.max(gy, this.heightAt(x, y) + sc * air); }   // never smaller than a speck
      if (!inView(x, gy, y, 3 * sc + 0.02)) continue;   // a body (wings and tail spread) lies well within 3 x its size
      const sy = sc * (e.rest && !e.fly ? 0.7 : 1), ph = (serial % 997) / 997;
      // out of breath (oxygen debt, 0..15) and calling ride above the action byte (anim.js)
      acts |= Math.round((e.breath || 0) * 15) * 256 | (e.call && e.callT && now - e.callT < 600 ? 4096 : 0);
      acts += packK(sc / tsc);   // its display factor (multiplier, minimum pixels), for the shader to read its true mass
      // each organism keeps its instance slot (keyed by serial) while it stays in a mesh: only its matrix and animation
      // are sent each frame. A near one goes to the far mesh when the near one is full; a near one standing on the
      // ground is sheared to its slope (feet on the slope, not in it or above it)
      const isNear = dist < nearK * sc;
      // sheared to the slope when near, or when drawn long enough (magnified) that a slope under it shows: its feet on
      // the ground, not buried at one end and floating at the other
      if ((isNear || sc > SLOPE_SC) && e.grounded) this._slope(x, y, h, sc, SL); else SL[0] = SL[1] = 0;
      if (isNear && placeBody(near, serial, x, gy, y, h, sc, sy, e, e.gait, ph, acts, rs, cr, cg, cb, SL[0], SL[1])) nn++;
      else if (placeBody(far, serial, x, gy, y, h, sc, sy, e, e.gait, ph, acts, rs, cr, cg, cb, SL[0], SL[1])) nf++;
    }
    // the killed: flattened, darkened bodies lying where they fell, sinking into the ground at the end
    this.corpses = this.corpses.filter((d) => { if (now - d.t0 < CORPSE_LIFE) return true; this.dead.delete(d.serial); return false; });
    for (const d of this.corpses) {
      const e = d.e, x = d.x + W * Math.round((cx - d.x) / W), y = d.y + W * Math.round((cz - d.y) / W);
      const a = (now - d.t0) / CORPSE_LIFE, sc = this._size(e), pk = packK(sc / this._trueSize(e));
      const gy = this._height(e, x, y, sc) - sc * 0.15 - Math.max(0, a - 0.75) * 4 * sc * 0.6;
      if (!inView(x, gy, y, 3 * sc + 0.02)) continue;
      const ddx = x - camX, ddz = y - camZ, nr = nearK * sc;
      const ci = e.sp * 3, dk = 0.45 - 0.2 * a, r = rgb[ci] * dk + 0.18, g = rgb[ci + 1] * dk * 0.5, b = rgb[ci + 2] * dk * 0.5;
      const key = -d.serial;   // a corpse's slot is apart from the living animal's (serials are > 0)
      e.pose = null;           // it lies as its body is built, not as it last moved (a flier shot down lies with wings folded)
      const isNear = ddx * ddx + ddz * ddz < nr * nr;
      if (isNear && e.grounded) this._slope(x, y, d.h, sc, SL); else SL[0] = SL[1] = 0;
      if (isNear && placeBody(near, key, x, gy, y, d.h, sc * 1.15, sc * 0.38, e, 0, 0, pk, 1, r, g, b, SL[0], SL[1])) nn++;
      else if (placeBody(far, key, x, gy, y, d.h, sc * 1.15, sc * 0.38, e, 0, 0, pk, 1, r, g, b, SL[0], SL[1])) nf++;
    }
    finishBodies(near); finishBodies(far);
    this.eggs.count = ne; this.eggs.instanceMatrix.needsUpdate = true; if (this.eggs.instanceColor) this.eggs.instanceColor.needsUpdate = true;

    // selection marker, field of view and perceived animals
    if (sel && selP) {
      const sz = this._size(sel);
      const gh = this._height(sel, selP[0], selP[1]);
      this.selRing.visible = this.mode !== 'ride';
      // the ring round its body, and (overview) a beam to find it from afar, as tall as a share of the view's distance
      // (the distance to the animal itself: following, the view centre c is at height 0, and a true-size body's ring was
      // as wide as the camera's height above sea level allowed)
      const camD = this.camera.position.distanceTo(c), ringD = Math.hypot(this.camera.position.x - selP[0], this.camera.position.y - gh, this.camera.position.z - selP[1]);
      const rs = Math.max(sz * 1.5, (this.mode === 'overview' ? ringD * 0.012 : 0)) * (1 + 0.08 * Math.sin(now / 200));
      this.selRing.position.set(selP[0], gh + Math.min(0.2, sz * 0.1), selP[1]); this.selRing.scale.setScalar(rs);
      this.selBeam.visible = this.mode === 'overview'; this.selBeam.position.set(selP[0], gh, selP[1]);
      this.selBeam.scale.set(camD * 0.004 / 0.06, camD * 0.25 / 30, camD * 0.004 / 0.06);
      const cl = (this.clades && this.clades[sel.sp]) || {};
      const R = probe && probe.serial === this.selected ? probe.sight : (cl.sight || 20);
      const fov = (sel.scan ? 360 : (cl.fov || 300)) * Math.PI / 180;
      this.fov.visible = this.mode !== 'overview';
      if (this._fovKey !== R + ':' + fov) {
        this._fovKey = R + ':' + fov;
        this.fov.geometry.dispose();
        this.fov.geometry = new THREE.CircleGeometry(R, 48, -fov / 2, fov); this.fov.geometry.rotateX(-Math.PI / 2);
      }
      this.fov.position.set(selP[0], gh + Math.min(0.35, sz * 0.2), selP[1]); this.fov.rotation.set(0, -selP[2], 0);
      this._perceive(sel, selP, R, fov, alpha, cx, cz);
    } else { this.selRing.visible = this.selBeam.visible = this.fov.visible = false; this.percMesh.count = 0; }
    this._drawFights(cx, cz);
    this._drawBlood(now);
    this._drawCalls(now, cx, cz);

    // a kill close by shakes the view
    let sx = 0, sy = 0;
    if (this.shake > 0.01) { sx = (Math.random() - 0.5) * this.shake; sy = (Math.random() - 0.5) * this.shake; this.camera.position.x += sx; this.camera.position.y += sy; this.shake *= 0.86; }
    this.look.update(); this.look.render();   // [look] light, sky and fog for this view, then the finish (js/look/post.js)
    this.camera.position.x -= sx; this.camera.position.y -= sy;
  }

  // ---- EXPLORE hooks (js/explore/): the ground and water under the person, and where a creature is drawn now
  /** Drawn ground height and standing/running water surface (NaN: none) at a world position. */
  groundWater(x, y) { if (!this.world) return [0, NaN]; return [this._ground(x, y), this.surfH[this._cellOf(x, y)]]; }
  /** Where organism e is drawn this frame (the copy nearest the view centre): x, z, ground y, centre y, radius. */
  creatureNow(serial, e, out) {
    const c = this.controls.target, now = performance.now(), P3 = this._p3 ||= [0, 0, 0];
    const [x, y] = this._pos(e, Math.min(1, (now - this.frameT) / this.frameDt), c.x, c.z, P3);
    if (e.stage === 0) { const r = this._eggR(e); out.x = x; out.z = y; out.gy = this.heightAt(x, y); out.cy = out.gy + 0.8 * r; out.r = r; return out; }
    const sc = this._size(e);
    let gy = this._height(e, x, y, sc);
    if (e.fly && e.air > 0.01) gy += this._lift(sc, now / 1000, serial) * e.air;
    out.x = x; out.z = y; out.gy = gy; out.cy = gy + 0.45 * sc; out.r = 0.55 * sc;
    return out;
  }

  /**
   * Where an organism of drawn size sc stands at (x, y): on the ground; in water where the sim holds it
   * (Respiration.Uptake; AGENTS format 7: depth in m and the share of the step spent there): a swimmer (0.1 + 0.9 fins
   * >= DEEP_SWIM, whatever its locomotion label) at its depth, so a fish stays down and an air-breather dives for that
   * share of a cycle and comes up to breathe for the rest, never below the bed; any other body at depth walks on the bed
   * (its depth is the whole column), else it wades or paddles at the surface. Sets e.under (0..15: how far the water
   * hides it, with depth and the water's murk) for the body shader, e.grounded (its feet on the ground: it is sheared to
   * the slope) and e.pose ('Swim' for a wader or landed flier swimming under water; null: as its locomotion).
   */
  _height(e, x, y, sc = this._size(e)) {
    e.under = 0; e.grounded = true; e.pose = null;
    if (!this.world) return 0;
    const g = this.ground ? this.ground.heightAt(x, y, this.camera.position) : this._ground(x, y), ci = this._cellOf(x, y), sf = this.surfH[ci];
    if (!(sf === sf) || sf <= g) return g;                       // dry ground (sf NaN: no water here)
    const swimmer = e.swim || (e.fins || 0) >= SWIM_FINS;
    let dm = e.depth > 0 ? e.depth : 0, y0;
    if (!swimmer) {
      y0 = dm > 0 ? g : Math.max(g, sf - 0.4 * sc);             // on the bed; or wading, or paddling with its back awash
      e.grounded = y0 === g;
    } else {
      const at = e.atDepth === undefined ? 1 : e.atDepth;
      if (dm > 0 && at < 0.98) {
        // an 8 s cycle (its own phase): down for the share 'at', easing up and down over a tenth of it, at the surface the rest
        const u = (performance.now() / 8000 + ((e.serial || 0) % 997) / 997) % 1, w = 0.1;
        dm *= at <= 0.02 ? 0 : smooth(0, w, u) * (1 - smooth(at - w, at, u));
      }
      if (!e.swim && dm <= 0.05) { y0 = Math.max(g, sf - 0.4 * sc); e.grounded = y0 === g; }   // a swimming wader at the surface wades
      else {
        y0 = Math.max(g + 0.25 * sc, Math.min(sf - 0.3 * sc, sf - dm * this.scale.upm));
        e.grounded = false;
        if (!e.swim) e.pose = 'Swim';                            // under water a wader (or a landed flier) swims
      }
    }
    // lost from view over a few metres in clear water, sooner where algae and plankton cloud it
    const murk = this._murkAt(ci), vis = 6 / (1 + 3 * murk), dd = (sf - y0) / this.scale.upm;
    if (y0 < sf - 0.45 * sc) e.under = Math.min(15, Math.round(15 * 0.92 * (1 - Math.exp(-dd / vis))));   // (wading: its back is dry)
    return y0;
  }

  /** The water's murk at a cell, 0..1 (algae and plankton in the FIELDS frame). */
  _murkAt(ci) {
    const f = this.fields; if (!f || !this.world) return 0;
    const fw = this.world.fw, d = f.d, fi = Math.min(d - 1, ((ci / fw) | 0) >> 1) * d + Math.min(d - 1, (ci % fw) >> 1);
    const ka = this._kAlgae ??= this._layer('algae'), kp = this._kPhyto ??= this._layer('phyto');
    return Math.min(1, ((ka >= 0 ? f.data[ka * d * d + fi] : 0) + (kp >= 0 ? f.data[kp * d * d + fi] : 0)) / 255);
  }

  /**
   * How far flier e is in the air now, 0 landed .. 1 flying, eased over about half a second as it takes off and lands.
   * The sim (Resolve, World.Airborne) lands a flier whenever it can stand where it is and goes no faster than it walks
   * (resting, eating or drinking land it); the frame does not say so, so it is read from its speed (e.vm, metres per
   * step: a landed flier walks at most a few tens of metres a step, a flying one covers hundreds) and the ground.
   */
  _air(e, x, y, now) {
    let want = 0;
    if (!e.rest) {
      const ci = this._cellOf(x, y), cell = this.world ? this.world.cell[ci] : 1;
      const canLand = (cell & 1) || ((e.fins || 0) >= SWIM_FINS) || ((cell & 2) && (e.legs || 0) >= 0.3);   // Body.GroundRealm
      want = !canLand || e.vm > FLY_VM * Math.pow(e.mass || 1, 0.17) ? 1 : 0;
    }
    if (e.air === undefined) e.air = want;
    const dt = Math.min(0.25, Math.max(0, (now - (e.airT || now)) / 1000)); e.airT = now;
    e.air += Math.sign(want - e.air) * Math.min(Math.abs(want - e.air), dt / 0.6);
    return smooth(0, 1, e.air);
  }

  /** The ground's slope under a body of drawn size sc heading h at (x, y): out[0] rise per run forward, out[1] to its
   *  left, over its own length (clamped to 45 degrees; a body is sheared to it, body.js writeMatrix). */
  _slope(x, y, h, sc, out) {
    const G = this.ground, cam = this.camera.position, d = 0.5 * sc, fx = Math.cos(h) * d, fz = Math.sin(h) * d;
    if (!G) { out[0] = out[1] = 0; return out; }
    const c = (v) => Math.max(-1, Math.min(1, v / (2 * d)));
    out[0] = c(G.heightAt(x + fx, y + fz, cam) - G.heightAt(x - fx, y - fz, cam));
    out[1] = c(G.heightAt(x - fz, y + fx, cam) - G.heightAt(x + fz, y - fx, cam));
    return out;
  }

  /** A flyer's height over the ground (in its own drawn lengths), bobbing. */
  _lift(sc, t, serial) { return sc * (7 + 0.8 * Math.sin(t * 3 + serial)); }

  /** An egg's drawn radius: a share of its kind's body as drawn. */
  _eggR(e) { return Math.max(0.12 * (e.size || 1.5) * this.scale.markK, 0.04 * this.scale.upm); }

  /** Drawn size of an organism (its own mass; juveniles smaller), in world units: the true size x the magnification. */
  _size(e) { const t = this._trueSize(e); return t * this.scale.dispK(t * MODEL_LEN * this.scale.mpu); }
  /** Its true drawn size (world units of the body model's view size, no multiplier): the size the shader reads its mass from. */
  _trueSize(e) { return (e.size || 1.5) * this._grown(e) * this.scale.bodyK; }

  /** Its length now against its adult length: from the server's true length (status.bodyLenM) when it has one; else a
   *  juvenile is 0.55 of it. The adult length is lengthK M^(1/3) times its length gene's share of the volume (ICD §2.1),
   *  as the body shader draws it. */
  _grown(e) {
    const L = e.serial !== undefined ? this.lenM.get(e.serial) : undefined;
    if (L === undefined || !e.mass) return e.stage === 1 ? 0.55 : 1;
    const a = e.appearance, ax = (i) => Math.pow(2, 2 * (a ? a[i] : 0.5) - 1);
    const adult = this.scale.lengthK * Math.cbrt(e.mass) * ax(0) / Math.cbrt(ax(0) * ax(1) * ax(2));
    return Math.min(1.25, Math.max(0.15, L / adult));
  }

  /** status.bodyLenM: [serial, metres, ...] for every living organism. */
  setBodyLengths(list) {
    const m = this.lenM; m.clear();
    for (let i = 0; i + 1 < list.length; i += 2) m.set(list[i], list[i + 1]);
  }

  /** status.bodyLook: [serial, endothermy, fibre, ...] (0..1 each) for the body shader; a body whose values change
   *  has its genes written again (e.gv, body.js placeBody). */
  setBodyLooks(list) {
    for (let i = 0; i + 2 < list.length; i += 3) {
      const e = this.agents.get(list[i]); if (!e) continue;
      // (fibre: its share of a budget spread over fifteen food pools; a third or more on fibrous food is a grazer)
      const fib = Math.min(1, Math.max(0, (list[i + 2] - 0.12) / 0.3));
      if (e.endo !== list[i + 1] || e.fibre !== fib) { e.endo = list[i + 1]; e.fibre = fib; e.gv = (e.gv | 0) + 1; }
    }
  }

  /** Body mass of an organism (juveniles about half). */
  _mass(e) { return (e.mass || 1) * (e.stage === 1 ? 0.5 : 1); }

  /** Mark the animals the selected one can see: kin blue, smaller green, larger red, eggs white. */
  _perceive(sel, selP, R, fov, alpha, cx, cz) {
    if (this.mode === 'overview') { this.percMesh.count = 0; return; }
    const myMass = this._mass(sel), P3 = [0, 0, 0];
    const m = new THREE.Matrix4(), q = new THREE.Quaternion(), p = new THREE.Vector3(), s = new THREE.Vector3(), col = new THREE.Color();
    let n = 0; const fx = Math.cos(selP[2]), fz = Math.sin(selP[2]);
    const list = [];
    for (const [ser, e] of this.agents) {
      if (ser === this.selected || n >= 128) continue;
      const [x, y] = this._pos(e, alpha, cx, cz, P3);
      const dx = x - selP[0], dy = y - selP[1], d = Math.hypot(dx, dy);
      if (d > R || d < 0.01) continue;
      if (fov < 6.2 && (dx * fx + dy * fz) / d < Math.cos(fov / 2)) continue;
      const mj = this._mass(e);
      let kind;
      if (e.stage === 0) { col.set(0xffffff); kind = 'egg'; }
      else if (e.sp === sel.sp) { col.set(0x5aa8ff); kind = 'kin'; }
      else if (mj * 1.15 < myMass) { col.set(0x5dff8a); kind = 'smaller'; }
      else if (mj > myMass * 1.15) { col.set(0xff4d5e); kind = 'larger'; }
      else { col.set(0xffd34d); kind = 'similar'; }
      const sz = Math.max(this._size(e) * 1.6, this._size(sel) * 0.5);
      p.set(x, this._height(e, x, y) + Math.min(0.25, sz * 0.1), y); q.identity(); s.set(sz, 1, sz);
      this.percMesh.setMatrixAt(n, m.compose(p, q, s)); this.percMesh.setColorAt(n, col); n++;
      list.push({ serial: ser, sp: e.sp, kind, d });
    }
    this.percMesh.count = n; this.percMesh.instanceMatrix.needsUpdate = true; if (this.percMesh.instanceColor) this.percMesh.instanceColor.needsUpdate = true;
    this.perceived = list;
  }
}

// ------------------------------------------------------------------ procedural geometry

function lerpAngle(a, b, t) { let d = b - a; while (d > Math.PI) d -= 2 * Math.PI; while (d < -Math.PI) d += 2 * Math.PI; return a + d * t; }

/** A cell's elevation, 0..1: from the 16-bit plane when the WORLD frame has it, else the 8-bit one. */
function elevOf(w, i) { return w.elev16 ? w.elev16[i] / 65535 : w.elev[i] / 255; }

/** A cell's water depth in world units (true scale: 1 unit = 54.5 m both ways). From metres when the WORLD frame gives
 *  them (w.depthM, a per-cell array in m; or w.depthUnit === 'm' with w.depth in m), else from the ICD depth byte
 *  (normalised elevation x 2000) through U (units per normalised elevation). */
function waterDepthU(w, i, U) {
  if (w.depthM) return w.depthM[i] * RELIEF / 8000;
  if (w.depthUnit === 'm') return w.depth[i] * RELIEF / 8000;
  return (w.depth[i] / 2000) * U;
}

function smooth(a, b, x) { const t = Math.min(1, Math.max(0, (x - a) / (b - a))); return t * t * (3 - 2 * t); }

/** Separate quads (4 corners each, in the order (x,y) (x+1,y) (x,y+1) (x+1,y+1)) facing up, with an RGBA or RGB colour. */
function quadGeometry(pos, colorSize) {
  const nq = pos.length / 12, idx = new Uint32Array(nq * 6), nor = new Float32Array(nq * 12);
  for (let q = 0; q < nq; q++) { const a = q * 4; idx.set([a, a + 2, a + 1, a + 1, a + 2, a + 3], q * 6); }
  for (let v = 0; v < nq * 4; v++) nor[v * 3 + 1] = 1;
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.BufferAttribute(new Float32Array(pos), 3));
  g.setAttribute('normal', new THREE.BufferAttribute(nor, 3));
  g.setAttribute('color', new THREE.BufferAttribute(new Float32Array(nq * 4 * colorSize), colorSize));
  g.setIndex(new THREE.BufferAttribute(idx, 1));
  return g;
}

/**
 * The wrap display, added to a material's shader: past half a map from the view centre (a square, as the map is) the
 * colour is dimmed and greyed by U.uWrapS (0 close up, 1 zoomed out), a thin pale line of width uWrapLine marks the
 * seam, and from 0.8 to 0.98 of a map away everything fades into the background, so the tiling's outer edge (where the
 * ground would end) never shows. U's uniform objects are shared, so setting them once a frame updates every material.
 */
function wrapPatch(mat, U, key) {
  // it runs after any shader additions the material already has (the terrain's), and names the result for three's
  // program cache (two materials with different additions must never share a program)
  const prev = mat.onBeforeCompile;
  if (key) mat.customProgramCacheKey = () => key + '+wrap';
  mat.onBeforeCompile = (sh, r) => {
    prev.call(mat, sh, r);
    Object.assign(sh.uniforms, U);
    sh.vertexShader = 'varying vec2 vWrapXZ;\n' + sh.vertexShader.replace('#include <project_vertex>', `#include <project_vertex>
      {
        vec4 wq = vec4(transformed, 1.0);
        #ifdef USE_INSTANCING
          wq = instanceMatrix * wq;
        #endif
        vWrapXZ = (modelMatrix * wq).xz;
      }`);
    sh.fragmentShader = 'uniform vec2 uWrapC;\nuniform float uWrapHalf, uWrapS, uWrapLine;\nuniform vec3 uWrapBg;\nvarying vec2 vWrapXZ;\n' +
      sh.fragmentShader.replace('#include <dithering_fragment>', `#include <dithering_fragment>
      {
        vec2 q = abs(vWrapXZ - uWrapC);
        float d = max(q.x, q.y), H = uWrapHalf;
        vec3 c = gl_FragColor.rgb;
        float ghost = step(H, d) * uWrapS;
        float l = dot(c, vec3(0.3, 0.59, 0.11));
        c = mix(c, mix(vec3(l), uWrapBg, 0.35) * 0.8, 0.6 * ghost);
        float seam = uWrapS * (1.0 - smoothstep(0.0, uWrapLine, abs(d - H)));
        c = mix(c, vec3(0.80, 0.90, 1.0), 0.6 * seam);
        c = mix(c, uWrapBg, smoothstep(1.6 * H, 1.96 * H, d));
        gl_FragColor.rgb = c;
      }`);
  };
}

/** Water depth of cell i, m, from the WORLD frame's depth plane (2026-11-01: round(16 sqrt(m)); ICD §WORLD). */
function depthM(w, i) { const b = w.depth[i] / 16; return b * b; }

/** The river plane for a server that does not send it: open water above lake level drains to its lowest wet
 *  neighbour (else its lowest), sized from its depth (2026-11-01: m = RIVER_D x Q^0.4, Q in m3/s). */
function deriveRivers(w) {
  const fw = w.fw, n = fw * fw, out = new Uint8Array(n), seaB = Math.ceil(w.sea * 255);
  for (let i = 0; i < n; i++) {
    if (w.cell[i] === 1 || w.elev[i] < seaB) continue;
    const x = i % fw, y = (i / fw) | 0;
    let best = -1, be = 1e9, any = -1, ae = 1e9;
    for (let k = 0; k < 8; k++) {
      const j = ((y + DIRY[k] + fw) % fw) * fw + ((x + DIRX[k] + fw) % fw), e = w.elev[j];
      if (w.cell[j] !== 1 && e <= w.elev[i] && e < be) { be = e; best = k; }
      if (e < ae) { ae = e; any = k; }
    }
    const ratio = Math.pow(Math.max(1e-3, depthM(w, i) / 0.3), 2.5);   // RIVER_D 0.3 m at 1 m3/s, depth ~ Q^0.4
    const size = Math.min(31, Math.max(1, 1 + Math.round(4 * Math.log2(Math.max(1, ratio)))));
    out[i] = (size << 3) | (best >= 0 ? best : any);
  }
  return out;
}
