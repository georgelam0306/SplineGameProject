# V2 UGC Property System Design Discussion

**Date:** 2025-01-29

## Overview

This conversation covers the ground-up redesign of a UGC (User-Generated Content) variable/property system. The goal is to create a unified schema system that handles:

- UGC Variables
- Components & Properties
- Serialization
- Sidecar/Computed States
- Versioning
- Undo/Redo
- Blittable Memory Layout
- Prefab Instancing
- Bindings
- Animations
- Memory-Mapped Runtime Loading

---

## Initial Context: V2 Design Recap

### Core Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                        EDITOR                               │
│                                                             │
│  • User defines schemas dynamically                         │
│  • Dynamic string-based access (slow, but acceptable here)  │
│  • Binding definitions stored as paths: "Player.Health"     │
└─────────────────────────────────────────────────────────────┘
                            │
                            │ Export / Compile
                            ▼
┌─────────────────────────────────────────────────────────────┐
│                     CODE GENERATION                         │
│                                                             │
│  • Proxy structs with typed accessors                       │
│  • Precomputed field offsets                                │
│  • StringHandle constants                                   │
│  • Binding propagation methods                              │
│  • Binary schema metadata                                   │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│                       RUNTIME                               │
│                                                             │
│  • Load binary data directly into memory                    │
│  • Use generated proxies: player.Health                     │
│  • Zero parsing, zero hashing, zero dictionaries            │
│  • Bindings are direct memory copies                        │
└─────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

1. **String Interning** - Variable names interned to integer handles (StringHandle)
2. **Schema-Based Flat Memory Layout** - User-defined structs become computed layouts with offsets
3. **Generated Proxy Structs** - Typed accessors over raw bytes
4. **Dual Access Patterns** - Raw bytes + schema for editor, generated proxy for runtime

---

## Codebase Analysis Summary

### Existing Systems Analyzed

1. **Property System** - Source-generated property metadata via `Property.Generator`, `PropertyDispatcher` with generated switch statements
2. **UGC Variables** - `PrefabVariablesComponent` stores user-defined variables, `PrefabBindingsComponent` stores variable-to-property bindings
3. **Undo/Redo** - Record-based command pattern with pending edit batching, component snapshots for complex changes
4. **Animation** - `AnimationLibraryComponent` with inline arrays, targets properties via PropertyId
5. **Serialization** - Binary chunks, string table, schema versioning, StableId refs
6. **Prefab Instancing** - Variable inheritance, override mask (64-bit), binding resolution
7. **Computed State** - `[ComputedState]` fields, slab separation, `RecomputeAll()`
8. **Memory Layout** - `[GpuStruct]` for std430 layout, `StructLayout`, `FieldOffset`

---

## Undo/Redo Architecture

### The Fundamental Tradeoff

```
DELTA APPROACH                    SNAPSHOT APPROACH
──────────────                    ─────────────────

Store what changed:               Store entire state:
{ field, before, after }          { before_blob, after_blob }

✓ Small storage                   ✓ Always correct
✓ Shows what changed              ✓ Schema-change proof
✗ Schema must be stable           ✗ Large storage
✗ Edge cases on delete/rename     ✗ No detail visibility
```

### Current System (Two Tiers)

| Tier | Target | Undo Granularity | Storage |
|------|--------|------------------|---------|
| **Code-generated properties** | Component + PropertyId | Per-field delta | PropertyValue before/after |
| **UGC variables** | Whole component | Full blob snapshot | byte[] before/after |

### Why Snapshots for UGC Variables

- No compile-time PropertyId for dynamic schemas
- Blob snapshot is immune to schema changes
- Simpler, always correct, no edge cases

### Animation Undo

Animation uses component snapshots because:
- Animation edits are often compound (multi-field)
- Auto-keying couples property + animation changes
- Structural changes (add/delete keyframe) are common
- Snapshot guarantees correctness

---

## Schema-Driven Animation Design

### The Key Insight: Editor vs Runtime Are Different Problems

```
EDITOR TIME                          RUNTIME
───────────────────                  ─────────────────────

Needs:                               Needs:
├─ Flexibility (add/remove)          ├─ Speed (evaluate fast)
├─ Undo/redo                         ├─ Memory efficiency
├─ Inspector integration             ├─ Blittable (memory-mapped)
└─ Doesn't need to be fast           └─ Fixed layout

Can use:                             Can use:
├─ List<T>                           ├─ Fixed arrays
├─ Managed objects                   ├─ Pointer arithmetic
└─ Dynamic allocation                └─ Precomputed offsets
```

### The `[EditorResizable]` Annotation

```csharp
[Schema]
public partial struct AnimationTrack
{
    [Property]
    public AnimationBinding Target;

    [Property]
    [EditorResizable]  // List<T> in editor, baked at export
    public Keyframe[] Keyframes;
}
```

