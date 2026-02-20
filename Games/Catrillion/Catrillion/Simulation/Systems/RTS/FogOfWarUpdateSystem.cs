using Catrillion.Config;
using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Services;
using Catrillion.Simulation.Systems.SimTable;
using Core;

namespace Catrillion.Simulation.Systems.RTS;

/// <summary>
/// Updates fog of war visibility.
/// - Decays visible tiles to fogged
/// - Reveals tiles around friendly units and buildings based on SightRange
/// </summary>
[TickRate(interval: 2)]
public sealed class FogOfWarUpdateSystem : SimTableSystem
{
    private readonly int _tileSize;

    public FogOfWarUpdateSystem(SimWorld world, GameDataManager<GameDocDb> gameData) : base(world)
    {
        _tileSize = GameConfig.Map.TileSize;
    }

    public override void Initialize()
    {
        // Initialize the fog grid if not already done
        var fogTable = World.FogOfWarGridStateRows;
        if (fogTable.Count == 0)
        {
            FogOfWarService.Initialize(fogTable);
        }
    }

    public override void Tick(in SimulationContext context)
    {
        var fogTable = World.FogOfWarGridStateRows;
        if (fogTable.Count == 0)
        {
            FogOfWarService.Initialize(fogTable);
        }

        // Step 1: Decay all visible → fogged
        FogOfWarService.DecayVisibility(fogTable);

        // Step 2: Reveal from friendly units
        RevealFromUnits(fogTable);

        // Step 3: Reveal from friendly buildings
        RevealFromBuildings(fogTable);
    }

    private void RevealFromUnits(FogOfWarGridStateRowTable fogTable)
    {
        var units = World.CombatUnitRows;

        for (int slot = 0; slot < units.Count; slot++)
        {
            var unit = units.GetRowBySlot(slot);

            // Skip dead units
            if (unit.Flags.IsDead()) continue;

            // Skip units with no sight range
            if (unit.SightRange <= 0) continue;

            // TODO: Filter by player/team if needed for competitive modes
            // For now, all player units reveal fog (co-op shared vision)

            // Convert world position to tile coordinates
            int tileX = (int)(unit.Position.X.ToFloat() / _tileSize);
            int tileY = (int)(unit.Position.Y.ToFloat() / _tileSize);

            FogOfWarService.RevealRadius(fogTable, tileX, tileY, unit.SightRange);
        }
    }

    private void RevealFromBuildings(FogOfWarGridStateRowTable fogTable)
    {
        var buildings = World.BuildingRows;

        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var building)) continue;

            // Skip inactive or dead buildings
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;
            if (building.Health <= 0) continue;

            // Skip buildings with no sight range
            if (building.SightRange <= 0) continue;

            // Use center of building for vision origin
            int centerTileX = building.TileX + building.Width / 2;
            int centerTileY = building.TileY + building.Height / 2;

            FogOfWarService.RevealRadius(fogTable, centerTileX, centerTileY, building.SightRange);
        }
    }
}
