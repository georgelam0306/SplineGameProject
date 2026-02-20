# V2 Property Drawer System

**Date:** 2025-01-30

---

## Overview

The V2 property drawer system replaces the current 5400-line `PropertyInspector` with:
- Generated per-component drawer code
- Type-safe `IPropertyDrawer<T>` interface
- Declarative `[Drawer<T>]` attributes
- Automatic command emission, key icons, and binding menus

---

## 1. Current System Problems

```csharp
// PropertyInspector.cs - 5400 lines

// Giant switch on property kind (200+ lines)
private void DrawSingleProperty(PropertyUiItem item, ...)
{
    switch (item.Kind)
    {
        case PropertyUiKind.Float:
            DrawFloatProperty(item, ...);  // 40 lines
            break;
        case PropertyUiKind.Int:
            DrawIntProperty(item, ...);    // 40 lines
            break;
        case PropertyUiKind.Vec4:
            // Manual custom drawer check
            if (item.Info.DrawerType == typeof(InsetsDrawer))
                DrawInsetsProperty(item, ...);
            else if (item.Info.DrawerType == typeof(CornerRadiusDrawer))
                DrawCornerRadiusProperty(item, ...);
            else
                DrawVectorGroup(item, ...);
            break;
        // ... 20+ more cases
    }
}

// Each draw method handles undo/keying manually (duplicated everywhere)
private void DrawFloatProperty(PropertyUiItem item, ...)
{
    var slot = GetSlot(item);
    TryDrawPropertyBindingContextMenu(slot, inputRect);  // Manual

    float value = PropertyDispatcher.ReadFloat(_propertyWorld, slot);

    if (ImScalarInput.DrawAt(...))
    {
        _commands.SetProperty(slot, PropertyValue.FromFloat(value));  // Manual
    }

    if (_commands.TryGetKeyableState(slot, out bool hasTrack, out bool hasKey))
    {
        DrawKeyIcon(keyRect, hasTrack, hasKey);  // Manual
    }
}

// Multi-select duplicates all logic (400+ lines)
private void DrawSinglePropertyMulti(PropertyUiItem item, ...) { ... }
```

**Problems:**
- 5400 lines, hard to maintain
- Manual command/undo integration in every drawer
- Multi-select duplicates single-select logic
- Adding new property type = modify giant switch
- Custom drawers require manual if/else chains

---

## 2. V2 Schema Declaration

```csharp
[Schema]
public partial struct RectGeometryComponent
{
    [Property(Name = "Width", Group = "Rectangle Path")]
    public float Width;

    [Property(Name = "Height", Group = "Rectangle Path")]
    public float Height;

    [Property(Name = "Corner Radius", Group = "Rectangle Path")]
    [Drawer<CornerRadiusDrawer>]  // Custom drawer
    public Vector4 CornerRadius;
}

[Schema]
public partial struct TransformComponent
{
    [Property(Name = "Position", Group = "Transform")]
    public Vector2 Position;

    [Property(Name = "Padding", Group = "Layout Container")]
    [Drawer<InsetsDrawer>]  // Custom drawer
    public Vector4 LayoutContainerPadding;

    [Property(Name = "Opacity", Group = "Appearance")]
    [Drawer<SliderDrawer>]
    [Range(0f, 1f)]
    public float Opacity;
}
```

---

## 3. Drawer Interface

```csharp
// Simple drawer (most common)
public interface IPropertyDrawer<T>
{
    bool Draw(ImRect rect, ref T value);
}

// Drawer that needs context (for popovers, workspace access, etc.)
public interface IPropertyDrawerWithContext<T>
{
    bool Draw(DrawerContext ctx, ImRect rect, ref T value);
}

// Drawer that needs attribute metadata (Range, Step, etc.)
public interface IPropertyDrawerWithMetadata<T>
{
    bool Draw(ImRect rect, ref T value, PropertyMetadata meta);
}
```

---

## 4. Implementing a Custom Drawer

### Example: Corner Radius Drawer

