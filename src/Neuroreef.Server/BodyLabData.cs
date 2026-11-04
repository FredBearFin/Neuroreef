// The Body Lab's record of a run of the body-plan prototype (docs/BODYPLAN.md, Neuroreef.Morph): what the lab writes to
// bodylab-<env>-<seed>.json and what its page (wwwroot/bodylab.html) draws. Each body carries its genome (exactly, so a
// file can be grown and evaluated again), the physical quantities development gave it, and what each part does by the
// physics evaluators' numbers (thrust share, share of the weight its limbs bear, crushing force, share of the oxygen,
// the chance a bite through it kills). No role here comes from a shape: a part is a "paddle" only if the swimming
// physics gives it thrust.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Neuroreef.Morph;

namespace Neuroreef.Server;

public sealed class LabFile
{
    public string Format { get; set; } = "neuroreef-bodylab";
    public int Version { get; set; } = 1;
    public string Env { get; set; } = "";      // the environment's name ("sea floor")
    public string EnvKey { get; set; } = "";   // its option value ("sea-floor")
    public uint Seed { get; set; }
    public int Population { get; set; }
    public int Generations { get; set; }
    public int Every { get; set; }
    public string Created { get; set; } = "";
    public bool Done { get; set; }
    /// <summary>The champion (and each species' best) every Every generations, from generation 0.</summary>
    public List<LabSnapshot> Snapshots { get; set; } = new();
    /// <summary>The final generation: its champion and the best of every species, best first.</summary>
    public LabSnapshot? Final { get; set; }
    public string? Table { get; set; }
    /// <summary>Set for a file exported from a live world (--world-bodies): "world". The bodies are its real animals, each
    /// lineage's first adult, grown at its own mass; Gen is the world's step and Best/Mean its lineage sizes.</summary>
    public string? Source { get; set; }
    public string? Detail { get; set; }
}

public sealed class LabSnapshot
{
    public int Gen { get; set; }
    public float Best { get; set; }
    public float Mean { get; set; }
    public int Species { get; set; }
    public LabBody Champion { get; set; } = new();
    public List<LabBody> Reps { get; set; } = new();
}

public sealed class LabGenome
{
    public float MassG { get; set; }
    public List<LabSegGene> Segs { get; set; } = new();

    public static LabGenome From(MorphGenome g) => new()
    {
        MassG = g.MassG,
        Segs = g.Segs.Select(s => new LabSegGene { Innov = s.Innov, P = (float[])s.P.Clone(), Apps = s.Apps.Select(a => new LabAppGene { Innov = a.Innov, P = (float[])a.P.Clone() }).ToList() }).ToList(),
    };

    public MorphGenome ToGenome()
    {
        var g = new MorphGenome { MassG = MassG };
        foreach (var s in Segs)
        {
            var sg = new SegGene { Innov = s.Innov };
            Array.Copy(s.P, sg.P, Math.Min(s.P.Length, SP.N));
            foreach (var a in s.Apps)
            {
                var ag = new AppGene { Innov = a.Innov };
                Array.Copy(a.P, ag.P, Math.Min(a.P.Length, AP.N));
                sg.Apps.Add(ag);
            }
            g.Segs.Add(sg);
        }
        return g;
    }
}
public sealed class LabSegGene { public uint Innov { get; set; } public float[] P { get; set; } = Array.Empty<float>(); public List<LabAppGene> Apps { get; set; } = new(); }
public sealed class LabAppGene { public uint Innov { get; set; } public float[] P { get; set; } = Array.Empty<float>(); }

/// <summary>One role a part plays, by the physics: its share (0..1) of that function in the whole body, and a sentence.</summary>
public sealed class LabRole
{
    public string Role { get; set; } = "";   // thrust, steering, support, skeleton (internal), crawling, crushing, striking, gas, protection, housing, passive
    public float Share { get; set; }
    public string Text { get; set; } = "";
}

