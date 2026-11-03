// The command line and the console's status line (no world is built).

using Neuroreef.Server;
using Xunit;

namespace Neuroreef.Tests;

public class OptionsTests
{
    [Fact] // the status line's rate is the steps since the last line: a paused or stalled run reads 0, not its last rate
    public void StatusRateIsZeroWhenTheStepStands()
    {
        Assert.Equal(0, Host.StatusRate(false, true, 500, 500, 10, 3.0));
        Assert.Equal(0, Host.StatusRate(false, false, 500, 500, 10, 3.0));   // stalled without a pause: still 0
        Assert.Equal(3.0, Host.StatusRate(false, false, 530, 500, 10, 99.0), 6);
        Assert.Equal(3.0, Host.StatusRate(true, false, 530, 0, 10, 3.0), 6);   // the first line of a phase: the sim's own measure
        Assert.Equal(0, Host.StatusRate(true, true, 530, 0, 10, 3.0));         // ... unless paused
    }

    [Fact] // the prehistory and the world have their own steps (2026-11-02), and the prehistory's length is a time
    public void StepLengthsAndPrehistoryLength()
    {
        float h0 = Nature.StepH, w0 = Nature.WorldStepH; int wm0 = Nature.WorldMap;
        try
        {
            var d = Options.Parse(new string[0]);
            Assert.Equal(K.STEP_H, d.PreStepH); Assert.Equal(K.STEP_H, d.WorldStepH);
            Assert.Equal(K.PREHISTORY_STEPS, d.PrehistorySettings(false).TotalSteps);   // the default: 500,000 steps
            Assert.Equal(K.STEP_H, Options.StepHFromMinutes(K.STEP_H * 60f + 0.001f));  // the setup's default comes back exact
            var o = Options.Parse(new[] { "--world-step-minutes", "10" });
            Assert.Equal(K.STEP_H, o.PreStepH); Assert.Equal(10f / 60f, o.WorldStepH, 6);
            o.ApplyNature(prehistory: true);
            Assert.Equal(K.STEP_H, Nature.StepH); Assert.Equal(10f / 60f, Nature.WorldStepH, 6);
            o.ApplyNature(prehistory: false);
            Assert.Equal(10f / 60f, Nature.StepH, 6);
            var p = Options.Parse(new[] { "--pre-step-minutes", "60", "--prehistory-years", "10" });
            Assert.Equal(30000, p.PrehistorySteps);                                    // 10 years of reference steps
            Assert.InRange(p.PrehistorySettings(false).TotalSteps, 87_600, 87_700);    // 10 years of 1-hour steps
            Assert.Equal(1f / 60f, Options.Parse(new[] { "--step-minutes", "1" }).WorldStepH, 6);
        }
        finally { Nature.StepH = h0; Nature.WorldStepH = w0; Nature.WorldMap = wm0; Map.Set(TestWorld.Side); BodySize.Reset(); }
    }

    [Fact] // the default start: a quarter of the memory over the bytes of a slot, in whole blocks, clamped
    public void DefaultSlotsFollowMemory()
    {
        long gb = 1L << 30;
        int pc16 = World.DefaultSlots(16 * gb, 1);
        Assert.InRange(pc16, 18_000, 20_000);                                   // ~4 GB of a 16 GB PC
        Assert.InRange((long)pc16 * World.BytesPerSlot, 3.5 * gb, 4.0 * gb);
        Assert.Equal(0, pc16 % World.GROW_BLOCK);
        Assert.True(World.DefaultSlots(8 * gb, 1) < pc16);
        Assert.Equal(World.DEFAULT_SLOTS_LO, World.DefaultSlots(2 * gb, 1));
        Assert.Equal(World.DEFAULT_SLOTS_HI, World.DefaultSlots(256 * gb, 1));
        Assert.True(World.DefaultSlots(16 * gb, 2) <= pc16 / 2);              // branches share it
        var o = Options.Parse(new[] { "--headless" });
        Assert.Equal(World.DefaultSlots(), o.Capacity);
        Assert.Equal(World.DefaultSlots(1), o.BranchSlots);
        Assert.Equal(World.DefaultSlots(4), Options.Parse(new[] { "--branches", "4" }).BranchSlots);
        Assert.Equal(20480, Options.Parse(new[] { "--capacity", "20480" }).Capacity);
    }

    [Fact] // the Body Lab's flags (BodyLab.cs): environment, seeds (a count from --seed, or a list), generations, population
    public void BodyLabFlags()
    {
        var d = Options.Parse(new[] { "--bodylab" });
        Assert.True(d.BodyLab); Assert.True(d.LabServe);
        Assert.Equal("all", d.LabEnv); Assert.Equal(new uint[] { 1 }, d.LabSeedList());
        Assert.Equal(800, d.LabGenerations); Assert.Equal(200, d.LabPopulation);
        var o = Options.Parse(new[] { "--bodylab", "--env", "sea-floor", "--seeds", "3", "--generations", "300", "--population", "120", "--every", "5", "--out", "x", "--no-serve", "--port", "5900" });
        Assert.Equal("sea-floor", o.LabEnv); Assert.Equal(new uint[] { 1, 2, 3 }, o.LabSeedList());
        Assert.Equal(300, o.LabGenerations); Assert.Equal(120, o.LabPopulation); Assert.Equal(5, o.LabEvery);
        Assert.Equal("x", o.LabOut); Assert.False(o.LabServe); Assert.Equal(5900, o.Port);
        Assert.Equal(new uint[] { 7, 8 }, Options.Parse(new[] { "--bodylab", "--seed", "7", "--seeds", "2" }).LabSeedList());
        Assert.Equal(new uint[] { 3, 7, 9 }, Options.Parse(new[] { "--bodylab", "--seeds", "3,7,9" }).LabSeedList());
        Assert.Equal("open-water", Options.Parse(new[] { "--env", "Open_Water" }).LabEnv);
        Assert.Equal("land", Options.Parse(new[] { "--env", "land" }).LabEnv);
        Assert.Throws<System.ArgumentException>(() => Options.Parse(new[] { "--bodylab", "--env", "space" }));
        Assert.False(Options.Parse(new string[0]).BodyLab);
    }

    [Fact] // --map sizes the world too unless --world-map is given (the NR_ECO example in RUN.txt runs on the valley)
    public void MapSetsTheWorldUnlessWorldMapIsGiven()
    {
        var o = Options.Parse(new[] { "--headless", "--random-brains", "--map", "valley", "--seed", "7", "--steps", "2000", "--report", "300", "--threads", "4" });
        Assert.Equal(256, o.PreMap); Assert.Equal(256, o.WorldMap);
        Assert.Equal(7u, o.Seed); Assert.Equal(2000, o.Steps); Assert.Equal(300, o.ReportEvery); Assert.Equal(4, o.Threads);
        Assert.True(o.Headless); Assert.True(o.RandomBrains);
        var p = Options.Parse(new[] { "--world-map", "region", "--map", "valley" });
        Assert.Equal(256, p.PreMap); Assert.Equal(512, p.WorldMap);
        var q = Options.Parse(new[] { "--map", "valley", "--world-map", "continent" });
        Assert.Equal(1024, q.WorldMap);
        Assert.Equal(2048, Options.Parse(new string[0]).WorldMap);
    }
}
