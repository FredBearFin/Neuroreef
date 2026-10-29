// RESOLVE phase (DESIGN §11, phase 3). Serial, in a seeded order that changes every step, so contested
// food and prey go to whoever comes first, deterministically. Applies movement, drinking, eating,
// strikes, mating and asexual reproduction, costs, digestion and death. Computes each animal's reward.

using System;

namespace Neuroreef;

public static class Resolve
{
    const uint BIT_EAT = 1u << (P.EAT - P.FIRST_ACTION), BIT_STRIKE = 1u << (P.STRIKE - P.FIRST_ACTION),
        BIT_DRINK = 1u << (P.DRINK - P.FIRST_ACTION), BIT_MATE = 1u << (P.MATE - P.FIRST_ACTION),
        BIT_REST = 1u << (P.REST - P.FIRST_ACTION), BIT_MARK = 1u << (P.MARK - P.FIRST_ACTION),
        BIT_LAY = 1u << (P.LAY - P.FIRST_ACTION), BIT_CACHE = 1u << (P.CACHE - P.FIRST_ACTION);

    public static void Run(World w, int orderA, int orderB)
    {
        int cap = w.Cap;
        for (int k = 0; k < cap; k++)
        {
            int i = (int)(((long)k * orderA + orderB) % cap);
            if (w.StageOf[i] < St.Juvenile || (w.Flags[i] & Flag.Newborn) != 0) continue;
            One(w, i);
        }
    }

