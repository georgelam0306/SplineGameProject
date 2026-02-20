using BaseTemplate.Simulation;

namespace BaseTemplate.Presentation.Rendering;

/// <summary>
/// Holds render interpolation state to break cyclic dependency between Game and render systems.
/// Game writes Alpha each frame, RenderInterpolationSystem reads it.
/// </summary>
public sealed class RenderInterpolation
{
    /// <summary>
    /// Fraction through current simulation tick (0-1).
    /// Set by Game after simulation loop, read by RenderInterpolationSystem.
    /// </summary>
    public float Alpha { get; set; }

    /// <summary>
    /// Simulation delta time in seconds. Set once at initialization.
    /// </summary>
    public float SimDeltaTime { get; set; } = 1f / SimulationConfig.TickRate;
}
