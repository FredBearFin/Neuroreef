// Physics of a morphology (PROTOTYPE, docs/BODYPLAN.md §4). What a body can do comes from its geometry and materials
// in the medium around it, never from the name of a part: any moving surface makes thrust in water by its area, span,
// aspect ratio and flatness; any part that reaches the ground and is stiff enough bears weight; any porous surface
// exchanges gas by its area and the medium; any mineralised outer surface protects, and must be moulted to grow.
//   swimming   body drag from fineness (Hoerner 1965), appendage drag, induced drag of the lift that holds a body that
//              is denser than water (Prandtl), thrust at the Froude efficiency of an actuator disk of the swept area,
//              times each propulsor's profile efficiency (lift-based by aspect ratio and flatness, drag-based ~0.3;
//              Vogel 1994; Walker & Westneat 2000; Lighthill 1975)
//   walking    weight in the medium (buoyancy), each limb's bending strength against the ground force over its lever
//              (safety factor 2; Alexander 1981; Biewener 1989), static stability from the limbs on the ground
//              (Full & Koditschek 1999: a tripod needs six legs at duty factor 0.5), the cost of supporting weight
//              per stride (Kram & Taylor 1990), of swinging the limbs, and of sliding what the limbs cannot hold up
//   flight     induced power of the actuator disk of the span (Pennycuick 2008; Ellington 1984) against flight-muscle
//              power; lift-based profile efficiency (Dickinson, Lehmann & Sane 1999)
//   breathing  Fick's law per area of folded exchange surface, by medium and whether the surface is everted or folded
//              in; the cost of ventilating it (water ~800x denser than air; Schmidt-Nielsen 1997); osmotic upkeep in
//              water, evaporation in air; skin breathing through thin skin
//   integument crushing strength ~ sigma t² (plate), moulting for a rigid outer skeleton (time soft and exposed,
//              material lost)
// All maths through MathK.

using System;

namespace Neuroreef.Morph;

public enum Medium { Water, Air }

public sealed class MorphEnv
{
    public string Name = "";
    public Medium Medium;
    public bool HasFloor;          // a substrate to walk on and to feed from
    public bool FoodInColumn;      // food suspended in the medium (plankton), taken by swept volume; else on the floor, by swept area
    public float FoodVol;          // J/m³
    public float FoodArea;         // J/m²
    public float Predation;        // attack pressure (fitness units: one unit is a basal metabolism's worth)
    public float PredSpeed;        // m/s: what a body must outrun
    public float PredBite;         // N: the force a predator crushes with
    public float Roughness;        // 0 smooth, 1 boulders: how much a legged gait needs static stability
    public float ObstacleH;        // m: the height of the obstacles a body must get over on the floor
    public float Surge;           // m/s: the water motion a body on the floor must hold against

    public float Rho => Medium == Medium.Water ? Phys.RHO_W : Phys.RHO_A;

    public static MorphEnv OpenWater() => new()
    {
        Name = "open water", Medium = Medium.Water, HasFloor = false, FoodInColumn = true, FoodVol = 900f,
        Predation = 0.5f, PredSpeed = 1.5f, PredBite = 10f, Roughness = 0f, Surge = 0f,
    };
    public static MorphEnv SeaFloor() => new()
    {
        Name = "sea floor", Medium = Medium.Water, HasFloor = true, FoodInColumn = false, FoodArea = 300f,
        Predation = 3.0f, PredSpeed = 1.5f, PredBite = 10f, Roughness = 1f, ObstacleH = 0.03f, Surge = 0.3f,
    };
    public static MorphEnv Land() => new()
    {
        Name = "land", Medium = Medium.Air, HasFloor = true, FoodInColumn = false, FoodArea = 500f,
        Predation = 0.5f, PredSpeed = 1.0f, PredBite = 10f, Roughness = 0.5f, ObstacleH = 0.01f, Surge = 0f,
    };
}

