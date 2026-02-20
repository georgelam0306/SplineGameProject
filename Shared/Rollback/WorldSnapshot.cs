namespace DerpTech.Rollback;

/// <summary>
/// Handles serialization/deserialization of simulation state snapshots for rollback.
/// Uses ISnapshotProvider interface to work with any game's SimWorld.
/// </summary>
public sealed class WorldSnapshot
{
    private const uint MagicNumber = 0x43415452; // "CATR"
    private const int Version = 2; // Updated version - removed entity count from header
    private const int HeaderSize = 16; // 4 header fields × 4 bytes

    private readonly ISnapshotProvider _snapshotProvider;

    public WorldSnapshot(ISnapshotProvider snapshotProvider)
    {
        _snapshotProvider = snapshotProvider;
    }

    /// <summary>
    /// Serialize the current simulation state to a buffer.
    /// </summary>
    /// <returns>Total bytes written.</returns>
    public unsafe int SerializeWorld(int frameNumber, Span<byte> buffer)
    {
        int offset = HeaderSize;

        // Save slab data (row data)
        int slabSize = _snapshotProvider.TotalSlabSize;
        fixed (byte* destPtr = buffer.Slice(offset))
        {
            _snapshotProvider.SaveTo(destPtr);
        }
        offset += slabSize;

        // Save metadata (freelist, counts, etc.)
        int metaSize = _snapshotProvider.TotalMetaSize;
        _snapshotProvider.SaveMetaTo(buffer.Slice(offset, metaSize));
        offset += metaSize;

        // Write header
        int headerOffset = 0;
        headerOffset = SnapshotSerializer.WriteInt32(buffer, headerOffset, (int)MagicNumber);
        headerOffset = SnapshotSerializer.WriteInt32(buffer, headerOffset, Version);
        headerOffset = SnapshotSerializer.WriteInt32(buffer, headerOffset, frameNumber);
        headerOffset = SnapshotSerializer.WriteInt32(buffer, headerOffset, offset); // totalSize

        return offset;
    }

    /// <summary>
    /// Deserialize simulation state from a buffer.
    /// </summary>
    public unsafe void DeserializeWorld(ReadOnlySpan<byte> buffer)
    {
        int offset = 0;
        offset = SnapshotSerializer.ReadInt32(buffer, offset, out int magic);
        if (magic != (int)MagicNumber)
        {
            throw new InvalidOperationException("Invalid snapshot magic number");
        }

        offset = SnapshotSerializer.ReadInt32(buffer, offset, out int version);
        if (version != Version)
        {
            throw new InvalidOperationException($"Unsupported snapshot version: {version}");
        }

        offset = SnapshotSerializer.ReadInt32(buffer, offset, out int frameNumber);
        offset = SnapshotSerializer.ReadInt32(buffer, offset, out int totalSize);

        // Load slab data
        int slabSize = _snapshotProvider.TotalSlabSize;
        fixed (byte* srcPtr = buffer.Slice(offset))
        {
            _snapshotProvider.LoadFrom(srcPtr);
        }
        offset += slabSize;

        // Load metadata
        int metaSize = _snapshotProvider.TotalMetaSize;
        _snapshotProvider.LoadMetaFrom(buffer.Slice(offset, metaSize));
    }

    /// <summary>
    /// Compute hash of current simulation state for desync detection.
    /// </summary>
    public ulong GetStateHash()
    {
        return _snapshotProvider.ComputeStateHash();
    }
}
