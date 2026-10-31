# NEUROREEF 2 — DESIGN DOCUMENT

Document NR-DD-001, Revision O. It implements NR-RQ-001 Revision G (`REQUIREMENTS.md`). The values of every
constant named here are in `CONSTANTS.md`, which is generated from the code.

**Saves that survive an update, and a faster start** (2026-10-31; Checkpoint.cs, Land.cs). Checkpoint version 18 writes a
kind byte per field (so a field a later program does not know can be skipped) and, for the big per-slot arrays (genome,
weights, traces, learning history, hidden state, gains, features), only the slots in use: each is written whole at a birth
before it is read. A save of format 12 to 17 (the genome layout is unchanged since 12) is migrated on load: every field
whose name, kind, element type and length still match is kept (the animals with their genomes and brains, the clades, the
step, the telemetry); a field the save lacks starts as a new world has it (a missing phenotype field: every animal's body
is expressed again from its genes); a field the program no longer has is dropped. The land (Checkpoint.LandFields:
terrain, climate, water cycle, plants with their genotype table, the pathogen reservoir) is rebuilt from the world's own
terrain seed and nature exactly as a new world builds it if the save is older than format 17 (other land rules) or any
land field no longer fits; the reservoir is then seeded as at creation, the seed and pollen animals carry are dropped,
and an animal standing where the rebuilt land does not hold it moves to the nearest cell that does. The load prints what
it kept and rebuilt. Startup: a world that is about to be loaded from a save builds no land and no founders (restore);
a prehistory forms its relief (Terrain.Shape: depends on the terrain seed alone) once and builds its branches side by
side, each on its own workers (ponds, plants and founders depend on the branch's run seed, so the rest is built per
branch: copying branch 0's spun-up land would change the other branches); the world that begins after a prehistory on
its map starts on branch 0's land as it was before its founders (same seed, same land) instead of growing it again;
--validate builds each seed's land once for its two runs. A world built from a Land is bit-identical to one that builds
its own (CheckpointTests).

**Evolving plants (Revision S)** (2026-10-30; Plants.cs). Land vegetation is no longer three fixed layers with designed
rates. Each land cell has four slots (herb, shrub, tree, sapling), each one genotype (an id into a table of up to 65,535)
and its cover (Pool.Grass, World.Shrub/Tree/Sapling, now updated in place: the old double buffers are gone). A genotype
is 20 traits of 4 bits: height, leaf mass per area, rooting, water-use efficiency, drought tolerance, temperature
optimum, deciduousness, flowering phase, reproductive allocation, seed mass, fruit pulp, wings, hooks, nectar, toxin,
thorns, resprouting, bark, nitrogen fixation, selfing. Its phenotype follows cited trade-offs (CONSTANTS.md, group
"Plants"); a genome of all 0.5 grows at the old R_G, R_S and R_T at 0.3, 1.3 and 15 m. Growth is carbon x water
colimitation under the light of taller slots, with root overlap, frost, drought mortality and leaf shedding. Mature
plants flower in a pulse at their own phase, set seed by wind or animal pollen (crosses need genetic and phenological
compatibility, else they self), and put seed into nectar, fruit (Pool.Fruit, eaten and dispersed by animals) and dry
seed, which wind takes to the cell itself, its 8 neighbours or (a simple fat tail) a 16x16-cell block spread over the
3x3 blocks around; mass is conserved exactly. Each cell keeps a seed bank per stratum (Pool.Seed is its sum) with one
seed and pollen parent pair; recruits are crosses with rare one-level mutations, and win the slot by lottery (Chesson &
Warner 1981). Animals taste plants at the mouth: toxins, tough leaves and fixed nitrogen change a bite's energy, thorns
its size (FOOD_HERE and the bite choice; FOOD_GRAD does not taste). Seed passes guts (gape-limited) into dung, caches go
into banks, hooked seed rides in coats, nectar feeders carry pollen. Fire burns by traits (resprouting, bark, height over
the flames). Founding: 128 random genotypes, each cell taking the best growers for its site; then a year of spin-up
without animals. Schedule: a stripe of rows (y = step mod 8) per step, rows in parallel, dt = 8, new genotypes queued per
row and committed serially, so results do not depend on the thread count; a census every 64 steps frees unused
genotypes. Not built yet (design steps 7-8 in full): lineage splitting into named species and varieties, the Flora
console panel and 3D tinting; the world report has a FLORA section and the console seven new map layers. Checkpoint
version 17.

**Land formed by geology (Revision R)** (2026-10-30; the user: "the land should be FORMED by geological processes at
world creation, not drawn from fractal noise"). Before, the relief was fBm noise, the lakes its lowest 10%, and 20
erosion passes cut it. Now Geology.Form (Geology.cs) makes it in deep time, and Terrain.Build finishes it with the same
fine Hydrology.Erode passes and the rest of BuildWater as before.
- *Plates.* 5-9 plates on the torus: a weighted, warped Voronoi tessellation. Each plate has a velocity (1-8 cm/yr and a
  slow spin) and a crust 30-45 km thick. At a convergent boundary, crusts within GEO_SUBDUCT of each other collide and
  thicken a belt on both sides. Otherwise the thinner plate sinks: the thicker one carries a volcanic arc 200 km in,
  and the sinking one gets a flexural moat. Divergent boundaries thin the crust into rifts, which stop sinking at
  20 km of crust. Only the closing or opening part of the relative motion works the crust, so transforms do little.
  Thickening follows mass balance (T c / (sqrt(pi) W) across a belt), and Airy isostasy turns it into rock uplift
  (x0.15). Each plate starts at the Airy height of its own crust.
- *Rock.* Old thick plates (40 km or more) expose crystalline basement; younger ones carry 1.5 km of sedimentary cover.
  Arcs and fast rift axes are volcanic. Erosion strips cover before it cuts bedrock, and sediment laid down becomes
  cover. Erodibility (Stock & Montgomery 1999) is 1, 0.5 and 0.25 /Myr for sedimentary, volcanic and crystalline rock.
  World.Rock holds the surface kind, and the fine passes cut it at those ratios (Hydrology.Erode's new kRock).
  Fertility comes from the parent rock: crystalline 0.4, sedimentary 0.6, volcanic 0.85 (andosols), +-FERT_VAR of local
  variation, plus alluvium as before. It replaces the old noise fertility.
- *Deep time.* 200 steps over 60 Myr on a coarse grid of FW/2 cells. Each step applies uplift, then stream power above
  a threshold (implicit, Braun & Willett 2013) over Priority-Flood routing of Climate.Rainfall (re-read every 25 steps
  as the relief grows). Then come transport and deposition as in Hydrology.Erode, hillslope diffusion, and flexural
  isostasy (the ground rises by 0.82 of what is worn off, spread over 150 km). Lakes, the Nature.Water quantile
  throughout, are the base level. The result is upsampled (Catmull-Rom), and roughness of 6% of the coarse cell-to-cell
  relief is added on top. Checkpoint version 14.
What did not work on the way: (1) With no limit, a big river dumped its load in one cell and built sediment towers 15 km
high; a deposit may now not rise above the lowest cell that feeds it. (2) With no plateau limit, rebound lifted eroded
peaks to 10-17 km and squashed the rest of the land under them; nothing now rises above GEO_H_MAX = 5 km. (3) Plains
were worn down to lake level. Their lakes then broke into thousands of puddles, and rivers fell to 0.02-0.5% of the
map. The fixes were flexural rebound, relief-proportional roughness only, and (4) an erosion threshold GEO_PSI. Without
the threshold, 60 Myr stripped the platform cover from all but the river beds, and 75% of the land was bare basement.
Measured (default map, seeds 1, 2, 3, 7, 11; before -> after):
- Relief above the lakes: mean 0.93-1.26 -> 0.84-1.15 km, SD 540-650 -> 710-900 m, p90 1.8-2.1 -> 2.0-2.7 km.
- Land above 1500 m: 21-38% -> 17-31%. Mean slope: 39-51 -> 98-121 m/cell (mountains are steeper and plains flatter).
- Convergent belts are 19-29% of the map and stand 1.3-2.1 km high, against 0.6-0.75 km in plate interiors. They hold
  64-95% of the highest tenth of the land.
- Rift belts are 5-10% of the map, stand -0.17 to +0.16 km, and are 46-71% lake. Lakes are 10.3-10.4% of the map.
- Surface rock on land: crystalline 46-57%, sedimentary 39-50%, volcanic 4-6%. At the end of deep time, dh/dt is
  5-18% of the uplift in the uplifting belts (near steady state).
- Rivers 0.82-1.05% -> 0.50-0.95% of the map. Alluvial land 10-12% -> 9-11%. Land fertility 0.56-0.66 -> 0.54-0.58.
- Drinkable water is 11-14 -> 13-15 cells away on average. Pond sites 650-1900 -> 94-1260 cells: a fluvially graded
  landscape has few closed hollows.
- Generation time: 0.7-1.2 -> 1.6-2.1 s at the 1024 map, and 3.3-3.7 -> 7.5-8.5 s at 2048 (rivers 0.43-0.64 -> 0.30-0.61%).
  These were measured on 4 cores while another job shared the machine.
On this graded relief the fine passes are transport-limited: valley floors fill (+5-50 m, seed 1) and slopes lose ~10 m. So
HydrologyTests now tests incision alone (no deposition or creep) and the full model's mass budget.
EcologyTests' ancestor-viability check now measures a clade by most of its founders, not by the first in the slots
(on the new land, three first founders varied just below FORAGE_MARGIN).
Simplifications: the plate boundaries are fixed over deep time; the plateau limit is a cap, not crustal flow; and the
plates' scale (GEO_KM 5,000 km) is not the climate's or the animals'.
**A running water cycle** (2026-10-30; the user: "a real, running water cycle ... physics, not designed rules"). Before,
soil moisture was a fixed map scaled by season and the year's rain (MoistBase x season x (1 + RainAnom) x evaporation),
ponds filled when that wetness passed POND_FILL, floods when it passed each cell's FloodWet, and rivers had their fixed
generation flow. Now water is stores and fluxes (WaterCycle.cs; Water cycle constants), updated every WC_EVERY = 50
steps (rows in parallel, then a serial topological routing pass):
- *Rain and snow.* Rain = P_MEAN x the rain pattern (Climate.Rainfall, with RAIN_LOCAL of it spread over the land of its
  latitude as recycled convective rain) x the moisture season by latitude x (1 + RainAnom). At or below T_SNOW it is
  snow, which melts by degree-days (DDF); a pack over SNOW_MAX is a glacier and its surplus runs off.
- *Soil (Manabe bucket).* Capacity SOIL_CAP, up to twice that on alluvium. Rain on the saturated share runs off (VIC
  curve, INFIL_B); evapotranspiration = PET(T) (Tetens, 0 at or below 0 °C) x plant cover x min(1, wetness / BETA_FC);
  drainage ~ wetness^4 recharges groundwater. **Moisture = soil water / capacity** (plants, fire and thirst read it);
  MoistBase now only seeds the first vegetation.
- *Groundwater.* Shallow (local) groundwater feeds each cell's own streams; GW_DEEP of recharge goes to a deep
  (regional) store that flows cell by cell down the drainage and emerges above GW_CAP: springs (drinkable, Wet = 2).
- *Routing, rivers, floods.* Runoff, baseflow and seeps run down the generation drainage (Hydrology.Route receivers,
  stored at Init) to the lakes. A river floods its floodplain cell when its discharge reaches FloodWet x its mean
  discharge (a slow, QMEAN_YEARS mean: the channel's size); the flood soaks the soil from the river's water.
- *Ponds and lakes.* Pond sites fill from their catchment's runoff and rain (fill-and-spill), and lose water by open
  water evaporation and seepage over a wetted area ~ sqrt(volume) (Hayashi & van der Kamp 2000); drinkable from POND_WET.
  Lakes keep their extent (changing Cell at runtime would touch realm checks, DistWater and DistLand); each lake's level
  is tracked: inflow + rain - evaporation (over a wetted area that shrinks as it falls), spilling above its sill
  through an outlet sized for its mean inflow (LAKE_STAGE).
- *Start.* WaterCycle.Init (Simulation, after Terrain.Build) runs WC_SPINUP = 2 average years (groundwater set to the
  steady state of the first year's recharge), then sets each river's and lake's mean flow. Checkpoint version 14.
Measured (default 1024 map, empty world, two years, seeds 3 and 7): the land budget (rain = ET + pond evaporation +
flow to the lakes + storage change) closes to ~1e-9 of the rain; the lakes' to ~1e-6. Of the rain (844-976 mm a year
on the land), 47-49% goes back to the air and 50-53% to the lakes (Earth's land: ~60/40). Land-mean moisture 0.46-0.53
over the seasons (it was 0.43-0.55), 0.33-0.60 within latitude bands. Big river mouths swing 0.6-1.9x their mean
discharge (0.46-2.8x at Climate 1, with snowmelt peaks; land-mean snow up to ~400 mm there, ~0 at the default 0.3).
Floods 0-365 cells (before 2-441). Ponds with water: 800-870 of 1,026 sites (seed 3) and ~1,700 of 1,811 (seed 7),
falling a few % in the dry season and much more in a drought, against 672-1,026 and 771-1,811 before; 320-490 spring
cells. **Drinkable water is closer: land is 6.5-7.1 cells from it on average, against ~11 before**, because most
ponds now hold water all year (the rain's season is only +-A_SEASON, so basins get runoff year round) and springs add
water away from lakes; expect fewer thirst deaths. Cost: +0.12-0.18 ms a step (fire and water phase), ~0.4-0.5 s more at
world creation; steps/s are unchanged within noise.
Tested (tests/WaterCycleTests.cs): the budget closes at climate 0, 0.3 and 1; a rainless season dries the soil below
60% while every river cell keeps flowing; snow lies through a cold spell, melts at exactly DDF per degree-day and peaks
the rivers above a warm season's flow; ponds fill in a wet year and empty in a drought; the water cycle gives the same
bits on 1 and 3 threads.
**Pathogens that live and evolve (Revision R)** (2026-10-30; the user: "pathogens become living, evolving agents with no
brain. They decide collectively through quorum sensing"). Before: an infection was a timer (DIS_STEPS) with a virulence
number and a fixed death chance per step (DIS_MORT), caught from sick look-alikes or a flat spillover (DIS_SPILL).
Measured before (seed 3, 3,000 steps): no deaths by disease. Now (Pathogens.cs; constants PG_*, one "Pathogens" block;
§9.6 is superseded):
- *A genome per infection*, 10 bytes plus the 4-byte antigen (World.PgGene, Strain): replication, toxin, four route
  weights (contact, ground, water, dung), persistence, quorum threshold, host breadth, and the founding lineage. It mutates
  on every transmission (PG_MUT, MHC_MUT). Trade-offs: persistence and breadth cost replication (De Paepe & Taddei 2006;
  Leggett et al. 2013); toxin raises shedding (PG_TOX_SHED, Ewald 1994) and damages the host, ending the infection.
  Breadth widens how far the antigen may sit from a host's MHC genes and still grow (host range, any species).
- *Within the host*: load L grows logistically at r x fit and is killed by an immune effector that expands clonally
  while it sees the pathogen (Antia, Levin & May 1994), helped by the immunity gene and MHC diversity. Clearance leaves
  a memory of the antigen. Memory protects against later strains as exp(-d^2/PG_CROSS_W^2) of antigenic distance, both
  against infection and as an immediate recalled response, and it decays over years. Death comes when damage (PG_DMG x
  toxin x L, repaired in proportion to the energy reserve) reaches 1. Load and immune effort raise basal cost; the
  immune response and damage are felt as malaise (pain).
- *Quorum sensing*: signal ∝ L, switch L^4/(L^4 + threshold^4). Below its threshold a strain makes PG_STEALTH of its
  toxin and shedding and is seen at PG_DETECT by the immune system. A threshold above 1 is never reached in one host.
- *Reservoir*: a per-cell load and strain (World.ResLoad, ResGene, ResKey). One strain per cell; a deposit takes over the
  cell with probability weighted by amount. It is fed by shedding (the ground, dung while the gut passes waste, the cell's
  water or a neighbouring water cell) and by dead hosts. It decays by the strain's persistence, PG_DRY times faster on
  dry land. River water carries PG_FLOW of its load a cell downstream per step, along receivers taken from World.Flow
  (Hydrology is read, not changed). Hosts pick it up by standing in a cell, eating there or drinking there. At world
  start PG_FOUNDERS random strains are laid in PG_SEED_CELLS random cells each; nothing seeds them again (the flat
  spillover is gone). Contacts no longer need to look alike: any shedding animal in reach exposes, and the strain's fit
  decides.
- *Telemetry*: global columns pathogen.* (infected, strains clustered within PG_STRAIN_D, founding lineages, share
  stealthy, mean traits, reservoir). The world report has a PATHOGENS section, including infections by route. Gene bank
  unchanged; checkpoint version 14.
Measured (default map, seed 3, 15,000 steps):
- *Counts.* 44,891 infections: 25,095 with food, 10,597 by contact, 8,521 from the ground, 678 with water. **No deaths by
  disease.** Infected hosts rose and fell in outbreaks, from 20 up to 904 at once, as the host population went from
  ~1,900 to 8,800 and back. Strains went from 12 founders to 77-95 by steps 8,000-15,000. Only 2 of the 12 founding
  lineages remain in hosts.
- *Traits, mean over infected hosts, step 32 to 15,000.* Replication rate 0.13 -> 0.36. Toxin 0.51 -> 0.14-0.48,
  falling between outbreaks. Quorum threshold 0.47 -> 2.2 load. Persistence 0.65 -> 0.96. Breadth 0.48 -> 0.44. Water
  route share 0.28 -> 0.40. Dung route 0.18 -> 0.17.
- *What happened: hide, not strike.* Thresholds climbed above 1, which no host can reach, so nearly 100% of infections
  are stealthy at mean loads of 0.01-0.06 and never switch to toxin. Strains also became spore-like: the high
  persistence costs them within-host growth (PG_PERS_COST). They live mostly in the ground and pass through hosts by
  grazing.
- *Why no deaths.* A strain that switched on, grew to high load and made toxin could kill (PG_DMG: ~11 steps at full
  toxin and load), but no such strain spread here. PG_DMG was raised from 0.04 to 0.1 for this reason: at 0.04 no
  strategy could ever have killed a fed host.
- *Cost.* +0.3-0.45 ms per step in LIFECYCLE (0.15 ms before), about 1.5% of a step. Whole-run steps/s were within
  machine noise: 27-47 before, 32-34 after.
- *Not tested.* Other seeds and larger maps. Whether denser host populations select for "hide then strike".

**Memory that matters: local senses and path integration** (2026-10-30; the user: "make an animal's memory useful,
by taking away its omniscient senses and giving it a self-motion sense"). Before the change SEEK_WATER and SEEK_LAND
steered down a map-wide distance field (DistDrink, DistLand), so an animal knew the way to water from anywhere, and a
memory of where water was could add nothing.
- *Water and land are sensed only nearby.* SEEK_WATER and SEEK_LAND point the way only when water (or land) lies within
  SENSE_WATER = 8 cells (32 units, MAX_SIGHT, the range WATER_CLOSE and LAND_CLOSE already faded over); beyond it they
  give no direction.
- *Path integration (dead reckoning).* Each animal keeps a vector back to where it last drank and one to where it last
  fed well (a bite worth FED_WELL steps' need), updated in RESOLVE from its own movement (PathMemory.cs). Its estimate of
  each step's move is off by a random vector of SD PI_ERR x the distance moved, so the remembered place drifts as the
  square root of the way travelled, as in desert ants (Mueller & Wehner 1988; Cheung et al. 2007). A newborn remembers
  nothing. Six new senses (DRINK_AHEAD/LEFT/NEAR, FED_AHEAD/LEFT/NEAR: the bearing against its heading, and
  1 / (1 + d / MEM_NEAR), 0 when nothing is remembered) and one new steering primitive, RETURN_FED, which the brain
  (or an evolved reflex) can learn to use. Gene bank and checkpoint version 12.
- *The thirst instinct can use memory.* The innate THIRSTY -> SEEK_WATER link is unchanged; beyond sensing range
  SEEK_WATER now heads for where the animal last drank, if it remembers. An animal that never drank, or whose pond has
  dried, is on its own.
Tested (tests/DecisionTests.cs): 100 cells from water, a thirsty walker with no memory turns less than a quarter of its
turn rate; with a stored drink vector it turns at full rate toward it. Over 60 steps the integrated vector stays within
4 SD of the true way back, and is never exact. Not yet measured: how this changes thirst deaths and ranging in a living
world (expect more thirst deaths at first, as the founders no longer find water from anywhere).
Other senses that still read more than an animal could know, left for now: FOLLOW_MOTHER and MOTHER_FAR give the way to a
young animal's mother at any distance; a mate's readiness, another's diet (NEAREST_MEAT) and weakness are read from its
internal state rather than from signals.
**Rivers that run (Revision Q, hydrology)** (2026-10-30; the user: "basic hydrology, so the world has meaningful
persistent places: river valleys, floodplains, fertile alluvial soil, wetlands. These should come from physics"). Measured before the change (default
1024 map, seeds 1, 2, 3, 7, 11): river cells were 0.00-0.01% of the map. The old D8 pass routed water into the first
pit of the noise relief and stopped, so flow hardly ever reached RIVER_ACC. Land was 11-20 cells from drinkable water
on average. Changes (Hydrology.cs, Terrain.Build):
- *Routing.* Priority-Flood+epsilon from the lakes outward (Barnes et al. 2014) gives every cell one receiver over the
  depression-filled surface, and an order in which each cell comes after the cell it drains into. Flow is the rain
  each cell gathers from above (Climate.Rainfall, which reads winds and relief but not the climate's strength, so a
  seed makes the same land at any climate setting). At the lakes it sums to exactly the rain that fell. Rivers are the
  land cells whose flow passes RIVER_ACC. They are drinkable shallow water whose depth goes as flow^0.4 (Leopold &
  Maddock 1953), and they run river to river into the existing lakes.
- *Erosion and alluvium.* ERODE_ITERS passes of hillslope creep (Culling 1960), detachment-limited stream power
  dh = -K A^0.5 S solved implicitly (Braun & Willett 2013), and transport: a load over capacity (~A^0.5 S) is partly
  dropped, and a closed basin keeps what it can hold up to its rim. Sediment is conserved: what the land loses is what
  reaches the lakes. Alluvium (what is left of the deposit) adds up to ALLUV_FERT to the soil's fertility (Fert).
- *Wetlands.* Closed basins that are left on the worked relief (BASIN_MIN below their rim) become pond sites, as well
  as the old local minima. Sediment fills many small basins, so without this the pond sites fell from ~2,000-2,600
  cells to ~200-340.
- *Floods.* Each land cell up to FLOOD_R flow steps above a river, and low enough above it, gets the wetness (season x
  year's rain) at which that river's stage tops its banks (BANKFULL) and reaches it. When it floods (Ponds.Update), the
  cell is drinkable (Wet = 3) and soaked (moisture 1). It also takes FLOOD_MIX of the gap between its nutrient and the
  river's, and that nutrient leaves the river (the flood pulse, Junk et al. 1989). Frozen rivers do not flood.
Measured after (default map, seeds 1, 2, 3, 7, 11): rivers are 0.8-1.05% of the map, floodplains 3.1-3.5% of the land,
alluvial soil (over ALLUV_D) 10-12% of the land. Alluvial cells have fertility 0.94-0.97 against 0.51-0.63 on other
land. Lake share is 10.3-10.5% (it was 10.0%; erosion lowers a few river mouths below lake level). Land is now 11-14
cells from drinkable water on average. Channels on slopes are cut ~20 m on average and up to 200-350 m; hillsides
lose ~6 m. Generation takes 0.75-1.0 s at the 1024 map (it was 0.5-0.7 s; erosion is ~0.28 s of it) and 3.5 s at 2048
(was 1.8 s). Over two simulated years (empty world, seeds 1 and 7), 12-35% of the floodplain flooded at least once.
Flooded cells ended with nutrient 0.62-0.73 and plant cover 0.76, against 0.58-0.63 and 0.62 on other land. Floodplain
cells that never flooded are poorer (nutrient ~0.43): the existing runoff rule washes a shore cell's nutrient into the
water beside it, and rivers now give the land many more shores. River water is near N_MAX.

**Brains that remember** (2026-10-30; the user: "why do big brains die so much, why are they not smarter"). Measured
before the change: a 256-unit brain held about 7 steps of its past, no more than a 24-unit brain, and learned a
delayed-cue task worse. Its many recurrent inputs drowned its senses, and its learning step grew with its size.
- *Wiring gain gene (B_WIRE).* It sets the spectral radius of the recurrent memory, from WIRE_LO to WIRE_HI
  (Body.WireScale estimates the radius by the circular law). Near 1 a reservoir holds the most memory (Jaeger 2001).
- *Fan-in.* A brain larger than BRAIN_START scales its sense weights by sqrt(BRAIN_START / units), so its senses are
  not lost among its memory units.
- *Normalised learning (NLMS).* When a brain's input size goes above NLMS_REF, its learning step is scaled down to match.
Measured after (tests/BrainTests.cs): memory capacity is 7.8 steps at 24 units and 23.2 at 256. On a cue that must be
held 6 steps, 12 units score +0.30 and 256 units +0.65. On a 3-step delay small brains still learn faster (+0.58 vs +0.33).

**Revision P: worlds start as food allows, and every world is new** (2026-10-30; the user: "why were so many spawning
in? ... how are they getting so much food?", "within 10,000 steps [one] kills everything else", "it starts the
ancestors as preforms ... it's supposed to be randomly generated every time"). Measured before the change (default
world, seed 7): every ancestor started with a fixed 600 founders whatever its size; 6,000 founders fell to about 400 by
step 3,000, 70% of deaths were starvation, and 3 of 10 lineages were left at step 10,000. Causes and changes:
- *A new seed every world.* The program always used seed 1, so every world had the same map, ancestors and names. With
  no --seed, a new world now draws a random seed (printed at start and shown in the console's bar; `--seed N` makes the
  same world again; a saved world or prehistory resumes with its own).
- *Founders set by food.* Each lineage starts with FOUND_FILL (a quarter) of what its home range can feed at its body size,
  brain and diet: the range's sustained plant yield (Fields.Yield: each pool's growth at the stock where it grows
  fastest, seasonal pools at their yearly mean) over a day of living, FIELD_METAB = 1.5 x basal (measured in this world:
  basal 0.92, moving 0.12, growth 0.16, breeding 0.15, heat 0.10). The setup's "founders" is now only an upper limit.
  A large body's home range widens (up to HOME_R_MAX) until it can feed FOUND_MIN founders, as home ranges grow with
  body mass.
- *Ancestors that can live where they live.* Of up to ANCESTOR_TRIES random bodies, an ancestor is the first whose home
  range can feed FOUND_MIN founders and whose gut can eat fast enough: one bite a step of a half-grazed cell, or its
  digestion rate, must give FORAGE_MARGIN days' need (a Holling type II limit). Nothing chooses a diet or a size: a
  20-unit nectar eater, a water animal with a grass diet or a meat-eater with nothing to eat is simply never an ancestor.
- *Food sensed as worth to the eater.* FOOD_HERE (and the food gradient) was the cell's food against the best food the
  diet could ever give, so a nectar eater in a meadow sensed ~0.02 and its eating instinct hardly fired: hungry
  founders stood on food and starved. It is now one bite's energy against a step's need, 1 - exp(-bite/need).
  Prey is valued the same way (a bite of its edible tissue).
- *Fibre is fermented, and fermenting takes time (the Jarman-Bell principle).* A gut holds ~M of food while food passes
  at ~M^0.75, so food stays M^0.25 longer in a bigger gut. The efficiency on fibrous foods (grass, browse, roots, dung,
  litter, wood) is multiplied by (1 - exp(-M^0.25/TAU_FERM)) / (1 - exp(-1/TAU_FERM)): x0.63 at M = 0.1, x1.5 at 10,
  x1.8 at 40 (never above DigMax). Before, digestion did not depend on size, so the smallest, fastest-breeding body won
  everywhere (with one ancestor per size class, a mass-0.2 lineage filled the map and every other class died out).
  Now small bodies do best on rich food (seed, fruit, nectar, meat) and big ones on grass and leaves: size niches
  follow from the physics, with no size given to any niche.
- *Warm- and cold-blooded bodies (endothermy gene B_ENDO, 0..1).* Every animal was an endotherm at T_BODY = 38 °C, so
  "fish" in 10-20 °C water spent 40-96% more than basal on heat, and no water lineage lived (0 of 36 in 9 worlds). Now a
  body's core is Heat.Core = T_op + e (T_BODY - T_op) (T_BODY above it): a cold-blooded body (e = 0) takes its
  surroundings' temperature and pays nothing to keep warm, but its chemistry runs at the Boltzmann-Arrhenius rate of
  metabolic theory, exp(E/k (1/T_BODY - 1/T_core)) with E_ACT = 0.65 eV (Gillooly et al. 2001): basal cost and digestion
  x0.23 at 20 °C, muscle speed by its square root. Ancestors draw e at random; nothing ties it to water or land. In the
  first runs almost every viable swimmer was cold-blooded, as fish are. "warm-bloodedness" is a trait in the charts.
- *Viability counts what a body really pays where it lives.* A range counts only the cells its locomotion lives in
  (water for a swimmer, land for a walker, both and shallows for a wader, anything for a flier); a day costs FIELD_METAB
  x basal at its core temperature plus the heat it must make there (Heat.Balance); and a brain may take at most
  BRAIN_SHARE_MAX = 25% of the basal cost (humans ~20%; Aiello & Wheeler 1995): the 256-unit brain drew 0.1-0.4 bodies
  it could not pay for and died in 9 worlds of 9.
- *The young grow continuously.* A juvenile grew only from energy above half its reserve, and the young lived near 40%
  full: at 400 steps they had built 12% of their adult tissue. It now grows at its top rate times its reserve density
  (a dynamic energy budget, Kooijman 2010), keeping GROW_RESERVE = 20% against starving.
- Measured over 9 worlds x 10 ancestors (512 map, 2,000 steps): ancestors with at least 10 alive went from 28 to 50 of 90,
  swimmers from 0 to 15 of 36, the big-brained ancestor from 1 to 4 of 9. Over 10,000 steps most worlds keep 1-5 lineages
  of a few dozen to a thousand animals; deaths are mostly starvation (about 60%) and kills (25%, most within a lineage,
  learned or evolved, not designed).
- *Appetite and diet choice.* With food at hand an animal eats while its gut has room, at full strength until its
  reserve is within SATIETY = 20% of full (it scaled with hunger alone, so adults half full stood on food and ate on
  47% of steps). And the food it bites is the one giving the most energy it can use this step, a bite no bigger than
  its gut passes in a step (optimal diet theory, Charnov 1976): a heap of dung or litter no longer beats a little good
  food and clogs the gut (gut food was 0.53 E/M against a best diet food of ~1.9). Births over 3,000 steps rose by
  10-55% in three worlds.
- *Mates are searched for, and the breeding condition evolves.* A ready animal with no mate in sight did nothing; in a
  thinly spread lineage a ready female had a mate in sight 3% of the time, and the last big lineage of one world died
  unmated. SEEK_MATE now steers toward a mate in sight, else toward its own kind, else straight on (long straight runs
  are the efficient search for what is out of sight; Bartumeus et al. 2005). And the share of its reserve a body needs
  before it breeds is a gene (B_BREEDE, READY_LO 0.25 .. READY_HI 0.9; it was a fixed 0.6): selection can find income
  breeders that breed thin and often, or capital breeders that wait. "breeding condition" is a trait in the charts.
- *Diets differ again, and there are more ancestors.* The staple diet given to every ancestor (grass on land,
  plankton in water) put them all in one niche; it is removed, as the viability test already makes every ancestor a
  body its range can feed. ANCESTORS_LAND 10 and ANCESTORS_WATER 6 (16, the most the clade slots allow; was 6 and 4):
  most random ancestors fail, and a prehistory of 10 left 1-4 lineages to found the world.
- *Song is learned when young.* Matching remembered tutors reinforced calling for life, and the match's running mean
  started at 0, so every early call was rewarded: voiced animals came to call on ~80% of steps, costing ~20% of
  basal on top of the big brain's 13-17%. The match now reinforces only in the sensitive period (IMIT_AGE gene), as
  song practice does in young songbirds, and its running mean starts at the first match. (Calling still rises to
  ~50-65% of steps by plain reinforcement. Energy spent on strikes, calls, moving and heat now counts against the
  reward as energy eaten counts for it (R_SPEND; animals learn by net gain), but a call is too cheap against the
  reward's swings for that to lower it measurably: only selection against costly callers can.)
- The world report (headless) ends with the VOICES table, as the prehistory report does.
- *Calmer newborns.* Every discrete action of a newborn brain started near 5% a step (FOUNDER_BIAS -48), so naive
  animals struck whatever was in reach every ~20 steps and founding groups killed each other (one lineage: 30% of its
  deaths by its own kind). All actions now start near 2% (FOUNDER_BIAS -64), the same for every action; the instincts
  (eat, drink, mate, flee...) were made stronger in the same change, at full need about 97% instead of 88% (KIT_W 120;
  the user: base instincts should be very strong, "like primary colours"); evolution can change any of them.

**Size classes with floors** (the user, 2026-10-30): a body-plan gene B_FLOOR puts each lineage in one of FLOOR_CLASSES = 4
size classes whose smallest body is 0.1, 1, 3 or 10 (Body.Floors). Mass still evolves continuously above the floor, up
to MASS_HI. The class gene is copied exactly (founder variation and ordinary mutation leave it alone) and steps up or down
one class with probability FLOOR_MUT = 5e-5 per birth. Ancestors are spread over the classes (ancestor a gets class a mod 4),
their mass log-uniform from the floor up to max(4 x floor, 20 on land or 5 in water). The console's AGENTS record carries
the body's actual size; the split test compares actual sizes.

**Big brains fixed** (2026-10-30, found after the user saw the big-brained founders kill each other at once): the
brain size was stored in a byte, so a 256-unit brain ran with 0 memory units while paying for 256; it is now a ushort.
And newborn brains are calm at any size: founder output weights from memory unit h are scaled by BRAIN_START/h beyond
BRAIN_START, and memory-layer weights from unit h by sqrt(BRAIN_START/h), so a 256-unit newborn strikes about as rarely
as a 24-unit one (8.1% vs 7.4%) instead of 11%. Its extra units start almost silent; learning or evolution brings them in.

**Changes in Revision O: climate and body heat** (the user, 2026-10-30: "I don't want you to specifically create
biozones ... come up with a logical system for how the world works ... simulate some physics ... and then this should
get created by itself ... heat tolerance genes, those should theoretically evolve through the simulation ... It's not
supposed to be predetermined." Designed with the physics designer. There are no biome types and no tolerance genes:
temperature follows from geometry and physics, plants, soil and ponds answer to it through response curves from the
literature, and an animal's cost of living in it follows from its body's size, fur and appendages; evolution varies only
those physical properties. Code: `Climate.cs` (climate and `Heat`), `Fields.Row`, `Ponds.Update`, `Resolve` step 7.)
- Temperature per cell (°C), written by FIELDS every step: latitude on the torus θ = 2πy/FW, sin(lat) = sin θ (equator,
  north pole, equator, south pole; the hemispheres' seasons are opposite by the sign of sin θ);
  T_phys = T_EQ − DT_EP·sin²θ + A_SEAS·sinθ·sin(2πt/year)·Cont − LAPSE·h; T = T_REF + Climate·(T_phys − T_REF).
  T_EQ 27 °C, DT_EP 40 K (North 1975), A_SEAS 20 K, LAPSE 6.5 K/km (ICAO), h = height above the lakes x H_RELIEF
  (3,000 m), continentality Cont = 1 − 0.75·exp(−distance to water / 6 cells) (Conrad 1946). Height and Cont are fixed at
  Terrain.Build (`World.TLapse` holds Climate x LAPSE x h); a row costs two sines, a cell one FMA. `World.Temp` holds the
  air temperature everywhere; water under it is at least T_WATER_MIN = 0.5 °C (deviation: the designer floored water
  cells' temperature itself, which would leave no frozen water to show; the floor is applied where water temperature is
  used). No daily cycle: a step is hours, and the day-averaged sun is in SOLAR. Temp is state walked by the checkpoint
  (and rewritten every step), so a resumed run is exact.
- Soil moisture: W = clamp(MoistBase·(1 + A_SEASON·season)·(1 + rain)·E(T)^−0.5 / Z), E = e_s(T)/e_s(T_REF) (Tetens;
  Budyko-style). Deviation: Z is not the land mean of E^−β but the value (by bisection at Build, over 8 phases of the
  year) that makes the land's mean clamped moisture equal to that with no climate; with the plain mean the land was 2.2%
  drier (the clamp at 1 loses the cold side's surplus). In a fields-only test the land is within 2% of Climate = 0.
  Ponds use the same factor in their 'full' test; a pond whose air is below 0 °C is frozen and cannot be drunk (lakes
  and rivers can still be drunk under ice: animals break ice or eat snow).
- Plants: grass, shrub and tree growth x Yan & Hunt (1999) beta curves (0, 25, 45), (2, 24, 45), (5, 22, 40) °C, 1 at
  T_REF, at most 1.1 (the tree minimum is the Körner & Paulsen 2004 treeline); algae and plankton x 1.066^(T − T_REF)
  at the water's temperature (Eppley 1972), nothing under ice; decomposition x 2^((T − T_REF)/10) (Lloyd & Taylor 1994).
  512-entry tables of 0.25 K from −60 °C. Initial vegetation is still from moisture only: it re-sorts itself.
- Nature.Climate (0..2, default 0.6): setup page (Nature, "Climate strength"), `--climate`, presets (0.6; Big 0.8),
  saved in checkpoints and world saves. Climate = 0 skips every rule above and the heat budget: a fields-only year is bit
  for bit Revision N's (tested against the hash of Revision N's code).
- Body heat (`Heat`, RESOLVE step 7): an endotherm at T_BODY = 38 °C with no heat stored from step to step (a step is
  longer than the thermal time constant of bodies up to tens of kg, Bakken 1976). Core area A = M^(2/3); fur i (gene
  B_INSUL) works at i·min(1, M^(1/3)); appendages a (gene B_APPEND). C_min = K_COND·C_BASAL·[A/(1+R_INS·i) + 0.15·a·A],
  C_max = K_COND·C_BASAL·[3·A/(1+R_INS·i) + a·A], K_COND = 1/16 per K (a bare M = 1 body is neutral down to 22 °C), R_INS
  4; in water x4 and fur keeps half. Operative temperature: the air (or the water) plus, out of water, Climate x SOLAR x
  (1 − 0.8·tree − 0.4·shrub) / (1 + 0.5·R_INS·i). Deviation: the designer's solar term had a factor A_c, which would
  make a big body hotter in the sun by many K; absorbed and lost heat both scale with area, so the rise of operative
  temperature is size-independent; SOLAR (unvalued in the design) = 6 K, a day-averaged load (Campbell & Norman 1998).
  Heat made H = basal + movement. If C_min·ΔT > H it makes the difference, up to SUMMIT = 5 x basal (Hinds et al. 1993),
  paid from energy; if C_max·ΔT < H it evaporates the difference, up to EVAP_MAX = 3 x basal (none under water), which
  adds thirst EVAP_REL x C_THIRST x Thirst x X / (C_BASAL x M) on land, not reduced by the water-economy gene. What the
  caps cannot cover builds heat strain (0.2 x shortfall in basal units, less 0.05 a step); at 1 the animal dies of cold
  or heat (new causes; Cause.N 10). Fur costs COST_INS = 0.06 x basal and 10% more movement cost; appendages 0.03.
  Founders start with i = 0.15, a = 0.3 (thermoneutral ~14-31 °C at M = 1) plus founder variation.
- Senses and steering (F.N 64 -> 66, P.N 39 -> 41, NSTEER 22, FIRST_ACTION 25): HEAT_NEED = tanh(net heat need /
  basal), last step's (deviation: the shortfalls beyond the caps are included, so an animal past its limits feels it
  more), AIR_T = clamp((T_op − 38)/30, −1, 1); WARMER and COOLER steer along the raw gradient of the operative
  temperature over the 4 neighbour cells (shade and water included). No reflex links them: using them is learned or
  evolved. The translator has phrases for both senses and both verbs.
- Display: field layers `temp` ((T + 40)/90) and `frozen`; frost on frozen land and pale ice on frozen water, and a
  "temperature" button that colours the ground by air temperature. AGENTS record byte 25 carries fur and appendages
  (4 bits each). Telemetry: traits fur, appendages, operative temperature (NTRAITS 29); spend.heat; deaths by cold and
  heat; land temperature mean, min, max and frozen share. The prehistory report has a CLIMATE section per branch.
- A slot holds 209,675 bytes (World.BytesPerSlot; 202,541 in Revision N): the bigger brain layout (2 senses, 2
  primitives) and 5 floats. Gene bank version 5, checkpoint version 5, AGENTS format 5 (byte 25 used).
- What it means for the physics as calibrated: with A = M^(2/3) against basal M^0.75, the lower critical temperature
  hardly depends on size (bare: ~25 °C at M = 0.1, ~18 °C at M = 20); fur is the main lever, and a summit of 5 x basal
  covers air far below freezing, so cold costs energy (and starvation) long before strain kills. In a fields-only run,
  trees spread less on cold high ground when the soil is equally wet, but on the real map, over a few years, drought
  and fire dominate tree dynamics and cold ground is wetter, so tree cover follows moisture more than temperature at
  first (trees live ~5 years at D_T; a treeline takes decades).
- Result of a short check (8,000 steps, 2 branches x 12,288 slots, region map, seed 1), same code at three climate
  strengths. Climate 0.6 (default): land air -22 .. 25 °C (mean 6 °C; a third of the land has an annual mean below
  0 °C, as the torus has two polar bands); branch 0 died out entirely, branch 1 ended with 243 organisms (two land
  omnivore clades). Deaths by cold 876 and 625 in two water clades (an immersed endotherm loses heat 4x faster at
  0.5-10 °C water) and 2-4 in others; no deaths by heat; dead clades had spent 50-245% of their basal energy on heat.
  Climate 0.3: land -1 .. 22 °C, 670 and 944 organisms, 28 cold deaths (a water clade). Climate 0 (Revision N rules):
  728 and 4,106. Tree cover was ~0.05-0.07 in every band below 20 °C and 0.30 above (moisture- and fire-driven over 8,000
  steps); grass rose from 0.07 (< 0 °C) to 0.54 (15-20 °C). No clear difference yet in mass, fur or appendages between
  the coldest and warmest third of living animals (fur 0.18-0.19 everywhere; founders 0.15, so it rose slightly).
  At the default the climate is a strong filter on founders; whether 0.6 should stay the default, and whether
  water-living bodies should be allowed lower body temperatures (ectothermy as a physical gene), are open questions.

**Home ranges** (the user, 2026-10-30): each founding lineage starts in its own home range (radius HOME_R of the map
side), the one of HOME_TRIES random sites farthest from the homes already taken; founding from a gene bank does the
same per clade. Before, every ancestor's founder groups were scattered over the whole map, so every species met every
other from the first step.

**Changes in Revision N: richer voices** (the user, 2026-10-29: the voices are "clearly too limited"; can languages
evolve new, unknown words? Designed with the communication designer. Nothing gives a sound or a sequence a meaning, and
nothing rewards communicating; the only intrinsic signal is matching the *form* of what elders say, through a gene
evolution can drop.)
- Utterances (`Voice.cs`): in one step a voiced animal makes every call it fired, up to MAX_SYL = 3 distinct sounds,
  in the order of how strongly each is intended (highest gain first; Engesser & Townsend 2019). A fired sound left out
  of the utterance counts as not made for learning. `World.Call` holds 3 bytes per slot. An utterance of n sounds costs
  C_CALL x basal x (1 - CALL_SYL + CALL_SYL x n), CALL_SYL = 0.5. There are 8 + 56 + 336 = 400 utterance types.
- Range is the caller's: a call carries HEAR_R x voice x M^(1/3) (HEAR_R = 16), at most MAX_SIGHT = 32, so the 5x5
  hash neighbourhood (HCELL 16) holds every caller a listener can hear. BUILD sorts this step's callers by hash cell
  (`CallStart`, `CallSorted`) and a listener scans all of them, with no neighbour cap. Loudness is 1 - d / range.
  Before, hearing used the listener's sight radius and the 64-neighbour cap.
- The focal caller: each listener attends to one caller, the loudest that looks alike (Body.LooksAlike), else the
  loudest of any kind (the person included); it stays with the same animal while that one goes on calling at least
  FOCAL_KEEP = 0.7 as loud as the loudest of its class (Bee & Micheyl 2008). 22 new senses (F.N 42 -> 64): FOC_POS_k
  (1, 0.67, 0.33 for sound k 1st, 2nd, 3rd in the focal utterance; 0 absent), MY_POS_k (the same for its own last
  utterance, an efference copy), FOC_KIN, FOC_MOTHER, FOC_ELDER ((focal age - own age) / own lifespan), FOC_SIZE (tanh
  log mass ratio), FOC_FACING (cosine of the focal's heading toward it), FOC_LIKE_ME (positions where the focal
  utterance equals its own last one, over the longer length). HEAR_0..7 are kept. A new steering primitive,
  FOLLOW_GAZE (P.N 38 -> 39, NSTEER 20, FIRST_ACTION 23), turns the way the focal caller faces (gaze following).
  The person's voice is heard within HEAR_R as a stranger: not kin, not mother, no age, size or gaze; the console's
  `speak` command takes up to 3 sounds.
- Vocal learning of form (Doupe & Kuhl 1999; Brainard & Doupe 2002; Tchernichovski et al. 2001; Kirby, Cornish &
  Smith 2008): two body genes beside B_VOICE (before the MHC block, which stays last): B_IMIT (0..1) and B_IMIT_AGE
  (sensitive period, IMIT_AGE_LO = 0.1 .. 1 of lifespan). The voiced ancestor starts with IMIT 0.5 and IMIT_AGE 1; every
  other ancestor with 0. During its sensitive period a learner counts the syllable pairs (start -> 1st, 1st -> 2nd,
  2nd -> 3rd) of what its focal caller says when that caller is grown, alike and older (`World.Tmpl`, 9 x 8 bytes per
  slot, saturating; halved every TMPL_HALF = 1000 steps, staggered by slot). On each step it makes an utterance, its
  match is the mean over its pairs of count / largest count, less its running mean (IMIT_RBAR); it is stored with the
  learning block (`HistV`) and `Brain.ApplyBlock` uses eta x (A + IMIT x V) on the 8 call rows only, in the same pass.
  With IMIT = 0 the dynamics are bit-identical to having no vocal learning (tested, `EvoSettings.VocalLearning`). The
  genes have no upkeep cost of their own (the learning uses the brain the animal already pays for), so where matching
  elders is of no use they drift; deviation from the designer's note, which named no cost either way.
- In a fixed two-animal test (a juvenile hearing an elder say [3, 5] for 2,000 steps, no other reward), the learner
  says [3, 5] about 150 times in its last 500 steps against 0 for the same brain without the gene, but it says [3]
  alone more often still: the pair score rewards starting right as much as finishing right. With the highest learning
  rate it locks onto the first partly matching form (e.g. [3, 2]); the learning rate is an evolving gene.
- The translator (display only): per clade and utterance type, count, first and last step made and the step it
  reached MIN_UTT = 30, the listeners' answers (from their focal caller of the same clade), and the sums of the
  caller's senses and of the listeners' gains. The sense and gain sums per type are kept for NTRACK = 4 clades (the
  first to call; a slot is reused when its clade dies out), ~2.7 MB per world in doubles. The panel lists the 24 most
  made utterances and any NEW word (reached n >= 30 within the last 2,000 steps and has a clear reading), keeps the
  per-sound view (each sound wherever it comes in an utterance), draws the rank-frequency of utterance types on
  log-log axes beside a 1/rank line, and gives a dialect measure: the Jensen-Shannon divergence between the last
  utterances of the clade's western and eastern halves, beside the same between odd and even slots (what sampling
  alone gives). A typed word picks the best utterance (or single sound); "3 5" sends those sounds. The 3D view shows
  an utterance as a stack of coloured rings, one per sound. The prehistory report has a VOICES section.
- A slot holds 202,541 bytes (World.BytesPerSlot; measured 202,435), 16.6 KB more than Revision M's 185,914 (Revision M's
  note said ~155 KB; the measured value was ~186 KB). Gene bank version 4, checkpoint version 4, AGENTS format 5
  (26-byte records: byte 24 holds the 2nd and 3rd sounds).
- Result of a short check (8,000 steps, 2 branches x 12,288 slots, region map, seeds 1 and 2): the voiced lineage
  died out in all four branches, as it does with Revision M's rules on the same seeds (founding bottleneck: mostly
  starvation and predation). Before dying it made 25,000-38,000 utterances of 300-360 kinds, 53-64 of them at least 30
  times; the most made were single sounds, and the clearest reading of each was "it said this sound last time" (a
  caller repeats itself), not anything about the world. Nothing in these runs is a language yet.

**Changes in Revision M: far bigger brains, and food that limits numbers** (the user, 2026-10-29: "bigger brains
far bigger"; "food levels and world size should control population ... natural forces that don't let it hit max
capacity"; the population laws reviewed by the ecologist)
- Brains have up to G.NH = 256 memory units (36 before). The brain-size gene is on a log scale (BRAIN_MIN..256), so a
  mutation changes a brain by a share of its size. Ordinary ancestors start with up to BRAIN_START = 24 units,
  big-brained ones with BIG_BRAIN_UNITS = 128. Only the units in use are computed (exactly the full rule, since unused
  units are always 0); the memory layer uses AVX2; mutation finds mutated bytes by geometric skips; the console probe no
  longer sends the memory weights. A slot holds ~155 KB; a world holds at most World.MaxSlots (~25,000).
- Brain tissue costs COST_BRAIN x C_BASAL x units / BRAIN_REF whatever the body (energy per neuron is about constant,
  Herculano-Houzel 2011): a big brain needs a big body. The 4x ceiling on the basal multiplier is gone.
- Each food has a most-digestible share (Pools.DigMax: grass and browse 0.5, root 0.6, seed 0.75, fruit 0.8, nectar 0.95,
  meat and blood 0.9, carrion 0.85, dung 0.3, litter 0.25, wood 0.15, detritus 0.4; Van Soest 1994, Robbins 1993); a
  diet gene share is a fraction of it. Before, almost any food could be digested at 0.95.
- Metabolism against lifespan: C_BASAL and C_MOVE x2 (0.004), C_DIGEST 0.025 and C_BITE 0.06 (most intake ~5x basal,
  Hammond & Diamond 1997). The ecology review asked for x4-5; at x4 every founder population starved before brains
  could evolve (random brains eat too rarely), so x2 is the tested value.
- MASS_LO = 0.1 (0.01 gave tiny bodies 32x the numbers from the same food; smaller consumers are the fields), and
  newborns and eggs are at least NEWBORN_MIN = 0.01.
- Founders per ancestor are set by the food in their home range (Revision P; FOUND_FILL, FIELD_METAB), at most the setup's limit.
- Voices: 8 call primitives (CALL_0..7) and 8 hearing senses (HEAR_0..7: how loud each sound was within the
  perception radius last step, from any direction), and two steering primitives, toward and away from the loudest
  call. No sound has a built-in meaning, and nothing rewards calling: a call costs C_CALL x basal and is heard by
  everyone, predators included. Only the most intended call is made in a step. Founders' call outputs start at
  FOUNDER_BIAS_CALL so that calling as a whole is as rare as one primitive.
- One voiced, far smarter lineage (the user: "ONE creature ... WAY smarter than the rest, like a human species"):
  a voice body gene (B_VOICE, 0..1; COST_VOICE of basal) sets how loud calls are; below VOICE_MIN a call makes no
  sound. Per branch, BIG_BRAINS = 1 big-brained ancestor, on land, starts with BIG_BRAIN_UNITS = 256 units and a full
  voice; every other ancestor starts mute with an ordinary brain. Voice is a gene, so another lineage could evolve one.
- The translator (console, display only; Meaning.cs): per clade and sound, the sum and sum of squares of each of
  the caller's 42 senses when it is made, against the same over the clade's usual sample (every 4th step per
  organism), and of each primitive's gain in listeners of the caller's clade on the step they hear it, against the
  clade's usual gains. A sound "means" the senses that differ most from usual: standardised difference
  d = (call mean - usual mean) / pooled spread (spread floored at 0.03), reported when at least 25 calls, |d| >= 0.25
  and |difference| / standard error >= 3, each sense and direction in plain words ("food here", "alone", "something
  is coming closer"). Listeners' answers are the gains that rise most, as plain verbs, beside 5 coarse categories
  (came, fled, froze, called back, ignored). The 7 fixed contexts of the first translator (danger, food here, mate
  near, hungry, alone, thirsty, young lost; 1.5x lift) are kept only as a fallback. The person can type a word; it is
  matched by keyword to a sense direction, the translator picks the sound with the highest d on it in the chosen
  clade, and the person clicks where to send it. The call is heard like any other on the next step; it is not an
  organism, and no audio is recorded.
- Defaults for a 16 GB computer: 2 branches x 16,384 slots, a world of 8,192 slots. Archive 16 and bank 48 genomes per
  clade. Gene bank version 3. Result: a 512-unit region settles at about 1,500-2,900 animals per branch, limited by food,
  with most ancestor lineages lost in the first years (a founding bottleneck).

**Changes in Revision L: predators that can win** (the user, 2026-10-29: "less animals, more evolution, more
demonstrated violence ... the animals should actively be terrorizing each other and evolving to win"; ecology review)
- A meat-eater digests carrion at SCAVENGE = 0.8 of its meat skill, so a hunter can return to its own kill (before,
  what did not fit in its gut went to a pool only carrion-specialists could eat). Almost all carnivores scavenge.
- Ancestors' armour and weapons start in the same random range (armour started ahead, so strike genes drifted down).
- Ancestor body mass is log-uniform over land 0.3-20, water 0.1-5 (was 0.3-2, 0.1-1): fewer, bigger animals can
  be selected from the start.
- Only a death counts as a kill (blood meals were counted as kills).
- The live world mutates at WorldMut (default 3) x the prehistory's rate: time compression for a small valley,
  not ecology (setup: Evolution speed in your world). Pet valley preset: plants x0.6, seasons x1.6, droughts x2.
- Splits are visible forms (§7.9): every SPEC_EVERY steps a clade is split when its visible traits (size, eyes,
  armour, weapons, legs, fins, wings, and pairs of each) form two clusters with a gap of SPLIT_GAP pooled SDs and
  some trait differs by SPLIT_VIS visible units (half again the mass, 0.15 of an organ, or a pair). The new clade is
  a subspecies while the two forms are still more than half fertile (their mean body genomes, as Mate judges), else a
  species; it keeps its parent's hue, light against dark. The old rule (half-infertile across all body genes) needed
  thousands of generations and never fired. Clades are labels: breeding is decided by genes alone.
- Display only: every strike is recorded (Telemetry.Fight: miss, wound, kill, blood bite, drained, egg) and shown
  in the console as flashes, blood stains and a fight log; the camera starts close over the biggest group.

**Changes in Revision K: a small world you can follow, and water you can live without** (the user, 2026-10-29:
"a personal intimate simulation, like following a wolf pack and their prey in a valley"; the water rules
reviewed by the ecologist)
- The map size is a setting (Map: valley 256, region 512, continent 1024 units), and the world may be smaller
  than its prehistory: evolve on a region, then watch a valley. Founding a world from a bank scales with the
  map's area. The world runs at 3 steps/s unless you choose otherwise, and a year is T_SEASON = 3,000 steps.
- The strength of nature is a setting (Nature: plants, seasons, season length, droughts, fire, disease,
  thirst, water, ponds), multipliers on the documented constants; 1 = the law. Saved in every checkpoint.
- Hunger senses prey: an animal that can digest meat or blood sees the value of the animal in reach as food
  (FOOD_HERE, CLIMB_FOOD), and EAT on prey is a strike. Before this hunger pointed only at plants.
- Ancestors' diet preferences are sparse and random: good at 1-3 of all 15 foods, poor at the rest. Revision P: a
  guaranteed staple (grass on land, plankton in water) was tried and removed, as it put every ancestor in the same
  niche; an ancestor is instead a body its home range can feed. Drain or kill is a share (PDrain), not a switch.
- The laying instinct: ready to breed → LAY, for cloners and egg-layers as the mating instinct is for the sexual.
- Water (§6.3, ecology review). Food holds water (FoodWater per pool; live plants as wet as their ground),
  and digestion makes metabolic water (MET_WATER); both relieve thirst by K_FOODWATER per unit body mass.
  SOIL_WATER now counts only damp ground. Thirst scales as M^-0.2. A new body gene, water economy (B_WATER),
  cuts water loss by up to WATER_ECON at COST_WATERECON of basal metabolism. Seasonal ponds (§10): land
  basins away from lakes hold water when the season and the year's rain are wet enough (POND_FILL) and dry
  out in the dry season and in droughts; drinking and SEEK_WATER use the distance to lakes or full ponds.
  Before this every land animal had to stay near the lakes, and the land between was empty.

**Changes in Revision J: the mating instinct and big-brained ancestors** (designed by the user, 2026-10-28)
- The starter kit (§7.7) gains the mating instinct: ready × a partner in reach → MATE, ready × no partner
  near → SEEK_MATE, at KIT_W like the survival reflexes. "Any animal that is sexual, that's literally the top
  of their goals." The drive is full in males, DRIVE_FEMALE in females and DRIVE_HERM in hermaphrodites. Before
  this, sexual ancestors had no instinct to breed while cloners bred whenever a random brain fired LAY, and in
  the user's run every sexual land ancestor crashed within 500 steps.
- Brains may have up to G.NH = 36 memory units (12 before). In each prehistory branch, BigBrains = 2 of the 10
  ancestors (one on land, one in water) start with BigBrainUnits = 36; the others start with a random size up
  to BRAIN_REF = 12. Settable (--big-brains, --big-brain-units). Brain cost per unit is unchanged (COST_BRAIN
  per BRAIN_REF units), so a 36-unit brain costs three times a 12-unit one; brain size is a gene and evolves.

**Changes in Revision I: the ecology review's second list** (reviewed)
- Cost of transport ∝ M^0.7·speed (Taylor et al. 1982); flight costs FLY_COST = 0.5 of running per
  distance (Schmidt-Nielsen 1972).
- Wings too small to fly still speed a runner by GLIDE·wings (wing-assisted running, Dial 2003).
- Hermaphrodites mate with either sex. Siring costs a hermaphrodite C_SIRE of its own litter's cost
  (Charnov 1982).
- Speciation needs a gap of SPLIT_GAP pooled SDs along the axis between the two centres, and centres at
  least SPLIT_MIN_D apart.
- Matching-alleles parasites (§9.6): every organism has NMHC = 4 recognition genes, drawn independently
  per founder (hyper-polymorphic, like MHC). A strain carries a key and infects a host with probability
  × exp(−rms(key − host)²/MHC_W²). A key is copied from the infecting contact and mutates by MHC_MUT; a
  strain from the reservoir starts from its first host's own type. Recognition genes count neither for
  fertility nor for speciation. Rare host types escape, which can favour sex (Hamilton 1980).

**Changes in Revision H: rules an ecology review found biased** (each was pushing toward asexual
cloners and egg-eating, or made hunting unreachable)
- Genetic distance is root-mean-square per gene, for fertility (D_COMPAT) and kin (KIN_LOOK): normal
  variation within a population is fully fertile and looks alike (§4, §7.9).
- Strikes wound in proportion to force; a body's mass is its defence; an egg's shell is its armour;
  own-kind eggs are kin (§9.4).
- A mating costs nothing when there is no room for young. Males get a short refractory period
  (REFRACT); a female's cooldown starts only when she conceives (§8).
- Ancestors' non-plant diet genes start low but off the floor, so selection can act on them (§6.2).
- One brain per ancestor; founders vary around it (FOUNDER_W_SD), so crossover recombines related
  networks (§6.2).
- The report warns when the slot cap, not food, limited the world (on more than 1% of steps). Each
  prehistory branch has 49,152 slots so that food limits it first.
- Speciation needs the two centres to be at least half-infertile (SPLIT_MIN_D, RMS per gene).

**Changes in Revision G.1: variation to evolve from.** The first Revision G run showed no body change
at all: founders were near-clones and body genes shared the brain's per-byte mutation rate (0.018
body changes per birth). Founders now vary around their ancestor (§6.2) and body genes have their own
rate (§7.3).

**Changes in Revision G: no designed categories left in bodies, senses or breeding** (the user:
"where else have you [hand-fixed things]")
- Senses are raw (§4): the nearest and the largest other organism, by relative size, closing speed,
  weapons and diet. No "big", "small" or "carnivore" category. Kin = looks alike.
- Organs act smoothly (§6.3, §9.2): half a fin swims, a legless body crawls, flight follows wing
  loading. Leg, fin and eye counts, lifespan, development time, brain size, hermaphroditism, sex
  ratio and immunity are genes, each with a cost.
- Mating has no species check (§7.9, §8): fertility falls with genetic distance, so species separate
  gradually and hybrids are possible.
- Thirst depends on where an organism is, not what it is (§6.3). Pathogens evolve their virulence
  (§9.6).

**Changes in Revision F: open-ended evolution** (the user: "you set a small amount of rules and
circumstances emerge")
- There are **no designed species**. The 20-species roster is gone. Life starts from 10 random
  ancestors (6 on land, 4 in water), all eating the primary producers of their realm, because nothing
  else exists to eat at the start (§6.2).
- **Bodies come entirely from genes**, on absolute ranges with physical costs (§6.3): size, legs, fins
  and wings (which decide locomotion and realm), speed, sight and field of view, armour, weapons, diet,
  and life history (sexual or asexual, eggs or live birth, litter, newborn size, care).
- **Species form on their own** (§7.9): organisms mate only within their clade, and a clade whose body
  genes have split into two clusters with few intermediates becomes two clades.
- The console draws every organism from its own genes (§13). Sections below that describe designed
  species (roster, body plans, reproduction modes) are replaced by §6.2, §6.3, §7.9 and §8.

**Changes from Revision B** (after review with the user, see §15)
- The laws of the world were reviewed once for physical consistency and are now frozen. No law is
  adjusted to produce an ecological outcome; outcomes are observed and reported.
- Body traits evolve too, with costs (§7.5).
- Founders start in groups (§7.4). (Revision E: there are no colonists anywhere; every run is births only.)
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
   feeds the producers again. Bacterial decomposition closes the cycle without any animal. Which
   organisms exist to eat each pool is not designed: it is whatever evolution produces (§6.2).
2. **Bodies, not behaviours, and the bodies evolve.** Each organism's body is expressed from its own
   genes (§6.3): mass, organs, locomotion and realm, diet, armour, weapons, senses, life history.
   Nothing in the genome says what it should *do*, and nothing outside the genome says what it
   should *be*.
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
   for the long evolution before the world begins. The prehistory is births and deaths only too: no
   run ever adds an animal after its founders (§7.4).

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

## 4. Senses: the shared feature vector (34 entries)

These are identical for every organism (NR-201). Others are seen only through raw observable
quantities (NR-202), within the sight range R and field of view Φ. There are no designed
categories such as "predator", "big" or "prey": an organism senses how large, how fast-closing,
how armed and how meat-eating the others are, and its brain and selection decide what that
means. SCAN widens Φ to 360° and R by 30% on the next step. "Closeness" of an organism at distance
d is 1 − d/R. "Density" is tanh(Σ closeness). The neighbourhood examined is capped at MAX_NEIGH = 64 organisms
per step, taken in spatial-hash order.

**Kin** are others that look like this organism: their visible body genes (mass, legs, fins, wings,
armour, strike, leg and fin counts) lie within KIN_LOOK of its own (Revision G). Kinship is a
perception, not a species label, so it can blur and split as bodies diverge. An egg never looks
like kin.

An egg is judged by its genes like anyone else: an egg of one's own kind is kin and is left alone
(Revision H; before, eggs were never kin, so mothers struck their own clutches).

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

**Others (10–25)**. O1 is the nearest non-kin organism or egg; O2 is the largest non-kin in view.

| i | Name | Meaning |
|---|---|---|
| 10 | KIN_DENSITY | organisms that look alike |
| 11 | KIN_CLOSE | nearest of those |
| 12 | MOTHER_FAR | 1 − closeness of mother; 0 if none or adult |
| 13 | MATE_CLOSE | nearest READY organism that is a possible partner (§8) |
| 14, 20 | O1_CLOSE, O2_CLOSE | closeness |
| 15, 21 | O1_SIZE, O2_SIZE | tanh(ln(m_other / m_self)), −1..1 |
| 16, 22 | O1_APPROACH, O2_APPROACH | closing speed / own vmax, −1..1 |
| 17, 23 | O1_ARMED, O2_ARMED | tanh(strike_other · cbrt(m_other / m_self)) |
| 18, 24 | O1_MEAT, O2_MEAT | meat-eating share of the other's diet (a visible attribute) |
| 19 | O1_WEAK | vulnerability of O1: egg, juvenile, fatigued, low armour |
| 25 | OTHERS | density of all non-kin |

**Environment (26–33)**

| i | Name | Meaning |
|---|---|---|
| 26 | FOOD_HERE | value of the best digestible food in own cell: max over pools of ρ·eff·amount, normalised |
| 27 | FOOD_GRAD | strength of the gradient of digestible food value |
| 28 | IN_WATER | 1 if standing or swimming in water |
| 29 | WATER_CLOSE | closeness of the nearest water (distance map) |
| 30 | LAND_CLOSE | closeness of the nearest land |
| 31 | FEAR | scent of meat-eaters here, plus fire nearby |
| 32 | CROWD | total animal density, any species |
| 33 | SEASON | sin of the season phase at own position (−1 dry … +1 wet) |

Smelling digestible food (features 26–27, primitive CLIMB_FOOD) is physiology. A body's senses are
tuned to what its gut can digest, the way taste is. It says nothing about when to eat, or whether
eating beats fleeing; that is the brain's job.

## 5. Actions: the shared primitive library (28 entries)

Identical for every animal (NR-203).

**Steering primitives** each return a unit vector, or zero if their target is absent:

| p | Name | Vector |
|---|---|---|
| 0 | APPROACH_NEAREST | toward O1 (the nearest non-kin) |
| 1 | APPROACH_LARGEST | toward O2 (the largest non-kin in view) |
| 2 | SHADOW_LARGEST | toward O2 beyond R/2, tangential inside R/2 |
| 3 | FLEE_LARGEST | away from O2 |
| 4 | FLEE_NEAREST | away from O1 |
| 5 | INTERCEPT_NEAREST | toward predicted position of O1 |
| 6 | CIRCLE_NEAREST | perpendicular to O1's bearing, on the side away from kin |
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
| 21 | STRIKE | the nearest non-kin organism or egg within reach (a blood-drinker: the largest in reach) |
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

## 6. The energy cycle and the organisms

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

### 6.2 Ancestors (Revision F)

There is no roster. Each world starts with ANCESTORS_LAND = 6 land and ANCESTORS_WATER = 4 water
ancestors, each its own clade, with ANCESTOR_COUNT = 600 founders in groups. An ancestor's body is drawn
at random within the "simple" corner of the gene space (`Clades.AncestorBody`):
- **Size:** a small body (land: mass 0.3–2; water: 0.1–1).
- **Locomotion:** organs above the threshold for its realm (land: legs; water: fins); little armour;
  almost no weapons.
- **Diet:** random proportions of the primary producers of its realm (land: grass, browse, seed, root,
  fruit, nectar; water: algae, phytoplankton). Every other pool is near zero, because nothing else
  exists to eat at the start.
- **Life history:** sexual or asexual, eggs or live birth, litter, newborn size and care, all random.

Each founder has its own random brain plus the starter kit (§7.7), and the ancestor's body with
standing variation: each body gene ± FOUNDER_BODY_SD, as in any natural population (Revision G.1).
Revision H: all founders of an ancestor share its one random brain, each byte ± FOUNDER_W_SD; and an
ancestor's non-plant diet genes start between −110 and −70 instead of at the floor, so meat or carrion
eating can be selected. Diet genes map linearly to digestion, normalised to unit length (L2, Revision I):
a side food costs a specialist little, and an even generalist digests each food at about 0.41 against a
specialist's 0.95. (A linear map with a sum-to-one budget starved every ancestor in tests: 21 organisms
against 22,309 with L2 after 8,000 steps.) Hunters, scavengers, parasites, flyers and waders can only appear by evolution.

### 6.3 Bodies from genes (Revision F, NR-320)

The body genes (`G.BODY`, `G.NBODY` bytes) are mapped to absolute ranges (`Body.Express`).
Functional traits have physical costs:

| Trait | Gene → value | Cost / effect |
|---|---|---|
| mass | log range MASS_LO..MASS_HI | all of allometry (§9.1) |
| legs, fins, wings | organ size 0..1 | speed in each medium follows smoothly from the organs (Revision G): land v·(CRAWL + (1 − CRAWL)·legs), water v·SWIM_SPEED·(0.1 + 0.9·fins), air only if wings·WING_LIFT ≥ M^(1/3) (wing loading). Any organism may enter land and shallows; deep water needs 0.1 + 0.9·fins ≥ DEEP_SWIM. Settled only with almost no organ at all. Half a fin is half a fin. COST_LIMB·(legs·legPairs + fins·finPairs)/2 and COST_WING·wings·M^(1/3) basal |
| leg, fin, eye pairs | 1..4 | more leg pairs spread an armoured body's drag (÷√pairs); more fin pairs turn faster (FIN_TURN); more eye pairs widen the field of view (EYE_FOV); COST_EYE per eye pair |
| lifespan | ×/÷ LIFE_RANGE | COST_LONGEVITY·(k − 1) basal (body repair) |
| development | ×/÷ LIFE_RANGE | maturity time × k; lifespan × k^DEVEL_AGING (fast developers age sooner) |
| brain | BRAIN_MIN..12 memory units | units beyond it do not exist; COST_BRAIN·units/12 basal |
| hermaphrodite | sign | both sexes in one body: any two hermaphrodites can mate and both conceive; COST_HERM basal |
| sex ratio | 0..1 | the chance that a separate-sexed mother's young is a daughter |
| immunity | 0..1 | lowers infection and disease death by IMMUNE_PROTECT·imm; COST_IMMUNE·imm basal |
| speed, sight | ×/÷ BODY_RANGE_TRAIT | COST_SPEED, COST_SIGHT basal |
| field of view | FOV_LO..FOV_HI | narrow sees further: range × (1.6 − fov/360) |
| armour | 0..ARMOUR_MAX | defence; COST_ARMOUR_ABS basal; slows (ARMOUR_DRAG) |
| strike | 0..STRIKE_MAX | needed to kill or drain; COST_STRIKE_ABS basal |
| diet | 15 shares of DIET_BUDGET | a pool the realm cannot offer is never digested; blood > meat with strike drains (parasite habit) |
| sex | sign | sexual (mates, §8) or asexual (LAY: mutated copies) |
| live birth | sign | live (mother carries embryos, pays tissue during gestation) or eggs (yolk paid at laying) |
| litter | 1..LITTER_MAX | the mother's energy limits how many she makes |
| newborn share | log BIRTH_LO..BIRTH_HI | big young cost more and grow up sooner |
| care | 0..1 | §7.8 |

**Inherited appearance.** Sixteen genes before the final MHC block independently control trunk
length/width/height, head size, neck/tail length, limb placement/shape, fin/wing shape, pigmentation
and markings (see ICD record bytes 26–41). They use the normal body-gene inheritance and mutation
path. Pigmentation and markings can drift without a dedicated fitness bonus; morphology can
affect locomotion. Founder draws span the allowed appearance and locomotion-organ ranges without
land/water anatomical presets; habitat and feeding viability filter candidate ancestors.
Insulation and heat-shedding appendages also start with random values.

The renderer develops the same inherited genes into proportions, appendages, covering and
body-local markings. Locomotion labels choose animation, not anatomical proportions. Meat-eating
does not prescribe a colour, and a clade split cannot repaint the animals. Clade colours remain
identification metadata and an optional overlay. Shared primitive geometry, bilateral pairing
and bounded gene ranges remain developmental constraints, not unrestricted topology evolution.
The expanded layout uses gene-bank/checkpoint version 9 and AGENTS format 6.

Thirst rises only on land cells, by C_THIRST·M^EXP_THIRST·(1 − WATER_ECON·g)·(1 − SOIL_WATER·moisture),
where g is the water-economy gene: any organism in water draws water from it, and damp ground dries an
organism out more slowly (Revision G). Eating relieves thirst by K_FOODWATER·(bite·water content + MET_WATER·
energy digested/RHO_MEAT)/M (Revision K): green plants, fruit and prey are wet, seed and wood dry, and live
plants dry out with their ground. An animal drinks within 1.5 cells of a lake, river or full seasonal pond. Gestation and breeding cooldown scale as C_GEST·M^0.25 and C_COOL·M^0.25.

### 6.4 Energy-cycle closure

Bacterial decomposition (§10.4) returns every dead pool to nutrient, so the cycle closes with or
without consumers. Which pools get eaten by animals is an outcome, and the report shows it.

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
- **Mutation** (both cases): each byte changes with probability μ by round(2·N), or, with probability
  μ·0.05, by round(16·N). There are two rates (Revision G.1):
  - brain, plasticity and reflex bytes: μ = MUT_RATE = 1/G.LEN, about one change per newborn;
  - body genes: μ = MUT_BODY, about 0.4 changes per newborn. Each body gene stands for a whole
    polygenic trait, so it changes far more often than one brain byte.

  A body-gene step of 2 changes a trait by about 1%.
- Nothing about an organism is fixed by its clade: every body trait can evolve over its whole range.

### 7.4 Prehistory and the live world (NR-210, NR-212, NR-313)

**Births only, everywhere (Revision E).** Every world, a prehistory branch or the live world, is seeded
once at step 0 with founders (a world has to start somewhere) and is then births and deaths only. No
animal ever appears except by being born from a parent of its species. A species that dies out is
gone from that world. The **prehistory** (§7.6) is a set of such worlds run at full speed before the
world begins; it saves its best brains as a gene bank. The **live world** is seeded once from that bank.
Tests wipe out a species in a prehistory branch and in a live world and check that it never returns,
and that every animal created after the founders was conceived or laid.

History: earlier revisions topped species up with colonists during the prehistory (bred from an elite
archive, until step 400,000) and copied the best genomes between branches. Both put animals into a
world that were not born there, and both were removed at the user's direction. What made a
colonist-free run viable was fixing why animals failed: the innate starter kit (§7.7) and food-limited
growth (§15). In a births-only test, 300 Drifter founders grew to about 1,200 in 8,000 steps.

**Founders** are random, by the same rule for every species. Memory-layer weights are
round(U(−1,1)·FOUNDER_W)/16. Output weights are small, round(U(−1,1)·FOUNDER_W2)/16, and the output
bias column starts at FOUNDER_BIAS/16 = −3 (± FOUNDER_BIAS_NOISE): a **calm newborn brain**, in
which every primitive starts near 5% and useful actions must be learned or evolved upward. (The
first prehistory runs used large random weights, so every action fired about half the time, and the
energy spent on constant strikes and mating attempts killed most animals young.) The plasticity
genes are drawn uniformly from their ranges; body genes start at 0, the species body plan. There is
no hand wiring, and tests assert that founder genomes depend only on the RNG and that founder gains
start low.

**Groups.** Real populations are clustered, and a lone animal cannot find a mate (the Allee effect).
Founders therefore start in groups of GROUP_SIZE = 8 within GROUP_SPREAD = 10 units of one site.

**Elite archive.** Each species keeps a record of the ARCHIVE = 64 best genomes of its dead. A genome
enters it when its owner dies, if it beats the weakest. The score counts offspring that grew up most:
SCORE_MATURED·(offspring that reached maturity) + offspring conceived + 0.1·(age / lifespan). The
archive is only read when a gene bank is written. It never puts an animal into a world.

**Gene bank.** The file holds, per species, the elite archive and up to 96 living genomes. Format
(little-endian):
1. Header: magic `NRBK`, version u16, species count u16, genome length u16, capacity u32, step u64,
   seed u32.
2. Then for each species: count u16, followed by count × (score f32, genome bytes).

A run started with `--load-bank` seeds each species from its bank genomes (mutated) instead of
random ones.

### 7.6 The prehistory run: branches, settings, and the start of the world (NR-323 … NR-326)

**Where it runs.** When `Neuroreef.exe` starts without evolved brains (no `evolved.nrbank`, no shipped
bank), the console opens on the prehistory: it runs live, at full speed, and can be watched in 3D.
`--evolve` (Evolve.bat) runs the same thing without the console. `--prehistory` forces a new one.

**Branches.** BRANCHES = 3 separate worlds evolve side by side on the same land (the same terrain seed,
different run seeds), each on its own thread with BRANCH_CAPACITY slots. Nothing passes between them.
Separate worlds explore different solutions, and a species that dies out in one may survive in
another. Branches meet at a barrier every 500 steps, so they stay in lockstep; settings changes and
pauses happen only there, while no branch is stepping.

**Length.** PREHISTORY_STEPS = 500,000 steps, shown as YEARS_PER_STEP = 1,000 symbolic years per step
(500 million years). The years are a label for the console, not a time scale of the model: one step is
still one step of the same laws.

**Settings (EvoSettings).** The console's Prehistory tab can change, while it runs, the mutation rate
and size, the share of big jumps, and the length. They are clamped to sensible ranges. The default
mutation rate is MUT_RATE = 1/G.LEN per byte, about one mutation per offspring, as in real genomes. A
selection test (`SelectionImprovesAHeritableTrait`) shows that a gene which helps animals eat spreads
from 50% to over 90% of a births-only population within a few thousand steps. The same happened at the
old rate of 0.02 (~39 mutations per offspring), so that rate was unrealistic but was not what blocked
evolution.

**Is it evolving?** Telemetry tracks, per species, the mean starter-kit gene and the best archive score.
The prehistory report shows both at the first and last sample, with the generation reached.

**Standing.** For each species in each branch, over the last 32 samples (~1,000 steps):
- self-sustaining: alive, and births at least 90% of deaths;
- breeding: births, but fewer than deaths;
- dying: alive, with no births;
- gone: extinct in that branch.

**The start of the world.** At the end (or when "Begin the world" is pressed), the branches stop and
the world begins from the best branch as a whole: the one with the most self-sustaining clades, then
the most organisms (`Prehistory.BestBranch`). Its living clades and up to BANK_LIVING genomes each
(plus their archives) are saved as `evolved.nrbank` beside the exe, so the next start skips the
prehistory. The live world is founded from it, each clade in its current proportion, and from then
on there are births and deaths only.

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
every species extinct within 100,000 steps of the last colonist (colonists were removed later, §7.4). The life tables showed animals dying of
thirst and starvation beside water and food: evolution from random wiring never found the basic
survival skills in a few hundred generations, which real animals inherit from hundreds of millions of
years of evolution. So founders now carry an innate starter kit, the same for every species: eight
reflex genes set to KIT_W (+ the usual small random spread):
- hungry × food here → EAT
- hungry → CLIMB_FOOD
- thirsty → SEEK_WATER
- thirsty × at water → DRINK
- tired × safe → REST
- an armed, larger meat-eater near (O1 or O2: close × meat × armed × larger) → FLEE_NEAREST and FLEE_LARGEST
- young × far from mother → FOLLOW_MOTHER

At full need a kit reflex gives about 97% (7.5 against the calm bias of −4; Revision P: the user asked for base instincts "like primary colours"; it was 88%). This is the one designed
behaviour in the model, chosen by the user on 2026-10-28. Everything above it stays emergent: what to
eat, herding, hunting, mating, timing. The kit genes are ordinary genes, so evolution can strengthen,
weaken or lose them. All other reflex links start small and random.

### 7.8 Parental care (Revision C, NR-328)

Care is a gene (0..1) of every organism (Revision F; it was a designed mammal and bird rule before).

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

### 7.9 Clades and speciation (Revision F, NR-333, NR-334)

A **clade** is a lineage: one of the ancestors, or a group that split from another clade. It has a
generated name, a colour (each ancestor a distinct hue; a daughter a shift of its parent's), its
parent, its ancestor, and its birth and extinction steps (`Clades.cs`, up to K.CLADES = 32 at once,
since the console frame has 5 bits for it).
- **Reproductive isolation is gradual (Revision G):** there is no clade check on mating. A mating
  produces young with probability exp(−(d/D_COMPAT)⁴), d the RMS per-gene distance of the two body
  genomes (Revision H: the quartic follows Orr's snowball, incompatibilities growing with the square of the
  substitutions; normal variation stays fertile, independent lineages are not),
  so close relatives breed freely, hybrids between nearby clades are possible, and diverged lineages
  stop interbreeding by themselves. A hybrid takes its mother's clade.
- **Speciation:** every SPEC_EVERY steps, each clade with at least 2·MIN_SPLIT members is examined.
  1. Up to 512 members' body genes (/127) are clustered by a deterministic 2-means.
  2. If the centres are more than SPLIT_SEP × the within-cluster RMS spread apart and at least
     SPLIT_MIN_D apart, and both sides have at least MIN_SPLIT members, the smaller side becomes a new
     clade. This is the genotypic-cluster species concept: two clusters with few intermediates.
  3. Every member of the clade, eggs and embryos included, is assigned to the nearer centre.

  A split never creates an organism and does not change who can mate: it names divergence that has
  already happened.
- A slot is recycled only CLADE_RECYCLE steps after its clade went extinct.
- The prehistory starts each branch from its own random ancestors. The world begins from the best
  branch as a whole (§7.6); per-species choice across branches no longer applies.

## 8. Reproduction (Revision F, NR-311)

- **Sexual organisms** reproduce with MATE (Revision H: only if there is a free slot; the father then
  rests REFRACT × his cooldown, the mother enters her cooldown only if she conceives; a male is ready
  when he can afford the mating and keep 15% of his reserve): two ready organisms in reach, both sexual, with the same
  sex system, and of opposite sex unless both are hermaphrodites. Both pay the mating cost. With
  probability exp(−(d/D_COMPAT)⁴) (§7.9) the mother conceives a litter of crossovers; two
  hermaphrodites both conceive. A separate-sexed mother's young are daughters with the probability of
  her sex-ratio gene.
- **Asexual organisms** reproduce alone with LAY: a litter of mutated copies, at no partner cost. This
  saves the two-fold cost of sex but loses crossover.

For both, each young costs F_BIRTH·Emax-at-birth of energy. An egg also carries its whole tissue at
laying; an embryo's tissue is paid by the mother over the gestation (her energy must stay above 0.1
Emax or she loses it). The mother stops when her energy would fall below 0.15 Emax, so litter size is
limited by condition. Eggs are laid where the mother is (her realm). A hatchling that cannot live there
because its genes differ moves to the nearest allowed cell, or dies.

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
| movement cost | C_MOVE·C_SPEED·M^EXP_MOVE·speed (cost of transport ~M^0.7 × speed, Taylor et al. 1982; × FLY_COST flying, × (1 + INS_MOVE·fur)) |
| fatigue | +c_F·g_SPRINT·speed/vmax; −c_rec (×3 resting) |
| thirst | +c_θ·(1 − SOIL_WATER·moisture) per step on land cells; 0 in water |
| growth | a juvenile spends min(energy above GROW_RESERVE·Emax, C_CARC·M·(1 − Birth)/A_m·ρ_meat/GROWTH_EFF) per step on tissue and matures on reaching adult mass (Revision D, §15) |
| reserve | Emax = C_EMAX·M with C_EMAX = 2: a stored reserve of the same order as tissue energy |

### 9.2 Movement and realm

- The heading turns toward the desired heading by at most the turn rate.
- A move into a cell outside the stage's realm is refused. The animal stays where it is and turns by
  ±90° (hashed).
- Realms (Revision G): every mobile organism may move on land and in shallow water; deep water needs
  enough fin (DEEP_SWIM); a flyer goes anywhere. Its top speed depends on the cell it is in: in the air
  for a flyer, otherwise its land speed on land, its water speed in deep water, and the better of its
  water speed and 0.6 × its land speed in shallows.
- Settled organisms have speed 0.

### 9.3 Gut, digestion and intake (NR-302)

- **Choosing the food:** EAT takes the pool with the highest ρ_k·eff_k·amount in the cell.
- **The bite:** b = min(c_bite·M^0.75, amount, Gcap − Gut).
- **Into the gut:** Gut += b, and GutEnergy += b·ρ_k·eff_k. The undigested part, b·(1 − eff_k),
  becomes DUNG.
- **Digestion:** each step, d = min(Gut, c_D·M^0.75) is digested, moving the proportional share of
  GutEnergy into Energy.

A full gut blocks EAT, so the intake rate saturates (Holling type II) from handling time alone.

### 9.4 Strikes, kills and parasitism (NR-304)

**Target (Revision H):** the nearest organism or egg within reach that does not look like kin (§4); a
blood-drinker picks the largest in reach. Kin, own-kind eggs included, is struck only when no other kind
is in reach, so cannibalism stays possible but is never forced.

**A hit lands with probability:**
```
power   = strike_a · M_a
defence = M_v · (DEF_BASE + armour_v) · (juvenile_v ? JUV_DEFENCE : 1)    an egg's shell is its inherited armour
p = P0_STRIKE · power/(power + defence) · (1 + 0.8·FATIGUE_v) · (1 − 0.5·SCAN_v) / (1 + c_conf·KIN_DENSITY_v)
```
A body's own mass is its first defence: a small striker rarely hurts a large animal (Carbone et al. 1999).

**Cost:** an attacker with a target pays c_strike·M_a. With no target, STRIKE does nothing.

**Outcomes:**

| Case | Result |
|---|---|
| Hit | The victim loses C_DAMAGE·power energy and feels pain. It dies only when its energy reaches 0 (cause: killed-by-attacker-clade). Then the attacker eats immediately as MEAT, up to its gut space; the rest becomes CARRION (DETRITUS in water). |
| Parasite attacker | It drains BLOOD, min(c_drain·M_a, host energy). The host dies only if its energy reaches 0 (cause: parasitism). |
| Miss | The victim's PAIN rises. If the victim's own weapon power strike_v·M_v exceeds the attacker's, the attacker loses c_retal·(difference) energy; eggs and unarmed bodies do not hit back. |

### 9.6 Disease (Revision C, NR-329; Revision G)

*Superseded by Revision R (pathogen genomes, within-host dynamics, quorum sensing, reservoir; see the note at the top
and Pathogens.cs). The text below describes the old model.*

Pathogens spread by contact between organisms that look alike (kin, §4: within the sum of their reaches,
counted in DECIDE). Each infection carries its own **virulence** v.
- **Infection:** per contact β = DIS_BETA·1.5·v/(0.5 + v)·(1 − IMMUNE_PROTECT·imm), v the mean
  virulence of the infected neighbours; p = 1 − (1 − β)^contacts, plus rare spillover DIS_SPILL from the
  environment, which brings a strain of v = 1.
- **Evolution of the pathogen:** a new infection takes the strain it caught and mutates it,
  v' = v·exp(VIR_MUT·N(0, 1)), clamped to 0.05..10. A more virulent strain spreads better per contact
  (saturating) but costs and kills its host more, so the virulence that wins is an outcome.
- **Course:** infection lasts DIS_STEPS. The organism pays DIS_COST·v extra basal cost (fever) and feels
  DIS_PAIN as malaise, so it can learn to rest. It dies with probability
  DIS_MORT·v·(1 + hunger)·(1 − IMMUNE_PROTECT·imm) per step, recorded as death by "disease".
- **Recovery:** gives immunity for DIS_IMMUNE steps.
- **Host defence:** the immunity gene (§6.3) is a cost paid every step against a risk that depends on
  crowding.

Telemetry records the number infected, new infections, deaths and the mean virulence per clade.

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
- **Lakes:** cells below the sea level (Nature.Water of the cells, LAKE_SHARE by default).
- **Rivers:** D8 flow accumulation over the elevation; cells whose accumulated catchment is above
  a threshold become river.
- **Depth:** lake depth is (sea − elevation). River depth is shallow.
- **Distance maps:** distance-to-water and distance-to-land are exact 8-connected chamfer distances on
  the torus (a label-correcting search, Revision K).
- **Moisture:** base moisture is high near water and in valleys.
- **Seasonal ponds (Revision K):** a land cell that is the lowest within POND_R cells and at least
  POND_GAP cells from a lake or river is a pond basin (with its 8 neighbours), kept with probability
  Nature.Ponds. Every POND_EVERY steps a basin is full when (1 + a_s·SEASON)·(1 + rain anomaly) ≥ POND_FILL,
  and the distance to drinkable water (lakes, rivers, full ponds) is remeasured. Ponds are for drinking
  only; their cells stay land.

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
| 4 | LIFECYCLE | — | timers | hatching, metamorphosis, disease, parental care |
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

**Animal models** (`wwwroot/js/creatures/`, display only). Organisms share instanced primitive
geometry at two detail levels rather than species-specific models. Each part is tagged as trunk,
head, neck, eye, limb, fin, wing, tail or armour. The vertex shader develops the organism's inherited
appearance genes into proportions and appendage shapes; genetic pigmentation, body-local markings
and insulation determine its surface. Optional clade colours identify related populations without
changing those genes.

The shader animates parts using speed (gait), a serial-derived phase and the action bits of the
AGENTS frame. Legs trot, wings flap, swimming bodies sway, heads dip to eat and jaws open on strikes.
The phase affects motion only, not inherited anatomy or pigmentation. Near and far meshes use the
same gene interpretation; far geometry omits small details. The renderer never feeds values back
into the simulation: physical morphology effects are expressed separately by `Body.Express`.

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

Founders start in groups (§7.4). This is a choice about how each world begins, not a law, and no run
adds animals after its founders.
