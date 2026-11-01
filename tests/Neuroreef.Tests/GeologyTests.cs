// Geology (Revision R): plates, uplift and deep-time erosion form the land. Mountains stand where plates converge,
// rifts sink and hold lakes, transform boundaries do little, hard rock wears slower than soft, and soils follow their
// parent rock. The genesis is deterministic and fits its time budget.

using System;
using System.Diagnostics;
using System.Linq;
using Xunit;

namespace Neuroreef.Tests;

public class GeologyTests
{
    static readonly uint[] Seeds = { 1, 2, 3 };

    /// <summary>A built world and the coarse deep-time state behind it (the genesis is deterministic, so a second
    /// Geology.Form of the same terrain seed is the one Terrain.Build used).</summary>
    static (World w, Geology.Crust c) Built(uint seed)
    {
        var w = new World(16, seed);
        Terrain.Build(w);
        return (w, Geology.Form(new World(16, seed)));
    }

    static int Coarse(Geology.Crust c, int i)
    {
        int f = Map.FW / c.CW;
        return (i / Map.FW / f) * c.CW + (i % Map.FW) / f;
    }

    static float Metres(World w, int i) => (w.Elev[i] - w.SeaLevel) / (1f - w.SeaLevel) * K.H_RELIEF;

    [Fact] // mountains lie along convergent boundaries: most of the highest land is in their belts, which are a minority of the map
    public void MountainsLieAlongConvergentBoundaries()
    {
        foreach (var seed in Seeds)
        {
            var (w, c) = Built(seed);
            int n = Map.FW * Map.FW;
            var land = Enumerable.Range(0, n).Where(i => w.Elev[i] >= w.SeaLevel).ToArray();
            float top = land.Select(i => w.Elev[i]).OrderBy(e => e).ElementAt(land.Length * 9 / 10);
            int topN = 0, topConv = 0, conv = 0; double hConv = 0, hIn = 0; int nIn = 0, nConv = 0;
            for (int i = 0; i < n; i++)
            {
                bool cv = c.Bound[Coarse(c, i)] == Geology.Convergent;
                if (cv) conv++;
                if (w.Elev[i] < w.SeaLevel) continue;
                if (w.Elev[i] >= top) { topN++; if (cv) topConv++; }
                if (cv) { hConv += Metres(w, i); nConv++; }
                else if (c.Bound[Coarse(c, i)] == Geology.Interior) { hIn += Metres(w, i); nIn++; }
            }
            hConv /= nConv; hIn /= nIn;
            Assert.True(conv < 0.35 * n, $"seed {seed}: convergent belts are {conv / (double)n:P0} of the map");
            // mountains (2026-11-01): it was over 0.7. Glaciers now cut the belts that rise above the snowline down toward it
            // (the buzzsaw) and crags of hard rock stand in plate interiors too, so the highest tenth is less wholly on the
            // belts: measured 79%, 62%, 83% (seeds 1, 2, 3); the belts still stand twice as high as the interiors
            Assert.True(topConv > 0.6 * topN, $"seed {seed}: {topConv / (double)topN:P0} of the highest tenth of the land is on convergent belts");
            Assert.True(hConv > 1.5 * hIn, $"seed {seed}: convergent belts stand {hConv:F0} m, plate interiors {hIn:F0} m");
            // uplift: collision belts and arcs rise; the coarse relief is near a steady state of uplift against erosion
            Assert.True(c.Residual < 0.2f, $"seed {seed}: dh/dt is {c.Residual:P0} of the uplift at the end of deep time");
        }
    }

