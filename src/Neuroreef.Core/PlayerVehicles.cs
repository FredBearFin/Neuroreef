// EXPLORE VEHICLES: the person may drive a jeep, fly a plane or steer a boat (the console's Explore mode, js/explore/vehicles).
// The console drives them (their physics is the console's, as the person's walking is); the server knows which vehicle the
// person is in, keeps its bombs and torpedoes, limits the rate of its automatic gun, and checks and applies its weapons:
// the jeep's gun as one more hitscan weapon (Player.Hit), bombs and torpedoes as explosions over an area (Player.Blast).
// Like the rest of Player, nothing here runs while World.Player is null, so a world without the person is bit for bit as
// before (EngineeringTests).

using System;
using System.Collections.Generic;

namespace Neuroreef;

/// <summary>A vehicle the person can spawn and drive. Speeds in m/s, slopes in degrees, depths in m. Realm: where it may be
/// placed (land, water). The console drives it with these numbers (Wire.Meta: explore.vehicles); the server uses the top
/// speed to judge where a weapon could have been launched from.</summary>
public sealed record VehicleSpec(string Id, string Name, string Realm, float MaxSpeedMps, float MaxSlopeDeg, float WadeDepthM,
    float MinDepthM, float StallMps, float CeilingM, WeaponMode Weapon);

public static class Vehicles
{
    // jeep: a four-wheel drive with a pintle-mounted automatic rifle. Plane: a light bomber (stalls under ~38 m/s, takes
    // off from flat ground). Boat: a fast patrol boat with torpedoes that run under the surface at a set depth.
    public static readonly VehicleSpec[] All =
    {
        new("jeep", "jeep", "land", 28f, 35f, 0.9f, 0f, 0f, 0f,
            new("jeep-gun", "mounted rifle", "auto", 350f, 30f, 0f, 0.12f, true, Vehicle: "jeep", Rounds: 400, HeatPerShot: 0.07f)),
        new("plane", "plane", "land", 110f, 6f, 0f, 0f, 38f, 3000f,
            new("bomb", "bomb", "bomb", 3000f, 600f, 125f, 0.6f, true, Vehicle: "plane", RadiusM: 35f, Rounds: 12, MaxTimeS: 40f, Area: true, Ignites: true)),
        new("boat", "boat", "water", 16f, 0f, 0f, 0.4f, 0f, 0f,
            new("torpedo", "torpedo", "torpedo", 1500f, 500f, 30f, 1.5f, true, Vehicle: "boat", RadiusM: 14f, Rounds: 8, MaxTimeS: 70f,
                DepthMinM: 0.5f, DepthMaxM: 30f, Area: true, Underwater: true)),
    };

    public static VehicleSpec? Spec(string? id) { if (id == null) return null; foreach (var v in All) if (v.Id == id) return v; return null; }

    /// <summary>Why a vehicle cannot be placed at (x, y) (world units), or null when it can: a jeep on land, a boat on water
    /// at least its depth deep, a plane on land flat enough to take off from (the field cell and its neighbours within
    /// the plane's slope; the console checks its runway on the drawn ground).</summary>
    public static string? SpawnCheck(World w, string kind, float x, float y)
    {
        var v = Spec(kind);
        if (v == null) return "unknown vehicle";
        int c = World.FIdx(x, y);
        bool land = w.Cell[c] == (byte)Neuroreef.Realm.Land;
        if (v.Realm == "water")
        {
            if (land) return "not on water";
            if (Player.WaterDepthM(w, c) < v.MinDepthM) return "too shallow";
            return null;
        }
        if (!land) return "not on land";
        if (v.Id == "plane")
        {
            int FW = Map.FW, cx = c % FW, cy = c / FW;
            float g = Player.GroundM(w, x, y), tan = MathK.Sin(v.MaxSlopeDeg * MathF.PI / 180f) / MathK.Cos(v.MaxSlopeDeg * MathF.PI / 180f);
            for (int k = 0; k < 4; k++)
            {
                int nx = World.WrapI(cx + (k == 0 ? 1 : k == 1 ? -1 : 0), FW), ny = World.WrapI(cy + (k == 2 ? 1 : k == 3 ? -1 : 0), FW);
                int n = ny * FW + nx;
                float gn = (MathF.Max(w.Elev[n], w.SeaLevel) - w.SeaLevel) / MathF.Max(1e-3f, 1f - w.SeaLevel) * Map.ReliefM;
                if (MathF.Abs(gn - g) > tan * K.GEO_CELL_M) return "not flat enough to take off";
            }
        }
        return null;
    }
}

