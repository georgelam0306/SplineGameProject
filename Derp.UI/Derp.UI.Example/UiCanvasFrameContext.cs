using System.Numerics;

namespace Derp.UI.Example;

public sealed class UiCanvasFrameContext
{
    public uint DeltaMicroseconds;
    public int WindowWidth;
    public int WindowHeight;

    public Vector2 MousePosition;
    public bool PrimaryDown;
    public float WheelDelta;
}

