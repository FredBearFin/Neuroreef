// FIELDS phase (DESIGN §10.2–10.6). Parallel over rows: every row reads the current buffers and
// writes only its own cells of the next buffers (nutrient, scent) and its own cells of the in-place pools.
// FIRE is serial (spread is directional). Water producers, decomposition, run-off, fire and scent live here;
// land plants are Plants.cs (Revision S), which runs after the swap.

using System;

namespace Neuroreef;

public static class Fields
{
    public static void Row(World w, int y)
    {
        int FW = Map.FW;
        var Pl = w.Pool;
        float[] Al = Pl[(int)Pool.Algae], Ph = Pl[(int)Pool.Phyto];
        float[] Ca = Pl[(int)Pool.Carrion], Du = Pl[(int)Pool.Dung], Li = Pl[(int)Pool.Litter], Wo = Pl[(int)Pool.Wood], De = Pl[(int)Pool.Detritus];
        float season = MathK.Sin(2f * MathF.PI * (float)(w.Step % (ulong)Nature.SeasonLength) / Nature.SeasonLength - 2f * MathF.PI * y / FW);
        int yu = World.WrapI(y - 1, FW) * FW, yd = World.WrapI(y + 1, FW) * FW, row = y * FW;
        // climate (Revision O): this row's temperature for this step; with Climate = 0 every factor below is skipped
        bool clim = Climate.On;
        float ta = 0f, tb = 0f;
        if (clim) Climate.RowParams(w, y, out ta, out tb);
        float[] Tmp = w.Temp;
        for (int x = 0; x < FW; x++)
        {
            int i = row + x;
            float rC = K.ROT_CARRION, rD = K.ROT_DUNG, rL = K.ROT_LITTER, rW = K.ROT_WOOD, rT = K.ROT_DETRITUS;
            int ti = 0; float temp = K.T_REF;
            if (clim)
            {
                temp = MathF.FusedMultiplyAdd(tb, w.Cont[i], ta) - w.TLapse[i];
                Tmp[i] = temp;
                bool water = w.Cell[i] != (byte)Realm.Land;
                ti = Climate.Idx(water ? Climate.Water(temp) : temp);
                // decomposition speeds up with warmth (Q10 = 2, Lloyd & Taylor 1994)
                float q = Climate.Rot[ti];
                rC *= q; rD *= q; rL *= q; rW *= q; rT *= q;
            }
            int xl = row + (x == 0 ? FW - 1 : x - 1), xr = row + (x == FW - 1 ? 0 : x + 1), up = yu + x, dn = yd + x;
            float N = w.Nutrient[i];

            // nutrient transport: water cells mix with water neighbours; each land-water pair carries run-off
            // from the land cell into the water cell (the same amount leaves one and enters the other)
            float Nn = N;
            bool wet = w.Cell[i] != (byte)Realm.Land;
            Nn += Transport(w, i, xl, wet) + Transport(w, i, xr, wet) + Transport(w, i, up, wet) + Transport(w, i, dn, wet);
            float sc = w.Scent[i], sb = 0.25f * (w.Scent[xl] + w.Scent[xr] + w.Scent[up] + w.Scent[dn]);
            w.ScentN[i] = MathF.Max(0f, (sc + 0.1f * (sb - sc)) * (1f - K.D_SCENT));

            // decomposition by bacteria: every dead pool rots into nutrient
            float rot = Ca[i] * rC + Du[i] * rD + Li[i] * rL + Wo[i] * rW + De[i] * rT;
            Ca[i] -= Ca[i] * rC; Du[i] -= Du[i] * rD; Li[i] -= Li[i] * rL; Wo[i] -= Wo[i] * rW; De[i] -= De[i] * rT;
            Nn += rot * K.C_NUTGAIN + K.WEATHER * w.Fert[i] * 0.1f;
            w.Poll[i] *= 1f - K.D_POLL;

            if (w.Cell[i] != (byte)Realm.Land)
            {
                // water producers (DESIGN §10.3)
                float lim = N / (N + K.K_N);
                float light = MathK.Exp(-w.Depth[i] / K.SHALLOW);
                float ga = (K.R_A * Nature.Plants * lim * Al[i] * (1f - Al[i]) + 0.0005f * lim) * light;
                float gp = K.R_PH * Nature.Plants * lim * (1f + 0.5f * season) * (Ph[i] + 0.002f) * (1f - Ph[i]);
                if (clim)
                {
                    // warmer water grows plankton faster (Eppley 1972); under ice nothing grows
                    float fe = temp < 0f ? 0f : Climate.Algae[ti];
                    ga *= fe; gp *= fe;
                }
                float da = K.M_A * Al[i], dp = K.M_PH * Ph[i];
                Al[i] = Math.Clamp(Al[i] + ga - da, 0f, 1f);
                Ph[i] = Math.Clamp(Ph[i] + gp - dp, 0f, 1f);
                De[i] += da + dp;
                Nn -= (ga + gp) * K.C_NUTUSE;
                w.NutrientN[i] = Math.Clamp(Nn, 0f, K.N_MAX);
                continue;
            }

            // land: the plants (Plants.cs) take their nutrient after the swap
            w.NutrientN[i] = Math.Clamp(Nn, 0f, K.N_MAX);
        }
    }

