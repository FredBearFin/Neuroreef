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
    public bool Headless, Genesis, Strays, NoBrowser, PrintConstants, Help, Empty;
    public long Steps = 100000;
    public int ReportEvery = 2000;
    public string? LoadBank, SaveBank, Csv;
    public int GenesisSteps = K.GENESIS;
    public float Rate = 30;

    public const string Usage = @"Neuroreef 2 - an energy-cycle ecosystem of learning brains

  Neuroreef.exe [options]

  (no options)          start the simulation from the shipped gene bank and open the console in the browser
  --genesis             start from random brains (no gene bank), with genesis immigration
  --seed N              world and run seed (default 1)
  --capacity N          agent slots (default 32768)
  --threads N           worker threads (default: cores - 1, max 8)
  --port N              web console port (default 5800)
  --rate N              steps per second (default 30, 0 = as fast as possible)
  --no-browser          do not open the browser
  --load-bank FILE      start from this gene bank
  --save-bank FILE      save a gene bank at the end (headless) or every 50,000 steps (server)
  --strays              keep immigration on after genesis (counted as strays)
  --genesis-steps N     length of the genesis immigration period (default 300000)

  --headless            run without the console and print line-printer reports
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
                case "--genesis": o.Genesis = true; break;
                case "--strays": o.Strays = true; break;
                case "--no-browser": o.NoBrowser = true; break;
                case "--load-bank": o.LoadBank = Next(); break;
                case "--save-bank": o.SaveBank = Next(); break;
                case "--genesis-steps": o.GenesisSteps = int.Parse(Next()); break;
                case "--headless": o.Headless = true; break;
                case "--empty": o.Empty = true; break;
                case "--steps": o.Steps = long.Parse(Next()); break;
                case "--report": o.ReportEvery = int.Parse(Next()); break;
                case "--csv": o.Csv = Next(); break;
                case "--print-constants": o.PrintConstants = true; break;
                case "-h": case "--help": case "/?": o.Help = true; break;
                default: throw new ArgumentException("unknown option " + s + " (try --help)");
            }
        }
        return o;
    }
}
