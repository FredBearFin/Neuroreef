# NEUROREEF 2 — DESIGN DOCUMENT

Document NR-DD-001, Revision A (draft for review). It implements NR-RQ-001 (`REQUIREMENTS.md`).

---

## 1. Purpose and the central idea

Neuroreef is an artificial-life ecosystem. In version 1 every species ran the same 14→8→3 network,
and "prey", "threat" and "kin" were fixed per species pair. A grazer and a hunter differed only in
hand-set weights. Version 2 changes the model:

1. **One set of senses.** Every animal perceives the world through the same feature vector. It sees
   other animals only by what can be observed about them: same species or not, bigger or smaller,
   meat-eater or not, approaching or not, young or not, tired or not.
2. **One behaviour library.** Every animal has the same primitive behaviours: seek, flee, climb a
   gradient, cohere, align, intercept, circle, hold, scan, sprint, eat, strike, drink, mate, rest,
   mark.
3. **Species are compositions.** A species is defined by its body plan and by its decision matrix,
   which weights the primitives from the features. "Grazing", "vigilance", "pack hunting" and
   "scavenging" are not written anywhere in the code. They are particular weightings of the same
   primitives, and evolution can re-weight them.
4. **Ecology is explicit.** Body size scales metabolism (Kleiber's law). A gut limits intake (handling
   time). Water is required. Strike success depends on size and armour. Vegetation goes through
   succession and burns.

The simulation core is C# with no dependencies. It is written in a fixed-memory, fixed-step,
deterministic style. A server streams it to a browser console for 3D viewing and analytics.

## 2. System overview

```
+---------------------------- Neuroreef.exe ----------------------------+
|                                                                        |
|  SIM THREAD                      SERVER (Kestrel)                      |
|  +--------------------+  write   +------------------+   WebSocket     |
|  | Neuroreef.Core     |--------->| Snapshot triple  |---------------> browser console
|  |  World (all state) |  latest  | buffer + sender  |   binary frames |  (three.js 3D,
|  |  Step phases 1..6  |          +------------------+   (ICD.md)      |   dashboards)
|  |  Telemetry rings   |<---------  commands (JSON)  <-----------------|
|  +--------------------+                                               |
|  worker pool (fixed N threads) for the parallel phases                |
+------------------------------------------------------------------------+
```

Headless mode runs the same core with no server and writes reports.

## 3. Memory map

All arrays are allocated once in `World` construction. The sizes are:
- C = capacity (default 32,768 animal slots).
- WORLD = side of the toroidal world in units (default 1,024).
- FW = WORLD / 4, the field grid side (default 256).
- GW = WORLD / 16, the spatial-hash grid side (default 64).
- F = 30, the number of decision inputs (26 features + 4 motivations).
- O = 32, the number of decision outputs (28 primitive gains + 4 motivations).

| Block | Type | Elements | Default size |
|---|---|---|---|
| Animal kinematics: X, Y, Heading, Speed | float | 4·C | 512 KB |
| Animal physiology: Energy, Gut, GutEnergy, Thirst, Fatigue, Age, Attacked, Cooldown, Gestation | float | 9·C | 1.1 MB |
| Animal identity: Species (byte), Sex (byte), Alive (byte), Flags (byte), Gen (ushort), Serial, Mother, Father (uint) | mixed | C | 160 KB |
| Motivations | float | 4·C | 512 KB |
| Body-plan genes | float | 16·C | 2 MB |
| Decision matrix | sbyte | (O·F)·C = 960·C | 30 MB |
| Founder matrices and body plans | sbyte / float | 6 species | 6 KB |
| Intents (heading, speed, target slot, action bits, primitive gains) | float / int | (4 + 28)·C | 4 MB |
| Spatial hash: CellStart, CellCount, Sorted | int | 2·GW² + C | 160 KB |
| Free-slot stack; breed requests; resolve order | int | 3·C | 384 KB |
| Vegetation fields: Grass, Shrub, Sapling, Tree, Fruit, Fuel; double-buffered | float | 12·FW² | 3 MB |
| Soil and water: Nutrient, Moisture, BaseMoisture, Water, Elevation | float | 5·FW² | 1.3 MB |
| Animal fields: Carrion, Scent, Fire, KillFlash, BirthFlash | float | 5·FW² | 1.3 MB |
| Telemetry counters | long | about 600 | 5 KB |
| Telemetry rings (S = 4,096 samples) | float | about 400·S | 6.5 MB |
| Snapshot buffers (×3) | byte | 12·C + headers | 1.2 MB |

