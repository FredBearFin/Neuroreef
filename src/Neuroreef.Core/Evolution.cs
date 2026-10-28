// Evolution settings (DESIGN §7.6). These are the knobs of the evolutionary search in a prehistory run,
// not laws of the world: they decide how genomes vary and are selected, never what happens to an
// animal. Defaults come from the constants table; the console may change them while a prehistory runs.
// A live world uses the defaults for the mutation of its newborns and has no colonists at all.

namespace Neuroreef;

public sealed class EvoSettings
{
    public float MutRate = K.MUT_RATE;            // mutation probability per genome byte
    public float MutSmall = K.MUT_SMALL;          // small mutation sigma (int8 units)
    public float MutBigShare = K.MUT_BIG_SHARE;   // share of mutations that are large jumps
    public float MutBig = K.MUT_BIG;              // large mutation sigma
    public float ArchiveShare = K.ARCHIVE_SHARE;  // share of colonists bred from the archive
    public int Tournament = K.TOURNAMENT;         // tournament size when picking colonist parents
    public float Floor = K.PREHISTORY_FLOOR;      // colonist floor as share of initial count
    public int ColonistSteps = K.PREHISTORY_COLONISTS;   // colonists arrive until this step
    public int TotalSteps = K.PREHISTORY_STEPS;          // the prehistory ends here
    public int MigrateEvery = K.MIGRATE_EVERY;           // steps between exchanges of the best genomes between branches
    public int Migrants = K.MIGRANTS;                    // genomes per species sent to the next branch

    public EvoSettings Clone() => (EvoSettings)MemberwiseClone();

    public void CopyFrom(EvoSettings o)
    {
        MutRate = o.MutRate; MutSmall = o.MutSmall; MutBigShare = o.MutBigShare; MutBig = o.MutBig;
        ArchiveShare = o.ArchiveShare; Tournament = o.Tournament; Floor = o.Floor;
        ColonistSteps = o.ColonistSteps; TotalSteps = o.TotalSteps; MigrateEvery = o.MigrateEvery; Migrants = o.Migrants;
    }

    /// <summary>Keep every knob inside a range where the search still works.</summary>
    public void Clamp()
    {
        MutRate = System.Math.Clamp(MutRate, 0.001f, 0.2f);
        MutSmall = System.Math.Clamp(MutSmall, 0.5f, 12f);
        MutBigShare = System.Math.Clamp(MutBigShare, 0f, 0.5f);
        MutBig = System.Math.Clamp(MutBig, 1f, 64f);
        ArchiveShare = System.Math.Clamp(ArchiveShare, 0f, 1f);
        Tournament = System.Math.Clamp(Tournament, 1, 16);
        Floor = System.Math.Clamp(Floor, 0f, 1f);
        TotalSteps = System.Math.Clamp(TotalSteps, 1000, 5000000);
        ColonistSteps = System.Math.Clamp(ColonistSteps, 0, TotalSteps);
        MigrateEvery = System.Math.Clamp(MigrateEvery, 1000, 1000000);
        Migrants = System.Math.Clamp(Migrants, 0, K.ARCHIVE);
    }
}
