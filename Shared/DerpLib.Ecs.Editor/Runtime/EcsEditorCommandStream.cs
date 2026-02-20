using System;
using DerpLib.Ecs;

namespace DerpLib.Ecs.Editor;

/// <summary>
/// Reusable buffer for editor/user-intent commands. This is intentionally separate from any world
/// so it can be used by tooling without depending on a concrete ECS implementation.
/// </summary>
public sealed class EcsEditorCommandStream
{
    private readonly EcsCommandBuffer<EcsEditorCommand> _buffer;

    public EcsEditorCommandStream(int initialCapacity = 128)
    {
        _buffer = new EcsCommandBuffer<EcsEditorCommand>(initialCapacity);
    }

    public ReadOnlySpan<EcsEditorCommand> Commands => _buffer.Commands;

    public int Count => _buffer.Count;

    public bool HasCommands => _buffer.HasCommands;

    public void Clear() => _buffer.Clear();

    public void Truncate(int count) => _buffer.Truncate(count);

    public void Enqueue(in EcsEditorCommand command) => _buffer.Enqueue(in command);
}