From this single definition, codegen produces:
1. **Editor Storage** - `List<T>` backed, add/remove methods
2. **Editor Accessors** - PropertyIds for all fields
3. **Bake Method** - `CalculateSize()` and `Bake()` methods
4. **Runtime Accessor** - Handles variable-size data via offset table

---

## Baking Process

### Step-by-Step

```
STEP 1: Calculate total size
─────────────────────────────
Track fixed part:   sizeof(AnimationBinding) + sizeof(int) = 28 bytes
Keyframes:          5 × sizeof(Keyframe) = 160 bytes
Total:              188 bytes

STEP 2: Allocate blob
byte[] blob = new byte[188];

STEP 3: Write fixed fields
Offset 0:  Write Target (24 bytes)
Offset 24: Write KeyframeCount = 5 (4 bytes)

STEP 4: Write keyframes
Offset 28:  Write Keyframe[0] (32 bytes)
Offset 60:  Write Keyframe[1] (32 bytes)
...
```

### Memory Layout (Single Track)

```
BAKED BLOB (188 bytes)
┌────────────────────────────────────────────────────────────────────────┐
│  ┌─────────────────────────┬──────────────┐                           │
│  │   Target (24 bytes)     │ Count (4)    │                           │
│  └─────────────────────────┴──────────────┘                           │
│  Offset: 0                   Offset: 24                                │
│                                                                        │
│  ┌──────────────┬──────────────┬──────────────┬──────────────┬──────┐ │
│  │  Keyframe 0  │  Keyframe 1  │  Keyframe 2  │  Keyframe 3  │  K4  │ │
│  │  32 bytes    │  32 bytes    │  32 bytes    │  32 bytes    │ 32b  │ │
│  └──────────────┴──────────────┴──────────────┴──────────────┴──────┘ │
│  Offset: 28      Offset: 60     Offset: 92     Offset: 124    156     │
└────────────────────────────────────────────────────────────────────────┘
```

### Nested Variable-Size Data (Offset Table)

For nested resizable arrays (e.g., Timeline with multiple Tracks of different sizes):

```
BAKED TIMELINE (620 bytes)
┌────────────────────────────────────────────────────────────────────────┐
│  HEADER (24 bytes)                                                     │
│  ┌──────────────┬─────────────┬─────────────────────────────────────┐ │
│  │ Name (8)     │ TrackCount  │ Track Offsets (3 × 4 bytes)         │ │
│  │ "Walk"       │    = 3      │ [24, 212, 336]                      │ │
│  └──────────────┴─────────────┴─────────────────────────────────────┘ │
│                                                                        │
│  TRACK DATA                                                            │
│  ┌────────────────────────────────────────────────────────────────┐   │
│  │ Track[0] @ offset 24 (188 bytes) - 5 keyframes                 │   │
│  └────────────────────────────────────────────────────────────────┘   │
│  ┌────────────────────────────────────────────────────────────────┐   │
│  │ Track[1] @ offset 212 (124 bytes) - 3 keyframes                │   │
│  └────────────────────────────────────────────────────────────────┘   │
│  ┌────────────────────────────────────────────────────────────────┐   │
│  │ Track[2] @ offset 336 (284 bytes) - 8 keyframes                │   │
│  └────────────────────────────────────────────────────────────────┘   │
└────────────────────────────────────────────────────────────────────────┘
```

---

## Generated Code Examples

### Editor Storage (Generated)

```csharp
public class AnimationTrack_Editor
{
    public AnimationBinding Target;
    public List<Keyframe> Keyframes = new();

    public void AddKeyframe(Keyframe k) => Keyframes.Add(k);
    public void RemoveKeyframe(int index) => Keyframes.RemoveAt(index);
}
```

### Bake Method (Generated)

```csharp
public static class AnimationTrack_Baker
{
    public static int CalculateBakedSize(AnimationTrack_Editor editor)
    {
        int size = 0;
        size += Unsafe.SizeOf<AnimationBinding>();
        size += sizeof(int);
        size += editor.Keyframes.Count * Unsafe.SizeOf<Keyframe>();
        return size;
    }

    public static void Bake(AnimationTrack_Editor editor, Span<byte> dest)
    {
        int offset = 0;

        Unsafe.WriteUnaligned(ref dest[offset], editor.Target);
        offset += Unsafe.SizeOf<AnimationBinding>();

        Unsafe.WriteUnaligned(ref dest[offset], editor.Keyframes.Count);
        offset += sizeof(int);

        foreach (var keyframe in editor.Keyframes)
        {
            Unsafe.WriteUnaligned(ref dest[offset], keyframe);
            offset += Unsafe.SizeOf<Keyframe>();
        }
    }
}
```

### Runtime Accessor (Generated)

