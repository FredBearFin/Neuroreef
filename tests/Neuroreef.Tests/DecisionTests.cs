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

    // ---- Revision P: local senses and path integration

    /// <summary>An all-land world whose only drinkable water is the column x = 0, and one walker whose brain does nothing
    /// but SEEK_WATER, facing +y at field column `col`.</summary>
    static (World world, int slot) ThirstyWalker(int col)
    {
        var (w, i) = Walker();
        int FW = Map.FW;
        for (int c = 0; c < FW * FW; c++) { int x = c % FW; w.DistDrink[c] = Math.Min(x, FW - x); }
        int g = i * G.LEN;
        w.Genome[g + G.SIGMA] = -128;
        for (int p = 0; p < P.N; p++)
        {
            int row = g + G.W2 + p * G.NIN;
            for (int k = 0; k < G.NIN; k++) w.Genome[row + k] = 0;
            w.Genome[row + G.NH + F.BIAS] = (sbyte)(p == P.SEEK_WATER ? 127 : -127);
        }
        for (int k = G.REFLEX; k < G.LEN; k++) w.Genome[g + k] = 0;
        Body.Express(w, i); Brain.InitLife(w, i);
        w.X[i] = col * K.FCELL + 2f; w.Y[i] = 2f * K.FCELL + 2f; w.Heading[i] = MathF.PI / 2f;
        w.Energy[i] = w.EmaxOf(i); w.Thirst[i] = 0.8f;
        Simulation.BuildFor(w);
        return (w, i);
    }

    static float TurnAfterDecide(World w, int i)
    {
        Decide.Run(w, i, new Decide.ProbeBuf());
        return w.IHeading[i] - MathF.PI / 2f;   // > 0: turned toward -x, where the water is
    }

    [Fact] // far from water an animal cannot steer toward it unless it remembers where it drank
    public void FarWaterIsFoundOnlyFromMemory()
    {
        // near water (3 cells): the local sense shows the way
        var (wn, n) = ThirstyWalker(3);
        PathMemory.WaterWay(wn, n, World.FIdx(wn.X[n], wn.Y[n]), out float nvx, out float nvy);
        Assert.True(nvx < 0f && MathF.Abs(nvy) < 1e-6f, $"near water: way ({nvx}, {nvy})");
        Assert.True(TurnAfterDecide(wn, n) > 0.9f * wn.PTurn[n], "near water it does not turn toward it");

        // far from water (100 cells, beyond SENSE_WATER) with no memory: no way is known, and it hardly turns
        var (w, i) = ThirstyWalker(100);
        int cell = World.FIdx(w.X[i], w.Y[i]);
        Assert.True(w.DistDrink[cell] > K.SENSE_WATER);
        PathMemory.WaterWay(w, i, cell, out float vx, out float vy);
        Assert.Equal(0f, vx); Assert.Equal(0f, vy);
        float blind = TurnAfterDecide(w, i);
        Assert.Equal(0f, w.Feat[i * F.N + F.DRINK_NEAR]);
        Assert.True(MathF.Abs(blind) < 0.25f * w.PTurn[i], $"with no memory it turned {blind}");

        // the same animal remembering that it drank 400 units to the -x side: it heads there
        var (wm, m) = ThirstyWalker(100);
        PathMemory.Drank(wm, m); wm.MemDrinkX[m] = -400f; wm.MemDrinkY[m] = 0f;
        float remembered = TurnAfterDecide(wm, m);
        Assert.True(remembered > 0.9f * wm.PTurn[m], $"with memory it turned {remembered}");
        // and senses it: the remembered water is to its left (it faces +y), not ahead, far away
        Assert.Equal(0f, wm.Feat[m * F.N + F.DRINK_AHEAD], 4);
        Assert.Equal(1f, wm.Feat[m * F.N + F.DRINK_LEFT], 4);
        Assert.Equal(1f / (1f + 400f / K.MEM_NEAR), wm.Feat[m * F.N + F.DRINK_NEAR], 4);
    }

    [Fact] // the drink vector follows the animal's own movement, with an error that grows with the way travelled
    public void PathIntegrationFollowsOwnMovementWithNoise()
    {
        var (w, i) = Walker();
        w.X[i] = 100f; w.Y[i] = 100f;
        PathMemory.Drank(w, i);
        float x0 = w.X[i], y0 = w.Y[i];
        double sumD2 = 0;
        for (int k = 0; k < 60; k++)
        {
            w.Step = (ulong)k + 1;
            w.IActs[i] = 0; w.IHeading[i] = 0.3f + 0.02f * k; w.ISpeed[i] = 1f; w.Energy[i] = w.EmaxOf(i); w.Thirst[i] = 0f;
            Resolve.Run(w, 1, 0);
            sumD2 += 1.0;
        }
        float tx = World.Delta1(x0 - w.X[i]), ty = World.Delta1(y0 - w.Y[i]);   // the true way back
        float ex = w.MemDrinkX[i] - tx, ey = w.MemDrinkY[i] - ty, err = MathF.Sqrt(ex * ex + ey * ey);
        float sd = K.PI_ERR * (float)Math.Sqrt(sumD2);   // per-axis SD of the summed error
        Assert.True(MathF.Sqrt(tx * tx + ty * ty) > 50f);
        Assert.True(err > 0f, "path integration is exact");
        Assert.True(err < 4f * sd, $"error {err} against expected SD {sd}");
        // drinking resets it
        PathMemory.Drank(w, i);
        Assert.Equal(0f, w.MemDrinkX[i]); Assert.Equal(Mem.Drink, w.MemHas[i] & Mem.Drink);
        // and a newborn remembers nothing
        PathMemory.Clear(w, i);
        Assert.Equal(0, w.MemHas[i]);
    }
}
