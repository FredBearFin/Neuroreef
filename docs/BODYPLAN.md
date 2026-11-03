# NEUROREEF: OPEN-ENDED BODY PLANS (design and prototype)

Status: design, plus a working prototype in `src/Neuroreef.Core/Morph/` (namespace `Neuroreef.Morph`).
Phase 2 (shadow expression, §10) has started: every live body is also translated to a module genome and its
capabilities computed from the developed morphology, alongside `Body.Express`, into `World.D` (write-only: the live
simulation reads none of it and is bit-identical). Tests are in `tests/Neuroreef.Tests/MorphTests.cs` and
`tests/Neuroreef.Tests/ShadowTests.cs`.

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

| Genome-level gene | Physical meaning |
|---|---|
| `MassG` | adult body mass, log 1 g – 10 kg (0.6 is the old fixed 0.25 kg); mutates on 20% of offspring by N(0, 0.04) (×1.45) |

Every segment and appendage gene carries an **innovation number** (`Innov`, NEAT): all founders share the trunk's
number 1, and each insertion or duplication gives the new module a fresh number from the run's `Innovations` counter.

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
is still poor. CPPNs (Stanley 2007) give a compact way to describe graded, repeating patterns along a body. Since
phase 1 the prototype uses both: innovation numbers align homologous modules for crossover (`Homology.Cross`) and
count unmatched ones in the compatibility distance (`Homology.Distance`), which the search uses to form species (§8).

## 3. Development: genome to morphology

`Development.Grow`:

1. Each segment gene grows `repeat` serial copies, each scaled by `taper`. Every appendage gene grows a bilateral
   (or sagittal) pair on every copy, scaled with it. Serial homologues are therefore graded, as Hox expression
   grades them.
2. The whole body is scaled to the mass its mass gene sets (`Development.MassOf`; `FixedMass` overrides it for tests
   and probes). This works the way today's sim keeps mass as its own gene and its shape genes at unit volume
   (`Body.Dimensions`). Limbs, armour and gills take mass *from* the trunk and its gut, not on top of it. Phase 0 fixed
   the mass at 0.25 kg because food capture (∝ size^(2/3) × speed) outran Kleiber's cost (∝ M^0.75) and every body
   ran to the largest its genes allowed. Phase 1 makes mass a gene again and lets the allometry in §4 decide.
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
  C_D,wet = C_f (1 + 1.5 f^−1.5 + 7 f^−3) for fineness f = length / √(W H) of the largest segment section. It is capped
  at a bluff body's 1.2 × frontal area, plus 0.3 × any steps in cross-section. Least drag for the volume is near
  f ≈ 4–5, as in fishes and cetaceans (tested). An early version used Hoerner's body-of-revolution formula on
  *frontal* area; it rewarded sheet-like bodies, because a sheet has a small equivalent diameter but a huge wetted
  area.
- **Skin friction by Reynolds number** (phase 1). C_f = 1.328 / √Re below Re = 5×10⁵ (Blasius), and
  0.074 Re^−0.2 − 1700/Re above (Prandtl–Schlichting; Schlichting 1979), with Re = U × length / ν. Small, slow bodies
  pay more friction per area than large, fast ones (tested).
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
  - A legless body crawls (phase 1) at Kram & Taylor's cost of generating force per stride, the stride being a body
    wave of 0.25 × its length (Gray 1946; Quillin 1999), plus a residual slip and mucus cost of 0.15 × the pressed
    weight (Denny 1980). At least 10% of its mass presses down, for adhesion. Lateral undulation then costs about what
    legs of the same mass do (Walton, Jayne & Bennett 1990; tested: 25 against Taylor's 17 J/(kg m) at 0.25 kg).
    Power comes from axial muscle × trunk flexibility.
  - Whatever the limbs do not hold up, the trunk crawls under at the crawling cost.
- **Obstacles** of height h every `ObstacleGap` metres (phase 1, replacing a speed factor that gave tall bodies free
  clearance):
  - the centre of mass must rise by h²/(h + clearance), where clearance is what the limbs lift the belly. A crawler
    lifts its whole weight by h. The muscle does that work at its efficiency, and it is not recovered on the way
    down;
  - a step higher than the body can reach up to (its limbs, or half its length draped over the edge if it can bend)
    forces a detour: speed × 1 / (1 + 0.5 (h / reach)²);
  - on rough ground (slopes up to tan = 0.5 × roughness), a centre of mass at height z over a base of half-width b
    topples past slopes steeper than atan(b/z) (static stability margin; McGhee & Frank 1968; Ting, Blickhan & Full
    1994). Speed × (1 − 0.5 P(topple)). Legged gaits also lose speed for lack of a tripod (Full & Koditschek 1999).
- **Surge** on the sea floor: a body holds station when μ · weight-in-water + limb hook force ≥ the surge drag on
  its side area, and when weight and hooks right it over its base against that drag acting at the side's centroid
  (toppling). A swimmer holds by thrust.

**Turning and escape** (phase 1; Webb 1983, 1984; Fish 2002; Howland 1974; Domenici & Blake 1997):

- **Control area.** A pair controls by its planform × flat/(flat + 2) × articulation × how far its muscle moment
  (200 kPa × cross-section × half its thickness) holds it against the hinge moment of its own lift (L × chord/4). The
  trunk controls by 0.5 × length × mean height × flexibility × axial muscle (saturating at a 10% share).
- **Least turning radius** R = 2 (M + 0.5 ρ V) / (ρ C_L S) with C_L = 1, and never below 0.1 × body length. Muscled
  flat pairs turn ~3× tighter than passive ones, and rigid finless bodies turn widest (tested).
- **Escape** from a predator by Howland's criterion: odds of capture (U_p / U)² × R / R_p, so a slower prey escapes
  if it turns inside its pursuer. In clutter (structures `Clutter` metres apart) each speed is slowed by
  gap / (gap + R) (Webb 1984), so manoeuvrability is worth most in structured habitats.
- **Predators** scale with their prey in a size spectrum (Cohen et al. 1993; Brose et al. 2006): bite ∝ M^(2/3),
  speed ∝ M^(1/6) (Peters 1983), turning radius 0.3 m × (M / 0.25 kg)^(1/3). In clutter the predator's radius is at
  most half the gap, since the predators that hunt a structured habitat can manoeuvre in it. So size alone neither
  armours nor rescues a body (tested).

**Feeding** (phase 1; Holling 1959; Vermeij 1987; Elner & Hughes 1978; Werner 1974; Warner & Jones 1976):

- **Fine food** as before: plankton by swept volume, through 1.7 × the head's frontal area (the old 0.5 V^(2/3) for a
  fineness-6 fish; Eggers 1977), and floor food by a swath of 0.8 V^(1/3). Detritus on the sea floor assimilates at
  0.4 (Valiela 1995), plankton and land food at 0.7, and prey flesh at 0.8.
- **Food items.** A count per m² or m³, each of fixed energy:
  - on the sea floor, shelled prey, 0.5/m² × 1.5 kJ, with shell strength log-uniform 0.1–300 N;
  - in open water, evasive prey, 0.05/m³ × 2 kJ, escaping at 0.4 m/s on a 2 cm turning radius.
- **Crushing force.** A pair crushes with min(muscle force, skeleton), times articulation and reach:
  - muscle force is 200 kPa × cross-section × a pincer lever ratio of 0.3;
  - the skeleton bears its moment capacity / (0.5 × length);
  - articulation enters because a jointless rigid rod cannot close on anything;
  - reach enters as (length / distance from the mouth)², because the item must be held at the mouth.

  A mouth with no appendages crushes 0.3 N × (M / 0.25 kg)^(2/3). Jaws and mandibles are serial homologues of arches
  and limbs (Snodgrass 1935), so a strong jaw has to come from appendage genes. Only a stiff, jointed, muscled pair
  at the mouth crushes hard (tested: 16 N against ≤ 2.4 N for soft, jointless, slack or short versions).
- **Evasive prey** are detected over 4 × the capture area and caught with Howland's odds, using the body's top speed
  and turning radius. Its strike reach (the articulated length past the mouth, held out against its weight, at the
  tip speed one contraction of 50 J/kg gives the appendage and its added water) is added to the prey's turning
  radius.
- **Holling's type II disc equation.** Intake = Σ a_i N_i E_i / (1 + Σ a_i N_i h_i). Handling time
  h = h₀ (1 + 2 F̄/F) (1 + (m_item / (0.1 M))²): slower as shells near the crushing force (Elner & Hughes 1978;
  Boulding 1984) and as the item nears the eater's size (Werner 1974). Locomotion is paid only in the search share,
  and the handler's muscle power during handling.
- **Gut throughput** ∝ M^0.75 (capacity ∝ M, retention ∝ M^0.25; Demment & Van Soest 1985), passing raw food at
  7.14 W per kg of gut at 0.25 kg, whatever its quality.
- **Patchy food** (land): 1 m² patches every 4 m. Each patch is eaten out and the body then travels to the next at
  its best travel speed (Charnov 1976). A bigger gut empties a patch sooner, so its travel share grows with size
  (tested: 0.2% at 10 g, 28% at 2 kg).

**Flight** (Pennycuick 2008; Ellington 1984; Dickinson, Lehmann & Sane 1999). Flight-muscle power (150 W/kg) times
lift-based profile efficiency, against the induced power of hovering on the actuator disk of the span:
1.2 W^1.5 / √(2 ρ A). The margin grows with span and muscle and falls with size (power ∝ M against need ∝ M^(7/6)).
A 10 g body with long, flat, muscled appendages flies (tested). Flight is evaluated but not yet an environment.
Phase 2 replaced the hovering bound with the least power of level flight and wingbeat-frequency-limited muscle
power (§10.3).

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
  - intake is the Holling intake of fine food and items × duty × hold, each food assimilated at its own efficiency,
    with the gut capping raw throughput;
  - on patchy floors, intake is averaged over a patch and the travel to the next.
- **Demand** = Kleiber 0.3 W kg^−0.75 on the active mass + locomotion / 0.25 (in the search share) + handling power +
  integument turnover and moult.
- **Net** = intake − demand − ventilation − anaerobic debt − surface costs.
- **Predation.**
  - Encounters rise with the body's own mean speed: √(u² + v²)/v (Gerritsen & Strickler 1977).
  - An attack kills by the armour's kill chance (against the size-scaled bite) unless the body escapes by Howland's
    criterion, and always kills during a moult.
- **Fitness** = net / basal − pressure × encounters × risk, at the best speed and mode. Both terms are per unit of
  physiological time (basal turnover), so mass is neutral to first order (Charnov 1993; Peterson & Wroblewski 1984:
  mortality ∝ M^−0.25 per unit of absolute time) and the second-order physics decides it.

**Environments** (`MorphEnv`). These are the only places the three worlds differ:

| | medium | fine food | items | predation (pressure, speed, bite at 0.25 kg) | floor |
|---|---|---|---|---|---|
| open water | water | plankton 900 J/m³ | evasive prey 0.05/m³ × 2 kJ, 0.4 m/s | 0.5, 1.5 m/s, 10 N | none |
| sea floor | water | detritus 300 J/m² (assim. 0.4) | shelled prey 0.5/m² × 1.5 kJ, 0.1–300 N | 3.0, 1.5 m/s, 10 N | boulders: roughness 1, obstacles 3 cm every 0.3 m, clutter 0.3 m, surge 0.3 m/s |
| land | air | 500 J/m² in 1 m² patches every 4 m | none | 0.5, 1.0 m/s, 10 N | roughness 0.5, obstacles 1 cm every 0.1 m |

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
- **Homology for crossover and distance** (implemented in the prototype in phase 1: `Innov`, `Homology`). Each module
  gene carries an innovation number (a `uint` assigned at duplication or insertion and copied with the gene), as in
  NEAT. Crossover aligns genes by innovation number. In the live sim, clade
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

### Phase 1: mass gene, food handling, turning, obstacle energetics, patchy food, speciation (2026-11-02)

**Harness.** `Evolver.Run`: 200 genomes, 1,000 generations, NEAT-style search:

