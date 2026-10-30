// Terrain, water and the initial vegetation (DESIGN §10.1). Runs once, at initialisation.
// Allocation is allowed here (it is part of construction, NR-101).

using System;

namespace Neuroreef;

public static class Terrain
{
    // Periodic value noise and fBm: the v1 method (makeTerrain), on a torus.
    sealed class Noise
    {
        readonly float[] _v; readonly int _l;
        public Noise(int l, ref Rng.Seq r) { _l = l; _v = new float[l * l]; for (int i = 0; i < _v.Length; i++) _v[i] = r.Next(); }
        public float At(float x, float y)
        {
            float fx = x * _l, fy = y * _l;
            int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy);
            float tx = fx - x0, ty = fy - y0;
            tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
            int xa = World.WrapI(x0, _l), xb = (xa + 1) % _l, ya = World.WrapI(y0, _l), yb = (ya + 1) % _l;
            float a = _v[ya * _l + xa], b = _v[ya * _l + xb], c = _v[yb * _l + xa], d = _v[yb * _l + xb];
            return (a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty;
        }
    }

    static Func<float, float, float> Fbm(ref Rng.Seq r, int baseL, int oct)
    {
        var ns = new Noise[oct];
        for (int o = 0; o < oct; o++) ns[o] = new Noise(baseL << o, ref r);
        return (x, y) =>
        {
            float s = 0, a = 1, n = 0;
            for (int o = 0; o < oct; o++) { s += ns[o].At(x, y) * a; n += a; a *= 0.52f; }
            return s / n;
        };
    }

    public static void Build(World w)
    {
        int FW = Map.FW, n = FW * FW;
        var r = new Rng.Seq(w.TerrainSeed * 7919u + 13u);
        var elevN = Fbm(ref r, 3, 6);
        var moistN = Fbm(ref r, 4, 4);
        var vegN = Fbm(ref r, 6, 3);

        float mn = 1, mx = 0;
        for (int y = 0; y < FW; y++)
            for (int x = 0; x < FW; x++)
            {
                float e = elevN(x / (float)FW, y / (float)FW);
                w.Elev[y * FW + x] = e; mn = MathF.Min(mn, e); mx = MathF.Max(mx, e);
            }
        for (int i = 0; i < n; i++) w.Elev[i] = (w.Elev[i] - mn) / (mx - mn);

        // Lake level: the LAKE_SHARE quantile of elevation.
        var sorted = (float[])w.Elev.Clone();
        Array.Sort(sorted);
        w.SeaLevel = sorted[(int)(Nature.Water * n)];

        // Rivers: D8 flow accumulation, processed from the highest cell down.
        var idx = new int[n];
        for (int i = 0; i < n; i++) idx[i] = i;
        var keys = (float[])w.Elev.Clone();
        Array.Sort(keys, idx);
        var acc = new float[n];
        for (int k = n - 1; k >= 0; k--)
        {
            int i = idx[k]; acc[i] += 1;
            int x = i % FW, y = i / FW, best = -1; float be = w.Elev[i];
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int j = World.WrapI(y + dy, FW) * FW + World.WrapI(x + dx, FW);
                    if (w.Elev[j] < be) { be = w.Elev[j]; best = j; }
                }
            if (best >= 0) acc[best] += acc[i];
        }

        for (int i = 0; i < n; i++)
        {
            float e = w.Elev[i];
            if (e < w.SeaLevel) w.Depth[i] = w.SeaLevel - e;
            else if (acc[i] > K.RIVER_ACC * (n / 65536f)) w.Depth[i] = K.SHALLOW * 0.5f;
            else w.Depth[i] = 0;
            w.Cell[i] = (byte)(w.Depth[i] <= 0 ? Realm.Land : w.Depth[i] <= K.SHALLOW ? Realm.Shallow : Realm.Deep);
        }

        Distance(w.Cell, (byte)Realm.Water, w.DistWater);
        Distance(w.Cell, (byte)Realm.Land, w.DistLand);
        Climate.Build(w);   // height, continentality and the temperature now (Revision O); draws no random numbers

