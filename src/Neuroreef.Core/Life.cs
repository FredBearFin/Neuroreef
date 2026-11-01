// Slots, genomes and the life cycle (DESIGN §7.3, §7.4, §8; phase 4 LIFECYCLE).

using System;

namespace Neuroreef;

public static class Life
{
    /// <summary>A free slot for a newborn or founder, or -1 if the world is full. A world with no free slot grows here
    /// (World.Grow: births are serial, in RESOLVE and founding), so it is full only at its memory limit (or MaxCap).</summary>
    public static int NewSlot(World w)
    {
        if (w.FreeTop == 0 && !w.Grow(1)) { Full(w); return -1; }
        int c = w.FreeStack[--w.FreeTop];
        w.AliveCount++;
        if (c >= w.SlotsUsed) w.SlotsUsed = c + 1;
        return c;
    }

    /// <summary>A birth or mating refused for want of a slot: the world is at its limit (counted per step for the reports).</summary>
    public static void Full(World w)
    {
        w.T.SlotShortage++;
        if (w.T.LastFullStep != w.Step) { w.T.LastFullStep = w.Step; w.T.FullSteps++; }
    }

    public static void FreeSlot(World w, int i)
    {
        w.StageOf[i] = St.Dead;
        w.Flags[i] = 0;
        w.FreeStack[w.FreeTop++] = i;
        w.AliveCount--;
    }

    /// <summary>Initialise a newly created slot. The genome must already be in place.</summary>
    public static void Place(World w, int c, int species, byte stage, float x, float y, float energy, int mother)
    {
        uint seed = w.Seed, step = (uint)w.Step;
        w.Spc[c] = (byte)species; w.StageOf[c] = stage;
        Array.Clear(w.Call, c * F.MAX_SYL, F.MAX_SYL); Array.Clear(w.LastUtt, c * F.MAX_SYL, F.MAX_SYL);
        Array.Clear(w.Tmpl, c * Voice.NTMPL, Voice.NTMPL);   // a newborn remembers no tutor yet
        w.IHeard[c] = 0; w.IHeardUser[c] = 0; w.IFocal[c] = -1; w.IFocalSerial[c] = 0; w.CallR[c] = 0;
        w.Flags[c] = Flag.Newborn;
        w.X[c] = World.Wrap(x); w.Y[c] = World.Wrap(y);
        w.Heading[c] = Rng.U(seed, step, (uint)c, Rs.Place) * 2f * MathF.PI;
        w.Speed[c] = 0;
        w.Energy[c] = energy; w.Gut[c] = 0; w.GutEnergy[c] = 0; w.GutWaste[c] = 0; w.GutSeedy[c] = 0;
        w.Grown[c] = 0;
        w.Thirst[c] = 0; w.Fatigue[c] = 0; w.Age[c] = 0; w.Pain[c] = 0; w.Cooldown[c] = 0; w.Timer[c] = 0; w.LastFear[c] = 0;
        w.ThermStrain[c] = 0; w.HeatNeed[c] = 0; w.TOp[c] = K.T_REF;
        w.Z[c] = 0; w.ZShare[c] = 0; w.O2Debt[c] = 0;   // it starts breathing freely, at the surface (Respiration.cs)
        PathMemory.Clear(w, c);   // a newborn remembers no place yet (Revision P)
        // sex: the mother's sex-ratio gene gives the chance of a daughter (0 = female)
        float pFemale = mother >= 0 && w.PHerm[mother] == 0 ? w.PSexRatio[mother] : 0.5f;
        w.Sex[c] = (byte)(Rng.U(seed, step, (uint)c, Rs.Sex) < pFemale ? 0 : 1);
        w.Serial[c] = w.NextSerial++;
        w.Children[c] = 0; w.Matured[c] = 0; w.LastChildSlot[c] = -1; w.LastChildSerial[c] = 0;
        w.IActs[c] = 0; w.ITarget[c] = -1; w.IMate[c] = -1; w.IScanning[c] = 0; w.ISpeed[c] = 0; w.IHeading[c] = w.Heading[c];
        Pathogens.Clear(w, c); w.CareLeft[c] = 0; w.CareStep[c] = 0;
        Plants.ClearAnimal(w, c);
        if (mother >= 0)
        {
            w.MotherSlot[c] = mother; w.MotherSerial[c] = w.Serial[mother];
            w.Gen[c] = (ushort)Math.Min(65535, w.Gen[mother] + 1);
            w.Children[mother]++; w.LastChildSlot[mother] = c; w.LastChildSerial[mother] = w.Serial[c];
        }
        else { w.MotherSlot[c] = -1; w.MotherSerial[c] = 0; w.Gen[c] = 0; }
        Array.Clear(w.Feat, c * F.N, F.N);
        Body.Express(w, c);
        Brain.InitLife(w, c);
        w.T.Created[species]++;
    }

