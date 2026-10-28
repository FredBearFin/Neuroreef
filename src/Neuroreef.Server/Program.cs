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

        GeneBank.Data? bank = null;
        if (!o.Genesis)
        {
            try
            {
                string evolved = Path.Combine(AppContext.BaseDirectory, "evolved.nrbank");
                if (o.LoadBank != null) { bank = GeneBank.Load(o.LoadBank); Console.WriteLine("gene bank: " + o.LoadBank); }
                else if (File.Exists(evolved)) { bank = GeneBank.Load(evolved); Console.WriteLine("gene bank: " + evolved + " (your evolved brains)"); }
                else { bank = ShippedBank(); if (bank != null) Console.WriteLine("gene bank: shipped with this build"); }
            }
            catch (Exception e) { Console.Error.WriteLine("gene bank not usable (" + e.Message + "); starting a genesis run"); bank = null; }
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
