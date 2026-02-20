# V2 System Integration with Derp.UI

**Date:** 2025-01-30

---

## 1. Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────────────────┐
│                                    DERP.UI EDITOR                                        │
│                                                                                         │
│  ┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐                   │
│  │   Layers Panel  │     │ Inspector Panel │     │  Canvas Panel   │                   │
│  │   (Hierarchy)   │     │  (Properties)   │     │  (Viewport)     │                   │
│  └────────┬────────┘     └────────┬────────┘     └────────┬────────┘                   │
│           │                       │                       │                             │
│           ▼                       ▼                       ▼                             │
│  ┌──────────────────────────────────────────────────────────────────────────────────┐  │
│  │                           WORKSPACE COMMANDS                                      │  │
│  │  • Property editing (SetProperty, SetPropertyBatch)                              │  │
│  │  • Component snapshots (SetFillComponent, SetTransformComponent, etc.)           │  │
│  │  • Structural changes (CreateShape, DeletePrefab, MoveEntity, etc.)              │  │
│  │  • Auto-keying logic (hardcoded field-by-field)                                  │  │
│  └──────────────────────────────────────────────────────────────────────────────────┘  │
│           │                                                                             │
│           ▼                                                                             │
│  ┌────────────────────┐  ┌────────────────────┐  ┌────────────────────┐                │
│  │     UndoStack      │  │  AnimationDocument │  │   ECS Components   │                │
│  │  (UndoRecord[])    │  │  (Timelines,Tracks)│  │  (Transform, Fill) │                │
│  └────────────────────┘  └────────────────────┘  └────────────────────┘                │
│                                                                                         │
└─────────────────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Current Pain Points → V2 Solutions

### 2.1 UndoRecord Explosion (29 kinds)

**Current:**
```csharp
internal enum UndoRecordKind : byte
{
    None = 0,
    SetProperty = 1,
    SetPropertyBatch = 2,
    SetFillComponent = 3,        // ← hardcoded
    SetTransformComponent = 8,   // ← hardcoded
    SetPaintComponent = 16,      // ← hardcoded
    // ... 29 total kinds
}

internal struct UndoRecord
{
    // Component-specific fields for each kind
    public FillComponent FillBefore;
    public FillComponent FillAfter;
    public TransformComponent TransformBefore;
    public TransformComponent TransformAfter;
    // ... grows with each new component
}
```

**V2 Solution:**
```csharp
// Only 3 generic kinds
enum UndoKind { Property, ComponentSnapshot, Structural }

// Generic blob storage
struct UndoRecord
{
    public UndoKind Kind;
    public int EntityId;
    public int PayloadOffset;  // Into blob storage
    public int PayloadSize;
}

// Schema-driven serialization
// Generator emits: FillComponent.ToBytes(), FillComponent.FromBytes()
```

---

### 2.2 PropertyValue / PropertyKey

**Current (Good foundation):**
```csharp
[StructLayout(LayoutKind.Explicit)]
public readonly struct PropertyValue
{
    [FieldOffset(0)] public readonly float Float;
    [FieldOffset(0)] public readonly int Int;
    [FieldOffset(0)] public readonly Vector2 Vec2;
    // ... union of all property types
}

internal readonly struct PropertyKey
{
    public readonly AnyComponentHandle Component;
    public readonly ushort PropertyIndexHint;
    public readonly ulong PropertyId;
    public readonly PropertyKind Kind;
}
```

**V2 Enhancement:**
- Keep PropertyValue/PropertyKey as-is (they work well)
- Add schema-driven PropertyId generation:

```csharp
// GENERATED from [Schema] FillComponent
public static class FillComponentProperties
{
    public const ulong Color = 0x12345678;      // Stable hash
    public const ulong UseGradient = 0x23456789;
    public const ulong GradientColorA = 0x34567890;
    // ...
}
```

---

### 2.3 Animation Bindings

**Current:**
```csharp
internal readonly struct AnimationBinding
{
    public readonly int TargetIndex;           // Entity reference
    public readonly ushort ComponentKind;      // Which component
    public readonly ushort PropertyIndexHint;  // Optimization
    public readonly ulong PropertyId;          // Which property
    public readonly PropertyKind PropertyKind; // Type for interpolation
}
```

