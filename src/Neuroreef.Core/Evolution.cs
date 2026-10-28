// Evolution settings (DESIGN §7.6): the mutation knobs and the length of a prehistory run. Defaults
// come from the constants table; the console may change them while a prehistory runs. They never add an
// animal: every run is births and deaths only after its founders. A live world uses the defaults.

namespace Neuroreef;

public sealed class EvoSettings
{
    public float MutRate = K.MUT_RATE;            // mutation probability per genome byte
    public float MutSmall = K.MUT_SMALL;          // small mutation sigma (int8 units)
    public float MutBigShare = K.MUT_BIG_SHARE;   // share of mutations that are large jumps
    public float MutBig = K.MUT_BIG;              // large mutation sigma
    public int TotalSteps = K.PREHISTORY_STEPS;   // the prehistory ends here

    public EvoSettings Clone() => (EvoSettings)MemberwiseClone();

    public void CopyFrom(EvoSettings o)
    {
        MutRate = o.MutRate; MutSmall = o.MutSmall; MutBigShare = o.MutBigShare; MutBig = o.MutBig; TotalSteps = o.TotalSteps;
    }

    /// <summary>Keep every knob inside a sensible range.</summary>
    public void Clamp()
    {
        MutRate = System.Math.Clamp(MutRate, 0.00005f, 0.05f);
        MutSmall = System.Math.Clamp(MutSmall, 0.5f, 12f);
        MutBigShare = System.Math.Clamp(MutBigShare, 0f, 0.5f);
        MutBig = System.Math.Clamp(MutBig, 1f, 64f);
        TotalSteps = System.Math.Clamp(TotalSteps, 1000, 5000000);
    }
}
