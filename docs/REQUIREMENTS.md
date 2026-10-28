# NEUROREEF 2 — REQUIREMENTS SPECIFICATION

Document NR-RQ-001, Revision B

Each requirement has a number, a statement and a verification method:
**T** = automated test, **A** = analysis of headless runs, **D** = demonstration in the console,
**I** = inspection of code or documents. Verification status is tracked in the matrix in section 8.

"Shall" is binding. "Should" is a goal.

---

## 1. Engineering discipline (NR-1xx)

| No. | Requirement | Ver. |
|---|---|---|
| NR-101 | The simulation shall allocate all of its working memory during initialisation. No heap allocation shall occur inside the step loop. | T |
| NR-102 | Capacity (maximum animals) shall be a start-up parameter. Every array size shall be derived from it and from the world size, and listed in the memory map (DESIGN §3). | I |
| NR-103 | The simulation shall advance in fixed steps. Wall-clock time shall affect only how many steps are run per second, never the result of a step. | I, T |
| NR-104 | Every random draw shall come from a counter-based hash keyed by (seed, step, slot, stream). The same seed and the same capacity shall give a bit-identical run. | T |
| NR-105 | The result shall be independent of the number of worker threads (1, 2, 4, 8 compared bit-for-bit). | T |
| NR-106 | Every tuning value shall be declared once, in `Constants.cs`, with its meaning, unit and origin. `docs/CONSTANTS.md` shall mirror it, and a test shall fail when the two disagree. | T |
| NR-107 | The simulation core shall have no dependency outside the .NET base library. | I |
| NR-108 | Every phase of a step shall have a documented contract: what it reads, what it writes, and whether it runs in parallel (DESIGN §4). | I |
| NR-109 | The state shall be checkable: population counts shall reconcile with births, deaths and strays every step (births − deaths + strays = Δpopulation). | T |

## 2. Decision architecture (NR-2xx)

| No. | Requirement | Ver. |
|---|---|---|
| NR-201 | All animals of all species shall use the same perception routine and produce the same feature vector (DESIGN §5.1). | I |
| NR-202 | No species-pair relation (prey, threat, kin) shall be coded. Animals shall perceive others only through observable attributes: species identity (same or other), relative size, carnivory, motion, age, fatigue. | I |
| NR-203 | All animals shall share one library of base behaviours (primitives, DESIGN §5.2). No species shall have a primitive that another lacks. | I |
| NR-204 | Each animal's behaviour shall be determined by its own brain (identical architecture for every species), which maps features and hidden state to primitive gains (DESIGN §7). | I |
| NR-205 | The brain genome shall be heritable, mutated at birth and recombined in sexual reproduction. | T |
| NR-206 | Compound behaviours (grazing, vigilance, herding, fleeing, stalking, pack hunting, give-up, scavenging, following mother) shall not exist as code. They may arise only as combinations of primitives chosen by the brain. | I |
| NR-207 | Each primitive shall produce its documented steering vector or effect in a scripted scene (DESIGN §5). | T |
| NR-208 | A recurrent hidden layer shall carry state between steps, so that a behaviour can persist after its trigger is gone. | T |
| NR-209 | For any selected animal, the console shall show its features, hidden state, primitive gains, actions and reward every step. | D |
| NR-210 | No behaviour shall be hand-set. Founder brains shall be drawn from the RNG only; a test shall verify that founder genomes are a function of (seed, slot) alone and contain no species-specific constants. | T |
| NR-211 | Brains shall learn during life by a reward-modulated rule; the reward shall be the same function of drive relief and pain for every species (DESIGN §7.2). Learned changes shall not be inherited. A test shall show the probability of a rewarded action rising in a two-choice scene. | T |
| NR-212 | Evolved genomes shall be savable to and loadable from a gene-bank file. A genesis mode shall start from random brains. | T |
| NR-213 | The console shall measure and display which combinations of primitives each species uses (behaviour fingerprints). | D |

## 3. Ecology (NR-3xx)

