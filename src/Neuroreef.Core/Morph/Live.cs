// Live expression from the module physics (docs/BODYPLAN.md §13.3, phase 4). Body.Express grows every body from its
// module genome (Morph.Codec) at its live mass and takes each capability it sets from the physics of that morphology
// (Physics.cs), not from the labelled body genes: speeds and costs per medium, standing height, flight margin and flight
// muscle, gas exchange in air and in water, protection, moulting, turning, basal tissue cost, drag shape and buoyancy
// (the gas space and fat in the swimming budget), crushing force. This file computes them in SI units (Caps of
// Shadow.cs plus what the live sim reads besides) and the "equivalent" organ values the parts of the live sim that are
// not yet physical read (the renderer, telemetry, a few gates): each is a stated conversion of a physical quantity, never
// a labelled gene.
// All maths through MathK.

using System;
using System.Text;

namespace Neuroreef.Morph;

/// <summary>What the live simulation reads of a grown body, in SI units at its adult mass.</summary>
public struct LiveCaps
{
    public Caps C;                        // speeds, costs, margin, stand, O2, protection odds, basal, drag shape, turning
    public float VSust;                   // fastest sustainable swimming speed (aerobic muscle), m/s
    public float CotLandTop;              // net cost a metre walking at its top speed, J/m
    public float O2Lung;                  // aerobic metabolism its air-breathing surfaces support in air (the gas space's lining and folded-in pockets), W
    public float FlyMuscle;               // flight muscle (muscle of lifting pairs, by their lift share), kg
    public float Gut;                     // gut size against the reference gut of its mass (Body.GutScale)
    public float GutRoom;                 // how many reference guts its trunk has room for (GUT_TISSUE): the most it can build
    public float MoultW;                  // what moulting its rigid integument costs while it grows, W
    public float MoultSoft;               // share of its growing time spent soft (a rigid integument must be moulted to grow)
    public float Crush;                   // N: the crushing force it brings to an item at the mouth (Physics.Handle)
    public float Support, Clear, NLegs;   // share of its weight its limbs bear on land; how far they lift its belly (m); legs on the ground
    public float Length;                  // m
    public float Density;                 // kg/m³ (buoyancy)
    public float SurfK;                   // heat: its trunk's surface over the plain body's of its mass (x M^(2/3)), each segment through its integument
    public float AppSurf;                 // heat: its appendages' surface on the same scale (Allen's rule: radiators)
    public float EvapK;                   // water: its evaporation in air (exposed exchange surface, lungs, skin through its integument) over the plain body's
}

public static class Live
{
    /// <summary>Moult (docs/BODYPLAN.md §13.3): a fully rigid outer skeleton spends this share of its growing time soft
    /// (postmoult stages A-B: a few days of an intermoult of ~2-4 weeks in growing decapods; Drach 1939; Hartnoll 1982).
    /// x its rigidity. The prototype's 5% was a share of a whole life; here only growth moults.</summary>
    public const float MOULT_SOFT_GROW = 0.15f;
    /// <summary>Days between moults of a growing body (Hartnoll 1982: ~10-30 days in juvenile crabs).</summary>
    public const float MOULT_DAYS = 20f;
    /// <summary>Flight muscle's mitochondrial volume density over the whole body's mean (pigeon pectoralis ~0.2 against ~0.045, most of the body being tissue of 0.02-0.06; Mathieu-Costello
    /// 1993; Suarez 1992; Else &amp; Hulbert 1985): what its resting upkeep is, per kg, against the body's.</summary>
    public const float MITO_RATIO = 4.4f;
    /// <summary>The reference gut's tissue as a share of body mass (docs/BODYPLAN.md §13.17): the gastrointestinal tract is
    /// ~5-10% of body mass (sheep, cattle, pigs: Huntington &amp; Reynolds 1987; Cant, McBride &amp; Croom 1996, the sources of
    /// K.GUT_SHARE), decapods' digestive gland 3-8% (the hepatosomatic index). A gut is tissue in the trunk: it can be no
    /// bigger than the visceral tissue its segments hold (Morphology.GutMass, Mat.GUT_SHARE of their non-muscle tissue), so
    /// a body can build at most GutMass / (GUT_TISSUE M) reference guts. The plain body holds ~0.2 of its mass there, room
    /// for ~2.9 reference guts, above K.GUT_FLEX_MAX (2.5), so it is not bound; a body that is mostly appendage is. Before,
    /// the live gut was built to the body's basal whatever its trunk, so a 0.31 kg body with a 22 g trunk (3.4 g of it
    /// visceral tissue) digested like a plain body of its mass.</summary>
    public const float GUT_TISSUE = 0.07f;
    /// <summary>How rough the ground is that a body stands on (docs/BODYPLAN.md §13.17): slopes up to tan = 0.5 x this
    /// (Physics.Topple), the prototype's land (MorphEnv.Land: 0.5, ~14°).</summary>
    public const float GROUND_ROUGH = 0.5f;

