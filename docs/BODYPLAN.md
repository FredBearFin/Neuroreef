# NEUROREEF: OPEN-ENDED BODY PLANS (design and prototype)

Status: design, plus a working prototype in `src/Neuroreef.Core/Morph/` (namespace `Neuroreef.Morph`).
The prototype is **not wired into the live simulation**. Tests are in `tests/Neuroreef.Tests/MorphTests.cs`.

## 1. The problem

In an all-ocean world no fish and no crustaceans appeared. The first idea was to add exoskeleton and jointed-leg
genes. The owner's answer was: *"you shouldn't need to add genes, you need to add the ability for genes to get
created."*

Today the body genome is a fixed list of labelled genes (`G.B_LEGS`, `B_FINS`, `B_WINGS`, `B_LEGN`, `B_FINN`,
`B_ARMOUR`, `B_LUNGS`, `B_MASS`, the shape genes `B_LENGTH`..`B_WING_SHAPE`, and so on). `Body.Express` maps each label
to a capability: fins give swim speed, wings give flight, legs give land speed. Evolution can tune organs we
designed, but it cannot invent one. A fin is a fin because we named it, and nothing can become a fin that we did
not call one.

The design below has three parts:

- a **variable-length genome of modules**, which gene duplication, divergence and loss can grow and change;
- **development** from genome to a physical morphology;
- **capabilities from physics** applied to that morphology.

Nothing is labelled. A "fin", "leg", "wing", "gill", "lung" or "shell" is a region of parameter space that the
physics happens to reward in some medium.

## 2. The genome: modules, not organs

A body genome is a list of **segment genes** (at most `MAX_SEG_GENES` = 8 in the prototype). Each segment gene carries
a list of **appendage genes** (at most 4). Every parameter is a number in [0,1] that development maps to a physical
range (logarithmic for sizes).

| Segment gene | Physical meaning |
|---|---|
| `len`, `wid`, `hei` | segment proportions (log 5–150 mm before the body is scaled to its mass) |
| `repeat` | serial copies grown from this one gene, 1–4 (metamerism) |
| `taper` | each copy's size relative to the one before (0.7–1.3): a positional gradient along the series |
| `intT`, `intMin` | integument thickness (1–30% of the local half-thickness) and mineral share 0..1: soft skin to mineralised cuticle |
| `pore`, `invag` | share of the surface that is thin gas-exchange epithelium (folded ×10), and the share of that folded *inward* (a pocket, lung or chamber) rather than everted |
| `axMus` | axial (trunk-bending) muscle share |

| Appendage gene (grows a pair) | Physical meaning |
|---|---|
| `len`, `chord`, `thick` | length, width and thickness (thickness ≤ width) |
| `droop` | angle below horizontal: 0 is held out sideways, 90° points straight down |
| `roll` | ≥ 0.5: the pair grows in the body's midplane, one blade up and one down, instead of left and right |
| `joints` | 0–6 articulations |
| `muscle` | the share of its segment's tissue that drives it |
| `mineral`, `wall`, `exo` | skeleton: mineral share, share of the cross-section that is skeleton, and the share of that placed as an outer tube (exoskeleton) rather than a central rod (endoskeleton) |
| `pore`, `invag` | exchange surface on the appendage, as for segments |

**Mutation operators** (`Mutate`, `MorphGenome.cs`). These change the *number* of parts as well as their values,
as gene duplication and loss do in Hox clusters and limb fields (Ohno 1970; Carroll, Grenier & Weatherbee 2005):

- **point**: each parameter mutates with probability 0.02, by N(0, 0.08). This is a per-site rate, so a larger
  genome carries more mutations.
- **diverge**: a whole module's parameters are nudged together, which lets duplicated copies specialise.
- **duplicate segment**: a tandem copy, with each copy half as long. The split is near-neutral (the body barely
  changes; tested), so selection does not cull the copy before it can diverge. This is Ohno's route to new function.
- **delete segment**: never removes the last segment.
- **duplicate appendage**: copies onto the same segment or a neighbour (a transposition). The two copies share the
  original's muscle.
