// Reflexes (DESIGN §7.7). Real newborns arrive with innate responses that learning cannot erase. Here
// every animal has the same eight reflex inputs, each a need met by an opportunity (or an alarm), and
// inherited genes R[p, c] that link each input to each primitive. The inputs are fixed for every
// species; which primitive a need drives, and how strongly, is left to evolution (founders start with
// small random links). The reflex term adds to the brain's output and is never changed by learning.

using System;

namespace Neuroreef;

public static class Reflex
{
    public const int HUNGRY_FOOD = 0, THIRSTY_WATER = 1, TIRED_SAFE = 2, READY_MATE = 3, HUNGRY_PREY = 4, ALARM = 5, DANGER = 6, LOST = 7;
    public static readonly string[] Names = { "hungry and food here", "thirsty and water near", "tired and safe", "ready and a mate near",
        "hungry and weak prey near", "alarm (fear or pain)", "large carnivore near", "young and far from mother" };

    /// <summary>The reflex inputs from the features.</summary>
    public static void Inputs(ReadOnlySpan<float> f, Span<float> c)
    {
        c[HUNGRY_FOOD] = f[F.HUNGER] * f[F.FOOD_HERE];
        c[THIRSTY_WATER] = f[F.THIRST] * f[F.WATER_CLOSE];
        c[TIRED_SAFE] = f[F.FATIGUE] * (1f - f[F.FEAR]);
        c[READY_MATE] = f[F.READY] * f[F.MATE_CLOSE];
        c[HUNGRY_PREY] = f[F.HUNGER] * f[F.SMALL_CLOSE] * f[F.SMALL_WEAK];
        c[ALARM] = MathF.Max(f[F.FEAR], f[F.PAIN]);
        c[DANGER] = f[F.BIG_CLOSE] * f[F.BIG_CARN];
        c[LOST] = f[F.JUVENILE] * f[F.MOTHER_FAR];
    }
}
