// Caves and karst (2026-11-01). Nothing here places a cave: caves are where soluble rock meets water that can dissolve it,
// and lava tubes are where basalt flowed down gentle ground. Built once at Terrain.Build (Terrain.Finish), from the rock,
// the relief, the rivers and the climate; fixed after that. Depends on the terrain seed alone, like the relief.
//
// Limestone. Geology makes three rocks; sedimentary rock is carbonate or clastic. Carbonate platforms grow in clear
// shallow seas away from rivers that bring sand and mud (Schlager 2005), so the carbonate share of sedimentary rock rises
// with distance from the crystalline and volcanic ground that sheds clastic sediment (CARB_L_CLASTIC), and which beds are
// carbonate is a regional draw (formations: low-frequency noise seeded by the terrain seed). The CARB_SHARE of sedimentary
// land with the highest score is carbonate (World.Carb, 0..1); rock with Carb >= CARB_MIN is karst.
// Dissolution. Rain that soaks through soil takes up the CO2 roots and decay put there: soil pCO2 rises with actual
// evapotranspiration (Brook, Folkoff & Box 1983), and water in equilibrium with calcite at that pCO2 holds
// [Ca]eq ~ pCO2^(1/3), a little less in warm water (Dreybrodt 1988). The rock removed is runoff x [Ca]eq: ~10-100 mm per
// thousand years (White 1984). Runoff and evapotranspiration are the hydrology's mean year (Hydrology.AnnualWater: Budyko,
// default climate), so a seed makes the same caves at any climate setting.
// Conduits. Karst water drains to springs where the karst meets base level: at the mouths of valleys with a stream
// (CHANNEL_A_MIN) whose drainage leaves the karst, into a river, a lake or the sea, or onto other rock (contact springs).
// The water table rises gently from each spring (CAVE_GRAD: conduit flow needs little head; Worthington 1991, Ford &
// Williams 2007), less under valleys (CAVE_VALLEY: conduits follow the fluvial pattern the karst inherits), so the lowest
// springs capture the most ground, as they do in karst (underground piracy). A Priority-Flood from the springs by that
// head over the karst gives each karst cell its head, the next cell down its conduit (CaveNext) and its spring
// (CaveSpring): a dendritic network converging on few springs (Palmer 1991). Where the ground stands below the head of the
// cell it would drain to, the water emerges there (an overflow outlet); where it stands only below its own head, the water
// table is at the ground and flows on beneath it. A river is crossed only where it stands CAVE_LOSE_H above the water
// table (a losing reach); one nearer the water table gains, and bounds the conduits. Conduit discharge is the recharge gathered along the network, and a passage forms where it reaches CAVE_Q_MIN
// (breakthrough needs flow: Dreybrodt 1990; Palmer 1991), wide as the square root of its discharge (CAVE_W1, CAVE_W_MAX)
// and the water's aggressiveness. CaveVoid is the share of the cell's area over open passage, CaveDepth the passage's
// depth below the ground (the vadose zone above the water table).
// Surface features (CaveKind bits).
// - Sinkholes: ponors where a stream from other rock reaches the karst and sinks (allogenic recharge), solution dolines
//   where diffuse recharge is fast (DOLINE_P x denudation), and collapse dolines over passages whose roof is thinner than
//   CAVE_ROOF x their width (breakdown; White & White 1969). They swallow surface water (CaveSink).
// - Ponors (bit Ponor) are the sinkholes that take a stream.
// - Losing streams: a river over karst whose bed stands CAVE_LOSE_H or more above the water table leaks CAVE_LEAK of its
//   flow per cell to the conduits; it resurges at the spring.
// - Springs: outlets whose conduit carries at least CAVE_Q_MIN.
// - Entrances: big springs (resurgence caves), collapse dolines, and valley walls that cut a passage. Passages formed when
//   base level stood higher are left dry as the valleys deepen (cave levels track river incision: Palmer 1987, Granger et
//   al. 2001); a relict level CAVE_LEVEL_M above the water table opens where a wall at least CAVE_WALL_S steep drops below
//   it beside it. Valley-wall and collapse entrances keep CAVE_ENTR_GAP cells apart (the nearer the spring is kept).
// Lava tubes. On volcanic ground, lava from a vent (a local high) runs down the steepest descent; on slopes gentler than
// LAVA_S a crust roofs the flow and it drains to a tube (Greeley 1987; Kauahikaua et al. 1998) up to LAVA_LEN cells long,
// LAVA_D below the ground; roof collapses open skylights (LAVA_SKY per cell), and its lower end is open.
//
// Water (Drain, one call from WaterCycle.Finish): sinkholes and losing streams swallow surface water, and karst groundwater
// drains through the conduits (CAVE_TAU), into the deep groundwater of the spring cell, which then holds more than the
// ground can and the rest emerges there (WaterCycle: Seep, so a strong karst spring is drinkable). Water moves from one
// store to another, so the budget closes. Shelter (Air, from Heat.Operative): cave air stays near the year's mean
// temperature (Badino 2004); an entrance keeps CAVE_SWING of the outside swing, out of the sun.
// Allocation is allowed here (construction, NR-101); Drain and Air allocate nothing.

