using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 229)]
public partial struct AnimationTimelineSelectionComponent
{
    [Column(Storage = ColumnStorage.Sidecar)]
    public int TimelineId;

    [Column(Storage = ColumnStorage.Sidecar)]
    public int TimelineIndex;

    [Column(Storage = ColumnStorage.Sidecar)]
    [Property(Name = "Duration (Frames)", Group = "Timeline", Order = 0, Min = 1f, Step = 1f)]
    public int DurationFrames;

    [Column(Storage = ColumnStorage.Sidecar)]
    [Property(Name = "Work Start", Group = "Timeline", Order = 1, Min = 0f, Step = 1f)]
    public int WorkStartFrame;

    [Column(Storage = ColumnStorage.Sidecar)]
    [Property(Name = "Work End", Group = "Timeline", Order = 2, Min = 0f, Step = 1f)]
    public int WorkEndFrame;

    [Column(Storage = ColumnStorage.Sidecar)]
    [Property(Name = "Snap FPS", Group = "Timeline", Order = 3, Min = 1f, Max = 240f, Step = 1f)]
    public int SnapFps;

    [Column(Storage = ColumnStorage.Sidecar)]
    [Property(Name = "Playback Speed", Group = "Timeline", Order = 4, Min = 0.01f, Step = 0.05f)]
    public float PlaybackSpeed;

    [Column(Storage = ColumnStorage.Sidecar)]
    [Property(Name = "Mode", Group = "Timeline", Order = 5, Kind = PropertyKind.Int, Min = 0f, Max = 2f, Step = 1f)]
    public int PlaybackMode;
}