    static void One(World w, int i)
    {
        var T = w.T;
        var loco = w.LocoOf(i); var realm = w.RealmOf(i);
        int s = w.Spc[i];
        uint seed = w.Seed, step = (uint)w.Step;
        float mass = w.MassOf(i), emax = K.C_EMAX * mass, mf = mass / w.PM[i];
        var diet = w.DietOf(i);
        float r = w.PendingR[i]; w.PendingR[i] = 0;
        uint acts = w.IActs[i];
        bool rest = (acts & BIT_REST) != 0;
        bool scanning = w.IScanning[i] != 0;

        // ---- 1. move (realm rule, DESIGN §9.2)
        float h = w.IHeading[i], spd = w.ISpeed[i];
        float nx = World.Wrap(w.X[i] + MathK.Cos(h) * spd), ny = World.Wrap(w.Y[i] + MathK.Sin(h) * spd);
        int nc = World.FIdx(nx, ny);
        if (spd > 0 && !w.CellAllows(nc, realm))
        {
            spd = 0;
            h += Rng.U(seed, step, (uint)i, Rs.Turn) < 0.5f ? MathF.PI * 0.5f : -MathF.PI * 0.5f;
        }
        else { w.X[i] = nx; w.Y[i] = ny; }
        w.Heading[i] = h; w.Speed[i] = spd;
        int cell = World.FIdx(w.X[i], w.Y[i]);
        bool waterCell = (w.Cell[cell] & (byte)Realm.Water) != 0;

        if (w.Fire[cell] > 0 && Rng.U(seed, step, (uint)i, Rs.Death) < K.FIRE_KILL) { Die(w, i, Cause.Fire, -1); return; }

        // ---- 2. drink
        if ((acts & BIT_DRINK) != 0 && w.DistDrink[cell] <= 1.5f)
        {
            float rel = MathF.Min(w.Thirst[i], K.DRINK_RATE);
            w.Thirst[i] -= rel; r += K.R_DRINK * rel;
        }

        // ---- 3. eat (DESIGN §9.3)
        float gutCap = w.PGut[i] * mf;
        // eating another organism is attacking it (DESIGN §9.3, Revision K): the same strike, chosen by hunger
        bool preyEat = (acts & BIT_EAT) != 0 && w.IEatPrey[i] != 0 && (acts & BIT_STRIKE) == 0 && w.PStrike[i] > 0;
        if (preyEat) acts |= BIT_STRIKE;
        if (!preyEat && (acts & BIT_EAT) != 0 && !scanning && w.Gut[i] < gutCap && !(waterCell && loco == Loco.Fly && (w.Cell[cell] & (byte)Realm.Deep) != 0))
        {
            int best = -1; float bv = 0;
            for (int p = 0; p < Pools.NFIELD; p++)
            {
                float e = diet[p];
                if (e <= 0) continue;
                float v = e * Pools.Rho[p] * MathF.Min(1f, w.Pool[p][cell]);
                if (v > bv) { bv = v; best = p; }
            }
            if (best >= 0)
            {
                float bite = MathF.Min(MathF.Min(w.PBite[i] * MathK.Pow(mf, K.EXP_BASAL), w.Pool[best][cell]), gutCap - w.Gut[i]);
                if (bite > 0)
                {
                    w.Pool[best][cell] -= bite;
                    if (best == (int)Pool.Browse) w.Sapling[cell] = MathF.Max(0, w.Sapling[cell] - 0.5f * bite);
                    else if (best == (int)Pool.Root) w.Pool[(int)Pool.Grass][cell] = MathF.Max(0, w.Pool[(int)Pool.Grass][cell] - 0.3f * bite);
                    else if (best == (int)Pool.Nectar) w.Poll[cell] = MathF.Min(1f, w.Poll[cell] + K.POLL_GAIN * bite);
                    r += Ingest(w, i, best, bite, diet[best], emax, cell);
                }
            }
        }

        // ---- 4. strike (DESIGN §9.4)
        if ((acts & BIT_STRIKE) != 0 && w.PStrike[i] > 0)
        {
            int v = w.ITarget[i];
            if (v >= 0 && w.StageOf[v] != St.Dead && w.StageOf[v] != St.Embryo)
            {
                w.Energy[i] -= K.C_STRIKE * mass;                       // no target, no strike (DESIGN §5)
                T.Strikes[s]++;
                float dx = World.Delta1(w.X[v] - w.X[i]), dy = World.Delta1(w.Y[v] - w.Y[i]);
                float reachAll = w.PReach[i] + 0.5f + 0.4f * MathK.Cbrt(w.ApparentMass(v));
                if (dx * dx + dy * dy <= reachAll * reachAll) r += Strike(w, i, v, gutCap, emax);
            }
        }

        // ---- 5. mate / lay (DESIGN §8)
        if ((acts & BIT_MATE) != 0 && w.IMate[i] >= 0) r += Mate(w, i, w.IMate[i], cell);
        if ((acts & BIT_LAY) != 0 && Decide.Ready(w, i)) r += Lay(w, i, cell);

        // ---- 6a. voice (Revision M): a sound costs a little energy, and is heard by everyone near on the next step
        int callK = -1;
        for (int k = 0; k < F.NSOUND; k++) if ((acts & (1u << (P.CALL_0 + k - P.FIRST_ACTION))) != 0) { callK = k; break; }
        if (w.PVoice[i] < K.VOICE_MIN) callK = -1;   // a mute animal's call makes no sound (and costs nothing)
        w.Call[i] = (byte)(callK + 1);
        if (callK >= 0) w.Energy[i] -= K.C_CALL * K.C_BASAL * MathK.Pow(mass, K.EXP_BASAL);
        Voices(w, i, s, callK, acts);

        // ---- 6. mark and scent, cache
        float carn = diet[(int)Pool.Meat];
        float scent = K.C_SCENT * carn * ((acts & BIT_MARK) != 0 ? K.MARK_GAIN : 1f);
        if ((acts & BIT_MARK) != 0 && carn <= 0) scent = K.C_SCENT * 0.5f;   // anyone can mark, faintly
        w.Scent[cell] = MathF.Min(4f, w.Scent[cell] + scent);
        if ((acts & BIT_CACHE) != 0 && w.GutSeedy[i] > 0 && !waterCell)
        {
            float m = 0.5f * w.GutSeedy[i], frac = m / MathF.Max(w.Gut[i], 1e-6f);
            w.Pool[(int)Pool.Seed][cell] += m;
            w.Gut[i] -= m; w.GutEnergy[i] *= 1f - frac; w.GutWaste[i] *= 1f - frac; w.GutSeedy[i] -= m;
        }

        // ---- 7. costs, fatigue, digestion (DESIGN §9.1, §9.3)
        float vmax = MathF.Max(w.Vmax(i), 1e-3f);
        // cost of transport: ~M^0.7 x speed (Taylor et al. 1982); equal to the old M*v^2 rule at M=1 and full walking speed
        float moveCost = K.C_MOVE * K.C_SPEED * MathK.Pow(mass, K.EXP_MOVE) * spd * (loco == Loco.Fly ? K.FLY_COST : 1f);
        bool sick = w.Infect[i] > 0;
        float basal = w.PBasal[i] * MathK.Pow(mf, K.EXP_BASAL) * (rest ? K.REST_BASAL : 1f) * (sick ? 1f + K.DIS_COST * w.Virulence[i] : 1f);
        // growth: a juvenile builds tissue from its spare energy (no free mass, DESIGN §9.1). It grows at most at
        // the rate that would reach adult size at PMature, and only from energy above GROW_RESERVE of its reserve:
        // a well-fed youngster grows fast, a hungry one slowly, a starving one not at all
        float grow = 0;
        if (w.StageOf[i] == St.Juvenile && w.Grown[i] < 1f)
        {
            float tissue = K.C_CARC * w.PM[i] * (1f - w.PBirth[i]) * K.RHO_MEAT / K.GROWTH_EFF;   // energy to build all of it
            grow = Math.Clamp(w.Energy[i] - K.GROW_RESERVE * emax, 0f, tissue / w.PMature[i]);
            w.Grown[i] = MathF.Min(1f, w.Grown[i] + grow / tissue);
        }
        w.Energy[i] -= basal + moveCost + grow;
        T.SpendBasal[s] += basal; T.SpendMove[s] += moveCost; T.SpendGrowth[s] += grow;
        float fat0 = w.Fatigue[i];
        float sprint = w.Gain[i * P.N + P.SPRINT];
        w.Fatigue[i] = Math.Clamp(w.Fatigue[i] + K.C_FATIGUE * sprint * spd / vmax - K.C_RECOVER * (rest ? 3f : 1f), 0f, 1f);
        r += K.R_REST * MathF.Max(0f, fat0 - w.Fatigue[i]);

        if (w.Gut[i] > 1e-7f)
        {
            float d = MathF.Min(w.Gut[i], w.PDig[i] * MathK.Pow(mf, K.EXP_BASAL));
            float frac = d / w.Gut[i];
            float de = w.GutEnergy[i] * frac, dung = w.GutWaste[i] * frac, seedy = w.GutSeedy[i] * frac;
            w.Energy[i] += de;
            w.Gut[i] -= d; w.GutEnergy[i] -= de; w.GutWaste[i] -= dung; w.GutSeedy[i] -= seedy;
            if (waterCell) w.Pool[(int)Pool.Detritus][cell] += dung;
            else
            {
                w.Pool[(int)Pool.Dung][cell] += dung;
                w.Sapling[cell] = MathF.Min(1f, w.Sapling[cell] + seedy * K.DUNG_SAPLING);
            }
        }
        if (w.Energy[i] > emax) w.Energy[i] = emax;

        // an organism dries out on land, less on moist ground; in water it does not (DESIGN §9.4)
        if (!waterCell) w.Thirst[i] = MathF.Min(1f, w.Thirst[i] + K.C_THIRST * Nature.Thirst * w.PThirstK[i] * MathF.Max(0f, 1f - K.SOIL_WATER * w.Moisture[cell]));
        w.Age[i] += 1;
        if (w.Cooldown[i] > 0) w.Cooldown[i] -= 1;
        w.Pain[i] = MathF.Max(sick ? K.DIS_PAIN : 0f, w.Pain[i] - K.PAIN_DECAY);   // an infected animal feels unwell
        float fear = w.Feat[i * F.N + F.FEAR];
        r -= K.R_FEAR * MathF.Max(0f, fear - w.LastFear[i]);
        w.LastFear[i] = fear;
        w.Reward[i] = r;
        T.RewardSum[s] += r;

        // ---- 8. death
        if (w.Energy[i] <= 0) { Die(w, i, Cause.Starve, -1); return; }
        if (w.Thirst[i] >= 1f) { Die(w, i, Cause.Thirst, -1); return; }
        float life = w.PLife[i];
        if (w.Age[i] > life)
        {
            float a = w.Age[i] / life;
            if (Rng.U(seed, step, (uint)i, Rs.Death + 1) < 0.002f * a * a) { Die(w, i, Cause.Old, -1); return; }
        }
    }

