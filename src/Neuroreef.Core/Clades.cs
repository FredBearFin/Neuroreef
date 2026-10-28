// Clades and speciation (DESIGN §7.9, Revision F). Life starts from ANCESTORS_LAND + ANCESTORS_WATER random
// ancestors, each its own clade. Organisms mate only within their clade. Every SPEC_EVERY steps each clade's
// body genes are examined: if they have separated into two clusters with few intermediates (genotypic-cluster
// species), the smaller cluster becomes a new clade, and from then on the two cannot interbreed. A split only
// renames organisms that already exist; nothing is ever added. Serial; all buffers preallocated.

using System;

namespace Neuroreef;

public static class Clades
{
    const int SAMPLE = 512;
    const int D = G.NBODY;

    /// <summary>Per-world scratch for speciation (allocated once, with the world).</summary>
    public sealed class Scratch
    {
        public readonly int[] Count = new int[K.CLADES];
        public readonly int[] Sample = new int[SAMPLE];
        public readonly float[] Vec = new float[SAMPLE * D];
        public readonly float[] C1 = new float[D], C2 = new float[D], M = new float[D];
        public int Version;                      // changes when the clade table changes (the console resends it)
        public int NextSerial = 1;
    }

    // ------------------------------------------------------------------ clade slots

    /// <summary>Take a free clade slot (never used, or extinct for CLADE_RECYCLE steps). -1 if none.</summary>
    public static int NewClade(World w, int parent, int ancestor)
    {
        int slot = -1;
        for (int k = 0; k < K.CLADES && slot < 0; k++) if (!w.Sp[k].Used) slot = k;
        for (int k = 0; k < K.CLADES && slot < 0; k++)
        {
            var c = w.Sp[k];
            if (!c.Alive && c.Extinct >= 0 && (long)w.Step - c.Extinct >= K.CLADE_RECYCLE) slot = k;
        }
        if (slot < 0) return -1;
        var s = w.Sp[slot];
        s.Clear();
        s.Id = slot; s.Used = true; s.Alive = true; s.Parent = parent; s.Ancestor = ancestor; s.Born = (long)w.Step;
        s.Serial = w.CladeScratch.NextSerial++;
        s.Generation = parent >= 0 ? w.Sp[parent].Generation + 1 : 0;
        s.Name = Name(w.Seed, (uint)s.Serial);
        float hue = parent >= 0 ? Hue(w.Sp[parent]) + (Rng.U(w.Seed, (uint)s.Serial, 0, Rs.Bank + 50) - 0.5f) * 70f : ancestor * 360f / (K.ANCESTORS_LAND + K.ANCESTORS_WATER);
        float light = parent >= 0 ? 0.45f + 0.2f * Rng.U(w.Seed, (uint)s.Serial, 1, Rs.Bank + 50) : 0.55f;
        Hsl(hue, 0.75f, light, out s.R, out s.G, out s.B);
        w.CladeScratch.Version++;
        return slot;
    }

    static readonly string[] Syl = { "ka", "ro", "vi", "mel", "tor", "an", "su", "pe", "dri", "lo", "zan", "qu", "ber", "ni", "os", "fa",
        "gil", "ur", "te", "mo", "sha", "rin", "el", "dov", "ka", "ith", "ax", "pul", "ce", "yo", "bri", "nar" };

    /// <summary>A pronounceable name from the clade's serial (deterministic).</summary>
    public static string Name(uint seed, uint serial)
    {
        uint h = Rng.Hash(seed, serial, 7, Rs.Bank + 60);
        int n = 2 + (int)(h % 2u);
        var sb = new System.Text.StringBuilder();
        for (int k = 0; k < n; k++) { sb.Append(Syl[(int)((h >> (4 + 5 * k)) % (uint)Syl.Length)]); }
        sb[0] = char.ToUpperInvariant(sb[0]);
        return sb.ToString();
    }

    static float Hue(Species s)
    {
        float r = s.R / 255f, g = s.G / 255f, b = s.B / 255f, mx = MathF.Max(r, MathF.Max(g, b)), mn = MathF.Min(r, MathF.Min(g, b)), d = mx - mn;
        if (d < 1e-4f) return 0;
        float h = mx == r ? (g - b) / d % 6f : mx == g ? (b - r) / d + 2f : (r - g) / d + 4f;
        return h * 60f;
    }

