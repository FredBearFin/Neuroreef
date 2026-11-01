// Usage: ScaleProbe <outdir> <map> <seed>...   (2026-11-01, the 3.5 km world) Builds each world's land (Terrain.Build:
// geology, the fine passes, water, caves) on a map of the given side and prints its scale: width, vertical scale,
// relief above the lakes, slopes, rivers (count, true widths, drainage areas), lakes, rain contrast, temperature,
// glaciers and rock. Writes per seed, FW x FW float32: elev_ (0..1), rock_, cell_ (World.Cell), rivwm_ (true river
// width in m, 0 off rivers), ice_ (glacier ice at the end of deep time, km); and meta_<map>_<seed>.txt
// (FW sea reliefM cellM latitude). tools/ScaleProbe/render.py draws them as shaded relief.
using System;
using System.IO;
using System.Linq;
using Neuroreef;

string dir = args[0]; int map = int.Parse(args[1]);
Directory.CreateDirectory(dir);
Map.Set(map);
static float Pct(float[] a, double q) { if (a.Length == 0) return float.NaN; var s = (float[])a.Clone(); Array.Sort(s); return s[Math.Clamp((int)(q * (s.Length - 1)), 0, s.Length - 1)]; }
for (int a = 2; a < args.Length; a++)
{
    uint seed = uint.Parse(args[a]);
    var w = new World(16, seed);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    Terrain.Build(w);
    double tb = sw.Elapsed.TotalSeconds;
    sw.Restart(); Geology.Form(new World(16, seed)); double tg = sw.Elapsed.TotalSeconds;
    int FW = Map.FW, n = FW * FW;
    float sea = w.SeaLevel, mPer = Map.ReliefM / (1f - sea), cell = K.GEO_CELL_M;
    float M(int i) => MathF.Max(0f, w.Elev[i] - sea) * mPer;
    var land = Enumerable.Range(0, n).Where(i => w.Cell[i] == (byte)Realm.Land).ToArray();
    var hl = land.Select(M).ToArray();
    // slope: steepest drop to a neighbour (deg)
    var sl = land.Select(i =>
    {
        int x = i % FW, y = i / FW; float best = 0f;
        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            int j = World.WrapI(y + dy, FW) * FW + World.WrapI(x + dx, FW);
            float d = (w.Elev[i] - MathF.Max(w.Elev[j], sea)) * mPer / (cell * (dx != 0 && dy != 0 ? 1.4142135f : 1f));
            best = MathF.Max(best, d);
        }
        return MathF.Atan(best) * 180f / MathF.PI;
    }).ToArray();
    // local relief in 1 km windows (max - min of the ground, lakes at their level)
    int r = Math.Max(1, (int)MathF.Round(500f / cell));
    var loc = new float[n];
    for (int y = 0; y < FW; y += 2) for (int x = 0; x < FW; x += 2)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++) { float e = MathF.Max(w.Elev[World.WrapI(y + dy, FW) * FW + World.WrapI(x + dx, FW)], sea); lo = MathF.Min(lo, e); hi = MathF.Max(hi, e); }
        loc[y * FW + x] = (hi - lo) * mPer;
    }
    var locs = loc.Where(v => v > 0f).ToArray();
    // rivers: channel and bank cells (RivW > 0); true widths from their discharge; drainage areas by routing unit rain
    var ones = new float[n]; Array.Fill(ones, 1f);
    var net = new Hydrology.Net(n); Hydrology.Route(w.Elev, sea, ones, net);
    var riv = Enumerable.Range(0, n).Where(i => w.RivW[i] > 0f && w.LakeOf[i] < 0).ToArray();
    var wm = riv.Select(i => Hydrology.WidthM(w.Flow[i])).ToArray();
    var area = riv.Select(i => net.Flow[i] * Map.CellKm2).ToArray();
    var qm = riv.Select(i => Hydrology.M3s(w.Flow[i])).ToArray();
    int lakeCells = Enumerable.Range(0, n).Count(i => w.LakeOf[i] >= 0);
    int lakes = Enumerable.Range(0, n).Where(i => w.LakeOf[i] >= 0).Select(i => w.LakeOf[i]).Distinct().Count();
    var rain = Terrain.RunoffRain(w); var rl = land.Select(i => rain[i]).ToArray();
    // rain against height and aspect: mean rain of the highest and lowest thirds of the land
    var byH = land.OrderBy(i => w.Elev[i]).ToArray(); int t3 = byH.Length / 3;
    float rLo = byH.Take(t3).Average(i => rain[i]), rHi = byH.Skip(2 * t3).Average(i => rain[i]);
    int lo0 = byH[0], hi0 = byH[^1];
    var crust = Geology.Form(new World(16, seed));
    int ice = crust.FineIce.Count(v => v > 0f);
    var rk = new int[Rock.N]; foreach (int i in land) rk[w.Rock[i]]++;
    var cs = Caves.Count(w);
    Console.WriteLine($"map {map} seed {seed}: width {Map.WidthM / 1000f:F2} km ({FW} cells of {cell} m, unit {K.M_PER_UNIT} m), vertical scale {Map.ReliefM:F0} m, latitude {w.LatDeg:F1}; Terrain.Build {tb:F1} s (Geology.Form {tg:F1} s)");
    Console.WriteLine($"  relief above the lakes: top {hl.Max():F0} m, land p50 {Pct(hl, 0.5):F0} p90 {Pct(hl, 0.9):F0} m; local relief in 1 km windows p50 {Pct(locs, 0.5):F0} p90 {Pct(locs, 0.9):F0} max {locs.Max():F0} m");
    Console.WriteLine($"  slopes (deg) p50 {Pct(sl, 0.5):F1} p90 {Pct(sl, 0.9):F1} p99 {Pct(sl, 0.99):F1} max {sl.Max():F1}; land {100.0 * land.Length / n:F1}% of the map");
    Console.WriteLine($"  rivers: {riv.Length} cells ({100.0 * riv.Length / n:F1}%), drainage density {riv.Length * cell / 1000f / (land.Length * Map.CellKm2):F2} km/km2; width (m) p50 {Pct(wm, 0.5):F2} p90 {Pct(wm, 0.9):F2} max {(wm.Length > 0 ? wm.Max() : 0):F2}; mean discharge max {(qm.Length > 0 ? qm.Max() : 0):F3} m3/s; drainage area max {(area.Length > 0 ? area.Max() : 0):F2} km2, p90 {Pct(area, 0.9):F2}");
    float fmax = 0f; int fi = 0; for (int i = 0; i < n; i++) if (w.LakeOf[i] < 0 && w.Flow[i] > fmax) { fmax = w.Flow[i]; fi = i; }
    Console.WriteLine($"  largest flow off the lakes: {fmax:F1} cells of mean rain ({fmax * Map.CellKm2:F3} km2), from {net.Flow[fi] * Map.CellKm2:F3} km2 of land");
    Console.WriteLine($"  lakes: {lakes} ({100.0 * lakeCells / n:F1}% of the map)");
    Console.WriteLine($"  rain over land (x mean): p10 {Pct(rl, 0.1):F2} p50 {Pct(rl, 0.5):F2} p90 {Pct(rl, 0.9):F2} min {rl.Min():F2} max {rl.Max():F2}; lowest third {rLo:F2}, highest third {rHi:F2}");
    Console.WriteLine($"  annual mean temperature (default climate x{Nature.Climate}): at the lakes {Climate.AnnualMean(w, lo0):F1} C, on top {Climate.AnnualMean(w, hi0):F1} C; glacier cells at the end of deep time {ice}");
    Console.WriteLine("  land rock: " + string.Join(", ", Enumerable.Range(0, Rock.N).Select(k => $"{Rock.Names[k]} {100.0 * rk[k] / land.Length:F1}%")) + $"; caves: karst cells {cs.KarstCells}, entrances {cs.Entrances}, sinkholes {cs.Sinkholes}, springs {cs.Springs}");
    void Dump(string name, float[] v)
    {
        var b = new byte[v.Length * 4]; Buffer.BlockCopy(v, 0, b, 0, b.Length);
        File.WriteAllBytes(Path.Combine(dir, $"{name}_{map}_{seed}.f32"), b);
    }
    var rw = new float[n]; foreach (int i in riv) rw[i] = Hydrology.WidthM(w.Flow[i]);
    Dump("elev", w.Elev); Dump("rock", Array.ConvertAll(w.Rock, b => (float)b)); Dump("cell", Array.ConvertAll(w.Cell, b => (float)b));
    Dump("rivwm", rw); Dump("ice", crust.FineIce);
    File.WriteAllText(Path.Combine(dir, $"meta_{map}_{seed}.txt"), $"{FW} {sea} {Map.ReliefM} {cell} {w.LatDeg}\n");
}
