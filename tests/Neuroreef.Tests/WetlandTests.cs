// Water depths in metres and wetlands from the water table (2026-11-01; Hydrology.Bathymetry, Wetlands.cs): standing water
// deepens from its shore along a shoreface profile with its surface flat; rivers are as deep as their discharge makes them;
// wetlands are where the water table meets the surface for much of the year, low and convergent, wet-soiled, peaty; peat
// forms only in waterlogged ground; flood-tolerant plants grow where the intolerant cannot.

using System;
using Xunit;

namespace Neuroreef.Tests;

public class WetlandTests
{
    static float Profile(float xM) => xM <= MathF.Pow(K.SHORE_HC / K.SHORE_A, 1.5f) ? K.SHORE_A * MathF.Pow(xM, 2f / 3f)
        : K.SHORE_HC + K.SHORE_S * (xM - MathF.Pow(K.SHORE_HC / K.SHORE_A, 1.5f));

    [Fact] // the sea and the lakes: a shore cell (one beside land or a river) is in the shallow realm, no cell is deeper than
           // the shoreface profile at its distance from the shore, each body's surface is flat, and the sea goes deeper offshore
    public void StandingWaterDeepensFromTheShore()
    {
        var w = new World(16, 3);
        Terrain.Build(w);
        int FW = Map.FW, n = FW * FW, shore = 0;
        float epm = w.ElevPerM, deepest = 0f;
        var lo = new float[w.LakeCount]; var hi = new float[w.LakeCount];
        Array.Fill(lo, float.MaxValue); Array.Fill(hi, float.MinValue);
        for (int i = 0; i < n; i++)
        {
            int l = w.LakeOf[i];
            if (l < 0) continue;
            float m = w.Depth[i] / epm, s = w.Elev[i] + w.Depth[i];
            lo[l] = MathF.Min(lo[l], s); hi[l] = MathF.Max(hi[l], s);
            deepest = MathF.Max(deepest, m);
            // a shore cell: one with land or a river beside it (not only at a corner, which lies 1.4 cells off)
            int x = i % FW, y = i / FW; bool edge = false;
            for (int k = 0; k < 4; k++)
            {
                int dx = k == 0 ? 1 : k == 1 ? -1 : 0, dy = k == 2 ? 1 : k == 3 ? -1 : 0;
                if (w.LakeOf[World.WrapI(y + dy, FW) * FW + World.WrapI(x + dx, FW)] < 0) edge = true;
            }
            if (edge)
            {
                shore++;
                Assert.True(m <= Profile(0.5f * K.GEO_CELL_M) * 1.001f + 0.01f, $"shore cell {i}: {m:F2} m");
                Assert.Equal((byte)Realm.Shallow, w.Cell[i]);
            }
        }
        Assert.True(shore > 0);
        for (int l = 0; l < w.LakeCount; l++) if (w.LakeArea[l] > 0) Assert.True(hi[l] - lo[l] < 1e-4f * epm * 1000f, $"lake {l}: its surface spans {(hi[l] - lo[l]) / epm:F3} m");
        Assert.True(deepest > K.SHORE_HC, $"the deepest water {deepest:F1} m");
        Assert.True(deepest < 400f, $"the deepest water {deepest:F1} m");
    }

    [Fact] // a region's wetlands: where the water table stands near the surface for a quarter of the year or more, on low
           // (near its drainage), convergent ground; their soil is wet, they hold peat, their kinds follow their definitions,
           // and the plants there are flood tolerant
    public void WetlandsFormWhereTheWaterTableMeetsTheSurface()
    {
        Map.Set(512);
        try
        {
            using var sim = new Simulation(64, 7, 2, empty: true);
            var w = sim.W;
            int n = Map.FW * Map.FW, wet = 0, dry = 0;
            double hW = 0, hD = 0, tW = 0, tD = 0, mW = 0, pW = 0, pD = 0, fW = 0, fD = 0, cW = 0, cD = 0;
            for (int i = 0; i < n; i++)
            {
                if (w.Cell[i] != (byte)Realm.Land) { Assert.Equal(0, w.Wetland[i]); continue; }
                if (w.Peat[i] > 0f) Assert.True(w.WetDur[i] > 0f, $"peat {w.Peat[i]} m on ground never waterlogged");
                double fl = 0, cv = 0;
                for (int s = 0; s < 3; s++)
                {
                    int g = w.POcc[i * 4 + s]; if (g == 0) continue;
                    float b = s == 0 ? w.Pool[(int)Pool.Grass][i] : s == 1 ? w.Shrub[i] : w.Tree[i];
                    fl += b * w.Flora.Level(g, PT.FLOOD) / 15.0; cv += b;
                }
                byte k = w.Wetland[i];
                if (k == Wetlands.None)
                {
                    Assert.True(w.WetDur[i] < K.WET_DUR);
                    dry++; hD += Math.Min(w.Hand[i], 1000f); tD += w.Twi[i]; pD += w.Peat[i]; fD += fl; cD += cv;
                    continue;
                }
                Assert.True(w.WetDur[i] >= K.WET_DUR);
                bool peat = w.Peat[i] >= K.PEAT_MIN;
                Assert.Equal(peat, k == Wetlands.Fen || k == Wetlands.Bog);
                if (k == Wetlands.Bog) Assert.True(w.WetMin[i] < K.WET_MINERO);
                if (k == Wetlands.Swamp) Assert.True(w.Shrub[i] + w.Tree[i] >= K.WET_WOODY);
                wet++; hW += Math.Min(w.Hand[i], 1000f); tW += w.Twi[i]; mW += w.Moisture[i]; pW += w.Peat[i]; fW += fl; cW += cv;
            }
            Assert.True(wet > 0.002 * (wet + dry), $"{wet} wetland cells of {wet + dry}");
            Assert.True(hW / wet < 0.2 * hD / dry, $"height above drainage: wetlands {hW / wet:F1} m, dry land {hD / dry:F1} m");
            Assert.True(tW / wet > tD / dry + 1, $"wetness index: wetlands {tW / wet:F2}, dry land {tD / dry:F2}");
            Assert.True(mW / wet > 0.85, $"wetland soil moisture {mW / wet:F2}");
            Assert.True(pW / wet > 10 * pD / dry && pW / wet > 0.3, $"peat: wetlands {pW / wet:F2} m, dry land {pD / dry:F3} m");
            Assert.True(fW / cW > fD / cD + 0.2, $"flood tolerance: wetlands {fW / cW:F2}, dry land {fD / cD:F2}");
        }
        finally { Map.Set(TestWorld.Side); }
    }

