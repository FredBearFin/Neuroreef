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
| P2 | shadow expression: translate every live genome (table above) and compute Morph capabilities *alongside* `Body.Express`; report both in telemetry; calibrate the module physics until the translated bodies of today's ancestors match today's speeds and costs within ~20% | 4–8 agent-hours |
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

## References

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
