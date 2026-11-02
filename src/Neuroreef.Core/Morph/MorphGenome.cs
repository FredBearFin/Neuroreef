// Open-ended body genome: PROTOTYPE (docs/BODYPLAN.md). Not wired into the live simulation.
//
// A body is a variable-length list of SEGMENT genes, each carrying a variable-length list of APPENDAGE genes. Nothing
// is labelled: there is no "fin", "leg", "wing", "gill" or "shell" gene. Each gene is a vector of physical parameters
// (sizes, stiffness, joints, muscle, porosity, integument), every one stored as a number in [0,1] and mapped to its
// physical range by development (Development.cs). Mutation operators change the NUMBER of parts as well as their
// values, as gene duplication and loss do in Hox clusters and limb fields (Ohno 1970; Carroll, Grenier & Weatherbee
// 2005):
//   point / diverge        perturb one copy's parameters, so duplicated copies can specialise
//   duplicate segment      tandem copy of a segment gene and its appendages, each copy half as long (a near-neutral
//                          split, so the copy is not culled before it can diverge)
//   delete segment         loss of a segment gene
//   duplicate appendage    copy of an appendage gene on its own segment or a neighbour (the muscle is shared out)
//   delete appendage       loss of an appendage gene
//   insert appendage       a new, tiny bud with random parameters (de novo outgrowth)
//   repeat count           a segment gene's serial repeat (metamerism) is one of its parameters
// All randomness through Rng.Seq; all maths through MathK (NR-104).

using System;
using System.Collections.Generic;

namespace Neuroreef.Morph;

/// <summary>Parameters of a segment gene (all stored in [0,1]).</summary>
public static class SP
{
    public const int LEN = 0, WID = 1, HEI = 2, TAPER = 3, REPEAT = 4, INT_T = 5, INT_MIN = 6, PORE = 7, INVAG = 8, AXMUS = 9;
    public const int N = 10;
    public static readonly string[] Names = { "len", "wid", "hei", "taper", "repeat", "intT", "intMin", "pore", "invag", "axMus" };
}

/// <summary>Parameters of an appendage gene (a bilateral pair; all stored in [0,1]).</summary>
public static class AP
{
    public const int LEN = 0, CHORD = 1, THICK = 2, DROOP = 3, JOINTS = 4, MUSCLE = 5, MINERAL = 6, WALL = 7, EXO = 8, PORE = 9, INVAG = 10, ROLL = 11;
    public const int N = 12;
    public static readonly string[] Names = { "len", "chord", "thick", "droop", "joints", "muscle", "mineral", "wall", "exo", "pore", "invag", "roll" };
}

public sealed class AppGene
{
    public readonly float[] P = new float[AP.N];
    public AppGene Clone() { var a = new AppGene(); Array.Copy(P, a.P, AP.N); return a; }
}

public sealed class SegGene
{
    public readonly float[] P = new float[SP.N];
    public readonly List<AppGene> Apps = new();
    public SegGene Clone()
    {
        var s = new SegGene(); Array.Copy(P, s.P, SP.N);
        foreach (var a in Apps) s.Apps.Add(a.Clone());
        return s;
    }
}

public sealed class MorphGenome
{
    public const int MAX_SEG_GENES = 8, MAX_APP_GENES = 4;
    public readonly List<SegGene> Segs = new();

    public MorphGenome Clone() { var g = new MorphGenome(); foreach (var s in Segs) g.Segs.Add(s.Clone()); return g; }

    public int ParamCount { get { int n = 0; foreach (var s in Segs) n += SP.N + s.Apps.Count * AP.N; return n; } }
    public int AppGeneCount { get { int n = 0; foreach (var s in Segs) n += s.Apps.Count; return n; } }

    /// <summary>The generic starting body: one segment, about as long as wide as high, soft thin skin, no appendages,
    /// no exchange surface (it breathes through its skin), a little axial muscle.</summary>
    public static MorphGenome Blob()
    {
        var g = new MorphGenome();
        var s = new SegGene();
        s.P[SP.LEN] = 0.75f; s.P[SP.WID] = 0.75f; s.P[SP.HEI] = 0.75f;   // ~6 cm each way
        s.P[SP.TAPER] = 0.5f; s.P[SP.REPEAT] = 0f;
        s.P[SP.INT_T] = 0.1f; s.P[SP.INT_MIN] = 0f; s.P[SP.PORE] = 0f; s.P[SP.INVAG] = 0.5f; s.P[SP.AXMUS] = 0.2f;
        g.Segs.Add(s);
        return g;
    }
}

/// <summary>Mutation rates per offspring.</summary>
public sealed class MutationRates
{
    public float PointRate = 0.02f;        // chance each parameter mutates (a per-site rate: more genes, more mutations)
    public float PointSigma = 0.08f;
    public float Diverge = 0.05f, DivergeSigma = 0.04f;
    public float DupSeg = 0.03f, DelSeg = 0.03f;
    public float DupApp = 0.04f, DelApp = 0.04f, InsApp = 0.04f;
}

public static class Mutate
{
    static float Gauss(ref Rng.Seq r) => (r.Next() + r.Next() + r.Next() - 1.5f) * 2f;   // ~N(0,1), as Rng.N
    static int Pick(ref Rng.Seq r, int n) => Math.Min(n - 1, (int)(r.Next() * n));
    static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;

