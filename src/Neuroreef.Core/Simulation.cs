// The step loop (DESIGN §11). Owns the World and a fixed worker pool. Step() runs the seven phases.
// After construction nothing is allocated (NR-101): the worker pool reuses its threads and delegates.

using System;
using System.Threading;

namespace Neuroreef;

public sealed class Simulation : IDisposable
{
    public readonly World W;
    public readonly Decide.ProbeBuf Probe = new();
    public readonly Snapshot Snap;
    public uint ProbeSerial;          // 0 = none. Set by the host between steps.
    public bool ProbeFollow;          // follow the lineage when the probed animal dies
    readonly Workers _pool;
    readonly int[] _active;
    readonly int _orderA;
    readonly Action<int, int> _decideJob, _fieldsJob;
    public readonly int[] ActiveCount;

    /// <param name="prehistory">true only for an evolve run: colonists may then appear (DESIGN §7.4). A live world never spawns.</param>
    public Simulation(int capacity, uint seed, int threads, GeneBank.Data? bank = null, bool prehistory = false, bool empty = false, uint terrainSeed = 0)
    {
        W = new World(capacity, seed, terrainSeed);
        var fails = Roster.ClosureCheck(W.Sp);
        if (fails.Count > 0) throw new InvalidOperationException("energy-cycle closure check failed:\n  " + string.Join("\n  ", fails));
        Terrain.Build(W);
        _active = new int[W.NS];
        ActiveCount = _active;
        W.Prehistory = prehistory && !empty;
        Snap = new Snapshot(W);

        // the visiting order of RESOLVE: k*A + B (mod Cap) with gcd(A, Cap) = 1
        int a = (int)(2654435761u % (uint)capacity) | 1;
        while (Gcd(a, capacity) != 1) a += 2;
        _orderA = a;

        // founders: random brains, or genomes from a gene bank (NR-210, NR-212)
        uint key = 1;
        for (int s = 0; s < W.NS; s++)
        {
            var sp = W.Sp[s];
            var src = bank?.Genomes[s];
            float gx = 0, gy = 0;
            for (int k = 0; k < (empty ? 0 : sp.InitCount); k++, key++)
            {
                // founders live in groups (herds, colonies, schools) of GROUP_SIZE around a shared site
                if (k % K.GROUP_SIZE == 0 && !Life.RandomSite(W, sp.Adult.Realm, key + 0x50000u, out gx, out gy)) break;
                bool adult = Rng.U(seed, 0, key, Rs.Founder) < 0.7f;
                ReadOnlySpan<sbyte> g = (src != null && src.Count > 0) ? (ReadOnlySpan<sbyte>)src[(int)(Rng.U(seed, 0, key, Rs.Bank) * src.Count)] : ReadOnlySpan<sbyte>.Empty;
                Life.Found(W, s, g, adult, key, gx, gy);
            }
        }
        Life.Run(W, _active);   // clears newborn flags, counts

        _decideJob = (lo, hi) => { for (int i = lo; i < hi; i++) Decide.Run(W, i, Probe); };
        _fieldsJob = (lo, hi) => { for (int y = lo; y < hi; y++) Fields.Row(W, y); };
        _pool = new Workers(Math.Max(1, threads));
    }

    static int Gcd(int a, int b) { while (b != 0) (a, b) = (b, a % b); return a; }

    /// <summary>Accumulated wall time per phase (ms): build, decide, resolve, life, fields, fire, telemetry.</summary>
    public readonly double[] PhaseMs = new double[7];
    long _t;
    void Lap(int k) { long t = System.Diagnostics.Stopwatch.GetTimestamp(); PhaseMs[k] += (t - _t) * 1000.0 / System.Diagnostics.Stopwatch.Frequency; _t = t; }

    public void Step()
    {
        var w = W;
        _t = System.Diagnostics.Stopwatch.GetTimestamp();
        Build();
        UpdateProbe();
        Lap(0);
        _pool.Run(_decideJob, w.Cap);
        Lap(1);
        int orderB = (int)(Rng.Hash(w.Seed, (uint)w.Step, 0, Rs.Order) % (uint)w.Cap);
        w.T.KillFlashN = 0;
        Resolve.Run(w, _orderA, orderB);
        Lap(2);
        Life.Run(w, _active);
        Lap(3);
        Weather.Update(w);
        _pool.Run(_fieldsJob, K.FW);
        Fields.Swap(w);
        Lap(4);
        Fields.Fire(w);
        Lap(5);
        w.Step++;
        if (w.Step % K.SAMPLE_EVERY == 0) w.T.Take();
        Snap.AfterStep();
        Lap(6);
    }

    /// <summary>BUILD: counting sort of every visible agent (not embryos) into the 16-unit hash grid.</summary>
    void Build()
    {
        var w = W;
        int n = K.GW * K.GW;
        Array.Clear(w.CellCount);
        for (int i = 0; i < w.Cap; i++)
        {
            byte st = w.StageOf[i];
            if (st == St.Dead || st == St.Embryo) { w.CellOf[i] = -1; continue; }
            int cx = (int)(w.X[i] / K.HCELL), cy = (int)(w.Y[i] / K.HCELL);
            if ((uint)cx >= K.GW) cx = World.WrapI(cx, K.GW);
            if ((uint)cy >= K.GW) cy = World.WrapI(cy, K.GW);
            int c = cy * K.GW + cx;
            w.CellOf[i] = c; w.CellCount[c]++;
        }
        int acc = 0;
        for (int c = 0; c < n; c++) { w.CellStart[c] = acc; acc += w.CellCount[c]; }
        w.CellStart[n] = acc;
        Array.Clear(w.CellCount);
        for (int i = 0; i < w.Cap; i++)
        {
            int c = w.CellOf[i];
            if (c < 0) continue;
            w.Sorted[w.CellStart[c] + w.CellCount[c]++] = i;
        }
    }

