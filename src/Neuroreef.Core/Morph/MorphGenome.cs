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
//   mass                   one genome-level gene sets the adult body mass (log 1 g .. 10 kg); development scales the
//                          whole body to it, and the physics' allometry (Kleiber, gut throughput, Reynolds number,
//                          predators in a size spectrum) decides what size pays
// Every module carries an INNOVATION NUMBER (NEAT; Stanley & Miikkulainen 2002), given when the module first appears
// (insertion, duplication) and copied with it, so crossover can line up homologous modules and the distance between
// genomes can count the unmatched ones (speciation, Evolver.cs).
// All randomness through Rng.Seq; all maths through MathK (NR-104).

using System;
using System.Collections.Generic;

namespace Neuroreef.Morph;

/// <summary>Parameters of a segment gene (all stored in [0,1]).</summary>
public static class SP
{
    public const int LEN = 0, WID = 1, HEI = 2, TAPER = 3, REPEAT = 4, INT_T = 5, INT_MIN = 6, PORE = 7, INVAG = 8, AXMUS = 9, GAS = 10, FAT = 11;
    /// <summary>The internal axial skeleton (docs/BODYPLAN.md §13.19, 2026-11-04): AX_SK its share of the segment's core
    /// cross-section (0 none, 1 Mat.AXSK_MAX), AX_MIN its mineral share (0 cartilage or a notochord's sheath, 1 bone), as
    /// an appendage's central rod is (AP.WALL, AP.MINERAL). A gene of 0 grows none: every genome made before it has 0.</summary>
    public const int AX_SK = 12, AX_MIN = 13;
    public const int N = 14;
    /// <summary>The parameters a segment slot of the live block holds (Codec): the first twelve. AX_SK and AX_MIN are in the
    /// block's tail, where an older genome's zero bytes read as 0.</summary>
    public const int N_SLOT = 12;
    public static readonly string[] Names = { "len", "wid", "hei", "taper", "repeat", "intT", "intMin", "pore", "invag", "axMus", "gas", "fat", "axSk", "axMin" };
}

/// <summary>Parameters of an appendage gene (a bilateral pair; all stored in [0,1]).</summary>
public static class AP
{
    public const int LEN = 0, CHORD = 1, THICK = 2, DROOP = 3, JOINTS = 4, MUSCLE = 5, MINERAL = 6, WALL = 7, EXO = 8, PORE = 9, INVAG = 10, ROLL = 11, HOLLOW = 12;
    public const int N = 13;
    public static readonly string[] Names = { "len", "chord", "thick", "droop", "joints", "muscle", "mineral", "wall", "exo", "pore", "invag", "roll", "hollow" };
}

public sealed class AppGene
{
    public readonly float[] P = new float[AP.N];
    public uint Innov;
    public AppGene Clone() { var a = new AppGene { Innov = Innov }; Array.Copy(P, a.P, AP.N); return a; }
}

public sealed class SegGene
{
    public readonly float[] P = new float[SP.N];
    public readonly List<AppGene> Apps = new();
    public uint Innov;
    public SegGene Clone()
    {
        var s = new SegGene { Innov = Innov }; Array.Copy(P, s.P, SP.N);
        foreach (var a in Apps) s.Apps.Add(a.Clone());
        return s;
    }
}

/// <summary>Hands out innovation numbers: one counter per evolutionary run, so runs stay deterministic.</summary>
public sealed class Innovations
{
    uint _next;
    public Innovations(uint start = 100) { _next = start; }
    public uint Next() => _next++;
    /// <summary>Start again at start (a reused counter: Codec, no allocation in the step loop).</summary>
    public void Reset(uint start) { _next = start; }
    /// <summary>For mutations made outside a run (tests, probes).</summary>
    public static readonly Innovations Shared = new(1_000_000);
}

