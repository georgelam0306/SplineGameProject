using Catrillion.Config;
using Catrillion.Core;
using Catrillion.Entities;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Services;
using Core;
using SimTable;

namespace Catrillion.Simulation.Systems.SimTable;

/// <summary>
/// Spawns enemy zombies when the game starts, using tier-based distribution.
/// Stronger zombie types spawn in outer zones, with more zombies in outer tiers.
/// </summary>
public sealed class EnemySpawnSystem : SimTableSystem
{
    private readonly GameDataManager<GameDocDb> _gameData;
    private readonly ZoneTierService _zoneTierService;
    private readonly TerrainDataService _terrainData;
    private readonly ZombieSpawnerService _zombieSpawner;
    private bool _hasSpawned;

    // Configuration (loaded from game data)
    private readonly int _mapWidth;
    private readonly int _mapHeight;
    private readonly int _tileSize;

    // Deterministic random salts
    private const int SaltX = 0x5A4F4D58;       // "ZOMX"
    private const int SaltY = 0x5A4F4D59;       // "ZOMY"

    public EnemySpawnSystem(
        SimWorld world,
        GameDataManager<GameDocDb> gameData,
        ZoneTierService zoneTierService,
        TerrainDataService terrainData,
        ZombieSpawnerService zombieSpawner) : base(world)
    {
        _gameData = gameData;
        _zoneTierService = zoneTierService;
        _terrainData = terrainData;
        _zombieSpawner = zombieSpawner;

        // Load map config from GameDocDb
        ref readonly var mapConfig = ref gameData.Db.MapConfigData.FindById(0);
        _mapWidth = mapConfig.WidthTiles * mapConfig.TileSize;
        _mapHeight = mapConfig.HeightTiles * mapConfig.TileSize;
        _tileSize = mapConfig.TileSize;
    }

    public override void Tick(in SimulationContext context)
    {
        // Don't spawn during countdown - wait until game starts
        if (!World.IsPlaying()) return;

        if (_hasSpawned) return;
        _hasSpawned = true;

        // Skip placed enemies if disabled for debugging
        if (GameConfig.Debug.DisablePlacedEnemies) return;

        // Wait for terrain to be generated
        if (!_terrainData.IsGenerated) return;

        SpawnZombiesProcedural(context);
    }

    private void SpawnZombiesProcedural(in SimulationContext context)
    {
        ref readonly var mapGenConfig = ref _gameData.Db.MapGenConfigData.FindById(0);
        int totalZombieCount = mapGenConfig.TotalZombieCount;
        int maxAttempts = mapGenConfig.ZombiePlacementAttempts;

        // Calculate per-tier budgets based on density multipliers
        // Tier 0 = safe zone (0 zombies), Tier 1-3 have increasing density
        var tierBudgets = CalculateTierBudgets(totalZombieCount);
        var tierSpawned = new int[_zoneTierService.TierCount];

        int spawnedCount = 0;
        int attempt = 0;

        while (spawnedCount < totalZombieCount && attempt < maxAttempts)
        {
            // Generate random position
            int x = DeterministicRandom.RangeWithSeed(
                context.SessionSeed, context.CurrentFrame, attempt, SaltX,
                0, _mapWidth);
            int y = DeterministicRandom.RangeWithSeed(
                context.SessionSeed, context.CurrentFrame, attempt, SaltY,
                0, _mapHeight);

            attempt++;

            // Get tier for this position
            int tier = _zoneTierService.GetTierAt(Fixed64.FromInt(x), Fixed64.FromInt(y));

            // Skip safe zone or if tier budget exhausted
            if (tier == 0) continue;
            if (tierSpawned[tier] >= tierBudgets[tier]) continue;

            // Check terrain passability
            int tileX = x / _tileSize;
            int tileY = y / _tileSize;
            if (!_terrainData.IsPassable(tileX, tileY)) continue;

            // Select zombie type using weighted random from allowed types
            int allowedMask = _zoneTierService.GetAllowedTypesMask(tier);
            ZombieTypeId typeId = _zombieSpawner.SelectWeightedZombieType(context, attempt, allowedMask);

            // Spawn zombie using service
            _zombieSpawner.SpawnZombie(context, x, y, typeId, ZombieState.Idle, spawnedCount);

            tierSpawned[tier]++;
            spawnedCount++;
        }
    }

    private int[] CalculateTierBudgets(int totalCount)
    {
        var budgets = new int[_zoneTierService.TierCount];
        Fixed64 totalWeight = Fixed64.Zero;

        // Calculate total weight from density multipliers
        for (int tier = 1; tier < _zoneTierService.TierCount; tier++)
        {
            totalWeight += _zoneTierService.GetZombieDensityMultiplier(tier);
        }

        if (totalWeight <= Fixed64.Zero)
        {
            // Fallback: distribute evenly if no weights
            int perTier = totalCount / (_zoneTierService.TierCount - 1);
            for (int tier = 1; tier < _zoneTierService.TierCount; tier++)
            {
                budgets[tier] = perTier;
            }
            return budgets;
        }

        // Distribute based on density multipliers
        int remaining = totalCount;
        for (int tier = 1; tier < _zoneTierService.TierCount; tier++)
        {
            Fixed64 multiplier = _zoneTierService.GetZombieDensityMultiplier(tier);
            Fixed64 ratio = multiplier / totalWeight;
            int budget = (ratio * Fixed64.FromInt(totalCount)).ToInt();
            budgets[tier] = budget;
            remaining -= budget;
        }

        // Add remainder to last tier
        budgets[_zoneTierService.TierCount - 1] += remaining;

        return budgets;
    }
}
