// Interface control (docs/ICD.md): the AGENTS frame round-trips.

using System;
using System.Buffers.Binary;
using Xunit;

namespace Neuroreef.Tests;

public class WireTests
{
    [Fact]
    public void AgentsFrameRoundTrips()
    {
        using var sim = new Simulation(8192, 21, 1);
        sim.Snap.Requested = true;
        sim.Step();
        Assert.True(sim.Snap.TryTake(out var b, out int len));
        Assert.Equal(0x02, b[0]);
        Assert.Equal(7, b[1]);
        Assert.Equal((uint)sim.W.Step, BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(4)));
        int n = (int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(8));
        Assert.Equal(12 + Snapshot.REC * n, len);
        var w = sim.W;
        int checkedN = 0;
        for (int k = 0; k < n; k++)
        {
            var r = b.AsSpan(12 + Snapshot.REC * k);
            uint serial = BinaryPrimitives.ReadUInt32LittleEndian(r.Slice(8));
            int i = sim.FindSerial(serial);
            Assert.True(i >= 0);
            float x = BinaryPrimitives.ReadUInt16LittleEndian(r) * Map.W / 65536f;
            float y = BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(2)) * Map.W / 65536f;
            Assert.InRange(MathF.Abs(x - w.X[i]), 0f, 0.02f);
            Assert.InRange(MathF.Abs(y - w.Y[i]), 0f, 0.02f);
            Assert.Equal(w.Spc[i], r[5] & 31);
            for (int gene = 0; gene < G.NAPPEARANCE; gene++)
                Assert.Equal(w.Genome[i * G.LEN + G.APPEARANCE + gene], unchecked((sbyte)r[26 + gene]));
            // format 7: depth in decimetres, time at depth, lungs and oxygen debt (Respiration.cs)
            Assert.InRange(MathF.Abs(BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(42)) / 10f - w.Z[i]), 0f, 0.051f);
            Assert.Equal((int)MathF.Round(w.ZShare[i] * 255f), r[44]);
            Assert.Equal((int)MathF.Round(w.PLungs[i] * 15f), r[45] & 15);
            if (++checkedN > 500) break;
        }
        int visible = 0;
        for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] != St.Dead && w.StageOf[i] != St.Embryo) visible++;
        Assert.Equal(visible, n);
    }

    [Fact] // the translator reads a sound's meaning from all the senses: a call always made with food at hand means "food here"
    public void TranslatorFindsTheSenseACallIsMadeUnder()
    {
        var w = new World(64, 1);
        w.Sp[0].Used = w.Sp[0].Alive = true;
        const int N = 8, SOUND = 2;
        for (int i = 0; i < N; i++) { w.Spc[i] = 0; w.StageOf[i] = St.Adult; }
        var rng = new Random(7);
        for (int step = 0; step < 2000; step++)
        {
            w.Step = (ulong)step;
            for (int i = 0; i < N; i++)
            {
                for (int f = 1; f < F.N; f++) w.Feat[i * F.N + f] = 0.5f * rng.NextSingle();
                for (int p = 0; p < P.N; p++) w.Gain[i * P.N + p] = 0.3f * rng.NextSingle();
                int callK = rng.NextDouble() < 0.2 ? SOUND : -1;
                if (callK >= 0) w.Feat[i * F.N + F.FOOD_HERE] = 0.9f + 0.1f * rng.NextSingle();   // it calls only with food at hand
                // the last one sometimes hears its own clade's sound, and then turns to eating
                bool hears = i == N - 1 && rng.NextDouble() < 0.3;
                w.IHeard[i] = hears ? 1 + 0 * F.NSOUND + SOUND : 0;
                if (hears) w.Gain[i * P.N + P.EAT] = 0.9f;
                Resolve.Voices(w, i, 0, hears ? -1 : callK, 0);
            }
        }
        var when = Meaning.RankSenses(w.T, 0, SOUND);
        Assert.NotEmpty(when);
        Assert.Equal(F.FOOD_HERE, when[0].Index);
        Assert.True(when[0].High && when[0].D > 2f, $"d = {when[0].D}");
        Assert.Equal("food here", Meaning.Phrase(when[0].Index, true));
        var then = Meaning.RankAnswers(w.T, 0, SOUND);
        Assert.NotEmpty(then);
        Assert.Equal(P.EAT, then[0].Index);
        Assert.Empty(Meaning.RankSenses(w.T, 0, 5));   // a sound never made means nothing
        // every sense and direction has a phrase, every behaviour a verb, and the console's lexicon carries the reading
        for (int f = 1; f < F.N; f++) { Assert.False(string.IsNullOrEmpty(Meaning.High[f])); Assert.False(string.IsNullOrEmpty(Meaning.Low[f])); }
        for (int p = 0; p < P.N; p++) Assert.False(string.IsNullOrEmpty(Meaning.Verbs[p]));
        using var doc = System.Text.Json.JsonDocument.Parse(Neuroreef.Server.Wire.Lexicon(w));
        var cl = doc.RootElement.GetProperty("clades")[0];
        Assert.False(cl.GetProperty("mute").GetBoolean());
        var snd = cl.GetProperty("sounds")[SOUND];
        Assert.Equal(F.FOOD_HERE, snd.GetProperty("when")[0].GetProperty("f").GetInt32());
        Assert.Equal("food here", snd.GetProperty("when")[0].GetProperty("text").GetString());
        Assert.Equal(P.EAT, snd.GetProperty("then")[0].GetProperty("p").GetInt32());
        Assert.Equal(F.N, snd.GetProperty("z").GetArrayLength());
    }

    [Fact, BigWorld] // 2026-11-01: the WORLD frame ends with the rivers as lines (ICD.md, RIVERS), as Hydrology.Lines has them
    public void WorldFrameCarriesRiverLines()
    {
        var w = new World(16, 3);
        Terrain.Build(w);
        var b = Neuroreef.Server.Wire.EncodeWorld(w);
        int n = Map.FW * Map.FW, o = 16 + 10 * n;
        int R = (int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o)), V = (int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o + 4));
        Assert.Equal(o + 8 + 16 * R + 16 * V + 2 * n, b.Length);   // then the depth in decimetres (2026-11-02)
        var lines = Hydrology.Lines(w);
        Assert.Equal(lines.Count, R);
        Assert.True(R > 20, $"{R} rivers");
        int pts = o + 8 + 16 * R, total = 0;
        for (int k = 0; k < R; k++)
        {
            var r = b.AsSpan(o + 8 + 16 * k);
            int first = (int)BinaryPrimitives.ReadUInt32LittleEndian(r), count = (int)BinaryPrimitives.ReadUInt32LittleEndian(r.Slice(4));
            int down = BinaryPrimitives.ReadInt32LittleEndian(r.Slice(8));
            Assert.Equal(total, first); total += count;
            Assert.Equal(lines[k].Points.Count, count); Assert.Equal(lines[k].Down, down); Assert.Equal(lines[k].Order, r[12]); Assert.Equal(lines[k].EndKind, r[13]);
            Assert.True(down < k);
            for (int p = 0; p < count; p++)
            {
                var q = b.AsSpan(pts + 16 * (first + p));
                var (x, y, wd, d) = lines[k].Points[p];
                Assert.Equal(x, BinaryPrimitives.ReadSingleLittleEndian(q)); Assert.Equal(y, BinaryPrimitives.ReadSingleLittleEndian(q.Slice(4)));
                Assert.Equal(wd, BinaryPrimitives.ReadSingleLittleEndian(q.Slice(8))); Assert.Equal(d, BinaryPrimitives.ReadSingleLittleEndian(q.Slice(12)));
            }
            // a river's first point lies in its head cell: a channel cell, at its centreline point
            var (x0, y0, _, _) = lines[k].Points[0];
            int c = World.WrapI((int)MathF.Floor(y0), Map.FW) * Map.FW + World.WrapI((int)MathF.Floor(x0), Map.FW);
            Assert.True(RiverStats.IsChannel(w, c), $"river {k} starts off its channel");
        }
        Assert.Equal(V, total);
    }

    [Fact] // 2026-11-01: elevation also as 16 bits (the u8 plane's steps terrace high mountains), the u8 plane kept for older clients
    public void WorldFrameCarriesFineElevation()
    {
        var w = new World(16, 3);
        Terrain.Build(w);
        var b = Neuroreef.Server.Wire.EncodeWorld(w);
        int n = Map.FW * Map.FW;
        for (int i = 0; i < n; i += 7)
        {
            float e = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(16 + 8 * n + 2 * i)) / 65535f;
            Assert.True(MathF.Abs(e - w.Elev[i]) <= 1f / 65535f, $"cell {i}: {e} against {w.Elev[i]}");
            Assert.Equal((byte)Math.Clamp((int)(w.Elev[i] * 255f), 0, 255), b[16 + i]);
        }
    }
}
