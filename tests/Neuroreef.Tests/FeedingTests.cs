// The feeding chain in real units (2026-11-01): a grazer beside food gains energy, a predator that catches prey gains
// the carcass's energy, tissue holds the energy of wet meat, a matched predator kills what it catches, prey and plants
// are compared by what a bite of each gives, and a victim that strikes back kills its attacker as a kill, not starvation.

using System;
using Xunit;

namespace Neuroreef.Tests;

public class FeedingTests
{
    /// <summary>A walker of mass gene 0 that digests only the given pool (and carrion, if it eats meat).</summary>
    static int Eater(World w, int clade, uint key, Pool food, sbyte strike = -127, sbyte armour = -127)
    {
        w.Sp[clade].Used = true; w.Sp[clade].Alive = true;
        int c = Life.Found(w, clade, ReadOnlySpan<sbyte>.Empty, true, key + 1);
        Assert.True(c >= 0);
        int g = c * G.LEN;
        w.Genome[g + G.B_LEGS] = 100; w.Genome[g + G.B_FINS] = -127; w.Genome[g + G.B_WINGS] = -127;   // a walker
        w.Genome[g + G.B_MASS] = 0; w.Genome[g + G.B_FLOOR] = 1;
        for (int p = 0; p < Pools.N; p++) w.Genome[g + G.B_DIET + p] = -127;
        w.Genome[g + G.B_DIET + (int)food] = 127;
        w.Genome[g + G.B_STRIKE] = strike; w.Genome[g + G.B_ARMOUR] = armour;
        w.Genome[g + G.B_ENDO] = 127; w.Genome[g + G.B_INSUL] = 0;
        Body.Express(w, c);
        w.Flags[c] = 0; w.StageOf[c] = St.Adult; w.Grown[c] = 1f;
        w.Age[c] = w.PMature[c] * 2;
        w.Gut[c] = w.GutEnergy[c] = w.GutWaste[c] = 0f; w.Thirst[c] = 0f; w.Fatigue[c] = 0f;
        return c;
    }

    static int LandCell(World w)
    {
        for (int c = 0; c < Map.FW * Map.FW; c++)
            if (w.Cell[c] == (byte)Realm.Land && w.Fire[c] == 0 && w.DistDrink[c] > 3f) return c;
        throw new InvalidOperationException("no land");
    }

    static void Put(World w, int i, int cell, float dx = 0.5f)
    {
        w.X[i] = (cell % Map.FW) * K.FCELL + dx; w.Y[i] = (cell / Map.FW) * K.FCELL + 0.5f;
    }

    static void Act(World w, int i, int prim, int target = -1)
    {
        w.IActs[i] = prim < 0 ? 0u : 1u << (prim - P.FIRST_ACTION);
        w.ITarget[i] = target; w.IMate[i] = -1; w.IScanning[i] = 0;
        w.IHeading[i] = w.Heading[i]; w.ISpeed[i] = 0;
    }

    [Fact] // 2026-11-02: a gut is built to its body's basal cost: a Kleiber body keeps the reference gut, a costlier one a bigger gut that pays its own upkeep, up to GUT_FLEX_MAX
    public void GutIsBuiltToItsBodysBasalCost()
    {
        float k = K.C_BASAL * MathK.Pow(3f, K.EXP_BASAL);
        // the reference body: the reference gut, and its basal cost unchanged
        Assert.Equal(1f, Body.GutScale(k, k, out float b1), 5);
        Assert.Equal(k, b1, 6);
        // a body costing twice Kleiber with the reference gut: the gut grows until its absorption is C_ABSORB / C_BASAL of
        // the whole cost, its own upkeep included, and that upkeep is GUT_SHARE x Kleiber per reference gut
        float g2 = Body.GutScale(2f * k, k, out float b2);
        Assert.True(g2 > 1f && g2 < K.GUT_FLEX_MAX);
        Assert.Equal(b2, k * g2, 6);                                       // absorption C_ABSORB x M^0.75 x g = scope x its own basal
        Assert.Equal(2f * k + K.GUT_SHARE * (g2 - 1f) * k, b2, 6);          // it pays for the gut it adds
        Assert.Equal(k + k / (1f - K.GUT_SHARE), b2, 5);                    // an organ's cost is raised by 1 / (1 - GUT_SHARE)
        // a very costly body: the gut stops at GUT_FLEX_MAX, so it absorbs less than the scope against its cost
        float g9 = Body.GutScale(9f * k, k, out float b9);
        Assert.Equal(K.GUT_FLEX_MAX, g9, 5);
        Assert.Equal(9f * k + K.GUT_SHARE * (K.GUT_FLEX_MAX - 1f) * k, b9, 5);
        Assert.True(k * g9 < b9);
        // a cheap body builds a smaller gut and saves its upkeep
        Assert.True(Body.GutScale(0.6f * k, k, out float b06) < 1f && b06 < 0.6f * k);
    }

