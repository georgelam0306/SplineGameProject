using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DerpTech.Rollback;

/// <summary>
/// Records inputs to a binary file using sparse format (v3).
/// Only non-empty frames are stored, with a per-player presence mask.
/// File format v3:
/// - Header: Magic (4 bytes), Version (4 bytes), PlayerCount (4 bytes), InputSize (4 bytes), Seed (8 bytes)
/// - Per frame (only for non-empty): FrameNumber (4 bytes), PresenceMask (1 byte), then InputSize for each player with bit set
/// </summary>
public sealed class InputRecorder<TInput> : IDisposable
    where TInput : unmanaged, IGameInput<TInput>, IEquatable<TInput>
{
    private const uint MagicNumber = 0x52504C59; // "RPLY" - Replay
    private const int Version = 3;

    private readonly BinaryWriter _writer;
    private readonly int _playerCount;
    private readonly int _inputSize;
    private readonly byte[] _inputBuffer;
    private bool _disposed;
    private int _framesSinceFlush;
    private int _framesRecorded;
    private int _framesSkipped;

    public InputRecorder(string filePath, int playerCount, long seed)
    {
        _playerCount = playerCount;
        _inputSize = Unsafe.SizeOf<TInput>();
        _inputBuffer = new byte[_inputSize];

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _writer = new BinaryWriter(File.Create(filePath));

        // Write header
        _writer.Write(MagicNumber);
        _writer.Write(Version);
        _writer.Write(playerCount);
        _writer.Write(_inputSize);
        _writer.Write(seed);
        _writer.Flush(); // Ensure header is written immediately

        Console.WriteLine($"[InputRecorder] Recording to {filePath} (players={playerCount}, inputSize={_inputSize}, seed={seed})");
    }

    public void RecordFrame(int frame, ReadOnlySpan<TInput> inputs)
    {
        if (_disposed) return;

        // Build presence mask and check if any player has input
        byte presenceMask = 0;
        for (int i = 0; i < _playerCount && i < inputs.Length; i++)
        {
            if (!inputs[i].IsEmpty)
            {
                presenceMask |= (byte)(1 << i);
            }
        }

        // Skip entirely empty frames
        if (presenceMask == 0)
        {
            _framesSkipped++;
            return;
        }

        // Write frame number and presence mask
        _writer.Write(frame);
        _writer.Write(presenceMask);

        // Write only non-empty inputs
        for (int i = 0; i < _playerCount && i < inputs.Length; i++)
        {
            if ((presenceMask & (1 << i)) != 0)
            {
                MemoryMarshal.Write(_inputBuffer, in inputs[i]);
                _writer.Write(_inputBuffer);
            }
        }

        _framesRecorded++;

        // Flush to disk periodically (every ~1 second at 60fps)
        _framesSinceFlush++;
        if (_framesSinceFlush >= 60)
        {
            _writer.Flush();
            _framesSinceFlush = 0;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _writer.Flush();
        _writer.Dispose();
        int total = _framesRecorded + _framesSkipped;
        int percent = total > 0 ? (_framesSkipped * 100 / total) : 0;
        Console.WriteLine($"[InputRecorder] Recording complete ({_framesRecorded} frames stored, {_framesSkipped} empty frames skipped = {percent}% compression)");
    }
}

/// <summary>
/// Replays inputs from a recorded binary file (supports v3 sparse format).
/// Builds a frame index on load for efficient random access.
/// </summary>
public sealed class InputReplayer<TInput> : IDisposable
    where TInput : unmanaged, IGameInput<TInput>, IEquatable<TInput>
{
    private const uint MagicNumber = 0x52504C59; // "RPLY"

    private readonly BinaryReader _reader;
    private readonly int _playerCount;
    private readonly int _inputSize;
    private readonly long _seed;
    private readonly int _version;
    private readonly TInput[] _frameInputs;
    private readonly byte[] _inputBuffer;

    // Frame index: frame number → file position (only for v3 sparse format)
    private readonly Dictionary<int, long>? _frameIndex;
    private int _maxFrame;

    private int _currentFrame;
    private int _nextFrameInFile;
    private bool _endOfFile;
    private bool _disposed;

    public int PlayerCount => _playerCount;
    public long Seed => _seed;
    public bool EndOfFile => _endOfFile;
    public int CurrentFrame => _currentFrame;
    public int MaxFrame => _maxFrame;

    public InputReplayer(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Replay file not found: {filePath}");
        }

        _reader = new BinaryReader(File.OpenRead(filePath));

        // Read header
        uint magic = _reader.ReadUInt32();
        if (magic != MagicNumber)
        {
            throw new InvalidDataException($"Invalid replay file magic number: {magic:X8}");
        }

        _version = _reader.ReadInt32();
        if (_version != 3)
        {
            throw new InvalidDataException($"Unsupported replay file version: {_version} (only v3 supported)");
        }

        _playerCount = _reader.ReadInt32();
        _inputSize = _reader.ReadInt32();
        _seed = _reader.ReadInt64();

        // Validate input size matches expected type
        int expectedSize = Unsafe.SizeOf<TInput>();
        if (_inputSize != expectedSize)
        {
            throw new InvalidDataException($"Input size mismatch: file has {_inputSize} bytes, expected {expectedSize}");
        }

        _frameInputs = new TInput[_playerCount];
        _inputBuffer = new byte[_inputSize];
        _currentFrame = -1;
        _nextFrameInFile = -1;

        // Build frame index for v3 sparse format
        _frameIndex = new Dictionary<int, long>();
        BuildFrameIndex();

        Console.WriteLine($"[InputReplayer] Loaded replay from {filePath} (players={_playerCount}, inputSize={_inputSize}, seed={_seed})");
    }

    private void BuildFrameIndex()
    {
        long headerEnd = _reader.BaseStream.Position;

        while (_reader.BaseStream.Position < _reader.BaseStream.Length)
        {
            long framePos = _reader.BaseStream.Position;

            try
            {
                int frame = _reader.ReadInt32();
                byte presenceMask = _reader.ReadByte();

                _frameIndex![frame] = framePos;
                _maxFrame = Math.Max(_maxFrame, frame);

                // Skip input data for present players
                int playerCount = BitOperations.PopCount(presenceMask);
                _reader.BaseStream.Seek(playerCount * _inputSize, SeekOrigin.Current);
            }
            catch (EndOfStreamException)
            {
                break;
            }
        }

        // Reset to start of data
        _reader.BaseStream.Position = headerEnd;
        _nextFrameInFile = -1;

        // Pre-read first frame if any exist
        if (_frameIndex!.Count > 0)
        {
            ReadNextFrame();
        }
        else
        {
            _endOfFile = true;
        }
    }

    private void ReadNextFrame()
    {
        if (_endOfFile) return;

        try
        {
            if (_reader.BaseStream.Position >= _reader.BaseStream.Length)
            {
                _endOfFile = true;
                return;
            }

            _nextFrameInFile = _reader.ReadInt32();
            byte presenceMask = _reader.ReadByte();

            // Clear all inputs first
            for (int i = 0; i < _playerCount; i++)
            {
                _frameInputs[i] = TInput.Empty;
            }

            // Read only present player inputs
            for (int i = 0; i < _playerCount; i++)
            {
                if ((presenceMask & (1 << i)) != 0)
                {
                    int bytesRead = _reader.Read(_inputBuffer, 0, _inputSize);
                    if (bytesRead != _inputSize)
                    {
                        _endOfFile = true;
                        return;
                    }
                    _frameInputs[i] = MemoryMarshal.Read<TInput>(_inputBuffer);
                }
            }
        }
        catch (EndOfStreamException)
        {
            _endOfFile = true;
        }
    }

    /// <summary>
    /// Gets inputs for the specified frame. Returns true if inputs were available.
    /// For sparse format, missing frames return Empty for all players (which is valid).
    /// </summary>
    public bool TryGetInputsForFrame(int frame, Span<TInput> outputs)
    {
        // Past max frame = end of replay
        if (frame > _maxFrame)
        {
            return false;
        }

        // Check if this frame exists in index
        if (_frameIndex != null && !_frameIndex.ContainsKey(frame))
        {
            // Frame not stored = all players had empty input
            for (int i = 0; i < outputs.Length; i++)
            {
                outputs[i] = TInput.Empty;
            }
            return true;
        }

        // Advance to the requested frame
        while (!_endOfFile && _nextFrameInFile < frame)
        {
            ReadNextFrame();
        }

        if (_nextFrameInFile == frame)
        {
            _currentFrame = frame;
            for (int i = 0; i < _playerCount && i < outputs.Length; i++)
            {
                outputs[i] = _frameInputs[i];
            }

            // Pre-read next frame
            ReadNextFrame();
            return true;
        }

        // Should not reach here for sparse format with index
        return false;
    }

    /// <summary>
    /// Gets the input for a specific player at the current frame.
    /// </summary>
    public TInput GetInput(int playerId)
    {
        if (playerId < 0 || playerId >= _playerCount)
        {
            return TInput.Empty;
        }
        return _frameInputs[playerId];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _reader.Dispose();
    }
}
