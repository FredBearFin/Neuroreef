// Climate and body heat (DESIGN Revision O). Nothing here names a biome or gives an animal a tolerance: the air
// temperature of each cell follows from latitude, season, height and the nearness of water, and the plants, the soil
// and each animal's heat budget answer to that temperature through their physics. Where a forest, a tundra or a
// desert ends up, and what body lives there, is what follows.
//
// Temperature (°C), per cell, written by FIELDS each step:
//   T_phys = T_EQ - DT_EP sin²θ + A_SEAS sinθ sin(2π t / year) Cont - LAPSE h,   θ = 2π y / FW (latitude on the torus:
//   equator, north pole, equator, south pole; the hemispheres' seasons are opposite by the sign of sinθ)
//   T = T_REF + Climate (T_phys - T_REF)
// There is no daily cycle: a step is hours, and the day-averaged sun is in SOLAR.
//
// Body heat: every animal is an endotherm at T_BODY with no heat store (a step is longer than a body's thermal time
// constant). Its dry conductance follows its core area M^(2/3), its fur and its appendages; it makes heat (basal +
// movement), shivers (up to SUMMIT x basal) when it loses more than it makes, and evaporates water (up to EVAP_MAX x
// basal, which makes it thirsty) when it cannot lose enough. What it cannot cover becomes heat strain, and strain kills.

using System;

namespace Neuroreef;

public static class Climate
{
    // ---- response tables: TT_N entries of TT_STEP from TT_LO (nearest entry)
    /// <summary>(e_s(T) / e_s(T_REF))^-EVAP_BETA (Tetens saturation vapour pressure): how evaporation dries the soil.</summary>
    public static readonly float[] Evap = Table(t => MathF.Pow(Es(t) / Es(K.T_REF), -K.EVAP_BETA));
    /// <summary>Yan & Hunt (1999) growth curves of grass, shrubs and trees, 1 at T_REF, at most PLANT_CAP; 0 outside (Tmin, Tmax).</summary>
    public static readonly float[] FGrass = Beta(K.GRASS_TMIN, K.GRASS_TOPT, K.GRASS_TMAX),
        FShrub = Beta(K.SHRUB_TMIN, K.SHRUB_TOPT, K.SHRUB_TMAX), FTree = Beta(K.TREE_TMIN, K.TREE_TOPT, K.TREE_TMAX);
    /// <summary>Algae and plankton growth x EPPLEY^(T - T_REF), at the water's temperature (Eppley 1972).</summary>
    public static readonly float[] Algae = Table(t => MathF.Pow(K.EPPLEY, MathF.Max(t, K.T_WATER_MIN) - K.T_REF));
    /// <summary>Decomposition x Q10_ROT^((T - T_REF) / 10) (Lloyd & Taylor 1994).</summary>
    public static readonly float[] Rot = Table(t => MathF.Pow(K.Q10_ROT, (t - K.T_REF) / 10f));

    internal static float Es(float t) => 0.6108f * MathF.Exp(17.27f * t / (t + 237.3f));

    static float[] Table(Func<float, float> f)
    {
        var a = new float[K.TT_N];
        for (int k = 0; k < K.TT_N; k++) a[k] = f(K.TT_LO + k * K.TT_STEP);
        return a;
    }

    static float[] Beta(float lo, float opt, float hi)
    {
        double F(double t) => t <= lo || t >= hi ? 0 : (hi - t) / (hi - opt) * Math.Pow((t - lo) / (opt - lo), (opt - lo) / (hi - opt));
        double r = F(K.T_REF);
        return Table(t => (float)Math.Min(K.PLANT_CAP, F(t) / r));
    }

    /// <summary>The table entry of a temperature.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static int Idx(float t)
    {
        int k = (int)((t - K.TT_LO) * (1f / K.TT_STEP) + 0.5f);
        return k < 0 ? 0 : k >= K.TT_N ? K.TT_N - 1 : k;
    }

    /// <summary>Is the climate on? (Climate = 0 is the world without it, bit for bit: every rule below is skipped.)</summary>
    public static bool On => Nature.Climate > 0f;

    /// <summary>Row constants of the temperature: T = a + b x Cont - TLapse (TLapse already holds Climate x LAPSE x height).</summary>
    public static void RowParams(World w, int y, out float a, out float b)
    {
        float s = MathF.Sin(2f * MathF.PI * y / Map.FW), c = Nature.Climate;
        float year = MathF.Sin(2f * MathF.PI * (float)(w.Step % (ulong)Nature.SeasonLength) / Nature.SeasonLength);
        a = K.T_REF + c * (K.T_EQ - K.DT_EP * s * s - K.T_REF);
        b = c * K.A_SEAS * s * year;
    }

