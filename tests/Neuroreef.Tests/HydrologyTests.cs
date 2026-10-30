// Hydrology (Revision Q): rain routed over the relief, rivers where it gathers, stream-power valleys, alluvium where
// running water drops its load, and floods that wet the floodplain and bring it the river's nutrients.

using System;
using Xunit;

namespace Neuroreef.Tests;

public class HydrologyTests
{
    /// <summary>The relief of world seed before and after the generation's erosion, with its rain.</summary>
    static (World w, float[] h0, float[] rain, Hydrology.Budget b) Worked(uint seed)
    {
        var w = new World(16, seed);
        Terrain.Relief(w);
        var rain = Terrain.RunoffRain(w);
        var h0 = (float[])w.Elev.Clone();
        var b = Hydrology.Erode(w.Elev, w.SeaLevel, rain, w.Alluvium, K.ERODE_ITERS);
        return (w, h0, rain, b);
    }

    [Fact] // flow accumulation: every land cell drains to a lake, and what reaches the lakes is exactly the rain that fell
    public void FlowAccumulationConservesRain()
    {
        var w = new World(16, 3);
        Terrain.Relief(w);
        var rain = Terrain.RunoffRain(w);
        int n = Map.FW * Map.FW;
        var net = new Hydrology.Net(n);
        Hydrology.Route(w.Elev, w.SeaLevel, rain, net);
        double fell = 0, arrived = 0;
        var inflow = new double[n];
        for (int i = 0; i < n; i++)
        {
            fell += rain[i];
            if (net.Recv[i] < 0) arrived += net.Flow[i]; else inflow[net.Recv[i]] += net.Flow[i];
        }
        Assert.True(fell > 0.5 * n, $"rain {fell}");
        Assert.Equal(1.0, arrived / fell, 4);
        // each cell's flow is its own rain plus what drains into it; outlets are lakes only
        for (int i = 0; i < n; i++)
        {
            Assert.Equal(rain[i] + inflow[i], net.Flow[i], Math.Max(1e-3, 1e-5 * net.Flow[i]));
            if (net.Recv[i] < 0) Assert.True(w.Elev[i] < w.SeaLevel, $"cell {i} is an outlet above the lakes");
            else Assert.True(net.Fill[i] >= net.Fill[net.Recv[i]], $"cell {i} drains uphill on the filled surface");
        }
        // no cycles: the topological order puts every receiver before the cells draining into it
        var pos = new int[n];
        for (int k = 0; k < n; k++) pos[net.Order[k]] = k;
        for (int i = 0; i < n; i++) if (net.Recv[i] >= 0) Assert.True(pos[net.Recv[i]] < pos[i]);
    }

    [Fact] // rivers are the cells where most rain gathers; they are drinkable water and run on, river to river, into a lake
    public void RiversFormAlongAccumulatedFlowAndReachWater()
    {
        var w = new World(16, 7);
        Terrain.Build(w);
        var (v, _, rain, _) = Worked(7);                         // the same relief, worked the same way
        int n = Map.FW * Map.FW;
        var net = new Hydrology.Net(n);
        Hydrology.Route(v.Elev, v.SeaLevel, rain, net);
        for (int i = 0; i < n; i++) Assert.Equal(w.Flow[i], net.Flow[i]);
        float thr = K.RIVER_ACC * (n / 65536f);
        int rivers = 0;
        for (int i = 0; i < n; i++)
        {
            bool river = w.Elev[i] >= w.SeaLevel && w.Cell[i] != (byte)Realm.Land;
            Assert.Equal(w.Elev[i] >= w.SeaLevel && w.Flow[i] > thr, river);
            if (!river) continue;
            rivers++;
            Assert.Equal(1, w.Wet[i]); Assert.Equal(0f, w.DistDrink[i]);
            Assert.True(w.Depth[i] > 0f && w.Depth[i] <= K.SHALLOW, $"river depth {w.Depth[i]}");
            int c = i, steps = 0;
            while (net.Recv[c] >= 0 && steps++ < n)
            {
                int r = net.Recv[c];
                Assert.True(w.Cell[r] != (byte)Realm.Land, $"river cell {c} runs onto land at {r}");   // flow only grows downstream
                c = r;
            }
            Assert.True(w.Elev[c] < w.SeaLevel, "a river ends somewhere but a lake");
        }
        Assert.InRange(rivers / (double)n, 0.003, 0.03);
        // bigger rivers are deeper (depth ~ discharge^0.4)
        int small = -1, big = -1;
        for (int i = 0; i < n; i++)
        {
            if (w.Elev[i] < w.SeaLevel || w.Cell[i] == (byte)Realm.Land) continue;
            if (small < 0 || w.Flow[i] < w.Flow[small]) small = i;
            if (big < 0 || w.Flow[i] > w.Flow[big]) big = i;
        }
        Assert.True(w.Depth[big] > w.Depth[small]);
    }

