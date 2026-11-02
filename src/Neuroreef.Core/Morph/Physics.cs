// Physics of a morphology (PROTOTYPE, docs/BODYPLAN.md §4). What a body can do comes from its geometry and materials
// in the medium around it, never from the name of a part: any moving surface makes thrust in water by its area, span,
// aspect ratio and flatness; any part that reaches the ground and is stiff enough bears weight; any porous surface
// exchanges gas by its area and the medium; any mineralised outer surface protects, and must be moulted to grow; any
// stiff, muscled, articulated part that reaches the mouth can grasp and crush food; any mobile surface steers.
//   swimming   body drag from fineness (Hoerner 1965) with skin friction by Reynolds number (Blasius; Prandtl-
//              Schlichting), appendage drag, induced drag of the lift that holds a body that is denser than water
//              (Prandtl), thrust at the Froude efficiency of an actuator disk of the swept area, times each propulsor's
//              profile efficiency (lift-based by aspect ratio and flatness, drag-based ~0.3; Vogel 1994; Walker &
//              Westneat 2000; Lighthill 1975)
//   turning    least turning radius R = 2 m_eff / (rho C_L S) of the lateral force its mobile surfaces and bending
//              trunk can make (Webb 1983, 1984; Fish 2002; Domenici & Blake 1997); escape and pursuit by Howland's
//              (1974) speed-against-turning-radius criterion
//   walking    weight in the medium (buoyancy), each limb's bending strength against the ground force over its lever
//              (safety factor 2; Alexander 1981; Biewener 1989), static stability from the limbs on the ground
//              (Full & Koditschek 1999: a tripod needs six legs at duty factor 0.5), the cost of supporting weight
//              per stride (Kram & Taylor 1990), of swinging the limbs, and of sliding what the limbs cannot hold up;
//              the work of lifting the body over obstacles its limbs do not clear, detours round those it cannot
//              reach over, toppling of a high centre of mass on a narrow base
//   feeding    swept area or volume for fine food; food items handled by Holling's (1959) type II disc equation,
//              opened only by a crushing force (muscle cross-section x lever ratio, within the skeleton's strength)
//              above the shell's (Vermeij 1987; Elner & Hughes 1978; Warner & Jones 1976), evasive prey caught by
//              speed, turning and strike reach; gut throughput ~ M^0.75 (Demment & Van Soest 1985); patchy food
//              paid for by travel between patches (Charnov 1976)
//   flight     induced power of the actuator disk of the span (Pennycuick 2008; Ellington 1984) against flight-muscle
//              power; lift-based profile efficiency (Dickinson, Lehmann & Sane 1999)
//   breathing  Fick's law per area of folded exchange surface, by medium and whether the surface is everted or folded
//              in; the cost of ventilating it (water ~800x denser than air; Schmidt-Nielsen 1997); osmotic upkeep in
//              water, evaporation in air; skin breathing through thin skin
//   integument crushing strength ~ sigma t² (plate), moulting for a rigid outer skeleton (time soft and exposed,
//              material lost)
//   predators  scaled with their prey in a size spectrum (bite ~ M^(2/3), speed ~ M^(1/6), turning radius ~ M^(1/3)),
//              so size itself is neutral to first order and the physics above decides it
// All maths through MathK.

using System;

namespace Neuroreef.Morph;

public enum Medium { Water, Air }

public sealed class MorphEnv
{
    public string Name = "";
    public Medium Medium;
    public bool HasFloor;          // a substrate to walk on and to feed from
    public bool FoodInColumn;      // fine food suspended in the medium (plankton), taken by swept volume; else on the floor, by swept area
    public float FoodVol;          // J/m³
    public float FoodArea;         // J/m²
    public float FoodAssim = 0.7f; // assimilation efficiency of the fine food (detritus is poor: Valiela 1995)
    public float Predation;        // attack pressure (fitness units: one unit is a basal metabolism's worth)
    public float PredSpeed;        // m/s: what a body of the reference mass must outrun (scaled with the prey's mass)
    public float PredBite;         // N: the force a predator of a reference-mass prey crushes with (scaled likewise)
    public float Roughness;        // 0 smooth, 1 boulders: how much a legged gait needs static stability, and the slopes it must not topple on
    public float ObstacleH;        // m: the height of the obstacles a body must get over on the floor
    public float ObstacleGap = 0.3f;   // m between obstacles along its path
    public float Clutter;          // m between the structures a swimmer must turn round (0: open water)
    public float Surge;            // m/s: the water motion a body on the floor must hold against
    // food items (Holling 1959): count per m² on the floor, or per m³ in the water column
    public float ItemDensity, ItemEnergy, ItemAssim = 0.8f;
    public float ItemFLo, ItemFHi; // shell strength range, N (log-uniform); 0 for soft items
    public float ItemEscape;       // m/s: evasive prey's escape speed (0: sessile)
    public float ItemTurnR = 0.02f;// m: evasive prey's turning radius
    public float ItemHandle;       // s: least handling time per item
    // patchy food (on the floor): patches of this area at this spacing; 0 for food spread evenly
    public float PatchArea, PatchGap;

    public float Rho => Medium == Medium.Water ? Phys.RHO_W : Phys.RHO_A;
    public float Nu => Medium == Medium.Water ? Phys.NU_W : Phys.NU_A;

    public static MorphEnv OpenWater() => new()
    {
        Name = "open water", Medium = Medium.Water, HasFloor = false, FoodInColumn = true, FoodVol = 900f,
        Predation = 0.5f, PredSpeed = 1.5f, PredBite = 10f, Roughness = 0f, Surge = 0f,
        // evasive prey (small crustaceans and fish): ~100 J/m³ of the 1,000 in the water, but detected from farther than a mouth sweeps
        ItemDensity = 0.05f, ItemEnergy = 2000f, ItemEscape = 0.4f, ItemTurnR = 0.02f, ItemHandle = 5f,
    };
    public static MorphEnv SeaFloor() => new()
    {
        Name = "sea floor", Medium = Medium.Water, HasFloor = true, FoodInColumn = false, FoodArea = 300f, FoodAssim = 0.4f,
        Predation = 3.0f, PredSpeed = 1.5f, PredBite = 10f, Roughness = 1f, ObstacleH = 0.03f, ObstacleGap = 0.3f, Clutter = 0.3f, Surge = 0.3f,
        // shelled prey (snails, bivalves, barnacles): richer than the detritus, but each must be cracked
        ItemDensity = 0.5f, ItemEnergy = 1500f, ItemFLo = 0.1f, ItemFHi = 300f, ItemHandle = 20f,
    };
    public static MorphEnv Land() => new()
    {
        Name = "land", Medium = Medium.Air, HasFloor = true, FoodInColumn = false, FoodArea = 500f,
        Predation = 0.5f, PredSpeed = 1.0f, PredBite = 10f, Roughness = 0.5f, ObstacleH = 0.01f, ObstacleGap = 0.1f, Surge = 0f,
        PatchArea = 1f, PatchGap = 4f,
    };
}

