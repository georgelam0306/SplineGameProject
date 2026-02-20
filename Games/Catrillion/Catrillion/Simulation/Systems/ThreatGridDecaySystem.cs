using Catrillion.Simulation.Components;
using Catrillion.Simulation.Grids;
using Catrillion.Simulation.Services;
using Catrillion.Simulation.Systems.SimTable;
using Core;

namespace Catrillion.Simulation.Systems;

/// <summary>
/// Decays threat levels across the grid.
/// Both current threat and peak threat decay, but at different rates.
/// Peak threat decays slower, giving zombies "memory" of where threats were.
/// </summary>
[TickRate(interval: 2)]
public sealed class ThreatGridDecaySystem : SimTableSystem
{
    public ThreatGridDecaySystem(SimWorld world) : base(world)
    {
    }

    public override void Tick(in SimulationContext context)
    {
        var threatTable = World.ThreatGridStateRows;
        if (threatTable.Count == 0) return;

        // Compute effective decay for this tick (rate per second * delta seconds)
        Fixed64 threatDecay = ThreatGridService.ThreatDecayRatePerSecond * DeltaSeconds;
        Fixed64 peakDecay = ThreatGridService.PeakThreatDecayRatePerSecond * DeltaSeconds;

        // Decay all cells in the threat grid
        for (int y = 0; y < ThreatCell.GridHeight; y++)
        {
            var threatRow = ThreatGridService.GetThreatRowSpan(threatTable, y);
            var peakRow = ThreatGridService.GetPeakThreatRowSpan(threatTable, y);

            for (int x = 0; x < ThreatCell.GridWidth; x++)
            {
                // Decay current threat
                Fixed64 current = threatRow[x];
                if (current > Fixed64.Zero)
                {
                    Fixed64 decayed = current - threatDecay;
                    if (decayed < Fixed64.Zero) decayed = Fixed64.Zero;
                    threatRow[x] = decayed;
                }

                // Decay peak threat (slower)
                Fixed64 peak = peakRow[x];
                if (peak > Fixed64.Zero)
                {
                    Fixed64 decayedPeak = peak - peakDecay;
                    if (decayedPeak < Fixed64.Zero) decayedPeak = Fixed64.Zero;
                    peakRow[x] = decayedPeak;
                }
            }
        }
    }
}
