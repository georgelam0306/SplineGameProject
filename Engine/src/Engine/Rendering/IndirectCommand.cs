using System.Runtime.InteropServices;

namespace DerpLib.Rendering;

/// <summary>
/// Matches VkDrawIndexedIndirectCommand exactly (20 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct IndirectCommand
{
    /// <summary>Number of indices to draw (from MeshHandle).</summary>
    public uint IndexCount;

    /// <summary>Number of instances to draw (computed during flush).</summary>
    public uint InstanceCount;

    /// <summary>Offset into global index buffer (from MeshHandle).</summary>
    public uint FirstIndex;

    /// <summary>Value added to each index (from MeshHandle). Signed!</summary>
    public int VertexOffset;

    /// <summary>Offset into instance buffer (computed during flush).</summary>
    public uint FirstInstance;

    public const int SizeInBytes = 20;
}
