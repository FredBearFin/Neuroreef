// Checkpoint (DESIGN §7.6): the whole state of a prehistory, so a run of weeks survives a reboot or a crash
// and resumes exactly where it was. Every state field of each branch's World (with its clade table, clade
// scratch, telemetry and evolution settings) is written by reflection, so a field added later is saved
// without anyone remembering to. Arrays are copied into the existing arrays on load (same capacity and
// genome layout); a readonly scalar (capacity, seed) must match, or the checkpoint is refused.
//
// Format: "NRCK" | u16 version | i32 G.LEN | i32 branches | i32 capacity | u32 seed | nature | per branch: object tree.
//         "NRWK" | u16 version | i32 G.LEN | i32 capacity | u32 seed | u32 terrain seed | nature | object tree.
// Object tree (version 18 on): per field its name, a kind byte (Kind) and its value; an empty name ends the object.
// With the kind, a field this program does not know can always be skipped. Before 18 there was no kind byte: the
// kind is taken from the field of the same name in this program, and an unknown field is skipped as an array.
// The largest per-slot arrays (genome, brain weights, traces, histories; SlotFields) are written for slots in use
// only (Kind.Slots): every one of them is written whole at a birth before it is read, so a dead slot's are not state.
// An array may hold more than 2 GB (W2f, Trace: Cap * G.W2N floats), so bytes are copied through spans at long offsets,
// a chunk at a time; a whole array (Kind.Arr) gives its byte count as i64 from version 19 on (i32 before).
// A file is written to <path>.tmp and then renamed over <path>, so a crash during a save keeps the last one.
//
// Older saves (OldestMigratable .. Version - 1) are migrated on load: every field whose name, kind, element type and
// length still match is kept (the animals, their genomes and brains, the clades, the step, the telemetry); what is
// missing or no longer fits starts as a new world has it. If the land of the save was built by other rules than this
// program's (before LandVersion), or any land field is missing or changed, the land is rebuilt from the world's own
// terrain seed and nature settings exactly as a new world builds it (terrain, water cycle, plants, pathogen reservoir)
// and the saved land is not used. Migration says what it kept and rebuilt.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Neuroreef;

public static class Checkpoint
{
    public const ushort Version = 19;   // 19: the byte count of a whole array as i64 (arrays over 2 GB); 18: a kind byte per field, only slots in use for the big per-slot arrays, older saves migrated; 17: evolving plants (Revision S: plant genotype table, slots, seed banks, dispersal); 16: pathogens (Revision R: pathogen genomes, immunity, reservoir); 15: the water cycle (rain, snow, soil water, groundwater, discharge, ponds, lakes); 14: geology (Revision R: plates, deep-time erosion, rock; World.Rock); 13: hydrology (Revision Q: flow, alluvium, floodplains); 12: path-integration senses and RETURN_FED (Revision P); 11: wiring-gain gene (Revision P); 10: inherited appearance genes and water-depth sense combined; 9: separate appearance/depth layouts; 8: breeding condition; 7: endothermy; 6: size floor; 5: body heat; 4: vocal learning; 3: ponds; 2: map and nature settings
    /// <summary>The oldest save this program can continue: the genome layout (GeneBank.Version 12) is unchanged since checkpoint 12.</summary>
    public const ushort OldestMigratable = 12;
    /// <summary>The oldest save whose land (relief, water, plants, reservoir) was made by this program's rules; older saves get their land rebuilt.</summary>
    public const ushort LandVersion = 17;
    const ushort TaggedVersion = 18;     // the first version with a kind byte per field
    const ushort LongBytesVersion = 19;  // the first version with an i64 byte count per whole array
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // classes whose state is saved by walking their fields; any other reference field is not state (links, buffers rebuilt each step)
    static readonly HashSet<Type> Walk = new() { typeof(World), typeof(Species), typeof(Clades.Scratch), typeof(Telemetry), typeof(EvoSettings), typeof(PlantTable) };
    static readonly Dictionary<Type, FieldInfo[]> _fields = new();

