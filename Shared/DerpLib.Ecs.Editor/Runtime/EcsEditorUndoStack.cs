using System;
using System.Buffers;

namespace DerpLib.Ecs.Editor;

public sealed class EcsEditorUndoStack : IDisposable
{
    private struct Frame
    {
        public EcsEditorCommand[]? Undo;
        public int UndoCount;
        public EcsEditorCommand[]? Redo;
        public int RedoCount;

        public void Release()
        {
            if (Undo != null)
            {
                ArrayPool<EcsEditorCommand>.Shared.Return(Undo, clearArray: false);
                Undo = null;
                UndoCount = 0;
            }

            if (Redo != null)
            {
                ArrayPool<EcsEditorCommand>.Shared.Return(Redo, clearArray: false);
                Redo = null;
                RedoCount = 0;
            }
        }
    }

    private readonly Frame[] _frames;
    private int _count;
    private int _cursor;

    public EcsEditorUndoStack(int maxTransactions = 64)
    {
        if (maxTransactions < 1)
        {
            maxTransactions = 1;
        }

        _frames = new Frame[maxTransactions];
        _count = 0;
        _cursor = 0;
    }

    public int MaxTransactions => _frames.Length;

    public int Count => _count;

    public bool CanUndo => _cursor > 0;

    public bool CanRedo => _cursor < _count;

    public void Clear()
    {
        for (int i = 0; i < _count; i++)
        {
            _frames[i].Release();
        }

        _count = 0;
        _cursor = 0;
    }

    public void Push(ReadOnlySpan<EcsEditorCommand> undoCommands, ReadOnlySpan<EcsEditorCommand> redoCommands)
    {
        if (undoCommands.Length == 0 || redoCommands.Length == 0)
        {
            return;
        }

        TruncateRedoHistory();

        if (_count >= _frames.Length)
        {
            DropOldest();
        }

        ref Frame frame = ref _frames[_count];
        frame.Undo = RentAndCopyReversed(undoCommands, out frame.UndoCount);
        frame.Redo = RentAndCopy(redoCommands, out frame.RedoCount);

        _count++;
        _cursor = _count;
    }

    public bool TryUndo(out ReadOnlySpan<EcsEditorCommand> undoCommands)
    {
        if (_cursor <= 0)
        {
            undoCommands = ReadOnlySpan<EcsEditorCommand>.Empty;
            return false;
        }

        _cursor--;
        Frame frame = _frames[_cursor];
        undoCommands = frame.Undo == null ? ReadOnlySpan<EcsEditorCommand>.Empty : frame.Undo.AsSpan(0, frame.UndoCount);
        return undoCommands.Length != 0;
    }

    public bool TryRedo(out ReadOnlySpan<EcsEditorCommand> redoCommands)
    {
        if (_cursor >= _count)
        {
            redoCommands = ReadOnlySpan<EcsEditorCommand>.Empty;
            return false;
        }

        Frame frame = _frames[_cursor];
        _cursor++;
        redoCommands = frame.Redo == null ? ReadOnlySpan<EcsEditorCommand>.Empty : frame.Redo.AsSpan(0, frame.RedoCount);
        return redoCommands.Length != 0;
    }

    public void Dispose()
    {
        Clear();
    }

    private void TruncateRedoHistory()
    {
        for (int i = _cursor; i < _count; i++)
        {
            _frames[i].Release();
        }

        _count = _cursor;
    }

    private void DropOldest()
    {
        _frames[0].Release();

        int lastIndex = _frames.Length - 1;
        for (int i = 0; i < lastIndex; i++)
        {
            _frames[i] = _frames[i + 1];
        }

        _frames[lastIndex] = default;

        if (_cursor > 0)
        {
            _cursor--;
        }

        if (_count > 0)
        {
            _count--;
        }
    }

    private static EcsEditorCommand[] RentAndCopy(ReadOnlySpan<EcsEditorCommand> commands, out int count)
    {
        count = commands.Length;
        EcsEditorCommand[] buffer = ArrayPool<EcsEditorCommand>.Shared.Rent(count);
        commands.CopyTo(buffer);
        return buffer;
    }

    private static EcsEditorCommand[] RentAndCopyReversed(ReadOnlySpan<EcsEditorCommand> commands, out int count)
    {
        count = commands.Length;
        EcsEditorCommand[] buffer = ArrayPool<EcsEditorCommand>.Shared.Rent(count);
        for (int i = 0; i < count; i++)
        {
            buffer[i] = commands[count - 1 - i];
        }
        return buffer;
    }
}
