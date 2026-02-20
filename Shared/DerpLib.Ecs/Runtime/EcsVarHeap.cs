using System;
using System.Runtime.CompilerServices;

namespace DerpLib.Ecs;

/// <summary>
/// Immutable-at-runtime variable data heap for baked resizables.
/// </summary>
public sealed class EcsVarHeap
{
    private byte[] _bytes;
    private int _usedBytes;

    public EcsVarHeap(int initialCapacityBytes = 0)
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

    public ReadOnlySpan<byte> Bytes
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _bytes.AsSpan(0, _usedBytes);
    }

    public void SetBytes(byte[] bytes, int usedBytes = -1)
    {
        if (bytes == null)
        {
            throw new ArgumentNullException(nameof(bytes));
        }

        if (usedBytes < 0)
        {
            usedBytes = bytes.Length;
        }

        if ((uint)usedBytes > (uint)bytes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(usedBytes));
        }

        _bytes = bytes;
        _usedBytes = usedBytes;
    }
}

