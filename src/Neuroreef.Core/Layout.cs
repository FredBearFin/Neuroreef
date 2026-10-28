// Fixed layouts of the senses (DESIGN §4), the primitive library (DESIGN §5) and the genome (DESIGN §7).
// Identical for every species (NR-201, NR-203, NR-204).

namespace Neuroreef;

/// <summary>Feature indices: the 30 senses every animal has.</summary>
public static class F
{
    public const int BIAS = 0, HUNGER = 1, THIRST = 2, FATIGUE = 3, READY = 4, AGE = 5, JUVENILE = 6, FULL = 7, PAIN = 8, CARRYING = 9,
        KIN_DENSITY = 10, KIN_CLOSE = 11, MOTHER_FAR = 12, MATE_CLOSE = 13,
        BIG_CLOSE = 14, BIG_DENSITY = 15, BIG_APPROACH = 16, BIG_CARN = 17,
        SMALL_CLOSE = 18, SMALL_DENSITY = 19, SMALL_WEAK = 20, CARN_CLOSE = 21,
        FOOD_HERE = 22, FOOD_GRAD = 23, IN_WATER = 24, WATER_CLOSE = 25, LAND_CLOSE = 26, FEAR = 27, CROWD = 28, SEASON = 29;
    public const int N = 30;
    public static readonly string[] Names =
    {
        "BIAS", "HUNGER", "THIRST", "FATIGUE", "READY", "AGE", "JUVENILE", "FULL", "PAIN", "CARRYING",
        "KIN_DENSITY", "KIN_CLOSE", "MOTHER_FAR", "MATE_CLOSE",
        "BIG_CLOSE", "BIG_DENSITY", "BIG_APPROACH", "BIG_CARN",
        "SMALL_CLOSE", "SMALL_DENSITY", "SMALL_WEAK", "CARN_CLOSE",
        "FOOD_HERE", "FOOD_GRAD", "IN_WATER", "WATER_CLOSE", "LAND_CLOSE", "FEAR", "CROWD", "SEASON",
    };
}

/// <summary>Primitive indices: the 28 base behaviours every animal has.</summary>
public static class P
{
    public const int SEEK_SMALL = 0, SEEK_BIG = 1, SHADOW_CARN = 2, FLEE_BIG = 3, FLEE_CARN = 4, INTERCEPT_SMALL = 5, CIRCLE_SMALL = 6,
        COHERE = 7, SEPARATE = 8, ALIGN = 9, FOLLOW_MOTHER = 10, SEEK_MATE = 11, CLIMB_FOOD = 12, SEEK_WATER = 13, SEEK_LAND = 14,
        AVOID_FEAR = 15, WANDER = 16,
        HOLD = 17, SCAN = 18, SPRINT = 19,
        EAT = 20, STRIKE = 21, DRINK = 22, MATE = 23, REST = 24, MARK = 25, LAY = 26, CACHE = 27;
    public const int N = 28;
    public const int NSTEER = 17;       // 0..16 steering vectors
    public const int FIRST_ACTION = 20; // 20..27 discrete actions
    public static readonly string[] Names =
    {
        "SEEK_SMALL", "SEEK_BIG", "SHADOW_CARN", "FLEE_BIG", "FLEE_CARN", "INTERCEPT_SMALL", "CIRCLE_SMALL",
        "COHERE", "SEPARATE", "ALIGN", "FOLLOW_MOTHER", "SEEK_MATE", "CLIMB_FOOD", "SEEK_WATER", "SEEK_LAND",
        "AVOID_FEAR", "WANDER", "HOLD", "SCAN", "SPRINT",
        "EAT", "STRIKE", "DRINK", "MATE", "REST", "MARK", "LAY", "CACHE",
    };
}

/// <summary>Genome layout (DESIGN §7.1): W1 (hidden), W2 (output over [hidden, features]), plasticity genes, body genes,
/// then (Revision C) the parental-care gene and the reflex genes.</summary>
public static class G
{
    public const int NH = 12;                    // recurrent hidden units
    public const int NIN = F.N + NH;             // 42 inputs to the hidden layer and to the output layer
    public const int W1N = NH * NIN;             // 504
    public const int W2N = P.N * NIN;            // 1176 (plastic)
    public const int W1 = 0, W2 = W1N;
    public const int ETA = W1N + W2N, LAMBDA = ETA + 1, SIGMA = ETA + 2, KAPPA = ETA + 3;
    // body genes (DESIGN §7.5): log-scale multipliers on the species' founder body plan, 0 = founder value
    public const int BODY = ETA + 4;
    public const int B_MASS = BODY, B_SPEED = BODY + 1, B_SIGHT = BODY + 2, B_ARMOUR = BODY + 3, B_STRIKE = BODY + 4, B_LITTER = BODY + 5,
        B_DIET = BODY + 6;                       // 15 bytes, one per pool
    public const int NBODY = 6 + Pools.N;        // 21
    public const int LEN_B = BODY + NBODY;       // 1705 bytes: the genome of Revision B gene banks
    // parental care (DESIGN §7.8): log-scale multiplier on the species' care level
    public const int B_CARE = LEN_B;
    // reflexes (DESIGN §7.7): an innate, non-plastic link from each need-and-opportunity input to each primitive
    public const int NC = 8;                     // reflex inputs (Reflex.Inputs)
    public const int REFLEX = B_CARE + 1;
    public const int REFLEXN = P.N * NC;         // 224
    public const int LEN = REFLEX + REFLEXN;     // 1930 bytes

    /// <summary>Plasticity genes are int8 mapped linearly onto their ranges.</summary>
    public static float Gene(sbyte b, float lo, float hi) => lo + (hi - lo) * ((b + 128) / 255f);
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
