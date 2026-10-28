// Ecology and brain requirements: energy-cycle closure, reproduction modes, founders, learning,
// gene bank (NR-205, NR-210 ... NR-212, NR-303, NR-311, NR-315, NR-316).

using System;
using System.IO;
using Xunit;

namespace Neuroreef.Tests;

public class EcologyTests
{
    [Fact] // NR-315, NR-316
    public void RosterClosesTheEnergyCycle()
    {
        var fails = Roster.ClosureCheck(Roster.All);
        Assert.True(fails.Count == 0, string.Join("\n", fails));
    }

    [Fact] // NR-315: the check really detects an open cycle
    public void ClosureCheckDetectsAnUneatenPool()
    {
        var copy = (Species[])Roster.All.Clone();
        var hummers = copy[5];
        var saved = hummers.Adult.Diet[(int)Pool.Nectar];
        var gl = copy[3].Adult.Diet[(int)Pool.Nectar];
        var bf = copy[14].Adult.Diet[(int)Pool.Nectar];
        try
        {
            hummers.Adult.Diet[(int)Pool.Nectar] = 0; copy[3].Adult.Diet[(int)Pool.Nectar] = 0; copy[14].Adult.Diet[(int)Pool.Nectar] = 0;
            var fails = Roster.ClosureCheck(copy);
            Assert.Contains(fails, f => f.Contains("nectar"));
        }
        finally { hummers.Adult.Diet[(int)Pool.Nectar] = saved; copy[3].Adult.Diet[(int)Pool.Nectar] = gl; copy[14].Adult.Diet[(int)Pool.Nectar] = bf; }
    }

    [Fact] // NR-210: founder brains come from the RNG only; founder bodies are the species body plan
    public void FounderGenomesAreRandomAndSpeciesFree()
    {
        var a = new sbyte[G.LEN]; var b = new sbyte[G.LEN]; var c = new sbyte[G.LEN];
        Brain.RandomGenome(a, 9, 0, 1234);
        Brain.RandomGenome(b, 9, 0, 1234);
        Brain.RandomGenome(c, 9, 0, 1235);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        for (int k = G.BODY; k < G.LEN; k++) Assert.Equal(0, a[k]);
        int nonzero = 0;
        for (int k = 0; k < G.ETA; k++) if (a[k] != 0) nonzero++;
        Assert.True(nonzero > G.ETA / 2);
    }

    [Fact] // NR-211: reward raises the probability of the rewarded action (two-choice scene)
    public void LearningRaisesProbabilityOfRewardedAction()
    {
        var w = new World(64, 1);
        int i = 0;
        Brain.RandomGenome(w.GenomeOf(i), 1, 0, 1);
        w.Genome[G.ETA] = 127;     // fast learner
        w.Genome[G.LAMBDA] = -128; // short trace
        w.Spc[i] = 0; w.StageOf[i] = St.Adult;
        Body.Express(w, i);
        Brain.InitLife(w, i);
        var f = new float[F.N]; var y = new float[G.NIN]; var z = new float[P.N]; var g = new float[P.N]; var d = new float[P.N];
        f[F.BIAS] = 1f; f[F.HUNGER] = 0.8f; f[F.FOOD_HERE] = 1f;
        float Prob() { Brain.Forward(w, i, f, y, z, g); return g[P.EAT]; }
        float p0 = Prob();
        var rs = new Rng.Seq(5);
        for (int t = 0; t < 4000; t++)
        {
            w.Step = (ulong)t;
            Brain.Learn(w, i);
            Brain.Forward(w, i, f, y, z, g);
            for (int p = 0; p < P.N; p++) d[p] = 0;
            bool eat = rs.Next() < g[P.EAT];
            d[P.EAT] = (eat ? 1f : 0f) - g[P.EAT];
            Brain.Record(w, i, y, d);
            w.Reward[i] = eat ? 1f : 0f;    // the reward for this step's action, credited next step
        }
        float p1 = Prob();
        Assert.True(p1 > p0 + 0.2f && p1 > 0.8f, $"P(EAT) {p0:F2} -> {p1:F2}");
    }

    [Fact] // NR-205: inheritance mixes parents row by row and mutates
    public void CrossoverTakesWholeRowsFromEitherParent()
    {
        var m = new sbyte[G.LEN]; var f = new sbyte[G.LEN]; var c = new sbyte[G.LEN];
        Array.Fill(m, (sbyte)10); Array.Fill(f, (sbyte)-10);
        Brain.Crossover(c, m, f, 1, 2, 3);
        int fromM = 0, fromF = 0;
        for (int row = 0; row < G.NH + P.N; row++)
        {
            int v = c[row * G.NIN];
            for (int k = 0; k < G.NIN; k++) Assert.Equal(v, c[row * G.NIN + k]);
            if (v == 10) fromM++; else fromF++;
        }
        Assert.True(fromM > 5 && fromF > 5);
        Brain.Mutate(c, 1, 2, 3);
        int changed = 0;
        for (int k = 0; k < G.LEN; k++) if (c[k] != 10 && c[k] != -10) changed++;
        Assert.InRange(changed, 1, G.LEN / 5);
    }

