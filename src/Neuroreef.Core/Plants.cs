// PLANTS (DESIGN §10.2, Revision S): land vegetation as evolving genotypes. Every land cell has four slots (herb, shrub,
// tree, sapling); each holds one genotype (an id into the plant table, 0 = empty) and its cover (Pool.Grass, World.Shrub,
// World.Tree, World.Sapling). A genotype is 20 traits of 4 bits (PT); its phenotype (Ph) is computed once, when it
// enters the table. Growth, reproduction, seed dispersal, recruitment and inheritance are physics on those traits: no
// growth form, biome or outcome is designed. The food pools animals eat (grass, browse, fruit, seed, nectar, root) come
// from the slots and their traits, and animals taste the plants' defences when they eat.
//
// Schedule (Simulation.Step, after FIELDS): every step one stripe of rows (y = step mod EVERY) is updated in place with
// dt = EVERY, rows of the stripe in parallel (each reads at most one row up and down, which no other row of the stripe
// writes). Phase A of a row grows its plants and releases their seeds (own bank, own SeedOut, own long-distance
// accumulator); phase B gathers the seeds its 8 neighbours released this epoch (each released seed is read exactly once,
// as every row is updated exactly once in any EVERY consecutive steps) and recruits from its banks. New genotypes found
// in the parallel phase are queued per row and entered into the table serially, in row order (Commit), so the result
// never depends on the thread count. A census every PLANT_CENSUS steps frees genotypes nothing refers to.

using System;

namespace Neuroreef;

/// <summary>Plant trait indices (each a level 0..15, x = level / 15).</summary>
public static class PT
{
    public const int HEIGHT = 0, LMA = 1, ROOT = 2, WUE = 3, DROUGHT = 4, TOPT = 5, DECID = 6, FLOWER = 7, REPRO = 8, SEED = 9,
        FRUIT = 10, WING = 11, HOOK = 12, NECTAR = 13, TOXIN = 14, THORN = 15, RESPROUT = 16, BARK = 17, NFIX = 18, SELF = 19, N = 20;
    public static readonly string[] Names =
        { "height", "lma", "root", "wue", "drought", "topt", "decid", "flower", "repro", "seed",
          "fruit", "wing", "hook", "nectar", "toxin", "thorn", "resprout", "bark", "nfix", "self" };
}

/// <summary>Offsets of the expressed phenotype of one genotype in PlantTable.Ph (NP floats per genotype).</summary>
public static class Ph
{
    public const int H = 0, R0 = 1, R0SAP = 2, LES = 3, CLIT = 4, KL = 5, LA = 6, REXP = 7, KNE = 8, XR = 9, GWF = 10, GCF = 11,
        WMIN = 12, DRM = 13, TOPT = 14, TAMP = 15, TFROST = 16, DECID = 17, PHIF = 18, AR = 19, MS = 20, EST = 21, BANKD = 22,
        FLESH = 23, PULP = 24, CS = 25, PSELF = 26, PFAR = 27, NECT = 28, EMUL = 29, THORN = 30, RESPR = 31, BARK = 32,
        NFIXN = 33, NFIXC = 34, CSTAT = 35, ANORM = 36, S0 = 37, DELTA = 38, LIFE = 39, RHO = 40, HOOK = 41, STRAT = 42,
        EFRUIT = 43, TOX = 44, XH = 45, CPHI = 46, SPHI = 47, NP = 48;
}

/// <summary>The plant genotype table (struct of arrays, saved by Checkpoint like the World).</summary>
public sealed class PlantTable
{
    public const int CAP = K.PLANT_GCAP, HCAP = 2 * CAP, NP = Ph.NP, NSTAT = 8;
    public readonly ulong[] KeyLo = new ulong[CAP];        // traits 0..15, 4 bits each
    public readonly ushort[] KeyHi = new ushort[CAP];      // traits 16..19
    public readonly float[] Phe = new float[CAP * NP];     // expressed phenotype
    public readonly ushort[] Lineage = new ushort[CAP], Parent = new ushort[CAP];
    public readonly int[] Born = new int[CAP], Count = new int[CAP];
    public readonly byte[] Used = new byte[CAP];
    public readonly int[] Hash = new int[HCAP];            // open addressing (linear probing), id or 0; rebuilt by Census
    public readonly int[] Free = new int[CAP]; public int FreeTop; public int Live;
    // genotypes found in the parallel phase, PLANT_PEND per row per update, entered by Commit in row order
    public readonly int[] PendN, PendCell; public readonly byte[] PendSlot; public readonly ulong[] PendLo; public readonly ushort[] PendHi, PendParent;
    // per-row counters of the parallel phase (summed by Commit): adoptions, crosses, mutations, failed crosses, queue full,
    // seed mass released by wind, landed in water
    public readonly double[] RowStat;
    public long Created, Freed, Establish, Crosses, Mutations, FailedCross, TableFull, PendFull;
    public double WindSeed, WaterSeed, GutSeed, CacheSeed, BurrSeed, ReleasedNb, GatheredNb;
    public long CensusSteps;
    public readonly int[] History = new int[256];          // live genotypes at each census (ring)
    public int HistN;

    public PlantTable(int fw)
    {
        PendN = new int[fw]; PendCell = new int[fw * K.PLANT_PEND]; PendSlot = new byte[fw * K.PLANT_PEND];
        PendLo = new ulong[fw * K.PLANT_PEND]; PendHi = new ushort[fw * K.PLANT_PEND]; PendParent = new ushort[fw * K.PLANT_PEND];
        RowStat = new double[fw * NSTAT];
        for (int i = CAP - 1; i >= 1; i--) Free[FreeTop++] = i;
    }

    public int Level(int id, int t) => t < 16 ? (int)((KeyLo[id] >> (4 * t)) & 15) : (KeyHi[id] >> (4 * (t - 16))) & 15;
    public float P(int id, int f) => Phe[id * NP + f];
}

public static class Plants
{
    const int SLOTS = 4, HERB = 0, SHRUB = 1, TREE = 2, SAP = 3;
    const int ST_ADOPT = 0, ST_CROSS = 1, ST_MUT = 2, ST_FAIL = 3, ST_PFULL = 4, ST_WIND = 5, ST_WATER = 6, ST_GATH = 7;

    /// <summary>Steps between two updates of a row (and dt of an update): PLANT_EVERY, or the largest power of two that divides the grid.</summary>
    public static int Every { get { int e = K.PLANT_EVERY; while (e > 1 && Map.FW % e != 0) e >>= 1; return e; } }
    static int NBX => (Map.FW + K.WIND_BLOCK - 1) / K.WIND_BLOCK;
    public static int Blocks => NBX * NBX;

    // ------------------------------------------------------------------ genome and phenotype

    public static float Smooth(float a, float b, float x) { float t = Math.Clamp((x - a) / (b - a), 0f, 1f); return t * t * (3 - 2 * t); }
    public static float HeightOf(float xh) => MathK.Pow(10f, K.H_LO + K.H_SPAN * xh);
    public static int StratumOfH(float h) => h < K.H_HERB ? HERB : h < K.H_SHRUB ? SHRUB : TREE;

    /// <summary>Static costs (share of production) of the defences and fixation at T = NFIX_T, for a genome of traits x.</summary>
    static float Costs(ReadOnlySpan<float> x, float wood)
        => K.TOX_COST * x[PT.TOXIN] + (K.THORN_COST_H + (K.THORN_COST_W - K.THORN_COST_H) * wood) * x[PT.THORN]
         + K.RESPROUT_COST * x[PT.RESPROUT] + K.BARK_COST * x[PT.BARK] * wood;

