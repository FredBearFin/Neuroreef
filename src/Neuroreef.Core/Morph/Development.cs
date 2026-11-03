// Development: genome -> morphology (PROTOTYPE, docs/BODYPLAN.md). A segment gene with repeat n grows n serial
// copies, each scaled by its taper (a positional gradient along the series, as Hox expression grades serial homologues);
// every appendage gene of the segment gene grows a bilateral pair on every copy, scaled with it. The morphology holds
// only physical quantities (metres, kilograms, pascals): what each part can DO is computed from them by Physics.cs.
// All maths through MathK.

using System;
using System.Collections.Generic;

namespace Neuroreef.Morph;

/// <summary>Material and tissue constants (SI). Each is a textbook value, cited in docs/BODYPLAN.md §4.</summary>
public static class Mat
{
    public const float RHO_TISSUE = 1060f;      // soft tissue (Schmidt-Nielsen 1984)
    public const float RHO_SOFT_INTEG = 1100f;  // skin, unmineralised cuticle
    public const float RHO_MINERAL_ADD = 1500f; // a fully mineralised integument or skeleton ~2,600 kg/m³ (calcite, apatite composites)
    public const float SIGMA_SOFT = 0.3e6f;     // failure stress of a soft (hydrostatic) support, Pa
    public const float SIGMA_RATIO = 500f;      // mineralised cuticle / bone ~150 MPa against it (Currey 2002; Vincent 1990)
    public const float MUSCLE_MAX_SHARE = 0.7f; // at most this share of a segment's tissue is muscle
    /// <summary>The most of a segment's or appendage's surface that can be exchange epithelium (phase 4, docs/BODYPLAN.md
    /// §13.11): all of it. It was 0.5, which held a fish's everted (gill) area to 5x its body surface, ~0.2 m²/kg at 3 kg,
    /// against 0.5-1.2 m²/kg in active fishes and up to ~3 in tunas (Gray 1954; Palzenberger &amp; Pohla 1992). At 1 the
    /// cap is ~0.6 m²/kg at 1 kg and the most a gill supplies ~15 W/kg there, a tuna's maximum (Brill 1996: 10-15 W/kg).
    /// A wholly porous surface is wholly unprotected and wholly wet (PKill, evaporation), so the physics prices it.</summary>
    public const float PORE_MAX = 1f;
    public const float FOLD = 10f;              // area amplification of a porous (lamellar, alveolar) exchange surface
    public const float GUT_SHARE = 0.4f;        // of the non-muscle trunk tissue
    public const float JOINT_MASS = 0.04f;      // each joint adds this share of the appendage's mass (hinge, membrane)
    // phase 2b (docs/BODYPLAN.md §11): an internal gas space, any share of a segment's core up to GAS_MAX, held at body
    // pressure; it lightens the body (buoyancy) and, where its lining is folded-in exchange epithelium (the segment's
    // invagination share), it is an air-breathing surface whose area grows with the space's VOLUME (alveolar or
    // tracheal parenchyma), not with the body's surface. SV_GAS: lining area per m³ of gas, in the prototype's area unit
    // (S_AIR_INT = 100 W per m²): mammal lungs hold ~57 mL of air per kg (Schmidt-Nielsen 1984) and support an aerobic
    // scope of ~10 x 3.4 W at 1 kg (Weibel & Hoppeler 2005), 34 W, so 34 / 100 / 5.7e-5 = 5,900 per metre
    public const float GAS_MAX = 0.15f, SV_GAS = 5900f;
    /// <summary>The most of its segment's tissue one pair gene's muscle can take (phase 2b: was 0.3, which held a flier's
    /// flight muscle to ~0.2 of its body against a bird's 0.15-0.35; Greenewalt 1962; Marden 1987).</summary>
    public static readonly float PAIR_MUSCLE = Ablate.Muscle ? 0.3f : 0.6f;
    /// <summary>Fat (phase 2c, docs/BODYPLAN.md §12): up to FAT_MAX of a segment's tissue volume is adipose tissue at
    /// RHO_FAT (~900 kg/m³; Schmidt-Nielsen 1984), as much as a seal's blubber (30-50% of its mass). It is neither gut
    /// nor muscle and metabolises at FAT_MET of the body's average per kg (human adipose ~4.5 kcal/kg/day against
    /// ~24 for the whole body; Elia 1992).</summary>
    public const float FAT_MAX = 0.5f, RHO_FAT = 900f, FAT_MET = 0.2f;
    /// <summary>Body fat of mammals as a share of the mass, 0.075 M^0.19 (fat ~0.075 M^1.19 kg; Pitts &amp; Bullard 1968;
    /// Calder 1984): 5% at 0.1 kg, 7.5% at 1 kg, 12% at 10 kg, at most 0.3.</summary>
    public const float FAT_K = 0.075f, FAT_EXP = 0.19f;
    public static float TypicalFat(float mass) => MathF.Min(0.3f, FAT_K * MathK.Pow(MathF.Max(mass, 1e-4f), FAT_EXP));
    /// <summary>The most hollow an internal skeleton can be: inner over outer diameter K up to 0.9 (bird and pterosaur
    /// long bones; terrestrial mammals ~0.55-0.6; Currey &amp; Alexander 1985).</summary>
    public const float HOLLOW_MAX = 0.9f;
}

