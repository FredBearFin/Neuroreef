// River channels as lines, not as chains of cell centres (2026-11-01; the owner: "the rivers are weird"). Before, a river
// was its D8 cells: a staircase of steps at multiples of 45 degrees, as straight as the grid. Now each river is a line
// through the plane, and its cells are the ones that line crosses:
// - The network is cut into rivers: from each head down, at every confluence the larger branch (more discharge) goes on and
//   the smaller one ends where it joins it. Rivers are laid out largest first, so a tributary meets its trunk where the
//   trunk now runs.
// - The valley axis is the river's cell path smoothed (a Gaussian of CHANNEL_SMOOTH cells, or 3/4 of the river's width),
//   held within a cell of where it was: the zigzags of the grid go, the valley stays.
// - Meanders: an alluvial river on a low gradient meanders. Sinuosity rises as the valley slope S falls below the
//   threshold between meandering and braided rivers, S_t = MEANDER_ST Q^-0.44 (Leopold & Wolman 1957; Q bankfull, m3/s):
//   P = 1 + (MEANDER_P - 1)(1 - S / S_t), 1 at or above S_t. The channel swings about the axis on a sine of wavelength
//   MEANDER_L widths (Leopold & Wolman 1960: lambda = 10.9 w), with the amplitude that gives that sinuosity
//   (A = 2.7 w at P = 1.5, as L&W found). It cannot swing beyond its valley floor (the ground within MEANDER_HAND of its
//   bed across the valley): a confined river meanders less. Amplitude and the floor's centre are smoothed along the river,
//   fade in over half a wavelength below its head and out before its end (a quarter wavelength before the trunk it
//   joins; two cells before its lake, which it may enter anywhere), and the phase starts at a random place (terrain seed).
//   Where a swing would loop back on the channel the neck is cut through (a cutoff, as rivers do), and no river crosses
//   into ground that drains to another lake.
// - The cells: a channel cell is each cell the line crosses, in order; each drains to the next, the last into its lake or
//   the river it joins. Where the line crosses a cell is kept (World.RivX, RivY: offset from the cell's centre), so the
//   line can be drawn smooth (Wire: RIVERS). Cells of the old path the river left drain to it (to the nearest of its new
//   cells, breadth first through the cells it left). Every cell still drains to the same lake as before, so the lakes'
//   balances and spill order stand.
// Allocation is allowed here (construction, NR-101).

using System;
using System.Collections.Generic;

namespace Neuroreef;

public static partial class Hydrology
{
    // sinuosity of y = A sin(2 pi x / lambda) against a = 2 pi A / lambda: the mean of sqrt(1 + a^2 cos^2) over a period
    const int SIN_N = 161; const float SIN_DA = 0.05f;
    static readonly float[] SinTable = MakeSinTable();
    static float[] MakeSinTable()
    {
        var t = new float[SIN_N];
        for (int k = 0; k < SIN_N; k++)
        {
            float a = k * SIN_DA; double s = 0;
            for (int j = 0; j < 256; j++) { float c = MathK.Cos((j + 0.5f) * (2f * MathF.PI / 256f)); s += MathF.Sqrt(1f + a * a * c * c); }
            t[k] = (float)(s / 256);
        }
        return t;
    }

    /// <summary>The a = 2 pi A / lambda of a sine whose sinuosity is p.</summary>
    internal static float SineForSinuosity(float p)
    {
        if (p <= 1f) return 0f;
        for (int k = 1; k < SIN_N; k++)
            if (SinTable[k] >= p) return (k - 1 + (p - SinTable[k - 1]) / (SinTable[k] - SinTable[k - 1])) * SIN_DA;
        return (SIN_N - 1) * SIN_DA;
    }

