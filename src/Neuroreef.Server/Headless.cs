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
        using var sim = new Simulation(o.Capacity, o.Seed, o.Threads, bank, prehistory: false, empty: o.Empty, terrainSeed: o.Seed, evo: o.WorldSettings());
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
        // resuming: the prehistory is rebuilt with the sizes it was started with
        if (o.CheckpointPath != null && File.Exists(o.CheckpointPath))
            try { var (nb, cap, sd) = Checkpoint.PrehistoryHeader(o.CheckpointPath); o.Branches = nb; o.BranchSlots = cap; o.Seed = sd; }
            catch (Exception ex) { Console.Error.WriteLine($"cannot resume from {o.CheckpointPath}: {ex.Message}"); return 2; }
        using var pre = new Prehistory(o.Branches, o.BranchSlots, o.Seed, o.Threads, e);
        var s0 = pre.Pending;
        if (o.CheckpointPath != null)
        {
            pre.CheckpointPath = o.CheckpointPath; pre.CheckpointMinutes = o.CheckpointMinutes;
            if (File.Exists(o.CheckpointPath))
            {
                try { pre.LoadCheckpoint(o.CheckpointPath); Console.WriteLine($"RESUMED from {o.CheckpointPath} at step {pre.Step:N0}"); }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"cannot resume from {o.CheckpointPath}: {ex.Message}");
                    Console.Error.WriteLine("Move or delete that file to start a new prehistory, or start with the options it was made with.");
                    return 2;
                }
            }
        }
        Console.WriteLine("NEUROREEF 2  PREHISTORY");
        if (s0.TotalSteps == int.MaxValue) Console.WriteLine($"{pre.B.Length} separate worlds evolve until you stop them (Ctrl+C saves and stops).");
        else Console.WriteLine($"{pre.B.Length} separate worlds evolve over {s0.TotalSteps:N0} steps ({(long)s0.TotalSteps * K.YEARS_PER_STEP / 1_000_000:N0} million symbolic years).");
        if (pre.CheckpointPath != null) Console.WriteLine($"Whole state saved every {pre.CheckpointMinutes:0} min to {pre.CheckpointPath}; run the same command again to resume.");
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
        pre.SaveCheckpoint();
        if (pre.Finished && pre.CheckpointPath != null)   // reached its length: the next start is a new prehistory
            try { File.Move(pre.CheckpointPath, pre.CheckpointPath + ".done", overwrite: true); } catch (Exception ex) { Console.Error.WriteLine(ex.Message); }
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
                // the long history first (runs of days), then the recent ring
                for (int k = 0; k < T.LongCount + T.SampleCount; k++)
                {
                    if (k < T.LongCount) T.LongSample(k, row); else T.Sample(k - T.LongCount, row);
                    if (row[T.SpeciesOffset(s, Telemetry.S_COUNT)] <= 0) continue;
                    for (int q = 0; q < tr.Length; q++) f1[q] = row[T.SpeciesOffset(s, Telemetry.S_TRAITS + tr[q])];
                    f1[tr.Length] = row[T.SpeciesOffset(s, Telemetry.S_KIT)];
                    if (!seen) { Array.Copy(f1, f0, f0.Length); seen = true; }
                }
                if (!seen) continue;
                report.AppendLine($"{pre.B[bestB].W.Sp[s].Name,-12} " + string.Join("  ", Enumerable.Range(0, f0.Length).Select(q => $"{f0[q]:0.##}->{f1[q]:0.##}")));
            }
        }
        for (int b = 0; b < pre.B.Length; b++) { report.AppendLine(); report.Append(VoiceReport(pre.B[b].W, b)); }
        for (int b = 0; b < pre.B.Length; b++) { report.AppendLine(); report.Append(ClimateReport(pre.B[b].W, b)); }
        for (int b = 0; b < pre.B.Length; b++) { report.AppendLine(); report.AppendLine($"=== BRANCH {b} ==="); report.Append(Summary(pre.B[b])); }
        Console.Write(report.ToString());
        if (o.ReportTxt != null) { File.WriteAllText(o.ReportTxt, report.ToString()); Console.WriteLine("report written: " + o.ReportTxt); }
        if (o.SaveBank != null) { pre.SaveBank(o.SaveBank); Console.WriteLine("evolved brains saved: " + o.SaveBank); }
        if (o.Csv != null) { WriteCsv(pre.B[bestB].W, o.Csv); Console.WriteLine("telemetry written: " + o.Csv); }
        return 0;
    }

    /// <summary>Voices of a world (Revision N; reporting only): per clade that has spoken, its living members' mean voice
    /// and vocal-learning genes, how many utterances and of how many kinds, how many kinds were made at least
    /// Telemetry.MIN_UTT times, and its most made utterances with the sense that stands out most when each is made.</summary>
    public static string VoiceReport(World w, int branch)
    {
        var T = w.T; var sb = new StringBuilder();
        sb.AppendLine($"VOICES in branch {branch}: clade, living (voiced), mean voice, vocal learning, utterances, kinds, kinds made >= {Telemetry.MIN_UTT}x; most made (count: what stands out when it is made)");
        var syl = new byte[F.MAX_SYL];
        for (int s = 0; s < K.CLADES; s++)
        {
            long utts = 0; int kinds = 0, common = 0;
            for (int t = 0; t < Voice.NTYPE; t++) { long n = T.UttN[s * Voice.NTYPE + t]; utts += n; if (n > 0) kinds++; if (n >= Telemetry.MIN_UTT) common++; }
            if (utts == 0 || !w.Sp[s].Used) continue;
            int alive = 0, voiced = 0; double voice = 0, imit = 0;
            for (int i = 0; i < w.Cap; i++)
                if (w.StageOf[i] >= St.Juvenile && w.Spc[i] == s) { alive++; voice += w.PVoice[i]; imit += w.PImit[i]; if (w.PVoice[i] >= K.VOICE_MIN) voiced++; }
            sb.Append($"{w.Sp[s].Name,-12} {alive,6} ({voiced})  voice {(alive > 0 ? voice / alive : 0):0.00}  learning {(alive > 0 ? imit / alive : 0):0.00}  {utts,9:N0} utterances  {kinds,3} kinds  {common,3} made >= {Telemetry.MIN_UTT}x");
            var top = Enumerable.Range(0, Voice.NTYPE).Where(t => T.UttN[s * Voice.NTYPE + t] > 0).OrderByDescending(t => T.UttN[s * Voice.NTYPE + t]).Take(6);
            foreach (int t in top)
            {
                int n = Voice.Decode(t, syl);
                var when = Meaning.RankUttSenses(T, s, t, 1);
                string read = when.Count > 0 ? $": {Meaning.Phrase(when[0].Index, when[0].High)} ({when[0].D:0.0})" : "";
                sb.Append($"\n    [{string.Join(" ", syl.Take(n))}] {T.UttN[s * Voice.NTYPE + t]:N0}{read}");
            }
            sb.AppendLine();
        }
        return sb.ToString();
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
            int land = 0; for (int i = 0; i < Map.FW * Map.FW; i++) if (w.Cell[i] == (byte)Realm.Land) land++;
            Console.WriteLine($"         grass {a[Telemetry.G_POOLS + 0] / land:F2} shrub {a[Telemetry.G_SHRUB]:F2} tree {a[Telemetry.G_TREE]:F2} " +
                $"fruit {a[Telemetry.G_POOLS + 2] / land:F3} seed {a[Telemetry.G_POOLS + 3] / land:F3} nectar {a[Telemetry.G_POOLS + 4] / land:F3} " +
                $"carrion {a[Telemetry.G_POOLS + 8]:F0} N {a[Telemetry.G_NUT_LAND]:F2}/{a[Telemetry.G_NUT_WATER]:F2} burning {a[Telemetry.G_BURNING]:F0} season {a[Telemetry.G_SEASON]:F2}");
        }
    }

    /// <summary>Climate and body heat of a world (Revision O; reporting only): the land's temperature now and over the
    /// samples kept, plant cover by annual mean temperature, per clade its bodies against the operative temperature its
    /// animals are in, deaths by cold and heat and the energy spent on keeping warm, and all living animals' bodies in the
    /// coldest, middle and warmest third of operative temperatures.</summary>
    public static string ClimateReport(World w, int branch)
    {
        var sb = new StringBuilder(); var T = w.T;
        var (tn, tx, tm) = Climate.LandRange(w);
        float rmin = float.MaxValue, rmax = float.MinValue; var row = new float[T.Len];
        for (int k = 0; k < T.SampleCount; k++) { T.Sample(k, row); rmin = MathF.Min(rmin, row[Telemetry.G_TMIN]); rmax = MathF.Max(rmax, row[Telemetry.G_TMAX]); }
        sb.AppendLine($"CLIMATE in branch {branch} (climate x{Nature.Climate:0.##}): land air temperature now {tn:F1} .. {tx:F1} °C (mean {tm:F1}); over the samples kept {rmin:F1} .. {rmax:F1} °C");
        if (!Climate.On) return sb.ToString();
        // plant cover by annual mean temperature
        float[] edges = { -100f, 0f, 5f, 10f, 15f, 20f, 25f, 100f };
        int nb = edges.Length - 1; var cnt = new int[nb]; var tr = new double[nb]; var sh = new double[nb]; var gr = new double[nb]; var mo = new double[nb];
        for (int i = 0; i < Map.FW * Map.FW; i++)
        {
            if (w.Cell[i] != (byte)Realm.Land) continue;
            float t = Climate.AnnualMean(w, i); int k = 0; while (k < nb - 1 && t >= edges[k + 1]) k++;
            cnt[k]++; tr[k] += w.Tree[i]; sh[k] += w.Shrub[i]; gr[k] += w.Pool[(int)Pool.Grass][i]; mo[k] += w.Moisture[i];
        }
        sb.AppendLine("  plant cover by annual mean temperature: band, land cells, tree, shrub, grass, soil moisture");
        for (int k = 0; k < nb; k++)
        {
            if (cnt[k] == 0) continue;
            string band = k == 0 ? $"< {edges[1]:F0} °C" : k == nb - 1 ? $">= {edges[k]:F0} °C" : $"{edges[k]:F0}..{edges[k + 1]:F0} °C";
            sb.AppendLine($"    {band,-12} {cnt[k],7}  tree {tr[k] / cnt[k]:F3}  shrub {sh[k] / cnt[k]:F3}  grass {gr[k] / cnt[k]:F3}  moisture {mo[k] / cnt[k]:F2}");
        }
        sb.AppendLine("  per clade (living): alive, mean mass, fur, appendages, mean operative temperature, deaths by cold / heat (whole run), energy on heat / on basal");
        for (int s = 0; s < w.NS; s++)
        {
            if (!w.Sp[s].Used) continue;
            int n = 0; double m = 0, f = 0, a = 0, to = 0;
            for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] >= St.Juvenile && w.Spc[i] == s) { n++; m += w.PM[i]; f += w.PInsul[i]; a += w.PAppend[i]; to += w.TOp[i]; }
            long dc = T.Deaths[s * Cause.N + Cause.Cold], dh = T.Deaths[s * Cause.N + Cause.Heat];
            if (n == 0 && dc + dh == 0) continue;
            double share = T.SpendBasal[s] > 0 ? T.SpendHeat[s] / T.SpendBasal[s] : 0;
            sb.AppendLine(n > 0
                ? $"    {w.Sp[s].Name,-12} {n,6}  mass {m / n:F2}  fur {f / n:F2}  appendages {a / n:F2}  T_op {to / n:F1} °C  cold {dc,6} heat {dh,6}  heat/basal {share:P1}"
                : $"    {w.Sp[s].Name,-12} {0,6}  (extinct)  cold {dc,6} heat {dh,6}  heat/basal {share:P1}");
        }
        // bodies against the operative temperature they are in, over all living animals
        int na = 0; for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] >= St.Juvenile) na++;
        if (na >= 30)
        {
            var tops = new float[na]; int q = 0;
            for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] >= St.Juvenile) tops[q++] = w.TOp[i];
            Array.Sort(tops);
            float c1 = tops[na / 3], c2 = tops[2 * na / 3];
            var bn = new int[3]; var bm = new double[3]; var bf = new double[3]; var ba = new double[3]; var bt = new double[3];
            for (int i = 0; i < w.Cap; i++)
            {
                if (w.StageOf[i] < St.Juvenile) continue;
                int k = w.TOp[i] < c1 ? 0 : w.TOp[i] < c2 ? 1 : 2;
                bn[k]++; bm[k] += w.PM[i]; bf[k] += w.PInsul[i]; ba[k] += w.PAppend[i]; bt[k] += w.TOp[i];
            }
            string[] nm = { "coldest third", "middle third", "warmest third" };
            sb.AppendLine("  all living animals by the operative temperature they are in: mean T_op, mass, fur, appendages");
            for (int k = 0; k < 3; k++) if (bn[k] > 0) sb.AppendLine($"    {nm[k],-14} {bn[k],6}  T_op {bt[k] / bn[k]:F1} °C  mass {bm[k] / bn[k]:F2}  fur {bf[k] / bn[k]:F3}  appendages {ba[k] / bn[k]:F3}");
        }
        return sb.ToString();
    }

    /// <summary>End-of-run observation table from the cumulative counters (what happened, not what should).</summary>
    public static string Summary(Simulation sim)
    {
        var w = sim.W; var T = w.T;
        var o = new StringBuilder();
        o.AppendLine();
        o.AppendLine($"OBSERVATION REPORT at step {w.Step:N0} (seed {w.Seed}). Counts are totals for the whole run.");
        o.AppendLine("CLADE         ALIVE  CREATED  IMMIGR  CONCEIV  MATINGS  LAYS  STRIKES  KILLS   DEATHS: starve thirst killed parasit old fire lost disease cold heat  TOP FOOD (energy)");
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
        o.AppendLine($"steps on which the world was full: {T.FullSteps:N0} of {sim.W.Step:N0}");
        if (T.FullSteps > 0.01 * Math.Max(1UL, sim.W.Step))
            o.AppendLine("INVALID: the slot cap, not food, limited this world on more than 1% of steps, so the cap chose who was born. Give it more slots or a smaller map.");
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
