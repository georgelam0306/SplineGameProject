using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Services;
using Core;

namespace Catrillion.Simulation.Systems.SimTable;

// Note: This system uses DeltaSeconds from SimTableSystem base class.
// All velocity values should be in units-per-second.

/// <summary>
/// Unified movement system for all moveable entities (zombies, combat units, projectiles).
/// Applies terrain collision for ground units, projectiles fly freely.
/// </summary>
public sealed class MoveableApplyMovementSystem : SimTableSystem
{
    private readonly TerrainDataService _terrainData;
    private readonly int _tileSize;

    public MoveableApplyMovementSystem(
        SimWorld world,
        TerrainDataService terrainData,
        GameDataManager<GameDocDb> gameData) : base(world)
    {
        _terrainData = terrainData;
        ref readonly var mapConfig = ref gameData.Db.MapConfigData.FindById(0);
        _tileSize = mapConfig.TileSize;
    }

    public override void Tick(in SimulationContext context)
    {
        // Process zombies with terrain collision
        ApplyMovementWithCollision(World.ZombieRows);

        // Process combat units with terrain collision
        ApplyMovementWithCollision(World.CombatUnitRows);

        // Process projectiles without collision (they fly over terrain)
        ApplyProjectileMovement();
    }

    private void ApplyMovementWithCollision(ZombieRowTable zombies)
    {
        for (int slot = 0; slot < zombies.Count; slot++)
        {
            if (!zombies.TryGetRow(slot, out var row)) continue;
            if (row.Flags.IsDead()) continue;

            // Skip if no velocity
            if (row.Velocity.X == Fixed64.Zero && row.Velocity.Y == Fixed64.Zero)
                continue;

            ApplyPositionWithCollision(ref row.Position, ref row.Velocity);
        }
    }

    private void ApplyMovementWithCollision(CombatUnitRowTable units)
    {
        for (int slot = 0; slot < units.Count; slot++)
        {
            if (!units.TryGetRow(slot, out var row)) continue;
            if (!row.Flags.HasFlag(MortalFlags.IsActive)) continue;

            // Skip if no velocity
            if (row.Velocity.X == Fixed64.Zero && row.Velocity.Y == Fixed64.Zero)
                continue;

            ApplyPositionWithCollision(ref row.Position, ref row.Velocity);
        }
    }

    private void ApplyPositionWithCollision(ref Fixed64Vec2 position, ref Fixed64Vec2 velocity)
    {
        // Scale velocity by delta time (velocity is in units-per-second)
        var scaledVelocity = new Fixed64Vec2(velocity.X * DeltaSeconds, velocity.Y * DeltaSeconds);
        var newPosition = position + scaledVelocity;

        // Check if new position is passable
        if (!IsPositionPassable(newPosition))
        {
            // Try sliding along X axis only
            var slideX = new Fixed64Vec2(position.X + scaledVelocity.X, position.Y);
            if (IsPositionPassable(slideX))
            {
                position = slideX;
                return;
            }

            // Try sliding along Y axis only
            var slideY = new Fixed64Vec2(position.X, position.Y + scaledVelocity.Y);
            if (IsPositionPassable(slideY))
            {
                position = slideY;
                return;
            }

            // Can't move at all - stay in place and clear velocity
            velocity = Fixed64Vec2.Zero;
            return;
        }

        position = newPosition;
    }

    private void ApplyProjectileMovement()
    {
        var projectiles = World.ProjectileRows;

        for (int slot = 0; slot < projectiles.Count; slot++)
        {
            if (!projectiles.TryGetRow(slot, out var row)) continue;

            // Skip if no velocity
            if (row.Velocity.X == Fixed64.Zero && row.Velocity.Y == Fixed64.Zero)
                continue;

            // Projectiles fly over terrain - no collision check
            // Scale velocity by delta time (velocity is in units-per-second)
            row.Position = new Fixed64Vec2(
                row.Position.X + row.Velocity.X * DeltaSeconds,
                row.Position.Y + row.Velocity.Y * DeltaSeconds);
        }
    }

    private bool IsPositionPassable(Fixed64Vec2 position)
    {
        int tileX = (position.X / Fixed64.FromInt(_tileSize)).ToInt();
        int tileY = (position.Y / Fixed64.FromInt(_tileSize)).ToInt();

        // Check terrain passability - O(1)
        if (_terrainData.IsGenerated && !_terrainData.IsPassable(tileX, tileY))
            return false;

        // Check building occupancy - O(1) via BuildingOccupancyGrid
        if (_terrainData.IsTileBlockedByBuilding(tileX, tileY))
            return false;

        return true;
    }
}