**V2 Integration:**
- AnimationBinding stays mostly the same (clean design)
- Animation playback uses `BeginPreview<T>()` (no commands)
- Keyframe editing uses command system for undo

```csharp
// Animation playback (no commands, marks dirty)
void UpdateAnimation(float time)
{
    foreach (var track in _tracks)
    {
        using (var preview = track.Entity.BeginPreview<T>())
        {
            preview.SetProperty(track.PropertyId, track.Sample(time));
        }
    }
    Flush();  // Toposorted propagation
}

// Keyframe editing (emits commands)
void SetKeyframe(Track track, int frame, PropertyValue value)
{
    _commandBus.Emit(new KeyframeChangedCommand(track, frame, before, value));
    WriteKeyframe(track, frame, value);
}
```

---

### 2.4 Prefab Variables (UGC)

**Current:**
```csharp
[Pooled]
public partial struct PrefabVariablesComponent : IComponent
{
    private const int MaxVariables = 64;

    public int VariableCount;

    // Inline arrays for variable metadata
    [InlineArray(MaxVariables)] public struct ULongBuffer { private ulong _element0; }
    [InlineArray(MaxVariables)] public struct PropertyKindBuffer { private PropertyKind _element0; }
    [InlineArray(MaxVariables)] public struct PropertyValueBuffer { private PropertyValue _element0; }

    public ULongBuffer VariableId;
    public PropertyKindBuffer VariableKind;
    public PropertyValueBuffer Value;
}
```

**V2 Solution:**
```
EDITOR TIME:
  • UGC schemas defined dynamically (List<UgcFieldDef>)
  • Dictionary storage for values (allocations OK)
  • Prefab inheritance tracking

BAKE TIME:
  • Generate blittable struct per schema
  • Inline arrays at exact size
  • Span<T> accessors

RUNTIME:
  • entity.Get<EnemyUnit>() - same as code-defined
  • Zero allocation, memory-mapped
```

---

### 2.5 Prefab Bindings

**Current:**
```csharp
[Pooled]
public partial struct PrefabBindingsComponent : IComponent
{
    public ushort BindingCount;
    public ushort Revision;

    [InlineArray(32)] public struct ULongBuffer { private ulong _element0; }
    [InlineArray(32)] public struct UShortBuffer { private ushort _element0; }

    public ULongBuffer VariableId;
    public ULongBuffer PropertyId;
    public UShortBuffer Direction;
    public UShortBuffer TargetComponentKind;
    // ...
}
```

**V2 Integration:**
- Bindings participate in toposorted evaluation
- At bake: compile expression AST → C# method
- At runtime: flat array of function pointers

```csharp
// BAKED binding evaluation (generated)
public static class Bindings_PlayerHUD
{
    public static void EvaluateAll(ref PlayerData player, ref HUD hud)
    {
        // Computed fields first (toposort order)
        player.HealthPercent = player.Health / (float)player.MaxHealth;

        // Then bindings
        hud.HealthBar.Fill = player.HealthPercent;
        hud.HealthBar.Color = ColorLerp(Red, Green, player.HealthPercent);
    }
}
```

---

### 2.6 Prefab Instances (Overrides)

**Current:**
```csharp
[Pooled]
public partial struct PrefabInstanceComponent : IComponent
{
    public uint SourcePrefabStableId;
    public uint SourcePrefabRevisionAtBuild;
    public uint SourcePrefabBindingsRevisionAtBuild;
    public byte CanvasSizeIsOverridden;

    // Override tracking
    [InlineArray(8)] public struct UShortBuffer { ... }
    public UShortBuffer VariableId;
    public byte OverrideMask;
    public int ValueCount;
}
```

**V2 Solution:**
- Editor: track overrides per-field (OverrideMask pattern)
- Bake: flatten everything (inherited + overrides merged)
- Runtime: no inheritance resolution, just read values

