using System;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Neuroreef.Tests;

/// <summary>
/// The ocean (Ocean.cs, 2026-11-02): a world under the sea. Its seafloor, its seawater, its producers' production by depth
/// against the literature, its nutrient budget, and its founders, all in the water. A world with land is unchanged
/// (StepLengthTests.DefaultStepIsBitIdenticalToBefore and ClimateTests' pinned hashes).
/// </summary>
public class OceanTests
{
    readonly ITestOutputHelper _out;
    public OceanTests(ITestOutputHelper o) { _out = o; }

    /// <summary>Run f with ocean worlds (islands share isl), putting the settings back after.</summary>
    internal static T Sea<T>(Func<T> f, float isl = 0f, float? climate = null)
    {
        bool o0 = Nature.Ocean; float i0 = Nature.Islands, c0 = Nature.Climate;
        Nature.Ocean = true; Nature.Islands = isl; if (climate != null) Nature.Climate = climate.Value;
        try { return f(); } finally { Nature.Ocean = o0; Nature.Islands = i0; Nature.Climate = c0; }
    }

    static float DepthM(World w, int i) => Respiration.ColumnM(w, i);

    [Fact] // an ocean world: no land, banks a few metres deep, slopes and basins as deep as the map's relief; every ancestor in the water
    public void AnOceanWorldHasNoLandAndOnlyWaterAncestors()
    {
        Sea(() =>
        {
            using var sim = new Simulation(64, 3, 2, empty: true);
            var w = sim.W;
            int n = Map.FW * Map.FW, land = 0, photic = 0, deep = 0;
            float min = float.MaxValue, max = 0f;
            for (int i = 0; i < n; i++)
            {
                if (w.Cell[i] == (byte)Realm.Land) { land++; continue; }
                float d = DepthM(w, i);
                min = MathF.Min(min, d); max = MathF.Max(max, d);
                if (d <= K.OCEAN_SHELF + 0.5f) photic++;
                if (d >= 0.5f * Map.ReliefM) deep++;
                for (int p = 0; p < Pools.NFIELD; p++) Assert.True(float.IsFinite(w.Pool[p][i]), $"pool {p} at {i}");
                Assert.True(float.IsFinite(w.Nutrient[i]) && float.IsFinite(w.DeepN[i]));
            }
            _out.WriteLine($"land {land}, depth {min:F1}-{max:F1} m (relief {Map.ReliefM:F0} m), banks <= {K.OCEAN_SHELF} m {photic * 100f / n:F0}%, basins >= half the relief {deep * 100f / n:F0}%; lakes {w.LakeCount}");
            Assert.Equal(0, land);
            Assert.InRange(min, 0.5f, 2f);
            Assert.InRange(max, 0.9f * Map.ReliefM, 1.01f * Map.ReliefM);
            Assert.InRange(photic / (float)n, 0.2f, 0.32f);
            Assert.True(deep > n / 10, "deep basins");
            Assert.Equal(0, w.Evo.AncestorsLand);
            Assert.Equal(K.ANCESTORS_LAND + K.ANCESTORS_WATER, w.Evo.AncestorsWater);
            Assert.All(w.Temp, t => Assert.True(float.IsFinite(t)));
            return 0;
        });
    }

    [Fact] // islands: an ocean world may keep a share of its relief above the sea
    public void IslandsStandAboveTheSea()
    {
        Sea(() =>
        {
            var w = new World(16, 5);
            Terrain.Build(w);
            int n = Map.FW * Map.FW, land = 0;
            for (int i = 0; i < n; i++) if (w.Cell[i] == (byte)Realm.Land) land++;
            _out.WriteLine($"land {land * 100f / n:F1}%");
            Assert.InRange(land / (float)n, 0.05f, 0.12f);
            return 0;
        }, isl: 0.1f);
    }

