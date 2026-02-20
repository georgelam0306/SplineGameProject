using DerpLib.Ecs;
using GyrussClone.Simulation.Ecs;
using FixedMath;

namespace GyrussClone.Simulation.Systems;

public sealed class BulletMovementSystem : IEcsSystem<SimEcsWorld>
{
    private static readonly Fixed64 MinRadius = Fixed64.FromInt(10);

    public void Update(SimEcsWorld world)
    {
        for (int row = 0; row < world.PlayerBullet.Count; row++)
        {
            ref var transform = ref world.PlayerBullet.PolarTransform(row);
            ref var bullet = ref world.PlayerBullet.Bullet(row);

            transform.Radius += transform.RadialSpeed * world.DeltaTime;
            bullet.TimeToLive -= world.DeltaTime;

            // Destroy if reached center or timed out
            if (transform.Radius < MinRadius || bullet.TimeToLive <= Fixed64.Zero)
            {
                world.PlayerBullet.QueueDestroy(world.PlayerBullet.Entity(row));
            }
        }
    }
}
