using Catrillion.Simulation.Components;
using FlowField;

namespace Catrillion.Simulation.Systems.SimTable;

/// <summary>
/// Flushes pending flow field invalidations.
/// Run this after building systems and before movement systems
/// to ensure flow fields are up-to-date when pathfinding runs.
/// </summary>
public sealed class FlowFieldInvalidationSystem : SimTableSystem
{
    private readonly IZoneFlowService _flowService;

    public FlowFieldInvalidationSystem(SimWorld world, IZoneFlowService flowService)
        : base(world)
    {
        _flowService = flowService;
    }

    public override void Tick(in SimulationContext context)
    {
        _flowService.FlushPendingInvalidations();
    }
}
