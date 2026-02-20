using System;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;

namespace Derp.UI;

internal static class AnimationEditorWindow
{
    private static readonly AnimationEditorState State = new();
    private static int _activePrefabId = -1;
    private static AnimationsLibrarySelectionKind _selectionKind;

    internal static bool IsTimelineMode()
    {
        return _selectionKind == AnimationsLibrarySelectionKind.Timeline;
    }

    internal static bool IsStateMachineMode()
    {
        return _selectionKind == AnimationsLibrarySelectionKind.StateMachine;
    }

    internal static int GetCurrentFrame()
    {
        return State.CurrentFrame;
    }

    internal static bool TryGetSelectedKeyframe(out int timelineId, out AnimationDocument.AnimationBinding binding, out int keyIndex)
    {
        var selected = State.Selected;
        timelineId = selected.TimelineId;
        binding = selected.Binding;
        keyIndex = selected.KeyIndex;

        if (timelineId <= 0)
        {
            return false;
        }

        if (keyIndex < 0)
        {
            return false;
        }

        return true;
    }

    internal static bool IsAutoKeyEnabled()
    {
        return State.AutoKeyEnabled;
    }

    internal static void AutoKeyAtCurrentFrame(
        UiWorkspace workspace,
        AnimationDocument.AnimationTimeline timeline,
        AnimationDocument.AnimationBinding binding,
        bool selectKeyframe = true)
    {
        AnimationEditorHelpers.AddOrUpdateKeyframeAtCurrentFrame(workspace, State, timeline, binding, selectKeyframe);
    }

    internal static void MarkPreviewDirty()
    {
        State.PreviewDirty = true;
    }

    internal static void TickPlayback(float deltaSeconds, UiWorkspace workspace)
    {
        if (deltaSeconds <= 0f || !float.IsFinite(deltaSeconds))
        {
            return;
        }

        if (!State.IsPlaying)
        {
            return;
        }

        if (_selectionKind == AnimationsLibrarySelectionKind.StateMachine)
        {
            State.IsPlaying = false;
            return;
        }

        if (!workspace.TryGetActiveAnimationDocument(out var doc) || !doc.TryGetSelectedTimeline(out var timeline) || timeline == null)
        {
            State.IsPlaying = false;
            return;
        }

        int duration = Math.Max(1, timeline.DurationFrames);
        int rangeStart = Math.Clamp(timeline.WorkStartFrame, 0, duration);
        int rangeEnd = Math.Clamp(timeline.WorkEndFrame, 0, duration);
        if (rangeEnd < rangeStart)
        {
            (rangeStart, rangeEnd) = (rangeEnd, rangeStart);
        }

        if (rangeEnd == rangeStart)
        {
            State.CurrentFrame = rangeStart;
            if (timeline.Mode == AnimationDocument.PlaybackMode.OneShot)
            {
                State.IsPlaying = false;
            }
            return;
        }

        int fps = timeline.SnapFps;
        if (fps <= 0)
        {
            fps = 60;
        }

        float speed = timeline.PlaybackSpeed;
        if (!float.IsFinite(speed) || speed <= 0f)
        {
            speed = 1f;
        }

        float framesToAdvance = deltaSeconds * speed * fps;
        if (!float.IsFinite(framesToAdvance) || framesToAdvance <= 0f)
        {
            return;
        }

        State.PlaybackFrameAccumulator += framesToAdvance;
        int stepCount = (int)State.PlaybackFrameAccumulator;
        if (stepCount <= 0)
        {
            return;
        }

        State.PlaybackFrameAccumulator -= stepCount;

        const int maxStepsPerTick = 2048;
        if (stepCount > maxStepsPerTick)
        {
            stepCount = maxStepsPerTick;
            State.PlaybackFrameAccumulator = 0f;
        }

        int frame = Math.Clamp(State.CurrentFrame, rangeStart, rangeEnd);
        int direction = State.PlaybackDirection >= 0 ? 1 : -1;

        for (int i = 0; i < stepCount; i++)
        {
            if (timeline.Mode == AnimationDocument.PlaybackMode.OneShot)
            {
                frame++;
                if (frame >= rangeEnd)
                {
                    frame = rangeEnd;
                    State.IsPlaying = false;
                    break;
                }
                continue;
            }

            if (timeline.Mode == AnimationDocument.PlaybackMode.Loop)
            {
                frame++;
                if (frame > rangeEnd)
                {
                    frame = rangeStart;
                }
                continue;
            }

            // PingPong
            frame += direction;
            if (frame >= rangeEnd)
            {
                frame = rangeEnd;
                direction = -1;
            }
            else if (frame <= rangeStart)
            {
                frame = rangeStart;
                direction = 1;
            }
        }

        State.PlaybackDirection = direction;
        State.CurrentFrame = frame;
    }

