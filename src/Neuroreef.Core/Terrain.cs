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

    internal static Func<float, float, float> Fbm(ref Rng.Seq r, int baseL, int oct)
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

    /// <summary>The raw relief of a world (Elev normalised to 0..1), its rock and its lake level, before the fine passes of
    /// running water have worked on it: the deep-time genesis of Geology.Form (Revision R).</summary>
    public static void Relief(World w) => Geology.Form(w);

    /// <summary>
    /// The rain that runs over the land (Revision Q): Climate.Rainfall over the land as it lies with only its lakes
    /// (relative to the land's mean, 1). Where rain falls follows the winds and the ground, not the climate's strength
    /// (Rainfall does not read it), so a seed makes the same land, rivers and valleys at any climate setting; the
    /// setting changes temperature and, as before, how much the soil's wetness follows the rain. Sets Cell to the lakes and land.
    /// </summary>
    public static float[] RunoffRain(World w)
    {
        int n = Map.FW * Map.FW;
        for (int i = 0; i < n; i++) w.Cell[i] = (byte)(w.Elev[i] < w.SeaLevel ? (w.SeaLevel - w.Elev[i] <= K.SHALLOW ? Realm.Shallow : Realm.Deep) : Realm.Land);
        var rain = new float[n];
        Climate.Rainfall(w, rain);
        return rain;
    }

    public static void Build(World w) => Finish(w, Shape(w));

    /// <summary>
    /// The first half of Build, which depends on the terrain seed alone (not the run seed): the geological genesis and
    /// the fine passes of running water. Returns the rain it ran with, which Finish routes again. Prehistory branches
    /// share their land, so this is done once and copied to each (Land.Shape).
    /// </summary>
    public static float[] Shape(World w)
    {
        // Geology (Revision R): plates, uplift and tens of millions of years of erosion make the relief and its rock
        Geology.Form(w);

        // Hydrology (Revision Q): rain falls on the land as it lies with its lakes, runs downhill (Priority-Flood
        // routing), cuts the ground and lays down what it carries (Hydrology.Erode). Rivers are the cells that gather
        // the most rain; nothing else decides where they are. Revision R: hard rock cuts slower.
        var rain0 = RunoffRain(w);
        Hydrology.Erode(w.Elev, w.SeaLevel, rain0, w.Alluvium, K.ERODE_ITERS, kRock: Geology.Erodibility(w));
        return rain0;
    }

    /// <summary>The second half of Build, on the shaped land: rivers, lakes, ponds, soil and the first plants (these draw on the run seed).</summary>
    public static void Finish(World w, float[] rain0)
    {
        var r = new Rng.Seq(w.TerrainSeed * 7919u + 13u);
        var moistN = Fbm(ref r, 4, 4);
        var vegN = Fbm(ref r, 6, 3);
        BuildWater(w, rain0, moistN, vegN);
    }

    /// <summary>Normalise Elev to 0..1 and set the sea level: the Nature.Water quantile of elevation (the relief's base
    /// level; what lies below it and is not cut off from it is the sea, Hydrology.Waters).</summary>
    internal static void LakeLevel(World w)
    {
        int n = Map.FW * Map.FW;
        float mn = float.MaxValue, mx = float.MinValue;
        for (int i = 0; i < n; i++) { mn = MathF.Min(mn, w.Elev[i]); mx = MathF.Max(mx, w.Elev[i]); }
        float sc = 1f / MathF.Max(1e-12f, mx - mn);
        for (int i = 0; i < n; i++) w.Elev[i] = (w.Elev[i] - mn) * sc;
        var sorted = (float[])w.Elev.Clone();
        Array.Sort(sorted);
        w.SeaLevel = sorted[(int)(Nature.Water * n)];
    }

    // rivers, lakes, ponds, soil and the first vegetation on the worked relief
    static void BuildWater(World w, float[] rain0, Func<float, float, float> moistN, Func<float, float, float> vegN)
    {
        int FW = Map.FW, n = FW * FW;
        // the sea, the lakes at their own levels, the drainage and the rivers, from the water balance (2026-10-31: before,
        // every cell below the Nature.Water quantile was a lake at that one level, and rivers were the cells over RIVER_ACC)
        var net = Hydrology.Waters(w, rain0);
        Hydrology.Floodplain(w, net);

        // continentality is distance from the sea and the lakes: a river damps no seasons (2026-10-31: rivers are now a
        // dense network, and counting them made every cell maritime); DistWater then measures to any water
        var stand = new byte[n];
        for (int i = 0; i < n; i++) stand[i] = (byte)(w.LakeOf[i] >= 0 ? 1 : 0);
        Distance(stand, 1, w.DistWater);
        Climate.Build(w);   // height, continentality and the temperature now (Revision O); draws no random numbers
        Distance(w.Cell, (byte)Realm.Water, w.DistWater);
        Distance(w.Cell, (byte)Realm.Land, w.DistLand);

        // Pond sites (Revision K, ecology review): land basins, the lowest point in their neighbourhood, away from
        // standing water; each holds rain in the wet season and dries out in the dry one (Ponds.Update). Revision Q: also
        // every cell of a closed basin the running water has left (the routing fills it to its rim: it is where rain gathers
        // and stands), as seasonal wetlands, pans and playas do. 2026-10-31: those are the hollows too small to hold a
        // lake and the dry floors of closed basins whose lake is smaller than the basin (net.Fill: the spill level).
        uint ps = Rng.Hash(w.Seed, 0, 0, Rs.Pond);
        for (int y = 0; y < FW; y++)
            for (int x = 0; x < FW; x++)
            {
                int i = y * FW + x;
                if (w.Cell[i] != (byte)Realm.Land || w.DistWater[i] < K.POND_GAP) continue;
                if (net.Fill[i] - w.Elev[i] >= K.BASIN_MIN && Rng.U(ps, 0, (uint)i, 0) < MathF.Min(1f, Nature.Ponds)) { w.PondSite[i] = 1; continue; }
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
                // parent material (Revision R): the soil weathers from the rock beneath it; FERT_VAR of local variation on top
                w.Fert[i] = Math.Clamp(Rock.Fert[w.Rock[i]] + K.FERT_VAR * 2f * (m - 0.5f), 0.1f, 1f);
                // alluvium (Revision Q): sediment laid down by running water makes young, rich soil
                if (w.Cell[i] == (byte)Realm.Land) w.Fert[i] = MathF.Min(1f, w.Fert[i] + K.ALLUV_FERT * (1f - MathK.Exp(-w.Alluvium[i] / K.ALLUV_D)));
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

        // Initial vegetation: a mosaic of grassland, shrubland and woodland, and plankton in the water. It is only a start:
        // Simulation grows the plants for PLANT_SPINUP years before step 0, and their cover becomes what their genes hold.
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
                P[(int)Pool.Browse][i] = shrub + 0.4f * tree;
                P[(int)Pool.Fruit][i] = 0.15f * tree;
                P[(int)Pool.Seed][i] = 0.10f * grass + 0.05f * tree;
                P[(int)Pool.Nectar][i] = 0.05f * (grass + shrub);
                P[(int)Pool.Root][i] = 0.4f * (grass + shrub);
                P[(int)Pool.Litter][i] = 0.15f;
                P[(int)Pool.Wood][i] = 0.3f * tree;
            }
        // the first plants' genes (Revision S): random founders, each cell taking the best growers for its site
        Plants.Found(w);
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
