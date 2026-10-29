// Fixed layouts of the senses (DESIGN §4), the primitive library (DESIGN §5) and the genome (DESIGN §7).
// Identical for every species (NR-201, NR-203, NR-204).

using System;

namespace Neuroreef;

/// <summary>
/// Feature indices: the 34 senses every organism has (Revision F: no built-in categories). Others are not sorted
/// into "bigger", "smaller" or "carnivore" by a designed threshold: an organism senses the two others that stand
/// out - the nearest (O1) and the largest in view (O2) - through raw quantities (how close, relative size, closing
/// speed, how armed, how meat-eating, how weak). What counts as prey or danger is for evolution and learning.
/// Kin are others that look like itself (similar body genes).
/// </summary>
public static class F
{
    public const int BIAS = 0, HUNGER = 1, THIRST = 2, FATIGUE = 3, READY = 4, AGE = 5, JUVENILE = 6, FULL = 7, PAIN = 8, CARRYING = 9,
        KIN_DENSITY = 10, KIN_CLOSE = 11, MOTHER_FAR = 12, MATE_CLOSE = 13,
        O1_CLOSE = 14, O1_SIZE = 15, O1_APPROACH = 16, O1_ARMED = 17, O1_MEAT = 18, O1_WEAK = 19,
        O2_CLOSE = 20, O2_SIZE = 21, O2_APPROACH = 22, O2_ARMED = 23, O2_MEAT = 24, OTHERS = 25,
        FOOD_HERE = 26, FOOD_GRAD = 27, IN_WATER = 28, WATER_CLOSE = 29, LAND_CLOSE = 30, FEAR = 31, CROWD = 32, SEASON = 33;
    public const int N = 34;
    public static readonly string[] Names =
    {
        "BIAS", "HUNGER", "THIRST", "FATIGUE", "READY", "AGE", "JUVENILE", "FULL", "PAIN", "CARRYING",
        "KIN_DENSITY", "KIN_CLOSE", "MOTHER_FAR", "MATE_CLOSE",
        "NEAREST_CLOSE", "NEAREST_SIZE", "NEAREST_APPROACH", "NEAREST_ARMED", "NEAREST_MEAT", "NEAREST_WEAK",
        "LARGEST_CLOSE", "LARGEST_SIZE", "LARGEST_APPROACH", "LARGEST_ARMED", "LARGEST_MEAT", "OTHERS",
        "FOOD_HERE", "FOOD_GRAD", "IN_WATER", "WATER_CLOSE", "LAND_CLOSE", "FEAR", "CROWD", "SEASON",
    };
}

/// <summary>Primitive indices: the 28 base behaviours every organism has. Steering toward or away from the two others
/// it senses (nearest = 1, largest = 2) replaces designed "seek prey" / "flee predator" primitives.</summary>
public static class P
{
    public const int APPROACH_1 = 0, APPROACH_2 = 1, SHADOW_2 = 2, FLEE_2 = 3, FLEE_1 = 4, INTERCEPT_1 = 5, CIRCLE_1 = 6,
        COHERE = 7, SEPARATE = 8, ALIGN = 9, FOLLOW_MOTHER = 10, SEEK_MATE = 11, CLIMB_FOOD = 12, SEEK_WATER = 13, SEEK_LAND = 14,
        AVOID_FEAR = 15, WANDER = 16,
        HOLD = 17, SCAN = 18, SPRINT = 19,
        EAT = 20, STRIKE = 21, DRINK = 22, MATE = 23, REST = 24, MARK = 25, LAY = 26, CACHE = 27;
    public const int N = 28;
    public const int NSTEER = 17;       // 0..16 steering vectors
    public const int FIRST_ACTION = 20; // 20..27 discrete actions
    public static readonly string[] Names =
    {
        "APPROACH_NEAREST", "APPROACH_LARGEST", "SHADOW_LARGEST", "FLEE_LARGEST", "FLEE_NEAREST", "INTERCEPT_NEAREST", "CIRCLE_NEAREST",
        "COHERE", "SEPARATE", "ALIGN", "FOLLOW_MOTHER", "SEEK_MATE", "CLIMB_FOOD", "SEEK_WATER", "SEEK_LAND",
        "AVOID_FEAR", "WANDER", "HOLD", "SCAN", "SPRINT",
        "EAT", "STRIKE", "DRINK", "MATE", "REST", "MARK", "LAY", "CACHE",
    };
}