/// <summary>Physical constants of the prototype (SI). Sources in docs/BODYPLAN.md §4.</summary>
public static class Phys
{
    public const float G = 9.81f, RHO_W = 1025f, RHO_A = 1.2f;
    public const float NU_W = 1.0e-6f, NU_A = 1.5e-5f;   // kinematic viscosity, m²/s
    public const float CF = 0.006f;           // skin-friction coefficient at Re ~1e5, the reference the old calibration used (Hoerner 1965; Vogel 1994)
    public const float U_REF = 0.3f;          // m/s: the speed the reference drag area is reported at
    public const float B0 = 0.3f;             // standard metabolism of an ectotherm, W kg^-0.75 (~20 °C; Clarke & Johnston 1999)
    public const float P_SPEC = 40f;          // sustained mechanical power of aerobic muscle, W/kg (Weis-Fogh & Alexander 1977)
    public const float P_FLIGHT = 150f;       // peak power of flight muscle, W/kg (Pennycuick 2008; Askew & Marsh 2002)
    public const float W_SPEC = 50f;          // work of one maximal contraction, J/kg of muscle (Josephson 1993; Askew & Marsh 2002)
    public const float ETA_M = 0.25f;         // muscle efficiency, mechanical / metabolic
    public const float DIGEST_RAW = 7.14f;    // W of raw food a kg of gut can pass at the reference mass (5 W assimilated at 0.7)
    public const float SIGMA_MUSCLE = 2e5f;   // isometric stress of muscle, Pa (Alexander 2003)
    public const float DUTY = 0.5f, SAFETY = 2f, KT = 0.1f;   // walking duty factor; bone/cuticle safety factor; cost of support per weight
                                                                        // per stride (Kram & Taylor 1990), set so a 0.2 kg walker with 5 cm strides costs
                                                                        // ~2 J/(N m), Taylor, Heglund & Maloiy's (1982) 10.7 M^-0.32 J/(kg m)
    public const float MU_CRAWL = 0.7f;       // sliding friction of a soft body dragged over rough ground (what limbs do not hold up)
    public const float CRAWL_WAVE = 0.25f;    // a crawler's stride: the length of its body wave, x body length (Quillin 1999; Gray 1946)
    public const float MU_SLIP = 0.15f;       // residual slip and mucus of limbless crawling, x pressed weight (Denny 1980)
    // aerobic metabolism one m² of folded exchange surface supports: [medium, everted / folded-in]
    public const float S_WATER_EXT = 25f, S_WATER_INT = 12f, S_AIR_EXT = 15f, S_AIR_INT = 100f;
    public const float SKIN_EX = 0.05f, SKIN_T0 = 2e-4f;     // skin breathing: share of an everted surface, falling e-fold per SKIN_T0 of integument
    // ventilation: share of the metabolism a surface serves
    public const float V_WATER_EXT = 0.10f, V_WATER_INT = 0.25f, V_AIR_EXT = 0.02f, V_AIR_INT = 0.04f;
    public const float OSMO = 0.5f;           // W per m² of exchange surface in water (ion pumping)
    public const float EXCH_TISSUE = 0.3f;    // W per m² of exchange surface in any medium: the upkeep of a thin, perfused epithelium
    public const float EVAP_EXT = 10f, EVAP_INT = 0.05f, EVAP_SKIN = 2f, EVAP_T0 = 3e-4f;   // water loss in air, as energy
    public const float ANAEROBIC = 3f;        // energy cost per W of demand not met aerobically
    public const float TURNOVER = 0.1f;       // W per kg of integument
    public const float MOULT_SOFT = 0.05f;    // share of its life a fully rigid body spends soft in moults
    public const float MOULT_COST = 0.77f;    // W per kg of rigid integument: half lost per moult, 4 MJ/kg, every 30 days
    public const float CRAWL_PRESS = 0.1f;    // share of its weight a crawling body presses on the ground (adhesion; Denny 1980)
    public const float EMA = 0.25f;           // effective mechanical advantage of limb muscles (Biewener 1989: ~0.1-1)
    public const float SWATH = 0.8f;          // the width a body searches with its mouth and senses, x V^(1/3)
    public const float CAPTURE = 1.7f;        // suspended food is cleared from this many times the head's frontal area
    // food handling
    public const float MA_CLAW = 0.3f;        // lever ratio of a pincer or jaw (crab chelae 0.2-0.5; Warner & Jones 1976; Alexander 1983)
    public const float F_MOUTH = 0.3f;        // N: what a soft mouth with no appendages can crush at the reference mass (~ M^(2/3))
    public const float E_TISSUE = 5e6f;       // J per kg of wet prey tissue (Cummins & Wuycheck 1971; Peters 1983)
    public const float GAPE = 0.1f;           // a prey item ~ this share of the eater's mass doubles its handling time (Werner 1974)
    public const float DETECT = 4f;           // evasive prey are detected over this many times the mouth's capture area
    // turning
    public const float CL_MAX = 1.0f;         // lift coefficient a deflected control surface or bent trunk reaches (Webb 1984; Fish 2002)
    public const float CA_LAT = 0.5f;         // lateral added-mass coefficient of an elongate body (Lighthill 1975)
    public const float PRED_R = 0.3f;         // m: turning radius of the predator of a reference-mass prey (~0.5 of a 0.6 m fish)
    public const float R_MAX = 100f;
    public const float R_MIN_L = 0.1f;        // least turning radius as a share of body length (Domenici & Blake 1997)
}

public struct MorphEval
{
    public float Fitness, Net, Basal, Intake, Speed, MaxSpeed, Mortality, PKill, MoultShare, O2Supply, O2Demand;
    public bool Walks, Legged;
    public float Hold, Support, Stability, FlightMargin;
    public float Mass, CrushForce, ItemShare, TurnR, Reach, Escape, CaptureP, TravelShare;
    public int Handler;            // index of the pair that handles food items (-1: the mouth alone)
}

public static class Physics
{
    // ---------- drag by Reynolds number ----------
    /// <summary>Skin-friction coefficient of a flat plate at Reynolds number Re: laminar (Blasius) below 5e5, the
    /// Prandtl-Schlichting transitional law above (Schlichting 1979; Hoerner 1965). Small, slow bodies pay more friction
    /// per area than large, fast ones.</summary>
    public static float SkinFriction(float re)
    {
        re = MathF.Max(re, 10f);
        return re < 5e5f ? 1.328f / MathK.Sqrt(re) : 0.074f * MathK.Pow(re, -0.2f) - 1700f / re;
    }

    // ---------- swimming ----------
    public struct SwimProps
    {
        public float DragArea;                      // at U_REF (for drag on walkers in water)
        public float Wet, FormK, Frontal, Step, AppEdge, AppFace, Len, Nu;
        public float LiftArea, Span, WNet, Cap, Sweep, EtaP;
    }

    /// <summary>Drag area of the trunk (drag / dynamic pressure, m²) at skin-friction coefficient cf: friction on its
    /// wetted area with Hoerner's (1965) form factor for fineness f = length / equivalent diameter,
    /// C_D,wet = C_f (1 + 1.5 f^-1.5 + 7 f^-3), at most a bluff body's 1.2 x its frontal area; plus 0.3 x the area of the
    /// steps between segments of different section. Least drag for its volume near f ~ 4-5, as fishes and cetaceans are
    /// (Webb 1975; Fish 1993).</summary>
    public static float BodyDragArea(Morphology m, float cf = Phys.CF)
    {
        float wet = 0f;
        foreach (var s in m.Segs) wet += s.Area;
        return TrunkDrag(wet, FormFactor(m.Fineness), m.FrontalArea, m.StepArea, cf);
    }

    static float FormFactor(float fineness) { float f = MathF.Max(0.05f, fineness); return 1f + 1.5f / (f * MathK.Sqrt(f)) + 7f / (f * f * f); }
    static float TrunkDrag(float wet, float formK, float frontal, float step, float cf) => MathF.Min(cf * wet * formK, 1.2f * frontal + cf * wet) + 0.3f * step;

