// The whole simulation state (DESIGN §3). Every array is allocated here, in the constructor, and
// nowhere else (NR-101, NR-102). Animal state is struct-of-arrays, indexed by slot.

using System;

namespace Neuroreef;

public sealed class World
{
    public readonly int Cap;
    public readonly uint Seed;
    public readonly Species[] Sp = new Species[K.CLADES];   // clade slots (DESIGN §7.9)
    public readonly int NS = K.CLADES;
    public ulong Step;

    // ---- agents: kinematics
    public readonly float[] X, Y, Heading, Speed;
    // ---- agents: physiology
    public readonly float[] Energy, Gut, GutEnergy, GutWaste, GutSeedy, Thirst, Fatigue, Age, Pain, Cooldown, Timer;
    public readonly float[] Grown;         // share of its growable tissue a juvenile has built (0 at birth, 1 = adult size; DESIGN §9.1)
    public readonly float[] Reward, RewardMean, PendingR, LastFear;
    // ---- agents: identity
    public readonly byte[] Spc, StageOf, Sex, Flags;
    public readonly ushort[] Gen;
    public readonly uint[] Serial, MotherSerial;
    public readonly int[] MotherSlot, Children, LastChildSlot, Matured;
    public readonly uint[] LastChildSerial;
    // ---- agents: phenotype, expressed from the body genes at creation (Body.Express)
    public readonly float[] PM, PBasal, PGut, PDig, PBite, PLife, PMature, PSight, PReach, PTurn, PVmax, PArmour, PStrike;
    public readonly byte[] PLitter;
    public readonly float[] PDiet;            // Cap * Pools.N digestive efficiencies
    public readonly float[] PCare;
    public readonly float[] PDrain;
    public readonly float[] PWaterEcon;       // water-economy gene expressed (0..1)
    public readonly float[] PThirstK;         // how fast it dries out, against C_THIRST (body size and the water-economy gene)           // share of successful strikes that drain blood instead of killing            // parental care level (0 = none; DESIGN §7.8)
    public readonly float[] PBirth, PFovCos, PGest, PCool, PLegs, PFins, PWings, PVLand, PVWater, PVAir, PSexRatio, PImmune;
    public readonly byte[] PLoco, PRealm, PSexual, PLive, PParasite, PLegPairs, PFinPairs, PEyePairs, PBrain, PHerm;
    // ---- agents: care and disease (DESIGN §7.8, §9.6)
    public readonly float[] CareLeft;         // care a mother can still give this step
    public readonly uint[] CareStep;          // step + 1 that CareLeft refers to (0 = never)
    public readonly float[] Infect, Immune;   // steps left infectious / immune (0 = susceptible)
    public readonly float[] Virulence;        // virulence of the pathogen strain an infected organism carries
    public readonly float[] IVirNear;         // mean virulence of the infected contacts, from DECIDE
    public readonly sbyte[] Strain;           // NMHC bytes per slot: the recognition key of the strain an organism carries
    public readonly int[] IStrainSrc;         // an infected contact whose strain this organism is exposed to, from DECIDE (-1 none)
    public readonly byte[] IInfNear;          // infected conspecifics in contact, counted by DECIDE (own slot only)
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
    // voices (Revision M): the sound each organism made this step (0 none, 1..8), and what each heard (loudest call)
    public readonly byte[] Call;
    public readonly int[] IHeard;          // 0 nothing; else 1 + caller clade x NSOUND + sound
    public readonly float[] IHeardX, IHeardY;
    public readonly byte[] IHeardUser;     // heard the person's own call this step
    // the person speaking into the world (the console's translator): one call at a time, heard on the next step
    public float UserCallX, UserCallY; public int UserCallSound; public ulong UserCallStep = ulong.MaxValue; public int UserCallId;
    public readonly int[] ITarget, IMate;  // target slot for STRIKE, partner slot for MATE (-1 none)
    public readonly float[] IFoodHere, IKinDensity;
    public readonly byte[] IEatPrey;       // EAT means attacking the target in reach (it is worth more than the plants here)
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
    public readonly byte[] PondSite;               // 1 = a basin that can hold a seasonal pond (Revision K)
    public readonly byte[] Wet;                    // drinkable water now: 1 = lake, sea or river, 2 = a full pond
    public readonly float[] DistDrink;             // cells to the nearest drinkable water (lakes and full ponds)
    public float SeaLevel;
    // ---- weather years (DESIGN §10.7) and wind seed rain (DESIGN §10.2)
    public float RainAnom;                 // this year's rainfall anomaly (0 = an average year)
    public long RainYear = -1;             // year RainAnom belongs to
    public float TreeMean;                 // mean tree cover of land cells (refreshed every 32 steps)

