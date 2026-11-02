// Respiration and the water column (2026-11-01). Nothing here says where an animal may live: it says how much oxygen
// its organs can draw where it is, and what being there costs. Where it cannot draw what it burns, it runs an oxygen
// debt, and a debt it cannot bear kills it (suffocated out of water, drowned in it). So gill-breathers die ashore and
// air-breathers that cannot keep their heads up die in water, from the physics alone.
//
//   demand    the energy it burns this step (basal, moving, keeping warm): oxygen consumed in proportion (Kleiber's
//             M^0.75, its activity and heat budget come from RESOLVE)
//   supply    lungs     LUNG_SCOPE x basal at M x the partial pressure of oxygen: the barometric formula,
//                       p(h)/p0 = exp(-M_air g h / (R T)), so thin mountain air gives less
//             gills     GILL_SCOPE x basal x the oxygen dissolved in the water against air-saturated water at T_REF
//                       (Henry's law with Weiss's 1970 solubility: warm water holds less; the partial pressure at the
//                       lake's height scales it), less in the still deep water of a warm stratified lake; in air,
//                       GILL_AIR of that (collapsed lamellae)
//             skin      SKIN x basal x M^(-1/3) (surface over volume) x the medium's oxygen, unless fur covers it
//             the cost of breathing: pumping water costs GILL_VENT of the metabolism it serves, air AIR_VENT, each more
//             as the medium holds less oxygen (water carries ~30x less oxygen than air and is ~800x denser)
//   gene      B_LUNGS: 0 gills, 1 lungs; between, both organs at those shares (an amphibian, a lungfish)
//
// The water column: a swimmer holds a depth below the surface (World.Z, metres), chosen by its depth-preference gene
// (B_DIVE) and pulled toward the surface as it runs out of breath (the surfacing reflex of air-breathing divers, and
// the aquatic surface respiration of fishes in hypoxic water). What depth means:
//   light     Beer-Lambert, exp(-K_LIGHT z): plankton is food near the surface; attached algae and detritus lie on the
//             bottom (within BOTTOM_BAND of it)
//   water     the mixed layer (Z_MIX) is as warm as the surface; below, it cools toward T_DEEP (the thermocline) and, in
//             warm standing water, loses its oxygen (Z_HYPOX); a flowing river is mixed through
//   breath    an animal that draws less than it needs at depth (an air-breather) dives on a held breath: its store,
//             O2_STORE x M, lasts store / need (~M^0.25: Schreer & Kovacs 1997), the round trip at DIVE_V limits how
//             deep it goes, and it spends the share of the step at depth that keeps its oxygen balanced (ZShare); the
//             rest it is at the surface breathing
//   reach     two animals in water meet only when their depths overlap (STRIKE_DZ), counting each one's time at its
//             depth and at the surface: a fish deep down is safe from a heron or a bear
// A body that cannot swim well enough to hold a depth (0.1 + 0.9 fins below DEEP_SWIM) either walks on the bottom
// (its head in the air where the water is shallower than it stands, STAND_H x M^(1/3)) or paddles at the surface,
// keeping its head up for the share of the time its fins and legs allow (LEG_PADDLE); whichever gives it more oxygen.
// All maths through MathK (NR-104); every function here reads the world and its own slot only.

using System;

namespace Neuroreef;

public static class Respiration
{
    const float R_GAS = 8.314f, M_AIR = 0.02896f, G0 = 9.81f, KELVIN = 273.15f;

    /// <summary>Partial pressure of oxygen at a height (m above the sea) against sea level, air at tC (°C): the isothermal
    /// barometric formula, exp(-M g h / (R T)); ~0.55 at 5,000 m.</summary>
    public static float AirO2(float altitudeM, float tC)
        => MathK.Exp(-MathF.Max(0f, altitudeM) * (M_AIR * G0 / R_GAS) / (MathF.Max(tC, -60f) + KELVIN));

