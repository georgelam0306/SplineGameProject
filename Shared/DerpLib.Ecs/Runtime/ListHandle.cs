using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DerpLib.Ecs;

/// <summary>
/// Handle to a variable-length list stored in a baked var-heap (immutable at runtime).
/// Offset is in bytes relative to the owning heap buffer.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct ListHandle<T> where T : unmanaged
{
    public static readonly ListHandle<T> Invalid = default;

    public readonly int OffsetBytes;
    public readonly int Count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ListHandle(int offsetBytes, int count)
    {
        OffsetBytes = offsetBytes;
        Count = count;
    }

    public bool IsValid
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => OffsetBytes >= 0 && Count > 0;
    }

    public override string ToString()
    {
        return IsValid
            ? $"ListHandle<{typeof(T).Name}>(offsetBytes={OffsetBytes}, count={Count})"
            : $"ListHandle<{typeof(T).Name}>(Invalid)";
    }
}

