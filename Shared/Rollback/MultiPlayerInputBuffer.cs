namespace DerpTech.Rollback;

/// <summary>
/// Circular buffer storing inputs for multiple players across multiple frames.
/// Supports both confirmed inputs from the network and predicted inputs for local simulation.
/// </summary>
public sealed class MultiPlayerInputBuffer<TInput>
    where TInput : unmanaged, IGameInput<TInput>, IEquatable<TInput>
{
    private readonly TInput[][] _inputs;
    private readonly bool[][] _inputConfirmed;
    private readonly int[][] _storedFrame;
    private readonly int[] _lastConfirmedFrame;
    private readonly int _frameCapacity;
    private readonly int _maxPlayers;

    public int FrameCapacity => _frameCapacity;
    public int MaxPlayers => _maxPlayers;

    public MultiPlayerInputBuffer(int frameCapacity, int maxPlayers)
    {
        _frameCapacity = frameCapacity;
        _maxPlayers = maxPlayers;

        _inputs = new TInput[frameCapacity][];
        _inputConfirmed = new bool[frameCapacity][];
        _storedFrame = new int[frameCapacity][];
        _lastConfirmedFrame = new int[maxPlayers];

        for (int frameIndex = 0; frameIndex < frameCapacity; frameIndex++)
        {
            _inputs[frameIndex] = new TInput[maxPlayers];
            _inputConfirmed[frameIndex] = new bool[maxPlayers];
            _storedFrame[frameIndex] = new int[maxPlayers];

            for (int playerIndex = 0; playerIndex < maxPlayers; playerIndex++)
            {
                _storedFrame[frameIndex][playerIndex] = -1;
            }
        }

        for (int playerIndex = 0; playerIndex < maxPlayers; playerIndex++)
        {
            _lastConfirmedFrame[playerIndex] = -1;
        }
    }

    public void StoreInput(int frame, int playerId, in TInput input)
    {
        if (playerId < 0 || playerId >= _maxPlayers)
        {
            return;
        }

        int bufferIndex = frame % _frameCapacity;
        _inputs[bufferIndex][playerId] = input;
        _inputConfirmed[bufferIndex][playerId] = true;
        _storedFrame[bufferIndex][playerId] = frame;

        if (frame > _lastConfirmedFrame[playerId])
        {
            _lastConfirmedFrame[playerId] = frame;
        }
    }

    public void StorePredictedInput(int frame, int playerId, in TInput input)
    {
        if (playerId < 0 || playerId >= _maxPlayers)
        {
            return;
        }

        int bufferIndex = frame % _frameCapacity;
        bool hasConfirmedForThisFrame = _inputConfirmed[bufferIndex][playerId]
            && _storedFrame[bufferIndex][playerId] == frame;

        if (!hasConfirmedForThisFrame)
        {
            _inputs[bufferIndex][playerId] = input;
            _inputConfirmed[bufferIndex][playerId] = false;
            _storedFrame[bufferIndex][playerId] = frame;
        }
    }

    public ref readonly TInput GetInput(int frame, int playerId)
    {
        int bufferIndex = frame % _frameCapacity;
        return ref _inputs[bufferIndex][playerId];
    }

    public bool HasInput(int frame, int playerId)
    {
        if (playerId < 0 || playerId >= _maxPlayers)
        {
            return false;
        }

        int bufferIndex = frame % _frameCapacity;
        return _inputConfirmed[bufferIndex][playerId]
            && _storedFrame[bufferIndex][playerId] == frame;
    }

    public bool HasAllInputs(int frame, int playerCount)
    {
        int bufferIndex = frame % _frameCapacity;

        for (int playerIndex = 0; playerIndex < playerCount; playerIndex++)
        {
            bool hasConfirmedForThisFrame = _inputConfirmed[bufferIndex][playerIndex]
                && _storedFrame[bufferIndex][playerIndex] == frame;

            if (!hasConfirmedForThisFrame)
            {
                return false;
            }
        }

        return true;
    }

    public int GetLastConfirmedFrame(int playerId)
    {
        if (playerId < 0 || playerId >= _maxPlayers)
        {
            return -1;
        }

        return _lastConfirmedFrame[playerId];
    }

    public int GetOldestUnconfirmedFrame(int playerCount)
    {
        int oldestUnconfirmed = int.MaxValue;

        for (int playerIndex = 0; playerIndex < playerCount; playerIndex++)
        {
            int lastConfirmed = _lastConfirmedFrame[playerIndex];
            int unconfirmedFrame = lastConfirmed + 1;

            if (unconfirmedFrame < oldestUnconfirmed)
            {
                oldestUnconfirmed = unconfirmedFrame;
            }
        }

        return oldestUnconfirmed;
    }

    public void ClearFrame(int frame)
    {
        int bufferIndex = frame % _frameCapacity;

        for (int playerIndex = 0; playerIndex < _maxPlayers; playerIndex++)
        {
            _inputs[bufferIndex][playerIndex] = default;
            _inputConfirmed[bufferIndex][playerIndex] = false;
            _storedFrame[bufferIndex][playerIndex] = -1;
        }
    }

    public void GetAllInputs(int frame, Span<TInput> outputInputs)
    {
        int bufferIndex = frame % _frameCapacity;

        for (int playerIndex = 0; playerIndex < _maxPlayers && playerIndex < outputInputs.Length; playerIndex++)
        {
            outputInputs[playerIndex] = _inputs[bufferIndex][playerIndex];
        }
    }

    /// <summary>
    /// Gets all confirmed inputs for a frame. Unconfirmed slots get Empty.
    /// Use this for replay recording to ensure only confirmed inputs are saved.
    /// </summary>
    public void GetAllConfirmedInputs(int frame, Span<TInput> outputInputs)
    {
        int bufferIndex = frame % _frameCapacity;

        for (int playerIndex = 0; playerIndex < _maxPlayers && playerIndex < outputInputs.Length; playerIndex++)
        {
            bool isConfirmed = _inputConfirmed[bufferIndex][playerIndex]
                && _storedFrame[bufferIndex][playerIndex] == frame;

            outputInputs[playerIndex] = isConfirmed ? _inputs[bufferIndex][playerIndex] : TInput.Empty;
        }
    }

    public TInput PredictInput(int playerId)
    {
        if (playerId < 0 || playerId >= _maxPlayers)
        {
            return TInput.Empty;
        }

        int lastFrame = _lastConfirmedFrame[playerId];
        if (lastFrame < 0)
        {
            return TInput.Empty;
        }

        int bufferIndex = lastFrame % _frameCapacity;
        return _inputs[bufferIndex][playerId];
    }

    public void Clear()
    {
        for (int frameIndex = 0; frameIndex < _frameCapacity; frameIndex++)
        {
            for (int playerIndex = 0; playerIndex < _maxPlayers; playerIndex++)
            {
                _inputs[frameIndex][playerIndex] = default;
                _inputConfirmed[frameIndex][playerIndex] = false;
                _storedFrame[frameIndex][playerIndex] = -1;
            }
        }

        for (int playerIndex = 0; playerIndex < _maxPlayers; playerIndex++)
        {
            _lastConfirmedFrame[playerIndex] = -1;
        }
    }
}
