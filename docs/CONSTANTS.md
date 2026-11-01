# NEUROREEF 2 — CONSTANTS TABLE

Document NR-CT-001, Revision B. **Generated** from `src/Neuroreef.Core/Constants.cs` and `Species.cs` by
`Neuroreef.exe --print-constants`; do not edit by hand. A test fails if this file and the code disagree (NR-106).

Origin codes: **V1** taken from Neuroreef v1; **ECO** ecological rule or exponent from the literature;
**GEO** geological or geophysical value from the literature; **HT** hand-tuned in headless runs; **-** derived.


## 1. World

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| WORLD | 1024 | units | side of the toroidal world | HT |
| FCELL | 4 | units | field cell side | V1 |
| FW | 256 | cells | field grid side (WORLD/FCELL) | - |
| HCELL | 128 | units | spatial-hash cell side (bodies): the 7x7 cells around an animal hold everything it can perceive in a step, MAX_PERCEIVE = 3 x HCELL | derived (2026-11-01): from the largest perception radius of a step in real metres (MAX_SIGHT widened by the path, Decide.Perceive): 1.5 km at M_PER_UNIT 4 m (2026-11-01, the 1 km world: 8 units when a unit was 54.5 m, 32 at 13.75 m; the same metres); was 16 when sight reached 32 units (1.7 km). A hash grid narrower than the neighbourhood is searched whole (Decide) |
| GW | 8 | cells | spatial-hash grid side | - |
| HCELL_CALL | 512 | units | spatial-hash cell side (callers): the 5x5 cells around a listener hold every caller it can hear, MAX_HEAR <= 2 x HCELL_CALL | derived (2026-11-01): from the loudest call in real metres (HEAR_R_M x MASS_HI^(1/3) = 3 km): 512 units of 4 m is 2 km (it was 32 units of 54.5 m); a map side must be a multiple of it or smaller (one cell: the whole map), and a callers' grid narrower than 5 cells is searched whole (Decide) |
| MAX_PERCEIVE | 384 | units | the farthest a step's perception reaches (sight along the way travelled, Decide.Perceive): the 7x7 hash neighbourhood | derived: 3 x HCELL (1.5 km); the largest walker's reach ~1 km, so it binds only for the fastest fliers |
| CAPACITY | 16384 | slots | agent slots (animals, eggs, embryos) a world starts with: it grows as births need more, limited only by memory (World.Grow) | HT: ~212 KB per slot with 256-unit brains (World.SlotBytes); not a cap since 2026-10-31: a seed gives the same run from any start |
| LAKE_SHARE | 0.1 | fraction | share of the map below sea level (Nature.Water, the default): the sea; lakes above it come from the water balance of closed basins (2026-10-31; before, every cell below this quantile was a lake at one level) | HT |
| SHALLOW | 2 | m | depth below which water is shallow (the shallow realm: wadeable, algae grow; World.ShallowE is it in elevation); it is also the depth over which light falls to 1/e (algae) | ECO/HT: the littoral wading zone of shorebirds and waders is under ~0.5-2 m (Ntiamoa-Baidu et al. 1998), and light falls to 1/e in 1-3 m of lake or coastal water (k_d 0.3-1 /m; Kirk 2011). Was 0.035 in elevation before 2026-11-01: ~150 m at H_RELIEF 3,000 m and ~320 m at 8,000 m, so nearly every river and two thirds of the sea and lakes were 'shallow' at 100-400 m |
| T_SEASON | 3000 | steps | season period (one year), by default | ECO: about one lifespan at M=1, so seasonal life histories can evolve (ecology review; 9000 before Revision K); since 2026-11-01 a step is STEP_H and the lifespan at M=1 is 2 years (C_LIFE) |
| A_SEASON | 0.45 | - | season amplitude on moisture | V1 |
| SAMPLE_EVERY | 32 | steps | telemetry sample interval | HT |
| RING | 4096 | samples | telemetry ring length | HT |
| LONG_RING | 2048 | samples | long-history ring length (one sample in LONG_EVERY) | HT: with LONG_EVERY it covers 33 million steps |
| LONG_EVERY | 512 | samples | samples between long-history samples | HT |
| MAX_NEIGH | 64 | animals | max neighbours examined per animal per step | HT |
| MAX_SIGHT | 82.6482 | units | max sight range: the farthest eye (MASS_HI, sight gene x BODY_RANGE_TRAIT, the narrowest field of view FOV_LO) while scanning (SCAN_SIGHT); 2.0913 = MASS_HI^EXP_SIGHT (a test checks it) | derived (2026-11-01): 332 m; was 32 units (1.7 km) for any eye, even a mouse's |
| SCAN_SIGHT | 1.3 | x | sight gained while scanning (standing still and looking: SCAN) | HT |

