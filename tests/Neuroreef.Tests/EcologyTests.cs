// Ecology and brain requirements: bodies from genes, reproduction genes, founders and ancestors, learning,
// gene bank (NR-205, NR-210 ... NR-212, NR-303, NR-311, NR-315, NR-316, NR-320).

using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Neuroreef.Tests;

public class EcologyTests
{
    [Fact] // NR-315, NR-316: life starts from 6 land and 4 water ancestors, each its own clade, all eating primary producers
    public void AncestorsAreRandomPlantEatersOfTheirRealm()
    {
        using var sim = new Simulation(16384, 7, 1);
        var w = sim.W;
        int used = 0; for (int s = 0; s < K.CLADES; s++) if (w.Sp[s].Used) used++;
        Assert.Equal(K.ANCESTORS_LAND + K.ANCESTORS_WATER, used);
        for (int i = 0; i < w.Cap; i++)
        {
            if (w.StageOf[i] < St.Juvenile) continue;
            int s = w.Spc[i];
            bool land = s < K.ANCESTORS_LAND;
            Assert.True(land ? (w.RealmOf(i) & Realm.Land) != 0 : (w.RealmOf(i) & Realm.Water) != 0, $"clade {s} realm {w.RealmOf(i)}");
            float plants = 0; for (int p = 0; p <= (int)Pool.Phyto; p++) plants += w.PDiet[i * Pools.N + p];
            Assert.True(plants > 0.85f * K.DIET_BUDGET, $"clade {s} plant share {plants}");
            Assert.True(w.PStrike[i] < 0.35f * K.STRIKE_MAX);
        }
        // the ancestors differ from each other: their bodies are random, not designed
        var masses = new float[used];
        for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] >= St.Juvenile) masses[w.Spc[i]] = w.PM[i];
        Assert.True(masses.Distinct().Count() >= used - 1);
    }

    [Fact] // NR-320: legs, fins and wings decide locomotion and realm; organs cost energy; wings cost more on a heavy body
    public void LocomotionAndRealmComeFromOrgans()
    {
        var w = new World(16, 1);
        Loco L(sbyte legs, sbyte fins, sbyte wings, sbyte mass = 0)
        {
            w.Genome[G.B_LEGS] = legs; w.Genome[G.B_FINS] = fins; w.Genome[G.B_WINGS] = wings; w.Genome[G.B_MASS] = mass;
            Body.Express(w, 0); return w.LocoOf(0);
        }
        Assert.Equal(Loco.Walk, L(100, -127, -127));
        Assert.Equal(Realm.Land, w.RealmOf(0));
        Assert.Equal(1f, w.PKThirst[0]);
        Assert.Equal(Loco.Swim, L(-127, 100, -127));
        Assert.Equal(Realm.Water, w.RealmOf(0));
        Assert.Equal(0f, w.PKThirst[0]);
        Assert.Equal(Loco.Wade, L(100, 100, -127));
        Assert.Equal(Loco.Fly, L(-127, -127, 127));
        Assert.Equal(Loco.Sessile, L(-127, -127, -127));
        Assert.Equal(0f, w.PVmax[0]);
        // costs: an organism with all organs pays more than one without; wings cost more on a heavier body
        L(-127, -127, -127); float bare = w.PBasal[0] / MathK.Pow(w.PM[0], K.EXP_BASAL);
        L(127, 127, -127); float limbs = w.PBasal[0] / MathK.Pow(w.PM[0], K.EXP_BASAL);
        Assert.True(limbs > bare);
        L(-127, -127, 127, -100); float wingSmall = w.PBasal[0] / MathK.Pow(w.PM[0], K.EXP_BASAL);
        L(-127, -127, 127, 100); float wingBig = w.PBasal[0] / MathK.Pow(w.PM[0], K.EXP_BASAL);
        Assert.True(wingBig > wingSmall);
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
        for (int k = G.BODY; k < G.REFLEX; k++) Assert.Equal(0, a[k]);     // body genes are set by the ancestor (Clades.AncestorBody)
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
        Assert.Equal(K.CLADES, bank.Genomes.Length);
        Assert.True(bank.Clades.Count >= 5);
        Assert.True(bank.Total > 100);
        using var sim2 = new Simulation(8192, 3, 2, bank);
        Assert.False(sim2.W.Prehistory);
        Assert.True(sim2.W.AliveCount > 1000);
    }

    // ---- reproduction from genes (NR-311): a scripted scene, intents set by hand, then RESOLVE and LIFECYCLE

    static (World w, int a, int b) Pair(bool sexual, bool live, int cladeB = 0)
    {
        var w = new World(256, 11);
        Terrain.Build(w);
        int a = Make(w, 0, 0, sexual, live), b = Make(w, cladeB, 1, sexual, live);
        w.Genome[b * G.LEN + G.B_MASS] = w.Genome[a * G.LEN + G.B_MASS];
        w.Sex[a] = 0; w.Sex[b] = 1;
        w.X[b] = w.X[a] + 0.5f; w.Y[b] = w.Y[a];
        return (w, a, b);
    }

    static int Make(World w, int clade, uint key, bool sexual = true, bool live = true)
    {
        w.Sp[clade].Used = true; w.Sp[clade].Alive = true;
        int c = Life.Found(w, clade, ReadOnlySpan<sbyte>.Empty, true, key + 1);
        Assert.True(c >= 0);
        int g = c * G.LEN;
        w.Genome[g + G.B_SEX] = (sbyte)(sexual ? 60 : -60);
        w.Genome[g + G.B_LIVE] = (sbyte)(live ? 60 : -60);
        w.Genome[g + G.B_LEGS] = 100; w.Genome[g + G.B_FINS] = -127; w.Genome[g + G.B_WINGS] = -127;   // a walker
        w.Genome[g + G.B_LITTER] = 40;
        Body.Express(w, c);
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
        var (w, a, b) = Pair(true, true);
        Act(w, a, P.MATE, b); w.IActs[b] = 0;
        Resolve.Run(w, 1, 0);
        int embryos = Count(w, St.Embryo);
        Assert.True(embryos >= 1 && embryos <= w.PLitter[a], $"embryos {embryos}, litter {w.PLitter[a]}");
        var counts = new int[w.NS];
        for (int t = 0; t < w.PGest[a] + 2; t++) { w.Energy[a] = w.EmaxOf(a); Life.Run(w, counts); }
        Assert.Equal(0, Count(w, St.Embryo));
        Assert.Equal(embryos, Count(w, St.Juvenile));
    }

    [Fact]
    public void EggLayersLayEggs()
    {
        var (w, a, b) = Pair(true, false);
        Act(w, a, P.MATE, b); w.IActs[b] = 0;
        Resolve.Run(w, 1, 0);
        Assert.True(Count(w, St.Egg) >= 1);
        Assert.Equal(0, Count(w, St.Embryo));
    }

    [Fact]
    public void AsexualOrganismsReproduceAloneAndDoNotMate()
    {
        var (w, a, b) = Pair(false, false);
        Act(w, a, P.MATE, b); w.IActs[b] = 0;
        Resolve.Run(w, 1, 0);
        Assert.Equal(0, Count(w, St.Egg));                 // asexual: mating does nothing
        Act(w, a, P.LAY);
        Resolve.Run(w, 1, 0);
        Assert.True(Count(w, St.Egg) >= 1);
        Assert.True(w.T.Lays[0] >= 1);
    }

    [Fact] // NR-334: organisms of different clades cannot interbreed
    public void DifferentCladesCannotMate()
    {
        var (w, a, b) = Pair(true, true, cladeB: 1);
        Act(w, a, P.MATE, b); w.IActs[b] = 0;
        Resolve.Run(w, 1, 0);
        Assert.Equal(0, Count(w, St.Embryo));
        Assert.False(Decide.ValidPartner(w, a, b));
    }

    [Fact]
    public void SameSexCannotMate()
    {
        var (w, a, b) = Pair(true, true);
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

    [Fact] // NR-320: diet genes share a fixed digestive budget; costly traits raise basal cost
    public void BodyGenesTradeOff()
    {
        var w = new World(16, 1);
        int i = 0;
        w.Genome[G.B_LEGS] = 100; w.Genome[G.B_FINS] = -127; w.Genome[G.B_WINGS] = -127;   // a land walker (legs only)
        Body.Express(w, i);
        float basal0 = w.PBasal[i], v0 = w.PVmax[i], grass0 = w.PDiet[(int)Pool.Grass], seed0 = w.PDiet[(int)Pool.Seed];
        float sum0 = 0; for (int p = 0; p < Pools.N; p++) sum0 += w.PDiet[p];
        Assert.InRange(sum0, K.DIET_BUDGET - 0.02f, K.DIET_BUDGET + 0.02f);
        Assert.Equal(0f, w.PDiet[(int)Pool.Algae]);        // a walker cannot graze algae
        w.Genome[G.B_DIET + (int)Pool.Grass] = 127;        // specialise on grass
        w.Genome[G.B_SPEED] = 127;                         // faster
        Body.Express(w, i);
        float sum = 0; for (int p = 0; p < Pools.N; p++) sum += w.PDiet[p];
        Assert.InRange(sum, K.DIET_BUDGET - 0.06f, K.DIET_BUDGET + 0.02f);
        Assert.True(w.PDiet[(int)Pool.Grass] > grass0);
        Assert.True(w.PDiet[(int)Pool.Seed] < seed0);
        Assert.True(w.PVmax[i] > v0 * 1.4f);
        Assert.True(w.PBasal[i] > basal0 * 1.15f);
    }

    [Fact] // NR-322: a growing juvenile pays for its tissue
    public void GrowthCostsEnergy()
    {
        var w = new World(256, 15); Terrain.Build(w);
        int a = Make(w, 0, 0), b = Make(w, 0, 1);       // two organisms, one kept juvenile
        w.Genome[b * G.LEN + G.B_MASS] = w.Genome[a * G.LEN + G.B_MASS]; Body.Express(w, b);
        w.StageOf[b] = St.Juvenile; w.Age[b] = 10; w.Grown[b] = 0;
        w.Energy[a] = w.Energy[b] = 10f; w.Gut[a] = w.Gut[b] = 0;
        Act(w, a, P.REST); w.IActs[a] = 0; Act(w, b, P.REST); w.IActs[b] = 0;
        Resolve.Run(w, 1, 0);
        float costAdult = 10f - w.Energy[a], costJuv = 10f - w.Energy[b];
        float growth = K.C_CARC * w.PM[b] * (1f - w.PBirth[b]) / w.PMature[b] * K.RHO_MEAT / K.GROWTH_EFF;
        Assert.True(costJuv > growth * 0.99f, $"juvenile paid {costJuv}, growth alone costs {growth}");
        Assert.True(costAdult < costJuv);
    }

    [Fact] // NR-322: growth is limited by spare energy: a hungry juvenile does not grow, a fed one grows and matures at adult size
    public void GrowthIsLimitedBySpareEnergy()
    {
        var w = new World(256, 16); Terrain.Build(w);
        int a = Make(w, 0, 0), b = Make(w, 0, 1);       // two juveniles
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
            w.Spc[i] = (byte)(i % K.CLADES); w.StageOf[i] = St.Adult;
            Body.Express(w, i); Brain.InitLife(w, i);
            f[F.BIAS] = 1; for (int k = 1; k < F.N; k++) f[k] = rs.Next();
            Brain.Forward(w, i, f, y, z, g);
            for (int p = 0; p < P.N; p++) { sum += g[p]; n++; }
        }
        Assert.True(sum / n < 0.12, $"mean founder gain {sum / n:F3}");
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
