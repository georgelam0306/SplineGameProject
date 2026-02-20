using System.Numerics;

namespace DerpLib.Rendering;

/// <summary>
/// 3D camera with position, target, and perspective projection.
/// </summary>
public struct Camera3D
{
    public Vector3 Position;
    public Vector3 Target;
    public Vector3 Up;
    public float FovY;    // Field of view in radians
    public float Near;
    public float Far;

    public Camera3D(Vector3 position, Vector3 target, float fovY = MathF.PI / 4f, float near = 0.1f, float far = 1000f)
    {
        Position = position;
        Target = target;
        Up = Vector3.UnitY;
        FovY = fovY;
        Near = near;
        Far = far;
    }

    /// <summary>
    /// Get the view matrix (world to camera space).
    /// </summary>
    public readonly Matrix4x4 GetViewMatrix()
    {
        return Matrix4x4.CreateLookAt(Position, Target, Up);
    }

    /// <summary>
    /// Get the projection matrix for the given aspect ratio.
    /// Note: Flips Y for Vulkan coordinate system.
    /// </summary>
    public readonly Matrix4x4 GetProjectionMatrix(float aspect)
    {
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(FovY, aspect, Near, Far);
        // Flip Y for Vulkan (Y-down NDC)
        proj.M22 *= -1;
        return proj;
    }

    /// <summary>
    /// Get combined view-projection matrix.
    /// </summary>
    public readonly Matrix4x4 GetViewProjection(float aspect)
    {
        return GetViewMatrix() * GetProjectionMatrix(aspect);
    }

    /// <summary>
    /// Get the forward direction vector (normalized).
    /// </summary>
    public readonly Vector3 Forward => Vector3.Normalize(Target - Position);

    /// <summary>
    /// Get the right direction vector (normalized).
    /// </summary>
    public readonly Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, Up));
}