```csharp
public unsafe ref struct BakedAnimationTrack_Accessor
{
    private byte* _data;

    public BakedAnimationTrack_Accessor(byte* data) => _data = data;

    public ref AnimationBinding Target
        => ref Unsafe.AsRef<AnimationBinding>(_data);

    public int KeyframeCount
        => Unsafe.ReadUnaligned<int>(_data + 24);

    public ref Keyframe GetKeyframe(int index)
    {
        if ((uint)index >= (uint)KeyframeCount)
            throw new IndexOutOfRangeException();

        byte* ptr = _data + 28 + (index * Unsafe.SizeOf<Keyframe>());
        return ref Unsafe.AsRef<Keyframe>(ptr);
    }

    public int TotalSize => 28 + KeyframeCount * Unsafe.SizeOf<Keyframe>();
}
```

---

## Complete Architecture Diagram

```
SCHEMA (one definition)
    │
    │ [EditorResizable] annotation
    │
    ├──────────────────────────────────────────────────────────┐
    │                                                          │
    ▼                                                          ▼
EDITOR CODEGEN                                          BAKE CODEGEN
    │                                                          │
    ├─ List<T> storage                                         ├─ CalculateSize()
    ├─ Add/Remove methods                                      ├─ Bake() method
    ├─ PropertyIds                                             ├─ Offset tables for nested
    └─ Undo integration                                        └─ Accessor structs
    │                                                          │
    ▼                                                          ▼
Editor uses flexible                                    Runtime loads blob
data during editing                                     with exact sizes
```

---

## Open Topics (Not Yet Covered)

1. **Runtime Animation API** - Callsite usage for playing animations
2. **Editor Timeline Scrubbing** - How scrubbing works with editor data
3. **Binding Compilation** - How bindings get baked
4. **Schema Versioning** - Migration between schema versions
5. **Hot Reload** - Editor preview with baked vs unbaked data

---

## Runtime Animation API

### What "Playing an Animation" Means

```
EVERY FRAME:
1. Figure out what frame we're on (based on time)
2. For each track in the animation:
   a. Evaluate the track at that frame → get a value
   b. Write that value to the target property
```

### Runtime Accessor Structs

```csharp
public unsafe ref struct BakedTimeline_Accessor
{
    private byte* _data;

    public StringHandle Name => Unsafe.ReadUnaligned<StringHandle>(_data);
    public int Duration => Unsafe.ReadUnaligned<int>(_data + 8);
    public int TrackCount => Unsafe.ReadUnaligned<int>(_data + 12);

    public BakedTrack_Accessor GetTrack(int index)
    {
        int offset = Unsafe.ReadUnaligned<int>(_data + 16 + index * 4);
        return new BakedTrack_Accessor(_data + offset);
    }
}

public unsafe ref struct BakedTrack_Accessor
{
    private byte* _data;

    public AnimationBinding Target => Unsafe.ReadUnaligned<AnimationBinding>(_data);
    public int KeyframeCount => Unsafe.ReadUnaligned<int>(_data + 24);

    public ref Keyframe GetKeyframe(int index)
    {
        return ref Unsafe.AsRef<Keyframe>(_data + 28 + index * sizeof(Keyframe));
    }

    public PropertyValue Evaluate(float frame)
    {
        // Find surrounding keyframes, interpolate
    }
}
```

### Compiled Bindings (Optimized)

Resolve targets once at load time, not every frame:

```csharp
public struct CompiledTrack
{
    public BakedTrack_Accessor Track;
    public byte* TargetPtr;      // Resolved: direct pointer to component
    public int TargetOffset;     // Resolved: offset within component
    public PropertyKind Kind;
}

// At load time
CompiledTrack[] CompileTracks(BakedTimeline_Accessor timeline, World world);

// At runtime - no lookup, just evaluate and write
void ApplyCompiledTrack(ref CompiledTrack compiled, float frame)
{
    PropertyValue value = compiled.Track.Evaluate(frame);
    byte* dest = compiled.TargetPtr + compiled.TargetOffset;
    // Write based on Kind
}
```

### High-Level API

```csharp
public class AnimationController
{
    private BakedTimeline_Accessor _timeline;
    private CompiledTrack[] _compiledTracks;
    private float _currentTime;

    public void Load(byte* bakedData, World world);
    public void Play();
    public void Pause();
    public void Stop();
    public void SetTime(float time);
    public void Update(float deltaTime);
    public void Apply();
}
```

---

## Editor Timeline Scrubbing

### Editor vs Runtime Difference

| Aspect | Editor | Runtime |
|--------|--------|---------|
| **Data structure** | `List<Keyframe>` | Baked blob with offsets |
| **Evaluation** | Same algorithm | Same algorithm |
| **Target resolution** | Via workspace + PropertyDispatcher | Compiled pointers |
| **Undo support** | Yes (for edits, not scrub) | No (read-only) |

### Scrubbing Flow

```
USER DRAGS SCRUBBER TO FRAME 32
         │
         ▼
1. Update current frame: _currentFrame = 32
         │
         ▼
2. Evaluate all tracks: value = Evaluate(track, 32)
         │
         ▼
3. Write to properties (no undo - preview only)
         │
         ▼
4. UI updates (preview shows new pose)
```

