using System;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using Property.Runtime;

namespace Derp.UI;

internal static class AnimationTimelineCanvasWidget
{
    private const float KeyDragThresholdPixels = 5f;
    private const float MarqueeThresholdPixels = 5f;

    public static void Draw(UiWorkspace workspace, AnimationEditorState state, AnimationDocument.AnimationTimeline? timeline, ImRect rect)
    {
        Im.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, 0xFF101010);

        if (timeline == null)
        {
            Im.Text("Create or select a timeline.", rect.X + Im.Style.Padding, rect.Y + Im.Style.Padding, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }

        float pixelsPerFrame = AnimationEditorHelpers.GetPixelsPerFrame(state);
        int duration = Math.Max(1, timeline.DurationFrames);
        int viewStartFrame = Math.Max(0, state.ViewStartFrame);

        var input = Im.Context.Input;
        if (rect.Contains(Im.MousePos))
        {
            if (input.KeyCtrl && input.ScrollDelta != 0f)
            {
                float oldPixelsPerFrame = pixelsPerFrame;
                float anchorFrame = viewStartFrame;
                if (oldPixelsPerFrame > 0.0001f)
                {
                    anchorFrame = viewStartFrame + (Im.MousePos.X - rect.X) / oldPixelsPerFrame;
                }

                float factor = MathF.Pow(1.15f, input.ScrollDelta);
                state.Zoom = Math.Clamp(state.Zoom * factor, 0.10f, 32f);

                pixelsPerFrame = AnimationEditorHelpers.GetPixelsPerFrame(state);
                if (pixelsPerFrame > 0.0001f)
                {
                    int newStart = (int)MathF.Round(anchorFrame - (Im.MousePos.X - rect.X) / pixelsPerFrame);
                    viewStartFrame = Math.Clamp(newStart, 0, duration);
                    state.ViewStartFrame = viewStartFrame;
                }
            }

            if (input.KeyCtrl && input.MouseMiddlePressed)
            {
                state.PanDrag.Active = true;
                state.PanDrag.StartMouseX = Im.MousePos.X;
                state.PanDrag.StartViewStartFrame = viewStartFrame;
            }
        }

        if (state.PanDrag.Active)
        {
            if (!input.MouseMiddleDown || !input.KeyCtrl)
            {
                state.PanDrag.Active = false;
            }
            else if (pixelsPerFrame > 0.0001f)
            {
                float dx = Im.MousePos.X - state.PanDrag.StartMouseX;
                int deltaFrames = (int)MathF.Round(dx / pixelsPerFrame);
                viewStartFrame = Math.Clamp(state.PanDrag.StartViewStartFrame - deltaFrames, 0, duration);
                state.ViewStartFrame = viewStartFrame;
            }
        }

        int workStartFrame = Math.Clamp(timeline.WorkStartFrame, 0, duration);
        int workEndFrame = Math.Clamp(timeline.WorkEndFrame, 0, duration);
        if (workEndFrame < workStartFrame)
        {
            (workStartFrame, workEndFrame) = (workEndFrame, workStartFrame);
        }

        float workX0 = rect.X + (workStartFrame - viewStartFrame) * pixelsPerFrame;
        float workX1 = rect.X + (workEndFrame - viewStartFrame) * pixelsPerFrame;
        if (workX1 < workX0)
        {
            (workX0, workX1) = (workX1, workX0);
        }

	        if (rect.Contains(Im.MousePos) && Im.MousePressed && !input.KeyCtrl && !input.KeyAlt && !input.KeyShift)
	        {
	            // Clear selection when clicking empty space (if we didn't hit a key or start marquee).
	            // Actual key hits cancel this via DrawTrackKeys.
	            state.Selected = default;
	            state.SelectedKeyFrames.Clear();
	            workspace.NotifyInspectorAnimationInteraction();
	        }

	        if (rect.Contains(Im.MousePos) && Im.MousePressed && !input.MouseMiddleDown)
	        {
	            state.KeyMarquee = new AnimationEditorState.KeyMarqueeDrag
	            {
	                Active = true,
	                Dragging = false,
	                Additive = input.KeyCtrl,
	                StartMouse = Im.MousePos,
	                CurrentMouse = Im.MousePos
	            };
	            workspace.NotifyInspectorAnimationInteraction();
	        }

        Im.PushClipRect(rect);
        DrawPlayheadLine(state, timeline, rect, pixelsPerFrame, viewStartFrame);

        for (int rowIndex = 0; rowIndex < state.Rows.Count; rowIndex++)
        {
            float y = rect.Y + rowIndex * AnimationEditorLayout.RowHeight - state.TrackScrollY;
            float rowH = AnimationEditorLayout.RowHeight;
            if (state.RowLayouts.Count == state.Rows.Count)
            {
                y = rect.Y + state.RowLayouts[rowIndex].OffsetY;
                rowH = state.RowLayouts[rowIndex].Height;
            }

            if (y > rect.Bottom || y + rowH < rect.Y)
            {
                continue;
            }

            var row = state.Rows[rowIndex];
            var rowRect = new ImRect(rect.X, y, rect.Width, rowH);

            uint line = ImStyle.WithAlphaF(Im.Style.Border, 0.35f);
            Im.DrawLine(rowRect.X, rowRect.Bottom, rowRect.Right, rowRect.Bottom, 1f, line);

            bool isPropertyRow = row.Kind == AnimationEditorState.RowKind.Property;
            bool isPropertyGroupRow = row.Kind == AnimationEditorState.RowKind.PropertyGroup;
            bool groupCollapsed = isPropertyGroupRow && AnimationEditorHelpers.IsGroupCollapsed(state, row.ChannelGroupId);

	            if ((isPropertyRow || isPropertyGroupRow) && row.IsAnimatable &&
	                rowRect.Contains(Im.MousePos) && Im.MousePressed)
	            {
	                var binding = new AnimationDocument.AnimationBinding(
                    targetIndex: row.TargetIndex,
                    componentKind: row.ComponentKind,
                    propertyIndexHint: row.PropertyIndexHint,
                    propertyId: row.PropertyId,
                    propertyKind: row.PropertyKind);

	                state.Selected = default;
	                state.SelectedTrack = new AnimationEditorState.TrackSelection { TimelineId = timeline.Id, Binding = binding };
	                state.GraphScopeChannelGroupKey = isPropertyGroupRow ? row.ChannelGroupId : 0UL;
	                workspace.NotifyInspectorAnimationInteraction();
	            }

            if ((isPropertyRow || isPropertyGroupRow) && row.IsAnimatable && !(isPropertyGroupRow && groupCollapsed))
            {
                var binding = new AnimationDocument.AnimationBinding(
                    targetIndex: row.TargetIndex,
                    componentKind: row.ComponentKind,
                    propertyIndexHint: row.PropertyIndexHint,
                    propertyId: row.PropertyId,
                    propertyKind: row.PropertyKind);

                var track = AnimationEditorHelpers.FindTrack(timeline, binding);
	                if (track != null)
	                {
                    if (state.SelectedTrack.TimelineId == timeline.Id && state.SelectedTrack.Binding == track.Binding)
                    {
                        uint highlight = ImStyle.WithAlphaF(Im.Style.Primary, 0.08f);
                        Im.DrawRect(rowRect.X, rowRect.Y, rowRect.Width, rowRect.Height, highlight);
	                    }
	                    ulong graphScopeKey = isPropertyGroupRow ? row.ChannelGroupId : 0UL;
	                    DrawTrackKeys(workspace, state, timeline, track, rowRect, pixelsPerFrame, viewStartFrame, duration, graphScopeKey);
	                }
	            }
            else if (isPropertyGroupRow && groupCollapsed)
            {
                var binding = new AnimationDocument.AnimationBinding(
                    targetIndex: row.TargetIndex,
                    componentKind: row.ComponentKind,
                    propertyIndexHint: row.PropertyIndexHint,
                    propertyId: row.PropertyId,
                    propertyKind: row.PropertyKind);

                if (state.SelectedTrack.TimelineId == timeline.Id && state.SelectedTrack.Binding == binding)
                {
                    uint highlight = ImStyle.WithAlphaF(Im.Style.Primary, 0.08f);
                    Im.DrawRect(rowRect.X, rowRect.Y, rowRect.Width, rowRect.Height, highlight);
                }

                DrawUnionKeysForChannelGroup(workspace, timeline, row, rowRect, pixelsPerFrame, viewStartFrame, duration);
            }
            else if (row.Kind == AnimationEditorState.RowKind.ComponentHeader)
            {
                if (AnimationEditorHelpers.IsComponentCollapsed(workspace, state, row.Entity, row.ComponentKind))
                {
                    DrawUnionKeysForComponent(timeline, row, rowRect, pixelsPerFrame, viewStartFrame, duration);
                }
            }
            else if (row.Kind == AnimationEditorState.RowKind.TargetHeader && row.TargetIndex >= 0 && AnimationEditorHelpers.IsTargetCollapsed(workspace, state, row.Entity))
            {
                    DrawUnionKeysForTarget(timeline, row, rowRect, pixelsPerFrame, viewStartFrame, duration);
            }
        }

	        UpdateAndDrawKeyMarquee(workspace, state, timeline, rect, pixelsPerFrame, viewStartFrame, duration);
	        Im.PopClipRect();

        float clipX0 = MathF.Max(rect.X, MathF.Min(rect.Right, workX0));
        float clipX1 = MathF.Max(rect.X, MathF.Min(rect.Right, workX1));
        if (clipX1 < clipX0)
        {
            (clipX0, clipX1) = (clipX1, clipX0);
        }

        Im.PushClipRect(rect);
        float leftW = MathF.Max(0f, clipX0 - rect.X);
        float rightW = MathF.Max(0f, rect.Right - clipX1);
        uint dim = 0xAA0A0A0A;
        if (leftW > 0f)
        {
            Im.DrawRect(rect.X, rect.Y, leftW, rect.Height, dim);
        }
        if (rightW > 0f)
        {
            Im.DrawRect(clipX1, rect.Y, rightW, rect.Height, dim);
        }

        uint boundary = 0xFF2A2A2A;
        Im.DrawLine(clipX0, rect.Y, clipX0, rect.Bottom, 1f, boundary);
        Im.DrawLine(clipX1, rect.Y, clipX1, rect.Bottom, 1f, boundary);
        Im.PopClipRect();
    }

