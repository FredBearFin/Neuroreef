// Utterances (DESIGN Revision N). An organism with a voice can make up to MAX_SYL of the 8 sounds in one step, each at
// most once, in the order of how strongly its brain intends each (Engesser & Townsend 2019: animal calls combine a few
// units into sequences). No sound and no sequence has a built-in meaning. Utterance types are the ordered sequences of
// 1..3 distinct sounds: 8 + 8x7 + 8x7x6 = 400, numbered here for the translator (display only).

using System;

namespace Neuroreef;

public static class Voice
{
    public const int NTYPE = F.NSOUND + F.NSOUND * (F.NSOUND - 1) + F.NSOUND * (F.NSOUND - 1) * (F.NSOUND - 2);   // 400
    public const int NTMPL = (F.NSOUND + 1) * F.NSOUND;   // template cells per slot: (start or previous sound) x next sound

    const uint CallBits = ((1u << F.NSOUND) - 1) << (P.CALL_0 - P.FIRST_ACTION);
    public static uint Bits => CallBits;

    /// <summary>The utterance in the fired call actions of slot i: the fired sounds by intended gain (w.Gain, highest
    /// first; ties to the lower sound), at most MAX_SYL. Writes sound numbers 1..8 into syl (0 after the last), returns how many.</summary>
    public static int Order(World w, int i, uint acts, Span<byte> syl)
    {
        syl[0] = syl[1] = syl[2] = 0;
        if ((acts & CallBits) == 0) return 0;
        int n = 0, gi = i * P.N + P.CALL_0;
        uint left = acts;
        for (; n < F.MAX_SYL; n++)
        {
            int best = -1; float bg = float.NegativeInfinity;
            for (int k = 0; k < F.NSOUND; k++)
                if ((left & (1u << (P.CALL_0 + k - P.FIRST_ACTION))) != 0 && w.Gain[gi + k] > bg) { bg = w.Gain[gi + k]; best = k; }
            if (best < 0) break;
            syl[n] = (byte)(best + 1);
            left &= ~(1u << (P.CALL_0 + best - P.FIRST_ACTION));
        }
        return n;
    }

    /// <summary>The call bits of an utterance (sounds 1..8, 0 = none).</summary>
    public static uint BitsOf(ReadOnlySpan<byte> syl)
    {
        uint b = 0;
        for (int p = 0; p < F.MAX_SYL; p++) if (syl[p] != 0) b |= 1u << (P.CALL_0 + syl[p] - 1 - P.FIRST_ACTION);
        return b;
    }

    /// <summary>Length of an utterance stored as MAX_SYL bytes.</summary>
    public static int Len(byte[] a, int o) => a[o] == 0 ? 0 : a[o + 1] == 0 ? 1 : a[o + 2] == 0 ? 2 : 3;

    /// <summary>The type number (0..399) of an utterance of sounds 1..8 (0 = none); -1 for none or a repeat.</summary>
    public static int TypeOf(int s0, int s1, int s2)
    {
        if (s0 == 0) return -1;
        int a = s0 - 1;
        if (s1 == 0) return a;
        if (s1 == s0) return -1;
        int b = s1 - 1, bi = b < a ? b : b - 1;                       // index among the 7 others
        if (s2 == 0) return F.NSOUND + a * (F.NSOUND - 1) + bi;
        if (s2 == s0 || s2 == s1) return -1;
        int c = s2 - 1, ci = c - (c > a ? 1 : 0) - (c > b ? 1 : 0);    // index among the 6 others
        return F.NSOUND + F.NSOUND * (F.NSOUND - 1) + (a * (F.NSOUND - 1) + bi) * (F.NSOUND - 2) + ci;
    }

    public static int TypeOf(byte[] a, int o) => TypeOf(a[o], a[o + 1], a[o + 2]);

    /// <summary>The sounds (1..8, 0 after the last) of utterance type t.</summary>
    public static int Decode(int t, Span<byte> syl)
    {
        syl[0] = syl[1] = syl[2] = 0;
        const int N1 = F.NSOUND, N2 = F.NSOUND * (F.NSOUND - 1);
        if (t < N1) { syl[0] = (byte)(t + 1); return 1; }
        if (t < N1 + N2)
        {
            int u = t - N1, a = u / (F.NSOUND - 1), bi = u % (F.NSOUND - 1), b = bi < a ? bi : bi + 1;
            syl[0] = (byte)(a + 1); syl[1] = (byte)(b + 1); return 2;
        }
        {
            int u = t - N1 - N2, ab = u / (F.NSOUND - 2), ci = u % (F.NSOUND - 2);
            int a = ab / (F.NSOUND - 1), bi = ab % (F.NSOUND - 1), b = bi < a ? bi : bi + 1;
            int lo = Math.Min(a, b), hi = Math.Max(a, b), c = ci;
            if (c >= lo) c++;
            if (c >= hi) c++;
            syl[0] = (byte)(a + 1); syl[1] = (byte)(b + 1); syl[2] = (byte)(c + 1); return 3;
        }
    }

    /// <summary>Template cell of a syllable pair: prev 0 = the start, 1..8 = the sound before; next 1..8.</summary>
    public static int Cell(int prev, int next) => prev * F.NSOUND + (next - 1);
}
