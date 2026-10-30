// Geology at world generation (Revision R): the land is formed, not drawn. Nothing here names a mountain range, a
// valley or a lake. A few rigid plates move over the torus; where they converge the crust thickens and rises
// (collision orogens, or volcanic arcs where the thinner plate sinks under the thicker), where they part it thins and
// sinks (rifts), and where they slide past each other little happens. Rain then wears the rising land down for tens of
// millions of years, faster in soft rock than in hard, until erosion nearly balances uplift. Mountain belts, rift
// basins, drainage basins, ridges and valleys, and where the rock is hard or soft, are what follows.
//
// Plates: a weighted Voronoi tessellation of the torus (seed points spread by best-candidate sampling), its boundaries
// bent by a low-frequency warp. Each plate has a velocity (a translation and a slow spin about its centre) and a
// crustal thickness. At each cell the boundary is the one with the nearest other plate: its distance d, its normal n,
// and the relative motion of the two plates there. Its normal component c (> 0: converging) works the crust; the
// tangential part (a transform) does not.
// Crust: mass balance. A belt of half-width W that takes up convergence c thickens at T c / (sqrt(pi) W) exp(-(d/W)^2)
// (the profile integrates to T c across the boundary: the crust shortened is the crust thickened); a rift thins the
// same way. Airy isostasy turns thickening into rock uplift: U = AIRY x dT/dt (Airy 1855). Plates start at the Airy
// height of their own thickness. A rift stops thinning at GEO_T_MIN (a stretched crust; McKenzie 1978).
// Erosion: on a coarse grid (FW / 2), GEO_ITERS steps over GEO_MYR million years: uplift (none above GEO_H_MAX, where a
// thickened crust spreads instead of rising); detachment-limited stream power above a threshold,
// dh/dt = -K (A^0.5 S - PSI) (Howard 1994; Tucker 2004), K by rock (Stock & Montgomery 1999), solved implicitly
// downstream-first (Braun & Willett 2013) over Priority-Flood routing of the rain (Climate.Rainfall, re-read as the
// relief grows); the load carried down and dropped where it exceeds capacity or fills a basin, as in Hydrology.Erode,
// but never above the lowest cell feeding it; hillslope diffusion; and flexural isostasy (the ground rises under
// what is worn off it and sinks under what is laid on it). The lake level is the Nature.Water quantile throughout, so
// lakes are the base level. Sediment laid down becomes soft sedimentary cover; erosion strips cover before bedrock.
// The worked coarse relief is upsampled (Catmull-Rom) to the field grid, with sub-grid roughness on top in proportion
// to the coarse relief, and Terrain.Build finishes it with the fine passes of Hydrology.Erode (hard rock cuts slower).
// Simplifications: plate boundaries are held fixed over the deep time (a kinematic steady state, not plate motion);
// the plateau limit is a cap, not crustal flow; the three scales of this world (the plates' GEO_KM, the climate's
// pole-to-pole rows, the animals' walking distances) are not one.
// Allocation is allowed here (it is part of construction, NR-101).

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Neuroreef;

/// <summary>Kinds of surface rock (World.Rock, Revision R).</summary>
public static class Rock
{
    public const byte Crystalline = 0, Volcanic = 1, Sedimentary = 2, N = 3;
    public static readonly string[] Names = { "crystalline", "volcanic", "sedimentary" };

    /// <summary>Stream-power erodibility (/Myr, A in km^2) of each kind.</summary>
    public static readonly float[] Erod = { K.GEO_K_CRYST, K.GEO_K_VOLC, K.GEO_K_SED };
    /// <summary>Base fertility of the soil each kind weathers into (its parent material).</summary>
    public static readonly float[] Fert = { K.FERT_CRYST, K.FERT_VOLC, K.FERT_SED };
}

public static class Geology
{
    /// <summary>What the nearest plate boundary of a cell is, within its belt (Crust.Bound).</summary>
    public const byte Interior = 0, Convergent = 1, Divergent = 2, Transform = 3;