```
EDITOR:
  GoblinWarrior (inherits EnemyUnit)
  ├── Health: 150   [OVERRIDDEN]
  ├── Damage: 20    [OVERRIDDEN]
  └── Speed: 5.0    [INHERITED]

BAKED:
  GoblinWarrior { Health: 150, Damage: 20, Speed: 5.0 }
```

---

## 3. Command System Integration

```
┌─────────────────────────────────────────────────────────────────────────────────────────┐
│                               PROXY MODES                                                │
├─────────────────────────────────────────────────────────────────────────────────────────┤
│                                                                                         │
│  Get<T>()           → Read-only, no side effects                                        │
│                                                                                         │
│  Ref<T>()           → Direct write, marks dirty, no commands                            │
│                       Use: system writes, binding propagation                            │
│                                                                                         │
│  BeginPreview<T>()  → Batched write, marks dirty, no commands                           │
│                       Use: animation playback, scrubbing                                 │
│                                                                                         │
│  BeginChange<T>()   → Batched write, marks dirty, EMITS commands                        │
│                       Use: user editing (inspector, gizmos)                              │
│                                                                                         │
└─────────────────────────────────────────────────────────────────────────────────────────┘
                                        │
                                        ▼
┌─────────────────────────────────────────────────────────────────────────────────────────┐
│                           COMMAND PROCESSORS                                             │
├─────────────────────────────────────────────────────────────────────────────────────────┤
│                                                                                         │
│  UndoProcessor:                                                                         │
│    PropertyChangedCommand → Record delta to undo stack                                  │
│                                                                                         │
│  AutoKeyProcessor:                                                                      │
│    PropertyChangedCommand → if (timeline.IsSelected && !isPlaying && autoKeyEnabled)   │
│                                CreateKeyframe(...)                                       │
│                                                                                         │
│  (Future) NetworkProcessor:                                                             │
│    PropertyChangedCommand → Send to other clients                                       │
│                                                                                         │
└─────────────────────────────────────────────────────────────────────────────────────────┘
```

---

## 4. Dirty Propagation & Toposort

```
BAKE TIME:
  Build dependency graph:
    • PlayerData.Health → PlayerData.HealthPercent (computed)
    • PlayerData.HealthPercent → HUD.HealthBar.Fill (binding)
    • Animation Track → PlayerData.Health (animation)

  Toposort → Evaluation order:
    1. Animation tracks (sample current time)
    2. Computed fields (recompute from inputs)
    3. Bindings (propagate to outputs)


RUNTIME FRAME:
  ┌─────────────────────────────────────────────┐
  │  1. Animation Update                         │
  │     foreach track: BeginPreview, set value   │
  │     (marks dirty, no commands)               │
  └─────────────────────────────────────────────┘
                        │
                        ▼
  ┌─────────────────────────────────────────────┐
  │  2. User Input                               │
  │     Inspector edit: BeginChange, set value   │
  │     (marks dirty, emits command)             │
  └─────────────────────────────────────────────┘
                        │
                        ▼
  ┌─────────────────────────────────────────────┐
  │  3. Flush()                                  │
  │     Evaluate in toposort order:              │
  │       • Computed fields recompute            │
  │       • Bindings propagate                   │
  │     All dirty flags cleared                  │
  └─────────────────────────────────────────────┘
                        │
                        ▼
  ┌─────────────────────────────────────────────┐
  │  4. Command Processing                       │
  │     UndoProcessor: record to stack           │
  │     AutoKeyProcessor: create keyframes       │
  └─────────────────────────────────────────────┘
```

---

## 5. File Mapping: Current → V2

| Current File | V2 Replacement | Notes |
|--------------|----------------|-------|
| `WorkspaceCommands.cs` (430KB) | Split into: | Monolith → Focused concerns |
| | `PropertySystem.cs` | Proxy modes (Get, Ref, BeginPreview, BeginChange) |
| | `CommandBus.cs` | Command emission and processor dispatch |
| | `UndoProcessor.cs` | Undo stack, applies/reverts deltas |
| | `AutoKeyProcessor.cs` | Auto-keying logic |
| | `HierarchyCommands.cs` | Create/delete/move entities |
| `UndoRecord.cs` | `UndoRecord.cs` | Simplified: 3 generic kinds + blob payloads |
| `PropertyValue.cs` | `PropertyValue.cs` | Keep as-is (good design) |
| `PropertyKey.cs` | `PropertyKey.cs` | Keep, add generated PropertyIds |
| `AnimationDocument.cs` | `AnimationDocument.cs` | Keep, integrate with BeginPreview |
| `PrefabVariablesComponent.cs` | Generated structs | UGC schemas → codegen |
| `PrefabBindingsComponent.cs` | Compiled bindings | Expression AST → C# methods |
| `PrefabInstanceComponent.cs` | Flattened at bake | Override tracking → baked values |

