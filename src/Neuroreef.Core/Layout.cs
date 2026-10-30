// Fixed layouts of the senses (DESIGN §4), the primitive library (DESIGN §5) and the genome (DESIGN §7).
// Identical for every species (NR-201, NR-203, NR-204).

using System;

namespace Neuroreef;

/// <summary>
/// Feature indices: the 66 senses every organism has (Revision F: no built-in categories). Others are not sorted
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
        FOOD_HERE = 26, FOOD_GRAD = 27, IN_WATER = 28, WATER_CLOSE = 29, LAND_CLOSE = 30, FEAR = 31, CROWD = 32, SEASON = 33,
        HEAR_0 = 34,                    // HEAR_0..HEAR_7: how loud each of the 8 sounds was nearby last step (Revision M: voices)
        // the focal caller (Revision N): the loudest caller that looks alike (else the loudest of any kind), kept while it
        // goes on calling. FOC_POS_k: where sound k came in its utterance (1, 0.67, 0.33 for 1st, 2nd, 3rd; 0 absent);
        // MY_POS_k: the same for this organism's own utterance last step (an efference copy)
        FOC_POS_0 = 42, MY_POS_0 = 50,
        FOC_KIN = 58,                   // the focal caller looks alike
        FOC_MOTHER = 59,                // the focal caller is its mother
        FOC_ELDER = 60,                 // the focal caller's age minus its own, as a share of its lifespan (-1..1)
        FOC_SIZE = 61,                  // the focal caller's size against its own (tanh of the log ratio)
        FOC_FACING = 62,                // the focal caller faces it (cosine, -1..1)
        FOC_LIKE_ME = 63,               // how much the focal utterance matches its own last one, position by position
        // body heat (Revision O): a signed internal need like HUNGER (last step's heat made minus heat evaporated, in
        // basal units, through tanh: > 0 cold, < 0 hot), and the operative temperature it is in against its body's
        HEAT_NEED = 64, AIR_T = 65, DEPTH = 66;
    public const int NSOUND = 8;
    public const int MAX_SYL = K.MAX_SYL; // syllables in one utterance at most (Revision N)
    public const int N = DEPTH + 1;   // 67
    public static readonly string[] Names =
    {
        "BIAS", "HUNGER", "THIRST", "FATIGUE", "READY", "AGE", "JUVENILE", "FULL", "PAIN", "CARRYING",
        "KIN_DENSITY", "KIN_CLOSE", "MOTHER_FAR", "MATE_CLOSE",
        "NEAREST_CLOSE", "NEAREST_SIZE", "NEAREST_APPROACH", "NEAREST_ARMED", "NEAREST_MEAT", "NEAREST_WEAK",
        "LARGEST_CLOSE", "LARGEST_SIZE", "LARGEST_APPROACH", "LARGEST_ARMED", "LARGEST_MEAT", "OTHERS",
        "FOOD_HERE", "FOOD_GRAD", "IN_WATER", "WATER_CLOSE", "LAND_CLOSE", "FEAR", "CROWD", "SEASON",
        "HEAR_0", "HEAR_1", "HEAR_2", "HEAR_3", "HEAR_4", "HEAR_5", "HEAR_6", "HEAR_7",
        "FOC_POS_0", "FOC_POS_1", "FOC_POS_2", "FOC_POS_3", "FOC_POS_4", "FOC_POS_5", "FOC_POS_6", "FOC_POS_7",
        "MY_POS_0", "MY_POS_1", "MY_POS_2", "MY_POS_3", "MY_POS_4", "MY_POS_5", "MY_POS_6", "MY_POS_7",
        "FOC_KIN", "FOC_MOTHER", "FOC_ELDER", "FOC_SIZE", "FOC_FACING", "FOC_LIKE_ME", "HEAT_NEED", "AIR_T", "DEPTH",
    };
}