| No. | Requirement | Ver. |
|---|---|---|
| NR-301 | Metabolic rate shall scale with mass^0.75 (Kleiber). Speed, gut capacity, reserves, lifespan and sight shall be derived from mass by the allometric rules in DESIGN §7.1. | I |
| NR-302 | Food shall pass through a gut of limited capacity and be digested at a limited rate, with efficiency set by diet genes. The resulting intake rate as a function of food density shall saturate (Holling type II). | A |
| NR-303 | Animals shall need water. Thirst shall rise over time and be cleared only by drinking at water. | T |
| NR-304 | Strike success shall depend on attacker power, prey mass, prey armour, juvenile status, prey fatigue, prey vigilance and prey group confusion (DESIGN §7.4). Strikes shall cost energy whether they succeed or not. | I, T |
| NR-305 | Who eats whom shall follow only from diet efficiencies, size and armour. The realised food web shall be measured, not declared. | I, A |
| NR-306 | Meat-eaters shall leave scent. Prey shall perceive scent and remember attacks (landscape of fear). | T |
| NR-307 | The vegetation shall have grass, shrub, sapling and tree layers that compete for light, soil nutrient and moisture, giving succession from bare ground toward forest (DESIGN §8). | A |
| NR-308 | Grazing shall remove grass. Browsing shall remove shrub leaves and kill saplings, so herbivores can hold back succession. | T |
| NR-309 | A travelling wet/dry season shall modulate moisture. Fire shall ignite in dry fuel, spread and reset vegetation, so the landscape stays a shifting mosaic. | A |
| NR-310 | Carcasses, dung and burned vegetation shall return nutrient to the soil. | T |
| NR-311 | Each species shall reproduce by its own mode: sexual, hermaphrodite, fission, facultative parthenogenesis, cyclic parthenogenesis or spores (DESIGN §8), with live birth or eggs, and litter size from the body plan. | T |
| NR-312 | There shall be no population cap other than capacity. Populations shall be limited by food, water, predation and disease of old age. | I |
| NR-313 | Immigrants (genesis immigration, and optional strays after genesis) shall be counted separately from births. After the genesis period, strays shall be off by default. | I |
| NR-314 | Acceptance: starting from the shipped gene bank, over seeds 1–3 and 200,000 steps with strays off, all 20 species shall be alive in ≥ 90% of telemetry samples. Predator–prey oscillations shall be visible, and no single vegetation layer shall exceed 80% of land cover for more than 25% of the run. Results, including failures, shall be reported. | A |
| NR-315 | The roster shall close the energy cycle: every pool has a consumer, every species has a food pool in its realm, every dead pool returns to nutrient. The program shall check this at start-up and refuse to run if it fails. | T |
| NR-316 | Every species shall have a reproduction mode whose preconditions it can meet in its realm. | T |
| NR-317 | Mutualisms (pollination raising fruit and seed set; seed dispersal by dung) shall be physical rules, not behaviours. | I |
| NR-318 | The world shall contain land and freshwater realms (lakes and rivers), linked by shore access, life-stage transitions and nutrient run-off. | I |
| NR-319 | Numerous small organisms shall be represented as super-individuals with their own brain, food and reproduction. | I |

## 4. Telemetry and analytics (NR-4xx)

| No. | Requirement | Ver. |
|---|---|---|
| NR-401 | The core shall keep running counters: births, deaths by cause (starvation, thirst, killed-by-species, old age, fire), killer × victim matrix, energy income by diet item, energy spending by category, hunts attempted and succeeded, matings, strays. | T |
| NR-402 | The core shall sample, into fixed ring buffers: population and age structure per species, primitive-usage share per species, mean decision-matrix drift against the founder, vegetation cover by layer, water, fire area, functional-response samples (intake versus local food density). | T |
| NR-403 | Explanations shown to the user shall be generated only from counters, by documented sentence rules. Each sentence shall carry the numbers it is based on. | I |
| NR-404 | Headless mode shall write the full telemetry to CSV. | T |

## 5. Console (NR-5xx)

| No. | Requirement | Ver. |
|---|---|---|
| NR-501 | The console shall show the world in real-time 3D: terrain, water, vegetation layers, fire, carcasses, and every animal as a per-species 3D model. | D |
| NR-502 | Camera modes: overview (orbit), follow-cam (third person, follows the lineage when the animal dies), ride-along (first person, with the animal's field of view and perceived animals marked). | D |
| NR-503 | An "experience a species" control shall pick a living member of that species and enter follow-cam or ride-along. | D |
| NR-504 | Dashboards: populations and phase plot; causes of death; killer × victim heatmap; energy budget; functional response; behaviour (primitive usage, mean matrix heatmap, drift); vegetation and fire; landscape of fear; per-species dossier; explanation feed. | D |
| NR-505 | The console shall work in desktop Chrome/Edge/Firefox and in Chrome on an Android tablet, with touch control. | D |
| NR-506 | A slow client shall never slow the simulation. | T |

## 6. Delivery (NR-6xx)

| No. | Requirement | Ver. |
|---|---|---|
| NR-601 | The program shall publish as a single self-contained Windows x64 executable that needs no installed runtime. | D |
| NR-602 | Starting the executable shall start the simulation, serve the console on port 5800, print the local and LAN addresses, and open the default browser. | D |
| NR-603 | All web assets, including third-party libraries, shall be embedded in the executable. No internet access shall be needed. | I |
| NR-604 | Continuous integration shall build and test on Windows and Linux and publish the executable as an artifact. | D |

## 7. Performance goals (NR-7xx)

| No. | Requirement | Ver. |
|---|---|---|
| NR-701 | The simulation should sustain ≥ 30 steps/s with 20,000 agents on a 6-core desktop CPU. | A |
| NR-702 | The console should render ≥ 50 frames/s at 1080p on a mid-range discrete GPU with 20,000 animals. | D |

## 8. Verification matrix

Filled in as tests and analyses are completed. Status: — not started, P pass, F fail.

| No. | Method | Evidence | Status |
|---|---|---|---|
| NR-101…NR-109 | T/I | `tests/Engineering*` | — |
| NR-201…NR-213 | T/I/D | `tests/Brain*`, `tests/Primitive*` | — |
| NR-301…NR-319 | T/A | `tests/Ecology*`, headless report | — |
| NR-401…NR-404 | T/I | `tests/Telemetry*` | — |
| NR-501…NR-506 | D/T | console screenshots | — |
| NR-601…NR-604 | D/I | CI artifact | — |
| NR-701…NR-702 | A/D | benchmark output | — |