```csharp
// Workspace/Drawers/CornerRadiusDrawer.cs

public struct CornerRadiusDrawer : IPropertyDrawer<Vector4>
{
    public bool Draw(ImRect rect, ref Vector4 value)
    {
        bool changed = false;
        float fieldWidth = (rect.Width - 12f) / 4f;  // 4 fields + spacing
        float x = rect.X;
        float y = rect.Y;
        float h = rect.Height;

        // TL
        if (Im.FloatInput("##tl", x, y, fieldWidth, h, ref value.X, min: 0f))
            changed = true;
        x += fieldWidth + 4f;

        // TR
        if (Im.FloatInput("##tr", x, y, fieldWidth, h, ref value.Y, min: 0f))
            changed = true;
        x += fieldWidth + 4f;

        // BR
        if (Im.FloatInput("##br", x, y, fieldWidth, h, ref value.Z, min: 0f))
            changed = true;
        x += fieldWidth + 4f;

        // BL
        if (Im.FloatInput("##bl", x, y, fieldWidth, h, ref value.W, min: 0f))
            changed = true;

        return changed;
    }
}
```

### Example: Insets Drawer (Padding/Margin)

```csharp
public struct InsetsDrawer : IPropertyDrawer<Vector4>
{
    public bool Draw(ImRect rect, ref Vector4 value)
    {
        bool changed = false;
        float fieldWidth = (rect.Width - 12f) / 4f;
        float x = rect.X;

        // L, T, R, B layout
        Im.Label("L", x, rect.Y); x += 12f;
        if (Im.FloatInput("##l", x, rect.Y, fieldWidth - 12f, rect.Height, ref value.X))
            changed = true;
        x += fieldWidth;

        Im.Label("T", x, rect.Y); x += 12f;
        if (Im.FloatInput("##t", x, rect.Y, fieldWidth - 12f, rect.Height, ref value.Y))
            changed = true;
        x += fieldWidth;

        Im.Label("R", x, rect.Y); x += 12f;
        if (Im.FloatInput("##r", x, rect.Y, fieldWidth - 12f, rect.Height, ref value.Z))
            changed = true;
        x += fieldWidth;

        Im.Label("B", x, rect.Y); x += 12f;
        if (Im.FloatInput("##b", x, rect.Y, fieldWidth - 12f, rect.Height, ref value.W))
            changed = true;

        return changed;
    }
}
```

---

## 5. What Gets Generated

### Per-Component Drawer

```csharp
// GENERATED: RectGeometryComponent.Drawer.g.cs

public static class RectGeometryComponentDrawer
{
    public static void Draw(DrawerContext ctx, Entity entity)
    {
        var editor = entity.GetRectGeometry();

        if (ImLayout.BeginPropertyGroup("Rectangle Path"))
        {
            DrawProperty_Width(ctx, entity, editor);
            DrawProperty_Height(ctx, entity, editor);
            DrawProperty_CornerRadius(ctx, entity, editor);
            ImLayout.EndPropertyGroup();
        }
    }

    private static void DrawProperty_Width(
        DrawerContext ctx, Entity entity, RectGeometryComponentEditor editor)
    {
        var rect = ImLayout.AllocatePropertyRow();
        var (labelRect, inputRect, keyRect, bindRect) = rect.SplitPropertyRow(ctx.LabelWidth);

        Im.Label("Width", labelRect);

        // Default FloatDrawer (no [Drawer] attribute)
        var value = editor._width;
        if (FloatDrawer.Draw(inputRect, ref value))
        {
            var before = editor._width;
            editor._width = value;

            ctx.CommandEmitter?.Emit(new PropertyChangedCommand
            {
                Entity = entity,
                ComponentKind = ComponentKind.RectGeometry,
                PropertyId = RectGeometryPropertyIds.Width,
                Before = PropertyValue.FromFloat(before),
                After = PropertyValue.FromFloat(value)
            });
        }

        DrawKeyIcon(keyRect, entity, RectGeometryPropertyIds.Width);
        DrawBindingContextMenu(bindRect, entity, RectGeometryPropertyIds.Width);
    }

    private static void DrawProperty_CornerRadius(
        DrawerContext ctx, Entity entity, RectGeometryComponentEditor editor)
    {
        var rect = ImLayout.AllocatePropertyRow();
        var (labelRect, inputRect, keyRect, bindRect) = rect.SplitPropertyRow(ctx.LabelWidth);

        Im.Label("Corner Radius", labelRect);

        // Custom drawer: [Drawer<CornerRadiusDrawer>]
        var value = editor._cornerRadius;
        if (CornerRadiusDrawer.Draw(inputRect, ref value))  // <-- Your drawer
        {
            var before = editor._cornerRadius;
            editor._cornerRadius = value;

            ctx.CommandEmitter?.Emit(new PropertyChangedCommand
            {
                Entity = entity,
                ComponentKind = ComponentKind.RectGeometry,
                PropertyId = RectGeometryPropertyIds.CornerRadius,
                Before = PropertyValue.FromVec4(before),
                After = PropertyValue.FromVec4(value)
            });
        }

        DrawKeyIcon(keyRect, entity, RectGeometryPropertyIds.CornerRadius);
        DrawBindingContextMenu(bindRect, entity, RectGeometryPropertyIds.CornerRadius);
    }
}
```

