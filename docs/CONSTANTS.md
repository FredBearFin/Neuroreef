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
| SWARM_CHUNK | 0.3 | - | share of a swarm's energy one strike removes (max) | HT |
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
| MUT_RATE | 0.02 | - | mutation probability per genome byte | V1 0.05, lowered |
| MUT_SMALL | 2 | int8 | small mutation sigma | HT |
| MUT_BIG_SHARE | 0.05 | - | share of mutations that are large jumps | V1 |
| MUT_BIG | 16 | int8 | large mutation sigma | V1 |
| ARCHIVE | 64 | genomes | elite archive size per species | HT |
| ARCHIVE_SHARE | 0.9 | - | share of colonists bred from the archive once it holds 4 genomes (the rest are random brains) | HT |
| TOURNAMENT | 3 | genomes | tournament size when choosing a colonist parent from the archive | HT |
| SCORE_MATURED | 3 | - | archive score weight per offspring that reached maturity | HT |
| GENESIS | 300000 | steps | genesis period (immigration floor ramps to 0) | HT |
| GENESIS_FLOOR | 0.25 | - | genesis floor as share of initial count | HT |
| GROUP_SIZE | 8 | agents | founders and colonists arrive in groups of this size at one site | ECO: populations are clustered; lone colonists cannot find mates (Allee effect) |
| GROUP_SPREAD | 10 | units | radius of a founder or colonist group | HT |
| BANK_LIVING | 96 | genomes | genomes per species stored in a gene bank (living sample) | HT |

## 6. Body

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| BODY_RANGE_MASS | 2 | - | range of the mass multiplier (x/÷) | HT |
| BODY_RANGE_TRAIT | 1.6 | - | range of the speed, sight, armour, strike and litter multipliers (x/÷) | HT |
| BODY_RANGE_DIET | 3 | - | range of a diet efficiency multiplier (x/÷), before the budget is renormalised | HT |
| DIET_NOVEL | 0.15 | - | efficiency a pool the founder cannot digest can gain at the gene's maximum | HT |
| COST_SPEED | 0.4 | - | basal cost per unit of extra speed | HT |
| COST_SIGHT | 0.15 | - | basal cost per unit of extra sight | HT |
| COST_ARMOUR | 0.2 | - | basal cost per unit of extra armour | HT |
| COST_STRIKE | 0.2 | - | basal cost per unit of extra strike | HT |
| ARMOUR_DRAG | 0.2 | - | speed loss per unit of extra armour | HT |

## 7. Reproduction

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| READY_ENERGY | 0.6 | xEmax | energy needed to be READY | HT |
| F_BIRTH | 0.4 | xEmax child | newborn energy (paid by parent at conception) | V1 |
| C_MATE | 0.02 | E/M | mating cost per partner | HT |
| FISSION_SHARE | 0.45 | - | fission: share of energy the child takes | HT |
| FACULT_FOOD | 0.5 | - | facultative: FOOD_HERE above which reproduction is asexual | HT |
| RESTING_MAX | 6000 | steps | cyclic: max dormancy of resting eggs | HT |
| SPORE_RADIUS | 3 | cells | spores: search radius | HT |

## 8. Vegetation

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| R_G | 0.01 | /step | grass growth | V1 |
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

## 9. Fire

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

## 10. Species body plans

Derived values follow the allometry rules of DESIGN §9.1. S = super-individual.