    /// <summary>Express the phenotype of traits x (0..1 each) into ph (NP floats).</summary>
    public static void Express(ReadOnlySpan<float> x, Span<float> ph)
    {
        ph.Clear();
        float xh = x[PT.HEIGHT], h = HeightOf(xh);
        int strat = StratumOfH(h);
        ph[Ph.H] = h; ph[Ph.STRAT] = strat; ph[Ph.XH] = xh;
        ph[Ph.R0] = K.R_G * MathK.Pow(h / K.H_REF, -K.EXP_R0);
        ph[Ph.R0SAP] = K.R_G * MathK.Pow(K.H_SAP / K.H_REF, -K.EXP_R0);
        float xl = x[PT.LMA];
        ph[Ph.LES] = K.LES_A - K.LES_B * xl;
        ph[Ph.CLIT] = K.C_LIT * MathK.Pow(2f, K.CLIT_EXP * (1f - 2f * xl)) * MathF.Min(1f, MathK.Pow(h / K.H_REF, -K.LEAF_EXP));
        ph[Ph.KL] = K.KL_A + K.KL_B * (1f - xl);
        ph[Ph.LA] = 1.2f - 0.4f * xl;
        float xr = x[PT.ROOT];
        ph[Ph.REXP] = 1.5f - xr; ph[Ph.KNE] = K.K_N * (1.5f - xr); ph[Ph.XR] = xr; ph[Ph.RHO] = K.RHO_LO + K.RHO_SPAN * xr;
        float xw = x[PT.WUE], xd = x[PT.DROUGHT];
        ph[Ph.GWF] = K.W_SUP * (0.5f + xw);
        ph[Ph.GCF] = (1f - K.WUE_GC * xw) * (1f - K.DRT_GC * xd);
        ph[Ph.WMIN] = Math.Clamp(K.WMIN_0 + K.WMIN_H * MathK.Pow(xh, 0.8f) - K.WMIN_D * xd, 0f, 0.6f);
        ph[Ph.DRM] = 1f - K.DRT_M * xd;
        float xt = x[PT.TOPT];
        ph[Ph.TOPT] = K.TOPT_LO + K.TOPT_SPAN * xt; ph[Ph.TAMP] = 0.7f + 0.6f * xt; ph[Ph.TFROST] = K.TFROST_HI - K.TFROST_SPAN * (1f - xt);
        ph[Ph.DECID] = K.DECID_P * x[PT.DECID];
        ph[Ph.PHIF] = 2f * MathF.PI * x[PT.FLOWER]; ph[Ph.CPHI] = MathK.Cos(ph[Ph.PHIF]); ph[Ph.SPHI] = MathK.Sin(ph[Ph.PHIF]);
        ph[Ph.AR] = K.AR_LO + K.AR_SPAN * x[PT.REPRO];
        float ms = K.SEED_REF * MathK.Pow(10f, K.SEED_SPAN * (x[PT.SEED] - 0.5f)), rel = ms / K.SEED_REF;
        ph[Ph.MS] = ms; ph[Ph.EST] = MathK.Pow(rel, K.EST_EXP); ph[Ph.BANKD] = MathK.Pow(rel, K.BANKD_EXP);
        float xf = x[PT.FRUIT], pulp = K.PULP_MAX * xf;
        ph[Ph.FLESH] = xf; ph[Ph.PULP] = pulp;
        float xwi = x[PT.WING], xhk = x[PT.HOOK];
        ph[Ph.CS] = 1f + K.WING_COST * xwi + K.HOOK_COST * xhk + xf * pulp;
        // wind: a seed released at height H falls at v_t and drifts D = U H / v_t (Greene & Johnson 1993)
        float vt = K.V_T0 * MathK.Pow(rel, 1f / 6f) / (1f + K.WING_K * xwi);
        float d = K.U_WIND * h / vt;
        float pself = 1f - MathK.Exp(-0.5f * K.CELL_M / d);
        float pfar = d > K.CELL_M ? MathK.Exp(-3f * K.CELL_M / d) : 0f;
        if (1f - pself - pfar < K.NB_MIN) pself = MathF.Max(0f, 1f - pfar - K.NB_MIN);
        ph[Ph.PSELF] = pself; ph[Ph.PFAR] = pfar;
        ph[Ph.NECT] = K.NECTAR_SHARE * x[PT.NECTAR];
        float xtox = x[PT.TOXIN], xnf = x[PT.NFIX];
        ph[Ph.TOX] = xtox;
        ph[Ph.EMUL] = (K.DIG_LO - K.DIG_SPAN * xl) * (1f - K.TOX_E * xtox) * (1f + K.NFIX_DIG * xnf) / EmulMid;
        ph[Ph.EFRUIT] = (1f - K.TOX_E * K.TOX_FRUIT * xtox) / (1f - K.TOX_E * K.TOX_FRUIT * 0.5f);
        ph[Ph.THORN] = K.THORN_B * x[PT.THORN];
        ph[Ph.RESPR] = K.RESPROUT_MAX * x[PT.RESPROUT];
        float wood = Smooth(0.4f, 0.7f, xh);
        ph[Ph.BARK] = K.BARK_MAX * x[PT.BARK] * wood;
        ph[Ph.NFIXN] = K.NFIX_N * xnf; ph[Ph.NFIXC] = K.NFIX_COST * xnf;
        ph[Ph.CSTAT] = Costs(x, wood);
        // allocation normaliser: a mid genome of this height (every other trait 0.5) puts 1 of its production into growth
        Span<float> mid = stackalloc float[PT.N]; mid.Fill(0.5f); mid[PT.HEIGHT] = xh;
        ph[Ph.ANORM] = 1f / (1f - Costs(mid, wood) - K.NFIX_COST * 0.5f - (K.AR_LO + K.AR_SPAN * 0.5f));
        float xs = x[PT.SELF];
        ph[Ph.S0] = K.SELF_LO + K.SELF_SPAN * xs; ph[Ph.DELTA] = K.INBR_LO - K.INBR_SPAN * xs;
        ph[Ph.LIFE] = 1f / (K.LIFE_HERB * MathK.Pow(h / K.H_REF, K.LIFE_EXP) * (0.5f + xl));
        ph[Ph.HOOK] = xhk;
    }

    static readonly float EmulMid = (K.DIG_LO - K.DIG_SPAN * 0.5f) * (1f - K.TOX_E * 0.5f) * (1f + K.NFIX_DIG * 0.5f);

    /// <summary>Gross production per unit cover with a share A of it in growth, and the water and temperature factors.</summary>
    static float WaterOf(float[] P, int o, float W, float ov) => MathK.Pow(MathF.Max(W, 1e-6f), P[o + Ph.REXP]) * (1f - ov);
    /// <summary>The same from ln W (one logarithm per cell, an exponential per plant).</summary>
    static float WaterOfLn(float[] P, int o, float lnW, float ov) => MathK.Exp(lnW * P[o + Ph.REXP]) * (1f - ov);

    static float Gross(float[] P, int o, float r0, float wk, float N, float T, float L, float A, out float sw, out float ft)
    {
        float wmin = P[o + Ph.WMIN];
        sw = Math.Clamp((wk - wmin) / (1f - wmin), 0f, 1f);
        float dT = (T - P[o + Ph.TOPT]) / K.T_WIDTH;
        ft = MathF.Max(0f, 1f - dT * dT);
        float kl = P[o + Ph.KL], fl = L * (1f + kl) / (L + kl);
        float ne = N + P[o + Ph.NFIXN], fn = ne / (ne + P[o + Ph.KNE]);
        float les = P[o + Ph.LES];
        float gc = r0 * les * A * ft * P[o + Ph.TAMP] * fl * fn * P[o + Ph.GCF];
        float gw = r0 * les * P[o + Ph.GWF] * sw;
        return gc + gw > 0f ? Norm * Nature.Plants * gc * gw / (gc + gw) : 0f;
    }

    /// <summary>Net growth rate per unit cover (per step) of phenotype P[o..] alone in a cell at soil water W, nutrient N,
    /// temperature T and light L, at the yearly mean of its flowering (phenology 1), leafy and with room: what Plants grows by.</summary>
    public static float Net(float[] P, int o, float W, float N, float T, float L)
    {
        float r0 = P[o + Ph.R0];
        float nfc = P[o + Ph.NFIXC] * (1f + MathF.Max(0f, (K.NFIX_T - T) / K.NFIX_TW));
        float A = MathF.Max(K.A_MIN, (1f - P[o + Ph.CSTAT] - nfc - P[o + Ph.AR]) * P[o + Ph.ANORM]);
        return Gross(P, o, r0, WaterOf(P, o, W, 0f), N, T, L, A, out _, out _) - K.RESP * r0 * P[o + Ph.LES];
    }

    /// <summary>NORM: a mid genome at full water, light and nutrient, at T_REF, grows at r0 net of respiration.</summary>
    public static readonly float Norm = ComputeNorm();
    static float ComputeNorm()
    {
        Span<float> x = stackalloc float[PT.N]; x.Fill(0.5f);
        var ph = new float[Ph.NP]; Express(x, ph);
        float dT = (K.T_REF - ph[Ph.TOPT]) / K.T_WIDTH, ft = (1f - dT * dT) * ph[Ph.TAMP];
        float gc = ft * ph[Ph.GCF], gw = ph[Ph.GWF];   // at full water every plant reads 1
        return (1f + K.RESP) * (gc + gw) / (gc * gw);
    }

    /// <summary>The flowering pulse at season phase psi (cos and sin of psi given): PHEN_PEAK ((1 + cos(psi - phi_f)) / 2)^4.</summary>
    static float Phen(float[] P, int o, float cpsi, float spsi) => Nature.Seasons <= 0f ? 1f : K.PHEN_PEAK * Pow4(0.5f * (1f + cpsi * P[o + Ph.CPHI] + spsi * P[o + Ph.SPHI]));
    static float Pow4(float v) { v *= v; return v * v; }

    // ------------------------------------------------------------------ table

    static ulong Mix(ulong lo, ushort hi) { ulong h = lo ^ ((ulong)hi * 0x9E3779B97F4A7C15UL); h ^= h >> 33; h *= 0xFF51AFD7ED558CCDUL; h ^= h >> 33; return h; }

    public static int Find(PlantTable t, ulong lo, ushort hi)
    {
        int m = PlantTable.HCAP - 1, j = (int)(Mix(lo, hi) & (ulong)m);
        while (true)
        {
            int id = t.Hash[j];
            if (id == 0) return 0;
            if (t.KeyLo[id] == lo && t.KeyHi[id] == hi) return id;
            j = (j + 1) & m;
        }
    }

    static void HashIn(PlantTable t, int id)
    {
        int m = PlantTable.HCAP - 1, j = (int)(Mix(t.KeyLo[id], t.KeyHi[id]) & (ulong)m);
        while (t.Hash[j] != 0) j = (j + 1) & m;
        t.Hash[j] = id;
    }

    /// <summary>Enter a genotype (serial). Returns its id, or 0 when the table is full.</summary>
    public static int Insert(World w, ulong lo, ushort hi, int parent, int lineage)
    {
        var t = w.Flora;
        int id = Find(t, lo, hi);
        if (id != 0) return id;
        if (t.FreeTop == 0) { t.TableFull++; return 0; }
        id = t.Free[--t.FreeTop];
        t.KeyLo[id] = lo; t.KeyHi[id] = hi; t.Used[id] = 1; t.Parent[id] = (ushort)parent; t.Lineage[id] = (ushort)lineage;
        t.Born[id] = (int)w.Step; t.Count[id] = 1; t.Live++; t.Created++;
        Span<float> x = stackalloc float[PT.N];
        for (int k = 0; k < PT.N; k++) x[k] = (k < 16 ? (int)((lo >> (4 * k)) & 15) : (hi >> (4 * (k - 16))) & 15) / 15f;
        Express(x, new Span<float>(t.Phe, id * PlantTable.NP, PlantTable.NP));
        HashIn(t, id);
        return id;
    }