    /// <summary>
    /// The World fields that are the land: terrain, climate, water cycle, plants (with the plant table, Flora) and the
    /// pathogen reservoir. They are made by Terrain.Build, WaterCycle.Init and the spin-up from the terrain seed, the run
    /// seed and the nature settings, and change as the world runs. Everything else is the animals, the clades and the clock.
    /// (EngineeringTests guards that every per-cell array of World is listed here.)
    /// </summary>
    public static readonly HashSet<string> LandFields = new(StringComparer.Ordinal)
    {
        // fields and plants
        "Pool", "Shrub", "Tree", "Sapling", "Nutrient", "Scent", "NutrientN", "ScentN",
        "POcc", "BankMom", "BankDad", "SeedMom", "SeedDad", "FruitG", "PollenDad", "BankM", "SeedOut", "PFlags",
        "WindAcc", "WindDist", "RowFar", "RowFarBest", "WindAccMom", "WindAccDad", "WindDistMom", "WindDistDad", "RowFarMom", "RowFarDad",
        "Flora", "Moisture", "Poll", "Temp", "Fire", "FireN", "TreeMean",
        // terrain, hydrology, geology, climate
        "Elev", "Depth", "MoistBase", "Fert", "Cell", "DistWater", "DistLand", "PondSite", "Wet", "DistDrink",
        "Flow", "Alluvium", "FloodWet", "FloodFrom", "Rock", "TLapse", "Cont", "ClimateZ", "SeaLevel",
        // water cycle and weather years
        "Rain", "SoilCap", "Recv", "DrainOrder", "Outlet", "LakeOf", "LakeArea", "LakeDepth", "LakeQm", "LakeGross", "LakeCount",
        "SoilWater", "Snow", "Ground", "Deep", "PondWater", "Runoff", "Discharge", "DischargeMean", "Seep", "LakeLevel", "LakeIn",
        "WcRow", "WaterOn", "WbRain", "WbET", "WbPondEvap", "WbToLakes", "WbLakeRain", "WbLakeEvap", "WbLakeSpill", "RainAnom", "RainYear",
        // the pathogen reservoir in the ground and water
        "ResLoad", "ResGene", "ResKey", "ResRecv", "ResOrder", "ResRivers", "ResNet",
    };

    /// <summary>Per-slot arrays written for slots in use only: each is written whole when a slot is born (the genome before
    /// Life.Place, the rest by Life.Place and Brain.InitLife), so what a dead slot holds is never read again.</summary>
    static readonly HashSet<string> SlotFields = new(StringComparer.Ordinal) { "Genome", "W2f", "Trace", "Hidden", "HistA", "HistV", "HistD", "HistY", "Gain", "Feat" };

    // the readonly scalars of a World that say which world it is: a save for another one is refused, not migrated
    static readonly HashSet<string> Identity = new(StringComparer.Ordinal) { "Cap", "Seed", "TerrainSeed", "NS" };

    enum Kind : byte { Scalar = 1, Str = 2, Arr = 3, Jagged = 4, Obj = 5, ObjArr = 6, Slots = 7 }

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

    /// <summary>What a load kept, started fresh and rebuilt (a save of this version keeps everything).</summary>
    public sealed class Migration
    {
        public int From, To = Version;
        public bool LandRebuilt, Reexpressed;
        public uint TerrainSeed; public ulong Step; public int Animals, Clades, Moved;
        public readonly SortedSet<string> Kept = new(StringComparer.Ordinal), Missing = new(StringComparer.Ordinal),
            Changed = new(StringComparer.Ordinal), Dropped = new(StringComparer.Ordinal);
        internal bool LandMissingOrChanged;
        public bool Migrated => From != To;

        public override string ToString()
        {
            if (!Migrated) return $"saved by this version of the program (format {To}): everything kept";
            static bool IsLand(string p) { int d = p.IndexOfAny(new[] { '.', '[' }); return LandFields.Contains(d < 0 ? p : p[..d]); }
            var sb = new StringBuilder();
            sb.AppendLine($"migrating a save of format {From} to format {To}: what still fits is kept, the rest starts as a new world has it");
            var kept = Kept.Where(p => !(LandRebuilt && IsLand(p))).ToList();
            bool telemetry = kept.Contains("T.Ring");
            sb.AppendLine($"  kept ({kept.Count} fields): the step ({Step:N0}), {Animals:N0} animals with their genomes, bodies and brains, {Clades} clades" +
                (telemetry ? ", the telemetry history" : "") + (LandRebuilt ? "" : ", the land, water and plants"));
            if (LandRebuilt)
                sb.AppendLine($"  rebuilt from terrain seed {TerrainSeed} and the saved nature, as a new world is built: land, water cycle, plants, pathogen reservoir" +
                    (From < LandVersion ? $" (a save before format {LandVersion} was made by older land rules)" : " (some of the saved land no longer fits)"));
            List("new in this version, started fresh", Missing.Where(p => !(LandRebuilt && IsLand(p))));
            List("changed shape or type, started fresh", Changed.Where(p => !(LandRebuilt && IsLand(p))));
            List("no longer used, dropped", Dropped);
            if (!telemetry) sb.AppendLine("  the telemetry history restarts (its layout changed); the running counters are kept");
            if (Reexpressed) sb.AppendLine("  every animal's body was expressed again from its genes (it has traits the save did not hold)");
            if (Moved > 0) sb.AppendLine($"  {Moved:N0} animals stood where the rebuilt land does not hold them: moved to the nearest place that does");
            return sb.ToString().TrimEnd();

            void List(string what, IEnumerable<string> names)
            {
                var l = names.ToList();
                if (l.Count == 0) return;
                sb.AppendLine($"  {what} ({l.Count}): " + string.Join(", ", l.Take(16)) + (l.Count > 16 ? $", and {l.Count - 16} more" : ""));
            }
        }
    }

