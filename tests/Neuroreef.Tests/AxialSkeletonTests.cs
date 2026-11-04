// The internal axial skeleton (docs/BODYPLAN.md §13.19, 2026-11-04): segment genes SP.AX_SK (its share of the core's
// section) and SP.AX_MIN (its mineral), and the trunk-bending physics that makes it pay on land (Physics.TrunkShare,
// Development.BendOf). These tests check the physics' direction and scaling, and that a genome without the genes (every
// older genome) reads and grows as one with them at 0.

using System;
using System.Linq;
using Neuroreef.Morph;
using Xunit;
using Xunit.Abstractions;

namespace Neuroreef.Tests;

public class AxialSkeletonTests
{
    readonly ITestOutputHelper _out;
    public AxialSkeletonTests(ITestOutputHelper o) { _out = o; }

    public enum Kind { Soft, Exo, Endo }

    /// <summary>A four-legged land body: a fore segment and a hind segment, each with a pair of straight, jointed, muscled,
    /// bony legs (a central rod, hollow), and a longer trunk segment between them that holds the lungs. Its trunk is soft,
    /// has a mineralised integument (an exoskeleton), or an internal skeleton.</summary>
    internal static MorphGenome Quad(Kind k)
    {
        var g = new MorphGenome();
        SegGene Seg(uint innov, float len, bool legs)
        {
            var s = new SegGene { Innov = innov };
            s.P[SP.LEN] = len; s.P[SP.WID] = 0.6f; s.P[SP.HEI] = 0.62f; s.P[SP.TAPER] = 0.5f; s.P[SP.REPEAT] = 0f;
            s.P[SP.INT_T] = 0.1f; s.P[SP.INT_MIN] = k == Kind.Exo ? 1f : 0f;
            s.P[SP.PORE] = 0.01f; s.P[SP.INVAG] = 1f; s.P[SP.AXMUS] = 0.25f; s.P[SP.GAS] = legs ? 0f : 0.5f; s.P[SP.FAT] = 0.1f;
            s.P[SP.AX_SK] = k == Kind.Endo ? 0.15f : 0f; s.P[SP.AX_MIN] = k == Kind.Endo ? 1f : 0f;
            if (legs)
            {
                var a = new AppGene { Innov = innov + 100 };
                a.P[AP.LEN] = 0.8f; a.P[AP.CHORD] = 0.45f; a.P[AP.THICK] = 0.62f; a.P[AP.DROOP] = 1f; a.P[AP.JOINTS] = 0.45f;
                a.P[AP.MUSCLE] = 0.7f; a.P[AP.MINERAL] = 1f; a.P[AP.WALL] = 0.25f; a.P[AP.EXO] = 0f; a.P[AP.HOLLOW] = 0.65f;
                s.Apps.Add(a);
            }
            return s;
        }
        g.Segs.Add(Seg(1, 0.55f, true));
        g.Segs.Add(Seg(2, 0.8f, false));
        g.Segs.Add(Seg(3, 0.55f, true));
        return g;
    }

    /// <summary>The share of its weight its limbs alone could bear (Physics.Walk's limb sum, before the trunk's limit).</summary>
    static float LimbShare(Morphology m)
    {
        float cap = 0f;
        foreach (var a in m.Pairs)
        {
            var s = m.Segs[a.Seg];
            float sin = MathK.Sin(a.Theta), cos = MathK.Cos(a.Theta);
            float contact = MathF.Min(1f, a.Len * sin / (0.5f * s.H + 0.002f)); contact *= contact;
            cap += 2f * Physics.LimbForce(m, a, a.Len * (cos + 0.25f)) * contact * a.Artic;
        }
        float w = Phys.G * MathF.Max(0f, m.Mass - Phys.RHO_A * m.Volume);
        return MathF.Min(1f, cap * Phys.DUTY / (Phys.SAFETY * w));
    }

