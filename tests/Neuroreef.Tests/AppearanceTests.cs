using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Neuroreef.Tests;

public class AppearanceTests
{
    static sbyte[] Ancestor(uint seed, bool land = true, int floor = 0)
    {
        var body = new sbyte[G.NBODY];
        Clades.AncestorBody(body, seed, 3, land, floorClass: floor);
        return body;
    }

    [Fact]
    public void AppearanceLayoutIsConsecutiveAndRecognitionStaysLast()
    {
        int[] genes = { G.B_LENGTH, G.B_WIDTH, G.B_HEIGHT, G.B_HEAD, G.B_NECK, G.B_TAIL,
            G.B_LIMB_POS, G.B_LIMB_SHAPE, G.B_FIN_SHAPE, G.B_WING_SHAPE, G.B_HUE, G.B_SAT,
            G.B_LIGHT, G.B_PATTERN, G.B_PATTERN_SCALE, G.B_PATTERN_HUE };
        Assert.Equal(16, G.NAPPEARANCE);
        Assert.Equal(Enumerable.Range(G.APPEARANCE, G.NAPPEARANCE), genes);
        Assert.Equal(G.APPEARANCE + G.NAPPEARANCE, G.B_MHC);
        Assert.Equal(G.B_MHC + G.NMHC, G.BODY + G.NBODY);
        Assert.Equal(67, G.NBODY);
        Assert.Equal(0f, Body.Organ(-127));
        Assert.Equal(0.5f, Body.Organ(0));
        Assert.Equal(1f, Body.Organ(127));
    }

    [Fact]
    public void AncestorsAreDeterministicAndNotRealmPresets()
    {
        for (uint seed = 0; seed < 100; seed++)
            for (int floor = 0; floor < K.FLOOR_CLASSES; floor++)
            {
                var land = Ancestor(seed, true, floor);
                Assert.Equal(land, Ancestor(seed, true, floor));
                Assert.Equal(land, Ancestor(seed, false, floor));
            }
    }

    [Fact]
    public void EveryAppearanceAndOrganTraitSpansTheFounderRange()
    {
        int[] genes = Enumerable.Range(G.APPEARANCE, G.NAPPEARANCE)
            .Concat(new[] { G.B_LEGS, G.B_FINS, G.B_WINGS, G.B_INSUL, G.B_APPEND }).ToArray();
        var bodies = Enumerable.Range(0, 2048).Select(seed => Ancestor((uint)seed)).ToArray();
        foreach (int gene in genes)
        {
            var values = bodies.Select(b => b[gene - G.BODY]).ToArray();
            Assert.True(values.Min() <= -120, $"gene {gene} minimum {values.Min()}");
            Assert.True(values.Max() >= 120, $"gene {gene} maximum {values.Max()}");
            Assert.True(values.Distinct().Count() > 200, $"gene {gene} lacks diversity");
            Assert.All(values, v => Assert.InRange(v, (sbyte)-127, (sbyte)127));
        }
        Assert.True(bodies.Select(b => string.Join(",", b.Skip(G.APPEARANCE - G.BODY).Take(G.NAPPEARANCE))).Distinct().Count() > 2000);
        for (int floor = 0; floor < K.FLOOR_CLASSES; floor++)
        {
            var masses = Enumerable.Range(0, 1024).Select(seed =>
                MathF.Max(Body.Floors[floor], G.GeneLog(Ancestor((uint)seed, floor: floor)[G.B_MASS - G.BODY], K.MASS_LO, K.MASS_HI))).ToArray();
            Assert.InRange(masses.Min(), Body.Floors[floor], Body.Floors[floor] * 1.05f);
            Assert.True(masses.Max() > 0.9f * K.MASS_HI);
        }
    }

