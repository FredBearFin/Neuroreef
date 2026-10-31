// The whole simulation state (DESIGN §3). Every array is allocated here, in the constructor, and
// nowhere else (NR-101, NR-102), except that the per-slot arrays ([PerSlot]) are reallocated when the world
// grows (Resize: there is no fixed animal cap). Animal state is struct-of-arrays, indexed by slot.

using System;

namespace Neuroreef;

public sealed class World
{
    /// <summary>Animal slots now. Not a limit: the world grows (Resize) when births need more. Set only by Resize.</summary>
    public int Cap;
    public readonly uint Seed;
    public readonly Species[] Sp = new Species[K.CLADES];   // clade slots (DESIGN §7.9)
    public readonly int NS = K.CLADES;
    public ulong Step;

    // ---- agents: kinematics
    [PerSlot] public float[] X, Y, Heading, Speed;
    // ---- agents: physiology
    [PerSlot] public float[] Energy, Gut, GutEnergy, GutWaste, GutSeedy, Thirst, Fatigue, Age, Pain, Cooldown, Timer;
    [PerSlot] public float[] Grown;         // share of its growable tissue a juvenile has built (0 at birth, 1 = adult size; DESIGN §9.1)
    [PerSlot] public float[] Reward, RewardMean, PendingR, LastFear;
    // ---- agents: path integration (Revision P), written by RESOLVE (own slot): the remembered vector (world units) from the
    // animal to where it last drank and to where it last fed well, kept up to date from its own (noisy) sense of how it moved
    [PerSlot] public float[] MemDrinkX, MemDrinkY, MemFedX, MemFedY;
    [PerSlot] public byte[] MemHas;         // Mem.Drink: it remembers a place it drank; Mem.Fed: a place it fed well
    // ---- agents: identity
    [PerSlot] public byte[] Spc, StageOf, Sex, Flags;
    [PerSlot] public ushort[] Gen;
    [PerSlot] public uint[] Serial, MotherSerial;
    [PerSlot] public int[] MotherSlot, Children, LastChildSlot, Matured;
    [PerSlot] public uint[] LastChildSerial;
    // ---- agents: phenotype, expressed from the body genes at creation (Body.Express)
    [PerSlot] public float[] PM, PBasal, PGut, PDig, PBite, PLife, PMature, PSight, PReach, PTurn, PVmax, PArmour, PStrike;
    [PerSlot] public byte[] PLitter;
    [PerSlot] public float[] PDiet;            // Cap * Pools.N digestive efficiencies
    [PerSlot] public float[] PCare;
    [PerSlot] public float[] PDrain;
    [PerSlot] public float[] PFloor;            // the smallest body its size class allows
    [PerSlot] public float[] PVoice;            // voice organ (0..1): loudness of its calls; mute below VOICE_MIN
    [PerSlot] public float[] PImit, PImitAge;  // vocal learning (0..1) and its sensitive period (share of lifespan) (Revision N)
    [PerSlot] public float[] PWaterEcon;       // water-economy gene expressed (0..1)
    [PerSlot] public float[] PInsul, PAppend;  // fur (insulation) and appendages (0..1), for the heat budget (Revision O)
    [PerSlot] public float[] PReadyE;
    [PerSlot] public float[] PWire;            // scale of its recurrent memory weights: its wiring gain over the raw radius of its genome's (Revision P)          // share of its reserve it needs before breeding (Revision P)
    [PerSlot] public float[] PEndo;            // endothermy (0..1): 0 its core follows its surroundings, 1 it is kept at T_BODY (Revision P)
    // ---- agents: body heat (Revision O), written by RESOLVE (own slot)
    [PerSlot] public float[] ThermStrain;      // heat strain from shortfalls beyond what the body can make or lose (1 = death by cold or heat)
    [PerSlot] public float[] HeatNeed;         // last step's heat need (HEAT_NEED sense): tanh(made - evaporated, in basal units)
    [PerSlot] public float[] TOp;              // last step's operative temperature (°C), for telemetry
    [PerSlot] public float[] PThirstK;         // how fast it dries out, against C_THIRST (body size and the water-economy gene)           // share of successful strikes that drain blood instead of killing            // parental care level (0 = none; DESIGN §7.8)
    [PerSlot] public float[] PBirth, PFovCos, PGest, PCool, PLegs, PFins, PWings, PVLand, PVWater, PVAir, PSexRatio, PImmune;
    [PerSlot] public byte[] PLoco, PRealm, PSexual, PLive, PParasite, PLegPairs, PFinPairs, PEyePairs, PHerm;
    [PerSlot] public ushort[] PBrain;       // memory units in use (up to G.NH = 256: a byte held at most 255, so 256 read as 0)
    // ---- agents: care and disease (DESIGN §7.8, §9.6)
    [PerSlot] public float[] CareLeft;         // care a mother can still give this step
    [PerSlot] public uint[] CareStep;          // step + 1 that CareLeft refers to (0 = never)
    // ---- pathogens (Revision R, Pathogens.cs): every infection carries its own pathogen genome. Per host slot:
    [PerSlot] public float[] PgLoad;           // pathogen load (0 = not infected; 1 = all a host can hold)
    [PerSlot] public byte[] PgGene;            // Cap * PG.N: the genome of the strain it carries (traits 0..255, and its founding lineage)
    [PerSlot] public sbyte[] Strain;           // Cap * NMHC: that strain's antigen, matched against host recognition genes (G.B_MHC)
    [PerSlot] public float[] PgFit;            // how well that strain fits this host (antigen against its MHC, with the strain's breadth)
    [PerSlot] public float[] ImmEff;           // immune effector level now (0 = none)
    [PerSlot] public float[] ImmMem;           // strength of its immune memory (0..1), of the antigen in ImmKey
    [PerSlot] public sbyte[] ImmKey;           // Cap * NMHC: the antigen of the last infection it cleared
    [PerSlot] public float[] PgDamage;         // tissue damage (1 = death by disease)
    [PerSlot] public float[] PgShed;           // what it sheds this step to each animal in contact (contact route), read by DECIDE
    [PerSlot] public float[] IDose;            // contact dose from shedding animals in contact, from DECIDE (own slot only)
    [PerSlot] public int[] IStrainSrc;         // the contact shedding most to it, whose strain it is exposed to, from DECIDE (-1 none)
    [PerSlot] public byte[] IInfNear;          // shedding animals in contact, counted by DECIDE (own slot only)
    // per field cell, the environmental reservoir: load, the strain it holds (one per cell), and the river network it flows
    // down (receiver -1 = none; ResOrder lists the ResRivers river cells downstream first). ResNet: that network is built
    public readonly float[] ResLoad;
    public readonly byte[] ResGene;           // FW*FW * PG.N
    public readonly sbyte[] ResKey;           // FW*FW * NMHC
    public readonly int[] ResRecv, ResOrder;
    public int ResRivers; public bool ResNet;
    public readonly float[] PgCentre;         // PG_CLUSTERS * PG.D: strain centres, scratch for telemetry
    // ---- agents: brain
    [PerSlot] public readonly SlotArray<sbyte> Genome;   // G.LEN per slot (paged: no one array holds every genome)
    [PerSlot] public readonly SlotArray<float> W2f;      // G.W2N per slot: working output weights = genome W2 + what was learned (not inherited)
    [PerSlot] public readonly SlotArray<float> Trace;    // G.W2N per slot: eligibility traces
    [PerSlot] public float[] HistA, HistD, HistY;   // learning block history: advantage, perturbation, inputs (Brain.BLOCK steps)
    [PerSlot] public float[] HistV;         // Cap * BLOCK  match of its own utterance to its template, less its running mean (0 when silent; Revision N)
    [PerSlot] public float[] VMean;         // running mean of that match
    [PerSlot] public byte[] Tmpl;           // Cap * Voice.NTMPL  remembered tutors' syllable pairs: (start or previous sound) -> next sound, counts
    [PerSlot] public float[] Hidden;        // Cap * G.NH
    /// <summary>Control experiment (--validate): when true, every animal's recurrent state (Hidden) is zeroed before its
    /// brain runs each step, so it keeps its genome and learned weights but remembers no past activity. Default off.</summary>
    public bool MemoryReset;
    [PerSlot] public float[] Gain;          // Cap * P.N     last gains (after exploration)
    [PerSlot] public float[] Feat;          // Cap * F.N     last features (for probe/telemetry)
    // ---- agents: intents written by DECIDE (own slot only)
    [PerSlot] public float[] IHeading, ISpeed;
    [PerSlot] public uint[] IActs;          // bit p-FIRST_ACTION set = action fires
    // voices (Revision M, N): the utterance each organism made this step (MAX_SYL bytes per slot: sounds 1..8, 0 = none),
    // the last utterance it made (whenever), and what each heard: its focal caller's utterance
    [PerSlot] public byte[] Call, LastUtt;
    [PerSlot] public int[] IHeard;          // 0 nothing; else 1 + focal caller's clade x Voice.NTYPE + utterance type
    [PerSlot] public float[] IHeardX, IHeardY;
    [PerSlot] public byte[] IHeardUser;     // its focal caller this step was the person
    [PerSlot] public int[] IFocal;          // the focal caller's slot (-1 none, -2 the person), written by DECIDE (own slot)
    [PerSlot] public uint[] IFocalSerial;   // and its serial, so attention stays with the same animal
    [PerSlot] public float[] CallR;         // how far its call carries this step (BUILD; 0 = silent)
    public readonly int[] CallStart;              // this step's callers in hash-cell order (BUILD): per hash cell, where its callers start
    [PerSlot] public int[] CallSorted;
    // the person speaking into the world (the console's translator): one utterance at a time, heard on the next step
    public float UserCallX, UserCallY; public ulong UserCallStep = ulong.MaxValue; public int UserCallId;
    public readonly byte[] UserCall = new byte[F.MAX_SYL];   // its sounds (1..8, 0 = none)
    [PerSlot] public int[] ITarget, IMate;  // target slot for STRIKE, partner slot for MATE (-1 none)
    [PerSlot] public float[] IFoodHere, IKinDensity;
    [PerSlot] public byte[] IEatPrey;       // EAT means attacking the target in reach (it is worth more than the plants here)
    [PerSlot] public byte[] IScanning;