---

## 6. Component Mapping: Current → V2

### Components That Become [Schema]

```csharp
// CURRENT
[Pooled]
public partial struct FillComponent : IComponent
{
    public Color32 Color;
    public bool UseGradient;
    public Color32 GradientColorA;
    public Color32 GradientColorB;
    // ...
}

// V2
[Schema]
public partial struct FillComponent
{
    [Property] public Color32 Color;
    [Property] public bool UseGradient;
    [Property] public Color32 GradientColorA;
    [Property] public Color32 GradientColorB;
    // Generator emits: PropertyIds, Diff, Serialize, Deserialize
}
```

### Components That Stay Manual

```csharp
// Structural components (no properties, no undo granularity needed)
public struct NodeComponent : IComponent
{
    public uint StableId;
    public uint ParentStableId;
    public int ChildCount;
    // Internal ECS bookkeeping, not user-editable
}
```

---

## 7. Migration Strategy

### Phase 1: Foundation
1. Implement proxy modes (Get, Ref, BeginPreview, BeginChange)
2. Implement command bus + basic processors
3. Keep existing UndoRecord kinds, add new generic kinds in parallel

### Phase 2: Property System
1. Add [Schema] attribute and generator
2. Convert one component (e.g., TransformComponent) to [Schema]
3. Verify undo, auto-key, animations still work

### Phase 3: Full Migration
1. Convert remaining components to [Schema]
2. Deprecate hardcoded UndoRecordKind entries
3. Simplify WorkspaceCommands.cs

### Phase 4: UGC Baking
1. Implement UGC schema → codegen pipeline
2. Implement prefab flattening at bake
3. Implement compiled bindings

---

## 8. Key Invariants

1. **Commands are for user intent only** - Animation playback, binding propagation, computed fields never emit commands

2. **Toposort is fixed at bake** - No runtime dependency resolution

3. **Flush() is the single propagation point** - All writes mark dirty, evaluation happens once

4. **Same API for code-defined and UGC** - `entity.Get<T>()` works for both

5. **Undo = restore + re-evaluate** - Only store user changes, derived values recompute

---

## 9. Callsite Examples: Current vs V2

### 9.1 Inspector Panel: Editing a Color

**CURRENT (WorkspaceCommands.cs):**
```csharp
// InspectorPanel.cs - Color picker changed
void OnColorPickerChanged(Entity entity, Color32 newColor)
{
    var fillHandle = entity.GetComponent<FillComponent>();
    var before = _world.GetComponent(fillHandle);

    // Direct mutation
    ref var fill = ref _world.GetComponent(fillHandle);
    fill.Color = newColor;

    // Manual undo recording (hardcoded for FillComponent)
    _commands.RecordFillComponentEdit(fillHandle, before, fill);

    // Manual auto-key check (hardcoded field names)
    if (_timeline.IsSelected && _autoKeyEnabled && !_timeline.IsPlaying)
    {
        var track = FindOrCreateTrack(entity, FillColorName);
        SetKeyframe(track, _timeline.CurrentFrame, PropertyValue.FromColor32(newColor));
    }

    // Manual dirty marking for renderer
    _dirtyFlags.MarkFillDirty(entity);
}
```

**V2:**
```csharp
// InspectorPanel.cs - Color picker changed
void OnColorPickerChanged(Entity entity, Color32 newColor)
{
    // BeginChange captures before state, emits command on dispose
    using (var edit = entity.BeginChange<FillComponent>())
    {
        edit.Color = newColor;
    }
    // That's it. Everything else is automatic:
    //   • UndoProcessor receives PropertyChangedCommand, records delta
    //   • AutoKeyProcessor receives command, creates keyframe if appropriate
    //   • Dirty flag set, propagation happens on Flush()
}
```

