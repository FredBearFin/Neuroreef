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

    // ------------------------------------------------------------------ invariance: the same physics per unit of time

    const float Fine = 10f / 60f;   // a 10-minute step, in hours

    /// <summary>Run f with worlds built at a step of stepH hours (and no climate when noClimate), putting both back after.</summary>
    static T At<T>(float stepH, Func<T> f, bool noClimate = true)
    {
        float h0 = Nature.StepH, c0 = Nature.Climate;
        Nature.StepH = stepH; if (noClimate) Nature.Climate = 0f;
        try { return f(); } finally { Nature.StepH = h0; Nature.Climate = c0; }
    }

    /// <summary>A small world on its relief (no water cycle or plants) and the land cell with the longest run of land to its
    /// east, so a walker heading east stays on land.</summary>
    static (World w, float x, float y) Land(uint seed = 3)
    {
        var w = new World(16, seed);
        Terrain.Build(w);
        int best = -1, bestRun = -1, FW = Map.FW;
        for (int c = 0; c < FW * FW; c += 3)
        {
            int run = 0, cx = c % FW, cy = c / FW;
            while (run < FW && w.Cell[cy * FW + (cx + run) % FW] == (byte)Realm.Land) run++;
            if (run > bestRun) { bestRun = run; best = c; }
        }
        Assert.True(bestRun >= 40, $"a run of {bestRun} land cells");
        return (w, (best % FW + 0.5f) * K.FCELL, (best / FW + 0.5f) * K.FCELL);
    }

    /// <summary>A plain walker of about 1 kg in slot 0 (no wings or fins, no fur), adult unless juvenile.</summary>
    static void Walker(World w, float x, float y, bool juvenile = false)
    {
        w.Genome[G.B_WINGS] = -127; w.Genome[G.B_LEGS] = 100; w.Genome[G.B_FINS] = -127; w.Genome[G.B_MASS] = -20;
        w.Genome[G.B_INSUL] = -127;
        w.Spc[0] = 0; w.StageOf[0] = juvenile ? St.Juvenile : St.Adult; Body.Express(w, 0);
        w.X[0] = x; w.Y[0] = y; w.Energy[0] = w.EmaxOf(0) * 0.9f; w.Thirst[0] = 0f; w.Gut[0] = 0f; w.GutEnergy[0] = 0f;
        w.Grown[0] = juvenile ? 0.2f : 1f; w.Age[0] = juvenile ? 0f : w.PMature[0] * 2f;
    }

    static void Intend(World w, uint acts, float speed)
    {
        w.IActs[0] = acts; w.ISpeed[0] = speed; w.IHeading[0] = 0f; w.ITarget[0] = -1; w.IMate[0] = -1; w.IScanning[0] = 0; w.IFoodHere[0] = 0f;
    }

    static int StepsIn(World w, float hours) => (int)MathF.Round(hours / w.StepH);

    static readonly uint Rest = 1u << (P.REST - P.FIRST_ACTION);

    [Fact] // a lone resting adult burns the same energy an hour at a 2.92-hour step and a 10-minute one
    public void BasalEnergyAnHourIsTheSameAtAFineStep()
    {
        float PerHour(float stepH) => At(stepH, () =>
        {
            var (w, x, y) = Land();
            Walker(w, x, y);
            int n = StepsIn(w, 24f);
            double spent = 0;
            for (int k = 0; k < n; k++)
            {
                w.Thirst[0] = 0f; w.Energy[0] = w.EmaxOf(0) * 0.9f;
                float e0 = w.Energy[0];
                Intend(w, Rest, 0f);
                Resolve.Run(w, 1, 0);
                Assert.NotEqual(St.Dead, w.StageOf[0]);
                spent += e0 - w.Energy[0];
                w.Step++;
            }
            return (float)(spent / (n * w.StepH));
        });
        float coarse = PerHour(K.STEP_H), fine = PerHour(Fine);
        Assert.True(coarse > 0f);
        Assert.InRange(fine / coarse, 0.98f, 1.02f);
    }

    [Fact] // walking at a set share of its top speed it covers the same ground an hour at either step
    public void DayRangeAtAFixedSpeedIsTheSameAtAFineStep()
    {
        (float perHour, float topPerHour) Travel(float stepH) => At(stepH, () =>
        {
            var (w, x, y) = Land();
            Walker(w, x, y);
            int n = StepsIn(w, 3f * K.STEP_H);   // ~9 hours
            double moved = 0;
            for (int k = 0; k < n; k++)
            {
                w.Thirst[0] = 0f; w.Energy[0] = w.EmaxOf(0) * 0.9f; w.Fatigue[0] = 0f;
                float x0 = w.X[0], y0 = w.Y[0];
                Intend(w, 0u, 0.08f * w.PVLand[0]);
                Resolve.Run(w, 1, 0);
                Assert.NotEqual(St.Dead, w.StageOf[0]);
                float dx = World.Delta1(w.X[0] - x0), dy = World.Delta1(w.Y[0] - y0);
                moved += MathF.Sqrt(dx * dx + dy * dy);
                w.Step++;
            }
            return ((float)(moved * K.M_PER_UNIT / (n * w.StepH)), w.PVLand[0] * K.M_PER_UNIT / w.StepH);
        });
        var c = Travel(K.STEP_H); var f = Travel(Fine);
        Assert.True(c.perHour > 10f, $"{c.perHour} m/h");
        Assert.InRange(f.topPerHour / c.topPerHour, 0.999f, 1.001f);   // top speed in m/h
        Assert.InRange(f.perHour / c.perHour, 0.95f, 1.05f);           // ground covered (footing varies along the way)
    }

    [Fact] // a well-fed juvenile grows as much in two days at either step
    public void JuvenileGrowthOverDaysIsTheSameAtAFineStep()
    {
        float Grew(float stepH) => At(stepH, () =>
        {
            var (w, x, y) = Land();
            Walker(w, x, y, juvenile: true);
            int n = StepsIn(w, 16f * K.STEP_H);   // ~2 days
            for (int k = 0; k < n; k++)
            {
                w.Thirst[0] = 0f; w.Energy[0] = w.EmaxOf(0);
                Intend(w, Rest, 0f);
                Resolve.Run(w, 1, 0);
                Assert.Equal(St.Juvenile, w.StageOf[0]);
                w.Step++;
            }
            return (w.Grown[0] - 0.2f) / (n * w.StepH);
        });
        float coarse = Grew(K.STEP_H), fine = Grew(Fine);
        Assert.True(coarse > 0f);
        Assert.InRange(fine / coarse, 0.97f, 1.03f);
    }

    [Fact] // a probability per reference step gives the same survival over a day at any step (World.PStep), and so does
           // the old-age mortality of RESOLVE: a population of old animals loses the same share in a day
    public void PerDayMortalityIsTheSameAtAFineStep()
    {
        var wc = At(K.STEP_H, () => new World(16, 1)); var wf = At(Fine, () => new World(16, 1));
        float p = 0.01f;
        double dayC = Math.Pow(1 - wc.PStep(p), wc.StepsPerDay), dayF = Math.Pow(1 - wf.PStep(p), wf.StepsPerDay);
        Assert.Equal(p, wc.PStep(p));
        Assert.InRange(dayF / dayC, 0.9999, 1.0001);
        Assert.InRange(wf.Keep(0.97f), 0.997f, 0.999f);

        // old-age deaths through RESOLVE: 2048 animals at twice their lifespan (0.002 a^2 = 0.008 a reference step)
        (int dead, double expect) Deaths(float stepH) => At(stepH, () =>
        {
            const int N = 2048;
            var w = new World(N, 11);
            Terrain.Build(w);
            int cell = -1;
            for (int c = 0; c < Map.FW * Map.FW; c++) if (w.Cell[c] == (byte)Realm.Land) { cell = c; break; }
            float x = (cell % Map.FW + 0.5f) * K.FCELL, y = (cell / Map.FW + 0.5f) * K.FCELL;
            w.Sp[0].Used = true; w.Sp[0].Alive = true;
            for (int k = 0; k < N; k++)
            {
                int a = Life.Found(w, 0, ReadOnlySpan<sbyte>.Empty, true, (uint)k + 1);
                Assert.True(a >= 0);
                w.Genome[a * G.LEN + G.B_WINGS] = -127; w.Genome[a * G.LEN + G.B_LEGS] = 100; w.Genome[a * G.LEN + G.B_FINS] = -127;
                Body.Express(w, a);
                w.X[a] = x; w.Y[a] = y; w.Flags[a] = 0; w.Age[a] = 2f * w.PLife[a];
            }
            int n = StepsIn(w, 24f);
            double expect = N * (1 - Math.Pow(1 - 0.008, n * w.Dt));
            for (int k = 0; k < n; k++)
            {
                for (int a = 0; a < N; a++)
                {
                    if (w.StageOf[a] == St.Dead) continue;
                    w.Thirst[a] = 0f; w.Energy[a] = w.EmaxOf(a) * 0.9f; w.Pain[a] = 0f;
                    w.IActs[a] = Rest; w.ISpeed[a] = 0f; w.IHeading[a] = 0f; w.ITarget[a] = -1; w.IMate[a] = -1; w.IScanning[a] = 0; w.IFoodHere[a] = 0f;
                }
                Resolve.Run(w, 1, 0);
                w.Step++;
            }
            int dead = 0;
            for (int a = 0; a < N; a++) if (w.StageOf[a] == St.Dead) dead++;
            return (dead, expect);
        });
        var dc = Deaths(K.STEP_H); var df = Deaths(Fine);
        // ~6% a day: ~130 of 2048, SD ~11
        Assert.InRange(dc.dead, dc.expect - 45, dc.expect + 45);
        Assert.InRange(df.dead, df.expect - 45, df.expect + 45);
        // (each expectation is for its own run's length: 8 steps of 2.92 h are 23.4 h, 144 of 10 min 24 h)
    }

    [Fact] // the land runs on the reference clock: plants, fields and water after the same time are the same, bit for bit
    public void PlantsAndFieldsGrowTheSameAtAFineStep()
    {
        ulong Fields(float stepH) => At(stepH, () =>
        {
            using var sim = new Simulation(64, 4, 1, empty: true);
            var w = sim.W;
            while (w.RefStep(w.Step) < 3u * (ulong)K.WC_EVERY / 2) sim.Step();   // 75 reference steps (~9 days), a water-cycle update in
            return ClimateTests.FieldsHash(w);
        }, noClimate: false);
        Assert.Equal(Fields(K.STEP_H), Fields(Fine));
    }

    [Fact] // a checkpoint keeps the length of its world's step (format 26): its header sets Nature, its load the world
    public void ACheckpointKeepsTheStepLength()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"nr-step-{Environment.ProcessId}.nrw");
        float h0 = Nature.StepH;
        try
        {
            At(Fine, () =>
            {
                using var sim = new Simulation(64, 5, 1, empty: true);
                for (int k = 0; k < 20; k++) sim.Step();
                Checkpoint.SaveWorld(sim.W, path);
                return 0;
            }, noClimate: false);
            Nature.StepH = K.STEP_H;
            using var back = new Simulation(64, 5, 1, empty: true, restore: true);
            Assert.Equal(1f, back.W.Dt);
            Checkpoint.LoadWorld(back.W, path);
            Assert.Equal(Fine, Nature.StepH);
            Assert.Equal(Fine, back.W.StepH);
            Assert.Equal(20UL, back.W.Step);
        }
        finally { Nature.StepH = h0; if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
    }

    [Fact] // a world of animals at a 10-minute step runs a simulated day: no NaN, animals alive, the land on its clock
    public void AFineStepWorldRunsADay()
    {
        At(Fine, () =>
        {
            using var sim = new Simulation(4096, 7, 2);
            var w = sim.W;
            Assert.InRange(w.Dt, 0.057f, 0.0571f);
            int alive0 = w.AliveCount, n = StepsIn(w, 24f);
            for (int k = 0; k < n; k++) sim.Step();
            Assert.True(w.AliveCount > alive0 / 2, $"{w.AliveCount} of {alive0} alive after a day");
            for (int i = 0; i < w.Cap; i++)
                if (w.StageOf[i] != St.Dead) Assert.True(float.IsFinite(w.Energy[i]) && float.IsFinite(w.X[i]), $"slot {i}");
            Assert.InRange(w.RefStep(w.Step), 8UL, 9UL);   // ~8.2 reference steps in a day
            return 0;
        }, noClimate: false);
    }
}
