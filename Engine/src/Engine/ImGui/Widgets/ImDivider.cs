using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;

namespace DerpLib.ImGui.Widgets;

public static class ImDivider
{
    public static void Draw(float thickness = 1f)
    {
        Draw(thickness, Im.Style.Border);
    }

    public static void Draw(float thickness, uint color)
    {
        float width = ImLayout.RemainingWidth();
        var rect = ImLayout.AllocateRect(width, thickness + Im.Style.Spacing * 2f);
        DrawHorizontalAt(rect.X, rect.Y + Im.Style.Spacing, width, thickness, color);
    }

    public static void DrawHorizontalAt(float x, float y, float width, float thickness = 1f)
    {
        DrawHorizontalAt(x, y, width, thickness, Im.Style.Border);
    }

    public static void DrawHorizontalAt(float x, float y, float width, float thickness, uint color)
    {
        Im.DrawRect(x, y, width, thickness, color);
    }

    public static void DrawVertical(float height, float thickness = 1f)
    {
        DrawVertical(height, thickness, Im.Style.Border);
    }

    public static void DrawVertical(float height, float thickness, uint color)
    {
        var rect = ImLayout.AllocateRect(thickness + Im.Style.Spacing * 2f, height);
        DrawVerticalAt(rect.X + Im.Style.Spacing, rect.Y, height, thickness, color);
    }

    public static void DrawVerticalAt(float x, float y, float height, float thickness = 1f)
    {
        DrawVerticalAt(x, y, height, thickness, Im.Style.Border);
    }

    public static void DrawVerticalAt(float x, float y, float height, float thickness, uint color)
    {
        Im.DrawRect(x, y, thickness, height, color);
    }
}
