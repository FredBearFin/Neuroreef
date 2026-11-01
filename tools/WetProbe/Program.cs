// Usage: WetProbe <outdir> <map> <seed>...   For each seed: the land of a new world (Terrain, the water cycle's spin-up and
// PLANT_SPINUP years of plants, no animals), then one more year of weather, plants and water. Prints water depths in
// metres by type (sea, lake, river), the realms, floods, the water table, and dumps FW x FW rasters (little-endian):
//   elev_, depth_ (m), deep_ (groundwater / GW_CAP), sat_ (share of the year the water table was at the surface),
//   moist_, flood_ (.f32), cell_, wet_ (.u8), and meta_<seed>.txt (FW sea). Extra.cs adds the wetlands.
// python3 tools/WetProbe/draw.py <outdir> <seed,...> <png prefix> draws them.
using System;
using System.IO;
using System.Linq;
using Neuroreef;

string dir = args[0]; int map = int.Parse(args[1]);
Directory.CreateDirectory(dir);
Map.Set(map);
for (int a = 2; a < args.Length; a++)
{
    uint seed = uint.Parse(args[a]);
    var t0 = System.Diagnostics.Stopwatch.StartNew();
    using var sim = new Simulation(64, seed, 2, empty: true);
    var w = sim.W;
    int FW = Map.FW, n = FW * FW;
    var sat = new float[n]; var flood = new float[n]; int ups = 0;
    for (int s = 0; s < Nature.SeasonLength; s++)
    {
        sim.Step();
        if (w.Step % K.WC_EVERY == 1)
        {
            ups++;
            for (int i = 0; i < n; i++)
            {
                if (w.Cell[i] != (byte)Realm.Land) continue;
                if (w.Deep[i] >= 0.98f * K.GW_CAP || w.Wet[i] >= 2) sat[i]++;
                if (w.Wet[i] == 3) flood[i]++;
            }
        }
    }
    for (int i = 0; i < n; i++) { sat[i] /= ups; flood[i] /= ups; }
    var depthM = new float[n];
    for (int i = 0; i < n; i++) depthM[i] = (w.Cell[i] & (byte)Realm.Water) != 0 ? w.Depth[i] / w.ElevPerM : 0f;
    Console.WriteLine($"map {map} seed {seed}: FW {FW}, sea level {w.SeaLevel:F3}, {1f / w.ElevPerM:F0} m per unit of elevation; built and run a year in {t0.Elapsed.TotalSeconds:F0} s");
    float[] edges = { 0.5f, 1, 2, 5, 10, 30, 100, 400, 1e9f };
    void Hist(string name, Func<int, bool> pick)
    {
        var d = Enumerable.Range(0, n).Where(i => (w.Cell[i] & (byte)Realm.Water) != 0 && pick(i)).Select(i => depthM[i]).OrderBy(v => v).ToArray();
        if (d.Length == 0) { Console.WriteLine($"  {name,-6} none"); return; }
        var cnt = new int[edges.Length];
        foreach (var v in d) { int k = 0; while (v > edges[k]) k++; cnt[k]++; }
        string h = string.Join(" ", edges.Select((e, k) => $"<={(e > 1e8 ? "inf" : e.ToString())}:{100.0 * cnt[k] / d.Length:F0}%"));
        Console.WriteLine($"  {name,-6} {d.Length,6} cells ({100.0 * d.Length / n:F1}%): p10 {d[d.Length / 10]:F1} p50 {d[d.Length / 2]:F1} p90 {d[d.Length * 9 / 10]:F1} max {d[^1]:F1} m | {h}");
    }
    bool IsSea(int i) => w.LakeOf[i] >= 0 && w.LakeExit[w.LakeOf[i]] < 0;
    Hist("all", i => true);
    Hist("sea", IsSea);
    Hist("lake", i => w.LakeOf[i] >= 0 && !IsSea(i));
    Hist("river", i => w.LakeOf[i] < 0);
    int shallow = 0, deep = 0, shallowLake = 0, lakeCells = 0;
    for (int i = 0; i < n; i++)
    {
        if (w.Cell[i] == (byte)Realm.Shallow) shallow++; if (w.Cell[i] == (byte)Realm.Deep) deep++;
        if (w.LakeOf[i] >= 0) { lakeCells++; if (w.Cell[i] == (byte)Realm.Shallow) shallowLake++; }
    }
    int land = 0, sat50 = 0, sat25 = 0, fl10 = 0, m95 = 0; double sF = 0, sS = 0, mM = 0;
    for (int i = 0; i < n; i++)
    {
        if (w.Cell[i] != (byte)Realm.Land) continue;
        land++; sF += flood[i]; sS += sat[i]; mM += w.Moisture[i];
        if (sat[i] >= 0.5f) sat50++; if (sat[i] >= 0.25f) sat25++; if (flood[i] >= 0.1f) fl10++; if (w.Moisture[i] >= 0.95f) m95++;
    }
    Console.WriteLine($"  realm: shallow {100.0 * shallow / n:F1}%, deep {100.0 * deep / n:F1}% of the map; sea and lakes shallow {100.0 * shallowLake / Math.Max(1, lakeCells):F1}%; land {land} cells");
    Console.WriteLine($"  land: flooded {100 * sF / land:F2}% of the year on average, >=10% of the year {100.0 * fl10 / land:F2}%; water table at the surface (deep store >= 98%, a pond, spring or flood) {100 * sS / land:F1}% of the year on average, >=25% {100.0 * sat25 / land:F1}%, >=50% {100.0 * sat50 / land:F1}% of land; moisture mean {mM / land:F2}, >=0.95 on {100.0 * m95 / land:F1}%");
    var (wm, ws, wg, wp, wsp, wf, wq) = WaterCycle.Stats(w);
    Console.WriteLine($"  water cycle: budget rain {w.WbRain:E3}, ET {w.WbET:E3} ({w.WbET / Math.Max(1, w.WbRain):F2}), ponds {wp}, springs {wsp}, flooded {wf}");
    Extra.Report(w, sat);
    void F(string name, float[] v) { var b = new byte[v.Length * 4]; Buffer.BlockCopy(v, 0, b, 0, b.Length); File.WriteAllBytes(Path.Combine(dir, $"{name}_{seed}.f32"), b); }
    F("elev", w.Elev); F("depth", depthM); F("deep", w.Deep.Select(v => v / K.GW_CAP).ToArray()); F("sat", sat); F("moist", w.Moisture); F("flood", flood);
    File.WriteAllBytes(Path.Combine(dir, $"cell_{seed}.u8"), w.Cell);
    File.WriteAllBytes(Path.Combine(dir, $"wet_{seed}.u8"), w.Wet);
    Extra.Dump(w, dir, seed);
    File.WriteAllText(Path.Combine(dir, $"meta_{seed}.txt"), $"{FW} {w.SeaLevel}\n");
}
