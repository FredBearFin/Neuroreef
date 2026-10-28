// The whole simulation state (DESIGN §3). Every array is allocated here, in the constructor, and
// nowhere else (NR-101, NR-102). Animal state is struct-of-arrays, indexed by slot.

using System;

namespace Neuroreef;

public sealed class World
{
    public readonly int Cap;
    public readonly uint Seed;
    public readonly Species[] Sp = Roster.All;
    public readonly int NS = Roster.Count;
    public ulong Step;

    // ---- agents: kinematics
    public readonly float[] X, Y, Heading, Speed;
    // ---- agents: physiology
    public readonly float[] Energy, Gut, GutEnergy, GutWaste, GutSeedy, Thirst, Fatigue, Age, Pain, Cooldown, Timer;
    public readonly float[] Reward, RewardMean, PendingR, LastFear;
    // ---- agents: identity
    public readonly byte[] Spc, StageOf, Sex, Flags;
    public readonly ushort[] Gen;
    public readonly uint[] Serial, MotherSerial;
    public readonly int[] MotherSlot, Children, LastChildSlot, Matured;
    public readonly uint[] LastChildSerial;
    // ---- agents: phenotype, expressed from the body genes at creation (Body.Express)
    public readonly float[] PM, PBasal, PGut, PDig, PBite, PLife, PMature, PSight, PReach, PTurn, PVmaxA, PVmaxJ, PArmour, PStrike, PUnit;
    public readonly byte[] PLitter;
    public readonly float[] PDietA, PDietJ;   // Cap * Pools.N
    // ---- agents: brain
    public readonly sbyte[] Genome;        // Cap * G.LEN
    public readonly float[] W2f;           // Cap * G.W2N   working output weights = genome W2 + what was learned (not inherited)
    public readonly float[] Trace;         // Cap * G.W2N   eligibility traces
    public readonly float[] HistA, HistD, HistY;   // learning block history: advantage, perturbation, inputs (Brain.BLOCK steps)
    public readonly float[] Hidden;        // Cap * G.NH
    public readonly float[] Gain;          // Cap * P.N     last gains (after exploration)
    public readonly float[] Feat;          // Cap * F.N     last features (for probe/telemetry)
    // ---- agents: intents written by DECIDE (own slot only)
    public readonly float[] IHeading, ISpeed;
    public readonly uint[] IActs;          // bit p-FIRST_ACTION set = action fires
    public readonly int[] ITarget, IMate;  // target slot for STRIKE, partner slot for MATE (-1 none)
    public readonly float[] IFoodHere, IKinDensity;
    public readonly byte[] IScanning;

    // ---- slot management
    public readonly int[] FreeStack; public int FreeTop;
    public readonly int[] Order;
    public uint NextSerial = 1;
    public int AliveCount;

    // ---- spatial hash (counting sort)
    public readonly int[] CellStart, CellCount, Sorted, CellOf;

    // ---- fields (DESIGN §10): food pools 0..12, then structure layers
    public readonly float[][] Pool;        // [Pools.NFIELD][FW*FW]
    public float[] Shrub, Tree, Sapling, Nutrient, Scent;
    public float[] ShrubN, TreeN, SaplingN, NutrientN, ScentN, GrassN;   // double buffers
    public readonly float[] Moisture, Poll;
    public float[] Fire, FireN;
    // ---- static terrain
    public readonly float[] Elev, Depth, MoistBase, Fert;
    public readonly byte[] Cell;           // Realm bits of the cell (Land, Shallow, Deep)
    public readonly float[] DistWater, DistLand;   // in cells
    public float SeaLevel;

    // ---- evolution
    public readonly sbyte[] Archive;       // NS * ARCHIVE * G.LEN
    public readonly float[] ArchiveScore;  // NS * ARCHIVE
    public readonly int[] ArchiveCount;
    public readonly float[] KinX, KinY;    // one living adult per species, sampled each LIFECYCLE (colonists land near kin)
    public readonly int[] KinN;
    public readonly sbyte[] Scratch;       // per-thread genome scratch: G.LEN * 2
    /// <summary>
    /// True only in a prehistory run (--evolve), the only place colonists may appear. A live world is
    /// births and deaths only: no animal ever appears except by birth (NR-313).
    /// </summary>
    public bool Prehistory;
    public int PrehistorySteps = K.PREHISTORY;

    // ---- telemetry
    public readonly Telemetry T;

