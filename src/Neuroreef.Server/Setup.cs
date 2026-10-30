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
            bytesPerSlot = World.BytesPerSlot, ramBytes = ram, cores = Environment.ProcessorCount, evolve = o.Evolve,
            maxUnits = G.NH, maxSlots = World.MaxSlots, refUnits = K.BRAIN_START, mapSizes = Map.Sizes, mapNames = Map.Names,
            v = new
            {
                preMap = o.PreMap, worldMap = o.WorldMap, water = o.Water, ponds = o.Ponds, climate = o.Climate,
                plants = o.Plants, seasons = o.Seasons, seasonLength = o.SeasonLength, drought = o.Drought, fire = o.Fire, disease = o.Disease, thirst = o.Thirst,
                ancestorsLand = o.AncestorsLand, ancestorsWater = o.AncestorsWater, founders = o.Founders > 0 ? o.Founders : K.ANCESTOR_COUNT,
                bigBrains = o.BigBrains >= 0 ? o.BigBrains : K.BIG_BRAINS, bigBrainUnits = o.BigBrainUnits > 0 ? o.BigBrainUnits : K.BIG_BRAIN_UNITS,
                matingDrive = o.MatingDrive, mutBrain = 1f, mutBody = 1f, worldMut = o.WorldMut,
                branches = o.Branches, branchSlots = o.BranchSlots,
                steps = o.Evolve && o.StepsSet ? (int)Math.Clamp(o.Steps, 0, int.MaxValue) : o.PrehistorySteps,
                worldSlots = o.Capacity, rate = o.Rate,
            },
        }));
        app.MapPost("/api/setup", async (HttpContext ctx) =>
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
            var e = doc.RootElement;
            int I(string k, int lo, int hi, int dflt) => e.TryGetProperty(k, out var v) && v.TryGetDouble(out double x) ? (int)Math.Clamp(Math.Round(x), lo, hi) : dflt;
            float F(string k, float lo, float hi, float dflt) => e.TryGetProperty(k, out var v) && v.TryGetDouble(out double x) ? (float)Math.Clamp(x, lo, hi) : dflt;
            o.PreMap = I("preMap", 256, 2048, o.PreMap); o.WorldMap = I("worldMap", 256, 2048, o.WorldMap); o.Water = F("water", 0.02f, 0.6f, o.Water); o.Ponds = F("ponds", 0f, 1f, o.Ponds); o.Climate = F("climate", 0f, 2f, o.Climate);
            o.Plants = F("plants", 0.1f, 4f, o.Plants); o.Seasons = F("seasons", 0f, 2.2f, o.Seasons); o.SeasonLength = I("seasonLength", 1000, 100000, o.SeasonLength);
            o.Drought = F("drought", 0f, 4f, o.Drought); o.Fire = F("fire", 0f, 10f, o.Fire); o.Disease = F("disease", 0f, 5f, o.Disease); o.Thirst = F("thirst", 0.1f, 4f, o.Thirst);
            o.AncestorsLand = I("ancestorsLand", 0, 16, o.AncestorsLand); o.AncestorsWater = I("ancestorsWater", 0, 16, o.AncestorsWater);
            o.Founders = I("founders", 20, 5000, K.ANCESTOR_COUNT);
            o.BigBrains = I("bigBrains", 0, 16, K.BIG_BRAINS); o.BigBrainUnits = I("bigBrainUnits", K.BRAIN_MIN, G.NH, K.BIG_BRAIN_UNITS);
            o.MatingDrive = F("matingDrive", 0f, 3f, o.MatingDrive);
            o.MutBrainX = F("mutBrain", 0f, 5f, 1f); o.MutBodyX = F("mutBody", 0f, 5f, 1f); o.WorldMut = F("worldMut", 0f, 10f, o.WorldMut);
            o.Branches = I("branches", 1, 8, o.Branches); o.BranchSlots = I("branchSlots", 2048, World.MaxSlots, o.BranchSlots);
            o.PrehistorySteps = I("steps", 0, int.MaxValue, o.PrehistorySteps);   // 0 = until you begin the world (or stop it)
            if (o.Evolve) { o.Steps = o.PrehistorySteps; o.StepsSet = true; }   // the headless prehistory takes its length from --steps
            o.Capacity = I("worldSlots", 1024, World.MaxSlots, o.Capacity); o.Rate = F("rate", 0f, 120f, o.Rate);
            Console.WriteLine($"setup: prehistory on a {o.PreMap}-unit map, {o.Branches} branches x {o.BranchSlots:N0} slots, " +
                $"{o.AncestorsLand} land + {o.AncestorsWater} water ancestors x {o.Founders} founders, {o.BigBrains} big-brained ({o.BigBrainUnits} units), " +
                $"{(o.PrehistorySteps == 0 ? "until you begin the world" : o.PrehistorySteps.ToString("N0") + " steps")}; world {o.WorldMap}-unit map, {o.Capacity:N0} slots");
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
