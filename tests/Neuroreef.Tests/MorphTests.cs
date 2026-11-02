// Body-plan prototype (docs/BODYPLAN.md): the physics of a morphology and the genome's mutation operators, and (slow)
// the evolution demo: from one generic blob, three environments, nothing labelled.

using System;
using System.Text;
using Neuroreef.Morph;
using Xunit;
using Xunit.Abstractions;

namespace Neuroreef.Tests;

public class MorphTests
{
    readonly ITestOutputHelper _out;
    public MorphTests(ITestOutputHelper o) { _out = o; }

    static int Seeds => int.TryParse(Environment.GetEnvironmentVariable("NR_MORPH_SEEDS"), out var s) ? s : 3;
    static int Gens => int.TryParse(Environment.GetEnvironmentVariable("NR_MORPH_GENS"), out var g) ? g : 800;

    // ---------- mutation operators ----------

    static MorphGenome WithLimb()
    {
        var g = MorphGenome.Blob();
        var r = new Rng.Seq(7);
        Mutate.InsertAppendage(g, 0, ref r);
        g.Segs[0].Apps[0].P[AP.MUSCLE] = 0.8f;
        return g;
    }

    [Fact]
    public void DuplicatingASegmentAddsOneAndBarelyChangesTheBody()
    {
        var g = WithLimb();
        var before = Development.Grow(g);
        Assert.True(Mutate.DuplicateSegment(g, 0));
        var after = Development.Grow(g);
        Assert.Equal(2, g.Segs.Count);
        Assert.Equal(before.SegmentCount + 1, after.SegmentCount);
        Assert.Equal(before.PairCount + 1, after.PairCount);            // the copy carries its appendage genes
        Assert.InRange(after.Length / before.Length, 0.9f, 1.1f);      // each copy half as long: a near-neutral split
        Assert.InRange(after.Mass, 0.99f * Development.BodyMass, 1.01f * Development.BodyMass);
        // the copies are independent: changing one leaves the other
        g.Segs[1].P[SP.INT_MIN] = 1f;
        Assert.Equal(0f, g.Segs[0].P[SP.INT_MIN]);
    }

    [Fact]
    public void DeletionRemovesPartsButNeverTheLastSegment()
    {
        var g = WithLimb();
        Assert.False(Mutate.DeleteSegment(g, 0));
        Mutate.DuplicateSegment(g, 0);
        Assert.True(Mutate.DeleteSegment(g, 1));
        Assert.Single(g.Segs);
        Assert.True(Mutate.DeleteAppendage(g, 0, 0));
        Assert.Empty(g.Segs[0].Apps);
        Assert.False(Mutate.DeleteAppendage(g, 0, 0));
    }

    [Fact]
    public void DuplicatingAnAppendageSharesItsMuscleAndCopiesCanDiverge()
    {
        var g = WithLimb();
        float mus = g.Segs[0].Apps[0].P[AP.MUSCLE];
        Assert.True(Mutate.DuplicateAppendage(g, 0, 0, 0));
        Assert.Equal(2, g.Segs[0].Apps.Count);
        Assert.Equal(mus, g.Segs[0].Apps[0].P[AP.MUSCLE] + g.Segs[0].Apps[1].P[AP.MUSCLE], 4);
        var r = new Rng.Seq(3);
        for (int i = 0; i < 50; i++) Mutate.Diverge(g, ref r, 0.1f);
        bool differ = false;
        for (int k = 0; k < AP.N; k++) differ |= g.Segs[0].Apps[0].P[k] != g.Segs[0].Apps[1].P[k];
        Assert.True(differ);
    }

