using System.Runtime.CompilerServices;
using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 218)]
public partial struct PrefabBindingsComponent
{
    public const int MaxBindings = 64;

    [InlineArray(MaxBindings)]
    public struct UShortBuffer
    {
        private ushort _element0;
    }

    [InlineArray(MaxBindings)]
    public struct UIntBuffer
    {
        private uint _element0;
    }

    [InlineArray(MaxBindings)]
    public struct ULongBuffer
    {
        private ulong _element0;
    }

    [InlineArray(MaxBindings)]
    public struct ByteBuffer
    {
        private byte _element0;
    }

    [Column]
    public uint Revision;

    [Column]
    public ushort BindingCount;

    // Variable target
    [Column]
    [Array(MaxBindings)]
    public UShortBuffer VariableId;

    // 0 = default (Variable -> Property).
    // 1 = bind to source (Property -> Variable).
    // 2 = list bind (List -> Layout children; auto-instantiated).
    [Column]
    [Array(MaxBindings)]
    public ByteBuffer Direction;

    // StableId of the source prefab node that owns the property.
    [Column]
    [Array(MaxBindings)]
    public UIntBuffer TargetSourceNodeStableId;

    // Property reference
    [Column]
    [Array(MaxBindings)]
    public UShortBuffer TargetComponentKind;

    [Column]
    [Array(MaxBindings)]
    public UShortBuffer PropertyIndexHint;

    [Column]
    [Array(MaxBindings)]
    public ULongBuffer PropertyId;

    [Column]
    [Array(MaxBindings)]
    public UShortBuffer PropertyKind;
}
