using System.Runtime.CompilerServices;
using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, PoolId = 240)]
public partial struct ConstraintListComponent
{
    public const int MaxConstraints = 16;

    public enum ConstraintKind : byte
    {
        MatchTargetSize = 0,
        MatchTargetPosition = 1,
        Scroll = 2
    }

    [InlineArray(MaxConstraints)]
    public struct ByteBuffer
    {
        private byte _element0;
    }

    [InlineArray(MaxConstraints)]
    public struct UIntBuffer
    {
        private uint _element0;
    }

    [Column]
    public ushort Count;

    [Column]
    public ByteBuffer EnabledValue;

    [Column]
    public ByteBuffer KindValue;

    // Constraint-kind specific options (bitmask). For Scroll:
    // - bit 0: resize handle based on normalized viewport/content ratio.
    [Column]
    public ByteBuffer FlagsValue;

    // StableId for the target node in the source prefab space. For regular nodes this is just World.GetStableId(entity).
    // For expanded prefab instances, this should be the PrefabExpandedComponent.SourceNodeStableId.
    // For Scroll, this is the scrollable object whose position is used to drive the handle.
    [Column]
    public UIntBuffer TargetSourceStableId;
}
