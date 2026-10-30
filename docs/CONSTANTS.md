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
| HCELL | 16 | units | spatial-hash cell side | V1 |
| GW | 64 | cells | spatial-hash grid side | - |
| CAPACITY | 16384 | slots | default agent slots (animals, eggs, embryos) | HT: Revision N, ~200 KB per slot with 256-unit brains (World.BytesPerSlot): 8,192 is ~1.7 GB |
| LAKE_SHARE | 0.1 | fraction | share of cells below lake level | HT |
| SHALLOW | 0.035 | elev | depth below which water is shallow (wadeable, algae grow) | HT |
| T_SEASON | 3000 | steps | season period (one year), by default | ECO: about one lifespan at M=1, so seasonal life histories can evolve (ecology review; 9000 before Revision K) |
| A_SEASON | 0.45 | - | season amplitude on moisture | V1 |
| SAMPLE_EVERY | 32 | steps | telemetry sample interval | HT |
| RING | 4096 | samples | telemetry ring length | HT |
| LONG_RING | 2048 | samples | long-history ring length (one sample in LONG_EVERY) | HT: with LONG_EVERY it covers 33 million steps |
| LONG_EVERY | 512 | samples | samples between long-history samples | HT |
| MAX_NEIGH | 64 | animals | max neighbours examined per animal per step | HT |
| MAX_SIGHT | 32 | units | max sight range (bounded by the 5x5 hash neighbourhood) | HT |

## 2. Allometry

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| EXP_BASAL | 0.75 | - | Kleiber exponent for basal cost and digestion | ECO |
| EXP_SPEED | 0.17 | - | max speed exponent | ECO |
| EXP_LIFE | 0.25 | - | lifespan and maturity exponent | ECO |
| EXP_SIGHT | 0.2 | - | sight range exponent | HT |
| EXP_TURN | -0.2 | - | turn rate exponent | HT |
| C_BASAL | 0.004 | E/step | basal cost at M=1 | ECO: Revision M, ecology review: x4, so an animal burns many times its tissue energy over a life (Rubner 1908; Speakman 2005) and fasts ~1/16 of a life, not a third (Lindstedt & Boyce 1985) |
| C_EMAX | 2 | E/M | energy reserve per unit mass | ECO: stored reserve of the same order as tissue energy |
| C_GUT | 0.3 | M/M | gut capacity per unit mass | HT |
| C_DIGEST | 0.025 | M/step | digestion rate at M=1 | ECO: Revision M: the most an animal can digest is ~5x basal at 0.5 digestibility (Hammond & Diamond 1997) |
| C_BITE | 0.06 | M/step | bite size at M=1 | Revision M: x5 with digestion (a bite fills the gut faster than it empties) |
| C_SPEED | 1.2 | units/step | max speed at M=1 (walk) | V1 |
| C_TURN | 0.32 | rad/step | turn rate at M=1 | V1 |
| C_LIFE | 3200 | steps | lifespan at M=1 | V1 |
| C_MATURE | 250 | steps | maturity age at M=1 | V1 |
| C_SIGHT | 16 | units | sight range at M=1 | V1 |
| C_MOVE | 0.004 | E/(M^0.7*v*C_SPEED) | movement cost (x C_SPEED x M^EXP_MOVE x speed) | ECO: Revision M: x4 with basal |
| EXP_MOVE | 0.7 | - | mass exponent of the cost of transport | ECO: running cost per unit distance scales ~M^0.7 (Taylor, Heglund & Maloiy 1982) |
| GLIDE | 0.3 | - | speed gained on land per unit of wing that cannot yet lift the body (wing-assisted running, gliding) | ECO: proto-wings aid running up slopes and gliding before powered flight (Dial 2003) |

## 3. Reproduction

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| C_SIRE | 0.2 | - | cost to a hermaphrodite of siring, as a share of its own litter's cost (male function) | ECO: male function takes 10-30% of a simultaneous hermaphrodite's reproductive resources (Charnov 1982) |

## 4. Clades

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| SPLIT_GAP | 3 | - | two forms are separate clades when, along the axis between their centres (visible traits), the gap is this many pooled SDs | ECO: genotypic clusters with few intermediates (Mallet 1995) |

## 5. Allometry

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| C_THIRST | 0.0012 | /step | thirst rise per step (death after ~830 dry steps) | HT |
| C_FATIGUE | 0.012 | /step | fatigue gain at full sprint | HT |
| C_RECOVER | 0.004 | /step | fatigue recovery (x3 resting) | HT |
| REST_BASAL | 0.85 | - | basal multiplier while resting | HT |
| SWIM_SPEED | 0.85 | - | speed factor for swimmers | HT |
| FLY_SPEED | 1.4 | - | speed factor for flyers | HT |
| FLY_COST | 0.5 | - | movement cost factor for flyers (per distance, against running) | ECO: flying costs less per distance than running at equal mass (Schmidt-Nielsen 1972); wing upkeep is in basal cost |
| DRINK_RATE | 0.25 | /step | drinking: thirst removed per step | HT |
| GROWTH_EFF | 0.75 | - | efficiency of turning energy into new tissue (growth, offspring) | ECO: net growth efficiency 0.6-0.8 |
| GROW_RESERVE | 0.2 | xEmax | a juvenile keeps this share of its reserve against starving; above it, it grows at its top rate times how full the reserve is | ECO: growth draws on the reserve in proportion to its density (dynamic energy budget, Kooijman 2010); hungry young grow slowly (Revision D; Revision P: was a threshold of 0.5, below which the young, living near 40% full, hardly grew) |

