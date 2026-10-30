// Validation harness (--validate N): does brain memory help animals survive?
// Each of N seeds builds the same world twice and runs it for --steps steps:
//   NORMAL  as the simulation runs;
//   RESET   the memory-reset control (World.MemoryReset): every animal's recurrent state is zeroed before its brain runs,
//           every step. Genomes, learned weights and traces are kept; only the remembered activity is cleared.
// A run is a function of its seed only (NR-105), so the two runs of a seed start from the same map and the same founders
// and differ only by the control. The report is per seed and paired over seeds (NORMAL - RESET).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Neuroreef.Server;

public static class Validate
{
    public const long DefaultSteps = 5000;
    const int SAMPLE = 50;   // steps between samples of population and lineages

    /// <summary>What one run measured.</summary>
    public sealed class Result
    {
        public uint Seed; public bool Reset;
        public int Steps;
        public int Pop0, Lin0, Big0;            // at the start: animals, founding lineages, big-brained founding lineages
        public double Brain0;                    // mean memory units of the living at the start
        public int Pop, Lin, Big, Clades;        // at the end: animals, surviving lineages, surviving big-brained lineages, clades alive
        public int BigAnimals;                   // living animals with a brain of at least the threshold at the end
        public double Brain;                     // mean memory units of the living at the end
        public double PopMean;                   // mean population over the samples
        public double LinLife, BigLife;          // share of the run each (big-brained) founding lineage was alive, mean over lineages
        public double Sps, BuildS;               // steps per second; seconds to build the world
        public long FullSteps;                   // steps on which every slot was taken (the slot cap, not food, then limits)
    }

    public static int Run(Options o, GeneBank.Data? bank)
    {
        long steps = o.StepsSet ? o.Steps : DefaultSteps;
        if (steps <= 0) { Console.Error.WriteLine("--validate needs --steps > 0"); return 2; }
        uint seed0 = o.SeedSet ? o.Seed : 1u;
        Console.WriteLine($"NEUROREEF 2  VALIDATION: does memory help survival?  {o.ValidateSeeds} seeds from {seed0}, {steps:N0} steps each, " +
            $"{Map.W}-unit map, {o.Capacity:N0} slots, {o.Threads} threads, big brain >= {o.BigBrainMin} units");
        Console.WriteLine($"brains: {(bank != null ? $"gene bank ({bank.Total} genomes)" : "random founders (the default world without a gene bank)")}");
        Console.WriteLine("each seed runs NORMAL, then RESET (recurrent state zeroed every step; weights and learning kept)");
        Console.WriteLine();
        var all = new List<Result>();
        Console.WriteLine(Header);
        for (int k = 0; k < o.ValidateSeeds; k++)
        {
            uint seed = seed0 + (uint)k;
            foreach (bool reset in new[] { false, true })
            {
                var r = RunOne(o, bank, seed, reset, steps);
                all.Add(r);
                Console.WriteLine(Row(r));
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();   // a world is gigabytes: free it before the next
            }
        }
        Console.WriteLine();
        Console.Write(Aggregate(all, o.BigBrainMin));
        return 0;
    }

    /// <summary>One world: build it from the seed, set the control, run it, measure.</summary>
    public static Result RunOne(Options o, GeneBank.Data? bank, uint seed, bool reset, long steps)
    {
        var bw = Stopwatch.StartNew();
        using var sim = new Simulation(o.Capacity, seed, o.Threads, bank, prehistory: false, terrainSeed: seed, evo: o.WorldSettings());
        var w = sim.W;
        w.MemoryReset = reset;
        var r = new Result { Seed = seed, Reset = reset, BuildS = bw.Elapsed.TotalSeconds };
        // founding lineages: each clade descends from one ancestor (Species.Ancestor); a lineage is big-brained if its
        // founders' mean brain is at least the threshold
        var units = new Dictionary<int, (double sum, int n)>();
        Census(w, units, out r.Pop0, out r.Brain0, out _, o.BigBrainMin);
        var lineages = units.Keys.ToArray();
        var big = new HashSet<int>(units.Where(kv => kv.Value.sum / kv.Value.n >= o.BigBrainMin).Select(kv => kv.Key));
        r.Lin0 = lineages.Length; r.Big0 = big.Count;
        var aliveSamples = new Dictionary<int, int>(); foreach (int a in lineages) aliveSamples[a] = 0;
        int samples = 0; double popSum = 0;
        var sw = Stopwatch.StartNew();
        long t = 0;
        for (; t < steps; t++)
        {
            sim.Step();
            if ((t + 1) % SAMPLE == 0 || t + 1 == steps)
            {
                Census(w, units, out int pop, out _, out _, o.BigBrainMin);
                samples++; popSum += pop;
                foreach (int a in lineages) if (units.ContainsKey(a)) aliveSamples[a]++;
                if (pop == 0) { t++; break; }
            }
        }
        r.Sps = t / Math.Max(1e-3, sw.Elapsed.TotalSeconds);
        r.Steps = (int)t;
        Census(w, units, out r.Pop, out r.Brain, out r.BigAnimals, o.BigBrainMin);
        r.Lin = lineages.Count(a => units.ContainsKey(a));
        r.Big = big.Count(a => units.ContainsKey(a));
        r.Clades = 0; for (int s = 0; s < w.NS; s++) if (sim.ActiveCount[s] > 0) r.Clades++;
        r.PopMean = samples > 0 ? popSum / samples : r.Pop0;
        r.LinLife = lineages.Length > 0 && samples > 0 ? lineages.Average(a => aliveSamples[a] / (double)samples) : 0;
        r.BigLife = big.Count > 0 && samples > 0 ? big.Average(a => aliveSamples[a] / (double)samples) : double.NaN;
        r.FullSteps = w.T.FullSteps;
        return r;
    }

