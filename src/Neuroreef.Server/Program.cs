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
        if (o.Evolve) return Headless.Evolve(o);
        if (o.Headless) return Headless.Run(o, bank);
        return Host.Run(o, bank);
    }

    public static GeneBank.Data? ShippedBank()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("bank/default.nrbank");
        return s == null ? null : GeneBank.Load(s);
    }
}
