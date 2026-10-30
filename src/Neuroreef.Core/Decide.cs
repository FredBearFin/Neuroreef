// DECIDE phase (DESIGN §11, phase 2). Parallel over slots. For each acting animal: apply the learning
// update due from last step's reward, perceive (§4), compute each primitive's vector (§5), run the
// brain (§7), add exploration, and write intents. Each call writes only its own slot (NR-105).

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Neuroreef;

public static class Decide
{
    /// <summary>Probe output for one selected animal (written by the worker that owns that slot).</summary>
    public sealed class ProbeBuf
    {
        public volatile int Slot = -1;
        public readonly float[] X = new float[F.N], Hid = new float[G.NH], Z = new float[P.N], Gn = new float[P.N];
        public uint Acts; public float Reward, RewardMean;
    }

    public sealed class DecisionMetrics
    {
        public readonly long[] Attempts = new long[P.N], Eligible = new long[P.N], Effective = new long[P.N];
        public long Decisions, CapHits, O1Changed, O2Changed, MateChanged, StrikeChanged,
            ScanActive, ScanBlockedEats, SprintRequested, SprintMoved;

        internal void Attempt(int p) => Interlocked.Increment(ref Attempts[p]);
        internal void Allow(int p) => Interlocked.Increment(ref Eligible[p]);
        internal void Apply(int p) => Interlocked.Increment(ref Effective[p]);
        internal void Add(ref long n) => Interlocked.Increment(ref n);
    }

    public static DecisionMetrics? Metrics { get; set; }

    struct Near { public float D, Dx, Dy; public int J; }