    [Fact] // 2026-11-02: an expressed body's absorption and digestion are its gut's: a big brain brings a bigger gut at a higher basal cost, and that gut takes up no more than it is given
    public void ABigBrainedBodyAbsorbsInProportionToItsCost()
    {
        float climate = Nature.Climate; Nature.Climate = 0f;
        try
        {
            var w = new World(256, 23); Terrain.Build(w);
            int cell = LandCell(w);
            int a = Eater(w, 0, 1, Pool.Seed);
            Put(w, a, cell);
            float m = w.PM[a], k = K.C_BASAL * MathK.Pow(m, K.EXP_BASAL);
            float basal0 = w.PBasal[a], absorb0 = w.PAbsorb[a], dig0 = w.PDig[a], brain0 = K.COST_BRAIN * K.C_BASAL * w.PBrain[a] / K.BRAIN_REF;
            Assert.Equal(K.C_ABSORB / K.C_BASAL * basal0, absorb0, 5);
            w.Genome[a * G.LEN + G.B_BRAIN] = Body.UnitsGene(Body.UnitsAffordable(m) * 0.8f); Body.Express(w, a);
            float brain = K.COST_BRAIN * K.C_BASAL * w.PBrain[a] / K.BRAIN_REF;
            Assert.True(brain - brain0 > 0.1f * k, $"a big brain: {brain / k:F2} of Kleiber, from {brain0 / k:F2}");
            // its cost: the brain, and the gut that feeds it
            Assert.Equal(basal0 + (brain - brain0) / (1f - K.GUT_SHARE), w.PBasal[a], 5);
            // its gut absorbs and digests in proportion, and holds no more
            Assert.Equal(K.C_ABSORB / K.C_BASAL * w.PBasal[a], w.PAbsorb[a], 4);
            Assert.Equal(w.PAbsorb[a] / absorb0, w.PDig[a] / dig0, 3);
            Assert.Equal(K.C_GUT * m, w.PGut[a], 6);
            // a gut full of seeds: it takes up at most its absorption a step, all of it from the gut
            w.Gut[a] = w.PGut[a]; w.GutEnergy[a] = w.Gut[a] * K.RHO_SEED * 0.75f; w.GutWaste[a] = 0f; w.Energy[a] = 0.3f * w.EmaxOf(a);
            float g0 = w.GutEnergy[a];
            Act(w, a, P.REST); w.IActs[a] = 0;
            Resolve.Run(w, 1, 0);
            float took = g0 - w.GutEnergy[a];
            Assert.True(took > absorb0 && took <= w.PAbsorb[a] * 1.0001f, $"absorbed {took} a step; its gut's limit {w.PAbsorb[a]}, the small-brained body's {absorb0}");
        }
        finally { Nature.Climate = climate; }
    }

    [Fact] // a hungry grazer standing in grass senses it, bites it and gains energy step after step
    public void HerbivoreBesideFoodGainsEnergy()
    {
        float climate = Nature.Climate; Nature.Climate = 0f;
        try
        {
            var w = new World(256, 21); Terrain.Build(w);
            int cell = LandCell(w);
            w.Pool[(int)Pool.Grass][cell] = 0.3f;
            int a = Eater(w, 0, 1, Pool.Grass);
            Put(w, a, cell);
            float e0 = 0.3f * w.EmaxOf(a);
            w.Energy[a] = e0;
            Simulation.BuildFor(w);
            Decide.Run(w, a, new Decide.ProbeBuf());
            Assert.True(w.IFoodHere[a] > 0.5f, $"FOOD_HERE {w.IFoodHere[a]} in a meadow");
            float grass0 = w.Pool[(int)Pool.Grass][cell];
            for (int t = 0; t < 40; t++) { Act(w, a, P.EAT); w.IFoodHere[a] = 1f; Resolve.Run(w, 1, 0); w.Step++; }
            Assert.NotEqual(St.Dead, w.StageOf[a]);
            Assert.True(w.Energy[a] > e0 + 5f * 40f * w.PBasal[a] * 0.2f, $"energy {e0} -> {w.Energy[a]} over 40 steps of eating");
            Assert.True(w.Pool[(int)Pool.Grass][cell] < grass0, "the grass it ate is gone from the cell");
        }
        finally { Nature.Climate = climate; }
    }

