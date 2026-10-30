// Drinkable water away from the lakes and rivers (DESIGN §9.4, Revision K; the water cycle): ponds, springs and floods,
// read from the water cycle's stores (WaterCycle.cs), and the distance to drinkable water. No threshold of wetness
// decides any of them any more:
// - a pond is a pond site (a closed basin) holding at least POND_WET of standing water: it fills from the runoff of
//   its catchment and from rain, and dries by evaporation and seepage (WaterCycle.Row and Finish);
// - a spring is a cell where groundwater emerges at SPRING_MIN or more (the water table meets the ground);
// - a flood is a floodplain cell whose river carries enough more than its mean discharge for its stage to reach it
//   (Hydrology.Floodplain: the discharge ratio FloodWet). A flood also leaves on the soil part of what the river
//   water carries (the flood pulse, Junk et al. 1989).
// Frozen water is not drinkable, and a frozen river does not flood. Ponds are for drinking only: no fish live in them.
// Before WaterCycle.Init has run (inside Terrain.Build) only lakes and rivers are drinkable.

using System;

namespace Neuroreef;

public static class Ponds
{
    /// <summary>Classify every cell's drinkable water (w.Wet) from the water cycle's stores, give floods their nutrient
    /// pulse, and remeasure the distance to drinkable water when anything changed.</summary>
    public static void Update(World w)
    {
        int n = Map.FW * Map.FW;
        bool on = w.WaterOn, changed = false;
        float springMin = K.SPRING_MIN * 365f * WaterCycle.YearFrac;
        for (int i = 0; i < n; i++)
        {
            byte v = 0;
            if ((w.Cell[i] & (byte)Realm.Water) != 0) v = 1;
            else if (on && w.Temp[i] >= 0f)
            {
                if ((w.PondSite[i] != 0 && w.PondWater[i] >= K.POND_WET) || w.Seep[i] >= springMin) v = 2;
                else if (w.FloodFrom[i] >= 0)
                {
                    // the river's stage reaches the cell: FLOOD_MIX of the nutrient difference passes from the river cell
                    // to the soil, the same amount leaving one and entering the other
                    int r = w.FloodFrom[i];
                    float qm = w.DischargeMean[r];
                    if (qm > 0f && w.Discharge[r] >= w.FloodWet[i] * qm)
                    {
                        v = 3;
                        float give = K.FLOOD_MIX * MathF.Max(0f, w.Nutrient[r] - w.Nutrient[i]);
                        w.Nutrient[r] -= give; w.Nutrient[i] += give;
                    }
                }
            }
            if (w.Wet[i] != v) { w.Wet[i] = v; changed = true; }
        }
        if (changed) Terrain.Distance(w.Wet, 3, w.DistDrink);
    }

    /// <summary>Cells with drinkable standing or emerging water away from lakes and rivers (ponds and springs).</summary>
    public static int Full(World w) { int c = 0; for (int i = 0; i < Map.FW * Map.FW; i++) if (w.Wet[i] == 2) c++; return c; }

    /// <summary>Floodplain cells under water now (Revision Q).</summary>
    public static int Flooded(World w) { int c = 0; for (int i = 0; i < Map.FW * Map.FW; i++) if (w.Wet[i] == 3) c++; return c; }
}
