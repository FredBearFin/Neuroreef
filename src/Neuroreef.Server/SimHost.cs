// Runs what the console shows, in two phases:
//   PREHISTORY  several branches evolve at full speed on their own threads (Prehistory, DESIGN §7.6);
//               the console views one branch at a time and can change the evolution settings.
//   LIVE        the world: seeded once from the prehistory's brains, then births and deaths only, at a
//               fixed step rate.
// The sim threads never wait for a client (NR-506): frames are filled only when asked and clients take
// whatever is newest. Commands from clients arrive through a queue and are applied between steps.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Neuroreef.Server;

public sealed class SimHost : IDisposable
{
    readonly Options _o;
    readonly Thread _thread;
    volatile bool _stop;
    public volatile bool Paused;
    public volatile float Rate;             // live world: steps per second, 0 = as fast as possible
    int _stepOnce;
    public double MeasuredRate;
    readonly ConcurrentQueue<Action<Simulation>> _cmds = new();

    // ---- phases
    public Prehistory? Pre { get; private set; }
    Simulation? _live;
    public volatile int ViewBranch;
    volatile bool _begin;
    public string BankSource { get; private set; }
    public string? BankPath { get; private set; }
    public int PhaseVersion;                 // increments when the viewed simulation changes (phase or branch)
    public bool IsPrehistory => _live == null;
    public string Phase => IsPrehistory ? "prehistory" : "live";

    /// <summary>The simulation the console shows: the live world, or the viewed prehistory branch.</summary>
    public Simulation Sim => _live ?? Pre!.B[Math.Clamp(ViewBranch, 0, Pre.B.Length - 1)];
    public World W => Sim.W;

    // ---- frames (latest wins)
    public readonly Latest Agents = new(), FieldsF = new(), ProbeF = new();
    public volatile bool WantAgents, WantFields, WantProbe;
    readonly byte[] _fieldsBuf, _probeBuf;

    public SimHost(Options o, GeneBank.Data? bank)
    {
        _o = o;
        Rate = o.Rate;
        BankSource = o.BankSource;
        _fieldsBuf = new byte[Wire.FieldsSize];
        _probeBuf = new byte[Wire.ProbeSize];
        if (bank != null || o.RandomBrains)
            _live = new Simulation(o.Capacity, o.Seed, o.Threads, bank, prehistory: false, terrainSeed: o.Seed);
        else
        {
            Pre = new Prehistory(o.Branches, K.BRANCH_CAPACITY, o.Seed, o.Threads, o.PrehistorySettings(stepsAreLength: false));
            BankSource = "prehistory";
        }
        _thread = new Thread(Loop) { IsBackground = true, Name = "neuroreef-host", Priority = ThreadPriority.AboveNormal };
    }

    public void Start() { Pre?.Start(); _thread.Start(); }

    public void Post(Action<Simulation> cmd) => _cmds.Enqueue(cmd);
    public void StepOnce() => Interlocked.Exchange(ref _stepOnce, 1);
    public void BeginWorld() => _begin = true;

    public void SetPaused(bool p) { Paused = p; if (Pre != null) Pre.Paused = p; }

    public void View(int branch)
    {
        if (Pre == null) return;
        ViewBranch = Math.Clamp(branch, 0, Pre.B.Length - 1);
        Interlocked.Increment(ref PhaseVersion);
    }

    void Loop()
    {
        var sw = Stopwatch.StartNew();
        double due = 0; long steps = 0; double lastT = 0; long lastSteps = 0;
        while (!_stop)
        {
            while (_cmds.TryDequeue(out var c)) { try { c(Sim); } catch (Exception e) { Console.Error.WriteLine("command failed: " + e.Message); } }

            if (IsPrehistory)
            {
                // the branches step on their own threads; here we only publish the viewed branch
                if (WantAgents) Sim.Snap.Requested = true;
                Publish(racy: true);
                MeasuredRate = Pre!.Rate;
                if (Pre.Finished || _begin) EnterLive();
                Thread.Sleep(5);
                continue;
            }

            double now = sw.Elapsed.TotalSeconds;
            bool run;
            if (Paused) { run = Interlocked.Exchange(ref _stepOnce, 0) == 1; due = now; }
            else if (Rate <= 0) run = true;
            else
            {
                if (due < now - 0.25) due = now - 0.25;     // do not spiral after a stall
                run = now >= due;
                if (run) due += 1.0 / Rate;
            }
            if (!run) { Thread.Sleep(1); Publish(racy: false); continue; }

            var sim = _live!;
            if (WantAgents) sim.Snap.Requested = true;
            sim.Step();
            steps++;
            Publish(racy: false);
            if (_o.SaveBank != null && sim.W.Step % 50000 == 0) { try { GeneBank.Save(sim.W, _o.SaveBank); } catch (Exception e) { Console.Error.WriteLine("bank save failed: " + e.Message); } }
            if (now - lastT >= 1.0) { MeasuredRate = (steps - lastSteps) / (now - lastT); lastT = now; lastSteps = steps; }
        }
    }

    /// <summary>End the prehistory: save its brains, then seed the live world from them (births and deaths only from here).</summary>
    void EnterLive()
    {
        var pre = Pre!;
        pre.StopAndWait();
        Console.WriteLine($"prehistory ended at step {pre.Step:N0} ({pre.Step * (ulong)K.YEARS_PER_STEP / 1_000_000:N0} million symbolic years)");
        var bank = pre.Bank();
        string path = _o.SaveBank ?? Path.Combine(AppContext.BaseDirectory, "evolved.nrbank");
        try { pre.SaveBank(path); BankPath = path; Console.WriteLine("evolved brains saved: " + path + " (the next start uses them)"); }
        catch (Exception e) { Console.Error.WriteLine("could not save the evolved brains: " + e.Message); }
        var live = new Simulation(_o.Capacity, _o.Seed, _o.Threads, bank, prehistory: false, terrainSeed: _o.Seed);
        pre.Dispose();
        Pre = null;
        _live = live;
        BankSource = "prehistory";
        Paused = false;
        Interlocked.Increment(ref PhaseVersion);
        Console.WriteLine("the world begins: births and deaths only");
    }

    void Publish(bool racy)
    {
        var sim = Sim;
        if (WantAgents && sim.Snap.TryTake(out var buf, out int len)) { Agents.Set(buf, len); WantAgents = false; }
        // in the prehistory the viewed branch is stepping on its own thread: these two read it without a
        // lock, which can mix two steps in one frame; display only, it never affects the run
        if (WantFields) { int n = Wire.EncodeFields(sim.W, _fieldsBuf); FieldsF.Set(_fieldsBuf, n); WantFields = false; }
        if (WantProbe) { int n = Wire.EncodeProbe(sim, _probeBuf); ProbeF.Set(_probeBuf, n); WantProbe = false; }
        _ = racy;
    }

    public void Dispose() { _stop = true; _thread.Join(2000); Pre?.Dispose(); _live?.Dispose(); }

    /// <summary>The newest copy of a frame, shared by all clients. Set copies; Get returns an immutable array.</summary>
    public sealed class Latest
    {
        byte[] _data = Array.Empty<byte>();
        long _ver;
        public void Set(byte[] src, int len)
        {
            var copy = new byte[len];
            Buffer.BlockCopy(src, 0, copy, 0, len);
            Volatile.Write(ref _data, copy);
            Interlocked.Increment(ref _ver);
        }
        public (byte[] data, long ver) Get() => (Volatile.Read(ref _data), Interlocked.Read(ref _ver));
    }
}
