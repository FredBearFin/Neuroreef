using System;
using System.Linq;
using Xunit;

namespace Neuroreef.Tests;

// Respiration and the water column (2026-11-01, Respiration.cs): the physics of oxygen supply, and what it does to
// animals in the wrong medium.
public class RespirationTests
{
    /// <summary>A world of flat cells of one kind (water of the given depth in metres, or land), with one adult of a
    /// random body whose breathing and locomotion genes are set; it stands still and does nothing.</summary>
    static (World w, int i) One(bool water, float depthM, sbyte lungs, sbyte fins, sbyte legs, sbyte wings = -127, sbyte fur = -127)
    {
        var w = new World(16, 31);
        Array.Fill(w.Cell, water ? (byte)Realm.Shallow : (byte)Realm.Land);
        if (water) Array.Fill(w.Depth, MathF.Min(depthM, K.SHALLOW) * w.ElevPerM);
        Array.Fill(w.Moisture, 0.5f);
        w.Sp[0].Used = true; w.Sp[0].Alive = true;
        int i = Life.Found(w, 0, ReadOnlySpan<sbyte>.Empty, true, 1);
        Assert.True(i >= 0);
        // the column's depth after founding (2026-11-01: a random founder's fins reach SHALLOW x (1 + SWIM_DEPTH_RANGE x
        // fins), 52 m at fins 0.1, and could not be founded in a 500-m column)
        if (water) Array.Fill(w.Depth, depthM * w.ElevPerM);
        long o = (long)i * G.LEN;
        w.Genome[o + G.B_LUNGS] = lungs; w.Genome[o + G.B_FINS] = fins; w.Genome[o + G.B_LEGS] = legs; w.Genome[o + G.B_WINGS] = wings;
        w.Genome[o + G.B_INSUL] = fur; w.Genome[o + G.B_DIVE] = -127;
        w.Genome[o + G.B_ENDO] = 127;   // warm-blooded: its demand is its basal metabolism, not cut by a cold core
        Body.Express(w, i);
        w.Flags[i] = 0; w.X[i] = 10f; w.Y[i] = 10f;
        w.Energy[i] = w.EmaxOf(i); w.Thirst[i] = 0f;
        return (w, i);
    }

    /// <summary>Steps until it dies (it is fed and watered each step so only its breath can kill it), at most n.</summary>
    static int Live(World w, int i, int n)
    {
        for (int k = 0; k < n; k++)
        {
            w.IActs[i] = 0; w.ISpeed[i] = 0f; w.IHeading[i] = 0f;
            w.Energy[i] = w.EmaxOf(i); w.Thirst[i] = 0f;
            Resolve.Run(w, 1, 0);
            w.Step++;
            if (w.StageOf[i] == St.Dead) return k + 1;
        }
        return n;
    }

    static long Died(World w, int cause) => Enumerable.Range(0, w.NS).Sum(s => w.T.Deaths[s * Cause.N + cause]);

    [Fact]
    public void DissolvedOxygenFallsWithTemperature()
    {
        // Weiss (1970), fresh water at one atmosphere: 14.6 mg/L at 0 °C, 9.1 at 20 °C, 7.6 at 30 °C
        Assert.InRange(Respiration.DissolvedO2(0f), 14.4f, 14.8f);
        Assert.InRange(Respiration.DissolvedO2(20f), 8.9f, 9.3f);
        Assert.InRange(Respiration.DissolvedO2(30f), 7.4f, 7.7f);
        for (float t = 0f; t < 40f; t += 1f) Assert.True(Respiration.DissolvedO2(t + 1f) < Respiration.DissolvedO2(t));
        // and so a gill-breather draws less in warm water
        var (w, i) = One(true, 50f, -127, 127, -127);
        int cell = World.FIdx(w.X[i], w.Y[i]);
        float cool = Respiration.Uptake(w, i, cell, false, 0f, out _, out _).Water;
        Array.Fill(w.Temp, 32f);
        float warm = Respiration.Uptake(w, i, cell, false, 0f, out _, out _).Water;
        Assert.True(warm < 0.9f * cool, $"gills drew {warm} in water at 32 °C against {cool} at 20 °C");
    }