    public static float DragAreaAt(in SwimProps p, float u)
    {
        float cf = SkinFriction(MathF.Max(u, 0.01f) * p.Len / p.Nu);
        return TrunkDrag(p.Wet, p.FormK, p.Frontal, p.Step, cf) + p.AppEdge + cf * p.AppFace;
    }

    /// <summary>Profile efficiency of a flapping or rowing surface: lift-based by aspect ratio (Prandtl's AR/(AR+2)) and
    /// flatness (a rod makes no lift); drag-based paddling at most ~0.3 (Walker &amp; Westneat 2000).</summary>
    public static float ProfileEta(float aspect, float flat)
    {
        float lift = 0.9f * aspect / (aspect + 2f) * flat / (flat + 2f);
        float drag = 0.3f * flat / (flat + 1f);
        return MathF.Max(lift, drag);
    }

    public static float Flex(Morphology m)
    {
        float r = 0f, w = 0f;
        foreach (var s in m.Segs) { r += s.IntMass * s.IntMin; w += s.IntMass; }
        float rig = w > 0f ? r / w : 0f;
        int n = m.Segs.Count;
        return (1f - rig) + rig * (1f - 1f / n);   // a rigid trunk bends only between its segments
    }

    /// <summary>Thrust share of each propulsor (pairs, then the trunk last), for reporting what a pair is used for.</summary>
    public static float[] ThrustShares(Morphology m)
    {
        var sh = new float[m.Pairs.Count + 1];
        float tot = 0f;
        for (int i = 0; i < m.Pairs.Count; i++)
        {
            var a = m.Pairs[i];
            float cap = 2f * a.Muscle * Phys.P_SPEC * a.Artic;
            sh[i] = cap * ProfileEta(2f * a.Len / a.Chord, a.Chord / a.Thick); tot += sh[i];
        }
        float flex = Flex(m), ax = 0f;
        foreach (var s in m.Segs) ax += s.AxMuscle;
        sh[^1] = ax * Phys.P_SPEC * flex * 0.8f * flex * MathF.Max(0f, 1f - MathK.Exp(-(m.Fineness - 0.5f) / 2f));
        tot += sh[^1];
        if (tot > 0f) for (int i = 0; i < sh.Length; i++) sh[i] /= tot;
        return sh;
    }

    public static SwimProps Swim(Morphology m, float rho, float nu = Phys.NU_W)
    {
        var p = new SwimProps { Nu = nu, Len = MathF.Max(m.Length, 1e-3f), Frontal = m.FrontalArea, Step = m.StepArea, FormK = FormFactor(m.Fineness) };
        foreach (var s in m.Segs) p.Wet += s.Area;
        float maxReach = 0f, capEta = 0f, tailFin = 0f;
        // where each segment ends along the body (0 head, 1 tail)
        Span<float> end = m.Segs.Count <= 64 ? stackalloc float[m.Segs.Count] : new float[m.Segs.Count];
        float x = 0f;
        for (int i = 0; i < m.Segs.Count; i++) { x += m.Segs[i].L; end[i] = x / MathF.Max(m.Length, 1e-6f); }
        foreach (var a in m.Pairs)
        {
            float flat = a.Chord / a.Thick, cos = a.Sagittal ? 0f : MathK.Cos(a.Theta);
            float cdEdge = 1.1f - 0.8f * flat / (flat + 3f);
            p.AppEdge += 2f * a.Len * a.Thick * cdEdge;
            p.AppFace += 2f * 2f * a.Len * a.Chord;
            p.LiftArea += 2f * a.Len * a.Chord * cos * flat / (flat + 2f);
            maxReach = MathF.Max(maxReach, a.Len * cos);
            // a flat blade in the body's plane of bending, on its hind third, deepens the trailing edge that undulation
            // sheds its wake from (Lighthill 1975: thrust ~ the added mass of the trailing depth)
            if (a.Sagittal && end[a.Seg] > 0.67f) tailFin = MathF.Max(tailFin, 2f * a.Len * flat / (flat + 2f));
            float cap = 2f * a.Muscle * Phys.P_SPEC * a.Artic;
            if (cap > 0f)
            {
                p.Cap += cap; p.Sweep += 2f * 0.5f * MathF.PI * a.Len * a.Len * a.Artic;
                capEta += cap * ProfileEta(2f * a.Len / a.Chord, flat);
            }
        }
        if (m.MaxW > m.MaxH) p.LiftArea += 0.5f * m.Length * m.MaxW * (1f - m.MaxH / m.MaxW);   // a flattened body lifts too
        p.Span = m.MaxW + 2f * maxReach;
        // axial undulation: the trunk's own muscle, if the trunk can bend (Lighthill 1975: thrust from the trailing depth)
        float flex = Flex(m), ax = 0f;
        foreach (var s in m.Segs) ax += s.AxMuscle;
        float capAx = ax * Phys.P_SPEC * flex;
        if (capAx > 0f)
        {
            float etaAx = 0.8f * flex * MathF.Max(0f, 1f - MathK.Exp(-(m.Fineness - 0.5f) / 2f));
            p.Cap += capAx; p.Sweep += (m.TailH + tailFin) * 0.2f * m.Length * flex;
            capEta += capAx * etaAx;
        }
        p.EtaP = p.Cap > 0f ? capEta / p.Cap : 0f;
        p.WNet = Phys.G * MathF.Abs(m.Mass - rho * m.Volume);
        p.DragArea = DragAreaAt(p, Phys.U_REF);
        return p;
    }

    /// <summary>Mechanical power to swim at U (holding its depth); float.PositiveInfinity if it cannot.</summary>
    public static float SwimPower(in SwimProps p, float rho, float u)
    {
        if (p.Cap <= 0f || p.Sweep <= 0f || p.EtaP <= 1e-4f) return u <= 0f && p.WNet <= 0f ? 0f : float.PositiveInfinity;
        float q = 0.5f * rho * u * u;
        float d = q * DragAreaAt(p, u);
        float lift = MathF.Min(p.WNet, q * p.LiftArea);
        float di = lift > 0f && q > 0f ? lift * lift / (q * MathF.PI * p.Span * p.Span * 0.8f) : 0f;
        float t = d + di, wRem = p.WNet - lift;
        float pw = wRem > 0f ? wRem * MathK.Sqrt(wRem / (2f * rho * p.Sweep)) / p.EtaP : 0f;   // hovering: actuator disk
        if (u > 0f)
        {
            float ct = t / (q * p.Sweep);
            float etaF = 2f / (1f + MathK.Sqrt(1f + ct));
            pw += t * u / (etaF * p.EtaP);
        }
        return pw;
    }

    // ---------- turning ----------
    /// <summary>Muscle cross-section serving a pair, one side (m²): its muscle mass over the length of the segment that houses it.</summary>
    static float MuscleArea(Morphology m, Pair a) => a.Muscle / (Mat.RHO_TISSUE * MathF.Max(m.Segs[a.Seg].L, 1e-3f));

