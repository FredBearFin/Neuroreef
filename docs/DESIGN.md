# NEUROREEF 2 — DESIGN DOCUMENT

Document NR-DD-001, Revision C. It implements NR-RQ-001 Revision C (`REQUIREMENTS.md`). The values of every
constant named here are in `CONSTANTS.md`, which is generated from the code.

**Changes from Revision B** (after review with the user, see §15)
- The laws of the world were reviewed once for physical consistency and are now frozen. No law is
  adjusted to produce an ecological outcome; outcomes are observed and reported.
- Body traits evolve too, with costs (§7.5).
- Founders and colonists arrive in groups (§7.4).
- Growth and offspring cost energy; newborn size is a per-species life-history trait (§8, §9.1).

**Changes from Revision A**
- The food web is now derived from the energy cycle: 20 species across land and freshwater.
- Reproduction differs by species.
- There are **no designed behaviours**: brains start random, learn during life and evolve across
  generations.
- Small life is modelled as super-individuals.

---

## 1. The idea in one page

Neuroreef is an ecosystem in which **only the rules of physics and physiology are designed**.
Behaviour is not designed.

1. **Energy cycle.** Sunlight, water and nutrient feed producers (vegetation fields). Producers feed
   herbivores. Herbivores feed predators. Everything that dies, sheds or excretes becomes a dead
   pool. Detritivores, fungi and bacteria return the dead pools to soil and water as nutrient, which
   feeds the producers again. The species roster (§6) was chosen by asking, for every pool in this
   cycle, "who eats this?". It was checked by asking, for every species, "what does it eat, and how
   does it reproduce?". The program repeats that check at start-up (§6.4).
2. **Bodies, not behaviours.** A species is a *body plan*:
   - mass, realm, locomotion and diet
   - armour, strike and senses
   - reproduction mode and life stages

   Nothing in a species definition says what it should *do*.
3. **One set of senses, one set of actions.** Every animal has the same 30 senses (§4) and the same
   28 primitive actions (§5). Every animal has the same brain architecture (§7).
4. **Brains start random, in a prehistory.** In a *prehistory* run every brain starts random. Animals then learn during
   life from a universal reward (relief of hunger, thirst and fatigue, and the success of mating
   and birth, minus pain), and evolution selects the brains that learn and act well. Grazing herds,
   vigilance, hunting, scavenging and following the mother may or may not emerge. The console
   measures which compositions of primitives each species actually uses (§10.3). It does not claim
   them.
5. **The world you watch is births and deaths only.** No animal ever appears in it except by being
   born from a parent, so an extinct species stays extinct. Its first animals are seeded once, at step
   0, with brains from a gene bank saved by a prehistory run (`--evolve` / Evolve.bat), which stands
   for the long evolution before the world begins. Only the prehistory uses colonists (§7.4).

## 2. System overview

```
+---------------------------- Neuroreef.exe ----------------------------+
|  SIM THREAD                         SERVER (Kestrel, port 5800)        |
|  Simulation.Step()  -- phases 1..7  Snapshot triple buffer ---------> WebSocket -> browser
|  World: all state, preallocated     Explain engine, commands  <------ (three.js 3D, dashboards)
|  Worker pool (fixed N threads) for the parallel phases                 |
+------------------------------------------------------------------------+
```

Headless mode runs the same core with no server and writes reports, CSV files and gene banks.

## 3. World and memory map

The world is a torus of WORLD = 1024 units. The field grid has cells of 4 units (FW = 256, 65,536
cells). The spatial hash has cells of 16 units (GW = 64). C = capacity = 65,536 agent slots by
default (`--capacity`). Eggs and embryos occupy slots too. When every slot is in use, births and
immigrants are refused and counted (`SlotShortage`); the console shows **WORLD FULL**, because that
is a limit of the computer, not an ecological result.

| Block | Type | Per slot or cell | Total (default) |
|---|---|---|---|
| Kinematics X, Y, Heading, Speed | float | 4 | 1.0 MB |
| Physiology Energy, Gut, GutEnergy, GutWaste, GutSeedy, Thirst, Fatigue, Age, Pain, Cooldown, Timer, Reward, RewardMean, PendingR, LastFear | float | 15 | 3.9 MB |
| Identity Species, Stage, Sex, Flags (byte), Gen (ushort), Serial, Mother, Children, last child (int) | mixed | — | 1.6 MB |
| Phenotype (expressed from body genes at birth): 15 floats + litter + 2 × 15 diet efficiencies | float | 46 | 12 MB |
| Genome (W1, W2, plasticity genes, body genes) | sbyte | 1,705 | 112 MB |
| Working output weights W2f (genome + learned) | float | 1,176 | 308 MB |
| Eligibility traces on W2 | float | 1,176 | 308 MB |
| Learning block history (4 steps of δ, y, A) | float | 284 | 74 MB |
| Hidden state, last features, last gains | float | 70 | 18 MB |
| Intents | mixed | — | 2.5 MB |
| Spatial hash, free stack | int | — | 0.8 MB |
| Fields: 13 food pools, shrub, tree, sapling, nutrient, scent, moisture, pollination, fire (×2 where double-buffered) | float | 31 | 8.1 MB |
| Static terrain: elevation, depth, moisture base, fertility, realm, distance-to-water, distance-to-land | float/byte | 7 | 1.6 MB |
| Elite archive (20 species × 32 genomes) | sbyte | — | 1.1 MB |
| Telemetry ring (4,096 samples × 1,621 floats) | float | — | 26.6 MB |
| Snapshots (×3) | byte | 12 | 2.4 MB |

The total is about 0.9 GB at the default capacity and scales linearly with `--capacity`
(`--capacity 32768` halves it).

## 4. Senses: the shared feature vector (30 entries)

These are identical for every animal (NR-201). Other animals are seen only through observable
attributes (NR-202), within the sight range R and field of view Φ. SCAN widens Φ to 360° and R by
30% on the next step. "Closeness" of an animal at distance d is 1 − d/R. "Density" is
tanh(Σ closeness). The neighbourhood examined is capped at 96 animals per step, taken in
spatial-hash order.

**Self and drives (0–9)**

| i | Name | Meaning |
|---|---|---|
| 0 | BIAS | 1 |
| 1 | HUNGER | 1 − Energy/Emax |
| 2 | THIRST | Thirst (0..1, death at 1) |
| 3 | FATIGUE | 0..1 |
| 4 | READY | 1 if able to reproduce now (§8) |
| 5 | AGE | Age/lifespan |
| 6 | JUVENILE | 1 before maturity |
| 7 | FULL | Gut/Gcap |
| 8 | PAIN | recent damage received, decays |
| 9 | CARRYING | 1 while gestating embryos |