    /// <summary>How many reference guts the trunk has room for: its visceral tissue (Morphology.GutMass) over a reference gut's
    /// (GUT_TISSUE of its mass).</summary>
    public static float GutRoom(Morphology m) => m.GutMass / MathF.Max(GUT_TISSUE * m.Mass, 1e-12f);

    /// <summary>Standing height (m), in the posture the body can hold on rough ground (docs/BODYPLAN.md §13.17). Upright it
    /// stands its limbs' clearance plus its trunk's height; but a centre of mass at height z over a base of half-width b tips
    /// past slopes steeper than atan(b / z) (Physics.Topple; McGhee &amp; Frank 1968; Ting, Blickhan &amp; Full 1994), and a body
    /// that tips lies on its side, as high as it is wide. So its standing height is the upright height where it stays upright
    /// and its width where it falls, weighted by the chance it tips (z and b mass-weighted over its segments; its feet's
    /// spread widens the base). Before, the standing height was the tallest segment's whatever its base, so a 9 kg body of
    /// 46 cm-tall, 2-4 cm-wide plates "stood" 47 cm in water (its head in the air, the bottom in reach) where it would lie
    /// 4 cm high.</summary>
    public static float StandPosture(Morphology m, in Physics.WalkProps wp)
    {
        float ms = 0f, zs = 0f, bs = 0f;
        foreach (var s in m.Segs) { float mm = s.TissueMass + s.FatMass + s.IntMass + s.EndoMass; ms += mm; zs += mm * 0.5f * s.H; bs += mm * 0.5f * s.W; }
        if (ms <= 0f) return wp.Clear + m.MaxH;
        float z = wp.Clear + zs / ms, b = bs / ms + wp.Span;
        float p = Physics.Topple(z, b, GROUND_ROUGH);
        return (1f - p) * (wp.Clear + m.MaxH) + p * m.MaxW;
    }

    static readonly float[] U = BuildSpeeds();
    static float[] BuildSpeeds()
    {
        var u = new float[240];
        for (int i = 0; i < u.Length; i++) u[i] = 0.002f * MathK.Exp(i * MathK.Log(60f / 0.002f) / (u.Length - 1));
        return u;
    }

    /// <summary>The fastest speed between grid point u (where it can still go) and the next grid point up, by bisection (16
    /// halvings: ~1e-6 of the step): on land at burst power (Shadow.LandMech), and in water within k x each folding's
    /// muscle capacity and at most cap (no closure: the step loop allocates nothing).</summary>
    static float RefineLand(Morphology m, in Physics.WalkProps wp, float axBurst, float k, float u)
    {
        float lo = u, hi = u * U[1] / U[0];
        if (Shadow.LandMech(m, wp, hi, axBurst, k) <= 0f) return hi;
        for (int j = 0; j < 16; j++) { float mid = 0.5f * (lo + hi); if (Shadow.LandMech(m, wp, mid, axBurst, k) <= 0f) lo = mid; else hi = mid; }
        return lo;
    }

    static bool CanSwim(ReadOnlySpan<Physics.SwimProps> sps, float k, float cap, float u)
    {
        foreach (var sp in sps) if (Physics.SwimPower(sp, Phys.RHO_W, u) <= MathF.Min(k * sp.Cap, cap)) return true;
        return false;
    }