    /// <summary>The coarse deep-time state of a world: plates, boundaries, uplift, rock and the worked relief.</summary>
    public sealed class Crust
    {
        public readonly int CW;            // coarse grid side
        public readonly float Km;          // km per coarse cell
        public readonly byte[] Plate, Bound, Bed;        // nearest plate, boundary kind within its belt, bedrock kind
        public readonly bool[] Arc, Collision;           // convergent cells on an arc (over a sinking plate) or in a collision belt
        public readonly float[] H, Sed, Uplift, Thick;   // km: relief, sedimentary cover; km/Myr: rock uplift; km: crust as the plates thickened or thinned it (not counting erosion)
        public readonly float[] Conv, Rel;               // km/Myr at the nearest boundary: closing speed (< 0: opening), relative speed
        public float Sea;                                // km: lake level on the coarse relief
        public int Plates;
        public float[] PX = Array.Empty<float>(), PY = Array.Empty<float>(), VX = Array.Empty<float>(), VY = Array.Empty<float>(),
            Spin = Array.Empty<float>(), PT = Array.Empty<float>(), PW = Array.Empty<float>();
        /// <summary>Over the last tenth of the run: mean |dh/dt| / mean U in the uplifting belts (0 = steady state).</summary>
        public float Residual;
        public Crust(int cw)
        {
            CW = cw; Km = K.GEO_KM / cw; int n = cw * cw;
            Plate = new byte[n]; Bound = new byte[n]; Bed = new byte[n]; Arc = new bool[n]; Collision = new bool[n];
            H = new float[n]; Sed = new float[n]; Uplift = new float[n]; Thick = new float[n]; Conv = new float[n]; Rel = new float[n];
        }
        public byte Surface(int i) => Sed[i] > K.GEO_SED_MIN ? Rock.Sedimentary : Bed[i];
    }

    /// <summary>Coarse grid side for the field grid: half of it, at least 64 cells (or the whole of a smaller grid).</summary>
    public static int CoarseSide(int fw) => Math.Max(fw / 2, Math.Min(fw, 64));

    /// <summary>
    /// The deep-time genesis of world w: sets Elev (0..1), SeaLevel (the Nature.Water quantile) and Rock at the field grid,
    /// before the fine passes of Hydrology.Erode. Deterministic in w.TerrainSeed. Returns the coarse state.
    /// </summary>
    public static Crust Form(World w)
    {
        int FW = Map.FW, CW = CoarseSide(FW);
        var c = new Crust(CW);
        uint seed = w.TerrainSeed * 2654435761u + 0x6E01u;
        MakePlates(c, seed);
        var rw = new Rng.Seq(seed + 1u);
        var warpX = Terrain.Fbm(ref rw, 3, 3); var warpY = Terrain.Fbm(ref rw, 3, 3);
        Tectonics(c, warpX, warpY);
        Erode(w, c);
        Upsample(w, c, seed + 2u);
        return c;
    }

    // ---- plates
    static float Wrap(float d) => d - MathF.Round(d);   // torus difference in map shares, -0.5..0.5