    [Fact]
    public void AppearanceIsInheritedAndMutatesAtTheBodyRate()
    {
        var mother = new sbyte[G.LEN]; var father = new sbyte[G.LEN]; var child = new sbyte[G.LEN];
        for (int k = 0; k < G.NAPPEARANCE; k++) { mother[G.APPEARANCE + k] = -100; father[G.APPEARANCE + k] = 100; }
        var disabled = new EvoSettings { MutRate = 0, MutBody = 0 };
        var changed = new bool[G.NAPPEARANCE];
        var fromMother = new bool[G.NAPPEARANCE]; var fromFather = new bool[G.NAPPEARANCE];
        for (uint key = 0; key < 256; key++)
        {
            Brain.Crossover(child, mother, father, 7, 0, key);
            Brain.Mutate(child, 7, 0, key, disabled);
            for (int k = 0; k < G.NAPPEARANCE; k++)
            {
                Assert.True(child[G.APPEARANCE + k] is -100 or 100);
                fromMother[k] |= child[G.APPEARANCE + k] == -100;
                fromFather[k] |= child[G.APPEARANCE + k] == 100;
            }
            Array.Clear(child);
            Brain.Mutate(child, 7, 0, key, new EvoSettings { MutRate = 0, MutBody = 0.2f });
            for (int k = 0; k < G.NAPPEARANCE; k++) changed[k] |= child[G.APPEARANCE + k] != 0;
            Assert.All(child.Take(G.BODY), b => Assert.Equal(0, b));
            Assert.All(child.Skip(G.REFLEX), b => Assert.Equal(0, b));
        }
        Assert.All(changed.Concat(fromMother).Concat(fromFather), Assert.True);
    }

    [Fact]
    public void DimensionsAndLimbShapeHaveBoundedPhysicalEffects()
    {
        var w = new World(1, 2);
        w.Genome[G.B_WINGS] = 127;
        Body.Express(w, 0);
        float land = w.PVLand[0], water = w.PVWater[0], air = w.PVAir[0], turn = w.PTurn[0], mass = w.PM[0];
        foreach (int gene in new[] { G.B_LENGTH, G.B_WIDTH, G.B_HEIGHT, G.B_LIMB_SHAPE })
        {
            w.Genome[gene] = -127; Body.Express(w, 0);
            float lowLand = w.PVLand[0], lowWater = w.PVWater[0], lowTurn = w.PTurn[0];
            w.Genome[gene] = 127; Body.Express(w, 0);
            Assert.True(lowLand != w.PVLand[0] || lowWater != w.PVWater[0] || lowTurn != w.PTurn[0]);
            Assert.InRange(w.PVLand[0] / land, 0.7f, 1.3f);
            Assert.InRange(w.PVWater[0] / water, 0.7f, 1.3f);
            Assert.InRange(w.PVAir[0] / air, 0.7f, 1.3f);
            Assert.InRange(w.PTurn[0] / turn, 0.7f, 1.3f);
            Assert.Equal(mass, w.PM[0]);
            w.Genome[gene] = 0;
        }
    }

    static World VisibleWorld()
    {
        var w = new World(4, 17);
        w.Sp[0].Used = w.Sp[0].Alive = true; w.Sp[0].Name = "Test";
        for (int i = 0; i < 4; i++)
        {
            Ancestor((uint)i).CopyTo(w.Genome, i * G.LEN + G.BODY);
            w.StageOf[i] = new[] { St.Adult, St.Juvenile, St.Egg, St.Embryo }[i];
            w.Serial[i] = (uint)(i + 1);
            Body.Express(w, i);
        }
        for (int k = 0; k < G.NAPPEARANCE; k++) w.Genome[G.APPEARANCE + k] = (sbyte)(k % 2 == 0 ? -127 : 127);
        Body.Express(w, 0);
        return w;
    }

    static byte[] Frame(World w)
    {
        var snapshot = new Snapshot(w) { Requested = true };
        snapshot.AfterStep();
        Assert.True(snapshot.TryTake(out var bytes, out int length));
        return bytes.AsSpan(0, length).ToArray();
    }

