// Runs the simulation on its own thread at a fixed step rate and publishes frames for the console.
// The sim thread never waits for a client (NR-506): it only fills buffers when asked, and clients take
// whatever is newest. Commands from clients arrive through a queue and are applied between steps.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace Neuroreef.Server;

public sealed class SimHost : IDisposable
{
    public readonly Simulation Sim;
    public World W => Sim.W;
    readonly Options _o;
    readonly Thread _thread;
    volatile bool _stop;
    public volatile bool Paused;
    public readonly string BankSource;
    public volatile float Rate;             // steps per second, 0 = as fast as possible
    int _stepOnce;
    public double MeasuredRate;
    readonly ConcurrentQueue<Action<Simulation>> _cmds = new();

    // ---- frames built on the sim thread on request (latest wins)
    public readonly Latest Agents = new(), FieldsF = new(), ProbeF = new();
    public volatile bool WantAgents, WantFields, WantProbe;
    readonly byte[] _fieldsBuf, _probeBuf;

    public SimHost(Options o, GeneBank.Data? bank)
    {
        _o = o;
        // the console always shows a live world: births and deaths only, never colonists
        Sim = new Simulation(o.Capacity, o.Seed, o.Threads, bank, prehistory: false);
        BankSource = o.BankSource;
        Rate = o.Rate;
        _fieldsBuf = new byte[Wire.FieldsSize];
        _probeBuf = new byte[Wire.ProbeSize];
        _thread = new Thread(Loop) { IsBackground = true, Name = "neuroreef-sim", Priority = ThreadPriority.AboveNormal };
    }

    public void Start() => _thread.Start();

    public void Post(Action<Simulation> cmd) => _cmds.Enqueue(cmd);
    public void StepOnce() => Interlocked.Exchange(ref _stepOnce, 1);

    void Loop()
    {
        var sw = Stopwatch.StartNew();
        double due = 0; long steps = 0; double lastT = 0; long lastSteps = 0;
        while (!_stop)
        {
            while (_cmds.TryDequeue(out var c)) { try { c(Sim); } catch (Exception e) { Console.Error.WriteLine("command failed: " + e.Message); } }
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
            if (!run) { Thread.Sleep(1); Publish(); continue; }

            if (WantAgents) Sim.Snap.Requested = true;
            Sim.Step();
            steps++;
            Publish();
            if (_o.SaveBank != null && W.Step % 50000 == 0) { try { GeneBank.Save(W, _o.SaveBank); } catch (Exception e) { Console.Error.WriteLine("bank save failed: " + e.Message); } }
            if (now - lastT >= 1.0) { MeasuredRate = (steps - lastSteps) / (now - lastT); lastT = now; lastSteps = steps; }
        }
    }

    void Publish()
    {
        if (WantAgents && Sim.Snap.TryTake(out var buf, out int len)) { Agents.Set(buf, len); WantAgents = false; }
        if (WantFields) { int n = Wire.EncodeFields(W, _fieldsBuf); FieldsF.Set(_fieldsBuf, n); WantFields = false; }
        if (WantProbe) { int n = Wire.EncodeProbe(Sim, _probeBuf); ProbeF.Set(_probeBuf, n); WantProbe = false; }
    }

    public void Dispose() { _stop = true; _thread.Join(2000); Sim.Dispose(); }

    /// <summary>The newest copy of a frame, shared by all clients. Set copies (on the sim thread); Get returns an immutable array.</summary>
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
