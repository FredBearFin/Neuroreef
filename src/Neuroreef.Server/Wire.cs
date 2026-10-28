// Frame encoders for the console link (docs/ICD.md). Binary frames are little-endian and start with an
// 8-byte header: type, version, reserved u16, step u32.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Neuroreef.Server;

public static class Wire
{
    public const byte T_WORLD = 0x01, T_AGENTS = 0x02, T_FIELDS = 0x03, T_TELEMETRY = 0x04, T_HISTORY = 0x05, T_PROBE = 0x06;
    public const int FD = K.FW / 2;                 // field frame side (2x2 averaged)
    public static readonly string[] FieldLayers =
        { "grass", "shrub", "tree", "browse", "fruit", "seed", "nectar", "root", "carrion", "dung+litter", "wood",
          "algae", "phyto", "detritus", "fire", "scent", "moisture", "nutrient", "pollination" };
    public static int FieldsSize => 12 + FieldLayers.Length * FD * FD;

    // probe float layout (offsets in floats after the 24-byte probe header)
    public const int PR_FEAT = 0, PR_HID = PR_FEAT + F.N, PR_Z = PR_HID + G.NH, PR_GAIN = PR_Z + P.N, PR_LEARN = PR_GAIN + P.N,
        PR_SCAL = PR_LEARN + P.N, NSCAL = 20, PR_PHEN = PR_SCAL + NSCAL, NPHEN = 7, PR_DIET = PR_PHEN + NPHEN,
        PR_W1 = PR_DIET + Pools.N, PR_W2 = PR_W1 + G.W1N, PR_END = PR_W2 + G.W2N;
    public static readonly string[] ProbeScalars =
        { "reward", "rewardMean", "energy", "emax", "thirst", "fatigue", "full", "age", "life", "mature",
          "x", "y", "heading", "speed", "generation", "children", "species", "stage", "sex", "pain" };
    public static readonly string[] ProbePhenotype = { "mass", "vmax", "sight", "armour", "strike", "litter", "basal" };
    public static int ProbeSize => 24 + 4 * PR_END;

