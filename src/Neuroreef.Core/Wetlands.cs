// Wetlands and peat (2026-11-01). Nothing here places a marsh or a bog: a wetland is land whose water table stands at or
// near the surface for much of the year, as the water cycle has it, and what grows, rots and piles up there follows.
//
// - The water table. A cell's deep groundwater (WaterCycle: Deep, mm) fills an aquifer of drainable porosity WT_SY: the
//   water table stands (GW_CAP - Deep) / WT_SY below the ground (GW_CAP: a 15-m aquifer), and at GW_CAP it reaches the
//   surface and the rest emerges (seeps, springs). Near water it stands no deeper than the ground's height above the
//   nearest drainage's water (HAND, Renno et al. 2008; Nobre et al. 2011: ground less than a few metres above its stream
//   is waterlogged): the stream or lake holds it up, so a valley floor, a shore or a floodplain is wet whatever its own
//   store holds. Within a cell the water table is shallower where the ground converges and is flat: TOPMODEL's local deficit
//   (Beven & Kirkby 1979; Sivapalan et al. 1987) puts it (TWI - mean TWI) / WT_F higher than the cell's mean, with the
//   topographic wetness index TWI = ln(a / tan b) (a the area drained per unit contour, tan b the slope).
// - Saturation. In an update the ground is waterlogged where that water table is within WET_ZWT of the surface (the
//   wetland hydrology criterion: saturation within 30 cm; NRC 1995), or where groundwater emerges, a pond stands or a
//   river floods. Then groundwater rises into the root zone (capillary rise: the soil fills from the deep store, store to
//   store, so the budget closes): its moisture is ~1. WetDur is the share of the last year it was waterlogged, WetMin the
//   share fed by minerotrophic water (emerging groundwater, floods, a water table held up by the water nearby).
// - Wetlands. Land waterlogged WET_DUR of the year or more is a wetland: a fen or a bog where peat is PEAT_MIN deep or
//   more (a bog if fed by rain alone, WetMin under WET_MINERO: ombrotrophic), else a swamp where woody plants (shrubs and
//   trees) cover WET_WOODY of it, else a marsh. Its water is shallow and drinkable (Ponds: Wet = 2) while waterlogged.
// - Peat (Fields.Row). Litter and wood decompose at ANOX_ROT of the aerobic rate for the share of the year the ground is
//   waterlogged (anoxia: Clymo 1984; Freeman et al. 2001), and humify into peat at PEAT_FORM a step there. Peat decays at
//   PEAT_ROT_AN a year under water and PEAT_ROT_OX a year when drained (oxidation, subsidence), faster when warm (Q10);
//   what decays returns its nutrient to the soil, so waterlogged ground cycles its nutrient slowly and holds it in peat. A
//   new world's peat is what that balance laid down over the PEAT_AGE years since the last glaciation (Holocene: Clymo's
//   model, P = p / alpha (1 - e^(-alpha T))).
// - Plants (Plants.cs) feel the waterlogging as anoxia (WetDur) through their flood-tolerance trait (PT.FLOOD).

using System;

namespace Neuroreef;

public static class Wetlands
{
    public const byte None = 0, Marsh = 1, Swamp = 2, Fen = 3, Bog = 4;
    public static readonly string[] Names = { "none", "marsh", "swamp", "fen", "bog" };

