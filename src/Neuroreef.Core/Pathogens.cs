// Pathogens (Revision R): living, evolving agents with no brain. Each infection carries its own pathogen genome
// (World.PgGene, World.Strain); a strain grows in its host against the host's immune response, sheds by the routes its
// genes weight, persists in the ground and water as its genes allow, and mutates when it passes to a new host. Nothing
// here says what a pathogen should become: virulence, stealth and host range are what the trade-offs leave.
//
// Collective decision without a brain: quorum sensing (Miller & Bassler 2001). The strain in a host makes a signal in
// proportion to its load; below its genetic threshold it stays stealthy (little toxin, little shedding, seen less by
// the immune system), above it it switches to toxin and shedding. The threshold evolves.
//
// Per step (LIFECYCLE, serial, Life.Run):
//   q    = L^n / (L^n + threshold^n)                    the quorum switch (n = PG_HILL)
//   mode = PG_STEALTH + (1 - PG_STEALTH) q              share of toxin and shedding expressed
//   det  = PG_DETECT + (1 - PG_DETECT) q                how well the immune system sees it
//   E'   = E + PG_RISE imm det L/(L + PG_HALF) (PG_NAIVE + E) - PG_E_DECAY E   clonal expansion
//   L'   = L exp(r fit (1 - L) - PG_KILL E det)         r: replication less persistence and breadth costs
//   D'   = D + PG_DMG toxin mode L - PG_REPAIR reserve  death when D >= 1
// Shedding PG_SHED L mode (1 + PG_TOX_SHED toxin) is split by the route genes: contact (to animals in reach, DECIDE),
// the ground of the host's cell, its dung, and water (the cell's or a neighbouring water cell's). The reservoir
// (World.ResLoad) decays by the strain's persistence, faster on dry land, and runs downstream along rivers.

using System;
using System.Linq;

namespace Neuroreef;

/// <summary>Layout of a pathogen genome (World.PgGene, ResGene): trait bytes 0..255, then its founding lineage.</summary>
public static class PG
{
    public const int REP = 0, TOX = 1, CONTACT = 2, ENV = 3, WATER = 4, DUNG = 5, PERS = 6, QS = 7, BREADTH = 8;
    public const int NTRAIT = 9, LINE = 9, N = 10;
    /// <summary>Dimensions of a strain for telemetry's clustering: the traits and the antigen.</summary>
    public const int D = NTRAIT + G.NMHC;
    public static readonly string[] Names = { "replication", "toxin", "contact", "environment", "water", "dung", "persistence", "quorum threshold", "host breadth" };
}

public static class Pathogens
{
    // gene byte -> expressed value (constant tables, made once)
    static readonly float[] RepT = new float[256], QsT = new float[256], DecayT = new float[256];
    static Pathogens()
    {
        for (int b = 0; b < 256; b++)
        {
            float u = b / 255f;
            RepT[b] = K.PG_REP_LO * MathK.Pow(K.PG_REP_HI / K.PG_REP_LO, u);
            QsT[b] = K.PG_QS_LO * MathK.Pow(K.PG_QS_HI / K.PG_QS_LO, u);
            DecayT[b] = K.PG_DECAY_HI * MathK.Pow(K.PG_DECAY_LO / K.PG_DECAY_HI, u);
        }
    }

    // random streams (offsets from Rs.Disease)
    const uint S_CONTACT = Rs.Disease + 200, S_TOUCH = Rs.Disease + 201, S_EAT = Rs.Disease + 202, S_DRINK = Rs.Disease + 203,
        S_DEP = Rs.Disease + 210, S_FLOW = Rs.Disease + 220, S_SEED = Rs.Disease + 230, S_MUT = Rs.Disease + 300;

    public static float Rep(byte b) => RepT[b];
    public static float Threshold(byte b) => QsT[b];
    public static float Decay(byte b) => DecayT[b];
    static float U8(byte b) => b * (1f / 255f);

    /// <summary>The quorum switch at load L for threshold theta: 0 stealthy, 1 quorate.</summary>
    public static float Quorum(float L, float theta)
    {
        if (L <= 0f) return 0f;
        float x = MathK.Pow(L / theta, K.PG_HILL);
        return x / (1f + x);
    }