    /// <summary>Write the temperature of row y for the current step.</summary>
    public static void TempRow(World w, int y)
    {
        RowParams(w, y, out float a, out float b);
        float[] T = w.Temp, C = w.Cont, L = w.TLapse;
        for (int i = y * Map.FW, e = i + Map.FW; i < e; i++) T[i] = MathF.FusedMultiplyAdd(b, C[i], a) - L[i];
    }

    /// <summary>Terrain.Build: height and continentality of every cell and this step's temperature.</summary>
    public static void Build(World w)
    {
        int n = Map.FW * Map.FW;
        if (!On) { Array.Fill(w.Temp, K.T_REF); Array.Clear(w.TLapse); Array.Fill(w.Cont, 1f); w.ClimateZ = 1f; return; }
        float sea = w.SeaLevel;
        for (int i = 0; i < n; i++)
        {
            float h = MathF.Max(0f, w.Elev[i] - sea) / (1f - sea) * K.H_RELIEF;      // m above the lakes
            w.TLapse[i] = Nature.Climate * K.LAPSE * h * 0.001f;
            w.Cont[i] = 1f - K.WATER_DAMP * MathF.Exp(-w.DistWater[i] / K.L_MAR);
        }
        for (int y = 0; y < Map.FW; y++) TempRow(w, y);
    }

    /// <summary>
    /// Where rain falls (Revision P), relative to the land's mean (1): air takes up water over lakes and seas, the
    /// prevailing wind of its latitude carries it over land, and it rains out as it goes, the more where the ground
    /// rises under it (orographic rain: wet windward slopes, dry rain shadows behind) and less far inland; the land
    /// gives some back (RAIN_RECYCLE). The belts of the general circulation set how much falls at each latitude: wet
    /// where air rises at the equator and near 55°, dry where it sinks near 30° and at the poles (Hadley, Ferrel and
    /// polar cells; trade winds from the east below 30°, westerlies to 60°, polar easterlies above). Rows are
    /// latitudes on the torus; each row is marched twice around so the air arriving at its start is already settled.
    /// </summary>
    public static void Rainfall(World w, float[] rain)
    {
        int FW = Map.FW;
        float sea = w.SeaLevel;
        double sum = 0; int nLand = 0;
        for (int y = 0; y < FW; y++)
        {
            float s = MathF.Abs(MathF.Sin(2f * MathF.PI * y / FW));
            float lat = MathF.Asin(MathF.Min(1f, s)) * (180f / MathF.PI);
            int dir = lat < 30f ? -1 : lat < 60f ? 1 : -1;
            float belt = K.RAIN_DRY + (1f - K.RAIN_DRY) * MathF.Exp(-lat * lat / (K.RAIN_BELT * K.RAIN_BELT))
                       + K.RAIN_MID * MathF.Exp(-(lat - 55f) * (lat - 55f) / (K.RAIN_BELT * K.RAIN_BELT));
            float q = 0f; int x = 0; float hPrev = 0f;
            for (int k = 0; k < 2 * FW; k++, x = World.WrapI(x + dir, FW))
            {
                int i = y * FW + x;
                float h = MathF.Max(0f, w.Elev[i] - sea) / (1f - sea);
                if (w.Cell[i] != (byte)Realm.Land) { q += (1f - q) * K.RAIN_EVAP; hPrev = 0f; continue; }
                float lift = MathF.Max(0f, h - hPrev) * K.RAIN_OROG;
                float r = MathF.Min(q, q * (1f / K.RAIN_L + lift));
                q += (1f - q) * K.RAIN_RECYCLE - r;
                hPrev = h;
                if (k >= FW) { rain[i] = r * belt; sum += rain[i]; nLand++; }
            }
        }
        float mean = nLand > 0 ? (float)(sum / nLand) : 1f;
        for (int i = 0; i < FW * FW; i++) rain[i] = w.Cell[i] == (byte)Realm.Land && mean > 0 ? rain[i] / mean : 0f;
    }

