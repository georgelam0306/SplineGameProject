using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Services;
using Core;

namespace Catrillion.Simulation.Systems.SimTable;

/// <summary>
/// Spawns resource nodes (Gold, Energy) during map generation.
/// More resources spawn in outer tiers based on resource density multiplier.
/// </summary>
public sealed class ResourceNodeSpawnSystem : SimTableSystem
{
    private readonly GameDataManager<GameDocDb> _gameData;
    private readonly ZoneTierService _zoneTierService;
    private readonly TerrainDataService _terrainData;
    private readonly ProceduralTerrainGenerator _generator;
    private bool _hasSpawned;

    // Configuration
    private readonly int _widthTiles;
    private readonly int _heightTiles;
    private readonly int _tileSize;

    // Deterministic random salts
    private const int SaltX = 0x52455358;  // "RESX"
    private const int SaltY = 0x52455359;  // "RESY"
    private const int SaltAmt = 0x52455341; // "RESA"

    public ResourceNodeSpawnSystem(
        SimWorld world,
        GameDataManager<GameDocDb> gameData,
        ZoneTierService zoneTierService,
        TerrainDataService terrainData) : base(world)
    {
        _gameData = gameData;
        _zoneTierService = zoneTierService;
        _terrainData = terrainData;
        _generator = new ProceduralTerrainGenerator(gameData);

        ref readonly var mapConfig = ref gameData.Db.MapConfigData.FindById(0);
        _widthTiles = mapConfig.WidthTiles;
        _heightTiles = mapConfig.HeightTiles;
        _tileSize = mapConfig.TileSize;
    }

    public override void Tick(in SimulationContext context)
    {
        if (!World.IsPlaying()) return;
        if (_hasSpawned) return;
        _hasSpawned = true;

        // Wait for terrain to be generated
        if (!_terrainData.IsGenerated) return;

        SpawnResourceNodes(context);
    }

    private void SpawnResourceNodes(in SimulationContext context)
    {
        ref readonly var mapGenConfig = ref _gameData.Db.MapGenConfigData.FindById(0);
        int goldPerZone = mapGenConfig.GoldDepositsPerZone;
        int stonePerZone = mapGenConfig.StoneDepositsPerZone;
        int ironPerZone = mapGenConfig.IronDepositsPerZone;
        int oilPerZone = mapGenConfig.OilDepositsPerZone;
        int amountMin = mapGenConfig.ResourceAmountMin;
        int amountMax = mapGenConfig.ResourceAmountMax;

        // Spawn starter resources very close to command center (guaranteed for early game)
        int starterStone = mapGenConfig.StarterStoneNodes;
        int starterIron = mapGenConfig.StarterIronNodes;
        SpawnStarterResources(context, ResourceTypeId.Stone, starterStone, amountMin, amountMax);
        SpawnStarterResources(context, ResourceTypeId.Iron, starterIron, amountMin, amountMax);

        // For each tier zone (skip safe zone tier 0)
        for (int tier = 1; tier < _zoneTierService.TierCount; tier++)
        {
            Fixed64 densityMultiplier = _zoneTierService.GetResourceDensityMultiplier(tier);
            int scaledGold = (densityMultiplier * Fixed64.FromInt(goldPerZone)).ToInt();
            int scaledStone = (densityMultiplier * Fixed64.FromInt(stonePerZone)).ToInt();
            int scaledIron = (densityMultiplier * Fixed64.FromInt(ironPerZone)).ToInt();
            int scaledOil = (densityMultiplier * Fixed64.FromInt(oilPerZone)).ToInt();

            // Spawn gold deposits in this tier (outer zones only)
            SpawnResourcesInZone(context, tier, ResourceTypeId.Gold, scaledGold, amountMin, amountMax);

            // Spawn stone deposits in this tier (for quarries)
            SpawnResourcesInZone(context, tier, ResourceTypeId.Stone, scaledStone, amountMin, amountMax);

            // Spawn iron deposits in this tier (for quarries/iron mines)
            SpawnResourcesInZone(context, tier, ResourceTypeId.Iron, scaledIron, amountMin, amountMax);

            // Spawn oil deposits in this tier (for refineries)
            SpawnResourcesInZone(context, tier, ResourceTypeId.Oil, scaledOil, amountMin, amountMax);
        }
    }

