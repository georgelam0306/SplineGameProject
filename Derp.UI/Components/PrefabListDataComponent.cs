using System.Runtime.CompilerServices;
using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 221)]
public partial struct PrefabListDataComponent
{
    public const int MaxListEntries = 8;
    public const int MaxItemsPerList = 32;
    public const int MaxFieldsPerItem = 16;
    public const int MaxTotalSlots = 512;

    [InlineArray(MaxListEntries)]
    public struct UShortBuffer8
    {
        private ushort _element0;
    }

    [InlineArray(MaxTotalSlots)]
    public struct ValueBuffer
    {
        private PropertyValue _element0;
    }

    // Per-entry metadata (up to 8 list variables per entity)
    [Column]
    public ushort EntryCount;

    [Column]
    [Array(MaxListEntries)]
    public UShortBuffer8 EntryVariableId;

    [Column]
    [Array(MaxListEntries)]
    public UShortBuffer8 EntryItemCount;

    // Offset into Items (in PropertyValue slots).
    [Column]
    [Array(MaxListEntries)]
    public UShortBuffer8 EntryItemStart;

    // Fields per item for this entry (frozen when type is assigned).
    [Column]
    [Array(MaxListEntries)]
    public UShortBuffer8 EntryFieldCount;

    // Flat buffer: Items[start + itemIndex * fieldCount + fieldIndex]
    [Column]
    [Array(MaxTotalSlots)]
    public ValueBuffer Items;
}
