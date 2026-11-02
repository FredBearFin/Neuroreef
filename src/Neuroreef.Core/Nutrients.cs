// NUTRIENTS (2026-11-02): the nutrient (nitrogen) budget. Every flow moves what it takes from one store into another,
// so total nutrient changes only by the explicit inputs and outputs counted in Book:
//   in:   weathering and deposition (Fields, K.WEATHER), symbiotic fixation (Plants, the NFIX trait)
//   out:  what a cell's soil or water holds above N_MAX (leaching and denitrification), the share of burned biomass's
//         nutrient that a fire sends to the air (FIRE_N_LOSS)
//   debt: nutrient that had to be made because a store had less than a flow needed (a plant cell's uptake beyond its soil,
//         an animal's growth or birth beyond the soil or water it stands on). Not physics: counted so it can be seen
//         (NutrientBalanceTests).
//
// The stores and what a unit of each holds (the nutrient unit is the N of 1/C_NUTUSE units of leafy cover at N_LEAF;
// UnitKgN in kg of N per cell):
//   soil and water         w.Nutrient (dissolved or plant-available)
//   leafy cover            C_NUTUSE per unit of herb, shrub, tree and sapling cover; litter the same (no resorption)
//   live wood              WOOD_N x C_NUTUSE per unit (w.LiveWood)
//   dead wood, detritus, carrion, dung, peat: tracked per cell (w.WoodN, DetN, CarN, DungN, PeatN): what fell in
//   algae and phytoplankton Water per unit (N_ALGAE, Redfield)
//   animals                N_BODY per kg of live mass, and the gut's undigested share (w.GutN)
//   seed, fruit, nectar    none: reproduction is counted as carbon only; the nutrient a seedling, or the litter a seed or
//                          fruit rots into, holds is taken from the soil when that tissue forms. (Seeds are N-rich in
//                          life; tracking it would mean tracking every seed in flight between cells.)
// Browse and roots are views of the cover, not stores: a bite of them takes the cover it removes.

using System;

namespace Neuroreef;

public static class Nutrients
{
    /// <summary>The inputs and outputs of the nutrient budget, per row of the field grid (so parallel rows can count
    /// their own), and whether the world is open to them.</summary>
    public sealed class Book
    {
        // IN, OUT: weathering and fixation, leaching and fire. FOUND: the bodies of founders the world brings in (and takes
        // back, those only measured), net. DEBT: a store (soil, water) driven below 0 by plants or producers; DRAW: by an
        // animal's growth or birth
        public const int IN = 0, OUT = 1, DEBT = 2, DRAW = 3, FOUND = 4, NK = 5;
        public readonly double[] Row;
        /// <summary>Inputs and outputs on (the default). Off, a closed world: no weathering, no fixation, nothing lost
        /// above N_MAX and every bit of a fire's nutrient returns as ash (tests of the mass balance).</summary>
        public bool Open = true;
        public Book(int fw) { Row = new double[fw * NK]; }
        double Sum(int k) { double s = 0; for (int i = k; i < Row.Length; i += NK) s += Row[i]; return s; }
        public double In => Sum(IN);
        public double Out => Sum(OUT);
        public double Debt => Sum(DEBT) + Sum(DRAW);
        public double DebtAnimals => Sum(DRAW);
        public double Founders => Sum(FOUND);
        public void Clear() => Array.Clear(Row);
    }

    /// <summary>Kilograms of nitrogen in a nutrient unit of a field cell.</summary>
    public static readonly float UnitKgN = K.N_LEAF * K.PLANT_KG_M2 * Pools.CellM2 / K.C_NUTUSE;
    /// <summary>Nutrient in a unit of leafy cover or litter.</summary>
    public const float Leaf = K.C_NUTUSE;
    /// <summary>Nutrient in a unit of live wood.</summary>
    public const float Wood = K.WOOD_N * K.C_NUTUSE;
    /// <summary>Nutrient in a unit of algae or phytoplankton (dry matter WATER_KG_M2 at N_ALGAE).</summary>
    public const float Water = K.N_ALGAE * K.WATER_KG_M2 / (K.N_LEAF * K.PLANT_KG_M2) * K.C_NUTUSE;
    /// <summary>Nutrient in a kilogram of live animal.</summary>
    public static readonly float BodyKg = K.N_BODY / UnitKgN;

    /// <summary>Nutrient in a living animal's body (its mass) and gut.</summary>
    public static float Body(World w, int i) => w.MassOf(i) * BodyKg;

    /// <summary>Nutrient in the plants of cell i: leafy cover and live wood.</summary>
    public static float Plants(World w, int i)
        => Leaf * (w.Pool[(int)Pool.Grass][i] + w.Shrub[i] + w.Tree[i] + w.Sapling[i]) + Wood * w.LiveWood[i];

