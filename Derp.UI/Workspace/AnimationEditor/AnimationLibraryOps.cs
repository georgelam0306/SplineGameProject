using System;
using Core;
using Property;

namespace Derp.UI;

internal static class AnimationLibraryOps
{
    public static void ImportToDocument(AnimationLibraryComponent.ViewProxy lib, AnimationDocument doc)
    {
        if (!lib.IsAlive)
        {
            return;
        }

        doc.ResetFromLibrary(
            nextFolderId: Math.Max(1, lib.NextFolderId),
            nextTimelineId: Math.Max(1, lib.NextTimelineId),
            rootFolderId: Math.Max(1, lib.RootFolderId),
            selectedTimelineId: lib.SelectedTimelineId);

        doc.Folders.Clear();
        doc.Timelines.Clear();

        ushort folderCount = lib.FolderCount;
        if (folderCount > AnimationLibraryComponent.MaxFolders)
        {
            folderCount = AnimationLibraryComponent.MaxFolders;
        }

        ReadOnlySpan<int> folderId = lib.FolderIdReadOnlySpan();
        ReadOnlySpan<StringHandle> folderName = lib.FolderNameReadOnlySpan();
        ReadOnlySpan<ushort> folderChildStart = lib.FolderChildStartReadOnlySpan();
        ReadOnlySpan<byte> folderChildCount = lib.FolderChildCountReadOnlySpan();

        ReadOnlySpan<byte> entryKindValue = lib.LibraryEntryKindValueReadOnlySpan();
        ReadOnlySpan<int> entryId = lib.LibraryEntryIdReadOnlySpan();

        ushort entryCount = lib.LibraryEntryCount;
        if (entryCount > AnimationLibraryComponent.MaxLibraryEntries)
        {
            entryCount = AnimationLibraryComponent.MaxLibraryEntries;
        }

        for (int i = 0; i < folderCount; i++)
        {
            var folder = new AnimationDocument.AnimationFolder
            {
                Id = folderId[i],
                Name = folderName[i]
            };

            ushort start = folderChildStart[i];
            byte count = folderChildCount[i];
            uint end = (uint)start + count;
            if (start < entryCount && end <= entryCount)
            {
                for (int childIndex = 0; childIndex < count; childIndex++)
                {
                    int entryIndex = start + childIndex;
                    var kind = (AnimationLibraryComponent.LibraryEntryKind)entryKindValue[entryIndex];
                    int childId = entryId[entryIndex];
                    var docKind = kind == AnimationLibraryComponent.LibraryEntryKind.Folder
                        ? AnimationDocument.LibraryEntryKind.Folder
                        : AnimationDocument.LibraryEntryKind.Timeline;
                    folder.Children.Add(new AnimationDocument.LibraryEntry(docKind, childId));
                }
            }

            doc.Folders.Add(folder);
        }

        ushort timelineCount = lib.TimelineCount;
        if (timelineCount > AnimationLibraryComponent.MaxTimelines)
        {
            timelineCount = AnimationLibraryComponent.MaxTimelines;
        }

        ReadOnlySpan<int> timelineId = lib.TimelineIdReadOnlySpan();
        ReadOnlySpan<StringHandle> timelineName = lib.TimelineNameReadOnlySpan();
        ReadOnlySpan<int> durationFrames = lib.TimelineDurationFramesReadOnlySpan();
        ReadOnlySpan<int> workStartFrame = lib.TimelineWorkStartFrameReadOnlySpan();
        ReadOnlySpan<int> workEndFrame = lib.TimelineWorkEndFrameReadOnlySpan();
        ReadOnlySpan<int> snapFps = lib.TimelineSnapFpsReadOnlySpan();
        ReadOnlySpan<float> playbackSpeed = lib.TimelinePlaybackSpeedReadOnlySpan();
        ReadOnlySpan<byte> playbackModeValue = lib.TimelinePlaybackModeValueReadOnlySpan();

        ReadOnlySpan<ushort> timelineTargetStart = lib.TimelineTargetStartReadOnlySpan();
        ReadOnlySpan<byte> timelineTargetCount = lib.TimelineTargetCountReadOnlySpan();
        ReadOnlySpan<ushort> timelineTrackStart = lib.TimelineTrackStartReadOnlySpan();
        ReadOnlySpan<byte> timelineTrackCount = lib.TimelineTrackCountReadOnlySpan();

        ushort targetCountTotal = lib.TargetCount;
        if (targetCountTotal > AnimationLibraryComponent.MaxTargets)
        {
            targetCountTotal = AnimationLibraryComponent.MaxTargets;
        }
        ReadOnlySpan<uint> targetStableId = lib.TargetStableIdReadOnlySpan();

        ushort trackCountTotal = lib.TrackCount;
        if (trackCountTotal > AnimationLibraryComponent.MaxTracks)
        {
            trackCountTotal = AnimationLibraryComponent.MaxTracks;
        }
        ReadOnlySpan<int> trackTimelineId = lib.TrackTimelineIdReadOnlySpan();
        ReadOnlySpan<ushort> trackTargetIndex = lib.TrackTargetIndexReadOnlySpan();
        ReadOnlySpan<ushort> trackComponentKind = lib.TrackComponentKindReadOnlySpan();
        ReadOnlySpan<ushort> trackPropertyIndexHint = lib.TrackPropertyIndexHintReadOnlySpan();
        ReadOnlySpan<ulong> trackPropertyId = lib.TrackPropertyIdReadOnlySpan();
        ReadOnlySpan<ushort> trackPropertyKind = lib.TrackPropertyKindReadOnlySpan();
        ReadOnlySpan<ushort> trackKeyStart = lib.TrackKeyStartReadOnlySpan();
        ReadOnlySpan<ushort> trackKeyCount = lib.TrackKeyCountReadOnlySpan();

        ushort keyCountTotal = lib.KeyCount;
        if (keyCountTotal > AnimationLibraryComponent.MaxKeys)
        {
            keyCountTotal = AnimationLibraryComponent.MaxKeys;
        }
        ReadOnlySpan<int> keyFrame = lib.KeyFrameReadOnlySpan();
        ReadOnlySpan<PropertyValue> keyValue = lib.KeyValueReadOnlySpan();
        ReadOnlySpan<byte> keyInterpolationValue = lib.KeyInterpolationValueReadOnlySpan();
        ReadOnlySpan<float> keyInTangent = lib.KeyInTangentReadOnlySpan();
        ReadOnlySpan<float> keyOutTangent = lib.KeyOutTangentReadOnlySpan();

        for (int i = 0; i < timelineCount; i++)
        {
            var timeline = new AnimationDocument.AnimationTimeline
            {
                Id = timelineId[i],
                Name = timelineName[i],
                DurationFrames = Math.Max(1, durationFrames[i]),
                WorkStartFrame = Math.Max(0, workStartFrame[i]),
                WorkEndFrame = Math.Max(0, workEndFrame[i]),
                SnapFps = Math.Clamp(snapFps[i], 1, 240),
                PlaybackSpeed = playbackSpeed[i],
                Mode = (AnimationDocument.PlaybackMode)playbackModeValue[i]
            };

            ushort tTargetStart = timelineTargetStart[i];
            byte tTargetCount = timelineTargetCount[i];
            uint tTargetEnd = (uint)tTargetStart + tTargetCount;
            if (tTargetStart < targetCountTotal && tTargetEnd <= targetCountTotal)
            {
                for (int t = 0; t < tTargetCount; t++)
                {
                    int targetIndex = tTargetStart + t;
                    timeline.Targets.Add(new AnimationDocument.AnimationTarget(targetStableId[targetIndex]));
                }
            }

            ushort tTrackStart = timelineTrackStart[i];
            byte tTrackCount = timelineTrackCount[i];
            uint tTrackEnd = (uint)tTrackStart + tTrackCount;
            if (tTrackStart < trackCountTotal && tTrackEnd <= trackCountTotal)
            {
                for (int t = 0; t < tTrackCount; t++)
                {
                    int trackIndex = tTrackStart + t;
                    if (trackTimelineId[trackIndex] != timeline.Id)
                    {
                        continue;
                    }

                    var binding = new AnimationDocument.AnimationBinding(
                        targetIndex: trackTargetIndex[trackIndex],
                        componentKind: trackComponentKind[trackIndex],
                        propertyIndexHint: trackPropertyIndexHint[trackIndex],
                        propertyId: trackPropertyId[trackIndex],
                        propertyKind: (PropertyKind)trackPropertyKind[trackIndex]);

                    var track = new AnimationDocument.AnimationTrack { Binding = binding };

                    ushort kStart = trackKeyStart[trackIndex];
                    ushort kCount = trackKeyCount[trackIndex];
                    uint kEnd = (uint)kStart + kCount;
                    if (kStart < keyCountTotal && kEnd <= keyCountTotal)
                    {
                        for (int k = 0; k < kCount; k++)
                        {
                            int keyIndex = kStart + k;
                            track.Keys.Add(new AnimationDocument.AnimationKeyframe
                            {
                                Frame = keyFrame[keyIndex],
                                Value = keyValue[keyIndex],
                                Interpolation = (AnimationDocument.Interpolation)keyInterpolationValue[keyIndex],
                                InTangent = keyInTangent[keyIndex],
                                OutTangent = keyOutTangent[keyIndex]
                            });
                        }
                    }

                    timeline.Tracks.Add(track);
                }
            }

            doc.Timelines.Add(timeline);
        }
    }

