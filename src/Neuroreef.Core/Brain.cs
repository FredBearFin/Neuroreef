// The brain (DESIGN §7): identical architecture for every species.
//   hidden h = tanh(W1 · [f, h_prev])          W1 from the genome, fixed for life
//   z        = W2f · [h, f]                    W2f = genome W2 + what was learned in life
//   gain g   = sigmoid(z)
//   reflex   z += R · c                         R from the genome, never learned (DESIGN §7.7)
// Learning: node perturbation with eligibility traces and a reward baseline (three-factor rule).

using System;
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.CompilerServices;

namespace Neuroreef;

public static class Brain
{
    // ------------------------------------------------------------------ genomes

    /// <summary>A random founder genome: a function of (seed, slot, step) only (NR-210).</summary>
    public static void RandomGenome(Span<sbyte> g, uint seed, uint step, uint key)
    {
        // memory layer: random; output layer: small random weights and a low bias, so every action of
        // every species starts rare and must be learned or evolved upward ("calm newborn brain")
        for (int k = 0; k < G.ETA; k++)
        {
            float u = Rng.U(seed, step, key, Rs.Founder + (uint)k * 4u) * 2f - 1f;
            int col = (k - G.W2) % G.NIN;
            if (k < G.W2) g[k] = (sbyte)MathF.Round(u * K.FOUNDER_W);
            else if (col == G.NH + F.BIAS) g[k] = (sbyte)(K.FOUNDER_BIAS + (int)MathF.Round(u * K.FOUNDER_BIAS_NOISE));
            else g[k] = (sbyte)MathF.Round(u * K.FOUNDER_W2);
        }
        for (int k = G.ETA; k < G.BODY; k++)
            g[k] = (sbyte)(int)(Rng.U(seed, step, key, Rs.Founder + (uint)k * 4u) * 255f - 128f);
        for (int k = G.BODY; k < G.REFLEX; k++) g[k] = 0;  // founder body and care = the species body plan
        // reflexes: small random links by the same rule as the output layer, plus the innate starter kit
        for (int k = G.REFLEX; k < G.LEN; k++)
            g[k] = (sbyte)MathF.Round((Rng.U(seed, step, key, Rs.Founder + (uint)k * 4u) * 2f - 1f) * K.FOUNDER_W2);
        foreach (var (c, p) in Reflex.Kit) g[G.REFLEX + p * G.NC + c] = (sbyte)Math.Clamp(K.KIT_W + g[G.REFLEX + p * G.NC + c], -127, 127);
    }

    /// <summary>Standing variation among founders of one ancestor: every brain and reflex byte ± FOUNDER_W_SD.</summary>
    public static void VaryBrain(Span<sbyte> g, uint seed, uint key)
    {
        for (int k = 0; k < G.LEN; k++)
        {
            if (k == G.BODY) { k = G.BODY + G.NBODY - 1; continue; }
            g[k] = (sbyte)Math.Clamp((int)MathF.Round(g[k] + Rng.N(seed, 0, key, Rs.Founder + 20000u + (uint)k) * K.FOUNDER_W_SD), -127, 127);
        }
    }

    /// <summary>Mutation (DESIGN §7.3): v1's mutateWord rule on every byte.</summary>
    public static void Mutate(Span<sbyte> g, uint seed, uint step, uint key) => Mutate(g, seed, step, key, Defaults);
    static readonly EvoSettings Defaults = new();

    public static void Mutate(Span<sbyte> g, uint seed, uint step, uint key, EvoSettings e)
    {
        for (int k = 0; k < G.LEN; k++)
        {
            // body genes (each a polygenic trait) have their own rate; brain and reflex bytes MUT_RATE
            float rate = k >= G.BODY && k < G.BODY + G.NBODY ? e.MutBody : e.MutRate;
            float u = Rng.U(seed, step, key, Rs.Mutate + (uint)k * 8u);
            if (u >= rate) continue;
            float n = Rng.N(seed, step, key, Rs.Mutate + (uint)k * 8u + 1u);
            float d = (u < rate * e.MutBigShare) ? n * e.MutBig : n * e.MutSmall;
            int v = g[k] + (int)MathF.Round(d);
            g[k] = (sbyte)Math.Clamp(v, -127, 127);
        }
    }

    /// <summary>Row-wise uniform crossover: each neuron's whole input row comes from one parent (linkage).</summary>
    public static void Crossover(Span<sbyte> child, ReadOnlySpan<sbyte> mother, ReadOnlySpan<sbyte> father, uint seed, uint step, uint key)
    {
        for (int row = 0; row < G.NH + P.N; row++)
        {
            int off = row * G.NIN;     // W1 rows then W2 rows are contiguous, each NIN long
            bool fromFather = Rng.U(seed, step, key, Rs.Cross + (uint)row) < 0.5f;
            (fromFather ? father : mother).Slice(off, G.NIN).CopyTo(child.Slice(off, G.NIN));
        }
        for (int k = G.ETA; k < G.REFLEX; k++)
            child[k] = Rng.U(seed, step, key, Rs.Cross + 100u + (uint)k) < 0.5f ? mother[k] : father[k];
        for (int p = 0; p < P.N; p++)
        {
            // each primitive's reflex row comes from one parent, like the brain's rows
            int off = G.REFLEX + p * G.NC;
            bool fromFather = Rng.U(seed, step, key, Rs.Cross + 5000u + (uint)p) < 0.5f;
            (fromFather ? father : mother).Slice(off, G.NC).CopyTo(child.Slice(off, G.NC));
        }
    }