    [Fact]
    public void SnapshotCarriesRawAppearanceForEveryVisibleStageAndIgnoresCladeColour()
    {
        var w = VisibleWorld();
        var before = Frame(w);
        Assert.Equal(6, before[1]);
        Assert.Equal(42, Snapshot.REC);
        Assert.Equal(12 + 3 * Snapshot.REC, before.Length);
        for (int i = 0; i < 3; i++)
            for (int k = 0; k < G.NAPPEARANCE; k++)
                Assert.Equal(w.Genome[i * G.LEN + G.APPEARANCE + k], unchecked((sbyte)before[12 + i * Snapshot.REC + 26 + k]));
        w.Sp[1].Name = "Relabelled"; w.Sp[1].R = 255; w.Sp[1].G = 12; w.Sp[1].B = 140;
        for (int i = 0; i < 4; i++) { w.Spc[i] = 1; Body.Express(w, i); }
        var after = Frame(w);
        for (int i = 0; i < 3; i++)
        {
            int offset = 12 + i * Snapshot.REC;
            Assert.Equal(1, after[offset + 5] & 31);
            before[offset + 5] = after[offset + 5];
        }
        Assert.Equal(before, after);
    }

    [Fact]
    public void GeneBankAndWorldRoundTripAppearanceAndRejectOldVersions()
    {
        var w = VisibleWorld();
        using var bank = new MemoryStream();
        GeneBank.Save(w, bank);
        Assert.Equal(9, GeneBank.Version);
        bank.Position = 0;
        var loaded = GeneBank.Load(bank);
        foreach (var genome in loaded.Genomes[0])
            Assert.Contains(Enumerable.Range(0, 2), i => w.GenomeOf(i).SequenceEqual(genome));
        Assert.Equal(2, loaded.Genomes[0].Count);
        bank.Position = 4; bank.WriteByte(8); bank.Position = 0;
        Assert.Throws<InvalidDataException>(() => GeneBank.Load(bank));
        string path = Path.Combine(Directory.GetCurrentDirectory(), ".appearance-" + Guid.NewGuid().ToString("N"));
        try
        {
            Checkpoint.SaveWorld(w, path);
            Assert.Equal(9, Checkpoint.Version);
            var restored = new World(w.Cap, w.Seed);
            Checkpoint.LoadWorld(restored, path);
            Assert.Equal(w.Genome, restored.Genome);
            Assert.Equal(Frame(w), Frame(restored));
            using (var file = File.OpenWrite(path)) { file.Position = 4; file.WriteByte(8); }
            Assert.Throws<InvalidDataException>(() => Checkpoint.WorldHeader(path));
            Assert.Throws<InvalidDataException>(() => Checkpoint.LoadWorld(restored, path));
            using (var file = File.Create(path)) { file.Write("NRCK"u8); file.WriteByte(8); file.WriteByte(0); }
            Assert.Throws<InvalidDataException>(() => Checkpoint.PrehistoryHeader(path));
        }
        finally { File.Delete(path); File.Delete(path + ".tmp"); }
    }

    [Fact]
    public void FoundingHabitatConstraintSurvivesPlacementFallback()
    {
        var w = new World(4, 1);
        Array.Fill(w.Cell, (byte)Realm.Deep);
        var genome = new sbyte[G.LEN];
        genome[G.B_LEGS] = 127; genome[G.B_FINS] = genome[G.B_WINGS] = -127;
        w.Evo.MutRate = w.Evo.MutBody = 0;
        Assert.Equal(-1, Life.Found(w, 0, genome, true, 1, 0, 0, Realm.Water));
        Assert.Equal(w.Cap, w.FreeTop);
        genome[G.B_FINS] = 127;
        int swimmer = Life.Found(w, 0, genome, true, 2, 0, 0, Realm.Water);
        Assert.True(swimmer >= 0);
        Assert.True(w.CellAllows(World.FIdx(w.X[swimmer], w.Y[swimmer]), w.RealmOf(swimmer) & Realm.Water));
    }
}
