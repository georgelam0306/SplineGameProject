using Catrillion.Simulation.Components;
using Catrillion.Simulation.Grids;
using Catrillion.Simulation.Services;
using Catrillion.Simulation.Systems.SimTable;
using Core;

namespace Catrillion.Simulation.Systems;

/// <summary>
/// Decays noise levels across the grid.
/// All noise cells gradually return to zero over time.
/// </summary>
[TickRate(interval: 2, offset: 1)]
public sealed class NoiseDecaySystem : SimTableSystem
{
    public NoiseDecaySystem(SimWorld world) : base(world)
    {
    }

    public override void Tick(in SimulationContext context)
    {
        var noiseTable = World.NoiseGridStateRows;
        if (noiseTable.Count == 0) return;

        // Compute effective decay for this tick (rate per second * delta seconds)
        Fixed64 noiseDecay = NoiseGridService.DecayRatePerSecond * DeltaSeconds;

        // Decay all cells in the grid
        for (int y = 0; y < NoiseCell.GridHeight; y++)
        {
            var rowSpan = NoiseGridService.GetRowSpan(noiseTable, y);
            for (int x = 0; x < NoiseCell.GridWidth; x++)
            {
                Fixed64 current = rowSpan[x];
                if (current > Fixed64.Zero)
                {
                    Fixed64 decayed = current - noiseDecay;
                    if (decayed < Fixed64.Zero) decayed = Fixed64.Zero;
                    rowSpan[x] = decayed;
                }
            }
        }
    }
}
