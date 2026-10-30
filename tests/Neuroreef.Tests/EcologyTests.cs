// Ecology and brain requirements: bodies from genes, reproduction genes, founders and ancestors, learning,
// gene bank (NR-205, NR-210 ... NR-212, NR-303, NR-311, NR-315, NR-316, NR-320).

using System;
using System.Collections.Generic;
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
            Assert.True(plants > 0.7f * K.DIET_BUDGET, $"clade {s} plant share {plants}");
            Assert.True(w.PStrike[i] <= 0.65f * K.STRIKE_MAX, $"clade {s} strike {w.PStrike[i]}");   // weapons and armour start in the same range (Revision L)
        }
        // the ancestors differ from each other: their bodies are random, not designed
        var masses = new float[used];
        for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] >= St.Juvenile) masses[w.Spc[i]] = w.PM[i];
        Assert.True(masses.Distinct().Count() >= used - 1);
    }

    [Fact] // NR-320: founders vary around their ancestor (standing variation), as a natural population does
    public void FoundersVaryAroundTheirAncestor()
    {
        var w = new World(512, 21);
        Terrain.Build(w);
        w.Sp[0].Used = true; w.Sp[0].Alive = true;
        var v = new List<float>();
        for (uint k = 0; k < 300; k++) { int c = Life.Found(w, 0, ReadOnlySpan<sbyte>.Empty, true, k + 1); if (c >= 0) v.Add(w.Genome[c * G.LEN + G.B_SPEED]); }
        Assert.True(v.Count > 200);
        float m = v.Average(), sd = MathF.Sqrt(v.Select(x => (x - m) * (x - m)).Average());
        Assert.InRange(sd, 6f, 20f);
    }

    [Fact] // DESIGN §7.3: body genes mutate at MUT_BODY, brain and reflex bytes at MUT_RATE
    public void BodyGenesMutateAtTheirOwnRate()
    {
        var g = new sbyte[G.LEN]; var e = new EvoSettings();
        long body = 0, brain = 0; const int N = 10000;
        for (uint t = 0; t < N; t++)
        {
            Array.Clear(g);
            Brain.Mutate(g, 5, t, 77, e);
            for (int k = 0; k < G.LEN; k++) if (g[k] != 0) { if (k >= G.BODY && k < G.BODY + G.NBODY) body++; else brain++; }
        }
        // a small change of sigma MUT_SMALL rounds to 0 about 20% of the time
        float pBody = body / (float)(N * G.NBODY), pBrain = brain / (float)(N * (G.LEN - G.NBODY));
        Assert.InRange(pBody, 0.6f * K.MUT_BODY, 1.05f * K.MUT_BODY);
        Assert.InRange(pBrain, 0.6f * K.MUT_RATE, 1.05f * K.MUT_RATE);
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
        Assert.Equal(0, (int)(w.RealmOf(0) & Realm.Deep));          // cannot stay up in open water
        Assert.True(w.PVLand[0] > w.PVWater[0]);
        Assert.Equal(Loco.Swim, L(-127, 100, -127));
        Assert.NotEqual(0, (int)(w.RealmOf(0) & Realm.Deep));
        Assert.True(w.PVWater[0] > w.PVLand[0] && w.PVLand[0] > 0);   // a legless swimmer can still crawl
        // speed follows smoothly from organ size: half a fin swims, slower than a whole one
        L(-127, 0, -127); float half = w.PVWater[0];
        L(-127, 127, -127); Assert.True(w.PVWater[0] > half && half > 0);
        // flight needs enough wing for the body's weight
        Assert.Equal(Loco.Fly, L(-127, -127, 127, -100));
        Assert.NotEqual(Loco.Fly, L(-127, -127, 127, 127));
        Assert.Equal(Loco.Wade, L(100, 100, -127));
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
        f[F.BIAS] = 1f; f[F.HUNGER] = 0.1f; f[F.FOOD_HERE] = 1f;   // barely hungry: the feeding instinct is weak, so what changes is learned
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
        // about one change per newborn on average: over 20 newborns, some changes, never a large share of the genome
        int changed = 0;
        for (uint key = 0; key < 20; key++)
        {
            var d = (sbyte[])c.Clone();
            Brain.Mutate(d, 1, 2, 100 + key);
            for (int k = 0; k < G.LEN; k++) if (d[k] != c[k]) changed++;
        }
        Assert.InRange(changed, 5, G.LEN / 5);
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
        Assert.True(sim2.W.AliveCount > 100, $"{sim2.W.AliveCount} founded");   // as many as each clade's home range can feed (Revision P)
    }

    // ---- reproduction from genes (NR-311): a scripted scene, intents set by hand, then RESOLVE and LIFECYCLE

    static (World w, int a, int b) Pair(bool sexual, bool live, int cladeB = 0)
    {
        var w = new World(256, 11);
        Terrain.Build(w);
        int a = Make(w, 0, 0, sexual, live), b = Make(w, cladeB, 1, sexual, live);
        for (int k = G.BODY; k < G.BODY + G.NBODY; k++) w.Genome[b * G.LEN + k] = w.Genome[a * G.LEN + k];   // genetically alike
        Body.Express(w, b); w.Energy[b] = w.EmaxOf(b); w.Age[b] = w.PMature[b] * 2;
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
        w.Genome[g + G.B_HERM] = -60;                                // separate sexes
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

    [Fact] // NR-334: there is no species check; mating fails more the more two genomes differ, and costs are still paid
    public void DistantGenomesCannotProduceYoung()
    {
        var (w, a, b) = Pair(true, true, cladeB: 1);
        Assert.True(Decide.ValidPartner(w, a, b));                  // a different clade is still a possible mate
        for (int k = G.B_LEGS; k <= G.B_IMMUNE; k++) w.Genome[b * G.LEN + k] = (sbyte)(w.Genome[a * G.LEN + k] > 0 ? -127 : 127);
        w.Genome[b * G.LEN + G.B_SEX] = 60; w.Genome[b * G.LEN + G.B_HERM] = -60;   // still sexual, separate sexes
        Body.Express(w, b); w.Energy[b] = w.EmaxOf(b); w.Age[b] = w.PMature[b] * 2;
        Assert.True(Body.Distance(w, a, b) > 2f * K.D_COMPAT, $"distance {Body.Distance(w, a, b)}");
        float ea = w.Energy[a];
        Act(w, a, P.MATE, b); w.IActs[b] = 0;
        Resolve.Run(w, 1, 0);
        Assert.Equal(0, Count(w, St.Embryo));
        Assert.Equal(1, w.T.Matings[0]);
        Assert.True(w.Energy[a] < ea);
    }

    [Fact] // NR-334: two hermaphrodites mate and both conceive
    public void HermaphroditesBothConceive()
    {
        var (w, a, b) = Pair(true, true);
        foreach (int i in new[] { a, b }) { w.Genome[i * G.LEN + G.B_HERM] = 60; Body.Express(w, i); w.Energy[i] = w.EmaxOf(i); w.Age[i] = w.PMature[i] * 2; }
        w.Sex[b] = w.Sex[a];
        Act(w, a, P.MATE, b); w.IActs[b] = 0;
        Resolve.Run(w, 1, 0);
        Assert.NotEqual(0, w.Flags[a] & Flag.Carrying);
        Assert.NotEqual(0, w.Flags[b] & Flag.Carrying);
    }

    [Fact] // DESIGN §9.4 (Revision H): a hit wounds in proportion to the striker's force; a tiny striker cannot kill a big adult at once
    public void StrikeDamageScalesWithMass()
    {
        var (w, a, b) = Pair(true, true, cladeB: 1);
        w.Genome[a * G.LEN + G.B_MASS] = -127; w.Genome[a * G.LEN + G.B_STRIKE] = 127; Body.Express(w, a);
        w.Genome[b * G.LEN + G.B_MASS] = 127; w.Genome[b * G.LEN + G.B_ARMOUR] = -127; Body.Express(w, b);
        w.Energy[a] = w.EmaxOf(a); w.Energy[b] = w.EmaxOf(b); w.StageOf[b] = St.Adult;
        w.X[b] = w.X[a] + 0.3f; w.Y[b] = w.Y[a];
        float eb = w.Energy[b];
        for (int t = 0; t < 20; t++) { Act(w, a, P.STRIKE); w.ITarget[a] = b; w.IActs[b] = 0; w.Energy[a] = w.EmaxOf(a); Resolve.Run(w, 1, 0); w.Step++; }
        Assert.NotEqual(St.Dead, w.StageOf[b]);
        Assert.True(eb - w.Energy[b] < 0.1f * eb, "a 0.05-mass striker took more than 10% of a 40-mass body's reserve in 20 strikes");
    }

    [Fact] // DESIGN §4, §9.4 (Revision H): an own-kind egg is kin: struck only when no other kind is in reach (cannibalism stays possible)
    public void OwnKindEggIsStruckOnlyWhenNothingElseIs()
    {
        var w = new World(256, 31); Terrain.Build(w);
        int a = Make(w, 0, 0, sexual: false, live: false);
        w.Genome[a * G.LEN + G.B_STRIKE] = 127; Body.Express(w, a); w.Energy[a] = w.EmaxOf(a);
        int e = Life.NewSlot(w);
        Life.CopyGenome(w, a, e, false);
        Life.Place(w, e, 0, St.Egg, w.X[a] + 0.4f, w.Y[a], 1f, a);
        Body.Express(w, e); w.Flags[e] = 0;
        Simulation.BuildFor(w);
        Decide.Run(w, a, new Decide.ProbeBuf());
        Assert.Equal(e, w.ITarget[a]);                                // nothing else: the egg (cannibalism)
        int o = Make(w, 1, 5);                                         // another kind, in reach
        for (int k = G.B_LEGS; k <= G.B_FINN; k++) w.Genome[o * G.LEN + k] = (sbyte)(w.Genome[a * G.LEN + k] > 0 ? -127 : 127);
        w.Genome[o * G.LEN + G.B_MASS] = w.Genome[a * G.LEN + G.B_MASS];
        Body.Express(w, o); w.X[o] = w.X[a] + 0.6f; w.Y[o] = w.Y[a]; w.Flags[o] = 0;
        Assert.False(Body.LooksAlike(w, a, o));
        Simulation.BuildFor(w);
        Decide.Run(w, a, new Decide.ProbeBuf());
        Assert.Equal(o, w.ITarget[a]);
    }

    [Fact] // DESIGN §8 (Revision H): the father gets a short refractory period; an infertile mating leaves the female free
    public void MaleRefractoryIsShortAndFailedMatingLeavesFemaleFree()
    {
        var (w, a, b) = Pair(true, true);
        Act(w, a, P.MATE, b); w.IActs[b] = 0;
        Resolve.Run(w, 1, 0);
        Assert.True(Count(w, St.Embryo) >= 1);
        Assert.InRange(w.Cooldown[b], 1f, 0.11f * w.PCool[b]);   // father
        Assert.True(w.Cooldown[a] >= w.PCool[a] - 1);            // mother conceived
        var (w2, a2, b2) = Pair(true, true, cladeB: 1);
        for (int k = G.B_LEGS; k <= G.B_IMMUNE; k++) w2.Genome[b2 * G.LEN + k] = (sbyte)(w2.Genome[a2 * G.LEN + k] > 0 ? -127 : 127);
        w2.Genome[b2 * G.LEN + G.B_SEX] = 60; w2.Genome[b2 * G.LEN + G.B_HERM] = -60;
        Body.Express(w2, b2); w2.Energy[b2] = w2.EmaxOf(b2); w2.Age[b2] = w2.PMature[b2] * 2;
        Act(w2, a2, P.MATE, b2); w2.IActs[b2] = 0;
        Resolve.Run(w2, 1, 0);
        Assert.Equal(0, Count(w2, St.Embryo));
        Assert.Equal(0f, w2.Cooldown[a2]);
    }

    [Fact] // DESIGN §8 (Revision H): with no free slot, a mating does nothing and costs nothing
    public void NoSlotNoMatingCost()
    {
        var (w, a, b) = Pair(true, true);
        float ea = w.Energy[a];
        int saved = w.FreeTop; w.FreeTop = 0;
        Act(w, a, P.MATE, b); w.IActs[b] = 0;
        Resolve.Run(w, 1, 0);
        w.FreeTop = saved;
        Assert.Equal(0, w.T.Matings[0]);
        Assert.True(w.Energy[a] >= ea - 0.05f * w.EmaxOf(a));   // only basal costs
        Assert.Equal(0f, w.Cooldown[a]);
    }

    [Fact] // DESIGN §6.2 (Revision H): founders of one ancestor share its brain, with small variation
    public void FoundersShareTheirAncestorsBrain()
    {
        using var sim = new Simulation(16384, 5, 1);
        var w = sim.W;
        int f1 = -1, f2 = -1, other = -1;
        for (int i = 0; i < w.Cap; i++)
        {
            if (w.StageOf[i] == St.Dead) continue;
            if (w.Spc[i] == 0) { if (f1 < 0) f1 = i; else if (f2 < 0) f2 = i; }
            else if (w.Spc[i] == 1 && other < 0) other = i;
        }
        float D(int x, int y) { float s = 0; for (int k = 0; k < G.W2; k++) { float d = w.Genome[x * G.LEN + k] - w.Genome[y * G.LEN + k]; s += d * d; } return MathF.Sqrt(s / G.W2); }
        Assert.True(D(f1, f2) < 5f, $"same-ancestor founders differ by {D(f1, f2)} per W1 byte");
        Assert.True(D(f1, other) > 2f * D(f1, f2));
    }

    [Fact] // DESIGN §8: a hermaphrodite can mate with a male (it is the mother) and pays for siring only when it sires
    public void HermaphroditeMatesWithAMale()
    {
        var (w, a, b) = Pair(true, true);
        w.Genome[a * G.LEN + G.B_HERM] = 60; Body.Express(w, a); w.Energy[a] = w.EmaxOf(a); w.Age[a] = w.PMature[a] * 2;
        w.Sex[b] = 1;
        Assert.True(Decide.ValidPartner(w, a, b));
        Act(w, a, P.MATE, b); w.IActs[b] = 0;
        Resolve.Run(w, 1, 0);
        Assert.NotEqual(0, w.Flags[a] & Flag.Carrying);              // the hermaphrodite carries the young
        Assert.Equal(0, w.Flags[b] & Flag.Carrying);
    }

    [Fact] // DESIGN §6.3: wings too small to fly still speed a runner (wing-assisted running)
    public void PartialWingsHelpARunner()
    {
        var w = new World(16, 1);
        w.Genome[G.B_LEGS] = 100; w.Genome[G.B_FINS] = -127; w.Genome[G.B_MASS] = 60;
        w.Genome[G.B_WINGS] = -127; Body.Express(w, 0); float none = w.PVLand[0];
        w.Genome[G.B_WINGS] = -20; Body.Express(w, 0);
        Assert.NotEqual(Loco.Fly, w.LocoOf(0));
        Assert.True(w.PVLand[0] > none);
    }

    [Fact] // DESIGN §7.7 (Revision J): every founder carries the mating instinct; it is stronger in males
    public void FoundersCarryTheMatingInstinct()
    {
        var g = new sbyte[G.LEN];
        Brain.RandomGenome(g, 3, 0, 42);
        Assert.True(g[G.REFLEX + P.MATE * G.NC + Reflex.READY_MATE] >= K.KIT_W - K.FOUNDER_W2);
        Assert.True(g[G.REFLEX + P.SEEK_MATE * G.NC + Reflex.READY_ALONE] >= K.KIT_W - K.FOUNDER_W2);
        Span<float> f = stackalloc float[F.N]; Span<float> male = stackalloc float[G.NC], female = stackalloc float[G.NC];
        f[F.READY] = 1f; f[F.MATE_CLOSE] = 0.8f;
        Reflex.Inputs(f, male, 1f); Reflex.Inputs(f, female, K.DRIVE_FEMALE);
        Assert.True(male[Reflex.READY_MATE] > female[Reflex.READY_MATE] && female[Reflex.READY_MATE] > 0);
    }

    [Fact] // DESIGN §6.2 (Revision J): BigBrains ancestors per branch start with BigBrainUnits memory units, the rest up to BRAIN_START (plus founder variation)
    public void SomeAncestorsStartBigBrained()
    {
        using var sim = new Simulation(8192, 7, 1, evo: new EvoSettings { BigBrains = 2, BigBrainUnits = 128 });
        // only the big-brained land ancestor has a voice (Revision M)
        for (int i = 0; i < sim.W.Cap; i++)
            if (sim.W.StageOf[i] != St.Dead) Assert.Equal(sim.W.Spc[i] == 0, sim.W.PVoice[i] >= K.VOICE_MIN);
        var w = sim.W;
        var maxUnits = new int[K.CLADES];
        for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] != St.Dead) maxUnits[w.Spc[i]] = Math.Max(maxUnits[w.Spc[i]], w.PBrain[i]);
        int big = 0;
        for (int s = 0; s < K.ANCESTORS_LAND + K.ANCESTORS_WATER; s++) if (maxUnits[s] >= 100) big++; else Assert.True(maxUnits[s] <= 2 * K.BRAIN_START, $"clade {s}: {maxUnits[s]} units");
        Assert.Equal(2, big);
        Assert.True(maxUnits[0] >= 100 && maxUnits[K.ANCESTORS_LAND] >= 100);   // one on land, one in water
    }

    [Fact] // NR-303: thirst rises on land only
    public void NoThirstInWater()
    {
        var w = new World(256, 14); Terrain.Build(w);
        int a = Make(w, 0, 0);
        int wc = -1; for (int c = 0; c < Map.FW * Map.FW && wc < 0; c++) if ((w.Cell[c] & (byte)Realm.Deep) != 0) wc = c;
        Assert.True(wc >= 0);
        w.X[a] = (wc % Map.FW + 0.5f) * K.FCELL; w.Y[a] = (wc / Map.FW + 0.5f) * K.FCELL;
        w.Genome[a * G.LEN + G.B_FINS] = 127; Body.Express(w, a);
        for (int t = 0; t < 50; t++) { Act(w, a, P.REST); w.IActs[a] = 0; Resolve.Run(w, 1, 0); }
        Assert.Equal(0f, w.Thirst[a]);
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
        // the budget is on gene shares; each food's assimilation is the share times its DigMax (Revision M)
        float Sh(int p) => w.PDiet[p] / Pools.DigMax[p] * 0.95f / (Pools.Fibrous(p) ? Body.Ferment(w.PM[i]) : 1f);   // fibre: less the size's fermentation (Revision P)
        float sum0 = 0; for (int p = 0; p < Pools.N; p++) sum0 += Sh(p) * Sh(p);
        Assert.InRange(MathF.Sqrt(sum0), K.DIET_BUDGET - 0.02f, K.DIET_BUDGET + 0.02f);   // L2 budget
        w.Genome[G.B_DIET + (int)Pool.Grass] = 127;        // specialise on grass
        w.Genome[G.B_SPEED] = 127;                         // faster
        Body.Express(w, i);
        float sum = 0; for (int p = 0; p < Pools.N; p++) sum += Sh(p) * Sh(p);
        Assert.InRange(MathF.Sqrt(sum), K.DIET_BUDGET - 0.06f, K.DIET_BUDGET + 0.02f);
        Assert.True(w.PDiet[(int)Pool.Grass] > grass0);
        Assert.True(w.PDiet[(int)Pool.Seed] < seed0);
        Assert.True(w.PVmax[i] > v0 * 1.4f);
        // the speed gene adds exactly its own cost to the basal rate (other costs unchanged)
        float extra = K.COST_SPEED * (Body.Factor(127, K.BODY_RANGE_TRAIT) - 1f) * K.C_BASAL * MathK.Pow(w.PM[i], K.EXP_BASAL);
        Assert.Equal(basal0 + extra, w.PBasal[i], 4);
    }

    [Fact] // every random ancestor can digest a primary producer available in its realm
    public void AncestorsCanDigestRealmProducers()
    {
        var w = new World(16, 1);
        foreach (bool land in new[] { true, false })
        {
            int producer = land ? (int)Pool.Grass : (int)Pool.Phyto;
            for (int seed = 0; seed < 32; seed++)
            {
                var body = new sbyte[G.NBODY];
                Clades.AncestorBody(body, (uint)seed, seed, land);
                body.CopyTo(new Span<sbyte>(w.Genome, G.BODY, G.NBODY));
                Body.Express(w, 0);

                Assert.True(Body.Reachable((Realm)w.PRealm[0], producer), $"seed {seed}, land {land}: producer not in realm");
                Assert.True(w.PDiet[producer] > 0f, $"seed {seed}, land {land}: producer not digestible");
            }
        }
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
        w.Energy[a] = 0.15f * w.EmaxOf(a);               // below GROW_RESERVE: maintenance only
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
            w.Genome[i * G.LEN + G.B_BRAIN] = Body.UnitsGene(12);   // an ordinary founder brain (12 units)
            for (int p = 0; p < P.N; p++)
            {
                int b0 = p >= P.CALL_0 && p < P.CALL_0 + F.NSOUND ? K.FOUNDER_BIAS_CALL : K.FOUNDER_BIAS;   // the 8 calls together start as rare as one primitive
                Assert.InRange(w.Genome[i * G.LEN + G.W2 + p * G.NIN + G.NH + F.BIAS], b0 - K.FOUNDER_BIAS_NOISE, b0 + K.FOUNDER_BIAS_NOISE);
            }
            w.Spc[i] = (byte)(i % K.CLADES); w.StageOf[i] = St.Adult;
            Body.Express(w, i); Brain.InitLife(w, i);
            f[F.BIAS] = 1; for (int k = 1; k < F.N; k++) f[k] = rs.Next();
            Brain.Forward(w, i, f, y, z, g);
            for (int p = 0; p < P.N; p++) { sum += g[p]; n++; }
        }
        Assert.True(sum / n < 0.13, $"mean founder gain {sum / n:F3}");
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

    [Fact] // Revision K: seasonal ponds fill when the ground is wet and dry when it is dry; drinking water is measured to them
    public void PondsFillInTheWetAndDryInTheDry()
    {
        // the law of season and rain alone (Revision O: with a climate, warm ponds also evaporate and cold ones freeze;
        // ClimateTests covers that)
        float climate = Nature.Climate; Nature.Climate = 0f;
        try { PondsFillCore(); } finally { Nature.Climate = climate; }
    }

    static void PondsFillCore()
    {
        using var sim = new Simulation(1024, 4, 1, empty: true);
        var w = sim.W; int n = Map.FW * Map.FW;
        int sites = 0; for (int c = 0; c < n; c++) sites += w.PondSite[c];
        Assert.True(sites > 0, "no pond sites");
        w.RainAnom = 0.5f;   // a wet year
        Ponds.Update(w);
        Assert.True(Ponds.Full(w) > sites / 2, $"{Ponds.Full(w)} of {sites} ponds full in a wet year");   // the season runs by latitude: the driest rows stay dry
        float far = 0; for (int c = 0; c < n; c++) { Assert.True(w.DistDrink[c] <= w.DistWater[c] + 1f, $"cell {c}: drink {w.DistDrink[c]}, water {w.DistWater[c]}"); far = MathF.Max(far, w.DistWater[c] - w.DistDrink[c]); }
        Assert.True(far > 5f, "ponds bring no water closer");
        w.RainAnom = -0.5f;  // a drought
        Ponds.Update(w);
        Assert.Equal(0, Ponds.Full(w));
        for (int c = 0; c < n; c++) Assert.Equal(w.DistWater[c], w.DistDrink[c]);
    }

    [Fact] // Revision K: green food and prey relieve thirst, dry seed much less; big bodies and the water-economy gene dry out slower
    public void WetFoodRelievesThirstAndWaterEconomySlowsDrying()
    {
        var w = new World(64, 1);
        for (int i = 0; i < 3; i++)
        {
            Brain.RandomGenome(w.GenomeOf(i), 1, 0, 1);
            w.Spc[i] = 0; w.StageOf[i] = St.Adult;
        }
        w.Genome[1 * G.LEN + G.B_WATER] = 127; w.Genome[0 * G.LEN + G.B_WATER] = -127; w.Genome[2 * G.LEN + G.B_WATER] = -127;
        w.Genome[2 * G.LEN + G.B_MASS] = 127; w.Genome[0 * G.LEN + G.B_MASS] = 0; w.Genome[1 * G.LEN + G.B_MASS] = 0;
        for (int i = 0; i < 3; i++) { Body.Express(w, i); Brain.InitLife(w, i); }
        Assert.True(w.PThirstK[1] < 0.5f * w.PThirstK[0], $"water economy {w.PThirstK[1]:F2} vs {w.PThirstK[0]:F2}");
        Assert.True(w.PThirstK[2] < w.PThirstK[0], "a bigger body dries out no slower");
        w.Moisture[0] = 1f;
        float Relief(int pool) { w.Thirst[0] = 1f; Resolve.Ingest(w, 0, pool, 0.05f * w.MassOf(0), 1f, w.EmaxOf(0), 0); return 1f - w.Thirst[0]; }
        float grass = Relief((int)Pool.Grass), seed = Relief((int)Pool.Seed), meat = Relief((int)Pool.Meat);
        Assert.True(grass > 0.01f && meat > 0.01f, $"grass {grass:F3}, meat {meat:F3}");
        // dry seed holds little water, but digesting it makes some: how desert seed-eaters live without drinking
        Assert.True(seed < grass && seed > 0.2f * grass, $"seed {seed:F3} vs grass {grass:F3}");
        w.Moisture[0] = 0.1f;
        Assert.True(Relief((int)Pool.Grass) < 0.6f * grass, "dry-season grass is as wet as green grass");
    }

    [Fact] // Revision M: a sound made nearby is heard on the next step, and so is the person's voice; none has a built-in meaning
    public void CallsAndThePersonsVoiceAreHeard()
    {
        using var sim = new Simulation(1024, 5, 1, empty: true);
        var w = sim.W;
        Life.RandomSite(w, Realm.Land, 77u, out float x, out float y);
        int a = Life.Found(w, 0, ReadOnlySpan<sbyte>.Empty, true, 1u, x, y), b = Life.Found(w, 0, ReadOnlySpan<sbyte>.Empty, true, 2u, x, y);
        Assert.True(a >= 0 && b >= 0);
        w.Sp[0].Used = w.Sp[0].Alive = true;
        foreach (int c in new[] { a, b }) { w.Genome[c * G.LEN + G.B_VOICE] = 127; Body.Express(w, c); }   // both have a voice
        sim.Step();                                     // settle positions and the spatial hash
        w.Call[a * F.MAX_SYL] = 3;                      // a made sound 3 (index 2) last step
        sim.Step();
        Assert.True(w.Feat[b * F.N + F.HEAR_0 + 2] > 0f, "the neighbour did not hear sound 3");
        for (int k = 0; k < F.NSOUND; k++) if (k != 2) Assert.True(w.Feat[b * F.N + F.HEAR_0 + k] <= 0f);
        // the person's voice: sound 6 spoken beside b is heard on the next step, and b's answer is counted
        w.UserCallX = w.X[b] + 1f; w.UserCallY = w.Y[b]; w.UserCall[0] = 6; w.UserCallStep = w.Step - 1; w.UserCallId++;
        sim.Step();
        Assert.True(w.Feat[b * F.N + F.HEAR_0 + 5] > 0f, "the animal did not hear the person");
        long answers = 0; foreach (var v in w.T.UserResp) answers += v;
        Assert.True(answers >= 1);
    }

    [Fact] // Revision M: a mute animal's call makes no sound
    public void MuteAnimalsMakeNoSound()
    {
        using var sim = new Simulation(1024, 6, 1, empty: true);
        var w = sim.W;
        Life.RandomSite(w, Realm.Land, 78u, out float x, out float y);
        int a = Life.Found(w, 0, ReadOnlySpan<sbyte>.Empty, true, 1u, x, y);
        w.Sp[0].Used = w.Sp[0].Alive = true;
        Assert.True(w.PVoice[a] < K.VOICE_MIN);         // ancestors without the voice are mute
        for (int p = 0; p < F.NSOUND; p++) w.Genome[a * G.LEN + G.W2 + (P.CALL_0 + p) * G.NIN + G.NH + F.BIAS] = 127;   // it tries to call all the time
        Brain.InitLife(w, a);
        for (int t = 0; t < 20; t++) { sim.Step(); Assert.Equal(0, w.Call[a * F.MAX_SYL]); }
    }

    [Fact] // Revision N: each ancestor starts in its own home range, apart from the others
    public void AncestorsStartInTheirOwnHomeRanges()
    {
        using var sim = new Simulation(8192, 11, 1);
        var w = sim.W;
        int n = K.ANCESTORS_LAND + K.ANCESTORS_WATER;
        // the circular mean of each clade's positions on the torus, and how far its members are from it
        var cx = new double[n]; var cy = new double[n]; var sx = new double[n]; var sy = new double[n]; var cnt = new int[n];
        double k2 = 2 * Math.PI / Map.W;
        for (int i = 0; i < w.Cap; i++)
        {
            if (w.StageOf[i] == St.Dead || w.Spc[i] >= n) continue;
            int s = w.Spc[i]; cnt[s]++;
            cx[s] += Math.Cos(w.X[i] * k2); sx[s] += Math.Sin(w.X[i] * k2); cy[s] += Math.Cos(w.Y[i] * k2); sy[s] += Math.Sin(w.Y[i] * k2);
        }
        var mx = new float[n]; var my = new float[n];
        for (int s = 0; s < n; s++) { if (cnt[s] == 0) continue; mx[s] = World.Wrap((float)(Math.Atan2(sx[s], cx[s]) / k2)); my[s] = World.Wrap((float)(Math.Atan2(sy[s], cy[s]) / k2)); }
        int inside = 0, total = 0;
        for (int i = 0; i < w.Cap; i++)
        {
            if (w.StageOf[i] == St.Dead || w.Spc[i] >= n) continue;
            int s = w.Spc[i]; float dx = World.Delta1(w.X[i] - mx[s]), dy = World.Delta1(w.Y[i] - my[s]);
            total++; if (dx * dx + dy * dy <= MathF.Pow(2.2f * K.HOME_R * Map.W, 2)) inside++;
        }
        Assert.True(inside > 0.9 * total, $"only {inside} of {total} founders are within their home range");
        float closest = float.MaxValue;
        for (int a = 0; a < n; a++) for (int b = a + 1; b < n; b++)
            if (cnt[a] > 0 && cnt[b] > 0) { float dx = World.Delta1(mx[a] - mx[b]), dy = World.Delta1(my[a] - my[b]); closest = MathF.Min(closest, MathF.Sqrt(dx * dx + dy * dy)); }
        Assert.True(closest > K.HOME_R * Map.W, $"two home ranges are only {closest:F0} apart");
    }

    [Fact] // the user (2026-10-30): size classes with their own floors (0.1, 1, 3, 10), inherited, rarely changing
    public void SizeClassesHaveFloorsThatRarelyChange()
    {
        var w = new World(16, 1);
        for (int c = 0; c < K.FLOOR_CLASSES; c++)
        {
            w.Genome[G.B_FLOOR] = (sbyte)c;
            w.Genome[G.B_MASS] = -128; Body.Express(w, 0);
            Assert.Equal(Body.Floors[c], w.PM[0], 3);                 // the smallest mass gene gives the class floor
            w.Genome[G.B_MASS] = 127; Body.Express(w, 0);
            Assert.True(w.PM[0] > 30f);                                 // above the floor, size is free up to MASS_HI
        }
        // the class copies exactly through mutation, but steps a class about FLOOR_MUT of the time
        var g = new sbyte[G.LEN]; int steps = 0, n = 200000;
        for (uint k = 0; k < n; k++) { g[G.B_FLOOR] = 2; Brain.Mutate(g, 7, 0, k); if (g[G.B_FLOOR] != 2) { steps++; Assert.InRange(g[G.B_FLOOR], 1, 3); } }
        Assert.InRange(steps, 1, (int)(n * K.FLOOR_MUT * 3));
        // ancestors are spread over the classes
        using var sim = new Simulation(8192, 13, 1);
        var seen = new System.Collections.Generic.HashSet<int>();
        for (int i = 0; i < sim.W.Cap; i++) if (sim.W.StageOf[i] != St.Dead) seen.Add(sim.W.Genome[i * G.LEN + G.B_FLOOR]);
        Assert.Equal(K.FLOOR_CLASSES, seen.Count);
    }

    [Fact] // a 256-unit brain really has 256 units (a byte held 255, so 256 read as 0), and a big newborn brain is as calm as a small one
    public void BigNewbornBrainsAreRealAndCalm()
    {
        var w = new World(256, 2); var rs = new Rng.Seq(3);
        var f = new float[F.N]; var y = new float[G.NIN]; var z = new float[P.N]; var g = new float[P.N];
        (double gain, double strike) Measure(int units)
        {
            double sum = 0, st = 0; int n = 0;
            for (int i = 0; i < 200; i++)
            {
                Brain.RandomGenome(w.GenomeOf(i), 2, 0, (uint)i);
                w.Genome[i * G.LEN + G.B_BRAIN] = Body.UnitsGene(units);
                w.Spc[i] = 0; w.StageOf[i] = St.Adult; Body.Express(w, i); Brain.InitLife(w, i);
                Assert.Equal(units, (int)w.PBrain[i]);
                for (int t = 0; t < 5; t++) { f[F.BIAS] = 1; for (int k = 1; k < F.N; k++) f[k] = rs.Next(); Brain.Forward(w, i, f, y, z, g); }
                for (int p = 0; p < P.N; p++) { sum += g[p]; n++; }
                st += g[P.STRIKE];
            }
            return (sum / n, st / 200);
        }
        var small = Measure(24); var big = Measure(G.NH);
        Assert.True(big.gain < small.gain * 1.15, $"big brain mean gain {big.gain:F3} vs {small.gain:F3}");
        Assert.True(big.strike < small.strike * 1.25, $"big brain P(strike) {big.strike:F3} vs {small.strike:F3}");
    }
}
