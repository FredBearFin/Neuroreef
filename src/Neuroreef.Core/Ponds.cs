// Seasonal ponds (DESIGN §9.4, Revision K, ecology review): rain pools in land basins in the wet season and dries out
// in the dry one and in drought years, so there is water away from the lakes part of the year, and the dry country
// between lakes can be crossed or lived in when the rains come. Ponds are for drinking only: no fish live in them.
// Rivers in flood spread over their floodplains here too (Revision Q).

using System;

namespace Neuroreef;

public static class Ponds
{
    /// <summary>Fill or dry each pond with the moisture of its cell (season x this year's rain), and remeasure the distance to drinkable water.</summary>
    public static void Update(World w)
    {
        int FW = Map.FW;
        for (int y = 0; y < FW; y++)
        {
            // how much wetter than its average the ground is now (as Fields: season by latitude, x this year's rain)
            float season = MathK.Sin(2f * MathF.PI * (float)(w.Step % (ulong)Nature.SeasonLength) / Nature.SeasonLength - 2f * MathF.PI * y / FW);
            float wet = (1f + K.A_SEASON * Nature.Seasons * season) * (1f + w.RainAnom);
            bool full = wet >= K.POND_FILL;
            if (!Climate.On)
                for (int i = y * FW; i < (y + 1) * FW; i++)
                    w.Wet[i] = (byte)((w.Cell[i] & (byte)Realm.Water) != 0 ? 1 : full && w.PondSite[i] != 0 ? 2 : 0);
            else
                // with a climate (Revision O): evaporation dries a warm pond sooner, and a frozen pond cannot be drunk
                for (int i = y * FW; i < (y + 1) * FW; i++)
                {
                    float t = w.Temp[i];
                    bool f = w.PondSite[i] != 0 && t >= 0f && wet * Climate.Evap[Climate.Idx(t)] / w.ClimateZ >= K.POND_FILL;
                    w.Wet[i] = (byte)((w.Cell[i] & (byte)Realm.Water) != 0 ? 1 : f ? 2 : 0);
                }
            // floods (Revision Q): a river's stage rises with the wetness of the season and the year; where it tops its banks
            // it spreads over the floodplain below it (Hydrology.Floodplain), which is wet and drinkable while it lasts and
            // leaves on it part of what the river water carries (the flood pulse, Junk et al. 1989): FLOOD_MIX of the
            // difference passes from the river cell to the soil, the same amount leaving one and entering the other.
            // A frozen river does not flood.
            for (int i = y * FW; i < (y + 1) * FW; i++)
                if (w.Wet[i] == 0 && wet >= w.FloodWet[i] && (!Climate.On || w.Temp[i] >= 0f))
                {
                    w.Wet[i] = 3;
                    int r = w.FloodFrom[i];
                    float give = K.FLOOD_MIX * MathF.Max(0f, w.Nutrient[r] - w.Nutrient[i]);
                    w.Nutrient[r] -= give; w.Nutrient[i] += give;
                }
        }
        Terrain.Distance(w.Wet, 3, w.DistDrink);
    }

    public static int Full(World w) { int c = 0; for (int i = 0; i < Map.FW * Map.FW; i++) if (w.Wet[i] == 2) c++; return c; }

    /// <summary>Floodplain cells under water now (Revision Q).</summary>
    public static int Flooded(World w) { int c = 0; for (int i = 0; i < Map.FW * Map.FW; i++) if (w.Wet[i] == 3) c++; return c; }
}