/// <summary>Primitive indices: the 41 base behaviours every organism has. Steering toward or away from the two others
/// it senses (nearest = 1, largest = 2) replaces designed "seek prey" / "flee predator" primitives.</summary>
public static class P
{
    public const int APPROACH_1 = 0, APPROACH_2 = 1, SHADOW_2 = 2, FLEE_2 = 3, FLEE_1 = 4, INTERCEPT_1 = 5, CIRCLE_1 = 6,
        COHERE = 7, SEPARATE = 8, ALIGN = 9, FOLLOW_MOTHER = 10, SEEK_MATE = 11, CLIMB_FOOD = 12, SEEK_WATER = 13, SEEK_LAND = 14,
        AVOID_FEAR = 15, WANDER = 16,
        TOWARD_CALL = 17, AWAY_CALL = 18,   // steer toward / away from the loudest call heard (Revision M: voices)
        FOLLOW_GAZE = 19,                   // turn the way the focal caller is facing (Revision N)
        WARMER = 20, COOLER = 21,           // steer up / down the gradient of the operative temperature around it (Revision O)
        HOLD = 22, SCAN = 23, SPRINT = 24,
        EAT = 25, STRIKE = 26, DRINK = 27, MATE = 28, REST = 29, MARK = 30, LAY = 31, CACHE = 32,
        CALL_0 = 33;                        // CALL_0..CALL_7: sounds, none with a built-in meaning; up to MAX_SYL in one utterance
    public const int N = CALL_0 + F.NSOUND;   // 41
    public const int NSTEER = 22;       // 0..21 steering vectors
    public const int FIRST_ACTION = 25; // 25..40 discrete actions
    public static readonly string[] Names =
    {
        "APPROACH_NEAREST", "APPROACH_LARGEST", "SHADOW_LARGEST", "FLEE_LARGEST", "FLEE_NEAREST", "INTERCEPT_NEAREST", "CIRCLE_NEAREST",
        "COHERE", "SEPARATE", "ALIGN", "FOLLOW_MOTHER", "SEEK_MATE", "CLIMB_FOOD", "SEEK_WATER", "SEEK_LAND",
        "AVOID_FEAR", "WANDER", "TOWARD_CALL", "AWAY_CALL", "FOLLOW_GAZE", "WARMER", "COOLER", "HOLD", "SCAN", "SPRINT",
        "EAT", "STRIKE", "DRINK", "MATE", "REST", "MARK", "LAY", "CACHE",
        "CALL_0", "CALL_1", "CALL_2", "CALL_3", "CALL_4", "CALL_5", "CALL_6", "CALL_7",
    };
}

/// <summary>Genome layout (DESIGN §7.1): W1 (hidden), W2 (output over [hidden, features]), plasticity genes, the body
/// genes that build the whole organism (DESIGN §6.3, Revision F), then the reflex genes.</summary>
public static class G
{
    public const int NH = 256;                   // recurrent hidden units at most (a brain gene decides how many are used; Revision M: 36 before)
    public const int NIN = F.N + NH;             // inputs to the hidden layer and to the output layer
    public const int W1N = NH * NIN;             
    public const int W2N = P.N * NIN;            // (plastic)
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
        B_VOICE = B_CARE + 18,                   // voice (0..1): how loud its calls are; below VOICE_MIN it is mute (Revision M)
        B_IMIT = B_CARE + 19,                    // vocal learning (0..1): how strongly matching a remembered tutor's form reinforces its calls (Revision N)
        B_IMIT_AGE = B_CARE + 20,                // sensitive period for memorising tutors, as a share of its lifespan (0.1..1)
        B_INSUL = B_CARE + 21,                   // insulation (0..1): fur or feathers; keeps heat in, costs energy (Revision O)
        B_APPEND = B_CARE + 22,                  // appendages (0..1): ears, limbs, tail as radiators; lose heat when warm (Revision O)
        B_FLOOR = B_CARE + 23,                   // body-plan size class (0..FLOOR_CLASSES-1): the smallest it can be; copied exactly, rarely steps a class
        B_ENDO = B_CARE + 24,                    // endothermy (0..1): how far it keeps its core above its surroundings (0 cold-blooded, 1 warm-blooded at T_BODY) (Revision P)
        B_BREEDE = B_CARE + 25,                  // breeding condition: the share of its reserve it needs before it breeds (READY_LO..READY_HI) (Revision P)
        APPEARANCE = B_CARE + 26,
        B_LENGTH = APPEARANCE, B_WIDTH = APPEARANCE + 1, B_HEIGHT = APPEARANCE + 2,
        B_HEAD = APPEARANCE + 3, B_NECK = APPEARANCE + 4, B_TAIL = APPEARANCE + 5,
        B_LIMB_POS = APPEARANCE + 6, B_LIMB_SHAPE = APPEARANCE + 7,
        B_FIN_SHAPE = APPEARANCE + 8, B_WING_SHAPE = APPEARANCE + 9,
        B_HUE = APPEARANCE + 10, B_SAT = APPEARANCE + 11, B_LIGHT = APPEARANCE + 12,
        B_PATTERN = APPEARANCE + 13, B_PATTERN_SCALE = APPEARANCE + 14, B_PATTERN_HUE = APPEARANCE + 15,
        B_MHC = APPEARANCE + NAPPEARANCE;         // NMHC recognition genes: a pathogen strain must match them to infect (always last)
    public const int NAPPEARANCE = 16;
    public const int NMHC = 4;
    public const int NBODY = B_MHC + NMHC - BODY;   // 67
    // reflexes (DESIGN §7.7): an innate, non-plastic link from each need-and-opportunity input to each primitive
    public const int NC = 11;                    // reflex inputs (Reflex.Inputs)
    public const int REFLEX = BODY + NBODY;
    public const int REFLEXN = P.N * NC;         // 451
    public const int LEN = REFLEX + REFLEXN;     

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
    public const int Starve = 0, Thirst = 1, Killed = 2, Parasite = 3, Old = 4, Fire = 5, Orphan = 6, Disease = 7, Cold = 8, Heat = 9;
    public const int N = 10;
    public static readonly string[] Names = { "starvation", "thirst", "killed", "parasitism", "old age", "fire", "lost before birth", "disease", "cold", "heat" };
}