    // ---- slot management. Free slots are a stack: a death pushes its slot, a birth takes the top. Below what was ever
    // freed lie the slots never used, the lowest on top (Cap - 1 at the bottom), so growing the world adds its new slots
    // at the bottom and no birth takes a slot other than one a bigger world would have given it (Resize)
    [PerSlot] public int[] FreeStack; public int FreeTop;
    public uint NextSerial = 1;
    public int AliveCount;
    /// <summary>Slots ever taken: every slot in use is below it. RESOLVE visits [0, power of two above it) (Simulation).</summary>
    public int SlotsUsed;
    /// <summary>The most slots this world may grow to (World.MaxSlots unless the run sets a hard limit, --max-capacity).
    /// Not state: a run setting, so a save does not keep it.</summary>
    [NonSerialized] public int MaxCap = MaxSlots;
    /// <summary>Times this world grew, and why it last could not (null: it never had to stop). Not state.</summary>
    [NonSerialized] public int Grows;
    [NonSerialized] public string? GrowLimit;

    // ---- spatial hash (counting sort)
    public readonly int[] CellStart, CellCount;
    [PerSlot] public int[] Sorted, CellOf;

    // ---- fields (DESIGN §10): food pools 0..12, then structure layers
    public readonly float[][] Pool;        // [Pools.NFIELD][FW*FW]
    public readonly float[] Shrub, Tree, Sapling;   // cover of the shrub, tree and sapling slots (grass = the herb slot, Pool.Grass), updated in place by Plants
    public float[] Nutrient, Scent;
    public float[] NutrientN, ScentN;   // double buffers
    // ---- plants (Revision S, Plants.cs): per land cell the genotype of each slot (herb, shrub, tree, sapling; 0 = empty),
    // per cell and stratum (herb, shrub, canopy) the seed bank's mass and its seed and pollen parents, the seed released
    // this epoch to each neighbour and its parents, the genotype of the fruit, pollen animals left, and leaf-on bits
    public readonly ushort[] POcc, BankMom, BankDad, SeedMom, SeedDad, FruitG, PollenDad;
    public readonly float[] BankM, SeedOut;
    public readonly byte[] PFlags;
    // long-distance seed: per block of WIND_BLOCK^2 cells and stratum, what lands this epoch and what gathers for the next,
    // and per row its share of the gathering (a row's own, so rows run in parallel)
    public readonly float[] WindAcc, WindDist, RowFar, RowFarBest;
    public readonly ushort[] WindAccMom, WindAccDad, WindDistMom, WindDistDad, RowFarMom, RowFarDad;
    public readonly PlantTable Flora;
    // per animal: the seed in its gut and coat (seed and pollen parent) and the pollen it carries
    [PerSlot] public ushort[] GutSeedG, GutSeedDad, FurSeedG, FurSeedDad, PollenG;
    [PerSlot] public float[] FurSeed;
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
    public float TreeMean;                 // mean tree cover of land cells (refreshed every 32 steps; telemetry)

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
    /// <summary>Approximate memory per agent slot (bytes), for the setup screen's estimate (SlotBytes measures it).</summary>
    public const int BytesPerSlot = G.LEN + 2 * G.W2N * 4 + Brain.BLOCK * (2 + P.N + G.NIN) * 4 + (G.NH + P.N + F.N + Pools.N + G.NMHC) * 4 + 432
        + Voice.NTMPL + 2 * F.MAX_SYL + 6 * 4    // Revision N: template, utterances, focal caller, call range and list, match mean, learning genes
        + 5 * 4                                  // Revision O: fur, appendages, heat strain, heat need, operative temperature
        + 4 * 4 + 1                              // Revision P: remembered drink and feeding vectors
        + PG.N + G.NMHC + 4 * 4                  // Revision R: pathogen genome, immune memory key, 8 floats where 4 were
        + 5 * 2 + 4                              // Revision S: seed in gut and coat, pollen
        + Body.NLOOK + 4 + 64;                   // derived (World.D): look signature, MHC diversity, packed neighbour
    public readonly uint TerrainSeed;          // branches of one prehistory share their land