    public static void CopyGenome(World w, int from, int to, bool mutate)
    {
        w.GenomeOf(from).CopyTo(w.GenomeOf(to));
        if (mutate) Brain.Mutate(w.GenomeOf(to), w.Seed, (uint)w.Step, (uint)to + 0x10000u, w.Evo);
    }

    public static void CrossGenome(World w, int mother, int father, int child)
    {
        Brain.Crossover(w.GenomeOf(child), w.GenomeOf(mother), w.GenomeOf(father), w.Seed, (uint)w.Step, (uint)child);
        Brain.Mutate(w.GenomeOf(child), w.Seed, (uint)w.Step, (uint)child + 0x10000u, w.Evo);
    }

    /// <summary>Fitness for the archive: offspring that grew up count most (DESIGN §7.4).</summary>
    public static float Score(World w, int i) => K.SCORE_MATURED * w.Matured[i] + w.Children[i] + 0.1f * MathF.Min(1f, w.Age[i] / w.PLife[i]);

    /// <summary>Insert the genome of a dying animal into its species' elite archive if it beats the weakest (DESIGN §7.4).</summary>
    public static void ToArchive(World w, int i)
    {
        float score = Score(w, i);
        if (score < 0.05f) return;
        ArchiveInsert(w, w.Spc[i], w.GenomeOf(i), score);
    }

    /// <summary>Put a genome into a species' archive if it beats the weakest entry (also used by migration between branches).</summary>
    public static void ArchiveInsert(World w, int s, ReadOnlySpan<sbyte> genome, float score)
    {
        int n = w.ArchiveCount[s], baseI = s * K.ARCHIVE, slot;
        if (n < K.ARCHIVE) { slot = n; w.ArchiveCount[s] = n + 1; }
        else
        {
            slot = 0; float worst = w.ArchiveScore[baseI];
            for (int k = 1; k < K.ARCHIVE; k++) if (w.ArchiveScore[baseI + k] < worst) { worst = w.ArchiveScore[baseI + k]; slot = k; }
            if (score <= worst) return;
        }
        w.ArchiveScore[baseI + slot] = score;
        genome.CopyTo(new Span<sbyte>(w.Archive, (baseI + slot) * G.LEN, G.LEN));
    }

    /// <summary>A random cell suitable for the given realm (hashed tries).</summary>
    /// <summary>
    /// A home range for a founding lineage (Revision N, the user: each ancestor starts in its own part of the map):
    /// of HOME_TRIES random sites its realm allows, the one farthest from every home already taken (torus distance).
    /// </summary>
    public static bool PickHome(World w, Realm realm, uint key, System.Collections.Generic.List<(float x, float y)> taken, out float hx, out float hy)
    {
        hx = hy = 0; float best = -1;
        for (int t = 0; t < K.HOME_TRIES; t++)
        {
            if (!RandomSite(w, realm, key + 0x9000u + (uint)t * 131u, out float x, out float y)) continue;
            float near = float.MaxValue;
            foreach (var (tx, ty) in taken) { float dx = World.Delta1(x - tx), dy = World.Delta1(y - ty); near = MathF.Min(near, dx * dx + dy * dy); }
            if (near > best) { best = near; hx = x; hy = y; }
        }
        if (best < 0) return false;
        taken.Add((hx, hy));
        return true;
    }

    /// <summary>A site its realm allows within the home range (radius r, by default HOME_R x map side) around (hx, hy); wider if none is found.</summary>
    public static bool HomeSite(World w, Realm realm, float hx, float hy, uint key, out float x, out float y, float r = 0f)
    {
        if (r <= 0f) r = K.HOME_R * Map.W;
        for (int t = 0; t < 96; t++)
        {
            float a = Rng.U(w.Seed, (uint)w.Step, key, Rs.Place + 300u + (uint)t * 2) * 2f * MathF.PI;
            float d = MathF.Sqrt(Rng.U(w.Seed, (uint)w.Step, key, Rs.Place + 301u + (uint)t * 2)) * r * (1f + t / 32);
            x = World.Wrap(hx + MathK.Cos(a) * d); y = World.Wrap(hy + MathK.Sin(a) * d);
            if (w.CellAllows(World.FIdx(x, y), realm)) return true;
        }
        return RandomSite(w, realm, key, out x, out y);
    }

