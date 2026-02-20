namespace DerpTech.Rollback;

public sealed class SnapshotBuffer
{
    private readonly byte[][] _frames;
    private readonly int[] _usedBytes;
    private readonly int _frameCapacity;
    private readonly int _frameCount;

    public int FrameCount => _frameCount;
    public int FrameCapacity => _frameCapacity;

    private int SafeIndex(int frameNumber)
    {
        int index = frameNumber % _frameCount;
        return index < 0 ? index + _frameCount : index;
    }

    public SnapshotBuffer(int frameCount, int bytesPerFrame)
    {
        _frameCount = frameCount;
        _frameCapacity = bytesPerFrame;
        _frames = new byte[frameCount][];
        _usedBytes = new int[frameCount];

        for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            _frames[frameIndex] = new byte[bytesPerFrame];
        }
    }

    public Span<byte> GetWriteBuffer(int frameNumber)
    {
        int index = SafeIndex(frameNumber);
        return _frames[index];
    }

    public ReadOnlySpan<byte> GetReadBuffer(int frameNumber)
    {
        int index = SafeIndex(frameNumber);
        return _frames[index].AsSpan(0, _usedBytes[index]);
    }

    public void SetUsedBytes(int frameNumber, int byteCount)
    {
        int index = SafeIndex(frameNumber);
        _usedBytes[index] = byteCount;
    }

    public int GetUsedBytes(int frameNumber)
    {
        int index = SafeIndex(frameNumber);
        return _usedBytes[index];
    }

    public bool HasFrame(int frameNumber, int currentFrame)
    {
        if (frameNumber < 0)
        {
            return false;
        }
        int oldestValidFrame = Math.Max(0, currentFrame - _frameCount + 1);
        return frameNumber >= oldestValidFrame && frameNumber <= currentFrame;
    }

    public void Clear()
    {
        for (int frameIndex = 0; frameIndex < _frameCount; frameIndex++)
        {
            Array.Clear(_frames[frameIndex], 0, _frames[frameIndex].Length);
            _usedBytes[frameIndex] = 0;
        }
    }
}