### Schema Registry

```csharp
// GENERATED: SchemaDrawers.g.cs

public static class SchemaDrawers
{
    public static void DrawComponent(DrawerContext ctx, Entity entity, ComponentKind kind)
    {
        switch (kind)
        {
            case ComponentKind.Transform:
                TransformComponentDrawer.Draw(ctx, entity);
                break;
            case ComponentKind.RectGeometry:
                RectGeometryComponentDrawer.Draw(ctx, entity);
                break;
            case ComponentKind.Paint:
                PaintComponentDrawer.Draw(ctx, entity);
                break;
            // ... generated for each [Schema] component
        }
    }
}
```

### Multi-Select (Also Generated)

```csharp
// GENERATED: RectGeometryComponent.Drawer.g.cs

public static class RectGeometryComponentDrawer
{
    // Single entity
    public static void Draw(DrawerContext ctx, Entity entity) { ... }

    // Multi-select (generated from same schema)
    public static void DrawMulti(DrawerContext ctx, ReadOnlySpan<Entity> entities)
    {
        Span<RectGeometryComponentEditor> editors = stackalloc RectGeometryComponentEditor[entities.Length];
        for (int i = 0; i < entities.Length; i++)
            editors[i] = entities[i].GetRectGeometry();

        if (ImLayout.BeginPropertyGroup("Rectangle Path"))
        {
            DrawPropertyMulti_Width(ctx, entities, editors);
            DrawPropertyMulti_CornerRadius(ctx, entities, editors);
            ImLayout.EndPropertyGroup();
        }
    }

    private static void DrawPropertyMulti_CornerRadius(
        DrawerContext ctx,
        ReadOnlySpan<Entity> entities,
        ReadOnlySpan<RectGeometryComponentEditor> editors)
    {
        var rect = ImLayout.AllocatePropertyRow();
        var (labelRect, inputRect, _, _) = rect.SplitPropertyRow(ctx.LabelWidth);

        Im.Label("Corner Radius", labelRect);

        // Check if values differ
        var first = editors[0]._cornerRadius;
        bool mixed = false;
        for (int i = 1; i < editors.Length; i++)
        {
            if (editors[i]._cornerRadius != first)
            {
                mixed = true;
                break;
            }
        }

        if (mixed)
        {
            if (CornerRadiusDrawer.DrawMixed(inputRect, out Vector4 newValue))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var before = editors[i]._cornerRadius;
                    editors[i]._cornerRadius = newValue;
                    ctx.CommandEmitter?.Emit(...);
                }
            }
        }
        else
        {
            var value = first;
            if (CornerRadiusDrawer.Draw(inputRect, ref value))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    editors[i]._cornerRadius = value;
                    ctx.CommandEmitter?.Emit(...);
                }
            }
        }
    }
}
```

---

## 6. V2 PropertyInspector (~500 lines)

