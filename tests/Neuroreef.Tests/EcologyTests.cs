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
        for (int k = G.BODY; k < G.REFLEX; k++) Assert.Equal(0, a[k]);     // body and care genes: the species plan
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
        Assert.False(sim2.W.Prehistory);
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

    [Fact] // NR-320: diet genes trade off within the founder's digestive budget; costly traits raise basal cost
    public void BodyGenesTradeOff()
    {
        var w = new World(16, 1);
        int i = 0;
        w.Spc[i] = 0; w.StageOf[i] = St.Adult;      // Drifters
        Body.Express(w, i);
        float basal0 = w.PBasal[i], v0 = w.PVmaxA[i], grass0 = w.PDietA[(int)Pool.Grass];
        float budget = 0; for (int p = 0; p < Pools.N; p++) budget += Roster.All[0].Adult.Diet[p];
        w.Genome[G.B_DIET + (int)Pool.Grass] = 127;   // specialise on grass
        w.Genome[G.B_SPEED] = 127;                    // faster
        Body.Express(w, i);
        float sum = 0; for (int p = 0; p < Pools.N; p++) sum += w.PDietA[p];
        Assert.InRange(sum, budget - 0.02f, budget + 0.02f);
        Assert.True(w.PDietA[(int)Pool.Grass] > grass0);
        Assert.True(w.PDietA[(int)Pool.Seed] < Roster.All[0].Adult.Diet[(int)Pool.Seed]);
        Assert.True(w.PVmaxA[i] > v0 * 1.4f);
        Assert.True(w.PBasal[i] > basal0 * 1.15f);
    }

    [Fact] // NR-322: a growing juvenile pays for its tissue
    public void GrowthCostsEnergy()
    {
        var w = new World(256, 15); Terrain.Build(w);
        int a = Make(w, 1, 0), b = Make(w, 1, 1);       // two Browsers, one kept juvenile
        w.StageOf[b] = St.Juvenile; w.Age[b] = 10; w.Grown[b] = 0;
        w.Energy[a] = w.Energy[b] = 10f; w.Gut[a] = w.Gut[b] = 0;
        Act(w, a, P.REST); w.IActs[a] = 0; Act(w, b, P.REST); w.IActs[b] = 0;
        Resolve.Run(w, 1, 0);
        float costAdult = 10f - w.Energy[a], costJuv = 10f - w.Energy[b];
        float growth = K.C_CARC * w.PM[b] * (1f - Roster.All[1].Birth) / w.PMature[b] * K.RHO_MEAT / K.GROWTH_EFF;
        Assert.True(costJuv > growth * 0.99f, $"juvenile paid {costJuv}, growth alone costs {growth}");
        Assert.True(costAdult < costJuv);
    }

    [Fact] // NR-322: growth is limited by spare energy: a hungry juvenile does not grow, a fed one grows and matures at adult size
    public void GrowthIsLimitedBySpareEnergy()
    {
        var w = new World(256, 16); Terrain.Build(w);
        int a = Make(w, 0, 0), b = Make(w, 0, 1);       // two juvenile Drifters
        w.StageOf[a] = w.StageOf[b] = St.Juvenile; w.Grown[a] = w.Grown[b] = 0.2f;
        w.Energy[a] = 0.3f * w.EmaxOf(a);                // below GROW_RESERVE: maintenance only
        w.Energy[b] = w.EmaxOf(b);                       // plenty spare
        Act(w, a, P.REST); w.IActs[a] = 0; Act(w, b, P.REST); w.IActs[b] = 0;
        Resolve.Run(w, 1, 0);
        Assert.Equal(0.2f, w.Grown[a]);
        Assert.True(w.Grown[b] > 0.2f);
        w.Grown[b] = 1f; w.T.Births[0] = 0;
        Life.Run(w, new int[w.NS]);
        Assert.Equal(St.Adult, w.StageOf[b]);
        Assert.Equal(St.Juvenile, w.StageOf[a]);
    }

    [Fact] // NR-502: follow-cam moves to the same lineage or species when the followed animal dies
    public void LineageFollowStaysWithTheSpecies()
    {
        using var sim = new Simulation(8192, 31, 1);
        sim.Step();
        int i = sim.PickRandom(0, 1);
        Assert.True(i >= 0);
        sim.ProbeSerial = sim.W.Serial[i]; sim.ProbeFollow = true;
        sim.Step();
        Assert.Equal(i, sim.Probe.Slot);
        Resolve.Die(sim.W, i, Cause.Starve, -1);
        for (int k = 0; k < 3; k++) sim.Step();
        Assert.NotEqual(0u, sim.ProbeSerial);
        int j = sim.FindSerial(sim.ProbeSerial);
        Assert.True(j >= 0);
        Assert.Equal(0, sim.W.Spc[j]);
    }

    [Fact] // NR-210: calm newborn brains - every primitive of every founder starts rare (same rule for all species)
    public void FounderBrainsStartCalm()
    {
        var w = new World(256, 2);
        var rs = new Rng.Seq(3);
        var f = new float[F.N]; var y = new float[G.NIN]; var z = new float[P.N]; var g = new float[P.N];
        double sum = 0; int n = 0;
        for (int i = 0; i < 200; i++)
        {
            Brain.RandomGenome(w.GenomeOf(i), 2, 0, (uint)i);
            for (int p = 0; p < P.N; p++) Assert.InRange(w.Genome[i * G.LEN + G.W2 + p * G.NIN + G.NH + F.BIAS], K.FOUNDER_BIAS - K.FOUNDER_BIAS_NOISE, K.FOUNDER_BIAS + K.FOUNDER_BIAS_NOISE);
            w.Spc[i] = (byte)(i % Roster.Count); w.StageOf[i] = St.Adult;
            Body.Express(w, i); Brain.InitLife(w, i);
            f[F.BIAS] = 1; for (int k = 1; k < F.N; k++) f[k] = rs.Next();
            Brain.Forward(w, i, f, y, z, g);
            for (int p = 0; p < P.N; p++) { sum += g[p]; n++; }
        }
        Assert.True(sum / n < 0.12, $"mean founder gain {sum / n:F3}");
    }

    [Fact] // DESIGN §7.4: once the archive has genomes, colonists are bred from it
    public void ColonistsAreBredFromTheArchive()
    {
        using var sim = new Simulation(16384, 41, 1, prehistory: true);
        var w = sim.W;
        // put a recognisable genome into the Drifter archive
        for (int k = 0; k < 8; k++)
        {
            var g = new Span<sbyte>(w.Archive, k * G.LEN, G.LEN);
            g.Fill(5); g.Slice(G.BODY).Clear();
            w.ArchiveScore[k] = 10 + k;
        }
        w.ArchiveCount[0] = 8;
        // remove the living Drifters so the floor triggers immigration
        for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] != St.Dead && w.Spc[i] == 0) Resolve.DieNoBody(w, i, Cause.Starve, -1);
        w.ArchiveCount[0] = 8;      // deaths may have added entries; keep only ours
        sim.Step();
        int bred = 0, total = 0;
        for (int i = 0; i < w.Cap; i++)
        {
            if (w.StageOf[i] == St.Dead || w.Spc[i] != 0 || (w.Flags[i] & Flag.Immigrant) == 0) continue;
            total++;
            int fives = 0; for (int k = 0; k < 200; k++) if (w.Genome[i * G.LEN + G.W2 + k] == 5) fives++;
            if (fives > 150) bred++;
        }
        Assert.True(total > 0);
        Assert.True(bred >= total / 2, $"{bred} of {total} colonists bred from the archive");
    }

    [Fact] // NR-313: in the live world no animal ever appears except by birth; extinction is permanent
    public void LiveWorldNeverSpawns()
    {
        using var sim = new Simulation(16384, 51, 1);           // a live world (not a prehistory)
        var w = sim.W;
        long founders = 0; foreach (var c in w.T.Created) founders += c;
        for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] != St.Dead && w.Spc[i] == 0) Resolve.DieNoBody(w, i, Cause.Starve, -1);
        for (int k = 0; k < 500; k++) sim.Step();
        int drifters = 0;
        for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] != St.Dead && w.Spc[i] == 0) drifters++;
        Assert.Equal(0, drifters);
        foreach (var m in w.T.Immigrants) Assert.Equal(0, m);
        // every slot created after the founders is a conception, a bud, a spore or a parthenogenetic young
        long created = 0; foreach (var c in w.T.Created) created += c;
        long conceived = 0; foreach (var c in w.T.Conceptions) conceived += c;
        long lays = 0; foreach (var c in w.T.Lays) lays += c;
        Assert.True(created - founders <= conceived + lays, $"created {created - founders}, conceived {conceived}, lays {lays}");
    }
}