    [Fact] // NR-212
    public void GeneBankRoundTrip()
    {
        using var sim = new Simulation(8192, 2, 2);
        for (int k = 0; k < 50; k++) sim.Step();
        var ms = new MemoryStream();
        GeneBank.Save(sim.W, ms);
        ms.Position = 0;
        var bank = GeneBank.Load(ms);
        Assert.Equal(Roster.Count, bank.Genomes.Length);
        Assert.True(bank.Total > 100);
        using var sim2 = new Simulation(8192, 3, 2, bank);
        Assert.False(sim2.W.Genesis);
        Assert.True(sim2.W.AliveCount > 1000);
    }

    // ---- reproduction modes (NR-311): a scripted scene, intents set by hand, then RESOLVE and LIFECYCLE

    static (World w, int a, int b) Pair(int species, bool female0)
    {
        var w = new World(256, 11);
        Terrain.Build(w);
        int a = Make(w, species, 0), b = Make(w, species, 1);
        w.Sex[a] = (byte)(female0 ? 0 : 1); w.Sex[b] = (byte)(female0 ? 1 : 0);
        w.X[b] = w.X[a] + 0.5f; w.Y[b] = w.Y[a];
        return (w, a, b);
    }

    static int Make(World w, int species, uint key)
    {
        int c = Life.Found(w, species, ReadOnlySpan<sbyte>.Empty, true, key + 1);
        Assert.True(c >= 0);
        w.Flags[c] = 0;
        w.Energy[c] = w.EmaxOf(c);
        w.Age[c] = w.PMature[c] * 2;
        w.Cooldown[c] = 0;
        return c;
    }

    static void Act(World w, int i, int prim, int partner = -1)
    {
        w.IActs[i] = 1u << (prim - P.FIRST_ACTION);
        w.IMate[i] = partner; w.ITarget[i] = -1;
        w.IHeading[i] = w.Heading[i]; w.ISpeed[i] = 0;
    }

    static int Count(World w, byte stage) { int n = 0; for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] == stage) n++; return n; }

    [Fact]
    public void SexualLiveBirthProducesEmbryosThenJuveniles()
    {
        var (w, a, b) = Pair(0, true);   // Drifters
        Act(w, a, P.MATE, b); w.IActs[b] = 0;
        Resolve.Run(w, 1, 0);
        int embryos = Count(w, St.Embryo);
        Assert.Equal(Roster.All[0].Litter, embryos);
        var counts = new int[Roster.Count];
        for (int t = 0; t < Roster.All[0].Gestation + 2; t++) { w.Energy[a] = w.EmaxOf(a); Life.Run(w, counts); }
        Assert.Equal(0, Count(w, St.Embryo));
        Assert.Equal(embryos, Count(w, St.Juvenile));
    }

    [Fact]
    public void FissionSplitsTheColony()
    {
        var w = new World(256, 12); Terrain.Build(w);
        int a = Make(w, 5, 0);           // Hummers
        float e0 = w.Energy[a];
        Act(w, a, P.LAY);
        Resolve.Run(w, 1, 0);
        Assert.Equal(1, Count(w, St.Juvenile));
        Assert.True(w.Energy[a] < e0 * 0.6f);
    }

    [Fact]
    public void SporesLandAsEggs()
    {
        var w = new World(256, 13); Terrain.Build(w);
        int a = Make(w, 8, 0);           // Rotmats
        Act(w, a, P.LAY);
        Resolve.Run(w, 1, 0);
        Assert.Equal(1, Count(w, St.Egg));
    }

    [Fact]
    public void HermaphroditesBothConceive()
    {
        var (w, a, b) = Pair(7, true);   // Delvers
        w.Sex[b] = w.Sex[a];             // same sex is fine for hermaphrodites
        Act(w, a, P.MATE, b); w.IActs[b] = 0;
        Resolve.Run(w, 1, 0);
        Assert.True(w.Children[a] > 0 && w.Children[b] > 0);
    }

    [Fact]
    public void SameSexSexualSpeciesCannotMate()
    {
        var (w, a, b) = Pair(0, true);
        w.Sex[b] = w.Sex[a];
        Act(w, a, P.MATE, b); w.IActs[b] = 0;
        Resolve.Run(w, 1, 0);
        Assert.Equal(0, Count(w, St.Embryo));
    }

    [Fact] // NR-303
    public void ThirstKillsLandAnimalsThatDoNotDrink()
    {
        var w = new World(256, 14); Terrain.Build(w);
        int a = Make(w, 0, 0);
        for (int t = 0; t < 2000 && w.StageOf[a] != St.Dead; t++)
        {
            Act(w, a, P.REST); w.IActs[a] = 0; w.Energy[a] = w.EmaxOf(a);
            Resolve.Run(w, 1, 0);
        }
        Assert.Equal(St.Dead, w.StageOf[a]);
        Assert.Equal(1, w.T.Deaths[0 * Cause.N + Cause.Thirst]);
    }
}