    /// <summary>
    /// The energy per step that an animal with this diet could take from cell i, year in and year out, without eating the
    /// plants down: each pool's growth from Row at the stock where it grows fastest (half of the room it can fill, the
    /// maximum sustained yield of logistic growth, Schaefer 1954), seasonal pools at their yearly mean (1/pi of the
    /// peak), litter and detritus at their inflow; times the pool's energy and the diet's efficiency. Carrion, dung and
    /// meat come from animals and are not counted. Used to found each lineage with what its home range can feed.
    /// </summary>
    public static float Yield(World w, int i, ReadOnlySpan<float> diet)
    {
        var Pl = w.Pool;
        float N = w.Nutrient[i], plants = Nature.Plants;
        float temp = K.T_REF;
        bool clim = Climate.On;
        if (clim)
        {
            Climate.RowParams(w, i / Map.FW, out float ta, out float tb);
            temp = MathF.FusedMultiplyAdd(tb, w.Cont[i], ta) - w.TLapse[i];
        }
        float y = 0f;
        if (w.Cell[i] != (byte)Realm.Land)
        {
            float lim = N / (N + K.K_N);
            float fe = clim ? (temp < 0f ? 0f : Climate.Algae[Climate.Idx(Climate.Water(temp))]) : 1f;
            float al = (K.R_A * plants * lim * 0.25f + 0.0005f * lim) * fe * MathK.Exp(-w.Depth[i] / K.SHALLOW);
            float ph = K.R_PH * plants * lim * 0.25f * fe;
            y += diet[(int)Pool.Algae] * Pools.Rho[(int)Pool.Algae] * al;
            y += diet[(int)Pool.Phyto] * Pools.Rho[(int)Pool.Phyto] * ph;
            y += diet[(int)Pool.Detritus] * Pools.Rho[(int)Pool.Detritus] * (K.M_A * Pl[(int)Pool.Algae][i] + K.M_PH * Pl[(int)Pool.Phyto][i]);
            return y;
        }
        return Plants.Yield(w, i, diet);
    }

    /// <summary>Is this a pool that Yield counts (it grows from plants, or from their fall)?</summary>
    public static bool Grows(int p) => p <= (int)Pool.Phyto || p == (int)Pool.Litter || p == (int)Pool.Detritus;

    /// <summary>Nutrient change of cell i due to its neighbour j (reads only the current buffer).</summary>
    static float Transport(World w, int i, int j, bool iWet)
    {
        bool jWet = w.Cell[j] != (byte)Realm.Land;
        if (iWet && jWet) return 0.25f * K.N_DIFF * (w.Nutrient[j] - w.Nutrient[i]);
        if (iWet) return K.RUNOFF * w.Nutrient[j];          // land neighbour washes into me
        if (jWet) return -K.RUNOFF * w.Nutrient[i];         // I wash into the water neighbour
        return 0f;                                         // soil does not flow
    }

    /// <summary>Swap the double buffers after all rows are done.</summary>
    public static void Swap(World w)
    {
        (w.Nutrient, w.NutrientN) = (w.NutrientN, w.Nutrient);
        (w.Scent, w.ScentN) = (w.ScentN, w.Scent);
    }

    /// <summary>FIRE phase (DESIGN §10.5). Serial; reads Fire, writes FireN, then swaps.</summary>
    public static void Fire(World w)
    {
        int FW = Map.FW, n = FW * FW;
        uint seed = w.Seed, step = (uint)w.Step;
        var Pl = w.Pool;
        float[] F0 = w.Fire, F1 = w.FireN;
        for (int i = 0; i < n; i++) F1[i] = MathF.Max(0f, F0[i] - 1f);
        for (int i = 0; i < n; i++)
        {
            if (w.Cell[i] != (byte)Realm.Land) continue;
            float fuel = Fuel(w, i);
            if (F0[i] > 0)
            {
                int x = i % FW, y = i / FW;
                for (int k = 0; k < 4; k++)
                {
                    int j = k switch
                    {
                        0 => y * FW + (x == 0 ? FW - 1 : x - 1),
                        1 => y * FW + (x == FW - 1 ? 0 : x + 1),
                        2 => World.WrapI(y - 1, FW) * FW + x,
                        _ => World.WrapI(y + 1, FW) * FW + x,
                    };
                    if (F0[j] > 0 || F1[j] > 0 || w.Cell[j] != (byte)Realm.Land) continue;
                    float fj = Fuel(w, j);
                    if (fj < K.FUEL_MIN) continue;
                    if (Rng.U(seed, step, (uint)j, Rs.Fire + (uint)k) < K.C_SPREAD * fj * (1f - w.Moisture[j])) Ignite(w, j, F1);
                }
                continue;
            }
            if (fuel < K.FUEL_MIN) continue;
            float dryness = 1f - w.Moisture[i];
            if (Rng.U(seed, step, (uint)i, Rs.Fire) < K.LAMBDA_FIRE * Nature.Fire * (9000f / Nature.SeasonLength) * fuel * dryness * dryness) Ignite(w, i, F1);
        }
        w.FireSwap();
    }

    static float Fuel(World w, int i)
        => (w.Pool[(int)Pool.Grass][i] + w.Shrub[i] + w.Pool[(int)Pool.Litter][i]) * (1f - w.Moisture[i]);

    static void Ignite(World w, int i, float[] F1)
    {
        F1[i] = K.FIRE_STEPS;
        // the plants burn as their traits let them (Plants.Burn: resprouting, bark, height over the flames)
        float burned = Plants.Burn(w, i, Fuel(w, i));
        w.Nutrient[i] = MathF.Min(K.N_MAX, w.Nutrient[i] + K.C_ASH * burned * 0.2f);
        w.T.FireCells++;
    }
}
