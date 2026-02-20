namespace DerpTech.Rollback;

/// <summary>
/// Buffer for pending network inputs that haven't been applied yet.
/// Used to queue remote inputs before they're processed by the rollback system.
/// </summary>
public sealed class PendingInputBuffer<TInput>
    where TInput : unmanaged, IGameInput<TInput>, IEquatable<TInput>
{
    public readonly struct PendingInput
    {
        public readonly int Frame;
        public readonly int PlayerId;
        public readonly TInput Input;

        public PendingInput(int frame, int playerId, in TInput input)
        {
            Frame = frame;
            PlayerId = playerId;
            Input = input;
        }
    }

    private readonly PendingInput[] _pending;
    private int _count;

    public int Count => _count;

    public PendingInputBuffer(int capacity = 256)
    {
        _pending = new PendingInput[capacity];
        _count = 0;
    }

    public void Enqueue(int frame, int playerId, in TInput input)
    {
        if (_count >= _pending.Length)
        {
            return;
        }

        _pending[_count] = new PendingInput(frame, playerId, in input);
        _count++;
    }

    public ref readonly PendingInput Get(int index)
    {
        return ref _pending[index];
    }

    public void Clear()
    {
        _count = 0;
    }
}
