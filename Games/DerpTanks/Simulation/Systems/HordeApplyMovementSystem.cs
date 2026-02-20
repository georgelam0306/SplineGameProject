using DerpLib.Ecs;
using DerpTanks.Simulation.Ecs;
using DerpTanks.Simulation.Services;
using FixedMath;

namespace DerpTanks.Simulation.Systems;

public sealed class HordeApplyMovementSystem : IEcsSystem<SimEcsWorld>
{
    public void Update(SimEcsWorld world)
    {
        Fixed64 dt = world.DeltaTime;

        Fixed64 min = Fixed64.FromInt(TankWorldProvider.ArenaMin);
        Fixed64 max = Fixed64.FromInt(TankWorldProvider.ArenaMax);

        for (int row = 0; row < world.Horde.Count; row++)
        {
            ref var transform = ref world.Horde.Transform(row);
            Fixed64Vec2 nextPos = transform.Position + transform.Velocity * dt;

            nextPos = Fixed64Vec2.Clamp(nextPos, new Fixed64Vec2(min, min), new Fixed64Vec2(max, max));

            transform.Position = nextPos;
        }

        world.Horde.RebuildSpatialIndex();
    }
}

