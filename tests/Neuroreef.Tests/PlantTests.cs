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
    static (ulong lo, ushort hi) Key(int mid, params (int t, int lv)[] set)
    {
        var lv = Enumerable.Repeat(mid, PT.N).ToArray();
        foreach (var (t, l) in set) lv[t] = l;
        ulong lo = 0; ushort hi = 0;
        for (int k = 0; k < 16; k++) lo |= (ulong)lv[k] << (4 * k);
        for (int k = 0; k < 4; k++) hi |= (ushort)(lv[16 + k] << (4 * k));
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

    static int Insert(World w, (ulong lo, ushort hi) k) => Plants.Insert(w, k.lo, k.hi, 0, 0);

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
            ulong lo = (ulong)args[5]!; ushort hi = (ushort)args[6]!; bool mut = (bool)args[7]!;
            int odd = 0;
            for (int q = 0; q < PT.N; q++)
            {
                int lv = q < 16 ? (int)((lo >> (4 * q)) & 15) : (hi >> (4 * (q - 16))) & 15;
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
        foreach (int tr in new[] { PT.TOXIN, PT.THORN, PT.RESPROUT, PT.NFIX })
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
        Assert.Equal(bank0 + w.GutSeedy[0], w.BankM[2 * n + d], 5);
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
        using var sim = new Simulation(1024, 8, 2);
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
}
