using DerpLib.Ecs;
using FixedMath;

namespace GyrussClone.Simulation.Ecs;

public struct BulletComponent : ISimComponent
{
    public Fixed64 TimeToLive;
}
