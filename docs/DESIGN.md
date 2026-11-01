# NEUROREEF 2 — DESIGN DOCUMENT

Document NR-DD-001, Revision O. It implements NR-RQ-001 Revision G (`REQUIREMENTS.md`). The values of every
constant named here are in `CONSTANTS.md`, which is generated from the code.

**Water depths in metres, and wetlands from the water table** (2026-11-01; Hydrology.Waters and Bathymetry, Wetlands.cs,
WaterCycle.cs, Fields.Row, Plants.cs, Wire.cs; tools/WetProbe; images docs/art/wetlands/: depth_before_*, depth_after_*
(water by depth, log scale; the shallow realm outlined) and wetlands_* (kinds and peat, the share of the year
waterlogged, the plants' flood tolerance)). Measured before (512 map, seeds 3, 5, 7, a new world's land and one more year):
- SHALLOW was 0.035 in elevation, ~320 m at H_RELIEF 8,000 m. Rivers were 87-324 m deep (p50 165-171 m: 0.5 x SHALLOW x
  (Q / RIVER_Q1)^0.4, at most SHALLOW); the sea p50 130-270 m, max 1.0-1.3 km; lakes p50 10-105 m, max 90-311 m: the
  relief's basins, filled to their spill points. 52-60% of all water cells were 100-400 m deep, and 64-78% of the sea and
  lakes counted as the "shallow" realm.
- No wetlands: the water table reached the surface (deep store full, a pond, spring or flood) on 0.3-1.7% of the land for
  half the year or more, and nothing came of it; moisture was 0.95 or more on 0-1% of the land.
Depths now (SHALLOW and every water depth in metres through World.ElevPerM; World.ShallowE is SHALLOW in elevation):
- *Rivers* by Leopold & Maddock's (1953) hydraulic geometry at their mean discharge in m3/s (Flow x P_MEAN over a 218-m
  cell): d = RIVER_D 0.3 m x Q^0.4, from RIVER_D_MIN 0.3 m to RIVER_D_MAX 10 m (1.2 m at 30 m3/s, 4.8 m at 1,000).
  The region's rivers are brooks: its largest carries 0.6-1.4 m3/s (a 28-km map gathers ~8 m3/s in all), so its
  channels are 0.3 m deep (0.24-0.35 m before the floor) and their banks 0.03-0.3 m; bigger maps' trunks are deeper.
  Width is unchanged (cells, ~sqrt(Q / RIVER_Q1)). Every river is in the shallow realm, as before.
- *The sea and the lakes* (Bathymetry): their basins fill with sediment to a shoreface, Dean's (1977, 1991) equilibrium
  profile h = SHORE_A x^(2/3) (A 0.08 m^(1/3), fine sand) at x m from the shore, down to the closure depth SHORE_HC 4 m
  (354 m out), then the sublittoral or shelf slope SHORE_S 3%; ground higher than that profile stays as it is. The bed
  (Elev) is raised to it, so Elev + Depth, the level, is unchanged; LakeDepth (the wetted area as a lake falls) is the
  new deepest point. The first ring of cells is 1.8 m deep (the shallow realm), the second 3.8 m, the third 9.7 m.
- After (same seeds): rivers 0.03-0.3 m (100% under 0.5 m); the sea p50 9-18 m, p90 42-65 m, max 65-95 m (16-22% of it
  under 2 m, 29-34% 2-10 m); lakes p50 1.8-2.7 m, p90 3.8-9.7 m, max 9-19 m (the region's lakes are small: 137-417
  cells in all). All water cells: 36-48% under 0.5 m (rivers), 11-17% 0.5-2 m, 13-24% 2-10 m, 25-31% 10-100 m, none
  deeper. The shallow realm is 9-14% of the map (15-20% before): every river, and 21-28% of the sea and lakes (64-78%).
- What reads SHALLOW, now in metres: the shallow realm (Hydrology, Terrain.RunoffRain), wading speed (World.GroundSpeed:
  wading to swimming over 0-2 m), swimming (Resolve: deeper than 2 m), the algae's light (Fields: e^(-depth / 2 m), so
  benthic algae grow in the littoral and fade by 10 m), the deepest water a body reaches (World.MaxWaterDepth: SHALLOW x
  (1 + SWIM_DEPTH_RANGE x fins), SWIM_DEPTH_RANGE 14 -> 249: 500 m with full fins, 52 m at fins 0.1; before, fins 0.1
  reached ~800 m, below every water) and the DEPTH sense (Decide: 0-500 m). Respiration already read metres (ColumnM,
  SurfaceM) and the vehicles too (Player.WaterDepthM: the boat needs 0.4 m, so it runs on the rivers; the jeep wades
  0.9 m). The WORLD frame's depth plane is now round(16 sqrt(m)) (ICD; world3d.js decodes it for river cuts, river
  colour and its fallback river plane; the lakes' colour darkens over 0-30 m, 0.55 units, not 2 units = 110 m).
  Wire's alluvium layer divided elevation by ALLUV_D in metres; it converts now.
- Floods: the stage over bankfull is the river's own depth x (wet^0.4 - BANKFULL^0.4), so 0.3-m brooks rise centimetres
  and reach only the cells level with their bed: flooded land fell from 0-1.1% to 0-0.05% of the year on average. That
  is what such small streams do; the floodplain's wetness now comes from its water table (below).
Wetlands (Wetlands.cs; nothing places one):
- *The water table.* Deep groundwater fills an aquifer of drainable porosity WT_SY 0.1, so the water table stands
  (GW_CAP - Deep) / WT_SY below the ground (GW_CAP: 15 m of aquifer), less (TWI - mean TWI) / WT_F where the ground
  converges and is flat (TOPMODEL: Beven & Kirkby 1979; WT_F 2 /m; TWI = ln(a / tan b), the cell's drained area per unit
  contour over its slope), and never deeper than the ground's height above the water it drains to (HAND: Renno et al.
  2008; Nobre et al. 2011): a stream or lake holds the water table up on its banks and shores. (Lowering the aquifer's
  capacity near water instead, so riparian groundwater seeps out sooner, cut the dry-season baseflow of WaterCycleTests from 5.9% to ~4%.)