public sealed class Segment
{
    public float L, W, H;                // m
    public float IntT, IntMin;           // integument thickness (m), mineral share 0..1
    public float Pore, Invag;            // share of the surface that is exchange epithelium; share of that folded inward
    public float AxShare;                // axial muscle share of its tissue
    public float Gas, GasV;              // share of its core that is an internal gas space; its volume, m³
    public float Fat, FatMass;           // share of its tissue volume that is fat; its mass, kg (TissueMass is the lean rest)
    public float TissueMass, IntMass, AxMuscle, AppMuscle;  // kg
    public float Area;                   // lateral surface, m²
    public float Volume;                 // m³ (tissue + integument)
    public float X0;                     // distance of its front from the head (the mouth), m
    public int Gene;                     // which segment gene grew it
    /// <summary>Every field to zero (a pooled segment reused by Development.Grow with a MorphPool).</summary>
    public void Clear() { L = W = H = IntT = IntMin = Pore = Invag = AxShare = Gas = GasV = Fat = FatMass = TissueMass = IntMass = AxMuscle = AppMuscle = Area = Volume = X0 = 0f; Gene = 0; }
}

public sealed class Pair
{
    public int Seg;                      // the segment it grows on
    public int Gene;                     // which appendage gene (global index) grew it
    public float Len, Chord, Thick;      // m
    public float Theta;                  // droop below the horizontal, rad (0 lateral, pi/2 straight down)
    public bool Sagittal;                // the pair grows in the body's midplane, one blade up and one down, instead of left and right
    public int Joints;
    public float MuscleShare, Muscle;    // of its segment's tissue; kg (housed in the segment), per side
    public float Mineral, Wall, Exo;     // skeleton: mineral share, share of the cross-section that is skeleton, share of that as an outer tube
    public float Hollow;                 // the internal skeleton's inner over outer diameter K (0: a solid rod)
    public float Pore, Invag;
    public float Mass;                   // one side, kg
    public float Volume;                 // one side, m³
    public float MomentCap;              // bending moment one side can bear at its base, N·m
    public float Artic;                  // articulation 0..1 (moving needs joints, or softness)
    public float ExoT;                   // thickness of its outer tube, m (its armour)
    /// <summary>Every field to zero (a pooled pair reused by Development.Grow with a MorphPool).</summary>
    public void Clear() { Seg = Gene = Joints = 0; Len = Chord = Thick = Theta = MuscleShare = Muscle = Mineral = Wall = Exo = Hollow = Pore = Invag = Mass = Volume = MomentCap = Artic = ExoT = 0f; Sagittal = false; }
}

public sealed class Morphology
{
    public readonly List<Segment> Segs = new();
    public readonly List<Pair> Pairs = new();

