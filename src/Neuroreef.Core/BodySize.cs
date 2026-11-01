// The body-size range of a world (DESIGN §6.3; the setup screen's Bodies group, 2026-11-01). The owner: "we want more
// customizability on the size floors - why not be able to do a world of giants on purpose." Chosen once at startup with
// the map and nature, the same for every branch and the live world of a run, and saved in checkpoints and world saves
// (Nature.Write). The default is K.MASS_LO..K.MASS_HI (0.1-40 kg) with the size classes' floors 0.1, 1, 3 and 10 kg,
// exactly as before, bit for bit.
//
// What it sets, and nothing else:
//   the mass gene's log scale: a body's mass is Min x (Max / Min)^((g + 128) / 255), so a mutation still changes a body
//     by a share of its size, whatever the range;
//   the size classes' floors (Body.Floors): given, or by default in the same log proportions of the range as 0.1, 1, 3, 10
//     are of 0.1..40;
//   the smallest newborn: NEWBORN_MIN is a tenth of the smallest adult (MASS_LO) and stays so of Min;
//   what is derived from the largest body: the farthest eye (MaxSight, x (Max / MASS_HI)^EXP_SIGHT), the farthest call
//     (MaxHear, x (Max / MASS_HI)^(1/3)), and from them the spatial-hash cells (Map.HCell, Map.HCellCall).
// Every law of a body (allometry, flight, heat, breath, gut, reach) is its own physics of its mass; nothing here limits
// what a body of a given mass can do. A world of giants holds few of them: real densities fall with size (Damuth 1981),
// and the setup screen says so.

using System;

namespace Neuroreef;

public static class BodySize
{
    /// <summary>The smallest and largest body mass (kg) a world can be set to: a 1 g animal to a 50 t one.</summary>
    public const float LIMIT_LO = 0.001f, LIMIT_HI = 50000f;
    /// <summary>The largest range is at least this many times its smallest body (one size class needs room to evolve in).</summary>
    public const float MIN_RATIO = 4f;

    static readonly float[] DefaultFloors = { 0.1f, 1f, 3f, 10f };

    /// <summary>Smallest body mass (kg) the mass gene gives.</summary>
    public static float Min { get; private set; } = K.MASS_LO;
    /// <summary>Largest body mass (kg) the mass gene gives.</summary>
    public static float Max { get; private set; } = K.MASS_HI;
    /// <summary>The smallest body of each size class (kg; Body.Floors).</summary>
    public static readonly float[] Floors = (float[])DefaultFloors.Clone();
    /// <summary>The smallest newborn or egg (kg): NEWBORN_MIN at the default range, a tenth of Min.</summary>
    public static float NewbornMin { get; private set; } = K.NEWBORN_MIN;
    /// <summary>The farthest any eye sees (units): K.MAX_SIGHT at the default range.</summary>
    public static float MaxSight { get; private set; } = K.MAX_SIGHT;
    /// <summary>The farthest any call carries (units): K.MAX_HEAR at the default range.</summary>
    public static float MaxHear { get; private set; } = K.MAX_HEAR;
    /// <summary>The smallest bodies' hash cell side (units) that holds a step's perception of the largest walker in its 7x7 cells.</summary>
    public static int CellNeed { get; private set; } = K.HCELL;
    /// <summary>The smallest callers' hash cell side (units) that holds the loudest call in its 5x5 cells.</summary>
    public static int CallCellNeed { get; private set; } = K.HCELL_CALL;

    public static bool IsDefault => Min == K.MASS_LO && Max == K.MASS_HI && FloorsAreDefault(Floors);

    static bool FloorsAreDefault(float[] f)
    {
        for (int k = 0; k < K.FLOOR_CLASSES; k++) if (f[k] != DefaultFloors[k]) return false;
        return true;
    }

    /// <summary>The floors of a range in the default's log proportions (0.1, 1, 3, 10 of 0.1..40); the default's own exactly.</summary>
    public static float[] ProportionalFloors(float min, float max)
    {
        if (min == K.MASS_LO && max == K.MASS_HI) return (float[])DefaultFloors.Clone();
        var f = new float[K.FLOOR_CLASSES];
        float span = MathK.Log(K.MASS_HI / K.MASS_LO), r = max / min;
        for (int k = 0; k < K.FLOOR_CLASSES; k++)
            f[k] = k == 0 ? min : min * MathK.Pow(r, MathK.Log(DefaultFloors[k] / K.MASS_LO) / span);
        return f;
    }

