// Slots, genomes and the life cycle (DESIGN §7.3, §7.4, §8; phase 4 LIFECYCLE).

using System;

namespace Neuroreef;

public static class Life
{
    public static int NewSlot(World w)
    {
        if (w.FreeTop == 0) { w.T.SlotShortage++; return -1; }
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
        var sp = w.Sp[species];
        uint seed = w.Seed, step = (uint)w.Step;
        w.Spc[c] = (byte)species; w.StageOf[c] = stage;
        w.Flags[c] = Flag.Newborn;
        w.X[c] = World.Wrap(x); w.Y[c] = World.Wrap(y);
        w.Heading[c] = Rng.U(seed, step, (uint)c, Rs.Place) * 2f * MathF.PI;
        w.Speed[c] = 0;
        w.Energy[c] = energy; w.Gut[c] = 0; w.GutEnergy[c] = 0; w.GutWaste[c] = 0; w.GutSeedy[c] = 0;
        w.Thirst[c] = 0; w.Fatigue[c] = 0; w.Age[c] = 0; w.Pain[c] = 0; w.Cooldown[c] = 0; w.Timer[c] = 0; w.LastFear[c] = 0;
        w.Sex[c] = (byte)(Rng.U(seed, step, (uint)c, Rs.Sex) < 0.5f ? 0 : 1);
        w.Serial[c] = w.NextSerial++;
        w.Children[c] = 0; w.LastChildSlot[c] = -1; w.LastChildSerial[c] = 0;
        w.IActs[c] = 0; w.ITarget[c] = -1; w.IMate[c] = -1; w.IScanning[c] = 0; w.ISpeed[c] = 0; w.IHeading[c] = w.Heading[c];
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
        if (mutate) Brain.Mutate(w.GenomeOf(to), w.Seed, (uint)w.Step, (uint)to + 0x10000u);
    }

    public static void CrossGenome(World w, int mother, int father, int child)
    {
        Brain.Crossover(w.GenomeOf(child), w.GenomeOf(mother), w.GenomeOf(father), w.Seed, (uint)w.Step, (uint)child);
        Brain.Mutate(w.GenomeOf(child), w.Seed, (uint)w.Step, (uint)child + 0x10000u);
    }

    /// <summary>Insert the genome of a dying animal into its species' elite archive if it beats the weakest (DESIGN §7.4).</summary>
    public static void ToArchive(World w, int i)
    {
        int s = w.Spc[i];
        var sp = w.Sp[s];
        float score = w.Children[i] + 0.1f * MathF.Min(1f, w.Age[i] / w.PLife[i]);
        if (score < 0.05f) return;
        int n = w.ArchiveCount[s], baseI = s * K.ARCHIVE, slot;
        if (n < K.ARCHIVE) { slot = n; w.ArchiveCount[s] = n + 1; }
        else
        {
            slot = 0; float worst = w.ArchiveScore[baseI];
            for (int k = 1; k < K.ARCHIVE; k++) if (w.ArchiveScore[baseI + k] < worst) { worst = w.ArchiveScore[baseI + k]; slot = k; }
            if (score <= worst) return;
        }
        w.ArchiveScore[baseI + slot] = score;
        Array.Copy(w.Genome, i * G.LEN, w.Archive, (baseI + slot) * G.LEN, G.LEN);
    }

    /// <summary>A random cell suitable for the given realm (hashed tries).</summary>
    public static bool RandomSite(World w, Realm realm, uint key, out float x, out float y)
    {
        for (int t = 0; t < 64; t++)
        {
            x = Rng.U(w.Seed, (uint)w.Step, key, Rs.Place + 100u + (uint)t * 2) * K.WORLD;
            y = Rng.U(w.Seed, (uint)w.Step, key, Rs.Place + 101u + (uint)t * 2) * K.WORLD;
            if (w.CellAllows(World.FIdx(x, y), realm)) return true;
        }
        x = y = 0; return false;
    }

    /// <summary>Create one animal from a founder source: a random genome, or a genome supplied by the caller.</summary>
    public static int Found(World w, int species, ReadOnlySpan<sbyte> genome, bool adult, uint key)
    {
        var sp = w.Sp[species];
        var body = sp.StageOf(!adult);
        if (!RandomSite(w, body.Realm, key, out float x, out float y)) return -1;
        int c = NewSlot(w);
        if (c < 0) return -1;
        if (genome.IsEmpty) Brain.RandomGenome(w.GenomeOf(c), w.Seed, (uint)w.Step, key);
        else { genome.CopyTo(w.GenomeOf(c)); Brain.Mutate(w.GenomeOf(c), w.Seed, (uint)w.Step, key + 0x20000u); }
        Place(w, c, species, adult ? St.Adult : St.Juvenile, x, y, 0f, -1);
        w.Energy[c] = K.C_EMAX * w.PM[c] * (0.5f + 0.3f * Rng.U(w.Seed, (uint)w.Step, key, Rs.Place + 7));
        if (adult) w.Age[c] = w.PMature[c] * (1f + 0.5f * Rng.U(w.Seed, (uint)w.Step, key, Rs.Place + 9));
        else w.Age[c] = w.PMature[c] * 0.8f * Rng.U(w.Seed, (uint)w.Step, key, Rs.Place + 9);
        return c;
    }

