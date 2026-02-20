using System;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Grids;
using Core;
using FlowField;

namespace Catrillion.Simulation.Systems.SimTable;

/// <summary>
/// Calculates velocities for combat units moving toward their order targets.
/// Uses flow field pathfinding to navigate around obstacles.
/// </summary>
public sealed class CombatUnitMovementSystem : SimTableSystem
{
    private readonly IZoneFlowService _flowService;

    // Unit speed in world units per second (2.0 per frame * 60 fps)
    private static readonly Fixed64 UnitSpeed = Fixed64.FromFloat(120f);
    // Arrival threshold (squared) - when to stop (16px)
    private static readonly Fixed64 ArrivalThresholdSq = Fixed64.FromFloat(256f); // 16 units squared
    // Direct approach threshold (squared) - switch from flow field to direct steering
    private static readonly Fixed64 DirectApproachThresholdSq = Fixed64.FromFloat(2304f); // 48px squared (~1.5 tiles)
    // Linear for velocity damping within direct approach zone
    private static readonly Fixed64 ArrivalThreshold = Fixed64.FromFloat(16f);
    private static readonly Fixed64 DirectApproachThreshold = Fixed64.FromFloat(48f);

    public CombatUnitMovementSystem(SimWorld world, IZoneFlowService flowService) : base(world)
    {
        _flowService = flowService;
    }

    public override void Tick(in SimulationContext context)
    {
        // Units don't move during countdown
        if (!World.IsPlaying()) return;

        var units = World.CombatUnitRows;

        for (int slot = 0; slot < units.Count; slot++)
        {
            if (!units.TryGetRow(slot, out var unit)) continue;
            if (!unit.Flags.HasFlag(MortalFlags.IsActive)) continue;

            // Skip garrisoned units - they don't move
            if (unit.GarrisonedInHandle.IsValid)
            {
                unit.PreferredVelocity = Fixed64Vec2.Zero;
                unit.Velocity = Fixed64Vec2.Zero;
                continue;
            }

            // Handle units moving to garrison
            if (unit.CurrentOrder == OrderType.EnterGarrison)
            {
                ProcessGarrisonMovement(ref unit);
                continue;
            }

            // Skip units without a move order (Move, AttackMove, or Patrol)
            bool hasMovementOrder = unit.CurrentOrder == OrderType.Move ||
                                    unit.CurrentOrder == OrderType.AttackMove ||
                                    unit.CurrentOrder == OrderType.Patrol;

            if (unit.GroupId == 0 || !hasMovementOrder)
            {
                unit.PreferredVelocity = Fixed64Vec2.Zero;
                unit.Velocity = Fixed64Vec2.Zero;
                continue;
            }

            // For AttackMove/Patrol: stop moving if we have an active target (let combat system handle it)
            if ((unit.CurrentOrder == OrderType.AttackMove || unit.CurrentOrder == OrderType.Patrol) && unit.TargetHandle.IsValid)
            {
                unit.PreferredVelocity = Fixed64Vec2.Zero;
                unit.Velocity = Fixed64Vec2.Zero;
                continue;
            }

            // Calculate distance to target for arrival check
            Fixed64Vec2 toTarget = new Fixed64Vec2(
                unit.OrderTarget.X - unit.Position.X,
                unit.OrderTarget.Y - unit.Position.Y
            );

            Fixed64 distSq = toTarget.X * toTarget.X + toTarget.Y * toTarget.Y;

            // Check if arrived
            if (distSq < ArrivalThresholdSq)
            {
                if (unit.CurrentOrder == OrderType.Patrol)
                {
                    // Patrol: swap start and target positions, continue patrolling
                    var oldTarget = unit.OrderTarget;
                    unit.OrderTarget = unit.PatrolStart;
                    unit.PatrolStart = oldTarget;
                    // Don't clear order - keep patrolling
                }
                else
                {
                    // Move/AttackMove: arrived, clear order
                    unit.PreferredVelocity = Fixed64Vec2.Zero;
                    unit.Velocity = Fixed64Vec2.Zero;
                    unit.GroupId = 0;
                    unit.CurrentOrder = OrderType.None;
                    continue;
                }
            }

            // Close to target? Use direct steering with velocity damping for smooth arrival
            if (distSq < DirectApproachThresholdSq)
            {
                Fixed64 dist = Fixed64.Sqrt(distSq);

                // Dampen velocity as we approach: lerp from full speed at DirectApproachThreshold to ~10% at ArrivalThreshold
                // t = (dist - arrival) / (directApproach - arrival), clamped to [0.1, 1.0]
                Fixed64 range = DirectApproachThreshold - ArrivalThreshold;
                Fixed64 t = (dist - ArrivalThreshold) / range;
                if (t < Fixed64.FromFloat(0.1f)) t = Fixed64.FromFloat(0.1f);
                if (t > Fixed64.OneValue) t = Fixed64.OneValue;

                Fixed64 dampedSpeed = UnitSpeed * t;
                unit.PreferredVelocity = new Fixed64Vec2(
                    (toTarget.X / dist) * dampedSpeed,
                    (toTarget.Y / dist) * dampedSpeed
                );
                continue;
            }

            // Far from target - use multi-target flow field for navigation around obstacles
            Fixed64Vec2 direction = Fixed64Vec2.Zero;

            // Look up the group's MoveCommandRow to get all formation slot targets
            var commands = World.MoveCommandRows;
            for (int cmdSlot = 0; cmdSlot < commands.Count; cmdSlot++)
            {
                if (!commands.TryGetRow(cmdSlot, out var cmd)) continue;
                if (commands.GetStableId(cmdSlot) != unit.GroupId) continue;
                if (cmd.SlotCount <= 0) continue;

                // Build targets span from stored slot positions
                Span<(int, int)> targets = stackalloc (int, int)[cmd.SlotCount];
                for (int i = 0; i < cmd.SlotCount; i++)
                {
                    targets[i] = (cmd.SlotTileXArray[i], cmd.SlotTileYArray[i]);
                }

                // Query multi-target flow field
                direction = _flowService.GetFlowDirectionForTargets(unit.Position, targets);
                break;
            }

            // Fallback to single-target if no command found or multi-target returned zero
            if (direction == Fixed64Vec2.Zero)
            {
                var destTile = WorldTile.FromPixel(unit.OrderTarget);
                direction = _flowService.GetFlowDirectionForDestination(
                    unit.Position, destTile.X, destTile.Y);
            }

            if (direction.X != Fixed64.Zero || direction.Y != Fixed64.Zero)
            {
                unit.PreferredVelocity = new Fixed64Vec2(
                    direction.X * UnitSpeed,
                    direction.Y * UnitSpeed
                );
            }
            else
            {
                // No valid path - stay in place
                unit.PreferredVelocity = Fixed64Vec2.Zero;
            }
        }
    }

