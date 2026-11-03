// Shadow expression (docs/BODYPLAN.md §10, phase 2): every live body translated to a module genome (Morph.Translate)
// and its capabilities computed from the developed morphology (Morph.Shadow), alongside Body.Express and read by
// nothing in the simulation. These tests check that the translator carries the live genes' key capabilities over, that
// the shadow agrees with the live values where the live simulation is physically grounded (over the founders of seeds
// 1-5, founding only), and that the shadow is write-only (the pinned hashes are StepLengthTests and ClimateTests).

using System;
using System.Text;
using Neuroreef.Morph;
using Xunit;
using Xunit.Abstractions;

namespace Neuroreef.Tests;

public class ShadowTests
{
    readonly ITestOutputHelper _out;
    public ShadowTests(ITestOutputHelper o) { _out = o; }

    static LiveGenes Body0(float mass) => Translate.Reference(mass);

    static string Line(string name, in Caps c) =>
        $"{name,-14} vL {c.VLand,6:F2} vW {c.VWater,6:F2} vA {c.VAir,6:F2} cru {c.Cruise,5:F2} cotL {c.CotLand,6:F2} cotW {c.CotWater,6:F3} cotA {c.CotAir,6:F2} " +
        $"mar {c.Margin,5:F2} fly {c.Flies} st {c.Stand,5:F3} O2a {c.O2Air,6:F2} O2w {c.O2Water,6:F2} odds {c.Odds,8:F3} bas {c.Basal,6:F2} str {c.Stream,5:F2} R {c.TurnR,6:F3}";

    [Fact]
    public void Probe()
    {
        foreach (float m in new[] { 0.1f, 1f, 10f })
        {
            var r = Body0(m);
            _out.WriteLine(Line($"walker {m}", Shadow.Of(r, 1f, 0f, out var mo)));
            var legless = r; legless.Legs = 0f;
            _out.WriteLine(Line($"legless {m}", Shadow.Of(legless, 1f, 0f)));
            var fish = r; fish.Legs = 0f; fish.Fins = 1f; fish.Lungs = 0f; fish.Endo = 0f; fish.LengthShare = 2.5f; fish.WidthShare = fish.HeightShare = MathF.Sqrt(1f / 2.5f);
            _out.WriteLine(Line($"fish {m}", Shadow.Of(fish, 1f, 0f)));
            var cubeFish = fish; cubeFish.LengthShare = cubeFish.WidthShare = cubeFish.HeightShare = 1f;
            _out.WriteLine(Line($"cube fish {m}", Shadow.Of(cubeFish, 1f, 0f, out var mcf)));
            var legFish = fish; legFish.Legs = 0.6f;
            _out.WriteLine(Line($"leg fish {m}", Shadow.Of(legFish, 1f, 0f, out var mlf)));
            var halfFin = fish; halfFin.Fins = 0.7f; halfFin.FinPairs = 3;
            _out.WriteLine(Line($"fins0.7x3 {m}", Shadow.Of(halfFin, 1f, 0f, out var mhf)));
            _out.WriteLine($"  densities: cube fish {mcf.Mass / mcf.Volume:F0}, leg fish {mlf.Mass / mlf.Volume:F0}, fins0.7x3 {mhf.Mass / mhf.Volume:F0}; live cot {K.COT_SWIM_J * MathK.Pow(m, K.EXP_MOVE) * K.ETA_LIFT / (K.ETA_PADDLE + (K.ETA_LIFT - K.ETA_PADDLE) * 0.7f):F3} at fins 0.7");
            var bird = r; bird.Wings = 1f; bird.LegPairs = 1;
            _out.WriteLine(Line($"bird {m}", Shadow.Of(bird, 1f, 0f, out var mb)));
            var arm = r; arm.Armour = 3f;
            _out.WriteLine(Line($"armour {m}", Shadow.Of(arm, 1f, 0f)));
            {
                // how much of a walker's cost a metre in water is holding its depth (it is denser than water: no gas in it)
                var sp = Physics.Swim(mo, Phys.RHO_W); var sp0 = sp; sp0.WNet = 0f;
                float best = 1e9f, best0 = 1e9f;
                for (float u = 0.05f; u < 3f; u *= 1.05f)
                {
                    best = MathF.Min(best, Physics.SwimPower(sp, Phys.RHO_W, u) / Phys.ETA_M / u);
                    best0 = MathF.Min(best0, Physics.SwimPower(sp0, Phys.RHO_W, u) / Phys.ETA_M / u);
                }
                _out.WriteLine($"  walker in water: least cost/m {best:F2} J/m, neutrally buoyant {best0:F2} J/m; density {mo.Mass / mo.Volume:F0} kg/m3");
                float legs = 0f; foreach (var a in mo.Pairs) legs += 2f * a.Mass;
                _out.WriteLine($"  walker legs {legs / mo.Mass:P1} of its mass, mineral {mo.InertMass / mo.Mass:P1} (Prange et al. 1979: whole skeleton {0.061f * MathK.Pow(m, 1.09f) / m:P1}), fat {mo.FatMass / mo.Mass:P1}");
            }
            { float fm = 0f, segT = 0f; foreach (var a in mb.Pairs) if (a.Theta < 0.1f && a.Muscle > 0f) fm += 2f * a.Muscle; segT = mb.Segs[1].TissueMass; _out.WriteLine($"  walker mass {mo.Mass:F3} L {mo.Length:F3} maxH {mo.MaxH:F3} pairs {mo.PairCount} muscle {mo.MuscleMass:F3}; bird flight muscle {fm / mb.Mass:F3} of its mass, thorax tissue {segT / mb.Mass:F2}, density {mb.Mass / mb.Volume:F0}, segs {mb.SegmentCount}"); }
        }
    }