    /// <summary>A recruit's genotype: each trait from the seed or the pollen parent, then with PLANT_MU one trait one level up or down.</summary>
    static void Cross(PlantTable t, int mom, int dad, uint h1, uint h2, out ulong lo, out ushort hi, out bool mutated)
    {
        ulong ml = t.KeyLo[mom], dl = t.KeyLo[dad]; ushort mh = t.KeyHi[mom], dh = t.KeyHi[dad];
        ulong mask = 0; uint mhk = 0;
        for (int k = 0; k < 16; k++) if (((h1 >> k) & 1) != 0) mask |= 15UL << (4 * k);
        for (int k = 0; k < 4; k++) if (((h1 >> (16 + k)) & 1) != 0) mhk |= 15u << (4 * k);
        lo = (ml & mask) | (dl & ~mask); hi = (ushort)((mh & mhk) | (dh & ~mhk & 0xFFFF));
        mutated = (h2 >> 8) * (1f / 16777216f) < K.PLANT_MU;
        if (!mutated) return;
        int tr = (int)((h2 & 0xFF) % PT.N), up = (h2 & 0x100) != 0 ? 1 : -1;
        int lv = tr < 16 ? (int)((lo >> (4 * tr)) & 15) : (hi >> (4 * (tr - 16))) & 15;
        int nl = lv + up; if (nl < 0 || nl > 15) nl = lv - up;
        if (tr < 16) lo = (lo & ~(15UL << (4 * tr))) | ((ulong)nl << (4 * tr));
        else hi = (ushort)((hi & ~(15 << (4 * (tr - 16)))) | (nl << (4 * (tr - 16))));
    }

    static int StratumOfKey(ulong lo) => StratumOfH(HeightOf((int)(lo & 15) / 15f));

    /// <summary>Genetic distance (sum of level differences) between two genotypes.</summary>
    public static int Distance(PlantTable t, int a, int b)
    {
        ulong x = t.KeyLo[a], y = t.KeyLo[b]; uint xh = t.KeyHi[a], yh = t.KeyHi[b];
        int d = 0;
        for (int k = 0; k < 16; k++) { d += Math.Abs((int)(x & 15) - (int)(y & 15)); x >>= 4; y >>= 4; }
        for (int k = 0; k < 4; k++) { d += Math.Abs((int)(xh & 15) - (int)(yh & 15)); xh >>= 4; yh >>= 4; }
        return d;
    }

    static float Compat(PlantTable t, int a, int b)
    {
        if (a == b) return 1f;
        float d = Distance(t, a, b) / K.D_PC, c = MathK.Exp(-d * d * d * d);
        if (Nature.Seasons > 0f) { float o = 0.5f * (1f + t.P(a, Ph.CPHI) * t.P(b, Ph.CPHI) + t.P(a, Ph.SPHI) * t.P(b, Ph.SPHI)); c *= o * o; }
        return c;
    }

    // ------------------------------------------------------------------ founding

    /// <summary>
    /// The first plants (Terrain.Build): PLANT_FOUNDERS random genotypes (every trait uniform), each its own lineage. Every land
    /// cell takes, per stratum, the best grower at its moisture and annual mean temperature of PLANT_DRAW founders of that
    /// stratum drawn at random as occupant and seed parent, and the second best as pollen parent (Keddy 1992: the
    /// community is filtered from the pool by the site). The cover laid down by Terrain stays as it is.
    /// </summary>
    public static void Found(World w)
    {
        var t = w.Flora;
        int FW = Map.FW, n = FW * FW;
        var byStrat = new System.Collections.Generic.List<int>[3];
        for (int s = 0; s < 3; s++) byStrat[s] = new();
        for (int f = 0; f < K.PLANT_FOUNDERS; f++)
        {
            ulong lo = 0; ushort hi = 0;
            uint a = Rng.Hash(w.Seed, (uint)f, 1, Rs.PlantFound), b = Rng.Hash(w.Seed, (uint)f, 2, Rs.PlantFound), c = Rng.Hash(w.Seed, (uint)f, 3, Rs.PlantFound);
            lo = a | ((ulong)b << 32); hi = (ushort)(c & 0xFFFF);
            int id = Insert(w, lo, hi, 0, f);
            if (id > 0) byStrat[StratumOfKey(lo)].Add(id);
        }
        var P = w.Pool; var G = P[(int)Pool.Grass];
        for (int i = 0; i < n; i++)
        {
            if (w.Cell[i] != (byte)Realm.Land) continue;
            float W = w.Moisture[i], T = Climate.On ? Climate.AnnualMean(w, i) : K.T_REF, N = w.Nutrient[i];
            for (int s = 0; s < 3; s++)
            {
                var L = byStrat[s];
                if (L.Count == 0) continue;
                int best = 0, second = 0; float bv = float.MinValue, sv = float.MinValue;
                for (int d = 0; d < K.PLANT_DRAW; d++)
                {
                    int id = L[(int)(Rng.U(w.Seed, (uint)i, (uint)(s * 16 + d), Rs.PlantFound + 1) * L.Count)];
                    int o = id * PlantTable.NP;
                    float r0 = t.Phe[o + Ph.R0];
                    float g = Gross(t.Phe, o, r0, WaterOf(t.Phe, o, W, 0f), N, T, 1f, 1f, out _, out _) - K.RESP * r0 * t.Phe[o + Ph.LES];
                    g /= r0;
                    if (g > bv) { second = best; sv = bv; best = id; bv = g; }
                    else if (g > sv && id != best) { second = id; sv = g; }
                }
                if (second == 0) second = best;
                int slot = s == TREE ? TREE : s;
                w.POcc[i * SLOTS + slot] = (ushort)best;
                if (s == TREE) w.POcc[i * SLOTS + SAP] = (ushort)best;
                w.BankMom[s * n + i] = (ushort)best; w.BankDad[s * n + i] = (ushort)second;
            }
            w.BankM[i] = 0.10f * G[i]; w.BankM[n + i] = 0.05f * w.Shrub[i]; w.BankM[2 * n + i] = 0.05f * w.Tree[i];
            P[(int)Pool.Seed][i] = w.BankM[i] + w.BankM[n + i] + w.BankM[2 * n + i];
            P[(int)Pool.Fruit][i] = 0.15f * w.Tree[i] * t.Phe[w.POcc[i * SLOTS + TREE] * PlantTable.NP + Ph.FLESH] * 2f;
            w.FruitG[i] = w.POcc[i * SLOTS + TREE];
            w.PFlags[i] = 0x0F;
        }
    }

    // ------------------------------------------------------------------ the update of one stripe

    /// <summary>Update the rows of this step's stripe (rows y = Step mod Every, a row per call; run in parallel).</summary>
    public static void StripeRow(World w, int k)
    {
        int E = Every, y = StripeY(w.Step, k);
        RowA(w, y, E);
        RowB(w, y, E);
    }

    public static int StripeRows => Map.FW / Every;

    /// <summary>The k-th row of the stripe updated at this step.</summary>
    public static int StripeY(ulong step, int k) => k * Every + (int)(step % (ulong)Every);

    /// <summary>Update one row now (phase A and B, dt = Every), outside the schedule: for tests and tools.</summary>
    public static void UpdateRow(World w, int y) { RowA(w, y, Every); RowB(w, y, Every); }

