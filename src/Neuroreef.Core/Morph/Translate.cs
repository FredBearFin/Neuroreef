// Translator: today's fixed body genes as a module genome (docs/BODYPLAN.md §9, phase 2: shadow expression).
// Every live genome (an ancestor, a gene-bank clade, a saved animal) is expressed both ways: by Body.Express from its
// labelled genes, and by Development.Grow and Physics from the module genome this builds. Nothing here is fitted to the
// live capabilities except where docs/BODYPLAN.md §10 says so; each mapping is a physical reading of what the label
// stands for:
//   shape (B_LENGTH, B_WIDTH, B_HEIGHT)   the body's axes (Body.Dimensions): a head, 1-4 trunk segments and a tail of
//                                         the same section, so the fineness is the live body's and there are no steps
//   B_HEAD, B_TAIL                        the head's and tail's share of the length
//   legs x B_LEGN, B_LIMB_SHAPE            B_LEGN stiff support pairs (droop 70°, 3 joints, round, bone: mineral 1,
//                                         endoskeletal), length and muscle from the leg gene
//   fins x B_FINN, B_FIN_SHAPE             B_FINN flat lateral pairs (droop 0°, flatness 8, aspect ratio from the shape
//                                         gene, muscle from the fin gene), plus a sagittal tail blade; the trunk's
//                                         axial muscle grows with the fin gene (a fish's myotomes are its propulsor)
//   wings, B_WING_SHAPE                   one long, flat, high-aspect-ratio lateral pair on an enlarged thorax holding
//                                         the flight muscle
//   armour                                integument thickness and mineral on every segment
//   B_LUNGS                               exchange surface sized to the oxygen the live organs supply (LUNG_SCOPE,
//                                         GILL_SCOPE x the organ base): lungs folded in, gills everted
//   B_MASS, B_ENDO, B_INSUL, B_APPEND, diet, life history, brain, senses, appearance: kept as genes (they are physiology
//                                         or behaviour, not structure); the shadow reads B_ENDO for the metabolic level
// All maths through MathK.

using System;

namespace Neuroreef.Morph;

/// <summary>The live body genes the translator reads, decoded as Body.Express decodes them.</summary>
public struct LiveGenes
{
    public float Mass, Legs, Fins, Wings, Armour, Lungs, Insul, Endo;
    public int LegPairs, FinPairs;
    public float LengthShare, WidthShare, HeightShare, Head, Tail, LimbShape, FinShape, WingShape;

    public static LiveGenes Of(ReadOnlySpan<sbyte> gn, float mass)
    {
        var l = new LiveGenes
        {
            Mass = mass,
            Legs = Body.Organ(gn[G.B_LEGS]), Fins = Body.Organ(gn[G.B_FINS]), Wings = Body.Organ(gn[G.B_WINGS]),
            Armour = Body.Organ(gn[G.B_ARMOUR]) * K.ARMOUR_MAX, Lungs = Body.Organ(gn[G.B_LUNGS]),
            Insul = Body.Organ(gn[G.B_INSUL]), Endo = Body.Organ(gn[G.B_ENDO]),
            LegPairs = Body.Pairs(gn[G.B_LEGN]), FinPairs = Body.Pairs(gn[G.B_FINN]),
            Head = Body.Organ(gn[G.B_HEAD]), Tail = Body.Organ(gn[G.B_TAIL]),
            LimbShape = Body.Organ(gn[G.B_LIMB_SHAPE]), FinShape = Body.Organ(gn[G.B_FIN_SHAPE]), WingShape = Body.Organ(gn[G.B_WING_SHAPE]),
        };
        Body.Dimensions(gn[G.B_LENGTH], gn[G.B_WIDTH], gn[G.B_HEIGHT], out l.LengthShare, out l.WidthShare, out l.HeightShare);
        return l;
    }

    /// <summary>The adult mass Body.Express gives these genes (its size class's floor, else the mass gene).</summary>
    public static float MassOf(ReadOnlySpan<sbyte> gn)
    {
        float floor = Body.Floors[Math.Clamp((int)gn[G.B_FLOOR], 0, K.FLOOR_CLASSES - 1)];
        return MathF.Max(floor, G.GeneLog(gn[G.B_MASS], BodySize.Min, BodySize.Max));
    }
}