- *Waterlogging.* In an update, land whose water table is within WET_ZWT 0.3 m of the surface (the wetland hydrology
  criterion), or where groundwater emerges, a pond stands or a river floods, is waterlogged: its soil fills from the deep
  store, then from the water running through it (store to store: the budget closes). WetDur is the share of the last
  year it was waterlogged (an exponential mean over a year; at creation the spin-up's last year), WetMin the share it
  was fed by minerotrophic water (emerging groundwater, a flood, a water table held up by the water nearby).
- *Kinds.* Land waterlogged WET_DUR (a quarter) of the year or more is a wetland: a fen (WetMin 0.1 or more) or a bog
  (rain-fed) where peat is PEAT_MIN 0.4 m deep or more, else a swamp where shrubs and trees cover WET_WOODY 30%, else a
  marsh (Cowardin et al. 1979; Rydin & Jeglum 2013). A waterlogged wetland's water is drinkable (Wet = 2): shallow water
  among plants, for waders and amphibians, as a pond's.
- *Peat* (Fields.Row, Wetlands.Peat). Litter and wood rot at ANOX_ROT 0.2 of the drained rate for the waterlogged share
  of the year and humify into peat at PEAT_FORM 5e-5 a step there (PEAT_M 2.5 mm of peat per unit of litter); peat
  decays at PEAT_ROT_AN 3e-4 a year under water and PEAT_ROT_OX 1.5% a year drained (Q10 as all decay); what decays
  returns its nutrient. A new world's peat is Clymo's (1984) P = p / alpha (1 - e^(-alpha T)) over PEAT_AGE 10,000 years
  (Simulation.BuildLand, after the plants' spin-up, from their litter).
- *Plants.* A 21st trait, flood tolerance (PT.FLOOD): the high key widened from 16 to 32 bits (room for 8 traits; the
  plant table's KeyHi and PendHi are uint, so an older save's land is rebuilt). Waterlogging takes WLOG_HARM 2 x WetDur x
  (1 - tolerance) of a plant's production (root anoxia: Kozlowski 1997; Colmer & Voesenek 2009), so in permanently
  waterlogged ground a plant under tolerance 0.5 cannot grow; tolerance costs FLOOD_COST 8% of production at its most
  (Costs: a mid genome is unchanged). Founders are random in it; the wetlands' plants are what recruitment, the lottery
  and mutation leave there.
Measured (512 map, seeds 3, 5, 7; a new world and one more year, no animals; WetProbe):
- Wetlands 0.55 / 0.41 / 1.06% of the land (69 / 54 / 144 cells): marsh 8 / 6 / 8, swamp 16 / 5 / 30, fen 38 / 38 / 95,
  bog 7 / 5 / 11. Settings: lake and sea shores 13 / 13 / 23 cells, river banks 42 / 26 / 59, spring lines away from
  water 5 / 8 / 38, rain-fed flats and hollows 9 / 7 / 24. Waterlogging is nearly all or nothing: 0.3-0.8% of the land is
  waterlogged 90% of the year or more, 0.5-1.3% a tenth of it. This is a mountain region (HAND median 310-363 m, under
  5 m on 0.6-1.7% of the land): its wetlands are valley floors, shores and spring lines, often single cells, not plains.
- Peat: mean 1.2 / 1.3 / 1.7 m in the wetlands (p90 2.6-3.5 m, max 4.0-8.2 m), on 0.6-1.4% of the land; 3-12 million m3
  per map. Litter and wood 1.4 x dry land's; nutrient 0.19-0.42 against 0.46-0.63 on dry land (held in the peat).
  Wetland soil moisture 0.92-0.98.
- Plants: cover-weighted flood tolerance in wetlands 0.78-0.98 (herbs), 0.74-0.87 (shrubs), 0.58-0.82 (trees), against
  0.14-0.60 on dry land; 47-78% of wetland plant cover is of tolerance 12-15 (of 15; 57-85% where waterlogged 90% of the
  year), against 6-22% on dry land; 51-91 genotypes in the wetlands. Wetland cover: herbs 0.07-0.13, shrubs 0.07-0.22,
  trees 0.18-0.32 (dry land: trees 0.16-0.65).
- The budget: ET share 0.42 / 0.53 / 0.43 of the rain, as before. Drinkable water: wetlands join ponds and springs.
Wire: FIELDS layers `wetland` (the kind most of each 2x2 block's wetland cells have, x 64: marsh 64, swamp 128, fen 192,
bog 255; not averaged) and `peat` (1 - e^(-m / 2 m)); WORLD's depth plane in metres (ICD). New World fields (land,
Checkpoint.LandFields): Twi, TwiMean, Hand, WetDur, WetMin, WetNow, Peat, Wetland.
The climate's latitude span (Climate.RowParams: theta = 2 pi y / FW; also Climate.Rainfall's belts and winds, the
seasons' phase in Fields, WaterCycle and Plants, Hydrology.AnnualWater, Caves' dissolution, Geology.Snowline): every map
spans equator, pole, equator, pole whatever its size. On the 512 map (128 rows, 28 km) the pole is 32 rows (7 km) from
the equator: the sea-level annual mean falls 40 K over 7 km (5.7 K per km, ~800 times the Earth's 0.007 K per km), the
snowline from 4.5 km to the lakes, and bands 14 km apart have opposite seasons. A region should sit at one latitude.
Proposed, not done (it changes every world, Geology's glaciers and Climate's owners' code): one Climate.Lat(y) used by
all of these, lat(y) = LAT0 + (span / 2) cos(2 pi y / FW) with span = the map's true extent (Map.WidthM / 111 km per
degree: 0.25 degrees at 512, 1 at 2048), continuous on the torus; LAT0 a world setting (Nature, default 45 degrees) or
drawn from the terrain seed. The prevailing wind and the rain belt then come from LAT0, and the seasons are in phase.
Scale: every depth, width and area here is in metres through World.ElevPerM and GEO_CELL_M (river discharge in m3/s
from the cell's area, Hydrology.M3s; the shoreface's distance from the shore in metres; TWI's drained area per unit
contour in m2/m; HAND in m), so it holds when the cell size or vertical scale changes (the coordinator's 3-4 km worlds,
GEO_CELL_M ~55 m). Hydrology.WidthM gives a river's real width, RIVER_W 5 m x Q^0.5 (Leopold & Maddock; 5 m at 1 m3/s),
narrower than a cell; World.RivW stays the drawn width in cells. world3d.js converts metres with RELIEF / 8000 (its
units per metre at H_RELIEF 8,000 m): if the vertical scale changes, that and RELIEF change together.
Not done: floodplain inundation as a wetland source at the region's scale (its brooks barely rise); RivW and the RIVERS
section in metres (WidthM is there for it); RIVER_BANK (150 m) still lets a wide river's bank cells stand far above its
0.3-m water; wetland animals beyond drinking.
Tests: WetlandTests (standing water deepens from its shore along the profile, flat-surfaced; a 512 region's wetlands
lie low and convergent, wet-soiled and peaty, of consistent kinds, with flood-tolerant plants; peat forms only in
waterlogged ground and mass is kept; tolerance lets plants grow where intolerant ones cannot, at a cost on drained
ground). HydrologyTests' hydraulic geometry in metres; WetYearsFloodTheFloodplain: more than 2 flooded cells, not 50
(4-5 now); WaterCycleTests' dry-spell baseflow over 4% of the wet discharge, not 5% (4.8% with the wetlands' ET, 5.9%
without); DecisionTests and AppearanceTests set depths through World.ShallowE; PlantTests' Key and Cross take a 32-bit
high key and flood tolerance costs growth; ClimateTests' hash re-pinned.
**Why the grazers boomed: free energy and a life-history clock without days** (2026-11-01; Plants.cs, Resolve.cs, Body.cs,
Life.cs, Constants.cs; reporting: Server/EcoWatch.cs, NR_ECO=1). The owner: "why are grazers booming?" Measured with
the new ecology census (512 map; seeds 5 and 7 to 3,000-3,600 steps, where the world reached 16,384 slots or ran out of
memory; seed 5 had 17,306 animals at step 3,000 and was killed by the OOM killer at ~7 GB when run uncapped):
- *Herbivores ate more than the plants made.* Offtake of live plant food was 108-231% of net primary production (NPP)
  per 300-step sample on seed 5 and 126% pooled on seed 7, where herbivores assimilated 55% of NPP's energy and their own
  production (growth and young) was 13-40% of it (Lindeman 1942; Pauly & Christensen 1995: 5-20% between levels;
  Cebrian 1999: terrestrial herbivores take 5-20%, grazed grasslands up to ~60%). Three leaks: (1) reproduction turned a
  unit of production (cover, 3.2 E/M as herb) into a unit of nectar (8), seed (7) or fruit (6), so on seed 7 the seed
  eaten held 1,158 E/km2/yr against 554 for all reproduction; (2) a bite of browse cost the woody plants half its mass
  (BROWSE_LOSS 0.5) and (3) a bite of roots 0.3 of its mass from the herbs, while both pools regrew toward their own
  capacity. The booming 0.14 kg "grazer" of seed 5 (Narelax) was a nectar-feeder; seed 7's (Vitepe 0.12 kg, 11,078 at
  step 3,600) a seed-eater.
- *No energy ceiling on digestion.* Digestion was bounded only by mass, so rich foods gave 30-47x basal metabolism at
  0.1 kg (grass 6.7x), against sustained budgets of ~7x (Hammond & Diamond 1997).
- *The life-history clock was V1's steps.* C_MATURE 250, C_GEST and C_COOL 150 steps were 30 and 18 days once a step
  became 2.92 h: a 0.14 kg body could mature in 19 days and breed every 11. Narelax grew at r = 0.034/day in its first
  300 steps and 0.011/day over its first half year, against Fenchel's (1974) r_max = 0.025 M(g)^-0.26 = 0.0069/day.
