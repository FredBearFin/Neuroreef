// Explore mode vehicles (PlayerVehicles.cs): the jeep's automatic gun is rate-limited by the server; bombs and torpedoes
// explode over an area, checked against where the person's vehicle has been, and damage every animal within the blast
// radius, falling off with distance.

using System;
using Xunit;

namespace Neuroreef.Tests;

public class VehicleTests
{
    static World Flat(Realm realm, uint seed = 17)
    {
        var w = new World(64, seed);
        Terrain.Build(w);
        Array.Fill(w.Cell, (byte)realm);
        Array.Fill(w.Elev, 0.6f);   // flat: a plane can take off anywhere
        if (realm != Realm.Land) Array.Fill(w.Depth, 0.05f);   // hundreds of metres of water
        w.Sp[0].Used = true; w.Sp[0].Alive = true;
        return w;
    }

    static int Animal(World w, float x, float y)
    {
        int i = Life.Found(w, 0, ReadOnlySpan<sbyte>.Empty, true, 1);
        Assert.True(i >= 0);
        w.Flags[i] = 0; w.StageOf[i] = St.Adult; w.PArmour[i] = 0f; w.Energy[i] = w.EmaxOf(i);
        w.X[i] = x; w.Y[i] = y; w.Heading[i] = 0f; w.Speed[i] = 0f;
        return i;
    }

    /// <summary>The person in a vehicle of this kind spawned where it stands, at a height above the ground (m).</summary>
    static Player InVehicle(World w, string kind, float x, float y, float altM = 0f)
    {
        var p = w.Player = new Player();
        p.Place(x, y, 0f, false);
        Assert.Null(p.Spawn(w, kind, x, y));
        p.Board(kind, altM);
        Assert.Equal(kind, p.Vehicle);
        p.Place(x, y, 0f, false);
        return p;
    }

    static float U(float metres) => metres / Player.M_PER_UNIT;

    [Fact]
    public void BombKillsWithinTheRadiusFallingOffAndSparesOutside()
    {
        var w = Flat(Realm.Land);
        const float X = 20f, Y = 20f;
        var bomb = Weapons.Mode("bomb")!;
        float R = bomb.RadiusM;
        int centre = Animal(w, X, Y), half = Animal(w, X + U(0.4f * R), Y), edge = Animal(w, X, Y + U(0.93f * R));
        int outside = Animal(w, X + U(1.3f * R), Y), far = Animal(w, X + 5f, Y + 5f);
        w.Energy[edge] = 500f;   // a big reserve, so the little the edge of the blast takes shows
        float eOut = w.Energy[outside], eFar = w.Energy[far], eEdge = w.Energy[edge];
        uint sCentre = w.Serial[centre], sHalf = w.Serial[half];
        int cell = World.FIdx(X, Y), sp = w.Spc[centre];
        float carrion0 = w.Pool[(int)Pool.Carrion][cell];
        // dry grass to burn where it lands
        w.Pool[(int)Pool.Grass][cell] = 1f; w.Moisture[cell] = 0f; w.Fire[cell] = 0f;

        // the plane flies 300 m up past (X - 0.5 km) and drops a bomb that lands at (X, Y) 8 s later
        var p = InVehicle(w, "plane", X - U(500f), Y, 300f);
        Assert.True(p.Airborne);
        p.Clock = 8.0;
        p.Place(X + U(300f), Y, 0f, false);
        var r = p.Blast(w, "bomb", X, Y, 0f, X - U(500f), Y, 8f, out var rep);
        Assert.Equal(Player.BlastResult.Exploded, r);
        Assert.Equal(2, rep.Killed);
        Assert.Equal(1, rep.Hurt);
        Assert.Equal(St.Dead, w.StageOf[centre]); Assert.Equal(St.Dead, w.StageOf[half]);
        Assert.Contains(rep.Victims, v => v.Serial == sCentre && v.Killed);
        Assert.Contains(rep.Victims, v => v.Serial == sHalf && v.Killed);
        Assert.Equal(2, w.T.Deaths[sp * Cause.N + Cause.Hunted]);
        Assert.True(w.Pool[(int)Pool.Carrion][cell] > carrion0, "the bodies stay as carrion");
        // near the edge: hurt a little (the damage falls off to 0 at the radius), in pain, the learner told
        Assert.Equal(St.Adult, w.StageOf[edge]);
        Assert.True(w.Energy[edge] < eEdge && w.Energy[edge] > 0.5f * eEdge, $"edge {w.Energy[edge]} of {eEdge}");
        float lossCentre = rep.Victims.Find(v => v.Serial == sCentre).Loss, lossEdge = eEdge - w.Energy[edge];
        Assert.True(lossEdge < 0.15f * lossCentre, $"edge lost {lossEdge}, the centre {lossCentre}");
        Assert.Equal(1f, w.Pain[edge]);
        Assert.True(w.PendingR[edge] < 0f);
        // outside the radius: untouched
        Assert.Equal(eOut, w.Energy[outside]); Assert.Equal(0f, w.Pain[outside]); Assert.Equal(St.Adult, w.StageOf[outside]);
        Assert.Equal(eFar, w.Energy[far]);
        // fear where it went off, a fire in the dry grass, a round used
        Assert.True(w.Scent[cell] >= 1f);
        Assert.Equal(1, rep.Fires); Assert.True(w.Fire[cell] > 0f);
        Assert.Equal(bomb.Rounds - 1, p.Rounds["bomb"]);
        Assert.Equal(2, p.BlastKills); Assert.Equal(2, p.Kills);
    }

