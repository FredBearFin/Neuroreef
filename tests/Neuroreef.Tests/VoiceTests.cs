// Richer voices (DESIGN Revision N): utterances of up to 3 sounds, the focal caller and who it is, the caller's range,
// gaze following, vocal learning of form from elders, and the translator's reading of whole utterances.

using System;
using System.Linq;
using Xunit;

namespace Neuroreef.Tests;

public class VoiceTests
{
    static (World w, float x, float y) NewWorld(uint seed, int cap = 64)
    {
        var w = new World(cap, seed); Terrain.Build(w);
        w.Sp[0].Used = w.Sp[0].Alive = true;
        Assert.True(Life.RandomSite(w, Realm.Land, 5u, out float x, out float y));
        return (w, x, y);
    }

    /// <summary>An organism of clade 0 at (x, y) with a full voice, mass 1 and no armour or weapons; body genes copied
    /// from refSlot when given (so the two look alike).</summary>
    static int Org(World w, int refSlot, float x, float y, bool adult, uint key)
    {
        int c = Life.Found(w, 0, ReadOnlySpan<sbyte>.Empty, adult, key, x, y);
        Assert.True(c >= 0);
        int g = c * G.LEN;
        if (refSlot >= 0) Array.Copy(w.Genome, refSlot * G.LEN + G.BODY, w.Genome, g + G.BODY, G.NBODY);
        else
        {
            w.Genome[g + G.B_MASS] = G.ToGeneLog(1f, K.MASS_LO, K.MASS_HI);
            w.Genome[g + G.B_ARMOUR] = -127; w.Genome[g + G.B_STRIKE] = -127;
            w.Genome[g + G.B_LEGS] = 100; w.Genome[g + G.B_FINS] = -127; w.Genome[g + G.B_WINGS] = -127;
            w.Genome[g + G.B_VOICE] = 127;
            w.Genome[g + G.B_IMIT] = -127; w.Genome[g + G.B_IMIT_AGE] = -127;
            w.Genome[g + G.B_BRAIN] = Body.UnitsGene(8);
        }
        Body.Express(w, c); Brain.InitLife(w, c);
        w.X[c] = World.Wrap(x); w.Y[c] = World.Wrap(y); w.Heading[c] = 0f; w.Flags[c] = 0;
        if (!adult) { w.StageOf[c] = St.Juvenile; w.Grown[c] = 0f; }
        return c;
    }

    static void Say(World w, int i, params int[] sounds)
    {
        Array.Clear(w.Call, i * F.MAX_SYL, F.MAX_SYL);
        for (int p = 0; p < sounds.Length; p++) w.Call[i * F.MAX_SYL + p] = (byte)sounds[p];
    }

    static float Feat(World w, int i, int f) => w.Feat[i * F.N + f];

    [Fact] // the 400 utterance types are the ordered sequences of 1-3 distinct sounds, numbered both ways
    public void UtteranceTypesRoundTrip()
    {
        Span<byte> s = stackalloc byte[F.MAX_SYL];
        var seen = new bool[Voice.NTYPE];
        for (int t = 0; t < Voice.NTYPE; t++)
        {
            int n = Voice.Decode(t, s);
            Assert.InRange(n, 1, 3);
            for (int p = 0; p < n; p++) { Assert.InRange(s[p], 1, 8); for (int q = 0; q < p; q++) Assert.NotEqual(s[q], s[p]); }
            Assert.Equal(t, Voice.TypeOf(s[0], s[1], s[2]));
            seen[t] = true;
        }
        Assert.All(seen, Assert.True);
        Assert.Equal(-1, Voice.TypeOf(3, 3, 0));   // no repeats
        Assert.Equal(-1, Voice.TypeOf(0, 0, 0));
    }

