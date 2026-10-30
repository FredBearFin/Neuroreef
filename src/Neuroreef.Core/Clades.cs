// Clades and speciation (DESIGN §7.9, Revision F; visible forms, Revision L). Life starts from ANCESTORS_LAND +
// ANCESTORS_WATER random ancestors, each its own clade. Every SPEC_EVERY steps each clade's visible traits are
// examined: if they have separated into two clearly different-looking forms with few intermediates (genotypic
// clusters), the smaller form becomes a new clade: a subspecies while the two are still more than half fertile
// together, else a species. Clades are labels: who can breed with whom is decided by genes alone (Mate), never by
// the label. A split only renames organisms that already exist; nothing is ever added. Serial; buffers preallocated.

using System;

namespace Neuroreef;

public static class Clades
{
    const int SAMPLE = 512;
    const int D = G.NBODY - G.NMHC;     // recognition genes excluded: their polymorphism is not speciation

    /// <summary>Per-world scratch for speciation (allocated once, with the world).</summary>
    public sealed class Scratch
    {
        public readonly int[] Count = new int[K.CLADES];
        public readonly int[] Sample = new int[SAMPLE];
        public readonly float[] Vec = new float[SAMPLE * D];
        public readonly float[] C1 = new float[D], C2 = new float[D], M = new float[D], G1 = new float[D], G2 = new float[D];
        public int Version;                      // changes when the clade table changes (the console resends it)
        public int NextSerial = 1;
    }

    // ------------------------------------------------------------------ clade slots

    /// <summary>Take a free clade slot (never used, or extinct for CLADE_RECYCLE steps). -1 if none.</summary>
    public static int NewClade(World w, int parent, int ancestor, bool sub = false)
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
        w.T.ResetVoices(slot);   // a reused slot starts without the old clade's words
        s.Id = slot; s.Used = true; s.Alive = true; s.Parent = parent; s.Ancestor = ancestor; s.Born = (long)w.Step;
        s.Serial = w.CladeScratch.NextSerial++;
        s.Generation = parent >= 0 ? w.Sp[parent].Generation + 1 : 0;
        s.Name = Name(w.Seed, (uint)s.Serial);
        s.Sub = sub;
        // a species takes a new hue near its parent's; a subspecies keeps the hue and flips light against dark,
        // so the two forms side by side read as related but clearly different
        float u0 = Rng.U(w.Seed, (uint)s.Serial, 0, Rs.Bank + 50), u1 = Rng.U(w.Seed, (uint)s.Serial, 1, Rs.Bank + 50);
        float hue = parent < 0 ? ancestor * 360f / Math.Max(1, w.Evo.AncestorsLand + w.Evo.AncestorsWater)
                  : Hue(w.Sp[parent]) + (u0 - 0.5f) * (sub ? 24f : 70f);
        float pl = parent >= 0 ? Light(w.Sp[parent]) : 0.55f;
        float light = parent < 0 ? 0.55f : sub ? (pl >= 0.5f ? 0.32f + 0.06f * u1 : 0.70f + 0.06f * u1) : 0.45f + 0.2f * u1;
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

