# Relational GameDocDatabase Design

This document describes the design for adding SQL-like relational features to GameDocDatabase while maintaining zero-allocation performance.

## Overview

GameDocDatabase currently supports O(1) primary key lookups and range queries via slot arrays. This enhancement adds:

1. **Forward Navigation** - Traverse from a doc to its referenced doc via FK
2. **Reverse Navigation** - Find all docs that reference a given doc
3. **Join Iterators** - Zero-allocation iteration over related doc pairs

## Goals

- **Zero allocation** - All operations return refs/spans, no heap allocations in hot paths
- **O(1) or O(log n)** - Maintain existing performance characteristics
- **Code-generated** - All relational methods generated at compile time
- **Lazy indexes** - Reverse indexes built on first access, not at load time
- **Immutable** - All data structures are read-only after load

## Architecture

```
Schema Definition               Generated Code
┌─────────────────────┐        ┌────────────────────────────────────┐
│ [GameDocTable]      │        │ UnitTypeDataTable                  │
│ struct UnitTypeData │        │   .FindById() - existing O(1)      │
│ {                   │───────▶│                                    │
│   [PrimaryKey]      │        │ UnitTypeDataExtensions             │
│   int Id;           │        │   .GetTrainedAtBuilding() - NEW    │
│                     │        └────────────────────────────────────┘
│   [ForeignKey(...)] │                      │
│   int TrainedAtBldg │                      ▼
│ }                   │        ┌────────────────────────────────────┐
└─────────────────────┘        │ GameDocDb                          │
                               │   .UnitsByTrainedAtBuilding        │
                               │     (lazy ReverseIndex)            │
                               │   .GetTrainableUnits(building)     │
                               └────────────────────────────────────┘
```

## API Design

### Forward Navigation

Navigate from a doc to its referenced doc:

```csharp
// Schema
[GameDocTable("UnitTypes")]
public struct UnitTypeData
{
    [PrimaryKey] public int Id;

    [ForeignKey(typeof(BuildingTypeData))]
    public int TrainedAtBuildingType;
}

// Generated extension method
public static ref readonly BuildingTypeData GetTrainedAtBuilding(
    this ref readonly UnitTypeData self,
    BuildingTypeDataTable buildingTable)
{
    return ref buildingTable.FindById(self.TrainedAtBuildingType);
}

// Usage
ref readonly var unit = ref db.UnitTypeData.FindById(0);
ref readonly var building = ref unit.GetTrainedAtBuilding(db.BuildingTypeData);
```

### Nullable Foreign Keys

For FKs that can be -1 (null), use `Nullable = true`:

```csharp
// Schema
[ForeignKey(typeof(TechTypeData), Nullable = true)]
public int RequiredTechId;

// Generated TryGet pattern
public static bool TryGetRequiredTech(
    this ref readonly UnitTypeData self,
    TechTypeDataTable techTable,
    out TechTypeData result)
{
    if (self.RequiredTechId < 0)
    {
        result = default;
        return false;
    }
    return techTable.TryFindById(self.RequiredTechId, out result);
}
```

### Custom Navigation Names

Override the auto-generated navigation name:

```csharp
[ForeignKey(typeof(BuildingTypeData), NavigationName = "FactoryBuilding")]
public int TrainedAtBuildingType;

// Generates: .GetFactoryBuilding() instead of .GetTrainedAtBuildingType()
```

### Reverse Navigation

Find all docs that reference a given doc:

```csharp
// Lazy-built reverse index in GameDocDb
public sealed class GameDocDb
{
    private UnitTypeDataByTrainedAtBuildingTypeIndex? _unitsByTrainedAtBuilding;

    public UnitTypeDataByTrainedAtBuildingTypeIndex UnitsByTrainedAtBuilding
        => _unitsByTrainedAtBuilding ??=
           new(UnitTypeData, BuildingTypeData.MaxId);

    // Convenience method
    public RangeView<UnitTypeData> GetTrainableUnits(
        ref readonly BuildingTypeData building)
        => UnitsByTrainedAtBuilding.GetByForeignKey(building.Id);
}

// Usage
ref readonly var building = ref db.BuildingTypeData.FindById(0);
foreach (ref readonly var unit in db.GetTrainableUnits(building))
{
    Console.WriteLine($"Can train: {unit.Name}");
}
```

### Join Iterators

Zero-allocation iteration over related pairs:

```csharp
// Generated join method
public static UnitTypeData_BuildingTypeDataJoin JoinTrainedAtBuilding(
    this UnitTypeDataTable leftTable,
    BuildingTypeDataTable rightTable)
{
    return new UnitTypeData_BuildingTypeDataJoin(leftTable, rightTable);
}

// Usage - zero allocation foreach
foreach (var (unit, building) in db.UnitTypeData.JoinTrainedAtBuilding(db.BuildingTypeData))
{
    Console.WriteLine($"{unit.Name} trained at {building.Name}");
}
```

### Chained Joins

For multi-hop relationships:

```csharp
// A -> B -> C pattern
foreach (var (research, building, category) in
    db.BuildingResearchData
        .JoinBuildingType(db.BuildingTypeData)
        .JoinCategory(db.BuildingCategoryData))
{
    // Three-way join
}
```

