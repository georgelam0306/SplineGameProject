namespace DerpLib.Rendering;

/// <summary>
/// Handle to a registered mesh in the MeshRegistry.
/// Contains the slot index and metadata needed for drawing.
/// </summary>
public readonly struct MeshHandle
{
    /// <summary>Slot index in the MeshRegistry.</summary>
    public int Id { get; }

    /// <summary>Number of vertices in this mesh.</summary>
    public int VertexCount { get; }

    /// <summary>Number of indices in this mesh.</summary>
    public int IndexCount { get; }

    /// <summary>Byte offset into the global vertex buffer.</summary>
    public uint VertexOffset { get; }

    /// <summary>Index offset into the global index buffer.</summary>
    public uint FirstIndex { get; }

    /// <summary>True if this mesh uses the instanced path (per-mesh region).</summary>
    public bool IsInstanced { get; }

    /// <summary>Base offset in the instanced buffer (only valid if IsInstanced).</summary>
    public int InstanceRegionBase { get; }

    /// <summary>Max instances for this mesh (only valid if IsInstanced).</summary>
    public int InstanceCapacity { get; }

    public MeshHandle(int id, int vertexCount, int indexCount, uint vertexOffset, uint firstIndex)
    {
        Id = id;
        VertexCount = vertexCount;
        IndexCount = indexCount;
        VertexOffset = vertexOffset;
        FirstIndex = firstIndex;
        IsInstanced = false;
        InstanceRegionBase = 0;
        InstanceCapacity = 0;
    }

    public MeshHandle(int id, int vertexCount, int indexCount, uint vertexOffset, uint firstIndex,
        int instanceRegionBase, int instanceCapacity)
    {
        Id = id;
        VertexCount = vertexCount;
        IndexCount = indexCount;
        VertexOffset = vertexOffset;
        FirstIndex = firstIndex;
        IsInstanced = true;
        InstanceRegionBase = instanceRegionBase;
        InstanceCapacity = instanceCapacity;
    }

    /// <summary>Invalid/null mesh handle.</summary>
    public static MeshHandle Invalid => new(-1, 0, 0, 0, 0);

    public bool IsValid => Id >= 0;
}