The total default is about 52 MB.

## 4. Step contract

One step equals one tick. Each phase lists what it reads and writes. "∥" marks a phase that runs in
parallel over a fixed partition of slot ranges or field rows. A parallel phase writes only into the
elements it owns, so its result does not depend on scheduling (NR-105).

| # | Phase | ∥ | Reads | Writes |
|---|---|---|---|---|
| 1 | BUILD | no | X, Y, Alive | CellStart, CellCount, Sorted (counting sort: deterministic) |
| 2 | DECIDE | ∥ slots | everything | Intents and Motivations of its own slot only |
| 3 | RESOLVE | no | Intents | kinematics, physiology, fields, Alive; counters |
| 4 | SPAWN | no | breed requests, free stack | new slots; counters |
| 5 | VEGETATION | ∥ rows | old field buffer | new field buffer (buffers swap afterwards) |
| 6 | TELEMETRY | no | everything | counters, rings (every K = 32 steps) |

**DECIDE** computes perception (§5.1), runs the decision matrix (§5.3) and turns gains into intents.
It does not change any shared state.

**RESOLVE** visits the living animals in an order that is a seeded permutation of the slots, different
every step: `order[k] = (k·A + B_step) mod C` with A odd and coprime with C. For each animal it applies:
1. Move.
2. Drink.
3. Eat or strike.
4. Pay costs.
5. Digest.
6. Age.
7. Die.
8. Mate (request).

Contested resources (the grass in a cell, a prey animal) go to whoever comes first in this order. That
is fair on average and fully deterministic.

## 5. Decision architecture

### 5.1 Features (identical for every animal; NR-201, NR-202)

Animals are perceived within the sight range R and the field of view Φ (body-plan genes; Scan widens
Φ to 360° and R by 30%). "Closeness" of an animal at distance d is `1 − d/R`. "Density" is the sum of
closeness over a class, passed through tanh.

**Bias and drives**

| i | Name | Meaning | Range |
|---|---|---|---|
| 0 | BIAS | constant 1 | 1 |
| 1 | HUNGER | 1 − Energy/Emax | 0..1 |
| 2 | THIRST | Thirst (death at 1) | 0..1 |
| 3 | FATIGUE | sprint debt / fatigue limit | 0..1 |
| 4 | READY | 1 if mature, not gestating, Energy > 0.6·Emax, cooldown over | 0/1 |
| 5 | AGE | Age / lifespan | 0..1 |
| 6 | JUVENILE | 1 if Age < maturity | 0/1 |
| 7 | FULL | Gut / gut capacity | 0..1 |

**Conspecifics**

| i | Name | Meaning | Range |
|---|---|---|---|
| 8 | KIN_DENSITY | density of conspecifics | 0..1 |
| 9 | KIN_CLOSE | closeness of nearest conspecific (crowding) | 0..1 |
| 10 | MOTHER_FAR | 1 − closeness of mother; 0 if no living mother or adult | 0..1 |
| 11 | MATE_CLOSE | closeness of nearest ready conspecific of the other sex | 0..1 |

**Larger animals of other species**

| i | Name | Meaning | Range |
|---|---|---|---|
| 12 | BIG_CLOSE | closeness of nearest larger heterospecific | 0..1 |
| 13 | BIG_DENSITY | density of larger heterospecifics | 0..1 |
| 14 | BIG_APPROACH | closing speed of nearest larger, divided by own vmax | −1..1 |
| 15 | BIG_CARNIVORE | carnivory trait of nearest larger | 0..1 |

**Smaller animals of other species**

| i | Name | Meaning | Range |
|---|---|---|---|
| 16 | SMALL_CLOSE | closeness of nearest smaller heterospecific | 0..1 |
| 17 | SMALL_DENSITY | density of smaller heterospecifics (prey confusion) | 0..1 |
| 18 | SMALL_WEAK | vulnerability of nearest smaller: juvenile, fatigued, low armour | 0..1 |

**Meat-eaters of any size**

| i | Name | Meaning | Range |
|---|---|---|---|
| 19 | CARN_CLOSE | closeness of nearest meat-eating heterospecific | 0..1 |

**Resources at the animal's cell**

