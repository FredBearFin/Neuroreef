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
    public static void Route(float[] h, float sea, float[] rain, Net net)
    {
        int FW = Map.FW, n = FW * FW;
        int[] recv = net.Recv, order = net.Order; float[] fill = net.Fill, flow = net.Flow;
        var seen = new bool[n];
        var pq = new PriorityQueue<int, float>(n);
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
    /// do not change). alluv receives the depth of sediment left on each cell. Returns the mass budget.
    /// </summary>
    public static Budget Erode(float[] h, float sea, float[] rain, float[] alluv, int iters,
        float kErode = K.ERODE_K, float kCarry = K.CARRY_K, float drop = K.DEPOSIT_FRAC, float kCreep = K.CREEP_K)
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
                float F = kErode * MathF.Pow(flow[c], K.ERODE_M) / Dist(c, r, FW);
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
                    float cap = kCarry * MathF.Pow(flow[c], K.ERODE_M) * slope;
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
    /// </summary>
    public static void Floodplain(World w, Net net)
    {
        int FW = Map.FW, n = FW * FW;
        var drain = new int[n]; var steps = new int[n];
        float bank = MathF.Pow(K.BANKFULL, K.RIVER_DEPTH_EXP);
        for (int q = 0; q < n; q++)
        {
            int c = net.Order[q], r = net.Recv[c];
            w.FloodWet[c] = float.PositiveInfinity; w.FloodFrom[c] = -1;
            if (w.Cell[c] != (byte)Realm.Land) { drain[c] = w.Depth[c] <= K.SHALLOW && w.Elev[c] >= w.SeaLevel ? c : -1; steps[c] = 0; continue; }
            if (r < 0) { drain[c] = -1; continue; }
            drain[c] = drain[r]; steps[c] = steps[r] + 1;
            int d = drain[c];
            if (d < 0 || steps[c] > K.FLOOD_R) continue;
            float above = w.Elev[c] - w.Elev[d];
            if (above > K.FLOOD_HAND * K.SHALLOW) continue;
            // stage / mean depth = wet^f; it reaches the cell when Depth (wet^f - BANKFULL^f) >= height above the river
            w.FloodWet[c] = MathF.Pow(bank + MathF.Max(0f, above) / w.Depth[d], 1f / K.RIVER_DEPTH_EXP);
            w.FloodFrom[c] = d;
        }
    }
}
