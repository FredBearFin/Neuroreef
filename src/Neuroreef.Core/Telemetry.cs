// Telemetry (DESIGN §12; NR-401, NR-402). Counters are incremented by the serial phases. Every
// SAMPLE_EVERY steps a sample (a fixed layout of floats) is written into a preallocated ring.
// Per-window quantities (births, deaths, intake...) are stored as the amount during that window.

using System;
using System.Collections.Generic;

namespace Neuroreef;

public sealed class Telemetry
{
    readonly World _w;
    public readonly int NS;
    // ---- running counters (cumulative)
    public readonly long[] Created, Births, Conceptions, Deaths, KillMatrix, Strikes, Kills, Matings, Lays, Immigrants, Infections;
    public readonly double[] Intake, SpendBasal, SpendMove, SpendGrowth, SpendRepro, RewardSum, CareGiven;
    public long SlotShortage, FullSteps;
    public ulong LastFullStep = ulong.MaxValue;
    public long Splits;                     // speciation events (DESIGN §7.9)
    public long FireCells;
    public readonly long[] DeathsByAge;     // species * 4: before birth, juvenile, young adult (< 2x maturity), older adult
    public readonly double[] AgeAtDeath;    // sum of age at death of hatched animals
    // ---- previous cumulative values, to form per-window deltas
    readonly long[] _pBirths, _pDeaths, _pKill, _pStrikes, _pKills, _pMatings, _pImm, _pConc;
    readonly double[] _pIntake, _pBasal, _pMove, _pReward;
    long _pFire;
    readonly long[] _pDAge; readonly double[] _pAge;
    readonly long[] _pInf; readonly double[] _pCare;

    // ---- sample layout
    public readonly string[] Names;
    public readonly int Len;
    public readonly int OffGlobal, OffSpecies, PerSpecies, OffKill;
    public const int S_COUNT = 0, S_JUV = 1, S_EGGS = 2, S_GEN = 3, S_ENERGY = 4, S_REWARD = 5, S_LEARNED = 6, S_BIRTHS = 7,
        S_CONC = 8, S_DEATHS = 9, S_INTAKE = S_DEATHS + Cause.N, S_SPEND = S_INTAKE + Pools.N, S_STRIKES = S_SPEND + 2, S_KILLS = S_STRIKES + 1,
        S_MATINGS = S_KILLS + 1, S_IMM = S_MATINGS + 1, S_DAGE = S_IMM + 1, S_MEANAGE = S_DAGE + 4,
        S_SICK = S_MEANAGE + 1, S_INFECT = S_SICK + 1, S_CARE = S_INFECT + 1, S_KIT = S_CARE + 1, S_ARCH = S_KIT + 1, S_TRAITS = S_ARCH + 1, S_GAINS = S_TRAITS + NTRAITS, S_END = S_GAINS + P.N;
    /// <summary>Evolving traits recorded per species (mean over living animals): body multipliers (1 = founder) and diet.</summary>
    public const int NTRAITS = 24, T_MASS = 0, T_LEGS = 1, T_FINS = 2, T_WINGS = 3, T_SPEED = 4, T_SIGHT = 5, T_ARMOUR = 6, T_STRIKE = 7,
        T_LITTER = 8, T_CARE = 9, T_MEAT = 10, T_SEXUAL = 11, T_LIVE = 12, T_LEGN = 13, T_LIFE = 14, T_MATURE = 15, T_BRAIN = 16,
        T_HERM = 17, T_FEMALE = 18, T_IMMUNE = 19, T_FINN = 20, T_EYEN = 21, T_VIR = 22, T_WATER = 23;
    public static readonly string[] TraitNames = { "mass", "legs", "fins", "wings", "speed", "sight", "armour", "strike", "litter", "care",
        "meat-eating", "sexual share", "live-birth share", "legs (count)", "lifespan", "age at maturity", "brain units",
        "hermaphrodite share", "daughter share", "immunity", "fin pairs", "eye pairs", "pathogen virulence", "water economy" };
    public const int G_STEP = 0, G_POOLS = 1, G_SHRUB = G_POOLS + Pools.NFIELD, G_TREE = G_SHRUB + 1, G_SAPLING = G_TREE + 1,
        G_NUT_LAND = G_SAPLING + 1, G_NUT_WATER = G_NUT_LAND + 1, G_FIRE = G_NUT_WATER + 1, G_BURNING = G_FIRE + 1, G_ALIVE = G_BURNING + 1,
        G_SEASON = G_ALIVE + 1, G_RAIN = G_SEASON + 1, G_END = G_RAIN + 1;