    private static void DrawPlayheadLine(AnimationEditorState state, AnimationDocument.AnimationTimeline timeline, ImRect rect, float pixelsPerFrame, int viewStartFrame)
    {
        int duration = Math.Max(1, timeline.DurationFrames);
        state.CurrentFrame = Math.Clamp(state.CurrentFrame, 0, duration);
        float x = rect.X + (state.CurrentFrame - viewStartFrame) * pixelsPerFrame;
        Im.DrawLine(x, rect.Y, x, rect.Bottom, 2f, 0xFF33CCFF);
    }

	    private static void UpdateAndDrawKeyMarquee(
	        UiWorkspace workspace,
	        AnimationEditorState state,
	        AnimationDocument.AnimationTimeline timeline,
	        ImRect rect,
	        float pixelsPerFrame,
        int viewStartFrame,
        int duration)
    {
        if (!state.KeyMarquee.Active)
        {
            return;
        }

        if (!Im.MouseDown)
        {
	            if (state.KeyMarquee.Dragging)
	            {
	                SelectKeysInMarquee(workspace, state, timeline, rect, pixelsPerFrame, viewStartFrame, duration);
	            }
	            state.KeyMarquee = default;
	            return;
	        }

        state.KeyMarquee.CurrentMouse = Im.MousePos;

        float dx = state.KeyMarquee.CurrentMouse.X - state.KeyMarquee.StartMouse.X;
        float dy = state.KeyMarquee.CurrentMouse.Y - state.KeyMarquee.StartMouse.Y;
        if (!state.KeyMarquee.Dragging)
        {
            float distSq = dx * dx + dy * dy;
            if (distSq >= (MarqueeThresholdPixels * MarqueeThresholdPixels))
            {
                state.KeyMarquee.Dragging = true;
            }
        }

        if (state.KeyMarquee.Dragging)
        {
            float x0 = MathF.Min(state.KeyMarquee.StartMouse.X, state.KeyMarquee.CurrentMouse.X);
            float y0 = MathF.Min(state.KeyMarquee.StartMouse.Y, state.KeyMarquee.CurrentMouse.Y);
            float x1 = MathF.Max(state.KeyMarquee.StartMouse.X, state.KeyMarquee.CurrentMouse.X);
            float y1 = MathF.Max(state.KeyMarquee.StartMouse.Y, state.KeyMarquee.CurrentMouse.Y);
            x0 = MathF.Max(rect.X, MathF.Min(rect.Right, x0));
            x1 = MathF.Max(rect.X, MathF.Min(rect.Right, x1));
            y0 = MathF.Max(rect.Y, MathF.Min(rect.Bottom, y0));
            y1 = MathF.Max(rect.Y, MathF.Min(rect.Bottom, y1));
            var marquee = new ImRect(x0, y0, MathF.Max(0f, x1 - x0), MathF.Max(0f, y1 - y0));

            uint fill = ImStyle.WithAlphaF(Im.Style.Primary, 0.12f);
            uint stroke = ImStyle.WithAlphaF(Im.Style.Primary, 0.65f);
            Im.DrawRect(marquee.X, marquee.Y, marquee.Width, marquee.Height, fill);
            Im.DrawRoundedRectStroke(marquee.X, marquee.Y, marquee.Width, marquee.Height, radius: 0f, stroke, strokeWidth: 1f);
        }
    }

