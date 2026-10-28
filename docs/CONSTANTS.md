# NEUROREEF 2 — CONSTANTS TABLE

Document NR-CT-001, Revision A (draft). `src/Neuroreef.Core/Constants.cs` holds the binding copy. A
test (NR-106) fails if this table and the code disagree.

**Origin codes**
- **V1**: value taken from Neuroreef v1.
- **ECO**: an ecological rule or exponent from the literature.
- **HT**: hand-tuned in headless runs. Starting values are given here, and they will be revised
  during tuning (plan step 4).

One step = one tick. Distances are world units. Energy is in arbitrary units, where 1 unit ≈ the
energy that one unit of mass stores.

## 1. World

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| WORLD | 1024 | units | side of the toroidal world | HT |
| FCELL | 4 | units | field cell side | V1 |
| CELL | 16 | units | spatial-hash cell side (≥ the largest sight range ÷ 2 neighbourhood rule) | V1 |
| CAPACITY | 32768 | slots | default animal slots | HT |
| SEA_SHARE | 0.12 | fraction | share of the map that is permanent lake | HT |
| POOL_SHARE | 0.03 | fraction | share of the map that is a seasonal pool | HT |
| T_SEASON | 9000 | steps | wet/dry season period | V1 |
| A_SEASON | 0.45 | — | season amplitude | V1 |
| SAMPLE_EVERY | 32 | steps | telemetry sample interval | HT |
| RING | 4096 | samples | telemetry ring length | HT |

## 2. Allometry (DESIGN §7.1)

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| EXP_BASAL | 0.75 | — | Kleiber exponent | ECO |
| EXP_SPEED | 0.17 | — | max speed exponent | ECO |
| EXP_LIFE | 0.25 | — | lifespan and maturity exponent | ECO |
| EXP_SIGHT | 0.20 | — | sight range exponent | HT |
| EXP_TURN | −0.20 | — | turn rate exponent | HT |
| C_BASAL | 0.0020 | E/step | basal cost at M = 1 | V1 (Drifter basal) |
| C_EMAX | 1.0 | E | reserve per unit mass | V1 |
| C_GUT | 0.30 | mass | gut capacity per unit mass | HT |
| C_DIGEST | 0.004 | mass/step | digestion rate at M = 1 | HT |
| C_BITE | 0.010 | mass/step | bite size at M = 1 | V1 (Drifter bite) |
| C_SPEED | 1.20 | units/step | vmax at M = 1 | V1 |
| C_TURN | 0.32 | rad/step | turn rate at M = 1 | V1 |
| C_LIFE | 3200 | steps | lifespan at M = 1 | V1 |
| C_MATURE | 250 | steps | maturity at M = 1 | V1 |
| C_SIGHT | 16 | units | sight range at M = 1 | V1 |
| C_MOVE | 0.0020 | E/(M·v²) | movement cost | V1 |
| C_THIRST | 0.0012 | /step | thirst rise (death after ~830 dry steps) | HT |
| C_FATIGUE | 0.012 | /step | fatigue gain at full sprint | HT |
| C_RECOVER | 0.004 | /step | fatigue recovery (×3 when resting) | HT |

## 3. Food (DESIGN §7.2)

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| RHO_GRASS | 3.2 | E/mass | energy density of grass | V1 (epsFlora) |
| RHO_BROWSE | 3.6 | E/mass | leaves | HT |
| RHO_FRUIT | 6.0 | E/mass | fruit | HT |
| RHO_MEAT | 8.0 | E/mass | fresh meat | HT |
| RHO_CARRION | 5.0 | E/mass | carrion | HT |

## 4. Hunting (DESIGN §7.4)

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| P0_STRIKE | 0.45 | — | base strike success | HT |
| C_CONF | 0.25 | — | prey-group confusion | V1 (confusion) |
| C_STRIKE | 0.004 | E/M | cost of a strike | HT |
| C_RETAL | 0.08 | E | retaliation per unit of excess defence | HT |
| C_CARC | 0.6 | mass/M | carcass mass per unit body mass | HT |
| JUV_DEFENCE | 0.25 | — | defence multiplier for juveniles | HT |