**Others (10–21)**

| i | Name | Meaning |
|---|---|---|
| 10 | KIN_DENSITY | conspecifics |
| 11 | KIN_CLOSE | nearest conspecific |
| 12 | MOTHER_FAR | 1 − closeness of mother; 0 if none or adult |
| 13 | MATE_CLOSE | nearest READY conspecific that is a valid partner |
| 14 | BIG_CLOSE | nearest heterospecific more than 15% heavier |
| 15 | BIG_DENSITY | density of those |
| 16 | BIG_APPROACH | closing speed of nearest big / own vmax, −1..1 |
| 17 | BIG_CARN | carnivory trait of the nearest big (visible attribute) |
| 18 | SMALL_CLOSE | nearest heterospecific or egg more than 15% lighter |
| 19 | SMALL_DENSITY | density of those |
| 20 | SMALL_WEAK | vulnerability of nearest small: egg, juvenile, fatigued, low armour |
| 21 | CARN_CLOSE | nearest heterospecific meat-eater of any size |

**Environment (22–29)**

| i | Name | Meaning |
|---|---|---|
| 22 | FOOD_HERE | value of the best digestible food in own cell: max over pools of ρ·eff·amount, normalised |
| 23 | FOOD_GRAD | strength of the gradient of digestible food value |
| 24 | IN_WATER | 1 if standing or swimming in water |
| 25 | WATER_CLOSE | closeness of the nearest water (distance map) |
| 26 | LAND_CLOSE | closeness of the nearest land |
| 27 | FEAR | scent of meat-eaters here, plus fire nearby |
| 28 | CROWD | total animal density, any species |
| 29 | SEASON | sin of the season phase at own position (−1 dry … +1 wet) |

Smelling digestible food (features 22–23, primitive CLIMB_FOOD) is physiology. A body's senses are
tuned to what its gut can digest, the way taste is. It says nothing about when to eat, or whether
eating beats fleeing; that is the brain's job.

## 5. Actions: the shared primitive library (28 entries)

Identical for every animal (NR-203).

**Steering primitives** each return a unit vector, or zero if their target is absent:

| p | Name | Vector |
|---|---|---|
| 0 | SEEK_SMALL | toward nearest small |
| 1 | SEEK_BIG | toward nearest big |
| 2 | SHADOW_CARN | toward nearest meat-eater beyond R/2, tangential inside R/2 |
| 3 | FLEE_BIG | away from nearest big |
| 4 | FLEE_CARN | away from nearest meat-eater |
| 5 | INTERCEPT_SMALL | toward predicted position of nearest small |
| 6 | CIRCLE_SMALL | perpendicular to its bearing, on the side away from kin |
| 7 | COHERE | toward kin centroid |
| 8 | SEPARATE | away from kin closer than 2 units |
| 9 | ALIGN | mean kin heading |
| 10 | FOLLOW_MOTHER | toward mother |
| 11 | SEEK_MATE | toward nearest valid partner |
| 12 | CLIMB_FOOD | up the gradient of digestible food value |
| 13 | SEEK_WATER | down the distance-to-water map |
| 14 | SEEK_LAND | down the distance-to-land map |
| 15 | AVOID_FEAR | down the scent gradient |
| 16 | WANDER | correlated random walk (hashed noise) |

**Speed and attention primitives**

| p | Name | Effect |
|---|---|---|
| 17 | HOLD | slows the animal |
| 18 | SCAN | stops the animal; Φ = 360°, R ×1.3 next step; forbids EAT this step |
| 19 | SPRINT | toward vmax; builds fatigue |

**Discrete actions** have a precondition. If the precondition fails, the action has no effect.

| p | Name | Precondition |
|---|---|---|
| 20 | EAT | digestible food here; gut not full; not scanning |
| 21 | STRIKE | a heterospecific within reach (smaller preferred); not an egg or embryo |
| 22 | DRINK | land-stage animal; water within reach |
| 23 | MATE | READY; valid partner READY within reach; mode allows sex now (§8) |
| 24 | REST | — |
| 25 | MARK | — (deposits scent) |
| 26 | LAY | READY; mode allows asexual reproduction now (fission, budding, spores, parthenogenesis; §8) |
| 27 | CACHE | gut holds seeds; stores them into the cell as a cache (seed pool) |

**From gains to motion**
- **Heading:** the steering sum s = Σ g_p·v_p (p ≤ 16) sets the desired heading. The turn is limited
  by the turn rate.
- **Speed:**
  `vmax·clamp(0.45 + 0.55·g_SPRINT − 0.45·g_HOLD, 0, 1)·(1 − g_SCAN)·(1 − 0.5·FATIGUE)`.
  Speed is 0 when resting or for sessile bodies.
- **Firing:** a discrete action fires **stochastically, with probability equal to its gain**. That
  randomness is the exploration that learning needs (§7.2).

## 6. The energy cycle and the roster

### 6.1 Pools

| Pool | Kind | Produced by | Lost by |
|---|---|---|---|
| GRASS | producer, land | growth (light, moisture, nutrient), spread | grazing, fire, shading, senescence → LITTER |
| BROWSE | producer, land | shrub and tree leaf growth | browsing, fire, senescence → LITTER |
| FRUIT | producer, land | trees in the wet season, × pollination | eating, rot → LITTER |
| SEED | producer, land | grass and trees, × pollination; caches | eating, germination into grass/saplings, rot |
| NECTAR | producer, land | flowering grass/shrubs in the wet season | eating (pollinates the cell) |
| ROOT | producer, land | grass and shrub biomass below ground | rooting, fire (partial) |
| ALGAE | producer, shallow water | growth (light, water nutrient) | grazing, death → DETRITUS |
| PHYTO | producer, open water | growth (water nutrient, season) | grazing, death → DETRITUS |
| CARRION | dead | death of animals; uneaten kills | scavenging, rot → NUTRIENT |
| DUNG | dead | undigested food of every animal | detritivores, rot → NUTRIENT |
| LITTER | dead | plant senescence | detritivores, fungi, rot, fire |
| WOOD | dead | tree death, fire | fungi, slow rot |
| DETRITUS | dead, water | algae/plankton death, carcasses in water, run-off | detritivore larvae, rot → NUTRIENT |
| MEAT | animal bodies | every animal | predation |
| BLOOD | animal bodies | every large animal | parasitism (drain without killing) |

Decomposition by bacteria is a field process. Every dead pool rots at its own rate into NUTRIENT
(soil or water), and nutrient diffuses across the shore as run-off. NUTRIENT feeds every producer,
which closes the cycle.

### 6.2 Roster

