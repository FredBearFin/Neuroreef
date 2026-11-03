// Shadow expression (docs/BODYPLAN.md §10, phase 2). Alongside Body.Express, every live body is translated to a
// module genome (Translate.cs), grown (Development.Grow) at its live mass, and its capabilities computed from that
// morphology by the prototype's physics (Physics.cs), in SI units: speed and cost per metre in each medium, flight,
// standing height, oxygen supply in air and water, protection, basal cost, drag shape and turning. They are written to
// World.D (Derived: per slot, never saved, recomputed from the genomes after a load) and read by nothing in the live
// simulation: the shadow is write-only, so the simulation is bit for bit what it was. ShadowDiag compares them with the
// live capabilities, converted to the same units.
// All maths through MathK.

using System;
using System.Collections.Generic;
using System.Text;

namespace Neuroreef.Morph;

/// <summary>Capabilities of a body in SI units, from either expression (shadow or live).</summary>
public struct Caps
{
    public float VLand, VWater, VAir;          // top (sprint) speed, m/s
    public float Cruise;                       // swimming speed of least cost per metre (shadow) or the live cruise (CruiseMps), m/s
    public float CotLand, CotWater, CotAir;    // net cost of transport, J/m (0 where it cannot move so)
    public float Margin, Flies;                // flight power margin (1 = level flight at the speed of least power), and 0/1
    public float Stand;                        // standing height, m: water shallower leaves its back in the air
    public float O2Air, O2Water;               // aerobic metabolism its exchange organs and skin support, W
    public float Odds;                         // protection: shadow, crushing force over the bite (PKill odds); live, (DEF_BASE + armour) M
    public float Basal;                        // basal cost, gut included (brain included), W
    public float Stream;                       // speed at a given power against a cube of its volume (drag^(-1/3))
    public float TurnR;                        // shadow: least turning radius in water at 1 m/s (m); live: turn rate (rad/step)
}

public static class Shadow
{
    /// <summary>Compute the shadow at every Body.Express (on by default; it changes nothing the simulation reads).</summary>
    public static bool Enabled = true;

    // ---- calibration of the shadow's evaluators (docs/BODYPLAN.md §10.3)
    /// <summary>Peak power of muscle in a brief sprint, W per kg of muscle: ~5x the sustained P_SPEC (Weis-Fogh &amp;
    /// Alexander 1977: ~250 W/kg; Askew &amp; Marsh 2002: 390 W/kg in quail flight muscle; Josephson 1993).</summary>
    public const float P_BURST = 250f;
    /// <summary>Elastic return of a running stride (phase 2b): the share of the limbs' mechanical work its tendons store and
    /// give back, R_TENDON x h / (h + H_TENDON) for hip height h: ~0.12 for a 0.1 kg runner (kangaroo rats' tendons are
    /// too thick to stretch much: Biewener &amp; Blickhan 1988), ~0.33 at 10 kg, ~0.45-0.5 in horses and kangaroos (Alexander
    /// 1988; Bennett &amp; Taylor 1995).</summary>
    public const float R_TENDON = 0.6f, H_TENDON = 0.25f;
    /// <summary>Burst swimming is limited by how fast the muscle can contract (Wardle 1975): at most one body wave of
    /// ~0.7 lengths a tail beat, a tail beat lasting at least twice the muscle's twitch time, 19 ms at 0.1 m and longer
    /// as L^0.45 (Wardle 1975: cod, saithe, mackerel; Videler 1993).</summary>
    public const float TWITCH_S = 0.019f, TWITCH_EXP = 0.45f, WAVE_PER_BEAT = 0.7f;
    public static float WardleSpeed(float length) => WAVE_PER_BEAT * length / (2f * TWITCH_S * MathK.Pow(MathF.Max(length, 1e-3f) / 0.1f, TWITCH_EXP));
    /// <summary>Joules per live energy unit (C_BASAL is Kleiber's BMR_W for a step of STEP_S).</summary>
    public const float J_PER_E = K.BMR_W * K.STEP_S / K.C_BASAL;

    static readonly float[] U = BuildSpeeds();
    static float[] BuildSpeeds()
    {
        var u = new float[480];
        for (int i = 0; i < u.Length; i++) u[i] = 0.002f * MathK.Exp(i * MathK.Log(60f / 0.002f) / (u.Length - 1));
        return u;
    }

