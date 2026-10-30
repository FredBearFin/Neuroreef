# NEUROREEF 2 — CONSTANTS TABLE

Document NR-CT-001, Revision B. **Generated** from `src/Neuroreef.Core/Constants.cs` and `Species.cs` by
`Neuroreef.exe --print-constants`; do not edit by hand. A test fails if this file and the code disagree (NR-106).

Origin codes: **V1** taken from Neuroreef v1; **ECO** ecological rule or exponent from the literature;
**HT** hand-tuned in headless runs; **-** derived.


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
| RIVER_ACC | 900 | cells | catchment (cells) above which a cell is river | HT |
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

## 4. Disease

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| MHC_W | 0.35 | - | match width: a strain infects a host whose recognition genes are within this RMS distance (/127) of its key | ECO: matching-alleles host-parasite genetics (Hamilton 1980; Lively 2010): rare host genotypes escape, which can favour sex |
| MHC_MUT | 6 | int8 | mutation of a strain's key per transmission (SD) | ECO: pathogens evolve far faster than their hosts |

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
| FLY_SPEED | 1.4 | - | speed factor for flyers | HT |
| FLY_COST | 0.5 | - | movement cost factor for flyers (per distance, against running) | ECO: flying costs less per distance than running at equal mass (Schmidt-Nielsen 1972); wing upkeep is in basal cost |
| DRINK_RATE | 0.25 | /step | drinking: thirst removed per step | HT |
| GROWTH_EFF | 0.75 | - | efficiency of turning energy into new tissue (growth, offspring) | ECO: net growth efficiency 0.6-0.8 |
| GROW_RESERVE | 0.2 | xEmax | a juvenile keeps this share of its reserve against starving; above it, it grows at its top rate times how full the reserve is | ECO: growth draws on the reserve in proportion to its density (dynamic energy budget, Kooijman 2010); hungry young grow slowly (Revision D; Revision P: was a threshold of 0.5, below which the young, living near 40% full, hardly grew) |

## 7. Food

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
| MUT_RATE | 1.03677E-05 | - | mutation probability per brain or reflex byte (1/LEN: about one mutation per offspring) | ECO: on the order of one mutation per genome per generation in functional sequence (Drake 1998; Lynch 2010); was 0.02 (~39 per offspring, mutational meltdown) |
| MUT_BODY | 0.01 | - | mutation probability per body gene per birth (about 0.4 body changes per newborn) | ECO: each body gene stands for a polygenic trait of many loci; mutational heritability ~1e-3 per trait per generation (Lynch & Walsh 1998), so a trait changes far more often than one brain byte |
| FOUNDER_BODY_SD | 12 | int8 | standing variation of founders' body genes around their ancestor (SD) | ECO: morphological traits vary with a CV of about 5-10% in natural populations (Houle 1992) |
| MUT_SMALL | 2 | int8 | small mutation sigma | HT |
| MUT_BIG_SHARE | 0.05 | - | share of mutations that are large jumps | V1 |
| MUT_BIG | 16 | int8 | large mutation sigma | V1 |
| RSCALE | 0.0625 | - | reflex input weight scale (int8 / 16, as the output layer) | - |
| SATIETY | 0.2 | xEmax | appetite is full while the reserve is more than this share below full (hunger above it), and fades to none as it tops up | ECO: voluntary intake is limited by gut fill and energy status, not by energy deficit alone (Forbes 2007); measured 2026-10-30: adults on food ate on 47% of steps and sat at 20-60% reserve, so females were ready to breed 12% of the time |
| KIT_W | 120 | int8 | founder strength of each starter-kit reflex (x/16): at full need, 120/16 = 7.5 against the calm bias of -4 gives about 97% | designed: the innate starter kit (DESIGN §7.7, Revision D); the user (2026-10-30): the base instincts should be very strong, "like primary colours" (was 80, 88% against a bias of -3) |
| ARCHIVE | 16 | genomes | elite archive size per species | HT (Revision M: 64 before; a genome is now ~83 KB) |
| SCORE_MATURED | 3 | - | archive score weight per offspring that reached maturity | HT |

## 10. Prehistory

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| PREHISTORY_STEPS | 500000 | steps | length of a prehistory run | HT |
| BRANCHES | 2 | branches | number of separate branches (populations on the same land) evolving side by side | HT (Revision M: 3 before; memory) |
| YEARS_PER_STEP | 1000 | years | symbolic time shown in the console: years per step | - |
| BRANCH_CAPACITY | 16384 | slots | agent slots per branch | HT: Revision M, 2 x 16,384 slots of 256-unit brains is ~5 GB (the report warns if the cap binds) |