The roster is 20 species. **S** marks a super-individual: one agent stands for many organisms (a
swarm, a plankton patch, a fungal mat), and its mass is the aggregate.

| # | Species | Niche | Realm (stage) | Food | Reproduction |
|---|---|---|---|---|---|
| 0 | Drifters | small grazer | land | GRASS | sexual, live birth |
| 1 | Browsers | large browser | land | BROWSE, FRUIT | sexual, live birth, 1 calf |
| 2 | Tuskers | rooting omnivore | land | ROOT, FRUIT, MEAT (small), CARRION | sexual, live birth |
| 3 | Gliders | frugivore, seed disperser | land, flies | FRUIT, SEED | sexual, eggs; dung plants saplings |
| 4 | Burrowers | granivore, cacher | land | SEED | sexual, live birth, large litters |
| 5 | Hummers (S) | pollinator swarm | land, flies | NECTAR | asexual colony fission |
| 6 | Chewers (S) | leaf-insect swarm | land, flies | GRASS, BROWSE | facultative: parthenogenesis when food is rich, sexual when poor |
| 7 | Delvers (S) | detritivore (worms/beetles) | land | DUNG, LITTER | hermaphrodite |
| 8 | Rotmats (S) | fungal mat, sessile | land | WOOD, LITTER, CARRION | spores |
| 9 | Gleaners | vertebrate scavenger | land | CARRION | sexual, live birth, litters of 6 |
| 10 | Snappers | insectivore | land, flies | MEAT (insect swarms, eggs) | sexual, eggs |
| 11 | Prowlers | solitary mesopredator | land | MEAT | sexual, live birth |
| 12 | Stalkers | pack hunter | land | MEAT | sexual, live birth |
| 13 | Leviathans | apex predator | land + shallows | MEAT, CARRION | sexual, live birth, 1 young |
| 14 | Bloodflies (S) | parasite | larva: water; adult: flies | larva: DETRITUS; adult: BLOOD, NECTAR | sexual, eggs in water |
| 15 | Siltmites (S) | zooplankton | water | PHYTO | cyclic parthenogenesis: asexual in the wet season, sexual resting eggs in the dry |
| 16 | Minnows | grazing fish | water | ALGAE, MEAT (Siltmites, eggs) | sexual spawning, many eggs |
| 17 | Pikes | predatory fish | water | MEAT | sexual spawning |
| 18 | Croakers | amphibian | tadpole: water; adult: land + shallows | tadpole: ALGAE; adult: MEAT (insects) | sexual, eggs in water, metamorphosis |
| 19 | Herons | wading bird | land + shallows, flies | MEAT (fish, tadpoles, larvae) | sexual, eggs |

**Who eats whom is not a table.** "MEAT" means the species can digest meat. What it can catch follows
from its realm, size, strike and the prey's armour (§9.4). The killer × victim matrix in the
analytics shows what actually happens (NR-305).

**Links between land and water**
- Herons, Leviathans and adult Croakers enter shallows.
- Croakers and Bloodflies change realm at metamorphosis.
- Carcasses in water and nutrient run-off feed DETRITUS and water NUTRIENT.

**Mutualisms are rules** (NR-317)
- When a Hummer eats nectar, the cell's POLLINATION level rises. Pollination multiplies FRUIT and SEED
  production.
- Dung of fruit and seed eaters seeds SAPLINGs in proportion to the seed share of their gut.

### 6.3 Body plans

Constants are in `CONSTANTS.md` §7. Each body plan holds:
- adult mass, realm per stage, locomotion (walk, swim, fly, sessile)
- the diet efficiency vector per stage (15 pools)
- armour, strike power, the parasite flag, the super-individual flag
- sight factor, field of view, speed factor, thirst factor
- reproduction mode, litter size, gestation/incubation, cooldown, egg medium (land/water)
- metamorphosis age (if two-stage)
- initial count

### 6.4 Energy-cycle closure check (NR-315, NR-316)

At start-up, and in a test, the program checks four things:
1. **Every pool has a consumer:** at least one species, at some stage, has efficiency > 0 for it.
   MEAT must be digestible by some species able to reach each realm. Dead pools additionally have
   rot.
2. **Every species can eat:** each stage has at least one pool with efficiency > 0 that exists in
   that stage's realm.
3. **Every species can reproduce:** each species has a reproduction mode. Egg-laying species have
   an egg medium they can reach.
4. **Every producer's nutrient is returned:** every pool's loss path ends in NUTRIENT, so no energy or
   nutrient is stranded.

The program refuses to start if the check fails, and prints the offending entries.

## 7. Brains

### 7.1 Architecture

The architecture is identical for every species (NR-204):

```
input  u = [features f (30), hidden h(t−1) (12)]                      42 values
hidden h(t) = tanh(W1 · u)                         W1: 12 × 42          (genome, fixed in life)
output z    = W2f · [h(t) (12), f (30)]            W2f: 28 × 42         (genome W2 + what was learned)
gain   g_p  = σ(z_p)
```

- The output layer sees the hidden state and also the raw features. These skip connections let a
  simple sense-to-action link be learned directly.
- The recurrent hidden layer gives memory and lasting states, such as alarm (NR-208).
- Genome weights are int8 with a scale of 1/16 (±7.9).
- There are 504 + 1,176 = 1,680 weights, plus 4 bytes of plasticity genes (the learning rate η, the
  trace decay λ, the exploration noise σ, the pull back to the genome κ) and 21 body genes (§7.5).
- `tanh`, `exp` and the trigonometric functions are computed by managed code (`MathK.cs`), so a run
  gives the same numbers on Windows and Linux, and the vector code avoids AVX-SSE transition costs.

### 7.2 Lifetime learning: reward-modulated plasticity (NR-211)

Learning uses node perturbation (Williams 1992; Fiete and Seung 2006), a three-factor rule:

```
Steering and speed gains:   ĝ_p = clamp(g_p + σ·n_p, 0, 1),  n_p ~ N(0,1) (hashed)
Discrete actions:           fired_p ~ Bernoulli(g_p)
Perturbation:               δ_p = ĝ_p − g_p        (or fired_p − g_p)
Trace:                      e_pj ← λ·e_pj + δ_p · y_j      y = [h, f]
Reward advantage:           A = r − r̄,   r̄ ← r̄ + 0.01·(r − r̄)
Update:                     W2f_pj ← W2f_pj + η·A·e_pj,   |W2f| ≤ 7.9
Pull back (every 32 steps): W2f ← W2f + 32κ·(W2_genome − W2f)
```

