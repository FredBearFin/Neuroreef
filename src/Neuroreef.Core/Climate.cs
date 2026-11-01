// Climate and body heat (DESIGN Revision O). Nothing here names a biome or gives an animal a tolerance: the air
// temperature of each cell follows from latitude, season, height and the nearness of water, and the plants, the soil
// and each animal's heat budget answer to that temperature through their physics. Where a forest, a tundra or a
// desert ends up, and what body lives there, is what follows.
//
// Temperature (°C), per cell, written by FIELDS each step:
//   T_phys = T_EQ - DT_EP sin²θ + A_SEAS sinθ sin(2π t / year) Cont - LAPSE h,   θ the latitude of the row (SinLat).
//   2026-11-01 (the 1 km world): a map sits at one latitude, the world's (World.LatDeg, drawn from its terrain seed),
//   and spans only its true north-south extent about it (Map.LatSpanDeg: 0.03° on the 256 map), so temperature varies
//   with height, water and the season, not from pole to equator. Nature.LatSpan >= 360 gives back the old torus of
//   latitudes, θ = 2π y / FW (equator, north pole, equator, south pole; the hemispheres' seasons opposite by sinθ).
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
    public static readonly float[] Evap = Table(t => MathK.Pow(Es(t) / Es(K.T_REF), -K.EVAP_BETA));
    /// <summary>Yan & Hunt (1999) growth curves of grass, shrubs and trees, 1 at T_REF, at most PLANT_CAP; 0 outside (Tmin, Tmax).</summary>
    public static readonly float[] FGrass = Beta(K.GRASS_TMIN, K.GRASS_TOPT, K.GRASS_TMAX),
        FShrub = Beta(K.SHRUB_TMIN, K.SHRUB_TOPT, K.SHRUB_TMAX), FTree = Beta(K.TREE_TMIN, K.TREE_TOPT, K.TREE_TMAX);
    /// <summary>Algae and plankton growth x EPPLEY^(T - T_REF), at the water's temperature (Eppley 1972).</summary>
    public static readonly float[] Algae = Table(t => MathK.Pow(K.EPPLEY, MathF.Max(t, K.T_WATER_MIN) - K.T_REF));
    /// <summary>Decomposition x Q10_ROT^((T - T_REF) / 10) (Lloyd & Taylor 1994).</summary>
    public static readonly float[] Rot = Table(t => MathK.Pow(K.Q10_ROT, (t - K.T_REF) / 10f));

    internal static float Es(float t) => 0.6108f * MathK.Exp(17.27f * t / (t + 237.3f));

    static float[] Table(Func<float, float> f)
    {
        var a = new float[K.TT_N];
        for (int k = 0; k < K.TT_N; k++) a[k] = f(K.TT_LO + k * K.TT_STEP);
        return a;
    }

    static float[] Beta(float lo, float opt, float hi)
    {
        double F(double t) => t <= lo || t >= hi ? 0 : (hi - t) / (hi - opt) * MathK.Pow((float)((t - lo) / (opt - lo)), (float)((opt - lo) / (hi - opt)));
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

    // ---- latitude (2026-11-01)
    /// <summary>Is the map the old torus of latitudes (Nature.LatSpan >= 360)? Else it sits at w.LatDeg.</summary>
    public static bool Torus => Nature.LatSpan >= 360f;

    /// <summary>Latitude (radians, &lt; 0 south) of field row y (fractional rows allowed): the world's latitude, plus half the
    /// map's span x sin(2π y / FW) so the torus closes smoothly. On the old torus, 2π y / FW folded to -π/2..π/2.</summary>
    public static float LatRad(World w, float y)
    {
        float th = 2f * MathF.PI * y / Map.FW;
        if (Torus) { float s = MathK.Sin(th), ph = ((th % MathF.PI) + MathF.PI) % MathF.PI; return (s < 0f ? -1f : 1f) * MathF.Min(ph, MathF.PI - ph); }
        return (w.LatDeg + 0.5f * Map.LatSpanDeg * MathK.Sin(th)) * (MathF.PI / 180f);
    }

    /// <summary>sin(latitude) of field row y (the temperature's pole-to-equator term and the seasons' sign).</summary>
    public static float SinLat(World w, float y) => Torus ? MathK.Sin(2f * MathF.PI * y / Map.FW) : MathK.Sin(LatRad(w, y));

    /// <summary>|latitude| of field row y in degrees (the rain belts and the prevailing winds).</summary>
    public static float AbsLatDeg(World w, float y) => MathF.Abs(LatRad(w, y)) * (180f / MathF.PI);

    /// <summary>The phase of the moisture season at field row y: 2π y / FW on the old torus (wet seasons by latitude),
    /// else the latitude itself (one phase across a map that sits at one latitude).</summary>
    public static float Phase(World w, float y) => Torus ? 2f * MathF.PI * y / Map.FW : LatRad(w, y);

    /// <summary>Is the climate on? (Climate = 0 is the world without it, bit for bit: every rule below is skipped.)</summary>
    public static bool On => Nature.Climate > 0f;

    /// <summary>Row constants of the temperature: T = a + b x Cont - TLapse (TLapse already holds Climate x LAPSE x height).</summary>
    public static void RowParams(World w, int y, out float a, out float b)
    {
        float s = SinLat(w, y), c = Nature.Climate;
        float year = MathK.Sin(2f * MathF.PI * (float)(w.Step % (ulong)Nature.SeasonLength) / Nature.SeasonLength);
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
        // continentality (2026-11-01, the 1 km world): the water damps the seasons by how much of it the air has crossed,
        // the share of standing water within ~L_MAR (a Gaussian of that width, three box blurs on the torus), so a pond or
        // a small lake damps its shore a little and the open sea fully: WATER_DAMP x min(1, 2 x share), the share being 1/2
        // at a straight shore of open water (oceanity indices measure the sea within a radius; a lake of a few hundred m
        // barely shifts the air's seasons). Before, any water at all, a lake of 0.4 km2 on 16 m cells, damped its shore as
        // the ocean does, so the valley floors had cooler summers than the hills and the treeline ran upside down
        var share = new float[n]; var tmp = new float[n];
        for (int i = 0; i < n; i++) share[i] = w.LakeOf[i] >= 0 ? 1f : 0f;
        int r = Math.Clamp((int)MathF.Round(K.L_MAR), 1, Map.FW / 2);
        for (int k = 0; k < 3; k++) BoxBlur(share, tmp, Map.FW, r);
        for (int i = 0; i < n; i++)
        {
            float h = MathF.Max(0f, w.Elev[i] - sea) / (1f - sea) * Map.ReliefM;     // m above the lakes (the map's vertical scale)
            w.TLapse[i] = Nature.Climate * K.LAPSE * h * 0.001f;
            w.Cont[i] = 1f - K.WATER_DAMP * MathF.Min(1f, 2f * share[i]);
        }
        for (int y = 0; y < Map.FW; y++) TempRow(w, y);
    }

    /// <summary>Box blur of radius r on a torus of side W, in place (Climate.Build's water share).</summary>
    static void BoxBlur(float[] a, float[] tmp, int W, int r)
    {
        float inv = 1f / (2 * r + 1);
        for (int y = 0; y < W; y++)
        {
            int row = y * W; float s = 0f;
            for (int k = -r; k <= r; k++) s += a[row + World.WrapI(k, W)];
            for (int x = 0; x < W; x++) { tmp[row + x] = s * inv; s += a[row + World.WrapI(x + r + 1, W)] - a[row + World.WrapI(x - r, W)]; }
        }
        for (int x = 0; x < W; x++)
        {
            float s = 0f;
            for (int k = -r; k <= r; k++) s += tmp[World.WrapI(k, W) * W + x];
            for (int y = 0; y < W; y++) { a[y * W + x] = s * inv; s += tmp[World.WrapI(y + r + 1, W) * W + x] - tmp[World.WrapI(y - r, W) * W + x]; }
        }
    }

    /// <summary>
    /// Where rain falls (Revision P), relative to the land's mean (1): air takes up water over lakes and seas, the
    /// prevailing wind of its latitude carries it over land, and it rains out as it goes; the land gives some back
    /// (RAIN_RECYCLE). The belts of the general circulation set how much falls at each latitude: wet where air rises at
    /// the equator and near 55°, dry where it sinks near 30° and at the poles (Hadley, Ferrel and polar cells; trade
    /// winds from the east below 30°, westerlies to 60°, polar easterlies above). Rows are latitudes; each row is marched
    /// around the torus until the air arriving at its start is settled.
    /// 2026-11-01 (the 1 km world): every distance in km, so the rain does not depend on the grid. The background rain
    /// falls off over RAIN_L (hundreds of km), not across a map; ground that lifts the air condenses RAIN_OROG of its
    /// water per km of rise, which falls out over RAIN_FALL downwind (Smith &amp; Barstad 2004), so a hill a few km wide
    /// gets a little more rain on its windward side and in its lee than far upwind, not many times more; and rain grows by
    /// RAIN_ZGRAD per km of height (seeder-feeder). On a map at one latitude every row has the same belt and wind.
    /// </summary>
    public static void Rainfall(World w, float[] rain)
    {
        int FW = Map.FW;
        float sea = w.SeaLevel, dx = K.GEO_CELL_M * 1e-3f, mapKm = FW * dx, zKm = Map.ReliefM * 1e-3f / MathF.Max(1e-6f, 1f - sea);
        float bg = 1f - MathK.Exp(-dx / K.RAIN_L), ev = 1f - MathK.Exp(-dx * K.RAIN_EVAP), rec = 1f - MathK.Exp(-dx * K.RAIN_RECYCLE);
        float fall = 1f - MathK.Exp(-dx / K.RAIN_FALL);
        // laps of the torus until the air and its condensate have forgotten where the march began: three of the longer of
        // the fallout length and the air's own relaxation over land, and one more that is kept
        float settle = 3f * MathF.Max(K.RAIN_FALL, 1f / (1f / K.RAIN_L + K.RAIN_RECYCLE));
        int laps = 1 + Math.Max(1, (int)MathF.Ceiling(settle / mapKm));
        double sum = 0; int nLand = 0;
        for (int y = 0; y < FW; y++)
        {
            float lat = AbsLatDeg(w, y);
            int dir = lat < 30f ? -1 : lat < 60f ? 1 : -1;
            float belt = K.RAIN_DRY + (1f - K.RAIN_DRY) * MathK.Exp(-lat * lat / (K.RAIN_BELT * K.RAIN_BELT))
                       + K.RAIN_MID * MathK.Exp(-(lat - 55f) * (lat - 55f) / (K.RAIN_BELT * K.RAIN_BELT));
            float q = 0.5f, cond = 0f; int x = 0; float hPrev = 0f;
            for (int k = 0, last = (laps - 1) * FW; k < laps * FW; k++, x = World.WrapI(x + dir, FW))
            {
                int i = y * FW + x;
                float h = MathF.Max(0f, w.Elev[i] - sea) * zKm;   // km above the lakes
                float drop = cond * fall; cond -= drop;
                // open water: the air takes up moisture at the water's own height (2026-10-31: a river or a lake above sea level
                // is not sea level, so the land beyond it is not lifted from 0)
                if (w.Cell[i] != (byte)Realm.Land) { q += (1f - q) * ev; hPrev = h; continue; }
                float lift = MathF.Min(q, q * MathF.Max(0f, h - hPrev) * K.RAIN_OROG);
                q -= lift; cond += lift;
                float r = q * bg;
                q += (1f - q) * rec - r;
                hPrev = h;
                if (k >= last) { rain[i] = (r + drop) * belt * (1f + K.RAIN_ZGRAD * h); sum += rain[i]; nLand++; }
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
            float s = SinLat(w, y), phase = Phase(w, y);
            float a = K.T_REF + Nature.Climate * (K.T_EQ - K.DT_EP * s * s - K.T_REF);
            for (int i = y * FW, e = i + FW; i < e; i++)
            {
                if (w.Cell[i] != (byte)Realm.Land) continue;
                for (int p = 0; p < PH; p++)
                {
                    float ph = 2f * MathF.PI * (p + 0.5f) / PH;
                    float season = MathK.Sin(ph - phase);                                 // as Fields: the moisture season by latitude
                    float b = Nature.Climate * K.A_SEAS * s * MathK.Sin(ph);
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
                    float season = MathK.Sin(2f * MathF.PI * (p + 0.5f) / PH - Phase(w, y));
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
        float s = SinLat(w, i / Map.FW);
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
        if ((w.CaveKind[cell] & Caves.Entrance) != 0) return Caves.Air(w, cell, t);   // shelter: cave air, out of the sun (Caves.cs)
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
        return MathK.Exp(K.E_ACT / kB * (1f / (K.T_BODY + 273.15f) - 1f / (MathF.Max(t, K.T_CORE_MIN) + 273.15f)));
    }

    /// <summary>
    /// Metabolic level of a body with endothermy e against a warm-blooded one at the same core temperature (2026-11-01):
    /// ECTO_LEVEL + (1 - ECTO_LEVEL) e. Endothermy is not only a warm core: it is tissue built to burn 5-10x faster
    /// (mitochondria, leaky membranes; Hulbert &amp; Else 2000), which costs that much at rest and gives that much aerobic
    /// capacity and endurance. Resting cost and the respiratory organs' capacity scale with Rate(core) x Level(e)
    /// (Activity); digestion (gut enzymes) and muscle power for a burst follow the core's temperature alone.
    /// </summary>
    public static float Level(float e) => K.ECTO_LEVEL + (1f - K.ECTO_LEVEL) * e;

    /// <summary>The pace of a body's chemistry at core temperature t and endothermy e against a warm-blooded body at
    /// T_BODY: Rate(t) x Level(e). 1 with no climate (no heat budget).</summary>
    public static float Activity(float t, float e) => Climate.On ? Rate(t) * Level(e) : 1f;

    /// <summary>
    /// Heat a body would have to make to keep its core from freezing (2026-11-01): its least dry conductance x how far its
    /// core is below T_FREEZE. A cold-blooded body cannot make it, so it is all shortfall (strain, death by cold): on land
    /// in a frost an ectotherm freezes, in water (never below T_WATER_MIN) it does not.
    /// </summary>
    public static float Freeze(float mass, float ins, float app, bool immersed, float core)
    {
        if (core >= K.T_FREEZE) return 0f;
        Conductance(mass, ins, app, immersed, out float cmin, out _);
        return cmin * (K.T_FREEZE - core);
    }

    /// <summary>Lower and upper critical temperatures (operative, °C) of a body making heat h: the thermoneutral zone.</summary>
    public static (float lower, float upper) Neutral(float mass, float ins, float app, bool immersed, float h)
    {
        Conductance(mass, ins, app, immersed, out float cmin, out float cmax);
        return (K.T_BODY - h / cmin, K.T_BODY - h / cmax);
    }
}
