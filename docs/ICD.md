# NEUROREEF 2 — INTERFACE CONTROL DOCUMENT

Document NR-ICD-001, Revision C. This is the link between the simulation server and the browser
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

### 0x02 AGENTS (~20 Hz; `view.hz` sets the rate per client)

| Offset | Type | Field |
|---|---|---|
| 8 | u32 | count n |
| 12 | 12 B × n | records (every living agent except embryos) |

Each record:

| Offset | Type | Field |
|---|---|---|
| 0 | u16 | x · 65536 / WORLD |
| 2 | u16 | y · 65536 / WORLD |
| 4 | u8 | heading · 256 / 2π |
| 5 | u8 | bits 0–4 species id; bit 5 sex |
| 6 | u8 | bits 0–1 stage (0 egg, 1 juvenile, 2 adult); bit 2 carrying; bit 3 scanning; bit 4 resting; bits 5–7 energy/Emax × 7 |
| 7 | u8 | actions fired this step: bit 0 EAT, 1 STRIKE, 2 DRINK, 3 MATE, 4 REST, 5 MARK, 6 LAY, 7 CACHE |
| 8 | u32 | serial (unique for the run) |

### 0x03 FIELDS (~1 Hz; `view.fieldsMs` sets the interval)

| Offset | Type | Field |
|---|---|---|
| 8 | u16 | side D = FW / 2 (2×2 cells averaged) |
| 10 | u16 | layer count L |
| 12 | u8[D²] × L | layers, in the order of `meta.fieldLayers`: grass, shrub, tree, browse, fruit, seed, nectar, root, carrion, dung+litter, wood, algae, phyto, detritus, fire, scent, moisture, nutrient, pollination |

Layer values are 0..255 for 0..1. Pools that can exceed 1 are sent as tanh(amount) (carrion,
dung+litter, wood, detritus) or scaled (fruit, seed ×4; nectar ×8; scent tanh(4·s)).

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
| `meta` | first frame | world size, prehistory flag of the simulation on view, bank source, species list with body plans, feature, primitive, pool and cause names, telemetry layout, probe layout, field layer names, land cell count |
| `status` | every 0.5 s | step, rate, measured rate, paused, alive, free slots, capacity, bank source (evolved / shipped / file / random), extinction step per species (−1 = alive), slot shortages, probe serial, follow flag, alive count per species, season, `phase` (`prehistory` / `live`), `phaseVersion` (changes when the simulation on view changes: another branch, or the world after the prehistory; the client then drops its history and the server resends HISTORY), `view` (branch on view), `branches`, `bankPath` |
| `prehistory` | every 1 s, during the prehistory | `step`, `rate` (steps/s per branch), `paused`, `finished`, `migrations`, `yearsPerStep`, `view`, `settings` (the EvoSettings fields), `branches`: per branch `step`, `alive`, `full`, and per species `standing` (0 gone, 1 propped, 2 breeding, 3 self-sustaining), `count`, `births`, `colonists` (last ~1,000 steps), `generation` |
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
| `evo` | `set`: {field: number} | prehistory: change EvoSettings fields (`mutRate`, `mutSmall`, `mutBigShare`, `mutBig`, `archiveShare`, `tournament`, `floor`, `colonistSteps`, `totalSteps`, `migrateEvery`, `migrants`); applied at the next barrier |
| `begin` | — | end the prehistory now and start the world from the brains evolved so far |

During the prehistory, `pause` / `resume` hold all branches; `rate` and `step` have no effect.

There is one probe for the simulation: when several clients select animals, the last selection wins.

## 4. Timing and safety

- Frames are taken from the newest data. A client that falls behind skips frames; it never queues
  them, and it never slows the simulation (NR-506).
- AGENTS, FIELDS and PROBE are encoded on the simulation thread between steps (so they are
  consistent), copied once, and shared by all clients. TELEMETRY and HISTORY are read from the
  telemetry ring on the server thread. Explanations and fingerprints read the simulation without
  locking; they are for display only and do not affect the run.
