using System;
using System.Runtime.CompilerServices;
using Catrillion.Core;
using Catrillion.Simulation.Components;
using Core;

namespace Catrillion.Simulation.Systems.SimTable;

/// <summary>
/// Reciprocal Velocity Obstacles system for fine-grained collision avoidance.
/// Uses spatial queries to find neighbors and computes collision-free velocities.
/// Works alongside SeparationSystem in a hybrid approach:
/// - RVO handles close-range (&lt;64px) agent-to-agent avoidance
/// - SeparationSystem handles longer-range crowd steering
/// </summary>
public sealed class RVOSystem : SimTableSystem
{
    // RVO configuration (hardcoded defaults, can be made configurable later)
    private readonly Fixed64 _neighborRadius = Fixed64.FromInt(24);  // Reduced from 64 for performance
    private readonly Fixed64 _neighborRadiusSq;
    private readonly int _maxNeighbors = 4;  // Reduced from 8 for performance
    private readonly Fixed64 _timeHorizon = Fixed64.FromInt(15);  // Reduced from 30
    private readonly Fixed64 _avoidanceWeight = Fixed64.FromFloat(1.0f);
    private readonly Fixed64 _maxAvoidanceForce = Fixed64.FromFloat(20.0f);
    private readonly Fixed64 _smoothingAlpha = Fixed64.FromFloat(0.4f);
    private readonly Fixed64 _defaultAgentRadius = Fixed64.FromInt(6);

    private readonly Fixed64 _oneMinusAlpha;
    private readonly Fixed64 _maxAvoidanceForceSq;

    // Pre-allocated neighbor buffer (reused each frame, zero allocation)
    private const int MaxNeighborsBuffer = 8;
    private readonly int[] _neighborSlots = new int[MaxNeighborsBuffer];

    // Delta-time inverse for scaling avoidance forces (computed each tick)
    private Fixed64 _deltaInverse;

    public RVOSystem(SimWorld world) : base(world)
    {
        _neighborRadiusSq = _neighborRadius * _neighborRadius;
        _oneMinusAlpha = Fixed64.OneValue - _smoothingAlpha;
        _maxAvoidanceForceSq = _maxAvoidanceForce * _maxAvoidanceForce;
    }

    public override void Tick(in SimulationContext context)
    {
        // Compute delta inverse for scaling avoidance forces
        // This cancels out the DeltaSeconds multiplication in MoveableApplyMovementSystem
        _deltaInverse = Fixed64.OneValue / DeltaSeconds;

        // Skip RVO for zombies - too many entities, use density separation instead
        CopyZombiePreferredToVelocity();
        ProcessCombatUnits();
    }

    private void CopyZombiePreferredToVelocity()
    {
        // Zombies just use PreferredVelocity directly - separation system handles avoidance
        var zombies = World.ZombieRows;
        int count = zombies.Count;

        for (int i = 0; i < count; i++)
        {
            var zombie = zombies.GetRowBySlot(i);
            if (zombie.Flags.IsDead()) continue;

            zombie.Velocity = zombie.PreferredVelocity;
        }
    }

