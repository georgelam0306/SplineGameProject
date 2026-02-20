using System.Runtime.CompilerServices;
using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 225)]
public partial struct PrefabInstancePropertyOverridesComponent
{
    public const int MaxOverrides = 256;

    [InlineArray(MaxOverrides)]
    public struct UIntBuffer
    {
        private uint _element0;
    }

    [InlineArray(MaxOverrides)]
    public struct UShortBuffer
    {
        private ushort _element0;
    }

    [InlineArray(MaxOverrides)]
    public struct ULongBuffer
    {
        private ulong _element0;
    }

    [InlineArray(MaxOverrides)]
    public struct ValueBuffer
    {
        private PropertyValue _element0;
    }

    [Column]
    public ushort Count;

    // Entries [0..RootOverrideCount) are reserved for overrides on the prefab root proxy
    // (SourceNodeStableId == PrefabInstanceComponent.SourcePrefabStableId).
    [Column]
    public ushort RootOverrideCount;

    [Column]
    [Array(MaxOverrides)]
    public UIntBuffer SourceNodeStableId;

    [Column]
    [Array(MaxOverrides)]
    public UShortBuffer ComponentKind;

    [Column]
    [Array(MaxOverrides)]
    public UShortBuffer PropertyIndexHint;

    [Column]
    [Array(MaxOverrides)]
    public ULongBuffer PropertyId;

    [Column]
    [Array(MaxOverrides)]
    public UShortBuffer PropertyKind;

    [Column]
    [Array(MaxOverrides)]
    public ValueBuffer Value;
}

