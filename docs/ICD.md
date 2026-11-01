# NEUROREEF 2 — INTERFACE CONTROL DOCUMENT

Document NR-ICD-001, Revision H (AGENTS format 7 adds depth in the water, time at depth and breathing; format 6 added inherited appearance genes). This is the link between the simulation server and the browser
console. Encoders: `src/Neuroreef.Core/Snapshot.cs` (AGENTS) and `src/Neuroreef.Server/Wire.cs`
(all others). Decoder: `src/Neuroreef.Server/wwwroot/js/net.js`. Test: `tests/Neuroreef.Tests/WireTests.cs`.

## 0. Endpoints

| Endpoint | Content |
|---|---|
| `GET /` and `GET /<file>` | the embedded console (HTML, JS, CSS, vendored three.js r160 and uPlot 1.6) |
| `GET /api/meta` | the same metadata JSON that is sent first on the WebSocket |
| `WS /ws` | the live link: JSON text frames and binary frames |

## 1. Binary frames (server → client)

All binary frames are **little-endian** and start with an 8-byte header:

| Offset | Type | Field |
|---|---|---|
| 0 | u8 | frame type |
| 1 | u8 | format version (= 1) |
| 2 | u16 | reserved (0) |
| 4 | u32 | simulation step |

### 0x01 WORLD (once, after the metadata)