```csharp
// PropertyInspector.cs - dramatically simplified

internal sealed class PropertyInspector
{
    private readonly DrawerContext _ctx;
    private readonly CommandBus _commandBus;

    // Popovers
    private readonly ColorPickerPopover _colorPicker;
    private readonly GradientEditorPopover _gradientEditor;

    public PropertyInspector(CommandBus commandBus)
    {
        _commandBus = commandBus;
        _ctx = new DrawerContext();
        _colorPicker = new ColorPickerPopover();
        _gradientEditor = new GradientEditorPopover();
    }

    // Entry point - delegates to generated code
    public void DrawEntityProperties(Entity entity)
    {
        _ctx.CurrentEntity = entity;
        _ctx.CommandEmitter = _commandBus;
        _ctx.IsMultiSelect = false;

        ComputeLayout(ImLayout.AvailableWidth);

        foreach (var componentKind in entity.GetSchemaComponents())
        {
            DrawComponentSection(entity, componentKind);
        }
    }

    public void DrawMultiEntityProperties(ReadOnlySpan<Entity> entities)
    {
        _ctx.SelectedEntities = entities;
        _ctx.CommandEmitter = _commandBus;
        _ctx.IsMultiSelect = true;

        ComputeLayout(ImLayout.AvailableWidth);

        // Find common components
        var commonComponents = GetCommonComponents(entities);

        foreach (var componentKind in commonComponents)
        {
            DrawComponentSectionMulti(entities, componentKind);
        }
    }

    private void DrawComponentSection(Entity entity, ComponentKind kind)
    {
        string header = SchemaRegistry.GetComponentDisplayName(kind);

        if (ImLayout.BeginCollapsibleSection(header))
        {
            // Generated drawer handles everything
            SchemaDrawers.DrawComponent(_ctx, entity, kind);
            ImLayout.EndCollapsibleSection();
        }
    }

    private void DrawComponentSectionMulti(ReadOnlySpan<Entity> entities, ComponentKind kind)
    {
        string header = SchemaRegistry.GetComponentDisplayName(kind);

        if (ImLayout.BeginCollapsibleSection(header))
        {
            SchemaDrawers.DrawComponentMulti(_ctx, entities, kind);
            ImLayout.EndCollapsibleSection();
        }
    }

    // Shared utilities used by generated drawers
    public void RenderPopovers()
    {
        _colorPicker.Render();
        _gradientEditor.Render();
    }

    private void ComputeLayout(float availableWidth)
    {
        _ctx.LabelWidth = Math.Min(120f, availableWidth * 0.35f);
        _ctx.InputWidth = availableWidth - _ctx.LabelWidth - 40f;  // key + bind icons
    }
}

// Shared context for all drawers
public sealed class DrawerContext
{
    public ICommandEmitter CommandEmitter;
    public Entity CurrentEntity;
    public bool IsMultiSelect;
    public ReadOnlySpan<Entity> SelectedEntities;

    public float LabelWidth;
    public float InputWidth;

    public IPopover ActivePopover;

    public void OpenPopover<T>(ImRect anchor, object state) where T : IPopover, new()
    {
        ActivePopover = new T();
        ActivePopover.Open(anchor, state);
    }

    public bool PopoverChanged<T>(out T value)
    {
        if (ActivePopover is IPopover<T> typed && typed.HasChanges)
        {
            value = typed.Value;
            return true;
        }
        value = default;
        return false;
    }
}
```

---

## 7. Built-in Default Drawers

```csharp
// These are used when no [Drawer<T>] is specified

public struct FloatDrawer : IPropertyDrawer<float>
{
    public bool Draw(ImRect rect, ref float value)
    {
        return Im.FloatInput("##float", rect, ref value);
    }
}

public struct IntDrawer : IPropertyDrawer<int>
{
    public bool Draw(ImRect rect, ref int value)
    {
        return Im.IntInput("##int", rect, ref value);
    }
}

public struct BoolDrawer : IPropertyDrawer<bool>
{
    public bool Draw(ImRect rect, ref bool value)
    {
        return Im.Checkbox("##bool", rect, ref value);
    }
}

public struct Vec2Drawer : IPropertyDrawer<Vector2>
{
    public bool Draw(ImRect rect, ref Vector2 value)
    {
        bool changed = false;
        float halfWidth = (rect.Width - 4f) / 2f;

        if (Im.FloatInput("##x", rect.X, rect.Y, halfWidth, rect.Height, ref value.X))
            changed = true;
        if (Im.FloatInput("##y", rect.X + halfWidth + 4f, rect.Y, halfWidth, rect.Height, ref value.Y))
            changed = true;

        return changed;
    }
}

public struct Vec3Drawer : IPropertyDrawer<Vector3>
{
    public bool Draw(ImRect rect, ref Vector3 value)
    {
        bool changed = false;
        float thirdWidth = (rect.Width - 8f) / 3f;
        float x = rect.X;

        if (Im.FloatInput("##x", x, rect.Y, thirdWidth, rect.Height, ref value.X))
            changed = true;
        x += thirdWidth + 4f;

        if (Im.FloatInput("##y", x, rect.Y, thirdWidth, rect.Height, ref value.Y))
            changed = true;
        x += thirdWidth + 4f;

        if (Im.FloatInput("##z", x, rect.Y, thirdWidth, rect.Height, ref value.Z))
            changed = true;

        return changed;
    }
}

public struct Vec4Drawer : IPropertyDrawer<Vector4>
{
    public bool Draw(ImRect rect, ref Vector4 value)
    {
        // Similar to Vec3 with 4 fields
    }
}

public struct Color32Drawer : IPropertyDrawerWithContext<Color32>
{
    public bool Draw(DrawerContext ctx, ImRect rect, ref Color32 value)
    {
        // Draw color swatch
        Im.DrawRect(rect, value);
        Im.DrawRectStroke(rect, Im.Style.Border);

        // Click opens color picker
        if (Im.IsItemClicked())
        {
            ctx.OpenPopover<ColorPickerPopover>(rect, ref value);
        }

        return ctx.PopoverChanged(out value);
    }
}

public struct StringHandleDrawer : IPropertyDrawer<StringHandle>
{
    public bool Draw(ImRect rect, ref StringHandle value)
    {
        Span<char> buffer = stackalloc char[256];
        int length = value.CopyTo(buffer);

        if (Im.TextInput("##str", buffer, ref length, rect))
        {
            value = new StringHandle(buffer[..length]);
            return true;
        }
        return false;
    }
}
```