    public readonly float[] Ring;          // RING * Len
    public readonly float[] LongRing;      // LONG_RING * Len: one sample in LONG_EVERY, for runs of weeks
    public int LongHead;
    public volatile int Head;              // number of samples written (ring index = (Head-1) % RING)
    readonly float[] _acc;                 // scratch per sample
    readonly int[] _cnt;
    public readonly float[] KillFlashCells = new float[256]; public int KillFlashN;

    public Telemetry(World w)
    {
        _w = w; NS = w.NS;
        int ns = NS;
        Created = new long[ns]; Births = new long[ns]; Conceptions = new long[ns]; Deaths = new long[ns * Cause.N];
        KillMatrix = new long[ns * ns]; Strikes = new long[ns]; Kills = new long[ns]; Matings = new long[ns]; Lays = new long[ns]; Immigrants = new long[ns]; Infections = new long[ns]; CareGiven = new double[ns]; _pInf = new long[ns]; _pCare = new double[ns]; DeathsByAge = new long[ns * 4]; AgeAtDeath = new double[ns]; _pDAge = new long[ns * 4]; _pAge = new double[ns];
        Intake = new double[ns * Pools.N]; SpendBasal = new double[ns]; SpendMove = new double[ns]; SpendGrowth = new double[ns]; SpendRepro = new double[ns]; RewardSum = new double[ns];
        _pBirths = new long[ns]; _pDeaths = new long[ns * Cause.N]; _pKill = new long[ns * ns]; _pStrikes = new long[ns]; _pKills = new long[ns];
        _pMatings = new long[ns]; _pImm = new long[ns]; _pConc = new long[ns];
        _pIntake = new double[ns * Pools.N]; _pBasal = new double[ns]; _pMove = new double[ns]; _pReward = new double[ns];

        var names = new List<string>();
        names.Add("step");
        for (int p = 0; p < Pools.NFIELD; p++) names.Add("pool." + Pools.Names[p]);
        names.AddRange(new[] { "cover.shrub", "cover.tree", "cover.sapling", "nutrient.land", "nutrient.water", "fire.ignited", "fire.burning", "agents.alive", "season", "rain.anomaly" });
        OffGlobal = 0;
        OffSpecies = names.Count;
        PerSpecies = S_END;
        foreach (var sp in w.Sp)
        {
            string n = "clade" + sp.Id.ToString("00") + ".";
            names.AddRange(new[] { n + "count", n + "juveniles", n + "eggs", n + "generation", n + "energy", n + "reward", n + "learned", n + "births", n + "conceptions" });
            foreach (var c in Cause.Names) names.Add(n + "death." + c);
            foreach (var p in Pools.Names) names.Add(n + "intake." + p);
            names.AddRange(new[] { n + "spend.basal", n + "spend.move", n + "strikes", n + "kills", n + "matings", n + "immigrants",
                n + "deaths.unborn", n + "deaths.juvenile", n + "deaths.young-adult", n + "deaths.old-adult", n + "age-at-death",
                n + "infected", n + "infections", n + "care.given", n + "evo.kit-genes", n + "evo.best-score" });
            foreach (var t in TraitNames) names.Add(n + "trait." + t);
            foreach (var p in P.Names) names.Add(n + "gain." + p);
        }
        OffKill = names.Count;
        foreach (var a in w.Sp) foreach (var b in w.Sp) names.Add("kill.clade" + a.Id.ToString("00") + ".clade" + b.Id.ToString("00"));
        Names = names.ToArray();
        Len = Names.Length;
        Ring = new float[K.RING * Len];
        LongRing = new float[K.LONG_RING * Len];
        _acc = new float[Len];
        _cnt = new int[ns];
    }