- species by compatibility distance (`Homology.Distance`: unmatched modules, mean parameter difference of matched
  ones, mass-gene difference), with a threshold that adapts toward 8 species;
- offspring per species in proportion to its members' mean shifted fitness (fitness sharing; Goldberg & Richardson
  1987); species that have not improved for 60 generations are dropped unless they hold the best;
- within a species: its champion kept, then tournament of 3, crossover of homologous modules at 30% (2% across
  species), and every mutation operator;
- the overall best is always kept.

The population starts from `MorphGenome.Blob()` at 0.25 kg. A run takes about 2 s.

To reproduce (`NR_MORPH_ENV` = open, floor or land picks one environment; `NR_MORPH_NOSPECIATE=1` turns speciation off;
`NR_MORPH_FIXMASS`, `NR_MORPH_LANDFOOD` and `NR_MORPH_LANDGAP` are for sensitivity runs):

```sh
NR_MORPH_SEEDS=6 NR_MORPH_GENS=1000 dotnet test tests/Neuroreef.Tests -c Release --filter "FullyQualifiedName~MorphTests.EvolutionDemo" --logger "console;verbosity=detailed"
```

Best of each of 6 runs per environment. A pair's *use* is its dominant role in the chosen way of life (`PairUses`):
the thrust it gives (swim), the weight it bears (support), being the crusher or striker that feeding uses (handle),
its share of the control area (steer), or passive.

| env | mass | segments | pairs, and for each: length, stiffness, joints, muscle, flatness, use | integument rigidity, moult | exchange surfaces | mode | fitness |
|---|---|---|---|---|---|---|---|
| open water | 1 g (5/6), 14 g (1/6) | 1–11 | 0 in 4/6. Seed 3: one lateral pair, 21 mm, soft (mineral 0.1), 4 joints, muscle 0.06, flat 26, **handle** (a raptorial striker). Seed 5: one 26 mm soft, unmuscled pair, swim | soft, 0, no moult | none: skin breathing (0.0005 m² everted in the 14 g run) | undulating swim 0.08–0.20 m/s; 26–28% of intake is evasive prey (catch chance 0.67) | 3.07–3.37 |
| sea floor | 12–213 g (four runs 41–92 g) | 5–26 | **4/6 grow one pair on the head segment: 2–4 mm, mineral 0.5–0.9, moment 2–20 mN m, 6 joints, muscle 0.10–0.30, round (flat 1–2), sagittal in 3/4 (one blade up, one down: a jaw), handle** | soft (≤ 0.02), no moult | everted 0–0.002 m² (gills), folded-in ≤ 0.0002 | swims slowly along the bottom, 0.01–0.03 m/s. Those with the head pair crush 1.2–2.8 N and take 61–67% of intake as shelled prey | 2.08–2.40 with the head pair, 1.25–1.42 without |
| land | 1–3 g | 13–23 | 0 (3/6 carry sub-millimetre buds, passive) | **rigid, moulting (rigidity ~1.0, moult 5%) in 3/6**; soft in 3/6; integument 3–7% of mass | none: skin breathing | legless crawl ~0.004 m/s; travel between patches ~1% of time | 2.25–2.51 |

Sensitivity and comparison runs (6 seeds × 1,000 generations each):

- **Sea floor without speciation:** best fitness 1.00–1.56, and 0/6 runs found the handling pair, against 4/6 with
  speciation.
- **Land with rich, sparse patches** (5,000 J/m², 1 m² every 10 m): 63–114 g legless crawlers with folded-in lungs
  (0.0009–0.002 m²), rigid and moulting in 2/6 (rigidity 0.65–0.83). No limbs.
- **Fixed-mass optima** (open water, 1 seed × 400 generations): 3.24 at 1 g, 3.10 at 10 g, 2.99 at 100 g, 2.91 at
  1 kg. In water, size is close to neutral, as the size-spectrum design intends, with a slight advantage to small
  bodies. On land the gradient is steep: 2.44 at 10 g, 1.93 at 100 g, 0.02 at 1 kg.

**What emerged, and why.**

- **A jaw-like handling appendage on the sea floor.** This is the clearest phase-1 result. Shelled prey are richer
  than detritus, and they are worth more because flesh assimilates at 0.8 against detritus's 0.4. Only a stiff,
  jointed, muscled pair at the mouth opens them, and evolution found exactly that in 4 of 6 runs: mineral 0.5–0.9,
  6 joints, the maximum muscle share, on the head segment. Usually the two blades are in the midplane, one above and
  one below the mouth. Nothing names it a jaw or a mandible. It is the region of parameter space that the crushing
  physics rewards, and it doubles fitness.
- **Rigid, moulting armour on land at small size.** Half the land runs evolved a fully mineralised, moulting
  integument on 1–3 g crawlers. Integument thickness scales with size, and the predator's bite with M^(2/3). At small
  size a thin skin dries out (evaporation falls e-fold per 0.3 mm), so a thick, hard cuticle pays twice, against
  desiccation and against predators. This is the exoskeleton-when-small argument, now operating in evolution.
- **A raptorial striker** appeared once in open water (a muscled, jointed, flat pair at the head, used to catch
  evasive prey). In a 1 kg fixed-mass run, a 207 mm, mineralised, jointed lateral pair did the same.
- **A muscled steering surface** appeared on the sea floor in an intermediate calibration: a 244 mm flat lateral
  pair, 3 joints, muscle 0.04, use "steer", on a 7 kg body. After the clutter-predator fix (below), the bodies
  shrank and it was not seen again.

**What did not emerge, and why.**

- **Powered fins: no.** At 1 g, axial undulation and skin breathing win. The lift-based efficiency of a paired fin
  would only matter at sizes the physics does not favour in open water. Turning is worth something (Howland escape,
  evasive-prey capture), but a small undulating body already turns at about the 0.1 × length floor.
- **Stiff jointed walking legs: no**, on the sea floor or on land.
  - In water, weight is ~5% of the body's, so legs pay only through obstacle clearance and surge grip. Swimming
    along the bottom avoids both.
  - On land, size runs small, because at a sparse food yield the swath a body sweeps grows only as M^(1/3) against a
    demand of M^0.75. At 1–3 g, legs carry little and cost gut mass, and the crawling cost is comparable (Walton et al.
    1990). Legs pay only at larger sizes (walking cost ∝ M^−0.32), which this food does not support.
  - With rich patches the optimum rises to ~100 g, but a slow, gut-limited body in a rich patch hardly travels, so
    legs still do not pay.
- **Crustacean-like armoured walker on the sea floor: no.** The handling appendage arrived on a soft body that swims
  along the bottom. A body near the minimum size escapes the sea-floor predator by turning tightly in clutter
  (escape 0.95), which is cheaper than armour plus moulting. The rigid crawler of phase 0 is no longer the optimum.
- **Mass hit the gene's lower bound (1 g) in open water and on land.** No physics below 1 g is modelled to stop it,
  such as viscous swimming at Re < 100 or particle size against gape. So the bound, not physics, sets the minimum
  there. The bound is a gene range (as in the live sim), not a fix.

### Phase 0 (fixed 0.25 kg, asexual tournament), kept for comparison

**Harness.** 200 genomes, 1,000 generations, tournament of 3, 2 elites, asexual, all operators. 6 seeds; the best of
each run is shown (2026-11-02):

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

**Phase 1 loopholes found and fixed.** Each is a physics correction, listed with its test:

- **A thread taking a fish's catch.** Plankton capture was 0.5 V^(2/3) whatever the shape, so a 2 g thread of
  fineness 160 took a fish's catch on a frontal area 60× smaller. Fix: capture follows the head's frontal area
  (`PlanktonCaptureFollowsTheHeadNotTheVolume`).
- **Small crawlers too cheap.** Crawling cost sliding friction at μ = 0.7 on the whole weight, the same per kg at
  every size, so a 2 g crawler was 3× cheaper than a 2 g walker. Fix: Kram & Taylor's force-per-stride cost over a
  body wave, plus slip; lateral undulation now costs about what legs do (Walton et al. 1990;
  `CrawlingCostsAboutWhatWalkingDoes...`).
- **A muscle-less stub "walking".** A 1 mm, muscle-less stub counted as "walking", because the unsupported weight
  slid at the old μ = 0.7, which was cheaper than the new crawl. Fix: the weight the limbs do not hold up is
  crawled at the crawling cost (`...AndAVestigialLimbIsNoDiscount`).
- **Turning on a coin.** A fineness-80 "eel" had so much lateral trunk area that it turned in a few millimetres.
  Fix: the least turning radius is at least 0.1 × body length (Domenici & Blake 1997).
- **A 100 kg predator stuck in boulders.** Predators were scaled to their prey, but clutter was fixed at 0.3 m, so a
  100 kg predator could not follow a 10 kg prey between boulders, and sea-floor bodies ran to the 10 kg cap. Fix:
  the predators that hunt a cluttered habitat turn within half its gap (Webb 1984).
- **Whole-body lift from one flat segment.** Body lift and frontal area mixed MaxW and MaxH from different
  segments, so a wide, flat head on a tall, narrow trunk "lifted" along the whole length. Fix: lift per segment, and
  frontal area from the largest single section.
- **The tall-body artefact (from phase 0).** Obstacle clearance by half the body height let tall bodies get over
  obstacles for free. Fix: the centre of mass must be lifted over each obstacle (work at muscle efficiency), a step
  beyond the body's reach forces a detour, and a high centre of mass on a narrow base topples on rough ground or in
  surge (`ObstaclesCostTheWorkOfLiftingTheBodyAndHeightGivesNothingForFree`).
- **Size as armour.** With a fixed 10 N bite, mass alone would armour: integument t² ∝ M^(2/3). Fix, at design time:
  predators scale with their prey (`PredatorsScaleWithTheirPreySoSizeAloneDoesNotArmour`).

**Suspect, not yet fixed.** The sea-floor handler bodies are tall, laterally compressed sheets, ~5 mm wide and
100 mm deep. Lateral area makes them turn at the 0.1 × length floor and escape the predator in clutter (escape
0.95). Compressed reef fish are indeed manoeuvrable (Webb 1984), but a 20:1 section is extreme. Roll stability and
the structural cost of a deep, thin, soft body are not modelled (Fish 2002; Weihs 2002), and they are the obvious
next correction.

**Limits of the prototype, after phase 1:**

1. **No lower size physics.** Open water and land run to the 1 g bound of the mass gene. Viscous swimming at low Re
   (below ~100), particle size against gape, and diffusion limits inside the body (Krogh) are not modelled.
2. **No roll or yaw stability.** Fish 2002's stability-against-manoeuvrability trade-off is half modelled. Mobile
   surfaces add control; passive keels add nothing.
3. **One predator** per environment, size-scaled, with no ambush model and no refuges to retreat into (clutter only
   slows pursuit).
4. **One environment per run, no ontogeny.** Moulting is a constant share of life. The soft-moult hazard and
   moulting's mass cost do not grow with size, so the exoskeleton's large-size penalty on land (Currey 1967) is only
   partial.
5. **Hand-set constants.** Food yields, item densities and strengths, and obstacle heights are order-of-magnitude
   values. Land food yield in particular decides the size, and so whether legs can pay.
6. **Sagittal orientation is a threshold** on `roll`, not a continuous rotation.
7. **Fine food on the floor is still gathered by a swath of the body's size, not by appendages.** Detritus picking,
   filter combs and antennae cannot be favoured: the 20 cm-arm loophole of phase 0 would return.