    [Fact] // a kilogram of live body holds ~7 MJ (wet tissue); growing it costs that over the growth efficiency (~9 MJ, not 24),
           // and a carcass's edible half holds 3.5 MJ per kg of body; meat in the gut is on the plant pools' (dry-matter-equivalent) basis
    public void TissueHoldsTheEnergyOfWetMeat()
    {
        Assert.Equal(7f, K.TISSUE_E * Pools.EnergyJ / 1e6f, 2);
        float perKg = Resolve.TissueCost(1f, 1f) * Pools.EnergyJ / 1e6f;
        Assert.InRange(perKg, 8.5f, 10f);
        Assert.Equal(3.5f, Resolve.CarcassKg(1f, 0f) * K.RHO_MEAT * Pools.EnergyJ / 1e6f, 2);
        // a pool kilogram of plants is RHO_PLANT / PLANT_KJ_G = 1.58 kg of dry matter; dry meat (~23 kJ/g) on that basis is RHO_MEAT
        float poolKgDry = K.RHO_PLANT * Pools.EnergyJ / 1e6f / K.PLANT_KJ_G;
        Assert.InRange(K.RHO_MEAT * Pools.EnergyJ / 1e6f / poolKgDry, 20f, 25f);
    }

    [Fact] // a predator matched to its prey kills what it catches: one landed hit takes a fed prey of its size and armour
    public void AMatchedPredatorKillsWhatItCatches()
    {
        var w = new World(256, 23); Terrain.Build(w);
        int cell = LandCell(w);
        int a = Eater(w, 0, 1, Pool.Meat, strike: 0);                 // strike 1 (of 2)
        int v = Eater(w, 1, 2, Pool.Grass, armour: -127);              // no armour: defence = mass x DEF_BASE
        Put(w, a, cell, 0.5f); Put(w, v, cell, 0.6f);
        Assert.True(w.PStrike[a] * w.PM[a] >= w.PM[v] * (K.DEF_BASE + w.PArmour[v]));
        w.Energy[a] = 0.5f * w.EmaxOf(a); w.Energy[v] = w.EmaxOf(v);
        int hits = 0;
        for (int t = 0; t < 200 && w.StageOf[v] != St.Dead; t++)
        {
            Act(w, a, P.STRIKE, v); Act(w, v, -1);
            float ev = w.Energy[v];
            Resolve.Run(w, 1, 0); w.Step++;
            if (w.StageOf[v] != St.Dead && w.Energy[v] < ev - 1e-4f - w.PBasal[v] * 2f) hits++;
            w.Energy[a] = 0.5f * w.EmaxOf(a); w.Gut[a] = w.GutEnergy[a] = w.GutWaste[a] = 0f;
        }
        Assert.Equal(St.Dead, w.StageOf[v]);
        Assert.Equal(0, hits);   // no wound short of death: the first landed hit killed it
    }

    [Fact] // a predator that kills gains the carcass: its edible tissue at RHO_MEAT and the victim's reserve, in proportion to what fits its gut
    public void APredatorThatCatchesPreyGainsTheCarcassEnergy()
    {
        var w = new World(256, 25); Terrain.Build(w);
        int cell = LandCell(w);
        int a = Eater(w, 0, 1, Pool.Meat, strike: 127);
        int v = Eater(w, 1, 2, Pool.Grass);
        Put(w, a, cell, 0.5f); Put(w, v, cell, 0.6f);
        w.Energy[a] = 0.3f * w.EmaxOf(a);
        float ev = 0.6f * w.EmaxOf(v); w.Energy[v] = ev;
        float eff = w.DietOf(a)[(int)Pool.Meat];
        for (int t = 0; t < 200 && w.StageOf[v] != St.Dead; t++)
        {
            Act(w, a, P.STRIKE, v); Act(w, v, -1);
            w.Gut[a] = w.GutEnergy[a] = w.GutWaste[a] = 0f; w.Energy[a] = 0.3f * w.EmaxOf(a);
            ev = w.Energy[v];
            Resolve.Run(w, 1, 0); w.Step++;
        }
        Assert.Equal(St.Dead, w.StageOf[v]);
        // what it swallowed this step (the gut after one step's digestion, plus what digestion passed into its reserve)
        float eat = MathF.Min(w.PGut[a], Resolve.CarcassKg(w.PM[v], ev));
        float meal = eat * K.RHO_MEAT * eff;
        float got = w.GutEnergy[a] + (w.Energy[a] - 0.3f * w.EmaxOf(a)) + w.PBasal[a] * 2f;
        Assert.True(got > 0.8f * meal && w.GutEnergy[a] <= meal * 1.0001f, $"meal {meal} E, in gut and reserve {got} E");
    }

