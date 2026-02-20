using Friflo.Engine.ECS.Systems;
using Raylib_cs;
using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;

namespace Catrillion.Rendering.Systems;

/// <summary>
/// Renders resource nodes (Gold, Energy, Stone, Iron, Oil) from SimWorld.
/// Resource nodes are displayed as colored rectangles on the terrain.
/// </summary>
public sealed class ResourceNodeRenderSystem : BaseSystem
{
    private readonly SimWorld _simWorld;
    private readonly int _tileSize;

    public ResourceNodeRenderSystem(SimWorld simWorld, GameDataManager<GameDocDb> gameData)
    {
        _simWorld = simWorld;
        ref readonly var mapConfig = ref gameData.Db.MapConfigData.FindById(0);
        _tileSize = mapConfig.TileSize;
    }

    protected override void OnUpdateGroup()
    {
        var resources = _simWorld.ResourceNodeRows;

        for (int slot = 0; slot < resources.Count; slot++)
        {
            var row = resources.GetRowBySlot(slot);

            // Skip depleted resources
            if (row.Flags.HasFlag(ResourceNodeFlags.IsDepleted)) continue;

            float worldX = row.TileX * _tileSize;
            float worldY = row.TileY * _tileSize;

            // Get color based on resource type
            Color color = GetResourceColor(row.TypeId);

            Raylib.DrawRectangle((int)worldX, (int)worldY, _tileSize, _tileSize, color);

            // Draw border for visibility
            Raylib.DrawRectangleLines((int)worldX, (int)worldY, _tileSize, _tileSize, Color.Black);

            // Draw resource type abbreviation
            string abbrev = GetResourceAbbrev(row.TypeId);
            int fontSize = 10;
            int textWidth = Raylib.MeasureText(abbrev, fontSize);
            int textX = (int)(worldX + _tileSize / 2 - textWidth / 2);
            int textY = (int)(worldY + _tileSize / 2 - fontSize / 2);
            Raylib.DrawText(abbrev, textX, textY, fontSize, Color.White);
        }
    }

    private static Color GetResourceColor(ResourceTypeId typeId) => typeId switch
    {
        ResourceTypeId.Gold => new Color(255, 215, 0, 255),    // Gold
        ResourceTypeId.Stone => new Color(128, 128, 128, 255), // Gray
        ResourceTypeId.Iron => new Color(100, 100, 140, 255),  // Steel blue
        ResourceTypeId.Oil => new Color(30, 30, 30, 255),      // Black
        _ => new Color(100, 100, 100, 255)                     // Default gray
    };

    private static string GetResourceAbbrev(ResourceTypeId typeId) => typeId switch
    {
        ResourceTypeId.Gold => "Au",
        ResourceTypeId.Stone => "St",
        ResourceTypeId.Iron => "Fe",
        ResourceTypeId.Oil => "Oil",
        _ => "?"
    };
}
