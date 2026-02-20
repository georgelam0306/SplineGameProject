using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DerpLib.Ecs;

/// <summary>
/// Tooling/bake helper for constructing a var-heap and producing list handles.
/// Not intended for per-frame runtime allocation.
/// </summary>
public sealed class EcsVarHeapBuilder
{
    private byte[] _bytes;
    private int _usedBytes;

    public EcsVarHeapBuilder(int initialCapacityBytes = 4096)
    {
        if (initialCapacityBytes < 0)
        {
            initialCapacityBytes = 0;
        }

        _bytes = initialCapacityBytes == 0 ? Array.Empty<byte>() : new byte[initialCapacityBytes];
        _usedBytes = 0;
    }

    public int UsedBytes
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _usedBytes;
    }

    public byte[] ToArray()
    {
        if (_usedBytes == 0)
        {
            return Array.Empty<byte>();
        }

        var dst = new byte[_usedBytes];
        _bytes.AsSpan(0, _usedBytes).CopyTo(dst);
        return dst;
    }

    public ListHandle<T> Add<T>(ReadOnlySpan<T> data) where T : unmanaged
    {
        if (data.Length == 0)
        {
            return ListHandle<T>.Invalid;
        }

        const int alignment = 16;
        int offsetBytes = Align(_usedBytes, alignment);

        int byteCount = data.Length * Unsafe.SizeOf<T>();
        EnsureCapacity(offsetBytes + byteCount);

        Span<byte> dst = _bytes.AsSpan(offsetBytes, byteCount);
        ReadOnlySpan<byte> src = MemoryMarshal.AsBytes(data);
        src.CopyTo(dst);

        _usedBytes = offsetBytes + byteCount;
        return new ListHandle<T>(offsetBytes, data.Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Align(int value, int alignment)
    {
        int mask = alignment - 1;
        return (value + mask) & ~mask;
    }

    private void EnsureCapacity(int requiredBytes)
    {
        if (requiredBytes <= _bytes.Length)
        {
            return;
        }

        int newLength = _bytes.Length == 0 ? 256 : _bytes.Length;
        while (newLength < requiredBytes)
        {
            newLength *= 2;
        }

        Array.Resize(ref _bytes, newLength);
    }
}