### Scrubbing vs Editing

```
SCRUBBING (preview only)                 EDITING (creates undo)
────────────────────────                 ─────────────────────

User drags scrubber                      User changes keyframe value
    │                                        │
    ▼                                        ▼
Write values directly                    Record undo delta
(no undo record)                         Write value
```

**Scrubbing doesn't create undo records** because:
- User is just previewing, not making permanent changes
- Scrubbing rapidly would flood undo stack
- The "true" values are in the keyframes, not the current preview

### Auto-Keying During Scrub

When user edits a property WHILE scrubbing:

```
1. User scrubs to frame 32
2. User drags PositionX slider from 100 to 150
3. System detects: timeline selected & auto-key enabled
4. Find or create keyframe at frame 32
5. Create compound undo record (property change + keyframe add/update)
```

---

## Advanced: Pre-Baked Samples at Fixed Rate

### The Concept

Instead of storing keyframes with complex interpolation (cubic, step), **sample the curve at a fixed rate** so runtime only does linear lerp.

```
ORIGINAL (complex interpolation)         BAKED (linear samples)
────────────────────────────────         ─────────────────────

Keyframes with cubic ease-in-out         Sample every frame:
                                         Sample 0:  Value 0.00
Runtime must:                            Sample 1:  Value 3.45
- Find surrounding keyframes             Sample 2:  Value 7.12
- Compute cubic hermite                  ...
- Handle step vs linear vs cubic         Sample 60: Value 100.00

                                         Runtime just:
                                         - Lerp between adjacent samples
```

### Memory Layout

```
BAKED TRACK (sampled at 60fps, 1 second = 61 samples)
┌────────────────────────────────────────────────────────────────────────┐
│ Target (24 bytes) │ Count=61 │ Rate=60 │ S0 │ S1 │ S2 │ ... │ S60    │
└────────────────────────────────────────────────────────────────────────┘

Total: 24 + 4 + 4 + (61 × 16) = 1008 bytes for 1 second of animation
```

### Runtime Evaluation (Trivial)

```csharp
public PropertyValue Evaluate(float frame)
{
    frame = Math.Clamp(frame, 0, SampleCount - 1);
    int left = (int)frame;
    int right = Math.Min(left + 1, SampleCount - 1);
    float t = frame - left;

    return Lerp(GetSample(left), GetSample(right), t);
}
```

No cubic math. No interpolation mode checks. No keyframe search. Just lerp.

---

## Advanced: Baking During Editor Time

### The Concept

Instead of separate editor and runtime representations:
- Editor stores **both** keyframes (for editing) AND samples (for preview)
- When keyframes change, immediately re-bake to samples
- Editor preview uses samples (same as runtime)
- No separate export step needed

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         UNIFIED TRACK                                   │
│                                                                         │
│  ┌─────────────────────────────┐   ┌─────────────────────────────────┐ │
│  │   AUTHORING DATA            │   │   BAKED DATA                    │ │
│  │   (for editing UI)          │   │   (for evaluation)              │ │
│  │                             │   │                                 │ │
│  │   List<Keyframe> Keyframes  │──►│   PropertyValue[] Samples      │ │
│  │                             │   │   (rebuilt when keyframes change)│ │
│  └─────────────────────────────┘   └─────────────────────────────────┘ │
│                                                                         │
│  Editor preview: uses Samples                                           │
│  Runtime: uses Samples                                                  │
│  Same evaluation code!                                                  │
└─────────────────────────────────────────────────────────────────────────┘
```

### The Data Structure

```csharp
public class AnimationTrack
{
    // === AUTHORING (for keyframe editing) ===
    public AnimationBinding Target;
    public List<Keyframe> Keyframes = new();

    // === BAKED (for evaluation) ===
    public int SampleRate;
    public int Duration;
    public PropertyValue[] Samples;

    private bool _samplesDirty = true;

    public void MarkDirty() => _samplesDirty = true;

    public void EnsureBaked()
    {
        if (!_samplesDirty) return;
        RebakeSamples();
        _samplesDirty = false;
    }

    public PropertyValue Evaluate(float frame)
    {
        EnsureBaked();
        // Linear lerp between samples
    }
}
```

### Incremental Rebake (Optimization)

If user edits one keyframe, only rebake the affected region:

```csharp
public void RebakeSamplesInRange(int startFrame, int endFrame)
{
    for (int frame = startFrame; frame <= endFrame; frame++)
    {
        Samples[frame] = EvaluateKeyframes(frame);
    }
}
```

### Combined Schema Annotation

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
    [BakeToSamples]        // ← NEW: indicates sample baking
    Keyframe[] Keyframes;
}
```

### Benefits Summary

| Aspect | Before | After |
|--------|--------|-------|
| **Runtime evaluation** | Keyframe search + cubic math | Array index + lerp |
| **Editor/Runtime parity** | Different code paths | Same code path |
| **Preview accuracy** | Might differ from runtime | Identical to runtime |
| **Export step** | Required | Optional (samples already baked) |
| **Code complexity** | Two evaluators | One evaluator |