    // ---- telemetry
    public readonly Telemetry T;

    /// <summary>What is derived from other state for speed, not state of its own (so a checkpoint does not save it):
    /// written by Body.Express and BUILD, refreshed from the genomes by Derived.Refresh after a load.</summary>
    public readonly Derived D;

    /// <summary>The most slots a world can have: its largest plain per-slot array (the learning inputs, HistY) is indexed
    /// by int (1.6 million slots, ~350 GB). The genomes, weights and traces are paged (SlotArray) and set no limit.</summary>
    public static int MaxSlots => int.MaxValue / (Brain.BLOCK * G.NIN) / 1024 * 1024;

    public World(int capacity, uint seed, uint terrainSeed = 0)
    {
        if (capacity < 1 || capacity > MaxSlots) throw new ArgumentOutOfRangeException(nameof(capacity), $"1 to {MaxSlots:N0} slots per world");
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
        Genome = new SlotArray<sbyte>(G.LEN, c);
        W2f = new SlotArray<float>(G.W2N, c);
        Trace = new SlotArray<float>(G.W2N, c);
        Hidden = new float[c * G.NH];
        HistA = new float[c * Brain.BLOCK]; HistV = new float[c * Brain.BLOCK]; VMean = new float[c]; Tmpl = new byte[c * Voice.NTMPL]; HistD = new float[c * Brain.BLOCK * P.N]; HistY = new float[c * Brain.BLOCK * G.NIN];
        Gain = new float[c * P.N];
        Feat = new float[c * F.N];
        IHeading = new float[c]; ISpeed = new float[c]; IActs = new uint[c]; Call = new byte[c * F.MAX_SYL]; LastUtt = new byte[c * F.MAX_SYL]; IFocal = new int[c]; IFocalSerial = new uint[c]; CallR = new float[c]; CallSorted = new int[c]; IHeard = new int[c]; IHeardX = new float[c]; IHeardY = new float[c]; IHeardUser = new byte[c]; ITarget = new int[c]; IMate = new int[c];
        IFoodHere = new float[c]; IKinDensity = new float[c]; IEatPrey = new byte[c]; IScanning = new byte[c];
        FreeStack = new int[c];
        for (int i = 0; i < c; i++) FreeStack[i] = c - 1 - i;
        FreeTop = c;

        CellStart = new int[Map.GW * Map.GW + 1]; CallStart = new int[Map.GW * Map.GW + 1]; CellCount = new int[Map.GW * Map.GW]; Sorted = new int[c]; CellOf = new int[c];

        int n = Map.FW * Map.FW;
        Pool = new float[Pools.NFIELD][];
        for (int p = 0; p < Pools.NFIELD; p++) Pool[p] = new float[n];
        Shrub = new float[n]; Tree = new float[n]; Sapling = new float[n]; Nutrient = new float[n]; Scent = new float[n];
        NutrientN = new float[n]; ScentN = new float[n];
        POcc = new ushort[4 * n]; BankMom = new ushort[3 * n]; BankDad = new ushort[3 * n]; SeedMom = new ushort[3 * n]; SeedDad = new ushort[3 * n];
        FruitG = new ushort[n]; PollenDad = new ushort[n]; BankM = new float[3 * n]; SeedOut = new float[3 * n]; PFlags = new byte[n];
        int nb = 3 * Plants.Blocks, nr = 3 * Map.FW * ((Map.FW + K.WIND_BLOCK - 1) / K.WIND_BLOCK);
        WindAcc = new float[nb]; WindDist = new float[nb]; WindAccMom = new ushort[nb]; WindAccDad = new ushort[nb]; WindDistMom = new ushort[nb]; WindDistDad = new ushort[nb];
        RowFar = new float[nr]; RowFarBest = new float[nr]; RowFarMom = new ushort[nr]; RowFarDad = new ushort[nr];
        Flora = new PlantTable(Map.FW);
        GutSeedG = new ushort[c]; GutSeedDad = new ushort[c]; FurSeedG = new ushort[c]; FurSeedDad = new ushort[c]; PollenG = new ushort[c]; FurSeed = new float[c];
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
        D = new Derived(c);
    }

