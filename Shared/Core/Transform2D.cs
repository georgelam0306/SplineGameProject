using System.Numerics;
using Friflo.Engine.ECS;

namespace Core;

/// <summary>
/// Display position component for rendering.
/// Interpolated from TransformSim2D by RenderInterpolationSystem.
/// </summary>
public struct Transform2D : IComponent
{
    public Vector2 Position;
}