using System;
using System.Collections.Generic;

namespace Neuroreef;

public static class Caves
{
    /// <summary>Bits of World.CaveKind (and of the WORLD frame's cave plane).</summary>
    public const byte Karst = 1, Lava = 2, Sinkhole = 4, Entrance = 8, Spring = 16, Losing = 32, Ponor = 64;

    const uint STREAM = 0x4B415253u;   // this file's own draws ("KARS"), keyed by the terrain seed

    /// <summary>What Build made: karst cells, cells over a karst passage or a lava tube, and the surface features.</summary>
    public readonly record struct Census(int Land, int KarstCells, int Passages, int LavaCells, int Sinkholes, int Springs, int Entrances, int Losing, int Ponors);

    /// <summary>Build the caves of w (Terrain.Finish, after the water): rain is the runoff rain the relief was made with.</summary>
    public static void Build(World w, float[] rain)
    {
        int FW = Map.FW, n = FW * FW;
        Array.Clear(w.Carb); Array.Clear(w.CaveVoid); Array.Clear(w.CaveDepth); Array.Clear(w.CaveFlow); Array.Clear(w.CaveSink);
        Array.Fill(w.CaveNext, -1); Array.Fill(w.CaveSpring, -1); Array.Clear(w.CaveKind);
        float sea = w.SeaLevel, mPer = K.H_RELIEF / MathF.Max(1e-6f, 1f - sea);   // metres per unit of elevation
        float scale = n / 65536f, qMin = K.CAVE_Q_MIN * scale;
        Carbonate(w);

        // the mean year's water and the rock it can dissolve
        var yld = new float[n]; var loss = new float[n];
        Hydrology.AnnualWater(w, rain, yld, loss);
        var ca = new float[n]; var den = new float[n];
        for (int y = 0; y < FW; y++)
        {
            float s = MathK.Sin(2f * MathF.PI * y / FW);
            float t0 = K.T_REF + K.CLIMATE * (K.T_EQ - K.DT_EP * s * s - K.T_REF);
            for (int i = y * FW; i < (y + 1) * FW; i++)
            {
                if (w.Carb[i] <= 0f) continue;
                float t = t0 - K.CLIMATE * K.LAPSE * MathF.Max(0f, w.Elev[i] - sea) * mPer * 0.001f;
                float p = K.P_MEAN * rain[i], aet = MathF.Max(0f, p - yld[i]);
                ca[i] = CaEq(aet, t);
                den[i] = Denudation(yld[i], ca[i]);
            }
        }

        // the karst's water table: a Priority-Flood by head from its outlets
        bool IsKarst(int i) => w.Carb[i] >= K.CARB_MIN && w.LakeOf[i] < 0;
        bool Land(int i) => w.Cell[i] == (byte)Realm.Land;
        float g = K.CAVE_GRAD * K.GEO_CELL_M / mPer, loseH = K.CAVE_LOSE_H / mPer;   // per cell, and the losing depth, in elevation
        var head = new float[n]; Array.Fill(head, float.MaxValue);
        var done = new bool[n]; var order = new int[n]; int no = 0;
        var pq = new PriorityQueue<int, (float, int)>();
        float mouthQ = K.CHANNEL_A_MIN * scale;   // a valley with a stream (Hydrology's least channel head)
        for (int i = 0; i < n; i++)
        {
            if (!IsKarst(i) || !Land(i)) continue;
            // a valley mouth: the surface drainage gathered here leaves the karst (into a river, a lake, the sea or other rock)
            int r = w.Recv[i];
            if (r >= 0 && (!Land(r) || !IsKarst(r)) && w.Flow[i] >= mouthQ) { head[i] = w.Elev[i]; pq.Enqueue(i, (head[i], i)); }
        }
        // conduits follow the valleys (the fractures and the fluvial pattern the karst inherits; the water table is lower
        // under them): the head rises less per cell under ground that gathers surface water
        var gc = new float[n];
        for (int i = 0; i < n; i++) gc[i] = g / (1f + K.CAVE_VALLEY * MathK.Log(1f + w.Flow[i] / qMin) * 1.442695f);
        while (pq.TryDequeue(out int i, out var pri))
        {
            if (done[i] || pri.Item1 != head[i]) continue;
            done[i] = true; order[no++] = i;
            int x = i % FW, y = i / FW;
            for (int k = 0; k < 8; k++)
            {
                int j = Nb(x, y, k, FW);
                if (done[j] || !IsKarst(j)) continue;
                float nh = head[i] + gc[j] * (k % 2 == 0 ? 1f : 1.4142135f);
                bool outlet = false;
                if (!Land(j)) { if (w.Elev[j] - nh < loseH) continue; }   // a river at or near the water table gains: the conduit ends beside it
                else if (w.Elev[j] < head[i]) { nh = w.Elev[j]; outlet = true; }   // the ground stands below the water table: it emerges here
                else if (w.Elev[j] < nh) nh = w.Elev[j];                           // the water table at the ground: it flows on below it
                if (nh < head[j]) { head[j] = nh; w.CaveNext[j] = outlet ? -1 : i; pq.Enqueue(j, (nh, j)); }
            }
        }
        // each karst cell's spring (outlets come before the cells that drain to them)
        for (int q = 0; q < no; q++) { int i = order[q], nx = w.CaveNext[i]; w.CaveSpring[i] = nx < 0 ? i : w.CaveSpring[nx]; }

        // sinkholes and losing streams: what the surface gives the conduits
        uint seed = Rng.Hash(w.TerrainSeed, 0, 0, STREAM);
        for (int q = 0; q < no; q++)
        {
            int i = order[q];
            float vadose = w.Elev[i] - head[i];
            if (vadose < loseH) continue;
            int x = i % FW, y = i / FW;
            bool fromOther = false;   // a stream from other rock reaches the karst here
            for (int k = 0; k < 8 && !fromOther; k++) { int j = Nb(x, y, k, FW); fromOther = w.Recv[j] == i && !IsKarst(j) && w.Flow[j] >= mouthQ; }
            if (!Land(i))
            {
                w.CaveKind[i] |= Losing;
                w.CaveSink[i] = fromOther ? K.CAVE_PONOR : K.CAVE_LEAK;
                if (fromOther) w.CaveKind[i] |= Sinkhole | Ponor;
            }
            else if (fromOther) { w.CaveSink[i] = K.CAVE_PONOR; w.CaveKind[i] |= Sinkhole | Ponor; }
            else if (Slope(w, i) * mPer / K.GEO_CELL_M < K.DOLINE_S && Rng.U(seed, 1, (uint)i, 0) < K.DOLINE_P * den[i] / K.CAVE_DEN_REF)
            { w.CaveSink[i] = 1f; w.CaveKind[i] |= Sinkhole; }
        }

        // conduit discharge: recharge gathered down the network (cells of mean land rain), sources first
        var qc = w.CaveFlow;
        for (int q = no - 1; q >= 0; q--)
        {
            int i = order[q];
            if (Land(i)) qc[i] += w.Carb[i] * yld[i] / K.P_MEAN;
            qc[i] += w.CaveSink[i] * w.Flow[i];
            int nx = w.CaveNext[i];
            if (nx >= 0) qc[nx] += qc[i];
        }

        // passages, springs, collapse dolines and entrances
        float level = K.CAVE_LEVEL_M / mPer;
        for (int q = 0; q < no; q++)
        {
            int i = order[q];
            if (qc[i] < qMin) continue;
            float wd = MathF.Min(K.CAVE_W_MAX, K.CAVE_W1 * MathF.Sqrt(qc[i] / qMin) * ca[i] / K.CA_REF);
            float depth = MathF.Max(0f, w.Elev[i] - head[i]) * mPer;
            w.CaveKind[i] |= Karst;
            w.CaveVoid[i] = MathF.Min(1f, wd / K.GEO_CELL_M);
            w.CaveDepth[i] = depth;
            if (!Land(i)) continue;
            if (w.CaveSpring[i] == i)
            {
                w.CaveKind[i] |= Spring;
                if (qc[i] >= K.CAVE_RESURGE * qMin) w.CaveKind[i] |= Entrance;   // a resurgence cave
                continue;
            }
            if (depth >= K.CAVE_ROOF_MIN && depth < K.CAVE_ROOF * wd && Rng.U(seed, 2, (uint)i, 0) < K.COLLAPSE_P && !NearEntrance(w, i, K.CAVE_ENTR_GAP))
            { w.CaveKind[i] |= Sinkhole | Entrance; w.CaveSink[i] = 1f; continue; }   // the roof fell in
            // a relict passage at the higher level: the ground beside it drops below it (a valley wall or cliff cuts it);
            // one cave per CAVE_ENTR_GAP cells of wall (the nearest to the spring: its passage is the biggest)
            float rel = head[i] + level;
            if (w.Elev[i] - rel < K.CAVE_ROOF_MIN / mPer || NearEntrance(w, i, K.CAVE_ENTR_GAP)) continue;
            int x = i % FW, y = i / FW;
            float wall = K.CAVE_WALL_S * K.GEO_CELL_M / mPer;   // the drop of a valley wall or cliff to the next cell
            for (int k = 0; k < 8; k++)
            {
                int j = Nb(x, y, k, FW);
                if (Land(j) && w.Elev[j] < rel && w.Elev[i] - w.Elev[j] >= wall * (k % 2 == 0 ? 1f : 1.4142135f)) { w.CaveKind[i] |= Entrance; break; }
            }
        }
        // a sinkhole drains to its spring; one that is not over the network (none) swallows nothing
        for (int i = 0; i < n; i++) if (w.CaveSink[i] > 0f && w.CaveSpring[i] < 0) { w.CaveSink[i] = 0f; w.CaveKind[i] &= unchecked((byte)~(Sinkhole | Losing | Ponor)); }

        LavaTubes(w, seed, mPer);
    }

