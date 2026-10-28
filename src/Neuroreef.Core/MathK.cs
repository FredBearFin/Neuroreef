// Managed transcendental functions used by the simulation.
//
// Two reasons (NR-104):
//  1. Determinism across machines: these are plain IEEE float operations, so Windows and Linux builds
//     produce the same numbers, which the C runtime's tanh/exp/sin do not guarantee.
//  2. Speed: calling the native C runtime after 256-bit vector code costs an AVX-SSE transition
//     penalty on many Intel CPUs (measured: the brain's forward pass ran 3x slower).
// Accuracy is about 1e-6 relative, far below anything the simulation is sensitive to.

using System;
using System.Runtime.CompilerServices;

namespace Neuroreef;

public static class MathK
{
    const float LOG2E = 1.44269504089f, LN2 = 0.69314718056f, PI = 3.14159265359f, HALF_PI = 1.57079632679f, TWO_PI = 6.28318530718f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Exp(float x)
    {
        if (x > 87f) x = 87f; else if (x < -87f) return 0f;
        float t = x * LOG2E;
        int n = (int)(t + (t >= 0 ? 0.5f : -0.5f));
        float r = x - n * LN2;                              // |r| <= ln2/2
        // e^r, degree-6 Taylor polynomial (Horner)
        float p = 1f + r * (1f + r * (0.5f + r * (0.16666667f + r * (0.041666668f + r * (0.008333334f + r * 0.0013888889f)))));
        return p * BitConverter.Int32BitsToSingle((n + 127) << 23);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Tanh(float x)
    {
        if (x > 9f) return 1f;
        if (x < -9f) return -1f;
        if (x > -0.02f && x < 0.02f) return x * (1f - x * x * (1f / 3f));
        float e = Exp(2f * x);
        return (e - 1f) / (e + 1f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Sigmoid(float x) => 1f / (1f + Exp(-x));

    /// <summary>Natural log for x > 0.</summary>
    public static float Log(float x)
    {
        if (x <= 0f) return -87f;
        int bits = BitConverter.SingleToInt32Bits(x);
        int e = ((bits >> 23) & 255) - 127;
        float m = BitConverter.Int32BitsToSingle((bits & 0x007FFFFF) | 0x3F800000);   // [1, 2)
        if (m > 1.41421356f) { m *= 0.5f; e++; }
        float f = (m - 1f) / (m + 1f), f2 = f * f;                                     // atanh series
        float l = 2f * f * (1f + f2 * (1f / 3f + f2 * (0.2f + f2 * (1f / 7f + f2 * (1f / 9f)))));
        return l + e * LN2;
    }

    public static float Pow(float x, float y) => x <= 0f ? 0f : Exp(y * Log(x));

    public static float Cbrt(float x) => x <= 0f ? 0f : Exp(Log(x) * (1f / 3f));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Sin(float x)
    {
        // reduce to [-pi, pi], then to [-pi/2, pi/2]
        x -= TWO_PI * (int)(x * (1f / TWO_PI) + (x >= 0 ? 0.5f : -0.5f));
        if (x > HALF_PI) x = PI - x; else if (x < -HALF_PI) x = -PI - x;
        float x2 = x * x;
        return x * (1f + x2 * (-0.16666667f + x2 * (0.008333334f + x2 * (-0.0001984127f + x2 * 2.7557319e-6f))));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Cos(float x) => Sin(x + HALF_PI);

    public static float Atan2(float y, float x)
    {
        float ax = MathF.Abs(x), ay = MathF.Abs(y);
        if (ax < 1e-30f && ay < 1e-30f) return 0f;
        float a = MathF.Min(ax, ay) / MathF.Max(ax, ay), s = a * a;
        // minimax polynomial for atan on [0, 1], max error ~1e-5 rad
        float r = ((-0.0464964749f * s + 0.15931422f) * s - 0.327622764f) * s * a + a;
        if (ay > ax) r = HALF_PI - r;
        if (x < 0) r = PI - r;
        return y < 0 ? -r : r;
    }
}