    // aggregates (Development.Finish)
    public float Mass, Volume, Length, MaxW, MaxH, TailH, Fineness, FrontalArea, StepArea;
    public float WaveLength;                             // the length its mass is spread over (Finish), m: a crawling wave's reach
    public float IntMass, ExoMass, Rigidity;             // whole integument; the mineralised outer share; mass-weighted mineral of the outer skeleton
    public float ExchExt, ExchInt, SkinArea, SkinThick;  // exchange areas (after folding), m²; non-porous skin and its mean thickness
    public float GasV, ExchGas;                          // internal gas volume, m³; the exchange area lining it (air only), m²
    public float GutMass, MuscleMass, TotalArea;
    public float InertMass;                              // the mineral in integument and skeleton: no metabolism of its own
    public float FatMass;                                // adipose tissue, kg: metabolises at Mat.FAT_MET of the average
    /// <summary>The mass Kleiber's metabolism is taken on: less the inert mineral, and less most of the fat beyond what
    /// the animals Kleiber's law was fitted to carry (Mat.TypicalFat: ~0.075 M^1.19 kg; Pitts &amp; Bullard 1968), or more
    /// for a body leaner than them: its lean tissue is the more of its mass.</summary>
    public float ActiveMass => Mass - InertMass - (1f - Mat.FAT_MET) * (FatMass - Mat.TypicalFat(Mass) * Mass);
    public int JointCount;

    /// <summary>Empty it for reuse (Development.Grow with a MorphPool): no parts, every aggregate zero.</summary>
    public void Clear()
    {
        Segs.Clear(); Pairs.Clear();
        Mass = Volume = Length = WaveLength = MaxW = MaxH = TailH = Fineness = FrontalArea = StepArea = IntMass = ExoMass = Rigidity = 0f;
        ExchExt = ExchInt = SkinArea = SkinThick = GasV = ExchGas = GutMass = MuscleMass = TotalArea = InertMass = FatMass = 0f;
        JointCount = 0;
    }

    public int SegmentCount => Segs.Count;
    public int PairCount => Pairs.Count;
}

/// <summary>
/// Reused parts for growing bodies without allocating (the live step loop grows one at every birth: Body.Express,
/// EngineeringTests.StepLoopDoesNotAllocate). One per thread (Pool): its two morphologies are the scale pass and the grown
/// body, so a body grown with a pool is valid only until the next growth on that thread.
/// </summary>
public sealed class MorphPool
{
    public const int MAX_SEGS = MorphGenome.MAX_SEG_GENES * Development.MAX_REPEAT, MAX_PAIRS = MAX_SEGS * MorphGenome.MAX_APP_GENES;
    readonly Morphology[] _m = { NewM(), NewM() };
    readonly Segment[] _s = new Segment[2 * MAX_SEGS];
    readonly Pair[] _p = new Pair[2 * MAX_PAIRS];
    int _ns, _np, _which;
    public MorphPool() { for (int k = 0; k < _s.Length; k++) _s[k] = new Segment(); for (int k = 0; k < _p.Length; k++) _p[k] = new Pair(); }
    static Morphology NewM() { var m = new Morphology(); m.Segs.Capacity = MAX_SEGS; m.Pairs.Capacity = MAX_PAIRS; return m; }
    /// <summary>The next morphology (alternating between the two), emptied, its parts taken from this pool's half.</summary>
    internal Morphology Take()
    {
        _which ^= 1; var m = _m[_which]; m.Clear();
        _ns = _which * MAX_SEGS; _np = _which * MAX_PAIRS;
        return m;
    }
    internal Segment Seg() { var s = _s[_ns++]; s.Clear(); return s; }
    internal Pair Pair() { var p = _p[_np++]; p.Clear(); return p; }
    [ThreadStatic] static MorphPool? _t;
    /// <summary>This thread's pool.</summary>
    public static MorphPool Thread => _t ??= new MorphPool();
}

public static class Development
{
    public const float SEG_L_LO = 0.005f, SEG_L_HI = 0.15f;
    public const float SEG_W_LO = 0.005f, SEG_W_HI = 0.15f;
    public const float INT_REL_LO = 0.01f, INT_REL_HI = 0.3f;      // integument thickness as a share of the segment's half-height
    public const float APP_L_LO = 0.003f, APP_L_HI = 0.2f;
    public const float APP_C_LO = 0.002f, APP_C_HI = 0.1f;
    public const float APP_T_LO = 0.001f, APP_T_HI = 0.05f;
    public const int MAX_REPEAT = 4, MAX_JOINTS = 6;

    static float LogMap(float g, float lo, float hi) => lo * MathK.Exp(g * MathK.Log(hi / lo));

    public static int Repeat(float g) => 1 + Math.Min(MAX_REPEAT - 1, (int)(g * MAX_REPEAT));
    public static int Joints(float g) => Math.Min(MAX_JOINTS, (int)(g * (MAX_JOINTS + 1)));
    public static float Taper(float g) => 0.7f + 0.6f * g;