    static void MakePlates(Crust c, uint seed)
    {
        var r = new Rng.Seq(seed);
        int P = K.GEO_PLATES_LO + (int)(r.Next() * (K.GEO_PLATES_HI - K.GEO_PLATES_LO + 1));
        P = Math.Clamp(P, K.GEO_PLATES_LO, K.GEO_PLATES_HI);
        c.Plates = P;
        c.PX = new float[P]; c.PY = new float[P]; c.VX = new float[P]; c.VY = new float[P]; c.Spin = new float[P]; c.PT = new float[P]; c.PW = new float[P];
        float spacing = 1f / MathF.Sqrt(P);
        for (int k = 0; k < P; k++)
        {
            // best candidate (Mitchell 1991): of a few random points, the one farthest from the plates already placed
            float bx = 0, by = 0, best = -1;
            for (int t = 0; t < 8; t++)
            {
                float x = r.Next(), y = r.Next(), dmin = 1f;
                for (int j = 0; j < k; j++) { float dx = Wrap(x - c.PX[j]), dy = Wrap(y - c.PY[j]); dmin = MathF.Min(dmin, dx * dx + dy * dy); }
                if (dmin > best) { best = dmin; bx = x; by = y; }
            }
            c.PX[k] = bx; c.PY[k] = by;
            c.PW[k] = r.Next() * K.GEO_SIZE_VAR * spacing;
            float ang = 2f * MathF.PI * r.Next(), sp = K.GEO_V_LO + (K.GEO_V_HI - K.GEO_V_LO) * r.Next();
            c.VX[k] = sp * MathK.Cos(ang); c.VY[k] = sp * MathK.Sin(ang);
            c.Spin[k] = (2f * r.Next() - 1f) * K.GEO_SPIN;
            c.PT[k] = K.GEO_T_LO + (K.GEO_T_HI - K.GEO_T_LO) * r.Next();
        }
    }

    // ---- boundaries, uplift and the starting crust
    static void Tectonics(Crust c, Func<float, float, float> warpX, Func<float, float, float> warpY)
    {
        int CW = c.CW, P = c.Plates;
        float L = K.GEO_KM, sq = MathF.Sqrt(MathF.PI), tRef = 0.5f * (K.GEO_T_LO + K.GEO_T_HI);
        Parallel.For(0, CW, y =>
        {
            for (int x = 0; x < CW; x++)
            {
                int i = y * CW + x;
                float u = (x + 0.5f) / CW, v = (y + 0.5f) / CW;
                float wu = u + K.GEO_WARP * (warpX(u, v) - 0.5f), wv = v + K.GEO_WARP * (warpY(u, v) - 0.5f);
                int a = -1, b = -1; float da = float.MaxValue, db = float.MaxValue;
                for (int k = 0; k < P; k++)
                {
                    float dx = Wrap(wu - c.PX[k]), dy = Wrap(wv - c.PY[k]);
                    float d = MathF.Sqrt(dx * dx + dy * dy) - c.PW[k];
                    if (d < da) { db = da; b = a; da = d; a = k; }
                    else if (d < db) { db = d; b = k; }
                }
                float dist = 0.5f * (db - da) * L;   // km to the boundary with plate b
                // the boundary normal, pointing from a into b: the gradient of the distance difference
                float ax = Wrap(wu - c.PX[a]), ay = Wrap(wv - c.PY[a]), bx = Wrap(wu - c.PX[b]), by = Wrap(wv - c.PY[b]);
                float la = MathF.Max(1e-6f, MathF.Sqrt(ax * ax + ay * ay)), lb = MathF.Max(1e-6f, MathF.Sqrt(bx * bx + by * by));
                float nx = ax / la - bx / lb, ny = ay / la - by / lb, nl = MathF.Sqrt(nx * nx + ny * ny);
                if (nl < 1e-6f) { nx = Wrap(c.PX[b] - c.PX[a]); ny = Wrap(c.PY[b] - c.PY[a]); nl = MathF.Max(1e-6f, MathF.Sqrt(nx * nx + ny * ny)); }
                nx /= nl; ny /= nl;
                // each plate's velocity here (km/Myr): translation plus spin about its centre
                float vax = c.VX[a] - c.Spin[a] * ay * L, vay = c.VY[a] + c.Spin[a] * ax * L;
                float vbx = c.VX[b] - c.Spin[b] * by * L, vby = c.VY[b] + c.Spin[b] * bx * L;
                float rx = vax - vbx, ry = vay - vby;
                float conv = rx * nx + ry * ny;                  // > 0: the plates close
                float rel = MathF.Sqrt(rx * rx + ry * ry);
                float Ta = c.PT[a], Tb = c.PT[b], Tm = 0.5f * (Ta + Tb);

                float rate = 0f; byte bed = Rock.Crystalline; bool volc = false, arc = false, coll = false;
                if (conv > 0f)
                {
                    if (MathF.Abs(Ta - Tb) < K.GEO_SUBDUCT)
                    {   // collision: both sides thicken
                        float g = MathK.Exp(-Sq(dist / K.GEO_W_OROGEN));
                        rate = Tm * conv / (sq * K.GEO_W_OROGEN) * g;
                        coll = dist < K.GEO_W_OROGEN;
                    }
                    else if (Ta > Tb)
                    {   // a rides over b: a volcanic arc behind the trench, shortening as well as magma
                        float g = MathK.Exp(-Sq((dist - K.GEO_ARC_D) / K.GEO_W_ARC));
                        rate = K.GEO_ARC_SHARE * Tm * conv / (sq * K.GEO_W_ARC) * g;
                        volc = g > 0.3f; arc = dist < K.GEO_ARC_D + K.GEO_W_ARC;
                    }
                    else
                    {   // a sinks under b: a flexural moat (trench, foredeep) in front of the load
                        float g = MathK.Exp(-Sq(dist / K.GEO_W_ARC));
                        rate = -K.GEO_FOREDEEP * K.GEO_ARC_SHARE * Tm * conv / (sq * K.GEO_W_ARC) * g;
                    }
                }
                else
                {   // rift: the crust thins; fast rifts erupt basalt along their axis
                    float g = MathK.Exp(-Sq(dist / K.GEO_W_RIFT));
                    rate = Tm * conv / (sq * K.GEO_W_RIFT) * g;
                    volc = g > 0.5f && -conv > 0.5f * K.GEO_V_HI;
                }
                if (volc) bed = Rock.Volcanic;

                byte kind = Interior;
                float belt = conv > 0f ? MathF.Max(K.GEO_W_OROGEN, K.GEO_ARC_D + K.GEO_W_ARC) : K.GEO_W_RIFT;
                if (dist < belt) kind = MathF.Abs(conv) < K.GEO_OBLIQUE * rel ? Transform : conv > 0f ? Convergent : Divergent;

                c.Plate[i] = (byte)a; c.Bound[i] = kind; c.Bed[i] = bed; c.Arc[i] = arc && kind == Convergent; c.Collision[i] = coll && kind == Convergent;
                c.Uplift[i] = K.GEO_AIRY * rate; c.Conv[i] = conv; c.Rel[i] = rel;
                c.Thick[i] = Ta;
                c.H[i] = K.GEO_AIRY * (Ta - tRef);                                  // Airy height of the plate's crust
                c.Sed[i] = Ta < K.GEO_T_CRATON && !volc ? K.GEO_COVER : 0f;         // young platforms carry cover, shields and lavas do not
            }
        });
    }

