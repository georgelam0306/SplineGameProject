# V2 Timeline & State Machine Integration

**Date:** 2025-01-30

---

## 1. Overview

V2 eliminates the dual representation pattern (AnimationDocument + AnimationLibraryComponent) by using schema-driven code generation. The generated editor classes **ARE** the editor state.

```
CURRENT (dual representation):
─────────────────────────────
AnimationDocument (hand-written editor state)
        ↓ manual sync ↓
AnimationLibraryComponent (runtime packed)


V2 (schema-driven):
───────────────────
[Schema]
struct AnimationTimeline { ... }
        ↓ codegen ↓
┌───────────────────────────────────────┐
│ TimelineEditor (List<T>, managed)     │  ← Editor state
│ TimelineRuntime (InlineArray, packed) │  ← Baked runtime
└───────────────────────────────────────┘
```

---

## 2. Animation Schema Definitions

```csharp
[Schema]
public partial struct AnimationKeyframe
{
    [Property] public int Frame;
    [Property] public PropertyValue Value;
    [Property] public Interpolation Interpolation;
    [Property] public float InTangent;
    [Property] public float OutTangent;
}

[Schema]
public partial struct AnimationBinding
{
    [Property] public uint TargetStableId;
    [Property] public ushort ComponentKind;
    [Property] public ushort PropertyIndexHint;
    [Property] public ulong PropertyId;
    [Property] public PropertyKind PropertyKind;
    [Property] public int ArrayIndex;
}

[Schema]
public partial struct AnimationTrack
{
    [Property] public AnimationBinding Binding;

    [Property] [EditorResizable]
    public AnimationKeyframe[] Keys;
}

[Schema]
public partial struct AnimationTarget
{
    [Property] public uint StableId;
}

[Schema]
public partial struct AnimationTimeline
{
    [Property] public uint Id;
    [Property] public StringHandle Name;
    [Property] public int DurationFrames;
    [Property] public PlaybackMode PlaybackMode;
    [Property] public float PlaybackSpeed;
    [Property] public int SnapFps;
    [Property] public int WorkStartFrame;
    [Property] public int WorkEndFrame;

    [Property] [EditorResizable]
    public AnimationTarget[] Targets;

    [Property] [EditorResizable]
    public AnimationTrack[] Tracks;
}

[Schema]
public partial struct AnimationFolder
{
    [Property] public uint Id;
    [Property] public StringHandle Name;

    [Property] [EditorResizable]
    public LibraryEntry[] Children;
}

[Schema]
public partial struct AnimationLibrary
{
    [Property] [EditorResizable]
    public AnimationFolder[] Folders;

    [Property] [EditorResizable]
    public AnimationTimeline[] Timelines;
}
```

---

## 3. Generated Editor Classes (Replaces AnimationDocument)

```csharp
// GENERATED: AnimationKeyframe.Editor.g.cs

public sealed class AnimationKeyframeEditor
{
    internal int _frame;
    internal PropertyValue _value;
    internal Interpolation _interpolation;
    internal float _inTangent;
    internal float _outTangent;

    public int Frame => _frame;
    public PropertyValue Value => _value;
    public Interpolation Interpolation => _interpolation;
    public float InTangent => _inTangent;
    public float OutTangent => _outTangent;
}
```

```csharp
// GENERATED: AnimationTrack.Editor.g.cs

public sealed class AnimationTrackEditor
{
    internal AnimationBinding _binding;
    internal List<AnimationKeyframeEditor> _keys;

    public AnimationBinding Binding => _binding;
    public IReadOnlyList<AnimationKeyframeEditor> Keys => _keys;

    public AnimationTrackEditor()
    {
        _keys = new List<AnimationKeyframeEditor>();
    }
}
```

```csharp
// GENERATED: AnimationTimeline.Editor.g.cs

public sealed class AnimationTimelineEditor
{
    internal uint _id;
    internal StringHandle _name;
    internal int _durationFrames;
    internal PlaybackMode _playbackMode;
    internal float _playbackSpeed;
    internal int _snapFps;
    internal int _workStartFrame;
    internal int _workEndFrame;
    internal List<AnimationTargetEditor> _targets;
    internal List<AnimationTrackEditor> _tracks;

    public uint Id => _id;
    public StringHandle Name => _name;
    public int DurationFrames => _durationFrames;
    public PlaybackMode PlaybackMode => _playbackMode;
    public float PlaybackSpeed => _playbackSpeed;
    public int SnapFps => _snapFps;
    public int WorkStartFrame => _workStartFrame;
    public int WorkEndFrame => _workEndFrame;
    public IReadOnlyList<AnimationTargetEditor> Targets => _targets;
    public IReadOnlyList<AnimationTrackEditor> Tracks => _tracks;

    public AnimationTimelineEditor()
    {
        _durationFrames = 60;
        _playbackMode = PlaybackMode.Loop;
        _playbackSpeed = 1f;
        _snapFps = 30;
        _targets = new List<AnimationTargetEditor>();
        _tracks = new List<AnimationTrackEditor>();
    }
}
```

