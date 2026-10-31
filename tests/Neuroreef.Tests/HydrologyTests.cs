// Hydrology (Revision Q): rain routed over the relief, rivers where it gathers, stream-power valleys, alluvium where
// running water drops its load, and floods that wet the floodplain and bring it the river's nutrients.

using System;
using Xunit;

namespace Neuroreef.Tests;

[BigWorld]
public class HydrologyTests
{
    /// <summary>The relief of world seed before and after the generation's erosion, with its rain.</summary>
    static (World w, float[] h0, float[] rain, Hydrology.Budget b) Worked(uint seed)
    {
        var w = new World(16, seed);
        Terrain.Relief(w);
        var rain = Terrain.RunoffRain(w);
        var h0 = (float[])w.Elev.Clone();
        // Revision R: the relief is the geological genesis, and its rock sets how fast each cell cuts, as in Terrain.Build
        var b = Hydrology.Erode(w.Elev, w.SeaLevel, rain, w.Alluvium, K.ERODE_ITERS, kRock: Geology.Erodibility(w));
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

    // 2026-10-31: rivers are no longer the cells over a fixed catchment (RIVER_ACC) but channels: they start where the
    // discharge running in and the slope pass the channel-initiation threshold (Q S^2 >= CHANNEL_C; Montgomery & Dietrich
    // 1992) and run on, cell by cell, to their lake or the sea
    static bool Channel(World w, int i) => w.Cell[i] != (byte)Realm.Land && w.LakeOf[i] < 0 && w.FloodFrom[i] == i;

    [Fact] // rivers run continuously from their heads to a lake or the sea; they are drinkable, and their discharge grows downstream
    public void RiversRunContinuouslyToALakeOrTheSea()
    {
        var w = new World(16, 7);
        Terrain.Build(w);
        int n = Map.FW * Map.FW, channels = 0, heads = 0, toSea = 0, mouths = 0;
        var fed = new bool[n];
        for (int i = 0; i < n; i++) if (w.Recv[i] >= 0 && Channel(w, i)) fed[w.Recv[i]] = true;
        for (int i = 0; i < n; i++)
        {
            if (w.Cell[i] != (byte)Realm.Land && w.LakeOf[i] < 0)
            {
                // every river cell is drinkable and wadeable, and belongs to a channel's cross-section
                Assert.Equal(1, w.Wet[i]); Assert.Equal(0f, w.DistDrink[i]);
                Assert.True(w.Depth[i] > 0f && w.Depth[i] <= K.SHALLOW, $"river depth {w.Depth[i]}");
                Assert.True(Channel(w, w.FloodFrom[i]), $"river cell {i} belongs to no channel");
            }
            if (!Channel(w, i)) continue;
            channels++;
            if (!fed[i]) heads++;
            int c = i, steps = 0;
            while (w.LakeOf[w.Recv[c]] < 0)
            {
                int r = w.Recv[c];
                Assert.True(Channel(w, r), $"river cell {c} runs onto {(w.Cell[r] == (byte)Realm.Land ? "land" : "a river bank")} at {r}");
                Assert.True(w.Flow[r] >= w.Flow[c], $"discharge falls downstream at {c}: {w.Flow[c]} -> {w.Flow[r]}");
                c = r;
                Assert.True(++steps < n, "a river runs in a circle");
            }
            if (c == i) { mouths++; if (w.LakeExit[w.LakeOf[w.Recv[c]]] < 0) toSea++; }
        }
        // a network, not a few trunks: ~2% of the map, tens of heads, and most mouths at the sea (measured 2.1%, 52 heads,
        // 41 mouths of which 34 at the sea; before, 0.53%, 11 heads, rivers only where 900 cells of rain gathered)
        Assert.InRange(channels / (double)n, 0.01, 0.06);
        Assert.True(heads > 30, $"{heads} channel heads");
        Assert.True(toSea > mouths / 2, $"{toSea} of {mouths} mouths at the sea");
        // channel initiation: no cell that the water running in from above and its slope put past the threshold is left
        // without a channel (Q S^2 >= CHANNEL_C, or Q >= RIVER_ACC, with Q at least CHANNEL_A_MIN)
        float scale = n / 65536f;
        var qin = new float[n];
        for (int i = 0; i < n; i++) if (w.Recv[i] >= 0 && w.LakeOf[i] < 0) qin[w.Recv[i]] += w.Flow[i];
        for (int i = 0; i < n; i++)
        {
            int r = w.Recv[i];
            if (w.LakeOf[i] >= 0 || r < 0 || Channel(w, i)) continue;
            float q = qin[i] * 0.999f;
            float down = w.LakeOf[r] >= 0 ? w.Elev[r] + w.Depth[r] : w.Elev[r];
            float dist = (i % Map.FW != r % Map.FW && i / Map.FW != r / Map.FW) ? 1.4142135f : 1f;
            float s = MathF.Max(0f, w.Elev[i] - down) / dist;
            Assert.False(q >= K.CHANNEL_A_MIN * scale && (q >= K.RIVER_ACC * scale || q * s * s >= K.CHANNEL_C),
                $"cell {i}: inflow {q:F0} and slope {s:F4} pass the channel threshold, but it is no river");
        }
    }

    [Fact] // downstream hydraulic geometry: depth ~ Q^0.4 up to wadeable, width ~ Q^0.5: the big rivers are several cells wide
    public void RiversFollowHydraulicGeometry()
    {
        var w = new World(16, 3);
        Terrain.Build(w);
        int n = Map.FW * Map.FW;
        float q1 = K.RIVER_Q1 * (n / 65536f);
        var section = new int[n];
        for (int i = 0; i < n; i++) if (w.Cell[i] != (byte)Realm.Land && w.LakeOf[i] < 0) section[w.FloodFrom[i]]++;
        double smallW = 0, bigW = 0; int nSmall = 0, nBig = 0, widest = 0;
        for (int i = 0; i < n; i++)
        {
            if (!Channel(w, i)) continue;
            float qr = w.Flow[i] / q1;
            float d = K.SHALLOW * MathF.Min(1f, K.RIVER_D * MathK.Pow(qr, K.RIVER_DEPTH_EXP));
            Assert.Equal(d, w.Depth[i], 1e-6f);                                   // depth ~ Q^0.4 (Leopold & Maddock 1953)
            if (qr < 1f) { smallW += section[i]; nSmall++; Assert.Equal(1, section[i]); }   // under RIVER_Q1: one cell
            else if (qr >= 9f) { bigW += section[i]; nBig++; }                     // 3 cells wide or more by width ~ Q^0.5
            widest = Math.Max(widest, section[i]);
        }
        // the cells beside a channel are shallower than it (a cross-section deepest at the channel)
        for (int i = 0; i < n; i++)
            if (w.Cell[i] != (byte)Realm.Land && w.LakeOf[i] < 0 && !Channel(w, i)) Assert.True(w.Depth[i] <= w.Depth[w.FloodFrom[i]], $"bank {i}");
        Assert.True(nBig > 20, $"{nBig} channel cells of rivers 3 or more cells wide");
        // a channel cell's own section counts each bank cell once (neighbouring channel cells share their banks), so the
        // big rivers' mean section is less than their width; measured 3.9 cells against 1 below RIVER_Q1, and 9 at the widest
        Assert.True(bigW / nBig > 2.0, $"rivers of Q >= 9 RIVER_Q1 have sections of {bigW / nBig:F2} cells");
        Assert.True(widest >= 5, $"the widest section is {widest} cells");
    }

    /// <summary>Spill levels by an independent Priority-Flood (no epsilon) from the sea: the lowest level at which water
    /// standing on each cell would run off to the sea.</summary>
    static float[] SpillLevels(World w)
    {
        int FW = Map.FW, n = FW * FW;
        var fill = new float[n]; var seen = new bool[n];
        var pq = new System.Collections.Generic.PriorityQueue<int, float>();
        for (int i = 0; i < n; i++)
            if (w.LakeOf[i] >= 0 && w.LakeExit[w.LakeOf[i]] < 0) { seen[i] = true; fill[i] = w.SeaLevel; pq.Enqueue(i, fill[i]); }
        while (pq.TryDequeue(out int c, out float z))
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int j = World.WrapI(c / FW + dy, FW) * FW + World.WrapI(c % FW + dx, FW);
                    if (seen[j]) continue;
                    seen[j] = true; fill[j] = MathF.Max(w.Elev[j], z); pq.Enqueue(j, fill[j]);
                }
        return fill;
    }

