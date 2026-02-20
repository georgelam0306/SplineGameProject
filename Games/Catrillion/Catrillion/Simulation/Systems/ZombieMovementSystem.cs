using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Grids;
using Catrillion.Simulation.Services;
using Catrillion.Simulation.Systems.SimTable;
using Core;
using FlowField;

namespace Catrillion.Simulation.Systems;

/// <summary>
/// Applies movement to zombies based on their current state.
/// Runs AFTER ZombieStateTransitionSystem - only reads state, never changes it.
/// Uses flow fields for pathfinding around obstacles.
/// </summary>
public sealed class ZombieMovementSystem : SimTableSystem
{
    private readonly CenterFlowService _centerFlowService;
    private readonly IZoneFlowService _zoneFlowService;
    private readonly int _tileSize;

    // Wander is 1/3 of chase speed
    private static readonly Fixed64 WanderSpeedMultiplier = Fixed64.FromFloat(0.333f);

    // Degrees to radians conversion
    private static readonly Fixed64 DegToRad = Fixed64.FromFloat(0.0174533f);

    public ZombieMovementSystem(
        SimWorld world,
        CenterFlowService centerFlowService,
        IZoneFlowService zoneFlowService,
        GameDataManager<GameDocDb> gameData) : base(world)
    {
        _centerFlowService = centerFlowService;
        _zoneFlowService = zoneFlowService;

        ref readonly var mapConfig = ref gameData.Db.MapConfigData.FindById(0);
        _tileSize = mapConfig.TileSize;
    }

    public override void Tick(in SimulationContext context)
    {
        // Zombies don't move during countdown
        if (!World.IsPlaying()) return;

        var zombies = World.ZombieRows;
        var threatTable = World.ThreatGridStateRows;
        bool hasThreatGrid = threatTable.Count > 0;

        int frame = context.CurrentFrame;

        for (int slot = 0; slot < zombies.Count; slot++)
        {
            var zombie = zombies.GetRowBySlot(slot);
            if (zombie.Flags.IsDead()) continue;

            // Apply movement based on current state
            switch (zombie.State)
            {
                case ZombieState.Idle:
                    ApplyIdleMovement(ref zombie);
                    break;

                case ZombieState.Wander:
                    ApplyWanderMovement(ref zombie, frame);
                    break;

                case ZombieState.Chase:
                    ApplyChaseMovement(ref zombie, threatTable, hasThreatGrid);
                    break;

                case ZombieState.Attack:
                    ApplyAttackMovement(ref zombie);
                    break;

                case ZombieState.WaveChase:
                    ApplyWaveChaseMovement(ref zombie);
                    break;
            }
        }
    }

    private void ApplyIdleMovement(ref ZombieRowRowRef zombie)
    {
        zombie.PreferredVelocity = Fixed64Vec2.Zero;
        zombie.Velocity = Fixed64Vec2.Zero;
    }

    private void ApplyWanderMovement(ref ZombieRowRowRef zombie, int frame)
    {
        // Direction changes slowly (every 0.5 seconds)
        int directionPhase = frame / (SimulationConfig.TickRate / 2);
        int directionSeed = zombie.WanderDirectionSeed + directionPhase;

        Fixed64 angle = Fixed64.FromInt(directionSeed % 360);
        Fixed64 radians = angle * DegToRad;

        Fixed64 dirX = Fixed64.Cos(radians);
        Fixed64 dirY = Fixed64.Sin(radians);

        var direction = new Fixed64Vec2(dirX, dirY);
        zombie.PreferredVelocity = direction * (zombie.MoveSpeed * WanderSpeedMultiplier);

        // Store facing angle
        zombie.FacingAngle = radians;
    }

