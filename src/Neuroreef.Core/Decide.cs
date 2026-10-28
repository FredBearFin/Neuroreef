// DECIDE phase (DESIGN §11, phase 2). Parallel over slots. For each acting animal: apply the learning
// update due from last step's reward, perceive (§4), compute each primitive's vector (§5), run the
// brain (§7), add exploration, and write intents. Each call writes only its own slot (NR-105).

using System;
using System.Runtime.CompilerServices;

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

    /// <summary>Value of the best digestible food in a cell, normalised to the animal's best possible food (0..1).</summary>
    public static float FoodValue(World w, ReadOnlySpan<float> diet, float best, int cell)
    {
        float v = 0;
        for (int p = 0; p < Pools.NFIELD; p++)
        {
            float e = diet[p];
            if (e <= 0) continue;
            v = MathF.Max(v, e * Pools.Rho[p] * MathF.Min(1f, w.Pool[p][cell]));
        }
        return v / best;
    }

    static int CellAt(int c, int dx, int dy)
        => World.WrapI(c / K.FW + dy, K.FW) * K.FW + World.WrapI(c % K.FW + dx, K.FW);

    public static void Run(World w, int i, ProbeBuf probe)
    {
        byte stg = w.StageOf[i];
        if (stg < St.Juvenile) return;
        var sp = w.Sp[w.Spc[i]];
        var body = w.StageBody(i);
        var diet = w.DietOf(i);
        uint seed = w.Seed, step = (uint)w.Step;
        var rs = new Rng.Seq(Rng.Hash(seed, step, (uint)i, Rs.Explore));
        // ---- learning update due from the last step's reward
        Brain.Learn(w, i);
        if (((int)w.Step + i) % 32 == 1) Brain.Relax(w, i);

        float x = w.X[i], y = w.Y[i], hd = w.Heading[i];
        float fx = MathK.Cos(hd), fy = MathK.Sin(hd);
        float myMass = w.ApparentMass(i);
        bool scanning = w.IScanning[i] != 0;
        float R = w.PSight[i] * (scanning ? 1.3f : 1f);
        float R2 = R * R;
        float fovCos = scanning ? -2f : sp.FovCos;
        float vmax = w.Vmax(i);
        int cell = World.FIdx(x, y);
        bool ready = Ready(w, i);
        int mySpc = w.Spc[i];
        bool myJuv = stg == St.Juvenile;

        // ---- perception of other animals (spatial hash, radius 1 or 2 cells)
        float kinSum = 0, kinCx = 0, kinCy = 0, kinHx = 0, kinHy = 0, sepX = 0, sepY = 0, crowd = 0, bigSum = 0, smallSum = 0;
        Near kin = new() { D = 1e9f, J = -1 }, big = new() { D = 1e9f, J = -1 }, small = new() { D = 1e9f, J = -1 },
             carn = new() { D = 1e9f, J = -1 }, mate = new() { D = 1e9f, J = -1 }, strike = new() { D = 1e9f, J = -1 }, strikeBig = new() { D = 1e9f, J = -1 };
        float reach = w.PReach[i];
        int hcx = (int)(x / K.HCELL), hcy = (int)(y / K.HCELL);
        int nOff = R > K.HCELL ? 25 : 9;
        int seen = 0;
        for (int o = 0; o < nOff && seen < K.MAX_NEIGH; o++)
            {
                int hc = World.WrapI(hcy + Ring[o].dy, K.GW) * K.GW + World.WrapI(hcx + Ring[o].dx, K.GW);
                int a = w.CellStart[hc], b = w.CellStart[hc + 1];
                for (int q = a; q < b && seen < K.MAX_NEIGH; q++)
                {
                    int j = w.Sorted[q];
                    if (j == i) continue;
                    seen++;                                                  // examined (the cap bounds the work)
                    float ddx = World.Delta1(w.X[j] - x), ddy = World.Delta1(w.Y[j] - y);
                    float d2 = ddx * ddx + ddy * ddy;
                    if (d2 > R2 || d2 < 1e-8f) continue;
                    float d = MathF.Sqrt(d2);
                    if (d > reach && (ddx * fx + ddy * fy) / d < fovCos) continue;     // outside field of view
                    float close = 1f - d / R;
                    crowd += close;
                    byte sj = w.StageOf[j];
                    if (w.Spc[j] == mySpc && sj >= St.Juvenile)
                    {
                        kinSum += close; kinCx += ddx; kinCy += ddy;
                        kinHx += MathK.Cos(w.Heading[j]); kinHy += MathK.Sin(w.Heading[j]);
                        if (d < 2f) { sepX -= ddx / d2; sepY -= ddy / d2; }
                        if (d < kin.D) kin = new Near { D = d, Dx = ddx, Dy = ddy, J = j };
                        if (ready && d < mate.D && ValidPartner(w, i, j)) mate = new Near { D = d, Dx = ddx, Dy = ddy, J = j };
                        continue;
                    }
                    // heterospecific (or egg of any species)
                    float mj = sj == St.Egg ? 0f : w.ApparentMass(j);
                    if (sj == St.Egg || mj * 1.15f < myMass)
                    {
                        smallSum += close;
                        if (d < small.D) small = new Near { D = d, Dx = ddx, Dy = ddy, J = j };
                        if (d <= reach + 0.5f && d < strike.D) strike = new Near { D = d, Dx = ddx, Dy = ddy, J = j };
                    }
                    else if (mj > myMass * 1.15f)
                    {
                        bigSum += close;
                        if (d < big.D) big = new Near { D = d, Dx = ddx, Dy = ddy, J = j };
                        if (d <= reach + 0.5f + 0.4f * MathK.Cbrt(mj) && d < strikeBig.D) strikeBig = new Near { D = d, Dx = ddx, Dy = ddy, J = j };
                    }
                    else if (d <= reach + 0.5f && d < strikeBig.D) strikeBig = new Near { D = d, Dx = ddx, Dy = ddy, J = j };
                    if (sj >= St.Juvenile && w.Carnivory(j) > 0.3f && d < carn.D) carn = new Near { D = d, Dx = ddx, Dy = ddy, J = j };
                }
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
            f[F.MOTHER_FAR] = MathF.Min(1f, MathF.Sqrt(mdx * mdx + mdy * mdy) / R);
        }
        f[F.MATE_CLOSE] = mate.J >= 0 ? 1f - mate.D / R : 0f;
        f[F.BIG_CLOSE] = big.J >= 0 ? 1f - big.D / R : 0f;
        f[F.BIG_DENSITY] = MathK.Tanh(bigSum);
        if (big.J >= 0)
        {
            int j = big.J;
            float rvx = w.Speed[j] * MathK.Cos(w.Heading[j]) - w.Speed[i] * fx, rvy = w.Speed[j] * MathK.Sin(w.Heading[j]) - w.Speed[i] * fy;
            float closing = -(rvx * big.Dx + rvy * big.Dy) / big.D;
            f[F.BIG_APPROACH] = Math.Clamp(closing / MathF.Max(vmax, 0.05f), -1f, 1f);
            f[F.BIG_CARN] = w.StageOf[j] >= St.Juvenile ? w.Carnivory(j) : 0f;
        }
        f[F.SMALL_CLOSE] = small.J >= 0 ? 1f - small.D / R : 0f;
        f[F.SMALL_DENSITY] = MathK.Tanh(smallSum);
        if (small.J >= 0) f[F.SMALL_WEAK] = Weakness(w, small.J);
        f[F.CARN_CLOSE] = carn.J >= 0 ? 1f - carn.D / R : 0f;

        float bestFood = 1e-6f;
        for (int p = 0; p < Pools.NFIELD; p++) bestFood = MathF.Max(bestFood, diet[p] * Pools.Rho[p]);
        float fh = FoodValue(w, diet, bestFood, cell);
        float fgx = FoodValue(w, diet, bestFood, CellAt(cell, 2, 0)) - FoodValue(w, diet, bestFood, CellAt(cell, -2, 0));
        float fgy = FoodValue(w, diet, bestFood, CellAt(cell, 0, 2)) - FoodValue(w, diet, bestFood, CellAt(cell, 0, -2));
        f[F.FOOD_HERE] = fh;
        f[F.FOOD_GRAD] = MathK.Tanh(MathF.Sqrt(fgx * fgx + fgy * fgy) * 3f);
        bool inWater = (w.Cell[cell] & (byte)Realm.Water) != 0;
        f[F.IN_WATER] = inWater ? 1f : 0f;
        f[F.WATER_CLOSE] = MathF.Max(0f, 1f - w.DistWater[cell] / 8f);
        f[F.LAND_CLOSE] = MathF.Max(0f, 1f - w.DistLand[cell] / 8f);
        float fireNear = (w.Fire[cell] > 0 || w.Fire[CellAt(cell, 2, 0)] > 0 || w.Fire[CellAt(cell, -2, 0)] > 0 || w.Fire[CellAt(cell, 0, 2)] > 0 || w.Fire[CellAt(cell, 0, -2)] > 0) ? 1f : 0f;
        f[F.FEAR] = MathF.Min(1f, MathK.Tanh(w.Scent[cell] * 4f) + fireNear);
        f[F.CROWD] = MathK.Tanh(crowd * 0.5f);
        f[F.SEASON] = w.SeasonAt(y);

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
                float n = (rs.Next() + rs.Next() + rs.Next() - 1.5f) * 2f;
                ge = Math.Clamp(g[p] + sigma * n, 0f, 1f);
            }
            else
            {
                bool fire = rs.Next() < g[p];
                ge = fire ? 1f : 0f;
                if (fire) acts |= 1u << (p - P.FIRST_ACTION);
            }
            delta[p] = ge - g[p];
            g[p] = ge;
        }
        Brain.Record(w, i, yv, delta);

        // ---- primitive vectors (DESIGN §5) and steering sum
        float sx = 0, sy = 0;
        void Add(float gain, float vx, float vy)
        {
            float l = MathF.Sqrt(vx * vx + vy * vy);
            if (l > 1e-6f) { sx += gain * vx / l; sy += gain * vy / l; }
        }
        if (small.J >= 0)
        {
            Add(g[P.SEEK_SMALL], small.Dx, small.Dy);
            int j = small.J;
            float lead = small.D / MathF.Max(vmax, 0.1f);
            Add(g[P.INTERCEPT_SMALL], small.Dx + w.Speed[j] * MathK.Cos(w.Heading[j]) * lead, small.Dy + w.Speed[j] * MathK.Sin(w.Heading[j]) * lead);
            float side = (kin.J >= 0 && (small.Dx * kin.Dy - small.Dy * kin.Dx) > 0) ? -1f : 1f;
            Add(g[P.CIRCLE_SMALL], -small.Dy * side, small.Dx * side);
        }
        if (big.J >= 0) { Add(g[P.SEEK_BIG], big.Dx, big.Dy); Add(g[P.FLEE_BIG], -big.Dx, -big.Dy); }
        if (carn.J >= 0)
        {
            if (carn.D > R * 0.5f) Add(g[P.SHADOW_CARN], carn.Dx, carn.Dy); else Add(g[P.SHADOW_CARN], -carn.Dy, carn.Dx);
            Add(g[P.FLEE_CARN], -carn.Dx, -carn.Dy);
        }
        if (kinSum > 0)
        {
            Add(g[P.COHERE], kinCx, kinCy);
            Add(g[P.ALIGN], kinHx, kinHy);
            Add(g[P.SEPARATE], sepX, sepY);
        }
        if (mom >= 0) Add(g[P.FOLLOW_MOTHER], mdx, mdy);
        if (mate.J >= 0) Add(g[P.SEEK_MATE], mate.Dx, mate.Dy);
        Add(g[P.CLIMB_FOOD], fgx, fgy);
        Add(g[P.SEEK_WATER], w.DistWater[CellAt(cell, -1, 0)] - w.DistWater[CellAt(cell, 1, 0)], w.DistWater[CellAt(cell, 0, -1)] - w.DistWater[CellAt(cell, 0, 1)]);
        Add(g[P.SEEK_LAND], w.DistLand[CellAt(cell, -1, 0)] - w.DistLand[CellAt(cell, 1, 0)], w.DistLand[CellAt(cell, 0, -1)] - w.DistLand[CellAt(cell, 0, 1)]);
        Add(g[P.AVOID_FEAR], w.Scent[CellAt(cell, -1, 0)] - w.Scent[CellAt(cell, 1, 0)], w.Scent[CellAt(cell, 0, -1)] - w.Scent[CellAt(cell, 0, 1)]);
        float wa = hd + (Rng.U(seed, step / 16u, (uint)i, Rs.Wander) - 0.5f) * 2.0f;   // slowly changing direction
        Add(g[P.WANDER], MathK.Cos(wa), MathK.Sin(wa));

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
        if (rest || body.Loco == Loco.Sessile) spd = 0f;

        // ---- intents
        w.IHeading[i] = newH;
        w.ISpeed[i] = spd;
        w.IActs[i] = acts;
        w.ITarget[i] = sp.Parasite ? (strikeBig.J >= 0 ? strikeBig.J : -1) : (strike.J >= 0 ? strike.J : strikeBig.J);
        w.IMate[i] = (mate.J >= 0 && mate.D <= reach + 1f) ? mate.J : -1;
        w.IFoodHere[i] = fh;
        w.IKinDensity[i] = f[F.KIN_DENSITY];
        w.IScanning[i] = (byte)(g[P.SCAN] > 0.5f ? 1 : 0);
        f.CopyTo(new Span<float>(w.Feat, i * F.N, F.N));
        if (probe.Slot == i)
        {
            f.CopyTo(probe.X); new ReadOnlySpan<float>(w.Hidden, i * G.NH, G.NH).CopyTo(probe.Hid);
            z.CopyTo(probe.Z); probe.Acts = acts; probe.Reward = w.Reward[i]; probe.RewardMean = w.RewardMean[i];
        }
    }

    public static bool Ready(World w, int i)
    {
        if (w.StageOf[i] != St.Adult) return false;
        if (w.Cooldown[i] > 0 || (w.Flags[i] & Flag.Carrying) != 0) return false;
        return w.Energy[i] > K.READY_ENERGY * w.EmaxOf(i);
    }

    public static bool ValidPartner(World w, int i, int j)
    {
        if (!Ready(w, j)) return false;
        var mode = w.Sp[w.Spc[i]].Mode;
        if (mode == Repro.Hermaphrodite) return true;
        if (mode == Repro.Fission || mode == Repro.Spores) return false;
        return w.Sex[i] != w.Sex[j];
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
    public const byte Carrying = 1, Newborn = 2, Resting = 4, Immigrant = 8;
}
