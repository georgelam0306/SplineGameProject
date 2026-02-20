using System;
using DerpLib.ImGui.Core;

namespace Derp.UI;

internal static class InspectorRow
{
    public const float PaddingX = 8f;

    public static ImRect GetPaddedRect(ImRect rect)
    {
        float x = rect.X + PaddingX;
        float width = Math.Max(1f, rect.Width - PaddingX * 2f);
        return new ImRect(x, rect.Y, width, rect.Height);
    }
}