    /// <summary>The live body's basal factor from the genes that stay genes (speed, sight, weapons, eyes, life
    /// history, immunity, water economy, voice, fur, radiators): Body.Express's basal factor less its organ terms
    /// (fins, wings, armour, lungs and gills), which the shadow takes from the morphology instead.</summary>
    public static float GeneBasalFactor(ReadOnlySpan<sbyte> gn)
    {
        float fs = Body.Factor(gn[G.B_SPEED], K.BODY_RANGE_TRAIT), fsi = Body.Factor(gn[G.B_SIGHT], K.BODY_RANGE_TRAIT);
        float strike = Body.Organ(gn[G.B_STRIKE]) * K.STRIKE_MAX;
        int eyePairs = Body.Pairs(gn[G.B_EYEN]);
        float life = Body.Factor(gn[G.B_LIFE], K.LIFE_RANGE);
        bool herm = gn[G.B_HERM] >= 0;
        return 1f + K.COST_SPEED * (fs - 1f) + K.COST_SIGHT * (fsi - 1f) + K.COST_STRIKE_ABS * strike
             + K.COST_EYE * eyePairs + K.COST_LONGEVITY * (life - 1f)
             + (herm ? K.COST_HERM : 0f) + K.COST_IMMUNE * Body.Organ(gn[G.B_IMMUNE]) + K.COST_WATERECON * Body.Organ(gn[G.B_WATER])
             + K.COST_VOICE * Body.Organ(gn[G.B_VOICE]) + K.COST_INS * Body.Organ(gn[G.B_INSUL]) + K.COST_APP * Body.Organ(gn[G.B_APPEND]);
    }

    /// <summary>The brain's basal cost (W): COST_BRAIN per unit, as Body.Express charges it.</summary>
    public static float BrainW(ReadOnlySpan<sbyte> gn) => K.COST_BRAIN * K.BMR_W * Body.Units(gn[G.B_BRAIN]) / K.BRAIN_REF;

    /// <summary>The shadow of a live body: translate, grow at its mass, compute. geneFactor and brainW as
    /// GeneBasalFactor and BrainW (1 and 0 for a bare body).</summary>
    public static Caps Of(in LiveGenes l, float geneFactor, float brainW) => Of(l, geneFactor, brainW, out _);

    public static Caps Of(in LiveGenes l, float geneFactor, float brainW, out Morphology m)
    {
        float level = Heat.Level(l.Endo);
        // its exchange surfaces are built to its own tissues' basal metabolism (symmorphosis, as the live organs are:
        // Respiration.OrganBase), so the body is first grown without them to find that metabolism
        var m0 = Development.Grow(Translate.FromLive(l, 0f), l.Mass);
        float organBase = BasalW(m0, level, geneFactor, 0f);
        m = Development.Grow(Translate.FromLive(l, organBase), l.Mass);
        return Evaluate(m, level, geneFactor, brainW);
    }

    /// <summary>Basal cost of a morphology (W), as the live sim builds it: Kleiber (BMR_W x M^0.75) on its active
    /// (non-mineral) mass at its metabolic level (Heat.Level), x the factor of the genes that stay genes; plus what
    /// its modules cost from their physics (integument turnover and moult, the exchange epithelia's upkeep) and the
    /// brain; then the gut built to that cost (Body.GutScale).</summary>
    public static float BasalW(Morphology m, float level, float geneFactor, float brainW)
    {
        float kleiber = K.BMR_W * MathK.Pow(MathF.Max(1e-6f, m.ActiveMass), K.EXP_BASAL) * level;
        float modules = Phys.TURNOVER * m.IntMass + Phys.MOULT_COST * m.Rigidity * m.ExoMass + Phys.EXCH_TISSUE * (m.ExchExt + m.ExchInt + m.ExchGas);
        Body.GutScale(kleiber * MathF.Max(0.5f, geneFactor) + modules + brainW, kleiber, out float basal);
        return basal;
    }

