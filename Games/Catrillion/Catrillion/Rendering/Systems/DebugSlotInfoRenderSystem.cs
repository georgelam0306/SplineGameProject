using System;
using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;
using Raylib_cs;
using Catrillion.Rendering.Components;
using Catrillion.Simulation;
using Catrillion.Simulation.Components;
using SimTable;
using Core;

namespace Catrillion.Rendering.Systems;

/// <summary>
/// DEBUG: Renders slot/stableId info overlay on entities with SimSlotRef.
/// Helps diagnose orphan detection issues by showing what each entity thinks its ID is.
/// RTS mode: tracks CombatUnits instead of old UnitRow.
/// </summary>
public sealed class DebugSlotInfoRenderSystem : QuerySystem<SimSlotRef, Transform2D>
{
    private readonly SimWorld _simWorld;
    private readonly int _combatUnitTableId;

    public DebugSlotInfoRenderSystem(SimWorld simWorld)
    {
        _simWorld = simWorld;
        _combatUnitTableId = SimWorld.GetTableId<CombatUnitRow>();
    }

    protected override void OnUpdate()
    {
#if DEBUG
        var units = _simWorld.CombatUnitRows;

        // Count Friflo entities vs SimWorld units
        int frifloUnitCount = 0;
        foreach (var e in Query.Entities)
        {
            ref readonly var sr = ref e.GetComponent<SimSlotRef>();
            if (sr.Handle.TableId == _combatUnitTableId) frifloUnitCount++;
        }
        int simWorldUnitCount = units.Count;

        // Draw count comparison at top of screen
        string countText = $"Friflo: {frifloUnitCount} | SimWorld: {simWorldUnitCount}";
        Color countColor = frifloUnitCount == simWorldUnitCount ? Color.Green : Color.Red;
        Raylib.DrawText(countText, 10, 50, 20, countColor);

        foreach (var entity in Query.Entities)
        {
            ref readonly var slotRef = ref entity.GetComponent<SimSlotRef>();
            ref readonly var transform = ref entity.GetComponent<Transform2D>();

            // Skip offscreen entities
            if (transform.Position.X < -5000f || transform.Position.Y < -5000f)
                continue;

            // Only show for combat unit entities
            var handle = slotRef.Handle;
            if (handle.TableId != _combatUnitTableId)
                continue;

            int actualSlot = units.GetSlot(handle);

            // Build debug text
            string debugText;
            Color textColor;

            if (actualSlot < 0)
            {
                debugText = $"ORPHAN! rawId:{handle.RawId} gen:{handle.Generation}";
                textColor = Color.Red;
            }
            else if (!units.TryGetRow(actualSlot, out var unit))
            {
                debugText = $"INVALID! rawId:{handle.RawId}";
                textColor = Color.Red;
            }
            else
            {
                bool isActive = unit.Flags.HasFlag(MortalFlags.IsActive);
                if (!isActive)
                {
                    debugText = $"INACTIVE! rawId:{handle.RawId}";
                    textColor = Color.Yellow;
                }
                else
                {
                    // Valid entity - check if position matches SimWorld
                    float simX = unit.Position.X.ToFloat();
                    float simY = unit.Position.Y.ToFloat();
                    float deltaX = MathF.Abs(transform.Position.X - simX);
                    float deltaY = MathF.Abs(transform.Position.Y - simY);

                    if (deltaX > 1f || deltaY > 1f)
                    {
                        debugText = $"DESYNC! rawId:{handle.RawId} d:{deltaX:F0},{deltaY:F0}";
                        textColor = Color.SkyBlue;
                    }
                    else
                    {
                        debugText = $"rawId:{handle.RawId} grp:{unit.GroupId}";
                        textColor = Color.Green;
                    }
                }
            }

            // Draw text above the entity
            int textX = (int)transform.Position.X - Raylib.MeasureText(debugText, 10) / 2;
            int textY = (int)transform.Position.Y - 30;

            // Draw background for readability
            Raylib.DrawRectangle(textX - 2, textY - 2, Raylib.MeasureText(debugText, 10) + 4, 14, new Color((byte)0, (byte)0, (byte)0, (byte)180));
            Raylib.DrawText(debugText, textX, textY, 10, textColor);
        }
#endif
    }
}
