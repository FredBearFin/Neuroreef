// Evolving plants (Revision S, Plants.cs): genome and phenotype, growth reaction norms, the schedule, seed mass,
// the animal interface, the table and the checkpoint.

using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Neuroreef.Tests;

public class PlantTests
{
    /// <summary>A genotype key with every trait at level mid, then the given (trait, level) pairs.</summary>
    static (ulong lo, uint hi) Key(int mid, params (int t, int lv)[] set)
    {
        var lv = Enumerable.Repeat(mid, PT.N).ToArray();
        foreach (var (t, l) in set) lv[t] = l;
        ulong lo = 0; uint hi = 0;
        for (int k = 0; k < 16; k++) lo |= (ulong)lv[k] << (4 * k);
        for (int k = 0; k < PT.N - 16; k++) hi |= (uint)lv[16 + k] << (4 * k);
        return (lo, hi);
    }

    static float[] Express(params (int t, float x)[] set)
    {
        var x = Enumerable.Repeat(0.5f, PT.N).ToArray();
        foreach (var (t, v) in set) x[t] = v;
        var ph = new float[Ph.NP];
        Plants.Express(x, ph);
        return ph;
    }

    static float XOfHeight(float h) => (MathF.Log10(h) - K.H_LO) / K.H_SPAN;

    static World Built(uint seed = 1)
    {
        var w = new World(64, seed);
        Terrain.Build(w);
        return w;
    }

    static int Insert(World w, (ulong lo, uint hi) k) => Plants.Insert(w, k.lo, k.hi, 0, 0);

    [Fact]
    public void KeyRoundTripsThroughTheTable()
    {
        var w = Built();
        var k = Key(7, (PT.HEIGHT, 3), (PT.TOXIN, 15), (PT.SELF, 2), (PT.NFIX, 11));
        int id = Insert(w, k);
        Assert.True(id > 0);
        Assert.Equal(3, w.Flora.Level(id, PT.HEIGHT)); Assert.Equal(15, w.Flora.Level(id, PT.TOXIN));
        Assert.Equal(2, w.Flora.Level(id, PT.SELF)); Assert.Equal(11, w.Flora.Level(id, PT.NFIX)); Assert.Equal(7, w.Flora.Level(id, PT.WUE));
        Assert.Equal(id, Plants.Find(w.Flora, k.lo, k.hi));
        Assert.Equal(id, Insert(w, k));   // the same genotype is one entry
        Assert.Equal(0, Plants.Distance(w.Flora, id, id));
        int other = Insert(w, Key(7, (PT.HEIGHT, 5), (PT.TOXIN, 15), (PT.SELF, 2), (PT.NFIX, 11)));
        Assert.Equal(2, Plants.Distance(w.Flora, id, other));
    }

