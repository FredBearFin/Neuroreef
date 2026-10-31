// NEUROREEF 2 - CONSTANTS TABLE (NR-106)
//
// Every tuning value of the simulation is declared here, once. Each entry carries its meaning,
// unit and origin. docs/CONSTANTS.md is generated from this file (Neuroreef.exe --print-constants)
// and a test fails if the two disagree.
//
// Origin codes:  V1  = taken from Neuroreef v1
//                ECO = ecological rule or exponent from the literature
//                GEO = geological or geophysical value from the literature (Revision R)
//                HT  = hand-tuned in headless runs
//
// One step = one tick. Distances are world units. Energy is arbitrary units (1 unit of body mass
// stores C_EMAX units). Field amounts are mass per 4x4 cell.

using System;

namespace Neuroreef;

[AttributeUsage(AttributeTargets.Field)]
public sealed class KAttribute : Attribute
{
    public readonly string Group, Meaning, Unit, Origin;
    public KAttribute(string group, string meaning, string unit, string origin)
    { Group = group; Meaning = meaning; Unit = unit; Origin = origin; }
}

public static class K
{
    // ---------------------------------------------------------------- 1. world
    [K("World", "side of the toroidal world", "units", "HT")] public const int WORLD = 1024;
    [K("World", "field cell side", "units", "V1")] public const int FCELL = 4;
    [K("World", "field grid side (WORLD/FCELL)", "cells", "-")] public const int FW = WORLD / FCELL;
    [K("World", "spatial-hash cell side", "units", "V1")] public const int HCELL = 16;
    [K("World", "spatial-hash grid side", "cells", "-")] public const int GW = WORLD / HCELL;
    [K("World", "agent slots (animals, eggs, embryos) a world starts with: it grows as births need more, limited only by memory (World.Grow)", "slots", "HT: ~212 KB per slot with 256-unit brains (World.SlotBytes); not a cap since 2026-10-31: a seed gives the same run from any start")] public const int CAPACITY = 16384;
    [K("World", "share of the map below sea level (Nature.Water, the default): the sea; lakes above it come from the water balance of closed basins (2026-10-31; before, every cell below this quantile was a lake at one level)", "fraction", "HT")] public const float LAKE_SHARE = 0.10f;
    [K("World", "depth below which water is shallow (wadeable, algae grow)", "elev", "HT")] public const float SHALLOW = 0.035f;
    [K("World", "season period (one year), by default", "steps", "ECO: about one lifespan at M=1, so seasonal life histories can evolve (ecology review; 9000 before Revision K)")] public const int T_SEASON = 3000;
    [K("World", "season amplitude on moisture", "-", "V1")] public const float A_SEASON = 0.45f;
    [K("World", "telemetry sample interval", "steps", "HT")] public const int SAMPLE_EVERY = 32;
    [K("World", "telemetry ring length", "samples", "HT")] public const int RING = 4096;
    [K("World", "long-history ring length (one sample in LONG_EVERY)", "samples", "HT: with LONG_EVERY it covers 33 million steps")] public const int LONG_RING = 2048;
    [K("World", "samples between long-history samples", "samples", "HT")] public const int LONG_EVERY = 512;
    [K("World", "max neighbours examined per animal per step", "animals", "HT")] public const int MAX_NEIGH = 64;
    [K("World", "max sight range (bounded by the 5x5 hash neighbourhood)", "units", "HT")] public const float MAX_SIGHT = 32f;

    // ---------------------------------------------------------------- 2. allometry
    [K("Allometry", "Kleiber exponent for basal cost and digestion", "-", "ECO")] public const float EXP_BASAL = 0.75f;
    [K("Allometry", "max speed exponent", "-", "ECO")] public const float EXP_SPEED = 0.17f;
    [K("Allometry", "lifespan and maturity exponent", "-", "ECO")] public const float EXP_LIFE = 0.25f;
    [K("Allometry", "sight range exponent", "-", "HT")] public const float EXP_SIGHT = 0.20f;
    [K("Allometry", "turn rate exponent", "-", "HT")] public const float EXP_TURN = -0.20f;
    [K("Allometry", "basal cost at M=1", "E/step", "ECO: Revision M, ecology review: x4, so an animal burns many times its tissue energy over a life (Rubner 1908; Speakman 2005) and fasts ~1/16 of a life, not a third (Lindstedt & Boyce 1985)")] public const float C_BASAL = 0.0040f;
    [K("Allometry", "energy reserve per unit mass", "E/M", "ECO: stored reserve of the same order as tissue energy")] public const float C_EMAX = 2.0f;
    [K("Allometry", "gut capacity per unit mass", "M/M", "HT")] public const float C_GUT = 0.30f;
    [K("Allometry", "digestion rate at M=1", "M/step", "ECO: Revision M: the most an animal can digest is ~5x basal at 0.5 digestibility (Hammond & Diamond 1997)")] public const float C_DIGEST = 0.025f;
    [K("Allometry", "bite size at M=1", "M/step", "Revision M: x5 with digestion (a bite fills the gut faster than it empties)")] public const float C_BITE = 0.06f;
    [K("Allometry", "max speed at M=1 (walk)", "units/step", "V1")] public const float C_SPEED = 1.20f;
    [K("Allometry", "turn rate at M=1", "rad/step", "V1")] public const float C_TURN = 0.32f;
    [K("Allometry", "lifespan at M=1", "steps", "V1")] public const float C_LIFE = 3200f;
    [K("Allometry", "maturity age at M=1", "steps", "V1")] public const float C_MATURE = 250f;
    [K("Allometry", "sight range at M=1", "units", "V1")] public const float C_SIGHT = 16f;
    [K("Allometry", "movement cost (x C_SPEED x M^EXP_MOVE x speed)", "E/(M^0.7*v*C_SPEED)", "ECO: Revision M: x4 with basal")] public const float C_MOVE = 0.0040f;
    [K("Allometry", "mass exponent of the cost of transport", "-", "ECO: running cost per unit distance scales ~M^0.7 (Taylor, Heglund & Maloiy 1982)")] public const float EXP_MOVE = 0.7f;
    [K("Allometry", "speed gained on land per unit of wing that cannot yet lift the body (wing-assisted running, gliding)", "-", "ECO: proto-wings aid running up slopes and gliding before powered flight (Dial 2003)")] public const float GLIDE = 0.3f;
    [K("Reproduction", "cost to a hermaphrodite of siring, as a share of its own litter's cost (male function)", "-", "ECO: male function takes 10-30% of a simultaneous hermaphrodite's reproductive resources (Charnov 1982)")] public const float C_SIRE = 0.2f;
    [K("Clades", "two forms are separate clades when, along the axis between their centres (visible traits), the gap is this many pooled SDs", "-", "ECO: genotypic clusters with few intermediates (Mallet 1995)")] public const float SPLIT_GAP = 3f;
    [K("Allometry", "thirst rise per step (death after ~830 dry steps)", "/step", "HT")] public const float C_THIRST = 0.0012f;
    [K("Allometry", "fatigue gain at full sprint", "/step", "HT")] public const float C_FATIGUE = 0.012f;
    [K("Allometry", "fatigue recovery (x3 resting)", "/step", "HT")] public const float C_RECOVER = 0.004f;
    [K("Allometry", "basal multiplier while resting", "-", "HT")] public const float REST_BASAL = 0.85f;
    [K("Allometry", "speed factor for swimmers", "-", "HT")] public const float SWIM_SPEED = 0.85f;
    [K("Allometry", "drinking: thirst removed per step", "/step", "HT")] public const float DRINK_RATE = 0.25f;
    [K("Allometry", "efficiency of turning energy into new tissue (growth, offspring)", "-", "ECO: net growth efficiency 0.6-0.8")] public const float GROWTH_EFF = 0.75f;
    [K("Allometry", "a juvenile keeps this share of its reserve against starving; above it, it grows at its top rate times how full the reserve is", "xEmax", "ECO: growth draws on the reserve in proportion to its density (dynamic energy budget, Kooijman 2010); hungry young grow slowly (Revision D; Revision P: was a threshold of 0.5, below which the young, living near 40% full, hardly grew)")] public const float GROW_RESERVE = 0.2f;

    // ---------------------------------------------------------------- 3. food (energy density)
    [K("Food", "energy density of grass", "E/M", "V1")] public const float RHO_GRASS = 3.2f;
    [K("Food", "energy density of browse (leaves)", "E/M", "HT")] public const float RHO_BROWSE = 3.6f;
    [K("Food", "energy density of fruit", "E/M", "HT")] public const float RHO_FRUIT = 6.0f;
    [K("Food", "energy density of seeds", "E/M", "HT")] public const float RHO_SEED = 7.0f;
    [K("Food", "energy density of nectar", "E/M", "HT")] public const float RHO_NECTAR = 8.0f;
    [K("Food", "energy density of roots", "E/M", "HT")] public const float RHO_ROOT = 4.0f;
    [K("Food", "energy density of algae", "E/M", "HT")] public const float RHO_ALGAE = 3.2f;
    [K("Food", "energy density of phytoplankton", "E/M", "HT")] public const float RHO_PHYTO = 3.6f;
    [K("Food", "energy density of carrion (partly decayed tissue)", "E/M", "ECO: below fresh meat")] public const float RHO_CARRION = 3.5f;
    [K("Food", "energy density of dung", "E/M", "HT")] public const float RHO_DUNG = 2.0f;
    [K("Food", "energy density of leaf litter", "E/M", "HT")] public const float RHO_LITTER = 1.8f;
    [K("Food", "energy density of dead wood", "E/M", "HT")] public const float RHO_WOOD = 1.6f;
    [K("Food", "energy density of aquatic detritus", "E/M", "HT")] public const float RHO_DETRITUS = 2.4f;
    [K("Food", "energy density of body tissue (meat); also the cost of building tissue", "E/M", "ECO: tissue ~2-4x the stored reserve per unit mass")] public const float RHO_MEAT = 4.0f;
    [K("Food", "energy density of blood", "E/M", "ECO: below tissue")] public const float RHO_BLOOD = 3.0f;

    // ---------------------------------------------------------------- 4. hunting
    [K("Hunting", "base strike success", "-", "HT")] public const float P0_STRIKE = 0.45f;
    [K("Hunting", "prey-group confusion", "-", "V1")] public const float C_CONF = 0.25f;
    [K("Hunting", "cost of a strike", "E/M", "HT")] public const float C_STRIKE = 0.004f;
    [K("Hunting", "retaliation per unit of excess defence", "E", "HT")] public const float C_RETAL = 0.06f;
    [K("Hunting", "edible tissue (carcass) per unit body mass; also the tissue built in growth", "M/M", "ECO: about half of live mass is edible tissue")] public const float C_CARC = 0.5f;
    [K("Hunting", "defence multiplier for juveniles", "-", "HT")] public const float JUV_DEFENCE = 0.25f;
    [K("Hunting", "defence of an unarmoured body per unit mass (skin, muscle, bone)", "-", "ECO: a body's own size is its first defence; predators rarely take prey much larger than themselves (Carbone et al. 1999)")] public const float DEF_BASE = 0.5f;
    [K("Hunting", "energy a landed hit removes per unit of strike power (strike x attacker mass)", "E", "ECO: wounding scales with weapon force; an armed attacker subdues equal-sized prey in a few hits (Revision H)")] public const float C_DAMAGE = 0.4f;
    [K("Hunting", "parasite drain per strike at M=1", "E", "HT")] public const float C_DRAIN = 0.05f;
    [K("Hunting", "pain added by a failed strike received", "-", "HT")] public const float PAIN_HIT = 0.6f;
    [K("Hunting", "pain decay per step", "/step", "HT")] public const float PAIN_DECAY = 0.03f;

