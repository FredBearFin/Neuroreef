// Bodies from genes (DESIGN §6.3, Revision F). Every property of an organism is expressed from its own
// genome at creation, on fixed absolute ranges; there is no designed species body plan and no designed
// threshold that decides what a body "is". Each trait has a physical cost, so evolution trades them off:
//   mass       everything scales by allometry (Kleiber): a bigger body costs more and matures later
//   legs, fins, wings  locomotion organs: speed on land, in water and in the air follows smoothly from their
//              sizes; a body flies when its flight muscles' power (~wings x M^(2/3)) reaches the least power flight
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
        // each axis from 1 / SHAPE_RANGE to SHAPE_RANGE of the cube's before the volume is restored (2026-11-02: was 1/2 to 2,
        // so the longest body was 2.5 cube sides, fineness 4; now 4.3, fineness up to 9, an eel's)
        float logR = MathK.Log(K.SHAPE_RANGE);
        length = MathK.Exp(logR * (2f * Organ(lengthGene) - 1f));
        width = MathK.Exp(logR * (2f * Organ(widthGene) - 1f));
        height = MathK.Exp(logR * (2f * Organ(heightGene) - 1f));
        float volumeScale = MathK.Cbrt(length * width * height);
        length /= volumeScale; width /= volumeScale; height /= volumeScale;
    }
    /// <summary>The smallest body of each size class (the user, 2026-10-30): 0.1, 1, 3 and 10 kg at the default body sizes,
    /// the world's own (BodySize) since 2026-11-01.</summary>
    public static float[] Floors => BodySize.Floors;
    /// <summary>Memory units in use from the brain-size gene: log scale BRAIN_MIN..G.NH, so a mutation changes a brain
    /// by a share of its size, as with body mass (Revision M: on a linear scale of 256 units, ordinary founder variation
    /// swung a 12-unit brain by tens of units).</summary>
    public static int Units(sbyte b) => Math.Clamp((int)MathF.Round(K.BRAIN_MIN * MathK.Pow((float)G.NH / K.BRAIN_MIN, Organ(b))), K.BRAIN_MIN, G.NH);
    /// <summary>The brain-size gene for a number of memory units (inverse of Units).</summary>
    public static sbyte UnitsGene(float units) => (sbyte)Math.Clamp((int)MathF.Round(MathK.Log(Math.Max(units, K.BRAIN_MIN) / K.BRAIN_MIN) / MathK.Log((float)G.NH / K.BRAIN_MIN) * 254f - 127f), -127, 127);

    /// <summary>
    /// The most memory units a body of m kg can surely pay for: its brain at most BRAIN_SHARE_MAX of its basal cost (the
    /// founding test, Life.RangeCapacity), with the cheapest body of that mass (basal factor 0.5, Express). A unit costs the
    /// same whatever the body (energy per neuron is about constant, Herculano-Houzel 2011), so it falls as M^0.75: ~140 units
    /// at 0.1 kg, ~770 at 1 kg (capped at G.NH by Units), ~24 at 10 g, 4 at 1 g (BRAIN_SHARE_MAX 0.6 since 2026-11-01; 0.25 before). Clades.AncestorBody draws a random ancestor's brain within it.
    /// </summary>
    public static float UnitsAffordable(float m) =>
        K.BRAIN_SHARE_MAX / (1f - K.BRAIN_SHARE_MAX) * 0.5f * MathK.Pow(m, K.EXP_BASAL) * K.BRAIN_REF / K.COST_BRAIN;

    /// <summary>
    /// The size of a body's gut against the reference gut of its mass (2026-11-02), and its whole basal cost with that
    /// gut's upkeep. Guts are built to the energy they must pass: their mass and absorptive capacity grow with demand
    /// (lactation, cold, migratory fattening; Hammond &amp; Diamond 1997; McWilliams &amp; Karasov 2001), so an animal's
    /// sustained intake is a fixed multiple of its own basal metabolism (the sustained metabolic scope, ~4-7x), not of
    /// its mass alone. Gut tissue is costly (GUT_SHARE of a Kleiber body's basal cost for the reference gut), so a gut
    /// of g references costs g x GUT_SHARE x Kleiber, and is built so its absorption, C_ABSORB x M^0.75 x g, is
    /// C_ABSORB / C_BASAL times the whole basal cost, its own upkeep included: g = rest / ((1 - GUT_SHARE) Kleiber),
    /// at most GUT_FLEX_MAX. A Kleiber body keeps the reference gut (g = 1) and its cost; every other cost is raised by
    /// 1 / (1 - GUT_SHARE), the gut that feeds it. No energy is made: the gut only passes food that is eaten, and pays for itself.
    /// </summary>
    /// <param name="basalRef">the basal cost with the reference gut (the gut's GUT_SHARE x Kleiber included), E/step</param>
    /// <param name="kleiber">Kleiber's basal cost for its mass, C_BASAL x M^0.75</param>
    /// <param name="basal">its whole basal cost with the gut it builds</param>
    public static float GutScale(float basalRef, float kleiber, out float basal) => GutScale(basalRef, kleiber, float.PositiveInfinity, out basal);

    /// <summary>The same, in a body that has room for at most room reference guts (phase 4, docs/BODYPLAN.md §13.17: the gut
    /// is tissue housed in the trunk, Morph.Live.GutRoom). A gut that cannot be housed is not built: the body builds the
    /// smaller gut it has room for, and pays that gut's upkeep only.</summary>
    public static float GutScale(float basalRef, float kleiber, float room, out float basal)
    {
        float rest = basalRef - K.GUT_SHARE * kleiber;   // everything but the gut
        float g = MathF.Min(MathF.Min(K.GUT_FLEX_MAX, MathF.Max(0f, room)), rest / ((1f - K.GUT_SHARE) * kleiber));
        basal = rest + K.GUT_SHARE * g * kleiber;
        return g;
    }

    public static void Express(World w, int i)
    {
        var gn = w.Genome.Slot(i);
        float floor = Floors[Math.Clamp((int)gn[G.B_FLOOR], 0, K.FLOOR_CLASSES - 1)];
        w.PFloor[i] = floor;
        float m = MathF.Max(floor, G.GeneLog(gn[G.B_MASS], BodySize.Min, BodySize.Max));   // its size class's floor; free above it (the world's range, BodySize)
        float fsi = Mul(gn[G.B_SIGHT], K.BODY_RANGE_TRAIT);
        float strike = Organ(gn[G.B_STRIKE]) * K.STRIKE_MAX;
        int eyePairs = Pairs(gn[G.B_EYEN]);
        float fov = MathF.Min(360f, G.Gene(gn[G.B_FOV], K.FOV_LO, K.FOV_HI) * (1f + K.EYE_FOV * (eyePairs - 1)));
        float life = Mul(gn[G.B_LIFE], K.LIFE_RANGE), devel = Mul(gn[G.B_DEVEL], K.LIFE_RANGE);
        float imm = Organ(gn[G.B_IMMUNE]);
        float wecon = Organ(gn[G.B_WATER]);
        float voice = Organ(gn[G.B_VOICE]);
        // body heat (Revision O): fur keeps heat in (a covering gene, kept). Since phase 4 the surface that loses heat, and
        // the appendages that shed it, are the grown body's own (PSurfK, PAppSurf below); B_APPEND is retired
        float ins = Organ(gn[G.B_INSUL]);
        w.PInsul[i] = ins;
        w.PEndo[i] = Organ(gn[G.B_ENDO]);
        w.PDive[i] = Organ(gn[G.B_DIVE]);   // where in the water column it holds (Respiration.cs)
        w.PReadyE[i] = K.READY_LO + (K.READY_HI - K.READY_LO) * Organ(gn[G.B_BREEDE]);
        w.PVoice[i] = voice;
        // vocal learning (Revision N): how strongly matching a remembered tutor reinforces its own calls, and for how
        // much of its life it memorises tutors (Doupe & Kuhl 1999; Brainard & Doupe 2002). No upkeep cost of its own:
        // the learning uses the brain it already pays for, so the gene drifts where it is of no use
        w.PImit[i] = Organ(gn[G.B_IMIT]);
        w.PImitAge[i] = K.IMIT_AGE_LO + (1f - K.IMIT_AGE_LO) * Organ(gn[G.B_IMIT_AGE]);
        int brain = Units(gn[G.B_BRAIN]);
        bool herm = gn[G.B_HERM] >= 0;
        w.PEyePairs[i] = (byte)eyePairs;
        w.PBrain[i] = (ushort)brain; w.PHerm[i] = (byte)(herm ? 1 : 0); w.PImmune[i] = imm;
        w.PWire[i] = WireScale(gn, brain);   // after the brain size: it scales the units in use
        w.PSexRatio[i] = Organ(gn[G.B_SEXRATIO]);

        w.PM[i] = m;
        w.PWaterEcon[i] = wecon;
        float mPow = MathK.Pow(m, K.EXP_BASAL);
        float dt = w.Dt;
        // (2026-11-02) its tissues' metabolic level (Heat.Level: a cold-blooded body's standard metabolism is ~1/5 of a
        // warm-blooded one's of its mass at the same body temperature) sets its body's resting cost, its gut and its
        // organs' capacity, in every world; the brain costs per unit whatever the body (Herculano-Houzel 2011)
        float level = Heat.Level(w.PEndo[i]);

        // ---- the body (docs/BODYPLAN.md §13.3, phase 4): grown from its module genome at its mass (Morph.Codec,
        // Development.Grow), every capability from the physics of that morphology (Morph.Live). The labelled body genes
        // (B_LEGS, B_FINS, B_WINGS, B_LEGN, B_FINN, B_ARMOUR, B_LUNGS, B_SPEED, B_APPEND, the shape genes) are retired: a
        // genome with no module block (an older save, a hand-made genome) is translated from them first (Morph.Translate),
        // and nothing else reads them
        // (decoded and grown in this thread's reused objects: no allocation at a birth; Codec.ForExpress, MorphPool)
        var mg = Morph.Codec.ForExpress(gn);
        var mo = Morph.Development.Grow(mg, m, Morph.MorphPool.Thread);
        float geneF = Morph.Live.GeneFactor(gn);
        float toW = K.BMR_W / K.C_BASAL;   // W per energy unit a reference step
        float brainW = K.COST_BRAIN * K.BMR_W * brain / K.BRAIN_REF;
        var lc = Morph.Live.Of(mo, level, geneF, brainW);
        var c = lc.C;
        // basal: Kleiber on its active (non-mineral, lean) mass at its level x the genes that stay genes, its integument's
        // turnover and its exchange epithelia's upkeep from their physics, its brain, and the gut built to all of it
        // (Body.GutScale). The labelled organs' upkeep terms (COST_LIMB, COST_WING, COST_ARMOUR_ABS, OrganUpkeep) are gone:
        // their tissue is in the morphology now. Moulting is paid while it grows (PMoultW: Resolve)
        float basal = c.Basal / toW;
        float gut = lc.Gut;
        w.PBasal[i] = basal * dt;
        // a gut-full lies in the gut's lumen, inside the trunk (docs/BODYPLAN.md §13.17): a trunk with room for less than the
        // reference gut holds that much less (Morph.Live.GutRoom); one with room for it holds the reference gut-full
        w.PGut[i] = K.C_GUT * m * MathF.Min(1f, lc.GutRoom);
        // a gut processes and absorbs at the pace of the tissues that build and run it (level): a reptile's food intake and
        // digestive throughput are a fraction of a mammal's of its size (Karasov & Diamond 1985; Nagy 2005)
        // (phase 4) the gut is built to the Kleiber cost of its active (non-mineral, lean) mass, as the basal is (Morph.Live.Of)
        float mAct = MathK.Pow(MathF.Max(1e-6f, mo.ActiveMass), K.EXP_BASAL);
        w.PDig[i] = K.C_DIGEST * mAct * gut * level * dt;
        w.PAbsorb[i] = K.C_ABSORB * mAct * gut * level * dt;   // energy the gut can take up a step (2026-11-01)
        w.PBite[i] = K.C_BITE * mPow * dt;
        float t = MathK.Pow(m, K.EXP_LIFE);
        w.PMature[i] = K.C_MATURE * t * devel / dt;
        w.PLife[i] = K.C_LIFE * t * life * MathK.Pow(devel, K.DEVEL_AGING) / dt;
        // a narrow field of view sees further (forward eyes), a wide one sees around but less far. In metres (2026-11-01):
        // C_SIGHT_M = 60 m at 1 kg, eye size and acuity ~M^0.2 (Kiltie 2000; Burton 2008)
        w.PSight[i] = MathF.Min(BodySize.MaxSight, K.C_SIGHT * MathK.Pow(m, K.EXP_SIGHT) * fsi * (1.6f - fov / 360f));
        w.PFovCos[i] = MathK.Cos(fov * 0.5f * MathF.PI / 180f);

        // speeds: a step's travel is a day range, which the live sim reads as a speed through Garland's sprint for the mass
        // x its speed against the plain body's (ReachM); so a physical top speed (m/s) is v0Ref x v / Garland's sprint
        float v0Ref = K.C_SPEED * MathK.Pow(m, K.EXP_SPEED) * dt;   // a step's travel at top speed of a plain body of this mass
        float perMps = v0Ref / SprintMps(m);
        float vLand = c.VLand * perMps, vWater = c.VWater * perMps;
        // flight: its flight-muscle power over the least power of level flight (Physics.FlightMargin), and enough flight
        // muscle to lift its weight at all (Marden 1987)
        bool flies = c.Flies > 0f;
        float vAir = flies ? MathF.Min(K.FLY_V_CAP * dt, c.VAir * perMps) : 0f;
        // swimming ability: its fastest sustained swimming speed in body lengths a second, read on the live scale where a
        // propulsor of 0..1 cruises at CRUISE_BL x (0.1 + 0.9 propulsor) lengths a second (Respiration.CruiseMps): the
        // gates that read it (deep water, how deep it goes, paddling) keep their meaning
        float prop = Math.Clamp((lc.VSust / MathF.Max(K.CRUISE_BL * lc.Length, 1e-6f) - 0.1f) / 0.9f, 0f, 1f);
        // legs: what its limbs do on land, read on the live scale where legs 0..1 stand a body at STAND_H M^(1/3) (0.3 + 0.7
        // legs): the share of its weight they bear x how far they raise it
        float legs = Math.Clamp((c.Stand / (K.STAND_H * MathK.Cbrt(m)) - 0.3f) / 0.7f, 0f, 1f) * lc.Support;
        bool deep = 0.1f + 0.9f * prop >= K.DEEP_SWIM;   // swims well enough to stay up in open water
        bool settled = vLand <= 0f && vWater <= 0f && !flies;
        if (settled) { vLand = 0; vWater = 0; }
        w.PVLand[i] = vLand; w.PVWater[i] = vWater; w.PVAir[i] = vAir;
        Realm realm = flies ? Realm.Any : Realm.Land | Realm.Shallow | (deep ? Realm.Deep : 0);
        Loco loco = settled ? Loco.Sessile : flies ? Loco.Fly : vWater > vLand ? (legs >= K.LIMB_MIN ? Loco.Wade : Loco.Swim) : (prop >= K.LIMB_MIN ? Loco.Wade : Loco.Walk);
        if (settled) realm = prop > legs ? Realm.Water : Realm.Land;
        // a flier eats where it can land: water-column food only if it also wades or swims (2026-10-31)
        Realm reach = flies ? GroundRealm(legs, prop) : realm;
        w.PLoco[i] = (byte)loco; w.PRealm[i] = (byte)realm;
        w.PVmax[i] = MathF.Max(vAir, MathF.Max(vLand, vWater));
        // turning: the turn rate of the plain body of its mass (C_TURN M^EXP_TURN) x the plain body's least turning radius
        // over its own (Physics.TurnRadius: its mobile surfaces and bending trunk against its inertia)
        w.PTurn[i] = K.C_TURN * MathK.Pow(m, K.EXP_TURN) * Morph.Live.RefTurnR(m) / MathF.Max(c.TurnR, 1e-6f);
        // reach in a step (2026-11-01): its body and the distance it sprints in a chase (ReachM)
        w.PLen[i] = lc.Length;
        w.PReach[i] = ReachM(m, w.PVmax[i] / v0Ref, w.PLen[i]) / K.M_PER_UNIT;
        // (2026-11-02) a chase runs at the speed of the medium it is in (World.ReachOf)
        w.PReachLand[i] = ReachM(m, vLand / v0Ref, w.PLen[i]) / K.M_PER_UNIT;
        w.PReachSwim[i] = ReachM(m, vWater / v0Ref, w.PLen[i]) / K.M_PER_UNIT;
        // protection: how much its integument cuts the chance that a bite scaled to its size kills it (Physics.PKill), against
        // the plain body's: PKill_ref / PKill = (1 + odds) / (1 + odds_ref), as armour on the live scale where defence is
        // mass x (DEF_BASE + armour), so the live strike's kill chance ~ power / (power + defence) falls as PKill does. A
        // probability ratio: it saturates as the plate makes the bite fail every time. (Phase 4, 2026-11-03, the armour check:
        // it was the ratio of the odds, odds / odds_ref, and the plain body's skin crushes at 0.05 of a bite, so that ratio
        // divided by a near-zero and gave a half-mineralised 1 kg body ~40x where its kill chance falls ~7x)
        float gain = (1f + c.Odds) / (1f + Morph.Live.RefOdds);
        w.PArmour[i] = K.DEF_BASE * MathF.Max(0f, gain - 1f);
        // the rest of what the live sim reads of a body, from the physics (per unit of distance and per step at adult mass)
        float ePerUnitPerJm = K.M_PER_UNIT / Morph.Shadow.J_PER_E;   // energy units per world unit, per J/m
        w.PProp[i] = prop; w.PMargin[i] = c.Margin; w.PStand[i] = c.Stand; w.PCruise[i] = c.Cruise;
        w.PCotLand[i] = c.CotLand * ePerUnitPerJm; w.PCotLandTop[i] = lc.CotLandTop * ePerUnitPerJm; w.PCotWater[i] = c.CotWater * ePerUnitPerJm;
        float basalW = MathF.Max(c.Basal, 1e-9f);
        w.PScopeAir[i] = c.O2Air / basalW; w.PScopeLung[i] = lc.O2Lung / basalW; w.PScopeWater[i] = c.O2Water / basalW;
        w.PMoultW[i] = lc.MoultW / toW * dt; w.PMoultSoft[i] = lc.MoultSoft;
        w.PCrush[i] = lc.Crush; w.PDensity[i] = lc.Density;
        w.PSegN[i] = (byte)Math.Min(255, mo.SegmentCount); w.PPairN[i] = (byte)Math.Min(255, mo.PairCount);
        // heat and water from the grown body's surface (docs/BODYPLAN.md §13.3): its whole surface (segments and pairs) over
        // a cube's of its volume, the share of it on its appendages (Allen's rule: radiators), and its evaporation through its
        // exposed exchange surface and skin, less through a thick integument (cuticle and wax), against a plain body's
        w.PSurfK[i] = lc.SurfK; w.PAppSurf[i] = lc.AppSurf; w.PAppend[i] = lc.AppSurf;
        w.PEvapK[i] = lc.EvapK;
        w.PThirstK[i] = MathK.Pow(m, K.EXP_THIRST) * lc.EvapK * (1f - K.WATER_ECON * wecon);
        // the physics' equivalents of the retired organ genes, for the renderer, telemetry and the gates that read them:
        // legs and swimming ability as above; wings, its flight muscle over a full-winged body's (FLY_MUSCLE_MAX of its mass);
        // lungs, the share of its aerobic scope in air that its air-breathing surfaces give, against its scope in water;
        // leg pairs, its limbs on the ground; fin pairs, its flat pairs that are not legs
        w.PLegs[i] = legs; w.PFins[i] = prop;
        w.PWings[i] = Math.Clamp(lc.FlyMuscle / (K.FLY_MUSCLE_MAX * m), 0f, 1f);
        w.PLungs[i] = w.PScopeLung[i] + w.PScopeWater[i] > 0f ? w.PScopeLung[i] / (w.PScopeLung[i] + w.PScopeWater[i]) : 0.5f;
        int legPairs = Math.Clamp((int)MathF.Round(lc.NLegs * 0.5f), 1, 4), finPairs = 0;
        foreach (var a in mo.Pairs) if (a.Chord >= 3f * a.Thick && a.Theta < MathF.PI / 4f) finPairs++;
        w.PLegPairs[i] = (byte)legPairs; w.PFinPairs[i] = (byte)Math.Clamp(finPairs, 1, 4);
        MorphVisible(mo, new Span<float>(w.PMorphVis, i * NMVIS, NMVIS));
        {
            // its proportions as the retired shape genes' bytes (Dimensions inverted: each axis exp(ln SHAPE_RANGE (2 g - 1)) of
            // the cube of its volume), so the renderer draws the grown body's length, width and height
            float side = MathK.Cbrt(MathF.Max(mo.Volume, 1e-12f)), lr = MathK.Log(K.SHAPE_RANGE);
            float wMean = 0f, hMean = 0f; foreach (var sg in mo.Segs) { wMean += sg.W * sg.L; hMean += sg.H * sg.L; }
            wMean /= MathF.Max(mo.Length, 1e-9f); hMean /= MathF.Max(mo.Length, 1e-9f);
            static sbyte Shape(float share, float lr) => (sbyte)Math.Clamp((int)MathF.Round(((MathK.Log(MathF.Max(share, 1e-6f)) / lr + 1f) * 0.5f) * 254f - 127f), -127, 127);
            w.PShapeG[i * 3] = Shape(mo.Length / side, lr); w.PShapeG[i * 3 + 1] = Shape(wMean / side, lr); w.PShapeG[i * 3 + 2] = Shape(hMean / side, lr);
        }
        Signature(w, i);   // its look and MHC diversity, cached for DECIDE and LIFECYCLE (see Signature), from the body above

        w.PStrike[i] = strike;
        w.PLitter[i] = (byte)Math.Clamp((int)MathF.Round(MathK.Exp(Organ(gn[G.B_LITTER]) * MathK.Log(K.LITTER_MAX))), 1, K.LITTER_MAX);
        float birth = G.GeneLog(gn[G.B_BIRTH], K.BIRTH_LO, K.BIRTH_HI);
        birth = MathF.Min(K.BIRTH_HI, MathF.Max(birth, BodySize.NewbornMin / m));   // eggs and young have a smallest size (Revision M; a tenth of the world's smallest body, BodySize)
        w.PBirth[i] = birth;
        w.PSexual[i] = (byte)(gn[G.B_SEX] >= 0 ? 1 : 0);
        w.PLive[i] = (byte)(gn[G.B_LIVE] >= 0 ? 1 : 0);
        // gestation (or incubation) grows with body size and with how big the young are at birth
        w.PGest[i] = K.C_GEST * t * MathF.Sqrt(birth / 0.1f) / dt;
        w.PCool[i] = K.C_COOL * t / dt;
        w.PCare[i] = Organ(gn[G.B_CARE]);
        ExpressDiet(w, i, reach, new Span<float>(w.PDiet, i * Pools.N, Pools.N));
        // a meat-eater can feed on a carcass, its own kill's included (Revision L, ecology review: wolves
        // return to a kill for days, and almost every carnivore scavenges)
        { int d = i * Pools.N; w.PDiet[d + (int)Pool.Carrion] = MathF.Max(w.PDiet[d + (int)Pool.Carrion], K.SCAVENGE * w.PDiet[d + (int)Pool.Meat]); }
        // a blood specialist with weapons drains its victims instead of killing them (a parasite habit)
        float blood = w.PDiet[i * Pools.N + (int)Pool.Blood], meat = w.PDiet[i * Pools.N + (int)Pool.Meat];
        // drain or kill is a matter of degree (ecology review): the share of successful strikes that drain blood
        w.PDrain[i] = blood + meat > 1e-4f ? blood / (blood + meat) : 0f;
        w.PParasite[i] = (byte)(w.PDrain[i] >= 0.5f ? 1 : 0);   // a label (and whom it targets): mostly a blood-drinker
        // the shadow comparison (docs/BODYPLAN.md §10-12) is retired with the switch: what it compared against is gone
    }

    /// <summary>Visible traits of a developed body for clade splitting (Clades.Vis), NMVIS of them, each in visible units
    /// (1 = a difference you notice at a glance): segments (half again as many a unit), pairs (one a unit), fineness and
    /// height over width (x1.5 a unit), its longest pair's length over its body length (0.15 a unit), mean pair droop (15°
    /// a unit), mean flatness (x1.5 a unit), integument rigidity (0.15 a unit), exchange surface over body surface (x1.5 a
    /// unit, from 1%), mean joints per pair (one a unit).</summary>
    public const int NMVIS = 10;
    public static readonly string[] MorphVisName = { "segments", "appendage pairs", "fineness", "body depth", "limb length", "limb angle", "limb flatness", "armour", "gills or lungs", "joints" };
    public static void MorphVisible(Morph.Morphology m, Span<float> v)
    {
        float l15 = 1f / MathK.Log(1.5f);
        v[0] = MathK.Log(MathF.Max(1, m.SegmentCount)) * l15;
        v[1] = m.PairCount;
        v[2] = MathK.Log(MathF.Max(m.Fineness, 1e-3f)) * l15;
        float hw = 0f; foreach (var s in m.Segs) hw = MathF.Max(hw, s.H / MathF.Max(s.W, 1e-6f));
        v[3] = MathK.Log(MathF.Max(hw, 1e-3f)) * l15;
        float longest = 0f, droop = 0f, flat = 0f, joints = 0f;
        foreach (var a in m.Pairs) { longest = MathF.Max(longest, a.Len); droop += a.Theta; flat += MathK.Log(a.Chord / MathF.Max(a.Thick, 1e-9f)); joints += a.Joints; }
        int np = Math.Max(1, m.PairCount);
        v[4] = longest / MathF.Max(m.Length, 1e-6f) / 0.15f;
        v[5] = droop / np * (180f / MathF.PI) / 15f;
        v[6] = flat / np * l15;
        v[7] = m.Rigidity / 0.15f;
        v[8] = MathK.Log(MathF.Max(0.01f, (m.ExchExt + m.ExchInt + m.ExchGas) / MathF.Max(m.TotalArea, 1e-9f)) / 0.01f) * l15;
        v[9] = joints / np;
    }

    /// <summary>Body length in metres (2026-11-01): the side of a cube of its volume at the density of water, BODY_L_M x
    /// M^(1/3) (M in kg), times its length gene's share of that cube (Dimensions: length x width x height = 1).</summary>
    public static float LengthM(float m, float lengthShare) => K.BODY_L_M * MathK.Cbrt(m) * lengthShare;

    /// <summary>Body length in metres of organism i at its present mass (a juvenile is smaller), for the console.</summary>
    public static float LengthM(World w, int i) => w.PLen[i] * MathK.Cbrt(MathF.Max(1e-6f, w.MassOf(i) / w.PM[i]));

    /// <summary>Top running speed in m/s at mass m kg (Garland 1983): log10 V(km/h) = log10 GARLAND_V1 + GARLAND_B log10 M +
    /// GARLAND_C (log10 M)^2. 4.0 m/s at 0.1 kg, 8.4 at 1 kg, 15 at 40 kg.</summary>
    public static float SprintMps(float m)
    {
        const float ln10 = 2.302585093f;
        float lg = MathK.Log(m) / ln10;
        return K.GARLAND_V1 / 3.6f * MathK.Exp(ln10 * (K.GARLAND_B * lg + K.GARLAND_C * lg * lg));
    }

    /// <summary>
    /// How far an animal reaches in a step, in metres (2026-11-01): its own body length and the distance it sprints in a
    /// chase, T_CHASE at Garland's top speed for its mass, times its own top speed against a plain body's (speed ratio,
    /// at most 2: speed genes, legs, wings). A step is hours, so a strike is not a bite at arm's length but the last
    /// rush of a stalk; most chases last 10-30 s (Elliott, Cowan &amp; Holling 1977). 60 m at 0.1 kg, 125 m at 1 kg, 225 m
    /// at 40 kg for plain bodies (was 1 + 0.4 M^(1/3) units: 66-130 m, plus 0.5 + 0.4 M^(1/3) units of target).
    /// </summary>
    public static float ReachM(float m, float speedRatio, float lengthM)
        => lengthM + SprintMps(m) * K.T_CHASE * Math.Clamp(speedRatio, 0f, 2f);

    /// <summary>Hoerner's (1965) volume drag of a streamlined body of fineness ratio f: 4 f^(1/3) + 6 f^(-7/6) + 24 f^(-8/3).</summary>
    public static float VolumeDrag(float f)
    {
        f = MathF.Max(f, 0.05f);
        return 4f * MathK.Cbrt(f) + 6f * MathK.Pow(f, -7f / 6f) + 24f * MathK.Pow(f, -8f / 3f);
    }

    /// <summary>Speed at a given power against a cube's (drag^(-1/3)), for a body of length share lengthShare and width x
    /// height share wh (Dimensions: length x width x height = 1, so wh = 1 / lengthShare when the volume holds).</summary>
    public static float Streamlining(float lengthShare, float wh)
        => MathK.Cbrt(VolumeDrag(1f) / VolumeDrag(lengthShare / MathF.Sqrt(MathF.Max(wh, 1e-4f))));

    /// <summary>
    /// What propels a body under water, in fins' worth (2026-11-02): its fins, and its wings stroked as flippers, as auks,
    /// diving petrels and penguins swim (Johansson &amp; Aldrin 2002; Watanuki et al. 2006). Water is ~800x denser than air,
    /// so the wing area that best drives a body through it is far smaller than the area that carries it in the air: a
    /// wing works as a flipper best at half the wing gene (a penguin's flipper; flightless at most masses, FlightMargin)
    /// and worse as it grows toward a flier's, which a diver must half fold (murres and diving petrels; the flight-versus-
    /// dive trade-off, Thaxter et al. 2010; Elliott et al. 2013): WING_SWIM x 4 w (1 - w). At most 1.
    /// </summary>
    public static float Propulsor(float fins, float wings)
        => MathF.Min(1f, fins + K.WING_SWIM * 4f * wings * (1f - wings));

    /// <summary>Where a flier can stand, perch, feed or rest once landed: land; shallow water if it wades on legs or
    /// swims; open water if it swims well enough to stay up (DEEP_SWIM).</summary>
    /// <param name="fins">its propulsor under water, fins and wings (Propulsor)</param>
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
        float walkRef = K.C_MOVE * MathK.Pow(K.FLY_M_REF, K.EXP_MOVE);   // per unit distance, two pairs of full legs at full speed
        return netRef / (K.FLY_COT_RATIO * walkRef) * MathK.Pow(m / K.FLY_M_REF, 1f / 6f);
    }

    /// <summary>The least power of flight at mass m (2026-10-31): FLIGHT_MULT x basal at FLY_M_REF, rising as M^(7/6).</summary>
    public static float FlightPowerMin(float m)
        => K.FLIGHT_MULT * K.C_BASAL * MathK.Pow(K.FLY_M_REF, K.EXP_BASAL) * MathK.Pow(m / K.FLY_M_REF, K.EXP_FLY_REQ);

    /// <summary>
    /// Speed kept on land under the load its bones carry (2026-11-01, replaces LOAD_DRAG and its 0.5 floor), against an
    /// unarmoured two-pair body of 1 kg. Peak bone stress is weight over bone section over the limb's effective mechanical
    /// advantage (EMA): weight ~M and section ~M^(2/3) give M^(1/3) at a fixed posture, but bigger mammals stand straighter,
    /// EMA ~M^0.26, which holds peak stress nearly constant up to ~300 kg (Biewener 1989, 1990); a straight leg cannot
    /// straighten further, so above that stress rises as M^(1/3) and giants move carefully (elephants do not gallop:
    /// Hutchinson et al. 2003). The weight is shared by the legs, each with its own bone, so stress ~ (1 + armour) x
    /// weight / (leg pairs x section). Speed is then limited twice over: by the muscles (the allometry, v0) and by the
    /// bones, whose peak force rises with speed while the safety factor is kept at ~2-4 (Alexander 1977; Biewener 1990),
    /// so the bone-limited speed is ~ allowed stress / static stress. Two limits on one speed add as 1/v = 1/v0 + 1/v_bone:
    /// v = v0 (1 + B) / (1 + B s), with s the stress against the reference and B = BONE_LOAD the reference's v0 / v_bone.
    /// Positive and finite for any mass, armour and leg count; a body with stress far above the limit walks at
    /// ~ v0 (1 + B) / (B s): it is held to a slow walk, never to zero.
    /// </summary>
    public static float LandLoadFactor(float m, float armour, int legPairs)
    {
        float s = BoneStress(m) / BoneStress(1f) * (1f + armour) * K.LEG_PAIRS_REF / legPairs;
        return (1f + K.BONE_LOAD) / (1f + K.BONE_LOAD * s);
    }

    /// <summary>Peak bone stress of an unarmoured body of mass m on its legs, in arbitrary units: M^(1/3) / EMA(M), the
    /// posture (EMA) straightening as M^EXP_EMA up to POSTURE_M and fully straight above it, joined smoothly
    /// (EMA = (1 + (POSTURE_M / M)^(EXP_EMA k))^(-1/k), k = POSTURE_K) (Biewener 1989, 1990).</summary>
    public static float BoneStress(float m)
    {
        float k = K.POSTURE_K;
        return MathK.Cbrt(m) * MathK.Pow(1f + MathK.Pow(K.POSTURE_M / m, K.EXP_EMA * k), 1f / k);
    }

    /// <summary>
    /// Land speed kept on flat ground by a body with wing gene w, flier or not (2026-11-01): its flight muscle is
    /// FLY_MUSCLE_MAX x w of its mass, tissue that is not leg, so leg power per unit weight, and the speed, fall by that
    /// share (flight and leg muscle trade off in the avian body plan, Heers &amp; Dial 2015; strong fliers walk badly:
    /// swifts, bats). Flapping does not help on the flat; on a slope it does (WingInclineGain).
    /// </summary>
    public static float WingGroundFactor(float wings) => 1f - K.FLY_MUSCLE_MAX * wings;

    /// <summary>
    /// Speed gained on a slope by flapping while running, with wing gene w on ground whose steepest slope has sine
    /// sinSlope (2026-11-01): 1 + WAIR_GAIN x w x sin(slope). The wings' aerodynamic force presses the feet onto the
    /// incline and adds to the legs' push against the slope's share of gravity, so partly feathered young birds run up
    /// slopes they could not climb on their legs alone, and the gain grows with the slope (wing-assisted incline running,
    /// Dial 2003; Tobalske &amp; Dial 2007); going down, flapping controls the descent (Dial, Jackson &amp; Segre 2008). On
    /// flat ground it is 1. This, not flat-ground running, is the physical way up to flight.
    /// </summary>
    public static float WingInclineGain(float wings, float sinSlope) => 1f + K.WAIR_GAIN * wings * sinSlope;

    /// <summary>The most any wing gene multiplies a land speed by, on the steepest ground (sine 1): the largest of
    /// WingGroundFactor x WingInclineGain over w in 0..1 (for bounds on travel, BodySize.WalkerPerceive).</summary>
    public static float WingGroundGainMax
    {
        get
        {
            float wStar = Math.Clamp((K.WAIR_GAIN - K.FLY_MUSCLE_MAX) / (2f * K.WAIR_GAIN * K.FLY_MUSCLE_MAX), 0f, 1f);
            return WingGroundFactor(wStar) * WingInclineGain(wStar, 1f);
        }
    }

    /// <summary>
    /// A juvenile's flight muscle as a share of its body, against an adult's of the same wing gene (2026-11-01): flight
    /// muscle grows with positive allometry and matures last, so young birds fledge only near adult size (Ricklefs 1979;
    /// Choi, Ricklefs &amp; Shea 1993): share ~ (mass / adult mass)^FLY_MUSCLE_ALLO. An adult's is 1.
    /// </summary>
    public static float FlightMuscleGrown(float mass, float pm) => MathK.Pow(MathF.Min(1f, mass / pm), K.FLY_MUSCLE_ALLO);

    /// <summary>
    /// Mass a flier must lift besides its body (2026-11-01): the food in its gut and the young it carries inside it (a
    /// live-bearer's litter, at half its birth mass on average over the gestation). Wing loading rises with it
    /// (Pennycuick 2008; Marden 1987); birds lay eggs rather than carry their young, bats carry a single pup.
    /// </summary>
    public static float FlightLoad(World w, int i)
    {
        float load = MathF.Max(0f, w.Gut[i]);
        if ((w.Flags[i] & Flag.Carrying) != 0) load += 0.5f * w.PLitter[i] * w.PBirth[i] * w.PM[i];
        return load;
    }

    /// <summary>
    /// Can flier i take off now (2026-11-01)? Flight muscle at a core temperature below T_BODY delivers less power: maximal
    /// muscle power has a Q10 of ~2-3 (Bennett 1984, 1985; Josephson 1981), as the body's chemistry here (Heat.Rate, Q10
    /// ~2.3 at E_ACT): insects and birds warm their flight muscle before they take off, and a cold ectotherm cannot fly
    /// until it is warm (Heinrich 1974, 1993). A load lowers the share of its mass that is flight muscle, which must be
    /// FLY_WING_MIN of the body's (Marden 1987: lift per unit flight muscle is nearly constant), and raises the least power
    /// of level flight as (total / body)^(3/2) at fixed wings (induced power ~W^2 / V, V_mp ~W^(1/2); Pennycuick 1975, 2008).
    /// Once in the air its own muscle's heat keeps it warm (Heinrich 1993), so the test is for taking off. A juvenile's
    /// flight muscle is a smaller share of its body (FlightMuscleGrown): it fledges when its own margin is met. A margin of
    /// at least 1 at the speed of least power is a positive rate of climb, (P_available - P_min) / (m g) (Pennycuick 2008).
    /// </summary>
    public static bool CanTakeOff(float wings, float pm, float mass, float load, float rateT)
    {
        float body = mass / (mass + load);
        return wings * FlightMuscleGrown(mass, pm) * rateT * body >= K.FLY_WING_MIN && LoadedMargin(wings, pm, mass, load, rateT) >= 1f;
    }

    /// <summary>Power margin of flight now (2026-11-01): its grown flight muscle's power at its muscle temperature over the
    /// least power of level flight with its load, FlightMargin(wings x grown, M) x rate x (body / (body + load))^(3/2)
    /// (Pennycuick 1975, 2008). For an adult with an empty gut and a warm core it is FlightMargin(wings, M).</summary>
    public static float LoadedMargin(float wings, float pm, float mass, float load, float rateT)
        => FlightMargin(wings * FlightMuscleGrown(mass, pm), mass) * rateT * MathK.Pow(mass / (mass + load), 1.5f);

    /// <summary>Phase 4 (docs/BODYPLAN.md §13.3): the loaded margin of a body whose adult margin margin0 comes from its grown
    /// morphology (Physics.FlightMargin, World.PMargin): its grown flight muscle's share (FlightMuscleGrown), its smaller size
    /// ((pm / mass)^(EXP_FLY_REQ - EXP_FLY_AVAIL): power required falls faster with size than power available), its muscle
    /// temperature and its load, as LoadedMargin.</summary>
    public static float LoadedMarginOf(float margin0, float pm, float mass, float load, float rateT)
        => margin0 * FlightMuscleGrown(mass, pm) * MathK.Pow(pm / MathF.Max(mass, 1e-6f), K.EXP_FLY_REQ - K.EXP_FLY_AVAIL) * rateT * MathK.Pow(mass / (mass + load), 1.5f);

    /// <summary>CanTakeOff for a grown morphology (phase 4): wings is its flight muscle over a full-winged body's (World.PWings,
    /// from the physics), margin0 its adult flight margin (World.PMargin).</summary>
    public static bool CanTakeOffOf(float wings, float margin0, float pm, float mass, float load, float rateT)
    {
        float body = mass / (mass + load);
        return wings * FlightMuscleGrown(mass, pm) * rateT * body >= K.FLY_WING_MIN && LoadedMarginOf(margin0, pm, mass, load, rateT) >= 1f;
    }

    /// <summary>
    /// Top air speed with a load (2026-11-01): the speed of least power rises as the square root of the weight (vmp, the
    /// unloaded body's, given), and the fastest level flight is where the power curve meets the loaded margin
    /// (TopFlightSpeed): a load lowers the top speed wherever power, not FLY_X_MAX, binds, and a margin at or below 1
    /// leaves it only the speed of least power, sinking (Pennycuick 1975, 2008). The maximum-range speed, 3^(1/4) = 1.32 x
    /// vmp for this power curve (minimum of power / speed), needs a loaded margin of 1.14. At most FLY_V_CAP.
    /// </summary>
    public static float LoadedTopAirSpeed(float vmp, float mass, float load, float loadedMargin, float dt = 1f)
        => MathF.Min(K.FLY_V_CAP * dt, vmp * MathK.Pow((mass + load) / mass, 0.5f) * TopFlightSpeed(loadedMargin));

    static void ExpressDiet(World w, int i, Realm realm, Span<float> dst)
    {
        var gn = w.Genome.Slot(i);
        float sum = 0;
        for (int p = 0; p < Pools.N; p++)
        {
            // linear map, L2 budget (Revision I): every gene step counts the same, so a pool can be selected up from zero,
            // and a small side share costs a specialist almost nothing, which is how omnivory starts. An even 6-way
            // generalist digests each food at ~0.41 against a specialist's 0.95. (A linear map with an L1 budget starved:
            // every ancestor became an even generalist at 1/6 per food.)
            float raw = Reachable(realm, p) ? Organ(gn[G.B_DIET + p]) : 0f;
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
        var ga = w.Genome.Slot(a).Slice(G.BODY); var gb = w.Genome.Slot(b).Slice(G.BODY); float s = 0;
        const int N = G.APPEARANCE - G.BODY;
        for (int k = 0; k < N; k++) { float d = (ga[k] - gb[k]) * (1f / 127f); s += d * d; }
        float body = s / N;
        // phase 3 (docs/BODYPLAN.md §13.2): and the module body genome, by innovation number (Morph.Codec.Distance: matched
        // modules by their parameters, unmatched ones as unrelated genes), on the same per-gene RMS scale, weighted equally
        // with the other body genes. Genomes without a block (hand-made) compare by their body genes alone
        var fa = w.Genome.Slot(a); var fb = w.Genome.Slot(b);
        if (!Morph.Codec.Has(fa) || !Morph.Codec.Has(fb)) return MathF.Sqrt(body);
        float dm = Morph.Codec.Distance(fa, fb);
        return MathF.Sqrt(0.5f * body + 0.5f * dm * dm);
    }

    static readonly int[] Visible = { G.B_MASS, G.B_LEGS, G.B_FINS, G.B_WINGS, G.B_ARMOUR, G.B_STRIKE, G.B_LEGN, G.B_FINN };

    /// <summary>Does b look like a's kind? Ecological traits and appearance each pass their own RMS test,
    /// so new cosmetic genes cannot dilute an existing visible ecological difference.</summary>
    /// Phase 4: what it looks like is its grown body (Signature), so this compares the cached signatures.
    public static bool LooksAlike(World w, int a, int b) => LooksAlikeCached(w, a, b);

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
        int o = i * NLOOK;
        var gn = w.Genome.Slot(i); var s = w.D.Look;
        // phase 4 (docs/BODYPLAN.md §13.5): the visible body is the grown one. Its organs as the physics' equivalents of the
        // retired genes (Express), on those genes' byte scales: legs, fins (swimming), wings (flight muscle), armour, leg and
        // fin pairs; its shape (the first 10 appearance bytes, once the shape genes) its visible morphology (MorphVisible),
        // 19 bytes (0.15 of a gene's range) a visible unit. Mass, weapons and pigment stay genes
        static sbyte Org(float v) => (sbyte)Math.Clamp((int)MathF.Round(v * 254f - 127f), -127, 127);
        static sbyte PairsB(int n) => Org((n - 1) / 4f + 0.125f);
        s[o + 0] = gn[G.B_MASS]; s[o + 1] = Org(w.PLegs[i]); s[o + 2] = Org(w.PFins[i]); s[o + 3] = Org(w.PWings[i]);
        s[o + 4] = Org(w.PArmour[i] / K.ARMOUR_MAX); s[o + 5] = gn[G.B_STRIKE]; s[o + 6] = PairsB(w.PLegPairs[i]); s[o + 7] = PairsB(w.PFinPairs[i]);
        for (int k = 0; k < G.NAPPEARANCE; k++) s[o + NVISIBLE + k] = gn[G.APPEARANCE + k];
        for (int k = 0; k < NMVIS && k < G.B_HUE - G.APPEARANCE; k++)
            s[o + NVISIBLE + k] = (sbyte)Math.Clamp((int)MathF.Round(w.PMorphVis[i * NMVIS + k] * 19f), -127, 127);
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
