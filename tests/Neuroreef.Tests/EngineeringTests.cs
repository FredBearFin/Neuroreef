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

    // ---- growth (2026-10-31): no fixed slot cap; a world grows as births need slots, and that changes nothing in the run

    /// <summary>A hash of the run that does not depend on how many slots the world has: every slot ever used, the free
    /// slots' order above the never-used ones, the counters and two fields.</summary>
    internal static ulong RunHash(World w)
    {
        ulong h = 14695981039346656037UL;
        void Mix(ulong v) { h ^= v; h *= 1099511628211UL; }
        void MixF(float v) => Mix((uint)BitConverter.SingleToInt32Bits(v));
        Mix(w.Step); Mix((ulong)w.AliveCount); Mix(w.NextSerial); Mix((ulong)w.SlotsUsed);
        Mix((ulong)w.T.SlotShortage); for (int s = 0; s < K.CLADES; s++) { Mix((ulong)w.T.Births[s]); Mix(w.Sp[s].Used ? 1UL : 0UL); }
        for (int i = 0; i < w.SlotsUsed; i++)
        {
            Mix(w.StageOf[i]);
            if (w.StageOf[i] == St.Dead) continue;
            MixF(w.X[i]); MixF(w.Y[i]); MixF(w.Energy[i]); MixF(w.Heading[i]); MixF(w.Hidden[i * G.NH]); Mix(w.Serial[i]);
            var wf = w.W2f.Slot(i); for (int k = 0; k < G.W2N; k += 97) MixF(wf[k]);
            var gn = w.GenomeOf(i); for (int k = 0; k < G.LEN; k += 89) Mix((byte)gn[k]);
        }
        // the free slots that were used once, top first (below them lie the never-used ones, which differ only in how many)
        for (int k = w.FreeTop - 1; k >= 0 && w.FreeStack[k] < w.SlotsUsed; k--) Mix((ulong)w.FreeStack[k]);
        for (int c = 0; c < Map.FW * Map.FW; c += 7) { MixF(w.Pool[0][c]); MixF(w.Nutrient[c]); MixF(w.Pool[(int)Pool.Carrion][c]); }
        return h;
    }

    [Fact] // a world that starts small grows past its start as births need slots, and runs exactly as one built big
    public void GrowingWorldRunsAsOneBuiltBig()
    {
        using var small = new Simulation(1024, 11, 2);
        using var big = new Simulation(4096, 11, 1);   // and on another number of threads (NR-105)
        Assert.Equal(RunHash(big.W), RunHash(small.W));
        int peak = 0;
        for (int k = 0; k < 400; k++)
        {
            small.Step(); big.Step();
            peak = Math.Max(peak, small.W.AliveCount);
            if (k % 50 == 49) Assert.Equal(RunHash(big.W), RunHash(small.W));
        }
        Assert.True(small.W.Cap > 1024 && small.W.Grows > 0, $"the small world never grew ({small.W.Cap} slots, peak {peak} animals)");
        Assert.True(peak > 1024, $"the run never needed more than its 1,024 starting slots (peak {peak}): the test proves nothing");
        Assert.Equal(0, small.W.T.SlotShortage);   // never full: it grew instead
        Assert.Equal(RunHash(big.W), RunHash(small.W));
        Assert.Equal(small.W.Cap - small.W.AliveCount, small.W.FreeTop);
        Assert.Null(small.W.GrowLimit);
    }

    [Fact] // a world limited to a hard maximum (--max-capacity) stops there, and says why
    public void HardLimitStopsGrowthAndSaysWhy()
    {
        using var sim = new Simulation(1024, 11, 2, maxCapacity: 1024);
        for (int k = 0; k < 100; k++) sim.Step();
        var w = sim.W;
        Assert.Equal(1024, w.Cap);
        Assert.True(w.T.SlotShortage > 0, "the run never needed more than 1,024 slots: the test proves nothing");
        Assert.NotNull(w.GrowLimit);
        Assert.Contains("--max-capacity", w.GrowLimit);
    }

    [Fact] // growing keeps every slot as it was; the new slots are as a world built with them has them
    public void ResizeKeepsEverySlot()
    {
        using var sim = new Simulation(1024, 12, 2);
        for (int k = 0; k < 30; k++) sim.Step();
        var w = sim.W;
        int cap = w.Cap;
        ulong before = SlotsHash(w, cap), run = RunHash(w);
        var free = w.FreeStack[..w.FreeTop];
        w.Resize(cap + 1024 + 7);
        Assert.Equal(cap + 1031, w.Cap);
        Assert.Equal(before, SlotsHash(w, cap));   // every per-slot array of slots [0, cap) as before
        Assert.Equal(run, RunHash(w));
        // the free stack: the new slots below the old free ones, the lowest on top
        Assert.Equal(free.Length + 1031, w.FreeTop);
        Assert.True(w.FreeStack.AsSpan(1031, free.Length).SequenceEqual(free));
        for (int k = 0; k < 1031; k++) Assert.Equal(w.Cap - 1 - k, w.FreeStack[k]);
        var fresh = new World(w.Cap, 99);
        foreach (var f in PerSlot.Of(typeof(World)))
        {
            if (f.Name == "FreeStack") continue;
            if (f.GetValue(w) is ISlotArray a) { var b = (ISlotArray)f.GetValue(fresh)!; Assert.True(a.Bytes(w.Cap - 1, 1).SequenceEqual(b.Bytes(w.Cap - 1, 1)), f.Name); continue; }
            var x = (Array)f.GetValue(w)!; var y = (Array)f.GetValue(fresh)!;
            int stride = x.Length / w.Cap;
            for (int e = cap * stride; e < x.Length; e++) Assert.True(Equals(x.GetValue(e), y.GetValue(e)), $"{f.Name}[{e}] of a new slot");
        }
    }

    /// <summary>A hash of the bytes of slots [0, cap) of every per-slot array of the world and its derived cache (FreeStack aside).</summary>
    static ulong SlotsHash(World w, int cap)
    {
        ulong h = 14695981039346656037UL;
        foreach (var o in new object[] { w, w.D })
            foreach (var f in PerSlot.Of(o.GetType()))
            {
                if (f.Name == "FreeStack") continue;
                var v = f.GetValue(o)!;
                ReadOnlySpan<byte> bytes;
                if (v is ISlotArray a) { for (int s = 0; s < cap; s++) foreach (byte x in a.Bytes(s, 1)) { h ^= x; h *= 1099511628211UL; } continue; }
                var arr = (Array)v; int stride = arr.Length / w.Cap;
                int es = System.Runtime.InteropServices.Marshal.SizeOf(arr.GetType().GetElementType()!);
                bytes = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference(arr), arr.Length * es);
                foreach (byte x in bytes[..(cap * stride * es)]) { h ^= x; h *= 1099511628211UL; }
            }
        return h;
    }

    [Fact] // every array of World and of its derived cache whose size follows the slots is [PerSlot], so growth reallocates it
    public void EverySlotArrayIsPerSlot()
    {
        const System.Reflection.BindingFlags fl = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var a = new World(16, 1); var b = new World(48, 1);
        foreach (var (t, x, y) in new (Type, object, object)[] { (typeof(World), a, b), (typeof(Derived), a.D, b.D) })
            foreach (var f in t.GetFields(fl))
            {
                bool perSlot = f.IsDefined(typeof(PerSlotAttribute), false);
                object? u = f.GetValue(x), v = f.GetValue(y);
                if (u is ISlotArray su) { Assert.True(perSlot, $"{t.Name}.{f.Name} is a slot array: mark it [PerSlot]"); Assert.Equal(16, su.Slots); continue; }
                if (u is not Array p || u is float[][]) continue;
                var q = (Array)v!;
                if (p.Length != q.Length) Assert.True(perSlot, $"{t.Name}.{f.Name} grows with the slots: mark it [PerSlot] (World.Resize reallocates only those)");
                if (perSlot) Assert.True(p.Length % 16 == 0 && q.Length == p.Length / 16 * 48, $"{t.Name}.{f.Name} is [PerSlot] but not slots x stride long");
            }
    }

    internal static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Neuroreef.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