    static float Light(Species s) => (MathF.Max(s.R, MathF.Max(s.G, s.B)) + MathF.Min(s.R, MathF.Min(s.G, s.B))) / 510f;

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
    /// The body genes of a random ancestor: a small, simple plant-eater of its realm. Its realm's primary producer is
    /// guaranteed in the diet; the other diet preferences and every other value inside the bounds are random.
    /// </summary>
    /// <param name="brainUnits">0: a random brain of up to BRAIN_START units; otherwise exactly this many (a big-brained ancestor)</param>
    public static void AncestorBody(Span<sbyte> body, uint seed, int ancestor, bool land, int brainUnits = 0, bool voiced = false, int floorClass = 0)
    {
        float U(int k) => Rng.U(seed, (uint)ancestor, (uint)k, Rs.Founder + 7000u);
        sbyte Lin(int k, float lo, float hi) => G.ToGene(lo + (hi - lo) * U(k), -1f, 1f);   // organ genes: -1..1 maps to 0..1 size
        for (int k = 0; k < G.NBODY; k++) body[k] = 0;
        int B = G.BODY;
        // size class: its floor, and a mass log-uniform from the floor up to 20 on land, 5 in water (at least 4x the floor)
        body[G.B_FLOOR - B] = (sbyte)Math.Clamp(floorClass, 0, K.FLOOR_CLASSES - 1);
        float fl = Body.Floors[body[G.B_FLOOR - B]], top = MathF.Min(K.MASS_HI, MathF.Max(4f * fl, land ? 20f : 5f));
        body[G.B_MASS - B] = G.ToGeneLog(fl * MathF.Pow(top / fl, U(0)), K.MASS_LO, K.MASS_HI);
        body[G.B_SPEED - B] = (sbyte)((U(1) - 0.5f) * 60f);
        body[G.B_SIGHT - B] = (sbyte)((U(2) - 0.5f) * 60f);
        // armour and weapons from the same range, so neither side of an arms race starts ahead (Revision L, ecology review)
        body[G.B_ARMOUR - B] = Lin(3, -1f, 0f);
        body[G.B_STRIKE - B] = Lin(4, -1f, 0f);
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
        // brain: a random size up to BRAIN_START units, or exactly brainUnits for a big-brained ancestor
        float units = brainUnits > 0 ? brainUnits : K.BRAIN_MIN + U(19) * (K.BRAIN_START - K.BRAIN_MIN);
        body[G.B_BRAIN - B] = Body.UnitsGene(units);
        body[G.B_HERM - B] = (sbyte)(U(35) < 0.5f ? -40 : 40);     // separate sexes or hermaphrodite
        body[G.B_SEXRATIO - B] = (sbyte)((U(36) - 0.5f) * 40f);    // about even sex ratio
        body[G.B_IMMUNE - B] = (sbyte)((U(37) * 2f - 1f) * 127f);
        body[G.B_WATER - B] = (sbyte)((U(38) * 2f - 1f) * 127f);   // water economy: anything
        body[G.B_VOICE - B] = (sbyte)(voiced ? 127 : -127);           // the user's design: one voiced lineage, the rest mute (a gene: it can evolve)
        // vocal learning (Revision N): the voiced lineage starts as a moderate learner that memorises tutors all its life;
        // the others start without it (genes: either can evolve)
        body[G.B_IMIT - B] = (sbyte)(voiced ? 0 : -127);
        body[G.B_IMIT_AGE - B] = (sbyte)(voiced ? 127 : -127);
        // body heat (Revision O): every ancestor starts with a thin coat and middling appendages, thermoneutral at about
        // 15-32 °C near T_REF at M = 1; founder variation and evolution do the rest (no tolerance is given)
        body[G.B_INSUL - B] = (sbyte)MathF.Round(K.FOUND_INS * 254f - 127f);
        body[G.B_APPEND - B] = (sbyte)MathF.Round(K.FOUND_APP * 254f - 127f);
        for (int k = 0; k < G.NMHC; k++) body[G.B_MHC - B + k] = (sbyte)((U(60 + k) * 2f - 1f) * 127f);   // recognition genes: anything
        // Diet preferences stay random (Revision K), but every ancestor can eat an abundant primary producer in its
        // realm: grass on land, phytoplankton in water. Without that staple, a random diet could make a founder unable
        // to eat the food available where it lives.
        for (int p = 0; p < Pools.N; p++) body[G.B_DIET - B + p] = (sbyte)(-90 + (int)(U(40 + p) * 40f));
        int good = 1 + (int)(U(70) * 3f);
        for (int k = 0; k < good; k++) body[G.B_DIET - B + (int)(U(71 + k) * Pools.N) % Pools.N] = (sbyte)(40 + (int)(U(74 + k) * 87f));
        body[G.B_DIET - B + (land ? (int)Pool.Grass : (int)Pool.Phyto)] = 127;
    }

