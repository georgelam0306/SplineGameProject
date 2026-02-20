using System;
using DerpLib.ImGui;

namespace Derp.UI;

internal static class AnimationEditorText
{
    public static void DrawEntityLabel(UiWorkspace workspace, EntityId entity, float x, float y, uint color)
    {
        Span<char> buffer = stackalloc char[32];

        UiNodeType nodeType = workspace.World.GetNodeType(entity);
        ReadOnlySpan<char> prefix =
            nodeType == UiNodeType.Prefab ? "Prefab " :
            nodeType == UiNodeType.BooleanGroup ? "Boolean " :
            "Shape ";

        int written = 0;
        prefix.CopyTo(buffer);
        written += prefix.Length;

        uint stableId = workspace.World.GetStableId(entity);
        int value = stableId == 0 ? entity.Value : (int)stableId;
        value.TryFormat(buffer[written..], out int idWritten);
        written += idWritten;

        Im.Text(buffer[..written], x, y, Im.Style.FontSize, color);
    }

    public static void DrawLabelWithInt(ReadOnlySpan<char> prefix, int value, float x, float y, uint color)
    {
        Span<char> buffer = stackalloc char[32];
        int written = 0;
        prefix.CopyTo(buffer);
        written += prefix.Length;
        value.TryFormat(buffer[written..], out int valueWritten);
        written += valueWritten;
        Im.Text(buffer[..written], x, y, Im.Style.FontSize, color);
    }

    public static string GetPlaybackModeLabel(AnimationDocument.PlaybackMode mode)
    {
        return mode switch
        {
            AnimationDocument.PlaybackMode.OneShot => "One Shot",
            AnimationDocument.PlaybackMode.Loop => "Loop",
            _ => "Ping Pong"
        };
    }

    public static void DrawTimecode(int frame, int fps, float x, float y, uint color)
    {
        if (fps <= 0)
        {
            fps = 60;
        }

        int totalSeconds = frame / fps;
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds - minutes * 60;
        int subFrame = frame - totalSeconds * fps;

        Span<char> buffer = stackalloc char[16];
        int written = 0;

        written += WriteTwoDigits(buffer[written..], minutes);
        buffer[written++] = ':';
        written += WriteTwoDigits(buffer[written..], seconds);
        buffer[written++] = ':';
        written += WriteTwoDigits(buffer[written..], subFrame);

        Im.Text(buffer[..written], x, y, Im.Style.FontSize, color);
    }

    private static int WriteTwoDigits(Span<char> buffer, int value)
    {
        int v = value;
        if (v < 0)
        {
            v = 0;
        }

        int tens = v / 10;
        int ones = v - tens * 10;

        if (buffer.Length < 2)
        {
            return 0;
        }

        buffer[0] = (char)('0' + (tens % 10));
        buffer[1] = (char)('0' + (ones % 10));
        return 2;
    }
}
