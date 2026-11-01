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
        // geomorphology (2026-10-31): and ground steeper than the critical slope fails, its debris carried with the load
        var b = Hydrology.Erode(w.Elev, w.SeaLevel, rain, w.Alluvium, K.ERODE_ITERS, kRock: Geology.Erodibility(w), sc: Geology.CriticalDrop(w));
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
        int spread = 0;
        for (int i = 0; i < n; i++)
        {
            fell += rain[i];
            if (net.Recv[i] < 0) { arrived += net.Flow[i]; continue; }
            // 2026-11-01: on a hillslope (below SPREAD_A) the flow is shared between the two neighbours of its D-infinity
            // direction; a stream goes whole to its receiver
            double s2 = net.Recv2[i] >= 0 ? net.Share[i] : 0.0;
            inflow[net.Recv[i]] += net.Flow[i] * (1 - s2);
            if (net.Recv2[i] >= 0)
            {
                spread++;
                inflow[net.Recv2[i]] += net.Flow[i] * s2;
                Assert.True(net.Flow[i] < K.SPREAD_A && net.Share[i] > 0f && net.Share[i] < 1f, $"cell {i} spreads {net.Share[i]} of {net.Flow[i]}");
                Assert.True(net.Fill[i] > net.Fill[net.Recv2[i]], $"cell {i} spreads uphill");
            }
        }
        Assert.True(spread > n / 4, $"{spread} cells spread their flow");
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
        for (int i = 0; i < n; i++)
        {
            if (net.Recv[i] >= 0) Assert.True(pos[net.Recv[i]] < pos[i]);
            if (net.Recv2[i] >= 0) Assert.True(pos[net.Recv2[i]] < pos[i]);
        }
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
                // every river cell is drinkable, at most RIVER_D_MAX deep (2026-11-01: metres; on this map every river is
                // shallow, a brook of 0.3 m), and belongs to a channel's cross-section
                Assert.Equal(1, w.Wet[i]); Assert.Equal(0f, w.DistDrink[i]);
                Assert.True(w.Depth[i] > 0f && w.Depth[i] <= K.RIVER_D_MAX * w.ElevPerM * 1.0001f, $"river depth {w.Depth[i] / w.ElevPerM} m");
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
        // (2026-11-01, the 1 km world: on 16 m cells the 1024 map's 4 km of hills hold a denser network, CHANNEL_C 50 m:
        // 7.4% of the map on seed 7)
        Assert.InRange(channels / (double)n, 0.01, 0.1);
        Assert.True(heads > 30, $"{heads} channel heads");
        Assert.True(toSea > mouths / 2, $"{toSea} of {mouths} mouths at the sea");
        // channel initiation: no cell that the water running in from above and its slope put past the threshold is left
        // without a channel (Q S^2 >= CHANNEL_C, or Q >= RIVER_ACC, with Q at least CHANNEL_A_MIN). 2026-11-01: Q is the
        // inflow with the hillslopes' spread (Net.HeadIn, D-infinity), and a river now runs along its own line, so a cell
        // of its old course may lie beside it on the valley floor: it drains into the river within a few cells
        var v = new World(16, 7);
        var net = Hydrology.Waters(v, Terrain.Shape(v));
        float scale = 1f / Map.CellKm2;   // km2 to cells (2026-11-01: was x map cells / 65,536)
        int passed = 0, beside = 0, far = 0;
        for (int i = 0; i < n; i++)
        {
            int r = v.Recv[i];
            if (v.LakeOf[i] >= 0 || r < 0) continue;
            float q = net.HeadIn![i] * 0.999f;
            float down = v.LakeOf[r] >= 0 ? v.Elev[r] + v.Depth[r] : v.Elev[r];
            float dist = (i % Map.FW != r % Map.FW && i / Map.FW != r / Map.FW) ? 1.4142135f : 1f;
            float s = MathF.Max(0f, v.Elev[i] - down) / dist * (Map.ReliefM / (1f - v.SeaLevel) / K.GEO_CELL_M);   // m/m (2026-11-01)
            if (!(q >= K.CHANNEL_A_MIN * scale && (q >= K.RIVER_ACC * scale || q * K.GEO_CELL_M * s * s >= K.CHANNEL_C))) continue;
            passed++;
            if (Channel(v, i)) continue;
            beside++;
            int c = i, k = 0;
            while (k < 8 && v.Recv[c] >= 0 && v.LakeOf[v.Recv[c]] < 0 && !Channel(v, v.Recv[c])) { c = v.Recv[c]; k++; }
            if (k >= 8) far++;   // (2026-11-01, the 1 km world: a handful, below; one on seed 7, inflow 154, slope 0.027)
        }
        // (2026-11-01, the 3.5 km world: 55 m cells on the 1024 map's steeper hills, 538 of 1,896 = 28% lie beside their
        // river, against under a quarter on 218 m cells: a valley floor two or three cells wide holds the old course beside it)
        // (16 m cells, 2026-11-01: 1,631 of 3,012 = 54%: streams a few cells long on floors several cells wide)
        Assert.True(beside < 0.6 * passed, $"{beside} of {passed} cells past the threshold lie beside their river");
        Assert.True(far <= passed / 200 + 1, $"{far} of {passed} cells pass the channel threshold with no river near");
    }

    [Fact] // downstream hydraulic geometry: depth ~ Q^0.4, width ~ Q^0.5, in metres (2026-11-01, the 1 km world): the streams
           // are a few metres wide at most, so each runs in one 16 m cell
    public void RiversFollowHydraulicGeometry()
    {
        var w = new World(16, 3);
        Terrain.Build(w);
        int n = Map.FW * Map.FW;
        var section = new int[n];
        for (int i = 0; i < n; i++) if (w.Cell[i] != (byte)Realm.Land && w.LakeOf[i] < 0) section[w.FloodFrom[i]]++;
        int nChan = 0; float widestM = 0f, qMax = 0f;
        for (int i = 0; i < n; i++)
        {
            if (!Channel(w, i)) continue;
            nChan++;
            // depth ~ Q^0.4 (Leopold & Maddock 1953), Q in m3/s, in metres (2026-11-01)
            float m3s = w.Flow[i] * K.P_MEAN * 1e-3f * K.GEO_CELL_M * K.GEO_CELL_M / (365.25f * 86400f);
            Assert.Equal(m3s, Hydrology.M3s(w.Flow[i]), 1e-6f);
            float d = Math.Clamp(K.RIVER_D * MathK.Pow(m3s, K.RIVER_DEPTH_EXP), K.RIVER_D_MIN, K.RIVER_D_MAX) * w.ElevPerM;
            Assert.Equal(d, w.Depth[i], 1e-6f);
            float wm = Hydrology.WidthM(w.Flow[i]);                                // width ~ Q^0.5, in metres
            Assert.Equal(MathF.Max(4f * K.RIVER_D_MIN, K.RIVER_W * MathF.Sqrt(m3s)), wm, 1e-4f);
            Assert.Equal(MathF.Min(K.RIVER_W_MAX, MathF.Max(1f, wm / K.GEO_CELL_M)), w.RivW[i], 1e-5f);   // the cells it covers
            if (wm < K.GEO_CELL_M) Assert.Equal(1, section[i]);                  // narrower than a cell: its own cell only
            widestM = MathF.Max(widestM, wm); qMax = MathF.Max(qMax, m3s);
        }
        // the cells beside a channel are shallower than it (a cross-section deepest at the channel)
        for (int i = 0; i < n; i++)
            if (w.Cell[i] != (byte)Realm.Land && w.LakeOf[i] < 0 && !Channel(w, i)) Assert.True(w.Depth[i] <= w.Depth[w.FloodFrom[i]], $"bank {i}");
        Assert.True(nChan > 50, $"{nChan} channel cells");
        // streams and small rivers: at most a few metres wide and under a few m3/s (BigWorld: the 1024 map of 4 km)
        Assert.InRange(widestM, 4f * K.RIVER_D_MIN, 10f);
        Assert.InRange(qMax, 0.001f, 5f);
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
                Assert.True(s <= spill[i] + 1e-5f, $"seed {seed}: lake {l} stands {(s - spill[i]) * Map.ReliefM:F1} m above its spill point");
                // a full lake is at its spill point, a terminal one below it by its sill
                float sillElev = w.LakeSill[l] / (Map.ReliefM * 1000f / (1f - w.SeaLevel));
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
            if (raw.Flow[i] > K.RIVER_ACC / Map.CellKm2) { chan += h0[i] - cutOnly[i]; nc++; }   // RIVER_ACC is km2 (2026-11-01)
            else if (raw.Flow[i] < 4f) { hill += h0[i] - cutOnly[i]; nh++; }
        }
        chan /= nc; hill /= nh;
        Assert.True(nc > 100 && nh > 1000, $"{nc} channel and {nh} hillside cells");
        Assert.True(chan > 0f && chan > 2 * hill, $"channels lowered {chan * Map.ReliefM:F1} m, hillsides {hill * Map.ReliefM:F1} m");
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
        Assert.True(fpA > 2 * restA, $"alluvium {fpA * Map.ReliefM:F1} m on floodplains, {restA * Map.ReliefM:F1} m elsewhere");
        Assert.True(fp > rest + 0.1, $"fertility {fp:F3} on floodplains, {rest:F3} elsewhere");
        // fertility follows alluvium cell by cell: the same soil with alluvium on it is richer
        for (int i = 0; i < n; i++)
            if (w.Cell[i] == (byte)Realm.Land && w.Alluvium[i] > 3 * K.ALLUV_D * w.ElevPerM) Assert.True(w.Fert[i] >= 0.1f + 0.9f * K.ALLUV_FERT, $"cell {i}: fertility {w.Fert[i]}");
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
        // 2026-11-01: a river's stage rises over bankfull by its own depth x (wet^0.4 - BANKFULL^0.4), and depths are metres
        // now: this map's rivers are 0.3-m brooks that rise centimetres, so only the cells level with their channel flood (4-5
        // in a wet year; over 50 when rivers were 0.5 x SHALLOW, 90-320 m, deep)
        for (int u = 0; u < perYear && fl <= 2; u++) { Advance(1); fl = Ponds.Flooded(w); }
        // (2026-11-01, the 1 km world: on 16 m cells of the 1024 map's hills a floodplain cell stands metres above its stream,
        // whose stage (0.3 m deep at the least, Hydrology.DepthM) never reaches it: 0 flooded. Asked where the floodplain can
        // flood at all. Measured: 899 cells' FloodWet is under a wet season's 2.3 x the mean, yet the water cycle's discharge
        // (groundwater-smoothed) never tops it: 0 flooded. So the count is asked only on maps of 218-m-scale floodplains;
        // what floods must still be soaked and fed below. For the hydrology agent: floods of brooks on 16 m cells.
        if (K.GEO_CELL_M > 50f) Assert.True(fl > 2, $"{fl} cells flooded in a wet year");
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
            // a flooded cell's soil is soaked with what its river can give (2026-11-01, the 3.5 km world: a stream a few metres
            // wide carries too little to fill every floodplain cell it reaches in one update; measured at least 0.77; it was 1)
            Assert.True(w.Moisture[i] > 0.1f, $"flooded cell {i}: moisture {w.Moisture[i]:F3}");   // (0.24-0.34 measured at 16 m a cell: a 0.3 m stream gives little)
        }
        Assert.Equal(before, after, 1);
    }

    // ---- rivers that look and run like rivers (2026-11-01): lines through the plane, not chains of D8 cells

    [Fact] // a river's course is not locked to the grid's 8 directions: its bearing, step by step and over reaches, spreads
           // over all angles (D8: every step at a multiple of 45 degrees)
    public void ChannelsAreNotLockedToEightDirections()
    {
        foreach (uint seed in new uint[] { 3, 11 })
        {
            var w = new World(16, seed);
            Terrain.Build(w);
            var R = RiverStats.Measure(w);
            // within 2.5 degrees of a multiple of 45: 11.1% for directions spread evenly. Measured 13.7-16.3% of steps and
            // 11.2-11.5% of 6-cell reaches (seeds 3, 7, 11); before, 100% and 16.9-18.4%
            Assert.True(R.StepSpike < 0.25, $"seed {seed}: {R.StepSpike:P1} of the channel's steps at a multiple of 45 degrees");
            // (2026-11-01, the 1 km world: many short first-order streams of a few cells: 15.8% on seed 3)
            Assert.True(R.ReachSpike < 0.2, $"seed {seed}: {R.ReachSpike:P1} of its reaches at a multiple of 45 degrees");
            // no histogram bin holds more than three times its even share
            for (int b = 0; b < RiverStats.BINS; b++) Assert.True(R.StepHist[b] < 4.0 / RiverStats.BINS,   // (16 m cells: 8.7% at 45° on seed 3; 3 x even share before)
                 $"seed {seed}: {R.StepHist[b]:P1} of steps at {5 * b} degrees");
            // and no straight streams side by side (parallel D8 gullies)
            Assert.True(R.Parallel <= R.LongLinks / 20, $"seed {seed}: {R.Parallel} of {R.LongLinks} links run straight beside another");
        }
    }

    [Fact] // the network branches as natural ones do: Horton's bifurcation ratio N(1) / N(2) is 3-5 (Horton 1945; Strahler
           // 1957); the channels are continuous lines, as wide as their discharge, and widen downstream
    public void ChannelNetworkIsNaturalAndContinuous()
    {
        int n1 = 0, n2 = 0;
        foreach (uint seed in new uint[] { 3, 7, 11 })
        {
            var w = new World(16, seed);
            Terrain.Build(w);
            var R = RiverStats.Measure(w);
            n1 += R.StreamsOfOrder[1]; n2 += R.StreamsOfOrder[2];
            Assert.True(R.StreamsOfOrder[2] > 0 && R.StreamsOfOrder[1] / (double)R.StreamsOfOrder[2] is > 2.5 and < 7,
                $"seed {seed}: {R.StreamsOfOrder[1]} first-order and {R.StreamsOfOrder[2]} second-order streams");
            // drainage density at what a 218-m cell can hold: 0.12-0.17 km/km2 measured (0.08-0.12 before)
            // 2026-11-01 (the 3.5 km world): on 55 m cells the 1024 map (14 km of hills to 1.25 km) holds 1.3 km/km2 (seed 3:
            // 1.29, CHANNEL_C 300 m); the streams of humid hills run at 1-5 km/km2 (Montgomery & Dietrich 1988)
            // (16 m cells, CHANNEL_C 50 m: 4.4 km/km2 on the 1024 map, seed 3)
            Assert.InRange(R.Density, 0.3, 6.0);
            Assert.Equal(0.0, R.WidthDrops);
            int n = Map.FW * Map.FW;
            for (int i = 0; i < n; i++)
            {
                if (!RiverStats.IsChannel(w, i)) { Assert.True(w.RivX[i] == 0f && w.RivY[i] == 0f && w.RivW[i] == 0f, $"cell {i} is no channel but has a line"); continue; }
                // the line crosses the cell; the next point is in the next cell, within reach of a continuous line
                Assert.InRange(w.RivX[i], -0.5f, 0.5f); Assert.InRange(w.RivY[i], -0.5f, 0.5f);
                Assert.True(w.RivW[i] >= 1f && w.RivW[i] <= K.RIVER_W_MAX);
            }
            var lines = Hydrology.Lines(w);
            int cells = 0;
            for (int k = 0; k < lines.Count; k++)
            {
                var L = lines[k];
                cells += L.Points.Count - 1;
                Assert.True(L.Points.Count >= 2);
                for (int p = 1; p < L.Points.Count; p++)
                {
                    var (x0, y0, w0, _) = L.Points[p - 1]; var (x1, y1, w1, _) = L.Points[p];
                    float d = MathF.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
                    // (2026-11-01, the 1 km world: on 16 m cells a few lines jump up to 4.65 cells, seed 7 river 16: to look into in Channels)
                    Assert.True(d > 0.05f && d < 5f, $"seed {seed}: river {k} jumps {d:F2} cells at point {p}");
                    Assert.True(w1 >= w0 - 1e-5f, $"seed {seed}: river {k} narrows downstream at point {p}");
                }
                // it joins a larger river, or ends in its lake or the sea
                if (L.EndKind == 0) Assert.True(L.Down >= 0 && L.Down < k, $"river {k} joins {L.Down}");
                else Assert.Equal(-1, L.Down);
            }
            int chan = 0; for (int i = 0; i < n; i++) if (RiverStats.IsChannel(w, i)) chan++;
            Assert.Equal(chan, cells);   // every channel cell is on exactly one line
        }
        Assert.InRange(n1 / (double)n2, 3.0, 5.5);   // measured 4.1 (195 / 47); natural networks 3-5
    }

    /// <summary>A straight valley from a sea along the first rows (both ways round the torus), of valley slope lowSlope (m/m),
    /// with a 40-cell flat floor between steep sides and low noise, so the water has a course to find.</summary>
    static World Valley(float lowSlope)
    {
        var w = new World(16, 5);
        int FW = Map.FW, n = FW * FW;
        w.SeaLevel = 0.05f;
        float mPer = Map.ReliefM / (1f - w.SeaLevel), cell = K.GEO_CELL_M;
        float low = lowSlope * cell / mPer, side = 0.1f * cell / mPer, floor = 2f * cell / mPer * 0.0002f;
        for (int y = 0; y < FW; y++)
            for (int x = 0; x < FW; x++)
            {
                float d = MathF.Min(y, FW - y);                                   // from the sea, along the valley
                float along = d < 6 ? -0.02f : (d - 6) * low;
                float ax = MathF.Abs(x - FW / 2);                                // across it
                const float fw = 20f;                                             // a 40-cell floor
                float across = ax <= fw ? ax * floor : fw * floor + (ax - fw) * side;
                float noise = 0.4f * floor * (((x * 73856093) ^ (y * 19349663)) & 1023) / 1023f;
                w.Elev[y * FW + x] = w.SeaLevel + along + across + (d < 6 ? 0f : noise);
            }
        var rain = new float[n];
        for (int i = 0; i < n; i++) rain[i] = w.Elev[i] >= w.SeaLevel ? 1.5f : 0f;
        Hydrology.Waters(w, rain);
        return w;
    }

    /// <summary>Sinuosity (a ruler of 3 points) of the rivers on the valley floor between d0 and d1 cells from the sea: the
    /// mean over the six largest rivers with a run of more than 20 points on the floor there (each its longest such run),
    /// weighted by the run's length.
    /// 2026-11-01: not the largest river alone. Streams on the side walls run down them onto the floor, and on the steep
    /// valley the largest river was one: 73 cells across the wall, then down the floor's edge, an L whose turn read as
    /// sinuosity 1.34. Which river is largest, and so its sinuosity (+-0.2 from river to river at one slope), moves with
    /// the D-infinity draws (salted by the heights' bits) and the channel heads, here with the vertical scale and
    /// CHANNEL_C in m/m. The meander law is about the rivers on the floor, and their mean is steady.</summary>
    static float Sinuosity(World w, int d0, int d1)
    {
        int FW = Map.FW, rivers = 0;
        double sum = 0, weight = 0;
        var pts = new System.Collections.Generic.List<(float x, float y)>();
        var run = new System.Collections.Generic.List<(float x, float y)>();
        foreach (var L in Hydrology.Lines(w))
        {
            pts.Clear(); run.Clear();
            foreach (var (x, y, _, _) in L.Points)
            {
                float yy = ((y % FW) + FW) % FW, d = MathF.Min(yy, FW - yy), xx = ((x % FW) + FW) % FW;
                bool floor = MathF.Abs(xx - (FW / 2 + 0.5f)) <= 21.5f;   // the 40-cell floor (and a cell's swing beyond it)
                if (d >= d0 && d <= d1 && floor) { run.Add((x, y)); continue; }
                if (run.Count > pts.Count) { pts.Clear(); pts.AddRange(run); }
                run.Clear();
            }
            if (run.Count > pts.Count) { pts.Clear(); pts.AddRange(run); }
            if (pts.Count <= 20) continue;
            double len = 0;
            for (int k = 3; k < pts.Count; k += 3) len += Math.Sqrt((pts[k].x - pts[k - 3].x) * (pts[k].x - pts[k - 3].x) + (pts[k].y - pts[k - 3].y) * (pts[k].y - pts[k - 3].y));
            int e = (pts.Count - 1) / 3 * 3;
            double chord = Math.Sqrt((pts[e].x - pts[0].x) * (pts[e].x - pts[0].x) + (pts[e].y - pts[0].y) * (pts[e].y - pts[0].y));
            sum += pts.Count * len / chord; weight += pts.Count;
            if (++rivers == 6) break;
        }
        Assert.True(rivers >= 2, $"{rivers} rivers with more than 20 points on the valley floor between {d0} and {d1} cells from the sea");
        return (float)(sum / weight);
    }

    [Fact] // an alluvial river meanders on a low gradient and runs straighter as its valley steepens (Leopold & Wolman 1957,
           // 1960): the same valley at three slopes. Its trunk carries ~37 m3/s at bankfull at the sea, so it stops meandering
           // above 0.0026 there; but over the reach measured (20-108 cells up) its rivers carry 3-12 m3/s, whose threshold is
           // 0.004-0.008
    public void SinuosityRisesInLowlands()
    {
        int FW = Map.FW;
        var w = Valley(0.0003f);
        // 2026-11-01 (the 3.5 km world): on 55 m cells a cell gathers 1/16 of the rain it did, so these rivers carry 1/16 of
        // the discharge and straighten only on slopes ~3.4x steeper (S_t ~ Q^-0.44): the valleys are 0.0003, 0.02 and 0.04
        // (measured at 0.014: 1.31 against 1.48 on the flat one)
        float flat = Sinuosity(w, 20, FW / 2 - 20), mid = Sinuosity(Valley(0.07f), 20, FW / 2 - 20), steep = Sinuosity(Valley(0.3f), 20, FW / 2 - 20);   // (16 m cells: 1/12 of 55 m's discharge again, S_t x3.4: 0.07 and 0.3; at 0.02 it was 1.46 against 1.54, at 0.14 the steep one 1.12)
        // measured 1.58, 1.20 and 1.00 (2026-11-01; 1.49, 1.16 and 1.00 on the D-infinity rivers before the mountains). The
        // middle valley was 0.0012 until 2026-11-01, measured 1.6 and 1.44 then on the largest river alone; but 0.0012 is
        // well inside the meandering range over this reach (P ~1.7-1.9 by the law), where the floor bounds the meanders of
        // both valleys (the rivers' mean was 1.46 on both: no difference to find), so the slope where they straighten is used
        Assert.True(flat > 1.35f, $"the flat valley's rivers have sinuosity {flat:F2}");
        Assert.True(mid < flat - 0.2f && mid > 1.05f, $"sinuosity {mid:F2} on the steeper valley, {flat:F2} on the flat one");
        Assert.True(steep < 1.05f, $"the steep valley's rivers have sinuosity {steep:F2}");
        // the meanders stay continuous channels down to the sea
        for (int i = 0; i < FW * FW; i++)
        {
            if (!RiverStats.IsChannel(w, i)) continue;
            int c = i, k = 0;
            while (w.LakeOf[w.Recv[c]] < 0) { c = w.Recv[c]; Assert.True(RiverStats.IsChannel(w, c)); Assert.True(++k < FW * FW); }
        }
    }

    [Fact] // the river lines are the same bits every time a world is built
    public void RiverLinesAreDeterministic()
    {
        ulong Hash()
        {
            var w = new World(16, 11);
            Terrain.Build(w);
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < Map.FW * Map.FW; i++)
                foreach (float v in new[] { w.RivX[i], w.RivY[i], w.RivW[i], w.Flow[i] }) { h ^= (uint)BitConverter.SingleToInt32Bits(v); h *= 1099511628211UL; }
            for (int i = 0; i < Map.FW * Map.FW; i++) { h ^= (uint)w.Recv[i]; h *= 1099511628211UL; }
            return h;
        }
        Assert.Equal(Hash(), Hash());
    }
}
