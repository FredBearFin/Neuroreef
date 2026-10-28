// Prehistory requirements (DESIGN §7.6): branches run in lockstep, settings changes reach every branch,
// nothing is ever added to a branch, the merged bank takes each species from one branch, and the world
// seeded from it is births and deaths only (NR-313, NR-323, NR-325, NR-326).

using System;
using System.Threading;
using Xunit;

namespace Neuroreef.Tests;

public class PrehistoryTests
{
    [Fact] // NR-313: a prehistory branch is births and deaths only: a species wiped out never returns
    public void PrehistoryNeverSpawns()
    {
        using var sim = new Simulation(16384, 52, 1, prehistory: true);
        var w = sim.W;
        Assert.True(w.Prehistory);
        long founders = 0; foreach (var c in w.T.Created) founders += c;
        for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] != St.Dead && w.Spc[i] == 0) Resolve.DieNoBody(w, i, Cause.Starve, -1);
        for (int k = 0; k < 500; k++) sim.Step();
        int drifters = 0;
        for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] != St.Dead && w.Spc[i] == 0) drifters++;
        Assert.Equal(0, drifters);
        foreach (var m in w.T.Immigrants) Assert.Equal(0, m);
        long created = 0; foreach (var c in w.T.Created) created += c;
        long conceived = 0; foreach (var c in w.T.Conceptions) conceived += c;
        long lays = 0; foreach (var c in w.T.Lays) lays += c;
        Assert.True(created - founders <= conceived + lays, $"created {created - founders}, conceived {conceived}, lays {lays}");
    }

    [Fact] // NR-323, NR-325: branches step in lockstep; a settings change reaches every branch at a barrier
    public void BranchesRunInLockstepAndTakeSettings()
    {
        var e = new EvoSettings { TotalSteps = 1000 };
        using var pre = new Prehistory(2, 16384, 72, 2, e);
        pre.Configure(s => { s.MutRate = 0.003f; s.MutBig = 20; });
        pre.Start();
        var until = DateTime.UtcNow.AddMinutes(5);
        while (!pre.Finished && DateTime.UtcNow < until) Thread.Sleep(50);
        Assert.True(pre.Finished);
        pre.StopAndWait();
        foreach (var b in pre.B)
        {
            Assert.Equal(1000UL, b.W.Step);
            Assert.Equal(0.003f, b.W.Evo.MutRate);
            Assert.Equal(20f, b.W.Evo.MutBig);
            foreach (var m in b.W.T.Immigrants) Assert.Equal(0, m);
        }
        // NR-326: the bank holds the living clades of the best branch; the world seeded from it never spawns
        int best = pre.BestBranch();
        Assert.InRange(best, 0, 1);
        var bank = pre.Bank();
        Assert.Equal(K.CLADES, bank.Genomes.Length);
        Assert.NotEmpty(bank.Clades);
        foreach (var c in bank.Clades) Assert.NotEmpty(bank.Genomes[c.Slot]);
        using var live = new Simulation(16384, 72, 1, bank, prehistory: false, terrainSeed: 72);
        Assert.False(live.W.Prehistory);
        for (int k = 0; k < 300; k++) live.Step();
        foreach (var m in live.W.T.Immigrants) Assert.Equal(0, m);
    }

    [Fact] // the console's settings stay inside a sensible range
    public void SettingsAreClamped()
    {
        var e = new EvoSettings { MutRate = 5, MutBigShare = -1, TotalSteps = 10 };
        e.Clamp();
        Assert.Equal(0.05f, e.MutRate);
        Assert.Equal(0f, e.MutBigShare);
        Assert.Equal(1000, e.TotalSteps);
    }
}
