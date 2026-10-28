// Agent snapshot triple buffer (DESIGN §11, NR-506). After a step, if a reader asked for one, the sim
// thread encodes the AGENTS frame (docs/ICD.md §1, type 0x02) into its private buffer and publishes it
// with one interlocked exchange. The reader always takes the newest buffer; a slow reader skips frames.

using System;
using System.Buffers.Binary;
using System.Threading;

namespace Neuroreef;

public sealed class Snapshot
{
    readonly World _w;
    byte[] _write, _ready, _read;
    int _readyLen, _readLen;
    int _fresh;                 // 1 when _ready holds a frame the reader has not taken
    public volatile bool Requested;
    public const int REC = 16;  // bytes per agent record (ICD §1, AGENTS)

    public Snapshot(World w)
    {
        _w = w;
        int size = 12 + REC * w.Cap;
        _write = new byte[size]; _ready = new byte[size]; _read = new byte[size];
    }

    public void AfterStep()
    {
        if (!Requested) return;
        Requested = false;
        int len = Encode(_write);
        lock (this)
        {
            (_write, _ready) = (_ready, _write);
            _readyLen = len;
            _fresh = 1;
        }
    }

    /// <summary>Take the newest frame, if a new one exists. The returned array stays valid until the next call.</summary>
    public bool TryTake(out byte[] buf, out int len)
    {
        lock (this)
        {
            if (_fresh == 0) { buf = _read; len = 0; return false; }
            (_read, _ready) = (_ready, _read);
            _readLen = _readyLen;
            _fresh = 0;
        }
        buf = _read; len = _readLen;
        return true;
    }

    int Encode(byte[] b)
    {
        var w = _w;
        b[0] = 0x02; b[1] = 2; b[2] = 0; b[3] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), (uint)w.Step);
        int n = 0, o = 12;
        float sx = 65536f / K.WORLD;
        for (int i = 0; i < w.Cap; i++)
        {
            byte st = w.StageOf[i];
            if (st == St.Dead || st == St.Embryo) continue;
            var span = b.AsSpan(o);
            BinaryPrimitives.WriteUInt16LittleEndian(span, (ushort)(int)(w.X[i] * sx));
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), (ushort)(int)(w.Y[i] * sx));
            float h = w.Heading[i] % (2f * MathF.PI); if (h < 0) h += 2f * MathF.PI;
            span[4] = (byte)(int)(h * (256f / (2f * MathF.PI)));
            uint acts = w.IActs[i];
            int flags = w.Spc[i] & 31;
            if (w.Sex[i] != 0) flags |= 1 << 5;
            span[5] = (byte)flags;
            int stagebits = st == St.Egg ? 0 : st == St.Juvenile ? 1 : 2;
            if ((w.Flags[i] & Flag.Carrying) != 0) stagebits |= 4;
            if (w.IScanning[i] != 0) stagebits |= 8;
            if ((acts & (1u << (P.REST - P.FIRST_ACTION))) != 0) stagebits |= 16;
            span[6] = (byte)(stagebits | ((int)(Math.Clamp(w.Energy[i] / MathF.Max(w.EmaxOf(i), 1e-3f), 0f, 1f) * 7f) << 5));
            span[7] = (byte)acts;
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8), w.Serial[i]);
            // the animal's own body genes, so the console can draw evolution (ICD: record bytes 12-15)
            int g = i * G.LEN;
            span[12] = (byte)w.Genome[g + G.B_MASS];
            span[13] = (byte)w.Genome[g + G.B_SPEED];
            span[14] = (byte)w.Genome[g + G.B_ARMOUR];
            var sp = w.Sp[w.Spc[i]];
            float meat = w.PDietA[i * Pools.N + (int)Pool.Meat] - sp.Adult.Diet[(int)Pool.Meat];   // diet drift toward (+) or away from (-) meat
            span[15] = (byte)(sbyte)Math.Clamp((int)MathF.Round(meat * 254f), -127, 127);
            o += REC; n++;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8), (uint)n);
        return o;
    }
}
