// Evolvable bodies (DESIGN §7.5). The genome carries body genes after the brain: log-scale multipliers
// on the species' founder body plan. Each slot's phenotype is expressed once, at creation, into
// per-slot arrays. Every trait has a cost, so evolution trades them off:
//   mass     everything scales by allometry (Kleiber): a bigger body costs more and matures later
//   speed    faster muscles raise basal cost
//   sight    better senses raise basal cost
//   armour   heavier armour raises basal cost and slows the animal
//   strike   stronger weapons raise basal cost
//   litter   more offspring per litter, but the same brood energy is shared among them
//   diet     each stage has a fixed digestive budget (sum of efficiencies of the founder); a gene raises
//            one pool's efficiency only at the expense of the others. Pools the founder cannot digest
//            can be gained by mutation, so a lineage can drift into a neighbouring niche.

using System;

namespace Neuroreef;

public static class Body
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    static float Mul(sbyte b, float range) => MathK.Exp(b * (1f / 127f) * MathK.Log(range));

    /// <summary>The multiplier a body gene gives (1 = the founder body plan).</summary>
    public static float Factor(sbyte b, float range) => Mul(b, range);

    public static void Express(World w, int i)
    {
        var sp = w.Sp[w.Spc[i]];
        int g = i * G.LEN;
        var gn = w.Genome;
        float fm = Mul(gn[g + G.B_MASS], K.BODY_RANGE_MASS);
        float fs = Mul(gn[g + G.B_SPEED], K.BODY_RANGE_TRAIT);
        float fsi = Mul(gn[g + G.B_SIGHT], K.BODY_RANGE_TRAIT);
        float fa = Mul(gn[g + G.B_ARMOUR], K.BODY_RANGE_TRAIT);
        float fst = Mul(gn[g + G.B_STRIKE], K.BODY_RANGE_TRAIT);
        float fl = Mul(gn[g + G.B_LITTER], K.BODY_RANGE_TRAIT);

        float m = sp.Mass * fm;
        w.PM[i] = m;
        float mPow = MathK.Pow(m, K.EXP_BASAL);
        float basalF = 1f + K.COST_SPEED * (fs - 1f) + K.COST_SIGHT * (fsi - 1f) + K.COST_ARMOUR * (fa - 1f) + K.COST_STRIKE * (fst - 1f);
        w.PBasal[i] = K.C_BASAL * mPow * Math.Clamp(basalF, 0.5f, 3f);
        w.PGut[i] = K.C_GUT * m;
        w.PDig[i] = K.C_DIGEST * mPow;
        w.PBite[i] = K.C_BITE * mPow;
        w.PLife[i] = K.C_LIFE * MathK.Pow(m, K.EXP_LIFE) * sp.KLife;
        w.PMature[i] = K.C_MATURE * MathK.Pow(m, K.EXP_LIFE) * sp.KMat;
        float sm = sp.Swarm ? MathF.Max(sp.UnitMass * 40f, 0.3f) : m;
        w.PSight[i] = MathF.Min(K.MAX_SIGHT, K.C_SIGHT * MathK.Pow(sm, K.EXP_SIGHT) * sp.KSight * fsi);
        w.PReach[i] = 1f + 0.4f * MathK.Pow(sp.Swarm ? 1f : m, 1f / 3f);
        w.PTurn[i] = K.C_TURN * MathK.Pow(m, K.EXP_TURN);
        float vwalk = K.C_SPEED * MathK.Pow(m, K.EXP_SPEED) * sp.KSpeed * fs / (1f + K.ARMOUR_DRAG * (fa - 1f));
        w.PVmaxA[i] = LocoSpeed(vwalk, sp.Adult.Loco);
        w.PVmaxJ[i] = LocoSpeed(vwalk, sp.Juv.Loco);
        w.PArmour[i] = sp.Armour * fa;
        w.PStrike[i] = sp.Strike * fst;
        w.PLitter[i] = (byte)Math.Clamp((int)MathF.Round(sp.Litter * fl), 1, 24);
        w.PUnit[i] = sp.Swarm ? sp.UnitMass * fm : 0f;
        // care: a lineage can evolve more or less of it; it costs the energy given (DESIGN §7.8)
        w.PCare[i] = sp.Care * Mul(gn[g + G.B_CARE], K.BODY_RANGE_TRAIT);
        ExpressDiet(w, i, sp.Adult, new Span<float>(w.PDietA, i * Pools.N, Pools.N));
        ExpressDiet(w, i, sp.Juv, new Span<float>(w.PDietJ, i * Pools.N, Pools.N));
    }

    static float LocoSpeed(float v, Loco l) => l switch
    {
        Loco.Sessile => 0f,
        Loco.Swim => v * K.SWIM_SPEED,
        Loco.Fly => v * K.FLY_SPEED,
        _ => v,
    };

    static void ExpressDiet(World w, int i, Stage st, Span<float> dst)
    {
        int g = i * G.LEN;
        float budget = 0, sum = 0;
        for (int p = 0; p < Pools.N; p++) budget += st.Diet[p];
        for (int p = 0; p < Pools.N; p++)
        {
            sbyte b = w.Genome[g + G.B_DIET + p];
            float raw = st.Diet[p] * Mul(b, K.BODY_RANGE_DIET) + K.DIET_NOVEL * MathF.Max(0, b) / 127f;
            // a pool the stage's realm cannot offer is never gained (a fish does not evolve to eat grass)
            if (st.Diet[p] <= 0 && !Reachable(st, p)) raw = 0;
            dst[p] = raw; sum += raw;
        }
        float k = sum > 0 ? budget / sum : 0;
        for (int p = 0; p < Pools.N; p++) dst[p] = MathF.Min(0.95f, dst[p] * k);
    }

    public static bool Reachable(Stage st, int p) => p switch
    {
        (int)Pool.Algae => (st.Realm & Realm.Shallow) != 0,
        (int)Pool.Phyto or (int)Pool.Detritus => (st.Realm & Realm.Water) != 0,
        (int)Pool.Meat or (int)Pool.Blood => true,
        _ => (st.Realm & Realm.Land) != 0,
    };
}
