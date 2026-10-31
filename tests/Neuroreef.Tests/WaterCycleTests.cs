// The water cycle (WaterCycle.cs): rain, snow, the soil bucket, groundwater, routing, ponds, floods and lakes as stores
// and fluxes. The budget closes; a dry spell empties the soil while groundwater keeps the rivers running; a snowpack
// stores winter's water and gives it to the rivers when it warms; ponds fill from runoff and dry when the rain stops.

using System;
using Xunit;

namespace Neuroreef.Tests;

[BigWorld]
public class WaterCycleTests
{
    static T With<T>(float climate, Func<T> f)
    {
        float old = Nature.Climate;
        Nature.Climate = climate;
        try { return f(); } finally { Nature.Climate = old; }
    }

    static World NewWorld(uint seed)
    {
        var w = new World(16, seed);
        Terrain.Build(w);
        WaterCycle.Init(w);
        return w;
    }

    /// <summary>Run updates of the water cycle alone (no plants), with the climate's temperature or a fixed one.</summary>
    static void Advance(World w, int updates, float? temp = null)
    {
        for (int u = 0; u < updates; u++)
        {
            w.Step += K.WC_EVERY;
            if (temp.HasValue) Array.Fill(w.Temp, temp.Value);
            else if (Climate.On) for (int y = 0; y < Map.FW; y++) Climate.TempRow(w, y);
            WaterCycle.Update(w);
        }
    }

    static int PerYear => Nature.SeasonLength / K.WC_EVERY;

    // rain in = evapotranspiration + pond evaporation + flow into the lakes + the change in what the land holds;
    // and for the lakes: inflow + rain = evaporation + spill + the change in their levels
    static (double land, double lakes) Residuals(World w, double land0, double lakes0)
    {
        double land = w.WbRain - w.WbET - w.WbPondEvap - w.WbToLakes - (WaterCycle.LandStore(w) - land0);
        double lakes = w.WbToLakes + w.WbLakeRain - w.WbLakeEvap - w.WbLakeSpill - (WaterCycle.LakeStore(w) - lakes0);
        return (land / w.WbRain, lakes / (w.WbToLakes + w.WbLakeRain));
    }

    // 2026-10-31: a river is its channel cells (FloodFrom of a river cell is the channel cell of its cross-section; the
    // cells beside a wide river's channel carry no stream of their own), and the discharge is measured at the mouths of
    // the major rivers (two cells wide or more: mean discharge 4 x RIVER_Q1): the many small streams that channel
    // initiation now gives drain small steep catchments with little groundwater, as headwaters do
    static double RiverDischarge(World w, out int rivers, out int flowing)
    {
        double q = 0; rivers = 0; flowing = 0;
        float major = 4f * K.RIVER_Q1 * (Map.FW * Map.FW / 65536f);
        for (int i = 0; i < Map.FW * Map.FW; i++)
        {
            if (w.Cell[i] == (byte)Realm.Land || w.LakeOf[i] >= 0 || w.FloodFrom[i] != i) continue;
            rivers++; if (w.Discharge[i] > 0f) flowing++;
            int r = w.Recv[i];
            if (r >= 0 && w.LakeOf[r] >= 0 && w.Flow[i] >= major) q += w.Discharge[i];   // the major rivers' mouths
        }
        return q;
    }

    static double SoilWater(World w)
    {
        double s = 0;
        for (int i = 0; i < Map.FW * Map.FW; i++) if (w.Cell[i] == (byte)Realm.Land) s += w.SoilWater[i];
        return s;
    }

    [Fact] // the budget closes, in a living world (plants, seasons, weather years, floods) with and without a climate
    public void WaterBudgetCloses()
    {
        foreach (float climate in new[] { 0f, K.CLIMATE, 1f })
            With(climate, () =>
            {
                using var sim = new Simulation(64, 5, 2, empty: true);
                var w = sim.W;
                double land0 = WaterCycle.LandStore(w), lakes0 = WaterCycle.LakeStore(w);
                for (int k = 0; k < 2 * Nature.SeasonLength; k++) sim.Step();
                var (land, lakes) = Residuals(w, land0, lakes0);
                Assert.True(Math.Abs(land) < 1e-5, $"climate {climate}: land budget off by {land:E2} of the rain");
                Assert.True(Math.Abs(lakes) < 1e-4, $"climate {climate}: lake budget off by {lakes:E2} of their inflow");
                // the shares are an Earth's (land: ~60% back to the air, ~40% to the sea; Trenberth et al. 2007), roughly
                double et = (w.WbET + w.WbPondEvap) / w.WbRain, run = w.WbToLakes / w.WbRain;
                // (on the tectonic relief of Revision R the land is higher and colder and evaporates less: 0.29 measured, seed 3)
                Assert.True(et >= 0.25 && et <= 0.8 && run >= 0.2 && run <= 0.75, $"climate {climate}: ET {et:F3}, runoff {run:F3} of the rain");
                // moisture is the soil's water over its capacity
                for (int i = 0; i < Map.FW * Map.FW; i++)
                    if (w.Cell[i] == (byte)Realm.Land) { Assert.InRange(w.Moisture[i], 0f, 1f); Assert.Equal(w.SoilWater[i] / w.SoilCap[i], w.Moisture[i], 5); }
                return 0;
            });
    }