public sealed partial class Player
{
    /// <summary>The vehicle the person is in (a VehicleSpec id), or null on foot; its height above the ground (m).</summary>
    public string? Vehicle;
    public float AltM;
    /// <summary>A plane in the air: the animals neither see nor strike it (Decide, Resolve read Present and this).</summary>
    public bool Airborne => Vehicle == "plane" && AltM > 25f;
    /// <summary>Seconds on the host's clock (set before Place, Hit and Blast; tests set it). It times the rate of automatic
    /// fire and the person's recent path; it is wall time, not sim time, and never reaches the simulation's state.</summary>
    public double Clock;
    /// <summary>Rounds left per area weapon (bombs, torpedoes), filled when its vehicle is spawned.</summary>
    public readonly Dictionary<string, int> Rounds = new();
    public int BlastKills, Blasts, FiresStarted;

    /// <summary>The share of a blast at depthM (m below the surface; 0 a bomb) that reaches animal i in the water column,
    /// as two animals meet there (Respiration.Overlap, with the blast at its depth for the whole step): i is at its depth
    /// World.Z for its share of the step ZShare and at the surface the rest, and is reached where it is within STRIKE_DZ
    /// of the blast. 1 for an animal out of water (ZShare 0) and a blast at the surface; a torpedo running deep misses an
    /// animal at the surface, and a surface shot misses a fish that stays deep.</summary>
    public static float DepthOverlap(World w, int i, float depthM)
    {
        float z = w.Z[i], s = w.ZShare[i], p = 0f;
        if (MathF.Abs(z - depthM) <= K.STRIKE_DZ) p += s;
        if (depthM <= K.STRIKE_DZ) p += 1f - s;
        return p;
    }

    // the person's recent path: where it was placed and when (a ring), so a launch point can be checked against it
    const int PATH_N = 512;
    readonly float[] _px = new float[PATH_N], _py = new float[PATH_N];
    readonly double[] _pt = new double[PATH_N];
    int _pn, _ph;
    // automatic fire: a bucket of BURST shots, refilled at 1 / cooldown per second
    const float BURST = 3f;
    readonly Dictionary<string, (float tokens, double t)> _bucket = new();

    void Record()
    {
        _px[_ph] = X; _py[_ph] = Y; _pt[_ph] = Clock;
        _ph = (_ph + 1) % PATH_N; if (_pn < PATH_N) _pn++;
    }

    /// <summary>The person gets in (or out: null) of a vehicle; only a spawned kind (Spawn) can be boarded.</summary>
    public void Board(string? vehicle, float altM)
    {
        Vehicle = vehicle != null && _spawned.Contains(vehicle) ? vehicle : null;
        AltM = Vehicle == null || !float.IsFinite(altM) ? 0f : Math.Clamp(altM, 0f, 10000f);
    }
    readonly HashSet<string> _spawned = new();

    /// <summary>A vehicle of this kind is placed in the world: its weapon is loaded. Returns why not, or null.</summary>
    public string? Spawn(World w, string kind, float x, float y)
    {
        var why = Vehicles.SpawnCheck(w, kind, x, y);
        if (why != null) return why;
        var v = Vehicles.Spec(kind)!;
        _spawned.Add(kind);
        Rounds[v.Weapon.Id] = v.Weapon.Rounds;
        _bucket.Remove(v.Weapon.Id);
        return null;
    }