    // ---------- shadow against live over the founders of seeds 1-5 (founding only: no step is run)

    static ShadowDiag.Row[]? _rows;
    static readonly System.Collections.Generic.List<string> Log = new();
    static readonly object _lock = new();

    /// <summary>The founders of seeds 1-5 on the test map, shadow against live (built once for the class).</summary>
    static ShadowDiag.Row[] Founders()
    {
        lock (_lock)
        {
            if (_rows != null) return _rows;
            var rows = ShadowDiag.NewRows();
            for (uint seed = 1; seed <= 5; seed++)
            {
                var t = System.Diagnostics.Stopwatch.StartNew();
                using (var sim = new Simulation(1024, seed, 2))
                {
                    ShadowDiag.Collect(sim.W, rows);
                    if (seed == 1) Log.Add(ShadowDiag.SpeciesReport(sim.W));   // the Headless telemetry of both expressions
                }
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();   // one world at a time: the machine is shared
                Log.Add($"seed {seed}: {t.ElapsedMilliseconds} ms, {GC.GetTotalMemory(false) / 1048576} MB after");
            }
            return _rows = rows;
        }
    }

    [Fact]
    public void ShadowAgainstLiveOverTheFoundersOfSeeds1To5()
    {
        var rows = Founders();
        foreach (var l in Log) _out.WriteLine(l);
        _out.WriteLine(ShadowDiag.Format(rows));
        float Med(string name) => Array.Find(rows, r => r.Name == name)!.Median;
        Assert.True(rows[0].Ratios.Count > 1000);
        // the calibrated capabilities, where the live simulation is physically grounded (docs/BODYPLAN.md §10.4): the
        // median founder within ~25%. The rest are reported, not asserted: §10.4 says which side is more physical
        Assert.InRange(Med("drag shape"), 0.8f, 1.25f);             // Hoerner's volume drag against the wetted-area drag
        Assert.InRange(Med("standing height"), 0.8f, 1.25f);        // Garcia & da Silva's shoulder height
        Assert.InRange(Med("land cost/m, legs>=0.7"), 0.75f, 1.3f); // Taylor, Heglund & Maloiy's cost of transport
        // legs, leg pairs and armour against the plain body. Phase 2c: 0.74 (was 0.86): the plain body's slimmer, tubular
        // legs run faster, and founders with every organ gene high (legs, four fin pairs, wings, a fish's axial muscle)
        // cannot house all that muscle in one trunk (MUSCLE_MAX_SHARE), which the live sim never asks (docs/BODYPLAN.md §12.5)
        Assert.InRange(Med("land speed (rel.)"), 0.7f, 1.3f);
        // flight's cost a metre at the speed of least power; ~1.3 since phase 2b's wing profile drag (Pennycuick 2008),
        // on founders' fliers that are bluff (fineness < 1.5), which the live cost ignores (docs/BODYPLAN.md §11.4)
        Assert.InRange(Med("air cost/m"), 0.7f, 1.35f);
        Assert.InRange(Med("water cruise, fins>=0.7"), 0.7f, 1.4f); // Videler's ~1 length a second
        Assert.InRange(Med("basal cost"), 0.7f, 1.05f);             // Kleiber at the metabolic level, less the labelled organs' upkeep
        // phase 2b (docs/BODYPLAN.md §11): the lung is the lining of a gas space bounded by volume, so the aerobic scope
        // each expression builds (Weibel & Hoppeler's ~10 in air, fishes' 2-5 in water) agrees
        Assert.InRange(Med("O2 scope in air"), 0.8f, 1.2f);
        Assert.InRange(Med("O2 scope in water"), 0.75f, 1.25f);
    }