    // ---------------------------------------------------------------- 5. brains, learning, evolution
    [K("Brain", "genome weight scale (int8 / 16)", "-", "-")] public const float WSCALE = 1f / 16f;
    [K("Brain", "max |weight| (genome + learned)", "-", "-")] public const float WMAX = 7.9f;
    [K("Brain", "founder memory-layer (W1) weight range: round(U(-1,1)*FOUNDER_W)", "int8", "HT")] public const int FOUNDER_W = 12;
    [K("Brain", "founder output-layer (W2) weight range", "int8", "HT")] public const int FOUNDER_W2 = 4;
    [K("Brain", "founder output bias (x/16): every primitive of every species starts near 2% (calm newborn brain)", "int8", "HT: was -48 (5%) until 2026-10-30, when naive animals struck whatever was in reach every ~20 steps and founding groups killed each other")] public const int FOUNDER_BIAS = -64;
    [K("Brain", "founder output bias of each of the 8 call primitives (x/16): together about 5%, like one primitive", "int8", "HT: calm newborn brain (Revision M)")] public const int FOUNDER_BIAS_CALL = -80;
    [K("Brain", "random spread of the founder output bias", "int8", "HT")] public const int FOUNDER_BIAS_NOISE = 4;
    [K("Brain", "standing variation of founders' brain and reflex bytes around their ancestor's (SD)", "int8", "ECO: members of one population share an inherited nervous system with small heritable variation; half the founder output-weight range (Revision H)")] public const float FOUNDER_W_SD = 2f;
    [K("Brain", "learning rate eta range low", "-", "HT")] public const float ETA_LO = 0.002f;
    [K("Brain", "learning rate eta range high", "-", "HT")] public const float ETA_HI = 0.06f;
    [K("Brain", "trace decay lambda range low", "-", "HT")] public const float LAMBDA_LO = 0.50f;
    [K("Brain", "trace decay lambda range high", "-", "HT")] public const float LAMBDA_HI = 0.97f;
    [K("Brain", "exploration noise sigma range low", "-", "HT")] public const float SIGMA_LO = 0.02f;
    [K("Brain", "exploration noise sigma range high", "-", "HT")] public const float SIGMA_HI = 0.30f;
    [K("Brain", "learned-delta decay kappa range high (low = 0)", "/step", "HT")] public const float KAPPA_HI = 0.002f;
    [K("Brain", "reward baseline rate", "/step", "HT")] public const float RBAR_RATE = 0.01f;
    [K("Brain", "reward: per unit intake / Emax", "-", "HT")] public const float R_INTAKE = 4.0f;
    [K("Brain", "reward: per unit thirst relieved", "-", "HT")] public const float R_DRINK = 2.0f;
    [K("Brain", "reward: per unit fatigue relieved", "-", "HT")] public const float R_REST = 0.5f;
    [K("Brain", "reward: mating", "-", "HT")] public const float R_MATE = 1.0f;
    [K("Brain", "reward: per offspring produced", "-", "HT")] public const float R_BIRTH = 2.0f;
    [K("Brain", "reward: per unit damage / Emax (negative)", "-", "HT")] public const float R_PAIN = 4.0f;
    [K("Brain", "reward: per unit fear rise (negative)", "-", "HT")] public const float R_FEAR = 0.5f;
    [K("Brain", "mutation probability per brain or reflex byte (1/LEN: about one mutation per offspring)", "-", "ECO: on the order of one mutation per genome per generation in functional sequence (Drake 1998; Lynch 2010); was 0.02 (~39 per offspring, mutational meltdown)")] public const float MUT_RATE = 1f / G.LEN;
    [K("Brain", "mutation probability per body gene per birth (about 0.4 body changes per newborn)", "-", "ECO: each body gene stands for a polygenic trait of many loci; mutational heritability ~1e-3 per trait per generation (Lynch & Walsh 1998), so a trait changes far more often than one brain byte")] public const float MUT_BODY = 0.01f;
    [K("Brain", "standing variation of founders' body genes around their ancestor (SD)", "int8", "ECO: morphological traits vary with a CV of about 5-10% in natural populations (Houle 1992)")] public const float FOUNDER_BODY_SD = 12f;
    [K("Brain", "small mutation sigma", "int8", "HT")] public const float MUT_SMALL = 2f;
    [K("Brain", "share of mutations that are large jumps", "-", "V1")] public const float MUT_BIG_SHARE = 0.05f;
    [K("Brain", "large mutation sigma", "int8", "V1")] public const float MUT_BIG = 16f;
    [K("Brain", "reflex input weight scale (int8 / 16, as the output layer)", "-", "-")] public const float RSCALE = 1f / 16f;
    [K("Brain", "appetite is full while the reserve is more than this share below full (hunger above it), and fades to none as it tops up", "xEmax", "ECO: voluntary intake is limited by gut fill and energy status, not by energy deficit alone (Forbes 2007); measured 2026-10-30: adults on food ate on 47% of steps and sat at 20-60% reserve, so females were ready to breed 12% of the time")] public const float SATIETY = 0.2f;
    [K("Brain", "input size (|input|^2 of memory units and senses) above which a brain's learning step is scaled down by NLMS_REF / |input|^2", "-", "ML: normalised LMS (Widrow & Hoff; Haykin 2002); measured 2026-10-30: ~17-25 for brains under 48 units in a living world, ~117 at 256 units")] public const float NLMS_REF = 25f;
    [K("Brain", "lowest spectral radius (feedback strength) of a brain's recurrent memory, set by the wiring-gain gene", "-", "ECO/ML: a reservoir holds the most memory near 1 (Jaeger 2001; Lukosevicius & Jaeger 2009); below it memories fade fast")] public const float WIRE_LO = 0.5f;
    [K("Brain", "highest spectral radius of a brain's recurrent memory", "-", "as WIRE_LO: above ~1.2 activity turns chaotic and scrambles what it holds")] public const float WIRE_HI = 1.5f;
    [K("Brain", "founder strength of each starter-kit reflex (x/16): at full need, 120/16 = 7.5 against the calm bias of -4 gives about 97%", "int8", "designed: the innate starter kit (DESIGN §7.7, Revision D); the user (2026-10-30): the base instincts should be very strong, \"like primary colours\" (was 80, 88% against a bias of -3)")] public const int KIT_W = 120;
    [K("Brain", "elite archive size per species", "genomes", "HT (Revision M: 64 before; a genome is now ~83 KB)")] public const int ARCHIVE = 16;
    [K("Brain", "archive score weight per offspring that reached maturity", "-", "HT")] public const float SCORE_MATURED = 3f;
    [K("Prehistory", "length of a prehistory run", "steps", "HT")] public const int PREHISTORY_STEPS = 500000;
    [K("Prehistory", "number of separate branches (populations on the same land) evolving side by side", "branches", "HT (Revision M: 3 before; memory)")] public const int BRANCHES = 2;
    [K("Prehistory", "symbolic time shown in the console: years per step", "years", "-")] public const int YEARS_PER_STEP = 1000;
    [K("Prehistory", "agent slots a branch starts with (it grows as needed, World.Grow)", "slots", "HT: Revision M; not a cap since 2026-10-31 (the report warns if a branch reaches its memory limit)")] public const int BRANCH_CAPACITY = 16384;
    [K("Brain", "founders start in groups of this size at one site", "agents", "ECO: populations are clustered; lone colonists cannot find mates (Allee effect)")] public const int GROUP_SIZE = 8;
    [K("Brain", "radius of a founder group", "units", "HT")] public const float GROUP_SPREAD = 10f;
    [K("Brain", "largest radius of a founding lineage's home range, as a share of the map side (a big body's range widens up to this until it can feed FOUND_MIN founders)", "-", "HT: a third of the map; home ranges grow about in proportion to body mass (Kelt & Van Vuren 2001)")] public const float HOME_R_MAX = 0.33f;
    [K("Brain", "radius of an ancestor's home range, as a share of the map side", "-", "Revision N (the user): each lineage starts in its own part of the map; species have homelands and meet as they spread")] public const float HOME_R = 0.12f;
    [K("Brain", "random sites tried when choosing a home range (the one farthest from the others is taken)", "-", "HT")] public const int HOME_TRIES = 24;
    [K("Brain", "genomes per species stored in a gene bank (living sample)", "genomes", "HT (Revision M: 96 before)")] public const int BANK_LIVING = 48;

    // ---------------------------------------------------------------- 5b. evolvable bodies (DESIGN §7.5)
    [K("Body", "range of the speed and sight multipliers (x/÷)", "-", "HT")] public const float BODY_RANGE_TRAIT = 1.6f;
    [K("Body", "basal cost per unit of extra speed", "-", "HT")] public const float COST_SPEED = 0.4f;
    [K("Body", "basal cost per unit of extra sight", "-", "HT")] public const float COST_SIGHT = 0.15f;
    [K("Body", "speed loss per unit of armour in water and air (on land armour is part of the load the legs carry, LOAD_DRAG)", "-", "HT")] public const float ARMOUR_DRAG = 0.2f;

    // ---------------------------------------------------------------- 5c. bodies from genes (DESIGN §6.3, Revision F)
    [K("Body", "smallest body mass a mass gene can give (the floor of the smallest size class)", "M", "ECO: Revision M: 0.01 gave tiny bodies 32x the numbers from the same food; the user (2026-10-30) asked for size classes with their own floors")] public const float MASS_LO = 0.1f;
    [K("Body", "number of body-plan size classes; class k cannot be smaller than Body.Floors[k] (0.1, 1, 3, 10)", "-", "designed: the user (2026-10-30): classes with different floors, inherited, rarely changing; size evolves freely above the floor")] public const int FLOOR_CLASSES = 4;
    [K("Body", "chance per birth that the size class steps up or down one class", "-", "designed: the user: inherited, rarely changes (a body-plan change is rare; ~1 in 20,000 births)")] public const float FLOOR_MUT = 5e-5f;
    [K("Body", "largest body mass a mass gene can give", "M", "ECO: a large mammal-sized organism (relative units)")] public const float MASS_HI = 40f;
    [K("Body", "organ size (0..1) below which an organ is vestigial (for describing and drawing bodies; speeds are smooth)", "-", "-")] public const float LIMB_MIN = 0.3f;
    [K("Body", "basal cost per unit of fin size per pair of fins (legs pay instead while they walk: LEG_SWING)", "xbasal", "ECO: locomotor muscle is maintained at a cost")] public const float COST_LIMB = 0.15f;
    [K("Body", "basal cost per unit of wing size (upkeep of wings and flight muscle; the power of flying is paid while airborne, FLIGHT_MULT)", "xbasal", "ECO: flight muscle is 15-25% of a bird's mass (Greenewalt 1962; Marden 1987), maintained like any muscle; until 2026-10-31 this was 0.5 x mass^(1/3), standing in for the flight power now paid per step")] public const float COST_WING = 0.15f;
    [K("Body", "basal cost per unit of armour", "xbasal", "ECO: heavy armour is costly to carry and grow")] public const float COST_ARMOUR_ABS = 0.12f;
    [K("Body", "basal cost per unit of weaponry", "xbasal", "ECO: jaws, teeth and claws are costly tissue")] public const float COST_STRIKE_ABS = 0.15f;
    [K("Body", "largest armour a gene can give", "-", "-")] public const float ARMOUR_MAX = 3f;
    [K("Body", "largest weaponry (strike) a gene can give", "-", "-")] public const float STRIKE_MAX = 2f;
    [K("Body", "fermentation time scale of fibre, in units of M^0.25 retention: the share fermented is 1 - exp(-M^0.25 / this), relative to M = 1", "-", "ECO: Jarman-Bell principle (Demment & Van Soest 1985; Clauss et al. 2013): fibre digestibility rises from ~25% in small to ~60% in large herbivores; 2 gives x0.63 at M = 0.1, x1.5 at 10, x1.8 at 40")] public const float TAU_FERM = 2f;
    [K("Body", "length (L2 norm) of the vector of digestive efficiencies: a generalist spreads it, a specialist concentrates it", "-", "ECO: digestive specialisation trades off breadth against efficiency")] public const float DIET_BUDGET = 1.0f;
    [K("Body", "largest litter a gene can give", "young", "-")] public const int LITTER_MAX = 12;
    [K("Body", "smallest newborn (or egg) mass", "M", "ECO: eggs and young have a physical smallest size (Revision M)")] public const float NEWBORN_MIN = 0.01f;
    [K("Body", "smallest newborn mass as a share of adult mass", "-", "ECO: fish and insect eggs")] public const float BIRTH_LO = 0.01f;
    [K("Body", "largest newborn mass as a share of adult mass", "-", "ECO: precocial mammals")] public const float BIRTH_HI = 0.3f;
    [K("Body", "narrowest field of view a gene can give", "degrees", "ECO: forward-facing predators")] public const float FOV_LO = 120f;
    [K("Body", "widest field of view a gene can give", "degrees", "ECO: prey with lateral eyes")] public const float FOV_HI = 360f;
    [K("Body", "gestation (or incubation) at M=1, scaling with M^0.25", "steps", "ECO: gestation scales about as M^0.25")] public const float C_GEST = 150f;
    [K("Body", "breeding cooldown at M=1, scaling with M^0.25", "steps", "ECO: as gestation")] public const float C_COOL = 150f;

    [K("Body", "swimming power (share of the body's top speed in water) needed to stay in deep water", "-", "ECO: staying up in open water needs swimming power")] public const float DEEP_SWIM = 0.3f;
    [K("Body", "additional maximum water depth per unit fin size, in SHALLOW-depth units", "x", "HT")] public const float SWIM_DEPTH_RANGE = 14f;
    [K("Body", "a legless body crawls on land at this share of its top speed", "-", "ECO: worms, snakes and stranded fish move slowly without legs")] public const float CRAWL = 0.05f;
    [K("Body", "energy of one call, as a share of basal metabolism at the caller's mass", "xbasal", "ECO: vocalising costs a few times resting metabolism for its duration (Ophir et al. 2010); a call is a short burst")] public const float C_CALL = 0.5f;
    [K("Body", "a voice organ below this makes no sound", "-", "HT: a little voice is no voice; above founder variation around a mute ancestor")] public const float VOICE_MIN = 0.25f;
    [K("Body", "basal cost of a full voice (larynx and its control)", "xbasal", "ECO: small; vocal tissue and its neural control are a minor part of upkeep")] public const float COST_VOICE = 0.03f;
    [K("Voice", "syllables in one utterance at most (distinct sounds, in the order of how strongly each is intended)", "sounds", "ECO: animal calls combine a few units into sequences, rarely more (Engesser & Townsend 2019)")] public const int MAX_SYL = 3;
    [K("Voice", "share of a call's cost that grows with each syllable: an utterance of n syllables costs C_CALL x basal x (1 - CALL_SYL + CALL_SYL x n)", "-", "ECO: a longer song costs more, but much of a call's cost is the start-up (Oberweger & Goller 2001; Ophir et al. 2010)")] public const float CALL_SYL = 0.5f;
    [K("Voice", "how far a full voice of a body of M=1 carries (range = HEAR_R x voice x M^(1/3), at most MAX_SIGHT)", "units", "ECO: bigger animals call louder and are heard further (Fletcher 2004; Wiley & Richards 1978); about the sight range at M=1 (C_SIGHT)")] public const float HEAR_R = 16f;
    [K("Voice", "a listener keeps attending to the same caller while it calls at least this share as loud as the loudest alike caller", "-", "ECO/HT: listeners stream one source among many (Bee & Micheyl 2008); hysteresis keeps attention from flickering")] public const float FOCAL_KEEP = 0.7f;
    [K("Voice", "steps over which the counts of a remembered tutor's syllable pairs halve", "steps", "ECO/HT: a template fades without new tutoring; about a third of a year (Doupe & Kuhl 1999; Tchernichovski et al. 2001); the young copy form from elders, so a language changes by iterated learning (Kirby, Cornish & Smith 2008)")] public const int TMPL_HALF = 1000;
    [K("Voice", "rate of the running mean of an organism's own match to its template", "/step of calling", "HT: a baseline, as RBAR_RATE is for reward")] public const float IMIT_RBAR = 0.05f;
    [K("Voice", "shortest sensitive period for memorising tutors, as a share of lifespan (the IMIT_AGE gene spans IMIT_AGE_LO..1)", "-", "ECO: songbirds memorise early in life, some species throughout (Brainard & Doupe 2002)")] public const float IMIT_AGE_LO = 0.1f;
    [K("Food", "share of its meat-digesting skill a meat-eater brings to carrion", "-", "ECO: almost all carnivores scavenge; wolves feed on a kill for days (DeVault et al. 2003)")] public const float SCAVENGE = 0.8f;
    [K("Land", "a pond site is the lowest cell within this many cells", "cells", "ECO: about one basin per hundred square cells on rolling ground (prairie potholes, desert playas)")] public const int POND_R = 2;
    [K("Land", "pond sites are at least this far from a lake, sea or river", "cells", "HT: a pond beside a lake adds nothing")] public const float POND_GAP = 6f;
    [K("Body", "share of thirst a land organism avoids on wet soil (dew, damp ground); water in food is counted separately (K_FOODWATER)", "-", "ECO: 0.8 before Revision K counted moist food too (ecology review: no double counting)")] public const float SOIL_WATER = 0.5f;
    [K("Body", "thirst relieved per unit of food eaten, per unit of water in it, per unit body mass", "/M", "ECO: continuous eating of fully green forage about balances drying on dry ground (C_THIRST / (digestion x 0.75)); preformed water in forage and prey (ecology review)")] public const float K_FOODWATER = 0.35f;
    [K("Body", "metabolic water: water made per unit of food energy digested, as mass of tissue burned", "-", "ECO: about 0.5 g of water per g of fat oxidised; how desert seed-eaters live without drinking")] public const float MET_WATER = 0.5f;
    [K("Body", "largest cut in water loss from the water-economy gene", "-", "ECO: kangaroo rats turn over about a quarter of the water of other rodents (Schmidt-Nielsen)")] public const float WATER_ECON = 0.7f;
    [K("Body", "basal cost of full water economy (kidney and nasal water recovery)", "xbasal", "ECO: kidneys use a few to 10% of basal metabolism")] public const float COST_WATERECON = 0.09f;
    [K("Body", "mass exponent of thirst (water turnover per unit mass)", "-", "ECO: mass-specific water turnover scales about as M^-0.2")] public const float EXP_THIRST = -0.2f;
    [K("Body", "basal cost per eye pair", "xbasal", "ECO: eyes and visual brain are costly tissue")] public const float COST_EYE = 0.04f;
    [K("Body", "field of view added by each extra eye pair (share of the base field)", "-", "ECO: more eyes cover more directions")] public const float EYE_FOV = 0.3f;
    [K("Body", "turning added per extra fin pair (times fin size)", "-", "ECO: paired fins steer and brake")] public const float FIN_TURN = 0.2f;
    [K("Body", "range of the lifespan and development multipliers (x/÷)", "-", "-")] public const float LIFE_RANGE = 2f;
    [K("Body", "basal cost per unit of extra lifespan (longer life needs more body repair)", "xbasal", "ECO: disposable-soma theory: longevity costs somatic maintenance (Kirkwood 1977)")] public const float COST_LONGEVITY = 0.25f;
    [K("Body", "lifespan scales with the development multiplier to this power (fast developers age sooner)", "-", "ECO: the fast-slow life-history continuum")] public const float DEVEL_AGING = 0.5f;
    [K("Body", "ordinary ancestors start with a random brain of up to this many memory units", "units", "the old limit before Revision J was 12; Revision M: 24")] public const int BRAIN_START = 24;
    [K("Body", "fewest memory units a brain can have", "units", "-")] public const int BRAIN_MIN = 2;
    [K("Body", "basal cost of a brain of BRAIN_REF memory units, scaled by units in use", "xbasal", "ECO: brain tissue costs about 2-20% of basal metabolism across vertebrates (humans ~20%); the largest possible brain costs a little more (Revision M)")] public const float COST_BRAIN = 0.25f;
    [K("Body", "reference brain size for the brain cost: the largest brain (G.NH)", "units", "Revision M: was 12, the old limit")] public const int BRAIN_REF = 256;
    [K("Brain", "big-brained ancestors per branch (the first on land, then alternating water and land), by default", "-", "designed: the user (Revision M): ONE lineage far smarter than the rest, like a human species; the big-brained land ancestor is also the voiced one")] public const int BIG_BRAINS = 1;
    [K("Brain", "memory units of a big-brained ancestor, by default", "units", "designed: the user (Revision M): the largest brain, ten times an ordinary ancestor's")] public const int BIG_BRAIN_UNITS = 256;
    [K("Brain", "strength of the mating instinct in females, against males (1)", "-", "designed by the user (2026-10-28): males are the more eager sex (Bateman 1948; Trivers 1972)")] public const float DRIVE_FEMALE = 0.6f;
    [K("Brain", "strength of the mating instinct in hermaphrodites, against males (1)", "-", "designed: between the two sexes")] public const float DRIVE_HERM = 0.8f;
    [K("Body", "basal cost of being a hermaphrodite (keeping both sexes' organs)", "xbasal", "ECO: hermaphrodites maintain male and female function")] public const float COST_HERM = 0.05f;
    [K("Body", "basal cost of full immune investment", "xbasal", "ECO: immune defence is costly (Lochmiller & Deerenberg 2000)")] public const float COST_IMMUNE = 0.12f;
    [K("Body", "share of infection and disease death that full immune investment prevents", "-", "ECO: partial, not total, protection")] public const float IMMUNE_PROTECT = 0.8f;
    [K("Body", "genetic distance at which mating succeeds with probability 1/e: p = exp(-(d/D)^4) (body genes / 127, RMS per gene)", "-", "ECO: Dobzhansky-Muller incompatibilities snowball with the square of the substitutions between lineages (Orr 1995); d^2 grows with substitutions: within a population (d ~0.13) p ~0.9, between independent ancestors (d ~0.4) p < 0.001")] public const float D_COMPAT = 0.25f;
    [K("Body", "others whose visible body genes are this close look like kin (body genes / 127, RMS per gene)", "-", "ECO: animals recognise their kind by appearance")] public const float KIN_LOOK = 0.25f;

