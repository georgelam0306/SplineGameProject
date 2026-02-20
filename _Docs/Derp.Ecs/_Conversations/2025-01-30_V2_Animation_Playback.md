# V2 Animation Playback Design

**Date:** 2025-01-30

---

## 1. The Core Problem: Where Does the Value Go?

Animation playback is fundamentally simple:

```
1. What frame are we on?  → frame 30
2. What value at that frame? → 50.0
3. Where does 50.0 go?  → ???
```

Step 3 is the problem. The animation system has a value and needs to write it somewhere in memory. We call that "somewhere" the **target**.

The challenge: targets can be **component properties** (known at compile time) or **UGC variables** (defined by the user at edit time).

---

## 2. Component Properties: Offset-Based Dispatch

Every property in a component lives at a fixed byte offset:

```
TransformComponent (32 bytes):
┌─────────────────────────────────────────────────────┐
│ Position.X │ Position.Y │ Rotation │ Scale │ ...   │
│ offset 0   │ offset 4   │ offset 8 │ off 12│       │
└─────────────────────────────────────────────────────┘

FillComponent (16 bytes):
┌─────────────────────────────────────────────────────┐
│ Color (4 bytes) │ Opacity │ ...                     │
│ offset 0        │ offset 4│                         │
└─────────────────────────────────────────────────────┘
```

If we know the offset, we can write directly without any switch:

```csharp
unsafe void WriteComponentProperty(
    Entity entity,
    ComponentKind component,
    int offset,
    PropertyKind kind,
    PropertyValue value)
{
    byte* componentPtr = GetComponentPointer(entity, component);

    switch (kind)
    {
        case PropertyKind.Float:
            *(float*)(componentPtr + offset) = value.Float;
            break;
        case PropertyKind.Int:
            *(int*)(componentPtr + offset) = value.Int;
            break;
        case PropertyKind.Vector2:
            *(Vector2*)(componentPtr + offset) = value.Vec2;
            break;
        // ... other value kinds
    }
}
```

The switch is on **value kind** (small, fixed set), not on property (unbounded).

---

## 3. UGC Variables: Dictionary-Based Dispatch

UGC variables are defined at edit time. The compiler never sees them:

```
User defines "PlayerStats":
├── Health (int)
├── MaxHealth (int)
├── Speed (float)
└── Equipment (nested struct)
    └── Weapon (nested struct)
        └── Damage (int)
```

### Editor Storage

```csharp
class UgcInstance
{
    public UgcSchema Schema;
    public Dictionary<ulong, UgcValue> Values;  // Key = interned field name
}

struct UgcValue
{
    public PropertyKind Kind;

    // If primitive:
    public PropertyValue PrimitiveValue;

    // If nested struct:
    public UgcInstance NestedInstance;

    // If array:
    public List<UgcValue> ArrayElements;
}
```

### Writing to UGC

```csharp
void WriteUgcVariable(Entity entity, ulong variableId, PropertyValue value)
{
    var ugc = GetUgcInstance(entity);
    ugc.Values[variableId] = new UgcValue
    {
        Kind = value.Kind,
        PrimitiveValue = value
    };
}
```

### Nested Field Paths

For deep nesting like `Equipment.Weapon.Damage`, we store a path and walk the tree:

```csharp
void WriteUgcNestedField(Entity entity, ulong variableId, ulong[] fieldPath, PropertyValue value)
{
    var ugc = GetUgcInstance(entity);
    var current = ugc.Values[variableId];

    // Walk to parent of target
    for (int i = 0; i < fieldPath.Length - 1; i++)
    {
        current = current.NestedInstance.Values[fieldPath[i]];
    }

    // Write to target
    current.NestedInstance.Values[fieldPath[^1]] = new UgcValue
    {
        Kind = value.Kind,
        PrimitiveValue = value
    };
}
```

This is slower than offset-based dispatch, but only happens once per track per frame.

---

## 4. The Two Worlds

```
COMPONENT PROPERTIES:              UGC VARIABLES:
─────────────────────              ──────────────
Known at compile time              Known at edit time
Fixed memory layout                Dynamic dictionary
Offset-based dispatch              Name-based lookup
Fast (pointer math)                Slower (hash + tree walk)
```

Animation needs to work with **both**.

---

## 5. Animation Binding Structure

