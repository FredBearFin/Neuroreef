// Explore mode: the person in the world (Player.cs). Hits are checked and applied by the sim; the animals sense the person
// as another organism and can flee it; predators can hurt it.

using System;
using Xunit;

namespace Neuroreef.Tests;

public class ExploreTests
{
    static (World w, int i) One(uint seed = 17)
    {
        var w = new World(64, seed);
        Terrain.Build(w);
        Array.Fill(w.Cell, (byte)Realm.Land);
        w.Sp[0].Used = true; w.Sp[0].Alive = true;
        int i = Life.Found(w, 0, ReadOnlySpan<sbyte>.Empty, true, 1);
        Assert.True(i >= 0);
        w.Flags[i] = 0; w.StageOf[i] = St.Adult; w.Energy[i] = w.EmaxOf(i);
        w.X[i] = 20f; w.Y[i] = 20f; w.Heading[i] = 0f;
        return (w, i);
    }

    static Player PlaceAt(World w, float x, float y) { var p = w.Player = new Player(); p.Place(x, y, 0f, false); return p; }

    [Fact]
    public void LethalHitKillsWithCauseHuntedAndLeavesACarcass()
    {
        var (w, i) = One();
        var p = PlaceAt(w, 21f, 20f);
        uint serial = w.Serial[i]; int sp = w.Spc[i];
        int cell = World.FIdx(w.X[i], w.Y[i]);
        float carrion0 = w.Pool[(int)Pool.Carrion][cell];
        long fights0 = w.T.FightSeq;
        w.Energy[i] = 0.05f;   // one rifle shot is more than its reserve
        var r = p.Hit(w, "rifle-shot", serial, 1f, 0f, out _, out float loss);
        Assert.Equal(Player.HitResult.Killed, r);
        Assert.True(loss > 0.05f);
        Assert.Equal(St.Dead, w.StageOf[i]);
        Assert.Equal(1, w.T.Deaths[sp * Cause.N + Cause.Hunted]);
        Assert.Equal("hunted", Cause.Names[Cause.Hunted]);
        Assert.True(w.Pool[(int)Pool.Carrion][cell] > carrion0, "the body stays as carrion for scavengers");
        Assert.Equal(1, p.Kills);
        // the kill is in the fight log, the person as the attacker (clade -1)
        Assert.Equal(fights0 + 1, w.T.FightSeq);
        int o = (int)(fights0 % Telemetry.FIGHT_N) * Telemetry.FIGHT_F;
        Assert.Equal(-1f, w.T.Fights[o + 2]);
        Assert.Equal(Telemetry.F_KILL, (int)w.T.Fights[o + 4]);
        // and the serial is gone: a second hit finds no animal
        Assert.Equal(Player.HitResult.NoAnimal, p.Hit(w, "rifle-shot", serial, 1f, 0f, out _, out _));
    }

    [Fact]
    public void NonLethalHitWoundsHurtsAndTellsTheLearner()
    {
        var (w, i) = One();
        var p = PlaceAt(w, 21f, 20f);
        float e0 = w.Energy[i];
        Assert.Equal(Player.HitResult.Wounded, p.Hit(w, "bow-shot", w.Serial[i], 0.1f, 0f, out _, out float loss));
        Assert.Equal(e0 - loss, w.Energy[i], 5);
        Assert.Equal(1f, w.Pain[i]);
        Assert.True(w.PendingR[i] < 0f);
        // armour stops part of it
        var (w2, j) = One(); w2.PArmour[j] = 2f;
        var p2 = PlaceAt(w2, 21f, 20f);
        p2.Hit(w2, "bow-shot", w2.Serial[j], 0.1f, 0f, out _, out float armoured);
        Assert.True(armoured < loss * 0.5f);
    }