	    private static void SelectKeysInMarquee(
	        UiWorkspace workspace,
	        AnimationEditorState state,
	        AnimationDocument.AnimationTimeline timeline,
	        ImRect rect,
	        float pixelsPerFrame,
        int viewStartFrame,
        int duration)
    {
        float x0 = MathF.Min(state.KeyMarquee.StartMouse.X, state.KeyMarquee.CurrentMouse.X);
        float y0 = MathF.Min(state.KeyMarquee.StartMouse.Y, state.KeyMarquee.CurrentMouse.Y);
        float x1 = MathF.Max(state.KeyMarquee.StartMouse.X, state.KeyMarquee.CurrentMouse.X);
        float y1 = MathF.Max(state.KeyMarquee.StartMouse.Y, state.KeyMarquee.CurrentMouse.Y);
        x0 = MathF.Max(rect.X, MathF.Min(rect.Right, x0));
        x1 = MathF.Max(rect.X, MathF.Min(rect.Right, x1));
        y0 = MathF.Max(rect.Y, MathF.Min(rect.Bottom, y0));
        y1 = MathF.Max(rect.Y, MathF.Min(rect.Bottom, y1));

        if (!state.KeyMarquee.Additive)
        {
            state.SelectedKeyFrames.Clear();
        }

        var selectionRect = new ImRect(x0, y0, MathF.Max(0f, x1 - x0), MathF.Max(0f, y1 - y0));

        float keySize = 8f;
        AnimationEditorState.SelectedKeyFrame firstSelected = default;
        bool hasFirst = false;

        for (int rowIndex = 0; rowIndex < state.Rows.Count; rowIndex++)
        {
            float rowY = rect.Y + rowIndex * AnimationEditorLayout.RowHeight - state.TrackScrollY;
            float rowH = AnimationEditorLayout.RowHeight;
            if (state.RowLayouts.Count == state.Rows.Count)
            {
                rowY = rect.Y + state.RowLayouts[rowIndex].OffsetY;
                rowH = state.RowLayouts[rowIndex].Height;
            }

            if (rowY > rect.Bottom || rowY + rowH < rect.Y)
            {
                continue;
            }

            var row = state.Rows[rowIndex];
            bool isPropertyRow = row.Kind == AnimationEditorState.RowKind.Property;
            bool isPropertyGroupRow = row.Kind == AnimationEditorState.RowKind.PropertyGroup;
            if (!(isPropertyRow || isPropertyGroupRow) || !row.IsAnimatable)
            {
                continue;
            }

            bool groupCollapsed = isPropertyGroupRow && AnimationEditorHelpers.IsGroupCollapsed(state, row.ChannelGroupId);
            if (groupCollapsed)
            {
                continue;
            }

            float cy = rowY + rowH * 0.5f;
            if (cy + keySize < selectionRect.Y || cy - keySize > selectionRect.Bottom)
            {
                continue;
            }

            var binding = new AnimationDocument.AnimationBinding(
                targetIndex: row.TargetIndex,
                componentKind: row.ComponentKind,
                propertyIndexHint: row.PropertyIndexHint,
                propertyId: row.PropertyId,
                propertyKind: row.PropertyKind);

            var track = AnimationEditorHelpers.FindTrack(timeline, binding);
            if (track == null || track.Keys.Count == 0)
            {
                continue;
            }

            for (int keyIndex = 0; keyIndex < track.Keys.Count; keyIndex++)
            {
                int frame = track.Keys[keyIndex].Frame;
                float keyX = rect.X + (frame - viewStartFrame) * pixelsPerFrame;
                var hit = new ImRect(keyX - keySize, cy - keySize, keySize * 2f, keySize * 2f);
                if (!hit.Overlaps(selectionRect))
                {
                    continue;
                }

                AddSelectedKeyFrame(state, timeline.Id, track.Binding, frame);
                if (!hasFirst)
                {
                    hasFirst = true;
                    firstSelected = new AnimationEditorState.SelectedKeyFrame
                    {
                        TimelineId = timeline.Id,
                        Binding = track.Binding,
                        Frame = frame
                    };
                }
            }
        }

        if (hasFirst)
        {
            var firstTrack = AnimationEditorHelpers.FindTrack(timeline, firstSelected.Binding);
            if (firstTrack != null)
            {
                for (int i = 0; i < firstTrack.Keys.Count; i++)
                {
                    if (firstTrack.Keys[i].Frame == firstSelected.Frame)
                    {
                        state.Selected = new AnimationEditorState.SelectedKey
                        {
                            TimelineId = timeline.Id,
                            Binding = firstSelected.Binding,
                            KeyIndex = i
                        };
                        state.SelectedTrack = new AnimationEditorState.TrackSelection { TimelineId = timeline.Id, Binding = firstSelected.Binding };
                        workspace.NotifyInspectorAnimationInteraction();
                        break;
                    }
                }
            }
        }
        else if (!state.KeyMarquee.Additive)
        {
            state.Selected = default;
        }
    }

