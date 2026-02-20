using System;
using System.Collections.Generic;
using System.Numerics;
using Core;
using Pooled.Runtime;
using Property;
using Property.Runtime;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;
using DerpLib.ImGui.Widgets;
using DerpLib.Sdf;
using FontAwesome.Sharp;
using static Derp.UI.UiColor32;
using static Derp.UI.UiFillGradient;

namespace Derp.UI;

internal sealed partial class PropertyInspector
{
    private const int PropertyTextBufferSize = 256;
    private const float InspectorSectionHeaderScale = 1.2f;
    private const float InspectorComponentHeaderScale = 1.1f;
    private const float InspectorGroupHeaderScale = 1.05f;
    private const float InspectorHeaderDividerThickness = 1f;
    private const float InspectorRowPaddingX = 8f;
    private const float KeyIconWidth = 18f;

    private static string[] _eventListenerVariableDropdownOptions = new string[96];
    private static ushort[] _eventListenerVariableDropdownOptionIds = new ushort[96];

    private static readonly string[] StrokeCapOptions = new string[]
    {
        "Butt",
        "Round",
        "Square",
        "Soft"
    };

    private static readonly string[] BlendModeOptions = new string[]
    {
        "Normal",
        "Darken",
        "Multiply",
        "Color Burn",
        "Lighten",
        "Screen",
        "Color Dodge",
        "Overlay",
        "Soft Light",
        "Hard Light",
        "Difference",
        "Exclusion",
        "Hue",
        "Saturation",
        "Color",
        "Luminosity"
    };

    private static readonly string[] LayoutTypeOptions = new string[]
    {
        "Stack",
        "Flex",
        "Grid"
    };

    private static readonly string[] LayoutDirectionOptions = new string[]
    {
        "Row",
        "Column"
    };

    private static readonly string[] LayoutAlignmentOptions = new string[]
    {
        "Start",
        "Center",
        "End"
    };

    private static readonly string[] LayoutJustifyOptions = new string[]
    {
        "Start",
        "Center",
        "End",
        "Space Between",
        "Space Around",
        "Space Evenly"
    };

    private static readonly string[] LayoutAlignItemsOptions = new string[]
    {
        "Start",
        "Center",
        "End",
        "Stretch"
    };

    private static readonly string[] LayoutAlignSelfOptions = new string[]
    {
        "Auto",
        "Start",
        "Center",
        "End",
        "Stretch"
    };

    private static readonly string[] LayoutSizeModeOptions = new string[]
    {
        "Fixed",
        "Hug",
        "Fill"
    };

    private readonly World _propertyWorld;
    private readonly UiWorkspace _workspace;
    private readonly WorkspaceCommands _commands;
    private readonly ColorPicker _colorPicker;
    private readonly PaintLayerOptionsPopover _paintLayerOptionsPopover;
    private readonly DataBindPopover _dataBindPopover;
    private readonly PrefabVariableColorPopover _prefabVariableColorPopover;

    private readonly List<PropertyUiItem> _propertyUiItems = new(capacity: 32);
    private int _propertyUiPropertyCount = -1;
    private ulong _propertyUiSignature;

    private ImRect _inspectorContentRectViewport;

    internal void SetInspectorContentRectViewport(ImRect rectViewport)
    {
        _inspectorContentRectViewport = rectViewport;
        _paintLayerOptionsPopover.SetInspectorContentRectViewport(rectViewport);
        _dataBindPopover.SetInspectorContentRectViewport(rectViewport);
        _prefabVariableColorPopover.SetInspectorContentRectViewport(rectViewport);
    }

    internal bool HasOpenInspectorPopovers(DerpLib.ImGui.Viewport.ImViewport viewport)
    {
        return _colorPicker.HasOpenPopoverForViewport(viewport) ||
               _paintLayerOptionsPopover.IsOpenForViewport(viewport) ||
               _dataBindPopover.IsOpenForViewport(viewport) ||
               _prefabVariableColorPopover.IsOpenForViewport(viewport);
    }

    internal bool TryGetInspectorContentRectViewport(out ImRect rectViewport)
    {
        rectViewport = _inspectorContentRectViewport;
        return rectViewport.Width > 0f && rectViewport.Height > 0f;
    }

    private readonly Dictionary<ulong, TextPropertyEditState> _textPropertyEditStates = new(capacity: 64);

    private readonly StringHandle _notesLabelHandle;
    private readonly StringHandle _descriptionLabelHandle;
    private readonly StringHandle _trimGroupHandle;
    private readonly StringHandle _dashGroupHandle;
    private readonly StringHandle _transformGroupHandle;
    private readonly StringHandle _appearanceGroupHandle;
    private readonly StringHandle _metadataGroupHandle;
    private readonly StringHandle _positionLabelHandle;
    private readonly StringHandle _scaleLabelHandle;
    private readonly StringHandle _anchorLabelHandle;
    private readonly StringHandle _rotationLabelHandle;
    private readonly StringHandle _depthLabelHandle;
    private readonly StringHandle _capLabelHandle;
    private readonly StringHandle _blendModeLabelHandle;
    private readonly StrokePropertyUiItemComparer _strokePropertyComparer;
    private readonly StringHandle _booleanOperationLabelHandle;
    private readonly StringHandle _layoutContainerGroupHandle;
    private readonly StringHandle _layoutContainerLayoutLabelHandle;
    private readonly StringHandle _layoutContainerDirectionLabelHandle;
    private readonly StringHandle _layoutContainerAlignItemsLabelHandle;
    private readonly StringHandle _layoutContainerJustifyLabelHandle;
    private readonly StringHandle _layoutContainerWidthModeLabelHandle;
    private readonly StringHandle _layoutContainerHeightModeLabelHandle;
    private readonly StringHandle _layoutContainerGridColumnsLabelHandle;
    private readonly StringHandle _layoutContainerEnabledLabelHandle;
    private readonly StringHandle _layoutContainerPaddingLabelHandle;
    private readonly StringHandle _layoutContainerSpacingLabelHandle;

    private readonly StringHandle _layoutChildGroupHandle;
    private readonly StringHandle _layoutChildIgnoreLayoutLabelHandle;
    private readonly StringHandle _layoutChildMarginLabelHandle;
    private readonly StringHandle _layoutChildAlignSelfLabelHandle;
    private readonly StringHandle _layoutChildPreferredSizeLabelHandle;
    private readonly StringHandle _layoutChildFlexGrowLabelHandle;
    private readonly StringHandle _layoutChildFlexShrinkLabelHandle;

    private readonly StringHandle _layoutConstraintGroupHandle;
    private readonly StringHandle _layoutConstraintEnabledLabelHandle;
    private readonly StringHandle _layoutConstraintAnchorMinLabelHandle;
    private readonly StringHandle _layoutConstraintAnchorMaxLabelHandle;
    private readonly StringHandle _layoutConstraintOffsetMinLabelHandle;
    private readonly StringHandle _layoutConstraintOffsetMaxLabelHandle;
    private readonly StringHandle _layoutConstraintMinSizeLabelHandle;
    private readonly StringHandle _layoutConstraintMaxSizeLabelHandle;

    private readonly StringHandle _textGroupHandle;
    private readonly StringHandle _textOverflowLabelHandle;
    private readonly StringHandle _textAlignXLabelHandle;
    private readonly StringHandle _textAlignYLabelHandle;
    private readonly StringHandle _textFontLabelHandle;

    private static readonly string[] TextOverflowOptions = { "Visible", "Hidden", "Clipped", "Ellipsis", "Fit" };
    private static readonly string[] TextAlignXOptions = { "Left", "Center", "Right" };
    private static readonly string[] TextAlignYOptions = { "Top", "Middle", "Bottom" };
    private static readonly string[] TextFontOptions = { "arial" };

    private float _propertyTableLabelWidth;
    private float _propertyTableInputWidth;
    private float _propertyTableTotalWidth;
    private ulong _propertyTableSignature;

    private PropertySlot[] _multiSlotsScratch = new PropertySlot[2048];
    private PropertyValue[] _multiValuesScratch = new PropertyValue[2048];

    private EntityId _gizmoTargetEntity;

    private readonly struct PrefabOverrideUiState
    {
        public readonly bool IsOverridden;
        public readonly PropertyValue DefaultValue;

        public PrefabOverrideUiState(bool isOverridden, in PropertyValue defaultValue)
        {
            IsOverridden = isOverridden;
            DefaultValue = defaultValue;
        }
    }

    private sealed class TextPropertyEditState
    {
        public char[] Buffer = new char[PropertyTextBufferSize];
        public int Length;
        public StringHandle Handle;
    }

    private sealed class StrokePropertyUiItemComparer : IComparer<PropertyUiItem>
    {
        private readonly StringHandle _strokeGroupHandle;
        private readonly StringHandle _dashGroupHandle;
        private readonly StringHandle _trimGroupHandle;

        public StrokePropertyUiItemComparer(StringHandle strokeGroupHandle, StringHandle dashGroupHandle, StringHandle trimGroupHandle)
        {
            _strokeGroupHandle = strokeGroupHandle;
            _dashGroupHandle = dashGroupHandle;
            _trimGroupHandle = trimGroupHandle;
        }

        public int Compare(PropertyUiItem x, PropertyUiItem y)
        {
            int groupOrderX = GetGroupOrder(x.Group);
            int groupOrderY = GetGroupOrder(y.Group);
            if (groupOrderX != groupOrderY)
            {
                return groupOrderX.CompareTo(groupOrderY);
            }

            int orderX = x.Info.Order;
            int orderY = y.Info.Order;
            if (orderX != orderY)
            {
                return orderX.CompareTo(orderY);
            }

            return x.Info.PropertyId.CompareTo(y.Info.PropertyId);
        }

        private int GetGroupOrder(StringHandle group)
        {
            if (!group.IsValid)
            {
                return 0;
            }
            if (group == _strokeGroupHandle)
            {
                return 1;
            }
            if (group == _dashGroupHandle)
            {
                return 2;
            }
            if (group == _trimGroupHandle)
            {
                return 3;
            }
            return 4;
        }
    }

    private enum PropertyUiKind : byte
    {
        Single = 0,
        Vector2 = 2,
        Vector3 = 3,
        Vector4 = 4,
    }

    private struct PropertyUiItem
    {
        public PropertyUiKind Kind;
        public ushort Index0;
        public ushort Index1;
        public ushort Index2;
        public ushort Index3;
        public PropertyInfo Info;
        public StringHandle Group;
        public string Label;
        public string WidgetId;
        public string? WidgetId2;
    }

    public PropertyInspector(World propertyWorld, UiWorkspace workspace, WorkspaceCommands commands)
    {
        _propertyWorld = propertyWorld;
        _workspace = workspace;
        _commands = commands;
        _colorPicker = new ColorPicker(_propertyWorld, _commands);
        _colorPicker.SetBindingInspector(this);
        _paintLayerOptionsPopover = new PaintLayerOptionsPopover(_colorPicker);
        _dataBindPopover = new DataBindPopover();
        _prefabVariableColorPopover = new PrefabVariableColorPopover();
        _notesLabelHandle = "Notes";
        _descriptionLabelHandle = "Description";
        _trimGroupHandle = "Trim";
        _dashGroupHandle = "Dash";
        _transformGroupHandle = "Transform";
        _appearanceGroupHandle = "Appearance";
        _metadataGroupHandle = "Metadata";
        _positionLabelHandle = "Position";
        _scaleLabelHandle = "Scale";
        _anchorLabelHandle = "Anchor";
        _rotationLabelHandle = "Rotation";
        _depthLabelHandle = "Depth";
        _booleanOperationLabelHandle = "Operation";
        _capLabelHandle = "Cap";
        _blendModeLabelHandle = "Blend Mode";
        _strokePropertyComparer = new StrokePropertyUiItemComparer(strokeGroupHandle: "Stroke", dashGroupHandle: _dashGroupHandle, trimGroupHandle: _trimGroupHandle);
        _layoutContainerGroupHandle = "Layout Container";
        _layoutContainerEnabledLabelHandle = "Container Enabled";
        _layoutContainerLayoutLabelHandle = "Layout";
        _layoutContainerDirectionLabelHandle = "Direction";
        _layoutContainerPaddingLabelHandle = "Padding";
        _layoutContainerSpacingLabelHandle = "Spacing";
        _layoutContainerAlignItemsLabelHandle = "Align Items";
        _layoutContainerJustifyLabelHandle = "Justify";
        _layoutContainerWidthModeLabelHandle = "Width Mode";
        _layoutContainerHeightModeLabelHandle = "Height Mode";
        _layoutContainerGridColumnsLabelHandle = "Grid Columns";

        _layoutChildGroupHandle = "Layout Child";
        _layoutChildIgnoreLayoutLabelHandle = "Ignore Layout";
        _layoutChildMarginLabelHandle = "Margin";
        _layoutChildAlignSelfLabelHandle = "Align Self";
        _layoutChildPreferredSizeLabelHandle = "Preferred Size";
        _layoutChildFlexGrowLabelHandle = "Flex Grow";
        _layoutChildFlexShrinkLabelHandle = "Flex Shrink";

        _layoutConstraintGroupHandle = "Layout Constraint";
        _layoutConstraintEnabledLabelHandle = "Constraints Enabled";
        _layoutConstraintAnchorMinLabelHandle = "Anchor Min";
        _layoutConstraintAnchorMaxLabelHandle = "Anchor Max";
        _layoutConstraintOffsetMinLabelHandle = "Offset Min";
        _layoutConstraintOffsetMaxLabelHandle = "Offset Max";
        _layoutConstraintMinSizeLabelHandle = "Min Size";
        _layoutConstraintMaxSizeLabelHandle = "Max Size";

        _textGroupHandle = "Text";
        _textOverflowLabelHandle = "Overflow";
        _textAlignXLabelHandle = "Align X";
        _textAlignYLabelHandle = "Align Y";
        _textFontLabelHandle = "Font";
    }

    public bool HasComponentPropertiesInGroup(AnyComponentHandle component, StringHandle group)
    {
        int propertyCount = PropertyDispatcher.GetPropertyCount(component);
        if (propertyCount <= 0)
        {
            return false;
        }

        EnsurePropertyUiCache(component, propertyCount);
        for (int i = 0; i < _propertyUiItems.Count; i++)
        {
            if (_propertyUiItems[i].Group == group)
            {
                return true;
            }
        }

        return false;
    }

    public void DrawComponentPropertiesGroupContent(AnyComponentHandle component, StringHandle group)
    {
        int propertyCount = PropertyDispatcher.GetPropertyCount(component);
        if (propertyCount <= 0)
        {
            return;
        }

        EnsurePropertyUiCache(component, propertyCount);
        GetPropertyTableLayout(component, propertyCount, out float labelWidth, out float inputWidth);

        Im.Context.BeginFocusScope();

        if (component.Kind == TransformComponent.Api.PoolIdConst && group == _transformGroupHandle)
        {
            DrawTransformGroupProperties(component, labelWidth, inputWidth);
            Im.Context.EndFocusScope();
            return;
        }

        for (int itemIndex = 0; itemIndex < _propertyUiItems.Count; itemIndex++)
        {
            PropertyUiItem item = _propertyUiItems[itemIndex];
            if (item.Group != group)
            {
                continue;
            }

            if (item.Kind == PropertyUiKind.Single)
            {
                DrawSingleProperty(component, item, labelWidth, inputWidth);
            }
            else
            {
                DrawVectorGroup(component, item, labelWidth, inputWidth);
            }
        }

        Im.Context.EndFocusScope();
    }

    public static void DrawInspectorSectionHeader(string label)
    {
        DrawHeader(label, InspectorSectionHeaderScale, Im.Style.TextPrimary);
    }

    public static void DrawInspectorGroupHeader(string label)
    {
        DrawHeader(label, InspectorGroupHeaderScale, Im.Style.TextSecondary, paddingX: InspectorRowPaddingX);
    }

    private static void DrawInspectorComponentHeader(string label)
    {
        DrawHeader(label, InspectorComponentHeaderScale, Im.Style.TextPrimary);
    }

    private static void DrawHeader(string label, float fontScale, uint color)
    {
        DrawHeader(label, fontScale, color, paddingX: 0f);
    }

    private static void DrawHeader(string label, float fontScale, uint color, float paddingX)
    {
        var style = Im.Style;
        float fontSize = style.FontSize * fontScale;
        float height = fontSize + style.Spacing;
        var rect = ImLayout.AllocateRect(0f, height);
        float textY = rect.Y + (height - fontSize) * 0.5f;
        Im.Text(label.AsSpan(), rect.X + paddingX, textY, fontSize, color);
        ImDivider.Draw(InspectorHeaderDividerThickness, style.Border);
    }

    public void DrawShapeProperties(in UiWorkspace.Shape shape)
    {
        ImLayout.Space(12f);
        DrawInspectorSectionHeader("Properties");

        if (!shape.Transform.IsNull)
        {
            DrawComponentProperties("Transform", TransformComponentProperties.ToAnyHandle(shape.Transform));
        }

        ShapeKind kind = GetShapeKind(shape);
        if (kind == ShapeKind.Rect && !shape.RectGeometry.IsNull)
        {
            DrawComponentProperties("Geometry", RectGeometryComponentProperties.ToAnyHandle(shape.RectGeometry));
        }
        else if (kind == ShapeKind.Circle && !shape.CircleGeometry.IsNull)
        {
            DrawComponentProperties("Geometry", CircleGeometryComponentProperties.ToAnyHandle(shape.CircleGeometry));
        }
    }

    public void DrawComponentProperties(string header, AnyComponentHandle component)
    {
        int propertyCount = PropertyDispatcher.GetPropertyCount(component);
        if (propertyCount <= 0)
        {
            return;
        }

        DrawInspectorComponentHeader(header);
        DrawPropertyList(component, propertyCount, header);
    }

    public void DrawComponentPropertiesContent(string header, AnyComponentHandle component)
    {
        int propertyCount = PropertyDispatcher.GetPropertyCount(component);
        if (propertyCount <= 0)
        {
            return;
        }

        _gizmoTargetEntity = EntityId.Null;
        _commands.SetActiveInspectorEntity(EntityId.Null);
        DrawPropertyList(component, propertyCount, header);
        _commands.SetActiveInspectorEntity(EntityId.Null);
        _gizmoTargetEntity = EntityId.Null;
    }

    public void DrawComponentPropertiesContent(EntityId targetEntity, string header, AnyComponentHandle component)
    {
        int propertyCount = PropertyDispatcher.GetPropertyCount(component);
        if (propertyCount <= 0)
        {
            return;
        }

        _gizmoTargetEntity = targetEntity;
        _commands.SetActiveInspectorEntity(targetEntity);
        DrawPropertyList(component, propertyCount, header);
        _commands.SetActiveInspectorEntity(EntityId.Null);
        _gizmoTargetEntity = EntityId.Null;
    }

    public void DrawMultiComponentPropertiesContent(string header, ReadOnlySpan<AnyComponentHandle> components)
    {
        if (components.IsEmpty)
        {
            return;
        }

        AnyComponentHandle component0 = components[0];
        if (component0.IsNull)
        {
            return;
        }

        int propertyCount = PropertyDispatcher.GetPropertyCount(component0);
        if (propertyCount <= 0)
        {
            return;
        }

        _gizmoTargetEntity = EntityId.Null;
        _commands.SetActiveInspectorEntity(EntityId.Null);
        DrawPropertyListMulti(component0, components, propertyCount, header);
        _commands.SetActiveInspectorEntity(EntityId.Null);
        _gizmoTargetEntity = EntityId.Null;
    }

    public void DrawFillPropertiesContent(FillComponentHandle fillHandle)
    {
        var fillView = FillComponent.Api.FromHandle(_propertyWorld, fillHandle);
        if (!fillView.IsAlive)
        {
            return;
        }

        float height = Im.Style.MinButtonHeight;
        var rect = ImLayout.AllocateRect(0f, height);
        var padded = GetPaddedRowRect(rect);
        _colorPicker.DrawFillPreviewRow(padded, fillHandle);
    }

    public void DrawFillPreviewRow(ImRect rect, FillComponentHandle fillHandle)
    {
        _colorPicker.DrawFillPreviewRow(rect, fillHandle);
    }

    public void DrawColor32PreviewRow(ImRect rect, string pickerId, PropertySlot slot, bool showOpacity)
    {
        _colorPicker.DrawColor32PreviewRow(rect, pickerId, slot, showOpacity);
    }

    public void DrawPaintFillLayerPreviewRow(ImRect rect, string pickerId, PaintComponentHandle paintHandle, int layerIndex)
    {
        _colorPicker.DrawPaintFillLayerPreviewRow(rect, pickerId, paintHandle, layerIndex);
    }

    public void DrawPaintStrokeLayerPreviewRow(ImRect rect, string pickerId, PaintComponentHandle paintHandle, int layerIndex)
    {
        _colorPicker.DrawPaintStrokeLayerPreviewRow(rect, pickerId, paintHandle, layerIndex);
    }

    public bool ColorPopoverCapturesMouse(DerpLib.ImGui.Viewport.ImViewport viewport)
    {
        return _colorPicker.CapturesMouse(viewport);
    }

    internal bool TryGetActivePaintFillGradientEditor(out EntityId targetEntity, out PaintComponentHandle paintHandle, out int layerIndex)
    {
        return _colorPicker.TryGetActivePaintFillGradientEditor(out targetEntity, out paintHandle, out layerIndex);
    }

    internal void SuppressColorPopoverCloseThisFrame()
    {
        _colorPicker.SuppressCloseThisFrame();
    }

    public void RenderInspectorColorPopover()
    {
        _colorPicker.RenderPopover();
    }

    public bool InspectorPopoversCaptureMouse(DerpLib.ImGui.Viewport.ImViewport viewport)
    {
        return _colorPicker.CapturesMouse(viewport) ||
               _paintLayerOptionsPopover.CapturesMouse(viewport) ||
               _dataBindPopover.CapturesMouse(viewport) ||
               _prefabVariableColorPopover.CapturesMouse(viewport);
    }

    public void RenderInspectorPopovers(UiWorkspace workspace)
    {
        _colorPicker.RenderPopover();
        _paintLayerOptionsPopover.RenderPopover(workspace);
        _dataBindPopover.RenderPopover(workspace);
        _prefabVariableColorPopover.RenderPopover(workspace);
    }

    public void OpenPaintLayerOptionsPopover(EntityId ownerEntity, int layerIndex, ImRect anchorRectViewport)
    {
        _paintLayerOptionsPopover.Open(ownerEntity, layerIndex, anchorRectViewport);
    }

    public void OpenPrefabVariableColorPopover(EntityId ownerEntity, ushort variableId, bool editInstance, ImRect anchorRectViewport)
    {
        _prefabVariableColorPopover.Open(ownerEntity, variableId, editInstance, anchorRectViewport);
    }

    private ShapeKind GetShapeKind(in UiWorkspace.Shape shape)
    {
        var view = ShapeComponent.Api.FromHandle(_propertyWorld, shape.Component);
        if (!view.IsAlive)
        {
            return ShapeKind.Rect;
        }
        return view.Kind;
    }

    private void DrawPropertyList(AnyComponentHandle component, int propertyCount, string? suppressGroupLabel)
    {
        EnsurePropertyUiCache(component, propertyCount);
        GetPropertyTableLayout(component, propertyCount, out float labelWidth, out float inputWidth);

        Im.Context.BeginFocusScope();

        bool hasRenderedLayoutContainerGroup = false;
        bool hasRenderedLayoutChildGroup = false;
        bool hasRenderedLayoutConstraintGroup = false;

        StringHandle currentGroup = default;
        bool isFirstGroup = true;
        int itemIndex = 0;
        while (itemIndex < _propertyUiItems.Count)
        {
            PropertyUiItem item = _propertyUiItems[itemIndex];
            if (item.Group != currentGroup)
            {
                currentGroup = item.Group;
                string groupLabel = currentGroup.ToString();

                bool isTransformComponent = component.Kind == TransformComponent.Api.PoolIdConst;
                bool isLayoutContainerGroup = isTransformComponent && currentGroup == _layoutContainerGroupHandle;
                bool isLayoutChildGroup = isTransformComponent && currentGroup == _layoutChildGroupHandle;
                bool isLayoutConstraintGroup = isTransformComponent && currentGroup == _layoutConstraintGroupHandle;
                bool isLayoutGroup = isLayoutContainerGroup || isLayoutChildGroup || isLayoutConstraintGroup;

                bool layoutGroupAlreadyRendered =
                    (isLayoutContainerGroup && hasRenderedLayoutContainerGroup) ||
                    (isLayoutChildGroup && hasRenderedLayoutChildGroup) ||
                    (isLayoutConstraintGroup && hasRenderedLayoutConstraintGroup);
                if (!string.IsNullOrEmpty(groupLabel))
                {
                    if (!string.IsNullOrEmpty(suppressGroupLabel) &&
                        string.Equals(groupLabel, suppressGroupLabel, StringComparison.OrdinalIgnoreCase))
                    {
                        groupLabel = string.Empty;
                    }

                    if (layoutGroupAlreadyRendered)
                    {
                        groupLabel = string.Empty;
                    }

                    if (!isFirstGroup && !string.IsNullOrEmpty(groupLabel))
                    {
                        ImLayout.Space(6f);
                        DrawInspectorGroupHeader(groupLabel);
                    }
                }

                isFirstGroup = false;

                if (isLayoutGroup)
                {
                    if (layoutGroupAlreadyRendered)
                    {
                        itemIndex = SkipGroupItems(itemIndex, currentGroup);
                        continue;
                    }

                    if (isLayoutContainerGroup)
                    {
                        DrawLayoutContainerGroupProperties(component, labelWidth, inputWidth);
                        hasRenderedLayoutContainerGroup = true;
                        itemIndex = SkipGroupItems(itemIndex, currentGroup);
                        continue;
                    }

                    if (isLayoutChildGroup)
                    {
                        DrawLayoutChildAndConstraintGroupProperties(component, labelWidth, inputWidth);
                        hasRenderedLayoutChildGroup = true;
                        hasRenderedLayoutConstraintGroup = true;
                        itemIndex = SkipGroupItems(itemIndex, currentGroup);
                        continue;
                    }

                    if (isLayoutConstraintGroup)
                    {
                        DrawLayoutConstraintGroupProperties(component, labelWidth, inputWidth);
                        hasRenderedLayoutConstraintGroup = true;
                        itemIndex = SkipGroupItems(itemIndex, currentGroup);
                        continue;
                    }
                }
            }

            if (component.Kind == TransformComponent.Api.PoolIdConst)
            {
                if ((hasRenderedLayoutContainerGroup && item.Group == _layoutContainerGroupHandle) ||
                    (hasRenderedLayoutChildGroup && item.Group == _layoutChildGroupHandle) ||
                    (hasRenderedLayoutConstraintGroup && item.Group == _layoutConstraintGroupHandle))
                {
                    itemIndex++;
                    continue;
                }
            }

            if (item.Kind == PropertyUiKind.Single)
            {
                DrawSingleProperty(component, item, labelWidth, inputWidth);
            }
            else
            {
                DrawVectorGroup(component, item, labelWidth, inputWidth);
            }

            itemIndex++;
        }

        Im.Context.EndFocusScope();
    }

