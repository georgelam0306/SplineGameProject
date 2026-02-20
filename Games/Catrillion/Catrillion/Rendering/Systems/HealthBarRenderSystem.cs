using Friflo.Engine.ECS.Systems;
using Raylib_cs;
using Catrillion.Config;
using Catrillion.Rendering.Components;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Services;
using Core;
using SimTable;

namespace Catrillion.Rendering.Systems;

/// <summary>
/// Renders health bars above damaged entities.
/// Uses ECS Transform2D for interpolated positions.
/// Only shows health bars when Health &lt; MaxHealth (cleaner visuals).
/// </summary>
public sealed class HealthBarRenderSystem : QuerySystem<SimSlotRef, Transform2D>
{
    private readonly SimWorld _simWorld;
    private readonly int _combatUnitTableId;
    private readonly int _zombieTableId;
    private readonly int _buildingTableId;

    // Health bar dimensions - units/zombies
    private const int BarWidth = 24;
    private const int BarHeight = 4;
    private const int BarOffsetY = -16; // Above entity

    // Health bar dimensions - buildings (larger)
    private const int BuildingBarWidth = 40;
    private const int BuildingBarHeight = 5;
    private const int BuildingBarOffsetY = -20; // Above building

    public HealthBarRenderSystem(SimWorld simWorld)
    {
        _simWorld = simWorld;
        _combatUnitTableId = SimWorld.GetTableId<CombatUnitRow>();
        _zombieTableId = SimWorld.GetTableId<ZombieRow>();
        _buildingTableId = SimWorld.GetTableId<BuildingRow>();
    }

    protected override void OnUpdate()
    {
        var units = _simWorld.CombatUnitRows;
        var zombies = _simWorld.ZombieRows;
        var buildings = _simWorld.BuildingRows;
        var fogTable = _simWorld.FogOfWarGridStateRows;
        bool hasFogOfWar = fogTable.Count > 0;

        foreach (var entity in Query.Entities)
        {
            ref readonly var slotRef = ref entity.GetComponent<SimSlotRef>();
            ref readonly var transform = ref entity.GetComponent<Transform2D>();

            var handle = slotRef.Handle;
            int tableId = handle.TableId;

            if (tableId == _combatUnitTableId)
            {
                int slot = units.GetSlot(handle);
                if (slot < 0) continue;
                if (!units.TryGetRow(slot, out var unit)) continue;

                if (!unit.Flags.HasFlag(MortalFlags.IsActive)) continue;
                if (unit.Flags.HasFlag(MortalFlags.IsDead)) continue;
                if (unit.Health >= unit.MaxHealth) continue;

                if (hasFogOfWar)
                {
                    int tileX = (int)(transform.Position.X / GameConfig.Map.TileSize);
                    int tileY = (int)(transform.Position.Y / GameConfig.Map.TileSize);
                    if (!FogOfWarService.IsTileVisible(fogTable, tileX, tileY)) continue;
                }

                DrawHealthBar(
                    (int)transform.Position.X - BarWidth / 2,
                    (int)transform.Position.Y + BarOffsetY,
                    unit.Health,
                    unit.MaxHealth,
                    isPlayer: true
                );
            }
            else if (tableId == _zombieTableId)
            {
                int slot = zombies.GetSlot(handle);
                if (slot < 0) continue;
                if (!zombies.TryGetRow(slot, out var zombie)) continue;

                if (zombie.Flags.IsDead()) continue;
                if (zombie.Health >= zombie.MaxHealth) continue;

                if (hasFogOfWar)
                {
                    int tileX = (int)(transform.Position.X / GameConfig.Map.TileSize);
                    int tileY = (int)(transform.Position.Y / GameConfig.Map.TileSize);
                    if (!FogOfWarService.IsTileVisible(fogTable, tileX, tileY)) continue;
                }

                DrawHealthBar(
                    (int)transform.Position.X - BarWidth / 2,
                    (int)transform.Position.Y + BarOffsetY,
                    zombie.Health,
                    zombie.MaxHealth,
                    isPlayer: false
                );
            }
            else if (tableId == _buildingTableId)
            {
                int slot = buildings.GetSlot(handle);
                if (slot < 0) continue;
                if (!buildings.TryGetRow(slot, out var building)) continue;

                if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;
                if (building.Flags.HasFlag(BuildingFlags.IsDead)) continue;
                if (building.Health >= building.MaxHealth) continue;

                if (hasFogOfWar)
                {
                    int tileX = (int)(transform.Position.X / GameConfig.Map.TileSize);
                    int tileY = (int)(transform.Position.Y / GameConfig.Map.TileSize);
                    if (!FogOfWarService.IsTileVisible(fogTable, tileX, tileY)) continue;
                }

                DrawHealthBar(
                    (int)transform.Position.X - BuildingBarWidth / 2,
                    (int)transform.Position.Y + BuildingBarOffsetY,
                    building.Health,
                    building.MaxHealth,
                    BuildingBarWidth,
                    BuildingBarHeight,
                    isPlayer: true
                );
            }
        }
    }

    private void DrawHealthBar(int x, int y, int current, int max, bool isPlayer)
    {
        DrawHealthBar(x, y, current, max, BarWidth, BarHeight, isPlayer);
    }

    private void DrawHealthBar(int x, int y, int current, int max, int barWidth, int barHeight, bool isPlayer)
    {
        if (max <= 0) return;

        float percentage = (float)current / max;
        if (percentage < 0) percentage = 0;
        if (percentage > 1) percentage = 1;

        int fillWidth = (int)(barWidth * percentage);

        Color fillColor;
        if (percentage > 0.5f)
            fillColor = Color.Green;
        else if (percentage > 0.25f)
            fillColor = Color.Yellow;
        else
            fillColor = Color.Red;

        Color bgColor = isPlayer ? new Color(40, 40, 40, 200) : new Color(60, 20, 20, 200);

        Raylib.DrawRectangle(x, y, barWidth, barHeight, bgColor);

        if (fillWidth > 0)
            Raylib.DrawRectangle(x, y, fillWidth, barHeight, fillColor);

        Raylib.DrawRectangleLines(x, y, barWidth, barHeight, Color.Black);
    }
}
