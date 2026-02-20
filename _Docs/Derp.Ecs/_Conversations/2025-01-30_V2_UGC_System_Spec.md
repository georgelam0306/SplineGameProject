# V2 UGC System Spec

**Date:** 2025-01-30

---

## 1. Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              EDITOR TIME                                     │
│                                                                             │
│  • User defines UGC schemas dynamically (fields, types, nesting)            │
│  • Allocations OK (Dictionary, List, managed objects)                       │
│  • Prefab instances can override inherited values                           │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    │ Export / Bake
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                              RUNTIME                                         │
│                                                                             │
│  • UGC schemas become real components via codegen                           │
│  • Blittable structs, inlined arrays, Span<T> access                        │
│  • Prefab instances flattened (inherited + overrides baked together)        │
│  • Same API as code-defined components: entity.Get<EnemyUnit>()             │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Editor: Schema Definition

User defines schemas in the editor UI:

```
UGC Schema: "ItemStats"
├── Damage (int)
├── CritChance (float)
└── Rarity (enum: Common, Rare, Epic, Legendary)

UGC Schema: "Buff"
├── Name (string)
├── Duration (float)
└── Stacks (int)

UGC Schema: "InventorySlot"
├── Item (nested: ItemStats)          ← Nested UGC struct
└── Quantity (int)

UGC Schema: "PlayerData"
├── Health (int)
├── MaxHealth (int)
├── Inventory (list of: InventorySlot)   ← List of UGC structs
└── ActiveBuffs (list of: Buff)          ← List of UGC structs
```

---

## 3. Editor: Storage (Allocations OK)

```csharp
// Schema metadata
class UgcSchema
{
    public StringHandle Name;
    public List<UgcFieldDef> Fields;
}

class UgcFieldDef
{
    public StringHandle Name;
    public UgcFieldKind Kind;           // Primitive, Nested, List
    public PropertyKind PrimitiveType;  // int, float, string, etc.
    public UgcSchema NestedSchema;      // For nested/list types
}

// Instance data (dynamic, managed)
class UgcInstance
{
    public UgcSchema Schema;
    public Dictionary<StringHandle, UgcValue> Values;
}
```

---

## 4. Editor: Prefab Inheritance

```
PREFAB: "EnemyUnit"
├── Health: 100
├── Damage: 10
└── Speed: 5.0

INSTANCE: "GoblinWarrior" (inherits EnemyUnit)
├── Health: 150   ← Overridden
├── Damage: 20    ← Overridden
└── Speed: 5.0    ← Inherited

INSTANCE: "GoblinScout" (inherits EnemyUnit)
├── Health: 100   ← Inherited
├── Damage: 10    ← Inherited
└── Speed: 8.0    ← Overridden
```

---

## 5. Bake: Flatten Instances

At export, instances are flattened (inherited + overrides merged):

```
GoblinWarrior blob: { Health: 150, Damage: 20, Speed: 5.0 }
GoblinScout blob:   { Health: 100, Damage: 10, Speed: 8.0 }
```

No runtime inheritance resolution. Just read the values.

---

## 6. Bake: Generate Components

Each UGC schema becomes a real component:

```csharp
// GENERATED from UGC schema "EnemyUnit"
[GeneratedUgcComponent]
public struct EnemyUnit : IComponent
{
    public int Health;
    public int Damage;
    public float Speed;
    public StringHandle UnitName;
}
```

---

## 7. Bake: Inlined Arrays with Span Access

Lists are inlined at baked size, exposed via Span<T>:

```csharp
// GENERATED from UGC schema "PlayerData"
// Baked with: Inventory count = 4, ActiveBuffs count = 2

[GeneratedUgcComponent]
public struct PlayerData : IComponent
{
    public int Health;
    public int MaxHealth;

    // Inlined at exact baked size
    private InventorySlot _inventory0;
    private InventorySlot _inventory1;
    private InventorySlot _inventory2;
    private InventorySlot _inventory3;

    private Buff _buff0;
    private Buff _buff1;

    // Span accessors
    public Span<InventorySlot> Inventory
    {
        get => MemoryMarshal.CreateSpan(ref _inventory0, 4);
    }

    public Span<Buff> ActiveBuffs
    {
        get => MemoryMarshal.CreateSpan(ref _buff0, 2);
    }
}
```

