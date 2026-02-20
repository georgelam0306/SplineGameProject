using Derp.Doc.Model;

namespace Derp.Doc.Commands;

/// <summary>
/// List-based undo/redo stack. Executing a new command clears the redo history.
/// </summary>
internal sealed class DocUndoStack
{
    private readonly List<DocCommand> _undoStack = new();
    private readonly List<DocCommand> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void Execute(DocCommand command, DocProject project)
    {
        command.Execute(project);
        _undoStack.Add(command);
        _redoStack.Clear();
    }

    public void Undo(DocProject project)
    {
        if (_undoStack.Count == 0) return;

        int last = _undoStack.Count - 1;
        var command = _undoStack[last];
        _undoStack.RemoveAt(last);

        command.Undo(project);
        _redoStack.Add(command);
    }

    public bool TryPeekUndoCommandKind(out DocCommandKind kind)
    {
        if (_undoStack.Count == 0)
        {
            kind = default;
            return false;
        }

        kind = _undoStack[_undoStack.Count - 1].Kind;
        return true;
    }

    public bool TryPeekUndoCommand(out DocCommand command)
    {
        if (_undoStack.Count == 0)
        {
            command = null!;
            return false;
        }

        command = _undoStack[_undoStack.Count - 1];
        return true;
    }

    public void Redo(DocProject project)
    {
        if (_redoStack.Count == 0) return;

        int last = _redoStack.Count - 1;
        var command = _redoStack[last];
        _redoStack.RemoveAt(last);

        command.Execute(project);
        _undoStack.Add(command);
    }

    public bool TryPeekRedoCommandKind(out DocCommandKind kind)
    {
        if (_redoStack.Count == 0)
        {
            kind = default;
            return false;
        }

        kind = _redoStack[_redoStack.Count - 1].Kind;
        return true;
    }

    public bool TryPeekRedoCommand(out DocCommand command)
    {
        if (_redoStack.Count == 0)
        {
            command = null!;
            return false;
        }

        command = _redoStack[_redoStack.Count - 1];
        return true;
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}