    /// <summary>Oxygen dissolved in fresh water in equilibrium with air at sea level (mg/L) at tC (°C): Weiss (1970),
    /// ln C = A1 + A2 (100/T) + A3 ln(T/100) + A4 (T/100) in mL/L, x 1.429 mg/mL. 14.6 at 0 °C, 9.1 at 20, 7.6 at 30.</summary>
    public static float DissolvedO2(float tC)
    {
        float t = (MathF.Max(tC, 0f) + KELVIN) / 100f;
        float ln = -173.4292f + 249.6339f / t + 143.3483f * MathK.Log(t) - 21.8492f * t;
        return MathK.Exp(ln) * 1.429f;
    }

    static readonly float DoRef = DissolvedO2(K.T_REF);

    /// <summary>Seconds in a reference step: a year is Nature.SeasonLength reference steps (x World.Dt for a world's step).</summary>
    public static float StepSeconds => 365.25f * 86400f / Nature.SeasonLength;

    /// <summary>Height of the surface (the water's where there is water, else the ground's) above the sea, m.</summary>
    public static float SurfaceM(World w, int cell) => MathF.Max(0f, (w.Elev[cell] + w.Depth[cell] - w.SeaLevel) / w.ElevPerM);

    /// <summary>Depth of the water column in a cell, m (0 on land).</summary>
    public static float ColumnM(World w, int cell) => (w.Cell[cell] & (byte)Realm.Water) != 0 ? MathF.Max(0f, w.Depth[cell]) / w.ElevPerM : 0f;

    static float AirT(World w, int cell) => Climate.On ? w.Temp[cell] : K.T_REF;

    /// <summary>A river channel or its banks: flowing water, mixed through.</summary>
    static bool Flowing(World w, int cell) => w.FloodFrom[cell] >= 0;

    /// <summary>Water temperature at depth z (m) in a cell: the surface's through the mixed layer, then toward T_DEEP.</summary>
    public static float WaterT(World w, int cell, float z)
    {
        float ts = Climate.Water(AirT(w, cell));
        if (w.Ocean) return Ocean.WaterT(ts, z);   // the sea's mixed layer and permanent thermocline (2026-11-02)
        if (z <= K.Z_MIX || Flowing(w, cell)) return ts;
        return K.T_DEEP + (ts - K.T_DEEP) * MathK.Exp(-(z - K.Z_MIX) / K.Z_THERMO);
    }

    /// <summary>
    /// Oxygen in the water at depth z (m) against air-saturated water at sea level and T_REF: saturation at the water's
    /// temperature there (Henry's law, Weiss 1970) and at the partial pressure over the lake, less below the mixed layer
    /// of warm standing water (stratified: the deep water is cut off from the air and its oxygen used up).
    /// </summary>
    public static float WaterO2(World w, int cell, float z)
    {
        float ta = AirT(w, cell), ts = Climate.Water(ta);
        // the sea (2026-11-02): salt holds less oxygen (Weiss 1970), and the deep water keeps most of it (Ocean.O2Kept)
        if (w.Ocean) return Ocean.DissolvedO2(Ocean.WaterT(ts, z), K.SALINITY) / DoRef * AirO2(SurfaceM(w, cell), ta) * Ocean.O2Kept(ts, z);
        float a = DissolvedO2(WaterT(w, cell, z)) / DoRef * AirO2(SurfaceM(w, cell), ta);
        if (z > K.Z_MIX && !Flowing(w, cell))
        {
            float strat = Math.Clamp((ts - K.T_DEEP) / 16f, 0f, 1f);
            a *= MathK.Exp(-strat * (z - K.Z_MIX) / K.Z_HYPOX);
        }
        return a;
    }

    /// <summary>
    /// The basal metabolism of body i at its current size, its brain's cost included (as RESOLVE charges it): the unit of
    /// its organs' capacity. Lungs, gills, heart and blood are built to the oxygen the body's own tissues need
    /// (symmorphosis: Weibel, Taylor &amp; Hoppeler 1991), so a costly body (a big brain, fast muscle, weapons) has organs to
    /// match; their scope over it is LUNG_SCOPE or GILL_SCOPE.
    /// </summary>
    public static float OrganBase(World w, int i)
    {
        float brain = K.COST_BRAIN * K.C_BASAL * w.PBrain[i] / K.BRAIN_REF * w.Dt;   // per step, as PBasal (World.Dt)
        return MathF.Max(1e-9f, (w.PBasal[i] - brain) * MathK.Pow(MathF.Max(w.MassOf(i), 1e-4f) / w.PM[i], K.EXP_BASAL) + brain);
    }