public static class Translate
{
    // ---- the physical reading of each label (docs/BODYPLAN.md §10.1). Lengths are in cube sides of the live body
    // (its length, width and height genes multiply to one cube; Body.Dimensions), so they scale with it.
    /// <summary>Leg length per unit of leg gene, cube sides: the live standing height on full legs is STAND_H M^(1/3)
    /// (Garcia &amp; da Silva 2006) and its belly's 0.3 of that, so full legs add 0.7 x 2.5 = 1.75 cube sides of height
    /// (a cube side is BODY_L_M M^(1/3) = 0.1 m at 1 kg); at 70° droop that is a 1.86-side leg.</summary>
    public const float LEG_LEN = 1.86f;
    public const float LEG_DROOP = 70f / 90f;   // gene value: 70° below the horizontal, a sprawling-to-upright posture
    public const float LEG_ROUND = 0.08f;       // chord and thickness as a share of its length: a slender limb, its muscle housed in the trunk (cursorial mammals ~0.06-0.1; Alexander 1983)
    public const float LEG_WALL = 0.35f;        // bone share of the limb's section (Currey 2002: cortical bone 20-40%)
    /// <summary>Fin length per unit of fin gene, as a share of the body's length (pectoral fins ~0.15-0.25 of a fish's length).</summary>
    public const float FIN_LEN = 0.2f;
    public const float FIN_FLAT = 8f;           // chord / thickness of a fin (a ray-stiffened membrane)
    /// <summary>Tail blade height (each blade, up and down) per unit of fin gene, as a share of the body's height.</summary>
    public const float TAIL_BLADE = 0.8f;
    /// <summary>Wing length (one side) on full wings, cube sides: a 1 kg bird's span is ~1 m (Pennycuick 2008: span
    /// ~1.17 M^0.39 m), 10 cube sides, so each wing is ~4.5 sides beyond the body.</summary>
    public const float WING_LEN = 4.5f;
    /// <summary>A wing is a membrane or a feathered vane: its solid section is ~1/40 of its chord, so a full wing pair
    /// is ~10% of the body (bird wings 8-13% of body mass with feathers; Pennycuick 2008). At 12 the pair was a third of it.</summary>
    public const float WING_FLAT = 40f;
    /// <summary>Share of the trunk's length (and so its mass) in the wing-bearing thorax on full wings: a bird's flight
    /// muscle is 15-35% of its mass (FLY_MUSCLE_MAX), and a pair gene drives with at most 0.3 of its segment's tissue.</summary>
    public const float THORAX_WING = 0.85f;
    /// <summary>Integument thickness of an unarmoured body, as a share of its half-thickness: mammal and reptile skin
    /// ~1-2 mm on a ~0.1 m body (Schmidt-Nielsen 1984).</summary>
    public const float SKIN_REL = 0.02f;
    /// <summary>An organ gene below this grows no module at all (a vestige the live body carries at no size).</summary>
    public const float VESTIGE = 0.02f;
    /// <summary>Lung air on full lungs, as a share of the whole body's volume (~57 mL per kg; Schmidt-Nielsen 1984).</summary>
    public const float LUNG_AIR = 0.057f;
    /// <summary>Muscle gene of a leg or fin pair per unit of its organ gene: 0.3 of its segment's tissue (Development's
    /// PAIR_MUSCLE 0.6 x 0.5), the share phase 2a used; a wing pair's gene is the wing gene itself (0.6 of the thorax).</summary>
    public const float LEG_MUSCLE = 0.5f;

    // innovation numbers of the translated modules: the same role gets the same number in every translated genome, so
    // homologous modules line up for crossover and distance (Homology)
    public const uint IN_TRUNK0 = 1, IN_HEAD = 2, IN_TRUNK1 = 3, IN_TAIL = 7, IN_LEG = 10, IN_FIN = 20, IN_BLADE = 30, IN_WING = 40;

