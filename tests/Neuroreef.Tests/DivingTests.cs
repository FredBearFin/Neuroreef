using System;
using Xunit;
using Xunit.Abstractions;

namespace Neuroreef.Tests;

/// <summary>
/// Feeding and chasing in the water column (2026-11-02): plankton is taken from the water a body sweeps at the depth it
/// is (Respiration.PlanktonKg), dives are swum and paid for (Breath.DiveM), a chase runs at the speed of the medium
/// (World.ReachOf), and wings stroke under water as flippers, best at a size too small to fly a heavy body
/// (Body.Propulsor).
/// </summary>
public class DivingTests
{
    readonly ITestOutputHelper _out;
    public DivingTests(ITestOutputHelper o) { _out = o; }

    static (World w, int cell) Sea(sbyte mass, sbyte fins, sbyte wings, float colM)
    {
        return OceanTests.Sea(() =>
        {
            var w = new World(16, 3);
            Terrain.Build(w);
            w.Genome[G.B_MASS] = mass; w.Genome[G.B_FINS] = fins; w.Genome[G.B_WINGS] = wings; w.Genome[G.B_LEGS] = -127;
            w.Genome[G.B_DIVE] = 127; w.Genome[G.B_LUNGS] = 127; w.Genome[G.B_INSUL] = -127;
            w.Spc[0] = 0; w.StageOf[0] = St.Adult; Body.Express(w, 0);
            int cell = -1;
            for (int c = 0; c < Map.FW * Map.FW && cell < 0; c++) if ((w.Cell[c] & (byte)Realm.Water) != 0) cell = c;
            w.Depth[cell] = colM * w.ElevPerM;
            w.X[0] = (cell % Map.FW + 0.5f) * K.FCELL; w.Y[0] = (cell / Map.FW + 0.5f) * K.FCELL;
            w.Pool[(int)Pool.Phyto][cell] = 0.3f;
            return (w, cell);
        });
    }

    [Fact] // plankton: what lies in the water its gape sweeps, at the depth it is, never the whole column at a bite
    public void PlanktonIsTakenFromTheWaterSwept()
    {
        var (w, c) = Sea(0, 127, -127, 40f);
        float stock = w.Pool[(int)Pool.Phyto][c] * Pools.Kg[(int)Pool.Phyto];
        float step = w.PVWater[0] * K.M_PER_UNIT;   // a step at top swimming speed
        float none = Respiration.PlanktonKg(w, 0, c, 0f, 0f, 0f), one = Respiration.PlanktonKg(w, 0, c, step, 0f, 0f), two = Respiration.PlanktonKg(w, 0, c, 2f * step, 0f, 0f);
        float bite = w.PBite[0];
        _out.WriteLine($"{w.PM[0]:F2} kg, {Body.LengthM(w, 0):F3} m long, swims {step:F0} m a step at top speed: takes {one * 1000:F3} g of plankton (the cell holds {stock:F1} kg in 40 m; its bite {bite * 1000:F0} g)");
        Assert.Equal(0f, none);
        Assert.Equal(2f * one, two, 4);
        Assert.True(one < 0.01f * stock, "a step's sweep is a sliver of the column");
        // deeper than the mixed layer it finds less (the light there)
        Assert.True(Respiration.PlanktonKg(w, 0, c, step, K.Z_MIX_SEA + 60f, 1f) < one);
    }

    [Fact] // a dive is swum down and back, at swimming's cost of transport
    public void ADiverPaysForItsDives()
    {
        var (w, c) = Sea(0, 127, -127, 30f);
        OceanTests.Sea(() =>
        {
            var b = Respiration.Uptake(w, 0, c, false, 0f, out float z, out float share);
            _out.WriteLine($"depth {z:F1} m for {share:P0} of the step: {b.DiveM:F0} m swum diving; extra cost {b.Extra:E3}");
            Assert.True(z > 0f && b.DiveM >= 2f * z * 0.999f);
            float dive = b.DiveM / K.M_PER_UNIT * K.C_MOVE * MathK.Pow(w.MassOf(0), K.EXP_MOVE);
            Assert.True(b.Extra >= dive * 0.999f, "the dive's cost is paid");
            return 0;
        });
    }

    [Fact] // a chase runs at the speed of the medium: a flier swimming reaches as far as it swims, not as far as it flies
    public void ChaseReachFollowsTheMedium()
    {
        var (w, c) = Sea(-60, -127, 127, 30f);
        Assert.Equal(Loco.Fly, w.LocoOf(0));
        w.Flags[0] = Flag.Landed;
        _out.WriteLine($"flier {w.PM[0]:F2} kg: reach in the air {w.PReach[0] * K.M_PER_UNIT:F0} m, swimming {w.PReachSwim[0] * K.M_PER_UNIT:F0} m, on land {w.PReachLand[0] * K.M_PER_UNIT:F0} m");
        Assert.Equal(w.PReachSwim[0], w.ReachOf(0));
        Assert.True(w.ReachOf(0) < w.PReach[0]);
        w.Flags[0] = Flag.Aloft;
        Assert.Equal(w.PReach[0], w.ReachOf(0));
    }

    [Fact] // wings stroke under water, best as small flippers: the wing a heavy body needs to fly is a worse flipper (Thaxter et al. 2010; Elliott et al. 2013)
    public void WingsSwimBestAtASizeTooSmallToFlyAHeavyBody()
    {
        Assert.Equal(0f, Body.Propulsor(0f, 0f));
        Assert.Equal(K.WING_SWIM, Body.Propulsor(0f, 0.5f), 5);
        Assert.True(Body.Propulsor(0f, 1f) < 0.01f);
        // a body of 12 kg: the least wing that flies it, against the flipper
        float m = 12f, wf = 0.5f;
        while (wf < 1f && Body.FlightMargin(wf, m) < 1f) wf += 0.01f;
        _out.WriteLine($"12 kg: flies from wings {wf:F2}, swims with them at {Body.Propulsor(0f, wf):F2} fins' worth; with a flipper (0.5, flightless) {Body.Propulsor(0f, 0.5f):F2}");
        Assert.True(Body.FlightMargin(0.5f, m) < 1f, "a half wing does not fly 12 kg");
        Assert.True(Body.Propulsor(0f, wf) < Body.Propulsor(0f, 0.5f));
        // in a body: the flipper-winged swims faster than the flier
        var flip = Sea(40, -127, 0, 30f); var fly = Sea(40, -127, 127, 30f);
        Assert.True(flip.w.PVWater[0] > fly.w.PVWater[0]);
    }
}
