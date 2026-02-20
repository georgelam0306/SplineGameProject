using DerpLib.Ecs;
using DerpTanks.Simulation.Ecs;
using DerpTanks.Simulation.Services;
using FixedMath;

namespace DerpTanks.Simulation.Systems;

public sealed class ProjectileMovementSystem : IEcsSystem<SimEcsWorld>
{
    public void Update(SimEcsWorld world)
    {
        Fixed64 dt = world.DeltaTime;
        Fixed64 min = Fixed64.FromInt(TankWorldProvider.ArenaMin);
        Fixed64 max = Fixed64.FromInt(TankWorldProvider.ArenaMax);

        for (int rowIndex = 0; rowIndex < world.Projectile.Count; rowIndex++)
        {
            ref var transform = ref world.Projectile.ProjectileTransform(rowIndex);
            ref var projectile = ref world.Projectile.Projectile(rowIndex);

            projectile.TimeToLive -= dt;
            if (projectile.TimeToLive.Raw <= 0)
            {
                world.Projectile.QueueDestroy(world.Projectile.Entity(rowIndex));
                continue;
            }

            transform.Position += transform.Velocity * dt;
            transform.Position = Fixed64Vec2.Clamp(transform.Position, new Fixed64Vec2(min, min), new Fixed64Vec2(max, max));
        }
    }
}