    /// <summary>Lateral control area (m²) of each pair at speed u, and of the trunk (last entry): a surface controls only
    /// as far as it can be deflected, i.e. it is articulated and its muscle's moment holds it against the hinge moment
    /// of its own lift (L x chord/4); the trunk only as far as it can bend and has axial muscle to bend it.</summary>
    public static float[] ControlAreas(Morphology m, float rho, float u)
    {
        var c = new float[m.Pairs.Count + 1];
        float q = 0.5f * rho * MathF.Max(u, 0.05f) * MathF.Max(u, 0.05f);
        for (int i = 0; i < m.Pairs.Count; i++)
        {
            var a = m.Pairs[i];
            float flat = a.Chord / a.Thick;
            float s = a.Len * a.Chord * flat / (flat + 2f);                    // one side's effective planform
            float hinge = q * s * Phys.CL_MAX * 0.25f * a.Chord;
            float mus = Phys.SIGMA_MUSCLE * MuscleArea(m, a) * 0.5f * a.Thick; // its muscle pulls at the base, half a thickness from the hinge
            float mob = a.Artic * MathF.Min(1f, hinge > 0f ? mus / hinge : 0f);
            c[i] = 2f * s * mob;
        }
        float meanH = 0f, ax = 0f, tis = 0f;
        foreach (var s in m.Segs) { meanH += s.H * s.L; ax += s.AxMuscle; tis += s.TissueMass; }
        meanH /= MathF.Max(m.Length, 1e-6f);
        float axShare = tis > 0f ? ax / tis : 0f;
        c[^1] = 0.5f * m.Length * meanH * Flex(m) * MathF.Min(1f, axShare / 0.1f);
        return c;
    }

    /// <summary>Least turning radius (m) at speed u: the centripetal force m_eff U²/R is the lateral force
    /// 0.5 rho U² C_L S its control surfaces make, so R = 2 m_eff / (rho C_L S), independent of speed where the muscles
    /// hold the surfaces (Webb 1983; Fish 2002). m_eff includes the lateral added mass of the water it must turn.</summary>
    public static float TurnRadius(Morphology m, float rho, float u)
    {
        float s = 0f;
        foreach (var c in ControlAreas(m, rho, u)) s += c;
        float mEff = m.Mass + Phys.CA_LAT * rho * m.Volume;
        // no body turns much tighter than ~0.1 of its own length, however much lateral area it has (Domenici & Blake 1997:
        // least radii 0.05-0.2 L); without this a very elongate trunk's side area made fineness-80 "eels" turn on a coin
        float rMin = Phys.R_MIN_L * m.Length;
        return s > 0f ? MathF.Min(Phys.R_MAX, MathF.Max(rMin, 2f * mEff / (rho * Phys.CL_MAX * s))) : Phys.R_MAX;
    }

    /// <summary>Howland's (1974) escape criterion as a smooth chance: a pursuer at speed Up and turning radius Rp catches
    /// prey at speed U and turning radius R with odds (Up/U)² x R/Rp (the prey escapes a faster pursuer by turning
    /// inside it). Returns the prey's chance to escape.</summary>
    public static float HowlandEscape(float u, float r, float up, float rp)
    {
        if (u <= 0f) return 0f;
        float odds = (up / u) * (up / u) * r / MathF.Max(rp, 1e-4f);
        return 1f / (1f + odds);
    }

    /// <summary>Speed through clutter: a body must turn round structures spaced gap apart, and loses speed in proportion
    /// to how much wider than the gap it turns (Webb 1984 on manoeuvring in structured habitats).</summary>
    public static float ClutterSpeed(float u, float r, float gap) => gap > 0f ? u * gap / (gap + r) : u;

    // ---------- walking ----------
    public struct WalkProps { public float WEff, Support, Stride, Stability, LegMass, Cap, Hook, NLegs, Clear, Reach, Span; }

    public static WalkProps Walk(Morphology m, float rho)
    {
        var w = new WalkProps { WEff = Phys.G * MathF.Max(0f, m.Mass - rho * m.Volume) };
        float capN = 0f, sw = 0f, sws = 0f, span = 0f, reach = 0f;
        foreach (var a in m.Pairs)
        {
            if (a.Sagittal) continue;   // one blade up, one down: not a pair of supports
            var s = m.Segs[a.Seg];
            float sin = MathK.Sin(a.Theta), cos = MathK.Cos(a.Theta);
            float contact = MathF.Min(1f, a.Len * sin / (0.5f * s.H + 0.002f));
            contact *= contact;   // a limb that barely reaches the ground carries little
            if (contact <= 0f) continue;
            float lever = a.Len * (cos + 0.25f);   // the foot's horizontal offset, plus the fore-aft swing of a stride
            float fMus = Phys.SIGMA_MUSCLE * MuscleArea(m, a) * Phys.EMA;   // its muscles must hold the joints against the ground force
            float f = MathF.Min(a.MomentCap / lever, fMus);
            float use = contact * a.Artic;
            capN += 2f * f * use;
            w.NLegs += 2f * use;
            float stride = a.Len * sin * a.Artic;
            sw += f * use; sws += f * use * stride; w.Clear += f * use * a.Len * sin;
            span += f * use * a.Len * cos; reach += f * use * a.Len * a.Artic;
            w.LegMass += 2f * a.Mass * contact;
            w.Cap += 2f * a.Muscle * Phys.P_SPEC * use;
            w.Hook += 2f * use * f;
        }
        w.Support = w.WEff > 0f ? MathF.Min(1f, capN * Phys.DUTY / (Phys.SAFETY * w.WEff)) : 1f;
        w.Stride = sw > 0f ? sws / sw : 0f;
        w.Clear = sw > 0f ? w.Clear / sw * w.Support : 0f;   // how far its limbs lift its belly off the ground
        w.Span = sw > 0f ? span / sw : 0f;                    // how far out its feet stand
        w.Reach = sw > 0f ? reach / sw : 0f;                  // how high a step its limbs can reach up to
        w.Stability = MathF.Min(1f, w.NLegs * Phys.DUTY / 3f);
        return w;
    }

    /// <summary>Metabolic power of moving over the floor at U, the cheaper of walking on its limbs (within its limb
    /// muscles' power, the trunk's helping to drag what the limbs do not hold up) and crawling on its belly (within all
    /// its muscles' power); infinity if it can do neither.</summary>
    public static float WalkMetabolic(Morphology m, in WalkProps w, float waterDragArea, float rho, float u, float axialCap, out bool legged)
    {
        float drag = 0.5f * rho * u * u * waterDragArea * u;   // the medium's drag (water; ~0 in air)
        float best = float.PositiveInfinity;
        legged = false;
        // limbless crawling (peristalsis, lateral undulation) pays Kram & Taylor's cost of generating force per stride, the
        // stride being a body wave (CRAWL_WAVE x length), plus a residual slip (MU_SLIP): lateral undulation then costs about
        // what legs of the same mass do (Walton, Jayne & Bennett 1990). The old sliding-friction cost (mu = 0.7 on the whole
        // weight, the same per kg at every size) made a 2 g crawler 3x cheaper than a 2 g walker (docs/BODYPLAN.md §8).
        float crawlW = MathF.Max(w.WEff, Phys.CRAWL_PRESS * m.Mass * Phys.G);
        float wave = MathF.Max(Phys.CRAWL_WAVE * m.Length, 1e-3f);
        float crawlSup = Phys.KT * crawlW * u / wave;
        float crawlMech = Phys.MU_SLIP * crawlW * u + drag + crawlSup * Phys.ETA_M;
        if (crawlMech <= axialCap + w.Cap) best = crawlSup + (Phys.MU_SLIP * crawlW * u + drag) / Phys.ETA_M;
        if (w.Stride > 1e-3f && w.Cap > 0f)
        {
            float sup = Phys.KT * w.WEff * w.Support * u / w.Stride;
            float swing = 4f * w.LegMass * u * u * u / w.Stride;
            // what the limbs do not hold up, the trunk crawls under (at the crawling cost, not a cheaper slide: a vestigial,
            // muscle-less limb once made "walking" a discount on crawling; docs/BODYPLAN.md §8)
            float rest = 1f - w.Support;
            float slideMech = rest * (Phys.MU_SLIP * crawlW * u + crawlSup * Phys.ETA_M);
            float legMech = swing + slideMech + drag + sup * Phys.ETA_M;
            float leg = sup + rest * crawlSup + (swing + rest * Phys.MU_SLIP * crawlW * u + drag) / Phys.ETA_M;
            if (legMech <= w.Cap + axialCap * (1f - w.Support) && leg < best) { best = leg; legged = true; }
        }
        return best;
    }

