// The module genome in the live genome (docs/BODYPLAN.md §13.2, phase 3): a fixed-CAPACITY, variable-COUNT block of
// G.MORPHN bytes at G.MORPH, after the brain, body and reflex genes. Genes beyond the count are ignored; duplication and
// deletion shift entries within the block. Every parameter is one byte, (b + 127) / 254 in [0,1] (as Body.Organ reads an
// organ gene), and every module carries its NEAT innovation number (4 bytes, little-endian), so homologous modules line
// up for crossover and distance in the live sim as in the prototype (Homology).
//
// Layout: [0] segment-gene count (0 = no module genome: an older or hand-made genome, expressed through Translate)
//         then MAX_SEG_GENES segment slots of SEG_BYTES each:
//           innovation (4) | SP.N parameters | appendage count (1) | MAX_APP_GENES appendage slots of APP_BYTES:
//             innovation (4) | AP.N parameters
//
// The capacity is the developmental limit of the prototype (MorphGenome.MAX_SEG_GENES = 8 segment genes, each grown as up
// to Development.MAX_REPEAT = 4 serial copies, so at most 32 segments; MAX_APP_GENES = 4 pair genes per segment gene, so at
// most 128 pairs): 681 bytes a slot, against the ~98 KB of the brain's genome. It is a limit on how many distinct
// segment genes (Hox-like identities) a body can carry, not on its size: the mass gene scales any body. Real bilaterians
// carry ~4-14 Hox paralogue groups (Carroll, Grenier & Weatherbee 2005), and a segment gene's serial repeat makes the
// series. Mutations that would exceed it do nothing (Mutate's operators refuse), as a duplication that disrupts a full
// cluster would be lost.
// All maths through MathK; all randomness through Rng (NR-104).

using System;

namespace Neuroreef.Morph;

public static class Codec
{
    public const int APP_BYTES = 4 + AP.N;                                       // 17
    public const int SEG_BYTES = 4 + SP.N + 1 + MorphGenome.MAX_APP_GENES * APP_BYTES;   // 85
    public const int SIZE = 1 + MorphGenome.MAX_SEG_GENES * SEG_BYTES;           // 681

    static sbyte B(float p) => (sbyte)Math.Clamp((int)MathF.Round(p * 254f - 127f), -127, 127);
    static float P(sbyte b) => (b + 127) / 254f;

    static void PutU(Span<sbyte> s, int o, uint v) { s[o] = (sbyte)(byte)v; s[o + 1] = (sbyte)(byte)(v >> 8); s[o + 2] = (sbyte)(byte)(v >> 16); s[o + 3] = (sbyte)(byte)(v >> 24); }
    static uint GetU(ReadOnlySpan<sbyte> s, int o) => (uint)(byte)s[o] | (uint)(byte)s[o + 1] << 8 | (uint)(byte)s[o + 2] << 16 | (uint)(byte)s[o + 3] << 24;

    /// <summary>The block of a whole genome.</summary>
    public static Span<sbyte> Block(Span<sbyte> genome) => genome.Slice(G.MORPH, G.MORPHN);
    public static ReadOnlySpan<sbyte> Block(ReadOnlySpan<sbyte> genome) => genome.Slice(G.MORPH, G.MORPHN);

    /// <summary>Does this genome carry a module genome (a non-zero segment count)?</summary>
    public static bool Has(ReadOnlySpan<sbyte> genome) => genome.Length >= G.LEN && genome[G.MORPH] > 0;

    /// <summary>Write g into a block (SIZE bytes). The parameters are quantised to 1/254; the mass gene is not stored (the
    /// live mass genes B_MASS and B_FLOOR set the mass).</summary>
    public static void Encode(MorphGenome g, Span<sbyte> block)
    {
        block.Slice(0, SIZE).Clear();
        int n = Math.Min(g.Segs.Count, MorphGenome.MAX_SEG_GENES);
        block[0] = (sbyte)n;
        for (int i = 0; i < n; i++)
        {
            var s = g.Segs[i];
            int o = 1 + i * SEG_BYTES;
            PutU(block, o, s.Innov);
            for (int k = 0; k < SP.N; k++) block[o + 4 + k] = B(s.P[k]);
            int na = Math.Min(s.Apps.Count, MorphGenome.MAX_APP_GENES);
            block[o + 4 + SP.N] = (sbyte)na;
            for (int j = 0; j < na; j++)
            {
                var a = s.Apps[j];
                int q = o + 4 + SP.N + 1 + j * APP_BYTES;
                PutU(block, q, a.Innov);
                for (int k = 0; k < AP.N; k++) block[q + 4 + k] = B(a.P[k]);
            }
        }
    }

