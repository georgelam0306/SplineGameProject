using Catrillion.Simulation.Components;
using Catrillion.Simulation.Systems.SimTable;
using Core;
using SimTable;

namespace Catrillion.Simulation.Systems;

/// <summary>
/// Handles zombie melee damage application when attack timer completes.
/// Runs AFTER ZombieStateTransitionSystem - damage is applied when re-entering Attack state.
/// </summary>
public sealed class ZombieCombatSystem : SimTableSystem
{
    private static readonly Fixed64 PixelsPerTile = Fixed64.FromInt(32);

    public ZombieCombatSystem(SimWorld world) : base(world)
    {
    }

    public override void Tick(in SimulationContext context)
    {
        if (!World.IsPlaying()) return;

        var zombies = World.ZombieRows;
        var buildings = World.BuildingRows;
        var combatUnits = World.CombatUnitRows;

        for (int slot = 0; slot < zombies.Count; slot++)
        {
            var zombie = zombies.GetRowBySlot(slot);

            // Only process zombies in Attack state with valid targets
            if (zombie.State != ZombieState.Attack) continue;
            if (zombie.Flags.IsDead()) continue;
            if (!zombie.TargetHandle.IsValid) continue;

            // Deal damage when StateTimer hits 1 (attack completes this frame)
            // Timer will be decremented to 0 by transition system, then reset on re-entry
            if (zombie.StateTimer != 1) continue;

            // AttackRange is in tiles, convert to pixels and square for distance check
            Fixed64 attackRangePx = zombie.AttackRange * PixelsPerTile;
            Fixed64 attackRangeSq = attackRangePx * attackRangePx;

            var handle = zombie.TargetHandle;

            // Determine target type from TableId and apply damage
            if (handle.TableId == BuildingRowTable.TableIdConst)
            {
                int targetSlot = buildings.GetSlot(handle);
                if (targetSlot < 0) continue;
                if (!buildings.TryGetRow(targetSlot, out var building)) continue;
                if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;
                if (building.Health <= 0) continue;

                // Range check using distance to nearest edge of building (not center)
                Fixed64 minX = Fixed64.FromInt(building.TileX * 32);
                Fixed64 minY = Fixed64.FromInt(building.TileY * 32);
                Fixed64 maxX = Fixed64.FromInt((building.TileX + building.Width) * 32);
                Fixed64 maxY = Fixed64.FromInt((building.TileY + building.Height) * 32);

                Fixed64 closestX = Fixed64.Clamp(zombie.Position.X, minX, maxX);
                Fixed64 closestY = Fixed64.Clamp(zombie.Position.Y, minY, maxY);

                Fixed64 dx = closestX - zombie.Position.X;
                Fixed64 dy = closestY - zombie.Position.Y;
                Fixed64 distSq = dx * dx + dy * dy;
                if (distSq > attackRangeSq) continue;

                // Apply damage (buildings have no armor)
                building.Health -= zombie.Damage;
            }
            else if (handle.TableId == CombatUnitRowTable.TableIdConst)
            {
                int targetSlot = combatUnits.GetSlot(handle);
                if (targetSlot < 0) continue;
                if (!combatUnits.TryGetRow(targetSlot, out var target)) continue;
                if (target.Flags.IsDead()) continue;

                // Range check using zombie's actual AttackRange
                Fixed64 dx = target.Position.X - zombie.Position.X;
                Fixed64 dy = target.Position.Y - zombie.Position.Y;
                Fixed64 distSq = dx * dx + dy * dy;
                if (distSq > attackRangeSq) continue;

                // Apply damage (consider armor)
                int damage = zombie.Damage;
                int armor = target.Armor;
                int actualDamage = damage - armor;
                if (actualDamage < 1) actualDamage = 1; // Minimum 1 damage

                target.Health -= actualDamage;
            }
        }
    }
}