    static float RefineSwim(ReadOnlySpan<Physics.SwimProps> sps, float k, float cap, float u)
    {
        float lo = u, hi = u * U[1] / U[0];
        if (CanSwim(sps, k, cap, hi)) return hi;
        for (int j = 0; j < 16; j++) { float mid = 0.5f * (lo + hi); if (CanSwim(sps, k, cap, mid)) lo = mid; else hi = mid; }
        return lo;
    }

    /// <summary>The flight muscle of a morphology (kg): the muscle of pairs that make lift, by their share of a flat,
    /// horizontal blade (as Shadow.Evaluate counts it; a leg's muscle is not a wing's).</summary>
    public static float FlightMuscle(Morphology m)
    {
        float f = 0f;
        // only pairs held out near the horizontal (droop under 45°): a drooped pair (a leg) strokes across the lift it would
        // make, and its muscle is not a wing's (phase 4: legs counted ~11% of their muscle as flight muscle, so a wingless
        // walker "flapped" up slopes, World.GroundSpeed)
        foreach (var a in m.Pairs) if (!a.Sagittal && a.Muscle > 0f && a.Artic > 0f && a.Theta < MathF.PI / 4f) f += 2f * a.Muscle * MathK.Cos(a.Theta) * (a.Chord / a.Thick) / (a.Chord / a.Thick + 2f);
        return f;
    }

    /// <summary>The aerobic metabolism its air-breathing surfaces support in air, W: the folded-in pockets and the gas
    /// space's lining (Physics.O2Supply's internal terms in air). What a swimmer's head at the surface breathes.</summary>
    public static float LungO2(Morphology m) => m.ExchInt * Phys.S_AIR_INT + m.ExchGas * Phys.S_AIR_INT;

    /// <summary>What its integument's turnover and its exchange epithelia cost at rest (W), at its tissues' metabolic level
    /// (docs/BODYPLAN.md §13.18). Phys.TURNOVER and Phys.EXCH_TISSUE are a warm-blooded body's tissue rates; an ectotherm's
    /// tissues respire at ~1/4-1/5 of a mammal's at the same temperature (Else &amp; Hulbert 1981; K.ECTO_LEVEL), and its skin
    /// and gills are such tissues, as its Kleiber term and its gut already are. Before, a 12.6 kg cold fish paid its 8.4 m²
    /// of gill epithelium 2.5 W, 54% of its Kleiber rate, where gill tissue is measured at a few to ~10% of a fish's
    /// standard metabolism (Mommsen 1984; Morgan &amp; Iwama 1999), and a gut built to that basal outgrew its trunk (GutRoom).
    /// A moult's cost (Phys.MOULT_COST) is the energy of the tissue it rebuilds, not a rate, so it is not scaled.</summary>
    public static float ModuleUpkeep(Morphology m, float level)
        => level * (Phys.TURNOVER * m.IntMass + Phys.EXCH_TISSUE * (m.ExchExt + m.ExchInt + m.ExchGas));

