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
        if (water) Array.Fill(w.Depth, depthM * w.ElevPerM);
        Array.Fill(w.Moisture, 0.5f);
        w.Sp[0].Used = true; w.Sp[0].Alive = true;
        int i = Life.Found(w, 0, ReadOnlySpan<sbyte>.Empty, true, 1);
        Assert.True(i >= 0);
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
        var (w, i) = One(false, 0f, -127, 127, 64);
        int steps = Live(w, i, 40);
        Assert.True(steps < 40, "a gill-breather lived 40 steps on land");
        Assert.Equal(1, Died(w, Cause.Suffocated));
        // the same body in water breathes
        var (v, j) = One(true, 20f, -127, 127, 64);
        Assert.Equal(40, Live(v, j, 40));
        Assert.Equal(0f, v.O2Debt[j]);
        // and it feels its air hunger before it dies: the BREATH sense rises ashore
        var (u, k) = One(false, 0f, -127, 127, 64);
        u.IActs[k] = 0; u.ISpeed[k] = 0f;
        Resolve.Run(u, 1, 0);
        if (u.StageOf[k] != St.Dead) Assert.True(Respiration.Breathless(u, k) > 0f);
    }

    [Fact]
    public void AirBreatherDrownsWhenSubmerged()
    {
        // lungs, no fins, no legs, no fur: in 30 m of water it cannot keep its head up
        var (w, i) = One(true, 30f, 127, -127, -127);
        int steps = Live(w, i, 40);
        Assert.True(steps < 40, "an air-breather that cannot keep its head up lived 40 steps in deep water");
        Assert.Equal(1, Died(w, Cause.Drowned));
        // the same body on land breathes
        var (v, j) = One(false, 0f, 127, -127, -127);
        Assert.Equal(40, Live(v, j, 40));
        // and one that paddles well (legs) keeps its head up in the same water
        var (u, k) = One(true, 30f, 127, -127, 127);
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
        Assert.True(founders > 0 && breathe >= 0.9 * founders, $"{breathe} of {founders} water founders can breathe where they were put");
        for (int k = 0; k < 300; k++) sim.Step();
        int alive = water.Count(s => w.Sp[s].Alive);
        Assert.True(alive >= nWater / 2, $"{alive} of {nWater} water lineages alive after 300 steps");
    }
}