**Block update.** Applying the update every step reads and writes about 19 KB per animal, which made
the simulation memory-bound. The perturbations δ, inputs y and advantages A of 4 steps are stored and
applied in one pass (the sums are expanded exactly, `Brain.ApplyBlock`). The result equals the
per-step rule except that the weights used by the forward pass are up to 3 steps old. Blocks are
staggered by slot.

The reward r is **the same rule for every animal** (it is drive reduction, not behaviour):
```
r = 4·intake/Emax + 2·(thirst relieved) + 0.5·(fatigue relieved)
    + 1·(mated) + 2·(offspring born) − 4·(damage received)/Emax − 0.5·Δ(FEAR)⁺
```

Learning changes only the working weights W2f, never the genome. W2f starts equal to the genome at birth. What an animal
learns is not inherited, but evolution selects η, λ, σ and κ, and the starting weights that make
learning work.

### 7.3 Inheritance (NR-205)

- **Sexual:** row-wise uniform crossover of W1 rows and W2 rows from the two parents.
- **Asexual:** a copy of the single parent.
- **Mutation** (both cases): each byte changes with probability μ = 0.02 by round(2·N), or, with
  probability μ·0.05, by round(16·N). The plasticity genes and body genes mutate by the same rule
  (a body-gene step of 2 changes a trait by about 1%).
- **Species identity** (locomotion, realm, reproduction mode, life stages) is fixed; body traits drift
  within their ranges (§7.5).

### 7.5 Evolvable bodies (NR-320)

The genome ends with 21 body genes, each a signed byte b. A gene is a log-scale multiplier on the
species' founder value: factor = range^(b/127), so b = 0 is the founder body plan. The phenotype is
expressed once, when the slot is created (`Body.Express`). Every trait has a cost, so evolution trades
them off:

| Gene | Range | Effect | Cost |
|---|---|---|---|
| mass | ×/÷ 2 | adult mass; all allometry follows (§9.1) | larger basal cost, gut, slower maturity, longer life |
| speed | ×/÷ 1.6 | top speed | basal × (1 + 0.4·(f − 1)) |
| sight | ×/÷ 1.6 | sight range (≤ 32) | basal × (1 + 0.15·(f − 1)) |
| armour | ×/÷ 1.6 | armour | basal × (1 + 0.2·(f − 1)); speed ÷ (1 + 0.2·(f − 1)) |
| strike | ×/÷ 1.6 | strike power | basal × (1 + 0.2·(f − 1)) |
| litter | ×/÷ 1.6 | offspring per litter | the founder's brood energy is shared among them |
| diet × 15 | ×/÷ 3 | efficiency per pool | each stage keeps the founder's total digestive budget: raising one pool lowers the others |

A pool the founder cannot digest can be gained up to 0.15 (before renormalisation) at the gene's
maximum, if that pool exists in the stage's realm. A lineage can therefore drift into a neighbouring
niche (a scavenger that hunts, a browser that grazes) without any species being redesigned.

### 7.4 Prehistory and the live world (NR-210, NR-212, NR-313)

**Why there are two kinds of run.** A random brain cannot keep an animal alive long enough to raise
young, so a world started from random brains with no help empties in its first generation. Real
animals never start from random brains: their brains were shaped before any one of them was born.
So evolution from random brains runs in a separate, labelled **prehistory** (§7.6), where
colonists keep species going while brains evolve, and saves its result as a gene bank. The **live
world**, the one shown in the console, is seeded once from that bank and is then births and deaths
only. Its code path never reaches the colonist code (`World.Prehistory` is false), and a test kills a
whole species and checks that it never returns. (An earlier version ran colonists in the console
world too, which meant that the animals being watched were mostly spawned. That was wrong for a life
simulation and has been removed.)

The rest of this section describes the prehistory.

**Founders** are random, by the same rule for every species. Memory-layer weights are
round(U(−1,1)·FOUNDER_W)/16. Output weights are small, round(U(−1,1)·FOUNDER_W2)/16, and the output
bias column starts at FOUNDER_BIAS/16 = −3 (± FOUNDER_BIAS_NOISE): a **calm newborn brain**, in
which every primitive starts near 5% and useful actions must be learned or evolved upward. (The
first prehistory runs used large random weights, so every action fired about half the time, and the
energy spent on constant strikes and mating attempts killed most animals young.) The plasticity
genes are drawn uniformly from their ranges; body genes start at 0, the species body plan. There is
no hand wiring, and tests assert that founder genomes depend only on the RNG and that founder gains
start low.

**Groups.** Real populations are clustered, and a lone colonist cannot find a mate (the Allee
effect; the first prehistory runs showed every sexual species stuck at its floor while colonists were
scattered uniformly). Founders and colonists therefore arrive in groups of GROUP_SIZE = 8 within
GROUP_SPREAD = 10 units of one site.

**Colonists (prehistory only).** During the first PREHISTORY steps (default 300,000), any species below its floor
receives one colonist group per step on suitable cells (its realm):
- **Breeding from the best.** Each species keeps an **elite archive** of ARCHIVE = 64 genomes. A
  genome enters it when its owner dies, if it beats the weakest. The score counts offspring that
  grew up most: SCORE_MATURED·(offspring that reached maturity) + offspring conceived + 0.1·(age /
  lifespan).
- Once the archive holds 4 genomes, ARCHIVE_SHARE = 90% of colonists are bred from it. Parents are
  chosen by tournament (best of TOURNAMENT = 3 random entries). A colonist of a sexual species is a
  crossover of two winners; an asexual one is a copy. Both are then mutated.
- The other 10% are new random brains, which keeps variation.
- A colonist group lands next to a living adult of its species if one exists (sampled each step),
  so successful genes meet each other.

The floor ramps linearly from PREHISTORY_FLOOR = 25% of the species' initial count to 0 over the prehistory. Colonists are counted separately from births. After the colonist period the prehistory continues without them.

**Gene bank.** The file holds, per species, the elite archive and up to 96 living genomes. Format
(little-endian):
1. Header: magic `NRBK`, version u16, species count u16, genome length u16, capacity u32, step u64,
   seed u32.
2. Then for each species: count u16, followed by count × (score f32, genome bytes).

A run started with `--load-bank` seeds each species from its bank genomes (mutated) instead of
random ones. A live world never has colonists, with or without a bank.

### 7.6 The prehistory run: branches, settings, and the start of the world (NR-323 … NR-326)

**Where it runs.** When `Neuroreef.exe` starts without evolved brains (no `evolved.nrbank`, no shipped
bank), the console opens on the prehistory: it runs live, at full speed, and can be watched in 3D.
`--evolve` (Evolve.bat) runs the same thing without the console. `--prehistory` forces a new one.

