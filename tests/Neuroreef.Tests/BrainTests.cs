// The brain as a memory (Revision P): how many steps of past input a newborn brain can recall, by size and wiring gain,
// and whether a bigger brain learns a task that needs memory better than a small one.

using System;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Neuroreef.Tests;

public class BrainTests
{
    readonly ITestOutputHelper o; public BrainTests(ITestOutputHelper o) => this.o = o;

    /// <summary>A lone newborn brain of exactly this many units and this wiring-gain gene.</summary>
    internal static World Newborn(int units, sbyte wire, uint key)
    {
        var w = new World(8, 3);
        Brain.RandomGenome(w.GenomeOf(0), 7, 0, key);
        for (int gv = -127; gv <= 127; gv++) if (Body.Units((sbyte)gv) >= units) { w.Genome[G.B_BRAIN] = (sbyte)gv; break; }
        w.Genome[G.B_WIRE] = wire;
        w.Spc[0] = 0; w.StageOf[0] = St.Adult;
        Body.Express(w, 0); Brain.InitLife(w, 0);
        return w;
    }

    /// <summary>
    /// Memory capacity (Jaeger 2001): one sense is driven by white noise u(t); a linear readout of the memory units is fit
    /// to recall u(t - k) for k = 1..kmax, and the capacity is the sum of the squared correlations. N units hold at most N.
    /// </summary>
    internal static double MemoryCapacity(World w, int kmax = 40, int T = 1600, int seed = 1)
    {
        var rng = new Random(seed);
        int nu = w.PBrain[0], burn = 200;
        var f = new float[F.N]; var y = new float[G.NIN]; var z = new float[P.N]; var g = new float[P.N];
        var u = new double[T]; var H = new double[T, nu];
        for (int t = 0; t < T; t++)
        {
            Array.Clear(f); f[F.BIAS] = 1f; u[t] = rng.NextDouble() * 2 - 1; f[F.HUNGER] = (float)(0.5 + 0.5 * u[t]);
            Brain.Forward(w, 0, f, y, z, g);
            for (int h = 0; h < nu; h++) H[t, h] = y[h];
        }
        double mc = 0;
        for (int k = 1; k <= kmax; k++)
        {
            int n = T - burn - k, m = nu + 1;
            var A = new double[m, m]; var b = new double[m];
            for (int t = burn; t < T - k; t++)
                for (int p = 0; p < m; p++)
                {
                    double xp = p < nu ? H[t + k, p] : 1; b[p] += xp * u[t];
                    for (int q = p; q < m; q++) A[p, q] += xp * (q < nu ? H[t + k, q] : 1);
                }
            for (int p = 0; p < m; p++) { A[p, p] += 1e-4 * n; for (int q = 0; q < p; q++) A[p, q] = A[q, p]; }
            var wv = Solve(A, b, m);
            double sy = 0, sp = 0, syy = 0, spp = 0, syp = 0;
            for (int t = burn; t < T - k; t++)
            {
                double pr = wv[nu]; for (int p = 0; p < nu; p++) pr += wv[p] * H[t + k, p];
                sy += u[t]; sp += pr; syy += u[t] * u[t]; spp += pr * pr; syp += u[t] * pr;
            }
            double cov = syp / n - sy / n * sp / n, vy = syy / n - sy / n * sy / n, vp = spp / n - sp / n * sp / n;
            if (vp > 1e-12) mc += cov * cov / (vy * vp);
        }
        return mc;
    }

    static double[] Solve(double[,] A, double[] b, int n)
    {
        var M = (double[,])A.Clone(); var x = (double[])b.Clone();
        for (int c = 0; c < n; c++)
        {
            int piv = c; for (int r = c + 1; r < n; r++) if (Math.Abs(M[r, c]) > Math.Abs(M[piv, c])) piv = r;
            for (int k = 0; k < n; k++) (M[c, k], M[piv, k]) = (M[piv, k], M[c, k]); (x[c], x[piv]) = (x[piv], x[c]);
            for (int r = c + 1; r < n; r++) { double fct = M[r, c] / M[c, c]; for (int k = c; k < n; k++) M[r, k] -= fct * M[c, k]; x[r] -= fct * x[c]; }
        }
        var s = new double[n];
        for (int r = n - 1; r >= 0; r--) { double v = x[r]; for (int k = r + 1; k < n; k++) v -= M[r, k] * s[k]; s[r] = v / M[r, r]; }
        return s;
    }

