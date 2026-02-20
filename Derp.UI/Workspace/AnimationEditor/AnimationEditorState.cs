using System.Collections.Generic;
using System.Numerics;
using Property;

namespace Derp.UI;

internal sealed class AnimationEditorState
{
    public float Zoom = 1f;
    public int ViewStartFrame;
    public int CurrentFrame;
    public bool GraphMode;

    public bool AutoKeyEnabled;

    public bool IsPlaying;
    public float PlaybackFrameAccumulator;
    public int PlaybackDirection = 1;

    public bool IsDraggingPlayhead;

    public float TrackScrollY;
    public float TrackContentHeight;

    public bool PreviewDirty;
    public int PreviewAppliedTimelineId;
    public int PreviewAppliedFrame;

    public int GraphActiveTimelineId;
    public AnimationDocument.AnimationBinding GraphActiveBinding;
    public byte GraphActiveColorChannel;

    public ulong GraphScopeChannelGroupKey;
    public ulong GraphRangeKey;
    public float GraphMinValue;
    public float GraphMaxValue;

    public ulong GraphViewKey;
    public float GraphZoomY = 1f;
    public float GraphPanY;

    public bool TimelineMenuOpen;
    public Vector2 TimelineMenuPosViewport;
    public int TimelineMenuOpenFrame;
    public bool TimelineMenuNeedsSync;

    public readonly char[] TimelineCurrentBuffer = new char[16];
    public int TimelineCurrentLength;
    public readonly char[] TimelineDurationBuffer = new char[16];
    public int TimelineDurationLength;
    public readonly char[] TimelineSpeedBuffer = new char[16];
    public int TimelineSpeedLength;
    public readonly char[] TimelineSnapFpsBuffer = new char[16];
    public int TimelineSnapFpsLength;

    public SelectedKey Selected;
    public TrackSelection SelectedTrack;
    public KeyframeDrag KeyDrag;
    public KeyMarqueeDrag KeyMarquee;
    public readonly List<SelectedKeyFrame> SelectedKeyFrames = new(capacity: 64);
    public readonly AnimationKeyClipboard KeyClipboard = new();
    public readonly List<Row> Rows = new(capacity: 256);
    public readonly List<ulong> CollapsedChannelGroups = new(capacity: 64);
    public readonly List<ulong> CollapsedTargets = new(capacity: 32);
    public readonly List<ulong> CollapsedComponents = new(capacity: 64);
    public readonly List<RowLayout> RowLayouts = new(capacity: 256);

    public ZoomRangeSliderDrag ZoomDrag;
    public WorkAreaDrag WorkDrag;

    public ViewPanDrag PanDrag;

    public struct SelectedKey
    {
        public int TimelineId;
        public AnimationDocument.AnimationBinding Binding;
        public int KeyIndex;
    }

    public struct KeyframeDrag
    {
        public bool Active;
        public bool Dragging;
        public int TimelineId;
        public AnimationDocument.AnimationBinding Binding;
        public int KeyIndex;
        public float StartMouseX;
        public int StartFrame;
    }

    public struct KeyMarqueeDrag
    {
        public bool Active;
        public bool Dragging;
        public bool Additive;
        public Vector2 StartMouse;
        public Vector2 CurrentMouse;
    }

    public struct SelectedKeyFrame
    {
        public int TimelineId;
        public AnimationDocument.AnimationBinding Binding;
        public int Frame;
    }

    public sealed class AnimationKeyClipboard
    {
        public readonly List<ClipboardKeyframe> Keys = new(capacity: 64);
        public int BaseFrame;
    }

    public struct ClipboardKeyframe
    {
        public AnimationDocument.AnimationBinding Binding;
        public AnimationDocument.AnimationKeyframe Key;
    }

    public struct TrackSelection
    {
        public int TimelineId;
        public AnimationDocument.AnimationBinding Binding;
    }

    public struct RowLayout
    {
        public float OffsetY;
        public float Height;
    }

    public struct ZoomRangeSliderDrag
    {
        public ZoomRangeDragKind Kind;
        public float StartMouseX;
        public int StartViewStartFrame;
        public int StartViewEndFrame;
    }

    public enum ZoomRangeDragKind : byte
    {
        None = 0,
        Pan = 1,
        ResizeLeft = 2,
        ResizeRight = 3
    }

    public struct WorkAreaDrag
    {
        public WorkAreaDragKind Kind;
        public float StartMouseX;
        public int StartWorkStartFrame;
        public int StartWorkEndFrame;
    }

    public enum WorkAreaDragKind : byte
    {
        None = 0,
        Pan = 1,
        ResizeLeft = 2,
        ResizeRight = 3
    }

    public struct ViewPanDrag
    {
        public bool Active;
        public float StartMouseX;
        public int StartViewStartFrame;
    }

    public enum RowKind : byte
    {
        TargetHeader = 0,
        ComponentHeader = 1,
        PropertyGroup = 2,
        Property = 3
    }

    public struct Row
    {
        public RowKind Kind;
        public int Depth;
        public string Label;
        public EntityId Entity;
        public int TargetIndex;
        public ushort ComponentKind;
        public ushort PropertyIndexHint;
        public ulong PropertyId;
        public PropertyKind PropertyKind;
        public ulong ChannelGroupId;
        public ushort ChannelIndex;
        public ushort ChannelCount;
        public bool IsAnimatable;
    }
}
