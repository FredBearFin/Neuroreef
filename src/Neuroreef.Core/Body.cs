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
    /// <summary>Memory units in use from the brain-size gene: log scale BRAIN_MIN..G.NH, so a mutation changes a brain
    /// by a share of its size, as with body mass (Revision M: on a linear scale of 256 units, ordinary founder variation
    /// swung a 12-unit brain by tens of units).</summary>
    public static int Units(sbyte b) => Math.Clamp((int)MathF.Round(K.BRAIN_MIN * MathF.Pow((float)G.NH / K.BRAIN_MIN, Organ(b))), K.BRAIN_MIN, G.NH);
    /// <summary>The brain-size gene for a number of memory units (inverse of Units).</summary>
    public static sbyte UnitsGene(float units) => (sbyte)Math.Clamp((int)MathF.Round(MathF.Log(Math.Max(units, K.BRAIN_MIN) / K.BRAIN_MIN) / MathF.Log((float)G.NH / K.BRAIN_MIN) * 254f - 127f), -127, 127);

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
        float wecon = Organ(gn[g + G.B_WATER]);
        float voice = Organ(gn[g + G.B_VOICE]);
        w.PVoice[i] = voice;
        int brain = Units(gn[g + G.B_BRAIN]);
        bool herm = gn[g + G.B_HERM] >= 0;
        w.PLegs[i] = legs; w.PFins[i] = fins; w.PWings[i] = wings;
        w.PLegPairs[i] = (byte)legPairs; w.PFinPairs[i] = (byte)finPairs; w.PEyePairs[i] = (byte)eyePairs;
        w.PBrain[i] = (byte)brain; w.PHerm[i] = (byte)(herm ? 1 : 0); w.PImmune[i] = imm;
        w.PSexRatio[i] = Organ(gn[g + G.B_SEXRATIO]);

        w.PM[i] = m;
        w.PWaterEcon[i] = wecon;
        w.PThirstK[i] = MathK.Pow(m, K.EXP_THIRST) * (1f - K.WATER_ECON * wecon);
        float mPow = MathK.Pow(m, K.EXP_BASAL);
        float basalF = 1f + K.COST_SPEED * (fs - 1f) + K.COST_SIGHT * (fsi - 1f) + K.COST_ARMOUR_ABS * armour + K.COST_STRIKE_ABS * strike
                     + K.COST_LIMB * (legs * legPairs * 0.5f + fins * finPairs * 0.5f) + K.COST_WING * wings * MathK.Cbrt(m)
                     + K.COST_EYE * eyePairs + K.COST_LONGEVITY * (life - 1f)
                     + (herm ? K.COST_HERM : 0f) + K.COST_IMMUNE * imm + K.COST_WATERECON * wecon + K.COST_VOICE * voice;
        // brain tissue costs the same per unit whatever the body (Revision M, ecology review: energy per neuron is about
        // constant, Herculano-Houzel 2011; Aiello & Wheeler 1995): a big brain needs a big body to pay for it
        w.PBasal[i] = K.C_BASAL * mPow * MathF.Max(0.5f, basalF) + K.COST_BRAIN * K.C_BASAL * brain / K.BRAIN_REF;
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
        if (!flies) vLand *= 1f + K.GLIDE * wings;   // wings that cannot lift still help (wing-assisted running), from the first increment
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
        birth = MathF.Min(K.BIRTH_HI, MathF.Max(birth, K.NEWBORN_MIN / m));   // eggs and young have a smallest size (Revision M)
        w.PBirth[i] = birth;
        w.PSexual[i] = (byte)(gn[g + G.B_SEX] >= 0 ? 1 : 0);
        w.PLive[i] = (byte)(gn[g + G.B_LIVE] >= 0 ? 1 : 0);
        // gestation (or incubation) grows with body size and with how big the young are at birth
        w.PGest[i] = K.C_GEST * t * MathF.Sqrt(birth / 0.1f);
        w.PCool[i] = K.C_COOL * t;
        w.PCare[i] = Organ(gn[g + G.B_CARE]);
        ExpressDiet(w, i, realm, new Span<float>(w.PDiet, i * Pools.N, Pools.N));
        // a meat-eater can feed on a carcass, its own kill's included (Revision L, ecology review: wolves
        // return to a kill for days, and almost every carnivore scavenges)
        { int d = i * Pools.N; w.PDiet[d + (int)Pool.Carrion] = MathF.Max(w.PDiet[d + (int)Pool.Carrion], K.SCAVENGE * w.PDiet[d + (int)Pool.Meat]); }
        // a blood specialist with weapons drains its victims instead of killing them (a parasite habit)
        float blood = w.PDiet[i * Pools.N + (int)Pool.Blood], meat = w.PDiet[i * Pools.N + (int)Pool.Meat];
        // drain or kill is a matter of degree (ecology review): the share of successful strikes that drain blood
        w.PDrain[i] = blood + meat > 1e-4f ? blood / (blood + meat) : 0f;
        w.PParasite[i] = (byte)(w.PDrain[i] >= 0.5f ? 1 : 0);   // a label (and whom it targets): mostly a blood-drinker
    }

    static void ExpressDiet(World w, int i, Realm realm, Span<float> dst)
    {
        int g = i * G.LEN;
        float sum = 0;
        for (int p = 0; p < Pools.N; p++)
        {
            // linear map, L2 budget (Revision I): every gene step counts the same, so a pool can be selected up from zero,
            // and a small side share costs a specialist almost nothing, which is how omnivory starts. An even 6-way
            // generalist digests each food at ~0.41 against a specialist's 0.95. (A linear map with an L1 budget starved:
            // every ancestor became an even generalist at 1/6 per food.)
            float raw = Reachable(realm, p) ? Organ(w.Genome[g + G.B_DIET + p]) : 0f;
            dst[p] = raw; sum += raw * raw;
        }
        float k = sum > 0 ? K.DIET_BUDGET / MathF.Sqrt(sum) : 0;
        // the gene share of the digestive budget (a specialist's 0.95) times the most any animal can get from that food
        // (Revision M, ecology review: herbivores assimilate 20-60% of plant food, carnivores ~80%; Van Soest 1994, Robbins 1993)
        for (int p = 0; p < Pools.N; p++) dst[p] = MathF.Min(0.95f, dst[p] * k) / 0.95f * Pools.DigMax[p];
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
        const int N = G.NBODY - G.NMHC;              // recognition genes are not part of fertility (balancing selection keeps them diverse)
        for (int k = 0; k < N; k++) { float d = (w.Genome[ga + k] - w.Genome[gb + k]) * (1f / 127f); s += d * d; }
        return MathF.Sqrt(s / N);
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
