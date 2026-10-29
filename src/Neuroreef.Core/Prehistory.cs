// The prehistory (DESIGN §7.6): several branches - separate worlds on the same land - evolve side by side
// at full speed. Each branch is seeded once with founders and is then births and deaths only, like the
// live world: nothing is ever added to it. Every SYNC steps all branches meet at a barrier, where the
// console's setting changes are applied and a pause can hold them. At the end, each species' brains are
// taken from the branch where it did best.

using System;
using System.IO;
using System.Threading;

namespace Neuroreef;

public sealed class Prehistory : IDisposable
{
    public const int SYNC = 500;                 // steps between barriers
    public readonly Simulation[] B;
    public readonly EvoSettings Pending = new(); // edited by the console; copied into every branch at the next barrier
    readonly object _pendingLock = new();
    bool _pendingDirty;
    readonly Thread[] _threads;
    readonly Barrier _barrier;
    volatile bool _stop, _paused;
    public volatile bool Finished;
    public ulong Step => B[0].W.Step;
    public double Rate;                          // steps per second (per branch, all in lockstep)
    public string? CheckpointPath;               // when set, the whole state is saved here every CheckpointMinutes (at a barrier)
    public double CheckpointMinutes = 30;
    public string? LastCheckpointError;
    public DateTime LastCheckpoint = DateTime.MinValue;
    long _lastSaveMs;
    long _lastT; ulong _lastStep;
    readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();

    public Prehistory(int branches, int capacity, uint seed, int threads, EvoSettings? settings = null)
    {
        int n = Math.Max(1, branches);
        B = new Simulation[n];
        int per = Math.Max(1, threads / n);
        if (settings != null) Pending.CopyFrom(settings);
        Pending.Clamp();
        for (int b = 0; b < n; b++)
        {
            // every branch has its own run seed but the same land (terrain seed)
            B[b] = new Simulation(capacity, seed + 1000u * (uint)b, per, null, prehistory: true, terrainSeed: seed, evo: Pending);
            B[b].W.Evo.CopyFrom(Pending);
        }
        _barrier = new Barrier(n, _ => AtBarrier());
        _threads = new Thread[n];
        for (int b = 0; b < n; b++)
        {
            int id = b;
            _threads[b] = new Thread(() => Loop(id)) { IsBackground = true, Name = "neuroreef-branch-" + b };
        }
    }

    public void Start() { foreach (var t in _threads) t.Start(); }

    public bool Paused { get => _paused; set => _paused = value; }

    /// <summary>Change the evolution settings; they take effect at the next barrier in every branch.</summary>
    public void Configure(Action<EvoSettings> edit)
    {
        lock (_pendingLock) { edit(Pending); Pending.Clamp(); _pendingDirty = true; }
    }

    /// <summary>End the prehistory now (the console's "Begin the world").</summary>
    public void Stop() { _stop = true; }

    /// <summary>Stop and wait until no branch is stepping, so the result can be read consistently.</summary>
    public void StopAndWait()
    {
        _stop = true; _paused = false;
        foreach (var t in _threads) if (t.IsAlive) t.Join();
    }

    void Loop(int b)
    {
        var sim = B[b];
        try
        {
            while (!_stop && !Finished)
            {
                for (int k = 0; k < SYNC && !_stop; k++)
                {
                    if (sim.W.Step >= (ulong)sim.W.Evo.TotalSteps) break;
                    sim.Step();
                }
                _barrier.SignalAndWait();
            }
        }
        catch (BarrierPostPhaseException e) { Console.Error.WriteLine("prehistory barrier failed: " + e.InnerException?.Message); }
        finally { _barrier.RemoveParticipant(); }
    }