    // ---- the rock

    /// <summary>Carbonate share of each cell's rock (World.Carb): Geology's limestone (Rock.Limestone) is wholly carbonate; of the
    /// other sedimentary rock, that far from clastic sources in the formations a regional draw makes carbonate, CARB_SHARE of it.</summary>
    static void Carbonate(World w)
    {
        int FW = Map.FW, n = FW * FW;
        var src = new byte[n]; var dist = new float[n];
        for (int i = 0; i < n; i++) src[i] = (byte)(w.Rock[i] != Rock.Sedimentary && w.Rock[i] != Rock.Limestone && w.Elev[i] >= w.SeaLevel ? 1 : 0);
        Terrain.Distance(src, 1, dist);
        var r = new Rng.Seq(w.TerrainSeed * 40503u + 0x4B41u);
        var formation = Terrain.Fbm(ref r, 4, 3);
        var score = new float[n]; var land = new List<float>();
        for (int y = 0; y < FW; y++)
            for (int x = 0; x < FW; x++)
            {
                int i = y * FW + x;
                if (w.Rock[i] != Rock.Sedimentary) { score[i] = float.MinValue; continue; }
                float far = 1f - MathK.Exp(-dist[i] / K.CARB_L_CLASTIC);
                score[i] = formation(x / (float)FW, y / (float)FW) + K.CARB_FAR_W * far;
                if (w.Elev[i] >= w.SeaLevel) land.Add(score[i]);
            }
        if (land.Count == 0) return;
        land.Sort();
        float lo = land[Math.Clamp((int)((1f - K.CARB_SHARE) * land.Count), 0, land.Count - 1)];
        float hi = land[Math.Clamp((int)((1f - 0.5f * K.CARB_SHARE) * land.Count), 0, land.Count - 1)];
        float span = MathF.Max(1e-6f, hi - lo);
        // the carbonate share climbs from CARB_MIN at the threshold to 1 halfway through the carbonate's scores
        for (int i = 0; i < n; i++)
            if (w.Rock[i] == Rock.Limestone) w.Carb[i] = 1f;   // Geology's own limestone platforms are carbonate through and through
            else if (score[i] >= lo) w.Carb[i] = MathF.Min(1f, K.CARB_MIN + (1f - K.CARB_MIN) * (score[i] - lo) / span);
            else if (score[i] > float.MinValue) w.Carb[i] = MathF.Max(0f, K.CARB_MIN * (1f - (lo - score[i]) / span));
    }

