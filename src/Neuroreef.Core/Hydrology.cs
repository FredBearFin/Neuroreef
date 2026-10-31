// Hydrology at world generation (Revision Q): where the rain goes. Nothing here names a river, a valley or a
// floodplain: rain falls where Climate.Rainfall puts it, runs downhill over the heightmap, cuts the ground where
// much water runs steeply, and drops what it carries where the ground flattens. Rivers, valleys, alluvial soil
// and floodplains are what follows.
//
// Routing: Priority-Flood+epsilon (Barnes, Lehman & Mulla 2014) from the lakes outward. Every land cell gets one
// receiver (its lowest neighbour on the filled surface, D8), a closed basin drains over its lowest rim, and the
// pop order is a topological order (each cell comes after the cell it drains into). Flow is the rain each cell
// gathers from all the cells above it (rain-weighted flow accumulation), so it is exactly the rain that falls.
// Erosion: detachment-limited stream power, dh/dt = -K A^m S (Howard 1994; Whipple & Tucker 1999), solved
// implicitly downstream-first (Braun & Willett 2013), so it is stable at any step. Transport: what is cut is
// carried down; where the load is more than the stream can carry (capacity ~ A^m S) a share of the surplus is
// dropped, and a closed basin keeps all it can hold up to its rim. Alluvium is what is left of that deposit.
// Standing and running water (2026-10-31, Waters): the sea is the low ground below sea level; every closed depression
// above it holds the lake its water balance allows (inflow against evaporation), full to its spill point in a humid
// basin, smaller and terminal in a dry one; rivers start where discharge and slope pass the channel-initiation
// threshold and run on to their lake or the sea, as wide and deep as their discharge makes them.
// Allocation is allowed here (it is part of construction, NR-101).

using System;
using System.Collections.Generic;

namespace Neuroreef;

public static class Hydrology
{
    /// <summary>The drainage of a heightmap: receiver (-1 at a lake), the cells in topological order (outlets first),
    /// the depression-filled surface and the rain each cell gathers.</summary>
    public sealed class Net
    {
        public readonly int[] Recv, Order;
        public readonly float[] Fill, Flow;
        public Net(int n) { Recv = new int[n]; Order = new int[n]; Fill = new float[n]; Flow = new float[n]; }
    }

    /// <summary>What an erosion pass did (whole-map sums, in elevation x cells).</summary>
    public struct Budget { public double Eroded, Deposited, ToLakes; }

    static float Dist(int a, int b, int FW) => (a % FW != b % FW && a / FW != b / FW) ? 1.4142135f : 1f;

    /// <summary>
    /// Route rain over h on the torus. Cells below sea are lakes, the outlets; if there are none the lowest cell is.
    /// Flow[i] = rain[i] + the Flow of every cell draining into i; at the lakes it sums to the whole map's rain.
    /// </summary>
    public static void Route(float[] h, float sea, float[] rain, Net net) => Route(h, sea, rain, net, Map.FW, null, null);