public sealed class MorphGenome
{
    public const int MAX_SEG_GENES = 8, MAX_APP_GENES = 4;
    public readonly List<SegGene> Segs = new();
    /// <summary>The mass gene in [0,1]: adult body mass, log-mapped from 1 g to 10 kg (Development.MassOf).</summary>
    public float MassG = BLOB_MASS_G;
    /// <summary>The gene value of 0.25 kg, the mass every body had before the mass gene.</summary>
    public static readonly float BLOB_MASS_G = MathK.Log(0.25f / Development.MASS_LO) / MathK.Log(Development.MASS_HI / Development.MASS_LO);

    public MorphGenome Clone() { var g = new MorphGenome { MassG = MassG }; foreach (var s in Segs) g.Segs.Add(s.Clone()); return g; }

    public int ParamCount { get { int n = 1; foreach (var s in Segs) n += SP.N + s.Apps.Count * AP.N; return n; } }
    public int AppGeneCount { get { int n = 0; foreach (var s in Segs) n += s.Apps.Count; return n; } }

    /// <summary>The generic starting body: one segment, about as long as wide as high, soft thin skin, no appendages,
    /// no exchange surface (it breathes through its skin), a little axial muscle, 0.25 kg.</summary>
    public static MorphGenome Blob()
    {
        var g = new MorphGenome();
        var s = new SegGene { Innov = 1 };   // every founder's trunk is homologous
        s.P[SP.LEN] = 0.75f; s.P[SP.WID] = 0.75f; s.P[SP.HEI] = 0.75f;   // ~6 cm each way
        s.P[SP.TAPER] = 0.5f; s.P[SP.REPEAT] = 0f;
        s.P[SP.INT_T] = 0.1f; s.P[SP.INT_MIN] = 0f; s.P[SP.PORE] = 0f; s.P[SP.INVAG] = 0.5f; s.P[SP.AXMUS] = 0.2f; s.P[SP.GAS] = 0f; s.P[SP.FAT] = 0f;
        s.P[SP.AX_SK] = 0f; s.P[SP.AX_MIN] = 0f;   // no internal skeleton
        g.Segs.Add(s);
        return g;
    }
}

/// <summary>
/// Reused gene objects for decoding, mutating and recombining module genomes without allocating (the live step loop does
/// it at every birth; Codec). Taken in order until Reset; when it runs out it allocates, so it is never wrong, only slower.
/// Genes from a pool are valid until its next Reset.
/// </summary>
public sealed class GenePool
{
    readonly SegGene[] _s = new SegGene[96];
    readonly AppGene[] _a = new AppGene[384];
    int _ns, _na;
    public GenePool()
    {
        for (int k = 0; k < _s.Length; k++) { _s[k] = new SegGene(); _s[k].Apps.Capacity = MorphGenome.MAX_APP_GENES; }
        for (int k = 0; k < _a.Length; k++) _a[k] = new AppGene();
    }
    public void Reset() { _ns = 0; _na = 0; }
    public SegGene Seg(uint innov)
    {
        var s = _ns < _s.Length ? _s[_ns++] : new SegGene();
        s.Apps.Clear(); s.Innov = innov; Array.Clear(s.P);
        return s;
    }
    public AppGene App(uint innov)
    {
        var a = _na < _a.Length ? _a[_na++] : new AppGene();
        a.Innov = innov; Array.Clear(a.P);
        return a;
    }
    public AppGene CloneApp(AppGene a) { var c = App(a.Innov); Array.Copy(a.P, c.P, AP.N); return c; }
    public SegGene CloneSeg(SegGene s)
    {
        var c = Seg(s.Innov); Array.Copy(s.P, c.P, SP.N);
        foreach (var a in s.Apps) c.Apps.Add(CloneApp(a));
        return c;
    }
    /// <summary>A genome object with room for the most segment genes (its list never grows).</summary>
    public static MorphGenome NewGenome() { var g = new MorphGenome(); g.Segs.Capacity = MorphGenome.MAX_SEG_GENES + 1; return g; }
}