- **delete appendage**.
- **insert appendage**: a tiny bud with random parameters (de novo outgrowth).
- **repeat count**: an ordinary parameter, so a segment gene can become a series.

Artificial-life precedents. Sims (1994) evolved directed-graph genotypes whose nodes are body parts with recursive
repeat limits. This is the direct ancestor of this design: his "repeat" and "duplicate a node" operators are ours.
Stanley's NEAT (Stanley & Miikkulainen 2002) shows how to grow a genome by complexification. Historical markings
(innovation numbers) let homologous genes line up for crossover, and speciation protects new structure while it
is still poor. CPPNs (Stanley 2007) give a compact way to describe graded, repeating patterns along a body. The
prototype is asexual. Its `Pair.Gene` and `Segment.Gene` indices are the place where NEAT-style innovation numbers
would go (§7).

## 3. Development: genome to morphology

`Development.Grow`:

1. Each segment gene grows `repeat` serial copies, each scaled by `taper`. Every appendage gene grows a bilateral
   (or sagittal) pair on every copy, scaled with it. Serial homologues are therefore graded, as Hox expression
   grades them.
2. The whole body is scaled to a fixed mass (`BodyMass` = 0.25 kg). This works the way today's sim keeps mass as its
   own gene and its shape genes at unit volume (`Body.Dimensions`). Limbs, armour and gills take mass *from* the
   trunk and its gut, not on top of it. Without this, food capture (∝ size^(2/3) × speed) slightly outruns Kleiber's
   cost (∝ M^0.75), and every body ran to the largest its genes allowed.
3. The morphology then holds only physical quantities:
   - segments: L, W, H, integument thickness and mineral, exchange area, muscle mass, gut mass;
   - appendage pairs: length, chord, thickness, angle, joints, muscle mass, the bending moment the skeleton bears,
     articulation, and the thickness of the outer tube;
   - body aggregates: mass, volume (and so density), fineness, frontal and wetted area, steps in cross-section,
     everted and folded-in exchange area, skin area and thickness, integument and exoskeleton mass, rigidity, and
     inert (mineral) mass.

**Skeleton.** An appendage's section is an ellipse (semi-axes chord/2 and thick/2). Its skeleton area (`wall` ×
section) is split between an outer tube (`exo`) and a central rod. Second moments are I = π/4 a b³ (1 − k⁴) for the
tube and π r⁴/4 for the rod. Failure stress is log-interpolated from 0.3 MPa (a soft, hydrostatic support) to 150 MPa
(mineralised cuticle or bone; Currey 2002; Vincent 1990). For the same mass, a tube bears about twice the moment of
a rod (Alexander 1983). **Articulation** = 1 − exp(−(joints + 1.5 (1 − mineral)) / 1.2): a rigid limb moves only at its
joints, and a soft one bends anywhere (a hydrostat) but bears little.

## 4. Capabilities from physics (`Physics.cs`)

Every evaluator reads only the morphology and the medium.

**Swimming** (Vogel 1994; Lighthill 1975; Webb 1975; Walker & Westneat 2000):

- **Trunk drag.** Skin friction on the wetted area with Hoerner's (1965) form factor,
  C_D,wet = C_f (1 + 1.5 f^−1.5 + 7 f^−3) for fineness f = length / √(W H). It is capped at a bluff body's 1.2 ×
  frontal area, plus 0.3 × any steps in cross-section. Least drag for the volume is near f ≈ 4–5, as in fishes and
  cetaceans (tested). An early version used Hoerner's body-of-revolution formula on *frontal* area; it rewarded
  sheet-like bodies, because a sheet has a small equivalent diameter but a huge wetted area.
- **Appendage drag.** Edge-on frontal drag (rounded rods worst) plus friction on both faces.
- **Holding depth.** A body denser than water must be held up: |M − ρV| g. Dynamic lift comes from horizontal flat
  surfaces (and a flattened trunk), costing Prandtl's induced drag L² / (q π b² e). Whatever lift does not cover is
  hovered on the actuator disk of its propulsors.