    static float Sq(float x) => x * x;

    // ---- deep-time erosion against uplift, on the coarse grid
    static void Erode(World w, Crust c)
    {
        int CW = c.CW, n = CW * CW;
        float dt = K.GEO_MYR / K.GEO_ITERS, km = c.Km, cellA = km * km;
        var net = new Hydrology.Net(n);
        var seen = new bool[n]; var pq = new PriorityQueue<int, float>(n);
        var rain = new float[n]; var cut = new float[n]; var load = new float[n]; var h0 = new float[n]; var hMark = new float[n];
        var sorted = new float[n]; var hStep = new float[n]; var tmp = new float[n]; var low = new float[n];
        int flexR = Math.Max(1, (int)MathF.Round(K.GEO_FLEX / km));
        float carry = K.CARRY_K / K.ERODE_K;   // capacity against erodibility, as at the fine passes
        float diff = K.GEO_DIFF * dt / cellA;  // explicit diffusion number per step
        int sub = Math.Max(1, (int)MathF.Ceiling(diff / 0.2f)); diff /= sub;
        int mark = K.GEO_ITERS - K.GEO_ITERS / 10;
        for (int it = 0; it < K.GEO_ITERS; it++)
        {
            if (it % 10 == 0) c.Sea = Quantile(c.H, sorted, Nature.Water);
            if (it % K.GEO_RAIN_EVERY == 0) CoarseRain(w, c, rain);
            if (it == mark) Array.Copy(c.H, hMark, n);

            // uplift; a rift or moat stops sinking once its crust is thinned to GEO_T_MIN
            for (int i = 0; i < n; i++)
            {
                float u = c.Uplift[i] * dt;
                if (u < 0f) { float room = (c.Thick[i] - K.GEO_T_MIN) * K.GEO_AIRY; u = MathF.Max(u, -MathF.Max(0f, room)); }
                else u = MathF.Min(u, MathF.Max(0f, K.GEO_H_MAX - c.H[i]));   // a crust this high spreads under its own weight instead of rising
                c.H[i] += u; c.Thick[i] += u / K.GEO_AIRY;
            }
            Array.Copy(c.H, hStep, n);

            Hydrology.Route(c.H, c.Sea, rain, net, CW, seen, pq);
            int[] recv = net.Recv, order = net.Order; float[] flow = net.Flow, fill = net.Fill;
            // stream power above a threshold, outlets first: dh/dt = -K (sqrt(A) S - PSI) where positive, implicitly
            // h' = (h + F h'_r + K dt PSI) / (1 + F), F = K dt sqrt(A) / dx (A, dx in km)
            for (int q = 0; q < n; q++)
            {
                int i = order[q], r = recv[i];
                cut[i] = 0f;
                if (r < 0 || c.H[i] <= c.H[r]) continue;
                float kr = Rock.Erod[c.Surface(i)] * dt;
                float F = kr * MathF.Sqrt(flow[i] * cellA) / (Dist(i, r, CW) * km);
                float hn = (c.H[i] + F * c.H[r] + kr * K.GEO_PSI) / (1f + F);
                if (hn >= c.H[i]) continue;
                cut[i] = c.H[i] - hn; c.H[i] = hn;
            }
            // transport, sources first: cover goes before bedrock; a surplus over capacity is partly dropped; a basin or a
            // lake keeps what it can hold up to its rim (the lake to its level); what is left over leaves the land
            Array.Clear(load); Array.Fill(low, float.MaxValue);
            for (int q = n - 1; q >= 0; q--)
            {
                int i = order[q], r = recv[i];
                float s = load[i] + cut[i];
                c.Sed[i] = MathF.Max(0f, c.Sed[i] - cut[i]);
                float dep;
                // a deposit cannot raise a cell above the lowest cell that drains into it (the channel would go round it)
                float room = low[i] == float.MaxValue ? 0f : MathF.Max(0f, low[i] - c.H[i]);
                if (r < 0) dep = MathF.Min(s, MathF.Max(0f, c.Sea - c.H[i]));
                else
                {
                    float slope = (c.H[i] - c.H[r]) / (Dist(i, r, CW) * km);
                    if (slope <= 0f) dep = MathF.Min(s, MathF.Max(0f, fill[i] - c.H[i]));
                    else
                    {
                        float cap = carry * K.GEO_K_SED * dt * MathF.Sqrt(flow[i] * cellA) * slope;
                        dep = s > cap ? K.DEPOSIT_FRAC * (s - cap) : 0f;
                    }
                }
                dep = MathF.Min(dep, room);
                c.H[i] += dep; c.Sed[i] += dep;
                if (r >= 0) { load[r] += s - dep; low[r] = MathF.Min(low[r], c.H[i]); }
            }
            // hillslope diffusion (explicit, sub-stepped to stay stable); what moves is regolith, so it joins the cover
            for (int k = 0; k < sub; k++)
            {
                Array.Copy(c.H, h0, n);
                float dk = diff;
                Parallel.For(0, CW, y =>
                {
                    int ym = (y == 0 ? CW - 1 : y - 1) * CW, yp = (y == CW - 1 ? 0 : y + 1) * CW, yr = y * CW;
                    for (int x = 0; x < CW; x++)
                    {
                        int i = yr + x, xm = x == 0 ? CW - 1 : x - 1, xp = x == CW - 1 ? 0 : x + 1;
                        float dh = dk * (h0[yr + xm] + h0[yr + xp] + h0[ym + x] + h0[yp + x] - 4f * h0[i]);
                        c.H[i] = h0[i] + dh;
                        if (dh > 0f) c.Sed[i] += dh; else c.Sed[i] = MathF.Max(0f, c.Sed[i] + dh);
                    }
                });
            }
            // flexural isostasy: the lithosphere rises under eroded ground and sinks under laid-down sediment, by
            // RHO_CRUST / RHO_MANTLE of the change in load, spread over the flexural wavelength
            for (int i = 0; i < n; i++) hStep[i] = c.H[i] - hStep[i];
            Blur(hStep, tmp, CW, flexR); Blur(hStep, tmp, CW, flexR);
            for (int i = 0; i < n; i++)
            {
                float up = -K.GEO_REBOUND * hStep[i];
                if (up > 0f) up = MathF.Min(up, MathF.Max(0f, K.GEO_H_MAX - c.H[i]));   // the plateau limit holds here too
                c.H[i] += up;
            }
        }
        c.Sea = Quantile(c.H, sorted, Nature.Water);

        // how near the steady state: |dh/dt| against U over the last tenth, in the uplifting belts
        double du = 0, uu = 0; float span = (K.GEO_ITERS - mark) * dt;
        for (int i = 0; i < n; i++)
            if (c.Uplift[i] > 0.1f * K.GEO_AIRY && c.H[i] > c.Sea) { du += MathF.Abs(c.H[i] - hMark[i]) / span; uu += c.Uplift[i]; }
        c.Residual = uu > 0 ? (float)(du / uu) : 0f;
    }