**Branches (an island model).** BRANCHES = 3 separate populations evolve side by side on the same land
(the same terrain seed, different run seeds), each on its own thread and each with BRANCH_CAPACITY
slots. Separate populations explore different solutions; every MIGRATE_EVERY = 20,000 steps the
MIGRANTS = 4 best archive genomes of each species are copied from each branch into the next one's
archive, so a good solution found in one can spread. Branches meet at a barrier every 500 steps, so
they stay in lockstep; settings changes, pauses and migrations happen only there, while no branch is
stepping, which keeps every branch deterministic for a given sequence of settings.

**Length.** PREHISTORY_STEPS = 500,000 steps, shown as YEARS_PER_STEP = 1,000 symbolic years per step
(500 million years). The years are a label for the console, not a time scale of the model: one step
is still one step of the same laws. Colonists arrive until PREHISTORY_COLONISTS = 400,000; the last
100,000 steps are births only in every branch, so the prehistory ends with a test of whether each
species can live without help.

**Settings (EvoSettings).** The console's Prehistory tab can change, while it runs: mutation rate and
size, share of big jumps, the share of colonists bred from the archive, the tournament size, the
colonist floor, migration interval and count, and when colonists stop and the prehistory ends. These
are knobs of the evolutionary search, not laws of the world (§15): they change how genomes vary and
are selected, never what happens to a living animal. They are clamped to ranges where the search
still works.

**Standing.** For each species in each branch, over the last 32 samples (~1,000 steps):
self-sustaining (alive; births at least 90% of deaths and at least twice the colonists), breeding
(births, but not replacing itself), propped (alive only through colonists or unbred founders), or
gone.

**The start of the world.** At the end (or when "Begin the world" is pressed), the branches stop and
each species' gene bank is taken from the branch where it stands best (then by numbers alive). The
bank is saved as `evolved.nrbank` beside the exe, so the next start skips the prehistory, and the live
world is created from it. From that moment there are births and deaths only.

### 7.7 Reflexes and the starter kit (Revision C, D; NR-327)

Real newborns arrive with innate responses that learning cannot erase. Every animal has the same nine
reflex inputs, each a need, a need met by an opportunity, or a danger:
1. hungry × food here
2. hungry
3. thirsty
4. thirsty × at water
5. tired × safe
6. large carnivore near
7. young × far from mother
8. ready × mate near
9. hungry × weak prey near

Reflex genes R (int8, 28 × 9, appended to the genome) link each input to each primitive:
`z_p += RSCALE · Σ_c R[p, c]·c`. Learning never changes them. Rows are inherited whole from one parent
and mutate like every gene.

**Starter kit (Revision D).** The full 500,000-step prehistory of Revision B (3 branches) ended with
every species extinct within 100,000 steps of the last colonist. The life tables showed animals dying of
thirst and starvation beside water and food: evolution from random wiring never found the basic
survival skills in a few hundred generations, which real animals inherit from hundreds of millions of
years of evolution. So founders now carry an innate starter kit, the same for every species: eight
reflex genes set to KIT_W (+ the usual small random spread):
- hungry × food here → EAT
- hungry → CLIMB_FOOD
- thirsty → SEEK_WATER
- thirsty × at water → DRINK
- tired × safe → REST
- large carnivore near → FLEE_CARN and FLEE_BIG
- young × far from mother → FOLLOW_MOTHER

At full need a kit reflex gives about 88% (5 against the calm bias of −3). This is the one designed
behaviour in the model, chosen by the user on 2026-10-28. Everything above it stays emergent: what to
eat, herding, hunting, mating, timing. The kit genes are ordinary genes, so evolution can strengthen,
weaken or lose them. All other reflex links start small and random.

### 7.8 Parental care (Revision C, NR-328)

Mammals (live birth) suckle their young, and birds (eggs on land, flying parents) feed their chicks.
Fish, amphibians, insects and fungi give no care. This is a species-level rule from the body plan,
`Care` = 1 or 0, and a heritable care gene scales it (×/÷ BODY_RANGE_TRAIT).

A mother in contact with her young (within the sum of their reaches) before weaning (age < WEAN ×
time to maturity) gives them energy each step:
- up to CARE_RATE × care × her basal cost per step, shared by all her young;
- never more than they can hold;
- never taking her own reserve below CARE_RESERVE × Emax.

She pays the energy given divided by CARE_EFF, counted as reproductive spending, so energy is
conserved. The young taste it as food (reward), so following the mother can be learned and evolved;
FOLLOW_MOTHER and MOTHER_FAR already exist.

**Genome (Revision D).** After the Revision B layout (1,705 bytes) come B_CARE (1 byte) and the reflex
genes (252 bytes): G.LEN = 1,958. A Revision B gene bank still loads, with these genes at 0 (species
care level, no reflexes).

## 8. Reproduction modes (NR-311, NR-316)

**READY** means all of these hold:
- mature
- Energy > 0.6·Emax
- the cooldown is over
- not carrying embryos

**Valid partner:**
- for a sexual species, a READY conspecific of the other sex
- for a hermaphrodite, any READY conspecific

**Offspring at conception.** Offspring are created when they are conceived. Each takes a free slot,
in one of two stages:
- **EMBRYO:** live birth. It moves with its mother, cannot be seen or eaten, and dies if she dies.
- **EGG:** laid at once in the parent's cell. If the egg medium is water, the parent must be in or
  beside water. An egg is immobile, has no brain and can be eaten.

**Costs** (energy is conserved: a body is never free):
- **Reserve:** the parent pays the newborn's starting reserve F_BIRTH·Emax_child at conception.
- **Tissue:** building the newborn's tissue costs C_CARC·m_birth·ρ_meat / GROWTH_EFF. An egg carries
  it as yolk, so egg-layers pay it at laying. A live-bearing mother pays it step by step during
  gestation; a mother whose reserve falls below 10% loses the embryo (cause: lost before birth).
- **Newborn mass** m_birth is a per-species life-history trait (`Birth`, CONSTANTS §species): 15% of
  adult mass for a Drifter calf, 1% for a Pike or Croaker egg.
- If the parent cannot pay, the litter is smaller.

**Hatching.** After the gestation or incubation timer ends, the offspring hatches as a JUVENILE.

| Mode | Action | Rule |
|---|---|---|
| SEXUAL | MATE | partner of other sex; the female conceives a litter; each child is a crossover of both parents |
| HERMAPHRODITE | MATE | any partner; *both* conceive (each a litter of ⌈n/2⌉) |
| FISSION | LAY | splits into two: the child takes 45% of energy and a mutated copy of the genome; it appears adjacent as a juvenile |
| FACULTATIVE | LAY / MATE | LAY (parthenogenetic, asexual litter) only when FOOD_HERE > 0.5, otherwise MATE |
| CYCLIC | LAY / MATE | LAY only when SEASON > 0 (wet). In the dry season, MATE gives *resting eggs* that hatch only when SEASON turns positive (or after a maximum timer) |
| SPORES | LAY | the mat emits a spore (EGG stage, incubation) into the best of 4 hashed cells within 3 cells, by dead matter |