    [Fact]
    public void InsertionGrowsATinyBudAndGenomesStayInBounds()
    {
        var g = MorphGenome.Blob();
        var r = new Rng.Seq(11);
        Assert.True(Mutate.InsertAppendage(g, 0, ref r));
        var m = Development.Grow(g);
        Assert.Equal(1, m.PairCount);
        Assert.True(m.Pairs[0].Len < 0.02f);
        var rates = new MutationRates { DupSeg = 0.5f, DelSeg = 0.2f, DupApp = 0.5f, DelApp = 0.2f, InsApp = 0.5f };
        for (int i = 0; i < 2000; i++)
        {
            Mutate.Apply(g, ref r, rates);
            Assert.InRange(g.Segs.Count, 1, MorphGenome.MAX_SEG_GENES);
            foreach (var s in g.Segs)
            {
                Assert.InRange(s.Apps.Count, 0, MorphGenome.MAX_APP_GENES);
                foreach (var v in s.P) Assert.InRange(v, 0f, 1f);
            }
        }
        var body = Development.Grow(g);
        Assert.InRange(body.Mass, 0.99f * Development.BodyMass, 1.01f * Development.BodyMass);
    }

    [Fact]
    public void ARepeatedSegmentGeneGrowsASeriesGradedByItsTaper()
    {
        var g = MorphGenome.Blob();
        g.Segs[0].P[SP.REPEAT] = 1f; g.Segs[0].P[SP.TAPER] = 0f;   // 4 copies, each 0.7 of the one before
        var m = Development.Grow(g);
        Assert.Equal(4, m.SegmentCount);
        for (int i = 1; i < 4; i++) Assert.Equal(0.7f, m.Segs[i].L / m.Segs[i - 1].L, 3);
    }

    // ---------- physics ----------

    static Morphology Trunk(float lenG, float widG, int segs = 1)
    {
        var g = Probe(segs, 0.2f, 0f, 0f, 0f, 0.5f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, lenG, widG, widG);
        return Development.Grow(g);
    }

    [Fact]
    public void DragAtAGivenMassIsLeastForAStreamlinedBody()
    {
        var stubby = Trunk(0.2f, 0.8f);          // fineness < 1
        var fish = Trunk(0.85f, 0.45f);          // fineness ~4-6
        var eel = Trunk(1f, 0.05f, 3);           // fineness > 15
        _out.WriteLine($"fineness {stubby.Fineness:F1} {fish.Fineness:F1} {eel.Fineness:F1}");
        Assert.InRange(fish.Fineness, 3f, 8f);
        Assert.True(eel.Fineness > 12f && stubby.Fineness < 1.2f);
        float dS = Physics.BodyDragArea(stubby), dF = Physics.BodyDragArea(fish), dE = Physics.BodyDragArea(eel);
        _out.WriteLine($"drag area {dS:E2} {dF:E2} {dE:E2}");
        Assert.True(dF < dS && dF < dE);
    }

    [Fact]
    public void AFlatHighAspectBladeIsTheEfficientPropulsorAndARodIsNot()
    {
        Assert.True(Physics.ProfileEta(8f, 10f) > Physics.ProfileEta(2f, 10f));   // span
        Assert.True(Physics.ProfileEta(8f, 10f) > Physics.ProfileEta(8f, 1f));    // flatness
        Assert.InRange(Physics.ProfileEta(8f, 1f), 0.1f, 0.35f);                  // a rod can only row
    }

    [Fact]
    public void SwimmingCostRisesSteeplyWithSpeed()
    {
        var g = Probe(1, 0.2f, 0f, 0f, 0f, 1f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0.85f, 0.45f, 0.45f);
        var m = Development.Grow(g);
        var sp = Physics.Swim(m, Phys.RHO_W);
        // slow, it pays to hold a body denser than water up (a hover that dynamic lift relieves as it speeds up); fast,
        // drag ~U² x U, less the Froude efficiency gain
        float p0 = Physics.SwimPower(sp, Phys.RHO_W, 0f), p1 = Physics.SwimPower(sp, Phys.RHO_W, 0.5f), p2 = Physics.SwimPower(sp, Phys.RHO_W, 1f);
        _out.WriteLine($"swim power at 0, 0.5, 1 m/s: {p0:F4} {p1:F4} {p2:F4} W");
        Assert.True(p0 > 0f);
        Assert.True(p2 > 4f * p1);
    }

