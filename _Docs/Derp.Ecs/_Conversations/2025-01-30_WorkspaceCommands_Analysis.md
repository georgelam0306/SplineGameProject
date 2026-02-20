# WorkspaceCommands Analysis

**Date:** 2025-01-30

## TLDR

WorkspaceCommands.cs is a 430KB monolithic class where every new component type requires:
1. New `UndoRecordKind` enum entry
2. New fields in `UndoRecord` struct
3. Hardcoded property name constants
4. Specialized `Record*` and `Apply*` methods
5. Manual field-by-field comparison for auto-keying

---

## The Hardcoding Problem

### 1. UndoRecordKind Explosion (27 types and growing)

```csharp
internal enum UndoRecordKind : byte
{
    None = 0,
    SetProperty = 1,
    SetPropertyBatch = 2,
    SetFillComponent = 3,          // ← hardcoded for FillComponent
    SetTransformComponent = 8,     // ← hardcoded for TransformComponent
    ResizeShape = 9,               // ← hardcoded for shapes
    SetPaintComponent = 16,        // ← hardcoded for PaintComponent
    CreatePrefab = 5,
    CreateShape = 6,
    CreateBooleanGroup = 7,
    DeleteShape = 18,
    // ... 27 total kinds
}
```

Every new component or operation = new enum value + new handling code.

### 2. UndoRecord Has Component-Specific Fields

```csharp
internal struct UndoRecord
{
    public UndoRecordKind Kind;

    // Transform-specific
    public TransformComponentHandle TransformHandle;
    public TransformComponent TransformBefore;
    public TransformComponent TransformAfter;

    // RectGeometry-specific
    public RectGeometryComponentHandle RectGeometryHandle;
    public RectGeometryComponent RectGeometryBefore;
    public RectGeometryComponent RectGeometryAfter;

    // CircleGeometry-specific
    public CircleGeometryComponentHandle CircleGeometryHandle;
    public CircleGeometryComponent CircleGeometryBefore;
    public CircleGeometryComponent CircleGeometryAfter;

    // PrefabCanvas-specific
    public PrefabCanvasComponentHandle PrefabCanvasHandle;
    public PrefabCanvasComponent PrefabCanvasBefore;
    public PrefabCanvasComponent PrefabCanvasAfter;

    // ... more component-specific fields
}
```

This struct grows every time a new component needs undo support.

### 3. Hardcoded Property Names

```csharp
private static readonly StringHandle FillColorName = "Color";
private static readonly StringHandle FillUseGradientName = "Use Gradient";
private static readonly StringHandle GradientColorAName = "Color A";
private static readonly StringHandle GradientColorBName = "Color B";
private static readonly StringHandle StopsColor0Name = "Color 0";
private static readonly StringHandle StopsColor1Name = "Color 1";
// ... 16 more for gradient stops
```

These exist because auto-keying needs to find properties by name to create keyframes.

### 4. Manual Field-by-Field Comparison (100+ lines)

```csharp
private void AutoKeyFillComponentEdit(FillComponentHandle fillHandle,
    in FillComponent before, in FillComponent after)
{
    // Manual comparison for every field
    if (!before.Color.Equals(after.Color) && !slots.Color.Component.IsNull)
    {
        AutoKeyIfTimelineSelected(slots.Color, ...);
    }

    if (before.UseGradient != after.UseGradient && ...)
    {
        AutoKeyIfTimelineSelected(slots.UseGradient, ...);
    }

    if (!before.GradientColorA.Equals(after.GradientColorA) && ...)
    {
        AutoKeyIfTimelineSelected(slots.GradientColorA, ...);
    }

    // ... 15 more field comparisons
}
```

### 5. Specialized Record/Apply Methods

Each component type has its own recording method:

```csharp
public void RecordTransformComponentEdit(TransformComponentHandle handle,
    in TransformComponent before, in TransformComponent after)

public void RecordResizeRectShapeEdit(...)
public void RecordResizeCircleShapeEdit(...)
public void RecordResizePrefabInstanceEdit(...)
public void RecordFillComponentEdit(...)
public void RecordMovePrefab(...)
public void RecordResizePrefab(...)
```

---

## What the V2 System Should Solve

| Current (Hardcoded) | V2 (Schema-Driven) |
|---------------------|-------------------|
| 27+ UndoRecordKind entries | 2-3 generic kinds (SetProperty, ComponentSnapshot, Structural) |
| UndoRecord has component-specific fields | Generic blob storage + schema reference |
| Property names as string constants | Generated PropertyIds from schema |
| Manual field-by-field comparison | Generated diff from schema |
| Per-component Record* methods | One generic `RecordEdit<T>()` |
| Per-component Apply* methods | One generic `ApplyEdit()` using schema |

### The Key Insight

The current code manually does what **code generation should do**:

```
CURRENT:
  Developer writes FillComponent
  Developer adds UndoRecordKind.SetFillComponent
  Developer adds FillComponent fields to UndoRecord
  Developer adds RecordFillComponentEdit()
  Developer adds AutoKeyFillComponentEdit() with 16 field comparisons
  Developer adds property name constants

V2:
  Developer writes [Schema] FillComponent
  Generator produces:
    - PropertyIds for all fields
    - Diff method (returns changed fields)
    - Blob serialization (for undo snapshots)
    - Auto-key bindings
```

---

## Specific Pain Points to Address

1. **Auto-keying** - Currently requires knowing every field by name. V2 should iterate fields from schema.

2. **Undo granularity** - Current system has both per-property (good) and per-component (coarse) undo. Some components use blob snapshots when they should use per-field deltas.

3. **Component-specific resize** - `RecordResizeRectShapeEdit`, `RecordResizeCircleShapeEdit`, `RecordResizePrefabInstanceEdit` are nearly identical. Should be generic.

4. **Batched edits** - `SetPropertyBatch2`, `SetPropertyBatch3`, `SetPropertyBatch4` are copy-pasted with different arities. Should be one method with span.

5. **The 430KB file** - Single class doing too much. Should be split by concern (Undo, Clipboard, PropertyEditing, Hierarchy, etc.)

---

## Files Analyzed

- `Workspace/WorkspaceCommands.cs` (430KB, ~10,000 lines)
- `Workspace/UndoRecord.cs`
- `Editor/EditorCommands.cs`