    // ---- growth (2026-10-31): no fixed cap. A world grows when births need slots, limited only by memory (or a hard
    // limit the run sets, MaxCap). Growing changes nothing in the run: the new slots are what a world built bigger has
    // there (zeros, the free stack's untouched bottom), and no slot is chosen by capacity (the free stack gives the same
    // slot at any size; RESOLVE's order depends on SlotsUsed, not Cap). So a seed gives the same run from any start size.

    /// <summary>The last step refused a birth for want of a slot: the world is full, at its limit (GrowLimit says which).</summary>
    public bool FullNow => T.LastFullStep != ulong.MaxValue && T.LastFullStep + 1 >= Step;

    /// <summary>Between steps (Simulation.Step) the world keeps at least this many slots free, and an eighth of its slots.</summary>
    public const int MIN_FREE = 256;
    /// <summary>A world grows to 3/2 of its slots (at least what is needed), in whole blocks of GROW_BLOCK.</summary>
    public const int GROW_BLOCK = 1024;
    /// <summary>Memory kept free of slots: an eighth of what the machine (or container) gives the process, at least 1 GB.</summary>
    public static long MemoryReserve(long available) => Math.Max(available / 8, 1L << 30);

    static long _slotBytes;
    /// <summary>Memory one slot takes: every per-slot array of the world and of its derived cache (bytes).</summary>
    public long SlotBytes => _slotBytes != 0 ? _slotBytes : _slotBytes = PerSlot.BytesPerSlot(this, Cap) + PerSlot.BytesPerSlot(D, Cap);