    /// <summary>Standing variation: each founder's body genes vary around its ancestor's by FOUNDER_BODY_SD.</summary>
    public static void VaryBody(Span<sbyte> body, uint seed, uint key)
    {
        sbyte floor = body[G.B_FLOOR - G.BODY];   // the size class is copied exactly, not varied
        for (int k = 0; k < body.Length; k++)
            body[k] = (sbyte)Math.Clamp((int)MathF.Round(body[k] + Rng.N(seed, 0, key, Rs.Founder + 9000u + (uint)k) * K.FOUNDER_BODY_SD), -127, 127);
        // recognition genes are hyper-polymorphic within populations (MHC): each founder draws its own
        for (int k = 0; k < G.NMHC; k++)
            body[G.B_MHC - G.BODY + k] = (sbyte)(Rng.U(seed, 0, key, Rs.Founder + 9500u + (uint)k) * 254f - 127f);
        body[G.B_FLOOR - G.BODY] = floor;
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

    // The traits you can see on a body, each in "visible units": 1 unit is a difference you notice at a glance
    // (Revision L, the user: "subspeciation that is very clearly visual"): half again the body mass, 0.15 of an
    // organ's range (legs, fins, wings, armour, weapons, eyes), or one more pair of legs, fins or eyes.
    static readonly int[] VisGene = { G.B_MASS, G.B_SIGHT, G.B_ARMOUR, G.B_STRIKE, G.B_LEGS, G.B_FINS, G.B_WINGS, G.B_LEGN, G.B_FINN, G.B_EYEN };
    static readonly string[] VisName = { "size", "eyes", "armour", "weapons", "legs", "fins", "wings", "leg pairs", "fin pairs", "eye pairs" };
    const int V = 10;
    static readonly float MassUnit = MathF.Log(1.5f) / MathF.Log(K.MASS_HI / K.MASS_LO) * 254f;
    static float VisUnit(int k) => k == 0 ? MassUnit : k >= 7 ? 254f / 4f : 0.15f * 254f;
    // size is the body's actual size (its class floor included), not the mass gene alone
    static float Vis(World w, int i, int k) => k == 0 ? MathF.Log(w.PM[i] / K.MASS_LO) / MathF.Log(1.5f) : w.Genome[i * G.LEN + VisGene[k]] / VisUnit(k);

    /// <summary>
    /// Split clade s if its visible traits form two clusters with few intermediates (a gap of SPLIT_GAP pooled SDs
    /// along the axis between them, Mallet's genotypic clusters) and the two forms differ visibly (by at least
    /// SPLIT_VIS units in some trait). The new clade is a subspecies while the two centres are still more than half
    /// fertile together (their whole body genomes, as Mate uses), else a species. Returns the new clade or -1.
    /// </summary>
    public static int TrySplit(World w, int s, Scratch x)
    {
        // a deterministic sample of up to SAMPLE members (every k-th by slot)
        int members = x.Count[s], stride = Math.Max(1, members / SAMPLE), n = 0, seen = 0;
        for (int i = 0; i < w.Cap && n < SAMPLE; i++)
        {
            if (w.StageOf[i] == St.Dead || w.Spc[i] != s) continue;
            if (seen++ % stride != 0) continue;
            x.Sample[n] = i;
            for (int k = 0; k < V; k++) x.Vec[n * V + k] = Vis(w, i, k);
            n++;
        }
        if (n < 2 * K.MIN_SPLIT / Math.Max(1, stride)) return -1;
        // 2-means: start from the member farthest from the mean, and the one farthest from it
        Array.Clear(x.M);
        for (int a = 0; a < n; a++) for (int k = 0; k < V; k++) x.M[k] += x.Vec[a * V + k] / n;
        int f1 = FarthestV(x.Vec, n, x.M);
        Array.Copy(x.Vec, f1 * V, x.C1, 0, V);
        int f2 = FarthestV(x.Vec, n, x.C1);
        Array.Copy(x.Vec, f2 * V, x.C2, 0, V);
        Span<float> s1 = stackalloc float[V];
        for (int it = 0; it < 10; it++)
        {
            Array.Clear(x.M); s1.Clear();         // M accumulates cluster 2, s1 cluster 1
            int n1 = 0, n2 = 0;
            for (int a = 0; a < n; a++)
            {
                if (D2V(x.Vec, a, x.C1) <= D2V(x.Vec, a, x.C2)) { n1++; for (int k = 0; k < V; k++) s1[k] += x.Vec[a * V + k]; }
                else { n2++; for (int k = 0; k < V; k++) x.M[k] += x.Vec[a * V + k]; }
            }
            if (n1 == 0 || n2 == 0) return -1;
            for (int k = 0; k < V; k++) { x.C1[k] = s1[k] / n1; x.C2[k] = x.M[k] / n2; }
        }
        // the two forms must look different: some trait apart by SPLIT_VIS units
        int most = 0; float big = 0;
        for (int k = 0; k < V; k++) { float d = MathF.Abs(x.C2[k] - x.C1[k]); if (d > big) { big = d; most = k; } }
        float sep = 0; for (int k = 0; k < V; k++) { float d = x.C2[k] - x.C1[k]; sep += d * d; }
        sep = MathF.Sqrt(sep);
        w.Sp[s].SplitDist = big / K.SPLIT_VIS; w.Sp[s].SplitGap = 0;
        if (big < K.SPLIT_VIS) return -1;
        // few intermediates: along the axis between the centres, the gap is SPLIT_GAP pooled SDs
        float m1 = 0, m2 = 0, q1 = 0, q2 = 0; int k1 = 0, k2 = 0;
        for (int a = 0; a < n; a++)
        {
            float t = 0;
            for (int k = 0; k < V; k++) t += (x.Vec[a * V + k] - x.C1[k]) * (x.C2[k] - x.C1[k]);
            t /= sep;
            if (D2V(x.Vec, a, x.C1) <= D2V(x.Vec, a, x.C2)) { m1 += t; q1 += t * t; k1++; } else { m2 += t; q2 += t * t; k2++; }
        }
        if (k1 == 0 || k2 == 0) return -1;
        m1 /= k1; m2 /= k2;
        float pooled = MathF.Sqrt(MathF.Max(1e-8f, (q1 - k1 * m1 * m1 + q2 - k2 * m2 * m2) / n));
        w.Sp[s].SplitGap = (m2 - m1) / pooled / K.SPLIT_GAP;
        if ((m2 - m1) / pooled < K.SPLIT_GAP) return -1;
        // count the whole clade by nearest centre; both sides must be viable populations
        int c1 = 0, c2 = 0;
        Array.Clear(x.G1); Array.Clear(x.G2);   // whole body genomes of the two forms, for their fertility together
        for (int i = 0; i < w.Cap; i++)
        {
            if (w.StageOf[i] == St.Dead || w.Spc[i] != s) continue;
            bool second = NearSecondV(w, i, x);
            var g = second ? x.G2 : x.G1;
            for (int k = 0; k < D; k++) g[k] += Gene(w, i, k);
            if (second) c2++; else c1++;
        }
        if (c1 < K.MIN_SPLIT || c2 < K.MIN_SPLIT) return -1;
        float dg = 0; for (int k = 0; k < D; k++) { float d = x.G1[k] / c1 - x.G2[k] / c2; dg += d * d; }
        dg = MathF.Sqrt(dg / D) / K.D_COMPAT;
        bool sub = MathF.Exp(-dg * dg * dg * dg) > 0.5f;   // still more than half fertile together
        bool secondIsNew = c2 <= c1;
        int nc = NewClade(w, s, w.Sp[s].Ancestor, sub);
        if (nc < 0) return -1;
        for (int i = 0; i < w.Cap; i++)
        {
            if (w.StageOf[i] == St.Dead || w.Spc[i] != s) continue;
            if (NearSecondV(w, i, x) == secondIsNew) w.Spc[i] = (byte)nc;
        }
        // how the new form looks against the old: its two most different traits
        var cn = secondIsNew ? x.C2 : x.C1; var co = secondIsNew ? x.C1 : x.C2;
        int second2 = -1; float b2 = 0;
        for (int k = 0; k < V; k++) { float d = MathF.Abs(cn[k] - co[k]); if (k != most && d > b2 && d >= 0.5f * K.SPLIT_VIS) { b2 = d; second2 = k; } }
        string Why(int k) => k == 0 ? (cn[0] > co[0] ? $"bigger (x{MathF.Pow(1.5f, cn[0] - co[0]):0.#})" : $"smaller (x{MathF.Pow(1.5f, cn[0] - co[0]):0.##})")
                              : (cn[k] > co[k] ? "more " : "fewer ").Replace("fewer ", k >= 7 ? "fewer " : "less ") + VisName[k];
        w.Sp[nc].SplitWhy = second2 >= 0 ? Why(most) + ", " + Why(second2) : Why(most);
        w.T.Splits++;
        return nc;
    }

    static bool NearSecondV(World w, int i, Scratch x)
    {
        float d1 = 0, d2 = 0;
        for (int k = 0; k < V; k++) { float v = Vis(w, i, k), a = v - x.C1[k], b = v - x.C2[k]; d1 += a * a; d2 += b * b; }
        return d2 < d1;
    }

    static int FarthestV(float[] vec, int n, float[] c)
    {
        int best = 0; float bd = -1;
        for (int a = 0; a < n; a++) { float d = D2V(vec, a, c); if (d > bd) { bd = d; best = a; } }
        return best;
    }

    static float D2V(float[] vec, int a, float[] c) { float s = 0; for (int k = 0; k < V; k++) { float d = vec[a * V + k] - c[k]; s += d * d; } return s; }
}
