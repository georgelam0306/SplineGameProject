# V2 Editor Codegen

**Date:** 2025-01-30

---

## Overview

The V2 system generates separate representations for editor and runtime:

- **Editor:** Managed classes, `List<T>` for resizable arrays, proxy pattern for change tracking
- **Runtime:** Blittable structs, inlined arrays at exact baked size, `Span<T>` access

This document covers the **editor-side codegen**.

---

## 1. Schema Definition (What You Write)

```csharp
[Schema]
public partial struct PaintLayer
{
    [Property] public PaintLayerKind Kind;
    [Property] public bool IsVisible;
    [Property] public float Opacity;
    [Property] public Color32 FillColor;
    [Property] public bool FillUseGradient;

    [Property] [EditorResizable]
    public GradientStop[] GradientStops;
}

[Schema]
public partial struct PaintComponent
{
    [Property] [EditorResizable]
    public PaintLayer[] Layers;
}
```

Key annotations:
- `[Schema]` - Marks struct for code generation
- `[Property]` - Field participates in property system (undo, animation, bindings)
- `[EditorResizable]` - `List<T>` in editor, inlined at bake

---

## 2. Generated: Editor Storage Classes

```csharp
// GENERATED: PaintLayer.Editor.g.cs

public sealed class PaintLayerEditor
{
    // Backing storage (internal for proxy access)
    internal PaintLayerKind _kind;
    internal bool _isVisible;
    internal float _opacity;
    internal Color32 _fillColor;
    internal bool _fillUseGradient;
    internal List<GradientStopEditor> _gradientStops;

    // Property accessors (read-only public access)
    public PaintLayerKind Kind => _kind;
    public bool IsVisible => _isVisible;
    public float Opacity => _opacity;
    public Color32 FillColor => _fillColor;
    public bool FillUseGradient => _fillUseGradient;
    public IReadOnlyList<GradientStopEditor> GradientStops => _gradientStops;

    // Constructor
    public PaintLayerEditor()
    {
        _gradientStops = new List<GradientStopEditor>();
        SetDefaults();
    }

    private void SetDefaults()
    {
        _kind = PaintLayerKind.Fill;
        _isVisible = true;
        _opacity = 1f;
        _fillColor = new Color32(210, 210, 220, 255);
    }
}
```

```csharp
// GENERATED: PaintComponent.Editor.g.cs

public sealed class PaintComponentEditor
{
    internal List<PaintLayerEditor> _layers;

    public IReadOnlyList<PaintLayerEditor> Layers => _layers;

    public PaintComponentEditor()
    {
        _layers = new List<PaintLayerEditor>();
    }
}
```

**Key points:**
- Editor classes are `sealed class` (not struct) - allocations OK at edit time
- Backing fields are `internal` for proxy access
- Public accessors are read-only - writes go through proxies
- `[EditorResizable]` fields become `List<T>`

---

## 3. Generated: Property IDs

```csharp
// GENERATED: PaintLayer.PropertyIds.g.cs

public static class PaintLayerPropertyIds
{
    public const ulong Kind = 0x1A2B3C4D00000001;
    public const ulong IsVisible = 0x1A2B3C4D00000002;
    public const ulong Opacity = 0x1A2B3C4D00000003;
    public const ulong FillColor = 0x1A2B3C4D00000004;
    public const ulong FillUseGradient = 0x1A2B3C4D00000005;
    public const ulong GradientStops = 0x1A2B3C4D00000006;
}

public static class PaintComponentPropertyIds
{
    public const ulong Layers = 0x2B3C4D5E00000001;
}
```

**Key points:**
- Stable hashes derived from schema + property name
- Used for undo records, animation bindings, serialization
- No string comparisons at runtime

---

## 4. Generated: Edit Proxy

The proxy intercepts property writes and optionally emits commands.