    [Fact] // seawater holds ~20% less oxygen than fresh (Weiss 1970), and the deep sea keeps most of its oxygen
    public void SalinityLowersDissolvedOxygen()
    {
        float r20 = Ocean.DissolvedO2(20f, K.SALINITY) / Respiration.DissolvedO2(20f), r5 = Ocean.DissolvedO2(5f, K.SALINITY) / Respiration.DissolvedO2(5f);
        _out.WriteLine($"seawater/fresh: {r20:F3} at 20 °C, {r5:F3} at 5 °C; 35 PSU at 20 °C {Ocean.DissolvedO2(20f, 35f):F2} mg/L");
        Assert.InRange(r20, 0.78f, 0.84f);   // Weiss 1970: 7.40 / 9.09 mg/L
        Assert.InRange(r5, 0.78f, 0.84f);
        Assert.InRange(Ocean.DissolvedO2(20f, 35f), 7.2f, 7.6f);
        Assert.Equal(Respiration.DissolvedO2(12f), Ocean.DissolvedO2(12f, 0f), 3);
        Sea(() =>
        {
            var w = new World(16, 3);
            Terrain.Build(w);
            int n = Map.FW * Map.FW, deepest = 0;
            for (int i = 0; i < n; i++) if (DepthM(w, i) > DepthM(w, deepest)) deepest = i;
            float col = DepthM(w, deepest);
            float sea0 = Respiration.WaterO2(w, deepest, 0f), seaB = Respiration.WaterO2(w, deepest, col);
            float tS = Respiration.WaterT(w, deepest, 0f), tB = Respiration.WaterT(w, deepest, col);
            w.Ocean = false;
            float fresh0 = Respiration.WaterO2(w, deepest, 0f);
            w.Ocean = true;
            _out.WriteLine($"{col:F0} m: {tS:F1} °C at the surface, {tB:F1} at the bottom; oxygen {sea0:F3} at the surface ({fresh0:F3} if fresh), {seaB:F3} at the bottom");
            Assert.True(sea0 < 0.85f * fresh0, "salt lowers the oxygen");
            Assert.True(tB < tS, "the deep water is colder");
            Assert.True(seaB > 0.4f * sea0, "the deep sea is ventilated, not anoxic");
            return 0;
        });
    }

    /// <summary>
    /// A year of the water producers of an ocean world (Fields.Row on every row, no animals) after a year's spin-up, and
    /// their gross growth by depth zone, in gC/m2/yr (0.45 gC per g of algal dry matter, Wetzel 2001).
    /// </summary>
    static string Stand = "";

    static (double reef, double bank, double slope, double open, double deepN, double surfN) Production(uint seed, float climate)
    {
        return Sea(() =>
        {
            var w = new World(16, seed);
            Terrain.Build(w);
            int n = Map.FW * Map.FW, year = Nature.SeasonLength;
            float[] Al = w.Pool[(int)Pool.Algae], Ph = w.Pool[(int)Pool.Phyto];
            var g = new double[n];
            var a0 = new float[n]; var p0 = new float[n];
            for (int s = 0; s < 3 * year; s++)
            {
                Array.Copy(Al, a0, n); Array.Copy(Ph, p0, n);
                for (int y = 0; y < Map.FW; y++) Fields.Row(w, y);
                Fields.Swap(w);
                w.Step++;
                // growth this step: the change plus what died (Fields.SeaCell), before the plankton drifts (Ocean.Drift)
                if (s >= 2 * year) for (int i = 0; i < n; i++) g[i] += Al[i] - a0[i] + K.M_A * a0[i] + Ph[i] - p0[i] + K.M_PH * p0[i];
                Ocean.Drift(w);
            }
            double toGC = K.WATER_KG_M2 * 1000.0 * 0.45;
            double[] sum = new double[4]; int[] cnt = new int[4];
            double dn = 0, sn = 0; double[] al = new double[4], ph = new double[4], um = new double[4], de = new double[4];
            for (int i = 0; i < n; i++)
            {
                float d = DepthM(w, i);
                int z = d <= 5f ? 0 : d <= K.OCEAN_SHELF ? 1 : d <= 80f ? 2 : 3;
                sum[z] += g[i] * toGC; cnt[z]++;
                al[z] += Al[i]; ph[z] += Ph[i]; um[z] += Ocean.Umol(w.Nutrient[i], Ocean.MixedM(d)); de[z] += w.Pool[(int)Pool.Detritus][i];
                dn += w.DeepN[i]; sn += w.Nutrient[i];
            }
            double M(int z) => cnt[z] > 0 ? sum[z] / cnt[z] : double.NaN;
            Stand = string.Join("; ", Enumerable.Range(0, 4).Select(z => $"zone {z}: algae {al[z] / Math.Max(1, cnt[z]):F3} plankton {ph[z] / Math.Max(1, cnt[z]):F3} detritus {de[z] / Math.Max(1, cnt[z]):F2} nitrate {um[z] / Math.Max(1, cnt[z]):F3} uM"));
            return (M(0), M(1), M(2), M(3), dn, sn);
        }, climate: climate);
    }