    /// <summary>
    /// How many animals like organism c a home range of radius r around (hx, hy) can feed: the sustained energy yield of
    /// the cells its realm allows (Fields.Yield, for its diet), over what one of them burns in a day of living
    /// (FIELD_METAB x its basal cost, brain included). None if its gut cannot pass enough of the best of those foods to
    /// pay for a day even when there is plenty (digestion rate x energy x efficiency below the need).
    /// </summary>
    /// <param name="habitat">the realm the lineage is founded in (2026-11-01): a lineage founded in water must feed and
    /// breathe in water, so only those of its cells count (Realm.Any: every cell its locomotion allows)</param>
    public static float RangeCapacity(World w, int c, float hx, float hy, float r, Realm habitat = Realm.Any)
    {
        // where it can forage is where it moves well: a swimmer in water, a walker on land, a wader on both and in
        // the shallows, a flier anywhere (a swimmer can crawl ashore, but cannot live there)
        var realm = (Loco)w.PLoco[c] switch
        {
            Loco.Swim => Realm.Water,
            Loco.Walk => Realm.Land,
            Loco.Wade => Realm.Land | Realm.Shallow,
            _ => w.RealmOf(c),
        } & habitat;
        if (realm == 0) return 0f;
        var diet = w.DietOf(c);
        float need = K.FIELD_METAB * MathF.Max(w.PBasal[c], 1e-6f);
        // a brain the body cannot pay for: more than BRAIN_SHARE_MAX of its basal cost
        float brainCost = K.COST_BRAIN * K.C_BASAL * w.PBrain[c] / K.BRAIN_REF;
        if (brainCost > K.BRAIN_SHARE_MAX * w.PBasal[c]) return 0f;
        float cs = Map.W / (float)Map.FW;
        int rc = (int)MathF.Ceiling(r / cs), cx = (int)(hx / cs), cy = (int)(hy / cs);
        double y = 0, heatNeed = 0, breathCost = 0, actSum = 0; int n = 0;
        Span<double> stock = stackalloc double[Pools.NFIELD];
        Span<float> reached = stackalloc float[Pools.N];
        for (int dy = -rc; dy <= rc; dy++)
            for (int dx = -rc; dx <= rc; dx++)
            {
                if ((dx * dx + dy * dy) * cs * cs > r * r) continue;
                int cell = World.WrapI(cy + dy, Map.FW) * Map.FW + World.WrapI(cx + dx, Map.FW);
                if (!w.CellAllows(c, cell, realm)) continue;
                // only where it can breathe, and in water only the food it reaches at the depth it would hold (2026-11-01)
                if (!Respiration.Breathable(w, c, cell, out float z, out float share, out float bc)) continue;
                breathCost += bc;   // ventilation, osmoregulation and buoyancy there (2026-11-01)
                for (int p = 0; p < Pools.N; p++) reached[p] = diet[p] * Respiration.Access(w, cell, p, z, share);
                y += Fields.Yield(w, cell, reached); n++;
                for (int p = 0; p < Pools.NFIELD; p++) stock[p] += w.Pool[p][cell] * Pools.Kg[p] * Respiration.Access(w, cell, p, z, share);   // kg
                if (Climate.On)
                {
                    heatNeed += DayNeed(w, c, cell);
                    bool imm = w.Cell[cell] != (byte)Realm.Land && (Loco)w.PLoco[c] != Loco.Fly;
                    actSum += Heat.Rate(Heat.Core(Heat.Operative(w, cell, w.PInsul[c], imm), w.PEndo[c]));   // the gut's pace: the core's temperature
                }
            }
        if (n == 0) return 0f;
        // with a climate, a day here costs what this body pays at the temperature around it (Revision P): its basal
        // cost at its core temperature, and the heat it must make to keep its core where its endothermy holds it
        if (Climate.On) need = MathF.Max(1e-6f, (float)(heatNeed / n));
        need += (float)(breathCost / n);   // what breathing there costs (Respiration.Cost): gills in water, lungs at depth
        // the food the animals already living in the range make (2026-11-01): their production as prey, their bodies
        // as carrion and their dung (AnimalFood). Before, only growing plant food counted, so an ancestor that eats
        // mostly meat, carrion or dung was never founded: no prey existed when it was tested
        var an = AnimalFood(w, c, hx, hy, r, realm, diet, need);
        // how fast it can eat: one bite a step, of what stands in a cell once grazed to half (the stock of the
        // maximum sustained yield), and no faster than its gut passes food (a Holling type II limit). It must
        // cover FORAGE_MARGIN days of need, as a naive forager spends most steps doing other things than eating.
        // (2026-11-01) A kind of food counts only if it could live on it so: plant food (and what falls from plants and
        // animals into the water), prey, carrion and dung each. Before, the energy of every food was counted once any one
        // passed, so a meat-eater could be founded on plants its gut cannot pass fast enough to live on
        float margin = K.FORAGE_MARGIN * need, rate = 0f;
        // its gut passes and absorbs food at its core temperature's pace (2026-11-01: a cold gut digests slowly, as in
        // RESOLVE), whatever the food
        float act = Climate.On ? (float)(actSum / n) : 1f;
        float dig = w.PDig[c] * act, absorb = w.PAbsorb[c] * act;
        for (int p = 0; p < Pools.NFIELD; p++)
        {
            if (diet[p] <= 0 || !Fields.Grows(p)) continue;
            float pass = MathF.Min(MathF.Min(w.PBite[c], dig), 0.5f * (float)(stock[p] / n));
            rate = MathF.Max(rate, MathF.Min(absorb, diet[p] * Pools.Rho[p] * pass));   // no more than its gut absorbs (C_ABSORB)
        }
        double fed = rate >= margin ? y + an.Detritus : 0;
        // a kill goes into the gut whole, as much of it as fits (Resolve.Strike), and passes as fast as the gut digests
        if (an.MeatKg > 0f && MathF.Min(absorb, diet[(int)Pool.Meat] * K.RHO_MEAT * MathF.Min(MathF.Min(dig, w.PGut[c]), an.MeatKg)) >= margin)
            fed += an.Meat;
        // carrion and dung are bitten from what lies about: their stock where nothing eats them is inflow over rot
        // (per cell of the range), and a forager finds it half eaten, as plants are
        if (an.CarrionKg > 0 && MathF.Min(absorb, diet[(int)Pool.Carrion] * K.RHO_CARRION * MathF.Min(MathF.Min(w.PBite[c], dig), 0.5f * (float)(an.CarrionKg / K.ROT_CARRION / n))) >= margin)
            fed += an.Carrion;
        if (an.DungKg > 0 && MathF.Min(absorb, diet[(int)Pool.Dung] * K.RHO_DUNG * MathF.Min(MathF.Min(w.PBite[c], dig), 0.5f * (float)(an.DungKg / K.ROT_DUNG / n))) >= margin)
            fed += an.Dung;
        return (float)(fed / need);
    }

