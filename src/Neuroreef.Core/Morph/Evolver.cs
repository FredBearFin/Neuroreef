// A small evolutionary harness for the body-plan prototype (docs/BODYPLAN.md §8). A population of genomes in one fixed
// environment; fitness is the energy surplus its morphology earns there by physics (Physics.Evaluate), less its
// predation mortality. Nothing here or in the physics names an organ: what evolves is whatever the physics pays for.
//
// Search is NEAT-style (Stanley & Miikkulainen 2002): genomes are grouped into species by their compatibility distance
// on innovation numbers (Homology.Distance), each species gets offspring in proportion to its members' shared fitness
// (fitness sharing: Goldberg & Richardson 1987), so a new body plan that is not yet the best is not swamped by the
// current best one, and offspring are bred within their species by tournament, with crossover of homologous modules
// (Homology.Cross) and every mutation operator (MorphGenome.cs). The overall best genome is always kept (elitism).

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
    public int Species;                     // species in the final generation
    /// <summary>The best genome of each final species, best first.</summary>
    public readonly List<(MorphGenome G, float Fitness, int Size)> Champions = new();
}

public sealed class SearchOptions
{
    public bool Speciate = true;
    public int TargetSpecies = 8;
    public float CrossRate = 0.3f, InterspeciesRate = 0.02f;
    public int Stagnation = 60;      // generations without improvement before a species (not holding the best) is dropped
}

public static class Evolver
{
    sealed class Species
    {
        public MorphGenome Rep = null!;
        public readonly List<int> Members = new();
        public float BestEver = float.NegativeInfinity;
        public int Stale;
    }

    public static EvolveResult Run(MorphEnv env, uint seed, int population = 120, int generations = 600, MutationRates? rates = null, SearchOptions? opt = null)
    {
        rates ??= new MutationRates();
        opt ??= new SearchOptions();
        var r = new Rng.Seq(seed);
        var inn = new Innovations();
        var pop = new MorphGenome[population];
        for (int i = 0; i < population; i++)
        {
            pop[i] = MorphGenome.Blob();
            Mutate.Point(pop[i], ref r, 0.3f, 0.05f);   // the founders differ a little
        }
        var fit = new float[population];
        var best = new float[generations];
        var species = new List<Species>();
        float thresh = 0.6f;
        const int TOUR = 3;
        for (int gen = 0; gen < generations; gen++)
        {
            for (int i = 0; i < population; i++) fit[i] = Physics.Evaluate(Development.Grow(pop[i]), env).Fitness;
            int gBest = 0;
            for (int i = 1; i < population; i++) if (fit[i] > fit[gBest]) gBest = i;
            best[gen] = fit[gBest];

            // speciate: each genome joins the first species whose representative is within the threshold
            foreach (var s in species) s.Members.Clear();
            if (opt.Speciate)
            {
                for (int i = 0; i < population; i++)
                {
                    Species? home = null;
                    foreach (var s in species) if (Homology.Distance(pop[i], s.Rep) < thresh) { home = s; break; }
                    if (home == null) { home = new Species { Rep = pop[i] }; species.Add(home); }
                    home.Members.Add(i);
                }
            }
            else
            {
                if (species.Count == 0) species.Add(new Species { Rep = pop[0] });
                for (int i = 0; i < population; i++) species[0].Members.Add(i);
            }
            species.RemoveAll(s => s.Members.Count == 0);
            foreach (var s in species)
            {
                float sb = float.NegativeInfinity;
                foreach (int i in s.Members) sb = MathF.Max(sb, fit[i]);
                if (sb > s.BestEver + 1e-4f) { s.BestEver = sb; s.Stale = 0; } else s.Stale++;
            }
            if (opt.Speciate) thresh = species.Count > opt.TargetSpecies ? thresh * 1.05f : species.Count < opt.TargetSpecies ? MathF.Max(0.1f, thresh / 1.05f) : thresh;

            if (gen == generations - 1)
            {
                var res = Finish(pop, fit, gBest, best, species, env);
                return res;
            }

            // offspring quotas by shared fitness: mean (shifted) fitness of a species' members
            float fMin = float.PositiveInfinity;
            for (int i = 0; i < population; i++) fMin = MathF.Min(fMin, fit[i]);
            var quota = new float[species.Count];
            float qSum = 0f;
            for (int k = 0; k < species.Count; k++)
            {
                var s = species[k];
                bool holdsBest = s.Members.Contains(gBest);
                if (s.Stale > opt.Stagnation && !holdsBest) continue;
                float sum = 0f;
                foreach (int i in s.Members) sum += fit[i] - fMin + 0.1f;
                quota[k] = sum / s.Members.Count;
                qSum += quota[k];
            }
            var n = new int[species.Count];
            int given = 1;   // slot 0 is the overall best
            var frac = new float[species.Count];
            for (int k = 0; k < species.Count; k++)
            {
                float q = qSum > 0f ? quota[k] / qSum * (population - 1) : 0f;
                n[k] = (int)q; frac[k] = q - n[k]; given += n[k];
            }
            while (given < population)
            {
                int kb = 0;
                for (int k = 1; k < species.Count; k++) if (frac[k] > frac[kb]) kb = k;
                n[kb]++; frac[kb] = -1f; given++;
            }

            var next = new MorphGenome[population];
            next[0] = pop[gBest].Clone();
            int slot = 1;
            for (int k = 0; k < species.Count; k++)
            {
                var s = species[k];
                var mem = s.Members;
                int sBest = mem[0];
                foreach (int i in mem) if (fit[i] > fit[sBest]) sBest = i;
                for (int c = 0; c < n[k]; c++)
                {
                    if (c == 0 && mem.Count >= 3 && sBest != gBest) { next[slot++] = pop[sBest].Clone(); continue; }   // the species' champion
                    int w = Tour(mem, fit, ref r, TOUR);
                    MorphGenome child;
                    if (r.Next() < opt.CrossRate)
                    {
                        int w2 = r.Next() < opt.InterspeciesRate ? (int)(r.Next() * population) % population : Tour(mem, fit, ref r, TOUR);
                        child = fit[w] >= fit[w2] ? Homology.Cross(pop[w], pop[w2], ref r) : Homology.Cross(pop[w2], pop[w], ref r);
                    }
                    else child = pop[w].Clone();
                    Mutate.Apply(child, ref r, rates, inn);
                    next[slot++] = child;
                }
            }
            // the next generation's representatives: a random member of each species
            foreach (var s in species) s.Rep = pop[s.Members[(int)(r.Next() * s.Members.Count) % s.Members.Count]].Clone();
            pop = next;
        }
        throw new InvalidOperationException("unreachable");
    }