    [Fact] // an utterance is the fired sounds by intended gain, highest first, at most 3, each once; it costs more the longer it is
    public void UtteranceIsOrderedByGainAndCostsByLength()
    {
        var (w, x, y) = NewWorld(3);
        int a = Org(w, -1, x, y, true, 1);
        float[] gains = { 0.1f, 0.9f, 0.3f, 0.5f, 0.7f, 0.2f, 0.05f, 0.6f };
        for (int k = 0; k < F.NSOUND; k++) w.Gain[a * P.N + P.CALL_0 + k] = gains[k];
        uint acts = 0; foreach (int k in new[] { 0, 1, 3, 4, 7 }) acts |= 1u << (P.CALL_0 + k - P.FIRST_ACTION);
        Span<byte> syl = stackalloc byte[F.MAX_SYL];
        Assert.Equal(3, Voice.Order(w, a, acts, syl));
        Assert.Equal(new byte[] { 2, 5, 8 }, syl.ToArray());

        // in DECIDE: a brain that wants every sound makes only 3 of them, in the order of its gains
        for (int k = 0; k < F.NSOUND; k++)
        {
            int row = a * G.W2N + (P.CALL_0 + k) * G.NIN;
            for (int c = 0; c < G.NIN; c++) w.W2f[row + c] = 0f;
            w.W2f[row + G.NH + F.BIAS] = K.WMAX - 0.1f * k;   // all near 1, sound 1 the most intended
        }
        w.Step = 2;
        Simulation.BuildFor(w);
        Decide.Run(w, a, new Decide.ProbeBuf());
        uint made = w.IActs[a] & Voice.Bits;
        Assert.Equal(3, System.Numerics.BitOperations.PopCount(made));
        int t = Resolve.Speak(w, a, w.IActs[a], w.MassOf(a));
        Assert.True(t >= 0);
        for (int p = 1; p < F.MAX_SYL; p++)
            Assert.True(w.Gain[a * P.N + P.CALL_0 + w.Call[a * 3 + p - 1] - 1] >= w.Gain[a * P.N + P.CALL_0 + w.Call[a * 3 + p] - 1]);

        // cost: C_CALL x basal x (1 - CALL_SYL + CALL_SYL x n)
        float Cost(int n)
        {
            uint bits = 0; for (int k = 0; k < n; k++) bits |= 1u << (P.CALL_0 + k - P.FIRST_ACTION);
            w.Energy[a] = 5f;
            Resolve.Speak(w, a, bits, w.MassOf(a));
            return 5f - w.Energy[a];
        }
        float c0 = Cost(0), c1 = Cost(1), c3 = Cost(3);
        Assert.Equal(0f, c0);
        Assert.Equal(K.C_CALL * K.C_BASAL * MathK.Pow(w.MassOf(a), K.EXP_BASAL), c1, 5);
        Assert.Equal(2f, c3 / c1, 3);
        Assert.Equal(3, Voice.Len(w.Call, a * F.MAX_SYL));
    }

    [Fact] // the focal caller is the loudest alike caller, kept while it stays at least FOCAL_KEEP as loud; else a stranger
    public void FocalCallerPrefersKinAndKeepsAttention()
    {
        var (w, x, y) = NewWorld(4);
        int L = Org(w, -1, x, y, true, 1);
        int A = Org(w, L, x + 3f, y, true, 2), B = Org(w, L, x - 5f, y, true, 3), C = Org(w, L, x, y + 1f, true, 4);
        w.Genome[C * G.LEN + G.B_ARMOUR] = 127; w.Genome[C * G.LEN + G.B_STRIKE] = 127; Body.Express(w, C);   // looks different
        Assert.True(Body.LooksAlike(w, L, A) && Body.LooksAlike(w, L, B) && !Body.LooksAlike(w, L, C));
        Say(w, A, 1); Say(w, B, 2); Say(w, C, 3);
        var probe = new Decide.ProbeBuf();
        void Run() { w.Step++; Simulation.BuildFor(w); Decide.Run(w, L, probe); }
        Run();
        Assert.Equal(A, w.IFocal[L]);                                     // the stranger is louder, but kin comes first
        Assert.Equal(1f, Feat(w, L, F.FOC_KIN));
        Assert.Equal(1f, Feat(w, L, F.FOC_POS_0 + 0));
        Assert.Equal(1f - 1f / w.CallR[C], Feat(w, L, F.HEAR_0 + 2), 4);  // everything is still heard
        w.X[B] = World.Wrap(x - 2f); Run();                                // B is now louder, A still >= 0.7 of it
        Assert.Equal(A, w.IFocal[L]);
        w.X[A] = World.Wrap(x + 9f); Run();                                // A falls below 0.7 of B
        Assert.Equal(B, w.IFocal[L]);
        Assert.Equal(1f, Feat(w, L, F.FOC_POS_0 + 1));
        Say(w, A); Say(w, B); Run();                                       // only the stranger calls
        Assert.Equal(C, w.IFocal[L]);
        Assert.Equal(0f, Feat(w, L, F.FOC_KIN));
        Say(w, C); Run();
        Assert.Equal(-1, w.IFocal[L]);
        for (int k = 0; k < F.NSOUND; k++) Assert.Equal(0f, Feat(w, L, F.FOC_POS_0 + k));
    }

