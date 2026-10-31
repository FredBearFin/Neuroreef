// Saves that survive an update of the program (Checkpoint migration), and a startup that builds each land once (Land):
// an older save keeps its animals, genomes, clades and step and rebuilds what no longer fits; a world built from a land
// made for it is exactly the world that builds its own.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Neuroreef.Tests;

public class CheckpointTests
{
    static byte[] Hash(string p) { using var f = File.OpenRead(p); return System.Security.Cryptography.SHA256.HashData(f); }

    static byte[] State(World w)
    {
        string p = Path.GetTempFileName();
        try { Checkpoint.SaveWorld(w, p); return Hash(p); } finally { File.Delete(p); }
    }

    // ---- an "old" save: the format before 18 (no kind byte, every slot written), as the program of that version wrote it

    static readonly HashSet<Type> Walk = new() { typeof(World), typeof(Species), typeof(Clades.Scratch), typeof(Telemetry), typeof(EvoSettings), typeof(PlantTable) };

    static void OldWrite(BinaryWriter bw, object o, Func<string, bool> drop, string path = "")
    {
        const BindingFlags fl = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var f in o.GetType().GetFields(fl).Where(x => x.FieldType != typeof(World) && !x.IsNotSerialized).OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            if (drop(path + f.Name)) continue;
            var t = f.FieldType; object? v = f.GetValue(o);
            if (t.IsPrimitive)
            {
                bw.Write(f.Name);
                switch (v)
                {
                    case bool b: bw.Write(b); break; case byte b: bw.Write(b); break; case sbyte b: bw.Write(b); break;
                    case short b: bw.Write(b); break; case ushort b: bw.Write(b); break; case int b: bw.Write(b); break;
                    case uint b: bw.Write(b); break; case long b: bw.Write(b); break; case ulong b: bw.Write(b); break;
                    case float b: bw.Write(b); break; case double b: bw.Write(b); break;
                    default: throw new NotSupportedException(t.Name);
                }
            }
            else if (t == typeof(string)) { bw.Write(f.Name); bw.Write((string?)v ?? ""); }
            else if (t.IsArray && t.GetElementType()!.IsPrimitive) { bw.Write(f.Name); OldArray(bw, (Array?)v); }
            else if (v is ISlotArray sa)
            {
                // the genomes, weights and traces were one array each: written whole
                bw.Write(f.Name);
                long bytes = (long)sa.Slots * sa.Stride * sa.ElementSize;
                bw.Write(sa.Slots * sa.Stride); bw.Write(checked((int)bytes));
                for (int s = 0; s < sa.Slots; s++) bw.Write(sa.Bytes(s, 1));
            }
            else if (t == typeof(float[][])) { bw.Write(f.Name); var a = (float[][])v!; bw.Write(a.Length); foreach (var x in a) OldArray(bw, x); }
            else if (t.IsArray && Walk.Contains(t.GetElementType()!)) { bw.Write(f.Name); var a = (Array)v!; bw.Write(a.Length); foreach (var x in a) OldWrite(bw, x!, drop, path + f.Name + "[]."); }
            else if (Walk.Contains(t) && v != null) { bw.Write(f.Name); OldWrite(bw, v, drop, path + f.Name + "."); }
        }
        if (o is World w && path == "")
        {
            // fields of the old program that this one no longer has (the disease of Revision I: Infect, Immune)
            bw.Write("Infect"); OldArray(bw, new float[w.Cap]);
            bw.Write("Immune"); OldArray(bw, new float[w.Cap]);
        }
        bw.Write("");
    }

    static void OldArray(BinaryWriter bw, Array? a)
    {
        if (a == null) { bw.Write(-1); return; }
        var bytes = new byte[Buffer.ByteLength(a)];
        Buffer.BlockCopy(a, 0, bytes, 0, bytes.Length);
        bw.Write(a.Length); bw.Write(bytes.Length); bw.Write(bytes);
    }

    static void OldSave(World w, string path, ushort version, Func<string, bool> drop)
    {
        using var bw = new BinaryWriter(File.Create(path));
        bw.Write("NRWK"u8.ToArray()); bw.Write(version);
        bw.Write(G.LEN); bw.Write(w.Cap); bw.Write(w.Seed); bw.Write(w.TerrainSeed); Nature.Write(bw);
        OldWrite(bw, w, drop);
    }

    static bool PlantField(string p) =>
        p is "Flora" or "POcc" or "BankMom" or "BankDad" or "SeedMom" or "SeedDad" or "FruitG" or "PollenDad" or "BankM" or "SeedOut" or "PFlags"
        or "WindAcc" or "WindDist" or "RowFar" or "RowFarBest" or "WindAccMom" or "WindAccDad" or "WindDistMom" or "WindDistDad" or "RowFarMom" or "RowFarDad"
        or "GutSeedG" or "GutSeedDad" or "FurSeedG" or "FurSeedDad" or "PollenG" or "FurSeed";

    [Fact] // a save of format 16 (before the evolving plants): the animals, genomes, clades and step survive; the land is rebuilt
    public void OldSaveKeepsItsAnimalsAndRebuildsTheLand()
    {
        using var a = new Simulation(8192, 93, 2);
        for (int k = 0; k < 300; k++) a.Step();
        var w = a.W;
        string path = Path.GetTempFileName();
        try
        {
            // format 16 had no plant genotypes, and its animals no immunity trait (a phenotype this save does not hold)
            OldSave(w, path, 16, p => PlantField(p) || p == "PImmune");
            var (cap, seed, ts) = Checkpoint.WorldHeader(path, out int version);
            Assert.Equal(16, version);
            using var b = new Simulation(cap, seed, 2, restore: true, terrainSeed: ts);
            var m = Checkpoint.LoadWorld(b, path);
            var r = b.W;

            Assert.True(m.Migrated);
            Assert.True(m.LandRebuilt);
            Assert.True(m.Reexpressed);
            Assert.Contains("Infect", m.Dropped); Assert.Contains("Immune", m.Dropped);
            Assert.Contains("PImmune", m.Missing);
            Assert.Contains("Genome", m.Kept); Assert.Contains("Sp[].Name", m.Kept); Assert.Contains("Step", m.Kept);
            string said = m.ToString();
            Assert.Contains("kept", said); Assert.Contains("rebuilt from terrain seed", said); Assert.Contains("Infect", said);

            // the animals, their genomes and brains, the clades and the clock are the save's
            Assert.Equal(w.Step, r.Step);
            Assert.Equal(w.AliveCount, r.AliveCount);
            int alive = 0;
            for (int i = 0; i < w.Cap; i++)
            {
                Assert.Equal(w.StageOf[i], r.StageOf[i]);
                if (w.StageOf[i] == St.Dead) continue;
                alive++;
                Assert.Equal(w.Serial[i], r.Serial[i]); Assert.Equal(w.Spc[i], r.Spc[i]); Assert.Equal(w.Energy[i], r.Energy[i]);
                Assert.True(w.GenomeOf(i).SequenceEqual(r.GenomeOf(i)));
                Assert.True(w.W2f.Slot(i).SequenceEqual(r.W2f.Slot(i)));
                Assert.Equal(w.PImmune[i], r.PImmune[i]);   // expressed again from the genes: the trait the save did not hold
            }
            Assert.True(alive > 100, $"only {alive} animals in the save");
            for (int s = 0; s < K.CLADES; s++) { Assert.Equal(w.Sp[s].Name, r.Sp[s].Name); Assert.Equal(w.Sp[s].Used, r.Sp[s].Used); }
            Assert.True(r.T.Births.SequenceEqual(w.T.Births));

            // the land is the one a new world of this seed has: the same relief, and plants grown from the spin-up
            using var fresh = new Simulation(8192, 93, 2, empty: true);
            Assert.True(fresh.W.Elev.AsSpan().SequenceEqual(r.Elev));
            Assert.True(fresh.W.POcc.AsSpan().SequenceEqual(r.POcc));
            Assert.True(r.Flora.Live > 0);
            Assert.Equal(0, r.GutSeedG.Count(g => g != 0));

            // and the world goes on
            for (int k = 0; k < 100; k++) b.Step();
            Assert.True(r.AliveCount > 0);
            Assert.Equal(w.Step + 100, r.Step);
        }
        finally { File.Delete(path); }
    }

    [Fact] // a save of format 17 (the same land rules, the format before the kind byte): everything is kept, and it goes on exactly
    public void Format17SaveContinuesExactly()
    {
        using var a = new Simulation(8192, 94, 2);
        for (int k = 0; k < 200; k++) a.Step();
        string path = Path.GetTempFileName();
        try
        {
            OldSave(a.W, path, 17, p => p is "Flora.Cap" or "Flora.HCap");   // the first saves of format 17 had a fixed plant table size (a constant)
            var (cap, seed, ts) = Checkpoint.WorldHeader(path);
            using var b = new Simulation(cap, seed, 2, restore: true, terrainSeed: ts);
            var m = Checkpoint.LoadWorld(b, path);
            Assert.True(m.Migrated);
            Assert.False(m.LandRebuilt);
            Assert.Empty(m.Missing); Assert.Empty(m.Changed);
            Assert.Equal(new[] { "Immune", "Infect" }, m.Dropped);
            for (int k = 0; k < 300; k++) { a.Step(); b.Step(); }
            Assert.True(State(a.W).AsSpan().SequenceEqual(State(b.W)), "the migrated world differs from the one that never stopped");
        }
        finally { File.Delete(path); }
    }

    [Fact] // a saved world holds the slots in use only for its big per-slot arrays (genomes, brains), and round-trips exactly
    public void SaveSkipsDeadSlotsAndRoundTrips()
    {
        using var a = new Simulation(8192, 95, 2);
        for (int k = 0; k < 100; k++) a.Step();
        string path = Path.GetTempFileName();
        try
        {
            Checkpoint.SaveWorld(a.W, path);
            int alive = 0; for (int i = 0; i < a.W.Cap; i++) if (a.W.StageOf[i] != St.Dead) alive++;
            long perSlot = G.LEN + 2L * G.W2N * 4;   // genome, weights, traces
            long size = new FileInfo(path).Length;
            Assert.True(size < (alive + 0.25 * a.W.Cap) * perSlot, $"{size:N0} bytes for {alive} animals in {a.W.Cap} slots");
            var (cap, seed, ts) = Checkpoint.WorldHeader(path);
            using var b = new Simulation(cap, seed, 2, restore: true, terrainSeed: ts);
            Assert.False(Checkpoint.LoadWorld(b, path).Migrated);
            Assert.True(State(a.W).AsSpan().SequenceEqual(State(b.W)));
            for (int k = 0; k < 300; k++) { a.Step(); b.Step(); }
            Assert.True(State(a.W).AsSpan().SequenceEqual(State(b.W)), "continued world differs");
        }
        finally { File.Delete(path); }
    }

    [Fact] // a world that grew is saved at its size and continues exactly, loaded into a world built at any size
    public void GrownWorldRoundTrips()
    {
        using var a = new Simulation(1024, 96, 2);
        for (int k = 0; k < 150; k++) a.Step();
        Assert.True(a.W.Cap > 1024, $"the world never grew ({a.W.Cap} slots)");
        string path = Path.GetTempFileName();
        try
        {
            Checkpoint.SaveWorld(a.W, path);
            var (cap, seed, ts) = Checkpoint.WorldHeader(path);
            Assert.Equal(a.W.Cap, cap);
            foreach (int built in new[] { 1024, cap + 2048 })   // smaller and bigger than the save: it is given the save's size
            {
                using var b = new Simulation(built, seed, 1, restore: true, terrainSeed: ts);
                Assert.False(Checkpoint.LoadWorld(b, path).Migrated);
                Assert.Equal(cap, b.W.Cap);
                Assert.True(State(a.W).AsSpan().SequenceEqual(State(b.W)), $"the loaded world (built with {built} slots) differs");
                Assert.Equal(EngineeringTests.RunHash(a.W), EngineeringTests.RunHash(b.W));
            }
            using var c = new Simulation(1024, seed, 1, restore: true, terrainSeed: ts);
            Checkpoint.LoadWorld(c, path);
            for (int k = 0; k < 250; k++) { a.Step(); c.Step(); }
            Assert.Equal(EngineeringTests.RunHash(a.W), EngineeringTests.RunHash(c.W));
            if (a.W.Cap == c.W.Cap) Assert.True(State(a.W).AsSpan().SequenceEqual(State(c.W)), "the continued world differs");
        }
        finally { File.Delete(path); }
    }

    [Fact] // the branches of a prehistory grow each on its own; a checkpoint keeps each branch's size and resumes exactly
    public void GrownPrehistoryResumes()
    {
        string path = Path.GetTempFileName();
        try
        {
            using var a = new Prehistory(2, 1024, 87, 2);
            for (int k = 0; k < 60; k++) foreach (var s in a.B) s.Step();
            Assert.True(a.B[0].W.Cap > 1024 || a.B[1].W.Cap > 1024, "no branch grew");
            Checkpoint.Save(a, path);
            var (n, cap, seed) = Checkpoint.PrehistoryHeader(path);
            Assert.Equal(Math.Max(a.B[0].W.Cap, a.B[1].W.Cap), cap);
            using var c = new Prehistory(n, 2048, seed, 2, restore: true);
            c.LoadCheckpoint(path);
            for (int b = 0; b < 2; b++)
            {
                Assert.Equal(a.B[b].W.Cap, c.B[b].W.Cap);
                Assert.True(State(a.B[b].W).AsSpan().SequenceEqual(State(c.B[b].W)), $"branch {b} differs");
            }
            for (int k = 0; k < 60; k++) for (int b = 0; b < 2; b++) { a.B[b].Step(); c.B[b].Step(); }
            for (int b = 0; b < 2; b++) Assert.Equal(EngineeringTests.RunHash(a.B[b].W), EngineeringTests.RunHash(c.B[b].W));
        }
        finally { File.Delete(path); }
    }

    // ---- arrays over 2 GB (W2f, Trace at ~39,000 slots or more): bytes copied through spans at long offsets, a chunk at a time

    [Fact] // a range of an array at a byte offset, in chunks of any size, round-trips; nothing outside it is touched
    public void ChunkedBytesRoundTrip()
    {
        var a = new float[1000];
        for (int i = 0; i < a.Length; i++) a[i] = i * 1.5f - 7;
        foreach (var (wc, rc) in new[] { (7, 5), (4096, 1), (Checkpoint.Chunk, 3001) })
        {
            var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) Checkpoint.WriteBytes(bw, a, 12, 3001, wc);
            Assert.Equal(3001, ms.Length);
            ms.Position = 0;
            var b = new float[1000];
            using (var br = new BinaryReader(ms)) Checkpoint.ReadBytes(br, b, 12, 3001, rc);
            var ab = System.Runtime.InteropServices.MemoryMarshal.AsBytes(a.AsSpan());
            var bb = System.Runtime.InteropServices.MemoryMarshal.AsBytes(b.AsSpan());
            Assert.True(ab.Slice(12, 3001).SequenceEqual(bb.Slice(12, 3001)));
            Assert.True(bb[..12].IndexOfAnyExcept((byte)0) < 0 && bb[3013..].IndexOfAnyExcept((byte)0) < 0);
        }
    }

    [Fact] // the byte view of an array is bounded by its length in bytes as a long: an offset past 2 GB is refused, never wrapped
    public void BytesAreBoundedAtLongOffsets()
    {
        var a = new double[100];
        Assert.Equal(800L, Checkpoint.ByteLength(a));
        a[99] = 3.25;
        Assert.Equal(3.25, BitConverter.ToDouble(Checkpoint.Bytes(a, 792, 8)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Checkpoint.Bytes(a, 793, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => Checkpoint.Bytes(a, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Checkpoint.Bytes(a, (1L << 32) + 8, 8));   // (int) of it would be 8
        Assert.Throws<ArgumentOutOfRangeException>(() => Checkpoint.Bytes(a, long.MaxValue - 4, 8));
    }

    [Fact] // a whole array gives its byte count as i64 from format 19 on; a save of format 18 (i32) still reads
    public void WholeArrayByteCountFormats()
    {
        var a = new int[] { 1, -2, 3, int.MaxValue };
        var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) Checkpoint.WriteArray(bw, a);
        Assert.Equal(1 + 4 + 8 + 16, ms.Length);
        ms.Position = 0;
        var b = new int[4];
        using (var br = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true)) Assert.True(Checkpoint.ReadArray(br, Checkpoint.Version, b));
        Assert.Equal(a, b);

        var old = new MemoryStream();
        using (var bw = new BinaryWriter(old, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            bw.Write((byte)TypeCode.Int32); bw.Write(a.Length); bw.Write(16);
            foreach (int x in a) bw.Write(x);
        }
        old.Position = 0;
        var c = new int[4];
        using (var br = new BinaryReader(old)) Assert.True(Checkpoint.ReadArray(br, 18, c));
        Assert.Equal(a, c);
    }

    /// <summary>A stream whose byte at position p is Pattern(p): read, it gives them; written, it checks them.</summary>
    sealed class PatternStream : Stream
    {
        public static byte Pattern(long p) => (byte)(p * 31 + (p >> 20));
        long _pos;
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> s) { for (int i = 0; i < s.Length; i++) s[i] = Pattern(_pos++); return s.Length; }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> s) { foreach (byte x in s) if (x != Pattern(_pos++)) throw new InvalidDataException($"byte {_pos - 1}"); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    [Fact] // a real array over 2 GB (2.4 GB of memory, so only with NEUROREEF_BIG_TESTS=1): read and written whole, in place
    public void ArrayOver2GBRoundTrips()
    {
        if (Environment.GetEnvironmentVariable("NEUROREEF_BIG_TESTS") != "1") return;
        var a = new float[600_000_000];
        long bytes = Checkpoint.ByteLength(a);
        Assert.True(bytes > int.MaxValue);
        using (var br = new BinaryReader(new PatternStream())) Checkpoint.ReadBytes(br, a, 0, bytes);
        var tail = System.Runtime.InteropServices.MemoryMarshal.AsBytes(a.AsSpan(a.Length - 4));
        for (int k = 0; k < tail.Length; k++) Assert.Equal(PatternStream.Pattern(bytes - 16 + k), tail[k]);
        using (var bw = new BinaryWriter(new PatternStream())) Checkpoint.WriteBytes(bw, a, 0, bytes);
    }

    [Fact] // a prehistory checkpoint of format 16 resumes with its branches' animals; each branch's land is rebuilt
    public void OldPrehistoryCheckpointMigrates()
    {
        string path = Path.GetTempFileName();
        try
        {
            using var a = new Prehistory(2, 2048, 83, 2);
            using (var bw = new BinaryWriter(File.Create(path)))
            {
                bw.Write("NRCK"u8.ToArray()); bw.Write((ushort)16);
                bw.Write(G.LEN); bw.Write(a.B.Length); bw.Write(a.B[0].W.Cap); bw.Write(a.B[0].W.Seed); Nature.Write(bw);
                foreach (var s in a.B) OldWrite(bw, s.W, PlantField);
            }
            using var c = new Prehistory(2, 2048, 83, 2, restore: true);
            var ms = c.LoadCheckpoint(path);
            for (int b = 0; b < 2; b++)
            {
                Assert.True(ms[b].LandRebuilt);
                Assert.Equal(a.B[b].W.AliveCount, c.B[b].W.AliveCount);
                for (int i = 0; i < a.B[b].W.Cap; i++)
                    if (a.B[b].W.StageOf[i] != St.Dead) Assert.True(a.B[b].W.GenomeOf(i).SequenceEqual(c.B[b].W.GenomeOf(i)));
                Assert.True(a.B[b].W.POcc.AsSpan().SequenceEqual(c.B[b].W.POcc), "the rebuilt plants are not the ones the branch grew");
            }
        }
        finally { File.Delete(path); }
    }

    [Fact] // the prehistory builds its relief once and its branches side by side: each branch is the world it built alone before
    public void PrehistoryBranchesAreTheWorldsBuiltAlone()
    {
        int worldMap = Nature.WorldMap;
        Nature.WorldMap = Map.W;   // the world that follows is on the prehistory's map: branch 0's land is kept for it
        try
        {
            using var pre = new Prehistory(2, 2048, 84, 2);
            for (int b = 0; b < 2; b++)
            {
                using var alone = new Simulation(2048, 84 + 1000u * (uint)b, 1, null, prehistory: true, terrainSeed: 84, evo: pre.Pending);
                alone.W.Evo.CopyFrom(pre.Pending);
                Assert.True(State(alone.W).AsSpan().SequenceEqual(State(pre.B[b].W)), $"branch {b} differs from the world built alone");
            }
            Assert.NotNull(pre.WorldLand);
            Assert.True(pre.WorldLand!.Fits(pre.B[0].W));
        }
        finally { Nature.WorldMap = worldMap; }
    }

    [Fact] // the world that begins on branch 0's land is the world that grows its own
    public void WorldOnAPrehistoryLandIsTheSame()
    {
        using var branch = new Simulation(2048, 85, 2, prehistory: true, keepLand: true, evo: new EvoSettings { MutRate = 0.002f });
        var land = branch.StartLand!;
        Assert.Equal(Land.Stage.Grown, land.Is);
        using var own = new Simulation(4096, 85, 2);
        using var given = new Simulation(4096, 85, 2, land: land);
        Assert.True(land.Fits(given.W));
        Assert.True(State(own.W).AsSpan().SequenceEqual(State(given.W)), "the world on the given land differs");
        for (int k = 0; k < 100; k++) { own.Step(); given.Step(); }
        Assert.True(State(own.W).AsSpan().SequenceEqual(State(given.W)), "the world on the given land runs differently");
        // a land for another seed is not used
        using var other = new Simulation(4096, 86, 2, empty: true, land: land);
        Assert.False(land.Fits(other.W));
        using var otherOwn = new Simulation(4096, 86, 2, empty: true);
        Assert.True(State(otherOwn.W).AsSpan().SequenceEqual(State(other.W)));
    }

    [Fact] // every array of World whose size follows the map is land (Checkpoint.LandFields): rebuilt with the land, and given with it
    public void EveryCellArrayIsLand()
    {
        const BindingFlags fl = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var small = new World(1000, 1);
        Map.Set(2 * TestWorld.Side);
        try
        {
            var big = new World(1000, 1);
            var notLand = new HashSet<string> { "CellStart", "CallStart", "CellCount" };   // the spatial hash, rebuilt every step
            foreach (var f in typeof(World).GetFields(fl))
            {
                if (f.GetValue(small) is not Array a || notLand.Contains(f.Name)) continue;
                var b = (Array)f.GetValue(big)!;
                bool perCell = a.Length != b.Length || (a is float[][] p && p[0].Length != ((float[][])b)[0].Length);
                if (perCell) Assert.True(Checkpoint.LandFields.Contains(f.Name), $"World.{f.Name} grows with the map: it is land, add it to Checkpoint.LandFields");
            }
        }
        finally { Map.Set(TestWorld.Side); }
    }
}
