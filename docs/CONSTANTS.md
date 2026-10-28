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
| CAPACITY | 65536 | slots | default agent slots (animals, eggs, embryos) | HT |
| LAKE_SHARE | 0.1 | fraction | share of cells below lake level | HT |
| RIVER_ACC | 900 | cells | catchment (cells) above which a cell is river | HT |
| SHALLOW | 0.035 | elev | depth below which water is shallow (wadeable, algae grow) | HT |
| T_SEASON | 9000 | steps | season period | V1 |
| A_SEASON | 0.45 | - | season amplitude on moisture | V1 |
| SAMPLE_EVERY | 32 | steps | telemetry sample interval | HT |
| RING | 4096 | samples | telemetry ring length | HT |
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
| C_BASAL | 0.002 | E/step | basal cost at M=1 | V1 |
| C_EMAX | 2 | E/M | energy reserve per unit mass | ECO: stored reserve of the same order as tissue energy |
| C_GUT | 0.3 | M/M | gut capacity per unit mass | HT |
| C_DIGEST | 0.005 | M/step | digestion rate at M=1 | HT |
| C_BITE | 0.012 | M/step | bite size at M=1 | V1 |
| C_SPEED | 1.2 | units/step | max speed at M=1 (walk) | V1 |
| C_TURN | 0.32 | rad/step | turn rate at M=1 | V1 |
| C_LIFE | 3200 | steps | lifespan at M=1 | V1 |
| C_MATURE | 250 | steps | maturity age at M=1 | V1 |
| C_SIGHT | 16 | units | sight range at M=1 | V1 |
| C_MOVE | 0.002 | E/(M*v^2) | movement cost | V1 |
| C_THIRST | 0.0012 | /step | thirst rise per step (death after ~830 dry steps) | HT |
| C_FATIGUE | 0.012 | /step | fatigue gain at full sprint | HT |
| C_RECOVER | 0.004 | /step | fatigue recovery (x3 resting) | HT |
| REST_BASAL | 0.85 | - | basal multiplier while resting | HT |
| SWIM_SPEED | 0.85 | - | speed factor for swimmers | HT |
| FLY_SPEED | 1.4 | - | speed factor for flyers | HT |
| FLY_COST | 1.6 | - | movement cost factor for flyers | HT |
| DRINK_RATE | 0.25 | /step | drinking: thirst removed per step | HT |
| GROWTH_EFF | 0.75 | - | efficiency of turning energy into new tissue (growth, offspring) | ECO: net growth efficiency 0.6-0.8 |
| GROW_RESERVE | 0.5 | xEmax | a juvenile grows only from energy above this share of its reserve | ECO: growth is fuelled by surplus after maintenance; hungry young grow slowly (Revision D) |

## 3. Food

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

## 4. Hunting

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| P0_STRIKE | 0.45 | - | base strike success | HT |
| C_CONF | 0.25 | - | prey-group confusion | V1 |
| C_STRIKE | 0.004 | E/M | cost of a strike | HT |
| C_RETAL | 0.06 | E | retaliation per unit of excess defence | HT |
| C_CARC | 0.5 | M/M | edible tissue (carcass) per unit body mass; also the tissue built in growth | ECO: about half of live mass is edible tissue |
| JUV_DEFENCE | 0.25 | - | defence multiplier for juveniles | HT |
| C_DRAIN | 0.05 | E | parasite drain per strike at M=1 | HT |
| PAIN_HIT | 0.6 | - | pain added by a failed strike received | HT |
| PAIN_DECAY | 0.03 | /step | pain decay per step | HT |

## 5. Brain

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| WSCALE | 0.0625 | - | genome weight scale (int8 / 16) | - |
| WMAX | 7.9 | - | max |weight| (genome + learned) | - |
| FOUNDER_W | 12 | int8 | founder memory-layer (W1) weight range: round(U(-1,1)*FOUNDER_W) | HT |
| FOUNDER_W2 | 4 | int8 | founder output-layer (W2) weight range | HT |
| FOUNDER_BIAS | -48 | int8 | founder output bias (x/16): every primitive of every species starts near 5% (calm newborn brain) | HT |
| FOUNDER_BIAS_NOISE | 4 | int8 | random spread of the founder output bias | HT |
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
| MUT_RATE | 0.000468604 | - | mutation probability per genome byte (1/LEN: about one mutation per offspring) | ECO: on the order of one mutation per genome per generation in functional sequence (Drake 1998; Lynch 2010); was 0.02 (~39 per offspring, mutational meltdown) |
| MUT_SMALL | 2 | int8 | small mutation sigma | HT |
| MUT_BIG_SHARE | 0.05 | - | share of mutations that are large jumps | V1 |
| MUT_BIG | 16 | int8 | large mutation sigma | V1 |
| RSCALE | 0.0625 | - | reflex input weight scale (int8 / 16, as the output layer) | - |
| KIT_W | 80 | int8 | founder strength of each starter-kit reflex (x/16): at full need, 80/16 = 5 against the calm bias of -3 gives about 88% | designed: the innate starter kit (DESIGN §7.7, Revision D) |
| ARCHIVE | 64 | genomes | elite archive size per species | HT |
| SCORE_MATURED | 3 | - | archive score weight per offspring that reached maturity | HT |

