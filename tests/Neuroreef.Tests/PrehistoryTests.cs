// Prehistory requirements (DESIGN §7.6): branches run in lockstep, settings changes reach every branch,
// the best genomes migrate between branches, the merged bank takes each species from one branch, and the
// world seeded from it has no colonists (NR-313, NR-323 ... NR-326).

using System;
using System.Threading;
using Xunit;

namespace Neuroreef.Tests;

public class PrehistoryTests
{
    [Fact] // NR-324: the best genomes of each species move to the next branch
    public void MigrationCopiesTheBestArchiveGenomes()
    {
        using var pre = new Prehistory(2, 16384, 71, 2);
        var from = pre.B[0].W; var to = pre.B[1].W;
        var g = new sbyte[G.LEN];
        for (int k = 0; k < G.LEN; k++) g[k] = (sbyte)((k * 7) % 50 - 25);
        Life.ArchiveInsert(from, 3, g, 1e6f);                  // far better than anything in branch B's archive
        pre.Migrate(1);
        int m = to.ArchiveCount[3]; bool found = false;
        for (int k = 0; k < m && !found; k++)
        {
            found = true;
            for (int j = 0; j < G.LEN; j++) if (to.Archive[(3 * K.ARCHIVE + k) * G.LEN + j] != g[j]) { found = false; break; }
        }
        Assert.True(found, "the best genome of species 3 did not reach the next branch");
        Assert.Equal(1, pre.Migrations);
    }

    [Fact] // NR-323, NR-325: branches step in lockstep; a settings change reaches every branch at a barrier
    public void BranchesRunInLockstepAndTakeSettings()
    {
        var e = new EvoSettings { TotalSteps = 1000, ColonistSteps = 800 };
        using var pre = new Prehistory(2, 16384, 72, 2, e);
        pre.Configure(s => { s.MutRate = 0.07f; s.Tournament = 5; });
        pre.Start();
        var until = DateTime.UtcNow.AddMinutes(5);
        while (!pre.Finished && DateTime.UtcNow < until) Thread.Sleep(50);
        Assert.True(pre.Finished);
        pre.StopAndWait();
        foreach (var b in pre.B)
        {
            Assert.Equal(1000UL, b.W.Step);
            Assert.Equal(0.07f, b.W.Evo.MutRate);
            Assert.Equal(5, b.W.Evo.Tournament);
            Assert.True(b.W.Prehistory);
        }
        // NR-326: the bank takes each species from one branch; the world seeded from it never spawns
        var choice = pre.BestBranchPerSpecies();
        Assert.All(choice, c => Assert.InRange(c, 0, 1));
        var bank = pre.Bank();
        Assert.Equal(Roster.Count, bank.Genomes.Length);
        for (int s = 0; s < Roster.Count; s++)
        {
            var src = pre.B[choice[s]];
            if (src.ActiveCount[s] > 0) Assert.NotEmpty(bank.Genomes[s]);
        }
        using var live = new Simulation(16384, 72, 1, bank, prehistory: false, terrainSeed: 72);
        Assert.False(live.W.Prehistory);
        for (int k = 0; k < 300; k++) live.Step();
        foreach (var m in live.W.T.Immigrants) Assert.Equal(0, m);
    }

    [Fact] // the console's settings stay inside a range where the search still works
    public void SettingsAreClamped()
    {
        var e = new EvoSettings { MutRate = 5, Tournament = 0, TotalSteps = 10, ColonistSteps = 99, Migrants = -3 };
        e.Clamp();
        Assert.Equal(0.2f, e.MutRate);
        Assert.Equal(1, e.Tournament);
        Assert.Equal(1000, e.TotalSteps);
        Assert.Equal(99, e.ColonistSteps);
        Assert.Equal(0, e.Migrants);
    }
}