    /// <summary>Failure stress of a support with this mineral share: log-interpolated, soft to mineralised.</summary>
    public static float Sigma(float mineral) => Mat.SIGMA_SOFT * MathK.Exp(mineral * MathK.Log(Mat.SIGMA_RATIO));
    public static float Density(float soft, float mineral) => soft + Mat.RHO_MINERAL_ADD * mineral;
    /// <summary>Share by mass of an integument or skeleton that is mineral.</summary>
    public static float MineralFraction(float mineral) => Mat.RHO_MINERAL_ADD * mineral / Density(Mat.RHO_SOFT_INTEG, mineral);

    /// <summary>Articulation of an appendage: a rigid one moves only at its joints, a soft one bends anywhere
    /// (a hydrostat) but cannot then bear much load. 0 for a rigid strut with no joints.</summary>
    public static float Articulation(int joints, float mineral) => 1f - MathK.Exp(-(joints + 1.5f * (1f - mineral)) / 1.2f);

    /// <summary>The range of the mass gene (kg), log-mapped. Segment and appendage genes set proportions; the whole body
    /// is then scaled to the mass its mass gene sets, as the live simulation keeps mass a gene of its own and its shape
    /// genes at unit volume (Body.Dimensions). So limbs, armour and gills take their mass from the trunk (and its gut
    /// and muscle), not on top of it. What size pays is decided by the allometry in Physics (Kleiber's metabolism, gut
    /// throughput ~ M^0.75, skin friction by Reynolds number, predators scaled in a size spectrum, obstacles and food
    /// items of fixed absolute size), not by a cap.</summary>
    public const float MASS_LO = 0.001f, MASS_HI = 10f;
    /// <summary>The reference mass the allometric constants are calibrated at (the old fixed mass).</summary>
    public const float M_REF = 0.25f;
    /// <summary>When positive, every body develops to this mass whatever its mass gene (tests and probes).</summary>
    public static float FixedMass = 0f;
    const float TrunkMass = 0.2f;   // first guess, corrected exactly below (every mass scales as the cube of the scale)

    public static float MassOf(MorphGenome g) => FixedMass > 0f ? FixedMass : LogMap(g.MassG, MASS_LO, MASS_HI);

    public static Morphology Grow(MorphGenome g) => Grow(g, MassOf(g));

    /// <summary>Grows the genome's body at an explicit adult mass (kg), whatever its mass gene: the live simulation's
    /// mass range differs from the prototype's gene range, and the shadow expression (Shadow.cs) grows a translated live
    /// genome at the live body's own mass. Pure (no static state), so it is safe on any thread.</summary>
    public static Morphology Grow(MorphGenome g, float massKg) => Grow(g, massKg, null);

    /// <summary>The same, with the parts taken from a pool (no allocation; valid until the next growth with that pool).</summary>
    public static Morphology Grow(MorphGenome g, float massKg, MorphPool? pool)
    {
        var m1 = GrowAt(g, 1f, pool);
        var m = GrowAt(g, m1.Mass > 0f ? MathK.Cbrt(massKg / m1.Mass) : 1f, pool);
        return m;
    }