    private void DrawPropertyListMulti(AnyComponentHandle component0, ReadOnlySpan<AnyComponentHandle> components, int propertyCount, string? suppressGroupLabel)
    {
        EnsurePropertyUiCache(component0, propertyCount);
        GetPropertyTableLayout(component0, propertyCount, out float labelWidth, out float inputWidth);

        Im.Context.BeginFocusScope();

        bool hasRenderedLayoutContainerGroup = false;
        bool hasRenderedLayoutChildGroup = false;
        bool hasRenderedLayoutConstraintGroup = false;

        StringHandle currentGroup = default;
        bool isFirstGroup = true;
        int itemIndex = 0;
        while (itemIndex < _propertyUiItems.Count)
        {
            PropertyUiItem item = _propertyUiItems[itemIndex];
            if (item.Group != currentGroup)
            {
                currentGroup = item.Group;
                string groupLabel = currentGroup.ToString();

                bool isTransformComponent = component0.Kind == TransformComponent.Api.PoolIdConst;
                bool isLayoutContainerGroup = isTransformComponent && currentGroup == _layoutContainerGroupHandle;
                bool isLayoutChildGroup = isTransformComponent && currentGroup == _layoutChildGroupHandle;
                bool isLayoutConstraintGroup = isTransformComponent && currentGroup == _layoutConstraintGroupHandle;
                bool isLayoutGroup = isLayoutContainerGroup || isLayoutChildGroup || isLayoutConstraintGroup;

                bool layoutGroupAlreadyRendered =
                    (isLayoutContainerGroup && hasRenderedLayoutContainerGroup) ||
                    (isLayoutChildGroup && hasRenderedLayoutChildGroup) ||
                    (isLayoutConstraintGroup && hasRenderedLayoutConstraintGroup);
                if (!string.IsNullOrEmpty(groupLabel))
                {
                    if (!string.IsNullOrEmpty(suppressGroupLabel) &&
                        string.Equals(groupLabel, suppressGroupLabel, StringComparison.OrdinalIgnoreCase))
                    {
                        groupLabel = string.Empty;
                    }

                    if (layoutGroupAlreadyRendered)
                    {
                        groupLabel = string.Empty;
                    }

                    if (!isFirstGroup && !string.IsNullOrEmpty(groupLabel))
                    {
                        ImLayout.Space(6f);
                        DrawInspectorGroupHeader(groupLabel);
                    }
                }

                isFirstGroup = false;

                if (isLayoutGroup)
                {
                    if (layoutGroupAlreadyRendered)
                    {
                        itemIndex = SkipGroupItems(itemIndex, currentGroup);
                        continue;
                    }

                    if (isLayoutContainerGroup)
                    {
                        DrawLayoutContainerGroupPropertiesMulti(component0, components, labelWidth, inputWidth);
                        hasRenderedLayoutContainerGroup = true;
                        itemIndex = SkipGroupItems(itemIndex, currentGroup);
                        continue;
                    }

                    if (isLayoutChildGroup)
                    {
                        DrawLayoutChildAndConstraintGroupPropertiesMulti(component0, components, labelWidth, inputWidth);
                        hasRenderedLayoutChildGroup = true;
                        hasRenderedLayoutConstraintGroup = true;
                        itemIndex = SkipGroupItems(itemIndex, currentGroup);
                        continue;
                    }

                    if (isLayoutConstraintGroup)
                    {
                        DrawLayoutConstraintGroupPropertiesMulti(component0, components, labelWidth, inputWidth);
                        hasRenderedLayoutConstraintGroup = true;
                        itemIndex = SkipGroupItems(itemIndex, currentGroup);
                        continue;
                    }
                }
            }

            if (component0.Kind == TransformComponent.Api.PoolIdConst)
            {
                if ((hasRenderedLayoutContainerGroup && item.Group == _layoutContainerGroupHandle) ||
                    (hasRenderedLayoutChildGroup && item.Group == _layoutChildGroupHandle) ||
                    (hasRenderedLayoutConstraintGroup && item.Group == _layoutConstraintGroupHandle))
                {
                    itemIndex++;
                    continue;
                }
            }

            if (item.Kind == PropertyUiKind.Single)
            {
                DrawSinglePropertyMulti(component0, components, item, labelWidth, inputWidth);
            }
            else
            {
                DrawVectorGroupMulti(component0, components, item, labelWidth, inputWidth);
            }

            itemIndex++;
        }

        Im.Context.EndFocusScope();
    }

    private int SkipGroupItems(int startIndex, StringHandle group)
    {
        int itemIndex = startIndex;
        while (itemIndex < _propertyUiItems.Count && _propertyUiItems[itemIndex].Group == group)
        {
            itemIndex++;
        }

        return itemIndex;
    }

    private void DrawLayoutContainerGroupProperties(AnyComponentHandle component, float labelWidth, float inputWidth)
    {
        TryDrawGroupItem(component, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerEnabledLabelHandle);
        if (!TryGetBool(component, _layoutContainerGroupHandle, _layoutContainerEnabledLabelHandle, out bool enabled) || !enabled)
        {
            return;
        }

        DrawLayoutListBindProperty(labelWidth, inputWidth);

        TryDrawGroupItem(component, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerLayoutLabelHandle);
        if (!TryGetInt(component, _layoutContainerGroupHandle, _layoutContainerLayoutLabelHandle, out int layoutType))
        {
            layoutType = 0;
        }

        if (layoutType == (int)LayoutType.Stack || layoutType == (int)LayoutType.Flex)
        {
            TryDrawGroupItem(component, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerDirectionLabelHandle);
            TryDrawGroupItem(component, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerPaddingLabelHandle);
            TryDrawGroupItem(component, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerSpacingLabelHandle);
            TryDrawGroupItem(component, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerAlignItemsLabelHandle);
            TryDrawGroupItem(component, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerJustifyLabelHandle);
            TryDrawGroupItem(component, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerWidthModeLabelHandle);
            TryDrawGroupItem(component, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerHeightModeLabelHandle);
            return;
        }

        if (layoutType == (int)LayoutType.Grid)
        {
            TryDrawGroupItem(component, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerGridColumnsLabelHandle);
            TryDrawGroupItem(component, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerPaddingLabelHandle);
            TryDrawGroupItem(component, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerSpacingLabelHandle);
            TryDrawGroupItem(component, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerAlignItemsLabelHandle);
            TryDrawGroupItem(component, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerJustifyLabelHandle);
            TryDrawGroupItem(component, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerWidthModeLabelHandle);
            TryDrawGroupItem(component, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerHeightModeLabelHandle);
            return;
        }

        InspectorHint.Draw("Unknown layout type.");
    }

    private void DrawLayoutListBindProperty(float labelWidth, float inputWidth)
    {
        if (_gizmoTargetEntity.IsNull)
        {
            return;
        }

        if (_workspace.World.HasComponent(_gizmoTargetEntity, PrefabExpandedComponent.Api.PoolIdConst))
        {
            // List binding is authored on prefab nodes, not expanded instances.
            return;
        }

        uint targetStableId = _workspace.World.GetStableId(_gizmoTargetEntity);
        if (targetStableId == 0)
        {
            return;
        }

        EntityId owningPrefab = _workspace.FindOwningPrefabEntity(_gizmoTargetEntity);
        if (owningPrefab.IsNull || _workspace.World.GetNodeType(owningPrefab) != UiNodeType.Prefab)
        {
            return;
        }

        uint owningPrefabStableId = _workspace.World.GetStableId(owningPrefab);

        ushort boundVariableId = 0;
        if (_workspace.World.TryGetComponent(owningPrefab, PrefabBindingsComponent.Api.PoolIdConst, out AnyComponentHandle bindingsAny) &&
            bindingsAny.IsValid)
        {
            var bindingsHandle = new PrefabBindingsComponentHandle(bindingsAny.Index, bindingsAny.Generation);
            var bindings = PrefabBindingsComponent.Api.FromHandle(_propertyWorld, bindingsHandle);
            if (bindings.IsAlive && bindings.BindingCount > 0)
            {
                const byte DirectionListBind = 2;

                ushort bindingCount = bindings.BindingCount;
                if (bindingCount > PrefabBindingsComponent.MaxBindings)
                {
                    bindingCount = PrefabBindingsComponent.MaxBindings;
                }

                ReadOnlySpan<byte> directions = bindings.DirectionReadOnlySpan();
                ReadOnlySpan<uint> targetNode = bindings.TargetSourceNodeStableIdReadOnlySpan();
                ReadOnlySpan<ushort> varId = bindings.VariableIdReadOnlySpan();

                for (int i = 0; i < bindingCount; i++)
                {
                    if (directions[i] == DirectionListBind && targetNode[i] == targetStableId)
                    {
                        boundVariableId = varId[i];
                        break;
                    }
                }
            }
        }

        string boundName = "(not bound)";
        bool boundMissing = false;
        bool boundTypeMissing = false;
        int boundItemCount = -1;

        if (_workspace.World.TryGetComponent(owningPrefab, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) &&
            varsAny.IsValid)
        {
            var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
            var vars = PrefabVariablesComponent.Api.FromHandle(_propertyWorld, varsHandle);
            if (vars.IsAlive && vars.VariableCount > 0)
            {
                ushort varCount = vars.VariableCount;
                if (varCount > PrefabVariablesComponent.MaxVariables)
                {
                    varCount = (ushort)PrefabVariablesComponent.MaxVariables;
                }

                ReadOnlySpan<ushort> ids = vars.VariableIdReadOnlySpan();
                ReadOnlySpan<StringHandle> names = vars.NameReadOnlySpan();
                ReadOnlySpan<int> kinds = vars.KindReadOnlySpan();
                ReadOnlySpan<PropertyValue> defaults = vars.DefaultValueReadOnlySpan();

                if (boundVariableId != 0)
                {
                    int varIndex = -1;
                    for (int i = 0; i < varCount; i++)
                    {
                        if (ids[i] == boundVariableId)
                        {
                            varIndex = i;
                            break;
                        }
                    }

                    if (varIndex >= 0 && (PropertyKind)kinds[varIndex] == PropertyKind.List)
                    {
                        boundName = names[varIndex].IsValid ? names[varIndex].ToString() : "List";
                        uint typeStableId = defaults[varIndex].UInt;
                        if (typeStableId == 0 || (owningPrefabStableId != 0 && typeStableId == owningPrefabStableId))
                        {
                            boundTypeMissing = true;
                        }
                        else
                        {
                            EntityId typePrefabEntity = _workspace.World.GetEntityByStableId(typeStableId);
                            boundTypeMissing = typePrefabEntity.IsNull || _workspace.World.GetNodeType(typePrefabEntity) != UiNodeType.Prefab;
                        }
                    }
                    else
                    {
                        boundMissing = true;
                    }
                }

                if (boundVariableId != 0 &&
                    _workspace.World.TryGetComponent(owningPrefab, PrefabListDataComponent.Api.PoolIdConst, out AnyComponentHandle listAny) &&
                    listAny.IsValid)
                {
                    var listHandle = new PrefabListDataComponentHandle(listAny.Index, listAny.Generation);
                    var list = PrefabListDataComponent.Api.FromHandle(_propertyWorld, listHandle);
                    if (list.IsAlive && list.EntryCount > 0)
                    {
                        ushort entryCount = list.EntryCount;
                        if (entryCount > PrefabListDataComponent.MaxListEntries)
                        {
                            entryCount = (ushort)PrefabListDataComponent.MaxListEntries;
                        }

                        ReadOnlySpan<ushort> entryVarId = list.EntryVariableIdReadOnlySpan();
                        ReadOnlySpan<ushort> entryItemCount = list.EntryItemCountReadOnlySpan();
                        for (int i = 0; i < entryCount; i++)
                        {
                            if (entryVarId[i] == boundVariableId)
                            {
                                boundItemCount = entryItemCount[i];
                                break;
                            }
                        }
                    }
                }
            }
        }

        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = GetPaddedRowRect(rect);

        float fontSize = Im.Style.FontSize;
        float textY = rect.Y + (rect.Height - fontSize) * 0.5f;
        Im.Text("List".AsSpan(), rect.X, textY, fontSize, Im.Style.TextPrimary);

        float inputX = rect.X + labelWidth;
        float contentWidth = Math.Max(1f, inputWidth - KeyIconWidth);
        var inputRect = new ImRect(inputX, rect.Y, contentWidth, rect.Height);

        string valueText = boundName;
        if (boundVariableId == 0)
        {
            valueText = "(not bound)";
        }
        else if (boundMissing)
        {
            valueText = "(missing)";
        }
        else if (boundTypeMissing)
        {
            valueText = boundName + " (type required)";
        }
        else if (boundItemCount >= 0)
        {
            valueText = boundName + " (" + boundItemCount.ToString() + " items)";
        }

        Im.Text(valueText.AsSpan(), inputRect.X + 6f, textY, fontSize, Im.Style.TextSecondary);

        bool hovered = inputRect.Contains(Im.MousePos);
        if (hovered && Im.Context.Input.MouseRightPressed)
        {
            ImContextMenu.OpenAt("layout_list_bind_menu", inputRect.X, inputRect.Bottom);
        }

        if (ImContextMenu.Begin("layout_list_bind_menu"))
        {
            ImContextMenu.ItemDisabled("List bind");
            ImContextMenu.Separator();

            bool renderedAny = false;
            if (_workspace.World.TryGetComponent(owningPrefab, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny2) &&
                varsAny2.IsValid)
            {
                var varsHandle2 = new PrefabVariablesComponentHandle(varsAny2.Index, varsAny2.Generation);
                var vars2 = PrefabVariablesComponent.Api.FromHandle(_propertyWorld, varsHandle2);
                if (vars2.IsAlive && vars2.VariableCount > 0)
                {
                    ushort varCount2 = vars2.VariableCount;
                    if (varCount2 > PrefabVariablesComponent.MaxVariables)
                    {
                        varCount2 = (ushort)PrefabVariablesComponent.MaxVariables;
                    }

                    ReadOnlySpan<ushort> ids2 = vars2.VariableIdReadOnlySpan();
                    ReadOnlySpan<StringHandle> names2 = vars2.NameReadOnlySpan();
                    ReadOnlySpan<int> kinds2 = vars2.KindReadOnlySpan();

                    for (int i = 0; i < varCount2; i++)
                    {
                        ushort id = ids2[i];
                        if (id == 0)
                        {
                            continue;
                        }

                        if ((PropertyKind)kinds2[i] != PropertyKind.List)
                        {
                            continue;
                        }

                        renderedAny = true;
                        string name = names2[i].IsValid ? names2[i].ToString() : "List";
                        bool selected = id == boundVariableId;
                        if (ImContextMenu.ItemCheckbox(name, ref selected))
                        {
                            _commands.SetPrefabListLayoutBinding(owningPrefab, targetStableId, id);
                        }
                    }
                }
            }

            if (!renderedAny)
            {
                ImContextMenu.ItemDisabled("(no list variables)");
            }

            if (boundVariableId != 0)
            {
                ImContextMenu.Separator();
                if (ImContextMenu.Item("Unbind"))
                {
                    _commands.SetPrefabListLayoutBinding(owningPrefab, targetStableId, variableId: 0);
                }
            }

            ImContextMenu.End();
        }
    }

    private void DrawLayoutChildAndConstraintGroupProperties(AnyComponentHandle component, float labelWidth, float inputWidth)
    {
        bool hasIgnoreLayout = TryGetBool(component, _layoutChildGroupHandle, _layoutChildIgnoreLayoutLabelHandle, out bool ignoreLayout);

        int mode = (hasIgnoreLayout && ignoreLayout) ? 1 : 0;
        if (DrawLayoutPositioningModeRow("Position", mode, labelWidth, inputWidth, out int nextMode))
        {
            bool nextIgnoreLayout = nextMode == 1;
            SetBool(component, _layoutChildGroupHandle, _layoutChildIgnoreLayoutLabelHandle, widgetId: Im.Context.GetId("layout_ignore_layout"), nextIgnoreLayout);
            ignoreLayout = nextIgnoreLayout;
        }

        if (!ignoreLayout)
        {
            TryDrawGroupItem(component, labelWidth, inputWidth, _layoutChildGroupHandle, _layoutChildMarginLabelHandle);
            TryDrawGroupItem(component, labelWidth, inputWidth, _layoutChildGroupHandle, _layoutChildAlignSelfLabelHandle);
            TryDrawGroupItem(component, labelWidth, inputWidth, _layoutChildGroupHandle, _layoutChildPreferredSizeLabelHandle);
            TryDrawGroupItem(component, labelWidth, inputWidth, _layoutChildGroupHandle, _layoutChildFlexGrowLabelHandle);
            TryDrawGroupItem(component, labelWidth, inputWidth, _layoutChildGroupHandle, _layoutChildFlexShrinkLabelHandle);
            return;
        }

        // Absolute positioning (opt-out of layout flow). The user can optionally enable anchor-based constraints.
        bool hasConstraintEnabled = TryGetBool(component, _layoutConstraintGroupHandle, _layoutConstraintEnabledLabelHandle, out bool constraintEnabled);
        bool anchorsEnabled = hasConstraintEnabled && constraintEnabled;

        if (DrawLayoutAnchorsEnabledRow("Anchors", anchorsEnabled, labelWidth, inputWidth, out bool nextAnchorsEnabled))
        {
            SetBool(component, _layoutConstraintGroupHandle, _layoutConstraintEnabledLabelHandle, widgetId: Im.Context.GetId("layout_constraint_enabled"), nextAnchorsEnabled);
            anchorsEnabled = nextAnchorsEnabled;
        }

        if (!anchorsEnabled)
        {
            return;
        }

        DrawLayoutAnchorPresets(component, labelWidth, inputWidth);

        TryDrawGroupItem(component, labelWidth, inputWidth, _layoutConstraintGroupHandle, _layoutConstraintAnchorMinLabelHandle);
        TryDrawGroupItem(component, labelWidth, inputWidth, _layoutConstraintGroupHandle, _layoutConstraintAnchorMaxLabelHandle);

        Vector2 anchorMin = Vector2.Zero;
        Vector2 anchorMax = Vector2.Zero;
        TryGetVec2(component, _layoutConstraintGroupHandle, _layoutConstraintAnchorMinLabelHandle, out anchorMin);
        TryGetVec2(component, _layoutConstraintGroupHandle, _layoutConstraintAnchorMaxLabelHandle, out anchorMax);
        bool isStretch = anchorMin.X != anchorMax.X || anchorMin.Y != anchorMax.Y;

        TryDrawGroupItem(component, labelWidth, inputWidth, _layoutConstraintGroupHandle, _layoutConstraintOffsetMinLabelHandle);
        if (isStretch)
        {
            TryDrawGroupItem(component, labelWidth, inputWidth, _layoutConstraintGroupHandle, _layoutConstraintOffsetMaxLabelHandle);
        }

        TryDrawGroupItem(component, labelWidth, inputWidth, _layoutConstraintGroupHandle, _layoutConstraintMinSizeLabelHandle);
        TryDrawGroupItem(component, labelWidth, inputWidth, _layoutConstraintGroupHandle, _layoutConstraintMaxSizeLabelHandle);
    }

    private void DrawLayoutConstraintGroupProperties(AnyComponentHandle component, float labelWidth, float inputWidth) { _ = component; _ = labelWidth; _ = inputWidth; }

    private void DrawLayoutContainerGroupPropertiesMulti(AnyComponentHandle component0, ReadOnlySpan<AnyComponentHandle> components, float labelWidth, float inputWidth)
    {
        TryDrawGroupItemMulti(component0, components, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerEnabledLabelHandle);
        if (!TryGetBoolMulti(component0, components, _layoutContainerGroupHandle, _layoutContainerEnabledLabelHandle, out _, out bool mixedEnabled, out bool anyEnabled) || !anyEnabled)
        {
            return;
        }

        TryDrawGroupItemMulti(component0, components, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerLayoutLabelHandle);
        if (!TryGetIntMulti(component0, components, _layoutContainerGroupHandle, _layoutContainerLayoutLabelHandle, out int layoutType, out bool mixedLayoutType))
        {
            layoutType = 0;
            mixedLayoutType = false;
        }

        if (mixedLayoutType)
        {
            return;
        }

        if (layoutType == (int)LayoutType.Stack || layoutType == (int)LayoutType.Flex)
        {
            TryDrawGroupItemMulti(component0, components, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerDirectionLabelHandle);
            TryDrawGroupItemMulti(component0, components, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerPaddingLabelHandle);
            TryDrawGroupItemMulti(component0, components, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerSpacingLabelHandle);
            TryDrawGroupItemMulti(component0, components, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerAlignItemsLabelHandle);
            TryDrawGroupItemMulti(component0, components, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerJustifyLabelHandle);
            TryDrawGroupItemMulti(component0, components, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerWidthModeLabelHandle);
            TryDrawGroupItemMulti(component0, components, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerHeightModeLabelHandle);
            return;
        }

        if (layoutType == (int)LayoutType.Grid)
        {
            TryDrawGroupItemMulti(component0, components, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerGridColumnsLabelHandle);
            TryDrawGroupItemMulti(component0, components, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerPaddingLabelHandle);
            TryDrawGroupItemMulti(component0, components, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerSpacingLabelHandle);
            TryDrawGroupItemMulti(component0, components, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerAlignItemsLabelHandle);
            TryDrawGroupItemMulti(component0, components, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerJustifyLabelHandle);
            TryDrawGroupItemMulti(component0, components, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerWidthModeLabelHandle);
            TryDrawGroupItemMulti(component0, components, labelWidth, inputWidth, _layoutContainerGroupHandle, _layoutContainerHeightModeLabelHandle);
            return;
        }

        InspectorHint.Draw("Unknown layout type.");
    }

    private void DrawLayoutChildAndConstraintGroupPropertiesMulti(AnyComponentHandle component0, ReadOnlySpan<AnyComponentHandle> components, float labelWidth, float inputWidth)
    {
        bool ignoreLayout = false;
        bool mixedIgnoreLayout = false;
        if (TryGetBoolMulti(component0, components, _layoutChildGroupHandle, _layoutChildIgnoreLayoutLabelHandle, out ignoreLayout, out mixedIgnoreLayout, out _))
        {
            int mode = mixedIgnoreLayout ? -1 : (ignoreLayout ? 1 : 0);
            if (DrawLayoutPositioningModeRow("Position", mode, labelWidth, inputWidth, out int nextMode))
            {
                bool nextIgnoreLayout = nextMode == 1;
                SetBoolMulti(component0, components, _layoutChildGroupHandle, _layoutChildIgnoreLayoutLabelHandle, widgetId: Im.Context.GetId("layout_ignore_layout_multi"), nextIgnoreLayout);
                ignoreLayout = nextIgnoreLayout;
                mixedIgnoreLayout = false;
            }
        }

        if (!mixedIgnoreLayout && !ignoreLayout)
        {
            TryDrawGroupItemMulti(component0, components, labelWidth, inputWidth, _layoutChildGroupHandle, _layoutChildMarginLabelHandle);
            TryDrawGroupItemMulti(component0, components, labelWidth, inputWidth, _layoutChildGroupHandle, _layoutChildAlignSelfLabelHandle);
            TryDrawGroupItemMulti(component0, components, labelWidth, inputWidth, _layoutChildGroupHandle, _layoutChildPreferredSizeLabelHandle);
            TryDrawGroupItemMulti(component0, components, labelWidth, inputWidth, _layoutChildGroupHandle, _layoutChildFlexGrowLabelHandle);
            TryDrawGroupItemMulti(component0, components, labelWidth, inputWidth, _layoutChildGroupHandle, _layoutChildFlexShrinkLabelHandle);
            return;
        }

        // Absolute positioning: expose anchor constraints when applicable, but keep it minimal in multi-select.
        bool anyEnabled = false;
        bool mixedEnabled = false;
        if (TryGetBoolMulti(component0, components, _layoutConstraintGroupHandle, _layoutConstraintEnabledLabelHandle, out _, out mixedEnabled, out anyEnabled))
        {
            bool anchorsEnabled = !mixedEnabled && anyEnabled;
            if (DrawLayoutAnchorsEnabledRow("Anchors", mixedEnabled ? (bool?)null : anchorsEnabled, labelWidth, inputWidth, out bool nextEnabled))
            {
                SetBoolMulti(component0, components, _layoutConstraintGroupHandle, _layoutConstraintEnabledLabelHandle, widgetId: Im.Context.GetId("layout_constraint_enabled_multi"), nextEnabled);
                anyEnabled = nextEnabled;
                mixedEnabled = false;
            }
        }
    }

    private void DrawLayoutConstraintGroupPropertiesMulti(AnyComponentHandle component0, ReadOnlySpan<AnyComponentHandle> components, float labelWidth, float inputWidth)
    {
        _ = component0;
        _ = components;
        _ = labelWidth;
        _ = inputWidth;
    }

    private bool DrawLayoutPositioningModeRow(string label, int selected, float labelWidth, float inputWidth, out int nextSelected)
    {
        nextSelected = selected;

        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = GetPaddedRowRect(rect);

        float fontSize = Im.Style.FontSize;
        float textY = rect.Y + (rect.Height - fontSize) * 0.5f;
        Im.Text(label.AsSpan(), rect.X, textY, fontSize, Im.Style.TextPrimary);

        float inputX = rect.X + labelWidth;
        float contentWidth = Math.Max(1f, inputWidth - KeyIconWidth);
        var inputRect = new ImRect(inputX, rect.Y, contentWidth, rect.Height);

        int baseWidgetId = Im.Context.GetId(label);
        var ctx = Im.Context;
        var input = ctx.Input;

        float segmentSpacing = Math.Max(1f, Im.Style.Spacing * 0.5f);
        float segmentWidth = (inputRect.Width - segmentSpacing) / 2f;
        if (segmentWidth < 1f)
        {
            segmentWidth = 1f;
            segmentSpacing = 0f;
        }

        bool changed = false;
        for (int i = 0; i < 2; i++)
        {
            float x = inputRect.X + i * (segmentWidth + segmentSpacing);
            var segRect = new ImRect(x, inputRect.Y, segmentWidth, inputRect.Height);
            int segId = baseWidgetId * 31 + i;

            bool segHovered = segRect.Contains(Im.MousePos);
            if (segHovered)
            {
                ctx.SetHot(segId);
            }

            if (ctx.IsHot(segId) && input.MousePressed)
            {
                ctx.SetActive(segId);
            }

            bool pressed = false;
            if (ctx.IsActive(segId))
            {
                if (input.MouseReleased)
                {
                    if (ctx.IsHot(segId))
                    {
                        pressed = true;
                    }
                    ctx.ClearActive();
                }
            }

            bool isSelected = selected == i;
            uint bg = isSelected ? Im.Style.Active : (ctx.IsActive(segId) ? Im.Style.Active : (ctx.IsHot(segId) ? Im.Style.Hover : Im.Style.Surface));
            uint border = isSelected ? Im.Style.Primary : Im.Style.Border;
            Im.DrawRoundedRect(segRect.X, segRect.Y, segRect.Width, segRect.Height, Im.Style.CornerRadius, bg);
            Im.DrawRoundedRectStroke(segRect.X, segRect.Y, segRect.Width, segRect.Height, Im.Style.CornerRadius, border, Im.Style.BorderWidth);

            uint textColor = isSelected ? Im.Style.TextPrimary : Im.Style.TextSecondary;
            float labelX = segRect.X + Im.Style.Padding;
            float labelY = segRect.Y + (segRect.Height - fontSize) * 0.5f;
            string option = i == 0 ? "Layout" : "Absolute";
            Im.Text(option.AsSpan(), labelX, labelY, fontSize, textColor);

            if (pressed && selected != i)
            {
                nextSelected = i;
                changed = true;
            }
        }

        return changed;
    }

