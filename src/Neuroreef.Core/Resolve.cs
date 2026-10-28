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
        var sp = w.Sp[w.Spc[i]];
        var body = w.StageBody(i);
        var T = w.T;
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
        if (spd > 0 && !w.CellAllows(nc, body.Realm))
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
        if ((acts & BIT_DRINK) != 0 && body.KThirst > 0 && w.DistWater[cell] <= 1.5f)
        {
            float rel = MathF.Min(w.Thirst[i], K.DRINK_RATE);
            w.Thirst[i] -= rel; r += K.R_DRINK * rel;
        }

        // ---- 3. eat (DESIGN §9.3)
        float gutCap = w.PGut[i] * mf;
        if ((acts & BIT_EAT) != 0 && !scanning && w.Gut[i] < gutCap && !(waterCell && body.Loco == Loco.Fly && (w.Cell[cell] & (byte)Realm.Deep) != 0))
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
                    r += Ingest(w, i, best, bite, diet[best], emax);
                }
            }
        }

        // ---- 4. strike (DESIGN §9.4)
        if ((acts & BIT_STRIKE) != 0 && w.PStrike[i] > 0)
        {
            int v = w.ITarget[i];
            w.Energy[i] -= K.C_STRIKE * mass;
            T.Strikes[s]++;
            if (v >= 0 && w.StageOf[v] != St.Dead && w.StageOf[v] != St.Embryo)
            {
                float dx = World.Delta1(w.X[v] - w.X[i]), dy = World.Delta1(w.Y[v] - w.Y[i]);
                float reachAll = w.PReach[i] + 0.5f + 0.4f * MathK.Cbrt(w.ApparentMass(v));
                if (dx * dx + dy * dy <= reachAll * reachAll) r += Strike(w, i, v, gutCap, emax);
            }
        }

        // ---- 5. mate / lay (DESIGN §8)
        if ((acts & BIT_MATE) != 0 && w.IMate[i] >= 0) r += Mate(w, i, w.IMate[i], cell);
        if ((acts & BIT_LAY) != 0 && Decide.Ready(w, i)) r += Lay(w, i, cell);

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
        float moveCost = K.C_MOVE * mass * spd * spd * (body.Loco == Loco.Fly ? K.FLY_COST : 1f);
        float basal = w.PBasal[i] * MathK.Pow(mf, K.EXP_BASAL) * (rest ? K.REST_BASAL : 1f);
        // growth: a juvenile builds tissue from energy (no free mass, DESIGN §9.1)
        float grow = 0;
        if (w.StageOf[i] == St.Juvenile && w.Age[i] < w.PMature[i])
            grow = K.C_CARC * w.PM[i] * (1f - sp.Birth) / w.PMature[i] * K.RHO_MEAT / K.GROWTH_EFF;
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

        w.Thirst[i] = MathF.Min(1f, w.Thirst[i] + K.C_THIRST * body.KThirst);
        w.Age[i] += 1;
        if (w.Cooldown[i] > 0) w.Cooldown[i] -= 1;
        w.Pain[i] = MathF.Max(0f, w.Pain[i] - K.PAIN_DECAY);
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
    static float Ingest(World w, int i, int pool, float mass, float eff, float emax)
    {
        float e = mass * Pools.Rho[pool] * eff;
        w.Gut[i] += mass; w.GutEnergy[i] += e; w.GutWaste[i] += mass * (1f - eff);
        if (pool == (int)Pool.Fruit || pool == (int)Pool.Seed) w.GutSeedy[i] += mass * (1f - eff);
        w.T.Intake[w.Spc[i] * Pools.N + pool] += e;
        return K.R_INTAKE * e / emax;
    }

    static float Strike(World w, int a, int v, float gutCap, float emax)
    {
        var sa = w.Sp[w.Spc[a]]; var sv = w.Sp[w.Spc[v]];
        var dietA = w.DietOf(a);
        uint seed = w.Seed, step = (uint)w.Step;
        float ma = w.MassOf(a);
        float power = w.PStrike[a] * ma;
        bool egg = w.StageOf[v] == St.Egg;
        float defence = egg ? 0f : w.PArmour[v] * w.ApparentMass(v) * (sv.Swarm ? 20f : 1f) * (w.StageOf[v] == St.Juvenile ? K.JUV_DEFENCE : 1f);
        float p = egg ? 0.9f
            : K.P0_STRIKE * power / (power + defence + 1e-6f) * (1f + 0.8f * w.Fatigue[v]) * (1f - 0.5f * w.IScanning[v])
              / (1f + K.C_CONF * w.IKinDensity[v]);
        float rew = 0;
        int cell = World.FIdx(w.X[v], w.Y[v]);
        bool water = (w.Cell[cell] & (byte)Realm.Water) != 0;
        if (Rng.U(seed, step, (uint)a, Rs.Strike) >= p)
        {
            w.Pain[v] = MathF.Min(1f, w.Pain[v] + K.PAIN_HIT);
            w.PendingR[v] -= K.R_PAIN * 0.05f;
            if (defence > power)
            {
                float hurt = K.C_RETAL * (defence - power);
                w.Energy[a] -= hurt;
                rew -= K.R_PAIN * hurt / emax;
            }
            return rew;
        }
        w.T.Kills[w.Spc[a]]++;
        float ev = MathF.Max(0f, w.Energy[v]);
        float space = MathF.Max(0f, gutCap - w.Gut[a]);
        if (sa.Parasite)
        {
            float drain = MathF.Min(K.C_DRAIN * MathK.Pow(w.PM[a], K.EXP_BASAL), ev);
            w.Energy[v] -= drain;
            w.Pain[v] = MathF.Min(1f, w.Pain[v] + 0.3f);
            w.PendingR[v] -= K.R_PAIN * drain / MathF.Max(w.EmaxOf(v), 1e-3f);
            float m = MathF.Min(space, drain / K.RHO_BLOOD);
            if (m > 0) rew += Ingest(w, a, (int)Pool.Blood, m, dietA[(int)Pool.Blood], emax);
            if (w.Energy[v] <= 0) { w.T.KillMatrix[w.Spc[a] * w.NS + w.Spc[v]]++; Die(w, v, Cause.Parasite, w.Spc[a]); }
            return rew;
        }
        if (sv.Swarm && !egg)
        {
            float chunkE = MathF.Min(K.SWARM_CHUNK * ev, space * K.RHO_MEAT);
            w.Energy[v] -= chunkE;
            w.Pain[v] = MathF.Min(1f, w.Pain[v] + 0.3f);
            w.PendingR[v] -= K.R_PAIN * chunkE / MathF.Max(w.EmaxOf(v), 1e-3f);
            float m = chunkE / K.RHO_MEAT;
            if (m > 0 && dietA[(int)Pool.Meat] > 0) rew += Ingest(w, a, (int)Pool.Meat, m, dietA[(int)Pool.Meat], emax);
            else if (m > 0) w.Pool[water ? (int)Pool.Detritus : (int)Pool.Carrion][cell] += m;
            if (w.Energy[v] < 0.05f * w.EmaxOf(v)) { w.T.KillMatrix[w.Spc[a] * w.NS + w.Spc[v]]++; Die(w, v, Cause.Killed, w.Spc[a]); }
            return rew;
        }
        // whole-body kill: eat what fits as meat, the rest becomes carrion (or detritus in water)
        float carcass = K.C_CARC * w.MassOf(v) + ev / K.RHO_MEAT;
        float eat = MathF.Min(space, carcass);
        if (eat > 0 && dietA[(int)Pool.Meat] > 0) { rew += Ingest(w, a, (int)Pool.Meat, eat, dietA[(int)Pool.Meat], emax); carcass -= eat; }
        w.T.KillMatrix[w.Spc[a] * w.NS + w.Spc[v]]++;
        DieNoBody(w, v, Cause.Killed, w.Spc[a]);
        w.Pool[water ? (int)Pool.Detritus : (int)Pool.Carrion][cell] += carcass;
        w.T.KillFlash(cell);
        return rew;
    }

    static bool ModeAllowsSex(World w, int i, Species sp)
    {
        return sp.Mode switch
        {
            Repro.Sexual or Repro.Hermaphrodite => true,
            Repro.Facultative => w.IFoodHere[i] <= K.FACULT_FOOD,
            Repro.Cyclic => w.SeasonAt(w.Y[i]) <= 0f,
            _ => false,
        };
    }

    static bool ModeAllowsAsex(World w, int i, Species sp)
    {
        return sp.Mode switch
        {
            Repro.Fission or Repro.Spores => true,
            Repro.Facultative => w.IFoodHere[i] > K.FACULT_FOOD,
            Repro.Cyclic => w.SeasonAt(w.Y[i]) > 0f,
            _ => false,
        };
    }

    static bool EggSiteOk(World w, Species sp, int cell)
    {
        if (sp.Medium == Medium.WaterEgg) return w.DistWater[cell] <= 1.5f;
        if (sp.Medium == Medium.LandEgg) return (w.Cell[cell] & (byte)Realm.Land) != 0;
        return true;
    }

    static float Mate(World w, int i, int j, int cell)
    {
        var sp = w.Sp[w.Spc[i]];
        if (!Decide.Ready(w, i) || w.StageOf[j] == St.Dead || !Decide.ValidPartner(w, i, j) || !ModeAllowsSex(w, i, sp)) return 0;
        if (!EggSiteOk(w, sp, cell)) return 0;
        float dx = World.Delta1(w.X[j] - w.X[i]), dy = World.Delta1(w.Y[j] - w.Y[i]);
        float rr = w.PReach[i] * 2f + 0.5f;
        if (dx * dx + dy * dy > rr * rr) return 0;
        w.Energy[i] -= K.C_MATE * w.PM[i]; w.Energy[j] -= K.C_MATE * w.PM[j];
        w.Cooldown[i] = sp.Cooldown; w.Cooldown[j] = sp.Cooldown;
        w.PendingR[j] += K.R_MATE;
        w.T.Matings[w.Spc[i]]++;
        float rew = K.R_MATE;
        bool resting = sp.Mode == Repro.Cyclic;   // sexual reproduction of cyclic species gives resting eggs
        if (sp.Mode == Repro.Hermaphrodite)
        {
            int half = (w.PLitter[i] + 1) / 2;
            rew += Conceive(w, i, j, half, cell, resting);
            w.PendingR[j] += Conceive(w, j, i, half, World.FIdx(w.X[j], w.Y[j]), resting);
        }
        else
        {
            int mother = w.Sex[i] == 0 ? i : j, father = mother == i ? j : i;
            float rb = Conceive(w, mother, father, w.PLitter[mother], World.FIdx(w.X[mother], w.Y[mother]), resting);
            if (mother == i) rew += rb; else w.PendingR[j] += rb;
        }
        return rew;
    }

    static float Lay(World w, int i, int cell)
    {
        var sp = w.Sp[w.Spc[i]];
        if (!ModeAllowsAsex(w, i, sp)) return 0;
        w.T.Lays[w.Spc[i]]++;
        if (sp.Mode == Repro.Fission)
        {
            int c = Life.NewSlot(w);
            if (c < 0) return 0;
            float e = w.Energy[i] * K.FISSION_SHARE, tissue = TissueCost(sp, w.PM[i]);
            if (e < tissue * 1.2f) { Life.FreeSlot(w, c); return 0; }
            w.Energy[i] -= e;
            e -= tissue;
            Life.CopyGenome(w, i, c, true);
            Life.Place(w, c, w.Spc[i], St.Juvenile, w.X[i] + 1f, w.Y[i], e, i);
            w.T.Births[w.Spc[i]]++;
            w.Cooldown[i] = sp.Cooldown;
            return K.R_BIRTH;
        }
        if (sp.Mode == Repro.Spores)
        {
            // the spore lands in the best of 4 hashed cells within SPORE_RADIUS by dead matter
            int bestC = cell; float bestV = -1;
            for (int k = 0; k < 4; k++)
            {
                int dx = (int)(Rng.U(w.Seed, (uint)w.Step, (uint)i, Rs.Spore + (uint)k * 2) * (2 * K.SPORE_RADIUS + 1)) - K.SPORE_RADIUS;
                int dy = (int)(Rng.U(w.Seed, (uint)w.Step, (uint)i, Rs.Spore + (uint)k * 2 + 1) * (2 * K.SPORE_RADIUS + 1)) - K.SPORE_RADIUS;
                int c2 = World.WrapI(cell / K.FW + dy, K.FW) * K.FW + World.WrapI(cell % K.FW + dx, K.FW);
                if ((w.Cell[c2] & (byte)Realm.Land) == 0) continue;
                float v = w.Pool[(int)Pool.Wood][c2] + w.Pool[(int)Pool.Litter][c2] + w.Pool[(int)Pool.Carrion][c2];
                if (v > bestV) { bestV = v; bestC = c2; }
            }
            int c = Life.NewSlot(w);
            if (c < 0) return 0;
            float e = K.F_BIRTH * K.C_EMAX * w.PM[i] * sp.Birth, tissue = TissueCost(sp, w.PM[i]);
            if (w.Energy[i] < (e + tissue) * 1.5f) { Life.FreeSlot(w, c); return 0; }
            w.Energy[i] -= e + tissue;
            Life.CopyGenome(w, i, c, true);
            float px = (bestC % K.FW + 0.5f) * K.FCELL, py = (bestC / K.FW + 0.5f) * K.FCELL;
            Life.Place(w, c, w.Spc[i], St.Egg, px, py, e, i);
            w.Timer[c] = sp.Gestation;
            w.Cooldown[i] = sp.Cooldown;
            return K.R_BIRTH;
        }
        // parthenogenesis (facultative, cyclic): asexual litter of clones (mutated)
        return Conceive(w, i, -1, w.PLitter[i], cell, false);
    }

    /// <summary>Create a litter at conception (DESIGN §8). father = -1 for asexual clones.</summary>
    static float Conceive(World w, int mother, int father, int n, int cell, bool resting)
    {
        var sp = w.Sp[w.Spc[mother]];
        // the brood energy of the founder litter is shared among the actual litter (quantity vs quality)
        float childE = MathF.Max(0.1f, K.F_BIRTH * sp.Litter / MathF.Max(1, w.PLitter[mother])) * K.C_EMAX * w.PM[mother] * sp.Birth;
        // eggs carry their whole tissue (yolk) when laid; an embryo's tissue is paid by the mother during gestation
        bool liveBirth = sp.Medium == Medium.Live || (father < 0 && sp.Mode is Repro.Facultative or Repro.Cyclic);
        float tissue = liveBirth ? 0f : TissueCost(sp, w.PM[mother]);
        int made = 0;
        bool live = liveBirth;
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
            w.Timer[c] = resting ? K.RESTING_MAX : sp.Gestation;
            if (resting) w.Flags[c] |= Flag.Resting;
            made++;
        }
        if (made > 0)
        {
            if (live) w.Flags[mother] |= Flag.Carrying;
            w.Cooldown[mother] = sp.Cooldown;
            w.T.Conceptions[w.Spc[mother]] += made;
        }
        return K.R_BIRTH * made;
    }

    /// <summary>Energy needed to build a newborn's tissue (the species' birth share of the adult mass).</summary>
    public static float TissueCost(Species sp, float adultMass) => K.C_CARC * adultMass * sp.Birth * K.RHO_MEAT / K.GROWTH_EFF;

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
