// The translator's open-ended reading of the voices (Revision M). Not a rule: nothing here is read by the simulation.
// A sound's meaning is not chosen from designed categories. It is whichever of the caller's 42 senses differ most
// from the clade's usual senses when the sound is made, and whichever behaviours the caller's clade turns to on
// hearing it. Each sense and each behaviour has a plain-language phrase so the console can say what it found.

using System;
using System.Collections.Generic;

namespace Neuroreef;

public static class Meaning
{
    /// <summary>Calls (and hearings) of a sound before the translator reads anything into it.</summary>
    public const int MIN_N = 25;
    /// <summary>Smallest standardised difference (in the clade's own spread of that sense) that is reported.</summary>
    public const float MIN_D = 0.25f;
    /// <summary>The difference must also stand out from chance: |mean difference| / its standard error at least this.</summary>
    public const float MIN_T = 3f;
    /// <summary>Floor on the spread of a sense, so a sense that hardly ever varies cannot give an infinite difference.</summary>
    public const float SD_FLOOR = 0.03f;

    /// <summary>What a sense being higher than usual means, in plain words (null: not reported).</summary>
    public static readonly string?[] High = new string?[F.N];
    /// <summary>What a sense being lower than usual means.</summary>
    public static readonly string?[] Low = new string?[F.N];
    /// <summary>Words a person might type for each sense direction (besides the words of the phrase itself).</summary>
    public static readonly string[][] KeysHigh = new string[F.N][], KeysLow = new string[F.N][];
    /// <summary>Each primitive as a plain verb, for what listeners do on hearing a sound.</summary>
    public static readonly string[] Verbs = new string[P.N];

    static void S(int f, string? hi, string? lo, string keysHi = "", string keysLo = "")
    {
        High[f] = hi; Low[f] = lo;
        KeysHigh[f] = keysHi.Length > 0 ? keysHi.Split(',') : Array.Empty<string>();
        KeysLow[f] = keysLo.Length > 0 ? keysLo.Split(',') : Array.Empty<string>();
    }

    static Meaning()
    {
        S(F.BIAS, null, null);
        S(F.HUNGER, "hungry", "well fed", "hungry,hunger,starving,starve", "full,fed,satisfied");
        S(F.THIRST, "thirsty", "not thirsty", "thirsty,thirst,parched", "quenched");
        S(F.FATIGUE, "tired", "rested", "tired,exhausted,sleepy,weary", "rested,fresh,energetic");
        S(F.READY, "ready to breed", "not ready to breed", "breed,ready,mate,love,partner,court", "");
        S(F.AGE, "old", "young in years", "old,elder,aged", "young");
        S(F.JUVENILE, "the caller is a youngster", "the caller is grown up", "baby,child,kid,youngster,cub,chick", "adult,grown");
        S(F.FULL, "gut full", "gut empty", "stuffed,full", "empty");
        S(F.PAIN, "hurt", "unhurt", "hurt,pain,ouch,injured,wounded,help,sick", "fine,ok,okay");
        S(F.CARRYING, "carrying young inside", "not carrying young", "pregnant,carrying,expecting", "");
        S(F.KIN_DENSITY, "among its own kind", "alone", "together,group,herd,flock,family,gather,friends", "alone,lonely,where,lost,come");
        S(F.KIN_CLOSE, "one of its kind close by", "none of its kind close", "friend,buddy,companion,near", "far");
        S(F.MOTHER_FAR, "a youngster far from its mother", "a youngster by its mother", "mother,mom,mum,mama,lost,parent", "");
        S(F.MATE_CLOSE, "a mate nearby", "no mate nearby", "mate,partner,love,babe,darling", "");
        S(F.O1_CLOSE, "another animal very close", "no other animal close", "stranger,intruder,near,close", "clear,safe");
        S(F.O1_SIZE, "the nearest animal is bigger", "the nearest animal is smaller", "bigger,large", "smaller,little,tiny");
        S(F.O1_APPROACH, "something is coming closer", "something is moving away", "coming,approach,incoming,closer", "leaving,gone,away");
        S(F.O1_ARMED, "the nearest animal is armed", "the nearest animal is harmless", "armed,weapon,claws,horns,teeth", "harmless");
        S(F.O1_MEAT, "a meat-eater is near", "a plant-eater is near", "predator,hunter,carnivore,danger,enemy", "herbivore,grazer");
        S(F.O1_WEAK, "a weak animal is near", "a strong animal is near", "weak,prey,easy,kill,hunt,attack", "strong");
        S(F.O2_CLOSE, "the biggest animal in view is close", "the biggest animal in view is far", "giant,looming", "");
        S(F.O2_SIZE, "a big animal is in view", "only small animals in view", "big,huge,giant,monster", "small");
        S(F.O2_APPROACH, "a big animal is coming closer", "a big animal is moving away", "charge,charging,stampede", "");
        S(F.O2_ARMED, "a big armed animal is in view", "the biggest animal in view is harmless", "", "");
        S(F.O2_MEAT, "a big meat-eater is in view", "the biggest animal in view eats plants", "danger,predator,watch out,beware,run,flee,hide,scary", "");
        S(F.OTHERS, "other kinds of animal around", "no other kinds around", "others,strangers,crowd", "");
        S(F.FOOD_HERE, "food here", "no food here", "food,eat,grass,fruit,meat,dinner,lunch,meal", "barren");
        S(F.FOOD_GRAD, "food nearby in one direction", "no food nearby", "this way,over there,find food,forage", "");
        S(F.IN_WATER, "in water", "on land", "swim,swimming,wet,lake,sea", "dry,ground");
        S(F.WATER_CLOSE, "water nearby", "far from water", "water,drink,pond,river", "desert,drought");
        S(F.LAND_CLOSE, "land nearby", "far from land", "shore,land,beach", "");
        S(F.FEAR, "smell of danger or fire", "no smell of danger", "fire,smoke,smell,scent,danger,burn", "safe,calm");
        S(F.CROWD, "crowded", "uncrowded", "crowded,busy,packed", "quiet,space");
        S(F.SEASON, "the good season", "the lean season", "spring,summer,season,rain", "winter,lean,dry");
        for (int k = 0; k < F.NSOUND; k++)
            S(F.HEAR_0 + k, $"hearing sound {k + 1}", $"not hearing sound {k + 1}", $"answer {k + 1},reply {k + 1},echo {k + 1}", "");

        string[] v =
        {
            "go toward the nearest animal", "go toward the biggest animal", "shadow the biggest animal", "flee the biggest animal",
            "flee the nearest animal", "cut off the nearest animal", "circle the nearest animal",
            "gather together", "spread apart", "move the same way", "follow mother", "look for a mate", "head for food",
            "head for water", "head for land", "move away from the danger smell", "wander", "go toward the caller", "move away from the caller",
            "stand still", "look around", "sprint", "eat", "attack", "drink", "mate", "rest", "mark the ground", "lay eggs", "bury seed",
        };
        for (int p = 0; p < v.Length; p++) Verbs[p] = v[p];
        for (int k = 0; k < F.NSOUND; k++) Verbs[P.CALL_0 + k] = $"call sound {k + 1}";
    }