public sealed class LabSeg
{
    public int Gene { get; set; }
    public float L { get; set; }
    public float W { get; set; }
    public float H { get; set; }
    public float X0 { get; set; }
    public float IntT { get; set; }      // integument thickness, m
    public float IntMin { get; set; }    // its mineral share 0..1 (rigidity)
    public float Pore { get; set; }      // share of its surface that is exchange epithelium
    public float Invag { get; set; }     // share of that folded inward
    public float AxShare { get; set; }   // axial muscle share of its tissue
    public float AxSk { get; set; }      // internal (axial) skeleton: its share of the cross-section (0 none; docs/BODYPLAN.md §13.19)
    public float AxMin { get; set; }     // its mineral share (cartilage 0 to bone 1)
    public float EndoMass { get; set; }  // its mass, kg
    public float BendCap { get; set; }   // N m: the bending moment its section bears (integument, internal skeleton, muscle)
    public float ExchExt { get; set; }   // m² (after folding)
    public float ExchInt { get; set; }
    public float Kill { get; set; }      // chance a predator's bite through it kills
    public string Primary { get; set; } = "";
    public List<LabRole> Roles { get; set; } = new();
    [JsonIgnore] public string Caption { get; set; } = "";   // the roles' sentences joined (the page joins them itself)
}

public sealed class LabPair
{
    public int Seg { get; set; }
    public int Gene { get; set; }
    public float Len { get; set; }
    public float Chord { get; set; }
    public float Thick { get; set; }
    public float Theta { get; set; }     // droop below the horizontal, rad
    public bool Sagittal { get; set; }
    public int Joints { get; set; }
    public float MuscleShare { get; set; }
    public float Mineral { get; set; }
    public float Wall { get; set; }
    public float Exo { get; set; }
    public float ExoT { get; set; }
    public float Pore { get; set; }
    public float Invag { get; set; }
    public float MomentCap { get; set; }  // N m one side bears at its base
    public float Artic { get; set; }
    public float ExchExt { get; set; }
    public float ExchInt { get; set; }
    public float Kill { get; set; }
    public string Use { get; set; } = "";  // Physics.PairUses
    public string Primary { get; set; } = "";
    public List<LabRole> Roles { get; set; } = new();
    [JsonIgnore] public string Caption { get; set; } = "";   // the roles' sentences joined (the page joins them itself)
}

public sealed class LabBody
{
    public string? Lineage { get; set; }   // its lineage's name, for a body from a live world
    public LabGenome Genome { get; set; } = new();
    public float Fitness { get; set; }
    public int Size { get; set; }          // members of its species (0 for a lone champion)
    public float Mass { get; set; }
    public float Length { get; set; }
    public int Segments { get; set; }
    public int PairCount { get; set; }
    public int JointCount { get; set; }
    public string Mode { get; set; } = "";
    public float Speed { get; set; }
    public float Rigidity { get; set; }
    public float IntShare { get; set; }
    public float EndoShare { get; set; }   // its trunk's internal skeleton's mass over its mass (docs/BODYPLAN.md §13.19)
    public float Support { get; set; }     // share of its weight its limbs bear on this ground (its trunk's bending included)
    public float Moult { get; set; }
    public float ExchExt { get; set; }
    public float ExchInt { get; set; }
    public float Crush { get; set; }
    public float ItemShare { get; set; }
    public float PKill { get; set; }
    public float Escape { get; set; }
    public float Mortality { get; set; }
    public float TurnR { get; set; }
    public float O2Supply { get; set; }
    public float O2Demand { get; set; }
    public float SkinO2 { get; set; }      // share of its oxygen through plain skin
    /// <summary>The chance it tips over on rough ground (BodyPose.Topple, as Morph.Live.StandPosture weights it): above
    /// 0.5 a body on the ground is drawn lying on its side (2026-11-03).</summary>
    public float Topple { get; set; }
    public string Summary { get; set; } = "";
    public List<LabSeg> Segs { get; set; } = new();
    public List<LabPair> Pairs { get; set; } = new();
}

