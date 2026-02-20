# Property System Implementation Plan (Derp.UI)

## Overview
Build a property system on top of the existing pooled + handle system where property values live in the owning component pools. The property layer is a generated, allocation-free view used for inspectors, animation, and serialization. Human code continues to use `Edit()` for SoA components.

## Scope and Constraints
- Zero allocations in per-frame/per-entity paths.
- No runtime fallbacks; generator errors for unsupported types.
- Stable property IDs derived from full field paths.
- Deterministic order of properties within a component.

## Prerequisite: Worktree
Before starting code changes, create a new worktree for this work:
```
git worktree add ../derpui-properties -b property-system
```
All implementation work happens in that worktree.

## Changes to Existing Pooling System (Minimal)
### 1) Expose pool for property access
File: `Shared/Pooled.Generator/SourceRenderer.cs`
- Add a generated accessor in `TypeName.Api`:
  - Signature: `internal static TypeNamePool GetPoolForProperties(global::Pooled.Runtime.IPoolRegistry reg)`
  - Implementation: return the cached pool instance (same as current `GetPool`).
  - This enables `ref` access to SoA columns for property accessors.

### 2) Emit `PoolIdConst` (optional but recommended)
File: `Shared/Pooled.Generator/SourceRenderer.cs`
- Emit: `public const ushort PoolIdConst = <PoolId>;`
- This gives the property dispatcher and serializers a consistent component identifier without extra mapping.

### 3) Handle-only lookup (optional)
File: `Shared/Pooled.Generator/SourceRenderer.cs`
- Emit: `public static bool TryGetHandleById(IPoolRegistry reg, TypeNameId id, out TypeNameHandle handle)`
- Allows dispatcher to avoid `ViewProxy` creation when only a handle is needed.

## New Projects
### 1) `Shared/Property.Annotations`
Purpose: attributes and enums used by source generator.
Files:
- `Shared/Property.Annotations/Property.cs`
  - `[Property]` attribute with metadata:
    - `Name`, `Group`, `Order`, `Flags`, `Min`, `Max`, `Step`, `ExpandSubfields`
    - `Kind` (for explicit override; usually inferred from field type)
- `Shared/Property.Annotations/PropertyKind.cs`
- `Shared/Property.Annotations/PropertyFlags.cs`

### 2) `Shared/Property.Runtime`
Purpose: runtime types used by generated code.
Files:
- `Shared/Property.Runtime/PropertyInfo.cs`
  - `PropertyInfo` struct (Name, Group, Kind, Flags, Min, Max, Step, Order, PropertyId)
- `Shared/Property.Runtime/AnyComponentHandle.cs`
  - `ComponentKind` enum (or use `PoolIdConst`)
  - `AnyComponentHandle` struct (Kind, Index, Generation)
- `Shared/Property.Runtime/PropertySlot.cs`
  - `PropertySlot` struct (AnyComponentHandle, PropertyIndex, PropertyId, Kind)

### 3) `Shared/Property.Generator`
Purpose: generate per-component properties and a global dispatcher.
Files:
- `Shared/Property.Generator/PropertyGenerator.cs`
- `Shared/Property.Generator/Diagnostics.cs`
- `Shared/Property.Generator/Models.cs`
- `Shared/Property.Generator/Renderer.Component.cs`
- `Shared/Property.Generator/Renderer.Dispatcher.cs`

## Property ID Generation (Stable)
### Algorithm
Use FNV-1a 64-bit at generation time.
Input string format:
```
global::<Namespace>.<TypeName>.<FieldPath>
```
Example:
```
global::Derp.UI.TransformComponent.Transform.Pose.Position.X
```

### Output
- Emit as constants: `public const ulong PositionXId = 0x...UL;`
- Never compute IDs at runtime.

## Subfield Expansion Rules (Subfields Preferred)
### When to expand
Expand if:
- Field type is in a known list (e.g., `System.Numerics.Vector2/3/4`, `Core.Color32`, `FixedMath.Fixed64Vec2/Vec3`), OR
- Field is `unmanaged` and `[Property(ExpandSubfields = true)]` is specified.

### Expansion behavior
- Generate a leaf property for each field in the nested struct.
- Property ID uses full path including subfield name.
- Property name uses base label + subfield label:
  - Base label from `[Property(Name = "...")]` or field name (split on casing).
  - Subfield suffix: `" X"`, `" Y"`, etc.
- Property order: `Order * 10 + subIndex` (so subfields are grouped and deterministic).
- `Group`, `Flags`, `Min/Max/Step` inherit from parent unless overridden by future subfield-specific metadata.