    /// <summary>Upkeep of the respiratory organs of a body with this lung share, x basal: COST_LUNG x lungs^ORGAN_EXP +
    /// COST_GILL x gills^ORGAN_EXP. A specialist pays for one organ; a body with both pays more (each has its overhead).</summary>
    public static float OrganUpkeep(float lungs)
        => K.COST_LUNG * MathK.Pow(lungs, K.ORGAN_EXP) + K.COST_GILL * MathK.Pow(1f - lungs, K.ORGAN_EXP);

    /// <summary>Oxygen debt that kills body i (its current mass): DEBT_MAX x basal at M = 1 x M.</summary>
    public static float DebtMax(World w, int i) => K.DEBT_MAX * K.C_BASAL * MathF.Max(w.MassOf(i), 1e-4f);

    /// <summary>The BREATH sense: its oxygen debt against the most it can bear (0..1).</summary>
    public static float Breathless(World w, int i) => MathF.Min(1f, w.O2Debt[i] / DebtMax(w, i));

    /// <summary>Where it breathes this step and what that gives: oxygen uptake (energy units of metabolism per step) by
    /// air, by water through gills, through the skin, and the oxygen of each medium (for the cost of breathing).</summary>
    public struct Breath
    {
        public float Air, Water, Skin, AAir, AWater;
        /// <summary>Energy per step of keeping its organs working here, beyond ventilation: the gills' osmoregulation
        /// in water (GILL_OSMO) and holding its depth against its lungs' buoyancy (BUOY_COST).</summary>
        public float Extra;
        /// <summary>Metres it swims down and up this step diving to its depth and back (2026-11-02): paid at swimming's cost
        /// of transport (in Extra), and water its gape sweeps (PlanktonKg).</summary>
        public float DiveM;
        public readonly float Uptake => Air + Water + Skin;
    }

    /// <summary>
    /// Where in the water column body i holds this step, and the oxygen it draws there (pure: writes nothing). Out of
    /// water, or airborne, it is at the surface (z 0, share 0). debt: its oxygen debt now (it pulls a swimmer up).
    /// In water, also what its organs cost there (Breath.Extra).
    /// </summary>
    public static Breath Uptake(World w, int i, int cell, bool airborne, float debt, out float z, out float share)
    {
        var b = UptakeAt(w, i, cell, airborne, debt, out z, out share);
        if (!airborne && (w.Cell[cell] & (byte)Realm.Water) != 0)
        {
            float basal = OrganBase(w, i) * Pace(w, i, cell, airborne), L = w.PLungs[i];   // the pumps work at its metabolic pace
            // the gills' surface leaks salts (or water) and ion pumps make up for it; air in the lungs pulls up, less
            // as it compresses with depth (Boyle: 1 atmosphere per 10 m), for the share of the step it is down
            b.Extra = basal * (K.GILL_OSMO * (1f - L) + K.BUOY_COST * L * share / (1f + z / 10f));
            // the dives: swum at swimming's cost of transport (C_MOVE x M^EXP_MOVE a unit of distance; 2026-11-02, before free)
            b.Extra += b.DiveM / K.M_PER_UNIT * K.C_MOVE * MathK.Pow(MathF.Max(w.MassOf(i), 1e-4f), K.EXP_MOVE) * (1f + K.INS_MOVE * w.PInsul[i]);
        }
        return b;
    }