    static int Tour(List<int> mem, float[] fit, ref Rng.Seq r, int size)
    {
        int w = mem[(int)(r.Next() * mem.Count) % mem.Count];
        for (int t = 1; t < size; t++) { int c = mem[(int)(r.Next() * mem.Count) % mem.Count]; if (fit[c] > fit[w]) w = c; }
        return w;
    }

    static EvolveResult Finish(MorphGenome[] pop, float[] fit, int gBest, float[] best, List<Species> species, MorphEnv env)
    {
        var res = new EvolveResult { Best = pop[gBest], BestByGen = best, Species = species.Count };
        res.BestBody = Development.Grow(res.Best);
        res.BestEval = Physics.Evaluate(res.BestBody, env);
        foreach (var g in pop) { var b = Development.Grow(g); res.MeanSegments += b.SegmentCount; res.MeanPairs += b.PairCount; }
        res.MeanSegments /= pop.Length; res.MeanPairs /= pop.Length;
        foreach (var s in species)
        {
            int sb = s.Members[0];
            foreach (int i in s.Members) if (fit[i] > fit[sb]) sb = i;
            res.Champions.Add((pop[sb], fit[sb], s.Members.Count));
        }
        res.Champions.Sort((a, b) => b.Fitness.CompareTo(a.Fitness));
        return res;
    }

