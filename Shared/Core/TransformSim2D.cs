using System.Numerics;
using Friflo.Engine.ECS;

namespace Core;

/// <summary>
/// Shadow copy of SimWorld position/velocity, synced each EndFrame.
/// Used by RenderInterpolationSystem for smooth extrapolation.
/// </summary>
public struct TransformSim2D : IComponent
{
    public Vector2 Position;
    public Vector2 PreviousPosition;
    public Vector2 Velocity;
}
