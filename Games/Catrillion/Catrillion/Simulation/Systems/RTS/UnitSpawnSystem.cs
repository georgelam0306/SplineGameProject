using Catrillion.Core;
using Catrillion.Entities;
using Catrillion.GameData.Schemas;
using Catrillion.Rendering.Components;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Services;
using Core;
using Raylib_cs;
using SimTable;

namespace Catrillion.Simulation.Systems.SimTable;

/// <summary>
/// Spawns initial team combat units on frame 0 at the center of the map.
/// Shared base co-op: spawns a single shared army with OwnerPlayerId = 0.
/// </summary>
public sealed class UnitSpawnSystem : SimTableSystem
{
    private readonly EntitySpawner _spawner;
    private readonly GameDataManager<GameDocDb> _gameData;
    private readonly TerrainDataService _terrainData;
    private bool _hasSpawned;

    // Map center from MapConfigData
    private readonly int _mapCenterX;
    private readonly int _mapCenterY;
    private readonly int _tileSize;
    private const int UnitsPerPlayer = 5;
    private const int UnitSpacing = 40;

    public UnitSpawnSystem(SimWorld world, EntitySpawner spawner, GameDataManager<GameDocDb> gameData, TerrainDataService terrainData) : base(world)
    {
        _spawner = spawner;
        _gameData = gameData;
        _terrainData = terrainData;

        // Load map config from GameDocDb
        ref readonly var mapConfig = ref gameData.Db.MapConfigData.FindById(0);
        _mapCenterX = mapConfig.WidthTiles * mapConfig.TileSize / 2;
        _mapCenterY = mapConfig.HeightTiles * mapConfig.TileSize / 2;
        _tileSize = mapConfig.TileSize;
    }

    public override void Tick(in SimulationContext context)
    {
        // Don't spawn during countdown - wait until game starts
        if (!World.IsPlaying()) return;

        if (_hasSpawned) return;

        // Wait for terrain to be generated
        if (!_terrainData.IsGenerated) return;

        _hasSpawned = true;

        // Shared base co-op: spawn single shared army for team (OwnerPlayerId = 0)
        SpawnTeamUnits();
    }

    private void SpawnTeamUnits()
    {
        var units = World.CombatUnitRows;
        const int teamUnits = 5;  // Single shared army size

        for (int i = 0; i < teamUnits; i++)
        {
            // Grid layout below map center (offset to avoid spawning on CC)
            int offsetX = (i % 3 - 1) * UnitSpacing;
            int offsetY = (i / 3) * UnitSpacing;

            int x = _mapCenterX + offsetX;
            int y = _mapCenterY + offsetY + 150;  // Spawn below CC

            // Find a passable spawn position (search nearby if blocked)
            (x, y) = FindPassableSpawnPosition(x, y);

            // Use builder pattern - creates both SimWorld row AND ECS entity
            // Transform2D is auto-added for tables with Position field
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
            row.Position = Fixed64Vec2.FromInt(x, y);
            row.Velocity = Fixed64Vec2.Zero;
            row.OwnerPlayerId = 0;  // Team ownership (shared base co-op)
            row.TypeId = UnitTypeId.Soldier;  // Must set before InitializeStats
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
        }
    }

    /// <summary>
    /// Finds a passable spawn position near the given coordinates.
    /// Searches in expanding rings around the target position.
    /// </summary>
    private (int x, int y) FindPassableSpawnPosition(int targetX, int targetY)
    {
        int tileX = targetX / _tileSize;
        int tileY = targetY / _tileSize;

        // If target is passable, use it
        if (_terrainData.IsPassable(tileX, tileY))
            return (targetX, targetY);

        // Search in expanding rings around target
        const int maxSearchRadius = 10;
        for (int radius = 1; radius <= maxSearchRadius; radius++)
        {
            // Check tiles in a ring at this radius
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    // Only check tiles on the ring perimeter
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                        continue;

                    int checkTileX = tileX + dx;
                    int checkTileY = tileY + dy;

                    if (_terrainData.IsPassable(checkTileX, checkTileY))
                    {
                        // Return center of the passable tile
                        return (checkTileX * _tileSize + _tileSize / 2,
                                checkTileY * _tileSize + _tileSize / 2);
                    }
                }
            }
        }

        // Fallback: return original position (unit will be stuck but at least spawns)
        return (targetX, targetY);
    }
}