```csharp
// GENERATED: AnimationLibrary.Editor.g.cs

public sealed class AnimationLibraryEditor
{
    internal List<AnimationFolderEditor> _folders;
    internal List<AnimationTimelineEditor> _timelines;

    public IReadOnlyList<AnimationFolderEditor> Folders => _folders;
    public IReadOnlyList<AnimationTimelineEditor> Timelines => _timelines;

    public AnimationLibraryEditor()
    {
        _folders = new List<AnimationFolderEditor>();
        _timelines = new List<AnimationTimelineEditor>();
    }
}
```

---

## 4. What This Eliminates

| Before | After |
|--------|-------|
| `AnimationDocument.cs` (hand-written) | Deleted - generated `AnimationLibraryEditor` |
| `AnimationTimeline` class | Generated `AnimationTimelineEditor` |
| `AnimationTrack` class | Generated `AnimationTrackEditor` |
| `AnimationKeyframe` class | Generated `AnimationKeyframeEditor` |
| Manual sync between Document ↔ Component | Gone - one source of truth |

---

## 5. State Machine Schema

```csharp
[Schema]
public partial struct StateMachineCondition
{
    [Property] public uint VariableId;
    [Property] public ConditionOp Op;
    [Property] public PropertyValue CompareValue;
}

[Schema]
public partial struct StateMachineTransition
{
    [Property] public uint Id;
    [Property] public uint LayerId;
    [Property] public TransitionFromKind FromKind;
    [Property] public uint FromStateId;
    [Property] public TransitionToKind ToKind;
    [Property] public uint ToStateId;
    [Property] public float DurationSeconds;
    [Property] public bool HasExitTime;
    [Property] public float ExitTime01;

    [Property] [EditorResizable]
    public StateMachineCondition[] Conditions;
}

[Schema]
public partial struct BlendChild
{
    [Property] public float Threshold;
    [Property] public uint TimelineId;
}

[Schema]
public partial struct StateMachineState
{
    [Property] public uint Id;
    [Property] public uint LayerId;
    [Property] public StringHandle Name;
    [Property] public StateKind Kind;
    [Property] public uint TimelineId;           // If Kind == Timeline
    [Property] public float PlaybackSpeed;
    [Property] public uint BlendParameterVariableId;  // If Kind == Blend1D
    [Property] public Vector2 EditorPosition;

    [Property] [EditorResizable]
    public BlendChild[] BlendChildren;           // If Kind == Blend1D
}

[Schema]
public partial struct StateMachineLayer
{
    [Property] public uint Id;
    [Property] public uint MachineId;
    [Property] public StringHandle Name;
    [Property] public uint EntryTargetStateId;
    [Property] public Vector2 EntryPos;
    [Property] public Vector2 AnyStatePos;
    [Property] public Vector2 ExitPos;
    [Property] public Vector2 Pan;
    [Property] public float Zoom;

    [Property] [EditorResizable]
    public StateMachineState[] States;

    [Property] [EditorResizable]
    public StateMachineTransition[] Transitions;
}

[Schema]
public partial struct StateMachine
{
    [Property] public uint Id;
    [Property] public StringHandle Name;

    [Property] [EditorResizable]
    public StateMachineLayer[] Layers;
}

[Schema]
public partial struct StateMachineDefinition
{
    [Property] [EditorResizable]
    public StateMachine[] Machines;
}
```

---

## 6. Entity Storage

```csharp
// Prefab entity has the editor components
var lib = entity.GetAnimationLibrary();           // Read-only
var edit = entity.BeginChangeAnimationLibrary();  // With commands

var smDef = entity.GetStateMachineDefinition();
var smEdit = entity.BeginChangeStateMachineDefinition();
```

---

## 7. PropertyDrawContext Integration

