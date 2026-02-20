using System.IO.MemoryMappedFiles;

namespace GameDocDatabase.Runtime;

public sealed class LiveBinaryReader : IDisposable
{
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly string _filePath;
    private bool _disposed;

    public static LiveBinaryReader Open(string filePath)
    {
        return new LiveBinaryReader(filePath);
    }

    private LiveBinaryReader(string filePath)
    {
        _filePath = filePath;
        _mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        _accessor = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
    }

    public LiveBinaryHeader ReadHeader()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LiveBinaryReader));
        }

        _accessor.Read(0, out LiveBinaryHeader header);
        return header;
    }

    public BinaryLoader LoadActiveSlot()
    {
        var header = ReadHeader();
        if (header.Magic != LiveBinaryHeader.MagicValue)
        {
            throw new InvalidDataException($"Invalid live magic: expected {LiveBinaryHeader.MagicValue:X8}, got {header.Magic:X8}");
        }

        long offset = header.ActiveSlot == 0 ? header.Slot0Offset : header.Slot1Offset;
        return BinaryLoader.Load(_filePath, offset, header.SlotSize);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _accessor.Dispose();
        _mmf.Dispose();
    }
}
