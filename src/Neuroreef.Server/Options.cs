using System;
using System.Collections.Generic;

namespace Neuroreef.Server;

/// <summary>Command-line options. Every option is listed in Usage.</summary>
public sealed class Options
{
    public uint Seed;   // 0 until chosen: --seed N, or a fresh random one for every new world (Parse)
    // slots a world (--capacity) or branch (--branch-slots) starts with: it grows as births need more. Default: a quarter of
    // the machine's memory (World.DefaultSlots; a branch's share of it if there are several), so it suits the machine
    public int Capacity = World.DefaultSlots();
    public int MaxCapacity;             // --max-capacity: the most slots a world or branch may grow to (0: as many as memory holds)
    // every core: the step loop's main thread works its share of each phase instead of waiting (2026-10-31: cores - 1 left a
    // quarter of a 4-core machine idle in the live server and headless runs)
    public int Threads = Math.Max(1, Math.Min(Environment.ProcessorCount, 8));
    public int Port = 5800;
    public bool Headless, RandomBrains, NoBrowser, PrintConstants, Help, Empty, Evolve, StepsSet, NewPrehistory;
    public string? ReportTxt;
    public long Steps = 100000;
    public int ReportEvery = 2000;
    public string? LoadBank, SaveBank, Csv, CheckpointPath;
    public double CheckpointMinutes = 30;
    public int BigBrains = -1, BigBrainUnits = 0, Founders = 0, BranchSlots;   // BranchSlots 0 until Parse: World.DefaultSlots(Branches)
    // the map of a prehistory and of the world: a valley by default (2026-11-01, the owner: "the worlds are too damn big ...
    // the world should be easy to see the whole thing"; first a region of 512 units, 28 km, then the valley; then "a
    // default world of ~1 km across, one valley with a stream, hills and a pond"): 256 units is 1 km at 16 m a field cell,
    // the whole of it in one view. The region (512, 2 km), the continent (1024, 4 km; 2026-10-30: against 512 the same
    // worlds kept 4-13 lineages instead of 3) and the supercontinent (2048, 8 km) stay choices on the setup screen and
    // --map / --world-map. 2026-11-01 (the owner, after the size sweep): the default is the supercontinent, 8 km, the
    // smallest map that kept 7-13 lineages at about the real (Damuth) density; one branch by default so it fits a home PC
    public int PreMap = 2048, WorldMap = 2048;
    public int AncestorsLand = K.ANCESTORS_LAND, AncestorsWater = K.ANCESTORS_WATER;
    public float MatingDrive = 1f, MutBrainX = 1f, MutBodyX = 1f;
    // nature (Nature.cs): multipliers on the laws, 1 = as documented
    public float Plants = 1f, Seasons = 1f, Drought = 1f, Fire = 1f, Disease = 1f, Thirst = 1f, Water = K.LAKE_SHARE, Ponds = 1f, Climate = K.CLIMATE;
    // the ocean (2026-11-02, Ocean.cs): --ocean puts the whole world under the sea (every ancestor in the water); --islands X
    // leaves that share of it standing above the sea (0 by default: no land)
    public bool Ocean; public float Islands, OceanShallow = K.OCEAN_BANKS;   // --ocean-shallow S: the share of its floor on banks and reef flats (1-30 m)
    public int SeasonLength = K.T_SEASON;
    // the length of a step in hours (2026-11-02; Nature.StepH): the prehistory's (--pre-step-minutes) and the watched world's
    // (--world-step-minutes); --step-minutes sets both. Both K.STEP_H (2.92 h) by default: a coarse prehistory evolves fast
    public float PreStepH = K.STEP_H, WorldStepH = K.STEP_H;