    [Fact] // a dry spell: the soil dries, the ponds' runoff stops, but groundwater keeps the rivers running
    public void DrySpellDriesTheSoilButRiversKeepBaseflow()
    {
        With(0f, () =>
        {
            var w = NewWorld(7);
            Advance(w, PerYear / 2);
            double soil0 = SoilWater(w), q0 = RiverDischarge(w, out int rivers, out _);
            w.RainAnom = -1f;   // not a drop for a season
            Advance(w, PerYear / 4);
            double soil1 = SoilWater(w), q1 = RiverDischarge(w, out _, out int flowing);
            double runoff = 0; for (int i = 0; i < Map.FW * Map.FW; i++) runoff += w.Runoff[i];
            Assert.True(soil1 < 0.6 * soil0, $"soil water {soil0 / 1e6:F2} -> {soil1 / 1e6:F2} (x1e6 mm cells)");
            Assert.Equal(0.0, runoff);                                   // no rain, no surface runoff: what flows is groundwater
            // dry-season baseflow is 10-50% of wet discharge in real rivers (baseflow index; Smakhtin 2001); 14% measured on
            // the steeper tectonic relief of Revision R, against over 20% on the old noise relief
            Assert.True(q1 > 0.1 * q0, $"rivers carry {q1 / q0:P0} of their wet discharge in the dry spell");
            Assert.True(flowing == rivers, $"{rivers - flowing} of {rivers} river cells dry");
            return 0;
        });
    }

    [Fact] // a snowpack stores winter's precipitation and gives it to the rivers when it warms
    public void SnowMeltsIntoSpringDischarge()
    {
        With(K.CLIMATE, () =>
        {
            var w = NewWorld(7);
            Advance(w, 10, temp: 10f);
            double qWarm = RiverDischarge(w, out _, out _);
            // a winter: everything falls as snow; the rivers fall to their baseflow
            double rain0 = w.WbRain;
            Advance(w, 20, temp: -10f);
            double snow = 0; for (int i = 0; i < Map.FW * Map.FW; i++) snow += w.Snow[i];
            double qWinter = RiverDischarge(w, out _, out _);
            double fell = w.WbRain - rain0;                              // it lies as snow (no melt, no evaporation), but for the
            Assert.InRange(snow, 0.9 * fell, fell * (1 + 1e-5));         // wettest windward cells, whose packs pass SNOW_MAX and flow away
            Assert.True(qWinter < 0.6 * qWarm, $"winter discharge {qWinter / qWarm:P0} of the warm one");
            // spring: it melts at DDF per degree-day (the deepest windward packs last longest), and the rivers run high
            var s0 = (float[])w.Snow.Clone();
            double peak = 0, left = 0;
            const int thaw = 10;
            for (int u = 0; u < thaw; u++)
            {
                Advance(w, 1, temp: 8f);
                peak = Math.Max(peak, RiverDischarge(w, out _, out _));
            }
            float melt = K.DDF * 8f * 365f * WaterCycle.YearFrac * thaw;
            for (int i = 0; i < Map.FW * Map.FW; i++) { left += w.Snow[i]; Assert.Equal(MathF.Max(0f, s0[i] - melt), w.Snow[i], 1e-3f * s0[i] + 1e-3f); }
            Assert.True(left < 0.5 * snow, $"{left / snow:P1} of the snow left after the thaw");
            Assert.True(peak > 1.5 * qWarm, $"spring peak {peak / qWarm:F2} x the discharge of a warm season with the same rain");
            return 0;
        });
        // and with Earth-like seasons, snow lies through the winter and is gone in the summer over much of the cold land
        With(1f, () =>
        {
            var w = NewWorld(7);
            int n = Map.FW * Map.FW, land = 0, seasonal = 0;
            var lo = new float[n]; var hi = new float[n]; Array.Fill(lo, float.MaxValue);
            for (int u = 0; u < 2 * PerYear; u++)
            {
                Advance(w, 1);
                if (u >= PerYear) for (int i = 0; i < n; i++) { lo[i] = MathF.Min(lo[i], w.Snow[i]); hi[i] = MathF.Max(hi[i], w.Snow[i]); }
            }
            for (int i = 0; i < n; i++) if (w.Cell[i] == (byte)Realm.Land) { land++; if (hi[i] > 20f && lo[i] < 1f) seasonal++; }
            Assert.True(seasonal > land / 50, $"{seasonal} of {land} land cells have a seasonal snowpack");
            for (int i = 0; i < n; i++) Assert.True(w.Snow[i] <= K.SNOW_MAX, $"cell {i}: snow {w.Snow[i]}");
            return 0;
        });
    }

