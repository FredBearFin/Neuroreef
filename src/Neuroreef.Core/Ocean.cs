// THE OCEAN (2026-11-02, the owner: "an ocean mode ... where the whole world is underwater, the evolution is all
// underwater stuff, the map is in the ocean"). Nature.Ocean (the setup's Ocean preset, --ocean) builds a world under the
// sea: the same deep-time relief as any world (Geology, the running water's passes), drowned. What changes is physics
// that a lake world does not need, and only an ocean world reads (World.Ocean); a world with land is bit for bit as before.
//
//   seafloor   the relief's own shape, ranked: the highest OCEAN_BANKS of the floor (after any islands, Nature.Islands)
//              are banks and reef flats from OCEAN_CREST to OCEAN_SHELF deep; below them it slopes down to basins
//              Map.ReliefM deep (153 m on the 256 map, 433 m on 1024, 725 m on 2048). Elevation keeps its frame:
//              SeaLevel 0.5 with 1 - SeaLevel standing for ReliefM metres, so every height and depth in metres (ElevPerM,
//              Respiration.ColumnM) reads as it does on land.
//   seawater   salinity SALINITY lowers oxygen's solubility (Weiss 1970's salinity term: ~19% at 20 °C); the mixed layer
//              is Z_MIX_SEA deep, below it the permanent thermocline cools toward T_DEEP_SEA by e per Z_THERMO_SEA; the
//              deep water keeps most of its oxygen (it sank cold at the poles), losing at most O2_LOSS_SEA of saturation
//   light      clear seawater, K_LIGHT_SEA: benthic algae grow by the light reaching the bottom; plankton by the mean
//              light of its mixed layer (Sverdrup 1953) with a Monod light response (SEA_I_IK)
//   nutrient   concentration matters at sea: growth is Monod in the mixed layer's concentration (K_N_SEA, umol/L), and
//              neighbouring water exchanges concentration, not amount. Where the bottom lies below the mixed layer,
//              E_RATIO of the plankton's deaths sinks to the bottom as detritus (marine snow), the rest is remineralised
//              in the mixed layer; what rots on that bottom goes into the deep water (World.DeepN), which the mixed layer
//              exchanges with across the thermocline (K_Z, and K_Z_CONV more when the surface is as cold as the deep:
//              winter convection). Inputs: SEA_N_IN (fixation and deposition) instead of weathering; output: the deep
//              water above C_DEEP_MAX (denitrification and burial). All of it in Nutrients.Book.
// Rivers, rain on land, lakes and ponds need land: with none they have no cells to work on (Hydrology, WaterCycle).
// All maths through MathK (NR-104).

using System;

namespace Neuroreef;

public static class Ocean
{
    /// <summary>The sea level of an ocean world's elevation frame: 1 - SEA0 is Map.ReliefM metres, so the floor (at most
    /// ReliefM deep) lies in 0..SEA0 and islands rise above it.</summary>
    public const float SEA0 = 0.5f;

    /// <summary>Nutrient units in a field cell per umol/L of nitrate over one metre of its water column (14 g/mol N).</summary>
    public static readonly float UnitsPerUmM = 14e-6f * Pools.CellM2 / Nutrients.UnitKgN;