---

## Key Principles Established

1. **One Schema, Two Representations** - Same schema generates both editor (flexible) and runtime (fixed) code
2. **`[EditorResizable]` Annotation** - Marks arrays that need List<T> in editor, baked at export
3. **Offset Tables for Nested Data** - Variable-size nested structures use offset tables for O(1) access
4. **Baking is Serialization** - Calculate size, allocate blob, write sequentially
5. **Runtime is Read-Only** - Baked data is immutable, accessor structs provide typed access
6. **`[BakeToSamples]` Annotation** - Pre-sample curves at fixed rate for trivial runtime evaluation
7. **Editor-Time Baking** - Rebake samples when keyframes change for unified editor/runtime code path

---

## Expression System for Data Binding

### The Problem

Bindings are currently direct value copies. But we often need transformations:

```
Health (0-100) → HealthBar.Width (0-500 pixels)    Need: Multiply by 5
Health (int) → HealthText.Text (string)            Need: Format as "HP: {0}"
IsAlive (bool) → Opacity (float)                   Need: Convert true→1.0, false→0.3
```

### Expression Categories

| Category | Examples | Input → Output |
|----------|----------|----------------|
| **Math (1 input)** | Add, Multiply, Negate, Abs, Clamp | num → num |
| **Math (2 inputs)** | Add2, Div2, Min2, Max2, Pow | 2 num → num |
| **Math (3 inputs)** | Lerp, Clamp, Remap | 3+ num → num |
| **Comparison** | GT, LT, EQ, InRange | 2 num → bool |
| **Conditional** | Ternary | bool + 2 any → any |
| **Conversion** | ToFloat, ToInt, ToBool, ToString | any → typed |
| **String** | Format, Concat | N any → string |
| **Color** | Lerp, Tint, FromRGB | varies → Color |
| **Easing** | EaseIn, EaseOut, EaseInOut | float → float |
| **Custom** | C# expression | N any → any |

### Binding with Expression

```csharp
[Schema]
public struct BindingDefinition
{
    [Property]
    [EditorResizable]
    public BindingSource[] Sources;      // Multiple sources

    [Property]
    public BindingPath Target;

    [Property]
    public BindingExpression Expression; // Transformation
}

[Schema]
public struct BindingSource
{
    [Property]
    public BindingPath Path;             // Variable or property path

    [Property]
    public StringHandle Alias;           // Name used in expression: "health", "maxHealth"
}
```

### Expression Library

Reusable predefined expressions:

```csharp
[Schema]
public struct ExpressionLibrary
{
    [Property]
    [EditorResizable]
    public ExpressionDefinition[] Expressions;
}

[Schema]
public struct ExpressionDefinition
{
    [Property]
    public StringHandle Name;            // "HealthToWidth", "PercentToFill"

    [Property]
    public BindingExpression Expression;

    [Property]
    public StringHandle Description;     // For UI tooltip
}
```

### Generated Code (Baked)

```csharp
// INPUT: Binding with two sources
Sources: [Player.Health, Player.MaxHealth]
Expression: Divide2
Target: HUD.HealthBar.Fill

// OUTPUT: Generated code
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void Binding_HealthPercent(byte* playerBlob, byte* hudBlob)
{
    int health = *(int*)(playerBlob + 0);
    int maxHealth = *(int*)(playerBlob + 4);

    float result = health / (float)maxHealth;

    *(float*)(hudBlob + 24) = result;
}
```

### Multi-Input Expression Example

```
Sources:
  [0] Path: "Player.Health",    Alias: "health"
  [1] Path: "Player.MaxHealth", Alias: "maxHealth"

Expression:
  Kind: Custom
  Code: "health / (float)maxHealth"

Target: "HUD.HealthBar.Fill"
```

### Cascading Bindings

One binding's output feeds another's input:

```
Variable: Player.Health (int)
    │
    ├──► Binding A: Divide by MaxHealth → HealthPercent (float)
    │                     │
    │                     └──► Binding B: Lerp(Red, Green, t) → HealthColor
    │                     │
    │                     └──► Binding C: Multiply by 500 → HealthBarWidth
```

Requires topological sort at bake time to determine evaluation order.

---

## DSL Expression Editor (Coda-Style)