```csharp
enum BindingTargetKind : byte
{
    ComponentProperty,
    UgcVariable,
}

struct AnimationBinding
{
    public uint TargetStableId;              // Which entity
    public BindingTargetKind TargetKind;
    public PropertyKind ValueKind;           // For interpolation

    // If ComponentProperty:
    public ushort ComponentKind;
    public int PropertyOffset;               // Byte offset within component
    public int ArrayIndex;                   // For Layers[2].Opacity

    // If UgcVariable:
    public ulong VariableId;                 // Root variable (interned name)
    public ulong[] FieldPath;                // For nested: [Equipment, Weapon, Damage]
}
```

---

## 6. Playback Dispatch

```csharp
void ApplyAnimatedValue(Entity entity, in AnimationBinding binding, PropertyValue value)
{
    entity.MarkDirty();

    switch (binding.TargetKind)
    {
        case BindingTargetKind.ComponentProperty:
            WriteComponentProperty(
                entity,
                binding.ComponentKind,
                binding.PropertyOffset,
                binding.ArrayIndex,
                binding.ValueKind,
                value);
            break;

        case BindingTargetKind.UgcVariable:
            WriteUgcVariable(
                entity,
                binding.VariableId,
                binding.FieldPath,
                value);
            break;
    }
}
```

**One branch per track**, then fast (offset) or flexible (dictionary) dispatch.

---

## 7. Pre-Sampled Keyframes

Instead of evaluating keyframes with complex interpolation every frame, we pre-sample at a fixed rate:

```
KEYFRAMES (authoring):              SAMPLES (playback):
──────────────────────              ───────────────────
Frame 0:  value 0.0                 Sample 0:  0.00
Frame 30: value 100.0               Sample 1:  3.33
(cubic ease-in-out)                 Sample 2:  6.89
                                    Sample 3:  10.67
                                    ...
                                    Sample 30: 100.00
```

### Sample Baking

```csharp
void RebakeSamples(AnimationTrackEditor track, int sampleRate)
{
    int duration = GetTrackDuration(track);
    int sampleCount = duration * sampleRate / 60 + 1;

    track.Samples = new PropertyValue[sampleCount];

    for (int i = 0; i < sampleCount; i++)
    {
        float frame = i * 60f / sampleRate;
        track.Samples[i] = EvaluateKeyframes(track.Keys, frame);
    }
}
```

### Sample Evaluation (Trivial)

```csharp
PropertyValue SampleTrack(PropertyValue[] samples, int sampleRate, float frame)
{
    float sampleIndex = frame * sampleRate / 60f;
    int left = (int)sampleIndex;
    int right = Math.Min(left + 1, samples.Length - 1);
    float t = sampleIndex - left;

    return PropertyValue.Lerp(samples[left], samples[right], t);
}
```

No cubic math, no keyframe search. Just array index + lerp.

### When to Rebake

- When keyframe added/removed/modified
- When interpolation mode changed
- Incremental: only rebake affected region (between modified keyframe and neighbors)

---

## 8. Compiled Track Structure

When playback starts (or timeline is selected), we "compile" tracks for fast evaluation:

```csharp
struct CompiledTrack
{
    // Pre-baked samples
    public PropertyValue[] Samples;
    public int SampleRate;

    // Resolved target
    public AnimationBinding Binding;
    public Entity ResolvedEntity;        // Cached entity lookup
}

CompiledTrack[] CompileTracks(AnimationTimelineEditor timeline, Entity rootEntity)
{
    var compiled = new CompiledTrack[timeline.Tracks.Count];

    for (int i = 0; i < timeline.Tracks.Count; i++)
    {
        var track = timeline.Tracks[i];

        compiled[i] = new CompiledTrack
        {
            Samples = track.Samples,
            SampleRate = timeline.SnapFps,
            Binding = track.Binding,
            ResolvedEntity = ResolveEntity(rootEntity, track.Binding.TargetStableId),
        };
    }

    return compiled;
}
```

---

## 9. Playback Loop

```csharp
void UpdatePlayback(CompiledTrack[] tracks, float currentFrame)
{
    foreach (ref var track in tracks.AsSpan())
    {
        // Sample (trivial lerp)
        var value = SampleTrack(track.Samples, track.SampleRate, currentFrame);

        // Dispatch (one branch, then offset or dictionary)
        ApplyAnimatedValue(track.ResolvedEntity, in track.Binding, value);
    }
}
```

---

## 10. Expression Bindings (Interpreted)