    /// <summary>
    /// The capacity of body i's respiratory organs here against their full capacity: its metabolic pace (Pace) to the
    /// power AEROBIC_T. At 1, a cold or cold-blooded body has less aerobic machinery and cannot keep up sustained activity
    /// (Bennett &amp; Ruben 1979: the aerobic capacity model of endothermy). AEROBIC_T is 0 (not active) since 2026-11-01,
    /// so this is 1; its need and running costs follow Pace either way. 1 with no climate.
    /// </summary>
    public static float Aerobic(World w, int i, int cell, bool airborne)
    {
        if (!Climate.On || K.AEROBIC_T <= 0f) return 1f;   // not active (AEROBIC_T 0): organs at their full capacity
        return MathK.Pow(Pace(w, i, cell, airborne), K.AEROBIC_T);
    }

    /// <summary>How fast body i burns here against a warm-blooded body at T_BODY (Heat.Activity at the core its endothermy
    /// keeps in this cell): its resting oxygen need and the running costs of its organs follow it, whatever its organs'
    /// capacity does (Aerobic). 1 with no climate.</summary>
    public static float Pace(World w, int i, int cell, bool airborne)
    {
        if (!Climate.On) return 1f;
        bool immersed = !airborne && (w.Cell[cell] & (byte)Realm.Water) != 0;
        float e = w.PEndo[i];
        return Heat.Activity(Heat.Core(Heat.Operative(w, cell, w.PInsul[i], immersed), e), e);
    }

    /// <summary>
    /// The depth a swimmer heads for (2026-11-01): of the surface, its preferred depth (B_DIVE) and the bottom, the one
    /// where the food its gut digests is richest within its reach there (plankton by the light, algae and detritus on the
    /// bottom; Access), as it climbs toward food on land (CLIMB_FOOD); the preferred depth when none is richer. Its
    /// breath then limits how deep and how long it stays (Uptake).
    /// </summary>
    static float ForageDepth(World w, int i, int cell, float col)
    {
        float pref = w.PDive[i] * col;
        float best = pref, bv = FoodAt(w, i, cell, pref);
        float v0 = FoodAt(w, i, cell, 0f); if (v0 > bv) { bv = v0; best = 0f; }
        float vb = FoodAt(w, i, cell, col); if (vb > bv) best = col;
        return best;
    }

    /// <summary>The richest food body i digests within reach at depth z in a cell (diet x energy x stock x Access).</summary>
    static float FoodAt(World w, int i, int cell, float z)
    {
        var diet = w.DietOf(i);
        float v = 0f;
        for (int p = 0; p < Pools.NFIELD; p++)
            if (diet[p] > 0f) v = MathF.Max(v, diet[p] * Pools.Rho[p] * w.Pool[p][cell] * Access(w, cell, p, z, 1f, 0f));
        return v;
    }