    /// <summary>Crossing obstacles of height h every gap metres at speed u. The centre of mass must rise by what the
    /// limbs do not already clear, ~h²/(h + clearance) (a crawler, clearance 0, lifts its whole weight by h), and the
    /// muscle does that work at its efficiency (it is not recovered on the way down). A step higher than the body can
    /// reach up to (its limbs, or half its length draped over the edge if it can bend) forces a detour. Returns the
    /// metabolic power and the speed factor. The body's own height gives it nothing: it must lift itself over the top
    /// either way (the old clearance-by-height model rewarded tall bodies for free; docs/BODYPLAN.md §8).</summary>
    public static float Obstacles(float wEff, float h, float gap, float clear, float reachUp, float u, out float uFactor)
    {
        uFactor = 1f;
        if (h <= 0f || gap <= 0f) return 0f;
        float rise = h * h / (h + clear);
        float r = MathF.Max(reachUp, 1e-4f);
        uFactor = 1f / (1f + 0.5f * (h / r) * (h / r));
        return wEff * rise * u / gap / Phys.ETA_M;
    }

    /// <summary>Chance of toppling on slopes of a rough floor: a centre of mass at height z over a base of half-width b
    /// tips past slopes steeper than atan(b/z) (static stability margin; McGhee &amp; Frank 1968; Ting, Blickhan &amp; Full
    /// 1994). Rough ground has slopes up to tan = 0.5 x roughness. Smooth (a soft step at the tipping slope).</summary>
    public static float Topple(float z, float b, float roughness)
    {
        float tg = 0.5f * roughness;
        if (tg <= 0f || z <= 0f) return 0f;
        float k = b / (z * tg);
        return 1f / (1f + k * k * k * k);
    }

    /// <summary>How far an appendage can hold itself out against its own weight in the medium (1 = fully).</summary>
    public static float SelfSupport(Pair a, float rho)
    {
        float wgt = Phys.G * MathF.Max(0f, a.Mass - rho * a.Volume);
        float moment = wgt * 0.5f * a.Len * MathK.Cos(a.Theta);
        return moment <= 0f ? 1f : MathF.Min(1f, a.MomentCap / (Phys.SAFETY * moment));
    }

    // ---------- food handling ----------
    public struct Handling
    {
        public float Force;        // N: the crushing force it can bring to a food item at the mouth
        public int Handler;        // the pair that gives it (-1 the mouth)
        public float Power;        // W (mechanical): what the handler's muscle can put into opening an item
        public float Reach;        // m: how far beyond the mouth a strike reaches, at full speed against evasive prey
        public int Striker;
    }

    /// <summary>Distance from the mouth to the base of a pair (m).</summary>
    public static float BaseDistance(Morphology m, Pair a) { var s = m.Segs[a.Seg]; return s.X0 + 0.5f * s.L + 0.25f * s.W; }

    /// <summary>What the body can bring to bear on a food item. A pair crushes with the lesser of its muscle force
    /// (isometric stress x cross-section x the pincer's lever ratio; Warner &amp; Jones 1976; Taylor 2000) and what its
    /// skeleton bears loaded at mid-length (a soft limb cannot press hard), times its articulation (a jointless rigid
    /// rod cannot close on anything), and only so far as it reaches the mouth (it must hold the item there). A mouth with
    /// no appendages crushes weakly (F_MOUTH); arthropod mandibles and vertebrate jaws are themselves serial homologues
    /// of appendages and arches (Snodgrass 1935), so stronger jaws must come from appendage genes here.
    /// A strike reaches beyond the mouth by the articulated length past it, held out against its weight, at the tip
    /// speed one contraction gives the appendage and the water it drags (sqrt(2 W m_muscle / (m + rho V))).</summary>
    public static Handling Handle(Morphology m, float rho, float preyEscape)
    {
        float sizeK = m.Mass / Development.M_REF;
        var h = new Handling { Force = Phys.F_MOUTH * MathK.Pow(sizeK, 2f / 3f), Handler = -1, Striker = -1 };
        for (int i = 0; i < m.Pairs.Count; i++)
        {
            var a = m.Pairs[i];
            float d = BaseDistance(m, a);
            float toMouth = MathF.Min(1f, a.Len / MathF.Max(d, 1e-4f)); toMouth *= toMouth;
            float fMus = Phys.SIGMA_MUSCLE * MuscleArea(m, a) * Phys.MA_CLAW;
            float fSk = a.MomentCap / MathF.Max(0.5f * a.Len, 1e-4f);
            float f = MathF.Min(fMus, fSk) * a.Artic * toMouth;
            if (f > h.Force) { h.Force = f; h.Handler = i; h.Power = 2f * a.Muscle * Phys.P_SPEC * a.Artic; }
            if (preyEscape > 0f && a.Muscle > 0f)
            {
                float vTip = MathK.Sqrt(2f * Phys.W_SPEC * a.Muscle / (a.Mass + rho * a.Volume));
                float reach = MathF.Max(0f, a.Len * a.Artic - d) * SelfSupport(a, rho) * vTip / (vTip + preyEscape);
                if (reach > h.Reach) { h.Reach = reach; h.Striker = i; }
            }
        }
        return h;
    }

    /// <summary>Share of shelled items (strength log-uniform in [lo, hi]) a crushing force F opens, and the mean strength of those it opens.</summary>
    public static float Openable(float f, float lo, float hi, out float meanStrength)
    {
        meanStrength = 0f;
        if (hi <= 0f) return 1f;                 // soft items
        if (f <= lo) return 0f;
        float fc = MathF.Min(f, hi);
        meanStrength = (fc - lo) / MathK.Log(fc / lo);
        return MathK.Log(fc / lo) / MathK.Log(hi / lo);
    }

    /// <summary>Handling time of one item (s): the least time, longer as the shell's strength nears the crushing force
    /// (repeated loading, chipping; Elner &amp; Hughes 1978; Boulding 1984), and as the item's mass nears the eater's
    /// (Werner 1974: handling time rises steeply with prey size relative to the mouth).</summary>
    public static float HandlingTime(float h0, float meanStrength, float force, float itemMass, float bodyMass)
    {
        float k = itemMass / (Phys.GAPE * bodyMass);
        return h0 * (1f + 2f * (force > 0f ? meanStrength / force : 0f)) * (1f + k * k);
    }

