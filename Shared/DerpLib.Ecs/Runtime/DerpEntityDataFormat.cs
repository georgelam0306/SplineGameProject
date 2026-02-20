using System;
using System.Runtime.InteropServices;

namespace DerpLib.Ecs;

/// <summary>
/// Binary format specification for .derpentitydata asset files.
///
/// File Layout:
/// [Header]         - 32 bytes: magic, version, checksum, flags, var-heap/string-table metadata
/// [EntityData]     - Variable: raw field bytes per archetype, per entity, per component, per field
/// [VarHeap]        - Variable (16-byte aligned): raw var-heap blob for ListHandle data
/// [StringTable]    - Variable (16-byte aligned): uint32 id + uint16 byteLength + UTF-8 bytes per entry
/// </summary>
public static class DerpEntityDataFormat
{
    /// <summary>Magic bytes identifying a .derpentitydata file: "DEED"</summary>
    public const uint Magic = 0x44454544; // "DEED" in little-endian

    /// <summary>Current format version.</summary>
    public const uint Version = 1;

    /// <summary>Alignment for data sections.</summary>
    public const int DataAlignment = 16;

    /// <summary>Size of the file header in bytes.</summary>
    public const int HeaderSize = 32;

    /// <summary>Computes CRC32 checksum.</summary>
    public static uint ComputeCrc32(ReadOnlySpan<byte> data)
    {
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

    /// <summary>Aligns an offset up to the specified boundary.</summary>
    public static int AlignTo(int offset, int alignment)
    {
        int remainder = offset % alignment;
        return remainder == 0 ? offset : offset + (alignment - remainder);
    }
}

/// <summary>
/// File header (32 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct DerpEntityDataHeader
{
    /// <summary>Magic bytes: 0x44454544 ("DEED")</summary>
    public uint Magic;

    /// <summary>Format version number.</summary>
    public uint Version;

    /// <summary>CRC32 checksum of all data after the header.</summary>
    public uint Checksum;

    /// <summary>Reserved flags.</summary>
    public uint Flags;

    /// <summary>Byte offset from file start to var-heap section (0 if empty).</summary>
    public uint VarHeapOffset;

    /// <summary>Byte count of the var-heap blob.</summary>
    public uint VarHeapSize;

    /// <summary>Byte offset from file start to string table section (0 if none).</summary>
    public uint StringTableOffset;

    /// <summary>Number of entries in the string table.</summary>
    public uint StringTableCount;
}