### Errors
Emit generator error if:
- Any intermediate type is not `unmanaged`.
- A nested field is a property (not a field).
- A nested field is a reference type or contains references.

## Generated Output (Per Component)
File: `Generated/<Namespace>.<ComponentName>.Properties.g.cs`

### Constants
- `public const ulong SchemaId = <hash>;`
- `public const ulong PositionXId = <hash>;`
- `public const int PropertyCount = <count>;`

### Metadata registration
- `public static void RegisterNames()`
  - Populates `StringHandle` fields via `StringRegistry`.
  - Called once during application startup.

### PropertyInfo storage
- `private static PropertyInfo _infoPositionX;`
- `public static ref readonly PropertyInfo PositionXInfo => ref _infoPositionX;`

### Per-field handles
Generated as internal structs by default:
```
internal readonly struct PositionXHandle
{
    public readonly TransformComponentHandle Component;
    public PositionXHandle(TransformComponentHandle component) { Component = component; }
}
```

### Per-field accessors
```
public static ref float PositionXRef(IPoolRegistry reg, TransformComponentHandle component)
{
    var pool = TransformComponent.Api.GetPoolForProperties(reg);
    return ref pool.PositionXRef(component);
}
```
No temporary allocations; return `ref` directly.

### Inspector helpers
```
public static bool TryGetInfoByIndex(int index, out PropertyInfo info)
public static PropertySlot GetSlotByIndex(AnyComponentHandle component, int index)
```

## Generated Output (Global Dispatcher)
File: `Generated/PropertyDispatcher.g.cs`
Responsibilities:
- `GetPropertyCount(AnyComponentHandle component)`
- `TryGetInfo(AnyComponentHandle component, int index, out PropertyInfo info)`
- `ReadFloat/WriteFloat/ReadBool/WriteBool` by `PropertyKind`
Implementation:
- `switch` on component kind or `PoolIdConst`
- Per-component `switch` on property index
- No delegates, no reflection

## Serialization and Keyframes
### Base values
- Serialized as part of component data (pooled storage).

### Keyframes
Store per-track bindings:
```
struct PropertyBinding
{
    ComponentKind ComponentKind;
    ulong ComponentId;
    ulong PropertyId;
}
```
At load time:
- Resolve `PropertyBinding` to a property slot using the dispatcher.
- If lookup fails, surface an error (no runtime fallback).

## Inspector Flow (General)
1) Selection yields `AnyComponentHandle`.
2) `PropertyDispatcher.GetPropertyCount` returns property count.
3) Loop index:
   - `TryGetInfo` for label/group/range.
   - `PropertySlot` for routing.
   - Switch by `PropertyKind` and read/write via dispatcher.
4) Human code uses `Edit()` for direct component mutation outside the inspector.

## Determinism and Hot Path Rules
- No lambdas in generated code.
- No LINQ.
- No per-frame allocations.
- No string concatenation in hot paths; use `StringHandle`.

## Implementation Steps (Detailed)
1) **Worktree**
   - Create worktree and branch: `git worktree add ../derpui-properties -b property-system`.
2) **Pooling generator changes**
   - Add `GetPoolForProperties` to `Api`.
   - Add `PoolIdConst` emission.
   - Add optional `TryGetHandleById`.
3) **Annotations project**
   - Add `Shared/Property.Annotations` project and reference in relevant projects.
   - Implement `[Property]` attribute and enums.
4) **Runtime project**
   - Add `Shared/Property.Runtime` project.
   - Add `PropertyInfo`, `AnyComponentHandle`, `PropertySlot`.
5) **Property generator**
   - Implement syntax discovery for `[Property]` fields.
   - Validate pooled partial struct and field types.
   - Expand subfields according to rules.
   - Render per-component helpers.
   - Render global dispatcher.
6) **Wire projects**
   - Reference `Property.Annotations` in component projects.
   - Reference `Property.Runtime` where inspector/serialization lives.
   - Add generator as analyzer to relevant projects.
7) **Smoke test**
   - Add one component in `Derp.UI` with `[Property]` fields and a nested struct.
   - Use dispatcher in a temporary inspector to read/write values.
8) **Serialization**
   - Add bindings to document format.
   - Load and apply keyframes using dispatcher.

## Validation Checklist
- Generated code compiles without allocations in hot paths.
- All properties have stable `PropertyId` constants.
- Subfield access updates pooled memory (`ref` paths).
- Inspector edits propagate to component data.
- Keyframe bindings resolve without fallbacks.