    /// <summary>Holling's (1959) type II disc equation for several foods: with encounter rates a_i N_i (per s) and handling
    /// times h_i, intake = sum(a_i N_i E_i) / (1 + sum(a_i N_i h_i)); the search-time share is 1 / (1 + sum(a_i N_i h_i)).</summary>
    public static float Holling(float fineRate, float itemRate, float itemEnergy, float handle, out float searchShare)
    {
        searchShare = 1f / (1f + itemRate * handle);
        return (fineRate + itemRate * itemEnergy) * searchShare;
    }

    // ---------- flight ----------
    /// <summary>Flight-muscle power against the induced power of hovering on the span of its lifting surfaces (an upper
    /// bound on the least power of forward flight, ~half of it; Pennycuick 2008). Above 1 it can fly.</summary>
    public static float FlightMargin(Morphology m)
    {
        float span = 0f, cap = 0f, capEta = 0f;
        foreach (var a in m.Pairs)
        {
            float c = 2f * a.Muscle * Phys.P_FLIGHT * a.Artic;
            if (c <= 0f || a.Sagittal) continue;
            float eta = 0.9f * (2f * a.Len / a.Chord) / (2f * a.Len / a.Chord + 2f) * (a.Chord / a.Thick) / (a.Chord / a.Thick + 2f);
            span = MathF.Max(span, 2f * a.Len * MathK.Cos(a.Theta));
            cap += c; capEta += c * eta;
        }
        if (cap <= 0f || span <= 0f) return 0f;
        float eta0 = capEta / cap;
        float wgt = m.Mass * Phys.G;
        float disk = MathF.PI * 0.25f * (span + m.MaxW) * (span + m.MaxW);
        float pInd = 1.2f * wgt * MathK.Sqrt(wgt / (2f * Phys.RHO_A * disk));
        return cap * eta0 / pInd;
    }

    // ---------- gas exchange ----------
    /// <summary>Aerobic metabolism its exchange surfaces and skin support in this medium, W; and the share of it each
    /// costs to ventilate.</summary>
    public static float O2Supply(Morphology m, Medium med, out float ventShare)
    {
        bool water = med == Medium.Water;
        float sExt = water ? Phys.S_WATER_EXT : Phys.S_AIR_EXT, sInt = water ? Phys.S_WATER_INT : Phys.S_AIR_INT;
        float ext = m.ExchExt * sExt, inn = m.ExchInt * sInt;
        float skin = m.SkinArea * sExt * Phys.SKIN_EX * MathK.Exp(-m.SkinThick / Phys.SKIN_T0);
        float tot = ext + inn + skin;
        ventShare = tot > 0f ? (ext * (water ? Phys.V_WATER_EXT : Phys.V_AIR_EXT) + inn * (water ? Phys.V_WATER_INT : Phys.V_AIR_INT)) / tot : 0f;
        return tot;
    }

    /// <summary>Energy its surfaces cost in the medium: osmotic upkeep of exchange surfaces in water, evaporation in air.</summary>
    public static float SurfaceUpkeep(Morphology m, Medium med, float internalO2)
    {
        float tissue = Phys.EXCH_TISSUE * (m.ExchExt + m.ExchInt);
        if (med == Medium.Water) return tissue + Phys.OSMO * (m.ExchExt + m.ExchInt);
        return tissue + Phys.EVAP_EXT * m.ExchExt + Phys.EVAP_INT * internalO2 + Phys.EVAP_SKIN * m.SkinArea * MathK.Exp(-m.SkinThick / Phys.EVAP_T0);
    }

    // ---------- integument ----------
    /// <summary>Chance that an attack by a predator of this bite kills, averaged over the body's surface: a surface
    /// crushes at ~4 sigma t² (a plate), an everted exchange surface is unprotected.</summary>
    public static float PKill(Morphology m, float bite)
    {
        float sum = 0f, area = 0f;
        foreach (var s in m.Segs)
        {
            float f = 4f * Development.Sigma(s.IntMin) * s.IntT * s.IntT / bite;
            float pk = 1f / (1f + f), open = s.Pore * (1f - s.Invag);
            sum += s.Area * (open + (1f - open) * pk); area += s.Area;
        }
        foreach (var a in m.Pairs)
        {
            float ar = 2f * 2f * a.Len * (a.Chord + a.Thick);
            float t = MathF.Max(a.ExoT * a.Exo, 1e-5f);
            float f = 4f * Development.Sigma(a.Mineral * a.Exo) * t * t / bite;
            float pk = 1f / (1f + f), open = a.Pore * (1f - a.Invag);
            sum += ar * (open + (1f - open) * pk); area += ar;
        }
        return area > 0f ? sum / area : 1f;
    }

    // ---------- the whole budget ----------
    static readonly float[] Speeds = BuildSpeeds();
    static float[] BuildSpeeds()
    {
        var u = new float[48];
        for (int i = 1; i < u.Length; i++) u[i] = 0.002f * MathK.Exp((i - 1) * MathK.Log(3f / 0.002f) / (u.Length - 2));
        return u;
    }

    public static float Kleiber(float mass) => Phys.B0 * MathK.Pow(mass, 0.75f);

    /// <summary>The cross-section of water a body clears of suspended food as it swims (m²): the stream through and
    /// around its head, CAPTURE x the head's frontal area (a planktivore's reaction field and gape scale with its head;
    /// Eggers 1977; Aksnes &amp; Giske 1993). CAPTURE = 1.7 gives a fineness-6 fish the old 0.5 V^(2/3). An earlier
    /// 0.5 V^(2/3), whatever the shape, let a 2 g thread of fineness 160 take a fish's catch on a frontal area 60x
    /// smaller (docs/BODYPLAN.md §8).</summary>
    public static float Capture(Morphology m)
    {
        if (m.Segs.Count == 0) return 0f;
        var h = m.Segs[0];
        return Phys.CAPTURE * MathF.PI / 4f * h.W * h.H;
    }

    /// <summary>Gut throughput of raw food (W): gut capacity ~ M but retention time ~ M^0.25, so throughput per kg of gut
    /// falls as M^-0.25 and the whole gut passes ~ M^0.75, as Kleiber's demand rises (Demment &amp; Van Soest 1985).</summary>
    public static float GutCapacity(Morphology m) => Phys.DIGEST_RAW * m.GutMass * MathK.Pow(m.Mass / Development.M_REF, -0.25f);