    public void KillFlash(int cell)
    {
        if (KillFlashN < KillFlashCells.Length) KillFlashCells[KillFlashN++] = cell;
    }

    // Fights, for the console to show (not a rule): a ring of the latest strikes, FIGHT_F floats each
    // (x, y, attacker clade, victim clade, kind, step). FightSeq counts every fight ever recorded.
    public const int FIGHT_N = 512, FIGHT_F = 6;
    public const int F_MISS = 0, F_WOUND = 1, F_KILL = 2, F_BITE = 3, F_DRAINED = 4, F_EGG = 5;
    public readonly float[] Fights = new float[FIGHT_N * FIGHT_F];
    public long FightSeq;

    public void Fight(float x, float y, int attacker, int victim, int kind, ulong step)
    {
        int o = (int)(FightSeq % FIGHT_N) * FIGHT_F;
        Fights[o] = x; Fights[o + 1] = y; Fights[o + 2] = attacker; Fights[o + 3] = victim; Fights[o + 4] = kind; Fights[o + 5] = step;
        FightSeq++;
    }

    /// <summary>Copy of sample k (0 = oldest kept) into dst. Returns false if not available.</summary>
    public bool Sample(int k, Span<float> dst)
    {
        int head = Head, n = Math.Min(head, K.RING);
        if (k < 0 || k >= n) return false;
        int idx = (head - n + k) % K.RING;
        new ReadOnlySpan<float>(Ring, idx * Len, Len).CopyTo(dst);
        return true;
    }

    public int SampleCount => Math.Min(Head, K.RING);

    /// <summary>The thinned long history (one sample in LONG_EVERY): k = 0 is the oldest kept.</summary>
    public bool LongSample(int k, Span<float> dst)
    {
        int head = LongHead, n = Math.Min(head, K.LONG_RING);
        if (k < 0 || k >= n) return false;
        new ReadOnlySpan<float>(LongRing, ((head - n + k) % K.LONG_RING) * Len, Len).CopyTo(dst);
        return true;
    }

    public int LongCount => Math.Min(LongHead, K.LONG_RING);