    public static Caps Evaluate(Morphology m, float level, float geneFactor, float brainW)
    {
        var c = new Caps();
        float k = P_BURST / Phys.P_SPEC;
        // ---- land (air, flat ground)
        var wp = Physics.Walk(m, Phys.RHO_A);
        float flex = Physics.Flex(m), axCap = 0f;
        foreach (var s in m.Segs) axCap += s.AxMuscle * Phys.P_SPEC * 0.5f;
        axCap *= flex;
        float bestCot = float.PositiveInfinity;
        foreach (float u in U)
        {
            if (LandMech(m, wp, u, k * axCap, k) <= 0f) c.VLand = u;
            float met = Physics.WalkMetabolic(m, wp, 0f, Phys.RHO_A, u, axCap, out _);
            if (!float.IsPositiveInfinity(met)) bestCot = MathF.Min(bestCot, met / u);
        }
        c.CotLand = float.IsPositiveInfinity(bestCot) ? 0f : bestCot;
        // ---- water: the top speed on burst power; the cruise where the cost a metre, basal included, is least (a
        // fish's optimum: Videler 1993), and its net cost a metre there (the live COT_SWIM_J is net)
        float basal = BasalW(m, level, geneFactor, brainW);
        // each folding of its pairs (Physics.SwimSet): a swimmer tucks what does not pay to stroke
        var sps = Physics.SwimSet(m, Phys.RHO_W);
        float bestGross = float.PositiveInfinity, vWardle = WardleSpeed(m.Length);
        foreach (float u in U)
        {
            float pBurst = float.PositiveInfinity, pCruise = float.PositiveInfinity;
            foreach (var sp in sps)
            {
                float pw = Physics.SwimPower(sp, Phys.RHO_W, u);
                if (pw <= k * sp.Cap) pBurst = MathF.Min(pBurst, pw);
                if (pw <= sp.Cap) pCruise = MathF.Min(pCruise, pw);
            }
            if (!float.IsPositiveInfinity(pBurst) && u <= vWardle) c.VWater = u;
            if (float.IsPositiveInfinity(pCruise)) continue;
            float gross = (pCruise / Phys.ETA_M + basal) / u;
            if (gross < bestGross) { bestGross = gross; c.Cruise = u; c.CotWater = pCruise / Phys.ETA_M / u; }
        }
        // ---- air: margin over the least power of level flight; the cost a metre at the speed of least power
        c.Margin = Physics.FlightMargin(m, out float pMin, out float vmp);
        float flightMuscle = 0f;
        // flight muscle: the muscle of pairs that make lift, by their share of a flat, horizontal blade (a leg's muscle is not a wing's)
        foreach (var a in m.Pairs) if (!a.Sagittal && a.Muscle > 0f && a.Artic > 0f) flightMuscle += 2f * a.Muscle * MathK.Cos(a.Theta) * (a.Chord / a.Thick) / (a.Chord / a.Thick + 2f);
        bool flies = c.Margin >= 1f && flightMuscle >= K.FLY_WING_MIN * K.FLY_MUSCLE_MAX * m.Mass;   // Marden (1987): ~16% flight muscle
        c.Flies = flies ? 1f : 0f;
        if (flies)
        {
            c.VAir = vmp * Body.TopFlightSpeed(c.Margin);
            c.CotAir = pMin / Phys.ETA_M / vmp;
        }
        // ---- standing, breathing, protection, basal, drag shape, turning
        c.Stand = wp.Clear + m.MaxH;
        c.O2Air = Physics.O2Supply(m, Medium.Air, out _);
        c.O2Water = Physics.O2Supply(m, Medium.Water, out _);
        float bite = 10f * MathK.Pow(m.Mass / Development.M_REF, 2f / 3f);   // MorphEnv.PredBite, scaled in the size spectrum
        float pk = Physics.PKill(m, bite);
        c.Odds = pk > 0f ? 1f / pk - 1f : 1e6f;
        c.Basal = basal;
        c.Stream = Streamlining(m);
        c.TurnR = Physics.TurnRadius(m, Phys.RHO_W, 1f);
        return c;
    }