    /// <summary>Terrain.Build, once the moisture base is known: the evaporation normaliser Z. reference: the moisture base the
    /// land would have with no climate (before Rainfall moved it), whose yearly total the climate keeps.</summary>
    public static void Normalise(World w, float[]? reference = null)
    {
        int n = Map.FW * Map.FW;
        if (!On) { w.ClimateZ = 1f; return; }
        // Z: the evaporation factor's normaliser, chosen so that the land as a whole is as moist over a year as it is with
        // no climate (soil moisture capped at 1 included): climate moves water around the map, it does not add or remove
        // it. The year is sampled at PH phases of the moisture and temperature seasons; Z is found by bisection.
        const int PH = 32;   // phases of the year (8 were too few once rain made some soils swing against the cap of 1)
        int FW = Map.FW, landN = 0;
        for (int i = 0; i < n; i++) if (w.Cell[i] == (byte)Realm.Land) landN++;
        if (landN == 0) { w.ClimateZ = 1f; return; }
        var mb = new float[landN * PH]; var ev = new float[landN * PH];
        double target = 0; int q = 0;
        for (int y = 0; y < FW; y++)
        {
            float s = MathF.Sin(2f * MathF.PI * y / FW);
            float a = K.T_REF + Nature.Climate * (K.T_EQ - K.DT_EP * s * s - K.T_REF);
            for (int i = y * FW, e = i + FW; i < e; i++)
            {
                if (w.Cell[i] != (byte)Realm.Land) continue;
                for (int p = 0; p < PH; p++)
                {
                    float ph = 2f * MathF.PI * (p + 0.5f) / PH;
                    float season = MathF.Sin(ph - 2f * MathF.PI * y / FW);                 // as Fields: the moisture season by latitude
                    float b = Nature.Climate * K.A_SEAS * s * MathF.Sin(ph);
                    float m = w.MoistBase[i] * (1f + K.A_SEASON * Nature.Seasons * season);
                    mb[q] = m; ev[q] = Evap[Idx(MathF.FusedMultiplyAdd(b, w.Cont[i], a) - w.TLapse[i])];
                    float m0 = reference == null ? m : reference[i] * (1f + K.A_SEASON * Nature.Seasons * season);
                    target += Math.Clamp(m0, 0f, 1f); q++;
                }
            }
        }
        // wet and dry years (Weather: the year's rain anomaly, SD RAIN_SD x Drought) at one SD either way, with and without
        // the climate alike: against the cap of 1 they do not cancel, and more so where rain gathers the water
        float sd = K.RAIN_SD * Nature.Drought;
        target = 0; q = 0;
        for (int y = 0; y < FW; y++)
            for (int i = y * FW, e = i + FW; i < e; i++)
            {
                if (w.Cell[i] != (byte)Realm.Land) continue;
                for (int p = 0; p < PH; p++, q++)
                {
                    float season = MathF.Sin(2f * MathF.PI * (p + 0.5f) / PH - 2f * MathF.PI * y / FW);
                    float m0 = (reference == null ? w.MoistBase[i] : reference[i]) * (1f + K.A_SEASON * Nature.Seasons * season);
                    target += 0.5 * (Math.Clamp(m0 * (1f - sd), 0f, 1f) + Math.Clamp(m0 * (1f + sd), 0f, 1f));
                }
            }
        double Mean(double z)
        {
            double t = 0;
            for (int k = 0; k < q; k++) { double x = mb[k] * ev[k] / z; t += 0.5 * (Math.Clamp(x * (1 - sd), 0.0, 1.0) + Math.Clamp(x * (1 + sd), 0.0, 1.0)); }
            return t;
        }
        double lo = 0.05, hi = 20;
        for (int it = 0; it < 50; it++) { double mid = Math.Sqrt(lo * hi); if (Mean(mid) > target) lo = mid; else hi = mid; }
        w.ClimateZ = (float)Math.Sqrt(lo * hi);
    }

    /// <summary>Annual mean air temperature of cell i (the season averages out).</summary>
    public static float AnnualMean(World w, int i)
    {
        if (!On) return K.T_REF;
        float s = MathF.Sin(2f * MathF.PI * (i / Map.FW) / Map.FW);
        return K.T_REF + Nature.Climate * (K.T_EQ - K.DT_EP * s * s - K.T_REF) - w.TLapse[i];
    }

    /// <summary>Water temperature under air temperature t.</summary>
    public static float Water(float t) => MathF.Max(t, K.T_WATER_MIN);

    /// <summary>Range and land mean of the temperature now (reporting).</summary>
    public static (float min, float max, float mean) LandRange(World w)
    {
        float mn = float.MaxValue, mx = float.MinValue; double s = 0; int n = 0;
        for (int i = 0; i < Map.FW * Map.FW; i++)
        {
            if (w.Cell[i] != (byte)Realm.Land) continue;
            float t = w.Temp[i]; mn = MathF.Min(mn, t); mx = MathF.Max(mx, t); s += t; n++;
        }
        return n == 0 ? (0, 0, 0) : (mn, mx, (float)(s / n));
    }
}

