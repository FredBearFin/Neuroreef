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
        w.Children[c] = 0; w.Matured[c] = 0; w.LastChildSlot[c] = -1; w.LastChildSerial[c] = 0;
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

    /// <summary>Fitness for the archive: offspring that grew up count most (DESIGN §7.4).</summary>
    public static float Score(World w, int i) => K.SCORE_MATURED * w.Matured[i] + w.Children[i] + 0.1f * MathF.Min(1f, w.Age[i] / w.PLife[i]);

    /// <summary>Insert the genome of a dying animal into its species' elite archive if it beats the weakest (DESIGN §7.4).</summary>
    public static void ToArchive(World w, int i)
    {
        int s = w.Spc[i];
        var sp = w.Sp[s];
        float score = Score(w, i);
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
        => Found(w, species, genome, adult, key, float.NaN, 0f);

    /// <summary>Create one animal. With a group centre (gx, gy), it is placed within GROUP_SPREAD of it, as colonists arrive together.</summary>
    public static int Found(World w, int species, ReadOnlySpan<sbyte> genome, bool adult, uint key, float gx, float gy)
    {
        var sp = w.Sp[species];
        var body = sp.StageOf(!adult);
        float x = 0, y = 0;
        bool ok = false;
        if (!float.IsNaN(gx))
            for (int t = 0; t < 16 && !ok; t++)
            {
                float a = Rng.U(w.Seed, (uint)w.Step, key, Rs.Place + 200u + (uint)t * 2) * 2f * MathF.PI;
                float d = Rng.U(w.Seed, (uint)w.Step, key, Rs.Place + 201u + (uint)t * 2) * K.GROUP_SPREAD;
                x = World.Wrap(gx + MathK.Cos(a) * d); y = World.Wrap(gy + MathK.Sin(a) * d);
                ok = w.CellAllows(World.FIdx(x, y), body.Realm);
            }
        if (!ok && !RandomSite(w, body.Realm, key, out x, out y)) return -1;
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
        Array.Clear(w.KinN);
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
            int si = w.Spc[i];
            activeCount[si]++;
            if (st == St.Adult)
            {
                // reservoir-sample one living adult per species: colonist groups land next to kin
                int nk = ++w.KinN[si];
                if (Rng.U(w.Seed, (uint)w.Step, (uint)i, Rs.Place + 300u) * nk < 1f) { w.KinX[si] = w.X[i]; w.KinY[si] = w.Y[i]; }
            }
            if (st == St.Juvenile && w.Age[i] >= w.PMature[i])
            {
                w.StageOf[i] = St.Adult;
                int m = w.MotherOf(i);
                if (m >= 0) w.Matured[m]++;
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
            if (activeCount[s] >= floor) continue;
            // colonists arrive as one group, next to living kin if there are any (a lone colonist would never find a mate)
            float gx, gy;
            if (w.KinN[s] > 0) { gx = w.KinX[s]; gy = w.KinY[s]; }
            else if (!RandomSite(w, w.Sp[s].Adult.Realm, (uint)s + 0x30000u, out gx, out gy)) continue;
            for (int k = 0; k < K.GROUP_SIZE && activeCount[s] < floor; k++)
            {
                uint key = (uint)(s * 1000 + k) + 0x40000u;
                ReadOnlySpan<sbyte> gsrc = Colonist(w, s, key);
                int c = Found(w, s, gsrc, true, key, gx, gy);
                if (c < 0) break;
                w.Flags[c] |= Flag.Immigrant;
                w.T.Immigrants[s]++;
                activeCount[s]++;
            }
        }
    }

    /// <summary>
    /// Genome of a colonist (DESIGN §7.4): once the archive holds 4 genomes, ARCHIVE_SHARE of colonists are bred
    /// from it, parents chosen by tournament; sexual species get a crossover of two winners. The rest are random.
    /// Returns empty for a random brain. Uses the world's scratch buffer.
    /// </summary>
    static ReadOnlySpan<sbyte> Colonist(World w, int s, uint key)
    {
        int n = w.ArchiveCount[s];
        if (n < 4 || Rng.U(w.Seed, (uint)w.Step, key, Rs.Immigrant) >= K.ARCHIVE_SHARE) return ReadOnlySpan<sbyte>.Empty;
        int a = Tournament(w, s, n, key, 0);
        var child = new Span<sbyte>(w.Scratch, 0, G.LEN);
        var pa = new ReadOnlySpan<sbyte>(w.Archive, (s * K.ARCHIVE + a) * G.LEN, G.LEN);
        bool sexual = w.Sp[s].Mode is Repro.Sexual or Repro.Hermaphrodite or Repro.Facultative or Repro.Cyclic;
        if (sexual)
        {
            int b = Tournament(w, s, n, key, 1);
            var pb = new ReadOnlySpan<sbyte>(w.Archive, (s * K.ARCHIVE + b) * G.LEN, G.LEN);
            Brain.Crossover(child, pa, pb, w.Seed, (uint)w.Step, key + 0x60000u);
        }
        else pa.CopyTo(child);
        return child;      // Found() mutates it
    }

    static int Tournament(World w, int s, int n, uint key, uint round)
    {
        int best = -1; float bs = float.MinValue;
        for (int t = 0; t < K.TOURNAMENT; t++)
        {
            int k = (int)(Rng.U(w.Seed, (uint)w.Step, key, Rs.Archive + round * 16u + (uint)t) * n);
            float sc = w.ArchiveScore[s * K.ARCHIVE + k];
            if (sc > bs) { bs = sc; best = k; }
        }
        return best;
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