    [Fact] // peat forms from litter only where the ground is waterlogged, and drained peat decays far faster than wet peat
    public void PeatFormsOnlyInWaterloggedGround()
    {
        var w = new World(1, 1);
        float[] Li = w.Pool[(int)Pool.Litter], Wo = w.Pool[(int)Pool.Wood];
        int wet = 0, dry = 1;
        w.WetDur[wet] = 1f; w.WetDur[dry] = 0f;
        Li[wet] = Li[dry] = 1f; Wo[wet] = Wo[dry] = 0.5f;
        float litter0 = Li[wet] + Wo[wet];
        double rot = 0;
        for (int s = 0; s < 3000; s++) { rot += Wetlands.Peat(w, wet, Li, Wo, 1f); Wetlands.Peat(w, dry, Li, Wo, 1f); }
        Assert.Equal(0f, w.Peat[dry]);
        Assert.True(w.Peat[wet] > 0f);
        // mass is kept: what left the litter is the peat, less what decayed out of it
        Assert.Equal(litter0 - Li[wet] - Wo[wet], w.Peat[wet] / K.PEAT_M + (float)rot, 3);
        w.Peat[wet] = w.Peat[dry] = 1f; Li[wet] = Li[dry] = Wo[wet] = Wo[dry] = 0f;
        for (int s = 0; s < 3000; s++) { Wetlands.Peat(w, wet, Li, Wo, 1f); Wetlands.Peat(w, dry, Li, Wo, 1f); }
        Assert.True(1f - w.Peat[dry] > 20f * (1f - w.Peat[wet]), $"a year: drained peat lost {1f - w.Peat[dry]:F4} m, waterlogged {1f - w.Peat[wet]:F5} m");
        Assert.True(Wetlands.Anoxic(1f) < 0.5f * Wetlands.Anoxic(0f));
    }

    [Fact] // waterlogging stops an intolerant plant's growth and spares a tolerant one, whose tolerance costs it on drained ground
    public void FloodToleranceLetsPlantsGrowInWaterloggedGround()
    {
        float[] Ex(float tol) { var x = new float[PT.N]; Array.Fill(x, 0.5f); x[PT.FLOOD] = tol; var ph = new float[Ph.NP]; Plants.Express(x, ph); return ph; }
        var tol = Ex(1f); var intol = Ex(0f);
        Assert.True(Plants.Net(intol, 0, 0.9f, 0.6f, K.T_REF, 1f, anox: 1f) < 0f, "an intolerant plant grows in waterlogged ground");
        Assert.True(Plants.Net(tol, 0, 0.9f, 0.6f, K.T_REF, 1f, anox: 1f) > 0f, "a tolerant plant cannot grow in waterlogged ground");
        Assert.True(Plants.Net(intol, 0, 0.9f, 0.6f, K.T_REF, 1f) > Plants.Net(tol, 0, 0.9f, 0.6f, K.T_REF, 1f), "tolerance is free on drained ground");
        // the founding mid genome is unchanged by the new trait's cost (its allocation is normalised)
        var mid = Ex(0.5f);
        Assert.Equal(Plants.Net(mid, 0, 1f, 1e6f, K.T_REF, 1f), Plants.Net(mid, 0, 1f, 1e6f, K.T_REF, 1f, anox: 0f));
    }
}
