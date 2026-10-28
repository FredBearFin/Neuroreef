# NEUROREEF 2 — INTERFACE CONTROL DOCUMENT

Document NR-ICD-001, Revision A (draft). This is the link between the simulation server and the
browser console: one WebSocket at `ws://<host>:5800/ws`.

All binary frames are **little-endian**. Every frame starts with an 8-byte header:

| Offset | Type | Field |
|---|---|---|
| 0 | u8 | frame type |
| 1 | u8 | format version (= 1) |
| 2 | u16 | reserved (0) |
| 4 | u32 | simulation step |

## 1. Server → client frames

### 0x01 WORLD (once, on connect)

| Offset | Type | Field |
|---|---|---|
| 8 | u16 | WORLD (units) |
| 10 | u16 | FW (field side) |
| 12 | u32 | seed |
| 16 | u8 | number of species S |
| 17 | … | per species: u8 name length, UTF-8 name, 3×u8 colour, f32 mass, f32 sight, f32 fov |
| … | u8[FW²] | elevation (0 = deepest, 255 = highest; sea level is sent as a u8 after this array) |

### 0x02 AGENTS (~20 Hz)

| Offset | Type | Field |
|---|---|---|
| 8 | u32 | count n |
| 12 | 12 B × n | records |

Each record:

| Offset | Type | Field |
|---|---|---|
| 0 | u16 | x · 65536/WORLD |
| 2 | u16 | y · 65536/WORLD |
| 4 | u8 | heading · 256/2π |
| 5 | u8 | species (low 3 bits), sex (bit 3), juvenile (bit 4), gestating (bit 5), scanning (bit 6), resting (bit 7) |
| 6 | u8 | energy / Emax · 255 |
| 7 | u8 | action bits: eat, strike, drink, mate, sprint, flee-dominant, hunt-dominant, (reserved) |
| 8 | u32 | serial |

When the client has set a viewport (§2), only animals inside it plus a margin are sent.

### 0x03 FIELDS (~2 Hz)

| Offset | Type | Field |
|---|---|---|
| 8 | u16 | side D (down-sampled; D = FW or FW/2) |
| 10 | u16 | layer mask |
| 12 | u8[D²] × layers | grass, shrub, sapling, tree, fruit, carrion, water, fire, scent, moisture, nutrient (order of mask bits 0..10) |

### 0x04 TELEMETRY (~2 Hz)

| Offset | Type | Field |
|---|---|---|
| 8 | u16 | n floats |
| 10 | f32 × n | latest sample, layout in `Telemetry.cs` (`SampleLayout`) |

The layout is also sent once, as JSON in frame 0x10.

### 0x05 HISTORY (on connect, and on request)

The whole ring, oldest first:

| Offset | Type | Field |
|---|---|---|
| 8 | u16 | n floats per sample |
| 10 | u16 | sample count k |
| 12 | f32 × n × k | samples |

### 0x06 PROBE (~20 Hz, while an animal is selected)

| Offset | Type | Field |
|---|---|---|
| 8 | u32 | serial (0 = selected animal died) |
| 12 | f32 × 30 | inputs x (26 features + 4 motivations) |
| … | f32 × 32 | outputs z |
| … | f32 × 28 | gains g |
| … | u32 | action bits |
| … | f32 × 8 | energy, gut, thirst, fatigue, age, x, y, heading |
| … | u32 × 3 | mother serial, generation, youngest living child serial |
| … | i8 × 960 | decision matrix (sent once per selection, then every 64 frames) |

### 0x10 JSON

A UTF-8 JSON document after the header. It carries:
- the telemetry sample layout
- species dossiers
- explanation sentences (`{"explain":[{"text":…,"refs":[…]}]}`)
- command acknowledgements

## 2. Client → server commands (text frames, JSON)

| Command | Fields | Effect |
|---|---|---|
| `select` | `serial` | start PROBE for this animal; 0 = none |
| `pick` | `species` | select a random living member of the species; reply with its serial |
| `follow` | `serial` | as select; server tracks lineage on death and reports the new serial |
| `speed` | `rate` (steps/s, 0 = max) | set rate |
| `pause` / `resume` / `step` | — | scheduler control |
| `view` | `x0, y0, x1, y1` (units), `hz` | viewport culling and frame rate for this client |
| `history` | — | resend HISTORY |

## 3. Timing

- Frames are taken from the newest snapshot. A client that falls behind skips frames. It never queues
  them (NR-506).
- Per client, at most one frame of each type is in flight.
