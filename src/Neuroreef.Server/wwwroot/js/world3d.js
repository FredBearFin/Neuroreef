// The 3D view: terrain, water, vegetation, fire, and every organism drawn from its own genes (creatures/body.js):
// one instanced mesh of detailed, animated bodies near the camera and one of low-detail bodies beyond it.
// World x maps to three.js x, world y maps to three.js z, height is three.js y. The world is a torus:
// the terrain is drawn as a 3x3 tiling around the camera, and every animal is drawn at its copy nearest
// to the view centre; in the overview the copies more than half a map from the view centre are dimmed and a
// thin line marks where the map wraps (wrapPatch), so one map is shown, never a grid of repeats.

import * as THREE from 'three';
import { OrbitControls } from '../lib/OrbitControls.js';
import { agentState, agentGenes, REC } from './net.js';
import { Look } from './look/look.js';
import { bodyGeometry, bodyMaterial, bodyMesh, beginBodies, placeBody, finishBodies, bodySize, appearanceColor } from './creatures/body.js';
import { Ground, CARVE_NONE } from './terrain/ground.js';
import { fromWire, fromCells, stampRivers, ribbonGeometry, patchRiverMaterial, riverWidth, preparePath } from './terrain/rivers.js';
import { decodeFeatures, stampFeatures, featureMeshes } from './terrain/caves.js';
import { patchLakeMaterial } from './terrain/water.js';
import { splitChunks, copyColors } from './terrain/chunks.js';

// Vertical scale (2026-11-01). Elevation is 0..1 with the lakes at w.sea; Climate reads (e - sea) / (1 - sea) x H_RELIEF
// (8000 m since the high, jagged mountains of 2026-11-01; 3000 m before) as the height above the lakes. Geology measures
// slopes on a field cell of GEO_CELL_M (218 m), so the land is drawn at true scale: a cell (4 units) is 218 m, and the
// 8000 m are RELIEF = 8000 x 4 / 218 = 146.8 units (no exaggeration). The 1024 map's land reaches 0.4-0.65 of it (3-5 km),
// the 2048 map's ~7.5 km; crags stand at up to 45-55 degrees by rock kind, as Geology made them. (55 units drew the 3000 m
// before; elevation comes as 16 bits when the WORLD frame has the plane, as the 8 bits' 31-m steps would terrace them.)
const RELIEF = 8000 * 4 / 218;   // 146.8 units
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
const NEAR_DIST = 90;        // organisms closer than this to the camera are drawn with the detailed body
const NEAR_CAP = 512;        // at most this many detailed bodies; the rest get the low-detail one (2026-10-31: 4096 cost the GPU
                             // a detailed body for every animal in a crowd, where a few hundred fill the near view)