    /// <summary>Put food into the gut. Returns the reward for it (taste of energy).</summary>
    // water in each food, per unit eaten, when fully moist (DESIGN §9.4, Revision K, ecology review): live plants hold
    // less as the soil dries (green grass ~75% water, dry-season grass ~10%), prey and fruit are wet, seed and wood dry
    static readonly float[] FoodWater = { 0.75f, 0.65f, 0.85f, 0.1f, 0.8f, 0.6f, 0.8f, 0.8f, 0.5f, 0.3f, 0.1f, 0.1f, 0.5f, 0.7f, 0.8f };
    static bool LivePlant(int p) => p <= (int)Pool.Root;

    /// <summary>What is going on around organism i (from this step's senses): the translator's context categories.</summary>
    static bool Ctx(ReadOnlySpan<float> f, int c) => c switch
    {
        0 => f[F.FEAR] > 0.3f || (f[F.O1_ARMED] > 0.4f && f[F.O1_CLOSE] > 0.4f && f[F.O1_SIZE] > 0.5f),
        1 => f[F.FOOD_HERE] > 0.5f,
        2 => f[F.MATE_CLOSE] > 0f,
        3 => f[F.HUNGER] > 0.6f,
        4 => f[F.KIN_DENSITY] < 0.05f,
        5 => f[F.THIRST] > 0.6f,
        _ => f[F.MOTHER_FAR] > 0.5f,
    };