### Syntax

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
  abs(x), floor(x), ceil(x), round(x), sqrt(x)
  format("HP: {0}/{1}", @health, @maxHealth)
  rgb(r, g, b), colorlerp(#FF0000, #00FF00, t)
  easein(t), easeout(t), easeinout(t)
```

### Example Expressions

```
SIMPLE MATH:
  @health / @maxHealth
  @baseDamage + @bonusDamage

CONDITIONALS:
  @health > 20 ? 1.0 : 0.3
  @isAlive ? #00FF00 : #FF0000

FUNCTIONS:
  clamp(@health, 0, 100)
  lerp(0, 500, @healthPercent)
  remap(@health, 0, 100, 0, 1)

STRING FORMATTING:
  format("HP: {0}", @health)
  format("{0} / {1}", @currentAmmo, @maxAmmo)

COLOR:
  lerp(#FF0000, #00FF00, @healthPercent)
  @healthPercent < 0.25 ? #FF0000 : #00FF00

COMPLEX:
  (@baseDamage + @bonusDamage) * @damageMultiplier
  clamp((@health + @shield) / @maxHealth, 0, 1)
```

### Editor UI Features

```
┌─────────────────────────────────────────────────────────────────────────┐
│ EXPRESSION EDITOR                                                       │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  Available Variables:                                                   │
│  ● @health (int)  ● @maxHealth (int)  ● @mana (int)                    │
│                                                                         │
│  Expression:                                                            │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │  [ @health ] / [ @maxHealth ] * [ 100 ]                         │   │
│  │     (int)         (int)          (int)                          │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  💡 Type: float                                                        │
│  📊 Preview: health=75, maxHealth=100 → 75.0                          │
│                                                                         │
│  Quick Insert: [+ -] [* /] [clamp] [lerp] [format] [? :]              │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

**Key UX Elements:**
- **Tokens/Pills** - Variables and functions as colored pills
- **Autocomplete** - Type `@` for variables, function names for signatures
- **Inline Type Hints** - See types as you build
- **Live Preview** - See result with sample values
- **Error Highlighting** - Invalid expressions show inline errors

### Autocomplete

When user types `@`:
```
┌───────────────────────────────────────────────────────────┐
│ ▶ @health        int      Player's current health         │
│   @healthMax     int      Maximum health                   │
│   @healthPercent float    Health as 0-1                    │
│   @mana          int      Current mana                     │
└───────────────────────────────────────────────────────────┘
```

When user types function name:
```
┌───────────────────────────────────────────────────────────┐
│ lerp(a, b, t) → float                                     │
│                                                           │
│ Linear interpolation between a and b.                     │
│ t=0 returns a, t=1 returns b.                             │
│                                                           │
│ Parameters:                                               │
│   a: float  - Start value                                 │
│   b: float  - End value                                   │
│   t: float  - Interpolant (0-1)                          │
└───────────────────────────────────────────────────────────┘
```

### Compilation Pipeline

```
DSL Text → Lexer → Parser → AST → Type Check → C# Codegen
```

**Example - Full Flow:**

```
User writes:
  lerp(#FF0000, #00FF00, @health / @maxHealth)

Parsed AST:
  FunctionCall: lerp
  ├── Arg 0: ColorLiteral(#FF0000)
  ├── Arg 1: ColorLiteral(#00FF00)
  └── Arg 2: BinaryExpr(/)
             ├── Left: VariableRef(@health)
             └── Right: VariableRef(@maxHealth)

  ResultType: Color32

Generated C#:
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static Color32 Expr_HealthColor(int health, int maxHealth)
  {
      return ColorLerp(
          new Color32(255, 0, 0, 255),
          new Color32(0, 255, 0, 255),
          health / (float)maxHealth
      );
  }
```

### Error Messages

```
"Cannot divide int by string"
   @health / @playerName
             ~~~~~~~~~~~

"Function 'lerp' expects 3 arguments, got 2"
   lerp(@a, @b)
   ~~~~~~~~~~~~

"Unknown variable '@heatlh' - did you mean '@health'?"
   @heatlh * 2
   ~~~~~~~
```

---

## Expression System Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    EXPRESSION SYSTEM ARCHITECTURE                       │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  EDITOR TIME                                                            │
│  ───────────                                                            │
│  • Expression Library (reusable expressions)                            │
│  • DSL Editor with autocomplete, type hints, preview                    │
│  • Interpreted evaluation for live preview                              │
│                                                                         │
│  BAKE TIME                                                              │
│  ─────────                                                              │
│  • Parse DSL to AST                                                     │
│  • Type check all expressions                                           │
│  • Detect shared subexpressions                                         │
│  • Topologically sort bindings                                          │
│  • Generate optimized C# with inlined expressions                       │
│                                                                         │
│  RUNTIME                                                                │
│  ───────                                                                │
│  • Fully inlined, no switch, no function pointers                       │
│  • Just math and memory writes                                          │
│  • Bindings_PlayerHUD.ApplyAll(playerBlob, hudBlob);                   │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Key Principles Established

1. **One Schema, Two Representations** - Same schema generates both editor (flexible) and runtime (fixed) code
2. **`[EditorResizable]` Annotation** - Marks arrays that need List<T> in editor, baked at export
3. **Offset Tables for Nested Data** - Variable-size nested structures use offset tables for O(1) access
4. **Baking is Serialization** - Calculate size, allocate blob, write sequentially
5. **Runtime is Read-Only** - Baked data is immutable, accessor structs provide typed access
6. **`[BakeToSamples]` Annotation** - Pre-sample curves at fixed rate for trivial runtime evaluation
7. **Editor-Time Baking** - Rebake samples when keyframes change for unified editor/runtime code path
8. **Expression Library** - Reusable named expressions for common transformations
9. **DSL Expressions** - User-friendly syntax with autocomplete, type checking, live preview
10. **Multi-Input Expressions** - Combine multiple sources with aliases for powerful computed bindings

---

## Open Questions

1. **Sample rate** - Fixed 60fps? Or per-animation configurable?
2. **Compression** - Worth it for constant regions (RLE)?
3. **Streaming** - For very long animations, stream samples from disk?
4. **Schema versioning** - Migration between schema versions
5. **Expression security** - Whitelist operations for custom C#?
6. **Expression chaining** - Single expression vs chain of expressions?
7. **Bidirectional expressions** - Can expressions work in reverse for two-way binding?

---

## Dual-View Editors: Text DSL ↔ Node Graph

### The Architecture: Shared AST

Both text and node graph views are representations of the same underlying AST:

```
                    ┌─────────────────┐
                    │       AST       │  ← Single source of truth
                    │  (Expression)   │
                    └─────────────────┘
                            │
            ┌───────────────┴───────────────┐
            ▼                               ▼
    ┌───────────────┐               ┌───────────────┐
    │   Text View   │               │  Graph View   │
    │   (DSL)       │               │  (Nodes)      │
    └───────────────┘               └───────────────┘
            │                               │
            │         on edit               │
            └───────────► AST ◄─────────────┘
                          │
                          ▼
                    regenerate other view
```

### AST Node Types → Graph Nodes

```
AST Node                          Graph Node
────────────────────────────────────────────────────────
VariableRef(@health)         →    [● health    ]──○
                                  (source node, no inputs)

Literal(42)                  →    [● 42        ]──○
                                  (constant node)

BinaryExpr(+, left, right)   →    ○──┬──[  +  ]──○
                                  ○──┘
                                  (2 inputs, 1 output)

FunctionCall(lerp, a, b, t)  →    ○──┬
                                  ○──┼──[ lerp ]──○
                                  ○──┘
                                  (N inputs based on signature)
```

### Round-Trip Example

```
USER TYPES:
  lerp(#FF0000, #00FF00, @health / @maxHealth)

PARSE → AST:
  FunctionCall: lerp
  ├── Arg 0: ColorLiteral(#FF0000)
  ├── Arg 1: ColorLiteral(#00FF00)
  └── Arg 2: BinaryExpr(/)
             ├── VariableRef(@health)
             └── VariableRef(@maxHealth)

AUTO-LAYOUT → GRAPH:
  ┌────────────┐
  │ #FF0000    │──────────┐
  └────────────┘          │
  ┌────────────┐          ▼
  │ #00FF00    │────────[lerp]────○ output
  └────────────┘          ▲
                          │
  ┌────────────┐          │
  │ @health    │───[ ÷ ]──┘
  └────────────┘     ▲
  ┌────────────┐     │
  │ @maxHealth │─────┘
  └────────────┘

USER EDITS GRAPH: Adds a clamp(0, 1) after the divide

GRAPH → AST → TEXT (regenerated):
  lerp(#FF0000, #00FF00, clamp(@health / @maxHealth, 0, 1))
```

### Layout and Formatting

| Problem | Solution |
|---------|----------|
| Graph loses node positions on text edit | Auto-layout always, or store layout separately with AST node matching |
| Text loses formatting on graph edit | Always reformat (pretty-print from AST) |

Recommendation: **Auto-layout graph, always reformat text** - simpler and more predictable.

---

## Abstract Syntax Trees (AST) Deep Dive

### The Problem AST Solves

Text is flat. You can't ask "what's the left operand?" of a string.

```
CONCRETE SYNTAX (what you type)        ABSTRACT SYNTAX (what it means)
─────────────────────────────────      ─────────────────────────────────
"@health / @maxHealth * 100"                  [*]
                                             /   \
Includes whitespace, parens,               [/]   [100]
exact characters                          /   \
                                    [health] [maxHealth]
```

**Abstract** = we keep semantic structure, discard surface details.

### Node Types

```csharp
// Leaf nodes (no children)
struct LiteralNode { object Value; }           // 42, "hello", #FF0000
struct VariableNode { string Name; }           // @health

// Internal nodes (have children)
struct BinaryExprNode
{
    BinaryOp Operator;   // Add, Mul, Div, etc.
    AstNode Left;
    AstNode Right;
}

struct FunctionCallNode
{
    string FunctionName;
    AstNode[] Arguments;
}
```

### Parsing: Text → Tokens → AST

```
INPUT:  "@health / @maxHealth * 100"

STEP 1 - LEXING:
  [Variable: "health"]
  [Operator: "/"]
  [Variable: "maxHealth"]
  [Operator: "*"]
  [Literal: 100]

STEP 2 - PARSING (respecting precedence):
  BinaryExpr(Multiply)
  ├── left: BinaryExpr(Divide)
  │         ├── left: Variable("health")
  │         └── right: Variable("maxHealth")
  └── right: Literal(100)
```

### Why Trees?

1. **Multiple passes** - Type check, optimize, generate code (without re-parsing)
2. **Transformation** - Replace nodes (e.g., `a/b` → `a * (1/b)`)
3. **Introspection** - "What variables does this use?" → walk tree, collect Variable nodes
4. **Multiple outputs** - Same AST → interpreted eval, C# codegen, node graph

### Walking the Tree

```csharp
int Evaluate(AstNode node, Dictionary<string, int> variables)
{
    switch (node)
    {
        case LiteralNode lit:
            return lit.Value;

        case VariableNode var:
            return variables[var.Name];

        case BinaryExprNode bin:
            int left = Evaluate(bin.Left, variables);
            int right = Evaluate(bin.Right, variables);
            return bin.Operator switch
            {
                BinaryOp.Add => left + right,
                BinaryOp.Mul => left * right,
                BinaryOp.Div => left / right,
            };
    }
}
```

### Data Structure for Runtime (Blittable)

```csharp
// Flat array with indices instead of pointers
AstNode[] nodes;

nodes[0] = { Kind: Multiply, Left: 1, Right: 4 }
nodes[1] = { Kind: Divide, Left: 2, Right: 3 }
nodes[2] = { Kind: Variable, Name: "health" }
nodes[3] = { Kind: Variable, Name: "maxHealth" }
nodes[4] = { Kind: Literal, Value: 100 }

// Tree structure:
//        [0:*]
//       /     \
//    [1:/]    [4:100]
//   /    \
// [2:h] [3:mh]
```

---

## Roslyn: Syntax Tree vs Semantic Model

Roslyn has **two separate trees**:
1. **Syntax Tree** - What you literally wrote
2. **Semantic Model / Bound Tree** - What it means

### Same Syntax → Different Semantics

| Syntax | Could Mean |
|--------|-----------|
| `foo` (IdentifierNameSyntax) | Local variable, parameter, field, property, type, namespace |
| `Print(x)` (InvocationExpression) | Which overload? |
| `a + b` (BinaryExpression) | int addition, string concat, custom operator+ |
| `x = y` (Assignment) | Implicit conversion inserted? |
| `list.Where(...)` | Instance method or extension method? |
| `var x = ...` | Actual inferred type? |

### Implicit Conversions (Inserted by Semantic Binding)

```csharp
int x = 5;
double y = x;    // implicit conversion
```

```
SYNTAX:     AssignmentExpression
            └── Left: IdentifierName("y")
            └── Right: IdentifierName("x")

SEMANTIC:   BoundAssignment
            └── Left: BoundLocal("y", type: double)
            └── Right: BoundConversion          ← INSERTED!
                       └── Operand: BoundLocal("x", type: int)
```

### Extension Methods (Rewritten)

```csharp
myList.Where(x => x > 5);
```

```
SYNTAX:     Looks like instance method on myList

SEMANTIC:   BoundCall
            └── Method: Enumerable.Where<T>(...)  ← STATIC!
            └── Arguments:
                └── [0]: myList                    ← First arg!
                └── [1]: lambda
```

### Different Syntax → Same Semantics

```csharp
Foo f1 = new Foo();
Foo f2 = new();        // Target-typed new (C# 9)

int GetX() => 5;
int GetY() { return 5; }

int[] a = new int[] { 1, 2, 3 };
int[] b = [1, 2, 3];   // Collection expression (C# 12)
```

All pairs produce equivalent semantic trees.

### Lowering (High-Level → Simple)

```csharp
// foreach → while + enumerator
foreach (var x in items) { }
↓
var e = items.GetEnumerator();
while (e.MoveNext()) { var x = e.Current; }

// using → try/finally
using (var f = File.Open(...)) { }
↓
var f = File.Open(...);
try { } finally { f?.Dispose(); }

// async/await → state machine class
```

### The Full Pipeline

```
SOURCE CODE
    │
    ▼
SYNTAX TREE (what you wrote)
    │  - Preserves whitespace, comments
    │  - No type info, no resolution
    │
    │ Binding
    ▼
BOUND TREE (what it means)
    │  - Types resolved, overloads picked
    │  - Implicit conversions inserted
    │  - Extension methods rewritten
    │
    │ Lowering
    ▼
LOWERED TREE (simplified)
    │  - foreach → while
    │  - async → state machine
    │
    │ Code generation
    ▼
   IL
```

**Key insight:** Syntax is about structure, semantics is about meaning. Roslyn keeps both because:
- **Syntax** needed for refactoring/formatting (preserve user's style)
- **Semantics** needed for analysis/compilation (understand meaning)