    [Fact] // rifts sink: divergent belts are the lowest ground and hold the lakes; transform boundaries barely move the crust
    public void RiftBasinsAreLowAndHoldLakes()
    {
        foreach (var seed in Seeds)
        {
            var (w, c) = Built(seed);
            int n = Map.FW * Map.FW;
            double hDiv = 0, hIn = 0; int nDiv = 0, nIn = 0, lakeDiv = 0, lakes = 0;
            for (int i = 0; i < n; i++)
            {
                byte b = c.Bound[Coarse(c, i)];
                if (w.Elev[i] < w.SeaLevel) { lakes++; if (b == Geology.Divergent) lakeDiv++; }
                if (b == Geology.Divergent) { hDiv += w.Elev[i]; nDiv++; }
                else if (b == Geology.Interior) { hIn += w.Elev[i]; nIn++; }
            }
            Assert.True(nDiv > 0.02 * n, $"seed {seed}: {nDiv} rift cells");
            Assert.True(hDiv / nDiv < hIn / nIn, $"seed {seed}: rifts at {hDiv / nDiv:F3}, interiors at {hIn / nIn:F3}");
            // the share of lake in the rifts is at least twice the map's. Geomorphology (2026-10-31): it was three times;
            // erosion on the land surface's own scale delivers far more sediment, which fills some rifts (as the Rhine
            // graben is filled; Tanganyika is not), and foredeep moats hold lakes too. Measured 24-41% lake in rifts
            Assert.True(lakeDiv / (double)nDiv > 2 * lakes / (double)n, $"seed {seed}: rifts are {lakeDiv / (double)nDiv:P0} lake, the map {lakes / (double)n:P0}");
            // the lake share is still Nature.Water (erosion may lower a few more cells below the level). Geomorphology
            // (2026-10-31): rivers no longer cut below the lakes they run into, so it is the quantile itself, -1 cell
            Assert.InRange(lakes / (double)n, Nature.Water - 1.0 / n, Nature.Water + 0.02);

            // transforms do little: only the closing or opening of the plates works the crust, not their sliding, so
            // per unit of relative plate motion a transform belt rises or sinks far less than a convergent one
            double uConv = 0, uTr = 0; int nc = 0, nt = 0;
            for (int i = 0; i < c.CW * c.CW; i++)
            {
                if (c.Rel[i] <= 0f) continue;
                if (c.Bound[i] == Geology.Convergent) { uConv += MathF.Abs(c.Uplift[i]) / c.Rel[i]; nc++; }
                if (c.Bound[i] == Geology.Transform) { uTr += MathF.Abs(c.Uplift[i]) / c.Rel[i]; nt++; }
            }
            if (nt > 0) Assert.True(uTr / nt < 0.5 * uConv / nc, $"seed {seed}: |uplift| per unit relative speed {uTr / nt:E2} on transforms, {uConv / nc:E2} on convergent belts");
        }
    }

    [Fact] // rock: arcs are volcanic, soft sediment fills the basins, and hard rock wears down slower under the same rain
    public void RockFollowsTectonicHistoryAndHardRockErodesLess()
    {
        var (w, c) = Built(2);
        int n = c.CW * c.CW, arc = 0, arcBed = 0, arcVolc = 0, rift = 0, riftSed = 0, rest = 0, restVolc = 0;
        for (int i = 0; i < n; i++)
        {
            if (c.Arc[i] && c.Uplift[i] > 0.1f * K.GEO_AIRY) { arc++; if (c.Bed[i] == Rock.Volcanic) arcBed++; if (c.Surface(i) == Rock.Volcanic) arcVolc++; }
            else if (c.H[i] >= c.Sea) { rest++; if (c.Surface(i) == Rock.Volcanic) restVolc++; }
            if (c.Bound[i] == Geology.Divergent && c.H[i] < c.Sea) { rift++; if (c.Surface(i) == Rock.Sedimentary) riftSed++; }
        }
        // mountains (2026-11-01): the arcs' crags now shed talus, which mantles their lower slopes as loose (sedimentary)
        // cover, so the volcanic rock shows at the surface on 43% of the rising arc (it was over half); it lies beneath
        // more than half of it, and shows there far more often than on the rest of the land
        Assert.True(arc > 0 && arcBed > 0.5 * arc, $"{arcBed} of {arc} rising arc cells are volcanic bedrock");
        Assert.True(arcVolc / (double)arc > 5.0 * restVolc / Math.Max(1, rest), $"{arcVolc} of {arc} rising arc cells are volcanic at the surface, {restVolc} of {rest} elsewhere");
        Assert.True(rift > 0 && riftSed > 0.8 * rift, $"{riftSed} of {rift} rift-lake floors are sediment");
        Assert.Equal(new[] { Rock.Crystalline, Rock.Limestone, Rock.Volcanic, Rock.Sedimentary },
            Enumerable.Range(0, Rock.N).Select(k => (byte)k).OrderBy(k => Rock.Erod[k]).ToArray());

        // the same relief and rain, all crystalline against all sedimentary: the hard rock loses far less
        var v = new World(16, 2);
        Terrain.Relief(v);
        var rain = Terrain.RunoffRain(v);
        int m = Map.FW * Map.FW;
        double Lost(byte kind)
        {
            var h = (float[])v.Elev.Clone();
            var k = new float[m]; Array.Fill(k, Rock.Erod[kind] / K.GEO_K_SED);
            var b = Hydrology.Erode(h, v.SeaLevel, rain, new float[m], K.ERODE_ITERS, kCreep: 0f, kRock: k);
            return b.Eroded;
        }
        double hard = Lost(Rock.Crystalline), soft = Lost(Rock.Sedimentary);
        Assert.True(hard < 0.6 * soft, $"crystalline lost {hard:F3}, sedimentary {soft:F3}");
    }

