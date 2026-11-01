// Usage: RiverProbe <outdir> <map> <seed>...   Builds each terrain seed's land (Terrain.Build) on a map of the given
// side, prints the river network's form (RiverStats) and the build time, and writes per seed (FW x FW, little-endian):
//   elev_<seed>.f32 (0..1), cell_<seed>.u8 (realm bits), lake_<seed>.i32 (LakeOf), recv_<seed>.i32 (Recv),
//   flow_<seed>.f32 (Flow), from_<seed>.i32 (FloodFrom), rivx_/rivy_/rivw_<seed>.f32 (channel geometry),
//   stats_<seed>.json (the histograms and links), meta_<seed>.txt (FW sea seconds)
// python3 tools/RiverProbe/draw.py <outdir> <seed> <png prefix> draws them.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Neuroreef;

string dir = args[0]; int map = int.Parse(args[1]);
Directory.CreateDirectory(dir);
Map.Set(map);
for (int a = 2; a < args.Length; a++)
{
    // "valley:<slope>": the synthetic valley of HydrologyTests.SinuosityRisesInLowlands (an alluvial plain of that valley
    // slope, m/m, below a steep upper reach), dumped as seed 9000 + slope x 1e5
    bool valley = args[a].StartsWith("valley:");
    uint seed = valley ? 9000u + (uint)MathF.Round(float.Parse(args[a][7..]) * 1e5f) : uint.Parse(args[a]);
    var w = new World(16, valley ? 5u : seed);
    var sw = Stopwatch.StartNew();
    if (valley) Valley(w, float.Parse(args[a][7..])); else Terrain.Build(w);
    double s = sw.Elapsed.TotalSeconds;
    if (!valley && Environment.GetEnvironmentVariable("PROBE_TIME") != null)
    {
        // the water alone: Waters (with the channel lines) on the shaped land, best of three
        var v = new World(16, seed); var rain = Terrain.Shape(v); double best = 1e9;
        for (int k = 0; k < 3; k++) { var t = Stopwatch.StartNew(); Hydrology.Waters(v, rain); best = Math.Min(best, t.Elapsed.TotalSeconds); }
        Console.WriteLine($"  Hydrology.Waters {best:F3} s");
    }
    var R = RiverStats.Measure(w);
    var asp = new double[RiverStats.BINS];
    double aspect = RiverStats.AspectSpike(w, 0.002f, asp);
    int n = Map.FW * Map.FW, sea = 0, lakes = 0, rivers = 0;
    for (int i = 0; i < n; i++) { if (w.LakeOf[i] >= 0) { if (w.LakeExit[w.LakeOf[i]] < 0) sea++; else lakes++; } else if (w.Cell[i] != (byte)Realm.Land) rivers++; }
    double dd = 0; int nd = 0; for (int i = 0; i < n; i++) if (w.Cell[i] == (byte)Realm.Land) { dd += w.DistDrink[i]; nd++; }
    Console.WriteLine($"map {map} seed {seed}: Terrain.Build {s:F2} s; sea {100.0 * sea / n:F1}%, lakes {100.0 * lakes / n:F2}%, rivers {100.0 * rivers / n:F2}%, drinkable water {dd / nd:F2} cells away, {w.PondSite.Sum(v => (int)v)} pond sites");
    Console.WriteLine($"  relief aspect at multiples of 45 deg (+-2.5): {aspect:P1} (uniform 11.1%)");
    Console.WriteLine(R);
    void F(string name, float[] v) { var b = new byte[v.Length * 4]; Buffer.BlockCopy(v, 0, b, 0, b.Length); File.WriteAllBytes(Path.Combine(dir, $"{name}_{seed}.f32"), b); }
    void I(string name, int[] v) { var b = new byte[v.Length * 4]; Buffer.BlockCopy(v, 0, b, 0, b.Length); File.WriteAllBytes(Path.Combine(dir, $"{name}_{seed}.i32"), b); }
    F("elev", w.Elev); F("flow", w.Flow); F("rivx", w.RivX); F("rivy", w.RivY); F("rivw", w.RivW);
    I("lake", w.LakeOf); I("recv", w.Recv); I("from", w.FloodFrom);
    File.WriteAllBytes(Path.Combine(dir, $"cell_{seed}.u8"), w.Cell);
    File.WriteAllText(Path.Combine(dir, $"meta_{seed}.txt"), $"{Map.FW} {w.SeaLevel} {s:F2}\n");
    File.WriteAllText(Path.Combine(dir, $"stats_{seed}.json"), JsonSerializer.Serialize(new
    {
        step = R.StepHist, reach = R.ReachHist, aspect = asp, stepSpike = R.StepSpike, reachSpike = R.ReachSpike, aspectSpike = aspect,
        sinLow = R.SinLow, sinMid = R.SinMid, sinHigh = R.SinHigh, sinMedian = R.SinMedian, rb = R.Rb, rl = R.RL,
        orders = R.StreamsOfOrder.Take(R.MaxOrder + 1).ToArray(), lengths = R.LengthOfOrder.Take(R.MaxOrder + 1).ToArray(),
        density = R.Density, parallel = R.Parallel, straight = R.Straight, longLinks = R.LongLinks,
        links = R.LinkInfo.Select(l => new[] { l.x, l.y, l.sin, l.slope, l.order }).ToArray(),
    }, new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
}

// the valley of HydrologyTests.Valley (keep the two the same)
static void Valley(World w, float lowSlope)
{
    int FW = Map.FW, n = FW * FW;
    w.SeaLevel = 0.05f;
    float mPer = Map.ReliefM / (1f - w.SeaLevel), cell = K.GEO_CELL_M;   // the map's vertical scale (2026-11-01)
    float low = lowSlope * cell / mPer, side = 0.1f * cell / mPer, floor = 2f * cell / mPer * 0.0002f;
    for (int y = 0; y < FW; y++)
        for (int x = 0; x < FW; x++)
        {
            float d = MathF.Min(y, FW - y);
            float along = d < 6 ? -0.02f : (d - 6) * low;
            float ax = MathF.Abs(x - FW / 2);
            const float fw = 20f;                                             // a 40-cell floor
            float across = ax <= fw ? ax * floor : fw * floor + (ax - fw) * side;
            float noise = 0.4f * floor * (((x * 73856093) ^ (y * 19349663)) & 1023) / 1023f;
            w.Elev[y * FW + x] = w.SeaLevel + along + across + (d < 6 ? 0f : noise);
        }
    var rain = new float[n];
    for (int i = 0; i < n; i++) rain[i] = w.Elev[i] >= w.SeaLevel ? 1.5f : 0f;
    Hydrology.Waters(w, rain);
}
