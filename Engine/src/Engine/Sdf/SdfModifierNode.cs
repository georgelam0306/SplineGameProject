using System.Numerics;
using System.Runtime.InteropServices;

namespace DerpLib.Sdf;

/// <summary>
/// A single node in the per-command modifier chain.
/// This is uploaded to a dedicated GPU storage buffer and referenced by commands via a head index.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct SdfModifierNode
{
    /// <summary>Previous node index (0 = end of chain).</summary>
    public uint Prev;

    /// <summary>Modifier type (matches <see cref="SdfModifierType"/>).</summary>
    public uint Type;

    /// <summary>Padding for std430 alignment (unused).</summary>
    public Vector2 Pad;

    /// <summary>
    /// Modifier parameters:
    /// Offset: x=offsetX, y=offsetY
    /// Feather: x=radiusPx, y=direction (0=Both, 1=Outside, 2=Inside)
    /// </summary>
    public Vector4 Params;

    public SdfModifierNode(uint prev, SdfModifierType type, Vector4 @params)
    {
        Prev = prev;
        Type = (uint)type;
        Pad = Vector2.Zero;
        Params = @params;
    }
}