    [Fact] // no valley drowned beyond its basin's spill point: the sea stands at sea level in the low ground the relief was
           // graded to; every lake stands flat at its own level, no higher than where it would spill, and never above dry ground beside it
    public void LakesStandAtOrBelowTheirSpillPoints()
    {
        foreach (uint seed in new uint[] { 3, 7, 11 })
        {
            var w = new World(16, seed);
            Terrain.Build(w);
            int n = Map.FW * Map.FW, sea = 0, lakes = 0;
            var spill = SpillLevels(w);
            var lvl = new float[w.LakeCount]; Array.Fill(lvl, float.NaN);
            for (int i = 0; i < n; i++)
            {
                int l = w.LakeOf[i];
                if (l < 0) continue;
                Assert.True(w.Cell[i] != (byte)Realm.Land);
                float s = w.Elev[i] + w.Depth[i];                              // the water's surface
                if (w.LakeExit[l] < 0) { sea++; Assert.True(w.Elev[i] < w.SeaLevel); Assert.Equal(w.SeaLevel, s, 1e-5f); continue; }
                lakes++;
                if (float.IsNaN(lvl[l])) lvl[l] = s; else Assert.Equal(lvl[l], s, 1e-5f);   // flat
                Assert.True(s <= spill[i] + 1e-5f, $"seed {seed}: lake {l} stands {(s - spill[i]) * K.H_RELIEF:F1} m above its spill point");
                // a full lake is at its spill point, a terminal one below it by its sill
                float sillElev = w.LakeSill[l] / (K.H_RELIEF * 1000f / (1f - w.SeaLevel));
                Assert.Equal(spill[i], s + sillElev, 2e-4f);
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int j = World.WrapI(i / Map.FW + dy, Map.FW) * Map.FW + World.WrapI(i % Map.FW + dx, Map.FW);
                        if (w.LakeOf[j] != l) Assert.True(w.Elev[j] >= s - 1e-3f, $"seed {seed}: lake {l} ({w.LakeArea[l]} cells, sill {w.LakeSill[l]:F0} mm) stands at {s:F5} above the ground beside it at {j}: {w.Elev[j]:F5} (cell {w.Cell[j]}, body {w.LakeOf[j]}, spill {spill[j]:F5}, here {w.Elev[i]:F5})");
                    }
            }
            // the sea is the low ground below sea level (the Nature.Water quantile): the share of the map it was
            int below = 0; for (int i = 0; i < n; i++) if (w.Elev[i] < w.SeaLevel) below++;
            Assert.Equal(below, sea);
            Assert.InRange(sea / (double)n, Nature.Water - 0.01, Nature.Water + 0.02);
            // lakes above it are what the closed basins hold (measured 0.2-2.2% of the map on seeds 3, 7, 11)
            Assert.InRange(lakes / (double)n, 0.0, 0.05);
        }
    }

    [Fact] // a closed basin holds the lake its water balance allows: a humid one fills to its sill and spills down to the sea,
           // a dry one keeps a smaller terminal lake that spills nothing
    public void ClosedBasinLakesFollowTheirWaterBalance()
    {
        (float area, float sill, int down, float outflow) Bowl(float wet)
        {
            var w = new World(16, 1);
            int FW = Map.FW, n = FW * FW;
            // a sea along the bottom rows; above it land rising away from the sea, with a round hollow in its slope
            for (int y = 0; y < FW; y++)
                for (int x = 0; x < FW; x++)
                {
                    float dy = MathF.Min(y, FW - y) / (float)FW;            // 0 at the sea, 0.5 farthest from it
                    float rx = (x - FW / 2) / (FW * 0.06f), ry = (y - FW / 4) / (FW * 0.06f), r2 = rx * rx + ry * ry;
                    w.Elev[y * FW + x] = y < 4 ? 0.02f : 0.1f + 0.8f * dy - (r2 < 1f ? 0.15f * (1f - r2) : 0f) + 0.0001f * ((x * 7 + y * 13) % 5);
                }
            w.SeaLevel = 0.05f;
            var rain = new float[n];
            for (int i = 0; i < n; i++) rain[i] = w.Elev[i] >= w.SeaLevel ? wet : 0f;
            Hydrology.Waters(w, rain);
            int lake = w.LakeOf[(FW / 4) * FW + FW / 2];
            Assert.True(lake >= 0 && w.LakeExit[lake] >= 0, $"the hollow holds a lake at {wet} x the mean rain");
            return (w.LakeArea[lake], w.LakeSill[lake], w.LakeDown[lake], w.Flow[w.LakeExit[lake]]);
        }
        var humid = Bowl(3f);     // 2,400 mm a year
        var arid = Bowl(0.3f);    // 240 mm a year: a semi-arid basin
        Assert.Equal(0f, humid.sill);                                   // full to its spill point
        Assert.Equal(0, humid.down);                                    // it spills into the sea (body 0)
        Assert.True(humid.outflow > 1f, $"its outflow carries {humid.outflow:F1} cells of rain");
        Assert.True(arid.sill > 0f && arid.area < 0.8f * humid.area, $"arid lake {arid.area} cells, {arid.sill:F0} mm below its sill; humid {humid.area}");
        Assert.True(arid.outflow < 0.5f, $"a terminal lake spills nothing: {arid.outflow:F2} below its exit");
    }

    [Fact] // stream power: channels are cut down more than twice as far as hillsides; what is cut is laid down or reaches a lake
    public void ErosionLowersChannelsAndConservesSediment()
    {
        // Revision R: on a relief already graded by deep time (Geology) the fine passes are transport-limited: the slopes
        // send down more than the valleys carry, and the valley floors fill (alluvial rivers). The incision itself is
        // tested alone, with no deposition and no creep: pure detachment-limited stream power on the same relief
        var w = new World(16, 1);
        Terrain.Relief(w);
        var rain = Terrain.RunoffRain(w);
        var h0 = (float[])w.Elev.Clone();
        int n = Map.FW * Map.FW;
        var raw = new Hydrology.Net(n);
        Hydrology.Route(h0, w.SeaLevel, rain, raw);
        var cutOnly = (float[])h0.Clone();
        Hydrology.Erode(cutOnly, w.SeaLevel, rain, new float[n], K.ERODE_ITERS, drop: 0f, kCreep: 0f, kRock: Geology.Erodibility(w));
        double chan = 0, hill = 0; int nc = 0, nh = 0;
        for (int i = 0; i < n; i++)
        {
            if (h0[i] < w.SeaLevel || raw.Fill[i] > h0[i]) continue;   // lakes and closed basins
            if (raw.Flow[i] > K.RIVER_ACC) { chan += h0[i] - cutOnly[i]; nc++; }
            else if (raw.Flow[i] < 4f) { hill += h0[i] - cutOnly[i]; nh++; }
        }
        chan /= nc; hill /= nh;
        Assert.True(nc > 100 && nh > 1000, $"{nc} channel and {nh} hillside cells");
        Assert.True(chan > 0f && chan > 2 * hill, $"channels lowered {chan * K.H_RELIEF:F1} m, hillsides {hill * K.H_RELIEF:F1} m");
        // the whole model: the land lost exactly what went into the lakes (stream load and creep); the rest moved and stayed
        var (v, v0, _, b) = Worked(1);
        double lost = 0;
        for (int i = 0; i < n; i++) lost += v0[i] - v.Elev[i];
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
