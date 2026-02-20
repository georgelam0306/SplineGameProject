using System;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using Property;

namespace Derp.UI;

internal static class AnimationInterpolationPanelWidget
{
    public static void Draw(UiWorkspace workspace, AnimationEditorState state, AnimationDocument.AnimationTimeline? timeline, ImRect rect)
    {
        Im.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.Background);
        Im.Text("Interpolation", rect.X + Im.Style.Padding, rect.Y + 6f, Im.Style.FontSize, Im.Style.TextPrimary);

        if (timeline == null || state.Selected.TimelineId != timeline.Id)
        {
            Im.Text("Select a key", rect.X + Im.Style.Padding, rect.Y + 30f, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }

        var track = AnimationEditorHelpers.FindTrack(timeline, state.Selected.Binding);
        if (track == null || state.Selected.KeyIndex < 0 || state.Selected.KeyIndex >= track.Keys.Count)
        {
            Im.Text("Select a key", rect.X + Im.Style.Padding, rect.Y + 30f, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }

        int selectedIndex = state.Selected.KeyIndex;
        var selectedKey = track.Keys[selectedIndex];

        int nextIndex = FindNextKeyIndex(track, selectedIndex);
        if (nextIndex < 0)
        {
            Im.Text("Select a key with a next key", rect.X + Im.Style.Padding, rect.Y + 30f, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }

        var nextKey = track.Keys[nextIndex];

        float y = rect.Y + 34f;
        float x = rect.X + Im.Style.Padding;

        float buttonW = 72f;
        float buttonH = Im.Style.MinButtonHeight;
        float buttonSpacing = Im.Style.Spacing;

        bool isFloat = state.Selected.Binding.PropertyKind == PropertyKind.Float;
        bool changed = false;

        if (Im.Button("Step", x, y, buttonW, buttonH))
        {
            ApplyPresetStep(ref selectedKey, ref nextKey);
            changed = true;
        }
        if (Im.Button("Linear", x + (buttonW + buttonSpacing) * 1f, y, buttonW, buttonH))
        {
            ApplyPresetLinear(ref selectedKey, ref nextKey, isFloat);
            changed = true;
        }
        uint disabledText = Im.Style.TextSecondary;
        uint enabledText = Im.Style.TextPrimary;

        if (isFloat)
        {
            if (Im.Button("Ease In", x + (buttonW + buttonSpacing) * 2f, y, buttonW, buttonH))
            {
                ApplyPresetEaseIn(ref selectedKey, ref nextKey);
                changed = true;
            }

            y += buttonH + buttonSpacing;

            if (Im.Button("Ease Out", x, y, buttonW, buttonH))
            {
                ApplyPresetEaseOut(ref selectedKey, ref nextKey);
                changed = true;
            }

            if (Im.Button("Ease InOut", x + (buttonW + buttonSpacing) * 1f, y, buttonW * 1.35f, buttonH))
            {
                ApplyPresetEaseInOut(ref selectedKey, ref nextKey);
                changed = true;
            }
        }
        else
        {
            Im.Text("Ease presets (Float only)", x + (buttonW + buttonSpacing) * 2f, y + 4f, Im.Style.FontSize, disabledText);
            y += buttonH + buttonSpacing;
        }

        if (changed)
        {
            track.Keys[selectedIndex] = selectedKey;
            track.Keys[nextIndex] = nextKey;
        }

        y += buttonH + buttonSpacing;

        if (!isFloat)
        {
            Im.Text("Curve preview (Float only)", rect.X + Im.Style.Padding, y, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }

        var previewRect = new ImRect(rect.X + Im.Style.Padding, y, rect.Width - Im.Style.Padding * 2f, rect.Bottom - y - Im.Style.Padding);
        if (previewRect.Width <= 0f || previewRect.Height <= 0f)
        {
            return;
        }

        DrawSegmentPreview(ref selectedKey, ref nextKey, previewRect, enabledText);
    }

    private static int FindNextKeyIndex(AnimationDocument.AnimationTrack track, int selectedIndex)
    {
        if ((uint)selectedIndex >= (uint)track.Keys.Count)
        {
            return -1;
        }

        int frame = track.Keys[selectedIndex].Frame;
        int nextIndex = -1;
        int nextFrame = int.MaxValue;

        for (int i = 0; i < track.Keys.Count; i++)
        {
            int f = track.Keys[i].Frame;
            if (f <= frame)
            {
                continue;
            }

            if (f < nextFrame)
            {
                nextFrame = f;
                nextIndex = i;
            }
        }

        return nextIndex;
    }

    private static void ApplyPresetStep(ref AnimationDocument.AnimationKeyframe a, ref AnimationDocument.AnimationKeyframe b)
    {
        a.Interpolation = AnimationDocument.Interpolation.Step;
        a.OutTangent = 0f;
        b.InTangent = 0f;
    }

    private static void ApplyPresetLinear(ref AnimationDocument.AnimationKeyframe a, ref AnimationDocument.AnimationKeyframe b, bool isFloat)
    {
        a.Interpolation = AnimationDocument.Interpolation.Linear;
        if (isFloat)
        {
            float delta = b.Value.Float - a.Value.Float;
            a.OutTangent = delta;
            b.InTangent = delta;
        }
        else
        {
            a.OutTangent = 0f;
            b.InTangent = 0f;
        }
    }

    private static void ApplyPresetEaseIn(ref AnimationDocument.AnimationKeyframe a, ref AnimationDocument.AnimationKeyframe b)
    {
        a.Interpolation = AnimationDocument.Interpolation.Cubic;
        float delta = b.Value.Float - a.Value.Float;
        a.OutTangent = 0f;
        b.InTangent = 2f * delta;
    }

    private static void ApplyPresetEaseOut(ref AnimationDocument.AnimationKeyframe a, ref AnimationDocument.AnimationKeyframe b)
    {
        a.Interpolation = AnimationDocument.Interpolation.Cubic;
        float delta = b.Value.Float - a.Value.Float;
        a.OutTangent = 2f * delta;
        b.InTangent = 0f;
    }

    private static void ApplyPresetEaseInOut(ref AnimationDocument.AnimationKeyframe a, ref AnimationDocument.AnimationKeyframe b)
    {
        a.Interpolation = AnimationDocument.Interpolation.Cubic;
        a.OutTangent = 0f;
        b.InTangent = 0f;
    }

    private static void DrawSegmentPreview(
        ref AnimationDocument.AnimationKeyframe a,
        ref AnimationDocument.AnimationKeyframe b,
        ImRect rect,
        uint curveColor)
    {
        Im.PushClipRect(rect);
        Im.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.Surface);
        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, ImStyle.WithAlphaF(Im.Style.Border, 0.6f), 1f);

        float delta = b.Value.Float - a.Value.Float;
        float m0 = 0f;
        float m1 = 0f;
        if (MathF.Abs(delta) > 0.000001f)
        {
            m0 = a.OutTangent / delta;
            m1 = b.InTangent / delta;
        }

        const int samples = 48;
        float minV = float.PositiveInfinity;
        float maxV = float.NegativeInfinity;

        for (int i = 0; i <= samples; i++)
        {
            float t = i / (float)samples;
            float v;

            if (a.Interpolation == AnimationDocument.Interpolation.Step)
            {
                v = i < samples ? 0f : 1f;
            }
            else if (a.Interpolation == AnimationDocument.Interpolation.Linear)
            {
                v = t;
            }
            else
            {
                v = HermiteInterpolate(0f, m0, 1f, m1, t);
            }

            if (v < minV) minV = v;
            if (v > maxV) maxV = v;
        }

        if (!float.IsFinite(minV) || !float.IsFinite(maxV))
        {
            minV = 0f;
            maxV = 1f;
        }

        float range = maxV - minV;
        if (!float.IsFinite(range) || range < 0.00001f)
        {
            range = 1f;
        }

        float pad = range * 0.10f;
        minV -= pad;
        maxV += pad;
        range = maxV - minV;
        if (!float.IsFinite(range) || range < 0.00001f)
        {
            minV = 0f;
            maxV = 1f;
            range = 1f;
        }

        float invRange = 1f / range;
        float midX = rect.X + rect.Width * 0.5f;

        uint grid = ImStyle.WithAlphaF(Im.Style.Border, 0.25f);
        Im.DrawLine(midX, rect.Y, midX, rect.Bottom, 1f, grid);

        float y0 = rect.Bottom - (0f - minV) * invRange * rect.Height;
        float y1 = rect.Bottom - (1f - minV) * invRange * rect.Height;
        if (y0 >= rect.Y && y0 <= rect.Bottom)
        {
            Im.DrawLine(rect.X, y0, rect.Right, y0, 1f, grid);
        }
        if (y1 >= rect.Y && y1 <= rect.Bottom)
        {
            Im.DrawLine(rect.X, y1, rect.Right, y1, 1f, grid);
        }

        float prevX = rect.X;
        float prevY = rect.Bottom - (0f - minV) * invRange * rect.Height;

        for (int i = 0; i <= samples; i++)
        {
            float t = i / (float)samples;
            float v;

            if (a.Interpolation == AnimationDocument.Interpolation.Step)
            {
                v = i < samples ? 0f : 1f;
            }
            else if (a.Interpolation == AnimationDocument.Interpolation.Linear)
            {
                v = t;
            }
            else
            {
                v = HermiteInterpolate(0f, m0, 1f, m1, t);
            }

            float x = rect.X + t * rect.Width;
            float y = rect.Bottom - (v - minV) * invRange * rect.Height;

            if (i > 0)
            {
                Im.DrawLine(prevX, prevY, x, y, 2f, curveColor);
            }

            prevX = x;
            prevY = y;
        }

        Im.PopClipRect();
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