    [Fact] // a caller is heard as far as its voice and body carry it (HEAR_R x voice x M^(1/3), at most MAX_SIGHT)
    public void CallRangeIsTheCallers()
    {
        var (w, x, y) = NewWorld(5);
        int L = Org(w, -1, x, y, true, 1);
        int A = Org(w, L, x + 12f, y, true, 2);
        Say(w, A, 4);
        w.Step = 1; Simulation.BuildFor(w); Decide.Run(w, L, new Decide.ProbeBuf());
        Assert.Equal(K.HEAR_R * w.PVoice[A] * MathK.Cbrt(w.MassOf(A)), w.CallR[A], 4);
        Assert.True(Feat(w, L, F.HEAR_0 + 3) > 0f);
        w.Genome[A * G.LEN + G.B_VOICE] = -10; Body.Express(w, A);         // a weaker voice: 12 units is out of its range
        Assert.True(w.PVoice[A] >= K.VOICE_MIN);
        w.Step = 2; Simulation.BuildFor(w); Decide.Run(w, L, new Decide.ProbeBuf());
        Assert.True(w.CallR[A] < 12f);
        Assert.Equal(0f, Feat(w, L, F.HEAR_0 + 3));
    }

    [Fact] // who the focal caller is: kin, mother, elder, bigger, facing me; what it said by position; how like my own last utterance
    public void FocalCallerSensesAndThePersonIsAStranger()
    {
        var (w, x, y) = NewWorld(6);
        int M = Org(w, -1, x + 2f, y, true, 1);
        int L = Org(w, M, x, y, false, 2);
        w.MotherSlot[L] = M; w.MotherSerial[L] = w.Serial[M];
        w.Age[L] = 10f; w.Age[M] = 500f;
        w.Heading[M] = MathF.PI;                                           // facing L
        Say(w, M, 2, 7, 4);
        w.LastUtt[L * 3] = 2; w.LastUtt[L * 3 + 1] = 4;                    // L last said [2, 4]
        w.Step = 1; Simulation.BuildFor(w); Decide.Run(w, L, new Decide.ProbeBuf());
        Assert.Equal(M, w.IFocal[L]);
        Assert.Equal(1f, Feat(w, L, F.FOC_POS_0 + 1)); Assert.Equal(0.67f, Feat(w, L, F.FOC_POS_0 + 6)); Assert.Equal(0.33f, Feat(w, L, F.FOC_POS_0 + 3));
        Assert.Equal(0f, Feat(w, L, F.FOC_POS_0 + 0));
        Assert.Equal(1f, Feat(w, L, F.MY_POS_0 + 1)); Assert.Equal(0.67f, Feat(w, L, F.MY_POS_0 + 3));
        Assert.Equal(1f / 3f, Feat(w, L, F.FOC_LIKE_ME), 4);
        Assert.Equal(1f, Feat(w, L, F.FOC_KIN));
        Assert.Equal(1f, Feat(w, L, F.FOC_MOTHER));
        Assert.Equal(Math.Clamp(490f / w.PLife[L], -1f, 1f), Feat(w, L, F.FOC_ELDER), 4);
        Assert.True(Feat(w, L, F.FOC_SIZE) > 0.3f);                       // a juvenile listens to a bigger adult
        Assert.True(Feat(w, L, F.FOC_FACING) > 0.99f);
        // the person: heard like a caller, but a stranger of no age, size or gaze
        Say(w, M);
        w.UserCall[0] = 5; w.UserCall[1] = 1; w.UserCall[2] = 0;
        w.UserCallX = x + 1f; w.UserCallY = y; w.Step = 2; w.UserCallStep = w.Step - 1;
        Simulation.BuildFor(w); Decide.Run(w, L, new Decide.ProbeBuf());
        Assert.Equal(-2, w.IFocal[L]);
        Assert.Equal(1, w.IHeardUser[L]);
        Assert.Equal(1f, Feat(w, L, F.FOC_POS_0 + 4)); Assert.Equal(0.67f, Feat(w, L, F.FOC_POS_0 + 0));
        foreach (int f in new[] { F.FOC_KIN, F.FOC_MOTHER, F.FOC_ELDER, F.FOC_SIZE, F.FOC_FACING }) Assert.Equal(0f, Feat(w, L, f));
        Assert.True(Feat(w, L, F.HEAR_0 + 4) > 0f);
    }