**Two-stage species** (Bloodflies, Croakers) switch realm, locomotion and diet at metamorphosis age.
Juveniles grow from M·Birth to M by maturity, so calves are small and a big predator sees them as
SMALL. Growth costs energy (§9.1).

## 9. Physiology and physical rules

### 9.1 Allometry (NR-301)

| Quantity | Rule |
|---|---|
| basal cost per step | c_B·M^0.75 (×0.85 resting) |
| reserve Emax | c_E·M |
| gut capacity | c_G·M |
| digestion per step | c_D·M^0.75 |
| vmax | c_V·M^0.17·k_speed (swim and fly have their own factors) |
| turn rate | c_τ·M^−0.2 |
| lifespan | c_L·M^0.25·k_life |
| maturity | c_A·M^0.25·k_mat: the fastest time to adult size, with food unlimited |
| sight R | min(32, c_R·M^0.2·k_sight) |
| reach | 1 + 0.4·M^(1/3) |
| movement cost | c_M·M·speed² |
| fatigue | +c_F·g_SPRINT·speed/vmax; −c_rec (×3 resting) |
| thirst | +c_θ·k_thirst per step (0 for aquatic stages and sessile mats) |
| growth | a juvenile spends min(energy above GROW_RESERVE·Emax, C_CARC·M·(1 − Birth)/A_m·ρ_meat/GROWTH_EFF) per step on tissue and matures on reaching adult mass (Revision D, §15) |
| reserve | Emax = C_EMAX·M with C_EMAX = 2: a stored reserve of the same order as tissue energy |

### 9.2 Movement and realm

- The heading turns toward the desired heading by at most the turn rate.
- A move into a cell outside the stage's realm is refused. The animal stays where it is and turns by
  ±90° (hashed).
- Realms:
  - land walkers: land cells only
  - swimmers: water cells only
  - waders: land, or water with depth < shallow
  - flyers: anywhere, but cannot rest, eat or lay over deep water
- Sessile mats have speed 0. They grow only by spores.

### 9.3 Gut, digestion and intake (NR-302)

- **Choosing the food:** EAT takes the pool with the highest ρ_k·eff_k·amount in the cell.
- **The bite:** b = min(c_bite·M^0.75, amount, Gcap − Gut).
- **Into the gut:** Gut += b, and GutEnergy += b·ρ_k·eff_k. The undigested part, b·(1 − eff_k),
  becomes DUNG.
- **Digestion:** each step, d = min(Gut, c_D·M^0.75) is digested, moving the proportional share of
  GutEnergy into Energy.

A full gut blocks EAT, so the intake rate saturates (Holling type II) from handling time alone.

### 9.4 Strikes, kills and parasitism (NR-304)

**Target:** the nearest heterospecific within reach. Smaller and eggs come first; larger only if
there is no smaller (mobbing and defence).

**Success probability:**
```
power   = strike_a · M_a
defence = armour_v · M_v · (juvenile_v ? 0.25 : 1)       eggs: defence 0
p = p0 · power/(power + defence) · (1 + 0.8·FATIGUE_v) · (1 − 0.5·SCAN_v) / (1 + c_conf·KIN_DENSITY_v)
```

**Cost:** the attacker always pays c_strike·M_a.

**Outcomes:**

| Case | Result |
|---|---|
| Normal success | The victim dies (cause: killed-by-attacker-species). The attacker eats immediately as MEAT, up to its gut space, with efficiency eff_meat. The rest of the body becomes CARRION in the cell. |
| Super-individual victim | It loses a chunk, min(attacker gut space, 30% of its energy), but it dies only if its energy falls below 5% of Emax. |
| Parasite attacker | It drains BLOOD, min(c_drain·M_a, host energy), and the host takes it as damage. The host dies only if its energy reaches 0 (cause: parasitism). |
| Failure | The victim's PAIN rises (it was attacked). If defence > power, the attacker loses c_retal·(defence − power) energy. |

### 9.6 Disease (Revision C, NR-329)

Each species has its own pathogen, which it catches from infected conspecifics in contact (within the
sum of their reaches, counted in DECIDE).
- **Infection:** p = 1 − (1 − DIS_BETA)^contacts · (1 − DIS_SPILL) per step. DIS_SPILL is rare
  spillover from the environment.
- **Course:** infection lasts DIS_STEPS. The animal pays DIS_COST extra basal cost (fever) and feels
  DIS_PAIN as malaise, so it can learn to rest. It dies with probability DIS_MORT·(1 + hunger) per step,
  recorded as death by "disease".
- **Recovery:** gives immunity for DIS_IMMUNE steps.

With DIS_BETA·DIS_STEPS = 0.5 per steady contact, an outbreak needs crowding: transmission is
density-dependent, so disease checks booming populations and fades in sparse ones. Telemetry records
the number infected, new infections and deaths per species.

### 9.5 Death

| Cause | Condition |
|---|---|
| starvation | Energy ≤ 0 |
| thirst | Thirst ≥ 1 |
| killed by species X | a successful strike (§9.4) |
| parasitism | drained to Energy 0 |
| old age | Age > lifespan, with probability 0.002·(Age/lifespan)² per step |
| fire | standing in a burning cell, probability 0.3 per step |
| drowning / stranding | none; realm rules forbid it |

On death, the body becomes CARRION in land cells or DETRITUS in water cells: mass c_carc·M plus
Energy/ρ_meat. Eggs and embryos of a dead mother die.

## 10. Vegetation, soil and water fields

### 10.1 Terrain and water

**Terrain**
- **Elevation:** periodic fBm noise, the v1 method.
- **Lakes:** cells below the sea level (12% of cells).
- **Rivers:** D8 flow accumulation over the elevation; cells whose accumulated catchment is above
  a threshold become river.
- **Depth:** lake depth is (sea − elevation). River depth is shallow.
- **Distance maps:** distance-to-water and distance-to-land come from two chamfer passes on the
  torus.
- **Moisture:** base moisture is high near water and in valleys.

**Season.** Each cell's moisture follows the base moisture modulated by a travelling sine wave:
```
SEASON = sin(2π·t/T − 2π·y/FW)
W = clamp(W_base·(1 + a_s·SEASON), 0, 1)
```

