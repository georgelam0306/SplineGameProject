# V2 UGC Property System - Full Spec

**Date:** 2025-01-30

---

## 1. Core Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           SCHEMA DEFINITION                                  │
│                                                                             │
│  [Schema]                                                                   │
│  public partial struct PlayerStats                                          │
│  {                                                                          │
│      [Property] public int Health;                                          │
│      [Property] public int MaxHealth;                                       │
│      [Property] [EditorResizable] public Buff[] Buffs;                      │
│      [ComputedState] public float HealthPercent;                            │
│  }                                                                          │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                    ┌───────────────┴───────────────┐
                    ▼                               ▼
             EDITOR CODEGEN                   RUNTIME CODEGEN
             ├─ List<T> for resizable         ├─ Fixed blob layout
             ├─ PropertyIds                   ├─ Accessor structs
             ├─ Undo integration              ├─ Offset tables
             └─ Inspector bindings            └─ Compiled bindings
```

---

## 2. Schema Annotations

| Annotation | Purpose |
|------------|---------|
| `[Schema]` | Marks struct for code generation |
| `[Property]` | Field exposed to property system |
| `[ComputedState]` | Derived from other fields, auto-recomputed |
| `[EditorResizable]` | List<T> in editor, baked to fixed array at export |
| `[BakeToSamples]` | Pre-sample animation curves at fixed rate |

---

## 3. Proxy Modes

```csharp
// READ-ONLY
var stats = entity.Get<PlayerStats>();
int health = stats.Health;


// REF (direct write, marks dirty, no commands)
ref var stats = ref entity.Ref<PlayerStats>();
stats.Health = 50;


// BEGIN PREVIEW (batched, marks dirty, no commands)
using (var preview = entity.BeginPreview<PlayerStats>())
{
    preview.Health = 50;
}


// BEGIN CHANGE (batched, emits commands)
using (var edit = entity.BeginChange<PlayerStats>())
{
    edit.Health = 50;
}
```

| Mode | Emits Command | Marks Dirty | Use Case |
|------|---------------|-------------|----------|
| `Get<T>()` | - | - | Reading values |
| `Ref<T>()` | ✗ | ✓ | System writes, bindings |
| `BeginPreview<T>()` | ✗ | ✓ | Animation playback, scrubbing |
| `BeginChange<T>()` | ✓ | ✓ | User editing |

---

## 4. Dirty Propagation & Toposort

All writes mark dirty. Propagation happens on `Flush()` in toposorted order.

```
BAKE TIME:
──────────
  Build dependency graph from:
    • Computed field dependencies
    • Binding/expression inputs
    • Animation track targets

  Toposort → Fixed evaluation order


RUNTIME:
────────
  1. Writes mark properties dirty
  2. Flush() evaluates in toposort order:
     • Animation tracks (sample values)
     • Computed fields (recompute)
     • Bindings/expressions (propagate)
```

```csharp
// Generated at bake time
public static class Bindings_PlayerHUD
{
    // Pre-sorted evaluation order
    public static void EvaluateAll(PlayerData* player, HUDData* hud)
    {
        // Computed fields
        player->HealthPercent = player->Health / (float)player->MaxHealth;

        // Bindings
        hud->HealthBar.Fill = player->HealthPercent;
        hud->HealthBar.Color = ColorLerp(Red, Green, player->HealthPercent);
    }
}
```

---

## 5. Command System

Commands are emitted **only** for user changes (BeginChange). Used for:
- Undo/Redo
- Auto-keying
- Networking (future)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                            COMMAND FLOW                                      │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  BeginChange.Dispose()                                                      │
│         │                                                                   │
│         ▼                                                                   │
│  PropertyChangedCommand { Entity, Property, Before, After }                 │
│         │                                                                   │
│         ▼                                                                   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                      COMMAND PROCESSORS                              │   │
│  ├─────────────────────────────────────────────────────────────────────┤   │
│  │                                                                     │   │
│  │  UndoProcessor:                                                     │   │
│  │    → Records delta to undo stack                                    │   │
│  │                                                                     │   │
│  │  AutoKeyProcessor:                                                  │   │
│  │    → if (timeline.IsSelected && autoKeyEnabled && !isPlaying)       │   │
│  │        CreateKeyframe(...)                                          │   │
│  │                                                                     │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

**What emits commands:**
- User property edits (BeginChange)
- Keyframe edits (add/modify/delete)
- Structural changes (create/delete entity)

**What does NOT emit commands:**
- Computed field updates (deterministic)
- Binding propagation (deterministic)
- Animation playback (deterministic)

---

## 6. Undo/Redo

Only user changes are recorded. Derived values recompute on restore.

```csharp
void Undo()
{
    var cmd = _undoStack.Pop();

    // Restore user value
    ref var data = ref cmd.Entity.Ref<T>();
    data.SetProperty(cmd.PropertyId, cmd.Before);

    // Re-evaluate (derived values recompute automatically)
    Flush();
}
```

**Undo records:**
- Property deltas (per-field before/after)
- Keyframe changes
- Structural changes (entity snapshots)

**NOT recorded (recomputed):**
- Computed fields
- Binding outputs
- Animated values during playback

---

## 7. Animation System

### Playback (No Commands)

```csharp
void UpdateAnimation(float deltaTime)
{
    _currentTime += deltaTime;

    // Sample all tracks using BeginPreview (no commands)
    foreach (var track in _tracks)
    {
        using (var preview = track.Entity.BeginPreview<T>())
        {
            preview.SetProperty(track.PropertyId, track.Sample(_currentTime));
        }
    }

    Flush();  // Propagate in toposort order
}
```

### Keyframe Editing (Emits Commands)

```csharp
void SetKeyframeValue(TrackId track, int frame, PropertyValue value)
{
    // Emits command for undo
    _commandBus.Emit(new KeyframeChangedCommand(track, frame, before, value));

    WriteKeyframe(track, frame, value);
    Flush();
}
```

### Auto-Keying (Command Processor Decides)

```csharp
class AutoKeyProcessor : ICommandProcessor
{
    public void Process(PropertyChangedCommand cmd)
    {
        if (!_timeline.IsSelected) return;
        if (!_autoKeyEnabled) return;
        if (_timeline.IsPlaying) return;

        var track = FindOrCreateTrack(cmd.Entity, cmd.PropertyId);
        SetKeyframe(track, _timeline.CurrentFrame, cmd.After);
    }
}
```

### Pre-Baked Samples

```csharp
[Schema]
struct AnimationTrack
{
    [Property]
    AnimationBinding Target;