    /// <summary>Between steps: if fewer than MIN_FREE (or Cap / 8) slots are free, grow, so that the next step's births find
    /// slots without growing in the middle of the step (which Life.NewSlot does if a step needs more).</summary>
    public void Reserve()
    {
        int want = Math.Max(MIN_FREE, Cap / 8);
        if (FreeTop < want) Grow(want - FreeTop);
    }

    /// <summary>
    /// Grow by at least need slots: to 3/2 of Cap or more, as far as MaxCap and memory allow (the machine's memory less
    /// MemoryReserve, against what the process holds). True if there are need more slots. Call only when no parallel job
    /// runs (between steps, or from the serial RESOLVE and founding).
    /// </summary>
    public bool Grow(int need)
    {
        long target = Math.Max(Cap + (long)Cap / 2, (long)Cap + need);
        target = (target + GROW_BLOCK - 1) / GROW_BLOCK * GROW_BLOCK;
        int limit = Math.Min(MaxCap, MaxSlots);
        if (target > limit)
        {
            target = limit;
            GrowLimit = MaxCap < MaxSlots ? $"its hard limit of {MaxCap:N0} slots (--max-capacity)" : $"the most slots one world can index ({MaxSlots:N0})";
        }
        if (target > Cap)
        {
            long afford = Cap + MemoryFree(false) / SlotBytes;
            if (afford < target) afford = Cap + MemoryFree(true) / SlotBytes;   // collect first: the old copies of the last growth may still count
            if (afford < target)
            {
                target = Math.Max(Cap, afford);
                var info = GC.GetGCMemoryInfo();
                GrowLimit = $"the memory limit ({Cap:N0} slots of {SlotBytes / 1024:N0} KB; {GC.GetTotalMemory(false) / (1 << 20):N0} MB in use of " +
                    $"{info.TotalAvailableMemoryBytes / (1 << 20):N0} MB available, {MemoryReserve(info.TotalAvailableMemoryBytes) / (1 << 20):N0} MB kept free)";
            }
        }
        if (target > Cap) Resize((int)target);
        return FreeTop >= need;
    }