```csharp
public class PropertyDrawContext
{
    public Entity Entity;
    public int CurrentFrame;
    public bool IsReadOnly;

    // Track state cache
    private Dictionary<(ulong propertyId, int arrayIndex), TrackState> _trackStates;

    // Pending actions from drawers
    private List<PendingKeyframeAction> _pendingActions;

    public void Populate(Entity entity, AnimationEditorState editorState)
    {
        Entity = entity;
        _trackStates.Clear();
        _pendingActions.Clear();

        // Get animation library directly from entity (generated editor class)
        var lib = entity.GetAnimationLibrary();

        if (lib == null || editorState.SelectedTimelineIndex < 0)
        {
            CurrentFrame = 0;
            return;
        }

        var timeline = lib.Timelines[editorState.SelectedTimelineIndex];
        CurrentFrame = editorState.PlayheadFrame;

        // Build track states directly from editor class
        foreach (var track in timeline.Tracks)
        {
            if (track.Binding.TargetStableId != entity.StableId)
                continue;

            var key = (track.Binding.PropertyId, track.Binding.ArrayIndex);
            _trackStates[key] = new TrackState
            {
                HasTrack = true,
                HasKeyAtFrame = HasKeyAtFrame(track.Keys, CurrentFrame)
            };
        }
    }

    private static bool HasKeyAtFrame(IReadOnlyList<AnimationKeyframeEditor> keys, int frame)
    {
        for (int i = 0; i < keys.Count; i++)
        {
            if (keys[i].Frame == frame) return true;
        }
        return false;
    }

    public TrackState GetTrackState(ulong propertyId, int arrayIndex = 0)
    {
        return _trackStates.GetValueOrDefault((propertyId, arrayIndex));
    }

    public void RequestKeyframeToggle(ulong propertyId, int arrayIndex, PropertyValue value)
    {
        _pendingActions.Add(new PendingKeyframeAction
        {
            PropertyId = propertyId,
            ArrayIndex = arrayIndex,
            Value = value
        });
    }

    public ReadOnlySpan<PendingKeyframeAction> PendingActions =>
        CollectionsMarshal.AsSpan(_pendingActions);

    public void ClearPendingActions() => _pendingActions.Clear();
}

public struct TrackState
{
    public static readonly TrackState None = default;

    public bool HasTrack;
    public bool HasKeyAtFrame;
}

public struct PendingKeyframeAction
{
    public ulong PropertyId;
    public int ArrayIndex;
    public PropertyValue Value;
}
```

---

## 8. State Machine Runtime Integration

When a state machine is playing, PropertyDrawContext determines the current frame from state time:

```csharp
public void PopulateWithStateMachine(
    Entity entity,
    AnimationEditorState editorState,
    StateMachineRuntimeComponent smRuntime)
{
    Entity = entity;
    _trackStates.Clear();

    if (!smRuntime.IsInitialized)
    {
        Populate(entity, editorState);
        return;
    }

    var lib = entity.GetAnimationLibrary();
    var smDef = entity.GetStateMachineDefinition();

    if (lib == null || smDef == null) return;

    // Get current state's timeline
    int layerIndex = 0;  // First active layer
    var currentStateId = smRuntime.LayerCurrentStateId[layerIndex];

    // Find state in definition
    StateMachineStateEditor state = FindStateById(smDef, currentStateId);
    if (state == null || state.Kind != StateKind.Timeline) return;

    // Find timeline
    AnimationTimelineEditor timeline = FindTimelineById(lib, state.TimelineId);
    if (timeline == null) return;

    // Compute frame from state time
    var stateTimeUs = smRuntime.LayerStateTimeUs[layerIndex];
    CurrentFrame = ComputeTimelineFrame(timeline, stateTimeUs, state.PlaybackSpeed);

    // Build track states from this timeline
    foreach (var track in timeline.Tracks)
    {
        if (track.Binding.TargetStableId != entity.StableId)
            continue;

        var key = (track.Binding.PropertyId, track.Binding.ArrayIndex);
        _trackStates[key] = new TrackState
        {
            HasTrack = true,
            HasKeyAtFrame = HasKeyAtFrame(track.Keys, CurrentFrame)
        };
    }
}

private int ComputeTimelineFrame(
    AnimationTimelineEditor timeline,
    uint stateTimeUs,
    float stateSpeed)
{
    float fps = timeline.SnapFps > 0 ? timeline.SnapFps : 30f;
    float speed = timeline.PlaybackSpeed * stateSpeed;
    float stepsF = stateTimeUs * (fps * speed) / 1_000_000f;

    int workStart = timeline.WorkStartFrame;
    int workEnd = timeline.WorkEndFrame > 0 ? timeline.WorkEndFrame : timeline.DurationFrames;
    int range = workEnd - workStart;

    if (range <= 0) return workStart;

    return timeline.PlaybackMode switch
    {
        PlaybackMode.OneShot => Math.Clamp((int)stepsF + workStart, workStart, workEnd),
        PlaybackMode.Loop => workStart + ((int)stepsF % range),
        PlaybackMode.PingPong => ComputePingPong((int)stepsF, workStart, range),
        _ => workStart
    };
}
```