| # | Species | Niche | Mass | Unit mass | Locomotion | Armour | Strike | Mode | Eggs | Litter | Gestation | Cooldown | Initial |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 0 | Drifters | small grazer | 1 | 1 | Walk | 0.3 | 0 | Sexual | Live | 2 | 150 | 150 | 2500 |
| 1 | Browsers | large browser | 18 | 18 | Walk | 1.6 | 0.6 | Sexual | Live | 1 | 600 | 600 | 250 |
| 2 | Tuskers | rooting omnivore | 6 | 6 | Walk | 1 | 0.7 | Sexual | Live | 4 | 300 | 300 | 300 |
| 3 | Gliders | frugivore, seed disperser | 0.6 | 0.6 | Fly | 0.2 | 0 | Sexual | LandEgg | 3 | 200 | 200 | 600 |
| 4 | Burrowers | granivore, cacher | 0.4 | 0.4 | Walk | 0.2 | 0 | Sexual | Live | 6 | 120 | 120 | 1200 |
| 5 | Hummers (S) | pollinator swarm | 0.8 | 0.01 | Fly | 0.5 | 0 | Fission | Live | 1 | 0 | 200 | 600 |
| 6 | Chewers (S) | leaf-insect swarm | 1 | 0.01 | Fly | 0.5 | 0 | Facultative | Live | 2 | 60 | 100 | 1200 |
| 7 | Delvers (S) | detritivore (worms, beetles) | 1.2 | 0.01 | Walk | 0.5 | 0 | Hermaphrodite | LandEgg | 2 | 150 | 150 | 1200 |
| 8 | Rotmats (S) | fungal mat (sessile) | 1 | 0.05 | Sessile | 2 | 0 | Spores | LandEgg | 1 | 300 | 300 | 500 |
| 9 | Gleaners | vertebrate scavenger | 2 | 2 | Walk | 0.4 | 0.3 | Sexual | Live | 6 | 200 | 250 | 300 |
| 10 | Snappers | insectivore | 0.35 | 0.35 | Fly | 0.2 | 0.8 | Sexual | LandEgg | 4 | 150 | 200 | 500 |
| 11 | Prowlers | solitary mesopredator | 3 | 3 | Walk | 0.5 | 1 | Sexual | Live | 3 | 250 | 300 | 200 |
| 12 | Stalkers | pack hunter | 4 | 4 | Walk | 0.5 | 1 | Sexual | Live | 3 | 300 | 400 | 200 |
| 13 | Leviathans | apex predator | 25 | 25 | Wade | 1 | 1.3 | Sexual | Live | 1 | 900 | 1000 | 40 |
| 14 | Bloodflies (S) | parasite swarm (aquatic larvae) | 0.5 | 0.005 | Swim → Fly | 0.3 | 0.6 | Sexual | WaterEgg | 6 | 100 | 150 | 600 |
| 15 | Siltmites (S) | zooplankton | 1.5 | 0.002 | Swim | 0.2 | 0 | Cyclic | WaterEgg | 2 | 40 | 60 | 1500 |
| 16 | Minnows | grazing fish | 0.4 | 0.4 | Swim | 0.3 | 0.4 | Sexual | WaterEgg | 8 | 100 | 150 | 1200 |
| 17 | Pikes | predatory fish | 3 | 3 | Swim | 0.6 | 1 | Sexual | WaterEgg | 4 | 200 | 300 | 150 |
| 18 | Croakers | amphibian (tadpole to frog) | 0.6 | 0.6 | Swim → Wade | 0.3 | 0.6 | Sexual | WaterEgg | 6 | 120 | 200 | 500 |
| 19 | Herons | wading bird | 2.5 | 2.5 | Fly | 0.4 | 1 | Sexual | LandEgg | 3 | 250 | 300 | 100 |

### Diet efficiencies (adult stage; juvenile stage in brackets where it differs)