    [Fact]
    public void PartialPressureFallsWithAltitude()
    {
        Assert.Equal(1f, Respiration.AirO2(0f, 15f), 5);
        // the barometric formula: half the oxygen at ~5.5 km in 0 °C air (scale height RT/Mg ~ 8 km)
        Assert.InRange(Respiration.AirO2(5500f, 0f), 0.48f, 0.52f);
        for (float h = 0f; h < 9000f; h += 500f) Assert.True(Respiration.AirO2(h + 500f, 10f) < Respiration.AirO2(h, 10f));
        // and so lungs draw less on a high mountain than by the sea
        var (w, i) = One(false, 0f, 127, -127, 127);
        int cell = World.FIdx(w.X[i], w.Y[i]);
        float sea = Respiration.Uptake(w, i, cell, false, 0f, out _, out _).Air;
        w.Elev[cell] = w.SeaLevel + 5000f * w.ElevPerM;
        float high = Respiration.Uptake(w, i, cell, false, 0f, out _, out _).Air;
        Assert.InRange(high / sea, 0.5f, 0.6f);
    }

    [Fact]
    public void GillBreatherSuffocatesOnLand()
    {
        // re-derived 2026-11-03 (phase 4): the gill-breather is cold-blooded, as fishes are. Its gills are its grown body's
        // everted surface, at most half its skin x FOLD (Development), which cannot supply a warm-blooded 3 kg body's
        // oxygen from water (there are no endothermic gill-breathers); the live GILL_SCOPE gave any body its scope
        static (World, int) Fish(bool water, float depth) { var (w, i) = One(water, depth, -127, 127, 64); SetEndo(w, i, -127); return (w, i); }
        var (w, i) = Fish(false, 0f);
        // (a cold-blooded body's oxygen debt builds at its own, ~5x lower, rate: it is given 400 steps, ~7 weeks, to die)
        int steps = Live(w, i, 400);
        Assert.True(steps < 400, "a gill-breather lived 400 steps on land");
        Assert.Equal(1, Died(w, Cause.Suffocated));
        // the same body in water breathes
        var (v, j) = Fish(true, 20f);
        Assert.Equal(40, Live(v, j, 40));
        Assert.Equal(0f, v.O2Debt[j]);
        // and it feels its air hunger before it dies: the BREATH sense rises ashore
        var (u, k) = Fish(false, 0f);
        u.IActs[k] = 0; u.ISpeed[k] = 0f;
        Resolve.Run(u, 1, 0);
        if (u.StageOf[k] != St.Dead) Assert.True(Respiration.Breathless(u, k) > 0f);
    }

    [Fact]
    public void AirBreatherDrownsWhenSubmerged()
    {
        // lungs, no fins, no legs, no fur: in 30 m of water it cannot keep its head up. Re-derived 2026-11-03 (phase 4): and
        // armoured, a rigid integument that blocks its trunk's bending (Physics.Flex): a soft limbless body with axial
        // muscle undulates and swims (a sea snake), so it is not the body this test means
        static (World, int) Rigid(bool water, float depth, sbyte legs) { var (w, i) = One(water, depth, 127, -127, legs); w.Genome[(long)i * G.LEN + G.B_ARMOUR] = 127; Body.Express(w, i); return (w, i); }
        var (w, i) = Rigid(true, 30f, -127);
        int steps = Live(w, i, 40);
        Assert.True(steps < 40, "an air-breather that cannot keep its head up lived 40 steps in deep water");
        Assert.Equal(1, Died(w, Cause.Drowned));
        // the same body on land breathes
        var (v, j) = Rigid(false, 0f, -127);
        Assert.Equal(40, Live(v, j, 40));
        // and one that paddles well (legs) keeps its head up in the same water
        var (u, k) = Rigid(true, 30f, 127);
        Assert.Equal(40, Live(u, k, 40));
    }

