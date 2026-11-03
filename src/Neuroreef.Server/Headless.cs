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
        var clock = Stopwatch.StartNew();
        Console.WriteLine(Options.MemoryLine("the world", o.Capacity, o.MaxCapacity));
        using var sim = new Simulation(o.Capacity, o.Seed, o.Threads, bank, prehistory: false, empty: o.Empty, terrainSeed: o.Seed, evo: o.WorldSettings(), maxCapacity: o.MaxCapacity);
        Console.WriteLine($"world built in {clock.Elapsed.TotalSeconds:F1} s");
        var w = sim.W;
        Console.WriteLine($"NEUROREEF 2  HEADLESS RUN  seed {o.Seed}  capacity {o.Capacity}  threads {o.Threads}  steps {o.Steps}");
        Console.WriteLine($"start: {(bank != null ? $"the world, brains from gene bank ({bank.Total} genomes), no spawning" : "the world, RANDOM BRAINS, no spawning")}   alive {w.AliveCount}");
        Console.Write(CladeLoco(w)); Console.WriteLine(LocoReport(w));
        Header(w);
        var sw = Stopwatch.StartNew();
        long lastT = 0; ulong lastStep = 0;
        bool stop = false;
        // encounter census (2026-11-01, NR_UNITS=1): what each decision senses, and how far animals travel per step
        bool units = Environment.GetEnvironmentVariable("NR_UNITS") == "1";
        if (Environment.GetEnvironmentVariable("NR_DIAG") == "1") Diag.D = new Diag();   // feeding-chain census (2026-11-01)
        if (units) Decide.Metrics = new Decide.DecisionMetrics();
        double moved = 0, movedMax = 0; long animalSteps = 0;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop = true; Console.WriteLine("stopping after this step (saving)..."); };
        var water = new WaterWatch();
        var eco = Environment.GetEnvironmentVariable("NR_ECO") == "1" ? new EcoWatch(sim) : null;   // ecology census (2026-11-01)
        for (long k = 0; k < o.Steps && !stop; k++)
        {
            sim.Step();
            water.Sample(w);
            eco?.Sample(sim);
            if (units)
                for (int i = 0; i < w.Cap; i++)
                    if (w.StageOf[i] >= St.Juvenile) { moved += w.Speed[i]; movedMax += w.Vmax(i); animalSteps++; }
            if (w.Step % (ulong)o.ReportEvery == 0)
            {
                double dt = (sw.ElapsedMilliseconds - lastT) / 1000.0;
                Report(sim, (w.Step - lastStep) / Math.Max(dt, 1e-3));
                lastT = sw.ElapsedMilliseconds; lastStep = w.Step;
            }
            if (o.SaveBank != null && w.Step % 50000 == 0) GeneBank.Save(w, o.SaveBank);
        }
        Console.WriteLine($"done: {w.Step} steps in {sw.Elapsed.TotalSeconds:F1} s ({w.Step / sw.Elapsed.TotalSeconds:F0} steps/s)");
        string summary = Summary(sim) + Environment.NewLine + water.Report(w) + (eco != null ? Environment.NewLine + eco.Report(sim) : "");
        Console.Write(summary);
        if (Diag.D != null) Console.Write(Diag.D.Report());
        if (units)
        {
            var m = Decide.Metrics!; var T = w.T;
            long conc = 0, mat = 0, kills = 0, starve = 0, thirst = 0, strikes = 0, lineages = 0;
            for (int s = 0; s < w.NS; s++)
            {
                conc += T.Conceptions[s]; mat += T.Matings[s]; kills += T.Kills[s]; strikes += T.Strikes[s];
                starve += T.Deaths[s * Cause.N + Cause.Starve]; thirst += T.Deaths[s * Cause.N + Cause.Thirst];
                if (sim.ActiveCount[s] > 0) lineages++;
            }
            double d = Math.Max(1, m.Decisions), mpu = Player.M_PER_UNIT;
            Console.WriteLine($"UNITS: alive {w.AliveCount}  lineages {lineages}  conceptions {conc}  matings {mat}  strikes {strikes}  kills {kills}  starved {starve}  thirst {thirst}");
            Console.WriteLine($"UNITS: decisions {m.Decisions:N0}  other in sight {m.SeeOther / d:P1}  kin in sight {m.SeeKin / d:P1}  ready partner in sight {m.SeeMate / d:P1}  strike target in reach {m.InReach / d:P1}");
            Console.WriteLine($"UNITS: moved per animal-step {moved / Math.Max(1, animalSteps) * mpu:F0} m (top speed {movedMax / Math.Max(1, animalSteps) * mpu:F0} m), x{w.StepsPerDay:F2} steps a day (a step {w.StepH * 60:0.#} min)");
        }
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
        var clock = Stopwatch.StartNew();
        bool resume = o.CheckpointPath != null && File.Exists(o.CheckpointPath);
        Console.WriteLine(Options.MemoryLine(o.Branches == 1 ? "the prehistory branch" : "the prehistory branches", o.BranchSlots, o.MaxCapacity, o.Branches));
        using var pre = new Prehistory(o.Branches, o.BranchSlots, o.Seed, o.Threads, e, restore: resume, maxCapacity: o.MaxCapacity);   // resuming: no land built only to be overwritten
        if (!resume) Console.WriteLine($"{pre.B.Length} branches built in {clock.Elapsed.TotalSeconds:F1} s");
        var s0 = pre.Pending;
        if (o.CheckpointPath != null)
        {
            pre.CheckpointPath = o.CheckpointPath; pre.CheckpointMinutes = o.CheckpointMinutes;
            if (resume)
            {
                try
                {
                    var ms = pre.LoadCheckpoint(o.CheckpointPath);
                    for (int b = 0; b < ms.Length; b++) if (ms[b].Migrated) Console.WriteLine($"branch {b}: {ms[b]}");
                    Console.WriteLine($"RESUMED from {o.CheckpointPath} at step {pre.Step:N0} ({clock.Elapsed.TotalSeconds:F1} s)");
                }
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
    public static string VoiceReport(World w, int branch, string? where = null)
    {
        var T = w.T; var sb = new StringBuilder();
        sb.AppendLine($"VOICES in {where ?? "branch " + branch}: clade, living (voiced), mean voice, vocal learning, utterances, kinds, kinds made >= {Telemetry.MIN_UTT}x; most made (count: what stands out when it is made)");
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
        {
            // the plants (Revision S): genotypes in plants and in the table, mean cover, establishments so far
            double g = 0, sh = 0, tr = 0; int land = 0;
            for (int i = 0; i < Map.FW * Map.FW; i++) if (w.Cell[i] == (byte)Realm.Land) { land++; g += w.Pool[(int)Pool.Grass][i]; sh += w.Shrub[i]; tr += w.Tree[i]; }
            land = Math.Max(1, land);
            Console.WriteLine($"         plants: {Plants.SlotGenotypes(w)} genotypes in plants, {w.Flora.Live} in the table; cover herb {g / land:F2} shrub {sh / land:F2} tree {tr / land:F2}; establishments {w.Flora.Establish:N0}, mutations {w.Flora.Mutations:N0}");
        }
        Console.WriteLine(LocoReport(w));
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
            land = Math.Max(1, land);   // an ocean world has none (2026-11-02: it printed NaN)
            Console.WriteLine($"         grass {a[Telemetry.G_POOLS + 0] / land:F2} shrub {a[Telemetry.G_SHRUB]:F2} tree {a[Telemetry.G_TREE]:F2} " +
                $"fruit {a[Telemetry.G_POOLS + 2] / land:F3} seed {a[Telemetry.G_POOLS + 3] / land:F3} nectar {a[Telemetry.G_POOLS + 4] / land:F3} " +
                $"carrion {a[Telemetry.G_POOLS + 8]:F0} N {a[Telemetry.G_NUT_LAND]:F2}/{a[Telemetry.G_NUT_WATER]:F2} burning {a[Telemetry.G_BURNING]:F0} season {a[Telemetry.G_SEASON]:F2}");
        }
    }

    /// <summary>Per clade (living): how many, the share that flies, mean mass, wings, legs and leg pairs (reporting only).</summary>
    public static string CladeLoco(World w)
    {
        var T = w.T; var o = new StringBuilder();
        o.AppendLine("LOCOMOTION per clade (living): alive, share flying, mean mass, mean wings, mean legs, mean leg pairs (of those with legs), created");
        for (int s = 0; s < w.NS; s++)
        {
            if (!w.Sp[s].Used) continue;
            int n = 0, fly = 0, legged = 0; double m = 0, wg = 0, lg = 0, lp = 0;
            for (int i = 0; i < w.Cap; i++)
            {
                if (w.StageOf[i] < St.Juvenile || w.Spc[i] != s) continue;
                n++; m += w.PM[i]; wg += w.PWings[i]; lg += w.PLegs[i];
                if (w.PLoco[i] == (byte)Loco.Fly) fly++;
                if (w.PLegs[i] >= K.LIMB_MIN) { legged++; lp += w.PLegPairs[i]; }
            }
            o.AppendLine(n > 0
                ? $"{w.Sp[s].Name,-12} {n,6}  fly {100.0 * fly / n,5:F1}%  mass {m / n:F2}  wings {wg / n:F2}  legs {lg / n:F2}  pairs {(legged > 0 ? lp / legged : 0):F2}  created {T.Created[s]}"
                : $"{w.Sp[s].Name,-12} {0,6}  (extinct)  created {T.Created[s]}");
        }
        return o.ToString();
    }

    /// <summary>Locomotion census (reporting only): animals and lineages by how most of their members move, fliers'
    /// body masses, and the distribution of leg pairs among animals with legs (legs >= LIMB_MIN).</summary>
    public static string LocoReport(World w)
    {
        var byLoco = new int[5]; var legPairs = new int[5]; var cladeLoco = new int[K.CLADES * 5];
        var flierMass = new System.Collections.Generic.List<float>();
        int n = 0, air = 0;
        for (int i = 0; i < w.Cap; i++)
        {
            if (w.StageOf[i] < St.Juvenile) continue;
            n++; int l = w.PLoco[i]; byLoco[l]++; cladeLoco[w.Spc[i] * 5 + l]++;
            if (l == (int)Loco.Fly) { flierMass.Add(w.PM[i]); if (w.Airborne(i)) air++; }
            if (w.PLegs[i] >= K.LIMB_MIN) legPairs[w.PLegPairs[i]]++;
        }
        int lineages = 0, flyLineages = 0;
        for (int s = 0; s < K.CLADES; s++)
        {
            int tot = 0, best = 0;
            for (int l = 0; l < 5; l++) { tot += cladeLoco[s * 5 + l]; if (cladeLoco[s * 5 + l] > cladeLoco[s * 5 + best]) best = l; }
            if (tot == 0) continue;
            lineages++; if (best == (int)Loco.Fly) flyLineages++;
        }
        flierMass.Sort();
        string fm = flierMass.Count > 0
            ? $"mass median {flierMass[flierMass.Count / 2]:F2} p90 {flierMass[flierMass.Count * 9 / 10]:F2} max {flierMass[^1]:F2}"
            : "none";
        return $"         loco: walk {byLoco[0]} wade {byLoco[1]} swim {byLoco[2]} fly {byLoco[3]} sessile {byLoco[4]}  " +
               $"fliers {(n > 0 ? 100.0 * byLoco[3] / n : 0):F1}% of animals, {flyLineages} of {lineages} lineages, {(byLoco[3] > 0 ? 100.0 * air / byLoco[3] : 0):F0}% of them airborne; fliers {fm}; " +
               $"leg pairs 1:{legPairs[1]} 2:{legPairs[2]} 3:{legPairs[3]} 4:{legPairs[4]}";
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
        o.AppendLine("CLADE         ALIVE  CREATED  IMMIGR  CONCEIV  MATINGS    LAYS  STRIKES  KILLS   DEATHS: starve thirst killed parasit old fire lost disease cold heat hunted suffoc drowned  TOP FOOD (energy)");
        for (int s = 0; s < w.NS; s++)
        {
            if (!w.Sp[s].Used) continue;
            var sb = new StringBuilder();
            sb.Append(w.Sp[s].Name.PadRight(12)).Append(sim.ActiveCount[s].ToString().PadLeft(7)).Append(T.Created[s].ToString().PadLeft(9))
              .Append(T.Immigrants[s].ToString().PadLeft(8)).Append(T.Conceptions[s].ToString().PadLeft(9)).Append(T.Matings[s].ToString().PadLeft(9))
              .Append(T.Lays[s].ToString().PadLeft(8)).Append(T.Strikes[s].ToString().PadLeft(9)).Append(T.Kills[s].ToString().PadLeft(7)).Append("          ");
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
        o.AppendLine($"slots: {w.Cap:N0} now, grown {w.Grows} times as births needed them ({w.SlotBytes / 1024.0:N1} KB of memory per slot, {(long)w.Cap * w.SlotBytes / (1 << 20):N0} MB in all)" +
            (w.GrowLimit != null ? $"; it could not grow past {w.GrowLimit}" : "; never limited"));
        o.AppendLine($"slot shortages (births or matings refused because the world could not grow): {T.SlotShortage}");
        o.AppendLine($"steps on which the world was full: {T.FullSteps:N0} of {sim.W.Step:N0}");
        if (T.FullSteps > 0.01 * Math.Max(1UL, sim.W.Step))
            o.AppendLine($"INVALID: the world could not grow past {w.Cap:N0} slots ({w.GrowLimit}) on more than 1% of steps, so that limit, not food, chose who was born. Give it more memory, a higher --max-capacity or a smaller map.");
        o.AppendLine();
        o.Append(Pathogens.Report(w));   // the pathogens' census, lineages and deaths by disease (Revision R)
        o.AppendLine();
        o.Append(Plants.Report(w));   // the plants' genotypes, dispersal, traits and lineages (Revision S)
        o.AppendLine();
        o.Append(VoiceReport(w, 0, "the world"));   // whether any utterance has come to go with something (2026-10-30)
        o.AppendLine();
        o.Append(CladeLoco(w));
        o.AppendLine(LocoReport(w).Trim());
        o.AppendLine(); o.Append(Neuroreef.Morph.Live.BodyPlanReport(w));   // the module bodies (docs/BODYPLAN.md §13; the shadow comparison of §11 is retired)
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

/// <summary>
/// Water census of a headless run (reporting only; reads the world, changes nothing). Every step, per clade: animal-steps
/// on land, in shallow and in deep water, and how often an animal stepped from water onto land; lineages are marked by
/// where their ancestor was founded (land or water). Over all animals on land: how high they stand (m above the sea),
/// small (M below 10) and big bodies apart.
/// </summary>
public sealed class WaterWatch
{
    public static readonly float[] AltEdges = { 500f, 1500f, 2500f, 3500f, 4500f };
    readonly long[] _land = new long[K.CLADES], _shallow = new long[K.CLADES], _deep = new long[K.CLADES], _exits = new long[K.CLADES];
    readonly long[] _altSmall = new long[6], _altBig = new long[6];
    public static readonly float[] ZEdges = { 0f, 2f, 10f, 30f, 100f };
    readonly double[] _zSum = new double[K.CLADES], _diveM = new double[2];
    readonly long[] _zN = new long[K.CLADES], _zGill = new long[6], _zLung = new long[6], _diveN = new long[2];
    readonly long[] _orgLand = new long[3], _orgWater = new long[3];
    // breathing classes from the physics: gills that cannot keep a day of living going in air (lungs below FIELD_METAB /
    // LUNG_SCOPE), lungs that cannot under water (gills below FIELD_METAB / GILL_SCOPE), amphibious between
    static readonly float LungsMin = K.FIELD_METAB / K.LUNG_SCOPE, LungsMax = 1f - K.FIELD_METAB / K.GILL_SCOPE;
    static int Organ(float lungs) => lungs < LungsMin ? 0 : lungs > LungsMax ? 2 : 1;
    uint[] _serial = Array.Empty<uint>();
    byte[] _wet = Array.Empty<byte>();
    long _samples;

    readonly StringBuilder _traj = new();
    long _tLand, _tWater;   // water-founded animal-steps on land and in water since the last trajectory line

    public void Sample(World w)
    {
        if (_serial.Length < w.Cap) { Array.Resize(ref _serial, w.Cap); Array.Resize(ref _wet, w.Cap); }
        _samples++;
        TrackLungs(w);
        for (int i = 0; i < w.Cap; i++)
        {
            if (w.StageOf[i] < St.Juvenile) continue;
            int s = w.Spc[i], cell = World.FIdx(w.X[i], w.Y[i]);
            byte kind = w.Cell[cell];
            bool air = w.Airborne(i);
            bool wet = (kind & (byte)Realm.Water) != 0 && !air;
            if ((kind & (byte)Realm.Land) != 0)
            {
                _land[s]++;
                if (!air)
                {
                    float m = (w.Elev[cell] - w.SeaLevel) / w.ElevPerM;
                    int b = 0; while (b < AltEdges.Length && m >= AltEdges[b]) b++;
                    (w.PM[i] >= 10f ? _altBig : _altSmall)[b]++;
                }
            }
            else if ((kind & (byte)Realm.Deep) != 0) _deep[s]++;
            else if ((kind & (byte)Realm.Shallow) != 0) _shallow[s]++;
            int org = Organ(w.PLungs[i]);
            if ((kind & (byte)Realm.Land) != 0) _orgLand[org]++; else if (wet) _orgWater[org]++;
            if (wet)
            {
                _zSum[s] += w.Z[i]; _zN[s]++;
                int b = 0; while (b < ZEdges.Length && w.Z[i] > ZEdges[b]) b++;
                (org == 2 ? _zLung : _zGill)[b]++;
                if (org == 2 && w.Z[i] > 0f) { _diveM[w.PM[i] >= 10f ? 1 : 0] += w.Z[i]; _diveN[w.PM[i] >= 10f ? 1 : 0]++; }
            }
            if (_serial[i] == w.Serial[i] && _wet[i] == 1 && (kind & (byte)Realm.Land) != 0) _exits[s]++;
            _serial[i] = w.Serial[i]; _wet[i] = (byte)(wet ? 1 : 0);
        }
    }

    /// <summary>Every TRAJ steps: the lung gene of water- and land-founded lineages (mean over living animals, and the
    /// share that are gill-breathers, lungs below LungsMin), the water-founded lineages alive, and their share of
    /// animal-steps on land since the last line.</summary>
    const int TRAJ = 500;
    void TrackLungs(World w)
    {
        for (int i = 0; i < w.Cap; i++)
        {
            if (w.StageOf[i] < St.Juvenile || w.Sp[w.Spc[i]].Ancestor < w.Evo.AncestorsLand) continue;
            byte kind = w.Cell[World.FIdx(w.X[i], w.Y[i])];
            if (kind == (byte)Realm.Land) _tLand++; else _tWater++;
        }
        if (w.Step % TRAJ != 0) return;
        if (_traj.Length == 0) _traj.AppendLine("     STEP   WATER-FOUNDED: ALIVE LINEAGES  LUNGS (mean, gills <0.15)   ON LAND since last   LAND-FOUNDED: LUNGS   ALL: ENDOTHERMY (mean, share > 0.5, lineages mostly warm)");
        int nw = 0, nl = 0, gw = 0, warm = 0; double lw = 0, ll = 0, en = 0;
        var lineages = new bool[K.CLADES];
        var cn = new int[K.CLADES]; var cw = new int[K.CLADES];
        for (int i = 0; i < w.Cap; i++)
        {
            if (w.StageOf[i] < St.Juvenile) continue;
            bool water = w.Sp[w.Spc[i]].Ancestor >= w.Evo.AncestorsLand;
            if (water) { nw++; lw += w.PLungs[i]; if (w.PLungs[i] < LungsMin) gw++; lineages[w.Spc[i]] = true; }
            else { nl++; ll += w.PLungs[i]; }
            en += w.PEndo[i]; cn[w.Spc[i]]++;
            if (w.PEndo[i] > 0.5f) { warm++; cw[w.Spc[i]]++; }
        }
        int warmLin = 0, lin = 0;
        for (int s = 0; s < K.CLADES; s++) if (cn[s] > 0) { lin++; if (2 * cw[s] > cn[s]) warmLin++; }
        _traj.AppendLine($"{w.Step,9}   {nw,6} in {lineages.Count(b => b),2}             {(nw > 0 ? lw / nw : 0),5:F3}  {Pct(gw, nw),5:F1}%" +
                         $"         {Pct(_tLand, _tLand + _tWater),6:F1}%         {(nl > 0 ? ll / nl : 0),5:F3}" +
                         $"         {(nw + nl > 0 ? en / (nw + nl) : 0),5:F3}  {Pct(warm, nw + nl),5:F1}%  {warmLin,2} of {lin,2}");
        _tLand = _tWater = 0;
    }

    public string Report(World w)
    {
        var o = new StringBuilder();
        if (_traj.Length > 0) { o.AppendLine("LUNG GENE in water- and land-founded lineages (living animals):"); o.Append(_traj); }
        o.AppendLine($"WATER CENSUS over {_samples:N0} steps: per clade, where its animal-steps were (land / shallow / deep water), steps from water onto land, and its living now");
        o.AppendLine("CLADE        FOUNDED   ALIVE   LAND%  SHALLOW%  DEEP%   ONTO LAND  (per 1000 water steps)   LUNGS  DEPTH IN WATER (m)  DIED: SUFFOC DROWNED");
        long wl = 0, ww = 0, we = 0;
        for (int s = 0; s < K.CLADES; s++)
        {
            if (!w.Sp[s].Used) continue;
            long tot = _land[s] + _shallow[s] + _deep[s];
            bool waterBorn = w.Sp[s].Ancestor >= w.Evo.AncestorsLand;
            int alive = 0; double lungs = 0;
            for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] >= St.Juvenile && w.Spc[i] == s) { alive++; lungs += w.PLungs[i]; }
            double per = _shallow[s] + _deep[s] > 0 ? 1000.0 * _exits[s] / (_shallow[s] + _deep[s]) : 0;
            string lg = alive > 0 ? (lungs / alive).ToString("F2") : "  - ";
            o.AppendLine($"{w.Sp[s].Name,-12} {(waterBorn ? "water" : "land"),-8}{alive,6}  {Pct(_land[s], tot),6:F1}  {Pct(_shallow[s], tot),8:F1}  {Pct(_deep[s], tot),5:F1}  {_exits[s],10}  {per,8:F1}" +
                $"                 {lg,5}  {(_zN[s] > 0 ? _zSum[s] / _zN[s] : 0),18:F1}  {w.T.Deaths[s * Cause.N + Cause.Suffocated],12} {w.T.Deaths[s * Cause.N + Cause.Drowned],7}");
            if (waterBorn) { wl += _land[s]; ww += _shallow[s] + _deep[s]; we += _exits[s]; }
        }
        o.AppendLine($"water-founded lineages: {Pct(wl, wl + ww):F1}% of their animal-steps on land; {we:N0} steps from water onto land ({(ww > 0 ? 1000.0 * we / ww : 0):F1} per 1000 animal-steps in water)");
        string Org(int k) => $"{Pct(_orgLand[k], _orgLand[k] + _orgWater[k]):F1}% of {_orgLand[k] + _orgWater[k]:N0}";
        o.AppendLine($"by breathing organ (all animals), share of animal-steps on land: gills (lungs < {LungsMin:F2}) {Org(0)}; amphibious {Org(1)}; lungs (> {LungsMax:F2}) {Org(2)}");
        {
            // the gill-breathers ashore now, and why they live: their size, warm-bloodedness, legs, oxygen debt and thirst
            int n = 0; double m = 0, endo = 0, legs = 0, debt = 0, thirst = 0, rate = 0, nearW = 0;
            for (int i = 0; i < w.Cap; i++)
            {
                if (w.StageOf[i] < St.Juvenile || Organ(w.PLungs[i]) != 0) continue;
                int c = World.FIdx(w.X[i], w.Y[i]);
                if (w.Cell[c] != (byte)Realm.Land || w.Airborne(i)) continue;
                n++; m += w.PM[i]; endo += w.PEndo[i]; legs += w.PLegs[i]; debt += Respiration.Breathless(w, i); thirst += w.Thirst[i];
                rate += Climate.On ? Heat.Rate(Heat.Core(w.TOp[i], w.PEndo[i])) : 1f;
                if (w.DistWater[c] <= 1.5f) nearW++;
            }
            if (n > 0) o.AppendLine($"gill-breathers ashore now: {n}; mean mass {m / n:F2}, warm-bloodedness {endo / n:F2}, metabolic rate x{rate / n:F2} of warm, legs {legs / n:F2}, breathless {debt / n:F2}, thirst {thirst / n:F2}; {100 * nearW / n:F0}% within 1.5 cells of water");
        }
        string Hist(long[] h) { long t = h.Sum(); return string.Join("  ", Enumerable.Range(0, 6).Select(b => $"{Label(b)} {Pct(h[b], t):F1}%")) + $"  (n {t:N0})"; }
        o.AppendLine("ALTITUDE of animal-steps on land (not airborne), m above the sea:");
        o.AppendLine("  small (M < 10): " + Hist(_altSmall));
        o.AppendLine("  big   (M >= 10): " + Hist(_altBig));
        string ZHist(long[] h) { long t = h.Sum(); return string.Join("  ", Enumerable.Range(0, 6).Select(b => $"{ZLabel(b)} {Pct(h[b], t):F1}%")) + $"  (n {t:N0})"; }
        {
            float[] ce = { 2f, 10f, 30f, 100f, 400f }; var ch = new long[6]; long rivers = 0;
            for (int c = 0; c < Map.FW * Map.FW; c++)
            {
                if ((w.Cell[c] & (byte)Realm.Water) == 0) continue;
                float d = Respiration.ColumnM(w, c); int b = 0; while (b < ce.Length && d >= ce[b]) b++; ch[b]++;
                if (w.FloodFrom[c] >= 0) rivers++;
            }
            long tc = ch.Sum();
            o.AppendLine($"WATER CELLS by depth of the column (m): <2 {Pct(ch[0], tc):F1}%  2-10 {Pct(ch[1], tc):F1}%  10-30 {Pct(ch[2], tc):F1}%  30-100 {Pct(ch[3], tc):F1}%  100-400 {Pct(ch[4], tc):F1}%  >=400 {Pct(ch[5], tc):F1}%  (n {tc:N0}, rivers {rivers:N0})");
        }
        o.AppendLine("DEPTH of animal-steps in water, m below the surface:");
        o.AppendLine("  gills or amphibious: " + ZHist(_zGill));
        o.AppendLine("  lungs:               " + ZHist(_zLung));
        o.AppendLine($"  air-breathers' dives: mean depth {(_diveN[0] > 0 ? _diveM[0] / _diveN[0] : 0):F1} m small (M < 10, n {_diveN[0]:N0}), {(_diveN[1] > 0 ? _diveM[1] / _diveN[1] : 0):F1} m big (n {_diveN[1]:N0})");
        long suf = 0, dro = 0, all = 0;
        for (int s = 0; s < K.CLADES; s++) { suf += w.T.Deaths[s * Cause.N + Cause.Suffocated]; dro += w.T.Deaths[s * Cause.N + Cause.Drowned]; for (int c = 0; c < Cause.N; c++) all += w.T.Deaths[s * Cause.N + c]; }
        o.AppendLine($"DEATHS: {suf:N0} suffocated, {dro:N0} drowned, of {all:N0} ({Pct(suf + dro, all):F1}%)");
        return o.ToString();
    }

    static string ZLabel(int b) => b == 0 ? "surface" : b == ZEdges.Length ? $">{ZEdges[^1]:0}" : $"{ZEdges[b - 1]:0}-{ZEdges[b]:0}";

    static string Label(int b) => b == 0 ? $"<{AltEdges[0]:0}" : b == AltEdges.Length ? $">={AltEdges[^1]:0}" : $"{AltEdges[b - 1]:0}-{AltEdges[b]:0}";
    static double Pct(long a, long t) => t > 0 ? 100.0 * a / t : 0;
}