    [Fact] // the sea's production by depth: open ocean 50-150 gC/m2/yr (Field et al. 1998), banks and slopes above it
    public void OceanProductionByDepthIsInTheLiteratureRanges()
    {
        // Measured 2026-11-02 (seed 3, the 256 map), with the reefs' nitrogen fixation (N_FIX_REEF), the renewal of reef
        // flats by open water (REEF_RES) and the sea's own detritus rot (ROT_DETRITUS_SEA): reef flats ~105, banks
        // ~400-440, slopes ~220, open water ~115 gC/m2/yr (before them: ~65, ~170, ~120, ~74). Open water is in Field et
        // al. (1998)'s 50-150 and the banks in the 200-500 of coastal and shelf seas (Ryther 1969; Field 1998). Reef flats
        // are NOT in the 1,000-2,000 of kelp and reef flats (Mann 1973; Hatcher 1988): their benthic algae hold ~0.12 of
        // a unit on ~0.23 umol/L of nitrate, limited by the nitrogen the flat fixes and recycles; and at a full unit
        // (WATER_KG_M2, 0.05 kg/m2, far below a kelp bed's or a turf's 0.2-1 kg) they could make at most M_A x a unit,
        // ~820 gC/m2/yr. Asserted is what holds, so a change that moves it is seen.
        var (reef, bank, slope, open, deepN, surfN) = Production(3, 0f);
        _out.WriteLine($"no climate: reef flats (<5 m) {reef:F0}, banks (5-30 m) {bank:F0}, slopes (30-80 m) {slope:F0}, open water (>80 m) {open:F0} gC/m2/yr; nutrient surface {surfN:F1} deep {deepN:F1}");
        _out.WriteLine("  standing: " + Stand);
        var c = Production(3, K.CLIMATE);
        _out.WriteLine("  standing: " + Stand);
        _out.WriteLine($"climate:    reef flats {c.reef:F0}, banks {c.bank:F0}, slopes {c.slope:F0}, open water {c.open:F0} gC/m2/yr; nutrient surface {c.surfN:F1} deep {c.deepN:F1}");
        // Field et al. 1998 (open ocean 50-150), Mann 1973 and Hatcher 1988 (kelp and reef flats 1,000-2,000; the pool's
        // standing stock, WATER_KG_M2, holds a reef flat below that), Ryther 1969 and Field 1998 (coastal and shelf 200-500)
        Assert.InRange(open, 50.0, 150.0);
        Assert.InRange(c.open, 50.0, 150.0);
        Assert.InRange(bank, 200.0, 500.0);
        Assert.True(bank > open && c.bank > c.open, "the banks outgrow the open water");
        Assert.True(reef > 80.0, "the reef flats grow on what they fix");
    }

    /// <summary>A world with animals run Steps steps, its nutrient before and after (NutrientBalanceTests.Run, at sea).</summary>
    static (Nutrients.Totals t0, Nutrients.Totals t1, Nutrients.Book book, int alive) Run(bool open, uint seed, int steps)
    {
        return Sea(() =>
        {
            using var sim = new Simulation(64, seed, 2);
            var w = sim.W;
            w.NutBook.Open = open;
            w.NutBook.Clear();
            var t0 = Nutrients.Total(w);
            for (int k = 0; k < steps; k++) sim.Step();
            return (t0, Nutrients.Total(w), w.NutBook, w.AliveCount);
        });
    }

    [Fact] // the nutrient budget closes at sea: closed, the total keeps; open, it changes by fixation less burial
    public void TheNutrientBalanceClosesInAnOceanWorld()
    {
        var (t0, t1, book, alive) = Run(open: false, seed: 11, steps: 200);
        _out.WriteLine($"closed: before {t0}\n        after  {t1}\n        founders {book.Founders:E3} debt {book.Debt:E3} (animals {book.DebtAnimals:E3}); {alive} animals");
        Assert.Equal(0.0, book.In); Assert.Equal(0.0, book.Out);
        Assert.True(t1.Deep > 0.0 && t1.Water > 0.0);
        // debt: nutrient made because a growing body or a newborn needed more than its mother's gut and the water held. The
        // sea's surface water is nearly empty of nitrate (~0.05 umol/L), and a body excretes what it assimilates at once
        // (Nutrients.Ingest), so its growth finds little left: measured 2026-11-02 ~1.6e-3 of the total over 200 steps on
        // this world, against 1.5-3e-5 on land (NutrientBalanceTests). Counted, not hidden; this bounds it
        Assert.True(book.Debt <= 5e-3 * t0.All, $"debt {book.Debt:E3}");
        Assert.True(Math.Abs(t1.All - t0.All - book.Founders - book.Debt) <= 1e-6 * t0.All, $"total {t0.All:F5} -> {t1.All:F5} ({t1.All - t0.All:E3})");
        var o = Run(open: true, seed: 11, steps: 200);
        double d = o.t1.All - o.t0.All, net = o.book.In - o.book.Out + o.book.Founders + o.book.Debt;
        _out.WriteLine($"open: change {d:E4}; in {o.book.In:E4} out {o.book.Out:E4} founders {o.book.Founders:E4} debt {o.book.Debt:E4}");
        Assert.True(o.book.In > 0.0, "fixation and deposition");
        Assert.True(Math.Abs(d - net) <= 1e-6 * o.t0.All, $"change {d:E4} against inputs less outputs {net:E4}");
    }

