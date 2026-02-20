namespace DerpTech.Rollback;

/// <summary>
/// Interface for deterministic simulation state that can be snapshotted and restored.
/// Generated SimWorld classes implement this interface automatically.
/// </summary>
public unsafe interface ISnapshotProvider
{
    /// <summary>Total size in bytes of the data slab (row data).</summary>
    int TotalSlabSize { get; }

    /// <summary>Total size in bytes of metadata (freelist, counts, etc.).</summary>
    int TotalMetaSize { get; }

    /// <summary>Total snapshot size (TotalSlabSize + TotalMetaSize).</summary>
    int TotalSnapshotSize { get; }

    /// <summary>Save all row data to destination pointer.</summary>
    void SaveTo(byte* dest);

    /// <summary>Load all row data from source pointer.</summary>
    void LoadFrom(byte* src);

    /// <summary>Save metadata to destination span.</summary>
    void SaveMetaTo(Span<byte> dest);

    /// <summary>Load metadata from source span.</summary>
    void LoadMetaFrom(ReadOnlySpan<byte> src);

    /// <summary>Compute deterministic hash of current state.</summary>
    ulong ComputeStateHash();
}