    // ------------------------------------------------------------------ a prehistory

    public static void Save(Prehistory pre, string path)
    {
        string tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write("NRCK"u8.ToArray()); bw.Write(Version);
            bw.Write(G.LEN); bw.Write(pre.B.Length); bw.Write(pre.B[0].W.Cap); bw.Write(pre.B[0].W.Seed); Nature.Write(bw);
            foreach (var s in pre.B) Write(bw, s.W, s.W, landOnly: false);
            fs.Flush(true);
        }
        File.Move(tmp, path, overwrite: true);
    }

    static ushort ReadVersion(BinaryReader br, string magic, string what)
    {
        var m = br.ReadBytes(4);
        if (m.Length != 4 || m[0] != magic[0] || m[1] != magic[1] || m[2] != magic[2] || m[3] != magic[3]) throw new InvalidDataException("not a " + what);
        ushort v = br.ReadUInt16();
        if (v > Version) throw new InvalidDataException($"{what} of format {v} is from a newer program (this one reads up to {Version})");
        if (v < OldestMigratable) throw new InvalidDataException($"{what} of format {v} is from an older program than this one can continue (format {OldestMigratable} on: the genome layout changed)");
        return v;
    }

    /// <summary>Branches, slots per branch and seed of a prehistory checkpoint, to rebuild a prehistory it fits.</summary>
    public static (int branches, int cap, uint seed) PrehistoryHeader(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);
        ReadVersion(br, "NRCK", "prehistory checkpoint");
        int len = br.ReadInt32();
        if (len != G.LEN) throw new InvalidDataException($"checkpoint genome length {len}, this program uses {G.LEN} (the rules changed)");
        var h = (br.ReadInt32(), br.ReadInt32(), br.ReadUInt32());
        Nature.Read(br);   // the map and nature it was made with: applied, so the prehistory is rebuilt to fit
        return h;
    }

    /// <summary>
    /// Restore a prehistory built with the same branches, capacity and seed (best built with restore: true, so no land
    /// is made only to be overwritten). An older save is migrated: branches whose land must be rebuilt are rebuilt in
    /// parallel. Returns what each branch kept. Throws if the checkpoint does not fit.
    /// </summary>
    public static Migration[] Load(Prehistory pre, string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        using var br = new BinaryReader(fs);
        ushort v = ReadVersion(br, "NRCK", "prehistory checkpoint");
        int len = br.ReadInt32(), n = br.ReadInt32(), cap = br.ReadInt32(); uint seed = br.ReadUInt32();
        int side = br.ReadInt32(); br.BaseStream.Seek(-4, SeekOrigin.Current);
        if (side != Map.W) throw new InvalidDataException($"checkpoint map is {side} units, this run's is {Map.W}");
        Nature.Read(br);
        if (len != G.LEN) throw new InvalidDataException($"checkpoint genome length {len}, this program uses {G.LEN} (the rules changed)");
        if (n != pre.B.Length || cap != pre.B[0].W.Cap || seed != pre.B[0].W.Seed)
            throw new InvalidDataException($"checkpoint has {n} branches, {cap} slots, seed {seed}; start with the same --branches and --seed");
        var ms = new Migration[n];
        var start = new long[n];
        var need = new bool[n];
        for (int b = 0; b < n; b++)
        {
            ms[b] = new Migration { From = v };
            start[b] = fs.Position;
            if (v != Version) { Read(new Ctx(br, v, ms[b], apply: false), pre.B[b].W); need[b] = NeedsLand(ms[b]); }
            else Read(new Ctx(br, v, ms[b], apply: true), pre.B[b].W);
        }
        if (v == Version) { for (int b = 0; b < n; b++) Summarise(ms[b], pre.B[b].W); return ms; }
        // an older save: rebuild the land where it must be (each branch on its own thread), then take what fits
        System.Threading.Tasks.Parallel.For(0, n, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = n }, b => { if (need[b]) pre.B[b].BuildLand(); });
        for (int b = 0; b < n; b++)
        {
            fs.Position = start[b];
            Read(new Ctx(br, v, new Migration(), apply: true, skipLand: need[b]), pre.B[b].W);
            ms[b].LandRebuilt = need[b];
            AfterMigration(pre.B[b].W, ms[b]);
        }
        return ms;
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
            Write(bw, w, w, landOnly: false);
            fs.Flush(true);
        }
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Read a saved world's capacity and seeds, to build a world it fits into.</summary>
    public static (int cap, uint seed, uint terrainSeed) WorldHeader(string path) => WorldHeader(path, out _);

    /// <summary>The same, with the save's format version (older than Version: it will be migrated).</summary>
    public static (int cap, uint seed, uint terrainSeed) WorldHeader(string path, out int version)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);
        version = ReadVersion(br, "NRWK", "saved world");
        int len = br.ReadInt32();
        if (len != G.LEN) throw new InvalidDataException($"saved world genome length {len}, this program uses {G.LEN} (the rules changed)");
        var h = (br.ReadInt32(), br.ReadUInt32(), br.ReadUInt32());
        Nature.Read(br);   // the map and nature it was made with: applied, so the world is rebuilt to fit
        return h;
    }

    /// <summary>Continue a saved world in this simulation (best built with restore: true). An older save is migrated; its
    /// land is rebuilt by the simulation if it must be. Returns what was kept and rebuilt.</summary>
    public static Migration LoadWorld(Simulation sim, string path) => LoadWorld(sim.W, path, () => sim.BuildLand());

    /// <summary>Load a saved world into w. rebuildLand builds w's land as a new world's (needed only for a save whose
    /// land must be rebuilt; without it such a save is refused).</summary>
    public static Migration LoadWorld(World w, string path, Action? rebuildLand = null)
    {
        var (cap, seed, ts) = WorldHeader(path);
        if (cap != w.Cap || seed != w.Seed || ts != w.TerrainSeed) throw new InvalidDataException("saved world does not fit this world");
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        using var br = new BinaryReader(fs);
        ushort v = ReadVersion(br, "NRWK", "saved world"); br.ReadInt32(); br.ReadInt32(); br.ReadUInt32(); br.ReadUInt32();
        Nature.Read(br);
        var m = new Migration { From = v };
        if (v == Version) { Read(new Ctx(br, v, m, apply: true), w); Summarise(m, w); return m; }
        long start = fs.Position;
        Read(new Ctx(br, v, m, apply: false), w);   // what the save holds, before anything is changed
        bool need = NeedsLand(m);
        if (need)
        {
            if (rebuildLand == null) throw new InvalidDataException($"saved world of format {v}: its land must be rebuilt; load it into a Simulation");
            rebuildLand();
        }
        fs.Position = start;
        Read(new Ctx(br, v, new Migration(), apply: true, skipLand: need), w);
        m.LandRebuilt = need;
        AfterMigration(w, m);
        return m;
    }

    static bool NeedsLand(Migration m) => m.From < LandVersion || m.LandMissingOrChanged;

    static void Summarise(Migration m, World w)
    {
        m.TerrainSeed = w.TerrainSeed; m.Step = w.Step;
        m.Animals = 0; for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] != St.Dead) m.Animals++;
        m.Clades = 0; foreach (var s in w.Sp) if (s.Used) m.Clades++;
    }

    /// <summary>After an older save is loaded: what depends on what was not kept is made consistent again.</summary>
    static void AfterMigration(World w, Migration m)
    {
        bool Lost(string p) => m.Missing.Contains(p) || m.Changed.Contains(p);
        // the telemetry samples: a ring of another layout was not read, so it starts again
        if (Lost("T.Ring")) w.T.Head = 0;
        if (Lost("T.LongRing")) w.T.LongHead = 0;
        // traits the save did not hold: every animal's body expressed again from its genome, as at its birth
        bool phenotype = m.Missing.Concat(m.Changed).Any(p => p.Length > 1 && p[0] == 'P' && char.IsUpper(p[1]) && !LandFields.Contains(p));
        bool pathogens = Lost("PgLoad");
        for (int i = 0; i < w.Cap; i++)
        {
            if (w.StageOf[i] == St.Dead) continue;
            if (phenotype) Body.Express(w, i);
            if (pathogens) Pathogens.Clear(w, i);
            if (m.LandRebuilt) Plants.ClearAnimal(w, i);   // seed and pollen it carried name plant genotypes of the old table
        }
        m.Reexpressed = phenotype;
        if (m.LandRebuilt)
        {
            Pathogens.Seed(w);   // the founding pathogen strains in the rebuilt ground and water, as a new world has them
            m.Moved = Relocate(w);
        }
        Summarise(m, w);
    }

    /// <summary>Animals standing where the (rebuilt) land does not hold them go to the nearest cell that does (within 32 cells).</summary>
    static int Relocate(World w)
    {
        int FW = Map.FW, moved = 0;
        for (int i = 0; i < w.Cap; i++)
        {
            if (w.StageOf[i] == St.Dead) continue;
            int cell = World.FIdx(w.X[i], w.Y[i]);
            if (w.CellAllows(i, cell)) continue;
            int cx = cell % FW, cy = cell / FW;
            for (int r = 1; r <= 32; r++)
            {
                int found = -1;
                for (int dy = -r; dy <= r && found < 0; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;   // the ring at distance r only
                        int c = World.WrapI(cy + dy, FW) * FW + World.WrapI(cx + dx, FW);
                        if (w.CellAllows(i, c)) { found = c; break; }
                    }
                if (found < 0) continue;
                w.X[i] = (found % FW + 0.5f) * K.FCELL; w.Y[i] = (found / FW + 0.5f) * K.FCELL;
                moved++;
                break;
            }
        }
        return moved;
    }

    // ------------------------------------------------------------------ the land alone (Land.cs: built once, used by many worlds)

    internal static byte[] SaveLand(World w)
    {
        var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true)) Write(bw, w, w, landOnly: true);
        return ms.ToArray();
    }

    internal static void LoadLand(World w, byte[] land)
    {
        using var br = new BinaryReader(new MemoryStream(land, writable: false));
        Read(new Ctx(br, Version, new Migration(), apply: true), w);
    }

    // ------------------------------------------------------------------ object tree: writing

    static void Write(BinaryWriter bw, object o, World w, bool landOnly)
    {
        List<(int start, int count)>? runs = null;
        foreach (var f in FieldsOf(o.GetType()))
        {
            if (landOnly && o is World && !LandFields.Contains(f.Name)) continue;
            var t = f.FieldType;
            object? v = f.GetValue(o);
            if (t.IsPrimitive) { bw.Write(f.Name); bw.Write((byte)Kind.Scalar); bw.Write((byte)Type.GetTypeCode(t)); WriteScalar(bw, t, v!); }
            else if (t == typeof(string)) { bw.Write(f.Name); bw.Write((byte)Kind.Str); bw.Write((string?)v ?? ""); }
            else if (t.IsArray && t.GetElementType()!.IsPrimitive)
            {
                bw.Write(f.Name);
                var a = (Array?)v;
                if (o is World && a != null && SlotFields.Contains(f.Name) && a.Length % w.Cap == 0)
                {
                    runs ??= Runs(w);
                    bw.Write((byte)Kind.Slots); WriteSlots(bw, a, a.Length / w.Cap, runs);
                }
                else { bw.Write((byte)Kind.Arr); WriteArray(bw, t.GetElementType()!, a); }
            }
            else if (t == typeof(float[][]))
            {
                bw.Write(f.Name); bw.Write((byte)Kind.Jagged);
                var a = (float[][])v!; bw.Write(a.Length); foreach (var x in a) WriteArray(bw, typeof(float), x);
            }
            else if (t.IsArray && Walk.Contains(t.GetElementType()!))
            {
                bw.Write(f.Name); bw.Write((byte)Kind.ObjArr);
                var a = (Array)v!; bw.Write(a.Length); foreach (var x in a) Write(bw, x!, w, false);
            }
            else if (Walk.Contains(t) && v != null) { bw.Write(f.Name); bw.Write((byte)Kind.Obj); Write(bw, v, w, false); }
        }
        bw.Write("");   // end of object
    }

    /// <summary>The slots in use (not dead), as runs.</summary>
    static List<(int, int)> Runs(World w)
    {
        var r = new List<(int, int)>();
        for (int i = 0; i < w.Cap;)
        {
            if (w.StageOf[i] == St.Dead) { i++; continue; }
            int s = i;
            while (i < w.Cap && w.StageOf[i] != St.Dead) i++;
            r.Add((s, i - s));
        }
        return r;
    }

    /// <summary>The bytes copied at a time: a span is at most 2 GB, an array may be more.</summary>
    internal const int Chunk = 1 << 24;

    /// <summary>The size in bytes of a primitive array, as a long (Buffer.ByteLength overflows past 2 GB).</summary>
    internal static long ByteLength(Array a) => a.LongLength * SizeOf(Type.GetTypeCode(a.GetType().GetElementType()!));

    /// <summary>The bytes [off, off + n) of a primitive array, at a long offset.</summary>
    internal static Span<byte> Bytes(Array a, long off, int n)
    {
        if (off < 0 || n < 0 || off > ByteLength(a) - n) throw new ArgumentOutOfRangeException(nameof(off), $"bytes {off}..{off + n} of an array of {ByteLength(a)}");
        return MemoryMarshal.CreateSpan(ref Unsafe.AddByteOffset(ref MemoryMarshal.GetArrayDataReference(a), (nint)off), n);
    }

    internal static void WriteBytes(BinaryWriter bw, Array a, long off, long bytes, int chunk = Chunk)
    {
        for (long k = 0; k < bytes; k += chunk)
            bw.Write(Bytes(a, off + k, (int)Math.Min(chunk, bytes - k)));
    }

    internal static void ReadBytes(BinaryReader br, Array a, long off, long bytes, int chunk = Chunk)
    {
        for (long k = 0; k < bytes; k += chunk)
        {
            var s = Bytes(a, off + k, (int)Math.Min(chunk, bytes - k));
            while (s.Length > 0) { int r = br.Read(s); if (r <= 0) throw new EndOfStreamException(); s = s[r..]; }
        }
    }

    static void WriteArray(BinaryWriter bw, Type elem, Array? a)
    {
        bw.Write((byte)Type.GetTypeCode(elem));
        if (a == null) { bw.Write(-1); return; }
        long bytes = ByteLength(a);
        bw.Write(a.Length); bw.Write(bytes);
        WriteBytes(bw, a, 0, bytes);
    }

    static void WriteSlots(BinaryWriter bw, Array a, int stride, List<(int start, int count)> runs)
    {
        var code = Type.GetTypeCode(a.GetType().GetElementType()!);
        int es = SizeOf(code);
        bw.Write((byte)code);
        bw.Write(a.Length); bw.Write(stride); bw.Write(runs.Count);
        foreach (var (s, c) in runs) { bw.Write(s); bw.Write(c); }
        long slot = (long)stride * es;
        foreach (var (s, c) in runs) WriteBytes(bw, a, s * slot, c * slot);
    }

    // ------------------------------------------------------------------ object tree: reading (and migrating)

    sealed class Ctx
    {
        public readonly BinaryReader Br; public readonly int Version; public readonly Migration M;
        public readonly bool Apply, SkipLand;
        public bool Tagged => Version >= TaggedVersion;
        public Ctx(BinaryReader br, int version, Migration m, bool apply, bool skipLand = false) { Br = br; Version = version; M = m; Apply = apply; SkipLand = skipLand; }
    }

    static bool Saved(FieldInfo f, object o)
    {
        var t = f.FieldType;
        return t.IsPrimitive || t == typeof(string) || (t.IsArray && t.GetElementType()!.IsPrimitive) || t == typeof(float[][])
            || (t.IsArray && Walk.Contains(t.GetElementType()!)) || (Walk.Contains(t) && f.GetValue(o) != null);
    }

    /// <summary>A readonly scalar: a size or an identity the constructor sets, not state.</summary>
    static bool Shape(FieldInfo f) => f.IsInitOnly && f.FieldType.IsPrimitive;

    static Kind KindOf(Type t) =>
        t.IsPrimitive ? Kind.Scalar : t == typeof(string) ? Kind.Str : t == typeof(float[][]) ? Kind.Jagged
        : t.IsArray && t.GetElementType()!.IsPrimitive ? Kind.Arr : t.IsArray ? Kind.ObjArr : Kind.Obj;

    static void Read(Ctx c, World w) => Read(c, w, "", c.Apply, false);

    /// <param name="apply">false: read past the values without changing o (the scan of an older save, or a field not taken)</param>
    /// <param name="land">o belongs to the land (the plant table)</param>
    static void Read(Ctx c, object o, string path, bool apply, bool land)
    {
        var br = c.Br; var m = c.M;
        var fields = FieldsOf(o.GetType());
        var byName = fields.ToDictionary(f => f.Name);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (string name = br.ReadString(); name.Length > 0; name = br.ReadString())
        {
            string p = path + name;
            bool isLand = land || (o is World && LandFields.Contains(name));
            byName.TryGetValue(name, out var f);
            Kind kind = c.Tagged ? (Kind)br.ReadByte() : f != null ? KindOf(f.FieldType) : Kind.Arr;
            if (f == null)
            {
                if (c.Version == Version && c.Tagged) throw new InvalidDataException($"checkpoint field {o.GetType().Name}.{name} is unknown to this program");
                if (c.Tagged) Skip(c, kind); else SkipOldArray(c, p);
                m.Dropped.Add(p);
                continue;
            }
            seen.Add(name);
            bool take = apply && !(c.SkipLand && isLand);
            bool ok = kind switch
            {
                Kind.Scalar => ReadScalarField(c, o, f, take, p),
                Kind.Str => ReadString(c, o, f, take),
                Kind.Arr or Kind.Slots => ReadArrayField(c, (Array?)(f.FieldType.IsArray && f.FieldType.GetElementType()!.IsPrimitive ? f.GetValue(o) : null), kind, take),
                Kind.Jagged => ReadJagged(c, f.FieldType == typeof(float[][]) ? (float[][]?)f.GetValue(o) : null, take),
                Kind.ObjArr => ReadObjArray(c, f.FieldType.IsArray && Walk.Contains(f.FieldType.GetElementType()!) ? (Array?)f.GetValue(o) : null, p, take, isLand),
                Kind.Obj => ReadObj(c, Walk.Contains(f.FieldType) ? f.GetValue(o) : null, p, take, isLand),
                _ => throw new InvalidDataException($"checkpoint field {p}: unknown kind {(int)kind}"),
            };
            if (ok) m.Kept.Add(p);
            else
            {
                if (c.Version == Version) throw new InvalidDataException($"checkpoint field {p} does not fit this program");
                m.Changed.Add(p);
                if (isLand && !Shape(f)) m.LandMissingOrChanged = true;   // (a size that changed shows in the arrays it sizes)
            }
        }
        foreach (var f in fields)
            if (!seen.Contains(f.Name) && Saved(f, o) && !Shape(f))   // a size this program sets itself (once a constant) is not missing
            {
                string p = path + f.Name;
                m.Missing.Add(p);
                if (land || (o is World && LandFields.Contains(f.Name))) m.LandMissingOrChanged = true;
            }
    }

    static bool ReadScalarField(Ctx c, object o, FieldInfo f, bool take, string p)
    {
        var br = c.Br;
        Type st = c.Tagged ? TypeOfCode((TypeCode)br.ReadByte()) : f.FieldType;
        object v = ReadScalar(br, st);
        if (!f.FieldType.IsPrimitive) return false;
        if (st != f.FieldType)
        {
            try { v = Convert.ChangeType(v, f.FieldType, System.Globalization.CultureInfo.InvariantCulture); }
            catch (Exception e) when (e is InvalidCastException or OverflowException) { return false; }
        }
        if (f.IsInitOnly)
        {
            if (Equals(v, f.GetValue(o))) return true;
            if (o is World && Identity.Contains(f.Name)) throw new InvalidDataException($"checkpoint {o.GetType().Name}.{f.Name} = {v}, this run has {f.GetValue(o)}");
            if (c.Version == Version) throw new InvalidDataException($"checkpoint {p} = {v}, this run has {f.GetValue(o)}");
            return false;
        }
        if (take) f.SetValue(o, v);
        return true;
    }

    static bool ReadString(Ctx c, object o, FieldInfo f, bool take)
    {
        string s = c.Br.ReadString();
        if (f.FieldType != typeof(string)) return false;
        if (take && !f.IsInitOnly) f.SetValue(o, s);
        return true;
    }

    /// <summary>A primitive array (Kind.Arr, or Kind.Slots: the slots in use) into a; false (and read past it) if it does not fit.</summary>
    static bool ReadArrayField(Ctx c, Array? a, Kind kind, bool take)
    {
        var br = c.Br;
        TypeCode code = c.Tagged ? (TypeCode)br.ReadByte() : TypeCode.Empty;
        int len = br.ReadInt32();
        if (kind == Kind.Slots)
        {
            int stride = br.ReadInt32(), nr = br.ReadInt32();
            var runs = new (int s, int n)[nr];
            long slots = 0;
            for (int k = 0; k < nr; k++) { runs[k] = (br.ReadInt32(), br.ReadInt32()); slots += runs[k].n; }
            int es = SizeOf(code);
            long slot = (long)stride * es;
            bool fits = a != null && a.Length == len && Type.GetTypeCode(a.GetType().GetElementType()!) == code;
            if (!fits || !take) { br.BaseStream.Seek(slots * slot, SeekOrigin.Current); return fits; }
            foreach (var (s, n) in runs) ReadBytes(br, a!, s * slot, n * slot);
            return true;
        }
        if (len < 0) return true;   // written as null: nothing to take
        long bytes = c.Version >= LongBytesVersion ? br.ReadInt64() : br.ReadInt32();
        bool ok = a != null && a.Length == len && bytes == ByteLength(a)
            && (!c.Tagged || Type.GetTypeCode(a.GetType().GetElementType()!) == code);
        if (!ok || !take) { br.BaseStream.Seek(bytes, SeekOrigin.Current); return ok; }
        ReadBytes(br, a!, 0, bytes);
        return true;
    }

    /// <summary>One whole array as a save of the given format holds it (Kind.Arr, tagged), into a (tests).</summary>
    internal static bool ReadArray(BinaryReader br, int version, Array a) => ReadArrayField(new Ctx(br, version, new Migration(), apply: true), a, Kind.Arr, take: true);

    internal static void WriteArray(BinaryWriter bw, Array a) => WriteArray(bw, a.GetType().GetElementType()!, a);

    static bool ReadJagged(Ctx c, float[][]? a, bool take)
    {
        int n = c.Br.ReadInt32();
        bool ok = a != null && a.Length == n;
        for (int k = 0; k < n; k++) ok &= ReadArrayField(c, ok && k < a!.Length ? a[k] : null, Kind.Arr, take && ok);
        return ok;
    }

    static bool ReadObjArray(Ctx c, Array? a, string p, bool take, bool land)
    {
        int n = c.Br.ReadInt32();
        bool ok = a != null && a.Length == n;
        for (int k = 0; k < n; k++)
        {
            object? x = a != null && k < a.Length ? a.GetValue(k) : null;
            if (x != null) Read(c, x, p + "[].", take, land);
            else if (c.Tagged) SkipObject(c);
            else throw new InvalidDataException($"checkpoint {p}: {n} elements, this program has {a?.Length ?? 0}; an old save cannot be read past them");
        }
        return ok;
    }

    static bool ReadObj(Ctx c, object? x, string p, bool take, bool land)
    {
        if (x != null) { Read(c, x, p + ".", take, land); return true; }
        if (!c.Tagged) throw new InvalidDataException($"checkpoint {p}: not an object in this program; an old save cannot be read past it");
        SkipObject(c);
        return false;
    }

    // ---- reading past what is not taken

    static void Skip(Ctx c, Kind kind)
    {
        var br = c.Br;
        switch (kind)
        {
            case Kind.Scalar: ReadScalar(br, TypeOfCode((TypeCode)br.ReadByte())); break;
            case Kind.Str: br.ReadString(); break;
            case Kind.Arr: case Kind.Slots: ReadArrayField(c, null, kind, false); break;
            case Kind.Jagged: ReadJagged(c, null, false); break;
            case Kind.Obj: SkipObject(c); break;
            case Kind.ObjArr: { int n = br.ReadInt32(); for (int k = 0; k < n; k++) SkipObject(c); break; }
            default: throw new InvalidDataException($"checkpoint: unknown field kind {(int)kind}");
        }
    }

    static void SkipObject(Ctx c)
    {
        for (string name = c.Br.ReadString(); name.Length > 0; name = c.Br.ReadString()) Skip(c, (Kind)c.Br.ReadByte());
    }

    /// <summary>A field of an old (untagged) save that this program no longer has: every field removed so far was a primitive array.</summary>
    static void SkipOldArray(Ctx c, string p)
    {
        var br = c.Br; var s = br.BaseStream;
        int len = br.ReadInt32();
        if (len == -1) return;
        int bytes = br.ReadInt32();
        bool plausible = len >= 0 && bytes >= 0 && (len == 0 ? bytes == 0 : bytes % len == 0 && bytes / len is 1 or 2 or 4 or 8) && s.Position + bytes <= s.Length;
        if (!plausible) throw new InvalidDataException($"checkpoint field {p} of this old save is unknown to this program and cannot be read past");
        s.Seek(bytes, SeekOrigin.Current);
    }

    static Type TypeOfCode(TypeCode t) => t switch
    {
        TypeCode.Boolean => typeof(bool), TypeCode.Byte => typeof(byte), TypeCode.SByte => typeof(sbyte),
        TypeCode.Int16 => typeof(short), TypeCode.UInt16 => typeof(ushort), TypeCode.Int32 => typeof(int), TypeCode.UInt32 => typeof(uint),
        TypeCode.Int64 => typeof(long), TypeCode.UInt64 => typeof(ulong), TypeCode.Single => typeof(float), TypeCode.Double => typeof(double),
        TypeCode.Char => typeof(char),
        _ => throw new InvalidDataException("checkpoint: unknown scalar type " + t),
    };

    static int SizeOf(TypeCode t) => t switch
    {
        TypeCode.Boolean or TypeCode.Byte or TypeCode.SByte => 1,
        TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Char => 2,
        TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Single => 4,
        TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Double => 8,
        _ => throw new InvalidDataException("checkpoint: unknown element type " + t),
    };

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