    /// <summary>Translator bookkeeping: the context of a call, the usual context, and how a listener answered what it heard.</summary>
    static void Voices(World w, int i, int s, int callK, uint acts)
    {
        var T = w.T;
        var f = new ReadOnlySpan<float>(w.Feat, i * F.N, F.N);
        if ((w.Step & 3) == (ulong)(i & 3))   // the usual context, sampled every 4th step per organism
        {
            T.BaseN[s]++;
            for (int c = 0; c < Telemetry.NCTX; c++) if (Ctx(f, c)) T.BaseCtx[s * Telemetry.NCTX + c]++;
        }
        if (callK >= 0)
        {
            int sk = s * F.NSOUND + callK;
            T.CallN[sk]++;
            for (int c = 0; c < Telemetry.NCTX; c++) if (Ctx(f, c)) T.CallCtx[sk * Telemetry.NCTX + c]++;
        }
        int h = w.IHeard[i];
        bool user = w.IHeardUser[i] != 0;
        if (h == 0 && !user) return;
        // the answer: going toward the call, away from it, stopping, calling back, or carrying on
        float dx = World.Delta1(w.IHeardX[i] - w.X[i]), dy = World.Delta1(w.IHeardY[i] - w.Y[i]), d = MathF.Sqrt(dx * dx + dy * dy);
        float vmax = MathF.Max(w.Vmax(i), 1e-3f), sp = w.ISpeed[i] / vmax;
        float cos = d > 1e-3f ? (MathK.Cos(w.IHeading[i]) * dx + MathK.Sin(w.IHeading[i]) * dy) / d : 0f;
        int r = callK >= 0 ? 3 : sp < 0.1f ? 2 : sp > 0.25f && cos > 0.5f ? 0 : sp > 0.25f && cos < -0.5f ? 1 : 4;
        if (user) { if (T.UserRespId != w.UserCallId) { Array.Clear(T.UserResp); T.UserRespId = w.UserCallId; } T.UserResp[r]++; }
        if (h != 0 && (h - 1) / F.NSOUND == s) T.CallResp[(h - 1) * Telemetry.NRESP + r]++;   // the caller's own clade
    }