    [Fact]
    public void RecruitsInheritEachTraitFromAParentAndMutateRarely()
    {
        var w = Built();
        var t = w.Flora;
        int a = Insert(w, Key(2)), b = Insert(w, Key(12));
        int mutated = 0, n = 4000; bool sawA = false, sawB = false;
        var cross = typeof(Plants).GetMethod("Cross", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        for (int k = 0; k < n; k++)
        {
            var args = new object?[] { t, a, b, Rng.Hash(1, (uint)k, 0, 1), Rng.Hash(1, (uint)k, 0, 2), null, null, null };
            cross.Invoke(null, args);
            ulong lo = (ulong)args[5]!; uint hi = (uint)args[6]!; bool mut = (bool)args[7]!;
            int odd = 0;
            for (int q = 0; q < PT.N; q++)
            {
                int lv = q < 16 ? (int)((lo >> (4 * q)) & 15) : (int)((hi >> (4 * (q - 16))) & 15);
                if (lv == 2) sawA = true; else if (lv == 12) sawB = true; else { odd++; Assert.True(lv == 1 || lv == 3 || lv == 11 || lv == 13, $"level {lv}"); }
            }
            Assert.True(odd <= (mut ? 1 : 0));
            if (mut) mutated++;
        }
        Assert.True(sawA && sawB);
        Assert.InRange(mutated / (double)n, K.PLANT_MU * 0.7, K.PLANT_MU * 1.3);
    }

    [Fact] // the mid genome reproduces the old producers: grass at 0.3 m, shrubs at 1.3 m, trees at 15 m (R_G, R_S, R_T before Revision S)
    public void MidGenomeGrowsAtTheOldRates()
    {
        foreach (var (h, rate) in new[] { (0.3f, 0.010f), (1.3f, 0.003f), (15f, 0.0004f) })
        {
            var ph = Express((PT.HEIGHT, XOfHeight(h)));
            float net = Plants.Net(ph, 0, 1f, 1e6f, K.T_REF, 1f);
            Assert.InRange(net / rate, 0.9f, 1.1f);
        }
    }

    [Fact] // 2026-11-01: water-limited production follows the supply/demand ratio and colimits near the lesser limit, so
           // soil at 0.2 of capacity is much less productive than at 0.4 (desert NPP was 2x the measured)
    public void DrySoilLimitsProduction()
    {
        var mid = Express((PT.HEIGHT, XOfHeight(0.3f)));
        float n2 = Plants.Net(mid, 0, 0.2f, 0.8f, K.T_REF, 1f), n4 = Plants.Net(mid, 0, 0.4f, 0.8f, K.T_REF, 1f), n9 = Plants.Net(mid, 0, 0.9f, 0.8f, K.T_REF, 1f);
        Assert.True(n2 < 0.6f * n4, $"net at 0.2 {n2}, at 0.4 {n4}");
        Assert.True(n4 < n9);
        // the colimitation is at most the lesser limit and close to it when they differ
        Assert.True(Plants.Colim(1f, 0.2f) <= 0.2f && Plants.Colim(1f, 0.2f) > 0.18f);
        Assert.Equal(0f, Plants.Colim(0f, 1f));
    }

    [Fact] // 2026-11-01: a closed canopy has no room to widen its crown, yet it fixes carbon: a tree puts it into live wood;
           // a herb sward's production is bounded by what its leaves can fix (REPRO_RATE per unit cover, Beer's law)
    public void ClosedCanopyGrowsWoodAndProductionIsBounded()
    {
        var w = Built();
        int c = LandWithHerb(w), n = Map.FW * Map.FW, y = c / Map.FW;
        int tree = Insert(w, Key(8, (PT.HEIGHT, 13), (PT.DECID, 0), (PT.TOPT, 6)));
        for (int i = y * Map.FW; i < (y + 1) * Map.FW; i++) { w.Moisture[i] = 0.8f; w.Nutrient[i] = 1f; w.Temp[i] = K.T_REF; w.WetDur[i] = 0f; }
        w.POcc[c * 4] = 0; w.Pool[(int)Pool.Grass][c] = 0f; w.POcc[c * 4 + 1] = 0; w.Shrub[c] = 0f; w.POcc[c * 4 + 3] = 0; w.Sapling[c] = 0f;
        w.POcc[c * 4 + 2] = (ushort)tree; w.Tree[c] = 1f; w.PFlags[c] = 0x0F; w.LiveWood[c] = 0f;
        float p0 = w.Flora.CellProd[c];
        Plants.UpdateRow(w, y);
        float prod = w.Flora.CellProd[c] - p0, dt = Plants.Every;
        Assert.True(w.LiveWood[c] > 0f, "no wood in a closed canopy");
        // the leaf turnover alone is CLIT + LIFE of the cover; with wood the canopy makes several times that
        float turnover = dt * (w.Flora.P(tree, Ph.CLIT) + w.Flora.P(tree, Ph.LIFE));
        Assert.True(prod > 2f * turnover, $"production {prod}, leaf turnover {turnover}");
        Assert.True(prod <= 1.2f * dt * K.REPRO_RATE * 1.2f, $"production {prod} above what a closed canopy fixes");
        // a sward of tiny, cheap-leaved herbs turns over fast but cannot produce more than its leaves fix
        int herb = Insert(w, Key(8, (PT.HEIGHT, 0), (PT.LMA, 0), (PT.DECID, 0), (PT.TOPT, 6)));
        int d = c + 1 < n && w.Cell[c + 1] == (byte)Realm.Land && (c + 1) / Map.FW == y ? c + 1 : c - 1;
        w.POcc[d * 4] = (ushort)herb; w.Pool[(int)Pool.Grass][d] = 0.8f; w.Shrub[d] = 0f; w.Tree[d] = 0f; w.Sapling[d] = 0f; w.PFlags[d] = 0x0F;
        for (int k = 1; k < 4; k++) w.POcc[d * 4 + k] = 0;
        float q0 = w.Flora.CellProd[d];
        Plants.UpdateRow(w, y);
        float hp = w.Flora.CellProd[d] - q0;
        Assert.True(hp <= dt * K.REPRO_RATE * Plants.Intercept(0.8f) * 1.6f, $"herb sward production {hp} per update, canopy bound {dt * K.REPRO_RATE * Plants.Intercept(0.8f)}");
    }

    [Fact] // 2026-11-01 (round 2): growth stops below ~5 °C at the meristems; short plants run warmer than the air, a tall
           // tree at air temperature, so in the cold trees stop growing first (the physical treeline, Körner & Paulsen 2004)
    public void ColdStopsTreesBeforeHerbs()
    {
        float tall = 15f, herb = 0.1f;
        Assert.Equal(0f, Plants.ColdGrowth(K.T_GROW_MIN - 0.5f, tall));
        Assert.True(Plants.ColdGrowth(K.T_GROW_MIN - 0.5f, herb) > 0.4f, "a short herb near the ground still grows");
        Assert.Equal(1f, Plants.ColdGrowth(K.T_GROW_MIN + K.T_GROW_RAMP + 0.1f, tall));
        Assert.True(Plants.ColdGrowth(7f, tall) < Plants.ColdGrowth(9f, tall), "growth rises with warmth above the threshold");
        // warmth also raises transpiration demand (vapour pressure deficit): the same soil water buys less growth when hot
        var mid = Express((PT.HEIGHT, XOfHeight(0.3f)));
        Assert.True(Plants.Net(mid, 0, 0.25f, 0.8f, 28f, 1f) < Plants.Net(mid, 0, 0.25f, 0.8f, K.T_REF, 1f));
    }

    [Fact] // 2026-11-01 (round 2): stems live as long as mature forests' mortality says, 1-2% a year (Stephenson & van Mantgem 2005)
    public void TreeLifeMatchesForestMortality()
    {
        float Years(float h) => 1f / (Express((PT.HEIGHT, XOfHeight(h)))[Ph.LIFE] * K.T_SEASON);
        Assert.InRange(Years(15f), 40f, 70f);
        Assert.InRange(Years(30f), 80f, 130f);
        Assert.InRange(Years(0.3f), 0.5f, 1.5f);   // the reference herb: a year
    }

    [Fact] // 2026-11-01: a new world (and a save from before the wood stock) starts its live wood from the woody cover
    public void LiveWoodStartsFromWoodyCover()
    {
        var w = Built();
        int withTrees = 0;
        for (int i = 0; i < Map.FW * Map.FW; i++)
        {
            if (w.Cell[i] != (byte)Realm.Land) { Assert.Equal(0f, w.LiveWood[i]); continue; }
            Assert.Equal(Plants.WoodFromCover(w, i), w.LiveWood[i], 4);
            if (w.Tree[i] > 0.3f && w.POcc[i * 4 + 2] != 0) { withTrees++; Assert.True(w.LiveWood[i] * K.PLANT_KG_M2 > 1f, $"wood {w.LiveWood[i] * K.PLANT_KG_M2} kg/m2 under tree cover {w.Tree[i]}"); }
        }
        Assert.True(withTrees > 0);
    }

    [Fact] // crossing reaction norms: each strategy wins somewhere and pays elsewhere
    public void TraitsTradeOffAcrossConditions()
    {
        float herb = XOfHeight(0.3f);
        float Rel(float[] a, float[] b, float W, float T, float L) => Plants.Net(a, 0, W, 0.6f, T, L) - Plants.Net(b, 0, W, 0.6f, T, L);
        // water-use efficiency: better in dry soil, worse in wet
        var wue = Express((PT.HEIGHT, herb), (PT.WUE, 1f)); var spend = Express((PT.HEIGHT, herb), (PT.WUE, 0f));
        Assert.True(Rel(wue, spend, 0.25f, K.T_REF, 1f) > 0f, "efficient plant not better in dry soil");
        Assert.True(Rel(wue, spend, 1f, K.T_REF, 1f) < 0f, "efficient plant not worse in wet soil");
        // temperature optimum: the cold-adapted grow better in the cold, the warm-adapted in the heat
        var cold = Express((PT.HEIGHT, herb), (PT.TOPT, 0.1f)); var hot = Express((PT.HEIGHT, herb), (PT.TOPT, 0.9f));
        Assert.True(Rel(cold, hot, 0.8f, 2f, 1f) > 0f); Assert.True(Rel(cold, hot, 0.8f, 30f, 1f) < 0f);
        // leaf economics: tough leaves lose less of their advantage in the shade (net of leaf turnover)
        var tough = Express((PT.HEIGHT, herb), (PT.LMA, 1f)); var cheap = Express((PT.HEIGHT, herb), (PT.LMA, 0f));
        float Leaf(float[] p, float L) => Plants.Net(p, 0, 0.8f, 0.6f, K.T_REF, L) - p[Ph.CLIT] - p[Ph.LIFE];
        Assert.True(Leaf(tough, 0.1f) - Leaf(cheap, 0.1f) > Leaf(tough, 1f) - Leaf(cheap, 1f), "shade does not favour tough leaves");
        Assert.True(Leaf(cheap, 1f) > Leaf(tough, 1f), "cheap leaves do not win in full light");
        // defences cost growth
        foreach (int tr in new[] { PT.TOXIN, PT.THORN, PT.RESPROUT, PT.NFIX, PT.FLOOD })
            Assert.True(Plants.Net(Express((PT.HEIGHT, herb), (tr, 1f)), 0, 0.8f, 0.6f, K.T_REF, 1f) < Plants.Net(Express((PT.HEIGHT, herb), (tr, 0f)), 0, 0.8f, 0.6f, K.T_REF, 1f), PT.Names[tr]);
        // a fixer gains where nutrient is short
        var fix = Express((PT.HEIGHT, herb), (PT.NFIX, 1f)); var nof = Express((PT.HEIGHT, herb), (PT.NFIX, 0f));
        Assert.True(Plants.Net(fix, 0, 0.8f, 0.02f, 25f, 1f) > Plants.Net(nof, 0, 0.8f, 0.02f, 25f, 1f), "fixer not better on poor soil");
    }

    static ulong PlantHash(World w)
    {
        ulong h = 14695981039346656037UL;
        void Mix(uint v) { h ^= v; h *= 1099511628211UL; }
        int n = Map.FW * Map.FW;
        for (int i = 0; i < n; i++)
        {
            Mix(BitConverter.SingleToUInt32Bits(w.Pool[(int)Pool.Grass][i])); Mix(BitConverter.SingleToUInt32Bits(w.Tree[i]));
            Mix(BitConverter.SingleToUInt32Bits(w.Shrub[i])); Mix(BitConverter.SingleToUInt32Bits(w.Pool[(int)Pool.Seed][i]));
            for (int k = 0; k < 4; k++) Mix(w.POcc[i * 4 + k]);
            for (int s = 0; s < 3; s++) { Mix(w.BankMom[s * n + i]); Mix(w.BankDad[s * n + i]); }
        }
        for (int id = 0; id < w.Flora.Cap; id += 97) { Mix((uint)w.Flora.KeyLo[id]); Mix(w.Flora.Used[id]); }
        Mix((uint)w.Flora.Live);
        return h;
    }

    [Fact] // NR-105 for plants: stripes in parallel and the serial commit give the same world on any thread count
    public void PlantsAreIndependentOfThreadCount()
    {
        ulong Run(int threads)
        {
            using var sim = new Simulation(1024, 11, threads);
            for (int k = 0; k < 40; k++) sim.Step();
            return PlantHash(sim.W);
        }
        ulong a = Run(1);
        Assert.Equal(a, Run(3));
    }

    [Fact] // every row is updated once in each epoch of Every steps
    public void StripesVisitEveryRowOncePerEpoch()
    {
        int E = Plants.Every, FW = Map.FW;
        Assert.Equal(0, FW % E);
        var seen = new int[FW];
        for (int s = 0; s < E; s++) for (int k = 0; k < Plants.StripeRows; k++) seen[Plants.StripeY((ulong)(100 * E + s), k)]++;
        Assert.All(seen, c => Assert.Equal(1, c));
    }

    [Fact] // seed blown out of a cell (to its neighbours and far) all lands somewhere: released = gathered + in flight
    public void WindDispersalConservesSeedMass()
    {
        using var sim = new Simulation(1024, 5, 2, empty: true);
        var w = sim.W; var t = w.Flora;
        int E = Plants.Every;
        for (int k = 0; k < 6 * E; k++) sim.Step();
        Assert.Equal(0UL, w.Step % (ulong)E);
        // seed released to the neighbours this epoch is still in flight towards a neighbouring row that was updated earlier
        // in the epoch (it takes it next epoch): the row above, unless this row came first; the row below for the last
        // row of the epoch. The row's own neighbours took theirs at once
        double inFlight = 0; int n = Map.FW * Map.FW;
        for (int j = 0; j < w.SeedOut.Length; j++)
        {
            int o = (j % n) / Map.FW % E;
            inFlight += w.SeedOut[j] * ((o >= 1 ? 3.0 : 0.0) + (o == E - 1 ? 3.0 : 0.0));
        }
        foreach (var v in w.WindAcc) inFlight += v;
        foreach (var v in w.RowFar) inFlight += v;
        Assert.True(t.ReleasedNb > 1.0, $"released {t.ReleasedNb}");
        Assert.True(Math.Abs((t.ReleasedNb - t.GatheredNb - inFlight) / t.ReleasedNb) < 1e-4, $"released {t.ReleasedNb}, gathered {t.GatheredNb}, in flight {inFlight}");
    }

    static int LandWithHerb(World w)
    {
        for (int i = 0; i < Map.FW * Map.FW; i++) if (w.Cell[i] == (byte)Realm.Land && w.POcc[i * 4] != 0 && w.Pool[(int)Pool.Grass][i] > 0.05f) return i;
        throw new InvalidOperationException("no herb cell");
    }

    [Fact] // toxins cut the energy of a bite, thorns its size; a mid genome changes neither
    public void ToxinsAndThornsDefend()
    {
        var w = Built();
        int c = LandWithHerb(w);
        float h = Plants.HeightOf(0.2f); int hl = 3;
        w.POcc[c * 4] = (ushort)Insert(w, Key(8, (PT.HEIGHT, hl), (PT.LMA, 8), (PT.TOXIN, 15), (PT.THORN, 15)));
        Plants.Taste(w, (int)Pool.Grass, c, K.C_BITE, out float eT, out float bT);
        Assert.True(eT < 0.6f, $"energy x{eT}"); Assert.True(bT < 0.9f, $"bite x{bT}");
        w.POcc[c * 4] = (ushort)Insert(w, Key(8, (PT.HEIGHT, hl), (PT.LMA, 8), (PT.TOXIN, 0), (PT.THORN, 0)));
        Plants.Taste(w, (int)Pool.Grass, c, K.C_BITE, out float e0, out float b0);
        Assert.True(e0 > 1.2f && b0 > 1f);
        Plants.Taste(w, (int)Pool.Carrion, c, K.C_BITE, out float ec, out float bc);
        Assert.Equal(1f, ec); Assert.Equal(1f, bc);
        Assert.True(h > 0f);
    }

    [Fact] // the animal tastes the plants it is about to bite (FOOD_HERE), not those two cells away (FOOD_GRAD)
    public void TasteIsAtTheMouth()
    {
        var w = Built();
        int c = LandWithHerb(w);
        w.POcc[c * 4] = (ushort)Insert(w, Key(8, (PT.HEIGHT, 3), (PT.TOXIN, 15)));
        var diet = new float[Pools.N]; diet[(int)Pool.Grass] = 0.5f;
        float plain = Decide.FoodValue(w, diet, 0.05f, 0.01f, c), tasted = Decide.FoodValue(w, diet, 0.05f, 0.01f, c, taste: true);
        Assert.True(tasted < plain, $"tasted {tasted}, untasted {plain}");
    }

    static void Animal(World w, int a, float mass)
    {
        w.StageOf[a] = St.Adult; w.PM[a] = mass; Plants.ClearAnimal(w, a); w.GutSeedy[a] = 0f;
    }

    [Fact] // seed swallowed with fruit keeps its parents and lands in dung; a small gape passes less of a big seed
    public void GutSeedCarriesItsGenotypeAndGapeLimits()
    {
        var w = Built();
        int c = LandWithHerb(w);
        int g = Insert(w, Key(8, (PT.HEIGHT, 13), (PT.FRUIT, 15), (PT.SEED, 15)));
        w.FruitG[c] = (ushort)g; w.Pool[(int)Pool.Fruit][c] = 1f;
        Animal(w, 0, 2f); Animal(w, 1, 0.01f);
        Plants.Eaten(w, 0, c, (int)Pool.Fruit, 0.1f, 0.5f);
        Plants.Eaten(w, 1, c, (int)Pool.Fruit, 0.1f, 0.5f);
        Assert.Equal(g, (int)w.GutSeedG[0]);
        Assert.True(w.GutSeedy[0] > 0f && w.GutSeedy[1] < 0.5f * w.GutSeedy[0], $"big {w.GutSeedy[0]}, small {w.GutSeedy[1]}");
        int n = Map.FW * Map.FW, d = LandWithHerb(w) + 1; while (w.Cell[d] != (byte)Realm.Land) d++;
        float bank0 = w.BankM[2 * n + d];
        Plants.Deposit(w, d, w.GutSeedG[0], w.GutSeedDad[0], w.GutSeedy[0], 0);
        // the gut holds kg, the banks cover units: a unit is Pools.Kg kg in a cell (2026-11-01)
        Assert.Equal(bank0 + w.GutSeedy[0] / Pools.Kg[(int)Pool.Seed], w.BankM[2 * n + d], 5);
        Assert.True(w.Flora.GutSeed > 0);
    }

    [Fact] // hooked seed catches in a coat and falls off elsewhere
    public void BurrsRideOnCoats()
    {
        var w = Built();
        int c = LandWithHerb(w), n = Map.FW * Map.FW;
        int g = Insert(w, Key(8, (PT.HEIGHT, 3), (PT.HOOK, 15)));
        w.BankMom[c] = (ushort)g; w.BankDad[c] = (ushort)g; w.BankM[c] = 0.5f;
        Animal(w, 0, 4f); w.PInsul[0] = 1f;
        Plants.Burrs(w, 0, c);
        Assert.True(w.FurSeed[0] > 0f); Assert.Equal(g, (int)w.FurSeedG[0]);
        Assert.True(w.BankM[c] < 0.5f);
        Assert.Equal(w.BankM[c] + w.BankM[n + c] + w.BankM[2 * n + c], w.Pool[(int)Pool.Seed][c], 5);
    }

    [Fact] // the census frees genotypes nothing refers to; a full table keeps the parent's genotype
    public void CensusFreesAndAFullTableFallsBack()
    {
        var w = Built(); var t = w.Flora;
        int live0 = t.Live;
        int orphan = Insert(w, Key(1, (PT.HEIGHT, 0)));
        Assert.Equal(live0 + 1, t.Live);
        Plants.Census(w);
        Assert.Equal(0, t.Used[orphan]);
        Assert.True(t.Live <= live0);   // founders no cell took are freed too
        Assert.Equal(0, Plants.Find(t, t.KeyLo[orphan], t.KeyHi[orphan]) == orphan ? 1 : 0);
        int top = t.FreeTop; t.FreeTop = 0;
        long full = t.TableFull;
        Assert.Equal(0, Insert(w, Key(14)));
        Assert.Equal(full + 1, t.TableFull);
        t.FreeTop = top;
    }

    [Fact] // the founders are random, many per world, and the land takes different ones in different places
    public void FoundingIsRandomAndSiteFiltered()
    {
        var a = Built(1); var b = Built(2);
        Assert.NotEqual(a.Flora.KeyLo[1], b.Flora.KeyLo[1]);
        var used = new System.Collections.Generic.HashSet<int>();
        foreach (var g in a.POcc) if (g != 0) used.Add(g);
        Assert.True(used.Count >= 10, $"{used.Count} founders in the cells");
        Assert.True(a.Flora.Live <= K.PLANT_FOUNDERS);
        // every occupied slot holds a genotype of its stratum (the sapling slot a tree)
        for (int i = 0; i < Map.FW * Map.FW; i++)
            for (int k = 0; k < 4; k++)
            {
                int g = a.POcc[i * 4 + k]; if (g == 0) continue;
                Assert.Equal(Math.Min(k, 2), (int)a.Flora.P(g, Ph.STRAT));
            }
    }

    [Fact] // fire: a tall, thick-barked tree keeps its stem, a herb that resprouts keeps its rootstock; banks keep half
    public void FireBurnsByTraits()
    {
        var w = Built();
        int c = LandWithHerb(w), n = Map.FW * Map.FW;
        w.POcc[c * 4] = (ushort)Insert(w, Key(8, (PT.HEIGHT, 3), (PT.RESPROUT, 15)));
        w.POcc[c * 4 + 2] = (ushort)Insert(w, Key(8, (PT.HEIGHT, 15), (PT.BARK, 15)));
        w.Pool[(int)Pool.Grass][c] = 0.4f; w.Tree[c] = 0.5f; w.Shrub[c] = 0f; w.Sapling[c] = 0f; w.BankM[c] = 0.2f;
        float burned = Plants.Burn(w, c, 0.5f);
        Assert.True(burned > 0f);
        Assert.InRange(w.Pool[(int)Pool.Grass][c], 0.4f * 0.8f, 0.4f * 0.9f);
        Assert.True(w.Tree[c] > 0.45f, $"tree {w.Tree[c]}");
        Assert.Equal(0.1f, w.BankM[c], 5);
        w.POcc[c * 4] = (ushort)Insert(w, Key(8, (PT.HEIGHT, 3), (PT.RESPROUT, 0)));
        w.Pool[(int)Pool.Grass][c] = 0.4f;
        Plants.Burn(w, c, 0.5f);
        Assert.Equal(0f, w.Pool[(int)Pool.Grass][c]);
        Assert.True(n > 0);
    }

    [Fact] // a world saved and loaded has the same plants: table, phenotypes, slots and banks
    public void CheckpointKeepsThePlants()
    {
        // 60 founders per ancestor (2026-11-01): with plant food in kg per m2 every clade founds at its limit (600), the
        // world grew to ~10,000 slots and its checkpoint to 2 GB, more than the test machine's free disk; the plants are the point
        using var sim = new Simulation(1024, 8, 2, evo: new EvoSettings { Founders = 60 });
        for (int k = 0; k < 20; k++) sim.Step();
        var w = sim.W;
        string path = Path.Combine(Directory.GetCurrentDirectory(), ".plants-" + Guid.NewGuid().ToString("N"));
        try
        {
            Checkpoint.SaveWorld(w, path);
            var r = new World(w.Cap, w.Seed);
            Checkpoint.LoadWorld(r, path);
            Assert.Equal(w.POcc, r.POcc); Assert.Equal(w.BankM, r.BankM); Assert.Equal(w.Flora.KeyLo, r.Flora.KeyLo);
            Assert.Equal(w.Flora.Phe, r.Flora.Phe); Assert.Equal(w.Flora.Hash, r.Flora.Hash); Assert.Equal(w.Flora.Live, r.Flora.Live);
            Assert.Equal(w.LiveWood, r.LiveWood);
            Assert.Equal(PlantHash(w), PlantHash(r));
        }
        finally { File.Delete(path); File.Delete(path + ".tmp"); }
    }

    [Fact] // the land is covered after the spin-up about as much as by the old producers (0.57 of the land, grass + shrub + tree)
    public void SpunUpLandIsCovered()
    {
        using var sim = new Simulation(1024, 3, 2, empty: true);
        var w = sim.W;
        double c = 0; int land = 0;
        for (int i = 0; i < Map.FW * Map.FW; i++) if (w.Cell[i] == (byte)Realm.Land) { land++; c += w.Pool[(int)Pool.Grass][i] + w.Shrub[i] + w.Tree[i]; }
        double mean = c / land;
        Assert.InRange(mean, 0.57 * 0.75, 0.57 * 1.25);
        Assert.True(Plants.SlotGenotypes(w) > K.PLANT_FOUNDERS / 2, "no genotypes");
        Assert.True(w.Flora.Establish > 0 && w.Flora.Crosses > 0 && w.Flora.Mutations > 0);
    }

    [Fact, Trait("Category", "Slow")] // after two years the drought-tolerant and water-efficient plants hold the dry land
    public void DrySoilSelectsWaterTraits()
    {
        using var sim = new Simulation(1024, 4, 3, empty: true);
        var w = sim.W;
        for (int k = 0; k < Nature.SeasonLength; k++) sim.Step();
        var cells = Enumerable.Range(0, Map.FW * Map.FW).Where(i => w.Cell[i] == (byte)Realm.Land && w.POcc[i * 4] != 0).OrderBy(i => w.MoistBase[i]).ToList();
        int third = cells.Count / 3;
        double Mean(System.Collections.Generic.IEnumerable<int> cs, int tr) => cs.Average(i => w.Flora.Level(w.POcc[i * 4], tr));
        double dryW = Mean(cells.Take(third), PT.WUE) + Mean(cells.Take(third), PT.DROUGHT);
        double wetW = Mean(cells.Skip(cells.Count - third), PT.WUE) + Mean(cells.Skip(cells.Count - third), PT.DROUGHT);
        Assert.True(dryW > wetW, $"WUE + drought tolerance of herbs: dry third {dryW:F2}, wet third {wetW:F2}");
    }

    [Fact] // 2026-11-01: aquatic primary production against the measured range. A shallow cell about 1 m deep, its nutrient
           // held at K_N (half saturation: ordinary, neither oligotrophic nor hypertrophic) and no climate (T_REF = 20 C),
           // grows for a year and is measured over the next. Lakes make 100-500 gC/m2/yr and eutrophic lakes up to ~1,000;
           // the littoral (periphyton, macrophytes and the plankton above them) 300-1,500 (Wetzel 2001, Limnology).
           // Phytoplankton turns over in days: sparse, it doubles in a few days (Eppley 1972; Reynolds 2006). Before,
           // the cell made ~100 gC/m2/yr and the plankton doubled in ~40 days.
    public void WaterProductionIsInTheLiteratureRange()
    {
        float climate = Nature.Climate; Nature.Climate = 0f;
        try
        {
            var w = Built(3);
            int n = Map.FW * Map.FW, cell = -1; float bestD = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if ((w.Cell[i] & (byte)Realm.Shallow) == 0) continue;
                float d = MathF.Abs(w.Depth[i] / w.ElevPerM - 1f);   // metres from 1 m deep
                if (d < bestD) { bestD = d; cell = i; }
            }
            Assert.True(cell >= 0 && bestD < 0.5f, "a shallow cell about 1 m deep");
            int y = cell / Map.FW, year = Nature.SeasonLength;
            float[] Al = w.Pool[(int)Pool.Algae], Ph = w.Pool[(int)Pool.Phyto];
            double gA = 0, gP = 0;
            for (int s = 0; s < 2 * year; s++)
            {
                for (int i = y * Map.FW; i < (y + 1) * Map.FW; i++) if (w.Cell[i] != (byte)Realm.Land) w.Nutrient[i] = K.K_N;
                float a0 = Al[cell], p0 = Ph[cell];
                Fields.Row(w, y);
                w.Step++;
                if (s < year) continue;
                gA += Al[cell] - a0 + K.M_A * a0;   // growth this step: the change plus what died (Fields.Row)
                gP += Ph[cell] - p0 + K.M_PH * p0;
            }
            const double gCperG = 0.45;   // carbon in algal dry matter (Wetzel 2001)
            double toGC = K.WATER_KG_M2 * 1000.0 * gCperG;   // a unit of a water pool per m2, in gC/m2
            double phyto = gP * toGC, littoral = (gA + gP) * toGC;
            Assert.InRange(phyto, 100.0, 1000.0);
            Assert.InRange(littoral, 300.0, 1500.0);
            // turnover: a sparse plankton at this nutrient doubles in under 3 days, as grazed plankton does
            double perDay = K.R_PH * (K.K_N / (K.K_N + K.K_N)) * K.STEPS_PER_DAY;
            Assert.InRange(MathK.Log(2f) / perDay, 0.5, 3.0);
        }
        finally { Nature.Climate = climate; }
    }
}