```csharp
// GENERATED: PaintLayerEditor.Proxy.g.cs

public ref struct PaintLayerEditProxy
{
    private readonly PaintLayerEditor _target;
    private readonly ICommandEmitter _commandEmitter;  // null for Preview mode
    private readonly Entity _entity;
    private readonly int _layerIndex;

    internal PaintLayerEditProxy(
        PaintLayerEditor target,
        Entity entity,
        int layerIndex,
        ICommandEmitter commandEmitter)
    {
        _target = target;
        _entity = entity;
        _layerIndex = layerIndex;
        _commandEmitter = commandEmitter;
    }

    public PaintLayerKind Kind
    {
        get => _target._kind;
        set
        {
            if (_target._kind == value) return;
            var before = _target._kind;
            _target._kind = value;
            EmitIfNeeded(PaintLayerPropertyIds.Kind,
                PropertyValue.FromInt((int)before),
                PropertyValue.FromInt((int)value));
        }
    }

    public float Opacity
    {
        get => _target._opacity;
        set
        {
            if (_target._opacity == value) return;
            var before = _target._opacity;
            _target._opacity = value;
            EmitIfNeeded(PaintLayerPropertyIds.Opacity,
                PropertyValue.FromFloat(before),
                PropertyValue.FromFloat(value));
        }
    }

    public Color32 FillColor
    {
        get => _target._fillColor;
        set
        {
            if (_target._fillColor.Equals(value)) return;
            var before = _target._fillColor;
            _target._fillColor = value;
            EmitIfNeeded(PaintLayerPropertyIds.FillColor,
                PropertyValue.FromColor32(before),
                PropertyValue.FromColor32(value));
        }
    }

    // ... other properties follow same pattern ...

    private void EmitIfNeeded(ulong propertyId, PropertyValue before, PropertyValue after)
    {
        _entity.MarkDirty();

        if (_commandEmitter != null)
        {
            _commandEmitter.Emit(new PropertyChangedCommand
            {
                Entity = _entity,
                ComponentKind = ComponentKind.PaintLayer,
                ArrayIndex = _layerIndex,
                PropertyId = propertyId,
                Before = before,
                After = after
            });
        }
    }
}
```

**Key points:**
- `ref struct` - stack only, no heap allocation
- Wraps the editor class, intercepts writes
- `_commandEmitter` is null for `BeginPreview`, non-null for `BeginChange`
- Always marks entity dirty (for propagation)
- Only emits command if `_commandEmitter` is set

---

## 5. Generated: List Proxy for Resizable Arrays

```csharp
// GENERATED: PaintComponentEditor.Proxy.g.cs

public ref struct PaintComponentEditProxy
{
    private readonly PaintComponentEditor _target;
    private readonly ICommandEmitter _commandEmitter;
    private readonly Entity _entity;

    public PaintLayerListProxy Layers => new PaintLayerListProxy(
        _target._layers, _entity, _commandEmitter);
}

public ref struct PaintLayerListProxy
{
    private readonly List<PaintLayerEditor> _list;
    private readonly Entity _entity;
    private readonly ICommandEmitter _commandEmitter;

    public PaintLayerEditProxy this[int index] => new PaintLayerEditProxy(
        _list[index], _entity, index, _commandEmitter);

    public int Count => _list.Count;

    public void Insert(int index, PaintLayerEditor layer)
    {
        _list.Insert(index, layer);
        _entity.MarkDirty();

        _commandEmitter?.Emit(new ListInsertCommand
        {
            Entity = _entity,
            ComponentKind = ComponentKind.Paint,
            PropertyId = PaintComponentPropertyIds.Layers,
            Index = index,
            Snapshot = layer.Serialize()  // For undo
        });
    }

    public void RemoveAt(int index)
    {
        var removed = _list[index];
        _list.RemoveAt(index);
        _entity.MarkDirty();

        _commandEmitter?.Emit(new ListRemoveCommand
        {
            Entity = _entity,
            ComponentKind = ComponentKind.Paint,
            PropertyId = PaintComponentPropertyIds.Layers,
            Index = index,
            Snapshot = removed.Serialize()  // For undo
        });
    }

    public void Move(int fromIndex, int toIndex)
    {
        var item = _list[fromIndex];
        _list.RemoveAt(fromIndex);
        _list.Insert(toIndex, item);
        _entity.MarkDirty();

        _commandEmitter?.Emit(new ListMoveCommand
        {
            Entity = _entity,
            ComponentKind = ComponentKind.Paint,
            PropertyId = PaintComponentPropertyIds.Layers,
            FromIndex = fromIndex,
            ToIndex = toIndex
        });
    }
}
```

**Key points:**
- `this[int index]` returns element proxy (for `edit.Layers[2].Color = x`)
- `Insert`, `RemoveAt`, `Move` emit structural commands
- Structural commands include snapshots for undo

---

## 6. Generated: Entity Extension Methods

```csharp
// GENERATED: EntityExtensions.Paint.g.cs

public static class EntityPaintExtensions
{
    // Read-only access
    public static PaintComponentEditor GetPaint(this Entity entity)
    {
        return entity.GetEditorComponent<PaintComponentEditor>();
    }

    // Direct write (marks dirty, no commands)
    public static PaintComponentEditor RefPaint(this Entity entity)
    {
        entity.MarkDirty();
        return entity.GetEditorComponent<PaintComponentEditor>();
    }

    // Preview mode (marks dirty, no commands, for animation/scrubbing)
    public static PaintComponentEditProxy BeginPreviewPaint(this Entity entity)
    {
        return new PaintComponentEditProxy(
            entity.GetEditorComponent<PaintComponentEditor>(),
            commandEmitter: null,
            entity);
    }

    // Change mode (marks dirty, emits commands, for user editing)
    public static PaintComponentEditProxy BeginChangePaint(this Entity entity)
    {
        return new PaintComponentEditProxy(
            entity.GetEditorComponent<PaintComponentEditor>(),
            commandEmitter: CommandBus.Current,
            entity);
    }
}
```