    static int RowOf(int cell) => cell / Map.FW;

    /// <summary>Take n from cell i's soil or water (serial phases, the current buffer). What it lacks is counted as debt.</summary>
    public static void Draw(World w, int i, float n)
    {
        if (n <= 0f) { Give(w, i, -n); return; }
        float have = w.Nutrient[i];
        if (n > have) { w.NutBook.Row[RowOf(i) * Book.NK + Book.DRAW] += n - have; w.Nutrient[i] = 0f; }
        else w.Nutrient[i] = have - n;
    }

    /// <summary>Give n to cell i's soil or water (serial phases, the current buffer); above N_MAX it is lost, if the world is open.</summary>
    public static void Give(World w, int i, float n)
    {
        if (n <= 0f) { if (n < 0f) Draw(w, i, -n); return; }
        w.Nutrient[i] = Cap(w, RowOf(i), w.Nutrient[i] + n);
    }

    /// <summary>A store's new level v, with what is above N_MAX counted out (open) and anything below 0 counted as debt.</summary>
    public static float Cap(World w, int y, float v)
    {
        var b = w.NutBook;
        if (v < 0f) { b.Row[y * Book.NK + Book.DEBT] -= v; return 0f; }
        if (v > K.N_MAX && b.Open) { b.Row[y * Book.NK + Book.OUT] += v - K.N_MAX; return K.N_MAX; }
        return v;
    }

    /// <summary>Cell i's soil after its plants took their nutrient, v (Plants, the cell's own row y): what the soil could not
    /// supply was not built, and that much of the cell's cover goes (Liebig's minimum: tissue cannot form without its
    /// nitrogen), every slot by the same share; only what even the whole cover cannot make up is counted as debt.</summary>
    public static float Uptake(World w, int y, int i, float v)
    {
        if (v >= 0f) return Cap(w, y, v);
        float[] G = w.Pool[(int)Pool.Grass];
        float cover = G[i] + w.Shrub[i] + w.Tree[i] + w.Sapling[i];
        float cut = cover > 0f ? MathF.Min(1f, -v / (Leaf * cover)) : 0f;
        G[i] -= G[i] * cut; w.Shrub[i] -= w.Shrub[i] * cut; w.Tree[i] -= w.Tree[i] * cut; w.Sapling[i] -= w.Sapling[i] * cut;
        return Cap(w, y, v + Leaf * cover * cut);
    }

    /// <summary>
    /// The nutrient in `units` of pool p of cell i that an animal is about to take (before the pool is reduced): the tracked
    /// pools give their share of what they hold, and lose it; litter and the water producers hold theirs per unit. The
    /// plants' own pools (grass, browse, roots) are counted by the change of the cell's plants (Plants), and seed, fruit
    /// and nectar hold none.
    /// </summary>
    public static float Take(World w, int p, int i, float units)
    {
        float have = w.Pool[p][i];
        float share = have > 0f ? MathF.Min(1f, units / have) : 0f;
        switch ((Pool)p)
        {
            case Pool.Litter: return Leaf * MathF.Min(units, have);
            case Pool.Algae: case Pool.Phyto: return Water * MathF.Min(units, have);
            case Pool.Wood: return Share(w.WoodN, i, share);
            case Pool.Detritus: return Share(w.DetN, i, share);
            case Pool.Carrion: return Share(w.CarN, i, share);
            case Pool.Dung: return Share(w.DungN, i, share);
            default: return 0f;
        }
    }

    static float Share(float[] a, int i, float s) { float n = a[i] * s; a[i] -= n; return n; }

    /// <summary>An animal swallowed food holding n (RESOLVE): the share it assimilates is excreted (urine) into the soil or
    /// water where it is, the rest stays in the gut for the dung. A body at steady mass excretes what it assimilates
    /// (grazers return 70-95% of what they eat; Haynes &amp; Williams 1993); growth draws its own (Grow).</summary>
    public static void Ingest(World w, int a, int cell, float n, float eff)
    {
        if (n <= 0f) return;
        float keep = n * (1f - eff);
        w.GutN[a] += keep;
        Give(w, cell, n - keep);
    }

    /// <summary>The gut passed a share frac of its contents as dung into cell (RESOLVE): the dung (or, in water, the
    /// detritus) takes that share of its nutrient.</summary>
    public static void Dung(World w, int a, int cell, float frac, bool water)
    {
        float n = w.GutN[a] * frac;
        w.GutN[a] -= n;
        if (water) w.DetN[cell] += n; else w.DungN[cell] += n;
    }

    /// <summary>A body appeared (birth, founding): its nutrient comes from its mother's gut (the food she ate) as far as
    /// it holds it, the rest from the soil or water where it is.</summary>
    public static void BodyIn(World w, int i, int mother) => FromGut(w, mother, World.FIdx(w.X[i], w.Y[i]), Body(w, i));