    private static void AddSelectedKeyFrame(AnimationEditorState state, int timelineId, AnimationDocument.AnimationBinding binding, int frame)
    {
        for (int i = 0; i < state.SelectedKeyFrames.Count; i++)
        {
            var existing = state.SelectedKeyFrames[i];
            if (existing.TimelineId == timelineId && existing.Binding == binding && existing.Frame == frame)
            {
                return;
            }
        }

        state.SelectedKeyFrames.Add(new AnimationEditorState.SelectedKeyFrame
        {
            TimelineId = timelineId,
            Binding = binding,
            Frame = frame
        });
    }

    private static void DrawUnionKeysForTarget(
        AnimationDocument.AnimationTimeline timeline,
        in AnimationEditorState.Row row,
        ImRect rowRect,
        float pixelsPerFrame,
        int viewStartFrame,
        int duration)
    {
        for (int i = 0; i < timeline.Tracks.Count; i++)
        {
            var track = timeline.Tracks[i];
            if (track.Binding.TargetIndex != row.TargetIndex || track.Keys.Count <= 0)
            {
                continue;
            }
            DrawUnionKeys(track, rowRect, pixelsPerFrame, viewStartFrame, duration);
        }
    }

    private static void DrawUnionKeysForComponent(
        AnimationDocument.AnimationTimeline timeline,
        in AnimationEditorState.Row row,
        ImRect rowRect,
        float pixelsPerFrame,
        int viewStartFrame,
        int duration)
    {
        for (int i = 0; i < timeline.Tracks.Count; i++)
        {
            var track = timeline.Tracks[i];
            if (track.Binding.TargetIndex != row.TargetIndex || track.Binding.ComponentKind != row.ComponentKind || track.Keys.Count <= 0)
            {
                continue;
            }
            DrawUnionKeys(track, rowRect, pixelsPerFrame, viewStartFrame, duration);
        }
    }

