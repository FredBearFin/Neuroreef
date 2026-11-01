// EXPLORE: the person walking in the world (the console's Explore mode). Not an organism and never in a slot: a body
// the animals sense as another organism (Decide.SensePlayer), that predators may strike (Resolve), and that hunts with
// the weapons below (Player.Hit). Its position comes from the console between steps. World.Player is null until a
// console enters Explore, and while it is null nothing here runs, so a world without the person runs bit for bit as
// before (EngineeringTests). It is not state: a save never holds it.

using System;

namespace Neuroreef;

/// <summary>One way of using a weapon (a spear thrust, a thrown spear, an arrow, a rifle shot). Ranges in metres; damage
/// in units of strike power (the sim's PStrike x mass of an attacker: an animal of mass 30 with full weapons strikes 60).</summary>
public sealed record WeaponMode(string Id, string Name, string Kind, float RangeM, float Damage, float SpeedMps, float Cooldown, bool Ammo);

/// <summary>A weapon: its primary use (left button) and an optional secondary one (right button).</summary>
public sealed record Weapon(string Id, string Name, string Key, WeaponMode Primary, WeaponMode? Secondary);

public static class Weapons
{
    // kinds: melee (a reach in front of the eye), thrown and bow (projectiles under gravity, simulated by the console),
    // hitscan (a straight ray). The console builds its weapon list from this table (Wire.Meta: explore.weapons)
    public static readonly Weapon[] All =
    {
        new("spear", "spear", "1", new("spear-thrust", "thrust", "melee", 3f, 45f, 0f, 0.6f, false),
                                   new("spear-throw", "throw", "thrown", 40f, 38f, 24f, 1.0f, false)),
        new("bow", "bow", "2", new("bow-shot", "arrow", "bow", 160f, 32f, 70f, 0.25f, true), null),
        new("rifle", "rifle", "3", new("rifle-shot", "shot", "hitscan", 400f, 80f, 0f, 0.9f, false), null),
    };

    public static WeaponMode? Mode(string id)
    {
        foreach (var w in All) { if (w.Primary.Id == id) return w.Primary; if (w.Secondary?.Id == id) return w.Secondary; }
        return null;
    }
}

public sealed class Player
{
    /// <summary>The pseudo-slot the person has in DECIDE's targets (o1, o2, strike) and in ITarget: never a real slot.</summary>
    public const int Slot = -2;
    /// <summary>Body mass in the sim's units (M = 1 is about 1 kg: K.FLY_M_MAX's convention): a 70 kg person.</summary>
    public const float MASS = 70f;
    /// <summary>Meat in its diet (an omnivore): what the animals sense as its carnivory, and its scent.</summary>
    public const float MEAT = 0.5f;
    /// <summary>How armed it looks (the PStrike an animal would sense): a person carrying a spear, bow or rifle.</summary>
    public const float STRIKE = 1f;
    public const float MAX_HP = 100f;
    /// <summary>Metres per world unit (K.M_PER_UNIT: a field cell of FCELL units is GEO_CELL_M m; the 3D view draws the land,
    /// and since 2026-11-01 the animals' senses and moves, at this scale).</summary>
    public const float M_PER_UNIT = K.M_PER_UNIT;
    /// <summary>Eye height above the ground, for line of sight (m).</summary>
    public const float EYE_M = 1.7f;

    public bool Present;
    public float X, Y, Heading;          // world units, radians (the sim's frame)
    public float Speed;                  // units moved over the last step (BeforeStep)
    public bool Running;                 // running is heard all round (NoiseR)
    public float Hp = MAX_HP;
    public int Deaths, Kills, Hits;
    public uint LastKiller;              // serial of the last animal that killed it
    float _lx = float.NaN, _ly;

    public float MassCbrt => MathK.Cbrt(MASS);

    /// <summary>How far it is heard from any direction: running or wading through brush is loud; walking only up close.</summary>
    public float NoiseR => Running ? K.HEAR_R : 0.25f * K.HEAR_R;

    /// <summary>Set by the console between steps.</summary>
    public void Place(float x, float y, float heading, bool running)
    {
        X = World.Wrap(x); Y = World.Wrap(y); Heading = heading; Running = running;
        if (!Present) { Present = true; _lx = float.NaN; }
    }