    /// <summary>A step of mn minutes in hours, clamped to Nature's range; the default (175.3 min, within a hundredth of a
    /// minute) is K.STEP_H exactly, so the default world is the reference run bit for bit.</summary>
    public static float StepHFromMinutes(float mn)
    {
        if (!(mn > 0f) || MathF.Abs(mn - K.STEP_H * 60f) < 0.01f) return K.STEP_H;
        return Math.Clamp(mn / 60f, Nature.STEP_H_MIN, Nature.STEP_H_MAX);
    }
    // the bodies (BodySize, 2026-11-01): the mass gene's range in kg and the size classes' floors (null: in the default's
    // proportions of the range). The default is 0.1-40 kg with floors 0.1, 1, 3 and 10 kg
    public float MassMin = K.MASS_LO, MassMax = K.MASS_HI;
    public float[]? Floors;
    // --validate N (Validate.cs): N seeds, each run normally and with the memory-reset control
    public int ValidateSeeds, BigBrainMin = 64;
    public bool SeedSet;
    // --bodylab (BodyLab.cs): the body-plan prototype on its own, with its 3D page. --seeds N runs seeds --seed .. --seed+N-1
    // (from 1 by default); --seeds 3,7,9 runs those
    public bool BodyLab, LabServe = true;
    public string LabEnv = "all";
    public string? LabOut;
    public int LabSeeds = 1, LabGenerations = 800, LabPopulation = 200, LabEvery;
    public uint[]? LabSeedArray;

    /// <summary>The Body Lab's seeds: the --seeds list, or --seeds N from --seed (default 1).</summary>
    public uint[] LabSeedList()
    {
        if (LabSeedArray != null) return LabSeedArray;
        uint first = SeedSet ? Seed : 1u;
        var s = new uint[Math.Max(1, LabSeeds)];
        for (int i = 0; i < s.Length; i++) s[i] = first + (uint)i;
        return s;
    }

    /// <summary>Apply the nature settings and the map for what is about to be built (not when resuming a save).</summary>
    public void ApplyNature(bool prehistory)
    {
        Nature.Plants = Plants; Nature.Seasons = Seasons; Nature.SeasonLength = SeasonLength; Nature.Drought = Drought;
        Nature.Fire = Fire; Nature.Disease = Disease; Nature.Thirst = Thirst; Nature.Water = Water; Nature.Ponds = Ponds; Nature.Climate = Climate; Nature.WorldMap = WorldMap;
        Nature.Ocean = Ocean; Nature.Islands = Islands; Nature.OceanShallow = OceanShallow;
        Nature.StepH = prehistory ? PreStepH : WorldStepH; Nature.WorldStepH = WorldStepH;
        Nature.Clamp();
        BodySize.Set(MassMin, MassMax, Floors);   // before the map: its hash cells follow the largest body
        Map.Set(prehistory ? PreMap : WorldMap);
    }
    public bool NoSetup, SetupDone;
    public bool NoCheckpoint, ResumePrehistory, ResumeWorld, NewWorld;
    public int PrehistorySteps = K.PREHISTORY_STEPS, Branches = 1;   // one branch: an 8 km default map is ~4 GB a branch (2026-11-01)

    /// <summary>The prehistory's starting evolution settings from the command line.</summary>
    /// <summary>The live world's mutation: the prehistory's, x WorldMut (a small valley needs it to show change).</summary>
    public EvoSettings WorldSettings()
    {
        var e = new EvoSettings { MatingDrive = MatingDrive };
        e.MutRate *= MutBrainX * WorldMut; e.MutBody *= MutBodyX * WorldMut;
        e.Clamp();
        return e;
    }

    public EvoSettings PrehistorySettings(bool stepsAreLength)
    {
        // PrehistorySteps is a time, in reference steps (K.STEP_H; --prehistory-years x the seasons' year): at another
        // prehistory step it is that many hours of steps (2026-11-02; the default is the same 500,000 steps). --steps (the
        // headless prehistory's length) stays a count of its own steps
        long n = stepsAreLength && StepsSet ? Steps
            : PreStepH == K.STEP_H || PrehistorySteps <= 0 ? PrehistorySteps : (long)Math.Round(PrehistorySteps * (double)K.STEP_H / PreStepH);
        var e = new EvoSettings { TotalSteps = n <= 0 || n > int.MaxValue ? int.MaxValue : (int)n };   // 0 = until stopped
        if (BigBrains >= 0) e.BigBrains = BigBrains;
        if (BigBrainUnits > 0) e.BigBrainUnits = BigBrainUnits;
        if (Founders > 0) e.Founders = Founders;
        e.AncestorsLand = AncestorsLand; e.AncestorsWater = AncestorsWater; e.MatingDrive = MatingDrive;
        if (Ocean) e.AllWater();   // an ocean world has no land to found on
        e.MutRate *= MutBrainX; e.MutBody *= MutBodyX;
        return e;
    }
    public string BankSource = "random";      // evolved, shipped, file, or random
    public float WorldMut = 3f;         // x mutation in the live world (time compression; 1 = the prehistory's rate)
    public float Rate = 3;              // the world starts at a watchable pace (the speed buttons change it)

