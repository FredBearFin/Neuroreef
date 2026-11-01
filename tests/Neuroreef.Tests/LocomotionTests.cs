// Locomotion physics (2026-11-01): the load bones carry on land (no floor on the speed), and flight's warm muscle,
// load, take-off and ground clumsiness.

using System;
using Xunit;

namespace Neuroreef.Tests;

public class LocomotionTests
{
    [Fact] // land speed under load stays positive and finite for every body size (0.001 - 50,000 kg), armour and leg count,
           // with no floor; giants slow as their bones' stress rises, more legs share the load
    public void LandSpeedIsPositiveAndFiniteForAnyBody()
    {
        for (float m = 0.001f; m <= 50000f; m *= 1.5f)
            for (int pairs = 1; pairs <= 4; pairs++)
                for (float armour = 0f; armour <= K.ARMOUR_MAX; armour += 0.5f)
                {
                    float f = Body.LandLoadFactor(m, armour, pairs);
                    Assert.True(float.IsFinite(f) && f > 0f, $"M {m} pairs {pairs} armour {armour}: {f}");
                    Assert.True(f <= 1f + K.BONE_LOAD, $"M {m}: {f}");
                    // more legs share the load, armour adds to it
                    if (pairs > 1) Assert.True(f > Body.LandLoadFactor(m, armour, pairs - 1));
                    if (armour > 0f) Assert.True(f < Body.LandLoadFactor(m, armour - 0.5f, pairs));
                }
        // the reference body keeps the allometry's speed; posture keeps stress nearly constant up to ~300 kg
        Assert.Equal(1f, Body.LandLoadFactor(1f, 0f, 2), 5);
        Assert.InRange(Body.LandLoadFactor(0.1f, 0f, 2), 1f, 1.05f);
        Assert.InRange(Body.LandLoadFactor(40f, 0f, 2), 0.93f, 1f);
        // above it a straight leg cannot compensate: a plain 5 t body keeps much less of its muscles' speed than a 40 kg one
        Assert.True(Body.LandLoadFactor(5000f, 0f, 2) < 0.8f * Body.LandLoadFactor(40f, 0f, 2));
        // and four pairs of legs (the case that went negative before) give a 14 t and a 50 t body a slow, real speed
        Assert.InRange(Body.LandLoadFactor(14000f, 0f, 4), 0.3f, 1f);
        Assert.InRange(Body.LandLoadFactor(50000f, K.ARMOUR_MAX, 4), 0.05f, 1f);
        // expressed bodies at the ends of a giants' range: positive land speed
        BodySize.Set(0.001f, 50000f); Map.Set(TestWorld.Side);
        try
        {
            var w = new World(16, 1);
            foreach (sbyte mass in new sbyte[] { -127, 0, 127 })
                foreach (sbyte legn in new sbyte[] { -127, 127 })
                {
                    w.Genome[G.B_MASS] = mass; w.Genome[G.B_LEGN] = legn; w.Genome[G.B_LEGS] = 127; w.Genome[G.B_ARMOUR] = 127;
                    Body.Express(w, 0);
                    Assert.True(float.IsFinite(w.PVLand[0]) && w.PVLand[0] > 0f, $"M {w.PM[0]}: {w.PVLand[0]}");
                }
        }
        finally { BodySize.Reset(); Map.Set(TestWorld.Side); }
    }

    static World Flier(World w, sbyte endo, sbyte mass = -60)
    {
        w.Genome[G.B_WINGS] = 127; w.Genome[G.B_LEGS] = 60; w.Genome[G.B_FINS] = -127; w.Genome[G.B_MASS] = mass;
        w.Genome[G.B_INSUL] = -127; w.Genome[G.B_ENDO] = endo;
        w.Spc[0] = 0; w.StageOf[0] = St.Adult; Body.Express(w, 0);
        return w;
    }