    /// <summary>
    /// The body mass (kg) organism j makes in a step, from its own life history (2026-11-01): a juvenile grows the rest of
    /// its adult mass over its maturation (PMature); an adult that bears young makes a litter of its birth share of its mass
    /// each breeding cycle (gestation and cooldown); a male, nothing of its own. This is a fed animal's rate: the
    /// production of a population below its carrying capacity, as the founders are.
    /// </summary>
    public static float BodyMade(World w, int j)
    {
        if (w.StageOf[j] == St.Juvenile) return (1f - w.PBirth[j]) * w.PM[j] / MathF.Max(1f, w.PMature[j]);
        if (w.PSexual[j] != 0 && w.PHerm[j] == 0 && w.Sex[j] != 0) return 0f;
        return w.PLitter[j] * w.PBirth[j] * w.PM[j] / MathF.Max(1f, w.PGest[j] + w.PCool[j]);
    }

    /// <summary>What the animals in a range make as food for one organism (Life.AnimalFood): energy per step from live
    /// prey, carrion on land, dung on land and the carcasses and dung that fall into the water as detritus; and the
    /// mean edible carcass of its catchable prey (kg) and the carrion and dung inflow on land (kg per step).</summary>
    public struct AnimalYield { public double Meat, Carrion, Dung, Detritus, CarrionKg, DungKg; public float MeatKg; }

