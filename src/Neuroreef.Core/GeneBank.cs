// Gene bank file (DESIGN §7.4, NR-212). Stores evolved genomes per species so that a later run can
// start from evolved brains instead of random ones.
//
// Format (little-endian):  "NRBK" | u16 version | u16 species | u16 genome length | u32 capacity
//                          | u64 step | u32 seed | per species: u16 count, count x (f32 score, genome bytes)

using System;
using System.Collections.Generic;
using System.IO;

namespace Neuroreef;

public static class GeneBank
{
    public const ushort Version = 1;

    public sealed class Data
    {
        public ulong Step; public uint Seed;
        public List<sbyte[]>[] Genomes = Array.Empty<List<sbyte[]>>();
        public int Total { get { int n = 0; foreach (var l in Genomes) n += l.Count; return n; } }
    }

    public static void Save(World w, Stream s)
    {
        var src = new World[w.NS];
        for (int k = 0; k < src.Length; k++) src[k] = w;
        Write(src, s);
    }

    /// <summary>A bank whose species come from different branches: species s from branches[choice[s]] (prehistory).</summary>
    public static void SaveMerged(Simulation[] branches, int[] choice, Stream s)
    {
        var src = new World[Roster.Count];
        for (int k = 0; k < src.Length; k++) src[k] = branches[choice[k]].W;
        Write(src, s);
    }

    static void Write(World[] src, Stream s)
    {
        var w0 = src[0];
        using var bw = new BinaryWriter(s, System.Text.Encoding.ASCII, leaveOpen: true);
        bw.Write("NRBK"u8.ToArray());
        bw.Write(Version); bw.Write((ushort)w0.NS); bw.Write((ushort)G.LEN); bw.Write((uint)w0.Cap);
        bw.Write(w0.Step); bw.Write(w0.Seed);
        for (int sp = 0; sp < w0.NS; sp++)
        {
            var w = src[sp];
            var entries = new List<(float score, int src, bool archive)>();
            for (int k = 0; k < w.ArchiveCount[sp]; k++) entries.Add((w.ArchiveScore[sp * K.ARCHIVE + k], k, true));
            var living = new List<(float, int, bool)>();
            for (int i = 0; i < w.Cap; i++)
                if (w.StageOf[i] >= St.Juvenile && w.Spc[i] == sp)
                    living.Add((Life.Score(w, i), i, false));
            living.Sort((a, b) => b.Item1.CompareTo(a.Item1));
            for (int k = 0; k < Math.Min(K.BANK_LIVING, living.Count); k++) entries.Add(living[k]);
            bw.Write((ushort)entries.Count);
            foreach (var (score, idx, archive) in entries)
            {
                bw.Write(score);
                var g = archive ? new ReadOnlySpan<sbyte>(w.Archive, (sp * K.ARCHIVE + idx) * G.LEN, G.LEN)
                                : new ReadOnlySpan<sbyte>(w.Genome, idx * G.LEN, G.LEN);
                foreach (var b in g) bw.Write(b);
            }
        }
    }

    public static Data Load(Stream s)
    {
        using var br = new BinaryReader(s, System.Text.Encoding.ASCII, leaveOpen: true);
        var magic = br.ReadBytes(4);
        if (magic.Length != 4 || magic[0] != 'N' || magic[1] != 'R' || magic[2] != 'B' || magic[3] != 'K') throw new InvalidDataException("not a gene bank");
        ushort ver = br.ReadUInt16(), ns = br.ReadUInt16(), len = br.ReadUInt16();
        if (ver != Version) throw new InvalidDataException($"gene bank version {ver}, expected {Version}");
        // a Revision B bank (no care or reflex genes) loads with those genes at 0: the species' care level and no reflexes
        if (len != G.LEN && len != G.LEN_B) throw new InvalidDataException($"gene bank genome length {len}, expected {G.LEN} (brain layout changed)");
        if (ns != Roster.Count) throw new InvalidDataException($"gene bank has {ns} species, expected {Roster.Count}");
        br.ReadUInt32();
        var d = new Data { Step = br.ReadUInt64(), Seed = br.ReadUInt32(), Genomes = new List<sbyte[]>[ns] };
        for (int sp = 0; sp < ns; sp++)
        {
            int n = br.ReadUInt16();
            var l = new List<sbyte[]>(n);
            for (int k = 0; k < n; k++)
            {
                br.ReadSingle();
                var g = new sbyte[G.LEN];
                for (int b = 0; b < len; b++) g[b] = br.ReadSByte();
                l.Add(g);
            }
            d.Genomes[sp] = l;
        }
        return d;
    }

    public static void Save(World w, string path) { using var f = File.Create(path); Save(w, f); }
    public static Data Load(string path) { using var f = File.OpenRead(path); return Load(f); }
}