    /// <summary>Before a step: its speed over the last step (units per step, as an animal's), and the scent it leaves where
    /// it stands, as an animal of its diet does (Resolve: C_SCENT x carnivory), so AVOID_FEAR and FEAR read it.</summary>
    public void BeforeStep(World w)
    {
        if (!Present) return;
        if (float.IsNaN(_lx)) Speed = 0f;
        else { float dx = World.Delta1(X - _lx), dy = World.Delta1(Y - _ly); Speed = MathF.Sqrt(dx * dx + dy * dy); }
        _lx = X; _ly = Y;
        int cell = World.FIdx(X, Y);
        w.Scent[cell] = MathF.Min(4f, w.Scent[cell] + K.C_SCENT * MEAT);
    }

    /// <summary>How weak it looks (O1_WEAK): a wounded person looks weaker.</summary>
    public float Weakness => 0.3f * (1f - Hp / MAX_HP);

    /// <summary>
    /// Animal a strikes the person (RESOLVE, serial). The same odds as a strike on an animal of the person's mass with no
    /// armour (Resolve.Strike): p = P0_STRIKE x power / (power + defence); a landed hit takes C_DAMAGE x power of a reserve of
    /// C_EMAX x MASS, shown as health. At 0 the person dies and is put back at full health (the console moves it).
    /// </summary>
    public void Struck(World w, int a)
    {
        float power = w.PStrike[a] * w.MassOf(a);
        float defence = MASS * K.DEF_BASE;
        float p = K.P0_STRIKE * power / (power + defence + 1e-6f);
        if (Rng.U(w.Seed, (uint)w.Step, (uint)a, Rs.Strike) >= p)
        {
            w.T.Fight(X, Y, w.Spc[a], -1, Telemetry.F_MISS, w.Step, w.Serial[a], 0);
            return;
        }
        Hits++;
        Hp -= K.C_DAMAGE * power / (K.C_EMAX * MASS) * MAX_HP;
        if (Hp > 0f) { w.T.Fight(X, Y, w.Spc[a], -1, Telemetry.F_WOUND, w.Step, w.Serial[a], 0); return; }
        w.T.Fight(X, Y, w.Spc[a], -1, Telemetry.F_KILL, w.Step, w.Serial[a], 0);
        Deaths++; LastKiller = w.Serial[a]; Hp = MAX_HP;
        _lx = float.NaN;
    }

    public enum HitResult { Wounded, Killed, NoAnimal, OutOfRange, NoLineOfSight, NotPresent, UnknownWeapon }

    /// <summary>The slot of the living organism with this serial, or -1.</summary>
    public static int SlotOf(World w, uint serial)
    {
        if (serial == 0) return -1;
        for (int i = 0; i < w.SlotsUsed; i++) if (w.Serial[i] == serial && w.StageOf[i] != St.Dead && w.StageOf[i] != St.Embryo) return i;
        return -1;
    }

    /// <summary>Body radius of organism i for hit tests (units): the sphere the console aims at, 0.55 of its true body length
    /// (Body.LengthM, 2026-11-01, sent as bodyLenM: the console draws bodies at true size), and 0.1 units (5 m) for the
    /// view's and the sim's places to differ. Was 0.55 of a drawn body 1.5 M^(1/3) units long (82 m at 1 kg).</summary>
    public static float BodyR(World w, int i) => 0.1f + 0.55f * Body.LengthM(w, i) / K.M_PER_UNIT;

    /// <summary>Height of the ground (or the water surface over it) at a place, in metres above the lakes.</summary>
    public static float GroundM(World w, float x, float y)
    {
        int c = World.FIdx(x, y);
        float e = MathF.Max(w.Elev[c], w.SeaLevel);
        return (e - w.SeaLevel) / MathF.Max(1e-3f, 1f - w.SeaLevel) * K.H_RELIEF;
    }

    /// <summary>
    /// A hit the console reports: weapon mode, the animal's serial, the draw (0..1, a bow; 1 otherwise) and the damage the
    /// console computed (it can only lower the weapon's). Checked against the person's place here: the animal lives, is
    /// within the weapon's range (plus its body and what it and the person can have moved since the console saw them),
    /// and the ground between them does not hide it. Then the damage (strike power) is taken from its energy reserve as a
    /// landed strike is (Resolve.Strike: C_DAMAGE x power), less what its armour stops: x DEF_BASE / (DEF_BASE + armour),
    /// the armour a quarter as good on a juvenile (JUV_DEFENCE). Its reserve grows with its mass (C_EMAX x M), so a big
    /// animal takes more hits. It feels pain and the learner is told (PendingR, credited at the next RESOLVE, as a strike
    /// is). When its reserve is gone it dies of Cause.Hunted, leaving its body as carrion (Resolve.Die).
    /// </summary>
    public HitResult Hit(World w, string mode, uint serial, float power, float reported, out int slot, out float loss)
        => Hit(w, mode, serial, power, reported, out slot, out loss, out _, out _);

