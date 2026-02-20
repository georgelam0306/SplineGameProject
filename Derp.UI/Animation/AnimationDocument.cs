using System;
using System.Collections.Generic;
using Property;

namespace Derp.UI;

internal sealed class AnimationDocument
{
    internal enum PlaybackMode : byte
    {
        OneShot = 0,
        Loop = 1,
        PingPong = 2
    }

    internal enum Interpolation : byte
    {
        Step = 0,
        Linear = 1,
        Cubic = 2
    }

    internal enum LibraryEntryKind : byte
    {
        Folder = 0,
        Timeline = 1
    }

    internal readonly struct LibraryEntry
    {
        public readonly LibraryEntryKind Kind;
        public readonly int Id;

        public LibraryEntry(LibraryEntryKind kind, int id)
        {
            Kind = kind;
            Id = id;
        }
    }

    internal sealed class AnimationFolder
    {
        public int Id;
        public string Name = string.Empty;
        public readonly List<LibraryEntry> Children = new(capacity: 16);
    }

    internal sealed class AnimationTimeline
    {
        public int Id;
        public string Name = string.Empty;

        public int DurationFrames = 60;
        public int WorkStartFrame = 0;
        public int WorkEndFrame = 60;

        public int SnapFps = 60;
        public float PlaybackSpeed = 1f;
        public PlaybackMode Mode = PlaybackMode.Loop;

        public readonly List<AnimationTarget> Targets = new(capacity: 16);
        public readonly List<AnimationTrack> Tracks = new(capacity: 64);
    }

    internal readonly struct AnimationTarget
    {
        public readonly uint StableId;

        public AnimationTarget(uint stableId)
        {
            StableId = stableId;
        }
    }

    internal readonly struct AnimationBinding : IEquatable<AnimationBinding>
    {
        public readonly int TargetIndex;
        public readonly ushort ComponentKind;
        public readonly ushort PropertyIndexHint;
        public readonly ulong PropertyId;
        public readonly PropertyKind PropertyKind;

        public AnimationBinding(int targetIndex, ushort componentKind, ushort propertyIndexHint, ulong propertyId, PropertyKind propertyKind)
        {
            TargetIndex = targetIndex;
            ComponentKind = componentKind;
            PropertyIndexHint = propertyIndexHint;
            PropertyId = propertyId;
            PropertyKind = propertyKind;
        }

        public bool Equals(AnimationBinding other)
        {
            return TargetIndex == other.TargetIndex &&
                ComponentKind == other.ComponentKind &&
                PropertyId == other.PropertyId &&
                PropertyKind == other.PropertyKind;
        }

        public override bool Equals(object? obj)
        {
            return obj is AnimationBinding other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = TargetIndex;
                hash = (hash * 397) ^ ComponentKind.GetHashCode();
                hash = (hash * 397) ^ PropertyId.GetHashCode();
                hash = (hash * 397) ^ (int)PropertyKind;
                return hash;
            }
        }

        public static bool operator ==(AnimationBinding left, AnimationBinding right) => left.Equals(right);
        public static bool operator !=(AnimationBinding left, AnimationBinding right) => !left.Equals(right);
    }

    internal sealed class AnimationTrack
    {
        public AnimationBinding Binding;
        public readonly List<AnimationKeyframe> Keys = new(capacity: 16);
    }

    internal struct AnimationKeyframe
    {
        public int Frame;
        public PropertyValue Value;
        public Interpolation Interpolation;
        public float InTangent;
        public float OutTangent;
    }

    private int _nextFolderId = 1;
    private int _nextTimelineId = 1;

    public readonly List<AnimationFolder> Folders = new(capacity: 16);
    public readonly List<AnimationTimeline> Timelines = new(capacity: 16);

    public int RootFolderId { get; private set; }
    public int SelectedTimelineId { get; set; }

    public AnimationDocument()
    {
        RootFolderId = CreateFolderInternal(name: "Animations");
    }

    internal void ResetFromLibrary(int nextFolderId, int nextTimelineId, int rootFolderId, int selectedTimelineId)
    {
        _nextFolderId = Math.Max(1, nextFolderId);
        _nextTimelineId = Math.Max(1, nextTimelineId);
        RootFolderId = Math.Max(1, rootFolderId);
        SelectedTimelineId = selectedTimelineId;
    }

    internal int GetNextFolderId()
    {
        return _nextFolderId;
    }

    internal int GetNextTimelineId()
    {
        return _nextTimelineId;
    }

    public AnimationFolder CreateFolder(string name, int parentFolderId)
    {
        int id = CreateFolderInternal(name);
        if (TryGetFolderById(parentFolderId, out AnimationFolder? parent) && parent != null)
        {
            parent.Children.Add(new LibraryEntry(LibraryEntryKind.Folder, id));
        }
        return GetFolderById(id);
    }

    public AnimationTimeline CreateTimeline(string name, int durationFrames, int snapFps, int parentFolderId)
    {
        var timeline = new AnimationTimeline
        {
            Id = _nextTimelineId++,
            Name = name,
            DurationFrames = Math.Max(1, durationFrames),
            SnapFps = Math.Clamp(snapFps, 1, 240),
        };
        timeline.WorkStartFrame = 0;
        timeline.WorkEndFrame = timeline.DurationFrames;
        Timelines.Add(timeline);

        if (TryGetFolderById(parentFolderId, out AnimationFolder? parent) && parent != null)
        {
            parent.Children.Add(new LibraryEntry(LibraryEntryKind.Timeline, timeline.Id));
        }

        if (SelectedTimelineId <= 0)
        {
            SelectedTimelineId = timeline.Id;
        }

        return timeline;
    }

    public bool TryGetSelectedTimeline(out AnimationTimeline? timeline)
    {
        return TryGetTimelineById(SelectedTimelineId, out timeline);
    }

    public bool TryGetTimelineById(int timelineId, out AnimationTimeline? timeline)
    {
        if (timelineId <= 0)
        {
            timeline = null;
            return false;
        }

        for (int i = 0; i < Timelines.Count; i++)
        {
            var t = Timelines[i];
            if (t.Id == timelineId)
            {
                timeline = t;
                return true;
            }
        }

        timeline = null;
        return false;
    }

    public AnimationTimeline GetTimelineById(int timelineId)
    {
        if (!TryGetTimelineById(timelineId, out AnimationTimeline? timeline) || timeline == null)
        {
            throw new KeyNotFoundException($"Timeline {timelineId} not found.");
        }
        return timeline;
    }

    public bool TryGetFolderById(int folderId, out AnimationFolder? folder)
    {
        if (folderId <= 0)
        {
            folder = null;
            return false;
        }

        for (int i = 0; i < Folders.Count; i++)
        {
            var f = Folders[i];
            if (f.Id == folderId)
            {
                folder = f;
                return true;
            }
        }

        folder = null;
        return false;
    }

    public AnimationFolder GetFolderById(int folderId)
    {
        if (!TryGetFolderById(folderId, out AnimationFolder? folder) || folder == null)
        {
            throw new KeyNotFoundException($"Folder {folderId} not found.");
        }
        return folder;
    }

    private int CreateFolderInternal(string name)
    {
        var folder = new AnimationFolder
        {
            Id = _nextFolderId++,
            Name = name
        };
        Folders.Add(folder);
        return folder.Id;
    }
}
