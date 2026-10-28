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
            float x = BinaryPrimitives.ReadUInt16LittleEndian(r) * K.WORLD / 65536f;
            float y = BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(2)) * K.WORLD / 65536f;
            Assert.InRange(MathF.Abs(x - w.X[i]), 0f, 0.02f);
            Assert.InRange(MathF.Abs(y - w.Y[i]), 0f, 0.02f);
            Assert.Equal(w.Spc[i], r[5] & 31);
            if (++checkedN > 500) break;
        }
        int visible = 0;
        for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] != St.Dead && w.StageOf[i] != St.Embryo) visible++;
        Assert.Equal(visible, n);
    }
}