    [Fact] // soils weather from their rock: volcanic soils are the richest, crystalline the poorest (alluvium aside)
    public void SoilFertilityFollowsParentRock()
    {
        var (w, _) = Built(2);
        var sum = new double[Rock.N]; var cnt = new int[Rock.N];
        for (int i = 0; i < Map.FW * Map.FW; i++)
        {
            if (w.Cell[i] != (byte)Realm.Land || w.Alluvium[i] > 0f) continue;
            sum[w.Rock[i]] += w.Fert[i]; cnt[w.Rock[i]]++;
            Assert.InRange(w.Fert[i], Rock.Fert[w.Rock[i]] - K.FERT_VAR - 1e-4f, Rock.Fert[w.Rock[i]] + K.FERT_VAR + 1e-4f);
        }
        // limestone (2026-11-01) shows only where erosion has stripped a young platform's clastic strata: 63 cells on this
        // small map, so it is checked when there are enough of it
        for (int k = 0; k < Rock.N; k++) if (k != Rock.Limestone) Assert.True(cnt[k] > 100, $"{cnt[k]} {Rock.Names[k]} cells");
        double cr = sum[Rock.Crystalline] / cnt[Rock.Crystalline], vo = sum[Rock.Volcanic] / cnt[Rock.Volcanic], se = sum[Rock.Sedimentary] / cnt[Rock.Sedimentary];
        Assert.True(vo > se && se > cr, $"fertility: volcanic {vo:F2}, sedimentary {se:F2}, crystalline {cr:F2}");
        if (cnt[Rock.Limestone] > 30)
        {
            double li = sum[Rock.Limestone] / cnt[Rock.Limestone];
            Assert.True(li > cr, $"fertility: limestone {li:F2}, crystalline {cr:F2}");
        }
    }

    // ---- geomorphology (2026-10-31): the relief is what uplift and erosion leave, with no cap, no cliffs and no repeats

    /// <summary>The steepest slope at each field cell as the game shows it (m/m): heights at H_RELIEF over 1 - SeaLevel,
    /// GEO_CELL_M a cell, central differences on the torus.</summary>
    static float[] Slopes(World w)
    {
        int FW = Map.FW, n = FW * FW;
        var s = new float[n];
        float k = K.H_RELIEF / (1f - w.SeaLevel) / K.GEO_CELL_M * 0.5f;
        for (int y = 0; y < FW; y++)
            for (int x = 0; x < FW; x++)
            {
                float gx = w.Elev[y * FW + World.WrapI(x + 1, FW)] - w.Elev[y * FW + World.WrapI(x - 1, FW)];
                float gy = w.Elev[World.WrapI(y + 1, FW) * FW + x] - w.Elev[World.WrapI(y - 1, FW) * FW + x];
                s[y * FW + x] = k * MathF.Sqrt(gx * gx + gy * gy);
            }
        return s;
    }

    static float Pct(float[] a, double q) { var b = (float[])a.Clone(); Array.Sort(b); return b[Math.Min(b.Length - 1, (int)(q * b.Length))]; }

