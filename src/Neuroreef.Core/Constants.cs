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
    [K("World", "default agent slots (animals, eggs, embryos)", "slots", "HT")] public const int CAPACITY = 65536;
    [K("World", "share of cells below lake level", "fraction", "HT")] public const float LAKE_SHARE = 0.10f;
    [K("World", "catchment (cells) above which a cell is river", "cells", "HT")] public const int RIVER_ACC = 900;
    [K("World", "depth below which water is shallow (wadeable, algae grow)", "elev", "HT")] public const float SHALLOW = 0.035f;
    [K("World", "season period", "steps", "V1")] public const int T_SEASON = 9000;
    [K("World", "season amplitude on moisture", "-", "V1")] public const float A_SEASON = 0.45f;
    [K("World", "telemetry sample interval", "steps", "HT")] public const int SAMPLE_EVERY = 32;
    [K("World", "telemetry ring length", "samples", "HT")] public const int RING = 4096;
    [K("World", "max neighbours examined per animal per step", "animals", "HT")] public const int MAX_NEIGH = 64;
    [K("World", "max sight range (bounded by the 5x5 hash neighbourhood)", "units", "HT")] public const float MAX_SIGHT = 32f;

    // ---------------------------------------------------------------- 2. allometry
    [K("Allometry", "Kleiber exponent for basal cost and digestion", "-", "ECO")] public const float EXP_BASAL = 0.75f;
    [K("Allometry", "max speed exponent", "-", "ECO")] public const float EXP_SPEED = 0.17f;
    [K("Allometry", "lifespan and maturity exponent", "-", "ECO")] public const float EXP_LIFE = 0.25f;
    [K("Allometry", "sight range exponent", "-", "HT")] public const float EXP_SIGHT = 0.20f;
    [K("Allometry", "turn rate exponent", "-", "HT")] public const float EXP_TURN = -0.20f;
    [K("Allometry", "basal cost at M=1", "E/step", "V1")] public const float C_BASAL = 0.0020f;
    [K("Allometry", "energy reserve per unit mass", "E/M", "ECO: stored reserve of the same order as tissue energy")] public const float C_EMAX = 2.0f;
    [K("Allometry", "gut capacity per unit mass", "M/M", "HT")] public const float C_GUT = 0.30f;
    [K("Allometry", "digestion rate at M=1", "M/step", "HT")] public const float C_DIGEST = 0.005f;
    [K("Allometry", "bite size at M=1", "M/step", "V1")] public const float C_BITE = 0.012f;
    [K("Allometry", "max speed at M=1 (walk)", "units/step", "V1")] public const float C_SPEED = 1.20f;
    [K("Allometry", "turn rate at M=1", "rad/step", "V1")] public const float C_TURN = 0.32f;
    [K("Allometry", "lifespan at M=1", "steps", "V1")] public const float C_LIFE = 3200f;
    [K("Allometry", "maturity age at M=1", "steps", "V1")] public const float C_MATURE = 250f;
    [K("Allometry", "sight range at M=1", "units", "V1")] public const float C_SIGHT = 16f;
    [K("Allometry", "movement cost", "E/(M*v^2)", "V1")] public const float C_MOVE = 0.0020f;
    [K("Allometry", "thirst rise per step (death after ~830 dry steps)", "/step", "HT")] public const float C_THIRST = 0.0012f;
    [K("Allometry", "fatigue gain at full sprint", "/step", "HT")] public const float C_FATIGUE = 0.012f;
    [K("Allometry", "fatigue recovery (x3 resting)", "/step", "HT")] public const float C_RECOVER = 0.004f;
    [K("Allometry", "basal multiplier while resting", "-", "HT")] public const float REST_BASAL = 0.85f;
    [K("Allometry", "speed factor for swimmers", "-", "HT")] public const float SWIM_SPEED = 0.85f;
    [K("Allometry", "speed factor for flyers", "-", "HT")] public const float FLY_SPEED = 1.40f;
    [K("Allometry", "movement cost factor for flyers", "-", "HT")] public const float FLY_COST = 1.6f;
    [K("Allometry", "drinking: thirst removed per step", "/step", "HT")] public const float DRINK_RATE = 0.25f;
    [K("Allometry", "efficiency of turning energy into new tissue (growth, offspring)", "-", "ECO: net growth efficiency 0.6-0.8")] public const float GROWTH_EFF = 0.75f;

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
    [K("Hunting", "share of a swarm's energy one strike removes (max)", "-", "HT")] public const float SWARM_CHUNK = 0.30f;
    [K("Hunting", "parasite drain per strike at M=1", "E", "HT")] public const float C_DRAIN = 0.05f;
    [K("Hunting", "pain added by a failed strike received", "-", "HT")] public const float PAIN_HIT = 0.6f;
    [K("Hunting", "pain decay per step", "/step", "HT")] public const float PAIN_DECAY = 0.03f;

    // ---------------------------------------------------------------- 5. brains, learning, evolution
    [K("Brain", "genome weight scale (int8 / 16)", "-", "-")] public const float WSCALE = 1f / 16f;
    [K("Brain", "max |weight| (genome + learned)", "-", "-")] public const float WMAX = 7.9f;
    [K("Brain", "founder memory-layer (W1) weight range: round(U(-1,1)*FOUNDER_W)", "int8", "HT")] public const int FOUNDER_W = 12;
    [K("Brain", "founder output-layer (W2) weight range", "int8", "HT")] public const int FOUNDER_W2 = 4;
    [K("Brain", "founder output bias (x/16): every primitive of every species starts near 5% (calm newborn brain)", "int8", "HT")] public const int FOUNDER_BIAS = -48;
    [K("Brain", "random spread of the founder output bias", "int8", "HT")] public const int FOUNDER_BIAS_NOISE = 4;
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
    [K("Brain", "mutation probability per genome byte", "-", "V1 0.05, lowered")] public const float MUT_RATE = 0.02f;
    [K("Brain", "small mutation sigma", "int8", "HT")] public const float MUT_SMALL = 2f;
    [K("Brain", "share of mutations that are large jumps", "-", "V1")] public const float MUT_BIG_SHARE = 0.05f;
    [K("Brain", "large mutation sigma", "int8", "V1")] public const float MUT_BIG = 16f;
    [K("Brain", "elite archive size per species", "genomes", "HT")] public const int ARCHIVE = 64;
    [K("Brain", "share of colonists bred from the archive once it holds 4 genomes (the rest are random brains)", "-", "HT")] public const float ARCHIVE_SHARE = 0.9f;
    [K("Brain", "tournament size when choosing a colonist parent from the archive", "genomes", "HT")] public const int TOURNAMENT = 3;
    [K("Brain", "archive score weight per offspring that reached maturity", "-", "HT")] public const float SCORE_MATURED = 3f;
    [K("Brain", "genesis period (immigration floor ramps to 0)", "steps", "HT")] public const int GENESIS = 300000;
    [K("Brain", "genesis floor as share of initial count", "-", "HT")] public const float GENESIS_FLOOR = 0.25f;
    [K("Brain", "founders and colonists arrive in groups of this size at one site", "agents", "ECO: populations are clustered; lone colonists cannot find mates (Allee effect)")] public const int GROUP_SIZE = 8;
    [K("Brain", "radius of a founder or colonist group", "units", "HT")] public const float GROUP_SPREAD = 10f;
    [K("Brain", "genomes per species stored in a gene bank (living sample)", "genomes", "HT")] public const int BANK_LIVING = 96;

    // ---------------------------------------------------------------- 5b. evolvable bodies (DESIGN §7.5)
    [K("Body", "range of the mass multiplier (x/÷)", "-", "HT")] public const float BODY_RANGE_MASS = 2.0f;
    [K("Body", "range of the speed, sight, armour, strike and litter multipliers (x/÷)", "-", "HT")] public const float BODY_RANGE_TRAIT = 1.6f;
    [K("Body", "range of a diet efficiency multiplier (x/÷), before the budget is renormalised", "-", "HT")] public const float BODY_RANGE_DIET = 3.0f;
    [K("Body", "efficiency a pool the founder cannot digest can gain at the gene's maximum", "-", "HT")] public const float DIET_NOVEL = 0.15f;
    [K("Body", "basal cost per unit of extra speed", "-", "HT")] public const float COST_SPEED = 0.4f;
    [K("Body", "basal cost per unit of extra sight", "-", "HT")] public const float COST_SIGHT = 0.15f;
    [K("Body", "basal cost per unit of extra armour", "-", "HT")] public const float COST_ARMOUR = 0.2f;
    [K("Body", "basal cost per unit of extra strike", "-", "HT")] public const float COST_STRIKE = 0.2f;
    [K("Body", "speed loss per unit of extra armour", "-", "HT")] public const float ARMOUR_DRAG = 0.2f;

    // ---------------------------------------------------------------- 6. reproduction
    [K("Reproduction", "energy needed to be READY", "xEmax", "HT")] public const float READY_ENERGY = 0.60f;
    [K("Reproduction", "newborn energy (paid by parent at conception)", "xEmax child", "V1")] public const float F_BIRTH = 0.40f;
    [K("Reproduction", "mating cost per partner", "E/M", "HT")] public const float C_MATE = 0.02f;
    [K("Reproduction", "fission: share of energy the child takes", "-", "HT")] public const float FISSION_SHARE = 0.45f;
    [K("Reproduction", "facultative: FOOD_HERE above which reproduction is asexual", "-", "HT")] public const float FACULT_FOOD = 0.5f;
    [K("Reproduction", "cyclic: max dormancy of resting eggs", "steps", "HT")] public const int RESTING_MAX = 6000;
    [K("Reproduction", "spores: search radius", "cells", "HT")] public const int SPORE_RADIUS = 3;

    // ---------------------------------------------------------------- 7. vegetation, soil, water
    [K("Vegetation", "grass growth", "/step", "V1")] public const float R_G = 0.010f;
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
