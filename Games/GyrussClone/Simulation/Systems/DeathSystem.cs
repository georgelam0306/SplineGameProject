using DerpLib.Ecs;
using GyrussClone.Simulation.Ecs;

namespace GyrussClone.Simulation.Systems;

public sealed class DeathSystem : IEcsSystem<SimEcsWorld>
{
    public void Update(SimEcsWorld world)
    {
        for (int row = 0; row < world.Enemy.Count; row++)
        {
            ref var enemy = ref world.Enemy.Enemy(row);

            if (enemy.Health <= 0)
            {
                world.Enemy.QueueDestroy(world.Enemy.Entity(row));
            }
        }
    }
}
