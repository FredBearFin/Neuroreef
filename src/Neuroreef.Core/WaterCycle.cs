// The water cycle: water as stores and fluxes. Nothing here names a wet or a dry place, a spring or a flood season.
// Rain falls where Climate.Rainfall puts it, as the season and the year bring it; where the air is cold it falls as
// snow and lies until it is warm enough to melt. On land it fills the soil (a Manabe bucket), which plants and warm air
// empty (evapotranspiration) and which drains below to groundwater; what the soil cannot hold runs off. Shallow
// groundwater feeds each cell's own small streams; deep groundwater flows downslope cell by cell into the rivers and
// lakes, and where more gathers than the ground can hold, it emerges (seeps and springs). Runoff, baseflow and seeps
// run down the drainage network to the lakes, and fill the closed basins on the way (ponds, which spill when full and
// lose water to the air and their floor). Rivers flood when their discharge tops what their channel carries, and lakes
// rise and fall with what comes in and goes out.
// What follows from that: soil moisture, ponds, springs, floods, river discharge.
//
// Units: stores in mm of water over a cell; fluxes in mm x cells per update (WC_EVERY steps). A year is
// Nature.SeasonLength steps of 365 days. Routing is instantaneous within an update: water crosses the map in days,
// an update is ~6 days. Every flux leaves one store and enters another, so the water budget closes (World.Wb*: rain = ET + pond evaporation + flow to
// the lakes + the change in LandStore).
// Parallel over rows for the per-cell part (Row: each row writes only its own cells), serial for the routing
// (Finish: a topological pass from the sources to the outlets). Allocation only in Init (construction, NR-101).
//
// Lakes keep their extent (their cells stay water, so the realm checks, DistWater and DistLand do not change): each
// lake's level is tracked against its level at creation, and its evaporating area shrinks as its level falls.

using System;

namespace Neuroreef;

public static class WaterCycle
{
    public const int NSUM = 3;   // per-row sums: rain, evapotranspiration, pond evaporation

    /// <summary>Potential evapotranspiration against T_REF: e_s(T) / e_s(T_REF) (Tetens), 0 at or below 0 °C.</summary>
    static readonly float[] Pet = PetTable();

    static float[] PetTable()
    {
        var a = new float[K.TT_N];
        for (int k = 0; k < K.TT_N; k++) { float t = K.TT_LO + k * K.TT_STEP; a[k] = t <= 0f ? 0f : Climate.Es(t) / Climate.Es(K.T_REF); }
        return a;
    }

    /// <summary>Share of a year one update spans.</summary>
    public static float YearFrac => (float)K.WC_EVERY / Nature.SeasonLength;

    static float Season(World w, int y) =>
        MathK.Sin(2f * MathF.PI * (float)(w.Step % (ulong)Nature.SeasonLength) / Nature.SeasonLength - 2f * MathF.PI * y / Map.FW);

    /// <summary>Rain per update (mm) on a cell of pattern 1 in row y now: the mean, x the season by latitude (as the
    /// moisture season always ran), x this year's anomaly.</summary>
    static float RainUnit(World w, int y)
        => K.P_MEAN * YearFrac * MathF.Max(0f, 1f + K.A_SEASON * Nature.Seasons * Season(w, y)) * MathF.Max(0f, 1f + w.RainAnom);

