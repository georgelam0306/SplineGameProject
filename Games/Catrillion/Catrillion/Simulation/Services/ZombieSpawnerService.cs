using Catrillion.Core;
using Catrillion.Entities;
using Catrillion.GameData.Schemas;
using Catrillion.Rendering.Components;
using Catrillion.Simulation.Components;
using Core;
using Raylib_cs;
using SimTable;

namespace Catrillion.Simulation.Services;

/// <summary>
/// Reusable service for spawning zombies.
/// Extracts spawn logic from EnemySpawnSystem for use by wave systems.
/// </summary>
public sealed class ZombieSpawnerService
{
    private readonly SimWorld _world;
    private readonly EntitySpawner _spawner;
    private readonly GameDataManager<GameDocDb> _gameData;

    // Cached spawn weights for each zombie type
    private readonly int[] _zombieSpawnWeights;
    private readonly int _zombieTypeCount;

    // Deterministic random salts
    private const int SaltType = 0x5A4F4D54;    // "ZOMT"

    // Data file timing values are designed for 60fps, scale to actual TickRate
    private const int DataAssumedTickRate = 60;

    public ZombieSpawnerService(
        SimWorld world,
        EntitySpawner spawner,
        GameDataManager<GameDocDb> gameData)
    {
        _world = world;
        _spawner = spawner;
        _gameData = gameData;

        // Cache spawn weights for all zombie types
        _zombieTypeCount = 5;  // Walker, Runner, Fatty, Spitter, Doom
        _zombieSpawnWeights = new int[_zombieTypeCount];
        for (int i = 0; i < _zombieTypeCount; i++)
        {
            ref readonly var typeData = ref gameData.Db.ZombieTypeData.FindById(i);
            _zombieSpawnWeights[i] = typeData.SpawnWeight;
        }
    }

    /// <summary>
    /// Selects a zombie type using weighted random from allowed types.
    /// </summary>
    /// <param name="context">Simulation context for deterministic random.</param>
    /// <param name="slot">Unique slot/index for deterministic seed.</param>
    /// <param name="allowedMask">Bitmask of allowed zombie types.</param>
    /// <returns>Selected zombie type ID.</returns>
    public ZombieTypeId SelectWeightedZombieType(in SimulationContext context, int slot, int allowedMask)
    {
        // Calculate total weight for allowed types
        int totalWeight = 0;
        for (int i = 0; i < _zombieTypeCount; i++)
        {
            if ((allowedMask & (1 << i)) != 0)
            {
                totalWeight += _zombieSpawnWeights[i];
            }
        }

        if (totalWeight == 0)
        {
            // Fallback to Aged if nothing allowed (shouldn't happen)
            return ZombieTypeId.Aged;
        }

        // Pick random value in range
        int roll = DeterministicRandom.RangeWithSeed(
            context.SessionSeed, context.CurrentFrame, slot, SaltType,
            0, totalWeight);

        // Find which type the roll corresponds to
        int cumulative = 0;
        for (int i = 0; i < _zombieTypeCount; i++)
        {
            if ((allowedMask & (1 << i)) == 0) continue;

            cumulative += _zombieSpawnWeights[i];
            if (roll < cumulative)
            {
                return (ZombieTypeId)i;
            }
        }

        // Fallback (shouldn't reach here)
        return ZombieTypeId.Aged;
    }

    /// <summary>
    /// Spawns a zombie at the specified position with given type.
    /// </summary>
    /// <param name="context">Simulation context.</param>
    /// <param name="position">World position to spawn at.</param>
    /// <param name="typeId">Zombie type to spawn.</param>
    /// <param name="initialState">Initial AI state (Idle, Chase, etc.).</param>
    /// <param name="slot">Unique slot for deterministic random seed.</param>
    /// <returns>Handle to the spawned zombie.</returns>
    public SimHandle SpawnZombie(
        in SimulationContext context,
        Fixed64Vec2 position,
        ZombieTypeId typeId,
        ZombieState initialState,
        int slot)
    {
        var zombies = _world.ZombieRows;

        // Use builder pattern - creates both SimWorld row AND ECS entity
        var handle = _spawner.Create<ZombieRow>()
            .WithRender(new SpriteRenderer
            {
                TexturePath = "Resources/Characters/cat.png",
                Width = 32,
                Height = 32,
                SourceX = 0,
                SourceY = 0,
                SourceWidth = 32,
                SourceHeight = 32,
                Color = Color.White,
                IsVisible = true
            })
            .Spawn();

        // Initialize SimWorld row data
        var row = zombies.GetRow(handle.SimHandle);

        // Transform
        row.Position = position;
        row.Velocity = Fixed64Vec2.Zero;
        row.FacingAngle = Fixed64.Zero;

        // Identity - must set before InitializeStats
        row.TypeId = typeId;

        // Initialize all [CachedStat] fields from ZombieTypeData
        zombies.InitializeStats(handle.SimHandle, _gameData.Db);

        // Scale timing values from data's assumed tick rate (60fps) to actual TickRate
        row.IdleDurationMin = row.IdleDurationMin * SimulationConfig.TickRate / DataAssumedTickRate;
        row.IdleDurationMax = row.IdleDurationMax * SimulationConfig.TickRate / DataAssumedTickRate;
        row.WanderDurationMin = row.WanderDurationMin * SimulationConfig.TickRate / DataAssumedTickRate;
        row.WanderDurationMax = row.WanderDurationMax * SimulationConfig.TickRate / DataAssumedTickRate;

        // Non-cached stats
        row.Health = row.MaxHealth;
        row.AttackSpeed = Fixed64.OneValue;

        // AI State - use provided initial state
        row.State = initialState;
        row.StateTimer = DeterministicRandom.RangeWithSeed(
            context.SessionSeed, context.CurrentFrame, slot, 0x494E4954,  // "INIT"
            SimulationConfig.TickRate * 2, SimulationConfig.TickRate * 6);  // Random idle timer 2-6 seconds
        row.WanderDirectionSeed = DeterministicRandom.RangeWithSeed(
            context.SessionSeed, context.CurrentFrame, slot, 0x57414E44,  // "WAND"
            0, 360);
        row.TargetHandle = SimHandle.Invalid;
        row.TargetType = 0;
        row.NoiseAttraction = 0;

        // Pathfinding
        row.ZoneId = 0;
        row.Flow = Fixed64Vec2.Zero;

        // State
        row.Flags = MortalFlags.IsActive;
        row.DeathFrame = 0;

        return handle.SimHandle;
    }

    /// <summary>
    /// Spawns a zombie using integer coordinates.
    /// </summary>
    public SimHandle SpawnZombie(
        in SimulationContext context,
        int x, int y,
        ZombieTypeId typeId,
        ZombieState initialState,
        int slot)
    {
        return SpawnZombie(context, Fixed64Vec2.FromInt(x, y), typeId, initialState, slot);
    }
}
