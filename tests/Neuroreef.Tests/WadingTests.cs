using System;
using Xunit;
using Xunit.Abstractions;

namespace Neuroreef.Tests;

/// <summary>
/// Standing and walking in water (2026-11-02): a body stands only where the water is shallower than it stands
/// (Respiration.StandM); wading costs more a metre as the water rises up its legs (World.WadeFactor: Gleim &amp; Nicholas
/// 1989; Masumoto et al. 2004; Halsey 2016); deeper, it floats and swims. Wings carried by a body that never flies are
/// paid for and give it nothing in the water.
/// </summary>
public class WadingTests
{
    readonly ITestOutputHelper _out;
    public WadingTests(ITestOutputHelper o) { _out = o; }

    /// <summary>A small world, its slot 0 a legged, finless, wingless body of this mass gene, and a water cell whose depth
    /// the test sets (DepthAt).</summary>
    static (World w, int water, int land) Body0(sbyte mass = 0, sbyte wings = -127)
    {
        var w = new World(16, 3);
        Terrain.Build(w);
        w.Genome[G.B_LEGS] = 127; w.Genome[G.B_FINS] = -127; w.Genome[G.B_WINGS] = wings; w.Genome[G.B_MASS] = mass;
        w.Genome[G.B_INSUL] = -127;
        w.Spc[0] = 0; w.StageOf[0] = St.Adult; Body.Express(w, 0);
        int water = -1, land = -1;
        for (int c = 0; c < Map.FW * Map.FW; c++)
        {
            if (water < 0 && (w.Cell[c] & (byte)Realm.Water) != 0) water = c;
            if (land < 0 && w.Cell[c] == (byte)Realm.Land && w.SlopeSin(c) < 0.05f) land = c;
        }
        Assert.True(water >= 0 && land >= 0);
        return (w, water, land);
    }

    static void DepthAt(World w, int cell, float m) => w.Depth[cell] = m * w.ElevPerM;

    [Fact] // wading costs more a metre as the water rises up the legs: ~land's at the ankle, 2-3x at the hip (Gleim & Nicholas 1989)
    public void WadingCostRisesWithDepthOverLegLength()
    {
        var (w, water, land) = Body0();
        float leg = Respiration.LegM(w, 0), stand = Respiration.StandM(w, 0), m = w.MassOf(0);
        float spd = 0.3f * w.GroundVmax(0, land);
        float onLand = Resolve.MoveCost(w, 0, m, 1f, spd, land, false);
        float last = onLand, vLast = w.GroundVmax(0, land);
        foreach (float r in new[] { 0.1f, 0.3f, 0.6f, 1f })
        {
            DepthAt(w, water, r * leg);
            float c = Resolve.MoveCost(w, 0, m, 1f, spd, water, false), v = w.GroundVmax(0, water);
            _out.WriteLine($"{m:F2} kg, legs {leg:F3} m, stands {stand:F3} m: depth {r:F1} x leg: cost {c / onLand:F2} x land, top speed {v / w.GroundVmax(0, land):F2} x land");
            Assert.True(c > last, $"dearer as it deepens ({r})");
            Assert.True(v < vLast, $"slower as it deepens ({r})");
            last = c; vLast = v;
        }
        DepthAt(w, water, leg);
        Assert.InRange(Resolve.MoveCost(w, 0, m, 1f, spd, water, false) / onLand, 2f, 3.2f);   // at the hip
        DepthAt(w, water, 0.1f * leg);
        Assert.InRange(Resolve.MoveCost(w, 0, m, 1f, spd, water, false) / onLand, 1f, 1.1f);   // at the ankle
        Assert.Equal(1f + K.WADE_DRAG, World.WadeFactor(5f, 1f));   // deeper than the hip it stays there (buoyancy)
        // the same water costs a long-legged body less: 0.2 m is a heron's ankle and a rat's back
        var big = Body0(mass: 100);
        DepthAt(big.w, big.water, 0.2f * leg);
        float bigLeg = Respiration.LegM(big.w, 0);
        Assert.True(bigLeg > 2f * leg);
        Assert.True(World.WadeFactor(0.2f * leg, bigLeg) < World.WadeFactor(0.2f * leg, leg));
    }

