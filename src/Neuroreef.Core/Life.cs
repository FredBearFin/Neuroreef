// Slots, genomes and the life cycle (DESIGN §7.3, §7.4, §8; phase 4 LIFECYCLE).

using System;

namespace Neuroreef;

public static class Life
{
    public static int NewSlot(World w)
    {
        if (w.FreeTop == 0) { w.T.SlotShortage++; if (w.T.LastFullStep != w.Step) { w.T.LastFullStep = w.Step; w.T.FullSteps++; } return -1; }
        int c = w.FreeStack[--w.FreeTop];
        w.AliveCount++;
        return c;
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
        w.Call[c] = 0; w.IHeard[c] = 0; w.IHeardUser[c] = 0;
        w.Flags[c] = Flag.Newborn;
        w.X[c] = World.Wrap(x); w.Y[c] = World.Wrap(y);
        w.Heading[c] = Rng.U(seed, step, (uint)c, Rs.Place) * 2f * MathF.PI;
        w.Speed[c] = 0;
        w.Energy[c] = energy; w.Gut[c] = 0; w.GutEnergy[c] = 0; w.GutWaste[c] = 0; w.GutSeedy[c] = 0;
        w.Grown[c] = 0;
        w.Thirst[c] = 0; w.Fatigue[c] = 0; w.Age[c] = 0; w.Pain[c] = 0; w.Cooldown[c] = 0; w.Timer[c] = 0; w.LastFear[c] = 0;
        // sex: the mother's sex-ratio gene gives the chance of a daughter (0 = female)
        float pFemale = mother >= 0 && w.PHerm[mother] == 0 ? w.PSexRatio[mother] : 0.5f;
        w.Sex[c] = (byte)(Rng.U(seed, step, (uint)c, Rs.Sex) < pFemale ? 0 : 1);
        w.Serial[c] = w.NextSerial++;
        w.Children[c] = 0; w.Matured[c] = 0; w.LastChildSlot[c] = -1; w.LastChildSerial[c] = 0;
        w.IActs[c] = 0; w.ITarget[c] = -1; w.IMate[c] = -1; w.IScanning[c] = 0; w.ISpeed[c] = 0; w.IHeading[c] = w.Heading[c];
        w.Infect[c] = 0; w.Immune[c] = 0; w.Virulence[c] = 0; w.IInfNear[c] = 0; w.IVirNear[c] = 0; w.IStrainSrc[c] = -1; w.CareLeft[c] = 0; w.CareStep[c] = 0;
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
        Array.Copy(w.Genome, from * G.LEN, w.Genome, to * G.LEN, G.LEN);
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
        ArchiveInsert(w, w.Spc[i], new ReadOnlySpan<sbyte>(w.Genome, i * G.LEN, G.LEN), score);
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

    /// <summary>
    /// Create one founder organism of a clade at step 0 (or from a gene bank). With an empty genome, a random brain
    /// and a random land ancestor's body (tests). With a group centre (gx, gy), it is placed within GROUP_SPREAD of
    /// it, as populations are clustered. The genome given is mutated, so founders vary.
    /// </summary>
    public static int Found(World w, int clade, ReadOnlySpan<sbyte> genome, bool adult, uint key, float gx = float.NaN, float gy = 0f)
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
        var realm = w.RealmOf(c);
        float x = 0, y = 0;
        bool ok = false;
        if (!float.IsNaN(gx))
            for (int t = 0; t < 16 && !ok; t++)
            {
                float a = Rng.U(w.Seed, (uint)w.Step, key, Rs.Place + 200u + (uint)t * 2) * 2f * MathF.PI;
                float d = Rng.U(w.Seed, (uint)w.Step, key, Rs.Place + 201u + (uint)t * 2) * K.GROUP_SPREAD;
                x = World.Wrap(gx + MathK.Cos(a) * d); y = World.Wrap(gy + MathK.Sin(a) * d);
                ok = w.CellAllows(World.FIdx(x, y), realm);
            }
        if (!ok && !RandomSite(w, realm, key, out x, out y)) { FreeSlot(w, c); return -1; }
        Place(w, c, clade, adult ? St.Adult : St.Juvenile, x, y, 0f, -1);
        w.Energy[c] = K.C_EMAX * w.PM[c] * (0.5f + 0.3f * Rng.U(w.Seed, (uint)w.Step, key, Rs.Place + 7));
        if (adult) { w.Age[c] = w.PMature[c] * (1f + 0.5f * Rng.U(w.Seed, (uint)w.Step, key, Rs.Place + 9)); w.Grown[c] = 1f; }
        else { w.Age[c] = w.PMature[c] * 0.8f * Rng.U(w.Seed, (uint)w.Step, key, Rs.Place + 9); w.Grown[c] = w.Age[c] / w.PMature[c]; }
        return c;
    }

