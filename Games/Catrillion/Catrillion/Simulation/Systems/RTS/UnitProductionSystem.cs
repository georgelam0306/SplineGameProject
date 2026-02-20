using Catrillion.Core;
using Catrillion.Entities;
using Catrillion.GameData.Schemas;
using Catrillion.Rendering.Components;
using Catrillion.Simulation.Components;
using Core;
using Raylib_cs;
using SimTable;

namespace Catrillion.Simulation.Systems.SimTable;

/// <summary>
/// Processes production queues each tick and spawns units when complete.
/// Units spawn at the building's rally point or building edge if no rally point set.
/// </summary>
public sealed class UnitProductionSystem : SimTableSystem
{
    private readonly EntitySpawner _spawner;
    private readonly GameDataManager<GameDocDb> _gameData;
    private readonly int _tileSize;

    public UnitProductionSystem(SimWorld world, EntitySpawner spawner, GameDataManager<GameDocDb> gameData)
        : base(world)
    {
        _spawner = spawner;
        _gameData = gameData;
        _tileSize = gameData.Db.MapConfigData.FindById(0).TileSize;
    }

    public override void Tick(in SimulationContext context)
    {
        if (!World.IsPlaying()) return;

        var buildings = World.BuildingRows;
        var resources = World.GameResourcesRows.GetRowBySlot(0);

        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var building)) continue;
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;

            // Skip if not producing (255 = empty slot at front of queue)
            var queue = building.ProductionQueueArray;
            if (queue[0] == 255) continue;

            // Skip if building is not powered (when it requires power)
            ref readonly var buildingTypeData = ref _gameData.Db.BuildingTypeData.FindById((int)building.TypeId);
            if (buildingTypeData.RequiresPower && !building.Flags.HasFlag(BuildingFlags.IsPowered))
            {
                continue;  // Building not powered, production paused
            }

            // Increment progress
            building.ProductionProgress++;

            // Check if production complete
            if (building.ProductionProgress >= building.ProductionBuildTime)
            {
                SpawnUnit(ref building, queue[0], ref resources);

                // Shift queue forward
                for (int i = 0; i < queue.Length - 1; i++)
                {
                    queue[i] = queue[i + 1];
                }
                queue[queue.Length - 1] = 255;  // Last slot is now empty

                // Reset progress and set build time for next item (if any)
                building.ProductionProgress = 0;
                if (queue[0] != 255)
                {
                    ref readonly var nextUnitData = ref _gameData.Db.UnitTypeData.FindById(queue[0]);
                    building.ProductionBuildTime = nextUnitData.BuildTime;
                }
                else
                {
                    building.ProductionBuildTime = 0;
                }
            }
        }
    }

    private void SpawnUnit(ref BuildingRowRowRef building, byte unitTypeId, ref GameResourcesRowRowRef resources)
    {
        var units = World.CombatUnitRows;
        ref readonly var unitData = ref _gameData.Db.UnitTypeData.FindById(unitTypeId);

        // Determine spawn position
        Fixed64Vec2 spawnPos;
        bool hasRallyPoint = building.RallyPoint.X != Fixed64.Zero || building.RallyPoint.Y != Fixed64.Zero;
        if (hasRallyPoint)
        {
            // Use rally point as spawn position
            spawnPos = building.RallyPoint;
        }
        else
        {
            // Spawn at building edge (bottom center)
            int buildingBottomY = (building.TileY + building.Height) * _tileSize;
            int buildingCenterX = building.TileX * _tileSize + (building.Width * _tileSize) / 2;
            spawnPos = new Fixed64Vec2(
                Fixed64.FromInt(buildingCenterX),
                Fixed64.FromInt(buildingBottomY + 16)  // Slight offset from building edge
            );
        }

        // Use builder pattern to create unit (both SimWorld row and ECS entity)
        var handle = _spawner.Create<CombatUnitRow>()
            .WithRender(new SpriteRenderer
            {
                TexturePath = "Resources/Characters/cat.png",
                Width = 32,
                Height = 32,
                SourceX = 0,
                SourceY = 0,
                SourceWidth = 32,
                SourceHeight = 32,
                Color = Color.Blue  // Team color
            })
            .Spawn();

        // Initialize SimWorld row data
        var row = units.GetRow(handle.SimHandle);
        row.Position = spawnPos;
        row.Velocity = Fixed64Vec2.Zero;
        row.OwnerPlayerId = building.OwnerPlayerId;
        row.TypeId = (UnitTypeId)unitTypeId;
        row.GroupId = 0;
        row.SelectedByPlayerId = -1;
        row.CurrentOrder = OrderType.None;
        row.OrderTarget = Fixed64Vec2.Zero;
        row.Flags = MortalFlags.IsActive;

        // Initialize all [CachedStat] fields from UnitTypeData
        units.InitializeStats(handle.SimHandle, _gameData.Db);

        // Non-cached stats
        row.Health = row.MaxHealth;
        row.AttackTimer = 0;
        row.TargetHandle = SimHandle.Invalid;
        row.OrderTargetHandle = SimHandle.Invalid;
        row.GarrisonedInHandle = SimHandle.Invalid;

        // Update population count
        resources.Population += unitData.PopulationCost;

        // If rally point is set, give move order to rally point
        if (hasRallyPoint)
        {
            row.CurrentOrder = OrderType.Move;
            row.OrderTarget = building.RallyPoint;
        }
    }
}