    /// <summary>Eat mass of a pool: energy, water from the food and from digesting it, and the reward for both.</summary>
    public static float Ingest(World w, int i, int pool, float mass, float eff, float emax, int cell)
    {
        float e = mass * Pools.Rho[pool] * eff;
        // water from food and from digesting it (metabolic water: how seed-eating desert rodents live without drinking)
        float content = FoodWater[pool] * (LivePlant(pool) ? MathF.Min(1f, w.Moisture[cell] * 1.4f) : 1f);
        float wet = K.K_FOODWATER * (mass * content + K.MET_WATER * e / K.RHO_MEAT) / MathF.Max(w.MassOf(i), 1e-3f);
        float rel = MathF.Min(w.Thirst[i], wet);
        w.Thirst[i] -= rel;
        w.Gut[i] += mass; w.GutEnergy[i] += e; w.GutWaste[i] += mass * (1f - eff);
        if (pool == (int)Pool.Fruit || pool == (int)Pool.Seed) w.GutSeedy[i] += mass * (1f - eff);
        w.T.Intake[w.Spc[i] * Pools.N + pool] += e;
        return K.R_INTAKE * e / emax + K.R_DRINK * rel;
    }

    static float Strike(World w, int a, int v, float gutCap, float emax)
    {
        var dietA = w.DietOf(a);
        uint seed = w.Seed, step = (uint)w.Step;
        float ma = w.MassOf(a);
        float power = w.PStrike[a] * ma;
        bool egg = w.StageOf[v] == St.Egg;
        // the victim's own body resists (DEF_BASE per unit mass) and armour adds to it; an egg's shell is its
        // inherited armour; a juvenile's tissues are softer (DESIGN §9.4, Revision H)
        float defence = w.ApparentMass(v) * (K.DEF_BASE + w.PArmour[v]) * (w.StageOf[v] == St.Juvenile ? K.JUV_DEFENCE : 1f);
        float p = K.P0_STRIKE * power / (power + defence + 1e-6f)
                  * (egg ? 1f : (1f + 0.8f * w.Fatigue[v]) * (1f - 0.5f * w.IScanning[v]) / (1f + K.C_CONF * w.IKinDensity[v]));
        float rew = 0;
        int cell = World.FIdx(w.X[v], w.Y[v]);
        bool water = (w.Cell[cell] & (byte)Realm.Water) != 0;
        if (Rng.U(seed, step, (uint)a, Rs.Strike) >= p)
        {
            w.Pain[v] = MathF.Min(1f, w.Pain[v] + K.PAIN_HIT);
            w.PendingR[v] -= K.R_PAIN * 0.05f;
            // a victim hits back with its own weapons (passive tissue and eggs do not)
            float back = egg ? 0f : w.PStrike[v] * w.ApparentMass(v);
            if (back > power)
            {
                float hurt = K.C_RETAL * (back - power);
                w.Energy[a] -= hurt;
                rew -= K.R_PAIN * hurt / emax;
            }
            w.T.Fight(w.X[v], w.Y[v], w.Spc[a], w.Spc[v], Telemetry.F_MISS, w.Step, w.Serial[a], w.Serial[v]);
            return rew;
        }
        float space = MathF.Max(0f, gutCap - w.Gut[a]);
        bool drainHit = w.PDrain[a] > 0f && Rng.U(seed, step, (uint)a, Rs.Strike + 5u) < w.PDrain[a];
        if (!drainHit)
        {
            // a hit wounds in proportion to the weapon's force; the victim dies when its reserve runs out
            float dmg = K.C_DAMAGE * power;
            w.Energy[v] -= dmg;
            w.Pain[v] = MathF.Min(1f, w.Pain[v] + K.PAIN_HIT);
            w.PendingR[v] -= K.R_PAIN * dmg / MathF.Max(w.EmaxOf(v), 1e-3f);
            if (w.Energy[v] > 0) { w.T.Fight(w.X[v], w.Y[v], w.Spc[a], w.Spc[v], Telemetry.F_WOUND, w.Step, w.Serial[a], w.Serial[v]); return rew; }
        }
        float ev = MathF.Max(0f, w.Energy[v]);
        if (drainHit)
        {
            float drain = MathF.Min(K.C_DRAIN * MathK.Pow(w.PM[a], K.EXP_BASAL), ev);
            w.Energy[v] -= drain;
            w.Pain[v] = MathF.Min(1f, w.Pain[v] + 0.3f);
            w.PendingR[v] -= K.R_PAIN * drain / MathF.Max(w.EmaxOf(v), 1e-3f);
            float m = MathF.Min(space, drain / K.RHO_BLOOD);
            if (m > 0) rew += Ingest(w, a, (int)Pool.Blood, m, dietA[(int)Pool.Blood], emax, cell);
            bool dead = w.Energy[v] <= 0;
            w.T.Fight(w.X[v], w.Y[v], w.Spc[a], w.Spc[v], dead ? Telemetry.F_DRAINED : Telemetry.F_BITE, w.Step, w.Serial[a], w.Serial[v]);
            if (dead) { w.T.Kills[w.Spc[a]]++; w.T.KillMatrix[w.Spc[a] * w.NS + w.Spc[v]]++; Die(w, v, Cause.Parasite, w.Spc[a]); }
            return rew;
        }
        // whole-body kill: eat what fits as meat, the rest becomes carrion (or detritus in water)
        float carcass = K.C_CARC * w.MassOf(v) + ev / K.RHO_MEAT;
        float eat = MathF.Min(space, carcass);
        if (eat > 0 && dietA[(int)Pool.Meat] > 0) { rew += Ingest(w, a, (int)Pool.Meat, eat, dietA[(int)Pool.Meat], emax, cell); carcass -= eat; }
        w.T.Kills[w.Spc[a]]++;   // only a death counts as a kill (a blood meal is not one)
        w.T.KillMatrix[w.Spc[a] * w.NS + w.Spc[v]]++;
        w.T.Fight(w.X[v], w.Y[v], w.Spc[a], w.Spc[v], egg ? Telemetry.F_EGG : Telemetry.F_KILL, w.Step, w.Serial[a], w.Serial[v]);
        DieNoBody(w, v, Cause.Killed, w.Spc[a]);
        w.Pool[water ? (int)Pool.Detritus : (int)Pool.Carrion][cell] += carcass;
        w.T.KillFlash(cell);
        return rew;
    }

