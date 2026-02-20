using DerpLib.Ecs;
using DerpTanks.Simulation.Ecs;
using FixedMath;

namespace DerpTanks.Simulation.Systems;

public sealed class ProjectileHitSystem : IEcsSystem<SimEcsWorld>
{
    private const int MaxQueryResults = 256;

    public void Update(SimEcsWorld world)
    {
        Span<EntityHandle> buffer = world.QueryBuffer;
        if (buffer.Length <= 0)
        {
            return;
        }

        if (buffer.Length > MaxQueryResults)
        {
            buffer = buffer.Slice(0, MaxQueryResults);
        }

        for (int projectileRow = 0; projectileRow < world.Projectile.Count; projectileRow++)
        {
            ref var transform = ref world.Projectile.ProjectileTransform(projectileRow);
            ref var projectile = ref world.Projectile.Projectile(projectileRow);

            int hitCount = world.Horde.QueryRadius(transform.Position, projectile.ContactRadius, buffer);
            if (hitCount <= 0)
            {
                continue;
            }

            ApplyExplosionDamage(world, transform.Position, projectile.ExplosionRadius, projectile.Damage, buffer);
            world.Projectile.QueueDestroy(world.Projectile.Entity(projectileRow));
        }
    }

    private static void ApplyExplosionDamage(SimEcsWorld world, Fixed64Vec2 center, Fixed64 radius, int damage, Span<EntityHandle> buffer)
    {
        int hitCount = world.Horde.QueryRadius(center, radius, buffer);
        for (int i = 0; i < hitCount; i++)
        {
            EntityHandle enemy = buffer[i];
            if (!world.Horde.TryGetRow(enemy, out int row))
            {
                continue;
            }

            ref var combat = ref world.Horde.Combat(row);
            combat.Health -= damage;
        }
    }
}
