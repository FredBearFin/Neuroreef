// The memory-reset control of the validation harness (World.MemoryReset, --validate): with it on, an animal's
// recurrent state from the last step has no effect on this step; with it off (the default), it does.

using System;
using Xunit;

namespace Neuroreef.Tests;

public class MemoryResetTests
{
    const int Cap = 4096, Warm = 30;

    static Simulation Make(bool reset)
    {
        var sim = new Simulation(Cap, 11, 2);
        sim.W.MemoryReset = reset;
        for (int k = 0; k < Warm; k++) sim.Step();
        return sim;
    }

    /// <summary>Overwrite the remembered activity of every living animal's units in use.</summary>
    static int Scramble(World w)
    {
        int n = 0;
        for (int i = 0; i < w.Cap; i++)
        {
            if (w.StageOf[i] < St.Juvenile) continue;
            for (int h = 0; h < w.PBrain[i]; h++) w.Hidden[i * G.NH + h] = (h & 1) == 0 ? 0.7f : -0.7f;
            n++;
        }
        return n;
    }

    static int Differing(World a, World b)
    {
        // a scrambled memory may change who is born or dies this step: count the slots alive in both, plus any not alive in both
        int d = 0;
        for (int i = 0; i < a.Cap; i++)
        {
            bool la = a.StageOf[i] >= St.Juvenile, lb = b.StageOf[i] >= St.Juvenile;
            if (la != lb) { d++; continue; }
            if (!la) continue;
            for (int h = 0; h < G.NH; h++) if (a.Hidden[i * G.NH + h] != b.Hidden[i * G.NH + h]) { d++; break; }
        }
        return d;
    }

    [Fact] // the default world keeps its memory: the flag is off, the state is non-zero and what it holds changes the next step
    public void DefaultLeavesRecurrentStateUntouched()
    {
        using var a = Make(false);
        using var b = Make(false);
        Assert.False(a.W.MemoryReset);
        double sum = 0; for (int k = 0; k < a.W.Hidden.Length; k++) sum += Math.Abs(a.W.Hidden[k]);
        Assert.True(sum > 0, "no animal holds any recurrent state");
        Assert.Equal(0, Differing(a.W, b.W));               // the twins are the same run
        Assert.True(Scramble(a.W) > 100);
        a.Step(); b.Step();
        int d = Differing(a.W, b.W);
        Assert.True(d > 0, "the remembered state had no effect with the reset off");
    }

    [Fact] // with the flag on, whatever the state held before the step is erased: the scrambled twin computes exactly what the clean one does
    public void ResetZeroesRecurrentStateEveryStep()
    {
        using var a = Make(true);
        using var b = Make(true);
        Assert.Equal(0, Differing(a.W, b.W));
        Assert.True(Scramble(a.W) > 100);
        Assert.True(Differing(a.W, b.W) > 100);
        a.Step(); b.Step();
        Assert.Equal(0, Differing(a.W, b.W));
        // and the learned weights are kept: they are the same in both twins and not reset to the genome
        int learned = 0;
        for (int i = 0; i < a.W.Cap && learned == 0; i++)
        {
            if (a.W.StageOf[i] < St.Juvenile) continue;
            for (int k = 0; k < G.W2N; k++)
            {
                float w = a.W.W2f[(long)i * G.W2N + k];
                Assert.Equal(b.W.W2f[(long)i * G.W2N + k], w);
                if (w != a.W.Genome[(long)i * G.LEN + G.W2 + k] * K.WSCALE) learned++;
            }
        }
        Assert.True(learned > 0, "no learned weight differs from the genome");
    }
}