    /// <summary>Box blur of radius r on the torus (twice is near a Gaussian), in place.</summary>
    static void Blur(float[] a, float[] tmp, int W, int r)
    {
        float inv = 1f / (2 * r + 1);
        Parallel.For(0, W, y =>
        {
            int row = y * W; float s = 0f;
            for (int k = -r; k <= r; k++) s += a[row + World.WrapI(k, W)];
            for (int x = 0; x < W; x++) { tmp[row + x] = s * inv; s += a[row + World.WrapI(x + r + 1, W)] - a[row + World.WrapI(x - r, W)]; }
        });
        Parallel.For(0, W, x =>
        {
            float s = 0f;
            for (int k = -r; k <= r; k++) s += tmp[World.WrapI(k, W) * W + x];
            for (int y = 0; y < W; y++) { a[y * W + x] = s * inv; s += tmp[World.WrapI(y + r + 1, W) * W + x] - tmp[World.WrapI(y - r, W) * W + x]; }
        });
    }

    static float Dist(int a, int b, int W) => (a % W != b % W && a / W != b / W) ? 1.4142135f : 1f;

    static float Quantile(float[] h, float[] buf, float q)
    {
        Array.Copy(h, buf, h.Length); Array.Sort(buf);
        return buf[Math.Clamp((int)(q * h.Length), 0, h.Length - 1)];
    }

