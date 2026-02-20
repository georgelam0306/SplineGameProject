using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DerpLib.Ecs;

/// <summary>
/// Read-only view over baked resizable data in a var-heap.
/// </summary>
public readonly ref struct ResizableReadOnlyView<T> where T : unmanaged
{
    private readonly ReadOnlySpan<T> _span;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ResizableReadOnlyView(ReadOnlySpan<byte> heapBytes, in ListHandle<T> handle)
    {
        if (!handle.IsValid)
        {
            _span = ReadOnlySpan<T>.Empty;
            return;
        }

        int elementSize = Unsafe.SizeOf<T>();
        int byteCount = handle.Count * elementSize;
        if (handle.OffsetBytes < 0 || byteCount < 0)
        {
            _span = ReadOnlySpan<T>.Empty;
            return;
        }

        int endBytes = handle.OffsetBytes + byteCount;
        if ((uint)endBytes > (uint)heapBytes.Length)
        {
            _span = ReadOnlySpan<T>.Empty;
            return;
        }

        ReadOnlySpan<byte> slice = heapBytes.Slice(handle.OffsetBytes, byteCount);
        _span = MemoryMarshal.Cast<byte, T>(slice);
    }

    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _span.Length;
    }

    public ReadOnlySpan<T> Span
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _span;
    }

    public ref readonly T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref _span[index];
    }
}