    [Fact] // a learner's template collects syllable pairs only from an alike, grown, older focal caller during its sensitive period
    public void TemplateCollectsOnlyFromAlikeEldersInTheSensitivePeriod()
    {
        var (w, x, y) = NewWorld(7);
        int T = Org(w, -1, x + 2f, y, true, 1);
        int L = Org(w, T, x, y, false, 2);
        w.Genome[L * G.LEN + G.B_IMIT] = 127; w.Genome[L * G.LEN + G.B_IMIT_AGE] = 127; Body.Express(w, L);
        w.Age[L] = 10f; w.Age[T] = 500f;
        Say(w, T, 3, 5);
        int Total() { int s = 0; for (int k = 0; k < Voice.NTMPL; k++) s += w.Tmpl[L * Voice.NTMPL + k]; return s; }
        void Run() { w.Step += 2; Simulation.BuildFor(w); Decide.Run(w, L, new Decide.ProbeBuf()); }
        w.Step = 1; Run();
        Assert.Equal(1, w.Tmpl[L * Voice.NTMPL + Voice.Cell(0, 3)]);
        Assert.Equal(1, w.Tmpl[L * Voice.NTMPL + Voice.Cell(3, 5)]);
        Assert.Equal(2, Total());
        // not alike
        w.Genome[T * G.LEN + G.B_ARMOUR] = 127; w.Genome[T * G.LEN + G.B_STRIKE] = 127; Body.Express(w, T);
        Run(); Assert.Equal(2, Total());
        w.Genome[T * G.LEN + G.B_ARMOUR] = -127; w.Genome[T * G.LEN + G.B_STRIKE] = -127; Body.Express(w, T);
        // not grown
        w.StageOf[T] = St.Juvenile; Run(); Assert.Equal(2, Total()); w.StageOf[T] = St.Adult;
        // younger than the learner
        w.Age[T] = 5f; Run(); Assert.Equal(2, Total()); w.Age[T] = 500f;
        // past the sensitive period
        w.Genome[L * G.LEN + G.B_IMIT_AGE] = -127; Body.Express(w, L);
        w.Age[L] = 0.2f * w.PLife[L]; Run(); Assert.Equal(2, Total());
        w.Age[L] = 10f; w.Genome[L * G.LEN + G.B_IMIT_AGE] = 127; Body.Express(w, L);
        // no vocal learning
        w.Genome[L * G.LEN + G.B_IMIT] = -127; Body.Express(w, L);
        Assert.Equal(0f, w.PImit[L]);
        Run(); Assert.Equal(2, Total());
        // and with it again, it collects
        w.Genome[L * G.LEN + G.B_IMIT] = 127; Body.Express(w, L);
        Run(); Assert.Equal(4, Total());
    }

    [Fact] // FOLLOW_GAZE turns an animal the way its focal caller faces
    public void FollowGazeTurnsTheWayTheCallerFaces()
    {
        foreach (float theta in new[] { 1.0f, -1.0f })
        {
            var (w, x, y) = NewWorld(8);
            int Fc = Org(w, -1, x + 3f, y + 1f, true, 1);
            int L = Org(w, Fc, x, y, true, 2);
            w.Heading[Fc] = theta; w.Heading[L] = 0f;
            Say(w, Fc, 1);
            for (int p = 0; p < P.FIRST_ACTION; p++)
            {
                int row = L * G.W2N + p * G.NIN;
                for (int c = 0; c < G.NIN; c++) w.W2f[row + c] = 0f;
                w.W2f[row + G.NH + F.BIAS] = p == P.FOLLOW_GAZE ? K.WMAX : -K.WMAX;
                for (int c = 0; c < G.NC; c++) w.Genome[L * G.LEN + G.REFLEX + p * G.NC + c] = 0;
            }
            w.Genome[L * G.LEN + G.SIGMA] = -128;
            w.Step = (ulong)(6 - L % 4 + 8);                               // not a learning-block or relaxation step for L
            if (((int)w.Step + L) % 32 == 1) w.Step += 4;
            Simulation.BuildFor(w);
            Decide.Run(w, L, new Decide.ProbeBuf());
            Assert.Equal(Fc, w.IFocal[L]);
            float h = w.IHeading[L];
            Assert.True(MathF.Sign(h) == MathF.Sign(theta), $"theta {theta}: heading {h}");
            Assert.True(MathF.Abs(h - theta) < MathF.Abs(theta) - 0.01f);
        }
    }

