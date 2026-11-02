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
    public const float FOLD = 10f;              // area amplification of a porous (lamellar, alveolar) exchange surface
    public const float GUT_SHARE = 0.4f;        // of the non-muscle trunk tissue
    public const float JOINT_MASS = 0.04f;      // each joint adds this share of the appendage's mass (hinge, membrane)
}

public sealed class Segment
{
    public float L, W, H;                // m
    public float IntT, IntMin;           // integument thickness (m), mineral share 0..1
    public float Pore, Invag;            // share of the surface that is exchange epithelium; share of that folded inward
    public float AxShare;                // axial muscle share of its tissue
    public float TissueMass, IntMass, AxMuscle, AppMuscle;  // kg
    public float Area;                   // lateral surface, m²
    public float Volume;                 // m³ (tissue + integument)
    public float X0;                     // distance of its front from the head (the mouth), m
    public int Gene;                     // which segment gene grew it
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
    public float Pore, Invag;
    public float Mass;                   // one side, kg
    public float Volume;                 // one side, m³
    public float MomentCap;              // bending moment one side can bear at its base, N·m
    public float Artic;                  // articulation 0..1 (moving needs joints, or softness)
    public float ExoT;                   // thickness of its outer tube, m (its armour)
}

public sealed class Morphology
{
    public readonly List<Segment> Segs = new();
    public readonly List<Pair> Pairs = new();

    // aggregates (Development.Finish)
    public float Mass, Volume, Length, MaxW, MaxH, TailH, Fineness, FrontalArea, StepArea;
    public float IntMass, ExoMass, Rigidity;             // whole integument; the mineralised outer share; mass-weighted mineral of the outer skeleton
    public float ExchExt, ExchInt, SkinArea, SkinThick;  // exchange areas (after folding), m²; non-porous skin and its mean thickness
    public float GutMass, MuscleMass, TotalArea;
    public float InertMass;                              // the mineral in integument and skeleton: no metabolism of its own
    public int JointCount;

    public int SegmentCount => Segs.Count;
    public int PairCount => Pairs.Count;
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

    public static Morphology Grow(MorphGenome g)
    {
        var m1 = GrowAt(g, 1f);
        var m = GrowAt(g, m1.Mass > 0f ? MathK.Cbrt(MassOf(g) / m1.Mass) : 1f);
        return m;
    }

    static Morphology GrowAt(MorphGenome g, float kScale)
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

        var m = new Morphology();
        int appGene = 0;
        for (int gi = 0; gi < g.Segs.Count; gi++)
        {
            var sg = g.Segs[gi];
            int rep = Repeat(sg.P[SP.REPEAT]);
            float taper = Taper(sg.P[SP.TAPER]);
            float scale = kSize;
            for (int k = 0; k < rep; k++)
            {
                var s = new Segment
                {
                    Gene = gi,
                    L = LogMap(sg.P[SP.LEN], SEG_L_LO, SEG_L_HI) * scale,
                    W = LogMap(sg.P[SP.WID], SEG_W_LO, SEG_W_HI) * scale,
                    H = LogMap(sg.P[SP.HEI], SEG_W_LO, SEG_W_HI) * scale,
                    IntMin = sg.P[SP.INT_MIN], Pore = 0.5f * sg.P[SP.PORE], Invag = sg.P[SP.INVAG],
                    AxShare = 0.4f * sg.P[SP.AXMUS],
                };
                s.IntT = LogMap(sg.P[SP.INT_T], INT_REL_LO, INT_REL_HI);   // relative here; made absolute in Finish
                float core = s.L * s.W * s.H * (MathF.PI / 4f);
                s.Area = MathF.PI * 0.5f * (s.W + s.H) * s.L;   // lateral surface of an elliptic cylinder (first order); exposed ends added in Finish
                s.TissueMass = core * Mat.RHO_TISSUE;
                s.Volume = core;
                int segIndex = m.Segs.Count;
                m.Segs.Add(s);

                float musShareSum = s.AxShare;
                int first = m.Pairs.Count;
                for (int ai = 0; ai < sg.Apps.Count; ai++)
                {
                    var ag = sg.Apps[ai];
                    var p = new Pair
                    {
                        Seg = segIndex, Gene = appGene + ai,
                        Len = LogMap(ag.P[AP.LEN], APP_L_LO, APP_L_HI) * scale,
                        Chord = LogMap(ag.P[AP.CHORD], APP_C_LO, APP_C_HI) * scale,
                        Thick = MathF.Min(LogMap(ag.P[AP.THICK], APP_T_LO, APP_T_HI), LogMap(ag.P[AP.CHORD], APP_C_LO, APP_C_HI)) * scale,   // thickness is the lesser section axis
                        Theta = ag.P[AP.DROOP] * (MathF.PI / 2f), Sagittal = ag.P[AP.ROLL] >= 0.5f,
                        Joints = Joints(ag.P[AP.JOINTS]),
                        MuscleShare = 0.3f * ag.P[AP.MUSCLE],
                        Mineral = ag.P[AP.MINERAL], Wall = 0.02f + 0.98f * ag.P[AP.WALL], Exo = ag.P[AP.EXO],
                        Pore = 0.5f * ag.P[AP.PORE], Invag = ag.P[AP.INVAG],
                    };
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
        float rRod = MathF.Min(b, MathK.Sqrt(rodArea / MathF.PI));
        float iRod = MathF.PI / 4f * rRod * rRod * rRod * rRod;
        float sigma = Sigma(p.Mineral);
        p.MomentCap = sigma * (iTube / MathF.Max(b, 1e-6f) + (rRod > 0f ? iRod / rRod : 0f));
        p.ExoT = (1f - kIn) * b;
        float rhoSk = Density(Mat.RHO_SOFT_INTEG, p.Mineral);
        p.Volume = p.Len * sect;
        p.Mass = p.Len * ((sect - sk) * Mat.RHO_TISSUE + sk * rhoSk) * (1f + Mat.JOINT_MASS * p.Joints);
        p.Artic = Articulation(p.Joints, p.Mineral);
    }

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
            m.Mass += s.TissueMass + s.IntMass;
            m.Volume += s.Volume;
            m.Length += s.L;
            m.MaxW = MathF.Max(m.MaxW, s.W); m.MaxH = MathF.Max(m.MaxH, s.H);
            float a = MathF.PI / 4f * s.W * s.H;
            if (aPrev > 0f) m.StepArea += MathF.Abs(a - aPrev);
            aPrev = a;
            float ex = s.Pore * s.Area * Mat.FOLD;
            m.ExchExt += ex * (1f - s.Invag); m.ExchInt += ex * s.Invag;
            float skin = (1f - s.Pore) * s.Area;
            m.SkinArea += skin; skinT += skin * s.IntT;
            m.IntMass += s.IntMass;
            m.ExoMass += s.IntMass; rigW += s.IntMass * s.IntMin;
            m.InertMass += s.IntMass * MineralFraction(s.IntMin);
            m.GutMass += Mat.GUT_SHARE * (s.TissueMass - s.AxMuscle - s.AppMuscle);
            m.MuscleMass += s.AxMuscle + s.AppMuscle;
            m.TotalArea += s.Area;
        }
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
        m.FrontalArea = MathF.PI / 4f * m.MaxW * m.MaxH;
        m.Fineness = m.Length / MathF.Max(1e-6f, MathK.Sqrt(m.MaxW * m.MaxH));
    }
}