    [Fact] // ponds fill from the runoff of their basins after rain, and dry when the rain stops; drinking water follows them
    public void PondsFillAfterRainAndDryInDrought()
    {
        With(0f, () =>
        {
            var w = NewWorld(4);
            int n = Map.FW * Map.FW, sites = 0;
            for (int i = 0; i < n; i++) sites += w.PondSite[i];
            // 2026-10-31: 60 sites (102 before): hollows big enough hold lakes now, and pond sites keep POND_GAP from the
            // denser river network
            Assert.True(sites > 50, $"{sites} pond sites");
            int Ponded() { int c = 0; for (int i = 0; i < n; i++) if (w.PondSite[i] != 0 && w.Wet[i] == 2) c++; return c; }
            w.RainAnom = -1f;                  // a year without rain: they dry (bar those fed by groundwater)
            Advance(w, PerYear);
            int dry = Ponded();
            float far = 0; for (int i = 0; i < n; i++) far = MathF.Max(far, w.DistWater[i] - w.DistDrink[i]);
            w.RainAnom = 0.5f;                 // a wet year: they fill
            Advance(w, PerYear / 2);
            int wet = Ponded();
            // on the tectonic relief of Revision R a third of the ponds are spring-fed and outlast a drought: 53 wet against 37
            // after a year without rain (seed 4, 102 sites; the same on Windows and Linux since all math goes through MathK)
            // 2026-10-31: 13 wet against 3 (60 sites): the denser river network takes the deep groundwater that fed the
            // spring-fed ponds (it emerges into the first river it meets), and leaves the ponds smaller catchments
            Assert.True(wet > dry + sites / 10 && wet >= 2 * dry, $"{wet} ponds in a wet year, {dry} after a drought ({sites} sites)");
            Assert.True(wet > sites / 8, $"{wet} of {sites} pond sites hold water in a wet year");
            // drinkable water is measured to them
            for (int i = 0; i < n; i++)
            {
                if (w.Wet[i] != 0) Assert.Equal(0f, w.DistDrink[i]);
                Assert.True(w.DistDrink[i] <= w.DistWater[i] + 1e-3f, $"cell {i}: drink {w.DistDrink[i]}, water {w.DistWater[i]}");
            }
            float far2 = 0; for (int i = 0; i < n; i++) far2 = MathF.Max(far2, w.DistWater[i] - w.DistDrink[i]);
            Assert.True(far2 > 5f && far2 >= far, $"ponds bring water {far2:F1} cells closer (after the drought {far:F1})");
            // a frozen pond cannot be drunk
            Array.Fill(w.Temp, -5f);
            Ponds.Update(w);
            Assert.Equal(0, Ponds.Full(w));
            for (int i = 0; i < n; i++) Assert.Equal(w.DistWater[i], w.DistDrink[i]);
            return 0;
        });
    }

    [Fact] // 2026-10-31: a full lake spills down its outflow river into the next lake or the sea, and that river runs with it;
           // a terminal lake below its sill spills nothing
    public void LakesSpillDownTheirOutflowRivers()
    {
        var w = NewWorld(7);
        Advance(w, PerYear / 2);
        int spilling = 0, withRiver = 0;
        for (int l = 0; l < w.LakeCount; l++)
        {
            if (w.LakeExit[l] < 0) continue;                     // the sea
            int d = w.LakeDown[l];
            Assert.True(d >= 0 && d != l, $"lake {l} spills into {d}");
            if (w.LakeLevel[l] <= w.LakeSill[l]) continue;       // below its sill: it does not spill
            spilling++;
            int c = w.LakeExit[l];
            if (w.LakeOf[c] >= 0) { Assert.Equal(d, w.LakeOf[c]); continue; }   // straight into a lake beside it
            withRiver++;
            // down the outflow to the next lake, every cell carries the spill (and what joins it)
            for (; w.LakeOf[c] < 0; c = w.Recv[c]) Assert.True(w.Discharge[c] > 0f, $"lake {l}'s outflow is dry at {c}");
            Assert.Equal(d, w.LakeOf[c]);
        }
        Assert.True(spilling > 3 && withRiver > 0, $"{spilling} lakes spill, {withRiver} into an outflow river");
    }

    [Fact] // the water cycle is deterministic and does not depend on how many threads run its rows
    public void WaterCycleIsDeterministic()
    {
        ulong Hash(int threads)
        {
            using var sim = new Simulation(64, 3, threads, empty: true);
            for (int k = 0; k < 600; k++) sim.Step();
            var w = sim.W; ulong h = 14695981039346656037UL;
            void Mix(float v) { h ^= (uint)BitConverter.SingleToInt32Bits(v); h *= 1099511628211UL; }
            for (int i = 0; i < Map.FW * Map.FW; i++)
            {
                Mix(w.SoilWater[i]); Mix(w.Snow[i]); Mix(w.Ground[i]); Mix(w.PondWater[i]); Mix(w.Discharge[i]); Mix(w.DischargeMean[i]);
                h ^= w.Wet[i]; h *= 1099511628211UL;
            }
            return h;
        }
        Assert.Equal(Hash(1), Hash(3));
    }
}
