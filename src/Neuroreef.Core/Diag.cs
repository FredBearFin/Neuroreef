// Gut diagnostics of a headless run (2026-11-01, NR_DIAG=1; reporting only: counts, changes nothing in the world).
// Which limit sets each step's digestion (the bulk rate C_DIGEST, the absorption limit C_ABSORB, or the gut running
// empty) and each bite (its size C_BITE, the food within reach, or the room left in the gut C_GUT), and the intake
// that results: energy digested against basal, and dry matter against the voluntary-intake line 0.1 M^0.75 kg/day.
// Counting starts at step NR_ECO_FROM (default 0). RESOLVE is serial, so plain counters are deterministic.

using System;
using System.Text;

namespace Neuroreef;

public static class Diag
{
    public static readonly bool On = Environment.GetEnvironmentVariable("NR_DIAG") == "1";
    public static readonly ulong From = ulong.TryParse(Environment.GetEnvironmentVariable("NR_ECO_FROM"), out var f) ? f : 0UL;

    // digestion: animal-steps with something in the gut, and which limit set what passed
    public static long DigSteps, BulkBound, AbsorbBound, EmptyBound;
    // eating: bites taken, and which limit set their size; choices whose worth the absorption limit capped
    public static long Bites, BiteBound, ReachBound, GutBound, ChoiceCapped;
    // intake: energy digested and basal burned (E), dry matter digested (kg), and the sum of M^0.75 x steps of
    // every animal-step (for intake per metabolic mass), and of the gut's bulk rate (kg)
    public static double EDigested, EBasal, KgDigested, KgBitten, MetSteps, BulkRate;
    public static long AnimalSteps;

    public static bool Counting(World w) => On && w.Step >= From;

    public static void Animal(World w, float basal, float mPow)
    {
        if (!Counting(w)) return;
        AnimalSteps++; EBasal += basal; MetSteps += mPow;
    }

    public static void Digest(World w, float d, float de, float gut, float bulk, bool absorbBound)
    {
        if (!Counting(w)) return;
        DigSteps++; EDigested += de; KgDigested += d; BulkRate += bulk;
        if (absorbBound) AbsorbBound++;
        else if (d >= bulk * 0.9999f && gut > bulk) BulkBound++;
        else EmptyBound++;
    }

    public static void Bite(World w, float bite, float bite0, float reach, float room, bool capped)
    {
        if (!Counting(w)) return;
        Bites++; KgBitten += bite;
        if (capped) ChoiceCapped++;
        if (bite >= room * 0.9999f && room <= bite0 && room <= reach) GutBound++;
        else if (bite >= reach * 0.9999f && reach <= bite0) ReachBound++;
        else BiteBound++;
    }

    static string P(long a, long b) => b > 0 ? $"{100.0 * a / b:F1}%" : "-";

    public static string Report()
    {
        var o = new StringBuilder();
        o.AppendLine($"DIAG gut (from step {From}): {AnimalSteps} animal-steps, {DigSteps} digesting ({P(DigSteps, AnimalSteps)})");
        o.AppendLine($"DIAG digestion set by: bulk C_DIGEST {P(BulkBound, DigSteps)}, absorption C_ABSORB {P(AbsorbBound, DigSteps)}, gut ran empty {P(EmptyBound, DigSteps)}");
        o.AppendLine($"DIAG bites {Bites} ({P(Bites, AnimalSteps)} of animal-steps): set by bite size C_BITE {P(BiteBound, Bites)}, food in reach {P(ReachBound, Bites)}, room in the gut C_GUT {P(GutBound, Bites)}; choice capped at C_ABSORB {P(ChoiceCapped, Bites)}");
        double stepsDay = K.STEPS_PER_DAY;
        o.AppendLine($"DIAG intake: energy digested {(EBasal > 0 ? EDigested / EBasal : 0):F2}x basal over all animal-steps; " +
                     $"{(DigSteps > 0 ? EDigested / DigSteps : 0):E3} E per digesting step; " +
                     $"food {(MetSteps > 0 ? KgDigested / MetSteps * stepsDay : 0):F4} kg/kg^0.75/day digested, {(MetSteps > 0 ? KgBitten / MetSteps * stepsDay : 0):F4} bitten " +
                     $"(gut's bulk rate {(DigSteps > 0 ? BulkRate / DigSteps : 0):E3} kg per digesting step; mean energy density digested {(KgDigested > 0 ? EDigested / KgDigested : 0):F2} E/kg)");
        return o.ToString();
    }
}
