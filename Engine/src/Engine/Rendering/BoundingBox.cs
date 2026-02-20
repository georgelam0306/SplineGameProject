using System.Numerics;

namespace DerpLib.Rendering;

public readonly struct BoundingBox
{
    public readonly Vector3 Min;
    public readonly Vector3 Max;

    public BoundingBox(Vector3 min, Vector3 max)
    {
        Min = min;
        Max = max;
    }
}