    /// <summary>One shot of an automatic weapon at Clock: whether the bucket has one (and takes it).</summary>
    bool Fire(WeaponMode m)
    {
        float rate = 1f / MathF.Max(1e-3f, m.Cooldown);
        if (!_bucket.TryGetValue(m.Id, out var b)) b = (BURST, Clock);
        float tokens = MathF.Min(BURST, b.tokens + (float)Math.Max(0.0, Clock - b.t) * rate);
        if (tokens < 1f) { _bucket[m.Id] = (tokens, Clock); return false; }
        _bucket[m.Id] = (tokens - 1f, Clock);
        return true;
    }

    /// <summary>Depth of water over cell c (m); 0 on land.</summary>
    public static float WaterDepthM(World w, int c)
        => w.Cell[c] == (byte)Neuroreef.Realm.Land ? 0f : MathF.Max(0f, w.Depth[c]) / MathF.Max(1e-3f, 1f - w.SeaLevel) * Map.ReliefM;

    public enum BlastResult { Exploded, NotPresent, UnknownWeapon, WrongVehicle, NoAmmo, BadTime, LaunchFar, OutOfRange, TooFast, NotInWater, BadDepth }

    public sealed class BlastReport
    {
        public readonly List<(uint Serial, int Clade, float Loss, bool Killed)> Victims = new();
        public int Hurt, Killed, Fires, RoundsLeft;
        /// <summary>The launch point's distance from the person's recent path, and its allowance (m); the flight (m).</summary>
        public float LaunchErrM, LaunchLimitM, FlightM;
    }

    /// <summary>Slack for a launch point off the recorded path (m): the console sends its place ~5 times a second and
    /// reports the launch point a little later; plus what the vehicle covers in half a second at its top speed.</summary>
    public const float LAUNCH_SLACK_M = 60f;
    /// <summary>Slack on the flight's length (m): where the console saw the ground and what it rounds.</summary>
    public const float FLIGHT_SLACK_M = 25f;

    /// <summary>
    /// An explosion the console reports: an area weapon (a bomb, a torpedo) of the person's vehicle went off at (x, y)
    /// (world units; depthM below the surface for a torpedo) after flightS seconds from where it was launched, (lx, ly).
    /// Checked here: the person is in that weapon's vehicle and has a round left; the flight lasted no longer than the
    /// weapon can fly; the launch point lies on the person's recent path (within LAUNCH_SLACK_M plus half a second of
    /// the vehicle's top speed of where it was placed between the launch, flightS ago, and now); the explosion is within
    /// the weapon's range of the launch point, and no further than its top speed could carry it in that time; a torpedo
    /// explodes in or at the edge of water, at a depth it can run at. Then every living animal within the blast radius
    /// (m, across) takes the weapon's damage scaled by 1 - d / R, falling off to 0 at the edge, nothing beyond it, and by
    /// how much of the step its depth overlaps the blast's (DepthOverlap: as strikes in water, within STRIKE_DZ). Each one hurt feels pain and the learner is
    /// told, as for a hit (Damage); one killed dies of Cause.Hunted, its body left as carrion. The blast leaves a burst of
    /// scent where it went off, which the animals around read as fear (Decide: FEAR), and a bomb sets the plants of its
    /// field cell alight if they would burn (Fields.IgniteAt). A rejected explosion changes nothing.
    /// </summary>
    public BlastResult Blast(World w, string mode, float x, float y, float depthM, float lx, float ly, float flightS, out BlastReport rep)
    {
        rep = new BlastReport();
        if (!Present) return BlastResult.NotPresent;
        var m = Weapons.Mode(mode);
        if (m == null || !m.Area) return BlastResult.UnknownWeapon;
        if (m.Vehicle != Vehicle) return BlastResult.WrongVehicle;
        if (!Rounds.TryGetValue(m.Id, out int left) || left <= 0) return BlastResult.NoAmmo;
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(lx) || !float.IsFinite(ly) || !float.IsFinite(depthM)) return BlastResult.OutOfRange;
        if (!(flightS > 0f && flightS <= m.MaxTimeS)) return BlastResult.BadTime;
        var v = Vehicles.Spec(Vehicle)!;
        // the launch point against the path: the places recorded since the launch (and a little before), and now
        rep.LaunchLimitM = LAUNCH_SLACK_M + 0.5f * v.MaxSpeedMps;
        float best = DistM(lx, ly, X, Y);
        double t0 = Clock - flightS - 2.0;
        for (int k = 0; k < _pn; k++)
        {
            int q = (_ph - 1 - k + PATH_N) % PATH_N;
            if (_pt[q] < t0) break;
            best = MathF.Min(best, DistM(lx, ly, _px[q], _py[q]));
        }
        rep.LaunchErrM = best;
        if (!(best <= rep.LaunchLimitM)) return BlastResult.LaunchFar;
        float d = rep.FlightM = DistM(lx, ly, x, y);
        if (!(d <= m.RangeM + FLIGHT_SLACK_M)) return BlastResult.OutOfRange;
        if (!(d <= m.SpeedMps * flightS + FLIGHT_SLACK_M)) return BlastResult.TooFast;
        int cell = World.FIdx(x, y);
        if (m.Underwater)
        {
            if (!NearWater(w, cell)) return BlastResult.NotInWater;
            if (!(depthM >= m.DepthMinM - 0.01f && depthM <= m.DepthMaxM + 0.01f)) return BlastResult.BadDepth;
        }
        else depthM = 0f;
        Rounds[m.Id] = rep.RoundsLeft = left - 1;
        Blasts++;