## 2. Units

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| STEP_H | 2.922 | h | duration of one step: 365.25 days x 24 h / T_SEASON | derived: T_SEASON = 3000 steps is one year, so a step is 2.92 h (8.2 steps a day). Before 2026-11-01 the notes said ~3 h (water cycle) and ~5 h (pathogens); 5 h was wrong |
| STEP_S | 10519.2 | s | duration of one step | derived: STEP_H x 3600 = 10,519 s |
| STEPS_PER_DAY | 8.21355 | steps | steps in a day | derived: 24 / STEP_H = 8.21 |
| M_PER_UNIT | 4 | m | metres per world unit: a field cell (FCELL units) is GEO_CELL_M | derived (2026-11-01, the 1 km world): 4 m, so the default 256 map is 1.02 km across, 512 is 2.05 km, 1024 4.1 km and 2048 8.2 km. Was 54.5 m (GEO_CELL_M 218 m: the 256 map 14 km, 512 28 km) |
| BODY_L_M | 0.1 | m | body length at M = 1 kg: the side of a cube of the body's volume at the density of water, ~(M / 1000 kg/m3)^(1/3); a body is BODY_L_M x M^(1/3) x its length gene's share (Body.LengthM) | ECO: animal tissue is about as dense as water (~1000-1100 kg/m3; Schmidt-Nielsen 1984). A real mammal's head-body length is ~0.3 M^(1/3) m (mouse 8 cm, rabbit 40 cm, wolf 1.1 m) because it is ~5x longer than wide; the length genes here elongate a body up to 2.5x |
| C_SIGHT_M | 60 | m | how far an eye of a 1 kg body sees another 1 kg body (the base of sight, x M^EXP_SIGHT x sight gene x (1.6 - field of view / 360)) | ECO: eye axial length ~11 mm x M^0.2 in mammals (Kiltie 2000; Howland, Merola & Basarab 2004; Burton 2008: eye size ~M^0.2); acuity ~0.2 cycles/degree per mm of eye in non-primate mammals (mouse 0.5, rat 1, rabbit 3 c/deg; Veilleux & Kirk 2014), i.e. ~2.2 c/deg at 1 kg; a 0.3 m body is detected when it spans 1-2 resolution cells: 0.3 m / (1/2.2 to 1/4.4 deg) = 40-75 m. Was 16 units (870 m) |
| DMD_M | 1038 | m/day | how far animals travel in a day at M = 1 kg (daily movement distance, x M^EXP_SPEED) | ECO: daily movement distance of mammals 1.04 M^0.25 km (Garland 1983); day range ~M^0.25 in herbivores and steeper in carnivores (Carbone et al. 2005) |
| TRAVEL_SHARE | 0.2 | - | share of its top speed the world's animals travel at, on average over all steps (moving, eating, resting): top speed per step = DMD_M x M^EXP_SPEED / STEPS_PER_DAY / TRAVEL_SHARE, so on average they cover the day range | measured (2026-11-01, seeds 3 and 5, 512 map, 6000 steps): animals moved 25 m and 17 m a step against a top speed of 125 m and 116 m (0.20 and 0.15) |
| T_CHASE | 15 | s | how long a sprint at top running speed lasts when a predator closes on its prey (or prey bolts): a strike's reach in a step is its body plus the distance run in this time at Garland's top speed (Body.ReachM) | ECO: most predator chases last 10-30 s (Elliott, Cowan & Holling 1977); cheetahs average 173 m (~20 s; Hilborn et al. 2012); sprinting is anaerobic and short (Bennett & Ruben 1979) |
| GARLAND_V1 | 30.08 | km/h | top running speed at 1 kg: log10 V(km/h) = log10 V1 + GARLAND_B log10 M + GARLAND_C (log10 M)^2 | ECO: maximal running speed of 106 mammals (Garland 1983): 30 km/h at 1 kg, 14 at 0.1 kg, 54 at 40 kg |
| GARLAND_B | 0.25892 | - | linear term of Garland's top speed in log10 M | ECO: Garland 1983 |
| GARLAND_C | -0.06237 | - | quadratic term of Garland's top speed in log10 M | ECO: Garland 1983 (speed peaks near 120 kg) |
| BMR_W | 3.39 | W | resting metabolic rate of a 1 kg body (Kleiber's line): what C_BASAL stands for, so one energy unit is BMR_W x STEP_S / C_BASAL = 8.9 MJ | ECO: 70 kcal/day x M^0.75 = 3.4 W at 1 kg (Kleiber 1961) |
| COT_J | 10.7 | J/m | net cost of transport of running at 1 kg (x M^EXP_MOVE per metre) | ECO: 10.7 M^-0.316 J/(kg m) (Taylor, Heglund & Maloiy 1982); with BMR_W it gives C_MOVE |
| HR_KM2 | 0.03 | km2 | home range of a 1 kg body (x M): the area founders of one group share (Life.Found) | ECO: home range area ~M^1.0 across mammals (Kelt & Van Vuren 2001; Jetz et al. 2004); herbivores ~3 ha at 1 kg (rabbits 1-5 ha, roe deer 50-100 ha at 25 kg); carnivores ~10x more, from scarcer food (Jetz et al. 2004). Replaces GROUP_SPREAD = 10 units (545 m) for any body |

## 3. Allometry

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| EXP_BASAL | 0.75 | - | Kleiber exponent for basal cost and digestion | ECO |
| EXP_SPEED | 0.25 | - | mass exponent of top speed per step (the day's travel) | ECO: daily movement distance ~M^0.25 (Garland 1983; Carbone et al. 2005); was 0.17 before 2026-11-01 (walking speed ~M^(1/6) at a constant Froude number; Alexander 1989) |
| EXP_LIFE | 0.25 | - | lifespan and maturity exponent | ECO |
| EXP_SIGHT | 0.2 | - | sight range exponent | ECO: eye size and acuity ~M^0.2 (Kiltie 2000; Burton 2008) |
| EXP_TURN | -0.2 | - | turn rate exponent | HT |
| C_BASAL | 0.004 | E/step | basal cost at M=1 | ECO: Revision M, ecology review: x4, so an animal burns many times its tissue energy over a life (Rubner 1908; Speakman 2005) and fasts ~1/16 of a life, not a third (Lindstedt & Boyce 1985) |
| C_EMAX | 2 | E/M | energy reserve per unit mass | ECO: stored reserve of the same order as tissue energy |
| C_GUT | 0.3 | M/M | gut capacity per unit mass | HT |
| C_DIGEST | 0.025 | M/step | digestion rate at M=1 | ECO: Revision M: the most an animal can digest is ~5x basal at 0.5 digestibility (Hammond & Diamond 1997) |
| C_ABSORB | 0.028 | E/step | energy the gut can absorb per step at M=1 (x M^0.75): digestion passes rich digesta only as fast as their energy is taken up (Resolve), and the food a forager picks is worth at most this a step | ECO (2026-11-01): intestinal nutrient transport has a set capacity with modest spare (Karasov & Diamond 1983; Diamond 1991), and maximal sustained energy budgets reach ~7x basal (Hammond & Diamond 1997: lactating mice ~7, cold-exposed rodents 5-7, birds feeding young ~4). Before, mass alone bounded digestion, so seeds (7 E/M, 0.75 digestible), fruit and nectar gave 30-47x basal at 0.1 kg against 6.7x for grass |
| C_BITE | 0.06 | M/step | bite size at M=1 | Revision M: x5 with digestion (a bite fills the gut faster than it empties) |
| C_SPEED | 157.971 | units/step | top speed per step at M=1 (walk): DMD_M / STEPS_PER_DAY / TRAVEL_SHARE / M_PER_UNIT | derived (2026-11-01): 632 m a step (158 units of 4 m: over half a 1 km map; the torus wraps, and distances between animals are the shortest way round); was 1.2 units (65 m) from V1, so animals covered ~1/9 of a real day range |
| C_TURN | 0.32 | rad/step | turn rate at M=1 | V1 |
| C_LIFE | 5995.89 | steps | lifespan at M=1 (the age at which senescence sets in): 730 days | ECO (2026-11-01): wild mammals of ~1 kg rarely live past 2-3 years (wild rabbits, Thompson & King 1994; ~0.75 years at 20 g as wild mice, ~5 years at 40 kg); lifespan / maturity ~6, against Charnov's (1993) adult life expectancy of ~1.4 maturity ages and maximum longevities of ~10 (Prothero & Juergens 1987). Was 3200 steps (V1), set when a step had no duration |
| C_MATURE | 985.626 | steps | maturity age at M=1 (the fastest a juvenile can grow to adult size, on a full reserve): 120 days | ECO (2026-11-01): age at first reproduction of mammals ~M^0.25-0.3 (Millar & Zammuto 1983; Charnov 1993): house mouse 20 g 6 weeks (x 0.38 = 45 days here), rat 0.3 kg 10 weeks (89 days), rabbit 2 kg 4-5 months (143 days), roe deer 25 kg 14 months (268 days). Was 250 steps (V1: 30 days), 3-5x too fast |
| C_SIGHT | 15 | units | sight range at M=1: C_SIGHT_M / M_PER_UNIT | derived (2026-11-01): 15 units of 4 m (60 m); was 16 (870 m) from V1 |
| C_MOVE | 4.80089E-06 | E/(M^0.7*unit) | net cost of moving one unit at M=1 (x M^EXP_MOVE x distance): COT_J x M_PER_UNIT in energy units (C_BASAL is BMR_W x STEP_S) | ECO (2026-11-01): the cost of transport of Taylor, Heglund & Maloiy (1982) on the energy scale Kleiber's metabolism sets; 6.5e-5 per unit, 1.6% of a step's basal metabolism at 1 kg. Was 0.004 x C_SPEED = 0.0048 (Revision M), 73x the measured cost per metre, when a step's travel was 65 m |
| EXP_MOVE | 0.7 | - | mass exponent of the cost of transport | ECO: running cost per unit distance scales ~M^0.7 (Taylor, Heglund & Maloiy 1982) |
| GLIDE | 0.3 | - | speed gained on land per unit of wing that cannot yet lift the body (wing-assisted running, gliding) | ECO: proto-wings aid running up slopes and gliding before powered flight (Dial 2003) |

## 4. Reproduction

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| C_SIRE | 0.2 | - | cost to a hermaphrodite of siring, as a share of its own litter's cost (male function) | ECO: male function takes 10-30% of a simultaneous hermaphrodite's reproductive resources (Charnov 1982) |

## 5. Clades

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| SPLIT_GAP | 3 | - | two forms are separate clades when, along the axis between their centres (visible traits), the gap is this many pooled SDs | ECO: genotypic clusters with few intermediates (Mallet 1995) |

## 6. Allometry

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| C_THIRST | 0.0012 | /step | thirst rise per step (death after ~830 dry steps) | HT |
| C_FATIGUE | 0.012 | /step | fatigue gain at full sprint | HT |
| C_RECOVER | 0.004 | /step | fatigue recovery (x3 resting) | HT |
| REST_BASAL | 0.85 | - | basal multiplier while resting | HT |
| SWIM_SPEED | 0.85 | - | speed factor for swimmers | HT |
| DRINK_RATE | 0.25 | /step | drinking: thirst removed per step | HT |
| GROWTH_EFF | 0.75 | - | efficiency of turning energy into new tissue (growth, offspring) | ECO: net growth efficiency 0.6-0.8 |
| GROW_RESERVE | 0.2 | xEmax | a juvenile keeps this share of its reserve against starving; above it, it grows at its top rate times how full the reserve is | ECO: growth draws on the reserve in proportion to its density (dynamic energy budget, Kooijman 2010); hungry young grow slowly (Revision D; Revision P: was a threshold of 0.5, below which the young, living near 40% full, hardly grew) |

## 7. Food

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| RHO_GRASS | 3.2 | E/M | energy density of grass | V1 |
| RHO_BROWSE | 3.6 | E/M | energy density of browse (leaves) | HT |
| RHO_PLANT | 3.2 | E/M | energy of a unit of plant production (cover): what reproduction turns into fruit, seed and nectar keeps it, so their mass is the production over their own density (Plants), and a bite of roots takes this much cover per unit of its energy | mass and energy balance (2026-11-01): the energy of herb tissue (RHO_GRASS); before, a unit of production became a unit of nectar, seed or fruit, 2.5, 2.2 and 1.9x its energy |
| RHO_FRUIT | 6 | E/M | energy density of fruit | HT |
| RHO_SEED | 7 | E/M | energy density of seeds | HT |
| RHO_NECTAR | 8 | E/M | energy density of nectar | HT |
| RHO_ROOT | 4 | E/M | energy density of roots | HT |
| RHO_ALGAE | 3.2 | E/M | energy density of algae | HT |
| RHO_PHYTO | 3.6 | E/M | energy density of phytoplankton | HT |
| RHO_CARRION | 3.5 | E/M | energy density of carrion (partly decayed tissue) | ECO: below fresh meat |
| RHO_DUNG | 2 | E/M | energy density of dung | HT |
| RHO_LITTER | 1.8 | E/M | energy density of leaf litter | HT |
| RHO_WOOD | 1.6 | E/M | energy density of dead wood | HT |
| RHO_DETRITUS | 2.4 | E/M | energy density of aquatic detritus | HT |
| RHO_MEAT | 4 | E/M | energy density of body tissue (meat); also the cost of building tissue | ECO: tissue ~2-4x the stored reserve per unit mass |
| RHO_BLOOD | 3 | E/M | energy density of blood | ECO: below tissue |

## 8. Hunting

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| P0_STRIKE | 0.45 | - | base strike success | HT |
| C_CONF | 0.25 | - | prey-group confusion | V1 |
| C_STRIKE | 0.004 | E/M | cost of a strike | HT |
| C_RETAL | 0.06 | E | retaliation per unit of excess defence | HT |
| C_CARC | 0.5 | M/M | edible tissue (carcass) per unit body mass; also the tissue built in growth | ECO: about half of live mass is edible tissue |
| JUV_DEFENCE | 0.25 | - | defence multiplier for juveniles | HT |
| DEF_BASE | 0.5 | - | defence of an unarmoured body per unit mass (skin, muscle, bone) | ECO: a body's own size is its first defence; predators rarely take prey much larger than themselves (Carbone et al. 1999) |
| C_DAMAGE | 0.4 | E | energy a landed hit removes per unit of strike power (strike x attacker mass) | ECO: wounding scales with weapon force; an armed attacker subdues equal-sized prey in a few hits (Revision H) |
| C_DRAIN | 0.05 | E | parasite drain per strike at M=1 | HT |
| PAIN_HIT | 0.6 | - | pain added by a failed strike received | HT |
| PAIN_DECAY | 0.03 | /step | pain decay per step | HT |

## 9. Brain

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| WSCALE | 0.0625 | - | genome weight scale (int8 / 16) | - |
| WMAX | 7.9 | - | max |weight| (genome + learned) | - |
| FOUNDER_W | 12 | int8 | founder memory-layer (W1) weight range: round(U(-1,1)*FOUNDER_W) | HT |
| FOUNDER_W2 | 4 | int8 | founder output-layer (W2) weight range | HT |
| FOUNDER_BIAS | -64 | int8 | founder output bias (x/16): every primitive of every species starts near 2% (calm newborn brain) | HT: was -48 (5%) until 2026-10-30, when naive animals struck whatever was in reach every ~20 steps and founding groups killed each other |
| FOUNDER_BIAS_CALL | -80 | int8 | founder output bias of each of the 8 call primitives (x/16): together about 5%, like one primitive | HT: calm newborn brain (Revision M) |
| FOUNDER_BIAS_NOISE | 4 | int8 | random spread of the founder output bias | HT |
| FOUNDER_W_SD | 2 | int8 | standing variation of founders' brain and reflex bytes around their ancestor's (SD) | ECO: members of one population share an inherited nervous system with small heritable variation; half the founder output-weight range (Revision H) |
| ETA_LO | 0.002 | - | learning rate eta range low | HT |
| ETA_HI | 0.06 | - | learning rate eta range high | HT |
| LAMBDA_LO | 0.5 | - | trace decay lambda range low | HT |
| LAMBDA_HI | 0.97 | - | trace decay lambda range high | HT |
| SIGMA_LO | 0.02 | - | exploration noise sigma range low | HT |
| SIGMA_HI | 0.3 | - | exploration noise sigma range high | HT |
| KAPPA_HI | 0.002 | /step | learned-delta decay kappa range high (low = 0) | HT |
| RBAR_RATE | 0.01 | /step | reward baseline rate | HT |
| R_INTAKE | 4 | - | reward: per unit intake / Emax | HT |
| R_DRINK | 2 | - | reward: per unit thirst relieved | HT |
| R_REST | 0.5 | - | reward: per unit fatigue relieved | HT |
| R_MATE | 1 | - | reward: mating | HT |
| R_BIRTH | 2 | - | reward: per offspring produced | HT |
| R_PAIN | 4 | - | reward: per unit damage / Emax (negative) | HT |
| R_FEAR | 0.5 | - | reward: per unit fear rise (negative) | HT |
| MUT_RATE | 1.01051E-05 | - | mutation probability per brain or reflex byte (1/LEN: about one mutation per offspring) | ECO: on the order of one mutation per genome per generation in functional sequence (Drake 1998; Lynch 2010); was 0.02 (~39 per offspring, mutational meltdown) |
| MUT_BODY | 0.01 | - | mutation probability per body gene per birth (about 0.4 body changes per newborn) | ECO: each body gene stands for a polygenic trait of many loci; mutational heritability ~1e-3 per trait per generation (Lynch & Walsh 1998), so a trait changes far more often than one brain byte |
| FOUNDER_BODY_SD | 12 | int8 | standing variation of founders' body genes around their ancestor (SD) | ECO: morphological traits vary with a CV of about 5-10% in natural populations (Houle 1992) |
| MUT_SMALL | 2 | int8 | small mutation sigma | HT |
| MUT_BIG_SHARE | 0.05 | - | share of mutations that are large jumps | V1 |
| MUT_BIG | 16 | int8 | large mutation sigma | V1 |
| RSCALE | 0.0625 | - | reflex input weight scale (int8 / 16, as the output layer) | - |
| SATIETY | 0.2 | xEmax | appetite is full while the reserve is more than this share below full (hunger above it), and fades to none as it tops up | ECO: voluntary intake is limited by gut fill and energy status, not by energy deficit alone (Forbes 2007); measured 2026-10-30: adults on food ate on 47% of steps and sat at 20-60% reserve, so females were ready to breed 12% of the time |
| NLMS_REF | 25 | - | input size (|input|^2 of memory units and senses) above which a brain's learning step is scaled down by NLMS_REF / |input|^2 | ML: normalised LMS (Widrow & Hoff; Haykin 2002); measured 2026-10-30: ~17-25 for brains under 48 units in a living world, ~117 at 256 units |
| WIRE_LO | 0.5 | - | lowest spectral radius (feedback strength) of a brain's recurrent memory, set by the wiring-gain gene | ECO/ML: a reservoir holds the most memory near 1 (Jaeger 2001; Lukosevicius & Jaeger 2009); below it memories fade fast |
| WIRE_HI | 1.5 | - | highest spectral radius of a brain's recurrent memory | as WIRE_LO: above ~1.2 activity turns chaotic and scrambles what it holds |
| KIT_W | 120 | int8 | founder strength of each starter-kit reflex (x/16): at full need, 120/16 = 7.5 against the calm bias of -4 gives about 97% | designed: the innate starter kit (DESIGN §7.7, Revision D); the user (2026-10-30): the base instincts should be very strong, "like primary colours" (was 80, 88% against a bias of -3) |
| ARCHIVE | 16 | genomes | elite archive size per species | HT (Revision M: 64 before; a genome is now ~83 KB) |
| SCORE_MATURED | 3 | - | archive score weight per offspring that reached maturity | HT |

## 10. Prehistory

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| PREHISTORY_STEPS | 500000 | steps | length of a prehistory run | HT |
| BRANCHES | 2 | branches | number of separate branches (populations on the same land) evolving side by side | HT (Revision M: 3 before; memory) |
| YEARS_PER_STEP | 1000 | years | symbolic time shown in the console: years per step | - |
| BRANCH_CAPACITY | 16384 | slots | agent slots a branch starts with (it grows as needed, World.Grow) | HT: Revision M; not a cap since 2026-10-31 (the report warns if a branch reaches its memory limit) |

## 11. Brain

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| GROUP_SIZE | 8 | agents | founders start in groups of this size at one site | ECO: populations are clustered; lone colonists cannot find mates (Allee effect) |
| HOME_R_MAX | 0.33 | - | largest radius of a founding lineage's home range, as a share of the map side (a big body's range widens up to this until it can feed FOUND_MIN founders) | HT: a third of the map; home ranges grow about in proportion to body mass (Kelt & Van Vuren 2001) |
| HOME_R | 0.12 | - | radius of an ancestor's home range, as a share of the map side | Revision N (the user): each lineage starts in its own part of the map; species have homelands and meet as they spread |
| HOME_TRIES | 24 | - | random sites tried when choosing a home range (the one farthest from the others is taken) | HT |
| BANK_LIVING | 48 | genomes | genomes per species stored in a gene bank (living sample) | HT (Revision M: 96 before) |

## 12. Body

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| BODY_RANGE_TRAIT | 1.6 | - | range of the speed and sight multipliers (x/÷) | HT |
| COST_SPEED | 0.4 | - | basal cost per unit of extra speed | HT |
| COST_SIGHT | 0.15 | - | basal cost per unit of extra sight | HT |
| ARMOUR_DRAG | 0.2 | - | speed loss per unit of armour in water and air (on land armour is part of the load the legs carry, LOAD_DRAG) | HT |
| MASS_LO | 0.1 | M | smallest body mass a mass gene can give (the floor of the smallest size class) | ECO: Revision M: 0.01 gave tiny bodies 32x the numbers from the same food; the user (2026-10-30) asked for size classes with their own floors |
| FLOOR_CLASSES | 4 | - | number of body-plan size classes; class k cannot be smaller than Body.Floors[k] (0.1, 1, 3, 10) | designed: the user (2026-10-30): classes with different floors, inherited, rarely changing; size evolves freely above the floor |
| FLOOR_MUT | 5E-05 | - | chance per birth that the size class steps up or down one class | designed: the user: inherited, rarely changes (a body-plan change is rare; ~1 in 20,000 births) |
| MASS_HI | 40 | M | largest body mass a mass gene can give | ECO: a large mammal-sized organism (relative units) |
| LIMB_MIN | 0.3 | - | organ size (0..1) below which an organ is vestigial (for describing and drawing bodies; speeds are smooth) | - |
| COST_LIMB | 0.15 | xbasal | basal cost per unit of fin size per pair of fins (legs pay instead while they walk: LEG_SWING) | ECO: locomotor muscle is maintained at a cost |
| COST_WING | 0.15 | xbasal | basal cost per unit of wing size (upkeep of wings and flight muscle; the power of flying is paid while airborne, FLIGHT_MULT) | ECO: flight muscle is 15-25% of a bird's mass (Greenewalt 1962; Marden 1987), maintained like any muscle; until 2026-10-31 this was 0.5 x mass^(1/3), standing in for the flight power now paid per step |
| COST_ARMOUR_ABS | 0.12 | xbasal | basal cost per unit of armour | ECO: heavy armour is costly to carry and grow |
| COST_STRIKE_ABS | 0.15 | xbasal | basal cost per unit of weaponry | ECO: jaws, teeth and claws are costly tissue |
| ARMOUR_MAX | 3 | - | largest armour a gene can give | - |
| STRIKE_MAX | 2 | - | largest weaponry (strike) a gene can give | - |
| TAU_FERM | 2 | - | fermentation time scale of fibre, in units of M^0.25 retention: the share fermented is 1 - exp(-M^0.25 / this), relative to M = 1 | ECO: Jarman-Bell principle (Demment & Van Soest 1985; Clauss et al. 2013): fibre digestibility rises from ~25% in small to ~60% in large herbivores; 2 gives x0.63 at M = 0.1, x1.5 at 10, x1.8 at 40 |
| DIET_BUDGET | 1 | - | length (L2 norm) of the vector of digestive efficiencies: a generalist spreads it, a specialist concentrates it | ECO: digestive specialisation trades off breadth against efficiency |
| LITTER_MAX | 12 | young | largest litter a gene can give | - |
| NEWBORN_MIN | 0.01 | M | smallest newborn (or egg) mass | ECO: eggs and young have a physical smallest size (Revision M) |
| BIRTH_LO | 0.01 | - | smallest newborn mass as a share of adult mass | ECO: fish and insect eggs |
| BIRTH_HI | 0.3 | - | largest newborn mass as a share of adult mass | ECO: precocial mammals |
| FOV_LO | 120 | degrees | narrowest field of view a gene can give | ECO: forward-facing predators |
| FOV_HI | 360 | degrees | widest field of view a gene can give | ECO: prey with lateral eyes |
| C_GEST | 410.678 | steps | gestation (or incubation) at M=1 for a newborn of a tenth of adult mass: 50 days, x M^0.25 x sqrt(newborn share / 0.1) | ECO (2026-11-01): gestation ~M^0.25 (Kihlstroem 1972; Blueweiss et al. 1978), longer for precocial young (Martin & MacLarnon 1985): house mouse 19 days (14 here), rabbit 1.5 kg at 3% 31 days (30), guinea pig 0.9 kg at 10% 68 days (49), deer 50 kg at 8% 200 days (119). Was 150 steps (V1: 18 days) |
| C_COOL | 410.678 | steps | breeding cooldown at M=1 from conception (the shortest interval between litters): 50 days, x M^0.25 | ECO (2026-11-01): interbirth intervals of small mammals are about a gestation plus part of a lactation, ~M^0.25 (Millar 1977): house mouse ~20-25 days (19 here), rat 24-30 (37), wild rabbits 5-7 litters in a breeding season (55 days at 1.5 kg). Was 150 steps (V1: 18 days) |
| DEEP_SWIM | 0.3 | - | swimming power (share of the body's top speed in water) needed to stay in deep water | ECO: staying up in open water needs swimming power |
| SWIM_DEPTH_RANGE | 249 | x | additional maximum water depth per unit fin size, in SHALLOW-depth units (the deepest a body goes is SHALLOW x (1 + this x fins): 500 m with full fins, 52 m at fins 0.1) | HT: 14 when SHALLOW was 0.035 in elevation (a body with fins 0.1 reached ~800 m, below every water); 249 since SHALLOW is 2 m (2026-11-01), so a body with any fins reaches most water of the default map (its sea is at most ~100 m deep) and full fins 500 m; the DEPTH sense spans 0-500 m |
| CRAWL | 0.05 | - | a legless body crawls on land at this share of its top speed | ECO: worms, snakes and stranded fish move slowly without legs |
| C_CALL | 0.5 | xbasal | energy of one call, as a share of basal metabolism at the caller's mass | ECO: vocalising costs a few times resting metabolism for its duration (Ophir et al. 2010); a call is a short burst |
| VOICE_MIN | 0.25 | - | a voice organ below this makes no sound | HT: a little voice is no voice; above founder variation around a mute ancestor |
| COST_VOICE | 0.03 | xbasal | basal cost of a full voice (larynx and its control) | ECO: small; vocal tissue and its neural control are a minor part of upkeep |

## 13. Voice

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| MAX_SYL | 3 | sounds | syllables in one utterance at most (distinct sounds, in the order of how strongly each is intended) | ECO: animal calls combine a few units into sequences, rarely more (Engesser & Townsend 2019) |
| CALL_SYL | 0.5 | - | share of a call's cost that grows with each syllable: an utterance of n syllables costs C_CALL x basal x (1 - CALL_SYL + CALL_SYL x n) | ECO: a longer song costs more, but much of a call's cost is the start-up (Oberweger & Goller 2001; Ophir et al. 2010) |
| HEAR_R_M | 870 | m | how far a full voice of a body of M=1 kg carries (range = HEAR_R_M x voice x M^(1/3)) | ECO: bigger animals call louder and are heard further (Fletcher 2004; Wiley & Richards 1978): a songbird of 20-60 g is heard by its kind at ~100-300 m (Brenowitz 1982), a 40 kg wolf's howl at 6-10 km (Harrington & Mech 1979); x M^(1/3) through both gives 0.5-2.3 km at 1 kg. The old 16 units (870 m) lies in that range, so hearing is kept; until 2026-11-01 calls were cut off at MAX_SIGHT (1.7 km) |
| HEAR_R | 217.5 | units | how far a full voice of a body of M=1 carries, in world units | derived: HEAR_R_M / M_PER_UNIT = 218 (870 m) |
| MAX_HEAR | 743.839 | units | the farthest any call carries: HEAR_R x MASS_HI^(1/3) (3.42, a test checks it) at a full voice | derived: 3 km; the callers' hash (HCELL_CALL) is sized from it |
| FOCAL_KEEP | 0.7 | - | a listener keeps attending to the same caller while it calls at least this share as loud as the loudest alike caller | ECO/HT: listeners stream one source among many (Bee & Micheyl 2008); hysteresis keeps attention from flickering |
| TMPL_HALF | 1000 | steps | steps over which the counts of a remembered tutor's syllable pairs halve | ECO/HT: a template fades without new tutoring; about a third of a year (Doupe & Kuhl 1999; Tchernichovski et al. 2001); the young copy form from elders, so a language changes by iterated learning (Kirby, Cornish & Smith 2008) |
| IMIT_RBAR | 0.05 | /step of calling | rate of the running mean of an organism's own match to its template | HT: a baseline, as RBAR_RATE is for reward |
| IMIT_AGE_LO | 0.1 | - | shortest sensitive period for memorising tutors, as a share of lifespan (the IMIT_AGE gene spans IMIT_AGE_LO..1) | ECO: songbirds memorise early in life, some species throughout (Brainard & Doupe 2002) |

## 14. Food

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| SCAVENGE | 0.8 | - | share of its meat-digesting skill a meat-eater brings to carrion | ECO: almost all carnivores scavenge; wolves feed on a kill for days (DeVault et al. 2003) |

## 15. Land

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| POND_R | 2 | cells | a pond site is the lowest cell within this many cells | ECO: about one basin per hundred square cells on rolling ground (prairie potholes, desert playas) |
| POND_GAP | 6 | cells | pond sites are at least this far from a lake or the sea (from a river: POND_GAP_RIVER) | HT: a pond beside a lake adds nothing |
| POND_GAP_RIVER | 2 | cells | pond sites are at least this far from a river (2026-11-01; POND_GAP from a lake or the sea) | ECO: floodplains hold ponds and wetlands beside their rivers (oxbow lakes, backswamps; Junk et al. 1989); HT: off the river's own banks |

## 16. Body

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| SOIL_WATER | 0.5 | - | share of thirst a land organism avoids on wet soil (dew, damp ground); water in food is counted separately (K_FOODWATER) | ECO: 0.8 before Revision K counted moist food too (ecology review: no double counting) |
| K_FOODWATER | 0.35 | /M | thirst relieved per unit of food eaten, per unit of water in it, per unit body mass | ECO: continuous eating of fully green forage about balances drying on dry ground (C_THIRST / (digestion x 0.75)); preformed water in forage and prey (ecology review) |
| MET_WATER | 0.5 | - | metabolic water: water made per unit of food energy digested, as mass of tissue burned | ECO: about 0.5 g of water per g of fat oxidised; how desert seed-eaters live without drinking |
| WATER_ECON | 0.7 | - | largest cut in water loss from the water-economy gene | ECO: kangaroo rats turn over about a quarter of the water of other rodents (Schmidt-Nielsen) |
| COST_WATERECON | 0.09 | xbasal | basal cost of full water economy (kidney and nasal water recovery) | ECO: kidneys use a few to 10% of basal metabolism |
| EXP_THIRST | -0.2 | - | mass exponent of thirst (water turnover per unit mass) | ECO: mass-specific water turnover scales about as M^-0.2 |
| COST_EYE | 0.04 | xbasal | basal cost per eye pair | ECO: eyes and visual brain are costly tissue |
| EYE_FOV | 0.3 | - | field of view added by each extra eye pair (share of the base field) | ECO: more eyes cover more directions |
| FIN_TURN | 0.2 | - | turning added per extra fin pair (times fin size) | ECO: paired fins steer and brake |
| LIFE_RANGE | 2 | - | range of the lifespan and development multipliers (x/÷) | - |
| COST_LONGEVITY | 0.25 | xbasal | basal cost per unit of extra lifespan (longer life needs more body repair) | ECO: disposable-soma theory: longevity costs somatic maintenance (Kirkwood 1977) |
| DEVEL_AGING | 0.5 | - | lifespan scales with the development multiplier to this power (fast developers age sooner) | ECO: the fast-slow life-history continuum |
| BRAIN_START | 24 | units | ordinary ancestors start with a random brain of up to this many memory units | the old limit before Revision J was 12; Revision M: 24 |
| BRAIN_MIN | 2 | units | fewest memory units a brain can have | - |
| COST_BRAIN | 0.25 | xbasal | basal cost of a brain of BRAIN_REF memory units, scaled by units in use | ECO: brain tissue costs about 2-20% of basal metabolism across vertebrates (humans ~20%); the largest possible brain costs a little more (Revision M) |
| BRAIN_REF | 256 | units | reference brain size for the brain cost: the largest brain (G.NH) | Revision M: was 12, the old limit |

## 17. Brain

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| BIG_BRAINS | 1 | - | big-brained ancestors per branch (the first on land, then alternating water and land), by default | designed: the user (Revision M): ONE lineage far smarter than the rest, like a human species; the big-brained land ancestor is also the voiced one |
| BIG_BRAIN_UNITS | 256 | units | memory units of a big-brained ancestor, by default | designed: the user (Revision M): the largest brain, ten times an ordinary ancestor's |
| DRIVE_FEMALE | 0.6 | - | strength of the mating instinct in females, against males (1) | designed by the user (2026-10-28): males are the more eager sex (Bateman 1948; Trivers 1972) |
| DRIVE_HERM | 0.8 | - | strength of the mating instinct in hermaphrodites, against males (1) | designed: between the two sexes |

## 18. Body

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| COST_HERM | 0.05 | xbasal | basal cost of being a hermaphrodite (keeping both sexes' organs) | ECO: hermaphrodites maintain male and female function |
| COST_IMMUNE | 0.12 | xbasal | basal cost of full immune investment | ECO: immune defence is costly (Lochmiller & Deerenberg 2000) |
| IMMUNE_PROTECT | 0.8 | - | share of infection and disease death that full immune investment prevents | ECO: partial, not total, protection |
| D_COMPAT | 0.25 | - | genetic distance at which mating succeeds with probability 1/e: p = exp(-(d/D)^4) (body genes / 127, RMS per gene) | ECO: Dobzhansky-Muller incompatibilities snowball with the square of the substitutions between lineages (Orr 1995); d^2 grows with substitutions: within a population (d ~0.13) p ~0.9, between independent ancestors (d ~0.4) p < 0.001 |
| KIN_LOOK | 0.25 | - | others whose visible body genes are this close look like kin (body genes / 127, RMS per gene) | ECO: animals recognise their kind by appearance |

## 19. Locomotion physics

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| FLIGHT_MULT | 10 | xbasal | metabolic rate of flight at its cheapest speed (the minimum of the power curve) for a body of FLY_M_REF, as a multiple of basal metabolism | ECO: flying birds and bats metabolise at 10-15x their resting rate, against 2-4x for animals walking (Tucker 1970; Schmidt-Nielsen 1972; Norberg 1990; Masman & Klaassen 1987); flight is cheap per km only because it is fast |
| FLY_M_REF | 1 | M | mass at which the least power of flight is FLIGHT_MULT x basal | assumption: M = 1 is about 1 kg (see above); the middle of the range of flying birds (10 g - 15 kg) on a log scale |
| EXP_FLY_REQ | 1.16667 | - | mass exponent of the least power flight requires | ECO: for geometrically similar fliers, P_min ~ M^(7/6) (Pennycuick 1968, 2008; Ellington 1991); against basal M^0.75 a bigger flier pays more per unit basal (x3.8 basal at M = 0.1, x31 at M = 16) |
| EXP_FLY_AVAIL | 0.7 | - | mass exponent of the power a body's flight muscles can deliver | ECO: flight muscle is a fixed share of mass, and its mass-specific power falls with wingbeat frequency, so power available ~ M^0.7 (Pennycuick 1968; Ellington 1991; Marden 1994) |
| FLY_M_MAX | 16 | M | heaviest body that can fly, with the largest wings (wing gene 1): a body flies while wings x (FLY_M_MAX / M)^(EXP_FLY_REQ - EXP_FLY_AVAIL) >= 1, power available over the least power required | ECO: power available ~M^0.7 against power required ~M^(7/6) caps flapping flight at ~12-19 kg (Pennycuick 1968, 2008; Ellington 1991): the heaviest living fliers, bustards and swans, weigh 12-18 kg. At wing gene 0.5 the ceiling is 16 x 0.5^2.14 = 3.6 |
| FLY_WING_MIN | 0.45 | - | smallest wing gene that can take off at any size (the flight muscle needed to lift the body's own weight) | ECO: takeoff needs flight muscle of at least ~16% of body mass, nearly whatever the size, in birds, bats and insects (Marden 1987); the largest flight-muscle ratios are ~35% (hummingbirds, pigeons; Greenewalt 1962; Hartman 1961), so wing gene 1 is taken as ~35% and 16/35 = 0.45. Without it the power margin alone let a 0.1 body fly on a wing gene of 0.09 |
| FLY_VMP | 4 | - | speed of least power (a flier's cruise) as a multiple of the walking-speed allometry C_SPEED M^EXP_SPEED x speed gene | ECO: birds cruise at ~5-15 m/s (Pennycuick 2008: V_mp ~ M^(1/6), as walking speed here, EXP_SPEED) while walkers travel at ~1-2 m/s (Alexander 2003), so a flier cruises several times faster than it walks (was FLY_SPEED = 1.4 for top speed, i.e. ~0.9 for cruise). Since 2026-11-01 the walking allometry is a day's travel per step (DMD_M), so this is a flier's daily travel against a walker's |
| FLY_V_CAP | 1744 | units/step | fastest an animal moves in one step through the air, in world units | world cap (2026-11-01): 7 km a step (FLY_V_CAP_M / M_PER_UNIT: 1,744 units of 4 m, several times around a 1 km map; positions wrap, World.Wrap). An animal perceives what lay within sight of the way it travelled (Decide.Perceive), out to MAX_PERCEIVE; this keeps the fastest fliers' way inside it. Real fliers could go farther still (12 m/s for a step is 126 km): the cost per distance (FLY_COT_RATIO) is calibrated instead of the speed. Was 16 units (870 m) when sight reached 32 units |
| FLY_V_CAP_M | 6976 | m/step | fastest an animal moves in one step through the air, in metres | world cap (2026-11-01): see FLY_V_CAP; it was 128 units of 54.5 m |
| FLY_COT_RATIO | 0.4 | - | cost per distance of flying at the speed of least power, against walking's at the same mass (M = FLY_M_REF, full legs, two pairs) | ECO: flight's cost of transport is 1/3-1/2 of running's at equal mass (Tucker 1970; Schmidt-Nielsen 1972). It sets how long in a step a flier is really in the air: it flies at FLIGHT_MULT x basal for the share of the step its distance needs (Body.FlightFullStep), but the whole step when it cannot land |
| FLY_X_MAX | 1.6 | - | fastest level flight as a multiple of the speed of least power, when power allows (and at most FLY_V_CAP) (power there is (3/x + x^3)/4 = 1.49 x least) | ECO: birds fly up to ~1.5-2x their minimum-power speed; the maximum-range speed is ~1.3-1.6x (Pennycuick 2008; Tobalske et al. 2003). A flier with less margin flies at most where power required meets power available |
| FLY_U_MIN | 0.38 | - | slowest speed counted on the power curve, as a share of the speed of least power: hovering costs (3/u + u^3)/4 = 2x the least power | ECO: the power curve is U-shaped (induced power ~1/v, profile and parasite power ~v^3; Pennycuick 1968; Tucker 1973); hovering costs about twice the minimum (Norberg 1990; Ellington 1984) |
| LEG_SWING | 0.26 | - | share of the cost of walking that swinging the legs takes, for LEG_PAIRS_REF pairs of full legs at top speed; the rest (stance) every walker pays | ECO: swing-phase muscles use ~26% of limb-muscle energy in walking and running guinea fowl (Marsh et al. 2004; Modica & Kram 2005: ~20% in running humans); swing work per distance rises with speed (stride frequency) |
| LEG_PAIRS_REF | 2 | pairs | leg pairs of the body whose cost of transport the walking cost (C_MOVE, EXP_MOVE) describes | ECO: the cost-of-transport line is fitted to quadrupeds and bipeds (Taylor, Heglund & Maloiy 1982); hexapods and octopods lie near it (Full & Tu 1991), so each pair beyond this adds its swing |
| LEG_STAB | 0.6 | - | share of the slowing by rough ground that four pairs of full legs prevent (one pair: none; linear in pairs - 1 and leg size) | ECO: many legs keep a small, slow body statically stable on uneven ground (an alternating tripod; Ting, Blickhan & Full 1994); cockroaches cross rough terrain at near full speed (Sponberg & Full 2008) |
| ROUGH_REF | 0.15 | elev/cell | rise of the ground to a neighbouring cell at which ground counts as fully rough | HT: about the 90th percentile of land cells on a 256 map (0.20) and the 99th on a 1024 map (0.18) |
| ROUGH_SLOW | 0.5 | - | walking speed lost on fully rough ground by an M = 1 body without stabilising legs: v / (1 + ROUGH_SLOW x rough x M^(-1/3) x (1 - stability)) | ECO/HT: obstacles are bigger against a small body's legs (leg length ~ M^(1/3)), so roughness slows the small most (Kaspari & Weiser 1999: the size-grain hypothesis) |
| LOAD_DRAG | 0.2 | - | walking speed lost per unit of bone stress above an unarmoured two-pair body's: drag = LOAD_DRAG x M^(1/3) x ((1 + armour) / sqrt(leg pairs) - 1/sqrt(2)) | ECO: weight grows as M, bone section as M^(2/3), so bone stress ~ M^(1/3) and heavier bodies must move more carefully (Biewener 1989, 1990); more legs share it. At M = 1 the armour term equals the old ARMOUR_DRAG / sqrt(pairs) |

## 20. Clades

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| CLADES | 32 | clades | clade slots (lineages that can exist at once) | -: limited by the console frame (5 bits) |
| ANCESTORS_LAND | 10 | ancestors | ancestors that start on land | user choice |
| ANCESTORS_WATER | 6 | ancestors | ancestors that start in water | user choice |
| ANCESTOR_COUNT | 600 | organisms | founding organisms per ancestor | HT |
| FOUND_FILL | 0.25 | - | a lineage starts with this share of the animals its home range's plants can feed at its body size and brain | ECO: founding at a quarter of the carrying capacity leaves room for the founders' first wave of young (2026-10-30: a fixed 600 per ancestor started big bodies at many times their food, and 70% of founders starved; at a half, the first wave doubled the population by step 300 and it crashed by 90%; at a quarter, three worlds kept 3-4 lineages and 190-1,150 animals at step 8,000 against 2-4 and 18-630) |
| FIELD_METAB | 1.5 | xbasal | a day of living costs this many times the basal cost (moving, growing, breeding, keeping warm) | measured: this world's own animals spend 1.45x basal over the first 3000 steps (2026-10-30, seed 7: basal 0.92, move 0.12, growth 0.16, breeding 0.15, heat 0.10); free-living mammals 2-3x (Nagy 1987) |
| FORAGE_MARGIN | 3 | - | an ancestor must be able to eat this many days' need per step of eating (one bite of a half-grazed cell, or its gut's rate) | measured: random founders eat on 25-50% of steps (2026-10-30), so a forager must gain at least 2-4x its need per bite to live |
| BRAIN_SHARE_MAX | 0.25 | - | the largest share of an ancestor's basal cost its brain may take | ECO: brain tissue takes at most ~20-25% of basal metabolism (humans ~20%; Aiello & Wheeler 1995; Mink et al. 1981); 2026-10-30: the big-brained ancestor drew a 0.1-0.4 body whose 256-unit brain cost more than the body, and died in 9 worlds of 9 |
| ANCESTOR_TRIES | 64 | - | random ancestor bodies tried per ancestor for one its home range can feed | HT: enough that almost every ancestor finds a viable body |
| FOUND_MIN | 50 | organisms | the smallest founding group an ancestor must be able to feed (FOUND_FILL x capacity) | ECO: the 50 of the 50/500 rule for a minimum viable population (Franklin 1980); smaller founding groups go extinct by chance (Allee effect) |
| SPEC_EVERY | 2000 | steps | steps between speciation checks | - |
| MIN_SPLIT | 40 | organisms | smallest cluster that can become a species | ECO: a viable population |
| SPLIT_VIS | 1 | - | two forms are a new clade only if some visible trait differs by this many visible units (1 = half again the mass, 0.15 of an organ, or a pair of limbs or eyes) | designed: the user asked for subspecies that are clearly visual (Revision L); fertility then says subspecies or species |
| CLADE_RECYCLE | 20000 | steps | an extinct clade's slot is reused only after this many steps | - |

## 21. Reward

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| R_SPEND | 1 | - | energy spent on its choices (strikes, calls, moving, heat) counts against the reward at this share of the weight energy eaten counts for it | ECO: animals learn by net energy gain (optimal foraging; Stephens & Krebs 1986); 1 = a unit spent weighs as a unit eaten. 2026-10-30: without it, calling (19-24% of basal) rose from ~38% to ~65% of steps by learning |

## 22. Reproduction

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| READY_LO | 0.25 | xEmax | lowest breeding condition a body can have: it breeds once its reserve is above this share (B_BREEDE gene) | ECO: income breeders breed on what they eat as they go, capital breeders on stored reserves (Jonsson 1997; Stephens et al. 2009); was the fixed READY_ENERGY = 0.6 until 2026-10-30 |
| READY_HI | 0.9 | xEmax | highest breeding condition a body can have | ECO: as READY_LO |
| F_BIRTH | 0.4 | xEmax child | newborn energy (paid by parent at conception) | V1 |
| C_MATE | 0.02 | E/M | mating cost per partner | HT |
| REFRACT | 0.1 | - | a male's refractory period after mating, as a share of the breeding cooldown | ECO: male refractory periods are short against female breeding cycles (Bateman 1948; Trivers 1972) |

## 23. Care

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| CARE_RATE | 1.5 | xbasal | milk or chick-feeding energy a mother can give per step, as a multiple of her basal cost (shared by all her young) | ECO: peak lactation milk energy is about 1-2x maternal basal metabolic rate (Hanwell & Peaker 1977) |
| CARE_EFF | 0.7 | - | efficiency of turning maternal energy into milk or food for the young | ECO: milk synthesis efficiency 0.6-0.8 |
| WEAN | 0.4 | - | care ends (weaning) when the young has built this share of its adult tissue | ECO: mammals wean at roughly a third to a half of adult mass |
| CARE_RESERVE | 0.4 | xEmax | a mother keeps at least this share of her energy reserve for herself | ECO: females in poor condition reduce or stop provisioning |

## 24. Pathogens

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| PG_REP_LO | 0.03 | /step | within-host growth rate at gene 0 (log scale to PG_REP_HI) | ECO: in vivo doubling times run from hours (Salmonella in mouse tissues, Grant et al. 2008) to about a day (M. tuberculosis): ln2/0.03 = 23 steps (~2.8 days at STEP_H) |
| PG_REP_HI | 0.5 | /step | within-host growth rate at gene 255 | ECO: ln2/0.5 = 1.4 steps (~4 h), a fast bacterial pathogen in a host |
| PG_QS_LO | 0.01 | load | quorum-sensing threshold at gene 0 (log scale to PG_QS_HI), in load | ECO: QS switches at a cell density set by the signal's receptor affinity, which mutates readily (Miller & Bassler 2001) |
| PG_QS_HI | 3 | load | quorum-sensing threshold at gene 255, in load (above 1 a host never holds a quorum: always stealthy) | ECO: QS-blind (lasR) mutants are common in chronic infections (Smith et al. 2006) |
| PG_HILL | 4 | - | steepness (Hill coefficient) of the quorum switch: on = L^n / (L^n + threshold^n) | ECO: autoinduction makes QS a switch-like positive feedback (Miller & Bassler 2001); LuxR-type responses have Hill coefficients ~2-4 |
| PG_STEALTH | 0.1 | - | share of toxin and shedding expressed below quorum (stealth mode) | ECO: QS controls most secreted virulence factors; QS-off cells make a small fraction (Pseudomonas lasR mutants, Rumbaugh et al. 2009) |
| PG_DETECT | 0.3 | - | immune detection below quorum, relative to a quorate population | ECO: QS-regulated exoproducts are what the immune system mostly sees; innate receptors still see the cells (PAMPs, Janeway 1989) |
| PG_DECAY_HI | 0.3 | /step | decay rate in the environment at persistence 0 (log scale to PG_DECAY_LO) | ECO: enveloped viruses and vegetative bacteria die within hours outside a host (half-life ~2 steps, ~6 h) |
| PG_DECAY_LO | 0.0002 | /step | decay rate in the environment at persistence 255 | ECO: spores and helminth eggs survive years (half-life ~3,500 steps, 1.2 years); anthrax spores decades (Walther & Ewald 2004) |
| PG_DRY | 3 | x | extra decay on dry land (not water, pond or flood) over water | ECO: desiccation and UV kill enteric bacteria faster on dry surfaces than in water |
| PG_PERS_COST | 0.5 | - | within-host growth lost at full persistence (spore coats and capsids cost replication) | ECO: survival-reproduction trade-off among phages (De Paepe & Taddei 2006); sporulation halts growth |
| PG_BREADTH | 2 | - | host-match width gained at full host breadth (x(1 + this) of MHC_W) | ECO: multi-host pathogens are common (Woolhouse, Taylor & Haydon 2001) |
| PG_GEN_COST | 0.5 | - | within-host growth lost at full host breadth (generalist cost) | ECO: a jack of all hosts is master of none (Leggett et al. 2013); specialists grow faster in their own host |
| MHC_W | 0.35 | - | match width: a strain grows in a host whose recognition genes are within this RMS distance (/127) of its antigen | ECO: matching-alleles host-parasite genetics (Hamilton 1980; Lively 2010): rare host genotypes escape, which can favour sex |
| MHC_MUT | 6 | int8 | mutation of a strain's antigen per transmission (SD) | ECO: pathogens evolve far faster than their hosts; antigenic drift (Smith et al. 2004) |
| PG_MUT | 4 | of 255 | mutation of each trait gene per transmission (SD) | ECO: pathogens evolve far faster than their hosts; virulence evolves within decades (myxoma, Fenner 1983) |
| PG_INOC | 0.01 | load | load at infection (the founding dose) | ECO: infections start from a small bottleneck of founders (Abel et al. 2015) |
| PG_CLEAR | 0.002 | load | load below which an infection is cleared | HT: a fifth of the founding dose |
| PG_KILL | 0.6 | /step | killing rate per unit immune effector at full detection | ECO: with PG_RISE an adaptive response clears a fast strain in ~1-2 weeks (30-70 steps); a strain growing too slowly to stimulate expansion above PG_E_DECAY persists (chronic carriage) |
| PG_RISE | 0.3 | /step | most a specific immune response multiplies per step (clonal expansion, x stimulation x detection x (PG_NAIVE + effector)) | ECO: within-host dynamics dX/dt = rho X P/(P + phi) (Antia, Levin & May 1994); activated T cells divide every 6-8 h (De Boer et al. 2001), ~0.25-0.35 doublings per 2.9-h step |
| PG_NAIVE | 0.02 | - | naive precursors a specific response expands from (in effector units) | ECO: a few hundred naive precursors per antigen, against ~10^5-fold expansion at the peak (Blattman et al. 2002) |
| PG_HALF | 0.01 | load | load at which immune stimulation is half its maximum | ECO: small antigen doses start an adaptive response (Bachmann & Jennings 2010); here the founding dose PG_INOC, so a stealthy strain is seen less but not ignored |
| PG_E_DECAY | 0.02 | /step | decay of the immune effector per step (contraction) | ECO: effector T cells contract within days to weeks after the peak |
| PG_IMM_BASE | 0.5 | - | immune stimulation of a host with no immunity gene (innate); the gene adds up to 1 | ECO: innate immunity alone controls many infections (Janeway & Medzhitov 2002) |
| PG_MHC_DIV | 0.5 | - | gain in immune stimulation at full MHC diversity (the spread of its recognition genes) | ECO: MHC heterozygote advantage: more distinct alleles present more antigens (Doherty & Zinkernagel 1975; Penn, Damjanovich & Potts 2002) |
| PG_CROSS_W | 0.25 | - | cross-immunity width: protection by memory falls as exp(-d^2/w^2) of antigen RMS distance (/127) | ECO: titres fall with antigenic distance; drifted influenza escapes old immunity (Smith et al. 2004) |
| PG_MEM_DECAY | 0.0003 | /step | decay of immune memory per step | ECO: antibody half-lives of 1-10+ years (Amanna, Carlson & Slifka 2007): ~2,300 steps (0.8 years) here |
| PG_RECALL | 1 | - | immune effector at once on reinfection, per unit cross-immunity (the secondary response) | ECO: memory responses are faster and larger (Ahmed & Gray 1996) |
| PG_DMG | 0.1 | /step | tissue damage per step per unit toxin x load (1 = death by disease) | ECO: virulence is damage from pathogen load and toxins (Casadevall & Pirofski 1999); untreated cholera can kill within a day of the first symptoms (Sack et al. 2004): a full-toxin strain at full load kills in ~11 steps (~1.3 days) |
| PG_REPAIR | 0.01 | /step | repair of tissue damage per step at a full energy reserve (x reserve share: a starving host cannot repair) | ECO: malnutrition raises case fatality (Scrimshaw, Taylor & Gordon 1968) |
| PG_COST_LOAD | 0.2 | xbasal | extra basal cost per unit load (what the pathogen takes) | ECO: parasites take a share of host resources |
| PG_COST_IMM | 0.3 | xbasal | extra basal cost per unit immune effector (fever, immune response) | ECO: an immune response raises resting metabolism 10-30%; fever ~10-13% per °C (Lochmiller & Deerenberg 2000) |
| PG_MALAISE | 0.5 | - | malaise felt (as pain) per unit immune effector; tissue damage adds to it | ECO: sickness behaviour is driven by cytokines of the immune response (Hart 1988) |
| PG_SHED | 0.1 | /step | shedding per step per unit load (all routes) | HT |
| PG_TOX_SHED | 2 | - | extra shedding per unit toxin (x(1 + this x toxin)) | ECO: toxins drive diarrhoea and coughing that carry pathogens out (cholera toxin; Ewald 1994): the virulence-transmission trade-off (Anderson & May 1982; Alizon et al. 2009) |
| PG_DUNG | 3 | x | faeces concentrate the dung route's shedding (only while the host's gut passes waste) | ECO: enteric pathogens reach 10^6-10^9 per gram of faeces |
| PG_INF | 100 | /dose | infection hazard per unit dose (x match x (1 - cross-immunity) x immunity gene's protection); a dose of 1 is a whole host's burden | ECO: exponential dose-response (Haas 1983); infective doses are 10^1-10^6 organisms (Kothary & Babu 2001) against 10^9-10^12 in a sick host, so ID50 here (0.7% of a burden) is on the high, hard-to-catch side |
| PG_TOUCH | 0.02 | /step | share of a cell's reservoir picked up per step by an animal standing in it | HT |
| PG_EAT | 0.2 | /bite | share of a cell's reservoir swallowed with a bite of food from it | HT: grazing and dung-eating are the faecal-oral route |
| PG_DRINK | 0.2 | /drink | share of a water cell's reservoir swallowed with a drink | HT: a waterhole is where waterborne pathogens meet hosts (cholera, Ewald 1991) |
| PG_FLOW | 0.5 | /step | share of a river cell's pathogens carried one cell downstream per step | ECO: rivers run far faster than a field cell (GEO_CELL_M, 218 m) per step (STEP_H, 2.9 h); the downstream-first update moves a cell's load one cell per step at most |
| PG_CARCASS | 1 | - | share of its load a dead host leaves in the ground where it died | ECO: carcasses are reservoirs (anthrax, Turner et al. 2014) |
| PG_RES_MIN | 1E-05 | load | reservoir load below which a cell is clean | HT: picked up by touch it is a hazard of 2e-5 per step |
| PG_FOUNDERS | 12 | strains | founding strains at world start, each a random genome in a few random cells | HT: replaces the old flat spillover; nothing seeds them again |
| PG_SEED_CELLS | 500 | cells | cells each founding strain is laid in, at random over the map | HT: 12 x 500 cells is ~9% of a 256^2 field: environmental microbes are everywhere, most of them unable to grow in any host |
| PG_SEED | 2 | load | load put in each founding cell | HT |
| PG_STRAIN_D | 0.1 | - | two pathogens are one strain (for telemetry) within this RMS genome distance (traits /255, antigen /127) | HT: one transmission drifts a genome ~0.03 by PG_MUT and MHC_MUT, so a strain is about a dozen transmissions of drift |
| PG_CLUSTERS | 256 | strains | most strains telemetry tells apart | HT |

## 25. Weather

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| RAIN_SD | 0.25 | - | standard deviation of the yearly rainfall anomaly | ECO: interannual rainfall CV 20-35% in semi-arid regions |
| RAIN_RHO | 0.4 | - | persistence of the rainfall anomaly from one year to the next (AR(1)) | ECO: droughts cluster in runs of years |
| RAIN_MAX | 0.7 | - | limit of the rainfall anomaly | - |

## 26. Vegetation

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| R_G | 0.01 | /step | grass growth | V1 |
| M_P | 0.001 | /step | sapling maturation | HT |
| K_N | 0.3 | - | nutrient half-saturation | HT |
| R_B | 0.004 | /step | browse regrowth toward capacity | HT |
| D_F | 0.004 | /step | fruit rot | HT |
| D_SD | 0.001 | /step | seed decay | HT |
| D_NC | 0.01 | /step | nectar decay | HT |
| R_R | 0.0015 | /step | root growth | HT |
| C_LIT | 0.0004 | /step | leaf senescence into litter | HT |
| POLL_GAIN | 8 | - | pollination increment per unit nectar eaten | HT |
| D_POLL | 0.005 | /step | pollination decay | HT |
| R_A | 0.008 | /step | algae growth | HT |
| M_A | 0.0015 | /step | algae death into detritus | HT |
| R_PH | 0.003 | /step | phytoplankton growth | HT |
| M_PH | 0.0015 | /step | phytoplankton death into detritus | HT |
| C_NUTUSE | 0.6 | - | nutrient used per unit plant growth | V1 |
| C_NUTGAIN | 0.7 | - | rot share that becomes nutrient | V1 |
| WEATHER | 0.0015 | /step | background weathering | V1 |
| N_DIFF | 0.05 | /step | nutrient mixing between neighbouring water cells | HT: water mixes, soil does not |
| ROT_CARRION | 0.0025 | /step | carrion rot | V1 |
| ROT_DUNG | 0.0015 | /step | dung rot | HT |
| ROT_LITTER | 0.0006 | /step | litter rot | HT |
| ROT_WOOD | 0.00015 | /step | wood rot | HT |
| ROT_DETRITUS | 0.0015 | /step | detritus rot | HT |
| N_MAX | 1.5 | - | max nutrient | V1 |
| RUNOFF | 0.002 | /step | run-off: share of a shore cell's soil nutrient that washes into each adjacent water cell | HT |

## 27. Plants

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| PLANT_EVERY | 8 | steps | steps between two updates of a row of plants (stripes: rows y = step mod this), and dt of an update | HT: Revision S; plants change over hundreds of steps, a stripe costs 1/8 of the land per step |
| PLANT_GCAP | 65536 | genotypes | largest genotype table (id 0 = empty); a map gets one entry per field cell, between 4,096 and this | HT: ushort ids; 6 MB at most |
| PLANT_FOUNDERS | 128 | genotypes | founding plant genotypes, every trait uniform at random, each its own lineage | HT: Revision S |
| PLANT_DRAW | 6 | - | founders of a stratum drawn per cell, of which the best grower there takes it | ECO: community assembly as a filter of the regional pool by the site (Keddy 1992) |
| PLANT_MU | 0.03 | /recruit | chance that a recruit's genotype has one trait one level up or down | HT: with crosses, a few new genotypes per row update |
| PLANT_PEND | 8 | - | new genotypes a row can queue per update (more keep their seed parent's) | HT |
| PLANT_CENSUS | 64 | steps | steps between censuses that free genotypes nothing refers to | HT |
| PLANT_SPINUP | 1 | years | years the plants (with weather, fields, fire and water, no animals) grow before step 0 | HT: Revision S; the founders' cover relaxes to what their genes can hold |
| WLOG_HARM | 2 | x | production lost to waterlogging: production x max(0, 1 - WLOG_HARM x anoxia (WetDur) x (1 - flood tolerance)); a plant of tolerance under 1 - 1 / WLOG_HARM cannot grow in permanently waterlogged ground | ECO: root anoxia stops aerobic respiration and nutrient uptake; most crop and upland plants lose most of their growth to weeks of waterlogging, while wetland plants with aerenchyma, adventitious roots and anaerobic metabolism grow on (Kozlowski 1997; Colmer & Voesenek 2009) |
| FLOOD_COST | 0.08 | - | share of production a fully flood-tolerant plant spends on its tolerance (aerenchyma, adventitious roots, radial oxygen barriers), x the trait | ECO: aerenchyma and root barriers cost structure and hydraulic capacity, and flood-tolerant species are poor competitors on drained ground (Lenssen et al. 1999; Colmer & Voesenek 2009) |
| H_LO | -1.3 | log10 m | log10 of the height (m) at trait level 0 | ECO: herbs from 5 cm |
| H_SPAN | 2.9 | log10 m | log10 span of height over the trait | ECO: up to 40 m trees |
| H_HERB | 0.8 | m | height below which a plant is a herb | ECO: growth-form boundaries (Raunkiaer 1934) |
| H_SHRUB | 5 | m | height below which a woody plant is a shrub (above, a tree) | ECO: as H_HERB |
| H_REF | 0.3 | m | height whose relative growth rate is R_G | HT: a 30 cm herb grows as the old grass |
| H_SAP | 2 | m | height of a sapling (growth rate, fire, browse) | ECO |
| EXP_R0 | 0.82 | - | relative growth rate falls with height as H^-EXP_R0 | ECO: metabolic scaling of plant growth (Enquist et al. 1999); with this the old shrub and tree rates come back at 1.3 m and 15 m |
| LES_A | 1.6 | x | leaf economics: photosynthetic return at LMA level 0 | ECO: leaf economics spectrum (Wright et al. 2004; Reich 2014) |
| LES_B | 1.2 | x | fall of the return to LMA level 15 (1.6 - 1.2 = 0.4) | ECO: Wright et al. 2004 |
| CLIT_EXP | 1.5 | - | leaf turnover C_LIT x 2^(CLIT_EXP (1 - 2 x_LMA)): leaf lifespan spans 2^3 over the trait | ECO: leaf lifespan spans ~10x across the spectrum (Wright et al. 2004) |
| LEAF_EXP | 0.3 | - | leaf share of a plant falls as (H/H_REF)^-LEAF_EXP (turnover per unit cover) | ECO: leaf mass fraction declines with size (Niklas 1994); 1, 0.5 and 0.3 of the old grass, shrub and tree litter |
| KL_A | 0.05 | - | light half-saturation at tough leaves | ECO: shade-tolerant leaves saturate at low light (Kitajima 1994) |
| KL_B | 0.4 | - | extra light half-saturation of cheap leaves | ECO: Kitajima 1994 |
| ROOT_OVL | 0.3 | - | water taken by the other slots per unit cover at equal rooting depth | ECO: two-layer root partitioning (Walter 1971); overlap falls with depth difference (Schenk & Jackson 2002) |
| RHO_LO | 0.1 | - | root share of biomass at ROOT level 0 (food: Pool.Root) | ECO: root:shoot ratios 0.1-0.7 (Bloom, Chapin & Mooney 1985) |
| RHO_SPAN | 0.6 | - | root share span over the trait | ECO: as RHO_LO |
| W_SUP | 3 | - | water-limited growth at full soil water, x r0 (colimitation with carbon) | HT: carbon and water colimit (Farquhar & Richards 1984); 3 makes a mid plant mostly carbon-limited when wet |
| WUE_GC | 0.35 | - | carbon gain lost at full water-use efficiency (stomata kept shut) | ECO: the WUE-assimilation trade-off (Cowan & Farquhar 1977) |
| WMIN_0 | 0.1 | - | soil water at which growth stops, base | ECO: wilting point |
| WMIN_H | 0.35 | - | extra stopping water for a tall plant, x x_H^0.8 | ECO: tall plants embolise first (Ryan & Yoder 1997; Koch et al. 2004) |
| WMIN_D | 0.35 | - | stopping water a fully drought-tolerant plant saves | ECO: xylem safety (Choat et al. 2012) |
| DRT_M | 0.7 | - | share of drought mortality drought tolerance saves | ECO: Choat et al. 2012 |
| DRT_GC | 0.4 | - | carbon gain lost at full drought tolerance | ECO: the safety-efficiency trade-off (Gleason et al. 2016) |
| DROUGHT_M | 0.15 | x r0 | drought mortality per step at zero water, x r0 | HT |
| TOPT_LO | -2 | °C | best temperature of growth at TOPT level 0 | ECO: alpine and arctic plants (Körner 2003) |
| TOPT_SPAN | 36 | K | span of the best temperature over the trait | ECO: up to 34 °C (tropical C4) |
| T_WIDTH | 20 | K | temperature at which growth stops, away from the optimum | ECO: thermal performance curves (Kingsolver & Huey 2008) |
| TFROST_HI | -3 | °C | frost that kills the leaves of the most warmth-loving plant | ECO: chilling-sensitive tropical plants (Sakai & Larcher 1987) |
| TFROST_SPAN | 35 | K | extra frost a fully cold-adapted plant survives | ECO: Sakai & Larcher 1987: boreal plants survive -40 °C |
| FROST_LOSS | 0.2 | - | share of leafy cover a frost kills per update | HT |
| DECID_P | 0.6 | - | water x temperature factor below which a fully deciduous plant sheds its leaves | ECO: deciduousness as a response to an unfavourable season (Chabot & Hicks 1982; Kikuzawa 1991) |
| DECID_HYST | 0.05 | - | hysteresis of leaf shedding and flushing | HT |
| RELEAF | 0.1 | - | share of cover a new flush of leaves costs | ECO: Kikuzawa 1991 |
| LEAFOFF_RESP | 0.2 | - | respiration and drought loss of a leafless plant, x a leafy one's | ECO: dormancy |
| RESP | 0.15 | x r0 | respiration per step, x r0 x leaf return | ECO: about half of gross production is respired; here the net rate of a mid plant is r0 |
| A_MIN | 0.05 | - | least share of production in growth | HT |
| AR_LO | 0.05 | - | reproductive allocation at REPRO level 0 | ECO: r/K allocation (Grime 1977; Abrahamson 1980) |
| AR_SPAN | 0.45 | - | span of reproductive allocation over the trait | ECO: annuals put up to half their production into seed |
| PHEN_PEAK | 3.657 | - | peak of the flowering pulse PHEN_PEAK ((1 + cos)/2)^4, so its yearly mean is 1 | - |
| REPRO_RATE | 0.01 | /step | production per unit leafy cover per step that reproduction draws on | ECO: production per unit leaf area is similar across growth forms (trees grow slowly as cover because most goes into wood); R_G of the old grass, so a mid tree fruits about as the old trees did |
| SEED_REF | 0.0001 | cover | seed mass (cover units) at the middle of the SEED trait | HT |
| SEED_SPAN | 3 | log10 | orders of magnitude of seed mass over the trait | ECO: seed mass spans 10^3 in a flora (Moles & Westoby 2004) |
| EST_EXP | 0.4 | - | establishment per seed rises as (m_s/ref)^EST_EXP | ECO: the seed size-number trade-off (Smith & Fretwell 1974; Moles & Westoby 2004) |
| BANKD_EXP | 0.3 | - | seed-bank decay falls as (m_s/ref)^BANKD_EXP (persistence) | ECO: Moles & Westoby 2004 |
| PULP_MAX | 4 | - | pulp per unit seed at FRUIT level 15 | ECO: pulp:seed ratios (Howe & Smallwood 1982; Jordano 2000) |
| WING_COST | 0.5 | - | extra cost per seed of full wings | ECO: Greene & Johnson 1993 |
| HOOK_COST | 0.3 | - | extra cost per seed of full hooks | ECO: Sorensen 1986 |
| V_T0 | 2 | m/s | terminal velocity of an unwinged reference seed | ECO: Greene & Johnson 1993 |
| WING_K | 3 | - | wings cut terminal velocity by 1 + WING_K x | ECO: winged seeds fall at 0.5-1 m/s |
| U_WIND | 3 | m/s | mean wind at release height | ECO: the ballistic model D = U H / v_t (Nathan et al. 2002) |
| CELL_M | 16 | m | a field cell's side in metres, for dispersal distances | derived (2026-11-01): GEO_CELL_M, the one cell size (it was a separate 25 m while a field cell was 218 m) |
| NB_MIN | 0.02 | - | least share of dry seed that leaves its cell (secondary dispersal by rain wash, ants, rolling) | ECO: secondary dispersal (Vander Wall & Longland 2004) |
| WIND_BLOCK | 16 | cells | side of a block of long-distance seed (it lands spread over the 3x3 blocks around) | HT: a simple fat tail, deterministic and mass-conserving |
| WIND_POLL | 0.4 | - | seed set from wind pollen per unit leafy cover of the same stratum next door | ECO: pollen limitation (Baker 1955; Knight et al. 2005) |
| ANIMAL_POLL | 0.6 | - | seed set per unit pollination (Poll) by animals | ECO: Rathcke & Lacey 1985 |
| D_PC | 24 | levels | genetic distance (sum of trait-level differences) of crosses: compatibility exp(-(D/D_PC)^4) x flowering overlap^2 | HT: about 1.2 levels per trait |
| NECTAR_SHARE | 0.2 | - | share of reproductive allocation in nectar at NECTAR level 15 | ECO: nectar costs a few to 30% of reproduction (Southwick 1984; Pyke 1991) |
| TOX_E | 0.7 | - | herbivore energy lost to full toxins | ECO: plant secondary compounds (Herms & Mattson 1992; Robbins 1987) |
| TOX_FRUIT | 0.2 | - | share of the toxin in fruit pulp | ECO: ripe fruit is mostly undefended (Cipollini & Levey 1997) |
| TOX_COST | 0.25 | - | share of production full toxins cost | ECO: growth-defence trade-off (Coley, Bryant & Chapin 1985) |
| THORN_B | 0.6 | - | bite lost to full thorns, for a big bite | ECO: spines restrict bite size (Cooper & Owen-Smith 1986) |
| B_HALF | 0.05 | bite | bite at which thorns cost half their full effect | HT: C_BITE at M=1 is 0.06 |
| THORN_COST_W | 0.15 | - | share of production full thorns cost a woody plant | ECO: Hanley et al. 2007 |
| THORN_COST_H | 0.05 | - | share of production full silica or hairs cost a herb | ECO: Massey et al. 2007 |
| RESPROUT_MAX | 0.85 | - | share of above-ground fire loss a full resprouter grows back | ECO: resprouting (Bond & Midgley 2001; Pausas & Keeley 2014) |
| RESPROUT_COST | 0.2 | - | share of production full resprouting (stored reserves) costs | ECO: Bond & Midgley 2001 |
| BARK_MAX | 0.9 | - | stem survival of a fire with full bark | ECO: bark thickness and fire survival (Hoffmann et al. 2012) |
| BARK_COST | 0.15 | - | share of production full bark costs | ECO: Hoffmann et al. 2012 |
| FLAME_H | 2 | m | flame height per unit fuel | ECO: stems above the flames survive (Higgins, Bond & Trollope 2000) |
| BANK_FIRE | 0.5 | - | share of the seed bank a fire leaves | ECO: buried seed survives fire |
| NFIX_N | 0.6 | - | nutrient a full nitrogen fixer adds to what it reads | ECO: symbiotic fixation (Vitousek & Field 1999) |
| NFIX_COST | 0.2 | - | share of production full fixation costs | ECO: Vitousek & Field 1999 |
| NFIX_T | 18 | °C | temperature below which fixation costs more | ECO: fixers are rare in the cold (Houlton et al. 2008) |
| NFIX_TW | 12 | K | cooling that doubles the cost of fixation | ECO: Houlton et al. 2008 |
| NFIX_DIG | 0.2 | - | extra energy herbivores get from a full fixer's leaves | ECO: nitrogen-rich leaves |
| DIG_LO | 1.25 | - | energy multiplier of the cheapest leaves | ECO: tough leaves are poor food (Coley 1983) |
| DIG_SPAN | 0.5 | - | fall of the energy multiplier to the toughest leaves | ECO: Coley 1983 |
| SELF_LO | 0.1 | - | seed set without pollen at SELF level 0 | ECO: self-compatibility (Baker 1955) |
| SELF_SPAN | 0.8 | - | extra seed set without pollen at full selfing | ECO: Baker 1955 |
| INBR_LO | 0.5 | - | inbreeding depression of selfed recruits of an outcrosser | ECO: Husband & Schemske 1996 |
| INBR_SPAN | 0.3 | - | inbreeding depression purged by full selfing | ECO: Husband & Schemske 1996 |
| LIFE_HERB | 3000 | steps | life expectancy of a reference herb (x (H/H_REF)^LIFE_EXP x (0.5 + LMA)) | HT: a year; a 15 m tree lives ~20x longer, close to the old tree mortality |
| LIFE_EXP | 0.75 | - | life expectancy rises as H^LIFE_EXP | ECO: metabolic scaling of lifespan (Marba et al. 2007) |
| B_SEED0 | 0.01 | cover | most cover recruited from a bank per update | HT |
| EST0 | 0.005 | - | establishment per seed per update at full growth | HT: a bank of 0.05 cover of reference seed recruits ~90% of B_SEED0 |
| BROWSE_H | 6 | m | height an animal browses up to | ECO: browsers reach ~5-6 m (du Toit 1990) |
| BROWSE_LOSS | 1 | - | woody cover lost per unit browse eaten | mass balance (2026-11-01): leaves and shoots eaten are the woody plant's own mass, so a unit of browse is a unit of its cover (RHO_BROWSE is the energy of woody tissue in reach). Was 0.5 (HT, as the old sapling loss): browse, regrowing toward its own capacity, fed browsers twice what the plants lost |
| BURR_PICK | 0.002 | /step | share of a bank of full hooks caught in a coat per step, x M^(2/3) x (0.3 + fur) | ECO: epizoochory (Sorensen 1986; Couvreur et al. 2004) |
| BURR_DROP | 0.03 | /step | share of seed in a coat that falls off per step | ECO: Couvreur et al. 2004 |
| M_GAPE | 1 | M | body mass whose gape passes a reference seed whole | ECO: gape limits the seed size dispersed (Wheelwright 1985) |

## 28. Fire

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| LAMBDA_FIRE | 2E-06 | /cell/step | ignition rate per unit dry fuel per land cell | HT: ~1 ignition per 100 steps in the dry season |
| C_SPREAD | 0.15 | - | spread probability per unit neighbour fuel | ECO: gives a fire return interval of ~2 years with no animals (savanna: 1-5 years) |
| C_ASH | 0.5 | - | nutrient from burned biomass | HT |
| FIRE_STEPS | 6 | steps | steps a cell burns | HT |
| FUEL_MIN | 0.25 | - | fuel below which fire cannot spread | HT |
| FIRE_KILL | 0.3 | - | probability per step that an animal in a burning cell dies | HT |
| C_SCENT | 0.02 | /step | scent deposit per unit carnivory | HT |
| D_SCENT | 0.01 | /step | scent decay | HT |
| MARK_GAIN | 4 | - | MARK multiplier on scent deposit | HT |

## 29. Climate

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| CLIMATE | 0.3 | - | default climate strength (Nature.Climate): T = T_REF + Climate x (T_phys - T_REF); 0 = no climate, everywhere T_REF and no heat budget | HT: Revision O; 1 = Earth-like contrasts. Sanity runs at 8,000 steps: 0.6 left 0 and 243 organisms in two branches, 0.3 left 670 and 944, 0 left 728 and 4,106 |
| RAIN_W | 0.7 | - | share of the soil's wetness that follows where rain falls (the rest follows the ground: near water, in valleys) | HT: Revision P; before, soil wetness came from the ground and noise only, and 80% of the land was in the driest two fifths |
| RAIN_L | 500 | km | distance over land over which wind-borne air loses all but 1/e of its water to the background (synoptic) rain, without lifting | ECO: precipitation falls off inland over hundreds to a few thousand km (continental interiors are dry; the e-folding of moisture transport inland ~500-2,000 km, Eltahir & Bras 1996; van der Ent et al. 2010). 2026-11-01: in km, so the rain of a 1 km map does not fall off across it; it was 30 cells, 6.5 km when a cell was 218 m |
| RAIN_OROG | 0.15 | /km | share of the air's water that condenses per km the ground lifts it (orographic condensation; it falls as rain over RAIN_FALL downwind) | ECO: saturated air lifted 1 km condenses ~1/3 of its vapour (the water vapour scale height ~2.5 km) and a share of it rains out (precipitation efficiency 0.3-0.5; Smith & Barstad 2004; Jiang & Smith 2003): 0.15. 2026-11-01: it was 40/3 per km falling where it condensed, which made every windward slope of a small hill many times wetter than its lee |
| RAIN_FALL | 10 | km | distance downwind over which orographic condensate falls out (1/e) | ECO: condensation and fallout take tau ~1,000-2,000 s, carried ~10-20 km by a 5-10 m/s wind (Smith & Barstad 2004), so hills narrower than this get little extra rain of their own: on a 1 km map the rain is nearly even, with a windward lean and the height gradient RAIN_ZGRAD (2026-11-01) |
| RAIN_ZGRAD | 0.5 | /km | rain gained per km of height above the lakes (x the rain at their level) | ECO: precipitation-elevation gradients of 3-10% per 100 m in mid-latitude uplands (Daly, Neilson & Phillips 1994; Basist, Bell & Meentemeyer 1994), from seeder-feeder enhancement that falls where it forms (Bergeron 1965; Hill, Browning & Bader 1981): 5% per 100 m (2026-11-01) |
| RAIN_EVAP | 0.92 | /km | how fast air over open water takes up water, per km (toward saturation) | HT: 0.2 per cell of 218 m before 2026-11-01 (0.92 per km), now in km |
| RAIN_RECYCLE | 0.046 | /km | water the land gives back to the air per km (evapotranspiration), toward saturation | ECO: about 40% of land rain is recycled from the land (Trenberth 1999); HT: 0.01 per cell of 218 m before 2026-11-01 (0.046 per km), now in km |
| RAIN_DRY | 0.3 | - | rain at the dry belts (near 30° and the poles), against the equator's 1 | ECO: zonal mean rain ~0.3-0.4 of the equator's in the subtropical highs and at the poles (Peixoto & Oort 1992) |
| RAIN_MID | 0.35 | - | extra rain of the mid-latitude belt near 55° | ECO: a second maximum ~0.5-0.6 of the equator's (Peixoto & Oort 1992) |
| RAIN_BELT | 14 | ° | width (degrees of latitude) of the equatorial and mid-latitude rain belts | ECO |
| T_REF | 20 | °C | reference temperature (the temperature of a world with no climate; plant curves, decomposition and evaporation are 1 here) | - |
| T_EQ | 27 | °C | annual mean sea-level temperature at the equator | ECO: observed zonal mean ~27 °C (North 1975) |
| DT_EP | 40 | K | equator-to-pole temperature drop, x sin² latitude | ECO: the second Legendre term of North's (1975) energy-balance climate, ~40 K |
| A_SEAS | 20 | K | seasonal amplitude at the pole in the middle of a continent, x sin latitude x continentality | ECO: continental interiors swing ~40 K winter to summer at high latitude (Conrad 1946) |
| LAT_LO | 10 | ° | lowest latitude a world is drawn at (World.LatDeg; either hemisphere) | HT (2026-11-01, the 1 km world): a map a few km across sits at one latitude; tropics to the cool temperate belt, where the land stays green |
| LAT_HI | 55 | ° | highest latitude a world is drawn at (World.LatDeg) | HT (2026-11-01): at 55° the Earth-like snowline stands ~340 m above the lakes (GEO_T_ELA), so the highest hills of the coolest worlds carry small glaciers in deep time; beyond ~60° it reaches the lakes |
| LAPSE | 6.5 | K/km | lapse rate: cooling with height | ECO: ICAO standard atmosphere, 6.5 K per km |
| H_RELIEF | 8000 | m | the relief of an orogen at the plates' scale, which a map reaches only over RELIEF_L_KM: a map's vertical scale is Map.ReliefM = H_RELIEF x min(1, (map width / RELIEF_L_KM)^RELIEF_HURST), so Elev (1 - SeaLevel) above the lakes is ReliefM metres and the highest ground stands at most that high (Climate, Hydrology, the caves, Explore, the 3D view) | GEO/HT: active orogens stand 4-9 km (the Alps 4.8, the Andes 7, the Himalaya 8.8). 2026-11-01 (the 1 km world): no longer every map's vertical scale; a 1 km map is a piece of a landscape, a valley and its hills (153 m on the 256 map) |
| RELIEF_L_KM | 200 | km | the window over which a landscape's relief reaches H_RELIEF (Map.ReliefM) | GEO: an orogen rises to its crest over ~150-250 km from its foreland (the Himalaya from the Ganges plain, the Andes from the Chaco); the half-width of a collision belt, GEO_W_OROGEN, is 300 km |
| RELIEF_HURST | 0.75 | - | exponent of relief against the width of the window it is measured over (Map.ReliefM) | GEO: topography is self-affine with a Hurst exponent ~0.7-0.8 (0.75: 153 m on the 1 km map, 2026-11-01) at these scales (Turcotte 1997; Mark & Aronson 1984); local relief grows with window size up to ~10-30 km (Ahnert 1984; Montgomery & Brandon 2002) |
| WATER_DAMP | 0.75 | - | share of the seasonal swing that open water takes away at the shore (continentality 1 - WATER_DAMP x exp(-distance / L_MAR)) | ECO: maritime climates swing a quarter of continental ones (Conrad 1946 continentality index) |
| L_MAR | 81.75 | cells | distance over which the water's damping of the seasons fades inland (cells: L_MAR_M / GEO_CELL_M) | derived (2026-11-01): 82 cells of 16 m, beyond a 1 km map; it was 6 cells of 218 m |
| L_MAR_M | 1308 | m | distance over which the water's damping of the seasons fades inland | HT: 1.3 km (6 cells of 218 m before 2026-11-01) |
| T_WATER_MIN | 0.5 | °C | water under ice stays at least this warm | ECO: lakes freeze from the top; the water below stays at 0-4 °C |
| EVAP_BETA | 0.5 | - | Budyko-style exponent of how evaporation dries soil: moisture x (e_s(T) / e_s(T_REF))^-EVAP_BETA (Tetens) | ECO: evaporative demand rises with saturation vapour pressure (Budyko 1974; Tetens 1930) |
| GRASS_TMIN | 0 | °C | grass: lowest, best and highest temperature of growth (Yan & Hunt beta curve, x 1 at T_REF, at most PLANT_CAP) | ECO: Yan & Hunt (1999); C4/C3 grasses grow from ~0 to ~45 °C |
| GRASS_TOPT | 25 | °C | grass: best temperature of growth | ECO: Yan & Hunt (1999) |
| GRASS_TMAX | 45 | °C | grass: highest temperature of growth | ECO: Yan & Hunt (1999) |
| SHRUB_TMIN | 2 | °C | shrubs: lowest temperature of growth | ECO: dwarf shrubs grow above the treeline (Körner 2003) |
| SHRUB_TOPT | 24 | °C | shrubs: best temperature of growth | ECO |
| SHRUB_TMAX | 45 | °C | shrubs: highest temperature of growth | ECO |
| TREE_TMIN | 5 | °C | trees: lowest temperature of growth | ECO: tree growth stops below ~5 °C, which sets the treeline (Körner & Paulsen 2004) |
| TREE_TOPT | 22 | °C | trees: best temperature of growth | ECO |
| TREE_TMAX | 40 | °C | trees: highest temperature of growth | ECO |
| PLANT_CAP | 1.1 | - | largest plant growth multiplier from temperature | HT: the beta curves peak a few % above their value at T_REF |
| EPPLEY | 1.066 | /K | algae and plankton growth multiplier per K (no growth under ice) | ECO: Eppley (1972): phytoplankton growth x1.066 per K |
| Q10_ROT | 2 | - | decomposition multiplier per 10 K | ECO: soil respiration Q10 ~2 (Lloyd & Taylor 1994) |
| TT_LO | -60 | °C | lowest temperature of the response tables (TT_N entries of TT_STEP) | - |
| TT_STEP | 0.25 | K | temperature step of the response tables | - |
| TT_N | 512 | - | entries of each response table | - |

## 30. Heat

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| E_ACT | 0.65 | eV | activation energy of metabolism: basal cost and digestion of a body whose core is colder than T_BODY slow as exp(E/k (1/T_BODY - 1/T)) | ECO: 0.6-0.7 eV across animals, plants and microbes (Gillooly et al. 2001; Brown et al. 2004): x0.23 at 20 °C, x0.48 at 28 °C |
| T_CORE_MIN | 1 | °C | coldest core temperature the metabolic rate follows (below it a cold-blooded body barely runs) | HT: near freezing; most ectotherms are torpid below ~2 °C |
| T_BODY | 38 | °C | body temperature of every animal (an endotherm; no heat is stored from step to step) | ECO: mammals 36-39 °C, birds ~40 °C; a step is hours, longer than the thermal time constant of bodies up to tens of kg (Bakken 1976) |
| K_COND | 0.0625 | xC_BASAL/K | dry conductance of a bare body of M=1, per unit core area M^(2/3), in units of C_BASAL per K | HT: a bare M=1 body is thermoneutral down to 38 - 16 = 22 °C (a naked mammal's lower critical temperature, Scholander et al. 1950) |
| R_INS | 4 | - | insulation: fur of thickness i divides the core's conductance by 1 + R_INS x i | ECO: arctic fur cuts conductance ~5x against tropical mammals (Scholander et al. 1950) |
| VASO | 3 | x | vasodilation: largest rise of the core's conductance when warm | ECO: peripheral blood flow raises conductance ~3x (Bakken 1976) |
| APP_LEAK | 0.15 | - | share of an appendage's conductance that leaks when it is cold (vasoconstricted) | ECO: countercurrent exchange in limbs and ears keeps most heat in (Scholander & Krog 1957) |
| K_WATER | 4 | x | conductance in water against air | ECO: water conducts heat ~25x air; convective loss of an immersed body ~4x (Nadel 1977) |
| FUR_WET | 0.5 | - | share of its insulation fur keeps when wet | ECO: wet fur loses about half its insulation (Scholander et al. 1950) |
| SOLAR | 6 | K | sun on open ground: rise of the operative temperature (x climate strength, x (1 - SHADE_TREE x tree - SHADE_SHRUB x shrub) / (1 + 0.5 R_INS x fur)) | ECO: a day-averaged solar load raises operative temperature several K over air temperature (Bakken 1976; Campbell & Norman 1998); size-independent, as absorbed and lost heat both scale with area |
| SHADE_TREE | 0.8 | - | share of the sun that tree cover blocks | ECO: canopy shade |
| SHADE_SHRUB | 0.4 | - | share of the sun that shrub cover blocks | ECO |
| SUMMIT | 5 | xbasal | most heat an animal can make by shivering and burning fat, x its basal | ECO: summit metabolism ~5x basal (Hinds et al. 1993) |
| EVAP_MAX | 3 | xbasal | most heat an animal can lose by evaporation (sweating, panting), x its basal | ECO: evaporative heat loss can reach a few times basal in heat (Schmidt-Nielsen 1997) |
| EVAP_REL | 2 | xC_THIRST | thirst per unit of evaporated heat (in basal units per unit body mass), against C_THIRST | HT: sweating a basal's worth doubles the drying rate (calibrated against C_THIRST, not latent heat) |
| STRAIN_GAIN | 0.2 | /basal | heat strain gained per unit of heat shortfall beyond the caps (in basal units) | HT: a full basal's shortfall kills in ~7 steps |
| STRAIN_REC | 0.05 | /step | heat strain recovered per step | HT: a shortfall below a quarter of basal is survived |
| COST_INS | 0.06 | xbasal | basal cost of full fur (insulation gene 1) | ECO: growing and moulting fur and feathers costs a few % of energy (Lindström et al. 1993) |
| INS_MOVE | 0.1 | xmove | extra cost of moving with full fur | ECO: a heavy coat adds to the cost of transport |
| COST_APP | 0.03 | xbasal | basal cost of full appendages (ears, limbs, tail as radiators; appendage gene 1) | ECO |
| FOUND_INS | 0.15 | 0..1 | founders' fur (insulation gene) | HT: with FOUND_APP and M=1 thermoneutral ~15-32 °C around T_REF |
| FOUND_APP | 0.3 | 0..1 | founders' appendages (appendage gene) | HT |
| AIR_T_SCALE | 30 | K | AIR_T sense: (operative temperature - T_BODY) / AIR_T_SCALE, clamped to -1..1 | - |

## 31. Memory

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| SENSE_WATER | 109 | cells | farthest an animal senses drinkable water or land: SEEK_WATER and SEEK_LAND point the way only within it, and WATER_CLOSE and LAND_CLOSE fade to 0 at it (cells: SENSE_WATER_M / GEO_CELL_M) | derived (2026-11-01): 109 cells of 16 m (it was 8 cells of 218 m, the same 1.74 km) |
| SENSE_WATER_M | 1744 | m | farthest an animal senses drinkable water or land, in metres (SENSE_WATER) | ECO (2026-11-01, in metres): 1.74 km, the horizon of an eye 0.24 m above flat ground (3.57 km x sqrt(height in m)), as for a body of a few to 40 kg; a lake or pond spans hundreds of metres and is seen to the horizon, and humidity and odour carry as far downwind. Before, it was set equal to MAX_SIGHT (32 units, now 6). Cues of distant water (sight, humidity, odour) carry a few body-lengths to a few km, not across a landscape (Dusenbery 1992). Before 2026-10-30 the way to water was a map-wide distance field |
| PI_ERR | 0.2 | - | path-integration error: each step's estimate of its own displacement is off by a random vector of SD PI_ERR x the distance moved (each axis) | ECO: (at BRAIN_START units; see PI_DIFF) odometer and compass errors of path integration add up with the way travelled, so a remembered place drifts as the square root of the distance (Mueller & Wehner 1988; Cheung et al. 2007); HT: a step is hours of travel, so one step is one leg of ~10-20% error |
| PI_DIFF | 13.625 | units | path-integration drift at rest: a held place diffuses by this SD a step (each axis) in a brain of BRAIN_START units; PI_ERR and PI_DIFF both scale as sqrt(BRAIN_START / units) | ML/ECO: a neural integrator (continuous attractor) diffuses at a rate ~ 1/N neurons (Burak & Fiete 2009); HT: 54.5 m a step (PI_DIFF_M / M_PER_UNIT, 2026-11-01: 13.6 units of 4 m); a 24-unit brain loses a place to about SENSE_WATER (1.7 km) in ~1000 still steps, a 256-unit one in ~10000 |
| PI_DIFF_M | 54.5 | m | path-integration drift at rest, in metres a step (PI_DIFF) | HT: 1 unit when a unit was 54.5 m (2026-11-01) |
| MEM_NEAR | 436 | units | distance of a remembered place at which its NEAR sense reads 0.5 (NEAR = 1 / (1 + d / MEM_NEAR); 0 when nothing is remembered) | -: 1.74 km, SENSE_WATER_M in units (436 units of 4 m; 32 units when a unit was 54.5 m; was MAX_SIGHT = 32 units until 2026-11-01, when sight became metres): NEAR reads 0.5 where the cues of water fade |
| DAMP_CUE | 0.02 | - | smallest difference in soil moisture across a cell (between its two neighbours) that a thirsty animal can feel and follow toward water; below it it walks downhill | HT: about the patchiness of moisture on uniform ground; animals follow damp ground and smell water at a distance (Polansky et al. 2015), and water gathers downhill |
| FED_WELL | 1 | steps | a meal is remembered as 'fed well' when one bite's energy is at least this many steps' need (FIELD_METAB x basal) | HT: a bite that pays for its step, FOOD_HERE 0.63; a patch richer than the cost of being there is worth coming back to (Charnov 1976) |

## 32. Hydrology

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| RIVER_ACC | 0.01 | km2 | catchment (km2 of mean land rain: Q x Map.CellKm2) above which a land cell is a river channel at any slope (below it the channel-initiation threshold CHANNEL_C decides) | HT: the only river rule before 2026-10-31 (rivers were the 0.5-0.7% of cells over it, 11-14 of them on a 1024 map); now the flat-ground limit of channel initiation. 2026-11-01: in km2 (it was 900 cells x map cells / 65,536, a share of the map, not an area); ECO: channels on low gradients start at ~0.1-1 km2 (Montgomery & Dietrich 1988); HT: 0.01 km2 (39 cells of 16 m): the 1 km map's land drains in catchments of at most ~0.1 km2 to its lake (the torus has no inflow), so its streams start near the small end of the range; on steep ground CHANNEL_C starts them sooner |
| CHANNEL_C | 50 | m | channel initiation: a channel head is a cell whose inflow Q (cells of mean land rain, Net.HeadIn) and slope S (m/m: the map's vertical scale Map.ReliefM over GEO_CELL_M a cell) give the specific catchment area times the slope squared, (Q x GEO_CELL_M) S^2 (m: Q cells of GEO_CELL_M^2 over a contour of GEO_CELL_M), at least CHANNEL_C; every cell below a head is channel down to its lake or sea | ECO: channel heads lie on a drainage area-slope threshold, (A / b) S^2 = const (Montgomery & Dietrich 1988, 1992: 25-200 m for the heads of ephemeral channels; the perennial, mapped streams start further down). HT (2026-11-01, the 1 km world): 50 m, the low end, so the small catchments of a 1 km map (at most ~0.1 km2 each, draining to its lake) start first-order streams: 0.1-1.8% of the 256 map (seeds 3, 5, 7). It was 1.2 cells of Q S^2 at 218 m a cell (262 m), and 300 m for a day at 55 m |
| CHANNEL_A_MIN | 0.0028 | km2 | least mean discharge (km2 of mean land rain: Q x Map.CellKm2) of a channel head however steep the ground | ECO: below a least source area, hollows are unchanneled colluvial slopes (Montgomery & Dietrich 1992; Dietrich & Dunne 1993): source areas 10^2-10^5 m2, ~10^3 on steep ground. 2026-11-01: in km2 (it was 15 cells x map cells / 65,536): 11 cells of 16 m |
| RIVER_Q1 | 0.0284 | km2 | reference mean discharge (km2 of mean land rain: Q x Map.CellKm2) of a major river (the water cycle's census of river mouths: 4 x this) | HT: 2026-11-01: in km2 (it was 150 cells x map cells / 65,536, and set the width in cells and the depth), 111 cells of 16 m; width and depth are now in metres (Hydrology.WidthM, DepthM) |
| RIVER_W_MAX | 5 | cells | widest a river is drawn on the field grid | HT: a cap; at 16 m a cell (2026-11-01) a river 5 cells wide carries ~400 m3/s, far beyond a 3.5-28 km map's |
| RIVER_BANK | 11.04 | m | the banks of a river more than one cell wide are cells within its half-width of the channel and at most this high above the channel's bed | HT: the river spreads over its valley floor, not up the valley's sides. 2026-11-01: 0.69 x GEO_CELL_M (38 m): the rise of a neighbouring cell on the same slopes goes with the cell (it was 150 m at 218 m a cell; before that 1 x SHALLOW in elevation) |
| BUDYKO_W | 2.6 | - | Budyko curve shape (Fu's equation): a catchment's mean evapotranspiration ET / P = 1 + PET/P - (1 + (PET/P)^w)^(1/w); what is left runs off and fills the lakes | ECO: Fu 1981; Zhang et al. 2004 (w ~2-3.5 across catchments; 2.6 the mean) |
| SEA_MIN | 0 | fraction | a connected region below sea level is part of the sea if it covers at least this share of the map (the largest always is); a smaller one is a closed basin and holds a lake only as its water balance allows | HT: 0 while Geology grades the land to every region below sea level as base level (it holds them all at sea level for 60 Myr and never cuts their rims): as closed basins they would fill to their spill points, 140-400 m higher, and lakes would cover 13-26% of the map (seeds 1, 2, 3, 7, 11; 1 = the largest region only). Raise it once deep time drains to the connected sea alone |
| LAKE_MIN_CELLS | 4 | cells | a closed depression holds a lake only if it is at least this many cells at its spill level; a smaller one is a pond site | HT: smaller hollows are seasonal ponds (Ponds.cs) |
| LAKE_MIN_DEPTH | 0.96 | m | a closed depression holds a lake only if its spill level is at least this far above its floor; a shallower one is a pond site | HT: 2026-11-01: 0.06 x GEO_CELL_M (3.3 m): the depth of the hollows the grid resolves goes with its cell at the same slopes (it was 13 m at 218 m a cell, 0.003 in elevation before that); ECO: small lakes and ponds are 1-5 m deep |
| SPREAD_A | 15 | cells | routing: flow spreads over the hillslope between the two neighbours of its D-infinity direction until it has gathered this much rain (cells of rain on the routing grid); beyond it, it is a stream and goes whole to one of them | ECO: divergent flow on hillslopes, convergent in channels (Quinn et al. 1991; Tarboton 1997); about CHANNEL_A_MIN, where hollows become channels (Montgomery & Dietrich 1992) |
| CHANNEL_SMOOTH | 1.5 | cells | a river's valley axis is its cell path smoothed by a Gaussian of this many cells (or 3/4 of its width, if more), and kept within a cell of the path | -: numerical; removes the grid's zigzags, not the valley's bends |
| MEANDER_L | 11 | widths | meander wavelength, x the river's width | ECO: lambda = 10.9 w^1.01 (Leopold & Wolman 1960); 7-15 widths in most rivers |
| MEANDER_ST | 0.0125 | - | the valley slope above which a river does not meander: MEANDER_ST x Q^-0.44 (Q bankfull, m3/s) | ECO: the meandering-braided threshold S = 0.06 Q^-0.44 in cfs (Leopold & Wolman 1957), 0.0125 Q^-0.44 in m3/s |
| MEANDER_P | 2 | - | sinuosity of a river on a flat valley floor; it falls linearly with S toward 1 at the threshold slope S_t | ECO: freely meandering lowland rivers 1.5-3 (Leopold, Wolman & Miller 1964; Schumm 1963) |
| MEANDER_HAND | 2.208 | m | a river meanders only over its valley floor: the ground at most this high above its bed | HT: floodplains stand metres above their rivers. 2026-11-01: 0.138 x GEO_CELL_M (7.6 m; it was 30 m at 218 m a cell, the floors of the relief's lowland valleys) |
| FLOW_EPS | 1E-06 | elev | least rise per cell across a filled basin or flat (Priority-Flood+epsilon), so every cell drains | -: numerical (Barnes, Lehman & Mulla 2014); far below any real slope |
| ERODE_ITERS | 20 | passes | passes of routing, stream-power incision and deposition at world generation | HT: enough to cut the main valleys; about 15 ms a pass at the default map (1024), 80 ms at 2048 |
| ERODE_K | 0.007 | /pass | stream-power erodibility per pass: dh = -ERODE_K A^ERODE_M S (A in cells of mean rain) | HT: measured at the default map, channels on slopes are cut ~20 m on average and up to ~300 m, hillsides ~6 m (mostly creep) |
| ERODE_M | 0.5 | - | drainage-area exponent of stream power and of carrying capacity (slope exponent 1) | ECO: m/n ~0.5 (Howard 1994; Whipple & Tucker 1999); observed river concavity 0.4-0.6 (Whipple 2004) |
| CREEP_K | 0.01 | /pass | hillslope creep per pass: dh = CREEP_K x (sum of the 4 neighbours' heights - 4 h) | ECO/HT: linear soil creep (Culling 1960; Roering et al. 1999), the hillslope half of every landscape-evolution model; rounds the finest noise over the passes |
| CARRY_K | 0.3 | /pass | carrying capacity: a stream carries at most CARRY_K A^ERODE_M S of sediment per pass | ECO/HT: transport capacity grows with stream power (Willgoose et al. 1991); well above ERODE_K, so steep channels carry what they cut (detachment-limited uplands) and flats drop it; at 0.03 the whole network was transport-limited and channels filled up instead of cutting |
| DEPOSIT_FRAC | 0.3 | - | share of a load above capacity dropped in each cell it passes | HT: a deposition length of a few cells (Davy & Lague 2009) |
| ALLUV_FERT | 0.5 | - | most fertility alluvium adds to a cell's soil (Fert + ALLUV_FERT x (1 - exp(-alluvium / ALLUV_D)), at most 1) | ECO: alluvial soils (fluvisols) are young, fine, mineral-rich and among the most fertile (FAO WRB 2015; the Nile, Indus and Mekong floodplains) |
| ALLUV_D | 0.96 | m | alluvium depth at which its fertility (and the soil's extra capacity, ALLUV_CAP) is 1 - 1/e of its most | HT: 2026-11-01: 0.06 x GEO_CELL_M (1 m): deposits go with the relief, which goes with the cell (13 m at 218 m a cell; 0.003 in elevation before that); ECO: alluvial soils are a metre or more deep |
| BASIN_MIN | 0.288 | m | a land cell at least this far below the rim of the closed basin it lies in is a pond site (a seasonal wetland) | ECO/HT: rain gathers and stands in closed basins (pans, playas, prairie potholes), seasonally where evaporation outpaces it. 2026-11-01: 0.018 x GEO_CELL_M (1 m; it was 4 m at 218 m a cell, 0.001 in elevation before that) |
| RIVER_D | 0.3 | m | a river's mean depth at a mean discharge of 1 m3/s: depth = RIVER_D x Q^RIVER_DEPTH_EXP (Q in m3/s: Flow x P_MEAN over a GEO_CELL_M cell), from RIVER_D_MIN to RIVER_D_MAX | ECO: downstream hydraulic geometry d = c Q^0.4 with c ~0.3 m (Leopold & Maddock 1953; Dingman 2009): 0.3 m at 1 m3/s, 1.2 m at 30, 4.8 m at 1,000, 16 m at the Mississippi's 16,000. Before 2026-11-01 it was 0.5 x SHALLOW (elevation) at RIVER_Q1, at most SHALLOW: rivers came out 90-320 m deep |
| RIVER_W | 5 | m | a river's real width at a mean discharge of 1 m3/s: width = RIVER_W x Q^0.5 (Q in m3/s; Hydrology.WidthM), whatever the cell size; World.RivW is the drawn width in cells | ECO: downstream hydraulic geometry w = a Q^0.5 with a ~4-8 m (Leopold & Maddock 1953; Dingman 2009): 5 m at 1 m3/s, 27 m at 30, 160 m at 1,000 |
| RIVER_D_MIN | 0.3 | m | shallowest a channel cell's water is | HT: the smallest channel a 218-m cell holds is a brook a few metres wide, ~0.2-0.5 m deep at mean flow; below it the cell would not hold running water at all |
| RIVER_D_MAX | 10 | m | deepest a river's mean flow is | ECO: the mean depth of the largest lowland rivers, 5-20 m (the Amazon's ~20 m at 200,000 m3/s); a cap the default maps never reach |
| SHORE_A | 0.08 | m^(1/3) | shoreface: standing water (the sea and the lakes) is at most SHORE_A x^(2/3) deep at x m from its shore, down to SHORE_HC; beyond, SHORE_HC + SHORE_S (x - x_c); deeper ground is filled to it (Waters: lacustrine and marine sediment) | ECO: Dean's (1977, 1991) equilibrium beach profile h = A x^(2/3), A 0.06-0.1 for fine sand (0.1-0.2 mm); lakes' littoral shelves follow the same wave-built profile. The first ring of cells (109 m out) is 1.8 m deep, the second 3.8 m |
| SHORE_HC | 4 | m | closure depth of the shoreface (SHORE_A), 354 m out: below it the bed falls at SHORE_S | ECO: the littoral, where light reaches the bed and rooted plants grow, ends at ~2-6 m in most lakes (Wetzel 2001); low-energy coasts close at 4-8 m (Hallermeier 1981) |
| SHORE_S | 0.03 | m/m | slope of the bed below the shoreface's closure depth (the sublittoral and the shelf), out to the basin's own floor | ECO/HT: the sublittoral slopes of small lakes are 2-10% (Hakanson 1981), so a lake 2 km across is ~20-30 m deep in the middle (relative depth 1-2%, typical: Hutchinson 1957); 3% takes the default map's sea to 50-150 m a few km out |
| RIVER_DEPTH_EXP | 0.4 | - | exponent of river depth (and stage) on discharge | ECO: at-a-station and downstream hydraulic geometry, depth ~ Q^0.4 (Leopold & Maddock 1953) |
| BANKFULL | 1.35 | x | discharge, against its mean, at which a river tops its banks (wetness = season x this year's rain) | ECO: bankfull flow recurs every 1-2 years (Leopold, Wolman & Miller 1964; Williams 1978): the wet season of an average year (1 + A_SEASON) just tops it |
| FLOOD_R | 3 | cells | floodplain: cells at most this many flow steps upstream of the river they drain into | HT: floodplains are a few channel widths to tens of km wide |
| FLOOD_HAND | 5.52 | m | floodplain: cells at most this high above that river | HT: bounds the search; the stage decides which of them flood. 2026-11-01: 0.345 x GEO_CELL_M (19 m; it was 75 m at 218 m a cell, 0.5 x SHALLOW in elevation before that) |

## 33. Geology

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| GEO_PLATES_LO | 5 | plates | fewest tectonic plates on the map (the count is drawn uniformly from GEO_PLATES_LO to GEO_PLATES_HI) | GEO: Earth has 7 major and ~8 secondary plates (Bird 2003); the map is a continent's worth of them |
| GEO_PLATES_HI | 9 | plates | most tectonic plates on the map | GEO: as GEO_PLATES_LO |
| GEO_KM | 5000 | km | side of the map as the plates see it | GEO/HT: a few plates of 1,500-3,000 km (Bird 2003) on a coarse grid that resolves a rift (GEO_W_RIFT) in 2-3 cells; not the climate's pole-to-pole rows nor the animals' walking scale (the world's scales are not one) |
| GEO_SIZE_VAR | 0.5 | - | spread of plate sizes: each plate's Voronoi weight is up to this share of the mean plate spacing | GEO: plate areas span orders of magnitude (Bird 2003; Morra et al. 2013) |
| GEO_WARP | 0.3 | - | bending of plate boundaries: the Voronoi is drawn on positions warped by up to this share of the map (low-frequency noise) | HT: real boundaries curve and kink; a warp of the plates' outline, not relief |
| GEO_V_LO | 10 | km/Myr | slowest plate | GEO: plate speeds 1-8 cm/yr (DeMets, Gordon & Argus 2010, MORVEL); 1 cm/yr = 10 km/Myr |
| GEO_V_HI | 80 | km/Myr | fastest plate | GEO: as GEO_V_LO |
| GEO_SPIN | 0.005 | rad/Myr | largest spin of a plate about its centre (drawn in ±this) | GEO: Euler-pole rotation rates 0.1-1 °/Myr (DeMets et al. 2010); 0.3 °/Myr |
| GEO_T_LO | 30 | km | thinnest plate crust | GEO: continental crust is 30-45 km thick, mean ~40 (Christensen & Mooney 1995) |
| GEO_T_HI | 45 | km | thickest plate crust | GEO: as GEO_T_LO |
| GEO_T_CRATON | 40 | km | plates at least this thick are old shields: crystalline basement at the surface; thinner, younger plates carry sedimentary cover | GEO: shields expose Precambrian basement; platforms are covered by younger strata (Sloss 1963; Durrheim & Mooney 1991) |
| GEO_COVER | 1.5 | km | sedimentary cover on a young platform at the start | GEO: platform cover is 1-3 km thick (the Russian and North American platforms; Sloss 1963) |
| GEO_T_MIN | 20 | km | thinnest crust a rift or a foredeep can stretch to; it stops sinking there | GEO: continental rifts thin the crust by a stretching factor of ~1.5-2 before breakup (McKenzie 1978) |
| GEO_SUBDUCT | 6 | km | at a convergent boundary, plates whose crust differs by more than this subduct (the thinner sinks, the thicker carries an arc); closer ones collide | HT: thinner, denser lithosphere sinks under buoyant thick crust (ocean-continent: the Andes); equal buoyant crusts collide (the Himalaya) |
| GEO_AIRY | 0.15 | - | Airy isostasy: rock uplift per unit of crustal thickening, (rho_m - rho_c) / rho_m | GEO: crust 2,800 kg/m3 floating on mantle 3,300 (Airy 1855; Turcotte & Schubert 2002) |
| GEO_W_OROGEN | 300 | km | half-width of a collision belt (thickening x exp(-(d/W)^2) on each side) | GEO: collision orogens are 200-1,000 km wide (the Alps ~200, the Himalaya-Tibet ~1,000) |
| GEO_W_ARC | 100 | km | half-width of a volcanic arc, and of the trench moat in front of it | GEO: arc volcanic fronts are ~50-150 km wide (Gill 1981) |
| GEO_ARC_D | 200 | km | distance of the arc behind the plate boundary (the arc-trench gap) | GEO: arc-trench gaps are 100-300 km (Dickinson 1973) |
| GEO_ARC_SHARE | 0.3 | - | share of the convergence an arc takes up as thickening (shortening and magma), against a collision belt's all | GEO/HT: at ocean-continent margins most convergence is subducted; arcs thicken by magmatic addition and some shortening (Isacks 1988; Reymer & Schubert 1984) |
| GEO_FOREDEEP | 0.3 | - | depth of the trench moat on the sinking plate, as a share of the arc's thickening | GEO: a flexural moat in front of a load is about a third of its height (Turcotte & Schubert 2002, §3.17) |
| GEO_W_RIFT | 100 | km | half-width of a rift (thinning x exp(-(d/W)^2) on each side) | GEO: rift basins are 40-100 km wide, rift zones up to ~200 (the East African and Baikal rifts; Ebinger 1989) |
| GEO_OBLIQUE | 0.34 | - | a boundary is transform where the plates' closing or opening is under this share of their relative motion (within ~20° of it) | HT: strike-slip boundaries carry little convergence (the San Andreas: ~10%, transpression) |
| GEO_CELL_M | 16 | m | a field cell's side: THE length scale of the world. Slopes, drainage areas and hillslopes are measured on it; a unit is GEO_CELL_M / FCELL (M_PER_UNIT), plants disperse over it (CELL_M), and the hydrology's areas (km2) and the climate's distances (km) are counted in it | HT (2026-11-01, the owner: a default world ~1 km across, one valley with a stream, hills and a pond, several hundred animals at true size): 16 m, so the 256 map is 1.02 km, 512 2.05 km, 1024 4.1 km and 2048 8.2 km; the grids keep their sizes. Was 218 m (the 256 map 14 km), and 55 m for a day (3.5 km). The 3D view draws a field cell over FCELL = 4 units and the relief at the same true scale (RELIEF = Map.ReliefM x 4 / GEO_CELL_M units, from meta); the plates' scale (GEO_KM) is not this one |
| GEO_SC | 0.6 | m/m | critical hillslope gradient of soft rock (sedimentary strata, regolith, talus, alluvium): hillslope transport rises without bound as a slope nears it, and steeper ground fails | GEO: soil-mantled and weak-rock hillslopes stand at 30-35° whatever the uplift rate (Burbank et al. 1996; Montgomery & Brandon 2002); Sc ~0.6-1.25 in nonlinear diffusion (Roering, Kirchner & Dietrich 1999); tan 31° |
| GEO_SC_CRYST | 1.4 | m/m | critical slope of crystalline bedrock (granite, gneiss): crags and walls stand this steep before they fail | GEO: the strength of the rock mass, not of soil, limits relief where rock is strong: hillslopes in intact crystalline rock stand at 40-60° and more (Schmidt & Montgomery 1995; Selby 1980 rock-mass strength); tan 54.5° |
| GEO_SC_VOLC | 1 | m/m | critical slope of volcanic rock (lavas, tuffs, breccias) | GEO: jointed lavas and welded tuffs stand in cliffs; weak tuffs and breccias do not: between basement and soft strata (Schmidt & Montgomery 1995; Selby 1980); tan 45° |
| GEO_SC_LIME | 1.2 | m/m | critical slope of limestone (massive carbonate strata) | GEO: massive limestone stands in cliffs and gorge walls (the Dolomites, the Verdon; Selby 1980 rates it strong); tan 50° |
| GEO_REPOSE | 0.7 | m/m | angle of repose of talus (scree): the debris of rockfall comes to rest at the foot of a crag at slopes up to this | GEO: talus slopes stand at 32-38° (Statham 1976; Selby 1993); tan 35° |
| GEO_TALUS | 0.5 | - | share of the rock that falls from a crag (critical-slope failure) that stays at its foot as talus; the rest is carried off with the rivers' load | GEO/HT: talus cones and aprons store most rockfall debris for 10^4-10^5 years (Hinchliffe & Ballantyne 1999); a step of deep time is 0.3 Myr, so half |
| GEO_T_ELA | -2 | °C | deep-time snowline (ELA): glaciers form above the height where the Earth-like annual mean temperature, T_EQ - DT_EP sin² latitude - LAPSE x height, falls to this (whatever the climate setting, so a seed makes the same land) | GEO: the annual mean air temperature at glacier ELAs is about -2 to -6 °C (Ohmura, Kasser & Funk 1992), and the Quaternary's mean ELA lay some 500 m below today's (Porter 1989), the height the glacial buzzsaw cuts to (Brozović, Burbank & Meigs 1997) |
| GEO_T_COLD | -10 | °C | a glacier is frozen to its bed (it does not slide or erode) where its bed is at or below this temperature; it slides fully at the melting point (0 °C), and between in proportion. The bed is the air's annual mean (Earth-like, as GEO_T_ELA) plus GEO_ICE_GEO x the ice's thickness | GEO: mountain glaciers are temperate well above their ELA, warmed by meltwater refreezing in the firn as well as by the geothermal flux; they are cold where the air's mean is below about -10 to -15 °C (Cuffey & Paterson 2010; Hooke 2005); cold-based ice protects its bed: thin ice on summits and plateaus is barely eroded while thick trunk glaciers cut troughs (selective linear erosion: Sugden 1978; Kleman & Hättestrand 1999), so peaks stand above the buzzsaw |
| GEO_ICE_GEO | 29 | K/km | a glacier's bed is warmer than its surface by this per km of ice (the geothermal flux conducted through it) | GEO: 60 mW/m2 through ice of conductivity 2.1 W/m/K (Cuffey & Paterson 2010) |
| GEO_ICE_BETA | 7 | m/yr per km | glacier mass balance gradient: ice gained (above the snowline) or lost (below it) per km of height | GEO: mass balance gradients 5-10 m/yr per km in mid-latitude mountains (Oerlemans 2001; Anderson, Molnar & Kessler 2006) |
| GEO_ICE_BMAX | 1.5 | m/yr | most snow a glacier gains a year (ice equivalent), at the mean land rain (x Climate.Rainfall's pattern: dry belts gain less) | GEO: accumulation rates of mountain glaciers 0.5-3 m/yr (Oerlemans 2001) |
| GEO_ICE_K | 0.0001 | - | glacial abrasion and quarrying: bed erosion per unit of sliding (E = K u_s) | GEO: 1e-4 (Humphrey & Raymond 1994; Herman et al. 2015 find E ~ u_s^1-2); glaciers erode 1-10 mm/yr, faster than rivers (Hallet, Hunter & Bogen 1996) |
| GEO_ICE_TAU | 100 | kPa | basal shear stress of a glacier: ice of a surface slope S is tau / (rho_ice g S) thick (perfectly plastic ice), and slides at its flux over width x thickness | GEO: 50-150 kPa (Nye 1952; Paterson 1994) |
| GEO_ICE_W | 0.5 | km | width of a glacier of ice flux GEO_ICE_Q; it goes as the flux^0.4, as river widths do; its valley is cut to its width | GEO: valley glaciers are 0.3-3 km wide (Paterson 1994); a trough's width follows its glacier's (Harbor 1992) |
| GEO_ICE_Q | 5E+06 | m3/yr | ice flux of a glacier GEO_ICE_W wide | GEO: a glacier of ~5 km2 of accumulation area at ~1 m/yr |
| GEO_ICE_SMAX | 1 | m/m | steepest ground that holds a glacier: snow avalanches off steeper ground onto the glacier below, which erodes its foot (cirque headwalls, arêtes and horns are what stands) | GEO: snow and ice do not stay on slopes over ~40-50°: headwalls feed glaciers by avalanches (Benn & Evans 2010); tan 45° |
| GEO_ICE_SMIN | 0.03 | m/m | gentlest ice surface slope at which a glacier's thickness is reckoned (thickness at most tau / (rho_ice g x this)) | HT: bounds ice thickness at ~370 m (valley glaciers are 100-1,000 m thick; Paterson 1994) |
| GEO_REBOUND | 0.82 | - | flexural isostasy: the ground rises by this share of the rock eroded from it (and sinks so under sediment laid on it), spread over GEO_FLEX | GEO: rho_crust / rho_mantle = 2,700 / 3,300; why old mountains outlast their erosion (Molnar & England 1990; Watts 2001) |
| GEO_FLEX | 150 | km | radius over which the lithosphere spreads the isostatic response to a load (two box blurs, on the coarse grid at the plates' scale) | GEO: flexural wavelengths of continental lithosphere are 100-300 km for elastic thicknesses of 10-40 km (Watts 2001) |
| GEO_MYR | 60 | Myr | length of the deep time over which uplift and erosion work the land | GEO: active orogens come near a flux steady state in a few to ~10 Myr (Willett & Brandon 2002); this is several times that |
| GEO_ITERS | 200 | steps | steps of uplift, stream power, transport and diffusion over GEO_MYR (on the coarse grid, FW / 2) | HT: 0.3 Myr a step; the incision is implicit, so the step is stable at any length |
| GEO_FINE_ITERS | 40 | steps | steps of deep time that are run again on the field grid at the end (after GEO_ITERS on the coarse grid), at the same step length | HT: the coarse grid (FW / 2) cannot hold a valley and the ridges either side of it; ~12 Myr is enough for the small valleys to come to grade (knickpoints travel K sqrt(A) ~ 1 km/Myr up the smallest) |
| GEO_ROUTE_EVERY | 2 | steps | steps between routing the rain again over the changing relief in deep time | HT: routing (Priority-Flood) is most of deep time's cost; the drainage moves little in 0.6 Myr; 2 halves the time at the same relief statistics |
| GEO_RAIN_EVERY | 25 | steps | steps between re-reading where rain falls on the growing relief (Climate.Rainfall) | HT: rain follows the relief (orographic rain, rain shadows), which changes slowly |
| GEO_N | 2 | - | slope exponent n of stream power, E = K A^(n/2) S^n (the area exponent keeps m / n = 0.5) | GEO: m/n ~0.5 from river concavity (Whipple & Tucker 1999; Whipple 2004); n > 1, near 2, where floods vary and incision has a threshold (Lague 2014; Harel, Mudd & Attal 2016), so relief grows less than in proportion to the uplift rate |
| GEO_PSI | 0.0001 | km^n | erosion threshold: bedrock is cut only where A^(n/2) S^n exceeds this (A in km2) | GEO: channels incise only above a critical shear stress (Tucker 2004; Lague, Hovius & Davy 2005); a channel draining 100 km2 on a slope of 1 in 1,000: graded lowland rivers and lake flats stop wearing down and keep their cover |
| GEO_K_SED | 8 | km^(1-n)/Myr | stream-power erodibility of soft sedimentary rock: dh/dt = -K A^(n/2) S^n (A in km2, n = GEO_N) | GEO/HT: K spans orders of magnitude from crystalline to weak sedimentary rock (Stock & Montgomery 1999); set so a belt rising ~1 mm/yr stands 1-3 km above the lakes, with channels much gentler than the hillslopes at GEO_SC beside them |
| GEO_K_VOLC | 4 | km^(1-n)/Myr | stream-power erodibility of volcanic rock | GEO: lavas, tuffs and breccias: between basement and soft strata (Stock & Montgomery 1999) |
| GEO_K_LIME | 3 | km^(1-n)/Myr | stream-power erodibility of limestone | GEO: massive carbonate is mechanically strong, between basement and volcanic rock (Stock & Montgomery 1999; Sklar & Dietrich 2001); it dissolves as well (karst: Caves.cs) |
| GEO_K_CRYST | 2 | km^(1-n)/Myr | stream-power erodibility of crystalline rock (granite, gneiss) | GEO: the hardest (Stock & Montgomery 1999); erodibility falls with tensile strength squared (Sklar & Dietrich 2001); the contrast is narrowed at cells hundreds of m wide, where jointing and weathering govern |
| GEO_DIFF | 0.01 | km2/Myr | hillslope diffusivity D of the land surface (soil creep), at low slopes; it grows as 1 / (1 - (S/GEO_SC)^2) | GEO: soil creep 0.003-0.01 m2/yr (Fernandes & Dietrich 1997; Roering et al. 1999); 1 m2/yr = 1 km2/Myr |
| GEO_SED_MIN | 0.02 | km | sediment (or limestone) thicker than this makes a cell's surface rock sedimentary (or limestone) | HT: 20 m of strata hides what lies below |
| GEO_ROUGH | 0.06 | - | sub-grid roughness: relief within a coarse cell, as a share of the relief between it and its neighbours | GEO/HT: topography is about self-similar over these scales (Turcotte 1997); it seeds the field grid's drainage, which the last steps of deep time then cut (Geology.Refine) |
| GEO_LIME | 0.8 | km | limestone at the base of a young platform's cover (GEO_COVER): the cover is carbonate below and clastic strata (sedimentary) above; erosion strips the clastic strata first | GEO: platform successions are carbonate-rich (the Russian and North American platforms; Sloss 1963); a carbonate platform builds 0.5-1 km in a few tens of Myr (Schlager 2005) |
| FERT_LIME | 0.65 | - | base fertility of soil on limestone | ECO: limestone weathers to base-rich rendzinas and terra rossa (FAO WRB 2015) |
| FERT_CRYST | 0.4 | - | base fertility of soil on crystalline rock | ECO: granite and gneiss weather to acid, base-poor soils (FAO WRB 2015) |
| FERT_VOLC | 0.85 | - | base fertility of soil on volcanic rock | ECO: volcanic ash and basalt weather to andosols, among the most fertile soils (Shoji, Nanzyo & Dahlgren 1993) |
| FERT_SED | 0.6 | - | base fertility of soil on sedimentary rock | ECO: shales weather to moderately rich soils, sandstones to poor ones; their mean (limestone: FERT_LIME) |
| FERT_VAR | 0.15 | - | local variation of fertility about its parent material (±, from organic matter, drainage and soil age) | HT: was the whole fertility (noise) before Revision R |

## 34. Hydrology

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| FLOOD_MIX | 0.3 | /update | share of the difference between the flooding river's nutrient and a flooded cell's that the flood leaves on the soil, at each water-cycle update (WC_EVERY steps); taken from the river | ECO/HT: the flood pulse brings river nutrients and silt onto the floodplain each year (Junk, Bayley & Sparks 1989; the Nile's ~1 mm of silt a year, Said 1993); 0.5 per 100 steps before the water cycle, the same rate per step at 50 |

## 35. Water cycle

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| WC_EVERY | 50 | steps | steps between water-cycle updates (rain, snow, soil, groundwater, routing, ponds, lakes, drinkable water) | HT: 60 updates a year at T_SEASON (about 6 days each at 365 days a year); a step is STEP_H = 2.9 hours, so the stores change little between updates |
| P_MEAN | 800 | mm/year | mean yearly rain on the land (the rain pattern Climate.Rainfall is relative to it) | ECO: mean land precipitation ~750-800 mm a year (Trenberth et al. 2007) |
| RAIN_LOCAL | 0.35 | - | share of the land's rain that falls as local convective rain of recycled moisture, spread evenly over the land of its latitude; the rest follows the wind-borne pattern (Climate.Rainfall) | ECO: ~40% of land rain comes from land evaporation (Trenberth 1999; van der Ent et al. 2010) and falls near where it rose; HT: without it a third of the land got under a quarter of the mean rain |
| PET_REF | 1000 | mm/year | potential evapotranspiration at T_REF; it follows the saturation vapour pressure e_s(T) / e_s(T_REF) (Tetens), and is 0 at or below 0 °C | ECO: PET of a 20 °C climate ~1,000-1,200 mm a year (Thornthwaite 1948; Hamon 1961: PET ~ saturation vapour density) |
| T_SNOW | 1 | °C | air temperature at or below which rain falls as snow | ECO: the 50% rain-snow line over land lies near 1 °C (Jennings et al. 2018) |
| DDF | 3 | mm/K/day | degree-day factor: snow melted per K above 0 °C per day | ECO: degree-day factors of snow 2.5-6 mm/K/day (Hock 2003) |
| SNOW_MAX | 4000 | mm | deepest snowpack (water equivalent): deeper snow is a glacier, and its ice carries the surplus downhill to where it melts and runs off | ECO: a glacier's accumulation is balanced by ice flow to its ablation zone (Paterson 1994); HT: several years of polar snowfall |
| SOIL_CAP | 150 | mm | water the soil holds for plants (field capacity of the Manabe bucket) on ordinary ground | ECO: Manabe (1969) 150 mm; plant-available water of the root zone 100-200 mm on most soils (Saxton & Rawls 2006) |
| ALLUV_CAP | 1 | - | extra share of SOIL_CAP that deep alluvium adds (x (1 - exp(-alluvium / ALLUV_D))) | ECO: deep alluvial loams hold 200-300 mm in the root zone (Saxton & Rawls 2006), twice thin upland soils |
| INFIL_B | 0.2 | - | shape of the spread of soil capacity within a cell: the saturated share of the cell is 1 - (1 - soil water / capacity)^(b / (1 + b)), and rain on it runs off (variable infiltration capacity) | ECO: Xinanjiang / VIC curve (Zhao 1992; Wood, Lettenmaier & Zartarian 1992), b 0.1-0.4 (Liang et al. 1994) |
| BETA_FC | 0.75 | - | soil wetness (share of capacity) above which evapotranspiration runs at its potential; below it falls in proportion (Manabe beta) | ECO: Manabe (1969): 0.75 |
| E_BARE | 0.4 | - | evapotranspiration of bare ground against a full plant cover (cover = grass + shrub + tree, at most 1) | ECO: transpiration is ~60% of land evapotranspiration (Schlesinger & Jasechko 2014): bare soil dries only its top few cm |
| K_PERC | 3 | mm/day | drainage from a saturated soil to groundwater; it goes as (soil water / capacity)^PERC_EXP | ECO/HT: drainage falls steeply as soil dries (Campbell 1974); groundwater recharge is ~10-30% of rain in humid regions (Doll & Fiedler 2008) |
| PERC_EXP | 4 | - | exponent of drainage on soil wetness | ECO/HT: conductivity falls as a high power of water content (Campbell 1974: 2b + 3 ~ 10 for loam); lower here because the bucket is the whole root zone |
| GW_TAU | 120 | days | groundwater residence time in a cell: each update WC_EVERY / GW_TAU of the shallow store feeds the cell's streams, and of the deep store flows on to the cell it drains to (or into the river or lake there) | ECO: baseflow recession constants of 50-200 days (Tallaksen 1995); water moves downslope cell by cell, so a valley gathers its hillsides' groundwater over years |
| GW_DEEP | 0.3 | - | share of the water draining from the soil (and from ponds' floors) that recharges the deep, regional groundwater, which flows on underground cell by cell down the drainage; the rest recharges the shallow, local groundwater that feeds the cell's own small streams (baseflow) | ECO: most groundwater discharges within its local flow system; a smaller share joins regional systems that emerge in valley bottoms and lowlands (Toth 1963); HT: a cell is several km across, so its own streams take most |
| GW_CAP | 1500 | mm | most deep groundwater a cell holds before its water table reaches the surface; the rest emerges (seepage, springs) | ECO/HT: an aquifer of 10-20 m and 10-20% drainable porosity holds 1-4 m of water; springs and seeps emerge where flow converges and the water table meets the ground (Toth 1963) |
| SPRING_MIN | 2 | mm/day | a groundwater seep (emerging flow) at least this strong is a drinkable spring | HT: a few mm a day over a cell is a permanent trickle; weaker seeps only keep the ground wet |
| POND_CAP | 250 | mm | most water a pond site holds (mm over its whole cell) before it spills downhill (fill-and-spill) | ECO: prairie potholes and pans are 0.5-2 m deep (Hayashi et al. 2016): with POND_FRAC a full pond is 1 m deep on average, 2 m at its deepest (a paraboloid); fill-and-spill (Spence & Woo 2003) |
| POND_FRAC | 0.25 | - | share of a pond site's cell a full pond covers; its wetted area goes as the square root of the water it holds | ECO: area ~ volume^(1/2) for a paraboloid basin (Hayashi & van der Kamp 2000); HT: wetlands cover a few to tens of % of pothole country |
| POND_WET | 10 | mm | water at a pond site (mm over its whole cell) from which the pond is drinkable | HT: the pond then covers 5% of the cell, ~20 cm deep on average |
| POND_SEEP | 2 | mm/day | seepage from a pond's wetted floor into the soil and groundwater | ECO: depression-focused recharge under prairie wetlands ~1-5 mm a day (Hayashi, van der Kamp & Rudolph 1998) |
| E_OPEN | 1.3 | x | open-water evaporation (ponds over their wetted area, lakes) against the potential evapotranspiration | ECO: Penman's E0: grass loses 0.6-0.8 of what open water does (Penman 1948) |
| QMEAN_YEARS | 10 | years | years over which a river's mean discharge (its channel's size, against which it floods) follows the flow it carries | ECO: channels adjust their size to the flows they carry over years to decades (Leopold, Wolman & Miller 1964) |
| LAKE_STAGE | 500 | mm | a lake's stage above its outlet's sill (its level at creation) when it receives its mean inflow; its outlet carries in proportion to the stage (a linear reservoir whose outlet is sized for the lake's mean flow) | ECO/HT: outlet channels are sized by the flows they carry, as river channels are (Leopold, Wolman & Miller 1964); lakes rise a few dm to m in floods; a lake below its sill does not spill |
| WC_SPINUP | 2 | years | years of weather the water cycle runs at world creation (average years), so soil, snow, groundwater and rivers start in their seasonal state | HT: groundwater is first set to the steady state of the first year's recharge, then a second year settles the seasons and gives each river its mean discharge |

## 36. Caves

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| CARB_SHARE | 0.35 | - | share of the sedimentary land that is carbonate (limestone, dolomite): the sedimentary cells with the highest carbonate score (a regional draw of formations plus distance from clastic sources) | ECO: carbonate rocks crop out over ~10-15% of the ice-free land (Ford & Williams 2007), ~20-30% of sedimentary outcrop (Blatt & Jones 1975); HT |
| CARB_L_CLASTIC | 12 | cells | distance from crystalline or volcanic ground (sources of sand and mud) over which clastic input dilutes carbonate: the score rises as 1 - exp(-d / CARB_L_CLASTIC) | ECO: carbonate factories need clear water away from siliciclastic input (Schlager 2005); HT |
| CARB_FAR_W | 0.25 | - | weight of the distance from clastic sources against the regional draw (noise 0..1) in the carbonate score | HT |
| CARB_MIN | 0.5 | - | carbonate share of the rock above which it karstifies (World.Carb) | ECO: pure limestones (> ~70-90% CaCO3) karstify; marls and sandy limestones barely (Ford & Williams 2007) |
| CA_REF | 2 | mmol/L | calcium of water in equilibrium with calcite at pCO2 = 0.01 atm and 10 °C; [Ca]eq ~ pCO2^(1/3) | ECO: Dreybrodt 1988; White 1988 (~2 mmol/L) |
| CA_T | 0.01 | /K | fall of [Ca]eq per K of water temperature above 10 °C | ECO: calcite and CO2 are less soluble in warm water: [Ca]eq at a fixed pCO2 falls ~15% from 10 to 25 °C (Dreybrodt 1988) |
| CAVE_DEN_REF | 30 | mm/kyr | karst denudation at which solution dolines form at DOLINE_P per cell (runoff x [Ca]eq, White 1984) | ECO: carbonate denudation 10-100 mm/kyr, ~30 in temperate humid karst (White 1984; Ford & Williams 2007) |
| CAVE_GRAD | 0.005 | m/m | water-table gradient of karst groundwater toward its springs: the head rises this much per length of conduit | ECO: conduit flow needs little head: karst water-table gradients 0.0005-0.01 (Worthington 1991; Ford & Williams 2007) |
| CAVE_Q_MIN | 0.00038 | km2 | conduit discharge (km2 of mean land rain: Q x Map.CellKm2) at which a passage forms (breakthrough), one CAVE_W1 wide | ECO: conduits need enough flow to reach breakthrough within the time available (Dreybrodt 1990; Palmer 1991); HT: 2026-11-01: in km2 (it was 2 cells x map cells / 65,536), at what it was on the 256 map |
| CAVE_W1 | 2 | m | passage width at CAVE_Q_MIN with water at CA_REF; it grows as sqrt(discharge / CAVE_Q_MIN) x [Ca]eq / CA_REF | ECO: passage size follows discharge (Palmer 1991); explorable caves start at ~1-3 m |
| CAVE_W_MAX | 30 | m | widest passage | ECO: large trunk passages are 10-30 m across, the largest ~100 m (Palmer 2007) |
| CAVE_VALLEY | 1 | - | how much less the head rises under ground that gathers surface water: the head's gradient is CAVE_GRAD / (1 + CAVE_VALLEY log2(1 + Flow / CAVE_Q_MIN)), so conduits follow the valleys and converge on a few springs | ECO: conduits follow the most favourable fractures and beds, often beneath the surface valleys whose pattern the karst inherits, and the water table is depressed toward them (Palmer 1991; Ford & Williams 2007); HT |
| CAVE_WALL_S | 0.35 | m/m | least slope (m/m) from a cell over a relict passage to the next for a valley wall or cliff to open the passage (an entrance) | ECO: cave entrances open in valley walls and cliffs that cut the cave levels (Palmer 2007); HT |
| CAVE_ENTR_GAP | 41 | cells | least distance between two valley-wall cave entrances (the one nearer the spring, on the bigger passage, is kept): one entrance stands for the caves of a stretch of wall (cells: CAVE_ENTR_GAP_M / GEO_CELL_M) | derived (2026-11-01): 41 cells of 16 m (3 cells of 218 m before) |
| CAVE_ENTR_GAP_M | 654 | m | least distance between two valley-wall cave entrances, in metres (CAVE_ENTR_GAP) | HT: 3 cells of ~220 m |
| CAVE_RESURGE | 8 | x | conduit discharge (x CAVE_Q_MIN) of a spring that issues from an open cave (a resurgence entrance) | HT |
| CAVE_ROOF | 3 | x | a passage whose roof is thinner than this many times its width breaks down (a collapse doline, a cave entrance) with COLLAPSE_P per cell | ECO: breakdown where the roof beam is thin against its span (White & White 1969) |
| COLLAPSE_P | 0.3 | - | chance per cell of passage under a thin roof that it has collapsed | HT |
| CAVE_ROOF_MIN | 5 | m | least rock over a relict passage for it to be a cave (not an unroofed trench) | HT |
| CAVE_LEVEL_M | 40 | m | height of the relict cave level above the water table: passages formed when base level stood higher, left dry as the valleys deepened; an entrance where the ground beside it drops below it | ECO: cave levels track river incision, tens of m per few 100 kyr (Palmer 1987; Granger, Fabel & Palmer 2001) |
| DOLINE_P | 0.02 | - | chance per karst cell, at CAVE_DEN_REF of denudation and slopes under DOLINE_S, of a solution doline big enough to show | ECO: doline densities 10-100 per km2 in doline karst (Ford & Williams 2007), most far smaller than a cell; HT |
| DOLINE_S | 0.15 | m/m | steepest ground (m/m) with solution dolines | ECO: doline fields lie on plateaus and gentle slopes (Ford & Williams 2007) |
| CAVE_LOSE_H | 10 | m | depth of the water table under a river bed (or a ponor) for the river to lose water to the karst | ECO: a stream loses where it stands above the water table (Ford & Williams 2007); HT |
| CAVE_LEAK | 0.05 | - | share of a losing river's water that sinks into the conduits per cell of its course over the karst | ECO: losing reaches lose a few % of their flow per km and may sink entirely in low water (White 1988); HT |
| CAVE_PONOR | 0.7 | - | share of a stream from other rock that sinks at its ponor where it reaches the karst | ECO: allogenic streams sink at the contact (Ford & Williams 2007); HT: floods overflow the ponor (CAVE_CAP) |
| CAVE_CAP | 3 | x | most a sinkhole or losing reach swallows, x its mean intake (CaveSink x the cell's mean discharge): a flood beyond backs up and runs on | ECO: ponors and conduits flood and overflow in high water (White 1988); HT |
| CAVE_TAU | 10 | days | drainage time of karst groundwater through the conduits to its spring (x the rock's carbonate share) | ECO: karst spring recession: quickflow coefficients 0.1-0.5 per day, diffuse flow 0.001-0.02 (Ford & Williams 2007; Kovacs et al. 2005) |
| CAVE_SWING | 0.2 | - | share of the outside air's departure from the year's mean that a cave entrance keeps (deeper in, none) | ECO: cave air stays near the mean annual surface temperature (Badino 2004); entrance zones keep a damped part of the outside swing (de Freitas, Littlejohn et al. 1982) |
| LAVA_S | 0.14 | m/m | steepest ground (m/m) under which a lava flow roofs over into a tube | ECO: tube-fed pahoehoe on slopes of ~1-8 degrees (Greeley 1987; Kauahikaua et al. 1998) |
| LAVA_LEN | 818 | cells | longest lava tube traced from a vent (cells: LAVA_LEN_M / GEO_CELL_M) | derived (2026-11-01): 818 cells of 16 m (60 of 218 m before) |
| LAVA_LEN_M | 13080 | m | longest lava tube traced from a vent, in metres (LAVA_LEN) | ECO: tubes run up to tens of km (Kazumura Cave, 65 km; Greeley 1987) |
| LAVA_VENT_R | 4 | cells | radius within which a vent is the highest volcanic ground | HT |
| LAVA_VENT_P | 0.5 | - | chance that a volcanic high is a vent whose flows made tubes | HT |
| LAVA_W | 8 | m | lava tube width | ECO: tubes are 1-15 m wide, ~5-10 typically (Greeley 1987) |
| LAVA_D | 8 | m | rock over a lava tube | ECO: roofs of a few m to ~15 m (Greeley 1987) |
| LAVA_SKY | 0.1 | - | chance per cell of a lava tube that its roof has fallen in (a skylight, an entrance) | ECO: skylights every few hundred m along active tubes (Kauahikaua et al. 1998); HT |

## 37. Wetlands

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| WT_SY | 0.1 | - | drainable porosity (specific yield) of the ground over which deep groundwater (WaterCycle: Deep) raises the water table: GW_CAP fills 15 m of it | ECO: specific yield 0.01-0.3, ~0.1-0.2 for sand and gravel, 0.02-0.1 for silt and till (Johnson 1967; Freeze & Cherry 1979) |
| WT_F | 2 | /m | TOPMODEL's decay of transmissivity with depth: within a cell the water table stands (TWI - mean TWI) / WT_F higher than its mean where the ground converges and is flat | ECO: f = 1-10 /m in TOPMODEL applications (Beven & Kirkby 1979; Beven 1997); 2 /m (an e-folding depth of 0.5 m) for loamy soils |
| WET_ZWT | 0.3 | m | the ground is waterlogged in an update where its water table stands at most this deep (or groundwater emerges, a pond stands, a river floods) | ECO: the wetland hydrology criterion, saturation within 30 cm (12 inches) of the surface (US Army Corps of Engineers 1987; NRC 1995) |
| WET_DUR | 0.25 | - | land waterlogged at least this share of the year is a wetland (WetDur) | ECO/HT: wetlands are saturated for much of the growing season (Cowardin et al. 1979: seasonally to permanently saturated); a quarter of the year is a seasonal marsh's, against 14 days for a jurisdictional one |
| WET_MINERO | 0.1 | - | a peatland is a bog (ombrotrophic: fed by rain alone) where emerging groundwater or floods wet it less than this share of the year (WetMin); otherwise a fen | ECO: bogs are raised above the groundwater and fed by rain, fens by mineral groundwater or surface water (Rydin & Jeglum 2013); HT |
| WET_WOODY | 0.3 | cover | a wetland without peat is a swamp where shrubs and trees cover at least this much of it, else a marsh | ECO: Cowardin et al. 1979: forested and scrub-shrub wetlands have >= 30% woody cover |
| TWI_SMIN | 0.001 | m/m | least slope of the wetness index ln(a / tan b) (flats and filled hollows) | -: numerical; TOPMODEL implementations floor tan b at 0.0001-0.001 |
| ANOX_ROT | 0.2 | x | decomposition of litter and wood in waterlogged (anoxic) ground, against drained ground | ECO: anaerobic decay runs at ~10-30% of aerobic (Clymo 1984; Freeman, Ostle & Kang 2001: the enzymic latch) |
| PEAT_FORM | 5E-05 | /step | share of the litter and wood of waterlogged ground that humifies into peat a step (x the share of the year it is waterlogged) | ECO/HT: in permanently waterlogged ground ~30% of the litter outlasts decay into peat (Clymo 1984: 10-20% of production reaches the catotelm); 5e-5 against ROT_LITTER x ANOX_ROT = 1.2e-4 |
| PEAT_M | 0.0025 | m | depth of peat that a unit of litter (or wood) mass makes | HT: calibrated so permanently waterlogged ground with the spun-up litter of the default map lays down ~0.3-1 mm of peat a year (Gorham 1991: long-term peat accumulation 0.2-1 mm/yr, ~0.5 typical) |
| PEAT_ROT_AN | 0.0003 | /year | decay of peat under water (the catotelm), a year at T_REF | ECO: Clymo's (1984) alpha of 1e-4 - 1e-3 a year for deep peat; 3e-4 (Clymo, Turunen & Tolonen 1998) |
| PEAT_ROT_OX | 0.015 | /year | decay of drained peat, a year at T_REF | ECO: drained peat oxidises and subsides ~1-2 cm a year (Hooijer et al. 2012; Leifeld et al. 2011), ~1-2% a year of a 1-m peat |
| PEAT_AGE | 10000 | years | years since the last glaciation over which a new world's peat has formed (Wetlands.Holocene) | ECO: most northern peatlands began 8-12 kyr ago (MacDonald et al. 2006) |
| PEAT_MIN | 0.4 | m | peat at least this deep makes a peatland (fen or bog) | ECO: Histosols and peatlands have >= 40 cm of organic soil (Soil Survey Staff 1999; Canada's 40 cm) |

## 38. Respiration

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| LUNG_SCOPE | 10 | xbasal | most oxygen full lungs take up in sea-level air, as a multiple of basal metabolism (an aerobic scope) | ECO: maximal over basal oxygen uptake is ~10 in mammals and birds (Weibel & Hoppeler 2005; Bishop 1999) |
| GILL_SCOPE | 4 | xbasal | most oxygen full gills take up in air-saturated water at T_REF, as a multiple of basal metabolism | ECO: the aerobic scope of fishes is 2-5 (Killen et al. 2016; Clark, Sandblom & Jutfelt 2013): water holds ~1/30 of air's oxygen and is ~800x denser and ~50x more viscous, so gills extract more (~80% against ~25%) but cannot pump much more of it (Schmidt-Nielsen 1997; Dejours 1981) |
| GILL_AIR | 0.02 | - | share of gills' uptake left in air: the lamellae collapse and stick together, and dry | ECO: out of water the secondary lamellae coalesce and a few % of the gas-exchange area stays open; most fishes suffocate within minutes to hours in air, and air-breathing fishes have stiffened or reduced gills (Graham 1997). With GILL_SCOPE, 0.08 of basal: a warm body suffocates at once, a cold-blooded one at 20 °C (its rate ~0.2) slowly |
| SKIN | 0.2 | xbasal | oxygen the skin takes up at M = 1, as a multiple of basal metabolism; it scales with surface over volume, M^(-1/3), and fur or feathers (the insulation gene) cover it | ECO: small frogs and salamanders take 25-50% of their resting oxygen through the skin, lungless salamanders all of it (Feder & Burggren 1985); a 0.1-unit body here takes 0.43 of basal |
| GILL_EVAP | 3 | x | out of water, full gills make a body dry out this many times faster again (their wet surface evaporates): x (1 + GILL_EVAP x gill share) | ECO: gills are a thin, wet epithelium of 10-50x the body's skin area; out of water fishes and gill-breathing crabs lose water many times faster than air-breathers of their size, and amphibious fishes and land crabs have reduced gills (Graham 1997; Burggren & McMahon 1988); HT |
| GILL_VENT | 0.1 | - | share of its metabolism a gill-breather spends pumping water over its gills, in air-saturated water at T_REF; more as the water holds less oxygen (x saturation^-1) | ECO: ventilating water costs fishes ~10% of resting metabolism, breathing air costs mammals 1-2% (Schmidt-Nielsen 1997; Jones & Schwarzfeld 1974) |
| AIR_VENT | 0.02 | - | share of its metabolism an air-breather spends breathing at sea level; more in thin air (x partial pressure^-1) | ECO: 1-2% at rest in mammals (Schmidt-Nielsen 1997) |
| O2_STORE | 0.035 | basal steps | oxygen stored in blood, muscle and lungs for a breath-hold dive, in steps of basal metabolism at M = 1; it grows as M, so a dive lasts ~M^0.25 | ECO: aerobic dive limits of ~6 min at 1 kg to ~1 h for great whales, ~M^0.25-0.3 (Schreer & Kovacs 1997; Kooyman 1989; Halsey, Butler & Blackburn 2006); 0.035 steps of ~3 h is 6 min |
| DEBT_MAX | 2 | basal steps | oxygen debt a body bears before it dies (anaerobic metabolism), in steps of basal metabolism at M = 1, x M: so ~M^0.25 steps without oxygen at rest | ECO: vertebrates live minutes to hours on anaerobic glycolysis, the anoxia-tolerant (crucian carp, turtles) far longer in the cold (Hochachka & Somero 2002); a cold body burns less and lasts longer here (Heat.Rate) |
| DIVE_V | 1.5 | m/s | vertical speed of a diving swimmer | ECO: divers descend at 1-2 m/s whatever their size (Schreer & Kovacs 1997; Watanabe et al. 2011) |
| K_LIGHT | 0.1 | 1/m | light attenuation coefficient of water (Beer-Lambert, I = I0 exp(-k z)): plankton, which grows by light, lies near the surface | ECO: 0.05-0.2 /m in lakes and coastal seas (Kirk 2011); 1% of the light at ~46 m |
| Z_MIX | 15 | m | depth of the wind-mixed surface layer of standing water: as warm as the surface and saturated with oxygen | ECO: epilimnia of lakes 5-20 m, ocean mixed layers 10-100 m (Wetzel 2001) |
| Z_THERMO | 30 | m | below the mixed layer the water cools toward T_DEEP by e per this depth (the thermocline) | ECO: Wetzel 2001 |
| T_DEEP | 4 | °C | temperature of deep standing water | ECO: fresh water is densest at 4 °C, and hypolimnia stay near it (Wetzel 2001) |
| Z_HYPOX | 50 | m | below the mixed layer of stratified standing water, oxygen falls by e per this depth; fully when the surface is T_DEEP + 16 °C warmer, not at all when it is no warmer (cold lakes mix) | ECO: hypolimnetic oxygen depletion in warm stratified lakes, oxygen minimum zones at 200-1,000 m in the sea (Wetzel 2001; Keeling, Kortzinger & Gruber 2010); HT |
| STAND_H | 0.25 | m | standing height of a body of M = 1 on full legs, ~M^(1/3): water shallower than this leaves its head in the air | ECO: shoulder height ~0.25 m at 1 kg to ~1.5 m at 300 kg (Garcia & da Silva 2006) |
| LEG_PADDLE | 0.3 | - | legs paddle: a share of a fin's worth for keeping its head above water | ECO: most land mammals swim by paddling (Fish 1993) |
| BOTTOM_BAND | 2 | m | an animal within this height of the bottom reaches what lies on it (attached algae, detritus) | HT |
| STRIKE_DZ | 5 | m | two animals in water reach each other only when their depths are within this | HT: a lunge or bite; a step is hours, so this stands for how often they meet in the column |

## 39. Organisms

There are no designed species (Revision F). Life starts from 10 land and 6 water ancestors with random bodies (Clades.AncestorBody), and every organism's body is expressed from its own genes on the ranges above (DESIGN §6.3).