/// <summary>An endotherm's heat budget (Revision O): the physics of its body against the operative temperature.</summary>
public static class Heat
{
    /// <summary>
    /// Operative temperature (°C) of an animal with fur ins in a cell: the air (or the water, when immersed) plus, out of
    /// water, the day-averaged sun on what the canopy leaves open; fur keeps part of the sun's heat out.
    /// </summary>
    public static float Operative(World w, int cell, float ins, bool immersed)
    {
        float t = w.Temp[cell];
        if (immersed) return Climate.Water(t);
        float open = w.Cell[cell] == (byte)Realm.Land ? 1f - K.SHADE_TREE * w.Tree[cell] - K.SHADE_SHRUB * w.Shrub[cell] : 1f;
        return t + Nature.Climate * K.SOLAR * open / (1f + 0.5f * K.R_INS * ins);
    }

    /// <summary>Dry conductance of a body (energy per step per K): least (cold, vasoconstricted) and most (warm, vasodilated).</summary>
    public static void Conductance(float mass, float ins, float app, bool immersed, out float cmin, out float cmax)
    {
        float c = MathK.Cbrt(mass), ac = c * c;
        float ie = ins * MathF.Min(1f, c);                 // a small body cannot carry a thick coat
        float kw = 1f;
        if (immersed) { ie *= K.FUR_WET; kw = K.K_WATER; }
        float core = ac / (1f + K.R_INS * ie);
        float g = kw * K.K_COND * K.C_BASAL;
        cmin = g * (core + K.APP_LEAK * app * ac);
        cmax = g * (K.VASO * core + app * ac);
    }

    /// <summary>
    /// The heat budget for one step. h: heat the body makes (basal + movement); basal: its full basal, the unit of the
    /// caps. Returns through out: heat it must make on top (thermogenesis, charged to energy), heat it must evaporate
    /// (none under water), and the shortfalls beyond the caps (cold, hot).
    /// </summary>
    public static void Balance(float mass, float ins, float app, float top, bool immersed, float h, float basal,
        out float thermo, out float evap, out float cold, out float hot, float core = K.T_BODY)
    {
        Conductance(mass, ins, app, immersed, out float cmin, out float cmax);
        float dT = core - top;
        thermo = 0; evap = 0; cold = 0; hot = 0;
        float lossMin = cmin * dT;
        if (lossMin > h) { float need = lossMin - h; thermo = MathF.Min(need, K.SUMMIT * basal); cold = need - thermo; return; }
        float lossMax = cmax * dT;
        if (lossMax < h) { float need = h - lossMax; evap = immersed ? 0f : MathF.Min(need, K.EVAP_MAX * basal); hot = need - evap; }
    }

    /// <summary>
    /// Core temperature (°C) of a body with endothermy e at operative temperature top (Revision P): a warm-blooded body
    /// (e = 1) keeps T_BODY, a cold-blooded one (e = 0) takes its surroundings' temperature, others between. Above
    /// T_BODY every body is as warm as its surroundings would make it, and must shed heat as an endotherm does.
    /// </summary>
    public static float Core(float top, float e) => top >= K.T_BODY ? K.T_BODY : top + e * (K.T_BODY - top);

    /// <summary>
    /// How fast a body's chemistry runs at core temperature t against T_BODY: the Boltzmann-Arrhenius factor
    /// exp(E/k (1/T_BODY - 1/t)) of metabolic theory (Gillooly et al. 2001; E = E_ACT): ~0.23 at 20 °C, 1 at T_BODY.
    /// Basal cost and digestion scale by it; muscle power by its square root.
    /// </summary>
    public static float Rate(float t)
    {
        const float kB = 8.617e-5f;   // Boltzmann's constant, eV/K
        return MathF.Exp(K.E_ACT / kB * (1f / (K.T_BODY + 273.15f) - 1f / (MathF.Max(t, K.T_CORE_MIN) + 273.15f)));
    }

    /// <summary>Lower and upper critical temperatures (operative, °C) of a body making heat h: the thermoneutral zone.</summary>
    public static (float lower, float upper) Neutral(float mass, float ins, float app, bool immersed, float h)
    {
        Conductance(mass, ins, app, immersed, out float cmin, out float cmax);
        return (K.T_BODY - h / cmin, K.T_BODY - h / cmax);
    }
}