    static Morphology GrowAt(MorphGenome g, float kScale, MorphPool? pool)
    {
        // pass 1: raw proportions and the scale that gives the trunk its mass
        float raw = 0f;
        foreach (var sg in g.Segs)
        {
            int rep = Repeat(sg.P[SP.REPEAT]);
            float taper = Taper(sg.P[SP.TAPER]), sc = 1f;
            float l = LogMap(sg.P[SP.LEN], SEG_L_LO, SEG_L_HI), w = LogMap(sg.P[SP.WID], SEG_W_LO, SEG_W_HI), h = LogMap(sg.P[SP.HEI], SEG_W_LO, SEG_W_HI);
            for (int k = 0; k < rep; k++) { raw += l * w * h * sc * sc * sc; sc *= taper; }
        }
        raw *= MathF.PI / 4f;
        float kSize = MathK.Cbrt(TrunkMass / Mat.RHO_TISSUE / MathF.Max(raw, 1e-12f)) * kScale;

        var m = pool != null ? pool.Take() : new Morphology();
        int appGene = 0;
        for (int gi = 0; gi < g.Segs.Count; gi++)
        {
            var sg = g.Segs[gi];
            int rep = Repeat(sg.P[SP.REPEAT]);
            float taper = Taper(sg.P[SP.TAPER]);
            float scale = kSize;
            for (int k = 0; k < rep; k++)
            {
                var s = pool != null ? pool.Seg() : new Segment();
                s.Gene = gi;
                s.L = LogMap(sg.P[SP.LEN], SEG_L_LO, SEG_L_HI) * scale;
                s.W = LogMap(sg.P[SP.WID], SEG_W_LO, SEG_W_HI) * scale;
                s.H = LogMap(sg.P[SP.HEI], SEG_W_LO, SEG_W_HI) * scale;
                s.IntMin = sg.P[SP.INT_MIN]; s.Pore = Mat.PORE_MAX * sg.P[SP.PORE]; s.Invag = sg.P[SP.INVAG];
                s.AxShare = 0.4f * sg.P[SP.AXMUS]; s.Gas = Ablate.Gas ? 0f : Mat.GAS_MAX * sg.P[SP.GAS]; s.Fat = Mat.FAT_MAX * sg.P[SP.FAT];
                s.IntT = LogMap(sg.P[SP.INT_T], INT_REL_LO, INT_REL_HI);   // relative here; made absolute in Finish
                float core = s.L * s.W * s.H * (MathF.PI / 4f);
                s.Area = MathF.PI * 0.5f * (s.W + s.H) * s.L;   // lateral surface of an elliptic cylinder (first order); exposed ends added in Finish
                s.GasV = core * s.Gas;
                s.FatMass = (core - s.GasV) * s.Fat * Mat.RHO_FAT;
                s.TissueMass = (core - s.GasV) * (1f - s.Fat) * Mat.RHO_TISSUE;   // the gas space holds no tissue; its fat is neither gut nor muscle
                s.Volume = core;
                int segIndex = m.Segs.Count;
                m.Segs.Add(s);

                float musShareSum = s.AxShare;
                int first = m.Pairs.Count;
                for (int ai = 0; ai < sg.Apps.Count; ai++)
                {
                    var ag = sg.Apps[ai];
                    var p = pool != null ? pool.Pair() : new Pair();
                    p.Seg = segIndex; p.Gene = appGene + ai;
                    p.Len = LogMap(ag.P[AP.LEN], APP_L_LO, APP_L_HI) * scale;
                    p.Chord = LogMap(ag.P[AP.CHORD], APP_C_LO, APP_C_HI) * scale;
                    p.Thick = MathF.Min(LogMap(ag.P[AP.THICK], APP_T_LO, APP_T_HI), LogMap(ag.P[AP.CHORD], APP_C_LO, APP_C_HI)) * scale;   // thickness is the lesser section axis
                    p.Theta = ag.P[AP.DROOP] * (MathF.PI / 2f); p.Sagittal = ag.P[AP.ROLL] >= 0.5f;
                    p.Joints = Joints(ag.P[AP.JOINTS]);
                    p.MuscleShare = Mat.PAIR_MUSCLE * ag.P[AP.MUSCLE];
                    p.Mineral = ag.P[AP.MINERAL]; p.Wall = 0.02f + 0.98f * ag.P[AP.WALL]; p.Exo = ag.P[AP.EXO]; p.Hollow = Mat.HOLLOW_MAX * ag.P[AP.HOLLOW];
                    p.Pore = Mat.PORE_MAX * ag.P[AP.PORE]; p.Invag = ag.P[AP.INVAG];
                    musShareSum += p.MuscleShare;
                    SkeletonOf(p);
                    m.Pairs.Add(p);
                }
                // muscle is housed in the segment: shares beyond MUSCLE_MAX_SHARE are scaled down together
                float k2 = musShareSum > Mat.MUSCLE_MAX_SHARE ? Mat.MUSCLE_MAX_SHARE / musShareSum : 1f;
                s.AxMuscle = s.AxShare * k2 * s.TissueMass;
                for (int q = first; q < m.Pairs.Count; q++) { var p = m.Pairs[q]; p.Muscle = p.MuscleShare * k2 * s.TissueMass * 0.5f; s.AppMuscle += 2f * p.Muscle; }
                scale *= taper;
            }
            appGene += sg.Apps.Count;
        }
        Finish(m);
        return m;
    }

