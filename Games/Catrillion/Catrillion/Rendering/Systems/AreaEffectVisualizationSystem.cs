using System;
using System.Numerics;
using Friflo.Engine.ECS.Systems;
using Raylib_cs;
using Catrillion.AppState;
using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;
using Core;

namespace Catrillion.Rendering.Systems;

/// <summary>
/// Renders area effect visualization:
/// - Dashed circle showing effect radius on preview/hover/select
/// - Boost icon above buildings receiving area bonuses
/// </summary>
public sealed class AreaEffectVisualizationSystem : BaseSystem
{
    private readonly GameplayStore _gameplayStore;
    private readonly SimWorld _simWorld;
    private readonly GameDataManager<GameDocDb> _gameData;
    private readonly int _tileSize;

    // Pre-allocated buffer for affected building slots (no allocation)
    private readonly int[] _affectedSlots = new int[100];
    private int _affectedCount;

    // Dashed circle config
    private const int DashSegments = 32;
    private static readonly Color RadiusColor = new(100, 200, 255, 180); // Cyan
    private static readonly Color PreviewRadiusColor = new(100, 255, 100, 150); // Green
    private static readonly Color IconColor = new(100, 255, 100, 220); // Green boost

    public AreaEffectVisualizationSystem(
        GameplayStore gameplayStore,
        SimWorld simWorld,
        GameDataManager<GameDocDb> gameData)
    {
        _gameplayStore = gameplayStore;
        _simWorld = simWorld;
        _gameData = gameData;

        ref readonly var mapConfig = ref gameData.Db.MapConfigData.FindById(0);
        _tileSize = mapConfig.TileSize;
    }

    protected override void OnUpdateGroup()
    {
        // Priority: Preview > Hover > Selected
        if (RenderPreviewRadius()) return;
        if (RenderHoveredBuildingRadius()) return;
        RenderSelectedBuildingRadius();
    }

    private bool RenderPreviewRadius()
    {
        if (!_gameplayStore.IsInBuildMode.CurrentValue) return false;

        var previewPos = _gameplayStore.PlacementPreview.CurrentValue;
        if (!previewPos.HasValue) return false;

        var buildingType = _gameplayStore.BuildModeType.CurrentValue;
        if (!buildingType.HasValue) return false;

        ref readonly var typeData = ref _gameData.Db.BuildingTypeData.FindById((int)buildingType.Value);
        if (typeData.EffectRadius <= Fixed64.Zero) return false;

        // Calculate center position from tile coords
        float centerX = (previewPos.Value.tileX + typeData.Width * 0.5f) * _tileSize;
        float centerY = (previewPos.Value.tileY + typeData.Height * 0.5f) * _tileSize;
        float radius = typeData.EffectRadius.ToFloat();

        DrawDashedCircle(centerX, centerY, radius, PreviewRadiusColor);

        // Show which existing buildings would be affected
        // For preview, use player 0 (shared base co-op)
        var sourcePos = new Fixed64Vec2(
            Fixed64.FromInt((int)centerX),
            Fixed64.FromInt((int)centerY));
        FindAffectedBuildings(
            sourcePos,
            typeData.EffectRadius,
            0,
            -1,
            typeData.AreaGoldBonus,
            typeData.AreaWoodBonus,
            typeData.AreaStoneBonus,
            typeData.AreaIronBonus,
            typeData.AreaOilBonus);
        RenderAffectedBuildingIcons();

        return true;
    }

    private bool RenderHoveredBuildingRadius()
    {
        int hoveredSlot = _gameplayStore.HoveredBuildingSlot.CurrentValue;
        if (hoveredSlot < 0) return false;

        var buildings = _simWorld.BuildingRows;
        if (!buildings.TryGetRow(hoveredSlot, out var building)) return false;
        if (building.EffectRadius <= Fixed64.Zero) return false;

        // Draw radius circle at building center
        float centerX = building.Position.X.ToFloat();
        float centerY = building.Position.Y.ToFloat();
        float radius = building.EffectRadius.ToFloat();

        DrawDashedCircle(centerX, centerY, radius, RadiusColor);

        // Find and highlight affected buildings
        FindAffectedBuildings(
            building.Position,
            building.EffectRadius,
            building.OwnerPlayerId,
            hoveredSlot,
            building.AreaGoldBonus,
            building.AreaWoodBonus,
            building.AreaStoneBonus,
            building.AreaIronBonus,
            building.AreaOilBonus);
        RenderAffectedBuildingIcons();

        return true;
    }

