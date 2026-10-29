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
    public static int FD => Map.FW / 2;              // field frame side (2x2 averaged)
    public static readonly string[] FieldLayers =
        { "grass", "shrub", "tree", "browse", "fruit", "seed", "nectar", "root", "carrion", "dung+litter", "wood",
          "algae", "phyto", "detritus", "fire", "scent", "moisture", "nutrient", "pollination", "pond", "temp", "frozen" };
    public static int FieldsSize => 12 + FieldLayers.Length * FD * FD;

    // probe float layout (offsets in floats after the 24-byte probe header)
    public const int PR_FEAT = 0, PR_HID = PR_FEAT + F.N, PR_Z = PR_HID + G.NH, PR_GAIN = PR_Z + P.N, PR_LEARN = PR_GAIN + P.N,
        PR_SCAL = PR_LEARN + P.N, NSCAL = 20, PR_PHEN = PR_SCAL + NSCAL, NPHEN = 7, PR_DIET = PR_PHEN + NPHEN,
        PR_W1 = PR_DIET + Pools.N, PR_W2 = PR_W1, PR_END = PR_W2 + G.W2N;   // the memory weights are not sent (Revision M: 256 units)
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
        int n = Map.FW * Map.FW;
        var b = new byte[16 + 3 * n];
        Header(b, T_WORLD, w.Step);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(8), (ushort)Map.FW);
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
        int o = 12, FW = Map.FW;
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
            14 => w.Fire[i] > 0 ? 1f : 0f, 15 => MathF.Tanh(w.Scent[i] * 4f), 16 => w.Moisture[i], 17 => w.Nutrient[i] / K.N_MAX, 18 => w.Poll[i], 19 => w.Wet[i] == 2 ? 1f : 0f,
            20 => (w.Temp[i] + 40f) / 90f, 21 => w.Temp[i] < 0f ? 1f : 0f,   // air temperature (-40..50 °C) and frost or ice (Revision O)
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
        float[] sc = { w.Reward[i], w.RewardMean[i], w.Energy[i], w.EmaxOf(i), w.Thirst[i], w.Fatigue[i], w.Gut[i] / MathF.Max(1e-6f, w.PGut[i] * w.MassOf(i) / w.PM[i]),
                       w.Age[i], w.PLife[i], w.PMature[i], w.X[i], w.Y[i], w.Heading[i], w.Speed[i], w.Gen[i], w.Children[i], w.Spc[i], w.StageOf[i], w.Sex[i], w.Pain[i] };
        sc.CopyTo(f.Slice(PR_SCAL));
        float[] ph = { w.PM[i], w.PVmax[i], w.PSight[i], w.PArmour[i], w.PStrike[i], w.PLitter[i], w.PBasal[i] };
        ph.CopyTo(f.Slice(PR_PHEN));
        w.DietOf(i).CopyTo(f.Slice(PR_DIET));
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
        var species = Clades(w);
        var o = new
        {
            type = "meta", version = 1, world = Map.W, fw = Map.FW, fd = FD, seed = w.Seed, capacity = w.Cap, prehistory = w.Prehistory, bankSource = h.BankSource,
            sampleEvery = K.SAMPLE_EVERY, species, features = F.Names, primitives = P.Names, firstAction = P.FIRST_ACTION, callFirst = P.CALL_0, nSound = F.NSOUND, maxSyl = F.MAX_SYL, ctxNames = Telemetry.CtxNames, respNames = Telemetry.RespNames,
            meaning = new { high = Meaning.High, low = Meaning.Low, keysHigh = Meaning.KeysHigh, keysLow = Meaning.KeysLow, verbs = Meaning.Verbs, minN = Meaning.MIN_N }, pools = Pools.Names, causes = Cause.Names, traitNames = Telemetry.TraitNames,
            fieldLayers = FieldLayers, telemetry = new { names = w.T.Names, offSpecies = w.T.OffSpecies, perSpecies = w.T.PerSpecies, offKill = w.T.OffKill,
                fields = new { count = Telemetry.S_COUNT, juv = Telemetry.S_JUV, eggs = Telemetry.S_EGGS, gen = Telemetry.S_GEN, energy = Telemetry.S_ENERGY,
                    reward = Telemetry.S_REWARD, learned = Telemetry.S_LEARNED, births = Telemetry.S_BIRTHS, conc = Telemetry.S_CONC, deaths = Telemetry.S_DEATHS,
                    intake = Telemetry.S_INTAKE, spend = Telemetry.S_SPEND, strikes = Telemetry.S_STRIKES, kills = Telemetry.S_KILLS, matings = Telemetry.S_MATINGS,
                    imm = Telemetry.S_IMM, dage = Telemetry.S_DAGE, meanAge = Telemetry.S_MEANAGE, sick = Telemetry.S_SICK, infections = Telemetry.S_INFECT, care = Telemetry.S_CARE, kit = Telemetry.S_KIT, arch = Telemetry.S_ARCH, traits = Telemetry.S_TRAITS, gains = Telemetry.S_GAINS },
                global = new { step = Telemetry.G_STEP, pools = Telemetry.G_POOLS, shrub = Telemetry.G_SHRUB, tree = Telemetry.G_TREE, sapling = Telemetry.G_SAPLING,
                    nutLand = Telemetry.G_NUT_LAND, nutWater = Telemetry.G_NUT_WATER, fire = Telemetry.G_FIRE, burning = Telemetry.G_BURNING, alive = Telemetry.G_ALIVE, season = Telemetry.G_SEASON, rain = Telemetry.G_RAIN, temp = Telemetry.G_TEMP, tmin = Telemetry.G_TMIN, tmax = Telemetry.G_TMAX, frozen = Telemetry.G_FROZEN } },
            probe = new { feat = PR_FEAT, hid = PR_HID, z = PR_Z, gain = PR_GAIN, learn = PR_LEARN, scal = PR_SCAL, scalars = ProbeScalars, phen = PR_PHEN,
                phenotype = ProbePhenotype, diet = PR_DIET, w1 = PR_W1, w2 = PR_W2, end = PR_END, nh = G.NH, nin = G.NIN },
            landCells = Enumerable.Range(0, Map.FW * Map.FW).Count(i => w.Cell[i] == (byte)Realm.Land),
        };
        return JsonSerializer.Serialize(o);
    }

    /// <summary>Trophic level estimate from the diet (for grouping only; not used by the simulation).</summary>
    /// <summary>
    /// The clade table with a live description of each clade, computed from its living members (DESIGN §7.9). Names and
    /// roles are not designed: 'niche' and 'trophic' only describe what the members' genes currently make them.
    /// </summary>
    public static object[] Clades(World w)
    {
        int ns = K.CLADES;
        var n = new int[ns]; var mass = new double[ns]; var sight = new double[ns]; var fov = new double[ns]; var birth = new double[ns];
        var sexual = new double[ns]; var live = new double[ns]; var diet = new double[ns * Pools.N]; var loco = new int[ns * 5];
        for (int i = 0; i < w.Cap; i++)
        {
            if (w.StageOf[i] < St.Juvenile) continue;
            int s = w.Spc[i]; n[s]++;
            mass[s] += w.PM[i]; sight[s] += w.PSight[i]; fov[s] += 2 * MathF.Acos(Math.Clamp(w.PFovCos[i], -1f, 1f)) * 180 / MathF.PI;
            birth[s] += w.PBirth[i]; sexual[s] += w.PSexual[i]; live[s] += w.PLive[i]; loco[s * 5 + w.PLoco[i]]++;
            for (int p = 0; p < Pools.N; p++) diet[s * Pools.N + p] += w.PDiet[i * Pools.N + p];
        }
        var list = new object[ns];
        for (int s = 0; s < ns; s++)
        {
            var c = w.Sp[s]; double k = Math.Max(1, n[s]);
            var d = new float[Pools.N]; for (int p = 0; p < Pools.N; p++) d[p] = (float)(diet[s * Pools.N + p] / k);
            int best = 0; for (int l = 1; l < 5; l++) if (loco[s * 5 + l] > loco[s * 5 + best]) best = l;
            string lc = ((Loco)best).ToString();
            float m = (float)(mass[s] / k);
            int tro = Trophic(d, m);
            string niche = n[s] == 0 ? (c.Used ? "extinct" : "") :
                $"{LocoWord((Loco)best)} {new[] { "decomposer", "plant-eater", "omnivore", "meat-eater", "large meat-eater" }[tro]}";
            list[s] = new
            {
                id = s, name = c.Name, color = $"#{c.R:x2}{c.G:x2}{c.B:x2}", used = c.Used, alive = c.Alive, parent = c.Parent, ancestor = c.Ancestor,
                generation = c.Generation, sub = c.Sub, why = c.SplitWhy, splitDist = MathF.Round(c.SplitDist, 2), splitGap = MathF.Round(c.SplitGap, 2), born = c.Born, extinct = c.Extinct, serial = c.Serial, count = n[s],
                niche, trophic = tro, mass = m, unitMass = m, swarm = false, parasite = false, loco = lc, juvLoco = lc, twoStage = false,
                sight = (float)(sight[s] / k), fov = (float)(fov[s] / k), birth = (float)(birth[s] / k), sexual = (float)(sexual[s] / k), live = (float)(live[s] / k),
                diet = d, juvDiet = d, shape = 0,
            };
        }
        return list;
    }

    static string LocoWord(Loco l) => l switch { Loco.Walk => "walking", Loco.Wade => "wading", Loco.Swim => "swimming", Loco.Fly => "flying", _ => "settled" };

    static int Trophic(float[] d, float mass)
    {
        float meat = d[(int)Pool.Meat] + d[(int)Pool.Blood], dead = d[(int)Pool.Carrion] + d[(int)Pool.Dung] + d[(int)Pool.Litter] + d[(int)Pool.Wood] + d[(int)Pool.Detritus];
        float plant = 0; for (int p = 0; p <= (int)Pool.Phyto; p++) plant += d[p];
        if (dead > meat && dead > plant) return 0;      // decomposers and scavengers
        if (meat > plant) return mass >= 10 ? 4 : 3;    // meat-eaters (large >= 10)
        return meat > 0.15f ? 2 : 1;                     // omnivores, plant-eaters
    }

    /// <summary>Utterance types shown per clade: the most made, and every new word besides.</summary>
    public const int WORDS_MAX = 24;
    /// <summary>A word is new when it was first made MIN_UTT times within this many steps and has a clear reading.</summary>
    public const int NEW_STEPS = 2000;

    /// <summary>The translator's data (Revision M, N): per living clade and sound, how often it is made, the senses that
    /// stand out around the caller against the clade's usual senses (open-ended, Meaning.cs) with the 7 fixed contexts
    /// as a fallback, and how members of the clade answered it; per utterance type (ordered sequences of 1..3 sounds),
    /// the same readings for the most made, when each was first and last made and whether it is new; the rank-frequency
    /// of all its utterance types; a dialect measure between the clade's western and eastern halves; and the answers to
    /// the person's latest call. Display only.</summary>
    public static string Lexicon(World w)
    {
        var T = w.T; int NS = F.NSOUND, NC = Telemetry.NCTX, NR = Telemetry.NRESP, NT = Voice.NTYPE;
        var clades = new List<object>();
        var z = new float[F.N];
        var syl = new byte[F.MAX_SYL];
        var dialect = Dialects(w);
        for (int s = 0; s < w.NS; s++)
        {
            if (!w.Sp[s].Alive) continue;
            long bn = T.BaseN[s], calls = 0;
            var bases = new float[NC];
            for (int c = 0; c < NC; c++) bases[c] = bn > 0 ? MathF.Round(T.BaseCtx[s * NC + c] / (float)bn, 3) : 0f;
            var sounds = new List<object>();
            for (int k = 0; k < NS; k++)
            {
                int sk = s * NS + k; long n = T.CallN[sk];
                var ctx = new float[NC]; for (int c = 0; c < NC; c++) ctx[c] = n > 0 ? MathF.Round(T.CallCtx[sk * NC + c] / (float)n, 3) : 0f;
                var resp = new long[NR]; for (int r = 0; r < NR; r++) resp[r] = T.CallResp[sk * NR + r];
                // open-ended meaning: every sense's standardised difference when the sound is made (0 = no clear difference),
                // the strongest as phrases, and what listeners of the clade turn to on hearing it
                Meaning.SenseDiffs(T, s, k, z);
                var zs = new float[F.N]; for (int f = 0; f < F.N; f++) zs[f] = float.IsNaN(z[f]) ? 0f : MathF.Round(z[f], 2);
                var when = Meaning.RankSenses(T, s, k, 3).Select(m => new { f = m.Index, hi = m.High, d = MathF.Round(m.D, 2), text = Meaning.Phrase(m.Index, m.High) }).ToArray();
                var then = Meaning.RankAnswers(T, s, k, 3).Select(m => new { p = m.Index, d = MathF.Round(m.D, 2), text = Meaning.Verbs[m.Index] }).ToArray();
                calls += n;
                sounds.Add(new { n, ctx, resp, heard = T.HeardN[sk], z = zs, when, then });
            }
            // utterance types: the most made, and any new word below them
            var order = Enumerable.Range(0, NT).Where(t => T.UttN[s * NT + t] > 0).OrderByDescending(t => T.UttN[s * NT + t]).ThenBy(t => t).ToArray();
            long utts = order.Sum(t => T.UttN[s * NT + t]);
            var words = new List<object>();
            for (int r = 0; r < order.Length; r++)
            {
                int t = order[r], st = s * NT + t; long n = T.UttN[st];
                bool recent = T.UttCross[st] > 0 && (long)w.Step + 1 - T.UttCross[st] <= NEW_STEPS;
                if (r >= WORDS_MAX && !recent) continue;
                var when = Meaning.RankUttSenses(T, s, t, 3).Select(m => new { f = m.Index, hi = m.High, d = MathF.Round(m.D, 2), text = Meaning.Phrase(m.Index, m.High) }).ToArray();
                var then = Meaning.RankUttAnswers(T, s, t, 3).Select(m => new { p = m.Index, d = MathF.Round(m.D, 2), text = Meaning.Verbs[m.Index] }).ToArray();
                bool isNew = recent && n >= Telemetry.MIN_UTT && when.Length > 0;
                if (r >= WORDS_MAX && !isNew) continue;
                int len = Voice.Decode(t, syl);
                Meaning.UttSenseDiffs(T, s, t, z);
                var zs = new float[F.N]; for (int f = 0; f < F.N; f++) zs[f] = float.IsNaN(z[f]) ? 0f : MathF.Round(z[f], 2);
                var resp = new long[NR]; for (int q = 0; q < NR; q++) resp[q] = T.UttResp[st * NR + q];
                words.Add(new { t, syl = syl.Take(len).Select(b => (int)b).ToArray(), rank = r + 1, n, heard = T.UttHeardN[st], resp,
                    first = T.UttFirst[st] - 1, last = T.UttLast[st] - 1, born = T.UttCross[st] - 1, isNew, z = zs, when, then });
            }
            var zipf = order.Select(t => T.UttN[s * NT + t]).ToArray();
            clades.Add(new { id = s, n = bn, calls, utts, types = order.Length, tracked = T.TrackOf[s] >= 0, mute = calls == 0, bases, sounds, words, zipf, dialect = dialect[s] });
        }
        return System.Text.Json.JsonSerializer.Serialize(new { type = "lexicon", step = w.Step, minUtt = Telemetry.MIN_UTT, newSteps = NEW_STEPS, clades,
            user = new { id = w.UserCallId, sounds = w.UserCall.Where(b => b != 0).Select(b => (int)b).ToArray(), resp = T.UserRespId == w.UserCallId ? T.UserResp : new long[NR] } });
    }

    /// <summary>
    /// A dialect measure per clade (display only): the Jensen-Shannon divergence (bits) between the last utterances of its
    /// members in the western and the eastern half of the map, beside the same between two halves drawn without regard to
    /// place (odd and even slots), which is what sampling alone gives. Null when a half has fewer than 20 speakers.
    /// </summary>
    static object?[] Dialects(World w)
    {
        int NT = Voice.NTYPE;
        var west = new int[K.CLADES * NT]; var east = new int[K.CLADES * NT]; var odd = new int[K.CLADES * NT]; var even = new int[K.CLADES * NT];
        var nw = new int[K.CLADES]; var ne = new int[K.CLADES]; var no = new int[K.CLADES]; var nv = new int[K.CLADES];
        for (int i = 0; i < w.Cap; i++)
        {
            if (w.StageOf[i] < St.Juvenile) continue;
            int t = Voice.TypeOf(w.LastUtt, i * F.MAX_SYL);
            if (t < 0) continue;
            int s = w.Spc[i], k = s * NT + t;
            if (w.X[i] < Map.W * 0.5f) { west[k]++; nw[s]++; } else { east[k]++; ne[s]++; }
            if ((i & 1) != 0) { odd[k]++; no[s]++; } else { even[k]++; nv[s]++; }
        }
        var res = new object?[K.CLADES];
        for (int s = 0; s < K.CLADES; s++)
        {
            if (nw[s] < 20 || ne[s] < 20) continue;
            res[s] = new { west = nw[s], east = ne[s], jsd = MathF.Round(Jsd(west, east, s * NT, NT, nw[s], ne[s]), 3),
                chance = no[s] >= 20 && nv[s] >= 20 ? MathF.Round(Jsd(odd, even, s * NT, NT, no[s], nv[s]), 3) : (float?)null };
        }
        return res;
    }

    static float Jsd(int[] a, int[] b, int o, int n, int na, int nb)
    {
        double d = 0;
        for (int k = 0; k < n; k++)
        {
            double p = a[o + k] / (double)na, q = b[o + k] / (double)nb, m = 0.5 * (p + q);
            if (p > 0) d += 0.5 * p * Math.Log2(p / m);
            if (q > 0) d += 0.5 * q * Math.Log2(q / m);
        }
        return (float)d;
    }
}
