// The wetlands' part of WetProbe (2026-11-01): wetland area and kinds, peat, the wetness index and height above drainage,
// and the plants of wetlands against dry land (flood tolerance, cover, genotypes).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Neuroreef;

static class Extra
{
    public static void Report(World w, float[] sat)
    {
        int n = Map.FW * Map.FW, land = 0;
        for (int i = 0; i < n; i++) if (w.Cell[i] == (byte)Realm.Land) land++;
        var (c, peat) = Wetlands.Count(w);
        int wet = c[1] + c[2] + c[3] + c[4];
        Console.WriteLine($"  wetlands {100.0 * wet / land:F2}% of land ({wet} cells): marsh {c[1]}, swamp {c[2]}, fen {c[3]}, bog {c[4]}; mean peat in wetlands {peat:F2} m");
        var pe = Enumerable.Range(0, n).Where(i => w.Cell[i] == (byte)Realm.Land && w.Peat[i] > 0.001f).Select(i => w.Peat[i]).OrderBy(v => v).ToArray();
        if (pe.Length > 0) Console.WriteLine($"  peat on {100.0 * pe.Length / land:F1}% of land (> 1 mm): p50 {pe[pe.Length / 2]:F2} p90 {pe[pe.Length * 9 / 10]:F2} max {pe[^1]:F2} m; >= 0.4 m on {pe.Count(v => v >= 0.4f)} cells; volume {pe.Sum() * K.GEO_CELL_M * K.GEO_CELL_M / 1e6:F1} million m3");
        // the water table and its drivers
        var dur = Enumerable.Range(0, n).Where(i => w.Cell[i] == (byte)Realm.Land).Select(i => w.WetDur[i]).ToArray();
        Console.WriteLine($"  waterlogged (WetDur) >= 0.1 of the year on {100.0 * dur.Count(v => v >= 0.1f) / land:F1}%, >= 0.25 {100.0 * dur.Count(v => v >= 0.25f) / land:F1}%, >= 0.5 {100.0 * dur.Count(v => v >= 0.5f) / land:F1}%, >= 0.9 {100.0 * dur.Count(v => v >= 0.9f) / land:F1}% of land; TWI mean {w.TwiMean:F1}");
        var hand = Enumerable.Range(0, n).Where(i => w.Cell[i] == (byte)Realm.Land).Select(i => w.Hand[i]).OrderBy(v => v).ToArray();
        Console.WriteLine($"  height above drainage: p10 {hand[hand.Length / 10]:F0} p50 {hand[hand.Length / 2]:F0} m; under 2 m {100.0 * hand.Count(v => v < 2f) / land:F1}%, under 5 m {100.0 * hand.Count(v => v < 5f) / land:F1}%, under 15 m {100.0 * hand.Count(v => v < 15f) / land:F1}% of land");
        // where the wetlands are: by setting
        int nearLake = 0, nearRiver = 0, spring = 0, flat = 0;
        for (int i = 0; i < n; i++)
        {
            if (w.Wetland[i] == 0) continue;
            int x = i % Map.FW, y = i / Map.FW; bool lk = false, rv = false;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                int j = World.WrapI(y + dy, Map.FW) * Map.FW + World.WrapI(x + dx, Map.FW);
                if (w.Cell[j] != (byte)Realm.Land) { if (w.LakeOf[j] >= 0) lk = true; else rv = true; }
            }
            if (lk) nearLake++; else if (rv) nearRiver++; else if (w.WetMin[i] > 0.1f) spring++; else flat++;
        }
        Console.WriteLine($"  wetland settings: lake or sea shore {nearLake}, river bank {nearRiver}, groundwater-fed away from water {spring}, rain-fed flats and hollows {flat}");
        // litter
        double lw = 0, ld = 0; int nw = 0, nd = 0;
        for (int i = 0; i < n; i++) { if (w.Cell[i] != (byte)Realm.Land) continue; float L = w.Pool[(int)Pool.Litter][i] + w.Pool[(int)Pool.Wood][i]; if (w.Wetland[i] != 0) { lw += L; nw++; } else { ld += L; nd++; } }
        Console.WriteLine($"  litter + wood: wetlands {lw / Math.Max(1, nw):F3}, dry land {ld / Math.Max(1, nd):F3}; nutrient wetlands {Mean(w, i => w.Wetland[i] != 0, i => w.Nutrient[i]):F3}, dry land {Mean(w, i => w.Cell[i] == (byte)Realm.Land && w.Wetland[i] == 0, i => w.Nutrient[i]):F3}; moisture wetlands {Mean(w, i => w.Wetland[i] != 0, i => w.Moisture[i]):F2}");
        float m3s = K.P_MEAN * 1e-3f * K.GEO_CELL_M * K.GEO_CELL_M / (365.25f * 86400f), qmax = 0f;
        for (int i = 0; i < n; i++) if (w.FloodFrom[i] == i) qmax = MathF.Max(qmax, w.Flow[i] * m3s);
        Console.WriteLine($"  largest river: mean discharge {qmax:F2} m3/s, mean depth {K.RIVER_D * MathF.Pow(qmax, K.RIVER_DEPTH_EXP):F2} m (before its floor of {K.RIVER_D_MIN} m)");
        Plants(w);
    }

    static double Mean(World w, Func<int, bool> pick, Func<int, float> v)
    {
        double s = 0; int c = 0;
        for (int i = 0; i < Map.FW * Map.FW; i++) if (pick(i)) { s += v(i); c++; }
        return c > 0 ? s / c : 0;
    }

    // the plants of wetlands (by waterlogging) against dry land: cover-weighted flood tolerance per stratum, cover, genotypes
    static void Plants(World w)
    {
        if (PT.N <= 20) return;
        int n = Map.FW * Map.FW; var t = w.Flora;
        string[] cls = { "dry (WetDur < 0.1)", "damp (0.1-0.25)", "wetland", "waterlogged >= 0.9" };
        for (int k = 0; k < 4; k++)
        {
            double[] cov = new double[3], fl = new double[3]; var gen = new HashSet<int>(); int cells = 0;
            var tolHist = new double[4];
            for (int i = 0; i < n; i++)
            {
                if (w.Cell[i] != (byte)Realm.Land) continue;
                float a = w.WetDur[i];
                bool pick = k switch { 0 => a < 0.1f, 1 => a >= 0.1f && a < 0.25f, 2 => w.Wetland[i] != 0, _ => a >= 0.9f };
                if (!pick) continue;
                cells++;
                for (int s = 0; s < 3; s++)
                {
                    int g = w.POcc[i * 4 + s]; if (g == 0) continue;
                    float b = s == 0 ? w.Pool[(int)Pool.Grass][i] : s == 1 ? w.Shrub[i] : w.Tree[i];
                    if (b <= 0f) continue;
                    int lv = t.Level(g, PT.FLOOD);
                    cov[s] += b; fl[s] += b * lv / 15.0; gen.Add(g); tolHist[Math.Min(3, lv / 4)] += b;
                }
            }
            if (cells == 0) continue;
            double tot = tolHist.Sum();
            Console.WriteLine($"  plants, {cls[k],-20} {cells,5} cells: cover herb {cov[0] / cells:F2} shrub {cov[1] / cells:F2} tree {cov[2] / cells:F2}; flood tolerance (cover-weighted) herb {fl[0] / Math.Max(1e-9, cov[0]):F2} shrub {fl[1] / Math.Max(1e-9, cov[1]):F2} tree {fl[2] / Math.Max(1e-9, cov[2]):F2}; cover by tolerance level 0-3/4-7/8-11/12-15: {string.Join("/", tolHist.Select(v => (100 * v / Math.Max(1e-9, tot)).ToString("F0")))}%; {gen.Count} genotypes");
        }
    }

    public static void Dump(World w, string dir, uint seed)
    {
        void F(string name, float[] v) { var b = new byte[v.Length * 4]; Buffer.BlockCopy(v, 0, b, 0, b.Length); File.WriteAllBytes(Path.Combine(dir, $"{name}_{seed}.f32"), b); }
        F("peat", w.Peat); F("twi", w.Twi); F("hand", w.Hand); F("wetdur", w.WetDur); F("tree", w.Tree); F("shrub", w.Shrub);
        File.WriteAllBytes(Path.Combine(dir, $"wetland_{seed}.u8"), w.Wetland);
        int n = Map.FW * Map.FW; var fl = new float[n];
        if (PT.N > 20)
            for (int i = 0; i < n; i++)
            {
                double c = 0, v = 0;
                for (int s = 0; s < 3; s++) { int g = w.POcc[i * 4 + s]; if (g == 0) continue; float b = s == 0 ? w.Pool[(int)Pool.Grass][i] : s == 1 ? w.Shrub[i] : w.Tree[i]; c += b; v += b * w.Flora.Level(g, PT.FLOOD) / 15.0; }
                fl[i] = c > 0 ? (float)(v / c) : -1f;
            }
        F("floodtol", fl);
    }
}
