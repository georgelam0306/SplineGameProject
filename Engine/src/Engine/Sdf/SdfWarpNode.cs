using System.Numerics;
using System.Runtime.InteropServices;

namespace DerpLib.Sdf;

/// <summary>
/// A single node in the per-command warp chain.
/// This is uploaded to a dedicated GPU storage buffer and referenced by commands via a head index.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct SdfWarpNode
{
    /// <summary>Previous node index (0 = end of chain).</summary>
    public uint Prev;

    /// <summary>Warp type (matches <see cref="SdfWarpType"/>).</summary>
    public uint Type;

    /// <summary>Padding for std430 alignment (unused).</summary>
    public Vector2 Pad;

    /// <summary>
    /// Warp parameters packed like the legacy command WarpParams layout:
    /// x=reserved, y=param1, z=param2, w=param3.
    /// </summary>
    public Vector4 Params;

    public SdfWarpNode(uint prev, SdfWarp warp)
    {
        Prev = prev;
        Type = (uint)warp.Type;
        Pad = Vector2.Zero;
        Params = new Vector4(0f, warp.Param1, warp.Param2, warp.Param3);
    }
}