---

### 9.2 Canvas Panel: Dragging a Shape (Transform Gizmo)

**CURRENT:**
```csharp
// CanvasInteractions.cs - During drag
void OnGizmoDrag(Entity entity, Vector2 delta)
{
    var transformHandle = entity.GetComponent<TransformComponent>();
    ref var transform = ref _world.GetComponent(transformHandle);

    // Accumulate drag
    transform.Position += delta;

    // NO undo recording during drag (would flood the stack)
    // NO auto-keying during drag

    _dirtyFlags.MarkTransformDirty(entity);
}

// On mouse up - commit the change
void OnGizmoDragEnd(Entity entity, Vector2 startPosition, Vector2 endPosition)
{
    var transformHandle = entity.GetComponent<TransformComponent>();
    var after = _world.GetComponent(transformHandle);

    // Reconstruct "before" state
    var before = after;
    before.Position = startPosition;

    // Record undo (hardcoded for TransformComponent)
    _commands.RecordTransformComponentEdit(transformHandle, before, after);

    // Manual auto-key (hardcoded field)
    if (_timeline.IsSelected && _autoKeyEnabled && !_timeline.IsPlaying)
    {
        AutoKeyTransformEdit(entity, before, after);  // 50+ lines of field comparison
    }
}
```

**V2:**
```csharp
// CanvasInteractions.cs - During drag (preview mode)
void OnGizmoDrag(Entity entity, Vector2 delta)
{
    // BeginPreview: no commands, just marks dirty
    using (var preview = entity.BeginPreview<TransformComponent>())
    {
        preview.Position += delta;
    }
    // Bindings/computed fields update on Flush(), but no undo record
}

// On mouse up - commit the change
void OnGizmoDragEnd(Entity entity)
{
    // Now emit the command with before/after delta
    _propertySystem.CommitPreview<TransformComponent>(entity);
    // CommitPreview:
    //   • Compares snapshot from BeginPreview to current state
    //   • Emits PropertyChangedCommand with delta
    //   • UndoProcessor and AutoKeyProcessor react automatically
}
```

---

### 9.3 Animation Playback

**CURRENT:**
```csharp
// AnimationSystem.cs
void UpdatePlayback(float deltaTime)
{
    _currentTime += deltaTime;

    foreach (var track in _timeline.Tracks)
    {
        var value = track.Sample(_currentTime);
        var entity = _targets[track.TargetIndex];

        // Switch on component kind (hardcoded)
        switch (track.ComponentKind)
        {
            case ComponentKind.Transform:
                ref var transform = ref entity.GetComponent<TransformComponent>();
                SetTransformProperty(ref transform, track.PropertyId, value);
                _dirtyFlags.MarkTransformDirty(entity);
                break;

            case ComponentKind.Fill:
                ref var fill = ref entity.GetComponent<FillComponent>();
                SetFillProperty(ref fill, track.PropertyId, value);
                _dirtyFlags.MarkFillDirty(entity);
                break;

            // ... case for every component type
        }
    }

    // Must NOT record undo or auto-key during playback
    // This is implicit (we just don't call RecordXxx methods)
}
```

**V2:**
```csharp
// AnimationSystem.cs
void UpdatePlayback(float deltaTime)
{
    _currentTime += deltaTime;

    foreach (var track in _timeline.Tracks)
    {
        var value = track.Sample(_currentTime);
        var entity = _targets[track.TargetIndex];

        // BeginPreview: marks dirty, no commands (animation shouldn't undo/auto-key)
        using (var preview = entity.BeginPreviewDynamic(track.ComponentKind))
        {
            preview.SetProperty(track.PropertyId, value);
        }
    }

    Flush();  // Propagate all changes in toposort order
}
```

---

### 9.4 Timeline Scrubbing