    // ---- evolution
    public readonly sbyte[] Archive;       // NS * ARCHIVE * G.LEN
    public readonly float[] ArchiveScore;  // NS * ARCHIVE
    public readonly int[] ArchiveCount;
    public readonly sbyte[] Scratch;       // per-thread genome scratch: G.LEN * 2
    public readonly Clades.Scratch CladeScratch = new();   // clade table version, serials, speciation buffers
    /// <summary>
    /// True in a prehistory branch. A label only: every world, prehistory or live, is births and deaths only
    /// after its founders; no animal ever appears except by birth (NR-313).
    /// </summary>
    public bool Prehistory;
    public readonly EvoSettings Evo = new();   // the evolutionary search's knobs (prehistory; mutation also in a live world)
    /// <summary>Approximate memory per agent slot (bytes), for the setup screen's estimate.</summary>
    public const int BytesPerSlot = G.LEN + 2 * G.W2N * 4 + Brain.BLOCK * (1 + P.N + G.NIN) * 4 + (G.NH + P.N + F.N + Pools.N + G.NMHC) * 4 + 432;
    public readonly uint TerrainSeed;          // branches of one prehistory share their land

    // ---- telemetry
    public readonly Telemetry T;

    /// <summary>The most slots a world can have: one array holds every genome (.NET arrays have at most 2^31 elements).</summary>
    public static int MaxSlots => int.MaxValue / G.LEN / 1024 * 1024;

    public World(int capacity, uint seed, uint terrainSeed = 0)
    {
        if (capacity > MaxSlots) throw new ArgumentOutOfRangeException(nameof(capacity), $"at most {MaxSlots:N0} slots per world (each holds a genome of {G.LEN:N0} bytes)");
        Cap = capacity; Seed = seed; TerrainSeed = terrainSeed == 0 ? seed : terrainSeed;
        int c = capacity;
        X = new float[c]; Y = new float[c]; Heading = new float[c]; Speed = new float[c];
        Energy = new float[c]; Gut = new float[c]; GutEnergy = new float[c]; GutWaste = new float[c]; GutSeedy = new float[c];
        Grown = new float[c];
        Thirst = new float[c]; Fatigue = new float[c]; Age = new float[c]; Pain = new float[c]; Cooldown = new float[c]; Timer = new float[c];
        Reward = new float[c]; RewardMean = new float[c]; PendingR = new float[c]; LastFear = new float[c];
        Spc = new byte[c]; StageOf = new byte[c]; Sex = new byte[c]; Flags = new byte[c];
        Gen = new ushort[c]; Serial = new uint[c]; MotherSerial = new uint[c];
        MotherSlot = new int[c]; Children = new int[c]; Matured = new int[c]; LastChildSlot = new int[c]; LastChildSerial = new uint[c];
        PM = new float[c]; PBasal = new float[c]; PGut = new float[c]; PDig = new float[c]; PBite = new float[c]; PLife = new float[c];
        PMature = new float[c]; PSight = new float[c]; PReach = new float[c]; PTurn = new float[c]; PVmax = new float[c];
        PArmour = new float[c]; PStrike = new float[c]; PLitter = new byte[c];
        PDiet = new float[c * Pools.N]; PCare = new float[c];
        PBirth = new float[c]; PFovCos = new float[c]; PGest = new float[c]; PCool = new float[c];
        PVLand = new float[c]; PVWater = new float[c]; PVAir = new float[c]; PSexRatio = new float[c]; PImmune = new float[c];
        PLegs = new float[c]; PFins = new float[c]; PWings = new float[c];
        PLoco = new byte[c]; PRealm = new byte[c]; PSexual = new byte[c]; PLive = new byte[c]; PParasite = new byte[c]; PDrain = new float[c]; PThirstK = new float[c]; PWaterEcon = new float[c]; PLegPairs = new byte[c];
        PFinPairs = new byte[c]; PEyePairs = new byte[c]; PBrain = new byte[c]; PHerm = new byte[c];
        for (int k = 0; k < K.CLADES; k++) Sp[k] = new Species { Id = k };
        CareLeft = new float[c]; CareStep = new uint[c]; Infect = new float[c]; Immune = new float[c]; IInfNear = new byte[c]; Virulence = new float[c]; IVirNear = new float[c]; Strain = new sbyte[c * G.NMHC]; IStrainSrc = new int[c];
        Genome = new sbyte[(long)c * G.LEN];
        W2f = new float[(long)c * G.W2N];
        Trace = new float[(long)c * G.W2N];
        Hidden = new float[c * G.NH];
        HistA = new float[c * Brain.BLOCK]; HistD = new float[c * Brain.BLOCK * P.N]; HistY = new float[c * Brain.BLOCK * G.NIN];
        Gain = new float[c * P.N];
        Feat = new float[c * F.N];
        IHeading = new float[c]; ISpeed = new float[c]; IActs = new uint[c]; Call = new byte[c]; IHeard = new int[c]; IHeardX = new float[c]; IHeardY = new float[c]; IHeardUser = new byte[c]; ITarget = new int[c]; IMate = new int[c];
        IFoodHere = new float[c]; IKinDensity = new float[c]; IEatPrey = new byte[c]; IScanning = new byte[c];
        FreeStack = new int[c]; Order = new int[c];
        for (int i = 0; i < c; i++) FreeStack[i] = c - 1 - i;
        FreeTop = c;

        CellStart = new int[Map.GW * Map.GW + 1]; CellCount = new int[Map.GW * Map.GW]; Sorted = new int[c]; CellOf = new int[c];

        int n = Map.FW * Map.FW;
        Pool = new float[Pools.NFIELD][];
        for (int p = 0; p < Pools.NFIELD; p++) Pool[p] = new float[n];
        GrassN = new float[n];
        Shrub = new float[n]; Tree = new float[n]; Sapling = new float[n]; Nutrient = new float[n]; Scent = new float[n];
        ShrubN = new float[n]; TreeN = new float[n]; SaplingN = new float[n]; NutrientN = new float[n]; ScentN = new float[n];
        Moisture = new float[n]; Poll = new float[n]; Fire = new float[n]; FireN = new float[n];
        Elev = new float[n]; Depth = new float[n]; MoistBase = new float[n]; Fert = new float[n]; Cell = new byte[n];
        DistWater = new float[n]; DistLand = new float[n]; PondSite = new byte[n]; Wet = new byte[n]; DistDrink = new float[n];

        Archive = new sbyte[NS * K.ARCHIVE * G.LEN];
        ArchiveScore = new float[NS * K.ARCHIVE];
        ArchiveCount = new int[NS];
        Scratch = new sbyte[G.LEN * 2];

        T = new Telemetry(this);
    }