---

## 8. Common Custom Drawer Patterns

### Enum Dropdown

```csharp
[Schema]
public partial struct TextComponent
{
    [Property]
    [Drawer<EnumDropdown<TextOverflow>>]
    public TextOverflow Overflow;
}

public struct EnumDropdown<TEnum> : IPropertyDrawer<TEnum> where TEnum : Enum
{
    private static readonly string[] _names = Enum.GetNames(typeof(TEnum));

    public bool Draw(ImRect rect, ref TEnum value)
    {
        int index = Convert.ToInt32(value);
        if (Im.Dropdown("##enum", _names, ref index, rect))
        {
            value = (TEnum)(object)index;
            return true;
        }
        return false;
    }
}
```

### Slider with Range

```csharp
[Schema]
public partial struct PaintLayer
{
    [Property]
    [Drawer<SliderDrawer>]
    [Range(0f, 1f)]
    public float Opacity;
}

public struct SliderDrawer : IPropertyDrawerWithMetadata<float>
{
    public bool Draw(ImRect rect, ref float value, PropertyMetadata meta)
    {
        float min = meta.GetFloat("Min", 0f);
        float max = meta.GetFloat("Max", 1f);

        return Im.Slider("##slider", rect, ref value, min, max);
    }
}
```

### Asset Reference Picker

```csharp
[Schema]
public partial struct SpriteComponent
{
    [Property]
    [Drawer<AssetPicker<Texture>>]
    public AssetRef<Texture> Texture;
}

public struct AssetPicker<T> : IPropertyDrawerWithContext<AssetRef<T>>
{
    public bool Draw(DrawerContext ctx, ImRect rect, ref AssetRef<T> value)
    {
        var asset = ctx.AssetDatabase.Load(value);
        DrawAssetPreview(rect, asset);

        if (Im.IsItemClicked())
        {
            ctx.OpenPopover<AssetBrowserPopover<T>>(rect, ref value);
        }

        return ctx.PopoverChanged(out value);
    }
}
```

### Gradient with Popover

```csharp
[Schema]
public partial struct PaintLayer
{
    [Property]
    [Drawer<GradientDrawer>]
    public GradientData Gradient;
}

public struct GradientDrawer : IPropertyDrawerWithContext<GradientData>
{
    public bool Draw(DrawerContext ctx, ImRect rect, ref GradientData value)
    {
        DrawGradientPreview(rect, value);

        if (Im.IsItemClicked())
        {
            ctx.OpenPopover<GradientEditorPopover>(rect, ref value);
        }

        return ctx.PopoverChanged(out value);
    }

    private void DrawGradientPreview(ImRect rect, GradientData gradient)
    {
        // Draw gradient bar preview
        for (int i = 0; i < gradient.Stops.Length - 1; i++)
        {
            var stop0 = gradient.Stops[i];
            var stop1 = gradient.Stops[i + 1];
            float x0 = rect.X + stop0.T * rect.Width;
            float x1 = rect.X + stop1.T * rect.Width;
            Im.DrawRectGradientH(x0, rect.Y, x1 - x0, rect.Height, stop0.Color, stop1.Color);
        }
    }
}
```