    /// <summary>Everything the live sim takes from a morphology grown at its live mass. level: Heat.Level of its
    /// endothermy; geneFactor: the basal factor of the genes that stay genes (GeneFactor); brainW its brain's cost.</summary>
    public static LiveCaps Of(Morphology m, float level, float geneFactor, float brainW)
    {
        var lc = new LiveCaps();
        var c = new Caps();
        float k = Shadow.P_BURST / Phys.P_SPEC;
        // ---- land
        var wp = Physics.Walk(m, Phys.RHO_A);
        float flex = Physics.Flex(m), axCap = 0f;
        foreach (var s in m.Segs) axCap += s.AxMuscle * Phys.P_SPEC * 0.5f;
        axCap *= flex;
        float bestCot = float.PositiveInfinity, topCot = 0f;
        foreach (float u in U)
        {
            if (Shadow.LandMech(m, wp, u, k * axCap, k) <= 0f) c.VLand = u;
            float met = Physics.WalkMetabolic(m, wp, 0f, Phys.RHO_A, u, axCap, out _);
            if (!float.IsPositiveInfinity(met)) { bestCot = MathF.Min(bestCot, met / u); topCot = met / u; }
        }
        c.CotLand = float.IsPositiveInfinity(bestCot) ? 0f : bestCot;
        // the grid's steps are 4.4%; the top speed is refined between grid points by bisection, so it is a continuous
        // function of the body (a gene step does not jump it by a grid step)
        if (c.VLand > 0f) c.VLand = RefineLand(m, wp, k * axCap, k, c.VLand);
        lc.CotLandTop = MathF.Max(c.CotLand, topCot);
        // ---- basal, gut, moult (the moult is paid while growing only: Body.Express keeps it out of the basal)
        float kleiber = K.BMR_W * MathK.Pow(MathF.Max(1e-6f, m.ActiveMass), K.EXP_BASAL) * level;
        lc.MoultW = Phys.MOULT_COST * m.Rigidity * m.ExoMass;
        float modules = ModuleUpkeep(m, level);
        // flight muscle's own upkeep (the avian premium, phase 4): resting metabolism goes with mitochondrial volume (proton
        // leak through the inner membrane is ~20-30% of standard metabolism, and tissue rates rank with mitochondrial
        // density; Rolfe & Brand 1997; Else & Hulbert 1985). Flight muscle holds ~MITO_RATIO times the body's mean
        // mitochondrial volume density (pigeon pectoralis ~0.2, hummingbird 0.35, against ~0.045 for a whole body;
        // Mathieu-Costello 1993; Suarez 1992), so it costs (MITO_RATIO - 1) x its share of the Kleiber rate on top. A full-winged
        // body (~0.17 of it flight muscle) pays ~+30%, within the measured avian range (non-passerines near mammals, passerines
        // +30-50%; McKechnie & Wolf 2004; Lasiewski & Dawson 1967), which COST_WING set by hand before
        float flyPremium = (MITO_RATIO - 1f) * FlightMuscle(m) / MathF.Max(m.Mass, 1e-9f) * kleiber;
        // the gut is built to its basal (Body.GutScale), but no bigger than its trunk has room for (GutRoom, docs/BODYPLAN.md §13.17)
        lc.GutRoom = GutRoom(m);
        lc.Gut = Body.GutScale(kleiber * MathF.Max(0.5f, geneFactor) + modules + flyPremium + brainW, kleiber, lc.GutRoom, out float basal);
        c.Basal = basal;
        // ---- water: burst top speed, sustained top speed, the cruise of least gross cost a metre and its net cost there
        Span<Physics.SwimProps> sps = stackalloc Physics.SwimProps[Physics.FOLD_N];
        Physics.SwimSet(m, Phys.RHO_W, sps);
        // sustained swimming is aerobic: its mechanical power can be no more than its muscle's efficiency x the oxygen its
        // exchange surfaces deliver beyond its basal, in water or (a head at the surface) through its air-breathing ones
        // (Brett 1964: a fish's critical swimming speed is set by its aerobic scope; Webb 1975). Phase 4 (2026-11-03): the
        // muscle's sustained capacity alone (P_SPEC on all its axial muscle, most of which in a fish is anaerobic white muscle)
        // gave a 1 kg fish 14 lengths a second sustained, against ~2-5 measured (Brett 1964; Videler 1993)
        float o2Swim = MathF.Max(Physics.O2Supply(m, Medium.Water, out _), LungO2(m));
        float aerobic = Phys.ETA_M * MathF.Max(0f, o2Swim - basal);
        // the twitch limit on the length its mass is spread over (docs/BODYPLAN.md §13.17: a weightless trailing thread
        // carries no muscle to make the body wave, yet on nose-to-tail length it raised the limit 30-60%)
        float bestGross = float.PositiveInfinity, vWardle = Shadow.WardleSpeed(m.WaveLength), burstCot = float.PositiveInfinity;
        foreach (float u in U)
        {
            float pBurst = float.PositiveInfinity, pCruise = float.PositiveInfinity;
            foreach (var sp in sps)
            {
                float pw = Physics.SwimPower(sp, Phys.RHO_W, u);
                if (pw <= k * sp.Cap) pBurst = MathF.Min(pBurst, pw);
                if (pw <= MathF.Min(sp.Cap, aerobic)) pCruise = MathF.Min(pCruise, pw);
            }
            if (!float.IsPositiveInfinity(pBurst) && u <= vWardle) c.VWater = u;
            if (!float.IsPositiveInfinity(pBurst)) burstCot = MathF.Min(burstCot, Phys.ANAEROBIC * pBurst / Phys.ETA_M / u);
            if (float.IsPositiveInfinity(pCruise)) continue;
            lc.VSust = u;
            float gross = (pCruise / Phys.ETA_M + basal) / u;
            if (gross < bestGross) { bestGross = gross; c.Cruise = u; c.CotWater = pCruise / Phys.ETA_M / u; }
        }
        if (c.VWater > 0f) c.VWater = MathF.Min(vWardle, RefineSwim(sps, k, float.PositiveInfinity, c.VWater));
        if (lc.VSust > 0f) lc.VSust = RefineSwim(sps, 1f, aerobic, lc.VSust);
        // a body that cannot sustain any swimming speed flounders on burst power, paid anaerobically (Phys.ANAEROBIC x),
        // so moving through water is never free; one that cannot move in water at all has no water cost (it cannot go there)
        if (c.CotWater <= 0f && !float.IsPositiveInfinity(burstCot)) c.CotWater = burstCot;
        // ---- air
        c.Margin = Physics.FlightMargin(m, out float pMin, out float vmp);
        lc.FlyMuscle = FlightMuscle(m);
        bool flies = c.Margin >= 1f && lc.FlyMuscle >= K.FLY_WING_MIN * K.FLY_MUSCLE_MAX * m.Mass;   // Marden (1987)
        c.Flies = flies ? 1f : 0f;
        if (flies) { c.VAir = vmp * Body.TopFlightSpeed(c.Margin); c.CotAir = pMin / Phys.ETA_M / vmp; }
        // ---- standing, breathing, protection, drag shape, turning, handling
        c.Stand = StandPosture(m, wp);
        c.O2Air = Physics.O2Supply(m, Medium.Air, out _);
        c.O2Water = Physics.O2Supply(m, Medium.Water, out _);
        lc.O2Lung = LungO2(m);
        c.Odds = Odds(m);
        c.Stream = Shadow.Streamlining(m);
        c.TurnR = Physics.TurnRadius(m, Phys.RHO_W, 1f);
        lc.C = c;
        lc.MoultSoft = MOULT_SOFT_GROW * m.Rigidity;
        lc.Crush = Physics.Handle(m, Phys.RHO_W, 0f).Force;
        lc.Support = wp.Support; lc.Clear = wp.Clear; lc.NLegs = wp.NLegs;
        lc.Length = m.Length;
        lc.Density = m.Mass / MathF.Max(m.Volume, 1e-12f);
        var (trunk, apps) = HeatSurfaces(m);
        float a23 = MathK.Pow(m.Mass, 2f / 3f);
        lc.SurfK = trunk / (Ref.Value.trunk * a23);
        // its appendages against the plain body's (legs, head and tail of a typical mammal), which carries the middle of the
        // retired radiator gene (0.5) that Heat's K_COND, APP_LEAK and VASO were calibrated with (a bare 1 kg body
        // thermoneutral down to 22 °C; Scholander et al. 1950): so the plain body loses heat as before, and longer, wider or
        // more pairs lose more (Allen's rule)
        lc.AppSurf = 0.5f * apps / MathF.Max(Ref.Value.apps * a23, 1e-12f);
        lc.EvapK = Evaporation(m, c.Basal) / (Ref.Value.evap * a23);
        return lc;
    }

