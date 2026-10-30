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

    [Fact] // NR-327: every founder carries the starter kit, and it drives the right action at full need
    public void FoundersCarryTheStarterKit()
    {
        using var sim = new Simulation(16384, 64, 1, empty: true);
        var w = sim.W;
        int c = Animal(w, 0, true, 6);           // a random founder (Found draws a random genome)
        foreach (var (ci, p) in Reflex.Kit)
            Assert.True(w.Genome[c * G.LEN + G.REFLEX + p * G.NC + ci] >= K.KIT_W - K.FOUNDER_W2, $"kit {Reflex.Names[ci]} -> {P.Names[p]}");
        Span<float> f = stackalloc float[F.N], y = stackalloc float[G.NIN], z = stackalloc float[P.N], g = stackalloc float[P.N];
        f[F.BIAS] = 1; f[F.THIRST] = 1; f[F.WATER_CLOSE] = 1;
        Brain.Forward(w, c, f, y, z, g);
        Assert.True(g[P.DRINK] > 0.7f, $"DRINK {g[P.DRINK]}");
        Assert.True(g[P.SEEK_WATER] > 0.7f, $"SEEK_WATER {g[P.SEEK_WATER]}");
        f.Clear(); f[F.BIAS] = 1; f[F.HUNGER] = 1; f[F.FOOD_HERE] = 1;
        Brain.Forward(w, c, f, y, z, g);
        Assert.True(g[P.EAT] > 0.7f, $"EAT {g[P.EAT]}");
        f.Clear(); f[F.BIAS] = 1; f[F.READY] = 1; f[F.MATE_CLOSE] = 1;
        Brain.Forward(w, c, f, y, z, g);
        Assert.True(g[P.MATE] > 0.7f, $"MATE {g[P.MATE]}");
        f[F.MATE_CLOSE] = 0;
        Brain.Forward(w, c, f, y, z, g);
        Assert.True(g[P.SEEK_MATE] > 0.7f, $"SEEK_MATE {g[P.SEEK_MATE]}");
        Assert.True(g[P.LAY] > 0.7f, $"LAY {g[P.LAY]}");
    }

    [Fact] // NR-205: natural selection works: in a births-only world a heritable trait that helps animals eat spreads
    public void SelectionImprovesAHeritableTrait()
    {
        // averaged over three independent populations: one population of a few hundred can drift either way by chance,
        // and floating-point differences between platforms send a single run down different paths
        double d = 0; long births = 0; var runs = new System.Text.StringBuilder();
        foreach (uint seed in new uint[] { 65, 66, 67 })
        {
            var (s0, s1, b) = SelectionRun(seed);
            Assert.True(b > 50, $"seed {seed}: only {b} births: too few generations to test selection");
            d += (s1 - s0) / 3; births += b; runs.Append($"{s0:F2}->{s1:F2} ");
        }
        Assert.True(d > 0.10, $"the eating allele did not spread: mean rise {d:F2} ({runs}) after {births} births");
    }

    static (double s0, double s1, long births) SelectionRun(uint seed)
    {
        using var sim = new Simulation(8192, seed, 1, empty: true);
        var w = sim.W;
        int eat = G.REFLEX + P.EAT * G.NC + Reflex.HUNGRY_FOOD;
        // 300 Drifter founders in groups; half lose the eat-when-hungry-at-food reflex (the allele is that one gene)
        for (int k = 0; k < 300; k++)
        {
            int c = Life.Found(w, 0, ReadOnlySpan<sbyte>.Empty, true, 9000u + (uint)k, (k / 8) * 37f % Map.W, (k / 8) * 91f % Map.W);
            if (c < 0) continue;
            // a fixed ordinary grazer body for every founder (the test is about selection, not about a random body's luck):
            // mass 1, walking legs, grass and seeds, sexual live birth, two young of 15% adult mass
            var b = w.GenomeOf(c).Slice(G.BODY, G.NBODY);
            b.Clear();
            b[G.B_MASS - G.BODY] = G.ToGeneLog(1f, K.MASS_LO, K.MASS_HI);
            b[G.B_LEGS - G.BODY] = 70; b[G.B_FINS - G.BODY] = -127; b[G.B_WINGS - G.BODY] = -127;
            b[G.B_STRIKE - G.BODY] = -127; b[G.B_ARMOUR - G.BODY] = -90;
            for (int p = 0; p < Pools.N; p++) b[G.B_DIET - G.BODY + p] = -127;
            b[G.B_DIET - G.BODY + (int)Pool.Grass] = 127; b[G.B_DIET - G.BODY + (int)Pool.Seed] = 60;
            b[G.B_SEX - G.BODY] = 60; b[G.B_LIVE - G.BODY] = 60;
            b[G.B_LITTER - G.BODY] = G.ToGene(MathF.Log(2f) / MathF.Log(K.LITTER_MAX) * 2f - 1f, -1f, 1f);
            b[G.B_BIRTH - G.BODY] = G.ToGeneLog(0.15f, K.BIRTH_LO, K.BIRTH_HI);
            b[G.B_CARE - G.BODY] = 0;
            b[G.B_BRAIN - G.BODY] = Body.UnitsGene(12);   // an ordinary 12-unit brain
            Body.Express(w, c);
            w.Energy[c] = 0.7f * w.EmaxOf(c);
            if (k % 2 == 1) w.Genome[c * G.LEN + eat] = 0;
        }
        double Share()
        {
            int n = 0, strong = 0;
            for (int i = 0; i < w.Cap; i++)
                if (w.StageOf[i] >= St.Juvenile && w.Spc[i] == 0) { n++; if (w.Genome[i * G.LEN + eat] >= K.KIT_W / 2) strong++; }
            return n == 0 ? double.NaN : (double)strong / n;
        }
        double s0 = Share();
        for (int t = 0; t < 3000; t++) sim.Step();
        double s1 = Share();
        return (s0, s1, w.T.Births[0]);
    }

    [Fact] // NR-328: a caring mother in contact feeds her young; the energy she loses is what they gain / CARE_EFF
    public void MothersFeedTheirYoungAndEnergyIsConserved()
    {
        using var sim = new Simulation(16384, 62, 1, empty: true);
        var w = sim.W;
        int m = Animal(w, 0, true, 2), j = Animal(w, 0, false, 3);
        w.Genome[m * G.LEN + G.B_CARE] = 127; Body.Express(w, m);   // a caring mother (care is a gene, like any other)
        Assert.True(w.PCare[m] > 0.99f);
        w.MotherSlot[j] = m; w.MotherSerial[j] = w.Serial[m];
        w.X[j] = w.X[m]; w.Y[j] = w.Y[m]; w.Age[j] = 0; w.Grown[j] = 0;
        w.Energy[m] = 0.9f * w.EmaxOf(m); w.Energy[j] = 0.2f * w.EmaxOf(j);
        float em = w.Energy[m], ej = w.Energy[j];
        Life.Run(w, new int[w.NS]);
        float gave = w.Energy[j] - ej, lost = em - w.Energy[m];
        Assert.True(gave > 0, "the young got nothing");
        Assert.InRange(MathF.Abs(gave / K.CARE_EFF - lost), 0f, 2e-6f * MathF.Max(1f, em + ej));
        Assert.True(gave <= K.CARE_RATE * w.PBasal[m] * w.PCare[m] + 1e-6f);
        // far from the mother: no care
        w.X[j] = World.Wrap(w.X[m] + 50); ej = w.Energy[j];
        Life.Run(w, new int[w.NS]);
        Assert.Equal(ej, w.Energy[j]);
    }

    [Fact] // DESIGN §9.6: matching alleles: a strain infects a host whose recognition genes match its key far more often
    public void StrainsInfectMatchingHostsMore()
    {
        int Infections(bool match)
        {
            using var sim = new Simulation(16384, 64, 1, empty: true);
            var w = sim.W;
            int a = Animal(w, 0, true, 4), src = Animal(w, 0, true, 5);
            w.Infect[src] = K.DIS_STEPS; w.Virulence[src] = 1f;
            for (int k = 0; k < G.NMHC; k++)
            {
                w.Strain[src * G.NMHC + k] = w.Genome[a * G.LEN + G.B_MHC + k];
                if (!match) w.Strain[src * G.NMHC + k] = (sbyte)(w.Genome[a * G.LEN + G.B_MHC + k] > 0 ? -127 : 127);
            }
            var act = new int[w.NS]; int n = 0;
            for (int t = 0; t < 3000; t++)
            {
                w.IInfNear[a] = 3; w.IVirNear[a] = 1f; w.IStrainSrc[a] = src; w.Infect[src] = K.DIS_STEPS;
                w.Energy[a] = w.EmaxOf(a); w.Energy[src] = w.EmaxOf(src);
                Life.Run(w, act); w.Step++;
                if (w.Infect[a] > 0) { n++; w.Infect[a] = 0; w.Immune[a] = 0; }
                if (w.StageOf[a] == St.Dead) break;
            }
            return n;
        }
        int hit = Infections(true), miss = Infections(false);
        Assert.True(hit > 5 * Math.Max(1, miss), $"matching {hit}, mismatched {miss}");
    }

    [Fact] // NR-329: contact with infected conspecifics infects; infection ends in recovery with immunity, or death
    public void DiseaseSpreadsByContactAndLeavesImmunity()
    {
        using var sim = new Simulation(16384, 63, 1, empty: true);
        var w = sim.W;
        int a = Animal(w, 0, true, 4);
        w.Energy[a] = w.EmaxOf(a);
        var act = new int[w.NS];
        for (int t = 0; t < 400 && w.Infect[a] == 0; t++) { w.IInfNear[a] = 5; w.IVirNear[a] = 1f; Life.Run(w, act); w.Step++; }
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

    [Fact] // NR-212: gene banks from before open-ended evolution (designed species) are refused with a clear message
    public void OldGeneBanksAreRefused()
    {
        var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            bw.Write("NRBK"u8.ToArray()); bw.Write((ushort)1); bw.Write((ushort)20); bw.Write((ushort)1705);
            bw.Write((uint)1024); bw.Write(5UL); bw.Write(9u);
        }
        ms.Position = 0;
        var ex = Assert.Throws<InvalidDataException>(() => GeneBank.Load(ms));
        Assert.Contains("prehistory", ex.Message);
    }

    // ---- speciation (NR-333): a clade whose body genes have separated into two clusters splits; a single cluster does not

    delegate void BodyEdit(int k, Span<sbyte> body);

    static World CladeWorld(uint seed, BodyEdit body)
    {
        var w = new World(4096, seed); Terrain.Build(w);
        int clade = Clades.NewClade(w, -1, 0);
        for (int k = 0; k < 240; k++)
        {
            int c = Life.Found(w, clade, ReadOnlySpan<sbyte>.Empty, true, 100u + (uint)k);
            Assert.True(c >= 0);
            body(k, w.GenomeOf(c).Slice(G.BODY, G.NBODY));
            Body.Express(w, c);
        }
        return w;
    }

    [Fact]
    public void DivergedCladeSplitsIntoTwoSpecies()
    {
        var w = CladeWorld(81, (k, b) =>
        {
            // two forms: small swift seed-eaters and large armoured browsers (plus a little individual noise)
            bool form = k % 2 == 0;
            b[G.B_MASS - G.BODY] = (sbyte)(form ? -90 + k % 7 : 90 - k % 5);
            b[G.B_ARMOUR - G.BODY] = (sbyte)(form ? -100 : 100);
            b[G.B_SPEED - G.BODY] = (sbyte)(form ? 80 : -80);
        });
        w.Step = K.SPEC_EVERY;
        Clades.Update(w);
        Assert.Equal(1, w.T.Splits);
        int a = 0, b2 = 0;
        for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] != St.Dead) { if (w.Spc[i] == 0) a++; else b2++; }
        Assert.Equal(120, a); Assert.Equal(120, b2);
        // two forms this far apart in their genes are a species, not a subspecies
        int x = -1, y = -1;
        for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] != St.Dead) { if (w.Spc[i] == 0 && x < 0) x = i; if (w.Spc[i] != 0 && y < 0) y = i; }
        Assert.NotEqual(w.Spc[x], w.Spc[y]);
        Assert.Equal(0, w.Sp[w.Spc[y]].Parent);
        Assert.False(w.Sp[w.Spc[y]].Sub);
        Assert.False(string.IsNullOrEmpty(w.Sp[w.Spc[y]].SplitWhy));
    }

    [Fact] // Revision L: two forms that look clearly different but still breed together are a subspecies
    public void VisibleFormsThatStillInterbreedAreASubspecies()
    {
        var w = CladeWorld(83, (k, b) =>
        {
            bool form = k % 2 == 0;
            b[G.B_MASS - G.BODY] = (sbyte)(form ? -30 + k % 5 : 30 - k % 5);   // one form about 3x the mass of the other
        });
        w.Step = K.SPEC_EVERY;
        Clades.Update(w);
        Assert.Equal(1, w.T.Splits);
        int nc = -1; for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] != St.Dead && w.Spc[i] != 0) { nc = w.Spc[i]; break; }
        Assert.True(w.Sp[nc].Sub, "should still be one species");
        Assert.Contains("size", w.Sp[nc].SplitWhy.Replace("bigger", "size").Replace("smaller", "size"));
    }

    [Fact] // Revision L: forms that differ only in what cannot be seen (speed, diet) are not named apart
    public void InvisibleDifferencesDoNotSplit()
    {
        var w = CladeWorld(84, (k, b) =>
        {
            bool form = k % 2 == 0;
            b[G.B_SPEED - G.BODY] = (sbyte)(form ? 100 : -100);
            b[G.B_DIET - G.BODY] = (sbyte)(form ? 100 : -100);
        });
        w.Step = K.SPEC_EVERY;
        Clades.Update(w);
        Assert.Equal(0, w.T.Splits);
    }

    [Fact]
    public void OneClusterDoesNotSplit()
    {
        var w = CladeWorld(82, (k, b) =>
        {
            b[G.B_MASS - G.BODY] = (sbyte)(k % 11 - 5);    // one population with ordinary variation
            b[G.B_SPEED - G.BODY] = (sbyte)(k % 7 - 3);
        });
        w.Step = K.SPEC_EVERY;
        Clades.Update(w);
        Assert.Equal(0, w.T.Splits);
    }
}