**CURRENT:**
```csharp
// TimelinePanel.cs - User drags playhead
void OnPlayheadDrag(int frame)
{
    _timeline.CurrentFrame = frame;

    // Sample all tracks at this frame
    foreach (var track in _timeline.Tracks)
    {
        var value = track.SampleAtFrame(frame);
        // ... same component switch as playback

        // NO undo during scrub
        // NO auto-key during scrub
    }
}
```

**V2:**
```csharp
// TimelinePanel.cs - User drags playhead
void OnPlayheadDrag(int frame)
{
    _timeline.CurrentFrame = frame;

    foreach (var track in _timeline.Tracks)
    {
        var value = track.SampleAtFrame(frame);
        var entity = _targets[track.TargetIndex];

        // BeginPreview: perfect for scrubbing (no commands)
        using (var preview = entity.BeginPreviewDynamic(track.ComponentKind))
        {
            preview.SetProperty(track.PropertyId, value);
        }
    }

    Flush();
}
```

---

### 9.5 Keyframe Editing in Timeline

**CURRENT:**
```csharp
// TimelinePanel.cs - User edits keyframe value directly
void OnKeyframeValueChanged(Track track, int frame, PropertyValue newValue)
{
    var oldKeyframe = track.GetKeyframe(frame);
    var before = oldKeyframe.Value;

    // Update keyframe
    track.SetKeyframeValue(frame, newValue);

    // Custom undo for keyframes (yet another UndoRecordKind)
    _commands.RecordKeyframeEdit(track.Id, frame, before, newValue);

    // Re-sample if we're at this frame
    if (_timeline.CurrentFrame == frame)
    {
        // Apply to entity... (same component switch)
    }
}
```

**V2:**
```csharp
// TimelinePanel.cs - User edits keyframe value directly
void OnKeyframeValueChanged(Track track, int frame, PropertyValue newValue)
{
    var before = track.GetKeyframe(frame).Value;

    // Emit command for keyframe change
    _commandBus.Emit(new KeyframeChangedCommand
    {
        TrackId = track.Id,
        Frame = frame,
        Before = before,
        After = newValue
    });

    track.SetKeyframeValue(frame, newValue);

    // If at current frame, update entity via preview
    if (_timeline.CurrentFrame == frame)
    {
        var entity = _targets[track.TargetIndex];
        using (var preview = entity.BeginPreviewDynamic(track.ComponentKind))
        {
            preview.SetProperty(track.PropertyId, newValue);
        }
        Flush();
    }
}
```

---

### 9.6 Auto-Keying Decision

**CURRENT (100+ lines in WorkspaceCommands.cs):**
```csharp
private void AutoKeyFillComponentEdit(FillComponentHandle fillHandle,
    in FillComponent before, in FillComponent after)
{
    var entity = GetEntityForFill(fillHandle);
    var slots = GetFillPropertySlots(entity);

    // Manual field-by-field comparison
    if (!before.Color.Equals(after.Color) && !slots.Color.Component.IsNull)
    {
        AutoKeyIfTimelineSelected(slots.Color, PropertyValue.FromColor32(after.Color));
    }

    if (before.UseGradient != after.UseGradient && !slots.UseGradient.Component.IsNull)
    {
        AutoKeyIfTimelineSelected(slots.UseGradient, PropertyValue.FromBool(after.UseGradient));
    }

    if (!before.GradientColorA.Equals(after.GradientColorA) && ...)
    {
        AutoKeyIfTimelineSelected(slots.GradientColorA, PropertyValue.FromColor32(after.GradientColorA));
    }

    // ... 15 more field comparisons
}
```

**V2 (AutoKeyProcessor - generic, schema-driven):**
```csharp
class AutoKeyProcessor : ICommandProcessor
{
    public void Process(PropertyChangedCommand cmd)
    {
        // Early out if conditions not met
        if (!_timeline.IsSelected) return;
        if (!_autoKeyEnabled) return;
        if (_timeline.IsPlaying) return;

        // Generic: works for any [Schema] component
        var track = FindOrCreateTrack(cmd.Entity, cmd.ComponentKind, cmd.PropertyId);
        SetKeyframe(track, _timeline.CurrentFrame, cmd.After);
    }
}

// The command already contains exactly what changed:
struct PropertyChangedCommand
{
    public Entity Entity;
    public ushort ComponentKind;
    public ulong PropertyId;
    public PropertyKind Kind;
    public PropertyValue Before;
    public PropertyValue After;
}
```