    /// <summary>Route on a torus of side FW (Geology's coarse grid); seen and pq are optional scratch reused across calls.</summary>
    internal static void Route(float[] h, float sea, float[] rain, Net net, int FW, bool[]? seen, PriorityQueue<int, float>? pq)
    {
        int n = FW * FW;
        int[] recv = net.Recv, order = net.Order; float[] fill = net.Fill, flow = net.Flow;
        if (seen == null) seen = new bool[n]; else Array.Clear(seen, 0, n);
        if (pq == null) pq = new PriorityQueue<int, float>(n); else pq.Clear();
        int lo = 0;
        for (int i = 0; i < n; i++)
        {
            if (h[i] < h[lo]) lo = i;
            if (h[i] < sea) { seen[i] = true; fill[i] = h[i]; recv[i] = -1; pq.Enqueue(i, h[i]); }
        }
        if (pq.Count == 0) { seen[lo] = true; fill[lo] = h[lo]; recv[lo] = -1; pq.Enqueue(lo, h[lo]); }
        int k = 0;
        while (pq.TryDequeue(out int c, out float z))
        {
            order[k++] = c;
            int x = c % FW, y = c / FW;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int j = World.WrapI(y + dy, FW) * FW + World.WrapI(x + dx, FW);
                    if (seen[j]) continue;
                    seen[j] = true;
                    fill[j] = MathF.Max(h[j], z + K.FLOW_EPS);
                    recv[j] = c;
                    pq.Enqueue(j, fill[j]);
                }
        }
        for (int i = 0; i < n; i++) flow[i] = rain[i];
        for (int q = n - 1; q >= 0; q--) { int c = order[q], r = recv[c]; if (r >= 0) flow[r] += flow[c]; }
    }

    /// <summary>
    /// Stream-power incision and deposition over iters passes, changing h in place (lakes, below sea, are base level and
    /// do not change). alluv receives the depth of sediment left on each cell. kRock (Revision R, optional): each cell's
    /// erodibility against soft sedimentary rock (Geology.Erodibility); hard rock cuts slower. Returns the mass budget.
    /// </summary>
    public static Budget Erode(float[] h, float sea, float[] rain, float[] alluv, int iters,
        float kErode = K.ERODE_K, float kCarry = K.CARRY_K, float drop = K.DEPOSIT_FRAC, float kCreep = K.CREEP_K, float[]? kRock = null)
    {
        int FW = Map.FW, n = FW * FW;
        var net = new Net(n);
        var cut = new float[n]; var load = new float[n]; var h0 = new float[n];
        var b = new Budget();
        Array.Clear(alluv);
        for (int it = 0; it < iters; it++)
        {
            // hillslope creep, dh/dt = kappa del^2 h (Culling 1960): soil moves down every slope, filling hollows and
            // rounding crests; what creeps into a lake is lost to it. The same pairwise flux leaves one cell and enters the other.
            if (kCreep > 0f)
            {
                Array.Copy(h, h0, n);
                for (int i = 0; i < n; i++)
                {
                    if (h0[i] < sea) continue;
                    int x = i % FW, y = i / FW;
                    int l = y * FW + (x == 0 ? FW - 1 : x - 1), r = y * FW + (x == FW - 1 ? 0 : x + 1);
                    int u = World.WrapI(y - 1, FW) * FW + x, d = World.WrapI(y + 1, FW) * FW + x;
                    float dh = Creep(h0, i, l, sea, kCreep, ref b) + Creep(h0, i, r, sea, kCreep, ref b)
                             + Creep(h0, i, u, sea, kCreep, ref b) + Creep(h0, i, d, sea, kCreep, ref b);
                    h[i] = h0[i] + dh;
                }
            }
            Route(h, sea, rain, net);
            int[] recv = net.Recv, order = net.Order; float[] flow = net.Flow, fill = net.Fill;
            // incision, outlets first: h' = (h + F h'_r) / (1 + F), F = K A^m / dx (implicit for n = 1)
            for (int q = 0; q < n; q++)
            {
                int c = order[q], r = recv[c];
                cut[c] = 0f;
                if (r < 0 || h[c] <= h[r]) continue;        // a lake, or the floor of a closed basin: no slope to cut
                // bedrock under alluvium is not cut until the alluvium is gone: sediment is soft (kRock 1)
                float kr = kRock == null || alluv[c] > 0f ? 1f : kRock[c];
                float F = kErode * kr * MathK.Pow(flow[c], K.ERODE_M) / Dist(c, r, FW);
                float hn = (h[c] + F * h[r]) / (1f + F);
                cut[c] = h[c] - hn; h[c] = hn;
            }
            // transport, sources first: the load runs down; a surplus over capacity is partly dropped, a basin fills to its rim
            Array.Clear(load);
            for (int q = n - 1; q >= 0; q--)
            {
                int c = order[q], r = recv[c];
                float s = load[c] + cut[c];
                b.Eroded += cut[c];
                alluv[c] = MathF.Max(0f, alluv[c] - cut[c]);
                if (r < 0) { b.ToLakes += s; continue; }
                float slope = (h[c] - h[r]) / Dist(c, r, FW);
                float dep;
                if (slope <= 0f) dep = MathF.Min(s, MathF.Max(0f, fill[c] - h[c]));
                else
                {
                    float cap = kCarry * MathK.Pow(flow[c], K.ERODE_M) * slope;
                    dep = s > cap ? drop * (s - cap) : 0f;
                }
                h[c] += dep; alluv[c] += dep; b.Deposited += dep;
                load[r] += s - dep;
            }
        }
        return b;
    }

    // creep between land cell i and neighbour j; a lake (below sea) keeps its bed, so what goes into it is lost to it
    static float Creep(float[] h0, int i, int j, float sea, float k, ref Budget b)
    {
        float f = k * (h0[j] - h0[i]);
        if (h0[j] < sea) b.ToLakes -= f;   // land is above every lake, so this only ever flows into it
        return f;
    }

    /// <summary>
    /// Floodplains (Revision Q): each land cell within FLOOD_R cells upstream of a river cell it drains into, and lower
    /// than FLOOD_HAND x SHALLOW above it (height above the nearest drainage, Rennó et al. 2008), gets the wetness
    /// (season x this year's rain against the average) at which that river's stage reaches it; others get +inf.
    /// A river's stage goes as discharge^RIVER_DEPTH_EXP (Leopold & Maddock 1953) and tops its banks at BANKFULL.
    /// 2026-10-31: a river is every water cell outside the sea and the lakes (LakeOf -1), and the river that floods a
    /// cell is the channel cell of that river's cross-section (World.FloodFrom of a river cell, set by Waters), whose
    /// discharge the water cycle carries; height is measured from the channel's bed.
    /// </summary>
    public static void Floodplain(World w, Net net)
    {
        int FW = Map.FW, n = FW * FW;
        var drain = new int[n]; var steps = new int[n];
        float bank = MathK.Pow(K.BANKFULL, K.RIVER_DEPTH_EXP);
        for (int q = 0; q < n; q++)
        {
            int c = net.Order[q], r = net.Recv[c];
            w.FloodWet[c] = float.PositiveInfinity;
            if (w.Cell[c] != (byte)Realm.Land)
            {
                drain[c] = w.LakeOf[c] < 0 ? w.FloodFrom[c] : -1; steps[c] = 0;   // a river cell: its channel (Waters)
                if (drain[c] < 0) w.FloodFrom[c] = -1;
                continue;
            }
            w.FloodFrom[c] = -1;
            if (r < 0) { drain[c] = -1; continue; }
            drain[c] = drain[r]; steps[c] = steps[r] + 1;
            int d = drain[c];
            if (d < 0 || steps[c] > K.FLOOD_R) continue;
            float above = w.Elev[c] - w.Elev[d];
            if (above > K.FLOOD_HAND * K.SHALLOW) continue;
            // stage / mean depth = wet^f; it reaches the cell when Depth (wet^f - BANKFULL^f) >= height above the river
            w.FloodWet[c] = MathK.Pow(bank + MathF.Max(0f, above) / w.Depth[d], 1f / K.RIVER_DEPTH_EXP);
            w.FloodFrom[c] = d;
        }
    }

    // ================================================================== standing and running water (2026-10-31)

    /// <summary>
    /// The water of a worked relief, from its water balance: the sea, the lakes, the drainage and the rivers. Sets
    /// LakeOf, LakeCount and the per-lake arrays (area, depth, sill, exit, downstream lake, upstream-first rank), Depth
    /// and Cell for every cell, Flow (mean yearly discharge, in cells of mean land rain: a cell of mean rain that all ran
    /// off gives 1), Recv and DrainOrder (the drainage the water cycle routes over), and for river cells FloodFrom (the
    /// channel cell of their cross-section). Returns that drainage as a Net (Fill: the depression-filled surface without
    /// epsilon, so Fill - Elev is how deep a cell lies in a closed hollow).
    /// <list type="number">
    /// <item>The sea: every connected region below SeaLevel (the Nature.Water quantile) of SEA_MIN of the map or more, and
    /// the largest. A smaller low is a closed basin like any other.</item>
    /// <item>Priority-Flood from the sea (Barnes, Lehman &amp; Mulla 2014) fills every depression to its spill point. Each
    /// depression's water balance, sources first (Fill-Spill: Barnes, Callaghan &amp; Wickert 2020): what runs into it
    /// (the land's mean yield P - ET, Budyko's curve in Fu's form, and what lakes upstream spill) against what its lake
    /// loses (open-water evaporation less the rain on it). Its lake rises from the floor, cell by cell in the order a
    /// rising pool floods them, while its inflow still pays for its evaporation; if it reaches the spill point it spills
    /// what is left downstream, otherwise it is a terminal lake and nothing leaves (Mifflin 1988's closed-basin lakes).
    /// So humid basins fill to their sills, arid ones hold shrunken lakes or none, and no lake stands above its sill.</item>
    /// <item>Drainage: each cell's receiver on the filled surface; in a terminal lake's basin the dry floor drains to the
    /// lake. Flow is the yield gathered along it, and every full lake passes its surplus on through its exit.</item>
    /// <item>Channels start where Q S^2 >= CHANNEL_C (Montgomery &amp; Dietrich 1992) or Q >= RIVER_ACC, and run on to their
    /// lake or the sea. Width ~ Q^0.5 and depth ~ Q^0.4 (Leopold &amp; Maddock 1953); a river wider than a cell covers the
    /// valley floor beside its channel.</item>
    /// </list>
    /// </summary>
    public static Net Waters(World w, float[] rain)
    {
        int FW = Map.FW, n = FW * FW;
        float[] h = w.Elev;
        float sea = w.SeaLevel, scale = n / 65536f;
        int[] lake = w.LakeOf;
        Array.Fill(lake, -1);

        // 1. the year's water at each cell: the land's yield and a lake's net loss (mm a year)
        var yld = new float[n]; var loss = new float[n];
        AnnualWater(w, rain, yld, loss);

        // 2. the sea
        int nl = Seas(w, lake), ns = nl;
        var level = new float[n];
        for (int l = 0; l < nl; l++) level[l] = sea;

        // 3. Priority-Flood from the sea: receivers, order, and the exact fill (spill levels)
        var recv = new int[n]; var order = new int[n]; var fill0 = new float[n]; var pos = new int[n];
        Flood(h, lake, level, recv, order, fill0);
        for (int q = 0; q < n; q++) pos[order[q]] = q;

        // depressions: connected cells under the same spill level
        var comp = new int[n]; Array.Fill(comp, -1);
        var stack = new int[n];
        var cStart = new List<int>(); var cells = new int[n]; int nc = 0, top = 0;
        var cSpill = new List<float>(); var cEntry = new List<int>(); var cLow = new List<int>();
        for (int i0 = 0; i0 < n; i0++)
        {
            if (lake[i0] >= 0 || comp[i0] >= 0 || !(fill0[i0] > h[i0])) continue;
            float s = fill0[i0];
            int sp = 0, first = top, entry = i0, low = i0;
            stack[sp++] = i0; comp[i0] = nc;
            while (sp > 0)
            {
                int c = stack[--sp]; cells[top++] = c;
                if (pos[c] < pos[entry]) entry = c;
                if (h[c] < h[low] || (h[c] == h[low] && c < low)) low = c;
                int x = c % FW, y = c / FW;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int j = World.WrapI(y + dy, FW) * FW + World.WrapI(x + dx, FW);
                        if (comp[j] < 0 && lake[j] < 0 && fill0[j] == s && fill0[j] > h[j]) { comp[j] = nc; stack[sp++] = j; }
                    }
            }
            Array.Sort(cells, first, top - first);   // a fixed order within each depression, whatever the search took
            cStart.Add(first); cSpill.Add(s); cEntry.Add(entry); cLow.Add(low);
            nc++;
        }
        cStart.Add(top);
        var entryOf = new int[n]; Array.Fill(entryOf, -1);
        for (int k = 0; k < nc; k++) entryOf[cEntry[k]] = k;

        // 4. the water balance of every depression, sources first; lakes rise in them as far as their inflow pays for
        var acc = new double[n];
        var lakeOut = new double[n];
        var compLake = new int[nc]; Array.Fill(compLake, -1);
        var stamp = new int[n]; Array.Fill(stamp, -1);
        var pq = new PriorityQueue<int, float>();
        var pool = new List<int>();
        float mmPerElev = K.H_RELIEF * 1000f / MathF.Max(1e-6f, 1f - sea);
        for (int q = n - 1; q >= 0; q--)
        {
            int c = order[q];
            if (lake[c] >= 0) continue;   // the sea
            acc[c] += yld[c];
            int k = entryOf[c];
            if (k >= 0)
            {
                int a = cStart[k], b = cStart[k + 1], low = cLow[k];
                float S = cSpill[k];
                if (b - a >= K.LAKE_MIN_CELLS && S - h[low] >= K.LAKE_MIN_DEPTH)
                {
                    // the pool rises from the floor: each next cell is the lowest one it touches (a sub-basin beyond a
                    // saddle fills to the saddle before the level rises on)
                    double budget = acc[c];
                    pool.Clear(); pq.Clear();
                    pq.Enqueue(low, h[low]); stamp[low] = k;
                    float top0 = h[low], next = S;
                    while (pq.TryDequeue(out int j, out float hj))
                    {
                        double cost = (double)yld[j] + loss[j];
                        if (budget - cost < 0) { next = MathF.Min(S, hj); break; }
                        budget -= cost; pool.Add(j); top0 = MathF.Max(top0, hj);
                        int x = j % FW, y = j / FW;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int m = World.WrapI(y + dy, FW) * FW + World.WrapI(x + dx, FW);
                                if (comp[m] == k && stamp[m] != k) { stamp[m] = k; pq.Enqueue(m, h[m]); }
                            }
                    }
                    bool full = pool.Count == b - a;
                    if (pool.Count > 0)
                    {
                        int id = nl++;
                        // a terminal lake stands between its highest flooded cell and the next (it would flood that next)
                        level[id] = full ? S : MathF.Min(S, top0 + MathF.Max(0.5f * (next - top0), 1e-4f));
                        float deep = 0f;
                        foreach (int j in pool) { lake[j] = id; deep = MathF.Max(deep, level[id] - h[j]); }
                        compLake[k] = id;
                        w.LakeArea[id] = pool.Count;
                        w.LakeDepth[id] = MathF.Max(1f, deep * mmPerElev);
                        w.LakeSill[id] = (S - level[id]) * mmPerElev;
                        w.LakeExit[id] = recv[c];   // the cell over its spill point
                        lakeOut[id] = full ? budget : 0.0;
                    }
                    // a terminal lake's basin passes nothing on; one that cannot keep even a cell of lake lets its water
                    // through (its pond sites hold what they can, as the water cycle has it)
                    acc[c] = full || pool.Count == 0 ? budget : 0.0;
                }
            }
            int r = recv[c];
            if (r >= 0 && lake[r] < 0) acc[r] += acc[c];
        }
        for (int l = 0; l < ns; l++)
        {
            w.LakeArea[l] = 0; w.LakeSill[l] = 0f; w.LakeExit[l] = -1; float deep = 0f;
            for (int i = 0; i < n; i++) if (lake[i] == l) { w.LakeArea[l]++; deep = MathF.Max(deep, sea - h[i]); }
            w.LakeDepth[l] = MathF.Max(1f, deep * mmPerElev);
        }
        w.LakeCount = nl;

        // 5. the drainage the water cycle runs on: the filled surface; in a terminal lake's basin the dry floor drains to
        // the lake (a flood from the lake over its own basin's cells); standing water is an outlet
        var net = new Net(n);
        int[] rv = net.Recv;
        Array.Copy(recv, rv, n);
        for (int i = 0; i < n; i++) if (lake[i] >= 0) rv[i] = -1;
        for (int k = 0; k < nc; k++)
        {
            int id = compLake[k];
            if (id < 0 || w.LakeArea[id] == cStart[k + 1] - cStart[k]) continue;
            pq.Clear();
            var fl = net.Fill;   // scratch here; set to fill0 below
            for (int q = cStart[k]; q < cStart[k + 1]; q++) { int c = cells[q]; stamp[c] = -1; if (lake[c] == id) { stamp[c] = -2; fl[c] = level[id]; pq.Enqueue(c, level[id]); } }
            while (pq.TryDequeue(out int c, out float z))
            {
                int x = c % FW, y = c / FW;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int m = World.WrapI(y + dy, FW) * FW + World.WrapI(x + dx, FW);
                        if (comp[m] != k || stamp[m] == -2) continue;
                        stamp[m] = -2; rv[m] = c; fl[m] = MathF.Max(h[m], z + K.FLOW_EPS); pq.Enqueue(m, fl[m]);
                    }
            }
        }
        Array.Copy(fill0, net.Fill, n);
        if (!TopoOrder(rv, net.Order)) throw new InvalidOperationException("Hydrology.Waters: the drainage has a cycle");
        Array.Copy(rv, w.Recv, n); Array.Copy(net.Order, w.DrainOrder, n);

        // lakes upstream first (a lake's exit lies further from the sea in the flood order than any lake it spills into)
        var rank = new int[nl]; var key = new int[nl];
        for (int l = 0; l < nl; l++) { rank[l] = l; key[l] = l < ns ? -1 - l : -pos[w.LakeExit[l]] - ns - 1; }
        Array.Sort(key, rank);
        Array.Copy(rank, w.LakeRank, nl);

        // 6. mean discharge: the yield gathered down the drainage, and each full lake's surplus down its exit
        float[] Q = net.Flow;
        var qd = new double[n];
        for (int i = 0; i < n; i++) qd[i] = lake[i] >= 0 ? 0.0 : yld[i];
        int[] ord = net.Order;
        for (int q = n - 1; q >= 0; q--) { int c = ord[q], r = rv[c]; if (r >= 0) qd[r] += qd[c]; }
        for (int t = 0; t < nl; t++)
        {
            int l = w.LakeRank[t];
            w.LakeDown[l] = -1;
            if (l < ns) continue;
            int c = w.LakeExit[l];
            while (c >= 0 && lake[c] < 0) { qd[c] += lakeOut[l]; c = rv[c]; }
            w.LakeDown[l] = c >= 0 ? lake[c] : -1;
        }
        for (int i = 0; i < n; i++) Q[i] = lake[i] >= 0 ? 0f : (float)(qd[i] / K.P_MEAN);
        Array.Copy(Q, w.Flow, n);

        // 7. channels: heads by discharge and slope, and every cell below a head down to its lake or the sea
        var chan = new bool[n];
        float aMin = K.CHANNEL_A_MIN * scale, aBig = K.RIVER_ACC * scale;
        for (int q = n - 1; q >= 0; q--)
        {
            int c = ord[q], r = rv[c];
            if (lake[c] >= 0 || r < 0) continue;
            // a head is fed from above: what runs in from upstream decides (a channel cell is water, and the rain on
            // its own ground never reaches the water cycle's streams)
            float qin = Q[c] - (float)(yld[c] / K.P_MEAN);
            if (!chan[c] && qin >= aMin)
            {
                float down = lake[r] >= 0 ? level[lake[r]] : h[r];
                float s = MathF.Max(0f, h[c] - down) / Dist(c, r, FW);
                if (qin >= aBig || qin * s * s >= K.CHANNEL_C) chan[c] = true;
            }
            if (chan[c] && lake[r] < 0) chan[r] = true;
        }

        // 8. hydraulic geometry: depth on every cell, a channel's width over its valley floor
        Array.Clear(w.Depth);
        var dep = w.Depth; var of = w.FloodFrom;
        Array.Fill(of, -1);
        for (int i = 0; i < n; i++)
            if (lake[i] >= 0) dep[i] = MathF.Max(level[lake[i]] - h[i], 1e-5f);
        for (int c = 0; c < n; c++)
        {
            if (!chan[c]) continue;
            float qr = Q[c] / (K.RIVER_Q1 * scale);
            float d = K.SHALLOW * MathF.Min(1f, K.RIVER_D * MathK.Pow(qr, K.RIVER_DEPTH_EXP));
            dep[c] = MathF.Max(d, 1e-5f); of[c] = c;
            // the river's half-width beyond its channel cell: every eligible cell within the whole cells of it, and the
            // lowest one of the next ring for a half cell more (a river 2 cells wide spreads to its lower side)
            float wd = MathF.Min(K.RIVER_W_MAX, MathF.Sqrt(MathF.Max(1f, qr)));
            float rad = 0.5f * (wd - 1f);
            int full = (int)rad, ring = full + 1;
            bool half = rad - full >= 0.5f;
            if (full == 0 && !half) continue;
            int x = c % FW, y = c / FW, best = -1, bestR2 = 0;
            for (int dy = -ring; dy <= ring; dy++)
                for (int dx = -ring; dx <= ring; dx++)
                {
                    int r2 = dx * dx + dy * dy;
                    if (r2 == 0 || r2 > ring * ring + ring) continue;
                    int j = World.WrapI(y + dy, FW) * FW + World.WrapI(x + dx, FW);
                    if (lake[j] >= 0 || chan[j] || h[j] > h[c] + K.RIVER_BANK * K.SHALLOW) continue;
                    if (r2 <= full * full + full) Bank(j, r2);
                    else if (half && (best < 0 || h[j] < h[best] || (h[j] == h[best] && j < best))) { best = j; bestR2 = r2; }
                }
            if (best >= 0) Bank(best, bestR2);

            // a parabolic cross-section: shallower toward the banks
            void Bank(int j, int r2)
            {
                float dj = d * MathF.Max(0.1f, 1f - r2 / ((rad + 1f) * (rad + 1f)));
                if (dj > dep[j]) { dep[j] = dj; of[j] = c; }
            }
        }
        for (int i = 0; i < n; i++)
            w.Cell[i] = (byte)(dep[i] <= 0f ? Realm.Land : dep[i] <= K.SHALLOW ? Realm.Shallow : Realm.Deep);
        return net;
    }

    // the sea: connected regions below SeaLevel of SEA_MIN of the map or more, and the largest; returns their count
    static int Seas(World w, int[] lake)
    {
        int FW = Map.FW, n = FW * FW;
        float sea = w.SeaLevel;
        var comp = new int[n]; Array.Fill(comp, -1);
        var size = new List<int>(); var stack = new int[n];
        int lo = 0;
        for (int i0 = 0; i0 < n; i0++)
        {
            if (w.Elev[i0] < w.Elev[lo]) lo = i0;
            if (w.Elev[i0] >= sea || comp[i0] >= 0) continue;
            int id = size.Count, sp = 0, cnt = 0;
            stack[sp++] = i0; comp[i0] = id;
            while (sp > 0)
            {
                int c = stack[--sp]; cnt++;
                int x = c % FW, y = c / FW;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int j = World.WrapI(y + dy, FW) * FW + World.WrapI(x + dx, FW);
                        if (w.Elev[j] < sea && comp[j] < 0) { comp[j] = id; stack[sp++] = j; }
                    }
            }
            size.Add(cnt);
        }
        int big = -1;
        for (int k = 0; k < size.Count; k++) if (big < 0 || size[k] > size[big]) big = k;
        var id2 = new int[size.Count]; int ns = 0;
        for (int k = 0; k < size.Count; k++) id2[k] = k == big || size[k] >= K.SEA_MIN * n ? ns++ : -1;
        for (int i = 0; i < n; i++) if (comp[i] >= 0) lake[i] = id2[comp[i]];
        if (ns == 0) { lake[lo] = 0; ns = 1; }   // a world with nothing below its sea level drains to its lowest cell
        return ns;
    }

    // Priority-Flood from every cell with lake >= 0 (at its body's level): receivers, a topological order (outlets
    // first) and the exact depression-filled surface. The flood takes the lowest filled height first and, among equal
    // ones (a filled depression or a flat), the cell reached first (Barnes, Lehman & Mulla 2014's Priority-Flood with
    // a first-in-first-out tie: a flat drains toward where it was entered, and no epsilon makes the fill inexact)
    static void Flood(float[] h, int[] lake, float[] level, int[] recv, int[] order, float[] fill0)
    {
        int FW = Map.FW, n = FW * FW;
        var seen = new bool[n];
        var pq = new PriorityQueue<int, (float z, int seq)>(n, FillOrder.Instance);
        int seq = 0;
        for (int i = 0; i < n; i++)
            if (lake[i] >= 0) { seen[i] = true; fill0[i] = level[lake[i]]; recv[i] = -1; pq.Enqueue(i, (fill0[i], seq++)); }
        int k = 0;
        while (pq.TryDequeue(out int c, out var z))
        {
            order[k++] = c;
            int x = c % FW, y = c / FW;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int j = World.WrapI(y + dy, FW) * FW + World.WrapI(x + dx, FW);
                    if (seen[j]) continue;
                    seen[j] = true;
                    fill0[j] = MathF.Max(h[j], z.z);
                    recv[j] = c;
                    pq.Enqueue(j, (fill0[j], seq++));
                }
        }
    }

    sealed class FillOrder : IComparer<(float z, int seq)>
    {
        public static readonly FillOrder Instance = new();
        public int Compare((float z, int seq) a, (float z, int seq) b) => a.z != b.z ? a.z.CompareTo(b.z) : a.seq.CompareTo(b.seq);
    }

    // a topological order of a receiver forest, outlets first (breadth first from the outlets, in cell order); false if
    // some cell never reaches an outlet (a cycle)
    static bool TopoOrder(int[] recv, int[] order)
    {
        int n = recv.Length;
        var cnt = new int[n + 1];
        for (int i = 0; i < n; i++) if (recv[i] >= 0) cnt[recv[i] + 1]++;
        for (int i = 0; i < n; i++) cnt[i + 1] += cnt[i];
        var kids = new int[n]; var fillp = (int[])cnt.Clone();
        for (int i = 0; i < n; i++) if (recv[i] >= 0) kids[fillp[recv[i]]++] = i;
        int k = 0;
        for (int i = 0; i < n; i++) if (recv[i] < 0) order[k++] = i;
        for (int h = 0; h < k; h++)
        {
            int c = order[h];
            for (int m = cnt[c]; m < cnt[c + 1]; m++) order[k++] = kids[m];
        }
        return k == n;
    }

    /// <summary>
    /// The mean year's water at each cell (mm a year): yld, what the land gives to the streams, P - ET with ET from Budyko's
    /// curve in Fu's form (ET/P = 1 + phi - (1 + phi^w)^(1/w), phi = PET/P); loss, what a lake there loses, open-water
    /// evaporation less the rain on it. P from the rain pattern (P_MEAN x rain; a cell that was water when the rain was
    /// drawn gets its row's land mean, as a lake does in the water cycle); PET from the annual mean temperature (Tetens,
    /// none at or below 0 °C), as WaterCycle has it. The temperature is the default climate's (K.CLIMATE), whatever the
    /// Nature.Climate setting: a seed makes the same land, lakes and rivers at any climate setting (as Terrain.RunoffRain
    /// does for the rain), and the setting changes how they run (WaterCycle), not where they are.
    /// </summary>
    internal static void AnnualWater(World w, float[] rain, float[] yld, float[] loss)
    {
        int FW = Map.FW;
        float sea = w.SeaLevel;
        float es0 = Climate.Es(K.T_REF);
        for (int y = 0; y < FW; y++)
        {
            double sr = 0; int nr = 0;
            for (int i = y * FW; i < (y + 1) * FW; i++) if (rain[i] > 0f) { sr += rain[i]; nr++; }
            float rowMean = nr > 0 ? (float)(sr / nr) : 1f;
            float s = MathK.Sin(2f * MathF.PI * y / FW);
            float t0 = K.T_REF + K.CLIMATE * (K.T_EQ - K.DT_EP * s * s - K.T_REF);
            for (int i = y * FW; i < (y + 1) * FW; i++)
            {
                float hm = MathF.Max(0f, w.Elev[i] - sea) / MathF.Max(1e-6f, 1f - sea) * K.H_RELIEF;
                float t = t0 - K.CLIMATE * K.LAPSE * hm * 0.001f;
                float pet = t <= 0f ? 0f : K.PET_REF * Climate.Es(t) / es0;
                float p = K.P_MEAN * (rain[i] > 0f || w.Elev[i] >= sea ? rain[i] : rowMean);
                float et = 0f;
                if (p > 0f && pet > 0f)
                {
                    float phi = pet / p;
                    et = p * (1f + phi - MathK.Pow(1f + MathK.Pow(phi, K.BUDYKO_W), 1f / K.BUDYKO_W));
                }
                yld[i] = Math.Clamp(p - et, 0f, p);
                float tw = Climate.Water(t);
                float ev = t > 0f ? K.E_OPEN * K.PET_REF * Climate.Es(tw) / es0 : 0f;
                loss[i] = ev - K.P_MEAN * rowMean;
            }
        }
    }
}
