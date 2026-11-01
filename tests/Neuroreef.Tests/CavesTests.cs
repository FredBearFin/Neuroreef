// Caves and karst (Caves.cs, 2026-11-01): caves form only in soluble rock (and lava tubes in volcanic), conduits drain
// toward springs, the water a sinkhole swallows reappears at its spring, the water budget still closes, and a cave
// entrance keeps its air near the year's mean temperature.

using System;
using System.Collections.Generic;
using Xunit;

namespace Neuroreef.Tests;

[BigWorld]
public class CavesTests
{
    // one 1024 world, built once for the tests that read its land (Terrain.Build and the water cycle's spin-up)
    static World? _w;
    static float[]? _rain;
    static World Land()
    {
        if (_w != null && _w.Elev.Length == Map.FW * Map.FW) return _w;
        var w = new World(16, 7);
        _rain = Terrain.Shape(w);
        Terrain.Finish(w, (float[])_rain.Clone());
        WaterCycle.Init(w);
        return _w = w;
    }

    [Fact] // karst passages, sinkholes and springs lie on carbonate (soluble) rock only, lava tubes on volcanic rock only;
           // and the caves are a function of the land: built again, they are the same
    public void CavesFormOnlyInSolubleRock()
    {
        var w = Land();
        int n = Map.FW * Map.FW;
        var c = Caves.Count(w);
        Assert.True(c.KarstCells > 0.03 * c.Land, $"karst on {c.KarstCells} of {c.Land} land cells");
        Assert.True(c.Passages > 100 && c.Sinkholes > 5 && c.Springs > 3 && c.Entrances > 3, c.ToString());
        for (int i = 0; i < n; i++)
        {
            byte k = w.CaveKind[i];
            if (w.Carb[i] > 0f) Assert.Equal(Rock.Sedimentary, w.Rock[i]);
            bool karst = w.Carb[i] >= K.CARB_MIN && w.LakeOf[i] < 0;
            if ((k & (Caves.Karst | Caves.Sinkhole | Caves.Spring | Caves.Losing | Caves.Ponor)) != 0 || w.CaveSpring[i] >= 0 || w.CaveSink[i] > 0f)
                Assert.True(karst, $"cell {i}: karst feature {k} on rock {Rock.Names[w.Rock[i]]} with carbonate {w.Carb[i]}");
            if ((k & Caves.Lava) != 0) Assert.Equal(Rock.Volcanic, w.Rock[i]);
            if (k == 0) Assert.Equal(0f, w.CaveVoid[i]);
        }
        // the same land makes the same caves
        var keep = ((byte[])w.CaveKind.Clone(), (float[])w.CaveFlow.Clone(), (int[])w.CaveSpring.Clone(), (float[])w.CaveSink.Clone());
        Caves.Build(w, (float[])_rain!.Clone());
        Assert.Equal(keep.Item1, w.CaveKind); Assert.Equal(keep.Item2, w.CaveFlow); Assert.Equal(keep.Item3, w.CaveSpring); Assert.Equal(keep.Item4, w.CaveSink);
    }

    [Fact] // every cell of the karst network drains down its conduit to its spring: no loops, the conduit's discharge grows
           // downstream, and no spring stands above the ground that drains to it (the water table falls toward it)
    public void ConduitsDrainTowardSprings()
    {
        var w = Land();
        int n = Map.FW * Map.FW, net = 0;
        var gathered = new Dictionary<int, int>();
        for (int i = 0; i < n; i++)
        {
            int sp = w.CaveSpring[i];
            if (sp < 0) continue;
            net++;
            Assert.Equal((byte)Realm.Land, w.Cell[sp]);
            Assert.Equal(sp, w.CaveSpring[sp]);
            Assert.True(w.Elev[i] >= w.Elev[sp], $"cell {i} lies below its spring {sp}");
            int c = i, steps = 0;
            while (c != sp)
            {
                int nx = w.CaveNext[c];
                Assert.True(nx >= 0, $"cell {i}: its conduit ends at {c} before its spring {sp}");
                Assert.Equal(sp, w.CaveSpring[nx]);
                Assert.True(w.CaveFlow[nx] >= w.CaveFlow[c], $"conduit discharge falls from {c} to {nx}");
                c = nx;
                Assert.True(++steps <= n, $"cell {i}: its conduit loops");
            }
            gathered[sp] = gathered.GetValueOrDefault(sp) + 1;
        }
        // the network converges: far fewer springs than cells, and every spring marked as one gathers a passage's discharge
        Assert.True(net > 0 && gathered.Count < net / 10, $"{gathered.Count} outlets for {net} cells");
        for (int i = 0; i < n; i++)
            if ((w.CaveKind[i] & Caves.Spring) != 0) Assert.True(w.CaveFlow[i] >= K.CAVE_Q_MIN * n / 65536f && w.CaveSpring[i] == i);
    }

