using System;
using System.IO;
using System.Reflection;

namespace Neuroreef.Server;

public static class Program
{
    public static int Main(string[] args)
    {
        Options o;
        try { o = Options.Parse(args); }
        catch (Exception e) { Console.Error.WriteLine(e.Message); return 2; }
        if (o.Help) { Console.WriteLine(Options.Usage); return 0; }
        if (o.PrintConstants) { Console.Write(ConstantsDoc.Generate()); return 0; }

        // A prehistory always starts from random brains. The world takes its first animals' brains from a
        // gene bank: --load-bank, then evolved.nrbank beside the exe, then the bank shipped with this build.
        // With none of these (or with --prehistory) the console runs a prehistory first.
        GeneBank.Data? bank = null;
        // a saved world (world.nrck beside the exe) is continued first, then a prehistory in progress
        string savedWorld = Path.Combine(AppContext.BaseDirectory, "world.nrck");
        if (!o.Evolve && !o.Headless && !o.NoCheckpoint && !o.NewWorld && !o.NewPrehistory && !o.RandomBrains && o.LoadBank == null && File.Exists(savedWorld))
        {
            Console.WriteLine("continuing your saved world (" + savedWorld + "); Neuroreef.exe --new-world starts a new one");
            o.ResumeWorld = true;
            return Host.Run(o, null);
        }
        if (o.NewWorld && File.Exists(savedWorld))
        {
            File.Move(savedWorld, savedWorld + ".old", overwrite: true);
            Console.WriteLine("a new world: the previous one was kept as " + savedWorld + ".old");
        }
        // a prehistory in progress (its checkpoint beside the exe) is resumed before anything else
        if (!o.Evolve && !o.Headless && !o.NoCheckpoint)
        {
            o.CheckpointPath ??= Path.Combine(AppContext.BaseDirectory, "prehistory.nrck");
            if (File.Exists(o.CheckpointPath) && o.LoadBank == null && !o.RandomBrains && !o.NewPrehistory)
            {
                Console.WriteLine("a prehistory is in progress: resuming it from " + o.CheckpointPath);
                o.NewPrehistory = true; o.ResumePrehistory = true;
                try { var (nb, cap, sd) = Checkpoint.PrehistoryHeader(o.CheckpointPath); o.Branches = nb; o.BranchSlots = cap; o.Seed = sd; }
                catch (Exception e) { Console.Error.WriteLine("cannot resume it: " + e.Message + " (move prehistory.nrck away to start a new one)"); return 2; }
            }
        }
        if (!o.Evolve && !o.RandomBrains && !o.NewPrehistory)
        {
            try
            {
                string evolved = Path.Combine(AppContext.BaseDirectory, "evolved.nrbank");
                if (o.LoadBank != null) { bank = GeneBank.Load(o.LoadBank); o.BankSource = "file"; Console.WriteLine("brains from gene bank: " + o.LoadBank); }
                else if (File.Exists(evolved)) { bank = GeneBank.Load(evolved); o.BankSource = "evolved"; Console.WriteLine("brains from your prehistory: " + evolved); }
                else { bank = ShippedBank(); if (bank != null) { o.BankSource = "shipped"; Console.WriteLine("brains from the gene bank shipped with this build"); } }
            }
            catch (Exception e) { Console.Error.WriteLine("gene bank not usable (" + e.Message + ")"); bank = null; }
        }
        if (bank == null && o.RandomBrains && !o.Empty)
        {
            Console.WriteLine("RANDOM BRAINS (no prehistory): nothing will be spawned, so most species will die out.");
        }
        if (o.Evolve)
        {
            // Evolve.bat / LongRun.bat: a NEW prehistory gets the setup page too (a resumed one keeps its sizes)
            bool resuming = o.CheckpointPath != null && File.Exists(o.CheckpointPath);
            if (!resuming && !o.NoSetup) Setup.Run(o);
            if (!resuming) o.ApplyNature(prehistory: true);   // a resumed one takes its map and nature from its checkpoint
            return Headless.Evolve(o);
        }
        if (o.Headless) { o.ApplyNature(prehistory: false); return Headless.Run(o, bank); }
        // a new prehistory: let the person choose its size first, so nothing is allocated that the machine cannot hold
        bool newPrehistory = bank == null && !o.RandomBrains && !o.ResumePrehistory;
        if (newPrehistory && !o.NoSetup && !o.SetupDone) Setup.Run(o);
        if (!o.ResumePrehistory) o.ApplyNature(prehistory: newPrehistory);   // a resumed one already took its own
        Console.WriteLine("nature: " + Nature.Describe());
        return Host.Run(o, bank);
    }

    public static GeneBank.Data? ShippedBank()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("bank/default.nrbank");
        return s == null ? null : GeneBank.Load(s);
    }
}
