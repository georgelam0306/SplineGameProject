using DerpLib.Ecs;

namespace GyrussClone.Simulation.Ecs;

public struct EnemyComponent : ISimComponent
{
    public int Health;
    public int EnemyType;
}
