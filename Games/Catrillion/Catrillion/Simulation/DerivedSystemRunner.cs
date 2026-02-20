using System.Diagnostics;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.DerivedSystems;
using FlowField;
using SimTable;

namespace Catrillion.Simulation;

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
    static void Setup(Builder b) =>
        b.Add<BuildingOccupancyGrid>(w => w.BuildingRows)   // 1st: depends on BuildingRows
         .Add<PowerGrid>(w => w.BuildingRows)               // 2nd: depends on BuildingRows
         .Add<ZoneGraphSystem>()                            // 3rd: manual invalidation (lazy rebuild)
         .Add<ZoneFlowSystem>();                            // 4th: manual invalidation (lazy rebuild)

    /// <summary>Syntax-only builder for Setup method. Never instantiated.</summary>
    private interface Builder
    {
        Builder Add<T>() where T : IDerivedSimSystem;
        Builder Add<T>(Func<SimWorld, object> dep) where T : IDerivedSimSystem;
    }
}