    private void RenderSelectedBuildingRadius()
    {
        var buildings = _simWorld.BuildingRows;

        // Find first selected building with effect radius
        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var building)) continue;
            if (building.SelectedByPlayerId < 0) continue;
            if (building.EffectRadius <= Fixed64.Zero) continue;
            if (building.Flags.IsDead()) continue;

            // Draw radius circle at building center
            float centerX = building.Position.X.ToFloat();
            float centerY = building.Position.Y.ToFloat();
            float radius = building.EffectRadius.ToFloat();

            DrawDashedCircle(centerX, centerY, radius, RadiusColor);

            // Find and highlight affected buildings
            FindAffectedBuildings(
                building.Position,
                building.EffectRadius,
                building.OwnerPlayerId,
                slot,
                building.AreaGoldBonus,
                building.AreaWoodBonus,
                building.AreaStoneBonus,
                building.AreaIronBonus,
                building.AreaOilBonus);
            RenderAffectedBuildingIcons();

            // Only show for first selected building with effect radius
            return;
        }
    }

    private void DrawDashedCircle(float centerX, float centerY, float radius, Color color)
    {
        const float anglePerSegment = MathF.PI * 2f / DashSegments;

        for (int i = 0; i < DashSegments; i++)
        {
            if (i % 2 != 0) continue; // Skip gaps (odd segments)

            float angle = i * anglePerSegment;
            float startX = centerX + MathF.Cos(angle) * radius;
            float startY = centerY + MathF.Sin(angle) * radius;
            float endX = centerX + MathF.Cos(angle + anglePerSegment) * radius;
            float endY = centerY + MathF.Sin(angle + anglePerSegment) * radius;

            Raylib.DrawLine((int)startX, (int)startY, (int)endX, (int)endY, color);
        }
    }

    private void FindAffectedBuildings(
        Fixed64Vec2 sourcePos,
        Fixed64 radius,
        byte ownerPlayerId,
        int excludeSlot,
        Fixed64 areaGoldBonus,
        Fixed64 areaWoodBonus,
        Fixed64 areaStoneBonus,
        Fixed64 areaIronBonus,
        Fixed64 areaOilBonus)
    {
        _affectedCount = 0;
        var buildings = _simWorld.BuildingRows;

        // Use spatial query - zero-allocation, uses spatial hash for O(1) chunk lookup
        foreach (int slot in buildings.QueryRadius(sourcePos, radius))
        {
            if (_affectedCount >= _affectedSlots.Length) break;
            if (slot == excludeSlot) continue;

            var building = buildings.GetRowBySlot(slot);
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;
            if (building.Flags.IsDead()) continue;
            if (building.OwnerPlayerId != ownerPlayerId) continue;

            // Only include buildings that generate the specific resources being boosted
            bool isAffected =
                (areaGoldBonus > Fixed64.Zero && building.GeneratesGold > 0) ||
                (areaWoodBonus > Fixed64.Zero && building.GeneratesWood > 0) ||
                (areaStoneBonus > Fixed64.Zero && building.GeneratesStone > 0) ||
                (areaIronBonus > Fixed64.Zero && building.GeneratesIron > 0) ||
                (areaOilBonus > Fixed64.Zero && building.GeneratesOil > 0);
            if (!isAffected) continue;

            _affectedSlots[_affectedCount++] = slot;
        }
    }

    private void RenderAffectedBuildingIcons()
    {
        var buildings = _simWorld.BuildingRows;
        const int iconSize = 10;

        for (int i = 0; i < _affectedCount; i++)
        {
            var building = buildings.GetRowBySlot(_affectedSlots[i]);

            // Center above building
            float centerX = (building.TileX + building.Width * 0.5f) * _tileSize;
            float topY = building.TileY * _tileSize - 16;

            // Draw upward arrow (triangle) - boost indicator
            Raylib.DrawTriangle(
                new Vector2(centerX, topY - iconSize),           // Top
                new Vector2(centerX - iconSize / 2f, topY),      // Bottom-left
                new Vector2(centerX + iconSize / 2f, topY),      // Bottom-right
                IconColor
            );
        }
    }
}
