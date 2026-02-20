using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace DerpDoc.Runtime;

public sealed class LiveBinaryWriter : IDisposable
{
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private bool _disposed;

    public LiveBinaryHeader Header { get; private set; }

    public static LiveBinaryWriter CreateOrOpen(string filePath, int tableCount, long slotSize)
    {
        return new LiveBinaryWriter(filePath, tableCount, slotSize);
    }

    private LiveBinaryWriter(string filePath, int tableCount, long slotSize)
    {
        int headerSize = Marshal.SizeOf<LiveBinaryHeader>();
        long slot0Offset = Align(headerSize, BinaryFormat.DataAlignment);
        long slot1Offset = Align(slot0Offset + slotSize, BinaryFormat.DataAlignment);
        long requiredCapacity = slot1Offset + slotSize;

        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
        bool fileExists = File.Exists(filePath);
        long mappedCapacity = requiredCapacity;
        if (fileExists)
        {
            long existingLength = new FileInfo(filePath).Length;
            if (existingLength > mappedCapacity)
            {
                mappedCapacity = existingLength;
            }
        }

        _mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.OpenOrCreate, mapName: null, capacity: mappedCapacity, access: MemoryMappedFileAccess.ReadWrite);
        _accessor = _mmf.CreateViewAccessor(0, mappedCapacity, MemoryMappedFileAccess.ReadWrite);

        LiveBinaryHeader existingHeader = default;
        bool hasValidExistingHeader = false;
        bool useExistingHeader = false;
        if (fileExists)
        {
            try
            {
                _accessor.Read(0, out existingHeader);
                if (existingHeader.Magic == LiveBinaryHeader.MagicValue)
                {
                    hasValidExistingHeader = true;
                    if (existingHeader.SlotSize == slotSize &&
                        existingHeader.Slot0Offset == slot0Offset &&
                        existingHeader.Slot1Offset == slot1Offset)
                    {
                        Header = existingHeader;
                        useExistingHeader = true;
                    }
                }
            }
            catch
            {
                hasValidExistingHeader = false;
                useExistingHeader = false;
            }
        }

        if (!useExistingHeader)
        {
            uint preservedGeneration = hasValidExistingHeader ? existingHeader.Generation : 0u;
            int preservedActiveSlot = hasValidExistingHeader && (existingHeader.ActiveSlot == 0 || existingHeader.ActiveSlot == 1)
                ? existingHeader.ActiveSlot
                : 0;

            var header = new LiveBinaryHeader
            {
                Magic = LiveBinaryHeader.MagicValue,
                Generation = preservedGeneration,
                ActiveSlot = preservedActiveSlot,
                TableCount = tableCount,
                Slot0Offset = slot0Offset,
                Slot1Offset = slot1Offset,
                SlotSize = slotSize,
            };

            Header = header;
            _accessor.Write(0, ref header);
            _accessor.Flush();
            return;
        }

        if (Header.TableCount != tableCount)
        {
            Header = new LiveBinaryHeader
            {
                Magic = Header.Magic,
                Generation = Header.Generation,
                ActiveSlot = Header.ActiveSlot,
                TableCount = tableCount,
                Slot0Offset = Header.Slot0Offset,
                Slot1Offset = Header.Slot1Offset,
                SlotSize = Header.SlotSize,
            };

            LiveBinaryHeader headerToWrite = Header;
            _accessor.Write(0, ref headerToWrite);
            _accessor.Flush();
        }
    }

    public void Write(byte[] slotBytes)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LiveBinaryWriter));
        }

        if (slotBytes.Length != Header.SlotSize)
        {
            throw new InvalidOperationException($"Slot size mismatch: header expects {Header.SlotSize}, got {slotBytes.Length}.");
        }

        _accessor.Read(0, out LiveBinaryHeader header);
        int activeSlot = header.ActiveSlot;
        if (activeSlot != 0 && activeSlot != 1)
        {
            activeSlot = 0;
        }
        int inactiveSlot = 1 - activeSlot;

        long offset = inactiveSlot == 0 ? Header.Slot0Offset : Header.Slot1Offset;
        _accessor.WriteArray(offset, slotBytes, 0, slotBytes.Length);

        header.ActiveSlot = inactiveSlot;
        header.Generation++;
        Header = header;

        _accessor.Write(0, ref header);
        _accessor.Flush();
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

    private static long Align(long value, int alignment)
    {
        long remainder = value % alignment;
        return remainder == 0 ? value : value + (alignment - remainder);
    }
}
