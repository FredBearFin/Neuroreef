// Seasonal ponds (DESIGN §9.4, Revision K, ecology review): rain pools in land basins in the wet season and dries out
// in the dry one and in drought years, so there is water away from the lakes part of the year, and the dry country
// between lakes can be crossed or lived in when the rains come. Ponds are for drinking only: no fish live in them.

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
            bool full = (1f + K.A_SEASON * Nature.Seasons * season) * (1f + w.RainAnom) >= K.POND_FILL;
            for (int i = y * FW; i < (y + 1) * FW; i++)
                w.Wet[i] = (byte)((w.Cell[i] & (byte)Realm.Water) != 0 ? 1 : full && w.PondSite[i] != 0 ? 2 : 0);
        }
        Terrain.Distance(w.Wet, 3, w.DistDrink);
    }

    public static int Full(World w) { int c = 0; for (int i = 0; i < Map.FW * Map.FW; i++) if (w.Wet[i] == 2) c++; return c; }
}