    // ---------------------------------------------------------------- 5e. locomotion physics (2026-10-31)
    // Mass units: this world's masses are not one consistent scale (a lifespan of ~1 year at M = 1 is a mouse's, a
    // bare M = 1 body's lower critical temperature of 22 °C and MASS_HI = 40 as "a large mammal" are a few kg's). For
    // flight, M = 1 is taken as about 1 kg: then MASS_LO (0.1) is a 100 g bird and MASS_HI (40) a 40 kg mammal.
    [K("Locomotion physics", "metabolic rate of flight at its cheapest speed (the minimum of the power curve) for a body of FLY_M_REF, as a multiple of basal metabolism", "xbasal", "ECO: flying birds and bats metabolise at 10-15x their resting rate, against 2-4x for animals walking (Tucker 1970; Schmidt-Nielsen 1972; Norberg 1990; Masman & Klaassen 1987); flight is cheap per km only because it is fast")] public const float FLIGHT_MULT = 10f;
    [K("Locomotion physics", "mass at which the least power of flight is FLIGHT_MULT x basal", "M", "assumption: M = 1 is about 1 kg (see above); the middle of the range of flying birds (10 g - 15 kg) on a log scale")] public const float FLY_M_REF = 1f;
    [K("Locomotion physics", "mass exponent of the least power flight requires", "-", "ECO: for geometrically similar fliers, P_min ~ M^(7/6) (Pennycuick 1968, 2008; Ellington 1991); against basal M^0.75 a bigger flier pays more per unit basal (x3.8 basal at M = 0.1, x31 at M = 16)")] public const float EXP_FLY_REQ = 7f / 6f;
    [K("Locomotion physics", "mass exponent of the power a body's flight muscles can deliver", "-", "ECO: flight muscle is a fixed share of mass, and its mass-specific power falls with wingbeat frequency, so power available ~ M^0.7 (Pennycuick 1968; Ellington 1991; Marden 1994)")] public const float EXP_FLY_AVAIL = 0.7f;
    [K("Locomotion physics", "heaviest body that can fly, with the largest wings (wing gene 1): a body flies while wings x (FLY_M_MAX / M)^(EXP_FLY_REQ - EXP_FLY_AVAIL) >= 1, power available over the least power required", "M", "ECO: power available ~M^0.7 against power required ~M^(7/6) caps flapping flight at ~12-19 kg (Pennycuick 1968, 2008; Ellington 1991): the heaviest living fliers, bustards and swans, weigh 12-18 kg. At wing gene 0.5 the ceiling is 16 x 0.5^2.14 = 3.6")] public const float FLY_M_MAX = 16f;
    [K("Locomotion physics", "smallest wing gene that can take off at any size (the flight muscle needed to lift the body's own weight)", "-", "ECO: takeoff needs flight muscle of at least ~16% of body mass, nearly whatever the size, in birds, bats and insects (Marden 1987); the largest flight-muscle ratios are ~35% (hummingbirds, pigeons; Greenewalt 1962; Hartman 1961), so wing gene 1 is taken as ~35% and 16/35 = 0.45. Without it the power margin alone let a 0.1 body fly on a wing gene of 0.09")] public const float FLY_WING_MIN = 0.45f;
    [K("Locomotion physics", "speed of least power (a flier's cruise) as a multiple of the walking-speed allometry C_SPEED M^EXP_SPEED x speed gene", "-", "ECO: birds cruise at ~5-15 m/s (Pennycuick 2008: V_mp ~ M^(1/6), as walking speed here, EXP_SPEED) while walkers travel at ~1-2 m/s (Alexander 2003), so a flier cruises several times faster than it walks; 4 keeps the fastest fliers under FLY_V_CAP (was FLY_SPEED = 1.4 for top speed, i.e. ~0.9 for cruise)")] public const float FLY_VMP = 4f;
    [K("Locomotion physics", "fastest an animal moves in one step through the air, in world units", "units/step", "world cap: an animal senses others only within MAX_SIGHT = 2 spatial-hash cells (HCELL) and decides once per step; moving more than one hash cell per step would overshoot what it sensed. Real fliers would go faster still: the cost per distance (FLY_COT_RATIO) is calibrated instead of the speed")] public const float FLY_V_CAP = 16f;
    [K("Locomotion physics", "cost per distance of flying at the speed of least power, against walking's at the same mass (M = FLY_M_REF, full legs, two pairs)", "-", "ECO: flight's cost of transport is 1/3-1/2 of running's at equal mass (Tucker 1970; Schmidt-Nielsen 1972). It sets how long in a step a flier is really in the air: it flies at FLIGHT_MULT x basal for the share of the step its distance needs (Body.FlightFullStep), but the whole step when it cannot land")] public const float FLY_COT_RATIO = 0.4f;
    [K("Locomotion physics", "fastest level flight as a multiple of the speed of least power, when power allows (and at most FLY_V_CAP) (power there is (3/x + x^3)/4 = 1.49 x least)", "-", "ECO: birds fly up to ~1.5-2x their minimum-power speed; the maximum-range speed is ~1.3-1.6x (Pennycuick 2008; Tobalske et al. 2003). A flier with less margin flies at most where power required meets power available")] public const float FLY_X_MAX = 1.6f;
    [K("Locomotion physics", "slowest speed counted on the power curve, as a share of the speed of least power: hovering costs (3/u + u^3)/4 = 2x the least power", "-", "ECO: the power curve is U-shaped (induced power ~1/v, profile and parasite power ~v^3; Pennycuick 1968; Tucker 1973); hovering costs about twice the minimum (Norberg 1990; Ellington 1984)")] public const float FLY_U_MIN = 0.38f;
    [K("Locomotion physics", "share of the cost of walking that swinging the legs takes, for LEG_PAIRS_REF pairs of full legs at top speed; the rest (stance) every walker pays", "-", "ECO: swing-phase muscles use ~26% of limb-muscle energy in walking and running guinea fowl (Marsh et al. 2004; Modica & Kram 2005: ~20% in running humans); swing work per distance rises with speed (stride frequency)")] public const float LEG_SWING = 0.26f;
    [K("Locomotion physics", "leg pairs of the body whose cost of transport the walking cost (C_MOVE, EXP_MOVE) describes", "pairs", "ECO: the cost-of-transport line is fitted to quadrupeds and bipeds (Taylor, Heglund & Maloiy 1982); hexapods and octopods lie near it (Full & Tu 1991), so each pair beyond this adds its swing")] public const float LEG_PAIRS_REF = 2f;
    [K("Locomotion physics", "share of the slowing by rough ground that four pairs of full legs prevent (one pair: none; linear in pairs - 1 and leg size)", "-", "ECO: many legs keep a small, slow body statically stable on uneven ground (an alternating tripod; Ting, Blickhan & Full 1994); cockroaches cross rough terrain at near full speed (Sponberg & Full 2008)")] public const float LEG_STAB = 0.6f;
    [K("Locomotion physics", "rise of the ground to a neighbouring cell at which ground counts as fully rough", "elev/cell", "HT: about the 90th percentile of land cells on a 256 map (0.20) and the 99th on a 1024 map (0.18)")] public const float ROUGH_REF = 0.15f;
    [K("Locomotion physics", "walking speed lost on fully rough ground by an M = 1 body without stabilising legs: v / (1 + ROUGH_SLOW x rough x M^(-1/3) x (1 - stability))", "-", "ECO/HT: obstacles are bigger against a small body's legs (leg length ~ M^(1/3)), so roughness slows the small most (Kaspari & Weiser 1999: the size-grain hypothesis)")] public const float ROUGH_SLOW = 0.5f;
    [K("Locomotion physics", "walking speed lost per unit of bone stress above an unarmoured two-pair body's: drag = LOAD_DRAG x M^(1/3) x ((1 + armour) / sqrt(leg pairs) - 1/sqrt(2))", "-", "ECO: weight grows as M, bone section as M^(2/3), so bone stress ~ M^(1/3) and heavier bodies must move more carefully (Biewener 1989, 1990); more legs share it. At M = 1 the armour term equals the old ARMOUR_DRAG / sqrt(pairs)")] public const float LOAD_DRAG = 0.2f;

    // ---------------------------------------------------------------- 5d. clades and speciation (DESIGN §7.9)
    [K("Clades", "clade slots (lineages that can exist at once)", "clades", "-: limited by the console frame (5 bits)")] public const int CLADES = 32;
    [K("Clades", "ancestors that start on land", "ancestors", "user choice")] public const int ANCESTORS_LAND = 10;
    [K("Clades", "ancestors that start in water", "ancestors", "user choice")] public const int ANCESTORS_WATER = 6;
    [K("Clades", "founding organisms per ancestor", "organisms", "HT")] public const int ANCESTOR_COUNT = 600;
    [K("Clades", "a lineage starts with this share of the animals its home range's plants can feed at its body size and brain", "-", "ECO: founding at a quarter of the carrying capacity leaves room for the founders' first wave of young (2026-10-30: a fixed 600 per ancestor started big bodies at many times their food, and 70% of founders starved; at a half, the first wave doubled the population by step 300 and it crashed by 90%; at a quarter, three worlds kept 3-4 lineages and 190-1,150 animals at step 8,000 against 2-4 and 18-630)")] public const float FOUND_FILL = 0.25f;
    [K("Clades", "a day of living costs this many times the basal cost (moving, growing, breeding, keeping warm)", "xbasal", "measured: this world's own animals spend 1.45x basal over the first 3000 steps (2026-10-30, seed 7: basal 0.92, move 0.12, growth 0.16, breeding 0.15, heat 0.10); free-living mammals 2-3x (Nagy 1987)")] public const float FIELD_METAB = 1.5f;
    [K("Clades", "an ancestor must be able to eat this many days' need per step of eating (one bite of a half-grazed cell, or its gut's rate)", "-", "measured: random founders eat on 25-50% of steps (2026-10-30), so a forager must gain at least 2-4x its need per bite to live")] public const float FORAGE_MARGIN = 3f;
    [K("Clades", "the largest share of an ancestor's basal cost its brain may take", "-", "ECO: brain tissue takes at most ~20-25% of basal metabolism (humans ~20%; Aiello & Wheeler 1995; Mink et al. 1981); 2026-10-30: the big-brained ancestor drew a 0.1-0.4 body whose 256-unit brain cost more than the body, and died in 9 worlds of 9")] public const float BRAIN_SHARE_MAX = 0.25f;
    [K("Clades", "random ancestor bodies tried per ancestor for one its home range can feed", "-", "HT: enough that almost every ancestor finds a viable body")] public const int ANCESTOR_TRIES = 64;
    [K("Clades", "the smallest founding group an ancestor must be able to feed (FOUND_FILL x capacity)", "organisms", "ECO: the 50 of the 50/500 rule for a minimum viable population (Franklin 1980); smaller founding groups go extinct by chance (Allee effect)")] public const int FOUND_MIN = 50;
    [K("Clades", "steps between speciation checks", "steps", "-")] public const int SPEC_EVERY = 2000;
    [K("Clades", "smallest cluster that can become a species", "organisms", "ECO: a viable population")] public const int MIN_SPLIT = 40;
    [K("Clades", "two forms are a new clade only if some visible trait differs by this many visible units (1 = half again the mass, 0.15 of an organ, or a pair of limbs or eyes)", "-", "designed: the user asked for subspecies that are clearly visual (Revision L); fertility then says subspecies or species")] public const float SPLIT_VIS = 1f;
    [K("Clades", "an extinct clade's slot is reused only after this many steps", "steps", "-")] public const int CLADE_RECYCLE = 20000;

    // ---------------------------------------------------------------- 6. reproduction
    [K("Reward", "energy spent on its choices (strikes, calls, moving, heat) counts against the reward at this share of the weight energy eaten counts for it", "-", "ECO: animals learn by net energy gain (optimal foraging; Stephens & Krebs 1986); 1 = a unit spent weighs as a unit eaten. 2026-10-30: without it, calling (19-24% of basal) rose from ~38% to ~65% of steps by learning")] public const float R_SPEND = 1f;
    [K("Reproduction", "lowest breeding condition a body can have: it breeds once its reserve is above this share (B_BREEDE gene)", "xEmax", "ECO: income breeders breed on what they eat as they go, capital breeders on stored reserves (Jonsson 1997; Stephens et al. 2009); was the fixed READY_ENERGY = 0.6 until 2026-10-30")] public const float READY_LO = 0.25f;
    [K("Reproduction", "highest breeding condition a body can have", "xEmax", "ECO: as READY_LO")] public const float READY_HI = 0.9f;
    [K("Reproduction", "newborn energy (paid by parent at conception)", "xEmax child", "V1")] public const float F_BIRTH = 0.40f;
    [K("Reproduction", "mating cost per partner", "E/M", "HT")] public const float C_MATE = 0.02f;
    [K("Reproduction", "a male's refractory period after mating, as a share of the breeding cooldown", "-", "ECO: male refractory periods are short against female breeding cycles (Bateman 1948; Trivers 1972)")] public const float REFRACT = 0.1f;

