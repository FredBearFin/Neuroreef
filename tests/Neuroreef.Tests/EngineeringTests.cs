// Engineering requirements NR-101 ... NR-109.

using System;
using System.IO;
using Xunit;

namespace Neuroreef.Tests;

public class EngineeringTests
{
    const int Cap = 16384;

    static ulong StateHash(World w)
    {
        // FNV-1a over the agent state and two fields; any difference in any slot changes it
        ulong h = 14695981039346656037UL;
        void Mix(float v) { h ^= (uint)BitConverter.SingleToInt32Bits(v); h *= 1099511628211UL; }
        for (int i = 0; i < w.Cap; i++)
        {
            h ^= w.StageOf[i]; h *= 1099511628211UL;
            if (w.StageOf[i] == St.Dead) continue;
            Mix(w.X[i]); Mix(w.Y[i]); Mix(w.Energy[i]); Mix(w.Heading[i]); h ^= w.Serial[i]; h *= 1099511628211UL;
            Mix(w.W2f[(long)i * G.W2N + 7]);
        }
        for (int c = 0; c < Map.FW * Map.FW; c += 7) { Mix(w.Pool[0][c]); Mix(w.Nutrient[c]); }
        return h;
    }

    static ulong Run(int threads, int steps, uint seed = 3)
    {
        using var sim = new Simulation(Cap, seed, threads);
        for (int k = 0; k < steps; k++) sim.Step();
        return StateHash(sim.W);
    }

    [Fact] // NR-104
    public void SameSeedGivesSameRun() => Assert.Equal(Run(2, 120), Run(2, 120));

    [Fact] // NR-104
    public void DifferentSeedGivesDifferentRun() => Assert.NotEqual(Run(2, 60, 3), Run(2, 60, 4));

    [Fact] // NR-105
    public void ResultIndependentOfThreadCount()
    {
        ulong a = Run(1, 120), b = Run(2, 120), c = Run(4, 120);
        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }

    [Fact] // NR-101
    public void StepLoopDoesNotAllocate()
    {
        // one worker thread, so every phase runs on this thread and the per-thread counter sees all of it
        // (a process-wide counter would also count other tests running in parallel)
        using var sim = new Simulation(Cap, 5, 1);
        for (int k = 0; k < 200; k++) sim.Step();      // warm up: JIT, tiering, lazy statics
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 300; k++) sim.Step();
        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.True(after == before, $"step loop allocated {after - before} bytes over 300 steps");
    }

    [Fact] // NR-109
    public void PopulationReconcilesWithBirthsAndDeaths()
    {
        using var sim = new Simulation(Cap, 7, 2);
        var w = sim.W;
        long created0 = Sum(w.T.Created), deaths0 = Sum(w.T.Deaths);
        int alive0 = w.AliveCount;
        for (int k = 0; k < 300; k++) sim.Step();
        long created = Sum(w.T.Created) - created0, deaths = Sum(w.T.Deaths) - deaths0;
        Assert.Equal(alive0 + created - deaths, w.AliveCount);
        int counted = 0;
        for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] != St.Dead) counted++;
        Assert.Equal(counted, w.AliveCount);
        Assert.Equal(w.Cap - w.AliveCount, w.FreeTop);
    }

    static long Sum(long[] a) { long s = 0; foreach (var v in a) s += v; return s; }

    [Fact] // NR-106
    public void ConstantsDocumentMatchesCode()
    {
        string path = Path.Combine(RepoRoot(), "docs", "CONSTANTS.md");
        Assert.True(File.Exists(path), "docs/CONSTANTS.md missing; generate it with Neuroreef --print-constants");
        string doc = File.ReadAllText(path).Replace("\r\n", "\n");
        Assert.True(doc == ConstantsDoc.Generate(), "docs/CONSTANTS.md is out of date; regenerate it with Neuroreef --print-constants > docs/CONSTANTS.md");
    }

    internal static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Neuroreef.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
