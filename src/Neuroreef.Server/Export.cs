// Data export: everything the telemetry holds, as plain files a person (or a spreadsheet, or another program)
// can read, written into a folder next to the exe. The live world writes it with every autosave and on exit;
// the console's Export button writes it on demand and offers it as a zip download.
//
//   population.csv   step, one column per clade: organisms alive (juveniles and adults)
//   traits.csv       step, clade, then every mean trait (long format: one row per clade per sample)
//   deaths.csv       step, clade, births, then deaths by cause (per sample window)
//   telemetry.csv    every telemetry column, every sample (the raw data)
//   clades.csv       the clade table: name, parent, ancestor, born, extinct, alive, what it is now
//   report.txt       the observation report (what each clade ate, how it died, its life table)
// Samples are the long history (one in LONG_EVERY, the whole run) followed by the recent ring.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Neuroreef.Server;

public static class Export
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>All samples, oldest first: the long history up to the start of the recent ring, then the ring.</summary>
    static List<float[]> Samples(Telemetry T)
    {
        var list = new List<float[]>();
        float firstRecent = float.MaxValue;
        if (T.SampleCount > 0) { var a = new float[T.Len]; T.Sample(0, a); firstRecent = a[Telemetry.G_STEP]; }
        for (int k = 0; k < T.LongCount; k++)
        {
            var a = new float[T.Len]; T.LongSample(k, a);
            if (a[Telemetry.G_STEP] < firstRecent) list.Add(a);
        }
        for (int k = 0; k < T.SampleCount; k++) { var a = new float[T.Len]; T.Sample(k, a); list.Add(a); }
        return list;
    }

    static string F(float v) => v.ToString("G6", Inv);

    /// <summary>Write the export folder for one simulation (between steps). Returns the folder.</summary>
    public static string Write(Simulation sim, string dir)
    {
        var w = sim.W; var T = w.T;
        Directory.CreateDirectory(dir);
        var rows = Samples(T);
        var used = new List<int>();
        for (int s = 0; s < K.CLADES; s++) if (w.Sp[s].Used) used.Add(s);

        using (var f = new StreamWriter(Path.Combine(dir, "population.csv")))
        {
            var h = new StringBuilder("step");
            foreach (int s in used) h.Append(',').Append(w.Sp[s].Name);
            f.WriteLine(h);
            foreach (var a in rows)
            {
                var sb = new StringBuilder(F(a[Telemetry.G_STEP]));
                foreach (int s in used) sb.Append(',').Append(F(a[T.SpeciesOffset(s, Telemetry.S_COUNT)]));
                f.WriteLine(sb);
            }
        }
        using (var f = new StreamWriter(Path.Combine(dir, "traits.csv")))
        {
            var h = new StringBuilder("step,clade,alive");
            foreach (var t in Telemetry.TraitNames) h.Append(',').Append(t.Replace(',', ';'));
            f.WriteLine(h);
            foreach (var a in rows)
                foreach (int s in used)
                {
                    float n = a[T.SpeciesOffset(s, Telemetry.S_COUNT)];
                    if (n <= 0) continue;
                    var sb = new StringBuilder(F(a[Telemetry.G_STEP])).Append(',').Append(w.Sp[s].Name).Append(',').Append(F(n));
                    for (int k = 0; k < Telemetry.NTRAITS; k++) sb.Append(',').Append(F(a[T.SpeciesOffset(s, Telemetry.S_TRAITS + k)]));
                    f.WriteLine(sb);
                }
        }
        using (var f = new StreamWriter(Path.Combine(dir, "deaths.csv")))
        {
            var h = new StringBuilder("step,clade,alive,births");
            foreach (var c in Cause.Names) h.Append(',').Append(c);
            f.WriteLine(h);
            foreach (var a in rows)
                foreach (int s in used)
                {
                    float n = a[T.SpeciesOffset(s, Telemetry.S_COUNT)], b = a[T.SpeciesOffset(s, Telemetry.S_BIRTHS)];
                    float d = 0; for (int c = 0; c < Cause.N; c++) d += a[T.SpeciesOffset(s, Telemetry.S_DEATHS + c)];
                    if (n <= 0 && b <= 0 && d <= 0) continue;
                    var sb = new StringBuilder(F(a[Telemetry.G_STEP])).Append(',').Append(w.Sp[s].Name).Append(',').Append(F(n)).Append(',').Append(F(b));
                    for (int c = 0; c < Cause.N; c++) sb.Append(',').Append(F(a[T.SpeciesOffset(s, Telemetry.S_DEATHS + c)]));
                    f.WriteLine(sb);
                }
        }
        using (var f = new StreamWriter(Path.Combine(dir, "telemetry.csv")))
        {
            // clade columns are named by slot (cladeNN.); clades.csv maps slots to names
            f.WriteLine(string.Join(",", T.Names));
            var sb = new StringBuilder();
            foreach (var a in rows)
            {
                sb.Clear();
                for (int j = 0; j < a.Length; j++) { if (j > 0) sb.Append(','); sb.Append(F(a[j])); }
                f.WriteLine(sb);
            }
        }
        using (var f = new StreamWriter(Path.Combine(dir, "clades.csv")))
        {
            f.WriteLine("slot,name,parent,ancestor,born_step,extinct_step,generation,alive_now,now");
            var desc = Wire.Clades(w);
            foreach (int s in used)
            {
                var c = w.Sp[s];
                string now = (string)desc[s].GetType().GetProperty("niche")!.GetValue(desc[s])!;
                string parent = c.Parent >= 0 ? w.Sp[c.Parent].Name : "";
                f.WriteLine($"{s},{c.Name},{parent},{c.Ancestor},{c.Born},{(c.Alive ? "" : c.Extinct.ToString(Inv))},{c.Generation},{sim.ActiveCount[s]},{now}");
            }
        }
        File.WriteAllText(Path.Combine(dir, "report.txt"),
            $"NEUROREEF 2 at step {w.Step:N0} (seed {w.Seed}), exported {DateTime.Now:yyyy-MM-dd HH:mm}{Environment.NewLine}" + Headless.Summary(sim));
        return dir;
    }

    /// <summary>The export folder as a zip, in memory (for the console's download).</summary>
    public static byte[] Zip(string dir)
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var file in Directory.GetFiles(dir))
            {
                var e = z.CreateEntry(Path.GetFileName(file), CompressionLevel.Fastest);
                using var es = e.Open(); using var fs = File.OpenRead(file); fs.CopyTo(es);
            }
        return ms.ToArray();
    }
}
