using System;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;

namespace Derp.UI;

public sealed partial class UiWorkspace
{
    internal void ShowToast(string message, int durationFrames = 180)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        _toastMessage = message;
        _toastStartFrame = Im.Context.FrameCount;
        _toastEndFrame = _toastStartFrame + Math.Max(1, durationFrames);
    }

    internal void ShowToast(ReadOnlySpan<char> message, int durationFrames = 180)
    {
        if (message.IsEmpty)
        {
            return;
        }

        ShowToast(message.ToString(), durationFrames);
    }

    internal void DrawToasts()
    {
        int now = Im.Context.FrameCount;
        if (_toastEndFrame <= now || string.IsNullOrEmpty(_toastMessage))
        {
            return;
        }

        float t = 1f;
        int remaining = _toastEndFrame - now;
        const int fadeFrames = 20;
        if (remaining < fadeFrames)
        {
            t = remaining / (float)fadeFrames;
        }

        uint bg = ImStyle.WithAlphaF(0xFF000000, 0.75f * t);
        uint text = ImStyle.WithAlphaF(Im.Style.TextPrimary, t);

        ReadOnlySpan<char> msg = _toastMessage.AsSpan();
        float fontSize = Im.Style.FontSize;
        float paddingX = 12f;
        float paddingY = 8f;
        float textW = msg.Length * (fontSize * 0.55f);
        float w = textW + paddingX * 2f;
        float h = fontSize + paddingY * 2f;

        var vp = Im.CurrentViewport;
        if (vp == null)
        {
            return;
        }
        float x = vp.Size.X * 0.5f - w * 0.5f;
        float y = vp.Size.Y - h - 18f;

        Im.DrawRoundedRect(x, y, w, h, Im.Style.CornerRadius, bg);
        Im.Text(msg, x + paddingX, y + paddingY, fontSize, text);
    }
}
