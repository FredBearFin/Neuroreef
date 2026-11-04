// Phases 3-4 (docs/BODYPLAN.md §13): the module genome stored in the live genome, mutated and recombined by the Morph
// operators, translated from older genomes, and expressed by the module physics.

using System;
using System.IO;
using System.Linq;
using Neuroreef.Morph;
using Xunit;

namespace Neuroreef.Tests;

public class MorphLiveTests
{
    static sbyte[] Genome(int seed)
    {
        var g = new sbyte[G.LEN];
        Clades.AncestorBody(new Span<sbyte>(g, G.BODY, G.NBODY), (uint)seed, seed, true);
        Clades.AncestorMorph(g, (uint)seed, seed);
        return g;
    }

    [Fact] // the block holds the genome it was given (to 1/254 a parameter) with its innovation numbers, within its capacity
    public void TheBlockRoundTripsAndIsBounded()
    {
        Assert.Equal(697, G.MORPHN);   // 2026-11-04: 681 + the internal skeleton's tail, 8 segment genes x 2 (docs/BODYPLAN.md §13.19)
        Assert.Equal(G.MORPH + G.MORPHN, G.LEN);
        for (int s = 1; s <= 20; s++)
        {
            var g = Genome(s);
            Assert.True(Codec.Has(g));
            var m = Codec.Decode(Codec.Block((ReadOnlySpan<sbyte>)g))!;
            Assert.InRange(m.Segs.Count, 1, MorphGenome.MAX_SEG_GENES);
            Assert.All(m.Segs, x => Assert.InRange(x.Apps.Count, 0, MorphGenome.MAX_APP_GENES));
            var again = new sbyte[Codec.SIZE];
            Codec.Encode(m, again);
            Assert.True(again.AsSpan().SequenceEqual(Codec.Block((ReadOnlySpan<sbyte>)g)));
        }
    }

    [Fact] // mutation changes the number of modules as well as their values, and every new module has a new innovation number
    public void MutationDuplicatesInsertsAndDeletesModules()
    {
        var g = Genome(3);
        var before = Codec.Decode(Codec.Block((ReadOnlySpan<sbyte>)g))!;
        int changedCount = 0;
        for (uint step = 0; step < 400; step++)
        {
            var c = (sbyte[])g.Clone();
            Codec.MutateBlock(c, 7, step, 11);
            var m = Codec.Decode(Codec.Block((ReadOnlySpan<sbyte>)c))!;
            if (m.Segs.Count != before.Segs.Count || m.AppGeneCount != before.AppGeneCount) changedCount++;
            var old = before.Segs.Select(x => x.Innov).Concat(before.Segs.SelectMany(x => x.Apps).Select(a => a.Innov)).ToHashSet();
            foreach (var x in m.Segs) if (!old.Contains(x.Innov)) Assert.True(x.Innov >= Codec.LIVE_INNOV_LO);
            Assert.InRange(m.Segs.Count, 1, MorphGenome.MAX_SEG_GENES);
        }
        Assert.True(changedCount > 20, $"structure changed in {changedCount} of 400 offspring");
        // the same draw is the same mutation (no shared counter)
        var a1 = (sbyte[])g.Clone(); var a2 = (sbyte[])g.Clone();
        Codec.MutateBlock(a1, 7, 5, 11); Codec.MutateBlock(a2, 7, 5, 11);
        Assert.True(a1.AsSpan().SequenceEqual(a2));
    }

    [Fact] // crossover keeps homologous modules aligned: a child of two copies of one genome is that genome; distance counts
           // unmatched modules as unrelated genes and matched ones by their parameters
    public void CrossoverAlignsByInnovationAndDistanceSeesStructure()
    {
        var g = Genome(5);
        var child = new sbyte[G.LEN];
        Codec.CrossBlock(child, g, g, 1, 2, 3);
        Assert.True(Codec.Block((ReadOnlySpan<sbyte>)child).SequenceEqual(Codec.Block((ReadOnlySpan<sbyte>)g)));
        Assert.Equal(0f, Codec.Distance(g, g));
        var h = Genome(6);
        float dUnrelated = Codec.Distance(g, h);
        var near = (sbyte[])g.Clone();
        Codec.MutateBlock(near, 1, 1, 1);
        Assert.True(Codec.Distance(g, near) < dUnrelated, $"a mutant {Codec.Distance(g, near):F3}, an unrelated genome {dUnrelated:F3}");
        Assert.InRange(dUnrelated, 0.3f, 1f);
    }

