// Evolution settings (DESIGN §7.6): the mutation knobs and the length of a prehistory run. Defaults
// come from the constants table; the console may change them while a prehistory runs. They never add an
// animal: every run is births and deaths only after its founders. A live world uses them x the setup's
// world evolution speed (Revision L: time compression for a small valley, not ecology).

namespace Neuroreef;

public sealed class EvoSettings
{
    public float MutRate = K.MUT_RATE;            // mutation probability per brain or reflex byte
    public float MutBody = K.MUT_BODY;            // mutation probability per body gene
    public float MutSmall = K.MUT_SMALL;          // small mutation sigma (int8 units)
    public float MutBigShare = K.MUT_BIG_SHARE;   // share of mutations that are large jumps
    public float MutBig = K.MUT_BIG;              // large mutation sigma
    public int TotalSteps = K.PREHISTORY_STEPS;   // the prehistory ends here
    public int BigBrains = K.BIG_BRAINS;          // big-brained ancestors per branch (founding only)
    public int BigBrainUnits = K.BIG_BRAIN_UNITS; // their memory units
    public int Founders = K.ANCESTOR_COUNT;       // founders per ancestor (founding only)
    public int AncestorsLand = K.ANCESTORS_LAND, AncestorsWater = K.ANCESTORS_WATER;   // random ancestors (founding only)
    public float MatingDrive = 1f;                // x the breeding instinct (mating and laying alike)

    public EvoSettings Clone() => (EvoSettings)MemberwiseClone();

    public void CopyFrom(EvoSettings o)
    {
        MutRate = o.MutRate; MutBody = o.MutBody; BigBrains = o.BigBrains; BigBrainUnits = o.BigBrainUnits; Founders = o.Founders; AncestorsLand = o.AncestorsLand; AncestorsWater = o.AncestorsWater; MatingDrive = o.MatingDrive; MutSmall = o.MutSmall; MutBigShare = o.MutBigShare; MutBig = o.MutBig; TotalSteps = o.TotalSteps;
    }

    /// <summary>Keep every knob inside a sensible range.</summary>
    public void Clamp()
    {
        MutRate = System.Math.Clamp(MutRate, 0.00005f, 0.05f);
        MutBody = System.Math.Clamp(MutBody, 0f, 0.2f);
        AncestorsLand = System.Math.Clamp(AncestorsLand, 0, 16); AncestorsWater = System.Math.Clamp(AncestorsWater, 0, 16 - AncestorsLand);
        if (AncestorsLand + AncestorsWater == 0) AncestorsLand = 1;
        MatingDrive = System.Math.Clamp(MatingDrive, 0f, 3f);
        BigBrains = System.Math.Clamp(BigBrains, 0, AncestorsLand + AncestorsWater);
        BigBrainUnits = System.Math.Clamp(BigBrainUnits, K.BRAIN_MIN, G.NH);
        Founders = System.Math.Clamp(Founders, 20, 5000);
        MutSmall = System.Math.Clamp(MutSmall, 0.5f, 12f);
        MutBigShare = System.Math.Clamp(MutBigShare, 0f, 0.5f);
        MutBig = System.Math.Clamp(MutBig, 1f, 64f);
        TotalSteps = System.Math.Clamp(TotalSteps, 1000, int.MaxValue);   // int.MaxValue = until stopped
    }
}