    /// <summary>Mass and bending strength of one side of an appendage: an elliptical section (semi-axes chord/2, thick/2)
    /// whose skeleton area (Wall share of the section) is split into an outer tube (Exo) and a central rod. Bending in
    /// the thin direction (a load across a flat blade): I = pi/4 a b³ for the ellipse, minus the hollow; pi r⁴/4 for the
    /// rod. A tube bears about twice the moment of a rod of the same mass (Alexander 1983; Currey 2002).</summary>
    public static void SkeletonOf(Pair p)
    {
        float a = 0.5f * p.Chord, b = 0.5f * p.Thick;
        float sect = MathF.PI * a * b;
        float sk = p.Wall * sect;
        float tubeArea = p.Exo * sk, rodArea = (1f - p.Exo) * sk;
        float kIn = MathK.Sqrt(MathF.Max(0f, 1f - tubeArea / sect));          // inner ellipse scale
        float iTube = MathF.PI / 4f * a * b * b * b * (1f - kIn * kIn * kIn * kIn);
        // the internal skeleton is a tube of inner/outer diameter K = Hollow (a long bone; K = 0 a solid rod) with the
        // rod's area, its outer radius R = sqrt(A / (pi (1 - K²))) no wider than the limb (then K follows from the area).
        // Its section modulus pi/4 R³ (1 - K⁴) is (1 + K²) / sqrt(1 - K²) times a rod's of the same mass: x1.7 at K 0.6,
        // x4.2 at 0.9 (Currey & Alexander 1985; Alexander 1983). Its bore holds the limb's soft tissue (marrow), which the
        // section already counts, so hollowness costs no mass. Thin walls fail by local buckling only below t/R ~ 0.01
        // (sigma_cr ~ 0.6 E t/R, E ~ 20 GPa for bone; Timoshenko & Gere 1961), thinner than HOLLOW_MAX allows
        float zRod = 0f;
        if (rodArea > 0f)
        {
            float k = p.Hollow, rOut = MathK.Sqrt(rodArea / (MathF.PI * MathF.Max(1e-6f, 1f - k * k)));
            if (rOut > b) { rOut = b; k = MathK.Sqrt(MathF.Max(0f, 1f - rodArea / (MathF.PI * b * b))); }
            float k2 = k * k;
            zRod = MathF.PI / 4f * rOut * rOut * rOut * (1f - k2 * k2);
        }
        float sigma = Sigma(p.Mineral);
        p.MomentCap = sigma * (iTube / MathF.Max(b, 1e-6f) + zRod);
        p.ExoT = (1f - kIn) * b;
        float rhoSk = Density(Mat.RHO_SOFT_INTEG, p.Mineral);
        p.Volume = p.Len * sect;
        p.Mass = p.Len * ((sect - sk) * Mat.RHO_TISSUE + sk * rhoSk) * (1f + Mat.JOINT_MASS * p.Joints);
        p.Artic = Articulation(p.Joints, p.Mineral);
    }

    /// <summary>Exchange area lining a m³ of gas space wholly made of respiratory parenchyma, at body mass M (kg): SV_GAS at
    /// 1 kg, finer in smaller bodies as M^-0.11 (alveolar surface ~M^0.95, Gehr et al. 1981, against lung volume ~M^1.06,
    /// Stahl 1967: smaller alveoli in smaller mammals).</summary>
    public static float GasLining(float mass) => Mat.SV_GAS * MathK.Pow(MathF.Max(mass, 1e-6f), -0.11f);

