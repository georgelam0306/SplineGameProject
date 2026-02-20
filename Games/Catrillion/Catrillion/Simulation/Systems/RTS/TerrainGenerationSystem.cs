using Catrillion.Simulation.Components;
using Catrillion.Simulation.Services;
using Catrillion.Simulation.Systems.SimTable;
using FlowField;

namespace Catrillion.Simulation.Systems.RTS;

/// <summary>
/// Generates procedural terrain on the first frame when game starts.
/// Runs before other spawn systems to ensure terrain is available.
/// </summary>
public sealed class TerrainGenerationSystem : SimTableSystem
{
    private readonly TerrainDataService _terrainData;
    private readonly IZoneFlowService _flowService;
    private bool _hasGenerated;

    public TerrainGenerationSystem(SimWorld world, TerrainDataService terrainData, IZoneFlowService flowService) : base(world)
    {
        _terrainData = terrainData;
        _flowService = flowService;
    }

    public override void Tick(in SimulationContext context)
    {
        // Only run once, and only when game is playing
        if (_hasGenerated) return;
        if (!World.IsPlaying()) return;

        _hasGenerated = true;

        // Generate terrain using session seed for determinism
        _terrainData.Generate(context.SessionSeed);

        // Invalidate all flow field sectors so they rebuild with terrain data
        // This ensures water/forest/mountain terrain blocks pathfinding
        _flowService.InvalidateAllFlows();
    }
}
