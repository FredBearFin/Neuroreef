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
    // ---- agents: path integration (Revision P), written by RESOLVE (own slot): the remembered vector (world units) from the
    // animal to where it last drank and to where it last fed well, kept up to date from its own (noisy) sense of how it moved
    public readonly float[] MemDrinkX, MemDrinkY, MemFedX, MemFedY;
    public readonly byte[] MemHas;         // Mem.Drink: it remembers a place it drank; Mem.Fed: a place it fed well
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
    public readonly float[] PFloor;            // the smallest body its size class allows
    public readonly float[] PVoice;            // voice organ (0..1): loudness of its calls; mute below VOICE_MIN
    public readonly float[] PImit, PImitAge;  // vocal learning (0..1) and its sensitive period (share of lifespan) (Revision N)
    public readonly float[] PWaterEcon;       // water-economy gene expressed (0..1)
    public readonly float[] PInsul, PAppend;  // fur (insulation) and appendages (0..1), for the heat budget (Revision O)
    public readonly float[] PReadyE;
    public readonly float[] PWire;            // scale of its recurrent memory weights: its wiring gain over the raw radius of its genome's (Revision P)          // share of its reserve it needs before breeding (Revision P)
    public readonly float[] PEndo;            // endothermy (0..1): 0 its core follows its surroundings, 1 it is kept at T_BODY (Revision P)
    // ---- agents: body heat (Revision O), written by RESOLVE (own slot)
    public readonly float[] ThermStrain;      // heat strain from shortfalls beyond what the body can make or lose (1 = death by cold or heat)
    public readonly float[] HeatNeed;         // last step's heat need (HEAT_NEED sense): tanh(made - evaporated, in basal units)
    public readonly float[] TOp;              // last step's operative temperature (°C), for telemetry
    public readonly float[] PThirstK;         // how fast it dries out, against C_THIRST (body size and the water-economy gene)           // share of successful strikes that drain blood instead of killing            // parental care level (0 = none; DESIGN §7.8)
    public readonly float[] PBirth, PFovCos, PGest, PCool, PLegs, PFins, PWings, PVLand, PVWater, PVAir, PSexRatio, PImmune;
    public readonly byte[] PLoco, PRealm, PSexual, PLive, PParasite, PLegPairs, PFinPairs, PEyePairs, PHerm;
    public readonly ushort[] PBrain;       // memory units in use (up to G.NH = 256: a byte held at most 255, so 256 read as 0)
    // ---- agents: care and disease (DESIGN §7.8, §9.6)
    public readonly float[] CareLeft;         // care a mother can still give this step
    public readonly uint[] CareStep;          // step + 1 that CareLeft refers to (0 = never)
    // ---- pathogens (Revision R, Pathogens.cs): every infection carries its own pathogen genome. Per host slot:
    public readonly float[] PgLoad;           // pathogen load (0 = not infected; 1 = all a host can hold)
    public readonly byte[] PgGene;            // Cap * PG.N: the genome of the strain it carries (traits 0..255, and its founding lineage)
    public readonly sbyte[] Strain;           // Cap * NMHC: that strain's antigen, matched against host recognition genes (G.B_MHC)
    public readonly float[] PgFit;            // how well that strain fits this host (antigen against its MHC, with the strain's breadth)
    public readonly float[] ImmEff;           // immune effector level now (0 = none)
    public readonly float[] ImmMem;           // strength of its immune memory (0..1), of the antigen in ImmKey
    public readonly sbyte[] ImmKey;           // Cap * NMHC: the antigen of the last infection it cleared
    public readonly float[] PgDamage;         // tissue damage (1 = death by disease)
    public readonly float[] PgShed;           // what it sheds this step to each animal in contact (contact route), read by DECIDE
    public readonly float[] IDose;            // contact dose from shedding animals in contact, from DECIDE (own slot only)
    public readonly int[] IStrainSrc;         // the contact shedding most to it, whose strain it is exposed to, from DECIDE (-1 none)
    public readonly byte[] IInfNear;          // shedding animals in contact, counted by DECIDE (own slot only)
    // per field cell, the environmental reservoir: load, the strain it holds (one per cell), and the river network it flows
    // down (receiver -1 = none; ResOrder lists the ResRivers river cells downstream first). ResNet: that network is built
    public readonly float[] ResLoad;
    public readonly byte[] ResGene;           // FW*FW * PG.N
    public readonly sbyte[] ResKey;           // FW*FW * NMHC
    public readonly int[] ResRecv, ResOrder;
    public int ResRivers; public bool ResNet;
    public readonly float[] PgCentre;         // PG_CLUSTERS * PG.D: strain centres, scratch for telemetry
    // ---- agents: brain
    public readonly sbyte[] Genome;        // Cap * G.LEN
    public readonly float[] W2f;           // Cap * G.W2N   working output weights = genome W2 + what was learned (not inherited)
    public readonly float[] Trace;         // Cap * G.W2N   eligibility traces
    public readonly float[] HistA, HistD, HistY;   // learning block history: advantage, perturbation, inputs (Brain.BLOCK steps)
    public readonly float[] HistV;         // Cap * BLOCK  match of its own utterance to its template, less its running mean (0 when silent; Revision N)
    public readonly float[] VMean;         // running mean of that match
    public readonly byte[] Tmpl;           // Cap * Voice.NTMPL  remembered tutors' syllable pairs: (start or previous sound) -> next sound, counts
    public readonly float[] Hidden;        // Cap * G.NH
    /// <summary>Control experiment (--validate): when true, every animal's recurrent state (Hidden) is zeroed before its
    /// brain runs each step, so it keeps its genome and learned weights but remembers no past activity. Default off.</summary>
    public bool MemoryReset;
    public readonly float[] Gain;          // Cap * P.N     last gains (after exploration)
    public readonly float[] Feat;          // Cap * F.N     last features (for probe/telemetry)
    // ---- agents: intents written by DECIDE (own slot only)
    public readonly float[] IHeading, ISpeed;
    public readonly uint[] IActs;          // bit p-FIRST_ACTION set = action fires
    // voices (Revision M, N): the utterance each organism made this step (MAX_SYL bytes per slot: sounds 1..8, 0 = none),
    // the last utterance it made (whenever), and what each heard: its focal caller's utterance
    public readonly byte[] Call, LastUtt;
    public readonly int[] IHeard;          // 0 nothing; else 1 + focal caller's clade x Voice.NTYPE + utterance type
    public readonly float[] IHeardX, IHeardY;
    public readonly byte[] IHeardUser;     // its focal caller this step was the person
    public readonly int[] IFocal;          // the focal caller's slot (-1 none, -2 the person), written by DECIDE (own slot)
    public readonly uint[] IFocalSerial;   // and its serial, so attention stays with the same animal
    public readonly float[] CallR;         // how far its call carries this step (BUILD; 0 = silent)
    public readonly int[] CallStart, CallSorted;   // this step's callers in hash-cell order (BUILD)
    // the person speaking into the world (the console's translator): one utterance at a time, heard on the next step
    public float UserCallX, UserCallY; public ulong UserCallStep = ulong.MaxValue; public int UserCallId;
    public readonly byte[] UserCall = new byte[F.MAX_SYL];   // its sounds (1..8, 0 = none)
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
    public readonly float[] Temp;          // air temperature (°C), written by FIELDS every step (Revision O); water is at least T_WATER_MIN under it
    public float[] Fire, FireN;
    // ---- static terrain
    public readonly float[] Elev, Depth, MoistBase, Fert;
    public readonly byte[] Cell;           // Realm bits of the cell (Land, Shallow, Deep)
    public readonly float[] DistWater, DistLand;   // in cells
    public readonly byte[] PondSite;               // 1 = a basin that can hold a seasonal pond (Revision K)
    public readonly byte[] Wet;                    // drinkable water now: 1 = lake, sea or river, 2 = a pond holding water or a spring, 3 = a flooded floodplain (Ponds.Update)
    public readonly float[] DistDrink;             // cells to the nearest drinkable water (lakes, full ponds and floods)
    // hydrology (Revision Q), fixed at Terrain.Build: the rain each cell gathers from above (in cells of mean rain),
    // the depth of alluvium left on it, and the wetness (season x year) at which its river floods it (+inf: never)
    public readonly float[] Flow, Alluvium, FloodWet;
    public readonly int[] FloodFrom;               // the river cell that floods it (-1: none)
    // geology (Revision R), fixed at Terrain.Build: the kind of rock at the surface (Rock.Crystalline, Volcanic, Sedimentary)
    public readonly byte[] Rock;
    // climate (Revision O), fixed at Terrain.Build: cooling by height x climate strength (K), continentality (0..1)
    public readonly float[] TLapse, Cont;
    public float ClimateZ = 1f;                    // normaliser of the evaporation factor: the land as moist over a year as with no climate
    public float SeaLevel;
    // ---- water cycle (WaterCycle.cs): stores in mm of water over a cell, fluxes in mm x cells per update (WC_EVERY steps).
    // Fixed at WaterCycle.Init: the rain pattern (relative to the land mean; a lake cell holds its row's land mean), soil
    // capacity, the drainage receivers and their topological order (outlets first), the lake each cell drains to
    // finally and the lake (connected open water below lake level) each lake cell belongs to, with its area and depth.
    public readonly float[] Rain, SoilCap;
    public readonly int[] Recv, DrainOrder, Outlet, LakeOf;
    public readonly float[] LakeArea, LakeDepth, LakeQm, LakeGross;   // per lake id: cells, deepest water (mm), mean and this update's gross inflow (rivers + rain)
    public int LakeCount;
    // changing: soil water, snowpack, shallow (local) and deep (regional) groundwater, pond water, this update's surface runoff, discharge leaving each cell,
    // its slow mean, groundwater emerging at the surface this update; each lake's level against its level at creation (mm)
    public readonly float[] SoilWater, Snow, Ground, Deep, PondWater, Runoff, Discharge, DischargeMean, Seep, LakeLevel, LakeIn;
    public readonly double[] WcRow;               // per-row sums of one update's fluxes (parallel rows, summed in row order)
    public bool WaterOn;                          // WaterCycle.Init has run: moisture, ponds and floods come from the water cycle
    // whole-run water budget of the land (every cell not a lake), mm x cells: rain in; out by evapotranspiration, by pond
    // evaporation and into the lakes. Lakes: rain, inflow, evaporation, spill. Reset at the end of Init.
    public double WbRain, WbET, WbPondEvap, WbToLakes, WbLakeRain, WbLakeEvap, WbLakeSpill;
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
    public const int BytesPerSlot = G.LEN + 2 * G.W2N * 4 + Brain.BLOCK * (2 + P.N + G.NIN) * 4 + (G.NH + P.N + F.N + Pools.N + G.NMHC) * 4 + 432
        + Voice.NTMPL + 2 * F.MAX_SYL + 6 * 4    // Revision N: template, utterances, focal caller, call range and list, match mean, learning genes
        + 5 * 4                                  // Revision O: fur, appendages, heat strain, heat need, operative temperature
        + 4 * 4 + 1                              // Revision P: remembered drink and feeding vectors
        + PG.N + G.NMHC + 4 * 4;                 // Revision R: pathogen genome, immune memory key, 8 floats where 4 were
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
        MemDrinkX = new float[c]; MemDrinkY = new float[c]; MemFedX = new float[c]; MemFedY = new float[c]; MemHas = new byte[c];
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
        PLoco = new byte[c]; PRealm = new byte[c]; PSexual = new byte[c]; PLive = new byte[c]; PParasite = new byte[c]; PDrain = new float[c]; PThirstK = new float[c]; PWaterEcon = new float[c]; PVoice = new float[c]; PFloor = new float[c]; PImit = new float[c]; PImitAge = new float[c]; PLegPairs = new byte[c];
        PInsul = new float[c]; PAppend = new float[c]; PEndo = new float[c]; PReadyE = new float[c]; PWire = new float[c]; ThermStrain = new float[c]; HeatNeed = new float[c]; TOp = new float[c]; Array.Fill(TOp, K.T_REF);
        PFinPairs = new byte[c]; PEyePairs = new byte[c]; PBrain = new ushort[c]; PHerm = new byte[c];
        for (int k = 0; k < K.CLADES; k++) Sp[k] = new Species { Id = k };
        CareLeft = new float[c]; CareStep = new uint[c];
        PgLoad = new float[c]; PgGene = new byte[c * PG.N]; Strain = new sbyte[c * G.NMHC]; PgFit = new float[c]; ImmEff = new float[c]; ImmMem = new float[c];
        ImmKey = new sbyte[c * G.NMHC]; PgDamage = new float[c]; PgShed = new float[c]; IDose = new float[c]; IStrainSrc = new int[c]; IInfNear = new byte[c];
        PgCentre = new float[K.PG_CLUSTERS * PG.D];
        Genome = new sbyte[(long)c * G.LEN];
        W2f = new float[(long)c * G.W2N];
        Trace = new float[(long)c * G.W2N];
        Hidden = new float[c * G.NH];
        HistA = new float[c * Brain.BLOCK]; HistV = new float[c * Brain.BLOCK]; VMean = new float[c]; Tmpl = new byte[c * Voice.NTMPL]; HistD = new float[c * Brain.BLOCK * P.N]; HistY = new float[c * Brain.BLOCK * G.NIN];
        Gain = new float[c * P.N];
        Feat = new float[c * F.N];
        IHeading = new float[c]; ISpeed = new float[c]; IActs = new uint[c]; Call = new byte[c * F.MAX_SYL]; LastUtt = new byte[c * F.MAX_SYL]; IFocal = new int[c]; IFocalSerial = new uint[c]; CallR = new float[c]; CallSorted = new int[c]; IHeard = new int[c]; IHeardX = new float[c]; IHeardY = new float[c]; IHeardUser = new byte[c]; ITarget = new int[c]; IMate = new int[c];
        IFoodHere = new float[c]; IKinDensity = new float[c]; IEatPrey = new byte[c]; IScanning = new byte[c];
        FreeStack = new int[c]; Order = new int[c];
        for (int i = 0; i < c; i++) FreeStack[i] = c - 1 - i;
        FreeTop = c;

        CellStart = new int[Map.GW * Map.GW + 1]; CallStart = new int[Map.GW * Map.GW + 1]; CellCount = new int[Map.GW * Map.GW]; Sorted = new int[c]; CellOf = new int[c];

        int n = Map.FW * Map.FW;
        Pool = new float[Pools.NFIELD][];
        for (int p = 0; p < Pools.NFIELD; p++) Pool[p] = new float[n];
        GrassN = new float[n];
        Shrub = new float[n]; Tree = new float[n]; Sapling = new float[n]; Nutrient = new float[n]; Scent = new float[n];
        ShrubN = new float[n]; TreeN = new float[n]; SaplingN = new float[n]; NutrientN = new float[n]; ScentN = new float[n];
        Moisture = new float[n]; Poll = new float[n]; Fire = new float[n]; FireN = new float[n];
        Temp = new float[n]; Array.Fill(Temp, K.T_REF); TLapse = new float[n]; Cont = new float[n];
        Elev = new float[n]; Depth = new float[n]; MoistBase = new float[n]; Fert = new float[n]; Cell = new byte[n];
        DistWater = new float[n]; DistLand = new float[n]; PondSite = new byte[n]; Wet = new byte[n]; DistDrink = new float[n];
        ResLoad = new float[n]; ResGene = new byte[n * PG.N]; ResKey = new sbyte[n * G.NMHC]; ResRecv = new int[n]; ResOrder = new int[n];
        Flow = new float[n]; Alluvium = new float[n]; FloodWet = new float[n]; Array.Fill(FloodWet, float.PositiveInfinity); FloodFrom = new int[n]; Array.Fill(FloodFrom, -1);
        Rock = new byte[n];
        Rain = new float[n]; SoilCap = new float[n]; Recv = new int[n]; DrainOrder = new int[n]; Outlet = new int[n]; LakeOf = new int[n];
        LakeArea = new float[n]; LakeDepth = new float[n]; LakeQm = new float[n]; LakeGross = new float[n];
        SoilWater = new float[n]; Snow = new float[n]; Ground = new float[n]; Deep = new float[n]; PondWater = new float[n]; Runoff = new float[n];
        Discharge = new float[n]; DischargeMean = new float[n]; Seep = new float[n]; LakeLevel = new float[n]; LakeIn = new float[n];
        WcRow = new double[Map.FW * WaterCycle.NSUM];

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

    /// <summary>Top speed in the cell's water depth, blended from wading to swimming.</summary>
    public float Vmax(int i, int cell)
    {
        float v;
        if (PLoco[i] == (byte)Neuroreef.Loco.Fly) v = PVAir[i];
        else if ((Cell[cell] & (byte)Realm.Water) != 0)
        {
            float depth = MathF.Max(0f, Depth[cell]);
            float wade = MathF.Max(PVWater[i], 0.6f * PVLand[i]);
            float mix = Math.Clamp(depth / K.SHALLOW, 0f, 1f);
            v = wade + (PVWater[i] - wade) * mix;
            float maxDepth = MaxWaterDepth(i);
            if (maxDepth > K.SHALLOW)
            {
                float load = Math.Clamp((depth - K.SHALLOW) / (maxDepth - K.SHALLOW), 0f, 1f);
                v *= 1f - 0.5f * load;
            }
        }
        else v = PVLand[i];
        return v * MathF.Max(0.3f, MassOf(i) / PM[i]);
    }

    public float Vmax(int i) => Vmax(i, FIdx(X[i], Y[i]));

    public bool CellAllows(int cell, Realm r) => (Cell[cell] & (byte)r) != 0;

    public float MaxWaterDepth(int i) => K.SHALLOW * (1f + K.SWIM_DEPTH_RANGE * PFins[i]);

    public bool CellAllows(int i, int cell) => CellAllows(i, cell, RealmOf(i));

    public bool CellAllows(int i, int cell, Realm realm)
    {
        byte kind = Cell[cell];
        if ((kind & (byte)Realm.Land) != 0) return (realm & Realm.Land) != 0;
        if ((kind & (byte)Realm.Water) == 0 || (realm & Realm.Water) == 0) return false;
        return PLoco[i] == (byte)Loco.Fly || Depth[cell] <= MaxWaterDepth(i);
    }

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

/// <summary>Bits of World.MemHas: what an animal remembers the way back to (Revision P).</summary>
public static class Mem
{
    public const byte Drink = 1, Fed = 2;
}