    // ---------- the translator carries the live genes' key capabilities over

    static Caps S(in LiveGenes l) => Shadow.Of(l, 1f, 0f);
    static LiveGenes Fish(float m)
    {
        var f = Translate.Reference(m); f.Legs = 0f; f.Fins = 1f; f.Lungs = 0f; f.Endo = 0f;
        f.LengthShare = 2.5f; f.WidthShare = f.HeightShare = MathF.Sqrt(1f / 2.5f);
        return f;
    }

    [Fact]
    public void LegsBecomeSupportsThatStandAndRunAndTheirLossLeavesACrawler()
    {
        foreach (float m in new[] { 0.1f, 1f, 10f })
        {
            var walker = Translate.Reference(m); var half = walker; half.Legs = 0.5f; var none = walker; none.Legs = 0f;
            Caps w = S(walker), h = S(half), n = S(none);
            float live = K.COT_J * MathK.Pow(m, K.EXP_MOVE) * (1f - K.LEG_SWING + K.LEG_SWING * 0.5f);
            _out.WriteLine($"{m} kg: stand {n.Stand:F3} {h.Stand:F3} {w.Stand:F3} (live {K.STAND_H * MathK.Cbrt(m):F3}); land speed {n.VLand:F2} {h.VLand:F2} {w.VLand:F2} (Garland {Body.SprintMps(m):F2}); cost/m {w.CotLand:F2} (live {live:F2})");
            Assert.True(w.Stand > h.Stand && h.Stand > n.Stand);
            Assert.True(w.VLand > 3f * n.VLand && h.VLand > 3f * n.VLand);   // legs run, a legless body crawls (at 10 kg half-length legs outrun full ones: their swing)
            // standing height: the live STAND_H x M^(1/3) on full legs (Garcia & da Silva 2006), within 20%
            Assert.InRange(w.Stand / (K.STAND_H * MathK.Cbrt(m)), 0.8f, 1.2f);
            // walking cost a metre: Taylor, Heglund & Maloiy's 10.7 M^0.7 J/m (stance and half the swing), within 35%
            Assert.InRange(w.CotLand / live, 0.65f, 1.35f);
        }
    }