## 5. Reproduction and evolution (DESIGN §5.4, §7.6)

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| MUT_RATE | 0.03 | /byte | matrix mutation probability | V1 (0.05), lowered for the larger matrix |
| MUT_SMALL | 2 | weight units | small step σ | HT |
| MUT_BIG_SHARE | 0.05 | — | share of mutations that are large jumps | V1 (0.04) |
| MUT_BIG | 16 | weight units | large jump σ | V1 (24/24 → 16/16) |
| BODY_DRIFT | 0.02 | log-σ | body-plan drift per generation | HT |
| BODY_LIMIT | 0.30 | fraction | max body-plan deviation from founder | HT |
| C_MATE | 0.02 | E/M | mating cost (each partner) | HT |
| F_BIRTH | 0.40 | ×Emax | newborn energy | V1 |
| READY_ENERGY | 0.60 | ×Emax | energy needed to be READY | HT |
| STRAYS | off | — | immigrants on extinction | — |
| STRAY_FLOOR | 4 | animals | stray threshold when enabled | V1 |

## 6. Vegetation (DESIGN §8)

| Name | Value | Unit | Meaning | Origin |
|---|---|---|---|---|
| R_G | 0.006 | /step | grass growth | V1 (growth) |
| SIGMA_G | 0.06 | — | grass spread | V1 (spread) |
| S_G | 0.002 | — | grass seed rain | HT |
| R_S | 0.0015 | /step | shrub growth | HT |
| S_S | 0.02 | — | shrub invasion of grass | HT |
| R_P | 0.0008 | /step | sapling recruitment | HT |
| M_P | 0.0010 | /step | sapling maturation | HT |
| R_T | 0.0004 | /step | tree growth | HT |
| D_T | 0.00005 | /step | tree mortality | HT |
| C_SHADE | 0.002 | /step | canopy shading of shrubs | HT |
| C_SHADE_G | 0.004 | /step | canopy shading of grass | HT |
| K_N | 0.3 | — | nutrient half-saturation | HT |
| R_F | 0.003 | /step | fruit production | HT |
| D_F | 0.004 | /step | fruit decay | HT |
| C_U | 0.0006 | /step | fuel accumulation | HT |
| D_U | 0.002 | /step | fuel decay when wet | HT |
| LAMBDA_FIRE | 2e−7 | /cell/step | ignition rate | HT |
| C_SPREAD | 0.35 | — | fire spread | HT |
| C_ASH | 0.5 | — | nutrient from burned biomass | HT |
| C_ROT | 0.0025 | /step | carrion rot | V1 (corpseDecay) |
| C_NUTGAIN | 0.70 | — | rot to nutrient | V1 (nutGain) |
| C_NUTUSE | 0.60 | — | nutrient per unit growth | V1 (nutUse) |
| WEATHER | 0.0015 | /step | nutrient weathering | V1 |
| C_SCENT | 0.02 | /step | scent deposit | HT |
| D_SCENT | 0.01 | /step | scent decay | HT |

## 7. Species body plans

Factors k multiply the allometric value from §2.

| Gene | Drifters | Browsers | Foragers | Stalkers | Leviathans | Gleaners |
|---|---|---|---|---|---|---|
| mass M | 1.0 | 18 | 1.5 | 4.0 | 25 | 2.0 |
| k_speed | 1.00 | 0.55 | 0.85 | 1.05 | 0.55 | 0.75 |
| k_life | 1.0 | 1.0 | 1.0 | 1.0 | 0.9 | 1.0 |
| k_mat | 1.0 | 1.2 | 1.0 | 1.3 | 1.3 | 1.0 |
| k_sight | 1.0 | 0.9 | 1.0 | 1.2 | 1.0 | 1.4 |
| field of view (°) | 300 | 270 | 240 | 160 | 140 | 240 |
| armour | 0.2 | 1.6 | 0.3 | 0.4 | 0.9 | 0.15 |
| strike power | 0.0 | 0.5 | 0.4 | 1.0 | 1.3 | 0.1 |
| eff grass | 0.60 | 0.20 | 0.10 | 0 | 0 | 0 |
| eff browse | 0.15 | 0.55 | 0.15 | 0 | 0 | 0 |
| eff fruit | 0.20 | 0.40 | 0.65 | 0 | 0 | 0.20 |
| eff meat (carnivory) | 0 | 0 | 0.50 | 0.85 | 0.85 | 0.30 |
| eff carrion | 0 | 0 | 0.40 | 0.30 | 0.40 | 0.80 |
| litter | 2 | 1 | 3 | 3 | 1 | 6 |
| gestation (steps) | 120 | 600 | 150 | 300 | 900 | 200 |
| cooldown after birth (steps) | 150 | 500 | 200 | 400 | 1200 | 250 |
| initial count | 6000 | 900 | 1500 | 500 | 60 | 600 |
| colour | cyan | amber | lime | magenta | violet | orange |