    // Hash-cell offsets of the 5x5 neighbourhood, nearest first (own cell, ring 1, ring 2).
    static readonly (int dx, int dy)[] Ring = BuildRing();
    static (int, int)[] BuildRing()
    {
        var l = new System.Collections.Generic.List<(int, int)>();
        for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++) l.Add((dx, dy));
        l.Sort((a, b) => Math.Max(Math.Abs(a.Item1), Math.Abs(a.Item2)).CompareTo(Math.Max(Math.Abs(b.Item1), Math.Abs(b.Item2))) is int c && c != 0
            ? c : (a.Item1 * a.Item1 + a.Item2 * a.Item2).CompareTo(b.Item1 * b.Item1 + b.Item2 * b.Item2));
        return l.ToArray();
    }

    /// <summary>
    /// What eating here is worth to this animal (0..1): the energy of one bite of the best food in the cell it can
    /// digest, against what it burns per step (FIELD_METAB x basal), as 1 - exp(-bite / need). A bite that pays for a
    /// step reads 0.63. (Before 2026-10-30 it was measured against the best food the diet could ever give, so a
    /// nectar eater in a meadow sensed almost nothing, its eating instinct hardly fired, and founders starved on food.)
    /// </summary>
    public static float FoodValue(World w, ReadOnlySpan<float> diet, float bite, float need, int cell)
    {
        float v = 0;
        for (int p = 0; p < Pools.NFIELD; p++)
        {
            float e = diet[p];
            if (e <= 0) continue;
            v = MathF.Max(v, e * Pools.Rho[p] * MathF.Min(bite, w.Pool[p][cell]));
        }
        return Worth(v, need);
    }

    /// <summary>The worth of an energy gain against a step's need: 1 - exp(-gain / need).</summary>
    public static float Worth(float gain, float need) => 1f - MathK.Exp(-gain / need);

    static int CellAt(int c, int dx, int dy)
        => World.WrapI(c / Map.FW + dy, Map.FW) * Map.FW + World.WrapI(c % Map.FW + dx, Map.FW);

    public static void Run(World w, int i, ProbeBuf probe)
    {
        byte stg = w.StageOf[i];
        if (stg < St.Juvenile) return;
        var metrics = Metrics;
        if (metrics != null) metrics.Add(ref metrics.Decisions);
        var diet = w.DietOf(i);
        uint seed = w.Seed, step = (uint)w.Step;
        var rs = new Rng.Seq(Rng.Hash(seed, step, (uint)i, Rs.Explore));

        float x = w.X[i], y = w.Y[i], hd = w.Heading[i];
        float fx = MathK.Cos(hd), fy = MathK.Sin(hd);
        float myMass = w.ApparentMass(i);
        bool scanning = w.IScanning[i] != 0;
        float R = MathF.Min(K.MAX_SIGHT, w.PSight[i] * (scanning ? 1.3f : 1f));   // the neighbour search reaches MAX_SIGHT, no further
        float R2 = R * R;
        float fovCos = scanning ? -2f : w.PFovCos[i];
        int cell = World.FIdx(x, y);
        float vmax = w.Vmax(i, cell);
        bool ready = Ready(w, i);
        int mySpc = w.Spc[i];
        bool myJuv = stg == St.Juvenile;

        // ---- perception of other organisms (spatial hash, radius 1 or 2 cells). Kin are others that look like
        // this organism; every other organism is sensed through raw quantities, with no designed category
        float kinSum = 0, kinCx = 0, kinCy = 0, kinHx = 0, kinHy = 0, sepX = 0, sepY = 0, crowd = 0, otherSum = 0, dose = 0, srcShed = 0;
        Near kin = new() { D = 1e9f, J = -1 }, o1 = new() { D = 1e9f, J = -1 }, o2 = new() { D = 1e9f, J = -1 },
             mate = new() { D = 1e9f, J = -1 }, strike = new() { D = 1e9f, J = -1 }, strikeBig = new() { D = 1e9f, J = -1 },
             strikeKin = new() { D = 1e9f, J = -1 };
        float o2Mass = -1f, strikeBigMass = -1f;
        float reach = w.PReach[i];
        int hcx = (int)(x / K.HCELL), hcy = (int)(y / K.HCELL);
        int nOff = R > K.HCELL ? 25 : 9;
        Span<int> neighbours = stackalloc int[K.MAX_NEIGH];
        Span<uint> priorities = stackalloc uint[K.MAX_NEIGH];
        int seen = SelectNeighbours(w, i, step, hcx, hcy, nOff, neighbours, priorities, out int candidates);
        int infNear = 0, strainSrc = -1;
        for (int q = 0; q < seen; q++)
        {
            int j = neighbours[q];
            float ddx = World.Delta1(w.X[j] - x), ddy = World.Delta1(w.Y[j] - y);
            float d2 = ddx * ddx + ddy * ddy;
            if (d2 > R2 || d2 < 1e-8f) continue;
            float d = MathF.Sqrt(d2);
            if (d > reach && (ddx * fx + ddy * fy) / d < fovCos) continue;
            float close = 1f - d / R;
            crowd += close;
            byte sj = w.StageOf[j];
            bool active = sj >= St.Juvenile;
            bool alike = Body.LooksAlike(w, i, j);
            // contact route (Revision R): any animal in reach that sheds, of any kind; whether its strain can grow here is the strain's host range
            float shedJ = w.PgShed[j];
            if (active && shedJ > 0f && d <= reach + w.PReach[j]) { infNear++; dose += shedJ; if (shedJ > srcShed) { srcShed = shedJ; strainSrc = j; } }
            if (ready && active && d < mate.D && ValidPartner(w, i, j)) mate = new Near { D = d, Dx = ddx, Dy = ddy, J = j };
            if (alike)
            {
                if (d <= reach + 0.5f + 0.4f * MathK.Cbrt(w.ApparentMass(j)) && d < strikeKin.D) strikeKin = new Near { D = d, Dx = ddx, Dy = ddy, J = j };
                if (!active) continue;
                kinSum += close; kinCx += ddx; kinCy += ddy;
                kinHx += MathK.Cos(w.Heading[j]); kinHy += MathK.Sin(w.Heading[j]);
                if (d < 2f) { sepX -= ddx / d2; sepY -= ddy / d2; }
                if (d < kin.D) kin = new Near { D = d, Dx = ddx, Dy = ddy, J = j };
                continue;
            }
            otherSum += close;
            float mj = w.ApparentMass(j);
            if (d < o1.D) o1 = new Near { D = d, Dx = ddx, Dy = ddy, J = j };
            if (mj > o2Mass) { o2Mass = mj; o2 = new Near { D = d, Dx = ddx, Dy = ddy, J = j }; }
            float reachJ = reach + 0.5f + 0.4f * MathK.Cbrt(mj);
            if (d <= reachJ && d < strike.D) strike = new Near { D = d, Dx = ddx, Dy = ddy, J = j };
            if (d <= reachJ && mj > strikeBigMass) { strikeBigMass = mj; strikeBig = new Near { D = d, Dx = ddx, Dy = ddy, J = j }; }
        }
        if (metrics != null)
        {
            if (candidates > K.MAX_NEIGH)
                CompareUncappedTargets(w, i, x, y, fx, fy, R, R2, fovCos, ready, reach, nOff,
                    o1, o2, mate, strike, strikeBig, strikeKin, metrics);
        }
        // ---- features (DESIGN §4)
        Span<float> f = stackalloc float[F.N];
        float emax = w.EmaxOf(i);
        f[F.BIAS] = 1f;
        f[F.HUNGER] = Math.Clamp(1f - w.Energy[i] / emax, 0f, 1f);
        f[F.THIRST] = w.Thirst[i];
        f[F.FATIGUE] = w.Fatigue[i];
        f[F.READY] = ready ? 1f : 0f;
        f[F.AGE] = MathF.Min(1f, w.Age[i] / w.PLife[i]);
        f[F.JUVENILE] = myJuv ? 1f : 0f;
        f[F.FULL] = Math.Clamp(w.Gut[i] / (w.PGut[i] * w.MassOf(i) / w.PM[i]), 0f, 1f);
        f[F.PAIN] = w.Pain[i];
        f[F.CARRYING] = (w.Flags[i] & Flag.Carrying) != 0 ? 1f : 0f;
        f[F.KIN_DENSITY] = MathK.Tanh(kinSum);
        f[F.KIN_CLOSE] = kin.J >= 0 ? 1f - kin.D / R : 0f;
        int mom = myJuv ? w.MotherOf(i) : -1;
        float mdx = 0, mdy = 0;
        if (mom >= 0)
        {
            mdx = World.Delta1(w.X[mom] - x); mdy = World.Delta1(w.Y[mom] - y);
            float md = MathF.Sqrt(mdx * mdx + mdy * mdy);
            f[F.MOTHER_FAR] = MathF.Min(1f, md / R);
            // it knows her by sight, sound and smell, all within MAX_SIGHT; further off it has lost her and knows no way back
            if (md > K.MAX_SIGHT) { f[F.MOTHER_FAR] = 1f; mdx = mdy = 0f; }
        }
        f[F.MATE_CLOSE] = mate.J >= 0 ? 1f - mate.D / R : 0f;
        if (o1.J >= 0) Other(w, i, o1, R, vmax, fx, fy, myMass, f, F.O1_CLOSE);
        if (o2.J >= 0) Other(w, i, o2, R, vmax, fx, fy, myMass, f, F.O2_CLOSE);
        if (o1.J >= 0) f[F.O1_WEAK] = Weakness(w, o1.J);
        f[F.OTHERS] = MathK.Tanh(otherSum);

        // hunger is diet-neutral (Revision K): the food an organism senses is whatever its gut digests, plants on the
        // ground or another organism nearby (worth its tissue at this organism's meat efficiency)
        float meatEff = diet[(int)Pool.Meat];
        float mfp = MathK.Pow(w.MassOf(i) / w.PM[i], K.EXP_BASAL);
        float bite = w.PBite[i] * mfp, need = MathF.Max(1e-7f, K.FIELD_METAB * w.PBasal[i] * mfp);
        float fh = FoodValue(w, diet, bite, need, cell);
        float fgx = FoodValue(w, diet, bite, need, CellAt(cell, 2, 0)) - FoodValue(w, diet, bite, need, CellAt(cell, -2, 0));
        float fgy = FoodValue(w, diet, bite, need, CellAt(cell, 0, 2)) - FoodValue(w, diet, bite, need, CellAt(cell, 0, -2));
        byte eatPrey = 0;
        if (o1.J >= 0 && meatEff > 0)
        {
            // value of eating it: a bite of its edible tissue, on the same scale as the plants
            float prey = Worth(meatEff * K.RHO_MEAT * MathF.Min(bite, K.C_CARC * w.ApparentMass(o1.J)), need);
            float close = 1f - o1.D / R;
            fgx += prey * close * o1.Dx / MathF.Max(o1.D, 1e-3f); fgy += prey * close * o1.Dy / MathF.Max(o1.D, 1e-3f);
            if (o1.J == strike.J && prey > fh) { fh = prey; eatPrey = 1; }                         // in reach and worth more than the plants
        }
        w.IEatPrey[i] = eatPrey;
        f[F.FOOD_HERE] = fh;
        f[F.FOOD_GRAD] = MathK.Tanh(MathF.Sqrt(fgx * fgx + fgy * fgy) * 3f);
        bool inWater = (w.Cell[cell] & (byte)Realm.Water) != 0;
        f[F.IN_WATER] = inWater ? 1f : 0f;
        f[F.DEPTH] = Math.Clamp(w.Depth[cell] / (K.SHALLOW * (1f + K.SWIM_DEPTH_RANGE)), 0f, 1f);
        f[F.WATER_CLOSE] = MathF.Max(0f, 1f - w.DistDrink[cell] / K.SENSE_WATER);   // drinkable water: lakes and full ponds
        f[F.LAND_CLOSE] = MathF.Max(0f, 1f - w.DistLand[cell] / K.SENSE_WATER);
        // path integration (Revision P): the remembered way back to where it last drank and where it last fed well
        byte has = w.MemHas[i];
        PathMemory.Senses((has & Mem.Drink) != 0, w.MemDrinkX[i], w.MemDrinkY[i], fx, fy, f, F.DRINK_AHEAD);
        PathMemory.Senses((has & Mem.Fed) != 0, w.MemFedX[i], w.MemFedY[i], fx, fy, f, F.FED_AHEAD);
        float fireNear = (w.Fire[cell] > 0 || w.Fire[CellAt(cell, 2, 0)] > 0 || w.Fire[CellAt(cell, -2, 0)] > 0 || w.Fire[CellAt(cell, 0, 2)] > 0 || w.Fire[CellAt(cell, 0, -2)] > 0) ? 1f : 0f;
        f[F.FEAR] = MathF.Min(1f, MathK.Tanh(w.Scent[cell] * 4f) + fireNear);
        f[F.CROWD] = MathK.Tanh(crowd * 0.5f);
        f[F.SEASON] = w.SeasonAt(y);
        // body heat (Revision O): its own heat need last step (> 0 cold, < 0 hot), and how warm it is here against its body
        bool flier = w.LocoOf(i) == Loco.Fly;
        float ins = w.PInsul[i];
        f[F.HEAT_NEED] = w.HeatNeed[i];
        float opHere = Heat.Operative(w, cell, ins, inWater && !flier);
        f[F.AIR_T] = Math.Clamp((opHere - K.T_BODY) / K.AIR_T_SCALE, -1f, 1f);
        // a cold-blooded body is slow in the cold: muscle power follows the square root of its chemistry's rate (Revision P)
        float musc = Climate.On ? MathF.Sqrt(Heat.Rate(Heat.Core(opHere, w.PEndo[i]))) : 1f;
        // ---- hearing (Revision M, N): every utterance made last step within the caller's range (a louder, bigger caller
        // carries further), from any direction, louder the closer; each sound as loud as its loudest caller
        Span<float> hear = stackalloc float[F.NSOUND];
        float loud = 0, callDx = 0, callDy = 0;
        int jA = -1, jB = -1; float lA = 0, lB = 0, dxA = 0, dyA = 0, dxB = 0, dyB = 0, dA = 0, dB = 0;   // loudest alike caller, loudest of any kind
        int prevF = w.IFocal[i]; float lPrev = 0, dxP = 0, dyP = 0, dP = 0; bool prevAlike = false;
        if (prevF >= 0 && w.Serial[prevF] != w.IFocalSerial[i]) prevF = -1;          // the slot holds another animal now
        for (int o = 0; o < 25; o++)
        {
            int hc = World.WrapI(hcy + Ring[o].dy, Map.GW) * Map.GW + World.WrapI(hcx + Ring[o].dx, Map.GW);
            for (int q = w.CallStart[hc], qe = w.CallStart[hc + 1]; q < qe; q++)
            {
                int j = w.CallSorted[q];
                if (j == i) continue;
                float rc = w.CallR[j];
                float ddx = World.Delta1(w.X[j] - x), ddy = World.Delta1(w.Y[j] - y);
                float d2 = ddx * ddx + ddy * ddy;
                if (d2 >= rc * rc) continue;
                float d = MathF.Sqrt(d2), l = 1f - d / rc;
                int cb = j * F.MAX_SYL;
                for (int p = 0; p < F.MAX_SYL; p++) { int sk = w.Call[cb + p]; if (sk != 0 && l > hear[sk - 1]) hear[sk - 1] = l; }
                if (l > loud) { loud = l; callDx = ddx; callDy = ddy; }
                bool alike = Body.LooksAlike(w, i, j);
                if (alike && l > lA) { lA = l; jA = j; dxA = ddx; dyA = ddy; dA = d; }
                if (l > lB) { lB = l; jB = j; dxB = ddx; dyB = ddy; dB = d; }
                if (j == prevF) { lPrev = l; dxP = ddx; dyP = ddy; dP = d; prevAlike = alike; }
            }
        }
        // the person's voice, made last step at a place on the map, is heard like any other call (it is no organism):
        // a stranger with an ordinary full voice
        float lU = 0, udx = 0, udy = 0;
        if (w.UserCallStep == w.Step - 1 && w.UserCall[0] != 0)
        {
            udx = World.Delta1(w.UserCallX - x); udy = World.Delta1(w.UserCallY - y);
            float ud = MathF.Sqrt(udx * udx + udy * udy);
            if (ud < K.HEAR_R)
            {
                lU = 1f - ud / K.HEAR_R;
                for (int p = 0; p < F.MAX_SYL; p++) { int sk = w.UserCall[p]; if (sk != 0 && lU > hear[sk - 1]) hear[sk - 1] = lU; }
                if (lU > loud) { loud = lU; callDx = udx; callDy = udy; }
            }
        }
        for (int k = 0; k < F.NSOUND; k++) f[F.HEAR_0 + k] = hear[k];
        // the focal caller: the loudest caller that looks alike, else the loudest of any kind (the person included).
        // Attention stays with the same caller while it goes on calling at least FOCAL_KEEP as loud as the loudest
        // of its class (Bee & Micheyl 2008)
        int foc = -1; float fdx = 0, fdy = 0, fd = 0; bool focAlike = false, heardUser = false;
        if (jA >= 0)
        {
            if (prevF >= 0 && prevAlike && lPrev >= K.FOCAL_KEEP * lA) { foc = prevF; fdx = dxP; fdy = dyP; fd = dP; }
            else { foc = jA; fdx = dxA; fdy = dyA; fd = dA; }
            focAlike = true;
        }
        else if (jB >= 0 || lU > 0)
        {
            float top = MathF.Max(lB, lU);
            if (prevF >= 0 && lPrev > 0 && lPrev >= K.FOCAL_KEEP * top) { foc = prevF; fdx = dxP; fdy = dyP; fd = dP; }
            else if (w.IFocal[i] == -2 && lU > 0 && lU >= K.FOCAL_KEEP * top) heardUser = true;
            else if (lU > lB) heardUser = true;
            else { foc = jB; fdx = dxB; fdy = dyB; fd = dB; }
            if (heardUser) { foc = -2; fdx = udx; fdy = udy; fd = MathF.Sqrt(udx * udx + udy * udy); }
        }
        w.IFocal[i] = foc; w.IFocalSerial[i] = foc >= 0 ? w.Serial[foc] : 0u;
        // what the focal caller said, where each sound came in it, and who it is: kin, mother, elder, size, facing me.
        // The person registers as a stranger of no age, size or gaze
        byte f0 = 0, f1 = 0, f2 = 0;
        if (foc >= 0) { int cb = foc * F.MAX_SYL; f0 = w.Call[cb]; f1 = w.Call[cb + 1]; f2 = w.Call[cb + 2]; }
        else if (foc == -2) { f0 = w.UserCall[0]; f1 = w.UserCall[1]; f2 = w.UserCall[2]; }
        if (f0 != 0) f[F.FOC_POS_0 + f0 - 1] = 1f;
        if (f1 != 0) f[F.FOC_POS_0 + f1 - 1] = 0.67f;
        if (f2 != 0) f[F.FOC_POS_0 + f2 - 1] = 0.33f;
        int mb = i * F.MAX_SYL;
        byte m0 = w.LastUtt[mb], m1 = w.LastUtt[mb + 1], m2 = w.LastUtt[mb + 2];
        if (m0 != 0) f[F.MY_POS_0 + m0 - 1] = 1f;
        if (m1 != 0) f[F.MY_POS_0 + m1 - 1] = 0.67f;
        if (m2 != 0) f[F.MY_POS_0 + m2 - 1] = 0.33f;
        if (f0 != 0 && m0 != 0)
        {
            int same = (f0 == m0 ? 1 : 0) + (f1 != 0 && f1 == m1 ? 1 : 0) + (f2 != 0 && f2 == m2 ? 1 : 0);
            int lf = f1 == 0 ? 1 : f2 == 0 ? 2 : 3, lm = m1 == 0 ? 1 : m2 == 0 ? 2 : 3;
            f[F.FOC_LIKE_ME] = same / (float)Math.Max(lf, lm);
        }
        float gzx = 0, gzy = 0;
        if (foc >= 0)
        {
            f[F.FOC_KIN] = focAlike ? 1f : 0f;
            f[F.FOC_MOTHER] = foc == w.MotherOf(i) ? 1f : 0f;
            f[F.FOC_ELDER] = Math.Clamp((w.Age[foc] - w.Age[i]) / MathF.Max(w.PLife[i], 1f), -1f, 1f);
            f[F.FOC_SIZE] = MathK.Tanh(MathK.Log(MathF.Max(w.ApparentMass(foc), 1e-4f) / MathF.Max(myMass, 1e-4f)));
            gzx = MathK.Cos(w.Heading[foc]); gzy = MathK.Sin(w.Heading[foc]);
            f[F.FOC_FACING] = fd > 1e-4f ? -(gzx * fdx + gzy * fdy) / fd : 0f;
            // vocal learning (Revision N): during its sensitive period a learner memorises the form of what an alike,
            // grown, older focal caller says, as counts of syllable pairs (start -> first, first -> second, second -> third).
            // Only form is stored, never what was going on (Doupe & Kuhl 1999; Brainard & Doupe 2002)
            if (focAlike && w.PImit[i] > 0f && w.Evo.VocalLearning && w.StageOf[foc] == St.Adult && w.Age[foc] > w.Age[i]
                && w.Age[i] < w.PImitAge[i] * w.PLife[i])
            {
                int tb = i * Voice.NTMPL;
                Bump(w.Tmpl, tb + Voice.Cell(0, f0));
                if (f1 != 0) Bump(w.Tmpl, tb + Voice.Cell(f0, f1));
                if (f2 != 0) Bump(w.Tmpl, tb + Voice.Cell(f1, f2));
            }
        }
        // a template fades: its counts halve every TMPL_HALF steps (staggered by slot)
        if (w.PImit[i] > 0f && ((long)w.Step + i) % K.TMPL_HALF == 0)
            for (int k = i * Voice.NTMPL, ke = k + Voice.NTMPL; k < ke; k++) w.Tmpl[k] >>= 1;

        // ---- learning update due from the last step's reward, with the rise in fear its move led to (sensed only now)
        w.Reward[i] -= K.R_FEAR * MathF.Max(0f, f[F.FEAR] - w.LastFear[i]);
        w.LastFear[i] = f[F.FEAR];
        Brain.Learn(w, i);
        if (((int)w.Step + i) % 32 == 1) Brain.Relax(w, i);

        // ---- brain
        Span<float> yv = stackalloc float[G.NIN];
        Span<float> z = stackalloc float[P.N];
        Span<float> g = stackalloc float[P.N];
        Span<float> delta = stackalloc float[P.N];
        Brain.Forward(w, i, f, yv, z, g);

        // the brain's intended gains (action probabilities) are what telemetry and the probe report
        g.CopyTo(new Span<float>(w.Gain, i * P.N, P.N));
        if (probe.Slot == i) g.CopyTo(probe.Gn);

        // exploration: perturb continuous gains, sample discrete actions (DESIGN §7.2)
        float sigma = Brain.Sigma(w, i);
        uint acts = 0;
        for (int p = 0; p < P.N; p++)
        {
            float ge;
            if (p < P.FIRST_ACTION)
            {
                // learning sees the noise drawn, not the clamped change: near 0 the clamp cuts the downward half, and its
                // mean would credit a gain with rewards it did not cause (audit 2026-10-30: +0.06 at sigma 0.16, g 0.02)
                float n = (rs.Next() + rs.Next() + rs.Next() - 1.5f) * 2f;
                ge = Math.Clamp(g[p] + sigma * n, 0f, 1f);
                delta[p] = sigma * n;
            }
            else
            {
                bool fire = rs.Next() < g[p];
                ge = fire ? 1f : 0f;
                if (fire) { acts |= 1u << (p - P.FIRST_ACTION); metrics?.Attempt(p); }
                delta[p] = ge - g[p];
            }
            if (metrics != null && p == P.SCAN && ge > 0.5f) metrics.Add(ref metrics.ScanActive);
            if (metrics != null && p == P.SPRINT && g[p] > 0.5f) metrics.Add(ref metrics.SprintRequested);
            g[p] = ge;
        }
        // one utterance (Revision N): of the sounds the brain chose to make, at most MAX_SYL, each once, in the order of
        // how strongly each is intended (Engesser & Townsend 2019). A sound left out is not made, and learning sees that
        uint callBits = Voice.Bits;
        if ((acts & callBits) != 0)
        {
            Span<byte> syl = stackalloc byte[F.MAX_SYL];
            Voice.Order(w, i, acts, syl);
            uint kept = Voice.BitsOf(syl);
            for (int k = 0; k < F.NSOUND; k++)
            {
                uint bit = 1u << (P.CALL_0 + k - P.FIRST_ACTION);
                if ((acts & bit) != 0 && (kept & bit) == 0) { int p = P.CALL_0 + k; delta[p] = -w.Gain[i * P.N + p]; g[p] = 0f; }
            }
            acts = (acts & ~callBits) | kept;
        }
        Brain.Record(w, i, yv, delta);

        // ---- primitive vectors (DESIGN §5) and steering sum
        float sx = 0, sy = 0;
        void Add(float gain, float vx, float vy)
        {
            float l = MathF.Sqrt(vx * vx + vy * vy);
            if (l > 1e-6f) { sx += gain * vx / l; sy += gain * vy / l; }
        }
        if (o1.J >= 0)
        {
            Add(g[P.APPROACH_1], o1.Dx, o1.Dy);
            Add(g[P.FLEE_1], -o1.Dx, -o1.Dy);
            int j = o1.J;
            float lead = o1.D / MathF.Max(vmax, 0.1f);
            Add(g[P.INTERCEPT_1], o1.Dx + w.Speed[j] * MathK.Cos(w.Heading[j]) * lead, o1.Dy + w.Speed[j] * MathK.Sin(w.Heading[j]) * lead);
            float side = (kin.J >= 0 && (o1.Dx * kin.Dy - o1.Dy * kin.Dx) > 0) ? -1f : 1f;
            Add(g[P.CIRCLE_1], -o1.Dy * side, o1.Dx * side);
        }
        if (o2.J >= 0)
        {
            Add(g[P.APPROACH_2], o2.Dx, o2.Dy); Add(g[P.FLEE_2], -o2.Dx, -o2.Dy);
            if (o2.D > R * 0.5f) Add(g[P.SHADOW_2], o2.Dx, o2.Dy); else Add(g[P.SHADOW_2], -o2.Dy, o2.Dx);
        }
        if (kinSum > 0)
        {
            Add(g[P.COHERE], kinCx, kinCy);
            Add(g[P.ALIGN], kinHx, kinHy);
            Add(g[P.SEPARATE], sepX, sepY);
        }
        if (mom >= 0) Add(g[P.FOLLOW_MOTHER], mdx, mdy);
        // looking for a mate (Revision P): toward one in sight; else toward its own kind, where mates are likely; else on
        // straight ahead, the efficient way to search for something out of sight (long straight runs, Bartumeus et al.
        // 2005). Before, a ready animal with no mate in sight did nothing, and thinly spread lineages died unmated
        if (mate.J >= 0) Add(g[P.SEEK_MATE], mate.Dx, mate.Dy);
        else if (kinSum > 0) Add(g[P.SEEK_MATE], kinCx, kinCy);
        else Add(g[P.SEEK_MATE], fx, fy);
        Add(g[P.CLIMB_FOOD], fgx, fgy);
        // water and land are sensed only within SENSE_WATER cells (Revision P: before, a map-wide distance field showed the
        // way from anywhere); beyond it SEEK_WATER heads for where it last drank, if it remembers, so the thirst instinct
        // can use what it has learned of the land
        PathMemory.WaterWay(w, i, cell, out float wvx, out float wvy);
        Add(g[P.SEEK_WATER], wvx, wvy);
        PathMemory.LandWay(w, cell, out float lvx, out float lvy);
        Add(g[P.SEEK_LAND], lvx, lvy);
        if ((has & Mem.Fed) != 0) Add(g[P.RETURN_FED], w.MemFedX[i], w.MemFedY[i]);
        Add(g[P.AVOID_FEAR], w.Scent[CellAt(cell, -1, 0)] - w.Scent[CellAt(cell, 1, 0)], w.Scent[CellAt(cell, 0, -1)] - w.Scent[CellAt(cell, 0, 1)]);
        // a correlated random walk: a compass direction drawn every 16 steps, blended into the next. (It was an offset to
        // the current heading held for 16 steps, so the animal turned the same way each step and walked in circles.)
        uint wb = (step + (uint)i) / 16u; float wt = ((step + (uint)i) & 15u) / 16f;
        float wa0 = Rng.U(seed, wb, (uint)i, Rs.Wander) * 2f * MathF.PI, wa1 = Rng.U(seed, wb + 1u, (uint)i, Rs.Wander) * 2f * MathF.PI;
        Add(g[P.WANDER], (1f - wt) * MathK.Cos(wa0) + wt * MathK.Cos(wa1), (1f - wt) * MathK.Sin(wa0) + wt * MathK.Sin(wa1));
        if (loud > 0) { Add(g[P.TOWARD_CALL], callDx, callDy); Add(g[P.AWAY_CALL], -callDx, -callDy); }
        if (foc >= 0) Add(g[P.FOLLOW_GAZE], gzx, gzy);   // turn the way the focal caller is facing (gaze following)
        if (Climate.On)
        {
            // up and down the operative temperature around it: air, the sun on open ground, shade, water (Revision O)
            int cl = CellAt(cell, -1, 0), cr = CellAt(cell, 1, 0), cu = CellAt(cell, 0, -1), cd = CellAt(cell, 0, 1);
            float tgx = Operative(w, cr, ins, flier) - Operative(w, cl, ins, flier), tgy = Operative(w, cd, ins, flier) - Operative(w, cu, ins, flier);
            Add(g[P.WARMER], tgx, tgy); Add(g[P.COOLER], -tgx, -tgy);
        }

        float newH = hd;
        if (sx * sx + sy * sy > 0.0025f)
        {
            float want = MathK.Atan2(sy, sx);
            float dAng = want - hd;
            while (dAng > MathF.PI) dAng -= 2f * MathF.PI;
            while (dAng < -MathF.PI) dAng += 2f * MathF.PI;
            float tr = w.PTurn[i];
            newH = hd + Math.Clamp(dAng, -tr, tr);
        }
        bool rest = (acts & (1u << (P.REST - P.FIRST_ACTION))) != 0;
        float spd = vmax * Math.Clamp(0.45f + 0.55f * g[P.SPRINT] - 0.45f * g[P.HOLD], 0f, 1f) * (1f - g[P.SCAN]) * (1f - 0.5f * w.Fatigue[i]);
        spd *= musc;
        if (rest || w.LocoOf(i) == Loco.Sessile) spd = 0f;

        // ---- intents
        w.IHeading[i] = newH;
        w.ISpeed[i] = spd;
        w.IActs[i] = acts;
        // a strike hits the nearest other in reach; a blood-drinker goes for the largest in reach
        // only when no other kind is in reach does a strike fall on kin
        w.ITarget[i] = w.PParasite[i] != 0 ? strikeBig.J : strike.J >= 0 ? strike.J : strikeKin.J;
        w.IMate[i] = (mate.J >= 0 && mate.D <= reach + 1f) ? mate.J : -1;
        w.IFoodHere[i] = fh;
        w.IKinDensity[i] = f[F.KIN_DENSITY];
        w.IInfNear[i] = (byte)Math.Min(255, infNear);
        w.IDose[i] = dose;
        w.IStrainSrc[i] = strainSrc;
        w.IScanning[i] = (byte)(g[P.SCAN] > 0.5f ? 1 : 0);
        // what it heard, for the translator: its focal caller's utterance and where it came from
        int ft = Voice.TypeOf(f0, f1, f2);
        w.IHeard[i] = foc >= 0 && ft >= 0 ? 1 + w.Spc[foc] * Voice.NTYPE + ft : 0;
        w.IHeardUser[i] = (byte)(heardUser ? 1 : 0);
        w.IHeardX[i] = x + fdx; w.IHeardY[i] = y + fdy;
        f.CopyTo(new Span<float>(w.Feat, i * F.N, F.N));
        if (probe.Slot == i)
        {
            f.CopyTo(probe.X); new ReadOnlySpan<float>(w.Hidden, i * G.NH, G.NH).CopyTo(probe.Hid);
            z.CopyTo(probe.Z); probe.Acts = acts; probe.Reward = w.Reward[i]; probe.RewardMean = w.RewardMean[i];
        }
    }

    static int SelectNeighbours(World w, int i, uint step, int hcx, int hcy, int nOff,
        Span<int> slots, Span<uint> priorities, out int candidates)
    {
        int count = 0;
        candidates = 0;
        for (int o = 0; o < nOff; o++)
        {
            int hc = World.WrapI(hcy + Ring[o].dy, Map.GW) * Map.GW + World.WrapI(hcx + Ring[o].dx, Map.GW);
            for (int q = w.CellStart[hc], qe = w.CellStart[hc + 1]; q < qe; q++)
            {
                int j = w.Sorted[q];
                if (j == i) continue;
                candidates++;
                if (candidates <= K.MAX_NEIGH)
                {
                    slots[count++] = j;
                }
                else
                {
                    if (candidates == K.MAX_NEIGH + 1)
                    {
                        for (int k = 0; k < count; k++) priorities[k] = Rng.Hash(w.Seed, step, w.Serial[i], w.Serial[slots[k]]);
                        for (int k = count / 2 - 1; k >= 0; k--) HeapDown(slots, priorities, count, k);
                    }
                    uint priority = Rng.Hash(w.Seed, step, w.Serial[i], w.Serial[j]);
                    if (Before(priority, j, priorities[0], slots[0]))
                    {
                        slots[0] = j; priorities[0] = priority;
                        HeapDown(slots, priorities, count, 0);
                    }
                }
            }
        }
        if (candidates <= K.MAX_NEIGH) return count;
        // When the cap binds, keep the 64 lowest stable priorities and process them in priority order.
        for (int k = 1; k < count; k++)
        {
            int slot = slots[k], j = k;
            uint priority = priorities[k];
            while (j > 0 && Before(priority, slot, priorities[j - 1], slots[j - 1]))
            {
                slots[j] = slots[j - 1]; priorities[j] = priorities[j - 1];
                j--;
            }
            slots[j] = slot; priorities[j] = priority;
        }
        return count;
    }

    static void HeapDown(Span<int> slots, Span<uint> priorities, int count, int k)
    {
        while (true)
        {
            int child = 2 * k + 1;
            if (child >= count) return;
            if (child + 1 < count && Before(priorities[child], slots[child], priorities[child + 1], slots[child + 1])) child++;
            if (!Before(priorities[k], slots[k], priorities[child], slots[child])) return;
            Swap(slots, priorities, k, child);
            k = child;
        }
    }

    static bool Before(uint pa, int a, uint pb, int b) => pa < pb || (pa == pb && a < b);

    static void Swap(Span<int> slots, Span<uint> priorities, int a, int b)
    {
        (slots[a], slots[b]) = (slots[b], slots[a]);
        (priorities[a], priorities[b]) = (priorities[b], priorities[a]);
    }

    static void CompareUncappedTargets(World w, int i, float x, float y, float fx, float fy, float R, float R2,
        float fovCos, bool ready, float reach, int nOff, Near o1, Near o2, Near mate, Near strike, Near strikeBig,
        Near strikeKin, DecisionMetrics metrics)
    {
        int seen = 0;
        Near allO1 = new() { D = 1e9f, J = -1 }, allO2 = new() { D = 1e9f, J = -1 },
            allMate = new() { D = 1e9f, J = -1 }, allStrike = new() { D = 1e9f, J = -1 },
            allStrikeBig = new() { D = 1e9f, J = -1 }, allStrikeKin = new() { D = 1e9f, J = -1 };
        float o2Mass = -1f, strikeBigMass = -1f;
        int hcx = (int)(x / K.HCELL), hcy = (int)(y / K.HCELL);
        for (int o = 0; o < nOff; o++)
        {
            int hc = World.WrapI(hcy + Ring[o].dy, Map.GW) * Map.GW + World.WrapI(hcx + Ring[o].dx, Map.GW);
            for (int q = w.CellStart[hc], qe = w.CellStart[hc + 1]; q < qe; q++)
            {
                int j = w.Sorted[q];
                if (j == i) continue;
                seen++;
                float dx = World.Delta1(w.X[j] - x), dy = World.Delta1(w.Y[j] - y), d2 = dx * dx + dy * dy;
                if (d2 > R2 || d2 < 1e-8f) continue;
                float d = MathF.Sqrt(d2);
                if (d > reach && (dx * fx + dy * fy) / d < fovCos) continue;
                byte stage = w.StageOf[j];
                bool active = stage >= St.Juvenile, alike = Body.LooksAlike(w, i, j);
                if (ready && active && d < allMate.D && ValidPartner(w, i, j))
                    allMate = new Near { D = d, Dx = dx, Dy = dy, J = j };
                if (alike)
                {
                    if (d <= reach + 0.5f + 0.4f * MathK.Cbrt(w.ApparentMass(j)) && d < allStrikeKin.D)
                        allStrikeKin = new Near { D = d, Dx = dx, Dy = dy, J = j };
                    continue;
                }
                if (d < allO1.D) allO1 = new Near { D = d, Dx = dx, Dy = dy, J = j };
                float mass = w.ApparentMass(j);
                if (mass > o2Mass) { o2Mass = mass; allO2 = new Near { D = d, Dx = dx, Dy = dy, J = j }; }
                float reachJ = reach + 0.5f + 0.4f * MathK.Cbrt(mass);
                if (d <= reachJ && d < allStrike.D) allStrike = new Near { D = d, Dx = dx, Dy = dy, J = j };
                if (d <= reachJ && mass > strikeBigMass)
                {
                    strikeBigMass = mass;
                    allStrikeBig = new Near { D = d, Dx = dx, Dy = dy, J = j };
                }
            }
        }
        if (seen <= K.MAX_NEIGH) return;
        metrics.Add(ref metrics.CapHits);
        if (o1.J != allO1.J) metrics.Add(ref metrics.O1Changed);
        if (o2.J != allO2.J) metrics.Add(ref metrics.O2Changed);
        int chosenMate = mate.J >= 0 && mate.D <= reach + 1f ? mate.J : -1;
        int fullMate = allMate.J >= 0 && allMate.D <= reach + 1f ? allMate.J : -1;
        if (chosenMate != fullMate) metrics.Add(ref metrics.MateChanged);
        int chosenStrike = w.PParasite[i] != 0 ? strikeBig.J : strike.J >= 0 ? strike.J : strikeKin.J;
        int fullStrike = w.PParasite[i] != 0 ? allStrikeBig.J : allStrike.J >= 0 ? allStrike.J : allStrikeKin.J;
        if (chosenStrike != fullStrike) metrics.Add(ref metrics.StrikeChanged);
    }

    /// <summary>Operative temperature an animal would be in at a cell (in the water if the cell is water and it does not fly).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float Operative(World w, int c, float ins, bool flier) => Heat.Operative(w, c, ins, !flier && (w.Cell[c] & (byte)Realm.Water) != 0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void Bump(byte[] t, int k) { if (t[k] < 255) t[k]++; }

    public static bool Ready(World w, int i)
    {
        if (w.StageOf[i] != St.Adult) return false;
        if (w.Cooldown[i] > 0 || (w.Flags[i] & Flag.Carrying) != 0) return false;
        // a male only needs to afford mating (and keep a reserve); a female (or hermaphrodite) must afford young
        if (w.PSexual[i] != 0 && w.PHerm[i] == 0 && w.Sex[i] != 0)
            return w.Energy[i] > K.C_MATE * w.PM[i] + 0.15f * w.EmaxOf(i);
        return w.Energy[i] > w.PReadyE[i] * w.EmaxOf(i);   // its own breeding threshold, a gene (Revision P)
    }

    /// <summary>
    /// A possible mate: ready, sexual, with the same sex system, and of the other sex (any two hermaphrodites can mate).
    /// There is no species check here: whether the mating produces young depends on how different their genes are
    /// (Resolve.Mate, D_COMPAT), so hybrids are possible and species separate gradually (DESIGN §7.9).
    /// </summary>
    public static bool ValidPartner(World w, int i, int j)
    {
        if (w.PSexual[i] == 0 || w.PSexual[j] == 0) return false;
        if (!Ready(w, j)) return false;
        // a hermaphrodite can mate with anyone sexual (mixed systems, so switching is not fatal); two separate-sexed need opposite sexes
        return w.PHerm[i] != 0 || w.PHerm[j] != 0 || w.Sex[i] != w.Sex[j];
    }

    /// <summary>Raw senses of another organism, written from feature index k: close, relative size, closing speed, armed, meat-eating.</summary>
    static void Other(World w, int i, Near o, float R, float vmax, float fx, float fy, float myMass, Span<float> f, int k)
    {
        int j = o.J;
        float mj = w.ApparentMass(j);
        f[k] = 1f - o.D / R;
        f[k + 1] = MathK.Tanh(MathK.Log(MathF.Max(mj, 1e-4f) / MathF.Max(myMass, 1e-4f)));
        float rvx = w.Speed[j] * MathK.Cos(w.Heading[j]) - w.Speed[i] * fx, rvy = w.Speed[j] * MathK.Sin(w.Heading[j]) - w.Speed[i] * fy;
        f[k + 2] = Math.Clamp(-(rvx * o.Dx + rvy * o.Dy) / MathF.Max(o.D, 1e-3f) / MathF.Max(vmax, 0.05f), -1f, 1f);
        bool active = w.StageOf[j] >= St.Juvenile;
        f[k + 3] = active ? MathK.Tanh(w.PStrike[j] * MathK.Cbrt(mj / MathF.Max(myMass, 1e-4f))) : 0f;
        f[k + 4] = active ? w.Carnivory(j) : 0f;
    }

    public static float Weakness(World w, int j)
    {
        byte s = w.StageOf[j];
        if (s == St.Egg) return 1f;
        float v = s == St.Juvenile ? 0.6f : 0f;
        v += 0.3f * w.Fatigue[j];
        v += 0.3f * MathF.Max(0f, 1f - w.PArmour[j]);
        return MathF.Min(1f, v);
    }
}

/// <summary>Per-slot flag bits.</summary>
public static class Flag
{
    public const byte Carrying = 1, Newborn = 2, Immigrant = 8;
}
