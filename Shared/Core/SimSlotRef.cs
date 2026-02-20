using Friflo.Engine.ECS;
using SimTable;

namespace Core;

/// <summary>
/// Links a Friflo ECS entity to a SimWorld table row via SimHandle.
/// Used by EndFrame() to sync positions from simulation to rendering.
/// </summary>
public struct SimSlotRef : IComponent
{
    /// <summary>
    /// Handle to the SimWorld entity (includes TableId and generation-validated StableId).
    /// </summary>
    public SimHandle Handle;
}