---

### 9.7 Undo/Redo

**CURRENT:**
```csharp
void Undo()
{
    var record = _undoStack.Pop();

    switch (record.Kind)
    {
        case UndoRecordKind.SetFillComponent:
            ref var fill = ref _world.GetComponent(record.FillHandle);
            fill = record.FillBefore;
            _dirtyFlags.MarkFillDirty(GetEntity(record.FillHandle));
            break;

        case UndoRecordKind.SetTransformComponent:
            ref var transform = ref _world.GetComponent(record.TransformHandle);
            transform = record.TransformBefore;
            _dirtyFlags.MarkTransformDirty(GetEntity(record.TransformHandle));
            break;

        // ... case for every UndoRecordKind (29 cases)
    }
}
```

**V2:**
```csharp
void Undo()
{
    var record = _undoStack.Pop();

    switch (record.Kind)
    {
        case UndoKind.Property:
            // Generic: restore single property
            var entity = _entityLookup[record.EntityId];
            ref var component = ref entity.RefDynamic(record.ComponentKind);
            component.SetProperty(record.PropertyId, record.Before);
            break;

        case UndoKind.ComponentSnapshot:
            // Generic: restore entire component from blob
            var blob = _blobStorage.GetSpan(record.PayloadOffset, record.PayloadSize);
            entity.RestoreComponent(record.ComponentKind, blob);
            break;

        case UndoKind.Structural:
            // Handled by HierarchyCommands
            _hierarchyCommands.UndoStructural(record);
            break;
    }

    Flush();  // Re-evaluate computed fields, bindings
}
```

---

### 9.8 UGC Variables: Editor vs Runtime

**EDITOR (Dynamic, allocations OK):**
```csharp
// VariablesPanel.cs - User edits a UGC variable
void OnVariableChanged(Entity prefabInstance, StringHandle variableName, PropertyValue newValue)
{
    // UgcInstance is a managed class in editor
    var instance = _ugcStorage.GetInstance(prefabInstance);
    var before = instance.Values[variableName];

    // BeginChange for undo/auto-key
    using (var edit = instance.BeginChange())
    {
        edit.SetValue(variableName, newValue);
    }

    // Bindings that depend on this variable will update on Flush()
    Flush();
}
```

**RUNTIME (Baked, zero allocation):**
```csharp
// GameplaySystem.cs - Read UGC-defined component
void UpdateEnemies(float deltaTime)
{
    foreach (var entity in _enemyQuery)
    {
        // EnemyUnit was a UGC schema, now a real component
        var enemy = entity.Get<EnemyUnit>();

        // Access like any code-defined component
        if (enemy.Health <= 0)
        {
            Die(entity);
            continue;
        }

        Move(entity, enemy.Speed * deltaTime);
    }
}

// Iterating UGC lists
void ApplyBuffs(Entity entity)
{
    var player = entity.Get<PlayerData>();

    // ActiveBuffs is Span<Buff>, inlined at baked size
    foreach (var buff in player.ActiveBuffs)
    {
        ApplyBuff(buff.Name, buff.Duration, buff.Stacks);
    }
}
```

---

### 9.9 Bindings with Expressions

**EDITOR (Define binding):**
```csharp
// BindingsPanel.cs - User creates expression binding
void CreateHealthBarBinding(Entity player, Entity hudHealthBar)
{
    var binding = new BindingDefinition
    {
        Sources = new[]
        {
            new BindingSource { Path = "PlayerData.Health", Alias = "health" },
            new BindingSource { Path = "PlayerData.MaxHealth", Alias = "maxHealth" }
        },
        Target = new BindingPath { Entity = hudHealthBar, Property = "Fill.Width" },
        Expression = new BindingExpression("@health / @maxHealth * 200")
    };

    _bindings.Add(binding);
}
```