    /// <summary>Living animals (juveniles and adults): count, mean memory units, how many have a big brain, and per founding lineage the sum of units and count.</summary>
    static void Census(World w, Dictionary<int, (double sum, int n)> byLineage, out int pop, out double meanUnits, out int bigAnimals, int bigMin)
    {
        byLineage.Clear(); pop = 0; bigAnimals = 0; double u = 0;
        for (int i = 0; i < w.Cap; i++)
        {
            if (w.StageOf[i] < St.Juvenile) continue;
            int a = w.Sp[w.Spc[i]].Ancestor, b = w.PBrain[i];
            pop++; u += b; if (b >= bigMin) bigAnimals++;
            byLineage.TryGetValue(a, out var v); byLineage[a] = (v.sum + b, v.n + 1);
        }
        meanUnits = pop > 0 ? u / pop : 0;
    }

    const string Header =
        "  SEED  MODE    STEPS  ST/S  BUILD |  POP0   POP  POPMEAN | LIN0  LIN  LINLIFE | BIG0  BIG  BIGLIFE  BIGANIM | BRAIN0  BRAIN | CLADES  FULL";

    static string Row(Result r) =>
        $"{r.Seed,6}  {(r.Reset ? "RESET " : "NORMAL")} {r.Steps,6} {r.Sps,5:F0} {r.BuildS,5:F1}s | {r.Pop0,5} {r.Pop,5} {r.PopMean,8:F0} | {r.Lin0,4} {r.Lin,4} {r.LinLife,7:P0} | " +
        $"{r.Big0,4} {r.Big,4} {(double.IsNaN(r.BigLife) ? "    -" : r.BigLife.ToString("P0")),7} {r.BigAnimals,8} | {r.Brain0,6:F1} {r.Brain,6:F1} | {r.Clades,6} {r.FullSteps,5}";

    /// <summary>Means per mode over seeds, and the paired difference NORMAL - RESET (mean and its standard error over seeds).</summary>
    public static string Aggregate(List<Result> all, int bigMin)
    {
        var sb = new StringBuilder();
        var seeds = all.Select(r => r.Seed).Distinct().ToArray();
        var pairs = seeds.Select(s => (n: all.First(r => r.Seed == s && !r.Reset), z: all.First(r => r.Seed == s && r.Reset))).ToArray();
        (string name, Func<Result, double> f)[] m =
        {
            ("population at end", r => r.Pop),
            ("mean population", r => r.PopMean),
            ("founding lineages surviving", r => r.Lin),
            ("share of founding lineages surviving", r => r.Lin0 > 0 ? r.Lin / (double)r.Lin0 : 0),
            ("lineage lifetime (share of run)", r => r.LinLife),
            ($"big-brained lineages surviving (>= {bigMin} u)", r => r.Big),
            ("big-brained lineage lifetime", r => r.BigLife),
            ("big-brained animals at end", r => r.BigAnimals),
            ("mean brain size at end (units)", r => r.Brain),
            ("clades alive at end", r => r.Clades),
            ("steps/s", r => r.Sps),
        };
        sb.AppendLine($"AGGREGATE over {seeds.Length} seeds (mean; DIFF = NORMAL - RESET paired by seed, +- standard error; seeds with NORMAL > RESET / < RESET)");
        sb.AppendLine($"{"",-44} {"NORMAL",9} {"RESET",9} {"DIFF",9} {"+-SE",7}  N>R N<R");
        foreach (var (name, f) in m)
        {
            var d = pairs.Select(p => f(p.n) - f(p.z)).Where(v => !double.IsNaN(v)).ToArray();
            double mn = Mean(pairs.Select(p => f(p.n))), mz = Mean(pairs.Select(p => f(p.z)));
            double md = d.Length > 0 ? d.Average() : double.NaN;
            double se = d.Length > 1 ? Math.Sqrt(d.Sum(v => (v - md) * (v - md)) / (d.Length - 1) / d.Length) : double.NaN;
            sb.AppendLine($"{name,-44} {mn,9:0.###} {mz,9:0.###} {md,9:+0.###;-0.###;0} {se,7:0.###}  {d.Count(v => v > 0),3} {d.Count(v => v < 0),3}");
        }
        if (all.Any(r => r.FullSteps > 0.01 * Math.Max(1, r.Steps)))
            sb.AppendLine("WARNING: some runs were full on more than 1% of steps: the slot cap, not food, limited them (raise --capacity).");
        return sb.ToString();
    }

    static double Mean(IEnumerable<double> v) { var a = v.Where(x => !double.IsNaN(x)).ToArray(); return a.Length > 0 ? a.Average() : double.NaN; }
}
