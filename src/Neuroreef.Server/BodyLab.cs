// The Body Lab (Neuroreef.exe --bodylab): runs the body-plan prototype (Neuroreef.Morph, docs/BODYPLAN.md) from the
// command line, prints its progress and the evolution demo's table, writes each run's lineage to
// bodylab-<env>-<seed>.json, and serves wwwroot/bodylab.html, which draws the evolved bodies in 3D and can start more
// runs live. It only observes the prototype's Evolver (its progress callback) and never touches the live simulation.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Neuroreef.Morph;

namespace Neuroreef.Server;

public static class BodyLab
{
    /// <summary>A run in progress or finished in this session.</summary>
    public sealed class LiveRun
    {
        public string Key = "";
        public LabFile File = new();
        public readonly object Lock = new();
        public int Gen;
        public volatile bool Done;
        public string? Path, Error;
    }

    static readonly List<LiveRun> Runs = new();
    static readonly CancellationTokenSource Stop = new();

    public static string FileName(string envKey, uint seed) => $"bodylab-{envKey}-{seed}.json";

    public static int Run(Options o)
    {
        var envs = o.LabEnv == "all" ? LabReport.EnvKeys : new[] { o.LabEnv };
        var seeds = o.LabSeedList();
        string outDir = Path.GetFullPath(o.LabOut ?? Path.Combine(AppContext.BaseDirectory, "bodylab"));
        Directory.CreateDirectory(outDir);
        Console.WriteLine("NEUROREEF BODY LAB - the body-plan prototype (docs/BODYPLAN.md)");
        Console.WriteLine($"environments {string.Join(", ", envs)}; seeds {string.Join(",", seeds)}; {o.LabGenerations} generations of {o.LabPopulation}; " +
            $"a snapshot every {Every(o)} generations; files in {outDir}");
        Console.WriteLine("Every body starts as the same soft blob. Nothing is labelled: the physics alone decides what a part does.");

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; Stop.Cancel(); };
        WebApplication? app = o.LabServe ? Serve(o, outDir) : null;
        // SIGTERM / closing the window stops it too (the web host takes SIGTERM and stops itself)
        app?.Lifetime.ApplicationStopping.Register(() => Stop.Cancel());
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop.Cancel();