    /// <summary>Climate.Rainfall over the coarse relief as it stands: drawn on the field grid, averaged to the coarse.</summary>
    static void CoarseRain(World w, Crust c, float[] rainC)
    {
        int FW = Map.FW, CW = c.CW, f = FW / CW;
        MinMax(c.H, out float mn, out float mx);
        float sc = 1f / MathF.Max(1e-6f, mx - mn);
        Sample(c.H, CW, w.Elev, FW, false);
        for (int i = 0; i < FW * FW; i++) w.Elev[i] = (w.Elev[i] - mn) * sc;
        w.SeaLevel = (c.Sea - mn) * sc;
        var rf = Terrain.RunoffRain(w);
        Array.Clear(rainC);
        float inv = 1f / (f * f);
        for (int y = 0; y < FW; y++)
            for (int x = 0; x < FW; x++) rainC[(y / f) * CW + x / f] += rf[y * FW + x] * inv;
    }

    static void MinMax(float[] a, out float mn, out float mx)
    {
        mn = float.MaxValue; mx = float.MinValue;
        foreach (var v in a) { if (v < mn) mn = v; if (v > mx) mx = v; }
    }

    /// <summary>Resample a periodic CW grid onto a periodic FW grid: bilinear, or Catmull-Rom bicubic.</summary>
    static void Sample(float[] src, int CW, float[] dst, int FW, bool cubic)
    {
        float s = (float)CW / FW;
        Parallel.For(0, FW, y =>
        {
            float gy = (y + 0.5f) * s - 0.5f; int y0 = (int)MathF.Floor(gy); float ty = gy - y0;
            for (int x = 0; x < FW; x++)
            {
                float gx = (x + 0.5f) * s - 0.5f; int x0 = (int)MathF.Floor(gx); float tx = gx - x0;
                float v;
                if (!cubic)
                {
                    int xa = World.WrapI(x0, CW), xb = World.WrapI(x0 + 1, CW), ya = World.WrapI(y0, CW) * CW, yb = World.WrapI(y0 + 1, CW) * CW;
                    v = (src[ya + xa] * (1 - tx) + src[ya + xb] * tx) * (1 - ty) + (src[yb + xa] * (1 - tx) + src[yb + xb] * tx) * ty;
                }
                else
                {
                    v = 0f;
                    for (int j = -1; j <= 2; j++)
                    {
                        int row = World.WrapI(y0 + j, CW) * CW; float wy = CR(j - ty), r = 0f;
                        for (int k = -1; k <= 2; k++) r += src[row + World.WrapI(x0 + k, CW)] * CR(k - tx);
                        v += r * wy;
                    }
                }
                dst[y * FW + x] = v;
            }
        });
    }