    /// <summary>
    /// Drown the worked relief (Terrain.Shape, an ocean world): rank its elevations and map them, monotonely, onto the
    /// seascape: islands (the top Nature.Islands, as high above the sea as they stood above their own lowest shore), the
    /// banks (OCEAN_BANKS of the map, OCEAN_CREST to OCEAN_SHELF deep, or the shore to OCEAN_SHELF with islands), and the
    /// slopes and basins down to Map.ReliefM. Ridges stay ridges and valleys valleys; only the sea level moves.
    /// </summary>
    public static void Drown(World w)
    {
        int n = Map.FW * Map.FW;
        float[] e = w.Elev;
        var sorted = (float[])e.Clone();
        Array.Sort(sorted);
        float isl = Math.Clamp(Nature.Islands, 0f, 0.5f), banks = MathF.Min(K.OCEAN_BANKS, 1f - isl);
        float eMin = sorted[0], eMax = sorted[n - 1];
        float eIsl = isl > 0f ? sorted[Math.Min(n - 1, (int)((1f - isl) * n))] : eMax;
        float eBrk = sorted[Math.Clamp((int)((1f - isl - banks) * n), 0, n - 1)];
        float D = Map.ReliefM, dTop = isl > 0f ? 0f : K.OCEAN_CREST, dBrk = MathF.Min(K.OCEAN_SHELF, 0.5f * D);
        float mPerE0 = Map.ReliefM / MathF.Max(1e-6f, 1f - w.SeaLevel);   // the land's own vertical scale, for the islands
        float epm = (1f - SEA0) / Map.ReliefM;
        for (int i = 0; i < n; i++)
        {
            float v = e[i], depth;
            if (isl > 0f && v >= eIsl) { e[i] = MathF.Min(1f, SEA0 + MathF.Max(1e-5f, (v - eIsl) * mPerE0) * epm); continue; }
            if (v >= eBrk) depth = dTop + (eIsl - v) / MathF.Max(1e-9f, eIsl - eBrk) * (dBrk - dTop);
            else depth = dBrk + (eBrk - v) / MathF.Max(1e-9f, eBrk - eMin) * (D - dBrk);
            e[i] = SEA0 - MathF.Max(depth, 0.05f) * epm;
        }
        w.SeaLevel = SEA0;
        Array.Clear(w.Alluvium);   // what the rivers laid down lies under the sea now
    }

    /// <summary>The mixed layer of a water cell of an ocean world (m): Z_MIX_SEA, or the whole column where it is shallower.</summary>
    public static float MixedM(float colM) => MathF.Max(0.1f, MathF.Min(colM, K.Z_MIX_SEA));

    /// <summary>Nitrate (umol/L) of n nutrient units in h metres of a cell's water.</summary>
    public static float Umol(float n, float h) => MathF.Max(0f, n) / (UnitsPerUmM * MathF.Max(h, 0.1f));

    /// <summary>Monod limitation of growth by nitrate at n units in the mixed layer of h metres (K_N_SEA).</summary>
    public static float Lim(float n, float h) { float c = Umol(n, h); return c / (c + K.K_N_SEA); }

    /// <summary>Benthic light: the share of the surface's light reaching a bottom col metres down, with a Monod response
    /// against its value at the surface (1 at the surface).</summary>
    public static float BottomLight(float colM)
    {
        float a = K.SEA_I_IK, I = a * MathK.Exp(-K.K_LIGHT_SEA * colM);
        return I / (I + 1f) * (1f + a) / a;
    }

    /// <summary>The plankton's light: the mean over a mixed layer of h metres of a Monod response to the light there
    /// (I0 exp(-k z) / (I0 exp(-k z) + I_K): ln((1 + a) / (1 + a exp(-k h))) / (k h)), against its value at the surface.
    /// Sverdrup's (1953) mean light of the mixed layer.</summary>
    public static float MixedLight(float h)
    {
        float a = K.SEA_I_IK, k = K.K_LIGHT_SEA, kh = MathF.Max(k * h, 1e-4f);
        float mean = MathK.Log((1f + a) / (1f + a * MathK.Exp(-kh))) / kh;
        return mean * (1f + a) / a;
    }

    /// <summary>Oxygen in fresh (S = 0) or sea water in equilibrium with air at sea level (mg/L) at tC (°C) and salinity s
    /// (PSU): Weiss (1970), ln C = A1 + A2 (100/T) + A3 ln(T/100) + A4 (T/100) + S (B1 + B2 (T/100) + B3 (T/100)^2) in mL/L,
    /// x 1.429 mg/mL. At 35 PSU, 7.4 mg/L at 20 °C against 9.1 fresh.</summary>
    public static float DissolvedO2(float tC, float s)
    {
        float t = (MathF.Max(tC, -1.9f) + 273.15f) / 100f;
        float ln = -173.4292f + 249.6339f / t + 143.3483f * MathK.Log(t) - 21.8492f * t
                 + s * (-0.033096f + 0.014259f * t - 0.0017f * t * t);
        return MathK.Exp(ln) * 1.429f;
    }