    [Fact] // prey and plants are compared by what a bite of each gives: a meat-eater in a meadow goes for the prey in reach,
           // a grazer does not, and a grazer's way to food does not lead to the other animal
    public void PreyAndPlantsAreComparedByTheirEnergy()
    {
        float climate = Nature.Climate; Nature.Climate = 0f;
        try
        {
            var w = new World(256, 27); Terrain.Build(w);
            int cell = LandCell(w);
            for (int c = 0; c < Map.FW * Map.FW; c++) if (w.Cell[c] == (byte)Realm.Land) w.Pool[(int)Pool.Grass][c] = 0.3f;
            int hunter = Eater(w, 0, 1, Pool.Meat, strike: 127);
            int grazer = Eater(w, 1, 2, Pool.Grass);
            w.Genome[grazer * G.LEN + G.B_DIET + (int)Pool.Meat] = -70; Body.Express(w, grazer);   // a little meat, as ancestors digest (genes -90..-50)
            Assert.True(w.DietOf(grazer)[(int)Pool.Meat] > 0f);
            int prey = Eater(w, 2, 3, Pool.Grass);
            for (int k = G.B_LEGS; k <= G.B_FINN; k++) w.Genome[prey * G.LEN + k] = (sbyte)(k == G.B_LEGS ? 100 : -127);
            Put(w, hunter, cell, 0.5f); Put(w, grazer, cell, 0.5f); Put(w, prey, cell, 0.9f);
            w.Y[grazer] += 0.02f;
            foreach (int i in new[] { hunter, grazer, prey }) { w.Energy[i] = 0.3f * w.EmaxOf(i); w.Heading[i] = 0f; }
            Simulation.BuildFor(w);
            Decide.Run(w, hunter, new Decide.ProbeBuf());
            Decide.Run(w, grazer, new Decide.ProbeBuf());
            Assert.Equal(1, w.IEatPrey[hunter]);
            Assert.Equal(0, w.IEatPrey[grazer]);
            Assert.True(w.Feat[grazer * F.N + F.FOOD_GRAD] < 0.05f, $"grazer's food gradient {w.Feat[grazer * F.N + F.FOOD_GRAD]} toward another animal");
        }
        finally { Nature.Climate = climate; }
    }

    [Fact] // a victim that strikes back and takes the last of its attacker's reserve kills it: a kill, not starvation
    public void DeathByRetaliationIsAKill()
    {
        var w = new World(256, 29); Terrain.Build(w);
        int cell = LandCell(w);
        int a = Eater(w, 0, 1, Pool.Meat, strike: -100);   // a small weapon (a strike needs one)
        w.Genome[a * G.LEN + G.B_MASS] = -127; w.Genome[a * G.LEN + G.B_FLOOR] = 0; Body.Express(w, a);
        int v = Eater(w, 1, 2, Pool.Grass, strike: 127, armour: 127);
        w.Genome[v * G.LEN + G.B_MASS] = 127; w.Genome[v * G.LEN + G.B_FLOOR] = 3; Body.Express(w, v);
        Put(w, a, cell, 0.5f); Put(w, v, cell, 0.6f);
        w.Energy[v] = w.EmaxOf(v);
        long killed = 0, starved = 0;
        for (int t = 0; t < 400 && w.StageOf[a] != St.Dead; t++)
        {
            Act(w, a, P.STRIKE, v); Act(w, v, -1);
            w.Energy[a] = 0.02f * w.EmaxOf(a); w.Thirst[a] = 0f;
            Resolve.Run(w, 1, 0); w.Step++;
        }
        for (int s = 0; s < w.NS; s++) { killed += w.T.Deaths[s * Cause.N + Cause.Killed]; starved += w.T.Deaths[s * Cause.N + Cause.Starve]; }
        Assert.Equal(St.Dead, w.StageOf[a]);
        var causes = new long[Cause.N]; for (int s = 0; s < w.NS; s++) for (int c = 0; c < Cause.N; c++) causes[c] += w.T.Deaths[s * Cause.N + c];
        Assert.True(killed == 1, "deaths by cause: " + string.Join(" ", causes));
        Assert.Equal(0, starved);
    }
}