    /// <summary>Memory the slots may still take (bytes, may be negative).</summary>
    static long MemoryFree(bool collect)
    {
        long avail = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return avail - MemoryReserve(avail) - GC.GetTotalMemory(collect);
    }

    /// <summary>Grow to newCap slots: every per-slot array is reallocated and its slots kept; the new slots are as a world
    /// built with newCap slots has them, and go to the bottom of the free stack (the slots never used, lowest on top).</summary>
    public void Resize(int newCap)
    {
        if (newCap <= Cap) throw new ArgumentOutOfRangeException(nameof(newCap), $"a world grows: {newCap:N0} slots is not more than {Cap:N0}");
        if (newCap > MaxSlots) throw new ArgumentOutOfRangeException(nameof(newCap), $"at most {MaxSlots:N0} slots per world");
        int old = Cap, add = newCap - old;
        PerSlot.Resize(this, old, newCap);
        PerSlot.Resize(D, old, newCap);
        Array.Fill(TOp, K.T_REF, old, add);
        Array.Copy(FreeStack, 0, FreeStack, add, FreeTop);
        for (int k = 0; k < add; k++) FreeStack[k] = newCap - 1 - k;
        FreeTop += add;
        Grows++;
        System.Threading.Volatile.Write(ref Cap, newCap);   // last: a reader that sees the new Cap sees the new arrays
    }

