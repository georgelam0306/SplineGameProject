using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 230)]
public partial struct AnimationKeyframeSelectionFloatComponent
{
    [Column(Storage = ColumnStorage.Sidecar)]
    public int TimelineId;

    [Column(Storage = ColumnStorage.Sidecar)]
    public int KeyIndex;

    [Column(Storage = ColumnStorage.Sidecar)]
    public int TargetIndex;

    [Column(Storage = ColumnStorage.Sidecar)]
    public ushort ComponentKind;

    [Column(Storage = ColumnStorage.Sidecar)]
    public ushort PropertyIndexHint;

    [Column(Storage = ColumnStorage.Sidecar)]
    public ulong PropertyId;

    [Column(Storage = ColumnStorage.Sidecar)]
    public PropertyKind BoundPropertyKind;

    [Column(Storage = ColumnStorage.Sidecar)]
    [Property(Name = "Frame", Group = "Keyframe", Order = 0, Flags = PropertyFlags.ReadOnly)]
    public int Frame;

    [Column(Storage = ColumnStorage.Sidecar)]
    [Property(Name = "Value", Group = "Keyframe", Order = 1)]
    public float Value;

    [Column(Storage = ColumnStorage.Sidecar)]
    [Property(Name = "Interpolation", Group = "Keyframe", Order = 2, Kind = PropertyKind.Int, Min = 0f, Max = 2f, Step = 1f)]
    public int Interpolation;

    [Column(Storage = ColumnStorage.Sidecar)]
    [Property(Name = "In Tangent", Group = "Keyframe", Order = 3)]
    public float InTangent;

    [Column(Storage = ColumnStorage.Sidecar)]
    [Property(Name = "Out Tangent", Group = "Keyframe", Order = 4)]
    public float OutTangent;
}