    static MorphGenome Legged(float mineral, float muscle = 0.8f) =>
        Probe(4, 0.4f, 0f, 0.2f, 1f, 0.1f, 1, 0.65f, 0.35f, 0.5f, 0.75f, 0.5f, muscle, mineral, 0.3f, 1f, 0.5f, 0.5f, 0.5f);

    [Fact]
    public void StiffMuscledLimbsHoldABodyUpOnLandAndSoftOrSlackOnesDoNot()
    {
        var stiff = Physics.Walk(Development.Grow(Legged(1f)), Phys.RHO_A);
        var soft = Physics.Walk(Development.Grow(Legged(0f)), Phys.RHO_A);
        var slack = Physics.Walk(Development.Grow(Legged(1f, 0f)), Phys.RHO_A);
        _out.WriteLine($"support stiff {stiff.Support:F2} soft {soft.Support:F2} slack {slack.Support:F2}; legs {stiff.NLegs:F1} stability {stiff.Stability:F2}");
        Assert.True(stiff.Support >= 0.99f);
        Assert.True(soft.Support < 0.5f * stiff.Support);
        Assert.True(slack.Support < 0.1f);
        Assert.True(stiff.Stability > 0.9f);   // 8 legs: a tripod down at duty factor 0.5
    }

    [Fact]
    public void WaterBearsMostOfTheWeightThatLimbsBearOnLand()
    {
        var m = Development.Grow(Legged(0f));
        var air = Physics.Walk(m, Phys.RHO_A);
        var water = Physics.Walk(m, Phys.RHO_W);
        Assert.True(water.WEff < 0.1f * air.WEff);
        Assert.True(water.Support > air.Support);
    }