---

## 9. Frame Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           EDITOR FRAME                                       │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  1. STATE MACHINE TICK (if playing)                                          │
│     └── StateMachineRuntimeSystem.Tick(deltaUs)                             │
│         └── ApplyTimelineAtFrame() → BeginPreview → marks dirty             │
│                                                                              │
│  2. FLUSH (toposorted propagation)                                           │
│     └── Computed fields update                                               │
│     └── Bindings propagate                                                   │
│                                                                              │
│  3. PROPERTY INSPECTOR                                                       │
│     └── PropertyDrawContext.Populate(entity, editorState)                   │
│         ├── Gets AnimationLibraryEditor directly from entity                │
│         ├── Determines CurrentFrame (playhead or state time)                │
│         └── Builds TrackState cache                                          │
│                                                                              │
│     └── ComponentDrawers.Draw(entity, rect, ctx)                            │
│         ├── Drawer reads ctx.GetTrackState() → shows keyframe button        │
│         ├── Drawer signals ctx.RequestKeyframeToggle() on click             │
│         └── Value changes → BeginChange → emits commands                    │
│                                                                              │
│  4. PROCESS KEYFRAME ACTIONS                                                 │
│     └── foreach action in ctx.PendingActions:                               │
│         ├── edit.Timelines[i].Tracks[j].Keys.Insert/RemoveAt                │
│         └── Emits KeyframeCommand for undo                                   │
│                                                                              │
│  5. COMMAND PROCESSING                                                       │
│     └── UndoProcessor records                                                │
│     └── AutoKeyProcessor (if enabled, creates keyframes)                    │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 10. Playback: V2 Proxy Integration

State machine runtime uses BeginPreview to apply animated values:

```csharp
// StateMachineRuntimeSystem.cs (V2)

void ApplyTimelineAtFrame(Entity entity, AnimationTimelineEditor timeline, float frame)
{
    foreach (var track in timeline.Tracks)
    {
        // Resolve target entity
        var targetEntity = ResolveTarget(entity, track.Binding.TargetStableId);
        if (targetEntity == Entity.Null) continue;

        // Evaluate keyframes
        var value = EvaluateTrackAtFrame(track, frame);

        // Apply via BeginPreview (marks dirty, no commands)
        ApplyAnimatedValue(targetEntity, track.Binding, value);
    }
}

void ApplyAnimatedValue(Entity entity, AnimationBinding binding, PropertyValue value)
{
    // Generated dispatch based on ComponentKind
    switch ((ComponentKind)binding.ComponentKind)
    {
        case ComponentKind.Transform:
        {
            var edit = entity.BeginPreviewTransform();
            edit.SetProperty(binding.PropertyId, value);
            break;
        }

        case ComponentKind.Paint:
        {
            var edit = entity.BeginPreviewPaint();
            edit.Layers[binding.ArrayIndex].SetProperty(binding.PropertyId, value);
            break;
        }

        case ComponentKind.Fill:
        {
            var edit = entity.BeginPreviewFill();
            edit.SetProperty(binding.PropertyId, value);
            break;
        }

        // ... generated for each animatable component
    }
}

PropertyValue EvaluateTrackAtFrame(AnimationTrackEditor track, float frame)
{
    var keys = track.Keys;
    if (keys.Count == 0) return default;
    if (keys.Count == 1) return keys[0].Value;

    // Find surrounding keyframes
    int leftIndex = 0;
    for (int i = 0; i < keys.Count - 1; i++)
    {
        if (keys[i + 1].Frame > frame)
        {
            leftIndex = i;
            break;
        }
        leftIndex = i;
    }

    var left = keys[leftIndex];
    var right = keys[Math.Min(leftIndex + 1, keys.Count - 1)];

    if (left.Frame == right.Frame) return left.Value;

    float t = (frame - left.Frame) / (float)(right.Frame - left.Frame);

    return left.Interpolation switch
    {
        Interpolation.Step => left.Value,
        Interpolation.Linear => PropertyValue.Lerp(left.Value, right.Value, t),
        Interpolation.Cubic => PropertyValue.Hermite(
            left.Value, left.OutTangent,
            right.Value, right.InTangent, t),
        _ => left.Value
    };
}
```

