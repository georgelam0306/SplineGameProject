using System.Numerics;

namespace DerpLib.Rendering;

public readonly struct Ray3D
{
    public readonly Vector3 Origin;
    public readonly Vector3 Direction;

    public Ray3D(Vector3 origin, Vector3 direction)
    {
        Origin = origin;
        Direction = direction;
    }
}
