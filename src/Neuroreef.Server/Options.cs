using System;
using System.Collections.Generic;

namespace Neuroreef.Server;

/// <summary>Command-line options. Every option is listed in Usage.</summary>
public sealed class Options
{
    public uint Seed = 1;
    public int Capacity = K.CAPACITY;
    public int Threads = Math.Max(1, Math.Min(Environment.ProcessorCount - 1, 8));
    public int Port = 5800;
    public bool Headless, RandomBrains, NoBrowser, PrintConstants, Help, Empty, Evolve, StepsSet, NewPrehistory;
    public string? ReportTxt;
    public long Steps = 100000;
    public int ReportEvery = 2000;
    public string? LoadBank, SaveBank, Csv, CheckpointPath;
    public double CheckpointMinutes = 30;
    public int BigBrains = -1, BigBrainUnits = 0, Founders = 0, BranchSlots = K.BRANCH_CAPACITY;
    // the map of a prehistory and of the world (a prehistory evolves in a region; the world you watch is a valley)
    public int PreMap = 512, WorldMap = 256;
    public int AncestorsLand = K.ANCESTORS_LAND, AncestorsWater = K.ANCESTORS_WATER;
    public float MatingDrive = 1f, MutBrainX = 1f, MutBodyX = 1f;
    // nature (Nature.cs): multipliers on the laws, 1 = as documented
    public float Plants = 1f, Seasons = 1f, Drought = 1f, Fire = 1f, Disease = 1f, Thirst = 1f, Water = K.LAKE_SHARE, Ponds = 1f;
    public int SeasonLength = K.T_SEASON;

    /// <summary>Apply the nature settings and the map for what is about to be built (not when resuming a save).</summary>
    public void ApplyNature(bool prehistory)
    {
        Nature.Plants = Plants; Nature.Seasons = Seasons; Nature.SeasonLength = SeasonLength; Nature.Drought = Drought;
        Nature.Fire = Fire; Nature.Disease = Disease; Nature.Thirst = Thirst; Nature.Water = Water; Nature.Ponds = Ponds; Nature.WorldMap = WorldMap;
        Nature.Clamp();
        Map.Set(prehistory ? PreMap : WorldMap);
    }
    public bool NoSetup, SetupDone;
    public bool NoCheckpoint, ResumePrehistory, ResumeWorld, NewWorld;
    public int PrehistorySteps = K.PREHISTORY_STEPS, Branches = K.BRANCHES;

    /// <summary>The prehistory's starting evolution settings from the command line.</summary>
    public EvoSettings PrehistorySettings(bool stepsAreLength)
    {
        long n = stepsAreLength && StepsSet ? Steps : PrehistorySteps;
        var e = new EvoSettings { TotalSteps = n <= 0 || n > int.MaxValue ? int.MaxValue : (int)n };   // 0 = until stopped
        if (BigBrains >= 0) e.BigBrains = BigBrains;
        if (BigBrainUnits > 0) e.BigBrainUnits = BigBrainUnits;
        if (Founders > 0) e.Founders = Founders;
        e.AncestorsLand = AncestorsLand; e.AncestorsWater = AncestorsWater; e.MatingDrive = MatingDrive;
        e.MutRate *= MutBrainX; e.MutBody *= MutBodyX;
        return e;
    }
    public string BankSource = "random";      // evolved, shipped, file, or random
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
  --branches N          prehistory branches (default 3)
  --prehistory-steps N  length of the prehistory (default 500000 steps = 500 million symbolic years)
  --seed N              world and run seed (default 1)
  --capacity N          agent slots of the world (default 65536)
  --threads N           worker threads (default: cores - 1, max 8)
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
  --big-brains N        prehistory: how many of the 10 ancestors in each branch start big-brained (default 2:
                        one on land, one in water; 0 = none)
  --big-brain-units N   prehistory: memory units of a big-brained ancestor (default 36; others start with up to 12)
  --founders N          prehistory: founders per ancestor (default 600)
  --branch-slots N      prehistory: animal slots per branch (default 49152; memory grows with it)
  --no-setup            skip the setup screen before a new prehistory (use the defaults or these options)
  --map valley|region|continent        map of the prehistory (default region)
  --world-map valley|region|continent  map of the world you watch (default valley)
  --ancestors-land N, --ancestors-water N   random ancestors (default 6 and 4; at most 16 in all)
  --mating-drive X      strength of the breeding instinct (default 1)
  Nature (multipliers, 1 = as documented): --plants X  --seasons X  --drought X  --fire X  --disease X  --thirst X
  --season-length N     steps per year (default 3000)   --water X   share of the map under water (default 0.10)
  --ponds X             share of land basins with seasonal ponds (default 1; 0 = water only in lakes and rivers)
  --headless            run the world without the console and print reports
  --steps N             headless: number of steps (default 100000)
  --report N            headless: report interval in steps (default 2000)
  --csv FILE            headless: write the telemetry ring as CSV at the end
  --empty               headless: plants only, no animals (checks the vegetation laws on their own)
  --print-constants     print docs/CONSTANTS.md and exit
";

