using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Grids;
using Catrillion.Simulation.Services;
using Catrillion.Simulation.Systems.SimTable;
using ConfigRefresh;
using Core;

namespace Catrillion.Simulation.Systems;

/// <summary>
/// Updates zombie NoiseAttraction based on nearby noise levels.
/// Iterates noise grid cells and uses spatial queries to find affected zombies.
/// Attraction radius scales with noise level - louder sounds attract from further away.
/// </summary>
[ConfigSource(typeof(MapConfigData), 0)]
public sealed partial class NoiseAttractionUpdateSystem : SimTableSystem
{
    private readonly GameDataManager<GameDocDb> _gameData;

    [CachedConfig] private Fixed64 _noiseAttractionRadiusPerUnit;
    [CachedConfig] private Fixed64 _noiseAttractionMinRadius;

    public NoiseAttractionUpdateSystem(SimWorld world, GameDataManager<GameDocDb> gameData) : base(world)
    {
        _gameData = gameData;

        // Load initial config
        ref readonly var config = ref gameData.Db.MapConfigData.FindById(0);
        _noiseAttractionRadiusPerUnit = config.NoiseAttractionRadiusPerUnit;
        _noiseAttractionMinRadius = config.NoiseAttractionMinRadius;
    }

    public override void Tick(in SimulationContext context)
    {
        // Check for hot-reload config changes
        RefreshConfigIfStale(_gameData.Generation, _gameData.Db);

        var noiseTable = World.NoiseGridStateRows;
        if (noiseTable.Count == 0) return;

        var zombies = World.ZombieRows;

        // First, clear all noise attraction (zombies not near noise should have 0)
        for (int i = 0; i < zombies.Count; i++)
        {
            var zombie = zombies.GetRowBySlot(i);
            zombie.NoiseAttraction = 0;
        }

        // Iterate noise grid - only process cells with significant noise
        for (int y = 0; y < NoiseCell.GridHeight; y++)
        {
            for (int x = 0; x < NoiseCell.GridWidth; x++)
            {
                var cell = new NoiseCell(x, y);
                Fixed64 noise = NoiseGridService.GetNoiseLevel(noiseTable, cell);

                // Skip quiet cells
                if (noise <= NoiseGridService.NoiseAttractionThreshold) continue;

                // Attraction radius scales with noise level
                Fixed64 attractionRadius = noise * _noiseAttractionRadiusPerUnit;
                if (attractionRadius < _noiseAttractionMinRadius)
                    attractionRadius = _noiseAttractionMinRadius;

                // Get cell center in world coordinates
                Fixed64Vec2 cellCenter = cell.ToPixelCenter();
                int noiseLevel = noise.ToInt();

                // Query zombies near this noise cell using spatial hash
                foreach (int slot in zombies.QueryRadius(cellCenter, attractionRadius))
                {
                    if (!zombies.TryGetRow(slot, out var zombie)) continue;
                    if (zombie.Flags.IsDead()) continue;

                    // Keep the highest noise level if zombie is near multiple sources
                    if (noiseLevel > zombie.NoiseAttraction)
                    {
                        zombie.NoiseAttraction = noiseLevel;
                    }
                }
            }
        }
    }
}