    /// <summary>Crushing odds 1 / PKill - 1 against the bite of a predator scaled to its size (MorphEnv.PredBite at the
    /// reference mass, x M^(2/3)).</summary>
    public static float Odds(Morphology m)
    {
        float bite = 10f * MathK.Pow(m.Mass / Development.M_REF, 2f / 3f);
        float pk = Physics.PKill(m, bite);
        return pk > 0f ? 1f / pk - 1f : 1e6f;
    }

    /// <summary>Heat (docs/BODYPLAN.md §13.3): the surface each part loses heat through, m², its trunk's (every segment's
    /// surface) and its appendages' (both faces of every pair), each through its integument as a conductive layer in series
    /// with the air's boundary layer: x 1 / (1 + t / T_INT_HEAT), t its thickness (Schmidt-Nielsen 1997: tissue and
    /// cuticle conduct ~0.4 W/(m K); a still boundary layer of ~10 W/(m² K) is worth ~4 cm of it). Fur is the
    /// insulation gene's, applied on top (Heat.Conductance).</summary>
    public const float T_INT_HEAT = 0.04f;
    public static (float trunk, float apps) HeatSurfaces(Morphology m)
    {
        float trunk = 0f, apps = 0f;
        foreach (var s in m.Segs) trunk += s.Area / (1f + s.IntT / T_INT_HEAT);
        foreach (var a in m.Pairs) apps += 2f * 2f * a.Len * (a.Chord + a.Thick) / (1f + MathF.Max(a.ExoT * a.Exo, 0.0002f) / T_INT_HEAT);
        return (trunk, apps);
    }