    [Property]
    int Duration;

    [Property]
    [EditorResizable]
    [BakeToSamples]  // Pre-sample at fixed rate
    Keyframe[] Keyframes;
}
```

Runtime evaluation is just linear lerp between pre-computed samples.

---

## 8. Expression System

### DSL Syntax

```
LITERALS:
  123, 3.14, true, "hello", #FF0000

VARIABLES (prefix with @):
  @health, @maxHealth, @player.name

OPERATORS:
  + - * / == != < > <= >= && || ! ? :

FUNCTIONS:
  min(a, b), max(a, b), clamp(x, lo, hi)
  lerp(a, b, t), remap(x, inLo, inHi, outLo, outHi)
  format("{0} / {1}", @current, @max)
```

### Example Expressions

```
@health / @maxHealth
lerp(#FF0000, #00FF00, @healthPercent)
@health > 20 ? 1.0 : 0.3
clamp((@health + @shield) / @maxHealth, 0, 1)
```

### Binding Definition

```csharp
[Schema]
public struct BindingDefinition
{
    [Property]
    [EditorResizable]
    public BindingSource[] Sources;      // Multiple inputs with aliases

    [Property]
    public BindingPath Target;

    [Property]
    public BindingExpression Expression;
}

[Schema]
public struct BindingSource
{
    [Property]
    public BindingPath Path;             // Variable or property path

    [Property]
    public StringHandle Alias;           // @health, @maxHealth
}
```

### Compilation Pipeline

```
DSL Text → Lexer → Parser → AST → Type Check → Toposort → C# Codegen
```

### Dual-View Editor (Text DSL ↔ Node Graph)

```
            ┌─────────────────┐
            │       AST       │  ← Single source of truth
            └─────────────────┘
                    │
        ┌───────────┴───────────┐
        ▼                       ▼
  ┌───────────┐           ┌───────────┐
  │ Text DSL  │ ◄───────► │ Node Graph│
  └───────────┘           └───────────┘
```

---

## 9. Baking Process

### Editor → Runtime

```
EDITOR (flexible)                    RUNTIME (fixed)
─────────────────                    ───────────────
List<Keyframe>           ──────►     Fixed array in blob
Dynamic bindings         ──────►     Compiled C# methods
String property names    ──────►     PropertyId integers
Expression AST           ──────►     Inlined evaluation code
```

### Memory Layout (Offset Tables)

```
BAKED TIMELINE (variable-size tracks)
┌────────────────────────────────────────────────────────────────────────┐
│  HEADER                                                                │
│  ┌──────────────┬─────────────┬─────────────────────────────────────┐ │
│  │ Name (8)     │ TrackCount  │ Track Offsets [24, 212, 336]        │ │
│  └──────────────┴─────────────┴─────────────────────────────────────┘ │
│                                                                        │
│  TRACK DATA (each at its offset)                                       │
│  ┌────────────────────────────────────────────────────────────────┐   │
│  │ Track[0] @ offset 24                                           │   │
│  │ Track[1] @ offset 212                                          │   │
│  │ Track[2] @ offset 336                                          │   │
│  └────────────────────────────────────────────────────────────────┘   │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 10. Key Principles

1. **One Schema, Two Representations** - Editor (flexible) and runtime (fixed) from same definition

2. **Toposorted Evaluation** - Dependency order computed at bake time, flat array at runtime

3. **Commands for User Intent Only** - Derived values don't emit commands

4. **Undo = Restore + Re-evaluate** - Only store user changes, derived values recompute

5. **Proxy Modes** - Get (read), Ref (direct), BeginPreview (batched), BeginChange (commands)

6. **Decoupled Processors** - Undo, auto-key, etc. are separate processors reacting to commands

7. **Flush for Propagation** - All writes mark dirty, propagation happens once on Flush()

---

## 11. UGC Variables

*[TO BE EXPANDED - Design discussion in progress]*

Key aspects to cover:
- Schema definition (user defines variables in editor)
- Codegen (baked into structs)
- Memory layout (blittable, offsets)
- Loading (memory-mapped, accessor structs)
- Prefab instancing (inheritance, overrides)
- Bindings (UGC variable → property)
- Integration with proxy system

---

## 12. Comparison to Current System

| Aspect | Current (WorkspaceCommands) | V2 |
|--------|----------------------------|-----|
| Undo record kinds | 27+ hardcoded | 2-3 generic |
| Per-component code | Manual for each | Generated from schema |
| Auto-keying | Hardcoded field names | Command processor decides |
| Propagation | Manual per-edit | Automatic via toposort |
| UGC vs Properties | Different code paths | Same proxy pattern |
| File size | 430KB monolith | Split by concern |

---

## Related Documents

- `2025-01-29_V2_UGC_Property_System_Design.md` - Original design discussion
- `2025-01-30_WorkspaceCommands_Analysis.md` - Analysis of current system pain points
