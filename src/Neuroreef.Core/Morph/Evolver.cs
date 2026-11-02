// A tiny evolutionary harness for the body-plan prototype (docs/BODYPLAN.md §6). A population of genomes in one fixed
// environment; fitness is the energy surplus its morphology earns there by physics (Physics.Evaluate), less its
// predation mortality. Tournament selection, a few elites, asexual offspring with every mutation operator
// (MorphGenome.cs). Nothing here or in the physics names an organ: what evolves is whatever the physics pays for.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Neuroreef.Morph;

public sealed class EvolveResult
{
    public MorphGenome Best = new();
    public Morphology BestBody = new();
    public MorphEval BestEval;
    public float[] BestByGen = Array.Empty<float>();
    public float MeanSegments, MeanPairs;   // over the final population
}

public static class Evolver
{
    public static EvolveResult Run(MorphEnv env, uint seed, int population = 120, int generations = 600, MutationRates? rates = null)
    {
        rates ??= new MutationRates();
        var r = new Rng.Seq(seed);
        var pop = new MorphGenome[population];
        for (int i = 0; i < population; i++)
        {
            pop[i] = MorphGenome.Blob();
            Mutate.Point(pop[i], ref r, 0.3f, 0.05f);   // the founders differ a little
        }
        var fit = new float[population];
        var best = new float[generations];
        var next = new MorphGenome[population];
        const int ELITE = 2, TOUR = 3;
        for (int gen = 0; gen < generations; gen++)
        {
            for (int i = 0; i < population; i++) fit[i] = Physics.Evaluate(Development.Grow(pop[i]), env).Fitness;
            var order = new int[population];
            for (int i = 0; i < population; i++) order[i] = i;
            Array.Sort(order, (a, b) => fit[b].CompareTo(fit[a]));
            best[gen] = fit[order[0]];
            if (gen == generations - 1) { pop = Reorder(pop, order); break; }
            for (int e = 0; e < ELITE; e++) next[e] = pop[order[e]].Clone();
            for (int i = ELITE; i < population; i++)
            {
                int w = (int)(r.Next() * population);
                for (int t = 1; t < TOUR; t++) { int c = (int)(r.Next() * population); if (fit[c] > fit[w]) w = c; }
                var child = pop[w].Clone();
                Mutate.Apply(child, ref r, rates);
                next[i] = child;
            }
            (pop, next) = (next, pop);
        }
        var res = new EvolveResult { Best = pop[0], BestByGen = best };
        res.BestBody = Development.Grow(res.Best);
        res.BestEval = Physics.Evaluate(res.BestBody, env);
        foreach (var g in pop) { var b = Development.Grow(g); res.MeanSegments += b.SegmentCount; res.MeanPairs += b.PairCount; }
        res.MeanSegments /= population; res.MeanPairs /= population;
        return res;
    }

    static MorphGenome[] Reorder(MorphGenome[] pop, int[] order)
    {
        var o = new MorphGenome[pop.Length];
        for (int i = 0; i < pop.Length; i++) o[i] = pop[order[i]];
        return o;
    }

    /// <summary>A plain-text account of a body: its segments and appendage pairs in physical units, and its budget.</summary>
    public static string Describe(Morphology m, MorphEval e)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(string.Format(c, "mass {0:F3} kg  length {1:F3} m  fineness {2:F2}  density {3:F0} kg/m3  segments {4}  appendage pairs {5}  joints {6}",
            m.Mass, m.Length, m.Fineness, m.Mass / m.Volume, m.SegmentCount, m.PairCount, m.JointCount));
        sb.AppendLine(string.Format(c, "integument {0:F1}% of mass, rigidity {1:F2}, moult share {2:F3}; exchange everted {3:F4} m2, folded-in {4:F4} m2; skin {5:F4} m2 at {6:F2} mm",
            100f * m.IntMass / m.Mass, m.Rigidity, e.MoultShare, m.ExchExt, m.ExchInt, m.SkinArea, m.SkinThick * 1000f));
        sb.AppendLine(string.Format(c, "fitness {0:F2}  net {1:F3} W  basal {2:F3} W  intake {3:F3} W  {4} at {5:F3} m/s (max {6:F3})  pKill {7:F2}  mortality {8:F2}  O2 {9:F3}/{10:F3} W  support {11:F2}  stability {12:F2}  hold {13:F2}",
            e.Fitness, e.Net, e.Basal, e.Intake, e.Walks ? "walks" : "swims", e.Speed, e.MaxSpeed, e.PKill, e.Mortality, e.O2Supply, e.O2Demand, e.Support, e.Stability, e.Hold));
        for (int i = 0; i < m.Segs.Count; i++)
        {
            var s = m.Segs[i];
            sb.AppendLine(string.Format(c, "  seg {0} (gene {1}): L {2:F1} W {3:F1} H {4:F1} mm  integ {5:F2} mm min {6:F2}  pore {7:F2} invag {8:F2}  axial muscle {9:F2}",
                i, s.Gene, s.L * 1000f, s.W * 1000f, s.H * 1000f, s.IntT * 1000f, s.IntMin, s.Pore, s.Invag, s.AxShare));
            foreach (var a in m.Pairs)
                if (a.Seg == i)
                    sb.AppendLine(string.Format(c, "     pair: len {0:F1} chord {1:F1} thick {2:F1} mm  AR {3:F1} flat {4:F1}  droop {5:F0} deg  joints {6}  muscle {7:F2}  mineral {8:F2} wall {9:F2} exo {10:F2}  pore {11:F2} invag {12:F2}",
                        a.Len * 1000f, a.Chord * 1000f, a.Thick * 1000f, 2f * a.Len / a.Chord, a.Chord / a.Thick, a.Theta * 180f / MathF.PI,
                        a.Joints, a.MuscleShare, a.Mineral, a.Wall, a.Exo, a.Pore, a.Invag));
        }
        return sb.ToString();
    }

    /// <summary>Summary statistics for a table row: means over appendage pairs, weighted by their length.</summary>
    public static Dictionary<string, float> Summary(Morphology m, MorphEval e)
    {
        var d = new Dictionary<string, float>
        {
            ["segments"] = m.SegmentCount, ["pairs"] = m.PairCount, ["mass"] = m.Mass, ["fineness"] = m.Fineness,
            ["rigidity"] = m.Rigidity, ["integShare"] = m.IntMass / m.Mass, ["moult"] = e.MoultShare,
            ["exchExt"] = m.ExchExt, ["exchInt"] = m.ExchInt, ["fitness"] = e.Fitness, ["walks"] = e.Walks ? 1f : 0f,
            ["speed"] = e.Speed, ["pKill"] = e.PKill,
        };
        float w = 0f, ar = 0f, flat = 0f, droop = 0f, joints = 0f, mineral = 0f, mus = 0f;
        foreach (var a in m.Pairs)
        {
            float k = a.Len; w += k;
            ar += k * 2f * a.Len / a.Chord; flat += k * a.Chord / a.Thick; droop += k * a.Theta * 180f / MathF.PI;
            joints += k * a.Joints; mineral += k * a.Mineral; mus += k * a.MuscleShare;
        }
        if (w > 0f) { ar /= w; flat /= w; droop /= w; joints /= w; mineral /= w; mus /= w; }
        d["AR"] = ar; d["flat"] = flat; d["droop"] = droop; d["joints"] = joints; d["mineral"] = mineral; d["appMuscle"] = mus;
        return d;
    }
}