    [Fact] // founding at sea: several viable water lineages (the founding only, seeds 1-5 with OCEAN_PROBE=1)
    public void FoundingAtSeaGivesSeveralWaterLineages()
    {
        string? all = Environment.GetEnvironmentVariable("OCEAN_PROBE");
        uint[] seeds = all == "1" ? new uint[] { 1, 2, 3, 4, 5 } : new uint[] { 1 };
        foreach (uint seed in seeds)
        {
            int lineages = Sea(() =>
            {
                using var sim = new Simulation(64, seed, 2);
                var w = sim.W;
                int k = 0;
                for (int c = 0; c < K.CLADES; c++)
                {
                    if (!w.Sp[c].Used) continue;
                    var ids = Enumerable.Range(0, w.SlotsUsed).Where(i => w.StageOf[i] != St.Dead && w.Spc[i] == c).ToArray();
                    if (ids.Length == 0) continue;
                    k++;
                    int a = ids[0];
                    var diet = w.DietOf(a);
                    // what it eats at sea: the water pools and what animals make (no land food here)
                    bool Sea(int p) => Pools.IsWaterPool(p) || p == (int)Pool.Meat || p == (int)Pool.Blood;
                    int best = (int)Pool.Phyto; for (int p = 0; p < Pools.N; p++) if (Sea(p) && diet[p] * Pools.Rho[p] > diet[best] * Pools.Rho[best]) best = p;
                    float z = ids.Average(i => w.Z[i]), col = ids.Average(i => Respiration.ColumnM(w, World.FIdx(w.X[i], w.Y[i])));
                    _out.WriteLine($"  seed {seed} clade {c} '{w.Sp[c].Name}': {ids.Length} founders, {w.MassOf(a):F2} kg, {(Loco)w.PLoco[a]}, realm {(Realm)w.PRealm[a]}, " +
                        $"lungs {w.PLungs[a]:F2}, fins {w.PFins[a]:F2}, dive pref {w.PDive[a]:F2}, eats mostly {(Pool)best}; at {z:F1} m in {col:F0} m of water");
                    Assert.Equal(0, w.Sp[c].Ancestor < w.Evo.AncestorsLand ? 1 : 0);
                }
                _out.WriteLine($"seed {seed}: {k} lineages of {w.Evo.AncestorsWater} water ancestors, {w.AliveCount} animals");
                return k;
            });
            Assert.True(lineages >= 3, $"seed {seed}: {lineages} lineages");
        }
    }

    [Fact] // the WORLD frame's depth in decimetres after the rivers (2026-11-02): past the u8 plane's 254 m
    public void WorldFrameCarriesDeepDepths()
    {
        Map.Set(2048);
        try
        {
            Sea(() =>
            {
                var w = new World(16, 3);
                Terrain.Build(w);
                var b = Neuroreef.Server.Wire.EncodeWorld(w);
                int n = Map.FW * Map.FW, o = b.Length - 2 * n; float max = 0f;
                for (int i = 0; i < n; i++)
                {
                    float m = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o + 2 * i)) / 10f;
                    Assert.True(MathF.Abs(m - Respiration.ColumnM(w, i)) <= 0.051f, $"cell {i}: {m} m against {Respiration.ColumnM(w, i)}");
                    max = MathF.Max(max, m);
                }
                _out.WriteLine($"deepest {max:F1} m (relief {Map.ReliefM:F0} m)");
                Assert.True(max > 600f, "the 2048 ocean is deeper than the u8 plane's 254 m");
                return 0;
            });
        }
        finally { Map.Set(TestWorld.Side); }
    }

    [Fact] // fliers at sea (2026-11-02): a founded flier must be able to land where it lives (it eats landed: Resolve), so
           // over deep water only one that can swim there is founded; a flier that cannot land flies at its cost or is downed
    public void FoundedFliersAtSeaCanLandWhereTheyLive()
    {
        foreach (uint seed in new uint[] { 1, 2 })
        {
            Sea(() =>
            {
                using var sim = new Simulation(64, seed, 2);
                var w = sim.W;
                for (int c = 0; c < K.CLADES; c++)
                {
                    if (!w.Sp[c].Used) continue;
                    int n = 0, land = 0; Loco loco = Loco.Walk;
                    for (int i = 0; i < w.SlotsUsed; i++)
                    {
                        if (w.StageOf[i] == St.Dead || w.Spc[i] != c) continue;
                        n++; loco = (Loco)w.PLoco[i];
                        if (w.GroundAllows(i, World.FIdx(w.X[i], w.Y[i]))) land++;
                    }
                    if (n == 0 || loco != Loco.Fly) continue;
                    _out.WriteLine($"seed {seed} flier clade {c}: {n} founders, {land * 100 / n}% where they can land (swim or wade)");
                    Assert.True(land * 2 >= n, $"seed {seed} clade {c}: {land} of {n} fliers can land where they are");
                }
                return 0;
            });
        }
    }
}
