using DerpLib.Ecs;
using GyrussClone.Simulation.Ecs;
using FixedMath;

namespace GyrussClone.Simulation.Systems;

public sealed class CollisionSystem : IEcsSystem<SimEcsWorld>
{
    public void Update(SimEcsWorld world)
    {
        // Bullet <-> Enemy collision
        for (int b = 0; b < world.PlayerBullet.Count; b++)
        {
            ref var bulletTransform = ref world.PlayerBullet.PolarTransform(b);

            Fixed64 bx = bulletTransform.Radius * Fixed64.CosLUT(bulletTransform.Angle);
            Fixed64 by = bulletTransform.Radius * Fixed64.SinLUT(bulletTransform.Angle);

            for (int e = 0; e < world.Enemy.Count; e++)
            {
                ref var enemyTransform = ref world.Enemy.PolarTransform(e);
                ref var enemy = ref world.Enemy.Enemy(e);

                if (enemy.Health <= 0)
                {
                    continue;
                }

                Fixed64 ex = enemyTransform.Radius * Fixed64.CosLUT(enemyTransform.Angle);
                Fixed64 ey = enemyTransform.Radius * Fixed64.SinLUT(enemyTransform.Angle);

                Fixed64 dx = bx - ex;
                Fixed64 dy = by - ey;
                Fixed64 distSq = dx * dx + dy * dy;
                Fixed64 threshold = world.CollisionRadius * world.CollisionRadius;

                if (distSq < threshold)
                {
                    enemy.Health = 0;
                    world.PlayerBullet.QueueDestroy(world.PlayerBullet.Entity(b));
                    world.Score += 100;
                    break;
                }
            }
        }

        // Enemy <-> Player collision
        if (!world.PlayerAlive)
        {
            return;
        }

        Fixed64 px = world.PlayerRadius * Fixed64.CosLUT(world.PlayerAngle);
        Fixed64 py = world.PlayerRadius * Fixed64.SinLUT(world.PlayerAngle);
        Fixed64 playerThreshold = world.PlayerCollisionRadius * world.PlayerCollisionRadius;

        for (int e = 0; e < world.Enemy.Count; e++)
        {
            ref var enemyTransform = ref world.Enemy.PolarTransform(e);
            ref var enemy = ref world.Enemy.Enemy(e);

            if (enemy.Health <= 0)
            {
                continue;
            }

            Fixed64 ex = enemyTransform.Radius * Fixed64.CosLUT(enemyTransform.Angle);
            Fixed64 ey = enemyTransform.Radius * Fixed64.SinLUT(enemyTransform.Angle);

            Fixed64 dx = px - ex;
            Fixed64 dy = py - ey;
            Fixed64 distSq = dx * dx + dy * dy;

            if (distSq < playerThreshold)
            {
                enemy.Health = 0;
                world.Lives--;

                if (world.Lives <= 0)
                {
                    world.PlayerAlive = false;
                }

                break;
            }
        }
    }
}
