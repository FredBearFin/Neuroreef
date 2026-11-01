// Measures of a river network's form (2026-11-01, rivers that look like rivers): what a geomorphologist reads off a map
// of channels, so the generated network can be checked against real ones. Diagnostics only: nothing in the simulation
// reads them (tools/RiverProbe, HydrologyTests).
// - Direction: the channel's bearing, step by step and over reaches of REACH cells, as a histogram folded to 0-180
//   degrees. D8 routing puts every step (and most reaches) at a multiple of 45 degrees.
// - Sinuosity: channel length (a ruler of RULER cells) over straight distance for each link (head or confluence to
//   the next confluence or mouth) of REACH cells or more, by valley slope. Real rivers: straight < 1.05, sinuous to 1.5, meandering above, highest on low gradients.
// - Strahler order and Horton's ratios: bifurcation Rb = N(w) / N(w+1) is 3-5 in natural networks (Horton 1945;
//   Strahler 1957), length ratio RL 1.5-3.5.
// - Drainage density (channel length / land area) and the mean distance from land to a channel.
// - Parallel straight streams: links of REACH cells or more that stay within a cell of their chord, and run within 5
//   degrees of another such link less than PAR_D cells away, side by side.
// - Width continuity: steps downstream where the drawn width falls (it should only grow, and jump at confluences).
// A channel cell is open water outside the sea and the lakes whose cross-section is its own (FloodFrom == itself);
// its point is its centre plus the channel's sub-cell offset (World.RivX, RivY) when it has one.

using System;
using System.Collections.Generic;

namespace Neuroreef;

public static class RiverStats
{
    public const int REACH = 6, PAR_D = 8, BINS = 36, RULER = 3;

    public sealed class Result
    {
        public int Channels, Heads, Links, MaxOrder;
        public double LengthKm, LandKm2, Density, MeanDistKm;
        public readonly double[] StepHist = new double[BINS], ReachHist = new double[BINS];   // 5-degree bins over 0-180, length-weighted, sum 1
        public double StepSpike, ReachSpike;      // share of length within 2.5 degrees of a multiple of 45 (uniform: 0.111)
        public double SinMedian, SinLow, SinMid, SinHigh;   // median sinuosity of links of REACH+ cells: all, valley slope < 0.002, 0.002-0.01, > 0.01
        public int NLow, NMid, NHigh;
        public readonly int[] StreamsOfOrder = new int[16];
        public readonly double[] LengthOfOrder = new double[16];
        public double Rb, RL;                     // Horton ratios from the regression of log N and log mean length on order
        public int Straight, Parallel, LongLinks; // links of REACH+ cells; straight ones (within a cell of their chord); straight ones with a parallel neighbour
        public double WidthDrops, SectionDrops;   // share of downstream steps where the drawn width / the water cross-section (cells) falls
        public readonly List<(float x, float y, float sin, float slope, int order)> LinkInfo = new();

        public override string ToString()
        {
            var s = new System.Text.StringBuilder();
            s.AppendLine($"  channels {Channels} cells, {Heads} heads, {Links} links, Strahler order up to {MaxOrder}");
            s.AppendLine($"  drainage density {Density:F3} km/km2 ({LengthKm:F0} km over {LandKm2:F0} km2), mean distance to a channel {MeanDistKm:F2} km");
            s.AppendLine($"  direction at multiples of 45 deg (+-2.5): step {StepSpike:P1}, reach of {REACH} cells {ReachSpike:P1} (uniform 11.1%)");
            s.AppendLine($"  sinuosity median {SinMedian:F3}; valley slope < 0.002: {SinLow:F3} ({NLow}), 0.002-0.01: {SinMid:F3} ({NMid}), > 0.01: {SinHigh:F3} ({NHigh})");
            s.Append("  streams by order:");
            for (int k = 1; k <= MaxOrder; k++) s.Append($" {k}:{StreamsOfOrder[k]} ({LengthOfOrder[k]:F1} cells)");
            s.AppendLine($"; Rb {Rb:F2}, RL {RL:F2}");
            s.AppendLine($"  links of {REACH}+ cells {LongLinks}: straight {Straight}, straight with a parallel neighbour {Parallel} ({(LongLinks > 0 ? Parallel / (double)LongLinks : 0):P1})");
            s.Append($"  width falls downstream at {WidthDrops:P2} of steps (drawn), water cross-section at {SectionDrops:P2}");
            return s.ToString();
        }
    }