The honest summary: phase 1's physics changed what evolves. A **stiff, jointed, muscled appendage at the mouth that
crushes shelled prey** evolved in 4 of 6 sea-floor runs, and only with speciation. **Rigid, moulting armour**
evolved on small land crawlers in 3 of 6 runs. In open water, evasive prey drove a **raptorial striker** once.
**Powered fins, stiff walking legs and an armoured crustacean did not emerge.** The mass gene, which was meant to
let the size arguments for them operate, sent open water and land to the smallest sizes allowed, where axial
undulation, skin breathing and crawling win.

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
| P0 (done) | prototype, physics, operators, tests, evolution harness | ~3 agent-hours |
| P1a (done) | mass gene and allometry, food handling (Holling, crushing, strikes), turning and Howland escape, obstacle energetics, patchy food, NEAT speciation and crossover; 13 new tests; 7 loopholes fixed | ~2 agent-hours |
| P1b | finish the prototype: lower-size physics, roll stability, refuges and ambush, a moult hazard that grows with size; iterate until each environment's outcome stops moving between calibrations | 3–6 agent-hours |
| P2a (done, §10) | shadow expression: translator, shadow capabilities in `World.D`, founders diagnostic, first calibration round (7 of 13 grounded capabilities within ~25%) | ~3 agent-hours |
| P2b (done, §11) | gas volume and buoyancy, a volume-bounded lung, flight muscle up to 0.6 of a segment, tendon elastic storage, burst-swimming limit, thin-plate bending and roll in surge, telemetry of both expressions, jaw ablations; water cost, land speed and flight margin still outside 25% (§11.6) | ~3 agent-hours |
| P2c (done, §12) | folding pairs in water, fat, a tubular skeleton, capture through a porous face, the flight-power exponent (live EXP_FLY_AVAIL 2/3 → 5/6, default world re-pinned); phase 4 can start after the head-exit fix (§12.7) | ~2.5 agent-hours |
| P3 | storage: a fixed-capacity, variable-count body block in the slot layout (now with innovation numbers and the mass gene); checkpoint, `GeneBank` and AGENTS-format version bumps; a v13 → v14 translator; ICD update | 3–5 agent-hours |
| P4 | expression from morphology: replace every reader of `PLegs`, `PFins`, `PWings`, `PLegPairs`, `PFinPairs`, `PLungs` and armour (Resolve, Respiration, Heat, Decide, Reflex, Clades, Species, Telemetry) with morphology-derived speeds per medium, O2 supply, armour, moult state, crushing and strike force, and turning radius; mutate and recombine with the new operators in LIFECYCLE | 10–20 agent-hours |
| P5 | recalibration: the ~40 test files that assert body-dependent numbers (Locomotion, Wading, Diving, Respiration, Feeding, FeedingBudget, Founding, Ocean, Ecology, Metabolism, Appearance, StepLength, ...), the founding draw (`Clades.AncestorBody`) and the constants doc; long runs in each world type | 10–25 agent-hours, plus the wall-clock of long runs |
| P6 | renderer: `body.js` builds from segments and pairs (§6); appearance clustering on the developed body | 4–8 agent-hours |
| P7 (optional) | per-module sensors in fixed brain slots (§5) | 5+ agent-hours |

Total: roughly **35–70 agent-hours** for P1b–P6. This is based on the pace so far: P0 and P1a together took about
5 agent-hours, including several calibration rounds. In calendar terms that is 1–3 weeks once owner review and long
simulation runs are included. The largest risks are P4 and P5: the live ecology is tuned around today's speeds and
costs, and every one of them will move. Each phase-1 calibration round also moved the evolved outcome, so P5
should expect several rounds, not one.

## 10. Phase 2a: shadow expression (2026-11-03)

Every live body is now expressed twice. `Body.Express` reads the labelled genes as before; at its end,
`Morph.Shadow.Express` translates the same genes to a module genome (`Morph/Translate.cs`), grows it at the live body's
own mass (`Development.Grow(g, mass)`, a new overload with no static state), and computes its capabilities from the
morphology with the prototype's physics (`Morph/Shadow.cs`). The results go to per-slot arrays in `World.D` (`Derived`:
`MVLand`, `MVWater`, `MVAir`, `MCruise`, `MCotLand`, `MCotWater`, `MCotAir`, `MMargin`, `MFlies`, `MStand`, `MO2Air`,
`MO2Water`, `MOdds`, `MBasal`, `MStream`, `MTurnR`).

- **Write-only.** Nothing in the simulation reads them. They use no random numbers and write only their own slot.
- **Not saved.** `Derived` is not walked by `Checkpoint`, so the checkpoint format is unchanged. `Derived.Refresh`
  (run after a load) recomputes them from the genomes.
- **Pinned hashes unchanged.** `StepLengthTests.DefaultStepIsBitIdenticalToBefore` and
  `ClimateTests.NoClimateIsBitIdenticalToBefore` pass unchanged.
- **Switch.** `Shadow.Enabled` (on by default) turns the computation off.

### 10.1 The translator

Lengths are in cube sides of the live body (`Body.Dimensions` makes length × width × height = 1). Development rescales
the whole body to its mass, so only proportions matter.

