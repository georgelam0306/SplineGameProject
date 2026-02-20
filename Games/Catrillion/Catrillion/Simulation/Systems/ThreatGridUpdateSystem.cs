using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Grids;
using Catrillion.Simulation.Services;
using Catrillion.Simulation.Systems.SimTable;
using Core;

namespace Catrillion.Simulation.Systems;

/// <summary>
/// Updates threat grid from various sources:
/// - Player proximity (continuous threat emission)
/// - Combat unit positions (friendly units create threat)
/// - Building positions (structures attract zombies)
/// - Noise grid spillover (noise contributes to threat)
/// Also updates noise grid from buildings and combat units.
/// </summary>
public sealed class ThreatGridUpdateSystem : SimTableSystem
{
    private readonly Fixed64 _noiseSpilloverMultiplier;

    public ThreatGridUpdateSystem(SimWorld world, GameDataManager<GameDocDb> gameData) : base(world)
    {
        ref readonly var mapConfig = ref gameData.Db.MapConfigData.FindById(0);
        _noiseSpilloverMultiplier = mapConfig.NoiseSpilloverMultiplier;
    }

    public override void Tick(in SimulationContext context)
    {
        var threatTable = World.ThreatGridStateRows;
        if (threatTable.Count == 0) return;

        // Clear noise grid - noise is recalculated fresh each tick from current sources
        var noiseTable = World.NoiseGridStateRows;
        if (noiseTable.Count > 0)
        {
            NoiseGridService.ClearGrid(noiseTable);
        }

        // Add threat from combat units (RTS mode - no direct player control)
        AddThreatFromCombatUnits(threatTable);

        // Add threat from buildings (structures attract zombies)
        AddThreatFromBuildings(threatTable);

        // Spillover from noise grid
        AddThreatFromNoise(threatTable);
    }

    private void AddThreatFromCombatUnits(ThreatGridStateRowTable threatTable)
    {
        var combatUnits = World.CombatUnitRows;
        var noiseTable = World.NoiseGridStateRows;
        bool hasNoiseGrid = noiseTable.Count > 0;

        for (int slot = 0; slot < combatUnits.Count; slot++)
        {
            var unit = combatUnits.GetRowBySlot(slot);
            if (unit.Flags.IsDead()) continue;

            // Use SetThreatMax - threat represents current sources, not accumulated history
            // ThreatLevel is loaded from UnitTypeData via CachedStat
            if (unit.ThreatLevel > 0)
            {
                ThreatGridService.SetThreatMax(
                    threatTable,
                    unit.Position,
                    Fixed64.FromInt(unit.ThreatLevel)
                );
            }

            // Combat units also generate noise (NoiseLevel from UnitTypeData)
            if (hasNoiseGrid && unit.NoiseLevel > 0)
            {
                NoiseGridService.AddNoise(
                    noiseTable,
                    unit.Position,
                    Fixed64.FromInt(unit.NoiseLevel)
                );
            }
        }
    }

    private void AddThreatFromBuildings(ThreatGridStateRowTable threatTable)
    {
        var buildings = World.BuildingRows;
        var noiseTable = World.NoiseGridStateRows;
        bool hasNoiseGrid = noiseTable.Count > 0;

        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var building)) continue;
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;
            if (building.Health <= 0) continue;

            // Buildings generate threat (non-accumulating - disappears when building dies)
            // ThreatLevel is loaded from BuildingTypeData via CachedStat
            if (building.ThreatLevel > 0)
            {
                ThreatGridService.SetThreatMax(
                    threatTable,
                    building.Position,
                    Fixed64.FromInt(building.ThreatLevel)
                );
            }

            // Buildings also generate noise (NoiseLevel from BuildingTypeData)
            if (hasNoiseGrid && building.NoiseLevel > 0)
            {
                NoiseGridService.AddNoise(
                    noiseTable,
                    building.Position,
                    Fixed64.FromInt(building.NoiseLevel)
                );
            }
        }
    }

    private void AddThreatFromNoise(ThreatGridStateRowTable threatTable)
    {
        var noiseTable = World.NoiseGridStateRows;
        if (noiseTable.Count == 0) return;

        // Noise grid is 32x32 (256px cells), threat grid is 64x64 (128px cells)
        // Each noise cell covers 2x2 threat cells (NoiseCell.ToThreatCell gives top-left)
        const int threatCellsPerNoiseCell = NoiseCell.CellSizePixels / ThreatCell.CellSizePixels;

        for (int noiseY = 0; noiseY < NoiseCell.GridHeight; noiseY++)
        {
            for (int noiseX = 0; noiseX < NoiseCell.GridWidth; noiseX++)
            {
                var noiseCell = new NoiseCell(noiseX, noiseY);
                Fixed64 noise = NoiseGridService.GetNoiseLevel(noiseTable, noiseCell);
                if (noise <= Fixed64.Zero) continue;

                Fixed64 spillover = noise * _noiseSpilloverMultiplier;

                // Convert noise cell to threat cell coordinates and spread across threat cells
                var threatBase = noiseCell.ToThreatCell();

                for (int dy = 0; dy < threatCellsPerNoiseCell; dy++)
                {
                    for (int dx = 0; dx < threatCellsPerNoiseCell; dx++)
                    {
                        var threatCell = new ThreatCell(threatBase.X + dx, threatBase.Y + dy);

                        // Use max semantics - noise spillover sets a floor, not accumulating
                        Fixed64 currentThreat = ThreatGridService.GetThreatLevel(threatTable, threatCell);
                        if (spillover > currentThreat)
                        {
                            ThreatGridService.SetThreatLevel(threatTable, threatCell, spillover);

                            // Update peak if needed
                            Fixed64 peak = ThreatGridService.GetPeakThreatLevel(threatTable, threatCell);
                            if (spillover > peak)
                            {
                                ThreatGridService.SetPeakThreatLevel(threatTable, threatCell, spillover);
                            }
                        }
                    }
                }
            }
        }
    }
}
