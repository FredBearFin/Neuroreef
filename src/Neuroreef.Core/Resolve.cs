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

    /// <summary>
    /// RESOLVE: every animal that acts, in the order k x orderA + orderB (mod n) for k in [0, n): n a power of two above
    /// every slot in use (World.SlotsUsed) and orderA odd, so each slot below n comes once. The order depends on the slots
    /// used, never on Cap, so a world that grew runs as one built bigger. A slot taken during the step (a newborn) is skipped.
    /// </summary>
    /// <summary>RESOLVE over a power of two above every slot (tests: orderA 1, orderB 0 visits the slots in order).</summary>
    public static void Run(World w, int orderA, int orderB) => Run(w, (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)w.Cap), orderA, orderB);

    public static void Run(World w, int n, int orderA, int orderB)
    {
        int cap = w.Cap, mask = n - 1;
        for (int k = 0; k < n; k++)
        {
            int i = (int)(((uint)k * (uint)orderA + (uint)orderB) & (uint)mask);
            if (i >= cap || w.StageOf[i] < St.Juvenile || (w.Flags[i] & Flag.Newborn) != 0) continue;
            One(w, i);
        }
        cap = w.Cap;   // it may have grown for this step's young
        // what others did to it this step (strikes, matings, a mother's feeding) is credited to this step's action, whether
        // it was resolved before or after them; read inside One it landed in this step or the next by the visiting order
        for (int i = 0; i < cap; i++)
        {
            if (w.PendingR[i] == 0f || w.StageOf[i] < St.Juvenile || (w.Flags[i] & Flag.Newborn) != 0) continue;
            w.Reward[i] += w.PendingR[i]; w.PendingR[i] = 0f;
        }
    }

    static void One(World w, int i)
    {
        var T = w.T;
        var loco = w.LocoOf(i);
        int s = w.Spc[i];
        uint seed = w.Seed, step = (uint)w.Step;
        float mass = w.MassOf(i), emax = K.C_EMAX * mass, mf = mass / w.PM[i];
        var diet = w.DietOf(i);
        float r = 0f;   // what others do to it is added after all have moved (Run)
        float spent = 0f;   // energy spent this step on what it chose to do (strikes, calls, moving, keeping warm), Revision P
        uint acts = w.IActs[i];
        bool rest = (acts & BIT_REST) != 0;
        bool scanning = w.IScanning[i] != 0;
        var metrics = Decide.Metrics;

        // ---- 1. move (realm rule, DESIGN §9.2)
        float h = w.IHeading[i], spd = w.ISpeed[i];
        float nx = w.X[i], ny = w.Y[i];
        float x0 = nx, y0 = ny;   // where its way this step began: a strike or a mating reaches along the way (2026-11-01)
        // a flier lands whenever it can and is not travelling faster than it walks: resting, eating food that is here or
        // drinking water within reach, it lands and walks (or stands) on its legs; it stays in the air only over ground it
        // cannot stand on (2026-10-31). An urge to eat with nothing here to eat does not ground it: it flies on to look
        bool flier = loco == Loco.Fly, airborne = false, canLand = true;
        if (flier)
        {
            int c0 = World.FIdx(nx, ny);
            float vGround = w.GroundVmax(i, c0);
            bool feed = (acts & BIT_EAT) != 0 && w.IFoodHere[i] > 0f, sip = (acts & BIT_DRINK) != 0 && w.DistDrink[c0] <= 1.5f && w.Thirst[i] > 0f;
            bool settle = rest || feed || sip || spd <= vGround;
            canLand = w.GroundAllows(i, c0);
            airborne = !(settle && canLand);
            // one on the ground takes off only if its flight muscle is warm enough and its load light enough (Body.CanTakeOff,
            // 2026-11-01: a cold ectotherm cannot fly until it is warm, a full gut raises its wing loading); in the air its
            // muscle's own heat keeps it warm. Where it cannot stand it is in the air already
            if (airborne && canLand && !w.Airborne(i))
            {
                float muscleT = 1f;
                if (Climate.On)
                {
                    bool wet = (w.Cell[c0] & (byte)Realm.Water) != 0;
                    muscleT = Heat.Rate(Heat.Core(Heat.Operative(w, c0, w.PInsul[i], wet), w.PEndo[i]));
                }
                if (!Body.CanTakeOff(w.PWings[i], w.PM[i], mass, Body.FlightLoad(w, i), muscleT)) airborne = false;
            }
            // in the air its load (and a juvenile's smaller flight muscle) sets how fast it can fly (Body.LoadedTopAirSpeed)
            if (airborne)
            {
                float load = Body.FlightLoad(w, i);
                float vmp = w.PVAir[i] * MathF.Max(0.3f, mf) / Body.TopFlightSpeed(Body.FlightMargin(w.PWings[i], w.PM[i]));
                spd = MathF.Min(spd, Body.LoadedTopAirSpeed(vmp, mass, load, Body.LoadedMargin(w.PWings[i], w.PM[i], mass, load, 1f)));
            }
            if (!airborne) spd = MathF.Min(spd, vGround);
        }
        if (spd > 0 && !Move(w, i, nx, ny, h, spd, !airborne, out nx, out ny, out float fraction))
        {
            spd *= fraction;
            h += Rng.U(seed, step, (uint)i, Rs.Turn) < 0.5f ? MathF.PI * 0.5f : -MathF.PI * 0.5f;
        }
        PathMemory.Integrate(w, i, World.Delta1(nx - w.X[i]), World.Delta1(ny - w.Y[i]));   // its own sense of how it moved (Revision P)
        w.X[i] = nx; w.Y[i] = ny;
        w.Heading[i] = h; w.Speed[i] = spd;
        if (metrics != null && w.Gain[i * P.N + P.SPRINT] > 0.5f && spd > 0f)
            metrics.Add(ref metrics.SprintMoved);
        int cell = World.FIdx(w.X[i], w.Y[i]);
        bool waterCell = (w.Cell[cell] & (byte)Realm.Water) != 0;
        // where in the water column it holds, and the oxygen it draws there (2026-11-01, Respiration.cs)
        var breath = Respiration.Place(w, i, cell, airborne);

        if (w.Fire[cell] > 0 && Rng.U(seed, step, (uint)i, Rs.Death) < K.FIRE_KILL) { Die(w, i, Cause.Fire, -1); return; }

        // ---- 2. drink
        if ((acts & BIT_DRINK) != 0 && w.DistDrink[cell] <= 1.5f && !airborne)
        {
            if (w.Thirst[i] > 0f) metrics?.Allow(P.DRINK);
            float rel = MathF.Min(w.Thirst[i], K.DRINK_RATE);
            w.Thirst[i] -= rel; r += K.R_DRINK * rel;
            if (rel > 0f) { metrics?.Apply(P.DRINK); PathMemory.Drank(w, i); Pathogens.Drank(w, i, cell); }   // waterborne pathogens (Revision R)
        }

        // on the ground a landed flier keeps to where it can stand (World.GroundAllows); for others it is the same rule
        static bool Move(World w, int i, float x, float y, float h, float speed, bool ground, out float nx, out float ny, out float fraction)
        {
            float dx = MathK.Cos(h) * speed, dy = MathK.Sin(h) * speed;
            int cx = (int)(x / K.FCELL), cy = (int)(y / K.FCELL);
            if (!(ground ? w.GroundAllows(i, World.WrapI(cy, Map.FW) * Map.FW + World.WrapI(cx, Map.FW)) : w.CellAllows(i, World.WrapI(cy, Map.FW) * Map.FW + World.WrapI(cx, Map.FW))))
            {
                nx = x; ny = y;
                fraction = 0f;
                return false;
            }

            int sx = Math.Sign(dx), sy = Math.Sign(dy);
            float tx = sx == 0 ? float.PositiveInfinity : (sx > 0 ? (cx + 1) * K.FCELL - x : x - cx * K.FCELL) / MathF.Abs(dx);
            float ty = sy == 0 ? float.PositiveInfinity : (sy > 0 ? (cy + 1) * K.FCELL - y : y - cy * K.FCELL) / MathF.Abs(dy);
            float dtx = sx == 0 ? float.PositiveInfinity : K.FCELL / MathF.Abs(dx);
            float dty = sy == 0 ? float.PositiveInfinity : K.FCELL / MathF.Abs(dy);
            while (true)
            {
                float t = MathF.Min(tx, ty);
                if (t > 1f) break;
                if (tx <= ty) { cx += sx; tx += dtx; }
                if (ty <= t) { cy += sy; ty += dty; }
                int cell = World.WrapI(cy, Map.FW) * Map.FW + World.WrapI(cx, Map.FW);
                if (!(ground ? w.GroundAllows(i, cell) : w.CellAllows(i, cell)))
                {
                    float back = 1e-4f / MathF.Max(speed, 1e-6f);
                    float safe = MathF.Max(0f, t - back);
                    nx = World.Wrap(x + dx * safe); ny = World.Wrap(y + dy * safe);
                    fraction = safe;
                    return false;
                }
            }
            nx = World.Wrap(x + dx); ny = World.Wrap(y + dy);
            fraction = 1f;
            return true;
        }

        // ---- 3. eat (DESIGN §9.3)
        float gutCap = w.PGut[i] * mf;
        var dg = Diag.D; int dgG = dg != null ? Diag.Group(w, i) : 0;   // feeding-chain census (reporting only)
        if (dg != null)
        {
            bool eat = (acts & BIT_EAT) != 0, food = w.IFoodHere[i] > 0.5f;
            dg.Steps[dgG]++; dg.Mass[dgG] += mass; dg.Fill[dgG] += w.Gut[i] / MathF.Max(gutCap, 1e-9f); dg.Hunger[dgG] += 1f - w.Energy[i] / emax;
            if (food) dg.FoodHere[dgG]++;
            if (eat) { dg.EatFired[dgG]++; if (food) dg.EatFiredFood[dgG]++; if (scanning) dg.EatScan[dgG]++; else if (w.Gut[i] >= gutCap) dg.EatFull[dgG]++; else if (airborne) dg.EatAir[dgG]++; }
            else if (food && w.Energy[i] < 0.5f * emax) dg.HungryNoEat[dgG]++;
        }
        // eating another organism is attacking it (DESIGN §9.3, Revision K): the same strike, chosen by hunger
        bool strikeAttempt = (acts & BIT_STRIKE) != 0;
        bool preyEat = (acts & BIT_EAT) != 0 && w.IEatPrey[i] != 0 && (acts & BIT_STRIKE) == 0 && w.PStrike[i] > 0;
        if (preyEat) { acts |= BIT_STRIKE; metrics?.Allow(P.EAT); }
        else if ((acts & BIT_EAT) != 0 && scanning && metrics != null) metrics.Add(ref metrics.ScanBlockedEats);
        if (!preyEat && (acts & BIT_EAT) != 0 && !scanning && w.Gut[i] < gutCap && !airborne)   // a flier eats landed (2026-10-31)
        {
            // which food: the most energy it can actually use this step, a bite no bigger than what its gut passes in a
            // step (Revision P): when the gut is the bottleneck, rich food beats a heap of poor food (optimal diet
            // theory, Charnov 1976). Before, a big heap of dung or litter won and filled the gut with food a third as rich
            // Revision S: it tastes the plants it bites: their defences change what a bite is worth (Plants.Taste)
            // (2026-11-01) what a step's digestion can absorb is bounded in energy too (C_ABSORB): food richer than the gut's
            // transporters can take up is worth no more a step than food that just saturates them, the denser kept on a tie
            int best = -1; float bv = 0, bestEff = 0, bestBM = 1f, bestRho = 0f;
            float pass = w.PDig[i] * MathK.Pow(mf, K.EXP_BASAL), bite0 = w.PBite[i] * MathK.Pow(mf, K.EXP_BASAL);
            float absorb = w.PAbsorb[i] * MathK.Pow(mf, K.EXP_BASAL);
            for (int p = 0; p < Pools.NFIELD; p++)
            {
                float e = diet[p];
                if (e <= 0) continue;
                float em = 1f, bm = 1f;
                if (p <= (int)Pool.Root && !waterCell) { Plants.Taste(w, p, cell, bite0, out em, out bm); e = MathF.Min(e * em, MathF.Max(e, Pools.DigMax[p])); }
                // in water, what it reaches at its depth: plankton by the light, algae and detritus on the bottom (Respiration.Access)
                // food in kg (Pools.Kg, 2026-11-01: a unit of a plant pool is its dry matter per m2 over the cell)
                float here = (waterCell ? w.Pool[p][cell] * Respiration.Access(w, i, cell, p) : w.Pool[p][cell]) * Pools.Kg[p];
                float v = MathF.Min(absorb, e * Pools.Rho[p] * MathF.Min(pass * bm, here));
                if (v > bv || (v == bv && v > 0f && e * Pools.Rho[p] > bestRho)) { bv = v; best = p; bestEff = e; bestBM = bm; bestRho = e * Pools.Rho[p]; }
            }
            if (best < 0 && dg != null) dg.EatNone[dgG]++;
            if (best >= 0)
            {
                float reach = (waterCell ? w.Pool[best][cell] * Respiration.Access(w, i, cell, best) : w.Pool[best][cell]) * Pools.Kg[best];
                float bite = MathF.Min(MathF.Min(bite0 * bestBM, reach), gutCap - w.Gut[i]);
                if (bite > 0)
                {
                    metrics?.Allow(P.EAT); metrics?.Apply(P.EAT);
                    float units = bite / Pools.Kg[best];   // the bite in the pool's units (cover for plants)
                    w.Pool[best][cell] = MathF.Max(0f, w.Pool[best][cell] - units);
                    T.EatenMass[best] += bite;
                    // roots are the herbs' and shrubs' own mass (2026-11-01): a bite of them takes its energy's worth of their cover
                    // (it was 0.3 of the bite from the herbs whatever its energy, so roots, regrowing toward their own capacity, fed
                    // diggers ~4x what the plants lost)
                    if (best == (int)Pool.Root) Plants.RootEaten(w, cell, units * K.RHO_ROOT / K.RHO_PLANT);
                    else if (best == (int)Pool.Nectar) w.Poll[cell] = MathF.Min(1f, w.Poll[cell] + K.POLL_GAIN * units);
                    if (!waterCell) Plants.Eaten(w, i, cell, best, bite, bestEff);   // what it took from the plants, and the seed and pollen it carries
                    if (dg != null) { dg.EatOk[dgG]++; dg.BiteKg[dgG] += bite; dg.BiteE[dgG] += bite * Pools.Rho[best] * bestEff; }
                    r += Ingest(w, i, best, bite, bestEff, emax, cell);
                    Pathogens.Ate(w, i, cell);   // what lies on the food is swallowed with it: the faecal-oral route (Revision R)
                }
            }
        }

        // ---- 4. strike (DESIGN §9.4)
        float e4 = w.Energy[i];   // what the strike section takes: its cost, and a victim striking back
        if ((acts & BIT_STRIKE) != 0 && w.PStrike[i] > 0)
        {
            int v = w.ITarget[i];
            if (v >= 0 && w.StageOf[v] != St.Dead && w.StageOf[v] != St.Embryo)
            {
                if (strikeAttempt) metrics?.Allow(P.STRIKE);
                w.Energy[i] -= K.C_STRIKE * mass;                       // no target, no strike (DESIGN §5)
                spent += K.C_STRIKE * mass;
                T.Strikes[s]++;
                float reachAll = Decide.StrikeReach(w.PReach[i], 0f, MathK.Cbrt(w.ApparentMass(v)));
                // in water they meet only where their depths overlap (Respiration.Overlap; 1 when neither is in water)
                float meet = Respiration.Overlap(w, i, v);
                if (dg != null) { dg.StrTry[dgG]++; dg.Strike[dgG] += K.C_STRIKE * mass; }
                if (AlongWay(w, i, x0, y0, w.X[v], w.Y[v]) <= reachAll * reachAll && (meet >= 1f || Rng.U(seed, step, (uint)i, Rs.Meet) < meet))
                {
                    if (dg != null) { dg.StrReach[dgG]++; if (preyEat) dg.PreyEat[dgG]++; if (w.Spc[v] == s) dg.StrKin[dgG]++; }
                    if (strikeAttempt) metrics?.Apply(P.STRIKE);
                    if (preyEat) metrics?.Apply(P.EAT);
                    float vx = w.X[v], vy = w.Y[v];
                    bool victimAloft = w.Airborne(v);
                    r += Strike(w, i, v, gutCap, emax);
                    // a flier that kills ground prey from the air comes down on it to eat it (2026-11-01): raptors stoop,
                    // kill and feed on the ground; only prey taken in the air, or over water it cannot stand in, is eaten
                    // on the wing. It is landed there, so it pays a take-off (warm muscle, the meal as load) to fly on
                    if (airborne && !victimAloft && w.StageOf[v] == St.Dead)   // killed (its slot freed)
                    {
                        int vc = World.FIdx(vx, vy);
                        if (w.GroundAllows(i, vc)) { PathMemory.Integrate(w, i, World.Delta1(vx - w.X[i]), World.Delta1(vy - w.Y[i])); w.X[i] = vx; w.Y[i] = vy; w.Speed[i] = 0f; }
                    }
                }
            }
            else if (v == Player.Slot && w.Player is { Present: true } pl)
            {
                // EXPLORE: a strike at the person, paid for as any strike; the person is hurt (Player.Struck)
                w.Energy[i] -= K.C_STRIKE * mass;
                spent += K.C_STRIKE * mass;
                T.Strikes[s]++;
                float reachAll = Decide.StrikeReach(w.PReach[i], 0f, pl.MassCbrt);
                if (AlongWay(w, i, x0, y0, pl.X, pl.Y) <= reachAll * reachAll && !pl.Airborne) pl.Struck(w, i);   // not a plane in the air
            }
        }

        float hurt4 = e4 - w.Energy[i] - ((acts & BIT_STRIKE) != 0 && w.PStrike[i] > 0 ? K.C_STRIKE * mass : 0f);
        // a victim that struck back and took the rest of its reserve killed it (2026-11-01: it was counted as starved at
        // the end of the step; measured: most 'starved' juveniles died so, with food in their gut)
        if (hurt4 > 1e-6f && w.Energy[i] <= 0f) { int v = w.ITarget[i]; Die(w, i, Cause.Killed, v >= 0 ? w.Spc[v] : -1); return; }
        // ---- 5. mate / lay (DESIGN §8)
        if ((acts & BIT_MATE) != 0 && w.IMate[i] >= 0) r += Mate(w, i, w.IMate[i], cell, x0, y0);
        if ((acts & BIT_LAY) != 0 && Decide.Ready(w, i)) r += Lay(w, i, cell);

        // ---- 6a. voice (Revision M): a sound costs a little energy, and is heard by everyone near on the next step
        float eCall = w.Energy[i];
        int callType = Speak(w, i, acts, mass);
        spent += eCall - w.Energy[i];
        if (dg != null) dg.Call[dgG] += eCall - w.Energy[i];
        if (metrics != null)
            for (int p = P.CALL_0; p < P.N; p++)
            {
                uint bit = 1u << (p - P.FIRST_ACTION);
                if ((acts & bit) == 0) continue;
                if (w.PVoice[i] >= K.VOICE_MIN) metrics.Allow(p);
                if (callType >= 0)
                    for (int k = 0; k < F.MAX_SYL; k++)
                        if (w.Call[i * F.MAX_SYL + k] == p - P.CALL_0 + 1) { metrics.Apply(p); break; }
            }
        Voices(w, i, s, callType, acts);

        // ---- 6. mark and scent, cache
        float carn = w.PDiet[i * Pools.N + (int)Pool.Meat];   // (not diet: a birth above may have grown the world, reallocating PDiet)
        float scent = K.C_SCENT * carn * ((acts & BIT_MARK) != 0 ? K.MARK_GAIN : 1f);
        if ((acts & BIT_MARK) != 0 && carn <= 0) scent = K.C_SCENT * 0.5f;   // anyone can mark, faintly
        float scentBefore = w.Scent[cell];
        if ((acts & BIT_MARK) != 0) metrics?.Allow(P.MARK);
        w.Scent[cell] = MathF.Min(4f, w.Scent[cell] + scent);
        if ((acts & BIT_MARK) != 0 && w.Scent[cell] > scentBefore) metrics?.Apply(P.MARK);
        if ((acts & BIT_CACHE) != 0 && w.GutSeedy[i] > 0 && !waterCell)
        {
            metrics?.Allow(P.CACHE);
            float m = 0.5f * w.GutSeedy[i], frac = m / MathF.Max(w.Gut[i], 1e-6f);
            Plants.Deposit(w, cell, w.GutSeedG[i], w.GutSeedDad[i], m, 1);
            w.Gut[i] -= m; w.GutEnergy[i] *= 1f - frac; w.GutWaste[i] *= 1f - frac; w.GutSeedy[i] -= m;
            metrics?.Apply(P.CACHE);
        }

        if (!waterCell || w.FurSeed[i] > 0f) Plants.Burrs(w, i, cell);   // hooked seed catches in its coat and falls off (Revision S)

        // ---- 7. costs, fatigue, digestion (DESIGN §9.1, §9.3)
        float vmax = MathF.Max(w.Vmax(i), 1e-3f);
        float moveCost = MoveCost(w, i, mass, mf, spd, cell, airborne, canLand);
        // a juvenile runs every unit of its brain from birth, so it pays for all of them; only the body's share scales
        // with its size (audit 2026-10-30: a young brain was charged at body scale; real infant brains cost more, not less)
        float brainBasal = K.COST_BRAIN * K.C_BASAL * w.PBrain[i] / K.BRAIN_REF;
        float fullBasal = (w.PBasal[i] - brainBasal) * MathK.Pow(mf, K.EXP_BASAL) + brainBasal;
        // core temperature (Revision P): a cold-blooded body takes its surroundings' temperature and its chemistry runs
        // slower in the cold (Heat.Rate): it burns less and digests less; a warm-blooded one pays to stay at T_BODY
        bool immersed = waterCell && !airborne;
        float top = K.T_REF, core = K.T_BODY, rateT = 1f;
        if (Climate.On)
        {
            top = Heat.Operative(w, cell, w.PInsul[i], immersed);
            // below the mixed layer the water is colder (Respiration.WaterT), for the share of the step it is down there
            if (immersed && w.ZShare[i] > 0f) top += w.ZShare[i] * (Respiration.WaterT(w, cell, w.Z[i]) - top);
            core = Heat.Core(top, w.PEndo[i]);
            rateT = Heat.Rate(core);
        }
        float basal = fullBasal * rateT * (rest ? K.REST_BASAL : 1f) * (1f + Pathogens.Sickness(w, i));   // the pathogen's take and the immune response (Revision R)
        // growth: a juvenile builds tissue from its reserve (no free mass, DESIGN §9.1). It grows at most at the rate
        // that would reach adult size at PMature, times how full its reserve is (the reserve density of a dynamic energy
        // budget: growth draws on the reserve continuously, Kooijman 2010), keeping GROW_RESERVE of it against
        // starving: a well-fed youngster grows fast, a hungry one slowly, a starving one not at all. (Until Revision P
        // it grew only from energy above half its reserve; the young lived near 40% full and hardly grew.)
        float grow = 0;
        if (w.StageOf[i] == St.Juvenile && w.Grown[i] < 1f)
        {
            float tissue = w.PM[i] * (1f - w.PBirth[i]) * K.TISSUE_E / K.GROWTH_EFF;   // energy to build all of it (TissueCost)
            grow = Math.Clamp(MathF.Min(tissue / w.PMature[i] * w.Energy[i] / emax, w.Energy[i] - K.GROW_RESERVE * emax), 0f, tissue / w.PMature[i]);
            w.Grown[i] = MathF.Min(1f, w.Grown[i] + grow / tissue);
        }
        // body heat (Revision O): the heat it makes (basal and movement) against what its body loses at the operative
        // temperature around it; it shivers when it loses too much and sweats or pants when it loses too little
        float thermo = 0f, evap = 0f, shortCold = 0f, shortHot = 0f;
        if (Climate.On)
        {
            Heat.Balance(mass, w.PInsul[i], w.PAppend[i], top, immersed, basal + moveCost, fullBasal, out thermo, out evap, out shortCold, out shortHot, core);
            // a body kept below T_BODY that makes more heat than it loses just runs a little warmer: it need not shed it
            if (core < K.T_BODY) { evap = 0f; shortHot = 0f; }
            w.TOp[i] = top;
            w.HeatNeed[i] = MathK.Tanh((thermo + shortCold - evap - shortHot) / fullBasal);
            // what it cannot cover strains it; a body recovers from a little strain each step
            w.ThermStrain[i] = MathF.Max(0f, w.ThermStrain[i] + K.STRAIN_GAIN * (shortCold + shortHot) / fullBasal - K.STRAIN_REC);
            T.SpendHeat[s] += thermo;
        }
        // breathing (2026-11-01): what pumping its medium costs, and its oxygen debt from what its organs could not draw
        float gasp0 = Respiration.Breathless(w, i);
        float breathe = Respiration.Breathe(w, i, breath, basal + moveCost + thermo);
        // air hunger hurts (one of the strongest aversive sensations; Banzett et al. 2008): a rising debt is felt as pain is
        r -= K.R_PAIN * MathF.Max(0f, Respiration.Breathless(w, i) - gasp0);
        w.Energy[i] -= basal + moveCost + grow + thermo + breathe;
        T.SpendBasal[s] += basal + breathe; T.SpendMove[s] += moveCost; T.SpendGrowth[s] += grow;
        if (dg != null)
        {
            dg.Basal[dgG] += basal; dg.Move[dgG] += moveCost; dg.Thermo[dgG] += thermo; dg.Breath[dgG] += breathe; dg.Grow[dgG] += grow;
            dg.Kleiber[dgG] += K.C_BASAL * MathK.Pow(mass, K.EXP_BASAL);
            dg.Fmr[dgG] += 4.82f * MathK.Pow(mass * 1000f, 0.734f) * 1000f / K.STEPS_PER_DAY / Pools.EnergyJ;   // mammals' FMR, kJ/day (Nagy 1999)
        }
        float fat0 = w.Fatigue[i];
        float sprint = w.Gain[i * P.N + P.SPRINT];
        if (rest && fat0 > 0f) metrics?.Allow(P.REST);
        w.Fatigue[i] = Math.Clamp(w.Fatigue[i] + K.C_FATIGUE * sprint * spd / vmax - K.C_RECOVER * (rest ? 3f : 1f), 0f, 1f);
        if (rest && w.Fatigue[i] < fat0) metrics?.Apply(P.REST);
        r += K.R_REST * MathF.Max(0f, fat0 - w.Fatigue[i]);

        float dgDe = 0f;   // census: energy absorbed this step
        if (w.Gut[i] > 1e-7f)
        {
            float d = MathF.Min(w.Gut[i], w.PDig[i] * MathK.Pow(mf, K.EXP_BASAL) * rateT);   // a cold gut digests slowly (Revision P)
            // the gut's absorptive capacity (C_ABSORB, 2026-11-01): rich digesta pass only as fast as their energy is taken up
            float absorbE = w.PAbsorb[i] * MathK.Pow(mf, K.EXP_BASAL) * rateT;
            if (dg != null) { if (d >= w.Gut[i]) { } else if (w.GutEnergy[i] * d / w.Gut[i] > absorbE) dg.AbsorbCapped[dgG]++; else dg.DigCapped[dgG]++; }
            if (w.GutEnergy[i] > absorbE) d = MathF.Min(d, w.Gut[i] * absorbE / w.GutEnergy[i]);
            float frac = d / w.Gut[i];
            float de = w.GutEnergy[i] * frac, dung = w.GutWaste[i] * frac, seedy = w.GutSeedy[i] * frac;
            w.Energy[i] += de;
            if (dg != null) { dg.Absorbed[dgG] += de; dgDe = de; }
            w.Gut[i] -= d; w.GutEnergy[i] -= de; w.GutWaste[i] -= dung; w.GutSeedy[i] -= seedy;
            if (waterCell) w.Pool[(int)Pool.Detritus][cell] += dung / Pools.Kg[(int)Pool.Detritus];   // kg into the pool's units
            else w.Pool[(int)Pool.Dung][cell] += dung;
            // the seed that passed the gut whole lands in its dung, with its parents (Revision S)
            Plants.Deposit(w, cell, w.GutSeedG[i], w.GutSeedDad[i], seedy, 0);
        }
        if (w.Energy[i] > emax) w.Energy[i] = emax;

        // an organism dries out on land, less on moist ground; in water it does not (DESIGN §9.4)
        // (2026-11-01) gills out of water dry out fast (Respiration.Drying)
        if (!waterCell) w.Thirst[i] = MathF.Min(1f, w.Thirst[i] + K.C_THIRST * Nature.Thirst * w.PThirstK[i] * Respiration.Drying(w, i) * MathF.Max(0f, 1f - K.SOIL_WATER * w.Moisture[cell]));
        // cooling by evaporation costs water, whatever the kidneys save (Revision O)
        if (evap > 0f && !waterCell) w.Thirst[i] = MathF.Min(1f, w.Thirst[i] + K.EVAP_REL * K.C_THIRST * Nature.Thirst * evap / (K.C_BASAL * mass));
        w.Age[i] += 1;
        if (w.Cooldown[i] > 0) w.Cooldown[i] -= 1;
        w.Pain[i] = MathF.Max(Pathogens.Malaise(w, i), w.Pain[i] - K.PAIN_DECAY);   // a sick animal feels unwell (immune response, damage)
        // energy spent counts against the reward as energy eaten counts for it (Revision P): the learner feels the net
        // gain, so a habit that costs and earns nothing (calling on 65-80% of steps) is not reinforced for free.
        // Basal cost is not counted: nothing it does changes it
        r -= K.R_SPEND * K.R_INTAKE * (spent + moveCost + thermo) / emax;
        w.Reward[i] = r;   // a rise in fear is added when it is sensed, next step (Decide.Run)
        T.RewardSum[s] += r;

        // ---- 8. death
        if (w.Energy[i] <= 0)
        {
            if (dg != null && hurt4 > 1e-6f) dg.StarveHurt[dgG]++;
            if (dg != null) dg.Starved(w, i, dgG, K.C_BASAL * MathK.Pow(mass, K.EXP_BASAL), w.PAbsorb[i] * MathK.Pow(mf, K.EXP_BASAL) * rateT, basal, thermo, moveCost, grow, breathe, brainBasal * rateT, rateT, dgDe, w.PDig[i] * MathK.Pow(mf, K.EXP_BASAL) * rateT * (w.Gut[i] > 1e-9f ? w.GutEnergy[i] / w.Gut[i] : 0f));
            Die(w, i, Cause.Starve, -1); return;
        }
        if (w.Thirst[i] >= 1f) { Die(w, i, Cause.Thirst, -1); return; }
        if (w.ThermStrain[i] >= 1f) { Die(w, i, shortCold > 0f ? Cause.Cold : Cause.Heat, -1); return; }
        if (w.O2Debt[i] >= Respiration.DebtMax(w, i)) { Die(w, i, immersed ? Cause.Drowned : Cause.Suffocated, -1); return; }
        float life = w.PLife[i];
        if (w.Age[i] > life)
        {
            float a = w.Age[i] / life;
            if (Rng.U(seed, step, (uint)i, Rs.Death + 1) < 0.002f * a * a) { Die(w, i, Cause.Old, -1); return; }
        }
    }

    /// <summary>
    /// Energy spent moving this step (2026-10-31). In the air: the power curve of flight, FlightPowerMin(M) x
    /// (3/u + u^3)/4 at u = speed / speed of least power, less the basal metabolism it includes, for the share of the
    /// step it is really in the air: its distance over a whole step's flight (Body.FlightFullStep), so flying costs
    /// FLY_COT_RATIO of walking per distance at cruise; the whole step when it could not land where it was (over
    /// water it cannot stand in, hovering or not). On the ground: the cost of transport ~M^0.7 x speed (Taylor et al.
    /// 1982), of which the stance share (1 - LEG_SWING) every walker pays and the swing share grows with its legs (size
    /// x pairs, against LEG_PAIRS_REF full pairs) and with speed against its top speed there (Marsh et al. 2004).
    /// Swimming has no swing. A heavy coat adds to every kind (Revision O).
    /// </summary>
    public static float MoveCost(World w, int i, float mass, float mf, float spd, int cell, bool airborne, bool canLand = true)
    {
        float coat = 1f + K.INS_MOVE * w.PInsul[i];
        if (airborne)
        {
            // the speed of least power in this world: its top air speed over how far above it power lets it fly (when
            // FLY_V_CAP holds the top speed down, the whole curve is compressed with it)
            // a load (a full gut, young carried inside, Body.FlightLoad) at fixed wings raises the least power as
            // (total / body)^(3/2) and the speed of least power as its square root, so the cost per distance rises in
            // proportion to the weight (induced power ~W^2 / V, V_mp ~W^(1/2): Pennycuick 1975, 2008) (2026-11-01)
            float wl = (mass + Body.FlightLoad(w, i)) / mass, wlRoot = MathK.Pow(wl, 0.5f), pLoad = wl * wlRoot;
            float vmp = w.PVAir[i] * MathF.Max(0.3f, mf) / Body.TopFlightSpeed(Body.FlightMargin(w.PWings[i], w.PM[i])) * wlRoot;
            float pMin = Body.FlightPowerMin(mass) * pLoad;
            float p = pMin * Body.PowerCurve(spd / MathF.Max(vmp, 1e-4f));
            float duty = canLand ? MathF.Min(1f, spd / (Body.FlightFullStep(mass) * wlRoot)) : 1f;
            // a step in the air that starts or ends on the ground has a take-off and climb-out, dearer than cruising
            // (TAKEOFF_S of least power: Pennycuick 2008; Askew, Marsh & Ellington 2001; Nudds & Bryant 2000)
            float takeoff = canLand ? pMin * K.TAKEOFF_S / K.STEP_S : 0f;
            return (MathF.Max(0f, p - K.C_BASAL * MathK.Pow(mass, K.EXP_BASAL)) * duty + takeoff) * coat;
        }
        if (spd <= 0f) return 0f;
        float cot = K.C_MOVE * MathK.Pow(mass, K.EXP_MOVE) * spd * coat;   // C_MOVE: per unit distance (Taylor et al. 1982, 2026-11-01)
        bool swimming = (w.Cell[cell] & (byte)Realm.Water) != 0 && w.Depth[cell] > w.ShallowE;
        if (swimming) return cot;
        float rel = MathF.Min(1f, spd / MathF.Max(w.GroundVmax(i, cell), 1e-4f));
        return cot * (1f - K.LEG_SWING + K.LEG_SWING * w.PLegs[i] * w.PLegPairs[i] / K.LEG_PAIRS_REF * rel);
    }

    /// <summary>Squared distance from (tx, ty) to the way organism i went this step, from (x0, y0) to where it is now
    /// (2026-11-01): a step is hours, so what it passed near on the way it could reach.</summary>
    static float AlongWay(World w, int i, float x0, float y0, float tx, float ty)
        => Decide.PathDist2(World.Delta1(w.X[i] - x0), World.Delta1(w.Y[i] - y0), World.Delta1(tx - x0), World.Delta1(ty - y0));

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

    /// <summary>Translator bookkeeping (not a rule; reads only): the context of a call, the usual context, and how a
    /// listener answered what it heard. The caller's whole sense vector and the listeners' gains are summed for the
    /// open-ended translator (Meaning.cs); the 7 fixed contexts are kept as its fallback.</summary>
    /// <param name="type">the utterance it made this step (Voice type 0..399; a single sound k is type k), -1 none</param>
    public static void Voices(World w, int i, int s, int type, uint acts)
    {
        var T = w.T;
        var f = new ReadOnlySpan<float>(w.Feat, i * F.N, F.N);
        var gain = new ReadOnlySpan<float>(w.Gain, i * P.N, P.N);
        if ((w.Step & 3) == (ulong)(i & 3))   // the usual context, sampled every 4th step per organism
        {
            T.BaseN[s]++;
            for (int c = 0; c < Telemetry.NCTX; c++) if (Ctx(f, c)) T.BaseCtx[s * Telemetry.NCTX + c]++;
            Accumulate(T.BaseFeat, T.BaseFeat2, s * F.N, f);
            Accumulate(T.BaseGain, T.BaseGain2, s * P.N, gain);
        }
        Span<byte> syl = stackalloc byte[F.MAX_SYL];
        if (type >= 0)
        {
            int n = Voice.Decode(type, syl);
            for (int p = 0; p < n; p++)   // each sound of it, for the per-sound view
            {
                int sk = s * F.NSOUND + syl[p] - 1;
                T.CallN[sk]++;
                for (int c = 0; c < Telemetry.NCTX; c++) if (Ctx(f, c)) T.CallCtx[sk * Telemetry.NCTX + c]++;
                Accumulate(T.CallFeat, T.CallFeat2, sk * F.N, f);
            }
            int st = s * Voice.NTYPE + type;   // the utterance as a whole
            long now = (long)w.Step + 1;
            if (T.UttFirst[st] == 0) T.UttFirst[st] = now;
            T.UttLast[st] = now;
            if (++T.UttN[st] == Telemetry.MIN_UTT) T.UttCross[st] = now;
            int tr = T.Track(s);
            if (tr >= 0) { T.UttFeatN[tr * Voice.NTYPE + type]++; Accumulate(T.UttFeat, T.UttFeat2, (tr * Voice.NTYPE + type) * F.N, f); }
        }
        int h = w.IHeard[i];
        bool user = w.IHeardUser[i] != 0;
        if (h == 0 && !user) return;
        // the answer: going toward the call, away from it, stopping, calling back, or carrying on
        float dx = World.Delta1(w.IHeardX[i] - w.X[i]), dy = World.Delta1(w.IHeardY[i] - w.Y[i]), d = MathF.Sqrt(dx * dx + dy * dy);
        float vmax = MathF.Max(w.Vmax(i), 1e-3f), sp = w.ISpeed[i] / vmax;
        float cos = d > 1e-3f ? (MathK.Cos(w.IHeading[i]) * dx + MathK.Sin(w.IHeading[i]) * dy) / d : 0f;
        int r = type >= 0 ? 3 : sp < 0.1f ? 2 : sp > 0.25f && cos > 0.5f ? 0 : sp > 0.25f && cos < -0.5f ? 1 : 4;
        if (user) { if (T.UserRespId != w.UserCallId) { Array.Clear(T.UserResp); T.UserRespId = w.UserCallId; } T.UserResp[r]++; }
        if (h != 0 && (h - 1) / Voice.NTYPE == s)   // its focal caller was of its own clade
        {
            int ht = (h - 1) % Voice.NTYPE, st = s * Voice.NTYPE + ht;
            T.UttResp[st * Telemetry.NRESP + r]++;
            T.UttHeardN[st]++;
            int tr = T.TrackOf[s];
            if (tr >= 0) { T.UttGainN[tr * Voice.NTYPE + ht]++; Accumulate(T.UttGain, T.UttGain2, (tr * Voice.NTYPE + ht) * P.N, gain); }
            int n = Voice.Decode(ht, syl);
            for (int p = 0; p < n; p++)
            {
                int sk = s * F.NSOUND + syl[p] - 1;
                T.CallResp[sk * Telemetry.NRESP + r]++;
                T.HeardN[sk]++;
                Accumulate(T.HeardGain, T.HeardGain2, sk * P.N, gain);
            }
        }
    }

    /// <summary>
    /// The voice (Revision M, N): the utterance in the fired call actions, up to MAX_SYL sounds in the order of how
    /// strongly each was intended, is made into w.Call (heard on the next step) and costs C_CALL x basal x
    /// (1 - CALL_SYL + CALL_SYL x syllables). A mute animal makes no sound and pays nothing. A vocal learner's match to
    /// its template is stored for the learning block. Returns the utterance type (Voice), -1 when silent.
    /// </summary>
    public static int Speak(World w, int i, uint acts, float mass)
    {
        int cb = i * F.MAX_SYL;
        var utt = new Span<byte>(w.Call, cb, F.MAX_SYL);
        int nsyl = w.PVoice[i] < K.VOICE_MIN ? 0 : Voice.Order(w, i, acts, utt);
        if (nsyl == 0) utt.Clear();
        utt.CopyTo(new Span<byte>(w.LastUtt, cb, F.MAX_SYL));   // its own utterance last step: silence too
        float vmatch = 0f;
        if (nsyl > 0)
        {
            w.Energy[i] -= K.C_CALL * K.C_BASAL * MathK.Pow(mass, K.EXP_BASAL) * (1f - K.CALL_SYL + K.CALL_SYL * nsyl);
            // matching its tutors reinforces only in its sensitive period, as song practice does in young songbirds; the
            // adult song is set (Revision P: rewarded for life, voiced animals came to call on ~80% of steps, ~20% of basal)
            if (w.PImit[i] > 0f && w.Evo.VocalLearning && w.Age[i] < w.PImitAge[i] * w.PLife[i]) vmatch = Match(w, i, utt);
        }
        w.HistV[i * Brain.BLOCK + (((int)(w.Step & 0x3FFFFFFF) + i) & (Brain.BLOCK - 1))] = vmatch;   // credited with this step's call outputs
        return nsyl > 0 ? Voice.TypeOf(utt[0], utt[1], utt[2]) : -1;
    }

    /// <summary>
    /// Vocal learning (Revision N): how well an utterance matches the learner's remembered tutors, by form only. Each of
    /// its syllable pairs (start -> first, first -> second, second -> third) scores its template count against the
    /// template's largest; the match is their mean, less the learner's running mean of it, so only doing better than
    /// usual reinforces. 0 while it has no template. Nothing about what was going on is used (Doupe & Kuhl 1999;
    /// Brainard & Doupe 2002; Tchernichovski et al. 2001).
    /// </summary>
    public static float Match(World w, int i, ReadOnlySpan<byte> utt)
    {
        int tb = i * Voice.NTMPL, max = 0;
        for (int k = tb, ke = tb + Voice.NTMPL; k < ke; k++) if (w.Tmpl[k] > max) max = w.Tmpl[k];
        if (max == 0) return 0f;
        float sum = w.Tmpl[tb + Voice.Cell(0, utt[0])]; int n = 1;
        for (int p = 1; p < F.MAX_SYL && utt[p] != 0; p++) { sum += w.Tmpl[tb + Voice.Cell(utt[p - 1], utt[p])]; n++; }
        float m = sum / (n * (float)max);
        if (w.VMean[i] == 0f) w.VMean[i] = m;   // the running mean starts at its first match, not at 0 (which rewarded every early call)
        float rv = m - w.VMean[i];
        w.VMean[i] += K.IMIT_RBAR * (m - w.VMean[i]);
        return rv;
    }

    static void Accumulate(double[] sum, double[] sum2, int o, ReadOnlySpan<float> v)
    {
        for (int k = 0; k < v.Length; k++) { double x = v[k]; sum[o + k] += x; sum2[o + k] += x * x; }
    }

    /// <summary>Eat mass of a pool: energy, water from the food and from digesting it, and the reward for both.</summary>
    public static float Ingest(World w, int i, int pool, float mass, float eff, float emax, int cell)
    {
        float e = mass * Pools.Rho[pool] * eff;
        // water from food and from digesting it (metabolic water: how seed-eating desert rodents live without drinking)
        float content = FoodWater[pool] * (LivePlant(pool) ? MathF.Min(1f, w.Moisture[cell] * 1.4f) : 1f);
        float wet = K.K_FOODWATER * (mass * content + K.MET_WATER * e / K.RHO_MEAT) / MathF.Max(w.MassOf(i), 1e-6f);   // (a guard only: a 1 g world's newborns are 0.1 g)
        float rel = MathF.Min(w.Thirst[i], wet);
        w.Thirst[i] -= rel;
        w.Gut[i] += mass; w.GutEnergy[i] += e; w.GutWaste[i] += mass * (1f - eff);
        w.T.Intake[w.Spc[i] * Pools.N + pool] += e;
        // a meal worth FED_WELL steps' need marks a place worth coming back to, plant, meat or blood (Revision P)
        if (e >= K.FED_WELL * K.FIELD_METAB * w.PBasal[i] * MathK.Pow(w.MassOf(i) / w.PM[i], K.EXP_BASAL)) PathMemory.Fed(w, i);
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
        var dg = Diag.D; int dgG = dg != null ? Diag.Group(w, a) : 0;
        if (dg != null) dg.StrP[dgG] += p;
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
                if (dg != null) { dg.RetalN[dgG]++; dg.RetalE[dgG] += hurt; dg.RetalBmr[dgG] += hurt / (K.C_BASAL * MathK.Pow(ma, K.EXP_BASAL)); }
                rew -= K.R_PAIN * hurt / emax;
            }
            w.T.Fight(w.X[v], w.Y[v], w.Spc[a], w.Spc[v], Telemetry.F_MISS, w.Step, w.Serial[a], w.Serial[v]);
            return rew;
        }
        float space = MathF.Max(0f, gutCap - w.Gut[a]);
        bool drainHit = w.PDrain[a] > 0f && Rng.U(seed, step, (uint)a, Rs.Strike + 5u) < w.PDrain[a];
        float e0 = w.Energy[v];
        if (!drainHit)
        {
            // a hit wounds in proportion to the weapon's force against the body that resists it (its defence: size, armour,
            // a juvenile's softness), as a share of a full reserve (WOUND); the victim dies when its reserve runs out.
            // (2026-11-01) It took C_DAMAGE x power whatever the victim, so a matched predator needed ~4 landed hits to kill
            // a fed prey of its own size, the prey's fat standing for its life (measured: 3.9 hits a kill, ~40 strikes)
            float dmg = MathF.Min(1f, power / (defence + 1e-6f)) * K.WOUND * w.EmaxOf(v);
            w.Energy[v] -= dmg;
            w.Pain[v] = MathF.Min(1f, w.Pain[v] + K.PAIN_HIT);
            w.PendingR[v] -= K.R_PAIN * dmg / MathF.Max(w.EmaxOf(v), 1e-3f);
            if (dg != null) dg.StrHit[dgG]++;
            if (w.Energy[v] > 0) { if (dg != null) dg.WoundOnly[dgG]++; w.T.Fight(w.X[v], w.Y[v], w.Spc[a], w.Spc[v], Telemetry.F_WOUND, w.Step, w.Serial[a], w.Serial[v]); return rew; }
        }
        // a kill leaves the victim's reserve in its carcass (2026-11-01: the killing wound took it out of the world)
        float ev = MathF.Max(0f, drainHit ? w.Energy[v] : e0);
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
        // whole-body kill: eat what fits as meat, the rest becomes carrion (or detritus in water). The carcass is its edible
        // tissue at the energy of wet tissue and its reserve (CarcassKg, 2026-11-01)
        float carcass = CarcassKg(w.MassOf(v), ev);
        float eat = MathF.Min(space, carcass);
        if (eat > 0 && dietA[(int)Pool.Meat] > 0) { rew += Ingest(w, a, (int)Pool.Meat, eat, dietA[(int)Pool.Meat], emax, cell); carcass -= eat; if (dg != null) { dg.StrMeatKg[dgG] += eat; dg.StrMeatE[dgG] += eat * K.RHO_MEAT * dietA[(int)Pool.Meat]; } }
        if (dg != null) dg.StrKill[dgG]++;
        w.T.Kills[w.Spc[a]]++;   // only a death counts as a kill (a blood meal is not one)
        w.T.KillMatrix[w.Spc[a] * w.NS + w.Spc[v]]++;
        w.T.Fight(w.X[v], w.Y[v], w.Spc[a], w.Spc[v], egg ? Telemetry.F_EGG : Telemetry.F_KILL, w.Step, w.Serial[a], w.Serial[v]);
        DieNoBody(w, v, Cause.Killed, w.Spc[a]);
        { int dp = water ? (int)Pool.Detritus : (int)Pool.Carrion; w.Pool[dp][cell] += carcass / Pools.Kg[dp]; }   // kg into the pool's units
        w.T.KillFlash(cell);
        return rew;
    }

    /// <summary>
    /// Sexual reproduction (DESIGN §8, §7.9): two ready organisms of a compatible sex system. Both pay the cost of mating;
    /// whether young result depends on how alike their genes are, p = exp(-(d/D_COMPAT)^2), so close relatives
    /// breed freely, diverged ones rarely, and species separate gradually. Hermaphrodites both conceive.
    /// </summary>
    static float Mate(World w, int i, int j, int cell, float x0, float y0)
    {
        if (!Decide.Ready(w, i) || w.StageOf[j] == St.Dead || !Decide.ValidPartner(w, i, j)) return 0;
        // both close in: within the sum of their reaches of the way i went this step (2026-11-01; was 2 x i's reach + 0.5 units of where it ended)
        float rr = w.PReach[i] + w.PReach[j];
        if (AlongWay(w, i, x0, y0, w.X[j], w.Y[j]) > rr * rr) return 0;
        if (w.FreeTop == 0 && !w.Grow(1)) { Life.Full(w); return 0; }     // no room for young (the world at its memory limit): nothing happens, nothing is paid
        Decide.Metrics?.Allow(P.MATE);
        w.Energy[i] -= K.C_MATE * w.PM[i]; w.Energy[j] -= K.C_MATE * w.PM[j];
        w.PendingR[j] += K.R_MATE;
        w.T.Matings[w.Spc[i]]++;
        Decide.Metrics?.Apply(P.MATE);
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
        Decide.Metrics?.Allow(P.LAY);
        w.T.Lays[w.Spc[i]]++;
        float reward = Conceive(w, i, -1, w.PLitter[i]);
        if (reward > 0f) Decide.Metrics?.Apply(P.LAY);
        return reward;
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
    /// (2026-11-01) The whole body is built, bone, gut and skin with the muscle, at the energy of wet tissue (TISSUE_E,
    /// ~7 kJ/g; Peters 1983) over GROWTH_EFF: ~9 MJ a kilogram, as measured costs of growth are (~1.3x the energy laid
    /// down; Brody 1945). It was C_CARC x RHO_MEAT, 24 MJ a kilogram, so growth and young cost 2.5x what they do.
    public static float TissueCost(float birthShare, float adultMass) => adultMass * birthShare * K.TISSUE_E / K.GROWTH_EFF;

    /// <summary>The food a body is when it dies, in kg of meat (RHO_MEAT): its edible tissue (C_CARC x mass at TISSUE_E)
    /// and its reserve (2026-11-01: the tissue was C_CARC x mass kg of meat, 5x its energy).</summary>
    public static float CarcassKg(float mass, float reserve) => (K.C_CARC * mass * K.TISSUE_E + MathF.Max(0f, reserve)) / K.RHO_MEAT;

    /// <summary>Death with a body left behind (carrion on land, detritus in water).</summary>
    public static void Die(World w, int i, int cause, int killer)
    {
        int cell = World.FIdx(w.X[i], w.Y[i]);
        bool water = (w.Cell[cell] & (byte)Realm.Water) != 0;
        float body = CarcassKg(w.MassOf(i), w.Energy[i]);
        { int dp = water ? (int)Pool.Detritus : (int)Pool.Carrion; w.Pool[dp][cell] += body / Pools.Kg[dp]; }   // kg into the pool's units
        DieNoBody(w, i, cause, killer);
    }

    public static void DieNoBody(World w, int i, int cause, int killer)
    {
        int s = w.Spc[i];
        if (Diag.D is { } dg && w.StageOf[i] >= St.Juvenile)
        {
            int g = Diag.Group(w, i);
            dg.Deaths[g * Cause.N + cause]++;
            if (cause == Cause.Starve)
            {
                if (w.IFoodHere[i] > 0.5f) dg.StarveFood[g]++;
                dg.StarveGut[g] += w.Gut[i] / MathF.Max(w.PGut[i] * w.MassOf(i) / w.PM[i], 1e-9f);
                if (w.StageOf[i] == St.Juvenile) dg.StarveJuv[g]++; else if (w.Age[i] < 2f * w.PMature[i]) dg.StarveYoungAdult[g]++;
            }
        }
        w.T.Deaths[s * Cause.N + cause]++;
        byte st = w.StageOf[i];
        int cls = st <= St.Embryo ? 0 : st == St.Juvenile ? 1 : w.Age[i] < 2f * w.PMature[i] ? 2 : 3;
        w.T.DeathsByAge[s * 4 + cls]++;
        if (cls > 0) w.T.AgeAtDeath[s] += w.Age[i];
        if (st >= St.Juvenile) Life.ToArchive(w, i);
        Pathogens.OnDeath(w, i);   // its pathogens stay where it died (Revision R)
        Life.FreeSlot(w, i);
    }
}