    [Fact] // the numbers of docs/BODYPLAN.md §13.19: a 15 kg land body with an internal skeleton, an exoskeleton or neither, and the same at 1 g
    public void LandBodiesBySkeleton()
    {
        var land = MorphEnv.Land();
        foreach (float mass in new[] { 0.001f, 0.25f, 1f, 5f, 15f, 40f })
            foreach (Kind k in Enum.GetValues<Kind>())
            {
                var m = Development.Grow(Quad(k), mass);
                var wp = Physics.Walk(m, Phys.RHO_A);
                var lc = Live.Of(m, 1f, 1f, 0f);
                var e = Physics.Evaluate(m, land);
                float bendMin = m.Segs.Min(s => s.BendCap);
                _out.WriteLine($"{mass,6:0.###} kg {k,-4}: endo {100 * m.EndoMass / m.Mass,5:0.0}% integ {100 * m.IntMass / m.Mass,5:0.0}% inert {100 * m.InertMass / m.Mass,5:0.0}% "
                    + $"muscle {100 * m.MuscleMass / m.Mass,4:0.0}% gutRoom {lc.GutRoom,4:0.00} | limbs {LimbShare(m),4:0.00} support {wp.Support,4:0.00} bend {bendMin:0.##E+0} N m clear {1000 * wp.Clear,5:0} mm | "
                    + $"vLand {lc.C.VLand,5:0.00} m/s CoT {lc.C.CotLand / (m.Mass * Phys.G),5:0.00} (J/(N m)) moult {lc.MoultW / lc.C.Basal * 100,5:0.0}% of basal, soft {100 * lc.MoultSoft,4:0.0}% | "
                    + $"proto: fit {e.Fitness,6:0.00} mode {Evolver.Mode(e)} sup {e.Support:0.00} moult {e.MoultShare:0.000}");
            }
        // what the soft trunk's strength is made of, and the least bony skeleton that carries a 15 kg body
        var soft = Development.Grow(Quad(Kind.Soft), 15f).Segs[1];
        float a = 0.5f * soft.W, b = 0.5f * soft.H, ai = a - soft.IntT, bi = b - soft.IntT;
        float integ = Development.Sigma(0f) * MathF.PI / (4f * b) * (a * b * b * b - ai * bi * bi * bi);
        _out.WriteLine($"soft 15 kg mid segment: W {1000 * soft.W:0} H {1000 * soft.H:0} mm, skin {1000 * soft.IntT:0.0} mm bears {integ:0.0} N m, its axial muscle {soft.BendCap - integ:0.0}, all {soft.BendCap:0.0}");
        foreach (float gk in new[] { 0.002f, 0.005f, 0.01f, 0.02f, 0.05f })
        {
            var g = Quad(Kind.Endo); foreach (var s in g.Segs) s.P[SP.AX_SK] = gk;
            var m = Development.Grow(g, 15f);
            _out.WriteLine($"15 kg, bony rod of {100 * Mat.AXSK_MAX * gk:0.00}% of the section: {100 * m.EndoMass / m.Mass:0.00}% of mass, support {Physics.Walk(m, Phys.RHO_A).Support:0.00}, vLand {Live.Of(m, 1f, 1f, 0f).C.VLand:0.00}");
        }
        _out.WriteLine($"Prange et al. 1979: mammal skeleton 0.061 M^1.09 = {100 * 0.061 * MathF.Pow(15f, 1.09f) / 15f:0.0}% of 15 kg, {100 * 0.061 * MathF.Pow(0.001f, 1.09f) / 0.001f:0.0}% of 1 g");
    }

    [Fact] // a soft trunk bears a small body on its limbs and not a large one; an internal skeleton or an exoskeleton bears both
    public void ATrunkMustBearItsWeightBetweenItsLimbs()
    {
        float Sup(Kind k, float mass) => Physics.Walk(Development.Grow(Quad(k), mass), Phys.RHO_A).Support;
        Assert.True(Sup(Kind.Soft, 0.01f) > 0.99f, $"soft 10 g {Sup(Kind.Soft, 0.01f)}");
        Assert.True(Sup(Kind.Soft, 15f) < 0.9f, $"soft 15 kg {Sup(Kind.Soft, 15f)}");
        Assert.True(Sup(Kind.Soft, 40f) < 0.7f, $"soft 40 kg {Sup(Kind.Soft, 40f)}");
        // and a soft 15 kg body is the slower and the costlier for it (Morph.Live: its top speed and its cost a metre on land)
        var ls = Live.Of(Development.Grow(Quad(Kind.Soft), 15f), 1f, 1f, 0f).C; var le = Live.Of(Development.Grow(Quad(Kind.Endo), 15f), 1f, 1f, 0f).C;
        Assert.True(le.VLand > 2f * ls.VLand && le.CotLand < 0.8f * ls.CotLand, $"vLand {le.VLand} against {ls.VLand}, CoT {le.CotLand} against {ls.CotLand}");
        Assert.True(Sup(Kind.Endo, 15f) > 0.99f, $"endo 15 kg {Sup(Kind.Endo, 15f)}");
        Assert.True(Sup(Kind.Exo, 15f) > 0.99f, $"exo 15 kg {Sup(Kind.Exo, 15f)}");
        // the trunk's limit scales with size: bending ~ M^(4/3) against strength ~ M (Galileo; McMahon 1973)
        Assert.True(Sup(Kind.Soft, 40f) <= Sup(Kind.Soft, 15f) && Sup(Kind.Soft, 15f) <= Sup(Kind.Soft, 1f));
    }