    [Fact] // flight muscle needs a warm core (Bennett 1984; Heinrich 1993): a cold ectotherm cannot take off, a warm one
           // can, and a warm-blooded one can in the same cold
    public void AColdEctothermCannotTakeOff() => WithClimate(() =>
    {
        var w = new World(16, 3);
        Terrain.Build(w);
        int cell = -1;
        for (int c = 0; c < Map.FW * Map.FW; c++) if (w.Cell[c] == (byte)Realm.Land) { cell = c; break; }
        Assert.True(cell >= 0);
        float x = (cell % Map.FW + 0.5f) * K.FCELL, y = (cell / Map.FW + 0.5f) * K.FCELL;
        bool TakesOff(sbyte endo, float airT)
        {
            Array.Fill(w.Temp, airT);
            Flier(w, endo);
            w.X[0] = x; w.Y[0] = y; w.Speed[0] = 0f; w.Energy[0] = w.EmaxOf(0) * 0.9f; w.Thirst[0] = 0f;
            w.Gut[0] = 0f; w.GutEnergy[0] = 0f; w.ThermStrain[0] = 0f;
            w.IActs[0] = 0u; w.ISpeed[0] = w.PVAir[0]; w.IHeading[0] = 0f; w.ITarget[0] = -1; w.IMate[0] = -1; w.IScanning[0] = 0;
            w.IFoodHere[0] = 0f;
            Assert.Equal(Loco.Fly, w.LocoOf(0));
            Assert.False(w.Airborne(0));   // it starts on the ground
            Resolve.Run(w, 1, 0);
            return w.Airborne(0);
        }
        // cold-blooded at 0 °C: its flight muscle is far too slow to lift it
        Array.Fill(w.Temp, 0f);
        float core = Heat.Core(Heat.Operative(w, cell, 0f, false), 0f);
        Assert.True(Heat.Rate(core) < K.FLY_WING_MIN, $"core {core}");
        Assert.False(TakesOff(-127, 0f));
        // the same body on a hot day, warm, flies
        Assert.True(TakesOff(-127, K.T_BODY));
        // warm-blooded, it flies at 0 °C
        Assert.True(TakesOff(127, 0f));
    });

    [Fact] // a full gut raises wing loading: flight costs more per hour and per distance, and a heavy meal can ground it
    public void FlightCostRisesWithLoad() => WithClimate(() =>
    {
        var w = Flier(new World(16, 1), 127);
        float m = w.PM[0];
        float vmp = w.PVAir[0] / Body.TopFlightSpeed(Body.FlightMargin(w.PWings[0], m));
        float Cost(float gut, bool canLand) { w.Gut[0] = gut; return Resolve.MoveCost(w, 0, m, 1f, vmp, 0, true, canLand); }
        float empty = Cost(0f, false), full = Cost(0.2f * m, false);
        Assert.True(full > empty * 1.2f, $"empty {empty}, loaded {full}");   // least power ~(1.2)^1.5 = 1.31
        Assert.True(Cost(0.2f * m, true) > Cost(0f, true));                // per distance too (the duty share)
        // taking off is dearer than cruising: a step that starts on the ground pays for a climb-out
        w.Gut[0] = 0f;
        Assert.True(Resolve.MoveCost(w, 0, m, 1f, 0f, 0, true, true) > 0f);
        // half wings (gene 0 ~ 0.5) on a light body: empty it takes off, with a gut a fifth of its mass it cannot (Marden 1987)
        Assert.True(Body.CanTakeOff(0.5f, 0.2f, 0.2f, 0f, 1f));
        Assert.False(Body.CanTakeOff(0.5f, 0.2f, 0.2f, 0.04f, 1f));
        // and its own chemistry's cold slows it as a load does
        Assert.False(Body.CanTakeOff(1f, 0.2f, 0.2f, 0f, Heat.Rate(15f)));
        Assert.True(Body.CanTakeOff(1f, 0.2f, 0.2f, 0f, Heat.Rate(K.T_BODY)));
    });

    [Fact] // very large bodies cannot fly at all, whatever their wings; flight's power curve and ceiling come from mass
    public void VeryLargeBodiesCannotFly()
    {
        foreach (float m in new[] { 20f, 100f, 1000f, 50000f })
        {
            Assert.True(Body.FlightMargin(1f, m) < 1f);
            Assert.False(Body.CanTakeOff(1f, m, m, 0f, 1f));
        }
        BodySize.Set(0.001f, 50000f); Map.Set(TestWorld.Side);
        try
        {
            var w = new World(16, 1);
            w.Genome[G.B_WINGS] = 127; w.Genome[G.B_MASS] = 127; Body.Express(w, 0);
            Assert.NotEqual(Loco.Fly, w.LocoOf(0));
            Assert.Equal(0f, w.PVAir[0]);
        }
        finally { BodySize.Reset(); Map.Set(TestWorld.Side); }
    }

    [Fact] // a flier walks slower than the same body without flight muscle (it carries it as dead load)
    public void AWingHeavyFlierWalksSlower()
    {
        var w = new World(16, 1);
        w.Genome[G.B_LEGS] = 127; w.Genome[G.B_MASS] = -60; w.Genome[G.B_WINGS] = -127; Body.Express(w, 0);
        float walker = w.PVLand[0];
        w.Genome[G.B_WINGS] = 127; Body.Express(w, 0);
        Assert.Equal(Loco.Fly, w.LocoOf(0));
        Assert.Equal(walker * (1f - K.FLY_MUSCLE_MAX), w.PVLand[0], 3);
    }

    static void WithClimate(Action a)
    {
        float climate = Nature.Climate; Nature.Climate = 1f;
        try { a(); } finally { Nature.Climate = climate; }
    }
}
