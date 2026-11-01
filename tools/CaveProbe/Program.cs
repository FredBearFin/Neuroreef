// Usage: CaveProbe <outdir> <map> <seed>...   Builds each seed's land (Terrain.Build, then a year of the water cycle) on a
// map of the given side, prints the caves' census, Caves.Build's time and the karst springs' discharge, and writes per seed
// (FW x FW): elev_, carb_, flow_ (cave discharge), void_, depth_ (.f32), kind_, cell_, rock_ (.u8), seep_ (.f32, mm/update),
// and meta_<map>_<seed>.txt (FW sea). tools/CaveProbe/render.py draws them:
//   python3 tools/CaveProbe/render.py <outdir> <map> 3,7,11 docs/art/terrain2/caves
using System;
using System.Diagnostics;
using System.IO;
using Neuroreef;

string dir = args[0]; int map = int.Parse(args[1]);
Directory.CreateDirectory(dir);
Map.Set(map);
for (int a = 2; a < args.Length; a++)
{
    uint seed = uint.Parse(args[a]);
    var w = new World(16, seed);
    var rain = Terrain.Shape(w);
    var sw = Stopwatch.StartNew();
    Terrain.Finish(w, rain);
    double tFinish = sw.Elapsed.TotalSeconds;
    sw.Restart();
    for (int k = 0; k < 3; k++) Caves.Build(w, rain);
    double tCaves = sw.Elapsed.TotalSeconds / 3;
    var c = Caves.Count(w);
    int n = Map.FW * Map.FW, sed = 0, volc = 0;
    for (int i = 0; i < n; i++) if (w.Cell[i] == (byte)Realm.Land) { if (w.Rock[i] == Rock.Sedimentary) sed++; if (w.Rock[i] == Rock.Volcanic) volc++; }
    Console.WriteLine($"map {map} seed {seed}: land {c.Land}, sedimentary {100.0 * sed / c.Land:F1}%, volcanic {100.0 * volc / c.Land:F1}%; karst {100.0 * c.KarstCells / c.Land:F1}% of land");
    Console.WriteLine($"  passages {c.Passages} cells ({100.0 * c.Passages / Math.Max(1, c.KarstCells):F1}% of karst), lava tubes {c.LavaCells} cells; sinkholes {c.Sinkholes} (ponors {c.Ponors}), springs {c.Springs}, entrances {c.Entrances}, losing river cells {c.Losing}");
    Console.WriteLine($"  Caves.Build {1000 * tCaves:F0} ms (Terrain.Finish with it {tFinish:F2} s)");

    WaterCycle.Init(w);
    float springMin = K.SPRING_MIN * 365f * WaterCycle.YearFrac;
    int ks = 0, kd = 0; double seepK = 0, seepAll = 0;
    for (int i = 0; i < n; i++)
    {
        seepAll += w.Seep[i];
        if ((w.CaveKind[i] & Caves.Spring) != 0) { ks++; seepK += w.Seep[i]; if (w.Seep[i] >= springMin) kd++; }
    }
    Console.WriteLine($"  after spin-up: karst springs drinkable {kd}/{ks}, carrying {100 * seepK / Math.Max(1e-9, seepAll):F0}% of all emerging groundwater");

    void F(string name, float[] v) { var b = new byte[v.Length * 4]; Buffer.BlockCopy(v, 0, b, 0, b.Length); File.WriteAllBytes(Path.Combine(dir, $"{name}_{map}_{seed}.f32"), b); }
    void B(string name, byte[] v) => File.WriteAllBytes(Path.Combine(dir, $"{name}_{map}_{seed}.u8"), v);
    F("elev", w.Elev); F("carb", w.Carb); F("flow", w.CaveFlow); F("void", w.CaveVoid); F("depth", w.CaveDepth); F("seep", w.Seep);
    B("kind", w.CaveKind); B("cell", w.Cell); B("rock", w.Rock);
    File.WriteAllText(Path.Combine(dir, $"meta_{map}_{seed}.txt"), $"{Map.FW} {w.SeaLevel}\n");
}
