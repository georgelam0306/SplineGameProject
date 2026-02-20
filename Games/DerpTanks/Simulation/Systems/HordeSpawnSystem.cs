using DerpLib.Ecs;
using DerpTanks.Simulation.Ecs;
using DerpTanks.Simulation.Services;
using FixedMath;

namespace DerpTanks.Simulation.Systems;

public sealed class HordeSpawnSystem : IEcsSystem<SimEcsWorld>
{
    private const int FramesPerSecond = 60;
    private const int FramesBetweenWaves = 4 * FramesPerSecond;

    private const int SpawnBatchPerFrame = 128;

    private const int BaseWaveSize = 2000;
    private const int WaveSizeGrowth = 500;

    private const int SaltEdge = 101;
    private const int SaltOffset = 102;
    private const int SaltDepth = 103;
    private const int SaltAngle = 104;
    private const int SaltRadius = 105;

    public void Update(SimEcsWorld world)
    {
        if (world.WaveSpawnRemaining <= 0)
        {
            if (world.CurrentFrame >= world.NextWaveFrame)
            {
                world.CurrentWave++;
                int targetWaveSize = BaseWaveSize + world.CurrentWave * WaveSizeGrowth;
                int available = world.Horde.Capacity - world.Horde.Count;
                world.WaveSpawnRemaining = targetWaveSize <= available ? targetWaveSize : available;
                world.NextWaveFrame = world.CurrentFrame + FramesBetweenWaves;
            }

            return;
        }

        int spawnCount = world.WaveSpawnRemaining;
        if (spawnCount > SpawnBatchPerFrame)
        {
            spawnCount = SpawnBatchPerFrame;
        }

        int baseCount = world.Horde.Count;
        for (int i = 0; i < spawnCount; i++)
        {
            int spawnIndex = baseCount + i;

            if (!world.Horde.TryQueueSpawn(out var spawn))
            {
                world.WaveSpawnRemaining = 0;
                break;
            }

            ref var transform = ref spawn.Transform;
            ref var combat = ref spawn.Combat;

            // Make wave 1 immediately visible: spawn near player.
            // Later waves spawn from the arena edges.
            if (world.CurrentWave == 1)
            {
                Fixed64 angle = DeterministicRandom.AngleWithSeed(world.SessionSeed, world.CurrentFrame, spawnIndex, SaltAngle);
                Fixed64.SinCosLUT(angle, out Fixed64 sin, out Fixed64 cos);
                int radiusInt = DeterministicRandom.RangeWithSeed(world.SessionSeed, world.CurrentFrame, spawnIndex, SaltRadius, 8, 20);
                Fixed64 radius = Fixed64.FromInt(radiusInt);
                transform.Position = new Fixed64Vec2(
                    world.PlayerPosition.X + cos * radius,
                    world.PlayerPosition.Y + sin * radius);
            }
            else
            {
                byte edge = (byte)DeterministicRandom.RangeWithSeed(world.SessionSeed, world.CurrentFrame, spawnIndex, SaltEdge, 0, 4);
                int offset = DeterministicRandom.RangeWithSeed(world.SessionSeed, world.CurrentFrame, spawnIndex, SaltOffset, TankWorldProvider.ArenaMin, TankWorldProvider.ArenaMax + 1);
                int depth = DeterministicRandom.RangeWithSeed(world.SessionSeed, world.CurrentFrame, spawnIndex, SaltDepth, 5, 40);

                int x;
                int y;

                // Spawn slightly outside arena bounds so flow pulls them inward.
                if (edge == 0)
                {
                    x = TankWorldProvider.ArenaMin - depth;
                    y = offset;
                }
                else if (edge == 1)
                {
                    x = TankWorldProvider.ArenaMax + depth;
                    y = offset;
                }
                else if (edge == 2)
                {
                    x = offset;
                    y = TankWorldProvider.ArenaMin - depth;
                }
                else
                {
                    x = offset;
                    y = TankWorldProvider.ArenaMax + depth;
                }

                transform.Position = Fixed64Vec2.FromInt(x, y);
            }
            transform.Velocity = Fixed64Vec2.Zero;
            transform.SmoothedSeparation = Fixed64Vec2.Zero;

            combat.Health = 3;
            combat.MoveSpeed = Fixed64.FromInt(1);

            world.WaveSpawnRemaining--;
        }
    }
}