    /// <summary>A plain-text account of a body: its segments and appendage pairs in physical units, and its budget.</summary>
    public static string Describe(Morphology m, MorphEval e, MorphEnv? env = null)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        string[]? uses = env != null ? Physics.PairUses(m, env, e) : null;
        sb.AppendLine(string.Format(c, "mass {0:F3} kg  length {1:F3} m  fineness {2:F2}  density {3:F0} kg/m3  segments {4}  appendage pairs {5}  joints {6}",
            m.Mass, m.Length, m.Fineness, m.Mass / m.Volume, m.SegmentCount, m.PairCount, m.JointCount));
        sb.AppendLine(string.Format(c, "integument {0:F1}% of mass, rigidity {1:F2}, moult share {2:F3}; exchange everted {3:F4} m2, folded-in {4:F4} m2; skin {5:F4} m2 at {6:F2} mm",
            100f * m.IntMass / m.Mass, m.Rigidity, e.MoultShare, m.ExchExt, m.ExchInt, m.SkinArea, m.SkinThick * 1000f));
        sb.AppendLine(string.Format(c, "fitness {0:F2}  net {1:F3} W  basal {2:F3} W  intake {3:F3} W  {4} at {5:F3} m/s (max {6:F3})  pKill {7:F2}  escape {8:F2}  mortality {9:F2}  O2 {10:F3}/{11:F3} W  support {12:F2}  stability {13:F2}  hold {14:F2}",
            e.Fitness, e.Net, e.Basal, e.Intake, Mode(e), e.Speed, e.MaxSpeed, e.PKill, e.Escape, e.Mortality, e.O2Supply, e.O2Demand, e.Support, e.Stability, e.Hold));
        sb.AppendLine(string.Format(c, "feeding: crush {0:F2} N (by {1}), items {2:F0}% of intake, strike reach {3:F1} mm, catch {4:F2}; turning radius {5:F3} m; travel {6:F0}% of time",
            e.CrushForce, e.Handler < 0 ? "mouth" : "pair " + e.Handler, 100f * e.ItemShare, e.Reach * 1000f, e.CaptureP, e.TurnR, 100f * e.TravelShare));
        int pi = 0;
        for (int i = 0; i < m.Segs.Count; i++)
        {
            var s = m.Segs[i];
            sb.AppendLine(string.Format(c, "  seg {0} (gene {1}): L {2:F1} W {3:F1} H {4:F1} mm  integ {5:F2} mm min {6:F2}  pore {7:F2} invag {8:F2}  axial muscle {9:F2}",
                i, s.Gene, s.L * 1000f, s.W * 1000f, s.H * 1000f, s.IntT * 1000f, s.IntMin, s.Pore, s.Invag, s.AxShare));
            for (int q = 0; q < m.Pairs.Count; q++)
            {
                var a = m.Pairs[q];
                if (a.Seg != i) continue;
                sb.AppendLine(string.Format(c, "     pair {12}: len {0:F1} chord {1:F1} thick {2:F1} mm  AR {3:F1} flat {4:F1}  droop {5:F0} deg{13}  joints {6}  muscle {7:F2}  mineral {8:F2} wall {9:F2} exo {10:F2}  moment {11:E1} N m  use {14}",
                    a.Len * 1000f, a.Chord * 1000f, a.Thick * 1000f, 2f * a.Len / a.Chord, a.Chord / a.Thick, a.Theta * 180f / MathF.PI,
                    a.Joints, a.MuscleShare, a.Mineral, a.Wall, a.Exo, a.MomentCap, q, a.Sagittal ? " sagittal" : "", uses != null ? uses[q] : "?"));
                pi++;
            }
        }
        return sb.ToString();
    }

    public static string Mode(in MorphEval e) => e.Walks ? (e.Legged ? "walks" : "crawls") : "swims";

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

    /// <summary>One compact line per appendage pair: stiffness (moment it bears), joints, muscle, flatness, droop, use.</summary>
    public static string PairLine(Morphology m, MorphEnv env, in MorphEval e)
    {
        var c = CultureInfo.InvariantCulture;
        var uses = Physics.PairUses(m, env, e);
        var sb = new StringBuilder();
        for (int i = 0; i < m.Pairs.Count; i++)
        {
            var a = m.Pairs[i];
            if (sb.Length > 0) sb.Append("; ");
            sb.Append(string.Format(c, "s{0} {1:F0}mm {2} min{3:F1} M{4:E0} j{5} mus{6:F2} flat{7:F0} {8}{9:F0}deg",
                a.Seg, a.Len * 1000f, uses[i], a.Mineral, a.MomentCap, a.Joints, a.MuscleShare, a.Chord / a.Thick,
                a.Sagittal ? "sag " : "", a.Theta * 180f / MathF.PI));
        }
        return sb.Length > 0 ? sb.ToString() : "none";
    }
}
