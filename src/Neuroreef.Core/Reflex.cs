// Reflexes (DESIGN §7.7). Real newborns arrive with innate responses that learning cannot erase. Every
// animal has the same ten reflex inputs, each a need, a need met by an opportunity, or a danger, and
// inherited genes R[p, c] linking each input to each primitive. The reflex term adds to the brain's
// output and is never changed by learning.
//
// Founders carry an innate starter kit (Revision D): the same five survival reflexes for every species,
// set to KIT_W in the founder genome. They are ordinary genes: evolution can strengthen, weaken or lose
// them. Revision J adds the mating instinct (the user: "any animal that is sexual, that's literally the top of
// their goals"): ready and a partner near -> MATE, ready and none near -> SEEK_MATE, stronger in males
// (DRIVE_FEMALE, DRIVE_HERM). An asexual organism has the same genes, but MATE and SEEK_MATE do nothing for it.
// Every other reflex link (hunting) starts small and random and must evolve.

using System;

namespace Neuroreef;

public static class Reflex
{
    public const int HUNGRY_FOOD = 0, HUNGRY = 1, THIRSTY = 2, THIRSTY_AT_WATER = 3, TIRED_SAFE = 4, DANGER = 5, LOST = 6,
        READY_MATE = 7, HUNGRY_PREY = 8, READY_ALONE = 9, READY_ANY = 10;
    public static readonly string[] Names = { "hungry and food here", "hungry", "thirsty", "thirsty and at water", "tired and safe",
        "armed larger meat-eater near", "young and far from mother", "ready and a mate near", "hungry and weak prey near", "ready and no mate near", "ready to breed" };

    /// <summary>The starter kit: (input, primitive) pairs set to KIT_W in every founder.</summary>
    public static readonly (int c, int p)[] Kit =
    {
        (HUNGRY_FOOD, P.EAT), (HUNGRY, P.CLIMB_FOOD),                 // eat when hungry and food is here; else move toward food
        (THIRSTY, P.SEEK_WATER), (THIRSTY_AT_WATER, P.DRINK),         // go to water when thirsty; drink there
        (TIRED_SAFE, P.REST),                                         // rest when tired and nothing is threatening
        (DANGER, P.FLEE_1), (DANGER, P.FLEE_2),                        // flee an armed, larger meat-eater
        (LOST, P.FOLLOW_MOTHER),                                      // a young animal follows its mother
        (READY_MATE, P.MATE), (READY_ALONE, P.SEEK_MATE),            // mate when ready and a partner is near; else look for one
        (READY_ANY, P.LAY),                                           // breed alone when ready (does nothing for a sexual organism)
    };

    /// <summary>The reflex inputs from the features.</summary>
    public static void Inputs(ReadOnlySpan<float> f, Span<float> c, float drive = 1f)
    {
        c[HUNGRY_FOOD] = f[F.HUNGER] * f[F.FOOD_HERE];
        c[HUNGRY] = f[F.HUNGER];
        c[THIRSTY] = f[F.THIRST];
        c[THIRSTY_AT_WATER] = f[F.THIRST] * MathF.Max(0f, (f[F.WATER_CLOSE] - 0.75f) * 4f);   // within about 2 cells of water
        c[TIRED_SAFE] = f[F.FATIGUE] * (1f - f[F.FEAR]);
        c[DANGER] = MathF.Max(Threat(f, F.O1_CLOSE), Threat(f, F.O2_CLOSE));
        c[LOST] = f[F.JUVENILE] * f[F.MOTHER_FAR];
        c[READY_MATE] = drive * f[F.READY] * f[F.MATE_CLOSE];
        c[READY_ALONE] = drive * f[F.READY] * (1f - f[F.MATE_CLOSE]);
        c[READY_ANY] = drive * f[F.READY];   // the same drive for cloners as for maters (ecology review)
        c[HUNGRY_PREY] = f[F.HUNGER] * f[F.O1_CLOSE] * f[F.O1_WEAK];
    }

    // close x eats meat x armed x larger (k = the CLOSE feature of that sensed organism)
    static float Threat(ReadOnlySpan<float> f, int k) => f[k] * f[k + 4] * f[k + 3] * MathF.Max(0f, f[k + 1]);
}