    /// <summary>Mechanical power short of a sprint at speed u on flat ground (negative or 0: it can), the better of
    /// crawling and walking, with muscle at k x its sustained power (P_BURST). The same terms as Physics.WalkMetabolic.</summary>
    internal static float LandMech(Morphology m, in Physics.WalkProps w, float u, float axBurst, float k)
    {
        float crawlW = MathF.Max(w.WEff, Phys.CRAWL_PRESS * m.Mass * Phys.G);
        float wave = MathF.Max(Phys.CRAWL_WAVE * m.WaveLength, 1e-3f);
        float crawlSup = Phys.KT * crawlW * u / wave;
        float kin = Physics.CrawlKinetic(m, u);
        float crawl = Phys.MU_SLIP * crawlW * u + kin + crawlSup * Phys.ETA_M - (axBurst + k * w.Cap);
        float best = crawl;
        if (w.Stride > 1e-3f && w.Cap > 0f)
        {
            float sup = Phys.KT * w.WEff * w.Support * u / w.Stride;
            float swing = Physics.SwingPower(w, u);
            float rest = 1f - w.Support;
            float elastic = 1f - R_TENDON * w.Stride / (w.Stride + H_TENDON);   // tendons return part of the limbs' work
            float legMech = (swing + sup * Phys.ETA_M) * elastic + rest * (Phys.MU_SLIP * crawlW * u + kin + crawlSup * Phys.ETA_M);
            best = MathF.Min(best, legMech - (k * w.Cap + axBurst * rest));
        }
        return best;
    }

    /// <summary>Speed at a given power against a cube of the same trunk volume: (drag area of a cube / its own)^(1/3),
    /// the live Body.Streamlining's measure, from the prototype's wetted-area drag (Hoerner 1965).</summary>
    public static float Streamlining(Morphology m)
    {
        float v = 0f; foreach (var s in m.Segs) v += s.L * s.W * s.H * (MathF.PI / 4f);
        float d = MathK.Cbrt(v / (MathF.PI / 4f));
        float cube = Physics.TrunkDrag(1.5f * MathF.PI * d * d, Physics.FormFactor(1f), MathF.PI / 4f * d * d, 0f, Phys.CF);
        return MathK.Cbrt(cube / MathF.Max(Physics.BodyDragArea(m), 1e-12f));
    }

    // ------------------------------------------------------------------ the live side, in the same units

    /// <summary>The live capabilities of slot i (an adult of its genes), converted to SI units from what Body.Express
    /// wrote. Speeds: a step's travel (PVLand, PVWater, PVAir) is a day range, not a speed; the live simulation reads it
    /// as a speed in metres a second through Garland's top running speed for the mass, x its speed against the plain
    /// body's (Body.ReachM), and so does this.</summary>
    public static Caps Live(World w, int i)
    {
        var gn = w.Genome.Slot(i);
        float m = w.PM[i], dt = w.Dt;
        var c = new Caps();
        float v0Ref = K.C_SPEED * MathK.Pow(m, K.EXP_SPEED) * dt, sprint = Body.SprintMps(m);
        c.VLand = sprint * w.PVLand[i] / v0Ref;
        c.VWater = sprint * w.PVWater[i] / v0Ref;
        c.VAir = sprint * w.PVAir[i] / v0Ref;
        float prop = Body.Propulsor(w.PFins[i], w.PWings[i]);
        c.Cruise = K.CRUISE_BL * w.PLen[i] * (0.1f + 0.9f * prop);
        float rel = 0.5f;   // walking at half its top speed (Resolve.MoveCost: the swing share grows with speed)
        c.CotLand = w.PVLand[i] > 0f ? K.COT_J * MathK.Pow(m, K.EXP_MOVE) * (1f - K.LEG_SWING + K.LEG_SWING * w.PLegs[i] * w.PLegPairs[i] / K.LEG_PAIRS_REF * rel) : 0f;
        float eta = K.ETA_PADDLE + (K.ETA_LIFT - K.ETA_PADDLE) * prop;
        c.CotWater = w.PVWater[i] > 0f ? K.COT_SWIM_J * (K.ETA_LIFT / eta) * MathK.Pow(m, K.EXP_MOVE) : 0f;
        c.Margin = Body.FlightMargin(w.PWings[i], m);
        bool flies = w.PLoco[i] == (byte)Loco.Fly;
        c.Flies = flies ? 1f : 0f;
        if (flies)
        {
            // per unit distance at the speed of least power (Resolve.MoveCost, duty = distance / FlightFullStep)
            float net = Body.FlightPowerMin(m) - K.C_BASAL * MathK.Pow(m, K.EXP_BASAL);
            c.CotAir = net / Body.FlightFullStep(m) * J_PER_E / K.M_PER_UNIT;
        }
        c.Stand = K.STAND_H * MathK.Cbrt(m) * (0.3f + 0.7f * w.PLegs[i]);
        float toW = K.BMR_W / K.C_BASAL / dt;
        float brain = K.COST_BRAIN * K.C_BASAL * w.PBrain[i] / K.BRAIN_REF * dt;
        float organ = (w.PBasal[i] - brain) * toW;
        float skin = K.SKIN * K.C_BASAL * MathK.Pow(m, K.EXP_BASAL) / MathK.Cbrt(m) * (1f - w.PInsul[i]) * K.BMR_W / K.C_BASAL;
        float L = w.PLungs[i];
        c.O2Air = organ * (K.LUNG_SCOPE * L + K.GILL_SCOPE * K.GILL_AIR * (1f - L)) + skin;
        c.O2Water = organ * K.GILL_SCOPE * (1f - L) + skin;
        c.Odds = (K.DEF_BASE + w.PArmour[i]) * m;
        c.Basal = w.PBasal[i] * toW;
        Body.Dimensions(gn[G.B_LENGTH], gn[G.B_WIDTH], gn[G.B_HEIGHT], out float ls, out float ws, out float hs);
        c.Stream = Body.Streamlining(ls, ws * hs);
        c.TurnR = w.PTurn[i];
        return c;
    }