### 10.2 Producer dynamics (per land cell, per step)

```
W     = MoistBase · (1 + A_SEASON·SEASON) · (1 + RAIN)          RAIN: this year's anomaly (§10.7)
grow  = W · N/(N + K_N)                                         grass, and the food pools
growS = max(0, (W − W_SHRUB)/(1 − W_SHRUB)) · N/(N + K_N)       shrubs need wetter soil
growT = max(0, (W − W_TREE)/(1 − W_TREE)) · N/(N + K_N)         trees and saplings need the most
lightG = 1 − 0.85·T − 0.35·S
lightS = 1 − 0.70·T
free   = 1 − G − S − T

G += r_G·grow·lightG·(G + s_G·Seed + σ_G·(Ḡ − G))·free
S += r_S·growS·lightS·(S + s_S·G)·(1 − S − T)                shrubs overtop grass
P += r_P·growT·(1 − GRASS_COMP·G)·((T̄ + T)·(0.2 + S) + WIND_SEED/r_P·T_mean)·(1 − P − T) − m_P·P
     + saplings planted by dung                             grass roots take seedlings' water; wind seed rain
T += m_P·P·(1 − T) + r_T·growT·T·(1 − T) − d_T·(1 + DROUGHT_T·max(0, W_TREE − W)/W_TREE)·T
                                                          (tree death → WOOD; droughts kill trees)
G −= shG·T·G,   S −= shS·T·S                              (canopy shading)
BROWSE = leaf share of shrub and tree cover, regrows at r_B toward (S + 0.6·T)
FRUIT  += r_F·T·max(SEASON, 0)·(1 + POLL) − d_F·FRUIT                 (rot → LITTER)
SEED   += r_Sd·(G + 0.5·T)·max(−SEASON, 0)·(1 + POLL) − d_Sd·SEED     (end of the wet season)
NECTAR += r_Nc·(0.5·G + S)·max(SEASON, 0) − d_Nc·NECTAR
ROOT   += r_R·(G + S)·(1 − ROOT) − rooting
LITTER += c_lit·(G + S + T)                                (senescence)
FUEL   = dry share of G + S + LITTER: (G + S + LITTER)·(1 − W)
N      −= c_use·(growth of G, S, T)
POLL   decays at d_P per step
```

Here x̄ is the mean of the four neighbours. **Light competition:** taller layers overtop lower ones,
so after growth the cover is clipped from the bottom: T ≤ 1, S ≤ 1 − T, G ≤ 1 − T − S. The grids double-buffer, and rows are computed
independently (NR-105).

