// The length of a step as a world setting (2026-11-02): World.StepH, K.STEP_H (2.92 h) by default. At the default the
// world is the reference run bit for bit; at a finer step the physics per unit of time is the same (basal metabolism,
// travel at a given speed, growth, a per-day mortality, the land's growth).

using System;
using Xunit;

namespace Neuroreef.Tests;

public class StepLengthTests
{
    /// <summary>FNV-1a over most of the animals' state and the fields.</summary>
    static ulong WorldHash(World w)
    {
        ulong h = 14695981039346656037UL;
        void Mix(float v) { h ^= (uint)BitConverter.SingleToInt32Bits(v); h *= 1099511628211UL; }
        for (int i = 0; i < w.Cap; i++)
        {
            h ^= w.StageOf[i]; h *= 1099511628211UL;
            if (w.StageOf[i] == St.Dead) continue;
            Mix(w.X[i]); Mix(w.Y[i]); Mix(w.Energy[i]); Mix(w.Heading[i]); Mix(w.Speed[i]); Mix(w.Gut[i]); Mix(w.GutEnergy[i]);
            Mix(w.Thirst[i]); Mix(w.Fatigue[i]); Mix(w.Age[i]); Mix(w.Pain[i]); Mix(w.Cooldown[i]); Mix(w.Timer[i]); Mix(w.Grown[i]);
            Mix(w.PgLoad[i]); Mix(w.ImmEff[i]); Mix(w.ImmMem[i]); Mix(w.PgDamage[i]); Mix(w.ThermStrain[i]); Mix(w.O2Debt[i]);
            Mix(w.PBasal[i]); Mix(w.PVmax[i]); Mix(w.PLife[i]); Mix(w.PGest[i]); Mix(w.RewardMean[i]);
            h ^= w.Serial[i]; h *= 1099511628211UL;
            Mix(w.W2f[(long)i * G.W2N + 7]);
        }
        h ^= (ulong)w.AliveCount; h *= 1099511628211UL;
        return h ^ ClimateTests.FieldsHash(w) * 31UL;
    }

    [Fact] // the default step is the reference run, bit for bit: a world with animals, climate on, 300 steps
    public void DefaultStepIsBitIdenticalToBefore()
    {
        Assert.Equal(K.STEP_H, Nature.StepH);
        using var sim = new Simulation(4096, 7, 2);
        Assert.Equal(1f, sim.W.Dt);
        for (int k = 0; k < 300; k++) sim.Step();
        Assert.True(sim.W.AliveCount > 0);
        // pinned on the code before the step became a setting (5565abc)
        Assert.Equal(14491964511445264084UL, WorldHash(sim.W));
    }
}
