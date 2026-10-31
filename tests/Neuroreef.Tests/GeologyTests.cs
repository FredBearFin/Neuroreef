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
            Assert.True(topConv > 0.7 * topN, $"seed {seed}: {topConv / (double)topN:P0} of the highest tenth of the land is on convergent belts");
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
        int n = c.CW * c.CW, arc = 0, arcVolc = 0, rift = 0, riftSed = 0;
        for (int i = 0; i < n; i++)
        {
            if (c.Arc[i] && c.Uplift[i] > 0.1f * K.GEO_AIRY) { arc++; if (c.Surface(i) == Rock.Volcanic) arcVolc++; }
            if (c.Bound[i] == Geology.Divergent && c.H[i] < c.Sea) { rift++; if (c.Surface(i) == Rock.Sedimentary) riftSed++; }
        }
        Assert.True(arc > 0 && arcVolc > 0.5 * arc, $"{arcVolc} of {arc} rising arc cells are volcanic");
        Assert.True(rift > 0 && riftSed > 0.8 * rift, $"{riftSed} of {rift} rift-lake floors are sediment");
        Assert.Equal(new[] { Rock.Crystalline, Rock.Volcanic, Rock.Sedimentary },
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
        for (int k = 0; k < Rock.N; k++) Assert.True(cnt[k] > 100, $"{cnt[k]} {Rock.Names[k]} cells");
        double cr = sum[Rock.Crystalline] / cnt[Rock.Crystalline], vo = sum[Rock.Volcanic] / cnt[Rock.Volcanic], se = sum[Rock.Sedimentary] / cnt[Rock.Sedimentary];
        Assert.True(vo > se && se > cr, $"fertility: volcanic {vo:F2}, sedimentary {se:F2}, crystalline {cr:F2}");
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

    [Fact] // slopes are bounded by the critical slope of threshold hillslopes (30-35°): no cliffs; lowlands are gentler than uplands
    public void SlopeDistributionIsBounded()
    {
        // before: 13-18% of the land (1024 map) and 41% (256) was steeper than 45° as the 3D view drew it; p99 66-77°
        foreach (var seed in Seeds)
        {
            var (w, _) = Built(seed);
            int n = Map.FW * Map.FW;
            var slope = Slopes(w);
            var land = Enumerable.Range(0, n).Where(i => w.Elev[i] >= w.SeaLevel).ToArray();
            var ls = land.Select(i => slope[i]).ToArray();
            float tan35 = MathF.Tan(35f * MathF.PI / 180f), tan40 = MathF.Tan(40f * MathF.PI / 180f);
            Assert.True(Pct(ls, 0.99) < tan35, $"seed {seed}: p99 land slope {Pct(ls, 0.99):F2} m/m");
            Assert.True(ls.Max() < tan40, $"seed {seed}: steepest land slope {ls.Max():F2} m/m");
            Assert.True(Pct(ls, 0.9) > 0.3f * K.GEO_SC, $"seed {seed}: the land is all but flat (p90 {Pct(ls, 0.9):F2})");
            // lowlands (the lowest quarter of the land) are gentler than uplands (the highest quarter)
            var byH = land.OrderBy(i => w.Elev[i]).ToArray();
            float lo = Pct(byH.Take(byH.Length / 4).Select(i => slope[i]).ToArray(), 0.5);
            float hi = Pct(byH.Skip(3 * byH.Length / 4).Select(i => slope[i]).ToArray(), 0.5);
            Assert.True(lo < 0.75f * hi, $"seed {seed}: median slope {lo:F2} in the lowlands, {hi:F2} in the uplands");
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