    // Catmull-Rom kernel
    static float CR(float t)
    {
        t = MathF.Abs(t);
        return t < 1f ? 1.5f * t * t * t - 2.5f * t * t + 1f : t < 2f ? -0.5f * t * t * t + 2.5f * t * t - 4f * t + 2f : 0f;
    }

    // ---- onto the field grid: the worked relief, sub-grid roughness, rock
    static void Upsample(World w, Crust c, uint seed)
    {
        int FW = Map.FW, n = FW * FW, CW = c.CW, f = Math.Max(1, FW / CW);
        Sample(c.H, CW, w.Elev, FW, true);
        // relief within a coarse cell, as a share of the relief between it and its neighbours (topography is about
        // self-similar over these scales): rough uplands, smooth plains and lake flats
        var relief = new float[CW * CW];
        for (int y = 0; y < CW; y++)
            for (int x = 0; x < CW; x++)
            {
                int i = y * CW + x; float h = c.H[i], m = 0f;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++) m = MathF.Max(m, MathF.Abs(c.H[World.WrapI(y + dy, CW) * CW + World.WrapI(x + dx, CW)] - h));
                relief[i] = K.GEO_ROUGH * m;
            }
        var amp = new float[n];
        Sample(relief, CW, amp, FW, false);
        var r = new Rng.Seq(seed);
        var rough = Terrain.Fbm(ref r, CW, 3);
        Parallel.For(0, FW, y =>
        {
            for (int x = 0; x < FW; x++)
            {
                int i = y * FW + x;
                w.Elev[i] += amp[i] * 2f * (rough(x / (float)FW, y / (float)FW) - 0.5f);
                int ci = Math.Min(CW - 1, y / f) * CW + Math.Min(CW - 1, x / f);
                w.Rock[i] = c.Surface(ci);
            }
        });
        Terrain.LakeLevel(w);
    }

    /// <summary>Each field cell's erodibility against soft sedimentary rock (for Hydrology.Erode).</summary>
    public static float[] Erodibility(World w)
    {
        int n = Map.FW * Map.FW;
        var k = new float[n];
        for (int i = 0; i < n; i++) k[i] = Rock.Erod[w.Rock[i]] / K.GEO_K_SED;
        return k;
    }
}
