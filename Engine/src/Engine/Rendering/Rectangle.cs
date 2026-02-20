namespace DerpLib.Rendering;

/// <summary>
/// A rectangle defined by position and size.
/// </summary>
public readonly struct Rectangle
{
    public readonly float X;
    public readonly float Y;
    public readonly float Width;
    public readonly float Height;

    public Rectangle(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }
}