---

## 11. Bake: Editor → Runtime

At export, generated code packs editor classes into runtime components:

```csharp
// GENERATED: AnimationLibrary.Bake.g.cs

public static class AnimationLibraryBaker
{
    public static AnimationLibraryComponent Bake(AnimationLibraryEditor editor)
    {
        var component = new AnimationLibraryComponent();

        int currentTargetIndex = 0;
        int currentTrackIndex = 0;
        int currentKeyIndex = 0;

        // Pack timelines
        for (int ti = 0; ti < editor.Timelines.Count; ti++)
        {
            var timeline = editor.Timelines[ti];

            component.TimelineId[ti] = timeline.Id;
            component.TimelineName[ti] = timeline.Name;
            component.TimelineDurationFrames[ti] = timeline.DurationFrames;
            component.TimelinePlaybackMode[ti] = timeline.PlaybackMode;
            component.TimelinePlaybackSpeed[ti] = timeline.PlaybackSpeed;
            component.TimelineSnapFps[ti] = timeline.SnapFps;
            component.TimelineWorkStartFrame[ti] = timeline.WorkStartFrame;
            component.TimelineWorkEndFrame[ti] = timeline.WorkEndFrame;

            // Targets
            component.TimelineTargetStart[ti] = (ushort)currentTargetIndex;
            component.TimelineTargetCount[ti] = (ushort)timeline.Targets.Count;

            foreach (var target in timeline.Targets)
            {
                component.TargetStableId[currentTargetIndex++] = target.StableId;
            }

            // Tracks
            component.TimelineTrackStart[ti] = (ushort)currentTrackIndex;
            component.TimelineTrackCount[ti] = (ushort)timeline.Tracks.Count;

            foreach (var track in timeline.Tracks)
            {
                component.TrackBinding[currentTrackIndex] = track.Binding;
                component.TrackKeyStart[currentTrackIndex] = (ushort)currentKeyIndex;
                component.TrackKeyCount[currentTrackIndex] = (ushort)track.Keys.Count;

                // Keyframes
                foreach (var key in track.Keys)
                {
                    component.KeyFrame[currentKeyIndex] = key.Frame;
                    component.KeyValue[currentKeyIndex] = key.Value;
                    component.KeyInterpolation[currentKeyIndex] = key.Interpolation;
                    component.KeyInTangent[currentKeyIndex] = key.InTangent;
                    component.KeyOutTangent[currentKeyIndex] = key.OutTangent;
                    currentKeyIndex++;
                }

                currentTrackIndex++;
            }
        }

        component.TimelineCount = (ushort)editor.Timelines.Count;
        component.TargetCount = (ushort)currentTargetIndex;
        component.TrackCount = (ushort)currentTrackIndex;
        component.KeyCount = (ushort)currentKeyIndex;

        return component;
    }
}
```

---

## 12. Keyframe Button States

```
○  Grey empty    - No track for this property
◇  Blue outline  - Has track, no keyframe at current frame
◆  Blue filled   - Keyframe exists at current frame
```

Click behavior:
- ○ or ◇ → Add keyframe at current frame
- ◆ → Remove keyframe at current frame

---

## 13. Summary

| Aspect | Before | V2 |
|--------|--------|-----|
| Editor state | AnimationDocument (hand-written) | AnimationLibraryEditor (generated) |
| Runtime state | AnimationLibraryComponent | Same, but baked from editor |
| Sync | Manual, error-prone | None needed - single source |
| Keyframe editing | Custom ops in AnimationLibraryOps | BeginChange proxy with commands |
| Playback | WriteValue() direct | BeginPreview proxy |
| Property drawer context | N/A | Reads directly from editor classes |

**Key V2 Benefits:**

1. **No AnimationDocument** - schema generates `AnimationLibraryEditor`
2. **No manual sync** - editor classes are the single source of truth
3. **Same proxy pattern** - `BeginChange` emits commands for undo
4. **PropertyDrawContext** reads track state directly from editor classes
5. **Bake at export** - generated code packs into runtime component

---

## Related Documents

- `2025-01-30_V2_Property_System_Spec.md` - Proxy modes, command system
- `2025-01-30_V2_Editor_Codegen.md` - Editor class generation
- `2025-01-30_V2_Property_Drawers.md` - Property drawer system
- `2025-01-30_V2_Derp_UI_Integration.md` - Full integration overview