    /// <summary>Growth rate of a strain before host fit: replication less what persistence and breadth cost it.</summary>
    public static float Growth(byte[] g, int o)
        => RepT[g[o + PG.REP]] * (1f - K.PG_PERS_COST * U8(g[o + PG.PERS])) * (1f - K.PG_GEN_COST * U8(g[o + PG.BREADTH]));

    /// <summary>How well an antigen (key[ko..]) fits host h's recognition genes, with the strain's breadth gene.</summary>
    public static float Fit(World w, int h, sbyte[] key, int ko, byte breadth)
    {
        float d2 = 0; int gh = h * G.LEN + G.B_MHC;
        for (int k = 0; k < G.NMHC; k++) { float d = (key[ko + k] - w.Genome[gh + k]) * (1f / 127f); d2 += d * d; }
        float wd = K.MHC_W * (1f + K.PG_BREADTH * U8(breadth));
        return MathK.Exp(-d2 / G.NMHC / (wd * wd));
    }

    /// <summary>Host h's cross-immunity to an antigen: its memory's strength, falling with antigenic distance.</summary>
    public static float Cross(World w, int h, sbyte[] key, int ko)
    {
        float m = w.ImmMem[h];
        if (m <= 0f) return 0f;
        float d2 = 0; int hm = h * G.NMHC;
        for (int k = 0; k < G.NMHC; k++) { float d = (key[ko + k] - w.ImmKey[hm + k]) * (1f / 127f); d2 += d * d; }
        return m * MathK.Exp(-d2 / G.NMHC / (K.PG_CROSS_W * K.PG_CROSS_W));
    }

    /// <summary>The spread of host h's recognition genes (RMS about their mean, /127): its MHC diversity, 0..1.</summary>
    public static float Diversity(World w, int h)
    {
        int gh = h * G.LEN + G.B_MHC; float m = 0;
        for (int k = 0; k < G.NMHC; k++) m += w.Genome[gh + k];
        m /= G.NMHC; float v = 0;
        for (int k = 0; k < G.NMHC; k++) { float d = w.Genome[gh + k] - m; v += d * d; }
        return MathF.Min(1f, MathF.Sqrt(v / G.NMHC) * (1f / 127f));
    }

    /// <summary>Extra basal cost of host i now, as a share of basal: what the pathogen takes and what fighting it costs.</summary>
    public static float Sickness(World w, int i) => K.PG_COST_LOAD * w.PgLoad[i] + K.PG_COST_IMM * w.ImmEff[i];

    /// <summary>Malaise host i feels (as pain): the immune response (cytokines) and its tissue damage.</summary>
    public static float Malaise(World w, int i) => MathF.Min(1f, K.PG_MALAISE * w.ImmEff[i] + w.PgDamage[i]);

    static bool WetCell(World w, int c) => (w.Cell[c] & (byte)Realm.Water) != 0 || w.Wet[c] != 0;

