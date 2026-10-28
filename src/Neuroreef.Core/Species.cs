// Species body plans (DESIGN §6). A species is a body: mass, realm, locomotion, diet, armour,
// senses, reproduction mode and life stages. Nothing here describes behaviour (NR-210).

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

public enum Repro : byte { Sexual, Hermaphrodite, Fission, Facultative, Cyclic, Spores }

public enum Medium : byte { Live, LandEgg, WaterEgg }

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

/// <summary>Body plan of one life stage. Two-stage species (metamorphosis) have a juvenile and an adult stage.</summary>
public sealed class Stage
{
    public Loco Loco;
    public float[] Diet = new float[Pools.N];      // digestive efficiency per pool, 0..1
    public float KThirst;                          // thirst factor; 0 for aquatic and sessile
    public Realm Realm => Loco switch
    {
        Loco.Walk or Loco.Sessile => Realm.Land,
        Loco.Wade => Realm.Land | Realm.Shallow,
        Loco.Swim => Realm.Water,
        _ => Realm.Any,
    };
    public float Carnivory => Diet[(int)Pool.Meat];
}

public sealed class Species
{
    public int Id;
    public string Name = "", Niche = "";
    public byte R, G, B;                // display colour
    public byte Shape;                  // console model id
    public float Mass;                  // adult (aggregate for super-individuals)
    public float Birth = 0.1f;          // newborn mass as a share of adult mass (life-history trait)
    public float UnitMass;              // mass of one organism: used for size perception and defence
    public bool Swarm, Parasite;
    public float Armour, Strike;
    public float KSpeed = 1, KLife = 1, KMat = 1, KSight = 1, Fov = 270;
    public Repro Mode;
    public Medium Medium;
    public int Litter, Gestation, Cooldown;
    public int InitCount;
    public Stage Juv = new(), Adult = new();
    public bool TwoStage;               // metamorphosis at maturity: juvenile stage has its own realm and diet
    public float Care;                  // parental care level (DESIGN §7.8): mammals and birds feed their young

    // Derived (allometry, DESIGN §9.1)
    public float Basal, Emax, GutCap, Digest, Bite, VmaxWalk, Turn, Life, Mature, Sight, Reach, FovCos;

    public Stage StageOf(bool juvenile) => (TwoStage && juvenile) ? Juv : Adult;

    internal void Derive()
    {
        float m = Mass;
        Basal = K.C_BASAL * MathK.Pow(m, K.EXP_BASAL);
        Emax = K.C_EMAX * m;
        GutCap = K.C_GUT * m;
        Digest = K.C_DIGEST * MathK.Pow(m, K.EXP_BASAL);
        Bite = K.C_BITE * MathK.Pow(m, K.EXP_BASAL);
        VmaxWalk = K.C_SPEED * MathK.Pow(m, K.EXP_SPEED) * KSpeed;
        Turn = K.C_TURN * MathK.Pow(m, K.EXP_TURN);
        Life = K.C_LIFE * MathK.Pow(m, K.EXP_LIFE) * KLife;
        Mature = K.C_MATURE * MathK.Pow(m, K.EXP_LIFE) * KMat;
        float sm = Swarm ? MathF.Max(UnitMass * 40f, 0.3f) : m;
        Sight = MathF.Min(K.MAX_SIGHT, K.C_SIGHT * MathK.Pow(sm, K.EXP_SIGHT) * KSight);
        Reach = 1f + 0.4f * MathK.Pow(Swarm ? 1f : m, 1f / 3f);
        FovCos = MathK.Cos(Fov * 0.5f * MathF.PI / 180f);
        // mammals (live birth) suckle their young and birds (eggs on land, flying parents) feed their chicks;
        // fish, amphibians, insects and fungi give no care (DESIGN §7.8)
        Care = !Swarm && (Medium == Medium.Live || (Medium == Medium.LandEgg && Adult.Loco == Loco.Fly)) ? 1f : 0f;
    }

    public float Vmax(Loco l) => l switch
    {
        Loco.Sessile => 0f,
        Loco.Swim => VmaxWalk * K.SWIM_SPEED,
        Loco.Fly => VmaxWalk * K.FLY_SPEED,
        _ => VmaxWalk,
    };
}

/// <summary>The roster (DESIGN §6.2), chosen by filling every pool of the energy cycle with a consumer.</summary>
public static class Roster
{
    public static readonly Species[] All = Build();
    public static int Count => All.Length;

