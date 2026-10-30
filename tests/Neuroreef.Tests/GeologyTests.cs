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
            // the share of lake in the rifts is at least three times the map's
            Assert.True(lakeDiv / (double)nDiv > 3 * lakes / (double)n, $"seed {seed}: rifts are {lakeDiv / (double)nDiv:P0} lake, the map {lakes / (double)n:P0}");
            // the lake share is still Nature.Water (erosion may lower a few more cells below the level)
            Assert.InRange(lakes / (double)n, Nature.Water, Nature.Water + 0.02);

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