    static Morphology Gills(float invag)
    {
        var g = Probe(2, 0.1f, 0f, 0.4f, invag, 0.2f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        return Development.Grow(g);
    }

    [Fact]
    public void EvertedSurfacesBreatheWaterAndFoldedInOnesBreatheAir()
    {
        var ever = Gills(0f);
        var folded = Gills(1f);
        float ew = Physics.O2Supply(ever, Medium.Water, out float vEW), fw = Physics.O2Supply(folded, Medium.Water, out float vFW);
        float ea = Physics.O2Supply(ever, Medium.Air, out _), fa = Physics.O2Supply(folded, Medium.Air, out _);
        Assert.True(ew > fw && vEW < vFW);    // in water: flow-through is better and cheaper to ventilate than a tidal pocket
        Assert.True(fa > ea);                 // in air: wet everted lamellae collapse; folded-in surfaces hold
        Assert.True(Physics.SurfaceUpkeep(ever, Medium.Air, 0f) > 2f * Physics.SurfaceUpkeep(folded, Medium.Air, 0f));   // and dry out
        Assert.True(fa > fw);                 // air carries ~30x the oxygen of water
    }

    static Morphology Integument(float t, float mineral) => Development.Grow(Probe(2, t, mineral, 0f, 0f, 0.2f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));

    [Fact]
    public void AThickMineralisedIntegumentProtectsCostsMassAndMustBeMoulted()
    {
        var soft = Integument(0.1f, 0f);
        var shell = Integument(0.7f, 1f);
        Assert.True(Physics.PKill(shell, 10f) < 0.2f);
        Assert.True(Physics.PKill(soft, 10f) > 0.9f);
        Assert.True(shell.IntMass > 5f * soft.IntMass);
        Assert.True(shell.Mass / shell.Volume > soft.Mass / soft.Volume);   // denser: it sinks
        Assert.Equal(0f, soft.Rigidity, 3);
        var es = Physics.Evaluate(shell, MorphEnv.SeaFloor());
        Assert.True(es.MoultShare > 0.04f);
        Assert.Equal(0f, Physics.Evaluate(soft, MorphEnv.SeaFloor()).MoultShare, 3);
        Assert.True(shell.InertMass > 0f && soft.InertMass < 1e-6f);
    }

    static float Flight(float lenG, float muscle, float bodyMass)
    {
        float keep = Development.BodyMass;
        Development.BodyMass = bodyMass;
        try { return Physics.FlightMargin(Development.Grow(Probe(1, 0.1f, 0f, 0.1f, 1f, 0f, 1, lenG, 0.75f, 0.1f, 0f, 0.3f, muscle, 0.6f, 0.2f, 1f))); }
        finally { Development.BodyMass = keep; }
    }

    [Fact]
    public void FlightMarginGrowsWithSpanAndMuscleAndFallsWithSize()
    {
        float shortW = Flight(0.6f, 1f, 0.25f), longW = Flight(1f, 1f, 0.25f), noMus = Flight(1f, 0f, 0.25f), small = Flight(1f, 1f, 0.01f);
        _out.WriteLine($"margin short {shortW:F2} long {longW:F2} no muscle {noMus:F2} at 10 g {small:F2}");
        Assert.Equal(0f, noMus);
        Assert.True(longW > shortW);
        Assert.True(small > longW);    // muscle power ~M, the power flight needs ~M^(7/6)
        Assert.True(small > 1f);       // a 10 g body with long, flat, muscled appendages flies
    }

    [Fact]
    public void ABlobCanBarelyLiveAndEvolutionImprovesIt()
    {
        var env = MorphEnv.OpenWater();
        float blob = Physics.Evaluate(Development.Grow(MorphGenome.Blob()), env).Fitness;
        var r = Evolver.Run(env, 5u, 40, 60);
        Assert.True(r.BestByGen[^1] > blob + 1f);
        for (int i = 1; i < r.BestByGen.Length; i++) Assert.True(r.BestByGen[i] >= r.BestByGen[i - 1] - 1e-5f);   // elitism
    }

    /// <summary>A hand-set genome, for probing the landscape only (never seeded into evolution).</summary>
    internal static MorphGenome Probe(int segs, float intT, float intMin, float pore, float invag, float axMus, int pairsPerSeg,
        float len, float chord, float thick, float droop, float joints, float muscle, float mineral, float wall, float exo, float lenG = 0.5f, float widG = 0.5f, float heiG = 0.5f)
    {
        var g = new MorphGenome();
        for (int i = 0; i < segs; i++)
        {
            var s = new SegGene();
            s.P[SP.LEN] = lenG; s.P[SP.WID] = widG; s.P[SP.HEI] = heiG; s.P[SP.TAPER] = 0.5f; s.P[SP.REPEAT] = 0f;
            s.P[SP.INT_T] = intT; s.P[SP.INT_MIN] = intMin; s.P[SP.PORE] = pore; s.P[SP.INVAG] = invag; s.P[SP.AXMUS] = axMus;
            for (int k = 0; k < pairsPerSeg; k++)
            {
                var a = new AppGene();
                a.P[AP.LEN] = len; a.P[AP.CHORD] = chord; a.P[AP.THICK] = thick; a.P[AP.DROOP] = droop; a.P[AP.JOINTS] = joints;
                a.P[AP.MUSCLE] = muscle; a.P[AP.MINERAL] = mineral; a.P[AP.WALL] = wall; a.P[AP.EXO] = exo;
                s.Apps.Add(a);
            }
            g.Segs.Add(s);
        }
        return g;
    }

    [Fact, Trait("Category", "Slow")]
    public void ProbeDesigns()
    {
        var floor = MorphEnv.SeaFloor();
        var land = MorphEnv.Land();
        foreach (var (name, g, env) in new[]
        {
            ("floor armoured walker", Probe(6, 0.6f, 1f, 0.3f, 1f, 0.1f, 1, 0.7f, 0.55f, 0.65f, 0.6f, 0.7f, 0.6f, 1f, 0.3f, 1f, 0.5f, 0.6f, 0.4f), floor),
            ("floor armoured walker gills out", Probe(6, 0.6f, 1f, 0.3f, 0f, 0.1f, 1, 0.7f, 0.55f, 0.65f, 0.6f, 0.7f, 0.6f, 1f, 0.3f, 1f, 0.5f, 0.6f, 0.4f), floor),
            ("floor soft walker", Probe(6, 0.3f, 0f, 0.3f, 0f, 0.1f, 1, 0.7f, 0.55f, 0.65f, 0.6f, 0.7f, 0.6f, 1f, 0.3f, 1f, 0.5f, 0.6f, 0.4f), floor),
            ("land stiff-legged walker", Probe(4, 0.5f, 0.5f, 0.2f, 1f, 0.1f, 1, 0.75f, 0.6f, 0.7f, 0.7f, 0.5f, 0.8f, 1f, 0.4f, 0.5f, 0.5f, 0.5f, 0.5f), land),
            ("land light-legged walker", Probe(4, 0.5f, 0.5f, 0.2f, 1f, 0.1f, 1, 0.65f, 0.35f, 0.5f, 0.75f, 0.5f, 0.8f, 1f, 0.3f, 1f, 0.5f, 0.5f, 0.5f), land),
            ("land light-legged walker, soft trunk", Probe(4, 0.4f, 0f, 0.2f, 1f, 0.1f, 1, 0.65f, 0.35f, 0.5f, 0.75f, 0.5f, 0.8f, 1f, 0.3f, 0f, 0.5f, 0.5f, 0.5f), land),
            ("floor light-legged armoured walker", Probe(4, 0.6f, 1f, 0.3f, 0f, 0.1f, 1, 0.65f, 0.35f, 0.5f, 0.75f, 0.5f, 0.8f, 1f, 0.3f, 1f, 0.5f, 0.5f, 0.5f), floor),
            ("land crawler",Probe(4, 0.5f, 0f, 0.2f, 1f, 0.6f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0.5f, 0.5f, 0.5f), land),
        })
        {
            var m = Development.Grow(g);
            _out.WriteLine($"-- {name}\n" + Evolver.Describe(m, Physics.Evaluate(m, env)));
        }
    }

    [Fact, Trait("Category", "Slow")]
    public void EvolutionDemo()
    {
        var sb = new StringBuilder();
        sb.AppendLine("env         seed  fit   segs pairs  fine  rigid integ% moult  exchExt exchInt  mode  speed  AR    flat  droop joints mineral appMus pKill");
        string detail = "";
        var landEnv = MorphEnv.Land();
        if (float.TryParse(Environment.GetEnvironmentVariable("NR_MORPH_LANDFOOD"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lf)) landEnv.FoodArea = lf;
        foreach (var env in new[] { MorphEnv.OpenWater(), MorphEnv.SeaFloor(), landEnv })
        {
            for (uint seed = 1; seed <= Seeds; seed++)
            {
                var r = Evolver.Run(env, seed, 200, Gens);
                var d = Evolver.Summary(r.BestBody, r.BestEval);
                sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0,-11} {1,4} {2,5:F2} {3,4} {4,5}  {5,5:F2} {6,5:F2} {7,5:F1} {8,6:F3} {9,7:F4} {10,7:F4}  {11,-5} {12,5:F2} {13,5:F1} {14,5:F1} {15,5:F0} {16,5:F1} {17,6:F2} {18,6:F2} {19,5:F2}",
                    env.Name, seed, d["fitness"], d["segments"], d["pairs"], d["fineness"], d["rigidity"], 100f * d["integShare"], d["moult"],
                    d["exchExt"], d["exchInt"], d["walks"] > 0 ? "walk" : "swim", d["speed"], d["AR"], d["flat"], d["droop"], d["joints"], d["mineral"], d["appMuscle"], d["pKill"]));
                if (seed == 1) detail += $"== {env.Name} (seed 1)  final population: mean segments {r.MeanSegments:F1}, mean pairs {r.MeanPairs:F1}\n" + Evolver.Describe(r.BestBody, r.BestEval) + "\n";
            }
        }
        _out.WriteLine(sb.ToString());
        _out.WriteLine(detail);
    }
}
