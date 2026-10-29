// Weather years (DESIGN §10.7). Each year (T_SEASON steps) has a rainfall anomaly that scales soil
// moisture everywhere: droughts and wet years. The anomaly is an AR(1) series, so dry years cluster.
// It is drawn from the terrain seed, so the branches of a prehistory live through the same climate.
// Also refreshes the mean tree cover used by wind seed rain (DESIGN §10.2). Serial, before FIELDS.

using System;

namespace Neuroreef;

public static class Weather
{
    public static void Update(World w)
    {
        long year = (long)(w.Step / (ulong)Nature.SeasonLength);
        while (w.RainYear < year)
        {
            w.RainYear++;
            float n = Rng.N(w.TerrainSeed, (uint)w.RainYear, 0, Rs.Weather);
            float a = w.RainYear == 0 ? K.RAIN_SD * Nature.Drought * n : K.RAIN_RHO * w.RainAnom + MathF.Sqrt(1f - K.RAIN_RHO * K.RAIN_RHO) * K.RAIN_SD * Nature.Drought * n;
            w.RainAnom = Math.Clamp(a, -K.RAIN_MAX, K.RAIN_MAX);
        }
        if (w.Step % 32 == 0 || w.TreeMean == 0)
        {
            double t = 0; int land = 0;
            for (int i = 0; i < Map.FW * Map.FW; i++) if (w.Cell[i] == (byte)Realm.Land) { t += w.Tree[i]; land++; }
            w.TreeMean = (float)(t / Math.Max(1, land));
        }
    }
}
