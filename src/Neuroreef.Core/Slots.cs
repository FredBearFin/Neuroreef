// Per-slot storage that grows (2026-10-31): a world has no fixed animal cap. Every per-slot array of World (and of its
// derived cache, World.D) is marked [PerSlot], and World.Resize reallocates them all to a new number of slots. The
// largest ones (genomes, working weights, traces: ~200 KB of a slot's ~210 KB) are SlotArrays, held in pages of whole
// slots, so no single .NET array has to hold every genome (an array holds at most 2^31 elements: one sbyte[] of
// genomes stopped at 21,504 slots) and growing copies only the last, partial page.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Neuroreef;

/// <summary>
/// A per-slot field: an array of Cap x stride elements (or a SlotArray), reallocated by World.Resize. Every array of
/// World or Derived whose size follows the number of slots carries it (EngineeringTests guards this).
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class PerSlotAttribute : Attribute { }

/// <summary>A per-slot array that is not reallocated as a whole: Stride elements per slot, in pages of whole slots.</summary>
public interface ISlotArray
{
    int Stride { get; }
    int Slots { get; }
    int ElementSize { get; }
    void Resize(int slots);
    /// <summary>The bytes of slots [slot, slot + count), which must lie in one page (PageSlots).</summary>
    Span<byte> Bytes(int slot, int count);
    /// <summary>Slots per page: a run of slots aligned to it lies in one page.</summary>
    int PageSlots { get; }
}

/// <summary>
/// Stride elements of T per slot, in pages of PageSlots whole slots (about PAGE_BYTES each; the last page holds only the
/// slots there are). Slot(i) is one slot's span; the indexer takes a flat index i x Stride + k, as a plain array did.
/// </summary>
public sealed class SlotArray<T> : ISlotArray where T : unmanaged
{
    /// <summary>Bytes a page holds at most (rounded down to a power of two of slots): genomes 512 slots a page, weights and
    /// traces 1,024. A world grows in blocks of World.GROW_BLOCK (1,024) slots, so its pages are whole and are never copied.</summary>
    public const int PAGE_BYTES = 1 << 26;

    public int Stride { get; }
    public int Slots { get; private set; }
    public int PageSlots => 1 << _shift;
    public int ElementSize => Unsafe.SizeOf<T>();
    readonly int _shift, _mask;
    T[][] _pages = Array.Empty<T[]>();

    public SlotArray(int stride, int slots)
    {
        Stride = stride;
        int per = Math.Max(1, PAGE_BYTES / (stride * Unsafe.SizeOf<T>()));
        _shift = System.Numerics.BitOperations.Log2((uint)per);
        _mask = (1 << _shift) - 1;
        Resize(slots);
    }

    /// <summary>Hold this many slots: those kept keep their values, new ones are zero. Only the last page is copied.</summary>
    public void Resize(int slots)
    {
        int per = 1 << _shift, np = (int)(((long)slots + per - 1) >> _shift);
        var pages = new T[np][];
        for (int p = 0; p < np; p++)
        {
            int n = Math.Min(per, slots - (p << _shift));
            var old = p < _pages.Length ? _pages[p] : null;
            if (old != null && old.Length == n * Stride) { pages[p] = old; continue; }
            var a = new T[n * Stride];
            if (old != null) Array.Copy(old, a, Math.Min(old.Length, a.Length));
            pages[p] = a;
        }
        _pages = pages;   // one reference: a reader on another thread sees the old pages or the new, never a mix
        Slots = slots;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> Slot(int i) => new(_pages[i >> _shift], (i & _mask) * Stride, Stride);

    /// <summary>Element k of slot i.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T At(int i, int k) => ref _pages[i >> _shift][(i & _mask) * Stride + k];

    /// <summary>Element index (slot index / Stride, k = index % Stride), as in a flat array of Slots x Stride.</summary>
    public ref T this[long index]
    {
        get
        {
            long s = index / Stride;
            if ((ulong)s >= (ulong)Slots) throw new IndexOutOfRangeException();
            return ref At((int)s, (int)(index - s * Stride));
        }
    }

    public Span<byte> Bytes(int slot, int count)
    {
        if (count == 0) return Span<byte>.Empty;
        if ((slot >> _shift) != ((slot + count - 1) >> _shift)) throw new ArgumentOutOfRangeException(nameof(count), "slots across pages");
        return System.Runtime.InteropServices.MemoryMarshal.AsBytes(new Span<T>(_pages[slot >> _shift], (slot & _mask) * Stride, count * Stride));
    }
}

/// <summary>Reallocation of every [PerSlot] field of an object (World, Derived) to a new number of slots.</summary>
public static class PerSlot
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Dictionary<Type, FieldInfo[]> _fields = new();

    /// <summary>The [PerSlot] fields of a type.</summary>
    public static FieldInfo[] Of(Type t)
    {
        lock (_fields)
        {
            if (!_fields.TryGetValue(t, out var f))
            {
                f = Array.FindAll(t.GetFields(Flags), x => x.IsDefined(typeof(PerSlotAttribute)));
                foreach (var x in f)
                    if (x.IsInitOnly && !typeof(ISlotArray).IsAssignableFrom(x.FieldType))
                        throw new InvalidOperationException($"{t.Name}.{x.Name} is [PerSlot] and readonly: it cannot be reallocated");
                _fields[t] = f;
            }
            return f;
        }
    }

    /// <summary>Reallocate o's per-slot fields from oldCap to newCap slots, keeping slots [0, min) and zeroing the rest.</summary>
    public static void Resize(object o, int oldCap, int newCap)
    {
        foreach (var f in Of(o.GetType()))
        {
            var v = f.GetValue(o);
            if (v is ISlotArray s) { s.Resize(newCap); continue; }
            var a = (Array)v!;
            int stride = a.Length / oldCap;
            var b = Array.CreateInstance(a.GetType().GetElementType()!, (long)newCap * stride);
            Array.Copy(a, b, Math.Min(a.LongLength, b.LongLength));
            f.SetValue(o, b);
        }
    }

    /// <summary>Bytes per slot of o's per-slot fields (o has cap slots).</summary>
    public static long BytesPerSlot(object o, int cap)
    {
        long n = 0;
        foreach (var f in Of(o.GetType()))
        {
            var v = f.GetValue(o);
            if (v is ISlotArray s) n += (long)s.Stride * s.ElementSize;
            else { var a = (Array)v!; n += a.LongLength / cap * System.Runtime.InteropServices.Marshal.SizeOf(a.GetType().GetElementType()!); }
        }
        return n;
    }
}