    static void Finish(Morphology m)
    {
        float aPrev = 0f, skinT = 0f;
        float rigW = 0f;
        float bodyLen = 0f;
        foreach (var s in m.Segs) { s.X0 = bodyLen; bodyLen += s.L; }
        for (int i = 0; i < m.Segs.Count; i++)
        {
            // exposed ends: the whole face of the first and last segment, and the step where a segment is wider than its neighbour
            var s = m.Segs[i];
            float a = MathF.PI / 4f * s.W * s.H;
            float front = i == 0 ? a : MathF.Max(0f, a - MathF.PI / 4f * m.Segs[i - 1].W * m.Segs[i - 1].H);
            float back = i == m.Segs.Count - 1 ? a : MathF.Max(0f, a - MathF.PI / 4f * m.Segs[i + 1].W * m.Segs[i + 1].H);
            s.Area += front + back;
            // integument thickness: a share of the body's least half-dimension there
            s.IntT *= 0.5f * MathF.Min(bodyLen, MathF.Min(s.W, s.H));
            s.IntMass = s.Area * s.IntT * Density(Mat.RHO_SOFT_INTEG, s.IntMin);
            s.Volume += s.Area * s.IntT;
        }
        foreach (var s in m.Segs)
        {
            m.Mass += s.TissueMass + s.FatMass + s.IntMass; m.FatMass += s.FatMass;
            m.Volume += s.Volume;
            m.Length += s.L;
            m.MaxW = MathF.Max(m.MaxW, s.W); m.MaxH = MathF.Max(m.MaxH, s.H);
            float a = MathF.PI / 4f * s.W * s.H;
            if (aPrev > 0f) m.StepArea += MathF.Abs(a - aPrev);
            aPrev = a;
            float ex = s.Pore * s.Area * Mat.FOLD;
            m.ExchExt += ex * (1f - s.Invag); m.ExchInt += ex * s.Invag;
            m.GasV += s.GasV; m.ExchGas += s.Invag * s.GasV;   // x the lining per m³ below
            float skin = (1f - s.Pore) * s.Area;
            m.SkinArea += skin; skinT += skin * s.IntT;
            m.IntMass += s.IntMass;
            m.ExoMass += s.IntMass; rigW += s.IntMass * s.IntMin;
            m.InertMass += s.IntMass * MineralFraction(s.IntMin);
            m.GutMass += Mat.GUT_SHARE * (s.TissueMass - s.AxMuscle - s.AppMuscle);
            m.MuscleMass += s.AxMuscle + s.AppMuscle;
            m.TotalArea += s.Area;
        }
        m.ExchGas *= GasLining(m.Mass);
        m.TailH = m.Segs.Count > 0 ? m.Segs[^1].H : 0f;
        foreach (var p in m.Pairs)
        {
            m.Mass += 2f * p.Mass;
            m.Volume += 2f * p.Volume;
            float area = 2f * p.Len * (p.Chord + p.Thick);   // both faces of one side, approximately
            float ex = p.Pore * area * Mat.FOLD;
            m.ExchExt += 2f * ex * (1f - p.Invag); m.ExchInt += 2f * ex * p.Invag;
            float skin = 2f * (1f - p.Pore) * area;
            m.SkinArea += skin; skinT += skin * MathF.Max(p.ExoT, 0.0002f);
            float exoMass = 2f * p.Len * p.Exo * p.Wall * MathF.PI * 0.25f * p.Chord * p.Thick * Density(Mat.RHO_SOFT_INTEG, p.Mineral);
            m.IntMass += exoMass; m.ExoMass += exoMass; rigW += exoMass * p.Mineral;
            m.InertMass += 2f * p.Len * p.Wall * MathF.PI * 0.25f * p.Chord * p.Thick * Density(Mat.RHO_SOFT_INTEG, p.Mineral) * MineralFraction(p.Mineral);
            m.JointCount += 2 * p.Joints;
            m.TotalArea += 2f * area;
        }
        m.SkinThick = m.SkinArea > 0f ? skinT / m.SkinArea : 0f;
        m.Rigidity = m.ExoMass > 0f ? rigW / m.ExoMass : 0f;
        // the largest cross-section of any one segment (MaxW x MaxH from different segments overstated it)
        float wh = 0f;
        foreach (var s in m.Segs) wh = MathF.Max(wh, s.W * s.H);
        m.FrontalArea = MathF.PI / 4f * wh;
        m.Fineness = m.Length / MathF.Max(1e-6f, MathK.Sqrt(wh));
        // the length its mass is spread over, (sum m)² / sum(m²/L): the whole length for a body of even thickness, about
        // the head's for a head on a weightless thread. A crawling wave pushes on the ground with the body's weight and
        // muscle, so a thread that has neither does not lengthen it (it made a 1.4 g head with a 10 cm thread of 0.4 mm
        // the cheapest crawler; docs/BODYPLAN.md §8)
        float sm = 0f, sm2 = 0f;
        foreach (var s in m.Segs) { float ms = s.TissueMass + s.FatMass + s.IntMass; sm += ms; sm2 += ms * ms / MathF.Max(s.L, 1e-6f); }
        m.WaveLength = sm2 > 0f ? MathF.Min(m.Length, sm * sm / sm2) : m.Length;
    }
}