    static Breath UptakeAt(World w, int i, int cell, bool airborne, float debt, out float z, out float share)
    {
        float m = MathF.Max(w.MassOf(i), 1e-4f), basal = OrganBase(w, i) * Aerobic(w, i, cell, airborne);
        float L = w.PLungs[i], Gi = 1f - L;
        // the skin is passive diffusion: it does not grow with what the body's tissues cost, only with its surface
        float skin = K.SKIN * K.C_BASAL * MathK.Pow(m, K.EXP_BASAL) * w.Dt / basal / MathK.Cbrt(m) * (1f - w.PInsul[i]);
        float aAir = AirO2(SurfaceM(w, cell), AirT(w, cell));
        var b = new Breath { AAir = aAir, AWater = 1f };
        z = 0f; share = 0f;
        if (airborne || (w.Cell[cell] & (byte)Realm.Water) == 0)
        {
            b.Air = basal * aAir * (K.LUNG_SCOPE * L + K.GILL_SCOPE * K.GILL_AIR * Gi);
            b.Skin = basal * aAir * skin;
            return b;
        }
        float col = ColumnM(w, cell);
        float aW0 = WaterO2(w, cell, 0f);
        float swim = 0.1f + 0.9f * Body.Propulsor(w.PFins[i], w.PWings[i]);   // fins and wings (2026-11-02)
        if (swim >= K.DEEP_SWIM)
        {
            // a swimmer: its preferred depth, pulled up as it runs out of breath
            float need = K.FIELD_METAB * OrganBase(w, i) * Pace(w, i, cell, airborne);   // what it burns (its capacity is basal x scope)
            float zt = ForageDepth(w, i, cell, col) * (1f - MathF.Min(1f, debt / DebtMax(w, i)));
            float us = basal * (aAir * K.LUNG_SCOPE * L + aW0 * (K.GILL_SCOPE * Gi + skin));
            if (zt <= 0f) { b.Air = basal * aAir * K.LUNG_SCOPE * L; b.Water = basal * aW0 * K.GILL_SCOPE * Gi; b.Skin = basal * aW0 * skin; b.AWater = aW0; return b; }
            float aZ = WaterO2(w, cell, zt), uz = basal * aZ * (K.GILL_SCOPE * Gi + skin);
            float f = 1f, tDive = 0f;
            if (uz < need)
            {
                // a breath-hold dive: no deeper than its store lasts the round trip at DIVE_V, and at depth only for the
                // share of the step that keeps its oxygen balanced; the rest at the surface
                float store = K.O2_STORE * K.C_BASAL * m;
                tDive = store / MathF.Max(need - uz, 1e-9f) * StepSeconds * w.Dt;   // seconds its store lasts a dive
                float zlim = K.DIVE_V * tDive * 0.5f;
                // at most half its store spent travelling: a dive to the limit would leave no time down there
                if (zt > 0.5f * zlim) { zt = 0.5f * zlim; aZ = WaterO2(w, cell, zt); uz = basal * aZ * (K.GILL_SCOPE * Gi + skin); }
                f = us > uz ? Math.Clamp((us - need) / (us - uz), 0f, 1f) : 0f;
                f *= zlim > 0f ? MathF.Max(0f, 1f - zt / zlim) : 0f;
            }
            z = f > 0f ? zt : 0f; share = f > 0f ? f : 0f;
            // down and up: once a step if it can stay down (it breathes there), else as many breath-hold dives as fill its
            // share of the step at depth, each lasting what its store lasts
            if (z > 0f) b.DiveM = 2f * z * (tDive > 0f ? MathF.Max(1f, share * StepSeconds * w.Dt / tDive) : 1f);
            b.Air = (1f - share) * basal * aAir * K.LUNG_SCOPE * L;
            b.Water = basal * K.GILL_SCOPE * Gi * ((1f - share) * aW0 + share * aZ);
            b.Skin = basal * skin * ((1f - share) * aW0 + share * aZ);
            b.AWater = (1f - share) * aW0 + share * aZ;
            return b;
        }
        // not a swimmer: on the bottom (its head in the air if the water is shallower than it stands) or paddling at the
        // surface with its head up for the share its fins and legs allow; whichever gives more oxygen
        float stand = StandM(w, i);
        float aB = WaterO2(w, cell, col);
        float headUp = col <= stand ? 1f : 0f;
        float ub = basal * (headUp * aAir * K.LUNG_SCOPE * L + aB * (K.GILL_SCOPE * Gi + skin));
        float up = MathF.Min(1f, (0.9f * Body.Propulsor(w.PFins[i], w.PWings[i]) + K.LEG_PADDLE * w.PLegs[i]) / K.DEEP_SWIM);   // fins and legs paddle; a limbless body cannot
        float usf = basal * (up * aAir * K.LUNG_SCOPE * L + aW0 * (K.GILL_SCOPE * Gi + skin));
        if (ub >= usf)
        {
            z = col; share = 1f;
            b.Air = basal * headUp * aAir * K.LUNG_SCOPE * L; b.Water = basal * aB * K.GILL_SCOPE * Gi; b.Skin = basal * aB * skin; b.AWater = aB;
        }
        else { b.Air = basal * up * aAir * K.LUNG_SCOPE * L; b.Water = basal * aW0 * K.GILL_SCOPE * Gi; b.Skin = basal * aW0 * skin; b.AWater = aW0; }
        return b;
    }

    /// <summary>RESOLVE, after it moved: its depth and time there this step (World.Z, ZShare), and its oxygen uptake.</summary>
    public static Breath Place(World w, int i, int cell, bool airborne)
    {
        var b = Uptake(w, i, cell, airborne, w.O2Debt[i], out float z, out float share);
        w.Z[i] = z; w.ZShare[i] = share;
        return b;
    }

