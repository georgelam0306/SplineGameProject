using System.Runtime.CompilerServices;
using Pooled;
using Property;
using Property.Runtime;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 243)]
public partial struct AnimationBasePoseRuntimeComponent
{
    public const int MaxSlots = 512;

    [InlineArray(MaxSlots)]
    public struct SlotBuffer
    {
        private PropertySlot _element0;
    }

    [InlineArray(MaxSlots)]
    public struct ValueBuffer
    {
        private PropertyValue _element0;
    }

    [Column]
    public uint SourcePrefabStableId;

    [Column]
    public uint AnimationLibraryRevision;

    [Column]
    public ushort SlotCount;

    [Column]
    [Array(MaxSlots)]
    public SlotBuffer Slot;

    [Column]
    [Array(MaxSlots)]
    public ValueBuffer BaseValue;
}
