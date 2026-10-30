// Gene bank file (DESIGN §7.4, NR-212). Stores the living clades of a world and their evolved genomes, so
// that a later world can start from them.
//
// Format version 2 (little-endian):
//   "NRBK" | u16 version | u16 clade slots | u16 genome length | u32 capacity | u64 step | u32 seed
//   per clade slot: u8 present; if present: u8 name length, name (ASCII), u8 r, g, b, i16 parent, u8 ancestor,
//   u16 generation, u32 organisms alive, u16 count, count x (f32 score, genome bytes)

using System;
using System.Collections.Generic;
using System.IO;

namespace Neuroreef;

public static class GeneBank
{
    public const ushort Version = 11;   // 11: wiring-gain gene (Revision P)     // 10: inherited appearance genes and water-depth sense combined; 9: separate appearance/depth layouts; 8: breeding condition; 7: endothermy; 6: size floor; 5: body heat; 4: vocal learning; 3: int32 genome length

    public sealed class CladeInfo
    {
        public int Slot; public string Name = ""; public byte R, G, B; public int Parent = -1, Ancestor, Generation, Count;
    }

    public sealed class Data
    {
        public ulong Step; public uint Seed;
        public List<sbyte[]>[] Genomes = Array.Empty<List<sbyte[]>>();   // by clade slot
        public List<CladeInfo> Clades = new();
        public int Total { get { int n = 0; foreach (var l in Genomes) n += l.Count; return n; } }
    }

    public static void Save(World w, Stream s)
    {
        using var bw = new BinaryWriter(s, System.Text.Encoding.ASCII, leaveOpen: true);
        bw.Write("NRBK"u8.ToArray());
        bw.Write(Version); bw.Write((ushort)K.CLADES); bw.Write(G.LEN); bw.Write((uint)w.Cap);
        bw.Write(w.Step); bw.Write(w.Seed);
        var alive = new int[K.CLADES];
        for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] >= St.Juvenile) alive[w.Spc[i]]++;
        for (int sp = 0; sp < K.CLADES; sp++)
        {
            var c = w.Sp[sp];
            if (!c.Used || alive[sp] == 0) { bw.Write((byte)0); continue; }
            bw.Write((byte)1);
            var name = System.Text.Encoding.ASCII.GetBytes(c.Name);
            bw.Write((byte)Math.Min(255, name.Length)); bw.Write(name, 0, Math.Min(255, name.Length));
            bw.Write(c.R); bw.Write(c.G); bw.Write(c.B); bw.Write((short)c.Parent); bw.Write((byte)c.Ancestor);
            bw.Write((ushort)Math.Min(65535, c.Generation)); bw.Write((uint)alive[sp]);
            // living genomes first (they are this clade now), then the clade's archive of its best dead
            var living = new List<(float, int)>();
            for (int i = 0; i < w.Cap; i++)
                if (w.StageOf[i] >= St.Juvenile && w.Spc[i] == sp) living.Add((Life.Score(w, i), i));
            living.Sort((a, b) => b.Item1.CompareTo(a.Item1));
            int nl = Math.Min(K.BANK_LIVING, living.Count), na = w.ArchiveCount[sp];
            bw.Write((ushort)(nl + na));
            for (int k = 0; k < nl; k++)
            {
                bw.Write(living[k].Item1);
                bw.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<sbyte>(w.Genome, living[k].Item2 * G.LEN, G.LEN)));
            }
            for (int k = 0; k < na; k++)
            {
                bw.Write(w.ArchiveScore[sp * K.ARCHIVE + k]);
                bw.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<sbyte>(w.Archive, (sp * K.ARCHIVE + k) * G.LEN, G.LEN)));
            }
        }
    }

    public static Data Load(Stream s)
    {
        using var br = new BinaryReader(s, System.Text.Encoding.ASCII, leaveOpen: true);
        var magic = br.ReadBytes(4);
        if (magic.Length != 4 || magic[0] != 'N' || magic[1] != 'R' || magic[2] != 'B' || magic[3] != 'K') throw new InvalidDataException("not a gene bank");
        ushort ver = br.ReadUInt16();
        if (ver != Version)
            throw new InvalidDataException(ver < Version ? $"gene bank version {ver}: it was made with older rules (a different brain or sense layout); run a new prehistory" : $"gene bank version {ver} is from a newer program");
        ushort ns = br.ReadUInt16(); int len = br.ReadInt32();
        if (len != G.LEN) throw new InvalidDataException($"gene bank genome length {len}, expected {G.LEN} (genome layout changed)");
        if (ns != K.CLADES) throw new InvalidDataException($"gene bank has {ns} clade slots, expected {K.CLADES}");
        br.ReadUInt32();
        var d = new Data { Step = br.ReadUInt64(), Seed = br.ReadUInt32(), Genomes = new List<sbyte[]>[ns] };
        for (int sp = 0; sp < ns; sp++)
        {
            d.Genomes[sp] = new List<sbyte[]>();
            if (br.ReadByte() == 0) continue;
            var c = new CladeInfo { Slot = sp };
            int nl = br.ReadByte(); c.Name = System.Text.Encoding.ASCII.GetString(br.ReadBytes(nl));
            c.R = br.ReadByte(); c.G = br.ReadByte(); c.B = br.ReadByte(); c.Parent = br.ReadInt16(); c.Ancestor = br.ReadByte();
            c.Generation = br.ReadUInt16(); c.Count = (int)br.ReadUInt32();
            int n = br.ReadUInt16();
            for (int k = 0; k < n; k++)
            {
                br.ReadSingle();
                var g = new sbyte[G.LEN];
                br.BaseStream.ReadExactly(System.Runtime.InteropServices.MemoryMarshal.AsBytes(g.AsSpan()));
                d.Genomes[sp].Add(g);
            }
            d.Clades.Add(c);
        }
        return d;
    }

    public static void Save(World w, string path) { using var f = File.Create(path); Save(w, f); }
    public static Data Load(string path) { using var f = File.OpenRead(path); return Load(f); }
}