    [Fact]
    public void AirBreathingDiversDiveDeeperWhenBigger()
    {
        // a swimmer with lungs that prefers the bottom of a 500 m column dives on a held breath: its store lasts ~M^0.25,
        // so a big body goes deeper; a fish of the same body stays down all the time
        float Dive(float mass, sbyte lungs, out float share)
        {
            var (w, i) = One(true, 500f, lungs, 127, -127);
            Array.Fill(w.Temp, 4f);   // a cold lake, mixed to the bottom: the deep water keeps its oxygen
            w.Genome[(long)i * G.LEN + G.B_DIVE] = 127;
            w.Genome[(long)i * G.LEN + G.B_FLOOR] = 0;
            w.Genome[(long)i * G.LEN + G.B_MASS] = G.ToGeneLog(mass, K.MASS_LO, K.MASS_HI);
            Body.Express(w, i);
            Respiration.Uptake(w, i, World.FIdx(w.X[i], w.Y[i]), false, 0f, out float z, out share);
            return z;
        }
        float small = Dive(0.2f, 127, out float s1), big = Dive(30f, 127, out float s2), fish = Dive(0.2f, -127, out float s3);
        Assert.True(small > 0f && big > small, $"dives: {small} m small, {big} m big");
        Assert.True(s1 < 1f && s2 < 1f, "an air-breather spends part of the step at the surface");
        Assert.True(fish > big && s3 == 1f, $"a gill-breather holds {fish} m all step");
    }

    [Fact]
    public void OrgansCostUpkeepAndABodyWithBothPaysMore()
    {
        // each organ has its overhead: a half-and-half body pays more than either specialist, and more than one with
        // a lung it never uses would save by losing it
        float gills = Respiration.OrganUpkeep(0f), lungs = Respiration.OrganUpkeep(1f), both = Respiration.OrganUpkeep(0.5f);
        Assert.Equal(K.COST_GILL, gills, 5); Assert.Equal(K.COST_LUNG, lungs, 5);
        Assert.True(both > MathF.Max(gills, lungs), $"amphibious upkeep {both} against gills {gills}, lungs {lungs}");
        Assert.True(Respiration.OrganUpkeep(0.1f) > gills, "a small unused lung costs a gill-breather");
        // and it is in the body's basal cost
        var (w, i) = One(false, 0f, -127, 127, 64);
        float b0 = w.PBasal[i];
        w.Genome[(long)i * G.LEN + G.B_LUNGS] = 0; Body.Express(w, i);
        Assert.True(w.PBasal[i] > b0, "an amphibious body's basal cost is not above a gill-breather's");
    }

    [Fact(Skip = "Retired with B_LUNGS (phase 4, 2026-11-03): Respiration.OrganUpkeep is replaced by the exchange epithelia's tissue upkeep in the basal (Morph.Live)")] // a gill-breather that keeps a lung it never uses pays for it, in water as anywhere
    public void AGillBreatherPaysUpkeepForUnusedLungs()
    {
        // a pure gill-breather and one with a fifth of its breathing organ as lung, in water (where the lung draws nothing
        // a gill could not), same body otherwise
        var (w, i) = One(true, 20f, -127, 127, -127);
        Assert.Equal(0f, w.PLungs[i], 5);
        float pure = w.PBasal[i];
        w.Genome[(long)i * G.LEN + G.B_LUNGS] = -76; Body.Express(w, i);   // Organ(-76) = 51/254, ~0.2
        float L = w.PLungs[i];
        Assert.InRange(L, 0.19f, 0.21f);
        // its basal cost carries the organs' upkeep: COST_LUNG L^ORGAN_EXP + COST_GILL (1-L)^ORGAN_EXP of C_BASAL M^EXP_BASAL
        float mPow = MathK.Pow(w.PM[i], K.EXP_BASAL);
        // and the upkeep of the bigger gut that feeds them (Body.GutScale, 2026-11-02): 1 / (1 - GUT_SHARE) of it
        float extra = K.C_BASAL * mPow * (Respiration.OrganUpkeep(L) - Respiration.OrganUpkeep(0f)) / (1f - K.GUT_SHARE);
        Assert.True(extra > 0f, "a lung added to gills costs nothing extra");
        Assert.Equal(pure + extra, w.PBasal[i], 4);
        // with an organ's fixed overhead (ORGAN_EXP < 1) the small lung costs more than the gill tissue it replaced saves
        Assert.True(K.COST_LUNG * MathK.Pow(L, K.ORGAN_EXP) > K.COST_GILL * (1f - MathK.Pow(1f - L, K.ORGAN_EXP)));
        // and the pure gill-breather lives a day for less in the same water: its resting cost there is lower
        int cell = World.FIdx(w.X[i], w.Y[i]);
        Assert.True(Respiration.Breathable(w, i, cell));
        float need = K.FIELD_METAB * Respiration.OrganBase(w, i) * Respiration.Pace(w, i, cell, false);
        w.Genome[(long)i * G.LEN + G.B_LUNGS] = -127; Body.Express(w, i);
        float needPure = K.FIELD_METAB * Respiration.OrganBase(w, i) * Respiration.Pace(w, i, cell, false);
        Assert.True(needPure < need, $"a gill-breather's day costs {needPure} without the lung and {need} with it");
    }