    /// <summary>Take a sample (every SAMPLE_EVERY steps). Serial; no allocation.</summary>
    public void Take()
    {
        var w = _w;
        var a = _acc;
        Array.Clear(a);
        Array.Clear(_cnt);
        int FW2 = Map.FW * Map.FW;
        a[G_STEP] = w.Step;
        double nl = 0, nw = 0; int land = 0, water = 0, burning = 0;
        for (int i = 0; i < FW2; i++)
        {
            for (int p = 0; p < Pools.NFIELD; p++) a[G_POOLS + p] += w.Pool[p][i];
            if (w.Cell[i] == (byte)Realm.Land)
            {
                land++; nl += w.Nutrient[i];
                a[G_SHRUB] += w.Shrub[i]; a[G_TREE] += w.Tree[i]; a[G_SAPLING] += w.Sapling[i];
            }
            else { water++; nw += w.Nutrient[i]; }
            if (w.Fire[i] > 0) burning++;
        }
        a[G_SHRUB] /= MathF.Max(1, land); a[G_TREE] /= MathF.Max(1, land); a[G_SAPLING] /= MathF.Max(1, land);
        a[G_NUT_LAND] = (float)(nl / Math.Max(1, land)); a[G_NUT_WATER] = (float)(nw / Math.Max(1, water));
        a[G_FIRE] = FireCells - _pFire; _pFire = FireCells;
        a[G_BURNING] = burning;
        a[G_ALIVE] = w.AliveCount;
        a[G_SEASON] = w.SeasonAt(Map.W * 0.5f);
        a[G_RAIN] = w.RainAnom;

        // per-animal statistics
        for (int i = 0; i < w.Cap; i++)
        {
            byte st = w.StageOf[i];
            if (st == St.Dead) continue;
            int s = w.Spc[i], o = OffSpecies + s * PerSpecies;
            if (st <= St.Embryo) { a[o + S_EGGS]++; continue; }
            _cnt[s]++;
            a[o + S_COUNT]++;
            if (st == St.Juvenile) a[o + S_JUV]++;
            if (w.Infect[i] > 0) a[o + S_SICK]++;
            // is it evolving? mean strength of the starter-kit reflex genes (founders start near KIT_W)
            int kb = i * G.LEN + G.REFLEX; float ks = 0;
            foreach (var (c, p) in Reflex.Kit) ks += w.Genome[kb + p * G.NC + c];
            a[o + S_KIT] += ks / Reflex.Kit.Length;
            int bg = i * G.LEN;
            a[o + S_TRAITS + T_MASS] += w.PM[i];
            a[o + S_TRAITS + T_LEGS] += w.PLegs[i];
            a[o + S_TRAITS + T_FINS] += w.PFins[i];
            a[o + S_TRAITS + T_WINGS] += w.PWings[i];
            a[o + S_TRAITS + T_SPEED] += Body.Factor(w.Genome[bg + G.B_SPEED], K.BODY_RANGE_TRAIT);
            a[o + S_TRAITS + T_SIGHT] += Body.Factor(w.Genome[bg + G.B_SIGHT], K.BODY_RANGE_TRAIT);
            a[o + S_TRAITS + T_ARMOUR] += w.PArmour[i];
            a[o + S_TRAITS + T_STRIKE] += w.PStrike[i];
            a[o + S_TRAITS + T_LITTER] += w.PLitter[i];
            a[o + S_TRAITS + T_CARE] += w.PCare[i];
            a[o + S_TRAITS + T_MEAT] += w.PDiet[i * Pools.N + (int)Pool.Meat];
            a[o + S_TRAITS + T_SEXUAL] += w.PSexual[i];
            a[o + S_TRAITS + T_LIVE] += w.PLive[i];
            a[o + S_TRAITS + T_LEGN] += w.PLegs[i] >= K.LIMB_MIN ? 2 * w.PLegPairs[i] : 0;
            a[o + S_TRAITS + T_LIFE] += w.PLife[i];
            a[o + S_TRAITS + T_MATURE] += w.PMature[i];
            a[o + S_TRAITS + T_BRAIN] += w.PBrain[i];
            a[o + S_TRAITS + T_HERM] += w.PHerm[i];
            a[o + S_TRAITS + T_FEMALE] += w.PSexRatio[i];
            a[o + S_TRAITS + T_IMMUNE] += w.PImmune[i];
            a[o + S_TRAITS + T_WATER] += w.PWaterEcon[i];
            a[o + S_TRAITS + T_FINN] += w.PFinPairs[i];
            a[o + S_TRAITS + T_EYEN] += w.PEyePairs[i];
            a[o + S_TRAITS + T_VIR] += w.Virulence[i];                // summed over the infected (others are 0)
            a[o + S_GEN] += w.Gen[i];
            a[o + S_ENERGY] += w.Energy[i] / w.EmaxOf(i);
            a[o + S_REWARD] += w.RewardMean[i];
            int g0 = i * P.N;
            for (int p = 0; p < P.N; p++) a[o + S_GAINS + p] += w.Gain[g0 + p];
            if ((i & 7) == 0)
            {
                // learned change |W2f - genome W2|, sampled on one slot in eight
                float d = 0; int gb = i * G.LEN + G.W2, wb = i * G.W2N;
                for (int k = 0; k < G.W2N; k += 4) d += MathF.Abs(w.W2f[wb + k] - w.Genome[gb + k] * K.WSCALE);
                a[o + S_LEARNED] += d / (G.W2N / 4) * 8f;
            }
        }
        for (int s = 0; s < NS; s++)
        {
            int o = OffSpecies + s * PerSpecies;
            float n = MathF.Max(1, _cnt[s]);
            a[o + S_GEN] /= n; a[o + S_ENERGY] /= n; a[o + S_REWARD] /= n; a[o + S_LEARNED] /= n; a[o + S_KIT] /= n;
            for (int k = 0; k < NTRAITS; k++) a[o + S_TRAITS + k] /= n;
            a[o + S_TRAITS + T_VIR] *= n / MathF.Max(1f, a[o + S_SICK]);   // mean over the infected only
            float best = 0;
            for (int k = 0; k < w.ArchiveCount[s]; k++) best = MathF.Max(best, w.ArchiveScore[s * K.ARCHIVE + k]);
            a[o + S_ARCH] = best;
            for (int p = 0; p < P.N; p++) a[o + S_GAINS + p] /= n;
            a[o + S_BIRTHS] = Births[s] - _pBirths[s]; _pBirths[s] = Births[s];
            a[o + S_CONC] = Conceptions[s] - _pConc[s]; _pConc[s] = Conceptions[s];
            for (int c = 0; c < Cause.N; c++) { int k = s * Cause.N + c; a[o + S_DEATHS + c] = Deaths[k] - _pDeaths[k]; _pDeaths[k] = Deaths[k]; }
            for (int p = 0; p < Pools.N; p++) { int k = s * Pools.N + p; a[o + S_INTAKE + p] = (float)(Intake[k] - _pIntake[k]); _pIntake[k] = Intake[k]; }
            a[o + S_SPEND] = (float)(SpendBasal[s] - _pBasal[s]); _pBasal[s] = SpendBasal[s];
            a[o + S_SPEND + 1] = (float)(SpendMove[s] - _pMove[s]); _pMove[s] = SpendMove[s];
            a[o + S_STRIKES] = Strikes[s] - _pStrikes[s]; _pStrikes[s] = Strikes[s];
            a[o + S_KILLS] = Kills[s] - _pKills[s]; _pKills[s] = Kills[s];
            a[o + S_MATINGS] = Matings[s] - _pMatings[s]; _pMatings[s] = Matings[s];
            a[o + S_IMM] = Immigrants[s] - _pImm[s]; _pImm[s] = Immigrants[s];
            a[o + S_INFECT] = Infections[s] - _pInf[s]; _pInf[s] = Infections[s];
            a[o + S_CARE] = (float)(CareGiven[s] - _pCare[s]); _pCare[s] = CareGiven[s];
            long hatched = 0;
            for (int c = 0; c < 4; c++) { int k = s * 4 + c; long d = DeathsByAge[k] - _pDAge[k]; a[o + S_DAGE + c] = d; if (c > 0) hatched += d; _pDAge[k] = DeathsByAge[k]; }
            a[o + S_MEANAGE] = hatched > 0 ? (float)((AgeAtDeath[s] - _pAge[s]) / hatched) : 0f; _pAge[s] = AgeAtDeath[s];
        }
        for (int k = 0; k < NS * NS; k++) { a[OffKill + k] = KillMatrix[k] - _pKill[k]; _pKill[k] = KillMatrix[k]; }

        int idx = Head % K.RING;
        Array.Copy(a, 0, Ring, idx * Len, Len);
        if (Head % K.LONG_EVERY == 0) { Array.Copy(a, 0, LongRing, (LongHead % K.LONG_RING) * Len, Len); LongHead++; }
        Head = Head + 1;
    }

    public int Index(string name) => Array.IndexOf(Names, name);
    public int SpeciesOffset(int s, int field) => OffSpecies + s * PerSpecies + field;
}
