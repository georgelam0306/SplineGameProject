using Friflo.Engine.ECS.Systems;
using Raylib_cs;
using Catrillion.Config;
using Catrillion.Rendering.Services;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Services;

namespace Catrillion.Rendering.Systems;

/// <summary>
/// Renders GARRISONED combat units on top of buildings.
/// Non-garrisoned units are rendered via SpriteRenderSystem.
/// Must run AFTER BuildingRenderSystem for correct z-order.
/// </summary>
public sealed class CombatUnitRenderSystem : BaseSystem
{
    private readonly SimWorld _simWorld;
    private const float UnitRadius = 8f;
    private const float SelectionRingRadius = 12f;

    public CombatUnitRenderSystem(SimWorld simWorld)
    {
        _simWorld = simWorld;
    }

    protected override void OnUpdateGroup()
    {
        var units = _simWorld.CombatUnitRows;
        var buildings = _simWorld.BuildingRows;
        var fogTable = _simWorld.FogOfWarGridStateRows;
        bool hasFogOfWar = fogTable.Count > 0;

        for (int slot = 0; slot < units.Count; slot++)
        {
            if (!units.TryGetRow(slot, out var unit)) continue;
            if (!unit.Flags.HasFlag(MortalFlags.IsActive)) continue;

            // Only render garrisoned units - non-garrisoned are rendered via SpriteRenderSystem
            if (!unit.GarrisonedInHandle.IsValid) continue;

            // Get building this unit is garrisoned in
            int buildingSlot = buildings.GetSlot(unit.GarrisonedInHandle);
            if (!buildings.TryGetRow(buildingSlot, out var building)) continue;

            // Fog of war visibility check: only show units in currently visible tiles
            // (Buildings show in fogged areas, but units inside them are hidden)
            if (hasFogOfWar && !IsBuildingVisible(fogTable, building.TileX, building.TileY, building.Width, building.Height))
            {
                continue;
            }

            // Find unit's index in garrison for positioning
            var unitHandle = units.GetHandle(slot);
            int garrisonIndex = building.FindUnitInGarrison(unitHandle);
            if (garrisonIndex < 0) garrisonIndex = 0; // Fallback

            // Position units in a row at building center
            float buildingCenterX = (building.TileX + building.Width / 2f) * 32f;
            float buildingCenterY = (building.TileY + building.Height / 2f) * 32f;
            float offsetX = (garrisonIndex - (building.GarrisonCount - 1) / 2f) * 16f;
            System.Numerics.Vector2 pos = new System.Numerics.Vector2(buildingCenterX + offsetX, buildingCenterY);

            // Draw selection ring if selected (player-colored for identification)
            if (unit.SelectedByPlayerId >= 0)
            {
                var selectionColor = PlayerColorService.GetPlayerColor(unit.SelectedByPlayerId);
                Raylib.DrawCircleLines((int)pos.X, (int)pos.Y, SelectionRingRadius, selectionColor);
            }

            // Determine unit color based on ownership
            Color unitColor;
            if (unit.OwnerPlayerId == 255)
            {
                // Enemy unit - red
                unitColor = Color.Red;
            }
            else if (unit.OwnerPlayerId == 0)
            {
                // Player 0 - blue
                unitColor = Color.Blue;
            }
            else if (unit.OwnerPlayerId == 1)
            {
                // Player 1 - cyan
                unitColor = Color.SkyBlue;
            }
            else
            {
                // Other players - purple
                unitColor = Color.Purple;
            }

            // Draw unit circle
            Raylib.DrawCircle((int)pos.X, (int)pos.Y, UnitRadius, unitColor);

            // Draw velocity indicator if moving
            if (unit.Velocity.X != Fixed64.Zero || unit.Velocity.Y != Fixed64.Zero)
            {
                var vel = unit.Velocity.ToVector2() * 5f;
                Raylib.DrawLine((int)pos.X, (int)pos.Y, (int)(pos.X + vel.X), (int)(pos.Y + vel.Y), Color.Yellow);
            }
        }
    }

    /// <summary>
    /// Checks if any tile of the building footprint is currently visible.
    /// Units are only shown in currently visible areas (not fogged).
    /// </summary>
    private static bool IsBuildingVisible(FogOfWarGridStateRowTable fogTable, int tileX, int tileY, int width, int height)
    {
        for (int ty = tileY; ty < tileY + height; ty++)
        {
            for (int tx = tileX; tx < tileX + width; tx++)
            {
                if (FogOfWarService.IsTileVisible(fogTable, tx, ty))
                {
                    return true;
                }
            }
        }
        return false;
    }
}