    // ---- small helpers used by every phase
    public static float Wrap(float v) { v %= Map.W; return v < 0 ? v + Map.W : v; }
    public static float Delta1(float d) { if (d > Map.W * 0.5f) d -= Map.W; else if (d < -Map.W * 0.5f) d += Map.W; return d; }
    public static int WrapI(int v, int m) { v %= m; return v < 0 ? v + m : v; }
    public static int FIdx(float x, float y)
    {
        int cx = (int)(x * (1f / K.FCELL)), cy = (int)(y * (1f / K.FCELL));
        if ((uint)cx >= Map.FW) cx = WrapI(cx, Map.FW);
        if ((uint)cy >= Map.FW) cy = WrapI(cy, Map.FW);
        return cy * Map.FW + cx;
    }

    public bool IsAlive(int i) => StageOf[i] != St.Dead;
    public bool IsActive(int i) => StageOf[i] >= St.Juvenile;      // has a body that acts
    public bool Juvenile(int i) => StageOf[i] == St.Juvenile;
    public Loco LocoOf(int i) => (Loco)PLoco[i];
    public Realm RealmOf(int i) => (Realm)PRealm[i];

    /// <summary>Current body mass: juveniles grow from the species' birth mass to their adult mass as they build tissue.</summary>
    public float MassOf(int i)
    {
        byte st = StageOf[i];
        if (st == St.Adult) return PM[i];
        float b = PBirth[i];
        if (st <= St.Embryo) return PM[i] * b;
        return PM[i] * (b + (1f - b) * MathF.Min(1f, Grown[i]));
    }

    /// <summary>Apparent size of one organism.</summary>
    public float ApparentMass(int i) => MassOf(i);

    public float EmaxOf(int i) => K.C_EMAX * MassOf(i);

    /// <summary>Digestive efficiencies of slot i.</summary>
    public Span<float> DietOf(int i) => new Span<float>(PDiet, i * Pools.N, Pools.N);

    public float Carnivory(int i) => PDiet[i * Pools.N + (int)Neuroreef.Pool.Meat];

    /// <summary>Top speed in the medium of a cell: flying, swimming (deep water), wading (shallow) or on land.</summary>
    public float Vmax(int i, int cell)
    {
        float v;
        if (PLoco[i] == (byte)Neuroreef.Loco.Fly) v = PVAir[i];
        else if ((Cell[cell] & (byte)Realm.Deep) != 0) v = PVWater[i];
        else if ((Cell[cell] & (byte)Realm.Shallow) != 0) v = MathF.Max(PVWater[i], 0.6f * PVLand[i]);
        else v = PVLand[i];
        return v * MathF.Max(0.3f, MassOf(i) / PM[i]);
    }

    public float Vmax(int i) => Vmax(i, FIdx(X[i], Y[i]));

    public bool CellAllows(int cell, Realm r) => (Cell[cell] & (byte)r) != 0;

    public float SeasonAt(float y) => MathK.Sin(2f * MathF.PI * (float)(Step % (ulong)Nature.SeasonLength) / Nature.SeasonLength - 2f * MathF.PI * y / Map.W);

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