    // last known facts about the probed animal, kept while it lives (its slot may be reused after death)
    int _probeSpecies = -1; float _probeX, _probeY; uint _probeChild;

    void UpdateProbe()
    {
        var w = W;
        uint ser = ProbeSerial;
        if (ser == 0) { Probe.Slot = -1; _probeSpecies = -1; return; }
        int slot = Probe.Slot;
        bool alive = slot >= 0 && w.Serial[slot] == ser && w.StageOf[slot] >= St.Juvenile;
        if (!alive) slot = FindSerial(ser);
        if (slot >= 0 && w.StageOf[slot] >= St.Juvenile)
        {
            Probe.Slot = slot;
            _probeSpecies = w.Spc[slot]; _probeX = w.X[slot]; _probeY = w.Y[slot]; _probeChild = w.LastChildSerial[slot];
            return;
        }
        // the animal is gone
        int next = -1;
        if (ProbeFollow && _probeSpecies >= 0)
        {
            // lineage follow: the youngest living child, else the nearest living conspecific
            int c = _probeChild != 0 ? FindSerial(_probeChild) : -1;
            next = (c >= 0 && w.StageOf[c] >= St.Juvenile) ? c : NearestKin(_probeSpecies, _probeX, _probeY);
        }
        Probe.Slot = next;
        ProbeSerial = next >= 0 ? w.Serial[next] : 0;
        if (next >= 0) { _probeSpecies = w.Spc[next]; _probeX = w.X[next]; _probeY = w.Y[next]; _probeChild = w.LastChildSerial[next]; }
        else _probeSpecies = -1;
    }

    public int FindSerial(uint ser)
    {
        var w = W;
        for (int i = 0; i < w.Cap; i++) if (w.Serial[i] == ser && w.StageOf[i] != St.Dead) return i;
        return -1;
    }

    int NearestKin(int species, float x, float y)
    {
        var w = W;
        int best = -1; float bd = float.MaxValue;
        for (int i = 0; i < w.Cap; i++)
        {
            if (w.StageOf[i] < St.Juvenile || w.Spc[i] != species) continue;
            float dx = World.Delta1(w.X[i] - x), dy = World.Delta1(w.Y[i] - y);
            float d = dx * dx + dy * dy;
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    /// <summary>Pick a random living member of a species (for "experience a species").</summary>
    public int PickRandom(int species, uint salt)
    {
        var w = W;
        int n = _active[species];
        if (n == 0) return -1;
        int target = (int)(Rng.U(w.Seed, (uint)w.Step, salt, Rs.Archive) * n), k = 0;
        for (int i = 0; i < w.Cap; i++)
            if (w.StageOf[i] >= St.Juvenile && w.Spc[i] == species && k++ == target) return i;
        return -1;
    }

    public void Dispose() => _pool.Dispose();

    /// <summary>
    /// A fixed pool of worker threads. Run(job, n) hands out [0, n) in small chunks through an interlocked
    /// counter, so the load balances; each element is computed independently, so the result does not
    /// depend on which thread took which chunk (NR-105). No allocation per call.
    /// </summary>
    sealed class Workers : IDisposable
    {
        readonly int _n;
        readonly Thread[] _threads;
        readonly SemaphoreSlim[] _go;
        readonly CountdownEvent _done;
        Action<int, int>? _job;
        int _total, _chunk, _next;
        volatile bool _stop;

        public Workers(int n)
        {
            _n = n;
            _go = new SemaphoreSlim[n];
            _threads = new Thread[n];
            _done = new CountdownEvent(1);
            for (int k = 1; k < n; k++)
            {
                int id = k;
                _go[k] = new SemaphoreSlim(0);
                _threads[k] = new Thread(() => Loop(id)) { IsBackground = true, Name = "neuroreef-worker-" + k };
                _threads[k].Start();
            }
        }

        void Work()
        {
            while (true)
            {
                int lo = Interlocked.Add(ref _next, _chunk) - _chunk;
                if (lo >= _total) return;
                _job!(lo, Math.Min(_total, lo + _chunk));
            }
        }

        void Loop(int id)
        {
            while (true)
            {
                _go[id].Wait();
                if (_stop) return;
                Work();
                _done.Signal();
            }
        }

        public void Run(Action<int, int> job, int total)
        {
            _job = job; _total = total;
            if (_n == 1) { job(0, total); return; }
            _chunk = Math.Max(1, Math.Min(256, total / (_n * 8)));
            _next = 0;
            _done.Reset(_n - 1);
            for (int k = 1; k < _n; k++) _go[k].Release();
            Work();
            _done.Wait();
        }

        public void Dispose()
        {
            _stop = true;
            for (int k = 1; k < _n; k++) _go[k].Release();
        }
    }
}
