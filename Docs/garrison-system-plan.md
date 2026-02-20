# Dynamic Garrison System Plan (Future)

## Overview
Units can enter/exit buildings and fire from inside using the building's attack range.

---

## Data Model Changes

### BuildingTypeData.cs
```csharp
public int GarrisonCapacity;  // 0 = cannot garrison
```

### BuildingRow.cs
```csharp
// Fixed slots for deterministic snapshotting (max 6 units)
public SimHandle GarrisonSlot0;
public SimHandle GarrisonSlot1;
public SimHandle GarrisonSlot2;
public SimHandle GarrisonSlot3;
public SimHandle GarrisonSlot4;
public SimHandle GarrisonSlot5;
public byte GarrisonCount;
```

### CombatUnitRow.cs
```csharp
public SimHandle GarrisonedInHandle;  // Invalid = not garrisoned

// Add to OrderType enum:
EnterGarrison,
ExitGarrison
```

### GameInput.cs
```csharp
public SimHandle GarrisonTargetHandle;
public bool HasEnterGarrisonCommand;
public bool HasExitGarrisonCommand;
```

---

## New Systems

### GarrisonCommandSystem.cs
- Processes enter/exit commands from input
- Sets `EnterGarrison` order on selected units when right-clicking friendly building
- Sets `ExitGarrison` order when exit command issued

### GarrisonEnterSystem.cs
- Monitors units with `EnterGarrison` order
- When unit reaches building (distance check), adds to garrison slot
- Sets `unit.GarrisonedInHandle` and clears velocity

### GarrisonExitSystem.cs
- Monitors units with `ExitGarrison` order
- Removes from garrison slot, positions outside building
- Clears `GarrisonedInHandle`

---

## Existing System Modifications

### CombatUnitMovementSystem.cs
```csharp
// Skip movement for garrisoned units
if (unit.GarrisonedInHandle.IsValid)
{
    unit.Velocity = Fixed64Vec2.Zero;
    continue;
}
```

### CombatUnitCombatSystem.cs
When garrisoned:
- Fire from building position instead of unit position
- Use building's attack range

```csharp
Fixed64Vec2 firePosition = unit.Position;
Fixed64 effectiveRange = unit.AttackRange;

if (unit.GarrisonedInHandle.IsValid)
{
    int buildingSlot = buildings.GetSlot(unit.GarrisonedInHandle);
    if (buildingSlot >= 0 && buildings.TryGetRow(buildingSlot, out var building))
    {
        firePosition = building.Position;
        effectiveRange = building.AttackRange;
    }
}
```

### CombatUnitTargetAcquisitionSystem.cs
Use building position/range for target search when garrisoned.

### BuildingDeathSystem.cs
When building dies:
- Eject all garrisoned units outside
- Apply 50% HP damage to ejected units
- Clear all garrison slots

```csharp
private void ReleaseGarrisonedUnits(ref BuildingRowAccessor building)
{
    var units = World.CombatUnitRows;

    EjectUnit(building.GarrisonSlot0, building.Position, units);
    EjectUnit(building.GarrisonSlot1, building.Position, units);
    // ... etc

    building.GarrisonCount = 0;
}

private void EjectUnit(SimHandle unitHandle, Fixed64Vec2 buildingPos, CombatUnitTable units)
{
    if (!unitHandle.IsValid) return;

    int slot = units.GetSlot(unitHandle);
    if (!units.TryGetRow(slot, out var unit)) return;

    unit.Health = unit.Health / 2;  // Take 50% damage
    unit.GarrisonedInHandle = SimHandle.Invalid;
    unit.Position = buildingPos;
}
```

---

## DI Registration

**GameComposition.Simulation.cs:**
```csharp
// Add bindings
.Bind().As(Singleton).To<GarrisonCommandSystem>()
.Bind().As(Singleton).To<GarrisonEnterSystem>()
.Bind().As(Singleton).To<GarrisonExitSystem>()

// System execution order (after movement, before combat):
moveCommand,
garrisonCommand,
garrisonEnter,
garrisonExit,
combatTargetAcquisition,
```

---

## Design Decisions

| Decision | Rationale |
|----------|-----------|
| 6 fixed slots | SimTable requires fixed-size for snapshotting |
| Use building range | Elevated position justifies increased range |
| 50% HP on eject | Forgiving gameplay, allows retreat |
| Right-click = enter | Intuitive RTS control scheme |

---

## Testing Considerations

1. **Determinism** - Garrison state serializes correctly for rollback
2. **Edge cases:**
   - Unit dies while garrisoned
   - Building destroyed with units inside
   - Full building (capacity exceeded)
   - Multiple players garrisoning same building
3. **Combat** - Projectiles spawn from building position with correct range
