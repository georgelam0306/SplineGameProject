using System;
using System.Runtime.CompilerServices;

namespace DerpLib.Ecs;

/// <summary>
/// Reusable command buffer storage for structural changes. Intended to be allocated once
/// and reused every frame (Clear + Append), with occasional growth.
/// </summary>
public sealed class EcsCommandBuffer<TCommand> where TCommand : unmanaged
{
    private TCommand[] _buffer;
    private int _count;

    public EcsCommandBuffer(int initialCapacity = 128)
    {
        if (initialCapacity < 1)
        {
            initialCapacity = 1;
        }

        _buffer = new TCommand[initialCapacity];
        _count = 0;
    }

    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _count;
    }

    public bool HasCommands
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _count != 0;
    }

    public ReadOnlySpan<TCommand> Commands
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer.AsSpan(0, _count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        _count = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Truncate(int count)
    {
        if (count < 0)
        {
            count = 0;
        }

        if (count > _count)
        {
            count = _count;
        }

        _count = count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Enqueue(in TCommand command)
    {
        int index = _count;
        if ((uint)index >= (uint)_buffer.Length)
        {
            Grow(index + 1);
        }

        _buffer[index] = command;
        _count = index + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int requiredCapacity)
    {
        int oldLength = _buffer.Length;
        int newLength = oldLength * 2;
        if (newLength < requiredCapacity)
        {
            newLength = requiredCapacity;
        }

        Array.Resize(ref _buffer, newLength);
    }
}