For bindings with expressions like `@health / @maxHealth * 100`:

```csharp
struct ExpressionBinding
{
    public AnimationBinding[] Sources;    // Input variables
    public AnimationBinding Target;       // Output property
    public ExpressionNode Expression;     // AST
}

struct ExpressionNode
{
    public ExpressionKind Kind;

    // If Literal:
    public PropertyValue LiteralValue;

    // If Variable:
    public int SourceIndex;               // Index into Sources array

    // If Binary (Add, Mul, Div, etc.):
    public int LeftIndex;                 // Index into node array
    public int RightIndex;

    // If Function (lerp, clamp, etc.):
    public BuiltinFunction Function;
    public int[] ArgIndices;
}
```

### Interpreted Evaluation

```csharp
PropertyValue EvaluateExpression(
    ExpressionNode[] nodes,
    int rootIndex,
    PropertyValue[] sourceValues)
{
    ref var node = ref nodes[rootIndex];

    return node.Kind switch
    {
        ExpressionKind.Literal => node.LiteralValue,

        ExpressionKind.Variable => sourceValues[node.SourceIndex],

        ExpressionKind.Add => PropertyValue.Add(
            EvaluateExpression(nodes, node.LeftIndex, sourceValues),
            EvaluateExpression(nodes, node.RightIndex, sourceValues)),

        ExpressionKind.Mul => PropertyValue.Mul(
            EvaluateExpression(nodes, node.LeftIndex, sourceValues),
            EvaluateExpression(nodes, node.RightIndex, sourceValues)),

        ExpressionKind.Div => PropertyValue.Div(
            EvaluateExpression(nodes, node.LeftIndex, sourceValues),
            EvaluateExpression(nodes, node.RightIndex, sourceValues)),

        ExpressionKind.Lerp => PropertyValue.Lerp(
            EvaluateExpression(nodes, node.ArgIndices[0], sourceValues),
            EvaluateExpression(nodes, node.ArgIndices[1], sourceValues),
            EvaluateExpression(nodes, node.ArgIndices[2], sourceValues).Float),

        // ... other operations
    };
}
```

### Binding Evaluation in Flush

```csharp
void EvaluateBindings(ExpressionBinding[] bindings)
{
    // Bindings are pre-sorted in dependency order (toposort)
    foreach (ref var binding in bindings.AsSpan())
    {
        // Read source values
        Span<PropertyValue> sourceValues = stackalloc PropertyValue[binding.Sources.Length];
        for (int i = 0; i < binding.Sources.Length; i++)
        {
            sourceValues[i] = ReadProperty(binding.Sources[i]);
        }

        // Evaluate expression
        var result = EvaluateExpression(
            binding.Expression.Nodes,
            binding.Expression.RootIndex,
            sourceValues);

        // Write to target
        ApplyAnimatedValue(binding.Target.Entity, in binding.Target, result);
    }
}
```

---

## 11. Frame Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           EDITOR FRAME                                   │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  1. ADVANCE TIME                                                         │
│     currentFrame += deltaTime * playbackSpeed                            │
│                                                                          │
│  2. ANIMATION SAMPLING                                                   │
│     foreach track in compiledTracks:                                     │
│         value = SampleTrack(track.Samples, currentFrame)                │
│         ApplyAnimatedValue(track.Entity, track.Binding, value)          │
│         → Marks entity dirty                                             │
│                                                                          │
│  3. FLUSH (toposorted)                                                   │
│     foreach binding in expressionBindings:                               │
│         sourceValues = ReadSources(binding.Sources)                      │
│         result = EvaluateExpression(binding.Expression, sourceValues)   │
│         ApplyAnimatedValue(binding.Target.Entity, binding.Target, result)│
│         → Marks entity dirty                                             │
│                                                                          │
│  4. CLEAR DIRTY FLAGS                                                    │
│                                                                          │
│  5. RENDER                                                               │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 12. Editor vs Runtime

| Aspect | Editor | Runtime (Baked) |
|--------|--------|-----------------|
| **Component dispatch** | Offset-based | Offset-based |
| **UGC dispatch** | Dictionary + tree walk | Offset-based (UGC → components) |
| **Keyframe storage** | List<Keyframe> | Inlined array |
| **Sample storage** | PropertyValue[] | Inlined array |
| **Expression eval** | Interpreted (AST walk) | Compiled C# methods |
| **Binding order** | Computed toposort | Pre-baked array |

