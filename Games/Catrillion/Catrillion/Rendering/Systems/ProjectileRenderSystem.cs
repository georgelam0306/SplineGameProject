using System.Numerics;
using Friflo.Engine.ECS.Systems;
using Raylib_cs;
using Catrillion.Rendering;
using Catrillion.Simulation.Components;

namespace Catrillion.Rendering.Systems;

/// <summary>
/// Renders projectiles in the game world with interpolation.
/// </summary>
public sealed class ProjectileRenderSystem : BaseSystem
{
    private readonly SimWorld _simWorld;
    private readonly RenderInterpolation _interpolation;
    private readonly Vector2[] _previousPositions = new Vector2[2000]; // Match ProjectileRow capacity
    private const float ProjectileRadius = 3f;

    public ProjectileRenderSystem(SimWorld simWorld, RenderInterpolation interpolation)
    {
        _simWorld = simWorld;
        _interpolation = interpolation;
    }

    protected override void OnUpdateGroup()
    {
        var projectiles = _simWorld.ProjectileRows;
        float alpha = _interpolation.Alpha;

        for (int slot = 0; slot < projectiles.Count; slot++)
        {
            if (!projectiles.TryGetRow(slot, out var proj)) continue;
            if (!proj.Flags.HasFlag(ProjectileFlags.IsActive)) continue;

            var currentPos = proj.Position.ToVector2();
            var prevPos = _previousPositions[slot];

            // First frame or after respawn: snap to current
            if (prevPos == Vector2.Zero && currentPos != Vector2.Zero)
                prevPos = currentPos;

            var pos = Vector2.Lerp(prevPos, currentPos, alpha);
            _previousPositions[slot] = currentPos;

            // Different colors for different projectile types
            Color color = proj.Type switch
            {
                ProjectileType.Bullet => Color.Yellow,
                ProjectileType.Arrow => Color.Brown,
                ProjectileType.Shell => Color.Orange,
                ProjectileType.Rocket => Color.Red,
                ProjectileType.AcidSpit => Color.Green,
                _ => Color.White
            };

            // Draw projectile as a small circle
            Raylib.DrawCircle((int)pos.X, (int)pos.Y, ProjectileRadius, color);

            // Draw velocity line (direction indicator)
            var vel = proj.Velocity.ToVector2();
            if (vel.X != 0 || vel.Y != 0)
            {
                float len = System.MathF.Sqrt(vel.X * vel.X + vel.Y * vel.Y);
                if (len > 0)
                {
                    float dirX = vel.X / len * 6f;
                    float dirY = vel.Y / len * 6f;
                    Raylib.DrawLine(
                        (int)pos.X,
                        (int)pos.Y,
                        (int)(pos.X + dirX),
                        (int)(pos.Y + dirY),
                        color
                    );
                }
            }
        }
    }
}
