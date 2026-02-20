using Core;

namespace RVO;

/// <summary>
/// Configuration parameters for RVO collision avoidance.
/// All values use Fixed64 for deterministic behavior.
/// </summary>
public readonly struct RVOConfiguration
{
    /// <summary>
    /// Radius for spatial neighbor queries.
    /// Units should avoid others within this distance.
    /// </summary>
    public readonly Fixed64 NeighborRadius;

    /// <summary>
    /// Squared neighbor radius (pre-computed for spatial queries).
    /// </summary>
    public readonly Fixed64 NeighborRadiusSq;

    /// <summary>
    /// Maximum number of neighbors to consider per agent.
    /// Higher values = more accurate but slower.
    /// </summary>
    public readonly int MaxNeighbors;

    /// <summary>
    /// Time horizon for collision prediction (in simulation frames).
    /// Longer horizon = earlier avoidance, but may cause over-reaction.
    /// </summary>
    public readonly Fixed64 TimeHorizon;

    /// <summary>
    /// Weight multiplier for avoidance forces.
    /// </summary>
    public readonly Fixed64 AvoidanceWeight;

    /// <summary>
    /// Maximum magnitude of avoidance force.
    /// </summary>
    public readonly Fixed64 MaxAvoidanceForce;

    /// <summary>
    /// Squared max avoidance force (pre-computed).
    /// </summary>
    public readonly Fixed64 MaxAvoidanceForceSq;

    /// <summary>
    /// EMA smoothing factor for separation forces (0-1).
    /// Higher = more responsive, lower = smoother.
    /// </summary>
    public readonly Fixed64 SmoothingAlpha;

    /// <summary>
    /// Pre-computed (1 - SmoothingAlpha) for EMA.
    /// </summary>
    public readonly Fixed64 OneMinusAlpha;

    /// <summary>
    /// Default agent radius when not specified.
    /// </summary>
    public readonly Fixed64 DefaultAgentRadius;

    public RVOConfiguration(
        Fixed64 neighborRadius,
        int maxNeighbors,
        Fixed64 timeHorizon,
        Fixed64 avoidanceWeight,
        Fixed64 maxAvoidanceForce,
        Fixed64 smoothingAlpha,
        Fixed64 defaultAgentRadius)
    {
        NeighborRadius = neighborRadius;
        NeighborRadiusSq = neighborRadius * neighborRadius;
        MaxNeighbors = maxNeighbors;
        TimeHorizon = timeHorizon;
        AvoidanceWeight = avoidanceWeight;
        MaxAvoidanceForce = maxAvoidanceForce;
        MaxAvoidanceForceSq = maxAvoidanceForce * maxAvoidanceForce;
        SmoothingAlpha = smoothingAlpha;
        OneMinusAlpha = Fixed64.OneValue - smoothingAlpha;
        DefaultAgentRadius = defaultAgentRadius;
    }

    /// <summary>
    /// Creates a default configuration suitable for most games.
    /// </summary>
    public static RVOConfiguration Default => new RVOConfiguration(
        neighborRadius: Fixed64.FromInt(24),
        maxNeighbors: 4,
        timeHorizon: Fixed64.FromInt(15),
        avoidanceWeight: Fixed64.FromFloat(1.0f),
        maxAvoidanceForce: Fixed64.FromFloat(20.0f),
        smoothingAlpha: Fixed64.FromFloat(0.4f),
        defaultAgentRadius: Fixed64.FromInt(6)
    );

    /// <summary>
    /// Creates a high-quality configuration for smaller agent counts.
    /// </summary>
    public static RVOConfiguration HighQuality => new RVOConfiguration(
        neighborRadius: Fixed64.FromInt(64),
        maxNeighbors: 8,
        timeHorizon: Fixed64.FromInt(30),
        avoidanceWeight: Fixed64.FromFloat(1.0f),
        maxAvoidanceForce: Fixed64.FromFloat(20.0f),
        smoothingAlpha: Fixed64.FromFloat(0.3f),
        defaultAgentRadius: Fixed64.FromInt(6)
    );

    /// <summary>
    /// Creates a fast configuration for large agent counts.
    /// </summary>
    public static RVOConfiguration Fast => new RVOConfiguration(
        neighborRadius: Fixed64.FromInt(16),
        maxNeighbors: 2,
        timeHorizon: Fixed64.FromInt(10),
        avoidanceWeight: Fixed64.FromFloat(1.0f),
        maxAvoidanceForce: Fixed64.FromFloat(20.0f),
        smoothingAlpha: Fixed64.FromFloat(0.5f),
        defaultAgentRadius: Fixed64.FromInt(6)
    );
}
