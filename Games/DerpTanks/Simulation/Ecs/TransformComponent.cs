using DerpLib.Ecs;
using FixedMath;

namespace DerpTanks.Simulation.Ecs;

public struct TransformComponent : ISimComponent
{
    public Fixed64Vec2 Position;
    public Fixed64Vec2 Velocity;
    public Fixed64Vec2 SmoothedSeparation;
}
