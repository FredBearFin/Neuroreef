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
        Assert.Equal(681, G.MORPHN);
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
}
