// The brain (DESIGN §7): identical architecture for every species.
//   hidden h = tanh(W1 · [f, h_prev])          W1 from the genome, fixed for life
//   z        = W2f · [h, f]                    W2f = genome W2 + what was learned in life
//   gain g   = sigmoid(z)
//   reflex   z += R · c                         R from the genome, never learned (DESIGN §7.7)
// Learning: node perturbation with eligibility traces and a reward baseline (three-factor rule).

using System;
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
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
            int col = (k - G.W2) % G.NIN, row = (k - G.W2) / G.NIN;
            // calling is one behaviour made with 8 sounds: together they start as rare as any other primitive
            int bias = row >= P.CALL_0 && row < P.CALL_0 + F.NSOUND ? K.FOUNDER_BIAS_CALL : K.FOUNDER_BIAS;
            // the output weights from memory unit h shrink as 1/h beyond BRAIN_START units, so a big newborn brain is as
            // calm as a small one: its extra units start almost silent, and learning (the output layer is plastic) or
            // evolution brings them in as they pay
            float hs = col < G.NH ? (float)K.BRAIN_START / MathF.Max(K.BRAIN_START, col + 1) : 1f;
            // likewise the memory layer's weights from other memory units (input j >= F.N is unit j - F.N): a unit's
            // summed input does not grow with the brain, so big brains do not start saturated
            int jin = k % G.NIN;
            float hw = jin >= F.N ? MathF.Sqrt((float)K.BRAIN_START / MathF.Max(K.BRAIN_START, jin - F.N + 1)) : 1f;
            if (k < G.W2) g[k] = (sbyte)MathF.Round(u * K.FOUNDER_W * hw);
            else if (col < G.NH) g[k] = (sbyte)MathF.Round(u * K.FOUNDER_W2 * hs);
            else if (col == G.NH + F.BIAS) g[k] = (sbyte)(bias + (int)MathF.Round(u * K.FOUNDER_BIAS_NOISE));
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
    public static void VaryBrain(Span<sbyte> g, uint seed, uint key, int units = G.NH)
    {
        // the memory rows of units the brain does not use are left as the ancestor's (Revision M: with 256 units,
        // varying every byte of every founder took most of the founding time); they vary once they are grown
        for (int k = 0; k < G.LEN; k++)
        {
            if (k == units * G.NIN && k < G.W2) { k = G.W2 - 1; continue; }
            if (k == G.BODY) { k = G.BODY + G.NBODY - 1; continue; }
            g[k] = (sbyte)Math.Clamp((int)MathF.Round(g[k] + Rng.N(seed, 0, key, Rs.Founder + 20000u + (uint)k) * K.FOUNDER_W_SD), -127, 127);
        }
    }

    /// <summary>Mutation (DESIGN §7.3): v1's mutateWord rule on every byte.</summary>
    public static void Mutate(Span<sbyte> g, uint seed, uint step, uint key) => Mutate(g, seed, step, key, Defaults);
    static readonly EvoSettings Defaults = new();

    public static void Mutate(Span<sbyte> g, uint seed, uint step, uint key, EvoSettings e)
    {
        // body genes (each a polygenic trait) have their own rate; brain and reflex bytes MUT_RATE. Each byte mutates
        // with its rate independently; the mutated bytes are found by geometric skips (Revision M: a genome of
        // ~90,000 bytes made one random draw per byte the cost of every birth)
        uint j = 0;
        sbyte floor = g[G.B_FLOOR];
        Segment(g, 0, G.BODY, e.MutRate, seed, step, key, e, ref j);
        Segment(g, G.BODY, G.BODY + G.NBODY, e.MutBody, seed, step, key, e, ref j);
        Segment(g, G.BODY + G.NBODY, G.LEN, e.MutRate, seed, step, key, e, ref j);
        // the size class is copied exactly, and only rarely steps up or down one class
        float u = Rng.U(seed, step, key, Rs.Mutate + 0x3FFFFFF0u);
        if (u < K.FLOOR_MUT) floor = (sbyte)Math.Clamp(floor + (u < K.FLOOR_MUT * 0.5f ? -1 : 1), 0, K.FLOOR_CLASSES - 1);
        g[G.B_FLOOR] = floor;
    }

    static void Segment(Span<sbyte> g, int start, int end, float rate, uint seed, uint step, uint key, EvoSettings e, ref uint j)
    {
        if (rate <= 0f) return;
        float lq = MathF.Log(1f - MathF.Min(rate, 0.999f));
        int k = start - 1;
        while (true)
        {
            float u = Rng.U(seed, step, key, Rs.Mutate + 4u * j++);
            k += 1 + (int)MathF.Min(1e9f, MathF.Floor(MathF.Log(MathF.Max(1e-12f, 1f - u)) / lq));
            if (k >= end) return;
            bool big = Rng.U(seed, step, key, Rs.Mutate + 4u * j++) < e.MutBigShare;
            float d = Rng.N(seed, step, key, Rs.Mutate + 4u * j++) * (big ? e.MutBig : e.MutSmall);
            g[k] = (sbyte)Math.Clamp(g[k] + (int)MathF.Round(d), -127, 127);
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
        Array.Clear(w.HistV, i * BLOCK, BLOCK); w.VMean[i] = 0;
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
        // vocal learning (Revision N): on the call rows only, the match of its own utterances to its remembered tutors
        // (HistV) adds to the advantage, weighted by the learning gene: eta x (A + IMIT x V). With IMIT = 0 the call rows
        // use exactly the coefficients of every other row (Doupe & Kuhl 1999; Brainard & Doupe 2002)
        float c0v = c0, cAv = cA, cBv = cB, cCv = cC, cDv = cD;
        float imit = w.PImit[i];
        if (imit > 0f && w.Evo.VocalLearning)
        {
            float V0 = A0 + imit * w.HistV[hb], V1 = A1 + imit * w.HistV[hb + 1], V2 = A2 + imit * w.HistV[hb + 2], V3 = A3 + imit * w.HistV[hb + 3];
            c0v = eta * (V0 * lam + V1 * l2 + V2 * l3 + V3 * l4);
            cAv = eta * (V0 + V1 * lam + V2 * l2 + V3 * l3);
            cBv = eta * (V1 + V2 * lam + V3 * l2);
            cCv = eta * (V2 + V3 * lam);
            cDv = eta * V3;
        }
        fixed (float* W = &w.W2f[i * G.W2N])
        fixed (float* E = &w.Trace[i * G.W2N])
        fixed (float* D = &w.HistD[hb * P.N])
        fixed (float* Y = &w.HistY[hb * G.NIN])
        {
            float* y0 = Y, y1 = Y + G.NIN, y2 = Y + 2 * G.NIN, y3 = Y + 3 * G.NIN;
            var hi = Vector256.Create(K.WMAX); var lo = Vector256.Create(-K.WMAX);
            var vl4 = Vector256.Create(l4);
            int units = w.PBrain[i];
            for (int p = 0; p < P.N; p++)
            {
                bool call = p >= P.CALL_0;                                              // the call rows are the last F.NSOUND
                float k0 = call ? c0v : c0, kA = call ? cAv : cA, kB = call ? cBv : cB, kC = call ? cCv : cC, kD = call ? cDv : cD;
                var vc0 = Vector256.Create(k0);
                float d0 = D[p], d1 = D[P.N + p], d2 = D[2 * P.N + p], d3 = D[3 * P.N + p];
                float wA = kA * d0, wB = kB * d1, wC = kC * d2, wD = kD * d3;          // weight-update coefficients
                float eA = l3 * d0, eB = l2 * d1, eC = lam * d2, eD = d3;              // trace coefficients
                float* Wr = W + p * G.NIN, Er = E + p * G.NIN;
                var vwA = Vector256.Create(wA); var vwB = Vector256.Create(wB); var vwC = Vector256.Create(wC); var vwD = Vector256.Create(wD);
                var veA = Vector256.Create(eA); var veB = Vector256.Create(eB); var veC = Vector256.Create(eC); var veD = Vector256.Create(eD);
                // the inputs from memory units the brain does not have are always 0: their weights and traces stay
                // as they are, so only [0, units) and the features [NH, NIN) are updated (exactly the full rule)
                for (int seg = 0; seg < 2; seg++)
                {
                int k = seg == 0 ? 0 : G.NH, kend = seg == 0 ? units : G.NIN;
                for (; k <= kend - 8; k += 8)
                {
                    var e = Vector256.Load(Er + k);
                    var a = Vector256.Load(y0 + k); var b = Vector256.Load(y1 + k); var c = Vector256.Load(y2 + k); var d = Vector256.Load(y3 + k);
                    var dw = vc0 * e + vwA * a + vwB * b + vwC * c + vwD * d;
                    Vector256.Store(Vector256.Min(hi, Vector256.Max(lo, Vector256.Load(Wr + k) + dw)), Wr + k);
                    Vector256.Store(vl4 * e + veA * a + veB * b + veC * c + veD * d, Er + k);
                }
                for (; k < kend; k++)
                {
                    float e = Er[k];
                    float dw = k0 * e + wA * y0[k] + wB * y1[k] + wC * y2[k] + wD * y3[k];
                    Wr[k] = Math.Clamp(Wr[k] + dw, -K.WMAX, K.WMAX);
                    Er[k] = l4 * e + eA * y0[k] + eB * y1[k] + eC * y2[k] + eD * y3[k];
                }
                }
            }
        }
    }

    /// <summary>Pull learned weights back toward the genome (kappa), applied every 32 steps per slot.</summary>
    public static void Relax(World w, int i)
    {
        int g0 = i * G.LEN, o = i * G.W2N;
        float kappa = 32f * G.Gene(w.Genome[g0 + G.KAPPA], 0f, K.KAPPA_HI);
        int units = w.PBrain[i];
        for (int p = 0; p < P.N; p++)
            for (int seg = 0; seg < 2; seg++)
                for (int k = p * G.NIN + (seg == 0 ? 0 : G.NH), kend = p * G.NIN + (seg == 0 ? units : G.NIN); k < kend; k++)
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
        // the mating drive: full in males, weaker in females and hermaphrodites (DESIGN §7.7)
        float drive = (w.PSexual[i] == 0 ? 1f : w.PHerm[i] != 0 ? K.DRIVE_HERM : w.Sex[i] == 0 ? K.DRIVE_FEMALE : 1f) * w.Evo.MatingDrive;
        Reflex.Inputs(f, new Span<float>(c, G.NC), drive);
        fixed (float* hid = &w.Hidden[i * G.NH])
        fixed (sbyte* gen = &w.Genome[i * G.LEN])
        fixed (float* W = &w.W2f[i * G.W2N])
        fixed (float* yp = y)
        {
            for (int k = 0; k < G.NH; k++) u[F.N + k] = hid[k];
            int units = w.PBrain[i];                 // the brain-size gene: memory units beyond it do not exist
            for (int h = units; h < G.NH; h++) { hid[h] = 0f; yp[h] = 0f; u[F.N + h] = 0f; }
            int nu = F.N + units;                   // inputs that exist: the features and the units in use
            for (int h = 0; h < units; h++)
            {
                float v = MathK.Tanh(DotS8(gen + h * G.NIN, u, nu) * K.WSCALE);
                hid[h] = v; yp[h] = v;
            }
            for (int k = 0; k < F.N; k++) yp[G.NH + k] = f[k];
            sbyte* rx = gen + G.REFLEX;
            for (int p = 0; p < P.N; p++)
            {
                float sv = Dot(W + p * G.NIN, yp, units) + Dot(W + p * G.NIN + G.NH, yp + G.NH, F.N);
                sbyte* rr = rx + p * G.NC;
                float rs = 0;
                for (int k = 0; k < G.NC; k++) rs += rr[k] * c[k];
                sv += rs * K.RSCALE;
                z[p] = sv;
                g[p] = 1f / (1f + MathK.Exp(-sv));
            }
        }
    }

    /// <summary>Dot product of int8 weights and float inputs (8 at a time with AVX2).</summary>
    static unsafe float DotS8(sbyte* w, float* x, int n)
    {
        int k = 0; float s = 0;
        if (Avx2.IsSupported)
        {
            var acc = Vector256<float>.Zero;
            for (; k <= n - 8; k += 8)
                acc = Avx.Add(acc, Avx.Multiply(Avx.ConvertToVector256Single(Avx2.ConvertToVector256Int32(w + k)), Avx.LoadVector256(x + k)));
            s = Vector256.Sum(acc);
        }
        for (; k < n; k++) s += w[k] * x[k];
        return s;
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