    [Fact]
    public void FinsBecomeThrustSurfacesAndSwimAtAFishsCostAMetre()
    {
        foreach (float m in new[] { 0.1f, 1f, 10f })
        {
            var fish = Fish(m); var finless = fish; finless.Fins = 0f;
            Caps f = S(fish), n = S(finless);
            float live = K.COT_SWIM_J * MathK.Pow(m, K.EXP_MOVE), liveStream = Body.Streamlining(fish.LengthShare, fish.WidthShare * fish.HeightShare);
            _out.WriteLine($"{m} kg: swim {n.VWater:F2} -> {f.VWater:F2} m/s, cost/m {n.CotWater:F3} -> {f.CotWater:F3} (live {live:F3}), cruise {f.Cruise:F2} m/s, drag shape {f.Stream:F2} (live {liveStream:F2})");
            Assert.True(f.VWater >= n.VWater);   // both may reach the twitch-time limit of burst swimming (Wardle 1975)
            Assert.True(f.CotWater < n.CotWater);
            // COT_SWIM_J ~1 J/(kg m) at 1 kg x M^0.7 (Videler & Nolet 1990), whose own note puts a streamlined 1 kg body's
            // drag-based cost at ~0.5 J/m; phase 2c: a cruising fish folds its paired fins (§12.1), so it sits nearer that
            // (and a fish's net minimum falls as ~M^-0.3 per kg, ~0.25 J/(kg m) at 10 kg, below COT_SWIM_J's M^0.7 line)
            Assert.InRange(f.CotWater / live, 0.4f, 1.3f);
            Assert.InRange(f.Stream / liveStream, 0.8f, 1.25f);
        }
    }

    [Fact]
    public void WingsBecomeAFlyingPairWhoseMarginFallsWithSize()
    {
        var bird = Translate.Reference(0.1f); bird.Wings = 1f; bird.LegPairs = 1;
        var none = bird; none.Wings = 0f;
        var big = bird; big.Mass = 10f;
        Caps b = S(bird), n = S(none), g = S(big);
        _out.WriteLine($"margin 0.1 kg {b.Margin:F2} (live {Body.FlightMargin(1f, 0.1f):F2}), 10 kg {g.Margin:F2} (live {Body.FlightMargin(1f, 10f):F2}); air speed {b.VAir:F1} m/s, cost/m {b.CotAir:F2} J/m");
        Assert.Equal(1f, b.Flies);
        Assert.Equal(0f, n.Flies);
        Assert.True(g.Margin < b.Margin);
        Assert.True(b.VAir > b.VLand);
    }

    [Fact]
    public void ArmourBecomesAHardThickIntegumentAndLungsAndGillsBreatheEachTheirMedium()
    {
        var bare = Translate.Reference(1f); bare.Endo = 0f; var shell = bare; shell.Armour = 3f;
        Caps a = S(bare), s = S(shell);
        var ms = Development.Grow(Translate.FromLive(shell), 1f);
        _out.WriteLine($"protection odds {a.Odds:F3} -> {s.Odds:F3}, rigidity {ms.Rigidity:F2}");
        Assert.True(s.Odds > 5f * a.Odds);
        Assert.True(ms.Rigidity > 0.9f);
        var lung = bare; lung.Lungs = 1f; var gill = bare; gill.Lungs = 0f;
        Caps l = S(lung), gi = S(gill);
        _out.WriteLine($"lungs: air {l.O2Air:F2} water {l.O2Water:F2}; gills: air {gi.O2Air:F2} water {gi.O2Water:F2} W (basal {l.Basal:F2})");
        Assert.True(l.O2Air > 3f * l.O2Water);
        Assert.True(gi.O2Water > 3f * gi.O2Air);
        // an ectotherm's organs are built to their live scopes over its basal metabolism (LUNG_SCOPE 10, GILL_SCOPE 4)
        Assert.InRange(l.O2Air / (K.LUNG_SCOPE * l.Basal), 0.7f, 1.3f);
        Assert.InRange(gi.O2Water / (K.GILL_SCOPE * gi.Basal), 0.7f, 1.3f);
    }
}
