// FIELDS phase (DESIGN §10.2–10.6). Parallel over rows: every row reads the current buffers and
// writes only its own cells of the next buffers (grass, shrub, tree, sapling, nutrient, scent) and its
// own cells of the in-place pools. FIRE is serial (spread is directional). Producers, decomposition,
// run-off, fire and scent live here.

using System;

namespace Neuroreef;

public static class Fields
{
    public static void Row(World w, int y)
    {
        int FW = Map.FW;
        var Pl = w.Pool;
        float[] G = Pl[(int)Pool.Grass], GN = w.GrassN;
        float[] Br = Pl[(int)Pool.Browse], Fr = Pl[(int)Pool.Fruit], Sd = Pl[(int)Pool.Seed], Nc = Pl[(int)Pool.Nectar], Rt = Pl[(int)Pool.Root];
        float[] Al = Pl[(int)Pool.Algae], Ph = Pl[(int)Pool.Phyto];
        float[] Ca = Pl[(int)Pool.Carrion], Du = Pl[(int)Pool.Dung], Li = Pl[(int)Pool.Litter], Wo = Pl[(int)Pool.Wood], De = Pl[(int)Pool.Detritus];
        float season = MathK.Sin(2f * MathF.PI * (float)(w.Step % (ulong)Nature.SeasonLength) / Nature.SeasonLength - 2f * MathF.PI * y / FW);
        int yu = World.WrapI(y - 1, FW) * FW, yd = World.WrapI(y + 1, FW) * FW, row = y * FW;
        for (int x = 0; x < FW; x++)
        {
            int i = row + x;
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
            float rot = Ca[i] * K.ROT_CARRION + Du[i] * K.ROT_DUNG + Li[i] * K.ROT_LITTER + Wo[i] * K.ROT_WOOD + De[i] * K.ROT_DETRITUS;
            Ca[i] -= Ca[i] * K.ROT_CARRION; Du[i] -= Du[i] * K.ROT_DUNG; Li[i] -= Li[i] * K.ROT_LITTER; Wo[i] -= Wo[i] * K.ROT_WOOD; De[i] -= De[i] * K.ROT_DETRITUS;
            Nn += rot * K.C_NUTGAIN + K.WEATHER * w.Fert[i] * 0.1f;
            w.Poll[i] *= 1f - K.D_POLL;

            if (w.Cell[i] != (byte)Realm.Land)
            {
                // water producers (DESIGN §10.3)
                float lim = N / (N + K.K_N);
                float ga = (w.Cell[i] == (byte)Realm.Shallow) ? K.R_A * Nature.Plants * lim * Al[i] * (1f - Al[i]) + 0.0005f * lim : 0f;
                float gp = K.R_PH * Nature.Plants * lim * (1f + 0.5f * season) * (Ph[i] + 0.002f) * (1f - Ph[i]);
                float da = K.M_A * Al[i], dp = K.M_PH * Ph[i];
                Al[i] = Math.Clamp(Al[i] + ga - da, 0f, 1f);
                Ph[i] = Math.Clamp(Ph[i] + gp - dp, 0f, 1f);
                De[i] += da + dp;
                Nn -= (ga + gp) * K.C_NUTUSE;
                w.NutrientN[i] = Math.Clamp(Nn, 0f, K.N_MAX);
                GN[i] = 0; w.ShrubN[i] = 0; w.TreeN[i] = 0; w.SaplingN[i] = 0;
                continue;
            }

            // land producers (DESIGN §10.2)
            float W = Math.Clamp(w.MoistBase[i] * (1f + K.A_SEASON * Nature.Seasons * season) * (1f + w.RainAnom), 0f, 1f);
            w.Moisture[i] = W;
            float nut = N / (N + K.K_N);
            float grow = W * nut;
            // water limits each layer differently: grass grows at any moisture, shrubs above W_SHRUB, trees above W_TREE
            float growS = MathF.Max(0f, (W - K.W_SHRUB) / (1f - K.W_SHRUB)) * nut;
            float growT = MathF.Max(0f, (W - K.W_TREE) / (1f - K.W_TREE)) * nut;
            float g = G[i], s = w.Shrub[i], t = w.Tree[i], p = w.Sapling[i];
            float lightG = 1f - 0.85f * t - 0.35f * s, lightS = 1f - 0.70f * t;
            float free = MathF.Max(0f, 1f - g - s - t);
            float gbar = 0.25f * (G[xl] + G[xr] + G[up] + G[dn]);
            float tbar = 0.25f * (w.Tree[xl] + w.Tree[xr] + w.Tree[up] + w.Tree[dn]);
            float dG = K.R_G * Nature.Plants * grow * lightG * (g + K.S_G * Sd[i] + K.G_RAIN + K.SIGMA_G * MathF.Max(0f, gbar - g)) * free;
            float dS = K.R_S * Nature.Plants * growS * lightS * (s + K.S_S * g) * MathF.Max(0f, 1f - s - t);   // shrubs overtop grass
            // saplings: seed from nearby trees and, rarely, from far away by wind; grass roots take their water
            float seedSrc = (tbar + t) * (0.2f + s) + K.WIND_SEED / K.R_P * w.TreeMean;
            float dP = K.R_P * Nature.Plants * growT * (1f - K.GRASS_COMP * g) * seedSrc * MathF.Max(0f, 1f - p - t) - K.M_P * p;
            float mature = K.M_P * p * (1f - t);
            float drought = 1f + K.DROUGHT_T * MathF.Max(0f, K.W_TREE - W) / K.W_TREE;
            float dT = mature + K.R_T * Nature.Plants * growT * t * (1f - t) - K.D_T * drought * t;
            float shadeG = K.SH_G * t * g, shadeS = K.SH_S * t * s;
            float gn = Math.Clamp(g + dG - shadeG - K.C_LIT * g, 0f, 1f);
            float sn = Math.Clamp(s + dS - shadeS - K.C_LIT * 0.5f * s, 0f, 1f);
            float tn = Math.Clamp(t + dT, 0f, 1f);
            // light competition: trees overtop shrubs, shrubs overtop grass; the lowest layer gives way
            if (tn > 1f) tn = 1f;
            if (sn > 1f - tn) sn = 1f - tn;
            if (gn > 1f - tn - sn) gn = MathF.Max(0f, 1f - tn - sn);
            GN[i] = gn; w.ShrubN[i] = sn; w.TreeN[i] = tn;
            w.SaplingN[i] = Math.Clamp(p + dP, 0f, 1f);
            Wo[i] += K.D_T * drought * t;
            Li[i] += K.C_LIT * (g + 0.5f * s + 0.3f * t) + shadeG + shadeS;

            // food pools produced by the vegetation
            float wetS = MathF.Max(season, 0f), dry = MathF.Max(-season, 0f), poll = w.Poll[i];
            float bcap = s + K.B_TREE * t;
            Br[i] = MathF.Max(0f, Br[i] + K.R_B * Nature.Plants * grow * (bcap - Br[i]));
            float fr = K.R_F * Nature.Plants * t * wetS * (1f + poll) * grow;
            Fr[i] += fr - K.D_F * Fr[i]; Li[i] += K.D_F * Fr[i];
            float sd = K.R_SD * Nature.Plants * (g + 0.5f * t) * dry * (1f + poll) * (0.3f + grow);
            Sd[i] = MathF.Max(0f, Sd[i] + sd - K.D_SD * Sd[i]);
            Nc[i] = MathF.Max(0f, Nc[i] + K.R_NC * Nature.Plants * (0.5f * g + s) * wetS * grow - K.D_NC * Nc[i]);
            Rt[i] = Math.Clamp(Rt[i] + K.R_R * Nature.Plants * (g + s) * grow * (1f - Rt[i]), 0f, 1f);

            Nn -= (MathF.Max(0f, dG) + MathF.Max(0f, dS) + MathF.Max(0f, dT) + fr + sd) * K.C_NUTUSE;
            w.NutrientN[i] = Math.Clamp(Nn, 0f, K.N_MAX);
        }
    }

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
        (w.Pool[(int)Pool.Grass], w.GrassN) = (w.GrassN, w.Pool[(int)Pool.Grass]);
        (w.Shrub, w.ShrubN) = (w.ShrubN, w.Shrub);
        (w.Tree, w.TreeN) = (w.TreeN, w.Tree);
        (w.Sapling, w.SaplingN) = (w.SaplingN, w.Sapling);
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
        var Pl = w.Pool;
        F1[i] = K.FIRE_STEPS;
        // above-ground parts burn; grasses and shrubs keep their rootstock and resprout (RESPROUT)
        float g = Pl[(int)Pool.Grass][i], sh = w.Shrub[i];
        float burned = g * (1f - K.RESPROUT) + sh * (1f - K.RESPROUT) + w.Sapling[i] + Pl[(int)Pool.Browse][i] + Pl[(int)Pool.Litter][i]
                     + Pl[(int)Pool.Nectar][i] + Pl[(int)Pool.Seed][i] + 0.4f * w.Tree[i];
        Pl[(int)Pool.Grass][i] = g * K.RESPROUT; w.Shrub[i] = sh * K.RESPROUT; w.Sapling[i] = 0; Pl[(int)Pool.Browse][i] = 0; Pl[(int)Pool.Litter][i] = 0;
        Pl[(int)Pool.Nectar][i] = 0; Pl[(int)Pool.Seed][i] = 0;
        Pl[(int)Pool.Wood][i] += 0.4f * w.Tree[i] * 0.5f;
        w.Tree[i] *= 0.6f;
        Pl[(int)Pool.Root][i] *= 0.7f;
        w.Nutrient[i] = MathF.Min(K.N_MAX, w.Nutrient[i] + K.C_ASH * burned * 0.2f);
        w.T.FireCells++;
    }
}
