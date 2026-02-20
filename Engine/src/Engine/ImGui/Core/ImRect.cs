using System.Numerics;

namespace DerpLib.ImGui.Core;

/// <summary>
/// Rectangle struct for UI bounds, layout, and hit-testing.
/// Uses left/top/width/height representation.
/// </summary>
public struct ImRect
{
    public float X;
    public float Y;
    public float Width;
    public float Height;

    public ImRect(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    //=== Properties ===

    public readonly float Left => X;
    public readonly float Top => Y;
    public readonly float Right => X + Width;
    public readonly float Bottom => Y + Height;

    public readonly Vector2 Position => new(X, Y);
    public readonly Vector2 Size => new(Width, Height);
    public readonly Vector2 Center => new(X + Width * 0.5f, Y + Height * 0.5f);

    public readonly Vector2 TopLeft => new(X, Y);
    public readonly Vector2 TopRight => new(Right, Y);
    public readonly Vector2 BottomLeft => new(X, Bottom);
    public readonly Vector2 BottomRight => new(Right, Bottom);

    public readonly bool IsEmpty => Width <= 0 || Height <= 0;

    public static ImRect Zero => default;

    //=== Factory Methods ===

    public static ImRect FromPositionSize(Vector2 position, Vector2 size)
        => new(position.X, position.Y, size.X, size.Y);

    public static ImRect FromPositionSize(float x, float y, float width, float height)
        => new(x, y, width, height);

    public static ImRect FromMinMax(Vector2 min, Vector2 max)
        => new(min.X, min.Y, max.X - min.X, max.Y - min.Y);

    public static ImRect FromMinMax(float minX, float minY, float maxX, float maxY)
        => new(minX, minY, maxX - minX, maxY - minY);

    public static ImRect FromCenter(Vector2 center, Vector2 size)
        => new(center.X - size.X * 0.5f, center.Y - size.Y * 0.5f, size.X, size.Y);

    public static ImRect FromCenter(float cx, float cy, float width, float height)
        => new(cx - width * 0.5f, cy - height * 0.5f, width, height);

    //=== Hit Testing ===

    /// <summary>
    /// Returns true if point is inside rect. Inclusive left/top, exclusive right/bottom.
    /// </summary>
    public readonly bool Contains(Vector2 point)
        => point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;

    /// <summary>
    /// Returns true if point is inside rect. Inclusive left/top, exclusive right/bottom.
    /// </summary>
    public readonly bool Contains(float px, float py)
        => px >= X && px < Right && py >= Y && py < Bottom;

    /// <summary>
    /// Returns true if the other rect is fully inside this rect.
    /// Inclusive left/top, exclusive right/bottom.
    /// </summary>
    public readonly bool Contains(ImRect other)
        => other.X >= X && other.Right <= Right && other.Y >= Y && other.Bottom <= Bottom;

    /// <summary>
    /// Returns true if this rect overlaps with another.
    /// </summary>
    public readonly bool Overlaps(ImRect other)
        => X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;

    //=== Geometry Operations ===

    /// <summary>
    /// Returns intersection of two rects. Returns Zero if no overlap.
    /// </summary>
    public readonly ImRect Intersect(ImRect other)
    {
        float x1 = MathF.Max(X, other.X);
        float y1 = MathF.Max(Y, other.Y);
        float x2 = MathF.Min(Right, other.Right);
        float y2 = MathF.Min(Bottom, other.Bottom);

        if (x2 <= x1 || y2 <= y1)
            return Zero;

        return new ImRect(x1, y1, x2 - x1, y2 - y1);
    }

    /// <summary>
    /// Expand rect by amount on all sides.
    /// </summary>
    public readonly ImRect Expand(float amount)
        => new(X - amount, Y - amount, Width + amount * 2, Height + amount * 2);

    /// <summary>
    /// Expand rect by different amounts horizontally and vertically.
    /// </summary>
    public readonly ImRect Expand(float horizontal, float vertical)
        => new(X - horizontal, Y - vertical, Width + horizontal * 2, Height + vertical * 2);

    /// <summary>
    /// Shrink rect by amount on all sides (negative expand).
    /// </summary>
    public readonly ImRect Shrink(float amount)
        => Expand(-amount);

    /// <summary>
    /// Shrink rect by different amounts horizontally and vertically.
    /// </summary>
    public readonly ImRect Shrink(float horizontal, float vertical)
        => Expand(-horizontal, -vertical);

    /// <summary>
    /// Offset rect by delta.
    /// </summary>
    public readonly ImRect Offset(Vector2 delta)
        => new(X + delta.X, Y + delta.Y, Width, Height);

    /// <summary>
    /// Offset rect by delta.
    /// </summary>
    public readonly ImRect Offset(float dx, float dy)
        => new(X + dx, Y + dy, Width, Height);

    //=== Split Operations (for layout) ===

    /// <summary>
    /// Split off a rect from the top. Returns (top portion, remainder).
    /// </summary>
    public readonly (ImRect top, ImRect remainder) SplitTop(float height)
    {
        height = MathF.Min(height, Height);
        var top = new ImRect(X, Y, Width, height);
        var remainder = new ImRect(X, Y + height, Width, Height - height);
        return (top, remainder);
    }

    /// <summary>
    /// Split off a rect from the bottom. Returns (bottom portion, remainder).
    /// </summary>
    public readonly (ImRect bottom, ImRect remainder) SplitBottom(float height)
    {
        height = MathF.Min(height, Height);
        var bottom = new ImRect(X, Bottom - height, Width, height);
        var remainder = new ImRect(X, Y, Width, Height - height);
        return (bottom, remainder);
    }

    /// <summary>
    /// Split off a rect from the left. Returns (left portion, remainder).
    /// </summary>
    public readonly (ImRect left, ImRect remainder) SplitLeft(float width)
    {
        width = MathF.Min(width, Width);
        var left = new ImRect(X, Y, width, Height);
        var remainder = new ImRect(X + width, Y, Width - width, Height);
        return (left, remainder);
    }

    /// <summary>
    /// Split off a rect from the right. Returns (right portion, remainder).
    /// </summary>
    public readonly (ImRect right, ImRect remainder) SplitRight(float width)
    {
        width = MathF.Min(width, Width);
        var right = new ImRect(Right - width, Y, width, Height);
        var remainder = new ImRect(X, Y, Width - width, Height);
        return (right, remainder);
    }

    /// <summary>
    /// Split horizontally at ratio (0-1). Returns (left, right).
    /// </summary>
    public readonly (ImRect left, ImRect right) SplitHorizontal(float ratio)
    {
        ratio = Math.Clamp(ratio, 0f, 1f);
        float splitWidth = Width * ratio;
        var left = new ImRect(X, Y, splitWidth, Height);
        var right = new ImRect(X + splitWidth, Y, Width - splitWidth, Height);
        return (left, right);
    }

    /// <summary>
    /// Split vertically at ratio (0-1). Returns (top, bottom).
    /// </summary>
    public readonly (ImRect top, ImRect bottom) SplitVertical(float ratio)
    {
        ratio = Math.Clamp(ratio, 0f, 1f);
        float splitHeight = Height * ratio;
        var top = new ImRect(X, Y, Width, splitHeight);
        var bottom = new ImRect(X, Y + splitHeight, Width, Height - splitHeight);
        return (top, bottom);
    }

    //=== Padding ===

    /// <summary>
    /// Apply uniform padding (shrink from all sides).
    /// </summary>
    public readonly ImRect Pad(float padding)
        => Shrink(padding);

    /// <summary>
    /// Apply padding with different values for each side.
    /// </summary>
    public readonly ImRect Pad(float left, float top, float right, float bottom)
        => new(X + left, Y + top, Width - left - right, Height - top - bottom);

    //=== Clamping ===

    /// <summary>
    /// Clamp a point to be within this rect.
    /// </summary>
    public readonly Vector2 ClampPoint(Vector2 point)
    {
        return new Vector2(
            Math.Clamp(point.X, X, Right - 1),
            Math.Clamp(point.Y, Y, Bottom - 1)
        );
    }

    /// <summary>
    /// Clamp this rect to be within bounds (keeps size if possible).
    /// </summary>
    public readonly ImRect ClampTo(ImRect bounds)
    {
        float newX = Math.Clamp(X, bounds.X, bounds.Right - Width);
        float newY = Math.Clamp(Y, bounds.Y, bounds.Bottom - Height);
        return new ImRect(newX, newY, Width, Height);
    }

    //=== Equality ===

    public readonly bool Equals(ImRect other)
        => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;

    public override readonly bool Equals(object? obj)
        => obj is ImRect other && Equals(other);

    public override readonly int GetHashCode()
        => HashCode.Combine(X, Y, Width, Height);

    public static bool operator ==(ImRect left, ImRect right) => left.Equals(right);
    public static bool operator !=(ImRect left, ImRect right) => !left.Equals(right);

    public override readonly string ToString()
        => $"ImRect({X}, {Y}, {Width}, {Height})";
}