    // ---------------------------------------------------------------- 6b. parental care (DESIGN §7.8)
    [K("Care", "milk or chick-feeding energy a mother can give per step, as a multiple of her basal cost (shared by all her young)", "xbasal", "ECO: peak lactation milk energy is about 1-2x maternal basal metabolic rate (Hanwell & Peaker 1977)")] public const float CARE_RATE = 1.5f;
    [K("Care", "efficiency of turning maternal energy into milk or food for the young", "-", "ECO: milk synthesis efficiency 0.6-0.8")] public const float CARE_EFF = 0.7f;
    [K("Care", "care ends (weaning) when the young has built this share of its adult tissue", "-", "ECO: mammals wean at roughly a third to a half of adult mass")] public const float WEAN = 0.4f;
    [K("Care", "a mother keeps at least this share of her energy reserve for herself", "xEmax", "ECO: females in poor condition reduce or stop provisioning")] public const float CARE_RESERVE = 0.4f;

    // ---------------------------------------------------------------- 6c. pathogens (Revision R, Pathogens.cs; replaces the disease of DESIGN §9.6)
    // One step is about 5 hours (an old 50-step infection was 1-2 weeks of a 2-year life). A pathogen's load is in units
    // of what one host can hold (1). Its genome is bytes 0..255; each trait's range below is what the byte spans.
    [K("Pathogens", "within-host growth rate at gene 0 (log scale to PG_REP_HI)", "/step", "ECO: in vivo doubling times run from hours (Salmonella in mouse tissues, Grant et al. 2008) to about a day (M. tuberculosis): ln2/0.03 = 23 steps (~5 days)")] public const float PG_REP_LO = 0.03f;
    [K("Pathogens", "within-host growth rate at gene 255", "/step", "ECO: ln2/0.5 = 1.4 steps (~7 h), a fast bacterial pathogen in a host")] public const float PG_REP_HI = 0.5f;
    [K("Pathogens", "quorum-sensing threshold at gene 0 (log scale to PG_QS_HI), in load", "load", "ECO: QS switches at a cell density set by the signal's receptor affinity, which mutates readily (Miller & Bassler 2001)")] public const float PG_QS_LO = 0.01f;
    [K("Pathogens", "quorum-sensing threshold at gene 255, in load (above 1 a host never holds a quorum: always stealthy)", "load", "ECO: QS-blind (lasR) mutants are common in chronic infections (Smith et al. 2006)")] public const float PG_QS_HI = 3f;
    [K("Pathogens", "steepness (Hill coefficient) of the quorum switch: on = L^n / (L^n + threshold^n)", "-", "ECO: autoinduction makes QS a switch-like positive feedback (Miller & Bassler 2001); LuxR-type responses have Hill coefficients ~2-4")] public const float PG_HILL = 4f;
    [K("Pathogens", "share of toxin and shedding expressed below quorum (stealth mode)", "-", "ECO: QS controls most secreted virulence factors; QS-off cells make a small fraction (Pseudomonas lasR mutants, Rumbaugh et al. 2009)")] public const float PG_STEALTH = 0.1f;
    [K("Pathogens", "immune detection below quorum, relative to a quorate population", "-", "ECO: QS-regulated exoproducts are what the immune system mostly sees; innate receptors still see the cells (PAMPs, Janeway 1989)")] public const float PG_DETECT = 0.3f;
    [K("Pathogens", "decay rate in the environment at persistence 0 (log scale to PG_DECAY_LO)", "/step", "ECO: enveloped viruses and vegetative bacteria die within hours outside a host (half-life ~2 steps, ~10 h)")] public const float PG_DECAY_HI = 0.3f;
    [K("Pathogens", "decay rate in the environment at persistence 255", "/step", "ECO: spores and helminth eggs survive years (half-life ~3,500 steps); anthrax spores decades (Walther & Ewald 2004)")] public const float PG_DECAY_LO = 2e-4f;
    [K("Pathogens", "extra decay on dry land (not water, pond or flood) over water", "x", "ECO: desiccation and UV kill enteric bacteria faster on dry surfaces than in water")] public const float PG_DRY = 3f;
    [K("Pathogens", "within-host growth lost at full persistence (spore coats and capsids cost replication)", "-", "ECO: survival-reproduction trade-off among phages (De Paepe & Taddei 2006); sporulation halts growth")] public const float PG_PERS_COST = 0.5f;
    [K("Pathogens", "host-match width gained at full host breadth (x(1 + this) of MHC_W)", "-", "ECO: multi-host pathogens are common (Woolhouse, Taylor & Haydon 2001)")] public const float PG_BREADTH = 2f;
    [K("Pathogens", "within-host growth lost at full host breadth (generalist cost)", "-", "ECO: a jack of all hosts is master of none (Leggett et al. 2013); specialists grow faster in their own host")] public const float PG_GEN_COST = 0.5f;
    [K("Pathogens", "match width: a strain grows in a host whose recognition genes are within this RMS distance (/127) of its antigen", "-", "ECO: matching-alleles host-parasite genetics (Hamilton 1980; Lively 2010): rare host genotypes escape, which can favour sex")] public const float MHC_W = 0.35f;
    [K("Pathogens", "mutation of a strain's antigen per transmission (SD)", "int8", "ECO: pathogens evolve far faster than their hosts; antigenic drift (Smith et al. 2004)")] public const float MHC_MUT = 6f;
    [K("Pathogens", "mutation of each trait gene per transmission (SD)", "of 255", "ECO: pathogens evolve far faster than their hosts; virulence evolves within decades (myxoma, Fenner 1983)")] public const float PG_MUT = 4f;
    [K("Pathogens", "load at infection (the founding dose)", "load", "ECO: infections start from a small bottleneck of founders (Abel et al. 2015)")] public const float PG_INOC = 0.01f;
    [K("Pathogens", "load below which an infection is cleared", "load", "HT: a fifth of the founding dose")] public const float PG_CLEAR = 0.002f;
    [K("Pathogens", "killing rate per unit immune effector at full detection", "/step", "ECO: with PG_RISE an adaptive response clears a fast strain in ~1-2 weeks (30-70 steps); a strain growing too slowly to stimulate expansion above PG_E_DECAY persists (chronic carriage)")] public const float PG_KILL = 0.6f;
    [K("Pathogens", "most a specific immune response multiplies per step (clonal expansion, x stimulation x detection x (PG_NAIVE + effector))", "/step", "ECO: within-host dynamics dX/dt = rho X P/(P + phi) (Antia, Levin & May 1994); activated T cells divide every 6-8 h (De Boer et al. 2001), ~0.4-0.6 per 5-h step")] public const float PG_RISE = 0.3f;
    [K("Pathogens", "naive precursors a specific response expands from (in effector units)", "-", "ECO: a few hundred naive precursors per antigen, against ~10^5-fold expansion at the peak (Blattman et al. 2002)")] public const float PG_NAIVE = 0.02f;
    [K("Pathogens", "load at which immune stimulation is half its maximum", "load", "ECO: small antigen doses start an adaptive response (Bachmann & Jennings 2010); here the founding dose PG_INOC, so a stealthy strain is seen less but not ignored")] public const float PG_HALF = 0.01f;
    [K("Pathogens", "decay of the immune effector per step (contraction)", "/step", "ECO: effector T cells contract within days to weeks after the peak")] public const float PG_E_DECAY = 0.02f;
    [K("Pathogens", "immune stimulation of a host with no immunity gene (innate); the gene adds up to 1", "-", "ECO: innate immunity alone controls many infections (Janeway & Medzhitov 2002)")] public const float PG_IMM_BASE = 0.5f;
    [K("Pathogens", "gain in immune stimulation at full MHC diversity (the spread of its recognition genes)", "-", "ECO: MHC heterozygote advantage: more distinct alleles present more antigens (Doherty & Zinkernagel 1975; Penn, Damjanovich & Potts 2002)")] public const float PG_MHC_DIV = 0.5f;
    [K("Pathogens", "cross-immunity width: protection by memory falls as exp(-d^2/w^2) of antigen RMS distance (/127)", "-", "ECO: titres fall with antigenic distance; drifted influenza escapes old immunity (Smith et al. 2004)")] public const float PG_CROSS_W = 0.25f;
    [K("Pathogens", "decay of immune memory per step", "/step", "ECO: antibody half-lives of 1-10+ years (Amanna, Carlson & Slifka 2007): ~2,300 steps here")] public const float PG_MEM_DECAY = 3e-4f;
    [K("Pathogens", "immune effector at once on reinfection, per unit cross-immunity (the secondary response)", "-", "ECO: memory responses are faster and larger (Ahmed & Gray 1996)")] public const float PG_RECALL = 1f;
    [K("Pathogens", "tissue damage per step per unit toxin x load (1 = death by disease)", "/step", "ECO: virulence is damage from pathogen load and toxins (Casadevall & Pirofski 1999); untreated cholera can kill within a day of the first symptoms (Sack et al. 2004): a full-toxin strain at full load kills in ~11 steps (~2 days)")] public const float PG_DMG = 0.1f;
    [K("Pathogens", "repair of tissue damage per step at a full energy reserve (x reserve share: a starving host cannot repair)", "/step", "ECO: malnutrition raises case fatality (Scrimshaw, Taylor & Gordon 1968)")] public const float PG_REPAIR = 0.01f;
    [K("Pathogens", "extra basal cost per unit load (what the pathogen takes)", "xbasal", "ECO: parasites take a share of host resources")] public const float PG_COST_LOAD = 0.2f;
    [K("Pathogens", "extra basal cost per unit immune effector (fever, immune response)", "xbasal", "ECO: an immune response raises resting metabolism 10-30%; fever ~10-13% per °C (Lochmiller & Deerenberg 2000)")] public const float PG_COST_IMM = 0.3f;
    [K("Pathogens", "malaise felt (as pain) per unit immune effector; tissue damage adds to it", "-", "ECO: sickness behaviour is driven by cytokines of the immune response (Hart 1988)")] public const float PG_MALAISE = 0.5f;
    [K("Pathogens", "shedding per step per unit load (all routes)", "/step", "HT")] public const float PG_SHED = 0.1f;
    [K("Pathogens", "extra shedding per unit toxin (x(1 + this x toxin))", "-", "ECO: toxins drive diarrhoea and coughing that carry pathogens out (cholera toxin; Ewald 1994): the virulence-transmission trade-off (Anderson & May 1982; Alizon et al. 2009)")] public const float PG_TOX_SHED = 2f;
    [K("Pathogens", "faeces concentrate the dung route's shedding (only while the host's gut passes waste)", "x", "ECO: enteric pathogens reach 10^6-10^9 per gram of faeces")] public const float PG_DUNG = 3f;
    [K("Pathogens", "infection hazard per unit dose (x match x (1 - cross-immunity) x immunity gene's protection); a dose of 1 is a whole host's burden", "/dose", "ECO: exponential dose-response (Haas 1983); infective doses are 10^1-10^6 organisms (Kothary & Babu 2001) against 10^9-10^12 in a sick host, so ID50 here (0.7% of a burden) is on the high, hard-to-catch side")] public const float PG_INF = 100f;
    [K("Pathogens", "share of a cell's reservoir picked up per step by an animal standing in it", "/step", "HT")] public const float PG_TOUCH = 0.02f;
    [K("Pathogens", "share of a cell's reservoir swallowed with a bite of food from it", "/bite", "HT: grazing and dung-eating are the faecal-oral route")] public const float PG_EAT = 0.2f;
    [K("Pathogens", "share of a water cell's reservoir swallowed with a drink", "/drink", "HT: a waterhole is where waterborne pathogens meet hosts (cholera, Ewald 1991)")] public const float PG_DRINK = 0.2f;
    [K("Pathogens", "share of a river cell's pathogens carried one cell downstream per step", "/step", "ECO: rivers run far faster than a cell (16 m) per step (5 h); the downstream-first update moves a cell's load one cell per step at most")] public const float PG_FLOW = 0.5f;
    [K("Pathogens", "share of its load a dead host leaves in the ground where it died", "-", "ECO: carcasses are reservoirs (anthrax, Turner et al. 2014)")] public const float PG_CARCASS = 1f;
    [K("Pathogens", "reservoir load below which a cell is clean", "load", "HT: picked up by touch it is a hazard of 2e-5 per step")] public const float PG_RES_MIN = 1e-5f;
    [K("Pathogens", "founding strains at world start, each a random genome in a few random cells", "strains", "HT: replaces the old flat spillover; nothing seeds them again")] public const int PG_FOUNDERS = 12;
    [K("Pathogens", "cells each founding strain is laid in, at random over the map", "cells", "HT: 12 x 500 cells is ~9% of a 256^2 field: environmental microbes are everywhere, most of them unable to grow in any host")] public const int PG_SEED_CELLS = 500;
    [K("Pathogens", "load put in each founding cell", "load", "HT")] public const float PG_SEED = 2f;
    [K("Pathogens", "two pathogens are one strain (for telemetry) within this RMS genome distance (traits /255, antigen /127)", "-", "HT: one transmission drifts a genome ~0.03 by PG_MUT and MHC_MUT, so a strain is about a dozen transmissions of drift")] public const float PG_STRAIN_D = 0.1f;
    [K("Pathogens", "most strains telemetry tells apart", "strains", "HT")] public const int PG_CLUSTERS = 256;

    // ---------------------------------------------------------------- 6d. weather years (DESIGN §10.7)
    [K("Weather", "standard deviation of the yearly rainfall anomaly", "-", "ECO: interannual rainfall CV 20-35% in semi-arid regions")] public const float RAIN_SD = 0.25f;
    [K("Weather", "persistence of the rainfall anomaly from one year to the next (AR(1))", "-", "ECO: droughts cluster in runs of years")] public const float RAIN_RHO = 0.4f;
    [K("Weather", "limit of the rainfall anomaly", "-", "-")] public const float RAIN_MAX = 0.7f;

    // ---------------------------------------------------------------- 7. vegetation, soil, water
    [K("Vegetation", "grass growth", "/step", "V1")] public const float R_G = 0.010f;
    [K("Vegetation", "sapling maturation", "/step", "HT")] public const float M_P = 0.0010f;
    [K("Vegetation", "nutrient half-saturation", "-", "HT")] public const float K_N = 0.30f;
    [K("Vegetation", "browse regrowth toward capacity", "/step", "HT")] public const float R_B = 0.004f;
    [K("Vegetation", "fruit rot", "/step", "HT")] public const float D_F = 0.004f;
    [K("Vegetation", "seed decay", "/step", "HT")] public const float D_SD = 0.001f;
    [K("Vegetation", "nectar decay", "/step", "HT")] public const float D_NC = 0.01f;
    [K("Vegetation", "root growth", "/step", "HT")] public const float R_R = 0.0015f;
    [K("Vegetation", "leaf senescence into litter", "/step", "HT")] public const float C_LIT = 0.0004f;
    [K("Vegetation", "pollination increment per unit nectar eaten", "-", "HT")] public const float POLL_GAIN = 8f;
    [K("Vegetation", "pollination decay", "/step", "HT")] public const float D_POLL = 0.005f;
    [K("Vegetation", "algae growth", "/step", "HT")] public const float R_A = 0.008f;
    [K("Vegetation", "algae death into detritus", "/step", "HT")] public const float M_A = 0.0015f;
    [K("Vegetation", "phytoplankton growth", "/step", "HT")] public const float R_PH = 0.003f;
    [K("Vegetation", "phytoplankton death into detritus", "/step", "HT")] public const float M_PH = 0.0015f;
    [K("Vegetation", "nutrient used per unit plant growth", "-", "V1")] public const float C_NUTUSE = 0.60f;
    [K("Vegetation", "rot share that becomes nutrient", "-", "V1")] public const float C_NUTGAIN = 0.70f;
    [K("Vegetation", "background weathering", "/step", "V1")] public const float WEATHER = 0.0015f;
    [K("Vegetation", "nutrient mixing between neighbouring water cells", "/step", "HT: water mixes, soil does not")] public const float N_DIFF = 0.05f;
    [K("Vegetation", "carrion rot", "/step", "V1")] public const float ROT_CARRION = 0.0025f;
    [K("Vegetation", "dung rot", "/step", "HT")] public const float ROT_DUNG = 0.0015f;
    [K("Vegetation", "litter rot", "/step", "HT")] public const float ROT_LITTER = 0.0006f;
    [K("Vegetation", "wood rot", "/step", "HT")] public const float ROT_WOOD = 0.00015f;
    [K("Vegetation", "detritus rot", "/step", "HT")] public const float ROT_DETRITUS = 0.0015f;
    [K("Vegetation", "max nutrient", "-", "V1")] public const float N_MAX = 1.5f;
    [K("Vegetation", "run-off: share of a shore cell's soil nutrient that washes into each adjacent water cell", "/step", "HT")] public const float RUNOFF = 0.002f;