    private static void DrawUnionKeysForChannelGroup(
        UiWorkspace workspace,
        AnimationDocument.AnimationTimeline timeline,
        in AnimationEditorState.Row row,
        ImRect rowRect,
        float pixelsPerFrame,
        int viewStartFrame,
        int duration)
    {
        for (int i = 0; i < timeline.Tracks.Count; i++)
        {
            var track = timeline.Tracks[i];
            if (track.Binding.TargetIndex != row.TargetIndex || track.Binding.ComponentKind != row.ComponentKind || track.Keys.Count <= 0)
            {
                continue;
            }

            if (!AnimationEditorHelpers.TryResolveSlot(workspace, timeline, track.Binding, out PropertySlot slot))
            {
                continue;
            }

            if (!PropertyDispatcher.TryGetInfo(slot.Component, (int)slot.PropertyIndex, out var info))
            {
                continue;
            }

            ulong key = AnimationEditorHelpers.MakeChannelGroupKey(row.ComponentKind, info.ChannelGroupId);
            if (key != row.ChannelGroupId)
            {
                continue;
            }

            DrawUnionKeys(track, rowRect, pixelsPerFrame, viewStartFrame, duration);
        }
    }

    private static void DrawUnionKeys(
        AnimationDocument.AnimationTrack track,
        ImRect rowRect,
        float pixelsPerFrame,
        int viewStartFrame,
        int duration)
    {
        float keySize = 6f;
        float cy = rowRect.Y + rowRect.Height * 0.5f;
        uint color = ImStyle.WithAlphaF(Im.Style.Primary, 0.85f);

        for (int keyIndex = 0; keyIndex < track.Keys.Count; keyIndex++)
        {
            int frame = track.Keys[keyIndex].Frame;
            if ((uint)frame > (uint)duration)
            {
                continue;
            }

            float x = rowRect.X + (frame - viewStartFrame) * pixelsPerFrame;
            if (x < rowRect.X - keySize || x > rowRect.Right + keySize)
            {
                continue;
            }

            AnimationEditorHelpers.DrawDiamond(x, cy, keySize, color);
        }
    }