**Water competition (Revision C, NR-331).** Grasses grow at any moisture; shrubs and trees need wetter
soil (W_SHRUB, W_TREE), so trees are confined to wet ground and wet years, as savanna woody cover is
capped by rainfall (Sankaran et al. 2005). Grass roots take the topsoil water that tree seedlings need
(Walter's two-layer hypothesis), so a dense sward keeps trees from establishing. Together with fire
(which grass survives and saplings do not) this is the mechanism that keeps grasslands open. Seeds
reach new ground rarely by wind (WIND_SEED, in proportion to the world's mean tree cover) as well as
from neighbouring trees and in dung.

**Herbivory (NR-308)**
- Grazing removes GRASS.
- Browsing removes BROWSE and kills saplings: P −= 0.5·b.
- Rooting removes ROOT and some GRASS: G −= 0.3·b.

### 10.3 Water producers

```
ALGAE += r_A·Nw/(Nw + K_N)·(depth < shallow)·(1 − ALGAE) − m_A·ALGAE    (death → DETRITUS)
PHYTO += r_Ph·Nw/(Nw + K_N)·(1 + 0.5·SEASON)·(1 − PHYTO) − m_Ph·PHYTO
```

### 10.4 Decomposition and run-off (NR-310)

Each dead pool X rots at rate c_rot,X. Of the rotted amount, the fraction c_nutGain becomes NUTRIENT.
The rates are:

| Pool | Rot rate |
|---|---|
| carrion | fast |
| dung | medium |
| litter | slow |
| wood | very slow |
| detritus | medium |

Soil nutrient does not flow between land cells. Water cells mix with water neighbours (N_DIFF), and
each shore cell loses RUNOFF of its soil nutrient per step into each adjacent water cell (the same
amount leaves the land and enters the water). Weathering adds c_w·fertility.

### 10.5 Fire (NR-309)

- **Ignition:** λ·FUEL·(1 − W)² per cell per step.
- **Spread:** to each neighbour with probability c_spread·FUEL_nb·(1 − W_nb). Calibrated once so that
  a world without animals burns every ~2 years per cell (savanna fire return: 1–5 years).
- **A burning cell:** grass and shrubs keep RESPROUT = 25% as rootstock and resprout; saplings,
  BROWSE, LITTER, NECTAR and SEED drop to 0. T is multiplied by 0.6, and
  the lost T becomes WOOD. ROOT is multiplied by 0.7. N += c_ash × the burned biomass.
- **Duration:** fire lasts 6 steps.

### 10.7 Weather years (Revision C, NR-330)

Each year (T_SEASON steps) has a rainfall anomaly RAIN that scales soil moisture everywhere:
`RAIN_y = RAIN_RHO·RAIN_(y−1) + √(1 − RAIN_RHO²)·RAIN_SD·n_y`, clamped to ±RAIN_MAX, with n_y a standard
normal from the terrain seed. So droughts cluster, and the branches of a prehistory share one climate.
A dry year means less growth, more fire (drier fuel) and dying trees; a wet year the reverse.
Telemetry records it as `rain.anomaly`.

### 10.6 Scent

Meat-eaters deposit c_scent·eff_meat into their cell each step, and MARK multiplies this by 4. Scent
decays at d_scent and diffuses at 10%.

## 11. Step contract (NR-108)

"∥" means the phase is parallel, over fixed slot ranges or field rows. Each worker writes only what
it owns.

| # | Phase | ∥ | Reads | Writes |
|---|---|---|---|---|
| 1 | BUILD | — | X, Y, alive | spatial hash (counting sort) |
| 2 | DECIDE | ∥ | world | own slot: learning update (from last reward), hidden state, gains, intents, probe |
| 3 | RESOLVE | — | intents | kinematics, physiology, fields, deaths, conceptions, reward; counters. Visiting order is a seeded permutation that changes each step. |
| 4 | LIFECYCLE | — | timers | hatching, metamorphosis, immigration |
| 5 | FIELDS | ∥ | old buffers | new buffers (swap) |
| 6 | FIRE | — | fuel, moisture | fire state (serial: spread is directional) |
| 7 | TELEMETRY | — | everything | counters; ring sample every 32 steps |

## 12. Telemetry and transparency

### 12.1 Counters (NR-401)

Kept per species:
- births, conceptions
- deaths per cause
- the killer × victim matrix (20 × 20)
- energy intake per pool (15)
- energy spent: basal, movement, strike, reproduction
- strikes and successes
- matings, LAY events, immigrants

### 12.2 Samples (NR-402)

Taken every 32 steps. Per species:
- count, juveniles, eggs/embryos, mean generation
- mean energy, mean reward, mean |Δ| (how much has been learned)
- the gain share of each of the 28 primitives

Global:
- total amount in each pool
- burning cells, nutrient (land and water)

### 12.2b Life table (NR-214)

Deaths by age class per species (before birth, juvenile, young adult < 2× maturity, older adult) and
the mean age at death, in the samples, the headless report and the Species tab. It shows where a
life cycle breaks.

### 12.3 Behaviour fingerprints (NR-213)

For each species, the console clusters the live animals' gain vectors (k-means, k = 4, on the server
thread). It names each cluster by its top three primitives, for example "CLIMB_FOOD + EAT + COHERE".
It plots the share of each cluster over time. That is how *emergent* behaviours are shown: measured,
not asserted.

### 12.4 Explanations (NR-403)

These are sentence rules over counter windows. Each sentence lists the counters it used.

### 12.5 Probe

For the selected animal the probe records:
- inputs, hidden state, z, gains and fired actions
- reward and reward mean
- top contributing inputs to the strongest primitive
- |Δ| per output row (what it has learned)
- mother, children, generation

## 13. Server and console

These are unchanged from Revision A, except that there are 20 species and the dashboards add the
following:
- an **energy-cycle diagram**: a live Sankey of pools → species → dead pools → nutrient
- a **trophic stack**: biomass by trophic level
- **behaviour fingerprints** per species
- **learning curves**: mean reward and |Δ|
- **realm views**: land and water
- **gene-bank status**

Cameras: overview, follow-cam (lineage), ride-along.

**Animal models** (`wwwroot/js/creatures/`, display only). Each species has two models:
- **Near:** a detailed, animated model for animals within 90 units of the camera, up to 1,500 per
  species.
  - A model is a list of parts. Each part is tagged as body, head, a leg, a wing, the tail, the jaw or
    a swarm member, and has a pivot and a colour role. Colours come from the species colour: the body
    is countershaded, and eyes, horns and beaks have their own roles.
  - The vertex shader moves the parts from three things per animal: speed (gait), a phase taken from
    its serial, and the action bits of the AGENTS frame. Legs trot in step with speed, wings flap,
    tails and fish bodies sway, the head dips to eat, the jaw opens and the body lunges on a strike,
    and swarm members wander.
  - There are no skeletons, so each species is still one instanced draw call per detail level.
- **Far:** the simple shape, in one colour, for everything else.

`gallery.html` shows every species near and far without running the world. The models read nothing
back into the simulation.

## 14. Code organisation

```
src/Neuroreef.Core/
  Constants.cs  Species.cs (body plans + closure check)  Rng.cs  World.cs
  Terrain.cs  Fields.cs  Fire.cs  Perception.cs  Brain.cs  Decide.cs
  Resolve.cs  Lifecycle.cs  Telemetry.cs  GeneBank.cs  Probe.cs  Snapshot.cs  Simulation.cs
src/Neuroreef.Server/  Program.cs  SimHost.cs  Wire.cs  Explain.cs  Fingerprint.cs  wwwroot/
src/Neuroreef.Headless (part of the Server exe: --headless)
tests/Neuroreef.Tests/
```

## 15. The laws and how they were set (NR-321)

**Revision D: food-limited growth (a correction).** The growth law of Revision B charged a juvenile the
energy of its tissue at a fixed rate, the rate that reaches adult mass at PMature. That rate is about
four times what a newborn can digest (a newborn Drifter: growth 0.009 E/step against at most 0.002 E/step
digested), so almost no juvenile could reach maturity under any behaviour: the law was infeasible, not
merely harsh. The full Revision B prehistory showed it (juvenile deaths the largest class in every
species). Now a juvenile grows only from energy above GROW_RESERVE of its reserve, at most at that
fastest rate, and matures on reaching adult size (`Grown` = 1), not at a fixed age. A well-fed youngster
matures at PMature; a hungry one later; milk (§7.8) speeds it. Energy is still conserved.

**Revision C** added new mechanisms: reflexes, parental care, disease, weather years, water-limited
plant growth and wind seed rain (§7.7, §7.8, §9.6, §10.2, §10.7). Their constants were set once from
the biology cited in the constants table, before any run with them, and are frozen like the rest. The
existing laws were not changed.


The simulation has two kinds of numbers:

1. **Laws**: the physics and chemistry of this world — growth rates, energy densities, rot rates,
   costs, fire spread. They have no neutral value; someone has to choose them.
2. **Outcomes**: who survives, how large populations get, whether cycles appear, what behaviour
   evolves.

Policy, agreed with the user: **laws are set once, from physical plausibility and conservation of
energy and mass, documented with their origin, and then frozen. They are never adjusted to produce or
prevent an outcome.** Extinctions, crashes and failures are reported as results. A law is changed
later only if it breaks a physical rule (for example, creating energy from nothing).

The one-time review made these changes, each for a stated physical reason:

| Change | Reason |
|---|---|
| Growth and offspring tissue cost energy (GROWTH_EFF, C_CARC, RHO_MEAT) | a body was free to build, but yielded energy when eaten: energy from nothing |
| Newborn mass per species (`Birth`) | a fixed 30% newborn made a fish egg cost a third of a fish; real clutch size and egg size trade off |
| Reserve C_EMAX = 2 | stored reserve of the same order as tissue energy |
| Canopy shading of grass and shrubs reduced | grass vanished with no animals present |
| Light competition (taller layers overtop) | grass took all free space and locked shrubs out, which is not how shrubs grow |
| Resprouting after fire | savanna grasses and shrubs resprout from rootstock |
| Shrub growth calibrated to ~1/4 year e-folding | measured against shrub recovery after disturbance |
| Soil nutrient does not diffuse; run-off from shore cells | soil does not flow; nutrients wash into water |
| Fire spread calibrated to a ~2-year fire return interval | measured in a world without animals (`--empty`) |
| Super-individual masses raised | one agent stands for more organisms; a representation choice that changes no ecology |

`--empty` runs the vegetation laws without animals, so they can be checked on their own.

Changes to how the population is **seeded** (group colonisation, §7.4) are evolution mechanisms, not
laws: they decide how colonists arrive, not what happens to them.