## Implementation Details

### Reverse Index Structure

Each reverse index stores:
- **Range array**: `(int Start, int Count)[]` indexed by FK value
- **Sorted records**: Records sorted by FK for contiguous access

```csharp
public sealed class UnitTypeDataByTrainedAtBuildingTypeIndex
{
    private readonly (int Start, int Count)[] _ranges;
    private readonly UnitTypeData[] _sortedRecords;

    public RangeView<UnitTypeData> GetByForeignKey(int fkValue)
    {
        if ((uint)fkValue >= (uint)_ranges.Length)
            return RangeView<UnitTypeData>.Empty;

        var (start, count) = _ranges[fkValue];
        return new RangeView<UnitTypeData>(_sortedRecords.AsSpan(start, count));
    }
}
```

Memory layout for 1000 buildings, 50 units:
- Range array: 1000 * 8 bytes = 8 KB
- Sorted records: 50 * sizeof(UnitTypeData)

### Join Enumerator

Ref struct enumerator for zero-allocation foreach:

```csharp
public readonly ref struct UnitTypeData_BuildingTypeDataJoin
{
    private readonly UnitTypeDataTable _leftTable;
    private readonly BuildingTypeDataTable _rightTable;

    public ref struct Enumerator
    {
        private readonly ReadOnlySpan<UnitTypeData> _leftRecords;
        private readonly BuildingTypeDataTable _rightTable;
        private int _index;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            _index++;
            return _index < _leftRecords.Length;
        }

        public (UnitTypeData Left, BuildingTypeData Right) Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                ref readonly var left = ref _leftRecords[_index];
                ref readonly var right = ref _rightTable.FindById(left.TrainedAtBuildingType);
                return (left, right);
            }
        }
    }
}
```

## Performance Characteristics

| Operation | Complexity | Allocation |
|-----------|------------|------------|
| Forward navigation | O(1) | Zero |
| Reverse navigation lookup | O(1) | Zero |
| Reverse navigation iteration | O(k) | Zero |
| Join iteration | O(n) | Zero |
| Reverse index building | O(n log n) | Once per index |

### Lazy Index Building

Reverse indexes are built lazily on first access:

```csharp
// First access triggers O(n log n) build
var trainableUnits = db.GetTrainableUnits(building);

// Subsequent accesses are O(1)
var moreUnits = db.GetTrainableUnits(anotherBuilding);
```

This avoids startup cost if reverse navigation is unused.

## Annotation Reference

### ForeignKeyAttribute

```csharp
[AttributeUsage(AttributeTargets.Field)]
public sealed class ForeignKeyAttribute : Attribute
{
    /// <summary>The table type this FK references.</summary>
    public Type ReferencedTable { get; }

    /// <summary>Custom navigation method name. Defaults to field name without "Id" suffix.</summary>
    public string? NavigationName { get; set; }

    /// <summary>If true, FK can be -1 (null). Generates TryGet pattern.</summary>
    public bool Nullable { get; set; } = false;
}
```

### HasManyAttribute

Optional attribute to customize reverse navigation:

```csharp
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = true)]
public sealed class HasManyAttribute : Attribute
{
    /// <summary>The table type that references this table.</summary>
    public Type ReferencingTable { get; }

    /// <summary>The FK field name in the referencing table.</summary>
    public string ForeignKeyField { get; }

    /// <summary>Custom name for the reverse navigation.</summary>
    public string? NavigationName { get; set; }
}

// Usage
[GameDocTable("BuildingTypes")]
[HasMany(typeof(UnitTypeData), "TrainedAtBuildingType", NavigationName = "TrainableUnits")]
public struct BuildingTypeData { ... }
```

## Files Modified/Created

| File | Change |
|------|--------|
| `GameDocDatabase.Annotations/GameDocTableAttribute.cs` | Add NavigationName, Nullable to ForeignKeyAttribute; add HasManyAttribute |
| `GameDocDatabase.Generator/TableSchemaModel.cs` | Add ForeignKeyInfo, ReverseNavigationInfo; extend FieldInfo |
| `GameDocDatabase.Generator/GameDocDbGenerator.cs` | Extract FK metadata, call new renderers |
| `GameDocDatabase.Generator/NavigationExtensionsRenderer.cs` | NEW - forward navigation |
| `GameDocDatabase.Generator/ReverseIndexRenderer.cs` | NEW - reverse index classes |
| `GameDocDatabase.Generator/JoinRenderer.cs` | NEW - join iterators |

## Migration Guide

Existing code continues to work unchanged. To add relational navigation:

1. Add `[ForeignKey(typeof(TargetTable))]` to FK fields
2. Rebuild - generator creates extension methods automatically
3. Use `.GetXxx()` for forward nav, `db.GetXxxs()` for reverse nav

## Future Enhancements

- **Pre-computed binary indexes** - Store reverse indexes in .bin file for faster load
- **Composite FK support** - Multi-field foreign keys
- **Cascading deletes** - If source tables become mutable
- **Query builder** - LINQ-like fluent API for complex queries
