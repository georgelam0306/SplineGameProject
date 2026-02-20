using Core;

namespace RVO;

/// <summary>
/// Read-only snapshot of an RVO agent's state for computing avoidance.
/// Use this struct when querying neighbors.
/// </summary>
public readonly struct RVOAgentSnapshot
{
    public readonly Fixed64Vec2 Position;
    public readonly Fixed64Vec2 Velocity;
    public readonly Fixed64 Radius;

    public RVOAgentSnapshot(Fixed64Vec2 position, Fixed64Vec2 velocity, Fixed64 radius)
    {
        Position = position;
        Velocity = velocity;
        Radius = radius;
    }
}

/// <summary>
/// Input data for computing RVO velocity for a single agent.
/// </summary>
public readonly struct RVOAgentInput
{
    public readonly Fixed64Vec2 Position;
    public readonly Fixed64Vec2 PreferredVelocity;
    public readonly Fixed64Vec2 SmoothedSeparation;
    public readonly Fixed64 MaxSpeed;
    public readonly Fixed64 Radius;

    public RVOAgentInput(
        Fixed64Vec2 position,
        Fixed64Vec2 preferredVelocity,
        Fixed64Vec2 smoothedSeparation,
        Fixed64 maxSpeed,
        Fixed64 radius)
    {
        Position = position;
        PreferredVelocity = preferredVelocity;
        SmoothedSeparation = smoothedSeparation;
        MaxSpeed = maxSpeed;
        Radius = radius;
    }
}

/// <summary>
/// Output result from RVO velocity computation.
/// </summary>
public readonly struct RVOAgentResult
{
    public readonly Fixed64Vec2 Velocity;
    public readonly Fixed64Vec2 SmoothedSeparation;

    public RVOAgentResult(Fixed64Vec2 velocity, Fixed64Vec2 smoothedSeparation)
    {
        Velocity = velocity;
        SmoothedSeparation = smoothedSeparation;
    }

    public static RVOAgentResult FromPreferred(Fixed64Vec2 preferredVelocity, Fixed64Vec2 smoothedSeparation)
    {
        return new RVOAgentResult(preferredVelocity, smoothedSeparation);
    }
}
