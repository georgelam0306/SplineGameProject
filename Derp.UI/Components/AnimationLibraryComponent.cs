using System.Runtime.CompilerServices;
using Core;
using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 228)]
public partial struct AnimationLibraryComponent
{
    // NOTE: These limits are per-prefab. They are intentionally conservative for now.
    public const int MaxFolders = 32;
    public const int MaxLibraryEntries = 256;
    public const int MaxTimelines = 64;
    public const int MaxTargets = 256;
    public const int MaxTracks = 256;
    public const int MaxKeys = 2048;

    public enum PlaybackMode : byte
    {
        OneShot = 0,
        Loop = 1,
        PingPong = 2
    }

    public enum Interpolation : byte
    {
        Step = 0,
        Linear = 1,
        Cubic = 2
    }

    public enum LibraryEntryKind : byte
    {
        Folder = 0,
        Timeline = 1
    }

    [InlineArray(MaxFolders)]
    public struct IntBufferFolders
    {
        private int _element0;
    }

    [InlineArray(MaxFolders)]
    public struct StringHandleBufferFolders
    {
        private StringHandle _element0;
    }

    [InlineArray(MaxFolders)]
    public struct UShortBufferFolders
    {
        private ushort _element0;
    }

    [InlineArray(MaxFolders)]
    public struct ByteBufferFolders
    {
        private byte _element0;
    }

    [InlineArray(MaxLibraryEntries)]
    public struct ByteBufferEntries
    {
        private byte _element0;
    }

    [InlineArray(MaxLibraryEntries)]
    public struct IntBufferEntries
    {
        private int _element0;
    }

    [InlineArray(MaxTimelines)]
    public struct IntBufferTimelines
    {
        private int _element0;
    }

    [InlineArray(MaxTimelines)]
    public struct StringHandleBufferTimelines
    {
        private StringHandle _element0;
    }

    [InlineArray(MaxTimelines)]
    public struct FloatBufferTimelines
    {
        private float _element0;
    }

    [InlineArray(MaxTimelines)]
    public struct UShortBufferTimelines
    {
        private ushort _element0;
    }

    [InlineArray(MaxTimelines)]
    public struct ByteBufferTimelines
    {
        private byte _element0;
    }

    [InlineArray(MaxTargets)]
    public struct UIntBufferTargets
    {
        private uint _element0;
    }

    [InlineArray(MaxTracks)]
    public struct ByteBufferTracks
    {
        private byte _element0;
    }

    [InlineArray(MaxTracks)]
    public struct UShortBufferTracks
    {
        private ushort _element0;
    }

    [InlineArray(MaxTracks)]
    public struct IntBufferTracks
    {
        private int _element0;
    }

    [InlineArray(MaxTracks)]
    public struct ULongBufferTracks
    {
        private ulong _element0;
    }

    [InlineArray(MaxKeys)]
    public struct IntBufferKeys
    {
        private int _element0;
    }

    [InlineArray(MaxKeys)]
    public struct ValueBufferKeys
    {
        private PropertyValue _element0;
    }

    [InlineArray(MaxKeys)]
    public struct FloatBufferKeys
    {
        private float _element0;
    }

    [InlineArray(MaxKeys)]
    public struct ByteBufferKeys
    {
        private byte _element0;
    }

    [Column]
    public uint Revision;

    [Column]
    public int RootFolderId;

    [Column(Storage = ColumnStorage.Sidecar)]
    public int SelectedTimelineId;

    [Column]
    public int NextFolderId;

    [Column]
    public int NextTimelineId;

    [Column]
    public ushort FolderCount;

    [Column]
    public IntBufferFolders FolderId;

    [Column]
    public StringHandleBufferFolders FolderName;

    [Column]
    public UShortBufferFolders FolderChildStart;

    [Column]
    public ByteBufferFolders FolderChildCount;

    [Column]
    public ushort LibraryEntryCount;

    [Column]
    public ByteBufferEntries LibraryEntryKindValue;

    [Column]
    public IntBufferEntries LibraryEntryId;

    [Column]
    public ushort TimelineCount;

    [Column]
    public IntBufferTimelines TimelineId;

    [Column]
    public StringHandleBufferTimelines TimelineName;

    [Column]
    public IntBufferTimelines TimelineDurationFrames;

    [Column]
    public IntBufferTimelines TimelineWorkStartFrame;

    [Column]
    public IntBufferTimelines TimelineWorkEndFrame;

    [Column]
    public IntBufferTimelines TimelineSnapFps;

    [Column]
    public FloatBufferTimelines TimelinePlaybackSpeed;

    [Column]
    public ByteBufferTimelines TimelinePlaybackModeValue;

    [Column]
    public UShortBufferTimelines TimelineTargetStart;

    [Column]
    public ByteBufferTimelines TimelineTargetCount;

    [Column]
    public UShortBufferTimelines TimelineTrackStart;

    [Column]
    public ByteBufferTimelines TimelineTrackCount;

    [Column]
    public ushort TargetCount;

    [Column]
    public UIntBufferTargets TargetStableId;

    [Column]
    public ushort TrackCount;

    [Column]
    public IntBufferTracks TrackTimelineId;

    [Column]
    public UShortBufferTracks TrackTargetIndex;

    [Column]
    public UShortBufferTracks TrackComponentKind;

    [Column]
    public UShortBufferTracks TrackPropertyIndexHint;

    [Column]
    public ULongBufferTracks TrackPropertyId;

    [Column]
    public UShortBufferTracks TrackPropertyKind;

    [Column]
    public UShortBufferTracks TrackKeyStart;

    [Column]
    public UShortBufferTracks TrackKeyCount;

    [Column]
    public ushort KeyCount;

    [Column]
    public IntBufferKeys KeyFrame;

    [Column]
    public ValueBufferKeys KeyValue;

    [Column]
    public ByteBufferKeys KeyInterpolationValue;

    [Column]
    public FloatBufferKeys KeyInTangent;

    [Column]
    public FloatBufferKeys KeyOutTangent;

    internal static AnimationLibraryComponent CreateDefault()
    {
        var lib = new AnimationLibraryComponent
        {
            Revision = 1,
            RootFolderId = 1,
            SelectedTimelineId = 0,
            NextFolderId = 2,
            NextTimelineId = 1,

            FolderCount = 1,
            LibraryEntryCount = 0,
            TimelineCount = 0,
            TargetCount = 0,
            TrackCount = 0,
            KeyCount = 0
        };

        lib.FolderId[0] = 1;
        lib.FolderName[0] = "Animations";
        lib.FolderChildStart[0] = 0;
        lib.FolderChildCount[0] = 0;
        return lib;
    }
}