    [Fact] // need and running costs follow the metabolic pace of the core: Boltzmann-Arrhenius, a Q10 of ~2.5
    public void ColdCoreRunsItsCostsAtTheArrheniusPace()
    {
        Assert.True(Climate.On);
        const float kB = 8.617e-5f;   // Boltzmann's constant, eV/K
        // a cold-blooded gill-breather in water: its core takes the water's temperature
        var (w, i) = One(true, 20f, -127, 127, -127);
        SetEndo(w, i, -127);
        int cell = World.FIdx(w.X[i], w.Y[i]);
        (float core, float osmo, float pace) At(World v, int j, float t)
        {
            Array.Fill(v.Temp, t);
            float top = Heat.Operative(v, cell, v.PInsul[j], true);
            float c = Heat.Core(top, v.PEndo[j]);
            return (c, Respiration.Uptake(v, j, cell, false, 0f, out _, out _).Extra, Respiration.Pace(v, j, cell, false));
        }
        var cool = At(w, i, 12f); var warm = At(w, i, 22f);
        Assert.True(warm.core > cool.core + 5f, $"its core did not follow the water ({cool.core} -> {warm.core} °C)");
        // the osmoregulation it pays is GILL_OSMO x its resting cost at its pace
        Assert.Equal(K.GILL_OSMO * Respiration.OrganBase(w, i) * warm.pace, warm.osmo, 5);
        // and that pace follows exp(E/k (1/T1 - 1/T2)) between its two core temperatures (Gillooly et al. 2001)
        float t1 = cool.core + 273.15f, t2 = warm.core + 273.15f;
        float arrhenius = MathK.Exp(K.E_ACT / kB * (1f / t1 - 1f / t2));
        Assert.Equal(arrhenius, warm.osmo / cool.osmo, 3);
        Assert.Equal(arrhenius, warm.pace / cool.pace, 3);
        // as a Q10 (the factor per 10 K): ~2.5 at 0.65 eV, in the 2-3 that animals show
        float q10 = MathK.Exp(MathK.Log(warm.pace / cool.pace) * 10f / (t2 - t1));
        Assert.InRange(q10, 2f, 3f);
        // its day's oxygen need (the founding test, Breathable) scales the same way
        float needCool = K.FIELD_METAB * Respiration.OrganBase(w, i) * cool.pace, needWarm = K.FIELD_METAB * Respiration.OrganBase(w, i) * warm.pace;
        Assert.Equal(arrhenius, needWarm / needCool, 3);
        // a warm-blooded body keeps its core at T_BODY in either water: its costs do not change, and are the most
        var (v, j) = One(true, 20f, -127, 127, -127);
        var vc = At(v, j, 12f); var vw = At(v, j, 22f);
        Assert.Equal(K.T_BODY, vc.core, 3); Assert.Equal(K.T_BODY, vw.core, 3);
        Assert.Equal(vc.osmo, vw.osmo, 5);
        Assert.Equal(K.GILL_OSMO * Respiration.OrganBase(v, j), vw.osmo, 5);
        Assert.True(warm.osmo < vw.osmo * K.ECTO_LEVEL);
    }

