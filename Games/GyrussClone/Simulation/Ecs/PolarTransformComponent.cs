using DerpLib.Ecs;
using FixedMath;

namespace GyrussClone.Simulation.Ecs;

public struct PolarTransformComponent : ISimComponent
{
    public Fixed64 Angle;
    public Fixed64 Radius;
    public Fixed64 AngularSpeed;
    public Fixed64 RadialSpeed;
}