    public World(int capacity, uint seed)
    {
        Cap = capacity; Seed = seed;
        int c = capacity;
        X = new float[c]; Y = new float[c]; Heading = new float[c]; Speed = new float[c];
        Energy = new float[c]; Gut = new float[c]; GutEnergy = new float[c]; GutWaste = new float[c]; GutSeedy = new float[c];
        Thirst = new float[c]; Fatigue = new float[c]; Age = new float[c]; Pain = new float[c]; Cooldown = new float[c]; Timer = new float[c];
        Reward = new float[c]; RewardMean = new float[c]; PendingR = new float[c]; LastFear = new float[c];
        Spc = new byte[c]; StageOf = new byte[c]; Sex = new byte[c]; Flags = new byte[c];
        Gen = new ushort[c]; Serial = new uint[c]; MotherSerial = new uint[c];
        MotherSlot = new int[c]; Children = new int[c]; Matured = new int[c]; LastChildSlot = new int[c]; LastChildSerial = new uint[c];
        PM = new float[c]; PBasal = new float[c]; PGut = new float[c]; PDig = new float[c]; PBite = new float[c]; PLife = new float[c];
        PMature = new float[c]; PSight = new float[c]; PReach = new float[c]; PTurn = new float[c]; PVmaxA = new float[c]; PVmaxJ = new float[c];
        PArmour = new float[c]; PStrike = new float[c]; PUnit = new float[c]; PLitter = new byte[c];
        PDietA = new float[c * Pools.N]; PDietJ = new float[c * Pools.N];
        Genome = new sbyte[(long)c * G.LEN];
        W2f = new float[(long)c * G.W2N];
        Trace = new float[(long)c * G.W2N];
        Hidden = new float[c * G.NH];
        HistA = new float[c * Brain.BLOCK]; HistD = new float[c * Brain.BLOCK * P.N]; HistY = new float[c * Brain.BLOCK * G.NIN];
        Gain = new float[c * P.N];
        Feat = new float[c * F.N];
        IHeading = new float[c]; ISpeed = new float[c]; IActs = new uint[c]; ITarget = new int[c]; IMate = new int[c];
        IFoodHere = new float[c]; IKinDensity = new float[c]; IScanning = new byte[c];
        FreeStack = new int[c]; Order = new int[c];
        for (int i = 0; i < c; i++) FreeStack[i] = c - 1 - i;
        FreeTop = c;

        CellStart = new int[K.GW * K.GW + 1]; CellCount = new int[K.GW * K.GW]; Sorted = new int[c]; CellOf = new int[c];

        int n = K.FW * K.FW;
        Pool = new float[Pools.NFIELD][];
        for (int p = 0; p < Pools.NFIELD; p++) Pool[p] = new float[n];
        GrassN = new float[n];
        Shrub = new float[n]; Tree = new float[n]; Sapling = new float[n]; Nutrient = new float[n]; Scent = new float[n];
        ShrubN = new float[n]; TreeN = new float[n]; SaplingN = new float[n]; NutrientN = new float[n]; ScentN = new float[n];
        Moisture = new float[n]; Poll = new float[n]; Fire = new float[n]; FireN = new float[n];
        Elev = new float[n]; Depth = new float[n]; MoistBase = new float[n]; Fert = new float[n]; Cell = new byte[n];
        DistWater = new float[n]; DistLand = new float[n];

        Archive = new sbyte[NS * K.ARCHIVE * G.LEN];
        ArchiveScore = new float[NS * K.ARCHIVE];
        ArchiveCount = new int[NS];
        KinX = new float[NS]; KinY = new float[NS]; KinN = new int[NS];
        Scratch = new sbyte[G.LEN * 2];

        T = new Telemetry(this);
    }

    // ---- small helpers used by every phase
    public static float Wrap(float v) { v %= K.WORLD; return v < 0 ? v + K.WORLD : v; }
    public static float Delta1(float d) { if (d > K.WORLD * 0.5f) d -= K.WORLD; else if (d < -K.WORLD * 0.5f) d += K.WORLD; return d; }
    public static int WrapI(int v, int m) { v %= m; return v < 0 ? v + m : v; }
    public static int FIdx(float x, float y)
    {
        int cx = (int)(x * (1f / K.FCELL)), cy = (int)(y * (1f / K.FCELL));
        if ((uint)cx >= K.FW) cx = WrapI(cx, K.FW);
        if ((uint)cy >= K.FW) cy = WrapI(cy, K.FW);
        return cy * K.FW + cx;
    }

    public bool IsAlive(int i) => StageOf[i] != St.Dead;
    public bool IsActive(int i) => StageOf[i] >= St.Juvenile;      // has a body that acts
    public bool Juvenile(int i) => StageOf[i] == St.Juvenile;
    public Stage StageBody(int i) => Sp[Spc[i]].StageOf(StageOf[i] == St.Juvenile);

    /// <summary>Current body mass: juveniles grow from the species' birth mass to their adult mass.</summary>
    public float MassOf(int i)
    {
        byte st = StageOf[i];
        if (st == St.Adult) return PM[i];
        float b = Sp[Spc[i]].Birth;
        if (st <= St.Embryo) return PM[i] * b;
        return PM[i] * (b + (1f - b) * MathF.Min(1f, Age[i] / PMature[i]));
    }

    /// <summary>Apparent size of one organism (super-individuals look like their members).</summary>
    public float ApparentMass(int i) => PUnit[i] > 0 ? PUnit[i] : MassOf(i);

    public float EmaxOf(int i) => K.C_EMAX * MassOf(i);

    /// <summary>Digestive efficiencies of slot i in its current stage.</summary>
    public Span<float> DietOf(int i)
        => new Span<float>(StageOf[i] == St.Juvenile && Sp[Spc[i]].TwoStage ? PDietJ : PDietA, i * Pools.N, Pools.N);

    public float Carnivory(int i) => (StageOf[i] == St.Juvenile && Sp[Spc[i]].TwoStage ? PDietJ : PDietA)[i * Pools.N + (int)Neuroreef.Pool.Meat];

    public float Vmax(int i) => (StageOf[i] == St.Juvenile && Sp[Spc[i]].TwoStage ? PVmaxJ[i] : PVmaxA[i]) * MathF.Max(0.3f, MassOf(i) / PM[i]);

    public bool CellAllows(int cell, Realm r) => (Cell[cell] & (byte)r) != 0;

    public float SeasonAt(float y) => MathK.Sin(2f * MathF.PI * (float)(Step % K.T_SEASON) / K.T_SEASON - 2f * MathF.PI * y / K.WORLD);

    /// <summary>Is the mother of i still the same living animal? Returns her slot or -1.</summary>
    public int MotherOf(int i)
    {
        int m = MotherSlot[i];
        if (m < 0 || Serial[m] != MotherSerial[i] || StageOf[m] == St.Dead) return -1;
        return m;
    }

    public void FireSwap() { (Fire, FireN) = (FireN, Fire); }

    public Span<sbyte> GenomeOf(int i) => new Span<sbyte>(Genome, i * G.LEN, G.LEN);
}