    private void ApplyChaseMovement(
        ref ZombieRowRowRef zombie,
        ThreatGridStateRowTable threatTable,
        bool hasThreatGrid)
    {
        Fixed64Vec2 direction = Fixed64Vec2.Zero;
        bool foundTarget = false;
        // Priority 1: Flow field pathfinding to target (respects obstacles)
        if (zombie.TargetHandle.IsValid)
        {
            var handle = zombie.TargetHandle;

            // Building targets need special handling - path to perimeter tiles
            if (handle.TableId == BuildingRowTable.TableIdConst)
            {
                int slot = World.BuildingRows.GetSlot(handle);
                if (slot >= 0 && World.BuildingRows.TryGetRow(slot, out var building))
                {
                    if (building.Health > 0)
                    {
                        foundTarget = true;
                        direction = _zoneFlowService.GetFlowDirectionForBuildingDestination(
                            zombie.Position,
                            building.TileX, building.TileY,
                            building.Width, building.Height,
                            ignoreBuildings: true);
                    }
                }
            }
            // Non-building targets (units) - use standard flow
            else
            {
                var targetPos = GetTargetPosition(zombie);
                if (targetPos.HasValue)
                {
                    foundTarget = true;
                    int destTileX = (targetPos.Value.X / Fixed64.FromInt(_tileSize)).ToInt();
                    int destTileY = (targetPos.Value.Y / Fixed64.FromInt(_tileSize)).ToInt();
                    direction = _zoneFlowService.GetFlowDirectionForDestination(
                        zombie.Position, destTileX, destTileY, ignoreBuildings: true);
                }
            }
        }

        // Priority 2: Threat grid - get highest threat cell and path to it via flow field
        if (hasThreatGrid && !foundTarget)
        {
            var (threatCell, threatValue, _) = ThreatGridService.FindHighestThreatNearby(
                threatTable, zombie.Position, zombie.ThreatSearchRadius
            );

            if (threatValue > Fixed64.Zero)
            {
                foundTarget = true;
                // Convert threat cell to tile coordinates using generated API
                var threatTile = threatCell.ToTileCenter();

                // Path to threat tile using flow field (ignores buildings for zombies)
                direction = _zoneFlowService.GetFlowDirectionForDestination(
                    zombie.Position, threatTile.X, threatTile.Y, ignoreBuildings: true);
            }
        }

        // Priority 3: Fallback to center flow (heads toward command center)
        if (!foundTarget)
        {
            direction = _centerFlowService.GetFlowDirection(zombie.Position, ignoreBuildings: true);
        }

        zombie.PreferredVelocity = direction * zombie.MoveSpeed;
        zombie.Flow = direction; // For debug visualization
    }

    private void ApplyAttackMovement(ref ZombieRowRowRef zombie)
    {
        // No movement during attack
        zombie.PreferredVelocity = Fixed64Vec2.Zero;
        zombie.Velocity = Fixed64Vec2.Zero;
    }

    /// <summary>
    /// Wave chase: prioritize nearby targets, but always fall back to center flow.
    /// Never stops moving toward command center.
    /// </summary>
    private void ApplyWaveChaseMovement(ref ZombieRowRowRef zombie)
    {
        Fixed64Vec2 direction = Fixed64Vec2.Zero;
        bool foundTarget = false;

        // Priority 1: Path to acquired target if valid
        if (zombie.TargetHandle.IsValid)
        {
            var handle = zombie.TargetHandle;

            if (handle.TableId == BuildingRowTable.TableIdConst)
            {
                int slot = World.BuildingRows.GetSlot(handle);
                if (slot >= 0 && World.BuildingRows.TryGetRow(slot, out var building))
                {
                    if (building.Health > 0)
                    {
                        foundTarget = true;
                        direction = _zoneFlowService.GetFlowDirectionForBuildingDestination(
                            zombie.Position,
                            building.TileX, building.TileY,
                            building.Width, building.Height,
                            ignoreBuildings: true);
                    }
                }
            }
            else
            {
                var targetPos = GetTargetPosition(zombie);
                if (targetPos.HasValue)
                {
                    foundTarget = true;
                    int destTileX = (targetPos.Value.X / Fixed64.FromInt(_tileSize)).ToInt();
                    int destTileY = (targetPos.Value.Y / Fixed64.FromInt(_tileSize)).ToInt();
                    direction = _zoneFlowService.GetFlowDirectionForDestination(
                        zombie.Position, destTileX, destTileY, ignoreBuildings: true);
                }
            }
        }

        // Priority 2: Always use center flow (toward command center)
        if (!foundTarget)
        {
            direction = _centerFlowService.GetFlowDirection(zombie.Position, ignoreBuildings: true);
        }

        zombie.PreferredVelocity = direction * zombie.MoveSpeed;
        zombie.Flow = direction;
    }

    /// <summary>
    /// Get the position of the zombie's current target, if valid.
    /// Handles both BuildingRow and CombatUnitRow targets via IPlayerTargetableUnits.
    /// </summary>
    private Fixed64Vec2? GetTargetPosition(ZombieRowRowRef zombie)
    {
        if (!World.TryGetPlayerTargetableUnits(zombie.TargetHandle, out var target))
            return null;

        // Health check covers both building (Health <= 0) and unit death
        if (target.Health <= 0)
            return null;

        return target.Position;
    }
}
