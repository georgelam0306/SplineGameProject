using Friflo.Engine.ECS.Systems;
using Raylib_cs;
using Catrillion.AppState;
using Catrillion.Config;
using Catrillion.Simulation.Components;

namespace Catrillion.Rendering.Systems;

/// <summary>
/// Renders production progress bar above hovered production buildings.
/// Shows how close the building is to delivering its next resource batch.
/// </summary>
public sealed class ProductionProgressRenderSystem : BaseSystem
{
    private readonly SimWorld _simWorld;
    private readonly GameplayStore _gameplayStore;

    // Progress bar dimensions
    private const int BarWidth = 32;
    private const int BarHeight = 6;
    private const int BarOffsetY = -24; // Above building (above health bar)
    private const int TileSize = GameConfig.Map.TileSize;
    private const int DefaultCycleDuration = 300; // 5 seconds in frames

    // Colors
    private static readonly Color BarBackground = new(40, 40, 40, 200);
    private static readonly Color BarFill = new(100, 200, 255, 255); // Cyan for production
    private static readonly Color ConstructionFill = new(255, 180, 80, 255); // Orange for construction
    private static readonly Color BarBorder = Color.White;

    public ProductionProgressRenderSystem(SimWorld simWorld, GameplayStore gameplayStore)
    {
        _simWorld = simWorld;
        _gameplayStore = gameplayStore;
    }

    protected override void OnUpdateGroup()
    {
        int hoveredSlot = _gameplayStore.HoveredBuildingSlot.CurrentValue;
        if (hoveredSlot < 0) return;

        var buildings = _simWorld.BuildingRows;
        if (!buildings.TryGetRow(hoveredSlot, out var building)) return;

        // Calculate building center position (world coords)
        float centerX = (building.TileX + building.Width * 0.5f) * TileSize;
        float topY = building.TileY * TileSize;

        // Position bar above building
        int x = (int)centerX - BarWidth / 2;
        int y = (int)topY + BarOffsetY;

        // Check if building is under construction - show construction progress
        if (building.Flags.HasFlag(BuildingFlags.IsUnderConstruction))
        {
            float progress = building.ConstructionBuildTime > 0
                ? (float)building.ConstructionProgress / building.ConstructionBuildTime
                : 0;
            if (progress < 0) progress = 0;
            if (progress > 1) progress = 1;

            DrawProgressBar(x, y, progress, ConstructionFill);
            return;
        }

        // Only show for active production buildings
        if (!building.Flags.HasFlag(BuildingFlags.IsActive)) return;

        bool isProduction = building.EffectiveGeneratesGold > 0 ||
                           building.EffectiveGeneratesWood > 0 ||
                           building.EffectiveGeneratesStone > 0 ||
                           building.EffectiveGeneratesIron > 0 ||
                           building.EffectiveGeneratesOil > 0;
        if (!isProduction) return;

        // Calculate progress
        int cycleDuration = building.ProductionCycleDuration > 0
            ? building.ProductionCycleDuration
            : DefaultCycleDuration;

        float prodProgress = (float)building.ResourceAccumulator / cycleDuration;
        if (prodProgress < 0) prodProgress = 0;
        if (prodProgress > 1) prodProgress = 1;

        // Draw progress bar
        DrawProgressBar(x, y, prodProgress, BarFill);
    }

    private static void DrawProgressBar(int x, int y, float progress, Color fillColor)
    {
        // Draw background
        Raylib.DrawRectangle(x, y, BarWidth, BarHeight, BarBackground);

        // Draw fill
        int fillWidth = (int)(BarWidth * progress);
        if (fillWidth > 0)
        {
            Raylib.DrawRectangle(x, y, fillWidth, BarHeight, fillColor);
        }

        // Draw border
        Raylib.DrawRectangleLines(x, y, BarWidth, BarHeight, BarBorder);
    }
}
