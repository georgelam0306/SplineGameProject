using System.Collections.Generic;
using Property.Runtime;

namespace Derp.UI;

internal sealed class UndoStack
{
    private readonly List<UndoRecord> _undo = new(capacity: 128);
    private readonly List<UndoRecord> _redo = new(capacity: 128);

    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    public void ClearRedo()
    {
        _redo.Clear();
    }

    public void Push(in UndoRecord record)
    {
        _undo.Add(record);
        _redo.Clear();
    }

    public bool TryPopUndo(out UndoRecord record)
    {
        int count = _undo.Count;
        if (count <= 0)
        {
            record = default;
            return false;
        }

        int index = count - 1;
        record = _undo[index];
        _undo.RemoveAt(index);
        return true;
    }

    public bool TryPopRedo(out UndoRecord record)
    {
        int count = _redo.Count;
        if (count <= 0)
        {
            record = default;
            return false;
        }

        int index = count - 1;
        record = _redo[index];
        _redo.RemoveAt(index);
        return true;
    }

    public void PushRedo(in UndoRecord record)
    {
        _redo.Add(record);
    }

    public void PushUndo(in UndoRecord record)
    {
        _undo.Add(record);
    }
}