| i | Name | Meaning | Range |
|---|---|---|---|
| 20 | GRASS_HERE | grass biomass in own cell | 0..1 |
| 21 | BROWSE_HERE | shrub + sapling leaf biomass | 0..1 |
| 22 | FRUIT_HERE | fruit | 0..1 |
| 23 | CARRION_HERE | carrion, tanh | 0..1 |
| 24 | WATER_CLOSE | closeness of nearest water cell, within 3·R | 0..1 |

**Danger memory**

| i | Name | Meaning | Range |
|---|---|---|---|
| 25 | FEAR | max(scent here, Attacked memory) | 0..1 |

**Motivation inputs** (the motivation state from the previous step)

| i | Name | Meaning | Range |
|---|---|---|---|
| 26…29 | MOT0…MOT3 | motivation state from the previous step | −1..1 |

"Larger" and "smaller" compare actual masses, with a ±15% dead band in which another animal is
neither. "Meat-eater" is the visible carnivory trait (the body plan's meat efficiency), so it is an
attribute of the other animal, not a relation table.

### 5.2 Behaviour library (identical for every animal; NR-203)

A primitive is an *operator* applied to a *target*. Steering primitives return a unit vector (zero if
their target is absent). Speed and action primitives return nothing; their gain is used directly.

**Steering primitives**

| p | Primitive | Vector |
|---|---|---|
| 0 | SEEK_SMALL | toward nearest smaller heterospecific |
| 1 | SEEK_BIG | toward nearest larger heterospecific |
| 2 | SEEK_CARN | toward nearest meat-eater, but tangential once inside R/2 (shadowing) |
| 3 | FLEE_BIG | away from nearest larger heterospecific |
| 4 | FLEE_CARN | away from nearest meat-eater |
| 5 | INTERCEPT_SMALL | toward the predicted position of nearest smaller (lead = d / own speed) |
| 6 | CIRCLE_SMALL | perpendicular to the target bearing, on the side away from nearest kin (flanking) |
| 7 | COHERE | toward the centroid of visible kin |
| 8 | SEPARATE | away from kin closer than 2 units, weighted by inverse distance |
| 9 | ALIGN | mean heading of visible kin |
| 10 | FOLLOW_MOTHER | toward mother |
| 11 | SEEK_MATE | toward nearest ready mate |
| 12 | CLIMB_GRASS | gradient of grass (central difference over 2 cells) |
| 13 | CLIMB_BROWSE | gradient of browse |
| 14 | CLIMB_FRUIT | gradient of fruit |
| 15 | CLIMB_CARRION | gradient of carrion, sampled at 3 cells so the scent carries |
| 16 | SEEK_WATER | toward nearest water cell |
| 17 | AVOID_FEAR | down the gradient of scent |
| 18 | WANDER | heading + slowly changing hashed noise (correlated random walk) |

**Speed and attention primitives**

| p | Primitive | Effect |
|---|---|---|
| 19 | HOLD | reduces speed |
| 20 | SCAN | stops the animal; widens the field of view to 360° and range ×1.3 next step; forbids EAT |
| 21 | SPRINT | raises speed toward vmax; builds fatigue |

**Action primitives** (each fires if gain > 0.5 and its precondition holds)

| p | Primitive | Precondition | Effect |
|---|---|---|---|
| 22 | EAT | food here; gut not full; not scanning | bite from the field with the best digestible value |
| 23 | STRIKE | target within reach; not juvenile | attack nearest smaller (or larger, if no smaller), §7.4 |
| 24 | DRINK | water within reach | Thirst → 0 over a few steps |
| 25 | MATE | READY, mate within reach and also READY | conception (§7.6) |
| 26 | REST | — | speed 0; fatigue recovers 3× faster; basal cost × 0.85 |
| 27 | MARK | — | deposit own scent (meat-eaters always deposit a little anyway) |

**Motivation outputs** (p = 28…31) feed back as features 26…29.

### 5.3 Decision matrix (NR-204, NR-208)

Each animal owns the matrix W, of size O × F = 32 × 30 entries, stored as signed bytes. A weight w
stands for w/16, so the range is ±7.9.

```
x      = [features f(0..25), motivations m(0..3)]          (30 inputs)
z_p    = Σ_i W[p,i] · x_i / 16                             (32 outputs)
m'_k   = tanh(z_{28+k})                                    motivation update
g_p    = σ(z_p) = 1 / (1 + e^{-z_p})       for p < 28      primitive gain, 0..1
```

The feature BIAS = 1 acts as the bias column. A gain of 0.5 is neutral. Founders set the bias rows to
about −3 (gain ≈ 0.05), so a primitive is quiet until a feature turns it on.

**Intent from gains**
- **Steering:** `s = Σ_{p≤18} g_p · v_p`. The desired heading is atan2(s). The new heading turns
  toward it by at most the turn rate. If |s| < 0.05, the heading is kept.
- **Speed:**
  `speed = vmax · clamp(0.45 + 0.55·g_SPRINT − 0.45·g_HOLD, 0, 1) · (1 − g_SCAN) · (1 − rest) · (1 − 0.5·FATIGUE)`.
  The action REST sets rest = 1.
- **Actions:** each action primitive fires if its gain is > 0.5 and its precondition holds.
  EAT and SCAN are exclusive: the larger gain wins.

One evaluation is 960 multiply-adds plus 32 transcendental functions per animal per step.

### 5.4 Inheritance (NR-205)

- **Crossover:** uniform per output row. The child takes each row (one primitive's whole
  weighting) from mother or father with probability ½. Keeping rows whole keeps a primitive's wiring
  intact, analogous to genetic linkage.
- **Mutation:** each byte, with probability μ = 0.03, moves by round(N(0,1)·2). With probability
  μ·0.05 it instead jumps by round(N(0,1)·16). The result is clamped to ±127. (N(0,1) is approximated,
  as in v1, by the sum of three uniforms.)
- **Body plan:** each heritable gene is multiplied by exp(0.02·N(0,1)) and clamped to ±30% of the
  species founder value. Diet efficiencies are clamped to 0..1, so a species cannot leave its niche by
  drift alone in a short run, but a long run can.

## 6. Founder compositions

These are the hand-set starting matrices. Each line is `PRIMITIVE ← weight·FEATURE + …` in weight
units of 1/16 (so +48 = +3.0). Entries not listed are 0, apart from small founder noise
(±2, uniform). Every primitive row also has its bias: −48 unless stated.

The tables below *are* the design of each species' behaviour. Each is a combination of the same
primitives (NR-206).

### 6.1 Shared core (all species start with these rows)

```
SEPARATE      ← +96·KIN_CLOSE                        personal space
WANDER        ← bias −8                              light background exploration
SEEK_WATER    ← +112·THIRST                          drink when thirsty
DRINK         ← +128·THIRST  (bias −24)
REST          ← +96·FATIGUE  −64·FEAR                rest when tired unless afraid
SEEK_MATE     ← +80·READY  +40·MATE_CLOSE  −48·HUNGER
MATE          ← +112·READY                           (bias −40)
FOLLOW_MOTHER ← +96·MOTHER_FAR  +48·JUVENILE
MOT1 (alarm)  ← +48·FEAR +40·BIG_APPROACH +56·MOT1   persistence: alarm lingers
```

### 6.2 Drifters (small grazer): "a grazing, vigilant herd"

```
CLIMB_GRASS  ← +80·HUNGER  −48·FULL
EAT          ← +96·HUNGER  +48·GRASS_HERE  −64·MOT1
COHERE       ← bias +8, +40·MOT1                    herd; tighter when alarmed
ALIGN        ← +48·KIN_DENSITY
SCAN         ← +48·BIG_CLOSE +32·FEAR −40·HUNGER
FLEE_CARN    ← +96·CARN_CLOSE +48·MOT1
FLEE_BIG     ← +56·BIG_APPROACH
SPRINT       ← +64·MOT1 +48·BIG_APPROACH
AVOID_FEAR   ← +48·FEAR
```

**Vigilance** is SCAN + COHERE when a big animal is near and hunger is moderate. **Escape** is
FLEE + SPRINT driven by the alarm motivation, which persists after the predator leaves sight.

### 6.3 Browsers (large browser): "armoured giants with calves"

```
CLIMB_BROWSE ← +72·HUNGER  −48·FULL
CLIMB_FRUIT  ← +48·HUNGER
EAT          ← +80·HUNGER +48·BROWSE_HERE +32·FRUIT_HERE
COHERE       ← +40·KIN_DENSITY +32·MOT1
STRIKE       ← +80·CARN_CLOSE +40·MOT1  −64·JUVENILE    adults defend against predators
SEEK_CARN    ← +48·CARN_CLOSE −80·JUVENILE              adults mob predators
FLEE_CARN    ← +64·JUVENILE +48·CARN_CLOSE              calves run
FOLLOW_MOTHER← (core) +96·MOTHER_FAR, plus +64·MOT1      calves run to mother when alarmed
```

**Adult defence** is SEEK_CARN + STRIKE. Armour (§7.4) makes it work. **Calf protection** is
FOLLOW_MOTHER under alarm. Browsing on saplings holds back forest (§8).

Some lines combine two features in one gate (for example "adult and carnivore close"). The matrix is
linear before the sigmoid, so such a gate is written as two weights (a positive one on the trigger, a
negative one on JUVENILE) that only together pass the threshold. That is the realised form shown.

### 6.4 Foragers (small omnivore): "opportunist"

```
CLIMB_FRUIT   ← +64·HUNGER
CLIMB_CARRION ← +40·HUNGER
EAT           ← +96·HUNGER +40·FRUIT_HERE +40·CARRION_HERE
SEEK_SMALL    ← +40·HUNGER +40·SMALL_WEAK                 takes the weak or young
STRIKE        ← +64·SMALL_WEAK +40·HUNGER
FLEE_CARN     ← +96·CARN_CLOSE +32·MOT1
FLEE_BIG      ← +48·BIG_CLOSE
SPRINT        ← +56·MOT1
COHERE        ← +24·MOT1
```

### 6.5 Stalkers (mesopredator): "pack hunters"

```
MOT0 (hunt)     ← +56·HUNGER +40·SMALL_CLOSE +48·MOT0 −64·FULL −48·FATIGUE   (bias −40)
COHERE          ← bias 0, −32·MOT0                        stay as a pack while not hunting
ALIGN           ← +40·KIN_DENSITY
SEEK_SMALL      ← +64·MOT0
CIRCLE_SMALL    ← +40·MOT0 +40·KIN_DENSITY −48·SMALL_CLOSE*   flank when packmates present
INTERCEPT_SMALL ← +72·MOT0 +56·SMALL_CLOSE
SPRINT          ← +64·SMALL_CLOSE +32·MOT0 −72·FATIGUE    sprint only near the end of a chase
STRIKE          ← +96·SMALL_CLOSE +48·MOT0 +32·SMALL_WEAK
EAT             ← +96·CARRION_HERE +48·HUNGER             eat the kill
HOLD            ← +40·FULL                                lie up on a full gut
FLEE_BIG        ← +56·BIG_CARNIVORE +40·BIG_CLOSE
MARK            ← +24·MOT0
```

`*` The CIRCLE row's negative weight on SMALL_CLOSE turns flanking into interception as the gap
closes. **Give-up** is not coded. FATIGUE suppresses SPRINT and the hunt motivation, so a chase that
does not close before fatigue builds ends by itself, which is the marginal-value outcome.

### 6.6 Leviathans (apex predator): "solitary ambusher"

```
MOT0 (hunt)     ← +48·HUNGER +40·SMALL_CLOSE +40·MOT0 −64·FULL −40·FATIGUE   (bias −40)
SEPARATE        ← (core) +96·KIN_CLOSE, plus +40·KIN_DENSITY   solitary
HOLD            ← +56·MOT0 −72·SMALL_CLOSE                 wait in ambush ...
SEEK_SMALL      ← +40·MOT0 +48·SMALL_CLOSE                  ... then lunge
SPRINT          ← +80·SMALL_CLOSE −64·FATIGUE
STRIKE          ← +96·SMALL_CLOSE +48·MOT0 +40·SMALL_WEAK
SEEK_WATER      ← (core), plus +32·MOT0                     hunt at waterholes
EAT             ← +96·CARRION_HERE +40·HUNGER
REST            ← (core), plus +56·FULL
```

**Ambush** is HOLD while hunting, released into SEEK + SPRINT when prey comes close. Waiting near water
is a weighting of the shared SEEK_WATER primitive by the hunt motivation.

### 6.7 Gleaners (scavenger): "follow the hunters"

```
CLIMB_CARRION ← +96·HUNGER
SEEK_CARN     ← +56·HUNGER −40·CARRION_HERE           shadow predators at a distance
EAT           ← +112·CARRION_HERE +48·HUNGER
FLEE_CARN     ← +64·MOT1 +48·CARN_CLOSE −40·HUNGER     brave when hungry
FLEE_BIG      ← +40·BIG_APPROACH
COHERE        ← +32·KIN_DENSITY
SPRINT        ← +48·MOT1
```

Gleaners have litters of 6 and high juvenile mortality, an r-strategy.

### 6.8 Founder verification scenes (NR-207)

These are automated tests that build a small world by hand.

| Scene | Set-up | Pass condition |
|---|---|---|
| Herd | 30 Drifters scattered, full grass | mean nearest-kin distance halves within 300 steps |
| Vigilance | 10 grazing Drifters, one Stalker walking toward them | SCAN or FLEE gain > 0.5 in ≥ 80% before contact |
| Pack | 3 Stalkers, 1 Drifter in open | kill within 400 steps in ≥ 70% of 20 seeds |
| Give-up | 1 Stalker, 1 Drifter with a 2× head start | chase abandoned (SPRINT < 0.3) before 600 steps |
| Scavenger | 1 Stalker hunting, 2 Gleaners | Gleaners stay within 2R of the Stalker and reach the carcass |
| Calf | Browser mother + calf, a Leviathan approaches | calf distance to mother stays < 6 |
| Thirst | any species, water 40 units away | drinks before Thirst = 1 |

## 7. Animal ecology

### 7.1 Allometry (NR-301)

Every rate derives from mass M. The species factor k (where present) lets a species be faster or
slower than the allometric norm.

| Quantity | Rule |
|---|---|
| Basal cost per step | B = c_B · M^0.75 |
| Energy reserve max | Emax = c_E · M |
| Gut capacity | Gcap = c_G · M |
| Digestion rate | D = c_D · M^0.75 |
| Max speed | vmax = c_V · M^0.17 · k_speed |
| Turn rate | τ = c_τ · M^−0.2 |
| Lifespan | L = c_L · M^0.25 · k_life |
| Maturity age | A_m = c_A · M^0.25 · k_mat |
| Sight range | R = c_R · M^0.2 · k_sight |
| Reach (strike, eat, mate) | r = 1.0 + 0.4 · M^0.33 |
| Thirst rate | θ = c_θ (per step; independent of mass) |
| Movement cost | c_M · M · speed² |
| Sprint fatigue | Fatigue += c_F · g_SPRINT · speed/vmax; recovery c_R0 per step (×3 when resting) |

### 7.2 Gut and digestion (NR-302)

Food types k ∈ {grass, browse, fruit, meat, carrion} have energy densities ρ_k. EAT takes a bite
b = min(c_bite·M^0.75, food here, Gcap − Gut) of the food type with the highest ρ_k·eff_k present. It
adds b to Gut and b·ρ_k·eff_k to GutEnergy.

Each step, d = min(Gut, D) is digested. Energy += GutEnergy·d/Gut, and the undigested share
(1 − eff_k) of the bite returns as dung, which becomes soil nutrient in the current cell.

A full gut stops EAT. The gut empties only at D. Intake rate therefore saturates at high food density,
giving a Holling type II response from handling time alone. The telemetry samples intake against local
food density to show it (NR-402).

### 7.3 Water (NR-303)

Thirst increases by θ per step. DRINK within reach of a water cell lowers it by 0.25 per step. Thirst ≥
1 is death (cause: thirst). Water cells are permanent lakes (elevation below sea level) plus seasonal
pools (low basins whose moisture is above 0.8).

### 7.4 Strike outcome (NR-304)

When attacker a strikes target v within reach:

```
power   = strike_a · M_a
defence = armour_v · M_v · (JUVENILE_v ? 0.25 : 1)
p       = p0 · power/(power + defence) · (1 + 0.8·FATIGUE_v) · (1 − 0.5·SCAN_v)
          / (1 + c_conf · KIN_DENSITY_v)
```

- **Cost:** the attacker pays c_strike·M_a whether the strike succeeds or not.
- **Success:** the target dies (cause: killed-by-species-of-a). Its body becomes carrion, with
  carrion mass = c_carc·M_v + its energy/ρ_meat. The attacker's gut is filled from that carrion at
  once, as meat (up to Gcap). The rest stays for scavengers.
- **Failure:** the target's Attacked memory is set to 1. If defence > power, the attacker also loses
  c_retal·(defence − power) energy, so armour plus defence can hurt a predator.

### 7.5 Costs and death

- **Energy cost per step:** B·(REST ? 0.85 : 1) + c_M·M·speed² + strike and mating costs.
- **Death causes:**
  - Energy ≤ 0: starvation.
  - Thirst ≥ 1: thirst.
  - Old age: Age > L, with probability 0.002 per step, rising with Age/L.
  - Standing in a burning cell: probability 0.3 per step.

### 7.6 Reproduction (NR-311)

- **Sexes:** two. Sex is assigned at birth from the hash.
- **Mating:** when MATE fires and a READY mate of the other sex is within reach, the female conceives.
  The male's matrix and body plan are copied into a per-female "sperm" slot, which is in the
  preallocated breed-request table.
- **Costs:** both pay c_mate·M. The female enters gestation (body-plan gene) and is not READY until
  her cooldown ends.
- **Birth:** at the end of gestation, the female gives birth to a litter of n (body-plan gene, ≥ 1)
  if free slots exist. Each newborn gets the energy fraction f_birth·Emax_child, paid by the mother.
  If she cannot pay, the litter is smaller.
- **Newborns:** each is placed within 2 units of the mother, with Mother = mother's serial, and a
  genome from §5.4.

Strays (NR-313), if enabled, appear when a species has fewer than `strayFloor` members. They are
mutated copies of the founder, placed on random land.

## 8. Vegetation and soil (NR-307 … NR-310)

Each 4×4-unit cell holds:
- grass G, shrub S, sapling P and tree T, each as cover 0..1 with G + S + T ≤ 1
- fruit F
- fuel U
- nutrient N
- moisture W
- burning flag/intensity B

### 8.1 Moisture and season

```
season(x,y,t) = sin(2π·t/T_season − 2π·y/FW)       (travelling wave, as in v1)
W = clamp(W_base(x,y) · (1 + a_s·season), 0, 1)
```

W_base comes from terrain: high near water and in valleys, low on ridges.

### 8.2 Growth (per step)

```
light_G = 1 − 0.85·T − 0.35·S
light_S = 1 − 0.70·T
grow    = W · N/(N + K_N)

G += r_G·grow·light_G·(G + s_G + σ·(Ḡ_nb − G))·(1 − G − S − T)
S += r_S·grow·light_S·(S + s_S·G)·(1 − G − S − T)              shrubs invade grass
P += r_P·grow·(T̄_nb + T)·(0.2 + S)·(1 − P − T) − m_P·P         seeds from trees; nurse shrubs
T += m_P·P·(1 − T) + r_T·grow·T·(1 − T) − d_T·T                 saplings mature, trees grow
S −= c_shade·T·S;   G −= c_shadeG·T·G                           canopy shades out the layers below
F += r_F·T·max(season, 0) − d_F·F                               fruit in the wet season
U += c_U·(G + S)·(1 − W) − d_U·U·W                              fuel accumulates when dry
N += weathering + carrion rot + dung + ash − c_N·(growth of G, S, T)
```

Here x̄_nb is the mean of the four neighbours. The fields double-buffer, and each row is computed
independently (NR-105).

### 8.3 Herbivory (NR-308)

- **Grazing:** an EAT on grass removes G directly.
- **Browsing:** an EAT on browse takes from S first. A browse bite also kills saplings: P −= 0.5·b.
  Heavy browsing therefore stops forest recruitment, the "browser holds back forest" effect.
- **Fruit:** an EAT on fruit takes F.

### 8.4 Fire (NR-309)

- **Ignition:** per cell, the probability is λ_fire·U·(1 − W)².
- **Spread:** a burning cell ignites a neighbour with probability c_spread·U_nb·(1 − W_nb).
- **Burning:** G = S = P = 0, T ×= 0.6, U = 0, N += c_ash·(burned biomass). B decays over 6 steps.
- **Animals:** an animal in a cell with B > 0 can die (cause: fire). Fire also appears in the FEAR
  feature as scent.

### 8.5 Carrion and scent

- **Carrion:** rots at c_rot per step. Of the rot, the fraction c_nutGain becomes N.
- **Scent:** each meat-eater deposits c_scent·carnivory per step into its cell (MARK adds ×4). Scent
  diffuses 10% to neighbours and decays at d_scent.

## 9. Terrain

Periodic fBm value noise produces the elevation and moisture base maps. This is the v1 method,
`makeTerrain`, generalised. Elevation is normalised so that the sea share is about 12%: lakes, not
an ocean, so that the land is connected. The lowest 3% of land basins are seasonal pools. Fertility
seeds the initial N, and the initial vegetation is a mosaic: grass everywhere, shrubs and trees where
W_base is high.

## 10. Telemetry, explanation and probe

**Counters** (NR-401) are 64-bit integers or doubles, incremented in RESOLVE and SPAWN, which are
serial, so they need no atomics.

**Samples** (NR-402) are taken every K = 32 steps into rings of S = 4,096. Each sample holds:
- per species: count, juveniles, births, deaths by cause, energy in per diet item, energy out per
  category, hunts attempted/succeeded, mean generation, mean |W − W_founder|, and the gain share of
  each of the 28 primitives
- global: vegetation covers, water, burning cells, carrion, scent
- a sub-sample of up to 64 (local food density, intake) pairs per herbivore species, for the
  functional response

**Explanation engine** (NR-403). It runs on the server thread, not the simulation thread. It compares
windows of the rings using fixed rules, for example:
> "Drifters fell 31% in 2,000 steps: 64% of deaths were Stalker kills; Stalker pack size rose from
> 3.1 to 4.4; grass is at 22% of land."

Each rule is listed in `Explain.cs` with the counters it reads.

**Probe.** For the selected serial, DECIDE also copies x (30), z (32), the gains and the action bits
into a probe record. The server sends it in the PROBE frame (`ICD.md`).

## 11. Server and console

The server has three parts:
- **Snapshot triple buffer.** After each step, if the server asked for a frame, the simulation thread
  writes the AGENTS snapshot into the free buffer and publishes it with one interlocked exchange. The
  sender always takes the newest buffer. A slow client simply skips frames (NR-506).
- **Rate scheduler.** It runs a target number of steps/s (default 30; 1×, 2×, 4×, max). Pause and
  single-step are supported.
- **Commands.** JSON over the same WebSocket: select, follow, speed, pause, step, set the rate or
  viewport.

The console is three.js plus uPlot, embedded:
- **3D world:**
  - terrain mesh and water
  - vegetation: instanced grass tufts, shrubs and trees, with density from the fields at 2 Hz
  - fire and smoke particles
  - carcasses and per-species low-poly models
  - interpolation between AGENTS frames
- **Cameras:** overview, follow-cam (on the animal's death it moves to its youngest living offspring,
  else the nearest kin) and ride-along (first person at eye height, the field-of-view cone, perceived
  animals outlined by class).
- **Ride-along HUD:** drives as bars, the live primitive-gain bars ("what it is doing"), the top three
  contributing features of the strongest primitive ("why"), and the matrix heatmap of this animal.
- **Dashboards:** as listed in NR-504.

## 12. Code organisation

```
src/Neuroreef.Core/
  Constants.cs     all tuning values (NR-106)
  Rng.cs           PCG hash; Uniform/Normal(seed, step, slot, stream)
  World.cs         every array (§3); construction = the only allocation
  Terrain.cs       fBm, lakes, pools, initial vegetation
  Genome.cs        founder matrices (§6), crossover, mutation (§5.4)
  Perception.cs    features (§5.1) using the spatial hash
  Behaviours.cs    primitive vectors (§5.2)
  Decide.cs        matrix evaluation, intents (§5.3)
  Resolve.cs       movement, eating, strikes, costs, death, mating (§7)
  Spawn.cs         births, strays
  Vegetation.cs    §8
  Telemetry.cs     counters, rings
  Probe.cs, Snapshot.cs, Explain.cs
  Simulation.cs    owns World and the worker pool; Step() runs phases 1–6
src/Neuroreef.Server/   Program.cs, SimHost.cs, Wire.cs (ICD encoders), wwwroot/
tests/Neuroreef.Tests/  Engineering, Behaviour (scenes), Ecology, Telemetry, Wire
```

## 13. Open questions for review

1. **Six species.** The redesigned web adds Foragers, an omnivore that also takes the young of others.
   Any other niche you want, such as an insect swarm, a bird or an aquatic species?
2. **Sexual reproduction.** I added two sexes and mate-finding, because it gives crossover and lets
   the MATE primitive matter. This was not on your list; it can be dropped back to asexual.
3. **World scale.** The default is 1,024 units and up to 32,768 animals, with ~15–20k expected
   alive. Larger is possible at a lower step rate.
4. **Founder compositions (§6).** These are my hand-designed starting points. Review whether each
   species' composition matches how you picture it.