    public static bool IsChannel(World w, int i) => w.Cell[i] != (byte)Realm.Land && w.LakeOf[i] < 0 && w.FloodFrom[i] == i;

    /// <summary>The point of channel cell i (cell units, x along a row), unwrapped near (nx, ny).</summary>
    static (float x, float y) Pt(World w, int i, float nx, float ny)
    {
        int FW = Map.FW;
        float x = i % FW + w.RivX[i], y = i / FW + w.RivY[i];
        x += FW * MathF.Round((nx - x) / FW); y += FW * MathF.Round((ny - y) / FW);
        return (x, y);
    }

    public static Result Measure(World w)
    {
        int FW = Map.FW, n = FW * FW;
        var R = new Result();
        float cellKm = K.GEO_CELL_M * 1e-3f;
        float mPerElev = K.H_RELIEF / MathF.Max(1e-6f, 1f - w.SeaLevel);
        var chan = new bool[n]; var donors = new int[n];
        int land = 0;
        for (int i = 0; i < n; i++)
        {
            if (w.LakeOf[i] < 0) land++;
            if (!IsChannel(w, i)) continue;
            chan[i] = true; R.Channels++;
        }
        for (int i = 0; i < n; i++) if (chan[i] && w.Recv[i] >= 0 && chan[w.Recv[i]]) donors[w.Recv[i]]++;
        // Strahler order, sources first
        var ord = new int[n]; var best = new int[n]; var nbest = new int[n];
        for (int q = n - 1; q >= 0; q--)
        {
            int c = w.DrainOrder[q];
            if (!chan[c]) continue;
            if (donors[c] == 0) { ord[c] = 1; R.Heads++; }
            else ord[c] = nbest[c] >= 2 ? best[c] + 1 : best[c];
            int r = w.Recv[c];
            if (r >= 0 && chan[r]) { if (ord[c] > best[r]) { best[r] = ord[c]; nbest[r] = 1; } else if (ord[c] == best[r]) nbest[r]++; }
            R.MaxOrder = Math.Max(R.MaxOrder, ord[c]);
        }
        R.MaxOrder = Math.Min(R.MaxOrder, 15);

        // the downstream point of a channel cell: its receiver's point (a channel), or the lake cell it runs into
        (float x, float y) Down(int c, float nx, float ny)
        {
            int r = w.Recv[c];
            if (r < 0) return (nx, ny);
            if (chan[r]) return Pt(w, r, nx, ny);
            float x = r % FW, y = r / FW;
            x += FW * MathF.Round((nx - x) / FW); y += FW * MathF.Round((ny - y) / FW);
            return (x, y);
        }

        // length, step directions, width continuity
        double len = 0, wdrop = 0, sdrop = 0, steps = 0;
        var section = new int[n];
        for (int i = 0; i < n; i++) if (w.Cell[i] != (byte)Realm.Land && w.LakeOf[i] < 0 && w.FloodFrom[i] >= 0) section[w.FloodFrom[i]]++;
        for (int c = 0; c < n; c++)
        {
            if (!chan[c]) continue;
            var p = Pt(w, c, c % FW, c / FW);
            var d = Down(c, p.x, p.y);
            float dx = d.x - p.x, dy = d.y - p.y, l = MathF.Sqrt(dx * dx + dy * dy);
            len += l;
            if (l > 0f) Bin(R.StepHist, dx, dy, l);
            int r = w.Recv[c];
            if (r >= 0 && chan[r])
            {
                steps++;
                if (w.RivW[r] < w.RivW[c] - 1e-4f) wdrop++;
                if (donors[r] == 1 && section[r] < section[c]) sdrop++;
            }
            // the reach direction: REACH steps down
            int e = c, k = 0;
            while (k < REACH && w.Recv[e] >= 0 && chan[w.Recv[e]]) { e = w.Recv[e]; k++; }
            if (k == REACH)
            {
                var q = Pt(w, e, p.x, p.y);
                Bin(R.ReachHist, q.x - p.x, q.y - p.y, 1f);
            }
        }
        R.WidthDrops = steps > 0 ? wdrop / steps : 0; R.SectionDrops = steps > 0 ? sdrop / steps : 0;
        R.StepSpike = Normalise(R.StepHist); R.ReachSpike = Normalise(R.ReachHist);
        R.LengthKm = len * cellKm; R.LandKm2 = land * cellKm * cellKm; R.Density = R.LengthKm / R.LandKm2;

        // streams of each order (Horton): a stream starts at a head or where the order rises, and runs while it holds
        for (int c = 0; c < n; c++)
        {
            if (!chan[c] || ord[c] > 15) continue;
            if (donors[c] == 0 || ord[c] > best[c]) R.StreamsOfOrder[ord[c]]++;
            R.LengthOfOrder[ord[c]] += 1;
        }
        for (int k = 1; k <= R.MaxOrder; k++) if (R.StreamsOfOrder[k] > 0) R.LengthOfOrder[k] /= R.StreamsOfOrder[k];
        (R.Rb, R.RL) = Horton(R);

        // links: from a head or a confluence down to the next confluence or the mouth. Sinuosity is measured with a ruler of
        // RULER cells (every RULER-th point), at the scale of a reach rather than of the grid's own steps; a link is straight
        // when no point of it lies more than a cell off its chord
        var links = new List<(float sx, float sy, float ex, float ey, bool straight)>();
        var sins = new List<double>(); var sl = new List<double>(); var sm = new List<double>(); var sh = new List<double>();
        var pts = new List<(float x, float y)>();
        for (int c = 0; c < n; c++)
        {
            if (!chan[c] || donors[c] == 1) continue;   // a link starts at a head (0) or a confluence (2+)
            var p0 = Pt(w, c, c % FW, c / FW);
            pts.Clear(); pts.Add(p0);
            float px = p0.x, py = p0.y; int e = c;
            while (true)
            {
                var d = Down(e, px, py);
                px = d.x; py = d.y; pts.Add(d);
                int r = w.Recv[e];
                if (r < 0 || !chan[r] || donors[r] >= 2) break;
                e = r;
            }
            float chord = MathF.Sqrt((px - p0.x) * (px - p0.x) + (py - p0.y) * (py - p0.y));
            R.Links++;
            if (pts.Count - 1 < REACH || chord < 1f) continue;
            double l = 0; int prev = 0;
            for (int k = RULER; ; k += RULER)
            {
                int kk = Math.Min(k, pts.Count - 1);
                l += MathF.Sqrt((pts[kk].x - pts[prev].x) * (pts[kk].x - pts[prev].x) + (pts[kk].y - pts[prev].y) * (pts[kk].y - pts[prev].y));
                prev = kk;
                if (kk == pts.Count - 1) break;
            }
            float ux = (px - p0.x) / chord, uy = (py - p0.y) / chord, off = 0f;
            foreach (var q in pts) off = MathF.Max(off, MathF.Abs((q.x - p0.x) * uy - (q.y - p0.y) * ux));
            int last = w.Recv[e] >= 0 ? w.Recv[e] : e;
            float drop = MathF.Max(0f, w.Elev[c] - MathF.Max(w.Elev[last], w.SeaLevel)) * mPerElev;
            float slope = drop / (chord * K.GEO_CELL_M);
            double s = Math.Max(1.0, l / chord);
            sins.Add(s);
            (slope < 0.002f ? sl : slope < 0.01f ? sm : sh).Add(s);
            links.Add((p0.x, p0.y, px, py, off <= 1f));
            R.LinkInfo.Add((p0.x, p0.y, (float)s, slope, ord[c]));
        }
        R.SinMedian = Median(sins); R.SinLow = Median(sl); R.SinMid = Median(sm); R.SinHigh = Median(sh);
        R.NLow = sl.Count; R.NMid = sm.Count; R.NHigh = sh.Count;
        R.LongLinks = links.Count;

        // parallel straight links
        var straight = new List<int>();
        for (int k = 0; k < links.Count; k++) if (links[k].straight) straight.Add(k);
        R.Straight = straight.Count;
        foreach (int a in straight)
        {
            var A = links[a];
            float ax = A.ex - A.sx, ay = A.ey - A.sy, al = MathF.Sqrt(ax * ax + ay * ay);
            ax /= al; ay /= al;
            foreach (int b in straight)
            {
                if (a == b) continue;
                var B = links[b];
                float bx = B.ex - B.sx, by = B.ey - B.sy, bl = MathF.Sqrt(bx * bx + by * by);
                if (MathF.Abs(ax * by - ay * bx) / bl > 0.0872f || ax * bx + ay * by < 0f) continue;   // within 5 degrees, same way
                // B's midpoint, unwrapped near A's: beside A (across within PAR_D, along within A's span)
                float mx = 0.5f * (B.sx + B.ex), my = 0.5f * (B.sy + B.ey);
                mx += FW * MathF.Round((A.sx - mx) / FW); my += FW * MathF.Round((A.sy - my) / FW);
                float rx = mx - A.sx, ry = my - A.sy, along = rx * ax + ry * ay, across = MathF.Abs(rx * ay - ry * ax);
                if (across > 0.5f && across <= PAR_D && along >= 0f && along <= al) { R.Parallel++; break; }
            }
        }

        // mean distance from land to a channel (8-connected chamfer)
        var mask = new byte[n];
        for (int i = 0; i < n; i++) mask[i] = (byte)(chan[i] ? 1 : 0);
        var dist = new float[n];
        Terrain.Distance(mask, 1, dist);
        double sd = 0; int nd = 0;
        for (int i = 0; i < n; i++) if (w.Cell[i] == (byte)Realm.Land && dist[i] < 999f) { sd += dist[i]; nd++; }
        R.MeanDistKm = nd > 0 ? sd / nd * cellKm : 0;
        return R;
    }

