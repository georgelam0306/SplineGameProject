using System.Runtime.CompilerServices;
using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 220)]
public partial struct PrefabInstanceBindingCacheComponent
{
    public const int MaxResolvedBindings = 64;

    [InlineArray(MaxResolvedBindings)]
    public struct UShortBuffer
    {
        private ushort _element0;
    }

    [InlineArray(MaxResolvedBindings)]
    public struct UIntBuffer
    {
        private uint _element0;
    }

    [InlineArray(MaxResolvedBindings)]
    public struct ULongBuffer
    {
        private ulong _element0;
    }

    [InlineArray(MaxResolvedBindings)]
    public struct ByteBuffer
    {
        private byte _element0;
    }

    [Column]
    public ushort Count;

    [Column]
    [Array(MaxResolvedBindings)]
    public UShortBuffer VariableId;

    // 0 = Variable -> Property (default).
    // 1 = Property -> Variable (bind-to-source).
    // 2 = List -> Layout children (auto-instantiated).
    [Column]
    [Array(MaxResolvedBindings)]
    public ByteBuffer Direction;

    [Column]
    [Array(MaxResolvedBindings)]
    public UIntBuffer TargetEntityStableId;

    [Column]
    [Array(MaxResolvedBindings)]
    public UShortBuffer ComponentKind;

    [Column]
    [Array(MaxResolvedBindings)]
    public UShortBuffer PropertyIndex;

    [Column]
    [Array(MaxResolvedBindings)]
    public ULongBuffer PropertyId;

    [Column]
    [Array(MaxResolvedBindings)]
    public UShortBuffer PropertyKind;
}