    [Fact]
    public void TorpedoAtDepthMissesASurfaceTargetWhenDepthsDoNotOverlap()
    {
        var w = Flat(Realm.Deep);
        const float X = 30f, Y = 30f;
        int surface = Animal(w, X, Y), deep = Animal(w, X + U(2f), Y);
        float eSurface = w.Energy[surface];
        var p = InVehicle(w, "boat", X - U(300f), Y);
        // the swimmers' depths as the AGENTS records will carry them: one at the surface, one 24 m down
        p.DepthOf = (ww, i) => i == deep ? 24f : 0f;
        p.Clock = 12.0; p.Place(X - U(250f), Y, 0f, false);
        var r = p.Blast(w, "torpedo", X, Y, 25f, X - U(300f), Y, 12f, out var rep);
        Assert.Equal(Player.BlastResult.Exploded, r);
        Assert.Equal(St.Dead, w.StageOf[deep]);
        Assert.Equal(St.Adult, w.StageOf[surface]);
        Assert.Equal(eSurface, w.Energy[surface]);
        Assert.Single(rep.Victims);

        // without depths on the wire every swimmer is at the surface: a torpedo set to run 2 m down catches it
        p.DepthOf = null;
        p.Clock = 30.0; p.Place(X - U(250f), Y, 0f, false);
        r = p.Blast(w, "torpedo", X, Y, 2f, X - U(300f), Y, 15f, out rep);
        Assert.Equal(Player.BlastResult.Exploded, r);
        Assert.Equal(St.Dead, w.StageOf[surface]);
    }