    /// <param name="distM">how far the animal was from the person (m); limitM: the farthest the weapon reached it (m)</param>
    public HitResult Hit(World w, string mode, uint serial, float power, float reported, out int slot, out float loss, out float distM, out float limitM)
    {
        slot = -1; loss = 0f; distM = limitM = 0f;
        if (!Present) return HitResult.NotPresent;
        var m = Weapons.Mode(mode);
        if (m == null) return HitResult.UnknownWeapon;
        int i = SlotOf(w, serial);
        if (i < 0) return HitResult.NoAnimal;
        slot = i;
        float dx = World.Delta1(w.X[i] - X), dy = World.Delta1(w.Y[i] - Y), d = MathF.Sqrt(dx * dx + dy * dy);
        // what the console may lag behind: its animal moved up to a step and a half, the person up to two steps' moves
        float slack = 1.5f * w.Speed[i] + 2f * Speed + 0.1f;
        float range = m.RangeM / M_PER_UNIT;
        distM = d * M_PER_UNIT; limitM = (range + BodyR(w, i) + slack) * M_PER_UNIT;
        if (!(d <= range + BodyR(w, i) + slack)) return HitResult.OutOfRange;
        if (!LineOfSight(w, X, Y, w.X[i], w.Y[i], m.Kind == "thrown" || m.Kind == "bow" ? 0.25f * m.RangeM : 0f)) return HitResult.NoLineOfSight;
        float dmg = m.Damage * Math.Clamp(power, 0.2f, 1f);
        if (reported > 0f) dmg = MathF.Min(dmg, reported);
        bool juv = w.StageOf[i] == St.Juvenile, egg = w.StageOf[i] == St.Egg;
        float armour = w.PArmour[i] * (juv ? K.JUV_DEFENCE : 1f);
        loss = K.C_DAMAGE * dmg * K.DEF_BASE / (K.DEF_BASE + armour);
        int cell = World.FIdx(w.X[i], w.Y[i]);
        if (!egg)
        {
            w.Energy[i] -= loss;
            w.Pain[i] = 1f;
            w.PendingR[i] -= K.R_PAIN * loss / MathF.Max(w.EmaxOf(i), 1e-3f);
            if (w.Energy[i] > 0f) { w.T.Fight(w.X[i], w.Y[i], -1, w.Spc[i], Telemetry.F_WOUND, w.Step, 0, w.Serial[i]); return HitResult.Wounded; }
        }
        w.T.Fight(w.X[i], w.Y[i], -1, w.Spc[i], egg ? Telemetry.F_EGG : Telemetry.F_KILL, w.Step, 0, w.Serial[i]);
        w.T.KillFlash(cell);
        Kills++;
        Resolve.Die(w, i, Cause.Hunted, -1);
        return HitResult.Killed;
    }

    /// <summary>Whether the ground between two places hides one from the other: the ground (cell elevations, in metres)
    /// sampled along the way may rise at most a little above the line from the person's eye to the animal's middle; an
    /// arc (a thrown spear, an arrow) may also pass over by up to arcM.</summary>
    public static bool LineOfSight(World w, float x0, float y0, float x1, float y1, float arcM)
    {
        float dx = World.Delta1(x1 - x0), dy = World.Delta1(y1 - y0), d = MathF.Sqrt(dx * dx + dy * dy);
        float h0 = GroundM(w, x0, y0) + EYE_M, h1 = GroundM(w, x1, y1) + 10f;
        int n = Math.Max(2, (int)(d / (0.5f * K.FCELL)) + 1);
        const float SLACK = 25f;   // the view's triangles and the sim's cells differ by this much on steep ground
        for (int k = 1; k < n; k++)
        {
            float t = k / (float)n;
            float line = h0 + (h1 - h0) * t + arcM * 4f * t * (1f - t);
            if (GroundM(w, World.Wrap(x0 + dx * t), World.Wrap(y0 + dy * t)) > line + SLACK) return false;
        }
        return true;
    }
}