    private void ProcessCombatUnits()
    {
        var units = World.CombatUnitRows;
        int count = units.Count;

        for (int i = 0; i < count; i++)
        {
            var unit = units.GetRowBySlot(i);
            if (unit.Flags.IsDead()) continue;

            var result = ComputeRVOVelocityForUnit(
                unit.Position,
                unit.PreferredVelocity,
                unit.SmoothedSeparation,
                unit.MoveSpeed,
                GetAgentRadius(unit.AgentRadius),
                units,
                i);

            // Write results back
            unit.Velocity = result.velocity;
            unit.SmoothedSeparation = result.smoothedSeparation;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Fixed64 GetAgentRadius(Fixed64 agentRadius)
    {
        // Use stored radius if set, otherwise use default
        return agentRadius.Raw > 0 ? agentRadius : _defaultAgentRadius;
    }

    private (Fixed64Vec2 velocity, Fixed64Vec2 smoothedSeparation) ComputeRVOVelocity(
        Fixed64Vec2 position,
        Fixed64Vec2 preferredVelocity,
        Fixed64Vec2 smoothedSeparation,
        Fixed64 maxSpeed,
        Fixed64 agentRadius,
        ZombieRowTable table,
        int selfSlot)
    {
        // 1. Gather neighbors using spatial query
        int neighborCount = GatherZombieNeighbors(table, position, selfSlot);

        // Also check for nearby combat units to avoid
        neighborCount = AddCombatUnitNeighbors(position, neighborCount);

        // 2. If no neighbors, use preferred velocity directly
        if (neighborCount == 0)
        {
            return (preferredVelocity, smoothedSeparation);
        }

        // 3. Compute avoidance velocity
        Fixed64Vec2 avoidance = ComputeAvoidanceVelocityForZombie(
            position, preferredVelocity, agentRadius,
            table, neighborCount);

        // 4. Apply EMA smoothing
        Fixed64Vec2 smoothedAvoid = smoothedSeparation * _oneMinusAlpha + avoidance * _smoothingAlpha;

        // 5. Blend with preferred velocity
        // Scale avoidance by deltaInverse so it cancels out the DeltaSeconds multiplication in movement
        Fixed64Vec2 newVelocity = preferredVelocity + smoothedAvoid * _avoidanceWeight * _deltaInverse;

        // 6. Clamp to max speed
        Fixed64 speedSq = newVelocity.LengthSquared();
        Fixed64 maxSpeedSq = maxSpeed * maxSpeed;
        if (speedSq > maxSpeedSq)
        {
            newVelocity = newVelocity.Normalized() * maxSpeed;
        }

        return (newVelocity, smoothedAvoid);
    }

    private (Fixed64Vec2 velocity, Fixed64Vec2 smoothedSeparation) ComputeRVOVelocityForUnit(
        Fixed64Vec2 position,
        Fixed64Vec2 preferredVelocity,
        Fixed64Vec2 smoothedSeparation,
        Fixed64 maxSpeed,
        Fixed64 agentRadius,
        CombatUnitRowTable table,
        int selfSlot)
    {
        // Gather unit neighbors
        int neighborCount = GatherCombatUnitNeighbors(table, position, selfSlot);

        // Also avoid zombies
        neighborCount = AddZombieNeighbors(position, neighborCount);

        if (neighborCount == 0)
        {
            return (preferredVelocity, smoothedSeparation);
        }

        Fixed64Vec2 avoidance = ComputeAvoidanceVelocityForUnit(
            position, preferredVelocity, agentRadius,
            table, neighborCount);

        Fixed64Vec2 smoothedAvoid = smoothedSeparation * _oneMinusAlpha + avoidance * _smoothingAlpha;

        // Scale avoidance by deltaInverse so it cancels out the DeltaSeconds multiplication in movement
        Fixed64Vec2 newVelocity = preferredVelocity + smoothedAvoid * _avoidanceWeight * _deltaInverse;

        Fixed64 speedSq = newVelocity.LengthSquared();
        Fixed64 maxSpeedSq = maxSpeed * maxSpeed;
        if (speedSq > maxSpeedSq)
        {
            newVelocity = newVelocity.Normalized() * maxSpeed;
        }

        return (newVelocity, smoothedAvoid);
    }

    private int GatherZombieNeighbors(ZombieRowTable table, Fixed64Vec2 position, int selfSlot)
    {
        int count = 0;

        foreach (int slot in table.QueryRadius(position, _neighborRadius))
        {
            if (slot == selfSlot) continue;
            if (!table.TryGetRow(slot, out var neighbor)) continue;
            if (neighbor.Flags.IsDead()) continue;

            // Simple unsorted gathering - just take first N
            _neighborSlots[count] = slot;
            count++;
            if (count >= _maxNeighbors) break;
        }

        return count;
    }

    private int GatherCombatUnitNeighbors(CombatUnitRowTable table, Fixed64Vec2 position, int selfSlot)
    {
        int count = 0;

        foreach (int slot in table.QueryRadius(position, _neighborRadius))
        {
            if (slot == selfSlot) continue;
            if (!table.TryGetRow(slot, out var neighbor)) continue;
            if (neighbor.Flags.IsDead()) continue;

            _neighborSlots[count] = slot;
            count++;
            if (count >= _maxNeighbors) break;
        }

        return count;
    }

    private int AddCombatUnitNeighbors(Fixed64Vec2 position, int existingCount)
    {
        // Skip cross-type queries for performance - rely on separation system
        return existingCount;
    }

    private int AddZombieNeighbors(Fixed64Vec2 position, int existingCount)
    {
        // Skip cross-type queries for performance - rely on separation system
        return existingCount;
    }

    private Fixed64Vec2 ComputeAvoidanceVelocityForZombie(
        Fixed64Vec2 position,
        Fixed64Vec2 preferredVelocity,
        Fixed64 agentRadius,
        ZombieRowTable zombieTable,
        int neighborCount)
    {
        Fixed64Vec2 totalAvoidance = Fixed64Vec2.Zero;

        for (int i = 0; i < neighborCount; i++)
        {
            int slot = _neighborSlots[i];
            var neighbor = zombieTable.GetRowBySlot(slot);

            Fixed64Vec2 avoidDir = ComputeAvoidanceForNeighbor(
                position, preferredVelocity, agentRadius,
                neighbor.Position, neighbor.Velocity, GetAgentRadius(neighbor.AgentRadius));

            totalAvoidance = totalAvoidance + avoidDir;
        }

        // Clamp total avoidance
        Fixed64 avoidMagSq = totalAvoidance.LengthSquared();
        if (avoidMagSq > _maxAvoidanceForceSq)
        {
            totalAvoidance = totalAvoidance.Normalized() * _maxAvoidanceForce;
        }

        return totalAvoidance;
    }

    private Fixed64Vec2 ComputeAvoidanceVelocityForUnit(
        Fixed64Vec2 position,
        Fixed64Vec2 preferredVelocity,
        Fixed64 agentRadius,
        CombatUnitRowTable unitTable,
        int neighborCount)
    {
        Fixed64Vec2 totalAvoidance = Fixed64Vec2.Zero;

        for (int i = 0; i < neighborCount; i++)
        {
            int slot = _neighborSlots[i];
            var neighbor = unitTable.GetRowBySlot(slot);

            Fixed64Vec2 avoidDir = ComputeAvoidanceForNeighbor(
                position, preferredVelocity, agentRadius,
                neighbor.Position, neighbor.Velocity, GetAgentRadius(neighbor.AgentRadius));

            totalAvoidance = totalAvoidance + avoidDir;
        }

        Fixed64 avoidMagSq = totalAvoidance.LengthSquared();
        if (avoidMagSq > _maxAvoidanceForceSq)
        {
            totalAvoidance = totalAvoidance.Normalized() * _maxAvoidanceForce;
        }

        return totalAvoidance;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Fixed64Vec2 ComputeAvoidanceForNeighbor(
        Fixed64Vec2 position,
        Fixed64Vec2 preferredVelocity,
        Fixed64 agentRadius,
        Fixed64Vec2 neighborPos,
        Fixed64Vec2 neighborVel,
        Fixed64 neighborRadius)
    {
        Fixed64Vec2 relPos = neighborPos - position;
        Fixed64 combinedRadius = agentRadius + neighborRadius;

        // PROXIMITY-BASED SEPARATION (handles idle units and close neighbors)
        // Soft separation zone: 1.5x the collision radius
        Fixed64 distSq = relPos.LengthSquared();
        Fixed64 separationRadius = combinedRadius + combinedRadius / Fixed64.FromInt(2); // 1.5x
        Fixed64 separationRadiusSq = separationRadius * separationRadius;

        if (distSq < separationRadiusSq)
        {
            if (distSq.Raw == 0)
            {
                // Exactly on top - deterministic fallback direction
                return new Fixed64Vec2(Fixed64.OneValue, Fixed64.Zero);
            }

            Fixed64 dist = Fixed64.Sqrt(distSq);
            Fixed64Vec2 pushDir = (-relPos) / dist; // Normalized away from neighbor

            // Linear falloff: full force at center, zero at separationRadius
            Fixed64 proximity = (separationRadius - dist) / separationRadius;
            return pushDir * proximity * Fixed64.FromInt(2);
        }

        // TTC-BASED AVOIDANCE (for moving units predicting future collisions)
        Fixed64Vec2 relVel = preferredVelocity - neighborVel;
        Fixed64 ttc = ComputeTimeToCollision(relPos, relVel, combinedRadius);

        // No collision predicted within time horizon
        if (ttc >= _timeHorizon || ttc <= Fixed64.Zero)
        {
            return Fixed64Vec2.Zero;
        }

        // Compute avoidance direction (perpendicular to relative position)
        Fixed64Vec2 avoidDir = ComputeAvoidanceDirection(relPos, relVel);

        // Weight by urgency (inverse of ttc)
        Fixed64 urgency = (_timeHorizon - ttc) / _timeHorizon;

        return avoidDir * urgency;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Fixed64 ComputeTimeToCollision(
        Fixed64Vec2 relPos,
        Fixed64Vec2 relVel,
        Fixed64 combinedRadius)
    {
        Fixed64 relPosSq = relPos.LengthSquared();
        Fixed64 combinedRadiusSq = combinedRadius * combinedRadius;

        // Already overlapping
        if (relPosSq <= combinedRadiusSq)
        {
            return Fixed64.Zero;
        }

        Fixed64 dotProduct = Fixed64Vec2.Dot(relPos, relVel);

        // Moving apart (dot product positive means relative velocity points away)
        if (dotProduct >= Fixed64.Zero)
        {
            return Fixed64.MaxValue;
        }

        Fixed64 relVelSq = relVel.LengthSquared();
        if (relVelSq.Raw == 0)
        {
            return Fixed64.MaxValue;
        }

        // Quadratic formula discriminant: b^2 - ac
        // where a = relVelSq, b = dotProduct, c = relPosSq - combinedRadiusSq
        Fixed64 discriminant = dotProduct * dotProduct - relVelSq * (relPosSq - combinedRadiusSq);

        if (discriminant < Fixed64.Zero)
        {
            return Fixed64.MaxValue;
        }

        Fixed64 sqrtDisc = Fixed64.Sqrt(discriminant);
        Fixed64 t = (-dotProduct - sqrtDisc) / relVelSq;

        return t > Fixed64.Zero ? t : Fixed64.MaxValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Fixed64Vec2 ComputeAvoidanceDirection(
        Fixed64Vec2 relPos,
        Fixed64Vec2 relVel)
    {
        // Use perpendicular to relative position
        // Choose the perpendicular that moves us away from collision path
        Fixed64Vec2 perpCW = new Fixed64Vec2(relPos.Y, -relPos.X);   // Clockwise
        Fixed64Vec2 perpCCW = new Fixed64Vec2(-relPos.Y, relPos.X);  // Counter-clockwise

        // Choose the perpendicular that has a negative dot product with relVel
        // (i.e., moves us opposite to the collision direction)
        Fixed64 dotCW = Fixed64Vec2.Dot(perpCW, relVel);
        Fixed64 dotCCW = Fixed64Vec2.Dot(perpCCW, relVel);

        Fixed64Vec2 avoidDir = dotCW < dotCCW ? perpCW : perpCCW;

        return avoidDir.Normalized();
    }
}