    static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
    static float Inv(float x, float lo, float hi) => x <= 0f ? 0f : Clamp01(MathK.Log(x / lo) / MathK.Log(hi / lo));

    /// <summary>
    /// The module genome of a live body. organBaseW: the metabolism its respiratory organs are built to (W; the live
    /// Respiration.OrganBase of the adult), which sizes its exchange surfaces as the live organs' scopes do; 0 leaves
    /// it to skin breathing. The mass gene is set from the live mass where the prototype's range holds it; grow it at the
    /// live mass with Development.Grow(g, mass).
    /// </summary>
    public static MorphGenome FromLive(in LiveGenes l, float organBaseW = 0f)
    {
        var g = Build(l, 0f, 0f, 0f);
        // the internal gas space: lung air (~57 mL per kg, ~6% of the volume: Schmidt-Nielsen 1984) x the lung gene; a
        // swimmer (the live propulsor) keeps itself neutrally buoyant, as teleosts regulate their swim bladders and
        // diving tetrapods their lungs and fat (Alexander 1966; Webb 1975), so its gas fills what makes it so
        var m0 = Development.Grow(g, l.Mass);
        float core = 0f; foreach (var s in m0.Segs) core += s.L * s.W * s.H * (MathF.PI / 4f);
        float neutral = MathF.Max(0f, l.Mass / Phys.RHO_W - m0.Volume) / MathF.Max(core, 1e-12f);
        float gas = MathF.Max(LUNG_AIR * l.Lungs * l.Mass / Mat.RHO_TISSUE / MathF.Max(core, 1e-12f), neutral * Body.Propulsor(l.Fins, l.Wings));   // lung air is a share of the whole body's volume
        float gasG = Clamp01(gas / Mat.GAS_MAX);
        if (organBaseW > 0f || gasG > 0f)
        {
            var m1 = Development.Grow(Build(l, 0f, 0f, gasG), l.Mass);
            // the exchange surfaces, sized as the live organs are (their scopes over the organ base): the lungs are the
            // lining of the gas space (the invagination share of it: Mat.SV_GAS per m³), the gills everted
            // surface (FOLD x pore share of the skin, at most 0.5 of it)
            float aLung = K.LUNG_SCOPE * l.Lungs * organBaseW / Phys.S_AIR_INT;
            float aGill = K.GILL_SCOPE * (1f - l.Lungs) * organBaseW / Phys.S_WATER_EXT;
            float invag = m1.GasV > 0f ? Clamp01(aLung / (m1.GasV * Mat.SV_GAS)) : 0f;
            float segArea = 0f; foreach (var s in m1.Segs) segArea += s.Area;
            float pore = aGill / (Mat.FOLD * MathF.Max(1f - invag, 0.05f) * MathF.Max(segArea, 1e-9f));   // Development: Pore = 0.5 x gene
            g = Build(l, Clamp01(2f * pore), invag, gasG);
        }
        g.MassG = Clamp01(MathK.Log(l.Mass / Development.MASS_LO) / MathK.Log(Development.MASS_HI / Development.MASS_LO));
        return g;
    }

    /// <summary>The live reference body of a mass: full legs on two pairs, no fins, wings or armour, a cube, the middle
    /// of every other gene (the body Body.Express's C_SPEED and C_TURN describe).</summary>
    public static LiveGenes Reference(float mass) => new()
    {
        Mass = mass, Legs = 1f, Fins = 0f, Wings = 0f, Armour = 0f, Lungs = 1f, Insul = 0f, Endo = 1f,
        LegPairs = 2, FinPairs = 1, LengthShare = 1f, WidthShare = 1f, HeightShare = 1f, Head = 0.5f, Tail = 0.5f,
        LimbShape = 0.5f, FinShape = 0.5f, WingShape = 0.5f,
    };

