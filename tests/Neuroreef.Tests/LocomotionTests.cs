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
            w.X[0] = x; w.Y[0] = y; w.Speed[0] = 0f; w.Flags[0] = Flag.Landed; w.Energy[0] = w.EmaxOf(0) * 0.9f; w.Thirst[0] = 0f;
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

    [Fact] // wings on the ground: one continuous function of the wing gene for flier and non-flier alike (wing-assisted
           // running against the flight muscle's dead load), no jump at the flight threshold, and a wing-heavy flier slow
    public void WingsOnTheGroundAreOneContinuousFunction()
    {
        var w = new World(16, 1);
        w.Genome[G.B_LEGS] = 127; w.Genome[G.B_MASS] = -60; w.Genome[G.B_WINGS] = -127; Body.Express(w, 0);
        float walker = w.PVLand[0];
        w.Genome[G.B_WINGS] = 127; Body.Express(w, 0);
        Assert.Equal(Loco.Fly, w.LocoOf(0));
        // phase 4 (2026-11-03, docs/BODYPLAN.md §13.7): the flight muscle's dead load is the module physics' own (the thorax that
        // houses it takes trunk tissue from the leg segments), no longer the live x (1 - FLY_MUSCLE_MAX w); the claim stays: a
        // wing-heavy flier is slow on the ground
        Assert.True(w.PVLand[0] < 0.9f * walker);
        // across the flight threshold the land speed does not jump: flying or not is not a term of the land speed. Re-derived
        // (phase 4): the translator grows a wing pair only above its vestige (Translate.VESTIGE, a jump of the translation,
        // not of the physics), so the steps are checked from there, at the 0.5% of one gene step it was before
        float prev = float.NaN; bool crossed = false, wasFly = false;
        for (int g = -120; g <= 127; g++)
        {
            w.Genome[G.B_WINGS] = (sbyte)g; Body.Express(w, 0);
            bool fly = w.LocoOf(0) == Loco.Fly;
            if (!float.IsNaN(prev) && fly != wasFly) Assert.InRange(w.PVLand[0] / prev, 0.98f, 1.02f);
            if (fly) crossed = true;
            prev = w.PVLand[0]; wasFly = fly;
        }
        Assert.True(crossed);
    }

    [Fact] // a juvenile's flight muscle grows with it: a newborn flier cannot take off, it fledges near adult size
    public void JuvenilesFledgeWhenTheirFlightMuscleIsGrown()
    {
        float pm = 1f;
        Assert.False(Body.CanTakeOff(1f, pm, 0.1f * pm, 0f, 1f));
        Assert.False(Body.CanTakeOff(1f, pm, 0.4f * pm, 0f, 1f));
        Assert.True(Body.CanTakeOff(1f, pm, 0.6f * pm, 0f, 1f));
        Assert.True(Body.CanTakeOff(1f, pm, pm, 0f, 1f));
        // smaller wings fledge later
        Assert.False(Body.CanTakeOff(0.6f, pm, 0.7f * pm, 0f, 1f));
        Assert.True(Body.CanTakeOff(0.6f, pm, 0.9f * pm, 0f, 1f));
        // through Resolve: a newborn of a flying species stays on the ground however fast it wants to go
        float climate = Nature.Climate; Nature.Climate = 0f;
        try
        {
            var w = new World(16, 3);
            Terrain.Build(w);
            int cell = -1;
            for (int c = 0; c < Map.FW * Map.FW; c++) if (w.Cell[c] == (byte)Realm.Land) { cell = c; break; }
            Flier(w, 127);
            w.StageOf[0] = St.Juvenile; w.Grown[0] = 0f;
            Assert.True(w.MassOf(0) < 0.4f * w.PM[0]);
            w.X[0] = (cell % Map.FW + 0.5f) * K.FCELL; w.Y[0] = (cell / Map.FW + 0.5f) * K.FCELL; w.Speed[0] = 0f;
            w.Energy[0] = w.EmaxOf(0) * 0.9f; w.Gut[0] = 0f; w.IActs[0] = 0u; w.ISpeed[0] = w.PVAir[0]; w.ITarget[0] = -1; w.IMate[0] = -1;
            Resolve.Run(w, 1, 0);
            Assert.False(w.Airborne(0));
        }
        finally { Nature.Climate = climate; }
    }

    [Fact] // a load lowers the top air speed where power binds (Pennycuick 2008): a heavier body has less margin to spare
    public void LoadLowersTopAirSpeed()
    {
        // a flier near its ceiling: margin 1.2, so power, not FLY_X_MAX, sets its top speed
        float m = 1f, wings = 1.2f / Body.FlightMargin(1f, m), vmp = 10f;
        float empty = Body.LoadedTopAirSpeed(vmp, m, 0f, Body.LoadedMargin(wings, m, m, 0f, 1f));
        float fed = Body.LoadedTopAirSpeed(vmp, m, 0.1f, Body.LoadedMargin(wings, m, m, 0.1f, 1f));
        Assert.True(fed < empty, $"empty {empty}, loaded {fed}");
        // too heavy to hold level flight: it keeps only the speed of least power of its loaded weight
        float heavy = Body.LoadedMargin(wings, m, m, 0.3f, 1f);
        Assert.True(heavy < 1f);
        Assert.Equal(vmp * MathK.Pow(1.3f, 0.5f), Body.LoadedTopAirSpeed(vmp, m, 0.3f, heavy), 3);
    }

    [Fact] // phase 4 (2026-11-03, docs/BODYPLAN.md §13.7): the basal cost is the grown body's tissues (Kleiber on its active mass,
           // integument turnover, exchange epithelia). The live COST_WING (a bird's BMR 30-50% above a mammal's; McKechnie & Wolf
           // 2004) has no tissue-physics counterpart in the module body and is retired with the labelled wing gene: a flier's
           // basal is a mammal's of its mass within 15%. KNOWN GAP: the avian premium (docs/BODYPLAN.md §13.9)
    public void FlightMuscleRaisesBasalLikeABirds()
    {
        var w = new World(16, 1);
        w.Genome[G.B_LEGS] = 60; w.Genome[G.B_MASS] = -60; w.Genome[G.B_WINGS] = -127;
        w.Genome[G.B_ENDO] = 127; Body.Express(w, 0);   // warm-blooded, as birds and mammals (2026-11-02: the level, Heat.Level, scales a cold body's cost)
        float mammal = w.PBasal[0];
        w.Genome[G.B_WINGS] = 127; Body.Express(w, 0);
        Assert.Equal(Loco.Fly, w.LocoOf(0));
        Assert.InRange(w.PBasal[0] / mammal, 0.85f, 1.15f);
    }

    [Fact] // a flier that kills ground prey from the air comes down on the kill to eat (raptors feed on the ground)
    public void AFlierLandsOnItsKill()
    {
        float climate = Nature.Climate; Nature.Climate = 0f;
        try
        {
            var w0 = new World(16, 3);
            Terrain.Build(w0);
            int cell = -1;
            for (int c = 0; c < Map.FW * Map.FW && cell < 0; c++)
            {
                int cx = c % Map.FW, cy = c / Map.FW;
                bool run = true;   // land under the predator's start and under its prey
                for (int k = 0; k <= 2; k++) if (w0.Cell[cy * Map.FW + (cx + k) % Map.FW] != (byte)Realm.Land) run = false;
                if (run) cell = c;
            }
            Assert.True(cell >= 0);
            float x = (cell % Map.FW + 0.5f) * K.FCELL, y = (cell / Map.FW + 0.5f) * K.FCELL;
            int landed = 0, kills = 0;
            for (int t = 0; t < 30 && kills < 3; t++)
            {
                var w = new World(16, 3);
                Terrain.Build(w);
                w.Step = (ulong)t;
                int a = Life.NewSlot(w), b = Life.NewSlot(w);
                // the predator: a flier with weapons and a meat diet, travelling east at its air speed
                var g0 = w.Genome.Slot(a);
                // (phase 4: 0.5 kg, where the full-winged module body flies; at the old 2 kg its margin is under 1)
                g0[G.B_WINGS] = 127; g0[G.B_LEGS] = 60; g0[G.B_MASS] = -60; g0[G.B_STRIKE] = 127; g0[G.B_INSUL] = -127;
                for (int p = 0; p < Pools.N; p++) g0[G.B_DIET + p] = -127;
                g0[G.B_DIET + (int)Pool.Meat] = 127;
                w.Spc[a] = 0; w.StageOf[a] = St.Adult; Body.Express(w, a);
                w.X[a] = x; w.Y[a] = y; w.Speed[a] = w.PVAir[a]; w.Energy[a] = w.EmaxOf(a) * 0.5f; w.Gut[a] = 0f; w.GutEnergy[a] = 0f; w.Thirst[a] = 0f;
                w.IActs[a] = 1u << (P.STRIKE - P.FIRST_ACTION); w.ISpeed[a] = w.PVAir[a]; w.IHeading[a] = 0f; w.ITarget[a] = b; w.IMate[a] = -1;
                Assert.Equal(Loco.Fly, w.LocoOf(a));
                // the prey: a small walker on the ground just ahead, nearly spent
                var g1 = w.Genome.Slot(b);
                g1[G.B_LEGS] = 60; g1[G.B_MASS] = -127; g1[G.B_WINGS] = -127; g1[G.B_STRIKE] = -127; g1[G.B_ARMOUR] = -127;
                w.Spc[b] = 0; w.StageOf[b] = St.Adult; Body.Express(w, b);
                float px = x + K.FCELL, py = y;
                w.X[b] = px; w.Y[b] = py; w.Speed[b] = 0f; w.Energy[b] = 0.02f * w.EmaxOf(b); w.Thirst[b] = 0f;
                w.IActs[b] = 0u; w.ISpeed[b] = 0f; w.ITarget[b] = -1; w.IMate[b] = -1;
                Resolve.Run(w, 1, 0);
                if (w.StageOf[b] == St.Dead)
                {
                    kills++;
                    if (MathF.Abs(World.Delta1(w.X[a] - px)) < 1e-3f && MathF.Abs(World.Delta1(w.Y[a] - py)) < 1e-3f && !w.Airborne(a)) landed++;
                }
            }
            Assert.True(kills > 0, "no kill in 30 tries");
            Assert.Equal(kills, landed);
        }
        finally { Nature.Climate = climate; }
    }

    [Fact] // wing-assisted incline running (Dial 2003; Tobalske & Dial 2007): flapping helps on slopes, the more the steeper,
           // and not at all on flat ground, where wings are only the flight muscle's dead load
    public void WingsHelpOnSlopesNotOnTheFlat()
    {
        Assert.Equal(1f, Body.WingInclineGain(0.4f, 0f));
        Assert.True(Body.WingInclineGain(0.4f, 0.7f) > Body.WingInclineGain(0.4f, 0.3f));
        // small wings: a net loss on the flat, a net gain on a steep slope (the physical way up to flight)
        Assert.True(Body.WingGroundFactor(0.4f) * Body.WingInclineGain(0.4f, 0f) < 1f);
        Assert.True(Body.WingGroundFactor(0.4f) * Body.WingInclineGain(0.4f, 0.8f) > 1f);
        Assert.InRange(Body.WingGroundGainMax, 1.1f, 1.3f);
        // in a world: on the steepest land cell a winged body keeps (1 - FLY_MUSCLE_MAX w)(1 + WAIR_GAIN w sin slope) of a
        // wingless one's speed; a wingless body is as before
        var w = new World(16, 3);
        Terrain.Build(w);
        int steep = -1; float most = 0f;
        for (int c = 0; c < Map.FW * Map.FW; c++)
            if (w.Cell[c] == (byte)Realm.Land && w.SlopeSin(c) > most) { most = w.SlopeSin(c); steep = c; }
        Assert.True(steep >= 0 && most > 0f);
        Console.WriteLine($"steepest land slope on the test map: sin {most:F3} ({MathF.Asin(most) * 180f / MathF.PI:F0} degrees)");
        w.StageOf[0] = St.Adult; w.Genome[G.B_LEGS] = 127; w.Genome[G.B_MASS] = -60; w.Genome[G.B_WINGS] = -127; Body.Express(w, 0);
        float bare = w.GroundVmax(0, steep);
        Assert.Equal(w.PVLand[0] * w.Footing(0, steep), bare, 5);   // wingless: unchanged
        w.Genome[G.B_WINGS] = (sbyte)MathF.Round(0.4f * 254f - 127f); Body.Express(w, 0);
        float wo = w.PWings[0], flat = w.PVLand[0] * w.Footing(0, steep);
        // phase 4: the flat-ground dead load is in the module physics' land speed (PVLand), the slope's gain on top of it
        Assert.Equal(Body.WingInclineGain(wo, most), w.GroundVmax(0, steep) / flat, 3);
    }

    [Fact] // a newborn flier over water it cannot stand in is in the water, not in the air: it cannot fly before it fledges
    public void ANewbornOverDeepWaterIsInTheWater()
    {
        float climate = Nature.Climate; Nature.Climate = 0f;
        try
        {
            var w = new World(16, 3);
            Terrain.Build(w);
            int a = Life.NewSlot(w);
            Flier(w, 127);
            w.Genome.Slot(0).CopyTo(w.Genome.Slot(a));
            Body.Express(w, a);
            int cell = -1;
            // water deeper than it stands (phase 4: the module flier strokes its wings in water and may swim there, so the
            // cell is any it cannot stand in, not only one it cannot land on)
            for (int c = 0; c < Map.FW * Map.FW; c++) if (cell < 0 && (w.Cell[c] & (byte)Realm.Water) != 0 && Respiration.ColumnM(w, c) > 2f * w.PStand[a]) cell = c;
            Assert.True(cell >= 0, "no deep water on the test map");
            w.Spc[a] = 0; w.StageOf[a] = St.Juvenile; w.Grown[a] = 0f; w.Flags[a] = 0;   // past its first step as a newborn
            w.X[a] = (cell % Map.FW + 0.5f) * K.FCELL; w.Y[a] = (cell / Map.FW + 0.5f) * K.FCELL;
            w.Speed[a] = 0f; w.Energy[a] = 0.9f * w.EmaxOf(a); w.Thirst[a] = 0f; w.Gut[a] = 0f;
            Assert.Equal(Loco.Fly, w.LocoOf(a));
            Assert.False(w.Airborne(a));   // being over water it cannot stand in is not flying (it was, until 2026-11-01)
            w.IActs[a] = 0u; w.ISpeed[a] = w.PVAir[a]; w.IHeading[a] = 0f; w.ITarget[a] = -1; w.IMate[a] = -1;
            Resolve.Run(w, 1, 0);
            if (w.StageOf[a] == St.Dead) return;   // drowned: what the physics says for a body that cannot swim
            Assert.False(w.Airborne(a));
            Assert.NotEqual(0, w.Flags[a] & Flag.Landed);
            Assert.True(w.Speed[a] <= w.GroundVmax(a, cell) * 1.001f + 1e-5f, "it paddles, it does not fly");
        }
        finally { Nature.Climate = climate; }
    }

    static void WithClimate(Action a)
    {
        float climate = Nature.Climate; Nature.Climate = 1f;
        try { a(); } finally { Nature.Climate = climate; }
    }
}