### Bake Process

At export time:

1. **UGC schemas → generated components**
   - All UgcVariable bindings become ComponentProperty bindings
   - Dictionary dispatch → offset dispatch

2. **Expressions → compiled methods**
   ```csharp
   // GENERATED
   public static class Bindings_PlayerHUD
   {
       public static void Evaluate(ref PlayerStats stats, ref HUDComponent hud)
       {
           hud.HealthBar.Fill = stats.Health / (float)stats.MaxHealth;
       }
   }
   ```

3. **Samples → inlined arrays**
   - Exact size known, no allocations at runtime

---

## 13. Performance Characteristics

### Editor (Acceptable Overhead)

| Operation | Cost |
|-----------|------|
| Sample lookup | O(1) - array index |
| Sample lerp | O(1) - one lerp |
| Component dispatch | O(1) - offset write |
| UGC dispatch | O(d) - dictionary lookup + d-level tree walk |
| Expression eval | O(n) - n nodes in AST |
| Binding flush | O(b) - b bindings |

For typical animations (< 100 tracks, < 50 bindings), this is sub-millisecond.

### Runtime (Optimal)

| Operation | Cost |
|-----------|------|
| Sample lookup | O(1) |
| Sample lerp | O(1) |
| All dispatch | O(1) - everything is offset-based |
| Expression eval | O(1) - inlined compiled code |
| Binding flush | O(1) - direct method call |

---

## 14. Key Decisions

1. **Pre-sampled keyframes** - Trivial lerp at playback, complex interpolation at bake
2. **Two-path dispatch** - Component (offset) and UGC (dictionary), one branch per track
3. **Interpreted expressions in editor** - AST walk, slower but no hot-reload complexity
4. **Compiled expressions at runtime** - Generated C# methods, maximum performance
5. **Same playback loop** - Editor and runtime use same structure, just different dispatch paths

---

## 15. Sample Rate: Use Timeline's SnapFps

Each timeline has a `SnapFps` setting (e.g., 30, 60, 120). Samples are baked at this rate.

```csharp
void RebakeSamples(AnimationTrackEditor track, AnimationTimelineEditor timeline)
{
    int sampleRate = timeline.SnapFps;  // Use timeline's configured rate
    int durationFrames = timeline.DurationFrames;
    int sampleCount = durationFrames + 1;  // One sample per frame at SnapFps

    track.Samples = new PropertyValue[sampleCount];

    for (int i = 0; i < sampleCount; i++)
    {
        float frame = i;  // Each sample = one frame at SnapFps
        track.Samples[i] = EvaluateKeyframes(track.Keys, frame);
    }

    track.SampleRate = sampleRate;
}
```

**Why per-timeline:**
- UI animations may want 60fps for smoothness
- Gameplay animations may be fine at 30fps
- Cinematic timelines might use 24fps to match film

---

## 16. Incremental Rebake

When a keyframe is modified, only rebake the affected region:

```
KEYFRAMES:
    K0          K1          K2          K3
    ●───────────●───────────●───────────●
    frame 0     frame 20    frame 50    frame 80

USER EDITS K1:
    Only rebake samples between K0 and K2 (frames 0-50)
    Samples for frames 50-80 are unchanged
```

### Implementation

```csharp
struct RebakeRegion
{
    public int StartFrame;
    public int EndFrame;
}

RebakeRegion ComputeRebakeRegion(AnimationTrackEditor track, int modifiedKeyIndex)
{
    var keys = track.Keys;

    // Find neighboring keyframes
    int prevKeyFrame = modifiedKeyIndex > 0
        ? keys[modifiedKeyIndex - 1].Frame
        : 0;

    int nextKeyFrame = modifiedKeyIndex < keys.Count - 1
        ? keys[modifiedKeyIndex + 1].Frame
        : track.DurationFrames;

    return new RebakeRegion
    {
        StartFrame = prevKeyFrame,
        EndFrame = nextKeyFrame
    };
}

void RebakeSamplesIncremental(
    AnimationTrackEditor track,
    AnimationTimelineEditor timeline,
    RebakeRegion region)
{
    int sampleRate = timeline.SnapFps;

    for (int frame = region.StartFrame; frame <= region.EndFrame; frame++)
    {
        int sampleIndex = frame;  // 1:1 mapping when sampleRate == SnapFps
        track.Samples[sampleIndex] = EvaluateKeyframes(track.Keys, frame);
    }
}
```