    /// <summary>
    /// The energy per step organism c could take, year in and year out, from the animals already living within r of
    /// (hx, hy) in cells it can live and breathe in (2026-11-01). Each animal's production is the body it makes from its
    /// own life history (BodyMade), at TISSUE_E a kilogram.
    /// <list type="bullet">
    /// <item>Live prey: it must strike at all and digest meat. It can take a prey while its strike power (strike x mass,
    /// Resolve.Strike) is not less than the prey's counterblow (its strike x its mass), so a miss does not wound it:
    /// all of a weaker prey's production, and of a stronger one the share it makes while still small enough (power over
    /// the adult's counterblow, as the counterblow grows with the body). It eats, at its meat efficiency, the intake that
    /// would make TROPHIC_EFF of that production into its own production at its own production efficiency (the young a
    /// population of adults like it bears, against that plus its day's need: 1-5% for the founders' bodies, as mammals'
    /// 1-3%; Begon, Townsend &amp; Harper, Ecology), and never more than the edible tissue (CarcassKg) of that production.</item>
    /// <item>Carrion: every body dies at last, and in a steady population as much dies as is made (production over
    /// biomass equals the mortality rate; Allen 1971): the edible tissue of what it makes falls as carrion on land,
    /// detritus in water (Resolve.Die), counted at its inflow as plant litter is (Plants.Yield).</item>
    /// <item>Dung: what each animal's gut leaves of the food that pays its day (FIELD_METAB x basal, eaten at its best
    /// food's energy and efficiency), dung on land, detritus in water, at its inflow.</item>
    /// </list>
    /// Nothing is counted for an animal of c's own clade.
    /// </summary>
    public static AnimalYield AnimalFood(World w, int c, float hx, float hy, float r, Realm realm, ReadOnlySpan<float> diet, float need)
    {
        var a = new AnimalYield();
        double preyKg = 0, preyProd = 0, preyEdible = 0, nPrey = 0;
        float power = w.PStrike[c] * w.PM[c];
        bool strikes = w.PStrike[c] > 0f && diet[(int)Pool.Meat] > 0f;
        int clade = w.Spc[c];
        for (int j = 0; j < w.Cap; j++)
        {
            if (j == c || w.StageOf[j] < St.Juvenile || w.Spc[j] == clade) continue;
            float dx = World.Delta1(w.X[j] - hx), dy = World.Delta1(w.Y[j] - hy);
            if (dx * dx + dy * dy > r * r) continue;
            int cell = World.FIdx(w.X[j], w.Y[j]);
            if (!w.CellAllows(c, cell, realm) || !Respiration.Breathable(w, c, cell)) continue;
            bool water = (w.Cell[cell] & (byte)Realm.Water) != 0;
            float made = BodyMade(w, j);                    // body mass it makes a step, kg
            float madeKg = Resolve.CarcassKg(made, 0f);     // the edible tissue of it, in kg of meat
            if (strikes)
            {
                float back = w.PStrike[j] * w.PM[j];
                float share = back <= power ? 1f : power / back;   // the share of its growth it makes while weaker than c
                preyProd += share * K.TISSUE_E * made;
                preyEdible += share * K.RHO_MEAT * madeKg;
                preyKg += share * Resolve.CarcassKg(share * w.MassOf(j), 0f); nPrey += share;
            }
            // what is made dies at last (in a steady population deaths match production): carrion on land, detritus in water
            if (water) a.Detritus += diet[(int)Pool.Detritus] * K.RHO_DETRITUS * madeKg;
            else { a.Carrion += diet[(int)Pool.Carrion] * K.RHO_CARRION * madeKg; a.CarrionKg += madeKg; }
            // its dung: the food that pays its day, at its best food's energy and efficiency, less what it absorbs
            var dj = w.DietOf(j);
            float best = 0f, bestEff = 0f;
            for (int p = 0; p < Pools.N; p++) if (dj[p] * Pools.Rho[p] > best) { best = dj[p] * Pools.Rho[p]; bestEff = dj[p]; }
            if (best <= 0f) continue;
            float waste = K.FIELD_METAB * w.PBasal[j] / best * (1f - bestEff);   // kg per step
            if (water) a.Detritus += diet[(int)Pool.Detritus] * K.RHO_DETRITUS * waste;
            else { a.Dung += diet[(int)Pool.Dung] * K.RHO_DUNG * waste; a.DungKg += waste; }
        }
        if (nPrey > 0)
        {
            a.MeatKg = (float)(preyKg / nPrey);
            // its own production per step: a population of adults like it, half of them mothers if the sexes are separate
            float mothers = w.PSexual[c] != 0 && w.PHerm[c] == 0 ? 0.5f : 1f;
            double own = K.TISSUE_E * mothers * w.PLitter[c] * w.PBirth[c] * w.PM[c] / MathF.Max(1f, w.PGest[c] + w.PCool[c]);
            double eps = K.TROPHIC_EFF * preyProd * (own + need) / own;      // the intake that makes TROPHIC_EFF of theirs its own
            a.Meat = Math.Min(eps, diet[(int)Pool.Meat] * preyEdible);
        }
        return a;
    }

    /// <summary>
    /// The smallest group of organism c's kind that can breed (2026-11-01): one if it reproduces alone (asexual, LAY),
    /// else a pair (separate sexes, or hermaphrodites, which here still mate with a partner: Resolve.Mate). A lineage can
    /// be founded by so few: single pairs and single gravid females found island populations (the Daphne Major "Big Bird"
    /// lineage of Darwin's finches from one immigrant male and local females; Lamichhaney et al. 2018), though most such
    /// small groups die out by chance (Allee effects, demographic stochasticity; Courchamp et al. 1999). With separate
    /// sexes the founders' sexes are drawn at random (Life.Place), so a pair can be two of one sex.
    /// </summary>
    public static int BreedingGroup(World w, int c) => w.PSexual[c] == 0 ? 1 : 2;