    static void Hsl(float h, float s, float l, out byte R, out byte G, out byte B)
    {
        h = ((h % 360f) + 360f) % 360f;
        float c = (1f - MathF.Abs(2f * l - 1f)) * s, x = c * (1f - MathF.Abs(h / 60f % 2f - 1f)), m = l - c / 2f;
        float r, g, b;
        if (h < 60) { r = c; g = x; b = 0; } else if (h < 120) { r = x; g = c; b = 0; } else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; } else if (h < 300) { r = x; g = 0; b = c; } else { r = c; g = 0; b = x; }
        R = (byte)(255 * (r + m)); G = (byte)(255 * (g + m)); B = (byte)(255 * (b + m));
    }

    // ------------------------------------------------------------------ ancestors

    /// <summary>
    /// The body genes of a random ancestor: a small, simple plant-eater of its realm (the only food that exists at the
    /// start). Every value inside those bounds is random; nothing else is decided.
    /// </summary>
    public static void AncestorBody(Span<sbyte> body, uint seed, int ancestor, bool land)
    {
        float U(int k) => Rng.U(seed, (uint)ancestor, (uint)k, Rs.Founder + 7000u);
        sbyte Lin(int k, float lo, float hi) => G.ToGene(lo + (hi - lo) * U(k), -1f, 1f);   // organ genes: -1..1 maps to 0..1 size
        for (int k = 0; k < G.NBODY; k++) body[k] = 0;
        int B = G.BODY;
        body[G.B_MASS - B] = G.ToGeneLog(land ? 0.3f * MathF.Pow(2f / 0.3f, U(0)) : 0.1f * MathF.Pow(10f, U(0)), K.MASS_LO, K.MASS_HI);
        body[G.B_SPEED - B] = (sbyte)((U(1) - 0.5f) * 60f);
        body[G.B_SIGHT - B] = (sbyte)((U(2) - 0.5f) * 60f);
        body[G.B_ARMOUR - B] = Lin(3, -1f, -0.6f);                 // little armour
        body[G.B_STRIKE - B] = Lin(4, -1f, -0.7f);                 // almost no weapons
        body[G.B_LITTER - B] = Lin(5, -0.8f, 0.4f);
        body[G.B_CARE - B] = Lin(6, -1f, 0.2f);
        body[G.B_LEGS - B] = land ? Lin(7, -0.2f, 0.6f) : Lin(7, -1f, -0.6f);   // land: legs above the walking threshold
        body[G.B_FINS - B] = land ? Lin(8, -1f, -0.6f) : Lin(8, -0.2f, 0.6f);   // water: fins above the swimming threshold
        body[G.B_WINGS - B] = Lin(9, -1f, -0.6f);
        body[G.B_FOV - B] = (sbyte)((U(10) - 0.3f) * 160f);
        body[G.B_SEX - B] = (sbyte)(U(11) < 0.5f ? -40 : 40);
        body[G.B_LIVE - B] = (sbyte)(U(12) < 0.5f ? -40 : 40);
        body[G.B_BIRTH - B] = (sbyte)((U(13) - 0.5f) * 120f);
        body[G.B_LEGN - B] = (sbyte)((U(14) * 2f - 1f) * 127f);   // any number of leg pairs
        body[G.B_FINN - B] = (sbyte)((U(15) * 2f - 1f) * 127f);   // fin pairs
        body[G.B_EYEN - B] = (sbyte)((U(16) * 2f - 1f) * 127f);   // eye pairs
        body[G.B_LIFE - B] = (sbyte)((U(17) - 0.5f) * 120f);      // lifespan, development and brain size: anywhere mid-range
        body[G.B_DEVEL - B] = (sbyte)((U(18) - 0.5f) * 120f);
        body[G.B_BRAIN - B] = (sbyte)((U(19) * 2f - 1f) * 127f);
        body[G.B_HERM - B] = (sbyte)(U(35) < 0.5f ? -40 : 40);     // separate sexes or hermaphrodite
        body[G.B_SEXRATIO - B] = (sbyte)((U(36) - 0.5f) * 40f);    // about even sex ratio
        body[G.B_IMMUNE - B] = (sbyte)((U(37) * 2f - 1f) * 127f);
        // diet: the primary producers of its realm, in random proportions; every other pool near zero
        int[] plants = land ? new[] { (int)Pool.Grass, (int)Pool.Browse, (int)Pool.Seed, (int)Pool.Root, (int)Pool.Fruit, (int)Pool.Nectar }
                            : new[] { (int)Pool.Algae, (int)Pool.Phyto };
        for (int p = 0; p < Pools.N; p++) body[G.B_DIET - B + p] = -127;
        foreach (int p in plants) body[G.B_DIET - B + p] = (sbyte)(U(20 + p) * 127f);
    }

    // ------------------------------------------------------------------ bookkeeping and speciation (serial, after LIFECYCLE)

    public static void Update(World w)
    {
        var x = w.CladeScratch;
        Array.Clear(x.Count);
        for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] != St.Dead) x.Count[w.Spc[i]]++;
        for (int s = 0; s < K.CLADES; s++)
        {
            var c = w.Sp[s];
            if (!c.Used) continue;
            bool alive = x.Count[s] > 0;
            if (c.Alive && !alive) { c.Alive = false; c.Extinct = (long)w.Step; x.Version++; }
        }
        if (w.Step > 0 && w.Step % K.SPEC_EVERY == 0)
            for (int s = 0; s < K.CLADES; s++)
                if (w.Sp[s].Alive && x.Count[s] >= 2 * K.MIN_SPLIT) TrySplit(w, s, x);
    }

    static float Gene(World w, int i, int k) => w.Genome[i * G.LEN + G.BODY + k] * (1f / 127f);

    /// <summary>Split clade s if its body genes form two clusters with few intermediates. Returns the new clade or -1.</summary>
    public static int TrySplit(World w, int s, Scratch x)
    {
        // a deterministic sample of up to SAMPLE members (every k-th by slot)
        int members = x.Count[s], stride = Math.Max(1, members / SAMPLE), n = 0, seen = 0;
        for (int i = 0; i < w.Cap && n < SAMPLE; i++)
        {
            if (w.StageOf[i] == St.Dead || w.Spc[i] != s) continue;
            if (seen++ % stride != 0) continue;
            x.Sample[n] = i;
            for (int k = 0; k < D; k++) x.Vec[n * D + k] = Gene(w, i, k);
            n++;
        }
        if (n < 2 * K.MIN_SPLIT / Math.Max(1, stride)) return -1;
        // 2-means: start from the member farthest from the mean, and the one farthest from it
        Array.Clear(x.M);
        for (int a = 0; a < n; a++) for (int k = 0; k < D; k++) x.M[k] += x.Vec[a * D + k] / n;
        int f1 = Farthest(x.Vec, n, x.M);
        Array.Copy(x.Vec, f1 * D, x.C1, 0, D);
        int f2 = Farthest(x.Vec, n, x.C1);
        Array.Copy(x.Vec, f2 * D, x.C2, 0, D);
        int n1 = 0, n2 = 0; float spread = 0;
        Span<float> s1 = stackalloc float[D];
        for (int it = 0; it < 10; it++)
        {
            Array.Clear(x.M);                     // M accumulates cluster 2, s1 cluster 1
            s1.Clear();
            n1 = n2 = 0; spread = 0;
            for (int a = 0; a < n; a++)
            {
                float d1 = Dist2(x.Vec, a, x.C1), d2 = Dist2(x.Vec, a, x.C2);
                if (d1 <= d2) { n1++; for (int k = 0; k < D; k++) s1[k] += x.Vec[a * D + k]; spread += d1; }
                else { n2++; for (int k = 0; k < D; k++) x.M[k] += x.Vec[a * D + k]; spread += d2; }
            }
            if (n1 == 0 || n2 == 0) return -1;
            for (int k = 0; k < D; k++) { x.C1[k] = s1[k] / n1; x.C2[k] = x.M[k] / n2; }
        }
        float sep = MathF.Sqrt(Dist2C(x.C1, x.C2));
        float rms = MathF.Sqrt(spread / n);
        if (sep < K.SPLIT_MIN_D || sep < K.SPLIT_SEP * rms) return -1;
        // count the whole clade by nearest centre; both sides must be viable populations
        int c1 = 0, c2 = 0;
        for (int i = 0; i < w.Cap; i++)
        {
            if (w.StageOf[i] == St.Dead || w.Spc[i] != s) continue;
            if (NearSecond(w, i, x)) c2++; else c1++;
        }
        if (c1 < K.MIN_SPLIT || c2 < K.MIN_SPLIT) return -1;
        bool secondIsNew = c2 <= c1;
        int nc = NewClade(w, s, w.Sp[s].Ancestor);
        if (nc < 0) return -1;
        for (int i = 0; i < w.Cap; i++)
        {
            if (w.StageOf[i] == St.Dead || w.Spc[i] != s) continue;
            if (NearSecond(w, i, x) == secondIsNew) w.Spc[i] = (byte)nc;
        }
        w.T.Splits++;
        return nc;
    }

    static bool NearSecond(World w, int i, Scratch x)
    {
        float d1 = 0, d2 = 0;
        for (int k = 0; k < D; k++) { float v = Gene(w, i, k), a = v - x.C1[k], b = v - x.C2[k]; d1 += a * a; d2 += b * b; }
        return d2 < d1;
    }

    static int Farthest(float[] vec, int n, float[] c)
    {
        int best = 0; float bd = -1;
        for (int a = 0; a < n; a++) { float d = Dist2(vec, a, c); if (d > bd) { bd = d; best = a; } }
        return best;
    }

    static float Dist2(float[] vec, int a, float[] c) { float s = 0; for (int k = 0; k < D; k++) { float d = vec[a * D + k] - c[k]; s += d * d; } return s; }
    static float Dist2C(float[] a, float[] b) { float s = 0; for (int k = 0; k < D; k++) { float d = a[k] - b[k]; s += d * d; } return s; }
}
