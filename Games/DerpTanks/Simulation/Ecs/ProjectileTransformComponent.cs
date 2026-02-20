using DerpLib.Ecs;
using FixedMath;

namespace DerpTanks.Simulation.Ecs;

public struct ProjectileTransformComponent : ISimComponent
{
    public Fixed64Vec2 Position;
    public Fixed64Vec2 Velocity;
}

