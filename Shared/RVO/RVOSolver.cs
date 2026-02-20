using System.Runtime.CompilerServices;
using Core;

namespace RVO;

/// <summary>
/// Reciprocal Velocity Obstacles solver for collision avoidance.
/// Computes collision-free velocities for agents based on their neighbors.
///
/// This is a deterministic implementation using Fixed64 math, suitable
/// for rollback netcode and deterministic simulations.
///
/// Two-phase avoidance approach:
/// 1. Proximity-based separation: Handles idle/overlapping agents
/// 2. TTC-based avoidance: Predicts and avoids future collisions
/// </summary>
public sealed class RVOSolver
{
    private readonly RVOConfiguration _config;

    public RVOSolver(RVOConfiguration config)
    {
        _config = config;
    }

    /// <summary>
    /// Computes the collision-free velocity for an agent given its neighbors.
    /// </summary>
    /// <param name="agent">The agent to compute velocity for</param>
    /// <param name="neighbors">Span of neighbor snapshots (should be gathered via spatial query)</param>
    /// <param name="deltaInverse">Inverse of delta time (1/dt) for scaling forces</param>
    /// <returns>The computed velocity and updated smoothed separation</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RVOAgentResult ComputeVelocity(
        in RVOAgentInput agent,
        ReadOnlySpan<RVOAgentSnapshot> neighbors,
        Fixed64 deltaInverse)
    {
        // No neighbors = use preferred velocity directly
        if (neighbors.Length == 0)
        {
            return RVOAgentResult.FromPreferred(agent.PreferredVelocity, agent.SmoothedSeparation);
        }

        // Compute avoidance from all neighbors
        Fixed64Vec2 avoidance = ComputeAvoidanceVelocity(
            agent.Position,
            agent.PreferredVelocity,
            GetAgentRadius(agent.Radius),
            neighbors);

        // Apply EMA smoothing
        Fixed64Vec2 smoothedAvoid = agent.SmoothedSeparation * _config.OneMinusAlpha + avoidance * _config.SmoothingAlpha;

        // Blend with preferred velocity
        // Scale avoidance by deltaInverse so it cancels out the dt multiplication in movement
        Fixed64Vec2 newVelocity = agent.PreferredVelocity + smoothedAvoid * _config.AvoidanceWeight * deltaInverse;

        // Clamp to max speed
        Fixed64 speedSq = newVelocity.LengthSquared();
        Fixed64 maxSpeedSq = agent.MaxSpeed * agent.MaxSpeed;
        if (speedSq > maxSpeedSq)
        {
            newVelocity = newVelocity.Normalized() * agent.MaxSpeed;
        }

        return new RVOAgentResult(newVelocity, smoothedAvoid);
    }

    /// <summary>
    /// Computes the collision-free velocity for an agent given its neighbors.
    /// Overload that doesn't require deltaInverse (uses 1.0).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RVOAgentResult ComputeVelocity(
        in RVOAgentInput agent,
        ReadOnlySpan<RVOAgentSnapshot> neighbors)
    {
        return ComputeVelocity(agent, neighbors, Fixed64.OneValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Fixed64 GetAgentRadius(Fixed64 agentRadius)
    {
        return agentRadius.Raw > 0 ? agentRadius : _config.DefaultAgentRadius;
    }

    private Fixed64Vec2 ComputeAvoidanceVelocity(
        Fixed64Vec2 position,
        Fixed64Vec2 preferredVelocity,
        Fixed64 agentRadius,
        ReadOnlySpan<RVOAgentSnapshot> neighbors)
    {
        Fixed64Vec2 totalAvoidance = Fixed64Vec2.Zero;

        for (int i = 0; i < neighbors.Length; i++)
        {
            ref readonly var neighbor = ref neighbors[i];

            Fixed64Vec2 avoidDir = ComputeAvoidanceForNeighbor(
                position, preferredVelocity, agentRadius,
                neighbor.Position, neighbor.Velocity, GetAgentRadius(neighbor.Radius));

            totalAvoidance = totalAvoidance + avoidDir;
        }

        // Clamp total avoidance
        Fixed64 avoidMagSq = totalAvoidance.LengthSquared();
        if (avoidMagSq > _config.MaxAvoidanceForceSq)
        {
            totalAvoidance = totalAvoidance.Normalized() * _config.MaxAvoidanceForce;
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

        // PHASE 1: PROXIMITY-BASED SEPARATION
        // Handles idle units and close neighbors with soft separation zone (1.5x collision radius)
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

        // PHASE 2: TTC-BASED AVOIDANCE
        // For moving units, predict and avoid future collisions
        Fixed64Vec2 relVel = preferredVelocity - neighborVel;
        Fixed64 ttc = ComputeTimeToCollision(relPos, relVel, combinedRadius);

        // No collision predicted within time horizon
        if (ttc >= _config.TimeHorizon || ttc <= Fixed64.Zero)
        {
            return Fixed64Vec2.Zero;
        }

        // Compute avoidance direction (perpendicular to relative position)
        Fixed64Vec2 avoidDir = ComputeAvoidanceDirection(relPos, relVel);

        // Weight by urgency (inverse of ttc)
        Fixed64 urgency = (_config.TimeHorizon - ttc) / _config.TimeHorizon;

        return avoidDir * urgency;
    }

    /// <summary>
    /// Computes time to collision using quadratic formula.
    /// Returns MaxValue if no collision will occur.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 ComputeTimeToCollision(
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

    /// <summary>
    /// Computes the avoidance direction (perpendicular to relative position).
    /// Chooses the perpendicular that moves away from the collision path.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec2 ComputeAvoidanceDirection(
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