    /// <summary>Reset the life-time state of slot i from its genome: working weights, traces, hidden state.</summary>
    public static void InitLife(World w, int i)
    {
        int g0 = i * G.LEN, o = i * G.W2N;
        for (int k = 0; k < G.W2N; k++) { w.W2f[o + k] = w.Genome[g0 + G.W2 + k] * K.WSCALE; w.Trace[o + k] = 0f; }
        Array.Clear(w.Hidden, i * G.NH, G.NH);
        Array.Clear(w.HistA, i * BLOCK, BLOCK);
        Array.Clear(w.HistD, i * BLOCK * P.N, BLOCK * P.N);
        Array.Clear(w.HistY, i * BLOCK * G.NIN, BLOCK * G.NIN);
        Array.Clear(w.Gain, i * P.N, P.N);
        w.Reward[i] = 0; w.RewardMean[i] = 0; w.PendingR[i] = 0;
    }

    // ------------------------------------------------------------------ evaluation and learning

    public const int BLOCK = 4;   // learning updates are applied every BLOCK steps per slot (staggered by slot)

    /// <summary>
    /// Learning bookkeeping at the start of DECIDE for slot i (DESIGN §7.2). The reward r_t credits the
    /// trace of the previous step. It is stored; every BLOCK steps the block is applied in one pass over
    /// the weights. The result equals the per-step rule except that weights are up to BLOCK-1 steps stale.
    /// </summary>
    public static void Learn(World w, int i)
    {
        float r = w.Reward[i];
        float adv = r - w.RewardMean[i];
        w.RewardMean[i] += K.RBAR_RATE * (r - w.RewardMean[i]);
        int t = (int)(w.Step & 0x3FFFFFFF);
        w.HistA[i * BLOCK + ((t - 1 + i) & (BLOCK - 1))] = adv;
        if (((t + i) & (BLOCK - 1)) == 0) ApplyBlock(w, i);
    }

    /// <summary>Store this step's perturbation and inputs for the block update.</summary>
    public static void Record(World w, int i, ReadOnlySpan<float> y, ReadOnlySpan<float> delta)
    {
        int k = ((int)(w.Step & 0x3FFFFFFF) + i) & (BLOCK - 1);
        delta.CopyTo(new Span<float>(w.HistD, (i * BLOCK + k) * P.N, P.N));
        y.CopyTo(new Span<float>(w.HistY, (i * BLOCK + k) * G.NIN, G.NIN));
    }

    static unsafe void ApplyBlock(World w, int i)
    {
        int g0 = i * G.LEN;
        float eta = G.Gene(w.Genome[g0 + G.ETA], K.ETA_LO, K.ETA_HI);
        float lam = G.Gene(w.Genome[g0 + G.LAMBDA], K.LAMBDA_LO, K.LAMBDA_HI);
        float l2 = lam * lam, l3 = l2 * lam, l4 = l3 * lam;
        int hb = i * BLOCK;
        float A0 = w.HistA[hb], A1 = w.HistA[hb + 1], A2 = w.HistA[hb + 2], A3 = w.HistA[hb + 3];
        float c0 = eta * (A0 * lam + A1 * l2 + A2 * l3 + A3 * l4);
        float cA = eta * (A0 + A1 * lam + A2 * l2 + A3 * l3);
        float cB = eta * (A1 + A2 * lam + A3 * l2);
        float cC = eta * (A2 + A3 * lam);
        float cD = eta * A3;
        fixed (float* W = &w.W2f[i * G.W2N])
        fixed (float* E = &w.Trace[i * G.W2N])
        fixed (float* D = &w.HistD[hb * P.N])
        fixed (float* Y = &w.HistY[hb * G.NIN])
        {
            float* y0 = Y, y1 = Y + G.NIN, y2 = Y + 2 * G.NIN, y3 = Y + 3 * G.NIN;
            var hi = Vector256.Create(K.WMAX); var lo = Vector256.Create(-K.WMAX);
            var vc0 = Vector256.Create(c0); var vl4 = Vector256.Create(l4);
            for (int p = 0; p < P.N; p++)
            {
                float d0 = D[p], d1 = D[P.N + p], d2 = D[2 * P.N + p], d3 = D[3 * P.N + p];
                float wA = cA * d0, wB = cB * d1, wC = cC * d2, wD = cD * d3;          // weight-update coefficients
                float eA = l3 * d0, eB = l2 * d1, eC = lam * d2, eD = d3;              // trace coefficients
                float* Wr = W + p * G.NIN, Er = E + p * G.NIN;
                var vwA = Vector256.Create(wA); var vwB = Vector256.Create(wB); var vwC = Vector256.Create(wC); var vwD = Vector256.Create(wD);
                var veA = Vector256.Create(eA); var veB = Vector256.Create(eB); var veC = Vector256.Create(eC); var veD = Vector256.Create(eD);
                int k = 0;
                for (; k <= G.NIN - 8; k += 8)
                {
                    var e = Vector256.Load(Er + k);
                    var a = Vector256.Load(y0 + k); var b = Vector256.Load(y1 + k); var c = Vector256.Load(y2 + k); var d = Vector256.Load(y3 + k);
                    var dw = vc0 * e + vwA * a + vwB * b + vwC * c + vwD * d;
                    Vector256.Store(Vector256.Min(hi, Vector256.Max(lo, Vector256.Load(Wr + k) + dw)), Wr + k);
                    Vector256.Store(vl4 * e + veA * a + veB * b + veC * c + veD * d, Er + k);
                }
                for (; k < G.NIN; k++)
                {
                    float e = Er[k];
                    float dw = c0 * e + wA * y0[k] + wB * y1[k] + wC * y2[k] + wD * y3[k];
                    Wr[k] = Math.Clamp(Wr[k] + dw, -K.WMAX, K.WMAX);
                    Er[k] = l4 * e + eA * y0[k] + eB * y1[k] + eC * y2[k] + eD * y3[k];
                }
            }
        }
    }

