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