    /// <summary>LIFECYCLE phase: eggs and embryos, hatching, metamorphosis, disease, parental care. Serial. Nothing is
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
                float cost = Resolve.TissueCost(w.PBirth[m], w.PM[m]) / MathF.Max(1f, w.PGest[m]);
                if (w.Energy[m] < cost + 0.1f * w.EmaxOf(m)) { Resolve.DieNoBody(w, i, Cause.Orphan, -1); continue; }
                w.Energy[m] -= cost;
                w.T.SpendRepro[w.Spc[m]] += cost;
                if (--w.Timer[i] <= 0) { Hatch(w, i); ClearCarrying(w, m); }
                continue;
            }
            if (st == St.Egg)
            {
                int cell = World.FIdx(w.X[i], w.Y[i]);
                if (w.Fire[cell] > 0) { Resolve.Die(w, i, Cause.Fire, -1); continue; }
                w.Timer[i] -= 1;
                if (w.Timer[i] <= 0) Hatch(w, i);
                continue;
            }
            int si = w.Spc[i];
            if (Disease(w, i, si)) continue;          // died of it
            if (st == St.Juvenile) Care(w, i, si);
            activeCount[si]++;
            if (st == St.Juvenile && w.Grown[i] >= 1f)              // mature on reaching adult size
            {
                w.StageOf[i] = St.Adult;
                int m = w.MotherOf(i);
                if (m >= 0) w.Matured[m]++;
            }
        }
    }

    /// <summary>
    /// Disease (DESIGN §9.6): pathogens are caught by contact with infected organisms that look alike (counted by
    /// DECIDE), so they spread in crowds and fade in sparse populations. Each infection carries its own virulence v,
    /// inherited from the strain caught and mutated (VIR_MUT): a more virulent strain spreads better per contact but
    /// costs and kills its host more, so virulence evolves. The host's immunity gene lowers both infection and death.
    /// Infection lasts DIS_STEPS; recovery gives immunity for DIS_IMMUNE. Returns true if the organism died.
    /// </summary>
    static bool Disease(World w, int i, int s)
    {
        uint seed = w.Seed, step = (uint)w.Step;
        float protect = 1f - K.IMMUNE_PROTECT * w.PImmune[i];
        if (w.Infect[i] > 0)
        {
            w.Infect[i] -= 1;
            if (w.Infect[i] <= 0) { w.Infect[i] = 0; w.Virulence[i] = 0; w.Immune[i] = K.DIS_IMMUNE; return false; }
            float hunger = Math.Clamp(1f - w.Energy[i] / w.EmaxOf(i), 0f, 1f);
            if (Rng.U(seed, step, (uint)i, Rs.Disease + 1) < K.DIS_MORT * w.Virulence[i] * (1f + hunger) * protect) { Resolve.Die(w, i, Cause.Disease, -1); return true; }
            return false;
        }
        if (w.Immune[i] > 0) { w.Immune[i] -= 1; return false; }
        int n = w.IInfNear[i];
        float v = n > 0 ? w.IVirNear[i] : 1f;
        // matching alleles: a strain infects a host whose recognition genes match its key (Red Queen, Hamilton 1980)
        int src = w.IStrainSrc[i];
        bool fromContact = n > 0 && src >= 0 && w.Infect[src] > 0;
        float match = fromContact ? Match(w, src, i) : 1f;
        float beta = K.DIS_BETA * Nature.Disease * 1.5f * v / (0.5f + v) * protect * match;     // transmission saturates with virulence
        float escape = 1f;
        for (; n > 0; n--) escape *= 1f - beta;
        float pContact = 1f - escape, pSpill = K.DIS_SPILL * Nature.Disease * protect;
        float u = Rng.U(seed, step, (uint)i, Rs.Disease);
        if (u < pContact + pSpill)
        {
            float v0 = u < pContact ? w.IVirNear[i] : 1f;           // a strain caught from a neighbour, or a new one from the reservoir
            w.Infect[i] = K.DIS_STEPS;
            w.Virulence[i] = Math.Clamp(v0 * MathK.Exp(K.VIR_MUT * Rng.N(seed, step, (uint)i, Rs.Disease + 2)), 0.05f, 10f);
            // the strain's key: the contact's (or, from the reservoir, the host's own type), mutated
            for (int k = 0; k < G.NMHC; k++)
            {
                // from the reservoir: the strain that just succeeded on this host, so it fits its type
                float key = (u < pContact && fromContact ? w.Strain[src * G.NMHC + k] : w.Genome[i * G.LEN + G.B_MHC + k])
                            + Rng.N(seed, step, (uint)i, Rs.Disease + 100u + (uint)k) * K.MHC_MUT;
                w.Strain[i * G.NMHC + k] = (sbyte)Math.Clamp((int)MathF.Round(key), -127, 127);
            }
            w.T.Infections[s]++;
        }
        return false;
    }

    /// <summary>How well the strain carried by s matches host h's recognition genes (1 = perfect).</summary>
    static float Match(World w, int s, int h)
    {
        float d2 = 0; int gh = h * G.LEN + G.B_MHC;
        for (int k = 0; k < G.NMHC; k++) { float d = (w.Strain[s * G.NMHC + k] - w.Genome[gh + k]) * (1f / 127f); d2 += d * d; }
        return MathK.Exp(-d2 / G.NMHC / (K.MHC_W * K.MHC_W));
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
        if (!w.CellAllows(cell, w.RealmOf(i)))
        {
            for (int t = 0; t < 24; t++)
            {
                float a = t * 0.785f, d = 2f + t * 0.5f;
                float x = World.Wrap(w.X[i] + MathK.Cos(a) * d), y = World.Wrap(w.Y[i] + MathK.Sin(a) * d);
                if (w.CellAllows(World.FIdx(x, y), w.RealmOf(i))) { w.X[i] = x; w.Y[i] = y; return; }
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