    [Fact] // no plateau at a cap: the highest ground is peaks and ridges, not a flat at one height; most land lies low
    public void NoPlateauAtACap()
    {
        // before (a 5 km cap on uplift, GEO_H_MAX): 3-16% of the coarse land stood at the cap, 12-17% of the field
        // grid's land in the top tenth of the relief, and 7-22% of the land above half the relief was flat (< 3°)
        foreach (var seed in Seeds)
        {
            var (w, _) = Built(seed);
            int n = Map.FW * Map.FW;
            var slope = Slopes(w);
            var land = Enumerable.Range(0, n).Where(i => w.Elev[i] >= w.SeaLevel).ToArray();
            float top = land.Max(i => w.Elev[i]);
            float Rel(int i) => (w.Elev[i] - w.SeaLevel) / (top - w.SeaLevel);
            var high = land.Where(i => Rel(i) > 0.5f).ToArray();
            int flatHigh = high.Count(i => slope[i] < MathF.Tan(3f * MathF.PI / 180f));
            Assert.True(high.Length > 0 && flatHigh < 0.03 * high.Length, $"seed {seed}: {flatHigh} of {high.Length} cells above half the relief are flat");
            // no height holds a crowd: the most populous 1% band of the upper half of the relief has under 3% of the land
            var bands = new int[100];
            foreach (var i in land) bands[Math.Min(99, (int)(Rel(i) * 100))]++;
            int crowd = bands.Skip(50).Max();
            Assert.True(crowd < 0.03 * land.Length, $"seed {seed}: {crowd} land cells in one 1% band of the upper relief");
            int top10 = land.Count(i => Rel(i) > 0.9f);
            Assert.True(top10 < 0.02 * land.Length, $"seed {seed}: {top10} cells in the top tenth of the relief");
            // hypsometry: most land is low, the mountains are a minority (the median under 40% of the highest ground)
            float median = land.Select(Rel).OrderBy(v => v).ElementAt(land.Length / 2);
            Assert.True(median < 0.4f, $"seed {seed}: median land at {median:P0} of the relief");
        }
    }