        float R = m.RadiusM, Ru = R / M_PER_UNIT;
        for (int i = 0; i < w.SlotsUsed; i++)
        {
            byte st = w.StageOf[i];
            if (st == St.Dead || st == St.Embryo) continue;
            float dx = World.Delta1(w.X[i] - x), dy = World.Delta1(w.Y[i] - y);
            if (MathF.Abs(dx) > Ru + 1f || MathF.Abs(dy) > Ru + 1f) continue;
            float dh = MathF.Sqrt(dx * dx + dy * dy) * M_PER_UNIT;
            // its body's own size counts: the blast reaches its near side (half a body of about 0.5 M^(1/3) m: 1.7 m at M 40)
            float dist = MathF.Max(0f, dh - 0.25f * MathK.Cbrt(w.PM[i]));
            if (dist >= R) continue;
            // in the water column only where the depths overlap (a bomb goes off at the surface)
            float reach = DepthOverlap(w, i, depthM);
            if (reach <= 0f) continue;
            float f = (1f - dist / R) * reach;
            uint ser = w.Serial[i]; int sp = w.Spc[i];
            bool killed = Damage(w, i, m.Damage * f, out float loss);
            rep.Victims.Add((ser, sp, loss, killed));
            if (killed) { rep.Killed++; BlastKills++; } else rep.Hurt++;
        }
        // fear: a burst of scent on the cell and its neighbours (Decide reads FEAR from the scent)
        int FW = Map.FW, cx = cell % FW, cy = cell / FW;
        for (int k = 0; k < 5; k++)
        {
            int n = World.WrapI(cy + (k == 3 ? 1 : k == 4 ? -1 : 0), FW) * FW + World.WrapI(cx + (k == 1 ? 1 : k == 2 ? -1 : 0), FW);
            w.Scent[n] = MathF.Min(4f, w.Scent[n] + (k == 0 ? 1f : 0.5f));
        }
        if (m.Ignites && Fields.IgniteAt(w, cell)) { rep.Fires++; FiresStarted++; }
        return BlastResult.Exploded;
    }

    static float DistM(float ax, float ay, float bx, float by)
    {
        float dx = World.Delta1(ax - bx), dy = World.Delta1(ay - by);
        return MathF.Sqrt(dx * dx + dy * dy) * M_PER_UNIT;
    }

    /// <summary>Water on the cell or one of its 8 neighbours (a torpedo that runs onto the shore explodes there).</summary>
    static bool NearWater(World w, int c)
    {
        int FW = Map.FW, cx = c % FW, cy = c / FW;
        for (int oy = -1; oy <= 1; oy++)
            for (int ox = -1; ox <= 1; ox++)
                if (w.Cell[World.WrapI(cy + oy, FW) * FW + World.WrapI(cx + ox, FW)] != (byte)Neuroreef.Realm.Land) return true;
        return false;
    }
}