    [Fact] // stream power: channels on slopes are cut down more than twice as far as hillsides; what is cut is laid down or reaches a lake
    public void ErosionLowersChannelsAndConservesSediment()
    {
        var (w, h0, rain, b) = Worked(1);
        int n = Map.FW * Map.FW;
        var raw = new Hydrology.Net(n);
        Hydrology.Route(h0, w.SeaLevel, rain, raw);
        double chan = 0, hill = 0, lost = 0; int nc = 0, nh = 0;
        for (int i = 0; i < n; i++)
        {
            lost += h0[i] - w.Elev[i];
            if (h0[i] < w.SeaLevel || raw.Fill[i] > h0[i]) continue;   // lakes and closed basins
            if (raw.Flow[i] > K.RIVER_ACC) { chan += h0[i] - w.Elev[i]; nc++; }
            else if (raw.Flow[i] < 4f) { hill += h0[i] - w.Elev[i]; nh++; }
        }
        chan /= nc; hill /= nh;
        Assert.True(nc > 100 && nh > 1000, $"{nc} channel and {nh} hillside cells");
        Assert.True(chan > 0f && chan > 2 * hill, $"channels lowered {chan * K.H_RELIEF:F1} m, hillsides {hill * K.H_RELIEF:F1} m");
        // mass: the land lost exactly what went into the lakes (stream load and creep); the rest moved and stayed
        Assert.True(b.Eroded > 0 && b.Deposited > 0 && b.Deposited < b.Eroded);
        Assert.Equal(1.0, lost / b.ToLakes, 2);
    }

    [Fact] // alluvium lies on low, flat ground along the rivers, and makes its soil more fertile
    public void SedimentRaisesFloodplainFertility()
    {
        var w = new World(16, 2);
        Terrain.Build(w);
        int n = Map.FW * Map.FW;
        double fp = 0, fpA = 0, rest = 0, restA = 0; int nf = 0, nr = 0;
        for (int i = 0; i < n; i++)
        {
            if (w.Cell[i] != (byte)Realm.Land) continue;
            if (!float.IsPositiveInfinity(w.FloodWet[i])) { fp += w.Fert[i]; fpA += w.Alluvium[i]; nf++; }
            else { rest += w.Fert[i]; restA += w.Alluvium[i]; nr++; }
        }
        fp /= nf; rest /= nr; fpA /= nf; restA /= nr;
        Assert.True(nf > 200, $"{nf} floodplain cells");
        Assert.True(fpA > 2 * restA, $"alluvium {fpA * K.H_RELIEF:F1} m on floodplains, {restA * K.H_RELIEF:F1} m elsewhere");
        Assert.True(fp > rest + 0.1, $"fertility {fp:F3} on floodplains, {rest:F3} elsewhere");
        // fertility follows alluvium cell by cell: the same soil with alluvium on it is richer
        for (int i = 0; i < n; i++)
            if (w.Cell[i] == (byte)Realm.Land && w.Alluvium[i] > 3 * K.ALLUV_D) Assert.True(w.Fert[i] >= 0.1f + 0.9f * K.ALLUV_FERT, $"cell {i}: fertility {w.Fert[i]}");
    }

    [Fact] // floods (the water cycle): when a wet year's runoff raises a river's discharge far enough over its mean, it tops its
           // banks; the floodplain is wet, drinkable, soaked (from the river's water) and gets the river's nutrient (taken from
           // the river); in a drought nothing floods
    public void WetYearsFloodTheFloodplain()
    {
        var w = new World(16, 7);
        Terrain.Build(w);
        WaterCycle.Init(w);
        int n = Map.FW * Map.FW, perYear = Nature.SeasonLength / K.WC_EVERY;
        void Advance(int updates)
        {
            for (int u = 0; u < updates; u++)
            {
                w.Step += K.WC_EVERY;
                if (Climate.On) for (int y = 0; y < Map.FW; y++) Climate.TempRow(w, y);
                WaterCycle.Update(w);
            }
        }
        w.RainAnom = -0.6f;
        int most = 0;
        for (int u = 0; u < perYear; u++) { Advance(1); most = Math.Max(most, Ponds.Flooded(w)); }
        Assert.Equal(0, most);
        for (int i = 0; i < n; i++) w.Nutrient[i] = w.Cell[i] == (byte)Realm.Land ? 0.2f : 1.2f;
        double before = 0; for (int i = 0; i < n; i++) before += w.Nutrient[i];
        w.RainAnom = 0.6f;
        int fl = 0;
        for (int u = 0; u < perYear && fl <= 50; u++) { Advance(1); fl = Ponds.Flooded(w); }
        Assert.True(fl > 50, $"{fl} cells flooded in a wet year");
        double after = 0;
        for (int i = 0; i < n; i++)
        {
            after += w.Nutrient[i];
            if (w.Wet[i] != 3) continue;
            int d = w.FloodFrom[i];
            Assert.True(w.Cell[i] == (byte)Realm.Land && d >= 0);
            Assert.True(w.DischargeMean[d] > 0f && w.Cell[d] != (byte)Realm.Land);
            Assert.Equal(0f, w.DistDrink[i]);
            Assert.True(w.Nutrient[i] > 0.2f, $"flooded cell {i} nutrient {w.Nutrient[i]}");
            Assert.Equal(1f, w.Moisture[i], 4);   // a flooded cell's soil is soaked
        }
        Assert.Equal(before, after, 1);
    }
}
