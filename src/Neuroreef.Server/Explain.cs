// Explanations and behaviour fingerprints (DESIGN §12.3, §12.4; NR-213, NR-403). Both run on the server
// thread and only read the simulation. Explanations are fixed sentence rules over the telemetry ring;
// every sentence carries the numbers it is based on. Fingerprints cluster the live animals' primitive
// gains, to show which combinations of primitives each species actually uses.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Neuroreef.Server;

public static class Explain
{
    public sealed record Sentence(string text, int species, string kind);

    public static List<Sentence> Run(World w, int windowSamples = 32)
    {
        var res = new List<Sentence>();
        var T = w.T;
        int n = T.SampleCount;
        if (n < 4) return res;
        int back = Math.Min(windowSamples, n - 1);
        var now = new float[T.Len]; var then = new float[T.Len]; var acc = new float[T.Len];
        T.Sample(n - 1, now); T.Sample(n - 1 - back, then);
        for (int k = n - back; k < n; k++)
        {
            var a = new float[T.Len]; T.Sample(k, a);
            for (int j = 0; j < T.Len; j++) acc[j] += a[j];
        }
        int steps = back * K.SAMPLE_EVERY;
        int O(int s, int f) => T.SpeciesOffset(s, f);
        for (int s = 0; s < w.NS; s++)
        {
            string name = w.Sp[s].Name;
            float c0 = then[O(s, Telemetry.S_COUNT)], c1 = now[O(s, Telemetry.S_COUNT)];
            float deaths = 0; for (int c = 0; c < Cause.N; c++) deaths += acc[O(s, Telemetry.S_DEATHS + c)];
            float births = acc[O(s, Telemetry.S_BIRTHS)], imm = acc[O(s, Telemetry.S_IMM)];
            if (c0 > 0 && c1 == 0) { res.Add(new($"{name} died out in the last {steps:N0} steps ({deaths:N0} deaths, {births:N0} births).", s, "extinction")); continue; }
            if (c0 == 0 && c1 > 0) { res.Add(new($"{name} reappeared: {c1:N0} now, from {births:N0} births and {imm:N0} immigrants.", s, "return")); continue; }
            if (c0 < 10) continue;
            float ch = (c1 - c0) / c0;
            if (MathF.Abs(ch) < 0.25f) continue;
            string dir = ch > 0 ? "rose" : "fell";
            string why;
            if (ch < 0 && deaths > 0)
            {
                int top = 0; float tv = 0;
                for (int c = 0; c < Cause.N; c++) { float v = acc[O(s, Telemetry.S_DEATHS + c)]; if (v > tv) { tv = v; top = c; } }
                why = $"{100 * tv / deaths:F0}% of {deaths:N0} deaths were {Cause.Names[top]}";
                if (top == Cause.Killed)
                {
                    int killer = -1; float kv = 0;
                    for (int a = 0; a < w.NS; a++) { float v = acc[T.OffKill + a * w.NS + s]; if (v > kv) { kv = v; killer = a; } }
                    if (killer >= 0) why += $", most by {w.Sp[killer].Name} ({kv:N0})";
                }
            }
            else why = $"{births:N0} births against {deaths:N0} deaths" + (imm > 0 ? $" and {imm:N0} immigrants" : "");
            int food = -1; float fv = 0, ftot = 0;
            for (int p = 0; p < Pools.N; p++) { float v = acc[O(s, Telemetry.S_INTAKE + p)]; ftot += v; if (v > fv) { fv = v; food = p; } }
            string foodS = food >= 0 && ftot > 0 ? $"; main food {Pools.Names[food]} ({100 * fv / ftot:F0}% of intake)" : "";
            res.Add(new($"{name} {dir} {MathF.Abs(ch) * 100:F0}% in {steps:N0} steps ({c0:N0} → {c1:N0}): {why}{foodS}.", s, ch > 0 ? "rise" : "fall"));
        }
        // vegetation and fire
        float land = MathF.Max(1, CountLand(w));
        float g0 = then[Telemetry.G_POOLS] / land, g1 = now[Telemetry.G_POOLS] / land;
        if (g0 > 0.02f && MathF.Abs(g1 - g0) / g0 > 0.2f)
            res.Add(new($"Grass cover went from {g0 * 100:F0}% to {g1 * 100:F0}% of land; shrubs {now[Telemetry.G_SHRUB] * 100:F0}%, trees {now[Telemetry.G_TREE] * 100:F0}%.", -1, "plants"));
        float ign = acc[Telemetry.G_FIRE];
        if (ign > 100) res.Add(new($"Fire burned {ign:N0} cells in {steps:N0} steps (season {(now[Telemetry.G_SEASON] < 0 ? "dry" : "wet")}).", -1, "fire"));
        return res;
    }

