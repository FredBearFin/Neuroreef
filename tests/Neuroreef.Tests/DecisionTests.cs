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

    static (float[] features, float heading, float speed, int target, int mate, uint actions) DecideCrowd(bool reverse)
    {
        var w = new World(96, 19);
        Terrain.Build(w);
        Array.Fill(w.Cell, (byte)Realm.Land);
        w.Sp[0].Used = true; w.Sp[0].Alive = true;
        for (uint key = 1; key <= 72; key++)
        {
            int i = Life.Found(w, 0, ReadOnlySpan<sbyte>.Empty, true, key);
            Assert.True(i >= 0);
            w.Flags[i] = 0;
            w.X[i] = 5f + ((key - 1) % 8) * 1.2f;
            w.Y[i] = 5f + ((key - 1) / 8) * 1.1f;
        }
        Simulation.BuildFor(w);
        if (reverse)
            for (int c = 0; c < Map.GW * Map.GW; c++)
                Array.Reverse(w.Sorted, w.CellStart[c], w.CellStart[c + 1] - w.CellStart[c]);
        Decide.Run(w, 0, new Decide.ProbeBuf());
        var features = new float[F.N];
        Array.Copy(w.Feat, features, F.N);
        return (features, w.IHeading[0], w.ISpeed[0], w.ITarget[0], w.IMate[0], w.IActs[0]);
    }

    [Fact]
    public void MovementStopsAtOneCellBarrierInsteadOfCrossingIt()
    {
        var (w, i) = Walker();
        int barrier = 2 * Map.FW + 4;
        w.Cell[barrier] = (byte)Realm.Deep; w.Depth[barrier] = w.MaxWaterDepth(i) + K.SHALLOW;

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
    public void MovementChecksRealmAcrossTheWrappedEdge()
    {
        var (w, i) = Walker();
        int barrier = 2 * Map.FW;
        w.Cell[barrier] = (byte)Realm.Deep; w.Depth[barrier] = w.MaxWaterDepth(i) + K.SHALLOW;

        Move(w, i, Map.W - 2f, 10f, 0f, 8f);

        Assert.InRange(w.X[i], Map.W - 0.01f, Map.W);
        Assert.InRange(w.Speed[i], 1.99f, 2f);
    }

    [Fact]
    public void MovementStopsJustBeforeBlockedCellBoundary()
    {
        var (w, i) = Walker();
        int barrier = 2 * Map.FW + 4;
        w.Cell[barrier] = (byte)Realm.Deep; w.Depth[barrier] = w.MaxWaterDepth(i) + K.SHALLOW;

        Move(w, i, 15f, 10f, 0f, 4f);

        Assert.InRange(w.X[i], 15.99f, 16f);
        Assert.InRange(w.Speed[i], 0.99f, 1f);
    }

    [Fact]
    public void NeighbourChoiceDoesNotDependOnSpatialHashOrder()
    {
        var forward = DecideCrowd(false);
        var reversed = DecideCrowd(true);

        Assert.Equal(forward.features, reversed.features);
        Assert.Equal(forward.heading, reversed.heading);
        Assert.Equal(forward.speed, reversed.speed);
        Assert.Equal(forward.target, reversed.target);
        Assert.Equal(forward.mate, reversed.mate);
        Assert.Equal(forward.actions, reversed.actions);
    }

    [Fact]
    public void WaterDepthControlsAccessAndSwimmingSpeedContinuously()
    {
        var (w, i) = Walker();
        int shallow = 3 * Map.FW + 3, deep = shallow + 1;
        w.Cell[shallow] = w.Cell[deep] = (byte)Realm.Deep;
        w.Depth[shallow] = K.SHALLOW * 0.5f;
        w.Depth[deep] = K.SHALLOW * 5f;

        w.Genome[i * G.LEN + G.B_FINS] = -127;
        Body.Express(w, i);
        Assert.True(w.CellAllows(i, shallow));
        Assert.False(w.CellAllows(i, deep));
        float lowFinSpeed = w.Vmax(i, shallow);

        w.Genome[i * G.LEN + G.B_FINS] = 127;
        Body.Express(w, i);
        Assert.True(w.CellAllows(i, deep));
        Assert.True(w.Vmax(i, shallow) > w.Vmax(i, deep));
        Assert.True(w.Vmax(i, shallow) > lowFinSpeed);
    }

    [Fact]
    public void DepthSenseTracksLocalWaterDepth()
    {
        var (w, i) = Walker();
        int cell = World.FIdx(w.X[i], w.Y[i]);
        w.Cell[cell] = (byte)Realm.Deep;
        w.Depth[cell] = K.SHALLOW * 3f;

        Decide.Run(w, i, new Decide.ProbeBuf());

        Assert.Equal(0.2f, w.Feat[i * F.N + F.DEPTH], 4);
    }

    [Fact]
    public void AlgaeYieldFallsWithWaterDepth()
    {
        var w = new World(1, 23);
        int shallow = 0, deep = 1;
        w.Cell[shallow] = (byte)Realm.Shallow; w.Depth[shallow] = K.SHALLOW * 0.5f;
        w.Cell[deep] = (byte)Realm.Deep; w.Depth[deep] = K.SHALLOW * 5f;
        w.Nutrient[shallow] = w.Nutrient[deep] = 1f;
        w.Pool[(int)Pool.Algae][shallow] = w.Pool[(int)Pool.Algae][deep] = 0.1f;
        var diet = new float[Pools.N]; diet[(int)Pool.Algae] = 1f;

        Assert.True(Fields.Yield(w, shallow, diet) > Fields.Yield(w, deep, diet));
    }
}