/// <summary>Builds the lab's records from the prototype's own physics (it changes nothing in it).</summary>
public static class LabReport
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static readonly string[] EnvKeys = { "open-water", "sea-floor", "land" };
    public static MorphEnv EnvOf(string key) => key switch
    {
        "open-water" => MorphEnv.OpenWater(),
        "sea-floor" => MorphEnv.SeaFloor(),
        "land" => MorphEnv.Land(),
        _ => throw new ArgumentException("--env open-water|sea-floor|land|all"),
    };

    /// <summary>A number for display, to 4 significant figures (genomes are written exactly).</summary>
    static float R(float x)
    {
        if (!float.IsFinite(x) || x == 0f) return 0f;
        return float.Parse(x.ToString("G4", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    static string Pct(float x) => (100f * x).ToString(x < 0.1f ? "F1" : "F0", CultureInfo.InvariantCulture) + "%";
    static string N(float x, string f = "F1") => x.ToString(f, CultureInfo.InvariantCulture);
    static string Mm(float m) => m >= 0.1f ? N(100f * m, "F1") + " cm" : N(1000f * m, m >= 0.01f ? "F0" : "F1") + " mm";

    // the cross-section of a pair's muscle (m²): the same formula as Physics.MuscleArea (private there)
    static float MuscleArea(Morphology m, Pair a) => a.Muscle / (Mat.RHO_TISSUE * MathF.Max(m.Segs[a.Seg].L, 1e-3f));

    public static LabBody Body(MorphGenome g, MorphEnv env, int size = 0, float massKg = 0f)
    {
        var m = massKg > 0f ? Development.Grow(g, massKg) : Development.Grow(g);
        var e = Physics.Evaluate(m, env);
        var b = new LabBody
        {
            Genome = LabGenome.From(g), Fitness = R(e.Fitness), Size = size,
            Mass = R(m.Mass), Length = R(m.Length), Segments = m.SegmentCount, PairCount = m.PairCount, JointCount = m.JointCount,
            Mode = Evolver.Mode(e), Speed = R(e.Speed), Rigidity = R(m.Rigidity), IntShare = R(m.IntMass / m.Mass), EndoShare = R(m.EndoMass / m.Mass), Support = R(e.Support), Moult = R(e.MoultShare),
            ExchExt = R(m.ExchExt), ExchInt = R(m.ExchInt), Crush = R(e.CrushForce), ItemShare = R(e.ItemShare), PKill = R(e.PKill),
            Escape = R(e.Escape), Mortality = R(e.Mortality), TurnR = R(MathF.Min(e.TurnR, 99f)), O2Supply = R(e.O2Supply), O2Demand = R(e.O2Demand),
            Topple = R(BodyPose.Topple(m)),
        };

        bool water = env.Medium == Medium.Water;
        bool swims = !e.Walks && water, legged = e.Walks && e.Legged, crawls = e.Walks && !e.Legged;
        float sExt = water ? Phys.S_WATER_EXT : Phys.S_AIR_EXT, sInt = water ? Phys.S_WATER_INT : Phys.S_AIR_INT;
        float o2 = MathF.Max(e.O2Supply, 1e-12f);
        float skinO2 = m.SkinArea * sExt * Phys.SKIN_EX * MathK.Exp(-m.SkinThick / Phys.SKIN_T0);
        b.SkinO2 = R(skinO2 / o2);
        float sizeK = m.Mass / Development.M_REF;
        float bite = env.PredBite * MathK.Pow(sizeK, 2f / 3f);
        var uses = Physics.PairUses(m, env, e);
        var thrust = Physics.ThrustShares(m);
        var ctl = water ? Physics.ControlAreas(m, env.Rho, 1f) : new float[m.Pairs.Count + 1];
        float ctlTot = 0f; foreach (var c in ctl) ctlTot += c;
        var hd = Physics.Handle(m, env.Rho, env.ItemEscape);
        bool items = e.ItemShare > 0.05f;

        // the share of the weight each pair's limbs bear (as Physics.PairUses)
        var sup = new float[m.Pairs.Count];
        float supTot = 0f;
        for (int i = 0; i < m.Pairs.Count; i++)
        {
            var a = m.Pairs[i];
            if (a.Sagittal) continue;
            var s = m.Segs[a.Seg];
            float sin = MathK.Sin(a.Theta), cos = MathK.Cos(a.Theta);
            float contact = MathF.Min(1f, a.Len * sin / (0.5f * s.H + 0.002f)); contact *= contact;
            sup[i] = Physics.LimbForce(m, a, a.Len * (cos + 0.25f)) * contact * a.Artic;
            supTot += sup[i];
        }
        float axTot = 0f; foreach (var s in m.Segs) axTot += s.AxMuscle;

        for (int i = 0; i < m.Segs.Count; i++)
        {
            var s = m.Segs[i];
            float ex = s.Pore * s.Area * Mat.FOLD;
            float ext = ex * (1f - s.Invag), inn = ex * s.Invag;
            float f = 4f * Development.Sigma(s.IntMin) * s.IntT * s.IntT / bite;
            float pk = 1f / (1f + f), open = s.Pore * (1f - s.Invag);
            var ls = new LabSeg
            {
                Gene = s.Gene, L = R(s.L), W = R(s.W), H = R(s.H), X0 = R(s.X0), IntT = R(s.IntT), IntMin = R(s.IntMin), Pore = R(s.Pore), Invag = R(s.Invag),
                AxShare = R(s.AxShare), AxSk = R(s.AxSk), AxMin = R(s.AxMin), EndoMass = R(s.EndoMass), BendCap = R(s.BendCap),
                ExchExt = R(ext), ExchInt = R(inn), Kill = R(open + (1f - open) * pk),
            };
            // its internal skeleton (docs/BODYPLAN.md §13.19): what share of the section's bending strength it gives, which
            // carries the trunk between its limbs and anchors their moments (Physics.TrunkShare, LimbForce)
            if (s.EndoMass > 0f && s.BendCap > 0f && s.BendEndo / s.BendCap >= 0.1f)
                ls.Roles.Add(new LabRole
                {
                    Role = "skeleton", Share = R(s.BendEndo / s.BendCap),
                    Text = $"Support (internal skeleton): a {(s.AxMin > 0.5f ? "bony" : s.AxMin > 0.15f ? "half-mineralised" : "cartilaginous")} rod of {Pct(s.AxSk)} of its section ({N(1000f * s.EndoMass, "F1")} g) gives {Pct(s.BendEndo / s.BendCap)} of the {N(s.BendCap, "F2")} N m its trunk bears here in bending" +
                        (legged ? $"; the limbs bear {Pct(e.Support)} of the weight" : "") + ". It grows with the body, unmoulted.",
                });
            float axShare = axTot > 0f ? s.AxMuscle / axTot : 0f;
            if (swims && thrust[^1] * axShare >= 0.05f)
                ls.Roles.Add(new LabRole { Role = "thrust", Share = R(thrust[^1] * axShare), Text = $"Thrust: its axial muscle bends the trunk for {Pct(thrust[^1] * axShare)} of the swimming power." });
            if (swims && ctlTot > 0f && ctl[^1] * axShare / ctlTot >= 0.1f)
                ls.Roles.Add(new LabRole { Role = "steering", Share = R(ctl[^1] * axShare / ctlTot), Text = $"Steering: bending, it gives {Pct(ctl[^1] * axShare / ctlTot)} of the turning force." });
            if (crawls && axShare >= 0.05f)
                ls.Roles.Add(new LabRole { Role = "crawling", Share = R(axShare), Text = $"Crawling: {Pct(axShare)} of the axial muscle that drives the body wave over the ground." });
            AddGas(ls.Roles, ext * sExt + inn * sInt, o2, inn / MathF.Max(ex, 1e-12f), ex > 0f);
            AddArmour(ls.Roles, pk, s.IntMin, s.IntT, false);
            float tissue = s.TissueMass / m.Mass;
            if ((ls.Roles.Count == 0 || ls.Roles.All(r => r.Role is "protection" or "skeleton")) && tissue >= 0.02f)
                ls.Roles.Add(new LabRole { Role = "housing", Share = R(tissue), Text = $"Housing: {Pct(tissue)} of the body's tissue (gut and muscle) is here." });
            else if (ls.Roles.Count == 0 || ls.Roles.All(r => r.Role is "protection" or "skeleton"))
                ls.Roles.Add(new LabRole { Role = "passive", Share = 0f, Text = $"Passive: the physics gives it no measurable thrust, crawling, steering or gas exchange, and it holds only {Pct(tissue)} of the body's tissue." });
            Finish(ls.Roles, out var prim, out var cap); ls.Primary = prim; ls.Caption = cap;
            b.Segs.Add(ls);
        }

        for (int i = 0; i < m.Pairs.Count; i++)
        {
            var a = m.Pairs[i];
            float area = 2f * a.Len * (a.Chord + a.Thick);
            float ex = a.Pore * area * Mat.FOLD;
            float ext = 2f * ex * (1f - a.Invag), inn = 2f * ex * a.Invag;
            float t = MathF.Max(a.ExoT * a.Exo, 1e-5f);
            float f = 4f * Development.Sigma(a.Mineral * a.Exo) * t * t / bite;
            float pk = 1f / (1f + f), open = a.Pore * (1f - a.Invag);
            var lp = new LabPair
            {
                Seg = a.Seg, Gene = a.Gene, Len = R(a.Len), Chord = R(a.Chord), Thick = R(a.Thick), Theta = R(a.Theta), Sagittal = a.Sagittal, Joints = a.Joints,
                MuscleShare = R(a.MuscleShare), Mineral = R(a.Mineral), Wall = R(a.Wall), Exo = R(a.Exo), ExoT = R(a.ExoT), Pore = R(a.Pore), Invag = R(a.Invag),
                MomentCap = R(a.MomentCap), Artic = R(a.Artic), ExchExt = R(ext), ExchInt = R(inn), Kill = R(open + (1f - open) * pk), Use = uses[i],
            };
            if (items && i == hd.Handler && !env.FoodInColumn)
            {
                float opened = env.ItemFHi > 0f ? Physics.Openable(e.CrushForce, env.ItemFLo, env.ItemFHi, out _) : 1f;
                lp.Roles.Add(new LabRole { Role = "crushing", Share = R(e.ItemShare), Text = $"Handling and crushing: it closes on food items at the mouth with {N(e.CrushForce)} N, enough to open {Pct(opened)} of the shells here; items are {Pct(e.ItemShare)} of what it eats." });
            }
            if (items && i == hd.Striker && env.FoodInColumn)
                lp.Roles.Add(new LabRole { Role = "striking", Share = R(e.ItemShare), Text = $"Handling: it strikes {Mm(e.Reach)} beyond the mouth at evasive prey (caught {Pct(e.CaptureP)} of the chases); prey is {Pct(e.ItemShare)} of what it eats." });
            if (swims && thrust[i] >= 0.05f)
                lp.Roles.Add(new LabRole { Role = "thrust", Share = R(thrust[i]), Text = $"Thrust: {Pct(thrust[i])} of the swimming power (aspect ratio {N(2f * a.Len / a.Chord)}, {N(a.Chord / a.Thick)}x as wide as thick)." });
            if (swims && ctlTot > 0f && ctl[i] / ctlTot >= 0.1f)
                lp.Roles.Add(new LabRole { Role = "steering", Share = R(ctl[i] / ctlTot), Text = $"Steering: {Pct(ctl[i] / ctlTot)} of the turning force its muscles can hold the surfaces against." });
            if (legged && supTot > 0f && sup[i] / supTot >= 0.05f)
                lp.Roles.Add(new LabRole { Role = "support", Share = R(sup[i] / supTot), Text = $"Support: {Pct(sup[i] / supTot)} of the load the limbs bear in walking (each side bears {a.MomentCap:0.##E+0} N m at its base, {a.Joints} joints)." });
            AddGas(lp.Roles, ext * sExt + inn * sInt, o2, inn / MathF.Max(ex, 1e-12f), ex > 0f);
            AddArmour(lp.Roles, pk, a.Mineral * a.Exo, a.ExoT * a.Exo, true);
            if (lp.Roles.Count == 0 || lp.Roles.All(r => r.Role == "protection"))
                lp.Roles.Add(new LabRole { Role = "passive", Share = 0f, Text = "Passive: the physics gives it no measurable thrust, support, steering, handling or gas exchange (it costs mass and drag)." });
            Finish(lp.Roles, out var prim, out var cap); lp.Primary = prim; lp.Caption = cap;
            b.Pairs.Add(lp);
        }

        b.Summary = $"{Kg(m.Mass)}, {Mm(m.Length)} long, {m.SegmentCount} segments, {m.PairCount} appendage pairs; {Evolver.Mode(e)} at {N(e.Speed, "F2")} m/s. " +
            $"Oxygen: {Pct(skinO2 / o2)} through the skin. A bite kills {Pct(e.PKill)} of the time; {(m.Rigidity > 0.5f ? $"a rigid integument ({Pct(e.MoultShare)} of its life soft in moults)" : "a soft integument")}." +
            (e.ItemShare > 0.05f ? $" Food items are {Pct(e.ItemShare)} of its intake." : "");
        return b;
    }

    static string Kg(float kg) => kg >= 1f ? N(kg, "F2") + " kg" : N(1000f * kg, kg >= 0.01f ? "F0" : "F1") + " g";

    static void AddGas(List<LabRole> roles, float partO2, float o2, float inShare, bool has)
    {
        if (!has) return;
        float sh = partO2 / o2;
        if (sh < 0.05f) return;
        string where = inShare > 0.66f ? "folded inside (a lung or tracheae)" : inShare > 0.33f ? "partly folded in" : "everted, like a gill";
        roles.Add(new LabRole { Role = "gas", Share = R(sh), Text = $"Gas exchange: {Pct(sh)} of its oxygen, through porous surface {where}." });
    }

    static void AddArmour(List<LabRole> roles, float pk, float mineral, float thick, bool appendage)
    {
        // only where the integument really stops bites: a soft skin "protects" nothing
        if (pk > 0.5f) return;
        roles.Add(new LabRole
        {
            Role = "protection", Share = R(1f - pk),
            Text = $"Protection: a predator's bite through it kills only {Pct(pk)} of the time ({(mineral > 0.5f ? "mineralised" : "tough")} {(appendage ? "outer tube" : "integument")} {N(thick * 1000f, "F2")} mm thick).",
        });
    }

    // the role it is drawn in: the one with the largest share, except that a small share of a vital function outranks armour
    static readonly string[] Order = { "crushing", "striking", "thrust", "support", "skeleton", "crawling", "steering", "gas", "protection", "housing", "passive" };
    static void Finish(List<LabRole> roles, out string primary, out string caption)
    {
        LabRole? best = null;
        foreach (var r in roles)
        {
            if (best == null) { best = r; continue; }
            bool rArm = r.Role is "protection" or "skeleton", bArm = best.Role is "protection" or "skeleton";   // structure: any vital function outranks it
            if (bArm && !rArm) { best = r; continue; }
            if (rArm && !bArm) continue;
            if (r.Role is "crushing" or "striking") { best = r; continue; }
            if (best.Role is "crushing" or "striking") continue;
            if (r.Share > best.Share) best = r;
        }
        primary = best?.Role ?? "passive";
        roles.Sort((x, y) => Array.IndexOf(Order, x.Role).CompareTo(Array.IndexOf(Order, y.Role)));
        caption = string.Join(" ", roles.Select(r => r.Text));
    }

    /// <summary>A snapshot of a generation: its champion and the best of up to maxReps other species, fittest first
    /// (the champion's own species is the champion, so it is not repeated in Reps).</summary>
    public static LabSnapshot Snapshot(GenerationInfo gi, MorphEnv env, int maxReps = 5)
    {
        float mean = 0f; foreach (var f in gi.Fit) mean += f; mean /= Math.Max(1, gi.Fit.Length);
        int champSize = 0;
        foreach (var (bi, sz) in gi.Species) if (bi == gi.Best) champSize = sz;
        var snap = new LabSnapshot { Gen = gi.Gen, Best = R(gi.Fit[gi.Best]), Mean = R(mean), Species = gi.SpeciesCount, Champion = Body(gi.Pop[gi.Best], env, champSize) };
        foreach (var (bi, sz) in gi.Species.Where(s => s.Best != gi.Best).OrderByDescending(s => gi.Fit[s.Best]).Take(maxReps))
            snap.Reps.Add(Body(gi.Pop[bi], env, sz));
        return snap;
    }

    public const string TableHeader = "env         seed   fit  mass kg segs pairs  fine rigid integ% moult  exchExt exchInt  mode    speed crushN items% turnR  spp  pairs (seg, length, use, mineral, moment N m, joints, muscle, flatness, droop)";

    /// <summary>A row of the MorphTests evolution demo's table (tests/Neuroreef.Tests/MorphTests.cs).</summary>
    public static string TableRow(EvolveResult r, MorphEnv env, uint seed)
    {
        var c = CultureInfo.InvariantCulture;
        var m = r.BestBody; var e = r.BestEval;
        var modes = new Dictionary<string, int>();
        foreach (var ch in r.Champions)
        {
            var cm = Development.Grow(ch.G); var ce = Physics.Evaluate(cm, env);
            string k = Evolver.Mode(ce) + (cm.Rigidity > 0.5f ? "-rigid" : "");
            modes[k] = modes.TryGetValue(k, out var v) ? v + 1 : 1;
        }
        string spp = string.Join(",", modes.Select(kv => kv.Key + ":" + kv.Value));
        return string.Format(c,
            "{0,-11} {1,4} {2,5:F2} {3,8:F3} {4,4} {5,5} {6,5:F1} {7,5:F2} {8,5:F1} {9,5:F3} {10,7:F4} {11,7:F4}  {12,-6} {13,5:F2} {14,6:F1} {15,5:F0} {16,5:F2} {17,3} [{18}]  {19}",
            env.Name, seed, e.Fitness, m.Mass, m.SegmentCount, m.PairCount, m.Fineness, m.Rigidity, 100f * m.IntMass / m.Mass, e.MoultShare,
            m.ExchExt, m.ExchInt, Evolver.Mode(e), e.Speed, e.CrushForce, 100f * e.ItemShare, MathF.Min(e.TurnR, 99f), r.Species, spp,
            Evolver.PairLine(m, env, e));
    }

    public static string Serialize(LabFile f) => JsonSerializer.Serialize(f, Json);
    public static LabFile Deserialize(string json) => JsonSerializer.Deserialize<LabFile>(json, Json) ?? throw new FormatException("not a Body Lab file");
}
