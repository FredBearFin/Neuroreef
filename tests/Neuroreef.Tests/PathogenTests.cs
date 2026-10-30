// Revision R: pathogens as evolving agents with no brain (Pathogens.cs): within-host growth against the immune
// response, antigen-specific memory, quorum sensing, the environmental reservoir and its rivers, and evolution under
// transmission.

using System;
using Xunit;

namespace Neuroreef.Tests;

public class PathogenTests
{
    static int Animal(World w, uint key)
    {
        int c = Life.Found(w, 0, ReadOnlySpan<sbyte>.Empty, true, key);
        Assert.True(c >= 0);
        w.Energy[c] = w.EmaxOf(c);
        return c;
    }

    /// <summary>A strain genome: replication, toxin, quorum threshold, persistence, breadth (bytes), equal route weights.</summary>
    static byte[] Gene(int rep, int tox, int qs, int pers = 0, int breadth = 0, int line = 0)
    {
        var g = new byte[PG.N];
        g[PG.REP] = (byte)rep; g[PG.TOX] = (byte)tox; g[PG.QS] = (byte)qs; g[PG.PERS] = (byte)pers; g[PG.BREADTH] = (byte)breadth; g[PG.LINE] = (byte)line;
        g[PG.CONTACT] = g[PG.ENV] = g[PG.WATER] = g[PG.DUNG] = 64;
        return g;
    }

    /// <summary>The host's own recognition genes as an antigen: a perfect fit.</summary>
    static sbyte[] HostKey(World w, int h)
    {
        var k = new sbyte[G.NMHC];
        for (int j = 0; j < G.NMHC; j++) k[j] = w.Genome[h * G.LEN + G.B_MHC + j];
        return k;
    }

    static void SetMhc(World w, int h, sbyte v) { for (int j = 0; j < G.NMHC; j++) w.Genome[h * G.LEN + G.B_MHC + j] = v; }

    [Fact] // within-host dynamics: the load grows from the founding dose, the immune response expands and clears it, memory is left
    public void LoadGrowsThenIsCleared()
    {
        using var sim = new Simulation(4096, 81, 1, empty: true);
        var w = sim.W;
        int a = Animal(w, 1);
        Pathogens.Infect(w, a, Gene(rep: 200, tox: 0, qs: 0), 0, HostKey(w, a), 0, false);
        Assert.Equal(K.PG_INOC, w.PgLoad[a]);
        var act = new int[w.NS];
        float peak = 0, peakE = 0; int cleared = -1;
        for (int t = 0; t < 400 && cleared < 0; t++)
        {
            w.Energy[a] = w.EmaxOf(a);
            Life.Run(w, act); w.Step++;
            Array.Clear(w.ResLoad);   // no reinfection from what it shed
            peak = MathF.Max(peak, w.PgLoad[a]); peakE = MathF.Max(peakE, w.ImmEff[a]);
            if (w.PgLoad[a] <= 0f) cleared = t;
        }
        Assert.True(w.StageOf[a] != St.Dead, "a toxin-free strain killed its host");
        Assert.True(peak > 20 * K.PG_INOC, $"load peaked at only {peak}");
        Assert.True(peakE > 0.5f, $"immune response peaked at only {peakE}");
        Assert.True(cleared > 5, $"cleared at step {cleared}");
        Assert.Equal(1f, w.ImmMem[a]);
        Assert.Equal(HostKey(w, a)[0], w.ImmKey[a * G.NMHC]);
        // the same strain again meets a recalled response at once, and a lower chance of infection
        Assert.True(Pathogens.Cross(w, a, HostKey(w, a), 0) > 0.99f);
    }

    [Fact] // antigen-specific immunity: memory of one antigen protects against it, not against a drifted one of equal fit
    public void AntigenDriftEscapesOldImmunity()
    {
        using var sim = new Simulation(4096, 82, 1, empty: true);
        var w = sim.W;
        int a = Animal(w, 2);
        SetMhc(w, a, 0);
        sbyte[] old = new sbyte[] { 40, 40, 40, 40 }, drifted = new sbyte[] { -40, -40, -40, -40 };   // the same distance from the host's MHC
        var g = Gene(rep: 128, tox: 0, qs: 128);
        Assert.Equal(Pathogens.Fit(w, a, old, 0, 0), Pathogens.Fit(w, a, drifted, 0, 0), 5);
        int Infections(sbyte[] key)
        {
            int n = 0;
            for (int t = 0; t < 3000; t++)
            {
                w.PgLoad[a] = 0; w.ImmEff[a] = 0;
                w.ImmMem[a] = 1f; Array.Copy(old, 0, w.ImmKey, a * G.NMHC, G.NMHC);   // it remembers the old antigen
                if (Pathogens.Expose(w, a, 0.5f, g, 0, key, 0, 999u)) n++;
                w.Step++;
            }
            w.PgLoad[a] = 0;
            return n;
        }
        int same = Infections(old), escape = Infections(drifted);
        Assert.True(Pathogens.Cross(w, a, old, 0) > 0.99f && Pathogens.Cross(w, a, drifted, 0) < 0.01f);
        Assert.True(escape > 20 * Math.Max(1, same), $"remembered antigen {same} infections, drifted {escape}");
    }