/// <summary>Physical constants of the prototype (SI). Sources in docs/BODYPLAN.md §4.</summary>
public static class Phys
{
    public const float G = 9.81f, RHO_W = 1025f, RHO_A = 1.2f;
    public const float CF = 0.006f;           // skin-friction coefficient at Re ~1e5 (Hoerner 1965; Vogel 1994)
    public const float B0 = 0.3f;             // standard metabolism of an ectotherm, W kg^-0.75 (~20 °C; Clarke & Johnston 1999)
    public const float P_SPEC = 40f;          // sustained mechanical power of aerobic muscle, W/kg (Weis-Fogh & Alexander 1977)
    public const float P_FLIGHT = 150f;       // peak power of flight muscle, W/kg (Pennycuick 2008; Askew & Marsh 2002)
    public const float ETA_M = 0.25f;         // muscle efficiency, mechanical / metabolic
    public const float ASSIM = 0.7f;          // assimilation efficiency
    public const float DIGEST = 5f;           // W of assimilated energy a kg of gut can pass
    public const float SIGMA_MUSCLE = 2e5f;   // isometric stress of muscle, Pa (Alexander 2003)
    public const float DUTY = 0.5f, SAFETY = 2f, KT = 0.1f;   // walking duty factor; bone/cuticle safety factor; cost of support per weight
                                                                        // per stride (Kram & Taylor 1990), set so a 0.2 kg walker with 5 cm strides costs
                                                                        // ~2 J/(N m), Taylor, Heglund & Maloiy's (1982) 10.7 M^-0.32 J/(kg m)
    public const float MU_CRAWL = 0.7f;       // sliding friction of a soft body dragged over rough ground
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
}

public struct MorphEval
{
    public float Fitness, Net, Basal, Intake, Speed, MaxSpeed, Mortality, PKill, MoultShare, O2Supply, O2Demand;
    public bool Walks;
    public float Hold, Support, Stability, FlightMargin;
}

public static class Physics
{
    // ---------- swimming ----------
    public struct SwimProps { public float DragArea, LiftArea, Span, WNet, Cap, Sweep, EtaP; }