### When Full Rebake is Required

- Keyframe added or removed (changes which keyframes are neighbors)
- Timeline duration changed
- SnapFps changed
- Interpolation mode changed on a keyframe

### When Incremental is Sufficient

- Keyframe value changed (same frame, same neighbors)
- Keyframe tangent changed (cubic handles)

---

## 17. UGC Path Interning

Nested UGC field paths like `Equipment.Weapon.Damage` are interned to avoid repeated parsing and allocation.

### Path Handle

```csharp
readonly struct UgcFieldPath
{
    // Interned path segments (each segment is a StringHandle)
    private readonly ulong[] _segments;

    // Cached hash for fast lookup
    private readonly int _hash;

    public int Depth => _segments.Length;
    public ulong this[int index] => _segments[index];

    public static UgcFieldPath Intern(string dotPath)
    {
        // Check cache first
        if (PathCache.TryGet(dotPath, out var cached))
            return cached;

        // Parse and intern
        var parts = dotPath.Split('.');
        var segments = new ulong[parts.Length];

        for (int i = 0; i < parts.Length; i++)
        {
            segments[i] = StringHandle.Intern(parts[i]);
        }

        var path = new UgcFieldPath(segments);
        PathCache.Add(dotPath, path);
        return path;
    }
}
```

### Path Cache

```csharp
static class UgcPathCache
{
    // String path → interned UgcFieldPath
    private static readonly Dictionary<string, UgcFieldPath> _cache = new();

    // Reverse lookup for debugging
    private static readonly Dictionary<int, string> _pathStrings = new();

    public static bool TryGet(string path, out UgcFieldPath result)
        => _cache.TryGetValue(path, out result);

    public static void Add(string path, UgcFieldPath interned)
    {
        _cache[path] = interned;
        _pathStrings[interned.GetHashCode()] = path;
    }

    public static string GetDebugString(UgcFieldPath path)
        => _pathStrings.GetValueOrDefault(path.GetHashCode(), "<unknown>");
}
```

### Updated Animation Binding

```csharp
struct AnimationBinding
{
    public uint TargetStableId;
    public BindingTargetKind TargetKind;
    public PropertyKind ValueKind;

    // If ComponentProperty:
    public ushort ComponentKind;
    public int PropertyOffset;
    public int ArrayIndex;

    // If UgcVariable:
    public ulong VariableId;              // Root variable (StringHandle)
    public UgcFieldPath FieldPath;        // Interned path (zero alloc on access)
}
```

### Updated UGC Write

```csharp
void WriteUgcNestedField(Entity entity, ulong variableId, UgcFieldPath path, PropertyValue value)
{
    var ugc = GetUgcInstance(entity);
    var current = ugc.Values[variableId];

    // Walk using interned segments (no string allocation)
    for (int i = 0; i < path.Depth - 1; i++)
    {
        current = current.NestedInstance.Values[path[i]];
    }

    // Write to final segment
    current.NestedInstance.Values[path[path.Depth - 1]] = new UgcValue
    {
        Kind = value.Kind,
        PrimitiveValue = value
    };
}
```

**Benefits:**
- Parse path string once (when binding is created)
- Zero allocation on every frame
- Fast dictionary lookup (ulong keys)

---

## 18. Expression Caching

For expressions with repeated subexpressions, cache intermediate results.

### The Problem

```
Expression: (@health / @maxHealth) * 100 + (@health / @maxHealth) * 50

Subexpression (@health / @maxHealth) appears twice.
Without caching: evaluated twice per frame.
```

### Solution: Flatten to SSA Form

Convert the AST to Static Single Assignment form where each subexpression is evaluated once:

```
ORIGINAL AST:
        [+]
       /   \
    [*]     [*]
   /   \   /   \
 [/]  100 [/]  50
 / \      / \
@h @mh   @h @mh

SSA FORM (flattened):
  t0 = @health
  t1 = @maxHealth
  t2 = t0 / t1        ← subexpression computed once
  t3 = t2 * 100
  t4 = t2 * 50        ← reuses t2
  t5 = t3 + t4
  result = t5
```

### Implementation

