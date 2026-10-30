// Checkpoint (DESIGN §7.6): the whole state of a prehistory, so a run of weeks survives a reboot or a crash
// and resumes exactly where it was. Every state field of each branch's World (with its clade table, clade
// scratch, telemetry and evolution settings) is written by reflection, so a field added later is saved
// without anyone remembering to. Arrays are copied into the existing arrays on load (same capacity and
// genome layout); a readonly scalar (capacity, seed) must match, or the checkpoint is refused.
//
// Format: "NRCK" | u16 version | i32 G.LEN | i32 branches | i32 capacity | u32 seed | per branch: object tree.
// A file is written to <path>.tmp and then renamed over <path>, so a crash during a save keeps the last one.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Neuroreef;

public static class Checkpoint
{
    public const ushort Version = 14;   // 14: geology (Revision R: plates, deep-time erosion, rock; World.Rock); 13: hydrology (Revision Q: flow, alluvium, floodplains); 12: path-integration senses and RETURN_FED (Revision P); 11: wiring-gain gene (Revision P); 10: inherited appearance genes and water-depth sense combined; 9: separate appearance/depth layouts; 8: breeding condition; 7: endothermy; 6: size floor; 5: body heat; 4: vocal learning; 3: ponds; 2: map and nature settings
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // classes whose state is saved by walking their fields; any other reference field is not state (links, buffers rebuilt each step)
    static readonly HashSet<Type> Walk = new() { typeof(World), typeof(Species), typeof(Clades.Scratch), typeof(Telemetry), typeof(EvoSettings) };
    static readonly Dictionary<Type, FieldInfo[]> _fields = new();

    static FieldInfo[] FieldsOf(Type t)
    {
        lock (_fields)
        {
            if (!_fields.TryGetValue(t, out var f))
            {
                // a link back to the world (Telemetry._w) is not state
                f = t.GetFields(Flags).Where(x => x.FieldType != typeof(World)).OrderBy(x => x.Name, StringComparer.Ordinal).ToArray();
                _fields[t] = f;
            }
            return f;
        }
    }