	    private static void DrawTrackKeys(
	        UiWorkspace workspace,
	        AnimationEditorState state,
	        AnimationDocument.AnimationTimeline timeline,
	        AnimationDocument.AnimationTrack track,
        ImRect rowRect,
        float pixelsPerFrame,
        int viewStartFrame,
        int duration,
        ulong graphScopeChannelGroupKey)
    {
        // Only allow key time-dragging if the drag started on a key in this canvas.
        if (state.KeyDrag.Active &&
            state.KeyDrag.TimelineId == timeline.Id &&
            state.KeyDrag.Binding == track.Binding)
        {
            if (!Im.MouseDown)
            {
                state.KeyDrag = default;
            }
            else
            {
                int keyIndex = state.KeyDrag.KeyIndex;
                if ((uint)keyIndex < (uint)track.Keys.Count)
                {
                    if (!state.KeyDrag.Dragging)
                    {
                        float dx = MathF.Abs(Im.MousePos.X - state.KeyDrag.StartMouseX);
                        if (dx >= KeyDragThresholdPixels)
                        {
                            state.KeyDrag.Dragging = true;
                        }
                    }

                    if (state.KeyDrag.Dragging)
                    {
                        int deltaFrames = (int)MathF.Round((Im.MousePos.X - state.KeyDrag.StartMouseX) / pixelsPerFrame);
                        int newFrame = state.KeyDrag.StartFrame + deltaFrames;
                        newFrame = Math.Clamp(newFrame, 0, duration);

                        var key = track.Keys[keyIndex];
                        int oldFrame = key.Frame;
                        if (key.Frame != newFrame)
                        {
                            key.Frame = newFrame;
                            track.Keys[keyIndex] = key;
                            UpdateSelectedKeyFrame(state, timeline.Id, track.Binding, oldFrame, newFrame);
                            AnimationEditorHelpers.SortKeys(track.Keys);
                            state.PreviewDirty = true;
                            state.Selected = FindSelectedKeyIndexAfterSort(timeline.Id, track, newFrame);
                            state.SelectedTrack = new AnimationEditorState.TrackSelection
                            {
                                TimelineId = timeline.Id,
                                Binding = track.Binding
                            };
                            state.GraphScopeChannelGroupKey = graphScopeChannelGroupKey;
                            state.KeyDrag.KeyIndex = state.Selected.KeyIndex;
                        }
                    }
                }
                else
                {
                    state.KeyDrag = default;
                }
            }
        }

        float keySize = 8f;
        float cy = rowRect.Y + rowRect.Height * 0.5f;

        for (int keyIndex = 0; keyIndex < track.Keys.Count; keyIndex++)
        {
            int frame = track.Keys[keyIndex].Frame;
            float x = rowRect.X + (frame - viewStartFrame) * pixelsPerFrame;
            if (x < rowRect.X - keySize || x > rowRect.Right + keySize)
            {
                continue;
            }

            bool selected =
                state.Selected.TimelineId == timeline.Id &&
                state.Selected.Binding == track.Binding &&
                state.Selected.KeyIndex == keyIndex;

            bool selectedViaMarquee = IsSelectedKeyFrame(state, timeline.Id, track.Binding, frame);

            uint color = selected ? 0xFF33CCFF : selectedViaMarquee ? 0xFF66DDFF : 0xFFCCCCCC;
            AnimationEditorHelpers.DrawDiamond(x, cy, keySize, color);

            var hit = new ImRect(x - keySize, cy - keySize, keySize * 2f, keySize * 2f);
            if (hit.Contains(Im.MousePos) && Im.MousePressed)
            {
                state.KeyMarquee = default;
                state.Selected = new AnimationEditorState.SelectedKey
                {
                    TimelineId = timeline.Id,
                    Binding = track.Binding,
                    KeyIndex = keyIndex
                };
                state.SelectedTrack = new AnimationEditorState.TrackSelection
                {
                    TimelineId = timeline.Id,
                    Binding = track.Binding
                };
                workspace.NotifyInspectorAnimationInteraction();
                state.GraphScopeChannelGroupKey = graphScopeChannelGroupKey;

                var input = Im.Context.Input;
                if (!input.KeyCtrl)
                {
                    state.SelectedKeyFrames.Clear();
                    AddSelectedKeyFrame(state, timeline.Id, track.Binding, frame);
                }
                else
                {
                    ToggleSelectedKeyFrame(state, timeline.Id, track.Binding, frame);
                }

                state.KeyDrag = new AnimationEditorState.KeyframeDrag
                {
                    Active = true,
                    Dragging = false,
                    TimelineId = timeline.Id,
                    Binding = track.Binding,
                    KeyIndex = keyIndex,
                    StartMouseX = Im.MousePos.X,
                    StartFrame = frame
                };
            }
        }
    }