    /// <summary>Before a stripe: at the start of each epoch, the long-distance seed of the last epoch lands (3x3 blocks).</summary>
    public static void BeginStep(World w)
    {
        if (w.Step % (ulong)Every != 0) return;
        int nbx = NBX, nb = nbx * nbx;
        for (int b = 0; b < nb; b++)
        {
            int bx = b % nbx, by = b / nbx;
            for (int s = 0; s < 3; s++)
            {
                double m = 0; float best = -1f; int bm = 0, bd = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int j = (World.WrapI(by + dy, nbx) * nbx + World.WrapI(bx + dx, nbx)) * 3 + s;
                        float a = w.WindAcc[j];
                        m += a / 9.0;
                        if (a > best) { best = a; bm = w.WindAccMom[j]; bd = w.WindAccDad[j]; }
                    }
                int o = b * 3 + s;
                w.WindDist[o] = (float)m; w.WindDistMom[o] = (ushort)(m > 0 ? bm : 0); w.WindDistDad[o] = (ushort)(m > 0 ? bd : 0);
            }
        }
        Array.Clear(w.WindAcc); Array.Clear(w.WindAccMom); Array.Clear(w.WindAccDad);
    }

    static float Room(int slot, float g, float s, float t, float p) => slot switch
    {
        HERB => 1f - g - s - t,
        SHRUB => 1f - s - t,
        TREE => 1f - t,
        _ => 1f - p - t,
    };

    static void RowA(World w, int y, int E)
    {
        var t = w.Flora; var Pe = t.Phe;
        int FW = Map.FW, n = FW * FW, row = y * FW, NP = PlantTable.NP;
        float dt = E;
        var Pl = w.Pool;
        float[] G = Pl[(int)Pool.Grass], Br = Pl[(int)Pool.Browse], Fr = Pl[(int)Pool.Fruit], Sd = Pl[(int)Pool.Seed], Nc = Pl[(int)Pool.Nectar], Rt = Pl[(int)Pool.Root];
        float[] Li = Pl[(int)Pool.Litter], Wo = Pl[(int)Pool.Wood];
        float[] S = w.Shrub, Tr = w.Tree, Sp = w.Sapling;
        int yu = World.WrapI(y - 1, FW) * FW, yd = World.WrapI(y + 1, FW) * FW;
        float psi = 2f * MathF.PI * (float)(w.Step % (ulong)Nature.SeasonLength) / Nature.SeasonLength - 2f * MathF.PI * y / FW;
        float cpsi = MathK.Cos(psi), spsi = MathK.Sin(psi);
        uint seed = w.Seed, step = (uint)w.Step;
        int nbx = NBX, farRow = y * nbx * 3;
        double windOut = 0;
        Span<float> B = stackalloc float[SLOTS];
        Span<int> id = stackalloc int[SLOTS];
        for (int x = 0; x < FW; x++)
        {
            int i = row + x;
            for (int s = 0; s < 3; s++) { w.SeedOut[s * n + i] = 0f; w.SeedMom[s * n + i] = 0; w.SeedDad[s * n + i] = 0; }
            if (w.Cell[i] != (byte)Realm.Land) continue;
            float W = w.Moisture[i], N = w.Nutrient[i], T = w.Temp[i], lnW = MathK.Log(MathF.Max(W, 1e-6f));
            B[HERB] = G[i]; B[SHRUB] = S[i]; B[TREE] = Tr[i]; B[SAP] = Sp[i];
            int flags = w.PFlags[i];
            for (int k = 0; k < SLOTS; k++)
            {
                id[k] = w.POcc[i * SLOTS + k];
                if (id[k] != 0 && B[k] < 1e-7f) { id[k] = 0; w.POcc[i * SLOTS + k] = 0; }
                if (id[k] == 0) B[k] = 0f;
            }
            bool onS = (flags & 2) != 0, onT = (flags & 4) != 0;
            float laS = id[SHRUB] != 0 ? Pe[id[SHRUB] * NP + Ph.LA] : 1f, laT = id[TREE] != 0 ? Pe[id[TREE] * NP + Ph.LA] : 1f;
            float shT = onT ? B[TREE] * laT : 0f, shS = onS ? B[SHRUB] * laS : 0f;
            float lH = MathF.Max(0.02f, 1f - 0.85f * shT - 0.35f * shS), lS = MathF.Max(0.02f, 1f - 0.70f * shT), lP = lS * (1f - 0.2f * B[SHRUB]);
            float prod = 0f, fixedShare = 0f, litter = 0f, wood = 0f, fenvB = 0f, fenvBsum = 0f, fenvR = 0f, fenvRsum = 0f;
            float g0 = B[HERB], s0 = B[SHRUB], t0 = B[TREE], p0 = B[SAP];
            for (int k = 0; k < SLOTS; k++)
            {
                int g = id[k];
                if (g == 0) continue;
                int o = g * NP;
                float b = B[k];
                // root overlap: other slots take a share of the water in proportion to their cover and the overlap of their rooting depth
                float ov = 0f, xr = Pe[o + Ph.XR];
                for (int j = 0; j < SLOTS; j++) if (j != k && id[j] != 0) ov += B[j] * (1f - MathF.Abs(Pe[id[j] * NP + Ph.XR] - xr));
                ov = MathF.Min(0.95f, K.ROOT_OVL * ov);
                float L = k == HERB ? lH : k == SHRUB ? lS : k == SAP ? lP : 1f;
                float r0 = k == SAP ? Pe[o + Ph.R0SAP] : Pe[o + Ph.R0];
                float phen = k == SAP ? 0f : Phen(Pe, o, cpsi, spsi);
                float nfc = Pe[o + Ph.NFIXC] * (1f + MathF.Max(0f, (K.NFIX_T - T) / K.NFIX_TW));
                float A = MathF.Max(K.A_MIN, (1f - Pe[o + Ph.CSTAT] - nfc - Pe[o + Ph.AR] * phen) * Pe[o + Ph.ANORM]);
                float wk = WaterOfLn(Pe, o, lnW, ov);
                float G0 = Gross(Pe, o, r0, wk, N, T, L, 1f, out float sw, out float ft);
                float gA = A != 1f ? Gross(Pe, o, r0, wk, N, T, L, A, out _, out _) : G0;
                // leaf phenology: shed leaves when water x temperature falls below the deciduousness threshold (hysteresis)
                bool on = (flags & (1 << k)) != 0;
                float pv = sw * ft, th = Pe[o + Ph.DECID];
                if (on && pv < th - K.DECID_HYST) on = false;
                else if (!on && pv > th + K.DECID_HYST) { on = true; b -= K.RELEAF * b; }
                if (on) flags |= 1 << k; else flags &= ~(1 << k);
                float les = Pe[o + Ph.LES], resp = K.RESP * r0 * les;
                float net = on ? gA - resp : -K.LEAFOFF_RESP * resp;
                float room = MathF.Max(0f, Room(k, B[HERB], B[SHRUB], B[TREE], B[SAP]));
                float grow = net > 0f ? net * b * room : net * b;
                float lit = on ? Pe[o + Ph.CLIT] * b : 0f;
                float wmin = Pe[o + Ph.WMIN];
                float mdr = r0 * K.DROUGHT_M * MathF.Max(0f, wmin - wk) / MathF.Max(wmin, 0.05f) * Pe[o + Ph.DRM] * (on ? 1f : K.LEAFOFF_RESP);
                float mbg = Pe[o + Ph.LIFE];
                float dB = dt * (grow - lit - (mdr + mbg) * b);
                float frost = on && T < Pe[o + Ph.TFROST] ? K.FROST_LOSS * b : 0f;
                dB -= frost;
                litter += dt * lit + frost;
                float dead = dt * (mdr + mbg) * b;
                if (k == HERB) litter += dead; else wood += dead;
                prod += MathF.Max(0f, dt * grow);
                float fe = G0 / (r0 * (1f + K.RESP));
                if (k == SHRUB || k == TREE || k == SAP) { fenvB += fe * b; fenvBsum += b; }
                if (k == HERB || k == SHRUB) { fenvR += fe * b; fenvRsum += b; }
                // reproduction: a mature, leafy plant puts AR x phenology of its production into flowers, nectar and seed
                if (k != SAP && on && b >= 0.05f + 0.25f * Pe[o + Ph.XH] && phen > 1e-3f)
                {
                    float R = dt * Pe[o + Ph.AR] * phen * K.REPRO_RATE * Nature.Plants * fe * b;
                    // pollen: wind from leafy neighbours of the same stratum, and what animals carried here
                    float bn = 0f; int best = 0; float bb = 0f;
                    for (int q = 0; q < 4; q++)
                    {
                        int j = q switch { 0 => row + (x == 0 ? FW - 1 : x - 1), 1 => row + (x == FW - 1 ? 0 : x + 1), 2 => yu + x, _ => yd + x };
                        int gj = w.POcc[j * SLOTS + k];
                        if (gj == 0) continue;
                        float bj = k == HERB ? G[j] : k == SHRUB ? S[j] : Tr[j];
                        if ((w.PFlags[j] & (1 << k)) == 0) continue;
                        bn += 0.25f * bj;
                        if (bj > bb) { bb = bj; best = gj; }
                    }
                    float poll = w.Poll[i];
                    int dad = g;
                    uint hh = Rng.Hash(seed, step, (uint)(i * SLOTS + k), Rs.PlantPick);
                    float u1 = (hh >> 8) * (1f / 16777216f), u2 = (Rng.Hash(seed, step, (uint)(i * SLOTS + k), Rs.PlantPick + 1) >> 8) * (1f / 16777216f);
                    int pd = w.PollenDad[i];
                    if (pd != 0 && t.Used[pd] != 0 && Pe[pd * NP + Ph.STRAT] == k && u1 < poll / (poll + bn + 1e-6f)) dad = pd;
                    else if (best != 0) dad = best;
                    float set;
                    if (dad != g && Compat(t, g, dad) > u2) set = MathF.Min(1f, Pe[o + Ph.S0] + K.WIND_POLL * bn + K.ANIMAL_POLL * poll);
                    else { if (dad != g) t.RowStat[y * PlantTable.NSTAT + ST_FAIL]++; dad = g; set = Pe[o + Ph.S0]; }
                    float nect = Pe[o + Ph.NECT] * R;
                    Nc[i] += nect;
                    float seedInv = (R - nect) * set;
                    dB += (R - nect) * (1f - set) * room;   // what was not set goes back into growth
                    float sm = seedInv / Pe[o + Ph.CS];
                    float flesh = Pe[o + Ph.FLESH] * sm, dry = sm - flesh;
                    if (flesh > 0f)
                    {
                        float fm = flesh * (1f + Pe[o + Ph.PULP]);
                        Fr[i] += fm;
                        if (w.FruitG[i] == 0 || u1 * Fr[i] < fm) w.FruitG[i] = (ushort)g;
                    }
                    if (dry > 0f)
                    {
                        float self = dry * Pe[o + Ph.PSELF], far = dry * Pe[o + Ph.PFAR], nb = dry - self - far;
                        BankAdd(w, k, i, g, dad, self, u2);
                        int so = k * n + i;
                        w.SeedOut[so] = nb / 8f; w.SeedMom[so] = (ushort)g; w.SeedDad[so] = (ushort)dad;
                        int fo = farRow + (x / K.WIND_BLOCK) * 3 + k;
                        w.RowFar[fo] += far;
                        if (far > w.RowFarBest[fo]) { w.RowFarBest[fo] = far; w.RowFarMom[fo] = (ushort)g; w.RowFarDad[fo] = (ushort)dad; }
                        windOut += nb + far;
                    }
                    prod += R;
                }
                fixedShare += Pe[o + Ph.NFIXN] * b;
                B[k] = MathF.Max(0f, b + dB);
            }
            // saplings grow into the canopy as it opens (M_P), merged with the tree there
            if (id[SAP] != 0 && B[SAP] > 0f)
            {
                float mat = MathF.Min(B[SAP], dt * K.M_P * B[SAP] * MathF.Max(0f, 1f - B[TREE]));
                B[SAP] -= mat;
                Merge(w, i, TREE, id[SAP], mat, B, id, Rng.U(seed, step, (uint)i, Rs.PlantPick + 2));
            }
            // light competition: trees overtop shrubs, shrubs overtop herbs; what has no room falls as litter
            if (B[TREE] > 1f) { wood += B[TREE] - 1f; B[TREE] = 1f; }
            if (B[SHRUB] > 1f - B[TREE]) { litter += B[SHRUB] - (1f - B[TREE]); B[SHRUB] = 1f - B[TREE]; }
            float hmax = MathF.Max(0f, 1f - B[TREE] - B[SHRUB]);
            if (B[HERB] > hmax) { litter += B[HERB] - hmax; B[HERB] = hmax; }
            if (B[SAP] > 1f - B[TREE]) { litter += B[SAP] - MathF.Max(0f, 1f - B[TREE]); B[SAP] = MathF.Max(0f, 1f - B[TREE]); }
            G[i] = B[HERB]; S[i] = B[SHRUB]; Tr[i] = B[TREE]; Sp[i] = B[SAP];
            for (int k = 0; k < SLOTS; k++) w.POcc[i * SLOTS + k] = (ushort)(B[k] > 1e-7f ? id[k] : 0);
            w.PFlags[i] = (byte)flags;
            Li[i] += litter; Wo[i] += wood;

            // the food pools the plants make: browse (leaves in reach), roots, the decay of fruit, nectar and the seed bank
            float cap = 0f, capR = 0f;
            if (id[SHRUB] != 0 && (flags & 2) != 0) cap += B[SHRUB] * Reach(Pe, id[SHRUB] * NP, false);
            if (id[TREE] != 0 && (flags & 4) != 0) cap += B[TREE] * Reach(Pe, id[TREE] * NP, false);
            if (id[SAP] != 0 && (flags & 8) != 0) cap += B[SAP] * Reach(Pe, id[SAP] * NP, true);
            if (id[HERB] != 0) capR += B[HERB] * Pe[id[HERB] * NP + Ph.RHO] / RhoMid;
            if (id[SHRUB] != 0) capR += B[SHRUB] * Pe[id[SHRUB] * NP + Ph.RHO] / RhoMid;
            float fB = fenvBsum > 0f ? fenvB / fenvBsum : 0f, fR = fenvRsum > 0f ? fenvR / fenvRsum : 0f;
            Br[i] = MathF.Max(0f, Br[i] + MathF.Min(1f, dt * K.R_B * Nature.Plants * fB) * (cap - Br[i]));
            Rt[i] = Math.Clamp(Rt[i] + MathF.Min(1f, dt * K.R_R * Nature.Plants * fR) * (MathF.Min(1f, capR) - Rt[i]), 0f, 1f);
            float rot = Fr[i] * MathF.Min(1f, dt * K.D_F);
            Fr[i] -= rot;
            if (rot > 0f && w.FruitG[i] != 0)
            {
                int fg = w.FruitG[i]; float sd = rot / (1f + Pe[fg * NP + Ph.PULP]);
                Li[i] += rot - sd;
                BankAdd(w, (int)Pe[fg * NP + Ph.STRAT], i, fg, fg, sd, Rng.U(seed, step, (uint)i, Rs.PlantPick + 3));
            }
            else Li[i] += rot;
            if (Fr[i] < 1e-7f) w.FruitG[i] = 0;
            Nc[i] = MathF.Max(0f, Nc[i] - Nc[i] * MathF.Min(1f, dt * K.D_NC));
            for (int s = 0; s < 3; s++)
            {
                int bo = s * n + i; int m = w.BankMom[bo];
                if (m == 0) continue;
                float d = w.BankM[bo] * MathF.Min(1f, dt * K.D_SD / Pe[m * NP + Ph.BANKD]);
                w.BankM[bo] -= d; Li[i] += d;
            }
            // nutrient taken up by growth, less what fixation supplies
            float tot = B[HERB] + B[SHRUB] + B[TREE] + B[SAP];
            float fx = tot > 0f ? fixedShare / tot : 0f, fixS = fx / (N + fx + 1e-6f);
            w.Nutrient[i] = Math.Clamp(N - prod * K.C_NUTUSE * (1f - fixS), 0f, K.N_MAX);
        }
        t.RowStat[y * PlantTable.NSTAT + ST_WIND] += windOut;
    }

    static readonly float RhoMid = K.RHO_LO + 0.5f * K.RHO_SPAN;

    /// <summary>Browse a leafy woody plant offers per unit cover: the leaves within an animal's reach (BROWSE_H), less its root share.</summary>
    static float Reach(float[] Pe, int o, bool sapling)
    {
        float h = sapling ? K.H_SAP : Pe[o + Ph.H];
        return (1f - Pe[o + Ph.RHO]) / (1f - RhoMid) * MathF.Min(1f, K.BROWSE_H / h);
    }

    /// <summary>Add seed of (mom, dad) to bank s of cell i; the bank's parents become these with probability m / bank.</summary>
    static void BankAdd(World w, int s, int i, int mom, int dad, float m, float u)
    {
        if (m <= 0f || mom == 0) return;
        int bo = s * Map.FW * Map.FW + i;
        w.BankM[bo] += m;
        if (w.BankMom[bo] == 0 || u * w.BankM[bo] < m) { w.BankMom[bo] = (ushort)mom; w.BankDad[bo] = (ushort)dad; }
    }

    /// <summary>
    /// Add cover m of genotype g to a slot (Chesson &amp; Warner 1981, a lottery for the site): it takes the slot with
    /// probability m phi_in / (m phi_in + B phi_occ), phi a relative growth rate, here taken equal; the cover adds.
    /// </summary>
    static void Merge(World w, int i, int slot, int g, float m, Span<float> B, Span<int> id, float u)
    {
        if (m <= 0f || g == 0) return;
        if (id[slot] == 0 || B[slot] <= 0f || u * (m + B[slot]) < m) id[slot] = g;
        B[slot] += m;
    }

    static void RowB(World w, int y, int E)
    {
        var t = w.Flora; var Pe = t.Phe;
        int FW = Map.FW, n = FW * FW, row = y * FW, NP = PlantTable.NP;
        float dt = E;
        uint seed = w.Seed, step = (uint)w.Step;
        int nbx = NBX, yu = World.WrapI(y - 1, FW), yd = World.WrapI(y + 1, FW);
        int by = y / K.WIND_BLOCK, bh = Math.Min(K.WIND_BLOCK, FW - by * K.WIND_BLOCK);
        float[] G = w.Pool[(int)Pool.Grass], Sd = w.Pool[(int)Pool.Seed], De = w.Pool[(int)Pool.Detritus];
        double gathered = 0, water = 0;
        int st = y * PlantTable.NSTAT;
        Span<float> B = stackalloc float[SLOTS];
        Span<int> id = stackalloc int[SLOTS];
        for (int x = 0; x < FW; x++)
        {
            int i = row + x;
            bool land = w.Cell[i] == (byte)Realm.Land;
            int bx = x / K.WIND_BLOCK, bw = Math.Min(K.WIND_BLOCK, FW - bx * K.WIND_BLOCK), blk = by * nbx + bx;
            float share = 1f / (bw * bh);
            int xl = x == 0 ? FW - 1 : x - 1, xr = x == FW - 1 ? 0 : x + 1;
            for (int s = 0; s < 3; s++)
            {
                // the seed released towards this cell by its 8 neighbours this epoch and its share of the long-distance seed;
                // the bank's parents become those of one incoming lot, lot j with probability m_j / (bank + all incoming):
                // the same as adding the lots one by one, each taking over with probability m / bank (one draw, not nine)
                float inTot = 0f;
                for (int q = 0; q < 9; q++) inTot += Incoming(w, q, s, n, FW, x, y, xl, xr, yu, yd, blk, share);
                if (inTot <= 0f) continue;
                gathered += inTot;
                if (!land) { De[i] += inTot; water += inTot; continue; }
                int bo = s * n + i;
                float tot = w.BankM[bo] + inTot, u = Rng.U(seed, step, (uint)bo, Rs.PlantPick + 4) * tot;
                w.BankM[bo] = tot;
                if (u < inTot || w.BankMom[bo] == 0)
                {
                    float c = 0f;
                    for (int q = 0; q < 9; q++)
                    {
                        float m = Incoming(w, q, s, n, FW, x, y, xl, xr, yu, yd, blk, share);
                        if (m <= 0f) continue;
                        c += m;
                        if (c > u || q == 8 || c >= inTot)
                        {
                            Parents(w, q, s, n, FW, x, y, xl, xr, yu, yd, blk, out int mom, out int dad);
                            if (mom != 0) { w.BankMom[bo] = (ushort)mom; w.BankDad[bo] = (ushort)dad; break; }
                        }
                    }
                }
            }
            if (!land) continue;

            // recruitment from each bank into its stratum's slot (the canopy bank into saplings)
            B[HERB] = G[i]; B[SHRUB] = w.Shrub[i]; B[TREE] = w.Tree[i]; B[SAP] = w.Sapling[i];
            for (int k = 0; k < SLOTS; k++) id[k] = w.POcc[i * SLOTS + k];
            float W = w.Moisture[i], N = w.Nutrient[i], T = w.Temp[i], lnW = MathK.Log(MathF.Max(W, 1e-6f));
            for (int s = 0; s < 3; s++)
            {
                int bo = s * n + i, mom = w.BankMom[bo];
                float bank = w.BankM[bo];
                if (mom == 0 || bank <= 1e-9f) { if (bank <= 1e-9f) { w.BankMom[bo] = 0; w.BankDad[bo] = 0; } continue; }
                int dad = w.BankDad[bo]; if (dad == 0 || t.Used[dad] == 0) dad = mom;
                int o = mom * NP;
                int target = s == TREE ? SAP : s;
                float r0 = target == SAP ? Pe[o + Ph.R0SAP] : Pe[o + Ph.R0];
                float L = target == HERB ? MathF.Max(0.02f, 1f - 0.85f * B[TREE] - 0.35f * B[SHRUB]) : target == SHRUB ? MathF.Max(0.02f, 1f - 0.7f * B[TREE]) : MathF.Max(0.02f, (1f - 0.7f * B[TREE]) * (1f - 0.2f * B[SHRUB]));
                float phi = Gross(Pe, o, r0, WaterOfLn(Pe, o, lnW, 0f), N, T, L, 1f, out _, out _) / (r0 * (1f + K.RESP)) - K.RESP / (1f + K.RESP);
                if (phi <= 0f) continue;
                float room = MathF.Max(0f, Room(target, B[HERB], B[SHRUB], B[TREE], B[SAP]));
                int occ = id[target];
                float gap = room + (occ != 0 ? dt * Pe[occ * NP + Ph.LIFE] : 0f);
                float nseed = bank / Pe[o + Ph.MS];
                bool selfed = dad == mom;
                float mrec = K.B_SEED0 * (1f - MathK.Exp(-nseed * K.EST0 * Pe[o + Ph.EST] * phi)) * MathF.Min(1f, gap) * (selfed ? 1f - Pe[o + Ph.DELTA] : 1f);
                mrec = MathF.Min(mrec, bank);
                if (mrec <= 1e-9f) continue;
                w.BankM[bo] = bank - mrec;
                // whether the recruits take the slot (Merge's lottery), then their genotype (a cross; it may belong to another stratum)
                float u = Rng.U(seed, step, (uint)bo, Rs.PlantPick + 5);
                bool adopt = id[target] == 0 || B[target] <= 0f || u * (mrec + B[target]) < mrec;
                if (!adopt) { B[target] += mrec; continue; }
                uint h1 = Rng.Hash(seed, step, (uint)bo, Rs.PlantCross), h2 = Rng.Hash(seed, step, (uint)bo, Rs.PlantMut);
                Cross(t, mom, dad, h1, h2, out ulong lo, out ushort hi, out bool mut);
                int ks = StratumOfKey(lo), slot = ks == TREE ? SAP : ks;
                if (slot != target && id[slot] != 0 && B[slot] > 0f && u * (mrec + B[slot]) >= mrec) { B[target] += mrec; continue; }
                B[slot] += mrec;
                t.RowStat[st + ST_ADOPT]++;
                if (dad != mom) t.RowStat[st + ST_CROSS]++;
                if (mut) t.RowStat[st + ST_MUT]++;
                int gid = lo == t.KeyLo[mom] && hi == t.KeyHi[mom] ? mom : lo == t.KeyLo[dad] && hi == t.KeyHi[dad] ? dad : Find(t, lo, hi);
                if (gid == 0)
                {
                    // a new genotype: queued for the table, the slot holds the seed parent until then
                    int pn = t.PendN[y];
                    if (pn < K.PLANT_PEND)
                    {
                        int pi = y * K.PLANT_PEND + pn;
                        t.PendCell[pi] = i; t.PendSlot[pi] = (byte)slot; t.PendLo[pi] = lo; t.PendHi[pi] = hi; t.PendParent[pi] = (ushort)mom;
                        t.PendN[y] = pn + 1;
                    }
                    else t.RowStat[st + ST_PFULL]++;
                    gid = StratumOfKey(t.KeyLo[mom]) == ks ? mom : id[slot];
                }
                id[slot] = gid;
            }
            // clip again to the light hierarchy
            if (B[SHRUB] > 1f - B[TREE]) B[SHRUB] = MathF.Max(0f, 1f - B[TREE]);
            float hmax = MathF.Max(0f, 1f - B[TREE] - B[SHRUB]);
            if (B[HERB] > hmax) B[HERB] = hmax;
            if (B[SAP] > 1f - B[TREE]) B[SAP] = MathF.Max(0f, 1f - B[TREE]);
            G[i] = B[HERB]; w.Shrub[i] = B[SHRUB]; w.Tree[i] = B[TREE]; w.Sapling[i] = B[SAP];
            for (int k = 0; k < SLOTS; k++) w.POcc[i * SLOTS + k] = (ushort)(B[k] > 1e-7f ? id[k] : 0);
            Sd[i] = w.BankM[i] + w.BankM[n + i] + w.BankM[2 * n + i];
        }
        t.RowStat[st + ST_GATH] += gathered;
        t.RowStat[st + ST_WATER] += water;
    }

    static int Src(int q, int s, int n, int FW, int x, int y, int xl, int xr, int yu, int yd)
    {
        int jy = q < 3 ? yu : q < 5 ? y : yd;
        int jx = q switch { 0 => xl, 1 => x, 2 => xr, 3 => xl, 4 => xr, 5 => xl, 6 => x, _ => xr };
        return s * n + jy * FW + jx;
    }

    static float Incoming(World w, int q, int s, int n, int FW, int x, int y, int xl, int xr, int yu, int yd, int blk, float share)
        => q < 8 ? w.SeedOut[Src(q, s, n, FW, x, y, xl, xr, yu, yd)] : w.WindDist[blk * 3 + s] * share;

    static void Parents(World w, int q, int s, int n, int FW, int x, int y, int xl, int xr, int yu, int yd, int blk, out int mom, out int dad)
    {
        if (q < 8) { int j = Src(q, s, n, FW, x, y, xl, xr, yu, yd); mom = w.SeedMom[j]; dad = w.SeedDad[j]; }
        else { mom = w.WindDistMom[blk * 3 + s]; dad = w.WindDistDad[blk * 3 + s]; }
    }

    /// <summary>After a stripe (serial, rows in order): queued genotypes enter the table, long-distance seed joins its block, counters are summed.</summary>
    public static void Commit(World w)
    {
        var t = w.Flora;
        int E = Every, FW = Map.FW, off = (int)(w.Step % (ulong)E), nbx = NBX;
        for (int y = off; y < FW; y += E)
        {
            for (int q = 0; q < t.PendN[y]; q++)
            {
                int pi = y * K.PLANT_PEND + q, cell = t.PendCell[pi], slot = t.PendSlot[pi];
                int par = t.PendParent[pi];
                int id = Insert(w, t.PendLo[pi], t.PendHi[pi], par, t.Lineage[par]);
                if (id == 0) continue;   // the table is full: the recruit keeps its seed parent's genotype
                if (SlotB(w, cell, slot) > 1e-7f) w.POcc[cell * SLOTS + slot] = (ushort)id;
            }
            t.PendN[y] = 0;
            int st = y * PlantTable.NSTAT;
            t.Establish += (long)t.RowStat[st + ST_ADOPT]; t.Crosses += (long)t.RowStat[st + ST_CROSS]; t.Mutations += (long)t.RowStat[st + ST_MUT];
            t.FailedCross += (long)t.RowStat[st + ST_FAIL]; t.PendFull += (long)t.RowStat[st + ST_PFULL];
            t.ReleasedNb += t.RowStat[st + ST_WIND]; t.GatheredNb += t.RowStat[st + ST_GATH]; t.WaterSeed += t.RowStat[st + ST_WATER];
            t.WindSeed += t.RowStat[st + ST_WIND];
            Array.Clear(t.RowStat, st, PlantTable.NSTAT);
            // this row's long-distance seed joins its block's accumulator (one parent pair per block: the largest share, by lottery)
            int by = y / K.WIND_BLOCK;
            for (int bx = 0; bx < nbx; bx++)
                for (int s = 0; s < 3; s++)
                {
                    int fo = (y * nbx + bx) * 3 + s;
                    float m = w.RowFar[fo];
                    if (m > 0f)
                    {
                        int b = (by * nbx + bx) * 3 + s;
                        w.WindAcc[b] += m;
                        if (w.WindAccMom[b] == 0 || Rng.U(w.Seed, (uint)w.Step, (uint)fo, Rs.PlantPick + 6) * w.WindAcc[b] < m)
                        { w.WindAccMom[b] = w.RowFarMom[fo]; w.WindAccDad[b] = w.RowFarDad[fo]; }
                    }
                    w.RowFar[fo] = 0f; w.RowFarBest[fo] = 0f;
                }
        }
        if (w.Step % K.PLANT_CENSUS == 0) Census(w);
    }

    static float SlotB(World w, int i, int k) => k == HERB ? w.Pool[(int)Pool.Grass][i] : k == SHRUB ? w.Shrub[i] : k == TREE ? w.Tree[i] : w.Sapling[i];

    static void Refs(int[] c, ushort[] a) { for (int i = 0; i < a.Length; i++) c[a[i]]++; }

    /// <summary>Count every reference to every genotype; free those nothing refers to; rebuild the hash.</summary>
    public static void Census(World w)
    {
        var t = w.Flora; var C = t.Count;
        Array.Clear(C);
        Refs(C, w.POcc); Refs(C, w.BankMom); Refs(C, w.BankDad); Refs(C, w.SeedMom); Refs(C, w.SeedDad); Refs(C, w.FruitG); Refs(C, w.PollenDad);
        Refs(C, w.WindAccMom); Refs(C, w.WindAccDad); Refs(C, w.WindDistMom); Refs(C, w.WindDistDad); Refs(C, w.RowFarMom); Refs(C, w.RowFarDad);
        Refs(C, w.GutSeedG); Refs(C, w.GutSeedDad); Refs(C, w.FurSeedG); Refs(C, w.FurSeedDad); Refs(C, w.PollenG);
        C[0] = 0;
        // lineage roots and parents are names only: they do not hold a genotype in the table
        for (int id = 1; id < PlantTable.CAP; id++)
            if (t.Used[id] != 0 && C[id] == 0) { t.Used[id] = 0; t.Free[t.FreeTop++] = id; t.Live--; t.Freed++; }
        Array.Clear(t.Hash);
        for (int id = 1; id < PlantTable.CAP; id++) if (t.Used[id] != 0) HashIn(t, id);
        t.History[t.HistN++ % t.History.Length] = t.Live;
        t.CensusSteps++;
    }

    // ------------------------------------------------------------------ fire

    /// <summary>A fire burns cell i: herbs keep RESPR of their cover, woody stems survive by height over the flames or by bark
    /// (Higgins, Bond &amp; Trollope 2000), killed stems resprout by RESPR; banks keep BANK_FIRE. Returns the biomass burned.</summary>
    public static float Burn(World w, int i, float fuel)
    {
        var t = w.Flora; var Pe = t.Phe; int NP = PlantTable.NP, n = Map.FW * Map.FW;
        var Pl = w.Pool;
        float burned = 0f;
        float flame = K.FLAME_H * fuel;
        int gh = w.POcc[i * SLOTS + HERB];
        float g = Pl[(int)Pool.Grass][i];
        float keep = gh != 0 ? Pe[gh * NP + Ph.RESPR] : 0f;
        burned += g * (1f - keep); Pl[(int)Pool.Grass][i] = g * keep;
        float woodIn = 0f;
        for (int k = 1; k < SLOTS; k++)
        {
            int id = w.POcc[i * SLOTS + k];
            float b = k == SHRUB ? w.Shrub[i] : k == TREE ? w.Tree[i] : w.Sapling[i];
            if (id == 0 || b <= 0f) continue;
            int o = id * NP;
            float h = k == SAP ? K.H_SAP : Pe[o + Ph.H];
            float surv = MathF.Max(Pe[o + Ph.BARK], Math.Clamp((h - flame) / h, 0f, 1f));
            float killed = b * (1f - surv), re = killed * Pe[o + Ph.RESPR], lost = killed - re;
            float nb = b - lost;
            woodIn += 0.5f * lost; burned += 0.5f * lost;
            if (k == SHRUB) w.Shrub[i] = nb; else if (k == TREE) w.Tree[i] = nb; else w.Sapling[i] = nb;
        }
        Pl[(int)Pool.Wood][i] += woodIn;
        for (int s = 0; s < 3; s++) { float b = w.BankM[s * n + i]; burned += b * (1f - K.BANK_FIRE); w.BankM[s * n + i] = b * K.BANK_FIRE; }
        Pl[(int)Pool.Seed][i] = w.BankM[i] + w.BankM[n + i] + w.BankM[2 * n + i];
        burned += Pl[(int)Pool.Browse][i] + Pl[(int)Pool.Litter][i] + Pl[(int)Pool.Nectar][i] + Pl[(int)Pool.Fruit][i];
        Pl[(int)Pool.Browse][i] = 0; Pl[(int)Pool.Litter][i] = 0; Pl[(int)Pool.Nectar][i] = 0; Pl[(int)Pool.Fruit][i] = 0; w.FruitG[i] = 0;
        Pl[(int)Pool.Root][i] *= 0.7f;
        return burned;
    }

    // ------------------------------------------------------------------ animals

    /// <summary>
    /// What a bite of pool p in this cell is worth to an animal's gut (energy multiplier, from leaf economics, toxins and
    /// fixed nitrogen; Coley 1983; Robbins 1987) and how much of a bite of size bite it gets past thorns (Cooper &amp;
    /// Owen-Smith 1986). Both are 1 for a mid genome, for foods that are not live plants, and on cells without plants.
    /// </summary>
    public static void Taste(World w, int p, int cell, float bite, out float eMul, out float bMul)
    {
        eMul = 1f; bMul = 1f;
        var t = w.Flora; var Pe = t.Phe; int NP = PlantTable.NP;
        int g = 0;
        switch ((Pool)p)
        {
            case Pool.Grass: case Pool.Root: g = w.POcc[cell * SLOTS + HERB]; break;
            case Pool.Browse:
                {
                    int s = w.POcc[cell * SLOTS + SHRUB], tr = w.POcc[cell * SLOTS + TREE];
                    float ws = s != 0 ? w.Shrub[cell] * Reach(Pe, s * NP, false) : 0f, wt = tr != 0 ? w.Tree[cell] * Reach(Pe, tr * NP, false) : 0f;
                    if (ws + wt <= 0f) return;
                    float em = 0f, th = 0f;
                    if (ws > 0f) { em += ws * Pe[s * NP + Ph.EMUL]; th += ws * Pe[s * NP + Ph.THORN]; }
                    if (wt > 0f) { em += wt * Pe[tr * NP + Ph.EMUL]; th += wt * Pe[tr * NP + Ph.THORN]; }
                    eMul = em / (ws + wt); bMul = ThornMul(th / (ws + wt), bite);
                    return;
                }
            case Pool.Fruit: { int f = w.FruitG[cell]; if (f != 0) eMul = Pe[f * NP + Ph.EFRUIT]; return; }
            default: return;
        }
        if (g == 0) return;
        eMul = Pe[g * NP + Ph.EMUL];
        if (p == (int)Pool.Grass) bMul = ThornMul(Pe[g * NP + Ph.THORN], bite);
    }

    static float ThornMul(float th, float bite)
    {
        float f = bite / (bite + K.B_HALF);
        return (1f - th * f) / (1f - 0.5f * K.THORN_B * f);
    }

    /// <summary>After an animal ate bite of pool p in this cell (RESOLVE, serial): what it took from the plants, the seed
    /// it swallowed (with their parents) and the pollen it carried. eff is its assimilation of the bite.</summary>
    public static void Eaten(World w, int a, int cell, int p, float bite, float eff)
    {
        var t = w.Flora; var Pe = t.Phe; int NP = PlantTable.NP, n = Map.FW * Map.FW;
        uint u = Rng.Hash(w.Seed, (uint)w.Step, (uint)a, Rs.PlantPick + 7);
        float uf = (u >> 8) * (1f / 16777216f);
        switch ((Pool)p)
        {
            case Pool.Browse:
                {
                    // browsing takes leaves and shoots: BROWSE_LOSS of the bite from the woody slots, by what each offers
                    int s = w.POcc[cell * SLOTS + SHRUB], tr = w.POcc[cell * SLOTS + TREE], sp = w.POcc[cell * SLOTS + SAP];
                    float ws = s != 0 ? w.Shrub[cell] * Reach(Pe, s * NP, false) : 0f, wt = tr != 0 ? w.Tree[cell] * Reach(Pe, tr * NP, false) : 0f;
                    float wp = sp != 0 ? w.Sapling[cell] * Reach(Pe, sp * NP, true) : 0f, sum = ws + wt + wp;
                    if (sum <= 0f) return;
                    float loss = K.BROWSE_LOSS * bite / sum;
                    w.Shrub[cell] = MathF.Max(0f, w.Shrub[cell] - loss * ws); w.Tree[cell] = MathF.Max(0f, w.Tree[cell] - loss * wt); w.Sapling[cell] = MathF.Max(0f, w.Sapling[cell] - loss * wp);
                    return;
                }
            case Pool.Fruit:
                {
                    int f = w.FruitG[cell];
                    if (f == 0) return;
                    // seeds pass the gut whole if the fruit fits the gape (Wheelwright 1985)
                    float rel = Pe[f * NP + Ph.MS] / K.SEED_REF;
                    float gape = MathF.Min(1f, MathK.Cbrt(w.MassOf(a) / K.M_GAPE) / MathK.Cbrt(rel));
                    float viable = MathF.Min(bite * (1f - eff), bite / (1f + Pe[f * NP + Ph.PULP]) * gape);
                    GutAdd(w, a, f, f, viable, uf);
                    return;
                }
            case Pool.Seed:
                {
                    float tot = w.BankM[cell] + w.BankM[n + cell] + w.BankM[2 * n + cell];
                    if (tot <= 0f) return;
                    int bs = 0; float bb = -1f;
                    for (int s = 0; s < 3; s++) if (w.BankM[s * n + cell] > bb) { bb = w.BankM[s * n + cell]; bs = s; }
                    int mom = w.BankMom[bs * n + cell], dad = w.BankDad[bs * n + cell];
                    float keep = MathF.Max(0f, 1f - bite / tot);
                    for (int s = 0; s < 3; s++) w.BankM[s * n + cell] *= keep;
                    GutAdd(w, a, mom, dad, bite * (1f - eff), uf);
                    return;
                }
            case Pool.Nectar:
                {
                    // a pollinator leaves the pollen it carries and takes the flowering occupant's
                    if (w.PollenG[a] != 0) w.PollenDad[cell] = w.PollenG[a];
                    int h = w.POcc[cell * SLOTS + HERB], s = w.POcc[cell * SLOTS + SHRUB];
                    float bh = h != 0 && (w.PFlags[cell] & 1) != 0 ? w.Pool[(int)Pool.Grass][cell] : 0f, bs = s != 0 && (w.PFlags[cell] & 2) != 0 ? w.Shrub[cell] : 0f;
                    int g = bh >= bs ? h : s;
                    if (g != 0 && (bh > 0f || bs > 0f)) w.PollenG[a] = (ushort)g;
                    return;
                }
        }
    }

    static void GutAdd(World w, int a, int mom, int dad, float m, float u)
    {
        if (m <= 0f || mom == 0) return;
        w.GutSeedy[a] += m;
        if (w.GutSeedG[a] == 0 || u * w.GutSeedy[a] < m) { w.GutSeedG[a] = (ushort)mom; w.GutSeedDad[a] = (ushort)dad; }
        if (dad == 0) w.GutSeedDad[a] = (ushort)mom;
    }

    /// <summary>Seed an animal leaves in a cell (in dung, a cache, from its fur): into the bank of its stratum, or lost in water.</summary>
    public static void Deposit(World w, int cell, int mom, int dad, float m, int vector)
    {
        if (m <= 0f) return;
        var t = w.Flora;
        if (vector == 0) t.GutSeed += m; else if (vector == 1) t.CacheSeed += m; else t.BurrSeed += m;
        if (w.Cell[cell] != (byte)Realm.Land || mom == 0 || t.Used[mom] == 0) { w.Pool[(int)Pool.Detritus][cell] += m; return; }
        if (dad == 0 || t.Used[dad] == 0) dad = mom;
        int s = (int)t.P(mom, Ph.STRAT);
        BankAdd(w, s, cell, mom, dad, m, Rng.U(w.Seed, (uint)w.Step, (uint)cell, Rs.PlantPick + 8 + (uint)vector));
        int n = Map.FW * Map.FW;
        w.Pool[(int)Pool.Seed][cell] = w.BankM[cell] + w.BankM[n + cell] + w.BankM[2 * n + cell];
    }

    /// <summary>Burrs (RESOLVE, each animal on land each step): hooked seed in the banks catch in its coat (more on a big,
    /// furry body; Sorensen 1986; Couvreur 2004) and fall off at BURR_DROP per step.</summary>
    public static void Burrs(World w, int a, int cell)
    {
        float fur = w.FurSeed[a];
        if (fur > 0f)
        {
            float d = fur * K.BURR_DROP;
            w.FurSeed[a] = fur - d;
            Deposit(w, cell, w.FurSeedG[a], w.FurSeedDad[a], d, 2);
            if (w.FurSeed[a] < 1e-9f) { w.FurSeed[a] = 0f; w.FurSeedG[a] = 0; w.FurSeedDad[a] = 0; }
        }
        if (w.Cell[cell] != (byte)Realm.Land) return;
        int n = Map.FW * Map.FW; var t = w.Flora;
        float coat = MathK.Pow(w.MassOf(a), 2f / 3f) * (0.3f + w.PInsul[a]);
        for (int s = 0; s < 3; s++)
        {
            int bo = s * n + cell, mom = w.BankMom[bo];
            if (mom == 0) continue;
            float hook = t.P(mom, Ph.HOOK);
            if (hook <= 0f) continue;
            float m = MathF.Min(0.5f * w.BankM[bo], K.BURR_PICK * hook * w.BankM[bo] * coat);
            if (m <= 0f) continue;
            w.BankM[bo] -= m;
            float u = Rng.U(w.Seed, (uint)w.Step, (uint)a, Rs.PlantPick + 11 + (uint)s);
            w.FurSeed[a] += m;
            if (w.FurSeedG[a] == 0 || u * w.FurSeed[a] < m) { w.FurSeedG[a] = (ushort)mom; w.FurSeedDad[a] = w.BankDad[bo]; }
        }
        w.Pool[(int)Pool.Seed][cell] = w.BankM[cell] + w.BankM[n + cell] + w.BankM[2 * n + cell];
    }

    /// <summary>A new animal carries no seed or pollen.</summary>
    public static void ClearAnimal(World w, int c)
    {
        w.GutSeedG[c] = 0; w.GutSeedDad[c] = 0; w.FurSeedG[c] = 0; w.FurSeedDad[c] = 0; w.PollenG[c] = 0; w.FurSeed[c] = 0f;
    }

    // ------------------------------------------------------------------ yield (founding)

    /// <summary>The land part of Fields.Yield: each plant pool's sustained production in cell i from its slots' growth, at
    /// the stock of maximum sustained yield, times the energy multiplier of its plants.</summary>
    public static float Yield(World w, int i, ReadOnlySpan<float> diet)
    {
        var t = w.Flora; var Pe = t.Phe; int NP = PlantTable.NP;
        float W = w.Moisture[i], N = w.Nutrient[i], T = w.Temp[i];
        float g = w.Pool[(int)Pool.Grass][i], s = w.Shrub[i], tr = w.Tree[i];
        float y = 0f, dB = 0f, dF = 0f, dSd = 0f, dNc = 0f, dR = 0f, dL = 0f, fB = 0f;
        float lH = MathF.Max(0.02f, 1f - 0.85f * tr - 0.35f * s);
        for (int k = 0; k < 3; k++)
        {
            int id = w.POcc[i * SLOTS + k];
            if (id == 0) continue;
            int o = id * NP;
            float b = k == HERB ? g : k == SHRUB ? s : tr, r0 = Pe[o + Ph.R0];
            float L = k == HERB ? lH : k == SHRUB ? MathF.Max(0.02f, 1f - 0.7f * tr) : 1f;
            float G0 = Gross(Pe, o, r0, WaterOf(Pe, o, W, 0f), N, T, L, 1f, out _, out _), fe = G0 / (r0 * (1f + K.RESP));
            float em = Pe[o + Ph.EMUL];
            float R = Pe[o + Ph.AR] * K.REPRO_RATE * Nature.Plants * fe * b;
            float nect = Pe[o + Ph.NECT] * R, sm = (R - nect) * Pe[o + Ph.S0] / Pe[o + Ph.CS];
            dNc += nect; dF += Pe[o + Ph.FLESH] * sm * (1f + Pe[o + Ph.PULP]) * Pe[o + Ph.EFRUIT]; dSd += (1f - Pe[o + Ph.FLESH]) * sm;
            dL += Pe[o + Ph.CLIT] * b;
            if (k == HERB)
            {
                float room = MathF.Max(0f, 1f - s - tr);
                y += diet[(int)Pool.Grass] * Pools.Rho[(int)Pool.Grass] * em * MathF.Max(0f, G0 - K.RESP * r0 * Pe[o + Ph.LES]) * 0.25f * room * room;
                dR += K.R_R * Nature.Plants * fe * b * 0.25f;
            }
            else { float c = b * Reach(Pe, o, false); dB += K.R_B * Nature.Plants * fe * 0.5f * c * em; fB += c; if (k == SHRUB) dR += K.R_R * Nature.Plants * fe * b * 0.25f; }
        }
        y += diet[(int)Pool.Browse] * Pools.Rho[(int)Pool.Browse] * dB;
        y += diet[(int)Pool.Fruit] * Pools.Rho[(int)Pool.Fruit] * dF;
        y += diet[(int)Pool.Seed] * Pools.Rho[(int)Pool.Seed] * dSd;
        y += diet[(int)Pool.Nectar] * Pools.Rho[(int)Pool.Nectar] * dNc;
        y += diet[(int)Pool.Root] * Pools.Rho[(int)Pool.Root] * dR;
        y += diet[(int)Pool.Litter] * Pools.Rho[(int)Pool.Litter] * dL;
        return y;
    }

    // ------------------------------------------------------------------ reporting

    /// <summary>Mean level of every trait over the occupied slots of a stratum (0 herb, 1 shrub, 2 tree and sapling), weighted by cover.</summary>
    public static double[] MeanTraits(World w, int stratum, out double cover)
    {
        var t = w.Flora; int n = Map.FW * Map.FW;
        var m = new double[PT.N]; cover = 0;
        for (int i = 0; i < n; i++)
        {
            if (w.Cell[i] != (byte)Realm.Land) continue;
            int k = stratum;
            int id = w.POcc[i * SLOTS + k];
            float b = k == HERB ? w.Pool[(int)Pool.Grass][i] : k == SHRUB ? w.Shrub[i] : w.Tree[i];
            if (id == 0 || b <= 0f) continue;
            cover += b;
            for (int q = 0; q < PT.N; q++) m[q] += b * t.Level(id, q);
        }
        if (cover > 0) for (int q = 0; q < PT.N; q++) m[q] /= cover;
        return m;
    }

    /// <summary>Distinct genotypes now in the slots (live plants), and in the table.</summary>
    public static int SlotGenotypes(World w)
    {
        var seen = new System.Collections.Generic.HashSet<int>();
        foreach (var g in w.POcc) if (g != 0) seen.Add(g);
        return seen.Count;
    }

    /// <summary>The flora report: genotypes, table, establishment, dispersal by vector, traits per stratum, the largest lineages.</summary>
    public static string Report(World w)
    {
        var t = w.Flora; var sb = new System.Text.StringBuilder();
        int n = Map.FW * Map.FW;
        sb.AppendLine($"FLORA at step {w.Step:N0}: {SlotGenotypes(w):N0} genotypes in plants, {t.Live:N0} in the table of {PlantTable.CAP - 1:N0} ({t.TableFull:N0} recruits kept their parent's genotype because it was full, {t.PendFull:N0} by the queue)");
        sb.AppendLine($"  establishments {t.Establish:N0}, crosses {t.Crosses:N0}, failed crosses {t.FailedCross:N0}, mutations {t.Mutations:N0}, genotypes created {t.Created:N0}, freed {t.Freed:N0}");
        sb.AppendLine($"  seed dispersed: wind {t.WindSeed:F1}, animal guts {t.GutSeed:F2}, caches {t.CacheSeed:F2}, burrs {t.BurrSeed:F3}; landed in water {t.WaterSeed:F2}");
        if (t.HistN > 0)
        {
            sb.Append("  live genotypes at each census (every ").Append(K.PLANT_CENSUS).Append(" steps, last ").Append(Math.Min(t.HistN, 16)).Append("):");
            for (int k = Math.Max(0, t.HistN - 16); k < t.HistN; k++) sb.Append(' ').Append(t.History[k % t.History.Length]);
            sb.AppendLine();
        }
        sb.Append("  mean trait levels (0..15) by stratum, weighted by cover:\n    stratum  cover   ");
        for (int q = 0; q < PT.N; q++) sb.Append(PT.Names[q].PadLeft(8));
        sb.AppendLine();
        string[] sn = { "herb", "shrub", "tree" };
        for (int s = 0; s < 3; s++)
        {
            var m = MeanTraits(w, s, out double c);
            sb.Append($"    {sn[s],-7} {c,7:F0}  ");
            for (int q = 0; q < PT.N; q++) sb.Append(m[q].ToString("F1").PadLeft(8));
            sb.AppendLine();
        }
        // lineages: cover held by each founder's descendants
        var lc = new double[K.PLANT_FOUNDERS]; var lg = new System.Collections.Generic.HashSet<int>[K.PLANT_FOUNDERS];
        for (int i = 0; i < n; i++)
            for (int k = 0; k < 3; k++)
            {
                int id = w.POcc[i * SLOTS + k]; if (id == 0) continue;
                float b = k == HERB ? w.Pool[(int)Pool.Grass][i] : k == SHRUB ? w.Shrub[i] : w.Tree[i];
                int l = t.Lineage[id] % K.PLANT_FOUNDERS; lc[l] += b; (lg[l] ??= new()).Add(id);
            }
        var order = new int[K.PLANT_FOUNDERS]; for (int k = 0; k < order.Length; k++) order[k] = k;
        Array.Sort(order, (a, b) => lc[b].CompareTo(lc[a]));
        int alive = 0; foreach (var v in lc) if (v > 0) alive++;
        sb.AppendLine($"  founding lineages with plants: {alive} of {K.PLANT_FOUNDERS}; the largest (cover, genotypes, height, stratum):");
        for (int r = 0; r < Math.Min(8, alive); r++)
        {
            int l = order[r]; int any = 0; foreach (var g in lg[l]!) { any = g; break; }
            sb.AppendLine($"    lineage {l,3}: cover {lc[l],8:F0}  genotypes {lg[l]!.Count,5}  e.g. height {t.P(any, Ph.H):F1} m, {sn[(int)t.P(any, Ph.STRAT)]}");
        }
        return sb.ToString();
    }
}