    private bool DrawLayoutAnchorsEnabledRow(string label, bool enabled, float labelWidth, float inputWidth, out bool nextEnabled)
    {
        nextEnabled = enabled;
        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = GetPaddedRowRect(rect);

        float fontSize = Im.Style.FontSize;
        float textY = rect.Y + (rect.Height - fontSize) * 0.5f;
        Im.Text(label.AsSpan(), rect.X, textY, fontSize, Im.Style.TextPrimary);

        float inputX = rect.X + labelWidth;
        float contentWidth = Math.Max(1f, inputWidth - KeyIconWidth);
        var inputRect = new ImRect(inputX, rect.Y, contentWidth, rect.Height);

        bool value = enabled;
        bool changed = Im.Checkbox("layout_anchors_enabled", ref value, inputRect.X, inputRect.Y);
        if (changed)
        {
            nextEnabled = value;
        }

        return changed;
    }

    private bool DrawLayoutAnchorsEnabledRow(string label, bool? enabled, float labelWidth, float inputWidth, out bool nextEnabled)
    {
        bool current = enabled ?? false;
        bool mixed = !enabled.HasValue;

        nextEnabled = current;
        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = GetPaddedRowRect(rect);

        float fontSize = Im.Style.FontSize;
        float textY = rect.Y + (rect.Height - fontSize) * 0.5f;
        Im.Text(label.AsSpan(), rect.X, textY, fontSize, Im.Style.TextPrimary);

        float inputX = rect.X + labelWidth;
        float contentWidth = Math.Max(1f, inputWidth - KeyIconWidth);
        var inputRect = new ImRect(inputX, rect.Y, contentWidth, rect.Height);

        bool value = current;
        bool changed = mixed
            ? Im.CheckboxTriState("layout_anchors_enabled_multi", ref value, mixed: true, inputRect.X, inputRect.Y)
            : Im.Checkbox("layout_anchors_enabled_multi", ref value, inputRect.X, inputRect.Y);
        if (changed)
        {
            nextEnabled = value;
        }

        return changed;
    }

    private void DrawLayoutAnchorPresets(AnyComponentHandle component, float labelWidth, float inputWidth)
    {
        // Presets only work with a concrete entity (so we can preserve offsets in parent-space).
        EntityId targetEntity = _gizmoTargetEntity;
        if (targetEntity.IsNull)
        {
            return;
        }

        if (!TryGetVec2(component, _layoutConstraintGroupHandle, _layoutConstraintAnchorMinLabelHandle, out Vector2 anchorMin) ||
            !TryGetVec2(component, _layoutConstraintGroupHandle, _layoutConstraintAnchorMaxLabelHandle, out Vector2 anchorMax))
        {
            return;
        }

        bool isStretch = anchorMin.X != anchorMax.X || anchorMin.Y != anchorMax.Y;
        if (isStretch)
        {
            // TODO: Support converting stretch anchors without changing layout semantics.
            return;
        }

        Vector2 anchorCenter = (anchorMin + anchorMax) * 0.5f;
        int selX = QuantizeAnchor01(anchorCenter.X);
        int selY = QuantizeAnchor01(anchorCenter.Y);

        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight * 3f + Im.Style.Spacing * 2f);
        rect = GetPaddedRowRect(rect);

        float fontSize = Im.Style.FontSize;
        float textY = rect.Y + (Im.Style.MinButtonHeight - fontSize) * 0.5f;
        Im.Text("Preset".AsSpan(), rect.X, textY, fontSize, Im.Style.TextPrimary);

        float inputX = rect.X + labelWidth;
        float contentWidth = Math.Max(1f, inputWidth - KeyIconWidth);
        var gridRect = new ImRect(inputX, rect.Y, contentWidth, rect.Height);

        float cellGap = Math.Max(1f, Im.Style.Spacing * 0.5f);
        float cellW = (gridRect.Width - cellGap * 2f) / 3f;
        float cellH = (gridRect.Height - cellGap * 2f) / 3f;
        if (cellW < 1f) { cellW = 1f; cellGap = 0f; }
        if (cellH < 1f) { cellH = 1f; cellGap = 0f; }

        var ctx = Im.Context;
        var input = ctx.Input;
        int baseWidgetId = ctx.GetId("layout_anchor_preset");

        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                float cx = gridRect.X + x * (cellW + cellGap);
                float cy = gridRect.Y + y * (cellH + cellGap);
                var cellRect = new ImRect(cx, cy, cellW, cellH);

                int cellIndex = y * 3 + x;
                int id = baseWidgetId * 31 + cellIndex;

                bool hovered = cellRect.Contains(Im.MousePos);
                if (hovered)
                {
                    ctx.SetHot(id);
                }

                if (ctx.IsHot(id) && input.MousePressed)
                {
                    ctx.SetActive(id);
                }

                bool pressed = false;
                if (ctx.IsActive(id))
                {
                    if (input.MouseReleased)
                    {
                        if (ctx.IsHot(id))
                        {
                            pressed = true;
                        }
                        ctx.ClearActive();
                    }
                }

                bool isSelected = selX == x && selY == y;
                uint bg = isSelected ? Im.Style.Active : (ctx.IsActive(id) ? Im.Style.Active : (ctx.IsHot(id) ? Im.Style.Hover : Im.Style.Surface));
                uint border = isSelected ? Im.Style.Primary : Im.Style.Border;
                Im.DrawRoundedRect(cellRect.X, cellRect.Y, cellRect.Width, cellRect.Height, Im.Style.CornerRadius, bg);
                Im.DrawRoundedRectStroke(cellRect.X, cellRect.Y, cellRect.Width, cellRect.Height, Im.Style.CornerRadius, border, Im.Style.BorderWidth);

