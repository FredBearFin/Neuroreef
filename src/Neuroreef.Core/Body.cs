// Bodies from genes (DESIGN §6.3, Revision F). Every property of an organism is expressed from its own
// genome at creation, on fixed absolute ranges; there is no designed species body plan and no designed
// threshold that decides what a body "is". Each trait has a physical cost, so evolution trades them off:
//   mass       everything scales by allometry (Kleiber): a bigger body costs more and matures later
//   legs, fins, wings  locomotion organs: speed on land, in water and in the air follows smoothly from their
//              sizes; a body flies when its wings can lift it (wing loading), and stays in deep water when it
//              swims well enough; each organ costs basal energy, wings more the heavier the body
//   leg pairs, fin pairs, eye pairs   how many: more legs carry armour with less slowing, more fins steer,
//              more eyes see around; each pair costs upkeep
//   speed, sight, field of view   multipliers that raise basal cost; a narrow field of view sees further
//   armour, strike   defence and weapons, each costly; armour slows
//   diet       shares of a fixed digestive budget over the 15 pools; a pool its realm cannot offer is never digested
//   life history   lifespan (longer life costs body repair), development time (fast developers age sooner),
//              brain size (memory units cost energy), sexual or asexual, separate sexes or hermaphrodite and
//              the sex ratio, eggs or live birth, litter, newborn size, care, immune investment

using System;

namespace Neuroreef;