    static Species S(int id, string name, string niche, uint rgb, byte shape, float mass, Loco loco,
                     float armour, float strike, Repro mode, Medium med, int litter, int gest, int cool, int init,
                     params (Pool p, float e)[] diet)
    {
        var s = new Species
        {
            Id = id, Name = name, Niche = niche, R = (byte)(rgb >> 16), G = (byte)(rgb >> 8), B = (byte)rgb, Shape = shape,
            Mass = mass, UnitMass = mass, Armour = armour, Strike = strike,
            Mode = mode, Medium = med, Litter = litter, Gestation = gest, Cooldown = cool, InitCount = init,
        };
        s.Adult.Loco = loco;
        foreach (var (p, e) in diet) s.Adult.Diet[(int)p] = e;
        s.Adult.KThirst = (loco == Loco.Swim || loco == Loco.Sessile) ? 0f : 1f;
        s.Juv = s.Adult;
        return s;
    }

    static void Stage2(Species s, Loco loco, params (Pool p, float e)[] diet)
    {
        s.TwoStage = true;
        s.Juv = new Stage { Loco = loco, KThirst = loco == Loco.Swim ? 0f : 1f };
        foreach (var (p, e) in diet) s.Juv.Diet[(int)p] = e;
    }

    static Species[] Build()
    {
        var L = new List<Species>();
        Species x;
        // --- primary consumers (land)
        x = S(0, "Drifters", "small grazer", 0x2ee0ff, 0, 1.0f, Loco.Walk, 0.3f, 0f, Repro.Sexual, Medium.Live, 2, 150, 150, 2500,
              (Pool.Grass, 0.60f), (Pool.Browse, 0.15f), (Pool.Seed, 0.20f));
        x.Fov = 300; L.Add(x);
        x = S(1, "Browsers", "large browser", 0xffc238, 1, 18f, Loco.Walk, 1.6f, 0.6f, Repro.Sexual, Medium.Live, 1, 600, 600, 250,
              (Pool.Browse, 0.60f), (Pool.Fruit, 0.45f), (Pool.Grass, 0.20f));
        x.KSpeed = 0.6f; x.Fov = 280; L.Add(x);
        x = S(2, "Tuskers", "rooting omnivore", 0xb07a50, 2, 6f, Loco.Walk, 1.0f, 0.7f, Repro.Sexual, Medium.Live, 4, 300, 300, 300,
              (Pool.Root, 0.60f), (Pool.Fruit, 0.50f), (Pool.Meat, 0.45f), (Pool.Carrion, 0.40f), (Pool.Seed, 0.30f));
        x.KSpeed = 0.8f; x.Fov = 240; L.Add(x);
        x = S(3, "Gliders", "frugivore, seed disperser", 0xff7ad9, 3, 0.6f, Loco.Fly, 0.2f, 0f, Repro.Sexual, Medium.LandEgg, 3, 200, 200, 600,
              (Pool.Fruit, 0.70f), (Pool.Seed, 0.40f), (Pool.Nectar, 0.30f));
        x.Fov = 300; x.KSight = 1.2f; L.Add(x);
        x = S(4, "Burrowers", "granivore, cacher", 0xd8c49a, 4, 0.4f, Loco.Walk, 0.2f, 0f, Repro.Sexual, Medium.Live, 6, 120, 120, 1200,
              (Pool.Seed, 0.70f), (Pool.Root, 0.30f), (Pool.Grass, 0.10f));
        x.Fov = 320; L.Add(x);
        // --- insects and other super-individuals (land)
        x = S(5, "Hummers", "pollinator swarm", 0xfff27a, 5, 0.8f, Loco.Fly, 0.5f, 0f, Repro.Fission, Medium.Live, 1, 0, 200, 600,
              (Pool.Nectar, 0.80f));
        x.Swarm = true; x.UnitMass = 0.01f; x.KLife = 0.6f; x.Adult.KThirst = 0.5f; x.Fov = 360; L.Add(x);
        x = S(6, "Chewers", "leaf-insect swarm", 0x9be14a, 6, 1.0f, Loco.Fly, 0.5f, 0f, Repro.Facultative, Medium.Live, 2, 60, 100, 1200,
              (Pool.Grass, 0.50f), (Pool.Browse, 0.50f));
        x.Swarm = true; x.UnitMass = 0.01f; x.KLife = 0.5f; x.KSpeed = 0.5f; x.Adult.KThirst = 0.5f; x.Fov = 360; L.Add(x);
        x = S(7, "Delvers", "detritivore (worms, beetles)", 0x8a6a4a, 7, 1.2f, Loco.Walk, 0.5f, 0f, Repro.Hermaphrodite, Medium.LandEgg, 2, 150, 150, 1200,
              (Pool.Dung, 0.60f), (Pool.Litter, 0.50f));
        x.Swarm = true; x.UnitMass = 0.01f; x.KSpeed = 0.35f; x.KLife = 0.7f; x.Adult.KThirst = 0.3f; x.Fov = 360; L.Add(x);
        x = S(8, "Rotmats", "fungal mat (sessile)", 0xe8e0d0, 8, 1.0f, Loco.Sessile, 2.0f, 0f, Repro.Spores, Medium.LandEgg, 1, 300, 300, 500,
              (Pool.Wood, 0.50f), (Pool.Litter, 0.50f), (Pool.Carrion, 0.40f));
        x.Swarm = true; x.UnitMass = 0.05f; x.KLife = 1.5f; x.Fov = 360; L.Add(x);
        // --- scavenger
        x = S(9, "Gleaners", "vertebrate scavenger", 0xff7624, 9, 2.0f, Loco.Walk, 0.4f, 0.3f, Repro.Sexual, Medium.Live, 6, 200, 250, 300,
              (Pool.Carrion, 0.80f), (Pool.Meat, 0.30f));
        x.KSight = 1.4f; x.Fov = 260; L.Add(x);
        // --- secondary and higher consumers (land)
        x = S(10, "Snappers", "insectivore", 0x4aa8ff, 10, 0.35f, Loco.Fly, 0.2f, 0.8f, Repro.Sexual, Medium.LandEgg, 4, 150, 200, 500,
              (Pool.Meat, 0.75f), (Pool.Fruit, 0.15f));
        x.Fov = 220; L.Add(x);
        x = S(11, "Prowlers", "solitary mesopredator", 0xc04ad0, 11, 3.0f, Loco.Walk, 0.5f, 1.0f, Repro.Sexual, Medium.Live, 3, 250, 300, 200,
              (Pool.Meat, 0.80f), (Pool.Carrion, 0.20f));
        x.KSpeed = 1.05f; x.Fov = 180; x.KSight = 1.2f; L.Add(x);
        x = S(12, "Stalkers", "pack hunter", 0xff3a9e, 12, 4.0f, Loco.Walk, 0.5f, 1.0f, Repro.Sexual, Medium.Live, 3, 300, 400, 200,
              (Pool.Meat, 0.85f), (Pool.Carrion, 0.30f));
        x.KSpeed = 1.1f; x.Fov = 170; x.KSight = 1.2f; x.KMat = 1.3f; L.Add(x);
        x = S(13, "Leviathans", "apex predator", 0x9e66ff, 13, 25f, Loco.Wade, 1.0f, 1.3f, Repro.Sexual, Medium.Live, 1, 900, 1000, 40,
              (Pool.Meat, 0.85f), (Pool.Carrion, 0.40f));
        x.KSpeed = 0.65f; x.Fov = 150; x.KMat = 1.3f; L.Add(x);
        // --- freshwater
        x = S(14, "Bloodflies", "parasite swarm (aquatic larvae)", 0xd02828, 14, 0.5f, Loco.Fly, 0.3f, 0.6f, Repro.Sexual, Medium.WaterEgg, 6, 100, 150, 600,
              (Pool.Blood, 0.70f), (Pool.Nectar, 0.50f));
        x.Swarm = true; x.Parasite = true; x.UnitMass = 0.005f; x.KLife = 0.6f; x.Adult.KThirst = 0.5f; x.Fov = 360;
        Stage2(x, Loco.Swim, (Pool.Detritus, 0.60f), (Pool.Phyto, 0.30f)); L.Add(x);
        x = S(15, "Siltmites", "zooplankton", 0xb8f0e8, 15, 1.5f, Loco.Swim, 0.2f, 0f, Repro.Cyclic, Medium.WaterEgg, 2, 40, 60, 1500,
              (Pool.Phyto, 0.70f), (Pool.Detritus, 0.20f));
        x.Swarm = true; x.UnitMass = 0.002f; x.KLife = 0.4f; x.KSpeed = 0.4f; x.Fov = 360; L.Add(x);
        x = S(16, "Minnows", "grazing fish", 0x7ad0ff, 16, 0.4f, Loco.Swim, 0.3f, 0.4f, Repro.Sexual, Medium.WaterEgg, 8, 100, 150, 1200,
              (Pool.Algae, 0.60f), (Pool.Meat, 0.50f), (Pool.Detritus, 0.20f));
        x.Fov = 300; L.Add(x);
        x = S(17, "Pikes", "predatory fish", 0x3f8f6a, 17, 3.0f, Loco.Swim, 0.6f, 1.0f, Repro.Sexual, Medium.WaterEgg, 4, 200, 300, 150,
              (Pool.Meat, 0.85f));
        x.KSpeed = 1.15f; x.Fov = 200; L.Add(x);
        x = S(18, "Croakers", "amphibian (tadpole to frog)", 0x5fd068, 18, 0.6f, Loco.Wade, 0.3f, 0.6f, Repro.Sexual, Medium.WaterEgg, 6, 120, 200, 500,
              (Pool.Meat, 0.70f));
        x.Adult.KThirst = 0.5f; x.Fov = 280;
        Stage2(x, Loco.Swim, (Pool.Algae, 0.60f), (Pool.Detritus, 0.20f)); L.Add(x);
        x = S(19, "Herons", "wading bird", 0xe8f0ff, 19, 2.5f, Loco.Fly, 0.4f, 1.0f, Repro.Sexual, Medium.LandEgg, 3, 250, 300, 100,
              (Pool.Meat, 0.80f));
        x.Fov = 200; x.KSight = 1.1f; L.Add(x);

        // newborn mass as a share of adult mass: large calves for big mammals, tiny eggs for fish and insects
        float[] birth = { 0.15f, 0.10f, 0.05f, 0.10f, 0.05f, 0.15f, 0.10f, 0.10f, 0.10f, 0.05f,
                          0.10f, 0.05f, 0.05f, 0.06f, 0.02f, 0.20f, 0.02f, 0.01f, 0.01f, 0.08f };
        for (int k = 0; k < L.Count; k++) L[k].Birth = birth[k];

        var a = L.ToArray();
        for (int i = 0; i < a.Length; i++) { if (a[i].Id != i) throw new InvalidOperationException("roster id order"); a[i].Derive(); }
        return a;
    }