    public static uint ExportFromDocument(AnimationDocument doc, AnimationLibraryComponent.ViewProxy lib)
    {
        if (!lib.IsAlive)
        {
            return 0;
        }

        Span<int> folderId = lib.FolderIdSpan();
        Span<StringHandle> folderName = lib.FolderNameSpan();
        Span<ushort> folderChildStart = lib.FolderChildStartSpan();
        Span<byte> folderChildCount = lib.FolderChildCountSpan();

        Span<byte> entryKindValue = lib.LibraryEntryKindValueSpan();
        Span<int> entryId = lib.LibraryEntryIdSpan();

        Span<int> timelineId = lib.TimelineIdSpan();
        Span<StringHandle> timelineName = lib.TimelineNameSpan();
        Span<int> durationFrames = lib.TimelineDurationFramesSpan();
        Span<int> workStartFrame = lib.TimelineWorkStartFrameSpan();
        Span<int> workEndFrame = lib.TimelineWorkEndFrameSpan();
        Span<int> snapFps = lib.TimelineSnapFpsSpan();
        Span<float> playbackSpeed = lib.TimelinePlaybackSpeedSpan();
        Span<byte> playbackModeValue = lib.TimelinePlaybackModeValueSpan();
        Span<ushort> timelineTargetStart = lib.TimelineTargetStartSpan();
        Span<byte> timelineTargetCount = lib.TimelineTargetCountSpan();
        Span<ushort> timelineTrackStart = lib.TimelineTrackStartSpan();
        Span<byte> timelineTrackCount = lib.TimelineTrackCountSpan();

        Span<uint> targetStableId = lib.TargetStableIdSpan();

        Span<int> trackTimelineId = lib.TrackTimelineIdSpan();
        Span<ushort> trackTargetIndex = lib.TrackTargetIndexSpan();
        Span<ushort> trackComponentKind = lib.TrackComponentKindSpan();
        Span<ushort> trackPropertyIndexHint = lib.TrackPropertyIndexHintSpan();
        Span<ulong> trackPropertyId = lib.TrackPropertyIdSpan();
        Span<ushort> trackPropertyKind = lib.TrackPropertyKindSpan();
        Span<ushort> trackKeyStart = lib.TrackKeyStartSpan();
        Span<ushort> trackKeyCount = lib.TrackKeyCountSpan();

        Span<int> keyFrame = lib.KeyFrameSpan();
        Span<PropertyValue> keyValue = lib.KeyValueSpan();
        Span<byte> keyInterpolationValue = lib.KeyInterpolationValueSpan();
        Span<float> keyInTangent = lib.KeyInTangentSpan();
        Span<float> keyOutTangent = lib.KeyOutTangentSpan();

        ushort nextEntry = 0;
        ushort nextTarget = 0;
        ushort nextTrack = 0;
        ushort nextKey = 0;

        int rootFolderId = doc.RootFolderId <= 0 ? 1 : doc.RootFolderId;
        lib.RootFolderId = rootFolderId;
        lib.SelectedTimelineId = doc.SelectedTimelineId;
        lib.NextFolderId = doc.GetNextFolderId();
        lib.NextTimelineId = doc.GetNextTimelineId();

        int folderCount = Math.Min(doc.Folders.Count, AnimationLibraryComponent.MaxFolders);
        for (int i = 0; i < folderCount; i++)
        {
            var folder = doc.Folders[i];
            folderId[i] = folder.Id;
            folderName[i] = folder.Name;
            folderChildStart[i] = nextEntry;

            int childCount = Math.Min(folder.Children.Count, byte.MaxValue);
            if ((uint)nextEntry + (uint)childCount > AnimationLibraryComponent.MaxLibraryEntries)
            {
                childCount = AnimationLibraryComponent.MaxLibraryEntries - nextEntry;
            }

            folderChildCount[i] = (byte)Math.Max(0, childCount);
            for (int c = 0; c < childCount; c++)
            {
                int entryIndex = nextEntry + c;
                var entry = folder.Children[c];
                entryKindValue[entryIndex] = (byte)(entry.Kind == AnimationDocument.LibraryEntryKind.Folder
                    ? AnimationLibraryComponent.LibraryEntryKind.Folder
                    : AnimationLibraryComponent.LibraryEntryKind.Timeline);
                entryId[entryIndex] = entry.Id;
            }

            nextEntry = (ushort)(nextEntry + childCount);
        }

        lib.FolderCount = (ushort)folderCount;
        lib.LibraryEntryCount = nextEntry;

        int timelineCount = Math.Min(doc.Timelines.Count, AnimationLibraryComponent.MaxTimelines);
        for (int i = 0; i < timelineCount; i++)
        {
            var t = doc.Timelines[i];
            timelineId[i] = t.Id;
            timelineName[i] = t.Name;
            durationFrames[i] = t.DurationFrames;
            workStartFrame[i] = t.WorkStartFrame;
            workEndFrame[i] = t.WorkEndFrame;
            snapFps[i] = t.SnapFps;
            playbackSpeed[i] = t.PlaybackSpeed;
            playbackModeValue[i] = (byte)t.Mode;

            timelineTargetStart[i] = nextTarget;
            timelineTrackStart[i] = nextTrack;

            int targetCount = Math.Min(t.Targets.Count, byte.MaxValue);
            if ((uint)nextTarget + (uint)targetCount > AnimationLibraryComponent.MaxTargets)
            {
                targetCount = AnimationLibraryComponent.MaxTargets - nextTarget;
            }

            timelineTargetCount[i] = (byte)Math.Max(0, targetCount);
            for (int j = 0; j < targetCount; j++)
            {
                int idx = nextTarget + j;
                targetStableId[idx] = t.Targets[j].StableId;
            }
            nextTarget = (ushort)(nextTarget + targetCount);

            int trackCount = Math.Min(t.Tracks.Count, byte.MaxValue);
            if ((uint)nextTrack + (uint)trackCount > AnimationLibraryComponent.MaxTracks)
            {
                trackCount = AnimationLibraryComponent.MaxTracks - nextTrack;
            }

            timelineTrackCount[i] = (byte)Math.Max(0, trackCount);
            for (int j = 0; j < trackCount; j++)
            {
                int idx = nextTrack + j;
                var tr = t.Tracks[j];
                trackTimelineId[idx] = t.Id;
                trackTargetIndex[idx] = (ushort)tr.Binding.TargetIndex;
                trackComponentKind[idx] = tr.Binding.ComponentKind;
                trackPropertyIndexHint[idx] = tr.Binding.PropertyIndexHint;
                trackPropertyId[idx] = tr.Binding.PropertyId;
                trackPropertyKind[idx] = (ushort)tr.Binding.PropertyKind;

                trackKeyStart[idx] = nextKey;

                int keyCount = Math.Min(tr.Keys.Count, ushort.MaxValue);
                if ((uint)nextKey + (uint)keyCount > AnimationLibraryComponent.MaxKeys)
                {
                    keyCount = AnimationLibraryComponent.MaxKeys - nextKey;
                }

                trackKeyCount[idx] = (ushort)Math.Max(0, keyCount);
                for (int k = 0; k < keyCount; k++)
                {
                    int keyIndex = nextKey + k;
                    var key = tr.Keys[k];
                    keyFrame[keyIndex] = key.Frame;
                    keyValue[keyIndex] = key.Value;
                    keyInterpolationValue[keyIndex] = (byte)key.Interpolation;
                    keyInTangent[keyIndex] = key.InTangent;
                    keyOutTangent[keyIndex] = key.OutTangent;
                }

                nextKey = (ushort)(nextKey + keyCount);
            }
            nextTrack = (ushort)(nextTrack + trackCount);
        }

        lib.TimelineCount = (ushort)timelineCount;
        lib.TargetCount = nextTarget;
        lib.TrackCount = nextTrack;
        lib.KeyCount = nextKey;

        uint revision = lib.Revision;
        if (revision == 0)
        {
            revision = 1;
        }
        lib.Revision = revision + 1;
        return lib.Revision;
    }
}
