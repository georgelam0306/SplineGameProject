using System;

namespace DerpLib.Ecs.Editor;

public sealed class EcsEditorAutoKeyProcessor : IEcsEditorCommandProcessor
{
    private readonly IEcsEditorAutoKeySink _sink;

    public EcsEditorAutoKeyProcessor(IEcsEditorAutoKeySink sink)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    public void Process(ReadOnlySpan<EcsEditorCommand> input, EcsEditorCommandStream output)
    {
        for (int i = 0; i < input.Length; i++)
        {
            ref readonly EcsEditorCommand cmd = ref input[i];
            if (cmd.EditMode == EcsEditMode.Change)
            {
                _sink.OnCommand(in cmd);
            }

            output.Enqueue(in cmd);
        }
    }
}