/// <summary>Mutation rates per offspring.</summary>
public sealed class MutationRates
{
    public float PointRate = 0.02f;        // chance each parameter mutates (a per-site rate: more genes, more mutations)
    public float PointSigma = 0.08f;
    public float Diverge = 0.05f, DivergeSigma = 0.04f;
    public float DupSeg = 0.03f, DelSeg = 0.03f;
    public float DupApp = 0.04f, DelApp = 0.04f, InsApp = 0.04f;
    public float MassRate = 0.2f, MassSigma = 0.04f;   // the mass gene: 0.04 in gene space is x1.45 in mass
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
    /// the body is nearly unchanged until the copies diverge. The copy is a new gene (a paralogue): it and its
    /// appendage genes get new innovation numbers.</summary>
    public static bool DuplicateSegment(MorphGenome g, int i, Innovations? inn = null, GenePool? pool = null)
    {
        if (g.Segs.Count >= MorphGenome.MAX_SEG_GENES) return false;
        inn ??= Innovations.Shared;
        var s = g.Segs[i];
        float h = HalfStep(Development.SEG_L_LO, Development.SEG_L_HI);
        s.P[SP.LEN] = Clamp01(s.P[SP.LEN] - h);
        var c = pool != null ? pool.CloneSeg(s) : s.Clone();
        c.Innov = inn.Next();
        foreach (var a in c.Apps) a.Innov = inn.Next();
        g.Segs.Insert(i + 1, c);
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
    public static bool DuplicateAppendage(MorphGenome g, int i, int j, int target, Innovations? inn = null, GenePool? pool = null)
    {
        var s = g.Segs[i];
        if (j < 0 || j >= s.Apps.Count) return false;
        var t = g.Segs[Math.Clamp(target, 0, g.Segs.Count - 1)];
        if (t.Apps.Count >= MorphGenome.MAX_APP_GENES) return false;
        var a = s.Apps[j];
        a.P[AP.MUSCLE] *= 0.5f;
        var c = pool != null ? pool.CloneApp(a) : a.Clone();
        c.Innov = (inn ?? Innovations.Shared).Next();
        t.Apps.Add(c);
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
    public static bool InsertAppendage(MorphGenome g, int i, ref Rng.Seq r, Innovations? inn = null, GenePool? pool = null)
    {
        var s = g.Segs[i];
        if (s.Apps.Count >= MorphGenome.MAX_APP_GENES) return false;
        uint id = (inn ?? Innovations.Shared).Next();
        var a = pool != null ? pool.App(id) : new AppGene { Innov = id };
        for (int k = 0; k < AP.N; k++) a.P[k] = r.Next();
        a.P[AP.LEN] = 0.05f * r.Next(); a.P[AP.CHORD] = 0.1f * r.Next(); a.P[AP.MUSCLE] = 0.1f * r.Next();
        s.Apps.Add(a);
        return true;
    }

    /// <summary>One offspring's mutations: point and divergence always possible, then each structural operator with its rate.</summary>
    public static void Apply(MorphGenome g, ref Rng.Seq r, MutationRates m, Innovations? inn = null, GenePool? pool = null)
    {
        Point(g, ref r, m.PointRate, m.PointSigma);
        if (r.Next() < m.MassRate) g.MassG = Clamp01(g.MassG + m.MassSigma * Gauss(ref r));
        if (r.Next() < m.Diverge) Diverge(g, ref r, m.DivergeSigma);
        if (r.Next() < m.DupSeg) DuplicateSegment(g, Pick(ref r, g.Segs.Count), inn, pool);
        if (r.Next() < m.DelSeg) DeleteSegment(g, Pick(ref r, g.Segs.Count));
        if (r.Next() < m.InsApp) InsertAppendage(g, Pick(ref r, g.Segs.Count), ref r, inn, pool);
        if (r.Next() < m.DupApp)
        {
            int i = Pick(ref r, g.Segs.Count);
            var s = g.Segs[i];
            if (s.Apps.Count > 0)
            {
                int target = r.Next() < 0.3f ? i + (r.Next() < 0.5f ? -1 : 1) : i;
                if (target < 0 || target >= g.Segs.Count || g.Segs[target].Apps.Count >= MorphGenome.MAX_APP_GENES) target = i;
                DuplicateAppendage(g, i, Pick(ref r, s.Apps.Count), target, inn, pool);
            }
        }
        if (r.Next() < m.DelApp)
        {
            int i = Pick(ref r, g.Segs.Count);
            if (g.Segs[i].Apps.Count > 0) DeleteAppendage(g, i, Pick(ref r, g.Segs[i].Apps.Count));
        }
    }
}

/// <summary>NEAT-style recombination and compatibility on innovation numbers (Stanley &amp; Miikkulainen 2002).</summary>
public static class Homology
{
    /// <summary>Crossover: the child keeps the fitter parent's module structure and order. Each module the other parent
    /// also has (the same innovation number) is inherited whole from either parent at random, and its appendage genes
    /// are aligned the same way. Modules only the less fit parent has are not inherited (NEAT's disjoint and excess
    /// genes). The mass gene comes from either parent.</summary>
    public static MorphGenome Cross(MorphGenome fitter, MorphGenome other, ref Rng.Seq r, MorphGenome? into = null, GenePool? pool = null)
    {
        var c = into ?? new MorphGenome();
        c.Segs.Clear();
        c.MassG = r.Next() < 0.5f ? fitter.MassG : other.MassG;
        foreach (var s in fitter.Segs)
        {
            SegGene? o = null;
            foreach (var t in other.Segs) if (t.Innov == s.Innov) { o = t; break; }
            if (o == null) { c.Segs.Add(pool != null ? pool.CloneSeg(s) : s.Clone()); continue; }
            var ns = pool != null ? pool.Seg(s.Innov) : new SegGene { Innov = s.Innov };
            Array.Copy(r.Next() < 0.5f ? s.P : o.P, ns.P, SP.N);
            foreach (var a in s.Apps)
            {
                AppGene? b = null;
                foreach (var t in o.Apps) if (t.Innov == a.Innov) { b = t; break; }
                var src = b != null && r.Next() < 0.5f ? b : a;
                ns.Apps.Add(pool != null ? pool.CloneApp(src) : src.Clone());
            }
            c.Segs.Add(ns);
        }
        return c;
    }

    public const float C_UNMATCHED = 1f, C_PARAM = 3f, C_MASS = 2f;

    /// <summary>Compatibility distance: C_UNMATCHED x (modules with no homologue in the other genome) / (modules in the
    /// larger genome), plus C_PARAM x the mean absolute parameter difference of matched modules, plus C_MASS x the
    /// difference of the mass genes.</summary>
    public static float Distance(MorphGenome a, MorphGenome b)
    {
        int na = 0, nb = 0, matched = 0, matchedParams = 0;
        float diff = 0f;
        foreach (var s in a.Segs) na += 1 + s.Apps.Count;
        foreach (var s in b.Segs) nb += 1 + s.Apps.Count;
        foreach (var s in a.Segs)
        {
            SegGene? o = null;
            foreach (var t in b.Segs) if (t.Innov == s.Innov) { o = t; break; }
            if (o == null) continue;
            matched++;
            for (int k = 0; k < SP.N; k++) diff += MathF.Abs(s.P[k] - o.P[k]);
            matchedParams += SP.N;
            foreach (var x in s.Apps)
                foreach (var y in o.Apps)
                    if (x.Innov == y.Innov)
                    {
                        matched++;
                        for (int k = 0; k < AP.N; k++) diff += MathF.Abs(x.P[k] - y.P[k]);
                        matchedParams += AP.N;
                        break;
                    }
        }
        int unmatched = na + nb - 2 * matched;
        return C_UNMATCHED * unmatched / Math.Max(1, Math.Max(na, nb))
             + C_PARAM * (matchedParams > 0 ? diff / matchedParams : 0f)
             + C_MASS * MathF.Abs(a.MassG - b.MassG);
    }
}