    /// <summary>
    /// After Terrain.Build: the rain pattern, the drainage, the lakes and the soil; then WC_SPINUP average years of
    /// weather, so the stores start in their seasonal state; then moisture, ponds and floods from them.
    /// </summary>
    /// <param name="parallel">runs a job over [0, n) on the simulation's workers (rows); null runs it here</param>
    public static void Init(World w, Action<Action<int, int>, int>? parallel = null)
    {
        int FW = Map.FW, n = FW * FW;
        float sea = w.SeaLevel;
        // where rain falls: the pattern of the winds and the ground (Climate.Rainfall; land mean 1, none on open water or rivers)
        Climate.Rainfall(w, w.Rain);
        for (int y = 0; y < FW; y++)
        {
            double s = 0; int c = 0;
            for (int i = y * FW; i < (y + 1) * FW; i++) if (w.Cell[i] == (byte)Realm.Land) { s += w.Rain[i]; c++; }
            float rowMean = c > 0 ? (float)(s / c) : 1f;
            // part of the rain is recycled moisture that falls evenly over the land of its latitude (the land's mean
            // stays 1); a lake gets what the land of its latitude gets
            for (int i = y * FW; i < (y + 1) * FW; i++)
                if (w.Cell[i] == (byte)Realm.Land) w.Rain[i] = (1f - K.RAIN_LOCAL) * w.Rain[i] + K.RAIN_LOCAL * rowMean;
                else if (w.Elev[i] < sea) w.Rain[i] = rowMean;
        }
        // the drainage of the worked relief: the same receivers Terrain.Build routed the rivers with
        var net = new Hydrology.Net(n);
        Hydrology.Route(w.Elev, sea, w.Rain, net);
        Array.Copy(net.Recv, w.Recv, n); Array.Copy(net.Order, w.DrainOrder, n);
        // lakes: connected open water below lake level (8 neighbours, on the torus)
        Array.Fill(w.LakeOf, -1);
        var stack = new int[n];
        int nl = 0;
        float mmPerElev = K.H_RELIEF * 1000f / (1f - sea);
        for (int i0 = 0; i0 < n; i0++)
        {
            if (w.Elev[i0] >= sea || w.LakeOf[i0] >= 0) continue;
            int sp = 0; stack[sp++] = i0; w.LakeOf[i0] = nl;
            float area = 0, deep = 0;
            while (sp > 0)
            {
                int c = stack[--sp]; area++; deep = MathF.Max(deep, (sea - w.Elev[c]) * mmPerElev);
                int x = c % FW, y = c / FW;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int j = World.WrapI(y + dy, FW) * FW + World.WrapI(x + dx, FW);
                        if (w.Elev[j] < sea && w.LakeOf[j] < 0) { w.LakeOf[j] = nl; stack[sp++] = j; }
                    }
            }
            w.LakeArea[nl] = area; w.LakeDepth[nl] = MathF.Max(deep, 1f); w.LakeLevel[nl] = 0f; w.LakeQm[nl] = 0f;
            nl++;
        }
        w.LakeCount = nl;
        // the lake each cell drains to in the end (outlets first in DrainOrder)
        for (int q = 0; q < n; q++)
        {
            int c = w.DrainOrder[q], r = w.Recv[c];
            w.Outlet[c] = w.LakeOf[c] >= 0 ? w.LakeOf[c] : r >= 0 ? w.Outlet[r] : -1;
        }
        // a first guess of each lake's mean inflow, for its outlet during the spin-up: half the rain on its catchment, all on itself
        float unit = K.P_MEAN * YearFrac;
        for (int i = 0; i < n; i++) if (w.Outlet[i] >= 0) w.LakeQm[w.Outlet[i]] += unit * w.Rain[i] * (w.LakeOf[i] >= 0 ? 1f : 0.5f);
        // soil: alluvium holds more water
        for (int i = 0; i < n; i++)
        {
            w.SoilCap[i] = K.SOIL_CAP * (1f + K.ALLUV_CAP * (1f - MathK.Exp(-w.Alluvium[i] / K.ALLUV_D)));
            w.SoilWater[i] = w.Cell[i] == (byte)Realm.Land ? 0.5f * w.SoilCap[i] : 0f;
            w.Snow[i] = 0f; w.Ground[i] = 0f; w.Deep[i] = 0f; w.PondWater[i] = 0f; w.Seep[i] = 0f; w.Discharge[i] = 0f; w.DischargeMean[i] = 0f;
        }