    // the water cycle's stores and fluxes, to run one update twice from the same state
    static Func<Action> Snapshot(World w)
    {
        var arrays = new Array[] { w.SoilWater, w.Snow, w.Ground, w.Deep, w.PondWater, w.Runoff, w.Discharge, w.DischargeMean, w.Seep,
            w.LakeLevel, w.LakeIn, w.LakeGross, w.LakeQm, w.Moisture, w.Wet, w.DistDrink, w.Nutrient };
        var copies = Array.ConvertAll(arrays, a => (Array)a.Clone());
        double[] wb = { w.WbRain, w.WbET, w.WbPondEvap, w.WbToLakes, w.WbLakeRain, w.WbLakeEvap, w.WbLakeSpill };
        ulong step = w.Step;
        return () => () =>
        {
            for (int k = 0; k < arrays.Length; k++) Array.Copy(copies[k], arrays[k], arrays[k].Length);
            (w.WbRain, w.WbET, w.WbPondEvap, w.WbToLakes, w.WbLakeRain, w.WbLakeEvap, w.WbLakeSpill) = (wb[0], wb[1], wb[2], wb[3], wb[4], wb[5], wb[6]);
            w.Step = step;
        };
    }

    [Fact] // water poured into a sinkhole is swallowed and comes out at its spring, the water cycle's own routing doing it
    public void SinkholeWaterReappearsAtItsSpring()
    {
        var w = Land();
        int n = Map.FW * Map.FW;
        // a land sinkhole (not a losing river) whose spring already runs (its ground is full, so what arrives emerges)
        int sink = -1;
        for (int i = 0; i < n && sink < 0; i++)
            if ((w.CaveKind[i] & Caves.Sinkhole) != 0 && w.Cell[i] == (byte)Realm.Land && w.CaveSink[i] >= 0.5f && w.Flow[i] > 0f && w.Seep[w.CaveSpring[i]] > 0f) sink = i;
        Assert.True(sink >= 0, "no land sinkhole with a running spring");
        int sp = w.CaveSpring[sink];
        float pour = 0.5f * K.CAVE_CAP * w.Flow[sink] * K.P_MEAN * WaterCycle.YearFrac;   // under what it can swallow
        const int U = 4;
        var restore = Snapshot(w)();
        double Run(bool add)
        {
            restore();
            double seep = 0;
            for (int u = 0; u < U; u++)
            {
                w.Step += K.WC_EVERY;
                Array.Fill(w.Temp, 15f);
                for (int y = 0; y < Map.FW; y++) WaterCycle.Row(w, y);
                if (add && u == 0) w.Runoff[sink] += pour;
                WaterCycle.Finish(w);
                seep += w.Seep[sp];
            }
            return seep;
        }
        double without = Run(false), with = Run(true);
        float take = pour * w.CaveSink[sink];
        // the deep groundwater passes GW_TAU's share of itself on each update; the rest of what arrived has emerged
        float k = MathF.Min(1f, 365f * WaterCycle.YearFrac / K.GW_TAU);
        Assert.True(with - without >= 0.8 * (1 - k) * take, $"poured {pour:F0}, swallowed {take:F0}, {with - without:F0} more emerged at spring {sp}");
        Assert.True(with - without <= take * 1.0001 + 1e-3, $"swallowed {take:F0}, {with - without:F0} more emerged");
        restore();
    }

