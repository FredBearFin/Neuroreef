// Bodies from genes (DESIGN §6.3, Revision F). Every property of an organism is expressed from its own
// genome at creation, on fixed absolute ranges; there is no designed species body plan. Each trait has a
// physical cost, so evolution trades them off:
//   mass       everything scales by allometry (Kleiber): a bigger body costs more and matures later
//   legs/fins/wings  locomotion organs: they decide where an organism can live and how fast it moves,
//              and each costs basal energy; wings cost more the heavier the body, so flight favours the small
//   speed, sight     multipliers that raise basal cost; a narrow field of view sees further
//   armour     defence against strikes; heavy, slows the organism and costs basal energy
//   strike     weaponry: needed to kill or to drain blood; costs basal energy
//   diet       shares of a fixed digestive budget over the 15 pools (generalist against specialist); a pool
//              its realm cannot offer is never digested (a swimmer cannot graze)
//   litter, newborn share, live birth, sex, care: the life history

using System;

namespace Neuroreef;

public static class Body
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    static float Mul(sbyte b, float range) => MathK.Exp(b * (1f / 127f) * MathK.Log(range));

    /// <summary>The multiplier a speed or sight gene gives (1 = the middle of its range).</summary>
    public static float Factor(sbyte b, float range) => Mul(b, range);

    /// <summary>An organ size gene, 0..1.</summary>
    public static float Organ(sbyte b) => (b + 127) / 254f;

    public static void Express(World w, int i)
    {
        int g = i * G.LEN;
        var gn = w.Genome;
        float m = G.GeneLog(gn[g + G.B_MASS], K.MASS_LO, K.MASS_HI);
        float fs = Mul(gn[g + G.B_SPEED], K.BODY_RANGE_TRAIT);
        float fsi = Mul(gn[g + G.B_SIGHT], K.BODY_RANGE_TRAIT);
        float armour = Organ(gn[g + G.B_ARMOUR]) * K.ARMOUR_MAX;
        float strike = Organ(gn[g + G.B_STRIKE]) * K.STRIKE_MAX;
        float legs = Organ(gn[g + G.B_LEGS]), fins = Organ(gn[g + G.B_FINS]), wings = Organ(gn[g + G.B_WINGS]);
        float fov = G.Gene(gn[g + G.B_FOV], K.FOV_LO, K.FOV_HI);

        // locomotion and realm follow from the organs
        bool canWalk = legs >= K.LIMB_MIN, canSwim = fins >= K.LIMB_MIN, canFly = wings >= K.WING_MIN;
        Loco loco = canFly ? Loco.Fly : canWalk && canSwim ? Loco.Wade : canWalk ? Loco.Walk : canSwim ? Loco.Swim : Loco.Sessile;
        Realm realm = Locomotion.RealmOf(loco, fins > legs);
        w.PLoco[i] = (byte)loco; w.PRealm[i] = (byte)realm;
        w.PLegs[i] = legs; w.PFins[i] = fins; w.PWings[i] = wings;
        // land organisms lose water and must drink; swimmers and settled organisms take it from their surroundings
        w.PKThirst[i] = loco switch { Loco.Swim or Loco.Sessile => 0f, Loco.Wade => 0.5f, _ => 1f };

        w.PM[i] = m;
        float mPow = MathK.Pow(m, K.EXP_BASAL);
        float basalF = 1f + K.COST_SPEED * (fs - 1f) + K.COST_SIGHT * (fsi - 1f) + K.COST_ARMOUR_ABS * armour + K.COST_STRIKE_ABS * strike
                     + K.COST_LIMB * (legs + fins) + K.COST_WING * wings * MathK.Cbrt(m);
        w.PBasal[i] = K.C_BASAL * mPow * Math.Clamp(basalF, 0.5f, 4f);
        w.PGut[i] = K.C_GUT * m;
        w.PDig[i] = K.C_DIGEST * mPow;
        w.PBite[i] = K.C_BITE * mPow;
        w.PLife[i] = K.C_LIFE * MathK.Pow(m, K.EXP_LIFE);
        w.PMature[i] = K.C_MATURE * MathK.Pow(m, K.EXP_LIFE);
        // a narrow field of view sees further (forward eyes), a wide one sees around but less far
        w.PSight[i] = MathF.Min(K.MAX_SIGHT, K.C_SIGHT * MathK.Pow(m, K.EXP_SIGHT) * fsi * (1.6f - fov / 360f));
        w.PFovCos[i] = MathK.Cos(fov * 0.5f * MathF.PI / 180f);
        w.PReach[i] = 1f + 0.4f * MathK.Cbrt(m);
        w.PTurn[i] = K.C_TURN * MathK.Pow(m, K.EXP_TURN);
        float v = K.C_SPEED * MathK.Pow(m, K.EXP_SPEED) * fs / (1f + K.ARMOUR_DRAG * armour);
        w.PVmax[i] = loco switch
        {
            Loco.Fly => v * K.FLY_SPEED * wings,
            Loco.Walk => v * (0.4f + 0.6f * legs),
            Loco.Swim => v * K.SWIM_SPEED * (0.4f + 0.6f * fins),
            Loco.Wade => v * 0.8f * (0.4f + 0.3f * (legs + fins)),
            _ => 0f,
        };
        w.PArmour[i] = armour;
        w.PStrike[i] = strike;
        w.PLitter[i] = (byte)Math.Clamp((int)MathF.Round(MathF.Exp(Organ(gn[g + G.B_LITTER]) * MathF.Log(K.LITTER_MAX))), 1, K.LITTER_MAX);
        w.PBirth[i] = G.GeneLog(gn[g + G.B_BIRTH], K.BIRTH_LO, K.BIRTH_HI);
        w.PSexual[i] = (byte)(gn[g + G.B_SEX] >= 0 ? 1 : 0);
        w.PLive[i] = (byte)(gn[g + G.B_LIVE] >= 0 ? 1 : 0);
        float t = MathK.Pow(m, K.EXP_LIFE);
        w.PGest[i] = K.C_GEST * t;
        w.PCool[i] = K.C_COOL * t;
        w.PCare[i] = Organ(gn[g + G.B_CARE]);
        ExpressDiet(w, i, realm, new Span<float>(w.PDiet, i * Pools.N, Pools.N));
        // a blood specialist with weapons drains its victims instead of killing them (a parasite habit)
        float blood = w.PDiet[i * Pools.N + (int)Pool.Blood], meat = w.PDiet[i * Pools.N + (int)Pool.Meat];
        w.PParasite[i] = (byte)(blood > 0.2f && blood > meat ? 1 : 0);
    }

    static void ExpressDiet(World w, int i, Realm realm, Span<float> dst)
    {
        int g = i * G.LEN;
        float sum = 0;
        for (int p = 0; p < Pools.N; p++)
        {
            float raw = Reachable(realm, p) ? MathK.Exp(w.Genome[g + G.B_DIET + p] * (1f / 127f) * MathK.Log(20f)) : 0f;
            dst[p] = raw; sum += raw;
        }
        float k = sum > 0 ? K.DIET_BUDGET / sum : 0;
        for (int p = 0; p < Pools.N; p++) dst[p] = MathF.Min(0.95f, dst[p] * k);
    }

    /// <summary>Can an organism living in this realm get at this pool?</summary>
    public static bool Reachable(Realm realm, int p) => p switch
    {
        (int)Pool.Algae => (realm & Realm.Shallow) != 0,
        (int)Pool.Phyto or (int)Pool.Detritus => (realm & Realm.Water) != 0,
        (int)Pool.Meat or (int)Pool.Blood => true,
        _ => (realm & Realm.Land) != 0,
    };
}