| Species | grass | browse | fruit | seed | nectar | root | algae | phyto | carrion | dung | litter | wood | detritus | meat | blood |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Drifters | 0.6 | 0.15 |  | 0.2 |  |  |  |  |  |  |  |  |  |  |  |
| Browsers | 0.2 | 0.6 | 0.45 |  |  |  |  |  |  |  |  |  |  |  |  |
| Tuskers |  |  | 0.5 | 0.3 |  | 0.6 |  |  | 0.4 |  |  |  |  | 0.45 |  |
| Gliders |  |  | 0.7 | 0.4 | 0.3 |  |  |  |  |  |  |  |  |  |  |
| Burrowers | 0.1 |  |  | 0.7 |  | 0.3 |  |  |  |  |  |  |  |  |  |
| Hummers |  |  |  |  | 0.8 |  |  |  |  |  |  |  |  |  |  |
| Chewers | 0.5 | 0.5 |  |  |  |  |  |  |  |  |  |  |  |  |  |
| Delvers |  |  |  |  |  |  |  |  |  | 0.6 | 0.5 |  |  |  |  |
| Rotmats |  |  |  |  |  |  |  |  | 0.4 |  | 0.5 | 0.5 |  |  |  |
| Gleaners |  |  |  |  |  |  |  |  | 0.8 |  |  |  |  | 0.3 |  |
| Snappers |  |  | 0.15 |  |  |  |  |  |  |  |  |  |  | 0.75 |  |
| Prowlers |  |  |  |  |  |  |  |  | 0.2 |  |  |  |  | 0.8 |  |
| Stalkers |  |  |  |  |  |  |  |  | 0.3 |  |  |  |  | 0.85 |  |
| Leviathans |  |  |  |  |  |  |  |  | 0.4 |  |  |  |  | 0.85 |  |
| Bloodflies |  |  |  |  | 0.5 |  |  |  (0.3) |  |  |  |  |  (0.6) |  | 0.7 |
| Siltmites |  |  |  |  |  |  |  | 0.7 |  |  |  |  | 0.2 |  |  |
| Minnows |  |  |  |  |  |  | 0.6 |  |  |  |  |  | 0.2 | 0.5 |  |
| Pikes |  |  |  |  |  |  |  |  |  |  |  |  |  | 0.85 |  |
| Croakers |  |  |  |  |  |  |  (0.6) |  |  |  |  |  |  (0.2) | 0.7 |  |
| Herons |  |  |  |  |  |  |  |  |  |  |  |  |  | 0.8 |  |

### Derived values

| Species | Basal E/step | Emax | Gut | Vmax | Lifespan | Maturity | Sight | FoV ° |
|---|---|---|---|---|---|---|---|---|
| Drifters | 0.00200 | 2 | 0.3 | 1.2 | 3200 | 250 | 16 | 300 |
| Browsers | 0.01748 | 36 | 5.4 | 1.18 | 6591 | 515 | 28.5 | 280 |
| Tuskers | 0.00767 | 12 | 1.8 | 1.3 | 5008 | 391 | 22.9 | 240 |
| Gliders | 0.00136 | 1.2 | 0.18 | 1.54 | 2816 | 220 | 17.3 | 300 |
| Burrowers | 0.00101 | 0.8 | 0.12 | 1.03 | 2545 | 199 | 13.3 | 320 |
| Hummers | 0.00169 | 1.6 | 0.24 | 1.62 | 1816 | 236 | 13.3 | 360 |
| Chewers | 0.00200 | 2 | 0.3 | 0.84 | 1600 | 250 | 13.3 | 360 |
| Delvers | 0.00229 | 2.4 | 0.36 | 0.43 | 2344 | 262 | 13.3 | 360 |
| Rotmats | 0.00200 | 2 | 0.3 | 0 | 4800 | 250 | 18.4 | 360 |
| Gleaners | 0.00336 | 4 | 0.6 | 1.35 | 3805 | 297 | 25.7 | 260 |
| Snappers | 0.00091 | 0.7 | 0.105 | 1.41 | 2461 | 192 | 13 | 220 |
| Prowlers | 0.00456 | 6 | 0.9 | 1.52 | 4211 | 329 | 23.9 | 180 |
| Stalkers | 0.00566 | 8 | 1.2 | 1.67 | 4525 | 460 | 25.3 | 170 |
| Leviathans | 0.02236 | 50 | 7.5 | 1.35 | 7155 | 727 | 30.5 | 150 |
| Bloodflies | 0.00119 | 1 | 0.15 | 1.49 | 1615 | 210 | 12.6 | 360 |
| Siltmites | 0.00271 | 3 | 0.45 | 0.44 | 1417 | 277 | 12.6 | 360 |
| Minnows | 0.00101 | 0.8 | 0.12 | 0.87 | 2545 | 199 | 13.3 | 300 |
| Pikes | 0.00456 | 6 | 0.9 | 1.41 | 4211 | 329 | 19.9 | 200 |
| Croakers | 0.00136 | 1.2 | 0.18 | 1.1 | 2816 | 220 | 14.4 | 280 |
| Herons | 0.00398 | 5 | 0.75 | 1.96 | 4024 | 314 | 21.1 | 200 |
