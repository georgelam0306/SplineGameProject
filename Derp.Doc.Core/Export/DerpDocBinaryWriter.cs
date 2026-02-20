using System.Text;

namespace Derp.Doc.Export;

internal sealed class DerpDocBinaryWriter
{
    private readonly List<BinaryTableSection> _tables = new();
    private readonly List<(uint Id, string Value)> _stringRegistry = new();

    public void AddTable(BinaryTableSection table)
    {
        _tables.Add(table);
    }

    public void SetStringRegistry(IEnumerable<(uint Id, string Value)> entries)
    {
        _stringRegistry.Clear();
        foreach (var entry in entries)
        {
            _stringRegistry.Add(entry);
        }
    }

    public byte[] Build()
    {
        using var stream = new MemoryStream(64 * 1024);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        const int headerSize = 24;
        long headerPos = stream.Position;

        // Header placeholder
        WriteHeader(writer,
            magic: DerpDocBinaryFormat.Magic,
            version: DerpDocBinaryFormat.Version,
            tableCount: (uint)_tables.Count,
            checksum: 0,
            stringRegistryOffset: 0,
            stringRegistryCount: 0);

        // Directory placeholder
        long directoryPos = stream.Position;
        for (int i = 0; i < _tables.Count; i++)
        {
            WriteDirectoryEntry(writer, 0, 0, 0, 0, 0, 0);
        }

        // String table for table names
        int stringTableStart = checked((int)stream.Position);
        var nameOffsets = new uint[_tables.Count];
        for (int i = 0; i < _tables.Count; i++)
        {
            nameOffsets[i] = checked((uint)(stream.Position - stringTableStart));
            byte[] nameBytes = Encoding.UTF8.GetBytes(_tables[i].Name);
            writer.Write(nameBytes);
            writer.Write((byte)0);
        }

        Align(writer, stream, DerpDocBinaryFormat.DataAlignment);

        var directoryEntries = new (uint NameOffset, uint RecordOffset, uint RecordCount, uint RecordSize, uint SlotArrayOffset, uint SlotArrayLength)[_tables.Count];

        for (int i = 0; i < _tables.Count; i++)
        {
            var table = _tables[i];
            uint recordOffset = checked((uint)stream.Position);
            writer.Write(table.Records);
            Align(writer, stream, DerpDocBinaryFormat.DataAlignment);

            uint slotArrayOffset = checked((uint)stream.Position);
            for (int s = 0; s < table.SlotArray.Length; s++)
            {
                writer.Write(table.SlotArray[s]);
            }
            Align(writer, stream, DerpDocBinaryFormat.DataAlignment);

            directoryEntries[i] = (
                NameOffset: nameOffsets[i],
                RecordOffset: recordOffset,
                RecordCount: table.RecordCount,
                RecordSize: checked((uint)table.RecordSize),
                SlotArrayOffset: slotArrayOffset,
                SlotArrayLength: checked((uint)table.SlotArray.Length));
        }

        // String registry section
        Align(writer, stream, DerpDocBinaryFormat.DataAlignment);
        uint stringRegistryOffsetFinal = 0;
        uint stringRegistryCountFinal = 0;
        if (_stringRegistry.Count > 0)
        {
            stringRegistryOffsetFinal = checked((uint)stream.Position);
            stringRegistryCountFinal = checked((uint)_stringRegistry.Count);

            for (int i = 0; i < _stringRegistry.Count; i++)
            {
                var entry = _stringRegistry[i];
                writer.Write(entry.Id);
                byte[] bytes = Encoding.UTF8.GetBytes(entry.Value);
                if (bytes.Length > ushort.MaxValue)
                {
                    throw new InvalidOperationException($"String registry entry too long: {bytes.Length} bytes.");
                }
                writer.Write((ushort)bytes.Length);
                writer.Write(bytes);
            }
        }

        // Patch directory
        stream.Position = directoryPos;
        for (int i = 0; i < directoryEntries.Length; i++)
        {
            var e = directoryEntries[i];
            WriteDirectoryEntry(writer, e.NameOffset, e.RecordOffset, e.RecordCount, e.RecordSize, e.SlotArrayOffset, e.SlotArrayLength);
        }

        // Compute checksum of everything after header.
        stream.Position = headerSize;
        byte[] data = stream.ToArray();
        uint checksum = Crc32.Compute(data.AsSpan(headerSize));

        // Patch header
        stream.Position = headerPos;
        WriteHeader(writer,
            magic: DerpDocBinaryFormat.Magic,
            version: DerpDocBinaryFormat.Version,
            tableCount: (uint)_tables.Count,
            checksum: checksum,
            stringRegistryOffset: stringRegistryOffsetFinal,
            stringRegistryCount: stringRegistryCountFinal);

        return stream.ToArray();
    }

    private static void Align(BinaryWriter writer, Stream stream, int alignment)
    {
        while (stream.Position % alignment != 0)
        {
            writer.Write((byte)0);
        }
    }

    private static void WriteHeader(
        BinaryWriter writer,
        uint magic,
        uint version,
        uint tableCount,
        uint checksum,
        uint stringRegistryOffset,
        uint stringRegistryCount)
    {
        writer.Write(magic);
        writer.Write(version);
        writer.Write(tableCount);
        writer.Write(checksum);
        writer.Write(stringRegistryOffset);
        writer.Write(stringRegistryCount);
    }

    private static void WriteDirectoryEntry(
        BinaryWriter writer,
        uint nameOffset,
        uint recordOffset,
        uint recordCount,
        uint recordSize,
        uint slotArrayOffset,
        uint slotArrayLength)
    {
        writer.Write(nameOffset);
        writer.Write(recordOffset);
        writer.Write(recordCount);
        writer.Write(recordSize);
        writer.Write(slotArrayOffset);
        writer.Write(slotArrayLength);
    }
}

