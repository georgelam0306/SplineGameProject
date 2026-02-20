using DerpLib.Ecs;
using GyrussClone.Simulation.Ecs;
using FixedMath;

namespace GyrussClone.Simulation.Systems;

public sealed class PlayerFireSystem : IEcsSystem<SimEcsWorld>
{
    public void Update(SimEcsWorld world)
    {
        if (!world.FireRequested)
        {
            return;
        }

        world.FireRequested = false;

        if (!world.PlayerAlive)
        {
            return;
        }

        if (world.CurrentFrame < world.NextFireFrame)
        {
            return;
        }

        if (world.PlayerBullet.Count >= world.PlayerBullet.Capacity)
        {
            return;
        }

        if (!world.PlayerBullet.TryQueueSpawn(out var spawn))
        {
            return;
        }

        ref var transform = ref spawn.PolarTransform;
        ref var bullet = ref spawn.Bullet;

        transform.Angle = world.PlayerAngle;
        transform.Radius = world.PlayerRadius;
        transform.AngularSpeed = Fixed64.Zero;
        transform.RadialSpeed = -world.BulletRadialSpeed; // Negative = inward

        bullet.TimeToLive = world.BulletLifetime;

        world.NextFireFrame = world.CurrentFrame + world.FireCooldownFrames;
    }
}
