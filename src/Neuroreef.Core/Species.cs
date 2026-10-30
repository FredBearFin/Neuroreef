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
    // the most energy an animal can get from each food, a specialist's assimilation (Revision M, ecology review:
    // Van Soest 1994; Robbins 1993): grass browse fruit seed nectar root algae phyto carrion dung litter wood detritus meat blood
    public static readonly float[] DigMax = { 0.5f, 0.5f, 0.8f, 0.75f, 0.95f, 0.6f, 0.6f, 0.7f, 0.85f, 0.3f, 0.25f, 0.15f, 0.4f, 0.9f, 0.9f };
    /// <summary>Foods whose energy is mostly in fibre, digested by fermentation (grass, browse, roots, dung, litter, wood).</summary>
    public static bool Fibrous(int p) => p == (int)Pool.Grass || p == (int)Pool.Browse || p == (int)Pool.Root || p == (int)Pool.Dung || p == (int)Pool.Litter || p == (int)Pool.Wood;
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
    public float SplitDist, SplitGap;
    public float HomeX, HomeY, HomeR;   // where its founders started, and how wide that home range was (Revision P; 0 = not founded)   // at the last check: how visibly different its two forms were, and how clear the gap (1 = enough)

    public void Clear() { Name = ""; Used = Alive = false; Parent = -1; Ancestor = 0; Born = 0; Extinct = -1; Generation = 0; Serial = 0; R = G = B = 0; Sub = false; SplitWhy = ""; SplitDist = SplitGap = 0; HomeX = HomeY = HomeR = 0; }
}
