using System.Numerics;

namespace DerpLib.Rendering;

/// <summary>
/// 2D camera with offset, target, rotation, and zoom.
/// </summary>
public struct Camera2D
{
    public Vector2 Offset;    // Screen offset (usually screen center)
    public Vector2 Target;    // World position to look at
    public float Rotation;    // Rotation in radians
    public float Zoom;        // Zoom level (1.0 = normal)

    public Camera2D(Vector2 offset, Vector2 target, float rotation = 0f, float zoom = 1f)
    {
        Offset = offset;
        Target = target;
        Rotation = rotation;
        Zoom = zoom;
    }

    /// <summary>
    /// Create a default 2D camera centered on the screen.
    /// </summary>
    public static Camera2D Default(float screenWidth, float screenHeight)
    {
        return new Camera2D(
            new Vector2(screenWidth / 2f, screenHeight / 2f),
            Vector2.Zero,
            0f,
            1f
        );
    }

    /// <summary>
    /// Get the combined view-projection matrix for 2D rendering.
    /// Produces screen coordinates where (0,0) is top-left.
    /// </summary>
    public readonly Matrix4x4 GetProjection(float width, float height)
    {
        // Build transform: translate to target, rotate, scale by zoom, offset to screen position
        // Then apply orthographic projection

        // Camera transform (inverse of what we want to apply to world)
        var transform =
            Matrix4x4.CreateTranslation(-Target.X, -Target.Y, 0) *
            Matrix4x4.CreateRotationZ(-Rotation) *
            Matrix4x4.CreateScale(Zoom, Zoom, 1) *
            Matrix4x4.CreateTranslation(Offset.X, Offset.Y, 0);

        // Orthographic projection: screen coords to NDC
        // Maps (0,0)-(width,height) to (-1,-1)-(1,1)
        // In Vulkan Y-down: NDC Y=-1 is top, NDC Y=+1 is bottom
        // So screen (0,0) -> NDC (-1,-1) = top-left ✓
        var ortho = new Matrix4x4(
            2f / width, 0, 0, 0,
            0, 2f / height, 0, 0,
            0, 0, 1, 0,
            -1, -1, 0, 1
        );

        return transform * ortho;
    }

    /// <summary>
    /// Convert screen coordinates to world coordinates.
    /// </summary>
    public readonly Vector2 ScreenToWorld(Vector2 screen)
    {
        // Reverse the camera transform
        var offset = screen - Offset;
        offset /= Zoom;

        // Apply rotation (reverse)
        float cos = MathF.Cos(Rotation);
        float sin = MathF.Sin(Rotation);
        var rotated = new Vector2(
            offset.X * cos - offset.Y * sin,
            offset.X * sin + offset.Y * cos
        );

        return rotated + Target;
    }

    /// <summary>
    /// Convert world coordinates to screen coordinates.
    /// </summary>
    public readonly Vector2 WorldToScreen(Vector2 world)
    {
        var offset = world - Target;

        // Apply rotation
        float cos = MathF.Cos(-Rotation);
        float sin = MathF.Sin(-Rotation);
        var rotated = new Vector2(
            offset.X * cos - offset.Y * sin,
            offset.X * sin + offset.Y * cos
        );

        return rotated * Zoom + Offset;
    }
}