    /// <summary>
    /// Sexual reproduction (DESIGN §8, §7.9): two ready organisms of a compatible sex system. Both pay the cost of mating;
    /// whether young result depends on how alike their genes are, p = exp(-(d/D_COMPAT)^2), so close relatives
    /// breed freely, diverged ones rarely, and species separate gradually. Hermaphrodites both conceive.
    /// </summary>
    static float Mate(World w, int i, int j, int cell)
    {
        if (!Decide.Ready(w, i) || w.StageOf[j] == St.Dead || !Decide.ValidPartner(w, i, j)) return 0;
        float dx = World.Delta1(w.X[j] - w.X[i]), dy = World.Delta1(w.Y[j] - w.Y[i]);
        float rr = w.PReach[i] * 2f + 0.5f;
        if (dx * dx + dy * dy > rr * rr) return 0;
        if (w.FreeTop == 0) { w.T.SlotShortage++; if (w.T.LastFullStep != w.Step) { w.T.LastFullStep = w.Step; w.T.FullSteps++; } return 0; }     // no room for young: nothing happens, nothing is paid
        w.Energy[i] -= K.C_MATE * w.PM[i]; w.Energy[j] -= K.C_MATE * w.PM[j];
        w.PendingR[j] += K.R_MATE;
        w.T.Matings[w.Spc[i]]++;
        float rew = K.R_MATE;
        bool hi = w.PHerm[i] != 0, hj = w.PHerm[j] != 0, herm = hi && hj;
        // with one hermaphrodite, the separate-sexed partner keeps its role; a hermaphrodite meeting a male is the mother
        int mother = herm ? i : hi ? (w.Sex[j] == 0 ? j : i) : hj ? (w.Sex[i] == 0 ? i : j) : w.Sex[i] == 0 ? i : j;
        int father = mother == i ? j : i;
        // the father has only a short refractory period; a mother's breeding cooldown starts when she conceives (Conceive)
        w.Cooldown[father] = K.REFRACT * w.PCool[father];
        // male function costs a hermaphrodite a share of its own litter's cost (both, when two hermaphrodites mate)
        if (w.PHerm[father] != 0) w.Energy[father] -= K.C_SIRE * LitterCost(w, father);
        if (herm) w.Energy[mother] -= K.C_SIRE * LitterCost(w, mother);
        float d = Body.Distance(w, i, j) / K.D_COMPAT;
        if (Rng.U(w.Seed, (uint)w.Step, (uint)i, Rs.Compat) >= MathK.Exp(-d * d * d * d)) return rew;   // incompatible: no young
        if (herm)
        {
            rew += Conceive(w, i, j, w.PLitter[i]);
            w.PendingR[j] += Conceive(w, j, i, w.PLitter[j]);
            return rew;
        }
        float rb = Conceive(w, mother, father, w.PLitter[mother]);
        if (mother == i) rew += rb; else w.PendingR[j] += rb;
        return rew;
    }

