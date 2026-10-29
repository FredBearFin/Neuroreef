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
    volatile int _beginBranch = -1;
    public string? WorldPath;                // the live world's whole state: autosaved here, resumed on the next start
    public string ExportRoot = Path.Combine(AppContext.BaseDirectory, "exports");
    double _lastSave;
    public string BankSource { get; private set; }
    public string? BankPath { get; private set; }
    public int PhaseVersion;                 // increments when the viewed simulation changes (phase or branch)
    public bool IsPrehistory => _live == null && Pre != null;
    public string Phase => IsPrehistory ? "prehistory" : "live";

    /// <summary>The simulation the console shows: the live world, or the viewed prehistory branch.</summary>
    public Simulation Sim
    {
        get
        {
            // while the prehistory turns into the world (both briefly absent), readers wait for the world
            for (int t = 0; ; t++)
            {
                var live = _live; if (live != null) return live;
                var pre = Pre; if (pre != null) return pre.B[Math.Clamp(ViewBranch, 0, pre.B.Length - 1)];
                Thread.Sleep(10);
            }
        }
    }
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
        WorldPath = o.NoCheckpoint ? null : Path.Combine(AppContext.BaseDirectory, "world.nrck");
        if (o.ResumeWorld && WorldPath != null)
        {
            // continue a saved world exactly where it was: build an empty world of its size, then load its state
            var (cap, seed, ts) = Checkpoint.WorldHeader(WorldPath);
            _live = new Simulation(cap, seed, o.Threads, null, prehistory: false, empty: true, terrainSeed: ts);
            Checkpoint.LoadWorld(_live.W, WorldPath);
            BankSource = "saved world";
            Console.WriteLine($"continuing your saved world at step {_live.W.Step:N0} ({_live.W.AliveCount:N0} alive)");
        }
        else if (bank != null || o.RandomBrains)
            _live = new Simulation(o.Capacity, o.Seed, o.Threads, bank, prehistory: false, terrainSeed: o.Seed, evo: o.WorldSettings());
        else
        {
            Pre = new Prehistory(o.Branches, o.BranchSlots, o.Seed, o.Threads, o.PrehistorySettings(stepsAreLength: false));
            BankSource = "prehistory";
            Rate = 0;   // a prehistory runs as fast as possible until the console asks for a speed
            if (o.CheckpointPath != null)
            {
                Pre.CheckpointPath = o.CheckpointPath; Pre.CheckpointMinutes = o.CheckpointMinutes;
                if (o.ResumePrehistory)
                {
                    Pre.LoadCheckpoint(o.CheckpointPath);   // a checkpoint that does not fit stops the program with its reason
                    Console.WriteLine($"resumed the prehistory at step {Pre.Step:N0}");
                }
                else if (File.Exists(o.CheckpointPath))
                {
                    File.Move(o.CheckpointPath, o.CheckpointPath + ".old", overwrite: true);   // --prehistory: a new one; keep the old state
                    Console.WriteLine("a new prehistory: the previous one's state was kept as " + o.CheckpointPath + ".old");
                }
            }
        }
        _thread = new Thread(Loop) { IsBackground = true, Name = "neuroreef-host", Priority = ThreadPriority.AboveNormal };
    }

    public void Start() { Pre?.Start(); _thread.Start(); }

    public void Post(Action<Simulation> cmd) => _cmds.Enqueue(cmd);
    public void StepOnce() => Interlocked.Exchange(ref _stepOnce, 1);
    public void BeginWorld(int branch = -1) { _beginBranch = branch; _begin = true; }

    /// <summary>Folder the current world (or viewed branch) exports to.</summary>
    public string ExportDir => Path.Combine(ExportRoot, IsPrehistory ? $"prehistory-branch-{ViewBranch}" : $"world-seed{W.Seed}");

    /// <summary>Write the export on the simulation thread; completes with the folder (or an error).</summary>
    public System.Threading.Tasks.Task<string> RequestExport()
    {
        var tcs = new System.Threading.Tasks.TaskCompletionSource<string>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        Post(s => { try { tcs.SetResult(Export.Write(s, ExportDir)); } catch (Exception e) { tcs.SetException(e); } });
        return tcs.Task;
    }

    /// <summary>Save the live world's whole state and its data export (call on the simulation thread, or after Stop).</summary>
    public void SaveLive()
    {
        var sim = _live;
        if (sim == null) return;
        if (WorldPath != null)
            try { Checkpoint.SaveWorld(sim.W, WorldPath); } catch (Exception e) { Console.Error.WriteLine("world save failed: " + e.Message); }
        try { Export.Write(sim, ExportDir); } catch (Exception e) { Console.Error.WriteLine("export failed: " + e.Message); }
    }

    /// <summary>Stop the simulation thread, then save (the program is closing).</summary>
    public void StopAndSave()
    {
        _stop = true;
        if (_thread.IsAlive) _thread.Join(30000);
        if (_live != null)
        {
            SaveLive();
            Console.WriteLine($"world saved at step {_live.W.Step:N0}: start Neuroreef.exe again to continue it. Data: {ExportDir}");
        }
    }

    public void SetPaused(bool p) { Paused = p; if (Pre != null) Pre.Paused = p; }

    /// <summary>The console's speed (steps per second, 0 = as fast as possible), for the world and the prehistory alike.</summary>
    bool _rateChosen;   // the console set a speed (kept when the world begins)
    public void SetRate(float r) { Rate = r; _rateChosen = true; var pre = Pre; if (pre != null) pre.RateLimit = r; }

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
            if (now - _lastSave >= _o.CheckpointMinutes * 60) { _lastSave = now; SaveLive(); }
            if (now - lastT >= 1.0) { MeasuredRate = (steps - lastSteps) / (now - lastT); lastT = now; lastSteps = steps; }
        }
    }

    /// <summary>End the prehistory: save its brains, then seed the live world from them (births and deaths only from here).</summary>
    void EnterLive()
    {
        var pre = Pre!;
        pre.StopAndWait();
        if (!_rateChosen) Rate = _o.Rate;   // the prehistory ran flat out; the world runs at the speed chosen in setup
        Console.WriteLine($"prehistory ended at step {pre.Step:N0} ({pre.Step * (ulong)K.YEARS_PER_STEP / 1_000_000:N0} million symbolic years)");
        // every branch is kept: its brains (branch-N.nrbank) and its data (exports/prehistory-branch-N)
        for (int b = 0; b < pre.B.Length; b++)
        {
            try { GeneBank.Save(pre.B[b].W, Path.Combine(AppContext.BaseDirectory, $"branch-{b}.nrbank")); } catch (Exception e) { Console.Error.WriteLine(e.Message); }
            try { Export.Write(pre.B[b], Path.Combine(ExportRoot, $"prehistory-branch-{b}")); } catch (Exception e) { Console.Error.WriteLine(e.Message); }
        }
        int chosen = _beginBranch >= 0 && _beginBranch < pre.B.Length ? _beginBranch : pre.BestBranch();
        Console.WriteLine($"all branches saved as branch-0..{pre.B.Length - 1}.nrbank; the world begins from branch {chosen}" + (_beginBranch < 0 ? " (most self-sustaining clades)" : " (your choice)"));
        Console.WriteLine("  to start a world from another branch later: Neuroreef.exe --load-bank branch-N.nrbank");
        var ms = new MemoryStream(); GeneBank.Save(pre.B[chosen].W, ms); ms.Position = 0;
        var bank = GeneBank.Load(ms);
        string path = _o.SaveBank ?? Path.Combine(AppContext.BaseDirectory, "evolved.nrbank");
        try { GeneBank.Save(pre.B[chosen].W, path); BankPath = path; Console.WriteLine("evolved brains saved: " + path + " (the next start uses them)"); }
        catch (Exception e) { Console.Error.WriteLine("could not save the evolved brains: " + e.Message); }
        // the prehistory is over: its state is kept, but the next start will not resume it
        if (pre.CheckpointPath != null && File.Exists(pre.CheckpointPath))
            try { File.Move(pre.CheckpointPath, pre.CheckpointPath + ".done", overwrite: true); } catch (Exception e) { Console.Error.WriteLine(e.Message); }
        // the world gets its own map (a valley after a big prehistory): free the branches first, then build it
        Pre = null;
        pre.Dispose();
        if (Map.W != Nature.WorldMap) { Map.Set(Nature.WorldMap); Console.WriteLine($"the world is a {Map.Name} ({Map.W} units)"); }
        var live = new Simulation(_o.Capacity, _o.Seed, _o.Threads, bank, prehistory: false, terrainSeed: _o.Seed, evo: _o.WorldSettings());
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
