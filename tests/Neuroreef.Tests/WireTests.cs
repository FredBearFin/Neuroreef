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
}