## 6. Food

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| RHO_GRASS | 3.2 | E/M | energy density of grass | V1 |
| RHO_BROWSE | 3.6 | E/M | energy density of browse (leaves) | HT |
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

## 7. Hunting

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

## 8. Brain

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
| MUT_RATE | 1.01445E-05 | - | mutation probability per brain or reflex byte (1/LEN: about one mutation per offspring) | ECO: on the order of one mutation per genome per generation in functional sequence (Drake 1998; Lynch 2010); was 0.02 (~39 per offspring, mutational meltdown) |
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

## 9. Prehistory

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| PREHISTORY_STEPS | 500000 | steps | length of a prehistory run | HT |
| BRANCHES | 2 | branches | number of separate branches (populations on the same land) evolving side by side | HT (Revision M: 3 before; memory) |
| YEARS_PER_STEP | 1000 | years | symbolic time shown in the console: years per step | - |
| BRANCH_CAPACITY | 16384 | slots | agent slots per branch | HT: Revision M, 2 x 16,384 slots of 256-unit brains is ~5 GB (the report warns if the cap binds) |

## 10. Brain

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| GROUP_SIZE | 8 | agents | founders start in groups of this size at one site | ECO: populations are clustered; lone colonists cannot find mates (Allee effect) |
| GROUP_SPREAD | 10 | units | radius of a founder group | HT |
| HOME_R_MAX | 0.33 | - | largest radius of a founding lineage's home range, as a share of the map side (a big body's range widens up to this until it can feed FOUND_MIN founders) | HT: a third of the map; home ranges grow about in proportion to body mass (Kelt & Van Vuren 2001) |
| HOME_R | 0.12 | - | radius of an ancestor's home range, as a share of the map side | Revision N (the user): each lineage starts in its own part of the map; species have homelands and meet as they spread |
| HOME_TRIES | 24 | - | random sites tried when choosing a home range (the one farthest from the others is taken) | HT |
| BANK_LIVING | 48 | genomes | genomes per species stored in a gene bank (living sample) | HT (Revision M: 96 before) |