| live gene(s) | module genome |
|---|---|
| `B_LENGTH`, `B_WIDTH`, `B_HEIGHT` | a head, `max(B_LEGN, B_FINN)` trunk segments and a tail. Every segment has the live width and height (no steps), so the fineness is the live body's |
| `B_HEAD`, `B_TAIL` | the head's share of the length, 0.10–0.20, and the tail's, 0.08–0.20 |
| `B_LEGS` × `B_LEGN` | one support pair per trunk segment: length 1.86 × legs cube sides (the live standing height `STAND_H` M^(1/3) (0.3 + 0.7 legs); Garcia & da Silva 2006). Droop 70°, 3 joints, round (chord = thickness = 0.08 × length), bone (mineral 1, skeleton 35% of the section, endoskeletal), muscle gene = legs |
| `B_FINS` × `B_FINN`, `B_FIN_SHAPE` | one flat lateral pair per trunk segment: length 0.2 × body length × fins, aspect ratio 1–4 from the shape gene, flatness 8, 2 joints, soft (mineral 0.2), muscle gene = fins. A sagittal tail blade of 0.8 × body height × fins. Axial muscle gene 0.25 + 0.75 fins (AxShare 0.1 for a walker's back, 0.4 for a fish's myotomes) |
| `B_WINGS`, `B_WING_SHAPE` | one flat lateral pair on the thorax: length 4.5 × (0.4 + 0.6 wings) cube sides (a 1 kg bird's ~1 m span; Pennycuick 2008), aspect ratio 5–15, flatness 12, 3 joints, mineral 0.6, hollow (exoskeletal share 0.8), muscle gene = wings. The thorax takes (1 − wings)/n + 0.85 wings of the trunk, so it houses the flight muscle |
| `B_ARMOUR` (0–3) | integument thickness 2% × (1 + armour) of the half-thickness, and mineral armour / 3, on every segment |
| `B_LUNGS` | exchange surface sized to the live organs' scopes over the body's own basal metabolism: folded-in area LUNG_SCOPE × lungs × basal / S_AIR_INT, everted area GILL_SCOPE × gills × basal / S_WATER_EXT. It is spread over every segment's surface (FOLD × pore share, at most 0.5) |
| an organ gene below 0.02 | no module at all |
| `B_MASS`, `B_FLOOR` | the live mass. The genome's mass gene is set where the prototype's 1 g–10 kg range holds it |
| `B_ENDO` | kept as a gene. It sets the metabolic level (`Heat.Level`) that scales Kleiber's basal |
| speed, sight, strike, eyes, life history, immunity, water economy, voice, `B_INSUL`, `B_APPEND`, brain | kept as genes. Their basal factor and the brain's cost pass through unchanged (`Shadow.GeneBasalFactor`, `BrainW`) |

Innovation numbers are fixed per role: trunk 1, head 2, further trunk segments 3–5, tail 7, legs 10+, fins 20+, tail
blade 30, wings 40. So translated genomes line up homologously for crossover and distance.

### 10.2 The capabilities, and how the live values are put in the same units

| capability | shadow (from the morphology) | live, converted |
|---|---|---|
| land, water, air speed (m/s) | top speed on flat ground, in water and in level flight, with muscle at P_BURST = 250 W/kg (Weis-Fogh & Alexander 1977). Air: v_mp × `Body.TopFlightSpeed(margin)` | a step's travel is a day range (DMD_M), not a speed. The live sim reads it as a speed through Garland's sprint × its speed over the plain body's (`Body.ReachM`); so does the comparison |
| cruise in water (m/s) | the speed of least gross cost a metre (basal included; Videler 1993) | `Respiration.CruiseMps` (1 length a second × the propulsor) |
| cost per metre (J/m, net) | land: least of `WalkMetabolic`/u. Water: at the cruise. Air: least power / ETA_M / v_mp | land: COT_J M^0.7 × (stance + swing at half speed). Water: `Respiration.SwimCost`. Air: `Resolve.MoveCost` per distance |
| flight margin; flies | `Physics.FlightMargin`. Flies if margin ≥ 1 and flight muscle ≥ FLY_WING_MIN × FLY_MUSCLE_MAX of the mass (Marden 1987) | `Body.FlightMargin`; `PLoco == Fly` |
| standing height (m) | limb clearance + trunk height | `Respiration.StandM` (adult) |
| O2 supply in air, in water (W) | `Physics.O2Supply` | organ base × scopes + skin (`Respiration.UptakeAt`, at sea level and saturated water) |
| protection | crushing odds 1/PKill − 1 against the size-scaled bite. Compared as the gain over the same body unarmoured | (DEF_BASE + armour)/DEF_BASE, the odds factor in `Resolve.Strike` |
| basal cost (W) | Kleiber on the non-mineral mass × level × gene factor, + integument turnover and moult, exchange-epithelium upkeep and the brain, then `Body.GutScale` | PBasal |
| drag shape | (drag of a cube of its trunk volume / its own)^(1/3), by the wetted-area drag | `Body.Streamlining` (Hoerner's volume drag) |
| turning | least radius in water at 1 m/s. Compared as the plain body's radius over its own | PTurn / (C_TURN M^EXP_TURN) |

### 10.3 Calibrations made, and which side is more physical

Each change is a physical correction. Each was tested on the reference bodies (`ShadowTests`) before it was compared
with the founders. All 28 fast `MorphTests` and the `EvolutionDemo` still pass.

1. **Limb swing (Morph was wrong).** It was 4 m_leg u³ / stride: the whole limb at the foot's speed, at the walking
   stride. That held a 1 kg walker's sprint to ~2 m/s against Garland's 8.4. Now:
   - a limb's mass is proximal, so SWING_K = 0.16 of it moves at the foot's speed (radius of gyration ~0.4 L;
     Hildebrand 1985);
   - the stride lengthens with speed by Alexander's dynamic similarity, 2.3 h Fr^0.3 (Alexander & Jayes 1983);
   - `Physics.StrideAt` and `SwingPower` do this.
2. **Undulation's lateral kinetic work (Morph was missing it).** A limbless crawler's speed had no limit but slip and
   support, and a 1 kg fish "crawled" on land at 7 m/s. Now CRAWL_KIN × M u³ / wave length is paid (slip angles
   ~45°; Gray 1946; Jayne 1986; `CrawlKinetic`).
3. **Walking cost KT 0.10 → 0.12 (calibrated).** KT had been set on a hand-made 0.2 kg walker with 5 cm strides. The
   translated legs stand a body at the live STAND_H, which is empirical. To match Taylor, Heglund & Maloiy's
   10.7 M^0.7 J/m:
   - the plain walker needs 0.14 (at 0.12 it is at 0.76–0.88 of Taylor over 0.1–10 kg);
   - the full-legged founders need 0.10.
   0.12 puts their median at 1.05. Taylor's line is the more physical side.
4. **Flight margin (Morph was wrong).** It was flight-muscle power at a fixed 150 W/kg against the induced power of
   *hovering*, an upper bound about twice the least power of level flight. That gave a 1 kg bird with a 0.9 m span a
   margin of 0.8. Now:
   - **required power** is Pennycuick's least power of level flight: induced K_IND W²/(2ρ S_d v) on the span's disk,
     plus the body's parasite drag, at its minimum;
   - **stall limit:** no slower than stalling the lifting area at CL 1.6. A leg or rod has span but no area, and once
     "flew";
   - **available power** is W_FLAP = 17 J/kg a wingbeat (Askew & Marsh 2002) at the wingbeat frequency of its wing
     (Pennycuick 1996: f = M^(3/8) g^(1/2) b^(−23/24) S^(−1/3) ρ^(−3/8)). So the margin falls as ~M^(−1/3) for
     similar shapes;
   - the live sim's M^(−1/2) (available power ∝ M^(2/3)) sits at the other end of the measured 0.67–0.8 exponent of
     available power (Ellington 1991; Marden 1994). Neither exponent is clearly more physical; the shadow's is
     mechanistic.
5. **Gills in air (live was right).** An everted surface in air supplied 15 W/m², 60% of its value in water. Soft
   lamellae collapse and stick in air, and only a few % stay open (Graham 1997), as the live GILL_AIR (0.02) says.
   S_AIR_EXT is now 0.5 W/m². Skin, which does not collapse, keeps the old value for skin breathing in air
   (S_AIR_SKIN = 15). Stiffened everted gills (land crabs) are not modelled.
6. **Translator choices (not fitted).** Limb slenderness 0.08 (it was 0.12, which made limbs 30% of the body), and
   P_BURST 250 W/kg. Both are literature values, chosen before the comparison.

**Effect on the prototype's evolution (6 seeds × 1,000 generations, re-run with these changes).**

- **Open water:** as in phase 1 (1 g undulators; one raptorial striker).
- **Land:** rigid, moulting crawlers in 4/6 (3/6 in phase 1).
- **Sea floor:** the crushing head pair evolved in **1/6** runs (seed 5: mineral 0.9, 5 joints, muscle 0.30, 3.5 N,
  69% of intake as shelled prey), against 4/6 in phase 1. The other runs are soft swimmers along the bottom. Which
  change moved it (the dearer walking and crawling, most likely) is not yet known. P2b should run the ablations.

### 10.4 Shadow against live: the founders of seeds 1–5

`ShadowTests.ShadowAgainstLiveOverTheFoundersOfSeeds1To5`:

- **Sample:** every founder of `new Simulation(1024, seed, 2)` for seeds 1–5, with no step run: 17,521 animals,
  ~15 s a seed;
- **Measure:** the ratio shadow / live per capability: median, 10th and 90th percentile;
- **Assertion:** the calibrated rows are asserted within ~25%.

| capability | live grounded? | n | median | p10 | p90 | verdict |
|---|---|---|---|---|---|---|
| drag shape | yes (Hoerner) | 17,521 | **1.05** | 0.97 | 1.75 | calibrated; the tail is flat, wide bodies, where wetted-area drag punishes more than Hoerner's body of revolution |
| standing height | yes (Garcia & da Silva) | 17,521 | **1.04** | 0.71 | 1.40 | calibrated |
| land cost/m, legs ≥ 0.7 | yes (Taylor et al.) | 5,975 | **1.05** | 0.85 | 4.46 | calibrated. The tail is few-legged armoured bodies |
| land cost/m, all | yes | 17,521 | 1.58 | 0.95 | 5.30 | **Morph more physical for crawlers.** A short legless body's body wave (0.25 L) is a short stride, so it pays several times a walker's cost a metre. The live sim charges legless bodies the stance cost of walking. Walton et al. (1990) found snakes, which are long, near walking's cost, and the Morph agrees for long bodies |
| land speed (rel. to the plain body) | yes | 17,521 | **1.14** | 0.40 | 1.89 | calibrated (legs, pairs, armour, wings) |
| land speed (m/s) | yes (Garland) | 17,521 | 0.67 | 0.23 | 1.22 | **live more physical.** The shadow matches Garland at 0.1 kg (4.0 m/s) but reaches only 0.66 of it at 1 kg and 0.55 at 10 kg: the Morph has no tendon elastic storage (Alexander 1988; Bennett & Taylor 1995), which grows with size. Next phase |
| air cost/m | yes (Tucker; Pennycuick) | 787 | **1.17** | 0.82 | 1.55 | calibrated |
| flight margin | yes (FLY_M_MAX 16 kg) | 17,066 | 0.32 | 0.08 | 1.23 | **live more physical.** A pair gene drives with at most 0.3 of its segment's tissue (`Development`), so flight muscle stops near 0.2–0.27 of the body against a bird's 0.35, and most founders' bodies are bluff (parasite drag). A 1 kg full-winged body has margin 1.6 (live 4.0); a 10 kg one cannot fly (live 1.26) |
| flies (agreement) | yes | 17,521 | 76% agree | | | follows from the margin |
| water cruise, fins ≥ 0.7 | yes (Videler) | 2,841 | **1.13** | 0.78 | 2.17 | calibrated |
| water cruise, all | no | 17,521 | 2.67 | 1.06 | 6.60 | the live 0.1 lengths a second of a finless body is a hand-set floor. The shadow's optimum is physics |
| water cost/m, fins ≥ 0.7 | yes (COT_SWIM_J) | 2,841 | 7.15 | 4.16 | 30.9 | **live more physical** (see the buoyancy note below) |
| water cost/m, all | yes | 17,521 | 10.0 | 3.93 | 31.2 | the same |
| O2 supply in air | yes (Weibel's scope 10) | 17,521 | 0.52 | 0.24 | 0.75 | **live more physical.** The folded-in area is bounded by 0.5 of the body surface × FOLD 10, so an endotherm's lungs at full scope do not fit. An ectotherm's do (within 4%). A lung's area is bounded by volume, not surface: next phase |
| O2 supply in water | yes (scope 2–5) | 17,447 | 0.78 | 0.31 | 2.74 | the same bound for endothermic gills; the high tail is lungs counted as water pockets (12 W/m²), which a vertebrate lung is not |
| basal cost | yes (Kleiber × level) | 17,521 | **0.75** | 0.65 | 0.85 | within the asserted 0.7–1.05. The 25% gap is the live organs' upkeep terms (COST_LIMB for fins, COST_WING, COST_ARMOUR_ABS, `OrganUpkeep`), which have no tissue-physics counterpart in the Morph. For birds the live sim is grounded (BMR +30–50%; McKechnie & Wolf 2004). For fins and armour its terms are qualitative |
| water speed (m/s; rel.) | no | 17,521 | 0.34; 0.56 | | | the live water speed is Garland's running sprint × a hand-set factor. A 1 kg shadow fish bursts at 5.6 m/s (22 lengths/s, high: no lactate limit) |
| air speed | no | 787 | 0.92 | 0.49 | 1.12 | the live air speed is a day's travel × FLY_VMP, not a flight speed |
| protection gain | no (DEF_BASE is HT) | 17,521 | 1.65 | 0.87 | 8.22 | not calibrated. The plate physics (σ t²) gives armour 3 a ~50× crushing gain on the trunk, diluted by unarmoured limbs |
| turning (rel.) | no (C_TURN, FIN_TURN are HT) | 17,521 | 2.93 | 1.54 | 4.96 | not calibrated: the live turn rate is a hand-set allometry |

**Buoyancy, the largest disagreement.** Translated bodies are ~1,160 kg/m³ (tissue at 1,060 plus mineral bone) and
carry no gas. In water they pay to hold their depth. The plain 1 kg walker's least cost a metre is 9.3 J/m, and 0.38 J/m if it
were neutrally buoyant (`Probe`). Real animals are close to neutral, with lungs (~57 mL/kg; Schmidt-Nielsen 1984), swim
bladders (~5% of volume; Alexander 1966) or fat. The live sim assumes it, and charges lungs' buoyancy separately
(BUOY_COST). So **the live sim is more physical here**, and the Morph needs a gas volume: lung air in folded-in
air-breathing surfaces, and a gas bladder from a gut pocket. A translated fish with no dense limbs matches COT_SWIM_J
within 20% at 0.1–10 kg even now.

### 10.5 Next: the rest of phase 2 (P2b), in agent-hours

| work | effort |
|---|---|
| Gas volume and buoyancy in the Morph (lung air, a swim bladder as a folded-in gas pocket), and its cost of depth (Boyle) | 1–1.5 |
| Folded-in exchange area bounded by internal volume, not body surface (Weibel's alveolar area) | 0.5–1 |
| A pair's muscle share up to a flier's (flight muscle 0.35 of the body), re-checking the MorphTests that set muscle genes | 0.5–1 |
| Tendon elastic storage in running (and the lactate limit of burst swimming) | 1 |
| Telemetry of both expressions (the shadow's speeds, costs and margins beside the live ones), and the `--bodylab` viewer's hook | 0.5–1 |
| Ablations of the sea-floor jaw against the §10.3 changes; re-run the evolution demo; re-calibrate the founders table | 1–1.5 |

Total ~4.5–7 agent-hours before phase 3 (storage) can start. Phase 4 (expression from morphology) should not start
until water cost, flight margin and O2 supply are also within ~25%, because the live ecology rests on them.

## 11. Phase 2b: gas, volume-bounded lungs, flight muscle, tendons, thin plates and roll (2026-11-03)

The live simulation is unchanged and the shadow is still write-only. `StepLengthTests.DefaultStepIsBitIdenticalToBefore`
and `ClimateTests.NoClimateIsBitIdenticalToBefore` pass unchanged.

### 11.1 What changed in the module physics

| change | physics | where |
|---|---|---|
| **Internal gas space** | A new segment gene `gas` (0..1) makes up to GAS_MAX = 0.15 of the segment's core a gas space at body pressure. It holds no tissue, so at a fixed mass it enlarges the body and lightens it in water. The weight a swimmer must hold up is G \|M − ρV\|, so an over-light body pays too. In air it lifts nothing | `Development` (`Segment.Gas`, `GasV`), `Physics.Swim` |
| **Lung = lined gas space, bounded by volume** | Where the segment's invagination share lines the gas space with exchange epithelium, the lining is an air-breathing surface of GasLining(M) × V_gas. GasLining = SV_GAS M^−0.11, SV_GAS = 5,900 per metre (in the prototype's area unit, S_AIR_INT = 100 W/m²): 57 mL of air per kg (Stahl 1967; Schmidt-Nielsen 1984) supports a 1 kg mammal's aerobic scope of ~10 × 3.4 W (Weibel & Hoppeler 2005). The M^−0.11 is alveolar surface ~M^0.95 (Gehr et al. 1981) against lung volume ~M^1.06. Under water the lining supplies nothing (it holds a breath, not a supply). An unlined gas space is a swim bladder | `Development.GasLining`, `Physics.O2Supply`, `SurfaceUpkeep` |
| **Muscle per pair 0.3 → 0.6 of its segment's tissue** | So one pair can carry a flier's flight muscle (15–35% of the body; Greenewalt 1962; Marden 1987). The segment's total muscle stays capped at 0.7 | `Mat.PAIR_MUSCLE` |
| **Profile drag of wings** | C_D,pro = 0.02 on the lifting area, added to parasite power (Pennycuick 2008; Tucker 1987). Without it a fish's fins "flew" at 45 m/s, where their small area no longer stalled. A drooped pair's flight efficiency and its counted flight muscle are × cos(droop) (a leg strokes across its lift) | `Physics.FlightMargin`, `Shadow.Evaluate` |
| **Tendon elastic return (shadow sprint only)** | The share of limb work tendons store and return is R_TENDON h / (h + H_TENDON), R_TENDON 0.6, H_TENDON 0.25 m: ~0.15 at 0.1 kg, ~0.24 at 1 kg, ~0.36 at 10 kg (kangaroo rats store little, Biewener & Blickhan 1988; ~0.45–0.5 in horses and kangaroos, Alexander 1988; Bennett & Taylor 1995). It is applied to the sprint (muscle power) only. Taylor's cost a metre, to which KT is calibrated, already includes elastic savings | `Shadow.LandMech` |
| **Burst swimming limit (shadow)** | At most one body wave of ~0.7 L a tail beat, a beat lasting at least two twitch times, 19 ms at 0.1 m and longer as L^0.45 (Wardle 1975; Videler 1993): ~1.8 m/s at 0.1 m | `Shadow.WardleSpeed` |
| **Thin-plate bending** | A segment's broad face bears pressure p = 2 (σ_soft T²/6 + σ(mineral) t T) / (D/2)² / SAFETY, a plate of depth D and thickness T cantilevered from the axis, its integument as sandwich faces (Timoshenko & Woinowsky-Krieger 1959; Gibson & Ashby 1997). The Body Lab's 77 × 2.6 mm soft sheet bears 242 Pa against 1,153 Pa at a 1.5 m/s escape turn, so it bends instead of turning. The trunk's control area (turning) is × the face-area-weighted min(1, p_cap / q C_L), and a flattened body's lift likewise | `Physics.FacePressureCap`, `PlateFactor`, `ControlAreas`, `Swim` |
| **Roll in surge (sea floor)** | A floor forager lives in the surge's boundary layer u = U min(1, z/δ), δ = 1.5 cm (Jonsson & Carlsen 1976; Fredsøe & Deigaard 1992: 0.072 A (A/k)^−0.25 for a 0.38 m orbit over 3 cm roughness). The drag ∝ u² rolls the body about mid-height with qSH³/(12δ²) inside the layer and qS(δ/3 − δ²/4H) above it. It is righted by its weight in water on its half-width, by its metacentric height (a submerged body has no waterplane, so its stability is the centre of buoyancy over the centre of mass: a dorsal gas space, ρ g V_gas H/4; Alexander 1966; Webb 2002), and by its mobile pairs' lift on their half-span (Weihs 2002; Fish 2002). The share it holds (`RollHold`) multiplies a swimmer's holding of station, as the toppling test does a walker's. The 77 × 2.6 mm sheet holds 0.017, a round body of the same 50 g 0.86 | `Physics.RollHold`, `Evaluate` |
| **Ablations** | `NR_MORPH_ABLATE` = kt, swing, crawl, muscle, plate, gas (comma-separated) reverts one change for a sensitivity run. Empty by default | `Morph.Ablate` |

No shape penalty is used. The plate and roll terms are the mechanics of a thin, tall body, and they leave a round or
moderately compressed body untouched (`AThinDeepSheetBendsAndRollsOverWhereARoundBodyOfTheSameMassHoldsUpright`).

### 11.2 What changed in the translator

- **Gas** (`Translate.GasShare`): lung air, 57 mL/kg × the lung gene; and a swim bladder that fills to neutral for every
  body the live simulation lets live in deep water (`Body.GroundRealm`: 0.1 + 0.9 propulsor ≥ DEEP_SWIM). The larger of
  the two is used. Both are exact at a fixed mass: neutrality needs f = (M − ρ_w V0) / (core0 ρ_t), and a gas volume T
  needs f = T / (core0 (1 + T ρ_t / M)). The WIP's approximation overshot neutrality by ~13%. Translated fish are now
  1,025 kg/m³. Walkers stay at ~1,094: lung air, mineral bone and no fat.
- **Lungs and gills together.** One invagination share i serves both, as the lining's respiratory share and as the
  folded-in share of the surface's exchange epithelium P. So P(1 − i) = aGill and i(P + G) = aLung, where G is the
  lining of a wholly respiratory gas space. That gives G i² − (aGill + G + aLung) i + aLung = 0, solved exactly. The WIP
  sized P from the gills alone, and its folded-in part then added aGill × i/(1 − i) of internal area, counted at
  100 W/m² in air.
- **Wings.** A winged body has a thorax of its own, THORAX_WING × wings of the trunk, its wing pair driving with 0.6 of
  the thorax's tissue. Legs and fins grow on the segments behind it, which share the rest. In the WIP a thorax of an
  even trunk share was inserted whatever the wing gene, and almost every founder carries some wing gene. So a vestigial
  wing shrank the leg segments, and their muscle, by a third. A full-winged body's flight muscle is 0.27 of its mass,
  against the live FLY_MUSCLE_MAX 0.35.
- **Wing mass.** WING_FLAT 12 → 40: a membrane or feathered vane is ~1/40 of its chord thick, so a full wing pair is
  ~10% of the body (birds 8–13%; Pennycuick 2008). At 12 the pair was a third of it.
- **Leg and fin muscle gene** 0.5 × the organ gene, so legs and fins keep the 0.3 of their segment that phase 2a used.

### 11.3 The sea-floor jaw: what moved it (ablations)

Phase 2a reported the crushing head pair in 1/6 sea-floor runs, against 4/6 in phase 1, and suspected the dearer
walking and crawling. The ablations ran in a separate checkout of the 2a commit (6493cf7), with only the switches
added, so the gene layout and random draws are 2a's:

| physics (2a, then reverting) | 6 seeds | 20 seeds | fitness with the jaw | without |
|---|---|---|---|---|
| phase 2a as committed | 1/6 | 7/20 | 1.80–2.39 | 1.14–1.55 |
| KT 0.12 → 0.10 | 2/6 | 7/20 | 1.86–2.39 | 1.08–1.56 |
| no lateral kinetic work of crawling | 2/6 | 5/20 | 2.24–2.42 | 1.30–1.55 |
| limb swing of phase 1 | 1/6 (identical to 2a: no leg is used) | — | | |
| KT and crawl both reverted (phase 1's physics) | 4/6 | 8/20 | 2.00–2.40 | 1.07–1.55 |

**Cause: sampling, not physics.** Over 20 seeds every variant finds the jaw in 25–40% of runs, within binomial noise
(±2 runs at n = 20). Wherever it is found it is the fittest plan, at ~2.3 against ~1.5. The 4/6 of phase 1 and the 1/6
of phase 2a are two draws of six from a ~35% rate (P(≥ 4/6) ≈ 0.18 and P(≤ 1/6) ≈ 0.32 at that rate). Any change
in fitness sends every seed down a different path, so six seeds cannot show a change smaller than ~2×. No
re-calibration was made for it. KT stays 0.12, and the crawl term stays, because it is physics (a 1 kg fish
otherwise "crawled" at 7 m/s). Outcome counts are reported over 20 seeds from now on.

### 11.4 Shadow against live: the founders of seeds 1–5

`ShadowTests.ShadowAgainstLiveOverTheFoundersOfSeeds1To5`, 21,917 animals (17,521 in §10.4: the founding draw has
changed since). Medians of shadow / live, 10th and 90th percentiles. Phase 2a's median in brackets.

| capability | grounded | n | median (2a) | p10 | p90 | verdict |
|---|---|---|---|---|---|---|
| drag shape | yes | 21,917 | **1.05** (1.05) | 0.98 | 1.78 | within 25% |
| standing height | yes | 21,917 | **1.09** (1.04) | 0.78 | 1.51 | within 25% |
| land cost/m, legs ≥ 0.7 | yes | 8,067 | **0.98** (1.05) | 0.81 | 3.72 | within 25% |
| land speed (rel. to the plain body) | yes | 21,917 | **0.86** (1.14) | 0.33 | 1.46 | within 25% |
| air cost/m | yes | 1,192 | **1.29** (1.17) | 1.03 | 1.64 | at the edge. Profile drag (new) raised it; the founders' fliers are bluff (fineness < 1.5), and the live cost ignores shape. Morph more physical: profile power is ~ the absolute minimum power at AR ~8 (Pennycuick 2008) |
| water cruise, fins ≥ 0.7 | yes | 3,246 | **0.96** (1.13) | 0.65 | 2.34 | within 25% |
| basal cost | yes | 21,917 | **0.74** (0.75) | 0.66 | 0.85 | the live organ-upkeep terms (§10.4); asserted 0.7–1.05 |
| **O2 scope in air** (new) | yes | 21,917 | **0.93** | 0.74 | 0.99 | within 25%. The volume-bounded lung builds the live scope |
| **O2 scope in water** (new) | yes | 21,815 | **0.91** | 0.45 | 1.08 | within 25% |
| O2 supply in air (absolute) | yes | 21,917 | 0.68 (0.52) | 0.54 | 0.78 | = scope × the basal gap: each expression builds its organs to its own basal |
| O2 supply in water (absolute) | yes | 21,815 | 0.68 (0.78) | 0.33 | 0.85 | the same; the 2a tail of lungs counted as water pockets is gone |
| land speed (m/s) | yes | 21,917 | 0.63 (0.67) | 0.23 | 1.02 | **not within 25%.** 75% of founders are > 3 kg, where the shadow reaches ~0.65 of Garland: its limbs are too heavy to swing. A translated leg is ~23% of the body, ~12% of it bone, against an appendicular skeleton of ~3% (Prange et al. 1979: skeleton 0.061 M^1.09). Slimmer legs (slenderness 0.06, bone 0.20, both in the cited ranges) fit Garland at 1 kg (8.3 vs 8.4 m/s) but a 10 kg walker can then not hold its weight: a bone is a solid rod here, not a tube in the limb, so it bears ~half the moment per mass of a real long bone (Currey 2002). Reverted. Needs an internal tubular skeleton |
| flight margin | yes | 21,461 | 0.57 (0.32) | 0.15 | 1.22 | **not within 25%, and should not be forced.** The shadow's margin falls as M^−1/3 (Pennycuick's wingbeat scaling), the live one as M^−1/2; they agree at 10 kg (0.92 vs 1.26) and part at small size (0.1 kg: 4.3 vs 12.6). A 0.1 kg bird that lifts ~0.8× its weight (Chai & Millard 1997) has a level-flight margin of ~4–5, so the shadow is closer there. Full wings give 0.27 of the body in flight muscle (live 0.35) |
| flies (agreement) | yes | 21,917 | 77% (76%) | | | follows from the margin |
| water cost/m, fins ≥ 0.7 | yes | 3,246 | 3.83 (7.15) | 2.28 | 7.67 | **not within 25%.** Halved by the gas space. The rest is shape and appendages, which the live cost ignores |
| water cost/m, fins ≥ 0.7 at a fish's shape (new) | yes | 3,246 | 2.52 | 1.88 | 3.86 | the founders' own genes at fineness 4. Stripping their legs, wings and armour gives 1.36 (seed 1); a translated ectothermic fish is 0.75–0.81 of COT_SWIM_J at 0.1–10 kg. What is left is metabolic level: a higher basal moves the least-gross-cost cruise to a faster, dearer speed a metre (Videler 1993). The appendage drag is real, but the shadow cannot fold a limb or wing to the body, so it overstates it |
| water cost/m, all | yes (fish) | 21,917 | 4.21 (10.0) | 2.29 | 9.04 | the same, plus walkers at ~1,094 kg/m³ (lung air, mineral bone, no fat) holding their depth. Live charges every body a neutral swimmer's cost |
| land cost/m, all | yes | 21,917 | 1.38 (1.58) | 0.88 | 4.46 | Morph more physical for short legless crawlers (§10.4) |
| water speed, air speed, cruise (all), protection, turning | no | | 0.37, 0.63, 2.29, 3.86, 3.64 | | | live values are hand-set (§10.4) |

Every calibrated row that was within ~25% in phase 2a still is. O2 supply is now within 25% as a scope. Land
speed, flight margin and water cost are not, for the reasons given. None of the three is fixed by a fit.

**Telemetry.** `ShadowDiag.SpeciesReport` prints, per living clade, the mean live / shadow land, water and air speed,
the cost a metre in each medium, basal, O2 in air and the share that flies. `Headless.Summary` appends it, so every
headless run and export carries both expressions. It reads only, in one pass over the slots.

### 11.5 What the prototype evolved with the phase-2b physics

`MorphTests.EvolutionDemo`, 6 seeds × 1,000 generations per environment (and 20 seeds on the sea floor). The table
now also shows each body's most compressed section (H/W), its gas share and its density.

| env | best bodies | compared with phase 2a |
|---|---|---|
| open water | 1 g–0.36 kg undulators of 3–9 segments, fitness 3.17–3.38, everted gills or skin breathing, all neutrally buoyant (gas 3% of the volume, 1,025 kg/m³). **New: in 2/6 the best is a single-segment disc, 52–58 mm across and 1.8 mm thick, that drifts face-first at 1 cm/s with almost no muscle**, sieving plankton across its whole face (fitness 3.14) | as phase 1 and 2a for the undulators. The disc needs neutral buoyancy, which no body had before. It is medusa-like (medusae are among the cheapest swimmers; Gemmell et al. 2013), but it is fed by a capture rule (1.7 × the head's frontal area) that was justified by a fish's reaction field. A solid face-on disc does not clear its whole face: particles follow the streamlines round it (Rubenstein & Koehl 1977). **Suspect**, next correction |
| sea floor | **the crushing head pair in 4/6, and 8/20** (32–105 g; mineral 0.4–1.0, 2–6 joints, muscle up to the new 0.6, crush 1.5–6.5 N, 63–72% of intake as shelled prey, fitness 1.97–2.52). The rest are 0.15–0.65 kg soft swimmers along the bottom at 1.2–1.8. All neutrally buoyant | 1/6 and 7/20 in 2a. The rate is unchanged (§11.3); the jaws are stronger. **The bodies are still laterally compressed: H/W 4–18 in the jaw bodies (12–18 in 7 of 8), 1.4–13 in the rest**, against ~20 in phase 1 and the 30:1 Body Lab sheet. The plate and roll terms bite (seed 1's swimmer holds station 0.31 of the time in surge) but do not remove the form |
| land | 1–2 g legless crawlers of 21–25 segments; **rigid, moulting integument in 4/6** (rigidity 0.96–1.0, moult 5%) | as 2a (4/6) |

No walking legs, powered fins or flight evolved. The phase-1 reasons hold (§8): the food yields keep land bodies at
1–2 g, and undulation wins in water at the sizes that pay.

### 11.6 Gaps left, and whether phase 4 can start

Gaps, in the order they matter for phase 4:

1. **Water cost of non-fish bodies** (the median founder 3.8× live with fins ≥ 0.7). The shadow charges appendages
   their drag and cannot fold them (a folded wing or leg should cost ~its frontal area against the body, not its own
   wetted area). And the translator gives walkers no fat (body density ~1,000–1,060 in mammals), so they hold their
   depth at 1,094 kg/m³. Next: a fold state for passive pairs in water; a fat share of a segment's tissue (900 kg/m³).
2. **Land speed of large bodies** (0.63; ~0.65 of Garland above 3 kg). Next: an internal tubular skeleton, so a
   limb can be as light as Prange's skeleton allometry and still bear its load.
3. **Flight margin** (0.57). The two expressions scale differently with mass (M^−1/3 against M^−1/2), so no single
   correction brings the median within 25%. That is a decision for phase 4, not a calibration: the shadow's
   mechanistic scaling would make small fliers' margins ~3× smaller than the live sim's today.
4. **The plankton disc** in open water (capture by a solid face's whole frontal area) and the **compressed sea-floor
   bodies** (H/W 12–18), both suspects for P1b.
5. The cost of a gas space's depth (Boyle) is not modelled in the prototype, which has no depth. The live sim keeps
   BUOY_COST for lungs.

**Can phase 4 start?** No. §10.5 set the condition: water cost, flight margin and O2 supply within ~25%, because the
live ecology rests on them. O2 supply now is, as a scope. Water cost and flight margin are not. Each has a known
cause, and gap 3 is partly a choice of which scaling to keep. Phase 3 (storage) does not depend on these numbers and
can start.

Effort for the rest of P2, in agent-hours: folding passive pairs and a fat share (1–1.5); a tubular endoskeleton and
the limb mass check against Prange (1–1.5); the capture of a bluff face, and the compressed-body check over 20 seeds
(1); a decision on the flight-margin exponent with the owner (0.5).

## 12. Phase 2c: folding, fat, tubular bone, filtering faces and the flight-power exponent (2026-11-03)

### 12.1 Folding pairs in water

`Physics.SwimSet` builds the swimming properties three ways: every pair stroking, the drooped pairs (legs, droop
over 45°) folded, and every articulated pair folded. `SwimBest` takes the cheapest one the muscle can hold at each
speed, and the most sustainable one when none can. A folded pair lies along the trunk. It makes no thrust, lift or
span. Its edge no longer meets the flow and one face lies on the body, each as far as its articulation lets it fold
flat (seals, otters and diving birds tuck their limbs; murres half-fold their wings; Fish 1993; Lovvorn 2001;
Thaxter et al. 2010). A walker on the floor keeps its legs out. A 1 kg neutrally buoyant walker at 0.5 m/s needs
0.64 W with its legs out and 0.15 W with them folded (`ASwimmerFoldsItsLegsAndPaysLessThanDraggingThemOut`). A
cruising fish folds its paired fins too, so the translated fish now costs 0.45–0.65 of COT_SWIM_J at 0.1–10 kg. That
lies between the constant's own drag estimate for a streamlined 1 kg fish (~0.5 J/m) and its measured 1 J/m, and
below its M^0.7 line at 10 kg.

### 12.2 Fat, and a tubular skeleton

- **Fat** (segment gene `fat`): up to 0.5 of a segment's tissue volume is adipose tissue at 900 kg/m³. It is neither
  gut nor muscle, and it metabolises at 0.2 of the body's average per kg (Elia 1992). Kleiber's law was fitted to
  animals that carry typical fat, ~0.075 M^1.19 kg (Pitts & Bullard 1968; Calder 1984). So the metabolic mass is the
  mass, less the mineral and 0.8 × the fat beyond that typical share (a leaner body has more active tissue).
  Translated bodies carry the typical fat. A 1 kg walker is now 7.4% fat and 1,044 kg/m³ (it was 1,094).
- **Tubular bone** (appendage gene `hollow`): the internal skeleton is a tube of inner/outer diameter K ≤ 0.9 with
  the rod's area, no wider than the limb. Its section modulus is (1 + K²)/√(1 − K²) × a rod's of the same mass: 1.7 at
  K 0.6 (`AHollowBoneBearsMoreThanASolidRodOfTheSameMass`; Currey & Alexander 1985). Its bore holds the limb's soft
  tissue, so it costs no mass. At K = 0 it is the old rod, bit for bit.
- **Translator legs:** slenderness 0.08 → 0.06 (inside the cited cursorial 0.06–0.1), bone K 0.6 (terrestrial
  mammals 0.55–0.6), and thickness ∝ length^(1/3), so every leg bears the body's weight at the same safety factor
  (bending stress F L / d³ fixed; Alexander 1981). A full leg pair is now 14% of the body, its mineral 4.0% (whole
  skeleton 5–7.5% at 0.1–10 kg; Prange et al. 1979); it was 22% and 6.5%. The plain walker's sprint is 1.27, 0.89 and
  0.78 of Garland's at 0.1, 1 and 10 kg (it was 1.04, 0.73 and 0.66).
- Two translator artefacts found on the way. (1) A small wing gene's short thorax set the genome's length scale until
  the legs clamped at their longest gene, 30% short; the thorax no longer sets it. (2) The leg (and fin) gene now sets
  the body's whole leg muscle, however many trunk segments the fins need (one leg pair among four fin segments had a
  quarter of a walker's leg muscle).

### 12.3 Plankton capture through a porous face

Food is cleared only from the water that passes through the head: in at the mouth, out through its everted exchange
surface (gill slits and rakers, a sponge's or a tunicate's pores). The water pushed round a solid face carries its
particles round it (Rubenstein & Koehl 1977; Shimeta & Jumars 1991). The head's open fraction φ is its everted
epithelium's unfolded area over its frontal area, at most 1. A screen of open fraction φ has the pressure-loss
coefficient k = (1 − φ)/φ² and passes 1/(1 + k/4) of the oncoming flow (Taylor & Davies 1944; the leakiness of
filters, Cheer & Koehl 1987): 0.67 at φ = 0.5, 0.04 at 0.1, 0 for a solid face. The 2b disc, solid, now clears
nothing (`ASolidFaceClearsNoPlanktonAndAFilteringFaceClearsByItsOpenFraction`).

### 12.4 The flight-power exponent: the live side was wrong

The live margin was wings × (FLY_M_MAX/M)^(7/6 − 2/3), falling as M^(−1/2). The shadow's mechanistic margin falls as
~M^(−1/3). The evidence:

| line of evidence | power available ∝ | margin ∝ |
|---|---|---|
| Pennycuick 1968, 1975 (classic): fixed work per kg per beat, f ∝ M^(−1/3) | M^(2/3) | M^(−1/2) |
| Pennycuick 1996, 2001: measured f ∝ M^(3/8) b^(−23/24) S^(−1/3) ρ^(−3/8), i.e. f ∝ M^(−1/6) for similar fliers; fixed work per beat (Askew & Marsh 2002) | M^(5/6) | M^(−1/3) |
| Ellington 1991; Marden 1994: measured | M^(0.7–0.8) | M^(−0.37 to −0.47) |
| Marden 2005: maximum force output of flight motors ∝ M^1.0; at a size-independent stroke velocity | up to M^1 | up to M^(−1/6) |

Every line except the classic one lies above 2/3. The classic one rests on a wingbeat-frequency scaling that similar
fliers do not follow. **Choice: EXP_FLY_AVAIL = 5/6**, the mechanistic value from the measured frequency allometry,
inside the measured range and below Marden's upper bound. It agrees with the shadow's own wingbeat-frequency
mechanism. The live side changed (`Constants.EXP_FLY_AVAIL`). The ceiling stays at FLY_M_MAX = 16 kg for full
wings. Below it margins are smaller (5.4 instead of 12.6 at 0.1 kg; the shadow gives 4.2), and the half-wing ceiling
is 2 kg (was 4).

**It could not stay bit-identical.** The default world has fliers from its first step, so their margins, flight
speeds and which bodies fly change. `StepLengthTests.DefaultStepIsBitIdenticalToBefore` is re-pinned with a dated
comment (11681709169049197875 → 873474179074618927). `ClimateTests.NoClimateIsBitIdenticalToBefore` is unchanged. The
flight-related live classes pass unchanged except the half-wing ceiling in `EcologyTests.LargeBodiesCannotFly` (now
2 kg): Ecology, Diving, Locomotion and Wading (68 tests), Ocean (11).

### 12.5 Shadow against live: the founders of seeds 1–5 (phase 2c)

21,714 founders. Phase 2b's median in brackets.

| capability | median (2b) | p10 | p90 | verdict |
|---|---|---|---|---|
| drag shape | **1.04** (1.05) | 0.98 | 1.68 | within 25% |
| standing height | **1.10** (1.09) | 0.80 | 1.56 | within 25% |
| land cost/m, legs ≥ 0.7 | **0.97** (0.98) | 0.78 | 2.92 | within 25% |
| land speed (rel.) | 0.74 (0.86) | 0.31 | 1.19 | just outside; see below |
| air cost/m | 1.31 (1.29) | 1.05 | 1.64 | just outside (profile drag on bluff fliers, §11.4); asserted ≤ 1.35 |
| water cruise, fins ≥ 0.7 | 1.33 (0.96) | 1.05 | 2.98 | just outside: folding makes the least-cost cruise faster |
| basal cost | 0.75 (0.74) | 0.66 | 0.86 | as before (the live organ-upkeep terms) |
| O2 scope in air / water | **0.93 / 0.92** (0.93 / 0.91) | | | within 25% |
| **water cost/m, fins ≥ 0.7 at a fish's shape** | **1.30** (2.52) | 0.92 | 2.03 | at the edge: folded fins and limbs, and fat |
| water cost/m, fins ≥ 0.7 | 2.26 (3.83) | 1.58 | 5.57 | the rest is the founders' bluff shapes (fineness < 1.5), which the live cost ignores |
| water cost/m, all | 3.34 (4.21) | 1.61 | 7.57 | the same |
| land speed (m/s) | 0.64 (0.63) | 0.26 | 0.99 | outside; see below |
| flight margin | 0.57 (0.57) | 0.14 | 1.46 | outside, for a different reason now; see below |
| flies (agreement) | 82% (77%) | | | |

Where the three still apart now stand:

- **Flight margin.** The exponents now agree; full-winged reference fliers are at 0.77 (0.1 kg) and 0.69 (10 kg).
  The median founder is still 0.57 because the live margin is linear in the wing gene and blind to shape, while
  founders' fliers are bluff (parasite drag) and 75% weigh over 3 kg. That is live less physical.
- **Land speed.** The plain walker is within 25% of Garland at 0.1–10 kg. The founders' median is pulled down by
  bodies with every organ high at once. One clade in seed 1 (11–32 kg, one leg pair, four full fin pairs, wings 0.4,
  a fish's axial muscle) runs at 1.3 m/s against the plain body's 11: its trunk cannot house all that muscle
  (MUSCLE_MAX_SHARE 0.7), so its one leg pair holds 61% of its weight. Without the fins it runs at 8.3 m/s. The live
  sim credits every organ gene in full, independently. That is live less physical too.
- **Water cost.** At a fish's shape the founders are now at 1.30. The rest is shape.

### 12.6 What the prototype evolved (phase 2c)

| env | best bodies | compared with 2b |
|---|---|---|
| open water (6 seeds) | **6/6 are one-segment barrels**, 34–263 g, fineness 5.2–5.7, H/W 4.4–6.2, neutral, everted exchange 0.003–0.012 m², swimming at 0.24–0.28 m/s, fitness 2.97–2.98 | the face-on disc is gone: a solid face clears nothing. The 1 g undulators are gone too: they had no gill openings. In a one-segment body the head is the whole body, so its whole side counts as exit area: a salp-like barrel, water in at the front and out through porous sides. Salps and tunicates are such filters, but the open fraction should count only the exit near the mouth (the head's own length). **Suspect**, next correction |
| sea floor (20 seeds) | **the crushing head pair in 6/20** (53–71 g, mineral 0.6–1.0, 6 joints, muscle 0.6, crush 3.6–5.1 N, 69–71% of intake as shelled prey, fitness 2.31–2.48); soft bottom swimmers otherwise (1.11–1.63). **New: in 1/20 (seed 15) a 2.1 kg rigid, moulting walker on three pairs of mineralised, jointed legs** (18 mm and 6 mm, 3–4 joints, droop 51–65°, "support"), gas 9% of the volume (1,018 kg/m³), fitness 1.41 | 8/20 in 2b, within noise. The armoured, legged walker that phase 1 never found now appears, though it is not the fittest plan. Jaw bodies' H/W is 2.9–16.2 (12–18 in 2b) |
| land (6 seeds) | 1–2 g legless crawlers of 8–30 segments; rigid, moulting integument in 3/6 (rigidity 0.99–1.0; 0.41 in one more) | 4/6 in 2b |

### 12.7 Can phase 4 start?

§10.5 set the condition: water cost, flight margin and O2 supply within ~25%. Measured against the cases where the
live side is physically grounded:

- **O2 supply: met**, as a scope (0.93 / 0.92).
- **Water cost: met at the body COT_SWIM_J describes** (1.30 at a fish's shape, at the edge). The raw founders (2.26)
  differ by body shape, which the live cost ignores and the shadow does not. The shadow is the more physical side.
- **Flight margin: the scaling is now agreed** (both M^−1/3; full wings 0.69–0.77). The median founder (0.57)
  differs by body shape and by how the margin grows with the wing gene. Again the live side is the less physical.

So the remaining disagreements are no longer shadow errors. They are the live sim's simplifications, which phase 4
exists to replace. **Phase 4 can start**, with two conditions:

1. The owner accepts that the live ecology will move where the shadow is more physical. Bluff swimmers and bluff
   fliers lose (water cost ×2, margin ×0.6). Bodies with every organ gene high lose most (they cannot house the
   muscle). The flight exponent change (§12.4) has already moved fliers in every world.
2. The head-exit suspect (§12.6) is fixed first (~0.5 agent-hour), so the live sim does not inherit salp barrels.

Land speed (0.64, relative 0.74) is not a gate in §10.5, and its gap is the same over-committed bodies.

## 13. Phases 3-5: the module body goes live (2026-11-03)

### 13.1 The head-exit fix (the salp barrel of §12.6)

`Physics.HeadOpenFraction` now counts only the everted epithelium on the flow path: within `PHARYNX_D` = 1 section
diameter (√(W H) of the head) of the mouth. Water that enters the mouth leaves through the pharynx's walls within about
one diameter; farther along a closed tube it has to travel the tube first, and the resistance of that path grows with its
length (Hagen-Poiseuille; Vogel 1994). A fish's gill slits lie within about a head depth behind its mouth (Liem 1980).
Test: `OnlyTheExitNearTheMouthFiltersSoALongPorousBarrelClearsNoMoreThanItsHead` (a barrel of L/D 3.9 now has an open
fraction of 0.34, against 0.45 for a head of L/D 1 with the same pores).

Open-water demo, 6 seeds x 1,000 generations, after the fix:

| seed | fitness | mass | segments | pairs | fineness | exchange (everted) | mode |
|---|---|---|---|---|---|---|---|
| 1 | 2.97 | 219 g | 6 | 0 | 5.3 | 0.0100 m², all on the head segment | undulating swim 0.28 m/s |
| 2 | 2.87 | 138 g | 8 | 0 | 6.3 | 0.0056 | 0.32 m/s |
| 3 | 2.94 | 211 g | 8 | 0 | 5.0 | 0.0095 | 0.28 m/s |
| 4 | 2.94 | 9 g | 9 | 2 (1 mm passive buds) | 4.6 | 0.0012 | 0.20 m/s |
| 5 | 2.93 | 32 g | 8 | 0 | 5.3 | 0.0028 | 0.24 m/s |
| 6 | 2.98 | 149 g | 6 | 0 | 5.5 | 0.0080 | 0.28 m/s |

The one-segment barrels are gone in 6/6. Every best body is now a multi-segment undulator of fishlike fineness whose
porous (gill) surface is confined to its first segment: a head with gill openings and a muscular trunk behind it.
Fitness is ~1% below the barrels' (2.87-2.98 against 2.97-2.98), so the barrel had been exploiting the loophole by
only a little, but it set every run's body plan.

### 13.2 Phase 3: storage

- **The block.** Every live genome ends in a module block of `G.MORPHN` = 681 bytes at `G.MORPH`, after the brain,
  body and reflex genes (`Morph/Codec.cs`): a segment-gene count, then 8 segment slots of 85 bytes (a 4-byte NEAT
  innovation number, the 12 segment parameters, an appendage count, and 4 appendage slots of a 4-byte innovation
  number and 13 parameters). A parameter is one byte, `(b + 127) / 254`. Genes beyond the count are ignored, and
  duplication and deletion shift entries within the block. `G.LEN` grows by 681 bytes (from ~98 KB).
- **The cap is the developmental limit of the prototype** and is stated: at most 8 segment genes (`MAX_SEG_GENES`),
  each grown as up to 4 serial copies (`Development.MAX_REPEAT`), so at most 32 segments, and 4 appendage genes per
  segment gene, so at most 128 pairs. It limits how many distinct segment identities (Hox-like genes) a body carries,
  not its size, since the mass gene scales any body (bilaterians carry ~4–14 Hox paralogue groups; Carroll, Grenier &
  Weatherbee 2005). An operator that would exceed it does nothing.
- **Mutation** (`Brain.Mutate` → `Codec.MutateBlock`): every Morph operator at the prototype's per-offspring rates
  (point 0.02 per parameter × N(0, 0.08), diverge 5%, duplicate and delete a segment 3% each, duplicate, delete and
  insert an appendage 4% each, the repeat count as a parameter). The byte-level rate `MUT_RATE` is now `1 / G.MORPH`,
  the length of every gene but the block, so it is what `1 / G.LEN` was.
- **Innovation numbers** for new modules come from a run of numbers that starts at a hash of (seed, step, slot), at
  2^16 or above (clear of the translator's fixed role numbers). No shared counter is needed, so births on any thread
  stay deterministic. Two lineages share a number only by a 32-bit hash collision.
- **Crossover** (`Brain.Crossover` → `Codec.CrossBlock`) is `Homology.Cross`: the child takes one parent's module order
  and structure, at random (the live sim has no fitness at mating; NEAT keeps the fitter parent's disjoint genes), and
  each module both parents carry from either.
- **Distance.** `Body.Distance` (mating compatibility) is √(½ d_body² + ½ d_morph²), where d_morph
  (`Codec.Distance`, read from the bytes without allocating) is an RMS per gene on the body genes' scale: matched
  modules by their parameter differences, every parameter of an unmatched module at √(2/3), the RMS difference of two
  independent uniform genes. Genomes without a block compare by body genes alone. Clade splitting (`Clades.Vis`) and
  the look signature (`Body.Signature`, `LooksAlike`) use the grown body's visible traits (§13.5).
- **Formats.** Checkpoint version 28 → 29, gene bank 13 → 14. Older genomes are read at their old length
  (`G.MORPH`), and `Codec.TranslateInto` writes each one's block from its labelled body genes (Morph.Translate, its
  exchange surfaces sized to its own basal as the shadow sized them). Every living genome and every archive entry is
  translated, and every animal is expressed again. A genome with no block (a hand-made test genome) is translated each
  time it is expressed, so a test can still describe "a 1 kg legged walker" by its labelled genes.

### 13.3 Phase 4: the switch

`Body.Express` decodes the block, grows it at the live mass (`Development.Grow(g, mass)`), and takes every body
capability from the physics of that morphology (`Morph/Live.cs`):

| live value | from the morphology | conversion |
|---|---|---|
| PVLand, PVWater, PVAir | top speed on flat ground (`Shadow.LandMech` at burst power 250 W/kg), burst in water (within Wardle's twitch limit), level flight (v_mp × `TopFlightSpeed(margin)`) | a step's travel = v / Garland's sprint × the plain body's travel (the live reading of speed, `Body.ReachM`) |
| PLoco, PRealm | flies if margin ≥ 1 and flight muscle ≥ FLY_WING_MIN × FLY_MUSCLE_MAX of the mass (Marden 1987); deep water if it sustains DEEP_SWIM × CRUISE_BL lengths a second; sessile if it cannot move | as before on these |
| PMargin; PWings | `Physics.FlightMargin` (wingbeat-frequency-limited muscle work against the least power of level flight); flight muscle of lifting pairs / (FLY_MUSCLE_MAX M) | take-off and loaded margin (`Body.CanTakeOffOf`, `LoadedMarginOf`): a juvenile's grown muscle share and (pm/m)^(1/3) |
| PCotLand, PCotLandTop, PCotWater | least net cost a metre walking or crawling (`WalkMetabolic`), at the top speed; at the cruise of least gross cost a metre in water (a body that cannot sustain any swimming pays burst power anaerobically, ×3) | J/m → energy units a world unit (M_PER_UNIT / J_PER_E), × (mass/adult)^0.7. MoveCost runs from the least to the top cost with speed |
| PCruise | the swimming speed of least gross cost a metre (Videler 1993) | × (mass/adult)^(1/3) |
| PStand | limb clearance + trunk height | × (mass/adult)^(1/3) (Respiration.StandM) |
| PScopeAir, PScopeLung, PScopeWater | `Physics.O2Supply` in air and in water (everted and folded-in surfaces, the gas space's lining, the skin through its integument), and its air-breathing surfaces alone | over its basal: Respiration's uptake = basal × the medium's oxygen (altitude's partial pressure, dissolved O2) × scope; a swimmer's head at the surface breathes the lung scope |
| PArmour | crushing odds against a bite scaled to its size (`Physics.PKill`) over the plain body's | DEF_BASE × (gain − 1), so defence = mass × DEF_BASE × gain: the live strike's power / (power + defence) is the physics' bite / (bite + crush) |
| moulting | a rigid outer skeleton is soft for MOULT_SOFT_GROW (0.15) × rigidity of the time it grows, every MOULT_DAYS (20 d) (Drach 1939; Hartnoll 1982), and sheds its cuticle (MOULT_COST × rigidity × exoskeleton mass) while it grows | `World.Moulting`: armour 0 and speed × (1 − rigidity) when soft; the cost charged to its reserve while growing |
| PTurn | least turning radius in water at 1 m/s (`Physics.TurnRadius`) | C_TURN M^EXP_TURN × the plain body's radius (× M^(1/3)) / its own |
| PBasal, gut | Kleiber on the active (non-mineral, lean) mass × level × the factor of the genes that stay genes, integument turnover, exchange epithelium upkeep, brain; the gut built to it (`Body.GutScale`) | W → energy units. The organ upkeep terms (COST_LIMB, COST_WING, COST_ARMOUR_ABS, OrganUpkeep) and B_SPEED's cost are gone |
| drag shape, buoyancy | in the swimming budget: wetted-area drag by fineness and Reynolds number, holding depth against \|M − ρV\| g with the gas space and fat | — |
| heat (Heat.Conductance) | PSurfK: its segments' surface, each through its integument (1 / (1 + t / 4 cm): tissue ~0.4 W/(m K) against a boundary layer of ~10 W/(m² K)); PAppSurf: its pairs' surface on the same scale (Allen's rule) | over the plain body's trunk surface × M^(2/3); fur (B_INSUL, kept) covers it as before |
| water (thirst) | PEvapK: evaporation of everted wet surface, of its lungs at basal, and of its skin through its integument (Physics' EVAP terms) | over the plain body's; replaces Respiration.Drying's GILL_EVAP |
| PLen | the grown body's length | — |
| PCrush | the strongest crushing force at the mouth (`Physics.Handle`) | reported only (§13.9) |

The labelled body genes `B_LEGS`, `B_FINS`, `B_WINGS`, `B_LEGN`, `B_FINN`, `B_ARMOUR`, `B_LUNGS`, `B_SPEED`, `B_APPEND`
and the shape genes are retired: they stay in the layout and are read only to translate a genome that has no block.
`PLegs`, `PFins`, `PWings`, `PLungs`, `PLegPairs` and `PFinPairs` now hold the physics' equivalents of those genes,
each derived from a physical quantity (the table in `Body.Express`), for the renderer, telemetry and the few gates
that read them (wading, paddling, the footing's stability, depth range). The shadow comparison is retired with the
switch (`Shadow.Express` is no longer called).

### 13.4 Founding

`Clades.AncestorMorph` grows each random ancestor from the generic seed (`MorphGenome.Blob`, one segment) with its
parameters drawn anywhere in their ranges, then up to 10 random rounds of the module operators: a tandem duplication
of a segment gene (then diverged by N(0, 0.15)), a de novo appendage with every parameter random, a duplicated
appendage (diverged), or a deleted one. Nothing says leg, fin or wing. Whether it can live where it starts is the
founding's test (breathing, moving and being fed in its home range), up to ANCESTOR_TRIES = 256 draws, as for every
other ancestor gene. In a sea world the draw is the same and the sea is the founding's realm. Founders vary around
their ancestor by FOUNDER_BODY_SD on every module parameter (`Clades.VaryMorph`).

### 13.5 Visible traits

Clade splitting uses the grown body's traits (`Body.MorphVisible`): segments, pairs, fineness, depth, longest limb,
limb angle, limb flatness, armour (rigidity), exchange surface and joints, with size, eyes, weapons and eye pairs. The
look signature carries the physics' organ equivalents and the morphology in place of the shape genes.

### 13.6 Console

The clade table (`clades` and `meta.species`, docs/ICD.md) carries each clade's `morph`: mean segment and pair counts,
and its representative member's grown body, every segment (L, W, H, rigidity, porosity, gas) and every pair (length,
flatness, joints, rigidity, porosity, droop, sagittal, and its physics role). The AGENTS record keeps its layout; its
legs, fins, wings, armour and shape bytes are the morphology's legacy display values, so the existing renderer keeps
working until it draws the module body.
## References

- Phase 2c (§12): Calder, W. A. (1984). *Size, Function, and Life History*. Harvard. Cheer, A. Y. L. & Koehl, M. A. R. (1987). Paddles and rakes: fluid flow through bristled appendages of small organisms. *J. Theor. Biol.* 129. Currey, J. D. & Alexander, R. McN. (1985). The thickness of the walls of tubular bones. *J. Zool.* 206. Elia, M. (1992). Organ and tissue contribution to metabolic rate. In *Energy Metabolism* (Kinney & Tucker, eds.). Raven. Ellington, C. P. (1991). Limitations on animal flight performance. *J. Exp. Biol.* 160. Lovvorn, J. R. (2001). Upstroke thrust, drag effects, and stroke-glide cycles in wing-propelled swimming by birds. *Am. Zool.* 41. Marden, J. H. (1994). From damselflies to pterosaurs: how burst and sustainable flight performance scale with size. *Am. J. Physiol.* 266. Marden, J. H. (2005). Scaling of maximum net force output by motors used for locomotion. *J. Exp. Biol.* 208. Pennycuick, C. J. (1996). Wingbeat frequency of birds in steady cruising flight: new data and improved predictions. *J. Exp. Biol.* 199. Pennycuick, C. J. (2001). Speeds and wingbeat frequencies of migrating birds compared with calculated benchmarks. *J. Exp. Biol.* 204. Pitts, G. C. & Bullard, T. R. (1968). Some interspecific aspects of body composition in mammals. In *Body Composition in Animals and Man*. NAS. Shimeta, J. & Jumars, P. A. (1991). Physical mechanisms and rates of particle capture by suspension-feeders. *Oceanogr. Mar. Biol. Annu. Rev.* 29. Taylor, G. I. & Davies, R. M. (1944). The aerodynamics of porous sheets. *ARC R&M* 2237. Thaxter, C. B. et al. (2010). Influence of wing loading on the trade-off between pursuit-diving and flight in common guillemots and razorbills. *J. Exp. Biol.* 213.
- Phase 2b (§11): Alexander, R. McN. (1966). Physical aspects of swimbladder function. *Biol. Rev.* 41. Alexander, R. McN. (1988). *Elastic Mechanisms in Animal Movement*. Cambridge. Bennett, M. B. & Taylor, G. C. (1995). Scaling of elastic strain energy in kangaroos and the benefits of being big. *Nature* 378. Biewener, A. A. & Blickhan, R. (1988). Kangaroo rat locomotion: design for elastic energy storage or acceleration? *J. Exp. Biol.* 140. Chai, P. & Millard, D. (1997). Flight and size constraints: hovering performance of large hummingbirds under maximal loading. *J. Exp. Biol.* 200. Fredsøe, J. & Deigaard, R. (1992). *Mechanics of Coastal Sediment Transport*. World Scientific. Gehr, P. et al. (1981). Design of the mammalian respiratory system. V. Scaling morphometric pulmonary diffusing capacity to body mass. *Respir. Physiol.* 44. Gemmell, B. J. et al. (2013). Passive energy recapture in jellyfish contributes to propulsive advantage over other metazoans. *PNAS* 110. Gibson, L. J. & Ashby, M. F. (1997). *Cellular Solids*, 2nd ed. Cambridge. Greenewalt, C. H. (1962). Dimensional relationships for flying animals. *Smithson. Misc. Collect.* 144. Jonsson, I. G. & Carlsen, N. A. (1976). Experimental and theoretical investigations in an oscillatory turbulent boundary layer. *J. Hydraul. Res.* 14. Marden, J. H. (1987). Maximum lift production during takeoff in flying animals. *J. Exp. Biol.* 130. Prange, H. D., Anderson, J. F. & Rahn, H. (1979). Scaling of skeletal mass to body mass in birds and mammals. *Am. Nat.* 113. Rubenstein, D. I. & Koehl, M. A. R. (1977). The mechanisms of filter feeding: some theoretical considerations. *Am. Nat.* 111. Stahl, W. R. (1967). Scaling of respiratory variables in mammals. *J. Appl. Physiol.* 22. Timoshenko, S. & Woinowsky-Krieger, S. (1959). *Theory of Plates and Shells*, 2nd ed. McGraw-Hill. Tucker, V. A. (1987). Gliding birds: the effect of variable wing span. *J. Exp. Biol.* 133. Videler, J. J. (1993). *Fish Swimming*. Chapman & Hall. Wardle, C. S. (1975). Limit of fish swimming speed. *Nature* 255. Webb, P. W. (2002). Control of posture, depth, and swimming trajectories of fishes. *Integr. Comp. Biol.* 42. Weibel, E. R. & Hoppeler, H. (2005). Exercise-induced maximal metabolic rate scales with muscle aerobic capacity. *J. Exp. Biol.* 208.
- Aksnes, D. L. & Giske, J. (1993). A theoretical model of aquatic visual feeding. *Ecol. Model.* 67.
- Boulding, E. G. (1984). Crab-resistant features of shells of burrowing bivalves. *J. Exp. Mar. Biol. Ecol.* 76.
- Brose, U. et al. (2006). Consumer-resource body-size relationships in natural food webs. *Ecology* 87.
- Charnov, E. L. (1976). Optimal foraging, the marginal value theorem. *Theor. Popul. Biol.* 9.
- Charnov, E. L. (1993). *Life History Invariants*. Oxford.
- Cohen, J. E., Pimm, S. L., Yodzis, P. & Saldaña, J. (1993). Body sizes of animal predators and animal prey in food webs. *J. Anim. Ecol.* 62.
- Currey, J. D. (1967). The failure of exoskeletons and endoskeletons. *J. Morphol.* 123.
- Demment, M. W. & Van Soest, P. J. (1985). A nutritional explanation for body-size patterns of ruminant and nonruminant herbivores. *Am. Nat.* 125.
- Domenici, P. & Blake, R. W. (1997). The kinematics and performance of fish fast-start swimming. *J. Exp. Biol.* 200.
- Eggers, D. M. (1977). The nature of prey selection by planktivorous fish. *Ecology* 58.
- Elner, R. W. & Hughes, R. N. (1978). Energy maximization in the diet of the shore crab, *Carcinus maenas*. *J. Anim. Ecol.* 47.
- Fish, F. E. (2002). Balancing requirements for stability and maneuverability in cetaceans. *Integr. Comp. Biol.* 42.
- Goldberg, D. E. & Richardson, J. (1987). Genetic algorithms with sharing for multimodal function optimization. *ICGA* 2.
- Gray, J. (1946). The mechanism of locomotion in snakes. *J. Exp. Biol.* 23.
- Holling, C. S. (1959). Some characteristics of simple types of predation and parasitism. *Can. Entomol.* 91.
- Howland, H. C. (1974). Optimal strategies for predator avoidance: the relative importance of speed and manoeuvrability. *J. Theor. Biol.* 47.
- Josephson, R. K. (1993). Contraction dynamics and power output of skeletal muscle. *Annu. Rev. Physiol.* 55.
- McGhee, R. B. & Frank, A. A. (1968). On the stability properties of quadruped creeping gaits. *Math. Biosci.* 3.
- Peters, R. H. (1983). *The Ecological Implications of Body Size*. Cambridge.
- Peterson, I. & Wroblewski, J. S. (1984). Mortality rate of fishes in the pelagic ecosystem. *Can. J. Fish. Aquat. Sci.* 41.
- Quillin, K. J. (1999). Kinematic scaling of locomotion by hydrostatic animals: ontogeny of peristaltic crawling by the earthworm. *J. Exp. Biol.* 202.
- Schlichting, H. (1979). *Boundary-Layer Theory*, 7th ed. McGraw-Hill.
- Snodgrass, R. E. (1935). *Principles of Insect Morphology*. McGraw-Hill.
- Ting, L. H., Blickhan, R. & Full, R. J. (1994). Dynamic and static stability in hexapedal runners. *J. Exp. Biol.* 197.
- Valiela, I. (1995). *Marine Ecological Processes*, 2nd ed. Springer.
- Walton, M., Jayne, B. C. & Bennett, A. F. (1990). The energetic cost of limbless locomotion. *Science* 249.
- Warner, G. F. & Jones, A. R. (1976). Leverage and muscle type in crab chelae. *J. Zool.* 180.
- Webb, P. W. (1983). Speed, acceleration and manoeuvrability of two teleost fishes. *J. Exp. Biol.* 102.
- Webb, P. W. (1984). Body form, locomotion and foraging in aquatic vertebrates. *Am. Zool.* 24.
- Weihs, D. (2002). Stability versus maneuverability in aquatic locomotion. *Integr. Comp. Biol.* 42.
- Werner, E. E. (1974). The fish size, prey size, handling time relation in several sunfishes. *J. Fish. Res. Board Can.* 31.
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