    /// <summary>The sea's temperature at depth z (m) under a surface at ts: the mixed layer's, then the thermocline's.</summary>
    public static float WaterT(float ts, float z)
        => z <= K.Z_MIX_SEA ? ts : K.T_DEEP_SEA + (ts - K.T_DEEP_SEA) * MathK.Exp(-(z - K.Z_MIX_SEA) / K.Z_THERMO_SEA);

    /// <summary>The share of saturation the sea's oxygen keeps at depth z (m) under a surface at ts: all of it in the mixed
    /// layer; below, warm stratified water loses up to O2_LOSS_SEA (respiration of the sinking detritus), cold mixed none.</summary>
    public static float O2Kept(float ts, float z)
    {
        if (z <= K.Z_MIX_SEA) return 1f;
        float strat = Math.Clamp((ts - K.T_DEEP_SEA) / 16f, 0f, 1f);
        return 1f - K.O2_LOSS_SEA * strat * (1f - MathK.Exp(-(z - K.Z_MIX_SEA) / K.Z_HYPOX_SEA));
    }

    /// <summary>
    /// FIELDS, an ocean water cell's vertical nutrient exchange this step (pure, its own cell): the flux (units, + up into
    /// the mixed layer) across the thermocline between the mixed layer (nSurf in h m) and the deep water below it (deep in
    /// col - h m), by diffusion across Z_THERMO_SEA at K_Z, more under a surface as cold as the deep (K_Z_CONV).
    /// </summary>
    public static float Upwell(float nSurf, float deep, float h, float colM, float tSurf)
    {
        float hd = colM - h;
        if (hd <= 0.5f) return 0f;
        float strat = Math.Clamp((tSurf - K.T_DEEP_SEA) / 16f, 0f, 1f);
        float kz = K.K_Z + K.K_Z_CONV * (1f - strat);
        float sec = 365.25f * 86400f / K.T_SEASON;   // a reference step (Fields runs on the reference clock)
        float dz = MathF.Max(K.Z_THERMO_SEA, 0.5f * colM);
        float f = kz * sec / dz * (deep / hd - nSurf / h);   // units per m of column x m
        // never more than evens out the two layers in one step
        float even = (deep + nSurf) * h / colM - nSurf;
        return f > 0f ? MathF.Min(f, MathF.Max(0f, even)) : MathF.Max(f, MathF.Min(0f, even));
    }

    /// <summary>The nitrogen the sea gains a reference step in a cell (SEA_N_IN), in nutrient units.</summary>
    public static readonly float NIn = K.SEA_N_IN * 1e-3f * Pools.CellM2 / Nutrients.UnitKgN / K.T_SEASON;

    /// <summary>The most the deep water of a column holds (C_DEEP_MAX over col - h m), in nutrient units.</summary>
    public static float DeepMax(float colM) => K.C_DEEP_MAX * UnitsPerUmM * MathF.Max(0f, colM - MixedM(colM));

    /// <summary>A new ocean world's deep water (Terrain.BuildWater): C_DEEP0 below every mixed layer; and its surface
    /// water at the nutrient the lakes start with, as for any water.</summary>
    public static void InitDeep(World w)
    {
        int n = Map.FW * Map.FW;
        for (int i = 0; i < n; i++)
        {
            if ((w.Cell[i] & (byte)Realm.Water) == 0) { w.DeepN[i] = 0f; continue; }
            float col = Respiration.ColumnM(w, i);
            w.DeepN[i] = K.C_DEEP0 * UnitsPerUmM * MathF.Max(0f, col - MixedM(col));
        }
    }
}
