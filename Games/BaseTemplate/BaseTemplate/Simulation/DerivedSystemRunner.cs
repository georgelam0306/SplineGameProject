using System.Diagnostics;
using BaseTemplate.Simulation.Components;
using SimTable;

namespace BaseTemplate.Simulation;

/// <summary>
/// Manages derived state systems. Generated code handles version tracking and auto-invalidation.
/// </summary>
public partial class DerivedSystemRunner
{
    /// <summary>
    /// Declares derived systems and their table dependencies.
    /// Order matters - each system may depend on previous ones.
    /// The generator produces: constructor, RebuildAll(), InvalidateAll().
    /// </summary>
    [Conditional("DERIVED_SYSTEM_SETUP")]
    static void Setup(Builder b) { } // No derived systems in base template

    /// <summary>Syntax-only builder for Setup method. Never instantiated.</summary>
    private interface Builder
    {
        Builder Add<T>() where T : IDerivedSimSystem;
        Builder Add<T>(Func<SimWorld, object> dep) where T : IDerivedSimSystem;
    }
}