    /// <summary>Water lost by evaporation in air, in the prototype's energy-equivalent units (W; Physics.SurfaceUpkeep's
    /// air terms): everted wet exchange surface (EVAP_EXT per m²), its lungs in proportion to the oxygen they deliver at
    /// basal (EVAP_INT), and its skin, e-fold less per EVAP_T0 of integument (cuticle and wax: Hadley 1994).</summary>
    public static float Evaporation(Morphology m, float basalW)
        => Phys.EVAP_EXT * m.ExchExt + Phys.EVAP_INT * MathF.Min(basalW, LungO2(m)) + Phys.EVAP_SKIN * m.SkinArea * MathK.Exp(-m.SkinThick / Phys.EVAP_T0);

    // ---- references: the live sim's plain body (Translate.Reference) at 1 kg, whose capabilities the per-step constants
    // C_TURN and DEF_BASE describe. Both scale out of mass: its integument t ~ M^(1/3) against a bite ~ M^(2/3) leaves its
    // crushing odds mass-free, and its least turning radius R = 2 m_eff / (rho C_L S) ~ M^(1/3).
    static readonly Lazy<(float odds, float turnR, float trunk, float evap, float apps)> Ref = new(() =>
    {
        var l = Translate.Reference(1f);
        var c = Shadow.Of(l, 1f, 0f, out var m);
        var hs = HeatSurfaces(m);
        return (Odds(m), Physics.TurnRadius(m, Phys.RHO_W, 1f), hs.trunk, Evaporation(m, c.Basal), hs.apps);
    });
    public static float RefOdds => Ref.Value.odds;
    public static float RefTurnR(float mass) => Ref.Value.turnR * MathK.Cbrt(mass);

    /// <summary>The live basal factor of the genes that stay genes: Shadow.GeneBasalFactor less the retired speed gene and appendage (radiator) gene (retired in
    /// phase 4: speed comes from the muscle the morphology carries, so its cost goes with it).</summary>
    public static float GeneFactor(ReadOnlySpan<sbyte> gn)
        => Shadow.GeneBasalFactor(gn) - K.COST_SPEED * (Body.Factor(gn[G.B_SPEED], K.BODY_RANGE_TRAIT) - 1f) - K.COST_APP * Body.Organ(gn[G.B_APPEND]);

    // ------------------------------------------------------------------ summaries (console, telemetry, clades)

