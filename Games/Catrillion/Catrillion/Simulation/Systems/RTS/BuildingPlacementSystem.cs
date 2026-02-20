using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Services;
using Core;
using FlowField;
using SimTable;

namespace Catrillion.Simulation.Systems.SimTable;

/// <summary>
/// Processes building placement input and creates buildings on the map.
/// Validates placement before creating the building.
/// </summary>
public sealed class BuildingPlacementSystem : SimTableSystem
{
    private readonly GameDataManager<GameDocDb> _gameData;
    private readonly IZoneFlowService _flowService;
    private readonly TerrainDataService _terrainData;
    private readonly EnvironmentService _environmentService;
    private readonly BuildingConstraintValidator _constraintValidator;
    private readonly PowerNetworkService _powerNetwork;
    private readonly int _tileSize;

    public BuildingPlacementSystem(
        SimWorld world,
        GameDataManager<GameDocDb> gameData,
        IZoneFlowService flowService,
        TerrainDataService terrainData,
        EnvironmentService environmentService,
        BuildingConstraintValidator constraintValidator,
        PowerNetworkService powerNetwork)
        : base(world)
    {
        _gameData = gameData;
        _flowService = flowService;
        _terrainData = terrainData;
        _environmentService = environmentService;
        _constraintValidator = constraintValidator;
        _powerNetwork = powerNetwork;
        _tileSize = gameData.Db.MapConfigData.FindById(0).TileSize;
    }

    public override void Tick(in SimulationContext context)
    {
        if (!World.IsPlaying()) return;

        for (int playerId = 0; playerId < context.PlayerCount; playerId++)
        {
            ref readonly var input = ref context.GetInput(playerId);

            if (!input.HasBuildingPlacement) continue;

            var buildingTypeId = (BuildingTypeId)input.BuildingTypeToBuild;
            int tileX = input.BuildingPlacementTile.X;
            int tileY = input.BuildingPlacementTile.Y;

            // Validate placement (bounds, overlap, etc.)
            if (!IsValidPlacement(playerId, buildingTypeId, tileX, tileY))
            {
                continue;
            }

            // Check if player can afford the building (shared base co-op: global resources)
            ref readonly var typeData = ref _gameData.Db.BuildingTypeData.FindById((int)buildingTypeId);
            var resources = World.GameResourcesRows.GetRowBySlot(0);

            // Check affordability against global resources
            if (resources.Gold < typeData.CostGold ||
                resources.Wood < typeData.CostWood ||
                resources.Stone < typeData.CostStone ||
                resources.Iron < typeData.CostIron ||
                resources.Oil < typeData.CostOil)
            {
                continue;
            }

            // Create the building
            PlaceBuilding(playerId, buildingTypeId, tileX, tileY);

            // Track building construction for stats
            UpdateBuildingStats();

            // Deduct building costs from global resources
            resources.Gold -= typeData.CostGold;
            resources.Wood -= typeData.CostWood;
            resources.Stone -= typeData.CostStone;
            resources.Iron -= typeData.CostIron;
            resources.Oil -= typeData.CostOil;
        }
    }

    private void UpdateBuildingStats()
    {
        var stats = World.MatchStatsRows;
        if (stats.TryGetRow(0, out var row))
        {
            row.BuildingsConstructed++;
        }
    }

