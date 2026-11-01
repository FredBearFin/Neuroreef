// Feeding-chain diagnostics (2026-11-01, NR_DIAG=1; reporting only: reads the world, changes nothing).
// Counts every link from sensing food to digesting it, the energy budget against field metabolic rate, strikes and
// kills, and what an animal had around it when it starved. Filled in RESOLVE, which is serial, so plain sums suffice.

using System;
using System.Text;

namespace Neuroreef;

public sealed class Diag
{
    public static Diag? D;

    /// <summary>Diet group of an organism for the census: 1 when meat is its best-digested food, else 0.</summary>
    public static int Group(World w, int i)
    {
        var d = w.DietOf(i);
        float plant = 0f; for (int p = 0; p <= (int)Pool.Phyto; p++) plant = MathF.Max(plant, d[p]);
        return (d[(int)Pool.Meat] > plant ? 1 : 0) + (w.StageOf[i] == St.Juvenile ? 2 : 0);
    }
    static readonly string[] GName = { "plant-eaters, adult", "meat-eaters, adult", "plant-eaters, juvenile", "meat-eaters, juvenile" };

    public const int G = 4;
    public readonly double[] Steps = new double[G], Mass = new double[G], Fill = new double[G], Hunger = new double[G],
        FoodHere = new double[G], EatFired = new double[G], EatFiredFood = new double[G], EatScan = new double[G], EatFull = new double[G],
        EatAir = new double[G], EatNone = new double[G], EatOk = new double[G], BiteKg = new double[G], BiteE = new double[G],
        HungryNoEat = new double[G], Absorbed = new double[G], AbsorbCapped = new double[G], DigCapped = new double[G],
        Basal = new double[G], Move = new double[G], Thermo = new double[G], Breath = new double[G], Grow = new double[G], Strike = new double[G],
        Call = new double[G], Kleiber = new double[G], Fmr = new double[G];
    // strikes by the attacker's group: tried at a target, target in reach, hit, kill, meat eaten (E), prey-eat strikes,
    // prey a bigger body than the attacker
    public readonly double[] StrTry = new double[G], StrReach = new double[G], StrHit = new double[G], StrKill = new double[G],
        StrMeatE = new double[G], StrMeatKg = new double[G], PreyEat = new double[G], StrP = new double[G], StrKin = new double[G], WoundOnly = new double[G];
    public readonly long[] Deaths = new long[G * Cause.N];
    public readonly double[] StarveFood = new double[G], StarveGut = new double[G], StarveJuv = new double[G], StarveYoungAdult = new double[G],
        StarveCap = new double[G], StarveBasal = new double[G], StarveThermo = new double[G], StarveMove = new double[G], StarveGrow = new double[G],
        StarveEndo = new double[G], StarveRate = new double[G], StarveBreath = new double[G], StarveBrain = new double[G], StarveMassShare = new double[G], StarveDe = new double[G], StarveDens = new double[G], StarveDig = new double[G],
        StarveHurt = new double[G], RetalN = new double[G], RetalE = new double[G], RetalBmr = new double[G];
    /// <summary>The last step's budget of an organism that starved (RESOLVE): absorption limit and costs, in its own Kleiber BMR.</summary>
    public void Starved(World w, int i, int g, float bmr, float cap, float basal, float thermo, float move, float grow, float breathe, float brain, float rateT, float de, float dig)
    {
        StarveDe[g] += de / bmr; StarveDig[g] += dig / bmr; StarveDens[g] += w.Gut[i] > 1e-9f ? w.GutEnergy[i] / w.Gut[i] : 0f;
        StarveCap[g] += cap / bmr; StarveBasal[g] += basal / bmr; StarveThermo[g] += thermo / bmr; StarveMove[g] += move / bmr; StarveGrow[g] += grow / bmr;
        StarveBreath[g] += breathe / bmr; StarveBrain[g] += brain / bmr; StarveEndo[g] += w.PEndo[i]; StarveRate[g] += rateT; StarveMassShare[g] += w.MassOf(i) / w.PM[i];
    }

