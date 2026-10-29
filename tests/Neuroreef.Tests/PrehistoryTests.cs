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

    static void RunTo(Prehistory pre, int steps)
    {
        pre.Configure(s => s.TotalSteps = steps);
        pre.Start();
        var until = DateTime.UtcNow.AddMinutes(10);
        while (!pre.Finished && DateTime.UtcNow < until) Thread.Sleep(20);
        Assert.True(pre.Finished);
        pre.StopAndWait();
    }

    // a save's fingerprint (a checkpoint of 256-unit brains can pass 2 GB, too big to read into one array)
    static byte[] Hash(string p) { using var f = System.IO.File.OpenRead(p); return System.Security.Cryptography.SHA256.HashData(f); }

    static byte[] State(Prehistory pre)
    {
        string p = System.IO.Path.GetTempFileName();
        try { Checkpoint.Save(pre, p); return Hash(p); } finally { System.IO.File.Delete(p); }
    }

    [Fact] // DESIGN §7.6: a prehistory saved and resumed continues exactly as one that never stopped
    public void CheckpointResumesExactly()
    {
        byte[] straight;
        using (var a = new Prehistory(2, 4096, 81, 2)) { RunTo(a, 1500); straight = State(a); }
        string path = System.IO.Path.GetTempFileName();
        try
        {
            using (var b = new Prehistory(2, 4096, 81, 2)) { RunTo(b, 1000); Checkpoint.Save(b, path); }
            using var c = new Prehistory(2, 4096, 81, 2);
            c.LoadCheckpoint(path);
            Assert.Equal(1000UL, c.Step);
            RunTo(c, 1500);
            var resumed = State(c);
            Assert.Equal(straight.Length, resumed.Length);
            Assert.True(straight.AsSpan().SequenceEqual(resumed), "resumed state differs from the straight run");
            // a checkpoint for another seed is refused
            using var d = new Prehistory(2, 4096, 82, 2);
            Assert.Throws<System.IO.InvalidDataException>(() => d.LoadCheckpoint(path));
        }
        finally { System.IO.File.Delete(path); }
    }

    static byte[] WorldState(World w)
    {
        string p = System.IO.Path.GetTempFileName();
        try { Checkpoint.SaveWorld(w, p); return Hash(p); } finally { System.IO.File.Delete(p); }
    }

    [Fact] // a live world saved and continued is exactly the world that never stopped
    public void SavedWorldContinuesExactly()
    {
        using var a = new Simulation(8192, 91, 1);
        for (int k = 0; k < 300; k++) a.Step();
        string path = System.IO.Path.GetTempFileName();
        try
        {
            Checkpoint.SaveWorld(a.W, path);
            var (cap, seed, ts) = Checkpoint.WorldHeader(path);
            using var b = new Simulation(cap, seed, 1, null, prehistory: false, empty: true, terrainSeed: ts);
            Checkpoint.LoadWorld(b.W, path);
            for (int k = 0; k < 200; k++) { a.Step(); b.Step(); }
            Assert.True(WorldState(a.W).AsSpan().SequenceEqual(WorldState(b.W)), "continued world differs");
        }
        finally { System.IO.File.Delete(path); }
    }

    [Fact] // the data export writes every file, with one population column per clade
    public void ExportWritesTheData()
    {
        using var sim = new Simulation(8192, 92, 1);
        for (int k = 0; k < 400; k++) sim.Step();
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nr-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            Neuroreef.Server.Export.Write(sim, dir);
            foreach (var f in new[] { "population.csv", "traits.csv", "deaths.csv", "telemetry.csv", "clades.csv", "report.txt" })
                Assert.True(System.IO.File.Exists(System.IO.Path.Combine(dir, f)), f);
            var pop = System.IO.File.ReadAllLines(System.IO.Path.Combine(dir, "population.csv"));
            Assert.StartsWith("step,", pop[0]);
            Assert.True(pop.Length > 5);
            Assert.Equal(pop[0].Split(',').Length, pop[^1].Split(',').Length);
            Assert.NotEmpty(Neuroreef.Server.Export.Zip(dir));
        }
        finally { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); }
    }

    [Fact] // NR-323, NR-325: branches step in lockstep; a settings change reaches every branch at a barrier
    public void BranchesRunInLockstepAndTakeSettings()
    {
        var e = new EvoSettings { TotalSteps = 1000 };
        using var pre = new Prehistory(2, 8192, 72, 2, e);
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
        using var live = new Simulation(8192, 72, 1, bank, prehistory: false, terrainSeed: 72);
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