    [Fact]
    public void OutOfRangeAndUnknownHitsAreRejected()
    {
        var (w, i) = One();
        var p = PlaceAt(w, 40f, 20f);   // 20 units (over a km) away
        float e0 = w.Energy[i];
        Assert.Equal(Player.HitResult.OutOfRange, p.Hit(w, "spear-thrust", w.Serial[i], 1f, 0f, out _, out _));
        Assert.Equal(Player.HitResult.OutOfRange, p.Hit(w, "bow-shot", w.Serial[i], 1f, 0f, out _, out _));
        Assert.Equal(e0, w.Energy[i]);
        // a spear thrust reaches 3 m past the body the console aims at: from 30 m off it, no; from 1 m off it, yes
        w.Speed[i] = 0f;
        float surface = Player.BodyR(w, i) - 0.1f, m = 1f / Player.M_PER_UNIT;
        p.Place(w.X[i] + surface + 30f * m + 0.1f, w.Y[i], 0f, false);
        Assert.Equal(Player.HitResult.OutOfRange, p.Hit(w, "spear-thrust", w.Serial[i], 1f, 0f, out _, out _, out float dm, out float lm));
        Assert.True(dm > lm);
        Assert.Equal(e0, w.Energy[i]);
        p.Place(40f, 20f, 0f, false);
        Assert.Equal(Player.HitResult.UnknownWeapon, p.Hit(w, "laser", w.Serial[i], 1f, 0f, out _, out _));
        Assert.Equal(Player.HitResult.NoAnimal, p.Hit(w, "rifle-shot", 999999u, 1f, 0f, out _, out _));
        p.Present = false;
        Assert.Equal(Player.HitResult.NotPresent, p.Hit(w, "rifle-shot", w.Serial[i], 1f, 0f, out _, out _));
        Assert.Equal(e0, w.Energy[i]);
        Assert.Equal(St.Adult, w.StageOf[i]);
        // and the same thrust from 1 m off the body lands
        p.Place(w.X[i] + surface + 1f * m, w.Y[i], 0f, false);
        var near = p.Hit(w, "spear-thrust", w.Serial[i], 1f, 0f, out _, out float loss);
        Assert.True(near is Player.HitResult.Wounded or Player.HitResult.Killed, near.ToString());
        Assert.True(loss > 0f);
    }

    /// <summary>A brain that only flees the nearest other organism (FLEE_1 ~ 1, every other primitive ~ 0).</summary>
    static void OnlyFlee(World w, int i)
    {
        var W = w.W2f.Slot(i); W.Clear();
        var gn = w.Genome.Slot(i);
        for (int p = 0; p < P.N; p++)
        {
            for (int k = 0; k < G.NC; k++) gn[G.REFLEX + p * G.NC + k] = 0;
            W[p * G.NIN + G.NH + F.BIAS] = p == P.FLEE_1 ? 12f : -12f;
        }
    }

    [Fact]
    public void AnimalsSenseTheNearbyPersonAndFleeIt()
    {
        var (w, i) = One();
        OnlyFlee(w, i);
        // nobody there: no other organism sensed
        Simulation.BuildFor(w);
        Decide.Run(w, i, new Decide.ProbeBuf());
        Assert.Equal(0f, w.Feat[i * F.N + F.O1_CLOSE]);

        var p = PlaceAt(w, 20f + 0.8f * w.PSight[i], 20.2f);   // in front, within sight (2026-11-01: sight is metres now, ~60 m x M^0.2; was (23, 21), 3.2 units = 175 m)
        float Dist() { float dx = World.Delta1(p.X - w.X[i]), dy = World.Delta1(p.Y - w.Y[i]); return MathF.Sqrt(dx * dx + dy * dy); }
        float d0 = Dist();
        Simulation.BuildFor(w);
        Decide.Run(w, i, new Decide.ProbeBuf());
        var f = new ReadOnlySpan<float>(w.Feat, i * F.N, F.N);
        Assert.True(f[F.O1_CLOSE] > 0f, "the person is sensed as the nearest other organism");
        Assert.True(f[F.O1_SIZE] > 0f, "and as bigger than it");
        Assert.True(f[F.O1_ARMED] > 0f, "and armed");
        Assert.Equal(Player.MEAT, f[F.O1_MEAT]);
        Assert.True(f[F.O2_CLOSE] > 0f, "the biggest in view too");
        // it turns away and runs: a few steps later it is further off
        for (int t = 0; t < 12; t++)
        {
            w.Step++;
            w.Energy[i] = w.EmaxOf(i); w.Fatigue[i] = 0f;
            p.BeforeStep(w);
            Simulation.BuildFor(w);
            Decide.Run(w, i, new Decide.ProbeBuf());
            Resolve.Run(w, 1, 0);
        }
        Assert.True(Dist() > d0 + 1f, $"fled from {d0:F2} to {Dist():F2} units");
    }

    [Fact]
    public void APredatorStrikingThePersonHurtsItAndItComesBackAfterDeath()
    {
        var (w, i) = One();
        w.PStrike[i] = 2f; w.PM[i] = 40f; w.Energy[i] = w.EmaxOf(i);
        var p = PlaceAt(w, 20.5f, 20f);
        int hits = 0;
        for (int t = 0; t < 400 && p.Deaths == 0; t++)
        {
            w.Step++;
            w.IActs[i] = 1u << (P.STRIKE - P.FIRST_ACTION); w.ITarget[i] = Player.Slot; w.ISpeed[i] = 0; w.IHeading[i] = 0;
            w.Energy[i] = w.EmaxOf(i); w.Thirst[i] = 0f;
            Resolve.Run(w, 1, 0);
            hits = p.Hits;
        }
        Assert.True(hits > 0);
        Assert.Equal(1, p.Deaths);
        Assert.Equal(w.Serial[i], p.LastKiller);
        Assert.Equal(Player.MAX_HP, p.Hp);
    }
}