Now (no caps, no population terms):
- RHO_PLANT (= RHO_GRASS): nectar, seed and fruit flesh are made at production x RHO_PLANT / their own density (fewer
  seeds, as seed size and number trade off); Plants.Yield counts them at RHO_PLANT. BROWSE_LOSS 1 (leaves are the woody
  plant's own mass); a bite of roots takes its energy's worth of herb, then shrub, cover (Plants.RootEaten).
- C_ABSORB = 7 x C_BASAL (x M^0.75, PAbsorb): digestion passes rich digesta only as fast as their energy is absorbed
  (Karasov & Diamond 1983; Diamond 1991), and the forager values a food at most at that rate (denser kept on a tie).
  Life.RangeCapacity uses the same bound.
- The clock in days (x M^0.25): maturity 120 days, gestation 50 days at a newborn of a tenth of adult mass (x sqrt of
  the share, as before), cooldown 50 days, lifespan 730 days at 1 kg (sources in CONSTANTS.md). A 0.14 kg body's
  time-limited r with a litter of 6 is 2.7x Fenchel's line (EcologyTests.LifeHistoryIsInDays); energy limits it below.
After (seeds 5, 7, 3; 9,000 steps; pooled after the first year, before -> after where measured):
- offtake of NPP 108-231% / 126% -> 27.6 / 54.8 / 42.8%; herbivores' assimilation 55% (seed 7) -> 12-26% of NPP;
  herbivore production / NPP 13-40% -> 3.7 / 7.3 / 5.0%; NPP 1.3-1.5 -> 3.0-3.9 kJ/m2/yr (the plants are no longer
  grazed below their own regrowth: herb cover 0.03 -> 0.36-0.50).
- Seed 5: Narelax crashed (733 -> 241) once nectar carried only the energy the plants fixed; Yosupul (0.11 kg,
  fruit and roots) then grew from 99 to 3,839 at r = 0.0043/day (Fenchel 0.0074 at 110 g), still growing at 9,000 steps.
  Seed 7: Osnar (0.15-0.19 kg seed-eater) cycled with the seasons, 315 -> 612 -> 331 -> 880 -> 119; 763 Nipeka (21 kg).
  Seed 3: 360-833 animals, 20-30 kg root and grass eaters most numerous.
Still unrealistic, and why (not changed here):
- NPP is 3-4 kJ/m2/yr, ~1,000-5,000x below real land (300-1,000 g/m2/yr, ~5-20 MJ/m2/yr): R_G and the cover units make
  a field cell (4.75 ha) grow a few kg of food a year. Densities therefore lie 2-3 orders below Damuth's rule
  (D = 4e4 M(g)^-0.75 /km2): Yosupul 6.1 /km2 against 1,148; 20-30 kg herbivores 0.1-1.2 /km2 against 18-23. Seed 5's
  baseline 22/km2 for 0.14 kg was already 45x below Damuth: the boom was against the world's own food, not Damuth.
  At these densities the density-size slope is not Damuth's (on seeds 3 and 7 the big herbivores now outnumber the small).
- No predator level emerges in 3 years: carnivore biomass 0-1.3 kg/km2, 1 : 12-842 of herbivores (Carbone & Gittleman
  2002: ~1 : 100), carnivores' production 0.1-1.5% of the herbivores' (5-20%). Most "killed" deaths are herbivores and
  omnivores striking others (strike target in reach on 3-8% of decisions); regulation is by food (starvation, embryos
  lost) and by that interference, not predation.
- Pathogens die out on every seed, before and after: infected 18-37% at the start, 0% by step ~4,000. Contact
  transmission caught 9 of 10,937 infections on seed 5 (nearest neighbours lie ~200 m apart at 6/km2, beyond a step's
  reach); strains selected for persistence in the ground, replication fell to the bottom of its range (0.033/step) and
  the immune response cleared them. That is below the threshold host density of directly transmitted disease (Anderson &
  May 1979), so disease is not density-dependent at these densities.
- 65-88% of the living are juveniles: the young grow to full size, which on a full reserve takes the whole maturity time;
  mean age at death 66-300 days against lifespans of 500-2,400.
Reporting: NR_ECO=1 prints an ECO line every 300 steps (animals and biomass by trophic group from what each clade ate,
herb cover, NPP, offtake, production ratios, infected share, deaths by cause, the largest clade) and pooled tables at the
end; PlantTable.Production and Telemetry.EatenMass are counters nothing reads back (no hash moved: the plants' and
fields' state is unchanged by them). Tests: EcologyTests.AbsorptionBoundsRichFood, LifeHistoryIsInDays.

**A world you see whole, and bodies at their true size** (2026-11-01; Options.cs, setup.html, js/scale.js,
js/world3d.js, js/look/vegetation.js; images docs/art/scale/). The owner: "the worlds are too damn big now ... the world
should be easy to see the whole thing", "we don't need worlds that reach 60k population", and animals and plants drawn
~100-400 times their size (a 1 kg animal 80 m long, shrubs 50 m). Now:
- The default map of the prehistory and the world is the region, 512 units = 28 km (it was the continent, 1024 = 56 km):
  it has a mountain range, a branching river system, lakes and karst, and its whole surface fits one view. A region
  feeds about a quarter of a continent's animals (some thousands to ~25,000). Valley, continent and supercontinent stay
  on the setup screen and --map / --world-map.
- The overview opens on the whole map (fitWorld: centred, from above at a gentle tilt); Home or the Whole world button
  returns to it; the wheel zooms toward the cursor (from the whole map to a few metres in ~60 notches) and a double-click
  dives to a quarter of the distance there; the view centre follows the ground and the camera never goes under it.
- Bodies are drawn at their true length, 0.1 M^(1/3) m times the length gene's share of the body (ICD §2.1: 1 kg about
  10 cm, 40 kg about 34 cm; metres per unit and bodyLenM1kg from meta/status.units, and each juvenile's present length
  from status.bodyLenM), times the overview's "creature size" control
  (x1, 2, 5, 10, 20, 50, 100; default x20, labelled as magnified; keys [ and ]). Plants are drawn at the sim's plant
  height in metres (trees 3-45 m, shrubs up to 4 m, grass tufts 0.5 m, boulders a few metres), times the fourth root of
  the creatures' factor (x2.1 at x20), as many to a field cell as its cover fits crowns of that size. Explore is always
  x1, so its eye height (1.7 m), speeds and weapon reaches are in the same metres as the bodies. In the overview no
  animal is drawn shorter than 1.8 px, so the whole map shows where the animals are as a sprinkling of dots, not a carpet.
- Swimmers are drawn under the water's surface at their depth (AGENTS format 7: depth in m, at true scale), fading
  into the water's colour with depth and its murk (algae, plankton); an air-breather that spends only part of each step
  at depth dives for that share of an 8 s cycle and is at the surface for the rest. Never below the bed.

**High, jagged mountains: rock strength, glaciers, talus and the true height scale** (2026-11-01; Geology.cs,
Hydrology.Erode, Terrain.Shape, H_RELIEF; tools/TerrainProbe/mtn.py; images docs/art/terrain2/mtn_before_* and
mtn_after_*: _shade top-down, _view the highest peak, _view2 the highest below 45° latitude, both at true scale). The
owner: "there are no jagged high mountains". Diagnosis (1024 map, seeds 3, 7, 11):
- *The cap.* Deep time made 7-8 km of relief on the coarse grid (Crust.Scale 0.37-0.40 for seeds 3 and 11), and Refine
  scaled it down to H_RELIEF = 3,000 m; the game read at most 2.8-3.0 km above the lakes.
- *One critical slope.* Every rock failed at GEO_SC (31°): no land was steeper than 33°, p99 31° on every rock. Mountains
  were pyramids of 31° facets with rounded crests: crest sharpness (a ridge cell above the mean of its 8 neighbours) 30-34 m.
- *Soft rock everywhere.* Sediment over 20 m thick made a cell "sedimentary" and fail at 31° all the way down, and creep
  and diffusion laid such mantles on half the cells of every mountainside, in a salt-and-pepper mosaic; the fine passes
  then shaved up to 1.5 km off summits whose surface was a 25 m mantle on granite.
- *No glaciers.* Nothing cut the land above the snowline.
Now:
- *Rock strength sets the critical slope* (Rock.Sc; Schmidt & Montgomery 1995): crystalline 54.5° (GEO_SC_CRYST 1.4),
  limestone 50° (1.2), volcanic 45° (1.0), soft strata, regolith, talus and alluvium 31° (GEO_SC). Loose cover fails at
  GEO_SC only down to the rock beneath it, which then stands at its own (Geology.Fail's cover). Nonlinear diffusion takes
  the critical slope of the higher cell's rock; the fine passes' creep runs at kRock of the higher cell (soil production
  on hard rock is slow) and they know the rock beneath the cover (Crust.FineBed, FineSed; Geology.Cover, Expose: where
  they strip the cover the rock below becomes the surface).
- *Talus* (GEO_TALUS, GEO_REPOSE): half of what falls from a crag in a step comes to rest on its lowest neighbour, up to
  35° from the crag; it is loose (sedimentary) cover. None lands on a glacier or a lake.
- *Glaciers* (Geology.Glaciate, each step on both grids): the deep-time snowline (ELA) is where the Earth-like annual mean
  T_EQ - DT_EP sin² lat - LAPSE z reaches GEO_T_ELA = -2 °C (whatever the climate setting, so a seed makes one land:
  4.5 km at the equator, 1.4 km at 45°, the lakes' level from 61°). Mass balance GEO_ICE_BETA 7 m/yr per km, at most
  GEO_ICE_BMAX 1.5 m/yr x the rain; the balance flux runs down the drainage until melt takes it. Plastic ice
  (tau 100 kPa) of width 0.5 km x (Q / 5e6 m3/yr)^0.4 slides at Q S rho g / (W tau) and erodes E = 1e-4 u_s (Humphrey &
  Raymond 1994), implicitly like stream power with n = 1; it slides fully with its bed at the melting point and not at
  all at GEO_T_COLD = -10 °C (the bed is the air's mean + 29 K per km of ice). Ground steeper than 45° holds no ice: its
  snow avalanches on and the glacier cuts its foot (headwalls, arêtes, horns). Cells abreast of the bed within the
  glacier's half-width and under its surface are pulled toward a parabolic cross-section (U troughs, where the glacier is
  more than two cells wide). A glacier carries its load to its snout. Crust.Ice / FineIce keep the last step's ice.
- *Relief from uplift against erosion*, no longer scaled to 3 km. **H_RELIEF is now 8,000 m and means the vertical
  scale**: a cell stands (Elev - SeaLevel) / (1 - SeaLevel) x H_RELIEF m above the lakes, as before; the highest ground
  stands at most that high (deep time's relief is scaled down only above it: the 2048 map reaches 7.5 km, scale 0.99).
  On the 1024 map the top is 3.1-5.2 km, so Elev of the highest land is ~0.4-0.65, not 1.
- *Climate stays consistent*: Climate.Build and Hydrology read metres through H_RELIEF as before; Rainfall's orographic
  lift is now per km (RAIN_OROG 40/3 per km, the same per metre as 40 per 3,000 m) and the coarse rain of deep time sees
  the relief at the game's vertical scale. The channel-head threshold Q S^2 >= CHANNEL_C now takes S in m/m (CHANNEL_C
  2.4, the old 0.01 in elev per cell at 3,000 m): in elev units it had cut the channels from 2.1% to 0.2% of the map.
- *Limestone* (Rock.Limestone = 3, for the caves): a young platform's cover is GEO_LIME = 0.8 km of limestone under
  0.7 km of clastic strata (Crust.Lime); erosion strips clastics, then limestone, then bedrock (Crust.Strip). K 3, Sc 50°,
  fertility 0.65. Land limestone 1.3-12.8%.
Measured (1024 map, seeds 3, 7, 11; before -> after):
- Max relief above the lakes 2,963 / 2,832 / 2,947 m -> 5,170 / 3,134 / 4,714 m. Geology's own top is 6.2 / 3.3 /
  5.4 km; Hydrology.Erode's 20 fine passes then lower the upper half of the relief by 444 / 93 / 390 m on average
  (p99 1.0 / 0.3 / 0.9 km). That is worth a look: they are not deep time, and their stream power is in elevation units.
  Land over 1,500 m 8.5 / 9.8 / 7.6% -> 28.4 / 9.7 / 21.8%; over 3,000 m 0 -> 3.7 / 0 / 4.0%; median land
  460 / 582 / 388 -> 805 / 498 / 699 m.
- Slope by rock (steepest drop to a neighbour, p50/p90/p99, before -> after): crystalline 15-23 / 28-31 / 31° ->
  25-31 / 50-53 / 54.6°, 26-35% of it steeper than 40° and 11-18% steeper than 50° (0 before); volcanic p99 28-31 ->
  45°; sedimentary p99 31 -> 31-32° (under 0.2% of it over 40°); limestone p99 29-37°.
- Snowline: land above the ELA 42.5 / 45.1 / 38.1% -> 46.8 / 43.0 / 41.9%, most of it in the polar rows, where the ELA
  is at the lakes. At latitudes under 45° it went from 0.9 to 8.3 / 1.5 / 4.8%. The buzzsaw (same seeds, glaciers off
  -> on, latitudes whose ELA is 0.3-3.5 km): land above the ELA 32 / 16 / 23% -> 26 / 9 / 18%; p99 height above it
  2.6 / 1.8 / 2.5 -> 2.1 / 1.3 / 1.8 km. Glaciers run on the routed steps (every GEO_ROUTE_EVERY) for those steps
  together. Steady state: residual 0.04-0.06 before, 0.07-0.09 after (0.11-0.13 without glaciers).
- Jaggedness (land above half the top): crest sharpness 30 / 34 / 30 m -> 103 / 100 / 88 m; mean |Laplacian| 48 / 53 / 48
  -> 204 / 219 / 179 m per cell²; steeper than 40° 0 -> 70 / 76 / 57%.
- Land rock: crystalline 65-76%, volcanic 5%, sedimentary 17-19% (24-35% before: thin mantles on crags are shed),
  limestone 1-13%.
- Generation: Terrain.Build 2.9 s at 1024 and ~12.5 s at 2048 on 4 shared cores at load ~5 (it was 2.4 and 9.7). At
  load 14-18, back to back with HEAD, the 2048 map took 14-21 s against HEAD's 17.6-17.8 s, so the budget test fails
  for both under that load.
Tests: GeologyTests CragsStandOnHardRock, GlaciersCutTheLandAboveTheSnowline ([BigWorld]; Geology.Form(w, glaciers:
false) is the control) and ReliefScalesWithUplift (coarse land by tercile of rock uplift stands higher, the top third
over 1.5 x the bottom); SlopeDistributionIsBounded became SlopeDistributionIsBoundedByRock (each rock by its own Sc,
soft rock p99 under 35°); arcs are volcanic bedrock (talus now mantles 57% of their surface); the highest tenth of the
land is over 60% (was 70%) on convergent belts (79 / 62 / 83%: the buzzsaw lowers glaciated belts); limestone joins the
erodibility order and the fertility test. HydrologyTests' channel threshold check takes S in m/m. ClimateTests' no-climate
hash is re-pinned (0x106402C035234308).
For the renderer and the 3D view (not changed here; wwwroot is theirs):
- world3d.js draws (e - sea) / (1 - sea) x RELIEF with RELIEF = 55 units for 3,000 m. At true scale (a 218 m cell is 4
  units) **RELIEF must be 8000 x 4 / 218 = 146.8 units** (H_RELIEF x FCELL / GEO_CELL_M).
- Its "rock on the highest ground" tint (ea > 0.78) assumed the land fills 0..1 above the lakes; the top is now at
  0.4-0.6 of the scale on the 1024 map, so tint by slope and rock instead (World.Rock: 3 = limestone).
- Wire.EncodeWorld sends Elev as one byte: at 8,000 m that is ~35 m a step (it was ~13 m), which terraces 218 m cells
  by ~9°; the elevation needs 16 bits (as Surface already is).
Not done:
- Elevation-unit constants (SHALLOW, RIVER_BANK, FLOOD_HAND, ALLUV_D, BASIN_MIN) now mean ~2.7 x as many metres. Done at
  the integration (2026-11-01): RIVER_BANK, FLOOD_HAND, ALLUV_D, BASIN_MIN and LAKE_MIN_DEPTH are in metres (converted by
  World.ElevPerM), at what they meant when the hydrology was tuned (H_RELIEF 3,000 m, 1 - SeaLevel ~0.7). SHALLOW stays
  a game depth in elevation (swimming, light, the shallow realm), and FLOW_EPS is numerical.
- Fault scarps along plate boundaries.
- The parallel gully comb on uniform slopes may come from Refine's roughness: Fbm with its base lattice on the coarse
  grid, upsampled by exactly 2. Not yet tried.
**Rivers that run along their own lines** (2026-11-01; Hydrology.Route and Waters, Channels.cs, RiverStats.cs, Wire
RIVERS; images docs/art/terrain2/riv_before_*.png and riv_after_*.png, tools/RiverProbe). The owner: "the rivers are
weird". Measured before (1024 map, seeds 3, 7, 11; RiverStats):
- Every step of every channel ran at a multiple of 45 degrees (100%, against 11% for directions spread evenly), and 17-18%
  of 6-cell reaches did: D8 staircases and straight 45-degree runs. A river was drawn as the chain of its cell centres.
- The relief itself leaned to the grid: 64-65% of the steep land faced within 22.5 degrees of a diagonal (uniform 50%),
  from D8 receivers in deep-time and fine erosion. The discharge map had D8 streaks down every hillslope.
- Drainage density 0.08-0.12 km/km2 (36-40 heads, 830-1,210 channel cells); land lay 3.1-4.2 km from a channel and
  10.6-13.6 cells from drinkable water. Strahler order 2-3, N(1) / N(2) 4.0-6.3.
- Sinuosity (3-cell ruler) 1.11-1.13, all of it the staircase's own; no reach had a valley slope under 0.002.
- No straight streams ran side by side on this relief: the parallel gullies of docs/art/terrain/hydro_after_3.png
  were drawn on the relief before the geomorphology merge.
Changes:
- *Routing (Route, Waters).* Flow directions are D-infinity (Tarboton 1997) over the filled surface: the steepest of the
  8 facets around a cell, an angle between a cardinal and a diagonal neighbour. On a hillslope (below SPREAD_A of
  gathered rain) the flow is shared between them in proportion to the angle (dispersive, as Quinn et al. 1991's
  multiple-flow routing), so a plane gathers no streaks. A stream (above it) goes whole to one of them, drawn with the
  same odds from a hash of the cell and the relief (Fairfield & Leymarie 1991's rho8), so it runs down the true aspect on
  average. Net gives the second receiver and its share (Recv2, Share). Inside a filled depression the flood's receivers
  stand. Geology's deep time runs on the same Route (its owner agreed): the diagonal lean of the relief fell to 58-59%.
- *Channel heads* are placed by the inflow with the hillslopes' spread (Net.HeadIn), not along the single receivers.
  CHANNEL_C 0.01 -> 0.005: drainage density 0.12-0.17 km/km2. A 218-m cell cannot hold the real 1-5 km/km2 of
  first-order streams without drowning the land, so this is what a map at the 1:250,000 scale shows, not the field's.
- *Channel lines (Channels.cs).* The network is cut into rivers (head down, the larger branch going on at each
  confluence), laid largest first. The valley axis is the cell path smoothed (Gaussian, CHANNEL_SMOOTH cells or 3/4 of the
  width) and held within a cell of it. An alluvial river meanders where its valley slope S is under the meandering-braided
  threshold S_t = MEANDER_ST Q^-0.44 (Leopold & Wolman 1957; Q bankfull, m3/s): sinuosity 1 + (MEANDER_P - 1)(1 - S/S_t),
  on a sine of wavelength MEANDER_L widths (Leopold & Wolman 1960) with the amplitude that gives that sinuosity, no wider
  than the valley floor (ground within MEANDER_HAND of the bed). The amplitude fades in below the head and out before the
  river's end; a loop that meets itself is cut through (a neck cutoff); no line enters ground that drains to another
  lake. The channel cells are the cells the line crosses; each drains to the next, and where the line crosses it is
  kept (World.RivX, RivY, with the width RivW; Checkpoint land fields). Cells of the old course drain to the river.
  Every cell drains to the lake it drained to before, so the lakes' balances and spill order stand. The discharge is
  gathered again along the new drainage.
- *Pond sites* may lie POND_GAP_RIVER (2) cells from a river; POND_GAP (6) still holds from the sea and the lakes.
  With the denser network only 1 site was left POND_GAP from every river on seeds 3 and 4; now 34-74 on seeds 3, 4, 7
  (floodplain ponds and backswamps; seed 4 had 16 before).
- *Wire.* WORLD ends with a RIVERS section: each river's points (x, y in cells, width, depth), the river it joins, its
  order and how it ends (ICD.md). Hydrology.Lines builds it.
After (same seeds):
- Steps at a multiple of 45 degrees: 13.7-16.3%; 6-cell reaches 11.1-11.5% (uniform 11.1%). No histogram bin holds 3x
  its share.
- Drainage density 0.12-0.17 km/km2 (63-67 heads, 1,870-2,540 channel cells; river water 3.0-5.0% of the map, against
  1.4-3.2%); land 2.0-2.8 km from a channel, 7.3-9.8 cells from drinkable water.
- N(1) / N(2) 3.3-5.0 (pooled 4.1; natural networks 3-5). Strahler order is still 2-3, and Horton's length ratio is
  1.2-1.4 (natural 1.5-3.5): the grid holds no first-order streams, and most networks end in a lake after a few
  confluences.
- Sinuosity 1.06-1.08: these are mountain rivers on valley slopes of 1-10%, which do not meander (above S_t, 0.005-0.03
  for their discharges). On a synthetic valley with a 40-cell floor (HydrologyTests.Valley; images riv_after_valley_*)
  the trunk's sinuosity is ~1.6 at a valley slope of 0.0003, 1.44 at 0.0012 and 1.00 at 0.01.
- The drawn width never falls downstream; the water cross-section falls at 1.5-4.4% of steps (it was 1.0-7.6%).
- Generation time: Hydrology.Waters with the channel lines takes 0.04-0.06 s at 1024. Terrain.Build took 2.8-3.5 s with
  the machine lightly loaded (2.3-6.4 s before, under varying load) and 4-7 s at a load of 16 on 4 cores.
Tests (HydrologyTests): flow accumulation conserves rain with the spread shares; no cell past the channel threshold lies
more than 8 cells from its river; no angle spikes; N(1) / N(2) in 3-5.5 pooled; continuous lines (points under 2.2 cells
apart, width never falling, every channel cell on exactly one line); sinuosity rising as the valley slope falls; the lines
are the same bits on every build. WireTests checks the RIVERS section against Hydrology.Lines. ClimateTests' no-climate
hash is re-pinned (0x753FBC092B902E01).
Not done: braided reaches (steep, heavily loaded rivers are straight lines, not several threads); deltas and estuaries.
The evenly spaced parallel gullies on long uniform slopes are the relief's (Geology owns it); they now follow the true
aspect, not 45 degrees.

**Caves and karst from rock and water** (2026-11-01; Caves.cs; images docs/art/terrain2/caves_*.png). The owner saw no
caves. Now, built at Terrain.Finish from the rock, relief, rivers and the mean year's water (the terrain seed alone):
- *Limestone.* Sedimentary rock is carbonate (World.Carb) where a regional draw of formations (fBm of the terrain seed)
  plus distance from clastic sources (crystalline and volcanic ground; Schlager 2005) puts it in the top CARB_SHARE of
  sedimentary land. Rock with Carb >= CARB_MIN is karst: 9.7-12.6% of the land (seeds 3, 7, 11 at the 1024 map). No new
  rock kind: Geology and World.Rock are unchanged.
- *Dissolution.* Soil pCO2 from actual evapotranspiration (Brook et al. 1983), [Ca]eq ~ pCO2^(1/3), less in warm water
  (Dreybrodt 1988); denudation = runoff x [Ca]eq (White 1984). It sizes passages and sets how many solution dolines form.
- *Conduits.* Springs are valley mouths (a stream of CHANNEL_A_MIN) where the drainage leaves the karst. A Priority-Flood
  from them by water-table head (CAVE_GRAD, less under valleys: CAVE_VALLEY) gives each karst cell its conduit (CaveNext)
  and spring (CaveSpring): dendritic networks converging on a few springs, the lowest capturing most (underground piracy).
  A passage forms where the conduit gathers CAVE_Q_MIN, width ~ sqrt(Q) (CaveVoid: share of the cell over open
  passage; CaveDepth: m below the ground). Rivers standing CAVE_LOSE_H above the water table lose CAVE_LEAK per cell.
- *Features* (CaveKind bits): sinkholes (ponors where streams from other rock reach the karst, solution dolines, collapse
  dolines over thin roofs), karst springs, losing river cells, and entrances (resurgences, collapses, and relict passages
  CAVE_LEVEL_M above the water table cut by valley walls). Lava tubes on gentle volcanic ground below vents (LAVA_S),
  with skylights; the volcanic arcs are steep (median slope 0.3-0.5), so tubes are rare (5-14 cells per map).
- Per seed (3, 7, 11): passages 1,617 / 2,007 / 1,690 cells (23-31% of the karst), sinkholes 186 / 220 / 165 (ponors
  135 / 103 / 72), springs 90 / 71 / 92, entrances 61 / 60 / 86, losing river cells 161 / 149 / 63, lava tube cells
  14 / 5 / 14. After the spin-up, 33-48 karst springs per map are drinkable (Wet = 2), and karst springs carry 79-88% of
  all emerging groundwater. Caves.Build takes 20-50 ms at the 1024 map.
- *Water* (one call in WaterCycle.Finish, Caves.Drain): a sinkhole or losing reach swallows CaveSink of the surface water
  reaching it (at most CAVE_CAP x its mean intake), and karst groundwater drains through the conduits (CAVE_TAU); both go
  to the deep groundwater of the spring cell, where what the ground cannot hold emerges (Seep). Store to store: the budget
  closes (CavesTests, WaterCycleTests).
- *Shelter* (one line in Heat.Operative): in an entrance cell the operative temperature is the cell's annual mean plus
  CAVE_SWING of the air's departure from it, with no sun (Badino 2004). The brain is unchanged: animals feel it through
  the operative temperature they already sense. With no climate it is T_REF, as before.
- New World fields (land, in Checkpoint.LandFields): Carb, CaveVoid, CaveDepth, CaveFlow, CaveSink, CaveNext,
  CaveSpring, CaveKind. The WORLD frame gains two trailing planes (cave features, cave courses; ICD.md).
- Not done: cave interiors as habitat beyond the entrance (no dark zone, no troglobites); sea and fluvial caves.

**Water from its balance: the sea, lakes at their spill points, river channels** (2026-10-31; Hydrology.Waters,
Terrain.BuildWater, WaterCycle.cs; images docs/art/terrain/hydro_before_*.png and hydro_after_*.png). The owner saw no
rivers and drowned low ground. Measured before (1024 map, seeds 3, 7, 11):
- Standing water was exactly the cells below the Nature.Water quantile (10.3-10.5%), all at that one level, in 6-19
  separate bodies. The largest was 5.9-6.9% of the map; the others (3.5-4.4%) were cut off from it by rims whose spill
  points stand 140-370 m above that level on average. 40-54% of the standing water was shallower than SHALLOW.
- The quantile did not drown low ground beyond any spill point: no body stood above its rim, and only 1.7-2.6% of the
  land lay within 35 m above the water. The flat, broad water was the rift basins Geology grades to that level.
- Rivers were the cells over RIVER_ACC: 0.53-0.68% of the map in 11-14 rivers, all one cell wide, 61-126 m deep.
  Strahler order at most 2, the longest 87-130 cells. Every river reached standing water, but few reached the largest body.
- Water by type: sea and lakes 10.3-10.5%, rivers 0.5-0.7%, ponds 0.1-1.4%, springs 0.5-0.7%, floods 0 at the start.
  Drinkable water was 6.6-7.3 cells away on average.
Now:
- *The sea* is the low ground below sea level (SeaLevel, still the Nature.Water quantile). **Nature.Water now means the
  share of the map that is sea**; the lakes above it come from the water balance. Every region below sea level counts
  as sea (SEA_MIN = 0), because Geology holds every such region at sea level as base level for 60 Myr and never cuts
  its rim. Treated strictly as closed basins (SEA_MIN = 1: only the largest region is sea), those regions fill to their
  spill points, and lakes cover 13-26% of the map (seeds 1, 2, 3, 7, 11). Once deep time drains to the connected sea
  alone, SEA_MIN can rise.
- *Lakes.* A Priority-Flood from the sea fills every depression to its spill point. It orders by the exact filled height,
  first-in-first-out among equals, so the spill levels are exact and no epsilon is needed. Sources first, each
  depression's lake rises from its floor, cell by cell in the order a rising pool floods them (Fill-Spill: Barnes,
  Callaghan & Wickert 2020). It keeps rising while its inflow pays for what the lake loses. The inflow is the mean yield
  P - ET of its catchment, with ET from Budyko's curve in Fu's form (w = 2.6), plus what full lakes upstream spill.
  The temperature for PET is the default climate's (K.CLIMATE) whatever the setting, so a seed makes the same land and
  water at any climate setting (ClimateTests); the setting changes how lakes and rivers run, not where they are.
  The loss is open-water evaporation (E_OPEN x PET) less the rain on the lake. A lake that reaches its sill is full and
  spills its surplus through its exit cell. One that stops short is terminal: it passes nothing on, and its dry floor
  drains to it and holds pond sites (playas). A depression smaller than LAKE_MIN_CELLS or shallower than
  LAKE_MIN_DEPTH is a pond site, as before. No lake stands above its spill point, and none stands above the dry ground
  beside it (HydrologyTests).
- *Drainage and discharge.* World.Recv and DrainOrder are now Waters' own: the filled surface; in a terminal basin, toward
  its lake. Flow is the mean discharge in cells of mean land rain: the yield of everything above, plus the surplus of
  full lakes upstream carried down their outflows. It is no longer the rain gathered.
- *Channels.* A head is a cell whose inflow from upstream Q and slope S give Q S^2 >= CHANNEL_C (Montgomery & Dietrich
  1992; the threshold does not change with the map's size). Any inflow over RIVER_ACC also makes a head, and no head has
  less than CHANNEL_A_MIN. Every cell below a head is channel, down to its lake or the sea. A head is judged by its
  inflow, not its own rain: a channel cell is water, and the rain on it never reaches the water cycle's streams.
- *Hydraulic geometry* (Leopold & Maddock 1953). Depth = RIVER_D x SHALLOW x (Q / RIVER_Q1)^0.4, at most SHALLOW, so every
  river stays wadeable. Width = sqrt(Q / RIVER_Q1) cells, from 1 to RIVER_W_MAX. A wider river covers the cells beside its
  channel that are at most RIVER_BANK (m; x SHALLOW before 2026-11-01) above the channel's bed, in a parabolic cross-section. FloodFrom of a
  river cell is its channel cell: floodplains and floods read that channel's depth and discharge.
- *The water cycle.* Lakes are processed upstream first (LakeRank). A lake rises LakeSill above its level at creation
  before it spills (0 for a full lake). What it spills runs down its outflow (LakeExit, along Recv) into the next lake
  (LakeDown) in the same update, and adds to the discharge of every river cell on the way. Only the sea's spill leaves
  the map (WbLakeSpill), so the budget closes as before. A flood's soaking is now taken from the level of the lake its
  river runs to, since the lakes are balanced first.
- New World fields (all land, listed in Checkpoint.LandFields): LakeSill, LakeExit, LakeDown, LakeRank. A save without
  them rebuilds its land, so no format change is needed.
- One-line hook in Climate.Rainfall: over open water the air's previous height is the water's own height, not 0. Before,
  every high river cell made the next land cell downwind lift its air from sea level, a spurious orographic spike. That
  spike moved enough rain to put the land's ET share at 0.24, outside WaterCycleTests' range. Continentality
  (Climate.Build's Cont) is now measured from the sea and the lakes only: a river damps no seasons, and with the dense
  network every cell had become maritime. BuildWater does this by passing Climate.Build a DistWater to standing water,
  then measuring DistWater to all water.
After (same seeds):
- Sea 10.3-10.5%; lakes 0.2-2.2% in 6-10 lakes, nearly all full and spilling (the climate is humid: P_MEAN 800 mm).
- Rivers 2.2-4.3% of the map, with channels 2.1-3.6% and 52-85 heads. Strahler order 2-3. The longest river is 91-201
  cells, and 80-85% of mouths reach the sea.
- Width 1-5 cells, with cross-sections up to 9-12 cells where rivers join. Depth 24-126 m (0.2-1 x SHALLOW).
- Drinkable water is 5.4-6.5 cells away on average. Generation time is unchanged: Waters takes ~0.3 s at 1024.
Tests: HydrologyTests checks that rivers run continuously to a lake or the sea, the hydraulic geometry, that lakes stand
at or below their spill points, and that closed-basin lakes follow their water balance (a synthetic bowl: humid full and
spilling, semi-arid terminal and smaller). WaterCycleTests checks that lakes spill down their outflow rivers. The dry-spell
test now measures baseflow at the mouths of major rivers (two cells wide or more): the many small new streams drain
small steep catchments with little groundwater. It also checks that channel cells keep flowing. Pond sites on seed 4 fell
from 102 to 60: hollows big enough hold lakes, and pond sites keep POND_GAP from the denser river network. In a wet year
13 of them hold water, against 3 after a drought; before, it was 53 against 37. Most spring-fed ponds are gone: the deep
groundwater now emerges into the first river it meets. ClimateTests' no-climate hash is re-pinned (0x996581FE746A3C7C).
Not done here (other owners):
- The 3D view draws a lake's surface at the sea's height only below SeaLevel. A lake above it should be drawn at
  Elev + Depth, which is flat across the lake.
- Pathogens.BuildNet counts water at or above SeaLevel as river, so lakes above sea join its reservoir network.
- Geology's deep time grades the land to every region below the quantile. Grading it to the connected sea alone (lakes
  filling and spilling, outlets cut) would make SEA_MIN = 1 consistent.

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
it kept and rebuilt. Version 19 gives a whole array's byte count as i64 and copies every array through spans at long
offsets, a chunk at a time, so an array over 2 GB (W2f, Trace at ~39,000 slots or more) saves and loads; a save of format
18 (i32 counts) is migrated with everything kept. Startup: a world that is about to be loaded from a save builds no land and no founders (restore);
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

**Relief that looks like land (geomorphology)** (2026-10-31; Geology.cs, Hydrology.Erode; the user's screenshots showed
flat-topped plateaus behind near-vertical cliffs). Measured before (1024 map, seeds 3, 7, 11; tools/TerrainProbe, images
in docs/art/terrain/geo_before_*): 3-12% of the coarse land stood at the 5 km cap (GEO_H_MAX), the top tenth of the relief
held 0.3-12% of the land, and 7-22% of the land above half the relief was flat (< 3°). As the 3D view draws it, 13-18% of
the land was steeper than 45° (p99 66-68°). The main cause was a scale error: slopes were made on the plates' 39 km cells
and shown on 218 m ones (the 3D view draws H_RELIEF over 55 units and a field cell over 4), ~90 times steeper; the cap
then cut the belts flat, and an erosion threshold kept the flats. The top-view repeats are the 3D view drawing the torus
3 x 3 when zoomed out: the relief has no shorter period (autocorrelation at shifts beyond a quarter map at most 0.14-0.33, a long belt, not a
repeat; at most 0.03 for its detail). Changes:
- *Two scales.* The plates (belts, rifts, flexure) keep GEO_KM; the land surface (slopes, drainage area, hillslopes) is
  measured at GEO_CELL_M = 218 m a field cell, the scale it is shown at.
- *No cap.* Uplift has no ceiling; erosion that rises steeply with relief bounds it (Montgomery & Brandon 2002):
  stream power E = K A^(n/2) S^n with m/n = 0.5 and n = 2 (Whipple & Tucker 1999; Lague 2014), solved implicitly and
  never below the lake a river runs into; critical-slope failure at GEO_SC = 0.6 (31°), its debris carried with the load;
  nonlinear hillslope diffusion D S / (1 - (S/Sc)^2) (Roering et al. 1999). K is 2-8 km^-1/Myr by rock.
- *The last of deep time at full resolution.* After 200 coarse steps, the relief, its sediment and its rock uplift (the
  plates' plus the flexural rebound the coarse grid ended with) are upsampled and 40 more steps are run on the field grid,
  so valleys and the ridges between them are cut rather than interpolated. The relief is scaled down to stand at most
  H_RELIEF above the lakes, at the start and 10 steps from the end (never up: the 256 map's 14 km of land holds only
  1.5-2.2 km at slopes it can stand at); Elev is then H_RELIEF / (1 - SeaLevel) m per unit (scaled down again if the last
  steps raised the top above H_RELIEF), so slopes are what deep time made. On the field grid lake floors sink
  under their fill (as the coarse grid's flexure does), so rift lakes are not filled to the brim.
- *Fine passes.* Hydrology.Erode takes the same critical slope (sc, as elevation per cell); rivers cut down to their lake's
  level, not below it, and creep into a lake stops at its level, so the lake share is the Nature.Water quantile exactly.
Measured after (1024 map, seeds 3, 7, 11; geo_after_*): no cap, nothing flat on high ground (0.3-0.8% of the land above half
the relief), the top tenth of the relief 0.1-0.2% of the land; slopes as drawn p50 12-19°, p99 31.5-32°, p99.9 under 33°;
lowland channels have wide floors (47-69% of the cells within 4 of them lie within 30 m of their height) and upland ones
narrow V's (16-17%); hypsometric integral 0.18-0.23 (it was 0.27-0.38), median land 390-580 m (600-790); lakes 10.0%
(10.3-10.5). Land rock: crystalline 61-69%, sedimentary 26-35%, volcanic 4-5%; alluvial land (> ALLUV_D) 11-15% (it was
9-11%). Rivers 0.78-0.86% of the map, 0.25% for seed 7 (a dissected upland between many lakes). Generation: Terrain.Build
2.4 s at 1024 and 9.7 s at 2048 on 4 shared cores (load ~6). GeologyTests now test no plateau at a cap, bounded slopes,
no periodicity and valley shape; the rift test asks for twice the map's lake share in rifts (24-41%; erosion now fills
some rifts), and the lake share is the quantile ±1 cell. Not done: the 3D view's vertical scale and torus tiling (world3d.js).
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
  SENSE_WATER = 8 cells (32 units, 1.74 km: the horizon of an eye ~0.24 m above flat ground, 2026-11-01; it was set
  equal to the old MAX_SIGHT); beyond it they give no direction.
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

**Body sizes are a world setting** (2026-11-01, the owner: "we want more customizability on the size floors - why not be
able to do a world of giants on purpose"): the setup screen's Bodies group (and --bodies, --body-min, --body-max,
--body-floors) chooses the mass gene's range, 0.001 to 50,000 kg (BodySize; presets Normal 0.1-40 kg, Giants 100 kg-20 t,
Tiny 1 g-1 kg), and the size classes' floors: by default the same log proportions of the range as 0.1, 1, 3 and 10 are of
0.1-40, or set one by one. The mass gene maps on a log scale onto the range, so a mutation changes a body by a share of its
size. The smallest newborn is a tenth of the smallest body (NEWBORN_MIN at the default). What depended on the largest body
is the world's own: the farthest eye (BodySize.MaxSight, x (Max/40)^0.2), the farthest call (MaxHear, x (Max/40)^(1/3)) and
the spatial-hash cells (Map.HCell, Map.HCellCall: the smallest divisors of the map side that hold a step's largest
perception and the loudest call; a grid narrower than its neighbourhood searches each cell once). Every other law of a body
is its own physics of its mass: flight stops near 16 kg whatever the range, giants overheat and hold their breath longer,
walk the day ranges of their size (M^0.25) and live longer. Saved in checkpoints and world saves from format 23; older
saves load with the default. With the default range a run is bit for bit what it was. Real densities fall with size
(Damuth 1981: log10 D/km2 = 1.30 - 0.66 log10 M), so the setup screen shows what the chosen map would hold of real animals
of these sizes and warns that giants need a much bigger map; the map is never resized for you. One law had to be mended
for giants: the leg-load term of walking speed (LOAD_DRAG x M^(1/3) x ...) is negative for bodies whose legs share the
load better than the reference, and above ~14 t with three or four leg pairs made the speed negative or infinite; the
gain is now at most twice the plain body's speed (it never binds up to 1.7 t).

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
- Range is the caller's: a call carries HEAR_R x voice x M^(1/3) (HEAR_R_M = 870 m = 16 units), at most MAX_HEAR (3 km),
  so the 5x5 callers' hash neighbourhood (HCELL_CALL = 32, its own grid since 2026-11-01) holds every caller a listener
  can hear. BUILD sorts this step's callers by callers' hash cell
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
  prehistory branch has 49,152 slots so that food limits it first. (Since 2026-10-31 a world grows as
  births need slots, §3: only its memory limit can bind.)
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

## 0b. Real units (2026-11-01)

The animals now live in the land's metres. A step is STEP_H = 2.922 h (T_SEASON = 3000 steps are 365.25 days), a unit is
M_PER_UNIT = 54.5 m (GEO_CELL_M / FCELL), masses are kg. Sight is 60 m x M^0.2 for a 1 kg target (eye size and acuity,
Kiltie 2000; Burton 2008), hearing 870 m x voice x M^(1/3), a step's top travel 632 m x M^0.25 (the day range, Garland
1983, over the share of top speed animals use, TRAVEL_SHARE), the cost of moving a metre Taylor et al.'s (1982) on
Kleiber's energy scale, a strike's reach the body plus a 15 s sprint at Garland's top speed, a founder group's spread the
home range of its body (HR_KM2 x M, Jetz et al. 2004). A step is hours, so what an animal perceives is what lay within
sight of the way it travelled (Decide.Perceive: the disc of the band's area, Gerritsen & Strickler 1977), and a strike or
a mating reaches along that way (Resolve.AlongWay). The bodies' hash cell (HCELL = 8, 7x7 cells) is sized from the
largest perception, the callers' (HCELL_CALL = 32, 5x5) from the loudest call (both wider in a world of bigger bodies: Map.HCell, Map.HCellCall). docs/CONSTANTS.md group Units has every
value and its source; docs/ICD.md §2.1 what the console gets.

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
cells). The spatial hash has cells of 16 units (GW = 64). C = capacity = the agent slots the world
has now; it starts at 16,384 (`--capacity`) and grows (2026-10-31). Eggs and embryos occupy slots too.

**Growth.** There is no fixed animal cap. Every per-slot array of World (and of its derived cache,
World.D) is marked `[PerSlot]`; World.Resize reallocates them all. Between steps the world keeps at
least MIN_FREE (256) slots and an eighth of its slots free, growing to 3/2 of its size (in blocks of
1,024) when it has fewer; a step that still runs out grows inside RESOLVE, where births are serial.
The genomes, working weights and traces (about 200 KB of a slot's 212 KB) are paged (SlotArray:
pages of 512 or 1,024 whole slots, 64 MB), so no one array holds every genome (an sbyte[] stops at
2^31 elements: 21,504 genomes) and growth never copies them. Growth changes nothing in the run: the
free slots are a stack whose bottom holds the never-used slots, lowest on top, so the new slots go
below every free one and each birth takes the slot a bigger world would have given it; RESOLVE's
order runs over a power of two above the slots ever used (World.SlotsUsed), not over C. The same seed
gives the same run from any starting capacity (EngineeringTests). A world stops growing only at its
memory limit (the machine's or container's memory, less an eighth of it and at least 1 GB, against
what the process holds) or at `--max-capacity`; only then are births refused and counted
(`SlotShortage`), the console shows **WORLD FULL** with the reason, and a run full on more than 1% of
steps is reported INVALID, because that is a limit of the computer, not an ecological result.

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

(The per-slot sizes above are of the 36-unit brain. With 256-unit brains a slot takes about 212 KB,
World.SlotBytes: the genome 96 KB, W2f and traces 54 KB each, the learning history 6 KB, the rest
about 3 KB.) Memory scales with the slots the world has grown to, so with the animals it holds.

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
| mass | log range of the world's body sizes (BodySize; MASS_LO..MASS_HI by default) | all of allometry (§9.1) |
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
organism out more slowly (Revision G). Eating relieves thirst by K_FOODWATER·(bite·w/(1 − w) + MET_WATER·
energy digested)/M (Revision K; since 2026-11-01 the bite is dry matter and w the fresh food's water share): green plants, fruit and prey are wet, seed and wood dry, and live
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
| movement cost | on the ground C_MOVE·C_SPEED·M^EXP_MOVE·speed·(1 − LEG_SWING + LEG_SWING·legs·pairs/LEG_PAIRS_REF·speed/vmax) (cost of transport ~M^0.7 × speed, Taylor et al. 1982; leg swing, Marsh et al. 2004; no swing swimming); airborne FlightPowerMin(M)·(3/u + u³)/4 − basal, u = speed/v_mp ≥ FLY_U_MIN (§9.2b); all × (1 + INS_MOVE·fur) |
| fatigue | +c_F·g_SPRINT·speed/vmax; −c_rec (×3 resting) |
| thirst | +c_θ·(1 − SOIL_WATER·moisture) per step on land cells; 0 in water |
| growth | a juvenile spends min(energy above GROW_RESERVE·Emax, TISSUE_E·M·(1 − Birth)/A_m/GROWTH_EFF) per step on tissue and matures on reaching adult mass (Revision D, §15) |
| reserve | Emax = C_EMAX·M with C_EMAX = 2: a stored reserve of the same order as tissue energy |

### 9.2 Movement and realm

- The heading turns toward the desired heading by at most the turn rate.
- A move into a cell outside the stage's realm is refused. The animal stays where it is and turns by
  ±90° (hashed).
- Realms (Revision G): every mobile organism may move on land and in shallow water; deep water needs
  enough fin (DEEP_SWIM); a flyer goes anywhere while airborne, and where it can stand when landed (§9.2b). Its top speed depends on the cell it is in: in the air
  for a flyer, otherwise its land speed on land, its water speed in deep water, and the better of its
  water speed and 0.6 × its land speed in shallows.
- Settled organisms have speed 0.

### 9.2b Locomotion physics (2026-10-31)

- **Who flies.** Power available from flight muscle ~ wings·M^0.7, least power required ~ M^(7/6)
  (Pennycuick 1968, 2008; Ellington 1991). A body flies when the margin wings·(FLY_M_MAX/M)^(7/6 − 0.7) ≥ 1:
  nothing above FLY_M_MAX = 16 flies (M = 1 is taken as ~1 kg; the heaviest fliers are 12-18 kg). Takeoff also
  needs wings ≥ FLY_WING_MIN at any size (flight muscle ≥ ~16% of body mass, Marden 1987).
- **Flight costs by the hour.** The least power of flight is FlightPowerMin(M) = FLIGHT_MULT·C_BASAL·M^(7/6)
  (10× basal at M = 1; Tucker 1970, Norberg 1990), at the speed of least power v_mp. The power curve is U-shaped:
  P = P_min·(3/u + u³)/4, u = v/v_mp (induced power ~1/u, profile and parasite ~u³); hovering (u below
  FLY_U_MIN) costs 2× P_min. The speed of least power is FLY_VMP = 4× the walking-speed allometry (birds cruise
  5-15 m/s, walkers travel 1-2 m/s); top air speed is v_mp·x where the curve meets the margin, at most
  FLY_X_MAX = 1.6 (1.49× P_min there) and at most FLY_V_CAP = 128 units/step (7 km; 2026-11-01: an animal perceives
  along the way it travelled, out to MAX_PERCEIVE). A step is hours, so its speed is a step's travel: a flier that could land
  is in the air only for the share of the step its distance needs, spd / FlightFullStep(M), and pays the power for
  that share; FlightFullStep is calibrated so flying at v_mp costs FLY_COT_RATIO = 0.4 of walking per distance at
  M = 1 (Tucker 1970; Schmidt-Nielsen 1972), growing as M^0.3 (0.17 at M = 0.1, 0.93 at M = 13). A flier that
  cannot land where it is pays the whole step's power, moving or not. Walking's cost per step at top speed is
  ~1.0-1.3× basal net, below Taylor et al. 1982 for continuous walking (~4.7× BMR at 1.5 m/s for 1 kg), as befits a
  step's average travel: locomotion is ~8% of this world's spending, as in field energy budgets.
- **Landed.** There is no "take off" output. A flier that rests, eats food that is here, drinks water within
  reach, or goes no faster than it walks,
  lands wherever it can stand (land; shallow water if it has legs or swims; deep water if it swims well enough)
  and walks on its legs at its ground speed, paying basal and the walking cost only. It stays airborne only over
  ground it cannot stand on, or when travelling faster than its legs carry it. It eats and drinks only landed.
- **Diet reach.** A flier digests water-column food (phyto, algae, detritus) only if it also wades on legs or swims.
- **Legs.** Legs have no upkeep; they pay a swing cost while walking: the stance share (1 − LEG_SWING) of the cost
  of transport every walker pays, the swing share grows with leg size × pairs (against LEG_PAIRS_REF = 2) and with
  speed against its top speed. On rough ground (the largest rise to a neighbour cell, ROUGH_REF fully rough) a body
  walks at 1/(1 + ROUGH_SLOW·rough·M^(−1/3)·(1 − stability)), stability = LEG_STAB·legs·(pairs − 1)/3: many legs
  steady a small body (Ting, Blickhan & Full 1994). The legs carry the body's weight: land speed is divided by
  1 + LOAD_DRAG·M^(1/3)·((1 + armour)/√pairs − 1/√2) (bone stress ~M^(1/3), Biewener 1989), so more legs help most
  a heavy or armoured body; in water and air armour adds ARMOUR_DRAG·armour/√2 of drag.

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

On death, the body becomes CARRION in land cells or DETRITUS in water cells: (TISSUE_E·M + Energy)/ρ_meat kg of dry
meat (2026-11-01; c_carc·M + Energy/ρ_meat before). Eggs and embryos of a dead mother die.

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
| Growth and offspring tissue cost energy (GROWTH_EFF, TISSUE_E; C_CARC and RHO_MEAT before 2026-11-01) | a body was free to build, but yielded energy when eaten: energy from nothing |
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
