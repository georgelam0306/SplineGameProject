namespace Derp.UI;

internal static class AnimationEditorShortcuts
{
    public static void Handle(UiWorkspace workspace, AnimationEditorState state, AnimationDocument.AnimationTimeline timeline)
    {
        var input = DerpLib.ImGui.Im.Context.Input;

        if (input.KeyCtrlC)
        {
            CopySelectedKeys(state, timeline);
        }

        if (input.KeyCtrlV)
        {
            PasteKeysAtPlayhead(workspace, state, timeline);
        }

        if (!input.KeyDelete && !input.KeyBackspace)
        {
            return;
        }

        if (DerpLib.ImGui.Im.Context.FocusId != 0 || DerpLib.ImGui.Im.Context.AnyActive || DerpLib.ImGui.Im.Context.WantCaptureKeyboard)
        {
            return;
        }

        DeleteSelectedKeys(state, timeline);
    }

    private static void CopySelectedKeys(AnimationEditorState state, AnimationDocument.AnimationTimeline timeline)
    {
        state.KeyClipboard.Keys.Clear();
        state.KeyClipboard.BaseFrame = 0;

        int timelineId = timeline.Id;
        int minFrame = int.MaxValue;

        if (state.SelectedKeyFrames.Count > 0)
        {
            for (int i = 0; i < state.SelectedKeyFrames.Count; i++)
            {
                var selected = state.SelectedKeyFrames[i];
                if (selected.TimelineId != timelineId)
                {
                    continue;
                }
                if (selected.Frame < minFrame)
                {
                    minFrame = selected.Frame;
                }

                var track = AnimationEditorHelpers.FindTrack(timeline, selected.Binding);
                if (track == null)
                {
                    continue;
                }

                for (int keyIndex = 0; keyIndex < track.Keys.Count; keyIndex++)
                {
                    var key = track.Keys[keyIndex];
                    if (key.Frame == selected.Frame)
                    {
                        state.KeyClipboard.Keys.Add(new AnimationEditorState.ClipboardKeyframe
                        {
                            Binding = track.Binding,
                            Key = key
                        });
                        break;
                    }
                }
            }
        }
        else if (state.Selected.TimelineId == timelineId)
        {
            var track = AnimationEditorHelpers.FindTrack(timeline, state.Selected.Binding);
            if (track != null && state.Selected.KeyIndex >= 0 && state.Selected.KeyIndex < track.Keys.Count)
            {
                var key = track.Keys[state.Selected.KeyIndex];
                minFrame = key.Frame;
                state.KeyClipboard.Keys.Add(new AnimationEditorState.ClipboardKeyframe
                {
                    Binding = track.Binding,
                    Key = key
                });
            }
        }

        if (state.KeyClipboard.Keys.Count == 0)
        {
            return;
        }

        if (minFrame == int.MaxValue)
        {
            minFrame = 0;
        }

        state.KeyClipboard.BaseFrame = minFrame;
    }

    private static void PasteKeysAtPlayhead(UiWorkspace workspace, AnimationEditorState state, AnimationDocument.AnimationTimeline timeline)
    {
        if (state.KeyClipboard.Keys.Count == 0)
        {
            return;
        }

        int timelineId = timeline.Id;
        int duration = System.Math.Max(1, timeline.DurationFrames);
        int playheadFrame = System.Math.Clamp(state.CurrentFrame, 0, duration);
        int deltaFrames = playheadFrame - state.KeyClipboard.BaseFrame;

        state.SelectedKeyFrames.Clear();
        state.Selected = default;
        state.SelectedTrack = default;

        bool selectedSet = false;
        for (int i = 0; i < state.KeyClipboard.Keys.Count; i++)
        {
            var clip = state.KeyClipboard.Keys[i];
            var key = clip.Key;
            int newFrame = System.Math.Clamp(key.Frame + deltaFrames, 0, duration);
            key.Frame = newFrame;

            var track = GetOrCreateTrack(timeline, clip.Binding);
            AddOrUpdateKeyframe(track, key);

            state.SelectedKeyFrames.Add(new AnimationEditorState.SelectedKeyFrame
            {
                TimelineId = timelineId,
                Binding = track.Binding,
                Frame = newFrame
            });

            if (!selectedSet)
            {
                selectedSet = true;
                for (int keyIndex = 0; keyIndex < track.Keys.Count; keyIndex++)
                {
                    if (track.Keys[keyIndex].Frame == newFrame)
                    {
                        state.Selected = new AnimationEditorState.SelectedKey
                        {
                            TimelineId = timelineId,
                            Binding = track.Binding,
                            KeyIndex = keyIndex
                        };
                        state.SelectedTrack = new AnimationEditorState.TrackSelection { TimelineId = timelineId, Binding = track.Binding };
                        workspace.NotifyInspectorAnimationInteraction();
                        break;
                    }
                }
            }
        }

        state.PreviewDirty = true;
    }

    private static void DeleteSelectedKeys(AnimationEditorState state, AnimationDocument.AnimationTimeline timeline)
    {
        int timelineId = timeline.Id;
        if (state.SelectedKeyFrames.Count > 0)
        {
            for (int i = 0; i < state.SelectedKeyFrames.Count; i++)
            {
                var selected = state.SelectedKeyFrames[i];
                if (selected.TimelineId != timelineId)
                {
                    continue;
                }

                var track = AnimationEditorHelpers.FindTrack(timeline, selected.Binding);
                if (track == null)
                {
                    continue;
                }

                for (int keyIndex = 0; keyIndex < track.Keys.Count; keyIndex++)
                {
                    if (track.Keys[keyIndex].Frame == selected.Frame)
                    {
                        track.Keys.RemoveAt(keyIndex);
                        state.PreviewDirty = true;
                        break;
                    }
                }
            }

            state.SelectedKeyFrames.Clear();
            state.Selected = default;
            return;
        }

        if (state.Selected.TimelineId != timelineId)
        {
            return;
        }

        var selectedTrack = AnimationEditorHelpers.FindTrack(timeline, state.Selected.Binding);
        if (selectedTrack == null || state.Selected.KeyIndex < 0 || state.Selected.KeyIndex >= selectedTrack.Keys.Count)
        {
            state.Selected = default;
            return;
        }

        selectedTrack.Keys.RemoveAt(state.Selected.KeyIndex);
        state.Selected = default;
        state.PreviewDirty = true;
    }

    private static AnimationDocument.AnimationTrack GetOrCreateTrack(AnimationDocument.AnimationTimeline timeline, AnimationDocument.AnimationBinding binding)
    {
        var existing = AnimationEditorHelpers.FindTrack(timeline, binding);
        if (existing != null)
        {
            return existing;
        }

        var track = new AnimationDocument.AnimationTrack
        {
            Binding = binding
        };
        timeline.Tracks.Add(track);
        return track;
    }

    private static void AddOrUpdateKeyframe(AnimationDocument.AnimationTrack track, AnimationDocument.AnimationKeyframe key)
    {
        for (int i = 0; i < track.Keys.Count; i++)
        {
            if (track.Keys[i].Frame == key.Frame)
            {
                track.Keys[i] = key;
                return;
            }
        }

        track.Keys.Add(key);
        AnimationEditorHelpers.SortKeys(track.Keys);
    }
}
