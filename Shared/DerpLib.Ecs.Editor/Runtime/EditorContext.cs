namespace DerpLib.Ecs.Editor;

/// <summary>
/// Global editor context that owns the command stream. Thread-static for safety,
/// but editor is assumed single-threaded.
/// </summary>
public static class EditorContext
{
    [System.ThreadStatic]
    private static EcsEditorCommandStream? _stream;

    [System.ThreadStatic]
    private static EcsEditorCommandStream? _undoScratchStream;

    [System.ThreadStatic]
    private static EcsEditorCommandStream? _redoScratchStream;

    [System.ThreadStatic]
    private static EcsEditorCommandPipeline? _pipeline;

    [System.ThreadStatic]
    private static EcsEditorUndoStack? _undoStack;

    [System.ThreadStatic]
    private static int _activeSessionCount;

    internal static EcsEditorCommandStream Stream => _stream ??= new EcsEditorCommandStream(256);

    public static EcsEditorCommandPipeline? Pipeline
    {
        get => _pipeline;
        set => _pipeline = value;
    }

    public static EcsEditorUndoStack? UndoStack
    {
        get => _undoStack;
        set => _undoStack = value;
    }

    public static EcsEditorCommandStream UndoScratchStream => _undoScratchStream ??= new EcsEditorCommandStream(256);

    public static EcsEditorCommandStream RedoScratchStream => _redoScratchStream ??= new EcsEditorCommandStream(256);

    public static int BeginSession()
    {
        if (_activeSessionCount != 0)
        {
            throw new System.InvalidOperationException("Nested edit sessions not supported.");
        }

        _activeSessionCount = 1;
        return Stream.Count;
    }

    public static void EndSession(int startIndex)
    {
        if (_activeSessionCount != 1)
        {
            throw new System.InvalidOperationException("Edit session mismatch.");
        }

        _activeSessionCount = 0;
        Stream.Truncate(startIndex);
    }

    public static void Enqueue(in EcsEditorCommand cmd) => Stream.Enqueue(in cmd);

    public static System.ReadOnlySpan<EcsEditorCommand> Commands => Stream.Commands;

    public static void Clear() => Stream.Clear();
}