    private static bool IsSelectedKeyFrame(AnimationEditorState state, int timelineId, AnimationDocument.AnimationBinding binding, int frame)
    {
        for (int i = 0; i < state.SelectedKeyFrames.Count; i++)
        {
            var selected = state.SelectedKeyFrames[i];
            if (selected.TimelineId == timelineId && selected.Binding == binding && selected.Frame == frame)
            {
                return true;
            }
        }

        return false;
    }

    private static void ToggleSelectedKeyFrame(AnimationEditorState state, int timelineId, AnimationDocument.AnimationBinding binding, int frame)
    {
        for (int i = 0; i < state.SelectedKeyFrames.Count; i++)
        {
            var existing = state.SelectedKeyFrames[i];
            if (existing.TimelineId == timelineId && existing.Binding == binding && existing.Frame == frame)
            {
                state.SelectedKeyFrames.RemoveAt(i);
                return;
            }
        }

        AddSelectedKeyFrame(state, timelineId, binding, frame);
    }

    private static void UpdateSelectedKeyFrame(AnimationEditorState state, int timelineId, AnimationDocument.AnimationBinding binding, int oldFrame, int newFrame)
    {
        for (int i = 0; i < state.SelectedKeyFrames.Count; i++)
        {
            var selected = state.SelectedKeyFrames[i];
            if (selected.TimelineId == timelineId && selected.Binding == binding && selected.Frame == oldFrame)
            {
                selected.Frame = newFrame;
                state.SelectedKeyFrames[i] = selected;
                return;
            }
        }
    }

    private static AnimationEditorState.SelectedKey FindSelectedKeyIndexAfterSort(int timelineId, AnimationDocument.AnimationTrack track, int frame)
    {
        for (int i = 0; i < track.Keys.Count; i++)
        {
            if (track.Keys[i].Frame == frame)
            {
                return new AnimationEditorState.SelectedKey
                {
                    TimelineId = timelineId,
                    Binding = track.Binding,
                    KeyIndex = i
                };
            }
        }

        return default;
    }
}