    /// <summary>
    /// Spawns starter resources very close to the command center.
    /// These are guaranteed for early game economy (Stone for quarries, Iron for later).
    /// </summary>
    private void SpawnStarterResources(
        in SimulationContext context,
        ResourceTypeId resourceType,
        int count,
        int amountMin,
        int amountMax)
    {
        if (count <= 0) return;

        int centerTileX = _widthTiles / 2;
        int centerTileY = _heightTiles / 2;

        // Command center is ~4x4 tiles, power radius is 256px (8 tiles).
        // Spawn resources safely within power range (3-6 tiles from center)
        const int minRadius = 3;
        const int maxRadius = 6;

        int spawnedCount = 0;
        int attempt = 0;
        int maxAttempts = count * 200;
        int slotBase = 9000 + (int)resourceType * 100;  // Unique slot base for starters

        // Spawn resources in a ring very close to command center
        while (spawnedCount < count && attempt < maxAttempts)
        {
            // Generate angle and radius for circular distribution
            int angleSlot = DeterministicRandom.RangeWithSeed(
                context.SessionSeed, context.CurrentFrame, slotBase + attempt, SaltX,
                0, 360);
            // Spawn very close to command center
            int radiusTiles = DeterministicRandom.RangeWithSeed(
                context.SessionSeed, context.CurrentFrame, slotBase + attempt, SaltY,
                minRadius, maxRadius);

            attempt++;

            // Convert polar to tile coordinates (angle in radians: degrees * PI / 180)
            Fixed64 angleRad = Fixed64.FromInt(angleSlot) * Fixed64.Pi / Fixed64.FromInt(180);
            int tileX = centerTileX + (Fixed64.FromInt(radiusTiles) * Fixed64.Cos(angleRad)).ToInt();
            int tileY = centerTileY + (Fixed64.FromInt(radiusTiles) * Fixed64.Sin(angleRad)).ToInt();

            // Bounds check
            if (tileX < 0 || tileX >= _widthTiles || tileY < 0 || tileY >= _heightTiles) continue;

            // Don't require passable terrain - nodes can be on any terrain
            // Players will build quarries on nearby passable terrain

            // Check no existing resource at this tile
            if (HasResourceNodeAt(tileX, tileY)) continue;

            // Spawn resource node with slightly higher amounts for starters
            int amount = DeterministicRandom.RangeWithSeed(
                context.SessionSeed, context.CurrentFrame, slotBase + spawnedCount, SaltAmt,
                amountMin, amountMax);

            SpawnResourceNode(tileX, tileY, resourceType, amount);
            spawnedCount++;
        }
    }

    private void SpawnResourcesInZone(
        in SimulationContext context,
        int tier,
        ResourceTypeId resourceType,
        int count,
        int amountMin,
        int amountMax)
    {
        if (count <= 0) return;

        int spawnedCount = 0;
        int attempt = 0;
        int maxAttempts = count * 100;  // Allow many attempts to find valid positions
        int slotBase = tier * 1000 + (int)resourceType * 100;  // Unique slot base per tier/type

        while (spawnedCount < count && attempt < maxAttempts)
        {
            // Generate random tile position
            int tileX = DeterministicRandom.RangeWithSeed(
                context.SessionSeed, context.CurrentFrame, slotBase + attempt, SaltX,
                0, _widthTiles);
            int tileY = DeterministicRandom.RangeWithSeed(
                context.SessionSeed, context.CurrentFrame, slotBase + attempt, SaltY,
                0, _heightTiles);

            attempt++;

            // Get world position center of tile
            int worldX = tileX * _tileSize + _tileSize / 2;
            int worldY = tileY * _tileSize + _tileSize / 2;

            // Verify this tile is in the correct tier
            int tileTier = _zoneTierService.GetTierAt(Fixed64.FromInt(worldX), Fixed64.FromInt(worldY));
            if (tileTier != tier) continue;

            // Check if terrain allows this resource type (e.g., Wood on Dirt, Stone/Iron on Mountain)
            // Note: We don't check passability - resources like forests spawn on impassable Dirt terrain
            // Players build nearby on passable terrain and the building queries nodes within its radius
            var terrain = _terrainData.GetTerrainAt(tileX, tileY);
            if (!_generator.CanSpawnResource(terrain, resourceType)) continue;

            // Check if there's already a resource node at this tile
            if (HasResourceNodeAt(tileX, tileY)) continue;

            // Spawn resource node
            int amount = DeterministicRandom.RangeWithSeed(
                context.SessionSeed, context.CurrentFrame, slotBase + spawnedCount, SaltAmt,
                amountMin, amountMax);

            SpawnResourceNode(tileX, tileY, resourceType, amount);
            spawnedCount++;
        }
    }

    private bool HasResourceNodeAt(int tileX, int tileY)
    {
        var resources = World.ResourceNodeRows;
        for (int slot = 0; slot < resources.Count; slot++)
        {
            var row = resources.GetRowBySlot(slot);
            if (row.TileX == tileX && row.TileY == tileY)
                return true;
        }
        return false;
    }

    private void SpawnResourceNode(int tileX, int tileY, ResourceTypeId typeId, int amount)
    {
        var resources = World.ResourceNodeRows;
        var handle = resources.Allocate();
        var row = resources.GetRow(handle);

        row.TileX = (ushort)tileX;
        row.TileY = (ushort)tileY;
        row.TypeId = typeId;
        row.RemainingAmount = amount;
        row.MaxAmount = amount;
        row.HarvestRate = 10;  // Base harvest rate
        row.HarvesterCount = 0;
        row.Flags = ResourceNodeFlags.None;
    }
}