        var rows = new List<string>();
        var detail = new StringBuilder();
        foreach (var key in envs)
            foreach (var seed in seeds)
            {
                if (Stop.IsCancellationRequested) break;
                var (res, run) = RunOne(key, seed, o.LabGenerations, o.LabPopulation, Every(o), outDir, true);
                if (res == null) { Console.WriteLine(run.Error ?? "stopped"); continue; }
                rows.Add(run.File.Table!);
                detail.AppendLine(run.File.Detail);
            }
        if (rows.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(LabReport.TableHeader);
            foreach (var r in rows) Console.WriteLine(r);
            Console.WriteLine();
            Console.Write(detail.ToString());
        }
        if (app == null) return 0;
        Console.WriteLine($"Body Lab page: http://localhost:{o.Port}/bodylab.html  (it keeps serving the files above; Ctrl+C to stop)");
        Stop.Token.WaitHandle.WaitOne();
        Console.WriteLine("stopping...");
        app.StopAsync().Wait(2000);
        return 0;
    }

    static int Every(Options o) => o.LabEvery > 0 ? o.LabEvery : Math.Max(1, o.LabGenerations / 100);

    /// <summary>One evolutionary run: prints a line per batch of generations (if print), records a snapshot every
    /// `every` generations and the final species, writes the JSON. Null result if it was stopped or failed.</summary>
    public static (EvolveResult?, LiveRun) RunOne(string envKey, uint seed, int generations, int population, int every, string outDir, bool print)
    {
        var env = LabReport.EnvOf(envKey);
        Directory.CreateDirectory(outDir);
        var run = new LiveRun
        {
            Key = $"{envKey}-{seed}-{DateTime.Now:HHmmss}",
            File = new LabFile { Env = env.Name, EnvKey = envKey, Seed = seed, Population = population, Generations = generations, Every = every, Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) },
        };
        lock (Runs) Runs.Add(run);
        int printEvery = Math.Max(1, (int)Math.Round(generations / 30.0));
        var clock = Stopwatch.StartNew();
        try
        {
            var res = Evolver.Run(env, seed, population, generations, null, null, gi =>
            {
                if (Stop.IsCancellationRequested) throw new OperationCanceledException();
                bool last = gi.Gen == gi.Generations - 1;
                if (gi.Gen % every == 0 || last)
                {
                    var snap = LabReport.Snapshot(gi, env);
                    lock (run.Lock) { run.File.Snapshots.Add(snap); run.Gen = gi.Gen; }
                }
                else run.Gen = gi.Gen;
                if (print && (gi.Gen % printEvery == 0 || last))
                {
                    var b = run.File.Snapshots[^1].Gen == gi.Gen ? run.File.Snapshots[^1].Champion : LabReport.Body(gi.Pop[gi.Best], env);
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0,-10} seed {1,-3} gen {2,5}/{3}  best {4,6:F2}  species {5,2}  champion {6,9} {7,2} segs {8,2} pairs  {9,-6} {10}  ({11:F0} s)",
                        env.Name, seed, gi.Gen + 1, gi.Generations, gi.Fit[gi.Best], gi.SpeciesCount, MassText(b.Mass), b.Segments, b.PairCount, b.Mode,
                        b.Rigidity > 0.5f ? "rigid" : "soft ", clock.Elapsed.TotalSeconds));
                }
            });
            var final = new LabSnapshot
            {
                Gen = generations - 1, Best = res.BestEval.Fitness, Species = res.Species, Champion = LabReport.Body(res.Best, env, res.Champions.Count > 0 ? res.Champions[0].Size : 0),
            };
            // every final species' best but the champion's own (Champions is best first)
            foreach (var (g, _, size) in res.Champions.Skip(1)) final.Reps.Add(LabReport.Body(g, env, size));
            string detail = $"== {env.Name} (seed {seed})  final population: mean segments {res.MeanSegments:F1}, mean pairs {res.MeanPairs:F1}, species {res.Species}\n" + Evolver.Describe(res.BestBody, res.BestEval, env);
            for (int k = 1; k < Math.Min(3, res.Champions.Count); k++)
            {
                var cm = Development.Grow(res.Champions[k].G);
                detail += $"   -- species champion {k} (size {res.Champions[k].Size})\n" + Evolver.Describe(cm, Physics.Evaluate(cm, env), env);
            }
            string path = Path.Combine(outDir, FileName(envKey, seed));
            lock (run.Lock)
            {
                run.File.Final = final; run.File.Done = true;
                run.File.Table = LabReport.TableRow(res, env, seed);
                run.File.Detail = detail;
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, LabReport.Serialize(run.File));
                File.Move(tmp, path, overwrite: true);
                run.Path = path;
            }
            run.Done = true;
            if (print) Console.WriteLine($"   wrote {path} ({new FileInfo(path).Length / 1024:N0} KB, {run.File.Snapshots.Count} snapshots)");
            return (res, run);
        }
        catch (OperationCanceledException) { run.Error = "stopped"; run.Done = true; return (null, run); }
        catch (Exception e) { run.Error = "failed: " + e.Message; run.Done = true; Console.Error.WriteLine($"body lab run {envKey} seed {seed} failed: {e}"); return (null, run); }
    }

    static string MassText(float kg) => kg >= 1f ? kg.ToString("F2", CultureInfo.InvariantCulture) + " kg" : (1000f * kg).ToString(kg >= 0.01f ? "F0" : "F1", CultureInfo.InvariantCulture) + " g";

    /// <summary>The Body Lab's web server on the console's port: the embedded pages, the run files and live runs.</summary>
    static WebApplication? Serve(Options o, string outDir)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://0.0.0.0:{o.Port}");
        var app = builder.Build();
        var asm = Assembly.GetExecutingAssembly();
        var files = asm.GetManifestResourceNames().Where(n => n.StartsWith("www")).ToDictionary(n => n.Replace('\\', '/'), n => n);
        Thread? liveThread = null;

        app.MapGet("/api/bodylab/runs", () =>
        {
            object[] runs;
            lock (Runs) runs = Runs.Select(r => (object)new { key = r.Key, env = r.File.Env, envKey = r.File.EnvKey, seed = r.File.Seed, gen = r.Gen, generations = r.File.Generations, done = r.Done, error = r.Error }).ToArray();
            var list = new DirectoryInfo(outDir).GetFiles("bodylab-*.json").OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => new { name = f.Name, kb = f.Length / 1024, modified = f.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) }).ToArray();
            return Results.Json(new
            {
                lab = true, runs, files = list, envs = LabReport.EnvKeys,
                defaults = new { generations = o.LabGenerations, population = o.LabPopulation, seed = o.LabSeedList()[0] },
            });
        });
        app.MapGet("/api/bodylab/live/{key}", (string key, int? since) =>
        {
            LiveRun? r;
            lock (Runs) r = Runs.FirstOrDefault(x => x.Key == key);
            if (r == null) return Results.NotFound();
            string json;
            lock (r.Lock)
            {
                var f = r.File;
                int from = Math.Clamp(since ?? 0, 0, f.Snapshots.Count);
                var part = new LabFile
                {
                    Env = f.Env, EnvKey = f.EnvKey, Seed = f.Seed, Population = f.Population, Generations = f.Generations, Every = f.Every, Created = f.Created,
                    Done = r.Done, Final = r.Done ? f.Final : null, Table = r.Done ? f.Table : null, Snapshots = f.Snapshots.GetRange(from, f.Snapshots.Count - from),
                };
                json = JsonSerializer.Serialize(new { file = part, since = from, gen = r.Gen, error = r.Error }, LabReport.Json);
            }
            return Results.Content(json, "application/json");
        });
        app.MapGet("/api/bodylab/file/{name}", (string name) =>
        {
            if (name.IndexOfAny(new[] { '/', '\\' }) >= 0 || !name.StartsWith("bodylab-") || !name.EndsWith(".json")) return Results.BadRequest();
            string p = Path.Combine(outDir, name);
            return File.Exists(p) ? Results.File(p, "application/json") : Results.NotFound();
        });
        app.MapPost("/api/bodylab/start", async (HttpContext ctx) =>
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
            var e = doc.RootElement;
            string env = e.TryGetProperty("env", out var ev) ? ev.GetString() ?? "" : "";
            if (!LabReport.EnvKeys.Contains(env)) return Results.BadRequest(new { error = "env must be one of " + string.Join(", ", LabReport.EnvKeys) });
            int I(string k, int lo, int hi, int d) => e.TryGetProperty(k, out var v) && v.TryGetDouble(out double x) ? (int)Math.Clamp(Math.Round(x), lo, hi) : d;
            int gens = I("generations", 2, 20000, o.LabGenerations), pop = I("population", 10, 2000, o.LabPopulation);
            uint seed = (uint)I("seed", 1, int.MaxValue, 1);
            lock (Runs)
            {
                if (liveThread != null && liveThread.IsAlive || Runs.Any(r => !r.Done)) return Results.Conflict(new { error = "a run is already evolving; wait for it to finish" });
                int every = Math.Max(1, gens / 100);
                liveThread = new Thread(() => RunOne(env, seed, gens, pop, every, outDir, true)) { IsBackground = true, Name = "bodylab" };
                liveThread.Start();
            }
            Thread.Sleep(50);
            lock (Runs) return Results.Json(new { key = Runs[^1].Key });
        });
        app.MapGet("/{**path}", async ctx =>
        {
            string path = ctx.Request.Path.Value ?? "/";
            if (path == "/" || path == "") { ctx.Response.Redirect("/bodylab.html"); return; }
            // NR_WWW=<wwwroot folder>: serve the page from disk while working on it (no rebuild)
            string? dev = Environment.GetEnvironmentVariable("NR_WWW");
            string? devFile = dev != null && !path.Contains("..") ? Path.Combine(dev, path.TrimStart('/')) : null;
            Stream? s = devFile != null && File.Exists(devFile) ? File.OpenRead(devFile)
                : files.TryGetValue("www" + path, out var res) ? asm.GetManifestResourceStream(res) : null;
            if (s == null) { ctx.Response.StatusCode = 404; return; }
            ctx.Response.ContentType = Host.Mime.TryGetValue(Path.GetExtension(path), out var m) ? m : "application/octet-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            await using (s) await s.CopyToAsync(ctx.Response.Body);
        });
        try { app.StartAsync().GetAwaiter().GetResult(); }
        catch (Exception e)
        {
            Console.Error.WriteLine($"could not open port {o.Port} for the Body Lab page ({e.Message}); the runs go on without it (try --port 5801)");
            return null;
        }
        string url = $"http://localhost:{o.Port}/bodylab.html";
        Console.WriteLine($"Body Lab page: {url}  (watch the runs live there)");
        if (!o.NoBrowser) Host.OpenBrowser(url);
        return app;
    }
}