    /// <summary>A founder is brought into the world (Life.Found): its body's nutrient comes with it from outside, an
    /// input of the model (Book.FOUND), as the animal is; a founder made only to be measured takes it out again (FounderOut).</summary>
    public static void FounderIn(World w, int i) => w.NutBook.Row[RowOf(World.FIdx(w.X[i], w.Y[i])) * Book.NK + Book.FOUND] += Body(w, i);

    public static void FounderOut(World w, int i)
    {
        w.NutBook.Row[RowOf(World.FIdx(w.X[i], w.Y[i])) * Book.NK + Book.FOUND] -= Body(w, i) + w.GutN[i];
        w.GutN[i] = 0f;
    }

    /// <summary>n for an animal's new tissue: from the gut of animal a (the food it ate) as far as it holds it, the rest
    /// from cell's soil or water (Draw).</summary>
    static void FromGut(World w, int a, int cell, float n)
    {
        if (n <= 0f) { Give(w, cell, -n); return; }
        if (a >= 0) { float g = MathF.Min(n, w.GutN[a]); w.GutN[a] -= g; n -= g; }
        if (n > 0f) Draw(w, cell, n);
    }

    /// <summary>A body that leaves no carcass is removed: its nutrient and its gut's return to the soil or water where it is.</summary>
    public static void BodyOut(World w, int i)
    {
        Give(w, World.FIdx(w.X[i], w.Y[i]), Body(w, i) + w.GutN[i]);
        w.GutN[i] = 0f;
    }

    /// <summary>Animal a grew from mass m0 to m1 (kg): what the new tissue holds comes from its gut, else from where it is.</summary>
    public static void Grow(World w, int a, int cell, float m0, float m1) => FromGut(w, a, cell, (m1 - m0) * BodyKg);

    /// <summary>A carcass holding n falls into cell: carrion on land, detritus in water.</summary>
    public static void Carcass(World w, int cell, bool water, float n)
    {
        if (water) w.DetN[cell] += n; else w.CarN[cell] += n;
    }

    /// <summary>The dead pools' nutrient from their mass, for a new world (Plants.Found) or an older save: dead wood and
    /// litter at leafy content, detritus at the water producers', carrion as body, dung as litter (per kg).</summary>
    public static void InitPools(World w)
    {
        int n = Map.FW * Map.FW;
        float[] Wo = w.Pool[(int)Pool.Wood], De = w.Pool[(int)Pool.Detritus], Ca = w.Pool[(int)Pool.Carrion], Du = w.Pool[(int)Pool.Dung];
        float dungKg = Leaf / Pools.Kg[(int)Pool.Litter];
        for (int i = 0; i < n; i++)
        {
            w.WoodN[i] = Leaf * Wo[i]; w.DetN[i] = Water * De[i];
            w.CarN[i] = BodyKg * Ca[i]; w.DungN[i] = dungKg * Du[i];
            w.PeatN[i] = Leaf / K.PEAT_M * w.Peat[i];
        }
    }

    /// <summary>The world's nutrient by store (double sums).</summary>
    public struct Totals
    {
        public double Soil, Water, Plants, Litter, DeadWood, Peat, Detritus, Producers, Carrion, Dung, Animals;
        public readonly double All => Soil + Water + Plants + Litter + DeadWood + Peat + Detritus + Producers + Carrion + Dung + Animals;
        public override readonly string ToString() =>
            $"soil {Soil:F4} water {Water:F4} plants {Plants:F4} litter {Litter:F4} deadwood {DeadWood:F4} peat {Peat:F4} detritus {Detritus:F4} producers {Producers:F4} carrion {Carrion:F6} dung {Dung:F6} animals {Animals:F6} = {All:F4}";
    }

    public static Totals Total(World w)
    {
        var t = new Totals();
        int n = Map.FW * Map.FW;
        float[] Li = w.Pool[(int)Pool.Litter], Al = w.Pool[(int)Pool.Algae], Ph = w.Pool[(int)Pool.Phyto];
        for (int i = 0; i < n; i++)
        {
            if (w.Cell[i] == (byte)Realm.Land) t.Soil += w.Nutrient[i]; else t.Water += w.Nutrient[i];
            t.Plants += Plants(w, i);
            t.Litter += Leaf * Li[i];
            t.DeadWood += w.WoodN[i]; t.Peat += w.PeatN[i]; t.Detritus += w.DetN[i];
            t.Producers += Water * (Al[i] + Ph[i]);
            t.Carrion += w.CarN[i]; t.Dung += w.DungN[i];
        }
        for (int i = 0; i < w.Cap; i++)
        {
            if (w.StageOf[i] == St.Dead) continue;
            t.Animals += Body(w, i) + w.GutN[i];
        }
        return t;
    }
}