        // spin-up: average years, from the step the world starts at
        ulong step0 = w.Step; float anom0 = w.RainAnom;
        w.RainAnom = 0f;
        int perYear = Math.Max(1, (int)MathF.Round(1f / YearFrac));
        var rech = new float[n]; var before = new float[n]; var qsum = new double[n]; var lsum = new double[nl];
        Action<int, int> rows = (lo, hi) => { for (int y = lo; y < hi; y++) Row(w, y); };
        Action<int, int> temps = (lo, hi) => { for (int y = lo; y < hi; y++) Climate.TempRow(w, y); };
        parallel ??= (job, count) => job(0, count);
        for (int yr = 0; yr < K.WC_SPINUP; yr++)
        {
            for (int u = 0; u < perYear; u++)
            {
                w.Step = step0 + (ulong)((yr * perYear + u) * K.WC_EVERY);
                if (Climate.On) parallel(temps, FW);
                if (yr == 0) for (int i = 0; i < n; i++) before[i] = w.Ground[i] + w.Deep[i];
                parallel(rows, FW);
                if (yr == 0) for (int i = 0; i < n; i++) rech[i] += w.Ground[i] + w.Deep[i] - before[i];
                Finish(w, spinup: true);
                if (yr == K.WC_SPINUP - 1) { for (int i = 0; i < n; i++) qsum[i] += w.Discharge[i]; for (int l = 0; l < nl; l++) lsum[l] += w.LakeGross[l]; }
            }
            // after the first year: groundwater at the steady state of that year's recharge, which would take decades to
            // fill cell by cell (each cell passes on what it receives; where more arrives than it can hold, it emerges)
            if (yr == 0 && K.WC_SPINUP > 1) SteadyGround(w, rech, perYear);
        }
        for (int i = 0; i < n; i++) w.DischargeMean[i] = (float)(qsum[i] / perYear);
        for (int l = 0; l < nl; l++) w.LakeQm[l] = (float)(lsum[l] / perYear);
        w.Step = step0; w.RainAnom = anom0;
        if (Climate.On) for (int y = 0; y < FW; y++) Climate.TempRow(w, y);
        Array.Fill(w.LakeLevel, 0f, 0, nl);
        w.WbRain = w.WbET = w.WbPondEvap = w.WbToLakes = w.WbLakeRain = w.WbLakeEvap = w.WbLakeSpill = 0;
        w.WaterOn = true;
        Ponds.Update(w);   // floods soak their soil from the next update on, so the budget counts that water
    }

    // groundwater at the steady state of a mean recharge (per year, mm per cell), sources first: what it receives from
    // above and from its soil, it passes on at the rate it holds it
    static void SteadyGround(World w, float[] rech, int perYear)
    {
        int n = Map.FW * Map.FW;
        float k = GroundRate();
        var gin = new float[n];
        for (int q = n - 1; q >= 0; q--)
        {
            int c = w.DrainOrder[q];
            if (w.Cell[c] != (byte)Realm.Land) continue;
            float r = rech[c] / perYear;
            w.Ground[c] = (1f - k) * (1f - K.GW_DEEP) * r / k;        // shallow: what it holds after an update at steady state
            float add = K.GW_DEEP * r + gin[c];
            float g = (1f - k) * add / k, x = add / k;                 // deep: after an update, and before it passes some on
            if (g > K.GW_CAP) { g = K.GW_CAP; x = K.GW_CAP + add; }
            w.Deep[c] = g;
            int rc = w.Recv[c];
            if (rc >= 0 && w.Cell[rc] == (byte)Realm.Land) gin[rc] += k * x;
        }
    }

    static float GroundRate() => MathF.Min(1f, 365f * YearFrac / K.GW_TAU);

    /// <summary>One update of the land cells of row y: rain or snow, melt, the soil bucket, evapotranspiration,
    /// drainage to groundwater, and a pond's losses. Writes only row y.</summary>
    public static void Row(World w, int y)
    {
        int FW = Map.FW;
        float f = YearFrac, days = 365f * f;
        float pu = RainUnit(w, y), petU = K.PET_REF * f;
        float melt1 = K.DDF * days, perc1 = K.K_PERC * days, seep1 = K.POND_SEEP * days;
        float[] G = w.Pool[(int)Pool.Grass];
        double sR = 0, sE = 0, sP = 0;
        for (int i = y * FW, e = i + FW; i < e; i++)
        {
            if (w.Cell[i] != (byte)Realm.Land) continue;
            float t = w.Temp[i];
            float p = pu * w.Rain[i]; sR += p;
            // snow: it falls where the air is cold and melts by degree-days where it is warm (Hock 2003)
            float snow = w.Snow[i];
            if (t <= K.T_SNOW) { snow += p; p = 0f; }
            if (snow > 0f && t > 0f) { float m = MathF.Min(snow, melt1 * t); snow -= m; p += m; }
            float ice = MathF.Max(0f, snow - K.SNOW_MAX);   // a glacier: its ice flows away and melts lower down (runoff)
            snow -= ice;
            w.Snow[i] = snow;
            // a pond takes the rain and melt on its wetted area
            float pw = w.PondWater[i], pa = 0f;
            if (pw > 0f) { pa = PondArea(pw); pw += pa * p; p -= pa * p; }
            // the bucket: rain on the saturated share of the cell runs off (VIC), what it cannot hold at all runs off too
            float cap = w.SoilCap[i], s0 = w.SoilWater[i];
            float ro = p > 0f ? Infiltration(s0, cap, p) : 0f;
            float s = s0 + p - ro;
            if (s > cap) { ro += s - cap; s = cap; }
            ro += ice;
            // evapotranspiration: potential (warmth) x plant cover x soil wetness (Manabe 1969); none under snow
            float pet = snow > 0f ? 0f : petU * Pet[Climate.Idx(t)];
            if (pet > 0f)
            {
                float cover = MathF.Min(1f, G[i] + w.Shrub[i] + w.Tree[i]);
                float et = MathF.Min(s, pet * (K.E_BARE + (1f - K.E_BARE) * cover) * MathF.Min(1f, s / (K.BETA_FC * cap)));
                s -= et; sE += et;
            }
            // drainage below the roots to groundwater (Campbell 1974: steeply less as the soil dries)
            float rw = s / cap, rp = 1f;
            for (int e2 = 0; e2 < K.PERC_EXP; e2++) rp *= rw;
            float d = MathF.Min(s, perc1 * rp);
            s -= d; Recharge(w, i, d);
            // a pond's wetted area: open water evaporates (not under ice) and seeps into the soil, the soil's surplus on to groundwater
            if (pw > 0f)
            {
                float ev = MathF.Min(pw, pa * K.E_OPEN * petU * Pet[Climate.Idx(t)]);
                pw -= ev; sP += ev;
                float sp = MathF.Min(pw, pa * seep1), a = MathF.Min(sp, cap - s);
                pw -= sp; s += a; Recharge(w, i, sp - a);
                w.PondWater[i] = pw;
            }
            w.SoilWater[i] = s; w.Runoff[i] = ro;
            w.Moisture[i] = s / cap;
        }
        int o = y * NSUM;
        w.WcRow[o] = sR; w.WcRow[o + 1] = sE; w.WcRow[o + 2] = sP;
    }

    static void Recharge(World w, int i, float d) { float deep = K.GW_DEEP * d; w.Deep[i] += deep; w.Ground[i] += d - deep; }

    /// <summary>Share of a cell under a pond holding pw (mm over the cell): POND_FRAC x sqrt(pw / POND_CAP).</summary>
    public static float PondArea(float pw) => K.POND_FRAC * MathF.Sqrt(MathF.Min(1f, pw / K.POND_CAP));

    /// <summary>
    /// Runoff from water p falling on a soil holding s of cap: the soil's point capacities spread as the VIC curve of
    /// shape b = INFIL_B (Zhao 1992; Wood et al. 1992), so a share 1 - (1 - s / cap)^(b / (1 + b)) of the cell is
    /// saturated and sheds what falls on it (rain per update is small against capacity). Tabulated: 0 &lt;= result &lt;= p.
    /// </summary>
    public static float Infiltration(float s, float cap, float p)
    {
        float x = Math.Clamp(s / cap, 0f, 1f) * (SAT_N - 1);
        int k = Math.Min((int)x, SAT_N - 2);
        float a = Sat[k] + (Sat[k + 1] - Sat[k]) * (x - k);
        return p * a;
    }

    const int SAT_N = 257;
    static readonly float[] Sat = SatTable();
    static float[] SatTable()
    {
        var t = new float[SAT_N];
        for (int k = 0; k < SAT_N; k++) t[k] = 1f - (float)Math.Pow(1.0 - k / (double)(SAT_N - 1), K.INFIL_B / (1.0 + K.INFIL_B));
        return t;
    }

    /// <summary>An update, serially (tests, Init): every row, then the routing and what follows it.</summary>
    public static void Update(World w)
    {
        for (int y = 0; y < Map.FW; y++) Row(w, y);
        Finish(w);
    }

    /// <summary>
    /// After the rows: groundwater moves downslope and emerges, runoff runs down the network into the ponds, rivers
    /// and lakes; river discharge, its slow mean, the drinkable water (Ponds.Update), floods soaking the floodplain,
    /// the lakes' balance, and the budget. Serial and in a fixed order (deterministic).
    /// </summary>
    public static void Finish(World w, bool spinup = false)
    {
        int FW = Map.FW, n = FW * FW;
        double sR = 0, sE = 0, sP = 0;
        for (int y = 0; y < FW; y++) { sR += w.WcRow[y * NSUM]; sE += w.WcRow[y * NSUM + 1]; sP += w.WcRow[y * NSUM + 2]; }
        w.WbRain += sR; w.WbET += sE; w.WbPondEvap += sP;

        float k = GroundRate();
        float[] Q = w.Discharge;
        Array.Clear(Q);
        Array.Clear(w.LakeIn, 0, w.LakeCount);
        double toLakes = 0;
        for (int q = n - 1; q >= 0; q--)   // sources first: every cell after all the cells that drain into it
        {
            int c = w.DrainOrder[q];
            if (w.LakeOf[c] >= 0) continue;
            int r = w.Recv[c];
            float qc = Q[c] + w.Runoff[c];
            if (w.Cell[c] == (byte)Realm.Land)
            {
                // shallow groundwater feeds the cell's own streams (baseflow); deep groundwater flows on underground to
                // the cell it drains to (or into its river or lake), and above what the ground holds the water table meets
                // the surface and the rest emerges (seeps, springs)
                float o = k * w.Ground[c];
                w.Ground[c] -= o; qc += o;
                float g = w.Deep[c], od = k * g;
                g -= od;
                float ex = 0f;
                if (g > K.GW_CAP) { ex = g - K.GW_CAP; g = K.GW_CAP; }
                w.Deep[c] = g; w.Seep[c] = ex; qc += ex;
                if (r >= 0 && w.Cell[r] == (byte)Realm.Land) w.Deep[r] += od; else qc += od;
                // a closed basin holds what runs into it, and spills what it cannot hold (fill-and-spill)
                if (w.PondSite[c] != 0) { float p = w.PondWater[c] + qc; qc = MathF.Max(0f, p - K.POND_CAP); w.PondWater[c] = p - qc; }
            }
            Q[c] = qc;
            if (r < 0) toLakes += qc;                                          // the lowest cell of a world with no lakes
            else if (w.LakeOf[r] >= 0) { w.LakeIn[w.LakeOf[r]] += qc; toLakes += qc; }
            else Q[r] += qc;
        }
        // a channel's size follows the flow it carries over years
        float a = YearFrac / K.QMEAN_YEARS;
        if (!spinup) for (int i = 0; i < n; i++) w.DischargeMean[i] += a * (Q[i] - w.DischargeMean[i]);
        if (!spinup) { Ponds.Update(w); Soak(w, out double soaked); toLakes -= soaked; }
        w.WbToLakes += toLakes;
        Lakes(w, spinup);
    }

    // a flooded floodplain is soaked: its soil fills from the river that floods it (the water leaves the river)
    static void Soak(World w, out double soaked)
    {
        soaked = 0;
        int n = Map.FW * Map.FW;
        for (int i = 0; i < n; i++)
        {
            if (w.Wet[i] != 3) continue;
            int d = w.FloodFrom[i];
            float give = MathF.Min(w.SoilCap[i] - w.SoilWater[i], w.Discharge[d]);
            if (give <= 0f) continue;
            w.SoilWater[i] += give; w.Discharge[d] -= give;
            w.Moisture[i] = w.SoilWater[i] / w.SoilCap[i];
            int l = w.Outlet[d];
            if (l >= 0) w.LakeIn[l] -= give;
            soaked += give;
        }
    }

    // each lake: inflow + rain - evaporation over its wet area; above its outlet's sill it spills in proportion to its stage
    // (outlet sized for its mean inflow: LAKE_STAGE above the sill at the mean), solved exactly over the update
    static void Lakes(World w, bool spinup)
    {
        int FW = Map.FW, nl = w.LakeCount;
        if (nl == 0) return;
        float f = YearFrac, petU = K.PET_REF * f;
        double rainT = 0, evT = 0, spillT = 0;
        for (int l = 0; l < nl; l++) w.LakeGross[l] = w.LakeIn[l];   // what the rivers bring
        for (int y = 0; y < FW; y++)
        {
            float pu = RainUnit(w, y);
            for (int i = y * FW, e = i + FW; i < e; i++)
            {
                int l = w.LakeOf[i];
                if (l < 0) continue;
                float wa = Wetted(w, l), t = w.Temp[i];
                float rain = pu * w.Rain[i] * wa;
                float ev = t > 0f ? K.E_OPEN * petU * Pet[Climate.Idx(Climate.Water(t))] * wa : 0f;   // none under ice
                w.LakeIn[l] += rain - ev; w.LakeGross[l] += rain; rainT += rain; evT += ev;
            }
        }
        float a = YearFrac / K.QMEAN_YEARS;
        for (int l = 0; l < nl; l++)
        {
            float A = w.LakeArea[l], L0 = w.LakeLevel[l], L1 = L0 + w.LakeIn[l] / A, L = L1;
            float lam = w.LakeQm[l] / (K.LAKE_STAGE * A);   // share of the stage the outlet passes per update
            if (L1 > 0f && lam > 0f)
            {
                // dL/dt = i - lam L from max(L0, 0), with i the inflow above the sill
                float start = MathF.Max(L0, 0f), i = L1 - start, decay = MathK.Exp(-lam);
                L = i / lam + (start - i / lam) * decay;
                L = Math.Clamp(L, MathF.Min(L1, 0f), L1);
            }
            w.LakeLevel[l] = L; spillT += (double)(L1 - L) * A;
            if (!spinup) w.LakeQm[l] += a * (w.LakeGross[l] - w.LakeQm[l]);
        }
        w.WbLakeRain += rainT; w.WbLakeEvap += evT; w.WbLakeSpill += spillT;
    }

    // share of a lake's area still under water: a falling lake shrinks toward its deepest point (a cone-like basin)
    static float Wetted(World w, int l) => Math.Clamp(1f + w.LakeLevel[l] / w.LakeDepth[l], 0f, 1f);

    // ------------------------------------------------------------------ reporting

    /// <summary>Water now stored on the land (every cell not a lake): soil, snow, shallow and deep groundwater, ponds (mm x cells).</summary>
    public static double LandStore(World w)
    {
        double s = 0;
        for (int i = 0; i < Map.FW * Map.FW; i++)
            if (w.LakeOf[i] < 0) s += (double)w.SoilWater[i] + w.Snow[i] + w.Ground[i] + w.Deep[i] + w.PondWater[i];
        return s;
    }

    /// <summary>Water held in the lakes above (or below) their level at creation (mm x cells).</summary>
    public static double LakeStore(World w)
    {
        double s = 0;
        for (int l = 0; l < w.LakeCount; l++) s += (double)w.LakeLevel[l] * w.LakeArea[l];
        return s;
    }

    /// <summary>A summary of the water now: land-mean soil moisture, snow (mm), groundwater (mm), cells of standing pond
    /// water, springs, flooded cells, and the river discharge against its mean (discharge-weighted over river cells).</summary>
    public static (float moist, float snow, float ground, int ponds, int springs, int flooded, float riverQ) Stats(World w)
    {
        int n = Map.FW * Map.FW, land = 0, ponds = 0, springs = 0, flooded = 0;
        double m = 0, sn = 0, g = 0, q = 0, qm = 0;
        float springMin = K.SPRING_MIN * 365f * YearFrac;
        for (int i = 0; i < n; i++)
        {
            if (w.Cell[i] == (byte)Realm.Land)
            {
                land++; m += w.Moisture[i]; sn += w.Snow[i]; g += w.Ground[i] + w.Deep[i];
                if (w.PondSite[i] != 0 && w.PondWater[i] >= K.POND_WET) ponds++;
                if (w.Seep[i] >= springMin) springs++;
                if (w.Wet[i] == 3) flooded++;
            }
            else if (w.LakeOf[i] < 0) { q += w.Discharge[i]; qm += w.DischargeMean[i]; }
        }
        land = Math.Max(1, land);
        return ((float)(m / land), (float)(sn / land), (float)(g / land), ponds, springs, flooded, qm > 0 ? (float)(q / qm) : 0f);
    }
}