    /// <summary>Body.Express's hook: the shadow of slot i, written to World.D's M* arrays (own slot only).</summary>
    public static void Express(World w, int i)
    {
        if (!Enabled) return;
        var gn = w.Genome.Slot(i);
        var l = LiveGenes.Of(gn, w.PM[i]);
        var c = Of(l, GeneBasalFactor(gn), BrainW(gn));
        var d = w.D;
        d.MVLand[i] = c.VLand; d.MVWater[i] = c.VWater; d.MVAir[i] = c.VAir; d.MCruise[i] = c.Cruise;
        d.MCotLand[i] = c.CotLand; d.MCotWater[i] = c.CotWater; d.MCotAir[i] = c.CotAir;
        d.MMargin[i] = c.Margin; d.MFlies[i] = (byte)(c.Flies > 0f ? 1 : 0); d.MStand[i] = c.Stand;
        d.MO2Air[i] = c.O2Air; d.MO2Water[i] = c.O2Water; d.MOdds[i] = c.Odds; d.MBasal[i] = c.Basal;
        d.MStream[i] = c.Stream; d.MTurnR[i] = c.TurnR;
    }

    /// <summary>The shadow of slot i read back from World.D.</summary>
    public static Caps Stored(World w, int i)
    {
        var d = w.D;
        return new Caps
        {
            VLand = d.MVLand[i], VWater = d.MVWater[i], VAir = d.MVAir[i], Cruise = d.MCruise[i],
            CotLand = d.MCotLand[i], CotWater = d.MCotWater[i], CotAir = d.MCotAir[i], Margin = d.MMargin[i], Flies = d.MFlies[i],
            Stand = d.MStand[i], O2Air = d.MO2Air[i], O2Water = d.MO2Water[i], Odds = d.MOdds[i], Basal = d.MBasal[i],
            Stream = d.MStream[i], TurnR = d.MTurnR[i],
        };
    }
}

/// <summary>Shadow against live, per capability, over the living animals of worlds (the founders, when no step has
/// run): the median of the ratio shadow / live and its spread (10th and 90th percentiles).</summary>
public static class ShadowDiag
{
    public sealed class Row
    {
        public string Name = "";
        public bool Grounded;               // the live value is physically grounded (literature), so it is a calibration target
        public readonly List<float> Ratios = new();
        public float Median => Q(0.5f);
        public float P10 => Q(0.1f);
        public float P90 => Q(0.9f);
        public float Q(float q)
        {
            if (Ratios.Count == 0) return float.NaN;
            var a = Ratios.ToArray(); Array.Sort(a);
            return a[Math.Clamp((int)MathF.Round(q * (a.Length - 1)), 0, a.Length - 1)];
        }
        public int Agree, Total;            // flight: how many of how many agree on whether it flies
        public float AgreeShare => Total > 0 ? (float)Agree / Total : float.NaN;
    }

    public static readonly string[] Names =
    {
        "land speed", "water speed", "air speed", "water cruise speed", "land cost/m", "water cost/m", "air cost/m",
        "flight margin", "flies", "standing height", "O2 supply in air", "O2 supply in water", "protection gain",
        "basal cost", "drag shape", "turning (rel.)", "land speed (rel.)", "water speed (rel.)",
        "land cost/m, legs>=0.7", "water cost/m, fins>=0.7", "water cruise, fins>=0.7", "water cost/m, fins>=0.7, fish shape",
        "O2 scope in air", "O2 scope in water",
    };
    /// <summary>Which live values are physically grounded (docs/BODYPLAN.md §10.2).</summary>
    static readonly bool[] Grounded = { true, false, false, false, true, true, true, true, true, true, true, true, false, true, true, false, true, false, true, true, true, true, true, true };