    /// <summary>The first of cell c's eight neighbours that holds water (-1 none).</summary>
    static int NearWater(World w, int c)
    {
        int x = c % Map.FW, y = c / Map.FW;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int n = World.WrapI(y + dy, Map.FW) * Map.FW + World.WrapI(x + dx, Map.FW);
                if (WetCell(w, n)) return n;
            }
        return -1;
    }

    // ------------------------------------------------------------------ infection

    /// <summary>
    /// Expose host i to a dose of the strain at (gene[go..], key[ko..]). Infection follows the exponential dose-response
    /// (Haas 1983), times how well the strain fits it, less its cross-immunity and its immunity gene's protection. A new
    /// infection takes the strain mutated. Returns true if it was infected.
    /// </summary>
    public static bool Expose(World w, int i, float dose, byte[] gene, int go, sbyte[] key, int ko, uint stream)
    {
        if (w.PgLoad[i] > 0f || dose <= 0f || Nature.Disease <= 0f) return false;
        float fit = Fit(w, i, key, ko, gene[go + PG.BREADTH]);
        float cross = Cross(w, i, key, ko);
        float protect = 1f - K.IMMUNE_PROTECT * w.PImmune[i];
        float h = K.PG_INF * Nature.Disease * dose * fit * (1f - cross) * protect;
        if (h <= 0f || Rng.U(w.Seed, (uint)w.Step, (uint)i, stream) >= 1f - MathK.Exp(-h)) return false;
        Infect(w, i, gene, go, key, ko, true);
        if (stream - S_CONTACT < 4u) w.T.InfByRoute[stream - S_CONTACT]++;
        return true;
    }

    /// <summary>Host i takes the strain at (gene[go..], key[ko..]), mutated if asked: load PG_INOC, and its memory recalled at once.</summary>
    public static void Infect(World w, int i, byte[] gene, int go, sbyte[] key, int ko, bool mutate)
    {
        uint seed = w.Seed, step = (uint)w.Step;
        int o = i * PG.N, ok = i * G.NMHC;
        for (int k = 0; k < PG.N; k++)
        {
            int v = gene[go + k];
            if (mutate && k < PG.NTRAIT) v = Math.Clamp((int)MathF.Round(v + K.PG_MUT * Rng.N(seed, step, (uint)i, S_MUT + (uint)k)), 0, 255);
            w.PgGene[o + k] = (byte)v;
        }
        for (int k = 0; k < G.NMHC; k++)
        {
            float v = key[ko + k];
            if (mutate) v += K.MHC_MUT * Rng.N(seed, step, (uint)i, S_MUT + 20u + (uint)k);
            w.Strain[ok + k] = (sbyte)Math.Clamp((int)MathF.Round(v), -127, 127);
        }
        w.PgFit[i] = Fit(w, i, w.Strain, ok, w.PgGene[o + PG.BREADTH]);
        w.PgLoad[i] = K.PG_INOC;
        w.ImmEff[i] = MathF.Max(w.ImmEff[i], K.PG_RECALL * Cross(w, i, w.Strain, ok));
        w.PgShed[i] = 0f;
        w.T.Infections[w.Spc[i]]++;
    }

    /// <summary>Host i picks up a share of the reservoir in cell c (touch, a bite, a drink): it leaves the cell, and may infect.</summary>
    public static void Pick(World w, int i, int c, float share, uint stream)
    {
        if (w.PgLoad[i] > 0f) return;
        float f = w.ResLoad[c];
        if (f <= 0f) return;
        float dose = share * f;
        w.ResLoad[c] = f - dose;
        Expose(w, i, dose, w.ResGene, c * PG.N, w.ResKey, c * G.NMHC, stream);
    }

    /// <summary>Host i bit food from cell c (RESOLVE): it swallows what lies there.</summary>
    public static void Ate(World w, int i, int c) => Pick(w, i, c, K.PG_EAT, S_EAT);

    /// <summary>Host i drank at cell c (RESOLVE): from c's water, or the neighbouring water cell holding the most pathogens.</summary>
    public static void Drank(World w, int i, int c)
    {
        int wc = WetCell(w, c) ? c : -1;
        if (wc < 0)
        {
            float best = -1f;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int n = World.WrapI(c / Map.FW + dy, Map.FW) * Map.FW + World.WrapI(c % Map.FW + dx, Map.FW);
                    if (WetCell(w, n) && w.ResLoad[n] > best) { best = w.ResLoad[n]; wc = n; }
                }
        }
        if (wc >= 0) Pick(w, i, wc, K.PG_DRINK, S_DRINK);
    }

    /// <summary>Put amount of the strain at (gene[go..], key[ko..]) into cell c. A cell holds one strain: the newcomer takes
    /// it over with probability amount / (load after), a lottery weighted by numbers.</summary>
    static void Deposit(World w, int c, float amount, byte[] gene, int go, sbyte[] key, int ko, uint slot, uint stream)
    {
        if (amount <= 0f) return;
        float f = w.ResLoad[c];
        if (f <= 0f || Rng.U(w.Seed, (uint)w.Step, slot, stream) * (f + amount) < amount)
        {
            Array.Copy(gene, go, w.ResGene, c * PG.N, PG.N);
            Array.Copy(key, ko, w.ResKey, c * G.NMHC, G.NMHC);
        }
        w.ResLoad[c] = f + amount;
    }

    /// <summary>A host died (Resolve.DieNoBody): the pathogens in its body stay in the ground where it lies.</summary>
    public static void OnDeath(World w, int i)
    {
        float L = w.PgLoad[i];
        if (L <= 0f) return;
        Deposit(w, World.FIdx(w.X[i], w.Y[i]), K.PG_CARCASS * L, w.PgGene, i * PG.N, w.Strain, i * G.NMHC, (uint)i, S_DEP + 5);
        w.PgLoad[i] = 0f; w.PgShed[i] = 0f;
    }

    /// <summary>A newborn slot: no infection, no memory, no damage.</summary>
    public static void Clear(World w, int c)
    {
        w.PgLoad[c] = 0f; w.PgFit[c] = 0f; w.ImmEff[c] = 0f; w.ImmMem[c] = 0f; w.PgDamage[c] = 0f; w.PgShed[c] = 0f;
        w.IDose[c] = 0f; w.IStrainSrc[c] = -1; w.IInfNear[c] = 0;
    }

    // ------------------------------------------------------------------ within the host

    /// <summary>
    /// One step of host i (LIFECYCLE): an uninfected host is exposed by contact and by the ground it stands on; an
    /// infected one runs the within-host dynamics, sheds, and recovers or dies. Returns true if it died.
    /// </summary>
    public static bool Host(World w, int i, int s)
    {
        float L = w.PgLoad[i];
        float reserve = Math.Clamp(w.Energy[i] / w.EmaxOf(i), 0f, 1f);
        if (w.ImmMem[i] > 0f) w.ImmMem[i] *= 1f - K.PG_MEM_DECAY;
        if (L <= 0f)
        {
            if (w.PgDamage[i] > 0f) w.PgDamage[i] = MathF.Max(0f, w.PgDamage[i] - K.PG_REPAIR * reserve);
            if (w.ImmEff[i] > 0f) { w.ImmEff[i] -= K.PG_E_DECAY * w.ImmEff[i]; if (w.ImmEff[i] < 1e-3f) w.ImmEff[i] = 0f; }
            int src = w.IStrainSrc[i];
            if (w.IDose[i] > 0f && src >= 0 && src != i && w.PgLoad[src] > 0f)
                Expose(w, i, w.IDose[i], w.PgGene, src * PG.N, w.Strain, src * G.NMHC, S_CONTACT);
            if (w.PgLoad[i] <= 0f)
            {
                int cell = World.FIdx(w.X[i], w.Y[i]);
                if (w.ResLoad[cell] > 0f) Pick(w, i, cell, K.PG_TOUCH, S_TOUCH);
            }
            return false;
        }

        var g = w.PgGene; int o = i * PG.N;
        float tox = U8(g[o + PG.TOX]);
        float q = Quorum(L, QsT[g[o + PG.QS]]);
        float mode = K.PG_STEALTH + (1f - K.PG_STEALTH) * q, det = K.PG_DETECT + (1f - K.PG_DETECT) * q;
        float E = w.ImmEff[i];
        float imm = (K.PG_IMM_BASE + w.PImmune[i]) * (1f + K.PG_MHC_DIV * w.D.MhcDiv[i]);   // Diversity(w, i), cached at Body.Express
        float r = Growth(g, o) * w.PgFit[i];
        float L1 = MathF.Min(1f, L * MathK.Exp(r * (1f - L) - K.PG_KILL * E * det));
        // clonal expansion (Antia, Levin & May 1994); the bound of 10 is a numerical guard only (killing at E = 10 is far
        // above any strain's growth, so a response clears its strain well before it)
        w.ImmEff[i] = MathF.Min(10f, E + K.PG_RISE * imm * det * L / (L + K.PG_HALF) * (K.PG_NAIVE + E) - K.PG_E_DECAY * E);
        float D = MathF.Max(0f, w.PgDamage[i] + K.PG_DMG * tox * mode * L - K.PG_REPAIR * reserve);
        w.PgDamage[i] = D;
        if (D >= 1f) { Resolve.Die(w, i, Cause.Disease, -1); return true; }
        if (L1 < K.PG_CLEAR)
        {
            // cleared: memory of this antigen replaces the last (one memory per host); the effector contracts from here
            w.PgLoad[i] = 0f; w.PgShed[i] = 0f;
            Array.Copy(w.Strain, i * G.NMHC, w.ImmKey, i * G.NMHC, G.NMHC);
            w.ImmMem[i] = 1f;
            return false;
        }
        w.PgLoad[i] = L1;

        // shedding, split by the route genes
        float shed = Shedding(w, i);
        float wc = g[o + PG.CONTACT], we = g[o + PG.ENV], ww = g[o + PG.WATER], wd = g[o + PG.DUNG], sum = wc + we + ww + wd;
        if (sum <= 0f) { wc = we = ww = wd = 1f; sum = 4f; }
        shed /= sum;
        w.PgShed[i] = shed * wc;
        int c = World.FIdx(w.X[i], w.Y[i]), go = o, ko = i * G.NMHC;
        Deposit(w, c, shed * we, g, go, w.Strain, ko, (uint)i, S_DEP);
        if (w.GutWaste[i] > 0f) Deposit(w, c, shed * wd * K.PG_DUNG, g, go, w.Strain, ko, (uint)i, S_DEP + 1);
        if (ww > 0f)
        {
            int wcell = WetCell(w, c) ? c : NearWater(w, c);
            if (wcell >= 0) Deposit(w, wcell, shed * ww, g, go, w.Strain, ko, (uint)i, S_DEP + 2);
        }
        return false;
    }

    /// <summary>What host i sheds per step by all routes: PG_SHED x load x quorum mode x (1 + PG_TOX_SHED x toxin).</summary>
    public static float Shedding(World w, int i)
    {
        float L = w.PgLoad[i];
        if (L <= 0f) return 0f;
        int o = i * PG.N;
        float q = Quorum(L, QsT[w.PgGene[o + PG.QS]]);
        return K.PG_SHED * L * (K.PG_STEALTH + (1f - K.PG_STEALTH) * q) * (1f + K.PG_TOX_SHED * U8(w.PgGene[o + PG.TOX]));
    }

    // ------------------------------------------------------------------ the reservoir

    /// <summary>The river network the reservoir flows down: each river cell (water above lake level) drains to its
    /// neighbour gathering the most rain (World.Flow), which is downstream; river cells sorted by falling flow are in
    /// downstream-first order. Built once per world (allocates a sort key once, as construction).</summary>
    public static void BuildNet(World w)
    {
        int FW = Map.FW, n = FW * FW, k = 0;
        for (int c = 0; c < n; c++)
        {
            w.ResRecv[c] = -1;
            if ((w.Cell[c] & (byte)Realm.Water) == 0 || w.Elev[c] < w.SeaLevel) continue;
            int x = c % FW, y = c / FW, best = -1; float bf = w.Flow[c];
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int j = World.WrapI(y + dy, FW) * FW + World.WrapI(x + dx, FW);
                    if ((w.Cell[j] & (byte)Realm.Water) != 0 && w.Flow[j] > bf) { bf = w.Flow[j]; best = j; }
                }
            w.ResRecv[c] = best;
            if (best >= 0) w.ResOrder[k++] = c;
        }
        var key = new float[k];
        for (int q = 0; q < k; q++) key[q] = -w.Flow[w.ResOrder[q]];
        Array.Sort(key, w.ResOrder, 0, k);
        w.ResRivers = k;
        w.ResNet = true;
    }

    /// <summary>The founding strains of a new world: PG_FOUNDERS random genomes, each laid in PG_SEED_CELLS random cells (a strain of the soil and water).</summary>
    public static void Seed(World w)
    {
        if (!w.ResNet) BuildNet(w);
        if (Nature.Disease <= 0f) return;
        uint seed = w.Seed;
        Span<byte> gene = stackalloc byte[PG.N];
        Span<sbyte> key = stackalloc sbyte[G.NMHC];
        int FW = Map.FW;
        for (int f = 0; f < K.PG_FOUNDERS; f++)
        {
            uint fk = (uint)f;
            for (int k = 0; k < PG.NTRAIT; k++) gene[k] = (byte)(Rng.U(seed, 0, fk, S_SEED + (uint)k) * 256f);
            gene[PG.LINE] = (byte)f;
            for (int k = 0; k < G.NMHC; k++) key[k] = (sbyte)(Rng.U(seed, 0, fk, S_SEED + 20u + (uint)k) * 254f - 127f);
            for (int t = 0; t < K.PG_SEED_CELLS; t++)
            {
                int c = (int)(Rng.U(seed, 0, fk, S_SEED + 40u + (uint)t) * FW * FW) % (FW * FW);
                gene.CopyTo(new Span<byte>(w.ResGene, c * PG.N, PG.N));
                key.CopyTo(new Span<sbyte>(w.ResKey, c * G.NMHC, G.NMHC));
                w.ResLoad[c] = K.PG_SEED;
            }
        }
    }

    /// <summary>The reservoir's step (end of LIFECYCLE): river water carries PG_FLOW of each cell's load one cell downstream
    /// (downstream first, so nothing moves twice), then every cell's load decays by its strain's persistence.</summary>
    public static void Field(World w)
    {
        if (!w.ResNet) BuildNet(w);
        var R = w.ResLoad;
        for (int k = 0; k < w.ResRivers; k++)
        {
            int c = w.ResOrder[k]; float f = R[c];
            if (f <= 0f) continue;
            float a = K.PG_FLOW * f;
            R[c] = f - a;
            Deposit(w, w.ResRecv[c], a, w.ResGene, c * PG.N, w.ResKey, c * G.NMHC, (uint)c, S_FLOW);
        }
        int n = R.Length;
        var gene = w.ResGene;
        for (int c = 0; c < n; c++)
        {
            float f = R[c];
            if (f <= 0f) continue;
            float d = DecayT[gene[c * PG.N + PG.PERS]];
            if ((w.Cell[c] & (byte)Realm.Water) == 0 && w.Wet[c] == 0) d = MathF.Min(1f, d * K.PG_DRY);
            f *= 1f - d;
            R[c] = f < K.PG_RES_MIN ? 0f : f;
        }
    }

    // ------------------------------------------------------------------ telemetry

    /// <summary>Global telemetry columns written by Census, from Telemetry.G_PG.</summary>
    public const int C_HOSTS = 0, C_STRAINS = 1, C_LINES = 2, C_STEALTH = 3, C_REP = 4, C_TOX = 5, C_QS = 6, C_PERS = 7, C_BREADTH = 8,
        C_CONTACT = 9, C_ENV = 10, C_WATER = 11, C_DUNG = 12, C_LOAD = 13, C_RES_CELLS = 14, C_RES_LOAD = 15, C_RES_PERS = 16, C_RES_TOX = 17, NCOL = 18;
    public static readonly string[] ColNames = { "infected", "strains", "lineages", "stealthy", "replication", "toxin", "quorum", "persistence", "breadth",
        "route.contact", "route.env", "route.water", "route.dung", "load", "reservoir.cells", "reservoir.load", "reservoir.persistence", "reservoir.toxin" };

    /// <summary>
    /// Pathogen census into a[o..o+NCOL): infected hosts and their strains' mean traits (replication as rate, quorum
    /// threshold as load, the rest 0..1, routes as shares), the share below quorum, strains (genomes clustered within
    /// PG_STRAIN_D, over hosts and up to 2,048 reservoir cells), founding lineages alive anywhere, and the reservoir.
    /// Serial; no allocation.
    /// </summary>
    public static void Census(World w, float[] a, int o)
    {
        for (int k = 0; k < NCOL; k++) a[o + k] = 0f;
        Span<ulong> lines = stackalloc ulong[4]; lines.Clear();
        int nc = 0, hosts = 0;
        double rep = 0, tox = 0, qs = 0, pers = 0, br = 0, rc = 0, re = 0, rw = 0, rd = 0, load = 0, stealth = 0;
        var g = w.PgGene;
        for (int i = 0; i < w.Cap; i++)
        {
            float L = w.PgLoad[i];
            if (L <= 0f || w.StageOf[i] == St.Dead) continue;
            hosts++; int b = i * PG.N;
            rep += RepT[g[b + PG.REP]]; tox += U8(g[b + PG.TOX]); float th = QsT[g[b + PG.QS]]; qs += th; pers += U8(g[b + PG.PERS]); br += U8(g[b + PG.BREADTH]);
            float sum = g[b + PG.CONTACT] + g[b + PG.ENV] + g[b + PG.WATER] + g[b + PG.DUNG];
            if (sum > 0f) { rc += g[b + PG.CONTACT] / sum; re += g[b + PG.ENV] / sum; rw += g[b + PG.WATER] / sum; rd += g[b + PG.DUNG] / sum; }
            load += L; if (Quorum(L, th) < 0.5f) stealth++;
            lines[g[b + PG.LINE] >> 6] |= 1UL << (g[b + PG.LINE] & 63);
            Cluster(w, g, b, w.Strain, i * G.NMHC, ref nc);
        }
        int cells = 0; double rl = 0, rp = 0, rt = 0;
        int n = w.ResLoad.Length;
        for (int c = 0; c < n; c++)
        {
            float f = w.ResLoad[c];
            if (f <= 0f) continue;
            cells++; rl += f; int b = c * PG.N;
            rp += U8(w.ResGene[b + PG.PERS]); rt += U8(w.ResGene[b + PG.TOX]);
            lines[w.ResGene[b + PG.LINE] >> 6] |= 1UL << (w.ResGene[b + PG.LINE] & 63);
        }
        // strains in the reservoir: up to 2,048 occupied cells, evenly spread over the map's order
        if (cells > 0)
        {
            int stride = Math.Max(1, cells / 2048), seen = 0;
            for (int c = 0; c < n; c++)
            {
                if (w.ResLoad[c] <= 0f) continue;
                if (seen++ % stride == 0) Cluster(w, w.ResGene, c * PG.N, w.ResKey, c * G.NMHC, ref nc);
            }
        }
        int nl = 0; for (int k = 0; k < 4; k++) nl += System.Numerics.BitOperations.PopCount(lines[k]);
        float hn = Math.Max(1, hosts);
        a[o + C_HOSTS] = hosts; a[o + C_STRAINS] = nc; a[o + C_LINES] = nl;
        a[o + C_STEALTH] = (float)(stealth / hn); a[o + C_REP] = (float)(rep / hn); a[o + C_TOX] = (float)(tox / hn); a[o + C_QS] = (float)(qs / hn);
        a[o + C_PERS] = (float)(pers / hn); a[o + C_BREADTH] = (float)(br / hn);
        a[o + C_CONTACT] = (float)(rc / hn); a[o + C_ENV] = (float)(re / hn); a[o + C_WATER] = (float)(rw / hn); a[o + C_DUNG] = (float)(rd / hn);
        a[o + C_LOAD] = (float)(load / hn);
        a[o + C_RES_CELLS] = cells; a[o + C_RES_LOAD] = (float)rl;
        a[o + C_RES_PERS] = cells > 0 ? (float)(rp / cells) : 0f; a[o + C_RES_TOX] = cells > 0 ? (float)(rt / cells) : 0f;
    }

    /// <summary>Leader clustering: a genome joins the first strain centre within PG_STRAIN_D, or starts a new one.</summary>
    static void Cluster(World w, byte[] gene, int go, sbyte[] key, int ko, ref int nc)
    {
        Span<float> v = stackalloc float[PG.D];
        for (int k = 0; k < PG.NTRAIT; k++) v[k] = gene[go + k] * (1f / 255f);
        for (int k = 0; k < G.NMHC; k++) v[PG.NTRAIT + k] = key[ko + k] * (1f / 127f);
        var C = w.PgCentre; float lim = K.PG_STRAIN_D * K.PG_STRAIN_D * PG.D;
        for (int c = 0; c < nc; c++)
        {
            float d2 = 0; int b = c * PG.D;
            for (int k = 0; k < PG.D && d2 < lim; k++) { float d = v[k] - C[b + k]; d2 += d * d; }
            if (d2 < lim) return;
        }
        if (nc >= K.PG_CLUSTERS) return;
        v.CopyTo(new Span<float>(C, nc * PG.D, PG.D));
        nc++;
    }

    /// <summary>The PATHOGENS section of a world report: the census over the samples kept, the lineages alive now, and
    /// the whole run's infections and deaths by disease. Reporting only.</summary>
    public static string Report(World w)
    {
        var T = w.T; var sb = new System.Text.StringBuilder();
        long inf = 0, dis = 0, all = 0;
        for (int s = 0; s < w.NS; s++) { inf += T.Infections[s]; dis += T.Deaths[s * Cause.N + Cause.Disease]; for (int c = 0; c < Cause.N; c++) all += T.Deaths[s * Cause.N + c]; }
        sb.AppendLine($"PATHOGENS at step {w.Step:N0}: {inf:N0} infections and {dis:N0} deaths by disease ({(all > 0 ? (double)dis / all : 0):P1} of all deaths) over the run");
        var ir = T.InfByRoute;
        sb.AppendLine($"  infections caught by contact {ir[0]:N0}, from the ground {ir[1]:N0}, with food {ir[2]:N0}, with water {ir[3]:N0}");
        sb.AppendLine("     STEP  INFECTED STRAINS LINES STEALTHY  REPLIC  TOXIN  QUORUM  PERSIST  BREADTH  ROUTES c/e/w/d      LOAD   RESERVOIR cells (load, persist)");
        var row = new float[T.Len]; int ns = T.SampleCount, o = Telemetry.G_PG;
        int rows = Math.Min(10, ns);
        for (int r = 0; r < rows; r++)
        {
            int k = rows == 1 ? ns - 1 : (int)((long)r * (ns - 1) / (rows - 1));
            T.Sample(k, row);
            sb.AppendLine($"  {row[Telemetry.G_STEP],7:F0}  {row[o + C_HOSTS],8:F0} {row[o + C_STRAINS],7:F0} {row[o + C_LINES],5:F0} {row[o + C_STEALTH],8:P0}  {row[o + C_REP],6:F3}  {row[o + C_TOX],5:F2}  {row[o + C_QS],6:F2}  {row[o + C_PERS],7:F2}  {row[o + C_BREADTH],7:F2}  " +
                $"{row[o + C_CONTACT]:F2}/{row[o + C_ENV]:F2}/{row[o + C_WATER]:F2}/{row[o + C_DUNG]:F2}  {row[o + C_LOAD],6:F3}   {row[o + C_RES_CELLS],6:F0} ({row[o + C_RES_LOAD]:F0}, {row[o + C_RES_PERS]:F2})");
        }
        // lineages alive now: hosts (by clade) and reservoir cells
        var hosts = new int[256]; var cells = new int[256]; var tox = new double[256]; var qs = new double[256];
        var cl = new System.Collections.Generic.Dictionary<int, int>[256];
        for (int i = 0; i < w.Cap; i++)
        {
            if (w.PgLoad[i] <= 0f || w.StageOf[i] == St.Dead) continue;
            int l = w.PgGene[i * PG.N + PG.LINE]; hosts[l]++; tox[l] += U8(w.PgGene[i * PG.N + PG.TOX]); qs[l] += QsT[w.PgGene[i * PG.N + PG.QS]];
            cl[l] ??= new(); cl[l].TryGetValue(w.Spc[i], out int m); cl[l][w.Spc[i]] = m + 1;
        }
        for (int c = 0; c < w.ResLoad.Length; c++) if (w.ResLoad[c] > 0f) cells[w.ResGene[c * PG.N + PG.LINE]]++;
        sb.AppendLine("  founding lineages alive now: hosts (mean toxin, quorum threshold), reservoir cells, host clades");
        int alive = 0;
        for (int l = 0; l < 256; l++)
        {
            if (hosts[l] == 0 && cells[l] == 0) continue;
            alive++;
            string by = cl[l] == null ? "" : string.Join(", ", cl[l].OrderByDescending(p => p.Value).Take(4).Select(p => $"{w.Sp[p.Key].Name} {p.Value}"));
            sb.AppendLine(hosts[l] > 0
                ? $"    lineage {l,3}: {hosts[l],6} hosts (toxin {tox[l] / hosts[l]:F2}, quorum {qs[l] / hosts[l]:F2})  {cells[l],6} cells   {by}"
                : $"    lineage {l,3}: {0,6} hosts                             {cells[l],6} cells");
        }
        if (alive == 0) sb.AppendLine("    none: every founding strain has died out");
        return sb.ToString();
    }
}