                if (pressed)
                {
                    Vector2 nextAnchor = new Vector2(AnchorIndexTo01(x), AnchorIndexTo01(y));
                    ApplyAnchorPresetMaintainParentSpace(targetEntity, component, nextAnchor);
                    selX = x;
                    selY = y;
                }
            }
        }
    }

    private static int QuantizeAnchor01(float v)
    {
        if (v < 0.25f) return 0;
        if (v > 0.75f) return 2;
        return 1;
    }

    private static float AnchorIndexTo01(int index)
    {
        return index switch
        {
            0 => 0f,
            2 => 1f,
            _ => 0.5f
        };
    }

    private void ApplyAnchorPresetMaintainParentSpace(EntityId targetEntity, AnyComponentHandle component, Vector2 anchor01)
    {
        if (!TryGetSlot(component, _layoutConstraintGroupHandle, _layoutConstraintAnchorMinLabelHandle, out PropertySlot anchorMinSlot) ||
            !TryGetSlot(component, _layoutConstraintGroupHandle, _layoutConstraintAnchorMaxLabelHandle, out PropertySlot anchorMaxSlot) ||
            !TryGetSlot(component, _layoutConstraintGroupHandle, _layoutConstraintOffsetMinLabelHandle, out PropertySlot offsetMinSlot))
        {
            return;
        }

        Vector2 anchorMin = PropertyDispatcher.ReadVec2(_propertyWorld, anchorMinSlot);
        Vector2 anchorMax = PropertyDispatcher.ReadVec2(_propertyWorld, anchorMaxSlot);
        Vector2 offsetMin = PropertyDispatcher.ReadVec2(_propertyWorld, offsetMinSlot);
        Vector2 offsetMax = Vector2.Zero;
        bool isStretch = anchorMin.X != anchorMax.X || anchorMin.Y != anchorMax.Y;
        PropertySlot offsetMaxSlot = default;
        if (isStretch && TryGetSlot(component, _layoutConstraintGroupHandle, _layoutConstraintOffsetMaxLabelHandle, out offsetMaxSlot))
        {
            offsetMax = PropertyDispatcher.ReadVec2(_propertyWorld, offsetMaxSlot);
        }

        if (!TryComputeConstraintParentRectLocal(targetEntity, out ImRect parentRectLocal))
        {
            // Fallback: just set anchors.
            int widgetId = Im.Context.GetId("layout_anchor_preset_apply");
            _commands.SetPropertyValue(widgetId, isEditing: false, anchorMinSlot, PropertyValue.FromVec2(anchor01));
            _commands.SetPropertyValue(widgetId, isEditing: false, anchorMaxSlot, PropertyValue.FromVec2(anchor01));
            return;
        }

        Vector2 parentMin = new Vector2(parentRectLocal.X, parentRectLocal.Y);
        Vector2 parentSize = new Vector2(parentRectLocal.Width, parentRectLocal.Height);
        parentSize = new Vector2(parentSize.X == 0f ? 1f : parentSize.X, parentSize.Y == 0f ? 1f : parentSize.Y);

        int widgetBaseId = Im.Context.GetId("layout_anchor_preset_apply");

        if (isStretch)
        {
            Vector2 anchoredMinStart = parentMin + new Vector2(parentSize.X * anchorMin.X, parentSize.Y * anchorMin.Y);
            Vector2 anchoredMaxStart = parentMin + new Vector2(parentSize.X * anchorMax.X, parentSize.Y * anchorMax.Y);
            Vector2 rectMin = anchoredMinStart + offsetMin;
            Vector2 rectMax = anchoredMaxStart + offsetMax;

            Vector2 newAnchorMin = anchor01;
            Vector2 newAnchorMax = anchor01;

            Vector2 anchoredMinNew = parentMin + new Vector2(parentSize.X * newAnchorMin.X, parentSize.Y * newAnchorMin.Y);
            Vector2 anchoredMaxNew = parentMin + new Vector2(parentSize.X * newAnchorMax.X, parentSize.Y * newAnchorMax.Y);

            Vector2 newOffsetMin = rectMin - anchoredMinNew;
            Vector2 newOffsetMax = rectMax - anchoredMaxNew;

            _commands.SetPropertyValue(widgetBaseId, isEditing: false, anchorMinSlot, PropertyValue.FromVec2(newAnchorMin));
            _commands.SetPropertyValue(widgetBaseId, isEditing: false, anchorMaxSlot, PropertyValue.FromVec2(newAnchorMax));
            _commands.SetPropertyValue(widgetBaseId, isEditing: false, offsetMinSlot, PropertyValue.FromVec2(newOffsetMin));
            if (offsetMaxSlot.PropertyId != 0)
            {
                _commands.SetPropertyValue(widgetBaseId, isEditing: false, offsetMaxSlot, PropertyValue.FromVec2(newOffsetMax));
            }
            return;
        }

        Vector2 anchorPointStart = parentMin + new Vector2(parentSize.X * anchorMin.X, parentSize.Y * anchorMin.Y);
        Vector2 pivotPos = anchorPointStart + offsetMin;

        Vector2 newAnchorPoint = parentMin + new Vector2(parentSize.X * anchor01.X, parentSize.Y * anchor01.Y);
        Vector2 newOffset = pivotPos - newAnchorPoint;

        _commands.SetPropertyValue(widgetBaseId, isEditing: false, anchorMinSlot, PropertyValue.FromVec2(anchor01));
        _commands.SetPropertyValue(widgetBaseId, isEditing: false, anchorMaxSlot, PropertyValue.FromVec2(anchor01));
        _commands.SetPropertyValue(widgetBaseId, isEditing: false, offsetMinSlot, PropertyValue.FromVec2(newOffset));
    }

    private bool TryComputeConstraintParentRectLocal(EntityId childEntity, out ImRect parentRectLocal)
    {
        parentRectLocal = default;
        if (childEntity.IsNull)
        {
            return false;
        }

        EntityId parent = _workspace.World.GetParent(childEntity);
        if (parent.IsNull)
        {
            return false;
        }

        if (!_workspace.TryGetLayoutIntrinsicSize(parent, out Vector2 parentSize, out Vector2 parentPivot01))
        {
            return false;
        }

        parentRectLocal = RectTransformMath.GetRectFromPivot(Vector2.Zero, parentPivot01, parentSize);
        return parentRectLocal.Width > 0f && parentRectLocal.Height > 0f;
    }

    private void SetBool(AnyComponentHandle component, StringHandle group, StringHandle name, int widgetId, bool value)
    {
        if (!TryGetSlot(component, group, name, out PropertySlot slot) || slot.Kind != PropertyKind.Bool)
        {
            return;
        }

        _commands.SetPropertyValue(widgetId, isEditing: false, slot, PropertyValue.FromBool(value));
    }

    private void SetBoolMulti(AnyComponentHandle component0, ReadOnlySpan<AnyComponentHandle> components, StringHandle group, StringHandle name, int widgetId, bool value)
    {
        if (!TryGetSlot(component0, group, name, out PropertySlot slot0) || slot0.Kind != PropertyKind.Bool)
        {
            return;
        }

        _commands.SetPropertyValue(widgetId, isEditing: false, slot0, PropertyValue.FromBool(value));
        for (int i = 1; i < components.Length; i++)
        {
            if (!TryResolveSlotForMulti(components[i], slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out PropertySlot slot))
            {
                continue;
            }
            _commands.SetPropertyValue(widgetId, isEditing: false, slot, PropertyValue.FromBool(value));
        }
    }

    private bool TryDrawGroupItem(AnyComponentHandle component, float labelWidth, float inputWidth, StringHandle group, StringHandle name)
    {
        for (int itemIndex = 0; itemIndex < _propertyUiItems.Count; itemIndex++)
        {
            PropertyUiItem item = _propertyUiItems[itemIndex];
            if (item.Group != group || item.Info.Name != name)
            {
                continue;
            }

            if (item.Kind == PropertyUiKind.Single)
            {
                DrawSingleProperty(component, item, labelWidth, inputWidth);
            }
            else
            {
                DrawVectorGroup(component, item, labelWidth, inputWidth);
            }

            return true;
        }

        return false;
    }

    private bool TryDrawGroupItemMulti(AnyComponentHandle component0, ReadOnlySpan<AnyComponentHandle> components, float labelWidth, float inputWidth, StringHandle group, StringHandle name)
    {
        for (int itemIndex = 0; itemIndex < _propertyUiItems.Count; itemIndex++)
        {
            PropertyUiItem item = _propertyUiItems[itemIndex];
            if (item.Group != group || item.Info.Name != name)
            {
                continue;
            }

            if (item.Kind == PropertyUiKind.Single)
            {
                DrawSinglePropertyMulti(component0, components, item, labelWidth, inputWidth);
            }
            else
            {
                DrawVectorGroupMulti(component0, components, item, labelWidth, inputWidth);
            }

            return true;
        }

        return false;
    }

    private bool TryGetBool(AnyComponentHandle component, StringHandle group, StringHandle name, out bool value)
    {
        value = default;
        if (!TryGetSlot(component, group, name, out PropertySlot slot))
        {
            return false;
        }

        if (slot.Kind != PropertyKind.Bool)
        {
            return false;
        }

        value = PropertyDispatcher.ReadBool(_propertyWorld, slot);
        return true;
    }

    private bool TryGetBoolMulti(AnyComponentHandle component0, ReadOnlySpan<AnyComponentHandle> components, StringHandle group, StringHandle name, out bool value, out bool mixed, out bool anyTrue)
    {
        value = default;
        mixed = false;
        anyTrue = false;

        if (!TryGetSlot(component0, group, name, out PropertySlot slot0))
        {
            return false;
        }

        bool first = PropertyDispatcher.ReadBool(_propertyWorld, slot0);
        value = first;
        anyTrue = first;

        for (int i = 1; i < components.Length; i++)
        {
            if (!TryResolveSlotForMulti(components[i], slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out PropertySlot slot))
            {
                return false;
            }

            bool current = PropertyDispatcher.ReadBool(_propertyWorld, slot);
            anyTrue |= current;
            mixed |= current != first;
        }

        return true;
    }

    private bool TryGetInt(AnyComponentHandle component, StringHandle group, StringHandle name, out int value)
    {
        value = default;
        if (!TryGetSlot(component, group, name, out PropertySlot slot))
        {
            return false;
        }

        if (slot.Kind != PropertyKind.Int)
        {
            return false;
        }

        value = PropertyDispatcher.ReadInt(_propertyWorld, slot);
        return true;
    }

    private bool TryGetIntMulti(AnyComponentHandle component0, ReadOnlySpan<AnyComponentHandle> components, StringHandle group, StringHandle name, out int value, out bool mixed)
    {
        value = default;
        mixed = false;

        if (!TryGetSlot(component0, group, name, out PropertySlot slot0))
        {
            return false;
        }

        int first = PropertyDispatcher.ReadInt(_propertyWorld, slot0);
        value = first;

        for (int i = 1; i < components.Length; i++)
        {
            if (!TryResolveSlotForMulti(components[i], slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out PropertySlot slot))
            {
                return false;
            }

            int current = PropertyDispatcher.ReadInt(_propertyWorld, slot);
            mixed |= current != first;
        }

        return true;
    }

    private bool TryGetVec2(AnyComponentHandle component, StringHandle group, StringHandle name, out Vector2 value)
    {
        value = default;
        if (!TryGetSlot(component, group, name, out PropertySlot slot))
        {
            return false;
        }

        if (slot.Kind != PropertyKind.Vec2)
        {
            return false;
        }

        value = PropertyDispatcher.ReadVec2(_propertyWorld, slot);
        return true;
    }

    private bool TryGetVec2Multi(AnyComponentHandle component0, ReadOnlySpan<AnyComponentHandle> components, StringHandle group, StringHandle name, out Vector2 value, out bool mixed)
    {
        value = default;
        mixed = false;

        if (!TryGetSlot(component0, group, name, out PropertySlot slot0))
        {
            return false;
        }

        Vector2 first = PropertyDispatcher.ReadVec2(_propertyWorld, slot0);
        value = first;

        for (int i = 1; i < components.Length; i++)
        {
            if (!TryResolveSlotForMulti(components[i], slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out PropertySlot slot))
            {
                return false;
            }

            Vector2 current = PropertyDispatcher.ReadVec2(_propertyWorld, slot);
            mixed |= current != first;
        }

        return true;
    }

    private bool TryGetSlot(AnyComponentHandle component, StringHandle group, StringHandle name, out PropertySlot slot)
    {
        slot = default;
        for (int itemIndex = 0; itemIndex < _propertyUiItems.Count; itemIndex++)
        {
            PropertyUiItem item = _propertyUiItems[itemIndex];
            if (item.Group != group || item.Info.Name != name)
            {
                continue;
            }

            slot = PropertyDispatcher.GetSlot(component, item.Index0);
            return true;
        }

        return false;
    }

    private static bool TryResolveSlotForMulti(AnyComponentHandle component, ushort propertyIndexHint, ulong propertyId, PropertyKind kind, out PropertySlot slot)
    {
        slot = default;
        if (component.IsNull)
        {
            return false;
        }

        int propertyCount = PropertyDispatcher.GetPropertyCount(component);
        if (propertyCount <= 0)
        {
            return false;
        }

        if (propertyIndexHint < propertyCount)
        {
            if (PropertyDispatcher.TryGetInfo(component, propertyIndexHint, out var hintInfo) &&
                hintInfo.PropertyId == propertyId &&
                hintInfo.Kind == kind)
            {
                slot = PropertyDispatcher.GetSlot(component, propertyIndexHint);
                return true;
            }
        }

        for (int propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
        {
            if (!PropertyDispatcher.TryGetInfo(component, propertyIndex, out var info))
            {
                continue;
            }
            if (info.PropertyId != propertyId || info.Kind != kind)
            {
                continue;
            }

            slot = PropertyDispatcher.GetSlot(component, propertyIndex);
            return true;
        }

        return false;
    }

    private void GetPropertyTableLayout(AnyComponentHandle component, int propertyCount, out float labelWidth, out float inputWidth)
    {
        float totalWidth = ImLayout.RemainingWidth() - InspectorRowPaddingX * 2f;
        if (totalWidth <= 0f)
        {
            totalWidth = 220f;
        }

        ulong signature = ComputePropertySignature(component, propertyCount);
        if (_propertyTableSignature == signature && _propertyTableTotalWidth.Equals(totalWidth))
        {
            labelWidth = _propertyTableLabelWidth;
            inputWidth = _propertyTableInputWidth;
            return;
        }

        int maxLabelLength = 0;
        for (int i = 0; i < _propertyUiItems.Count; i++)
        {
            int len = _propertyUiItems[i].Label.Length;
            if (len > maxLabelLength)
            {
                maxLabelLength = len;
            }
        }

        const float columnGap = 4f;
        float textWidth = maxLabelLength * Im.Style.FontSize * 0.6f;
        float maxLabelWidth = Math.Max(20f, totalWidth * 0.40f);
        float minLabelWidth = Math.Min(70f, maxLabelWidth);
        labelWidth = Math.Clamp(textWidth + columnGap, minLabelWidth, maxLabelWidth);
        inputWidth = Math.Max(100f, totalWidth - labelWidth);

        _propertyTableTotalWidth = totalWidth;
        _propertyTableLabelWidth = labelWidth;
        _propertyTableInputWidth = inputWidth;
        _propertyTableSignature = signature;
    }

    private void EnsureMultiScratchCapacity(int capacity)
    {
        if (_multiSlotsScratch.Length < capacity)
        {
            Array.Resize(ref _multiSlotsScratch, Math.Max(capacity, _multiSlotsScratch.Length * 2));
        }
        if (_multiValuesScratch.Length < capacity)
        {
            Array.Resize(ref _multiValuesScratch, Math.Max(capacity, _multiValuesScratch.Length * 2));
        }
    }

    private void EnsurePropertyUiCache(AnyComponentHandle component, int propertyCount)
    {
        ulong signature = ComputePropertySignature(component, propertyCount);
        if (_propertyUiPropertyCount == propertyCount && _propertyUiSignature == signature && _propertyUiItems.Count > 0)
        {
            return;
        }

        _propertyUiItems.Clear();

        for (int propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
        {
            if (!PropertyDispatcher.TryGetInfo(component, propertyIndex, out var info))
            {
                continue;
            }

            if ((info.Flags & PropertyFlags.Hidden) != 0)
            {
                continue;
            }

            if (TryBuildVectorGroup(component, propertyCount, ref propertyIndex, info, out var vectorItem))
            {
                _propertyUiItems.Add(vectorItem);
                continue;
            }

            var item = new PropertyUiItem
            {
                Kind = PropertyUiKind.Single,
                Index0 = (ushort)propertyIndex,
                Info = info,
                Group = info.Group,
                Label = info.Name.ToString(),
                WidgetId = BuildWidgetId(info.PropertyId),
                WidgetId2 = null
            };
            _propertyUiItems.Add(item);
        }

        if (component.Kind == StrokeComponent.Api.PoolIdConst)
        {
            _propertyUiItems.Sort(_strokePropertyComparer);
        }

        _propertyUiPropertyCount = propertyCount;
        _propertyUiSignature = signature;
    }

    private void DrawTransformGroupProperties(AnyComponentHandle component, float labelWidth, float inputWidth)
    {
        TryDrawTransformGroupItem(component, labelWidth, inputWidth, _positionLabelHandle);
        TryDrawTransformGroupItem(component, labelWidth, inputWidth, _scaleLabelHandle);
        TryDrawTransformGroupItem(component, labelWidth, inputWidth, _anchorLabelHandle);
        TryDrawTransformGroupItem(component, labelWidth, inputWidth, _rotationLabelHandle);
        TryDrawTransformGroupItem(component, labelWidth, inputWidth, _depthLabelHandle);

        for (int itemIndex = 0; itemIndex < _propertyUiItems.Count; itemIndex++)
        {
            PropertyUiItem item = _propertyUiItems[itemIndex];
            if (item.Group != _transformGroupHandle)
            {
                continue;
            }

            if (item.Info.Name == _positionLabelHandle ||
                item.Info.Name == _scaleLabelHandle ||
                item.Info.Name == _anchorLabelHandle ||
                item.Info.Name == _rotationLabelHandle ||
                item.Info.Name == _depthLabelHandle)
            {
                continue;
            }

            if (item.Kind == PropertyUiKind.Single)
            {
                DrawSingleProperty(component, item, labelWidth, inputWidth);
            }
            else
            {
                DrawVectorGroup(component, item, labelWidth, inputWidth);
            }
        }
    }

    private void TryDrawTransformGroupItem(AnyComponentHandle component, float labelWidth, float inputWidth, StringHandle name)
    {
        for (int itemIndex = 0; itemIndex < _propertyUiItems.Count; itemIndex++)
        {
            PropertyUiItem item = _propertyUiItems[itemIndex];
            if (item.Group != _transformGroupHandle || item.Info.Name != name)
            {
                continue;
            }

            if (item.Kind == PropertyUiKind.Single)
            {
                DrawSingleProperty(component, item, labelWidth, inputWidth);
            }
            else
            {
                DrawVectorGroup(component, item, labelWidth, inputWidth);
            }
            return;
        }
    }

    private bool TryGetTransformPositionSlots(AnyComponentHandle component, out PropertySlot xSlot, out PropertySlot ySlot)
    {
        xSlot = default;
        ySlot = default;

        for (int itemIndex = 0; itemIndex < _propertyUiItems.Count; itemIndex++)
        {
            PropertyUiItem item = _propertyUiItems[itemIndex];
            if (item.Group != _transformGroupHandle || item.Info.Name != _positionLabelHandle || item.Kind != PropertyUiKind.Vector2)
            {
                continue;
            }

            xSlot = PropertyDispatcher.GetSlot(component, item.Index0);
            ySlot = PropertyDispatcher.GetSlot(component, item.Index1);
            return true;
        }

        return false;
    }

    private bool TryGetTransformPositionSlotVec2(AnyComponentHandle component, out PropertySlot slot)
    {
        slot = default;

        for (int itemIndex = 0; itemIndex < _propertyUiItems.Count; itemIndex++)
        {
            PropertyUiItem item = _propertyUiItems[itemIndex];
            if (item.Group != _transformGroupHandle || item.Info.Name != _positionLabelHandle || item.Kind != PropertyUiKind.Single || item.Info.Kind != PropertyKind.Vec2)
            {
                continue;
            }

            slot = PropertyDispatcher.GetSlot(component, item.Index0);
            return true;
        }

        return false;
    }

    private static ulong ComputePropertySignature(AnyComponentHandle component, int propertyCount)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offsetBasis;

        for (int propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
        {
            if (!PropertyDispatcher.TryGetInfo(component, propertyIndex, out var info))
            {
                continue;
            }

            hash ^= info.PropertyId;
            hash *= prime;
            hash ^= (uint)info.Kind;
            hash *= prime;
        }

        hash ^= (uint)propertyCount;
        hash *= prime;
        return hash;
    }

    private bool TryBuildVectorGroup(
        AnyComponentHandle component,
        int propertyCount,
        ref int propertyIndex,
        PropertyInfo info,
        out PropertyUiItem item)
    {
        item = default;
        if (info.Kind != PropertyKind.Float)
        {
            return false;
        }

        string label = info.Name.ToString();
        if (!label.EndsWith(" X", StringComparison.Ordinal))
        {
            return false;
        }

        string baseLabel = label.Substring(0, label.Length - 2);
        if (!TryMatchVectorComponent(component, propertyCount, propertyIndex + 1, baseLabel, " Y", out _))
        {
            return false;
        }

        int vectorSize = 2;
        ushort index1 = (ushort)(propertyIndex + 1);
        ushort index2 = 0;
        ushort index3 = 0;

        if (TryMatchVectorComponent(component, propertyCount, propertyIndex + 2, baseLabel, " Z", out _))
        {
            vectorSize = 3;
            index2 = (ushort)(propertyIndex + 2);

            if (TryMatchVectorComponent(component, propertyCount, propertyIndex + 3, baseLabel, " W", out _))
            {
                vectorSize = 4;
                index3 = (ushort)(propertyIndex + 3);
            }
        }

        item = new PropertyUiItem
        {
            Kind = vectorSize == 4 ? PropertyUiKind.Vector4 :
                   vectorSize == 3 ? PropertyUiKind.Vector3 :
                   PropertyUiKind.Vector2,
            Index0 = (ushort)propertyIndex,
            Index1 = index1,
            Index2 = index2,
            Index3 = index3,
            Info = info,
            Group = info.Group,
            Label = baseLabel,
            WidgetId = BuildWidgetId(info.PropertyId),
            WidgetId2 = null
        };

        propertyIndex += vectorSize - 1;
        return true;
    }

    private bool TryMatchVectorComponent(
        AnyComponentHandle component,
        int propertyCount,
        int propertyIndex,
        string baseLabel,
        string suffix,
        out PropertyInfo info)
    {
        info = default;
        if (propertyIndex >= propertyCount)
        {
            return false;
        }

        if (!PropertyDispatcher.TryGetInfo(component, propertyIndex, out info))
        {
            return false;
        }

        if (info.Kind != PropertyKind.Float)
        {
            return false;
        }

        string label = info.Name.ToString();
        return label == baseLabel + suffix;
    }

    private static bool IsVectorEditing(int parentWidgetId, int componentCount)
    {
        var ctx = Im.Context;
        for (int componentIndex = 0; componentIndex < componentCount; componentIndex++)
        {
            int id = parentWidgetId * 31 + componentIndex;
            if (ctx.IsActive(id) || ctx.IsFocused(id))
            {
                return true;
            }
        }

        return false;
    }

    private void DrawVectorGroupMulti(AnyComponentHandle component0, ReadOnlySpan<AnyComponentHandle> components, PropertyUiItem item, float labelWidth, float inputWidth)
    {
        float minValue = float.IsNaN(item.Info.Min) ? float.MinValue : item.Info.Min;
        float maxValue = float.IsNaN(item.Info.Max) ? float.MaxValue : item.Info.Max;

        int componentCount = item.Kind switch
        {
            PropertyUiKind.Vector2 => 2,
            PropertyUiKind.Vector3 => 3,
            PropertyUiKind.Vector4 => 4,
            _ => 1
        };
        if (componentCount <= 1)
        {
            return;
        }

        PropertySlot slot0 = PropertyDispatcher.GetSlot(component0, item.Index0);
        PropertySlot slot1 = PropertyDispatcher.GetSlot(component0, item.Index1);
        PropertySlot slot2 = componentCount >= 3 ? PropertyDispatcher.GetSlot(component0, item.Index2) : default;
        PropertySlot slot3 = componentCount >= 4 ? PropertyDispatcher.GetSlot(component0, item.Index3) : default;

        float baseX = PropertyDispatcher.ReadFloat(_propertyWorld, slot0);
        float baseY = PropertyDispatcher.ReadFloat(_propertyWorld, slot1);
        float baseZ = componentCount >= 3 ? PropertyDispatcher.ReadFloat(_propertyWorld, slot2) : 0f;
        float baseW = componentCount >= 4 ? PropertyDispatcher.ReadFloat(_propertyWorld, slot3) : 0f;

	        uint mixedMask = 0;
	        for (int i = 1; i < components.Length; i++)
	        {
	            AnyComponentHandle component = components[i];
	            PropertySlot other0;
	            PropertySlot other1;
	            PropertySlot other2 = default;
	            PropertySlot other3 = default;
	            if (!TryResolveSlotForMulti(component, slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out other0) ||
	                !TryResolveSlotForMulti(component, slot1.PropertyIndex, slot1.PropertyId, slot1.Kind, out other1) ||
	                (componentCount >= 3 && !TryResolveSlotForMulti(component, slot2.PropertyIndex, slot2.PropertyId, slot2.Kind, out other2)) ||
	                (componentCount >= 4 && !TryResolveSlotForMulti(component, slot3.PropertyIndex, slot3.PropertyId, slot3.Kind, out other3)))
	            {
	                return;
	            }

            float x = PropertyDispatcher.ReadFloat(_propertyWorld, other0);
            float y = PropertyDispatcher.ReadFloat(_propertyWorld, other1);
            float z = componentCount >= 3 ? PropertyDispatcher.ReadFloat(_propertyWorld, other2) : 0f;
            float w = componentCount >= 4 ? PropertyDispatcher.ReadFloat(_propertyWorld, other3) : 0f;

            if (x != baseX) { mixedMask |= 1u; }
            if (y != baseY) { mixedMask |= 2u; }
            if (componentCount >= 3 && z != baseZ) { mixedMask |= 4u; }
            if (componentCount >= 4 && w != baseW) { mixedMask |= 8u; }
        }

        if ((item.Info.Flags & PropertyFlags.ReadOnly) != 0)
        {
            DrawReadOnlyVectorGroupRow(item, labelWidth, inputWidth, componentCount, baseX, baseY, baseZ, baseW, mixedMask);
            _commands.NotifyPropertyWidgetState(Im.Context.GetId(item.WidgetId), isEditing: false);
            return;
        }

        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = GetPaddedRowRect(rect);
        int widgetId = Im.Context.GetId(item.WidgetId);

        bool changed;
        uint changedMask = 0;

        if (componentCount == 2)
        {
            var vector = new Vector2(baseX, baseY);
            Vector2 before = vector;
            changed = ImVectorInput.DrawAtMixed(item.Label, item.WidgetId, rect.X, rect.Y, labelWidth, inputWidth, rightOverlayWidth: 0f, ref vector, mixedMask, minValue, maxValue, "F2");
            if (vector.X != before.X) { changedMask |= 1u; }
            if (vector.Y != before.Y) { changedMask |= 2u; }

            if (changed && changedMask != 0)
            {
                int totalSlots = components.Length * 2;
                EnsureMultiScratchCapacity(totalSlots);

                for (int entityIndex = 0; entityIndex < components.Length; entityIndex++)
                {
                    AnyComponentHandle component = components[entityIndex];
                    PropertySlot xSlot = entityIndex == 0 ? slot0 : default;
                    PropertySlot ySlot = entityIndex == 0 ? slot1 : default;
                    if (entityIndex != 0)
                    {
                        if (!TryResolveSlotForMulti(component, slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out xSlot) ||
                            !TryResolveSlotForMulti(component, slot1.PropertyIndex, slot1.PropertyId, slot1.Kind, out ySlot))
                        {
                            return;
                        }
                    }

                    float currentX = PropertyDispatcher.ReadFloat(_propertyWorld, xSlot);
                    float currentY = PropertyDispatcher.ReadFloat(_propertyWorld, ySlot);
                    float afterX = (changedMask & 1u) != 0u ? vector.X : currentX;
                    float afterY = (changedMask & 2u) != 0u ? vector.Y : currentY;

                    int baseIndex = entityIndex * 2;
                    _multiSlotsScratch[baseIndex] = xSlot;
                    _multiSlotsScratch[baseIndex + 1] = ySlot;
                    _multiValuesScratch[baseIndex] = PropertyValue.FromFloat(afterX);
                    _multiValuesScratch[baseIndex + 1] = PropertyValue.FromFloat(afterY);
                }

                bool isEditing = IsVectorEditing(widgetId, componentCount);
                _commands.SetPropertyValuesMany(widgetId, isEditing, _multiSlotsScratch.AsSpan(0, totalSlots), _multiValuesScratch.AsSpan(0, totalSlots));
            }
            else
            {
                _commands.NotifyPropertyWidgetState(widgetId, IsVectorEditing(widgetId, componentCount));
            }

            return;
        }

        if (componentCount == 3)
        {
            var vector = new Vector3(baseX, baseY, baseZ);
            Vector3 before = vector;
            changed = ImVectorInput.DrawAtMixed(item.Label, item.WidgetId, rect.X, rect.Y, labelWidth, inputWidth, rightOverlayWidth: 0f, ref vector, mixedMask, minValue, maxValue, "F2");
            if (vector.X != before.X) { changedMask |= 1u; }
            if (vector.Y != before.Y) { changedMask |= 2u; }
            if (vector.Z != before.Z) { changedMask |= 4u; }

            if (changed && changedMask != 0)
            {
                int totalSlots = components.Length * 3;
                EnsureMultiScratchCapacity(totalSlots);

                for (int entityIndex = 0; entityIndex < components.Length; entityIndex++)
                {
                    AnyComponentHandle component = components[entityIndex];
                    PropertySlot xSlot = entityIndex == 0 ? slot0 : default;
                    PropertySlot ySlot = entityIndex == 0 ? slot1 : default;
                    PropertySlot zSlot = entityIndex == 0 ? slot2 : default;
                    if (entityIndex != 0)
                    {
                        if (!TryResolveSlotForMulti(component, slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out xSlot) ||
                            !TryResolveSlotForMulti(component, slot1.PropertyIndex, slot1.PropertyId, slot1.Kind, out ySlot) ||
                            !TryResolveSlotForMulti(component, slot2.PropertyIndex, slot2.PropertyId, slot2.Kind, out zSlot))
                        {
                            return;
                        }
                    }

                    float currentX = PropertyDispatcher.ReadFloat(_propertyWorld, xSlot);
                    float currentY = PropertyDispatcher.ReadFloat(_propertyWorld, ySlot);
                    float currentZ = PropertyDispatcher.ReadFloat(_propertyWorld, zSlot);
                    float afterX = (changedMask & 1u) != 0u ? vector.X : currentX;
                    float afterY = (changedMask & 2u) != 0u ? vector.Y : currentY;
                    float afterZ = (changedMask & 4u) != 0u ? vector.Z : currentZ;

                    int baseIndex = entityIndex * 3;
                    _multiSlotsScratch[baseIndex] = xSlot;
                    _multiSlotsScratch[baseIndex + 1] = ySlot;
                    _multiSlotsScratch[baseIndex + 2] = zSlot;
                    _multiValuesScratch[baseIndex] = PropertyValue.FromFloat(afterX);
                    _multiValuesScratch[baseIndex + 1] = PropertyValue.FromFloat(afterY);
                    _multiValuesScratch[baseIndex + 2] = PropertyValue.FromFloat(afterZ);
                }

                bool isEditing = IsVectorEditing(widgetId, componentCount);
                _commands.SetPropertyValuesMany(widgetId, isEditing, _multiSlotsScratch.AsSpan(0, totalSlots), _multiValuesScratch.AsSpan(0, totalSlots));
            }
            else
            {
                _commands.NotifyPropertyWidgetState(widgetId, IsVectorEditing(widgetId, componentCount));
            }

            return;
        }

        {
            var vector = new Vector4(baseX, baseY, baseZ, baseW);
            Vector4 before = vector;
            changed = ImVectorInput.DrawAtMixed(item.Label, item.WidgetId, rect.X, rect.Y, labelWidth, inputWidth, rightOverlayWidth: 0f, ref vector, mixedMask, minValue, maxValue, "F2");
            if (vector.X != before.X) { changedMask |= 1u; }
            if (vector.Y != before.Y) { changedMask |= 2u; }
            if (vector.Z != before.Z) { changedMask |= 4u; }
            if (vector.W != before.W) { changedMask |= 8u; }

            if (changed && changedMask != 0)
            {
                int totalSlots = components.Length * 4;
                EnsureMultiScratchCapacity(totalSlots);

                for (int entityIndex = 0; entityIndex < components.Length; entityIndex++)
                {
                    AnyComponentHandle component = components[entityIndex];
                    PropertySlot xSlot = entityIndex == 0 ? slot0 : default;
                    PropertySlot ySlot = entityIndex == 0 ? slot1 : default;
                    PropertySlot zSlot = entityIndex == 0 ? slot2 : default;
                    PropertySlot wSlot = entityIndex == 0 ? slot3 : default;
                    if (entityIndex != 0)
                    {
                        if (!TryResolveSlotForMulti(component, slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out xSlot) ||
                            !TryResolveSlotForMulti(component, slot1.PropertyIndex, slot1.PropertyId, slot1.Kind, out ySlot) ||
                            !TryResolveSlotForMulti(component, slot2.PropertyIndex, slot2.PropertyId, slot2.Kind, out zSlot) ||
                            !TryResolveSlotForMulti(component, slot3.PropertyIndex, slot3.PropertyId, slot3.Kind, out wSlot))
                        {
                            return;
                        }
                    }

                    float currentX = PropertyDispatcher.ReadFloat(_propertyWorld, xSlot);
                    float currentY = PropertyDispatcher.ReadFloat(_propertyWorld, ySlot);
                    float currentZ = PropertyDispatcher.ReadFloat(_propertyWorld, zSlot);
                    float currentW = PropertyDispatcher.ReadFloat(_propertyWorld, wSlot);
                    float afterX = (changedMask & 1u) != 0u ? vector.X : currentX;
                    float afterY = (changedMask & 2u) != 0u ? vector.Y : currentY;
                    float afterZ = (changedMask & 4u) != 0u ? vector.Z : currentZ;
                    float afterW = (changedMask & 8u) != 0u ? vector.W : currentW;

                    int baseIndex = entityIndex * 4;
                    _multiSlotsScratch[baseIndex] = xSlot;
                    _multiSlotsScratch[baseIndex + 1] = ySlot;
                    _multiSlotsScratch[baseIndex + 2] = zSlot;
                    _multiSlotsScratch[baseIndex + 3] = wSlot;
                    _multiValuesScratch[baseIndex] = PropertyValue.FromFloat(afterX);
                    _multiValuesScratch[baseIndex + 1] = PropertyValue.FromFloat(afterY);
                    _multiValuesScratch[baseIndex + 2] = PropertyValue.FromFloat(afterZ);
                    _multiValuesScratch[baseIndex + 3] = PropertyValue.FromFloat(afterW);
                }

                bool isEditing = IsVectorEditing(widgetId, componentCount);
                _commands.SetPropertyValuesMany(widgetId, isEditing, _multiSlotsScratch.AsSpan(0, totalSlots), _multiValuesScratch.AsSpan(0, totalSlots));
            }
            else
            {
                _commands.NotifyPropertyWidgetState(widgetId, IsVectorEditing(widgetId, componentCount));
            }
        }
    }

    private void DrawSinglePropertyMulti(AnyComponentHandle component0, ReadOnlySpan<AnyComponentHandle> components, PropertyUiItem item, float labelWidth, float inputWidth)
    {
        PropertySlot slot0 = PropertyDispatcher.GetSlot(component0, item.Index0);
        if ((item.Info.Flags & PropertyFlags.ReadOnly) != 0)
        {
            DrawReadOnlyPropertyRow(item, slot0, labelWidth, inputWidth, prefabOverrideState: default);
            return;
        }

        if (TryDrawGeneratedDrawerMulti(item, component0, components, slot0, labelWidth, inputWidth))
        {
            return;
        }
        switch (item.Info.Kind)
        {
            case PropertyKind.Float:
            {
                float value = PropertyDispatcher.ReadFloat(_propertyWorld, slot0);
                bool mixed = false;
                for (int i = 1; i < components.Length; i++)
                {
                    if (!TryResolveSlotForMulti(components[i], slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out PropertySlot slot))
                    {
                        return;
                    }

                    float current = PropertyDispatcher.ReadFloat(_propertyWorld, slot);
                    mixed |= current != value;
                }

                float minInput = float.IsNaN(item.Info.Min) ? float.MinValue : item.Info.Min;
                float maxInput = float.IsNaN(item.Info.Max) ? float.MaxValue : item.Info.Max;
                if (minInput == maxInput)
                {
                    if (minInput >= float.MaxValue)
                    {
                        minInput = float.MaxValue - 1f;
                        maxInput = float.MaxValue;
                    }
                    else
                    {
                        maxInput = minInput + 1f;
                    }
                }

                var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
                rect = GetPaddedRowRect(rect);
                float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
                Im.Text(item.Label.AsSpan(), rect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

                float inputX = rect.X + labelWidth;
                bool changed = ImScalarInput.DrawAt(item.WidgetId, inputX, rect.Y, inputWidth, rightOverlayWidth: 0f, ref value, minInput, maxInput, "F2", mixed);

                int widgetId = Im.Context.GetId(item.WidgetId);
                bool isEditing = Im.Context.IsActive(widgetId) || Im.Context.IsFocused(widgetId);
                if (changed)
                {
                    EnsureMultiScratchCapacity(components.Length);
                    _multiSlotsScratch[0] = slot0;
                    for (int i = 1; i < components.Length; i++)
                    {
                        if (!TryResolveSlotForMulti(components[i], slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out PropertySlot slot))
                        {
                            return;
                        }

                        _multiSlotsScratch[i] = slot;
                    }

                    _multiValuesScratch[0] = PropertyValue.FromFloat(value);
                    _commands.SetPropertyValuesMany(widgetId, isEditing, _multiSlotsScratch.AsSpan(0, components.Length), _multiValuesScratch.AsSpan(0, 1));
                }
                else
                {
                    _commands.NotifyPropertyWidgetState(widgetId, isEditing);
                }
                break;
            }
            case PropertyKind.Int:
            {
                if (TryDrawBooleanOperationDropdownMulti(component0, components, item, slot0, labelWidth, inputWidth))
                {
                    break;
                }
                if (TryDrawBlendModeDropdownMulti(component0, components, item, slot0, labelWidth, inputWidth))
                {
                    break;
                }
                if (TryDrawStrokeCapDropdownMulti(component0, components, item, slot0, labelWidth, inputWidth))
                {
                    break;
                }
                if (TryDrawLayoutContainerDropdownMulti(component0, components, item, slot0, labelWidth, inputWidth))
                {
                    break;
                }
                if (TryDrawLayoutChildDropdownMulti(component0, components, item, slot0, labelWidth, inputWidth))
                {
                    break;
                }

                int first = PropertyDispatcher.ReadInt(_propertyWorld, slot0);
                bool mixed = false;
                for (int i = 1; i < components.Length; i++)
                {
                    if (!TryResolveSlotForMulti(components[i], slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out PropertySlot slot))
                    {
                        return;
                    }

                    int current = PropertyDispatcher.ReadInt(_propertyWorld, slot);
                    mixed |= current != first;
                }

                float minInput = float.IsNaN(item.Info.Min) ? float.MinValue : item.Info.Min;
                float maxInput = float.IsNaN(item.Info.Max) ? float.MaxValue : item.Info.Max;
                int minValue = minInput <= int.MinValue ? int.MinValue : (int)MathF.Round(minInput);
                int maxValue = maxInput >= int.MaxValue ? int.MaxValue : (int)MathF.Round(maxInput);
                if (minValue == maxValue)
                {
                    if (minValue == int.MaxValue)
                    {
                        minValue = int.MaxValue - 1;
                        maxValue = int.MaxValue;
                    }
                    else
                    {
                        maxValue = minValue + 1;
                    }
                }

                float value = first;
                var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
                rect = GetPaddedRowRect(rect);
                float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
                Im.Text(item.Label.AsSpan(), rect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

                float inputX = rect.X + labelWidth;
                bool changed = ImScalarInput.DrawAt(item.WidgetId, inputX, rect.Y, inputWidth, rightOverlayWidth: 0f, ref value, minValue, maxValue, "F0", mixed);

                int widgetId = Im.Context.GetId(item.WidgetId);
                bool isEditing = Im.Context.IsActive(widgetId) || Im.Context.IsFocused(widgetId);
                if (changed)
                {
                    int newValue = (int)MathF.Round(value);
                    newValue = Math.Clamp(newValue, minValue, maxValue);

                    EnsureMultiScratchCapacity(components.Length);
                    _multiSlotsScratch[0] = slot0;
                    for (int i = 1; i < components.Length; i++)
                    {
                        if (!TryResolveSlotForMulti(components[i], slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out PropertySlot slot))
                        {
                            return;
                        }

                        _multiSlotsScratch[i] = slot;
                    }

                    _multiValuesScratch[0] = PropertyValue.FromInt(newValue);
                    _commands.SetPropertyValuesMany(widgetId, isEditing, _multiSlotsScratch.AsSpan(0, components.Length), _multiValuesScratch.AsSpan(0, 1));
                }
                else
                {
                    _commands.NotifyPropertyWidgetState(widgetId, isEditing);
                }

                break;
            }
            case PropertyKind.Vec2:
            {
                Vector2 value = PropertyDispatcher.ReadVec2(_propertyWorld, slot0);
                Vector2 before = value;
                uint mixedMask = 0;
                for (int i = 1; i < components.Length; i++)
                {
                    if (!TryResolveSlotForMulti(components[i], slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out PropertySlot slot))
                    {
                        return;
                    }

                    Vector2 current = PropertyDispatcher.ReadVec2(_propertyWorld, slot);
                    if (current.X != value.X) { mixedMask |= 1u; }
                    if (current.Y != value.Y) { mixedMask |= 2u; }
                }

                float minValue = float.IsNaN(item.Info.Min) ? float.MinValue : item.Info.Min;
                float maxValue = float.IsNaN(item.Info.Max) ? float.MaxValue : item.Info.Max;

                var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
                rect = GetPaddedRowRect(rect);
                int widgetId = Im.Context.GetId(item.WidgetId);

                bool changed = ImVectorInput.DrawAtMixed(item.Label, item.WidgetId, rect.X, rect.Y, labelWidth, inputWidth, rightOverlayWidth: 0f, ref value, mixedMask, minValue, maxValue, "F2");
                uint changedMask = 0;
                if (value.X != before.X) { changedMask |= 1u; }
                if (value.Y != before.Y) { changedMask |= 2u; }

                bool isEditing = IsVectorEditing(widgetId, componentCount: 2);
                if (changed && changedMask != 0)
                {
                    EnsureMultiScratchCapacity(components.Length);
                    _multiSlotsScratch[0] = slot0;
                    for (int i = 1; i < components.Length; i++)
                    {
                        if (!TryResolveSlotForMulti(components[i], slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out PropertySlot slot))
                        {
                            return;
                        }
                        _multiSlotsScratch[i] = slot;
                    }

                    EnsureMultiScratchCapacity(components.Length);
                    for (int i = 0; i < components.Length; i++)
                    {
                        Vector2 current = PropertyDispatcher.ReadVec2(_propertyWorld, _multiSlotsScratch[i]);
                        if ((changedMask & 1u) != 0u) { current.X = value.X; }
                        if ((changedMask & 2u) != 0u) { current.Y = value.Y; }
                        _multiValuesScratch[i] = PropertyValue.FromVec2(current);
                    }

                    _commands.SetPropertyValuesMany(widgetId, isEditing, _multiSlotsScratch.AsSpan(0, components.Length), _multiValuesScratch.AsSpan(0, components.Length));
                }
                else
                {
                    _commands.NotifyPropertyWidgetState(widgetId, isEditing);
                }
                break;
            }
            case PropertyKind.Vec3:
            {
                Vector3 value = PropertyDispatcher.ReadVec3(_propertyWorld, slot0);
                Vector3 before = value;
                uint mixedMask = 0;
                for (int i = 1; i < components.Length; i++)
                {
                    if (!TryResolveSlotForMulti(components[i], slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out PropertySlot slot))
                    {
                        return;
                    }

                    Vector3 current = PropertyDispatcher.ReadVec3(_propertyWorld, slot);
                    if (current.X != value.X) { mixedMask |= 1u; }
                    if (current.Y != value.Y) { mixedMask |= 2u; }
                    if (current.Z != value.Z) { mixedMask |= 4u; }
                }

                float minValue = float.IsNaN(item.Info.Min) ? float.MinValue : item.Info.Min;
                float maxValue = float.IsNaN(item.Info.Max) ? float.MaxValue : item.Info.Max;

                var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
                rect = GetPaddedRowRect(rect);
                int widgetId = Im.Context.GetId(item.WidgetId);

                bool changed = ImVectorInput.DrawAtMixed(item.Label, item.WidgetId, rect.X, rect.Y, labelWidth, inputWidth, rightOverlayWidth: 0f, ref value, mixedMask, minValue, maxValue, "F2");
                uint changedMask = 0;
                if (value.X != before.X) { changedMask |= 1u; }
                if (value.Y != before.Y) { changedMask |= 2u; }
                if (value.Z != before.Z) { changedMask |= 4u; }

                bool isEditing = IsVectorEditing(widgetId, componentCount: 3);
                if (changed && changedMask != 0)
                {
                    EnsureMultiScratchCapacity(components.Length);
                    _multiSlotsScratch[0] = slot0;
                    for (int i = 1; i < components.Length; i++)
                    {
                        if (!TryResolveSlotForMulti(components[i], slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out PropertySlot slot))
                        {
                            return;
                        }
                        _multiSlotsScratch[i] = slot;
                    }

                    EnsureMultiScratchCapacity(components.Length);
                    for (int i = 0; i < components.Length; i++)
                    {
                        Vector3 current = PropertyDispatcher.ReadVec3(_propertyWorld, _multiSlotsScratch[i]);
                        if ((changedMask & 1u) != 0u) { current.X = value.X; }
                        if ((changedMask & 2u) != 0u) { current.Y = value.Y; }
                        if ((changedMask & 4u) != 0u) { current.Z = value.Z; }
                        _multiValuesScratch[i] = PropertyValue.FromVec3(current);
                    }

                    _commands.SetPropertyValuesMany(widgetId, isEditing, _multiSlotsScratch.AsSpan(0, components.Length), _multiValuesScratch.AsSpan(0, components.Length));
                }
                else
                {
                    _commands.NotifyPropertyWidgetState(widgetId, isEditing);
                }
                break;
            }
            case PropertyKind.Vec4:
            {
                Vector4 value = PropertyDispatcher.ReadVec4(_propertyWorld, slot0);
                Vector4 before = value;
                uint mixedMask = 0;
                for (int i = 1; i < components.Length; i++)
                {
                    if (!TryResolveSlotForMulti(components[i], slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out PropertySlot slot))
                    {
                        return;
                    }

                    Vector4 current = PropertyDispatcher.ReadVec4(_propertyWorld, slot);
                    if (current.X != value.X) { mixedMask |= 1u; }
                    if (current.Y != value.Y) { mixedMask |= 2u; }
                    if (current.Z != value.Z) { mixedMask |= 4u; }
                    if (current.W != value.W) { mixedMask |= 8u; }
                }

                float minValue = float.IsNaN(item.Info.Min) ? float.MinValue : item.Info.Min;
                float maxValue = float.IsNaN(item.Info.Max) ? float.MaxValue : item.Info.Max;

                int widgetId = Im.Context.GetId(item.WidgetId);
                var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
                rect = GetPaddedRowRect(rect);
                bool changed = ImVectorInput.DrawAtMixed(item.Label, item.WidgetId, rect.X, rect.Y, labelWidth, inputWidth, rightOverlayWidth: 0f, ref value, mixedMask, minValue, maxValue, "F2");
                bool isEditing = IsVectorEditing(widgetId, componentCount: 4);

                uint changedMask = 0;
                if (value.X != before.X) { changedMask |= 1u; }
                if (value.Y != before.Y) { changedMask |= 2u; }
                if (value.Z != before.Z) { changedMask |= 4u; }
                if (value.W != before.W) { changedMask |= 8u; }

                if (changed && changedMask != 0)
                {
                    EnsureMultiScratchCapacity(components.Length);
                    _multiSlotsScratch[0] = slot0;
                    for (int i = 1; i < components.Length; i++)
                    {
                        if (!TryResolveSlotForMulti(components[i], slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out PropertySlot slot))
                        {
                            return;
                        }
                        _multiSlotsScratch[i] = slot;
                    }

                    EnsureMultiScratchCapacity(components.Length);
                    for (int i = 0; i < components.Length; i++)
                    {
                        Vector4 current = PropertyDispatcher.ReadVec4(_propertyWorld, _multiSlotsScratch[i]);
                        if ((changedMask & 1u) != 0u) { current.X = value.X; }
                        if ((changedMask & 2u) != 0u) { current.Y = value.Y; }
                        if ((changedMask & 4u) != 0u) { current.Z = value.Z; }
                        if ((changedMask & 8u) != 0u) { current.W = value.W; }
                        _multiValuesScratch[i] = PropertyValue.FromVec4(current);
                    }

                    _commands.SetPropertyValuesMany(widgetId, isEditing, _multiSlotsScratch.AsSpan(0, components.Length), _multiValuesScratch.AsSpan(0, components.Length));
                }
                else
                {
                    _commands.NotifyPropertyWidgetState(widgetId, isEditing);
                }
                break;
            }
            case PropertyKind.Bool:
            {
                bool value = PropertyDispatcher.ReadBool(_propertyWorld, slot0);
                bool mixed = false;
                for (int i = 1; i < components.Length; i++)
                {
                    if (!TryResolveSlotForMulti(components[i], slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out PropertySlot slot))
                    {
                        return;
                    }

                    bool current = PropertyDispatcher.ReadBool(_propertyWorld, slot);
                    mixed |= current != value;
                }

                var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
                rect = GetPaddedRowRect(rect);
                float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
                Im.Text(item.Label.AsSpan(), rect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

                var inputRect = new ImRect(rect.X + labelWidth, rect.Y, inputWidth, rect.Height);
                Im.DrawRoundedRect(inputRect.X, inputRect.Y, inputRect.Width, inputRect.Height, Im.Style.CornerRadius, Im.Style.Surface);
                Im.DrawRoundedRectStroke(inputRect.X, inputRect.Y, inputRect.Width, inputRect.Height, Im.Style.CornerRadius, Im.Style.Border, Im.Style.BorderWidth);

                float checkboxX = inputRect.X + Im.Style.Padding;
                float checkboxY = rect.Y + (rect.Height - Im.Style.CheckboxSize) * 0.5f;
                if (Im.CheckboxTriState(item.WidgetId, ref value, mixed, checkboxX, checkboxY))
                {
                    EnsureMultiScratchCapacity(components.Length);
                    _multiSlotsScratch[0] = slot0;
                    for (int i = 1; i < components.Length; i++)
                    {
                        if (!TryResolveSlotForMulti(components[i], slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out PropertySlot slot))
                        {
                            return;
                        }
                        _multiSlotsScratch[i] = slot;
                    }

                    int widgetId = Im.Context.GetId(item.WidgetId);
                    _multiValuesScratch[0] = PropertyValue.FromBool(value);
                    _commands.SetPropertyValuesMany(widgetId, isEditing: false, _multiSlotsScratch.AsSpan(0, components.Length), _multiValuesScratch.AsSpan(0, 1));
                }

                break;
            }
            default:
            {
                DrawPropertyLabel(item.Label);
                Im.LabelText("(multi-edit unsupported)");
                break;
            }
        }
    }

    private void DrawVectorGroup(AnyComponentHandle component, PropertyUiItem item, float labelWidth, float inputWidth)
    {
        float minValue = float.IsNaN(item.Info.Min) ? float.MinValue : item.Info.Min;
        float maxValue = float.IsNaN(item.Info.Max) ? float.MaxValue : item.Info.Max;

        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = GetPaddedRowRect(rect);
        int widgetId = Im.Context.GetId(item.WidgetId);
        switch (item.Kind)
        {
            case PropertyUiKind.Vector2:
            {
                PropertySlot xSlot = PropertyDispatcher.GetSlot(component, item.Index0);
                PropertySlot ySlot = PropertyDispatcher.GetSlot(component, item.Index1);
                float xValue = PropertyDispatcher.ReadFloat(_propertyWorld, xSlot);
                float yValue = PropertyDispatcher.ReadFloat(_propertyWorld, ySlot);

                if ((item.Info.Flags & PropertyFlags.ReadOnly) != 0)
                {
                    DrawReadOnlyVectorGroupRow(item, labelWidth, inputWidth, componentCount: 2, xValue, yValue, 0f, 0f, mixedMask: 0u);
                    _commands.NotifyPropertyWidgetState(widgetId, isEditing: false);
                    return;
                }

                var vector = new Vector2(xValue, yValue);
                if (ImVectorInput.DrawAt(item.Label, item.WidgetId, rect.X, rect.Y, labelWidth, inputWidth, KeyIconWidth, ref vector, minValue, maxValue, "F2"))
                {
                    bool isEditing = Im.Context.AnyActive;
                    if (component.Kind == TransformComponent.Api.PoolIdConst && item.Info.Name == _anchorLabelHandle && !_gizmoTargetEntity.IsNull &&
                        TryGetTransformPositionSlots(component, out PropertySlot posXSlot, out PropertySlot posYSlot))
                    {
                        _commands.SetTransformAnchorMaintainParentSpace(widgetId, isEditing, _gizmoTargetEntity, posXSlot, posYSlot, xSlot, ySlot, vector);
                    }
                    else
                    {
                        _commands.SetPropertyBatch2(
                            widgetId,
                            isEditing,
                            xSlot,
                            PropertyValue.FromFloat(vector.X),
                            ySlot,
                            PropertyValue.FromFloat(vector.Y));
                    }
                }

                float inputX = rect.X + labelWidth;
                float componentWidth = (inputWidth - Im.Style.Spacing) * 0.5f;
                DrawVectorComponentKeyIcon(xSlot, new ImRect(inputX + componentWidth - KeyIconWidth, rect.Y, KeyIconWidth, rect.Height));
                DrawVectorComponentKeyIcon(ySlot, new ImRect(inputX + componentWidth + Im.Style.Spacing + componentWidth - KeyIconWidth, rect.Y, KeyIconWidth, rect.Height));
                break;
            }
            case PropertyUiKind.Vector3:
            {
                PropertySlot xSlot = PropertyDispatcher.GetSlot(component, item.Index0);
                PropertySlot ySlot = PropertyDispatcher.GetSlot(component, item.Index1);
                PropertySlot zSlot = PropertyDispatcher.GetSlot(component, item.Index2);
                float xValue = PropertyDispatcher.ReadFloat(_propertyWorld, xSlot);
                float yValue = PropertyDispatcher.ReadFloat(_propertyWorld, ySlot);
                float zValue = PropertyDispatcher.ReadFloat(_propertyWorld, zSlot);

                if ((item.Info.Flags & PropertyFlags.ReadOnly) != 0)
                {
                    DrawReadOnlyVectorGroupRow(item, labelWidth, inputWidth, componentCount: 3, xValue, yValue, zValue, 0f, mixedMask: 0u);
                    _commands.NotifyPropertyWidgetState(widgetId, isEditing: false);
                    return;
                }

                var vector = new Vector3(xValue, yValue, zValue);
                if (ImVectorInput.DrawAt(item.Label, item.WidgetId, rect.X, rect.Y, labelWidth, inputWidth, KeyIconWidth, ref vector, minValue, maxValue, "F2"))
                {
                    bool isEditing = Im.Context.AnyActive;
                    _commands.SetPropertyBatch3(
                        widgetId,
                        isEditing,
                        xSlot,
                        PropertyValue.FromFloat(vector.X),
                        ySlot,
                        PropertyValue.FromFloat(vector.Y),
                        zSlot,
                        PropertyValue.FromFloat(vector.Z));
                }

                float inputX = rect.X + labelWidth;
                float componentWidth = (inputWidth - Im.Style.Spacing * 2f) / 3f;
                DrawVectorComponentKeyIcon(xSlot, new ImRect(inputX + componentWidth - KeyIconWidth, rect.Y, KeyIconWidth, rect.Height));
                DrawVectorComponentKeyIcon(ySlot, new ImRect(inputX + (componentWidth + Im.Style.Spacing) + componentWidth - KeyIconWidth, rect.Y, KeyIconWidth, rect.Height));
                DrawVectorComponentKeyIcon(zSlot, new ImRect(inputX + (componentWidth + Im.Style.Spacing) * 2f + componentWidth - KeyIconWidth, rect.Y, KeyIconWidth, rect.Height));
                break;
            }
            case PropertyUiKind.Vector4:
            {
                PropertySlot xSlot = PropertyDispatcher.GetSlot(component, item.Index0);
                PropertySlot ySlot = PropertyDispatcher.GetSlot(component, item.Index1);
                PropertySlot zSlot = PropertyDispatcher.GetSlot(component, item.Index2);
                PropertySlot wSlot = PropertyDispatcher.GetSlot(component, item.Index3);
                float xValue = PropertyDispatcher.ReadFloat(_propertyWorld, xSlot);
                float yValue = PropertyDispatcher.ReadFloat(_propertyWorld, ySlot);
                float zValue = PropertyDispatcher.ReadFloat(_propertyWorld, zSlot);
                float wValue = PropertyDispatcher.ReadFloat(_propertyWorld, wSlot);

                if ((item.Info.Flags & PropertyFlags.ReadOnly) != 0)
                {
                    DrawReadOnlyVectorGroupRow(item, labelWidth, inputWidth, componentCount: 4, xValue, yValue, zValue, wValue, mixedMask: 0u);
                    _commands.NotifyPropertyWidgetState(widgetId, isEditing: false);
                    return;
                }

                var vector = new Vector4(xValue, yValue, zValue, wValue);
                if (ImVectorInput.DrawAt(item.Label, item.WidgetId, rect.X, rect.Y, labelWidth, inputWidth, KeyIconWidth, ref vector, minValue, maxValue, "F2"))
                {
                    bool isEditing = Im.Context.AnyActive;
                    _commands.SetPropertyBatch4(
                        widgetId,
                        isEditing,
                        xSlot,
                        PropertyValue.FromFloat(vector.X),
                        ySlot,
                        PropertyValue.FromFloat(vector.Y),
                        zSlot,
                        PropertyValue.FromFloat(vector.Z),
                        wSlot,
                        PropertyValue.FromFloat(vector.W));
                }

                float inputX = rect.X + labelWidth;
                float componentWidth = (inputWidth - Im.Style.Spacing * 3f) / 4f;
                DrawVectorComponentKeyIcon(xSlot, new ImRect(inputX + componentWidth - KeyIconWidth, rect.Y, KeyIconWidth, rect.Height));
                DrawVectorComponentKeyIcon(ySlot, new ImRect(inputX + (componentWidth + Im.Style.Spacing) + componentWidth - KeyIconWidth, rect.Y, KeyIconWidth, rect.Height));
                DrawVectorComponentKeyIcon(zSlot, new ImRect(inputX + (componentWidth + Im.Style.Spacing) * 2f + componentWidth - KeyIconWidth, rect.Y, KeyIconWidth, rect.Height));
                DrawVectorComponentKeyIcon(wSlot, new ImRect(inputX + (componentWidth + Im.Style.Spacing) * 3f + componentWidth - KeyIconWidth, rect.Y, KeyIconWidth, rect.Height));
                break;
            }
        }

        _commands.NotifyPropertyWidgetState(widgetId, Im.Context.AnyActive);
    }

    private void DrawSingleProperty(AnyComponentHandle component, PropertyUiItem item, float labelWidth, float inputWidth)
    {
        var slot = PropertyDispatcher.GetSlot(component, item.Index0);
        if (TryGetPrefabPropertyBindingForSlot(slot, out PrefabPropertyBinding binding))
        {
            DrawBoundPropertyRow(item, slot, labelWidth, inputWidth, binding);
            return;
        }

        TryGetPrefabOverrideUiState(_gizmoTargetEntity, slot, out PrefabOverrideUiState prefabOverrideState);

        if ((item.Info.Flags & PropertyFlags.ReadOnly) != 0)
        {
            DrawReadOnlyPropertyRow(item, slot, labelWidth, inputWidth, in prefabOverrideState);
            return;
        }

        if (TryDrawEventListenerVariableBindingDropdown(component, item, slot, labelWidth, inputWidth, in prefabOverrideState))
        {
            return;
        }

        if (TryDrawGeneratedDrawerSingle(item, slot, labelWidth, inputWidth))
        {
            return;
        }
        switch (item.Info.Kind)
        {
            case PropertyKind.Float:
                DrawFloatProperty(item, slot, labelWidth, inputWidth, in prefabOverrideState);
                break;
            case PropertyKind.Int:
                if (TryDrawBooleanOperationDropdown(component, item, slot, labelWidth, inputWidth))
                {
                    break;
                }
                if (TryDrawBlendModeDropdown(component, item, slot, labelWidth, inputWidth))
                {
                    break;
                }
                if (TryDrawStrokeCapDropdown(component, item, slot, labelWidth, inputWidth))
                {
                    break;
                }
                if (TryDrawLayoutContainerDropdown(component, item, slot, labelWidth, inputWidth))
                {
                    break;
                }
                if (TryDrawLayoutChildDropdown(component, item, slot, labelWidth, inputWidth))
                {
                    break;
                }
                if (TryDrawTextOverflowDropdown(component, item, slot, labelWidth, inputWidth))
                {
                    break;
                }
                if (TryDrawTextAlignXIcons(component, item, slot, labelWidth, inputWidth))
                {
                    break;
                }
                if (TryDrawTextAlignYIcons(component, item, slot, labelWidth, inputWidth))
                {
                    break;
                }
                DrawIntProperty(item, slot, labelWidth, inputWidth, in prefabOverrideState);
                break;
            case PropertyKind.Vec2:
            {
                int widgetId = Im.Context.GetId(item.WidgetId);
                float minValue = float.IsNaN(item.Info.Min) ? float.MinValue : item.Info.Min;
                float maxValue = float.IsNaN(item.Info.Max) ? float.MaxValue : item.Info.Max;

                var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
                rect = GetPaddedRowRect(rect);
                bool hovered = rect.Contains(Im.MousePos);
                var inputRect = new ImRect(rect.X + labelWidth, rect.Y, inputWidth, rect.Height);
                TryDrawPropertyBindingContextMenu(slot, inputRect, allowUnbind: true);
                Vector2 value = PropertyDispatcher.ReadVec2(_propertyWorld, slot);
                if (ImVectorInput.DrawAt(item.Label, item.WidgetId, rect.X, rect.Y, labelWidth, inputWidth, KeyIconWidth, ref value, minValue, maxValue, "F2"))
                {
                    bool isEditing = Im.Context.AnyActive;
                    if (component.Kind == TransformComponent.Api.PoolIdConst && item.Info.Name == _anchorLabelHandle && !_gizmoTargetEntity.IsNull &&
                        TryGetTransformPositionSlotVec2(component, out PropertySlot positionSlot))
                    {
                        _commands.SetTransformAnchorMaintainParentSpaceVec2(widgetId, isEditing, _gizmoTargetEntity, positionSlot, slot, value);
                    }
                    else
                    {
                        _commands.SetPropertyValue(widgetId, isEditing, slot, PropertyValue.FromVec2(value));
                    }
                }
                _commands.NotifyPropertyWidgetState(widgetId, Im.Context.AnyActive);

                var ctx = Im.Context;
                bool isActive =
                    ctx.IsActive(widgetId * 31 + 0) || ctx.IsFocused(widgetId * 31 + 0) ||
                    ctx.IsActive(widgetId * 31 + 1) || ctx.IsFocused(widgetId * 31 + 1);
                ReportGeneratedGizmoSingle(item, slot, hovered, isActive);

                if (item.Info.HasChannels && !item.Info.IsChannel)
                {
                    DrawVectorRootChannelKeyIcons(slot, rect.X + labelWidth, rect.Y, inputWidth, channelCount: 2);
                }
                else
                {
                    DrawVectorPropertyKeyIcons(slot, rect.X + labelWidth, rect.Y, inputWidth, componentCount: 2);
                }
                DrawPrefabOverrideBorderIfNeeded(inputRect, in prefabOverrideState);
                break;
            }
            case PropertyKind.Vec3:
            {
                int widgetId = Im.Context.GetId(item.WidgetId);
                float minValue = float.IsNaN(item.Info.Min) ? float.MinValue : item.Info.Min;
                float maxValue = float.IsNaN(item.Info.Max) ? float.MaxValue : item.Info.Max;

                var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
                rect = GetPaddedRowRect(rect);
                bool hovered = rect.Contains(Im.MousePos);
                var inputRect = new ImRect(rect.X + labelWidth, rect.Y, inputWidth, rect.Height);
                TryDrawPropertyBindingContextMenu(slot, inputRect, allowUnbind: true);
                Vector3 value = PropertyDispatcher.ReadVec3(_propertyWorld, slot);
                if (ImVectorInput.DrawAt(item.Label, item.WidgetId, rect.X, rect.Y, labelWidth, inputWidth, KeyIconWidth, ref value, minValue, maxValue, "F2"))
                {
                    bool isEditing = Im.Context.AnyActive;
                    _commands.SetPropertyValue(widgetId, isEditing, slot, PropertyValue.FromVec3(value));
                }
                _commands.NotifyPropertyWidgetState(widgetId, Im.Context.AnyActive);

                var ctx = Im.Context;
                bool isActive =
                    ctx.IsActive(widgetId * 31 + 0) || ctx.IsFocused(widgetId * 31 + 0) ||
                    ctx.IsActive(widgetId * 31 + 1) || ctx.IsFocused(widgetId * 31 + 1) ||
                    ctx.IsActive(widgetId * 31 + 2) || ctx.IsFocused(widgetId * 31 + 2);
                ReportGeneratedGizmoSingle(item, slot, hovered, isActive);

                if (item.Info.HasChannels && !item.Info.IsChannel)
                {
                    DrawVectorRootChannelKeyIcons(slot, rect.X + labelWidth, rect.Y, inputWidth, channelCount: 3);
                }
                else
                {
                    DrawVectorPropertyKeyIcons(slot, rect.X + labelWidth, rect.Y, inputWidth, componentCount: 3);
                }
                DrawPrefabOverrideBorderIfNeeded(inputRect, in prefabOverrideState);
                break;
            }
            case PropertyKind.Vec4:
            {
                int widgetId = Im.Context.GetId(item.WidgetId);
                float minValue = float.IsNaN(item.Info.Min) ? float.MinValue : item.Info.Min;
	                float maxValue = float.IsNaN(item.Info.Max) ? float.MaxValue : item.Info.Max;

	                Vector4 value = PropertyDispatcher.ReadVec4(_propertyWorld, slot);
	                var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
	                rect = GetPaddedRowRect(rect);
                    bool hovered = rect.Contains(Im.MousePos);
                    var inputRect = new ImRect(rect.X + labelWidth, rect.Y, inputWidth, rect.Height);
                    TryDrawPropertyBindingContextMenu(slot, inputRect, allowUnbind: true);
	                bool changed = ImVectorInput.DrawAt(item.Label, item.WidgetId, rect.X, rect.Y, labelWidth, inputWidth, KeyIconWidth, ref value, minValue, maxValue, "F2");
	                if (item.Info.HasChannels && !item.Info.IsChannel)
	                {
	                    DrawVectorRootChannelKeyIcons(slot, rect.X + labelWidth, rect.Y, inputWidth, channelCount: 4);
	                }
	                else
	                {
	                    DrawVectorPropertyKeyIcons(slot, rect.X + labelWidth, rect.Y, inputWidth, componentCount: 4);
	                }

                if (changed)
                {
                    bool isEditing = Im.Context.AnyActive;
                    _commands.SetPropertyValue(widgetId, isEditing, slot, PropertyValue.FromVec4(value));
                }
                _commands.NotifyPropertyWidgetState(widgetId, Im.Context.AnyActive);

                var ctx = Im.Context;
                bool isActive =
                    ctx.IsActive(widgetId * 31 + 0) || ctx.IsFocused(widgetId * 31 + 0) ||
                    ctx.IsActive(widgetId * 31 + 1) || ctx.IsFocused(widgetId * 31 + 1) ||
                    ctx.IsActive(widgetId * 31 + 2) || ctx.IsFocused(widgetId * 31 + 2) ||
                    ctx.IsActive(widgetId * 31 + 3) || ctx.IsFocused(widgetId * 31 + 3);
                ReportGeneratedGizmoSingle(item, slot, hovered, isActive);

                DrawPrefabOverrideBorderIfNeeded(inputRect, in prefabOverrideState);
                break;
            }
            case PropertyKind.Bool:
            {
                int widgetId = Im.Context.GetId(item.WidgetId);
                bool value = PropertyDispatcher.ReadBool(_propertyWorld, slot);

                var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
                rect = GetPaddedRowRect(rect);
                bool hovered = rect.Contains(Im.MousePos);
                float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
                Im.Text(item.Label.AsSpan(), rect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

                var inputRect = new ImRect(rect.X + labelWidth, rect.Y, inputWidth, rect.Height);
                TryDrawPropertyBindingContextMenu(slot, inputRect, allowUnbind: true);
                Im.DrawRoundedRect(inputRect.X, inputRect.Y, inputRect.Width, inputRect.Height, Im.Style.CornerRadius, Im.Style.Surface);
                Im.DrawRoundedRectStroke(inputRect.X, inputRect.Y, inputRect.Width, inputRect.Height, Im.Style.CornerRadius, Im.Style.Border, Im.Style.BorderWidth);

                float checkboxX = inputRect.X + Im.Style.Padding;
                float checkboxY = rect.Y + (rect.Height - Im.Style.CheckboxSize) * 0.5f;
                if (Im.Checkbox(item.WidgetId, ref value, checkboxX, checkboxY))
                {
                    _commands.SetPropertyValue(widgetId, isEditing: false, slot, PropertyValue.FromBool(value));
                }
                _commands.NotifyPropertyWidgetState(widgetId, isEditing: false);
                ReportGeneratedGizmoSingle(item, slot, hovered, active: false);
                DrawKeyIcon(slot, inputRect);
                DrawPrefabOverrideBorderIfNeeded(inputRect, in prefabOverrideState);
                break;
            }
            case PropertyKind.StringHandle:
                if (TryDrawTextFontDropdown(component, item, slot, labelWidth, inputWidth))
                {
                    break;
                }
                DrawStringHandleProperty(item, slot, labelWidth, inputWidth, in prefabOverrideState);
                break;
            case PropertyKind.Color32:
                DrawColorProperty(item, slot, labelWidth, inputWidth, in prefabOverrideState);
                break;
            default:
                DrawPropertyLabel(item.Label);
                Im.LabelText("(unsupported type)");
                break;
        }
    }

    private void DrawReadOnlyPropertyRow(PropertyUiItem item, in PropertySlot slot, float labelWidth, float inputWidth, in PrefabOverrideUiState prefabOverrideState)
    {
        int rowWidgetId = Im.Context.GetId(item.WidgetId);

        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = GetPaddedRowRect(rect);
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(item.Label.AsSpan(), rect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        var inputRect = new ImRect(rect.X + labelWidth, rect.Y, inputWidth, rect.Height);
        TryDrawPropertyBindingContextMenu(slot, inputRect, allowUnbind: true);

        if (slot.Kind == PropertyKind.Float)
        {
            float minValue = float.IsNaN(item.Info.Min) ? float.MinValue : item.Info.Min;
            float maxValue = float.IsNaN(item.Info.Max) ? float.MaxValue : item.Info.Max;
            float value = PropertyDispatcher.ReadFloat(_propertyWorld, slot);
            _ = ImScalarInput.DrawAt(item.WidgetId, inputRect.X, inputRect.Y, inputRect.Width, rightOverlayWidth: 0f, ref value, minValue, maxValue, "F2", mixed: false, disabled: true);
            _commands.NotifyPropertyWidgetState(rowWidgetId, isEditing: false);
            DrawPrefabOverrideBorderIfNeeded(inputRect, in prefabOverrideState);
            return;
        }

        if (slot.Kind == PropertyKind.Vec2)
        {
            float minValue = float.IsNaN(item.Info.Min) ? float.MinValue : item.Info.Min;
            float maxValue = float.IsNaN(item.Info.Max) ? float.MaxValue : item.Info.Max;
            Vector2 value = PropertyDispatcher.ReadVec2(_propertyWorld, slot);
            _ = ImVectorInput.DrawAt(item.WidgetId, inputRect.X, inputRect.Y, inputRect.Width, rightOverlayWidth: 0f, ref value, minValue, maxValue, "F2", disabled: true);
            _commands.NotifyPropertyWidgetState(rowWidgetId, isEditing: false);
            DrawPrefabOverrideBorderIfNeeded(inputRect, in prefabOverrideState);
            return;
        }

        if (slot.Kind == PropertyKind.Vec3)
        {
            float minValue = float.IsNaN(item.Info.Min) ? float.MinValue : item.Info.Min;
            float maxValue = float.IsNaN(item.Info.Max) ? float.MaxValue : item.Info.Max;
            Vector3 value = PropertyDispatcher.ReadVec3(_propertyWorld, slot);
            _ = ImVectorInput.DrawAt(item.WidgetId, inputRect.X, inputRect.Y, inputRect.Width, rightOverlayWidth: 0f, ref value, minValue, maxValue, "F2", disabled: true);
            _commands.NotifyPropertyWidgetState(rowWidgetId, isEditing: false);
            DrawPrefabOverrideBorderIfNeeded(inputRect, in prefabOverrideState);
            return;
        }

        if (slot.Kind == PropertyKind.Vec4)
        {
            float minValue = float.IsNaN(item.Info.Min) ? float.MinValue : item.Info.Min;
            float maxValue = float.IsNaN(item.Info.Max) ? float.MaxValue : item.Info.Max;
            Vector4 value = PropertyDispatcher.ReadVec4(_propertyWorld, slot);
            _ = ImVectorInput.DrawAt(item.WidgetId, inputRect.X, inputRect.Y, inputRect.Width, rightOverlayWidth: 0f, ref value, minValue, maxValue, "F2", disabled: true);
            _commands.NotifyPropertyWidgetState(rowWidgetId, isEditing: false);
            DrawPrefabOverrideBorderIfNeeded(inputRect, in prefabOverrideState);
            return;
        }

        Im.DrawRoundedRect(inputRect.X, inputRect.Y, inputRect.Width, inputRect.Height, Im.Style.CornerRadius, Im.Style.Surface);
        DrawBoundReadOnlyValue(slot, inputRect, textY);
        Im.DrawRoundedRectStroke(inputRect.X, inputRect.Y, inputRect.Width, inputRect.Height, Im.Style.CornerRadius, Im.Style.Primary, Im.Style.BorderWidth);
        _commands.NotifyPropertyWidgetState(rowWidgetId, isEditing: false);
        DrawPrefabOverrideBorderIfNeeded(inputRect, in prefabOverrideState);
    }

    private static void DrawReadOnlyVectorGroupRow(PropertyUiItem item, float labelWidth, float inputWidth, int componentCount, float x, float y, float z, float w, uint mixedMask)
    {
        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = GetPaddedRowRect(rect);
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(item.Label.AsSpan(), rect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        float minValue = float.IsNaN(item.Info.Min) ? float.MinValue : item.Info.Min;
        float maxValue = float.IsNaN(item.Info.Max) ? float.MaxValue : item.Info.Max;

        var inputRect = new ImRect(rect.X + labelWidth, rect.Y, inputWidth, rect.Height);

        if (componentCount == 2)
        {
            Vector2 value = new Vector2(x, y);
            _ = ImVectorInput.DrawAtMixed(item.WidgetId, inputRect.X, inputRect.Y, inputRect.Width, rightOverlayWidth: 0f, ref value, mixedMask, minValue, maxValue, "F2", disabled: true);
            return;
        }

        if (componentCount == 3)
        {
            Vector3 value = new Vector3(x, y, z);
            _ = ImVectorInput.DrawAtMixed(item.WidgetId, inputRect.X, inputRect.Y, inputRect.Width, rightOverlayWidth: 0f, ref value, mixedMask, minValue, maxValue, "F2", disabled: true);
            return;
        }

        Vector4 value4 = new Vector4(x, y, z, w);
        _ = ImVectorInput.DrawAtMixed(item.WidgetId, inputRect.X, inputRect.Y, inputRect.Width, rightOverlayWidth: 0f, ref value4, mixedMask, minValue, maxValue, "F2", disabled: true);
    }

    private readonly struct PrefabPropertyBinding
    {
        public readonly EntityId OwningPrefab;
        public readonly ushort VariableId;
        public readonly byte Direction;
        public readonly StringHandle VariableName;

        public PrefabPropertyBinding(EntityId owningPrefab, ushort variableId, byte direction, StringHandle variableName)
        {
            OwningPrefab = owningPrefab;
            VariableId = variableId;
            Direction = direction;
            VariableName = variableName;
        }
    }

    private void DrawBoundPropertyRow(PropertyUiItem item, in PropertySlot slot, float labelWidth, float inputWidth, in PrefabPropertyBinding binding)
    {
        int rowWidgetId = Im.Context.GetId(item.WidgetId);

        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = GetPaddedRowRect(rect);
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(item.Label.AsSpan(), rect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        var inputRect = new ImRect(rect.X + labelWidth, rect.Y, inputWidth, rect.Height);
        TryDrawPropertyBindingContextMenu(slot, inputRect, allowUnbind: true);

        bool isEditable =
            binding.Direction == 1 &&
            (item.Info.Flags & PropertyFlags.ReadOnly) == 0;
        uint borderColor = isEditable ? Im.Style.Secondary : Im.Style.Primary;
        if (!isEditable)
        {
            Im.DrawRoundedRect(inputRect.X, inputRect.Y, inputRect.Width, inputRect.Height, Im.Style.CornerRadius, Im.Style.Surface);
        }

        if (inputRect.Contains(Im.MousePos))
        {
            Im.Context.SetHot(rowWidgetId);
        }

        if (!isEditable)
        {
            DrawBoundReadOnlyValue(slot, inputRect, textY);
            Im.DrawRoundedRectStroke(inputRect.X, inputRect.Y, inputRect.Width, inputRect.Height, Im.Style.CornerRadius, borderColor, Im.Style.BorderWidth);
            _commands.NotifyPropertyWidgetState(rowWidgetId, isEditing: false);
            return;
        }

        DrawBoundEditableValue(rowWidgetId, slot, inputRect);

        // Draw border last so it stays on top of widgets.
        Im.DrawRoundedRectStroke(inputRect.X, inputRect.Y, inputRect.Width, inputRect.Height, Im.Style.CornerRadius, borderColor, Im.Style.BorderWidth);
        if (slot.Kind != PropertyKind.Color32)
        {
            DrawKeyIcon(slot, inputRect);
        }
    }

    private void DrawBoundReadOnlyValue(in PropertySlot slot, ImRect inputRect, float textY)
    {
        float valueX = inputRect.X + Im.Style.Padding;
        uint color = Im.Style.TextSecondary;

        switch (slot.Kind)
        {
            case PropertyKind.Float:
            {
                Span<char> buffer = stackalloc char[32];
                float v = PropertyDispatcher.ReadFloat(_propertyWorld, slot);
                if (!v.TryFormat(buffer, out int written, "0.##"))
                {
                    written = 0;
                }
                Im.Text(buffer[..written], valueX, textY, Im.Style.FontSize, color);
                break;
            }
            case PropertyKind.Int:
            {
                Span<char> buffer = stackalloc char[32];
                int v = PropertyDispatcher.ReadInt(_propertyWorld, slot);
                v.TryFormat(buffer, out int written);
                Im.Text(buffer[..written], valueX, textY, Im.Style.FontSize, color);
                break;
            }
            case PropertyKind.Bool:
            {
                bool v = PropertyDispatcher.ReadBool(_propertyWorld, slot);
                ReadOnlySpan<char> text = v ? "True".AsSpan() : "False".AsSpan();
                Im.Text(text, valueX, textY, Im.Style.FontSize, color);
                break;
            }
            case PropertyKind.Vec2:
            {
                Vector2 v = PropertyDispatcher.ReadVec2(_propertyWorld, slot);
                DrawVec2Text(v, valueX, textY, color);
                break;
            }
            case PropertyKind.Vec3:
            {
                Vector3 v = PropertyDispatcher.ReadVec3(_propertyWorld, slot);
                DrawVec3Text(v, valueX, textY, color);
                break;
            }
            case PropertyKind.Vec4:
            {
                Vector4 v = PropertyDispatcher.ReadVec4(_propertyWorld, slot);
                DrawVec4Text(v, valueX, textY, color);
                break;
            }
            case PropertyKind.Color32:
            {
                Color32 c = PropertyDispatcher.ReadColor32(_propertyWorld, slot);
                Span<char> buffer = stackalloc char[10];
                buffer[0] = '#';
                WriteHexByte(buffer.Slice(1, 2), c.R);
                WriteHexByte(buffer.Slice(3, 2), c.G);
                WriteHexByte(buffer.Slice(5, 2), c.B);
                WriteHexByte(buffer.Slice(7, 2), c.A);
                Im.Text(buffer[..9], valueX, textY, Im.Style.FontSize, color);
                break;
            }
            case PropertyKind.StringHandle:
            {
                StringHandle h = PropertyDispatcher.ReadStringHandle(_propertyWorld, slot);
                string s = h.IsValid ? h.ToString() : string.Empty;
                Im.Text(s.AsSpan(), valueX, textY, Im.Style.FontSize, color);
                break;
            }
            default:
                Im.Text("—".AsSpan(), valueX, textY, Im.Style.FontSize, color);
                break;
        }
    }

    private void DrawBoundEditableValue(int rowWidgetId, in PropertySlot slot, ImRect inputRect)
    {
        float x = inputRect.X;
        float y = inputRect.Y;
        float w = inputRect.Width;
        float h = inputRect.Height;
        float contentW = Math.Max(1f, w - KeyIconWidth);

        Im.Context.PushId(rowWidgetId);
        switch (slot.Kind)
        {
            case PropertyKind.Float:
            {
                float v = PropertyDispatcher.ReadFloat(_propertyWorld, slot);
                if (ImScalarInput.DrawAt("value", x, y, w, rightOverlayWidth: KeyIconWidth, ref v, float.MinValue, float.MaxValue, "0.##"))
                {
                    int widgetId = Im.Context.GetId("value");
                    bool isEditing = Im.Context.IsActive(widgetId) || Im.Context.IsFocused(widgetId);
                    _commands.SetPropertyValue(widgetId, isEditing, slot, PropertyValue.FromFloat(v));
                }
                else
                {
                    int widgetId = Im.Context.GetId("value");
                    bool isEditing = Im.Context.IsActive(widgetId) || Im.Context.IsFocused(widgetId);
                    _commands.NotifyPropertyWidgetState(widgetId, isEditing);
                }
                break;
            }
            case PropertyKind.Int:
            {
                int v = PropertyDispatcher.ReadInt(_propertyWorld, slot);
                if (ImScalarInput.DrawIntAt(label: string.Empty, id: "value", x, y, labelWidth: 0f, inputWidth: w, rightOverlayWidth: KeyIconWidth, ref v, int.MinValue, int.MaxValue))
                {
                    int widgetId = Im.Context.GetId("value");
                    bool isEditing = Im.Context.IsActive(widgetId) || Im.Context.IsFocused(widgetId);
                    _commands.SetPropertyValue(widgetId, isEditing, slot, PropertyValue.FromInt(v));
                }
                else
                {
                    int widgetId = Im.Context.GetId("value");
                    bool isEditing = Im.Context.IsActive(widgetId) || Im.Context.IsFocused(widgetId);
                    _commands.NotifyPropertyWidgetState(widgetId, isEditing);
                }
                break;
            }
            case PropertyKind.Bool:
            {
                bool v = PropertyDispatcher.ReadBool(_propertyWorld, slot);
                float checkboxX = x + Im.Style.Padding;
                float checkboxY = y + (h - Im.Style.CheckboxSize) * 0.5f;
                if (Im.Checkbox("value", ref v, checkboxX, checkboxY))
                {
                    int widgetId = Im.Context.GetId("value");
                    _commands.SetPropertyValue(widgetId, isEditing: false, slot, PropertyValue.FromBool(v));
                }
                else
                {
                    int widgetId = Im.Context.GetId("value");
                    _commands.NotifyPropertyWidgetState(widgetId, isEditing: false);
                }
                break;
            }
            case PropertyKind.Vec2:
            {
                Vector2 v = PropertyDispatcher.ReadVec2(_propertyWorld, slot);
                if (ImVectorInput.DrawAt("value", x, y, w, rightOverlayWidth: KeyIconWidth, ref v, float.MinValue, float.MaxValue, "F2"))
                {
                    bool isEditing = Im.Context.AnyActive;
                    int widgetId = Im.Context.GetId("value");
                    _commands.SetPropertyValue(widgetId, isEditing, slot, PropertyValue.FromVec2(v));
                }
                else
                {
                    int widgetId = Im.Context.GetId("value");
                    _commands.NotifyPropertyWidgetState(widgetId, isEditing: Im.Context.AnyActive);
                }
                break;
            }
            case PropertyKind.Vec3:
            {
                Vector3 v = PropertyDispatcher.ReadVec3(_propertyWorld, slot);
                if (ImVectorInput.DrawAt("value", x, y, w, rightOverlayWidth: KeyIconWidth, ref v, float.MinValue, float.MaxValue, "F2"))
                {
                    bool isEditing = Im.Context.AnyActive;
                    int widgetId = Im.Context.GetId("value");
                    _commands.SetPropertyValue(widgetId, isEditing, slot, PropertyValue.FromVec3(v));
                }
                else
                {
                    int widgetId = Im.Context.GetId("value");
                    _commands.NotifyPropertyWidgetState(widgetId, isEditing: Im.Context.AnyActive);
                }
                break;
            }
            case PropertyKind.Vec4:
            {
                Vector4 v = PropertyDispatcher.ReadVec4(_propertyWorld, slot);
                if (ImVectorInput.DrawAt("value", x, y, w, rightOverlayWidth: KeyIconWidth, ref v, float.MinValue, float.MaxValue, "F2"))
                {
                    bool isEditing = Im.Context.AnyActive;
                    int widgetId = Im.Context.GetId("value");
                    _commands.SetPropertyValue(widgetId, isEditing, slot, PropertyValue.FromVec4(v));
                }
                else
                {
                    int widgetId = Im.Context.GetId("value");
                    _commands.NotifyPropertyWidgetState(widgetId, isEditing: Im.Context.AnyActive);
                }
                break;
            }
            case PropertyKind.StringHandle:
            {
                TextPropertyEditState state = GetTextPropertyEditState(slot.PropertyId);
                StringHandle currentHandle = PropertyDispatcher.ReadStringHandle(_propertyWorld, slot);
                int inputWidgetId = Im.Context.GetId("value");
                bool wasFocused = Im.Context.IsFocused(inputWidgetId);
                if (!wasFocused && state.Handle != currentHandle)
                {
                    SetTextBufferFromHandle(state, currentHandle);
                }

                int length = state.Length;
                bool changed = Im.TextInput("value", state.Buffer, ref length, state.Buffer.Length, x, y, contentW);
                if (changed)
                {
                    StringHandle newHandle = new string(state.Buffer, 0, length);
                    bool isEditing = Im.Context.IsFocused(inputWidgetId);
                    _commands.SetPropertyValue(inputWidgetId, isEditing, slot, PropertyValue.FromStringHandle(newHandle));
                    state.Handle = newHandle;
                }

                state.Length = length;
                _commands.NotifyPropertyWidgetState(inputWidgetId, isEditing: Im.Context.IsFocused(inputWidgetId));
                break;
            }
            case PropertyKind.Color32:
            {
                _colorPicker.DrawColor32PreviewRow(inputRect, pickerId: "color", slot, showOpacity: true);
                break;
            }
            default:
            {
                float textY = y + (h - Im.Style.FontSize) * 0.5f;
                Im.Text("—".AsSpan(), x + Im.Style.Padding, textY, Im.Style.FontSize, Im.Style.TextSecondary);
                _commands.NotifyPropertyWidgetState(rowWidgetId, isEditing: false);
                break;
            }
        }
        Im.Context.PopId();
    }

    private static void WriteHexByte(Span<char> dst, byte value)
    {
        const string digits = "0123456789ABCDEF";
        dst[0] = digits[(value >> 4) & 0xF];
        dst[1] = digits[value & 0xF];
    }

    private static void DrawVec2Text(Vector2 v, float x, float y, uint color)
    {
        Span<char> buffer = stackalloc char[64];
        int written = 0;
        written += WriteFloat(buffer[written..], v.X);
        buffer[written++] = ',';
        buffer[written++] = ' ';
        written += WriteFloat(buffer[written..], v.Y);
        Im.Text(buffer[..written], x, y, Im.Style.FontSize, color);
    }

    private static void DrawVec3Text(Vector3 v, float x, float y, uint color)
    {
        Span<char> buffer = stackalloc char[96];
        int written = 0;
        written += WriteFloat(buffer[written..], v.X);
        buffer[written++] = ',';
        buffer[written++] = ' ';
        written += WriteFloat(buffer[written..], v.Y);
        buffer[written++] = ',';
        buffer[written++] = ' ';
        written += WriteFloat(buffer[written..], v.Z);
        Im.Text(buffer[..written], x, y, Im.Style.FontSize, color);
    }

    private static void DrawVec4Text(Vector4 v, float x, float y, uint color)
    {
        Span<char> buffer = stackalloc char[128];
        int written = 0;
        written += WriteFloat(buffer[written..], v.X);
        buffer[written++] = ',';
        buffer[written++] = ' ';
        written += WriteFloat(buffer[written..], v.Y);
        buffer[written++] = ',';
        buffer[written++] = ' ';
        written += WriteFloat(buffer[written..], v.Z);
        buffer[written++] = ',';
        buffer[written++] = ' ';
        written += WriteFloat(buffer[written..], v.W);
        Im.Text(buffer[..written], x, y, Im.Style.FontSize, color);
    }

    private static int WriteFloat(Span<char> dst, float v)
    {
        if (!v.TryFormat(dst, out int written, "0.##"))
        {
            return 0;
        }
        return written;
    }

    private bool TryGetPrefabPropertyBindingForSlot(in PropertySlot slot, out PrefabPropertyBinding binding)
    {
        binding = default;

        EntityId targetEntity = _gizmoTargetEntity;
        if (targetEntity.IsNull)
        {
            return false;
        }

        if (_workspace.World.HasComponent(targetEntity, PrefabExpandedComponent.Api.PoolIdConst))
        {
            return false;
        }

        EntityId owningPrefab = _workspace.FindOwningPrefabEntity(targetEntity);
        if (owningPrefab.IsNull || _workspace.World.GetNodeType(owningPrefab) != UiNodeType.Prefab)
        {
            return false;
        }

        if (!_workspace.World.TryGetComponent(owningPrefab, PrefabBindingsComponent.Api.PoolIdConst, out AnyComponentHandle bindingsAny) || !bindingsAny.IsValid)
        {
            return false;
        }

        uint targetStableId = _workspace.World.GetStableId(targetEntity);
        if (targetStableId == 0)
        {
            return false;
        }

        var bindingsHandle = new PrefabBindingsComponentHandle(bindingsAny.Index, bindingsAny.Generation);
        var bindings = PrefabBindingsComponent.Api.FromHandle(_propertyWorld, bindingsHandle);
        if (!bindings.IsAlive || bindings.BindingCount == 0)
        {
            return false;
        }

        ushort bindingCount = bindings.BindingCount;
        if (bindingCount > PrefabBindingsComponent.MaxBindings)
        {
            bindingCount = PrefabBindingsComponent.MaxBindings;
        }

        ushort boundVariableId = 0;
        byte boundDirection = 0;
        ReadOnlySpan<uint> targetNode = bindings.TargetSourceNodeStableIdReadOnlySpan();
        ReadOnlySpan<ushort> targetComponent = bindings.TargetComponentKindReadOnlySpan();
        ReadOnlySpan<ulong> propertyId = bindings.PropertyIdReadOnlySpan();
        ReadOnlySpan<ushort> varId = bindings.VariableIdReadOnlySpan();
        ReadOnlySpan<byte> direction = bindings.DirectionReadOnlySpan();

        for (int i = 0; i < bindingCount; i++)
        {
            if (targetNode[i] == targetStableId && targetComponent[i] == slot.Component.Kind && propertyId[i] == slot.PropertyId)
            {
                boundVariableId = varId[i];
                boundDirection = direction[i];
                break;
            }
        }

        if (boundVariableId == 0)
        {
            return false;
        }

        StringHandle variableName = default;

        if (_workspace.World.TryGetComponent(owningPrefab, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) && varsAny.IsValid)
        {
            var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
            var vars = PrefabVariablesComponent.Api.FromHandle(_propertyWorld, varsHandle);
            if (vars.IsAlive && vars.VariableCount > 0)
            {
                ushort count = vars.VariableCount;
                if (count > PrefabVariablesComponent.MaxVariables)
                {
                    count = (ushort)PrefabVariablesComponent.MaxVariables;
                }

                ReadOnlySpan<ushort> ids = vars.VariableIdReadOnlySpan();
                ReadOnlySpan<StringHandle> names = vars.NameReadOnlySpan();

                for (int i = 0; i < count; i++)
                {
                    if (ids[i] == boundVariableId)
                    {
                        variableName = names[i];
                        break;
                    }
                }
            }
        }

        binding = new PrefabPropertyBinding(owningPrefab, boundVariableId, boundDirection, variableName);
        return true;
    }

    private bool TryDrawTextOverflowDropdown(AnyComponentHandle component, PropertyUiItem item, PropertySlot slot, float labelWidth, float inputWidth)
    {
        if (component.Kind != TextComponent.Api.PoolIdConst)
        {
            return false;
        }

        if (item.Info.Group != _textGroupHandle || item.Info.Name != _textOverflowLabelHandle)
        {
            return false;
        }

        DrawIntDropdownProperty(item, slot, labelWidth, inputWidth, TextOverflowOptions, minValue: 0, maxValue: 4);
        return true;
    }

    private bool TryDrawTextAlignXIcons(AnyComponentHandle component, PropertyUiItem item, PropertySlot slot, float labelWidth, float inputWidth)
    {
        if (component.Kind != TextComponent.Api.PoolIdConst)
        {
            return false;
        }

        if (item.Info.Group != _textGroupHandle || item.Info.Name != _textAlignXLabelHandle)
        {
            return false;
        }

        DrawTextAlignIcons(item, slot, labelWidth, inputWidth, axis: 0, minValue: 0, maxValue: 2);
        return true;
    }

    private bool TryDrawTextAlignYIcons(AnyComponentHandle component, PropertyUiItem item, PropertySlot slot, float labelWidth, float inputWidth)
    {
        if (component.Kind != TextComponent.Api.PoolIdConst)
        {
            return false;
        }

        if (item.Info.Group != _textGroupHandle || item.Info.Name != _textAlignYLabelHandle)
        {
            return false;
        }

        DrawTextAlignIcons(item, slot, labelWidth, inputWidth, axis: 1, minValue: 0, maxValue: 2);
        return true;
    }

    private void DrawTextAlignIcons(PropertyUiItem item, PropertySlot slot, float labelWidth, float inputWidth, int axis, int minValue, int maxValue)
    {
        int current = PropertyDispatcher.ReadInt(_propertyWorld, slot);
        int selected = Math.Clamp(current, minValue, maxValue);

        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = GetPaddedRowRect(rect);

        float fontSize = Im.Style.FontSize;
        float textY = rect.Y + (rect.Height - fontSize) * 0.5f;
        Im.Text(item.Label.AsSpan(), rect.X, textY, fontSize, Im.Style.TextPrimary);

        float inputX = rect.X + labelWidth;
        bool hovered = rect.Contains(Im.MousePos);
        TryDrawPropertyBindingContextMenu(slot, new ImRect(inputX, rect.Y, inputWidth, rect.Height), allowUnbind: true);

        float contentWidth = Math.Max(1f, inputWidth - KeyIconWidth);
        var inputRect = new ImRect(inputX, rect.Y, contentWidth, rect.Height);

        int baseWidgetId = Im.Context.GetId(item.WidgetId);
        var ctx = Im.Context;
        var input = ctx.Input;

        float segmentSpacing = Math.Max(1f, Im.Style.Spacing * 0.5f);
        float segmentWidth = (inputRect.Width - segmentSpacing * 2f) / 3f;
        if (segmentWidth < 1f)
        {
            segmentWidth = 1f;
            segmentSpacing = 0f;
        }

        bool changed = false;
        for (int i = 0; i < 3; i++)
        {
            float x = inputRect.X + i * (segmentWidth + segmentSpacing);
            var segRect = new ImRect(x, inputRect.Y, segmentWidth, inputRect.Height);
            int segId = baseWidgetId * 31 + i;

            bool segHovered = segRect.Contains(Im.MousePos);
            if (segHovered)
            {
                ctx.SetHot(segId);
            }

            if (ctx.IsHot(segId) && input.MousePressed)
            {
                ctx.SetActive(segId);
            }

            bool pressed = false;
            if (ctx.IsActive(segId))
            {
                if (input.MouseReleased)
                {
                    if (ctx.IsHot(segId))
                    {
                        pressed = true;
                    }
                    ctx.ClearActive();
                }
            }

            bool isSelected = selected == i;
            uint bg = isSelected ? Im.Style.Active : (ctx.IsActive(segId) ? Im.Style.Active : (ctx.IsHot(segId) ? Im.Style.Hover : Im.Style.Surface));
            uint border = isSelected ? Im.Style.Primary : Im.Style.Border;
            Im.DrawRoundedRect(segRect.X, segRect.Y, segRect.Width, segRect.Height, Im.Style.CornerRadius, bg);
            Im.DrawRoundedRectStroke(segRect.X, segRect.Y, segRect.Width, segRect.Height, Im.Style.CornerRadius, border, Im.Style.BorderWidth);

            uint iconColor = isSelected ? Im.Style.TextPrimary : Im.Style.TextSecondary;
            if (axis == 0)
            {
                DrawAlignIcon(segRect, TextAlignXIconLabels[i], iconColor);
            }
            else
            {
                DrawAlignIcon(segRect, TextAlignYIconLabels[i], iconColor);
            }

            if (pressed && selected != i)
            {
                selected = i;
                changed = true;
            }
        }

        int widgetId = baseWidgetId;
        if (changed)
        {
            _commands.SetPropertyValue(widgetId, isEditing: false, slot, PropertyValue.FromInt(selected));
            ReportGeneratedGizmoSingle(item, slot, hovered, false);
        }
        else
        {
            _commands.NotifyPropertyWidgetState(widgetId, isEditing: false);
            ReportGeneratedGizmoSingle(item, slot, hovered, false);
        }

        DrawKeyIcon(slot, new ImRect(inputRect.Right, rect.Y, KeyIconWidth, rect.Height));
    }

    private static readonly string[] TextAlignXIconLabels =
    [
        ((char)IconChar.AlignLeft).ToString(),
        ((char)IconChar.AlignCenter).ToString(),
        ((char)IconChar.AlignRight).ToString(),
    ];

    private static readonly string[] TextAlignYIconLabels =
    [
        ((char)IconChar.ArrowUp).ToString(),
        ((char)IconChar.ArrowsUpDown).ToString(),
        ((char)IconChar.ArrowDown).ToString(),
    ];

    private static void DrawAlignIcon(ImRect rect, string icon, uint color)
    {
        float fontSize = Im.Style.FontSize;
        float iconWidth = Im.MeasureTextWidth(icon.AsSpan(), fontSize);
        float x = rect.X + (rect.Width - iconWidth) * 0.5f;
        float y = rect.Y + (rect.Height - fontSize) * 0.5f;
        Im.Text(icon.AsSpan(), x, y, fontSize, color);
    }

    private bool TryDrawTextFontDropdown(AnyComponentHandle component, PropertyUiItem item, PropertySlot slot, float labelWidth, float inputWidth)
    {
        if (component.Kind != TextComponent.Api.PoolIdConst)
        {
            return false;
        }

        if (item.Info.Group != _textGroupHandle || item.Info.Name != _textFontLabelHandle)
        {
            return false;
        }

        StringHandle current = PropertyDispatcher.ReadStringHandle(_propertyWorld, slot);
        string currentName = current.IsValid ? current.ToString() : string.Empty;

        int selected = 0;
        for (int i = 0; i < TextFontOptions.Length; i++)
        {
            if (string.Equals(TextFontOptions[i], currentName, StringComparison.OrdinalIgnoreCase))
            {
                selected = i;
                break;
            }
        }

        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = GetPaddedRowRect(rect);

        float fontSize = Im.Style.FontSize;
        float textY = rect.Y + (rect.Height - fontSize) * 0.5f;
        Im.Text(item.Label.AsSpan(), rect.X, textY, fontSize, Im.Style.TextPrimary);

        float inputX = rect.X + labelWidth;
        bool hovered = rect.Contains(Im.MousePos);
        TryDrawPropertyBindingContextMenu(slot, new ImRect(inputX, rect.Y, inputWidth, rect.Height), allowUnbind: true);
        if (Im.Dropdown(item.WidgetId, TextFontOptions, ref selected, inputX, rect.Y, inputWidth, KeyIconWidth))
        {
            int widgetId = Im.Context.GetId(item.WidgetId);
            StringHandle handle = TextFontOptions[selected];
            _commands.SetPropertyValue(widgetId, isEditing: false, slot, PropertyValue.FromStringHandle(handle));
            ReportGeneratedGizmoSingle(item, slot, hovered, false);
        }
        else
        {
            int widgetId = Im.Context.GetId(item.WidgetId);
            _commands.NotifyPropertyWidgetState(widgetId, isEditing: false);
            ReportGeneratedGizmoSingle(item, slot, hovered, false);
        }

        DrawKeyIcon(slot, new ImRect(inputX, rect.Y, inputWidth, rect.Height));
        return true;
    }

    private void DrawIntDropdownProperty(PropertyUiItem item, PropertySlot slot, float labelWidth, float inputWidth, string[] options, int minValue, int maxValue)
    {
        int current = PropertyDispatcher.ReadInt(_propertyWorld, slot);
        int selected = Math.Clamp(current, minValue, maxValue);

        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = GetPaddedRowRect(rect);

        float fontSize = Im.Style.FontSize;
        float textY = rect.Y + (rect.Height - fontSize) * 0.5f;
        Im.Text(item.Label.AsSpan(), rect.X, textY, fontSize, Im.Style.TextPrimary);

        float inputX = rect.X + labelWidth;
        bool hovered = rect.Contains(Im.MousePos);
        TryDrawPropertyBindingContextMenu(slot, new ImRect(inputX, rect.Y, inputWidth, rect.Height), allowUnbind: true);
        if (Im.Dropdown(item.WidgetId, options, ref selected, inputX, rect.Y, inputWidth, KeyIconWidth))
        {
            int widgetId = Im.Context.GetId(item.WidgetId);
            _commands.SetPropertyValue(widgetId, isEditing: false, slot, PropertyValue.FromInt(selected));
            ReportGeneratedGizmoSingle(item, slot, hovered, false);
        }
        else
        {
            int widgetId = Im.Context.GetId(item.WidgetId);
            _commands.NotifyPropertyWidgetState(widgetId, isEditing: false);
            ReportGeneratedGizmoSingle(item, slot, hovered, false);
        }

        DrawKeyIcon(slot, new ImRect(inputX, rect.Y, inputWidth, rect.Height));
    }

    private bool TryDrawEventListenerVariableBindingDropdown(
        AnyComponentHandle component,
        PropertyUiItem item,
        PropertySlot slot,
        float labelWidth,
        float inputWidth,
        in PrefabOverrideUiState prefabOverrideState)
    {
        if (component.Kind != EventListenerComponent.Api.PoolIdConst)
        {
            return false;
        }

        if (_gizmoTargetEntity.IsNull)
        {
            return false;
        }

        if (slot.Kind != PropertyKind.Int)
        {
            return false;
        }

        PropertyKind expectedKind = item.Index0 == 0 || item.Index0 == 6 ? PropertyKind.Bool : PropertyKind.Trigger;

        EntityId owningPrefab = _workspace.FindOwningPrefabEntity(_gizmoTargetEntity);
        if (owningPrefab.IsNull)
        {
            return false;
        }

        EnsureEventListenerDropdownCapacity(PrefabVariablesComponent.MaxVariables + 8);

        int optionCount = 0;
        _eventListenerVariableDropdownOptions[optionCount] = "(None)";
        _eventListenerVariableDropdownOptionIds[optionCount] = 0;
        optionCount++;

        if (_workspace.World.TryGetComponent(owningPrefab, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) && varsAny.IsValid)
        {
            var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
            var vars = PrefabVariablesComponent.Api.FromHandle(_propertyWorld, varsHandle);
            if (vars.IsAlive)
            {
                ushort count = vars.VariableCount;
                if (count > PrefabVariablesComponent.MaxVariables)
                {
                    count = PrefabVariablesComponent.MaxVariables;
                }

                ReadOnlySpan<ushort> ids = vars.VariableIdReadOnlySpan();
                ReadOnlySpan<int> kinds = vars.KindReadOnlySpan();
                ReadOnlySpan<StringHandle> names = vars.NameReadOnlySpan();

                for (int i = 0; i < count; i++)
                {
                    if ((PropertyKind)kinds[i] != expectedKind)
                    {
                        continue;
                    }

                    ushort id = ids[i];
                    StringHandle nameHandle = names[i];
                    string name = nameHandle.IsValid ? nameHandle.ToString() : string.Empty;
                    if (string.IsNullOrEmpty(name))
                    {
                        name = "Var " + id;
                    }
                    else
                    {
                        name = name + " (" + id + ")";
                    }

                    _eventListenerVariableDropdownOptions[optionCount] = name;
                    _eventListenerVariableDropdownOptionIds[optionCount] = id;
                    optionCount++;

                    if (optionCount >= _eventListenerVariableDropdownOptions.Length)
                    {
                        break;
                    }
                }
            }
        }

        int current = PropertyDispatcher.ReadInt(_propertyWorld, slot);
        ushort currentId = current <= 0 ? (ushort)0 : current >= ushort.MaxValue ? ushort.MaxValue : (ushort)current;

        int selectedIndex = 0;
        for (int i = 0; i < optionCount; i++)
        {
            if (_eventListenerVariableDropdownOptionIds[i] == currentId)
            {
                selectedIndex = i;
                break;
            }
        }

        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = GetPaddedRowRect(rect);
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(item.Label.AsSpan(), rect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        var inputRect = new ImRect(rect.X + labelWidth, rect.Y, inputWidth, rect.Height);
        bool hovered = rect.Contains(Im.MousePos);

        bool changed = Im.Dropdown(
            item.WidgetId,
            new ReadOnlySpan<string>(_eventListenerVariableDropdownOptions, 0, optionCount),
            ref selectedIndex,
            inputRect.X,
            inputRect.Y,
            inputRect.Width,
            rightOverlayWidth: 0f);

        int widgetId = Im.Context.GetId(item.WidgetId);
        if (changed)
        {
            ushort nextId = selectedIndex <= 0 ? (ushort)0 : _eventListenerVariableDropdownOptionIds[selectedIndex];
            _commands.SetPropertyValue(widgetId, isEditing: false, slot, PropertyValue.FromInt(nextId));
        }
        else
        {
            _commands.NotifyPropertyWidgetState(widgetId, isEditing: false);
        }

        ReportGeneratedGizmoSingle(item, slot, hovered, false);
        DrawPrefabOverrideBorderIfNeeded(inputRect, in prefabOverrideState);
        return true;
    }

    private static void EnsureEventListenerDropdownCapacity(int required)
    {
        if (_eventListenerVariableDropdownOptions.Length >= required &&
            _eventListenerVariableDropdownOptionIds.Length >= required)
        {
            return;
        }

        int next = Math.Max(required, _eventListenerVariableDropdownOptions.Length * 2);
        Array.Resize(ref _eventListenerVariableDropdownOptions, next);
        Array.Resize(ref _eventListenerVariableDropdownOptionIds, next);
    }

    private bool TryDrawStrokeCapDropdown(AnyComponentHandle component, PropertyUiItem item, PropertySlot slot, float labelWidth, float inputWidth)
    {
        if (component.Kind != StrokeComponent.Api.PoolIdConst)
        {
            return false;
        }

        bool isCap = item.Info.Name == _capLabelHandle;
        bool isStrokeCapGroup = item.Info.Group == _trimGroupHandle || item.Info.Group == _dashGroupHandle;
        if (!isCap || !isStrokeCapGroup)
        {
            return false;
        }

        int widgetId = Im.Context.GetId(item.WidgetId);
        int selectedIndex = PropertyDispatcher.ReadInt(_propertyWorld, slot);
        if ((uint)selectedIndex >= (uint)StrokeCapOptions.Length)
        {
            selectedIndex = 0;
        }

        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = GetPaddedRowRect(rect);
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(item.Label.AsSpan(), rect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        var inputRect = new ImRect(rect.X + labelWidth, rect.Y, inputWidth, rect.Height);
        float overlayWidth = GetDropdownRightOverlayWidth(slot);
        if (Im.Dropdown(item.WidgetId, StrokeCapOptions, ref selectedIndex, inputRect.X, inputRect.Y, inputRect.Width, overlayWidth))
        {
            _commands.SetPropertyValue(widgetId, isEditing: false, slot, PropertyValue.FromInt(selectedIndex));
        }

        _commands.NotifyPropertyWidgetState(widgetId, isEditing: false);
        DrawKeyIcon(slot, inputRect);
        return true;
    }

    private bool TryDrawStrokeCapDropdownMulti(AnyComponentHandle component0, ReadOnlySpan<AnyComponentHandle> components, PropertyUiItem item, PropertySlot slot0, float labelWidth, float inputWidth)
    {
        if (component0.Kind != StrokeComponent.Api.PoolIdConst)
        {
            return false;
        }

        bool isCap = item.Info.Name == _capLabelHandle;
        bool isStrokeCapGroup = item.Info.Group == _trimGroupHandle || item.Info.Group == _dashGroupHandle;
        if (!isCap || !isStrokeCapGroup)
        {
            return false;
        }

        int first = PropertyDispatcher.ReadInt(_propertyWorld, slot0);
        if ((uint)first >= (uint)StrokeCapOptions.Length)
        {
            first = 0;
        }

        bool mixed = false;
        for (int i = 1; i < components.Length; i++)
        {
            if (!TryResolveSlotForMulti(components[i], slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out PropertySlot slot))
            {
                return false;
            }

            int current = PropertyDispatcher.ReadInt(_propertyWorld, slot);
            if ((uint)current >= (uint)StrokeCapOptions.Length)
            {
                current = 0;
            }
            mixed |= current != first;
        }

        int widgetId = Im.Context.GetId(item.WidgetId);
        int selectedIndex = mixed ? -1 : first;

        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = GetPaddedRowRect(rect);
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(item.Label.AsSpan(), rect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        var inputRect = new ImRect(rect.X + labelWidth, rect.Y, inputWidth, rect.Height);
        if (Im.Dropdown(item.WidgetId, StrokeCapOptions, ref selectedIndex, inputRect.X, inputRect.Y, inputRect.Width, rightOverlayWidth: 0f))
        {
            EnsureMultiScratchCapacity(components.Length);
            _multiSlotsScratch[0] = slot0;
            for (int i = 1; i < components.Length; i++)
            {
                if (!TryResolveSlotForMulti(components[i], slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out PropertySlot slot))
                {
                    return false;
                }
                _multiSlotsScratch[i] = slot;
            }

            _multiValuesScratch[0] = PropertyValue.FromInt(selectedIndex);
            _commands.SetPropertyValuesMany(widgetId, isEditing: false, _multiSlotsScratch.AsSpan(0, components.Length), _multiValuesScratch.AsSpan(0, 1));
        }
        else
        {
            _commands.NotifyPropertyWidgetState(widgetId, isEditing: false);
        }

        return true;
    }

    private bool TryDrawBlendModeDropdown(AnyComponentHandle component, PropertyUiItem item, PropertySlot slot, float labelWidth, float inputWidth)
    {
        if (component.Kind != BlendComponent.Api.PoolIdConst)
        {
            return false;
        }

        if (item.Info.Name != _blendModeLabelHandle)
        {
            return false;
        }

        int widgetId = Im.Context.GetId(item.WidgetId);
        int selectedIndex = PropertyDispatcher.ReadInt(_propertyWorld, slot);
        if ((uint)selectedIndex >= (uint)BlendModeOptions.Length)
        {
            selectedIndex = 0;
        }

        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = GetPaddedRowRect(rect);
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(item.Label.AsSpan(), rect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        var inputRect = new ImRect(rect.X + labelWidth, rect.Y, inputWidth, rect.Height);
        float overlayWidth = GetDropdownRightOverlayWidth(slot);
        if (Im.Dropdown(item.WidgetId, BlendModeOptions, ref selectedIndex, inputRect.X, inputRect.Y, inputRect.Width, overlayWidth))
        {
            _commands.SetPropertyValue(widgetId, isEditing: false, slot, PropertyValue.FromInt(selectedIndex));
        }

        _commands.NotifyPropertyWidgetState(widgetId, isEditing: false);
        DrawKeyIcon(slot, inputRect);
        return true;
    }

    private bool TryDrawBlendModeDropdownMulti(AnyComponentHandle component0, ReadOnlySpan<AnyComponentHandle> components, PropertyUiItem item, PropertySlot slot0, float labelWidth, float inputWidth)
    {
        if (component0.Kind != BlendComponent.Api.PoolIdConst)
        {
            return false;
        }

        if (item.Info.Name != _blendModeLabelHandle)
        {
            return false;
        }

        int first = PropertyDispatcher.ReadInt(_propertyWorld, slot0);
        if ((uint)first >= (uint)BlendModeOptions.Length)
        {
            first = 0;
        }

        bool mixed = false;
        for (int i = 1; i < components.Length; i++)
        {
            if (!TryResolveSlotForMulti(components[i], slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out PropertySlot slot))
            {
                return false;
            }

            int current = PropertyDispatcher.ReadInt(_propertyWorld, slot);
            if ((uint)current >= (uint)BlendModeOptions.Length)
            {
                current = 0;
            }
            mixed |= current != first;
        }

        int widgetId = Im.Context.GetId(item.WidgetId);
        int selectedIndex = mixed ? -1 : first;

        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = GetPaddedRowRect(rect);
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(item.Label.AsSpan(), rect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        var inputRect = new ImRect(rect.X + labelWidth, rect.Y, inputWidth, rect.Height);
        if (Im.Dropdown(item.WidgetId, BlendModeOptions, ref selectedIndex, inputRect.X, inputRect.Y, inputRect.Width, rightOverlayWidth: 0f))
        {
            EnsureMultiScratchCapacity(components.Length);
            _multiSlotsScratch[0] = slot0;
            for (int i = 1; i < components.Length; i++)
            {
                if (!TryResolveSlotForMulti(components[i], slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out PropertySlot slot))
                {
                    return false;
                }
                _multiSlotsScratch[i] = slot;
            }

            _multiValuesScratch[0] = PropertyValue.FromInt(selectedIndex);
            _commands.SetPropertyValuesMany(widgetId, isEditing: false, _multiSlotsScratch.AsSpan(0, components.Length), _multiValuesScratch.AsSpan(0, 1));
        }
        else
        {
            _commands.NotifyPropertyWidgetState(widgetId, isEditing: false);
        }

        return true;
    }

    private bool TryDrawLayoutContainerDropdown(AnyComponentHandle component, PropertyUiItem item, PropertySlot slot, float labelWidth, float inputWidth)
    {
        if (component.Kind != TransformComponent.Api.PoolIdConst)
        {
            return false;
        }

        if (item.Info.Group != _layoutContainerGroupHandle)
        {
            return false;
        }

        string[]? options = null;
        if (item.Info.Name == _layoutContainerLayoutLabelHandle)
        {
            options = LayoutTypeOptions;
        }
        else if (item.Info.Name == _layoutContainerDirectionLabelHandle)
        {
            options = LayoutDirectionOptions;
        }
        else if (item.Info.Name == _layoutContainerAlignItemsLabelHandle)
        {
            options = LayoutAlignItemsOptions;
        }
        else if (item.Info.Name == _layoutContainerJustifyLabelHandle)
        {
            options = LayoutJustifyOptions;
        }
        else if (item.Info.Name == _layoutContainerWidthModeLabelHandle || item.Info.Name == _layoutContainerHeightModeLabelHandle)
        {
            options = LayoutSizeModeOptions;
        }

        if (options == null)
        {
            return false;
        }

        int widgetId = Im.Context.GetId(item.WidgetId);
        int selectedIndex = PropertyDispatcher.ReadInt(_propertyWorld, slot);
        if ((uint)selectedIndex >= (uint)options.Length)
        {
            selectedIndex = 0;
        }

        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = GetPaddedRowRect(rect);
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(item.Label.AsSpan(), rect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        var inputRect = new ImRect(rect.X + labelWidth, rect.Y, inputWidth, rect.Height);
        float overlayWidth = GetDropdownRightOverlayWidth(slot);
        if (Im.Dropdown(item.WidgetId, options, ref selectedIndex, inputRect.X, inputRect.Y, inputRect.Width, overlayWidth))
        {
            _commands.SetPropertyValue(widgetId, isEditing: false, slot, PropertyValue.FromInt(selectedIndex));
        }

        _commands.NotifyPropertyWidgetState(widgetId, isEditing: false);
        DrawKeyIcon(slot, inputRect);
        return true;
    }

    private bool TryDrawLayoutContainerDropdownMulti(AnyComponentHandle component0, ReadOnlySpan<AnyComponentHandle> components, PropertyUiItem item, PropertySlot slot0, float labelWidth, float inputWidth)
    {
        if (component0.Kind != TransformComponent.Api.PoolIdConst)
        {
            return false;
        }

        if (item.Info.Group != _layoutContainerGroupHandle)
        {
            return false;
        }

        string[]? options = null;
        if (item.Info.Name == _layoutContainerLayoutLabelHandle)
        {
            options = LayoutTypeOptions;
        }
        else if (item.Info.Name == _layoutContainerDirectionLabelHandle)
        {
            options = LayoutDirectionOptions;
        }
        else if (item.Info.Name == _layoutContainerAlignItemsLabelHandle)
        {
            options = LayoutAlignItemsOptions;
        }
        else if (item.Info.Name == _layoutContainerJustifyLabelHandle)
        {
            options = LayoutJustifyOptions;
        }
        else if (item.Info.Name == _layoutContainerWidthModeLabelHandle || item.Info.Name == _layoutContainerHeightModeLabelHandle)
        {
            options = LayoutSizeModeOptions;
        }

        if (options == null)
        {
            return false;
        }

        int first = PropertyDispatcher.ReadInt(_propertyWorld, slot0);
        if ((uint)first >= (uint)options.Length)
        {
            first = 0;
        }

        bool mixed = false;
        for (int i = 1; i < components.Length; i++)
        {
            if (!TryResolveSlotForMulti(components[i], slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out PropertySlot slot))
            {
                return false;
            }

            int current = PropertyDispatcher.ReadInt(_propertyWorld, slot);
            if ((uint)current >= (uint)options.Length)
            {
                current = 0;
            }
            mixed |= current != first;
        }

        int widgetId = Im.Context.GetId(item.WidgetId);
        int selectedIndex = mixed ? -1 : first;

        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = GetPaddedRowRect(rect);
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(item.Label.AsSpan(), rect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        var inputRect = new ImRect(rect.X + labelWidth, rect.Y, inputWidth, rect.Height);
        if (Im.Dropdown(item.WidgetId, options, ref selectedIndex, inputRect.X, inputRect.Y, inputRect.Width, rightOverlayWidth: 0f))
        {
            EnsureMultiScratchCapacity(components.Length);
            _multiSlotsScratch[0] = slot0;
            for (int i = 1; i < components.Length; i++)
            {
                if (!TryResolveSlotForMulti(components[i], slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out PropertySlot slot))
                {
                    return false;
                }
                _multiSlotsScratch[i] = slot;
            }

            _multiValuesScratch[0] = PropertyValue.FromInt(selectedIndex);
            _commands.SetPropertyValuesMany(widgetId, isEditing: false, _multiSlotsScratch.AsSpan(0, components.Length), _multiValuesScratch.AsSpan(0, 1));
        }
        else
        {
            _commands.NotifyPropertyWidgetState(widgetId, isEditing: false);
        }

        return true;
    }

    private bool TryDrawLayoutChildDropdown(AnyComponentHandle component, PropertyUiItem item, PropertySlot slot, float labelWidth, float inputWidth)
    {
        if (component.Kind != TransformComponent.Api.PoolIdConst)
        {
            return false;
        }

        if (item.Info.Group != _layoutChildGroupHandle)
        {
            return false;
        }

        if (item.Info.Name != _layoutChildAlignSelfLabelHandle)
        {
            return false;
        }

        int widgetId = Im.Context.GetId(item.WidgetId);
        int selectedIndex = PropertyDispatcher.ReadInt(_propertyWorld, slot);
        if ((uint)selectedIndex >= (uint)LayoutAlignSelfOptions.Length)
        {
            selectedIndex = 0;
        }

        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = GetPaddedRowRect(rect);
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(item.Label.AsSpan(), rect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        var inputRect = new ImRect(rect.X + labelWidth, rect.Y, inputWidth, rect.Height);
        float overlayWidth = GetDropdownRightOverlayWidth(slot);
        if (Im.Dropdown(item.WidgetId, LayoutAlignSelfOptions, ref selectedIndex, inputRect.X, inputRect.Y, inputRect.Width, overlayWidth))
        {
            _commands.SetPropertyValue(widgetId, isEditing: false, slot, PropertyValue.FromInt(selectedIndex));
        }

        _commands.NotifyPropertyWidgetState(widgetId, isEditing: false);
        DrawKeyIcon(slot, inputRect);
        return true;
    }

    private bool TryDrawLayoutChildDropdownMulti(AnyComponentHandle component0, ReadOnlySpan<AnyComponentHandle> components, PropertyUiItem item, PropertySlot slot0, float labelWidth, float inputWidth)
    {
        if (component0.Kind != TransformComponent.Api.PoolIdConst)
        {
            return false;
        }

        if (item.Info.Group != _layoutChildGroupHandle)
        {
            return false;
        }

        if (item.Info.Name != _layoutChildAlignSelfLabelHandle)
        {
            return false;
        }

        int first = PropertyDispatcher.ReadInt(_propertyWorld, slot0);
        if ((uint)first >= (uint)LayoutAlignSelfOptions.Length)
        {
            first = 0;
        }

        bool mixed = false;
        for (int i = 1; i < components.Length; i++)
        {
            if (!TryResolveSlotForMulti(components[i], slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out PropertySlot slot))
            {
                return false;
            }

            int current = PropertyDispatcher.ReadInt(_propertyWorld, slot);
            if ((uint)current >= (uint)LayoutAlignSelfOptions.Length)
            {
                current = 0;
            }
            mixed |= current != first;
        }

        int widgetId = Im.Context.GetId(item.WidgetId);
        int selectedIndex = mixed ? -1 : first;

        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = GetPaddedRowRect(rect);
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(item.Label.AsSpan(), rect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        var inputRect = new ImRect(rect.X + labelWidth, rect.Y, inputWidth, rect.Height);
        if (Im.Dropdown(item.WidgetId, LayoutAlignSelfOptions, ref selectedIndex, inputRect.X, inputRect.Y, inputRect.Width, rightOverlayWidth: 0f))
        {
            EnsureMultiScratchCapacity(components.Length);
            _multiSlotsScratch[0] = slot0;
            for (int i = 1; i < components.Length; i++)
            {
                if (!TryResolveSlotForMulti(components[i], slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out PropertySlot slot))
                {
                    return false;
                }
                _multiSlotsScratch[i] = slot;
            }

            _multiValuesScratch[0] = PropertyValue.FromInt(selectedIndex);
            _commands.SetPropertyValuesMany(widgetId, isEditing: false, _multiSlotsScratch.AsSpan(0, components.Length), _multiValuesScratch.AsSpan(0, 1));
        }
        else
        {
            _commands.NotifyPropertyWidgetState(widgetId, isEditing: false);
        }

        return true;
    }

    private bool TryDrawBooleanOperationDropdown(AnyComponentHandle component, PropertyUiItem item, PropertySlot slot, float labelWidth, float inputWidth)
    {
        if (component.Kind != BooleanGroupComponent.Api.PoolIdConst)
        {
            return false;
        }

        if (item.Info.Name != _booleanOperationLabelHandle)
        {
            return false;
        }

        int widgetId = Im.Context.GetId(item.WidgetId);
        int selectedIndex = PropertyDispatcher.ReadInt(_propertyWorld, slot);
        if (selectedIndex < 0)
        {
            selectedIndex = 0;
        }
        else if (selectedIndex >= UiWorkspace.BooleanOpOptions.Length)
        {
            selectedIndex = UiWorkspace.BooleanOpOptions.Length - 1;
        }

        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = GetPaddedRowRect(rect);
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(item.Label.AsSpan(), rect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        var inputRect = new ImRect(rect.X + labelWidth, rect.Y, inputWidth, rect.Height);
        float overlayWidth = GetDropdownRightOverlayWidth(slot);
        if (Im.Dropdown(item.WidgetId, UiWorkspace.BooleanOpOptions, ref selectedIndex, inputRect.X, inputRect.Y, inputRect.Width, overlayWidth))
        {
            _commands.SetPropertyValue(widgetId, isEditing: false, slot, PropertyValue.FromInt(selectedIndex));
        }

        _commands.NotifyPropertyWidgetState(widgetId, isEditing: false);
        DrawKeyIcon(slot, inputRect);
        return true;
    }

    private bool TryDrawBooleanOperationDropdownMulti(AnyComponentHandle component0, ReadOnlySpan<AnyComponentHandle> components, PropertyUiItem item, PropertySlot slot0, float labelWidth, float inputWidth)
    {
        if (component0.Kind != BooleanGroupComponent.Api.PoolIdConst)
        {
            return false;
        }

        if (item.Info.Name != _booleanOperationLabelHandle)
        {
            return false;
        }

        int first = PropertyDispatcher.ReadInt(_propertyWorld, slot0);
        if (first < 0)
        {
            first = 0;
        }
        else if (first >= UiWorkspace.BooleanOpOptions.Length)
        {
            first = UiWorkspace.BooleanOpOptions.Length - 1;
        }

        bool mixed = false;
        for (int i = 1; i < components.Length; i++)
        {
            if (!TryResolveSlotForMulti(components[i], slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out PropertySlot slot))
            {
                return false;
            }

            int current = PropertyDispatcher.ReadInt(_propertyWorld, slot);
            if (current < 0)
            {
                current = 0;
            }
            else if (current >= UiWorkspace.BooleanOpOptions.Length)
            {
                current = UiWorkspace.BooleanOpOptions.Length - 1;
            }
            mixed |= current != first;
        }

        int widgetId = Im.Context.GetId(item.WidgetId);
        int selectedIndex = mixed ? -1 : first;

        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = GetPaddedRowRect(rect);
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(item.Label.AsSpan(), rect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        var inputRect = new ImRect(rect.X + labelWidth, rect.Y, inputWidth, rect.Height);
        if (Im.Dropdown(item.WidgetId, UiWorkspace.BooleanOpOptions, ref selectedIndex, inputRect.X, inputRect.Y, inputRect.Width, rightOverlayWidth: 0f))
        {
            EnsureMultiScratchCapacity(components.Length);
            _multiSlotsScratch[0] = slot0;
            for (int i = 1; i < components.Length; i++)
            {
                if (!TryResolveSlotForMulti(components[i], slot0.PropertyIndex, slot0.PropertyId, slot0.Kind, out PropertySlot slot))
                {
                    return false;
                }
                _multiSlotsScratch[i] = slot;
            }

            _multiValuesScratch[0] = PropertyValue.FromInt(selectedIndex);
            _commands.SetPropertyValuesMany(widgetId, isEditing: false, _multiSlotsScratch.AsSpan(0, components.Length), _multiValuesScratch.AsSpan(0, 1));
        }
        else
        {
            _commands.NotifyPropertyWidgetState(widgetId, isEditing: false);
        }

        return true;
    }

    private float GetDropdownRightOverlayWidth(PropertySlot slot)
    {
        if (!_commands.TryGetKeyableState(slot, out _, out _))
        {
            return 0f;
        }

        return KeyIconWidth;
    }

    private void DrawFloatProperty(PropertyUiItem item, PropertySlot slot, float labelWidth, float inputWidth, in PrefabOverrideUiState prefabOverrideState)
    {
        var info = item.Info;
        float value = PropertyDispatcher.ReadFloat(_propertyWorld, slot);
        float minInput = float.IsNaN(info.Min) ? float.MinValue : info.Min;
        float maxInput = float.IsNaN(info.Max) ? float.MaxValue : info.Max;
        if (minInput == maxInput)
        {
            if (minInput >= float.MaxValue)
            {
                minInput = float.MaxValue - 1f;
                maxInput = float.MaxValue;
            }
            else
            {
                maxInput = minInput + 1f;
            }
        }

        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = GetPaddedRowRect(rect);
        bool hovered = rect.Contains(Im.MousePos);
        var inputRect = new ImRect(rect.X + labelWidth, rect.Y, inputWidth, rect.Height);
        TryDrawPropertyBindingContextMenu(slot, inputRect, allowUnbind: true);
        if (ImScalarInput.DrawAt(item.Label, item.WidgetId, rect.X, rect.Y, labelWidth, inputWidth, KeyIconWidth,
            ref value, minInput, maxInput, "F2"))
        {
            int widgetId = Im.Context.GetId(item.WidgetId);
            bool isEditing = Im.Context.IsActive(widgetId) || Im.Context.IsFocused(widgetId);
            _commands.SetPropertyValue(widgetId, isEditing, slot, PropertyValue.FromFloat(value));
            ReportGeneratedGizmoSingle(item, slot, hovered, isEditing);
        }
        else
        {
            int widgetId = Im.Context.GetId(item.WidgetId);
            bool isEditing = Im.Context.IsActive(widgetId) || Im.Context.IsFocused(widgetId);
            _commands.NotifyPropertyWidgetState(widgetId, isEditing);
            ReportGeneratedGizmoSingle(item, slot, hovered, isEditing);
        }

        DrawKeyIcon(slot, new ImRect(rect.X + labelWidth, rect.Y, inputWidth, rect.Height));
        DrawPrefabOverrideBorderIfNeeded(inputRect, in prefabOverrideState);
    }

    private void DrawIntProperty(PropertyUiItem item, PropertySlot slot, float labelWidth, float inputWidth, in PrefabOverrideUiState prefabOverrideState)
    {
        int intValue = PropertyDispatcher.ReadInt(_propertyWorld, slot);
        float minInput = float.IsNaN(item.Info.Min) ? float.MinValue : item.Info.Min;
        float maxInput = float.IsNaN(item.Info.Max) ? float.MaxValue : item.Info.Max;
        int minValue = minInput <= int.MinValue ? int.MinValue : (int)MathF.Round(minInput);
        int maxValue = maxInput >= int.MaxValue ? int.MaxValue : (int)MathF.Round(maxInput);
        if (minValue == maxValue)
        {
            if (minValue == int.MaxValue)
            {
                minValue = int.MaxValue - 1;
                maxValue = int.MaxValue;
            }
            else
            {
                maxValue = minValue + 1;
            }
        }

        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = GetPaddedRowRect(rect);
        bool hovered = rect.Contains(Im.MousePos);
        var inputRect = new ImRect(rect.X + labelWidth, rect.Y, inputWidth, rect.Height);
        TryDrawPropertyBindingContextMenu(slot, inputRect, allowUnbind: true);
        if (ImScalarInput.DrawIntAt(item.Label, item.WidgetId, rect.X, rect.Y, labelWidth, inputWidth, KeyIconWidth,
            ref intValue, minValue, maxValue))
        {
            int widgetId = Im.Context.GetId(item.WidgetId);
            bool isEditing = Im.Context.IsActive(widgetId) || Im.Context.IsFocused(widgetId);
            _commands.SetPropertyValue(widgetId, isEditing, slot, PropertyValue.FromInt(intValue));
            ReportGeneratedGizmoSingle(item, slot, hovered, isEditing);
        }
        else
        {
            int widgetId = Im.Context.GetId(item.WidgetId);
            bool isEditing = Im.Context.IsActive(widgetId) || Im.Context.IsFocused(widgetId);
            _commands.NotifyPropertyWidgetState(widgetId, isEditing);
            ReportGeneratedGizmoSingle(item, slot, hovered, isEditing);
        }

        DrawKeyIcon(slot, new ImRect(rect.X + labelWidth, rect.Y, inputWidth, rect.Height));
        DrawPrefabOverrideBorderIfNeeded(inputRect, in prefabOverrideState);
    }

    private void DrawKeyIcon(PropertySlot slot, ImRect inputRect)
    {
        if (!_commands.TryGetKeyableState(slot, out bool hasTrack, out bool hasKey))
        {
            return;
        }

        var iconRect = new ImRect(inputRect.Right - KeyIconWidth, inputRect.Y, KeyIconWidth, inputRect.Height);
        DrawKeyIconDiamond(iconRect, filled: hasKey, highlighted: hasTrack);
        if (iconRect.Contains(Im.MousePos) && Im.MousePressed)
        {
            _commands.ToggleKeyAtPlayhead(slot);
        }
    }

    private void DrawVectorComponentKeyIcon(PropertySlot slot, ImRect iconRect)
    {
        if (!_commands.TryGetKeyableState(slot, out bool hasTrack, out bool hasKey))
        {
            return;
        }

        DrawKeyIconDiamond(iconRect, filled: hasKey, highlighted: hasTrack);
        if (iconRect.Contains(Im.MousePos) && Im.MousePressed)
        {
            _commands.ToggleKeyAtPlayhead(slot);
        }
    }

    private void DrawVectorPropertyKeyIcons(PropertySlot slot, float inputX, float y, float inputWidth, int componentCount)
    {
        // For a plain Vec2/3/4 value property (no channels), we show one key icon (on the last component slot).
        if (!_commands.TryGetKeyableState(slot, out bool hasTrack, out bool hasKey))
        {
            return;
        }

        float height = Im.Style.MinButtonHeight;
        float spacing = Im.Style.Spacing;
        float componentWidth = componentCount switch
        {
            2 => (inputWidth - spacing) * 0.5f,
            3 => (inputWidth - spacing * 2f) / 3f,
            4 => (inputWidth - spacing * 3f) / 4f,
            _ => inputWidth
        };

        float lastComponentX = inputX + (componentWidth + spacing) * (componentCount - 1);
        var iconRect = new ImRect(lastComponentX + componentWidth - KeyIconWidth, y, KeyIconWidth, height);
        DrawKeyIconDiamond(iconRect, filled: hasKey, highlighted: hasTrack);
        if (iconRect.Contains(Im.MousePos) && Im.MousePressed)
        {
            _commands.ToggleKeyAtPlayhead(slot);
        }
    }

    private void DrawVectorRootChannelKeyIcons(PropertySlot rootSlot, float inputX, float y, float inputWidth, int channelCount)
    {
        float height = Im.Style.MinButtonHeight;
        float spacing = Im.Style.Spacing;
        float componentWidth = channelCount switch
        {
            2 => (inputWidth - spacing) * 0.5f,
            3 => (inputWidth - spacing * 2f) / 3f,
            4 => (inputWidth - spacing * 3f) / 4f,
            _ => inputWidth
        };

        for (ushort channelIndex = 0; channelIndex < channelCount; channelIndex++)
        {
            if (!_commands.TryGetKeyableChannelState(rootSlot, channelIndex, out bool hasTrack, out bool hasKey))
            {
                continue;
            }

            float componentX = inputX + (componentWidth + spacing) * channelIndex;
            var iconRect = new ImRect(componentX + componentWidth - KeyIconWidth, y, KeyIconWidth, height);
            DrawKeyIconDiamond(iconRect, filled: hasKey, highlighted: hasTrack);
            if (iconRect.Contains(Im.MousePos) && Im.MousePressed)
            {
                _commands.ToggleKeyAtPlayhead(rootSlot, channelIndex);
            }
        }
    }

    private static void DrawKeyIconDiamond(ImRect rect, bool filled, bool highlighted)
    {
        if (rect.Width <= 0f || rect.Height <= 0f)
        {
            return;
        }

        uint color = highlighted ? Im.Style.Primary : Im.Style.TextSecondary;
        float size = MathF.Min(rect.Width, rect.Height) * 0.46f;
        float cx = rect.X + rect.Width * 0.5f;
        float cy = rect.Y + rect.Height * 0.5f;

        if (filled)
        {
            AnimationEditorHelpers.DrawFilledDiamond(cx, cy, size, color);
            return;
        }

        float half = size * 0.5f;
        Im.DrawLine(cx, cy - half, cx + half, cy, 1f, color);
        Im.DrawLine(cx + half, cy, cx, cy + half, 1f, color);
        Im.DrawLine(cx, cy + half, cx - half, cy, 1f, color);
        Im.DrawLine(cx - half, cy, cx, cy - half, 1f, color);
    }

    private void DrawStringHandleProperty(PropertyUiItem item, PropertySlot slot, float labelWidth, float inputWidth, in PrefabOverrideUiState prefabOverrideState)
    {
        TextPropertyEditState state = GetTextPropertyEditState(item.Info.PropertyId);
        char[] buffer = state.Buffer;
        StringHandle currentHandle = PropertyDispatcher.ReadStringHandle(_propertyWorld, slot);

        int widgetId = Im.Context.GetId(item.WidgetId);
        bool wasFocused = Im.Context.IsFocused(widgetId);
        if (!wasFocused && state.Handle != currentHandle)
        {
            SetTextBufferFromHandle(state, currentHandle);
        }

        int length = state.Length;
        bool changed;
        bool hovered = false;
        ImRect inputRect = default;
        if (IsMultilineLabel(item.Info.Name))
        {
            DrawPropertyLabel(item.Label);
            float height = Im.Style.MinButtonHeight * 4f;
            var rect = ImLayout.AllocateRect(0f, height);
            rect = GetPaddedRowRect(rect);
            hovered = rect.Contains(Im.MousePos);
            inputRect = rect;
            TryDrawPropertyBindingContextMenu(slot, rect, allowUnbind: true);
            changed = ImTextArea.DrawAt(item.WidgetId, buffer, ref length, buffer.Length, rect.X, rect.Y, rect.Width, rect.Height, wordWrap: true);
        }
        else
        {
            var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
            rect = GetPaddedRowRect(rect);
            hovered = rect.Contains(Im.MousePos);
            float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
            Im.Text(item.Label.AsSpan(), rect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

            inputRect = new ImRect(rect.X + labelWidth, rect.Y, inputWidth, rect.Height);
            TryDrawPropertyBindingContextMenu(slot, inputRect, allowUnbind: true);
            changed = Im.TextInput(item.WidgetId, buffer, ref length, buffer.Length, rect.X + labelWidth, rect.Y, inputWidth);
        }

        if (changed)
        {
            string newText = new string(buffer, 0, length);
            StringHandle newHandle = newText;
            bool isEditing = Im.Context.IsFocused(widgetId);
            _commands.SetPropertyValue(widgetId, isEditing, slot, PropertyValue.FromStringHandle(newHandle));
            state.Handle = newHandle;
        }

        state.Length = length;
        bool isEditingWidget = Im.Context.IsFocused(widgetId);
        _commands.NotifyPropertyWidgetState(widgetId, isEditingWidget);
        ReportGeneratedGizmoSingle(item, slot, hovered, isEditingWidget);
        DrawPrefabOverrideBorderIfNeeded(inputRect, in prefabOverrideState);
    }

    private void DrawColorProperty(PropertyUiItem item, PropertySlot slot, float labelWidth, float inputWidth, in PrefabOverrideUiState prefabOverrideState)
    {
        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = GetPaddedRowRect(rect);
        var inputRect = new ImRect(rect.X + labelWidth, rect.Y, inputWidth, rect.Height);
        TryDrawPropertyBindingContextMenu(slot, inputRect, allowUnbind: true);
        _colorPicker.DrawColor32PropertyRow(
            item.Label,
            item.WidgetId,
            slot,
            item.Index0,
            _propertyUiPropertyCount,
            rect.X,
            rect.Y,
            labelWidth,
            inputWidth);
        DrawPrefabOverrideBorderIfNeeded(inputRect, in prefabOverrideState);
    }

    public void DrawPropertyBindingContextMenu(EntityId targetEntity, in PropertySlot slot, ImRect inputRect, bool allowUnbind)
    {
        TryDrawPropertyBindingContextMenuCore(targetEntity, slot, inputRect, allowUnbind);
    }

    private void TryDrawPropertyBindingContextMenu(in PropertySlot slot, ImRect inputRect, bool allowUnbind)
    {
        TryDrawPropertyBindingContextMenuCore(_gizmoTargetEntity, slot, inputRect, allowUnbind);
    }

    private void TryDrawPropertyBindingContextMenuCore(EntityId targetEntity, in PropertySlot slot, ImRect inputRect, bool allowUnbind)
    {
        if (targetEntity.IsNull)
        {
            return;
        }

        bool isExpanded = _workspace.World.HasComponent(targetEntity, PrefabExpandedComponent.Api.PoolIdConst);
        TryGetPrefabOverrideUiState(targetEntity, slot, out PrefabOverrideUiState prefabOverrideState);
        if (isExpanded)
        {
            if (!prefabOverrideState.IsOverridden)
            {
                return;
            }

            bool hoveredOverride = inputRect.Contains(Im.MousePos);
            if (hoveredOverride && Im.Context.Input.MouseRightPressed)
            {
                ImContextMenu.OpenAt("prop_override_menu", inputRect.X, inputRect.Bottom);
            }

            if (ImContextMenu.Begin("prop_override_menu"))
            {
                if (ImContextMenu.Item("Reset to Default"))
                {
                    int resetWidgetId = Im.Context.GetId("reset_to_default");
                    _commands.SetPropertyValue(resetWidgetId, isEditing: false, targetEntity, slot, prefabOverrideState.DefaultValue);
                }

                ImContextMenu.End();
            }

            return;
        }

        EntityId owningPrefab = _workspace.FindOwningPrefabEntity(targetEntity);
        if (owningPrefab.IsNull || _workspace.World.GetNodeType(owningPrefab) != UiNodeType.Prefab)
        {
            return;
        }

        PrefabVariablesComponent.ViewProxy vars = default;
        bool hasVars = false;
        if (_workspace.World.TryGetComponent(owningPrefab, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) && varsAny.IsValid)
        {
            var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
            vars = PrefabVariablesComponent.Api.FromHandle(_propertyWorld, varsHandle);
            hasVars = vars.IsAlive && vars.VariableCount > 0;
        }

        uint targetStableId = _workspace.World.GetStableId(targetEntity);
        if (targetStableId == 0)
        {
            return;
        }

        var anchorRectViewport = inputRect.Offset(Im.CurrentTranslation);

        Im.Context.PushId(unchecked((int)slot.PropertyId));
        Im.Context.PushId(unchecked((int)targetStableId));

        ushort boundVariableId = 0;
        if (_workspace.World.TryGetComponent(owningPrefab, PrefabBindingsComponent.Api.PoolIdConst, out AnyComponentHandle bindingsAny) && bindingsAny.IsValid)
        {
            var bindingsHandle = new PrefabBindingsComponentHandle(bindingsAny.Index, bindingsAny.Generation);
            var bindings = PrefabBindingsComponent.Api.FromHandle(_propertyWorld, bindingsHandle);
            if (bindings.IsAlive)
            {
                ushort bindingCount = bindings.BindingCount;
                if (bindingCount > PrefabBindingsComponent.MaxBindings)
                {
                    bindingCount = PrefabBindingsComponent.MaxBindings;
                }

                ReadOnlySpan<uint> targetNode = bindings.TargetSourceNodeStableIdReadOnlySpan();
                ReadOnlySpan<ushort> targetComponent = bindings.TargetComponentKindReadOnlySpan();
                ReadOnlySpan<ulong> propertyId = bindings.PropertyIdReadOnlySpan();
                ReadOnlySpan<ushort> varId = bindings.VariableIdReadOnlySpan();

                for (int i = 0; i < bindingCount; i++)
                {
                    if (targetNode[i] == targetStableId && targetComponent[i] == slot.Component.Kind && propertyId[i] == slot.PropertyId)
                    {
                        boundVariableId = varId[i];
                        break;
                    }
                }
            }
        }

        bool hovered = inputRect.Contains(Im.MousePos);
        if (hovered && Im.Context.Input.MouseRightPressed)
        {
            ImContextMenu.OpenAt("prop_bind_menu", inputRect.X, inputRect.Bottom);
        }

        if (ImContextMenu.Begin("prop_bind_menu"))
        {
            if (prefabOverrideState.IsOverridden)
            {
                if (ImContextMenu.Item("Reset to Default"))
                {
                    int resetWidgetId = Im.Context.GetId("reset_to_default");
                    _commands.SetPropertyValue(resetWidgetId, isEditing: false, targetEntity, slot, prefabOverrideState.DefaultValue);
                }

                ImContextMenu.Separator();
            }

            if (boundVariableId != 0)
            {
                if (ImContextMenu.Item("Update Bind"))
                {
                    int compatibleVariableCount = 0;
                    if (hasVars)
                    {
                        PropertyKind slotKind = slot.Kind;
                        ReadOnlySpan<ushort> ids = vars.VariableIdReadOnlySpan();
                        ReadOnlySpan<int> kinds = vars.KindReadOnlySpan();

                        ushort count = vars.VariableCount;
                        if (count > PrefabVariablesComponent.MaxVariables)
                        {
                            count = (ushort)PrefabVariablesComponent.MaxVariables;
                        }

                        for (int i = 0; i < count; i++)
                        {
                            if (ids[i] == 0)
                            {
                                continue;
                            }

                            if ((PropertyKind)kinds[i] == slotKind)
                            {
                                compatibleVariableCount++;
                            }
                        }
                    }

                    _dataBindPopover.Open(owningPrefab, targetStableId, slot.Component.Kind, slot, anchorRectViewport);
                }

                ImContextMenu.ItemDisabled("Auto Bind");

                if (allowUnbind && ImContextMenu.Item("Unbind"))
                {
                    _commands.SetPrefabPropertyBinding(owningPrefab, targetStableId, slot.Component.Kind, slot, variableId: 0, direction: 0);
                }
            }
            else
            {
                ImContextMenu.ItemDisabled("Not bound");
                ImContextMenu.Separator();
            }

            ImContextMenu.ItemDisabled("Bind To Variable");

            if (!hasVars)
            {
                ImContextMenu.ItemDisabled("(no variables)");
            }
            else
            {
                PropertyKind slotKind = slot.Kind;
                ReadOnlySpan<ushort> ids = vars.VariableIdReadOnlySpan();
                ReadOnlySpan<StringHandle> names = vars.NameReadOnlySpan();
                ReadOnlySpan<int> kinds = vars.KindReadOnlySpan();

                ushort count = vars.VariableCount;
                if (count > PrefabVariablesComponent.MaxVariables)
                {
                    count = (ushort)PrefabVariablesComponent.MaxVariables;
                }

                bool any = false;
                for (int i = 0; i < count; i++)
                {
                    ushort id = ids[i];
                    if (id == 0)
                    {
                        continue;
                    }

                    var kind = (PropertyKind)kinds[i];
                    if (kind != slotKind)
                    {
                        continue;
                    }

                    any = true;
                    string label = names[i].IsValid ? names[i].ToString() : $"Var {id}";
                    if (boundVariableId == id)
                    {
                        label = $"{label} (bound)";
                    }

                    if (ImContextMenu.Item(label))
                    {
                        _commands.SetPrefabPropertyBinding(owningPrefab, targetStableId, slot.Component.Kind, slot, variableId: id, direction: 0);
                }
            }

                if (!any)
                {
                    ImContextMenu.ItemDisabled("No matching variables");
                }
            }

            ImContextMenu.End();
        }
        Im.Context.PopId();
        Im.Context.PopId();
    }

    private static void DrawPrefabOverrideBorderIfNeeded(ImRect inputRect, in PrefabOverrideUiState prefabOverrideState)
    {
        if (!prefabOverrideState.IsOverridden)
        {
            return;
        }

        uint borderColor = ImStyle.WithAlphaF(Im.Style.Secondary, 0.95f);
        float borderWidth = MathF.Max(Im.Style.BorderWidth, 2f);
        Im.DrawRoundedRectStroke(inputRect.X, inputRect.Y, inputRect.Width, inputRect.Height, Im.Style.CornerRadius, borderColor, borderWidth);
    }

    private bool TryGetPrefabOverrideUiState(EntityId targetEntity, in PropertySlot slot, out PrefabOverrideUiState state)
    {
        state = default;

        if (targetEntity.IsNull)
        {
            return false;
        }

        EntityId instanceRootEntity = EntityId.Null;
        uint sourcePrefabStableId = 0;
        uint sourceNodeStableId = 0;

        if (_workspace.World.TryGetComponent(targetEntity, PrefabExpandedComponent.Api.PoolIdConst, out AnyComponentHandle expandedAny) && expandedAny.IsValid)
        {
            var expandedHandle = new PrefabExpandedComponentHandle(expandedAny.Index, expandedAny.Generation);
            var expanded = PrefabExpandedComponent.Api.FromHandle(_propertyWorld, expandedHandle);
            if (!expanded.IsAlive || expanded.InstanceRootStableId == 0 || expanded.SourceNodeStableId == 0)
            {
                return false;
            }

            instanceRootEntity = _workspace.World.GetEntityByStableId(expanded.InstanceRootStableId);
            if (instanceRootEntity.IsNull || _workspace.World.GetNodeType(instanceRootEntity) != UiNodeType.PrefabInstance)
            {
                return false;
            }

            if (_workspace.World.TryGetComponent(instanceRootEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) && instanceAny.IsValid)
            {
                var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
                var instance = PrefabInstanceComponent.Api.FromHandle(_propertyWorld, instanceHandle);
                if (instance.IsAlive)
                {
                    sourcePrefabStableId = instance.SourcePrefabStableId;
                    sourceNodeStableId = expanded.SourceNodeStableId;
                }
            }
        }
        else if (_workspace.World.GetNodeType(targetEntity) == UiNodeType.PrefabInstance &&
                 _workspace.World.TryGetComponent(targetEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) &&
                 instanceAny.IsValid)
        {
            instanceRootEntity = targetEntity;
            var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
            var instance = PrefabInstanceComponent.Api.FromHandle(_propertyWorld, instanceHandle);
            if (instance.IsAlive)
            {
                sourcePrefabStableId = instance.SourcePrefabStableId;
                sourceNodeStableId = sourcePrefabStableId;
            }
        }

        if (instanceRootEntity.IsNull || sourcePrefabStableId == 0 || sourceNodeStableId == 0)
        {
            return false;
        }

        EntityId sourceEntity = _workspace.World.GetEntityByStableId(sourceNodeStableId);
        if (sourceEntity.IsNull)
        {
            return false;
        }

        if (!_workspace.World.TryGetComponent(sourceEntity, slot.Component.Kind, out AnyComponentHandle sourceComponentAny) || !sourceComponentAny.IsValid)
        {
            return false;
        }

        if (!TryResolveSlotForPrefabOverride(sourceComponentAny, (ushort)slot.PropertyIndex, slot.PropertyId, slot.Kind, out PropertySlot sourceSlot))
        {
            return false;
        }

        PropertyValue defaultValue = ReadPropertyValue(_propertyWorld, sourceSlot);

        bool isOverridden = false;
        if (_workspace.World.TryGetComponent(instanceRootEntity, PrefabInstancePropertyOverridesComponent.Api.PoolIdConst, out AnyComponentHandle overridesAny) &&
            overridesAny.IsValid)
        {
            var overridesHandle = new PrefabInstancePropertyOverridesComponentHandle(overridesAny.Index, overridesAny.Generation);
            var overrides = PrefabInstancePropertyOverridesComponent.Api.FromHandle(_propertyWorld, overridesHandle);
            if (overrides.IsAlive && overrides.Count > 0)
            {
                int count = overrides.Count;
                if (count > PrefabInstancePropertyOverridesComponent.MaxOverrides)
                {
                    count = PrefabInstancePropertyOverridesComponent.MaxOverrides;
                }

                int rootCount = overrides.RootOverrideCount;
                if (rootCount > count)
                {
                    rootCount = count;
                }

                bool isRoot = sourceNodeStableId == sourcePrefabStableId;
                int searchStart = isRoot ? 0 : rootCount;
                int searchEnd = isRoot ? rootCount : count;

                ReadOnlySpan<uint> srcNode = overrides.SourceNodeStableIdReadOnlySpan().Slice(0, count);
                ReadOnlySpan<ushort> componentKind = overrides.ComponentKindReadOnlySpan().Slice(0, count);
                ReadOnlySpan<ulong> propertyId = overrides.PropertyIdReadOnlySpan().Slice(0, count);

                for (int i = searchStart; i < searchEnd; i++)
                {
                    if (srcNode[i] == sourceNodeStableId && componentKind[i] == slot.Component.Kind && propertyId[i] == slot.PropertyId)
                    {
                        isOverridden = true;
                        break;
                    }
                }
            }
        }

        if (!isOverridden &&
            targetEntity.Value == instanceRootEntity.Value &&
            sourceNodeStableId == sourcePrefabStableId &&
            slot.Component.Kind == TransformComponent.Api.PoolIdConst &&
            PropertyDispatcher.TryGetInfo(slot.Component, slot.PropertyIndex, out PropertyInfo slotInfo) &&
            slotInfo.Group == "Layout Child")
        {
            // For prefab instance wrapper layout-child properties, treat any divergence from the source as overridden
            // even if the override entry wasn't recorded (e.g., legacy data / edits before override tracking).
            PropertyValue currentValue = ReadPropertyValue(_propertyWorld, slot);
            if (!currentValue.Equals(slot.Kind, defaultValue))
            {
                isOverridden = true;
            }
        }

        state = new PrefabOverrideUiState(isOverridden, in defaultValue);
        return true;
    }

    private static bool TryResolveSlotForPrefabOverride(AnyComponentHandle component, ushort propertyIndexHint, ulong propertyId, PropertyKind kind, out PropertySlot slot)
    {
        slot = default;

        int propertyCount = PropertyDispatcher.GetPropertyCount(component);
        if (propertyCount <= 0)
        {
            return false;
        }

        int hintIndex = propertyIndexHint;
        if (hintIndex >= 0 && hintIndex < propertyCount)
        {
            if (PropertyDispatcher.TryGetInfo(component, hintIndex, out var hintInfo) && hintInfo.PropertyId == propertyId && hintInfo.Kind == kind)
            {
                slot = PropertyDispatcher.GetSlot(component, hintIndex);
                return true;
            }
        }

        for (int propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
        {
            if (!PropertyDispatcher.TryGetInfo(component, propertyIndex, out var info))
            {
                continue;
            }

            if (info.PropertyId != propertyId || info.Kind != kind)
            {
                continue;
            }

            slot = PropertyDispatcher.GetSlot(component, propertyIndex);
            return true;
        }

        return false;
    }

    private static PropertyValue ReadPropertyValue(World registry, in PropertySlot slot)
    {
        switch (slot.Kind)
        {
            case PropertyKind.Float:
                return PropertyValue.FromFloat(PropertyDispatcher.ReadFloat(registry, slot));
            case PropertyKind.Int:
                return PropertyValue.FromInt(PropertyDispatcher.ReadInt(registry, slot));
            case PropertyKind.Bool:
                return PropertyValue.FromBool(PropertyDispatcher.ReadBool(registry, slot));
            case PropertyKind.Vec2:
                return PropertyValue.FromVec2(PropertyDispatcher.ReadVec2(registry, slot));
            case PropertyKind.Vec3:
                return PropertyValue.FromVec3(PropertyDispatcher.ReadVec3(registry, slot));
            case PropertyKind.Vec4:
                return PropertyValue.FromVec4(PropertyDispatcher.ReadVec4(registry, slot));
            case PropertyKind.Color32:
                return PropertyValue.FromColor32(PropertyDispatcher.ReadColor32(registry, slot));
            case PropertyKind.StringHandle:
                return PropertyValue.FromStringHandle(PropertyDispatcher.ReadStringHandle(registry, slot));
            case PropertyKind.Fixed64:
                return PropertyValue.FromFixed64(PropertyDispatcher.ReadFixed64(registry, slot));
            case PropertyKind.Fixed64Vec2:
                return PropertyValue.FromFixed64Vec2(PropertyDispatcher.ReadFixed64Vec2(registry, slot));
            case PropertyKind.Fixed64Vec3:
                return PropertyValue.FromFixed64Vec3(PropertyDispatcher.ReadFixed64Vec3(registry, slot));
            default:
                return default;
        }
    }

    private TextPropertyEditState GetTextPropertyEditState(ulong propertyId)
    {
        if (!_textPropertyEditStates.TryGetValue(propertyId, out TextPropertyEditState? state) || state == null)
        {
            state = new TextPropertyEditState();
            _textPropertyEditStates[propertyId] = state;
        }

        return state;
    }

    private static void SetTextBufferFromHandle(TextPropertyEditState state, StringHandle valueHandle)
    {
        string textValue = valueHandle.IsValid ? valueHandle.ToString() : string.Empty;
        int length = Math.Min(textValue.Length, state.Buffer.Length);
        textValue.AsSpan(0, length).CopyTo(state.Buffer);
        state.Length = length;
        state.Handle = valueHandle;
    }

    private bool IsMultilineLabel(StringHandle nameHandle)
    {
        return nameHandle == _notesLabelHandle || nameHandle == _descriptionLabelHandle;
    }

    private static void DrawPropertyLabel(string label)
    {
        Im.LabelText(label);
    }

    private static ImRect GetPaddedRowRect(ImRect rect)
    {
        float x = rect.X + InspectorRowPaddingX;
        float width = MathF.Max(1f, rect.Width - InspectorRowPaddingX * 2f);
        return new ImRect(x, rect.Y, width, rect.Height);
    }

    private static void GetPropertyInputLayout(string label, float totalWidth, out float labelWidth, out float inputWidth)
    {
        if (totalWidth <= 0f)
        {
            totalWidth = 220f;
        }

        const float columnGap = 4f;
        float textWidth = label.Length * Im.Style.FontSize * 0.6f;
        float maxLabelWidth = totalWidth * 0.40f;
        float minLabelWidth = Math.Min(70f, maxLabelWidth);
        labelWidth = Math.Clamp(textWidth + columnGap, minLabelWidth, maxLabelWidth);
        inputWidth = Math.Max(100f, totalWidth - labelWidth);
    }

    private static string BuildWidgetId(ulong propertyId)
    {
        return "prop_" + propertyId.ToString("X16");
    }
}
