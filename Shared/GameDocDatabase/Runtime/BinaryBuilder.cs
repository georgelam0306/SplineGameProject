using System.Text;
using System.Text.Json;

namespace GameDocDatabase.Runtime;

/// <summary>
/// String registry entry for serialization.
/// </summary>
public readonly struct StringRegistryEntry
{
    public readonly uint Id;
    public readonly string Value;

    public StringRegistryEntry(uint id, string value)
    {
        Id = id;
        Value = value;
    }
}

/// <summary>
/// Builds a binary GameDocDb file from JSON data files.
/// </summary>
public class BinaryBuilder
{
    private readonly List<TableData> _tables = new();
    private StringRegistryEntry[] _stringRegistry = Array.Empty<StringRegistryEntry>();

    /// <summary>
    /// Sets the string registry entries to be written to the binary file.
    /// </summary>
    public void SetStringRegistry(IEnumerable<StringRegistryEntry> entries)
    {
        _stringRegistry = entries.ToArray();
    }

    /// <summary>
    /// Adds a table from JSON data.
    /// </summary>
    public unsafe void AddTable<T>(string tableName, T[] records, Func<T, int> getPrimaryKey) where T : unmanaged
    {
        var tableData = new TableData
        {
            Name = tableName,
            RecordSize = sizeof(T),
            Records = new byte[records.Length * sizeof(T)],
            RecordCount = records.Length
        };

        // Copy records to byte array
        unsafe
        {
            fixed (byte* dest = tableData.Records)
            fixed (T* src = records)
            {
                Buffer.MemoryCopy(src, dest, tableData.Records.Length, tableData.Records.Length);
            }
        }

        // Build slot array for O(1) lookup
        if (records.Length > 0)
        {
            int maxId = 0;
            foreach (var record in records)
            {
                int id = getPrimaryKey(record);
                if (id > maxId) maxId = id;
            }

            tableData.SlotArray = new int[maxId + 1];
            Array.Fill(tableData.SlotArray, -1);

            for (int i = 0; i < records.Length; i++)
            {
                int id = getPrimaryKey(records[i]);
                tableData.SlotArray[id] = i;
            }
        }
        else
        {
            tableData.SlotArray = Array.Empty<int>();
        }

        _tables.Add(tableData);
    }

    /// <summary>
    /// Writes the binary file with file locking to prevent race conditions.
    /// </summary>
    public void WriteTo(string outputPath)
    {
        // Use exclusive lock to prevent multiple processes writing simultaneously
        // Needs ReadWrite for checksum calculation
        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var writer = new BinaryWriter(stream);

        // Reserve space for header
        var header = new BinaryHeader
        {
            Magic = BinaryFormat.Magic,
            Version = BinaryFormat.Version,
            TableCount = (uint)_tables.Count,
            Checksum = 0 // Will be filled in later
        };

        long headerPos = stream.Position;
        WriteHeader(writer, header);

        // Write table directory (reserve space, fill in later)
        var directoryPos = stream.Position;
        var directoryEntries = new TableDirectoryEntry[_tables.Count];
        for (int i = 0; i < _tables.Count; i++)
        {
            WriteDirectoryEntry(writer, default);
        }

        // Write string table
        var stringTableStart = (int)stream.Position;
        var nameOffsets = new uint[_tables.Count];
        for (int i = 0; i < _tables.Count; i++)
        {
            nameOffsets[i] = (uint)(stream.Position - stringTableStart);
            var nameBytes = Encoding.UTF8.GetBytes(_tables[i].Name);
            writer.Write(nameBytes);
            writer.Write((byte)0); // null terminator
        }

        // Align to 16 bytes
        while (stream.Position % BinaryFormat.DataAlignment != 0)
            writer.Write((byte)0);

        // Write table data and fill in directory entries
        for (int i = 0; i < _tables.Count; i++)
        {
            var table = _tables[i];

            // Records
            var recordOffset = (uint)stream.Position;
            writer.Write(table.Records);

            // Align
            while (stream.Position % BinaryFormat.DataAlignment != 0)
                writer.Write((byte)0);

            // Slot array
            var slotArrayOffset = (uint)stream.Position;
            foreach (var slot in table.SlotArray)
            {
                writer.Write(slot);
            }

            // Align
            while (stream.Position % BinaryFormat.DataAlignment != 0)
                writer.Write((byte)0);

            directoryEntries[i] = new TableDirectoryEntry
            {
                NameOffset = nameOffsets[i],
                RecordOffset = recordOffset,
                RecordCount = (uint)table.RecordCount,
                RecordSize = (uint)table.RecordSize,
                SlotArrayOffset = slotArrayOffset,
                SlotArrayLength = (uint)table.SlotArray.Length
            };
        }

        // Go back and write directory entries
        stream.Position = directoryPos;
        for (int i = 0; i < _tables.Count; i++)
        {
            WriteDirectoryEntry(writer, directoryEntries[i]);
        }

        // Write string registry section
        stream.Position = stream.Length; // Go to end

        // Align to 16 bytes before string registry
        while (stream.Position % BinaryFormat.DataAlignment != 0)
            writer.Write((byte)0);

        if (_stringRegistry.Length > 0)
        {
            header.StringRegistryOffset = (uint)stream.Position;
            header.StringRegistryCount = (uint)_stringRegistry.Length;

            foreach (var entry in _stringRegistry)
            {
                writer.Write(entry.Id);
                var strBytes = Encoding.UTF8.GetBytes(entry.Value);
                writer.Write((ushort)strBytes.Length);
                writer.Write(strBytes);
            }
        }
        else
        {
            header.StringRegistryOffset = 0;
            header.StringRegistryCount = 0;
        }

        // Calculate and write checksum
        const int headerSize = 24; // 6 uint fields
        stream.Position = headerSize;
        var dataBytes = new byte[stream.Length - headerSize];
        stream.Read(dataBytes);
        header.Checksum = BinaryFormatExtensions.ComputeCrc32(dataBytes);

        stream.Position = headerPos;
        WriteHeader(writer, header);
    }

    private static void WriteHeader(BinaryWriter writer, BinaryHeader header)
    {
        writer.Write(header.Magic);
        writer.Write(header.Version);
        writer.Write(header.TableCount);
        writer.Write(header.Checksum);
        writer.Write(header.StringRegistryOffset);
        writer.Write(header.StringRegistryCount);
    }

    private static void WriteDirectoryEntry(BinaryWriter writer, TableDirectoryEntry entry)
    {
        writer.Write(entry.NameOffset);
        writer.Write(entry.RecordOffset);
        writer.Write(entry.RecordCount);
        writer.Write(entry.RecordSize);
        writer.Write(entry.SlotArrayOffset);
        writer.Write(entry.SlotArrayLength);
    }

    private class TableData
    {
        public string Name { get; init; } = "";
        public int RecordSize { get; init; }
        public byte[] Records { get; init; } = Array.Empty<byte>();
        public int RecordCount { get; init; }
        public int[] SlotArray { get; set; } = Array.Empty<int>();
    }
}
