using System.Buffers.Binary;
using Steamworks;

namespace Networking;

/// <summary>
/// Extension methods for deterministic SteamId to Guid conversion.
/// </summary>
public static class SteamIdExtensions
{
    /// <summary>
    /// Converts a SteamId to a deterministic Guid.
    /// The SteamId is embedded in the lower 8 bytes with a fixed "STEAM" marker in the upper bytes.
    /// </summary>
    public static Guid ToGuid(this SteamId steamId)
    {
        Span<byte> bytes = stackalloc byte[16];

        // Upper 8 bytes: Fixed "STEAM\0\0\0" marker (makes it identifiable)
        bytes[0] = 0x53; // 'S'
        bytes[1] = 0x54; // 'T'
        bytes[2] = 0x45; // 'E'
        bytes[3] = 0x41; // 'A'
        bytes[4] = 0x4D; // 'M'
        bytes[5] = 0x00;
        bytes[6] = 0x00;
        bytes[7] = 0x00;

        // Lower 8 bytes: SteamId value
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(8), steamId.Value);

        return new Guid(bytes);
    }

    /// <summary>
    /// Converts a Guid back to a SteamId.
    /// Only valid for Guids created by ToGuid().
    /// </summary>
    public static SteamId ToSteamId(this Guid guid)
    {
        Span<byte> bytes = stackalloc byte[16];
        guid.TryWriteBytes(bytes);

        return new SteamId { Value = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(8)) };
    }

    /// <summary>
    /// Checks if a Guid was created from a SteamId (has the STEAM marker).
    /// </summary>
    public static bool IsSteamGuid(this Guid guid)
    {
        Span<byte> bytes = stackalloc byte[16];
        guid.TryWriteBytes(bytes);

        return bytes[0] == 0x53 && // 'S'
               bytes[1] == 0x54 && // 'T'
               bytes[2] == 0x45 && // 'E'
               bytes[3] == 0x41 && // 'A'
               bytes[4] == 0x4D;   // 'M'
    }
}