| Offset | Type | Field |
|---|---|---|
| 8 | u16 | FW (field grid side, 256) |
| 10 | u16 | reserved |
| 12 | f32 | lake level (normalised elevation) |
| 16 | u8[FW²] | elevation, 0..255 = normalised 0..1 |
| 16 + FW² | u8[FW²] | realm bits per cell: 1 land, 2 shallow water, 4 deep water |
| 16 + 2·FW² | u8[FW²] | water depth × 2000, clamped to 255 |
| 16 + 3·FW² | u8[FW²] | rivers (2026-10-31, display only): 0 = not a river (land, the sea or a lake); for a river cell (open water outside the sea and the lakes) bits 0-2 = the neighbour it drains to (0 +x, 1 +x+y, 2 +y, 3 −x+y, 4 −x, 5 −x−y, 6 −y, 7 +x−y; y is the row) and bits 3-7 = size class 1 + round(4·log2(Flow / river threshold)), 1..31 (quarter octaves of the rain it gathers over the catchment at which a cell becomes river, RIVER_ACC). A client that predates it ignores the trailing plane |
| 16 + 4·FW² | u16[FW²] | standing water surface (2026-10-31, display only): for a cell of the sea or a lake, its water level (bed + depth) as normalised elevation × 65535, at least 1; 0 for land and rivers. Lakes in upland basins stand at their own levels, above or below the sea level. A client that predates it ignores the trailing plane and draws all standing water at the sea level |
| 16 + 6·FW² | u8[FW²] | cave features (2026-11-01, Caves.cs; World.CaveKind), bits: 1 karst passage under the cell, 2 lava tube under the cell, 4 sinkhole (a doline or ponor that swallows surface water), 8 cave entrance (a resurgence, a collapse doline, a valley-wall opening or a lava-tube skylight; an animal there shelters in cave air), 16 karst spring (where a conduit discharges), 32 losing river (a river cell over karst that leaks into the conduits), 64 ponor (the sinkhole takes a stream from other rock). Bits combine (a collapse doline is 1+4+8). A client that predates it ignores the trailing planes |
| 16 + 7·FW² | u8[FW²] | cave courses (2026-11-01, display only): 0 = none; for a cell with bit 1 or 2 above, bits 0-2 = the neighbour its conduit or tube runs to (the river direction codes; 0 also where it ends: a spring or a tube's lower end) and bits 3-7 = size class 1 + round(4·log2(conduit discharge / CAVE_Q_MIN)), 1..31 (quarter octaves; 1 for a lava tube). Following the directions from any cell with bit 1 stays on passage cells and leads to its spring |
| 16 + 8·FW² | u16[FW²] | elevation × 65535 (2026-11-01): the same elevation as the u8 plane at offset 16, finer (the u8 plane's 1/255 steps are ~35 m at H_RELIEF 8000 m, and terrace high mountains); the u8 plane stays, for clients that predate this one |
| 16 + 10·FW² | RIVERS | the rivers as lines (2026-11-01, display only; below). A client that predates it ignores the trailing section |

**RIVERS section** (`Wire.Rivers`, built by `Hydrology.Lines`). Rivers are no longer chains of cell centres: each runs along a
smooth line through the plane, meandering on low gradients, and its channel cells are the cells that line crosses. Draw a
river as a curve through its points (a Catmull-Rom spline through them is smooth; straight segments are close enough at one
point per cell) at the width each point gives, rather than from the river plane's per-cell directions.

| Offset (from the section) | Type | Field |
|---|---|---|
| 0 | u32 | river count R |
| 4 | u32 | point count V (all rivers) |
| 8 | 16 B × R | river records, largest river (most discharge where it ends) first |
| 8 + 16·R | 16 B × V | points, each river's in order from its head downstream |

River record:

| Offset | Type | Field |
|---|---|---|
| 0 | u32 | index of its first point |
| 4 | u32 | number of points (at least 2) |
| 8 | i32 | the river it joins (an index into the records, always smaller than its own), or −1 if it ends in a lake or the sea |
| 12 | u8 | Strahler order where it ends |
| 13 | u8 | how it ends: 0 joins a river, 1 a lake, 2 the sea |
| 14 | u16 | reserved (0) |

Point:

| Offset | Type | Field |
|---|---|---|
| 0 | f32 | x in field cells: cell (i, j) spans [i, i+1) × [j, j+1), so its centre is (i + 0.5, j + 0.5) |
| 4 | f32 | y in field cells (y is the row) |
| 8 | f32 | the river's width there, in cells (1..RIVER_W_MAX; it never narrows downstream) |
| 12 | f32 | its depth there, in normalised elevation (as World.Depth; the depth plane is this × 2000) |

A river's points are the centreline points of its channel cells, one per cell, and a last point where it ends: on the river
it joins (that river's point in the cell it flows into, so the two lines meet) or the centre of the lake or sea cell it
flows into. Points are unwrapped along a river (the map is a torus): consecutive points are less than 2 cells apart, and
x and y may fall outside [0, FW); take them modulo FW to place them. The river plane above still marks every river cell
(channel and banks) with the neighbour it drains to, which is now the next cell along the line.

### 0x02 AGENTS (~20 Hz; `view.hz` sets the rate per client)

| Offset | Type | Field |
|---|---|---|
| 8 | u32 | count n |
| 12 | 46 B × n | records (every living organism except embryos); header byte 1 = format version 7 |

Each record:

| Offset | Type | Field |
|---|---|---|
| 0 | u16 | x · 65536 / WORLD |
| 2 | u16 | y · 65536 / WORLD |
| 4 | u8 | heading · 256 / 2π |
| 5 | u8 | bits 0–4 clade slot (0..31); bit 5 sex |
| 6 | u8 | bits 0–1 stage (0 egg, 1 juvenile, 2 adult); bit 2 carrying; bit 3 scanning; bit 4 resting; bits 5–7 energy/Emax × 7 |
| 7 | u8 | actions fired this step: bit 0 EAT, 1 STRIKE, 2 DRINK, 3 MATE, 4 REST, 5 MARK, 6 LAY, 7 CACHE |
| 8 | u32 | serial (unique for the run) |
| 12 | i8 | body gene: mass (value = MASS_LO·(MASS_HI/MASS_LO)^((g+128)/255)) |
| 13 | i8 | body gene: legs (organ size (g+127)/254) |
| 14 | i8 | body gene: fins |
| 15 | i8 | body gene: wings |
| 16 | i8 | body gene: sight |
| 17 | i8 | body gene: armour |
| 18 | i8 | body gene: strike |
| 19 | i8 | meat-eating efficiency × 254 − 127 |
| 20 | u8 | leg pairs (1..4) |
| 21 | u8 | bits 0–3 locomotion label (Loco: walk, wade, swim, fly, settled), for drawing only; bits 4–7 the first sound of its utterance this step (0 none, 1..8) |
| 22 | u8 | fin pairs (1..4) |
| 23 | u8 | eye pairs (1..4) |
| 24 | u8 | bits 0–3 the second sound of its utterance, bits 4–7 the third (0 none; Revision N) |
| 25 | u8 | bits 4–7 fur (insulation gene × 15), bits 0–3 appendages (× 15) (Revision O; reserved 0 before) |
| 26–41 | i8[16] | inherited appearance genes, in the order below; each maps to 0..1 as (g+127)/254 |
| 42 | u16 | depth below the water's surface, decimetres (metres × 10; 0 at the surface or out of water) |
| 44 | u8 | share of the step spent at that depth × 255 (the rest at the surface: an air-breather's dives; 255 for a fish or a bottom-walker) |
| 45 | u8 | bits 0–3 lungs × 15 (0 gills only, 15 lungs only); bits 4–7 oxygen debt × 15 (0 breathing freely, 15 about to suffocate or drown) |

Appearance genes: length, width, height, head size, neck length, tail length, limb placement,
limb shape, fin shape, wing shape, hue, saturation, lightness, markings, marking scale,
and marking hue. These are organism genes, not values generated from serials or clade labels.
The first 42 record bytes retain their format-6 meaning (the first 26 their format-5 meaning).

Depth (format 7, `src/Neuroreef.Core/Respiration.cs`): metres of water above the animal. The
column under it is the cell's water depth (the WORLD frame's depth byte gives it coarsely; in metres
it is `Depth / ElevPerM`). A swimmer holds a depth its depth-preference gene picks, pulled up when out
of breath; an air-breather dives there on a held breath for the given share of the step and is at the
surface the rest, so a renderer can show it at the depth (diving) or at the surface by that share.
A non-swimmer in water is either on the bottom (depth = the column) or at the surface (0).

### 0x03 FIELDS (~1 Hz; `view.fieldsMs` sets the interval)

| Offset | Type | Field |
|---|---|---|
| 8 | u16 | side D = FW / 2 (2×2 cells averaged) |
| 10 | u16 | layer count L |
| 12 | u8[D²] × L | layers, in the order of `meta.fieldLayers`: grass, shrub, tree, browse, fruit, seed, nectar, root, carrion, dung+litter, wood, algae, phyto, detritus, fire, scent, moisture, nutrient, pollination, pond, temp, frozen, plant height, toxin, thorns, drought tolerance, fleshy fruit, leaf-on, flora, flood, snow, volcanic, sedimentary, alluvium, limestone |

Layer values are 0..255 for 0..1. Pools that can exceed 1 are sent as tanh(amount) (carrion,
dung+litter, wood, detritus) or scaled (fruit, seed ×4; nectar ×8; scent tanh(4·s)). `pond` is 1 where a seasonal
pond holds water; `temp` is the air temperature as (T + 40) / 90 (−40..50 °C; Revision O); `frozen` is 1 where the air
is below 0 °C (frost on land, ice on water). The plant layers (Revision S): `plant height` is the tallest plant with
cover above 0.02 on the height trait's log scale (5 cm..40 m); `toxin`, `thorns` and `drought tolerance` are the
cover-weighted trait levels /15 of the cell's slots; `fleshy fruit` is the FRUIT level /15 of the fruit there; `leaf-on`
is the leafy share of the cover; `flora` is (founding lineage + 1) / 129 of the plant with most cover (0 = none). `flood` (2026-10-31) is 1 where a
river has flooded its floodplain (Wet = 3); clients look layers up by name, so one that predates it ignores it.
The terrain-material layers (2026-11-01, display only): `snow` is the snowpack's cover, 1 − e^(−SWE / 30 mm) (0 on water,
and everywhere when the water cycle is off: the client then falls back on `temp`); `volcanic`, `sedimentary` and
`limestone` are the share of the 2×2 cells whose surface rock (World.Rock) is of that kind (the rest is crystalline); `alluvium` is the
alluvium's depth as 1 − e^(−depth / ALLUV_D).

### 0x04 TELEMETRY (when a new sample exists, checked every 0.5 s)

| Offset | Type | Field |
|---|---|---|
| 8 | u32 | sample number |
| 12 | f32 × n | the newest sample; n and the meaning of each value are in `meta.telemetry.names` |

### 0x05 HISTORY (on connect, and on the `history` command)

| Offset | Type | Field |
|---|---|---|
| 8 | u32 | number of samples k (at most 400, decimated) |
| 12 | u32 | floats per sample n |
| 16 | u32 | stride (one kept sample per `stride` ring samples) |
| 20 | f32 × n × k | samples, oldest first |

### 0x06 PROBE (~10 Hz, while an animal is selected)

| Offset | Type | Field |
|---|---|---|
| 8 | u32 | serial (0: no animal; the frame ends here) |
| 12 | u32 | slot |
| 16 | u32 | mother's serial |
| 20 | u32 | youngest child's serial |
| 24 | f32 × `meta.probe.end` | the values below |

Float offsets (from `meta.probe`):

| Offset | Count | Content |
|---|---|---|
| feat | 30 | features (`meta.features`) |
| hid | 12 | recurrent memory units |
| z | 28 | output pre-activations |
| gain | 28 | primitive gains (the brain's intended values before exploration) |
| learn | 28 | mean \|W2f − W2_genome\| per output row: what this animal has learned |
| scal | 20 | `meta.probe.scalars`: reward, rewardMean, energy, emax, thirst, fatigue, full, age, life, mature, x, y, heading, speed, generation, children, species, stage, sex, pain |
| phen | 7 | `meta.probe.phenotype`: mass, vmax, sight, armour, strike, litter, basal |
| diet | 15 | digestive efficiency per pool, current stage |
| w1 | 504 | genome W1 (12 × 42), as weights |
| w2 | 1,176 | working W2f (28 × 42) |

## 2. Text frames (server → client), JSON

Every text frame has a `type`:

| type | When | Content |
|---|---|---|
| `meta` | first frame | world size, prehistory flag of the simulation on view, bank source, `species` = the clade table (32 slots: id, name, color, used, alive, parent, ancestor, generation, born, extinct, serial, count, niche and trophic level described from the members' genes, mean mass, majority locomotion, sight, fov, newborn share, sexual and live-birth shares, diet), `traitNames`, feature, primitive, pool and cause names, telemetry layout, probe layout, field layer names, land cell count, `explore` (metres per unit, the person's mass and health, `weapons`: id, name, key, `primary` and `secondary` modes with id, kind melee / thrown / bow / hitscan, `rangeM`, `damage`, `speedMps`, `cooldown`, `ammo`; 2026-11-01 vehicles: each mode also `vehicle` (null on foot), `radiusM`, `rounds`, `heatPerShot`, `maxTimeS`, `depthMinM`, `depthMaxM`, `area`, `underwater`, `ignites`; `vehicles`: id, name, `realm` (land / water), `maxSpeedMps`, `maxSlopeDeg`, `wadeDepthM`, `minDepthM`, `stallMps`, `ceilingM`, `weapon` (a mode as above: jeep `jeep-gun` kind auto, plane `bomb` kind bomb, boat `torpedo` kind torpedo); `launchSlackM`) |
| `status` | every 0.5 s | step, rate, measured rate, paused, alive, free slots, capacity (the slots the world has grown to), bank source (evolved / shipped / file / random), extinction step per species (−1 = alive), slot shortages, `full` (the last step refused a birth: the world is at its memory limit or --max-capacity), `growLimit` (that limit, in words, or null), probe serial, follow flag, alive count per species, season, `rain` (this year's rainfall anomaly), `phase` (`prehistory` / `live`), `phaseVersion` (changes when the simulation on view changes: another branch, or the world after the prehistory; the client then drops its history and the server resends HISTORY), `view` (branch on view), `branches`, `bankPath`, `player` (Explore mode: `present`, `hp`, `maxHp`, `deaths`, `kills`, `hits`, `x`, `y`, and for vehicles `vehicle` (or null), `alt`, `blasts`, `blastKills`, `fires`; null when no one has explored) |
| `vehicle` | answer to `vehicle` | `kind`, `ok`, `why` (null, or why it cannot be placed: not on land, not on water, too shallow, not flat enough to take off), `x`, `y`, `weapon`, `rounds` |
| `blast` | answer to `blast` | `id`, `weapon`, `ok`, `result` (Exploded, NotPresent, UnknownWeapon, WrongVehicle, NoAmmo, BadTime, LaunchFar, OutOfRange, TooFast, NotInWater, BadDepth), `hurt`, `killed`, `fires`, `rounds` (left), `kills`, `blastKills`, `victims` (`serial`, `clade`, `loss`, `killed`), `launchErrM`, `launchLimitM`, `flightM`, `x`, `y` |
| `clades` | when the clade table changes, and every 5 s | `step`, `splits`, `list`: the clade table of the simulation on view (as in `meta.species`) |
| `prehistory` | every 1 s, during the prehistory | `step`, `rate` (steps/s per branch), `paused`, `finished`, `yearsPerStep`, `view`, `settings` (the EvoSettings fields), `branches`: per branch `clades` (that branch's clade table), `splits`, `step`, `alive`, `full`, and per species `standing` (0 gone, 1 dying, 2 breeding, 3 self-sustaining), `count`, `births` (last ~1,000 steps), `generation` |
| `lexicon` | every 1.5 s | the translator (display only; Revision M, N): `step`, `minUtt`, `newSteps`, `clades`: per living clade `id`, `calls`, `utts`, `types`, `tracked`, `mute`, `bases`, `sounds` (per sound: `n`, `ctx`, `resp`, `heard`, `z` (standardised difference of every sense), `when`, `then`), `words` (the most made utterance types and every new one: `t`, `syl` (sounds 1..8 in order), `rank`, `n`, `heard`, `resp`, `first`, `last`, `born` (step it reached `minUtt`), `isNew`, `z`, `when`, `then`), `zipf` (counts of its utterance types, most made first), `dialect` (`west`, `east`, `jsd`, `chance`, or null); `user`: `id`, `sounds`, `resp` (how listeners answered the person's latest call) |
| `explain` | every 3 s | `items`: sentences generated from the telemetry (§12.4 of DESIGN), each with `text`, `species` (−1 = none) and `kind` |
| `fingerprints` | every 3 s | per species: sampled count and up to 3 clusters (`label`, `share`, `top`, `centroid`) |
| `ack` / `error` | on commands | result of a command |

## 3. Commands (client → server), JSON text frames

| cmd | Fields | Effect |
|---|---|---|
| `select` | `serial`, `follow` | start the PROBE for this animal (0 = none); with `follow`, the probe moves to the youngest living child (else the nearest conspecific) when the animal dies |
| `pick` | `species` | select a random living member of the species, with follow on |
| `follow` | `on` | lineage follow on or off |
| `rate` | `value` | steps per second (0 = as fast as possible) |
| `pause` / `resume` / `step` | — | scheduler control |
| `history` | — | resend HISTORY |
| `view` | `hz`, `fieldsMs` | AGENTS rate (1–30 Hz) and FIELDS interval (250–10,000 ms) for this client |
| `savebank` | — | save a gene bank next to the executable |
| `branch` | `index` | prehistory: view this branch |
| `evo` | `set`: {field: number} | prehistory: change EvoSettings fields (`mutRate`, `mutSmall`, `mutBigShare`, `mutBig`, `totalSteps`); applied at the next barrier |
| `speak` | `x`, `y`, `sounds`: [k1, k2, k3] (0..7, up to 3 distinct; or the older `sound`: k) | the person's utterance at a place, heard on the next step by the animals within HEAR_R as a stranger's (Revision N) |
| `begin` | — | end the prehistory now and start the world from the brains evolved so far |
| `player` | `on` (false: leave), `x`, `y` (world units), `h` (heading, radians), `run`, `veh` (2026-11-01: the vehicle it is in, a `meta.explore.vehicles` id; absent on foot), `alt` (its height above the ground, m) | Explore mode, live world only: the person's place, a few times a second. The animals sense it as a 70 kg armed omnivore (Player.cs), heard twice as far in a vehicle, and neither see nor strike a plane more than 25 m up; it leaves when `on` is false, its console disconnects, or no update came for 5 s. Only a kind placed with `vehicle` can be boarded. The server keeps the places it is sent, with its own clock, as the person's path (for `blast`) |
| `vehicle` | `kind`, `x`, `y` (world units) | Explore vehicles: a vehicle of the kind was placed there. The server checks the place (a jeep or plane on land, a plane's cell and its neighbours within its slope; a boat on water at least `minDepthM` deep) and loads its weapon (`rounds`); answered with a `vehicle` frame |
| `blast` | `id` (the console's), `weapon` (an area mode: `bomb`, `torpedo`), `x`, `y` (where it went off, world units), `depth` (m below the surface; a torpedo), `lx`, `ly` (where it was launched), `t` (its flight, s: at least the wall time since launch) | Explore vehicles: a bomb or torpedo exploded. Checked (Player.Blast): the person is in that weapon's vehicle with a round left; `t` within the weapon's `maxTimeS`; the launch point within `launchSlackM` + half a second of the vehicle's top speed of the path the person was sent along since `t` + 2 s ago; the explosion within `rangeM` of the launch and no further than `speedMps` × `t` (+25 m); a torpedo in or beside water at a depth it can run at. Then every animal within `radiusM` takes the weapon's damage × (1 − d / R) × how much of its step its depth overlaps the blast's (within STRIKE_DZ, as strikes in water: World.Z for ZShare of the step, at the surface the rest), as a hit (pain, the learner told; killed: cause hunted, carrion); a burst of scent there reads as fear; a bomb sets dry fuel on its cell alight (Fields.IgniteAt). Answered with a `blast` frame |
| `hit` | `weapon` (a mode id of `meta.explore.weapons`), `serial`, `power` (0..1, a bow's draw), `damage` (at most the weapon's), `x`, `y`, `z` | Explore mode: the console's aim hit an animal. The server checks it (alive, in range of the person's place, ground not in the way) and applies it (Player.Hit); it answers this console with a `hit` frame: `serial`, `ok`, `killed`, `result` (Wounded, Killed, NoAnimal, OutOfRange, NoLineOfSight, NotPresent, UnknownWeapon, and for vehicles WrongVehicle (the mode belongs to another vehicle, or to the person on foot) and RateLimited (an `auto` mode, the jeep's gun: the server allows a burst of 3 hits, then one per `cooldown` of its own clock)), `clade`, `loss` (energy taken), `kills` |

During the prehistory, `pause` / `resume` hold all branches; `rate` and `step` have no effect.

There is one probe for the simulation: when several clients select animals, the last selection wins.

## 4. Timing and safety

- Frames are taken from the newest data. A client that falls behind skips frames; it never queues
  them, and it never slows the simulation (NR-506).
- AGENTS, FIELDS and PROBE are encoded on the simulation thread between steps (so they are
  consistent), copied once, and shared by all clients. TELEMETRY and HISTORY are read from the
  telemetry ring on the server thread. Explanations and fingerprints read the simulation without
  locking; they are for display only and do not affect the run.