    /// <summary>
    /// RESOLVE, with this step's costs: the energy it spends breathing (returned, charged to its reserve) and its oxygen
    /// debt, which grows by what its uptake falls short of its demand (metabolism and breathing) and is repaid from
    /// what it can draw beyond it.
    /// </summary>
    public static float Breathe(World w, int i, in Breath b, float demand)
    {
        float cost = Cost(b, demand);
        float net = b.Uptake - demand - cost;
        w.O2Debt[i] = net < 0f ? w.O2Debt[i] - net : MathF.Max(0f, w.O2Debt[i] - net);
        return cost;
    }

    /// <summary>What breathing costs at this demand: ventilating its media (AIR_VENT, GILL_VENT, more as they hold less
    /// oxygen), and its organs' osmoregulation and buoyancy here (Breath.Extra). Aerobic: it needs oxygen too.</summary>
    public static float Cost(in Breath b, float demand)
    {
        float u = b.Uptake;
        float vent = u > 1e-12f ? (b.Air * K.AIR_VENT / MathF.Max(b.AAir, 0.05f) + b.Water * K.GILL_VENT / MathF.Max(b.AWater, 0.05f)) / u : 0f;
        return demand * MathF.Min(0.5f, vent) + b.Extra;
    }

    /// <summary>
    /// The share of its top speed body i can still put out (2026-11-01): muscle power falls as its oxygen debt builds
    /// (lactate and acidosis: the fatigue of exercise beyond aerobic capacity; Hochachka &amp; Somero 2002), to 10% when it is
    /// about to suffocate. So an animal out of breath slows down and its debt stops growing, unless it cannot draw even
    /// its resting need. Before, it ran on at full speed until it died.
    /// </summary>
    public static float Stamina(World w, int i) => 1f - 0.9f * Breathless(w, i);

    /// <summary>How much faster than its skin alone body i dries out of water: its gills' wet surface evaporates (GILL_EVAP).</summary>
    public static float Drying(World w, int i) => 1f + K.GILL_EVAP * (1f - w.PLungs[i]);

    /// <summary>Can body i live here at rest: does it draw what a day of living needs (FIELD_METAB x basal), with
    /// breathing's cost, at the depth it would hold? (Founding: Life.RangeCapacity, Simulation.ProbeCapacity.)</summary>
    public static bool Breathable(World w, int i, int cell) => Breathable(w, i, cell, out _, out _, out _);

    /// <param name="cost">what breathing there costs a day of living (ventilation, osmoregulation, buoyancy), energy per step</param>
    public static bool Breathable(World w, int i, int cell, out float z, out float share, out float cost)
    {
        var b = Uptake(w, i, cell, false, 0f, out z, out share);
        float need = K.FIELD_METAB * OrganBase(w, i) * Pace(w, i, cell, false);
        cost = Cost(b, need);
        return b.Uptake >= need + cost;
    }

    /// <summary>How tall body i stands at its present size (m): STAND_H x M^(1/3) x (0.3 + 0.7 legs), the shoulder height
    /// of a body on full legs (Garcia &amp; da Silva 2006), its belly's height on none. Water shallower than this leaves its
    /// head and back in the air and its feet on the bottom: it stands and wades. Deeper, its trunk is under water, and a
    /// body about as dense as water (Schmidt-Nielsen 1984) floats off its feet: it swims or paddles (2026-11-02).</summary>
    public static float StandM(World w, int i) => K.STAND_H * MathK.Cbrt(MathF.Max(w.MassOf(i), 1e-4f)) * (0.3f + 0.7f * w.PLegs[i]);

    /// <summary>Its leg length (hip height, m): WADE_LEG of its standing height. The depth against which wading's cost is
    /// measured (World.WadeFactor).</summary>
    public static float LegM(World w, int i) => K.WADE_LEG * StandM(w, i);