    public const string Usage = @"Neuroreef 2 - an energy-cycle ecosystem of learning brains

  Neuroreef.exe [options]

  Starting the program runs the PREHISTORY first, live in the browser: several branches (separate
  worlds on the same land) evolve at full speed over about 500 million symbolic years. Each starts from
  founders and is then births and deaths only; nothing is ever added, so a species that dies out is
  gone. You can watch any branch and change the mutation settings while it runs. When it ends, or when
  you press 'Begin the world', the best brains of each species seed THE WORLD: births and deaths only.
  The evolved brains are saved as evolved.nrbank next to the exe; the next start begins the world
  from them at once (delete or rename the file to run a new prehistory).

  (no options)          prehistory, then the world (or the world at once if evolved.nrbank exists)
  --prehistory          run a new prehistory even if evolved.nrbank exists
  --random-brains       skip the prehistory: the world with random brains (most species will die out)
  --load-bank FILE      begin the world with the brains in this gene bank
  --branches N          prehistory branches (default 1)
  --prehistory-steps N  length of the prehistory in reference steps of 2.92 h (default 500000 = 167 years of world time,
                        500 million symbolic years); at another --pre-step-minutes it is the same time in that step's steps
  --prehistory-years Y  the same, in years (Y x the seasons' year)
  --seed N              world and run seed: the same N gives the same map, ancestors and names
                        (default: a new random seed every time; it is printed at start)
  --capacity N          agent slots the world starts with (default: a quarter of the memory, 4,096-32,768;
                        about 19,000 = 4 GB on a 16 GB PC); it grows as births need more,
                        limited only by memory (it keeps an eighth of the machine's memory, at least 1 GB, free)
  --max-capacity N      the most slots a world or prehistory branch may grow to (default: as many as memory holds)
  --threads N           worker threads (default: all cores, max 8)
  --port N              web console port (default 5800)
  --rate N              steps per second of the world (default 3, 0 = as fast as possible)
  --no-browser          do not open the browser
  --save-bank FILE      where to save evolved brains (default evolved.nrbank next to the exe)

  --evolve              the prehistory without the console (headless): saves evolved.nrbank and
                        evolve-report.txt/.csv next to the exe (what Evolve.bat runs). It saves its
                        whole state to prehistory.nrck every 30 minutes; starting it again with the
                        same --branches and --seed resumes from there (a reboot loses at most 30 min)
  --steps 0             evolve: run until stopped (Ctrl+C saves and stops)
  --checkpoint FILE     evolve: where to save and resume the whole state (default prehistory.nrck)
  --checkpoint-minutes N   evolve: minutes between saves (default 30)
  --no-checkpoint       evolve: never save or resume the whole state
  --big-brains N        prehistory: how many of the 10 ancestors in each branch start big-brained (default 1,
                        on land; 0 = none)
  --big-brain-units N   prehistory: memory units of a big-brained ancestor (default 256; others start with up to 24)
  --founders N          prehistory: founders per ancestor (default 600)
  --branch-slots N      prehistory: animal slots each branch starts with (default: as --capacity, shared
                        among the branches; it grows as needed)
  --no-setup            skip the setup screen before a new prehistory (use the defaults or these options)
  --map valley|region|continent|supercontinent        map of the prehistory, and of the world unless --world-map is given
                        (default supercontinent; 256/512/1024/2048 units, 1/2/4/8 km across)
  --world-map valley|region|continent|supercontinent  map of the world you watch (default supercontinent, as big as the prehistory)
  --ancestors-land N, --ancestors-water N   random ancestors (default 6 and 4; at most 16 in all)
  --mating-drive X      strength of the breeding instinct (default 1)
  Nature (multipliers, 1 = as documented): --plants X  --seasons X  --drought X  --fire X  --disease X  --thirst X
  --season-length N     reference steps per year (default 3000)   --water X   share of the map under water (default 0.10)
  --step-minutes X      the length of a step in minutes, 1..720, for the prehistory and the world (default 175.3 = 2.92 h:
                        3000 steps a year). Fine steps (1-10 min) let animals move at watchable real speeds; the default
                        and coarser run evolution fast
  --pre-step-minutes X, --world-step-minutes X   the same for the prehistory or the watched world alone (a coarse
                        prehistory and a fine world: --world-step-minutes 10)
  --ponds X             share of land basins with seasonal ponds (default 1; 0 = water only in lakes and rivers)
  --ocean               an ocean world: the whole map under the sea (a few banks, a shelf, slopes and deep basins as
                        deep as the map's relief), seawater, and every ancestor in the water
  --islands X           an ocean world with this share of the map above the sea as islands (default 0: no land)
  --ocean-shallow S     an ocean world with this share of its floor on banks and reef flats 1-30 m deep (default 0.05;
                        0.25 is a reef-flat world); the next 0.6 is shelf to 130 m, the rest slopes to the basins
  --climate X           climate strength 0..2: how strongly sun, height and seasons change temperature
                        (default 0.6; 0 = everywhere 20 °C and no body-heat budget)
  --bodies normal|giants|tiny   body sizes: 0.1-40 kg (default), 100 kg-20 t, or 1 g-1 kg
  --body-min KG, --body-max KG   the smallest and largest body the mass gene gives (0.001..50000 kg)
  --body-floors A,B,C,D the four size classes' smallest bodies in kg (default: 0.1, 1, 3, 10 in the same
                        log proportions of the range)
  --world-mutation X    mutation in the world you watch, x the prehistory's (default 3: faster change in a small valley)
  --headless            run the world without the console and print reports
  --steps N             headless: number of steps (default 100000)
  --report N            headless: report interval in steps (default 2000)
  --csv FILE            headless: write the telemetry ring as CSV at the end
  --empty               headless: plants only, no animals (checks the vegetation laws on their own)
  --validate N          headless experiment: N seeds (from --seed, default 1), each world run twice for --steps steps
                        (default 5000): normally, and with every animal's recurrent memory zeroed each step (learned
                        weights kept). Prints survival of lineages and big-brained lineages, population, brain size, steps/s
  --big-brain-min N     validate: memory units from which a lineage or animal counts as big-brained (default 64)
  --print-constants     print docs/CONSTANTS.md and exit

  BODY LAB (the body-plan prototype, docs/BODYPLAN.md; it does not start the world)
  --bodylab             evolve bodies from one soft blob by physics alone, print the progress and a table,
                        write bodylab-<env>-<seed>.json, and show them in 3D at http://localhost:5800/bodylab.html
  --env E               open-water, sea-floor, land or all (default all)
  --seeds N             seeds --seed .. --seed+N-1 (default 1 seed, from 1); or a list: --seeds 3,7,9
  --generations N       generations per run (default 800)
  --population N        bodies per generation (default 200)
  --every N             a snapshot of the champion and the species every N generations (default: generations/100)
  --out DIR             where to write the JSON files (default: the bodylab folder next to the exe)
  --no-serve            run, write the files and exit without the page
";

    /// <summary>What limits a run, printed at start: the slots it starts with, their memory, and how far memory (or
    /// --max-capacity) lets it grow (World.Grow keeps World.MemoryReserve free).</summary>
    public static string MemoryLine(string what, int slots, int maxCapacity, int worlds = 1)
    {
        long avail = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, reserve = World.MemoryReserve(avail);
        long per = World.BytesPerSlot, most = Math.Max(0, avail - reserve) / per / Math.Max(1, worlds);
        string limit = maxCapacity > 0 && maxCapacity < most ? $"{Math.Max(maxCapacity, slots):N0} (--max-capacity)" : $"about {Math.Min(most, World.MaxSlots):N0} (memory, less the land)";
        return $"memory: {what} start{(worlds == 1 ? "s" : "")} with {slots:N0} slots x {per / 1024:N0} KB = {Gb((long)slots * per)}" +
            $"{(worlds > 1 ? $" each ({worlds})" : "")}; {Gb(avail)} available, {Gb(reserve)} kept free; " +
            $"{(worlds > 1 ? "each" : "it")} may grow to {limit} slots";
    }
    static string Gb(long b) => $"{b / (double)(1L << 30):F1} GB";

    public static Options Parse(string[] a)
    {
        var o = new Options();
        bool worldMapSet = false; double years = 0;
        for (int i = 0; i < a.Length; i++)
        {
            string s = a[i];
            string Next() => i + 1 < a.Length ? a[++i] : throw new ArgumentException("missing value after " + s);
            switch (s)
            {
                case "--seed": o.Seed = uint.Parse(Next()); o.SeedSet = true; break;
                case "--capacity": o.Capacity = Math.Clamp(int.Parse(Next()), 1024, World.MaxSlots); break;
                case "--max-capacity": o.MaxCapacity = Math.Clamp(int.Parse(Next()), 1024, World.MaxSlots); break;
                case "--threads": o.Threads = int.Parse(Next()); break;
                case "--port": o.Port = int.Parse(Next()); break;
                case "--rate": o.Rate = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--random-brains": o.RandomBrains = true; break;
                case "--prehistory": o.NewPrehistory = true; break;
                case "--no-browser": o.NoBrowser = true; break;
                case "--load-bank": o.LoadBank = Next(); break;
                case "--save-bank": o.SaveBank = Next(); break;
                case "--prehistory-steps": o.PrehistorySteps = int.Parse(Next()); break;
                case "--prehistory-years": years = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--branches": o.Branches = Math.Clamp(int.Parse(Next()), 1, 8); o.NoSetup = true; break;
                case "--headless": o.Headless = true; break;
                case "--empty": o.Empty = true; break;
                case "--steps": o.Steps = long.Parse(Next()); o.StepsSet = true; break;
                case "--evolve": o.Evolve = true; break;
                case "--report": o.ReportEvery = int.Parse(Next()); break;
                case "--csv": o.Csv = Next(); break;
                case "--checkpoint": o.CheckpointPath = Next(); break;
                case "--checkpoint-minutes": o.CheckpointMinutes = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--no-checkpoint": o.NoCheckpoint = true; break;
                case "--new-world": o.NewWorld = true; break;
                case "--big-brains": o.BigBrains = int.Parse(Next()); o.NoSetup = true; break;
                case "--founders": o.Founders = int.Parse(Next()); o.NoSetup = true; break;
                case "--branch-slots": o.BranchSlots = Math.Clamp(int.Parse(Next()), 2048, World.MaxSlots); o.NoSetup = true; break;
                case "--no-setup": o.NoSetup = true; break;
                // the world is as big as the prehistory unless --world-map says otherwise (2026-11-01: --map valley with
                // --headless --random-brains built the 8 km default world, since only the prehistory took the map)
                case "--map": o.PreMap = MapSide(Next()); if (!worldMapSet) o.WorldMap = o.PreMap; o.NoSetup = true; break;
                case "--world-map": o.WorldMap = MapSide(Next()); worldMapSet = true; break;
                case "--ancestors-land": o.AncestorsLand = int.Parse(Next()); o.NoSetup = true; break;
                case "--ancestors-water": o.AncestorsWater = int.Parse(Next()); o.NoSetup = true; break;
                case "--mating-drive": o.MatingDrive = F(Next()); break;
                case "--plants": o.Plants = F(Next()); break;
                case "--seasons": o.Seasons = F(Next()); break;
                case "--season-length": o.SeasonLength = int.Parse(Next()); break;
                case "--step-minutes": o.PreStepH = o.WorldStepH = StepHFromMinutes(float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture)); break;
                case "--pre-step-minutes": o.PreStepH = StepHFromMinutes(float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture)); break;
                case "--world-step-minutes": o.WorldStepH = StepHFromMinutes(float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture)); break;
                case "--drought": o.Drought = F(Next()); break;
                case "--fire": o.Fire = F(Next()); break;
                case "--disease": o.Disease = F(Next()); break;
                case "--thirst": o.Thirst = F(Next()); break;
                case "--water": o.Water = F(Next()); break;
                case "--ponds": o.Ponds = F(Next()); break;
                case "--ocean": o.Ocean = true; break;
                case "--islands": o.Islands = Math.Clamp(F(Next()), 0f, 0.5f); o.Ocean = true; break;
                case "--ocean-shallow": o.OceanShallow = Math.Clamp(F(Next()), 0f, 0.6f); o.Ocean = true; break;
                case "--climate": o.Climate = F(Next()); break;
                case "--world-mutation": o.WorldMut = F(Next()); break;
                case "--bodies": (o.MassMin, o.MassMax) = BodyPreset(Next()); o.Floors = null; break;
                case "--body-min": o.MassMin = F(Next()); break;
                case "--body-max": o.MassMax = F(Next()); break;
                case "--body-floors": o.Floors = Array.ConvertAll(Next().Split(','), F); if (o.Floors.Length != K.FLOOR_CLASSES) throw new ArgumentException($"--body-floors needs {K.FLOOR_CLASSES} masses"); break;
                case "--big-brain-units": o.BigBrainUnits = int.Parse(Next()); o.NoSetup = true; break;
                case "--validate": o.ValidateSeeds = Math.Max(1, int.Parse(Next())); o.Headless = true; break;
                case "--big-brain-min": o.BigBrainMin = Math.Clamp(int.Parse(Next()), K.BRAIN_MIN, G.NH); break;
                case "--print-constants": o.PrintConstants = true; break;
                case "--bodylab": o.BodyLab = true; break;
                case "--env":
                    o.LabEnv = Next().ToLowerInvariant().Replace('_', '-');
                    if (o.LabEnv != "all" && Array.IndexOf(LabReport.EnvKeys, o.LabEnv) < 0) throw new ArgumentException("--env open-water|sea-floor|land|all");
                    break;
                case "--seeds":
                {
                    string v = Next();
                    if (v.Contains(',')) { o.LabSeedArray = Array.ConvertAll(v.Split(',', StringSplitOptions.RemoveEmptyEntries), x => Math.Max(1u, uint.Parse(x))); o.LabSeeds = o.LabSeedArray.Length; }
                    else o.LabSeeds = Math.Clamp(int.Parse(v), 1, 1000);
                    break;
                }
                case "--generations": o.LabGenerations = Math.Clamp(int.Parse(Next()), 2, 100000); break;
                case "--population": o.LabPopulation = Math.Clamp(int.Parse(Next()), 10, 5000); break;
                case "--every": o.LabEvery = Math.Clamp(int.Parse(Next()), 1, 100000); break;
                case "--out": o.LabOut = Next(); break;
                case "--no-serve": o.LabServe = false; break;
                case "-h": case "--help": case "/?": o.Help = true; break;
                default: throw new ArgumentException("unknown option " + s + " (try --help)");
            }
        }
        // every new world is different: its map, its ancestors' bodies and brains and their names all come from the
        // seed, so with no --seed it is drawn at random (a saved world or prehistory resumes with its own seed)
        if (o.BranchSlots == 0) o.BranchSlots = World.DefaultSlots(o.Branches);
        if (years > 0) o.PrehistorySteps = (int)Math.Clamp(Math.Round(years * o.SeasonLength), 1000, int.MaxValue);   // after --season-length
        while (o.Seed == 0) o.Seed = (uint)System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, int.MaxValue);
        if (o.Evolve)
        {
            string dir = AppContext.BaseDirectory;
            o.Headless = true;
            if (o.ReportEvery == 2000) o.ReportEvery = 10000;
            o.SaveBank ??= System.IO.Path.Combine(dir, "evolved.nrbank");
            o.Csv ??= System.IO.Path.Combine(dir, "evolve-report.csv");
            o.ReportTxt = System.IO.Path.Combine(dir, "evolve-report.txt");
            if (!o.NoCheckpoint) o.CheckpointPath ??= System.IO.Path.Combine(dir, "prehistory.nrck");
        }
        return o;
    }

    static float F(string v) => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
    /// <summary>The setup screen's body presets (kg).</summary>
    public static (float, float) BodyPreset(string v) => v.ToLowerInvariant() switch
    {
        "normal" => (K.MASS_LO, K.MASS_HI),
        "giants" => (100f, 20000f),
        "tiny" => (0.001f, 1f),
        _ => throw new ArgumentException("--bodies normal|giants|tiny"),
    };
    static int MapSide(string v)
    {
        int k = Array.IndexOf(Map.Names, v.ToLowerInvariant());
        return k >= 0 ? Map.Sizes[k] : int.Parse(v);
    }
}