    [Fact] // quorum sensing: at the same low load, a strain whose threshold is above it stays stealthy: less toxin, shedding and detection
    public void HighQuorumThresholdStrainStaysHiddenAtLowLoad()
    {
        using var sim = new Simulation(4096, 83, 1, empty: true);
        var w = sim.W;
        int lo = Animal(w, 3), hi = Animal(w, 4);
        SetMhc(w, lo, 10); SetMhc(w, hi, 10);
        var key = HostKey(w, lo);
        Pathogens.Infect(w, lo, Gene(rep: 0, tox: 255, qs: 0), 0, key, 0, false);     // threshold PG_QS_LO: quorate at any load that matters
        Pathogens.Infect(w, hi, Gene(rep: 0, tox: 255, qs: 255), 0, key, 0, false);   // threshold PG_QS_HI: never quorate in one host
        w.PgLoad[lo] = w.PgLoad[hi] = 0.1f; w.ImmEff[lo] = w.ImmEff[hi] = 0.1f;
        Assert.True(Pathogens.Quorum(0.1f, Pathogens.Threshold(0)) > 0.99f);
        Assert.True(Pathogens.Quorum(0.1f, Pathogens.Threshold(255)) < 1e-4f);
        float shedLo = Pathogens.Shedding(w, lo), shedHi = Pathogens.Shedding(w, hi);
        w.Energy[lo] = w.Energy[hi] = 0f;   // no repair, so the damage made this step shows
        Life.Run(w, new int[w.NS]);
        Assert.True(shedHi < 0.15f * shedLo, $"shedding: quorate {shedLo}, stealthy {shedHi}");
        Assert.True(w.PgDamage[hi] < 0.15f * w.PgDamage[lo], $"damage: quorate {w.PgDamage[lo]}, stealthy {w.PgDamage[hi]}");
        Assert.True(w.ImmEff[hi] < w.ImmEff[lo], $"immune effector: quorate {w.ImmEff[lo]}, stealthy {w.ImmEff[hi]}");
        // and it is killed less: the stealthy load falls less (or grows more) than the quorate one
        Assert.True(w.PgLoad[hi] > w.PgLoad[lo], $"load: quorate {w.PgLoad[lo]}, stealthy {w.PgLoad[hi]}");
    }

    [Fact] // the reservoir: waterborne pathogens run down the river network, never up it
    [BigWorld]
    public void WaterbornePathogensTravelDownstream()
    {
        using var sim = new Simulation(64, 84, 1, empty: true);
        var w = sim.W;
        Pathogens.BuildNet(w);
        Assert.True(w.ResRivers > 100, $"only {w.ResRivers} river cells");
        // a river cell with at least 12 river cells below it
        int src = -1; var path = new int[13];
        for (int k = w.ResRivers - 1; k >= 0 && src < 0; k--)   // from the top of the network
        {
            int c = w.ResOrder[k], n = 1; path[0] = c;
            while (n < path.Length && w.ResRecv[path[n - 1]] >= 0) { path[n] = w.ResRecv[path[n - 1]]; n++; }
            if (n == path.Length) src = c;
        }
        Assert.True(src >= 0, "no river reach of 12 cells");
        for (int j = 1; j < path.Length; j++) Assert.True(w.Flow[path[j]] > w.Flow[path[j - 1]], "receivers gather more flow");
        Array.Clear(w.ResLoad);
        var g = Gene(rep: 0, tox: 0, qs: 0, pers: 255);
        Array.Copy(g, 0, w.ResGene, src * PG.N, PG.N);
        w.ResLoad[src] = 1f;
        for (int t = 0; t < 30; t++) { Pathogens.Field(w); w.Step++; }
        double total = 0, onPath = 0;
        for (int c = 0; c < w.ResLoad.Length; c++) total += w.ResLoad[c];
        foreach (int c in path) onPath += w.ResLoad[c];
        Assert.True(w.ResLoad[src] < 1e-6f, $"{w.ResLoad[src]} left at the source");
        Assert.True(total > 0.9, $"persistent pathogens decayed to {total}");
        // everything is on the path or below its end; the cell 12 steps down holds some of it
        Assert.True(w.ResLoad[path[12]] > 0f, "nothing reached 12 cells downstream");
        int end = path[12]; double below = 0;
        while (end >= 0) { if (Array.IndexOf(path, end) < 0) below += w.ResLoad[end]; end = w.ResRecv[end]; }
        Assert.True(onPath + below > 0.999 * total, $"{total - onPath - below} went off the river's course");
    }