## 11. Body

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| BODY_RANGE_TRAIT | 1.6 | - | range of the speed and sight multipliers (x/÷) | HT |
| COST_SPEED | 0.4 | - | basal cost per unit of extra speed | HT |
| COST_SIGHT | 0.15 | - | basal cost per unit of extra sight | HT |
| ARMOUR_DRAG | 0.2 | - | speed loss per unit of extra armour | HT |
| MASS_LO | 0.1 | M | smallest body mass a mass gene can give (the floor of the smallest size class) | ECO: Revision M: 0.01 gave tiny bodies 32x the numbers from the same food; the user (2026-10-30) asked for size classes with their own floors |
| FLOOR_CLASSES | 4 | - | number of body-plan size classes; class k cannot be smaller than Body.Floors[k] (0.1, 1, 3, 10) | designed: the user (2026-10-30): classes with different floors, inherited, rarely changing; size evolves freely above the floor |
| FLOOR_MUT | 5E-05 | - | chance per birth that the size class steps up or down one class | designed: the user: inherited, rarely changes (a body-plan change is rare; ~1 in 20,000 births) |
| MASS_HI | 40 | M | largest body mass a mass gene can give | ECO: a large mammal-sized organism (relative units) |
| LIMB_MIN | 0.3 | - | organ size (0..1) below which an organ is vestigial (for describing and drawing bodies; speeds are smooth) | - |
| COST_LIMB | 0.15 | xbasal | basal cost per unit of fin size, or of leg size per pair of legs | ECO: locomotor muscle is maintained at a cost |
| COST_WING | 0.5 | xbasal | basal cost per unit of wing size, times mass^(1/3) | ECO: flight power rises faster than mass, so flight favours the small |
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
| C_GEST | 150 | steps | gestation (or incubation) at M=1, scaling with M^0.25 | ECO: gestation scales about as M^0.25 |
| C_COOL | 150 | steps | breeding cooldown at M=1, scaling with M^0.25 | ECO: as gestation |
| WING_LIFT | 1.6 | - | a body can fly when wing size x WING_LIFT reaches mass^(1/3) (wing loading) | ECO: lift scales with wing area, weight with volume, so large flyers need relatively bigger wings |
| DEEP_SWIM | 0.3 | - | swimming power (share of the body's top speed in water) needed to stay in deep water | ECO: staying up in open water needs swimming power |
| SWIM_DEPTH_RANGE | 14 | x | additional maximum water depth per unit fin size, in SHALLOW-depth units | HT |
| CRAWL | 0.05 | - | a legless body crawls on land at this share of its top speed | ECO: worms, snakes and stranded fish move slowly without legs |
| C_CALL | 0.5 | xbasal | energy of one call, as a share of basal metabolism at the caller's mass | ECO: vocalising costs a few times resting metabolism for its duration (Ophir et al. 2010); a call is a short burst |
| VOICE_MIN | 0.25 | - | a voice organ below this makes no sound | HT: a little voice is no voice; above founder variation around a mute ancestor |
| COST_VOICE | 0.03 | xbasal | basal cost of a full voice (larynx and its control) | ECO: small; vocal tissue and its neural control are a minor part of upkeep |

## 12. Voice

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| MAX_SYL | 3 | sounds | syllables in one utterance at most (distinct sounds, in the order of how strongly each is intended) | ECO: animal calls combine a few units into sequences, rarely more (Engesser & Townsend 2019) |
| CALL_SYL | 0.5 | - | share of a call's cost that grows with each syllable: an utterance of n syllables costs C_CALL x basal x (1 - CALL_SYL + CALL_SYL x n) | ECO: a longer song costs more, but much of a call's cost is the start-up (Oberweger & Goller 2001; Ophir et al. 2010) |
| HEAR_R | 16 | units | how far a full voice of a body of M=1 carries (range = HEAR_R x voice x M^(1/3), at most MAX_SIGHT) | ECO: bigger animals call louder and are heard further (Fletcher 2004; Wiley & Richards 1978); about the sight range at M=1 (C_SIGHT) |
| FOCAL_KEEP | 0.7 | - | a listener keeps attending to the same caller while it calls at least this share as loud as the loudest alike caller | ECO/HT: listeners stream one source among many (Bee & Micheyl 2008); hysteresis keeps attention from flickering |
| TMPL_HALF | 1000 | steps | steps over which the counts of a remembered tutor's syllable pairs halve | ECO/HT: a template fades without new tutoring; about a third of a year (Doupe & Kuhl 1999; Tchernichovski et al. 2001); the young copy form from elders, so a language changes by iterated learning (Kirby, Cornish & Smith 2008) |
| IMIT_RBAR | 0.05 | /step of calling | rate of the running mean of an organism's own match to its template | HT: a baseline, as RBAR_RATE is for reward |
| IMIT_AGE_LO | 0.1 | - | shortest sensitive period for memorising tutors, as a share of lifespan (the IMIT_AGE gene spans IMIT_AGE_LO..1) | ECO: songbirds memorise early in life, some species throughout (Brainard & Doupe 2002) |

## 13. Food

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| SCAVENGE | 0.8 | - | share of its meat-digesting skill a meat-eater brings to carrion | ECO: almost all carnivores scavenge; wolves feed on a kill for days (DeVault et al. 2003) |

## 14. Land

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| POND_R | 2 | cells | a pond site is the lowest cell within this many cells | ECO: about one basin per hundred square cells on rolling ground (prairie potholes, desert playas) |
| POND_GAP | 6 | cells | pond sites are at least this far from a lake, sea or river | HT: a pond beside a lake adds nothing |

## 15. Body

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

## 16. Brain

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| BIG_BRAINS | 1 | - | big-brained ancestors per branch (the first on land, then alternating water and land), by default | designed: the user (Revision M): ONE lineage far smarter than the rest, like a human species; the big-brained land ancestor is also the voiced one |
| BIG_BRAIN_UNITS | 256 | units | memory units of a big-brained ancestor, by default | designed: the user (Revision M): the largest brain, ten times an ordinary ancestor's |
| DRIVE_FEMALE | 0.6 | - | strength of the mating instinct in females, against males (1) | designed by the user (2026-10-28): males are the more eager sex (Bateman 1948; Trivers 1972) |
| DRIVE_HERM | 0.8 | - | strength of the mating instinct in hermaphrodites, against males (1) | designed: between the two sexes |

## 17. Body

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| COST_HERM | 0.05 | xbasal | basal cost of being a hermaphrodite (keeping both sexes' organs) | ECO: hermaphrodites maintain male and female function |
| COST_IMMUNE | 0.12 | xbasal | basal cost of full immune investment | ECO: immune defence is costly (Lochmiller & Deerenberg 2000) |
| IMMUNE_PROTECT | 0.8 | - | share of infection and disease death that full immune investment prevents | ECO: partial, not total, protection |
| D_COMPAT | 0.25 | - | genetic distance at which mating succeeds with probability 1/e: p = exp(-(d/D)^4) (body genes / 127, RMS per gene) | ECO: Dobzhansky-Muller incompatibilities snowball with the square of the substitutions between lineages (Orr 1995); d^2 grows with substitutions: within a population (d ~0.13) p ~0.9, between independent ancestors (d ~0.4) p < 0.001 |
| KIN_LOOK | 0.25 | - | others whose visible body genes are this close look like kin (body genes / 127, RMS per gene) | ECO: animals recognise their kind by appearance |

## 18. Clades

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

## 19. Reward

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| R_SPEND | 1 | - | energy spent on its choices (strikes, calls, moving, heat) counts against the reward at this share of the weight energy eaten counts for it | ECO: animals learn by net energy gain (optimal foraging; Stephens & Krebs 1986); 1 = a unit spent weighs as a unit eaten. 2026-10-30: without it, calling (19-24% of basal) rose from ~38% to ~65% of steps by learning |

## 20. Reproduction

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| READY_LO | 0.25 | xEmax | lowest breeding condition a body can have: it breeds once its reserve is above this share (B_BREEDE gene) | ECO: income breeders breed on what they eat as they go, capital breeders on stored reserves (Jonsson 1997; Stephens et al. 2009); was the fixed READY_ENERGY = 0.6 until 2026-10-30 |
| READY_HI | 0.9 | xEmax | highest breeding condition a body can have | ECO: as READY_LO |
| F_BIRTH | 0.4 | xEmax child | newborn energy (paid by parent at conception) | V1 |
| C_MATE | 0.02 | E/M | mating cost per partner | HT |
| REFRACT | 0.1 | - | a male's refractory period after mating, as a share of the breeding cooldown | ECO: male refractory periods are short against female breeding cycles (Bateman 1948; Trivers 1972) |

## 21. Care

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| CARE_RATE | 1.5 | xbasal | milk or chick-feeding energy a mother can give per step, as a multiple of her basal cost (shared by all her young) | ECO: peak lactation milk energy is about 1-2x maternal basal metabolic rate (Hanwell & Peaker 1977) |
| CARE_EFF | 0.7 | - | efficiency of turning maternal energy into milk or food for the young | ECO: milk synthesis efficiency 0.6-0.8 |
| WEAN | 0.4 | - | care ends (weaning) when the young has built this share of its adult tissue | ECO: mammals wean at roughly a third to a half of adult mass |
| CARE_RESERVE | 0.4 | xEmax | a mother keeps at least this share of her energy reserve for herself | ECO: females in poor condition reduce or stop provisioning |

## 22. Pathogens

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| PG_REP_LO | 0.03 | /step | within-host growth rate at gene 0 (log scale to PG_REP_HI) | ECO: in vivo doubling times run from hours (Salmonella in mouse tissues, Grant et al. 2008) to about a day (M. tuberculosis): ln2/0.03 = 23 steps (~5 days) |
| PG_REP_HI | 0.5 | /step | within-host growth rate at gene 255 | ECO: ln2/0.5 = 1.4 steps (~7 h), a fast bacterial pathogen in a host |
| PG_QS_LO | 0.01 | load | quorum-sensing threshold at gene 0 (log scale to PG_QS_HI), in load | ECO: QS switches at a cell density set by the signal's receptor affinity, which mutates readily (Miller & Bassler 2001) |
| PG_QS_HI | 3 | load | quorum-sensing threshold at gene 255, in load (above 1 a host never holds a quorum: always stealthy) | ECO: QS-blind (lasR) mutants are common in chronic infections (Smith et al. 2006) |
| PG_HILL | 4 | - | steepness (Hill coefficient) of the quorum switch: on = L^n / (L^n + threshold^n) | ECO: autoinduction makes QS a switch-like positive feedback (Miller & Bassler 2001); LuxR-type responses have Hill coefficients ~2-4 |
| PG_STEALTH | 0.1 | - | share of toxin and shedding expressed below quorum (stealth mode) | ECO: QS controls most secreted virulence factors; QS-off cells make a small fraction (Pseudomonas lasR mutants, Rumbaugh et al. 2009) |
| PG_DETECT | 0.3 | - | immune detection below quorum, relative to a quorate population | ECO: QS-regulated exoproducts are what the immune system mostly sees; innate receptors still see the cells (PAMPs, Janeway 1989) |
| PG_DECAY_HI | 0.3 | /step | decay rate in the environment at persistence 0 (log scale to PG_DECAY_LO) | ECO: enveloped viruses and vegetative bacteria die within hours outside a host (half-life ~2 steps, ~10 h) |
| PG_DECAY_LO | 0.0002 | /step | decay rate in the environment at persistence 255 | ECO: spores and helminth eggs survive years (half-life ~3,500 steps); anthrax spores decades (Walther & Ewald 2004) |
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
| PG_RISE | 0.3 | /step | most a specific immune response multiplies per step (clonal expansion, x stimulation x detection x (PG_NAIVE + effector)) | ECO: within-host dynamics dX/dt = rho X P/(P + phi) (Antia, Levin & May 1994); activated T cells divide every 6-8 h (De Boer et al. 2001), ~0.4-0.6 per 5-h step |
| PG_NAIVE | 0.02 | - | naive precursors a specific response expands from (in effector units) | ECO: a few hundred naive precursors per antigen, against ~10^5-fold expansion at the peak (Blattman et al. 2002) |
| PG_HALF | 0.01 | load | load at which immune stimulation is half its maximum | ECO: small antigen doses start an adaptive response (Bachmann & Jennings 2010); here the founding dose PG_INOC, so a stealthy strain is seen less but not ignored |
| PG_E_DECAY | 0.02 | /step | decay of the immune effector per step (contraction) | ECO: effector T cells contract within days to weeks after the peak |
| PG_IMM_BASE | 0.5 | - | immune stimulation of a host with no immunity gene (innate); the gene adds up to 1 | ECO: innate immunity alone controls many infections (Janeway & Medzhitov 2002) |
| PG_MHC_DIV | 0.5 | - | gain in immune stimulation at full MHC diversity (the spread of its recognition genes) | ECO: MHC heterozygote advantage: more distinct alleles present more antigens (Doherty & Zinkernagel 1975; Penn, Damjanovich & Potts 2002) |
| PG_CROSS_W | 0.25 | - | cross-immunity width: protection by memory falls as exp(-d^2/w^2) of antigen RMS distance (/127) | ECO: titres fall with antigenic distance; drifted influenza escapes old immunity (Smith et al. 2004) |
| PG_MEM_DECAY | 0.0003 | /step | decay of immune memory per step | ECO: antibody half-lives of 1-10+ years (Amanna, Carlson & Slifka 2007): ~2,300 steps here |
| PG_RECALL | 1 | - | immune effector at once on reinfection, per unit cross-immunity (the secondary response) | ECO: memory responses are faster and larger (Ahmed & Gray 1996) |
| PG_DMG | 0.1 | /step | tissue damage per step per unit toxin x load (1 = death by disease) | ECO: virulence is damage from pathogen load and toxins (Casadevall & Pirofski 1999); untreated cholera can kill within a day of the first symptoms (Sack et al. 2004): a full-toxin strain at full load kills in ~11 steps (~2 days) |
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
| PG_FLOW | 0.5 | /step | share of a river cell's pathogens carried one cell downstream per step | ECO: rivers run far faster than a cell (16 m) per step (5 h); the downstream-first update moves a cell's load one cell per step at most |
| PG_CARCASS | 1 | - | share of its load a dead host leaves in the ground where it died | ECO: carcasses are reservoirs (anthrax, Turner et al. 2014) |
| PG_RES_MIN | 1E-05 | load | reservoir load below which a cell is clean | HT: picked up by touch it is a hazard of 2e-5 per step |
| PG_FOUNDERS | 12 | strains | founding strains at world start, each a random genome in a few random cells | HT: replaces the old flat spillover; nothing seeds them again |
| PG_SEED_CELLS | 500 | cells | cells each founding strain is laid in, at random over the map | HT: 12 x 500 cells is ~9% of a 256^2 field: environmental microbes are everywhere, most of them unable to grow in any host |
| PG_SEED | 2 | load | load put in each founding cell | HT |
| PG_STRAIN_D | 0.1 | - | two pathogens are one strain (for telemetry) within this RMS genome distance (traits /255, antigen /127) | HT: one transmission drifts a genome ~0.03 by PG_MUT and MHC_MUT, so a strain is about a dozen transmissions of drift |
| PG_CLUSTERS | 256 | strains | most strains telemetry tells apart | HT |

## 23. Weather

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| RAIN_SD | 0.25 | - | standard deviation of the yearly rainfall anomaly | ECO: interannual rainfall CV 20-35% in semi-arid regions |
| RAIN_RHO | 0.4 | - | persistence of the rainfall anomaly from one year to the next (AR(1)) | ECO: droughts cluster in runs of years |
| RAIN_MAX | 0.7 | - | limit of the rainfall anomaly | - |

## 24. Vegetation

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

## 25. Plants

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
| CELL_M | 25 | m | a field cell's side in metres, for dispersal distances | HT |
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
| BROWSE_LOSS | 0.5 | - | woody cover lost per unit browse eaten | HT: as the old sapling loss |
| BURR_PICK | 0.002 | /step | share of a bank of full hooks caught in a coat per step, x M^(2/3) x (0.3 + fur) | ECO: epizoochory (Sorensen 1986; Couvreur et al. 2004) |
| BURR_DROP | 0.03 | /step | share of seed in a coat that falls off per step | ECO: Couvreur et al. 2004 |
| M_GAPE | 1 | M | body mass whose gape passes a reference seed whole | ECO: gape limits the seed size dispersed (Wheelwright 1985) |

## 26. Fire

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

## 27. Climate

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| CLIMATE | 0.3 | - | default climate strength (Nature.Climate): T = T_REF + Climate x (T_phys - T_REF); 0 = no climate, everywhere T_REF and no heat budget | HT: Revision O; 1 = Earth-like contrasts. Sanity runs at 8,000 steps: 0.6 left 0 and 243 organisms in two branches, 0.3 left 670 and 944, 0 left 728 and 4,106 |
| RAIN_W | 0.7 | - | share of the soil's wetness that follows where rain falls (the rest follows the ground: near water, in valleys) | HT: Revision P; before, soil wetness came from the ground and noise only, and 80% of the land was in the driest two fifths |
| RAIN_L | 30 | cells | cells over land over which wind-borne air loses all but 1/e of its water (1/this rains out per cell) | HT: rain falls off inland over hundreds of km (continental interiors are dry) |
| RAIN_OROG | 40 | - | extra rain-out per unit rise of the ground under the wind (share of the relief) | ECO: orographic rain: windward slopes are wet, lee slopes dry (rain shadows; Roe 2005) |
| RAIN_EVAP | 0.2 | - | how fast air over water takes up water, per cell | HT |
| RAIN_RECYCLE | 0.01 | - | water the land gives back to the air per cell (evapotranspiration) | ECO: about 40% of land rain is recycled from the land (Trenberth 1999) |
| RAIN_DRY | 0.3 | - | rain at the dry belts (near 30° and the poles), against the equator's 1 | ECO: zonal mean rain ~0.3-0.4 of the equator's in the subtropical highs and at the poles (Peixoto & Oort 1992) |
| RAIN_MID | 0.35 | - | extra rain of the mid-latitude belt near 55° | ECO: a second maximum ~0.5-0.6 of the equator's (Peixoto & Oort 1992) |
| RAIN_BELT | 14 | ° | width (degrees of latitude) of the equatorial and mid-latitude rain belts | ECO |
| T_REF | 20 | °C | reference temperature (the temperature of a world with no climate; plant curves, decomposition and evaporation are 1 here) | - |
| T_EQ | 27 | °C | annual mean sea-level temperature at the equator | ECO: observed zonal mean ~27 °C (North 1975) |
| DT_EP | 40 | K | equator-to-pole temperature drop, x sin² latitude | ECO: the second Legendre term of North's (1975) energy-balance climate, ~40 K |
| A_SEAS | 20 | K | seasonal amplitude at the pole in the middle of a continent, x sin latitude x continentality | ECO: continental interiors swing ~40 K winter to summer at high latitude (Conrad 1946) |
| LAPSE | 6.5 | K/km | lapse rate: cooling with height | ECO: ICAO standard atmosphere, 6.5 K per km |
| H_RELIEF | 3000 | m | height of the highest ground above the lakes | HT: rolling hills to low mountains |
| WATER_DAMP | 0.75 | - | share of the seasonal swing that open water takes away at the shore (continentality 1 - WATER_DAMP x exp(-distance / L_MAR)) | ECO: maritime climates swing a quarter of continental ones (Conrad 1946 continentality index) |
| L_MAR | 6 | cells | distance over which the sea's damping of the seasons fades inland | HT: ~25 units, a few hours' walk |
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

## 28. Heat

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

## 29. Memory

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| SENSE_WATER | 8 | cells | farthest an animal senses drinkable water or land: SEEK_WATER and SEEK_LAND point the way only within it, and WATER_CLOSE and LAND_CLOSE fade to 0 at it | HT: 8 cells = 32 units = MAX_SIGHT, the farthest any eye in this world sees, and the range WATER_CLOSE already faded over; cues of distant water (sight, humidity, odour) carry a few body-lengths to a few km, not across a landscape (Dusenbery 1992). Before 2026-10-30 the way to water was a map-wide distance field |
| PI_ERR | 0.2 | - | path-integration error: each step's estimate of its own displacement is off by a random vector of SD PI_ERR x the distance moved (each axis) | ECO: (at BRAIN_START units; see PI_DIFF) odometer and compass errors of path integration add up with the way travelled, so a remembered place drifts as the square root of the distance (Mueller & Wehner 1988; Cheung et al. 2007); HT: a step is hours of travel, so one step is one leg of ~10-20% error |
| PI_DIFF | 1 | units | path-integration drift at rest: a held place diffuses by this SD a step (each axis) in a brain of BRAIN_START units; PI_ERR and PI_DIFF both scale as sqrt(BRAIN_START / units) | ML/ECO: a neural integrator (continuous attractor) diffuses at a rate ~ 1/N neurons (Burak & Fiete 2009); HT: a 24-unit brain loses a place to about its sight range (32) in ~1000 still steps, a 256-unit one in ~10000 |
| MEM_NEAR | 32 | units | distance of a remembered place at which its NEAR sense reads 0.5 (NEAR = 1 / (1 + d / MEM_NEAR); 0 when nothing is remembered) | -: MAX_SIGHT, so NEAR reads 0.5 at the edge of sight |
| FED_WELL | 1 | steps | a meal is remembered as 'fed well' when one bite's energy is at least this many steps' need (FIELD_METAB x basal) | HT: a bite that pays for its step, FOOD_HERE 0.63; a patch richer than the cost of being there is worth coming back to (Charnov 1976) |

## 30. Hydrology

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| RIVER_ACC | 900 | cells | rain-weighted catchment (cells of mean land rain, x map cells / 65,536) above which a land cell is river | HT: was 900 cells of any rain before Revision Q; rivers start where enough rain gathers |
| FLOW_EPS | 1E-06 | elev | least rise per cell across a filled basin or flat (Priority-Flood+epsilon), so every cell drains | -: numerical (Barnes, Lehman & Mulla 2014); far below any real slope |
| ERODE_ITERS | 20 | passes | passes of routing, stream-power incision and deposition at world generation | HT: enough to cut the main valleys; about 15 ms a pass at the default map (1024), 80 ms at 2048 |
| ERODE_K | 0.007 | /pass | stream-power erodibility per pass: dh = -ERODE_K A^ERODE_M S (A in cells of mean rain) | HT: measured at the default map, channels on slopes are cut ~20 m on average and up to ~300 m, hillsides ~6 m (mostly creep) |
| ERODE_M | 0.5 | - | drainage-area exponent of stream power and of carrying capacity (slope exponent 1) | ECO: m/n ~0.5 (Howard 1994; Whipple & Tucker 1999); observed river concavity 0.4-0.6 (Whipple 2004) |
| CREEP_K | 0.01 | /pass | hillslope creep per pass: dh = CREEP_K x (sum of the 4 neighbours' heights - 4 h) | ECO/HT: linear soil creep (Culling 1960; Roering et al. 1999), the hillslope half of every landscape-evolution model; rounds the finest noise over the passes |
| CARRY_K | 0.3 | /pass | carrying capacity: a stream carries at most CARRY_K A^ERODE_M S of sediment per pass | ECO/HT: transport capacity grows with stream power (Willgoose et al. 1991); well above ERODE_K, so steep channels carry what they cut (detachment-limited uplands) and flats drop it; at 0.03 the whole network was transport-limited and channels filled up instead of cutting |
| DEPOSIT_FRAC | 0.3 | - | share of a load above capacity dropped in each cell it passes | HT: a deposition length of a few cells (Davy & Lague 2009) |
| ALLUV_FERT | 0.5 | - | most fertility alluvium adds to a cell's soil (Fert + ALLUV_FERT x (1 - exp(-alluvium / ALLUV_D)), at most 1) | ECO: alluvial soils (fluvisols) are young, fine, mineral-rich and among the most fertile (FAO WRB 2015; the Nile, Indus and Mekong floodplains) |
| ALLUV_D | 0.003 | elev | alluvium depth at which its fertility is 1 - 1/e of ALLUV_FERT | HT: about 3 m (relief H_RELIEF over the elevation range) |
| BASIN_MIN | 0.001 | elev | a land cell at least this far below the rim of the closed basin it lies in is a pond site (a seasonal wetland) | ECO/HT: about 3 m; rain gathers and stands in closed basins (pans, playas, prairie potholes), seasonally where evaporation outpaces it |
| RIVER_D | 0.5 | xSHALLOW | river depth at RIVER_ACC, x SHALLOW (rivers stay wadeable: at most SHALLOW) | HT: 0.5 before Revision Q for every river |
| RIVER_DEPTH_EXP | 0.4 | - | exponent of river depth (and stage) on discharge | ECO: at-a-station and downstream hydraulic geometry, depth ~ Q^0.4 (Leopold & Maddock 1953) |
| BANKFULL | 1.35 | x | discharge, against its mean, at which a river tops its banks (wetness = season x this year's rain) | ECO: bankfull flow recurs every 1-2 years (Leopold, Wolman & Miller 1964; Williams 1978): the wet season of an average year (1 + A_SEASON) just tops it |
| FLOOD_R | 3 | cells | floodplain: cells at most this many flow steps upstream of the river they drain into | HT: floodplains are a few channel widths to tens of km wide |
| FLOOD_HAND | 0.5 | xSHALLOW | floodplain: cells at most this high above that river, x SHALLOW | HT: bounds the search; the stage decides which of them flood |

## 31. Geology

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
| GEO_H_MAX | 5 | km | highest ground uplift can raise: above it a thickened crust spreads under its own weight instead of rising | GEO: orogenic plateaus stand at most ~5 km (Tibet, the Altiplano at ~4); a thicker crust flows apart (Molnar & Lyon-Caen 1988) |
| GEO_REBOUND | 0.82 | - | flexural isostasy: the ground rises by this share of the rock eroded from it (and sinks so under sediment laid on it), spread over GEO_FLEX | GEO: rho_crust / rho_mantle = 2,700 / 3,300; why old mountains outlast their erosion (Molnar & England 1990; Watts 2001) |
| GEO_FLEX | 150 | km | radius over which the lithosphere spreads the isostatic response to a load (two box blurs) | GEO: flexural wavelengths of continental lithosphere are 100-300 km for elastic thicknesses of 10-40 km (Watts 2001) |
| GEO_MYR | 60 | Myr | length of the deep time over which uplift and erosion work the land | GEO: active orogens come near a flux steady state in a few to ~10 Myr (Willett & Brandon 2002); this is several times that |
| GEO_ITERS | 200 | steps | steps of uplift, stream power, transport and diffusion over GEO_MYR (on the coarse grid, FW / 2) | HT: 0.3 Myr a step; the incision is implicit, so the step is stable at any length |
| GEO_RAIN_EVERY | 25 | steps | steps between re-reading where rain falls on the growing relief (Climate.Rainfall) | HT: rain follows the relief (orographic rain, rain shadows), which changes slowly |
| GEO_PSI | 0.3 | km | erosion threshold: bedrock is cut only where sqrt(A) S exceeds this (A in km2) | GEO: channels incise only above a critical shear stress (Tucker 2004; Lague, Hovius & Davy 2005); a channel draining 10,000 km2 on a slope of 3 in 1,000; low plains stop wearing down and keep their cover (without it, 60 Myr stripped the platform cover from all but the river beds, and 75% of the land was bare basement) |
| GEO_K_SED | 1 | /Myr | stream-power erodibility of soft sedimentary rock: dh/dt = -K sqrt(A) S (A in km2) | GEO: K spans ~0.1-100 /Myr for m = 0.5 from crystalline to weak sedimentary and volcaniclastic rock (Stock & Montgomery 1999) |
| GEO_K_VOLC | 0.5 | /Myr | stream-power erodibility of volcanic rock | GEO: lavas, tuffs and breccias: between basement and soft strata (Stock & Montgomery 1999) |
| GEO_K_CRYST | 0.25 | /Myr | stream-power erodibility of crystalline rock (granite, gneiss) | GEO: the hardest (Stock & Montgomery 1999); erodibility falls with tensile strength squared (Sklar & Dietrich 2001); the contrast is narrowed at cells tens of km wide, where jointing and weathering govern |
| GEO_DIFF | 50 | km2/Myr | hillslope diffusivity on the coarse grid (sub-grid landsliding and creep) | HT: an effective diffusivity for cells tens of km wide, far above soil creep (~0.01 km2/Myr; Fernandes & Dietrich 1997) as in large-scale landscape models (Tucker & Bras 1998) |
| GEO_SED_MIN | 0.02 | km | sediment thicker than this makes a cell's surface rock sedimentary | HT: 20 m of strata hides its bedrock |
| GEO_ROUGH | 0.06 | - | sub-grid roughness: relief within a coarse cell, as a share of the relief between it and its neighbours | GEO/HT: topography is about self-similar over these scales (Turcotte 1997); roughness on top of the worked relief, not the relief |
| FERT_CRYST | 0.4 | - | base fertility of soil on crystalline rock | ECO: granite and gneiss weather to acid, base-poor soils (FAO WRB 2015) |
| FERT_VOLC | 0.85 | - | base fertility of soil on volcanic rock | ECO: volcanic ash and basalt weather to andosols, among the most fertile soils (Shoji, Nanzyo & Dahlgren 1993) |
| FERT_SED | 0.6 | - | base fertility of soil on sedimentary rock | ECO: shales and limestones weather to moderately rich soils, sandstones to poor ones; their mean |
| FERT_VAR | 0.15 | - | local variation of fertility about its parent material (±, from organic matter, drainage and soil age) | HT: was the whole fertility (noise) before Revision R |

## 32. Hydrology

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| FLOOD_MIX | 0.3 | /update | share of the difference between the flooding river's nutrient and a flooded cell's that the flood leaves on the soil, at each water-cycle update (WC_EVERY steps); taken from the river | ECO/HT: the flood pulse brings river nutrients and silt onto the floodplain each year (Junk, Bayley & Sparks 1989; the Nile's ~1 mm of silt a year, Said 1993); 0.5 per 100 steps before the water cycle, the same rate per step at 50 |

## 33. Water cycle

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| WC_EVERY | 50 | steps | steps between water-cycle updates (rain, snow, soil, groundwater, routing, ponds, lakes, drinkable water) | HT: 60 updates a year at T_SEASON (about 6 days each at 365 days a year); a step is ~3 hours, so the stores change little between updates |
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

## 34. Organisms

There are no designed species (Revision F). Life starts from 10 land and 6 water ancestors with random bodies (Clades.AncestorBody), and every organism's body is expressed from its own genes on the ranges above (DESIGN §6.3).
