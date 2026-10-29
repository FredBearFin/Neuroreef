// Clades (DESIGN §7.9, Revision F). There is no designed roster: a clade is a lineage that descends from one
// of the random ancestors, or that split off from another clade when its genes diverged. It carries only a
// name, a colour and its history. Every property of an organism comes from its own genome (Body.Express).

using System;
using System.Collections.Generic;

namespace Neuroreef;

/// <summary>Energy pools of the cycle. 0..12 are fields on the grid; MEAT and BLOOD are animal bodies.</summary>
public enum Pool : byte
{
    Grass, Browse, Fruit, Seed, Nectar, Root, Algae, Phyto,
    Carrion, Dung, Litter, Wood, Detritus,
    Meat, Blood,
}

[Flags]
public enum Realm : byte { None = 0, Land = 1, Shallow = 2, Deep = 4, Water = Shallow | Deep, Any = Land | Shallow | Deep }

public enum Loco : byte { Walk, Wade, Swim, Fly, Sessile }


public static class Pools
{
    public const int N = 15;
    public const int NFIELD = 13;
    public static readonly string[] Names =
        { "grass", "browse", "fruit", "seed", "nectar", "root", "algae", "phyto",
          "carrion", "dung", "litter", "wood", "detritus", "meat", "blood" };
    public static readonly float[] Rho =
        { K.RHO_GRASS, K.RHO_BROWSE, K.RHO_FRUIT, K.RHO_SEED, K.RHO_NECTAR, K.RHO_ROOT, K.RHO_ALGAE, K.RHO_PHYTO,
          K.RHO_CARRION, K.RHO_DUNG, K.RHO_LITTER, K.RHO_WOOD, K.RHO_DETRITUS, K.RHO_MEAT, K.RHO_BLOOD };
    public static bool IsDead(int p) => p >= (int)Pool.Carrion && p <= (int)Pool.Detritus;
    public static bool IsWaterPool(int p) => p == (int)Pool.Algae || p == (int)Pool.Phyto || p == (int)Pool.Detritus;
}

public static class Locomotion
{
    /// <summary>The realm an organism can live in.</summary>
    public static Realm RealmOf(Loco l, bool aquaticBase) => l switch
    {
        Loco.Walk => Realm.Land,
        Loco.Wade => Realm.Land | Realm.Shallow,
        Loco.Swim => Realm.Water,
        Loco.Fly => Realm.Any,
        _ => aquaticBase ? Realm.Water : Realm.Land,           // sessile: where it settled
    };
}

/// <summary>One clade slot: a lineage with a name and a history. Physiology lives in the organisms, not here.</summary>
public sealed class Species
{
    public int Id;
    public string Name = "";
    public byte R, G, B;                // display colour
    public bool Used;                   // the slot holds (or held) a clade
    public bool Alive;                  // at least one living organism (any stage)
    public int Parent = -1;             // clade it split from (-1 = an ancestor)
    public int Ancestor;                // index of the ancestor lineage (0..9)
    public long Born, Extinct = -1;     // steps
    public int Generation;              // how many splits since the ancestor
    public int Serial;                  // unique number for the run (slots are recycled; serials are not)
    public bool Sub;                    // a subspecies: still more than half fertile with the clade it split from
    public string SplitWhy = "";        // how it looked different when it split ("bigger (x1.8), more armour")
    public float SplitDist, SplitGap;   // at the last check: how visibly different its two forms were, and how clear the gap (1 = enough)

    public void Clear() { Name = ""; Used = Alive = false; Parent = -1; Ancestor = 0; Born = 0; Extinct = -1; Generation = 0; Serial = 0; R = G = B = 0; Sub = false; SplitWhy = ""; SplitDist = SplitGap = 0; }
}
