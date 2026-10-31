// Usage: TerrainProbe <outdir> <map> <seed>...   Builds the land of each terrain seed (Terrain.Shape: geology and the
// fine erosion passes) on a map of the given side and writes, per seed:
//   elev_<map>_<seed>.f32    field-grid elevation (0..1), FW x FW float32
//   flow_<map>_<seed>.f32    rain gathered per cell (Hydrology.Route over the final relief)
//   alluv_<map>_<seed>.f32   alluvium depth (elevation units), FW x FW
//   coarse_<map>_<seed>.f32  the coarse deep-time relief in km, CW x CW (uplift_, bound_: its rock uplift and boundary kinds)
//   meta_<map>_<seed>.txt    FW CW sea coarseSea seconds residual
// tools/TerrainProbe/stats.py turns them into statistics and images (geomorphology, 2026-10-31):
//   python3 tools/TerrainProbe/stats.py <outdir> <map> 3,7,11 docs/art/terrain/geo_after
// PROBE_BUILD=1 also times a whole Terrain.Build.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Neuroreef;

string dir = args[0]; int map = int.Parse(args[1]);
Directory.CreateDirectory(dir);
Map.Set(map);
for (int a = 2; a < args.Length; a++)
{
    uint seed = uint.Parse(args[a]);
    var w = new World(16, seed);
    var sw = Stopwatch.StartNew();
    var rain = Terrain.Shape(w);
    double s = sw.Elapsed.TotalSeconds;
    int n = Map.FW * Map.FW;
    var net = new Hydrology.Net(n);
    Hydrology.Route(w.Elev, w.SeaLevel, rain, net);
    sw.Restart(); var c = Geology.Form(new World(16, seed)); double tf = sw.Elapsed.TotalSeconds;
    if (Environment.GetEnvironmentVariable("PROBE_BUILD") != null) { sw.Restart(); Terrain.Build(new World(16, seed)); Console.WriteLine($"  Geology.Form {tf:F2} s, Terrain.Build {sw.Elapsed.TotalSeconds:F2} s"); }
    void Dump(string name, float[] v)
    {
        var b = new byte[v.Length * 4]; Buffer.BlockCopy(v, 0, b, 0, b.Length);
        File.WriteAllBytes(Path.Combine(dir, $"{name}_{map}_{seed}.f32"), b);
    }
    Dump("elev", w.Elev); Dump("flow", net.Flow); Dump("alluv", w.Alluvium); Dump("coarse", c.H); Dump("uplift", c.Uplift); Dump("bound", Array.ConvertAll(c.Bound, b => (float)b));
    var rk = new int[Rock.N]; int nl = 0;
    for (int i = 0; i < n; i++) if (w.Elev[i] >= w.SeaLevel) { rk[w.Rock[i]]++; nl++; }
    int a0 = 0, a1 = 0, vA = 0, v0 = 0;
    for (int i = 0; i < n; i++) if (w.Elev[i] >= w.SeaLevel) { if (w.Alluvium[i] > 0f) a0++; if (w.Alluvium[i] > K.ALLUV_D) a1++; if (w.Rock[i] == Rock.Volcanic) { v0++; if (w.Alluvium[i] > 0f) vA++; } }
    Console.WriteLine($"  land with alluvium > 0: {100.0 * a0 / nl:F1}%, > ALLUV_D: {100.0 * a1 / nl:F1}%; volcanic cells {v0}, {vA} with alluvium");
    Console.WriteLine("  land rock: " + string.Join(", ", Enumerable.Range(0, Rock.N).Select(k => $"{Rock.Names[k]} {100.0 * rk[k] / nl:F1}%")));
    File.WriteAllText(Path.Combine(dir, $"meta_{map}_{seed}.txt"), $"{Map.FW} {c.CW} {w.SeaLevel} {c.Sea} {s:F2} {c.Residual}\n");
    Console.WriteLine($"map {map} seed {seed}: FW {Map.FW} CW {c.CW} shape {s:F2} s, residual {c.Residual:F3}, vertical scale {c.Scale:F2}, top {1000 * c.Top:F0} m");
}
