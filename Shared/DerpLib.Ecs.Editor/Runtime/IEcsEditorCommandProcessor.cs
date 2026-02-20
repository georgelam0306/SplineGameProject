using System;

namespace DerpLib.Ecs.Editor;

public interface IEcsEditorCommandProcessor
{
    void Process(ReadOnlySpan<EcsEditorCommand> input, EcsEditorCommandStream output);
}

