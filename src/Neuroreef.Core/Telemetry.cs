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
    public readonly double[] Intake, SpendBasal, SpendMove, SpendGrowth, SpendRepro, SpendHeat, RewardSum, CareGiven;
    public long SlotShortage, FullSteps;
    public ulong LastFullStep = ulong.MaxValue;
    public long Splits;                     // speciation events (DESIGN §7.9)
    public long FireCells;
    public readonly long[] DeathsByAge;     // species * 4: before birth, juvenile, young adult (< 2x maturity), older adult
    public readonly double[] AgeAtDeath;    // sum of age at death of hatched animals
    // ---- previous cumulative values, to form per-window deltas
    readonly long[] _pBirths, _pDeaths, _pKill, _pStrikes, _pKills, _pMatings, _pImm, _pConc;
    readonly double[] _pIntake, _pBasal, _pMove, _pHeat, _pReward;
    long _pFire;
    readonly long[] _pDAge; readonly double[] _pAge;
    readonly long[] _pInf; readonly double[] _pCare;

    // ---- sample layout
    public readonly string[] Names;
    public readonly int Len;
    public readonly int OffGlobal, OffSpecies, PerSpecies, OffKill;
    public const int S_COUNT = 0, S_JUV = 1, S_EGGS = 2, S_GEN = 3, S_ENERGY = 4, S_REWARD = 5, S_LEARNED = 6, S_BIRTHS = 7,
        S_CONC = 8, S_DEATHS = 9, S_INTAKE = S_DEATHS + Cause.N, S_SPEND = S_INTAKE + Pools.N, S_STRIKES = S_SPEND + 3, S_KILLS = S_STRIKES + 1,
        S_MATINGS = S_KILLS + 1, S_IMM = S_MATINGS + 1, S_DAGE = S_IMM + 1, S_MEANAGE = S_DAGE + 4,
        S_SICK = S_MEANAGE + 1, S_INFECT = S_SICK + 1, S_CARE = S_INFECT + 1, S_KIT = S_CARE + 1, S_ARCH = S_KIT + 1, S_TRAITS = S_ARCH + 1, S_GAINS = S_TRAITS + NTRAITS, S_END = S_GAINS + P.N;
    /// <summary>Evolving traits recorded per species (mean over living animals): body multipliers (1 = founder) and diet.</summary>
    public const int NTRAITS = 32, T_MASS = 0, T_LEGS = 1, T_FINS = 2, T_WINGS = 3, T_SPEED = 4, T_SIGHT = 5, T_ARMOUR = 6, T_STRIKE = 7,
        T_LITTER = 8, T_CARE = 9, T_MEAT = 10, T_SEXUAL = 11, T_LIVE = 12, T_LEGN = 13, T_LIFE = 14, T_MATURE = 15, T_BRAIN = 16,
        T_HERM = 17, T_FEMALE = 18, T_IMMUNE = 19, T_FINN = 20, T_EYEN = 21, T_VIR = 22, T_WATER = 23, T_VOICE = 24, T_IMIT = 25,
        T_INSUL = 26, T_APPEND = 27, T_TOP = 28, T_FLOOR = 29, T_ENDO = 30, T_BREEDE = 31;   // fur, appendages, and the operative temperature its animals are in (Revision O)
    public static readonly string[] TraitNames = { "mass", "legs", "fins", "wings", "speed", "sight", "armour", "strike", "litter", "care",
        "meat-eating", "sexual share", "live-birth share", "legs (count)", "lifespan", "age at maturity", "brain units",
        "hermaphrodite share", "daughter share", "immunity", "fin pairs", "eye pairs", "pathogen virulence", "water economy", "voice", "vocal learning",
        "fur", "appendages", "operative temperature", "size floor", "warm-bloodedness", "breeding condition" };
    public const int G_STEP = 0, G_POOLS = 1, G_SHRUB = G_POOLS + Pools.NFIELD, G_TREE = G_SHRUB + 1, G_SAPLING = G_TREE + 1,
        G_NUT_LAND = G_SAPLING + 1, G_NUT_WATER = G_NUT_LAND + 1, G_FIRE = G_NUT_WATER + 1, G_BURNING = G_FIRE + 1, G_ALIVE = G_BURNING + 1,
        G_SEASON = G_ALIVE + 1, G_RAIN = G_SEASON + 1,
        G_TEMP = G_RAIN + 1, G_TMIN = G_TEMP + 1, G_TMAX = G_TMIN + 1, G_FROZEN = G_TMAX + 1, G_END = G_FROZEN + 1;   // land temperature: mean, lowest, highest, share frozen (Revision O)

    public readonly float[] Ring;          // RING * Len
    public readonly float[] LongRing;      // LONG_RING * Len: one sample in LONG_EVERY, for runs of weeks
    public int LongHead;
    public volatile int Head;              // number of samples written (ring index = (Head-1) % RING)
    readonly float[] _acc;                 // scratch per sample
    readonly int[] _cnt;
    public readonly float[] KillFlashCells = new float[256]; public int KillFlashN;

    // ---- voices (Revision M), for the console's translator: per clade x sound, how often it was made, what was going on
    // around the caller when it was made (CallCtx), and what listeners of the caller's clade did next (CallResp). BaseCtx
    // is the same context over every member, so a sound's meaning is what is more common when it is made than usual.
    public const int NCTX = 7, NRESP = 5;
    public static readonly string[] CtxNames = { "danger", "food here", "mate near", "hungry", "alone", "thirsty", "young lost" };
    public static readonly string[] RespNames = { "came", "fled", "froze", "called back", "ignored" };
    public readonly long[] CallN, CallCtx, CallResp, BaseN, BaseCtx;
    // open-ended meaning (Meaning.cs): per clade x sound, the sum and sum of squares of each of the caller's senses
    // (CallFeat), and of each primitive's gain in listeners of the caller's clade on the step they hear it (HeardGain,
    // over HeardN hearings); the same over the clade's usual sample (BaseFeat, BaseGain, over BaseN)
    public readonly double[] CallFeat, CallFeat2, BaseFeat, BaseFeat2, HeardGain, HeardGain2, BaseGain, BaseGain2;
    public readonly long[] HeardN;
    public readonly long[] UserResp = new long[NRESP];   // how listeners answered the person's latest call
    public int UserRespId;
    // utterances (Revision N), per clade x utterance type (Voice.NTYPE ordered sequences of 1..3 sounds): how often made,
    // when first and last made and when it was first made MIN_UTT times (step + 1; 0 = never), how listeners of the
    // caller's clade answered (UttResp, UttHeardN). The sums of the caller's senses and of the listeners' gains per type
    // are kept for NTRACK clades only (the first clades to call; a slot is reused when its clade dies out), to keep memory small
    public const int NTRACK = 4, MIN_UTT = 30;
    public readonly long[] UttN, UttFirst, UttLast, UttCross, UttResp, UttHeardN;
    public readonly int[] TrackOf = new int[K.CLADES], TrackClade = new int[NTRACK];   // clade -> tracker (-1 none), tracker -> clade (-1 free)
    public readonly double[] UttFeat, UttFeat2, UttGain, UttGain2;   // NTRACK x NTYPE x F.N (senses), x P.N (gains)
    public readonly long[] UttFeatN, UttGainN;                       // NTRACK x NTYPE: how many made and heard since tracking began

    public Telemetry(World w)
    {
        _w = w; NS = w.NS;
        int ns = NS;
        CallN = new long[ns * F.NSOUND]; CallCtx = new long[ns * F.NSOUND * NCTX]; CallResp = new long[ns * F.NSOUND * NRESP];
        BaseN = new long[ns]; BaseCtx = new long[ns * NCTX];
        CallFeat = new double[ns * F.NSOUND * F.N]; CallFeat2 = new double[ns * F.NSOUND * F.N];
        BaseFeat = new double[ns * F.N]; BaseFeat2 = new double[ns * F.N];
        HeardGain = new double[ns * F.NSOUND * P.N]; HeardGain2 = new double[ns * F.NSOUND * P.N]; HeardN = new long[ns * F.NSOUND];
        BaseGain = new double[ns * P.N]; BaseGain2 = new double[ns * P.N];
        int nt = ns * Voice.NTYPE;
        UttN = new long[nt]; UttFirst = new long[nt]; UttLast = new long[nt]; UttCross = new long[nt]; UttHeardN = new long[nt]; UttResp = new long[nt * NRESP];
        Array.Fill(TrackOf, -1); Array.Fill(TrackClade, -1);
        UttFeat = new double[NTRACK * Voice.NTYPE * F.N]; UttFeat2 = new double[NTRACK * Voice.NTYPE * F.N];
        UttGain = new double[NTRACK * Voice.NTYPE * P.N]; UttGain2 = new double[NTRACK * Voice.NTYPE * P.N];
        UttFeatN = new long[NTRACK * Voice.NTYPE]; UttGainN = new long[NTRACK * Voice.NTYPE];
        Created = new long[ns]; Births = new long[ns]; Conceptions = new long[ns]; Deaths = new long[ns * Cause.N];
        KillMatrix = new long[ns * ns]; Strikes = new long[ns]; Kills = new long[ns]; Matings = new long[ns]; Lays = new long[ns]; Immigrants = new long[ns]; Infections = new long[ns]; CareGiven = new double[ns]; _pInf = new long[ns]; _pCare = new double[ns]; DeathsByAge = new long[ns * 4]; AgeAtDeath = new double[ns]; _pDAge = new long[ns * 4]; _pAge = new double[ns];
        Intake = new double[ns * Pools.N]; SpendBasal = new double[ns]; SpendMove = new double[ns]; SpendGrowth = new double[ns]; SpendRepro = new double[ns]; SpendHeat = new double[ns]; RewardSum = new double[ns];
        _pBirths = new long[ns]; _pDeaths = new long[ns * Cause.N]; _pKill = new long[ns * ns]; _pStrikes = new long[ns]; _pKills = new long[ns];
        _pMatings = new long[ns]; _pImm = new long[ns]; _pConc = new long[ns];
        _pIntake = new double[ns * Pools.N]; _pBasal = new double[ns]; _pMove = new double[ns]; _pHeat = new double[ns]; _pReward = new double[ns];

        var names = new List<string>();
        names.Add("step");
        for (int p = 0; p < Pools.NFIELD; p++) names.Add("pool." + Pools.Names[p]);
        names.AddRange(new[] { "cover.shrub", "cover.tree", "cover.sapling", "nutrient.land", "nutrient.water", "fire.ignited", "fire.burning", "agents.alive", "season", "rain.anomaly",
            "temp.land", "temp.min", "temp.max", "frozen.land" });
        OffGlobal = 0;
        OffSpecies = names.Count;
        PerSpecies = S_END;
        foreach (var sp in w.Sp)
        {
            string n = "clade" + sp.Id.ToString("00") + ".";
            names.AddRange(new[] { n + "count", n + "juveniles", n + "eggs", n + "generation", n + "energy", n + "reward", n + "learned", n + "births", n + "conceptions" });
            foreach (var c in Cause.Names) names.Add(n + "death." + c);
            foreach (var p in Pools.Names) names.Add(n + "intake." + p);
            names.AddRange(new[] { n + "spend.basal", n + "spend.move", n + "spend.heat", n + "strikes", n + "kills", n + "matings", n + "immigrants",
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

    /// <summary>The tracker of clade s's utterance senses, assigned when it first calls (-1 if none is free). A tracker whose
    /// clade has died out is cleared and reused. No allocation.</summary>
    public int Track(int s)
    {
        int t = TrackOf[s];
        if (t >= 0) return t;
        for (int k = 0; k < NTRACK && t < 0; k++) if (TrackClade[k] < 0) t = k;
        for (int k = 0; k < NTRACK && t < 0; k++) if (!_w.Sp[TrackClade[k]].Alive) t = k;
        if (t < 0) return -1;
        if (TrackClade[t] >= 0) TrackOf[TrackClade[t]] = -1;
        TrackClade[t] = s; TrackOf[s] = t;
        int f = t * Voice.NTYPE * F.N, g = t * Voice.NTYPE * P.N;
        Array.Clear(UttFeat, f, Voice.NTYPE * F.N); Array.Clear(UttFeat2, f, Voice.NTYPE * F.N);
        Array.Clear(UttGain, g, Voice.NTYPE * P.N); Array.Clear(UttGain2, g, Voice.NTYPE * P.N);
        Array.Clear(UttFeatN, t * Voice.NTYPE, Voice.NTYPE); Array.Clear(UttGainN, t * Voice.NTYPE, Voice.NTYPE);
        return t;
    }

    /// <summary>Forget the voice statistics of clade slot s (a new clade takes the slot).</summary>
    public void ResetVoices(int s)
    {
        int n = F.NSOUND, t = Voice.NTYPE;
        Array.Clear(CallN, s * n, n); Array.Clear(CallCtx, s * n * NCTX, n * NCTX); Array.Clear(CallResp, s * n * NRESP, n * NRESP);
        Array.Clear(CallFeat, s * n * F.N, n * F.N); Array.Clear(CallFeat2, s * n * F.N, n * F.N);
        Array.Clear(HeardGain, s * n * P.N, n * P.N); Array.Clear(HeardGain2, s * n * P.N, n * P.N); Array.Clear(HeardN, s * n, n);
        Array.Clear(UttN, s * t, t); Array.Clear(UttFirst, s * t, t); Array.Clear(UttLast, s * t, t); Array.Clear(UttCross, s * t, t);
        Array.Clear(UttHeardN, s * t, t); Array.Clear(UttResp, s * t * NRESP, t * NRESP);
        BaseN[s] = 0; Array.Clear(BaseCtx, s * NCTX, NCTX); Array.Clear(BaseFeat, s * F.N, F.N); Array.Clear(BaseFeat2, s * F.N, F.N);
        Array.Clear(BaseGain, s * P.N, P.N); Array.Clear(BaseGain2, s * P.N, P.N);
        if (TrackOf[s] >= 0) { TrackClade[TrackOf[s]] = -1; TrackOf[s] = -1; }
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
    public readonly uint[] FightWho = new uint[FIGHT_N * 2];   // attacker and victim serials, for the console to animate them
    public long FightSeq;

    public void Fight(float x, float y, int attacker, int victim, int kind, ulong step, uint sa, uint sv)
    {
        int o = (int)(FightSeq % FIGHT_N) * FIGHT_F, q = (int)(FightSeq % FIGHT_N) * 2;
        FightWho[q] = sa; FightWho[q + 1] = sv;
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
        double nl = 0, nw = 0, tl = 0; int land = 0, water = 0, burning = 0, frozen = 0;
        float tmn = float.MaxValue, tmx = float.MinValue;
        for (int i = 0; i < FW2; i++)
        {
            for (int p = 0; p < Pools.NFIELD; p++) a[G_POOLS + p] += w.Pool[p][i];
            if (w.Cell[i] == (byte)Realm.Land)
            {
                land++; nl += w.Nutrient[i];
                float t = w.Temp[i]; tl += t; if (t < tmn) tmn = t; if (t > tmx) tmx = t; if (t < 0f) frozen++;
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
        a[G_TEMP] = (float)(tl / Math.Max(1, land)); a[G_TMIN] = land > 0 ? tmn : 0f; a[G_TMAX] = land > 0 ? tmx : 0f;
        a[G_FROZEN] = frozen / (float)Math.Max(1, land);

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
            a[o + S_TRAITS + T_VOICE] += w.PVoice[i];
            a[o + S_TRAITS + T_IMIT] += w.PImit[i];
            a[o + S_TRAITS + T_INSUL] += w.PInsul[i];
            a[o + S_TRAITS + T_APPEND] += w.PAppend[i];
            a[o + S_TRAITS + T_TOP] += w.TOp[i];
            a[o + S_TRAITS + T_FLOOR] += w.PFloor[i];
            a[o + S_TRAITS + T_ENDO] += w.PEndo[i];
            a[o + S_TRAITS + T_BREEDE] += w.PReadyE[i];
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
            a[o + S_SPEND + 2] = (float)(SpendHeat[s] - _pHeat[s]); _pHeat[s] = SpendHeat[s];
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