    /// <summary>The steepest drop from each field cell to one of its 8 neighbours (m/m, a lake at its level), as the game
    /// shows it: what Geology.Fail bounds by the rock's critical slope.</summary>
    static float[] Steepest(World w)
    {
        int FW = Map.FW, n = FW * FW;
        var s = new float[n];
        float k = K.H_RELIEF / (1f - w.SeaLevel) / K.GEO_CELL_M;
        for (int y = 0; y < FW; y++)
            for (int x = 0; x < FW; x++)
            {
                int i = y * FW + x; float best = 0f;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        float e = MathF.Max(w.Elev[World.WrapI(y + dy, FW) * FW + World.WrapI(x + dx, FW)], w.SeaLevel);
                        best = MathF.Max(best, (w.Elev[i] - e) / (dx != 0 && dy != 0 ? 1.4142135f : 1f));
                    }
                s[i] = best * k;
            }
        return s;
    }

    static float Deg(float s) => MathF.Atan(s) * 180f / MathF.PI;

    [Fact] // slopes are bounded by each rock's critical slope: soft rock and debris at 30-35°, no rock beyond its own; lowlands are gentler than uplands
    public void SlopeDistributionIsBoundedByRock()
    {
        // before (geomorphology, 2026-10-31): 13-18% of the land (1024 map) and 41% (256) was steeper than 45° as the 3D
        // view drew it; p99 66-77°. Then every rock failed at GEO_SC (31°). Mountains (2026-11-01): each rock at its own
        // critical slope (Rock.Sc), so this bounds each rock by its own; the crags are CragsStandOnHardRock
        foreach (var seed in Seeds)
        {
            var (w, _) = Built(seed);
            int n = Map.FW * Map.FW;
            var slope = Slopes(w); var st = Steepest(w);
            var land = Enumerable.Range(0, n).Where(i => w.Elev[i] >= w.SeaLevel).ToArray();
            var ls = land.Select(i => slope[i]).ToArray();
            float tan35 = MathF.Tan(35f * MathF.PI / 180f);
            var soft = land.Where(i => w.Rock[i] == Rock.Sedimentary).Select(i => st[i]).ToArray();
            Assert.True(Pct(soft, 0.99) < tan35, $"seed {seed}: p99 slope on soft rock {Deg(Pct(soft, 0.99)):F1}°");
            foreach (var i in land)   // a cell's steepest drop is at most its rock's critical slope (+3° of what the fine passes leave)
                Assert.True(Deg(st[i]) < Deg(Rock.Sc[w.Rock[i]]) + 3f || w.Rock[i] == Rock.Sedimentary,
                    $"seed {seed}: {Rock.Names[w.Rock[i]]} cell at {Deg(st[i]):F1}°, its critical slope {Deg(Rock.Sc[w.Rock[i]]):F1}°");
            float scMax = Rock.Sc.Max();
            Assert.True(Deg(st.Max()) < Deg(scMax) + 5f, $"seed {seed}: steepest land {Deg(st.Max()):F1}°");
            Assert.True(Pct(ls, 0.9) > 0.3f * K.GEO_SC, $"seed {seed}: the land is all but flat (p90 {Pct(ls, 0.9):F2})");
            // lowlands (the lowest quarter of the land) are gentler than uplands (the highest quarter)
            var byH = land.OrderBy(i => w.Elev[i]).ToArray();
            float lo = Pct(byH.Take(byH.Length / 4).Select(i => slope[i]).ToArray(), 0.5);
            float hi = Pct(byH.Skip(3 * byH.Length / 4).Select(i => slope[i]).ToArray(), 0.5);
            Assert.True(lo < 0.75f * hi, $"seed {seed}: median slope {lo:F2} in the lowlands, {hi:F2} in the uplands");
        }
    }

    // ---- mountains (2026-11-01): strong rock stands in crags, glaciers cut the land above the snowline, relief follows uplift

    [Fact] // hard crystalline and volcanic rock stands in steep crags and cliffs; soft sediment does not
    public void CragsStandOnHardRock()
    {
        // before: every rock failed at GEO_SC; no land was steeper than 33° (1024 map, seeds 3, 7, 11). Measured after
        // (1024 map): 26-35% of crystalline land is steeper than 40° and 11-18% steeper than 50°; volcanic p99 45°;
        // sedimentary p99 31°
        foreach (var seed in Seeds)
        {
            var (w, _) = Built(seed);
            int n = Map.FW * Map.FW;
            var st = Steepest(w);
            float[] Of(byte k) => Enumerable.Range(0, n).Where(i => w.Elev[i] >= w.SeaLevel && w.Rock[i] == k).Select(i => st[i]).ToArray();
            var cr = Of(Rock.Crystalline); var vo = Of(Rock.Volcanic); var se = Of(Rock.Sedimentary);
            float tan40 = MathF.Tan(40f * MathF.PI / 180f);
            double crSteep = cr.Count(s => s > tan40) / (double)cr.Length, seSteep = se.Count(s => s > tan40) / (double)se.Length;
            Assert.True(crSteep > 0.08, $"seed {seed}: {crSteep:P1} of the crystalline land is steeper than 40°");
            Assert.True(Deg(Pct(cr, 0.99)) > 45f, $"seed {seed}: p99 slope on crystalline rock {Deg(Pct(cr, 0.99)):F1}°");
            Assert.True(seSteep < 0.01, $"seed {seed}: {seSteep:P1} of the sedimentary land is steeper than 40°");
            if (vo.Length > 50) Assert.True(Pct(vo, 0.99) > Pct(se, 0.99) + 0.1f, $"seed {seed}: p99 slope volcanic {Deg(Pct(vo, 0.99)):F1}°, sedimentary {Deg(Pct(se, 0.99)):F1}°");
        }
    }

    [Fact] // glaciers above the snowline (ELA) cut the mountains toward it (the glacial buzzsaw); they form above it
    [BigWorld]
    public void GlaciersCutTheLandAboveTheSnowline()
    {
        // measured (1024 map, latitudes whose ELA is 0.3-3.5 km; seeds 3, 7, 11): land above the ELA 32/16/23% without
        // glaciers, 26/9/18% with them; p99 height above the ELA 2.6/1.8/2.5 km against 2.1/1.3/1.8 km
        int FW = Map.FW, n = FW * FW;
        var ela = new float[FW]; var tSea = new float[FW];
        Geology.Snowline(FW, ela, tSea);
        foreach (var seed in new uint[] { 3, 11 })
        {
            (double above, float p99, Geology.Crust c, World w) Run(bool glaciers)
            {
                var w = new World(16, seed);
                var c = Geology.Form(w, glaciers);
                var rel = new System.Collections.Generic.List<float>();
                for (int i = 0; i < n; i++)
                {
                    float e = ela[i / FW];
                    if (w.Elev[i] < w.SeaLevel || e < 0.3f || e > 3.5f) continue;
                    rel.Add(Metres(w, i) - 1000f * e);
                }
                var a = rel.ToArray();
                return (a.Count(v => v > 0f) / (double)a.Length, Pct(a, 0.99), c, w);
            }
            var (a0, p0, _, _) = Run(false);
            var (a1, p1, c1, w1) = Run(true);
            Assert.True(a1 < 0.85 * a0, $"seed {seed}: land above the ELA {a0:P1} without glaciers, {a1:P1} with them");
            Assert.True(p1 < p0 - 200f, $"seed {seed}: p99 height above the ELA {p0:F0} m without glaciers, {p1:F0} m with them");
            // the glaciers of the last step lie above the snowline, or not far below it (their tongues melt within ~1 km)
            int ice = 0, iceHigh = 0;
            for (int i = 0; i < n; i++)
            {
                if (c1.FineIce[i] <= 0f || w1.Elev[i] < w1.SeaLevel) continue;
                ice++; if (Metres(w1, i) > 1000f * ela[i / FW] - 1000f) iceHigh++;
            }
            Assert.True(ice > 0.02 * n && iceHigh > 0.95 * ice, $"seed {seed}: {ice} glacier cells, {iceHigh} of them above ELA - 1 km");
        }
    }

    [Fact] // relief grows with the rate of rock uplift: belts that rise faster stand higher (until crags and glaciers bound them)
    public void ReliefScalesWithUplift()
    {
        foreach (var seed in Seeds)
        {
            var (_, c) = Built(seed);
            // coarse land cells by their rock uplift (the plates'): terciles of the rising ones, and the plate interiors
            var up = Enumerable.Range(0, c.CW * c.CW).Where(i => c.H[i] >= c.Sea && c.Uplift[i] > 0.02f * K.GEO_AIRY).OrderBy(i => c.Uplift[i]).ToArray();
            double H(System.Collections.Generic.IEnumerable<int> s) => s.Average(i => c.H[i] - c.Sea);
            int t = up.Length / 3;
            double lo = H(up.Take(t)), mid = H(up.Skip(t).Take(t)), hi = H(up.Skip(2 * t));
            double still = H(Enumerable.Range(0, c.CW * c.CW).Where(i => c.H[i] >= c.Sea && MathF.Abs(c.Uplift[i]) < 0.005f * K.GEO_AIRY));
            Assert.True(still < lo && lo < mid && mid < hi, $"seed {seed}: mean height {still:F2} km where nothing rises, {lo:F2} / {mid:F2} / {hi:F2} km by tercile of uplift");
            Assert.True(hi > 1.5 * lo, $"seed {seed}: the fastest-rising third stands {hi:F2} km, the slowest {lo:F2} km");
        }
    }

    [Fact] // the relief is not periodic: no shape repeats across the map (a repeat in the 3D view is its torus tiling)
    public void ReliefIsNotPeriodic()
    {
        foreach (var seed in Seeds)
        {
            var (w, _) = Built(seed);
            int FW = Map.FW, n = FW * FW;
            // the land's height anomaly, whole and high-passed (each cell against its 4 neighbours)
            float E(int xx, int yy) => MathF.Max(w.Elev[World.WrapI(yy, FW) * FW + World.WrapI(xx, FW)], w.SeaLevel);
            var a = new double[n]; var hp = new double[n];
            double mean = 0; for (int i = 0; i < n; i++) mean += MathF.Max(w.Elev[i], w.SeaLevel); mean /= n;
            for (int y = 0; y < FW; y++)
                for (int x = 0; x < FW; x++)
                {
                    a[y * FW + x] = E(x, y) - mean;
                    hp[y * FW + x] = E(x, y) - 0.25 * (E(x - 1, y) + E(x + 1, y) + E(x, y - 1) + E(x, y + 1));
                }
            static double Ac(double[] v, int FW, int dx, int dy)
            {
                double s = 0, s0 = 0;
                for (int y = 0; y < FW; y++)
                    for (int x = 0; x < FW; x++)
                    {
                        double p = v[y * FW + x];
                        s += p * v[World.WrapI(y + dy, FW) * FW + World.WrapI(x + dx, FW)]; s0 += p * p;
                    }
                return s / s0;
            }
            // every shift of at least a quarter of the map (a repeat at period P < FW would correlate near 1 at shift P)
            double worst = 0, worstHp = 0; (int, int) at = (0, 0);
            for (int dy = 0; dy < FW; dy++)
                for (int dx = 0; dx < FW; dx++)
                {
                    int ex = Math.Min(dx, FW - dx), ey = Math.Min(dy, FW - dy);
                    if (ex * ex + ey * ey < FW * FW / 16) continue;
                    double r = Ac(a, FW, dx, dy);
                    if (r > worst) { worst = r; at = (dx, dy); }
                    worstHp = Math.Max(worstHp, Ac(hp, FW, dx, dy));
                }
            Assert.True(worst < 0.5, $"seed {seed}: the relief correlates {worst:F2} with itself shifted by {at}");
            Assert.True(worstHp < 0.2, $"seed {seed}: its fine detail correlates {worstHp:F2} with itself shifted");
        }
    }

    [Fact] // valleys: V-shaped in the uplands (steep sides, no floor), wide with flat floors in the lowlands
    [BigWorld]
    public void ValleysAreVShapedInUplandsAndWidenDownstream()
    {
        var w = new World(16, 3);
        Terrain.Build(w);
        int FW = Map.FW, n = FW * FW;
        float top = 0; for (int i = 0; i < n; i++) top = MathF.Max(top, w.Elev[i]);
        float m = K.H_RELIEF / (1f - w.SeaLevel);
        // around channel cells (gathering the rain of 32 cells or more): the share of the 9 x 9 cells around within 30 m
        // of the channel's height, in the uplands (over a quarter of the relief) and the lowlands (under a tenth)
        double Floor(float lo, float hi)
        {
            double sum = 0; int cnt = 0;
            for (int y = 0; y < FW; y++)
                for (int x = 0; x < FW; x++)
                {
                    int i = y * FW + x;
                    float r = (w.Elev[i] - w.SeaLevel) / (top - w.SeaLevel);
                    if (w.Elev[i] < w.SeaLevel || w.Flow[i] < 32f || r < lo || r >= hi) continue;
                    int near = 0;
                    for (int dy = -4; dy <= 4; dy++)
                        for (int dx = -4; dx <= 4; dx++)
                            if (MathF.Abs(w.Elev[World.WrapI(y + dy, FW) * FW + World.WrapI(x + dx, FW)] - w.Elev[i]) * m < 30f) near++;
                    sum += near / 81.0; cnt++;
                }
            Assert.True(cnt > 100, $"{cnt} channel cells between {lo:P0} and {hi:P0} of the relief");
            return sum / cnt;
        }
        double up = Floor(0.25f, 1.01f), low = Floor(0f, 0.1f);
        Assert.True(up < 0.3, $"upland valleys: {up:P0} of the cells around a channel lie within 30 m of it");
        Assert.True(low > 2 * up, $"lowland valleys: {low:P0} within 30 m of the channel, uplands {up:P0}");
    }

    [Fact] // the land is a function of the terrain seed alone: the same seed makes the same land, another seed another
    public void GenesisIsDeterministic()
    {
        var a = new World(16, 5); Terrain.Build(a);
        var b = new World(16, 5); Terrain.Build(b);
        var d = new World(16, 9, terrainSeed: 5); Terrain.Build(d);   // another world on the same land (a prehistory branch)
        var e = new World(16, 6); Terrain.Build(e);
        Assert.Equal(a.Elev, b.Elev); Assert.Equal(a.Rock, b.Rock); Assert.Equal(a.Flow, b.Flow); Assert.Equal(a.SeaLevel, b.SeaLevel);
        Assert.Equal(a.Elev, d.Elev); Assert.Equal(a.Rock, d.Rock);
        Assert.NotEqual(a.Elev, e.Elev);
    }

    [Fact] // world generation stays within its budget: ~5 s at the default map (1024), ~15 s at the largest (2048)
    public void GenerationFitsItsTimeBudget()
    {
        int side = Map.W;
        try
        {
            foreach (var (map, budget) in new[] { (1024, 5.0), (2048, 15.0) })
            {
                Map.Set(map);
                var w = new World(16, 3);
                var sw = Stopwatch.StartNew();
                Terrain.Build(w);
                double s = sw.Elapsed.TotalSeconds;
                Assert.True(s < budget, $"map {map}: generation took {s:F1} s (budget {budget} s)");
            }
        }
        finally { Map.Set(side); }
    }
}