    /// <summary>
    /// Handles movement for units with EnterGarrison order.
    /// Similar to regular movement but uses OrderTarget (building position).
    /// </summary>
    private void ProcessGarrisonMovement(ref CombatUnitRowRowRef unit)
    {
        Fixed64Vec2 toTarget = new Fixed64Vec2(
            unit.OrderTarget.X - unit.Position.X,
            unit.OrderTarget.Y - unit.Position.Y
        );

        Fixed64 distSq = toTarget.X * toTarget.X + toTarget.Y * toTarget.Y;

        // Check if arrived
        if (distSq < ArrivalThresholdSq)
        {
            unit.PreferredVelocity = Fixed64Vec2.Zero;
            unit.Velocity = Fixed64Vec2.Zero;
            // Don't clear order here - GarrisonEnterSystem handles that
            return;
        }

        // Close to target? Use direct steering
        if (distSq < DirectApproachThresholdSq)
        {
            Fixed64 dist = Fixed64.Sqrt(distSq);
            unit.PreferredVelocity = new Fixed64Vec2(
                (toTarget.X / dist) * UnitSpeed,
                (toTarget.Y / dist) * UnitSpeed
            );
            return;
        }

        // Far from target - use flow field
        var destTile = WorldTile.FromPixel(unit.OrderTarget);
        Fixed64Vec2 direction = _flowService.GetFlowDirectionForDestination(
            unit.Position, destTile.X, destTile.Y);

        if (direction.X != Fixed64.Zero || direction.Y != Fixed64.Zero)
        {
            unit.PreferredVelocity = new Fixed64Vec2(
                direction.X * UnitSpeed,
                direction.Y * UnitSpeed
            );
        }
        else
        {
            unit.PreferredVelocity = Fixed64Vec2.Zero;
        }
    }
}