    /// <summary>A day of living for organism c in a cell: FIELD_METAB x basal at its core temperature, plus the heat it must make (Revision P).</summary>
    static float DayNeed(World w, int c, int cell)
    {
        bool immersed = w.Cell[cell] != (byte)Realm.Land && (Loco)w.PLoco[c] != Loco.Fly;
        float top = Heat.Operative(w, cell, w.PInsul[c], immersed), core = Heat.Core(top, w.PEndo[c]);
        float basal = w.PBasal[c] * Heat.Activity(core, w.PEndo[c]);   // its core's temperature and metabolic level (2026-11-01)
        Heat.Balance(w.PM[c], w.PInsul[c], w.PAppend[c], top, immersed, basal, w.PBasal[c], out float thermo, out _, out float cold, out _, core);
        cold += Heat.Freeze(w.PM[c], w.PInsul[c], w.PAppend[c], immersed, core);   // a frost a cold-blooded body cannot survive
        return K.FIELD_METAB * basal + thermo + cold;   // heat it cannot make counts as a cost too: it must not live there
    }

    /// <summary>
    /// The home range of a founding lineage like organism c (Revision P): from HOME_R of the map side, widened step by
    /// step up to HOME_R_MAX until it can feed, at FOUND_FILL, the founders a lineage may start with (founders, by default
    /// the world's Founders; 2026-11-01: it was FOUND_MIN = 50, so a range stopped widening once it fed 50 at a quarter
    /// of its capacity, and a meat-eater's range stayed as small as fed 50). A big body needs a big range, as home
    /// ranges grow about in proportion to body mass (Kelt &amp; Van Vuren 2001), and a predator's about ten times a plant
    /// eater's of its size, from scarcer food (Jetz et al. 2004). (2026-11-01) A range that feeds none is widened too:
    /// a meat-eater's prey live in the other lineages' ranges, and it fed none in its own. Returns the radius; cap its capacity.
    /// </summary>
    /// <param name="habitat">the realm the lineage is founded in (RangeCapacity; 2026-11-01)</param>
    public static float RangeFor(World w, int c, float hx, float hy, out float cap, int founders = 0, Realm habitat = Realm.Any)
    {
        if (founders <= 0) founders = w.Evo.Founders;
        float r = K.HOME_R * Map.W;
        cap = RangeCapacity(w, c, hx, hy, r, habitat);
        while (K.FOUND_FILL * cap < founders && r < K.HOME_R_MAX * Map.W)
        {
            r = MathF.Min(r * 1.25f, K.HOME_R_MAX * Map.W);
            cap = RangeCapacity(w, c, hx, hy, r, habitat);
        }
        return r;
    }

    /// <summary>Radius of the home range of a body of mass m kg, in world units (2026-11-01): a circle of HR_KM2 x M km2
    /// (home range ~M^1.0, Kelt &amp; Van Vuren 2001; Jetz et al. 2004). 1.8 units (98 m) at 1 kg, 11 units (620 m) at
    /// 40 kg. (The founding range of a whole lineage, RangeFor, is a population's range, not this.)</summary>
    public static float HomeRangeR(float m) => MathF.Sqrt(K.HR_KM2 * m / MathF.PI) * 1000f / K.M_PER_UNIT;

    public static bool RandomSite(World w, Realm realm, uint key, out float x, out float y)
    {
        for (int t = 0; t < 64; t++)
        {
            x = Rng.U(w.Seed, (uint)w.Step, key, Rs.Place + 100u + (uint)t * 2) * Map.W;
            y = Rng.U(w.Seed, (uint)w.Step, key, Rs.Place + 101u + (uint)t * 2) * Map.W;
            if (w.CellAllows(World.FIdx(x, y), realm)) return true;
        }
        x = y = 0; return false;
    }

    static bool RandomSite(World w, int i, Realm realm, uint key, out float x, out float y)
    {
        for (int t = 0; t < 64; t++)
        {
            x = Rng.U(w.Seed, (uint)w.Step, key, Rs.Place + 100u + (uint)t * 2) * Map.W;
            y = Rng.U(w.Seed, (uint)w.Step, key, Rs.Place + 101u + (uint)t * 2) * Map.W;
            if (w.CellAllows(i, World.FIdx(x, y), realm)) return true;
        }
        x = y = 0; return false;
    }