    public static void Save(Prehistory pre, string path)
    {
        string tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write("NRCK"u8.ToArray()); bw.Write(Version);
            bw.Write(G.LEN); bw.Write(pre.B.Length); bw.Write(pre.B[0].W.Cap); bw.Write(pre.B[0].W.Seed); Nature.Write(bw);
            foreach (var s in pre.B) Write(bw, s.W);
            fs.Flush(true);
        }
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Branches, slots per branch and seed of a prehistory checkpoint, to rebuild a prehistory it fits.</summary>
    public static (int branches, int cap, uint seed) PrehistoryHeader(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);
        var magic = br.ReadBytes(4);
        if (magic.Length != 4 || magic[0] != 'N' || magic[1] != 'R' || magic[2] != 'C' || magic[3] != 'K') throw new InvalidDataException("not a prehistory checkpoint");
        if (br.ReadUInt16() != Version) throw new InvalidDataException("checkpoint from another version of the program");
        int len = br.ReadInt32();
        if (len != G.LEN) throw new InvalidDataException($"checkpoint genome length {len}, this program uses {G.LEN} (the rules changed)");
        var h = (br.ReadInt32(), br.ReadInt32(), br.ReadUInt32());
        Nature.Read(br);   // the map and nature it was made with: applied, so the prehistory is rebuilt to fit
        return h;
    }

    /// <summary>Restore a prehistory built with the same branches, capacity and seed. Throws if the checkpoint does not fit.</summary>
    public static void Load(Prehistory pre, string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        using var br = new BinaryReader(fs);
        var magic = br.ReadBytes(4);
        if (magic.Length != 4 || magic[0] != 'N' || magic[1] != 'R' || magic[2] != 'C' || magic[3] != 'K') throw new InvalidDataException("not a prehistory checkpoint");
        if (br.ReadUInt16() != Version) throw new InvalidDataException("checkpoint from another version of the program");
        int len = br.ReadInt32(), n = br.ReadInt32(), cap = br.ReadInt32(); uint seed = br.ReadUInt32();
        int side = br.ReadInt32(); br.BaseStream.Seek(-4, SeekOrigin.Current);
        if (side != Map.W) throw new InvalidDataException($"checkpoint map is {side} units, this run's is {Map.W}");
        Nature.Read(br);
        if (len != G.LEN) throw new InvalidDataException($"checkpoint genome length {len}, this program uses {G.LEN} (the rules changed)");
        if (n != pre.B.Length || cap != pre.B[0].W.Cap || seed != pre.B[0].W.Seed)
            throw new InvalidDataException($"checkpoint has {n} branches, {cap} slots, seed {seed}; start with the same --branches and --seed");
        foreach (var s in pre.B) Read(br, s.W);
    }

    // ------------------------------------------------------------------ one world (the live world: save and continue)

    /// <summary>Save one world's whole state ("NRWK"). Call between steps.</summary>
    public static void SaveWorld(World w, string path)
    {
        string tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write("NRWK"u8.ToArray()); bw.Write(Version);
            bw.Write(G.LEN); bw.Write(w.Cap); bw.Write(w.Seed); bw.Write(w.TerrainSeed); Nature.Write(bw);
            Write(bw, w);
            fs.Flush(true);
        }
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Read a saved world's capacity and seeds, to build a world it fits into.</summary>
    public static (int cap, uint seed, uint terrainSeed) WorldHeader(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);
        var magic = br.ReadBytes(4);
        if (magic.Length != 4 || magic[0] != 'N' || magic[1] != 'R' || magic[2] != 'W' || magic[3] != 'K') throw new InvalidDataException("not a saved world");
        if (br.ReadUInt16() != Version) throw new InvalidDataException("saved world from another version of the program");
        int len = br.ReadInt32();
        if (len != G.LEN) throw new InvalidDataException($"saved world genome length {len}, this program uses {G.LEN} (the rules changed)");
        var h = (br.ReadInt32(), br.ReadUInt32(), br.ReadUInt32());
        Nature.Read(br);   // the map and nature it was made with: applied, so the world is rebuilt to fit
        return h;
    }

    public static void LoadWorld(World w, string path)
    {
        var (cap, seed, ts) = WorldHeader(path);
        if (cap != w.Cap || seed != w.Seed || ts != w.TerrainSeed) throw new InvalidDataException("saved world does not fit this world");
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        using var br = new BinaryReader(fs);
        br.ReadBytes(4); br.ReadUInt16(); br.ReadInt32(); br.ReadInt32(); br.ReadUInt32(); br.ReadUInt32();
        Nature.Read(br);
        Read(br, w);
    }

    // ------------------------------------------------------------------ object tree

    static void Write(BinaryWriter bw, object o)
    {
        foreach (var f in FieldsOf(o.GetType()))
        {
            var t = f.FieldType;
            object? v = f.GetValue(o);
            if (t.IsPrimitive) { bw.Write(f.Name); WriteScalar(bw, t, v!); }
            else if (t == typeof(string)) { bw.Write(f.Name); bw.Write((string?)v ?? ""); }
            else if (t.IsArray && t.GetElementType()!.IsPrimitive) { bw.Write(f.Name); WriteArray(bw, (Array?)v); }
            else if (t == typeof(float[][])) { bw.Write(f.Name); var a = (float[][])v!; bw.Write(a.Length); foreach (var x in a) WriteArray(bw, x); }
            else if (t.IsArray && Walk.Contains(t.GetElementType()!)) { bw.Write(f.Name); var a = (Array)v!; bw.Write(a.Length); foreach (var x in a) Write(bw, x!); }
            else if (Walk.Contains(t) && v != null) { bw.Write(f.Name); Write(bw, v); }
        }
        bw.Write("");   // end of object
    }

    static void Read(BinaryReader br, object o)
    {
        var byName = FieldsOf(o.GetType()).ToDictionary(f => f.Name);
        for (string name = br.ReadString(); name.Length > 0; name = br.ReadString())
        {
            if (!byName.TryGetValue(name, out var f)) throw new InvalidDataException($"checkpoint field {o.GetType().Name}.{name} is unknown to this program");
            var t = f.FieldType;
            if (t.IsPrimitive)
            {
                object v = ReadScalar(br, t);
                if (f.IsInitOnly) { if (!Equals(v, f.GetValue(o))) throw new InvalidDataException($"checkpoint {o.GetType().Name}.{name} = {v}, this run has {f.GetValue(o)}"); }
                else f.SetValue(o, v);
            }
            else if (t == typeof(string)) { string s = br.ReadString(); if (!f.IsInitOnly) f.SetValue(o, s); }
            else if (t.IsArray && t.GetElementType()!.IsPrimitive) ReadArray(br, (Array)f.GetValue(o)!, name);
            else if (t == typeof(float[][])) { var a = (float[][])f.GetValue(o)!; Expect(br.ReadInt32(), a.Length, name); foreach (var x in a) ReadArray(br, x, name); }
            else if (t.IsArray) { var a = (Array)f.GetValue(o)!; Expect(br.ReadInt32(), a.Length, name); foreach (var x in a) Read(br, x!); }
            else Read(br, f.GetValue(o)!);
        }
    }

    static void Expect(int got, int want, string name)
    {
        if (got != want) throw new InvalidDataException($"checkpoint {name} has {got} elements, this run has {want}");
    }

    static readonly byte[] _buf = new byte[1 << 24];

    static void WriteArray(BinaryWriter bw, Array? a)
    {
        if (a == null) { bw.Write(-1); return; }
        int bytes = Buffer.ByteLength(a);
        bw.Write(a.Length); bw.Write(bytes);
        lock (_buf)
            for (int off = 0; off < bytes; off += _buf.Length)
            {
                int n = Math.Min(_buf.Length, bytes - off);
                Buffer.BlockCopy(a, off, _buf, 0, n);
                bw.Write(_buf, 0, n);
            }
    }

    static void ReadArray(BinaryReader br, Array a, string name)
    {
        int len = br.ReadInt32();
        if (len < 0) return;
        Expect(len, a.Length, name);
        int bytes = br.ReadInt32();
        if (bytes != Buffer.ByteLength(a)) throw new InvalidDataException($"checkpoint {name}: element type changed");
        lock (_buf)
            for (int off = 0; off < bytes; off += _buf.Length)
            {
                int n = Math.Min(_buf.Length, bytes - off);
                int got = br.Read(_buf, 0, n);
                while (got < n) { int r = br.Read(_buf, got, n - got); if (r <= 0) throw new EndOfStreamException(); got += r; }
                Buffer.BlockCopy(_buf, 0, a, off, n);
            }
    }

    static void WriteScalar(BinaryWriter bw, Type t, object v)
    {
        switch (Type.GetTypeCode(t))
        {
            case TypeCode.Boolean: bw.Write((bool)v); break;
            case TypeCode.Byte: bw.Write((byte)v); break;
            case TypeCode.SByte: bw.Write((sbyte)v); break;
            case TypeCode.Int16: bw.Write((short)v); break;
            case TypeCode.UInt16: bw.Write((ushort)v); break;
            case TypeCode.Int32: bw.Write((int)v); break;
            case TypeCode.UInt32: bw.Write((uint)v); break;
            case TypeCode.Int64: bw.Write((long)v); break;
            case TypeCode.UInt64: bw.Write((ulong)v); break;
            case TypeCode.Single: bw.Write((float)v); break;
            case TypeCode.Double: bw.Write((double)v); break;
            default: throw new NotSupportedException("checkpoint: field type " + t);
        }
    }

    static object ReadScalar(BinaryReader br, Type t) => Type.GetTypeCode(t) switch
    {
        TypeCode.Boolean => br.ReadBoolean(),
        TypeCode.Byte => br.ReadByte(),
        TypeCode.SByte => br.ReadSByte(),
        TypeCode.Int16 => br.ReadInt16(),
        TypeCode.UInt16 => br.ReadUInt16(),
        TypeCode.Int32 => br.ReadInt32(),
        TypeCode.UInt32 => br.ReadUInt32(),
        TypeCode.Int64 => br.ReadInt64(),
        TypeCode.UInt64 => br.ReadUInt64(),
        TypeCode.Single => br.ReadSingle(),
        TypeCode.Double => br.ReadDouble(),
        _ => throw new NotSupportedException("checkpoint: field type " + t),
    };
}