    /// <summary>
    /// What share of a food pool in a water cell body i reaches at its depth: plankton by the light where it is
    /// (Beer-Lambert), attached algae and detritus on the bottom only. 1 on land and for every other pool. From the
    /// surface (the share of the step it is not at depth) it reaches the bottom only where the water is no deeper than
    /// it stands: a wader's head reaches its feet, a body floating at the surface reaches down about its own height
    /// (a dabbling duck up-ends to about its length). Before 2026-11-02 every body at the surface reached the bottom
    /// anywhere BOTTOM_BAND (2 m) deep, so a 0.3 kg wader grazed algae under 2 m of water it could not stand in.
    /// </summary>
    public static float Access(World w, int i, int cell, int pool)
        => Access(w, cell, pool, w.Z[i], w.ZShare[i], StandM(w, i));

    public static float Access(World w, int cell, int pool, float z, float share, float reachM = 0f)
    {
        if ((w.Cell[cell] & (byte)Realm.Water) == 0) return 1f;
        if (pool == (int)Pool.Phyto)
        {
            // the sea's plankton lies through its mixed layer, and below it by the light there (2026-11-02: K_LIGHT_SEA)
            if (w.Ocean) return (1f - share) + share * (z <= K.Z_MIX_SEA ? 1f : MathK.Exp(-K.K_LIGHT_SEA * (z - K.Z_MIX_SEA)));
            return (1f - share) + share * MathK.Exp(-K.K_LIGHT * z);
        }
        if (pool == (int)Pool.Algae || pool == (int)Pool.Detritus)
        {
            float col = ColumnM(w, cell);
            return (z >= col - K.BOTTOM_BAND ? share : 0f) + (col <= reachM ? 1f - share : 0f);
        }
        return 1f;
    }

    /// <summary>
    /// The plankton (kg) body i can take in a step in a water cell (2026-11-02): plankton is spread through the mixed
    /// layer at the concentration its stock over the cell's area and that depth makes, and a body takes what lies in the
    /// water its open mouth or filter sweeps, GAPE x its frontal area x the metres it swims (sweptM: its travel and its dives),
    /// at the depths it is at for their shares of the step (Access: the mixed layer, less below it). Ram filter feeding
    /// and the clearance rates of suspension feeders (Jorgensen 1966; Hansen, Bjornsen &amp; Hansen 1997). Never more than
    /// the stock it reaches. Before, a body anywhere in the water could take the whole column's plankton at one bite.
    /// </summary>
    public static float PlanktonKg(World w, int i, int cell, float sweptM, float z, float share)
    {
        if ((w.Cell[cell] & (byte)Realm.Water) == 0) return 0f;
        float col = ColumnM(w, cell), h = w.Ocean ? Ocean.MixedM(col) : MathF.Max(0.1f, MathF.Min(col, K.Z_MIX));
        float stock = w.Pool[(int)Pool.Phyto][cell] * Pools.Kg[(int)Pool.Phyto] * Access(w, cell, (int)Pool.Phyto, z, share);
        float side = K.BODY_L_M * MathK.Cbrt(MathF.Max(w.MassOf(i), 1e-6f));   // its frontal area is about side^2
        return MathF.Min(stock, stock / (Pools.CellM2 * h) * K.GAPE * side * side * MathF.Max(0f, sweptM));
    }

    /// <summary>
    /// The chance that a and b meet in the water column: each is at its depth for its share of the step and at the
    /// surface the rest (an animal out of water is at the surface); they meet when within STRIKE_DZ. 1 when neither is
    /// in water.
    /// </summary>
    public static float Overlap(World w, int a, int b)
    {
        float za = w.Z[a], sa = w.ZShare[a], zb = w.Z[b], sb = w.ZShare[b];
        if (sa <= 0f && sb <= 0f) return 1f;
        float p = 0f;
        if (MathF.Abs(za - zb) <= K.STRIKE_DZ) p += sa * sb;
        if (za <= K.STRIKE_DZ) p += sa * (1f - sb);
        if (zb <= K.STRIKE_DZ) p += (1f - sa) * sb;
        p += (1f - sa) * (1f - sb);
        return p;
    }
}