    /// <summary>
    /// Create one founder organism of a clade at step 0 (or from a gene bank). With an empty genome, a random brain
    /// and a random land ancestor's body (tests). With a group centre (gx, gy), it is placed within the home range of its
    /// body around it (HomeRangeR), as a group shares a range and populations are clustered. The genome given is mutated, so founders vary.
    /// </summary>
    public static int Found(World w, int clade, ReadOnlySpan<sbyte> genome, bool adult, uint key, float gx = float.NaN, float gy = 0f, Realm habitat = Realm.Any)
    {
        int c = NewSlot(w);
        if (c < 0) return -1;
        if (genome.IsEmpty)
        {
            Brain.RandomGenome(w.GenomeOf(c), w.Seed, (uint)w.Step, key);
            Clades.AncestorBody(w.GenomeOf(c).Slice(G.BODY, G.NBODY), w.Seed, clade % (K.ANCESTORS_LAND + K.ANCESTORS_WATER), true);
            Clades.VaryBody(w.GenomeOf(c).Slice(G.BODY, G.NBODY), w.Seed, key);
        }
        else { genome.CopyTo(w.GenomeOf(c)); Brain.Mutate(w.GenomeOf(c), w.Seed, (uint)w.Step, key + 0x20000u, w.Evo); }
        Body.Express(w, c);
        var realm = w.RealmOf(c) & habitat;
        if (realm == 0) { FreeSlot(w, c); return -1; }
        float x = 0, y = 0;
        bool ok = false;
        float spread = HomeRangeR(w.PM[c]);
        if (!float.IsNaN(gx))
            for (int t = 0; t < 16 && !ok; t++)
            {
                float a = Rng.U(w.Seed, (uint)w.Step, key, Rs.Place + 200u + (uint)t * 2) * 2f * MathF.PI;
                float d = Rng.U(w.Seed, (uint)w.Step, key, Rs.Place + 201u + (uint)t * 2) * spread;
                x = World.Wrap(gx + MathK.Cos(a) * d); y = World.Wrap(gy + MathK.Sin(a) * d);
                ok = w.CellAllows(c, World.FIdx(x, y), realm);
            }
        if (!ok && !RandomSite(w, c, realm, key, out x, out y)) { FreeSlot(w, c); return -1; }
        Place(w, c, clade, adult ? St.Adult : St.Juvenile, x, y, 0f, -1);
        w.Energy[c] = K.C_EMAX * w.PM[c] * (0.5f + 0.3f * Rng.U(w.Seed, (uint)w.Step, key, Rs.Place + 7));
        if (adult) { w.Age[c] = w.PMature[c] * (1f + 0.5f * Rng.U(w.Seed, (uint)w.Step, key, Rs.Place + 9)); w.Grown[c] = 1f; }
        else { w.Age[c] = w.PMature[c] * 0.8f * Rng.U(w.Seed, (uint)w.Step, key, Rs.Place + 9); w.Grown[c] = w.Age[c] / w.PMature[c]; }
        return c;
    }

    /// <summary>LIFECYCLE phase: eggs and embryos, hatching, metamorphosis, disease (Pathogens.Host), parental care, the pathogen reservoir. Serial. Nothing is
    /// ever added here: every run, prehistory or live, is births and deaths only after its founders (NR-313).</summary>
    public static void Run(World w, int[] activeCount)
    {
        int cap = w.Cap;
        Array.Clear(activeCount);
        for (int i = 0; i < cap; i++)
        {
            byte st = w.StageOf[i];
            if (st == St.Dead) continue;
            w.Flags[i] &= unchecked((byte)~Flag.Newborn);
            if (st == St.Embryo)
            {
                int m = w.MotherOf(i);
                if (m < 0) { Resolve.DieNoBody(w, i, Cause.Orphan, -1); continue; }
                w.X[i] = w.X[m]; w.Y[i] = w.Y[m];
                // the mother builds the embryo's tissue during gestation; a starving mother loses it
                // an embryo develops at its mother's core temperature (2026-11-01): a warm-blooded mother's at full pace,
                // a cold-blooded one's slower in the cold (Gillooly et al. 2002); its tissue is built at the same pace
                float dev = Climate.On ? Heat.Rate(Heat.Core(w.TOp[m], w.PEndo[m])) : 1f;
                float cost = dev * Resolve.TissueCost(w.PBirth[m], w.PM[m]) / MathF.Max(1f, w.PGest[m]);
                if (w.Energy[m] < cost + 0.1f * w.EmaxOf(m)) { Resolve.DieNoBody(w, i, Cause.Orphan, -1); continue; }
                w.Energy[m] -= cost;
                w.T.SpendRepro[w.Spc[m]] += cost;
                w.Timer[i] -= dev;
                if (w.Timer[i] <= 0) { Hatch(w, i); ClearCarrying(w, m); }
                continue;
            }
            if (st == St.Egg)
            {
                int cell = World.FIdx(w.X[i], w.Y[i]);
                if (w.Fire[cell] > 0) { Resolve.Die(w, i, Cause.Fire, -1); continue; }
                if (Climate.On)
                {
                    // an egg develops at its own temperature (2026-11-01; Gillooly et al. 2002): the water's or the air's, warmed
                    // toward T_BODY by a warm-blooded mother that broods (her endothermy x her care), while she lives; it
                    // freezes below T_FREEZE. (Before, every egg hatched on a fixed timer, so warmth bought nothing.)
                    float amb = (w.Cell[cell] & (byte)Realm.Water) != 0 ? Climate.Water(w.Temp[cell]) : w.Temp[cell];
                    int mo = w.MotherOf(i);
                    float brood = mo >= 0 ? w.PEndo[mo] * w.PCare[mo] : 0f;
                    float te = amb < K.T_BODY ? amb + brood * (K.T_BODY - amb) : amb;
                    if (te < K.T_FREEZE) { Resolve.Die(w, i, Cause.Cold, -1); continue; }
                    w.Timer[i] -= Heat.Rate(te);
                }
                else w.Timer[i] -= 1;
                if (w.Timer[i] <= 0) Hatch(w, i);
                continue;
            }
            int si = w.Spc[i];
            if (Pathogens.Host(w, i, si)) continue;   // died of disease (Revision R)
            if (st == St.Juvenile) Care(w, i, si);
            activeCount[si]++;
            if (st == St.Juvenile && w.Grown[i] >= 1f)              // mature on reaching adult size
            {
                w.StageOf[i] = St.Adult;
                int m = w.MotherOf(i);
                if (m >= 0) w.Matured[m]++;
            }
        }
        Pathogens.Field(w);   // the environmental reservoir: river flow and decay (Revision R)
    }

