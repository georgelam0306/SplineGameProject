using DerpLib.Ecs;
using DerpTanks.Simulation.Ecs;

namespace DerpTanks.Simulation.Systems;

public sealed class HordeDeathSystem : IEcsSystem<SimEcsWorld>
{
    public void Update(SimEcsWorld world)
    {
        // Cleanup-only: remove entities whose health reached 0.
        for (int rowIndex = 0; rowIndex < world.Horde.Count; rowIndex++)
        {
            ref var combat = ref world.Horde.Combat(rowIndex);
            if (combat.Health > 0)
            {
                continue;
            }

            world.Horde.QueueDestroy(world.Horde.Entity(rowIndex));
        }
    }
}
