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
    readonly Action<int, int> _decideJob, _decideResetJob, _fieldsJob, _waterJob, _plantJob;
    public readonly int[] ActiveCount;

    /// <param name="prehistory">true for a prehistory branch (a label: every world is births and deaths only after its founders).</param>
    /// <param name="restore">its whole state comes from a save next (Checkpoint.LoadWorld or Load): no land is built and
    /// no founders made, only to be overwritten. Checkpoint builds the land if an older save needs it (BuildLand).</param>
    /// <param name="land">the land made already for this world (Land.Fits): its relief, or the whole spun-up land. Ignored if it does not fit.</param>
    /// <param name="keepLand">keep the spun-up land before the founders (StartLand), for another world of the same seed to begin on</param>
    /// <param name="capacity">the slots it starts with: it grows as births need more (World.Grow)</param>
    /// <param name="maxCapacity">the most slots it may grow to (0: as many as memory holds)</param>
    public Simulation(int capacity, uint seed, int threads, GeneBank.Data? bank = null, bool prehistory = false, bool empty = false, uint terrainSeed = 0, EvoSettings? evo = null,
        bool restore = false, Land? land = null, bool keepLand = false, int maxCapacity = 0)
    {
        W = new World(capacity, seed, terrainSeed);
        if (maxCapacity > 0) W.MaxCap = Math.Clamp(maxCapacity, capacity, World.MaxSlots);
        if (evo != null) W.Evo.CopyFrom(evo);   // before founding: it says how many ancestors are big-brained
        _pool = new Workers(Math.Max(1, threads));
        _fieldsJob = (lo, hi) => { for (int y = lo; y < hi; y++) Fields.Row(W, y); };
        _waterJob = (lo, hi) => { for (int y = lo; y < hi; y++) WaterCycle.Row(W, y); };
        _plantJob = (lo, hi) => { for (int k = lo; k < hi; k++) Plants.StripeRow(W, k); };
        if (!restore)
        {
            BuildLand(land != null && land.Fits(W) ? land : null);
            if (keepLand) StartLand = Land.Grown(W);
        }
        _active = new int[W.NS];
        ActiveCount = _active;
        W.Prehistory = prehistory && !empty && !restore;
        Snap = new Snapshot(W);

        // founders (DESIGN §7.9): the random ancestors, or the clades of a gene bank (NR-210, NR-212)
        if (!empty && !restore)
        {
            if (bank != null) FoundFromBank(bank, seed);
            else FoundAncestors(seed);
            Pathogens.Seed(W);   // the founding pathogen strains, in the ground and water (Revision R)
        }
        Life.Run(W, _active);   // clears newborn flags, counts
        W.Reserve();            // free slots for the first step's births

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

    /// <summary>The spun-up land before the founders came (with keepLand), for a world of the same seed to begin on.</summary>
    public Land? StartLand { get; private set; }

    /// <summary>
    /// The land as world creation makes it: terrain, then the water cycle in its seasonal state, then the plants grown for
    /// PLANT_SPINUP years (Revision S), all before any animal. From a fitting Land, what it holds is copied instead.
    /// Also used by Checkpoint to rebuild the land of an older save.
    /// </summary>
    public void BuildLand(Land? from = null)
    {
        if (from != null && from.Is == Land.Stage.Grown) { from.Into(W); return; }
        var rain = from?.Into(W);
        if (rain != null) Terrain.Finish(W, rain); else Terrain.Build(W);
        WaterCycle.Init(W, (job, n) => _pool.Run(job, n));   // rain, soil, groundwater, rivers, ponds and lakes in their seasonal state, before founders are fed
        SpinUp(K.PLANT_SPINUP * Nature.SeasonLength);   // the plants grow into what their genes can hold before the animals come (Revision S)
        Wetlands.Holocene(W);   // the peat its waterlogged ground laid down since the last glaciation, and the wetlands' kinds (2026-11-01)
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
        // ancestors whose test waits until the others are founded (2026-11-01, below): ancestor, clade, home
        var later = new System.Collections.Generic.List<(int a, int clade, float hx, float hy, uint probe)>();
        for (int a = 0; a < nAll; a++)
        {
            bool land = a < nLand;
            int clade = Clades.NewClade(w, -1, a);
            var realm = land ? Realm.Land : Realm.Water;
            // each ancestor has its own home range, as far as possible from the others: species meet at the edges of
            // their ranges as they spread, not everywhere at once (Revision N)
            if (!Life.PickHome(w, realm, 0x60000u + (uint)a * 977u, homes, out float hx, out float hy)) continue;
            if (!FoundAncestor(seed, a, clade, hx, hy, nLand, bigLand, bigWater, tmpl, key, ref key, mayWait: true)) later.Add((a, clade, hx, hy, key));
        }
        // the ancestors that eat mostly what animals make are tested now, on the animals living in their ranges, in the
        // order they were drawn (2026-11-01): prey before predators, as food webs are built from the bottom (Life.AnimalFood)
        foreach (var (a, clade, hx, hy, probe) in later) { WaitedClades.Add(clade); FoundAncestor(seed, a, clade, hx, hy, nLand, bigLand, bigWater, tmpl, probe, ref key, mayWait: false); }
    }

    /// <summary>The genome of each clade's founded ancestor, the body the founding judged viable (null if the clade was not
    /// founded); its founders vary around it (Clades.VaryBody, Brain.VaryBrain). Kept for this run only, not saved.</summary>
    public readonly sbyte[]?[] FoundedAncestors = new sbyte[]?[K.CLADES];

    /// <summary>The clades whose ancestors were tested after the others, on the animals living in their ranges (reporting).</summary>
    public readonly System.Collections.Generic.List<int> WaitedClades = new();

    /// <summary>
    /// Draw ancestor a's body and found its lineage around (hx, hy). An ancestor is a lineage that can live where it
    /// lives: of up to ANCESTOR_TRIES random bodies, the first whose home range can feed, at FOUND_FILL, the smallest group
    /// that can breed (Life.BreedingGroup) at its size, brain and diet, on the food that is there when it is tested
    /// (Life.RangeCapacity); none if no draw can. It starts with as many founders as that food feeds, up to Founders:
    /// a small group may die out by chance, as small founding populations do.
    /// Nothing says which diet or size: a water animal that eats grass, or a meat-eater with nothing to eat, is simply not
    /// among the ancestors (2026-10-30: such ancestors starved in the first few hundred steps). With mayWait, a body that
    /// gets most from what animals make (meat, blood, carrion, dung) and cannot yet be fed stops the draws and returns
    /// false: the ancestor is tested again once every other ancestor is founded, as its food is made by them
    /// (2026-11-01: before, only growing plant food counted, so meat-eaters were never founded). The draws and the test
    /// are the same either way (the probes are keyed by probeKey, the same both times); only when it is tested changes.
    /// </summary>
    bool FoundAncestor(uint seed, int a, int clade, float hx, float hy, int nLand, int bigLand, int bigWater, sbyte[] tmpl, uint probeKey, ref uint key, bool mayWait)
    {
        var w = W;
        bool land = a < nLand;
        bool big = land ? a < bigLand : a - nLand < bigWater;
        var realm = land ? Realm.Land : Realm.Water;
        float gx = 0, gy = 0;
        // the ancestor's one brain and body (Revision H: unrelated random brains per founder made crossover
        // scramble networks, a hidden cost only sex paid); each founder varies around it
        var anc = new sbyte[G.LEN];
        int bestT = -1;
        for (int t = 0; t < K.ANCESTOR_TRIES; t++)
        {
            Ancestor(anc, seed, a, t, land, big);
            float cap = K.FOUND_FILL * ProbeCapacity(w, clade, anc, realm, hx, hy, probeKey + 0x70000u + (uint)t, out _, out bool animalFood, out int group);
            // (2026-11-01) viable when its range feeds the smallest group that can breed (Life.BreedingGroup: a pair, or
            // one if it breeds alone), not a fixed FOUND_MIN = 50; the first viable draw is the ancestor
            bool viable = FoundersFed(cap / K.FOUND_FILL, group, w.Evo.Founders) > 0;
            if (mayWait && animalFood && !viable) return false;
            if (viable) { bestT = t; break; }
        }
        // no draw its range can feed a breeding group of: no lineage (before 2026-11-01 the best fed was founded with at
        // least GROUP_SIZE founders, however little fed them)
        if (bestT < 0) return true;
        Ancestor(anc, seed, a, bestT, land, big);
        FoundedAncestors[clade] = (sbyte[])anc.Clone();
        int units = Body.Units(anc[G.B_BRAIN]);
        // founders: FOUND_FILL of what its home range (widened until it feeds Founders, Life.RangeFor) can feed at this
        // body's size, brain and diet, at most Founders, spread over that range in groups of up to GROUP_SIZE
        float cap0 = ProbeCapacity(w, clade, anc, realm, hx, hy, probeKey + 0x70000u + (uint)bestT, out float range, out _, out int group0);
        int founders = FoundersFed(cap0, group0, w.Evo.Founders);
        w.Sp[clade].HomeX = hx; w.Sp[clade].HomeY = hy; w.Sp[clade].HomeR = range;
        for (int k = 0; k < founders; k++, key++)
        {
            if (k % K.GROUP_SIZE == 0 && !Life.HomeSite(w, realm, hx, hy, key + 0x50000u, out gx, out gy, range)) break;
            anc.CopyTo(tmpl, 0);
            Brain.VaryBrain(tmpl, seed, key, units);
            Clades.VaryBody(new Span<sbyte>(tmpl, G.BODY, G.NBODY), seed, key);
            Life.Found(w, clade, tmpl, Rng.U(seed, 0, key, Rs.Founder) < 0.7f, key, gx, gy, realm);
        }
        return true;
    }

    /// <summary>Does the diet of organism c get the most energy per kilogram from food that animals make (meat, blood,
    /// carrion, dung), rather than from plants and what falls from them?</summary>
    public static bool EatsAnimalFood(World w, int c)
    {
        var d = w.DietOf(c);
        float plant = 0f, animal = 0f;
        for (int p = 0; p < Pools.N; p++)
        {
            float e = d[p] * Pools.Rho[p];
            if (p == (int)Pool.Meat || p == (int)Pool.Blood || p == (int)Pool.Carrion || p == (int)Pool.Dung) animal = MathF.Max(animal, e);
            else plant = MathF.Max(plant, e);
        }
        return animal > plant;
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
    static float ProbeCapacity(World w, int clade, sbyte[] genome, Realm realm, float hx, float hy, uint key, out float range, out bool animalFood, out int group)
    {
        range = K.HOME_R * Map.W; animalFood = false; group = 2;
        if (!Life.HomeSite(w, realm, hx, hy, key, out float gx, out float gy)) return 0f;
        int c = Life.Found(w, clade, genome, true, key, gx, gy, realm);
        if (c < 0) return 0f;
        animalFood = EatsAnimalFood(w, c); group = Life.BreedingGroup(w, c);
        // it must breathe where its founders are put, in the realm it is founded in (2026-11-01: a gill-breather founded
        // on land, or an air-breather that cannot keep its head up in the water it is founded in, would not live a step)
        if (!Respiration.Breathable(w, c, World.FIdx(w.X[c], w.Y[c]))) { Life.FreeSlot(w, c); w.T.Created[clade]--; return 0f; }
        // what it can live on in the realm it is founded in (2026-11-01): a water lineage on water foods, where it can breathe
        // in water at the depth it holds, its organs' upkeep and running costs paid (before, a wading ancestor put in
        // water was fed by the land around it, and water lineages lived ashore on grass and roots, with lungs)
        range = Life.RangeFor(w, c, hx, hy, out float cap, habitat: realm);
        Life.FreeSlot(w, c);
        w.T.Created[clade]--;
        return cap;
    }

    /// <summary>
    /// How many founders a lineage starts with: FOUND_FILL of what its home range can feed (Life.RangeCapacity), at most
    /// the given limit; none if that is fewer than a breeding group (Life.BreedingGroup, 2026-11-01). A large body or a costly brain starts with fewer, a small one with
    /// more, as populations are set by food (Damuth 1981: density falls as M^-3/4).
    /// </summary>
    public static int FedFounders(World w, int c, float hx, float hy, int limit, out float range)
    {
        range = Life.RangeFor(w, c, hx, hy, out float cap, limit);
        return FoundersFed(cap, Life.BreedingGroup(w, c), limit);
    }

    /// <summary>Founders for a home range that can feed cap animals: FOUND_FILL of them, at most limit, and none if that
    /// is fewer than a breeding group (2026-11-01; replaces the fixed FOUND_MIN = 50 and the GROUP_SIZE floor).</summary>
    public static int FoundersFed(float cap, int group, int limit)
    {
        int n = Math.Min(limit, (int)MathF.Round(K.FOUND_FILL * cap));
        return n >= group ? n : 0;
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
                if (k == 0 && f >= 0)
                {
                    n = FedFounders(w, f, hx, hy, n, out range); s.HomeX = hx; s.HomeY = hy; s.HomeR = range;   // no more than its home range can feed
                    if (n == 0) { Life.FreeSlot(w, f); w.T.Created[c.Slot]--; }   // not even a breeding group (2026-11-01)
                }
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
        w.Player?.BeforeStep(w);   // EXPLORE: the person's speed and scent (nothing when no one explores)
        Build();
        UpdateProbe();
        Lap(0);
        if (w.MemoryReset) _pool.Run(_decideResetJob, w.Cap);
        else _pool.Run(_decideJob, w.CellStart[Map.GW * Map.GW]);
        Lap(1);
        // the visiting order of RESOLVE: k x A + B over a power of two n above every slot in use (Resolve.Run), A odd
        int n = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(1, w.SlotsUsed));
        int orderA = (int)(2654435761u & (uint)(n - 1)) | 1;
        int orderB = (int)(Rng.Hash(w.Seed, (uint)w.Step, 0, Rs.Order) & (uint)(n - 1));
        w.T.KillFlashN = 0;
        Resolve.Run(w, n, orderA, orderB);
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
        w.Reserve();   // grow between steps if the next one could run short of slots (World.Reserve)
        Seen();
        Lap(6);
    }

    /// <summary>BUILD: counting sort of every visible agent (not embryos) into the HCELL-unit hash grid.</summary>
    void Build() => BuildFor(W);

    /// <summary>Rebuild the spatial hash of a world (BUILD phase).</summary>
    public static unsafe void BuildFor(World w)
    {
        int n = Map.GW * Map.GW;
        Array.Clear(w.CellCount);
        for (int i = 0; i < w.Cap; i++)
        {
            byte st = w.StageOf[i];
            if (st == St.Dead || st == St.Embryo) { w.CellOf[i] = -1; continue; }
            int cx = (int)(w.X[i] / Map.HCell), cy = (int)(w.Y[i] / Map.HCell);
            if ((uint)cx >= Map.GW) cx = World.WrapI(cx, Map.GW);
            if ((uint)cy >= Map.GW) cy = World.WrapI(cy, Map.GW);
            int c = cy * Map.GW + cx;
            w.CellOf[i] = c; w.CellCount[c]++;
        }
        int acc = 0;
        for (int c = 0; c < n; c++) { w.CellStart[c] = acc; acc += w.CellCount[c]; }
        w.CellStart[n] = acc;
        Array.Clear(w.CellCount);
        var near = w.D.Near; var look = w.D.Look;
        for (int i = 0; i < w.Cap; i++)
        {
            int c = w.CellOf[i];
            if (c < 0) continue;
            int q = w.CellStart[c] + w.CellCount[c]++;
            w.Sorted[q] = i;
            // what DECIDE reads of it as a neighbour, packed (World.Neighbour)
            ref var nb = ref near[q];
            float h = w.Heading[i], m = w.ApparentMass(i);
            nb.X = w.X[i]; nb.Y = w.Y[i]; nb.HCos = MathK.Cos(h); nb.HSin = MathK.Sin(h);
            nb.Mass = m; nb.MassCbrt = MathK.Cbrt(m); nb.Shed = w.PgShed[i];
            nb.Slot = i; nb.Serial = w.Serial[i]; nb.Stage = w.StageOf[i];
            fixed (sbyte* dst = nb.Look) new ReadOnlySpan<sbyte>(look, i * Body.NLOOK, Body.NLOOK).CopyTo(new Span<sbyte>(dst, Body.NLOOK));
        }
        // this step's callers (Revision N): every organism that made a sound last step, sorted by the callers' own hash
        // (HCELL_CALL, coarser than the bodies' since 2026-11-01: a call carries kilometres, sight a few hundred metres),
        // with how far its call carries: HEAR_R x voice x M^(1/3) (a bigger, louder animal is heard further), at most
        // MAX_HEAR, so that the 5x5 callers' cells around a listener hold every caller it can hear. A listener scans all
        // of them, with no cap on how many
        int nc = Map.CGW * Map.CGW;
        var callCount = w.CellCount;   // (GW^2 >= CGW^2 cells: Map.HCellCall >= Map.HCell)
        Array.Clear(callCount);
        for (int i = 0; i < w.Cap; i++)
        {
            w.CallR[i] = 0f;
            if (w.CellOf[i] < 0 || w.Call[i * F.MAX_SYL] == 0 || w.StageOf[i] < St.Juvenile) continue;
            w.CallR[i] = MathF.Min(BodySize.MaxHear, K.HEAR_R * w.PVoice[i] * MathK.Cbrt(w.MassOf(i)));
            callCount[CallCell(w.X[i], w.Y[i])]++;
        }
        acc = 0;
        for (int c = 0; c < nc; c++) { w.CallStart[c] = acc; acc += callCount[c]; }
        w.CallStart[nc] = acc;
        Array.Clear(callCount);
        for (int i = 0; i < w.Cap; i++)
            if (w.CallR[i] > 0f) { int c = CallCell(w.X[i], w.Y[i]); w.CallSorted[w.CallStart[c] + callCount[c]++] = i; }
    }

    /// <summary>The callers' hash cell (Map.HCellCall units a side) of a place.</summary>
    public static int CallCell(float x, float y)
    {
        int cx = (int)(x / Map.HCellCall), cy = (int)(y / Map.HCellCall);
        if ((uint)cx >= Map.CGW) cx = World.WrapI(cx, Map.CGW);
        if ((uint)cy >= Map.CGW) cy = World.WrapI(cy, Map.CGW);
        return cy * Map.CGW + cx;
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
            // about 32 chunks per thread, at most 64 elements (2026-10-31: was 8 per thread, up to 256): DECIDE runs in hash
            // order, so a herd of big-brained animals sits in a few chunks, and big chunks left one thread with a long tail
            _chunk = Math.Max(1, Math.Min(64, total / (_n * 32)));
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