    static ulong Hash(World w)
    {
        ulong h = 14695981039346656037UL;
        void Mix(float v) { h ^= (uint)BitConverter.SingleToInt32Bits(v); h *= 1099511628211UL; }
        for (int i = 0; i < w.Cap; i++)
        {
            h ^= w.StageOf[i]; h *= 1099511628211UL;
            if (w.StageOf[i] == St.Dead) continue;
            Mix(w.X[i]); Mix(w.Y[i]); Mix(w.Energy[i]); Mix(w.Heading[i]); h ^= w.Serial[i]; h *= 1099511628211UL;
            for (int k = 0; k < F.NSOUND; k++) for (int c = G.NH; c < G.NIN; c++) Mix(w.W2f[(long)i * G.W2N + (P.CALL_0 + k) * G.NIN + c]);
            for (int p = 0; p < F.MAX_SYL; p++) { h ^= w.Call[i * F.MAX_SYL + p]; h *= 1099511628211UL; }
        }
        return h;
    }

    [Fact] // with the vocal-learning gene at 0, the run is bit-identical to a run without vocal learning at all
    public void NoVocalLearningGeneIsBitIdenticalToNoFeature()
    {
        ulong Run(bool feature, bool imit, out long calls)
        {
            using var sim = new Simulation(8192, 9, 1, evo: new EvoSettings { VocalLearning = feature, MutBody = 0f });   // no mutation can give a newborn the gene back
            var w = sim.W;
            for (int i = 0; i < w.Cap; i++)
                if (w.StageOf[i] != St.Dead && !imit) { w.Genome[i * G.LEN + G.B_IMIT] = -127; Body.Express(w, i); }
            calls = 0;
            for (int k = 0; k < 400; k++) { sim.Step(); for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] >= St.Juvenile && w.Call[i * 3] != 0) calls++; }
            return Hash(w);
        }
        ulong on = Run(true, false, out long c1), off = Run(false, false, out long c2), learn = Run(true, true, out _);
        Assert.True(c1 > 100, $"only {c1} calls");
        Assert.Equal(c1, c2);
        Assert.Equal(on, off);
        Assert.NotEqual(on, learn);   // the gene does something when it is there
    }


    [Fact] // a juvenile that hears an elder say [3, 5] for 2000 steps comes to say it; one without vocal learning does not
    public void JuvenileLearnsItsTutorsForm()
    {
        // a small, fixed world: the tutor says [3, 5] every step; the learner neither moves nor eats nor is rewarded, so
        // the only thing its call outputs can learn from is the match of its own utterances to its template
        int[] Run(bool imit)
        {
            var (w, x, y) = NewWorld(11);   // seeds 10-20: the learner says [3,5] more than the control in all; by 3x in 9 of 11 (in 13 a random control brain itself favours [3,5])
            int T = Org(w, -1, x + 2f, y, true, 1);
            int L = Org(w, T, x, y, false, 2);
            int g = L * G.LEN;
            w.Genome[g + G.B_IMIT] = (sbyte)(imit ? 127 : -127); w.Genome[g + G.B_IMIT_AGE] = 127;
            // a moderate learning rate and short traces (a high rate locks onto the first form that happens to match in part)
            w.Genome[g + G.ETA] = 0; w.Genome[g + G.LAMBDA] = -128; w.Genome[g + G.KAPPA] = -128; w.Genome[g + G.SIGMA] = -128;
            for (int k = 0; k < F.NSOUND; k++) w.Genome[g + G.W2 + (P.CALL_0 + k) * G.NIN + G.NH + F.BIAS] = -28;   // each sound ~15%
            Body.Express(w, L); Brain.InitLife(w, L);
            w.Age[L] = 10f; w.Age[T] = 500f;
            var probe = new Decide.ProbeBuf();
            const int N = 2000;
            var hist = new int[Voice.NTYPE + 1];   // utterances in the last 500 steps; [NTYPE]: the target in the first 500
            for (int t = 0; t < N; t++)
            {
                w.Step = (ulong)t + 1;
                Say(w, T, 3, 5);
                Simulation.BuildFor(w);
                Decide.Run(w, L, probe);
                int u = Resolve.Speak(w, L, w.IActs[L], w.MassOf(L));
                w.Energy[L] = w.EmaxOf(L);
                if (u >= 0 && t >= N - 500) hist[u]++;
                if (u == Voice.TypeOf(3, 5, 0) && t < 500) hist[Voice.NTYPE]++;
            }
            return hist;
        }
        int target = Voice.TypeOf(3, 5, 0);
        var learner = Run(true); var control = Run(false);
        int calls = learner.Take(Voice.NTYPE).Sum();
        // chance: how often the control says an average two-sound utterance
        float pairs = 0; for (int t = F.NSOUND; t < F.NSOUND + F.NSOUND * (F.NSOUND - 1); t++) pairs += control[t];
        float chance = pairs / (F.NSOUND * (F.NSOUND - 1));
        Assert.True(calls > 50, $"the learner made only {calls} utterances");
        Assert.True(learner[target] >= 3f * MathF.Max(1f, chance), $"learner said [3,5] {learner[target]}x in the last 500 steps; chance {chance:F1}");
        Assert.True(learner[target] >= 3 * Math.Max(1, control[target]), $"learner said [3,5] {learner[target]}x, the control {control[target]}x");
        Assert.True(control[target] < 3f * MathF.Max(1f, chance), $"the control said [3,5] {control[target]}x; chance {chance:F1}");
    }


    [Fact] // the translator reads a whole utterance's context: an utterance made with food at hand means "food here", and it is a new word
    public void TranslatorReadsAnUtterancesContext()
    {
        var w = new World(64, 1);
        w.Sp[0].Used = w.Sp[0].Alive = true;
        const int N = 8;
        int word = Voice.TypeOf(3, 7, 0), other = Voice.TypeOf(1, 0, 0);
        for (int i = 0; i < N; i++) { w.Spc[i] = 0; w.StageOf[i] = St.Adult; }
        var rng = new Random(7);
        for (int step = 0; step < 2000; step++)
        {
            w.Step = (ulong)step;
            for (int i = 0; i < N; i++)
            {
                for (int f = 1; f < F.N; f++) w.Feat[i * F.N + f] = 0.5f * rng.NextSingle();
                for (int p = 0; p < P.N; p++) w.Gain[i * P.N + p] = 0.3f * rng.NextSingle();
                double u = rng.NextDouble();
                int t = u < 0.15 ? word : u < 0.3 ? other : -1;
                if (t == word) w.Feat[i * F.N + F.FOOD_HERE] = 0.9f + 0.1f * rng.NextSingle();   // [3,7] only with food at hand
                bool hears = i == N - 1 && rng.NextDouble() < 0.3;
                w.IHeard[i] = hears ? 1 + 0 * Voice.NTYPE + word : 0;
                if (hears) w.Gain[i * P.N + P.EAT] = 0.9f;
                Resolve.Voices(w, i, 0, hears ? -1 : t, 0);
            }
        }
        var when = Meaning.RankUttSenses(w.T, 0, word);
        Assert.NotEmpty(when);
        Assert.Equal(F.FOOD_HERE, when[0].Index);
        Assert.True(when[0].High && when[0].D > 2f);
        Assert.DoesNotContain(Meaning.RankUttSenses(w.T, 0, other), m => m.Index == F.FOOD_HERE && m.High);
        var then = Meaning.RankUttAnswers(w.T, 0, word);
        Assert.NotEmpty(then);
        Assert.Equal(P.EAT, then[0].Index);
        // each sound of it is counted in the per-sound view
        Assert.True(w.T.CallN[2] > 0 && w.T.CallN[6] > 0 && w.T.CallN[2] == w.T.CallN[6]);
        using var doc = System.Text.Json.JsonDocument.Parse(Neuroreef.Server.Wire.Lexicon(w));
        var cl = doc.RootElement.GetProperty("clades")[0];
        var words = cl.GetProperty("words");
        var top = Enumerable.Range(0, words.GetArrayLength()).Select(k => words[k]).First(x => x.GetProperty("t").GetInt32() == word);
        Assert.Equal(new[] { 3, 7 }, top.GetProperty("syl").EnumerateArray().Select(v => v.GetInt32()).ToArray());
        Assert.Equal(F.FOOD_HERE, top.GetProperty("when")[0].GetProperty("f").GetInt32());
        Assert.True(top.GetProperty("isNew").GetBoolean());
        Assert.Equal(2, cl.GetProperty("types").GetInt32());
        Assert.Equal(2, cl.GetProperty("zipf").GetArrayLength());
    }
}
