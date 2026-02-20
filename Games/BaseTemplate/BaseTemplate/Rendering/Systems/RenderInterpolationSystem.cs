using System.Numerics;
using Friflo.Engine.ECS.Systems;
using Core;

namespace BaseTemplate.Presentation.Rendering.Systems;

/// <summary>
/// Interpolates display positions between sim ticks.
/// Reads from TransformSim2D (synced by EndFrame), writes to Transform2D.Position.
/// </summary>
public sealed class RenderInterpolationSystem : QuerySystem<Transform2D, TransformSim2D>
{
    private readonly RenderInterpolation _interpolation;

    public RenderInterpolationSystem(RenderInterpolation interpolation)
    {
        _interpolation = interpolation;
    }

    protected override void OnUpdate()
    {
        float alpha = _interpolation.Alpha;

        foreach (var chunk in Query.Chunks)
        {
            var transforms = chunk.Chunk1.Span;
            var simTransforms = chunk.Chunk2.Span;
            int length = chunk.Length;

            for (int i = 0; i < length; i++)
            {
                ref var transform = ref transforms[i];
                ref readonly var sim = ref simTransforms[i];

                var prev = sim.PreviousPosition;
                var curr = sim.Position;

                // First frame snap
                if (prev == Vector2.Zero && curr != Vector2.Zero)
                    prev = curr;

                // If not moving, snap to avoid micro-jitter
                if (sim.Velocity.LengthSquared() < 0.01f)
                {
                    transform.Position = curr;
                }
                else
                {
                    transform.Position = Vector2.Lerp(prev, curr, alpha);
                }
            }
        }
    }
}
