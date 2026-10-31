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
    readonly Action<int, int> _decideJob, _decideResetJob, _fieldsJob, _waterJob, _plantJob;
    public readonly int[] ActiveCount;

    /// <param name="prehistory">true for a prehistory branch (a label: every world is births and deaths only after its founders).</param>
    public Simulation(int capacity, uint seed, int threads, GeneBank.Data? bank = null, bool prehistory = false, bool empty = false, uint terrainSeed = 0, EvoSettings? evo = null)
    {
        W = new World(capacity, seed, terrainSeed);
        if (evo != null) W.Evo.CopyFrom(evo);   // before founding: it says how many ancestors are big-brained
        Terrain.Build(W);
        _pool = new Workers(Math.Max(1, threads));
        WaterCycle.Init(W, (job, n) => _pool.Run(job, n));   // rain, soil, groundwater, rivers, ponds and lakes in their seasonal state, before founders are fed
        _fieldsJob = (lo, hi) => { for (int y = lo; y < hi; y++) Fields.Row(W, y); };
        _waterJob = (lo, hi) => { for (int y = lo; y < hi; y++) WaterCycle.Row(W, y); };
        _plantJob = (lo, hi) => { for (int k = lo; k < hi; k++) Plants.StripeRow(W, k); };
        SpinUp(K.PLANT_SPINUP * Nature.SeasonLength);   // the plants grow into what their genes can hold before the animals come (Revision S)
        _active = new int[W.NS];
        ActiveCount = _active;
        W.Prehistory = prehistory && !empty;
        Snap = new Snapshot(W);

        // the visiting order of RESOLVE: k*A + B (mod Cap) with gcd(A, Cap) = 1
        int a = (int)(2654435761u % (uint)capacity) | 1;
        while (Gcd(a, capacity) != 1) a += 2;
        _orderA = a;

        // founders (DESIGN §7.9): the random ancestors, or the clades of a gene bank (NR-210, NR-212)
        if (!empty)
        {
            if (bank != null) FoundFromBank(bank, seed);
            else FoundAncestors(seed);
            Pathogens.Seed(W);   // the founding pathogen strains, in the ground and water (Revision R)
        }
        Life.Run(W, _active);   // clears newborn flags, counts

        _decideResetJob = (lo, hi) =>
        {
            // the memory-reset control (World.MemoryReset, off by default): forget the last step's activity of the
            // units in use (Brain.Forward zeroes the rest itself) and the places its brain holds; weights and learning traces are kept
            for (int i = lo; i < hi; i++) { Array.Clear(W.Hidden, i * G.NH, W.PBrain[i]); PathMemory.Clear(W, i); }
            for (int i = lo; i < hi; i++) Decide.Run(W, i, Probe);
        };
        // DECIDE visits the animals in hash-cell order (BUILD's W.Sorted: every one not dead or an embryo, so every one
        // that acts), not by slot: neighbours share cells, so the slots one worker reads stay in its cache. Each call writes
        // only its own slot and reads nothing another call writes (NR-105), so the order changes no result
        _decideJob = (lo, hi) =>
        {
            var sorted = W.Sorted;
            for (int q = lo; q < hi; q++) Decide.Run(W, sorted[q], Probe);
        };
        Seen();
    }

    /// <summary>
    /// The world without animals for this many steps (whole years: it ends in the season it started in): weather,
    /// fields, plants, fire and water. Then the clock, the weather years and the fire and water counters start again.
    /// </summary>
    void SpinUp(int steps)
    {
        var w = W;
        for (int k = 0; k < steps; k++)
        {
            Weather.Update(w);
            PlantsStep();
            Fields.Fire(w);
            if (w.Step % K.WC_EVERY == 0 && w.WaterOn) { _pool.Run(_waterJob, Map.FW); WaterCycle.Finish(w); }
            w.Step++;
        }
        w.Step = 0; w.RainYear = -1; w.RainAnom = 0f; w.T.FireCells = 0;
        w.WbRain = w.WbET = w.WbPondEvap = w.WbToLakes = w.WbLakeRain = w.WbLakeEvap = w.WbLakeSpill = 0;
    }

    /// <summary>FIELDS rows, the swap, then this step's stripe of plants and its serial commit.</summary>
    void PlantsStep()
    {
        var w = W;
        _pool.Run(_fieldsJob, Map.FW);
        Fields.Swap(w);
        Plants.BeginStep(w);
        _pool.Run(_plantJob, Plants.StripeRows);
        Plants.Commit(w);

    }

    /// <summary>AncestorsLand + AncestorsWater random ancestors, each its own clade, ANCESTOR_COUNT organisms in groups.</summary>
    void FoundAncestors(uint seed)
    {
        var w = W;
        var tmpl = new sbyte[G.LEN];
        uint key = 1;
        // big-brained ancestors (the user's experiment): the first of the land ancestors and the first of the
        // water ones, alternating, BigBrains in all; each starts with BigBrainUnits memory units. The big-brained
        // land ancestor is also the one with a voice (Revision M, the user: "ONE creature ... WAY smarter than the
        // rest, like a human species")
        int nBig = w.Evo.BigBrains, bigLand = (nBig + 1) / 2, bigWater = nBig / 2;
        int nLand = w.Evo.AncestorsLand, nAll = nLand + w.Evo.AncestorsWater;
        var homes = new System.Collections.Generic.List<(float, float)>();
        for (int a = 0; a < nAll; a++)
        {
            bool land = a < nLand;
            bool big = land ? a < bigLand : a - nLand < bigWater;
            int clade = Clades.NewClade(w, -1, a);
            var realm = land ? Realm.Land : Realm.Water;
            float gx = 0, gy = 0;
            // each ancestor has its own home range, as far as possible from the others: species meet at the edges of
            // their ranges as they spread, not everywhere at once (Revision N)
            if (!Life.PickHome(w, realm, 0x60000u + (uint)a * 977u, homes, out float hx, out float hy)) continue;
            // the ancestor's one brain and body (Revision H: unrelated random brains per founder made crossover
            // scramble networks, a hidden cost only sex paid); each founder varies around it
            // An ancestor is a lineage that can live where it lives: of up to ANCESTOR_TRIES random bodies, the first
            // whose home range can feed a founding group (FOUND_MIN) at its size, brain and diet, else the best fed.
            // Nothing says which diet or size: a water animal that eats grass, or a meat-eater with nothing to eat,
            // is simply not among the ancestors (2026-10-30: such ancestors starved in the first few hundred steps)
            var anc = new sbyte[G.LEN];
            int bestT = -1; float bestCap = 0f;
            for (int t = 0; t < K.ANCESTOR_TRIES; t++)
            {
                Ancestor(anc, seed, a, t, land, big);
                float cap = K.FOUND_FILL * ProbeCapacity(w, clade, anc, realm, hx, hy, key + 0x70000u + (uint)t, out _);
                if (cap > bestCap) { bestCap = cap; bestT = t; }
                if (cap >= K.FOUND_MIN) break;
            }
            if (bestT < 0) continue;   // no viable draw: never fall back to an incompatible or unfed body
            Ancestor(anc, seed, a, bestT, land, big);
            int units = Body.Units(anc[G.B_BRAIN]);
            // founders: FOUND_FILL of what its home range (widened for a big body, Life.RangeFor) can feed at this
            // body's size, brain and diet, at least one group and at most Founders, spread over that range
            float cap0 = ProbeCapacity(w, clade, anc, realm, hx, hy, key + 0x70000u + (uint)bestT, out float range);
            int founders = Math.Min(w.Evo.Founders, Math.Max(K.GROUP_SIZE, (int)MathF.Round(K.FOUND_FILL * cap0)));
            w.Sp[clade].HomeX = hx; w.Sp[clade].HomeY = hy; w.Sp[clade].HomeR = range;
            for (int k = 0; k < founders; k++, key++)
            {
                if (k % K.GROUP_SIZE == 0 && !Life.HomeSite(w, realm, hx, hy, key + 0x50000u, out gx, out gy, range)) break;
                anc.CopyTo(tmpl, 0);
                Brain.VaryBrain(tmpl, seed, key, units);
                Clades.VaryBody(new Span<sbyte>(tmpl, G.BODY, G.NBODY), seed, key);
                Life.Found(w, clade, tmpl, Rng.U(seed, 0, key, Rs.Founder) < 0.7f, key, gx, gy, realm);
            }
        }
    }

    /// <summary>Random ancestor genome number t of ancestor a (t = 0 is the first draw, as before 2026-10-30).</summary>
    void Ancestor(sbyte[] anc, uint seed, int a, int t, bool land, bool big)
    {
        var w = W;
        int draw = a + t * 0x10000;
        Brain.RandomGenome(anc, seed, 0, 0x700000u + (uint)draw);
        Clades.AncestorBody(new Span<sbyte>(anc, G.BODY, G.NBODY), seed, draw, land, big ? w.Evo.BigBrainUnits : 0, voiced: big && land, floorClass: a % K.FLOOR_CLASSES);   // ancestors spread over the size classes
    }

    /// <summary>What a home range can feed of animals with this genome, and the range's radius: one is made, measured and removed again.</summary>
    static float ProbeCapacity(World w, int clade, sbyte[] genome, Realm realm, float hx, float hy, uint key, out float range)
    {
        range = K.HOME_R * Map.W;
        if (!Life.HomeSite(w, realm, hx, hy, key, out float gx, out float gy)) return 0f;
        int c = Life.Found(w, clade, genome, true, key, gx, gy, realm);
        if (c < 0) return 0f;
        range = Life.RangeFor(w, c, hx, hy, out float cap);
        Life.FreeSlot(w, c);
        w.T.Created[clade]--;
        return cap;
    }

    /// <summary>
    /// How many founders a lineage starts with: FOUND_FILL of what its home range can feed (Life.RangeCapacity), at
    /// least one group and at most the given limit. A large body or a costly brain starts with fewer, a small one with
    /// more, as populations are set by food (Damuth 1981: density falls as M^-3/4).
    /// </summary>
    static int FedFounders(World w, int c, float hx, float hy, int limit, out float range)
    {
        range = Life.RangeFor(w, c, hx, hy, out float cap);
        return Math.Min(limit, Math.Max(K.GROUP_SIZE, (int)MathF.Round(K.FOUND_FILL * cap)));   // a clade with fewer than a group keeps them all
    }

    /// <summary>The clades of a gene bank, each founded with as many organisms as it had (up to ANCESTOR_COUNT x 2), in groups.</summary>
    void FoundFromBank(GeneBank.Data bank, uint seed)
    {
        var w = W;
        uint key = 1;
        var homes = new System.Collections.Generic.List<(float, float)>();
        foreach (var c in bank.Clades)
        {
            var s = w.Sp[c.Slot];
            s.Clear();
            s.Id = c.Slot; s.Used = true; s.Alive = true; s.Name = c.Name; s.R = c.R; s.G = c.G; s.B = c.B;
            s.Parent = c.Parent; s.Ancestor = c.Ancestor; s.Generation = c.Generation; s.Born = 0;
            s.Serial = w.CladeScratch.NextSerial++;
            var src = bank.Genomes[c.Slot];
            if (src.Count == 0) continue;
            // a smaller map holds fewer: founders scale with its area (a valley gets 1/16 of a continent's)
            float area = Map.W * (float)Map.W / (K.WORLD * (float)K.WORLD);
            int n = Math.Min(c.Count, Math.Max(20, (int)(K.ANCESTOR_COUNT * 2 * area)));
            float gx = 0, gy = 0, range = K.HOME_R * Map.W;
            // the clade's home range in the new world, away from the others' (Revision N)
            var home = RealmOfGenome(src[0]);
            if (!Life.PickHome(w, home, 0x61000u + (uint)c.Slot * 977u, homes, out float hx, out float hy)) continue;
            for (int k = 0; k < n; k++, key++)
            {
                var g = src[(int)(Rng.U(seed, 0, key, Rs.Bank) * src.Count)];
                if (k % K.GROUP_SIZE == 0)
                {
                    // the group's site follows the realm its genes give, within the clade's home range
                    var realm = RealmOfGenome(g);
                    if (!Life.HomeSite(w, realm, hx, hy, key + 0x50000u, out gx, out gy, range)) break;
                }
                int f = Life.Found(w, c.Slot, g, Rng.U(seed, 0, key, Rs.Founder) < 0.7f, key, gx, gy);
                if (k == 0 && f >= 0) { n = FedFounders(w, f, hx, hy, n, out range); s.HomeX = hx; s.HomeY = hy; s.HomeR = range; }   // no more than its home range can feed
            }
        }
        w.CladeScratch.Version++;
    }

    /// <summary>Where a founder from this genome can be placed: water if it swims better than it walks, else land.</summary>
    static Realm RealmOfGenome(sbyte[] g)
    {
        float legs = Body.Organ(g[G.B_LEGS]), fins = Body.Organ(g[G.B_FINS]);
        return K.SWIM_SPEED * (0.1f + 0.9f * fins) > K.CRAWL + (1f - K.CRAWL) * legs ? Realm.Water : Realm.Land;
    }

    static int Gcd(int a, int b) { while (b != 0) (a, b) = (b, a % b); return a; }

    /// <summary>Accumulated wall time per phase (ms): build, decide, resolve, life, fields, fire, telemetry.</summary>
    public readonly double[] PhaseMs = new double[7];
    long _t;
    void Lap(int k) { long t = System.Diagnostics.Stopwatch.GetTimestamp(); PhaseMs[k] += (t - _t) * 1000.0 / System.Diagnostics.Stopwatch.Frequency; _t = t; }

    // the world as the last step left it: if it is not so now, it was changed under the simulation (a checkpoint loaded
    // into it), and what is derived from the genomes (World.D) is rebuilt before the step
    ulong _seenStep = ulong.MaxValue; uint _seenSerial; int _seenFree;
    void Seen() { _seenStep = W.Step; _seenSerial = W.NextSerial; _seenFree = W.FreeTop; }

    public void Step()
    {
        var w = W;
        _t = System.Diagnostics.Stopwatch.GetTimestamp();
        if (w.Step != _seenStep || w.NextSerial != _seenSerial || w.FreeTop != _seenFree) w.D.Refresh(w);
        Build();
        UpdateProbe();
        Lap(0);
        if (w.MemoryReset) _pool.Run(_decideResetJob, w.Cap);
        else _pool.Run(_decideJob, w.CellStart[Map.GW * Map.GW]);
        Lap(1);
        int orderB = (int)(Rng.Hash(w.Seed, (uint)w.Step, 0, Rs.Order) % (uint)w.Cap);
        w.T.KillFlashN = 0;
        Resolve.Run(w, _orderA, orderB);
        Lap(2);
        Life.Run(w, _active);
        Clades.Update(w);
        Lap(3);
        Weather.Update(w);
        PlantsStep();
        Lap(4);
        Fields.Fire(w);
        // the water cycle (rows in parallel, then the routing, drinkable water and lakes in order)
        if (w.Step % K.WC_EVERY == 0 && w.WaterOn) { _pool.Run(_waterJob, Map.FW); WaterCycle.Finish(w); }
        Lap(5);
        w.Step++;
        if (w.Step % K.SAMPLE_EVERY == 0) w.T.Take();
        Snap.AfterStep();
        Seen();
        Lap(6);
    }

    /// <summary>BUILD: counting sort of every visible agent (not embryos) into the 16-unit hash grid.</summary>
    void Build() => BuildFor(W);

    /// <summary>Rebuild the spatial hash of a world (BUILD phase).</summary>
    public static void BuildFor(World w)
    {
        int n = Map.GW * Map.GW;
        Array.Clear(w.CellCount);
        for (int i = 0; i < w.Cap; i++)
        {
            byte st = w.StageOf[i];
            if (st == St.Dead || st == St.Embryo) { w.CellOf[i] = -1; continue; }
            int cx = (int)(w.X[i] / K.HCELL), cy = (int)(w.Y[i] / K.HCELL);
            if ((uint)cx >= Map.GW) cx = World.WrapI(cx, Map.GW);
            if ((uint)cy >= Map.GW) cy = World.WrapI(cy, Map.GW);
            int c = cy * Map.GW + cx;
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
        // this step's callers (Revision N): every organism that made a sound last step, sorted by hash cell like the
        // others, with how far its call carries: HEAR_R x voice x M^(1/3) (a bigger, louder animal is heard further),
        // at most MAX_SIGHT so that the 5x5 cell neighbourhood holds every caller a listener can hear. A listener scans
        // all of them, with no cap on how many
        Array.Clear(w.CellCount);
        for (int i = 0; i < w.Cap; i++)
        {
            w.CallR[i] = 0f;
            int c = w.CellOf[i];
            if (c < 0 || w.Call[i * F.MAX_SYL] == 0 || w.StageOf[i] < St.Juvenile) continue;
            w.CallR[i] = MathF.Min(K.MAX_SIGHT, K.HEAR_R * w.PVoice[i] * MathK.Cbrt(w.MassOf(i)));
            w.CellCount[c]++;
        }
        acc = 0;
        for (int c = 0; c < n; c++) { w.CallStart[c] = acc; acc += w.CellCount[c]; }
        w.CallStart[n] = acc;
        Array.Clear(w.CellCount);
        for (int i = 0; i < w.Cap; i++)
            if (w.CallR[i] > 0f) { int c = w.CellOf[i]; w.CallSorted[w.CallStart[c] + w.CellCount[c]++] = i; }
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
