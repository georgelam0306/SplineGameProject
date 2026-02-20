using Catrillion.Simulation.Components;
using Catrillion.Simulation.Systems.SimTable;
using Core;
using SimTable;

namespace Catrillion.Simulation.Systems.RTS;

/// <summary>
/// Automatically acquires zombie targets for idle combat units.
/// Runs BEFORE CombatUnitMovementSystem so units can face their targets.
/// </summary>
public sealed class CombatUnitTargetAcquisitionSystem : SimTableSystem
{
    public CombatUnitTargetAcquisitionSystem(SimWorld world) : base(world)
    {
    }

    public override void Tick(in SimulationContext context)
    {
        if (!World.IsPlaying()) return;

        var units = World.CombatUnitRows;
        var zombies = World.ZombieRows;
        var buildings = World.BuildingRows;

        // Reset all zombie incoming damage counters
        for (int slot = 0; slot < zombies.Count; slot++)
        {
            var zombie = zombies.GetRowBySlot(slot);
            zombie.IncomingDamage = 0;
        }

        for (int slot = 0; slot < units.Count; slot++)
        {
            var unit = units.GetRowBySlot(slot);
            if (unit.Flags.IsDead()) continue;

            // Only acquire targets when idle, on attack-move order, patrol, or garrisoned
            bool isGarrisoned = unit.GarrisonedInHandle.IsValid;
            bool canAcquireTargets = unit.CurrentOrder == OrderType.None ||
                                     unit.CurrentOrder == OrderType.AttackMove ||
                                     unit.CurrentOrder == OrderType.Patrol;
            if (!isGarrisoned && !canAcquireTargets)
                continue;

            // Determine search position and range
            // If garrisoned, use building's position and range (elevated position = better range)
            Fixed64Vec2 searchPosition = unit.Position;
            Fixed64 attackRange = unit.AttackRange;

            if (isGarrisoned)
            {
                int buildingSlot = buildings.GetSlot(unit.GarrisonedInHandle);
                if (buildings.TryGetRow(buildingSlot, out var building))
                {
                    searchPosition = building.Position;
                    attackRange = building.AttackRange;  // Use building's extended range
                }
            }

            // Find nearest zombie within attack range that isn't already claimed
            Fixed64 attackRangeSq = attackRange * attackRange;
            Fixed64 bestDistSq = attackRangeSq;
            SimHandle bestHandle = SimHandle.Invalid;
            int bestZombieSlot = -1;

            foreach (int zombieSlot in zombies.QueryRadius(searchPosition, attackRange))
            {
                if (!zombies.TryGetRow(zombieSlot, out var zombie)) continue;
                if (zombie.Flags.IsDead()) continue;

                // Skip zombies that already have enough incoming damage to die
                if (zombie.IncomingDamage >= zombie.Health) continue;

                Fixed64 dx = zombie.Position.X - searchPosition.X;
                Fixed64 dy = zombie.Position.Y - searchPosition.Y;
                Fixed64 distSq = dx * dx + dy * dy;

                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestHandle = zombies.GetHandle(zombieSlot);
                    bestZombieSlot = zombieSlot;
                }
            }

            unit.TargetHandle = bestHandle;

            // Add this unit's damage to the target's incoming damage counter
            if (bestZombieSlot >= 0 && zombies.TryGetRow(bestZombieSlot, out var targetZombie))
            {
                targetZombie.IncomingDamage += unit.Damage;
            }
        }
    }

    private bool IsTargetValid(SimHandle targetHandle, Fixed64Vec2 unitPos, Fixed64 attackRange)
    {
        var zombies = World.ZombieRows;
        int targetSlot = zombies.GetSlot(targetHandle);
        if (targetSlot < 0) return false;
        if (!zombies.TryGetRow(targetSlot, out var zombie)) return false;
        if (zombie.Flags.IsDead()) return false;

        // Check if still in range
        Fixed64 dx = zombie.Position.X - unitPos.X;
        Fixed64 dy = zombie.Position.Y - unitPos.Y;
        Fixed64 distSq = dx * dx + dy * dy;
        Fixed64 rangeSq = attackRange * attackRange;

        return distSq <= rangeSq;
    }
}