    public static Options Parse(string[] a)
    {
        var o = new Options();
        for (int i = 0; i < a.Length; i++)
        {
            string s = a[i];
            string Next() => i + 1 < a.Length ? a[++i] : throw new ArgumentException("missing value after " + s);
            switch (s)
            {
                case "--seed": o.Seed = uint.Parse(Next()); break;
                case "--capacity": o.Capacity = int.Parse(Next()); break;
                case "--threads": o.Threads = int.Parse(Next()); break;
                case "--port": o.Port = int.Parse(Next()); break;
                case "--rate": o.Rate = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--random-brains": o.RandomBrains = true; break;
                case "--prehistory": o.NewPrehistory = true; break;
                case "--no-browser": o.NoBrowser = true; break;
                case "--load-bank": o.LoadBank = Next(); break;
                case "--save-bank": o.SaveBank = Next(); break;
                case "--prehistory-steps": o.PrehistorySteps = int.Parse(Next()); break;
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
                case "--branch-slots": o.BranchSlots = Math.Clamp(int.Parse(Next()), 2048, 262144); o.NoSetup = true; break;
                case "--no-setup": o.NoSetup = true; break;
                case "--map": o.PreMap = MapSide(Next()); o.NoSetup = true; break;
                case "--world-map": o.WorldMap = MapSide(Next()); break;
                case "--ancestors-land": o.AncestorsLand = int.Parse(Next()); o.NoSetup = true; break;
                case "--ancestors-water": o.AncestorsWater = int.Parse(Next()); o.NoSetup = true; break;
                case "--mating-drive": o.MatingDrive = F(Next()); break;
                case "--plants": o.Plants = F(Next()); break;
                case "--seasons": o.Seasons = F(Next()); break;
                case "--season-length": o.SeasonLength = int.Parse(Next()); break;
                case "--drought": o.Drought = F(Next()); break;
                case "--fire": o.Fire = F(Next()); break;
                case "--disease": o.Disease = F(Next()); break;
                case "--thirst": o.Thirst = F(Next()); break;
                case "--water": o.Water = F(Next()); break;
                case "--ponds": o.Ponds = F(Next()); break;
                case "--big-brain-units": o.BigBrainUnits = int.Parse(Next()); o.NoSetup = true; break;
                case "--print-constants": o.PrintConstants = true; break;
                case "-h": case "--help": case "/?": o.Help = true; break;
                default: throw new ArgumentException("unknown option " + s + " (try --help)");
            }
        }
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
    static int MapSide(string v)
    {
        int k = Array.IndexOf(Map.Names, v.ToLowerInvariant());
        return k >= 0 ? Map.Sizes[k] : int.Parse(v);
    }
}