        // Pond sites (Revision K, ecology review): land basins, the lowest point in their neighbourhood, away from
        // standing water; each holds rain in the wet season and dries out in the dry one (Ponds.Update).
        uint ps = Rng.Hash(w.Seed, 0, 0, Rs.Pond);
        for (int y = 0; y < FW; y++)
            for (int x = 0; x < FW; x++)
            {
                int i = y * FW + x;
                if (w.Cell[i] != (byte)Realm.Land || w.DistWater[i] < K.POND_GAP) continue;
                bool low = true;
                for (int dy = -K.POND_R; dy <= K.POND_R && low; dy++)
                    for (int dx = -K.POND_R; dx <= K.POND_R; dx++)
                        if ((dx != 0 || dy != 0) && w.Elev[World.WrapI(y + dy, FW) * FW + World.WrapI(x + dx, FW)] <= w.Elev[i]) { low = false; break; }
                if (!low || Rng.U(ps, 0, (uint)i, 0) >= MathF.Min(1f, Nature.Ponds)) continue;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++) w.PondSite[World.WrapI(y + dy, FW) * FW + World.WrapI(x + dx, FW)] = 1;
            }
        Ponds.Update(w);

        // Moisture base and fertility.
        for (int y = 0; y < FW; y++)
            for (int x = 0; x < FW; x++)
            {
                int i = y * FW + x;
                float m = moistN(x / (float)FW, y / (float)FW);
                float wet = MathK.Exp(-w.DistWater[i] / 10f);
                float valley = 1f - MathF.Min(1f, MathF.Max(0f, (w.Elev[i] - w.SeaLevel) / (1f - w.SeaLevel)));
                w.MoistBase[i] = Math.Clamp(0.15f + 0.45f * wet + 0.25f * valley + 0.35f * (m - 0.5f) * 2f, 0.05f, 1f);
                w.Fert[i] = Math.Clamp(0.3f + 0.7f * (m - 0.25f) * 1.6f, 0.1f, 1f);
                w.Moisture[i] = w.MoistBase[i];
            }
        if (Climate.On)
        {
            // where rain falls (Revision P): the soil's wetness follows the rain (Climate.Rainfall) at RAIN_W of it, and
            // the ground's own lie (near water, in valleys) at the rest; the land as a whole stays as wet as before
            var rain = new float[FW * FW];
            Climate.Rainfall(w, rain);
            var before = (float[])w.MoistBase.Clone();   // the land's wetness with no climate: its total is kept
            double ms = 0; int nl = 0;
            for (int i = 0; i < FW * FW; i++) if (w.Cell[i] == (byte)Realm.Land) { ms += w.MoistBase[i]; nl++; }
            float mean = nl > 0 ? (float)(ms / nl) : 0.5f;
            for (int i = 0; i < FW * FW; i++)
            {
                if (w.Cell[i] != (byte)Realm.Land) continue;
                float rel = K.RAIN_W * rain[i] + (1f - K.RAIN_W) * w.MoistBase[i] / mean;
                w.MoistBase[i] = Math.Clamp(0.05f + (mean - 0.05f) * rel, 0.05f, 1f);
                w.Moisture[i] = w.MoistBase[i];
            }
            Climate.Normalise(w, before); Ponds.Update(w);   // evaporation normalised to keep the no-climate total; ponds refilled with it
        }

        // Initial vegetation: a mosaic of grassland, shrubland and woodland, and plankton in the water.
        var P = w.Pool;
        for (int y = 0; y < FW; y++)
            for (int x = 0; x < FW; x++)
            {
                int i = y * FW + x;
                float wb = w.MoistBase[i], v = vegN(x / (float)FW, y / (float)FW);
                w.Nutrient[i] = 0.35f + 0.3f * w.Fert[i];
                if (w.Cell[i] != (byte)Realm.Land)
                {
                    P[(int)Pool.Phyto][i] = 0.3f;
                    P[(int)Pool.Algae][i] = 0.4f * MathK.Exp(-w.Depth[i] / K.SHALLOW);
                    P[(int)Pool.Detritus][i] = 0.1f;
                    continue;
                }
                float tree = Math.Clamp((wb - 0.45f) * 1.6f + (v - 0.5f) * 1.2f, 0f, 0.7f);
                float shrub = Math.Clamp(0.25f + (v - 0.5f) * 0.8f, 0f, 0.4f) * (1f - tree);
                float grass = Math.Clamp(0.9f * wb * w.Fert[i] + 0.2f, 0f, 1f - tree - shrub);
                w.Tree[i] = tree; w.Shrub[i] = shrub; P[(int)Pool.Grass][i] = grass;
                w.Sapling[i] = 0.05f * tree;
                P[(int)Pool.Browse][i] = shrub + K.B_TREE * tree;
                P[(int)Pool.Fruit][i] = 0.15f * tree;
                P[(int)Pool.Seed][i] = 0.10f * grass + 0.05f * tree;
                P[(int)Pool.Nectar][i] = 0.05f * (grass + shrub);
                P[(int)Pool.Root][i] = 0.4f * (grass + shrub);
                P[(int)Pool.Litter][i] = 0.15f;
                P[(int)Pool.Wood][i] = 0.3f * tree;
            }
    }

    /// <summary>Multi-source distance (in cells, 8-connected chamfer) on the torus to the nearest cell with any bit of mask.</summary>
    [ThreadStatic] static int[]? _q;
    [ThreadStatic] static bool[]? _inQ;

    internal static void Distance(byte[] cell, byte mask, float[] dist)
    {
        int FW = Map.FW, n = FW * FW;
        if (_q == null || _q.Length < n) { _q = new int[n]; _inQ = new bool[n]; }   // reused: ponds remeasure this during the run
        var q = _q; var inQ = _inQ!; int h = 0, cnt = 0;
        for (int i = 0; i < n; i++)
        {
            inQ[i] = false;
            if ((cell[i] & mask) != 0) { dist[i] = 0; q[cnt++] = i; inQ[i] = true; }
            else dist[i] = float.MaxValue;
        }
        // label-correcting search on a ring queue: a cell goes back in the queue whenever its distance improves
        int t = cnt % n;
        while (cnt > 0)
        {
            int i = q[h]; h = (h + 1) % n; cnt--; inQ[i] = false;
            int x = i % FW, y = i / FW;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int j = World.WrapI(y + dy, FW) * FW + World.WrapI(x + dx, FW);
                    float nd = dist[i] + ((dx != 0 && dy != 0) ? 1.4142f : 1f);
                    if (nd < dist[j] - 1e-4f)
                    {
                        dist[j] = nd;
                        if (!inQ[j]) { inQ[j] = true; q[t] = j; t = (t + 1) % n; cnt++; }
                    }
                }
        }
        for (int i = 0; i < n; i++) if (dist[i] == float.MaxValue) dist[i] = 999f;
    }
}