const FAR_BUDGET = 16000;    // at most about this many low-detail bodies per frame (a sample of the far ones when there are more)
const DIRX = [1, 1, 0, -1, -1, -1, 0, 1], DIRY = [0, 1, 1, 1, 0, -1, -1, -1];   // the WORLD frame's river directions
const NCLADES = 32;

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
    this.camera = new THREE.PerspectiveCamera(55, 1, 0.3, Math.max(4000, this.W * 3));
    // the wrap display (wrapPatch): view centre, half a map, strength (0 close up .. 1 zoomed out), seam width, background
    this.wrapU = { uWrapC: { value: new THREE.Vector2() }, uWrapHalf: { value: this.W / 2 }, uWrapS: { value: 0 }, uWrapLine: { value: 1 },
                   uWrapBg: { value: new THREE.Color(0x0b1622) } };
    this._wrapS = 0;
    this.camera.position.set(this.W / 2 - 260, 260, this.W / 2 + 260);
    this.controls = new OrbitControls(this.camera, canvas);
    this.controls.target.set(this.W / 2, 0, this.W / 2);
    this.controls.enableDamping = true;
    this.controls.maxPolarAngle = Math.PI * 0.47;
    this.controls.minDistance = 8; this.controls.maxDistance = Math.max(400, this.W * 1.5);

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
      depthOf: (i) => Math.min(1.2, Math.max(0.15, (w.depth[i] / 2000) * U)), flowOf: (i) => Math.min(1, (riv[i] >> 3) / 31),
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
  }

  _dropTerrain() {
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
          snow = L('snow'), volc = L('volcanic'), sedi = L('sedimentary'), lime = L('limestone'), allu = L('alluvium');
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
        r += (0.10 - r) * T * 0.85; g += (0.26 - g) * T * 0.85; b += (0.12 - b) * T * 0.85;
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
      M2[i * 4] = lime ? lime[fi] : 0;
    }
    this.ground.coverUpdated(); this.ground.matUpdated();
    this.ground.uniforms.uPlain.value = this.tempOverlay ? 1 : 0;
    const fieldOf = (i) => f ? Math.min(d - 1, ((i / fw) | 0) >> 1) * d + Math.min(d - 1, (i % fw) >> 1) : 0;
    // lakes: shallow water turquoise, deep water dark blue (depth below the water's level at each corner); algae and plankton tint it green
    const LC = this.lakeColor, LV = this.lakeVC, LL = this.lakeVL;
    for (let v = 0; v < LV.length; v++) {
      const i = LV[v], fi = fieldOf(i), dd = Math.min(1, Math.max(0, LL[v] - this.cellH[i]) / 2.0);
      const al = f ? algae[fi] / 255 : 0, ph = f ? phyto[fi] / 255 : 0;
      LC[v * 4] = 0.10 - 0.07 * dd + 0.06 * al; LC[v * 4 + 1] = 0.40 - 0.24 * dd + 0.18 * al + 0.10 * ph; LC[v * 4 + 2] = 0.48 - 0.18 * dd + 0.02 * ph;
      LC[v * 4 + 3] = 0.62 + 0.33 * dd;
      if (frozen && frozen[fi] && this.isLake[i]) { LC[v * 4] = 0.80; LC[v * 4 + 1] = 0.88; LC[v * 4 + 2] = 0.94; LC[v * 4 + 3] = 0.95; }   // pale ice (Revision O)
    }
    // rivers: clear and light where small or shallow, deep green-blue where big and deep; algae tint them green
    const RC = this.rivColor, RV = this.rivVC, U = RELIEF / (1 - sea);
    for (let v = 0; v < RV.length; v++) {
      const i = RV[v], fi = fieldOf(i), al = f ? algae[fi] / 255 : 0;
      const dd = Math.min(1, Math.max((w.depth[i] / 2000) * U / 0.6, (this.riv[i] >> 3) / 24));
      RC[v * 3] = 0.24 - 0.15 * dd + 0.05 * al; RC[v * 3 + 1] = 0.52 - 0.20 * dd + 0.12 * al; RC[v * 3 + 2] = 0.58 - 0.12 * dd;
      if (frozen && frozen[fi]) { RC[v * 3] = 0.82; RC[v * 3 + 1] = 0.89; RC[v * 3 + 2] = 0.95; }
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
    this.frameDt = Math.min(250, Math.max(16, now - this.frameT)); this.frameT = now;
    const gen = (this._gen = (this._gen || 0) + 1), W = this.W, o = this.tmp;
    for (let k = 0; k < a.n; k++) {
      agentState(a, k, W, o);
      let e = this.agents.get(o.serial);
      if (!e) { e = { x: o.x, y: o.y, h: o.h, px: o.x, py: o.y, ph: o.h }; this.agents.set(o.serial, e); }
      else { e.px = e.x; e.py = e.y; e.ph = e.h; e.x = o.x; e.y = o.y; e.h = o.h; }
      e.sp = o.sp; e.stage = o.stage; e.acts = o.acts; e.scan = o.scan; e.rest = o.rest; e.energy = o.energy; e.gen = gen; e.carry = o.carrying;
      // the body its genes built is the same for its life: decoded the first time its serial is seen (and again only if
      // the first gene bytes differ, as when another world, whose serials start again at 1, is shown)
      const fp = a.dv.getUint32(k * REC + 12, true);
      if (e.fp !== fp) {
        e.fp = fp;
        agentGenes(a, k, o);
        e.gMass = o.gMass; e.m01 = o.m01; e.mass = o.mass; e.size = bodySize(o.mass);
        e.legs = o.legs; e.fins = o.fins; e.wings = o.wings; e.loco = o.loco; e.legPairs = o.legPairs; e.finPairs = o.finPairs; e.eyePairs = o.eyePairs;
        e.fly = o.loco === 'Fly'; e.swim = o.loco === 'Swim';
        e.sight = o.sight; e.armour = o.armour; e.strike = o.strike; e.meat = o.meat;
        e.fur = o.fur; e.append = o.append;
        (e.appearance ||= new Float32Array(16)).set(o.appearance);
      }
      e.call = o.call; e.call2 = o.call2; e.call3 = o.call3; if (o.call) e.callT = now;
    }
    for (const [s, e] of this.agents) if (e.gen !== gen) this.agents.delete(s);
    this.count = a.n;
    if (!this._framed && a.n > 0) { this._framed = true; this.closeUp(); }
  }

  /** Put the camera close over the biggest gathering of animals, near enough to watch them (the start view). */
  closeUp() {
    const C = 24, W = this.W, n = Math.max(1, Math.floor(W / C)), cnt = new Map();
    let best = -1, bk = 0;
    for (const e of this.agents.values()) {
      const k = Math.floor(e.y / C) % n * n + Math.floor(e.x / C) % n, c = (cnt.get(k) || 0) + 1;
      cnt.set(k, c); if (c > best) { best = c; bk = k; }
    }
    if (best < 0) return;
    let sx = 0, sy = 0, m = 0;
    for (const e of this.agents.values()) if (Math.floor(e.y / C) % n * n + Math.floor(e.x / C) % n === bk) { sx += e.x; sy += e.y; m++; }
    const x = sx / m, y = sy / m, g = this.world ? this.heightAt(x, y) : 0;
    this.controls.target.set(x, g, y);
    this.camera.position.set(x - 20, g + 14, y + 20);
    this.controls.update();
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

  _spray(x, y, z, n, speed, dx, dy) {
    for (let i = 0; i < n && this.blood.length < BLOOD_MAX; i++) {
      const a = Math.random() * Math.PI * 2, v = speed * (0.4 + Math.random());
      // most of it flies away from the blow
      this.blood.push({ x, y, z, vx: Math.cos(a) * v * 0.6 + dx * v, vy: Math.sin(a) * v * 0.6 + dy * v, vz: 2.5 + Math.random() * speed, s: 0.6 + Math.random() * 1.2 });
    }
  }

  /** Fights from the simulation: [x, y, attacker clade, victim clade, kind, step]; kinds as Telemetry.F_*. */
  addFights(list) {
    const now = performance.now();
    const c = this.controls.target, W = this.W;
    for (const f of list) {
      const [x, y, , , kind, , sa, sv] = f;
      if (kind === 0 && this.fx.length > FX_MAX / 2) continue;   // misses are the first to go when it is busy
      this.fx.push({ x, y, t0: now, kind });
      if (kind === 2 || kind === 4 || kind === 5) this.stains.push({ x, y, t0: now, r: kind === 5 ? 0.5 : 1.1 });
      // the two animals: the attacker lunges, the victim is thrown back and bleeds
      const A = sa ? this.agents.get(sa) : null, V = sv ? this.agents.get(sv) : null;
      let ux = 0, uy = 0;
      if (A && V) {
        ux = V.x - A.x; uy = V.y - A.y; ux -= Math.round(ux / W) * W; uy -= Math.round(uy / W) * W;
        const d = Math.hypot(ux, uy) || 1; ux /= d; uy /= d;
        A.lungeT = now; A.lx = ux; A.ly = uy; A.lungeD = Math.min(d * 0.8, 0.5 + 0.6 * (A.size || 1));
      }
      if (V && kind !== 0) { V.hitT = now; V.hx = ux; V.hy = uy; V.hitD = (kind === 2 ? 0.9 : 0.5) * (V.size || 1); }
      if (kind === 0) continue;
      const vs = V ? V.size || 1 : 1, gx = x + W * Math.round((c.x - x) / W), gy = y + W * Math.round((c.z - y) / W);
      const z = this.heightAt(gx, gy) + vs * 0.6;
      const N = [0, 14, 60, 10, 40, 8][kind] * Math.min(2.5, Math.max(0.6, vs));
      if (kind !== 5) this._spray(gx, gy, z, Math.round(N), 3 + 2 * vs, ux, uy);
      // the dead stay where they fell
      if ((kind === 2 || kind === 4) && V && !this.dead.has(sv)) {
        this.dead.add(sv);
        this.corpses.push({ e: { ...V }, x: V.x, y: V.y, h: V.h + (Math.random() - 0.5), t0: now, serial: sv });
        if (this.corpses.length > 120) { const old = this.corpses.shift(); this.dead.delete(old.serial); }
        const dc = Math.hypot(gx - c.x, gy - c.z);
        if (this.mode === 'overview' && dc < 45) this.shake = Math.max(this.shake, 0.35 * (1 - dc / 45) * Math.min(2, vs));
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
    const off = this.camera.position.clone().sub(c), d = off.length(), lim = 40;
    if (d > lim) off.multiplyScalar(lim / d);
    const g = this.heightAt(x, y);
    c.set(x, g, y); this.camera.position.set(x + off.x, g + Math.max(8, off.y), y + off.z);
  }

  _drawBlood(now) {
    const dt = Math.min(0.05, (now - this._lastFx) / 1000); this._lastFx = now;
    const m = new THREE.Matrix4(), keep = [];
    let k = 0;
    for (const b of this.blood) {
      b.vz -= 18 * dt; b.x += b.vx * dt; b.y += b.vy * dt; b.z += b.vz * dt;
      const g = this.heightAt(b.x, b.y);
      if (b.z <= g) {   // it lands: a spatter on the ground
        if (Math.random() < 0.25) this.stains.push({ x: b.x, y: b.y, t0: now, r: 0.12 + 0.18 * Math.random() });
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
      const a = (now - e.t0) / LIFE[e.kind], x = near(e.x, cx), y = near(e.y, cz), s = SIZE[e.kind] * (0.4 + 1.6 * a);
      m.makeScale(s, 1, s); m.setPosition(x, this.heightAt(x, y) + 0.4, y);
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
      m.makeScale(s, 1, s); m.setPosition(x, this.heightAt(x, y) + 0.08, y);
      this.stainMesh.setMatrixAt(k++, m);
    }
    this.stainMesh.count = k; this.stainMesh.instanceMatrix.needsUpdate = true;
  }

  setMode(m) {
    this.mode = m;
    this.controls.enabled = m === 'overview';
    this.camera.fov = m === 'ride' ? 78 : 55; if (m !== 'overview') this.camera.near = 0.3; this.camera.updateProjectionMatrix();
    this.scene.fog.near = m === 'ride' ? 60 : 250; this.scene.fog.far = m === 'ride' ? 380 : 900;
    if (m === 'overview' && this._selPos) this.controls.target.set(this._selPos[0], 0, this._selPos[1]);
    this._plants(true);
  }

  /** Where a screen point meets the ground, as map coordinates, or null. */
  groundAt(clientX, clientY) {
    const r = this.canvas.getBoundingClientRect();
    const ndc = new THREE.Vector2(((clientX - r.left) / r.width) * 2 - 1, -((clientY - r.top) / r.height) * 2 + 1);
    const ray = new THREE.Raycaster(); ray.setFromCamera(ndc, this.camera);
    const o = ray.ray.origin, d = ray.ray.direction, W = this.W;
    let t = 0;
    for (let i = 0; i < 600; i++) {
      const p = o.clone().addScaledVector(d, t);
      if (p.y <= this.heightAt(p.x, p.z) + 0.2) return [((p.x % W) + W) % W, ((p.z % W) + W) % W];
      t += Math.max(0.5, (p.y - this.heightAt(p.x, p.z)) * 0.5);
    }
    return null;
  }

  /** Show the person's call where it was made (sounds: 1..8, up to 3). */
  showMyCall(x, y, sounds) { this.myCall = { x, y, sounds: Array.isArray(sounds) ? sounds : [sounds], t0: performance.now() }; }

  _drawCalls(now, cx, cz) {
    const W = this.W, m = new THREE.Matrix4(), col = new THREE.Color(), near = (v, c) => v + W * Math.round((c - v) / W);
    let k = 0;
    // an utterance is a stack of rings, one per sound in its order: the first lowest and innermost
    const ring = (x, y, s, sound, a, p = 0) => {
      if (k >= CALL_MAX) return;
      m.makeScale(s, 1, s); m.setPosition(x, this.heightAt(x, y) + 0.5 + 0.45 * p, y);
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
      const q = ((now - e.callT) / 600), sz = (e.size || 1);
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

    if (this.mode !== 'overview' && selP) {
      const sz = this._size(sel);
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
      // zoomed far out the camera looks down more steeply: it stays within 0.7 of a map of the view centre, over the
      // map's own copy, so the tiling's faded outer edge is never in the foreground
      {
        const dd = this.camera.position.distanceTo(c);
        this.controls.maxPolarAngle = Math.min(Math.PI * 0.47, Math.asin(Math.min(1, 0.7 * W / Math.max(1, dd))));
      }
      this.controls.update();
      // fog and the terrain tiles follow the zoom, so zooming out never blanks the ground
      const camD = this.camera.position.distanceTo(c);
      this.scene.fog.near = Math.max(250, camD * 0.7); this.scene.fog.far = Math.max(900, camD * 2.4);
      // the near plane follows the zoom too, for depth precision where the shores lie a fraction of a unit above the lakes
      const near = Math.min(4, Math.max(0.3, camD * 0.004));
      if (Math.abs(near - this.camera.near) > 0.05) { this.camera.near = near; this.camera.updateProjectionMatrix(); }
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
    const near = this.near, far = this.far, rgb = this.cladeRGB, P3 = [0, 0, 0];
    const camX = this.camera.position.x, camZ = this.camera.position.z, near2 = NEAR_DIST * NEAR_DIST;
    this.bodyMat.userData.uniforms.uTime.value = now / 1000;
    this.bodyMat.userData.uniforms.uSpeciesOverlay.value = this.speciesOverlay ? 1 : 0;
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
        if (qx * qx + qz * qz >= near2) continue;
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
        const ey = this.heightAt(x, y) + 0.3;
        if (!inView(x, ey, y, 1)) continue;
        p.set(x, ey, y); q.identity(); s.set(1, 0.8, 1);
        this.eggs.setMatrixAt(ne, m.compose(p, q, s));
        if (this.speciesOverlay) eggCol.setRGB(rgb[ci], rgb[ci + 1], rgb[ci + 2]);
        else appearanceColor(e, eggCol);
        this.eggs.setColorAt(ne, eggCol.lerp(white, 0.6));
        ne++; continue;
      }
      const size = e.size, sc = size * (e.stage === 1 ? 0.55 : 1);
      let gy = this._height(e, x, y);
      if (e.fly && !e.rest) gy += 3.5 + Math.sin(t * 3 + serial) * 0.6;
      // gait from the distance moved since the last frame
      let mdx = e.x - e.px, mdy = e.y - e.py;
      if (mdx > Wh) mdx -= W; else if (mdx < -Wh) mdx += W;
      if (mdy > Wh) mdy -= W; else if (mdy < -Wh) mdy += W;
      const gait = Math.min(1, Math.hypot(mdx, mdy) / (0.25 * size + 0.05));
      e.gait = e.gait === undefined ? gait : e.gait + (gait - e.gait) * 0.15;
      if (!inView(x, gy, y, 3 * sc + 2)) continue;   // a body (wings and tail spread) lies well within 3 x its size
      const ddx = x - camX, ddz = y - camZ;
      const sy = sc * (e.rest && !e.fly ? 0.7 : 1), ph = (serial % 997) / 997, rs = e.rest ? 1 : 0;
      // each organism keeps its instance slot (keyed by serial) while it stays in a mesh: only its matrix and animation
      // are sent each frame. A near one goes to the far mesh when the near one is full
      if (ddx * ddx + ddz * ddz < near2 && placeBody(near, serial, x, gy, y, h, sc, sy, e, e.gait, ph, acts, rs, cr, cg, cb)) nn++;
      else if (placeBody(far, serial, x, gy, y, h, sc, sy, e, e.gait, ph, acts, rs, cr, cg, cb)) nf++;
    }
    // the killed: flattened, darkened bodies lying where they fell, sinking into the ground at the end
    this.corpses = this.corpses.filter((d) => { if (now - d.t0 < CORPSE_LIFE) return true; this.dead.delete(d.serial); return false; });
    for (const d of this.corpses) {
      const e = d.e, x = d.x + W * Math.round((cx - d.x) / W), y = d.y + W * Math.round((cz - d.y) / W);
      const a = (now - d.t0) / CORPSE_LIFE, sc = (e.size || 1) * (e.stage === 1 ? 0.55 : 1);
      const gy = this.heightAt(x, y, e.swim) - sc * 0.15 - Math.max(0, a - 0.75) * 4 * sc * 0.6;
      if (!inView(x, gy, y, 3 * sc + 2)) continue;
      const ddx = x - camX, ddz = y - camZ;
      const ci = e.sp * 3, dk = 0.45 - 0.2 * a, r = rgb[ci] * dk + 0.18, g = rgb[ci + 1] * dk * 0.5, b = rgb[ci + 2] * dk * 0.5;
      const key = -d.serial;   // a corpse's slot is apart from the living animal's (serials are > 0)
      if (ddx * ddx + ddz * ddz < near2 && placeBody(near, key, x, gy, y, d.h, sc * 1.15, sc * 0.38, e, 0, 0, 0, 1, r, g, b)) nn++;
      else if (placeBody(far, key, x, gy, y, d.h, sc * 1.15, sc * 0.38, e, 0, 0, 0, 1, r, g, b)) nf++;
    }
    finishBodies(near); finishBodies(far);
    this.eggs.count = ne; this.eggs.instanceMatrix.needsUpdate = true; if (this.eggs.instanceColor) this.eggs.instanceColor.needsUpdate = true;

    // selection marker, field of view and perceived animals
    if (sel && selP) {
      const sz = this._size(sel);
      const gh = this._height(sel, selP[0], selP[1]);
      this.selRing.visible = this.mode !== 'ride';
      this.selRing.position.set(selP[0], gh + 0.2, selP[1]); this.selRing.scale.setScalar(sz * 1.4 + 0.8 + 0.2 * Math.sin(now / 200));
      this.selBeam.visible = this.mode === 'overview'; this.selBeam.position.set(selP[0], gh, selP[1]);
      const cl = (this.clades && this.clades[sel.sp]) || {};
      const R = probe && probe.serial === this.selected ? probe.sight : (cl.sight || 20);
      const fov = (sel.scan ? 360 : (cl.fov || 300)) * Math.PI / 180;
      this.fov.visible = this.mode !== 'overview';
      if (this._fovKey !== R + ':' + fov) {
        this._fovKey = R + ':' + fov;
        this.fov.geometry.dispose();
        this.fov.geometry = new THREE.CircleGeometry(R, 48, -fov / 2, fov); this.fov.geometry.rotateX(-Math.PI / 2);
      }
      this.fov.position.set(selP[0], gh + 0.35, selP[1]); this.fov.rotation.set(0, -selP[2], 0);
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

  _height(e, x, y) { return this.heightAt(x, y, e.swim); }

  /** View size of an organism (its own mass; juveniles smaller). */
  _size(e) { return (e.size || 1.5) * (e.stage === 1 ? 0.55 : 1); }

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
      const sz = this._size(e) * 1.3 + 0.6;
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

/** A cell's elevation, 0..1: from the 16-bit plane when the WORLD frame has it, else the 8-bit one. */
function elevOf(w, i) { return w.elev16 ? w.elev16[i] / 65535 : w.elev[i] / 255; }

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

/** The river plane for a server that does not send it: open water above lake level drains to its lowest wet
 *  neighbour (else its lowest), sized from its depth (depth = SHALLOW x RIVER_D x (flow / threshold)^0.4). */
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
    const ratio = Math.pow(Math.max(1e-3, (w.depth[i] / 2000) / (0.035 * 0.5)), 2.5);
    const size = Math.min(31, Math.max(1, 1 + Math.round(4 * Math.log2(Math.max(1, ratio)))));
    out[i] = (size << 3) | (best >= 0 ? best : any);
  }
  return out;
}
