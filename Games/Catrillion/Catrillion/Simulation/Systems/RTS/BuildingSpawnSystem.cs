using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Services;
using Core;
using FlowField;
using SimTable;

namespace Catrillion.Simulation.Systems.SimTable;

/// <summary>
/// Spawns the initial Command Center for the team when the game starts.
/// Shared base co-op: one Command Center at map center with OwnerPlayerId = 0.
/// </summary>
public sealed class BuildingSpawnSystem : SimTableSystem
{
    private readonly GameDataManager<GameDocDb> _gameData;
    private readonly IZoneFlowService _flowService;
    private readonly TerrainDataService _terrainData;
    private readonly EnvironmentService _environmentService;
    private readonly PowerNetworkService _powerNetwork;
    private bool _hasSpawned;

    // Map center tile coordinates
    private readonly int _mapCenterTileX;
    private readonly int _mapCenterTileY;
    private readonly int _tileSize;

    public BuildingSpawnSystem(
        SimWorld world,
        GameDataManager<GameDocDb> gameData,
        IZoneFlowService flowService,
        TerrainDataService terrainData,
        EnvironmentService environmentService,
        PowerNetworkService powerNetwork)
        : base(world)
    {
        _gameData = gameData;
        _flowService = flowService;
        _terrainData = terrainData;
        _environmentService = environmentService;
        _powerNetwork = powerNetwork;

        // Load map config from GameDocDb
        ref readonly var mapConfig = ref gameData.Db.MapConfigData.FindById(0);
        _mapCenterTileX = mapConfig.WidthTiles / 2;
        _mapCenterTileY = mapConfig.HeightTiles / 2;
        _tileSize = mapConfig.TileSize;
    }

    public override void Tick(in SimulationContext context)
    {
        // Wait until game starts
        if (!World.IsPlaying()) return;

        if (_hasSpawned) return;
        _hasSpawned = true;

        // Initialize global resources for shared base co-op
        InitializeGlobalResources();

        // Shared base co-op: spawn single Command Center for team
        SpawnTeamCommandCenter();
    }

    private void InitializeGlobalResources()
    {
        var resources = World.GameResourcesRows;

        // Allocate if not already allocated
        if (resources.Count == 0)
        {
            resources.Allocate();
        }

        var row = resources.GetRowBySlot(0);

        // Starting resources (They Are Billions style)
        row.Gold = 400;
        row.Wood = 100;
        row.Stone = 50;
        row.Iron = 0;
        row.Oil = 0;

        // Base max storage (will be increased by ModifierApplicationSystem each tick)
        row.MaxGold = 500;
        row.MaxWood = 200;
        row.MaxStone = 200;
        row.MaxIron = 100;
        row.MaxOil = 50;

        // Energy/population (populated by buildings via ModifierApplicationSystem)
        row.Energy = 0;
        row.MaxEnergy = 0;
        row.Population = 0;
        row.MaxPopulation = 0;

        // No tech unlocked initially (tech 0 = no requirement)
        row.UnlockedTech = 0;
    }

    private void SpawnTeamCommandCenter()
    {
        var buildings = World.BuildingRows;

        // Get Command Center type data
        ref readonly var ccData = ref _gameData.Db.BuildingTypeData.FindById((int)BuildingTypeId.CommandCenter);

        // Position CC at map center
        int tileX = _mapCenterTileX - ccData.Width / 2;
        int tileY = _mapCenterTileY - ccData.Height / 2;

        // Allocate building row (buildings are sim-only, no ECS entity)
        var handle = buildings.Allocate();
        var row = buildings.GetRow(handle);

        // Tile position
        row.TileX = (ushort)tileX;
        row.TileY = (ushort)tileY;
        row.Width = (byte)ccData.Width;
        row.Height = (byte)ccData.Height;

        // World position (center of building for spatial queries)
        int centerWorldX = tileX * _tileSize + (ccData.Width * _tileSize) / 2;
        int centerWorldY = tileY * _tileSize + (ccData.Height * _tileSize) / 2;
        row.Position = new Fixed64Vec2(Fixed64.FromInt(centerWorldX), Fixed64.FromInt(centerWorldY));

        // Identity (shared base co-op: team ownership) - must set before InitializeStats
        row.TypeId = BuildingTypeId.CommandCenter;
        row.OwnerPlayerId = 0;  // Team ownership
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

        // Calculate environment bonus (CC has no environment requirements, so this will set defaults)
        _environmentService.CalculateEnvironmentBonus(buildings, handle, World.ResourceNodeRows);

        // Resource accumulator
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

        // State - Command Center is immediately active (not under construction)
        row.Flags = BuildingFlags.IsActive;
        // IsPowered will be set by ModifierApplicationSystem based on PowerGrid

        row.ConstructionProgress = 0;
        row.DeathFrame = 0;

        // Mark flow field tiles dirty for the building footprint
        for (int tx = tileX; tx < tileX + ccData.Width; tx++)
        {
            for (int ty = tileY; ty < tileY + ccData.Height; ty++)
            {
                _flowService.MarkTileDirty(tx, ty);
            }
        }

        // Mark tiles as blocked in occupancy grid
        _terrainData.MarkBuildingTiles(tileX, tileY, ccData.Width, ccData.Height, blocked: true);

        // Invalidate power network so it rebuilds with the new generator
        _powerNetwork.InvalidateNetwork();
    }
}
