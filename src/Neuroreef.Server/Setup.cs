// The setup screen: before a NEW prehistory is created (nothing to resume, no evolved brains), the console
// opens a page where the person chooses its size: branches, slots, founders, length, big-brained ancestors,
// and the live world's slots, with an estimate of the memory it will take. Nothing is allocated until they
// press Start, so a small machine never builds a world it cannot hold. The setup server then stops and the
// normal console starts on the same port; the page reloads itself into it.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Neuroreef.Server;

public static class Setup
{
    /// <summary>Show the setup page and wait until the person presses Start. Applies their choices to o.</summary>
    public static void Run(Options o)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://0.0.0.0:{o.Port}");
        var app = builder.Build();
        var done = new ManualResetEventSlim();
        var asm = Assembly.GetExecutingAssembly();
        string? page = asm.GetManifestResourceNames().FirstOrDefault(n => n.Replace('\\', '/') == "www/setup.html");

        long ram = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        app.MapGet("/api/setup-info", () => Results.Json(new
        {
            bytesPerSlot = World.BytesPerSlot, ramBytes = ram, cores = Environment.ProcessorCount,
            branches = o.Branches, branchSlots = o.BranchSlots, worldSlots = o.Capacity, founders = o.Founders > 0 ? o.Founders : K.ANCESTOR_COUNT,
            steps = o.Evolve && o.StepsSet ? (int)Math.Clamp(o.Steps, 0, int.MaxValue) : o.PrehistorySteps, evolve = o.Evolve, bigBrains = o.BigBrains >= 0 ? o.BigBrains : K.BIG_BRAINS, bigBrainUnits = o.BigBrainUnits > 0 ? o.BigBrainUnits : K.BIG_BRAIN_UNITS,
            maxUnits = G.NH, refUnits = K.BRAIN_REF, ancestors = K.ANCESTORS_LAND + K.ANCESTORS_WATER,
        }));
        app.MapPost("/api/setup", async (HttpContext ctx) =>
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
            var e = doc.RootElement;
            int I(string k, int lo, int hi, int dflt) => e.TryGetProperty(k, out var v) && v.TryGetInt64(out long x) ? (int)Math.Clamp(x, lo, hi) : dflt;
            o.Branches = I("branches", 1, 8, o.Branches);
            o.BranchSlots = I("branchSlots", 2048, 262144, o.BranchSlots);
            o.Capacity = I("worldSlots", 4096, 524288, o.Capacity);
            o.Founders = I("founders", 20, 5000, K.ANCESTOR_COUNT);
            o.PrehistorySteps = I("steps", 0, int.MaxValue, o.PrehistorySteps);   // 0 = until you begin the world (or stop it)
            if (o.Evolve) { o.Steps = o.PrehistorySteps; o.StepsSet = true; }   // the headless prehistory takes its length from --steps
            o.BigBrains = I("bigBrains", 0, K.ANCESTORS_LAND + K.ANCESTORS_WATER, K.BIG_BRAINS);
            o.BigBrainUnits = I("bigBrainUnits", K.BRAIN_MIN, G.NH, K.BIG_BRAIN_UNITS);
            Console.WriteLine($"setup: {o.Branches} branches x {o.BranchSlots:N0} slots, {o.Founders} founders per ancestor, " +
                $"{(o.PrehistorySteps == 0 ? "until you begin the world" : o.PrehistorySteps.ToString("N0") + " steps")}, " +
                $"{o.BigBrains} big-brained ancestors of {o.BigBrainUnits} units, world {o.Capacity:N0} slots");
            done.Set();
            return Results.Json(new { ok = true });
        });
        app.MapGet("/api/meta", () => Results.StatusCode(503));   // the console is not up yet (the page polls this)
        app.MapGet("/{**path}", async ctx =>
        {
            // every page during setup is the setup page (the console's own address included)
            if (page == null) { ctx.Response.StatusCode = 500; await ctx.Response.WriteAsync("setup page missing from this build"); return; }
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.Headers.CacheControl = "no-cache";
            await using var s = asm.GetManifestResourceStream(page)!;
            await s.CopyToAsync(ctx.Response.Body);
        });

        app.StartAsync().GetAwaiter().GetResult();
        string local = $"http://localhost:{o.Port}";
        Console.WriteLine("NEUROREEF 2 - SETUP");
        Console.WriteLine($"choose the size of the new prehistory in your browser: {local}");
        if (!o.NoBrowser) Host.OpenBrowser(local);
        bool cancelled = false;
        Console.CancelKeyPress += (_, ev) => { if (done.IsSet) return; ev.Cancel = true; cancelled = true; done.Set(); };
        done.Wait();
        if (cancelled) { app.StopAsync().Wait(2000); Console.WriteLine("setup cancelled"); Environment.Exit(0); }
        Thread.Sleep(300);                    // let the reply reach the page
        app.StopAsync().Wait(3000);
        o.SetupDone = true;
        o.NoBrowser = true;                   // the setup page reloads itself into the console
    }
}