public static class Body
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    static float Mul(sbyte b, float range) => MathK.Exp(b * (1f / 127f) * MathK.Log(range));

    /// <summary>The multiplier a speed, sight, lifespan or development gene gives (1 = the middle of its range).</summary>
    public static float Factor(sbyte b, float range) => Mul(b, range);

    /// <summary>Number of pairs (legs, fins, eyes) from a count gene: 1 to 4.</summary>
    public static int Pairs(sbyte b) => 1 + Math.Min(3, (int)(Organ(b) * 4f));
    public static int LegPairs(sbyte b) => Pairs(b);

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
        int legPairs = Pairs(gn[g + G.B_LEGN]), finPairs = Pairs(gn[g + G.B_FINN]), eyePairs = Pairs(gn[g + G.B_EYEN]);
        float fov = MathF.Min(360f, G.Gene(gn[g + G.B_FOV], K.FOV_LO, K.FOV_HI) * (1f + K.EYE_FOV * (eyePairs - 1)));
        float life = Mul(gn[g + G.B_LIFE], K.LIFE_RANGE), devel = Mul(gn[g + G.B_DEVEL], K.LIFE_RANGE);
        float imm = Organ(gn[g + G.B_IMMUNE]);
        int brain = K.BRAIN_MIN + (int)MathF.Round(Organ(gn[g + G.B_BRAIN]) * (G.NH - K.BRAIN_MIN));
        bool herm = gn[g + G.B_HERM] >= 0;
        w.PLegs[i] = legs; w.PFins[i] = fins; w.PWings[i] = wings;
        w.PLegPairs[i] = (byte)legPairs; w.PFinPairs[i] = (byte)finPairs; w.PEyePairs[i] = (byte)eyePairs;
        w.PBrain[i] = (byte)brain; w.PHerm[i] = (byte)(herm ? 1 : 0); w.PImmune[i] = imm;
        w.PSexRatio[i] = Organ(gn[g + G.B_SEXRATIO]);

        w.PM[i] = m;
        float mPow = MathK.Pow(m, K.EXP_BASAL);
        float basalF = 1f + K.COST_SPEED * (fs - 1f) + K.COST_SIGHT * (fsi - 1f) + K.COST_ARMOUR_ABS * armour + K.COST_STRIKE_ABS * strike
                     + K.COST_LIMB * (legs * legPairs * 0.5f + fins * finPairs * 0.5f) + K.COST_WING * wings * MathK.Cbrt(m)
                     + K.COST_EYE * eyePairs + K.COST_LONGEVITY * (life - 1f) + K.COST_BRAIN * brain / G.NH
                     + (herm ? K.COST_HERM : 0f) + K.COST_IMMUNE * imm;
        w.PBasal[i] = K.C_BASAL * mPow * Math.Clamp(basalF, 0.5f, 4f);
        w.PGut[i] = K.C_GUT * m;
        w.PDig[i] = K.C_DIGEST * mPow;
        w.PBite[i] = K.C_BITE * mPow;
        float t = MathK.Pow(m, K.EXP_LIFE);
        w.PMature[i] = K.C_MATURE * t * devel;
        w.PLife[i] = K.C_LIFE * t * life * MathK.Pow(devel, K.DEVEL_AGING);
        // a narrow field of view sees further (forward eyes), a wide one sees around but less far
        w.PSight[i] = MathF.Min(K.MAX_SIGHT, K.C_SIGHT * MathK.Pow(m, K.EXP_SIGHT) * fsi * (1.6f - fov / 360f));
        w.PFovCos[i] = MathK.Cos(fov * 0.5f * MathF.PI / 180f);
        w.PReach[i] = 1f + 0.4f * MathK.Cbrt(m);
        w.PTurn[i] = K.C_TURN * MathK.Pow(m, K.EXP_TURN) * (1f + K.FIN_TURN * (finPairs - 1) * fins);

        // locomotion: smooth speeds in each medium from the organ sizes
        float drag = K.ARMOUR_DRAG * armour / MathF.Sqrt(legs >= K.LIMB_MIN ? legPairs : 1);   // more legs spread a heavy body's weight
        float v = K.C_SPEED * MathK.Pow(m, K.EXP_SPEED) * fs / (1f + drag);
        float vLand = v * (K.CRAWL + (1f - K.CRAWL) * legs);
        float vWater = v * K.SWIM_SPEED * (0.1f + 0.9f * fins);
        bool flies = wings * K.WING_LIFT >= MathK.Cbrt(m);
        float vAir = flies ? v * K.FLY_SPEED * MathF.Min(1f, wings * K.WING_LIFT / MathK.Cbrt(m) - 0.5f) : 0f;
        bool deep = 0.1f + 0.9f * fins >= K.DEEP_SWIM;   // swims well enough to stay up in open water
        bool settled = legs < K.LIMB_MIN * 0.2f && fins < K.LIMB_MIN * 0.2f && !flies;
        if (settled) { vLand = 0; vWater = 0; }
        w.PVLand[i] = vLand; w.PVWater[i] = vWater; w.PVAir[i] = vAir;
        Realm realm = flies ? Realm.Any : Realm.Land | Realm.Shallow | (deep ? Realm.Deep : 0);
        Loco loco = settled ? Loco.Sessile : flies ? Loco.Fly : vWater > vLand ? (legs >= K.LIMB_MIN ? Loco.Wade : Loco.Swim) : (fins >= K.LIMB_MIN ? Loco.Wade : Loco.Walk);
        if (settled) realm = fins > legs ? Realm.Water : Realm.Land;
        w.PLoco[i] = (byte)loco; w.PRealm[i] = (byte)realm;
        w.PVmax[i] = MathF.Max(vAir, MathF.Max(vLand, vWater));

        w.PArmour[i] = armour;
        w.PStrike[i] = strike;
        w.PLitter[i] = (byte)Math.Clamp((int)MathF.Round(MathF.Exp(Organ(gn[g + G.B_LITTER]) * MathF.Log(K.LITTER_MAX))), 1, K.LITTER_MAX);
        float birth = G.GeneLog(gn[g + G.B_BIRTH], K.BIRTH_LO, K.BIRTH_HI);
        w.PBirth[i] = birth;
        w.PSexual[i] = (byte)(gn[g + G.B_SEX] >= 0 ? 1 : 0);
        w.PLive[i] = (byte)(gn[g + G.B_LIVE] >= 0 ? 1 : 0);
        // gestation (or incubation) grows with body size and with how big the young are at birth
        w.PGest[i] = K.C_GEST * t * MathF.Sqrt(birth / 0.1f);
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
            float raw = Reachable(realm, p) ? Organ(w.Genome[g + G.B_DIET + p]) : 0f;   // linear: no plateau at the floor
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

    /// <summary>Genetic distance between two organisms' body genes: root-mean-square per gene (/127), so it does not grow with the number of genes.</summary>
    public static float Distance(World w, int a, int b)
    {
        int ga = a * G.LEN + G.BODY, gb = b * G.LEN + G.BODY; float s = 0;
        for (int k = 0; k < G.NBODY; k++) { float d = (w.Genome[ga + k] - w.Genome[gb + k]) * (1f / 127f); s += d * d; }
        return MathF.Sqrt(s / G.NBODY);
    }

    static readonly int[] Visible = { G.B_MASS, G.B_LEGS, G.B_FINS, G.B_WINGS, G.B_ARMOUR, G.B_STRIKE, G.B_LEGN, G.B_FINN };

    /// <summary>Does b look like a's kind? (visible body genes within KIN_LOOK, root-mean-square per gene)</summary>
    public static bool LooksAlike(World w, int a, int b)
    {
        int ga = a * G.LEN, gb = b * G.LEN; float s = 0;
        foreach (int k in Visible) { float d = (w.Genome[ga + k] - w.Genome[gb + k]) * (1f / 127f); s += d * d; }
        return s < K.KIN_LOOK * K.KIN_LOOK * Visible.Length;
    }
}