    /// <summary>LIFECYCLE phase: eggs and embryos, hatching, metamorphosis, genesis immigration. Serial.</summary>
    public static void Run(World w, int[] activeCount)
    {
        int cap = w.Cap;
        Array.Clear(activeCount);
        for (int i = 0; i < cap; i++)
        {
            byte st = w.StageOf[i];
            if (st == St.Dead) continue;
            w.Flags[i] &= unchecked((byte)~Flag.Newborn);
            var sp = w.Sp[w.Spc[i]];
            if (st == St.Embryo)
            {
                int m = w.MotherOf(i);
                if (m < 0) { Resolve.DieNoBody(w, i, Cause.Orphan, -1); continue; }
                w.X[i] = w.X[m]; w.Y[i] = w.Y[m];
                // the mother builds the embryo's tissue during gestation; a starving mother loses it
                float cost = Resolve.TissueCost(sp, w.PM[m]) / MathF.Max(1, sp.Gestation);
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
                bool resting = (w.Flags[i] & Flag.Resting) != 0;
                if (resting ? (w.SeasonAt(w.Y[i]) > 0.2f || w.Timer[i] <= 0) : w.Timer[i] <= 0) Hatch(w, i);
                continue;
            }
            activeCount[w.Spc[i]]++;
            if (st == St.Juvenile && w.Age[i] >= w.PMature[i])
            {
                w.StageOf[i] = St.Adult;
                // metamorphosis: if the adult cannot live where the juvenile is, it moves to the nearest allowed side
                if (sp.TwoStage && !w.CellAllows(World.FIdx(w.X[i], w.Y[i]), sp.Adult.Realm))
                    if (RandomSite(w, sp.Adult.Realm, (uint)i, out float x, out float y)) { w.X[i] = x; w.Y[i] = y; }
            }
        }

        // genesis immigration (DESIGN §7.4): random brains and mutated elite genomes
        bool immigrate = w.Genesis && w.Step < (ulong)w.GenesisSteps;
        for (int s = 0; s < w.NS; s++)
        {
            float floor;
            if (immigrate) floor = w.Sp[s].InitCount * K.GENESIS_FLOOR * (1f - (float)w.Step / w.GenesisSteps);
            else if (w.StraysAfterGenesis) floor = 4;
            else continue;
            for (int k = 0; k < K.IMMIGRANTS_PER_STEP && activeCount[s] < floor; k++)
            {
                uint key = (uint)(s * 1000 + k) + 0x40000u;
                int n = w.ArchiveCount[s];
                ReadOnlySpan<sbyte> gsrc = ReadOnlySpan<sbyte>.Empty;
                if (n > 0 && Rng.U(w.Seed, (uint)w.Step, key, Rs.Immigrant) < 0.5f)
                {
                    int pick = (int)(Rng.U(w.Seed, (uint)w.Step, key, Rs.Archive) * n);
                    gsrc = new ReadOnlySpan<sbyte>(w.Archive, (s * K.ARCHIVE + pick) * G.LEN, G.LEN);
                }
                int c = Found(w, s, gsrc, true, key);
                if (c < 0) break;
                w.Flags[c] |= Flag.Immigrant;
                w.T.Immigrants[s]++;
                activeCount[s]++;
            }
        }
    }

    static void Hatch(World w, int i)
    {
        w.StageOf[i] = St.Juvenile;
        w.Flags[i] &= unchecked((byte)~Flag.Resting);
        w.Age[i] = 0; w.Timer[i] = 0;
        w.T.Births[w.Spc[i]]++;
        var body = w.StageBody(i);
        // a hatchling that cannot live where it hatched (for example a tadpole on a shore cell) moves to the nearest water/land
        int cell = World.FIdx(w.X[i], w.Y[i]);
        if (!w.CellAllows(cell, body.Realm))
        {
            for (int t = 0; t < 24; t++)
            {
                float a = t * 0.785f, d = 2f + t * 0.5f;
                float x = World.Wrap(w.X[i] + MathK.Cos(a) * d), y = World.Wrap(w.Y[i] + MathK.Sin(a) * d);
                if (w.CellAllows(World.FIdx(x, y), body.Realm)) { w.X[i] = x; w.Y[i] = y; return; }
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