    /// <summary>The module genome in a block, or null if it holds none (count 0).</summary>
    public static MorphGenome? Decode(ReadOnlySpan<sbyte> block)
    {
        int n = Math.Clamp((int)block[0], 0, MorphGenome.MAX_SEG_GENES);
        if (n == 0) return null;
        var g = new MorphGenome();
        for (int i = 0; i < n; i++)
        {
            int o = 1 + i * SEG_BYTES;
            var s = new SegGene { Innov = GetU(block, o) };
            for (int k = 0; k < SP.N; k++) s.P[k] = P(block[o + 4 + k]);
            int na = Math.Clamp((int)block[o + 4 + SP.N], 0, MorphGenome.MAX_APP_GENES);
            for (int j = 0; j < na; j++)
            {
                int q = o + 4 + SP.N + 1 + j * APP_BYTES;
                var a = new AppGene { Innov = GetU(block, q) };
                for (int k = 0; k < AP.N; k++) a.P[k] = P(block[q + 4 + k]);
                s.Apps.Add(a);
            }
            g.Segs.Add(s);
        }
        return g;
    }

    /// <summary>Quantise a genome as storing it would (encode, decode), so a genome built in memory is the one a slot holds.</summary>
    public static MorphGenome Quantised(MorphGenome g)
    {
        Span<sbyte> b = stackalloc sbyte[SIZE];
        Encode(g, b);
        return Decode(b)!;
    }

    // ------------------------------------------------------------------ the live genome's module operators

    /// <summary>Innovation numbers handed out by one offspring's mutations: a run of numbers from a start hashed from
    /// (seed, step, key), so no shared counter is needed and births on any thread stay deterministic. Starts at
    /// LIVE_INNOV_LO or above, clear of the translator's fixed role numbers (Translate.IN_*) and the founders' numbers.
    /// Two lineages draw the same number only by a 32-bit hash collision (~n²/2^33 for n innovations).</summary>
    public const uint LIVE_INNOV_LO = 1u << 16;
    public static Innovations InnovationsFor(uint seed, uint step, uint key)
        => new(LIVE_INNOV_LO | (Rng.Hash(seed, step, key, Rs.Mutate + 0x3FFFFF00u) & 0xFFFFFF00u));

    /// <summary>Mutate a genome's module block with every Morph operator (point, diverge, duplicate and delete a segment,
    /// duplicate, delete and insert an appendage, the repeat count: Mutate.Apply), at the prototype's per-offspring
    /// rates (MutationRates). A genome without a block is left as it is (it is expressed through Translate until one is
    /// written). The mass gene of the prototype is not used here (B_MASS mutates with the body genes).</summary>
    public static void MutateBlock(Span<sbyte> genome, uint seed, uint step, uint key)
    {
        var block = Block(genome);
        var g = Decode(block);
        if (g == null) return;
        var r = new Rng.Seq(Rng.Hash(seed, step, key, Rs.Mutate + 0x3FFFFF10u));
        var rates = LiveRates;
        float massG = g.MassG;
        Mutate.Apply(g, ref r, rates, InnovationsFor(seed, step, key));
        g.MassG = massG;
        Encode(g, block);
    }

    /// <summary>The live sim's module mutation rates: the prototype's (docs/BODYPLAN.md §2), mass excluded.</summary>
    public static readonly MutationRates LiveRates = new() { MassRate = 0f };

    /// <summary>Crossover of two parents' module blocks by innovation number (Homology.Cross): the child takes one parent's
    /// module order and structure, chosen at random (the live sim has no fitness to say which is fitter; NEAT keeps the
    /// fitter's disjoint genes, and at equal fitness either), and each module both carry from either parent.</summary>
    public static void CrossBlock(Span<sbyte> child, ReadOnlySpan<sbyte> mother, ReadOnlySpan<sbyte> father, uint seed, uint step, uint key)
    {
        var gm = Decode(Block(mother)); var gf = Decode(Block(father));
        var cb = Block(child);
        if (gm == null || gf == null) { (gm == null ? Block(father) : Block(mother)).CopyTo(cb); return; }
        var r = new Rng.Seq(Rng.Hash(seed, step, key, Rs.Cross + 0x3FFFFF20u));
        bool motherLeads = r.Next() < 0.5f;
        var c = motherLeads ? Homology.Cross(gm, gf, ref r) : Homology.Cross(gf, gm, ref r);
        Encode(c, cb);
    }