### Inline Nested Struct

```csharp
[Schema]
public partial struct PaintLayer
{
    [Property]
    [Drawer<InlineDrawer<GradientSettings>>]
    public GradientSettings Gradient;
}

[Schema]
public partial struct GradientSettings
{
    [Property] public Color32 ColorA;
    [Property] public Color32 ColorB;
    [Property] public float Angle;
    [Property] public GradientType Type;
}

// Built-in - recursively draws child properties
public struct InlineDrawer<T> : IPropertyDrawerWithContext<T> where T : struct
{
    public bool Draw(DrawerContext ctx, ImRect rect, ref T value)
    {
        bool changed = false;

        ImLayout.Indent();
        changed = SchemaDrawers.DrawInline<T>(ctx, ref value);
        ImLayout.Unindent();

        return changed;
    }
}
```

### Array Element Drawer

```csharp
[Schema]
public partial struct PaintComponent
{
    [Property]
    [EditorResizable]
    [ElementDrawer<PaintLayerElementDrawer>]
    public PaintLayer[] Layers;
}

public struct PaintLayerElementDrawer : IArrayElementDrawer<PaintLayer>
{
    public bool Draw(DrawerContext ctx, ImRect rect, int index, ref PaintLayer value)
    {
        bool changed = false;

        // Layer header with visibility toggle and delete button
        var headerRect = rect.SliceTop(24f);

        if (Im.Checkbox("##vis", headerRect.SliceLeft(24f), ref value.IsVisible))
            changed = true;

        Im.Label($"Layer {index}", headerRect);

        if (Im.Button("X", headerRect.SliceRight(24f)))
        {
            ctx.RequestRemoveElement(index);
        }

        // Layer content
        var contentRect = rect;
        ImLayout.Indent();

        if (value.Kind == PaintLayerKind.Fill)
        {
            if (Color32Drawer.Draw(ctx, contentRect.SliceTop(24f), ref value.FillColor))
                changed = true;
        }
        else
        {
            if (FloatDrawer.Draw(contentRect.SliceTop(24f), ref value.StrokeWidth))
                changed = true;
        }

        ImLayout.Unindent();

        return changed;
    }
}
```

---

## 9. Comparison: Current vs V2

| Aspect | Current | V2 |
|--------|---------|-----|
| **PropertyInspector.cs** | 5400 lines | ~500 lines |
| **Property type switch** | 200+ line switch | Generated per-component |
| **Multi-select** | Duplicated 400 lines | Generated from same schema |
| **Custom drawer declaration** | `[PropertyDrawer(typeof(...))]` | `[Drawer<T>]` generic |
| **Custom drawer interface** | Static class, manual signature | `IPropertyDrawer<T>` interface |
| **Command emission** | Manual in every drawer | Generated, automatic |
| **Key icon** | Manual per-property | Generated, automatic |
| **Binding menu** | Manual per-property | Generated, automatic |
| **Adding new property type** | Modify giant switch | Uses default drawer |
| **Adding new component** | Add cases to switch | Add `[Schema]`, rebuild |
| **Property groups** | Hardcoded methods | From `[Property(Group=)]` |

---

## 10. Workflow Summary

| Step | What You Do |
|------|-------------|
| 1 | Add `[Drawer<YourDrawer>]` attribute to property |
| 2 | Implement `IPropertyDrawer<T>` (simple) or `IPropertyDrawerWithContext<T>` (needs popovers) |
| 3 | Rebuild - generator wires it up automatically |

**You never touch:**
- PropertyInspector.cs
- Any switch statements
- Command emission code
- Key icon rendering
- Binding context menus
- Multi-select handling

---

## Related Documents

- `2025-01-30_V2_Editor_Codegen.md` - Editor storage and proxy generation
- `2025-01-30_V2_Property_System_Spec.md` - Proxy modes, command system
- `2025-01-30_V2_Derp_UI_Integration.md` - Full integration with Derp.UI