## 6. Prehistory

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| PREHISTORY_STEPS | 500000 | steps | length of a prehistory run | HT |
| BRANCHES | 3 | branches | number of separate branches (populations on the same land) evolving side by side | HT |
| YEARS_PER_STEP | 1000 | years | symbolic time shown in the console: years per step | - |
| BRANCH_CAPACITY | 24576 | slots | agent slots per branch | HT |

## 7. Brain

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| GROUP_SIZE | 8 | agents | founders start in groups of this size at one site | ECO: populations are clustered; lone colonists cannot find mates (Allee effect) |
| GROUP_SPREAD | 10 | units | radius of a founder group | HT |
| BANK_LIVING | 96 | genomes | genomes per species stored in a gene bank (living sample) | HT |

## 8. Body

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| BODY_RANGE_TRAIT | 1.6 | - | range of the speed and sight multipliers (x/÷) | HT |
| COST_SPEED | 0.4 | - | basal cost per unit of extra speed | HT |
| COST_SIGHT | 0.15 | - | basal cost per unit of extra sight | HT |
| ARMOUR_DRAG | 0.2 | - | speed loss per unit of extra armour | HT |
| MASS_LO | 0.05 | M | smallest body mass a mass gene can give | ECO: a small invertebrate-sized organism |
| MASS_HI | 40 | M | largest body mass a mass gene can give | ECO: a large mammal-sized organism (relative units) |
| LIMB_MIN | 0.3 | - | organ size (0..1) below which an organ is vestigial (for describing and drawing bodies; speeds are smooth) | - |
| COST_LIMB | 0.15 | xbasal | basal cost per unit of fin size, or of leg size per pair of legs | ECO: locomotor muscle is maintained at a cost |
| COST_WING | 0.5 | xbasal | basal cost per unit of wing size, times mass^(1/3) | ECO: flight power rises faster than mass, so flight favours the small |
| COST_ARMOUR_ABS | 0.12 | xbasal | basal cost per unit of armour | ECO: heavy armour is costly to carry and grow |
| COST_STRIKE_ABS | 0.15 | xbasal | basal cost per unit of weaponry | ECO: jaws, teeth and claws are costly tissue |
| ARMOUR_MAX | 3 | - | largest armour a gene can give | - |
| STRIKE_MAX | 2 | - | largest weaponry (strike) a gene can give | - |
| DIET_BUDGET | 1 | - | sum of digestive efficiencies an organism can have (a generalist spreads it, a specialist concentrates it) | ECO: digestive specialisation trades off breadth against efficiency |
| LITTER_MAX | 12 | young | largest litter a gene can give | - |
| BIRTH_LO | 0.01 | - | smallest newborn mass as a share of adult mass | ECO: fish and insect eggs |
| BIRTH_HI | 0.3 | - | largest newborn mass as a share of adult mass | ECO: precocial mammals |
| FOV_LO | 120 | degrees | narrowest field of view a gene can give | ECO: forward-facing predators |
| FOV_HI | 360 | degrees | widest field of view a gene can give | ECO: prey with lateral eyes |
| C_GEST | 150 | steps | gestation (or incubation) at M=1, scaling with M^0.25 | ECO: gestation scales about as M^0.25 |
| C_COOL | 150 | steps | breeding cooldown at M=1, scaling with M^0.25 | ECO: as gestation |
| WING_LIFT | 1.6 | - | a body can fly when wing size x WING_LIFT reaches mass^(1/3) (wing loading) | ECO: lift scales with wing area, weight with volume, so large flyers need relatively bigger wings |
| DEEP_SWIM | 0.3 | - | swimming power (share of the body's top speed in water) needed to stay in deep water | ECO: staying up in open water needs swimming power |
| CRAWL | 0.05 | - | a legless body crawls on land at this share of its top speed | ECO: worms, snakes and stranded fish move slowly without legs |
| SOIL_WATER | 0.8 | - | share of thirst a land organism avoids by taking water from wet soil and moist food | ECO: many animals and all settled organisms take water from their surroundings |
| COST_EYE | 0.04 | xbasal | basal cost per eye pair | ECO: eyes and visual brain are costly tissue |
| EYE_FOV | 0.3 | - | field of view added by each extra eye pair (share of the base field) | ECO: more eyes cover more directions |
| FIN_TURN | 0.2 | - | turning added per extra fin pair (times fin size) | ECO: paired fins steer and brake |
| LIFE_RANGE | 2 | - | range of the lifespan and development multipliers (x/÷) | - |
| COST_LONGEVITY | 0.25 | xbasal | basal cost per unit of extra lifespan (longer life needs more body repair) | ECO: disposable-soma theory: longevity costs somatic maintenance (Kirkwood 1977) |
| DEVEL_AGING | 0.5 | - | lifespan scales with the development multiplier to this power (fast developers age sooner) | ECO: the fast-slow life-history continuum |
| BRAIN_MIN | 2 | units | fewest memory units a brain can have | - |
| COST_BRAIN | 0.15 | xbasal | basal cost of a full brain (all memory units), scaled by units in use | ECO: brain tissue costs about 2-20% of basal metabolism across vertebrates |
| COST_HERM | 0.05 | xbasal | basal cost of being a hermaphrodite (keeping both sexes' organs) | ECO: hermaphrodites maintain male and female function |
| COST_IMMUNE | 0.12 | xbasal | basal cost of full immune investment | ECO: immune defence is costly (Lochmiller & Deerenberg 2000) |
| IMMUNE_PROTECT | 0.8 | - | share of infection and disease death that full immune investment prevents | ECO: partial, not total, protection |
| D_COMPAT | 0.3 | - | genetic distance at which mating succeeds with probability 1/e (body genes / 127, Euclidean) | ECO: hybrid incompatibility rises with genetic distance (Dobzhansky-Muller; Orr 1995) |
| KIN_LOOK | 0.25 | - | others whose visible body genes are this close look like kin (body genes / 127, Euclidean) | ECO: animals recognise their kind by appearance |

## 9. Clades

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| CLADES | 32 | clades | clade slots (lineages that can exist at once) | -: limited by the console frame (5 bits) |
| ANCESTORS_LAND | 6 | ancestors | ancestors that start on land | user choice |
| ANCESTORS_WATER | 4 | ancestors | ancestors that start in water | user choice |
| ANCESTOR_COUNT | 600 | organisms | founding organisms per ancestor | HT |
| SPEC_EVERY | 2000 | steps | steps between speciation checks | - |
| MIN_SPLIT | 40 | organisms | smallest cluster that can become a species | ECO: a viable population |
| SPLIT_SEP | 1.5 | - | two gene clusters are separate species when their centres are this many times their spread apart | ECO: genotypic-cluster species (Mallet 1995): two clusters with few intermediates |
| SPLIT_MIN_D | 0.25 | - | and at least this far apart (body genes / 127, Euclidean) | - |
| CLADE_RECYCLE | 20000 | steps | an extinct clade's slot is reused only after this many steps | - |

## 10. Reproduction

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| READY_ENERGY | 0.6 | xEmax | energy needed to be READY | HT |
| F_BIRTH | 0.4 | xEmax child | newborn energy (paid by parent at conception) | V1 |
| C_MATE | 0.02 | E/M | mating cost per partner | HT |

## 11. Care

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| CARE_RATE | 1.5 | xbasal | milk or chick-feeding energy a mother can give per step, as a multiple of her basal cost (shared by all her young) | ECO: peak lactation milk energy is about 1-2x maternal basal metabolic rate (Hanwell & Peaker 1977) |
| CARE_EFF | 0.7 | - | efficiency of turning maternal energy into milk or food for the young | ECO: milk synthesis efficiency 0.6-0.8 |
| WEAN | 0.4 | - | care ends (weaning) when the young has built this share of its adult tissue | ECO: mammals wean at roughly a third to a half of adult mass |
| CARE_RESERVE | 0.4 | xEmax | a mother keeps at least this share of her energy reserve for herself | ECO: females in poor condition reduce or stop provisioning |

## 12. Disease

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

## 13. Weather

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| RAIN_SD | 0.25 | - | standard deviation of the yearly rainfall anomaly | ECO: interannual rainfall CV 20-35% in semi-arid regions |
| RAIN_RHO | 0.4 | - | persistence of the rainfall anomaly from one year to the next (AR(1)) | ECO: droughts cluster in runs of years |
| RAIN_MAX | 0.7 | - | limit of the rainfall anomaly | - |

## 14. Vegetation

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

## 15. Fire

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

## 16. Organisms

There are no designed species (Revision F). Life starts from 6 land and 4 water ancestors with random bodies (Clades.AncestorBody), and every organism's body is expressed from its own genes on the ranges above (DESIGN §6.3).