    /// <summary>The physics role of each pair in the live body (no environment: what it would be used for): "fly" (it is
    /// flight muscle on a lifting blade), "support" (it bears weight on land), "swim" (its share of the thrust in water),
    /// "handle" (the strongest crusher at the mouth, above a soft mouth's), "steer" (its share of the control area),
    /// "breathe" (a tenth or more of its oxygen supply in air or in water passes this pair's exchange surface;
    /// docs/BODYPLAN.md §13.17), else "passive".</summary>
    public static string[] Roles(Morphology m)
    {
        var roles = new string[m.Pairs.Count];
        var thrust = Physics.ThrustShares(m);
        var ctl = Physics.ControlAreas(m, Phys.RHO_W, 1f);
        float ctlTot = 0f; foreach (var x in ctl) ctlTot += x;
        var hd = Physics.Handle(m, Phys.RHO_W, 0f);
        float wgt = Phys.G * MathF.Max(0f, m.Mass - Phys.RHO_A * m.Volume);
        bool flies = Physics.FlightMargin(m) >= 1f;
        for (int i = 0; i < m.Pairs.Count; i++)
        {
            var a = m.Pairs[i];
            var s = m.Segs[a.Seg];
            float sin = MathK.Sin(a.Theta), cos = MathK.Cos(a.Theta);
            float contact = a.Sagittal ? 0f : MathF.Min(1f, a.Len * sin / (0.5f * s.H + 0.002f)); contact *= contact;
            float f = Physics.LimbForce(m, a, MathF.Max(a.Len * (cos + 0.25f), 1e-6f));
            float sup = wgt > 0f ? 2f * f * contact * a.Artic * Phys.DUTY / (Phys.SAFETY * wgt) : 0f;
            float lift = a.Sagittal ? 0f : cos * (a.Chord / a.Thick) / (a.Chord / a.Thick + 2f);
            float sw = i < thrust.Length ? thrust[i] : 0f, st = ctlTot > 0f ? ctl[i] / ctlTot : 0f;
            if (flies && a.Muscle > 0f && lift > 0.5f && cos > 0.7f) roles[i] = "fly";
            else if (i == hd.Handler && hd.Force > 2f * Phys.F_MOUTH * MathK.Pow(m.Mass / Development.M_REF, 2f / 3f)) roles[i] = "handle";
            else if (sup >= 0.1f) roles[i] = "support";
            else if (sw >= 0.1f) roles[i] = "swim";
            else if (st >= 0.1f) roles[i] = "steer";
            else if (PairO2Share(m, a) >= 0.1f) roles[i] = "breathe";
            else roles[i] = "passive";
        }
        return roles;
    }

    static readonly string[] RoleNames = { "fly", "support", "swim", "handle", "steer", "breathe", "passive" };

    /// <summary>The role of each segment's internal skeleton (docs/BODYPLAN.md §13.19): "support (internal skeleton)" where
    /// it bears at least half of what the segment's section bears in bending (Segment.BendEndo against BendCap: the
    /// trunk's weight between its limbs and its limbs' moments at their roots, Physics.TrunkShare and LimbForce), "" where
    /// it has none or bears less.</summary>
    public const string SKELETON_ROLE = "support (internal skeleton)";
    public static string[] SegRoles(Morphology m)
    {
        var r = new string[m.Segs.Count];
        for (int i = 0; i < r.Length; i++) { var s = m.Segs[i]; r[i] = s.EndoMass > 0f && s.BendEndo >= 0.5f * s.BendCap ? SKELETON_ROLE : ""; }
        return r;
    }

    /// <summary>Does the body have a trunk skeleton that bears its trunk (any segment with the skeleton role)?</summary>
    public static bool HasEndoSkeleton(Morphology m)
    {
        foreach (var s in m.Segs) if (s.EndoMass > 0f && s.BendEndo >= 0.5f * s.BendCap) return true;
        return false;
    }

    /// <summary>The share of the body's oxygen supply this pair's exchange surface gives, in air or in water, whichever is
    /// larger (Physics.O2Supply's terms for its everted and folded-in epithelium).</summary>
    public static float PairO2Share(Morphology m, Pair a)
    {
        float ex = 2f * a.Pore * 2f * a.Len * (a.Chord + a.Thick) * Mat.FOLD, ext = ex * (1f - a.Invag), inn = ex * a.Invag;
        float air = Physics.O2Supply(m, Medium.Air, out _), water = Physics.O2Supply(m, Medium.Water, out _);
        float sa = air > 0f ? (ext * Phys.S_AIR_EXT + inn * Phys.S_AIR_INT) / air : 0f;
        float sw = water > 0f ? (ext * Phys.S_WATER_EXT + inn * Phys.S_WATER_INT) / water : 0f;
        return MathF.Max(sa, sw);
    }

