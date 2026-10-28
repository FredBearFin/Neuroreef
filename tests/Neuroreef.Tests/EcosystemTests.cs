// Revision C mechanisms: reflexes (NR-327), parental care (NR-328), disease (NR-329), weather years (NR-330)
// and Revision B gene banks still loading (NR-212).

using System;
using System.IO;
using Xunit;

namespace Neuroreef.Tests;

public class EcosystemTests
{
    static int Animal(World w, int species, bool adult, uint key)
    {
        int c = Life.Found(w, species, ReadOnlySpan<sbyte>.Empty, adult, key);
        Assert.True(c >= 0);
        return c;
    }

    [Fact] // NR-327: a reflex gene links a need met by an opportunity to an action, and learning does not touch it
    public void ReflexLinksNeedAndOpportunityToAction()
    {
        using var sim = new Simulation(16384, 61, 1, empty: true);
        var w = sim.W;
        int c = Animal(w, 0, true, 1);
        w.GenomeOf(c).Clear();
        w.Genome[c * G.LEN + G.REFLEX + P.EAT * G.NC + Reflex.HUNGRY_FOOD] = 127;
        Brain.InitLife(w, c);
        Span<float> f = stackalloc float[F.N], y = stackalloc float[G.NIN], z = stackalloc float[P.N], g = stackalloc float[P.N];
        f[F.BIAS] = 1; f[F.HUNGER] = 1; f[F.FOOD_HERE] = 1;
        Brain.Forward(w, c, f, y, z, g);
        Assert.True(g[P.EAT] > 0.99f, $"EAT {g[P.EAT]}");
        Assert.Equal(0.5f, g[P.DRINK], 3);
        f[F.FOOD_HERE] = 0;                       // hungry but nothing to eat: the reflex is silent
        Brain.Forward(w, c, f, y, z, g);
        Assert.Equal(0.5f, g[P.EAT], 3);
        // the reflex genes are not part of the learned weights
        Assert.Equal(G.W2N, P.N * G.NIN);
        Assert.True(G.REFLEX >= G.W2 + G.W2N);
    }

    [Fact] // NR-328: a mammal mother in contact feeds her young; the energy she loses is what they gain / CARE_EFF
    public void MothersFeedTheirYoungAndEnergyIsConserved()
    {
        using var sim = new Simulation(16384, 62, 1, empty: true);
        var w = sim.W;
        Assert.Equal(1f, w.Sp[0].Care);           // Drifters: mammals
        Assert.Equal(0f, w.Sp[16].Care);          // Minnows: fish give no care
        int m = Animal(w, 0, true, 2), j = Animal(w, 0, false, 3);
        w.MotherSlot[j] = m; w.MotherSerial[j] = w.Serial[m];
        w.X[j] = w.X[m]; w.Y[j] = w.Y[m]; w.Age[j] = 0;
        w.Energy[m] = 0.9f * w.EmaxOf(m); w.Energy[j] = 0.2f * w.EmaxOf(j);
        float em = w.Energy[m], ej = w.Energy[j];
        Life.Run(w, new int[w.NS]);
        float gave = w.Energy[j] - ej, lost = em - w.Energy[m];
        Assert.True(gave > 0, "the young got nothing");
        Assert.Equal(gave / K.CARE_EFF, lost, 5);
        Assert.True(gave <= K.CARE_RATE * w.PBasal[m] * w.PCare[m] + 1e-6f);
        // far from the mother: no care
        w.X[j] = World.Wrap(w.X[m] + 50); ej = w.Energy[j];
        Life.Run(w, new int[w.NS]);
        Assert.Equal(ej, w.Energy[j]);
    }

    [Fact] // NR-329: contact with infected conspecifics infects; infection ends in recovery with immunity, or death
    public void DiseaseSpreadsByContactAndLeavesImmunity()
    {
        using var sim = new Simulation(16384, 63, 1, empty: true);
        var w = sim.W;
        int a = Animal(w, 0, true, 4);
        w.Energy[a] = w.EmaxOf(a);
        var act = new int[w.NS];
        for (int t = 0; t < 400 && w.Infect[a] == 0; t++) { w.IInfNear[a] = 5; Life.Run(w, act); w.Step++; }
        Assert.True(w.Infect[a] > 0, "never infected despite contacts");
        Assert.Equal(1, w.T.Infections[0]);
        for (int t = 0; t < K.DIS_STEPS + 2 && w.StageOf[a] != St.Dead; t++) { w.IInfNear[a] = 0; w.Energy[a] = w.EmaxOf(a); Life.Run(w, act); w.Step++; }
        if (w.StageOf[a] != St.Dead) { Assert.Equal(0f, w.Infect[a]); Assert.True(w.Immune[a] > 0); }
        else Assert.Equal(1, w.T.Deaths[0 * Cause.N + Cause.Disease]);
        // with no contacts and no immunity, infection is rare (spillover only)
        int b = Animal(w, 0, true, 5); w.Immune[b] = 0; int got = 0;
        for (int t = 0; t < 200; t++) { w.IInfNear[b] = 0; Life.Run(w, act); w.Step++; if (w.Infect[b] > 0) got++; }
        Assert.True(got == 0 || w.T.Infections[0] <= 2);
    }

    [Fact] // NR-330: every year has its own rainfall; branches of one prehistory (same terrain seed) share the climate
    public void WeatherYearsVaryAndAreSharedByBranches()
    {
        var w1 = new World(1024, 71, terrainSeed: 70); var w2 = new World(1024, 72, terrainSeed: 70);
        double sum = 0, sum2 = 0; int n = 30;
        for (int y = 0; y < n; y++)
        {
            w1.Step = w2.Step = (ulong)y * K.T_SEASON;
            Weather.Update(w1); Weather.Update(w2);
            Assert.Equal(w1.RainAnom, w2.RainAnom);
            Assert.InRange(w1.RainAnom, -K.RAIN_MAX, K.RAIN_MAX);
            sum += w1.RainAnom; sum2 += w1.RainAnom * w1.RainAnom;
        }
        double sd = Math.Sqrt(sum2 / n - (sum / n) * (sum / n));
        Assert.True(sd > 0.1, $"rainfall hardly varies: sd {sd}");
    }

    [Fact] // NR-212: a gene bank from Revision B (shorter genome) loads; the new genes start at 0
    public void RevisionBGeneBanksStillLoad()
    {
        var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            bw.Write("NRBK"u8.ToArray()); bw.Write(GeneBank.Version); bw.Write((ushort)Roster.Count); bw.Write((ushort)G.LEN_B);
            bw.Write((uint)1024); bw.Write(5UL); bw.Write(9u);
            for (int s = 0; s < Roster.Count; s++)
            {
                bw.Write((ushort)1); bw.Write(1f);
                for (int k = 0; k < G.LEN_B; k++) bw.Write((sbyte)3);
            }
        }
        ms.Position = 0;
        var d = GeneBank.Load(ms);
        var g = d.Genomes[4][0];
        Assert.Equal(G.LEN, g.Length);
        Assert.Equal(3, g[G.LEN_B - 1]);
        for (int k = G.LEN_B; k < G.LEN; k++) Assert.Equal(0, g[k]);
    }
}
