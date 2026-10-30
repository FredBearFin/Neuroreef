using System;
using Xunit;

namespace Neuroreef.Tests;

public class DecisionTests
{
    static (World world, int slot) Walker()
    {
        var w = new World(64, 17);
        Terrain.Build(w);
        Array.Fill(w.Cell, (byte)Realm.Land);
        w.Sp[0].Used = true; w.Sp[0].Alive = true;
        int i = Life.Found(w, 0, ReadOnlySpan<sbyte>.Empty, true, 1);
        Assert.True(i >= 0);
        w.Flags[i] = 0;
        return (w, i);
    }

    static void Move(World w, int i, float x, float y, float heading, float speed)
    {
        w.X[i] = x; w.Y[i] = y; w.IHeading[i] = heading; w.ISpeed[i] = speed;
        Resolve.Run(w, 1, 0);
    }

    [Fact]
    public void MovementStopsAtOneCellBarrierInsteadOfCrossingIt()
    {
        var (w, i) = Walker();
        w.Cell[2 * Map.FW + 4] = (byte)Realm.Water;

        Move(w, i, 10f, 10f, 0f, 12f);

        Assert.InRange(w.X[i], 15.99f, 16f);
        Assert.InRange(w.Speed[i], 5.9f, 6f);
    }

    [Fact]
    public void MovementWrapsAcrossTheWorldEdgeWhenCellsAllowIt()
    {
        var (w, i) = Walker();

        Move(w, i, Map.W - 2f, 10f, 0f, 4f);

        Assert.InRange(w.X[i], 1.99f, 2.01f);
        Assert.Equal(4f, w.Speed[i]);
    }

    [Fact]
    public void MovementStopsJustBeforeBlockedCellBoundary()
    {
        var (w, i) = Walker();
        w.Cell[2 * Map.FW + 4] = (byte)Realm.Water;

        Move(w, i, 15f, 10f, 0f, 4f);

        Assert.InRange(w.X[i], 15.99f, 16f);
        Assert.InRange(w.Speed[i], 0.99f, 1f);
    }
}
