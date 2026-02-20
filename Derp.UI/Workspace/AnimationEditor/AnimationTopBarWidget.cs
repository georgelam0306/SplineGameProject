using System;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;

namespace Derp.UI;

internal static class AnimationTopBarWidget
{
    public static void Draw(UiWorkspace workspace, AnimationEditorState state, ImRect rect)
    {
        Im.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.Background);

        float x = rect.X + Im.Style.Padding;
        float y = rect.Y + 6f;

        if (Im.Button("Play", x, y, 60f, Im.Style.MinButtonHeight))
        {
            // TODO: playback
        }
        x += 60f + Im.Style.Spacing;

        if (Im.Button(state.GraphMode ? "Timeline" : "Graph", x, y, 84f, Im.Style.MinButtonHeight))
        {
            state.GraphMode = !state.GraphMode;
            state.Selected = default;
        }
        x += 84f + Im.Style.Spacing;

        if (!workspace.TryGetActiveAnimationDocument(out var doc))
        {
            Im.Text("No active prefab", x, y + 4f, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }

        doc.TryGetSelectedTimeline(out var timeline);
        if (timeline == null)
        {
            Im.Text("No timeline selected", x, y + 4f, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }

        if (Im.Button(AnimationEditorText.GetPlaybackModeLabel(timeline.Mode), x, y, 84f, Im.Style.MinButtonHeight))
        {
            timeline.Mode = timeline.Mode switch
            {
                AnimationDocument.PlaybackMode.OneShot => AnimationDocument.PlaybackMode.Loop,
                AnimationDocument.PlaybackMode.Loop => AnimationDocument.PlaybackMode.PingPong,
                _ => AnimationDocument.PlaybackMode.OneShot
            };
        }
        x += 84f + Im.Style.Spacing;

        AnimationEditorText.DrawLabelWithInt("FPS: ", timeline.SnapFps, x, y + 4f, Im.Style.TextSecondary);
        x += 70f;

        if (Im.Button("Dur-", x, y, 50f, Im.Style.MinButtonHeight))
        {
            timeline.DurationFrames = Math.Max(1, timeline.DurationFrames - timeline.SnapFps);
            timeline.WorkStartFrame = Math.Clamp(timeline.WorkStartFrame, 0, timeline.DurationFrames);
            timeline.WorkEndFrame = Math.Min(timeline.WorkEndFrame, timeline.DurationFrames);
            state.CurrentFrame = Math.Min(state.CurrentFrame, timeline.DurationFrames);
        }
        x += 50f + Im.Style.Spacing;

        if (Im.Button("Dur+", x, y, 50f, Im.Style.MinButtonHeight))
        {
            timeline.DurationFrames = Math.Max(1, timeline.DurationFrames + timeline.SnapFps);
            timeline.WorkEndFrame = Math.Clamp(timeline.WorkEndFrame, 0, timeline.DurationFrames);
        }
        x += 50f + Im.Style.Spacing;

        AnimationEditorText.DrawLabelWithInt("Frame: ", state.CurrentFrame, x, y + 4f, Im.Style.TextSecondary);
    }
}