    public static Row[] NewRows()
    {
        var r = new Row[Names.Length];
        for (int k = 0; k < r.Length; k++) r[k] = new Row { Name = Names[k], Grounded = Grounded[k] };
        return r;
    }

    static void Add(Row r, float shadow, float live) { if (live > 0f && shadow > 0f && !float.IsInfinity(shadow)) r.Ratios.Add(shadow / live); }

    /// <summary>Adds every living animal of w (limit: at most this many) to the rows.</summary>
    public static void Collect(World w, Row[] rows, int limit = int.MaxValue)
    {
        int n = 0;
        for (int i = 0; i < w.Cap && n < limit; i++)
        {
            if (w.StageOf[i] == St.Dead) continue;
            n++;
            var gn = w.Genome.Slot(i);
            var s = Shadow.Stored(w, i);
            var l = Shadow.Live(w, i);
            var lg = LiveGenes.Of(gn, w.PM[i]);
            Add(rows[0], s.VLand, l.VLand); Add(rows[1], s.VWater, l.VWater); Add(rows[2], s.VAir, l.VAir);
            Add(rows[3], s.Cruise, l.Cruise);
            Add(rows[4], s.CotLand, l.CotLand); Add(rows[5], s.CotWater, l.CotWater); Add(rows[6], s.CotAir, l.CotAir);
            Add(rows[7], s.Margin, l.Margin);
            rows[8].Total++; if ((s.Flies > 0f) == (l.Flies > 0f)) rows[8].Agree++;
            Add(rows[9], s.Stand, l.Stand); Add(rows[10], s.O2Air, l.O2Air); Add(rows[11], s.O2Water, l.O2Water);
            // protection: the gain its armour gives over the same body unarmoured, in each expression
            var bare = lg; bare.Armour = 0f;
            var sb = Shadow.Of(bare, 1f, 0f);
            Add(rows[12], s.Odds / MathF.Max(sb.Odds, 1e-9f), (K.DEF_BASE + w.PArmour[i]) / K.DEF_BASE);
            Add(rows[13], s.Basal, l.Basal); Add(rows[14], s.Stream, l.Stream);
            // turning and speeds against the plain body of the same mass (Translate.Reference), in each expression
            var sr = Shadow.Of(Translate.Reference(w.PM[i]), 1f, 0f);
            float liveTurnRef = K.C_TURN * MathK.Pow(w.PM[i], K.EXP_TURN);
            Add(rows[15], sr.TurnR / MathF.Max(s.TurnR, 1e-9f), l.TurnR / liveTurnRef);
            float v0 = K.C_SPEED * MathK.Pow(w.PM[i], K.EXP_SPEED) * w.Dt * Body.LandLoadFactor(w.PM[i], 0f, 2);
            Add(rows[16], s.VLand / MathF.Max(sr.VLand, 1e-9f), w.PVLand[i] / v0);
            Add(rows[17], s.VWater / MathF.Max(sr.VLand, 1e-9f), w.PVWater[i] / v0);
            // the bodies the live calibrations were made for: walkers on full legs, swimmers on full fins
            if (lg.Legs >= 0.7f) Add(rows[18], s.CotLand, l.CotLand);
            if (lg.Fins >= 0.7f) { Add(rows[19], s.CotWater, l.CotWater); Add(rows[20], s.Cruise, l.Cruise); }
            // the body COT_SWIM_J describes is a fish (Videler & Nolet 1990), at a fish's fineness. The live cost a metre
            // does not depend on shape; the physics does (a cube has ~2-3x a fineness-4 body's drag; Hoerner 1965), and
            // the founders are bluff (fineness < 1.5 in ~90% of them). So: the same genes at fineness 4 (length share 2.5,
            // the least drag for the volume; Webb 1975), against the same live value
            if (lg.Fins >= 0.7f)
            {
                var fish = lg; fish.LengthShare = 2.5f; fish.WidthShare = fish.HeightShare = MathK.Sqrt(1f / 2.5f);
                Add(rows[21], Shadow.Of(fish, Shadow.GeneBasalFactor(gn), Shadow.BrainW(gn)).CotWater, l.CotWater);
            }
            // aerobic scope: each expression builds its organs to its own basal (symmorphosis; Respiration.OrganBase), and
            // the shadow's basal is ~0.75 of the live one (the live organ-upkeep terms, docs/BODYPLAN.md §10.4), so the
            // absolute supply inherits that gap. The grounded quantity is the scope: ~10 in air for full lungs (Weibel &
            // Hoppeler 2005), 2-5 in water for gills (Killen et al. 2016)
            if (s.Basal > 0f && l.Basal > 0f) { Add(rows[22], s.O2Air / s.Basal, l.O2Air / l.Basal); Add(rows[23], s.O2Water / s.Basal, l.O2Water / l.Basal); }
        }
    }