## 11. Brain

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| GROUP_SIZE | 8 | agents | founders start in groups of this size at one site | ECO: populations are clustered; lone colonists cannot find mates (Allee effect) |
| GROUP_SPREAD | 10 | units | radius of a founder group | HT |
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

## 13. Voice

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| MAX_SYL | 3 | sounds | syllables in one utterance at most (distinct sounds, in the order of how strongly each is intended) | ECO: animal calls combine a few units into sequences, rarely more (Engesser & Townsend 2019) |
| CALL_SYL | 0.5 | - | share of a call's cost that grows with each syllable: an utterance of n syllables costs C_CALL x basal x (1 - CALL_SYL + CALL_SYL x n) | ECO: a longer song costs more, but much of a call's cost is the start-up (Oberweger & Goller 2001; Ophir et al. 2010) |
| HEAR_R | 16 | units | how far a full voice of a body of M=1 carries (range = HEAR_R x voice x M^(1/3), at most MAX_SIGHT) | ECO: bigger animals call louder and are heard further (Fletcher 2004; Wiley & Richards 1978); about the sight range at M=1 (C_SIGHT) |
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
| POND_GAP | 6 | cells | pond sites are at least this far from a lake, sea or river | HT: a pond beside a lake adds nothing |
| POND_FILL | 1 | x | a pond holds water while its cell is this much wetter than its average (season x this year's rain) | ECO: ephemeral pools fill in the wet season and dry in the dry season and in drought years, in wet and dry country alike |
| POND_EVERY | 100 | steps | steps between pond updates | HT: ponds change over a season, so a few times a season is enough |

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

## 19. Clades

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

## 20. Reward

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| R_SPEND | 1 | - | energy spent on its choices (strikes, calls, moving, heat) counts against the reward at this share of the weight energy eaten counts for it | ECO: animals learn by net energy gain (optimal foraging; Stephens & Krebs 1986); 1 = a unit spent weighs as a unit eaten. 2026-10-30: without it, calling (19-24% of basal) rose from ~38% to ~65% of steps by learning |

## 21. Reproduction

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| READY_LO | 0.25 | xEmax | lowest breeding condition a body can have: it breeds once its reserve is above this share (B_BREEDE gene) | ECO: income breeders breed on what they eat as they go, capital breeders on stored reserves (Jonsson 1997; Stephens et al. 2009); was the fixed READY_ENERGY = 0.6 until 2026-10-30 |
| READY_HI | 0.9 | xEmax | highest breeding condition a body can have | ECO: as READY_LO |
| F_BIRTH | 0.4 | xEmax child | newborn energy (paid by parent at conception) | V1 |
| C_MATE | 0.02 | E/M | mating cost per partner | HT |
| REFRACT | 0.1 | - | a male's refractory period after mating, as a share of the breeding cooldown | ECO: male refractory periods are short against female breeding cycles (Bateman 1948; Trivers 1972) |

## 22. Care

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| CARE_RATE | 1.5 | xbasal | milk or chick-feeding energy a mother can give per step, as a multiple of her basal cost (shared by all her young) | ECO: peak lactation milk energy is about 1-2x maternal basal metabolic rate (Hanwell & Peaker 1977) |
| CARE_EFF | 0.7 | - | efficiency of turning maternal energy into milk or food for the young | ECO: milk synthesis efficiency 0.6-0.8 |
| WEAN | 0.4 | - | care ends (weaning) when the young has built this share of its adult tissue | ECO: mammals wean at roughly a third to a half of adult mass |
| CARE_RESERVE | 0.4 | xEmax | a mother keeps at least this share of her energy reserve for herself | ECO: females in poor condition reduce or stop provisioning |

## 23. Disease

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| DIS_BETA | 0.01 | /contact/step | probability per step of infection per infected conspecific in contact | ECO: with the infectious period below, R0 = 0.5 per steady contact: outbreaks need crowding (density-dependent transmission) |
| DIS_STEPS | 50 | steps | infectious period | ECO: about 1.5% of the lifespan at M=1 (a 1-2 week infection in a 2-year life) |
| DIS_IMMUNE | 600 | steps | immunity after recovery | ECO: acquired immunity lasts months (about 20% of the lifespan at M=1) |
| DIS_SPILL | 1E-05 | /step | spillover from the environment (reservoir) per animal per step | ECO: rare sporadic infections seed outbreaks |
| VIR_MUT | 0.1 | - | spread of a pathogen's virulence at each transmission (log-normal) | ECO: pathogens mutate as they spread; virulence evolves (Anderson & May 1982) |
| DIS_COST | 0.4 | xbasal | extra basal cost while infected, per unit of virulence (fever, immune response) | ECO: fever raises metabolic rate ~10-13% per degree C; sickness costs 25-50% |
| DIS_MORT | 0.002 | /step | probability per step of dying while infected (x(1 + hunger)) | ECO: about 10% case fatality over the infectious period |
| DIS_PAIN | 0.3 | - | malaise felt while infected (as pain) | ECO: sickness behaviour |

## 24. Weather

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| RAIN_SD | 0.25 | - | standard deviation of the yearly rainfall anomaly | ECO: interannual rainfall CV 20-35% in semi-arid regions |
| RAIN_RHO | 0.4 | - | persistence of the rainfall anomaly from one year to the next (AR(1)) | ECO: droughts cluster in runs of years |
| RAIN_MAX | 0.7 | - | limit of the rainfall anomaly | - |

## 25. Vegetation

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| R_G | 0.01 | /step | grass growth | V1 |
| W_SHRUB | 0.2 | - | soil moisture below which shrubs cannot grow (grass has none) | ECO: woody plants need wetter soils than grasses; savanna woody cover is capped by rainfall (Sankaran et al. 2005) |
| W_TREE | 0.35 | - | soil moisture below which trees and saplings cannot grow | ECO: as W_SHRUB; trees need the most water |
| GRASS_COMP | 0.7 | - | share of a sapling's water taken by grass roots at full grass cover | ECO: Walter's two-layer hypothesis: grass roots take the topsoil water tree seedlings need |
| DROUGHT_T | 5 | x D_T | extra tree mortality at zero moisture (drought die-off), scaled by the shortfall below W_TREE | ECO: drought years kill trees |
| WIND_SEED | 0.0004 | /step | long-distance (wind) seed rain: sapling establishment per unit of the world's mean tree cover | ECO: rare long-distance dispersal lets trees reach new ground |
| SIGMA_G | 0.06 | - | grass spread from neighbours | V1 |
| S_G | 0.05 | - | grass establishment from seed | HT |
| G_RAIN | 0.002 | - | grass background seed rain | HT |
| R_S | 0.003 | /step | shrub growth | ECO: shrub cover e-folds in ~1/4 year (year = T_SEASON) |
| S_S | 0.01 | - | shrub invasion of grass | HT |
| R_P | 0.0008 | /step | sapling recruitment | HT |
| M_P | 0.001 | /step | sapling maturation | HT |
| R_T | 0.0004 | /step | tree growth | HT |
| D_T | 6E-05 | /step | tree mortality | HT |
| SH_G | 0.0003 | /step | canopy shading of grass | HT |
| SH_S | 0.0002 | /step | canopy shading of shrubs | HT |
| K_N | 0.3 | - | nutrient half-saturation | HT |
| R_B | 0.004 | /step | browse regrowth toward capacity | HT |
| B_TREE | 0.6 | - | browse capacity per unit tree cover | HT |
| R_F | 0.0025 | /step | fruit production | HT |
| D_F | 0.004 | /step | fruit rot | HT |
| R_SD | 0.0015 | /step | seed production | HT |
| D_SD | 0.001 | /step | seed decay | HT |
| R_NC | 0.004 | /step | nectar production | HT |
| D_NC | 0.01 | /step | nectar decay | HT |
| R_R | 0.0015 | /step | root growth | HT |
| C_LIT | 0.0004 | /step | leaf senescence into litter | HT |
| POLL_GAIN | 8 | - | pollination increment per unit nectar eaten | HT |
| D_POLL | 0.005 | /step | pollination decay | HT |
| DUNG_SAPLING | 0.5 | - | saplings planted per unit seed share in dung | HT |
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

## 26. Fire

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| LAMBDA_FIRE | 2E-06 | /cell/step | ignition rate per unit dry fuel per land cell | HT: ~1 ignition per 100 steps in the dry season |
| C_SPREAD | 0.15 | - | spread probability per unit neighbour fuel | ECO: gives a fire return interval of ~2 years with no animals (savanna: 1-5 years) |
| C_ASH | 0.5 | - | nutrient from burned biomass | HT |
| FIRE_STEPS | 6 | steps | steps a cell burns | HT |
| RESPROUT | 0.25 | - | share of grass and shrub cover that survives fire as rootstock and resprouts | ECO: savanna grasses and shrubs resprout |
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

## 29. Organisms

There are no designed species (Revision F). Life starts from 10 land and 6 water ancestors with random bodies (Clades.AncestorBody), and every organism's body is expressed from its own genes on the ranges above (DESIGN §6.3).