- **Thrust.** Each moving surface (an appendage with muscle and articulation, or the trunk itself when it has axial
  muscle and can bend) contributes muscle power and swept area.
  - Froude efficiency of the combined actuator disk: 2 / (1 + √(1 + C_T)).
  - Profile efficiency, lift-based: 0.9 · AR/(AR+2) · φ/(φ+2), with φ = chord/thickness (a rod makes no lift).
  - Profile efficiency, drag-based paddling: at most ~0.3.
  - Axial undulation: up to 0.8, by elongation and by trunk flexibility. A rigid integument blocks bending except
    between segments.
  - A flat sagittal blade on the hind third deepens the trailing edge that undulation sheds its wake from
    (Lighthill's added mass).
- **Weak swimmers.** A body too weak to hold a pace or its depth swims flat out for the share of the time its muscle
  allows.

**Walking and crawling** (Alexander 1981, 2003; Biewener 1989; Full & Koditschek 1999; Kram & Taylor 1990;
Taylor, Heglund & Maloiy 1982):

- **Weight.** Weight in the medium is (M − ρV) g. Water bears ~95% of it (tested).
- **Per-limb support.** A limb's foot reaches the ground if len · sin(droop) clears the half-height. It bears the
  lesser of two limits, each held to a safety factor of 2:
  - skeleton moment / lever, where lever = len (cos(droop) + 0.25);
  - muscle force: 200 kPa × muscle cross-section × EMA 0.25.

  A stiff, muscled limb holds a body up on land; soft or slack limbs do not (tested).
- **Static stability.** At duty factor 0.5, a tripod on the ground needs six legs.
- **Costs.**
  - Support follows Kram & Taylor: KT · W · U / stride, with KT calibrated to Taylor et al.'s 10.7 M^−0.32 J/(kg m)
    at 0.25 kg.
  - Limb swing: ~4 m_leg U³ / stride.
  - Whatever the limbs do not hold up slides at μ = 0.7.
  - A legless body crawls at μ = 0.7 on its weight (at least 10% of its mass pressing down, for adhesion; Denny 1980),
    powered by axial muscle × trunk flexibility.
- **Obstacles** of height h: effective speed is × clear / (clear + h), where clear = half the mean height plus what the
  limbs lift (Watson et al. 2002). Legged gaits also lose speed for lack of a tripod on rough ground.
- **Surge** on the sea floor: a body holds station when μ · weight-in-water + limb hook force ≥ the surge drag on
  its side area. A swimmer holds by thrust.

**Flight** (Pennycuick 2008; Ellington 1984; Dickinson, Lehmann & Sane 1999). Flight-muscle power (150 W/kg) times
lift-based profile efficiency, against the induced power of hovering on the actuator disk of the span:
1.2 W^1.5 / √(2 ρ A). The margin grows with span and muscle and falls with size (power ∝ M against need ∝ M^(7/6)).
A 10 g body with long, flat, muscled appendages flies (tested). Flight is evaluated but not yet an environment.

**Gas exchange** (Fick's law; Schmidt-Nielsen 1997; Dejours 1981):

- **Supply** per m² of folded exchange area:

  | Surface | Water | Air |
  |---|---|---|
  | everted (flow-through) | 25 W/m² | 15 W/m² (wet lamellae collapse and stick) |
  | folded-in | 12 W/m² (a tidal pocket) | 100 W/m² |

  Skin breathes at 5% of an everted surface, falling e-fold per 0.2 mm of integument.
- **Ventilation cost** as a share of the metabolism served: water everted 10%, water folded-in 25%, air 2–4%. Water
  is ~800× denser than air and carries ~30× less oxygen.
- **Surface costs.** Exchange surfaces cost tissue upkeep (0.3 W/m²) in any medium, plus ion pumping in water
  (0.5 W/m²). In air, everted wet surfaces lose water heavily (10 W/m²), lungs lose water in proportion to the oxygen
  they deliver, and skin loses water by its thickness.
- **Oxygen debt.** Demand that the surfaces cannot meet is paid anaerobically at 3× its energy. So exchange area
  evolves to match demand, which it does in every run.

**Integument** (Vermeij 1987 on shell strength and predation):

- **Protection.** A plate crushes at ~4 σ t². Kill chance per attack = 1 / (1 + F_crush / F_bite), averaged over the
  surface. Everted exchange surfaces are unprotected; folded-in ones are covered.
- **Costs.** Integument mass adds weight in water and on land. Mineral is metabolically inert, so it is excluded from
  Kleiber's mass.
- **Moulting.** A rigid outer skeleton must be moulted to grow. It spends 5% × rigidity of its life soft (killed by
  any attack, and unable to flee), and loses half its cuticle per moult (4 MJ/kg every 30 days, i.e.
  0.77 W/kg × rigidity × exoskeleton mass).

**Budget and fitness** (`Evaluate`):

- For each way of moving that the medium allows (swim; walk or crawl on a floor), at 48 speeds:
  - intake is ASSIM × food × duty × hold, capped by the gut (5 W per kg of gut);
  - food is swept volume (open water: 0.5 V^(2/3) × U × J/m³) or swept area (floor: 0.8 V^(1/3) × U × J/m²).
- **Demand** = Kleiber 0.3 W kg^−0.75 on the active mass + locomotion / 0.25 + integument turnover and moult.
- **Net** = intake − demand − ventilation − anaerobic debt − surface costs.
- **Predation.**
  - Encounters rise with the body's own speed: √(u² + v²)/v (Gerritsen & Strickler 1977).
  - An attack kills by the armour's kill chance unless the body outruns the predator (top speed against the
    predator's), and always kills during a moult.
- **Fitness** = net / basal − pressure × encounters × risk, at the best speed and mode.

**Environments** (`MorphEnv`). These are the only places the three worlds differ:

| | medium | food | predation (pressure, speed, bite) | floor |
|---|---|---|---|---|
| open water | water | plankton 900 J/m³ | 0.5, 1.5 m/s, 10 N | none |
| sea floor | water | benthic 300 J/m² | 3.0, 1.5 m/s, 10 N | boulders: roughness 1, obstacles 3 cm, surge 0.3 m/s |
| land | air | 500 J/m² | 0.5, 1.0 m/s, 10 N | roughness 0.5, obstacles 1 cm |

## 5. Brains on a variable body

Use a **fixed abstract action set** that the morphology executes. That is what the live brain already has:
`P.N` primitives (approach, flee, eat, strike, sprint, and so on) and `F.N` senses. A primitive says *what* to do; the
body's physics says *how fast and at what cost*:

- SPRINT runs at the top speed the morphology allows in that medium;
- STRIKE uses the stiffest, most muscled appendage's force;
- EAT is limited by the gut.

The brain genome stays fixed-length, so no brain re-wiring is needed when a limb is duplicated. Brains and bodies
evolve independently, as they do today.

Per-module sensors and actuators, in the style of Sims's per-part neurons, are a later, optional step. A module
could carry its own sense inputs (touch, a stretch receptor, a local light sensor), feeding a shared pool through a
small fixed set of *slots*: the first k modules by size. That allows a modular mind without changing the brain
layout. Not recommended before the body itself works in the live sim.

## 6. Rendering from modules

`js/creatures/body.js` today draws one parametric trunk tube with legs, fins, wings and eyes from labelled genes.
With modules, it builds:

- the trunk tube from the segment list (each segment a ring at its L, W and H, joined in sequence, so taper and
  steps show);
- each appendage pair as a tapered blade or tube of its length, chord and thickness, at its droop or in the
  midplane, with segment joints at its joint count;
- colour, sheen and roughness from the integument (mineral rigidity and thickness read as a hard, glossy plate;
  soft skin as matte);
- exchange surfaces as feathery fringes where they are everted.

The server sends the morphology (or the genome, which the client develops with a port of `Development.Grow`),
capped at 32 segments and 128 pairs. Animation comes from what the physics chose: undulation amplitude from axial
muscle; limbs stepping in alternating tripods when the body walks; flat blades stroking when it swims with them. A
new form therefore *looks* new, because nothing is drawn from a label.

## 7. Saves, gene bank, telemetry, species

- **Storage.** World slots are fixed-size arrays (`SlotArray`, `G.LEN`). Store the body genome in a fixed-*capacity*
  block with a count header:
  - 8 segment genes × (10 + 4 × 12) parameters at one byte each, plus counts: ~470 bytes (today's `NBODY` is 70);
  - genes beyond the count are ignored;
  - duplication and deletion shift entries within the block.

  Checkpoint and `GeneBank` get a version bump, and a v13 file translates through the migration map (§8).
- **Homology for crossover and distance.** Each module gene carries an innovation number (a `uint` assigned at
  duplication or insertion and copied with the gene), as in NEAT. Crossover aligns genes by innovation number. Clade
  distance (`Clades`, `Body.Distance`) is:
  - a NEAT-style compatibility: c1 × unmatched genes / N, plus c2 × mean parameter difference of matched genes;
  - plus a *morphological* distance on the developed body (segment and pair counts, fineness, density, rigidity,
    exchange areas, the length-weighted means of limb AR, flatness, droop, joints and mineral).

  Appearance clustering uses the morphological part.
- **Telemetry.** Report the developed body, not genes:
  - segment and pair counts;
  - the distribution of appendages in (AR, flatness, droop, joints, mineral, muscle) space, which is how a viewer
    sees fins, legs and wings appear without anyone naming them;
  - moulting share, exchange areas by placement, density.

  `Evolver.Summary` is the prototype of that record.

## 8. What the prototype evolved (honest report)

**Harness.** `Evolver.Run`: 200 genomes, 1,000 generations, tournament of 3, 2 elites, asexual, all operators. The
population starts from `MorphGenome.Blob()`, one soft cube-ish segment with no appendages and no exchange surface,
which breathes through its skin. A run takes about 1 s per environment.

To reproduce:

```sh
NR_MORPH_SEEDS=6 NR_MORPH_GENS=1000 dotnet test tests/Neuroreef.Tests -c Release --filter "FullyQualifiedName~MorphTests.EvolutionDemo" --logger "console;verbosity=detailed"
```

The run reported here used 6 seeds (the test defaults to 3 seeds and 800 generations); the best of each run is shown
(2026-11-02):

| env | seeds | segments | pairs | fineness | rigidity | integ. % | moult | everted exch. m² | folded-in m² | mode, speed | appendages (AR, flatness, droop, joints, mineral, muscle) | pKill |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| open water | 6/6 | 7–8 | 0–1 | 6.3–8.2 | 0 | 1.3–1.4 | 0 | 0.0048–0.0051 | 0 | swim 0.38 m/s | when present (4/6): one lateral pair, AR 2–7, flat 7–11, droop 0°, 2 joints, no mineral, **no muscle** | 1.0 |
| sea floor | 6/6 | 8 | 0–1 | 5.6–7.9 | 0 | 1.2–1.7 | 0 | 0.0030–0.0040 | ≤ 0.0001 | swim 0.15–0.20 m/s | one small pair (AR 4–23, flat 4–8), mostly lateral, no muscle | 1.0 |
| land | 6/6 | 3–5 | 0 | 0.37–0.42 | 0 | 5.3–6.0 | 0 | 0 | 0.0031 | crawl 0.03 m/s | none | 0.96 |

What did and did not emerge:

- **Open water: fish-like, partially.** Every run produced the same body plan:
  - an elongate trunk at fineness 6–8, the least-drag range for its volume;
  - axial undulatory swimming;
  - a thin, soft integument;
  - exchange surface that is entirely everted (gill-like), sized to demand (O2 supply equal to demand);
  - a cruise at ~1.5 body lengths/s.

  The trunk is *dorsoventrally flattened* (W ≈ 4× H): the flat body itself carries the lift that holds a slightly
  dense body up. In 4 of 6 runs a flat, lateral appendage pair appeared (flatness 7–11). It is a passive lifting
  surface with no muscle, not a flapping fin. Paired, muscled, lift-based fins did **not** evolve, because axial
  undulation is the more efficient propulsor at this size. In the real ocean too, most fish swim with the body and
  caudal fin, and the paired fins mostly steer and lift. Steering and manoeuvring are not modelled.
- **Sea floor: not crustacean-like.** In the final calibration, all 6 runs evolved the same soft, flattened axial
  swimmer as open water, foraging near the bottom. It pays the heavy predation (pKill 1, mortality ~1.4 against
  ~0.24 in open water).

  The armoured optimum exists and is fitter, but search rarely reaches it. In an earlier calibration (identical
  except that obstacle clearance used the *maximum* rather than the mean body height), one of four runs found a
  short, deep, fully rigid (mineral 1.0), moulting (5% of life soft) crawler with everted gills: pKill 0.09,
  fitness 1.98 against 1.5–1.6 for the swimmers. Getting there needs several genes to change at once: integument
  thickness and mineral on every segment, more gill to replace the skin breathing the armour smothers, and a switch
  from swimming to crawling. Search usually stalls short of that.

  **Many stiff jointed walking legs never evolved, on the sea floor or on land.** In water, weight is ~5% of the
  body's, so crawling on the belly is already cheap. Legs pay only through obstacle clearance and grip in surge,
  and they cost gut mass (body mass is fixed).
- **Land: internal air exchange yes; stiff limbs no.**
  - Every run folded its exchange surface fully inward (lung-like): wet everted surfaces collapse and dry out in air.
  - Every run thickened its integument (~0.3–1.2 mm) against desiccation.
  - No run grew supporting limbs. The bodies are legless crawlers that became tall and flat-sided, using their own
    height to clear 1 cm obstacles. That is an artefact of the obstacle model.

  `ProbeDesigns` sets a light, stiff, eight-legged walker by hand. Its legs do hold its weight (support 1.0,
  stability 1.0), but it scores *below* the crawler at this food yield. At 500 J/m², the gut limits intake at slow
  walking speeds, so mass spent on legs is pure loss. At 150–200 J/m², nothing that moves can pay its way (crawling
  or walking: Kram & Taylor's cost at 0.25 kg exceeds the food swept per metre). Between those yields, legs never
  became the best answer.

**Exploits found and fixed along the way.** All were found because selection is a ruthless auditor of the
physics, and every fix was a physical correction, not a nudge toward a target form:

- size ran away to the gene caps → the body is scaled to a fixed mass;
- a thin sheet breathing through its skin → skin breathing cut to a realistic share, and wetted-area drag;
- a disc whose end faces had no surface area → the ends are counted;
- legs bearing weight with no muscle → muscle-limited support;
- a belly-crawler penalised for lacking a tripod → stability applies to legged gaits only;
- a 20 cm hair-thin arm sweeping food for free → food gathering by appendages removed;
- lungs paying evaporation on capacity rather than use → paid on the oxygen delivered;
- appendage mass on top of a fixed trunk, and mineral metabolising → whole-body mass and an inert mineral fraction.

**Limits of the prototype:**

1. **Fixed size.** The body mass is fixed at 0.25 kg; a mass gene is the obvious next step. All scaling arguments
   (exoskeletons favoured when small, flight easier when small) are untested in evolution.
2. **No food handling.** Appendages do not catch, sweep or carry food. Claws, antennae, filter combs, mouthparts and
   raptorial limbs cannot be favoured.
3. **No steering, manoeuvring or stability in water.** Paired fins' main real job is absent. Surge holding is the only
   station-keeping physics.
4. **One predator** of fixed speed and bite per environment, with no arms race, no ambush model and no refuges.
5. **One environment per run, asexual, no ontogeny.** Moulting is a constant share of life, not modelled growth
   stages.
6. **Hand-set constants.** The supply per area, ventilation shares, evaporation costs, obstacle heights and food
   yields are order-of-magnitude values from the literature cited. Food yields in particular decide whether walking
   can pay at all.
7. **Sagittal orientation is a threshold** on the `roll` parameter (a 0.5 cut), not a continuous rotation.
8. **Search is weak.** Plain tournament selection, without speciation or diversity protection, found the armoured
   sea-floor optimum in only 1 of 4 seeds. NEAT-style speciation (§7) is the standard remedy.

The honest summary: the machinery is real. A variable-length genome grows, duplicates, diverges and deletes parts,
and the physics alone decides what each part does. Streamlining, gill-like everted exchange in water, lung-like
folded exchange in air, desiccation-resistant skin and matching exchange area to demand all **emerged without a
single labelled organ**. Rigid, moulting armour emerged but was rarely found. The crustacean's jointed legs and the
land walker's stiff limbs did **not** emerge. The probes say the physics as calibrated does not yet favour them:
these are the next things to model (food handling, a mass gene, steering, realistic obstacles), not things to add
as labels.

## 9. Migration and phased plan

**Today's genes as an initial module set.** This translator lets existing worlds, gene banks and ancestors be
expressed by the new body:

| today | modules |
|---|---|
| `B_LENGTH`, `B_WIDTH`, `B_HEIGHT`, `B_HEAD`, `B_NECK`, `B_TAIL` | 4–6 segment genes (head, neck, trunk, tail) with those proportions; the tail segment tapered |
| `B_MASS`, `B_FLOOR` | the mass gene (kept as is) |
| `B_LEGS` × `B_LEGN` pairs, `B_LIMB_POS`, `B_LIMB_SHAPE` | `B_LEGN` ventral appendage genes (droop ~70°, 3 joints, round section, mineral from bone, endoskeletal), length ∝ `B_LEGS`, placed by `B_LIMB_POS` |
| `B_FINS` × `B_FINN`, `B_FIN_SHAPE` | `B_FINN` lateral flat pairs (droop 0°, flatness ~8, AR from `B_FIN_SHAPE`, muscle ∝ `B_FINS`), plus a sagittal tail blade |
| `B_WINGS`, `B_WING_SHAPE` | one long, flat, high-AR, heavily muscled lateral pair |
| `B_ARMOUR` | integument thickness and mineral on every segment |
| `B_LUNGS` | the folded-in share of exchange surface (gills everted, lungs folded in), area sized to Kleiber demand |
| `B_APPEND` (radiators) | appendage area; `B_INSUL`, `B_ENDO`, the diet, life-history, brain and appearance genes stay as they are |

**Phases and effort.** This is the honest cost: every calibration and test today depends on today's body.

| phase | work | effort |
|---|---|---|
| P0 (done) | prototype, physics, operators, tests, evolution harness | — |
| P1 | finish the prototype's physics: mass gene, food handling by appendages, steering and manoeuvring, ambush and refuge predation, NEAT speciation and crossover with innovation numbers; until legs, armour and paired fins emerge for physical reasons in the harness | 2–3 weeks |
| P2 | shadow expression: translate every live genome (table above) and compute Morph capabilities *alongside* `Body.Express`; report both in telemetry; calibrate the module physics until the translated bodies of today's ancestors match today's speeds and costs within ~20% | 1–2 weeks |
| P3 | storage: a fixed-capacity, variable-count body block in the slot layout; checkpoint, `GeneBank` and AGENTS-format version bumps; a v13 → v14 translator; ICD update | 1 week |
| P4 | expression from morphology: replace every reader of `PLegs`, `PFins`, `PWings`, `PLegPairs`, `PFinPairs`, `PLungs` and armour (Resolve, Respiration, Heat, Decide, Reflex, Clades, Species, Telemetry) with morphology-derived speeds per medium, O2 supply, armour, moult state and strike force; mutate with the new operators in LIFECYCLE | 3–6 weeks |
| P5 | recalibration: the ~40 test files that assert body-dependent numbers (Locomotion, Wading, Diving, Respiration, Feeding, FeedingBudget, Founding, Ocean, Ecology, Metabolism, Appearance, StepLength, ...), the founding draw (`Clades.AncestorBody`) and the constants doc; long runs in each world type | 2–4 weeks |
| P6 | renderer: `body.js` builds from segments and pairs (§6); appearance clustering on the developed body | 1–2 weeks |
| P7 (optional) | per-module sensors in fixed brain slots (§5) | 2+ weeks |

Total: roughly **2.5–4.5 months** of focused work for P1–P6. The largest risks are P4 and P5:
the live ecology is tuned around today's speeds and costs, and every one of them will move.

## References

- Alexander, R. McN. (1981). Factors of safety in the structure of animals. *Sci. Prog.* 67.
- Alexander, R. McN. (1983). *Animal Mechanics*, 2nd ed. Blackwell.
- Alexander, R. McN. (2003). *Principles of Animal Locomotion*. Princeton.
- Askew, G. N. & Marsh, R. L. (2002). Muscle designed for maximum short-term power output: quail flight muscle. *J. Exp. Biol.* 205.
- Biewener, A. A. (1989). Scaling body support in mammals: limb posture and muscle mechanics. *Science* 245.
- Biewener, A. A. (2003). *Animal Locomotion*. Oxford.
- Carroll, S. B., Grenier, J. K. & Weatherbee, S. D. (2005). *From DNA to Diversity*, 2nd ed. Blackwell.
- Clarke, A. & Johnston, N. M. (1999). Scaling of metabolic rate with body mass and temperature in teleost fish. *J. Anim. Ecol.* 68.
- Currey, J. D. (2002). *Bones: Structure and Mechanics*. Princeton.
- Dejours, P. (1981). *Principles of Comparative Respiratory Physiology*. Elsevier.
- Denny, M. W. (1980). The role of gastropod pedal mucus in locomotion. *Nature* 285.
- Dickinson, M. H., Lehmann, F.-O. & Sane, S. P. (1999). Wing rotation and the aerodynamic basis of insect flight. *Science* 284.
- Dickinson, M. H., Farley, C. T., Full, R. J., Koehl, M. A. R., Kram, R. & Lehman, S. (2000). How animals move: an integrative view. *Science* 288.
- Ellington, C. P. (1984). The aerodynamics of hovering insect flight. *Phil. Trans. R. Soc. B* 305.
- Fish, F. E. (1993). Influence of hydrodynamic design and propulsive mode on mammalian swimming energetics. *Aust. J. Zool.* 42.
- Full, R. J. & Koditschek, D. E. (1999). Templates and anchors: neuromechanical hypotheses of legged locomotion on land. *J. Exp. Biol.* 202.
- Gerritsen, J. & Strickler, J. R. (1977). Encounter probabilities and community structure in zooplankton. *J. Fish. Res. Board Can.* 34.
- Hoerner, S. F. (1965). *Fluid-Dynamic Drag*. Hoerner.
- Kram, R. & Taylor, C. R. (1990). Energetics of running: a new perspective. *Nature* 346.
- Lighthill, M. J. (1975). *Mathematical Biofluiddynamics*. SIAM.
- Ohno, S. (1970). *Evolution by Gene Duplication*. Springer.
- Pennycuick, C. J. (2008). *Modelling the Flying Bird*. Academic Press.
- Schmidt-Nielsen, K. (1984). *Scaling: Why is Animal Size so Important?* Cambridge.
- Schmidt-Nielsen, K. (1997). *Animal Physiology*, 5th ed. Cambridge.
- Sims, K. (1994). Evolving virtual creatures. *SIGGRAPH '94*; Evolving 3D morphology and behavior by competition. *Artificial Life IV*.
- Stanley, K. O. & Miikkulainen, R. (2002). Evolving neural networks through augmenting topologies. *Evol. Comput.* 10.
- Stanley, K. O. (2007). Compositional pattern producing networks. *Genet. Program. Evolvable Mach.* 8.
- Taylor, C. R., Heglund, N. C. & Maloiy, G. M. O. (1982). Energetics and mechanics of terrestrial locomotion. *J. Exp. Biol.* 97.
- Vermeij, G. J. (1987). *Evolution and Escalation*. Princeton.
- Vincent, J. F. V. (1990). *Structural Biomaterials*. Princeton.
- Vogel, S. (1994). *Life in Moving Fluids*, 2nd ed. Princeton.
- Walker, J. A. & Westneat, M. W. (2000). Mechanical performance of aquatic rowing and flying. *Proc. R. Soc. B* 267.
- Watson, J. T., Ritzmann, R. E., Zill, S. N. & Pollack, A. J. (2002). Control of obstacle climbing in the cockroach. *J. Comp. Physiol. A* 188.
- Webb, P. W. (1975). Hydrodynamics and energetics of fish propulsion. *Bull. Fish. Res. Board Can.* 190.