    /// <summary>Telemetry of both expressions (phase 2b, docs/BODYPLAN.md §11): per living clade, the mean live and
    /// shadow values of the main capabilities in SI units (speeds m/s, costs J/m, basal and O2 W), in one pass over the
    /// slots. Read-only: it reads World.D's shadow arrays and the live per-slot values, and writes nothing.</summary>
    public static string SpeciesReport(World w)
    {
        const int NV = 9;
        var n = new int[w.NS]; var mass = new double[w.NS];
        var L = new double[w.NS, NV]; var S = new double[w.NS, NV];
        Span<float> lv = stackalloc float[NV], sv = stackalloc float[NV];
        for (int i = 0; i < w.Cap; i++)
        {
            if (w.StageOf[i] == St.Dead) continue;
            int sp = w.Spc[i];
            if (sp < 0 || sp >= w.NS) continue;
            var l = Shadow.Live(w, i); var s = Shadow.Stored(w, i);
            n[sp]++; mass[sp] += w.PM[i];
            lv[0] = l.VLand; lv[1] = l.VWater; lv[2] = l.VAir; lv[3] = l.CotLand; lv[4] = l.CotWater; lv[5] = l.CotAir; lv[6] = l.Basal; lv[7] = l.O2Air; lv[8] = l.Flies;
            sv[0] = s.VLand; sv[1] = s.VWater; sv[2] = s.VAir; sv[3] = s.CotLand; sv[4] = s.CotWater; sv[5] = s.CotAir; sv[6] = s.Basal; sv[7] = s.O2Air; sv[8] = s.Flies;
            for (int k = 0; k < NV; k++) { L[sp, k] += lv[k]; S[sp, k] += sv[k]; }
        }
        var sb = new StringBuilder();
        sb.AppendLine("BODY PLAN SHADOW (docs/BODYPLAN.md §10-11): per clade, live / shadow (module physics), means over the living; write-only, nothing reads it");
        sb.AppendLine("clade             n   mass kg  land m/s     water m/s    air m/s      land J/m       water J/m      air J/m      basal W        O2 air W       flies %");
        for (int sp = 0; sp < w.NS; sp++)
        {
            if (n[sp] == 0) continue;
            double k = 1.0 / n[sp];
            string P(int v, string f) => (L[sp, v] * k).ToString(f, System.Globalization.CultureInfo.InvariantCulture) + "/" + (S[sp, v] * k).ToString(f, System.Globalization.CultureInfo.InvariantCulture);
            sb.AppendLine($"{w.Sp[sp].Name,-12} {n[sp],6} {mass[sp] * k,8:F3}  {P(0, "F2"),-12} {P(1, "F2"),-12} {P(2, "F1"),-12} {P(3, "F1"),-14} {P(4, "F1"),-14} {P(5, "F2"),-12} {P(6, "F2"),-14} {P(7, "F1"),-14} {100 * L[sp, 8] * k:F0}/{100 * S[sp, 8] * k:F0}");
        }
        return sb.ToString();
    }

    public static string Format(Row[] rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("capability            grounded   n     median  p10     p90");
        foreach (var r in rows)
        {
            if (r.Name == "flies") { sb.AppendLine($"{r.Name,-22}{(r.Grounded ? "yes" : "no"),-9}{r.Total,5}  agree {r.AgreeShare:P0}"); continue; }
            sb.AppendLine($"{r.Name,-22}{(r.Grounded ? "yes" : "no"),-9}{r.Ratios.Count,5}  {r.Median,6:F2}  {r.P10,6:F2}  {r.P90,6:F2}");
        }
        return sb.ToString();
    }
}
