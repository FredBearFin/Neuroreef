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

        // A prehistory (--evolve) always starts from random brains. A live world takes its first animals'
        // brains from a gene bank: --load-bank, then evolved.nrbank beside the exe, then the shipped bank.
        GeneBank.Data? bank = null;
        if (!o.Evolve && !o.RandomBrains)
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
        if (bank == null && !o.Evolve && !o.Empty)
        {
            Console.WriteLine("No prehistory yet: these are RANDOM BRAINS. Nothing will be spawned, so most species will die out.");
            Console.WriteLine("Run Evolve.bat first to evolve brains, then start Neuroreef.exe again.");
        }
        if (o.Headless) return Headless.Run(o, bank);
        return Host.Run(o, bank);
    }

    public static GeneBank.Data? ShippedBank()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("bank/default.nrbank");
        return s == null ? null : GeneBank.Load(s);
    }
}