    [Fact]
    public void ImplausibleExplosionsAreRejectedAndChangeNothing()
    {
        var w = Flat(Realm.Land);
        const float X = 20f, Y = 20f;
        int a = Animal(w, X, Y);
        float e0 = w.Energy[a];
        // on foot: no bombs
        var foot = w.Player = new Player(); foot.Place(X, Y, 0f, false);
        Assert.Equal(Player.BlastResult.WrongVehicle, foot.Blast(w, "bomb", X, Y, 0f, X, Y, 5f, out _));

        var p = InVehicle(w, "plane", X, Y, 400f);
        int rounds = p.Rounds["bomb"];
        // flown a long way since: the launch point (where it was 60 s ago) is not on its recent path
        p.Clock = 60.0; p.Place(X + 10f, Y, 0f, false);
        Assert.Equal(Player.BlastResult.LaunchFar, p.Blast(w, "bomb", X, Y, 0f, X, Y, 5f, out var rep));
        Assert.True(rep.LaunchErrM > rep.LaunchLimitM);
        // a flight longer than a bomb can fall
        Assert.Equal(Player.BlastResult.BadTime, p.Blast(w, "bomb", X, Y, 0f, X + 10f, Y, 100f, out _));
        // beyond its range of the launch point (58 units: over 3 km)
        Assert.Equal(Player.BlastResult.OutOfRange, p.Blast(w, "bomb", X + 10f + 58f, Y, 0f, X + 10f, Y, 30f, out _));
        // within range but faster than a bomb travels: 545 m in 1 s
        Assert.Equal(Player.BlastResult.TooFast, p.Blast(w, "bomb", X, Y, 0f, X + 10f, Y, 1f, out _));
        // a torpedo from a plane, an unknown weapon, a rifle shot reported as an explosion
        Assert.Equal(Player.BlastResult.WrongVehicle, p.Blast(w, "torpedo", X + 10f, Y, 2f, X + 10f, Y, 5f, out _));
        Assert.Equal(Player.BlastResult.UnknownWeapon, p.Blast(w, "nuke", X + 10f, Y, 0f, X + 10f, Y, 5f, out _));
        Assert.Equal(Player.BlastResult.UnknownWeapon, p.Blast(w, "rifle-shot", X + 10f, Y, 0f, X + 10f, Y, 5f, out _));
        Assert.Equal(e0, w.Energy[a]);
        Assert.Equal(St.Adult, w.StageOf[a]);
        Assert.Equal(rounds, p.Rounds["bomb"]);

        // a torpedo cannot explode in the middle of the land
        var boatWorld = Flat(Realm.Land);
        int c = World.FIdx(5f, 5f); boatWorld.Cell[c] = (byte)Realm.Deep; boatWorld.Depth[c] = 0.05f;
        var b = boatWorld.Player = new Player(); b.Place(5f, 5f, 0f, false);
        Assert.Equal("not on water", Vehicles.SpawnCheck(boatWorld, "boat", 30f, 30f));
        Assert.NotNull(b.Spawn(boatWorld, "jeep", 5f, 5f));   // a jeep is not placed on water
        Assert.Null(b.Spawn(boatWorld, "boat", 5f, 5f));
        b.Board("boat", 0f); b.Place(5f, 5f, 0f, false);
        Assert.Equal(Player.BlastResult.NotInWater, b.Blast(boatWorld, "torpedo", 5f + U(600f), 5f, 2f, 5f, 5f, 25f, out _));
        Assert.Equal(Player.BlastResult.BadDepth, b.Blast(boatWorld, "torpedo", 5f + U(100f), 5f, 80f, 5f, 5f, 5f, out _));
        // and when the rounds are spent there are no more
        b.Rounds["torpedo"] = 0;
        Assert.Equal(Player.BlastResult.NoAmmo, b.Blast(boatWorld, "torpedo", 5f + U(100f), 5f, 2f, 5f, 5f, 5f, out _));
        // an unspawned vehicle cannot be boarded
        var q = new Player(); q.Place(1f, 1f, 0f, false); q.Board("plane", 100f);
        Assert.Null(q.Vehicle);
    }

    [Fact]
    public void JeepAutofireIsRateLimitedByTheServer()
    {
        var w = Flat(Realm.Land);
        int a = Animal(w, 20f, 20f);
        w.Energy[a] = 1e6f;   // it takes every round
        var gun = Weapons.Mode("jeep-gun")!;
        Assert.Equal("auto", gun.Kind);
        // on foot the jeep's gun is not at hand
        var foot = w.Player = new Player(); foot.Place(20.5f, 20f, 0f, false);
        Assert.Equal(Player.HitResult.WrongVehicle, foot.Hit(w, "jeep-gun", w.Serial[a], 1f, 0f, out _, out _));

        var p = InVehicle(w, "jeep", 20.5f, 20f);
        // and from the jeep, not the rifle in hand
        Assert.Equal(Player.HitResult.WrongVehicle, p.Hit(w, "rifle-shot", w.Serial[a], 1f, 0f, out _, out _));
        p.Clock = 100.0;
        int ok = 0, limited = 0;
        for (int k = 0; k < 20; k++)   // 20 hits reported at once: a short burst passes, the rest are refused
        {
            var r = p.Hit(w, "jeep-gun", w.Serial[a], 1f, 0f, out _, out _);
            if (r == Player.HitResult.Wounded) ok++; else if (r == Player.HitResult.RateLimited) limited++;
        }
        Assert.Equal(3, ok); Assert.Equal(17, limited);
        // a cooldown later, one more
        p.Clock += gun.Cooldown * 1.01;
        Assert.Equal(Player.HitResult.Wounded, p.Hit(w, "jeep-gun", w.Serial[a], 1f, 0f, out _, out _));
        Assert.Equal(Player.HitResult.RateLimited, p.Hit(w, "jeep-gun", w.Serial[a], 1f, 0f, out _, out _));
        // over 10 s of holding the trigger, no more than its rate (plus the burst) land
        ok = 0;
        for (int k = 0; k < 1000; k++)
        {
            p.Clock += 0.01;
            if (p.Hit(w, "jeep-gun", w.Serial[a], 1f, 0f, out _, out _) == Player.HitResult.Wounded) ok++;
        }
        Assert.InRange(ok, (int)(10f / gun.Cooldown) - 2, (int)(10f / gun.Cooldown) + 3);
    }
}