/// <summary>Genome layout (DESIGN §7.1): W1 (hidden), W2 (output over [hidden, features]), plasticity genes, the body
/// genes that build the whole organism (DESIGN §6.3, Revision F), then the reflex genes.</summary>
public static class G
{
    public const int NH = 36;                    // recurrent hidden units at most (a brain gene decides how many are used)
    public const int NIN = F.N + NH;             // 70 inputs to the hidden layer and to the output layer
    public const int W1N = NH * NIN;             // 2520
    public const int W2N = P.N * NIN;            // 1960 (plastic)
    public const int W1 = 0, W2 = W1N;
    public const int ETA = W1N + W2N, LAMBDA = ETA + 1, SIGMA = ETA + 2, KAPPA = ETA + 3;
    // body genes (DESIGN §6.3): absolute values on fixed ranges; nothing is relative to a designed species
    public const int BODY = ETA + 4;
    public const int B_MASS = BODY, B_SPEED = BODY + 1, B_SIGHT = BODY + 2, B_ARMOUR = BODY + 3, B_STRIKE = BODY + 4, B_LITTER = BODY + 5,
        B_DIET = BODY + 6,                       // 15 bytes, one per pool: shares of the digestive budget
        B_CARE = B_DIET + Pools.N,               // parental care
        B_LEGS = B_CARE + 1, B_FINS = B_CARE + 2, B_WINGS = B_CARE + 3,   // locomotion organs
        B_FOV = B_CARE + 4,                      // field of view (wide and short against narrow and long)
        B_SEX = B_CARE + 5,                      // >= 0 sexual, < 0 asexual
        B_LIVE = B_CARE + 6,                     // >= 0 live birth (gestation), < 0 eggs
        B_BIRTH = B_CARE + 7,                    // newborn mass as a share of adult mass
        B_LEGN = B_CARE + 8,                     // number of leg pairs (1..4: 2 to 8 legs)
        B_LIFE = B_CARE + 9,                     // lifespan multiplier (x/÷ 2): longer life costs body upkeep
        B_DEVEL = B_CARE + 10,                   // development time multiplier (x/÷ 2): fast developers age sooner
        B_BRAIN = B_CARE + 11,                   // memory units in use (BRAIN_MIN..G.NH): a bigger brain costs energy
        B_HERM = B_CARE + 12,                    // >= 0 hermaphrodite (both sexes in one), < 0 two separate sexes
        B_SEXRATIO = B_CARE + 13,                // share of a mother's young that are female (separate sexes)
        B_FINN = B_CARE + 14,                    // number of fin pairs (1..4)
        B_EYEN = B_CARE + 15,                    // number of eye pairs (1..4)
        B_IMMUNE = B_CARE + 16,                  // immune investment (0..1): resists disease, costs energy
        B_WATER = B_CARE + 17,                   // water economy (0..1): loses less water, costs energy (Revision K)
        B_MHC = B_CARE + 18;                     // NMHC recognition genes: a pathogen strain must match them to infect
    public const int NMHC = 4;
    public const int NBODY = B_MHC + NMHC - BODY;   // 43
    // reflexes (DESIGN §7.7): an innate, non-plastic link from each need-and-opportunity input to each primitive
    public const int NC = 11;                    // reflex inputs (Reflex.Inputs)
    public const int REFLEX = BODY + NBODY;
    public const int REFLEXN = P.N * NC;         // 252
    public const int LEN = REFLEX + REFLEXN;     // 2134 bytes

    /// <summary>Plasticity genes are int8 mapped linearly onto their ranges.</summary>
    public static float Gene(sbyte b, float lo, float hi) => lo + (hi - lo) * ((b + 128) / 255f);
    /// <summary>A gene on a logarithmic range (mass, newborn share).</summary>
    public static float GeneLog(sbyte b, float lo, float hi) => lo * MathF.Pow(hi / lo, (b + 128) / 255f);
    /// <summary>The gene byte that gives value v on a linear range (used to build ancestors).</summary>
    public static sbyte ToGene(float v, float lo, float hi) => (sbyte)Math.Clamp((int)MathF.Round((v - lo) / (hi - lo) * 255f - 128f), -127, 127);
    public static sbyte ToGeneLog(float v, float lo, float hi) => (sbyte)Math.Clamp((int)MathF.Round(MathF.Log(v / lo) / MathF.Log(hi / lo) * 255f - 128f), -127, 127);
}

/// <summary>Agent life stages within a slot.</summary>
public static class St
{
    public const byte Dead = 0, Egg = 1, Embryo = 2, Juvenile = 3, Adult = 4;
}

/// <summary>Causes of death (NR-401).</summary>
public static class Cause
{
    public const int Starve = 0, Thirst = 1, Killed = 2, Parasite = 3, Old = 4, Fire = 5, Orphan = 6, Disease = 7;
    public const int N = 8;
    public static readonly string[] Names = { "starvation", "thirst", "killed", "parasitism", "old age", "fire", "lost before birth", "disease" };
}
