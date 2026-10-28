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
    public bool Headless, RandomBrains, NoBrowser, PrintConstants, Help, Empty, Evolve, StepsSet;
    public string? ReportTxt;
    public long Steps = 100000;
    public int ReportEvery = 2000;
    public string? LoadBank, SaveBank, Csv;
    public int PrehistorySteps = K.PREHISTORY;
    public string BankSource = "random";      // evolved, shipped, file, or random
    public float Rate = 30;

    public const string Usage = @"Neuroreef 2 - an energy-cycle ecosystem of learning brains

  Neuroreef.exe [options]

  The world you watch is births and deaths only: no animal ever appears except by being born.
  The brains of its first animals come from a PREHISTORY run (Evolve.bat), which stands for the long
  evolution before the world begins; only the prehistory uses colonists.

  (no options)          open the world in the browser. Its first animals get their brains from evolved.nrbank
                        next to the exe if it exists, else from the gene bank shipped with this build, else
                        random brains (then most species will die out: run Evolve.bat first)
  --evolve              run the PREHISTORY: headless, full speed, 400,000 steps (about an hour on a desktop);
                        colonists keep species going while brains evolve, until step 300,000. Saves
                        evolved.nrbank and evolve-report.txt/.csv next to the exe
  --random-brains       open the world with random brains even if evolved.nrbank exists (no spawning)
  --seed N              world and run seed (default 1)
  --capacity N          agent slots (default 65536)
  --threads N           worker threads (default: cores - 1, max 8)
  --port N              web console port (default 5800)
  --rate N              steps per second (default 30, 0 = as fast as possible)
  --no-browser          do not open the browser
  --load-bank FILE      take the first animals' brains from this gene bank
  --save-bank FILE      save a gene bank at the end (headless) or every 50,000 steps (console)
  --prehistory-steps N  with --evolve: length of the colonist period (default 300000)

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
                case "--no-browser": o.NoBrowser = true; break;
                case "--load-bank": o.LoadBank = Next(); break;
                case "--save-bank": o.SaveBank = Next(); break;
                case "--prehistory-steps": o.PrehistorySteps = int.Parse(Next()); break;
                case "--headless": o.Headless = true; break;
                case "--empty": o.Empty = true; break;
                case "--steps": o.Steps = long.Parse(Next()); o.StepsSet = true; break;
                case "--evolve": o.Evolve = true; break;
                case "--report": o.ReportEvery = int.Parse(Next()); break;
                case "--csv": o.Csv = Next(); break;
                case "--print-constants": o.PrintConstants = true; break;
                case "-h": case "--help": case "/?": o.Help = true; break;
                default: throw new ArgumentException("unknown option " + s + " (try --help)");
            }
        }
        if (o.Evolve)
        {
            string dir = AppContext.BaseDirectory;
            o.Headless = true;
            if (!o.StepsSet) o.Steps = 400000;
            if (o.ReportEvery == 2000) o.ReportEvery = 10000;
            o.SaveBank ??= System.IO.Path.Combine(dir, "evolved.nrbank");
            o.Csv ??= System.IO.Path.Combine(dir, "evolve-report.csv");
            o.ReportTxt = System.IO.Path.Combine(dir, "evolve-report.txt");
        }
        return o;
    }
}