    /// <summary>Asexual reproduction (LAY): an asexual organism makes a litter of mutated copies of itself on its own.</summary>
    static float Lay(World w, int i, int cell)
    {
        if (w.PSexual[i] != 0) return 0;
        w.T.Lays[w.Spc[i]]++;
        return Conceive(w, i, -1, w.PLitter[i]);
    }

    /// <summary>
    /// Create a litter at conception (DESIGN §8). father = -1 for asexual copies. Each young costs its starting energy and,
    /// for an egg, its whole tissue (yolk) at once; an embryo's tissue is paid by the mother during gestation. How many
    /// young, how big, and eggs or live birth all come from the mother's genes; her energy limits the litter.
    /// </summary>
    static float Conceive(World w, int mother, int father, int n)
    {
        float birth = w.PBirth[mother];
        float childE = K.F_BIRTH * K.C_EMAX * w.PM[mother] * birth;
        bool live = w.PLive[mother] != 0;
        float tissue = live ? 0f : TissueCost(birth, w.PM[mother]);
        int made = 0;
        for (int k = 0; k < n; k++)
        {
            if (w.Energy[mother] < childE + tissue + 0.15f * w.EmaxOf(mother)) break;
            int c = Life.NewSlot(w);
            if (c < 0) break;
            w.Energy[mother] -= childE + tissue;
            w.T.SpendRepro[w.Spc[mother]] += childE + tissue;
            if (father >= 0) Life.CrossGenome(w, mother, father, c);
            else Life.CopyGenome(w, mother, c, true);
            Life.Place(w, c, w.Spc[mother], live ? St.Embryo : St.Egg, w.X[mother], w.Y[mother], childE, mother);
            w.Timer[c] = w.PGest[mother];
            made++;
        }
        if (made > 0)
        {
            if (live) w.Flags[mother] |= Flag.Carrying;
            w.Cooldown[mother] = w.PCool[mother];
            w.T.Conceptions[w.Spc[mother]] += made;
        }
        return K.R_BIRTH * made;
    }

    static float LitterCost(World w, int m)
        => w.PLitter[m] * (K.F_BIRTH * K.C_EMAX * w.PM[m] * w.PBirth[m] + (w.PLive[m] != 0 ? 0f : TissueCost(w.PBirth[m], w.PM[m])));

    /// <summary>Energy needed to build a newborn's tissue (its birth share of the adult mass).</summary>
    public static float TissueCost(float birthShare, float adultMass) => K.C_CARC * adultMass * birthShare * K.RHO_MEAT / K.GROWTH_EFF;

    /// <summary>Death with a body left behind (carrion on land, detritus in water).</summary>
    public static void Die(World w, int i, int cause, int killer)
    {
        int cell = World.FIdx(w.X[i], w.Y[i]);
        bool water = (w.Cell[cell] & (byte)Realm.Water) != 0;
        float body = K.C_CARC * w.MassOf(i) + MathF.Max(0f, w.Energy[i]) / K.RHO_MEAT;
        w.Pool[water ? (int)Pool.Detritus : (int)Pool.Carrion][cell] += body;
        DieNoBody(w, i, cause, killer);
    }

    public static void DieNoBody(World w, int i, int cause, int killer)
    {
        int s = w.Spc[i];
        w.T.Deaths[s * Cause.N + cause]++;
        byte st = w.StageOf[i];
        int cls = st <= St.Embryo ? 0 : st == St.Juvenile ? 1 : w.Age[i] < 2f * w.PMature[i] ? 2 : 3;
        w.T.DeathsByAge[s * 4 + cls]++;
        if (cls > 0) w.T.AgeAtDeath[s] += w.Age[i];
        if (st >= St.Juvenile) Life.ToArchive(w, i);
        Life.FreeSlot(w, i);
    }
}
