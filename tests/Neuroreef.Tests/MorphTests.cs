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
        Assert.InRange(after.Mass, 0.99f * Development.M_REF, 1.01f * Development.M_REF);
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
        float want = Development.MassOf(g);   // the body develops to the mass its mass gene sets
        Assert.InRange(body.Mass, 0.99f * want, 1.01f * want);
        Assert.InRange(want, Development.MASS_LO, Development.MASS_HI);
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
        // drag ~U² x U x C_f(Re), and laminar C_f ~ Re^-0.5, so friction power ~U^2.5, less the Froude efficiency gain
        float p0 = Physics.SwimPower(sp, Phys.RHO_W, 0f), p1 = Physics.SwimPower(sp, Phys.RHO_W, 0.5f), p2 = Physics.SwimPower(sp, Phys.RHO_W, 1f);
        _out.WriteLine($"swim power at 0, 0.5, 1 m/s: {p0:F4} {p1:F4} {p2:F4} W");
        Assert.True(p0 > 0f);
        Assert.True(p2 - p0 > 4f * (p1 - p0));   // the part above holding depth
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
        float keep = Development.FixedMass;
        Development.FixedMass = bodyMass;
        try { return Physics.FlightMargin(Development.Grow(Probe(1, 0.1f, 0f, 0.1f, 1f, 0f, 1, lenG, 0.75f, 0.1f, 0f, 0.3f, muscle, 0.6f, 0.2f, 1f))); }
        finally { Development.FixedMass = keep; }
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

    // ---------- Phase 1 physics: mass, feeding, turning, obstacles, search ----------

    static T AtMass<T>(float mass, Func<T> f)
    {
        float keep = Development.FixedMass;
        Development.FixedMass = mass;
        try { return f(); } finally { Development.FixedMass = keep; }
    }

    [Fact]
    public void TheMassGeneSetsBodyMassAndKleiberAndTheGutScaleAsThreeQuarters()
    {
        var g = Probe(3, 0.2f, 0f, 0.1f, 0f, 0.5f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        Assert.InRange(Development.Grow(g).Mass, 0.99f * Development.M_REF, 1.01f * Development.M_REF);   // the default gene is the old 0.25 kg
        g.MassG = 1f;
        var big = Development.Grow(g);
        g.MassG = 0f;
        var small = Development.Grow(g);
        Assert.InRange(big.Mass, 9.9f, 10.1f);
        Assert.InRange(small.Mass, 0.00099f, 0.00101f);
        Assert.Equal(small.Fineness, big.Fineness, 2);                       // the same shape, scaled
        // Kleiber: basal ~ M^0.75; gut throughput ~ M^0.75 too (Demment & Van Soest 1985)
        float kb = MathK.Log(Physics.Kleiber(big.Mass) / Physics.Kleiber(small.Mass)) / MathK.Log(big.Mass / small.Mass);
        float kg = MathK.Log(Physics.GutCapacity(big) / Physics.GutCapacity(small)) / MathK.Log(big.Mass / small.Mass);
        Assert.InRange(kb, 0.74f, 0.76f);
        Assert.InRange(kg, 0.73f, 0.77f);
    }

    [Fact]
    public void SkinFrictionFallsWithReynoldsNumberSoSmallSlowBodiesPayMorePerArea()
    {
        Assert.InRange(Physics.SkinFriction(1e5f), 0.0035f, 0.006f);         // Blasius 1.33/sqrt(Re)
        Assert.True(Physics.SkinFriction(1e3f) > 5f * Physics.SkinFriction(1e5f));
        Assert.InRange(Physics.SkinFriction(1e7f), 0.002f, 0.004f);          // Prandtl-Schlichting, turbulent
        var g = Probe(1, 0.2f, 0f, 0f, 0f, 1f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0.85f, 0.45f, 0.45f);
        var sp = Physics.Swim(Development.Grow(g), Phys.RHO_W);
        Assert.True(Physics.DragAreaAt(sp, 0.05f) > 1.5f * Physics.DragAreaAt(sp, 1f));
    }

    [Fact]
    public void PlanktonCaptureFollowsTheHeadNotTheVolume()
    {
        // the same mass as a fish and as a thread: the thread's tiny head clears far less water (a loophole fixed)
        var fish = Trunk(0.85f, 0.45f);
        var thread = Trunk(1f, 0.0f, 8);
        Assert.True(thread.Fineness > 4f * fish.Fineness);
        Assert.True(Physics.Capture(thread) < 0.2f * Physics.Capture(fish));
        float v23 = MathK.Pow(fish.Volume, 2f / 3f);
        Assert.InRange(Physics.Capture(fish) / v23, 0.25f, 1f);               // ~ the old 0.5 V^(2/3) for a fish
    }

    /// <summary>A body with one pair on its first segment, the pair set by mineral, joints and muscle (a claw probe).</summary>
    static Morphology Clawed(float mineral, float joints, float muscle, float len = 0.6f, float wall = 0.6f) =>
        Development.Grow(Probe(1, 0.3f, 0.5f, 0.1f, 0f, 0.1f, 1, len, 0.45f, 0.5f, 0.8f, joints, muscle, mineral, wall, 1f, 0.6f, 0.6f, 0.5f));

    [Fact]
    public void OnlyAStiffJointedMuscledAppendageAtTheMouthCrushesShells()
    {
        float claw = Physics.Handle(Clawed(1f, 0.5f, 0.8f), Phys.RHO_W, 0f).Force;
        float soft = Physics.Handle(Clawed(0f, 0.5f, 0.8f), Phys.RHO_W, 0f).Force;
        float rigidRod = Physics.Handle(Clawed(1f, 0f, 0.8f), Phys.RHO_W, 0f).Force;     // no joints: cannot close
        float slack = Physics.Handle(Clawed(1f, 0.5f, 0f), Phys.RHO_W, 0f).Force;
        float shortArm = Physics.Handle(Clawed(1f, 0.5f, 0.8f, 0.05f), Phys.RHO_W, 0f).Force;   // does not reach the mouth
        _out.WriteLine($"crush claw {claw:F2} soft {soft:F2} rod {rigidRod:F2} slack {slack:F2} short {shortArm:F2} N");
        Assert.True(claw > 3f * soft && claw > 3f * rigidRod && claw > 3f * slack && claw > 3f * shortArm);
        Assert.InRange(claw, 2f, 200f);                                       // crab chelae: ~10-100 N
        // more force opens more of the shells, and handling slows as shells near the force (Elner & Hughes 1978)
        float o1 = Physics.Openable(1f, 0.1f, 300f, out float m1), o2 = Physics.Openable(30f, 0.1f, 300f, out float m2);
        Assert.True(o2 > o1 && m2 > m1);
        Assert.True(Physics.HandlingTime(20f, 25f, 30f, 3e-4f, 0.25f) > Physics.HandlingTime(20f, 5f, 30f, 3e-4f, 0.25f));
        Assert.True(Physics.HandlingTime(20f, 5f, 30f, 3e-4f, 0.001f) > 3f * Physics.HandlingTime(20f, 5f, 30f, 3e-4f, 0.25f));   // too big for a small mouth (Werner 1974)
    }

    [Fact]
    public void HollingTypeTwoIntakeSaturatesAtOneOverHandlingTime()
    {
        float lo = Physics.Holling(0f, 0.001f, 1000f, 20f, out float sLo);
        float hi = Physics.Holling(0f, 10f, 1000f, 20f, out float sHi);
        Assert.InRange(lo, 0.95f, 1.0f);              // sparse: ~ a N E
        Assert.InRange(hi, 49f, 50f);                 // abundant: -> E / h
        Assert.True(sLo > 0.95f && sHi < 0.01f);      // and it spends its time handling, not searching
    }

    [Fact]
    public void ShelledPreyOnTheFloorRewardAClaw()
    {
        var env = MorphEnv.SeaFloor();
        var with = Physics.Evaluate(Clawed(1f, 0.5f, 0.8f), env);
        var without = Physics.Evaluate(Clawed(1f, 0.5f, 0f), env);
        _out.WriteLine($"claw: items {with.ItemShare:F2} crush {with.CrushForce:F1} N fit {with.Fitness:F2}; slack: items {without.ItemShare:F2} crush {without.CrushForce:F1} N fit {without.Fitness:F2}");
        Assert.True(with.ItemShare > without.ItemShare + 0.1f);
        Assert.True(with.Intake > without.Intake);
    }

    static Morphology Finned(float muscle, float joints, float flatChord = 0.6f) =>
        Development.Grow(Probe(3, 0.2f, 0f, 0.3f, 0f, 0.5f, 1, 0.55f, flatChord, 0.1f, 0f, joints, muscle, 0.3f, 0.2f, 0f, 0.6f, 0.45f, 0.45f));

    [Fact]
    public void MobileFlatPairedSurfacesTightenTheTurnAndATightTurnEscapesAFasterPursuer()
    {
        float rFins = Physics.TurnRadius(Finned(0.8f, 0.5f), Phys.RHO_W, 1f);
        float rPassive = Physics.TurnRadius(Finned(0f, 0.5f), Phys.RHO_W, 1f);
        float rRods = Physics.TurnRadius(Finned(0.8f, 0.5f, 0.1f), Phys.RHO_W, 1f);
        var rigid = Development.Grow(Probe(3, 0.6f, 1f, 0.3f, 0f, 0.5f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0.6f, 0.45f, 0.45f));
        float rRigid = Physics.TurnRadius(rigid, Phys.RHO_W, 1f);
        _out.WriteLine($"turn radius: muscled flat fins {rFins:F3}, passive {rPassive:F3}, muscled rods {rRods:F3}, rigid finless {rRigid:F3} m");
        Assert.True(rFins < 0.8f * rPassive);
        Assert.True(rFins < rRods);
        Assert.True(rRigid > rPassive);
        Assert.True(rFins >= Phys.R_MIN_L * Finned(0.8f, 0.5f).Length);       // never tighter than ~0.1 L
        // Howland (1974): a slower prey escapes if it turns tightly enough; at equal radii only speed counts
        Assert.True(Physics.HowlandEscape(1f, 0.05f, 1.5f, 0.3f) > 0.5f);
        Assert.True(Physics.HowlandEscape(1f, 0.3f, 1.5f, 0.3f) < 0.5f);
        // clutter slows a wide turner more than a tight one (Webb 1984)
        Assert.True(Physics.ClutterSpeed(1f, 0.05f, 0.3f) > 1.5f * Physics.ClutterSpeed(1f, 0.5f, 0.3f));
    }

    [Fact]
    public void ObstaclesCostTheWorkOfLiftingTheBodyAndHeightGivesNothingForFree()
    {
        // the body's own height no longer clears obstacles: a crawler lifts its whole weight by h, a limb clearance spares it
        float crawl = Physics.Obstacles(10f, 0.03f, 0.3f, 0f, 0.1f, 0.1f, out float uC);
        float legs = Physics.Obstacles(10f, 0.03f, 0.3f, 0.06f, 0.1f, 0.1f, out float uL);
        Assert.Equal(10f * 0.03f * 0.1f / 0.3f / Phys.ETA_M, crawl, 4);
        Assert.True(legs < 0.4f * crawl);
        Assert.Equal(uC, uL, 4);
        Physics.Obstacles(10f, 0.03f, 0.3f, 0f, 0.01f, 0.1f, out float uShort);   // cannot reach the top: detours
        Assert.True(uShort < 0.5f * uC);
        // a tall narrow body topples on rough ground; a low wide one does not
        Assert.True(Physics.Topple(0.05f, 0.01f, 1f) > 0.9f);
        Assert.True(Physics.Topple(0.01f, 0.05f, 1f) < 0.01f);
        // at the same mass, a tall body does not out-crawl a low one over obstacles (the old tall-body artefact)
        var env = MorphEnv.Land();
        var low = Physics.Evaluate(Development.Grow(Probe(4, 0.4f, 0f, 0.2f, 1f, 0.6f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0.5f, 0.6f, 0.4f)), env);
        var tall = Physics.Evaluate(Development.Grow(Probe(4, 0.4f, 0f, 0.2f, 1f, 0.6f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0.5f, 0.4f, 0.6f)), env);
        _out.WriteLine($"land crawler low {low.Fitness:F2} ({low.Speed:F3} m/s), tall {tall.Fitness:F2} ({tall.Speed:F3} m/s)");
        Assert.True(tall.Fitness <= low.Fitness + 0.05f);
    }

    [Fact]
    public void CrawlingCostsAboutWhatWalkingDoesAndAVestigialLimbIsNoDiscount()
    {
        // Walton, Jayne & Bennett (1990): lateral undulation costs about what legs do at the same mass
        var crawler = Development.Grow(Probe(4, 0.4f, 0f, 0.2f, 1f, 0.6f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0.6f, 0.5f, 0.5f));
        var wc = Physics.Walk(crawler, Phys.RHO_A);
        float cCrawl = Physics.WalkMetabolic(crawler, wc, 0f, Phys.RHO_A, 0.1f, 10f, out _) / (crawler.Mass * 0.1f);
        float taylor = 10.7f * MathK.Pow(crawler.Mass, -0.32f);
        _out.WriteLine($"crawl COT {cCrawl:F1} J/(kg m), Taylor walking {taylor:F1}");
        Assert.InRange(cCrawl / taylor, 0.4f, 3f);
        // a muscle-less stub "leg" is not a cheaper way to crawl
        var stub = Development.Grow(Probe(4, 0.4f, 0f, 0.2f, 1f, 0.6f, 1, 0.3f, 0.3f, 0.3f, 0.9f, 0.6f, 0.02f, 0.3f, 0.3f, 0f, 0.6f, 0.5f, 0.5f));
        var ws = Physics.Walk(stub, Phys.RHO_A);
        float stubCost = Physics.WalkMetabolic(stub, ws, 0f, Phys.RHO_A, 0.1f, 10f, out bool legged);
        float stubCrawl = Physics.WalkMetabolic(stub, new Physics.WalkProps { WEff = ws.WEff }, 0f, Phys.RHO_A, 0.1f, 10f, out _);
        Assert.True(!legged || stubCost >= 0.97f * stubCrawl);
    }

    [Fact]
    public void PatchyFoodChargesTravelSoTravelGrowsWithSize()
    {
        var env = MorphEnv.Land();
        var g = Probe(4, 0.5f, 0f, 0.2f, 1f, 0.6f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0.5f, 0.5f, 0.5f);
        var small = AtMass(0.01f, () => Physics.Evaluate(Development.Grow(g), env));
        var big = AtMass(2f, () => Physics.Evaluate(Development.Grow(g), env));
        _out.WriteLine($"travel share 10 g {small.TravelShare:F3}, 2 kg {big.TravelShare:F3}");
        Assert.True(big.TravelShare > small.TravelShare);   // a bigger gut empties a patch sooner (Charnov 1976)
        var even = MorphEnv.Land(); even.PatchGap = 0f;
        var evenE = Physics.Evaluate(Development.Grow(g), even);
        var patchE = Physics.Evaluate(Development.Grow(g), env);
        Assert.True(patchE.Net < evenE.Net);
    }

    [Fact]
    public void PredatorsScaleWithTheirPreySoSizeAloneDoesNotArmour()
    {
        // the same shell scaled up is crushed by a scaled-up bite just as often (Brose et al. 2006 size spectrum)
        var g = Probe(2, 0.5f, 1f, 0f, 0f, 0.2f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var envF = MorphEnv.SeaFloor();
        float pSmall = AtMass(0.01f, () => Physics.Evaluate(Development.Grow(g), envF).PKill);
        float pBig = AtMass(5f, () => Physics.Evaluate(Development.Grow(g), envF).PKill);
        Assert.Equal(pSmall, pBig, 2);
    }

    [Fact]
    public void CrossoverAlignsHomologousModulesAndDistanceCountsTheUnmatched()
    {
        var inn = new Innovations();
        var a = WithLimb();
        a.Segs[0].Apps[0].Innov = inn.Next();
        Mutate.DuplicateSegment(a, 0, inn);
        var b = a.Clone();
        Assert.Equal(0f, Homology.Distance(a, b), 5);
        var r = new Rng.Seq(9);
        Mutate.InsertAppendage(b, 1, ref r, inn);                     // b has one module a lacks
        b.Segs[0].P[SP.INT_MIN] = 1f;                                 // and a matched one differs
        float d = Homology.Distance(a, b);
        Assert.True(d > 0.2f);
        Assert.Equal(d, Homology.Distance(b, a), 4);
        // the child keeps the fitter parent's structure; matched modules come from either parent
        bool sawA = false, sawB = false;
        for (int k = 0; k < 40; k++)
        {
            var c = Homology.Cross(b, a, ref r);
            Assert.Equal(b.Segs.Count, c.Segs.Count);
            Assert.Equal(b.AppGeneCount, c.AppGeneCount);
            if (c.Segs[0].P[SP.INT_MIN] == 1f) sawB = true; else sawA = true;
            var c2 = Homology.Cross(a, b, ref r);
            Assert.Equal(a.AppGeneCount, c2.AppGeneCount);            // b's extra module (disjoint, less fit parent) is not inherited
        }
        Assert.True(sawA && sawB);
    }

    [Fact]
    public void SpeciationKeepsSeveralBodyPlansAlive()
    {
        var r = Evolver.Run(MorphEnv.SeaFloor(), 3u, 60, 80);
        _out.WriteLine($"species {r.Species}");
        Assert.True(r.Species >= 3);
        for (int i = 1; i < r.BestByGen.Length; i++) Assert.True(r.BestByGen[i] >= r.BestByGen[i - 1] - 1e-5f);   // the best is always kept
    }

    // ---------- phase 2b (docs/BODYPLAN.md §11)

    static MorphGenome WithGas(MorphGenome g, float gas, float invag)
    {
        foreach (var s in g.Segs) { s.P[SP.GAS] = gas; s.P[SP.INVAG] = invag; }
        return g;
    }

    [Fact]
    public void AGasSpaceMakesASwimmerNeutralAndTooMuchGasPaysAgain()
    {
        float Power(float gas)
        {
            var m = Development.Grow(WithGas(Probe(4, 0.1f, 0f, 0f, 0f, 0.8f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, lenG: 0.7f), gas, 0f), 0.1f);
            return Physics.SwimPower(Physics.Swim(m, Phys.RHO_W), Phys.RHO_W, 0.2f);
        }
        var m0 = Development.Grow(Probe(4, 0.1f, 0f, 0f, 0f, 0.8f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, lenG: 0.7f), 0.1f);
        float best = float.PositiveInfinity, bestG = 0f;
        for (float gg = 0f; gg <= 1f; gg += 0.02f) { float p = Power(gg); if (p < best) { best = p; bestG = gg; } }
        var mn = Development.Grow(WithGas(Probe(4, 0.1f, 0f, 0f, 0f, 0.8f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, lenG: 0.7f), bestG, 0f), 0.1f);
        _out.WriteLine($"no gas {m0.Mass / m0.Volume:F0} kg/m3, {Power(0f) * 1000f:F2} mW; best gas gene {bestG:F2}: {mn.Mass / mn.Volume:F0} kg/m3, {best * 1000f:F2} mW; full gas {Power(1f) * 1000f:F2} mW");
        Assert.True(m0.Mass / m0.Volume > Phys.RHO_W);
        Assert.InRange(mn.Mass / mn.Volume, Phys.RHO_W * 0.98f, Phys.RHO_W * 1.02f);   // the least cost is at neutral buoyancy
        Assert.True(best < 0.8f * Power(0f));
        Assert.True(Power(1f) > 1.2f * best);                                           // an over-light body holds itself down
        Assert.Equal(m0.Mass, mn.Mass, 3);                                               // the gas lightens the body by its volume, not its mass
    }

    [Fact]
    public void AGasSpaceLinedWithExchangeEpitheliumIsALungBoundedByVolumeThatBreathesAirOnly()
    {
        var g = WithGas(Probe(4, 0.1f, 0f, 0f, 0f, 0.3f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), 0.6f, 1f);
        var small = Development.Grow(g, 0.1f); var big = Development.Grow(g, 10f);
        float area(Morphology m) { float a = 0f; foreach (var s in m.Segs) a += s.Area; return a; }
        float o2Air = Physics.O2Supply(big, Medium.Air, out _), o2Water = Physics.O2Supply(big, Medium.Water, out _);
        var bladder = Development.Grow(WithGas(Probe(4, 0.1f, 0f, 0f, 0f, 0.3f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), 0.6f, 0f), 10f);
        _out.WriteLine($"lung area 0.1 -> 10 kg: x{big.ExchGas / small.ExchGas:F1} (gas x{big.GasV / small.GasV:F0}, body surface x{area(big) / area(small):F1}); 10 kg O2 air {o2Air:F1} W, water {o2Water:F2} W, unlined bladder air {Physics.O2Supply(bladder, Medium.Air, out _):F2} W");
        Assert.InRange(big.ExchGas / small.ExchGas, 0.9f * MathK.Pow(100f, 0.89f), 1.1f * MathK.Pow(100f, 0.89f));   // ~M^0.95 / M^0.06 (Gehr et al. 1981; Stahl 1967)
        Assert.True(big.ExchGas / small.ExchGas > 2f * area(big) / area(small));      // it outgrows the surface (M^(2/3))
        Assert.True(o2Air > 20f * o2Water);                                            // under water it holds a breath, not a supply
        Assert.True(Physics.O2Supply(bladder, Medium.Air, out _) < 0.05f * o2Air);     // an unlined gas space is a swim bladder
    }

    [Fact]
    public void AThinDeepSheetBendsAndRollsOverWhereARoundBodyOfTheSameMassHoldsUpright()
    {
        // the Body Lab's sea-floor sheet: 77 mm deep, 2.6 mm thin, soft
        var sheet = new Segment { L = 0.1f, W = 0.0026f, H = 0.077f, IntT = 2.6e-5f, IntMin = 0f };
        float cap = Physics.FacePressureCap(sheet);
        float qEscape = 0.5f * Phys.RHO_W * 1.5f * 1.5f;
        _out.WriteLine($"sheet face bears {cap:F0} Pa against {qEscape:F0} Pa at a 1.5 m/s escape");
        Assert.InRange(cap, 150f, 350f);
        Assert.True(cap < 0.3f * qEscape);
        float Hold(float widG, float heiG, out float plate)
        {
            var m = Development.Grow(Probe(4, 0.1f, 0f, 0f, 0f, 0.8f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, widG: widG, heiG: heiG), 0.05f);
            plate = Physics.PlateFactor(m, qEscape * Phys.CL_MAX);
            return Physics.RollHold(m, Phys.RHO_W, 0.3f, Physics.Walk(m, Phys.RHO_W).WEff);
        }
        float flat = Hold(0f, 1f, out float pf), round = Hold(0.5f, 0.5f, out float pr);
        _out.WriteLine($"roll hold in 0.3 m/s surge: sheet {flat:F3}, round {round:F3}; share of turning load borne: sheet {pf:F2}, round {pr:F2}");
        Assert.True(flat < 0.2f);
        Assert.True(round > 0.8f && round > 20f * flat);
        Assert.True(pf < 0.5f && pr > 0.99f);
    }

    [Fact]
    public void APairCanCarryAFliersShareOfFlightMuscle()
    {
        // one segment, one pair at full muscle and no axial muscle: 0.6 of the tissue, a bird's 0.35 of its body possible
        var g = Probe(1, 0.05f, 0f, 0f, 0f, 0f, 1, 0.8f, 0.7f, 0.1f, 0f, 0.5f, 1f, 0.6f, 0.1f, 0.8f);
        var m = Development.Grow(g, 0.1f);
        float fm = 0f; foreach (var a in m.Pairs) fm += 2f * a.Muscle;
        _out.WriteLine($"flight muscle {fm / m.Mass:F2} of the body");
        Assert.True(fm / m.Mass >= 0.35f);
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

    /// <summary>Fitness of one shape at masses from 1 g to 10 kg: what the allometry alone says about size.</summary>
    static string MassScan(MorphGenome g, MorphEnv env)
    {
        var sb = new StringBuilder();
        float keep = Development.FixedMass;
        try
        {
            foreach (float mass in new[] { 0.001f, 0.01f, 0.1f, 0.25f, 1f, 10f })
            {
                Development.FixedMass = mass;
                var m = Development.Grow(g);
                var e = Physics.Evaluate(m, env);
                sb.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}kg fit {1:F2} (net/basal {2:F2}, mort {3:F2}, {4} {5:F3} m/s, items {6:F0}%)  ",
                    mass, e.Fitness, e.Net / e.Basal, e.Mortality, Evolver.Mode(e), e.Speed, 100f * e.ItemShare));
            }
        }
        finally { Development.FixedMass = keep; }
        return sb.ToString();
    }

    [Fact, Trait("Category", "Slow")]
    public void EvolutionDemo()
    {
        var c = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("env         seed   fit  mass kg segs pairs  fine rigid integ% moult  exchExt exchInt  mode    speed crushN items% turnR  spp  pairs (seg, length, use, mineral, moment N m, joints, muscle, flatness, droop)");
        string detail = "";
        string only = Environment.GetEnvironmentVariable("NR_MORPH_ENV") ?? "";
        bool flat = Environment.GetEnvironmentVariable("NR_MORPH_NOSPECIATE") == "1";
        float keepMass = Development.FixedMass;
        if (float.TryParse(Environment.GetEnvironmentVariable("NR_MORPH_FIXMASS"), System.Globalization.NumberStyles.Float, c, out var fm)) Development.FixedMass = fm;
        try { DemoBody(c, sb, ref detail, only, flat); } finally { Development.FixedMass = keepMass; }
        _out.WriteLine(sb.ToString());
        _out.WriteLine(detail);
    }

    void DemoBody(System.Globalization.CultureInfo c, StringBuilder sb, ref string detail, string only, bool flat)
    {
        var landEnv = MorphEnv.Land();
        if (float.TryParse(Environment.GetEnvironmentVariable("NR_MORPH_LANDFOOD"), System.Globalization.NumberStyles.Float, c, out var lf)) landEnv.FoodArea = lf;
        if (float.TryParse(Environment.GetEnvironmentVariable("NR_MORPH_LANDGAP"), System.Globalization.NumberStyles.Float, c, out var lgap)) landEnv.PatchGap = lgap;
        foreach (var env in new[] { MorphEnv.OpenWater(), MorphEnv.SeaFloor(), landEnv })
        {
            if (only.Length > 0 && !env.Name.Contains(only)) continue;
            for (uint seed = 1; seed <= Seeds; seed++)
            {
                var r = Evolver.Run(env, seed, 200, Gens, null, new SearchOptions { Speciate = !flat });
                var m = r.BestBody; var e = r.BestEval;
                var modes = new System.Collections.Generic.Dictionary<string, int>();
                foreach (var ch in r.Champions)
                {
                    var cm = Development.Grow(ch.G); var ce = Physics.Evaluate(cm, env);
                    string k = Evolver.Mode(ce) + (cm.Rigidity > 0.5f ? "-rigid" : "");
                    modes[k] = modes.TryGetValue(k, out var v) ? v + 1 : 1;
                }
                string spp = string.Join(",", System.Linq.Enumerable.Select(modes, kv => kv.Key + ":" + kv.Value));
                sb.AppendLine(string.Format(c,
                    "{0,-11} {1,4} {2,5:F2} {3,8:F3} {4,4} {5,5} {6,5:F1} {7,5:F2} {8,5:F1} {9,5:F3} {10,7:F4} {11,7:F4}  {12,-6} {13,5:F2} {14,6:F1} {15,5:F0} {16,5:F2} {17,3} [{18}]  {19}",
                    env.Name, seed, e.Fitness, m.Mass, m.SegmentCount, m.PairCount, m.Fineness, m.Rigidity, 100f * m.IntMass / m.Mass, e.MoultShare,
                    m.ExchExt, m.ExchInt, Evolver.Mode(e), e.Speed, e.CrushForce, 100f * e.ItemShare, MathF.Min(e.TurnR, 99f), r.Species, spp,
                    Evolver.PairLine(m, env, e)));
                if (seed == 1)
                {
                    detail += $"== {env.Name} (seed 1)  final population: mean segments {r.MeanSegments:F1}, mean pairs {r.MeanPairs:F1}, species {r.Species}\n" + Evolver.Describe(m, e, env) + "\n";
                    detail += "   mass scan of the best (same shape): " + MassScan(r.Best, env) + "\n";
                    for (int k = 1; k < Math.Min(3, r.Champions.Count); k++)
                    {
                        var cm = Development.Grow(r.Champions[k].G);
                        detail += $"   -- species champion {k} (size {r.Champions[k].Size})\n" + Evolver.Describe(cm, Physics.Evaluate(cm, env), env) + "\n";
                    }
                }
            }
        }
    }
}