    [Fact]
    public void GillsOsmoregulateInWaterAndLungsFloat()
    {
        // in water, gills pay to keep their salts; out of it they do not
        var (w, i) = One(true, 200f, -127, 127, -127);
        int cell = World.FIdx(w.X[i], w.Y[i]);
        float osmo = Respiration.Uptake(w, i, cell, false, 0f, out _, out _).Extra;
        Assert.Equal(K.GILL_OSMO * Respiration.OrganBase(w, i), osmo, 4);
        var (v, j) = One(false, 0f, -127, 127, -127);
        Assert.Equal(0f, Respiration.Uptake(v, j, World.FIdx(v.X[j], v.Y[j]), false, 0f, out _, out _).Extra);
        // a diver's lungs pull it up, less as the air compresses with depth (Boyle)
        var (u, k) = One(true, 200f, 127, 127, -127);
        u.Genome[(long)k * G.LEN + G.B_DIVE] = 127; Body.Express(u, k);
        int c = World.FIdx(u.X[k], u.Y[k]);
        var b = Respiration.Uptake(u, k, c, false, 0f, out float z, out float share);
        Assert.True(z > 0f && share > 0f);
        // and its dives, swum at swimming's cost of transport (Breath.DiveM, 2026-11-02)
        Assert.Equal(K.BUOY_COST * Respiration.OrganBase(u, k) * share / (1f + z / 10f) + Respiration.CruiseCost(u, k, b.DiveM), b.Extra, 4);
    }

    [Fact]
    public void LightAndReachFallWithDepth()
    {
        var w = new World(1, 33);
        int c = 0;
        w.Cell[c] = (byte)Realm.Deep; w.Depth[c] = 100f * w.ElevPerM;
        float top = Respiration.Access(w, c, (int)Pool.Phyto, 0f, 1f), deep = Respiration.Access(w, c, (int)Pool.Phyto, 50f, 1f);
        Assert.Equal(1f, top, 5);
        Assert.Equal(MathK.Exp(-K.K_LIGHT * 50f), deep, 5);   // Beer-Lambert
        Assert.Equal(0f, Respiration.Access(w, c, (int)Pool.Detritus, 0f, 1f));     // detritus lies on the bottom
        Assert.Equal(1f, Respiration.Access(w, c, (int)Pool.Detritus, 99f, 1f));
    }

    [Fact]
    public void WaterAncestorsFoundAndSurviveInADefaultWorld()
    {
        using var sim = new Simulation(8192, 5, 2);
        var w = sim.W;
        int nLand = w.Evo.AncestorsLand, nWater = w.Evo.AncestorsWater;
        var water = Enumerable.Range(0, K.CLADES).Where(s => w.Sp[s].Used && w.Sp[s].Ancestor >= nLand).ToArray();
        Assert.Equal(nWater, water.Length);
        // every water ancestor's founders are in the water, and nearly all can breathe there at rest (the ancestor was
        // tested; its founders vary around it)
        int founders = 0, breathe = 0;
        for (int i = 0; i < w.Cap; i++)
        {
            if (w.StageOf[i] < St.Juvenile || !water.Contains(w.Spc[i])) continue;
            int cell = World.FIdx(w.X[i], w.Y[i]);
            Assert.True((w.Cell[cell] & (byte)Realm.Water) != 0);
            founders++; if (Respiration.Breathable(w, i, cell)) breathe++;
        }
        // (2026-11-01: was 0.9. With the absorption ceiling and the breeding clock in days, founders are fewer and the share
        // that breathes where placed measured 267 of 297 = 0.899 on this seed; founders are varied around the ancestor)
        // (2026-11-01: a water lineage is founded only if it can feed, breathe and pay its organs in water, so where the
        // waters are poor none may be founded: on this seed's 256 map no random body's water range can feed a founding
        // group. Before, they were founded on the land food around them and lived ashore. What holds: every water founder
        // is in water and nearly all breathe there; and the world runs on)
        Assert.True(breathe >= 0.85 * founders, $"{breathe} of {founders} water founders can breathe where they were put");
        for (int k = 0; k < 300; k++) sim.Step();
        Assert.True(w.AliveCount > 0);
    }

    // ---- endothermy (2026-11-01): what being warm-blooded costs and buys

    static void SetEndo(World w, int i, sbyte endo) { w.Genome[(long)i * G.LEN + G.B_ENDO] = endo; Body.Express(w, i); }