    // ---------------------------------------------------------------- 7b. plants (Revision S): evolving genotypes in four slots per land cell (Plants.cs)
    [K("Plants", "steps between two updates of a row of plants (stripes: rows y = step mod this), and dt of an update", "steps", "HT: Revision S; plants change over hundreds of steps, a stripe costs 1/8 of the land per step")] public const int PLANT_EVERY = 8;
    [K("Plants", "largest genotype table (id 0 = empty); a map gets one entry per field cell, between 4,096 and this", "genotypes", "HT: ushort ids; 6 MB at most")] public const int PLANT_GCAP = 65536;
    [K("Plants", "founding plant genotypes, every trait uniform at random, each its own lineage", "genotypes", "HT: Revision S")] public const int PLANT_FOUNDERS = 128;
    [K("Plants", "founders of a stratum drawn per cell, of which the best grower there takes it", "-", "ECO: community assembly as a filter of the regional pool by the site (Keddy 1992)")] public const int PLANT_DRAW = 6;
    [K("Plants", "chance that a recruit's genotype has one trait one level up or down", "/recruit", "HT: with crosses, a few new genotypes per row update")] public const float PLANT_MU = 0.03f;
    [K("Plants", "new genotypes a row can queue per update (more keep their seed parent's)", "-", "HT")] public const int PLANT_PEND = 8;
    [K("Plants", "steps between censuses that free genotypes nothing refers to", "steps", "HT")] public const int PLANT_CENSUS = 64;
    [K("Plants", "years the plants (with weather, fields, fire and water, no animals) grow before step 0", "years", "HT: Revision S; the founders' cover relaxes to what their genes can hold")] public const int PLANT_SPINUP = 1;
    [K("Plants", "log10 of the height (m) at trait level 0", "log10 m", "ECO: herbs from 5 cm")] public const float H_LO = -1.3f;
    [K("Plants", "log10 span of height over the trait", "log10 m", "ECO: up to 40 m trees")] public const float H_SPAN = 2.9f;
    [K("Plants", "height below which a plant is a herb", "m", "ECO: growth-form boundaries (Raunkiaer 1934)")] public const float H_HERB = 0.8f;
    [K("Plants", "height below which a woody plant is a shrub (above, a tree)", "m", "ECO: as H_HERB")] public const float H_SHRUB = 5f;
    [K("Plants", "height whose relative growth rate is R_G", "m", "HT: a 30 cm herb grows as the old grass")] public const float H_REF = 0.3f;
    [K("Plants", "height of a sapling (growth rate, fire, browse)", "m", "ECO")] public const float H_SAP = 2f;
    [K("Plants", "relative growth rate falls with height as H^-EXP_R0", "-", "ECO: metabolic scaling of plant growth (Enquist et al. 1999); with this the old shrub and tree rates come back at 1.3 m and 15 m")] public const float EXP_R0 = 0.82f;
    [K("Plants", "leaf economics: photosynthetic return at LMA level 0", "x", "ECO: leaf economics spectrum (Wright et al. 2004; Reich 2014)")] public const float LES_A = 1.6f;
    [K("Plants", "fall of the return to LMA level 15 (1.6 - 1.2 = 0.4)", "x", "ECO: Wright et al. 2004")] public const float LES_B = 1.2f;
    [K("Plants", "leaf turnover C_LIT x 2^(CLIT_EXP (1 - 2 x_LMA)): leaf lifespan spans 2^3 over the trait", "-", "ECO: leaf lifespan spans ~10x across the spectrum (Wright et al. 2004)")] public const float CLIT_EXP = 1.5f;
    [K("Plants", "leaf share of a plant falls as (H/H_REF)^-LEAF_EXP (turnover per unit cover)", "-", "ECO: leaf mass fraction declines with size (Niklas 1994); 1, 0.5 and 0.3 of the old grass, shrub and tree litter")] public const float LEAF_EXP = 0.3f;
    [K("Plants", "light half-saturation at tough leaves", "-", "ECO: shade-tolerant leaves saturate at low light (Kitajima 1994)")] public const float KL_A = 0.05f;
    [K("Plants", "extra light half-saturation of cheap leaves", "-", "ECO: Kitajima 1994")] public const float KL_B = 0.4f;
    [K("Plants", "water taken by the other slots per unit cover at equal rooting depth", "-", "ECO: two-layer root partitioning (Walter 1971); overlap falls with depth difference (Schenk & Jackson 2002)")] public const float ROOT_OVL = 0.3f;
    [K("Plants", "root share of biomass at ROOT level 0 (food: Pool.Root)", "-", "ECO: root:shoot ratios 0.1-0.7 (Bloom, Chapin & Mooney 1985)")] public const float RHO_LO = 0.1f;
    [K("Plants", "root share span over the trait", "-", "ECO: as RHO_LO")] public const float RHO_SPAN = 0.6f;
    [K("Plants", "water-limited growth at full soil water, x r0 (colimitation with carbon)", "-", "HT: carbon and water colimit (Farquhar & Richards 1984); 3 makes a mid plant mostly carbon-limited when wet")] public const float W_SUP = 3f;
    [K("Plants", "carbon gain lost at full water-use efficiency (stomata kept shut)", "-", "ECO: the WUE-assimilation trade-off (Cowan & Farquhar 1977)")] public const float WUE_GC = 0.35f;
    [K("Plants", "soil water at which growth stops, base", "-", "ECO: wilting point")] public const float WMIN_0 = 0.1f;
    [K("Plants", "extra stopping water for a tall plant, x x_H^0.8", "-", "ECO: tall plants embolise first (Ryan & Yoder 1997; Koch et al. 2004)")] public const float WMIN_H = 0.35f;
    [K("Plants", "stopping water a fully drought-tolerant plant saves", "-", "ECO: xylem safety (Choat et al. 2012)")] public const float WMIN_D = 0.35f;
    [K("Plants", "share of drought mortality drought tolerance saves", "-", "ECO: Choat et al. 2012")] public const float DRT_M = 0.7f;
    [K("Plants", "carbon gain lost at full drought tolerance", "-", "ECO: the safety-efficiency trade-off (Gleason et al. 2016)")] public const float DRT_GC = 0.4f;
    [K("Plants", "drought mortality per step at zero water, x r0", "x r0", "HT")] public const float DROUGHT_M = 0.15f;
    [K("Plants", "best temperature of growth at TOPT level 0", "°C", "ECO: alpine and arctic plants (Körner 2003)")] public const float TOPT_LO = -2f;
    [K("Plants", "span of the best temperature over the trait", "K", "ECO: up to 34 °C (tropical C4)")] public const float TOPT_SPAN = 36f;
    [K("Plants", "temperature at which growth stops, away from the optimum", "K", "ECO: thermal performance curves (Kingsolver & Huey 2008)")] public const float T_WIDTH = 20f;
    [K("Plants", "frost that kills the leaves of the most warmth-loving plant", "°C", "ECO: chilling-sensitive tropical plants (Sakai & Larcher 1987)")] public const float TFROST_HI = -3f;
    [K("Plants", "extra frost a fully cold-adapted plant survives", "K", "ECO: Sakai & Larcher 1987: boreal plants survive -40 °C")] public const float TFROST_SPAN = 35f;
    [K("Plants", "share of leafy cover a frost kills per update", "-", "HT")] public const float FROST_LOSS = 0.2f;
    [K("Plants", "water x temperature factor below which a fully deciduous plant sheds its leaves", "-", "ECO: deciduousness as a response to an unfavourable season (Chabot & Hicks 1982; Kikuzawa 1991)")] public const float DECID_P = 0.6f;
    [K("Plants", "hysteresis of leaf shedding and flushing", "-", "HT")] public const float DECID_HYST = 0.05f;
    [K("Plants", "share of cover a new flush of leaves costs", "-", "ECO: Kikuzawa 1991")] public const float RELEAF = 0.1f;
    [K("Plants", "respiration and drought loss of a leafless plant, x a leafy one's", "-", "ECO: dormancy")] public const float LEAFOFF_RESP = 0.2f;
    [K("Plants", "respiration per step, x r0 x leaf return", "x r0", "ECO: about half of gross production is respired; here the net rate of a mid plant is r0")] public const float RESP = 0.15f;
    [K("Plants", "least share of production in growth", "-", "HT")] public const float A_MIN = 0.05f;
    [K("Plants", "reproductive allocation at REPRO level 0", "-", "ECO: r/K allocation (Grime 1977; Abrahamson 1980)")] public const float AR_LO = 0.05f;
    [K("Plants", "span of reproductive allocation over the trait", "-", "ECO: annuals put up to half their production into seed")] public const float AR_SPAN = 0.45f;
    [K("Plants", "peak of the flowering pulse PHEN_PEAK ((1 + cos)/2)^4, so its yearly mean is 1", "-", "-")] public const float PHEN_PEAK = 3.657f;
    [K("Plants", "production per unit leafy cover per step that reproduction draws on", "/step", "ECO: production per unit leaf area is similar across growth forms (trees grow slowly as cover because most goes into wood); R_G of the old grass, so a mid tree fruits about as the old trees did")] public const float REPRO_RATE = 0.01f;
    [K("Plants", "seed mass (cover units) at the middle of the SEED trait", "cover", "HT")] public const float SEED_REF = 1e-4f;
    [K("Plants", "orders of magnitude of seed mass over the trait", "log10", "ECO: seed mass spans 10^3 in a flora (Moles & Westoby 2004)")] public const float SEED_SPAN = 3f;
    [K("Plants", "establishment per seed rises as (m_s/ref)^EST_EXP", "-", "ECO: the seed size-number trade-off (Smith & Fretwell 1974; Moles & Westoby 2004)")] public const float EST_EXP = 0.4f;
    [K("Plants", "seed-bank decay falls as (m_s/ref)^BANKD_EXP (persistence)", "-", "ECO: Moles & Westoby 2004")] public const float BANKD_EXP = 0.3f;
    [K("Plants", "pulp per unit seed at FRUIT level 15", "-", "ECO: pulp:seed ratios (Howe & Smallwood 1982; Jordano 2000)")] public const float PULP_MAX = 4f;
    [K("Plants", "extra cost per seed of full wings", "-", "ECO: Greene & Johnson 1993")] public const float WING_COST = 0.5f;
    [K("Plants", "extra cost per seed of full hooks", "-", "ECO: Sorensen 1986")] public const float HOOK_COST = 0.3f;
    [K("Plants", "terminal velocity of an unwinged reference seed", "m/s", "ECO: Greene & Johnson 1993")] public const float V_T0 = 2f;
    [K("Plants", "wings cut terminal velocity by 1 + WING_K x", "-", "ECO: winged seeds fall at 0.5-1 m/s")] public const float WING_K = 3f;
    [K("Plants", "mean wind at release height", "m/s", "ECO: the ballistic model D = U H / v_t (Nathan et al. 2002)")] public const float U_WIND = 3f;
    [K("Plants", "a field cell's side in metres, for dispersal distances", "m", "HT")] public const float CELL_M = 25f;
    [K("Plants", "least share of dry seed that leaves its cell (secondary dispersal by rain wash, ants, rolling)", "-", "ECO: secondary dispersal (Vander Wall & Longland 2004)")] public const float NB_MIN = 0.02f;
    [K("Plants", "side of a block of long-distance seed (it lands spread over the 3x3 blocks around)", "cells", "HT: a simple fat tail, deterministic and mass-conserving")] public const int WIND_BLOCK = 16;
    [K("Plants", "seed set from wind pollen per unit leafy cover of the same stratum next door", "-", "ECO: pollen limitation (Baker 1955; Knight et al. 2005)")] public const float WIND_POLL = 0.4f;
    [K("Plants", "seed set per unit pollination (Poll) by animals", "-", "ECO: Rathcke & Lacey 1985")] public const float ANIMAL_POLL = 0.6f;
    [K("Plants", "genetic distance (sum of trait-level differences) of crosses: compatibility exp(-(D/D_PC)^4) x flowering overlap^2", "levels", "HT: about 1.2 levels per trait")] public const float D_PC = 24f;
    [K("Plants", "share of reproductive allocation in nectar at NECTAR level 15", "-", "ECO: nectar costs a few to 30% of reproduction (Southwick 1984; Pyke 1991)")] public const float NECTAR_SHARE = 0.2f;
    [K("Plants", "herbivore energy lost to full toxins", "-", "ECO: plant secondary compounds (Herms & Mattson 1992; Robbins 1987)")] public const float TOX_E = 0.7f;
    [K("Plants", "share of the toxin in fruit pulp", "-", "ECO: ripe fruit is mostly undefended (Cipollini & Levey 1997)")] public const float TOX_FRUIT = 0.2f;
    [K("Plants", "share of production full toxins cost", "-", "ECO: growth-defence trade-off (Coley, Bryant & Chapin 1985)")] public const float TOX_COST = 0.25f;
    [K("Plants", "bite lost to full thorns, for a big bite", "-", "ECO: spines restrict bite size (Cooper & Owen-Smith 1986)")] public const float THORN_B = 0.6f;
    [K("Plants", "bite at which thorns cost half their full effect", "bite", "HT: C_BITE at M=1 is 0.06")] public const float B_HALF = 0.05f;
    [K("Plants", "share of production full thorns cost a woody plant", "-", "ECO: Hanley et al. 2007")] public const float THORN_COST_W = 0.15f;
    [K("Plants", "share of production full silica or hairs cost a herb", "-", "ECO: Massey et al. 2007")] public const float THORN_COST_H = 0.05f;
    [K("Plants", "share of above-ground fire loss a full resprouter grows back", "-", "ECO: resprouting (Bond & Midgley 2001; Pausas & Keeley 2014)")] public const float RESPROUT_MAX = 0.85f;
    [K("Plants", "share of production full resprouting (stored reserves) costs", "-", "ECO: Bond & Midgley 2001")] public const float RESPROUT_COST = 0.2f;
    [K("Plants", "stem survival of a fire with full bark", "-", "ECO: bark thickness and fire survival (Hoffmann et al. 2012)")] public const float BARK_MAX = 0.9f;
    [K("Plants", "share of production full bark costs", "-", "ECO: Hoffmann et al. 2012")] public const float BARK_COST = 0.15f;
    [K("Plants", "flame height per unit fuel", "m", "ECO: stems above the flames survive (Higgins, Bond & Trollope 2000)")] public const float FLAME_H = 2f;
    [K("Plants", "share of the seed bank a fire leaves", "-", "ECO: buried seed survives fire")] public const float BANK_FIRE = 0.5f;
    [K("Plants", "nutrient a full nitrogen fixer adds to what it reads", "-", "ECO: symbiotic fixation (Vitousek & Field 1999)")] public const float NFIX_N = 0.6f;
    [K("Plants", "share of production full fixation costs", "-", "ECO: Vitousek & Field 1999")] public const float NFIX_COST = 0.2f;
    [K("Plants", "temperature below which fixation costs more", "°C", "ECO: fixers are rare in the cold (Houlton et al. 2008)")] public const float NFIX_T = 18f;
    [K("Plants", "cooling that doubles the cost of fixation", "K", "ECO: Houlton et al. 2008")] public const float NFIX_TW = 12f;
    [K("Plants", "extra energy herbivores get from a full fixer's leaves", "-", "ECO: nitrogen-rich leaves")] public const float NFIX_DIG = 0.2f;
    [K("Plants", "energy multiplier of the cheapest leaves", "-", "ECO: tough leaves are poor food (Coley 1983)")] public const float DIG_LO = 1.25f;
    [K("Plants", "fall of the energy multiplier to the toughest leaves", "-", "ECO: Coley 1983")] public const float DIG_SPAN = 0.5f;
    [K("Plants", "seed set without pollen at SELF level 0", "-", "ECO: self-compatibility (Baker 1955)")] public const float SELF_LO = 0.1f;
    [K("Plants", "extra seed set without pollen at full selfing", "-", "ECO: Baker 1955")] public const float SELF_SPAN = 0.8f;
    [K("Plants", "inbreeding depression of selfed recruits of an outcrosser", "-", "ECO: Husband & Schemske 1996")] public const float INBR_LO = 0.5f;
    [K("Plants", "inbreeding depression purged by full selfing", "-", "ECO: Husband & Schemske 1996")] public const float INBR_SPAN = 0.3f;
    [K("Plants", "life expectancy of a reference herb (x (H/H_REF)^LIFE_EXP x (0.5 + LMA))", "steps", "HT: a year; a 15 m tree lives ~20x longer, close to the old tree mortality")] public const float LIFE_HERB = 3000f;
    [K("Plants", "life expectancy rises as H^LIFE_EXP", "-", "ECO: metabolic scaling of lifespan (Marba et al. 2007)")] public const float LIFE_EXP = 0.75f;
    [K("Plants", "most cover recruited from a bank per update", "cover", "HT")] public const float B_SEED0 = 0.01f;
    [K("Plants", "establishment per seed per update at full growth", "-", "HT: a bank of 0.05 cover of reference seed recruits ~90% of B_SEED0")] public const float EST0 = 0.005f;
    [K("Plants", "height an animal browses up to", "m", "ECO: browsers reach ~5-6 m (du Toit 1990)")] public const float BROWSE_H = 6f;
    [K("Plants", "woody cover lost per unit browse eaten", "-", "HT: as the old sapling loss")] public const float BROWSE_LOSS = 0.5f;
    [K("Plants", "share of a bank of full hooks caught in a coat per step, x M^(2/3) x (0.3 + fur)", "/step", "ECO: epizoochory (Sorensen 1986; Couvreur et al. 2004)")] public const float BURR_PICK = 0.002f;
    [K("Plants", "share of seed in a coat that falls off per step", "/step", "ECO: Couvreur et al. 2004")] public const float BURR_DROP = 0.03f;
    [K("Plants", "body mass whose gape passes a reference seed whole", "M", "ECO: gape limits the seed size dispersed (Wheelwright 1985)")] public const float M_GAPE = 1f;

