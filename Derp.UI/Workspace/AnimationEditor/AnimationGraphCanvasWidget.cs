using System;
using System.Numerics;
using Core;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Widgets;
using Property;
using Property.Runtime;

namespace Derp.UI;

internal static class AnimationGraphCanvasWidget
{
    private static readonly uint[] ChannelColors =
    [
        0xFFEF5350, // X
        0xFF66BB6A, // Y
        0xFF42A5F5, // Z
        0xFFAB47BC, // W
    ];

    private static readonly uint[] ColorChannelColors =
    [
        0xFFEF5350, // R
        0xFF66BB6A, // G
        0xFF42A5F5, // B
        0xFFB0BEC5, // A
    ];

    public static void Draw(UiWorkspace workspace, AnimationEditorState state, AnimationDocument.AnimationTimeline timeline, ImRect rect)
    {
        Im.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, 0xFF101010);

        float pixelsPerFrame = AnimationEditorHelpers.GetPixelsPerFrame(state);
        int durationFrames = Math.Max(1, timeline.DurationFrames);
        int viewStartFrame = Math.Clamp(state.ViewStartFrame, 0, durationFrames);
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
                    viewStartFrame = Math.Clamp(newStart, 0, durationFrames);
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
                viewStartFrame = Math.Clamp(state.PanDrag.StartViewStartFrame - deltaFrames, 0, durationFrames);
                state.ViewStartFrame = viewStartFrame;
            }
        }

        if (pixelsPerFrame <= 0f)
        {
            return;
        }

        float viewFrames = rect.Width / pixelsPerFrame;
        if (!float.IsFinite(viewFrames) || viewFrames <= 0f)
        {
            return;
        }

        int workStartFrame = Math.Clamp(timeline.WorkStartFrame, 0, durationFrames);
        int workEndFrame = Math.Clamp(timeline.WorkEndFrame, 0, durationFrames);
        if (workEndFrame < workStartFrame)
        {
            (workStartFrame, workEndFrame) = (workEndFrame, workStartFrame);
        }

        bool hasSelection = false;
        var selection = default(AnimationDocument.AnimationBinding);

        if (state.SelectedTrack.TimelineId == timeline.Id)
        {
            selection = state.SelectedTrack.Binding;
            hasSelection = true;
        }
        else if (state.Selected.TimelineId == timeline.Id)
        {
            selection = state.Selected.Binding;
            hasSelection = true;
        }

        if (!hasSelection)
        {
            Im.Text("Select a track to edit curves.", rect.X + Im.Style.Padding, rect.Y + Im.Style.Padding, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }

        bool isAutoSelection = selection.PropertyKind == PropertyKind.Auto;
        bool isGroupSelection = isAutoSelection && selection.PropertyId != 0UL;
        var curveRect = rect;
        if (curveRect.Width <= 0f || curveRect.Height <= 0f)
        {
            return;
        }

        if (isGroupSelection)
        {
            DrawChannelGroupCurves(workspace, state, timeline, selection, curveRect, durationFrames, viewStartFrame, viewFrames);
            DrawWorkAreaOverlay(rect, viewStartFrame, pixelsPerFrame, workStartFrame, workEndFrame);
            return;
        }

        if (isAutoSelection)
        {
            if (selection.ComponentKind != 0)
            {
                DrawComponentCurves(workspace, state, timeline, selection, curveRect, durationFrames, viewStartFrame, viewFrames);
                DrawWorkAreaOverlay(rect, viewStartFrame, pixelsPerFrame, workStartFrame, workEndFrame);
                return;
            }

            DrawTargetCurves(workspace, state, timeline, selection, curveRect, durationFrames, viewStartFrame, viewFrames);
            DrawWorkAreaOverlay(rect, viewStartFrame, pixelsPerFrame, workStartFrame, workEndFrame);
            return;
        }

        if (state.GraphScopeChannelGroupKey != 0UL)
        {
            DrawChannelGroupCurvesFromChannelSelection(workspace, state, timeline, selection, state.GraphScopeChannelGroupKey, curveRect, durationFrames, viewStartFrame, viewFrames);
            DrawWorkAreaOverlay(rect, viewStartFrame, pixelsPerFrame, workStartFrame, workEndFrame);
            return;
        }

        var track = AnimationEditorHelpers.FindTrack(timeline, selection);
        if (track == null || track.Keys.Count <= 0)
        {
            Im.Text("No keyed track selected.", rect.X + Im.Style.Padding, rect.Y + Im.Style.Padding, Im.Style.FontSize, Im.Style.TextSecondary);
            DrawWorkAreaOverlay(rect, viewStartFrame, pixelsPerFrame, workStartFrame, workEndFrame);
            return;
        }

        if (selection.PropertyKind == PropertyKind.Color32)
        {
            ulong viewKey = MakeGraphRangeKeySingle(timeline.Id, selection) ^ 0xC01C010Au;
            ResetGraphViewIfNeeded(state, viewKey);
            DrawColor32TrackCurves(state, timeline, selection, track, curveRect, durationFrames, viewStartFrame, viewFrames);
            DrawWorkAreaOverlay(rect, viewStartFrame, pixelsPerFrame, workStartFrame, workEndFrame);
            return;
        }

        if (selection.PropertyKind != PropertyKind.Float)
        {
            Im.Text("Graph editor currently supports Float tracks.", rect.X + Im.Style.Padding, rect.Y + Im.Style.Padding, Im.Style.FontSize, Im.Style.TextSecondary);
            DrawWorkAreaOverlay(rect, viewStartFrame, pixelsPerFrame, workStartFrame, workEndFrame);
            return;
        }

        float minValue;
        float maxValue;
        ulong rangeKey = MakeGraphRangeKeySingle(timeline.Id, selection);
        GetStableGraphRangeForTrack(state, rangeKey, track, out minValue, out maxValue);
        ResetGraphViewIfNeeded(state, rangeKey);

        if (track.Keys.Count > Curve.MaxPoints)
        {
            Im.Text("Graph supports up to 16 keys/track.", rect.X + Im.Style.Padding, rect.Y + Im.Style.Padding, Im.Style.FontSize, ImStyle.WithAlphaF(Im.Style.TextSecondary, 0.85f));
            DrawWorkAreaOverlay(rect, viewStartFrame, pixelsPerFrame, workStartFrame, workEndFrame);
            return;
        }

        Curve curve = default;
        Span<int> keyIndices = stackalloc int[Curve.MaxPoints];
        Span<AnimationDocument.Interpolation> originalInterpolation = stackalloc AnimationDocument.Interpolation[Curve.MaxPoints];
        Span<float> originalInTangents = stackalloc float[Curve.MaxPoints];
        Span<float> originalOutTangents = stackalloc float[Curve.MaxPoints];
        int pointCount = 0;
        for (int i = 0; i < track.Keys.Count; i++)
        {
            var key = track.Keys[i];
            float t = (key.Frame - viewStartFrame) / viewFrames;
            if (t < 0f || t > 1f)
            {
                continue;
            }

            keyIndices[pointCount] = i;
            originalInterpolation[pointCount] = key.Interpolation;
            originalInTangents[pointCount] = key.InTangent;
            originalOutTangents[pointCount] = key.OutTangent;
            curve.Add(new CurvePoint(t, key.Value.Float, tangentIn: key.InTangent, tangentOut: key.OutTangent));
            pointCount++;
        }

        if (pointCount == 0)
        {
            Im.Text("No keys in view.", rect.X + Im.Style.Padding, rect.Y + Im.Style.Padding, Im.Style.FontSize, Im.Style.TextSecondary);
            DrawWorkAreaOverlay(rect, viewStartFrame, pixelsPerFrame, workStartFrame, workEndFrame);
            return;
        }

        var ctx = Im.Context;
        ctx.PushId(MakeCurveEditorId(selection));
        int editorId = ctx.GetId("curve");
        ctx.PopId();

        uint prevCurveColor = ImCurveEditor.CurveColor;
        uint prevPointColor = ImCurveEditor.PointColor;
        uint prevSelectedColor = ImCurveEditor.SelectedPointColor;

        ImCurveEditor.CurveColor = Im.Style.Primary;
        ImCurveEditor.PointColor = 0xFFFFFFFF;
        ImCurveEditor.SelectedPointColor = Im.Style.Primary;

        DrawGraphBackgroundGrid(curveRect, viewStartFrame, viewFrames);

        bool allowCurveEditorInput = !(input.KeyCtrl && (input.ScrollDelta != 0f || input.MouseMiddleDown));
        var view = new ImCurveEditor.CurveView
        {
            ZoomX = 1f,
            ZoomY = state.GraphZoomY,
            PanOffset = new Vector2(0f, state.GraphPanY)
        };

        Im.PushClipRect(curveRect);
        bool changed = ImCurveEditor.DrawAt(
            editorId,
            ref curve,
            curveRect.X,
            curveRect.Y,
            curveRect.Width,
            curveRect.Height,
            minValue,
            maxValue,
            drawBackground: false,
            drawGrid: false,
            drawHandles: true,
            handleInput: allowCurveEditorInput,
            ref view);
        Im.PopClipRect();

        DrawGraphYAxisLabels(curveRect, minValue, maxValue, view);

        state.GraphZoomY = view.ZoomY;
        state.GraphPanY = view.PanOffset.Y;

        ImCurveEditor.CurveColor = prevCurveColor;
        ImCurveEditor.PointColor = prevPointColor;
        ImCurveEditor.SelectedPointColor = prevSelectedColor;

        if (changed)
        {
            state.PreviewDirty = true;
            for (int i = 0; i < pointCount; i++)
            {
                ref var p = ref curve[i];
                int keyIndex = keyIndices[i];
                var key = track.Keys[keyIndex];
                int frame = viewStartFrame + (int)MathF.Round(p.Time * viewFrames);
                key.Frame = Math.Clamp(frame, 0, durationFrames);
                key.Value = PropertyValue.FromFloat(p.Value);

                var prevInterpolation = originalInterpolation[i];
                float prevInTangent = originalInTangents[i];
                float prevOutTangent = originalOutTangents[i];
                bool tangentChanged = MathF.Abs(prevInTangent - p.TangentIn) > 0.001f || MathF.Abs(prevOutTangent - p.TangentOut) > 0.001f;

                key.Interpolation = tangentChanged ? AnimationDocument.Interpolation.Cubic : prevInterpolation;
                if (key.Interpolation == AnimationDocument.Interpolation.Cubic)
                {
                    key.InTangent = p.TangentIn;
                    key.OutTangent = p.TangentOut;
                }
                else
                {
                    key.InTangent = prevInTangent;
                    key.OutTangent = prevOutTangent;
                }
                track.Keys[keyIndex] = key;
            }
            AnimationEditorHelpers.SortKeys(track.Keys);
        }

        DrawWorkAreaOverlay(rect, viewStartFrame, pixelsPerFrame, workStartFrame, workEndFrame);
    }

    private static int MakeCurveEditorId(in AnimationDocument.AnimationBinding binding)
    {
        unchecked
        {
            ulong bits = binding.PropertyId;
            bits ^= ((ulong)binding.TargetIndex << 32);
            bits ^= ((ulong)binding.ComponentKind << 48);
            bits ^= ((ulong)binding.PropertyKind << 56);
            return (int)(bits ^ (bits >> 32));
        }
    }

    private static void DrawColor32TrackCurves(
        AnimationEditorState state,
        AnimationDocument.AnimationTimeline timeline,
        in AnimationDocument.AnimationBinding binding,
        AnimationDocument.AnimationTrack track,
        ImRect rect,
        int durationFrames,
        int viewStartFrame,
        float viewFrames)
    {
        if (track.Keys.Count > Curve.MaxPoints)
        {
            Im.Text("Graph supports up to 16 keys/track.", rect.X + Im.Style.Padding, rect.Y + Im.Style.Padding, Im.Style.FontSize, ImStyle.WithAlphaF(Im.Style.TextSecondary, 0.85f));
            return;
        }

        if (state.GraphActiveTimelineId != timeline.Id || state.GraphActiveBinding != binding)
        {
            state.GraphActiveTimelineId = timeline.Id;
            state.GraphActiveBinding = binding;
            state.GraphActiveColorChannel = 0;
        }

        byte activeChannel = state.GraphActiveColorChannel;
        if (activeChannel > 3)
        {
            activeChannel = 0;
        }

        var view = new ImCurveEditor.CurveView
        {
            ZoomX = 1f,
            ZoomY = state.GraphZoomY,
            PanOffset = new Vector2(0f, state.GraphPanY)
        };

        if (rect.Contains(Im.MousePos) && Im.MousePressed)
        {
            if (TryPickColorChannelByKey(track, rect, viewStartFrame, viewFrames, 0f, 255f, view, out byte pickedChannel))
            {
                activeChannel = pickedChannel;
            }
        }

        state.GraphActiveColorChannel = activeChannel;

        uint prevCurveColor = ImCurveEditor.CurveColor;
        uint prevPointColor = ImCurveEditor.PointColor;
        uint prevSelectedColor = ImCurveEditor.SelectedPointColor;

        DrawGraphBackgroundGrid(rect, viewStartFrame, viewFrames);

        var input = Im.Context.Input;
        bool allowCurveEditorInput = !(input.KeyCtrl && (input.ScrollDelta != 0f || input.MouseMiddleDown));

        Im.PushClipRect(rect);

        Span<int> keyIndices = stackalloc int[Curve.MaxPoints];
        for (int channelIndex = 0; channelIndex < 4; channelIndex++)
        {
            Curve curve = default;
            int pointCount = 0;

            for (int keyIndex = 0; keyIndex < track.Keys.Count; keyIndex++)
            {
                var key = track.Keys[keyIndex];
                float t = (key.Frame - viewStartFrame) / viewFrames;
                if (t < 0f || t > 1f)
                {
                    continue;
                }

                keyIndices[pointCount] = keyIndex;

                Color32 c = key.Value.Color32;
                float v = channelIndex switch
                {
                    0 => c.R,
                    1 => c.G,
                    2 => c.B,
                    _ => c.A
                };

                curve.Add(new CurvePoint(t, v, tangentIn: 0f, tangentOut: 0f));
                pointCount++;
            }

            if (pointCount <= 0)
            {
                continue;
            }

            uint color = ColorChannelColors[channelIndex];
            ImCurveEditor.CurveColor = color;
            ImCurveEditor.PointColor = 0xFFFFFFFF;
            ImCurveEditor.SelectedPointColor = color;

            int editorId = unchecked(MakeCurveEditorId(binding) ^ (int)(0x4B1D1C0Du + (uint)channelIndex * 0x9E3779B9u));
            bool isActiveCurve = channelIndex == activeChannel;

            bool changed = ImCurveEditor.DrawAt(
                editorId,
                ref curve,
                rect.X,
                rect.Y,
                rect.Width,
                rect.Height,
                minValue: 0f,
                maxValue: 255f,
                drawBackground: false,
                drawGrid: false,
                drawHandles: false,
                handleInput: isActiveCurve && allowCurveEditorInput,
                ref view);

            if (changed && isActiveCurve)
            {
                state.PreviewDirty = true;

                for (int i = 0; i < pointCount; i++)
                {
                    ref var p = ref curve[i];
                    int keyIndex = keyIndices[i];
                    var key = track.Keys[keyIndex];

                    int frame = viewStartFrame + (int)MathF.Round(p.Time * viewFrames);
                    key.Frame = Math.Clamp(frame, 0, durationFrames);

                    int channelValue = (int)MathF.Round(p.Value);
                    channelValue = Math.Clamp(channelValue, 0, 255);
                    byte b = (byte)channelValue;

                    Color32 prev = key.Value.Color32;
                    Color32 next = channelIndex switch
                    {
                        0 => new Color32(b, prev.G, prev.B, prev.A),
                        1 => new Color32(prev.R, b, prev.B, prev.A),
                        2 => new Color32(prev.R, prev.G, b, prev.A),
                        _ => new Color32(prev.R, prev.G, prev.B, b),
                    };

                    key.Value = PropertyValue.FromColor32(next);
                    track.Keys[keyIndex] = key;
                }

                AnimationEditorHelpers.SortKeys(track.Keys);
            }
        }

        Im.PopClipRect();

        DrawGraphYAxisLabels(rect, 0f, 255f, view);

        state.GraphZoomY = view.ZoomY;
        state.GraphPanY = view.PanOffset.Y;

        ImCurveEditor.CurveColor = prevCurveColor;
        ImCurveEditor.PointColor = prevPointColor;
        ImCurveEditor.SelectedPointColor = prevSelectedColor;
    }

    private static bool TryPickColorChannelByKey(
        AnimationDocument.AnimationTrack track,
        ImRect rect,
        int viewStartFrame,
        float viewFrames,
        float minValue,
        float maxValue,
        in ImCurveEditor.CurveView view,
        out byte channelIndex)
    {
        channelIndex = 0;

        float range = maxValue - minValue;
        if (!float.IsFinite(range) || range <= 0.000001f)
        {
            range = 1f;
        }

        float invRange = 1f / range;
        float bestDistSq = float.MaxValue;
        int bestChannel = 0;
        bool found = false;

        for (int ch = 0; ch < 4; ch++)
        {
            for (int keyIndex = 0; keyIndex < track.Keys.Count; keyIndex++)
            {
                var key = track.Keys[keyIndex];
                float t = (key.Frame - viewStartFrame) / viewFrames;
                if (t < 0f || t > 1f)
                {
                    continue;
                }

                Color32 c = key.Value.Color32;
                float v = ch switch
                {
                    0 => c.R,
                    1 => c.G,
                    2 => c.B,
                    _ => c.A
                };

                float x = rect.X + (t * view.ZoomX + view.PanOffset.X) * rect.Width;
                float normalized = (v - minValue) * invRange;
                float y = rect.Y + rect.Height - ((normalized * view.ZoomY) + view.PanOffset.Y) * rect.Height;

                float dx = Im.MousePos.X - x;
                float dy = Im.MousePos.Y - y;
                float distSq = dx * dx + dy * dy;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestChannel = ch;
                    found = true;
                }
            }
        }

        if (!found)
        {
            return false;
        }

        channelIndex = (byte)bestChannel;
        return true;
    }

    private static void ComputeValueRange(AnimationDocument.AnimationTrack track, out float minValue, out float maxValue)
    {
        minValue = 0f;
        maxValue = 0f;

        if (track.Keys.Count <= 0)
        {
            return;
        }

        float min = track.Keys[0].Value.Float;
        float max = min;

        for (int i = 1; i < track.Keys.Count; i++)
        {
            float v = track.Keys[i].Value.Float;
            if (v < min) min = v;
            if (v > max) max = v;
        }

        float range = max - min;
        if (range < 0.00001f)
        {
            range = 1f;
        }

        float pad = range * 0.10f;
        minValue = min - pad;
        maxValue = max + pad;

        EnsureValidRange(ref minValue, ref maxValue);
    }

    private static ulong MakeGraphRangeKeySingle(int timelineId, in AnimationDocument.AnimationBinding binding)
    {
        unchecked
        {
            ulong key = 0xA1u;
            key = (key << 32) ^ (uint)timelineId;
            key = (key << 32) ^ (uint)binding.TargetIndex;
            key = (key << 16) ^ binding.ComponentKind;
            key ^= binding.PropertyId;
            key ^= (ulong)binding.PropertyKind << 56;
            return key;
        }
    }

    private static ulong MakeGraphRangeKeyScope(int timelineId, uint kind, int targetIndex, ushort componentKind, ulong scopeKey)
    {
        unchecked
        {
            ulong key = kind;
            key = (key << 32) ^ (uint)timelineId;
            key = (key << 32) ^ (uint)targetIndex;
            key = (key << 16) ^ componentKind;
            key ^= scopeKey;
            return key;
        }
    }

    private static void GetStableGraphRangeForTrack(AnimationEditorState state, ulong rangeKey, AnimationDocument.AnimationTrack track, out float minValue, out float maxValue)
    {
        bool recompute = state.GraphRangeKey != rangeKey || !Im.MouseDown;
        if (!recompute)
        {
            minValue = state.GraphMinValue;
            maxValue = state.GraphMaxValue;

            // While dragging, keep the range stable, but allow it to expand if the user drags keys outside
            // the current bounds so the curve doesn't appear to "cap" at the edge.
            ComputeValueRange(track, out float currentMin, out float currentMax);
            float expandRange = currentMax - currentMin;
            float expandPad = expandRange * 0.15f;
            currentMin -= expandPad;
            currentMax += expandPad;
            EnsureValidRange(ref currentMin, ref currentMax);

            if (currentMin < minValue || currentMax > maxValue)
            {
                minValue = MathF.Min(minValue, currentMin);
                maxValue = MathF.Max(maxValue, currentMax);
                state.GraphMinValue = minValue;
                state.GraphMaxValue = maxValue;
            }
            return;
        }

        ComputeValueRange(track, out minValue, out maxValue);
        float range = maxValue - minValue;
        float extraPad = range * 0.15f;
        minValue -= extraPad;
        maxValue += extraPad;
        EnsureValidRange(ref minValue, ref maxValue);

        state.GraphRangeKey = rangeKey;
        state.GraphMinValue = minValue;
        state.GraphMaxValue = maxValue;
    }

    private static void GetStableGraphRangeForTracks(AnimationEditorState state, ulong rangeKey, AnimationDocument.AnimationTimeline timeline, ReadOnlySpan<int> trackIndices, int trackCount, out float minValue, out float maxValue)
    {
        bool recompute = state.GraphRangeKey != rangeKey || !Im.MouseDown;
        if (!recompute)
        {
            minValue = state.GraphMinValue;
            maxValue = state.GraphMaxValue;

            float currentMinValue = minValue;
            float currentMaxValue = maxValue;
            bool hasAny = false;

            for (int i = 0; i < trackCount; i++)
            {
                var track = timeline.Tracks[trackIndices[i]];
                for (int k = 0; k < track.Keys.Count; k++)
                {
                    float v = track.Keys[k].Value.Float;
                    if (!hasAny)
                    {
                        currentMinValue = v;
                        currentMaxValue = v;
                        hasAny = true;
                    }
                    else
                    {
                        if (v < currentMinValue) currentMinValue = v;
                        if (v > currentMaxValue) currentMaxValue = v;
                    }
                }
            }

            if (hasAny)
            {
                float expandRange = currentMaxValue - currentMinValue;
                if (!float.IsFinite(expandRange) || expandRange < 0.00001f)
                {
                    expandRange = 1f;
                }

                float expandPad = expandRange * 0.25f;
                currentMinValue -= expandPad;
                currentMaxValue += expandPad;
                EnsureValidRange(ref currentMinValue, ref currentMaxValue);

                if (currentMinValue < minValue || currentMaxValue > maxValue)
                {
                    minValue = MathF.Min(minValue, currentMinValue);
                    maxValue = MathF.Max(maxValue, currentMaxValue);
                    state.GraphMinValue = minValue;
                    state.GraphMaxValue = maxValue;
                }
            }
            return;
        }

        minValue = 0f;
        maxValue = 0f;
        bool init = false;

        for (int i = 0; i < trackCount; i++)
        {
            var track = timeline.Tracks[trackIndices[i]];
            for (int k = 0; k < track.Keys.Count; k++)
            {
                float v = track.Keys[k].Value.Float;
                if (!init)
                {
                    minValue = v;
                    maxValue = v;
                    init = true;
                }
                else
                {
                    if (v < minValue) minValue = v;
                    if (v > maxValue) maxValue = v;
                }
            }
        }

        float range = maxValue - minValue;
        if (!float.IsFinite(range) || range < 0.00001f)
        {
            range = 1f;
        }

        float pad = range * 0.25f;
        minValue -= pad;
        maxValue += pad;
        EnsureValidRange(ref minValue, ref maxValue);

        state.GraphRangeKey = rangeKey;
        state.GraphMinValue = minValue;
        state.GraphMaxValue = maxValue;
    }

    private static void DrawChannelGroupCurvesFromChannelSelection(
        UiWorkspace workspace,
        AnimationEditorState state,
        AnimationDocument.AnimationTimeline timeline,
        in AnimationDocument.AnimationBinding channelBinding,
        ulong groupKey,
        ImRect rect,
        int durationFrames,
        int viewStartFrame,
        float viewFrames)
    {
        var selection = new AnimationDocument.AnimationBinding(
            targetIndex: channelBinding.TargetIndex,
            componentKind: channelBinding.ComponentKind,
            propertyIndexHint: 0,
            propertyId: groupKey,
            propertyKind: PropertyKind.Auto);

        DrawChannelGroupCurves(workspace, state, timeline, selection, rect, durationFrames, viewStartFrame, viewFrames);
    }

    private static void EnsureValidRange(ref float minValue, ref float maxValue)
    {
        if (!float.IsFinite(minValue) || !float.IsFinite(maxValue))
        {
            minValue = 0f;
            maxValue = 1f;
            return;
        }
        if (maxValue < minValue)
        {
            (minValue, maxValue) = (maxValue, minValue);
        }
        if (maxValue == minValue)
        {
            minValue = MathF.BitDecrement(minValue);
            maxValue = MathF.BitIncrement(maxValue);
        }
    }

    private static void DrawChannelGroupCurves(
        UiWorkspace workspace,
        AnimationEditorState state,
        AnimationDocument.AnimationTimeline timeline,
        in AnimationDocument.AnimationBinding selection,
        ImRect rect,
        int durationFrames,
        int viewStartFrame,
        float viewFrames)
    {
        int targetIndex = selection.TargetIndex;
        if (targetIndex < 0 || targetIndex >= timeline.Targets.Count)
        {
            Im.Text("Invalid selection.", rect.X + Im.Style.Padding, rect.Y + Im.Style.Padding, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }

        uint stableId = timeline.Targets[targetIndex].StableId;
        EntityId targetEntity = workspace.World.GetEntityByStableId(stableId);
        if (targetEntity.IsNull || !workspace.World.TryGetComponent(targetEntity, selection.ComponentKind, out AnyComponentHandle component))
        {
            Im.Text("Invalid selection.", rect.X + Im.Style.Padding, rect.Y + Im.Style.Padding, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }

        ulong groupKey = selection.PropertyId;
        int propertyCount = PropertyDispatcher.GetPropertyCount(component);
        if (propertyCount <= 0)
        {
            Im.Text("Invalid selection.", rect.X + Im.Style.Padding, rect.Y + Im.Style.Padding, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }

        Span<int> trackIndices = stackalloc int[32];
        int trackCount = 0;

        for (int i = 0; i < timeline.Tracks.Count; i++)
        {
            var track = timeline.Tracks[i];
            var binding = track.Binding;

            if (binding.TargetIndex != selection.TargetIndex || binding.ComponentKind != selection.ComponentKind)
            {
                continue;
            }

            if (binding.PropertyKind != PropertyKind.Float || track.Keys.Count == 0)
            {
                continue;
            }

            if (!TryGetBindingChannelInfo(component, propertyCount, binding, out ulong channelGroupKey, out ushort channelIndex))
            {
                continue;
            }

            if (channelGroupKey != groupKey)
            {
                continue;
            }

            if (trackCount < trackIndices.Length)
            {
                trackIndices[trackCount] = i;
                trackCount++;
            }
        }

        if (trackCount == 0)
        {
            Im.Text("No keyed channels.", rect.X + Im.Style.Padding, rect.Y + Im.Style.Padding, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }

        float minValue;
        float maxValue;
        ulong rangeKey = MakeGraphRangeKeyScope(timeline.Id, kind: 0xB1u, selection.TargetIndex, selection.ComponentKind, groupKey);
        GetStableGraphRangeForTracks(state, rangeKey, timeline, trackIndices, trackCount, out minValue, out maxValue);
        ResetGraphViewIfNeeded(state, rangeKey);

        var view = new ImCurveEditor.CurveView
        {
            ZoomX = 1f,
            ZoomY = state.GraphZoomY,
            PanOffset = new Vector2(0f, state.GraphPanY)
        };

        DrawGraphBackgroundGrid(rect, viewStartFrame, viewFrames);

        DrawEditableCurvesInRect(workspace, state, timeline, rect, durationFrames, viewStartFrame, viewFrames, trackIndices, trackCount, minValue, maxValue, ref view);
        DrawGraphYAxisLabels(rect, minValue, maxValue, view);

        state.GraphZoomY = view.ZoomY;
        state.GraphPanY = view.PanOffset.Y;
    }

    private static bool TryGetBindingChannelInfo(
        AnyComponentHandle component,
        int propertyCount,
        in AnimationDocument.AnimationBinding binding,
        out ulong channelGroupKey,
        out ushort channelIndex)
    {
        channelGroupKey = 0;
        channelIndex = 0;

        ushort hint = binding.PropertyIndexHint;
        if (hint < propertyCount && PropertyDispatcher.TryGetInfo(component, hint, out var info) && info.PropertyId == binding.PropertyId)
        {
            if (!info.IsChannel)
            {
                return false;
            }

            channelGroupKey = AnimationEditorHelpers.MakeChannelGroupKey(component.Kind, info.ChannelGroupId);
            channelIndex = info.ChannelIndex;
            return true;
        }

        for (ushort i = 0; i < propertyCount; i++)
        {
            if (!PropertyDispatcher.TryGetInfo(component, i, out var propInfo))
            {
                continue;
            }

            if (propInfo.PropertyId != binding.PropertyId)
            {
                continue;
            }

            if (!propInfo.IsChannel)
            {
                return false;
            }

            channelGroupKey = AnimationEditorHelpers.MakeChannelGroupKey(component.Kind, propInfo.ChannelGroupId);
            channelIndex = propInfo.ChannelIndex;
            return true;
        }

        return false;
    }

    private static void DrawComponentCurves(
        UiWorkspace workspace,
        AnimationEditorState state,
        AnimationDocument.AnimationTimeline timeline,
        in AnimationDocument.AnimationBinding selection,
        ImRect rect,
        int durationFrames,
        int viewStartFrame,
        float viewFrames)
    {
        DrawMultiTrackCurves(
            workspace,
            state,
            timeline,
            selection.TargetIndex,
            componentKindFilter: selection.ComponentKind,
            rect,
            durationFrames,
            viewStartFrame,
            viewFrames,
            label: "Component");
    }

    private static void DrawTargetCurves(
        UiWorkspace workspace,
        AnimationEditorState state,
        AnimationDocument.AnimationTimeline timeline,
        in AnimationDocument.AnimationBinding selection,
        ImRect rect,
        int durationFrames,
        int viewStartFrame,
        float viewFrames)
    {
        DrawMultiTrackCurves(
            workspace,
            state,
            timeline,
            selection.TargetIndex,
            componentKindFilter: 0,
            rect,
            durationFrames,
            viewStartFrame,
            viewFrames,
            label: "Target");
    }

    private static void DrawMultiTrackCurves(
        UiWorkspace workspace,
        AnimationEditorState state,
        AnimationDocument.AnimationTimeline timeline,
        int targetIndex,
        ushort componentKindFilter,
        ImRect rect,
        int durationFrames,
        int viewStartFrame,
        float viewFrames,
        ReadOnlySpan<char> label)
    {
        if (targetIndex < 0 || targetIndex >= timeline.Targets.Count)
        {
            Im.Text("Invalid selection.", rect.X + Im.Style.Padding, rect.Y + Im.Style.Padding, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }

        const int maxTracks = 48;
        Span<int> indices = stackalloc int[maxTracks];
        int count = 0;

        for (int i = 0; i < timeline.Tracks.Count; i++)
        {
            var track = timeline.Tracks[i];
            var binding = track.Binding;

            if (binding.TargetIndex != targetIndex)
            {
                continue;
            }

            if (componentKindFilter != 0 && binding.ComponentKind != componentKindFilter)
            {
                continue;
            }

            if (track.Keys.Count <= 0)
            {
                continue;
            }

            if (binding.PropertyKind != PropertyKind.Float)
            {
                continue;
            }

            if (count < indices.Length)
            {
                indices[count++] = i;
            }
            else
            {
                break;
            }
        }

        if (count == 0)
        {
            Im.Text("No keyed tracks.", rect.X + Im.Style.Padding, rect.Y + Im.Style.Padding, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }

        float minValue;
        float maxValue;
        ulong scope = (ulong)componentKindFilter;
        ulong rangeKey = MakeGraphRangeKeyScope(timeline.Id, kind: 0xC1u, targetIndex, componentKindFilter, scope);
        GetStableGraphRangeForTracks(state, rangeKey, timeline, indices, count, out minValue, out maxValue);
        ResetGraphViewIfNeeded(state, rangeKey);

        var view = new ImCurveEditor.CurveView
        {
            ZoomX = 1f,
            ZoomY = state.GraphZoomY,
            PanOffset = new Vector2(0f, state.GraphPanY)
        };

        DrawGraphBackgroundGrid(rect, viewStartFrame, viewFrames);

        DrawEditableCurvesInRect(workspace, state, timeline, rect, durationFrames, viewStartFrame, viewFrames, indices, count, minValue, maxValue, ref view);
        DrawGraphYAxisLabels(rect, minValue, maxValue, view);
        state.GraphZoomY = view.ZoomY;
        state.GraphPanY = view.PanOffset.Y;
        Im.Text(label, rect.X + Im.Style.Padding, rect.Y + rect.Height - Im.Style.FontSize - 4f, Im.Style.FontSize * 0.8f, ImStyle.WithAlphaF(Im.Style.TextSecondary, 0.65f));
    }

    private static uint GetTrackColor(
        UiWorkspace workspace,
        AnimationDocument.AnimationTimeline timeline,
        in AnimationDocument.AnimationBinding binding)
    {
        if (binding.TargetIndex >= 0 && binding.TargetIndex < timeline.Targets.Count)
        {
            uint stableId = timeline.Targets[binding.TargetIndex].StableId;
            EntityId targetEntity = workspace.World.GetEntityByStableId(stableId);
            if (!targetEntity.IsNull && workspace.World.TryGetComponent(targetEntity, binding.ComponentKind, out AnyComponentHandle component))
            {
                int propertyCount = PropertyDispatcher.GetPropertyCount(component);
                if (TryGetBindingChannelInfo(component, propertyCount, binding, out _, out ushort channelIndex))
                {
                    return GetChannelColor(channelIndex);
                }
            }
        }

        unchecked
        {
            uint h = (uint)binding.PropertyId;
            h ^= (uint)(binding.PropertyId >> 32);
            h ^= (uint)binding.TargetIndex * 0x9E3779B9u;
            h ^= (uint)binding.ComponentKind * 0x85EBCA6Bu;
            h ^= (uint)binding.PropertyKind * 0xC2B2AE35u;

            uint r = (h & 0xFFu);
            uint g = ((h >> 8) & 0xFFu);
            uint b = ((h >> 16) & 0xFFu);

            r = 60u + (r % 160u);
            g = 60u + (g % 160u);
            b = 60u + (b % 160u);

            return 0xFF000000u | (r << 16) | (g << 8) | b;
        }
    }

    private static void DrawEditableCurvesInRect(
        UiWorkspace workspace,
        AnimationEditorState state,
        AnimationDocument.AnimationTimeline timeline,
        ImRect rect,
        int durationFrames,
        int viewStartFrame,
        float viewFrames,
        ReadOnlySpan<int> trackIndices,
        int trackCount,
        float minValue,
        float maxValue,
        ref ImCurveEditor.CurveView view)
    {
        if (state.GraphActiveTimelineId != timeline.Id)
        {
            state.GraphActiveTimelineId = timeline.Id;
            state.GraphActiveBinding = default;
        }

        var activeBinding = state.GraphActiveBinding;
        bool hasActive = false;
        for (int i = 0; i < trackCount; i++)
        {
            var b = timeline.Tracks[trackIndices[i]].Binding;
            if (b == activeBinding)
            {
                hasActive = true;
                break;
            }
        }
        if (!hasActive)
        {
            activeBinding = timeline.Tracks[trackIndices[0]].Binding;
        }

        if (rect.Contains(Im.MousePos) && Im.MousePressed)
        {
            if (TryPickCurveByKey(timeline, trackIndices, trackCount, rect, durationFrames, viewStartFrame, viewFrames, minValue, maxValue, out var picked))
            {
                activeBinding = picked;
            }
        }

        state.GraphActiveBinding = activeBinding;

        uint prevCurveColor = ImCurveEditor.CurveColor;
        uint prevPointColor = ImCurveEditor.PointColor;
        uint prevSelectedColor = ImCurveEditor.SelectedPointColor;

        Span<int> keyIndices = stackalloc int[Curve.MaxPoints];
        Span<AnimationDocument.Interpolation> originalInterpolation = stackalloc AnimationDocument.Interpolation[Curve.MaxPoints];
        Span<float> originalInTangents = stackalloc float[Curve.MaxPoints];
        Span<float> originalOutTangents = stackalloc float[Curve.MaxPoints];

        for (int i = 0; i < trackCount; i++)
        {
            var track = timeline.Tracks[trackIndices[i]];
            if (track.Keys.Count <= 0 || track.Binding.PropertyKind != PropertyKind.Float)
            {
                continue;
            }

            if (track.Keys.Count > Curve.MaxPoints)
            {
                continue;
            }

            bool isActiveCurve = track.Binding == activeBinding;
            uint color = GetTrackColor(workspace, timeline, track.Binding);

            ImCurveEditor.CurveColor = color;
            ImCurveEditor.PointColor = color;
            ImCurveEditor.SelectedPointColor = color;

            Curve curve = default;
            int pointCount = 0;
            for (int k = 0; k < track.Keys.Count; k++)
            {
                var key = track.Keys[k];
                float t = (key.Frame - viewStartFrame) / viewFrames;
                if (t < 0f || t > 1f)
                {
                    continue;
                }

                keyIndices[pointCount] = k;
                originalInterpolation[pointCount] = key.Interpolation;
                originalInTangents[pointCount] = key.InTangent;
                originalOutTangents[pointCount] = key.OutTangent;
                curve.Add(new CurvePoint(t, key.Value.Float, tangentIn: key.InTangent, tangentOut: key.OutTangent));
                pointCount++;
            }

            if (pointCount == 0)
            {
                continue;
            }

            var ctx = Im.Context;
            ctx.PushId(MakeCurveEditorId(track.Binding));
            int editorId = ctx.GetId("curve");
            ctx.PopId();

            Im.PushClipRect(rect);
            var input = Im.Context.Input;
            bool allowCurveEditorInput = !(input.KeyCtrl && (input.ScrollDelta != 0f || input.MouseMiddleDown));
            bool changed = ImCurveEditor.DrawAt(
                editorId,
                ref curve,
                rect.X,
                rect.Y,
                rect.Width,
                rect.Height,
                minValue,
                maxValue,
                drawBackground: false,
                drawGrid: false,
                drawHandles: isActiveCurve,
                handleInput: isActiveCurve && allowCurveEditorInput,
                ref view);
            Im.PopClipRect();

            if (changed)
            {
                state.PreviewDirty = true;
                for (int k = 0; k < pointCount; k++)
                {
                    ref var p = ref curve[k];
                    int keyIndex = keyIndices[k];
                    var key = track.Keys[keyIndex];
                    int frame = viewStartFrame + (int)MathF.Round(p.Time * viewFrames);
                    key.Frame = Math.Clamp(frame, 0, durationFrames);
                    key.Value = PropertyValue.FromFloat(p.Value);

                    var prevInterpolation = originalInterpolation[k];
                    float prevInTangent = originalInTangents[k];
                    float prevOutTangent = originalOutTangents[k];
                    bool tangentChanged = MathF.Abs(prevInTangent - p.TangentIn) > 0.001f || MathF.Abs(prevOutTangent - p.TangentOut) > 0.001f;

                    key.Interpolation = tangentChanged ? AnimationDocument.Interpolation.Cubic : prevInterpolation;
                    if (key.Interpolation == AnimationDocument.Interpolation.Cubic)
                    {
                        key.InTangent = p.TangentIn;
                        key.OutTangent = p.TangentOut;
                    }
                    else
                    {
                        key.InTangent = prevInTangent;
                        key.OutTangent = prevOutTangent;
                    }
                    track.Keys[keyIndex] = key;
                }
                AnimationEditorHelpers.SortKeys(track.Keys);
            }
        }

        ImCurveEditor.CurveColor = prevCurveColor;
        ImCurveEditor.PointColor = prevPointColor;
        ImCurveEditor.SelectedPointColor = prevSelectedColor;

        bool anyOversized = false;
        for (int i = 0; i < trackCount; i++)
        {
            if (timeline.Tracks[trackIndices[i]].Keys.Count > Curve.MaxPoints)
            {
                anyOversized = true;
                break;
            }
        }
        if (anyOversized)
        {
            Im.Text("Graph supports up to 16 keys/track.", rect.X + Im.Style.Padding, rect.Y + Im.Style.Padding, Im.Style.FontSize, ImStyle.WithAlphaF(Im.Style.TextSecondary, 0.85f));
        }
    }

    private static bool TryPickCurveByKey(
        AnimationDocument.AnimationTimeline timeline,
        ReadOnlySpan<int> trackIndices,
        int trackCount,
        ImRect rect,
        int durationFrames,
        int viewStartFrame,
        float viewFrames,
        float minValue,
        float maxValue,
        out AnimationDocument.AnimationBinding binding)
    {
        binding = default;

        float range = maxValue - minValue;
        if (!float.IsFinite(range) || range <= 0f)
        {
            return false;
        }

        float invRange = 1f / range;
        float bestDistSq = float.MaxValue;
        bool found = false;

        for (int i = 0; i < trackCount; i++)
        {
            var track = timeline.Tracks[trackIndices[i]];
            for (int k = 0; k < track.Keys.Count; k++)
            {
                var key = track.Keys[k];
                float time = (key.Frame - viewStartFrame) / viewFrames;
                if (time < 0f || time > 1f)
                {
                    continue;
                }
                float x = rect.X + time * rect.Width;
                float y = rect.Bottom - (key.Value.Float - minValue) * invRange * rect.Height;
                float dx = Im.MousePos.X - x;
                float dy = Im.MousePos.Y - y;
                float distSq = dx * dx + dy * dy;

                const float hitRadius = 10f;
                if (distSq <= hitRadius * hitRadius && distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    binding = track.Binding;
                    found = true;
                }
            }
        }

        return found;
    }

    private static void DrawGraphBackgroundGrid(ImRect rect, int viewStartFrame, float viewFrames)
    {
        Im.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.Surface);
        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, ImStyle.WithAlphaF(Im.Style.Border, 0.6f), 1f);

        if (viewFrames > 0f)
        {
            float pixelsPerFrame = rect.Width / viewFrames;
            int tickStep = GetTickStepFrames(pixelsPerFrame);
            if (tickStep <= 0)
            {
                tickStep = 1;
            }

            int endFrame = viewStartFrame + (int)(rect.Width / pixelsPerFrame) + 1;
            int firstTick = AlignUp(viewStartFrame, tickStep);

            uint minorGrid = ImStyle.WithAlphaF(Im.Style.Border, 0.18f);
            uint majorGrid = ImStyle.WithAlphaF(Im.Style.Border, 0.28f);

            for (int frame = firstTick; frame <= endFrame; frame += tickStep)
            {
                float x = rect.X + (frame - viewStartFrame) * pixelsPerFrame;
                if (x < rect.X || x > rect.Right)
                {
                    continue;
                }

                bool major = (frame % (tickStep * 5)) == 0;
                Im.DrawLine(x, rect.Y, x, rect.Bottom, 1f, major ? majorGrid : minorGrid);
            }
        }

        uint grid = ImStyle.WithAlphaF(Im.Style.Border, 0.25f);
        for (int i = 1; i < 4; i++)
        {
            float t = i / 4f;
            float y = rect.Y + rect.Height * t;
            Im.DrawLine(rect.X, y, rect.Right, y, 1f, grid);
        }
    }

    private static void DrawGraphYAxisLabels(ImRect rect, float minValue, float maxValue, in ImCurveEditor.CurveView view)
    {
        float range = maxValue - minValue;
        if (!float.IsFinite(range) || range <= 0.000001f)
        {
            range = 1f;
        }

        float zoomY = view.ZoomY;
        if (!float.IsFinite(zoomY) || zoomY == 0f)
        {
            zoomY = 1f;
        }

        float panY = view.PanOffset.Y;
        if (!float.IsFinite(panY))
        {
            panY = 0f;
        }

        float visibleMin = minValue + (-panY) / zoomY * range;
        float visibleMax = minValue + (1f - panY) / zoomY * range;
        if (visibleMax < visibleMin)
        {
            (visibleMin, visibleMax) = (visibleMax, visibleMin);
        }

        uint labelColor = ImStyle.WithAlphaF(Im.Style.TextSecondary, 0.75f);
        float fontSize = Im.Style.FontSize * 0.8f;
        float padX = 4f;

        Span<char> buffer = stackalloc char[32];

        for (int i = 0; i <= 4; i++)
        {
            float t = i / 4f;
            float v = visibleMax + (visibleMin - visibleMax) * t;
            float y = rect.Y + rect.Height * t - fontSize * 0.5f;

            int written = WriteFloat(buffer, v);
            if (written <= 0)
            {
                continue;
            }

            Im.Text(buffer[..written], rect.X + padX, y, fontSize, labelColor);
        }
    }

    private static int GetTickStepFrames(float pixelsPerFrame)
    {
        if (pixelsPerFrame >= 10f)
        {
            return 1;
        }
        if (pixelsPerFrame >= 6f)
        {
            return 2;
        }
        if (pixelsPerFrame >= 3f)
        {
            return 5;
        }
        if (pixelsPerFrame >= 1.5f)
        {
            return 10;
        }
        if (pixelsPerFrame >= 0.75f)
        {
            return 20;
        }
        if (pixelsPerFrame >= 0.35f)
        {
            return 50;
        }
        if (pixelsPerFrame >= 0.18f)
        {
            return 100;
        }
        if (pixelsPerFrame >= 0.09f)
        {
            return 200;
        }
        return 500;
    }

    private static int AlignUp(int value, int step)
    {
        if (step <= 0)
        {
            return value;
        }

        int rem = value % step;
        return rem == 0 ? value : value + (step - rem);
    }

    private static void DrawWorkAreaOverlay(
        ImRect rect,
        int viewStartFrame,
        float pixelsPerFrame,
        int workStartFrame,
        int workEndFrame)
    {
        float workX0 = rect.X + (workStartFrame - viewStartFrame) * pixelsPerFrame;
        float workX1 = rect.X + (workEndFrame - viewStartFrame) * pixelsPerFrame;
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

    private static void ResetGraphViewIfNeeded(AnimationEditorState state, ulong viewKey)
    {
        if (state.GraphViewKey == viewKey)
        {
            return;
        }

        state.GraphViewKey = viewKey;
        state.GraphZoomY = 1f;
        state.GraphPanY = 0f;
    }

    private static int WriteFloat(Span<char> buffer, float value)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        if (value.TryFormat(buffer, out int written, format: "0.##".AsSpan()))
        {
            return written;
        }

        return 0;
    }

    private static uint GetChannelColor(ushort channelIndex)
    {
        if (channelIndex < ChannelColors.Length)
        {
            return ChannelColors[channelIndex];
        }

        return Im.Style.Primary;
    }

    private static void DrawTrackCurve(
        AnimationDocument.AnimationTrack track,
        ImRect rect,
        int durationFrames,
        float minValue,
        float maxValue,
        uint color)
    {
        if (track.Keys.Count <= 0)
        {
            return;
        }

        float invRange = maxValue <= minValue ? 1f : 1f / (maxValue - minValue);

        for (int seg = 0; seg < track.Keys.Count - 1; seg++)
        {
            var k0 = track.Keys[seg];
            var k1 = track.Keys[seg + 1];

            float t0 = (float)k0.Frame / durationFrames;
            float t1 = (float)k1.Frame / durationFrames;
            if (t1 <= t0)
            {
                continue;
            }

            float x0 = rect.X + t0 * rect.Width;
            float x1 = rect.X + t1 * rect.Width;

            float y0 = rect.Bottom - (k0.Value.Float - minValue) * invRange * rect.Height;
            float y1 = rect.Bottom - (k1.Value.Float - minValue) * invRange * rect.Height;

            if (k0.Interpolation == AnimationDocument.Interpolation.Step)
            {
                Im.DrawLine(x0, y0, x1, y0, 2f, color);
                Im.DrawLine(x1, y0, x1, y1, 2f, color);
                continue;
            }

            float dx = MathF.Abs(x1 - x0);
            int subdivisions = Math.Clamp((int)MathF.Ceiling(dx / 12f), 2, 64);

            float prevX = x0;
            float prevY = y0;
            for (int i = 1; i <= subdivisions; i++)
            {
                float t = i / (float)subdivisions;
                float v;

                if (k0.Interpolation == AnimationDocument.Interpolation.Linear)
                {
                    v = k0.Value.Float + (k1.Value.Float - k0.Value.Float) * t;
                }
                else
                {
                    v = HermiteInterpolate(k0.Value.Float, k0.OutTangent, k1.Value.Float, k1.InTangent, t);
                }

                float time = t0 + (t1 - t0) * t;
                float x = rect.X + time * rect.Width;
                float y = rect.Bottom - (v - minValue) * invRange * rect.Height;

                Im.DrawLine(prevX, prevY, x, y, 2f, color);
                prevX = x;
                prevY = y;
            }
        }
    }

    private static void DrawTrackKeys(
        AnimationDocument.AnimationTrack track,
        ImRect rect,
        int durationFrames,
        float minValue,
        float maxValue,
        uint color)
    {
        if (track.Keys.Count <= 0)
        {
            return;
        }

        float invRange = maxValue <= minValue ? 1f : 1f / (maxValue - minValue);
        for (int i = 0; i < track.Keys.Count; i++)
        {
            var k = track.Keys[i];
            float t = (float)k.Frame / durationFrames;
            float x = rect.X + t * rect.Width;
            float y = rect.Bottom - (k.Value.Float - minValue) * invRange * rect.Height;

            float r = 4f;
            Im.DrawCircle(x, y, r, color);
            Im.DrawCircle(x, y, r - 1f, Im.Style.Surface);
            Im.DrawCircle(x, y, r - 2f, color);
        }
    }

    private static float HermiteInterpolate(float p0, float m0, float p1, float m1, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        float h00 = 2f * t3 - 3f * t2 + 1f;
        float h10 = t3 - 2f * t2 + t;
        float h01 = -2f * t3 + 3f * t2;
        float h11 = t3 - t2;

        return h00 * p0 + h10 * m0 + h01 * p1 + h11 * m1;
    }
}