---

## 7. Callsite Examples

### Reading (Inspector Display)

```csharp
void DrawPaintInspector(Entity entity)
{
    var paint = entity.GetPaint();  // Read-only

    for (int i = 0; i < paint.Layers.Count; i++)
    {
        var layer = paint.Layers[i];
        DrawLayerHeader(layer.Kind, layer.IsVisible);
        DrawColorPreview(layer.FillColor);
        DrawOpacitySlider(layer.Opacity);
    }
}
```

### User Editing (Inspector Interaction)

```csharp
void OnLayerColorChanged(Entity entity, int layerIndex, Color32 newColor)
{
    var edit = entity.BeginChangePaint();
    edit.Layers[layerIndex].FillColor = newColor;
    // PropertyChangedCommand automatically emitted
    // UndoProcessor and AutoKeyProcessor receive it
}

void OnOpacitySliderChanged(Entity entity, int layerIndex, float newOpacity)
{
    var edit = entity.BeginChangePaint();
    edit.Layers[layerIndex].Opacity = newOpacity;
}
```

### Adding/Removing Layers

```csharp
void OnAddFillLayer(Entity entity)
{
    var edit = entity.BeginChangePaint();
    edit.Layers.Insert(0, new PaintLayerEditor());
    // ListInsertCommand emitted for undo
}

void OnRemoveLayer(Entity entity, int layerIndex)
{
    var edit = entity.BeginChangePaint();
    edit.Layers.RemoveAt(layerIndex);
    // ListRemoveCommand emitted with snapshot for undo
}

void OnMoveLayer(Entity entity, int fromIndex, int toIndex)
{
    var edit = entity.BeginChangePaint();
    edit.Layers.Move(fromIndex, toIndex);
    // ListMoveCommand emitted
}
```

### Animation Playback (Preview Mode)

```csharp
void UpdateAnimation(float time)
{
    foreach (var track in _tracks)
    {
        var entity = _targets[track.TargetIndex];
        var preview = entity.BeginPreviewPaint();

        switch (track.PropertyId)
        {
            case PaintLayerPropertyIds.Opacity:
                preview.Layers[track.LayerIndex].Opacity = track.Sample(time);
                break;
            case PaintLayerPropertyIds.FillColor:
                preview.Layers[track.LayerIndex].FillColor = track.SampleColor(time);
                break;
        }
        // No commands emitted - preview mode
    }
}
```

### Gizmo Dragging (Preview, then Commit)

```csharp
void OnGradientHandleDrag(Entity entity, int layerIndex, int stopIndex, float newT)
{
    // During drag: preview mode (no undo spam)
    var preview = entity.BeginPreviewPaint();
    preview.Layers[layerIndex].GradientStops[stopIndex].T = newT;
}

void OnGradientHandleDragEnd(Entity entity, int layerIndex, int stopIndex)
{
    // On release: commit creates single undo record
    entity.CommitPreview();
}
```

---

## 8. No Size Limits

Because editor uses `List<T>`, there are no arbitrary limits:

```
CURRENT:
  MaxLayers = 32           // Hardcoded
  MaxGradientStops = 8     // Hardcoded

V2 EDITOR:
  List<PaintLayerEditor>        // No limit
  List<GradientStopEditor>      // No limit

V2 BAKE:
  Inlined at exact size used
```

---

## 9. Summary: Generated Files

| Generated File | Contents |
|----------------|----------|
| `PaintLayer.Editor.g.cs` | `PaintLayerEditor` class with backing fields |
| `PaintComponent.Editor.g.cs` | `PaintComponentEditor` class |
| `PaintLayer.PropertyIds.g.cs` | `const ulong` for each property |
| `PaintLayerEditor.Proxy.g.cs` | `PaintLayerEditProxy` ref struct |
| `PaintComponentEditor.Proxy.g.cs` | `PaintComponentEditProxy` + `PaintLayerListProxy` |
| `EntityExtensions.Paint.g.cs` | `GetPaint()`, `BeginChangePaint()`, `BeginPreviewPaint()` |

---

## Related Documents

- `2025-01-30_V2_Property_System_Spec.md` - Proxy modes, command system
- `2025-01-30_V2_UGC_System_Spec.md` - Runtime baking, Span<T> access
- `2025-01-30_V2_Derp_UI_Integration.md` - Full integration with Derp.UI