    public string Report()
    {
        var o = new StringBuilder();
        o.AppendLine("DIAG feeding chain per animal-step (by best-digested food, meat or a plant food, and stage)");
        for (int g = 0; g < G; g++)
        {
            double n = Math.Max(1, Steps[g]);
            o.AppendLine($"DIAG g{g} = {GName[g]}");
            o.AppendLine($"DIAG g{g}: animal-steps {Steps[g]:N0} mean mass {Mass[g] / n:F2} kg  reserve full {1 - Hunger[g] / n:P0}  gut full {Fill[g] / n:P0}");
            o.AppendLine($"DIAG g{g}: FOOD_HERE>0.5 {FoodHere[g] / n:P1}  EAT fired {EatFired[g] / n:P1} (with food here {EatFiredFood[g] / n:P1})  hungry(>0.5) and food here but no EAT {HungryNoEat[g] / n:P1}");
            o.AppendLine($"DIAG g{g}: EAT blocked: scanning {EatScan[g] / n:P1} gut full {EatFull[g] / n:P1} airborne {EatAir[g] / n:P1} nothing edible {EatNone[g] / n:P1}; bites {EatOk[g] / n:P1} of steps, mean bite {BiteKg[g] / Math.Max(1, EatOk[g]):F4} kg = {BiteE[g] / Math.Max(1, EatOk[g]):F4} E");
            o.AppendLine($"DIAG g{g}: digestion: absorbed {Absorbed[g] / n:F5} E/step; capped by absorption {AbsorbCapped[g] / n:P1} by digestion rate {DigCapped[g] / n:P1} of steps");
            double k = Math.Max(1e-12, Kleiber[g]);
            o.AppendLine($"DIAG g{g}: spend per Kleiber BMR (C_BASAL M^0.75): basal {Basal[g] / k:F2} move {Move[g] / k:F2} thermo {Thermo[g] / k:F2} breathe {Breath[g] / k:F2} grow {Grow[g] / k:F2} strike {Strike[g] / k:F2} call {Call[g] / k:F2}; intake {Absorbed[g] / k:F2}; FMR(mammal 4.82 M^0.734 kJ/d) = {Fmr[g] / k:F2} BMR");
            o.AppendLine($"DIAG g{g}: strikes {StrTry[g]:N0} in reach {StrReach[g]:N0} (prey-eat {PreyEat[g]:N0}, at kin {StrKin[g]:N0}) mean p {StrP[g] / Math.Max(1, StrReach[g]):F3} hits {StrHit[g]:N0} wounds {WoundOnly[g]:N0} kills {StrKill[g]:N0} meat eaten {StrMeatKg[g]:F1} kg = {StrMeatE[g]:F2} E");
            long all = 0; for (int c = 0; c < Cause.N; c++) all += Deaths[g * Cause.N + c];
            var dc = new StringBuilder();
            for (int c = 0; c < Cause.N; c++) if (Deaths[g * Cause.N + c] > 0) dc.Append($" {Cause.Names[c]} {Deaths[g * Cause.N + c]}");
            o.AppendLine($"DIAG g{g}: deaths {all}:{dc}");
            double sv = Math.Max(1, Deaths[g * Cause.N + Cause.Starve]);
            o.AppendLine($"DIAG g{g}: last step of the starved, per own Kleiber BMR: absorption limit {StarveCap[g] / sv:F2} basal {StarveBasal[g] / sv:F2} (brain {StarveBrain[g] / sv:F2}) thermo {StarveThermo[g] / sv:F2} move {StarveMove[g] / sv:F2} grow {StarveGrow[g] / sv:F2} breathe {StarveBreath[g] / sv:F2}; absorbed {StarveDe[g] / sv:F2}, bulk digestion x gut energy density {StarveDig[g] / sv:F2}, gut {StarveDens[g] / sv:F2} E/kg; endothermy {StarveEndo[g] / sv:F2} chemistry rate {StarveRate[g] / sv:F2} size {StarveMassShare[g] / sv:P0} of adult");
            o.AppendLine($"DIAG g{g}: struck back by a victim {RetalN[g]:N0} times, mean loss {RetalBmr[g] / Math.Max(1, RetalN[g]):F1} of its own Kleiber BMR ({RetalE[g]:F1} E in all); starved in a step it was struck back {StarveHurt[g] / sv:P0}");
            o.AppendLine($"DIAG g{g}: starved with FOOD_HERE>0.5 {StarveFood[g] / sv:P0}, mean gut fill {StarveGut[g] / sv:P0}, juveniles {StarveJuv[g] / sv:P0}, adults under 2 maturity ages {StarveYoungAdult[g] / sv:P0}");
        }
        return o.ToString();
    }
}
