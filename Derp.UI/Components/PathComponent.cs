using System.Numerics;
using System.Runtime.CompilerServices;
using Core;
using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 211)]
public partial struct PathComponent
{
    public const int MaxVertices = 64;

    [InlineArray(MaxVertices)]
    public struct Vec2Buffer
    {
        private Vector2 _element0;
    }

    [InlineArray(MaxVertices)]
    public struct IntBuffer
    {
        private int _element0;
    }

    [Column]
    public byte VertexCount;

    [Column]
    public Vector2 PivotLocal;

    // Per-vertex position and Bezier tangents, in local shape space.
    // Tangents are stored as deltas from Position (controlPoint = Position + Tangent).

    [Column]
    [Property(Name = "Position", Group = "Path", Order = 0, Flags = PropertyFlags.Hidden)]
    [Array(MaxVertices)]
    public Vec2Buffer PositionLocal;

    [Column]
    [Property(Name = "Tangent In", Group = "Path", Order = 1, Flags = PropertyFlags.Hidden)]
    [Array(MaxVertices)]
    public Vec2Buffer TangentInLocal;

    [Column]
    [Property(Name = "Tangent Out", Group = "Path", Order = 2, Flags = PropertyFlags.Hidden)]
    [Array(MaxVertices)]
    public Vec2Buffer TangentOutLocal;

    [Column]
    [Property(Name = "Vertex Kind", Group = "Path", Order = 3, Flags = PropertyFlags.Hidden, Min = 0f, Max = 2f, Step = 1f)]
    [Array(MaxVertices)]
    public IntBuffer VertexKind;
}