    /// <summary>
    /// Energy-cycle closure check (DESIGN §6.4, NR-315, NR-316). Returns a list of failures; empty = closed.
    /// </summary>
    public static List<string> ClosureCheck(Species[] roster)
    {
        var fails = new List<string>();
        var consumed = new bool[Pools.N];
        foreach (var s in roster)
        {
            foreach (var st in s.TwoStage ? new[] { s.Juv, s.Adult } : new[] { s.Adult })
            {
                bool eats = false;
                for (int p = 0; p < Pools.N; p++)
                {
                    if (st.Diet[p] <= 0) continue;
                    consumed[p] = true;
                    bool reachable = p switch
                    {
                        (int)Pool.Algae => (st.Realm & Realm.Shallow) != 0,
                        (int)Pool.Phyto or (int)Pool.Detritus => (st.Realm & Realm.Water) != 0,
                        (int)Pool.Meat or (int)Pool.Blood => true,
                        _ => (st.Realm & Realm.Land) != 0,
                    };
                    if (reachable) eats = true;
                }
                if (!eats) fails.Add($"{s.Name} ({st.Loco}) has no food pool reachable in its realm");
            }
            if (s.Litter < 1) fails.Add($"{s.Name} has no offspring per reproduction");
            bool needsPartner = s.Mode is Repro.Sexual or Repro.Hermaphrodite or Repro.Cyclic or Repro.Facultative;
            if (needsPartner && s.InitCount < 2) fails.Add($"{s.Name} needs partners but starts with < 2");
            if (s.Medium == Medium.WaterEgg && (s.Adult.Realm & Realm.Water) == 0 && s.Adult.Loco != Loco.Fly && s.Adult.Loco != Loco.Wade)
                fails.Add($"{s.Name} lays eggs in water but cannot reach water");
            if (s.Medium == Medium.LandEgg && (s.Adult.Realm & Realm.Land) == 0)
                fails.Add($"{s.Name} lays eggs on land but cannot reach land");
        }
        for (int p = 0; p < Pools.N; p++)
            if (!consumed[p]) fails.Add($"pool '{Pools.Names[p]}' has no consumer");
        // Every species is meat for someone, or ends as carrion/detritus which has consumers.
        if (!consumed[(int)Pool.Carrion] || !consumed[(int)Pool.Detritus]) fails.Add("dead animal bodies are not recycled");
        return fails;
    }
}
