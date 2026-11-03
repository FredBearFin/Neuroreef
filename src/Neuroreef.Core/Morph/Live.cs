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

    static readonly float[] U = BuildSpeeds();
    static float[] BuildSpeeds()
    {
        var u = new float[240];
        for (int i = 0; i < u.Length; i++) u[i] = 0.002f * MathK.Exp(i * MathK.Log(60f / 0.002f) / (u.Length - 1));
        return u;
    }

    /// <summary>The flight muscle of a morphology (kg): the muscle of pairs that make lift, by their share of a flat,
    /// horizontal blade (as Shadow.Evaluate counts it; a leg's muscle is not a wing's).</summary>
    public static float FlightMuscle(Morphology m)
    {
        float f = 0f;
        foreach (var a in m.Pairs) if (!a.Sagittal && a.Muscle > 0f && a.Artic > 0f) f += 2f * a.Muscle * MathK.Cos(a.Theta) * (a.Chord / a.Thick) / (a.Chord / a.Thick + 2f);
        return f;
    }

    /// <summary>The aerobic metabolism its air-breathing surfaces support in air, W: the folded-in pockets and the gas
    /// space's lining (Physics.O2Supply's internal terms in air). What a swimmer's head at the surface breathes.</summary>
    public static float LungO2(Morphology m) => m.ExchInt * Phys.S_AIR_INT + m.ExchGas * Phys.S_AIR_INT;

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
        lc.CotLandTop = MathF.Max(c.CotLand, topCot);
        // ---- basal, gut, moult (the moult is paid while growing only: Body.Express keeps it out of the basal)
        float kleiber = K.BMR_W * MathK.Pow(MathF.Max(1e-6f, m.ActiveMass), K.EXP_BASAL) * level;
        lc.MoultW = Phys.MOULT_COST * m.Rigidity * m.ExoMass;
        float modules = Phys.TURNOVER * m.IntMass + Phys.EXCH_TISSUE * (m.ExchExt + m.ExchInt + m.ExchGas);
        lc.Gut = Body.GutScale(kleiber * MathF.Max(0.5f, geneFactor) + modules + brainW, kleiber, out float basal);
        c.Basal = basal;
        // ---- water: burst top speed, sustained top speed, the cruise of least gross cost a metre and its net cost there
        var sps = Physics.SwimSet(m, Phys.RHO_W);
        float bestGross = float.PositiveInfinity, vWardle = Shadow.WardleSpeed(m.Length), burstCot = float.PositiveInfinity;
        foreach (float u in U)
        {
            float pBurst = float.PositiveInfinity, pCruise = float.PositiveInfinity;
            foreach (var sp in sps)
            {
                float pw = Physics.SwimPower(sp, Phys.RHO_W, u);
                if (pw <= k * sp.Cap) pBurst = MathF.Min(pBurst, pw);
                if (pw <= sp.Cap) pCruise = MathF.Min(pCruise, pw);
            }
            if (!float.IsPositiveInfinity(pBurst) && u <= vWardle) c.VWater = u;
            if (!float.IsPositiveInfinity(pBurst)) burstCot = MathF.Min(burstCot, Phys.ANAEROBIC * pBurst / Phys.ETA_M / u);
            if (float.IsPositiveInfinity(pCruise)) continue;
            lc.VSust = u;
            float gross = (pCruise / Phys.ETA_M + basal) / u;
            if (gross < bestGross) { bestGross = gross; c.Cruise = u; c.CotWater = pCruise / Phys.ETA_M / u; }
        }
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
        c.Stand = wp.Clear + m.MaxH;
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
        lc.AppSurf = apps / (Ref.Value.trunk * a23);
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
    static readonly Lazy<(float odds, float turnR, float trunk, float evap)> Ref = new(() =>
    {
        var l = Translate.Reference(1f);
        var c = Shadow.Of(l, 1f, 0f, out var m);
        return (Odds(m), Physics.TurnRadius(m, Phys.RHO_W, 1f), HeatSurfaces(m).trunk, Evaporation(m, c.Basal));
    });
    public static float RefOdds => Ref.Value.odds;
    public static float RefTurnR(float mass) => Ref.Value.turnR * MathK.Cbrt(mass);

    /// <summary>The live basal factor of the genes that stay genes: Shadow.GeneBasalFactor less the speed gene (retired in
    /// phase 4: speed comes from the muscle the morphology carries, so its cost goes with it).</summary>
    public static float GeneFactor(ReadOnlySpan<sbyte> gn)
        => Shadow.GeneBasalFactor(gn) - K.COST_SPEED * (Body.Factor(gn[G.B_SPEED], K.BODY_RANGE_TRAIT) - 1f);

    // ------------------------------------------------------------------ summaries (console, telemetry, clades)

    /// <summary>The physics role of each pair in the live body (no environment: what it would be used for): "fly" (it is
    /// flight muscle on a lifting blade), "support" (it bears weight on land), "swim" (its share of the thrust in water),
    /// "handle" (the strongest crusher at the mouth, above a soft mouth's), "steer" (its share of the control area), else
    /// "passive".</summary>
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
            float f = MathF.Min(a.MomentCap / MathF.Max(a.Len * (cos + 0.25f), 1e-6f), Phys.SIGMA_MUSCLE * Physics.MuscleArea(m, a) * Phys.EMA);
            float sup = wgt > 0f ? 2f * f * contact * a.Artic * Phys.DUTY / (Phys.SAFETY * wgt) : 0f;
            float lift = a.Sagittal ? 0f : cos * (a.Chord / a.Thick) / (a.Chord / a.Thick + 2f);
            float sw = i < thrust.Length ? thrust[i] : 0f, st = ctlTot > 0f ? ctl[i] / ctlTot : 0f;
            if (flies && a.Muscle > 0f && lift > 0.5f && cos > 0.7f) roles[i] = "fly";
            else if (i == hd.Handler && hd.Force > 2f * Phys.F_MOUTH * MathK.Pow(m.Mass / Development.M_REF, 2f / 3f)) roles[i] = "handle";
            else if (sup >= 0.1f) roles[i] = "support";
            else if (sw >= 0.1f) roles[i] = "swim";
            else if (st >= 0.1f) roles[i] = "steer";
            else roles[i] = "passive";
        }
        return roles;
    }

    /// <summary>One line describing a grown body: segments (their lengths, mm), and each appendage pair: its segment, length
    /// (mm), flatness (chord / thickness), joints, rigidity (mineral share), porosity (exchange share of its surface) and role.</summary>
    public static string Describe(Morphology m)
    {
        var c = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append(c, $"{m.Mass:0.###} kg, {m.SegmentCount} seg [");
        for (int i = 0; i < m.Segs.Count; i++) sb.Append(c, $"{(i > 0 ? " " : "")}{1000f * m.Segs[i].L:0.#}");
        sb.Append(c, $"] mm, fine {m.Fineness:0.0}, rigid {m.Rigidity:0.00}, pores {m.ExchExt:0.####}/{m.ExchInt + m.ExchGas:0.####} m2; {m.PairCount} pairs");
        var roles = Roles(m);
        for (int i = 0; i < m.Pairs.Count; i++)
        {
            var a = m.Pairs[i];
            sb.Append(c, $" | s{a.Seg} {1000f * a.Len:0.#}mm flat{a.Chord / MathF.Max(a.Thick, 1e-9f):0.#} j{a.Joints} min{a.Mineral:0.0} pore{a.Pore:0.00} {roles[i]}");
        }
        return sb.ToString();
    }
}