    /// <summary>Calcium in water at equilibrium with calcite (mmol/L) under the soil air of ground evaporating aet (mm a year)
    /// at t (°C): soil pCO2 from Brook, Folkoff &amp; Box (1983), [Ca]eq = CA_REF (pCO2 / 0.01 atm)^(1/3) (Dreybrodt 1988),
    /// CA_T less per K above 10 °C (calcite and CO2 are less soluble in warm water).</summary>
    public static float CaEq(float aet, float t)
    {
        float lg = -3.47f + 2.09f * (1f - MathK.Exp(-0.00172f * aet));       // log10 pCO2 (atm)
        float pco2 = MathK.Exp(lg * 2.302585f);
        return K.CA_REF * MathK.Cbrt(pco2 / 0.01f) * MathF.Max(0.3f, 1f - K.CA_T * (t - 10f));
    }

    /// <summary>Karst denudation (mm per thousand years) of runoff q (mm a year) carrying ca (mmol/L) of dissolved calcite: q x
    /// [Ca] x M(CaCO3) / rho (White 1984), 100 g/mol and 2,700 kg/m3.</summary>
    public static float Denudation(float q, float ca) => q * ca * (0.1f / 2700f) * 1000f;

    static void LavaTubes(World w, uint seed, float mPer)
    {
        int FW = Map.FW, n = FW * FW, R = K.LAVA_VENT_R;
        float skyP = K.LAVA_SKY, vw = MathF.Min(1f, K.LAVA_W / K.GEO_CELL_M);
        for (int i = 0; i < n; i++)
        {
            if (w.Rock[i] != Rock.Volcanic || w.Cell[i] != (byte)Realm.Land) continue;
            int x = i % FW, y = i / FW; float e = w.Elev[i]; bool top = true;
            for (int dy = -R; dy <= R && top; dy++)
                for (int dx = -R; dx <= R; dx++)
                {
                    int j = World.WrapI(y + dy, FW) * FW + World.WrapI(x + dx, FW);
                    if (j != i && w.Rock[j] == Rock.Volcanic && (w.Elev[j] > e || (w.Elev[j] == e && j < i))) { top = false; break; }
                }
            if (!top || Rng.U(seed, 3, (uint)i, 0) >= K.LAVA_VENT_P) continue;
            // the flow runs down the steepest descent; a tube where the ground is gentle
            int c = i, last = -1, run = 0;
            for (int s = 0; s < K.LAVA_LEN; s++)
            {
                int cx = c % FW, cy = c / FW, nx = -1; float best = 0f;
                for (int k = 0; k < 8; k++)
                {
                    int j = Nb(cx, cy, k, FW);
                    float d = (w.Elev[c] - w.Elev[j]) / (k % 2 == 0 ? 1f : 1.4142135f);
                    if (d > best) { best = d; nx = j; }
                }
                if (nx < 0 || w.Cell[nx] != (byte)Realm.Land || w.Rock[nx] != Rock.Volcanic) break;
                float slope = best * mPer / K.GEO_CELL_M;
                if (slope <= K.LAVA_S)
                {
                    w.CaveKind[nx] |= Lava;
                    if (run > 0 && w.CaveSpring[c] < 0) w.CaveNext[c] = nx;   // the tube's course (no karst water runs in it)
                    w.CaveVoid[nx] = MathF.Max(w.CaveVoid[nx], vw);
                    if (w.CaveDepth[nx] <= 0f) w.CaveDepth[nx] = K.LAVA_D;
                    if (Rng.U(seed, 4, (uint)nx, 0) < skyP) w.CaveKind[nx] |= Entrance;   // a skylight
                    last = nx; run++;
                }
                else if (run > 0) { if (run >= 3) w.CaveKind[last] |= Entrance; run = 0; }
                c = nx;
            }
            if (run >= 3) w.CaveKind[last] |= Entrance;   // the open lower end
        }
    }