    [Fact] // a gene bank of the format before the block (13) is read at its old genome length and translated (Morph.Translate)
    public void AnOlderGeneBankIsTranslated()
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            bw.Write("NRBK"u8.ToArray()); bw.Write(GeneBank.MorphFrom); bw.Write((ushort)K.CLADES); bw.Write(G.MORPH); bw.Write(16u);
            bw.Write(5UL); bw.Write(9u);
            var old = new sbyte[G.MORPH];
            Clades.AncestorBody(new Span<sbyte>(old, G.BODY, G.NBODY), 9, 2, true);
            for (int sp = 0; sp < K.CLADES; sp++)
            {
                if (sp != 0) { bw.Write((byte)0); continue; }
                bw.Write((byte)1); bw.Write((byte)1); bw.Write((byte)'A'); bw.Write((byte)1); bw.Write((byte)2); bw.Write((byte)3);
                bw.Write((short)-1); bw.Write((byte)0); bw.Write((ushort)0); bw.Write(1u); bw.Write((ushort)1);
                bw.Write(1f); bw.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(old.AsSpan()));
            }
        }
        ms.Position = 0;
        var d = GeneBank.Load(ms);
        var g = d.Genomes[0][0];
        Assert.Equal(G.LEN, g.Length);
        Assert.True(Codec.Has(g));
        var expect = new sbyte[G.LEN]; Array.Copy(g, expect, G.MORPH);
        Codec.TranslateInto(expect);
        Assert.True(g.AsSpan().SequenceEqual(expect));
    }

    [Fact] // a rigid outer skeleton must be moulted to grow: a growing rigid body is soft for a share of its time (and then has
           // no armour), a soft-skinned one and an adult never
    public void ARigidBodyMoultsWhileItGrowsAndIsSoftThen()
    {
        var w = new World(16, 1);
        var g = w.GenomeOf(0);
        g.Clear();
        g[G.B_ARMOUR] = 127; g[G.B_LEGS] = 127; g[G.B_MASS] = -60;
        w.StageOf[0] = St.Juvenile; w.Grown[0] = 0.3f; w.Serial[0] = 1;
        Body.Express(w, 0);
        Assert.True(w.PMoultSoft[0] > 0.1f && w.PMoultW[0] > 0f, $"soft share {w.PMoultSoft[0]}");
        int soft = 0, n = 2000;
        for (int t = 0; t < n; t++) { w.Age[0] = t * 0.37f; if (w.Moulting(0)) soft++; }
        Assert.InRange(soft / (float)n, 0.5f * w.PMoultSoft[0], 1.5f * w.PMoultSoft[0]);
        w.StageOf[0] = St.Adult; w.Grown[0] = 1f;
        Assert.False(w.Moulting(0));
        g[G.B_ARMOUR] = -127; w.StageOf[0] = St.Juvenile; w.Grown[0] = 0.3f; Body.Express(w, 0);
        Assert.True(w.PMoultSoft[0] < 0.2f * Live.MOULT_SOFT_GROW);
    }

    // ---- loopholes closed in §13.17 (2026-11-03): body dimensions or parts that gave a benefit without the tissue to back it

    /// <summary>The land world's most common lineage at step 1,500 of seed 3 (Anithmo, 0.309 kg): a 2-segment trunk of
    /// 22 g (5 x 43 x 57 mm and 3 x 73 x 68 mm) carrying three pairs of porous plates up to 79 mm long, 93% of its mass.</summary>
    static MorphGenome Anithmo()
    {
        var g = new MorphGenome();
        var s0 = new SegGene { Innov = 1 };
        new[] { 0.1456693f, 0.7637795f, 0.8464567f, 0.992126f, 0.24409449f, 0.21653543f, 0.36220473f, 0.515748f, 0.8385827f, 0.8070866f, 0.72047246f, 0.22047244f }.CopyTo(s0.P, 0);
        var a0 = new AppGene { Innov = 2 };
        new[] { 0.87401575f, 1f, 0.8031496f, 0.46850392f, 0.20866142f, 0.35826772f, 0.11023622f, 0.9015748f, 0.71653545f, 0.72047246f, 0.32677165f, 0.6417323f, 0.11023622f }.CopyTo(a0.P, 0);
        var a1 = new AppGene { Innov = 3 };
        new[] { 0.2007874f, 0.7637795f, 0.95669293f, 0.2480315f, 0.17322835f, 0.43700787f, 0.38582677f, 0.9173228f, 0.7440945f, 0.78740156f, 0.57086617f, 0.31889763f, 0.8031496f }.CopyTo(a1.P, 0);
        s0.Apps.Add(a0); s0.Apps.Add(a1);
        var s1 = new SegGene { Innov = 4 };
        new[] { 0.003937008f, 0.9212598f, 0.9015748f, 0.8937008f, 0.003937008f, 0.41338584f, 0.16929133f, 0.2519685f, 0.72440946f, 0.515748f, 0.87007874f, 0.31889763f }.CopyTo(s1.P, 0);
        var a2 = new AppGene { Innov = 5 };
        new[] { 0.88582677f, 1f, 0.78740156f, 0.511811f, 0.27559054f, 0.31889763f, 0.08267716f, 0.9015748f, 0.8267717f, 0.7677165f, 0.32677165f, 0.6062992f, 0.07874016f }.CopyTo(a2.P, 0);
        s1.Apps.Add(a2);
        g.Segs.Add(s0); g.Segs.Add(s1);
        return g;
    }

    [Fact] // the gut is tissue in the trunk: a body that is nearly all appendage cannot build a plain body's gut (it did: the
           // live gut was sized to its basal whatever its trunk), while a plain body, with room for ~2.9 guts, is not bound
    public void AGutMustFitInTheTrunkSoABodyOfAppendagesCannotDigestLikeAPlainOne()
    {
        var m = Development.Grow(Anithmo(), 0.309f);
        float trunk = 0f; foreach (var s in m.Segs) trunk += s.TissueMass + s.FatMass + s.IntMass;
        Assert.InRange(trunk / m.Mass, 0.05f, 0.10f);                 // 22 g of 309
        var lc = Live.Of(m, 1f, 1f, 0f);
        Assert.InRange(lc.GutRoom, 0.1f, 0.2f);                       // 3.4 g of visceral tissue against a reference gut of 22 g
        Assert.True(lc.Gut <= lc.GutRoom + 1e-6f, $"gut {lc.Gut} room {lc.GutRoom}");
        // the plain body of the same mass, and at 0.01-30 kg, has room for more than the largest gut it would build
        foreach (float mass in new[] { 0.01f, 0.309f, 1f, 30f })
        {
            Shadow.Of(Translate.Reference(mass), 1f, 0f, out var plain);
            float room = Live.GutRoom(plain);
            Assert.True(room > K.GUT_FLEX_MAX, $"plain body at {mass} kg: room for {room} guts");
        }
        // in the live world: it absorbs and digests in proportion to the gut it can house, and holds a gut-full in proportion
        var w = new World(16, 1);
        var gn = w.GenomeOf(0);
        gn.Clear(); gn[G.B_MASS] = 0;
        Codec.Encode(Anithmo(), Codec.Block(gn));
        w.StageOf[0] = St.Adult; w.Grown[0] = 1f; w.Serial[0] = 1;
        Body.Express(w, 0);
        float m0 = w.PM[0];
        float absorbAnithmo = w.PAbsorb[0], gutAnithmo = w.PGut[0];
        Codec.Encode(MorphGenome.Blob(), Codec.Block(gn));   // a plain one-segment body of the same mass gene
        Body.Express(w, 0);
        Assert.Equal(m0, w.PM[0]);
        Assert.True(absorbAnithmo < 0.3f * w.PAbsorb[0], $"absorb {absorbAnithmo} against a trunk's {w.PAbsorb[0]}");
        Assert.True(gutAnithmo < 0.3f * w.PGut[0], $"gut-full {gutAnithmo} against {w.PGut[0]}");
        // its porous plates are where it breathes (Live.PairO2Share, the "breathe" role of a pair with no other use)
        Assert.True(Live.PairO2Share(m, m.Pairs[0]) > 0.3f, $"share {Live.PairO2Share(m, m.Pairs[0])}");
    }

    [Fact] // a tall, thin trunk tips over on rough ground and lies on its side, so it stands about as high as it is wide; a
           // round body of the same mass stands its height (before, every body stood its tallest segment whatever its base)
    public void ATallThinPlateLiesOnItsSideAndStandsNoHigherThanItIsWide()
    {
        MorphGenome Trunk(float wid, float hei)
        {
            var g = MorphGenome.Blob();
            g.Segs[0].P[SP.LEN] = 0.5f; g.Segs[0].P[SP.WID] = wid; g.Segs[0].P[SP.HEI] = hei; g.Segs[0].P[SP.AXMUS] = 0.6f;
            return g;
        }
        var plate = Development.Grow(Trunk(0.2f, 1f), 9f);
        var round = Development.Grow(Trunk(0.6f, 0.6f), 9f);
        Assert.True(plate.MaxH > 8f * plate.MaxW, $"plate {plate.MaxH} x {plate.MaxW}");
        float sPlate = Live.Of(plate, 1f, 1f, 0f).C.Stand, sRound = Live.Of(round, 1f, 1f, 0f).C.Stand;
        Assert.True(sPlate < 1.5f * plate.MaxW, $"plate stands {sPlate} m, {plate.MaxH} high and {plate.MaxW} wide");
        Assert.InRange(sRound, 0.95f * round.MaxH, 1.0001f * round.MaxH);
    }

    [Fact] // a weightless thread trailing behind a swimmer carries no muscle to make the body wave, so it does not raise the
           // twitch limit on burst swimming (Wardle 1975); on nose-to-tail length it raised it 30-60%
    public void AWeightlessThreadDoesNotRaiseTheBurstSwimmingLimit()
    {
        var g = MorphGenome.Blob();
        g.Segs[0].P[SP.LEN] = 0.9f; g.Segs[0].P[SP.WID] = 0.6f; g.Segs[0].P[SP.HEI] = 0.6f; g.Segs[0].P[SP.AXMUS] = 0.8f; g.Segs[0].P[SP.PORE] = 0.3f;
        var t = g.Clone();
        var th = t.Segs[0].Clone(); th.Innov = 99; th.P[SP.LEN] = 1f; th.P[SP.WID] = 0f; th.P[SP.HEI] = 0f;
        t.Segs.Add(th);
        foreach (float mass in new[] { 0.01f, 0.3f, 3f })
        {
            var fish = Development.Grow(g, mass);
            var thread = Development.Grow(t, mass);
            Assert.True(thread.Length > 2f * fish.Length);
            Assert.True(thread.Segs[1].TissueMass < 0.03f * thread.Mass);
            float vFish = Live.Of(fish, 1f, 1f, 0f).C.VWater, vThread = Live.Of(thread, 1f, 1f, 0f).C.VWater;
            Assert.True(vThread < 1.05f * vFish, $"at {mass} kg: {vThread} m/s with the thread against {vFish}");
        }
    }

    [Fact] // a segment's side is its elliptic section's perimeter x its length (Ramanujan 1914); pi (a + b) was exact for a round
           // section and up to 21% short for a flat one, so a 1 m-wide crawling disc grew and paid for 13% too little side
           // (docs/BODYPLAN.md §13.18)
    public void AFlatSegmentHasTheWholeSurfaceOfItsSection()
    {
        static double Exact(double w, double h)
        {
            double a = 0.5 * w, b = 0.5 * h, sum = 0; int n = 20000;
            for (int k = 0; k < n; k++) { double t = (k + 0.5) * 2 * Math.PI / n; sum += Math.Sqrt(a * a * Math.Sin(t) * Math.Sin(t) + b * b * Math.Cos(t) * Math.Cos(t)); }
            return sum * 2 * Math.PI / n;
        }
        foreach (var (w, h) in new[] { (0.1f, 0.1f), (0.3f, 0.1f), (1.05f, 0.143f), (0.75f, 0.025f) })
        {
            double ex = Exact(w, h);
            Assert.InRange(Development.EllipsePerimeter(w, h) / ex, 0.999, 1.001);
        }
        Assert.True(Math.PI * 0.5 * (1.05 + 0.143) / Exact(1.05, 0.143) < 0.88);   // the old side of Tete's segments: 13% short
        // grown: a flat segment's side is the perimeter x its length (its exposed ends are added on top); a round one's is pi W L
        foreach (float wid in new[] { 0.75f, 1f })
        {
            var g = MorphGenome.Blob();
            g.Segs[0].P[SP.LEN] = 0.2f; g.Segs[0].P[SP.WID] = wid; g.Segs[0].P[SP.HEI] = 0.75f;
            var m = Development.Grow(g, 20f);
            var s = m.Segs[0];
            float ends = 2f * MathF.PI / 4f * s.W * s.H;
            Assert.InRange((s.Area - ends) / (float)(Exact(s.W, s.H) * s.L), 0.999f, 1.001f);
            if (wid == 0.75f) Assert.InRange((s.Area - ends) / (MathF.PI * s.W * s.L), 0.9999f, 1.0001f);
        }
    }

    [Fact] // a cold-blooded body's skin and gills are its own tissue, respiring at its metabolic level as its Kleiber term and its
           // gut do (Else & Hulbert 1981); at a warm body's rate a 12.6 kg cold fish paid its gills 54% of its Kleiber rate,
           // where gill tissue is a few to ~10% of a fish's standard metabolism (docs/BODYPLAN.md §13.18)
    public void AColdBodysSkinAndGillsCostItsMetabolicLevel()
    {
        var g = MorphGenome.Blob();
        g.Segs[0].P[SP.LEN] = 0.9f; g.Segs[0].P[SP.WID] = 0.6f; g.Segs[0].P[SP.HEI] = 0.6f; g.Segs[0].P[SP.PORE] = 1f; g.Segs[0].P[SP.INVAG] = 0.5f;
        g.Segs[0].P[SP.INT_T] = 0.3f;
        var m = Development.Grow(g, 12.6f);
        float cold = Heat.Level(0f);
        Assert.InRange(cold, 0.15f, 0.25f);
        Assert.Equal(cold * Live.ModuleUpkeep(m, 1f), Live.ModuleUpkeep(m, cold), 5);
        // the same shape builds the same gut, in reference guts, cold or warm (every term of its basal scales with its level)
        var lcCold = Live.Of(m, cold, 1f, 0f); var lcWarm = Live.Of(m, 1f, 1f, 0f);
        Assert.InRange(lcCold.Gut / lcWarm.Gut, 0.999f, 1.001f);
        Assert.InRange(lcCold.C.Basal / lcWarm.C.Basal, cold - 0.001f, cold + 0.001f);
        // its exchange epithelium's share of its Kleiber rate is a warm body's (0.04 here, at ~0.3 m² a kg), 1 / level less than
        // before (0.19)
        float kleiber = K.BMR_W * MathF.Pow(m.ActiveMass, K.EXP_BASAL) * cold;
        float gill = Phys.EXCH_TISSUE * cold * (m.ExchExt + m.ExchInt + m.ExchGas);
        Assert.InRange((m.ExchExt + m.ExchInt) / m.Mass, 0.15f, 0.5f);
        Assert.InRange(gill / kleiber, 0.02f, 0.15f);
    }
}