    static int _land = -1;
    static int CountLand(World w)
    {
        if (_land < 0) { int c = 0; for (int i = 0; i < K.FW * K.FW; i++) if (w.Cell[i] == (byte)Realm.Land) c++; _land = c; }
        return _land;
    }
}

public static class Fingerprint
{
    public sealed record Cluster(string label, float share, float[] centroid, int[] top);
    public sealed record Species(int species, int sampled, List<Cluster> clusters);

    /// <summary>k-means (k = 3) on up to 160 animals per species. Reads gains racily; display only.</summary>
    public static List<Species> Run(World w)
    {
        const int KC = 3, MAXS = 160, NP = P.N;
        var res = new List<Species>();
        var samples = new List<float[]>[w.NS];
        for (int s = 0; s < w.NS; s++) samples[s] = new List<float[]>();
        var global = new float[NP]; int gn = 0;
        int stride = 1 + w.AliveCount / 4000;
        for (int i = 0; i < w.Cap; i += stride)
        {
            if (w.StageOf[i] < St.Juvenile) continue;
            var l = samples[w.Spc[i]];
            if (l.Count >= MAXS) continue;
            var v = new float[NP];
            Array.Copy(w.Gain, i * NP, v, 0, NP);
            // actions are sampled 0/1 each step; their gain is the probability, which the sigmoid of z gives
            l.Add(v);
            for (int p = 0; p < NP; p++) global[p] += v[p];
            gn++;
        }
        if (gn == 0) return res;
        for (int p = 0; p < NP; p++) global[p] /= gn;
        for (int s = 0; s < w.NS; s++)
        {
            var X = samples[s];
            if (X.Count < 6) { res.Add(new Species(s, X.Count, new List<Cluster>())); continue; }
            var cent = new float[KC][];
            cent[0] = (float[])X[0].Clone();
            for (int k = 1; k < KC; k++)
            {
                float best = -1; int bi = 0;
                for (int i = 0; i < X.Count; i++)
                {
                    float d = float.MaxValue;
                    for (int c = 0; c < k; c++) d = MathF.Min(d, Dist(X[i], cent[c]));
                    if (d > best) { best = d; bi = i; }
                }
                cent[k] = (float[])X[bi].Clone();
            }
            var asg = new int[X.Count];
            for (int it = 0; it < 10; it++)
            {
                for (int i = 0; i < X.Count; i++)
                {
                    float bd = float.MaxValue;
                    for (int c = 0; c < KC; c++) { float d = Dist(X[i], cent[c]); if (d < bd) { bd = d; asg[i] = c; } }
                }
                for (int c = 0; c < KC; c++)
                {
                    var m = new float[NP]; int cnt = 0;
                    for (int i = 0; i < X.Count; i++) if (asg[i] == c) { cnt++; for (int p = 0; p < NP; p++) m[p] += X[i][p]; }
                    if (cnt > 0) { for (int p = 0; p < NP; p++) m[p] /= cnt; cent[c] = m; }
                }
            }
            var cl = new List<Cluster>();
            for (int c = 0; c < KC; c++)
            {
                int cnt = asg.Count(a => a == c);
                if (cnt == 0) continue;
                var top = Enumerable.Range(0, NP).OrderByDescending(p => cent[c][p] - global[p]).Take(3).ToArray();
                string label = string.Join(" + ", top.Select(p => P.Names[p]));
                cl.Add(new Cluster(label, cnt / (float)X.Count, cent[c], top));
            }
            cl.Sort((a, b) => b.share.CompareTo(a.share));
            res.Add(new Species(s, X.Count, cl));
        }
        return res;
    }

    static float Dist(float[] a, float[] b) { float d = 0; for (int p = 0; p < a.Length; p++) { float e = a[p] - b[p]; d += e * e; } return d; }
}
