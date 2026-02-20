using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;
using Catrillion.Config;
using Catrillion.Rendering.Components;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Services;
using Core;

namespace Catrillion.Rendering.Systems;

/// <summary>
/// Syncs SpriteRenderer.IsVisible based on fog of war visibility.
/// Entities in unexplored or fogged tiles are hidden.
/// </summary>
public sealed class FogOfWarVisibilitySyncSystem : QuerySystem<SimSlotRef, Transform2D, SpriteRenderer>
{
    private readonly SimWorld _simWorld;

    public FogOfWarVisibilitySyncSystem(SimWorld simWorld)
    {
        _simWorld = simWorld;
    }

    protected override void OnUpdate()
    {
        var fogTable = _simWorld.FogOfWarGridStateRows;
        bool hasFogOfWar = fogTable.Count > 0 && !GameConfig.Debug.DisableFogOfWar;

        // If no fog of war or disabled, all sprites are visible
        if (!hasFogOfWar)
        {
            foreach (var entity in Query.Entities)
            {
                ref var sprite = ref entity.GetComponent<SpriteRenderer>();
                sprite.IsVisible = true;
            }
            return;
        }

        // Check visibility based on fog of war
        foreach (var entity in Query.Entities)
        {
            ref readonly var transform = ref entity.GetComponent<Transform2D>();
            ref var sprite = ref entity.GetComponent<SpriteRenderer>();

            int tileX = (int)(transform.Position.X / GameConfig.Map.TileSize);
            int tileY = (int)(transform.Position.Y / GameConfig.Map.TileSize);

            sprite.IsVisible = FogOfWarService.IsTileVisible(fogTable, tileX, tileY);
        }
    }
}
