// Bodies from genes (DESIGN §6.3, Revision F). Every property of an organism is expressed from its own
// genome at creation, on fixed absolute ranges; there is no designed species body plan and no designed
// threshold that decides what a body "is". Each trait has a physical cost, so evolution trades them off:
//   mass       everything scales by allometry (Kleiber): a bigger body costs more and matures later
//   legs, fins, wings  locomotion organs: speed on land, in water and in the air follows smoothly from their
//              sizes; a body flies when its flight muscles' power (~wings x M^0.7) reaches the least power flight
//              requires (~M^(7/6)), and stays in deep water when it swims well enough; fins and wings cost upkeep,
//              legs cost their swing while walking and flight its power while airborne (Resolve)
//   leg pairs, fin pairs, eye pairs   how many: more legs share the body's weight, steady a small body on rough
//              ground and cost more to swing, more fins steer,
//              more eyes see around; each pair costs upkeep
//   speed, sight, field of view   multipliers that raise basal cost; a narrow field of view sees further
//   armour, strike   defence and weapons, each costly; armour slows
//   diet       shares of a fixed digestive budget over the 15 pools; a pool its realm cannot offer is never digested
//   life history   lifespan (longer life costs body repair), development time (fast developers age sooner),
//              brain size (memory units cost energy), sexual or asexual, separate sexes or hermaphrodite and
//              the sex ratio, eggs or live birth, litter, newborn size, care, immune investment
//   fur, appendages   insulation and radiators (Revision O): what a body costs to keep at T_BODY follows from its
//              size, its fur and its appendages against the temperature around it (Heat); each costs upkeep

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

    /// <summary>Relative body axes, sharing the renderer's exponential scale and unit-volume normalization.</summary>
    public static void Dimensions(sbyte lengthGene, sbyte widthGene, sbyte heightGene, out float length, out float width, out float height)
    {
        float logTwo = MathK.Log(2f);
        length = MathK.Exp(logTwo * (2f * Organ(lengthGene) - 1f));
        width = MathK.Exp(logTwo * (2f * Organ(widthGene) - 1f));
        height = MathK.Exp(logTwo * (2f * Organ(heightGene) - 1f));
        float volumeScale = MathK.Cbrt(length * width * height);
        length /= volumeScale; width /= volumeScale; height /= volumeScale;
    }
    /// <summary>The smallest body of each size class (the user, 2026-10-30).</summary>
    public static readonly float[] Floors = { 0.1f, 1f, 3f, 10f };
    /// <summary>Memory units in use from the brain-size gene: log scale BRAIN_MIN..G.NH, so a mutation changes a brain
    /// by a share of its size, as with body mass (Revision M: on a linear scale of 256 units, ordinary founder variation
    /// swung a 12-unit brain by tens of units).</summary>
    public static int Units(sbyte b) => Math.Clamp((int)MathF.Round(K.BRAIN_MIN * MathK.Pow((float)G.NH / K.BRAIN_MIN, Organ(b))), K.BRAIN_MIN, G.NH);
    /// <summary>The brain-size gene for a number of memory units (inverse of Units).</summary>
    public static sbyte UnitsGene(float units) => (sbyte)Math.Clamp((int)MathF.Round(MathK.Log(Math.Max(units, K.BRAIN_MIN) / K.BRAIN_MIN) / MathK.Log((float)G.NH / K.BRAIN_MIN) * 254f - 127f), -127, 127);

    public static void Express(World w, int i)
    {
        int g = i * G.LEN;
        var gn = w.Genome;
        Signature(w, i);   // its look and MHC diversity, cached for DECIDE and LIFECYCLE (see Signature)
        float floor = Floors[Math.Clamp((int)gn[g + G.B_FLOOR], 0, K.FLOOR_CLASSES - 1)];
        w.PFloor[i] = floor;
        float m = MathF.Max(floor, G.GeneLog(gn[g + G.B_MASS], K.MASS_LO, K.MASS_HI));   // its size class's floor; free above it
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
        // body heat (Revision O): fur keeps heat in; appendages (ears, limbs, tail) shed it when the body is warm
        float ins = Organ(gn[g + G.B_INSUL]), app = Organ(gn[g + G.B_APPEND]);
        w.PInsul[i] = ins; w.PAppend[i] = app;
        w.PEndo[i] = Organ(gn[g + G.B_ENDO]);
        w.PReadyE[i] = K.READY_LO + (K.READY_HI - K.READY_LO) * Organ(gn[g + G.B_BREEDE]);
        w.PVoice[i] = voice;
        // vocal learning (Revision N): how strongly matching a remembered tutor reinforces its own calls, and for how
        // much of its life it memorises tutors (Doupe & Kuhl 1999; Brainard & Doupe 2002). No upkeep cost of its own:
        // the learning uses the brain it already pays for, so the gene drifts where it is of no use
        w.PImit[i] = Organ(gn[g + G.B_IMIT]);
        w.PImitAge[i] = K.IMIT_AGE_LO + (1f - K.IMIT_AGE_LO) * Organ(gn[g + G.B_IMIT_AGE]);
        int brain = Units(gn[g + G.B_BRAIN]);
        bool herm = gn[g + G.B_HERM] >= 0;
        w.PLegs[i] = legs; w.PFins[i] = fins; w.PWings[i] = wings;
        w.PLegPairs[i] = (byte)legPairs; w.PFinPairs[i] = (byte)finPairs; w.PEyePairs[i] = (byte)eyePairs;
        w.PBrain[i] = (ushort)brain; w.PHerm[i] = (byte)(herm ? 1 : 0); w.PImmune[i] = imm;
        w.PWire[i] = WireScale(gn.AsSpan(g, G.LEN), brain);   // after the brain size: it scales the units in use
        w.PSexRatio[i] = Organ(gn[g + G.B_SEXRATIO]);

        w.PM[i] = m;
        w.PWaterEcon[i] = wecon;
        w.PThirstK[i] = MathK.Pow(m, K.EXP_THIRST) * (1f - K.WATER_ECON * wecon);
        float mPow = MathK.Pow(m, K.EXP_BASAL);
        float basalF = 1f + K.COST_SPEED * (fs - 1f) + K.COST_SIGHT * (fsi - 1f) + K.COST_ARMOUR_ABS * armour + K.COST_STRIKE_ABS * strike
                     + K.COST_LIMB * fins * finPairs * 0.5f + K.COST_WING * wings   // legs pay while they walk (LEG_SWING), flight while airborne
                     + K.COST_EYE * eyePairs + K.COST_LONGEVITY * (life - 1f)
                     + (herm ? K.COST_HERM : 0f) + K.COST_IMMUNE * imm + K.COST_WATERECON * wecon + K.COST_VOICE * voice
                     + K.COST_INS * ins + K.COST_APP * app;
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

        // locomotion: smooth speeds in each medium from the organ sizes. On land the legs carry the body's weight and its
        // armour: bone stress grows as M^(1/3), and more legs share it (LOAD_DRAG; an unarmoured two-pair body is the
        // allometry's own reference). In water and air armour only adds drag
        float v0 = K.C_SPEED * MathK.Pow(m, K.EXP_SPEED) * fs;
        float v = v0 / (1f + K.ARMOUR_DRAG * armour * 0.70710678f);
        float loadDrag = K.LOAD_DRAG * MathK.Cbrt(m) * ((1f + armour) / MathF.Sqrt(legs >= K.LIMB_MIN ? legPairs : 1) - 0.70710678f);
        float vLand = v0 / (1f + loadDrag) * (K.CRAWL + (1f - K.CRAWL) * legs);
        float vWater = v * K.SWIM_SPEED * (0.1f + 0.9f * fins);
        Dimensions(gn[g + G.B_LENGTH], gn[g + G.B_WIDTH], gn[g + G.B_HEIGHT], out float length, out float width, out float height);
        const float invLn2 = 1.442695041f;   // log2 through MathK.Log: the same on every machine (NR-104)
        length = MathK.Log(length) * invLn2 * 0.5f; width = MathK.Log(width) * invLn2 * 0.5f; height = MathK.Log(height) * invLn2 * 0.5f;
        float limbShape = Organ(gn[g + G.B_LIMB_SHAPE]) - 0.5f;
        // Long, narrow bodies slip through water but turn more slowly; tall, slender limbs favour stride
        // over paddling. Each bounded factor stays near one, so organs and mass still govern viability.
        float streamlining = Math.Clamp(1f + 0.2f * length - 0.15f * width - 0.15f * height, 0.75f, 1.25f);
        vLand *= 1f + 0.15f * height + 0.2f * limbShape * legs - 0.1f * width;
        vWater *= streamlining * (1f - 0.2f * limbShape * legs);
        w.PTurn[i] *= 1f - 0.2f * length + 0.1f * width;
        // flight (2026-10-31): a body flies when the power its flight muscles deliver (~ wings x M^0.7) reaches the least
        // power flight requires (~M^(7/6)) and its flight muscle can lift its weight at all (FLY_WING_MIN); the margin
        // sets how far above the speed of least power it can fly
        float margin = FlightMargin(wings, m);
        bool flies = margin >= 1f && wings >= K.FLY_WING_MIN;   // and enough flight muscle to take off at all (Marden 1987)
        if (!flies) vLand *= 1f + K.GLIDE * wings;   // wings that cannot lift still help (wing-assisted running), from the first increment
        float vAir = flies ? MathF.Min(K.FLY_V_CAP, v * K.FLY_VMP * TopFlightSpeed(margin) * streamlining) : 0f;
        bool deep = 0.1f + 0.9f * fins >= K.DEEP_SWIM;   // swims well enough to stay up in open water
        bool settled = legs < K.LIMB_MIN * 0.2f && fins < K.LIMB_MIN * 0.2f && !flies;
        if (settled) { vLand = 0; vWater = 0; }
        w.PVLand[i] = vLand; w.PVWater[i] = vWater; w.PVAir[i] = vAir;
        Realm realm = flies ? Realm.Any : Realm.Land | Realm.Shallow | (deep ? Realm.Deep : 0);
        Loco loco = settled ? Loco.Sessile : flies ? Loco.Fly : vWater > vLand ? (legs >= K.LIMB_MIN ? Loco.Wade : Loco.Swim) : (fins >= K.LIMB_MIN ? Loco.Wade : Loco.Walk);
        if (settled) realm = fins > legs ? Realm.Water : Realm.Land;
        // a flier eats where it can land: water-column food only if it also wades or swims (2026-10-31)
        Realm reach = flies ? GroundRealm(legs, fins) : realm;
        w.PLoco[i] = (byte)loco; w.PRealm[i] = (byte)realm;
        w.PVmax[i] = MathF.Max(vAir, MathF.Max(vLand, vWater));

        w.PArmour[i] = armour;
        w.PStrike[i] = strike;
        w.PLitter[i] = (byte)Math.Clamp((int)MathF.Round(MathK.Exp(Organ(gn[g + G.B_LITTER]) * MathK.Log(K.LITTER_MAX))), 1, K.LITTER_MAX);
        float birth = G.GeneLog(gn[g + G.B_BIRTH], K.BIRTH_LO, K.BIRTH_HI);
        birth = MathF.Min(K.BIRTH_HI, MathF.Max(birth, K.NEWBORN_MIN / m));   // eggs and young have a smallest size (Revision M)
        w.PBirth[i] = birth;
        w.PSexual[i] = (byte)(gn[g + G.B_SEX] >= 0 ? 1 : 0);
        w.PLive[i] = (byte)(gn[g + G.B_LIVE] >= 0 ? 1 : 0);
        // gestation (or incubation) grows with body size and with how big the young are at birth
        w.PGest[i] = K.C_GEST * t * MathF.Sqrt(birth / 0.1f);
        w.PCool[i] = K.C_COOL * t;
        w.PCare[i] = Organ(gn[g + G.B_CARE]);
        ExpressDiet(w, i, reach, new Span<float>(w.PDiet, i * Pools.N, Pools.N));
        // a meat-eater can feed on a carcass, its own kill's included (Revision L, ecology review: wolves
        // return to a kill for days, and almost every carnivore scavenges)
        { int d = i * Pools.N; w.PDiet[d + (int)Pool.Carrion] = MathF.Max(w.PDiet[d + (int)Pool.Carrion], K.SCAVENGE * w.PDiet[d + (int)Pool.Meat]); }
        // a blood specialist with weapons drains its victims instead of killing them (a parasite habit)
        float blood = w.PDiet[i * Pools.N + (int)Pool.Blood], meat = w.PDiet[i * Pools.N + (int)Pool.Meat];
        // drain or kill is a matter of degree (ecology review): the share of successful strikes that drain blood
        w.PDrain[i] = blood + meat > 1e-4f ? blood / (blood + meat) : 0f;
        w.PParasite[i] = (byte)(w.PDrain[i] >= 0.5f ? 1 : 0);   // a label (and whom it targets): mostly a blood-drinker
    }

    /// <summary>Where a flier can stand, perch, feed or rest once landed: land; shallow water if it wades on legs or
    /// swims; open water if it swims well enough to stay up (DEEP_SWIM).</summary>
    public static Realm GroundRealm(float legs, float fins)
    {
        bool swims = 0.1f + 0.9f * fins >= K.DEEP_SWIM;
        return Realm.Land | (swims || legs >= K.LIMB_MIN ? Realm.Shallow : 0) | (swims ? Realm.Deep : 0);
    }

    /// <summary>Flight power margin (2026-10-31): the power a body's flight muscles can deliver over the least power its
    /// flight requires, wings x (FLY_M_MAX / M)^(EXP_FLY_REQ - EXP_FLY_AVAIL). It flies when this is at least 1.</summary>
    public static float FlightMargin(float wings, float m) => wings * MathK.Pow(K.FLY_M_MAX / m, K.EXP_FLY_REQ - K.EXP_FLY_AVAIL);

    /// <summary>Power of flight against its least, at speed u x the speed of least power: (3/u + u^3)/4 (induced power
    /// falling as 1/u, profile and parasite power rising as u^3; minimum 1 at u = 1). Below FLY_U_MIN it is hovering.</summary>
    public static float PowerCurve(float u)
    {
        u = MathF.Max(u, K.FLY_U_MIN);
        return 0.25f * (3f / u + u * u * u);
    }

    /// <summary>Fastest level flight as a multiple of the speed of least power: where the power curve meets the margin,
    /// at most FLY_X_MAX (bisection: the curve rises monotonically above u = 1).</summary>
    public static float TopFlightSpeed(float margin)
    {
        if (margin <= 1f) return 1f;
        if (PowerCurve(K.FLY_X_MAX) <= margin) return K.FLY_X_MAX;
        float lo = 1f, hi = K.FLY_X_MAX;
        for (int k = 0; k < 24; k++) { float mid = 0.5f * (lo + hi); if (PowerCurve(mid) < margin) lo = mid; else hi = mid; }
        return lo;
    }

    /// <summary>
    /// How far a flier of mass m would go in one whole step of flying at its speed of least power, in world units: the
    /// world's per-step speeds are a step's travel, not a flight speed (a step is hours), so an airborne step is spent
    /// in the air only for the share of it its distance needs (Resolve.MoveCost). Calibrated so flying at the speed of
    /// least power costs FLY_COT_RATIO of walking per distance at FLY_M_REF (Tucker 1970; Schmidt-Nielsen 1972), and
    /// scaling as the speed of least power, M^(1/6) (Pennycuick 2008). Per distance the ratio then grows as M^0.3
    /// (power ~M^(7/6) over speed ~M^(1/6), against walking ~M^0.7): 0.2 at M = 0.1, 0.9 at M = 16.
    /// </summary>
    public static float FlightFullStep(float m)
    {
        float netRef = FlightPowerMin(K.FLY_M_REF) - K.C_BASAL * MathK.Pow(K.FLY_M_REF, K.EXP_BASAL);
        float walkRef = K.C_MOVE * K.C_SPEED * MathK.Pow(K.FLY_M_REF, K.EXP_MOVE);   // per unit distance, two pairs of full legs at full speed
        return netRef / (K.FLY_COT_RATIO * walkRef) * MathK.Pow(m / K.FLY_M_REF, 1f / 6f);
    }

    /// <summary>The least power of flight at mass m (2026-10-31): FLIGHT_MULT x basal at FLY_M_REF, rising as M^(7/6).</summary>
    public static float FlightPowerMin(float m)
        => K.FLIGHT_MULT * K.C_BASAL * MathK.Pow(K.FLY_M_REF, K.EXP_BASAL) * MathK.Pow(m / K.FLY_M_REF, K.EXP_FLY_REQ);

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
        // fibre is digested by fermentation, which takes time: gut capacity grows as M while the rate food passes
        // grows as M^0.75, so food stays M^0.25 longer in a bigger gut and more of its fibre is fermented (the
        // Jarman-Bell principle; Demment & Van Soest 1985). The share fermented is 1 - exp(-M^0.25 / TAU_FERM), against
        // its value at M = 1: a small animal gets less from grass and leaves, a big one more (never above DigMax)
        float ferm = Ferment(w.PM[i]);
        for (int p = 0; p < Pools.N; p++) if (Pools.Fibrous(p)) dst[p] = MathF.Min(Pools.DigMax[p], dst[p] * ferm);
    }

    /// <summary>
    /// The scale of a brain's recurrent weights (Revision P): its wiring-gain gene's spectral radius (WIRE_LO..WIRE_HI)
    /// over the radius its genome's recurrent block among its units has, estimated by the circular law as
    /// sqrt(sum of squared weights / units). A reservoir holds the most memory near a radius of 1 (Jaeger 2001); founder
    /// genomes had 2 at 24 units and 4.3 at 256, chaotic, and a 256-unit brain recalled about 7 steps of its input.
    /// </summary>
    public static float WireScale(ReadOnlySpan<sbyte> genome, int units)
    {
        if (units <= 0) return 1f;
        double s = 0;
        for (int h = 0; h < units; h++)
        {
            int r = h * G.NIN + F.N;
            for (int k = 0; k < units; k++) { double v = genome[r + k] * K.WSCALE; s += v * v; }
        }
        double rho = Math.Sqrt(s / units);
        float target = K.WIRE_LO + (K.WIRE_HI - K.WIRE_LO) * Organ(genome[G.B_WIRE]);
        return rho > 1e-6 ? (float)(target / rho) : 1f;
    }

    /// <summary>Fibre fermented by a body of mass m, against a body of mass 1 (Jarman-Bell; TAU_FERM).</summary>
    public static float Ferment(float m) => (1f - MathK.Exp(-MathK.Pow(m, 0.25f) / K.TAU_FERM)) / (1f - MathK.Exp(-1f / K.TAU_FERM));

    /// <summary>Can an organism living in this realm get at this pool?</summary>
    public static bool Reachable(Realm realm, int p) => p switch
    {
        (int)Pool.Algae => (realm & Realm.Shallow) != 0,
        (int)Pool.Phyto or (int)Pool.Detritus => (realm & Realm.Water) != 0,
        (int)Pool.Meat or (int)Pool.Blood => true,
        _ => (realm & Realm.Land) != 0,
    };

    /// <summary>Root-mean-square distance in the original ecological body genes (/127).
    /// Appearance and recognition variation do not create reproductive barriers or dilute ecological differences.</summary>
    public static float Distance(World w, int a, int b)
    {
        int ga = a * G.LEN + G.BODY, gb = b * G.LEN + G.BODY; float s = 0;
        const int N = G.APPEARANCE - G.BODY;
        for (int k = 0; k < N; k++) { float d = (w.Genome[ga + k] - w.Genome[gb + k]) * (1f / 127f); s += d * d; }
        return MathF.Sqrt(s / N);
    }

    static readonly int[] Visible = { G.B_MASS, G.B_LEGS, G.B_FINS, G.B_WINGS, G.B_ARMOUR, G.B_STRIKE, G.B_LEGN, G.B_FINN };

    /// <summary>Does b look like a's kind? Ecological traits and appearance each pass their own RMS test,
    /// so new cosmetic genes cannot dilute an existing visible ecological difference.</summary>
    public static bool LooksAlike(World w, int a, int b)
    {
        int ga = a * G.LEN, gb = b * G.LEN; float s = 0;
        foreach (int k in Visible) { float d = (w.Genome[ga + k] - w.Genome[gb + k]) * (1f / 127f); s += d * d; }
        if (s >= K.KIN_LOOK * K.KIN_LOOK * Visible.Length) return false;
        float shape = 0f, colour = 0f;
        for (int k = G.APPEARANCE; k < G.B_MHC; k++)
        {
            float d = MathF.Abs(w.Genome[ga + k] - w.Genome[gb + k]) * (1f / 127f);
            if (k == G.B_HUE || k == G.B_PATTERN_HUE) d = MathF.Min(d, 2f - d);
            if (k < G.B_HUE) shape += d * d;
            else colour += d * d;
        }
        return shape < K.KIN_LOOK * K.KIN_LOOK * 10 && colour < K.KIN_LOOK * K.KIN_LOOK * 6;
    }

    // ------------------------------------------------------------------ cached look (2026-10-31, performance only)

    /// <summary>Bytes of a look signature: the Visible genes in their order, then the appearance genes (APPEARANCE..B_MHC).</summary>
    public const int NLOOK = NVISIBLE + G.NAPPEARANCE;
    const int NVISIBLE = 8;
    const int HUE = NVISIBLE + G.B_HUE - G.APPEARANCE, PATTERN_HUE = NVISIBLE + G.B_PATTERN_HUE - G.APPEARANCE, SHAPE_END = NVISIBLE + G.B_HUE - G.APPEARANCE;

    /// <summary>
    /// Cache slot i's look signature (World.D.Look: the genes LooksAlike compares, NLOOK bytes together) and its MHC
    /// diversity (World.D.MhcDiv), from its genome. Called by Express, so whenever the genome is expressed. DECIDE compares
    /// each animal with its neighbours' looks, and the genes lie G.LEN (98 KB) apart in the genome array; the signature
    /// holds them in one cache line. Nothing changes: LooksAlike(sig, sig) is LooksAlike(w, a, b) bit for bit.
    /// </summary>
    public static void Signature(World w, int i)
    {
        int g = i * G.LEN, o = i * NLOOK;
        var gn = w.Genome; var s = w.D.Look;
        for (int k = 0; k < NVISIBLE; k++) s[o + k] = gn[g + Visible[k]];
        for (int k = 0; k < G.NAPPEARANCE; k++) s[o + NVISIBLE + k] = gn[g + G.APPEARANCE + k];
        w.D.MhcDiv[i] = Pathogens.Diversity(w, i);
    }

    /// <summary>LooksAlike from the cached signatures of a and b (Signature): the same test, in the same order.</summary>
    public static unsafe bool LooksAlikeCached(World w, int a, int b)
    {
        fixed (sbyte* s = w.D.Look) return LooksAlike(s + a * NLOOK, s + b * NLOOK);
    }

    /// <summary>LooksAlike of two look signatures (NLOOK bytes each; Signature): the same arithmetic as LooksAlike(w, a, b).</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static unsafe bool LooksAlike(sbyte* a, sbyte* b)
    {
        float s = 0;
        for (int k = 0; k < NVISIBLE; k++) { float d = (a[k] - b[k]) * (1f / 127f); s += d * d; }
        if (s >= K.KIN_LOOK * K.KIN_LOOK * NVISIBLE) return false;
        float shape = 0f, colour = 0f;
        for (int k = NVISIBLE; k < NLOOK; k++)
        {
            float d = MathF.Abs(a[k] - b[k]) * (1f / 127f);
            if (k == HUE || k == PATTERN_HUE) d = MathF.Min(d, 2f - d);
            if (k < SHAPE_END) shape += d * d;
            else colour += d * d;
        }
        return shape < K.KIN_LOOK * K.KIN_LOOK * 10 && colour < K.KIN_LOOK * K.KIN_LOOK * 6;
    }
}