    /// <summary>Hold exactly cap slots, before a save of that size is loaded into this world (Checkpoint): every slot's
    /// state comes from the save, so what the world held is not kept.</summary>
    internal void SetCapacityForLoad(int cap)
    {
        if (cap == Cap) return;
        if (cap < 1 || cap > MaxSlots) throw new System.IO.InvalidDataException($"a saved world of {cap:N0} slots (this program holds 1 to {MaxSlots:N0})");
        int old = Cap;
        PerSlot.Resize(this, old, cap);
        PerSlot.Resize(D, old, cap);
        Array.Fill(TOp, K.T_REF);
        for (int i = 0; i < cap; i++) FreeStack[i] = cap - 1 - i;
        FreeTop = cap; AliveCount = 0; SlotsUsed = 0;
        Array.Clear(StageOf);
        System.Threading.Volatile.Write(ref Cap, cap);
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

    /// <summary>Top speed in the cell: a flier's top air speed, otherwise its speed on the ground there (GroundVmax).</summary>
    public float Vmax(int i, int cell)
        => (PLoco[i] == (byte)Neuroreef.Loco.Fly ? PVAir[i] : GroundSpeed(i, cell)) * MathF.Max(0.3f, MassOf(i) / PM[i]);

    /// <summary>Top speed on the ground in this cell, the young's smallness included: walking on land, blended from
    /// wading to swimming in water. A landed flier walks at it (2026-10-31).</summary>
    public float GroundVmax(int i, int cell) => GroundSpeed(i, cell) * MathF.Max(0.3f, MassOf(i) / PM[i]);

    float GroundSpeed(int i, int cell)
    {
        if ((Cell[cell] & (byte)Realm.Water) != 0)
        {
            float depth = MathF.Max(0f, Depth[cell]);
            float wade = MathF.Max(PVWater[i], 0.6f * PVLand[i]);
            float mix = Math.Clamp(depth / K.SHALLOW, 0f, 1f);
            float v = wade + (PVWater[i] - wade) * mix;
            float maxDepth = MaxWaterDepth(i);
            if (maxDepth > K.SHALLOW)
            {
                float load = Math.Clamp((depth - K.SHALLOW) / (maxDepth - K.SHALLOW), 0f, 1f);
                v *= 1f - 0.5f * load;
            }
            return v;
        }
        return PVLand[i] * Footing(i, cell);
    }

    /// <summary>
    /// Walking speed kept on this ground (2026-10-31): rough or sloping ground (the largest rise to a neighbouring cell,
    /// ROUGH_REF fully rough) slows a body by 1 / (1 + ROUGH_SLOW x rough x M^(-1/3) x (1 - stability)): obstacles are
    /// bigger against a small body's legs, and many legs keep it statically stable (LEG_STAB: Ting, Blickhan & Full 1994).
    /// </summary>
    public float Footing(int i, int cell)
    {
        int fw = Map.FW, cx = cell % fw, cy = cell / fw;
        float e = Elev[cell];
        float d = MathF.Max(MathF.Max(MathF.Abs(Elev[cy * fw + (cx + 1) % fw] - e), MathF.Abs(Elev[cy * fw + (cx + fw - 1) % fw] - e)),
                            MathF.Max(MathF.Abs(Elev[(cy + 1) % fw * fw + cx] - e), MathF.Abs(Elev[(cy + fw - 1) % fw * fw + cx] - e)));
        float rough = MathF.Min(1f, d / K.ROUGH_REF);
        if (rough <= 0f) return 1f;
        float legs = PLegs[i], stab = K.LEG_STAB * legs * (PLegPairs[i] - 1) / 3f;
        float small = MathK.Cbrt(1f / MathF.Max(MassOf(i), 1e-3f));
        return 1f / (1f + K.ROUGH_SLOW * rough * small * (1f - stab));
    }

    public float Vmax(int i) => Vmax(i, FIdx(X[i], Y[i]));

    public bool CellAllows(int cell, Realm r) => (Cell[cell] & (byte)r) != 0;

    public float MaxWaterDepth(int i) => K.SHALLOW * (1f + K.SWIM_DEPTH_RANGE * PFins[i]);

    public bool CellAllows(int i, int cell) => CellAllows(i, cell, RealmOf(i));

    /// <summary>Can this flier land here (2026-10-31)? On land, and in water it can wade or swim in (Body.GroundRealm).
    /// Anything else answers as CellAllows.</summary>
    public bool GroundAllows(int i, int cell)
    {
        if (PLoco[i] != (byte)Neuroreef.Loco.Fly) return CellAllows(i, cell);
        byte kind = Cell[cell];
        if ((kind & (byte)Realm.Land) != 0) return true;
        return (kind & (byte)Body.GroundRealm(PLegs[i], PFins[i])) != 0 && Depth[cell] <= MaxWaterDepth(i);
    }

    /// <summary>Is flier i in the air (2026-10-31)? A flier lands whenever it can and is going no faster than it can walk
    /// (Resolve: resting, eating and drinking land it), so one in the air is over ground it cannot stand on or moved
    /// faster than its legs carry it.</summary>
    public bool Airborne(int i)
    {
        if (PLoco[i] != (byte)Neuroreef.Loco.Fly) return false;
        int cell = FIdx(X[i], Y[i]);
        return !GroundAllows(i, cell) || Speed[i] > GroundVmax(i, cell) * 1.0001f + 1e-6f;
    }

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

    public Span<sbyte> GenomeOf(int i) => Genome.Slot(i);
}

/// <summary>
/// Per-slot values derived from the genome, kept for speed (2026-10-31): not state, so a checkpoint does not save them
/// (Checkpoint walks World, not this class). Body.Express writes them; Refresh rebuilds them all from the genomes (the
/// simulation does when the world was changed under it, as by loading a checkpoint).
/// </summary>
public sealed class Derived
{
    [PerSlot] public sbyte[] Look;    // Cap * Body.NLOOK: the genes LooksAlike compares (Body.Signature)
    [PerSlot] public float[] MhcDiv;  // Pathogens.Diversity of each slot's recognition genes
    /// <summary>BUILD's packed copy of what DECIDE reads of every neighbour, in W.Sorted order (index = sorted position).</summary>
    [PerSlot] public Neighbour[] Near;

    public Derived(int cap) { Look = new sbyte[cap * Body.NLOOK]; MhcDiv = new float[cap]; Near = new Neighbour[cap]; }

    /// <summary>Recompute every living slot's values from its genome.</summary>
    public void Refresh(World w)
    {
        for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] != St.Dead) Body.Signature(w, i);
    }
}

/// <summary>
/// One animal as its neighbours see it this step, packed by BUILD into one cache line (World.D.Near, in hash-cell order):
/// DECIDE reads these for every neighbour, where the slot arrays it read before lay in a dozen places. The values are
/// the slot's own (or the MathK function of them DECIDE took), so every result is the same bit for bit.
/// </summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Size = 64)]
public unsafe struct Neighbour
{
    public float X, Y;
    public float HCos, HSin;      // MathK.Cos and Sin of its heading
    public float Mass, MassCbrt;  // ApparentMass and MathK.Cbrt of it
    public float Shed;            // PgShed
    public int Slot;
    public uint Serial;
    public byte Stage;
    public fixed sbyte Look[Body.NLOOK];   // its look signature (Body.Signature)
}

/// <summary>Bits of World.MemHas: what an animal remembers the way back to (Revision P).</summary>
public static class Mem
{
    public const byte Drink = 1, Fed = 2;
}