    internal static void ApplyPreviewForCanvasBeforeCanvasDraw(UiWorkspace workspace)
    {
        if (_selectionKind == AnimationsLibrarySelectionKind.StateMachine)
        {
            State.PreviewAppliedTimelineId = 0;
            State.PreviewAppliedFrame = 0;
            return;
        }

        if (!workspace.TryGetActiveAnimationDocument(out var doc) || !doc.TryGetSelectedTimeline(out var timeline) || timeline == null)
        {
            State.PreviewAppliedTimelineId = 0;
            State.PreviewAppliedFrame = 0;
            return;
        }

        int duration = Math.Max(1, timeline.DurationFrames);
        int frame = Math.Clamp(State.CurrentFrame, 0, duration);
        AnimationEditorHelpers.ApplyTimelineAtFrame(workspace, timeline, frame);
        State.PreviewAppliedTimelineId = timeline.Id;
        State.PreviewAppliedFrame = frame;
    }

    public static void DrawAnimationEditorWindow(UiWorkspace workspace)
    {
        State.PreviewDirty = false;

        var bodyRect = ImLayout.AllocateRect(ImLayout.RemainingWidth(), ImLayout.RemainingHeight());
        if (bodyRect.Width <= 0f || bodyRect.Height <= 0f)
        {
            return;
        }

        // The animation editor uses custom-drawn widgets, so we must explicitly capture input
        // to prevent global editor shortcuts (delete/copy/paste/etc.) from firing.
        if (bodyRect.Contains(Im.MousePos) || State.IsDraggingPlayhead || State.KeyDrag.Active || State.KeyMarquee.Active)
        {
            Im.Context.WantCaptureKeyboard = true;
            Im.Context.WantCaptureMouse = true;
        }

        AnimationsLibraryDragDrop.Update(Im.Context.Input, Im.Context.FrameCount);

        AnimationDocument? doc = null;
        StateMachineDefinitionComponent.ViewProxy stateMachineDefinition = default;
        bool hasStateMachineDefinition = false;
        if (workspace.TryGetActivePrefabId(out int prefabId))
        {
            if (_activePrefabId != prefabId)
            {
                _activePrefabId = prefabId;
                State.Selected = default;
                State.SelectedTrack = default;
                State.KeyDrag = default;
                State.TimelineMenuOpen = false;
                _selectionKind = AnimationsLibrarySelectionKind.Timeline;
                workspace.ClearStateMachineInspectorSelection();
            }

            if (workspace.TryGetActiveAnimationDocument(out var activeDoc))
            {
                doc = activeDoc;
            }

            hasStateMachineDefinition = workspace.TryGetActiveStateMachineDefinition(out stateMachineDefinition);
        }

        AnimationDocument.AnimationTimeline? timeline = null;
        if (_selectionKind == AnimationsLibrarySelectionKind.Timeline)
        {
            if (doc != null)
            {
                doc.TryGetSelectedTimeline(out timeline);
            }
            if (timeline != null)
            {
                AnimationTrackRowBuilder.Build(workspace, State, timeline);
            }
        }

        int previewFrameBefore = State.CurrentFrame;
        int previewTimelineIdBefore = State.PreviewAppliedTimelineId;
        int previewAppliedFrameBefore = State.PreviewAppliedFrame;

        float spacing = Im.Style.Spacing;

        float selectorWidth = MathF.Min(AnimationEditorLayout.TimelineSelectorWidth, MathF.Max(160f, bodyRect.Width * 0.18f));
        var timelineSelectorRect = new ImRect(bodyRect.X, bodyRect.Y, selectorWidth, bodyRect.Height);

        float rightX = timelineSelectorRect.Right + spacing;
        var rightRect = new ImRect(rightX, bodyRect.Y, MathF.Max(0f, bodyRect.Right - rightX), bodyRect.Height);

        AnimationsLibraryWidget.Draw(workspace, doc, stateMachineDefinition, hasStateMachineDefinition, State, ref _selectionKind, timelineSelectorRect);

        AnimationEditorPanel.DrawPanelBackground(rightRect);

        const float panelPadding = 2f;
        var contentRect = new ImRect(
            rightRect.X + panelPadding,
            rightRect.Y + panelPadding,
            MathF.Max(0f, rightRect.Width - panelPadding * 2f),
            MathF.Max(0f, rightRect.Height - panelPadding * 2f));

        if (_selectionKind == AnimationsLibrarySelectionKind.StateMachine)
        {
            if (hasStateMachineDefinition)
            {
                StateMachineEditorWindow.Draw(workspace, stateMachineDefinition, contentRect);
            }
        }
        else
        {
            workspace.ClearStateMachineInspectorSelection();

            float propertiesWidth = MathF.Min(AnimationEditorLayout.PropertiesPanelWidth, MathF.Max(220f, contentRect.Width * 0.24f));
            float interpolationWidth = MathF.Min(AnimationEditorLayout.InterpolationPanelWidth, MathF.Max(200f, contentRect.Width * 0.22f));
            float centerWidth = MathF.Max(0f, contentRect.Width - propertiesWidth - interpolationWidth - spacing * 2f);

            var propertiesRect = new ImRect(contentRect.X, contentRect.Y, propertiesWidth, contentRect.Height);
            var centerRect = new ImRect(propertiesRect.Right + spacing, contentRect.Y, centerWidth, contentRect.Height);
            var interpolationRect = new ImRect(centerRect.Right + spacing, contentRect.Y, interpolationWidth, contentRect.Height);

            AnimationTrackListWidget.Draw(workspace, State, timeline, propertiesRect);

            AnimationEditorLayout.SplitCenterPanel(centerRect, out var rulerRect, out var canvasRect);
            AnimationTimeRulerWidget.Draw(workspace, State, timeline, rulerRect);

            if (timeline != null && State.GraphMode)
            {
                AnimationGraphCanvasWidget.Draw(workspace, State, timeline, canvasRect);
            }
            else
            {
                AnimationTimelineCanvasWidget.Draw(workspace, State, timeline, canvasRect);
            }

            AnimationInterpolationPanelWidget.Draw(workspace, State, timeline, interpolationRect);

            if (timeline != null)
            {
                AnimationEditorShortcuts.Handle(workspace, State, timeline);
            }
        }

        if (_selectionKind == AnimationsLibrarySelectionKind.Timeline && timeline != null)
        {
            int duration = Math.Max(1, timeline.DurationFrames);
            int clampedFrame = Math.Clamp(State.CurrentFrame, 0, duration);
            if (State.CurrentFrame != clampedFrame)
            {
                State.CurrentFrame = clampedFrame;
            }

            bool previewMismatch =
                State.PreviewAppliedTimelineId != timeline.Id ||
                State.PreviewAppliedFrame != clampedFrame ||
                previewTimelineIdBefore != timeline.Id ||
                previewAppliedFrameBefore != clampedFrame;

            if (previewMismatch || State.PreviewDirty || previewFrameBefore != clampedFrame)
            {
                AnimationEditorHelpers.ApplyTimelineAtFrame(workspace, timeline, clampedFrame);
                State.PreviewAppliedTimelineId = timeline.Id;
                State.PreviewAppliedFrame = clampedFrame;
                workspace.RebuildCanvasSurfaceFromLastSize();
            }
        }

        AnimationsLibraryDragDropPreviewWidget.Draw(workspace);

        // Keep the serialized animation library component in sync with the editor document.
        // This lets animation edits persist via the normal component snapshot serializer.
        if (_selectionKind == AnimationsLibrarySelectionKind.Timeline && doc != null)
        {
            workspace.SyncActiveAnimationDocumentToLibrary();
        }
    }
}