    /// <summary>Pull learned weights back toward the genome (kappa), applied every 32 steps per slot.</summary>
    public static void Relax(World w, int i)
    {
        int g0 = i * G.LEN, o = i * G.W2N;
        float kappa = 32f * G.Gene(w.Genome[g0 + G.KAPPA], 0f, K.KAPPA_HI);
        for (int k = 0; k < G.W2N; k++)
        {
            float b = w.Genome[g0 + G.W2 + k] * K.WSCALE;
            w.W2f[o + k] += kappa * (b - w.W2f[o + k]);
        }
    }

    /// <summary>
    /// Forward pass. f = features (F.N), y = scratch (G.NIN) receiving [h, f], z and g receive outputs (P.N).
    /// Updates the hidden state of slot i.
    /// </summary>
    public static unsafe void Forward(World w, int i, ReadOnlySpan<float> f, Span<float> y, Span<float> z, Span<float> g)
    {
        float* u = stackalloc float[G.NIN];
        float* c = stackalloc float[G.NC];
        for (int k = 0; k < F.N; k++) u[k] = f[k];
        Reflex.Inputs(f, new Span<float>(c, G.NC));
        fixed (float* hid = &w.Hidden[i * G.NH])
        fixed (sbyte* gen = &w.Genome[i * G.LEN])
        fixed (float* W = &w.W2f[i * G.W2N])
        fixed (float* yp = y)
        {
            for (int k = 0; k < G.NH; k++) u[F.N + k] = hid[k];
            int units = w.PBrain[i];                 // the brain-size gene: memory units beyond it do not exist
            for (int h = units; h < G.NH; h++) { hid[h] = 0f; yp[h] = 0f; u[F.N + h] = 0f; }
            for (int h = 0; h < units; h++)
            {
                sbyte* row = gen + h * G.NIN;
                float s0 = 0, s1 = 0;
                for (int j = 0; j < G.NIN; j += 2) { s0 += row[j] * u[j]; s1 += row[j + 1] * u[j + 1]; }
                float v = MathK.Tanh((s0 + s1) * K.WSCALE);
                hid[h] = v; yp[h] = v;
            }
            for (int k = 0; k < F.N; k++) yp[G.NH + k] = f[k];
            sbyte* rx = gen + G.REFLEX;
            for (int p = 0; p < P.N; p++)
            {
                float sv = Dot(W + p * G.NIN, yp, G.NIN);
                sbyte* rr = rx + p * G.NC;
                float rs = 0;
                for (int k = 0; k < G.NC; k++) rs += rr[k] * c[k];
                sv += rs * K.RSCALE;
                z[p] = sv;
                g[p] = 1f / (1f + MathK.Exp(-sv));
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static unsafe float Dot(float* a, float* b, int n)
    {
        var acc = Vector256<float>.Zero;
        int k = 0;
        for (; k <= n - 8; k += 8) acc += Vector256.Load(a + k) * Vector256.Load(b + k);
        float s = Vector256.Sum(acc);
        for (; k < n; k++) s += a[k] * b[k];
        return s;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static unsafe void Axpy(float* y, float a, float* x, float decay, int n)
    {
        var va = Vector256.Create(a); var vd = Vector256.Create(decay);
        int k = 0;
        for (; k <= n - 8; k += 8) Vector256.Store(Vector256.Load(y + k) * vd + va * Vector256.Load(x + k), y + k);
        for (; k < n; k++) y[k] = decay * y[k] + a * x[k];
    }

    public static float Sigma(World w, int i) => G.Gene(w.Genome[i * G.LEN + G.SIGMA], K.SIGMA_LO, K.SIGMA_HI);
}