    /// <summary>Drag area of the trunk (drag / dynamic pressure, m²): skin friction on its wetted area with Hoerner's
    /// (1965) form factor for fineness f = length / equivalent diameter, C_D,wet = C_f (1 + 1.5 f^-1.5 + 7 f^-3), at most
    /// a bluff body's 1.2 x its frontal area; plus 0.3 x the area of the steps between segments of different section.
    /// Least drag for its volume near f ~ 4-5, as fishes and cetaceans are (Webb 1975; Fish 1993).</summary>
    public static float BodyDragArea(Morphology m)
    {
        float f = MathF.Max(0.05f, m.Fineness), wet = 0f;
        foreach (var s in m.Segs) wet += s.Area;
        float fr = Phys.CF * wet * (1f + 1.5f / (f * MathK.Sqrt(f)) + 7f / (f * f * f));
        return MathF.Min(fr, 1.2f * m.FrontalArea + Phys.CF * wet) + 0.3f * m.StepArea;
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

    public static SwimProps Swim(Morphology m, float rho)
    {
        var p = new SwimProps();
        p.DragArea = BodyDragArea(m);
        float maxReach = 0f, capEta = 0f, tailFin = 0f;
        // where each segment ends along the body (0 head, 1 tail)
        Span<float> end = m.Segs.Count <= 64 ? stackalloc float[m.Segs.Count] : new float[m.Segs.Count];
        float x = 0f;
        for (int i = 0; i < m.Segs.Count; i++) { x += m.Segs[i].L; end[i] = x / MathF.Max(m.Length, 1e-6f); }
        foreach (var a in m.Pairs)
        {
            float flat = a.Chord / a.Thick, cos = a.Sagittal ? 0f : MathK.Cos(a.Theta);
            float cdEdge = 1.1f - 0.8f * flat / (flat + 3f);
            p.DragArea += 2f * (a.Len * a.Thick * cdEdge + 2f * a.Len * a.Chord * Phys.CF);
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
        return p;
    }

    /// <summary>Mechanical power to swim at U (holding its depth); float.PositiveInfinity if it cannot.</summary>
    public static float SwimPower(in SwimProps p, float rho, float u)
    {
        if (p.Cap <= 0f || p.Sweep <= 0f || p.EtaP <= 1e-4f) return u <= 0f && p.WNet <= 0f ? 0f : float.PositiveInfinity;
        float q = 0.5f * rho * u * u;
        float d = q * p.DragArea;
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

    // ---------- walking ----------
    public struct WalkProps { public float WEff, Support, Stride, Stability, LegMass, Cap, Hook, NLegs, Clear; }

    public static WalkProps Walk(Morphology m, float rho)
    {
        var w = new WalkProps { WEff = Phys.G * MathF.Max(0f, m.Mass - rho * m.Volume) };
        float capN = 0f, sw = 0f, sws = 0f;
        foreach (var a in m.Pairs)
        {
            if (a.Sagittal) continue;   // one blade up, one down: not a pair of supports
            var s = m.Segs[a.Seg];
            float sin = MathK.Sin(a.Theta), cos = MathK.Cos(a.Theta);
            float contact = MathF.Min(1f, a.Len * sin / (0.5f * s.H + 0.002f));
            contact *= contact;   // a limb that barely reaches the ground carries little
            if (contact <= 0f) continue;
            float lever = a.Len * (cos + 0.25f);   // the foot's horizontal offset, plus the fore-aft swing of a stride
            float aMus = a.Muscle / (Mat.RHO_TISSUE * MathF.Max(s.L, 1e-3f));
            float fMus = Phys.SIGMA_MUSCLE * aMus * Phys.EMA;   // its muscles must hold the joints against the ground force
            float f = MathF.Min(a.MomentCap / lever, fMus);
            float use = contact * a.Artic;
            capN += 2f * f * use;
            w.NLegs += 2f * use;
            float stride = a.Len * sin * a.Artic;
            sw += f * use; sws += f * use * stride; w.Clear += f * use * a.Len * sin;
            w.LegMass += 2f * a.Mass * contact;
            w.Cap += 2f * a.Muscle * Phys.P_SPEC * use;
            w.Hook += 2f * use * f;
        }
        w.Support = w.WEff > 0f ? MathF.Min(1f, capN * Phys.DUTY / (Phys.SAFETY * w.WEff)) : 1f;
        w.Stride = sw > 0f ? sws / sw : 0f;
        w.Clear = sw > 0f ? w.Clear / sw * w.Support : 0f;   // how far its limbs lift it
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
        float crawlW = MathF.Max(w.WEff, Phys.CRAWL_PRESS * m.Mass * Phys.G);
        float crawlMech = Phys.MU_CRAWL * crawlW * u + drag;
        if (crawlMech <= axialCap + w.Cap) best = crawlMech / Phys.ETA_M;
        if (w.Stride > 1e-3f && w.Cap > 0f)
        {
            float sup = Phys.KT * w.WEff * w.Support * u / w.Stride;
            float swing = 4f * w.LegMass * u * u * u / w.Stride;
            float slide = Phys.MU_CRAWL * MathF.Max(w.WEff * (1f - w.Support), 0f) * u;
            float legMech = swing + slide + drag + sup * Phys.ETA_M;
            float leg = sup + (swing + slide + drag) / Phys.ETA_M;
            if (legMech <= w.Cap + axialCap * (1f - w.Support) && leg < best) { best = leg; legged = true; }
        }
        return best;
    }

    /// <summary>How far an appendage can hold itself out against its own weight in the medium (1 = fully).</summary>
    public static float SelfSupport(Pair a, float rho)
    {
        float wgt = Phys.G * MathF.Max(0f, a.Mass - rho * a.Volume);
        float moment = wgt * 0.5f * a.Len * MathK.Cos(a.Theta);
        return moment <= 0f ? 1f : MathF.Min(1f, a.MomentCap / (Phys.SAFETY * moment));
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

    /// <summary>Energy surplus of a morphology in an environment at the best way and speed of foraging it has, and its
    /// predation risk. Fitness = surplus / its basal metabolism - mortality.</summary>
    public static MorphEval Evaluate(Morphology m, MorphEnv env)
    {
        var e = new MorphEval();
        float rho = env.Rho;
        float basal = Kleiber(m.Mass - m.InertMass);   // mineral does not metabolise
        e.Basal = basal;
        float o2 = O2Supply(m, env.Medium, out float vent);
        float intO2 = m.ExchInt * (env.Medium == Medium.Water ? Phys.S_WATER_INT : Phys.S_AIR_INT);
        float upkeep = Phys.TURNOVER * m.IntMass + Phys.MOULT_COST * m.Rigidity * m.ExoMass;
        float surface = SurfaceUpkeep(m, env.Medium, 0f);
        // lungs lose water with the air they move, so with the oxygen they deliver, not with their capacity
        float lungEvap = env.Medium == Medium.Air && o2 > 0f ? Phys.EVAP_INT * intO2 / o2 : 0f;
        float gutCap = Phys.DIGEST * m.GutMass;
        float capture = 0.5f * MathK.Pow(m.Volume, 2f / 3f);
        // the width it searches on the floor: its mouth and senses, ~ its size. Gathering food with appendages (reaching,
        // sweeping, carrying it to the mouth) is not modelled: an early version let a hair-thin 20 cm arm sweep food for free
        // (docs/BODYPLAN.md §7)
        float swath = Phys.SWATH * MathK.Cbrt(m.Volume);
        e.O2Supply = o2;
        e.FlightMargin = env.Medium == Medium.Air ? FlightMargin(m) : 0f;

        bool water = env.Medium == Medium.Water;
        var sp = Swim(m, rho);
        var wp = Walk(m, rho);
        // crawling (peristalsis, undulation, pedal waves) bends the trunk, as swimming by undulation does
        float axCap = 0f; foreach (var s in m.Segs) axCap += s.AxMuscle * Phys.P_SPEC * 0.5f;
        axCap *= Flex(m);
        float meanH = 0f; foreach (var s in m.Segs) meanH += s.H * s.L; meanH /= MathF.Max(m.Length, 1e-6f);   // its height along its length
        // holding station against surge (on the floor): grip by weight and hooked limbs; a swimmer by its thrust
        float holdWalk = 1f, holdSwim = 1f;
        if (env.HasFloor && env.Surge > 0f)
        {
            float q = 0.5f * rho * env.Surge * env.Surge;
            float side = 0.8f * m.Length * m.MaxH;
            foreach (var a in m.Pairs) side += 2f * a.Len * a.Thick;
            holdWalk = MathF.Min(1f, (0.6f * wp.WEff + wp.Hook) / (q * side));
            float ps = SwimPower(sp, rho, env.Surge);
            holdSwim = float.IsPositiveInfinity(ps) ? 0f : MathF.Min(1f, sp.Cap / MathF.Max(ps, 1e-9f));
        }
        float bestNet = float.NegativeInfinity, maxU = 0f;
        const int NC = 2 * 48;
        Span<float> cNet = stackalloc float[NC], cU = stackalloc float[NC], cIn = stackalloc float[NC], cDem = stackalloc float[NC], cHold = stackalloc float[NC];
        Span<byte> cWalk = stackalloc byte[NC];
        int nc = 0;
        for (int mode = 0; mode < 2; mode++)
        {
            bool walk = mode == 1;
            if (walk && !env.HasFloor) continue;
            if (!walk && !water) continue;
            float hold = walk ? holdWalk : holdSwim;
            foreach (float u in Speeds)
            {
                float loco, duty = 1f, uFactor = 1f;
                if (walk)
                {
                    loco = WalkMetabolic(m, wp, water ? sp.DragArea : 0f, rho, u, axCap, out bool legged);
                    if (float.IsPositiveInfinity(loco)) continue;
                    // obstacles: a body gets over those its own height and its limbs clear and slows or detours for the rest
                    // (Watson et al. 2002); a legged gait on rough ground also needs a tripod down (Full & Koditschek 1999)
                    float clear = 0.5f * meanH + (legged ? wp.Clear : 0f);
                    uFactor = clear / (clear + env.ObstacleH);
                    if (legged) uFactor *= 1f - env.Roughness * (1f - wp.Stability);
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
                if (duty >= 1f && u * uFactor > maxU) maxU = u * uFactor;
                float food = env.FoodInColumn ? env.FoodVol * u * capture : env.FoodArea * u * uFactor * swath;
                float intake = MathF.Min(Phys.ASSIM * food * hold * duty, gutCap);
                float demand = basal + loco + upkeep;
                float net = intake - demand - vent * MathF.Min(demand, o2) - Phys.ANAEROBIC * MathF.Max(0f, demand - o2) - surface - lungEvap * MathF.Min(demand, o2);
                cNet[nc] = net; cU[nc] = u; cIn[nc] = intake; cDem[nc] = demand; cHold[nc] = hold; cWalk[nc] = (byte)(walk ? 1 : 0); nc++;
            }
        }
        // predation: encounters rise with its own speed (Gerritsen & Strickler 1977: ~ sqrt(u² + v²) for a predator
        // searching at v); an attack kills by its armour unless it outruns it (its top speed) and always while it moults
        e.PKill = PKill(m, env.PredBite);
        e.MoultShare = Phys.MOULT_SOFT * m.Rigidity;
        float esc = maxU * maxU / (maxU * maxU + env.PredSpeed * env.PredSpeed);
        float risk = (1f - e.MoultShare) * e.PKill * (1f - esc) + e.MoultShare;
        float bestFit = float.NegativeInfinity;
        for (int i = 0; i < nc; i++)
        {
            float enc = MathK.Sqrt(cU[i] * cU[i] + env.PredSpeed * env.PredSpeed) / env.PredSpeed;
            float f = cNet[i] / basal - env.Predation * enc * risk;
            if (f > bestFit)
            {
                bestFit = f; bestNet = cNet[i]; e.Intake = cIn[i]; e.Speed = cU[i]; e.Walks = cWalk[i] == 1; e.O2Demand = cDem[i]; e.Hold = cHold[i];
                e.Mortality = env.Predation * enc * risk;
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
        e.Net = bestNet;
        e.MaxSpeed = maxU;
        e.Support = wp.Support; e.Stability = wp.Stability;
        e.Fitness = bestFit;
        return e;
    }
}