    [Fact] // the wiring gain sets the radius of the scaled recurrent block (power iteration against the circular-law estimate)
    public void WiringGainSetsTheFeedbackStrength()
    {
        foreach (int units in new[] { 24, 256 })
            foreach (sbyte wire in new sbyte[] { -127, 0, 127 })
            {
                var w = Newborn(units, wire, 100);
                int nu = w.PBrain[0];
                var rng = new Random(3); var v = new double[nu]; for (int k = 0; k < nu; k++) v[k] = rng.NextDouble() - 0.5;
                double lam = 0;
                for (int it = 0; it < 300; it++)
                {
                    var nv = new double[nu];
                    for (int h = 0; h < nu; h++) { double s = 0; for (int k = 0; k < nu; k++) s += w.Genome[h * G.NIN + F.N + k] * K.WSCALE * w.PWire[0] * v[k]; nv[h] = s; }
                    double nn = Math.Sqrt(nv.Sum(x => x * x)), no = Math.Sqrt(v.Sum(x => x * x));
                    lam = nn / no; for (int k = 0; k < nu; k++) v[k] = nv[k] / nn;
                }
                float target = K.WIRE_LO + (K.WIRE_HI - K.WIRE_LO) * Body.Organ(wire);
                o.WriteLine($"units {nu}: wiring {target:F2} -> radius {lam:F2}");
                Assert.InRange(lam / target, 0.75, 1.25);
            }
    }

    [Fact] // a bigger brain remembers more: memory capacity grows with the units in use
    public void BiggerBrainsRememberMore()
    {
        double Mc(int units) => Enumerable.Range(0, 3).Average(k => MemoryCapacity(Newborn(units, 0, 200u + (uint)k)));
        double m24 = Mc(24), m256 = Mc(256);
        foreach (sbyte wire in new sbyte[] { -127, 127 }) o.WriteLine($"256 units, wiring gene {wire}: {MemoryCapacity(Newborn(256, wire, 200)):F1} steps");
        o.WriteLine($"memory capacity: 24 units {m24:F1} steps, 256 units {m256:F1} steps");
        Assert.True(m256 > 2.5 * m24 && m256 > 20, $"24 units {m24:F1}, 256 units {m256:F1}");
    }

    /// <summary>
    /// A delayed cue (Revision P): one of two sounds is heard for a single step, then after `delay` steps food is here;
    /// eating it then is rewarded after sound A and punished after sound B. At the moment of choice both trials look the
    /// same, so only a brain that remembers the sound can tell them apart. Returns P(EAT | A) - P(EAT | B) at the choice
    /// after `trials` trials of learning (node perturbation on EAT alone, as in LearningRaisesProbabilityOfRewardedAction).
    /// </summary>
    internal static double DelayedCue(int units, int delay, uint key, int trials = 3000)
    {
        var w = Newborn(units, 0, key);
        w.Genome[G.ETA] = 127; w.Genome[G.LAMBDA] = -128;   // a fast learner with a short trace, as in the learning test
        Body.Express(w, 0); Brain.InitLife(w, 0);
        var f = new float[F.N]; var y = new float[G.NIN]; var z = new float[P.N]; var g = new float[P.N]; var d = new float[P.N];
        var rs = new Rng.Seq(key * 7919u + 1u);
        int len = delay + 3; ulong step = 0;
        double Run(bool learn, bool cueA)
        {
            double pChoice = 0;
            for (int t = 0; t < len; t++, step++)
            {
                w.Step = step;
                if (learn) Brain.Learn(w, 0);
                Array.Clear(f); f[F.BIAS] = 1f; f[F.HUNGER] = 0.03f;
                if (t == 0) f[cueA ? F.HEAR_0 : F.HEAR_0 + 1] = 1f;
                bool choice = t == delay;
                if (choice) f[F.FOOD_HERE] = 1f;
                Brain.Forward(w, 0, f, y, z, g);
                if (choice) pChoice = g[P.EAT];
                for (int p = 0; p < P.N; p++) d[p] = 0;
                bool eat = rs.Next() < g[P.EAT];
                d[P.EAT] = (eat ? 1f : 0f) - g[P.EAT];
                if (learn) Brain.Record(w, 0, y, d);
                w.Reward[0] = choice && eat ? (cueA ? 1f : -1f) : 0f;
            }
            return pChoice;
        }
        for (int k = 0; k < trials; k++) Run(true, rs.Next() < 0.5f);
        double a = 0, b = 0; for (int k = 0; k < 50; k++) { a += Run(false, true); b += Run(false, false); }
        return (a - b) / 50;
    }

    [Fact] // how well brains of each size learn a cue they must hold in memory (printed); at the longest delay a big brain does at least as well (measured 2026-10-30: 12 units +0.30, 256 units +0.65)
    public void DelayedCueByBrainSize()
    {
        foreach (int delay in new[] { 1, 3, 6 })
        {
            string line = $"delay {delay}:";
            double small = 0, big = 0;
            foreach (int units in new[] { 12, 24, 64, 256 })
            {
                double sc = Enumerable.Range(0, 3).Average(k => DelayedCue(units, delay, 500u + (uint)k));
                line += $"  {units} units {sc:+0.00;-0.00}";
                if (units == 12) small = sc; if (units == 256) big = sc;
            }
            o.WriteLine(line);
            if (delay == 6) Assert.True(big >= small, $"at delay 6 a 256-unit brain scored {big:F2}, a 12-unit one {small:F2}");
        }
    }
}
