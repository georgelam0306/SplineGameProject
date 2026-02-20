using Friflo.Engine.ECS.Systems;
using Raylib_cs;
using Catrillion.Config;
using Catrillion.Rendering.Components;
using Catrillion.Rendering.Services;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Services;
using Core;
using SimTable;

namespace Catrillion.Rendering.Systems;

/// <summary>
/// Renders selection circles under selected combat units.
/// Uses ECS Transform2D for interpolated positions.
/// Runs BEFORE SpriteRenderSystem so circles appear underneath sprites.
/// </summary>
public sealed class SelectionCircleRenderSystem : QuerySystem<SimSlotRef, Transform2D>
{
    private readonly SimWorld _simWorld;
    private readonly int _combatUnitTableId;
    private const float SelectionRingRadius = 12f;
    private const float SpriteOffset = 8f;  // Half of 32x32 sprite

    public SelectionCircleRenderSystem(SimWorld simWorld)
    {
        _simWorld = simWorld;
        _combatUnitTableId = SimWorld.GetTableId<CombatUnitRow>();
    }

    protected override void OnUpdate()
    {
        var units = _simWorld.CombatUnitRows;
        var fogTable = _simWorld.FogOfWarGridStateRows;
        bool hasFogOfWar = fogTable.Count > 0;

        foreach (var entity in Query.Entities)
        {
            ref readonly var slotRef = ref entity.GetComponent<SimSlotRef>();
            ref readonly var transform = ref entity.GetComponent<Transform2D>();

            // Only process CombatUnit entities
            if (slotRef.Handle.TableId != _combatUnitTableId) continue;

            int slot = units.GetSlot(slotRef.Handle);
            if (slot < 0) continue;
            if (!units.TryGetRow(slot, out var unit)) continue;

            if (!unit.Flags.HasFlag(MortalFlags.IsActive)) continue;
            if (unit.Flags.HasFlag(MortalFlags.IsDead)) continue;

            // Skip garrisoned units - they're rendered by CombatUnitRenderSystem
            if (unit.GarrisonedInHandle.IsValid) continue;

            // Only draw for selected units
            if (unit.SelectedByPlayerId < 0) continue;

            // Fog of war: only show circles for units in visible tiles
            if (hasFogOfWar)
            {
                int tileX = (int)(transform.Position.X / GameConfig.Map.TileSize);
                int tileY = (int)(transform.Position.Y / GameConfig.Map.TileSize);
                if (!FogOfWarService.IsTileVisible(fogTable, tileX, tileY))
                {
                    continue;
                }
            }

            // Use interpolated position from Transform2D
            float centerX = transform.Position.X + SpriteOffset;
            float centerY = transform.Position.Y + SpriteOffset;

            // Use SelectedByPlayerId for color (matches garrisoned unit behavior)
            var playerColor = PlayerColorService.GetPlayerColor(unit.SelectedByPlayerId);

            // Draw selection circle ring
            Raylib.DrawCircleLines((int)centerX, (int)centerY, SelectionRingRadius, playerColor);
        }
    }
}