    // a direction into 5-degree bins over 0-180 (a channel's bearing, either way)
    static void Bin(double[] h, float dx, float dy, float wgt)
    {
        float a = MathK.Atan2(dy, dx) * 180f / MathF.PI;
        if (a < 0f) a += 180f;
        if (a >= 180f) a -= 180f;
        // bins centred on multiples of 5 degrees, so 0, 45, 90, 135 are bin centres
        int b = (int)MathF.Floor((a + 2.5f) / 5f) % BINS;
        h[b] += wgt;
    }

    static double Normalise(double[] h)
    {
        double s = 0; foreach (var v in h) s += v;
        if (s <= 0) return 0;
        for (int k = 0; k < h.Length; k++) h[k] /= s;
        return h[0] + h[9] + h[18] + h[27];
    }

    static double Median(List<double> v)
    {
        if (v.Count == 0) return double.NaN;
        v.Sort();
        return v.Count % 2 == 1 ? v[v.Count / 2] : 0.5 * (v[v.Count / 2 - 1] + v[v.Count / 2]);
    }

    // Horton's laws: the slopes of log N(w) and log L(w) against w, over orders with streams (the highest order, a few
    // trunks, is noisy and kept: it is the network's own)
    static (double rb, double rl) Horton(Result R)
    {
        double sx = 0, sy = 0, sxx = 0, sxy = 0, ly = 0, sxl = 0; int m = 0;
        for (int k = 1; k <= R.MaxOrder; k++)
        {
            if (R.StreamsOfOrder[k] <= 0) continue;
            double y = Math.Log(R.StreamsOfOrder[k]), l = Math.Log(Math.Max(1e-9, R.LengthOfOrder[k]));
            sx += k; sy += y; sxx += k * k; sxy += k * y; ly += l; sxl += k * l; m++;
        }
        if (m < 2) return (double.NaN, double.NaN);
        double den = m * sxx - sx * sx;
        double b = (m * sxy - sx * sy) / den, bl = (m * sxl - sx * ly) / den;
        return (Math.Exp(-b), Math.Exp(bl));
    }

    /// <summary>The direction of the relief's slope (the gradient's aspect by central differences) on land steeper than
    /// minSlope (elev per cell), as a histogram like the channels'; returns the share within 2.5 degrees of a multiple of 45.</summary>
    public static double AspectSpike(World w, float minSlope, double[] hist)
    {
        int FW = Map.FW;
        Array.Clear(hist);
        for (int y = 0; y < FW; y++)
            for (int x = 0; x < FW; x++)
            {
                int i = y * FW + x;
                if (w.Cell[i] != (byte)Realm.Land) continue;
                float gx = 0.5f * (w.Elev[y * FW + (x + 1) % FW] - w.Elev[y * FW + (x + FW - 1) % FW]);
                float gy = 0.5f * (w.Elev[(y + 1) % FW * FW + x] - w.Elev[(y + FW - 1) % FW * FW + x]);
                if (gx * gx + gy * gy < minSlope * minSlope) continue;
                Bin(hist, gx, gy, 1f);
            }
        return Normalise(hist);
    }
}
