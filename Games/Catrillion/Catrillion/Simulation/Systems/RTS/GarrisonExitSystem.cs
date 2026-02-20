using Catrillion.Simulation.Components;
using Core;
using SimTable;

namespace Catrillion.Simulation.Systems.SimTable;

/// <summary>
/// Processes exit garrison commands from input.
/// Supports both ejecting all units from selected buildings and ejecting single units.
/// </summary>
public sealed class GarrisonExitSystem : SimTableSystem
{
    // Exit radius from building center
    private static readonly Fixed64 ExitRadius = Fixed64.FromInt(48);

    // Precomputed angle step (2*PI / 6 ≈ 1.047)
    private static readonly Fixed64 AngleStep = Fixed64.Pi / Fixed64.FromInt(3);

    public GarrisonExitSystem(SimWorld world) : base(world)
    {
    }

    public override void Tick(in SimulationContext context)
    {
        // Commands not processed during countdown
        if (!World.IsPlaying()) return;

        var buildings = World.BuildingRows;
        var units = World.CombatUnitRows;

        for (int playerId = 0; playerId < context.PlayerCount; playerId++)
        {
            ref readonly var input = ref context.GetInput(playerId);

            // Handle "eject all" command
            if (input.HasExitGarrisonCommand)
            {
                // Find selected buildings for this player
                for (int slot = 0; slot < buildings.Count; slot++)
                {
                    if (!buildings.TryGetRow(slot, out var building)) continue;
                    if (building.SelectedByPlayerId != playerId) continue;
                    if (building.GarrisonCount == 0) continue;

                    EjectAllUnits(ref building, units);
                }
            }

            // Handle single unit eject
            if (input.SingleEjectUnitHandle.IsValid)
            {
                EjectSingleUnit(input.SingleEjectUnitHandle, buildings, units);
            }
        }
    }

    private void EjectSingleUnit(SimHandle unitHandle, BuildingRowTable buildings, CombatUnitRowTable units)
    {
        // Find the unit
        int unitSlot = units.GetSlot(unitHandle);
        if (!units.TryGetRow(unitSlot, out var unit)) return;

        // Get the building the unit is garrisoned in
        if (!unit.GarrisonedInHandle.IsValid) return;
        int buildingSlot = buildings.GetSlot(unit.GarrisonedInHandle);
        if (!buildings.TryGetRow(buildingSlot, out var building)) return;

        // Find which slot this unit occupies
        int slotIndex = building.FindUnitInGarrison(unitHandle);
        if (slotIndex < 0) return;

        // Eject the unit
        unit.GarrisonedInHandle = SimHandle.Invalid;
        unit.Position = CalculateExitPosition(building.Position, slotIndex);
        unit.Velocity = Fixed64Vec2.Zero;

        // Remove from building garrison
        building.RemoveFromGarrison(slotIndex);
    }

    private void EjectAllUnits(ref BuildingRowRowRef building, CombatUnitRowTable units)
    {
        var slots = building.GarrisonSlotArray;

        // Eject each slot using span accessor
        for (int i = 0; i < slots.Length; i++)
        {
            EjectFromSlot(ref slots[i], building.Position, i, units);
        }

        building.GarrisonCount = 0;
    }

    private void EjectFromSlot(ref SimHandle slotHandle, Fixed64Vec2 buildingPos, int slotIndex, CombatUnitRowTable units)
    {
        if (!slotHandle.IsValid) return;

        int unitSlot = units.GetSlot(slotHandle);
        if (units.TryGetRow(unitSlot, out var unit))
        {
            unit.GarrisonedInHandle = SimHandle.Invalid;
            unit.Position = CalculateExitPosition(buildingPos, slotIndex);
            unit.Velocity = Fixed64Vec2.Zero;
        }

        slotHandle = SimHandle.Invalid;
    }

    private static Fixed64Vec2 CalculateExitPosition(Fixed64Vec2 buildingCenter, int unitIndex)
    {
        // Spread units in a circle around building (60 degrees apart)
        Fixed64 angle = Fixed64.FromInt(unitIndex) * AngleStep;

        return new Fixed64Vec2(
            buildingCenter.X + Fixed64.Cos(angle) * ExitRadius,
            buildingCenter.Y + Fixed64.Sin(angle) * ExitRadius
        );
    }
}
