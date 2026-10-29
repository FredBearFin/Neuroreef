// The web host: serves the embedded console and streams frames over a WebSocket (docs/ICD.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Neuroreef.Server;

public static class Host
{
    internal static readonly Dictionary<string, string> Mime = new()
    {
        [".html"] = "text/html; charset=utf-8", [".js"] = "text/javascript; charset=utf-8", [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json", [".svg"] = "image/svg+xml", [".png"] = "image/png", [".txt"] = "text/plain; charset=utf-8",
    };

    public static int Run(Options o, GeneBank.Data? bank)
    {
        Console.WriteLine("NEUROREEF 2");
        Console.WriteLine("The world is births and deaths only: no animal is ever spawned.");
        using var host = new SimHost(o, bank);
        if (host.Pre != null)
            Console.WriteLine($"No evolved brains yet, so the PREHISTORY runs first: {host.Pre.B.Length} branches evolve at full speed " +
                (host.Pre.Pending.TotalSteps == int.MaxValue ? "(until you begin the world). "
                    : $"({host.Pre.Pending.TotalSteps:N0} steps = {(long)host.Pre.Pending.TotalSteps * K.YEARS_PER_STEP / 1_000_000:N0} million symbolic years). ") +
                "Watch it in the console; the world begins when it ends, or when you press Begin the world.");
        var state = new Shared(host);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.UseUrls($"http://0.0.0.0:{o.Port}");
        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

        var asm = Assembly.GetExecutingAssembly();
        app.MapGet("/api/meta", () => Results.Content(state.MetaJson, "application/json"));
        // the data export as a zip (written fresh on the simulation thread)
        app.MapGet("/api/export.zip", async () =>
        {
            try
            {
                string dir = await host.RequestExport();
                return Results.File(Export.Zip(dir), "application/zip", $"neuroreef-{Path.GetFileName(dir)}-step{host.W.Step}.zip");
            }
            catch (Exception ex) { return Results.Problem("export failed: " + ex.Message); }
        });
        app.Map("/ws", async ctx =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
            using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            await new Client(state, ws).Run(ctx.RequestAborted);
        });
        // embedded console files; resource names use '\' on Windows builds, so normalise them once
        var files = asm.GetManifestResourceNames().Where(n => n.StartsWith("www")).ToDictionary(n => n.Replace('\\', '/'), n => n);
        app.MapGet("/{**path}", async ctx =>
        {
            string path = ctx.Request.Path.Value ?? "/";
            if (path == "/" || path == "") path = "/index.html";
            var s = files.TryGetValue("www" + path, out var res) ? asm.GetManifestResourceStream(res) : null;
            if (s == null) { ctx.Response.StatusCode = 404; return; }
            ctx.Response.ContentType = Mime.TryGetValue(Path.GetExtension(path), out var m) ? m : "application/octet-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            await s.CopyToAsync(ctx.Response.Body);
        });

        host.Start();
        try { app.StartAsync().GetAwaiter().GetResult(); }
        catch (Exception e)
        {
            Console.Error.WriteLine($"could not open port {o.Port}: {e.Message}\ntry: Neuroreef.exe --port 5801");
            return 1;
        }
        string local = $"http://localhost:{o.Port}";
        Console.WriteLine($"console:        {local}");
        foreach (var ip in LanAddresses()) Console.WriteLine($"on your network: http://{ip}:{o.Port}   (tablet: same Wi-Fi; allow the firewall prompt)");
        Console.WriteLine("press Ctrl+C to stop" + (o.SaveBank != null ? $" (the gene bank is saved to {o.SaveBank} every 50,000 steps)" : ""));
        if (!o.NoBrowser) OpenBrowser(local);

        var done = new ManualResetEventSlim();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.Set(); };
        app.Lifetime.ApplicationStopping.Register(() => done.Set());   // SIGTERM / service stop
        AppDomain.CurrentDomain.ProcessExit += (_, _) => done.Set();
        var status = new Thread(() => StatusLoop(host, done)) { IsBackground = true };
        status.Start();
        done.Wait();
        Console.WriteLine("stopping...");
        var pre = host.Pre;
        if (pre == null) { host.StopAndSave(); app.StopAsync().Wait(2000); return 0; }
        if (pre != null)
        {
            // stopped during the prehistory: keep what has evolved so far, without making it the default
            string part = o.SaveBank ?? Path.Combine(AppContext.BaseDirectory, "prehistory-partial.nrbank");
            try { pre.StopAndWait(); pre.SaveCheckpoint(); pre.SaveBank(part); }
            catch (Exception e) { Console.Error.WriteLine("could not save the prehistory: " + e.Message); }
            Console.WriteLine($"prehistory stopped at step {pre.Step:N0}; brains so far saved: {part} (start with --load-bank to use them)");
            if (pre.CheckpointPath != null) Console.WriteLine("its whole state is saved: start Neuroreef.exe again to resume it");
        }
        else if (o.SaveBank != null) { GeneBank.Save(host.W, o.SaveBank); Console.WriteLine("gene bank saved: " + o.SaveBank); }
        app.StopAsync().Wait(2000);
        return 0;
    }

    static void StatusLoop(SimHost h, ManualResetEventSlim done)
    {
        while (!done.Wait(10000))
        {
            var pre = h.Pre;
            if (pre != null)
            {
                var parts = new List<string>();
                for (int b = 0; b < pre.B.Length; b++)
                {
                    int self = 0;
                    for (int sp = 0; sp < K.CLADES; sp++) if (Prehistory.StandingOf(pre.B[b], sp, out _, out _) == Prehistory.Standing.SelfSustaining) self++;
                    parts.Add($"branch {b}: {pre.B[b].W.AliveCount:N0} alive, {self} self-sustaining");
                }
                Console.WriteLine($"prehistory {pre.Step * (ulong)K.YEARS_PER_STEP / 1_000_000,4:N0} Myr (step {pre.Step:N0})  {pre.Rate,4:F0} steps/s  " + string.Join("  |  ", parts));
                continue;
            }
            var w = h.W;
            Console.WriteLine($"step {w.Step,9:N0}  {h.MeasuredRate,5:F0} steps/s  alive {w.AliveCount,6:N0}{(w.FreeTop == 0 ? "  WORLD FULL: births refused (raise --capacity)" : "")}");
        }
    }

    internal static IEnumerable<string> LanAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
                .Select(a => a.Address.ToString()).Distinct().ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    internal static void OpenBrowser(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS()) Process.Start("open", url);
            else if (Environment.GetEnvironmentVariable("DISPLAY") != null) Process.Start("xdg-open", url);
        }
        catch { /* no browser: the address is printed */ }
    }

    /// <summary>State shared by all clients: cached frames built on the server side.</summary>
    sealed class Shared
    {
        public readonly SimHost H;
        // the metadata and the terrain frame, rebuilt when the map changes (the world after a prehistory may be smaller)
        string _meta; byte[] _world; int _mapW;
        void Fresh() { if (_mapW != Map.W) lock (_lock) if (_mapW != Map.W) { _meta = Wire.Meta(H); _world = Wire.EncodeWorld(H.W); _mapW = Map.W; } }
        public string MetaJson { get { Fresh(); return _meta; } }
        public byte[] World { get { Fresh(); return _world; } }
        readonly object _lock = new();
        byte[]? _tele; int _teleHead = -1;
        string _explain = "[]"; string _prints = "[]"; DateTime _explainT;

        public Shared(SimHost h) { H = h; _meta = Wire.Meta(h); _world = Wire.EncodeWorld(h.W); _mapW = Map.W; _extinctAt = new long[h.W.NS]; Array.Fill(_extinctAt, -1L); }

        // the step at which each species was first seen with no living member (no adults, juveniles, eggs or embryos)
        readonly long[] _extinctAt;
        readonly int[] _alive = new int[K.CLADES];
        int _phaseSeen = -1;
        /// <summary>Called under the lock: when the viewed simulation changed (phase or branch), forget per-simulation caches.</summary>
        void CheckPhase()
        {
            if (_phaseSeen == H.PhaseVersion) return;
            _phaseSeen = H.PhaseVersion;
            Array.Fill(_extinctAt, -1L); _teleHead = -1; _tele = null; _preJson = null;
        }

        public long[] Extinct()
        {
            lock (_lock)
            {
                CheckPhase();
                var w = H.W;
                var r = new long[K.CLADES];
                for (int s = 0; s < K.CLADES; s++) r[s] = w.Sp[s].Used && !w.Sp[s].Alive ? w.Sp[s].Extinct : -1;
                return r;
            }
        }

        string? _cladesJson; int _cladesVer = -1; DateTime _cladesT;
        /// <summary>The clade table of the simulation on view: resent when it changes, and every 5 s for the live counts.</summary>
        public (string json, int ver) CladesJson()
        {
            lock (_lock)
            {
                var w = H.W;
                int ver = w.CladeScratch.Version * 7919 + H.PhaseVersion;
                if (_cladesJson == null || ver != _cladesVer || (DateTime.UtcNow - _cladesT).TotalSeconds > 5)
                {
                    _cladesJson = JsonSerializer.Serialize(new { type = "clades", step = w.Step, splits = w.T.Splits, list = Wire.Clades(w) });
                    _cladesVer = ver; _cladesT = DateTime.UtcNow;
                }
                return (_cladesJson, _cladesVer * 1000 + (int)(_cladesT.Ticks / 50_000_000 % 1000));
            }
        }

        public (byte[]? data, int head) Telemetry()
        {
            lock (_lock)
            {
                CheckPhase();
                int head = H.W.T.Head;
                if (head != _teleHead) { _tele = Wire.EncodeTelemetry(H.W); _teleHead = head; }
                return (_tele, _teleHead);
            }
        }

        string? _preJson; DateTime _preT;
        /// <summary>Prehistory progress: per branch, per species standing and count; the current evolution settings.</summary>
        public string? PrehistoryJson()
        {
            lock (_lock)
            {
                CheckPhase();
                var pre = H.Pre;
                if (pre == null) return null;
                if (_preJson != null && (DateTime.UtcNow - _preT).TotalSeconds < 1) return _preJson;
                _preT = DateTime.UtcNow;
                var e = pre.Pending;
                var branches = new List<object>();
                foreach (var b in pre.B)
                {
                    var stand = new int[K.CLADES]; var count = new int[K.CLADES]; var births = new float[K.CLADES];
                    var gen = new float[K.CLADES];
                    var traits = new float[K.CLADES * Neuroreef.Telemetry.NTRAITS];
                    var last = new float[b.W.T.Len];
                    bool has = b.W.T.SampleCount > 0 && b.W.T.Sample(b.W.T.SampleCount - 1, last);
                    for (int s = 0; s < K.CLADES; s++)
                    {
                        stand[s] = (int)Prehistory.StandingOf(b, s, out count[s], out births[s]);
                        float g = has ? last[b.W.T.SpeciesOffset(s, Neuroreef.Telemetry.S_GEN)] : 0;
                        gen[s] = float.IsFinite(g) ? g : 0;    // no animals: the mean is undefined
                        for (int k = 0; k < Neuroreef.Telemetry.NTRAITS; k++)
                        {
                            float tv = has ? last[b.W.T.SpeciesOffset(s, Neuroreef.Telemetry.S_TRAITS + k)] : 0;
                            traits[s * Neuroreef.Telemetry.NTRAITS + k] = float.IsFinite(tv) ? MathF.Round(tv, 3) : 0;
                        }
                    }
                    branches.Add(new { clades = Wire.Clades(b.W), splits = b.W.T.Splits, step = b.W.Step, alive = b.W.AliveCount, full = b.W.FreeTop == 0, standing = stand, count, births, generation = gen, traits });
                }
                _preJson = JsonSerializer.Serialize(new
                {
                    type = "prehistory", step = pre.Step, rate = pre.Rate, rateSet = pre.RateLimit, paused = pre.Paused, finished = pre.Finished,
                    yearsPerStep = K.YEARS_PER_STEP, view = H.ViewBranch, branches,
                    settings = new { mutRate = e.MutRate, mutBody = e.MutBody, mutSmall = e.MutSmall, mutBigShare = e.MutBigShare, mutBig = e.MutBig, totalSteps = e.TotalSteps },
                });
                return _preJson;
            }
        }

        public (string explain, string prints) Analysis()
        {
            lock (_lock)
            {
                if ((DateTime.UtcNow - _explainT).TotalSeconds > 2.5)
                {
                    _explainT = DateTime.UtcNow;
                    try
                    {
                        _explain = JsonSerializer.Serialize(new { type = "explain", step = H.W.Step, items = Explain.Run(H.W) });
                        _prints = JsonSerializer.Serialize(new
                        {
                            type = "fingerprints", step = H.W.Step,
                            species = Fingerprint.Run(H.W).Select(s => new { s.species, s.sampled, clusters = s.clusters.Select(c => new { c.label, c.share, c.top, centroid = c.centroid }) })
                        });
                    }
                    catch (Exception e) { Console.Error.WriteLine("analysis failed: " + e.Message); }
                }
                return (_explain, _prints);
            }
        }
    }

    /// <summary>One browser connection: a sender loop and a receiver loop.</summary>
    sealed class Client
    {
        readonly Shared _s; readonly WebSocket _ws;
        readonly SemaphoreSlim _send = new(1, 1);
        volatile int _agentHz = 20, _fieldsMs = 1000;
        volatile bool _history = true;

        public Client(Shared s, WebSocket ws) { _s = s; _ws = ws; }

        async Task Bin(byte[] b, CancellationToken ct) { await _send.WaitAsync(ct); try { await _ws.SendAsync(b, WebSocketMessageType.Binary, true, ct); } finally { _send.Release(); } }
        async Task Txt(string t, CancellationToken ct) { await _send.WaitAsync(ct); try { await _ws.SendAsync(Encoding.UTF8.GetBytes(t), WebSocketMessageType.Text, true, ct); } finally { _send.Release(); } }

        public async Task Run(CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try
            {
                await Txt(_s.MetaJson, cts.Token);
                await Bin(_s.World, cts.Token);
                var rx = Receive(cts.Token);
                var tx = SendLoop(cts.Token);
                await await Task.WhenAny(rx, tx);     // the second await surfaces a failure
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            catch (Exception e) { Console.Error.WriteLine("console connection failed: " + e.Message); }
            finally { cts.Cancel(); }
        }

        async Task SendLoop(CancellationToken ct)
        {
            var h = _s.H;
            long agentsVer = -1, fieldsVer = -1, probeVer = -1; int teleHead = -1, phaseSeen = h.PhaseVersion;
            double nextPre = 0, nextFights = 0, nextLex = 0; int cladesSent = int.MinValue; long fightSeen = -1;
            var sw = Stopwatch.StartNew();
            double nextAgents = 0, nextFields = 0, nextTele = 0, nextAnalysis = 0, nextStatus = 0, nextProbe = 0;
            while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                double t = sw.Elapsed.TotalMilliseconds;
                if (phaseSeen != h.PhaseVersion) { phaseSeen = h.PhaseVersion; _history = true; teleHead = -1; }   // a new simulation is on view
                if (_history) { _history = false; await Bin(Wire.EncodeHistory(h.W, 400), ct); }
                {
                    var (cj, cv) = _s.CladesJson();
                    if (cv != cladesSent) { cladesSent = cv; await Txt(cj, ct); }
                }
                if (t >= nextPre)
                {
                    nextPre = t + 1000;
                    var pj = _s.PrehistoryJson();
                    if (pj != null) await Txt(pj, ct);
                }
                if (t >= nextAgents)
                {
                    nextAgents = t + 1000.0 / Math.Max(1, _agentHz);
                    var (d, v) = h.Agents.Get();
                    if (v != agentsVer && d.Length > 0) { agentsVer = v; await Bin(d, ct); }
                    h.WantAgents = true;
                }
                if (t >= nextFields)
                {
                    nextFields = t + _fieldsMs;
                    var (d, v) = h.FieldsF.Get();
                    if (v != fieldsVer && d.Length > 0) { fieldsVer = v; await Bin(d, ct); }
                    h.WantFields = true;
                }
                if (t >= nextProbe && h.Sim.ProbeSerial != 0)
                {
                    nextProbe = t + 100;
                    var (d, v) = h.ProbeF.Get();
                    if (v != probeVer && d.Length > 0) { probeVer = v; await Bin(d, ct); }
                    h.WantProbe = true;
                }
                if (t >= nextTele)
                {
                    nextTele = t + 500;
                    var (d, head) = _s.Telemetry();
                    if (d != null && head != teleHead) { teleHead = head; await Bin(d, ct); }
                }
                if (t >= nextLex)
                {
                    nextLex = t + 1500;
                    await Txt(Wire.Lexicon(h.W), ct);
                }
                if (t >= nextFights)
                {
                    nextFights = t + 250;
                    var T = h.W.T; long seq = T.FightSeq;
                    if (fightSeen < 0 || seq < fightSeen) fightSeen = seq;   // a new connection or another world on view: only new fights
                    if (seq > fightSeen)
                    {
                        long from = Math.Max(fightSeen, seq - 96);   // at most 96 per message: a massacre is shown by its latest
                        var sb = new StringBuilder("{\"type\":\"fights\",\"list\":[");
                        for (long q = from; q < seq; q++)
                        {
                            int o = (int)(q % Telemetry.FIGHT_N) * Telemetry.FIGHT_F;
                            if (q > from) sb.Append(',');
                            sb.Append('[').Append(T.Fights[o].ToString("0.0", CultureInfo.InvariantCulture)).Append(',').Append(T.Fights[o + 1].ToString("0.0", CultureInfo.InvariantCulture))
                              .Append(',').Append((int)T.Fights[o + 2]).Append(',').Append((int)T.Fights[o + 3]).Append(',').Append((int)T.Fights[o + 4]).Append(',').Append((long)T.Fights[o + 5])
                              .Append(',').Append(T.FightWho[(int)(q % Telemetry.FIGHT_N) * 2]).Append(',').Append(T.FightWho[(int)(q % Telemetry.FIGHT_N) * 2 + 1]).Append(']');
                        }
                        sb.Append("]}");
                        fightSeen = seq;
                        await Txt(sb.ToString(), ct);
                    }
                }
                if (t >= nextStatus)
                {
                    nextStatus = t + 500;
                    var w = h.W;
                    await Txt(JsonSerializer.Serialize(new
                    {
                        type = "status", step = w.Step, rate = h.Rate, measured = h.MeasuredRate, paused = h.Paused, alive = w.AliveCount,
                        free = w.FreeTop, capacity = w.Cap, bankSource = h.BankSource, extinct = _s.Extinct(),
                        slotShortage = w.T.SlotShortage, probe = h.Sim.ProbeSerial, follow = h.Sim.ProbeFollow, active = h.Sim.ActiveCount,
                        season = w.SeasonAt(Map.W * 0.5f), mapSide = Map.W, rain = w.RainAnom, phase = h.Phase, phaseVersion = h.PhaseVersion, view = h.ViewBranch,
                        branches = h.Pre?.B.Length ?? 0, bankPath = h.BankPath,
                    }), ct);
                }
                if (t >= nextAnalysis)
                {
                    nextAnalysis = t + 3000;
                    var (ex, fp) = _s.Analysis();
                    await Txt(ex, ct); await Txt(fp, ct);
                }
                await Task.Delay(15, ct);
            }
        }

        async Task Receive(CancellationToken ct)
        {
            var buf = new byte[8192];
            var h = _s.H;
            while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                var r = await _ws.ReceiveAsync(buf, ct);
                if (r.MessageType == WebSocketMessageType.Close) return;
                if (r.MessageType != WebSocketMessageType.Text) continue;
                try
                {
                    using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(buf, 0, r.Count));
                    var e = doc.RootElement;
                    string cmd = e.GetProperty("cmd").GetString() ?? "";
                    switch (cmd)
                    {
                        case "select":
                        {
                            uint ser = e.GetProperty("serial").GetUInt32();
                            bool follow = e.TryGetProperty("follow", out var f) && f.GetBoolean();
                            h.Post(s => { s.ProbeSerial = ser; s.ProbeFollow = follow; s.Probe.Slot = -1; });
                            break;
                        }
                        case "pick":
                        {
                            int sp = e.GetProperty("species").GetInt32();
                            uint salt = (uint)Environment.TickCount;
                            h.Post(s => { int i = s.PickRandom(sp, salt); s.ProbeSerial = i >= 0 ? s.W.Serial[i] : 0; s.ProbeFollow = true; s.Probe.Slot = -1; });
                            break;
                        }
                        case "follow": { bool on = e.GetProperty("on").GetBoolean(); h.Post(s => s.ProbeFollow = on); break; }
                        case "rate": h.SetRate((float)e.GetProperty("value").GetDouble()); break;
                        case "pause": h.SetPaused(true); break;
                        case "resume": h.SetPaused(false); break;
                        case "branch": h.View(e.GetProperty("index").GetInt32()); break;
                        case "begin": h.BeginWorld(e.TryGetProperty("branch", out var br) ? br.GetInt32() : -1); break;
                        case "export": _ = h.RequestExport(); break;
                        case "evo":
                        {
                            // change the evolution settings of the running prehistory (applied at the next barrier)
                            var pre = h.Pre;
                            if (pre == null) break;
                            var el = e.GetProperty("set");
                            pre.Configure(s =>
                            {
                                foreach (var p in el.EnumerateObject())
                                {
                                    double v = p.Value.GetDouble();
                                    switch (p.Name)
                                    {
                                        case "mutRate": s.MutRate = (float)v; break;
                                        case "mutBody": s.MutBody = (float)v; break;
                                        case "mutSmall": s.MutSmall = (float)v; break;
                                        case "mutBigShare": s.MutBigShare = (float)v; break;
                                        case "mutBig": s.MutBig = (float)v; break;
                                        case "totalSteps": s.TotalSteps = (int)v; break;
                                    }
                                }
                            });
                            break;
                        }
                        case "step": h.StepOnce(); break;
                        case "speak":
                            {
                                // the person's voice: one of the 8 sounds at a place, heard by the animals near it on the next step
                                float sx = (float)e.GetProperty("x").GetDouble(), sy = (float)e.GetProperty("y").GetDouble();
                                int snd = Math.Clamp(e.GetProperty("sound").GetInt32(), 0, F.NSOUND - 1) + 1;
                                h.Post(s => { var w = s.W; w.UserCallX = World.Wrap(sx); w.UserCallY = World.Wrap(sy); w.UserCallSound = snd; w.UserCallStep = w.Step - 1; /* as if made last step: heard on the step that comes next */ w.UserCallId++; });
                                break;
                            }
                        case "history": _history = true; break;
                        case "view":
                            if (e.TryGetProperty("hz", out var hz)) _agentHz = Math.Clamp(hz.GetInt32(), 1, 30);
                            if (e.TryGetProperty("fieldsMs", out var fm)) _fieldsMs = Math.Clamp(fm.GetInt32(), 250, 10000);
                            break;
                        case "savebank":
                        {
                            string path = Path.Combine(Environment.CurrentDirectory, $"neuroreef-bank-step{h.W.Step}.nrbank");
                            h.Post(s => { GeneBank.Save(s.W, path); Console.WriteLine("gene bank saved: " + path); });
                            await Txt(JsonSerializer.Serialize(new { type = "ack", cmd, path }), ct);
                            break;
                        }
                    }
                }
                catch (Exception ex) { await Txt(JsonSerializer.Serialize(new { type = "error", message = ex.Message }), ct); }
            }
        }
    }
}
