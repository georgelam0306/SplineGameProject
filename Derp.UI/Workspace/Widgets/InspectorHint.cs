using DerpLib.ImGui;
using DerpLib.ImGui.Layout;

namespace Derp.UI;

internal static class InspectorHint
{
    public static void Draw(string text)
    {
        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = InspectorRow.GetPaddedRect(rect);
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(text.AsSpan(), rect.X, textY, Im.Style.FontSize, Im.Style.TextSecondary);
    }
}