    /// <summary>
    /// Set the range (kg) and the size classes' floors (null: in the default's proportions). Clamped to LIMIT_LO..LIMIT_HI
    /// with Max at least MIN_RATIO x Min; each floor inside the range and none below the one before. Only before any World
    /// is built: the map's hash cells follow (Map.Set is applied again to the present side).
    /// </summary>
    public static void Set(float min, float max, float[]? floors = null)
    {
        if (!(min > 0f)) min = K.MASS_LO;
        if (!(max > 0f)) max = K.MASS_HI;
        min = Math.Clamp(min, LIMIT_LO, LIMIT_HI / MIN_RATIO);
        max = Math.Clamp(max, min * MIN_RATIO, LIMIT_HI);
        var f = floors != null && floors.Length == K.FLOOR_CLASSES ? (float[])floors.Clone() : ProportionalFloors(min, max);
        for (int k = 0; k < K.FLOOR_CLASSES; k++)
        {
            float v = float.IsFinite(f[k]) ? f[k] : min;
            f[k] = Math.Clamp(v, k == 0 ? min : f[k - 1], max);
        }
        Min = min; Max = max;
        f.CopyTo(Floors, 0);
        if (min == K.MASS_LO && max == K.MASS_HI)
        {
            NewbornMin = K.NEWBORN_MIN; MaxSight = K.MAX_SIGHT; MaxHear = K.MAX_HEAR; CellNeed = K.HCELL; CallCellNeed = K.HCELL_CALL;
        }
        else
        {
            NewbornMin = K.NEWBORN_MIN * (min / K.MASS_LO);
            float big = max / K.MASS_HI;
            MaxSight = K.MAX_SIGHT * MathK.Pow(big, K.EXP_SIGHT);
            MaxHear = K.MAX_HEAR * MathK.Cbrt(big);
            CellNeed = Math.Max(K.HCELL, (int)MathF.Ceiling(WalkerPerceive(max) / 3f));
            CallCellNeed = Math.Max(K.HCELL_CALL, (int)MathF.Ceiling(MaxHear / 2f));
        }
        Map.Set(Map.W);   // the hash cells for this range (the side is unchanged)
    }

    public static void Reset() => Set(K.MASS_LO, K.MASS_HI);

    /// <summary>
    /// How far the largest walker of mass m perceives in a step at its top speed (Decide.Perceive without the cap): the
    /// farthest eye scanning along the way it went, sqrt(S^2 + 2 S v / pi), with v its top speed per step (speed gene at
    /// its top, the shape and limb factors at theirs, wing-assisted running). At the default range ~18 units, inside the
    /// 7x7 cells of K.HCELL. Fliers can go farther (to FLY_V_CAP) and are held at the hash neighbourhood, as before.
    /// </summary>
    public static float WalkerPerceive(float m)
    {
        float s = K.MAX_SIGHT * MathK.Pow(m / K.MASS_HI, K.EXP_SIGHT);
        float v = K.C_SPEED * MathK.Pow(m, K.EXP_SPEED) * K.BODY_RANGE_TRAIT * 1.3f * (1f + K.GLIDE);
        return MathF.Sqrt(s * s + 2f * s * v / MathF.PI);
    }

    // for checkpoints and world saves (Nature.Write and Read, from format 23): the range and the floors
    public static void Write(System.IO.BinaryWriter bw)
    {
        bw.Write(Min); bw.Write(Max);
        for (int k = 0; k < K.FLOOR_CLASSES; k++) bw.Write(Floors[k]);
    }

    public static void Read(System.IO.BinaryReader br)
    {
        float min = br.ReadSingle(), max = br.ReadSingle();
        var f = new float[K.FLOOR_CLASSES];
        for (int k = 0; k < K.FLOOR_CLASSES; k++) f[k] = br.ReadSingle();
        Set(min, max, f);
    }

    /// <summary>A mass in kg or t, for reports.</summary>
    public static string Kg(float m) => m >= 1000f ? $"{m / 1000f:0.##} t" : m >= 1f ? $"{m:0.##} kg" : $"{m * 1000f:0.##} g";

    public static string Describe() => $"bodies {Kg(Min)}-{Kg(Max)} (size classes from {Kg(Floors[0])}, {Kg(Floors[1])}, {Kg(Floors[2])}, {Kg(Floors[3])})";
}