    /// <summary>Bilinear height on the torus at (x, y) in cells (cell centres at integers).</summary>
    static float Bil(float[] h, float x, float y, int FW)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float tx = x - x0, ty = y - y0;
        int xa = World.WrapI(x0, FW), xb = World.WrapI(x0 + 1, FW), ya = World.WrapI(y0, FW) * FW, yb = World.WrapI(y0 + 1, FW) * FW;
        return (h[ya + xa] * (1 - tx) + h[ya + xb] * tx) * (1 - ty) + (h[yb + xa] * (1 - tx) + h[yb + xb] * tx) * ty;
    }

    sealed class River { public int[] Cells = Array.Empty<int>(); public int End; public float Key; }

    /// <summary>
    /// Lay each river of the channel network chan (draining along rv, ord outlets first) along its own line: smooth, and
    /// meandering on low gradients (the file's header). Changes rv and chan to the cells the lines cross and sets RivX,
    /// RivY on them; every cell keeps draining to the lake it drained to. Q: mean discharge (cells of mean land rain).
    /// </summary>
    internal static void Channels(World w, int[] lake, int[] rv, int[] ord, bool[] chan, float[] Q, float scale)
    {
        int FW = Map.FW, n = FW * FW;
        float[] h = w.Elev;
        Array.Clear(w.RivX); Array.Clear(w.RivY);
        float mPerElev = Map.ReliefM / MathF.Max(1e-6f, 1f - w.SeaLevel);
        float hand = K.MEANDER_HAND / mPerElev;
        float qPhys = K.P_MEAN * 1e-3f * K.GEO_CELL_M * K.GEO_CELL_M / (365.25f * 86400f) * K.BANKFULL;   // a cell of Q at bankfull, m3/s

        // the lake every cell drains to in the end
        var mouth = new int[n];
        for (int q = 0; q < n; q++) { int c = ord[q], r = rv[c]; mouth[c] = lake[c] >= 0 ? lake[c] : r >= 0 ? mouth[r] : -1; }
        // each channel cell's main donor (the channel draining into it with the most discharge)
        var main = new int[n]; Array.Fill(main, -1);
        bool any = false;
        for (int c = 0; c < n; c++)
        {
            if (!chan[c]) continue;
            any = true;
            int r = rv[c];
            if (r < 0 || !chan[r]) continue;
            int m = main[r];
            if (m < 0 || Q[c] > Q[m] || (Q[c] == Q[m] && c < m)) main[r] = c;
        }
        if (!any) return;
        // rivers: from each head down while it is the main branch
        var rivers = new List<River>();
        var path = new List<int>();
        var donor = new bool[n];
        for (int c = 0; c < n; c++) if (chan[c] && rv[c] >= 0 && chan[rv[c]]) donor[rv[c]] = true;
        for (int c0 = 0; c0 < n; c0++)
        {
            if (!chan[c0] || donor[c0]) continue;
            path.Clear();
            int c = c0;
            while (true)
            {
                path.Add(c);
                int r = rv[c];
                if (r < 0 || lake[r] >= 0 || main[r] != c) break;
                c = r;
            }
            int last = path[^1];
            rivers.Add(new River { Cells = path.ToArray(), End = rv[last] >= 0 ? rv[last] : last, Key = Q[last] });
        }
        var byKey = new int[rivers.Count];
        for (int k = 0; k < byKey.Length; k++) byKey[k] = k;
        Array.Sort(byKey, (a, b) => rivers[a].Key != rivers[b].Key ? rivers[b].Key.CompareTo(rivers[a].Key) : rivers[a].Cells[0].CompareTo(rivers[b].Cells[0]));

        var claim = new int[n]; Array.Fill(claim, -1);
        var nrv = new int[n];
        var tgt = new int[n]; Array.Fill(tgt, -1);           // of an old channel cell: the new channel cell a tributary joining there aims for
        var sumX = new float[n]; var sumY = new float[n]; var cnt = new int[n];
        var inPath = new int[n]; Array.Fill(inPath, -1);
        var newPath = new List<int>();
        var axisCell = new List<int>();
        uint phaseSeed = Rng.Hash(w.TerrainSeed, 0, 0, 0x3EA9u);

        foreach (int id in byKey)
        {
            var R = rivers[id];
            int m = R.Cells.Length, M = m + 1, mouthR = mouth[R.Cells[0]];
            if (claim[R.Cells[0]] >= 0)
            {
                // a larger river's meander has taken its head: it is a tributary no more (its cells drain to that river)
                foreach (int c in R.Cells) tgt[c] = R.Cells[0];
                continue;
            }
            // the axis: the cell centres unwrapped, and the end (the lake cell, or where the trunk now runs at the junction)
            var ax = new float[M]; var ay = new float[M];
            int x0 = R.Cells[0] % FW, y0 = R.Cells[0] / FW;
            ax[0] = x0; ay[0] = y0;
            for (int k = 1; k < M; k++)
            {
                int c = k < m ? R.Cells[k] : R.End;
                float cx = c % FW, cy = c / FW;
                if (k == m && lake[c] < 0 && tgt[c] >= 0) { int t = tgt[c]; cx = t % FW + w.RivX[t]; cy = t / FW + w.RivY[t]; }
                cx += FW * MathF.Round((ax[k - 1] - cx) / FW); cy += FW * MathF.Round((ay[k - 1] - cy) / FW);
                ax[k] = cx; ay[k] = cy;
            }
            // widths, the smoothed axis (ends fixed), held within a cell of the path
            var wd = new float[M];
            for (int k = 0; k < M; k++) wd[k] = MathF.Min(K.RIVER_W_MAX, MathF.Max(1f, Hydrology.WidthM(Q[R.Cells[Math.Min(k, m - 1)]]) / K.GEO_CELL_M));   // the cells it covers (World.RivW; 2026-11-01)
            var sx = new float[M]; var sy = new float[M];
            for (int k = 0; k < M; k++)
            {
                float sig = MathF.Max(K.CHANNEL_SMOOTH, 0.75f * wd[k]);
                int hw = Math.Min((int)(2.5f * sig), Math.Min(k, M - 1 - k));
                float tw = 0f, tx = 0f, ty = 0f;
                for (int j = -hw; j <= hw; j++) { float g = MathK.Exp(-0.5f * j * j / (sig * sig)); tw += g; tx += g * ax[k + j]; ty += g * ay[k + j]; }
                sx[k] = tx / tw; sy[k] = ty / tw;
                if (k < m) { sx[k] = Math.Clamp(sx[k], ax[k] - 1f, ax[k] + 1f); sy[k] = Math.Clamp(sy[k], ay[k] - 1f, ay[k] + 1f); }
            }
            // arc length, normals
            var s = new float[M]; var nx = new float[M]; var ny = new float[M];
            for (int k = 1; k < M; k++) s[k] = s[k - 1] + MathF.Sqrt((sx[k] - sx[k - 1]) * (sx[k] - sx[k - 1]) + (sy[k] - sy[k - 1]) * (sy[k] - sy[k - 1]));
            for (int k = 0; k < M; k++)
            {
                int a = Math.Max(0, k - 1), b = Math.Min(M - 1, k + 1);
                float tx = sx[b] - sx[a], ty = sy[b] - sy[a], l = MathF.Sqrt(tx * tx + ty * ty);
                if (l < 1e-6f) { nx[k] = k > 0 ? nx[k - 1] : 0f; ny[k] = k > 0 ? ny[k - 1] : 1f; continue; }
                nx[k] = -ty / l; ny[k] = tx / l;
            }
            // meanders: sinuosity from the valley slope against the meandering threshold, amplitude within the valley floor
            var amp = new float[M]; var off = new float[M];
            bool meanders = false;
            for (int k = 0; k < m; k++)
            {
                float lam = K.MEANDER_L * wd[k];
                int L = Math.Max(3, (int)(0.5f * lam));
                int a = Math.Max(0, k - L), b = Math.Min(m - 1, k + L);
                float ds = s[b] - s[a];
                float sv = ds > 0f ? MathF.Max(0f, h[R.Cells[a]] - h[R.Cells[b]]) * mPerElev / (ds * K.GEO_CELL_M) : 0f;
                float st = K.MEANDER_ST * MathK.Pow(MathF.Max(1e-4f, Q[R.Cells[k]] * qPhys), -0.44f);
                float u = 1f - MathF.Min(1f, sv / st);
                if (u <= 0f) continue;
                float p = 1f + (K.MEANDER_P - 1f) * u;
                float A = SineForSinuosity(p) * lam / (2f * MathF.PI);
                // the valley floor across the axis: how far each side stays within MEANDER_HAND of the bed
                float bed = h[R.Cells[k]] + hand, fp = 0f, fm = 0f, reach = MathF.Min(12f, A + 1f);
                for (float t = 0.5f; t <= reach; t += 0.5f) { if (Bil(h, sx[k] + t * nx[k], sy[k] + t * ny[k], FW) > bed) break; fp = t; }
                for (float t = 0.5f; t <= reach; t += 0.5f) { if (Bil(h, sx[k] - t * nx[k], sy[k] - t * ny[k], FW) > bed) break; fm = t; }
                amp[k] = MathF.Min(A, 0.5f * (fp + fm));
                off[k] = u * 0.5f * (fp - fm);
                if (amp[k] > 0f) meanders = true;
            }
            float phase0 = 2f * MathF.PI * Rng.U(phaseSeed, 0, (uint)R.Cells[0], 0);
            var ph = new float[M];
            if (meanders)
            {
                Smooth(amp, M, wd); Smooth(off, M, wd);
                for (int k = 0; k < M; k++)
                {
                    // it fades in over half a wavelength below its head, and out before its end: a quarter wavelength
                    // before the trunk it joins, two cells before its lake (it may enter the lake anywhere)
                    float lam = K.MEANDER_L * wd[k];
                    float e = MathF.Min(s[k] / (0.5f * lam), (s[M - 1] - s[k]) / (lake[R.End] >= 0 ? 2f : 0.25f * lam));
                    float taper = e >= 1f ? 1f : e <= 0f ? 0f : e * e * (3f - 2f * e);
                    amp[k] *= taper; off[k] *= taper;
                    if (k > 0) ph[k] = ph[k - 1] + 2f * MathF.PI * (s[k] - s[k - 1]) / lam;
                }
            }
            // lay the line, then try again without meanders, then keep the old cells
            bool ok = meanders && Lay(true);
            if (!ok) ok = Lay(false);
            if (!ok) LayOld();

            // commit: the cells drain one to the next, the last into its lake or the river it joins
            for (int k = 0; k < newPath.Count; k++)
            {
                int c = newPath[k];
                claim[c] = id;
                if (k + 1 < newPath.Count) nrv[c] = newPath[k + 1];
                float cx = c % FW, cy = c / FW;
                float px = sumX[c] / cnt[c], py = sumY[c] / cnt[c];
                px -= FW * MathF.Round((px - cx) / FW); py -= FW * MathF.Round((py - cy) / FW);
                w.RivX[c] = Math.Clamp(px - cx, -0.5f, 0.5f); w.RivY[c] = Math.Clamp(py - cy, -0.5f, 0.5f);
                inPath[c] = -1;
            }
            // where a tributary joining at each old cell aims: the new cell its axis point fell in, or the nearest one kept
            for (int k = 0; k < m; k++)
            {
                int best = -1;
                for (int d = 0; d < m && best < 0; d++)
                {
                    if (k - d >= 0 && claim[axisCell[k - d]] == id) best = axisCell[k - d];
                    else if (k + d < m && claim[axisCell[k + d]] == id) best = axisCell[k + d];
                }
                tgt[R.Cells[k]] = best >= 0 ? best : newPath[0];
            }

            // the cells a line crosses, with its checks: false if it would cross into ground that drains to another lake
            bool Lay(bool wave)
            {
                Reset();
                axisCell.Clear();
                for (int k = 0; k < M; k++)
                {
                    int J = k == M - 1 ? 1 : wave ? 4 * (1 + Math.Min(7, (int)(amp[k] * 2f * MathF.PI / (K.MEANDER_L * wd[k])) + (int)amp[k])) : 4;
                    for (int j = 0; j < J; j++)
                    {
                        float t = j / (float)J;
                        int b = Math.Min(M - 1, k + 1);
                        float px = sx[k] + t * (sx[b] - sx[k]), py = sy[k] + t * (sy[b] - sy[k]);
                        if (wave)
                        {
                            float qx = nx[k] + t * (nx[b] - nx[k]), qy = ny[k] + t * (ny[b] - ny[k]), ql = MathF.Sqrt(qx * qx + qy * qy);
                            if (ql > 1e-6f) { qx /= ql; qy /= ql; }
                            float d = off[k] + t * (off[b] - off[k]) + (amp[k] + t * (amp[b] - amp[k])) * MathK.Sin(phase0 + ph[k] + t * (ph[b] - ph[k]));
                            px += d * qx; py += d * qy;
                        }
                        int r = Step(px, py, out bool end);
                        if (r < 0) return false;
                        if (j == 0 && k < m) axisCell.Add(newPath[^1]);
                        if (end) { while (axisCell.Count < m) axisCell.Add(newPath[^1]); return true; }
                    }
                }
                return false;   // the line ended short of its lake or trunk
            }

            // the old cells, and on down the old drainage until a river laid already or the lake
            void LayOld()
            {
                Reset();
                axisCell.Clear();
                int c = R.Cells[0];
                for (int k = 0; ; k++)
                {
                    float px = c % FW, py = c / FW;
                    if (k < m) { px += Math.Clamp(sx[k] - ax[k], -0.5f, 0.5f); py += Math.Clamp(sy[k] - ay[k], -0.5f, 0.5f); }
                    if (newPath.Count > 0) { int p = newPath[^1]; float lx = sumX[p] / cnt[p], ly = sumY[p] / cnt[p]; px += FW * MathF.Round((lx - px) / FW); py += FW * MathF.Round((ly - py) / FW); }
                    int r = Step(px, py, out bool end, c);
                    if (r < 0) throw new InvalidOperationException("Hydrology.Channels: a river's old course is blocked");
                    if (k < m) axisCell.Add(newPath[^1]);
                    if (end) break;
                    c = rv[c];
                }
                while (axisCell.Count < m) axisCell.Add(newPath[^1]);
            }

            void Reset()
            {
                foreach (int c in newPath) { inPath[c] = -1; sumX[c] = 0f; sumY[c] = 0f; cnt[c] = 0; }
                newPath.Clear();
            }

            // the line reaches (px, py): the cell it is in (or cell, if given). Returns -1 if it may not go there; end: it has
            // reached its lake or a river laid already (the line stops there)
            int Step(float px, float py, out bool end, int cell = -1)
            {
                end = false;
                int c = cell >= 0 ? cell : World.WrapI((int)MathF.Round(py), FW) * FW + World.WrapI((int)MathF.Round(px), FW);
                if (newPath.Count > 0 && newPath[^1] == c) { sumX[c] += px; sumY[c] += py; cnt[c]++; return c; }
                if (mouth[c] != mouthR) return -1;
                if (lake[c] >= 0 || claim[c] >= 0)
                {
                    if (newPath.Count == 0) return -1;
                    nrv[newPath[^1]] = c; end = true; return c;
                }
                if (inPath[c] >= 0)
                {
                    // a cutoff: the loop since the line was last here is cut through
                    int at = inPath[c];
                    for (int k = newPath.Count - 1; k > at; k--) { int d = newPath[k]; inPath[d] = -1; sumX[d] = 0f; sumY[d] = 0f; cnt[d] = 0; }
                    newPath.RemoveRange(at + 1, newPath.Count - at - 1);
                    for (int k = axisCell.Count - 1; k >= 0 && inPath[axisCell[k]] < 0; k--) axisCell[k] = c;
                    sumX[c] += px; sumY[c] += py; cnt[c]++;
                    return c;
                }
                inPath[c] = newPath.Count; newPath.Add(c);
                sumX[c] = px; sumY[c] = py; cnt[c] = 1;
                return c;
            }
        }

        // the new channels drain along their lines; the cells a river left drain to it, breadth first through them
        var left = new List<int>();
        for (int c = 0; c < n; c++)
        {
            bool now = claim[c] >= 0;
            if (chan[c] && !now) left.Add(c);
            chan[c] = now;
            if (now) rv[c] = nrv[c];
        }
        var dist = new int[n]; Array.Fill(dist, -1);
        var queue = new Queue<int>();
        foreach (int c in left)
        {
            int x = c % FW, y = c / FW, best = -1;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int j = World.WrapI(y + dy, FW) * FW + World.WrapI(x + dx, FW);
                    if (chan[j] && mouth[j] == mouth[c] && (best < 0 || h[j] < h[best] || (h[j] == h[best] && j < best))) best = j;
                }
            if (best >= 0) { rv[c] = best; dist[c] = 1; queue.Enqueue(c); }
        }
        var isLeft = new bool[n];
        foreach (int c in left) isLeft[c] = true;
        while (queue.Count > 0)
        {
            int c = queue.Dequeue(), x = c % FW, y = c / FW;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int j = World.WrapI(y + dy, FW) * FW + World.WrapI(x + dx, FW);
                    if (!isLeft[j] || dist[j] >= 0 || mouth[j] != mouth[c]) continue;
                    dist[j] = dist[c] + 1; rv[j] = c; queue.Enqueue(j);
                }
        }
        // (a cell left that no new channel cell reaches keeps its old receiver: on down its old course, to the same lake)

        // smooth a per-point value along the river over a quarter of the meander wavelength
        static void Smooth(float[] v, int M, float[] wd)
        {
            var o = new float[M];
            for (int k = 0; k < M; k++)
            {
                float sig = MathF.Max(1f, 0.25f * K.MEANDER_L * wd[k]);
                int hw = (int)(2f * sig);
                float tw = 0f, tv = 0f;
                for (int j = Math.Max(0, k - hw); j <= Math.Min(M - 1, k + hw); j++) { float g = MathK.Exp(-0.5f * (j - k) * (j - k) / (sig * sig)); tw += g; tv += g * v[j]; }
                o[k] = tv / tw;
            }
            Array.Copy(o, v, M);
        }
    }

    /// <summary>A river as a line (Lines): points in field cells (cell (i, j) spans [i, i+1) x [j, j+1)), unwrapped so that
    /// consecutive points are near (the map is a torus: take them modulo FW), with the river's width (cells) and depth
    /// (normalised elevation, as World.Depth) at each.</summary>
    public sealed class RiverLine
    {
        public readonly List<(float x, float y, float width, float depth)> Points = new();
        public int Down = -1;      // the river it joins (an index into the list), -1 if it ends in a lake or the sea
        public int Order;          // Strahler order where it ends
        public int EndKind;        // 0 joins a river, 1 a lake, 2 the sea
    }

    /// <summary>
    /// The rivers of a built world as lines, for drawing (2026-11-01): from each channel head down, the larger branch going
    /// on at each confluence, largest river first. A river's points are its channel cells' centreline points (the cell's
    /// centre + RivX, RivY) in order, and a last point where it ends: on the river it joins (that river's point in the cell
    /// it flows into) or in its lake (the centre of the lake cell it flows into).
    /// </summary>
    public static List<RiverLine> Lines(World w)
    {
        int FW = Map.FW, n = FW * FW;
        var chan = new bool[n];
        for (int i = 0; i < n; i++) chan[i] = w.Cell[i] != (byte)Realm.Land && w.LakeOf[i] < 0 && w.FloodFrom[i] == i;
        var main = new int[n]; Array.Fill(main, -1);
        var ord = new int[n]; var best = new int[n]; var nbest = new int[n];
        for (int q = n - 1; q >= 0; q--)
        {
            int c = w.DrainOrder[q];
            if (!chan[c]) continue;
            ord[c] = best[c] == 0 ? 1 : nbest[c] >= 2 ? best[c] + 1 : best[c];
            int r = w.Recv[c];
            if (r < 0 || !chan[r]) continue;
            if (ord[c] > best[r]) { best[r] = ord[c]; nbest[r] = 1; } else if (ord[c] == best[r]) nbest[r]++;
            int m = main[r];
            if (m < 0 || w.Flow[c] > w.Flow[m] || (w.Flow[c] == w.Flow[m] && c < m)) main[r] = c;
        }
        var heads = new List<int>(); var key = new List<float>();
        for (int c = 0; c < n; c++) if (chan[c] && best[c] == 0) heads.Add(c);
        // each river's cells, and its last
        var cellsOf = new List<List<int>>();
        foreach (int c0 in heads)
        {
            var l = new List<int>();
            int c = c0;
            while (true)
            {
                l.Add(c);
                int r = w.Recv[c];
                if (r < 0 || !chan[r] || main[r] != c) break;
                c = r;
            }
            cellsOf.Add(l);
        }
        var idx = new int[cellsOf.Count];
        for (int k = 0; k < idx.Length; k++) idx[k] = k;
        Array.Sort(idx, (a, b) =>
        {
            float qa = w.Flow[cellsOf[a][^1]], qb = w.Flow[cellsOf[b][^1]];
            return qa != qb ? qb.CompareTo(qa) : cellsOf[a][0].CompareTo(cellsOf[b][0]);
        });
        var riverOf = new int[n]; Array.Fill(riverOf, -1);
        for (int k = 0; k < idx.Length; k++) foreach (int c in cellsOf[idx[k]]) riverOf[c] = k;
        var lines = new List<RiverLine>();
        for (int k = 0; k < idx.Length; k++)
        {
            var cells = cellsOf[idx[k]];
            var L = new RiverLine();
            float px = 0f, py = 0f;
            foreach (int c in cells) Add(c, true, c);
            int last = cells[^1], r = w.Recv[last];
            L.Order = ord[last];
            if (r >= 0 && chan[r]) { L.Down = riverOf[r]; L.EndKind = 0; Add(r, true, last); }
            else if (r >= 0) { L.EndKind = w.LakeOf[r] >= 0 && w.LakeExit[w.LakeOf[r]] < 0 ? 2 : 1; Add(r, false, last); }
            lines.Add(L);

            // the point of cell c (its centreline point, or its centre), with the width and depth of cell of
            void Add(int c, bool line, int of)
            {
                float x = c % FW + 0.5f + (line ? w.RivX[c] : 0f), y = c / FW + 0.5f + (line ? w.RivY[c] : 0f);
                if (L.Points.Count > 0) { x += FW * MathF.Round((px - x) / FW); y += FW * MathF.Round((py - y) / FW); }
                px = x; py = y;
                // the true width in field cells (2026-11-01): a stream a few metres wide is a small share of a 16 m cell
                L.Points.Add((x, y, Hydrology.WidthM(w.Flow[of]) / K.GEO_CELL_M, w.Depth[of]));
            }
        }
        return lines;
    }
}