    // Runs on one thread while every branch waits (no branch is stepping).
    void AtBarrier()
    {
        lock (_pendingLock)
        {
            if (_pendingDirty) { foreach (var s in B) s.W.Evo.CopyFrom(Pending); _pendingDirty = false; }
        }
        ulong step = Step;
        var e = B[0].W.Evo;
        long t = _sw.ElapsedMilliseconds;
        if (t - _lastT >= 1000) { Rate = (step - _lastStep) * 1000.0 / Math.Max(1, t - _lastT); _lastT = t; _lastStep = step; }
        if (step >= (ulong)e.TotalSteps) Finished = true;
        if (CheckpointPath != null && t - _lastSaveMs >= CheckpointMinutes * 60_000) { _lastSaveMs = t; SaveCheckpoint(); }
        while (_paused && !_stop) Thread.Sleep(50);
    }

    /// <summary>Save the whole state now. Call only while no branch is stepping (at a barrier, or after StopAndWait).</summary>
    public void SaveCheckpoint()
    {
        if (CheckpointPath == null) return;
        try { Checkpoint.Save(this, CheckpointPath); LastCheckpoint = DateTime.Now; LastCheckpointError = null; }
        catch (Exception ex) { LastCheckpointError = ex.Message; Console.Error.WriteLine("checkpoint failed: " + ex.Message); }
    }

    /// <summary>Resume from a checkpoint (before Start). The current settings (Pending) replace the saved ones.</summary>
    public void LoadCheckpoint(string path)
    {
        Checkpoint.Load(this, path);
        foreach (var s in B) s.W.Evo.CopyFrom(Pending);
        _lastStep = Step;
    }

    // ------------------------------------------------------------------ progress and the result

    public enum Standing : byte { Gone = 0, Dying = 1, Breeding = 2, SelfSustaining = 3 }

    /// <summary>
    /// How a species stands in a branch over the last ~1,000 steps: SelfSustaining = alive and births keep up
    /// with deaths (at least 90%); Breeding = births happen but the species is not replacing itself; Dying =
    /// alive with no births; Gone = none left.
    /// </summary>
    public static Standing StandingOf(Simulation sim, int s, out int alive, out float births)
    {
        var w = sim.W; var T = w.T;
        alive = sim.ActiveCount[s];
        births = 0;
        float deaths = 0;
        int n = T.SampleCount, back = Math.Min(32, n);
        var a = new float[T.Len];
        for (int k = n - back; k < n; k++)
        {
            T.Sample(k, a);
            births += a[T.SpeciesOffset(s, Telemetry.S_BIRTHS)];
            for (int c = 0; c < Cause.N; c++) deaths += a[T.SpeciesOffset(s, Telemetry.S_DEATHS + c)];
        }
        if (alive == 0) return Standing.Gone;
        if (births > 0 && births >= 0.9f * deaths) return Standing.SelfSustaining;
        if (births > 0) return Standing.Breeding;
        return Standing.Dying;
    }

    /// <summary>
    /// The branch the world begins from: the one with the most self-sustaining clades, then the most organisms. Clades
    /// differ between branches (each grew from its own random ancestors), so a branch is taken whole.
    /// </summary>
    public int BestBranch()
    {
        int best = 0; long bestKey = long.MinValue;
        for (int b = 0; b < B.Length; b++)
        {
            int self = 0;
            for (int s = 0; s < K.CLADES; s++) if (B[b].W.Sp[s].Alive && StandingOf(B[b], s, out _, out _) == Standing.SelfSustaining) self++;
            long key = (long)self * 10_000_000 + Math.Min(B[b].W.AliveCount, 9_999_999);
            if (key > bestKey) { bestKey = key; best = b; }
        }
        return best;
    }

    /// <summary>The gene bank of the prehistory: the living clades of the best branch.</summary>
    public void SaveBank(Stream s) => GeneBank.Save(B[BestBranch()].W, s);
    public void SaveBank(string path) { using var f = File.Create(path); SaveBank(f); }

    public GeneBank.Data Bank()
    {
        var ms = new MemoryStream();
        SaveBank(ms);
        ms.Position = 0;
        return GeneBank.Load(ms);
    }

    public void Dispose()
    {
        _stop = true; _paused = false;
        foreach (var t in _threads) if (t.IsAlive) t.Join(5000);
        foreach (var s in B) s.Dispose();
    }
}
