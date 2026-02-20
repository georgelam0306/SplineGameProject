using System;

namespace DerpLib.Ecs.Editor;

public sealed class EcsEditorCommandPipeline
{
    private readonly IEcsEditorCommandProcessor[] _processors;
    private readonly EcsEditorCommandStream _scratchA;
    private readonly EcsEditorCommandStream _scratchB;

    public EcsEditorCommandPipeline(IEcsEditorCommandProcessor[] processors, int scratchCapacity = 256)
    {
        _processors = processors ?? Array.Empty<IEcsEditorCommandProcessor>();
        _scratchA = new EcsEditorCommandStream(scratchCapacity);
        _scratchB = new EcsEditorCommandStream(scratchCapacity);
    }

    public ReadOnlySpan<EcsEditorCommand> Run(ReadOnlySpan<EcsEditorCommand> input)
    {
        ReadOnlySpan<EcsEditorCommand> current = input;
        EcsEditorCommandStream output = _scratchA;
        EcsEditorCommandStream next = _scratchB;

        output.Clear();
        next.Clear();

        for (int i = 0; i < _processors.Length; i++)
        {
            output.Clear();
            _processors[i].Process(current, output);
            current = output.Commands;

            // Swap scratch buffers for the next stage.
            EcsEditorCommandStream tmp = output;
            output = next;
            next = tmp;
        }

        return current;
    }
}