    /// <summary>
    /// Module distance between two genomes' blocks as an RMS per gene on the live body genes' scale (/127, as
    /// Body.Distance): matched modules (the same innovation number) by their parameter differences (2 dp: a parameter's
    /// [0,1] is a body gene's -127..127), and every parameter of an unmatched module at sqrt(2/3), the RMS difference of
    /// two independent uniform genes. Read straight from the bytes (no allocation): it is called at every mating.
    /// 0 when either genome has no block.
    /// </summary>
    public static float Distance(ReadOnlySpan<sbyte> ga, ReadOnlySpan<sbyte> gb)
    {
        var a = Block(ga); var b = Block(gb);
        int na = Math.Clamp((int)a[0], 0, MorphGenome.MAX_SEG_GENES), nb = Math.Clamp((int)b[0], 0, MorphGenome.MAX_SEG_GENES);
        if (na == 0 || nb == 0) return 0f;
        double sum = 0; int nParams = 0, unmatched = 0;
        int ParamsOf(ReadOnlySpan<sbyte> blk, int i) => SP.N + Math.Clamp((int)blk[1 + i * SEG_BYTES + 4 + SP.N], 0, MorphGenome.MAX_APP_GENES) * AP.N;
        // every module of a, against its homologue in b if any
        for (int i = 0; i < na; i++)
        {
            int oa = 1 + i * SEG_BYTES; uint ia = GetU(a, oa);
            int ob = -1;
            for (int j = 0; j < nb; j++) if (GetU(b, 1 + j * SEG_BYTES) == ia) { ob = 1 + j * SEG_BYTES; break; }
            if (ob < 0) { unmatched += ParamsOf(a, i); continue; }
            for (int k = 0; k < SP.N; k++) { float d = 2f * (P(a[oa + 4 + k]) - P(b[ob + 4 + k])); sum += d * d; }
            nParams += SP.N;
            int aa = Math.Clamp((int)a[oa + 4 + SP.N], 0, MorphGenome.MAX_APP_GENES), ab = Math.Clamp((int)b[ob + 4 + SP.N], 0, MorphGenome.MAX_APP_GENES);
            for (int x = 0; x < aa; x++)
            {
                int qa = oa + 4 + SP.N + 1 + x * APP_BYTES; uint ix = GetU(a, qa);
                int qb = -1;
                for (int y = 0; y < ab; y++) if (GetU(b, ob + 4 + SP.N + 1 + y * APP_BYTES) == ix) { qb = ob + 4 + SP.N + 1 + y * APP_BYTES; break; }
                if (qb < 0) { unmatched += AP.N; continue; }
                for (int k = 0; k < AP.N; k++) { float d = 2f * (P(a[qa + 4 + k]) - P(b[qb + 4 + k])); sum += d * d; }
                nParams += AP.N;
            }
            // b's appendage genes on this segment with no homologue in a
            for (int y = 0; y < ab; y++)
            {
                uint iy = GetU(b, ob + 4 + SP.N + 1 + y * APP_BYTES); bool found = false;
                for (int x = 0; x < aa && !found; x++) found = GetU(a, oa + 4 + SP.N + 1 + x * APP_BYTES) == iy;
                if (!found) unmatched += AP.N;
            }
        }
        // b's segment genes with no homologue in a
        for (int j = 0; j < nb; j++)
        {
            uint ib = GetU(b, 1 + j * SEG_BYTES); bool found = false;
            for (int i = 0; i < na && !found; i++) found = GetU(a, 1 + i * SEG_BYTES) == ib;
            if (!found) unmatched += ParamsOf(b, j);
        }
        sum += unmatched * (2.0 / 3.0);
        int total = nParams + unmatched;
        return total > 0 ? (float)Math.Sqrt(sum / total) : 0f;
    }

    // ------------------------------------------------------------------ migration of older genomes

    /// <summary>
    /// Write the module genome of a genome that has none (an older save, a gene bank, a hand-made test genome) from its
    /// labelled body genes, by the translator (Translate.FromLive, its exchange surfaces sized to its own tissues' basal
    /// metabolism as Shadow.Of sizes them). The labelled genes stay in the genome, retired: nothing expresses them once
    /// the block is written.
    /// </summary>
    public static void TranslateInto(Span<sbyte> genome)
    {
        var g = FromLabelled(genome);
        Encode(g, Block(genome));
    }

    /// <summary>The translated module genome of a genome's labelled body genes (TranslateInto).</summary>
    public static MorphGenome FromLabelled(ReadOnlySpan<sbyte> genome)
    {
        float mass = LiveGenes.MassOf(genome);
        var l = LiveGenes.Of(genome, mass);
        float level = Heat.Level(l.Endo);
        var m0 = Development.Grow(Translate.FromLive(l, 0f), l.Mass);
        float organBase = Shadow.BasalW(m0, level, Shadow.GeneBasalFactor(genome), 0f);
        return Translate.FromLive(l, organBase);
    }
}