    // ---- the water cycle and life

    /// <summary>
    /// WaterCycle.Finish, for a cell on the karst network (CaveSpring >= 0) before its own water is routed; qc is the surface
    /// water reaching it (inflow + runoff). A sinkhole or losing stream swallows CaveSink of it (at most CAVE_CAP x its mean
    /// intake: a flood beyond backs up and runs on), and karst groundwater drains through the conduits (CAVE_TAU); both go to
    /// the deep groundwater of the spring cell, where what the ground cannot hold emerges. Returns the surface water left.
    /// </summary>
    public static float Drain(World w, int c, float qc)
    {
        int sp = w.CaveSpring[c];
        if (sp == c || sp < 0) return qc;
        float unit = K.P_MEAN * WaterCycle.YearFrac;   // mm x cells per update of one cell of mean land rain
        float s = w.CaveSink[c];
        if (s > 0f && qc > 0f)
        {
            float take = MathF.Min(qc * s, K.CAVE_CAP * s * w.Flow[c] * unit);
            qc -= take; w.Deep[sp] += take;
        }
        if (w.Cell[c] == (byte)Realm.Land)
        {
            float d = w.Deep[c] * MathF.Min(1f, 365f * WaterCycle.YearFrac / K.CAVE_TAU) * w.Carb[c];
            w.Deep[c] -= d; w.Deep[sp] += d;
        }
        return qc;
    }