    [Fact] // no standing in water deeper than the body stands: it floats and swims, at swimming's speed and cost, and from the surface reaches no deeper than it is tall
    public void NoStandingDeeperThanTheBodyAllows()
    {
        var (w, water, _) = Body0(mass: -60);
        float stand = Respiration.StandM(w, 0), m = w.MassOf(0);
        DepthAt(w, water, 0.9f * stand);
        Assert.True(w.StandsIn(0, water));
        float spd = 0.5f * w.PVWater[0];
        DepthAt(w, water, 1.5f);   // a 1.5 m bank, many times its height
        _out.WriteLine($"{m:F2} kg stands {stand:F3} m; in 1.5 m: top speed {w.GroundVmax(0, water):E3} (swimming {w.PVWater[0]:E3}, land {w.PVLand[0]:E3})");
        Assert.True(stand < 0.5f);
        Assert.False(w.StandsIn(0, water));
        Assert.True(w.GroundVmax(0, water) <= w.PVWater[0] * 1.0001f, "it swims: no wading speed");
        float swim = K.C_MOVE * MathK.Pow(m, K.EXP_MOVE) * spd;
        Assert.Equal(swim, Resolve.MoveCost(w, 0, m, 1f, spd, water, false), 5);
        // the bottom's food: not from the surface where the water is deeper than it stands; within reach where it is not
        int alg = (int)Pool.Algae;
        Assert.Equal(0f, Respiration.Access(w, water, alg, 0f, 0f, stand));
        DepthAt(w, water, 0.5f * stand);
        Assert.Equal(1f, Respiration.Access(w, water, alg, 0f, 0f, stand));
        // (the Access a body is given reads its own height)
        w.Z[0] = 0f; w.ZShare[0] = 0f;
        DepthAt(w, water, 1.5f);
        Assert.Equal(0f, Respiration.Access(w, 0, water, alg));
    }

    [Fact] // a flier that never flies pays its wings' upkeep (COST_WING and the gut that serves it) and gets nothing for them in the water
    public void AFlierThatNeverFliesPaysItsWingUpkeep()
    {
        var bare = Body0(mass: -40);
        var wing = Body0(mass: -40, wings: 127);
        Assert.Equal(Loco.Fly, wing.w.LocoOf(0));
        float wo = wing.w.PWings[0];
        // its tissues' metabolic level (Heat.Level, 2026-11-02) scales the wings' upkeep with the rest of its basal cost
        float kleiber = K.C_BASAL * MathK.Pow(wing.w.PM[0], K.EXP_BASAL) * wing.w.Dt * Heat.Level(wing.w.PEndo[0]);
        float extra = wing.w.PBasal[0] - bare.w.PBasal[0];
        _out.WriteLine($"{wing.w.PM[0]:F2} kg, wings {wo:F2}: basal {bare.w.PBasal[0]:E3} -> {wing.w.PBasal[0]:E3} (+{100 * extra / bare.w.PBasal[0]:F0}%; COST_WING x wings alone {100 * K.COST_WING * wo:F0}% of Kleiber's)");
        Assert.True(extra >= K.COST_WING * wo * kleiber * 0.999f, "the wings' upkeep at least");
        // in water: the same height (no reach for wings), no faster, and every metre at least as dear
        foreach (float d in new[] { 0.02f, 0.06f, 1.5f })
        {
            DepthAt(bare.w, bare.water, d); DepthAt(wing.w, wing.water, d);
            Assert.Equal(Respiration.StandM(bare.w, 0), Respiration.StandM(wing.w, 0));
            Assert.True(wing.w.GroundVmax(0, wing.water) <= bare.w.GroundVmax(0, bare.water) * 1.0001f, $"no faster at {d} m");
            float spd = 0.5f * bare.w.GroundVmax(0, bare.water);
            Assert.True(Resolve.MoveCost(wing.w, 0, wing.w.MassOf(0), 1f, spd, wing.water, false) >= Resolve.MoveCost(bare.w, 0, bare.w.MassOf(0), 1f, spd, bare.water, false) * 0.999f);
        }
    }
}
