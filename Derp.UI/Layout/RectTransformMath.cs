using System.Numerics;
using DerpLib.ImGui.Core;

namespace Derp.UI;

internal static class RectTransformMath
{
    public static ImRect GetRectFromPivot(Vector2 pivotPos, Vector2 pivot01, Vector2 size)
    {
        float width = size.X;
        float height = size.Y;
        float x = pivotPos.X - pivot01.X * width;
        float y = pivotPos.Y - pivot01.Y * height;
        return new ImRect(x, y, width, height);
    }

    public static Vector2 GetPivotPosFromRect(ImRect rect, Vector2 pivot01)
    {
        return new Vector2(
            rect.X + rect.Width * pivot01.X,
            rect.Y + rect.Height * pivot01.Y);
    }

    public static Vector2 ClampSize(Vector2 size, Vector2 min, Vector2 max)
    {
        float width = MathF.Max(0f, size.X);
        float height = MathF.Max(0f, size.Y);

        if (min.X > 0f)
        {
            width = MathF.Max(width, min.X);
        }
        if (min.Y > 0f)
        {
            height = MathF.Max(height, min.Y);
        }

        if (max.X > 0f)
        {
            width = MathF.Min(width, max.X);
        }
        if (max.Y > 0f)
        {
            height = MathF.Min(height, max.Y);
        }

        return new Vector2(width, height);
    }

    public static ImRect Inset(ImRect rect, in Insets insets)
    {
        float x = rect.X + insets.Left;
        float y = rect.Y + insets.Top;
        float width = rect.Width - insets.Left - insets.Right;
        float height = rect.Height - insets.Top - insets.Bottom;

        if (width < 0f)
        {
            width = 0f;
        }
        if (height < 0f)
        {
            height = 0f;
        }

        return new ImRect(x, y, width, height);
    }
}

