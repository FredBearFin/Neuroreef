// Headless mode: run without the console, print tabulated line-printer reports, write CSV and banks.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Neuroreef.Server;

public static class Headless
{
    public static int Run(Options o, GeneBank.Data? bank)
    {
        using var sim = new Simulation(o.Capacity, o.Seed, o.Threads, bank, prehistory: false, empty: o.Empty, terrainSeed: o.Seed);
        var w = sim.W;
        Console.WriteLine($"NEUROREEF 2  HEADLESS RUN  seed {o.Seed}  capacity {o.Capacity}  threads {o.Threads}  steps {o.Steps}");
        Console.WriteLine($"start: {(bank != null ? $"the world, brains from gene bank ({bank.Total} genomes), no spawning" : "the world, RANDOM BRAINS, no spawning")}   alive {w.AliveCount}");
        Header(w);
        var sw = Stopwatch.StartNew();
        long lastT = 0; ulong lastStep = 0;
        bool stop = false;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop = true; Console.WriteLine("stopping after this step (saving)..."); };
        for (long k = 0; k < o.Steps && !stop; k++)
        {
            sim.Step();
            if (w.Step % (ulong)o.ReportEvery == 0)
            {
                double dt = (sw.ElapsedMilliseconds - lastT) / 1000.0;
                Report(sim, (w.Step - lastStep) / Math.Max(dt, 1e-3));
                lastT = sw.ElapsedMilliseconds; lastStep = w.Step;
            }
            if (o.SaveBank != null && w.Step % 50000 == 0) GeneBank.Save(w, o.SaveBank);
        }
        Console.WriteLine($"done: {w.Step} steps in {sw.Elapsed.TotalSeconds:F1} s ({w.Step / sw.Elapsed.TotalSeconds:F0} steps/s)");
        string summary = Summary(sim);
        Console.Write(summary);
        if (o.ReportTxt != null) { File.WriteAllText(o.ReportTxt, summary); Console.WriteLine("report written: " + o.ReportTxt); }
        if (o.SaveBank != null) { GeneBank.Save(w, o.SaveBank); Console.WriteLine("gene bank saved: " + o.SaveBank); }
        if (o.Csv != null) { WriteCsv(w, o.Csv); Console.WriteLine("telemetry written: " + o.Csv); }
        return 0;
    }

    /// <summary>The prehistory without the console (--evolve / Evolve.bat).</summary>
    public static int Evolve(Options o)
    {
        var e = o.PrehistorySettings(stepsAreLength: true);
        using var pre = new Prehistory(o.Branches, K.BRANCH_CAPACITY, o.Seed, o.Threads, e);
        var s0 = pre.Pending;
        Console.WriteLine("NEUROREEF 2  PREHISTORY");
        Console.WriteLine($"{pre.B.Length} separate worlds evolve over {s0.TotalSteps:N0} steps ({(long)s0.TotalSteps * K.YEARS_PER_STEP / 1_000_000:N0} million symbolic years).");
        Console.WriteLine("Each starts from founders and is then births and deaths only: nothing is ever added. A species that dies out is gone.");
        Console.WriteLine("The world you watch starts from the brains this saves:");
        Console.WriteLine("  " + o.SaveBank);
        Console.WriteLine("Leave this window open. Ctrl+C stops early and saves.");
        Console.WriteLine();
        Console.Write("     STEP  MYR  ST/S ");
        for (int b = 0; b < pre.B.Length; b++) Console.Write($"| BRANCH {b}: alive  self-sustaining species ");
        Console.WriteLine();
        bool stop = false;
        Console.CancelKeyPress += (_, ev) => { ev.Cancel = true; stop = true; Console.WriteLine("stopping (saving)..."); };
        pre.Start();
        var sw = Stopwatch.StartNew();
        ulong lastReport = 0;
        while (!pre.Finished && !stop)
        {
            System.Threading.Thread.Sleep(500);
            if (pre.Step - lastReport >= (ulong)o.ReportEvery || pre.Finished)
            {
                lastReport = pre.Step;
                var sb = new StringBuilder();
                sb.Append($"{pre.Step,9:N0} {pre.Step * (ulong)K.YEARS_PER_STEP / 1_000_000,4} {pre.Rate,5:F0} ");
                foreach (var b in pre.B)
                {
                    int self = 0;
                    for (int s = 0; s < b.W.NS; s++) if (Prehistory.StandingOf(b, s, out _, out _) == Prehistory.Standing.SelfSustaining) self++;
                    sb.Append($"| {b.W.AliveCount,15:N0}  {self,2} of {b.W.NS}                ");
                }
                Console.WriteLine(sb.ToString());
                if (o.SaveBank != null && pre.Step % 50000 < (ulong)Prehistory.SYNC) { try { pre.SaveBank(o.SaveBank); } catch (Exception ex) { Console.Error.WriteLine("save failed: " + ex.Message); } }
            }
        }
        pre.StopAndWait();
        Console.WriteLine($"done: {pre.Step:N0} steps in {sw.Elapsed.TotalMinutes:F1} min");
        var report = new StringBuilder();
        report.AppendLine($"NEUROREEF 2 PREHISTORY REPORT: {pre.Step:N0} steps, {pre.B.Length} branches (separate births-only worlds), seed {o.Seed}");
        report.AppendLine("Standing over the last ~1,000 steps: SELF = births keep up with deaths; BREEDING = births, but fewer than deaths; DYING = alive, no births; GONE = extinct.");
        int bestB = pre.BestBranch();
        for (int b = 0; b < pre.B.Length; b++)
        {
            var bw = pre.B[b].W;
            report.AppendLine();
            report.AppendLine($"BRANCH {b}{(b == bestB ? "  (the world begins from this branch)" : "")}: {bw.AliveCount:N0} organisms, {bw.T.Splits} speciation events");
            report.AppendLine("CLADE        STANDING        ALIVE  FROM            ANCESTOR  BORN AT    NOW");
            var desc = Wire.Clades(bw);
            for (int s = 0; s < K.CLADES; s++)
            {
                var c = bw.Sp[s];
                if (!c.Used) continue;
                var st = Prehistory.StandingOf(pre.B[b], s, out int alive, out _);
                string from = c.Parent >= 0 ? "split from " + bw.Sp[c.Parent].Name : "ancestor";
                string now = (string)desc[s].GetType().GetProperty("niche")!.GetValue(desc[s])!;
                report.AppendLine($"{c.Name,-12} {st.ToString().ToUpperInvariant().Replace("SELFSUSTAINING", "SELF"),-14} {alive,6}  {from,-15} {c.Ancestor,8}  {c.Born,8:N0}  {now}");
            }
        }
        report.AppendLine();
        report.AppendLine($"WHAT EVOLVED in branch {bestB}: per clade, first sample with organisms -> last (mass, legs, fins, wings, armour, strike, meat-eating, starter-kit gene)");
        {
            var T = pre.B[bestB].W.T; var row = new float[T.Len];
            int[] tr = { Telemetry.T_MASS, Telemetry.T_LEGS, Telemetry.T_FINS, Telemetry.T_WINGS, Telemetry.T_ARMOUR, Telemetry.T_STRIKE, Telemetry.T_MEAT };
            for (int s = 0; s < K.CLADES; s++)
            {
                if (!pre.B[bestB].W.Sp[s].Used) continue;
                float[] f0 = new float[tr.Length + 1], f1 = new float[tr.Length + 1]; bool seen = false;
                for (int k = 0; k < T.SampleCount; k++)
                {
                    T.Sample(k, row);
                    if (row[T.SpeciesOffset(s, Telemetry.S_COUNT)] <= 0) continue;
                    for (int q = 0; q < tr.Length; q++) f1[q] = row[T.SpeciesOffset(s, Telemetry.S_TRAITS + tr[q])];
                    f1[tr.Length] = row[T.SpeciesOffset(s, Telemetry.S_KIT)];
                    if (!seen) { Array.Copy(f1, f0, f0.Length); seen = true; }
                }
                if (!seen) continue;
                report.AppendLine($"{pre.B[bestB].W.Sp[s].Name,-12} " + string.Join("  ", Enumerable.Range(0, f0.Length).Select(q => $"{f0[q]:0.##}->{f1[q]:0.##}")));
            }
        }
        for (int b = 0; b < pre.B.Length; b++) { report.AppendLine(); report.AppendLine($"=== BRANCH {b} ==="); report.Append(Summary(pre.B[b])); }
        Console.Write(report.ToString());
        if (o.ReportTxt != null) { File.WriteAllText(o.ReportTxt, report.ToString()); Console.WriteLine("report written: " + o.ReportTxt); }
        if (o.SaveBank != null) { pre.SaveBank(o.SaveBank); Console.WriteLine("evolved brains saved: " + o.SaveBank); }
        if (o.Csv != null) { WriteCsv(pre.B[bestB].W, o.Csv); Console.WriteLine("telemetry written: " + o.Csv); }
        return 0;
    }

    static void Header(World w)
    {
        Console.WriteLine("   STEP  ST/S  ALIVE  CLADES  SPLITS  LARGEST CLADES (organisms)");
    }

    static int _reports;

    static void Report(Simulation sim, double sps)
    {
        var w = sim.W;
        var sb = new StringBuilder();
        sb.Append(w.Step.ToString().PadLeft(7)).Append(sps.ToString("F0").PadLeft(6)).Append(w.AliveCount.ToString().PadLeft(7)).Append(' ');
        int alive = 0; for (int s = 0; s < w.NS; s++) if (w.Sp[s].Alive) alive++;
        sb.Append(alive.ToString().PadLeft(7)).Append(w.T.Splits.ToString().PadLeft(8)).Append("  ");
        var top = Enumerable.Range(0, w.NS).Where(s => sim.ActiveCount[s] > 0).OrderByDescending(s => sim.ActiveCount[s]).Take(6);
        sb.Append(string.Join("  ", top.Select(s => $"{w.Sp[s].Name}:{sim.ActiveCount[s]}")));
        Console.WriteLine(sb.ToString());
        if (Environment.GetEnvironmentVariable("NR_PROFILE") == "1")
        {
            Console.WriteLine("         ms: " + string.Join(" ", sim.PhaseMs.Select(v => v.ToString("F0"))));
            Array.Clear(sim.PhaseMs);
        }
        // every 5th report, add a vegetation and learning line
        if (++_reports % 5 == 0 && w.T.Head > 0)
        {
            var a = new float[w.T.Len];
            w.T.Sample(w.T.SampleCount - 1, a);
            int land = 0; for (int i = 0; i < K.FW * K.FW; i++) if (w.Cell[i] == (byte)Realm.Land) land++;
            Console.WriteLine($"         grass {a[Telemetry.G_POOLS + 0] / land:F2} shrub {a[Telemetry.G_SHRUB]:F2} tree {a[Telemetry.G_TREE]:F2} " +
                $"fruit {a[Telemetry.G_POOLS + 2] / land:F3} seed {a[Telemetry.G_POOLS + 3] / land:F3} nectar {a[Telemetry.G_POOLS + 4] / land:F3} " +
                $"carrion {a[Telemetry.G_POOLS + 8]:F0} N {a[Telemetry.G_NUT_LAND]:F2}/{a[Telemetry.G_NUT_WATER]:F2} burning {a[Telemetry.G_BURNING]:F0} season {a[Telemetry.G_SEASON]:F2}");
        }
    }

    /// <summary>End-of-run observation table from the cumulative counters (what happened, not what should).</summary>
    public static string Summary(Simulation sim)
    {
        var w = sim.W; var T = w.T;
        var o = new StringBuilder();
        o.AppendLine();
        o.AppendLine($"OBSERVATION REPORT at step {w.Step:N0} (seed {w.Seed}). Counts are totals for the whole run.");
        o.AppendLine("CLADE         ALIVE  CREATED  IMMIGR  CONCEIV  MATINGS  LAYS  STRIKES  KILLS   DEATHS: starve thirst killed parasit old fire lost disease  TOP FOOD (energy)");
        for (int s = 0; s < w.NS; s++)
        {
            if (!w.Sp[s].Used) continue;
            var sb = new StringBuilder();
            sb.Append(w.Sp[s].Name.PadRight(12)).Append(sim.ActiveCount[s].ToString().PadLeft(7)).Append(T.Created[s].ToString().PadLeft(9))
              .Append(T.Immigrants[s].ToString().PadLeft(8)).Append(T.Conceptions[s].ToString().PadLeft(9)).Append(T.Matings[s].ToString().PadLeft(9))
              .Append(T.Lays[s].ToString().PadLeft(6)).Append(T.Strikes[s].ToString().PadLeft(9)).Append(T.Kills[s].ToString().PadLeft(7)).Append("          ");
            for (int c = 0; c < Cause.N; c++) sb.Append(T.Deaths[s * Cause.N + c].ToString().PadLeft(c == 0 ? 6 : 7));
            int best = -1; double bv = 0, tot = 0;
            for (int p = 0; p < Pools.N; p++) { double v = T.Intake[s * Pools.N + p]; tot += v; if (v > bv) { bv = v; best = p; } }
            sb.Append("   ").Append(best >= 0 ? $"{Pools.Names[best]} {bv:F0} of {tot:F0}" : "none");
            o.AppendLine(sb.ToString());
        }
        o.AppendLine();
        o.AppendLine("LIFE TABLE: where each species' life cycle breaks (deaths by age class, whole run)");
        o.AppendLine("CLADE        UNBORN  JUVENILE  YOUNG ADULT  OLDER ADULT   MEAN AGE AT DEATH    BORN AT STEP");
        for (int s = 0; s < w.NS; s++)
        {
            if (!w.Sp[s].Used) continue;
            long a0 = T.DeathsByAge[s * 4], a1 = T.DeathsByAge[s * 4 + 1], a2 = T.DeathsByAge[s * 4 + 2], a3 = T.DeathsByAge[s * 4 + 3];
            double mean = (a1 + a2 + a3) > 0 ? T.AgeAtDeath[s] / (a1 + a2 + a3) : 0;
            o.AppendLine($"{w.Sp[s].Name,-12}{a0,7}{a1,10}{a2,13}{a3,13}{mean,20:F0}{w.Sp[s].Born,16:N0}");
        }
        o.AppendLine();
        o.AppendLine($"slot shortages (births or matings refused because the world was full): {T.SlotShortage}");
        long conceived = 0; foreach (var c in T.Conceptions) conceived += c;
        if (T.SlotShortage > 0.001 * Math.Max(1, conceived))
            o.AppendLine($"WARNING: the slot cap, not food, limited this world ({T.SlotShortage:N0} refusals against {conceived:N0} conceptions): selection was distorted");
        return o.ToString();
    }

    public static void WriteCsv(World w, string path)
    {
        using var f = new StreamWriter(path);
        f.WriteLine(string.Join(",", w.T.Names));
        var a = new float[w.T.Len];
        var sb = new StringBuilder();
        for (int k = 0; k < w.T.SampleCount; k++)
        {
            w.T.Sample(k, a);
            sb.Clear();
            for (int j = 0; j < a.Length; j++) { if (j > 0) sb.Append(','); sb.Append(a[j].ToString("G6", CultureInfo.InvariantCulture)); }
            f.WriteLine(sb.ToString());
        }
    }
}
