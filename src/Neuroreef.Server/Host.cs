// The web host: serves the embedded console and streams frames over a WebSocket (docs/ICD.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    static readonly Dictionary<string, string> Mime = new()
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
                $"({host.Pre.Pending.TotalSteps:N0} steps = {(long)host.Pre.Pending.TotalSteps * K.YEARS_PER_STEP / 1_000_000:N0} million symbolic years). " +
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
        if (pre != null)
        {
            // stopped during the prehistory: keep what has evolved so far, without making it the default
            string part = o.SaveBank ?? Path.Combine(AppContext.BaseDirectory, "prehistory-partial.nrbank");
            try { pre.StopAndWait(); pre.SaveBank(part); }
            catch (Exception e) { Console.Error.WriteLine("could not save the prehistory: " + e.Message); }
            Console.WriteLine($"prehistory stopped at step {pre.Step:N0}; brains so far saved: {part} (start with --load-bank to use them)");
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
                    for (int sp = 0; sp < Roster.Count; sp++) if (Prehistory.StandingOf(pre.B[b], sp, out _, out _, out _) == Prehistory.Standing.SelfSustaining) self++;
                    parts.Add($"{(char)('A' + b)}: {pre.B[b].W.AliveCount:N0} alive, {self} self-sustaining");
                }
                Console.WriteLine($"prehistory {pre.Step * (ulong)K.YEARS_PER_STEP / 1_000_000,4:N0} Myr (step {pre.Step:N0})  {pre.Rate,4:F0} steps/s  " + string.Join("  |  ", parts));
                continue;
            }
            var w = h.W;
            Console.WriteLine($"step {w.Step,9:N0}  {h.MeasuredRate,5:F0} steps/s  alive {w.AliveCount,6:N0}{(w.FreeTop == 0 ? "  WORLD FULL: births refused (raise --capacity)" : "")}");
        }
    }

    static IEnumerable<string> LanAddresses()
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

    static void OpenBrowser(string url)
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
        public readonly string MetaJson;
        public readonly byte[] World;
        readonly object _lock = new();
        byte[]? _tele; int _teleHead = -1;
        string _explain = "[]"; string _prints = "[]"; DateTime _explainT;

        public Shared(SimHost h) { H = h; MetaJson = Wire.Meta(h); World = Wire.EncodeWorld(h.W); _extinctAt = new long[h.W.NS]; Array.Fill(_extinctAt, -1L); }

        // the step at which each species was first seen with no living member (no adults, juveniles, eggs or embryos)
        readonly long[] _extinctAt;
        readonly int[] _alive = new int[Roster.Count];
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
                if (H.IsPrehistory) return (long[])_extinctAt.Clone();     // in the prehistory colonists can return a species
                var w = H.W;
                Array.Clear(_alive);
                for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] != St.Dead) _alive[w.Spc[i]]++;
                for (int s = 0; s < w.NS; s++) if (_alive[s] == 0 && _extinctAt[s] < 0 && w.Step > 0) _extinctAt[s] = (long)w.Step;
                return (long[])_extinctAt.Clone();
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
                    var stand = new int[Roster.Count]; var count = new int[Roster.Count]; var births = new float[Roster.Count]; var col = new float[Roster.Count];
                    var gen = new float[Roster.Count];
                    var last = new float[b.W.T.Len];
                    bool has = b.W.T.SampleCount > 0 && b.W.T.Sample(b.W.T.SampleCount - 1, last);
                    for (int s = 0; s < Roster.Count; s++)
                    {
                        stand[s] = (int)Prehistory.StandingOf(b, s, out count[s], out births[s], out col[s]);
                        float g = has ? last[b.W.T.SpeciesOffset(s, Neuroreef.Telemetry.S_GEN)] : 0;
                        gen[s] = float.IsFinite(g) ? g : 0;    // no animals: the mean is undefined
                    }
                    branches.Add(new { step = b.W.Step, alive = b.W.AliveCount, full = b.W.FreeTop == 0, standing = stand, count, births, colonists = col, generation = gen });
                }
                _preJson = JsonSerializer.Serialize(new
                {
                    type = "prehistory", step = pre.Step, rate = pre.Rate, paused = pre.Paused, finished = pre.Finished, migrations = pre.Migrations,
                    yearsPerStep = K.YEARS_PER_STEP, view = H.ViewBranch, branches,
                    settings = new { mutRate = e.MutRate, mutSmall = e.MutSmall, mutBigShare = e.MutBigShare, mutBig = e.MutBig, archiveShare = e.ArchiveShare,
                        tournament = e.Tournament, floor = e.Floor, colonistSteps = e.ColonistSteps, totalSteps = e.TotalSteps, migrateEvery = e.MigrateEvery, migrants = e.Migrants },
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
            double nextPre = 0;
            var sw = Stopwatch.StartNew();
            double nextAgents = 0, nextFields = 0, nextTele = 0, nextAnalysis = 0, nextStatus = 0, nextProbe = 0;
            while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                double t = sw.Elapsed.TotalMilliseconds;
                if (phaseSeen != h.PhaseVersion) { phaseSeen = h.PhaseVersion; _history = true; teleHead = -1; }   // a new simulation is on view
                if (_history) { _history = false; await Bin(Wire.EncodeHistory(h.W, 400), ct); }
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
                if (t >= nextStatus)
                {
                    nextStatus = t + 500;
                    var w = h.W;
                    await Txt(JsonSerializer.Serialize(new
                    {
                        type = "status", step = w.Step, rate = h.Rate, measured = h.MeasuredRate, paused = h.Paused, alive = w.AliveCount,
                        free = w.FreeTop, capacity = w.Cap, bankSource = h.BankSource, extinct = _s.Extinct(),
                        slotShortage = w.T.SlotShortage, probe = h.Sim.ProbeSerial, follow = h.Sim.ProbeFollow, active = h.Sim.ActiveCount,
                        season = w.SeasonAt(K.WORLD * 0.5f), rain = w.RainAnom, phase = h.Phase, phaseVersion = h.PhaseVersion, view = h.ViewBranch,
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
                        case "rate": h.Rate = (float)e.GetProperty("value").GetDouble(); break;
                        case "pause": h.SetPaused(true); break;
                        case "resume": h.SetPaused(false); break;
                        case "branch": h.View(e.GetProperty("index").GetInt32()); break;
                        case "begin": h.BeginWorld(); break;
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
                                        case "mutSmall": s.MutSmall = (float)v; break;
                                        case "mutBigShare": s.MutBigShare = (float)v; break;
                                        case "mutBig": s.MutBig = (float)v; break;
                                        case "archiveShare": s.ArchiveShare = (float)v; break;
                                        case "tournament": s.Tournament = (int)Math.Round(v); break;
                                        case "floor": s.Floor = (float)v; break;
                                        case "colonistSteps": s.ColonistSteps = (int)v; break;
                                        case "totalSteps": s.TotalSteps = (int)v; break;
                                        case "migrateEvery": s.MigrateEvery = (int)v; break;
                                        case "migrants": s.Migrants = (int)Math.Round(v); break;
                                    }
                                }
                            });
                            break;
                        }
                        case "step": h.StepOnce(); break;
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