    /// <summary>Point mutations: each parameter mutates with probability rate, by N(0, sigma).</summary>
    public static void Point(MorphGenome g, ref Rng.Seq r, float rate, float sigma)
    {
        float p = rate;
        foreach (var s in g.Segs)
        {
            for (int k = 0; k < SP.N; k++) if (r.Next() < p) s.P[k] = Clamp01(s.P[k] + sigma * Gauss(ref r));
            foreach (var a in s.Apps)
                for (int k = 0; k < AP.N; k++) if (r.Next() < p) a.P[k] = Clamp01(a.P[k] + sigma * Gauss(ref r));
        }
    }

    /// <summary>Divergence: one module (a segment gene or an appendage gene) has all its parameters nudged at once.</summary>
    public static void Diverge(MorphGenome g, ref Rng.Seq r, float sigma)
    {
        var s = g.Segs[Pick(ref r, g.Segs.Count)];
        float[] v = s.Apps.Count > 0 && r.Next() < 0.6f ? s.Apps[Pick(ref r, s.Apps.Count)].P : s.P;
        for (int k = 0; k < v.Length; k++) v[k] = Clamp01(v[k] + sigma * Gauss(ref r));
    }

    /// <summary>Gene-space step that halves a log-mapped length (Development: L = lo (hi/lo)^g).</summary>
    public static float HalfStep(float lo, float hi) => MathK.Log(2f) / MathK.Log(hi / lo);

    /// <summary>Tandem duplication of a segment gene: the copy is inserted next to it and each copy is half as long, so
    /// the body is nearly unchanged until the copies diverge.</summary>
    public static bool DuplicateSegment(MorphGenome g, int i)
    {
        if (g.Segs.Count >= MorphGenome.MAX_SEG_GENES) return false;
        var s = g.Segs[i];
        float h = HalfStep(Development.SEG_L_LO, Development.SEG_L_HI);
        s.P[SP.LEN] = Clamp01(s.P[SP.LEN] - h);
        g.Segs.Insert(i + 1, s.Clone());
        return true;
    }

    public static bool DeleteSegment(MorphGenome g, int i)
    {
        if (g.Segs.Count <= 1) return false;
        g.Segs.RemoveAt(i);
        return true;
    }

    /// <summary>Duplicates appendage gene j of segment i, onto the same segment or (when it is full, or by chance) a
    /// neighbour. The two copies share the original's muscle.</summary>
    public static bool DuplicateAppendage(MorphGenome g, int i, int j, int target)
    {
        var s = g.Segs[i];
        if (j < 0 || j >= s.Apps.Count) return false;
        var t = g.Segs[Math.Clamp(target, 0, g.Segs.Count - 1)];
        if (t.Apps.Count >= MorphGenome.MAX_APP_GENES) return false;
        var a = s.Apps[j];
        a.P[AP.MUSCLE] *= 0.5f;
        t.Apps.Add(a.Clone());
        return true;
    }

    public static bool DeleteAppendage(MorphGenome g, int i, int j)
    {
        var s = g.Segs[i];
        if (j < 0 || j >= s.Apps.Count) return false;
        s.Apps.RemoveAt(j);
        return true;
    }

    /// <summary>A new appendage bud: tiny, every other parameter random.</summary>
    public static bool InsertAppendage(MorphGenome g, int i, ref Rng.Seq r)
    {
        var s = g.Segs[i];
        if (s.Apps.Count >= MorphGenome.MAX_APP_GENES) return false;
        var a = new AppGene();
        for (int k = 0; k < AP.N; k++) a.P[k] = r.Next();
        a.P[AP.LEN] = 0.05f * r.Next(); a.P[AP.CHORD] = 0.1f * r.Next(); a.P[AP.MUSCLE] = 0.1f * r.Next();
        s.Apps.Add(a);
        return true;
    }

    /// <summary>One offspring's mutations: point and divergence always possible, then each structural operator with its rate.</summary>
    public static void Apply(MorphGenome g, ref Rng.Seq r, MutationRates m)
    {
        Point(g, ref r, m.PointRate, m.PointSigma);
        if (r.Next() < m.Diverge) Diverge(g, ref r, m.DivergeSigma);
        if (r.Next() < m.DupSeg) DuplicateSegment(g, Pick(ref r, g.Segs.Count));
        if (r.Next() < m.DelSeg) DeleteSegment(g, Pick(ref r, g.Segs.Count));
        if (r.Next() < m.InsApp) InsertAppendage(g, Pick(ref r, g.Segs.Count), ref r);
        if (r.Next() < m.DupApp)
        {
            int i = Pick(ref r, g.Segs.Count);
            var s = g.Segs[i];
            if (s.Apps.Count > 0)
            {
                int target = r.Next() < 0.3f ? i + (r.Next() < 0.5f ? -1 : 1) : i;
                if (target < 0 || target >= g.Segs.Count || g.Segs[target].Apps.Count >= MorphGenome.MAX_APP_GENES) target = i;
                DuplicateAppendage(g, i, Pick(ref r, s.Apps.Count), target);
            }
        }
        if (r.Next() < m.DelApp)
        {
            int i = Pick(ref r, g.Segs.Count);
            if (g.Segs[i].Apps.Count > 0) DeleteAppendage(g, i, Pick(ref r, g.Segs[i].Apps.Count));
        }
    }
}