    private bool IsValidPlacement(int playerId, BuildingTypeId typeId, int tileX, int tileY)
    {
        ref readonly var typeData = ref _gameData.Db.BuildingTypeData.FindById((int)typeId);
        ref readonly var mapConfig = ref _gameData.Db.MapConfigData.FindById(0);

        // Check unique building constraint - only one allowed (shared base co-op)
        if (typeData.IsUnique)
        {
            var allBuildings = World.BuildingRows;
            for (int slot = 0; slot < allBuildings.Count; slot++)
            {
                if (!allBuildings.TryGetRow(slot, out var existingBuilding)) continue;
                if (existingBuilding.Flags.IsDead()) continue;
                if (existingBuilding.TypeId != typeId) continue;
                if (existingBuilding.Flags.HasFlag(BuildingFlags.IsActive) ||
                    existingBuilding.Flags.HasFlag(BuildingFlags.IsUnderConstruction))
                    return false;
            }
        }

        int width = typeData.Width;
        int height = typeData.Height;

        // Check map bounds
        if (tileX < 0 || tileY < 0 ||
            tileX + width > mapConfig.WidthTiles ||
            tileY + height > mapConfig.HeightTiles)
        {
            return false;
        }

        // Check terrain passability and resource nodes for all tiles in building footprint
        var resourceNodes = World.ResourceNodeRows;
        for (int tx = tileX; tx < tileX + width; tx++)
        {
            for (int ty = tileY; ty < tileY + height; ty++)
            {
                if (!_terrainData.IsPassable(tx, ty))
                {
                    return false;
                }
                // Check for resource node blocking (unless building requires placement on node)
                if (!typeData.RequiresOnTopOfNode && _terrainData.IsTileBlockedByResourceNode(tx, ty, resourceNodes))
                {
                    return false;
                }
            }
        }

        // Check for building overlap
        var buildings = World.BuildingRows;
        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var existing)) continue;
            // Check both active and under-construction buildings for overlap
            if (!existing.Flags.HasFlag(BuildingFlags.IsActive) &&
                !existing.Flags.HasFlag(BuildingFlags.IsUnderConstruction)) continue;

            // Check AABB overlap
            if (RectanglesOverlap(
                tileX, tileY, width, height,
                existing.TileX, existing.TileY, existing.Width, existing.Height))
            {
                return false;
            }
        }

        // Check constraint system (environment requirements, power connection, nearby buildings, etc.)
        var constraintResult = _constraintValidator.Validate(World, typeId, tileX, tileY);
        if (!constraintResult.IsValid)
        {
            return false;
        }

        return true;
    }

    private static bool RectanglesOverlap(
        int x1, int y1, int w1, int h1,
        int x2, int y2, int w2, int h2)
    {
        return x1 < x2 + w2 && x1 + w1 > x2 &&
               y1 < y2 + h2 && y1 + h1 > y2;
    }

    private void PlaceBuilding(int playerId, BuildingTypeId typeId, int tileX, int tileY)
    {
        var buildings = World.BuildingRows;
        ref readonly var typeData = ref _gameData.Db.BuildingTypeData.FindById((int)typeId);

        // Allocate building row
        var handle = buildings.Allocate();
        var row = buildings.GetRow(handle);

        // Tile position
        row.TileX = (ushort)tileX;
        row.TileY = (ushort)tileY;
        row.Width = (byte)typeData.Width;
        row.Height = (byte)typeData.Height;

        // World position (center of building for spatial queries)
        int centerWorldX = tileX * _tileSize + (typeData.Width * _tileSize) / 2;
        int centerWorldY = tileY * _tileSize + (typeData.Height * _tileSize) / 2;
        row.Position = new Fixed64Vec2(Fixed64.FromInt(centerWorldX), Fixed64.FromInt(centerWorldY));

        // Identity - must set before InitializeStats
        row.TypeId = typeId;
        row.OwnerPlayerId = (byte)playerId;
        row.SelectedByPlayerId = -1;

        // Initialize all [CachedStat] fields from BuildingTypeData
        buildings.InitializeStats(handle, _gameData.Db);

        // Non-cached stats
        row.Health = row.MaxHealth;

        // Effective generation (initialized to base values set by InitializeStats)
        row.EffectiveGeneratesGold = row.GeneratesGold;
        row.EffectiveGeneratesWood = row.GeneratesWood;
        row.EffectiveGeneratesStone = row.GeneratesStone;
        row.EffectiveGeneratesIron = row.GeneratesIron;
        row.EffectiveGeneratesOil = row.GeneratesOil;
        row.EffectiveGeneratesFood = row.GeneratesFood;

        // Calculate environment bonus
        _environmentService.CalculateEnvironmentBonus(buildings, handle, World.ResourceNodeRows);

        // Resource accumulator for sub-second generation
        row.ResourceAccumulator = 0;

        // Garrison (GarrisonCapacity is a CachedStat)
        row.GarrisonCount = 0;
        var garrisonSlots = row.GarrisonSlotArray;
        for (int i = 0; i < garrisonSlots.Length; i++)
            garrisonSlots[i] = SimHandle.Invalid;

        // Production queue (255 = empty slot, since 0 is valid UnitTypeId)
        var queue = row.ProductionQueueArray;
        for (int i = 0; i < queue.Length; i++)
            queue[i] = 255;
        row.ProductionProgress = 0;
        row.ProductionBuildTime = 0;

        // Construction state - use IsUnderConstruction if building has build time
        row.ConstructionProgress = 0;
        row.ConstructionBuildTime = typeData.BuildTime;
        row.DeathFrame = 0;

        if (typeData.BuildTime > 0)
        {
            // Building requires construction time
            row.Flags = BuildingFlags.IsUnderConstruction;
        }
        else
        {
            // Instant build (BuildTime = 0)
            row.Flags = BuildingFlags.IsActive;
            // IsPowered will be set by ModifierApplicationSystem based on PowerGrid
        }

        // Mark flow field tiles dirty for the building footprint
        for (int tx = tileX; tx < tileX + typeData.Width; tx++)
        {
            for (int ty = tileY; ty < tileY + typeData.Height; ty++)
            {
                _flowService.MarkTileDirty(tx, ty);
            }
        }

        // Mark tiles as blocked in occupancy grid
        _terrainData.MarkBuildingTiles(tileX, tileY, typeData.Width, typeData.Height, blocked: true);

        // Rebuild spatial hash so the new building is included in QueryRadius
        World.BuildingRows.SpatialSort();

        // Invalidate power network for instantly-completed buildings (BuildTime = 0)
        // Buildings under construction will invalidate when construction completes
        if (typeData.BuildTime <= 0)
        {
            _powerNetwork.InvalidateNetwork();
        }
    }
}
