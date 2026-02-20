using System.Runtime.InteropServices;

namespace DerpDoc.Runtime;

/// <summary>
/// Binary format specification for GameDocDatabase files.
///
/// File Layout:
/// [Header]           - 16 bytes: magic, version, table count, checksum
/// [TableDirectory]   - N * 24 bytes: one entry per table
/// [StringTable]      - Variable: null-terminated UTF-8 strings
/// [TableData]        - Variable: records + indexes per table
/// </summary>
public static class BinaryFormat
{
    /// <summary>Magic bytes identifying a GameDocDb file: "GDDB"</summary>
    public const uint Magic = 0x42444447; // "GDDB" in little-endian

    /// <summary>Current format version. v2 adds string registry section.</summary>
    public const uint Version = 2;

    /// <summary>Alignment for data sections (16 bytes for SIMD friendliness).</summary>
    public const int DataAlignment = 16;
}

/// <summary>
/// File header (24 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct BinaryHeader
{
    /// <summary>Magic bytes: 0x42444447 ("GDDB")</summary>
    public uint Magic;

    /// <summary>Format version number.</summary>
    public uint Version;

    /// <summary>Number of tables in this file.</summary>
    public uint TableCount;

    /// <summary>CRC32 checksum of all data after header.</summary>
    public uint Checksum;

    /// <summary>Offset to string registry section (0 if none).</summary>
    public uint StringRegistryOffset;

    /// <summary>Number of entries in string registry.</summary>
    public uint StringRegistryCount;
}

/// <summary>
/// Table directory entry (24 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TableDirectoryEntry
{
    /// <summary>Offset to table name in string table.</summary>
    public uint NameOffset;

    /// <summary>Offset to first record from start of file.</summary>
    public uint RecordOffset;

    /// <summary>Number of records in this table.</summary>
    public uint RecordCount;

    /// <summary>Size of each record in bytes.</summary>
    public uint RecordSize;

    /// <summary>Offset to slot array from start of file (0 if no index).</summary>
    public uint SlotArrayOffset;

    /// <summary>Length of slot array in elements.</summary>
    public uint SlotArrayLength;
}

/// <summary>
/// Extension methods for binary format operations.
/// </summary>
public static class BinaryFormatExtensions
{
    /// <summary>
    /// Aligns an offset to the specified boundary.
    /// </summary>
    public static int AlignTo(this int offset, int alignment)
    {
        int remainder = offset % alignment;
        return remainder == 0 ? offset : offset + (alignment - remainder);
    }

    /// <summary>
    /// Computes CRC32 checksum.
    /// </summary>
    public static uint ComputeCrc32(ReadOnlySpan<byte> data)
    {
        // Using simple CRC32 implementation
        const uint polynomial = 0xEDB88320;
        uint crc = 0xFFFFFFFF;

        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 1) != 0)
                    crc = (crc >> 1) ^ polynomial;
                else
                    crc >>= 1;
            }
        }

        return crc ^ 0xFFFFFFFF;
    }
}
