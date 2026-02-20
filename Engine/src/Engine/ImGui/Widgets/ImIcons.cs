using DerpLib.ImGui.Core;

namespace DerpLib.ImGui.Widgets;

public static class ImIcons
{
    public enum ChevronDirection : byte
    {
        Up = 0,
        Down = 1,
        Left = 2,
        Right = 3
    }

    public static void DrawChevron(float x, float y, float size, ChevronDirection direction, uint color, float thickness = 1.5f)
    {
        float halfSize = size * 0.5f;
        float cx = x + halfSize;
        float cy = y + halfSize;

        float armLen = halfSize * 0.9f;
        float sideOffset = armLen * 0.3f;
        float tipOffset = armLen * 0.5f;

        switch (direction)
        {
            case ChevronDirection.Up:
                Im.DrawLine(cx - armLen, cy + sideOffset, cx, cy - tipOffset, thickness, color);
                Im.DrawLine(cx, cy - tipOffset, cx + armLen, cy + sideOffset, thickness, color);
                break;
            case ChevronDirection.Down:
                Im.DrawLine(cx - armLen, cy - sideOffset, cx, cy + tipOffset, thickness, color);
                Im.DrawLine(cx, cy + tipOffset, cx + armLen, cy - sideOffset, thickness, color);
                break;
            case ChevronDirection.Left:
                Im.DrawLine(cx + sideOffset, cy - armLen, cx - tipOffset, cy, thickness, color);
                Im.DrawLine(cx - tipOffset, cy, cx + sideOffset, cy + armLen, thickness, color);
                break;
            default:
                Im.DrawLine(cx - sideOffset, cy - armLen, cx + tipOffset, cy, thickness, color);
                Im.DrawLine(cx + tipOffset, cy, cx - sideOffset, cy + armLen, thickness, color);
                break;
        }
    }

    public static void DrawDiamond(float cx, float cy, float size, uint color, float thickness = 1.5f)
    {
        float half = size * 0.5f;
        Im.DrawLine(cx, cy - half, cx + half, cy, thickness, color);
        Im.DrawLine(cx + half, cy, cx, cy + half, thickness, color);
        Im.DrawLine(cx, cy + half, cx - half, cy, thickness, color);
        Im.DrawLine(cx - half, cy, cx, cy - half, thickness, color);
    }

    public static void DrawFilledDiamond(float cx, float cy, float size, uint color)
    {
        const float invSqrt2 = 0.70710678f;
        float side = size * invSqrt2;
        float x = cx - side * 0.5f;
        float y = cy - side * 0.5f;
        Im.DrawRoundedRect(x, y, side, side, radius: 0f, color, rotation: MathF.PI * 0.25f);
    }

    public static void DrawLinkIcon(ImRect rect, uint color, float thickness = 1.5f)
    {
        float cx = rect.X + rect.Width * 0.5f;
        float cy = rect.Y + rect.Height * 0.5f;
        float radius = Math.Max(5f, rect.Width * 0.18f);
        float gap = 2f;

        Im.DrawCircle(cx - radius - gap, cy, radius, color);
        Im.DrawCircle(cx + radius + gap, cy, radius, color);
        Im.DrawLine(cx - gap, cy, cx + gap, cy, thickness, color);
    }

    public static void DrawUnlinkIcon(ImRect rect, uint color, float thickness = 1.5f)
    {
        float cx = rect.X + rect.Width * 0.5f;
        float cy = rect.Y + rect.Height * 0.5f;
        float radius = Math.Max(5f, rect.Width * 0.18f);
        float gap = 2f;

        Im.DrawCircle(cx - radius - gap, cy, radius, color);
        Im.DrawCircle(cx + radius + gap, cy, radius, color);

        float x0 = cx - gap - 3f;
        float x1 = cx + gap + 3f;
        float y0 = cy - 3f;
        float y1 = cy + 3f;
        Im.DrawLine(x0, y0, x1, y1, thickness, color);
    }
}