    /// <summary>The body plans of the living (docs/BODYPLAN.md §13.6): per clade, its members' mean mass, segments and
    /// pairs (World.PSegN, PPairN), the pairs by physics role over a sample of up to sample members (each grown from its
    /// module genome; Roles), its loco, and one member's body (Describe); then the whole world's segment and pair counts.
    /// Read-only.</summary>
    public static string BodyPlanReport(World w, int sample = 40)
    {
        var c = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("BODY PLANS (docs/BODYPLAN.md §13: grown from each animal's module genome): per clade, mean mass, segments and appendage pairs; pairs by physics role (fly/support/swim/handle/steer/breathe/passive) over up to " + sample + " members; one member's body");
        var segHist = new int[33]; var pairHist = new int[17];
        for (int sp = 0; sp < w.NS; sp++)
        {
            int n = 0, taken = 0, rep = -1, endoN = 0; double mass = 0, segs = 0, pairs = 0, rigid = 0, endo = 0;
            var roles = new int[RoleNames.Length];
            for (int i = 0; i < w.Cap; i++)
            {
                if (w.StageOf[i] == St.Dead || w.Spc[i] != sp) continue;
                n++; mass += w.PM[i]; segs += w.PSegN[i]; pairs += w.PPairN[i];
                segHist[Math.Min(32, (int)w.PSegN[i])]++; pairHist[Math.Min(16, (int)w.PPairN[i])]++;
                if (rep < 0 && w.StageOf[i] == St.Adult) rep = i;
                if (taken < sample)
                {
                    taken++;
                    var mo = Grown(w, i);
                    rigid += mo.Rigidity; endo += mo.EndoMass / MathF.Max(mo.Mass, 1e-12f);
                    if (HasEndoSkeleton(mo)) endoN++;
                    foreach (var r in Roles(mo)) roles[Array.IndexOf(RoleNames, r)]++;
                }
            }
            if (n == 0) continue;
            if (rep < 0) for (int i = 0; i < w.Cap && rep < 0; i++) if (w.StageOf[i] != St.Dead && w.Spc[i] == sp) rep = i;
            var rl = new StringBuilder();
            for (int k = 0; k < RoleNames.Length; k++) rl.Append(c, $" {RoleNames[k]} {roles[k]}");
            sb.AppendLine(string.Format(c, "{0,-12} {1,6} {2,8:F3} kg  segs {3,5:F1}  pairs {4,5:F1}  rigidity {5:F2}  internal skeleton {8}/{9} ({10:F1}% of mass)  loco {6}  roles:{7}", w.Sp[sp].Name, n, mass / n, segs / n, pairs / n, rigid / Math.Max(1, taken), (Loco)w.PLoco[rep], rl, endoN, taken, 100.0 * endo / Math.Max(1, taken)));
            sb.AppendLine("    e.g. " + Describe(Grown(w, rep)));
        }
        sb.Append("all living: segments");
        for (int k = 0; k < segHist.Length; k++) if (segHist[k] > 0) sb.Append(c, $" {k}:{segHist[k]}");
        sb.Append("; pairs");
        for (int k = 0; k < pairHist.Length; k++) if (pairHist[k] > 0) sb.Append(c, $" {(k == 16 ? "16+" : k.ToString(c))}:{pairHist[k]}");
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>Slot i's body grown from its genome at its adult mass.</summary>
    public static Morphology Grown(World w, int i)
    {
        ReadOnlySpan<sbyte> gn = w.Genome.Slot(i);
        return Development.Grow(Codec.Decode(Codec.Block(gn)) ?? Codec.FromLabelled(gn), w.PM[i]);
    }

    /// <summary>One line describing a grown body: segments (their lengths, mm), and each appendage pair: its segment, length
    /// (mm), flatness (chord / thickness), joints, rigidity (mineral share), porosity (exchange share of its surface) and role.</summary>
    public static string Describe(Morphology m)
    {
        var c = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append(c, $"{m.Mass:0.###} kg, {m.SegmentCount} seg [");
        for (int i = 0; i < m.Segs.Count; i++) sb.Append(c, $"{(i > 0 ? " " : "")}{1000f * m.Segs[i].L:0.#}");
        sb.Append(c, $"] mm, fine {m.Fineness:0.0}, rigid {m.Rigidity:0.00}, endo {100f * m.EndoMass / MathF.Max(m.Mass, 1e-12f):0.#}%, pores {m.ExchExt:0.####}/{m.ExchInt + m.ExchGas:0.####} m2; {m.PairCount} pairs");
        var roles = Roles(m);
        for (int i = 0; i < m.Pairs.Count; i++)
        {
            var a = m.Pairs[i];
            sb.Append(c, $" | s{a.Seg} {1000f * a.Len:0.#}mm flat{a.Chord / MathF.Max(a.Thick, 1e-9f):0.#} j{a.Joints} min{a.Mineral:0.0} pore{a.Pore:0.00} {roles[i]}");
        }
        return sb.ToString();
    }
}
