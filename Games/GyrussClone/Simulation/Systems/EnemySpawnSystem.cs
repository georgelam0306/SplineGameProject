using DerpLib.Ecs;
using GyrussClone.Simulation.Ecs;
using FixedMath;

namespace GyrussClone.Simulation.Systems;

public sealed class EnemySpawnSystem : IEcsSystem<SimEcsWorld>
{
    private static readonly Fixed64 SpawnRadius = Fixed64.FromInt(30);
    private static readonly Fixed64 MinAngularSpeed = Fixed64.FromFloat(0.5f);
    private static readonly Fixed64 MaxAngularSpeed = Fixed64.FromFloat(2.0f);

    public void Update(SimEcsWorld world)
    {
        // Start a new wave if no enemies remain to spawn and no enemies alive
        if (world.WaveSpawnRemaining <= 0 && world.Enemy.Count == 0 && world.CurrentFrame >= world.NextWaveFrame)
        {
            world.CurrentWave++;
            world.WaveSpawnRemaining = 8 + 4 * world.CurrentWave;
            world.NextWaveFrame = world.CurrentFrame + 120; // 2 seconds between waves
        }

        // Spawn enemies at a steady rate (one every 10 frames)
        if (world.WaveSpawnRemaining <= 0)
        {
            return;
        }

        if (world.CurrentFrame % 10 != 0)
        {
            return;
        }

        if (world.Enemy.Count >= world.Enemy.Capacity)
        {
            return;
        }

        if (!world.Enemy.TryQueueSpawn(out var spawn))
        {
            return;
        }

        int spawnSlot = world.CurrentWave * 1000 + world.WaveSpawnRemaining;

        ref var transform = ref spawn.PolarTransform;
        ref var enemy = ref spawn.Enemy;

        // Random angle
        transform.Angle = DeterministicRandom.AngleWithSeed(world.SessionSeed, world.CurrentFrame, spawnSlot, 1);

        // Start near center
        transform.Radius = SpawnRadius;

        // Random angular speed with random direction
        Fixed64 speed = DeterministicRandom.RangeWithSeed(world.SessionSeed, world.CurrentFrame, spawnSlot, 2, MinAngularSpeed, MaxAngularSpeed);
        int dir = DeterministicRandom.SignWithSeed(world.SessionSeed, world.CurrentFrame, spawnSlot, 3);
        transform.AngularSpeed = speed * Fixed64.FromInt(dir);

        // Spiral outward
        transform.RadialSpeed = world.EnemyBaseRadialSpeed;

        enemy.Health = 1;
        enemy.EnemyType = DeterministicRandom.RangeWithSeed(world.SessionSeed, world.CurrentFrame, spawnSlot, 4, 0, 3);

        world.WaveSpawnRemaining--;
    }
}
