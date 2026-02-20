using System.Numerics;

namespace Derp.UI;

internal readonly struct Insets
{
    public readonly float Left;
    public readonly float Top;
    public readonly float Right;
    public readonly float Bottom;

    public Insets(float left, float top, float right, float bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    // Order: L, T, R, B.
    public static Insets FromVector4(Vector4 value)
    {
        return new Insets(value.X, value.Y, value.Z, value.W);
    }
}