    [Fact] // the reservoir decays by the strain's persistence: hours for a fragile strain, years for a spore
    public void ReservoirDecaysByPersistence()
    {
        using var sim = new Simulation(64, 85, 1, empty: true);
        var w = sim.W;
        Pathogens.BuildNet(w);
        Array.Clear(w.ResLoad);
        int fragile = -1, spore = -1, mid = -1;
        for (int c = 0; c < w.ResLoad.Length && mid < 0; c++)
        {
            if (w.Cell[c] != (byte)Realm.Land || w.Wet[c] != 0) continue;
            if (fragile < 0) fragile = c; else if (spore < 0) spore = c; else mid = c;
        }
        void Put(int c, int pers) { var g = Gene(0, 0, 0, pers); Array.Copy(g, 0, w.ResGene, c * PG.N, PG.N); w.ResLoad[c] = 1f; }
        Put(fragile, 0); Put(spore, 255); Put(mid, 128);
        for (int t = 0; t < 20; t++) Pathogens.Field(w);
        Assert.Equal(0f, w.ResLoad[fragile]);                                  // (1 - 0.9)^20 is below PG_RES_MIN
        Assert.Equal(MathF.Pow(1f - K.PG_DRY * K.PG_DECAY_LO, 20), w.ResLoad[spore], 4);
        Assert.Equal(MathF.Pow(1f - K.PG_DRY * Pathogens.Decay(128), 20), w.ResLoad[mid], 4);
        Assert.True(Pathogens.Decay(0) > 100 * Pathogens.Decay(255));
    }

    [Fact] // evolution: when transmission goes with shedding, toxin (which raises shedding) and replication rise by passage
    public void VirulenceEvolvesUnderTransmission()
    {
        (double tox, double rep) Passage(bool byShedding)
        {
            using var sim = new Simulation(4096, 86, 1, empty: true);
            var w = sim.W;
            const int N = 60, GENS = 60, DAYS = 20;
            var hosts = new int[N];
            for (int h = 0; h < N; h++) { hosts[h] = Animal(w, 100u + (uint)h); SetMhc(w, hosts[h], 20); }
            var gene = new byte[N * PG.N]; var key = new sbyte[N * G.NMHC]; var weight = new float[N];
            for (int h = 0; h < N; h++) { Array.Copy(Gene(rep: 100, tox: 100, qs: 30), 0, gene, h * PG.N, PG.N); for (int j = 0; j < G.NMHC; j++) key[h * G.NMHC + j] = 20; }
            var rng = new Rng.Seq(7);
            for (int gen = 0; gen < GENS; gen++)
            {
                for (int h = 0; h < N; h++)
                {
                    int a = hosts[h];
                    w.PgLoad[a] = 0; w.ImmEff[a] = 0; w.ImmMem[a] = 0; w.PgDamage[a] = 0;
                    Pathogens.Infect(w, a, gene, h * PG.N, key, h * G.NMHC, true);
                }
                for (int d = 0; d < DAYS; d++)
                {
                    for (int h = 0; h < N; h++) { w.Energy[hosts[h]] = w.EmaxOf(hosts[h]); Pathogens.Host(w, hosts[h], 0); }
                    w.Step++;
                }
                float sum = 0;
                for (int h = 0; h < N; h++) { weight[h] = byShedding ? Pathogens.Shedding(w, hosts[h]) : 1f; sum += weight[h]; }
                var next = new byte[N * PG.N]; var nkey = new sbyte[N * G.NMHC];
                for (int h = 0; h < N; h++)
                {
                    float u = rng.Next() * sum; int d = 0;
                    while (d < N - 1 && (u -= weight[d]) > 0) d++;
                    Array.Copy(w.PgGene, hosts[d] * PG.N, next, h * PG.N, PG.N);
                    Array.Copy(w.Strain, hosts[d] * G.NMHC, nkey, h * G.NMHC, G.NMHC);
                }
                gene = next; key = nkey;
                Assert.All(hosts, a => Assert.True(w.StageOf[a] != St.Dead));
            }
            double t = 0, r = 0;
            for (int h = 0; h < N; h++) { t += gene[h * PG.N + PG.TOX]; r += gene[h * PG.N + PG.REP]; }
            return (t / N, r / N);
        }
        var sel = Passage(true); var drift = Passage(false);
        Assert.True(sel.tox > drift.tox + 10, $"toxin gene: shedding-weighted passage {sel.tox:F1}, random passage {drift.tox:F1} (from 100)");
        Assert.True(sel.rep > drift.rep + 10, $"replication gene: shedding-weighted passage {sel.rep:F1}, random passage {drift.rep:F1} (from 100)");
    }
}