    /// <summary>Operative temperature in a cave entrance whose outside air is t (Heat.Operative): the year's mean there, with
    /// CAVE_SWING of the outside's departure from it, and no sun (Badino 2004).</summary>
    public static float Air(World w, int cell, float t)
    {
        float m = Climate.AnnualMean(w, cell);
        return m + K.CAVE_SWING * (t - m);
    }

    /// <summary>Does this cell offer cave shelter (an entrance: a resurgence, a collapse, a valley-wall opening or a skylight)?</summary>
    public static bool Shelter(World w, int cell) => (w.CaveKind[cell] & Entrance) != 0;

    // ---- reporting

    public static Census Count(World w)
    {
        int n = Map.FW * Map.FW, land = 0, karst = 0, pass = 0, lava = 0, sink = 0, spring = 0, ent = 0, lose = 0, ponor = 0;
        for (int i = 0; i < n; i++)
        {
            byte b = w.CaveKind[i];
            if (w.Cell[i] == (byte)Realm.Land) land++;
            if (w.Carb[i] >= K.CARB_MIN && w.LakeOf[i] < 0) karst++;
            if ((b & Karst) != 0) pass++;
            if ((b & Lava) != 0) lava++;
            if ((b & Sinkhole) != 0) sink++;
            if ((b & Ponor) != 0) ponor++;
            if ((b & Spring) != 0) spring++;
            if ((b & Entrance) != 0) ent++;
            if ((b & Losing) != 0) lose++;
        }
        return new Census(land, karst, pass, lava, sink, spring, ent, lose, ponor);
    }

    static int Nb(int x, int y, int k, int FW) => World.WrapI(y + DY[k], FW) * FW + World.WrapI(x + DX[k], FW);
    // even k: the 4 sides; odd k: the diagonals
    static readonly int[] DX = { 1, 1, 0, -1, -1, -1, 0, 1 }, DY = { 0, 1, 1, 1, 0, -1, -1, -1 };

    static bool NearEntrance(World w, int i, int r)
    {
        int FW = Map.FW, x = i % FW, y = i / FW;
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                if ((w.CaveKind[World.WrapI(y + dy, FW) * FW + World.WrapI(x + dx, FW)] & Entrance) != 0) return true;
        return false;
    }

    // largest drop to a neighbour, elevation per cell
    static float Slope(World w, int i)
    {
        int FW = Map.FW, x = i % FW, y = i / FW; float e = w.Elev[i], d = 0f;
        for (int k = 0; k < 8; k++) d = MathF.Max(d, (e - w.Elev[Nb(x, y, k, FW)]) / (k % 2 == 0 ? 1f : 1.4142135f));
        return d;
    }
}