    /// <summary>WaterCycle.Init: the wetness index and the height above drainage of every cell, on the drainage (Recv,
    /// DrainOrder) and the waters Terrain.Build made.</summary>
    public static void Build(World w)
    {
        int FW = Map.FW, n = FW * FW;
        float epm = w.ElevPerM, cell = K.GEO_CELL_M;
        var area = new float[n]; var refH = new float[n];
        // drained area (cells), sources first
        for (int i = 0; i < n; i++) area[i] = w.Cell[i] == (byte)Realm.Land ? 1f : 0f;
        for (int q = n - 1; q >= 0; q--)
        {
            int c = w.DrainOrder[q], r = w.Recv[c];
            if (r >= 0 && w.Cell[c] == (byte)Realm.Land && w.Cell[r] == (byte)Realm.Land) area[r] += area[c];
        }
        // the water surface each cell drains to, outlets first
        for (int q = 0; q < n; q++)
        {
            int c = w.DrainOrder[q], r = w.Recv[c];
            refH[c] = w.Cell[c] != (byte)Realm.Land ? w.Elev[c] + w.Depth[c] : r >= 0 ? refH[r] : w.Elev[c];
        }
        double sum = 0; int land = 0;
        for (int i = 0; i < n; i++)
        {
            w.Twi[i] = 0f; w.Hand[i] = 0f;
            if (w.Cell[i] != (byte)Realm.Land) continue;
            int x = i % FW, y = i / FW;
            float e = w.Elev[i], drop = 0f;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int j = World.WrapI(y + dy, FW) * FW + World.WrapI(x + dx, FW);
                    float ej = w.Cell[j] == (byte)Realm.Land ? w.Elev[j] : w.Elev[j] + w.Depth[j];
                    drop = MathF.Max(drop, (e - ej) / (dx != 0 && dy != 0 ? 1.4142135f : 1f));
                }
            float tanB = MathF.Max(K.TWI_SMIN, drop / epm / cell);
            w.Twi[i] = MathK.Log(area[i] * cell / tanB);
            sum += w.Twi[i]; land++;
            w.Hand[i] = MathF.Max(0f, e - refH[i]) / epm;
        }
        w.TwiMean = land > 0 ? (float)(sum / land) : 0f;
    }

    /// <summary>Depth of the water table below the ground at cell c holding deep groundwater g (m; at or below 0: at the
    /// surface): its store's, (GW_CAP - g) / WT_SY less TOPMODEL's (TWI - mean TWI) / WT_F, but no deeper than the cell's
    /// height above the nearest drainage's water (held: that water holds it up).</summary>
    public static float TableM(World w, int c, float g, out bool held)
    {
        float z = (K.GW_CAP - g) * (0.001f / K.WT_SY) - (w.Twi[c] - w.TwiMean) / K.WT_F, h = w.Hand[c];
        held = h < z;
        return MathF.Min(z, h);
    }

    /// <summary>WaterCycle.Finish, for a land cell after its groundwater has moved (g: its deep store; emerged: what came out
    /// of it; qc: the water leaving the cell down its drainage this update): is its water table within WET_ZWT of the surface
    /// (or groundwater emerging, a pond standing, a flood)? If so its soil fills from below, from the deep store (capillary
    /// rise) and then from the water running through it (seepage from the stream, as a lake's or river's water holds up the
    /// water table on its shore): store to store, so the budget closes. Sets WetNow (bit 1 waterlogged; bit 2 fed by
    /// minerotrophic water: emerging groundwater, a flood, or a water table held up by the water nearby) and returns the
    /// deep store left.</summary>
    public static float Saturate(World w, int c, float g, float emerged, ref float qc)
    {
        bool flood = w.Wet[c] == 3;
        bool table = TableM(w, c, g, out bool held) <= K.WET_ZWT;
        bool sat = emerged > 0f || flood || (w.PondSite[c] != 0 && w.PondWater[c] >= K.POND_WET) || table;
        bool mineral = sat && (emerged > 0f || flood || (table && held));
        w.WetNow[c] = (byte)((sat ? 1 : 0) | (mineral ? 2 : 0));
        if (!sat) return g;
        float room = w.SoilCap[c] - w.SoilWater[c];
        if (room <= 0f) return g;
        float a = MathF.Min(g, room), b = MathF.Min(MathF.Max(0f, qc), room - a);
        g -= a; qc -= b;
        w.SoilWater[c] += a + b; w.Moisture[c] = w.SoilWater[c] / w.SoilCap[c];
        return g;
    }

    /// <summary>After an update (WaterCycle.Finish, not in the spin-up): the year's share of waterlogging follows this
    /// update's, and every cell's wetland kind is classified again.</summary>
    public static void Update(World w)
    {
        int n = Map.FW * Map.FW;
        float a = MathF.Min(1f, WaterCycle.YearFrac);
        for (int i = 0; i < n; i++)
        {
            if (w.Cell[i] != (byte)Realm.Land) continue;
            byte now = w.WetNow[i];
            w.WetDur[i] += a * ((now & 1) - w.WetDur[i]);
            w.WetMin[i] += a * (((now >> 1) & 1) - w.WetMin[i]);
        }
        Classify(w);
    }

    /// <summary>Every land cell's wetland kind from its waterlogging, peat and woody cover.</summary>
    public static void Classify(World w)
    {
        int n = Map.FW * Map.FW;
        for (int i = 0; i < n; i++)
        {
            byte k = None;
            if (w.Cell[i] == (byte)Realm.Land && w.WetDur[i] >= K.WET_DUR)
                k = w.Peat[i] >= K.PEAT_MIN ? (w.WetMin[i] < K.WET_MINERO ? Bog : Fen)
                  : w.Shrub[i] + w.Tree[i] >= K.WET_WOODY ? Swamp : Marsh;
            w.Wetland[i] = k;
        }
    }

    /// <summary>Fields.Row, a land cell: the decomposition factor of litter and wood under its waterlogging (1 when drained).</summary>
    public static float Anoxic(float wetDur) => 1f - (1f - K.ANOX_ROT) * wetDur;

    /// <summary>
    /// Fields.Row, a land cell, one step: litter and wood humify into peat for the share of the year the ground is
    /// waterlogged, and the peat decays (q: the temperature's factor on decomposition). Returns the nutrient that
    /// decayed out of the peat (it returns to the soil): the peat keeps what its litter and wood held (World.PeatN,
    /// 2026-11-02). Writes only cell i.
    /// </summary>
    public static float Peat(World w, int i, float[] Li, float[] Wo, float q)
    {
        float a = w.WetDur[i], p = w.Peat[i], pn = w.PeatN[i], rot = 0f;
        if (a > 0f)
        {
            float h = K.PEAT_FORM * a, l = Li[i] * h, o = Wo[i] * h, on = w.WoodN[i] * h;
            Li[i] -= l; Wo[i] -= o; w.WoodN[i] -= on; p += K.PEAT_M * (l + o);
            pn += Nutrients.Leaf * l + on;
        }
        if (p > 0f)
        {
            float r = (K.PEAT_ROT_AN * a + K.PEAT_ROT_OX * (1f - a)) * q / Nature.SeasonLength;
            p -= p * r; rot = pn * r; pn -= rot;
        }
        w.Peat[i] = p; w.PeatN[i] = pn;
        return rot;
    }

    /// <summary>
    /// A new world's peat (Simulation.BuildLand, after the spin-up): what the cell's waterlogging, litter and wood would
    /// have laid down over the PEAT_AGE years since the last glaciation, P = p / alpha (1 - e^(-alpha T)) (Clymo 1984), with
    /// p and alpha the rates Peat runs at (at the cell's annual mean temperature). Then the wetlands are classified.
    /// </summary>
    public static void Holocene(World w)
    {
        int n = Map.FW * Map.FW;
        float steps = Nature.SeasonLength;
        var Li = w.Pool[(int)Pool.Litter]; var Wo = w.Pool[(int)Pool.Wood];
        for (int i = 0; i < n; i++)
        {
            w.Peat[i] = 0f;
            float a = w.WetDur[i];
            if (w.Cell[i] != (byte)Realm.Land || a <= 0f) continue;
            float q = Climate.On ? Climate.Rot[Climate.Idx(Climate.AnnualMean(w, i))] : 1f;
            float p = K.PEAT_M * K.PEAT_FORM * a * (Li[i] + Wo[i]) * steps;            // m a year
            float alpha = (K.PEAT_ROT_AN * a + K.PEAT_ROT_OX * (1f - a)) * q;           // a year
            w.Peat[i] = alpha > 0f ? p / alpha * (1f - MathK.Exp(-alpha * K.PEAT_AGE)) : p * K.PEAT_AGE;
        }
        // the peat holds what its litter held (an initial state, as the peat itself is)
        for (int i = 0; i < n; i++) w.PeatN[i] = Nutrients.Leaf / K.PEAT_M * w.Peat[i];
        Classify(w);
    }

    /// <summary>Census: wetland cells of each kind (index by kind; 0 = dry land) and the mean peat over the wetlands (m).</summary>
    public static (int[] count, float peat) Count(World w)
    {
        var c = new int[5]; double p = 0; int m = 0;
        for (int i = 0; i < Map.FW * Map.FW; i++)
        {
            if (w.Cell[i] != (byte)Realm.Land) continue;
            c[w.Wetland[i]]++;
            if (w.Wetland[i] != None) { p += w.Peat[i]; m++; }
        }
        return (c, m > 0 ? (float)(p / m) : 0f);
    }
}
