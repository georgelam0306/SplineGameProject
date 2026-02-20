using DerpLib.Ecs;
using DerpTanks.Simulation.Ecs;
using DerpTanks.Simulation.Services;
using FixedMath;

namespace DerpTanks.Simulation.Systems;

public sealed class ProjectileFireSystem : IEcsSystem<SimEcsWorld>
{
    private static readonly Fixed64 MuzzleOffset = Fixed64.FromFloat(1.2f);

    public void Update(SimEcsWorld world)
    {
        if (!world.FireRequested)
        {
            return;
        }

        world.FireRequested = false;

        if (world.CurrentFrame < world.NextFireFrame)
        {
            return;
        }

        if (world.Projectile.Count >= world.Projectile.Capacity)
        {
            return;
        }

        Fixed64Vec2 dir = world.PlayerForward;
        if (dir == Fixed64Vec2.Zero)
        {
            dir = new Fixed64Vec2(Fixed64.Zero, Fixed64.OneValue);
        }

        Fixed64Vec2 spawnPos = new Fixed64Vec2(
            world.PlayerPosition.X + dir.X * MuzzleOffset,
            world.PlayerPosition.Y + dir.Y * MuzzleOffset);

        Fixed64 min = Fixed64.FromInt(TankWorldProvider.ArenaMin);
        Fixed64 max = Fixed64.FromInt(TankWorldProvider.ArenaMax);
        spawnPos = Fixed64Vec2.Clamp(spawnPos, new Fixed64Vec2(min, min), new Fixed64Vec2(max, max));

        if (!world.Projectile.TryQueueSpawn(out var spawn))
        {
            return;
        }

        ref var transform = ref spawn.ProjectileTransform;
        ref var projectile = ref spawn.Projectile;

        transform.Position = spawnPos;
        transform.Velocity = new Fixed64Vec2(dir.X * world.ProjectileSpeed, dir.Y * world.ProjectileSpeed);

        projectile.TimeToLive = world.ProjectileLifetime;
        projectile.ContactRadius = world.ProjectileContactRadius;
        projectile.ExplosionRadius = world.ProjectileExplosionRadius;
        projectile.Damage = world.ProjectileDamage;

        world.DebugLastShotFrame = world.CurrentFrame;
        world.DebugLastShotRawId = 0;
        world.DebugLastShotPosition = spawnPos;
        world.DebugLastShotDirection = dir;

        world.NextFireFrame = world.CurrentFrame + world.FireCooldownFrames;
    }
}