**BAKED (Generated C#):**
```csharp
// GENERATED: Bindings_PlayerHUD.cs
public static class Bindings_PlayerHUD
{
    // Toposorted evaluation order
    public static void EvaluateAll(
        ref PlayerData player,
        ref HUDComponent hud)
    {
        // Computed field (dependency of binding)
        player.HealthPercent = player.Health / (float)player.MaxHealth;

        // Expression binding: @health / @maxHealth * 200
        hud.HealthBar.Width = player.Health / (float)player.MaxHealth * 200f;

        // Color binding: lerp(#FF0000, #00FF00, @healthPercent)
        hud.HealthBar.Color = Color32.Lerp(
            new Color32(255, 0, 0, 255),
            new Color32(0, 255, 0, 255),
            player.HealthPercent);
    }
}
```

**RUNTIME (Called on Flush):**
```csharp
void Flush()
{
    // Evaluate all bindings in toposort order
    foreach (var bindingGroup in _bakedBindings)
    {
        bindingGroup.Evaluate();  // Calls generated EvaluateAll methods
    }

    _dirtyFlags.Clear();
}
```

---

### 9.10 Complete Frame Flow

```csharp
void EditorFrame(float deltaTime)
{
    // ══════════════════════════════════════════════════════════════════════════
    // PHASE 1: INPUT & INTERACTION
    // ══════════════════════════════════════════════════════════════════════════

    // Animation playback (BeginPreview - no commands)
    if (_timeline.IsPlaying)
    {
        _animationSystem.UpdatePlayback(deltaTime);  // Uses BeginPreview
    }

    // User interactions (BeginChange - emits commands)
    if (_inspector.ColorPickerChanged(out var entity, out var newColor))
    {
        using (var edit = entity.BeginChange<FillComponent>())
        {
            edit.Color = newColor;
        }
    }

    if (_canvas.GizmoDragging(out var dragEntity, out var delta))
    {
        using (var preview = dragEntity.BeginPreview<TransformComponent>())
        {
            preview.Position += delta;
        }
    }

    if (_canvas.GizmoDragEnded(out var commitEntity))
    {
        _propertySystem.CommitPreview<TransformComponent>(commitEntity);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PHASE 2: PROPAGATION
    // ══════════════════════════════════════════════════════════════════════════

    Flush();
    // Internally:
    //   1. Evaluate computed fields in toposort order
    //   2. Evaluate bindings in toposort order
    //   3. Clear dirty flags

    // ══════════════════════════════════════════════════════════════════════════
    // PHASE 3: COMMAND PROCESSING
    // ══════════════════════════════════════════════════════════════════════════

    _commandBus.ProcessPending();
    // Dispatches to:
    //   - UndoProcessor: records deltas
    //   - AutoKeyProcessor: creates keyframes if appropriate

    // ══════════════════════════════════════════════════════════════════════════
    // PHASE 4: RENDER
    // ══════════════════════════════════════════════════════════════════════════

    _renderer.Render();
}
```

---

## 10. Summary: Why V2 is Better

| Aspect | Current | V2 |
|--------|---------|-----|
| **Adding new component** | 5 files to modify, 100+ lines | Add `[Schema]`, rebuild |
| **Auto-key logic** | Per-component, hardcoded fields | Generic processor, schema-driven |
| **Undo record kinds** | 29 and growing | 3 generic kinds |
| **Animation playback** | Component switch, manual dirty | BeginPreview + Flush |
| **Scrubbing** | Careful to avoid undo/autokey | BeginPreview (naturally correct) |
| **User editing** | Manual undo + autokey calls | BeginChange (automatic) |
| **UGC variables** | Runtime dictionary lookup | Baked structs, same API |
| **Bindings** | Runtime interpretation | Compiled C# methods |

**The key insight:** V2 makes the *easy thing the right thing*:
- Want undo? Use `BeginChange`
- Don't want undo? Use `BeginPreview` or `Ref`
- The proxy mode you choose determines behavior automatically

---

## Related Documents

- `2025-01-30_V2_Property_System_Spec.md` - Full property system spec
- `2025-01-30_V2_UGC_System_Spec.md` - UGC baking and runtime spec
- `2025-01-30_WorkspaceCommands_Analysis.md` - Current system pain points
