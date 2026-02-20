using DerpLib.Ecs;
using FixedMath;

namespace DerpTanks.Simulation.Ecs;

public struct ProjectileComponent : ISimComponent
{
    public Fixed64 TimeToLive;
    public Fixed64 ContactRadius;
    public Fixed64 ExplosionRadius;
    public int Damage;
}