    // ---------------------------------------------------------------- 8. fire and scent
    [K("Fire", "ignition rate per unit dry fuel per land cell", "/cell/step", "HT: ~1 ignition per 100 steps in the dry season")] public const float LAMBDA_FIRE = 2e-6f;
    [K("Fire", "spread probability per unit neighbour fuel", "-", "ECO: gives a fire return interval of ~2 years with no animals (savanna: 1-5 years)")] public const float C_SPREAD = 0.15f;
    [K("Fire", "nutrient from burned biomass", "-", "HT")] public const float C_ASH = 0.5f;
    [K("Fire", "steps a cell burns", "steps", "HT")] public const int FIRE_STEPS = 6;
    [K("Fire", "fuel below which fire cannot spread", "-", "HT")] public const float FUEL_MIN = 0.25f;
    [K("Fire", "probability per step that an animal in a burning cell dies", "-", "HT")] public const float FIRE_KILL = 0.3f;
    [K("Fire", "scent deposit per unit carnivory", "/step", "HT")] public const float C_SCENT = 0.02f;
    [K("Fire", "scent decay", "/step", "HT")] public const float D_SCENT = 0.01f;
    [K("Fire", "MARK multiplier on scent deposit", "-", "HT")] public const float MARK_GAIN = 4f;

    // ---------------------------------------------------------------- 9. climate (Revision O): temperature from sun, height, season and water
    [K("Climate", "default climate strength (Nature.Climate): T = T_REF + Climate x (T_phys - T_REF); 0 = no climate, everywhere T_REF and no heat budget", "-", "HT: Revision O; 1 = Earth-like contrasts. Sanity runs at 8,000 steps: 0.6 left 0 and 243 organisms in two branches, 0.3 left 670 and 944, 0 left 728 and 4,106")] public const float CLIMATE = 0.3f;
    [K("Climate", "share of the soil's wetness that follows where rain falls (the rest follows the ground: near water, in valleys)", "-", "HT: Revision P; before, soil wetness came from the ground and noise only, and 80% of the land was in the driest two fifths")] public const float RAIN_W = 0.7f;
    [K("Climate", "cells over land over which wind-borne air loses all but 1/e of its water (1/this rains out per cell)", "cells", "HT: rain falls off inland over hundreds of km (continental interiors are dry)")] public const float RAIN_L = 30f;
    [K("Climate", "extra rain-out per unit rise of the ground under the wind (share of the relief)", "-", "ECO: orographic rain: windward slopes are wet, lee slopes dry (rain shadows; Roe 2005)")] public const float RAIN_OROG = 40f;
    [K("Climate", "how fast air over water takes up water, per cell", "-", "HT")] public const float RAIN_EVAP = 0.2f;
    [K("Climate", "water the land gives back to the air per cell (evapotranspiration)", "-", "ECO: about 40% of land rain is recycled from the land (Trenberth 1999)")] public const float RAIN_RECYCLE = 0.01f;
    [K("Climate", "rain at the dry belts (near 30° and the poles), against the equator's 1", "-", "ECO: zonal mean rain ~0.3-0.4 of the equator's in the subtropical highs and at the poles (Peixoto & Oort 1992)")] public const float RAIN_DRY = 0.3f;
    [K("Climate", "extra rain of the mid-latitude belt near 55°", "-", "ECO: a second maximum ~0.5-0.6 of the equator's (Peixoto & Oort 1992)")] public const float RAIN_MID = 0.35f;
    [K("Climate", "width (degrees of latitude) of the equatorial and mid-latitude rain belts", "°", "ECO")] public const float RAIN_BELT = 14f;
    [K("Climate", "reference temperature (the temperature of a world with no climate; plant curves, decomposition and evaporation are 1 here)", "°C", "-")] public const float T_REF = 20f;
    [K("Climate", "annual mean sea-level temperature at the equator", "°C", "ECO: observed zonal mean ~27 °C (North 1975)")] public const float T_EQ = 27f;
    [K("Climate", "equator-to-pole temperature drop, x sin² latitude", "K", "ECO: the second Legendre term of North's (1975) energy-balance climate, ~40 K")] public const float DT_EP = 40f;
    [K("Climate", "seasonal amplitude at the pole in the middle of a continent, x sin latitude x continentality", "K", "ECO: continental interiors swing ~40 K winter to summer at high latitude (Conrad 1946)")] public const float A_SEAS = 20f;
    [K("Climate", "lapse rate: cooling with height", "K/km", "ECO: ICAO standard atmosphere, 6.5 K per km")] public const float LAPSE = 6.5f;
    [K("Climate", "height of the highest ground above the lakes", "m", "HT: rolling hills to low mountains")] public const float H_RELIEF = 3000f;
    [K("Climate", "share of the seasonal swing that open water takes away at the shore (continentality 1 - WATER_DAMP x exp(-distance / L_MAR))", "-", "ECO: maritime climates swing a quarter of continental ones (Conrad 1946 continentality index)")] public const float WATER_DAMP = 0.75f;
    [K("Climate", "distance over which the sea's damping of the seasons fades inland", "cells", "HT: ~25 units, a few hours' walk")] public const float L_MAR = 6f;
    [K("Climate", "water under ice stays at least this warm", "°C", "ECO: lakes freeze from the top; the water below stays at 0-4 °C")] public const float T_WATER_MIN = 0.5f;
    [K("Climate", "Budyko-style exponent of how evaporation dries soil: moisture x (e_s(T) / e_s(T_REF))^-EVAP_BETA (Tetens)", "-", "ECO: evaporative demand rises with saturation vapour pressure (Budyko 1974; Tetens 1930)")] public const float EVAP_BETA = 0.5f;
    [K("Climate", "grass: lowest, best and highest temperature of growth (Yan & Hunt beta curve, x 1 at T_REF, at most PLANT_CAP)", "°C", "ECO: Yan & Hunt (1999); C4/C3 grasses grow from ~0 to ~45 °C")] public const float GRASS_TMIN = 0f;
    [K("Climate", "grass: best temperature of growth", "°C", "ECO: Yan & Hunt (1999)")] public const float GRASS_TOPT = 25f;
    [K("Climate", "grass: highest temperature of growth", "°C", "ECO: Yan & Hunt (1999)")] public const float GRASS_TMAX = 45f;
    [K("Climate", "shrubs: lowest temperature of growth", "°C", "ECO: dwarf shrubs grow above the treeline (Körner 2003)")] public const float SHRUB_TMIN = 2f;
    [K("Climate", "shrubs: best temperature of growth", "°C", "ECO")] public const float SHRUB_TOPT = 24f;
    [K("Climate", "shrubs: highest temperature of growth", "°C", "ECO")] public const float SHRUB_TMAX = 45f;
    [K("Climate", "trees: lowest temperature of growth", "°C", "ECO: tree growth stops below ~5 °C, which sets the treeline (Körner & Paulsen 2004)")] public const float TREE_TMIN = 5f;
    [K("Climate", "trees: best temperature of growth", "°C", "ECO")] public const float TREE_TOPT = 22f;
    [K("Climate", "trees: highest temperature of growth", "°C", "ECO")] public const float TREE_TMAX = 40f;
    [K("Climate", "largest plant growth multiplier from temperature", "-", "HT: the beta curves peak a few % above their value at T_REF")] public const float PLANT_CAP = 1.1f;
    [K("Climate", "algae and plankton growth multiplier per K (no growth under ice)", "/K", "ECO: Eppley (1972): phytoplankton growth x1.066 per K")] public const float EPPLEY = 1.066f;
    [K("Climate", "decomposition multiplier per 10 K", "-", "ECO: soil respiration Q10 ~2 (Lloyd & Taylor 1994)")] public const float Q10_ROT = 2f;
    [K("Climate", "lowest temperature of the response tables (TT_N entries of TT_STEP)", "°C", "-")] public const float TT_LO = -60f;
    [K("Climate", "temperature step of the response tables", "K", "-")] public const float TT_STEP = 0.25f;
    [K("Climate", "entries of each response table", "-", "-")] public const int TT_N = 512;

    // ---------------------------------------------------------------- 10. body heat (Revision O): an endotherm's heat budget from its body's physics
    [K("Heat", "activation energy of metabolism: basal cost and digestion of a body whose core is colder than T_BODY slow as exp(E/k (1/T_BODY - 1/T))", "eV", "ECO: 0.6-0.7 eV across animals, plants and microbes (Gillooly et al. 2001; Brown et al. 2004): x0.23 at 20 °C, x0.48 at 28 °C")] public const float E_ACT = 0.65f;
    [K("Heat", "coldest core temperature the metabolic rate follows (below it a cold-blooded body barely runs)", "°C", "HT: near freezing; most ectotherms are torpid below ~2 °C")] public const float T_CORE_MIN = 1f;
    [K("Heat", "body temperature of every animal (an endotherm; no heat is stored from step to step)", "°C", "ECO: mammals 36-39 °C, birds ~40 °C; a step is hours, longer than the thermal time constant of bodies up to tens of kg (Bakken 1976)")] public const float T_BODY = 38f;
    [K("Heat", "dry conductance of a bare body of M=1, per unit core area M^(2/3), in units of C_BASAL per K", "xC_BASAL/K", "HT: a bare M=1 body is thermoneutral down to 38 - 16 = 22 °C (a naked mammal's lower critical temperature, Scholander et al. 1950)")] public const float K_COND = 0.0625f;
    [K("Heat", "insulation: fur of thickness i divides the core's conductance by 1 + R_INS x i", "-", "ECO: arctic fur cuts conductance ~5x against tropical mammals (Scholander et al. 1950)")] public const float R_INS = 4f;
    [K("Heat", "vasodilation: largest rise of the core's conductance when warm", "x", "ECO: peripheral blood flow raises conductance ~3x (Bakken 1976)")] public const float VASO = 3f;
    [K("Heat", "share of an appendage's conductance that leaks when it is cold (vasoconstricted)", "-", "ECO: countercurrent exchange in limbs and ears keeps most heat in (Scholander & Krog 1957)")] public const float APP_LEAK = 0.15f;
    [K("Heat", "conductance in water against air", "x", "ECO: water conducts heat ~25x air; convective loss of an immersed body ~4x (Nadel 1977)")] public const float K_WATER = 4f;
    [K("Heat", "share of its insulation fur keeps when wet", "-", "ECO: wet fur loses about half its insulation (Scholander et al. 1950)")] public const float FUR_WET = 0.5f;
    [K("Heat", "sun on open ground: rise of the operative temperature (x climate strength, x (1 - SHADE_TREE x tree - SHADE_SHRUB x shrub) / (1 + 0.5 R_INS x fur))", "K", "ECO: a day-averaged solar load raises operative temperature several K over air temperature (Bakken 1976; Campbell & Norman 1998); size-independent, as absorbed and lost heat both scale with area")] public const float SOLAR = 6f;
    [K("Heat", "share of the sun that tree cover blocks", "-", "ECO: canopy shade")] public const float SHADE_TREE = 0.8f;
    [K("Heat", "share of the sun that shrub cover blocks", "-", "ECO")] public const float SHADE_SHRUB = 0.4f;
    [K("Heat", "most heat an animal can make by shivering and burning fat, x its basal", "xbasal", "ECO: summit metabolism ~5x basal (Hinds et al. 1993)")] public const float SUMMIT = 5f;
    [K("Heat", "most heat an animal can lose by evaporation (sweating, panting), x its basal", "xbasal", "ECO: evaporative heat loss can reach a few times basal in heat (Schmidt-Nielsen 1997)")] public const float EVAP_MAX = 3f;
    [K("Heat", "thirst per unit of evaporated heat (in basal units per unit body mass), against C_THIRST", "xC_THIRST", "HT: sweating a basal's worth doubles the drying rate (calibrated against C_THIRST, not latent heat)")] public const float EVAP_REL = 2f;
    [K("Heat", "heat strain gained per unit of heat shortfall beyond the caps (in basal units)", "/basal", "HT: a full basal's shortfall kills in ~7 steps")] public const float STRAIN_GAIN = 0.2f;
    [K("Heat", "heat strain recovered per step", "/step", "HT: a shortfall below a quarter of basal is survived")] public const float STRAIN_REC = 0.05f;
    [K("Heat", "basal cost of full fur (insulation gene 1)", "xbasal", "ECO: growing and moulting fur and feathers costs a few % of energy (Lindström et al. 1993)")] public const float COST_INS = 0.06f;
    [K("Heat", "extra cost of moving with full fur", "xmove", "ECO: a heavy coat adds to the cost of transport")] public const float INS_MOVE = 0.1f;
    [K("Heat", "basal cost of full appendages (ears, limbs, tail as radiators; appendage gene 1)", "xbasal", "ECO")] public const float COST_APP = 0.03f;
    [K("Heat", "founders' fur (insulation gene)", "0..1", "HT: with FOUND_APP and M=1 thermoneutral ~15-32 °C around T_REF")] public const float FOUND_INS = 0.15f;
    [K("Heat", "founders' appendages (appendage gene)", "0..1", "HT")] public const float FOUND_APP = 0.3f;
    [K("Heat", "AIR_T sense: (operative temperature - T_BODY) / AIR_T_SCALE, clamped to -1..1", "K", "-")] public const float AIR_T_SCALE = 30f;