```csharp
struct FlatExpression
{
    public FlatOp[] Ops;       // Operations in evaluation order
    public int ResultIndex;    // Which temp holds final result
}

struct FlatOp
{
    public FlatOpKind Kind;

    // Source operands (indices into temps array, or special values for inputs)
    public int Left;
    public int Right;

    // For literals
    public PropertyValue LiteralValue;

    // For variable references
    public int SourceIndex;
}

enum FlatOpKind : byte
{
    LoadLiteral,      // temps[dest] = literal
    LoadSource,       // temps[dest] = sources[sourceIndex]
    Add,              // temps[dest] = temps[left] + temps[right]
    Sub,
    Mul,
    Div,
    Lerp,             // temps[dest] = lerp(temps[a], temps[b], temps[c])
    Clamp,
    // ...
}
```

### Flattening with CSE (Common Subexpression Elimination)

```csharp
FlatExpression Flatten(ExpressionNode[] ast, int rootIndex)
{
    var ops = new List<FlatOp>();
    var nodeToTemp = new Dictionary<int, int>();  // AST node index → temp index

    int Flatten(int nodeIndex)
    {
        // Already computed? Reuse.
        if (nodeToTemp.TryGetValue(nodeIndex, out int existing))
            return existing;

        ref var node = ref ast[nodeIndex];
        int tempIndex = ops.Count;

        switch (node.Kind)
        {
            case ExpressionKind.Literal:
                ops.Add(new FlatOp
                {
                    Kind = FlatOpKind.LoadLiteral,
                    LiteralValue = node.LiteralValue
                });
                break;

            case ExpressionKind.Variable:
                ops.Add(new FlatOp
                {
                    Kind = FlatOpKind.LoadSource,
                    SourceIndex = node.SourceIndex
                });
                break;

            case ExpressionKind.Add:
                int left = Flatten(node.LeftIndex);
                int right = Flatten(node.RightIndex);
                ops.Add(new FlatOp
                {
                    Kind = FlatOpKind.Add,
                    Left = left,
                    Right = right
                });
                break;

            // ... other cases
        }

        nodeToTemp[nodeIndex] = tempIndex;
        return tempIndex;
    }

    int resultIndex = Flatten(rootIndex);

    return new FlatExpression
    {
        Ops = ops.ToArray(),
        ResultIndex = resultIndex
    };
}
```

### Evaluation (Linear, No Recursion)

```csharp
PropertyValue EvaluateFlat(
    FlatExpression expr,
    ReadOnlySpan<PropertyValue> sources)
{
    Span<PropertyValue> temps = stackalloc PropertyValue[expr.Ops.Length];

    for (int i = 0; i < expr.Ops.Length; i++)
    {
        ref var op = ref expr.Ops[i];

        temps[i] = op.Kind switch
        {
            FlatOpKind.LoadLiteral => op.LiteralValue,
            FlatOpKind.LoadSource => sources[op.SourceIndex],
            FlatOpKind.Add => PropertyValue.Add(temps[op.Left], temps[op.Right]),
            FlatOpKind.Sub => PropertyValue.Sub(temps[op.Left], temps[op.Right]),
            FlatOpKind.Mul => PropertyValue.Mul(temps[op.Left], temps[op.Right]),
            FlatOpKind.Div => PropertyValue.Div(temps[op.Left], temps[op.Right]),
            // ...
        };
    }

    return temps[expr.ResultIndex];
}
```

**Benefits:**
- Each subexpression evaluated exactly once
- No recursion (better for CPU branch prediction)
- Stack-allocated temps (no heap allocation)
- Trivial to compile to native code at bake time

### When to Flatten

- When expression binding is created/modified
- Cached on the `ExpressionBinding` struct
- Original AST kept for editor display/editing

```csharp
struct ExpressionBinding
{
    public AnimationBinding[] Sources;
    public AnimationBinding Target;

    // Original for editing
    public ExpressionNode[] AstNodes;
    public int AstRootIndex;

    // Flattened for evaluation (computed once)
    public FlatExpression Flat;
}
```

---

## 19. Decisions Summary

| Question | Decision |
|----------|----------|
| Sample rate | Use timeline's `SnapFps` |
| Incremental rebake | Yes - only affected region between neighboring keyframes |
| Expression caching | Yes - flatten to SSA form with CSE |
| UGC path interning | Yes - `UgcFieldPath` struct with interned segments |

---

## Related Documents

- `2025-01-30_V2_Property_System_Spec.md` - Proxy modes, command system
- `2025-01-30_V2_UGC_System_Spec.md` - UGC schema and baking
- `2025-01-30_V2_Timeline_StateMachine_Integration.md` - State machine integration
