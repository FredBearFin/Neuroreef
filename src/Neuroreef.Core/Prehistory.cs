// The prehistory (DESIGN §7.6): several branches - separate populations on the same land - evolve side by
// side at full speed. Each branch is a Simulation with colonists on (World.Prehistory). Every SYNC steps
// all branches meet at a barrier, where the console's setting changes are applied, a pause can hold
// them, and every MigrateEvery steps the best genomes of each species move to the next branch (an
// island model). At the end, each species' brains are taken from the branch where it did best.

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
            B[b] = new Simulation(capacity, seed + 1000u * (uint)b, per, null, prehistory: true, terrainSeed: seed);
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
        if (B.Length > 1 && e.Migrants > 0 && step > 0 && step % (ulong)e.MigrateEvery < SYNC) Migrate(e.Migrants);
        long t = _sw.ElapsedMilliseconds;
        if (t - _lastT >= 1000) { Rate = (step - _lastStep) * 1000.0 / Math.Max(1, t - _lastT); _lastT = t; _lastStep = step; }
        if (step >= (ulong)e.TotalSteps) Finished = true;
        while (_paused && !_stop) Thread.Sleep(50);
    }

    /// <summary>Island model: the best archive genomes of each species of branch b go to branch b+1.</summary>
    public void Migrate(int count)
    {
        int n = B.Length, ns = Roster.Count;
        var buf = new sbyte[G.LEN];
        for (int b = 0; b < n; b++)
        {
            var from = B[b].W; var to = B[(b + 1) % n].W;
            for (int s = 0; s < ns; s++)
            {
                int m = from.ArchiveCount[s];
                for (int c = 0; c < Math.Min(count, m); c++)
                {
                    // c-th best entry
                    int best = -1; float bs = float.MinValue;
                    for (int k = 0; k < m; k++)
                    {
                        float sc = from.ArchiveScore[s * K.ARCHIVE + k];
                        int rank = 0;
                        for (int j = 0; j < m; j++) if (from.ArchiveScore[s * K.ARCHIVE + j] > sc) rank++;
                        if (rank == c && sc > bs) { bs = sc; best = k; }
                    }
                    if (best < 0) continue;
                    Array.Copy(from.Archive, (s * K.ARCHIVE + best) * G.LEN, buf, 0, G.LEN);
                    Life.ArchiveInsert(to, s, buf, bs);
                }
            }
        }
        Migrations++;
    }
    public int Migrations;

    // ------------------------------------------------------------------ progress and the result

    public enum Standing : byte { Gone = 0, Propped = 1, Breeding = 2, SelfSustaining = 3 }

    /// <summary>
    /// How a species stands in a branch over the last ~1,000 steps:
    /// SelfSustaining = alive, births keep up with deaths (at least 90%) and outnumber colonists two to one;
    /// Breeding = births happen but the species is not replacing itself; Propped = alive only by colonists or
    /// by founders that have not bred; Gone = none left.
    /// </summary>
    public static Standing StandingOf(Simulation sim, int s, out int alive, out float births, out float colonists)
    {
        var w = sim.W; var T = w.T;
        alive = sim.ActiveCount[s];
        births = 0; colonists = 0;
        float deaths = 0;
        int n = T.SampleCount, back = Math.Min(32, n);
        var a = new float[T.Len];
        for (int k = n - back; k < n; k++)
        {
            T.Sample(k, a);
            births += a[T.SpeciesOffset(s, Telemetry.S_BIRTHS)];
            colonists += a[T.SpeciesOffset(s, Telemetry.S_IMM)];
            for (int c = 0; c < Cause.N; c++) deaths += a[T.SpeciesOffset(s, Telemetry.S_DEATHS + c)];
        }
        if (alive == 0) return Standing.Gone;
        if (births > 0 && births >= 0.9f * deaths && births >= 2f * colonists) return Standing.SelfSustaining;
        if (births > 0) return Standing.Breeding;
        return Standing.Propped;
    }

    /// <summary>For each species, the branch where it stands best (then by number alive).</summary>
    public int[] BestBranchPerSpecies()
    {
        int ns = Roster.Count;
        var choice = new int[ns];
        for (int s = 0; s < ns; s++)
        {
            int best = 0; long bestKey = long.MinValue;
            for (int b = 0; b < B.Length; b++)
            {
                var st = StandingOf(B[b], s, out int alive, out float births, out _);
                long key = (long)st * 10_000_000 + Math.Min(alive, 9_999_999);
                if (key > bestKey) { bestKey = key; best = b; }
            }
            choice[s] = best;
        }
        return choice;
    }

    /// <summary>The gene bank of the prehistory: each species from the branch where it did best.</summary>
    public void SaveBank(Stream s) => GeneBank.SaveMerged(B, BestBranchPerSpecies(), s);
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