---

## 8. Bake: Nested Structs

Nested UGC structs are inlined:

```csharp
// InventorySlot contains ItemStats inline
public struct InventorySlot
{
    public ItemStats Item;    // Inlined, not a pointer
    public int Quantity;
}

public struct ItemStats
{
    public int Damage;
    public float CritChance;
    public int Rarity;
}
```

---

## 9. Runtime: Callsite Examples

**Simple component access:**
```csharp
var enemy = entity.Get<EnemyUnit>();
int health = enemy.Health;
float speed = enemy.Speed;
```

**List iteration via Span:**
```csharp
var player = entity.Get<PlayerData>();

foreach (var slot in player.Inventory)
{
    DrawItem(slot.Item.Damage, slot.Quantity);
}

foreach (var buff in player.ActiveBuffs)
{
    ApplyBuff(buff.Name, buff.Duration, buff.Stacks);
}
```

**Indexing:**
```csharp
var firstSlot = player.Inventory[0];
var lastBuff = player.ActiveBuffs[^1];
```

**Nested struct with list:**
```csharp
var character = entity.Get<Character>();

// Nested access
int damage = character.Equipment.Weapon.Damage;

// List inside nested struct
foreach (var gem in character.Equipment.Gems)
{
    ApplyGemBonus(gem);
}
```

---

## 10. Runtime: Same API as Code-Defined Components

```csharp
// Code-defined component
var transform = entity.Get<Transform>();

// UGC-defined component (generated at bake)
var enemy = entity.Get<EnemyUnit>();

// Same API. Callsite doesn't know which is which.
```

---

## 11. Integration with Property System

UGC components use the same proxy pattern:

```csharp
// Read-only
var enemy = entity.Get<EnemyUnit>();

// Direct write (marks dirty, no commands)
ref var enemy = ref entity.Ref<EnemyUnit>();
enemy.Health = 50;

// User editing (emits commands)
using (var edit = entity.BeginChange<EnemyUnit>())
{
    edit.Health = 50;
}
```

---

## 12. Integration with Bindings

UGC fields participate in toposorted evaluation:

```csharp
// Baked binding: PlayerData.Health → HUD.HealthBar.Value

public static class Bindings_PlayerHUD
{
    public static void EvaluateAll(ref PlayerData player, ref HUD hud)
    {
        hud.HealthBar.Value = player.Health;
        hud.HealthBar.Fill = player.Health / (float)player.MaxHealth;
        hud.BuffCount.Value = player.ActiveBuffs.Length;
    }
}
```

---

## 13. Summary Table

| Aspect | Editor | Runtime |
|--------|--------|---------|
| **Schema definition** | Dynamic (user creates in UI) | Generated structs |
| **Storage** | Dictionary, List, managed | Blittable structs |
| **Lists** | `List<T>` | Inlined fields + `Span<T>` |
| **Nested structs** | Reference to instance | Inlined |
| **Prefab inheritance** | Override tracking | Flattened |
| **Access pattern** | UgcInstance.Values["Health"] | `entity.Get<T>().Health` |
| **Allocations** | OK | Zero |

---

## 14. Key Principles

1. **UGC schemas become real components** - No generic UgcDataComponent, no accessor ceremony

2. **Flatten at bake** - Prefab instances have all values inlined, no runtime inheritance

3. **Inline arrays at exact size** - We know counts at bake time

4. **Span<T> for list access** - Works like normal arrays

5. **Same API as code-defined** - `entity.Get<T>()` works for both

6. **Same proxy pattern** - Get, Ref, BeginPreview, BeginChange all work

7. **Same binding system** - UGC fields participate in toposort evaluation

---

## Related Documents

- `2025-01-30_V2_Property_System_Spec.md` - Full property system spec
- `2025-01-29_V2_UGC_Property_System_Design.md` - Original design discussion