    /// <summary>The phrase for a sense in one direction.</summary>
    public static string? Phrase(int f, bool high) => high ? High[f] : Low[f];

    /// <summary>One reading: a sense (or primitive), whether it is higher than usual, and how much (standardised difference).</summary>
    public readonly record struct Item(int Index, bool High, float D);

    static float Std(double sum, double sum2, double n, out float mean)
    {
        double m = sum / n, v = Math.Max(0, sum2 / n - m * m);
        mean = (float)m;
        return (float)v;
    }

    /// <summary>
    /// Standardised difference of every sense when clade s makes sound k against the clade's usual senses
    /// (NaN where there are too few calls or the difference is not clear). d = (call mean - usual mean) / pooled spread.
    /// </summary>
    public static void SenseDiffs(Telemetry T, int s, int k, Span<float> d)
    {
        int sk = s * F.NSOUND + k;
        Diffs(T.CallN[sk], T.BaseN[s], T.CallFeat, T.CallFeat2, sk * F.N, T.BaseFeat, T.BaseFeat2, s * F.N, F.N, d);
        d[F.BIAS] = float.NaN;
    }

    /// <summary>Standardised difference of every primitive's gain in listeners of clade s on the step they hear sound k
    /// from one of their own clade, against the clade's usual gains.</summary>
    public static void AnswerDiffs(Telemetry T, int s, int k, Span<float> d)
    {
        int sk = s * F.NSOUND + k;
        Diffs(T.HeardN[sk], T.BaseN[s], T.HeardGain, T.HeardGain2, sk * P.N, T.BaseGain, T.BaseGain2, s * P.N, P.N, d);
    }

    static void Diffs(long n, long bn, double[] sum, double[] sum2, int o, double[] bsum, double[] bsum2, int bo, int len, Span<float> d)
    {
        for (int f = 0; f < len; f++)
        {
            if (n < MIN_N || bn < MIN_N) { d[f] = float.NaN; continue; }
            float vc = Std(sum[o + f], sum2[o + f], n, out float mc), vb = Std(bsum[bo + f], bsum2[bo + f], bn, out float mb);
            float sd = MathF.Max(SD_FLOOR, MathF.Sqrt(0.5f * (vc + vb)));
            float se = MathF.Sqrt(vc / n + vb / bn + SD_FLOOR * SD_FLOOR / n);
            float diff = mc - mb, dd = diff / sd;
            d[f] = MathF.Abs(dd) >= MIN_D && MathF.Abs(diff) / se >= MIN_T ? dd : float.NaN;
        }
    }

    /// <summary>The senses that stand out most when clade s makes sound k, strongest first.</summary>
    public static List<Item> RankSenses(Telemetry T, int s, int k, int max = 3)
    {
        Span<float> d = stackalloc float[F.N];
        SenseDiffs(T, s, k, d);
        return Rank(d, max, false);
    }

    /// <summary>The behaviours that rise most in listeners of clade s on hearing sound k from their own clade, strongest first.</summary>
    public static List<Item> RankAnswers(Telemetry T, int s, int k, int max = 3)
    {
        Span<float> d = stackalloc float[P.N];
        AnswerDiffs(T, s, k, d);
        return Rank(d, max, true);
    }

    static List<Item> Rank(ReadOnlySpan<float> d, int max, bool risesOnly)
    {
        var list = new List<Item>();
        for (int f = 0; f < d.Length; f++)
        {
            float v = d[f];
            if (float.IsNaN(v) || (risesOnly && v <= 0)) continue;
            list.Add(new Item(f, v > 0, v));
        }
        list.Sort((a, b) => MathF.Abs(b.D).CompareTo(MathF.Abs(a.D)));
        if (list.Count > max) list.RemoveRange(max, list.Count - max);
        return list;
    }
}