    [Fact] // the internal skeleton is not moulted and does not stiffen the trunk's undulation; an exoskeleton is moulted and does
    public void AnInternalSkeletonGrowsWithoutMoultingAndBends()
    {
        var endo = Development.Grow(Quad(Kind.Endo), 15f); var exo = Development.Grow(Quad(Kind.Exo), 15f); var soft = Development.Grow(Quad(Kind.Soft), 15f);
        Assert.Equal(0f, Live.Of(endo, 1f, 1f, 0f).MoultSoft);
        Assert.Equal(0f, endo.Rigidity);
        Assert.True(Live.Of(exo, 1f, 1f, 0f).MoultSoft > 0.1f);
        Assert.Equal(Physics.Flex(soft), Physics.Flex(endo));
        Assert.True(Physics.Flex(exo) < Physics.Flex(endo));
        // it is mass in the trunk: bone mineral is inert, it takes room from the gut and the muscle, and it is denser than tissue
        Assert.True(endo.EndoMass > 0f && endo.InertMass > soft.InertMass);
        Assert.True(Live.GutRoom(endo) < Live.GutRoom(soft));
        Assert.True(endo.Mass / endo.Volume > soft.Mass / soft.Volume);
        // and it does not protect: a bite is stopped by the integument alone (the same within the 2% its denser, smaller body
        // thins the integument by)
        Assert.InRange(Physics.PKill(endo, 10f) / Physics.PKill(soft, 10f), 0.98f, 1.02f);
    }

    [Fact] // a genome without the genes (an older save's) grows exactly as one with them at 0, and the codec keeps them
    public void OldGenomesReadWithNoSkeleton()
    {
        var g = Quad(Kind.Endo);
        var block = new sbyte[Codec.SIZE];
        Codec.Encode(g, block);
        var back = Codec.Decode(block)!;
        for (int i = 0; i < g.Segs.Count; i++)
        {
            Assert.Equal(g.Segs[i].P[SP.AX_SK], back.Segs[i].P[SP.AX_SK], 2);
            Assert.Equal(g.Segs[i].P[SP.AX_MIN], back.Segs[i].P[SP.AX_MIN], 2);
        }
        // a block written before the tail: its first SIZE_V1 bytes, the tail zero
        Array.Clear(block, Codec.TAIL, Codec.SIZE - Codec.TAIL);
        var old = Codec.Decode(block)!;
        Assert.All(old.Segs, s => { Assert.Equal(0f, s.P[SP.AX_SK]); Assert.Equal(0f, s.P[SP.AX_MIN]); });
        var soft = Codec.Quantised(Quad(Kind.Soft));
        var a = Development.Grow(old, 3f); var b = Development.Grow(soft, 3f);
        Assert.Equal(0f, a.EndoMass);
        Assert.Equal(b.Mass, a.Mass); Assert.Equal(b.GutMass, a.GutMass); Assert.Equal(b.InertMass, a.InertMass);
        // the translator writes no internal skeleton
        var t = Translate.Reference(1f);
        Assert.All(Translate.FromLive(t, 0f).Segs, s => Assert.Equal(0f, s.P[SP.AX_SK]));
    }

    [Fact] // a gene bank of the format before the tail (14) is read at its old genome length: the block's head as it was, the tail zero
    public void AGeneBankBeforeTheTailReadsWithNoSkeleton()
    {
        var full = new sbyte[G.LEN];
        Clades.AncestorBody(new Span<sbyte>(full, G.BODY, G.NBODY), 9, 2, true);
        Clades.AncestorMorph(full, 9, 2);
        int oldLen = G.MORPH + Codec.SIZE_V1;
        Assert.Equal(oldLen, Checkpoint.GenomeLenOf(Checkpoint.AxialVersion - 1));
        using var ms = new System.IO.MemoryStream();
        using (var bw = new System.IO.BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            bw.Write("NRBK"u8.ToArray()); bw.Write(GeneBank.AxialFrom); bw.Write((ushort)K.CLADES); bw.Write(oldLen); bw.Write(16u);
            bw.Write(5UL); bw.Write(9u);
            for (int sp = 0; sp < K.CLADES; sp++)
            {
                if (sp != 0) { bw.Write((byte)0); continue; }
                bw.Write((byte)1); bw.Write((byte)1); bw.Write((byte)'A'); bw.Write((byte)1); bw.Write((byte)2); bw.Write((byte)3);
                bw.Write((short)-1); bw.Write((byte)0); bw.Write((ushort)0); bw.Write(1u); bw.Write((ushort)1);
                bw.Write(1f); bw.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(full.AsSpan(0, oldLen)));
            }
        }
        ms.Position = 0;
        var g = GeneBank.Load(ms).Genomes[0][0];
        Assert.Equal(G.LEN, g.Length);
        Assert.True(g.AsSpan(0, oldLen).SequenceEqual(full.AsSpan(0, oldLen)));
        var m = Codec.Decode(Codec.Block((ReadOnlySpan<sbyte>)g))!;
        Assert.All(m.Segs, s => { Assert.Equal(0f, s.P[SP.AX_SK]); Assert.Equal(0f, s.P[SP.AX_MIN]); });
        Assert.Equal(0f, Development.Grow(m, 2f).EndoMass);
    }

    [Fact] // the genes mutate like any other, and a new skeleton appears by mutation from none
    public void TheGenesMutate()
    {
        int arose = 0;
        for (uint k = 0; k < 400; k++)
        {
            var g = Quad(Kind.Soft);
            var r = new Rng.Seq(Rng.Hash(5u, k, 7u, 11u));
            Mutate.Apply(g, ref r, new MutationRates());
            if (g.Segs.Any(s => s.P[SP.AX_SK] > 0f)) arose++;
        }
        Assert.InRange(arose, 10, 200);
    }
}