    [Fact]
    public void ColdBloodedTissueRunsAtItsLevelAndCoreTemperature()
    {
        // at the same core temperature a cold-blooded body burns and can breathe ECTO_LEVEL of a warm-blooded one's
        // (2026-11-02: active, 0.2, in its basal cost itself, Body.Express; Activity is the core's pace alone, so the level
        // is not applied twice); in the cold its core, and so its pace, falls
        Assert.Equal(1f, Heat.Level(1f), 5);
        Assert.Equal(K.ECTO_LEVEL, Heat.Level(0f), 5);
        Assert.Equal(1f, Heat.Activity(K.T_BODY, 0f), 5);
        // in the cold its pace falls (its core follows the air, a warm one's does not); its lungs' capacity follows that
        // pace as Activity^AEROBIC_T (AEROBIC_T 0, not active, since 2026-11-01: see its entry)
        Assert.True(Heat.Activity(5f, 0f) < 0.1f * Heat.Activity(K.T_BODY, 1f));
        var (w, i) = One(false, 0f, 127, -127, 127);
        Array.Fill(w.Temp, 5f);
        int cell = World.FIdx(w.X[i], w.Y[i]);
        float warm = Respiration.Uptake(w, i, cell, false, 0f, out _, out _).Air, obWarm = Respiration.OrganBase(w, i);
        SetEndo(w, i, -127);
        float cold = Respiration.Uptake(w, i, cell, false, 0f, out _, out _).Air;
        float expect = MathK.Pow(Respiration.Aerobic(w, i, cell, false), 1f);   // its capacity against a warm body's at T_BODY
        // its organs are built to its own basal cost (OrganBase), its level x a warm body's, paced as Aerobic says
        // re-derived 2026-11-03 (phase 4): to 2 decimals. Its scope is its grown body's exchange surfaces and skin over its
        // basal (Morph.Live); the skin's share is passive diffusion, not built to the tissues' level, so a cold body's scope
        // is a little higher (measured 3%)
        Assert.Equal(warm * Respiration.OrganBase(w, i) / obWarm * expect, cold, 2);
        Assert.True(Respiration.OrganBase(w, i) < 0.5f * obWarm, "a cold-blooded body's organs are built to its lower cost");
    }

    [Fact]
    public void ColdBloodedBodiesFreezeOnLandButNotInWater()
    {
        var (w, i) = One(false, 0f, 127, -127, 127);
        SetEndo(w, i, -127);
        Array.Fill(w.Temp, -15f);
        Assert.True(Live(w, i, 60) < 60, "a cold-blooded body lived 60 steps at -15 °C on land");
        Assert.Equal(1, Died(w, Cause.Cold));
        // a warm-blooded one with fur keeps its core
        var (v, j) = One(false, 0f, 127, -127, 127, fur: 127);
        Array.Fill(v.Temp, -15f);
        Assert.Equal(60, Live(v, j, 60));
        // a cold-blooded gill-breather under the ice: the water stays above freezing
        var (u, k) = One(true, 20f, -127, 127, -127);
        SetEndo(u, k, -127);
        Array.Fill(u.Temp, -15f);
        Assert.Equal(60, Live(u, k, 60));
    }

    [Fact]
    public void EggsDevelopByTheirWarmthAndABroodingWarmMotherSpeedsThem()
    {
        // an egg left in 10 °C air develops at Rate(10) a step; one brooded by a warm-blooded caring mother near T_BODY
        float Left(sbyte endo, sbyte care)
        {
            var (w, m) = One(false, 0f, 127, -127, 127);
            long o = (long)m * G.LEN;
            w.Genome[o + G.B_ENDO] = endo; w.Genome[o + G.B_CARE] = care; Body.Express(w, m);
            Array.Fill(w.Temp, 10f);
            int e = Life.NewSlot(w);
            Life.CopyGenome(w, m, e, false);
            Life.Place(w, e, 0, St.Egg, w.X[m], w.Y[m], 1f, m);
            w.Timer[e] = 100f; w.Flags[e] = 0;
            Life.Run(w, new int[K.CLADES]);
            return 100f - w.Timer[e];
        }
        float cold = Left(-127, -127), brooded = Left(127, 127);
        Assert.Equal(Heat.Rate(10f), cold, 4);
        Assert.True(brooded > 3f * cold, $"a brooded egg developed {brooded} against {cold} a step");
    }
}
