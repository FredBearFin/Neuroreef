// NEUROREEF 2 - CONSTANTS TABLE (NR-106)
//
// Every tuning value of the simulation is declared here, once. Each entry carries its meaning,
// unit and origin. docs/CONSTANTS.md is generated from this file (Neuroreef.exe --print-constants)
// and a test fails if the two disagree.
//
// Origin codes:  V1  = taken from Neuroreef v1
//                ECO = ecological rule or exponent from the literature
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
    [K("World", "default agent slots (animals, eggs, embryos)", "slots", "HT: Revision M, ~155 KB per slot with 256-unit brains: 8,192 is ~1.3 GB")] public const int CAPACITY = 8192;
    [K("World", "share of cells below lake level", "fraction", "HT")] public const float LAKE_SHARE = 0.10f;
    [K("World", "catchment (cells) above which a cell is river", "cells", "HT")] public const int RIVER_ACC = 900;
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
    [K("Disease", "match width: a strain infects a host whose recognition genes are within this RMS distance (/127) of its key", "-", "ECO: matching-alleles host-parasite genetics (Hamilton 1980; Lively 2010): rare host genotypes escape, which can favour sex")] public const float MHC_W = 0.35f;
    [K("Disease", "mutation of a strain's key per transmission (SD)", "int8", "ECO: pathogens evolve far faster than their hosts")] public const float MHC_MUT = 6f;
    [K("Clades", "two forms are separate clades when, along the axis between their centres (visible traits), the gap is this many pooled SDs", "-", "ECO: genotypic clusters with few intermediates (Mallet 1995)")] public const float SPLIT_GAP = 3f;
    [K("Allometry", "thirst rise per step (death after ~830 dry steps)", "/step", "HT")] public const float C_THIRST = 0.0012f;
    [K("Allometry", "fatigue gain at full sprint", "/step", "HT")] public const float C_FATIGUE = 0.012f;
    [K("Allometry", "fatigue recovery (x3 resting)", "/step", "HT")] public const float C_RECOVER = 0.004f;
    [K("Allometry", "basal multiplier while resting", "-", "HT")] public const float REST_BASAL = 0.85f;
    [K("Allometry", "speed factor for swimmers", "-", "HT")] public const float SWIM_SPEED = 0.85f;
    [K("Allometry", "speed factor for flyers", "-", "HT")] public const float FLY_SPEED = 1.40f;
    [K("Allometry", "movement cost factor for flyers (per distance, against running)", "-", "ECO: flying costs less per distance than running at equal mass (Schmidt-Nielsen 1972); wing upkeep is in basal cost")] public const float FLY_COST = 0.5f;
    [K("Allometry", "drinking: thirst removed per step", "/step", "HT")] public const float DRINK_RATE = 0.25f;
    [K("Allometry", "efficiency of turning energy into new tissue (growth, offspring)", "-", "ECO: net growth efficiency 0.6-0.8")] public const float GROWTH_EFF = 0.75f;
    [K("Allometry", "a juvenile grows only from energy above this share of its reserve", "xEmax", "ECO: growth is fuelled by surplus after maintenance; hungry young grow slowly (Revision D)")] public const float GROW_RESERVE = 0.5f;

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
    [K("Brain", "founder output bias (x/16): every primitive of every species starts near 5% (calm newborn brain)", "int8", "HT")] public const int FOUNDER_BIAS = -48;
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
    [K("Brain", "founder strength of each starter-kit reflex (x/16): at full need, 80/16 = 5 against the calm bias of -3 gives about 88%", "int8", "designed: the innate starter kit (DESIGN §7.7, Revision D)")] public const int KIT_W = 80;
    [K("Brain", "elite archive size per species", "genomes", "HT (Revision M: 64 before; a genome is now ~83 KB)")] public const int ARCHIVE = 16;
    [K("Brain", "archive score weight per offspring that reached maturity", "-", "HT")] public const float SCORE_MATURED = 3f;
    [K("Prehistory", "length of a prehistory run", "steps", "HT")] public const int PREHISTORY_STEPS = 500000;
    [K("Prehistory", "number of separate branches (populations on the same land) evolving side by side", "branches", "HT (Revision M: 3 before; memory)")] public const int BRANCHES = 2;
    [K("Prehistory", "symbolic time shown in the console: years per step", "years", "-")] public const int YEARS_PER_STEP = 1000;
    [K("Prehistory", "agent slots per branch", "slots", "HT: Revision M, 2 x 16,384 slots of 256-unit brains is ~5 GB (the report warns if the cap binds)")] public const int BRANCH_CAPACITY = 16384;
    [K("Brain", "founders start in groups of this size at one site", "agents", "ECO: populations are clustered; lone colonists cannot find mates (Allee effect)")] public const int GROUP_SIZE = 8;
    [K("Brain", "radius of a founder group", "units", "HT")] public const float GROUP_SPREAD = 10f;
    [K("Brain", "genomes per species stored in a gene bank (living sample)", "genomes", "HT (Revision M: 96 before)")] public const int BANK_LIVING = 48;

    // ---------------------------------------------------------------- 5b. evolvable bodies (DESIGN §7.5)
    [K("Body", "range of the speed and sight multipliers (x/÷)", "-", "HT")] public const float BODY_RANGE_TRAIT = 1.6f;
    [K("Body", "basal cost per unit of extra speed", "-", "HT")] public const float COST_SPEED = 0.4f;
    [K("Body", "basal cost per unit of extra sight", "-", "HT")] public const float COST_SIGHT = 0.15f;
    [K("Body", "speed loss per unit of extra armour", "-", "HT")] public const float ARMOUR_DRAG = 0.2f;

    // ---------------------------------------------------------------- 5c. bodies from genes (DESIGN §6.3, Revision F)
    [K("Body", "smallest body mass a mass gene can give", "M", "ECO: Revision M, ecology review: the agents are animals with brains; smaller consumers live in the fields (algae, decomposers). 0.01 gave tiny bodies 32x the numbers from the same food")] public const float MASS_LO = 0.1f;
    [K("Body", "largest body mass a mass gene can give", "M", "ECO: a large mammal-sized organism (relative units)")] public const float MASS_HI = 40f;
    [K("Body", "organ size (0..1) below which an organ is vestigial (for describing and drawing bodies; speeds are smooth)", "-", "-")] public const float LIMB_MIN = 0.3f;
    [K("Body", "basal cost per unit of fin size, or of leg size per pair of legs", "xbasal", "ECO: locomotor muscle is maintained at a cost")] public const float COST_LIMB = 0.15f;
    [K("Body", "basal cost per unit of wing size, times mass^(1/3)", "xbasal", "ECO: flight power rises faster than mass, so flight favours the small")] public const float COST_WING = 0.5f;
    [K("Body", "basal cost per unit of armour", "xbasal", "ECO: heavy armour is costly to carry and grow")] public const float COST_ARMOUR_ABS = 0.12f;
    [K("Body", "basal cost per unit of weaponry", "xbasal", "ECO: jaws, teeth and claws are costly tissue")] public const float COST_STRIKE_ABS = 0.15f;
    [K("Body", "largest armour a gene can give", "-", "-")] public const float ARMOUR_MAX = 3f;
    [K("Body", "largest weaponry (strike) a gene can give", "-", "-")] public const float STRIKE_MAX = 2f;
    [K("Body", "length (L2 norm) of the vector of digestive efficiencies: a generalist spreads it, a specialist concentrates it", "-", "ECO: digestive specialisation trades off breadth against efficiency")] public const float DIET_BUDGET = 1.0f;
    [K("Body", "largest litter a gene can give", "young", "-")] public const int LITTER_MAX = 12;
    [K("Body", "smallest newborn (or egg) mass", "M", "ECO: eggs and young have a physical smallest size (Revision M)")] public const float NEWBORN_MIN = 0.01f;
    [K("Body", "smallest newborn mass as a share of adult mass", "-", "ECO: fish and insect eggs")] public const float BIRTH_LO = 0.01f;
    [K("Body", "largest newborn mass as a share of adult mass", "-", "ECO: precocial mammals")] public const float BIRTH_HI = 0.3f;
    [K("Body", "narrowest field of view a gene can give", "degrees", "ECO: forward-facing predators")] public const float FOV_LO = 120f;
    [K("Body", "widest field of view a gene can give", "degrees", "ECO: prey with lateral eyes")] public const float FOV_HI = 360f;
    [K("Body", "gestation (or incubation) at M=1, scaling with M^0.25", "steps", "ECO: gestation scales about as M^0.25")] public const float C_GEST = 150f;
    [K("Body", "breeding cooldown at M=1, scaling with M^0.25", "steps", "ECO: as gestation")] public const float C_COOL = 150f;

    [K("Body", "a body can fly when wing size x WING_LIFT reaches mass^(1/3) (wing loading)", "-", "ECO: lift scales with wing area, weight with volume, so large flyers need relatively bigger wings")] public const float WING_LIFT = 1.6f;
    [K("Body", "swimming power (share of the body's top speed in water) needed to stay in deep water", "-", "ECO: staying up in open water needs swimming power")] public const float DEEP_SWIM = 0.3f;
    [K("Body", "a legless body crawls on land at this share of its top speed", "-", "ECO: worms, snakes and stranded fish move slowly without legs")] public const float CRAWL = 0.05f;
    [K("Body", "energy of one call, as a share of basal metabolism at the caller's mass", "xbasal", "ECO: vocalising costs a few times resting metabolism for its duration (Ophir et al. 2010); a call is a short burst")] public const float C_CALL = 0.5f;
    [K("Body", "a voice organ below this makes no sound", "-", "HT: a little voice is no voice; above founder variation around a mute ancestor")] public const float VOICE_MIN = 0.25f;
    [K("Body", "basal cost of a full voice (larynx and its control)", "xbasal", "ECO: small; vocal tissue and its neural control are a minor part of upkeep")] public const float COST_VOICE = 0.03f;
    [K("Food", "share of its meat-digesting skill a meat-eater brings to carrion", "-", "ECO: almost all carnivores scavenge; wolves feed on a kill for days (DeVault et al. 2003)")] public const float SCAVENGE = 0.8f;
    [K("Land", "a pond site is the lowest cell within this many cells", "cells", "ECO: about one basin per hundred square cells on rolling ground (prairie potholes, desert playas)")] public const int POND_R = 2;
    [K("Land", "pond sites are at least this far from a lake, sea or river", "cells", "HT: a pond beside a lake adds nothing")] public const float POND_GAP = 6f;
    [K("Land", "a pond holds water while its cell is this much wetter than its average (season x this year's rain)", "x", "ECO: ephemeral pools fill in the wet season and dry in the dry season and in drought years, in wet and dry country alike")] public const float POND_FILL = 1.0f;
    [K("Land", "steps between pond updates", "steps", "HT: ponds change over a season, so a few times a season is enough")] public const int POND_EVERY = 100;
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

    // ---------------------------------------------------------------- 5d. clades and speciation (DESIGN §7.9)
    [K("Clades", "clade slots (lineages that can exist at once)", "clades", "-: limited by the console frame (5 bits)")] public const int CLADES = 32;
    [K("Clades", "ancestors that start on land", "ancestors", "user choice")] public const int ANCESTORS_LAND = 6;
    [K("Clades", "ancestors that start in water", "ancestors", "user choice")] public const int ANCESTORS_WATER = 4;
    [K("Clades", "founding organisms per ancestor", "organisms", "HT")] public const int ANCESTOR_COUNT = 600;
    [K("Clades", "founders per ancestor are ANCESTOR_COUNT x min(1, map area / continent area x this)", "-", "HT: a valley (1/16 of a continent) is stocked with 1/4 as many founders, so it does not start overstuffed (Revision M)")] public const float FOUND_AREA = 4f;
    [K("Clades", "steps between speciation checks", "steps", "-")] public const int SPEC_EVERY = 2000;
    [K("Clades", "smallest cluster that can become a species", "organisms", "ECO: a viable population")] public const int MIN_SPLIT = 40;
    [K("Clades", "two forms are a new clade only if some visible trait differs by this many visible units (1 = half again the mass, 0.15 of an organ, or a pair of limbs or eyes)", "-", "designed: the user asked for subspecies that are clearly visual (Revision L); fertility then says subspecies or species")] public const float SPLIT_VIS = 1f;
    [K("Clades", "an extinct clade's slot is reused only after this many steps", "steps", "-")] public const int CLADE_RECYCLE = 20000;

    // ---------------------------------------------------------------- 6. reproduction
    [K("Reproduction", "energy needed to be READY", "xEmax", "HT")] public const float READY_ENERGY = 0.60f;
    [K("Reproduction", "newborn energy (paid by parent at conception)", "xEmax child", "V1")] public const float F_BIRTH = 0.40f;
    [K("Reproduction", "mating cost per partner", "E/M", "HT")] public const float C_MATE = 0.02f;
    [K("Reproduction", "a male's refractory period after mating, as a share of the breeding cooldown", "-", "ECO: male refractory periods are short against female breeding cycles (Bateman 1948; Trivers 1972)")] public const float REFRACT = 0.1f;

    // ---------------------------------------------------------------- 6b. parental care (DESIGN §7.8)
    [K("Care", "milk or chick-feeding energy a mother can give per step, as a multiple of her basal cost (shared by all her young)", "xbasal", "ECO: peak lactation milk energy is about 1-2x maternal basal metabolic rate (Hanwell & Peaker 1977)")] public const float CARE_RATE = 1.5f;
    [K("Care", "efficiency of turning maternal energy into milk or food for the young", "-", "ECO: milk synthesis efficiency 0.6-0.8")] public const float CARE_EFF = 0.7f;
    [K("Care", "care ends (weaning) when the young has built this share of its adult tissue", "-", "ECO: mammals wean at roughly a third to a half of adult mass")] public const float WEAN = 0.4f;
    [K("Care", "a mother keeps at least this share of her energy reserve for herself", "xEmax", "ECO: females in poor condition reduce or stop provisioning")] public const float CARE_RESERVE = 0.4f;

    // ---------------------------------------------------------------- 6c. disease (DESIGN §9.6)
    [K("Disease", "probability per step of infection per infected conspecific in contact", "/contact/step", "ECO: with the infectious period below, R0 = 0.5 per steady contact: outbreaks need crowding (density-dependent transmission)")] public const float DIS_BETA = 0.01f;
    [K("Disease", "infectious period", "steps", "ECO: about 1.5% of the lifespan at M=1 (a 1-2 week infection in a 2-year life)")] public const int DIS_STEPS = 50;
    [K("Disease", "immunity after recovery", "steps", "ECO: acquired immunity lasts months (about 20% of the lifespan at M=1)")] public const int DIS_IMMUNE = 600;
    [K("Disease", "spillover from the environment (reservoir) per animal per step", "/step", "ECO: rare sporadic infections seed outbreaks")] public const float DIS_SPILL = 1e-5f;
    [K("Disease", "spread of a pathogen's virulence at each transmission (log-normal)", "-", "ECO: pathogens mutate as they spread; virulence evolves (Anderson & May 1982)")] public const float VIR_MUT = 0.1f;
    [K("Disease", "extra basal cost while infected, per unit of virulence (fever, immune response)", "xbasal", "ECO: fever raises metabolic rate ~10-13% per degree C; sickness costs 25-50%")] public const float DIS_COST = 0.4f;
    [K("Disease", "probability per step of dying while infected (x(1 + hunger))", "/step", "ECO: about 10% case fatality over the infectious period")] public const float DIS_MORT = 0.002f;
    [K("Disease", "malaise felt while infected (as pain)", "-", "ECO: sickness behaviour")] public const float DIS_PAIN = 0.3f;

    // ---------------------------------------------------------------- 6d. weather years (DESIGN §10.7)
    [K("Weather", "standard deviation of the yearly rainfall anomaly", "-", "ECO: interannual rainfall CV 20-35% in semi-arid regions")] public const float RAIN_SD = 0.25f;
    [K("Weather", "persistence of the rainfall anomaly from one year to the next (AR(1))", "-", "ECO: droughts cluster in runs of years")] public const float RAIN_RHO = 0.4f;
    [K("Weather", "limit of the rainfall anomaly", "-", "-")] public const float RAIN_MAX = 0.7f;

    // ---------------------------------------------------------------- 7. vegetation, soil, water
    [K("Vegetation", "grass growth", "/step", "V1")] public const float R_G = 0.010f;
    [K("Vegetation", "soil moisture below which shrubs cannot grow (grass has none)", "-", "ECO: woody plants need wetter soils than grasses; savanna woody cover is capped by rainfall (Sankaran et al. 2005)")] public const float W_SHRUB = 0.20f;
    [K("Vegetation", "soil moisture below which trees and saplings cannot grow", "-", "ECO: as W_SHRUB; trees need the most water")] public const float W_TREE = 0.35f;
    [K("Vegetation", "share of a sapling's water taken by grass roots at full grass cover", "-", "ECO: Walter's two-layer hypothesis: grass roots take the topsoil water tree seedlings need")] public const float GRASS_COMP = 0.7f;
    [K("Vegetation", "extra tree mortality at zero moisture (drought die-off), scaled by the shortfall below W_TREE", "x D_T", "ECO: drought years kill trees")] public const float DROUGHT_T = 5f;
    [K("Vegetation", "long-distance (wind) seed rain: sapling establishment per unit of the world's mean tree cover", "/step", "ECO: rare long-distance dispersal lets trees reach new ground")] public const float WIND_SEED = 0.0004f;
    [K("Vegetation", "grass spread from neighbours", "-", "V1")] public const float SIGMA_G = 0.06f;
    [K("Vegetation", "grass establishment from seed", "-", "HT")] public const float S_G = 0.05f;
    [K("Vegetation", "grass background seed rain", "-", "HT")] public const float G_RAIN = 0.002f;
    [K("Vegetation", "shrub growth", "/step", "ECO: shrub cover e-folds in ~1/4 year (year = T_SEASON)")] public const float R_S = 0.003f;
    [K("Vegetation", "shrub invasion of grass", "-", "HT")] public const float S_S = 0.01f;
    [K("Vegetation", "sapling recruitment", "/step", "HT")] public const float R_P = 0.0008f;
    [K("Vegetation", "sapling maturation", "/step", "HT")] public const float M_P = 0.0010f;
    [K("Vegetation", "tree growth", "/step", "HT")] public const float R_T = 0.0004f;
    [K("Vegetation", "tree mortality", "/step", "HT")] public const float D_T = 0.00006f;
    [K("Vegetation", "canopy shading of grass", "/step", "HT")] public const float SH_G = 0.0003f;
    [K("Vegetation", "canopy shading of shrubs", "/step", "HT")] public const float SH_S = 0.0002f;
    [K("Vegetation", "nutrient half-saturation", "-", "HT")] public const float K_N = 0.30f;
    [K("Vegetation", "browse regrowth toward capacity", "/step", "HT")] public const float R_B = 0.004f;
    [K("Vegetation", "browse capacity per unit tree cover", "-", "HT")] public const float B_TREE = 0.6f;
    [K("Vegetation", "fruit production", "/step", "HT")] public const float R_F = 0.0025f;
    [K("Vegetation", "fruit rot", "/step", "HT")] public const float D_F = 0.004f;
    [K("Vegetation", "seed production", "/step", "HT")] public const float R_SD = 0.0015f;
    [K("Vegetation", "seed decay", "/step", "HT")] public const float D_SD = 0.001f;
    [K("Vegetation", "nectar production", "/step", "HT")] public const float R_NC = 0.004f;
    [K("Vegetation", "nectar decay", "/step", "HT")] public const float D_NC = 0.01f;
    [K("Vegetation", "root growth", "/step", "HT")] public const float R_R = 0.0015f;
    [K("Vegetation", "leaf senescence into litter", "/step", "HT")] public const float C_LIT = 0.0004f;
    [K("Vegetation", "pollination increment per unit nectar eaten", "-", "HT")] public const float POLL_GAIN = 8f;
    [K("Vegetation", "pollination decay", "/step", "HT")] public const float D_POLL = 0.005f;
    [K("Vegetation", "saplings planted per unit seed share in dung", "-", "HT")] public const float DUNG_SAPLING = 0.5f;
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

    // ---------------------------------------------------------------- 8. fire and scent
    [K("Fire", "ignition rate per unit dry fuel per land cell", "/cell/step", "HT: ~1 ignition per 100 steps in the dry season")] public const float LAMBDA_FIRE = 2e-6f;
    [K("Fire", "spread probability per unit neighbour fuel", "-", "ECO: gives a fire return interval of ~2 years with no animals (savanna: 1-5 years)")] public const float C_SPREAD = 0.15f;
    [K("Fire", "nutrient from burned biomass", "-", "HT")] public const float C_ASH = 0.5f;
    [K("Fire", "steps a cell burns", "steps", "HT")] public const int FIRE_STEPS = 6;
    [K("Fire", "share of grass and shrub cover that survives fire as rootstock and resprouts", "-", "ECO: savanna grasses and shrubs resprout")] public const float RESPROUT = 0.25f;
    [K("Fire", "fuel below which fire cannot spread", "-", "HT")] public const float FUEL_MIN = 0.25f;
    [K("Fire", "probability per step that an animal in a burning cell dies", "-", "HT")] public const float FIRE_KILL = 0.3f;
    [K("Fire", "scent deposit per unit carnivory", "/step", "HT")] public const float C_SCENT = 0.02f;
    [K("Fire", "scent decay", "/step", "HT")] public const float D_SCENT = 0.01f;
    [K("Fire", "MARK multiplier on scent deposit", "-", "HT")] public const float MARK_GAIN = 4f;
}