    // ---------------------------------------------------------------- 11. local senses and path integration (Revision P)
    [K("Memory", "farthest an animal senses drinkable water or land: SEEK_WATER and SEEK_LAND point the way only within it, and WATER_CLOSE and LAND_CLOSE fade to 0 at it", "cells", "HT: 8 cells = 32 units = MAX_SIGHT, the farthest any eye in this world sees, and the range WATER_CLOSE already faded over; cues of distant water (sight, humidity, odour) carry a few body-lengths to a few km, not across a landscape (Dusenbery 1992). Before 2026-10-30 the way to water was a map-wide distance field")] public const float SENSE_WATER = 8f;
    [K("Memory", "path-integration error: each step's estimate of its own displacement is off by a random vector of SD PI_ERR x the distance moved (each axis)", "-", "ECO: (at BRAIN_START units; see PI_DIFF) odometer and compass errors of path integration add up with the way travelled, so a remembered place drifts as the square root of the distance (Mueller & Wehner 1988; Cheung et al. 2007); HT: a step is hours of travel, so one step is one leg of ~10-20% error")] public const float PI_ERR = 0.2f;
    [K("Memory", "path-integration drift at rest: a held place diffuses by this SD a step (each axis) in a brain of BRAIN_START units; PI_ERR and PI_DIFF both scale as sqrt(BRAIN_START / units)", "units", "ML/ECO: a neural integrator (continuous attractor) diffuses at a rate ~ 1/N neurons (Burak & Fiete 2009); HT: a 24-unit brain loses a place to about its sight range (32) in ~1000 still steps, a 256-unit one in ~10000")] public const float PI_DIFF = 1f;
    [K("Memory", "distance of a remembered place at which its NEAR sense reads 0.5 (NEAR = 1 / (1 + d / MEM_NEAR); 0 when nothing is remembered)", "units", "-: MAX_SIGHT, so NEAR reads 0.5 at the edge of sight")] public const float MEM_NEAR = 32f;
    [K("Memory", "smallest difference in soil moisture across a cell (between its two neighbours) that a thirsty animal can feel and follow toward water; below it it walks downhill", "-", "HT: about the patchiness of moisture on uniform ground; animals follow damp ground and smell water at a distance (Polansky et al. 2015), and water gathers downhill")] public const float DAMP_CUE = 0.02f;
    [K("Memory", "a meal is remembered as 'fed well' when one bite's energy is at least this many steps' need (FIELD_METAB x basal)", "steps", "HT: a bite that pays for its step, FOOD_HERE 0.63; a patch richer than the cost of being there is worth coming back to (Charnov 1976)")] public const float FED_WELL = 1f;
    // ---------------------------------------------------------------- 11. hydrology (Revision Q): rain runs off, cuts valleys and lays down alluvium
    [K("Hydrology", "catchment (cells of mean land rain, x map cells / 65,536) above which a land cell is a river channel at any slope (below it the channel-initiation threshold CHANNEL_C decides)", "cells", "HT: the only river rule before 2026-10-31 (rivers were the 0.5-0.7% of cells over it, 11-14 of them on a 1024 map); now the flat-ground limit of channel initiation")] public const int RIVER_ACC = 900;
    [K("Hydrology", "channel initiation: a channel head is a cell whose mean discharge Q (cells of mean land rain) and slope S (elev per cell) give Q S^2 >= CHANNEL_C; every cell below a head is channel down to its lake or sea", "cells", "ECO: channel heads lie on a drainage area-slope threshold, A ~ S^-2 (Montgomery & Dietrich 1988, 1992); Q S^2 does not change with the map's size (Q ~ FW^2, S ~ 1/FW); HT: the network's channels are 2-4% of the map at the 1024 map (50-90 heads), against 0.5-0.7% before 2026-10-31")] public const float CHANNEL_C = 0.01f;
    [K("Hydrology", "least mean discharge (cells of mean land rain, x map cells / 65,536) of a channel head however steep the ground", "cells", "ECO: below a least source area, hollows are unchanneled colluvial slopes (Montgomery & Dietrich 1992; Dietrich & Dunne 1993)")] public const float CHANNEL_A_MIN = 15f;
    [K("Hydrology", "mean discharge (cells of mean land rain, x map cells / 65,536) at which a river is one cell wide; width = sqrt(Q / RIVER_Q1) cells, at least 1", "cells", "ECO: downstream hydraulic geometry, width ~ Q^0.5 (Leopold & Maddock 1953); HT: the largest rivers of a 1024 map are 3-5 cells wide")] public const float RIVER_Q1 = 150f;
    [K("Hydrology", "widest a river is drawn on the field grid", "cells", "HT: wide enough to be seen and to drink from; a field cell is several km")] public const float RIVER_W_MAX = 5f;
    [K("Hydrology", "the banks of a river more than one cell wide are cells within its half-width of the channel and at most this high above the channel's bed, x SHALLOW", "xSHALLOW", "HT: the river spreads over its valley floor, not up the valley's sides; a cell beside a channel is ~100 m higher on this relief's mean slope")] public const float RIVER_BANK = 1f;
    [K("Hydrology", "Budyko curve shape (Fu's equation): a catchment's mean evapotranspiration ET / P = 1 + PET/P - (1 + (PET/P)^w)^(1/w); what is left runs off and fills the lakes", "-", "ECO: Fu 1981; Zhang et al. 2004 (w ~2-3.5 across catchments; 2.6 the mean)")] public const float BUDYKO_W = 2.6f;
    [K("Hydrology", "a connected region below sea level is part of the sea if it covers at least this share of the map (the largest always is); a smaller one is a closed basin and holds a lake only as its water balance allows", "fraction", "HT: 0 while Geology grades the land to every region below sea level as base level (it holds them all at sea level for 60 Myr and never cuts their rims): as closed basins they would fill to their spill points, 140-400 m higher, and lakes would cover 13-26% of the map (seeds 1, 2, 3, 7, 11; 1 = the largest region only). Raise it once deep time drains to the connected sea alone")] public const float SEA_MIN = 0f;
    [K("Hydrology", "a closed depression holds a lake only if it is at least this many cells at its spill level; a smaller one is a pond site", "cells", "HT: smaller hollows are seasonal ponds (Ponds.cs)")] public const int LAKE_MIN_CELLS = 4;
    [K("Hydrology", "a closed depression holds a lake only if its spill level is at least this far above its floor; a shallower one is a pond site", "elev", "HT: about 10 m at the default relief")] public const float LAKE_MIN_DEPTH = 0.003f;
    [K("Hydrology", "least rise per cell across a filled basin or flat (Priority-Flood+epsilon), so every cell drains", "elev", "-: numerical (Barnes, Lehman & Mulla 2014); far below any real slope")] public const float FLOW_EPS = 1e-6f;
    [K("Hydrology", "passes of routing, stream-power incision and deposition at world generation", "passes", "HT: enough to cut the main valleys; about 15 ms a pass at the default map (1024), 80 ms at 2048")] public const int ERODE_ITERS = 20;
    [K("Hydrology", "stream-power erodibility per pass: dh = -ERODE_K A^ERODE_M S (A in cells of mean rain)", "/pass", "HT: measured at the default map, channels on slopes are cut ~20 m on average and up to ~300 m, hillsides ~6 m (mostly creep)")] public const float ERODE_K = 0.007f;
    [K("Hydrology", "drainage-area exponent of stream power and of carrying capacity (slope exponent 1)", "-", "ECO: m/n ~0.5 (Howard 1994; Whipple & Tucker 1999); observed river concavity 0.4-0.6 (Whipple 2004)")] public const float ERODE_M = 0.5f;
    [K("Hydrology", "hillslope creep per pass: dh = CREEP_K x (sum of the 4 neighbours' heights - 4 h)", "/pass", "ECO/HT: linear soil creep (Culling 1960; Roering et al. 1999), the hillslope half of every landscape-evolution model; rounds the finest noise over the passes")] public const float CREEP_K = 0.01f;
    [K("Hydrology", "carrying capacity: a stream carries at most CARRY_K A^ERODE_M S of sediment per pass", "/pass", "ECO/HT: transport capacity grows with stream power (Willgoose et al. 1991); well above ERODE_K, so steep channels carry what they cut (detachment-limited uplands) and flats drop it; at 0.03 the whole network was transport-limited and channels filled up instead of cutting")] public const float CARRY_K = 0.3f;
    [K("Hydrology", "share of a load above capacity dropped in each cell it passes", "-", "HT: a deposition length of a few cells (Davy & Lague 2009)")] public const float DEPOSIT_FRAC = 0.3f;
    [K("Hydrology", "most fertility alluvium adds to a cell's soil (Fert + ALLUV_FERT x (1 - exp(-alluvium / ALLUV_D)), at most 1)", "-", "ECO: alluvial soils (fluvisols) are young, fine, mineral-rich and among the most fertile (FAO WRB 2015; the Nile, Indus and Mekong floodplains)")] public const float ALLUV_FERT = 0.5f;
    [K("Hydrology", "alluvium depth at which its fertility is 1 - 1/e of ALLUV_FERT", "elev", "HT: about 3 m (relief H_RELIEF over the elevation range)")] public const float ALLUV_D = 0.003f;
    [K("Hydrology", "a land cell at least this far below the rim of the closed basin it lies in is a pond site (a seasonal wetland)", "elev", "ECO/HT: about 3 m; rain gathers and stands in closed basins (pans, playas, prairie potholes), seasonally where evaporation outpaces it")] public const float BASIN_MIN = 0.001f;
    [K("Hydrology", "river depth at RIVER_Q1, x SHALLOW (depth = RIVER_D x SHALLOW x (Q / RIVER_Q1)^RIVER_DEPTH_EXP; rivers stay wadeable: at most SHALLOW)", "xSHALLOW", "HT: 0.5 before Revision Q for every river")] public const float RIVER_D = 0.5f;
    [K("Hydrology", "exponent of river depth (and stage) on discharge", "-", "ECO: at-a-station and downstream hydraulic geometry, depth ~ Q^0.4 (Leopold & Maddock 1953)")] public const float RIVER_DEPTH_EXP = 0.4f;
    [K("Hydrology", "discharge, against its mean, at which a river tops its banks (wetness = season x this year's rain)", "x", "ECO: bankfull flow recurs every 1-2 years (Leopold, Wolman & Miller 1964; Williams 1978): the wet season of an average year (1 + A_SEASON) just tops it")] public const float BANKFULL = 1.35f;
    [K("Hydrology", "floodplain: cells at most this many flow steps upstream of the river they drain into", "cells", "HT: floodplains are a few channel widths to tens of km wide")] public const int FLOOD_R = 3;
    [K("Hydrology", "floodplain: cells at most this high above that river, x SHALLOW", "xSHALLOW", "HT: bounds the search; the stage decides which of them flood")] public const float FLOOD_HAND = 0.5f;