    /// <summary>Energy surplus of a morphology in an environment at the best way and speed of foraging it has, and its
    /// predation risk. Fitness = surplus / its basal metabolism - mortality (both per unit of physiological time, so
    /// mass itself is neutral to first order: Charnov 1993).</summary>
    public static MorphEval Evaluate(Morphology m, MorphEnv env)
    {
        var e = new MorphEval { Mass = m.Mass };
        float rho = env.Rho;
        bool water = env.Medium == Medium.Water;
        float sizeK = m.Mass / Development.M_REF;
        float basal = Kleiber(m.Mass - m.InertMass);   // mineral does not metabolise
        e.Basal = basal;
        float o2 = O2Supply(m, env.Medium, out float vent);
        float intO2 = m.ExchInt * (water ? Phys.S_WATER_INT : Phys.S_AIR_INT);
        float upkeep = Phys.TURNOVER * m.IntMass + Phys.MOULT_COST * m.Rigidity * m.ExoMass;
        float surface = SurfaceUpkeep(m, env.Medium, 0f);
        // lungs lose water with the air they move, so with the oxygen they deliver, not with their capacity
        float lungEvap = env.Medium == Medium.Air && o2 > 0f ? Phys.EVAP_INT * intO2 / o2 : 0f;
        float gutCap = GutCapacity(m);
        float capture = Capture(m);
        // the width it searches on the floor: its mouth and senses, ~ its size. Gathering fine food with appendages (reaching,
        // sweeping) is not modelled: an early version let a hair-thin 20 cm arm sweep food for free. Appendages pay through
        // food ITEMS instead, which must be caught or cracked and held at the mouth (Handle).
        float swath = Phys.SWATH * MathK.Cbrt(m.Volume);
        e.O2Supply = o2;
        e.FlightMargin = env.Medium == Medium.Air ? FlightMargin(m) : 0f;

        // predators scaled to the prey in a size spectrum (Cohen et al. 1993; Brose et al. 2006): bite ~ muscle
        // cross-section ~ M^(2/3), speed ~ M^(1/6) (Peters 1983), turning radius ~ length ~ M^(1/3)
        float predBite = env.PredBite * MathK.Pow(sizeK, 2f / 3f);
        float predSpeed = env.PredSpeed * MathK.Pow(sizeK, 1f / 6f);
        float predR = Phys.PRED_R * MathK.Cbrt(sizeK);

        var sp = Swim(m, rho, env.Nu);
        var wp = Walk(m, rho);
        float itemEscape = env.ItemDensity > 0f ? env.ItemEscape : 0f;
        var hd = Handle(m, rho, itemEscape);
        // crawling (peristalsis, undulation, pedal waves) bends the trunk, as swimming by undulation does
        float axCap = 0f; foreach (var s in m.Segs) axCap += s.AxMuscle * Phys.P_SPEC * 0.5f;
        float flex = Flex(m);
        axCap *= flex;
        float meanH = 0f; foreach (var s in m.Segs) meanH += s.H * s.L; meanH /= MathF.Max(m.Length, 1e-6f);   // its height along its length

        // food items
        float itemMass = env.ItemEnergy / Phys.E_TISSUE;
        float meanF = 0f, open = 0f;
        if (env.ItemDensity > 0f) open = Openable(hd.Force, env.ItemFLo, env.ItemFHi, out meanF);
        float hItem = env.ItemDensity > 0f ? HandlingTime(env.ItemHandle, meanF, hd.Force, itemMass, m.Mass) : 0f;
        e.CrushForce = hd.Force; e.Reach = hd.Reach; e.Handler = hd.Handler;

        // turning: at the speed of an escape from its predator
        float turnR = water ? TurnRadius(m, rho, predSpeed) : Phys.R_MAX;
        e.TurnR = turnR;

        // holding station against surge (on the floor): grip by weight and hooked limbs, and not toppling (the drag on the
        // side area acts at the side's centroid; weight and hooks right it over the base); a swimmer holds by its thrust
        float holdWalk = 1f, holdSwim = 1f;
        float zCrawl = 0.5f * meanH, bCrawl = 0.5f * m.MaxW;
        if (env.HasFloor && env.Surge > 0f)
        {
            float q = 0.5f * rho * env.Surge * env.Surge;
            float side = 0.8f * m.Length * m.MaxH;
            foreach (var a in m.Pairs) side += 2f * a.Len * a.Thick;
            float drag = q * side;
            float slide = (0.6f * wp.WEff + wp.Hook) / drag;
            float zc = wp.Clear + 0.5f * m.MaxH, b = 0.5f * m.MaxW + wp.Span;
            float topple = (wp.WEff + wp.Hook) * b / (drag * zc);
            holdWalk = MathF.Min(1f, MathF.Min(slide, topple));
            float ps = SwimPower(sp, rho, env.Surge);
            holdSwim = float.IsPositiveInfinity(ps) ? 0f : MathF.Min(1f, sp.Cap / MathF.Max(ps, 1e-9f));
        }

        // per mode and speed: locomotion power, speed factor, duty (weak swimmers), legged?
        const int NS = 48;
        Span<float> lo = stackalloc float[2 * NS], uf = stackalloc float[2 * NS], du = stackalloc float[2 * NS];
        Span<byte> lg = stackalloc byte[2 * NS];
        float maxU = 0f; bool maxLegged = false; int maxMode = 0;
        for (int mode = 0; mode < 2; mode++)
        {
            bool walk = mode == 1;
            for (int k = 0; k < NS; k++)
            {
                int i = mode * NS + k;
                lo[i] = float.PositiveInfinity; uf[i] = 0f; du[i] = 0f; lg[i] = 0;
                if (walk && !env.HasFloor) continue;
                if (!walk && !water) continue;
                float u = Speeds[k];
                float duty = 1f, uFactor = 1f, loco;
                if (walk)
                {
                    loco = WalkMetabolic(m, wp, water ? sp.DragArea : 0f, rho, u, axCap, out bool legged);
                    if (float.IsPositiveInfinity(loco)) continue;
                    float clear = legged ? wp.Clear : 0f;
                    float reachUp = MathF.Max(legged ? wp.Reach : 0f, 0.5f * m.Length * flex);
                    loco += Obstacles(wp.WEff, env.ObstacleH, env.ObstacleGap, clear, reachUp, u, out float uObs);
                    uFactor = uObs;
                    // a legged gait on rough ground needs a tripod down (Full & Koditschek 1999), and nothing may topple
                    if (legged) uFactor *= 1f - env.Roughness * (1f - wp.Stability);
                    float z = legged ? clear + 0.5f * meanH : zCrawl, b = legged ? bCrawl + wp.Span : bCrawl;
                    uFactor *= 1f - 0.5f * Topple(z, b, env.Roughness);
                    lg[i] = (byte)(legged ? 1 : 0);
                }
                else
                {
                    float mech = SwimPower(sp, rho, u);
                    if (float.IsPositiveInfinity(mech)) continue;
                    if (mech > sp.Cap)
                    {
                        // too weak to hold this pace (or its depth) steadily: it swims flat out for the share of the time its
                        // muscle allows, and sinks or drifts the rest
                        if (!(sp.Cap > 0f)) continue;
                        duty = sp.Cap / mech; mech = sp.Cap;
                    }
                    loco = mech / Phys.ETA_M;
                }
                lo[i] = loco; uf[i] = uFactor; du[i] = duty;
                if (duty >= 1f && u * uFactor > maxU) { maxU = u * uFactor; maxLegged = lg[i] == 1; maxMode = mode; }
            }
        }
        e.MaxSpeed = maxU;

        // evasive prey: caught by Howland's criterion at its top speed and turning radius, the strike reach adding to the
        // prey's own turning radius (the pursuit ends once the prey is within reach)
        float pCatch = 0f;
        if (env.ItemDensity > 0f && env.ItemEscape > 0f)
        {
            float rSelf = water ? TurnRadius(m, rho, MathF.Max(maxU, 0.05f)) : Phys.R_MAX;
            pCatch = 1f - HowlandEscape(env.ItemEscape, env.ItemTurnR + hd.Reach, maxU, rSelf);
        }
        e.CaptureP = pCatch;

        // predation: encounters rise with its own speed (Gerritsen & Strickler 1977); an attack kills by its armour unless
        // it escapes (Howland: speed and turning against its predator's, both slowed by clutter for a swimmer), and always
        // while it moults
        e.PKill = PKill(m, predBite);
        e.MoultShare = Phys.MOULT_SOFT * m.Rigidity;
        float esc;
        if (maxMode == 0 && water)
            esc = HowlandEscape(ClutterSpeed(maxU, turnR, env.Clutter), turnR, ClutterSpeed(predSpeed, predR, env.Clutter), predR);
        else
            esc = HowlandEscape(maxU, 1f, predSpeed, 1f);
        e.Escape = esc;
        float risk = (1f - e.MoultShare) * e.PKill * (1f - esc) + e.MoultShare;

        // patchy food: the best travel speed between patches, as cost per metre and time per metre, for each mode
        bool patchy = env.HasFloor && env.PatchGap > 0f && env.PatchArea > 0f;
        float bestFit = float.NegativeInfinity, bestNet = float.NegativeInfinity;
        for (int mode = 0; mode < 2; mode++)
        {
            for (int k = 1; k < NS; k++)
            {
                int i = mode * NS + k;
                if (float.IsPositiveInfinity(lo[i])) continue;
                float u = Speeds[k], uEff = u * uf[i], duty = du[i];
                bool walk = mode == 1;
                float hold = walk ? holdWalk : holdSwim;
                // encounters
                float fine, itemRate;
                if (env.FoodInColumn)
                {
                    fine = env.FoodVol * u * capture;
                    itemRate = env.ItemDensity * Phys.DETECT * capture * u * pCatch;
                }
                else
                {
                    float a = swath * uEff;
                    fine = env.FoodArea * a;
                    itemRate = env.ItemDensity * a * open;
                }
                fine *= hold * duty; itemRate *= hold * duty;
                float raw = Holling(fine, itemRate, env.ItemEnergy, hItem, out float search);
                float itemRaw = itemRate * env.ItemEnergy * search;
                float fineRaw = raw - itemRaw;
                float assim = env.FoodAssim * fineRaw + env.ItemAssim * itemRaw;
                if (raw > gutCap) assim *= gutCap / raw;   // the gut passes raw food at its rate whatever it is worth
                float handlePow = hd.Handler >= 0 && !env.FoodInColumn ? hd.Power / Phys.ETA_M : 0f;
                float demand = basal + upkeep + lo[i] * search + handlePow * (1f - search);
                float net = NetOf(assim, demand, o2, vent, surface, lungEvap);
                float uMean = u * search, travelShare = 0f;
                if (patchy)
                {
                    // Charnov (1976): each patch is eaten out, then the body travels PatchGap to the next at its best speed
                    float patchE = env.PatchArea * env.FoodArea * env.FoodAssim * hold;
                    float tIn = assim > 0f ? patchE / assim : float.PositiveInfinity;
                    float bestRate = float.NegativeInfinity, bestShare = 0f, bestUm = uMean;
                    for (int kv = 1; kv < NS; kv += 2)
                    {
                        int iv = mode * NS + kv;
                        if (float.IsPositiveInfinity(lo[iv]) || uf[iv] <= 0f) continue;
                        float v = Speeds[kv] * uf[iv] * du[iv];
                        if (v <= 0f) continue;
                        float tT = env.PatchGap / v;
                        float netT = NetOf(0f, basal + upkeep + lo[iv], o2, vent, surface, lungEvap);
                        float rate = float.IsPositiveInfinity(tIn) ? netT : (net * tIn + netT * tT) / (tIn + tT);
                        if (rate > bestRate)
                        {
                            bestRate = rate;
                            bestShare = float.IsPositiveInfinity(tIn) ? 1f : tT / (tIn + tT);
                            bestUm = uMean * (1f - bestShare) + Speeds[kv] * bestShare;
                        }
                    }
                    if (float.IsNegativeInfinity(bestRate)) continue;
                    net = bestRate; travelShare = bestShare; uMean = bestUm;
                }
                float enc = MathK.Sqrt(uMean * uMean + predSpeed * predSpeed) / predSpeed;
                float f = net / basal - env.Predation * enc * risk;
                if (f > bestFit)
                {
                    bestFit = f; bestNet = net; e.Intake = assim; e.Speed = u; e.Walks = walk; e.Legged = lg[i] == 1; e.O2Demand = demand; e.Hold = hold;
                    e.Mortality = env.Predation * enc * risk;
                    e.ItemShare = raw > 0f ? env.ItemAssim * itemRaw / MathF.Max(1e-12f, env.FoodAssim * fineRaw + env.ItemAssim * itemRaw) : 0f;
                    e.TravelShare = travelShare;
                }
            }
        }
        if (float.IsNegativeInfinity(bestNet))
        {
            // it cannot even hold still (a body denser than water with nothing to swim with, in open water): it sinks out
            float demand = basal + upkeep;
            bestNet = -demand - Phys.ANAEROBIC * MathF.Max(0f, demand - o2) - surface - basal * 2f;
            e.O2Demand = demand;
            e.Mortality = env.Predation * risk;
            bestFit = bestNet / basal - e.Mortality;
        }
        _ = maxLegged;
        e.Net = bestNet;
        e.Support = wp.Support; e.Stability = wp.Stability;
        e.Fitness = bestFit;
        return e;
    }