    [Fact] // with sinkholes, losing streams and conduits, every flux still leaves one store and enters another: the budget
           // closes over two years, with and without a climate, and the karst springs run
    public void BudgetClosesWithKarst()
    {
        foreach (float climate in new[] { 0f, 1f })
        {
            float old = Nature.Climate;
            Nature.Climate = climate;
            try
            {
                var w = new World(16, 7);
                Terrain.Build(w);
                WaterCycle.Init(w);
                int n = Map.FW * Map.FW;
                double land0 = WaterCycle.LandStore(w), lakes0 = WaterCycle.LakeStore(w);
                double swallowed = 0;
                int perYear = Nature.SeasonLength / K.WC_EVERY;
                for (int u = 0; u < 2 * perYear; u++)
                {
                    w.Step += K.WC_EVERY;
                    if (Climate.On) for (int y = 0; y < Map.FW; y++) Climate.TempRow(w, y);
                    for (int y = 0; y < Map.FW; y++) WaterCycle.Row(w, y);
                    WaterCycle.Finish(w);
                    for (int i = 0; i < n; i++) if (w.CaveSink[i] > 0f) swallowed += w.Seep[w.CaveSpring[i]] > 0f ? 1 : 0;
                }
                double land = w.WbRain - w.WbET - w.WbPondEvap - w.WbToLakes - (WaterCycle.LandStore(w) - land0);
                double lakes = w.WbToLakes + w.WbLakeRain - w.WbLakeEvap - w.WbLakeSpill - (WaterCycle.LakeStore(w) - lakes0);
                Assert.True(Math.Abs(land / w.WbRain) < 1e-4, $"climate {climate}: land residual {land / w.WbRain:E2}");
                Assert.True(Math.Abs(lakes / (w.WbToLakes + w.WbLakeRain)) < 1e-4, $"climate {climate}: lake residual {lakes:E2}");
                Assert.True(swallowed > 0, "no sinkhole drains to a running spring");
            }
            finally { Nature.Climate = old; }
        }
    }

    [Fact] // a cave entrance holds its air near the year's mean (Badino 2004): it keeps CAVE_SWING of the outside's seasons,
           // out of the sun, so a body in it is spared most of a summer's heat and a winter's cold
    public void CaveShelterBuffersTemperature()
    {
        float old = Nature.Climate;
        Nature.Climate = 1f;
        try
        {
            var w = new World(16, 3);
            int y = Map.FW / 12, cave = y * Map.FW + 5, open = cave + 1;   // 30° of latitude: strong seasons about a mild mean
            Array.Fill(w.Cell, (byte)Realm.Land); Array.Fill(w.Cont, 1f);
            w.CaveKind[cave] = Caves.Entrance;
            float mean = Climate.AnnualMean(w, cave);
            float caveLo = float.MaxValue, caveHi = float.MinValue, openLo = float.MaxValue, openHi = float.MinValue;
            float worstOpen = 0f, worstCave = 0f;   // the most heat a body must make or evaporate in one step to stay at T_BODY
            for (int p = 0; p < 16; p++)
            {
                w.Step = (ulong)(p * Nature.SeasonLength / 16);
                Climate.TempRow(w, y);
                float tc = Heat.Operative(w, cave, K.FOUND_INS, false), to = Heat.Operative(w, open, K.FOUND_INS, false);
                Assert.Equal(w.Temp[cave], w.Temp[open]);
                Assert.True(MathF.Abs(tc - mean) <= K.CAVE_SWING * MathF.Abs(w.Temp[cave] - mean) + 1e-4f, $"cave {tc:F2} °C, mean {mean:F2}, air {w.Temp[cave]:F2}");
                caveLo = MathF.Min(caveLo, tc); caveHi = MathF.Max(caveHi, tc); openLo = MathF.Min(openLo, to); openHi = MathF.Max(openHi, to);
                Heat.Balance(1f, K.FOUND_INS, K.FOUND_APP, to, false, K.C_BASAL, K.C_BASAL, out float mo, out float eo, out float co, out float ho);
                Heat.Balance(1f, K.FOUND_INS, K.FOUND_APP, tc, false, K.C_BASAL, K.C_BASAL, out float mc, out float ec, out float cc, out float hc);
                worstOpen = MathF.Max(worstOpen, mo + eo + co + ho); worstCave = MathF.Max(worstCave, mc + ec + cc + hc);
            }
            Assert.True(openHi - openLo > 15f, $"the open ground swings {openHi - openLo:F1} K");
            Assert.True(caveHi - caveLo <= K.CAVE_SWING * (openHi - openLo) + 1e-3f, $"cave swings {caveHi - caveLo:F1} K, outside {openHi - openLo:F1} K");
            Assert.True(worstCave < 0.5f * worstOpen, $"worst step's heat to make or shed: open {worstOpen / K.C_BASAL:F2}, cave {worstCave / K.C_BASAL:F2} x basal");
            // with no climate, a cave is the world without one: T_REF, bit for bit
            Nature.Climate = 0f;
            Array.Fill(w.Temp, K.T_REF);
            Assert.Equal(Heat.Operative(w, open, 0.3f, false), Heat.Operative(w, cave, 0.3f, false));
        }
        finally { Nature.Climate = old; }
    }
}