    // ---------------------------------------------------------------- 12. geology (Revision R): plates, uplift and deep-time erosion form the land
    [K("Geology", "fewest tectonic plates on the map (the count is drawn uniformly from GEO_PLATES_LO to GEO_PLATES_HI)", "plates", "GEO: Earth has 7 major and ~8 secondary plates (Bird 2003); the map is a continent's worth of them")] public const int GEO_PLATES_LO = 5;
    [K("Geology", "most tectonic plates on the map", "plates", "GEO: as GEO_PLATES_LO")] public const int GEO_PLATES_HI = 9;
    [K("Geology", "side of the map as the plates see it", "km", "GEO/HT: a few plates of 1,500-3,000 km (Bird 2003) on a coarse grid that resolves a rift (GEO_W_RIFT) in 2-3 cells; not the climate's pole-to-pole rows nor the animals' walking scale (the world's scales are not one)")] public const float GEO_KM = 5000f;
    [K("Geology", "spread of plate sizes: each plate's Voronoi weight is up to this share of the mean plate spacing", "-", "GEO: plate areas span orders of magnitude (Bird 2003; Morra et al. 2013)")] public const float GEO_SIZE_VAR = 0.5f;
    [K("Geology", "bending of plate boundaries: the Voronoi is drawn on positions warped by up to this share of the map (low-frequency noise)", "-", "HT: real boundaries curve and kink; a warp of the plates' outline, not relief")] public const float GEO_WARP = 0.3f;
    [K("Geology", "slowest plate", "km/Myr", "GEO: plate speeds 1-8 cm/yr (DeMets, Gordon & Argus 2010, MORVEL); 1 cm/yr = 10 km/Myr")] public const float GEO_V_LO = 10f;
    [K("Geology", "fastest plate", "km/Myr", "GEO: as GEO_V_LO")] public const float GEO_V_HI = 80f;
    [K("Geology", "largest spin of a plate about its centre (drawn in ±this)", "rad/Myr", "GEO: Euler-pole rotation rates 0.1-1 °/Myr (DeMets et al. 2010); 0.3 °/Myr")] public const float GEO_SPIN = 0.005f;
    [K("Geology", "thinnest plate crust", "km", "GEO: continental crust is 30-45 km thick, mean ~40 (Christensen & Mooney 1995)")] public const float GEO_T_LO = 30f;
    [K("Geology", "thickest plate crust", "km", "GEO: as GEO_T_LO")] public const float GEO_T_HI = 45f;
    [K("Geology", "plates at least this thick are old shields: crystalline basement at the surface; thinner, younger plates carry sedimentary cover", "km", "GEO: shields expose Precambrian basement; platforms are covered by younger strata (Sloss 1963; Durrheim & Mooney 1991)")] public const float GEO_T_CRATON = 40f;
    [K("Geology", "sedimentary cover on a young platform at the start", "km", "GEO: platform cover is 1-3 km thick (the Russian and North American platforms; Sloss 1963)")] public const float GEO_COVER = 1.5f;
    [K("Geology", "thinnest crust a rift or a foredeep can stretch to; it stops sinking there", "km", "GEO: continental rifts thin the crust by a stretching factor of ~1.5-2 before breakup (McKenzie 1978)")] public const float GEO_T_MIN = 20f;
    [K("Geology", "at a convergent boundary, plates whose crust differs by more than this subduct (the thinner sinks, the thicker carries an arc); closer ones collide", "km", "HT: thinner, denser lithosphere sinks under buoyant thick crust (ocean-continent: the Andes); equal buoyant crusts collide (the Himalaya)")] public const float GEO_SUBDUCT = 6f;
    [K("Geology", "Airy isostasy: rock uplift per unit of crustal thickening, (rho_m - rho_c) / rho_m", "-", "GEO: crust 2,800 kg/m3 floating on mantle 3,300 (Airy 1855; Turcotte & Schubert 2002)")] public const float GEO_AIRY = 0.15f;
    [K("Geology", "half-width of a collision belt (thickening x exp(-(d/W)^2) on each side)", "km", "GEO: collision orogens are 200-1,000 km wide (the Alps ~200, the Himalaya-Tibet ~1,000)")] public const float GEO_W_OROGEN = 300f;
    [K("Geology", "half-width of a volcanic arc, and of the trench moat in front of it", "km", "GEO: arc volcanic fronts are ~50-150 km wide (Gill 1981)")] public const float GEO_W_ARC = 100f;
    [K("Geology", "distance of the arc behind the plate boundary (the arc-trench gap)", "km", "GEO: arc-trench gaps are 100-300 km (Dickinson 1973)")] public const float GEO_ARC_D = 200f;
    [K("Geology", "share of the convergence an arc takes up as thickening (shortening and magma), against a collision belt's all", "-", "GEO/HT: at ocean-continent margins most convergence is subducted; arcs thicken by magmatic addition and some shortening (Isacks 1988; Reymer & Schubert 1984)")] public const float GEO_ARC_SHARE = 0.3f;
    [K("Geology", "depth of the trench moat on the sinking plate, as a share of the arc's thickening", "-", "GEO: a flexural moat in front of a load is about a third of its height (Turcotte & Schubert 2002, §3.17)")] public const float GEO_FOREDEEP = 0.3f;
    [K("Geology", "half-width of a rift (thinning x exp(-(d/W)^2) on each side)", "km", "GEO: rift basins are 40-100 km wide, rift zones up to ~200 (the East African and Baikal rifts; Ebinger 1989)")] public const float GEO_W_RIFT = 100f;
    [K("Geology", "a boundary is transform where the plates' closing or opening is under this share of their relative motion (within ~20° of it)", "-", "HT: strike-slip boundaries carry little convergence (the San Andreas: ~10%, transpression)")] public const float GEO_OBLIQUE = 0.34f;
    [K("Geology", "highest ground uplift can raise: above it a thickened crust spreads under its own weight instead of rising", "km", "GEO: orogenic plateaus stand at most ~5 km (Tibet, the Altiplano at ~4); a thicker crust flows apart (Molnar & Lyon-Caen 1988)")] public const float GEO_H_MAX = 5f;
    [K("Geology", "flexural isostasy: the ground rises by this share of the rock eroded from it (and sinks so under sediment laid on it), spread over GEO_FLEX", "-", "GEO: rho_crust / rho_mantle = 2,700 / 3,300; why old mountains outlast their erosion (Molnar & England 1990; Watts 2001)")] public const float GEO_REBOUND = 0.82f;
    [K("Geology", "radius over which the lithosphere spreads the isostatic response to a load (two box blurs)", "km", "GEO: flexural wavelengths of continental lithosphere are 100-300 km for elastic thicknesses of 10-40 km (Watts 2001)")] public const float GEO_FLEX = 150f;
    [K("Geology", "length of the deep time over which uplift and erosion work the land", "Myr", "GEO: active orogens come near a flux steady state in a few to ~10 Myr (Willett & Brandon 2002); this is several times that")] public const float GEO_MYR = 60f;
    [K("Geology", "steps of uplift, stream power, transport and diffusion over GEO_MYR (on the coarse grid, FW / 2)", "steps", "HT: 0.3 Myr a step; the incision is implicit, so the step is stable at any length")] public const int GEO_ITERS = 200;
    [K("Geology", "steps between re-reading where rain falls on the growing relief (Climate.Rainfall)", "steps", "HT: rain follows the relief (orographic rain, rain shadows), which changes slowly")] public const int GEO_RAIN_EVERY = 25;
    [K("Geology", "erosion threshold: bedrock is cut only where sqrt(A) S exceeds this (A in km2)", "km", "GEO: channels incise only above a critical shear stress (Tucker 2004; Lague, Hovius & Davy 2005); a channel draining 10,000 km2 on a slope of 3 in 1,000; low plains stop wearing down and keep their cover (without it, 60 Myr stripped the platform cover from all but the river beds, and 75% of the land was bare basement)")] public const float GEO_PSI = 0.3f;
    [K("Geology", "stream-power erodibility of soft sedimentary rock: dh/dt = -K sqrt(A) S (A in km2)", "/Myr", "GEO: K spans ~0.1-100 /Myr for m = 0.5 from crystalline to weak sedimentary and volcaniclastic rock (Stock & Montgomery 1999)")] public const float GEO_K_SED = 1f;
    [K("Geology", "stream-power erodibility of volcanic rock", "/Myr", "GEO: lavas, tuffs and breccias: between basement and soft strata (Stock & Montgomery 1999)")] public const float GEO_K_VOLC = 0.5f;
    [K("Geology", "stream-power erodibility of crystalline rock (granite, gneiss)", "/Myr", "GEO: the hardest (Stock & Montgomery 1999); erodibility falls with tensile strength squared (Sklar & Dietrich 2001); the contrast is narrowed at cells tens of km wide, where jointing and weathering govern")] public const float GEO_K_CRYST = 0.25f;
    [K("Geology", "hillslope diffusivity on the coarse grid (sub-grid landsliding and creep)", "km2/Myr", "HT: an effective diffusivity for cells tens of km wide, far above soil creep (~0.01 km2/Myr; Fernandes & Dietrich 1997) as in large-scale landscape models (Tucker & Bras 1998)")] public const float GEO_DIFF = 50f;
    [K("Geology", "sediment thicker than this makes a cell's surface rock sedimentary", "km", "HT: 20 m of strata hides its bedrock")] public const float GEO_SED_MIN = 0.02f;
    [K("Geology", "sub-grid roughness: relief within a coarse cell, as a share of the relief between it and its neighbours", "-", "GEO/HT: topography is about self-similar over these scales (Turcotte 1997); roughness on top of the worked relief, not the relief")] public const float GEO_ROUGH = 0.06f;
    [K("Geology", "base fertility of soil on crystalline rock", "-", "ECO: granite and gneiss weather to acid, base-poor soils (FAO WRB 2015)")] public const float FERT_CRYST = 0.4f;
    [K("Geology", "base fertility of soil on volcanic rock", "-", "ECO: volcanic ash and basalt weather to andosols, among the most fertile soils (Shoji, Nanzyo & Dahlgren 1993)")] public const float FERT_VOLC = 0.85f;
    [K("Geology", "base fertility of soil on sedimentary rock", "-", "ECO: shales and limestones weather to moderately rich soils, sandstones to poor ones; their mean")] public const float FERT_SED = 0.6f;
    [K("Geology", "local variation of fertility about its parent material (±, from organic matter, drainage and soil age)", "-", "HT: was the whole fertility (noise) before Revision R")] public const float FERT_VAR = 0.15f;
    [K("Hydrology", "share of the difference between the flooding river's nutrient and a flooded cell's that the flood leaves on the soil, at each water-cycle update (WC_EVERY steps); taken from the river", "/update", "ECO/HT: the flood pulse brings river nutrients and silt onto the floodplain each year (Junk, Bayley & Sparks 1989; the Nile's ~1 mm of silt a year, Said 1993); 0.5 per 100 steps before the water cycle, the same rate per step at 50")] public const float FLOOD_MIX = 0.3f;

    // ---------------------------------------------------------------- 12. water cycle: rain, snow, soil, groundwater, rivers, ponds and lakes as stores and fluxes
    [K("Water cycle", "steps between water-cycle updates (rain, snow, soil, groundwater, routing, ponds, lakes, drinkable water)", "steps", "HT: 60 updates a year at T_SEASON (about 6 days each at 365 days a year); a step is ~3 hours, so the stores change little between updates")] public const int WC_EVERY = 50;
    [K("Water cycle", "mean yearly rain on the land (the rain pattern Climate.Rainfall is relative to it)", "mm/year", "ECO: mean land precipitation ~750-800 mm a year (Trenberth et al. 2007)")] public const float P_MEAN = 800f;
    [K("Water cycle", "share of the land's rain that falls as local convective rain of recycled moisture, spread evenly over the land of its latitude; the rest follows the wind-borne pattern (Climate.Rainfall)", "-", "ECO: ~40% of land rain comes from land evaporation (Trenberth 1999; van der Ent et al. 2010) and falls near where it rose; HT: without it a third of the land got under a quarter of the mean rain")] public const float RAIN_LOCAL = 0.35f;
    [K("Water cycle", "potential evapotranspiration at T_REF; it follows the saturation vapour pressure e_s(T) / e_s(T_REF) (Tetens), and is 0 at or below 0 °C", "mm/year", "ECO: PET of a 20 °C climate ~1,000-1,200 mm a year (Thornthwaite 1948; Hamon 1961: PET ~ saturation vapour density)")] public const float PET_REF = 1000f;
    [K("Water cycle", "air temperature at or below which rain falls as snow", "°C", "ECO: the 50% rain-snow line over land lies near 1 °C (Jennings et al. 2018)")] public const float T_SNOW = 1f;
    [K("Water cycle", "degree-day factor: snow melted per K above 0 °C per day", "mm/K/day", "ECO: degree-day factors of snow 2.5-6 mm/K/day (Hock 2003)")] public const float DDF = 3f;
    [K("Water cycle", "deepest snowpack (water equivalent): deeper snow is a glacier, and its ice carries the surplus downhill to where it melts and runs off", "mm", "ECO: a glacier's accumulation is balanced by ice flow to its ablation zone (Paterson 1994); HT: several years of polar snowfall")] public const float SNOW_MAX = 4000f;
    [K("Water cycle", "water the soil holds for plants (field capacity of the Manabe bucket) on ordinary ground", "mm", "ECO: Manabe (1969) 150 mm; plant-available water of the root zone 100-200 mm on most soils (Saxton & Rawls 2006)")] public const float SOIL_CAP = 150f;
    [K("Water cycle", "extra share of SOIL_CAP that deep alluvium adds (x (1 - exp(-alluvium / ALLUV_D)))", "-", "ECO: deep alluvial loams hold 200-300 mm in the root zone (Saxton & Rawls 2006), twice thin upland soils")] public const float ALLUV_CAP = 1f;
    [K("Water cycle", "shape of the spread of soil capacity within a cell: the saturated share of the cell is 1 - (1 - soil water / capacity)^(b / (1 + b)), and rain on it runs off (variable infiltration capacity)", "-", "ECO: Xinanjiang / VIC curve (Zhao 1992; Wood, Lettenmaier & Zartarian 1992), b 0.1-0.4 (Liang et al. 1994)")] public const float INFIL_B = 0.2f;
    [K("Water cycle", "soil wetness (share of capacity) above which evapotranspiration runs at its potential; below it falls in proportion (Manabe beta)", "-", "ECO: Manabe (1969): 0.75")] public const float BETA_FC = 0.75f;
    [K("Water cycle", "evapotranspiration of bare ground against a full plant cover (cover = grass + shrub + tree, at most 1)", "-", "ECO: transpiration is ~60% of land evapotranspiration (Schlesinger & Jasechko 2014): bare soil dries only its top few cm")] public const float E_BARE = 0.4f;
    [K("Water cycle", "drainage from a saturated soil to groundwater; it goes as (soil water / capacity)^PERC_EXP", "mm/day", "ECO/HT: drainage falls steeply as soil dries (Campbell 1974); groundwater recharge is ~10-30% of rain in humid regions (Doll & Fiedler 2008)")] public const float K_PERC = 3f;
    [K("Water cycle", "exponent of drainage on soil wetness", "-", "ECO/HT: conductivity falls as a high power of water content (Campbell 1974: 2b + 3 ~ 10 for loam); lower here because the bucket is the whole root zone")] public const int PERC_EXP = 4;
    [K("Water cycle", "groundwater residence time in a cell: each update WC_EVERY / GW_TAU of the shallow store feeds the cell's streams, and of the deep store flows on to the cell it drains to (or into the river or lake there)", "days", "ECO: baseflow recession constants of 50-200 days (Tallaksen 1995); water moves downslope cell by cell, so a valley gathers its hillsides' groundwater over years")] public const float GW_TAU = 120f;
    [K("Water cycle", "share of the water draining from the soil (and from ponds' floors) that recharges the deep, regional groundwater, which flows on underground cell by cell down the drainage; the rest recharges the shallow, local groundwater that feeds the cell's own small streams (baseflow)", "-", "ECO: most groundwater discharges within its local flow system; a smaller share joins regional systems that emerge in valley bottoms and lowlands (Toth 1963); HT: a cell is several km across, so its own streams take most")] public const float GW_DEEP = 0.3f;
    [K("Water cycle", "most deep groundwater a cell holds before its water table reaches the surface; the rest emerges (seepage, springs)", "mm", "ECO/HT: an aquifer of 10-20 m and 10-20% drainable porosity holds 1-4 m of water; springs and seeps emerge where flow converges and the water table meets the ground (Toth 1963)")] public const float GW_CAP = 1500f;
    [K("Water cycle", "a groundwater seep (emerging flow) at least this strong is a drinkable spring", "mm/day", "HT: a few mm a day over a cell is a permanent trickle; weaker seeps only keep the ground wet")] public const float SPRING_MIN = 2f;
    [K("Water cycle", "most water a pond site holds (mm over its whole cell) before it spills downhill (fill-and-spill)", "mm", "ECO: prairie potholes and pans are 0.5-2 m deep (Hayashi et al. 2016): with POND_FRAC a full pond is 1 m deep on average, 2 m at its deepest (a paraboloid); fill-and-spill (Spence & Woo 2003)")] public const float POND_CAP = 250f;
    [K("Water cycle", "share of a pond site's cell a full pond covers; its wetted area goes as the square root of the water it holds", "-", "ECO: area ~ volume^(1/2) for a paraboloid basin (Hayashi & van der Kamp 2000); HT: wetlands cover a few to tens of % of pothole country")] public const float POND_FRAC = 0.25f;
    [K("Water cycle", "water at a pond site (mm over its whole cell) from which the pond is drinkable", "mm", "HT: the pond then covers 5% of the cell, ~20 cm deep on average")] public const float POND_WET = 10f;
    [K("Water cycle", "seepage from a pond's wetted floor into the soil and groundwater", "mm/day", "ECO: depression-focused recharge under prairie wetlands ~1-5 mm a day (Hayashi, van der Kamp & Rudolph 1998)")] public const float POND_SEEP = 2f;
    [K("Water cycle", "open-water evaporation (ponds over their wetted area, lakes) against the potential evapotranspiration", "x", "ECO: Penman's E0: grass loses 0.6-0.8 of what open water does (Penman 1948)")] public const float E_OPEN = 1.3f;
    [K("Water cycle", "years over which a river's mean discharge (its channel's size, against which it floods) follows the flow it carries", "years", "ECO: channels adjust their size to the flows they carry over years to decades (Leopold, Wolman & Miller 1964)")] public const float QMEAN_YEARS = 10f;
    [K("Water cycle", "a lake's stage above its outlet's sill (its level at creation) when it receives its mean inflow; its outlet carries in proportion to the stage (a linear reservoir whose outlet is sized for the lake's mean flow)", "mm", "ECO/HT: outlet channels are sized by the flows they carry, as river channels are (Leopold, Wolman & Miller 1964); lakes rise a few dm to m in floods; a lake below its sill does not spill")] public const float LAKE_STAGE = 500f;
    [K("Water cycle", "years of weather the water cycle runs at world creation (average years), so soil, snow, groundwater and rivers start in their seasonal state", "years", "HT: groundwater is first set to the steady state of the first year's recharge, then a second year settles the seasons and gives each river its mean discharge")] public const int WC_SPINUP = 2;
}