    static float NetOf(float intake, float demand, float o2, float vent, float surface, float lungEvap)
        => intake - demand - vent * MathF.Min(demand, o2) - Phys.ANAEROBIC * MathF.Max(0f, demand - o2) - surface - lungEvap * MathF.Min(demand, o2);

    /// <summary>What each appendage pair is used for, in the way of life the evaluation chose: "swim" (its share of the
    /// thrust), "support" (of the weight its limbs bear), "handle" (it is the crusher or the striker that feeding uses),
    /// "steer" (of the control area), or "passive".</summary>
    public static string[] PairUses(Morphology m, MorphEnv env, in MorphEval e)
    {
        var uses = new string[m.Pairs.Count];
        var thrust = ThrustShares(m);
        var ctl = env.Medium == Medium.Water ? ControlAreas(m, env.Rho, 1f) : new float[m.Pairs.Count + 1];
        float ctlTot = 0f; foreach (var c in ctl) ctlTot += c;
        var wp = Walk(m, env.Rho);
        float supTot = 0f;
        var sup = new float[m.Pairs.Count];
        var hd = Handle(m, env.Rho, env.ItemEscape);
        for (int i = 0; i < m.Pairs.Count; i++)
        {
            var a = m.Pairs[i];
            if (a.Sagittal) continue;
            var s = m.Segs[a.Seg];
            float sin = MathK.Sin(a.Theta), cos = MathK.Cos(a.Theta);
            float contact = MathF.Min(1f, a.Len * sin / (0.5f * s.H + 0.002f)); contact *= contact;
            float fMus = Phys.SIGMA_MUSCLE * MuscleArea(m, a) * Phys.EMA;
            sup[i] = MathF.Min(a.MomentCap / (a.Len * (cos + 0.25f)), fMus) * contact * a.Artic;
            supTot += sup[i];
        }
        for (int i = 0; i < m.Pairs.Count; i++)
        {
            float sw = !e.Walks && env.Medium == Medium.Water ? thrust[i] : 0f;
            float su = e.Walks && e.Legged && supTot > 0f ? sup[i] / supTot : 0f;
            float st = !e.Walks && ctlTot > 0f ? ctl[i] / ctlTot : 0f;
            bool handles = e.ItemShare > 0.05f && (i == hd.Handler && !env.FoodInColumn || i == hd.Striker && env.FoodInColumn);
            float best = MathF.Max(MathF.Max(sw, su), st);
            uses[i] = handles ? "handle" : best < 0.1f ? "passive" : best == sw ? "swim" : best == su ? "support" : "steer";
        }
        return uses;
    }
}