    /// <summary>
    /// Parental care (DESIGN §7.8): a mammal or bird mother in contact with her young before weaning gives them
    /// energy (milk, food brought to the nest), up to CARE_RATE x her basal cost per step shared by all her young,
    /// at efficiency CARE_EFF, keeping CARE_RESERVE of her reserve. The young taste it as food (reward).
    /// </summary>
    static void Care(World w, int i, int s)
    {
        int m = w.MotherOf(i);
        if (m < 0 || w.PCare[m] <= 0 || w.Grown[i] >= K.WEAN) return;
        float dx = World.Delta1(w.X[m] - w.X[i]), dy = World.Delta1(w.Y[m] - w.Y[i]), reach = w.PReach[m] + w.PReach[i];
        if (dx * dx + dy * dy > reach * reach) return;
        uint mark = (uint)w.Step + 1u;           // 0 = never refreshed
        if (w.CareStep[m] != mark) { w.CareStep[m] = mark; w.CareLeft[m] = K.CARE_RATE * w.PCare[m] * w.PBasal[m]; }
        float emax = w.EmaxOf(i);
        float spare = (w.Energy[m] - K.CARE_RESERVE * w.EmaxOf(m)) * K.CARE_EFF;
        float give = MathF.Min(MathF.Min(w.CareLeft[m], emax - w.Energy[i]), spare);
        if (give <= 0) return;
        w.Energy[i] += give;
        w.Energy[m] -= give / K.CARE_EFF;
        w.CareLeft[m] -= give;
        w.T.SpendRepro[w.Spc[m]] += give / K.CARE_EFF;
        w.T.CareGiven[s] += give;
        w.PendingR[i] += K.R_INTAKE * give / emax;
    }

    static void Hatch(World w, int i)
    {
        w.StageOf[i] = St.Juvenile;
        w.Age[i] = 0; w.Timer[i] = 0;
        w.T.Births[w.Spc[i]]++;
        // a hatchling that cannot live where it hatched (its genes gave it another realm than its mother's) moves to the nearest allowed cell
        int cell = World.FIdx(w.X[i], w.Y[i]);
        if (!w.CellAllows(i, cell))
        {
            for (int t = 0; t < 24; t++)
            {
                float a = t * 0.785f, d = 2f + t * 0.5f;
                float x = World.Wrap(w.X[i] + MathK.Cos(a) * d), y = World.Wrap(w.Y[i] + MathK.Sin(a) * d);
                if (w.CellAllows(i, World.FIdx(x, y))) { w.X[i] = x; w.Y[i] = y; return; }
            }
            Resolve.Die(w, i, Cause.Orphan, -1);
        }
    }

    static void ClearCarrying(World w, int m)
    {
        // the mother stops carrying when her last embryo is born; checked cheaply via her last child
        int c = w.LastChildSlot[m];
        if (c < 0 || w.Serial[c] != w.LastChildSerial[m] || w.StageOf[c] != St.Embryo)
            w.Flags[m] &= unchecked((byte)~Flag.Carrying);
    }
}