    static MorphGenome Build(in LiveGenes l, float poreGene, float invag, float gasGene)
    {
        // a winged body carries its legs behind the wing-bearing thorax (a pelvis), so the thorax's tissue is the flight muscle's
        bool winged = l.Wings > VESTIGE;
        int legAt = winged ? 1 : 0;
        int n = Math.Min(MorphGenome.MAX_SEG_GENES - 2, Math.Max(1, Math.Max(l.LegPairs + legAt, l.FinPairs)));
        float L = l.LengthShare, W = l.WidthShare, H = l.HeightShare;
        float hs = 0.10f + 0.10f * l.Head, ts = 0.08f + 0.12f * l.Tail, trunk = 1f - hs - ts;
        // the thorax: an even share of the trunk without wings, most of it with full wings (it houses the flight muscle)
        float thorax = n == 1 ? trunk : trunk * ((1f - l.Wings) / n + l.Wings * THORAX_WING);
        float rest = n == 1 ? 0f : (trunk - thorax) / (n - 1);

        // every length in cube sides; the raw scale c (m per cube side) is chosen so each lies inside its gene's range,
        // as far as it can (Development rescales the whole body to its mass afterwards, so c changes nothing else)
        float legLen = LEG_LEN * l.Legs;
        float legT = LEG_ROUND * legLen;
        float finLen = FIN_LEN * L * l.Fins;
        float finAR = 1f + 3f * l.FinShape;                 // 2 x length / chord
        float finC = 2f * finLen / finAR, finT = finC / FIN_FLAT;
        float bladeLen = TAIL_BLADE * H * l.Fins, bladeC = 0.6f * bladeLen, bladeT = bladeC / 10f;
        float wingLen = WING_LEN * (0.4f + 0.6f * l.Wings) * (l.Wings > VESTIGE ? 1f : 0f);
        float wingAR = 5f + 10f * l.WingShape;
        float wingC = 2f * wingLen / wingAR, wingT = wingC / WING_FLAT;
        Span<float> seg = stackalloc float[] { hs * L, thorax * L, n > 1 ? rest * L : thorax * L, ts * L, W, H };
        Span<float> app = stackalloc float[] { legLen, legT, finLen, finC, finT, bladeLen, bladeC, bladeT, wingLen, wingC, wingT };
        float cLo = 0f, cHi = float.PositiveInfinity;
        foreach (var v in seg) { cLo = MathF.Max(cLo, Development.SEG_L_LO / v); cHi = MathF.Min(cHi, Development.SEG_L_HI / v); }
        float cSeg = cLo <= cHi ? MathK.Sqrt(cLo * cHi) : cLo;
        // appendages: those of real size should fit too (vestigial ones, below 2% of a side, may clamp at their least)
        float aLo = 0f, aHi = float.PositiveInfinity;
        for (int k = 0; k < app.Length; k++)
        {
            float v = app[k]; if (v < 0.02f) continue;
            float lo = k is 0 or 2 or 5 or 8 ? Development.APP_L_LO : k is 1 or 4 or 7 or 10 ? Development.APP_T_LO : Development.APP_C_LO;
            float hi = k is 0 or 2 or 5 or 8 ? Development.APP_L_HI : k is 1 or 4 or 7 or 10 ? Development.APP_T_HI : Development.APP_C_HI;
            aLo = MathF.Max(aLo, lo / v); aHi = MathF.Min(aHi, hi / v);
        }
        float c = cSeg;
        float lo2 = MathF.Max(cLo, aLo), hi2 = MathF.Min(cHi, aHi);
        if (lo2 <= hi2) c = MathK.Sqrt(lo2 * hi2);

        float SL(float v) => Inv(v * c, Development.SEG_L_LO, Development.SEG_L_HI);
        float SW(float v) => Inv(v * c, Development.SEG_W_LO, Development.SEG_W_HI);
        float AL(float v) => Inv(v * c, Development.APP_L_LO, Development.APP_L_HI);
        float AC(float v) => Inv(v * c, Development.APP_C_LO, Development.APP_C_HI);
        float AT(float v) => Inv(v * c, Development.APP_T_LO, Development.APP_T_HI);

        // integument: thicker and mineralised with armour (a plate's crushing force ~ sigma t², PKill)
        float intRel = MathF.Min(Development.INT_REL_HI, SKIN_REL * (1f + l.Armour));
        float intG = Inv(intRel, Development.INT_REL_LO, Development.INT_REL_HI), intMin = Clamp01(l.Armour / K.ARMOUR_MAX);
        float axMus = Clamp01(0.25f + 0.75f * l.Fins);   // AxShare 0.1 (a walker's back) to 0.4 (a fish's myotomes)

        var g = new MorphGenome();
        SegGene Seg(uint innov, float len)
        {
            var s = new SegGene { Innov = innov };
            s.P[SP.LEN] = SL(len); s.P[SP.WID] = SW(W); s.P[SP.HEI] = SW(H);
            s.P[SP.TAPER] = 0.5f; s.P[SP.REPEAT] = 0f;
            s.P[SP.INT_T] = intG; s.P[SP.INT_MIN] = intMin; s.P[SP.PORE] = poreGene; s.P[SP.INVAG] = invag; s.P[SP.AXMUS] = axMus; s.P[SP.GAS] = gasGene;
            return s;
        }
        g.Segs.Add(Seg(IN_HEAD, hs * L));
        for (int k = 0; k < n; k++)
        {
            var s = Seg(k == 0 ? IN_TRUNK0 : IN_TRUNK1 + (uint)(k - 1), (k == 0 ? thorax : rest) * L);
            if (k >= legAt && k - legAt < l.LegPairs && l.Legs > VESTIGE)
            {
                var a = new AppGene { Innov = IN_LEG + (uint)(k - legAt) };
                a.P[AP.LEN] = AL(legLen); a.P[AP.CHORD] = AC(legT); a.P[AP.THICK] = AT(legT);
                a.P[AP.DROOP] = LEG_DROOP; a.P[AP.JOINTS] = 3.5f / (Development.MAX_JOINTS + 1);
                a.P[AP.MUSCLE] = LEG_MUSCLE * l.Legs; a.P[AP.MINERAL] = 1f; a.P[AP.WALL] = (LEG_WALL - 0.02f) / 0.98f; a.P[AP.EXO] = 0f;
                s.Apps.Add(a);
            }
            if (k < l.FinPairs && l.Fins > VESTIGE)
            {
                var a = new AppGene { Innov = IN_FIN + (uint)k };
                a.P[AP.LEN] = AL(finLen); a.P[AP.CHORD] = AC(finC); a.P[AP.THICK] = AT(finT);
                a.P[AP.DROOP] = 0f; a.P[AP.JOINTS] = 2.5f / (Development.MAX_JOINTS + 1);
                a.P[AP.MUSCLE] = LEG_MUSCLE * l.Fins; a.P[AP.MINERAL] = 0.2f; a.P[AP.WALL] = 0.3f; a.P[AP.EXO] = 0f;
                s.Apps.Add(a);
            }
            if (k == 0 && winged)
            {
                var a = new AppGene { Innov = IN_WING };
                a.P[AP.LEN] = AL(wingLen); a.P[AP.CHORD] = AC(wingC); a.P[AP.THICK] = AT(wingT);
                a.P[AP.DROOP] = 0f; a.P[AP.JOINTS] = 3.5f / (Development.MAX_JOINTS + 1);
                a.P[AP.MUSCLE] = l.Wings; a.P[AP.MINERAL] = 0.6f; a.P[AP.WALL] = 0.1f; a.P[AP.EXO] = 0.8f;
                s.Apps.Add(a);
            }
            g.Segs.Add(s);
        }
        var tail = Seg(IN_TAIL, ts * L);
        if (l.Fins > VESTIGE)
        {
            var a = new AppGene { Innov = IN_BLADE };
            a.P[AP.LEN] = AL(bladeLen); a.P[AP.CHORD] = AC(bladeC); a.P[AP.THICK] = AT(bladeT);
            a.P[AP.DROOP] = 0f; a.P[AP.JOINTS] = 2.5f / (Development.MAX_JOINTS + 1); a.P[AP.ROLL] = 1f;
            a.P[AP.MUSCLE] = 0f; a.P[AP.MINERAL] = 0.2f; a.P[AP.WALL] = 0.3f; a.P[AP.EXO] = 0f;
            tail.Apps.Add(a);
        }
        g.Segs.Add(tail);
        return g;
    }
}