    static void Header(Span<byte> b, byte type, ulong step)
    {
        b[0] = type; b[1] = 1; b[2] = 0; b[3] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(4), (uint)step);
    }

    /// <summary>WORLD: u16 FW, u16 0, f32 sea level, u8[FW²] elevation, u8[FW²] realm bits, u8[FW²] depth.</summary>
    public static byte[] EncodeWorld(World w)
    {
        int n = K.FW * K.FW;
        var b = new byte[16 + 3 * n];
        Header(b, T_WORLD, w.Step);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(8), (ushort)K.FW);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(12), w.SeaLevel);
        for (int i = 0; i < n; i++)
        {
            b[16 + i] = (byte)Math.Clamp((int)(w.Elev[i] * 255f), 0, 255);
            b[16 + n + i] = w.Cell[i];
            b[16 + 2 * n + i] = (byte)Math.Clamp((int)(w.Depth[i] * 2000f), 0, 255);
        }
        return b;
    }

    /// <summary>FIELDS: u16 side, u16 layer count, u32 0, then each layer u8[side²] (DESIGN §10). Called on the sim thread.</summary>
    public static int EncodeFields(World w, byte[] b)
    {
        Header(b, T_FIELDS, w.Step);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(8), (ushort)FD);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(10), (ushort)FieldLayers.Length);
        int o = 12, FW = K.FW;
        for (int l = 0; l < FieldLayers.Length; l++)
        {
            for (int y = 0; y < FD; y++)
                for (int x = 0; x < FD; x++)
                {
                    int i0 = (2 * y) * FW + 2 * x;
                    float v = (Get(w, l, i0) + Get(w, l, i0 + 1) + Get(w, l, i0 + FW) + Get(w, l, i0 + FW + 1)) * 0.25f;
                    b[o++] = (byte)Math.Clamp((int)(v * 255f), 0, 255);
                }
        }
        return o;
    }

    static float Get(World w, int layer, int i)
    {
        var P = w.Pool;
        return layer switch
        {
            0 => P[(int)Pool.Grass][i], 1 => w.Shrub[i], 2 => w.Tree[i], 3 => P[(int)Pool.Browse][i], 4 => MathF.Min(1, P[(int)Pool.Fruit][i] * 4),
            5 => MathF.Min(1, P[(int)Pool.Seed][i] * 4), 6 => MathF.Min(1, P[(int)Pool.Nectar][i] * 8), 7 => P[(int)Pool.Root][i],
            8 => MathF.Tanh(P[(int)Pool.Carrion][i]), 9 => MathF.Tanh(P[(int)Pool.Dung][i] + P[(int)Pool.Litter][i]), 10 => MathF.Tanh(P[(int)Pool.Wood][i]),
            11 => P[(int)Pool.Algae][i], 12 => P[(int)Pool.Phyto][i], 13 => MathF.Tanh(P[(int)Pool.Detritus][i]),
            14 => w.Fire[i] > 0 ? 1f : 0f, 15 => MathF.Tanh(w.Scent[i] * 4f), 16 => w.Moisture[i], 17 => w.Nutrient[i] / K.N_MAX, 18 => w.Poll[i],
            _ => 0f,
        };
    }

    /// <summary>
    /// PROBE: u32 serial (0 = none), u32 slot, u32 mother serial, u32 youngest child serial, then floats (layout above).
    /// Called on the sim thread after a step.
    /// </summary>
    public static int EncodeProbe(Simulation sim, byte[] b)
    {
        var w = sim.W; var pr = sim.Probe;
        int i = pr.Slot;
        Header(b, T_PROBE, w.Step);
        var s = b.AsSpan();
        if (i < 0 || w.StageOf[i] == St.Dead) { BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(8), 0); return 24; }
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(8), w.Serial[i]);
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(12), (uint)i);
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(16), w.MotherSerial[i]);
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(20), w.LastChildSerial[i]);
        var f = new Span<float>(new float[PR_END]);
        pr.X.CopyTo(f.Slice(PR_FEAT)); pr.Hid.CopyTo(f.Slice(PR_HID)); pr.Z.CopyTo(f.Slice(PR_Z)); pr.Gn.CopyTo(f.Slice(PR_GAIN));
        int gb = i * G.LEN, wb = i * G.W2N;
        for (int p = 0; p < P.N; p++)
        {
            float d = 0;
            for (int k = 0; k < G.NIN; k++) d += MathF.Abs(w.W2f[wb + p * G.NIN + k] - w.Genome[gb + G.W2 + p * G.NIN + k] * K.WSCALE);
            f[PR_LEARN + p] = d / G.NIN;
        }
        var sp = w.Sp[w.Spc[i]];
        float[] sc = { w.Reward[i], w.RewardMean[i], w.Energy[i], w.EmaxOf(i), w.Thirst[i], w.Fatigue[i], w.Gut[i] / MathF.Max(1e-6f, w.PGut[i] * w.MassOf(i) / w.PM[i]),
                       w.Age[i], w.PLife[i], w.PMature[i], w.X[i], w.Y[i], w.Heading[i], w.Speed[i], w.Gen[i], w.Children[i], w.Spc[i], w.StageOf[i], w.Sex[i], w.Pain[i] };
        sc.CopyTo(f.Slice(PR_SCAL));
        float[] ph = { w.PM[i], w.PVmaxA[i], w.PSight[i], w.PArmour[i], w.PStrike[i], w.PLitter[i], w.PBasal[i] };
        ph.CopyTo(f.Slice(PR_PHEN));
        w.DietOf(i).CopyTo(f.Slice(PR_DIET));
        for (int k = 0; k < G.W1N; k++) f[PR_W1 + k] = w.Genome[gb + k] * K.WSCALE;
        new ReadOnlySpan<float>(w.W2f, wb, G.W2N).CopyTo(f.Slice(PR_W2));
        for (int k = 0; k < PR_END; k++) BinaryPrimitives.WriteSingleLittleEndian(s.Slice(24 + 4 * k), f[k]);
        return 24 + 4 * PR_END;
    }

    /// <summary>TELEMETRY: u32 sample index, then the latest sample (f32 × layout length).</summary>
    public static byte[]? EncodeTelemetry(World w)
    {
        var T = w.T;
        int n = T.SampleCount;
        if (n == 0) return null;
        var b = new byte[12 + 4 * T.Len];
        Header(b, T_TELEMETRY, w.Step);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8), (uint)T.Head);
        var a = new float[T.Len];
        T.Sample(n - 1, a);
        Buffer.BlockCopy(a, 0, b, 12, 4 * T.Len);
        return b;
    }

    /// <summary>HISTORY: u32 count, u32 layout length, u32 stride (samples per kept sample), then f32 × count × length, oldest first.</summary>
    public static byte[] EncodeHistory(World w, int maxSamples)
    {
        var T = w.T;
        int n = T.SampleCount, stride = Math.Max(1, (n + maxSamples - 1) / maxSamples), kept = (n + stride - 1) / stride;
        var b = new byte[20 + 4 * T.Len * kept];
        Header(b, T_HISTORY, w.Step);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8), (uint)kept);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), (uint)T.Len);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(16), (uint)stride);
        var a = new float[T.Len];
        int o = 20;
        for (int k = 0, idx = n % stride; k < kept; k++, idx += stride)
        {
            if (idx >= n) idx = n - 1;
            T.Sample(idx, a);
            Buffer.BlockCopy(a, 0, b, o, 4 * T.Len); o += 4 * T.Len;
        }
        return b;
    }

    /// <summary>Metadata for the console: species, senses, primitives, pools, causes, telemetry layout, frame layouts.</summary>
    public static string Meta(SimHost h)
    {
        var w = h.W;
        var species = w.Sp.Select(s => new
        {
            id = s.Id, name = s.Name, niche = s.Niche, color = $"#{s.R:x2}{s.G:x2}{s.B:x2}", shape = s.Shape, mass = s.Mass, unitMass = s.UnitMass,
            swarm = s.Swarm, parasite = s.Parasite, loco = s.Adult.Loco.ToString(), juvLoco = s.Juv.Loco.ToString(), twoStage = s.TwoStage,
            mode = s.Mode.ToString(), medium = s.Medium.ToString(), litter = s.Litter, gestation = s.Gestation, birth = s.Birth,
            armour = s.Armour, strike = s.Strike, fov = s.Fov, sight = s.Sight, life = s.Life, mature = s.Mature, init = s.InitCount,
            diet = s.Adult.Diet, juvDiet = s.Juv.Diet, trophic = Trophic(s),
        });
        var o = new
        {
            type = "meta", version = 1, world = K.WORLD, fw = K.FW, fd = FD, seed = w.Seed, capacity = w.Cap, prehistory = w.Prehistory, bankSource = h.BankSource,
            sampleEvery = K.SAMPLE_EVERY, species, features = F.Names, primitives = P.Names, pools = Pools.Names, causes = Cause.Names,
            fieldLayers = FieldLayers, telemetry = new { names = w.T.Names, offSpecies = w.T.OffSpecies, perSpecies = w.T.PerSpecies, offKill = w.T.OffKill,
                fields = new { count = Telemetry.S_COUNT, juv = Telemetry.S_JUV, eggs = Telemetry.S_EGGS, gen = Telemetry.S_GEN, energy = Telemetry.S_ENERGY,
                    reward = Telemetry.S_REWARD, learned = Telemetry.S_LEARNED, births = Telemetry.S_BIRTHS, conc = Telemetry.S_CONC, deaths = Telemetry.S_DEATHS,
                    intake = Telemetry.S_INTAKE, spend = Telemetry.S_SPEND, strikes = Telemetry.S_STRIKES, kills = Telemetry.S_KILLS, matings = Telemetry.S_MATINGS,
                    imm = Telemetry.S_IMM, dage = Telemetry.S_DAGE, meanAge = Telemetry.S_MEANAGE, sick = Telemetry.S_SICK, infections = Telemetry.S_INFECT, care = Telemetry.S_CARE, kit = Telemetry.S_KIT, arch = Telemetry.S_ARCH, gains = Telemetry.S_GAINS },
                global = new { step = Telemetry.G_STEP, pools = Telemetry.G_POOLS, shrub = Telemetry.G_SHRUB, tree = Telemetry.G_TREE, sapling = Telemetry.G_SAPLING,
                    nutLand = Telemetry.G_NUT_LAND, nutWater = Telemetry.G_NUT_WATER, fire = Telemetry.G_FIRE, burning = Telemetry.G_BURNING, alive = Telemetry.G_ALIVE, season = Telemetry.G_SEASON, rain = Telemetry.G_RAIN } },
            probe = new { feat = PR_FEAT, hid = PR_HID, z = PR_Z, gain = PR_GAIN, learn = PR_LEARN, scal = PR_SCAL, scalars = ProbeScalars, phen = PR_PHEN,
                phenotype = ProbePhenotype, diet = PR_DIET, w1 = PR_W1, w2 = PR_W2, end = PR_END, nh = G.NH, nin = G.NIN },
            landCells = Enumerable.Range(0, K.FW * K.FW).Count(i => w.Cell[i] == (byte)Realm.Land),
        };
        return JsonSerializer.Serialize(o);
    }

    /// <summary>Trophic level estimate from the diet (for grouping only; not used by the simulation).</summary>
    static int Trophic(Species s)
    {
        var d = s.Adult.Diet;
        float meat = d[(int)Pool.Meat] + d[(int)Pool.Blood], dead = d[(int)Pool.Carrion] + d[(int)Pool.Dung] + d[(int)Pool.Litter] + d[(int)Pool.Wood] + d[(int)Pool.Detritus];
        float plant = 0; for (int p = 0; p <= (int)Pool.Phyto; p++) plant += d[p];
        if (dead > meat && dead > plant) return 0;      // decomposers and scavengers
        if (meat > plant) return s.Mass >= 10 ? 4 : 3;  // predators (apex >= 10)
        return meat > 0 ? 2 : 1;                         // omnivores, herbivores
    }
}
