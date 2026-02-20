using System;
using System.Numerics;
using Core;
using Pooled.Runtime;
using Property;
using Property.Runtime;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Widgets;
using DerpLib.Sdf;
using static Derp.UI.UiColor32;
using static Derp.UI.UiFillGradient;

namespace Derp.UI;

internal sealed class ColorPicker
{
    private const float ColorPickerHeightOffset = 28f;
    private const float ColorPreviewSwatchPadding = 3f;
    private const float ColorPreviewSwatchSizeMin = 18f;
    private const float KeyIconWidth = 18f;
    private const float InspectorPopoverGap = 6f;
    private const int FillGradientMaxStops = MaxStops;

    private static readonly string[] GradientTypeOptions = new string[]
    {
        "Linear",
        "Radial",
        "Angular"
    };

    private readonly World _propertyWorld;
    private readonly WorkspaceCommands _commands;
    private PropertyInspector? _bindingInspector;

    private enum InspectorColorPopoverKind : byte
    {
        None = 0,
        Color32Property = 1,
        Fill = 2,
        PaintFillLayer = 3,
    }

    private InspectorColorPopoverKind _colorPopoverKind;
    private int _colorPopoverWidgetId;
    private int _colorPopoverOpenedFrame;
    private string? _colorPopoverPickerId;
    private PropertySlot _colorPopoverSlot;
    private EntityId _colorPopoverTargetEntity;
    private ImRect _colorPopoverAnchorRectViewport;
    private DerpLib.ImGui.Viewport.ImViewport? _colorPopoverViewport;
    private bool _colorPopoverMouseDownStartedInside;
    private float? _colorPopoverInspectorLeftViewport;

    private FillComponentHandle _colorPopoverFillHandle;
    private byte _colorPopoverFillActiveStopIndex;

    private PaintComponentHandle _colorPopoverPaintHandle;
    private int _colorPopoverPaintLayerIndex = -1;
    private byte _colorPopoverPaintActiveStopIndex;
    private bool _colorPopoverPaintIsStrokeLayer;

    private char[][]? _propertyColorHexBuffers;
    private int[]? _propertyColorHexLengths;
    private uint[]? _propertyColorHexLastArgb;
    private int _propertyColorHexCapacity;

    private int _opacityDragWidgetId;
    private float _opacityDragStartMouseX;
    private byte _opacityDragStartAlpha;
    private int _opacityPressWidgetId;
    private float _opacityPressStartMouseX;

    private char[][]? _propertyOpacityBuffers;
    private int[]? _propertyOpacityLengths;
    private byte[]? _propertyOpacityLastAlpha;
    private int _propertyOpacityCapacity;

    private FillComponentHandle _fillPreviewHexHandle;
    private char[]? _fillPreviewHexBuffer;
    private int _fillPreviewHexLength;
    private uint _fillPreviewHexLastArgb;

    private FillComponentHandle _fillPreviewOpacityHandle;
    private char[]? _fillPreviewOpacityBuffer;
    private int _fillPreviewOpacityLength;
    private byte _fillPreviewOpacityLastAlpha;

    private int[]? _colorPreviewWidgetIds;
    private char[][]? _colorPreviewHexBuffers;
    private int[]? _colorPreviewHexLengths;
    private uint[]? _colorPreviewHexLastArgb;
    private char[][]? _colorPreviewOpacityBuffers;
    private int[]? _colorPreviewOpacityLengths;
    private byte[]? _colorPreviewOpacityLastAlpha;
    private int _colorPreviewCapacity;
    private int _colorPreviewCount;

    private FillComponentHandle _fillPopoverStopCacheHandle;
    private char[][]? _fillPopoverStopHexBuffers;
    private int[]? _fillPopoverStopHexLengths;
    private uint[]? _fillPopoverStopHexLastArgb;
    private char[][]? _fillPopoverStopOpacityBuffers;
    private int[]? _fillPopoverStopOpacityLengths;
    private byte[]? _fillPopoverStopOpacityLastAlpha;

    private FillComponentHandle _gradientStopDragHandle;
    private int _gradientStopDragIndex = -1;

    private PaintComponentHandle _paintGradientStopDragHandle;
    private int _paintGradientStopDragLayerIndex = -1;
    private int _paintGradientStopDragIndex = -1;

    private int _suppressCloseFrame;

    public ColorPicker(World propertyWorld, WorkspaceCommands commands)
    {
        _propertyWorld = propertyWorld;
        _commands = commands;
    }

    internal void SetBindingInspector(PropertyInspector inspector)
    {
        _bindingInspector = inspector;
    }

    public void SuppressCloseThisFrame()
    {
        _suppressCloseFrame = Im.Context.FrameCount;
    }

    public bool CapturesMouse(DerpLib.ImGui.Viewport.ImViewport viewport)
    {
        if (!TryGetColorPopoverRect(viewport, out ImRect popupRect))
        {
            _colorPopoverMouseDownStartedInside = false;
            return false;
        }

        bool hovered = popupRect.Contains(Im.MousePosViewport);
        if (hovered && viewport.Input.MousePressed)
        {
            _colorPopoverMouseDownStartedInside = true;
        }
        if (viewport.Input.MouseReleased)
        {
            _colorPopoverMouseDownStartedInside = false;
        }

        return hovered || (_colorPopoverMouseDownStartedInside && viewport.Input.MouseDown);
    }

    public bool HasOpenPopoverForViewport(DerpLib.ImGui.Viewport.ImViewport viewport)
    {
        if (_colorPopoverKind == InspectorColorPopoverKind.None)
        {
            return false;
        }

        return _colorPopoverViewport != null && _colorPopoverViewport == viewport;
    }

    public bool TryGetActivePaintFillGradientEditor(out EntityId targetEntity, out PaintComponentHandle paintHandle, out int layerIndex)
    {
        targetEntity = EntityId.Null;
        paintHandle = default;
        layerIndex = -1;

        if (_colorPopoverKind != InspectorColorPopoverKind.PaintFillLayer ||
            _colorPopoverPaintHandle.IsNull ||
            _colorPopoverPaintLayerIndex < 0 ||
            _colorPopoverTargetEntity.IsNull)
        {
            return false;
        }

        if (!TryGetPaintFillUseGradient(_colorPopoverPaintHandle, _colorPopoverPaintLayerIndex, out bool useGradient) || !useGradient)
        {
            return false;
        }

        targetEntity = _colorPopoverTargetEntity;
        paintHandle = _colorPopoverPaintHandle;
        layerIndex = _colorPopoverPaintLayerIndex;
        return true;
    }

    public void RenderPopover()
    {
        if (_colorPopoverKind == InspectorColorPopoverKind.None)
        {
            return;
        }

        var viewport = Im.CurrentViewport;
        if (viewport == null)
        {
            CloseColorPopover();
            return;
        }

        if (_colorPopoverViewport != null && _colorPopoverViewport != viewport)
        {
            CloseColorPopover();
            return;
        }

        float padding = Im.Style.Padding;
        float popupWidth = Math.Clamp(300f, 200f, Math.Max(200f, viewport.Size.X - InspectorPopoverGap * 2f));
        float contentWidth = Math.Max(160f, popupWidth - padding * 2f);
        float pickerWidth = contentWidth;
        float pickerHeight = pickerWidth + ColorPickerHeightOffset;

        float popupHeight;
        if (_colorPopoverKind == InspectorColorPopoverKind.Fill)
        {
            if (_colorPopoverFillHandle.IsNull)
            {
                CloseColorPopover();
                return;
            }

            var fillView = FillComponent.Api.FromHandle(_propertyWorld, _colorPopoverFillHandle);
            if (!fillView.IsAlive)
            {
                CloseColorPopover();
                return;
            }

            bool useGradient = fillView.UseGradient;
            int stopCount = useGradient ? GetFillGradientStopCount(fillView) : 0;
            if (useGradient && stopCount == 0)
            {
                stopCount = 2;
            }
            popupHeight = ComputeFillPopoverHeight(pickerHeight, useGradient, stopCount);
        }
        else if (_colorPopoverKind == InspectorColorPopoverKind.PaintFillLayer)
        {
            if (_colorPopoverPaintHandle.IsNull || _colorPopoverPaintLayerIndex < 0)
            {
                CloseColorPopover();
                return;
            }

            int stopCount = 0;
            bool useGradient = false;
            if (TryGetPaintFillUseGradient(_colorPopoverPaintHandle, _colorPopoverPaintLayerIndex, out bool useGradientValue))
            {
                useGradient = useGradientValue;
            }
            if (useGradient)
            {
                stopCount = GetPaintFillGradientStopCount(_colorPopoverPaintHandle, _colorPopoverPaintLayerIndex);
                if (stopCount == 0)
                {
                    stopCount = 2;
                }
            }
            popupHeight = ComputeFillPopoverHeight(pickerHeight, useGradient, stopCount);
        }
        else
        {
            popupHeight = padding * 2f + pickerHeight;
        }

        popupWidth = Math.Min(popupWidth, viewport.Size.X - InspectorPopoverGap * 2f);
        popupHeight = Math.Min(popupHeight, viewport.Size.Y - InspectorPopoverGap * 2f);

        var popupRect = PlacePopover(viewport, _colorPopoverAnchorRectViewport, popupWidth, popupHeight, _colorPopoverInspectorLeftViewport);
        ImPopoverCloseButtons closeButtons = _suppressCloseFrame == Im.Context.FrameCount
            ? ImPopoverCloseButtons.None
            : ImPopoverCloseButtons.Left;
        if (ImPopover.ShouldClose(
                openedFrame: _colorPopoverOpenedFrame,
                closeOnEscape: true,
                closeOnOutsideButtons: closeButtons,
                consumeCloseClick: false,
                requireNoMouseOwner: false,
                useViewportMouseCoordinates: true,
                insideRect: popupRect))
        {
            CloseColorPopover();
            return;
        }

        var previousLayer = viewport.CurrentLayer;
        var drawList = viewport.GetDrawList(DerpLib.ImGui.Rendering.ImDrawLayer.Overlay);
        int previousSortKey = drawList.GetSortKey();
        var previousClipRect = drawList.GetClipRect();

        Im.SetDrawLayer(DerpLib.ImGui.Rendering.ImDrawLayer.Overlay);
        drawList.SetSortKey(Math.Max(previousSortKey, int.MaxValue - 2_048));
        drawList.ClearClipRect();

        var savedTranslation = Im.CurrentTranslation;
        bool pushedCancelTransform = false;
        if (savedTranslation.X != 0f || savedTranslation.Y != 0f)
        {
            Im.PushTransform(-savedTranslation);
            pushedCancelTransform = true;
        }

        using var popoverOverlayScope = ImPopover.PushOverlayScope(popupRect);

        Im.DrawRoundedRect(popupRect.X, popupRect.Y, popupRect.Width, popupRect.Height, Im.Style.CornerRadius, Im.Style.Background);
        Im.DrawRoundedRectStroke(popupRect.X, popupRect.Y, popupRect.Width, popupRect.Height, Im.Style.CornerRadius, Im.Style.Border, Im.Style.BorderWidth);

        // This popover is rendered in viewport space and may extend outside the inspector window.
        // Override the window content clip so nested widgets (TextInput, scroll views, etc.) can push clips normally.
        Im.PushClipRectOverride(new ImRect(0f, 0f, viewport.Size.X, viewport.Size.Y));
        _commands.SetAnimationTargetOverride(_colorPopoverTargetEntity);
        if (_colorPopoverKind == InspectorColorPopoverKind.Fill)
        {
            RenderFillPopoverContents(popupRect);
        }
        else if (_colorPopoverKind == InspectorColorPopoverKind.PaintFillLayer)
        {
            RenderPaintFillLayerPopoverContents(popupRect);
        }
        else
        {
            RenderColorPropertyPopoverContents(popupRect);
        }
        _commands.SetAnimationTargetOverride(default);
        Im.PopClipRect();

        if (pushedCancelTransform)
        {
            Im.PopTransform();
        }

        drawList.SetClipRect(previousClipRect);
        drawList.SetSortKey(previousSortKey);
        Im.SetDrawLayer(previousLayer);
    }

    public void DrawFillPreviewRow(ImRect rect, FillComponentHandle fillHandle)
    {
        int widgetId = GetFillEditWidgetId(fillHandle);

        bool isActive = _colorPopoverKind == InspectorColorPopoverKind.Fill &&
            !_colorPopoverFillHandle.IsNull &&
            _colorPopoverFillHandle.Equals(fillHandle);

        bool openRequested = DrawFillPreviewRowCore(widgetId, rect, fillHandle, isActive);
        if (openRequested)
        {
            OpenColorPopoverForFill(widgetId, fillHandle, rect.X, rect.Y, rect.Width, rect.Height);
        }
    }

    public void DrawPaintFillLayerPreviewRow(ImRect rect, string pickerId, PaintComponentHandle paintHandle, int layerIndex)
    {
        DrawPaintLayerPreviewRow(rect, pickerId, paintHandle, layerIndex, isStrokeLayer: false);
    }

    public void DrawPaintStrokeLayerPreviewRow(ImRect rect, string pickerId, PaintComponentHandle paintHandle, int layerIndex)
    {
        DrawPaintLayerPreviewRow(rect, pickerId, paintHandle, layerIndex, isStrokeLayer: true);
    }

    private void DrawPaintLayerPreviewRow(ImRect rect, string pickerId, PaintComponentHandle paintHandle, int layerIndex, bool isStrokeLayer)
    {
        if (paintHandle.IsNull || layerIndex < 0)
        {
            return;
        }

        int widgetId = Im.Context.GetId(pickerId);
        bool isActive = _colorPopoverKind == InspectorColorPopoverKind.PaintFillLayer &&
            !_colorPopoverPaintHandle.IsNull &&
            _colorPopoverPaintHandle.Equals(paintHandle) &&
            _colorPopoverPaintLayerIndex == layerIndex &&
            _colorPopoverPaintIsStrokeLayer == isStrokeLayer &&
            _colorPopoverWidgetId == widgetId;

        bool openRequested = DrawPaintFillLayerPreviewRowCore(widgetId, rect, paintHandle, layerIndex, isStrokeLayer, isActive);
        if (openRequested)
        {
            OpenColorPopoverForPaintFillLayer(widgetId, paintHandle, layerIndex, isStrokeLayer, rect.X, rect.Y, rect.Width, rect.Height);
        }
    }

    public void DrawColor32PreviewRow(ImRect rect, string pickerId, PropertySlot slot, bool showOpacity)
    {
        Color32 color = PropertyDispatcher.ReadColor32(_propertyWorld, slot);
        uint argb = ToArgb(color);

        int widgetId = Im.Context.GetId(pickerId);
        bool isActive = _colorPopoverKind == InspectorColorPopoverKind.Color32Property && _colorPopoverWidgetId == widgetId;

        bool showKeyIcon = _commands.TryGetKeyableState(slot, out bool hasTrack, out bool hasKey);

        int stateIndex = GetOrCreateColorPreviewState(widgetId);
        Span<char> hexBuffer = _colorPreviewHexBuffers![stateIndex];
        ref int hexLength = ref _colorPreviewHexLengths![stateIndex];
        ref uint lastArgb = ref _colorPreviewHexLastArgb![stateIndex];

        Span<char> opacityBuffer = _colorPreviewOpacityBuffers![stateIndex];
        ref int opacityLength = ref _colorPreviewOpacityLengths![stateIndex];
        ref byte lastAlpha = ref _colorPreviewOpacityLastAlpha![stateIndex];

        bool isHexFocused;
        bool isOpacityFocused;
        bool isOpacityDragging;

        Im.Context.PushId(pickerId);
        ImRect previewRect;
        ImRect colorIconRect;
        ImRect opacityIconRect;
        bool openRequested = DrawEditableColorPreviewRow(label: string.Empty,
            rect.X, rect.Y + (rect.Height - Im.Style.MinButtonHeight) * 0.5f,
            labelWidth: 0f,
            inputWidth: rect.Width,
            ref argb,
            ref lastArgb, hexBuffer, ref hexLength,
            ref lastAlpha, opacityBuffer, ref opacityLength,
            allowHex: true, allowOpacity: showOpacity, allowOpacityScrub: showOpacity, isActive,
            reserveColorKeyIcon: showKeyIcon && !showOpacity,
            reserveOpacityKeyIcon: showKeyIcon && showOpacity,
            out previewRect,
            out colorIconRect,
            out opacityIconRect);

        int hexWidgetId = Im.Context.GetId("hex");
        int opacityTextWidgetId = Im.Context.GetId("opacity_text");
        isHexFocused = Im.Context.IsFocused(hexWidgetId);
        isOpacityFocused = Im.Context.IsFocused(opacityTextWidgetId);
        isOpacityDragging = _opacityDragWidgetId == opacityTextWidgetId || _opacityPressWidgetId == opacityTextWidgetId;
        Im.Context.PopId();

        if (showKeyIcon)
        {
            ImRect keyIconRect = opacityIconRect.Width > 0f ? opacityIconRect : colorIconRect;
            if (keyIconRect.Width > 0f)
            {
                DrawKeyIconDiamond(keyIconRect, filled: hasKey, highlighted: hasTrack);
                if (keyIconRect.Contains(Im.MousePos) && Im.MousePressed)
                {
                    _commands.ToggleKeyAtPlayhead(slot);
                    openRequested = false;
                }
            }
        }

        if (argb != ToArgb(color))
        {
            bool isEditing = isActive || isHexFocused || isOpacityFocused || isOpacityDragging;
            _commands.SetPropertyValue(widgetId, isEditing, slot, PropertyValue.FromColor32(FromArgb(argb)));
        }
        else
        {
            bool isEditing = isActive || isHexFocused || isOpacityFocused || isOpacityDragging;
            _commands.NotifyPropertyWidgetState(widgetId, isEditing);
        }

        if (openRequested)
        {
            OpenColorPopoverForProperty(pickerId, slot, rect.X, rect.Y, rect.Width, rect.Height);
        }
    }

    public void DrawColor32PropertyRow(
        string label,
        string pickerId,
        PropertySlot slot,
        int propertyIndex,
        int propertyCount,
        float x,
        float y,
        float labelWidth,
        float inputWidth)
    {
        Color32 color = PropertyDispatcher.ReadColor32(_propertyWorld, slot);
        uint argb = ToArgb(color);

        int widgetId = Im.Context.GetId(pickerId);
        bool isActive = _colorPopoverKind == InspectorColorPopoverKind.Color32Property && _colorPopoverWidgetId == widgetId;

        EnsurePropertyColorHexCache(propertyCount);
        Span<char> hexBuffer = EnsurePropertyColorHexBuffer(propertyIndex);
        ref int hexLength = ref _propertyColorHexLengths![propertyIndex];
        ref uint lastArgb = ref _propertyColorHexLastArgb![propertyIndex];

        EnsurePropertyOpacityCache(propertyCount);
        Span<char> opacityBuffer = EnsurePropertyOpacityBuffer(propertyIndex);
        ref int opacityLength = ref _propertyOpacityLengths![propertyIndex];
        ref byte lastAlpha = ref _propertyOpacityLastAlpha![propertyIndex];

        bool isHexFocused;
        bool isOpacityFocused;
        bool isOpacityDragging;

        bool showKeyIcon = _commands.TryGetKeyableState(slot, out bool hasTrack, out bool hasKey);

        Im.Context.PushId(pickerId);
        ImRect previewRect;
        ImRect colorIconRect;
        ImRect opacityIconRect;
        bool openRequested = DrawEditableColorPreviewRow(label, x, y, labelWidth, inputWidth,
            ref argb,
            ref lastArgb, hexBuffer, ref hexLength,
            ref lastAlpha, opacityBuffer, ref opacityLength,
            allowHex: true, allowOpacity: true, allowOpacityScrub: true, isActive,
            reserveColorKeyIcon: false,
            reserveOpacityKeyIcon: showKeyIcon,
            out previewRect,
            out colorIconRect,
            out opacityIconRect);

        int hexWidgetId = Im.Context.GetId("hex");
        int opacityTextWidgetId = Im.Context.GetId("opacity_text");
        isHexFocused = Im.Context.IsFocused(hexWidgetId);
        isOpacityFocused = Im.Context.IsFocused(opacityTextWidgetId);
        isOpacityDragging = _opacityDragWidgetId == opacityTextWidgetId || _opacityPressWidgetId == opacityTextWidgetId;
        Im.Context.PopId();

        if (showKeyIcon)
        {
            if (opacityIconRect.Width > 0f)
            {
                DrawKeyIconDiamond(opacityIconRect, filled: hasKey, highlighted: hasTrack);
            }
            else if (colorIconRect.Width > 0f)
            {
                DrawKeyIconDiamond(colorIconRect, filled: hasKey, highlighted: hasTrack);
            }

            ImRect keyIconRect = opacityIconRect.Width > 0f ? opacityIconRect : colorIconRect;
            if (keyIconRect.Width > 0f && keyIconRect.Contains(Im.MousePos) && Im.MousePressed)
            {
                _commands.ToggleKeyAtPlayhead(slot);
                openRequested = false;
            }
        }

        if (argb != ToArgb(color))
        {
            bool isEditing = isActive || isHexFocused || isOpacityFocused || isOpacityDragging;
            _commands.SetPropertyValue(widgetId, isEditing, slot, PropertyValue.FromColor32(FromArgb(argb)));
        }
        else
        {
            bool isEditing = isActive || isHexFocused || isOpacityFocused || isOpacityDragging;
            _commands.NotifyPropertyWidgetState(widgetId, isEditing);
        }

        if (openRequested)
        {
            OpenColorPopoverForProperty(pickerId, slot, x + labelWidth, y, inputWidth, Im.Style.MinButtonHeight);
        }
    }

    private bool DrawFillPreviewRowCore(int widgetId, ImRect rect, FillComponentHandle fillHandle, bool isActive)
    {
        var fillView = FillComponent.Api.FromHandle(_propertyWorld, fillHandle);
        if (!fillView.IsAlive)
        {
            return false;
        }

        FillComponent before = SnapshotFill(fillView);

        bool useGradient = fillView.UseGradient;
        Color32 color = fillView.Color;
        int gradientStopCount = GetFillGradientStopCount(fillView);
        Color32 gradientColorA = gradientStopCount > 0 ? GetFillGradientStopColor(fillView, 0) : fillView.GradientColorA;
        Color32 gradientColorB = gradientStopCount > 0 ? GetFillGradientStopColor(fillView, gradientStopCount - 1) : fillView.GradientColorB;

        bool hovered = rect.Contains(Im.MousePos);
        uint background = hovered ? Im.Style.Hover : Im.Style.Surface;
        uint border = isActive ? Im.Style.Primary : (hovered ? Im.Style.Primary : Im.Style.Border);
        Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, background);
        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, border, Im.Style.BorderWidth);

        float swatchSize = Math.Max(ColorPreviewSwatchSizeMin, rect.Height - ColorPreviewSwatchPadding * 2f);
        float swatchX = rect.X + ColorPreviewSwatchPadding;
        float swatchY = rect.Y + (rect.Height - swatchSize) * 0.5f;
        var swatchRect = new ImRect(swatchX, swatchY, swatchSize, swatchSize);
        DrawCheckerboard(swatchRect, 5f);

        if (useGradient)
        {
            int stopCountForDraw = gradientStopCount >= 2 ? gradientStopCount : 2;
            stopCountForDraw = Math.Clamp(stopCountForDraw, 2, FillGradientMaxStops);

            Span<SdfGradientStop> stops = stackalloc SdfGradientStop[FillGradientMaxStops];
            if (gradientStopCount >= 2)
            {
                for (int stopIndex = 0; stopIndex < stopCountForDraw; stopIndex++)
                {
                    float t = GetFillGradientStopT(fillView, stopIndex);
                    Color32 stopColor = GetFillGradientStopColor(fillView, stopIndex);
                    stops[stopIndex] = new SdfGradientStop
                    {
                        Color = ImStyle.ToVector4(ToArgb(stopColor)),
                        Params = new Vector4(t, 0f, 0f, 0f)
                    };
                }
            }
            else
            {
                stops[0] = new SdfGradientStop
                {
                    Color = ImStyle.ToVector4(ToArgb(gradientColorA)),
                    Params = new Vector4(0f, 0f, 0f, 0f)
                };
                stops[1] = new SdfGradientStop
                {
                    Color = ImStyle.ToVector4(ToArgb(gradientColorB)),
                    Params = new Vector4(1f, 0f, 0f, 0f)
                };
            }

            Im.DrawRoundedRectGradientStops(swatchRect.X, swatchRect.Y, swatchRect.Width, swatchRect.Height, 2f, stops.Slice(0, stopCountForDraw));
        }
        else
        {
            Im.DrawRoundedRect(swatchRect.X, swatchRect.Y, swatchRect.Width, swatchRect.Height, 2f, ToArgb(color));
        }

        Im.DrawRoundedRectStroke(swatchRect.X, swatchRect.Y, swatchRect.Width, swatchRect.Height, 2f, Im.Style.Border, 1f);

        uint argb = useGradient ? ToArgb(gradientColorA) : ToArgb(color);

        float contentX = swatchRect.Right + 8f;
        float contentRight = rect.Right - 8f;
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;

        bool showKeyIcon = false;
        bool hasTrack = false;
        bool hasKey = false;
        PropertySlot fillColorSlot = default;
        ImRect keyIconRect = default;
        if (!useGradient && TryGetFillColorSlot(fillHandle, out fillColorSlot))
        {
            showKeyIcon = _commands.TryGetKeyableState(fillColorSlot, out hasTrack, out hasKey);
            if (showKeyIcon)
            {
                float iconRight = rect.Right - 8f;
                keyIconRect = new ImRect(iconRight - KeyIconWidth, rect.Y, KeyIconWidth, rect.Height);
                contentRight = keyIconRect.X;
            }
        }

        Im.Context.PushId(fillHandle.GetHashCode());
        int hexFocusWidgetId = Im.Context.GetId("hex");
        int opacityTextFocusWidgetId = Im.Context.GetId("opacity_text");
        EnsureFillPreviewOpacityCache(fillHandle);

        bool openRequested;
        if (useGradient)
        {
            Im.Text("Gradient".AsSpan(), contentX, textY, Im.Style.FontSize, Im.Style.TextPrimary);

            bool opacityHovered = DrawOpacityField(rect, contentRight, ref argb, _fillPreviewOpacityBuffer!, ref _fillPreviewOpacityLength, ref _fillPreviewOpacityLastAlpha, allowScrub: true);

            byte newAlpha = (byte)(argb >> 24);
            if (gradientStopCount > 0)
            {
                for (int stopIndex = 0; stopIndex < gradientStopCount; stopIndex++)
                {
                    Color32 stopColor = GetFillGradientStopColor(fillView, stopIndex);
                    if (stopColor.A != newAlpha)
                    {
                        SetFillGradientStopColor(ref fillView, stopIndex, new Color32(stopColor.R, stopColor.G, stopColor.B, newAlpha));
                    }
                }
                SyncFillGradientEndpoints(ref fillView);
            }
            else
            {
                if (fillView.GradientColorA.A != newAlpha)
                {
                    fillView.GradientColorA = new Color32(fillView.GradientColorA.R, fillView.GradientColorA.G, fillView.GradientColorA.B, newAlpha);
                }
                if (fillView.GradientColorB.A != newAlpha)
                {
                    fillView.GradientColorB = new Color32(fillView.GradientColorB.R, fillView.GradientColorB.G, fillView.GradientColorB.B, newAlpha);
                }
            }

            openRequested = Im.MousePressed && (swatchRect.Contains(Im.MousePos) || (hovered && !opacityHovered));
        }
        else
        {
            EnsureFillPreviewHexCache(fillHandle, argb);

            const float opacityFieldWidth = 56f;
            float opacityFieldLeft = contentRight - opacityFieldWidth;
            float hexFieldWidth = Math.Max(1f, opacityFieldLeft - contentX);
            float hexInputWidth = hexFieldWidth;
            var hexHoverRect = new ImRect(contentX, rect.Y, hexFieldWidth, rect.Height);
            var hexInputRect = new ImRect(contentX, rect.Y, hexInputWidth, rect.Height);

            int hexWidgetId = Im.Context.GetId("hex");
            bool wasFocused = Im.Context.IsFocused(hexWidgetId);
            bool changed = Im.TextInput("hex", _fillPreviewHexBuffer!, ref _fillPreviewHexLength, 8, hexInputRect.X, hexInputRect.Y, hexInputRect.Width,
                Im.ImTextInputFlags.NoBackground | Im.ImTextInputFlags.NoRounding | Im.ImTextInputFlags.BorderLeft);
            bool isFocused = Im.Context.IsFocused(hexWidgetId);

            if (changed)
            {
                bool ok = TryApplyHexToArgb(_fillPreviewHexBuffer.AsSpan(0, _fillPreviewHexLength), ref argb);
                if (ok)
                {
                    _fillPreviewHexLastArgb = argb;
                    _fillPreviewHexLength = 6;
                    WriteHex6((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, _fillPreviewHexBuffer.AsSpan(0, 6));
                }
            }

            if (wasFocused && !isFocused)
            {
                bool ok = TryApplyHexToArgb(_fillPreviewHexBuffer.AsSpan(0, _fillPreviewHexLength), ref argb);
                if (!ok)
                {
                    _fillPreviewHexLength = 6;
                    WriteHex6((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, _fillPreviewHexBuffer.AsSpan(0, 6));
                }
                _fillPreviewHexLastArgb = argb;
            }

            bool opacityHovered = DrawOpacityField(rect, contentRight, ref argb, _fillPreviewOpacityBuffer!, ref _fillPreviewOpacityLength, ref _fillPreviewOpacityLastAlpha, allowScrub: true);

            Color32 newColor = FromArgb(argb);
            if (fillView.Color != newColor)
            {
                fillView.Color = newColor;
            }

            bool iconHovered = showKeyIcon && keyIconRect.Contains(Im.MousePos);
            openRequested = Im.MousePressed && !iconHovered && (swatchRect.Contains(Im.MousePos) || (hovered && !opacityHovered && !hexHoverRect.Contains(Im.MousePos)));
        }

        bool isEditing = isActive ||
            Im.Context.IsFocused(hexFocusWidgetId) ||
            Im.Context.IsFocused(opacityTextFocusWidgetId) ||
            _opacityDragWidgetId == opacityTextFocusWidgetId ||
            _opacityPressWidgetId == opacityTextFocusWidgetId;
        Im.Context.PopId();

        if (showKeyIcon)
        {
            DrawKeyIconDiamond(keyIconRect, filled: hasKey, highlighted: hasTrack);
            if (keyIconRect.Contains(Im.MousePos) && Im.MousePressed)
            {
                _commands.ToggleKeyAtPlayhead(fillColorSlot);
                openRequested = false;
            }
        }

        FillComponent after = SnapshotFill(fillView);
        _commands.RecordFillComponentEdit(widgetId, isEditing, fillHandle, before, after);

        return openRequested;
    }

    private int GetOrCreateColorPreviewState(int widgetId)
    {
        if (_colorPreviewWidgetIds == null)
        {
            _colorPreviewCapacity = 16;
            _colorPreviewWidgetIds = new int[_colorPreviewCapacity];
            _colorPreviewHexBuffers = new char[_colorPreviewCapacity][];
            _colorPreviewHexLengths = new int[_colorPreviewCapacity];
            _colorPreviewHexLastArgb = new uint[_colorPreviewCapacity];
            _colorPreviewOpacityBuffers = new char[_colorPreviewCapacity][];
            _colorPreviewOpacityLengths = new int[_colorPreviewCapacity];
            _colorPreviewOpacityLastAlpha = new byte[_colorPreviewCapacity];
            _colorPreviewCount = 0;
        }

        for (int i = 0; i < _colorPreviewCount; i++)
        {
            if (_colorPreviewWidgetIds[i] == widgetId)
            {
                return i;
            }
        }

        if (_colorPreviewCount >= _colorPreviewCapacity)
        {
            int newCapacity = Math.Min(256, Math.Max(_colorPreviewCapacity * 2, 16));
            Array.Resize(ref _colorPreviewWidgetIds, newCapacity);
            Array.Resize(ref _colorPreviewHexBuffers, newCapacity);
            Array.Resize(ref _colorPreviewHexLengths, newCapacity);
            Array.Resize(ref _colorPreviewHexLastArgb, newCapacity);
            Array.Resize(ref _colorPreviewOpacityBuffers, newCapacity);
            Array.Resize(ref _colorPreviewOpacityLengths, newCapacity);
            Array.Resize(ref _colorPreviewOpacityLastAlpha, newCapacity);
            _colorPreviewCapacity = newCapacity;
        }

        int index = _colorPreviewCount++;
        _colorPreviewWidgetIds[index] = widgetId;
        _colorPreviewHexBuffers![index] = new char[16];
        _colorPreviewHexLengths![index] = -1;
        _colorPreviewHexLastArgb![index] = 0u;
        _colorPreviewOpacityBuffers![index] = new char[8];
        _colorPreviewOpacityLengths![index] = -1;
        _colorPreviewOpacityLastAlpha![index] = 0;
        return index;
    }

    private void EnsureFillPreviewHexCache(FillComponentHandle fillHandle, uint argb)
    {
        if (_fillPreviewHexBuffer == null)
        {
            _fillPreviewHexBuffer = new char[16];
            _fillPreviewHexLength = -1;
            _fillPreviewHexLastArgb = 0;
            _fillPreviewHexHandle = default;
        }

        if (!_fillPreviewHexHandle.Equals(fillHandle))
        {
            _fillPreviewHexHandle = fillHandle;
            _fillPreviewHexLastArgb = 0;
            _fillPreviewHexLength = -1;
        }

        int hexWidgetId = Im.Context.GetId("hex");
        bool isFocused = Im.Context.IsFocused(hexWidgetId);
        if (!isFocused && (_fillPreviewHexLength < 0 || argb != _fillPreviewHexLastArgb))
        {
            _fillPreviewHexLength = 6;
            WriteHex6((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, _fillPreviewHexBuffer.AsSpan(0, 6));
            _fillPreviewHexLastArgb = argb;
        }
    }

    private void EnsureFillPreviewOpacityCache(FillComponentHandle fillHandle)
    {
        if (_fillPreviewOpacityBuffer == null)
        {
            _fillPreviewOpacityBuffer = new char[8];
            _fillPreviewOpacityLength = -1;
            _fillPreviewOpacityLastAlpha = 0;
            _fillPreviewOpacityHandle = default;
        }

        if (!_fillPreviewOpacityHandle.Equals(fillHandle))
        {
            _fillPreviewOpacityHandle = fillHandle;
            _fillPreviewOpacityLength = -1;
            _fillPreviewOpacityLastAlpha = 0;
        }
    }

    private void EnsureFillPopoverStopCaches(FillComponentHandle fillHandle)
    {
        const int stopCount = FillGradientMaxStops;
        if (_fillPopoverStopHexBuffers == null)
        {
            _fillPopoverStopHexBuffers = new char[stopCount][];
            _fillPopoverStopHexLengths = new int[stopCount];
            _fillPopoverStopHexLastArgb = new uint[stopCount];
            _fillPopoverStopOpacityBuffers = new char[stopCount][];
            _fillPopoverStopOpacityLengths = new int[stopCount];
            _fillPopoverStopOpacityLastAlpha = new byte[stopCount];
            _fillPopoverStopCacheHandle = default;

            for (int stopIndex = 0; stopIndex < stopCount; stopIndex++)
            {
                _fillPopoverStopHexBuffers[stopIndex] = new char[16];
                _fillPopoverStopHexLengths[stopIndex] = -1;
                _fillPopoverStopHexLastArgb[stopIndex] = 0;

                _fillPopoverStopOpacityBuffers[stopIndex] = new char[8];
                _fillPopoverStopOpacityLengths[stopIndex] = -1;
                _fillPopoverStopOpacityLastAlpha[stopIndex] = 0;
            }
        }

        if (!_fillPopoverStopCacheHandle.Equals(fillHandle))
        {
            _fillPopoverStopCacheHandle = fillHandle;
            for (int stopIndex = 0; stopIndex < stopCount; stopIndex++)
            {
                _fillPopoverStopHexLengths![stopIndex] = -1;
                _fillPopoverStopHexLastArgb![stopIndex] = 0;
                _fillPopoverStopOpacityLengths![stopIndex] = -1;
                _fillPopoverStopOpacityLastAlpha![stopIndex] = 0;
            }
        }
    }

    private void ShiftFillPopoverStopCachesForInsert(int insertIndex, int currentStopCount)
    {
        if (_fillPopoverStopHexBuffers == null || _fillPopoverStopHexLengths == null || _fillPopoverStopHexLastArgb == null ||
            _fillPopoverStopOpacityBuffers == null || _fillPopoverStopOpacityLengths == null || _fillPopoverStopOpacityLastAlpha == null)
        {
            return;
        }

        int stopCount = Math.Clamp(currentStopCount, 0, FillGradientMaxStops);
        if (stopCount >= FillGradientMaxStops)
        {
            return;
        }

        insertIndex = Math.Clamp(insertIndex, 0, stopCount);
        for (int index = stopCount; index > insertIndex; index--)
        {
            CopyFillPopoverStopCache(index - 1, index);
        }
        ResetFillPopoverStopCache(insertIndex);
    }

    private void ShiftFillPopoverStopCachesForRemove(int removeIndex, int currentStopCount)
    {
        if (_fillPopoverStopHexBuffers == null || _fillPopoverStopHexLengths == null || _fillPopoverStopHexLastArgb == null ||
            _fillPopoverStopOpacityBuffers == null || _fillPopoverStopOpacityLengths == null || _fillPopoverStopOpacityLastAlpha == null)
        {
            return;
        }

        int stopCount = Math.Clamp(currentStopCount, 0, FillGradientMaxStops);
        if (stopCount <= 0)
        {
            return;
        }

        removeIndex = Math.Clamp(removeIndex, 0, stopCount - 1);
        for (int index = removeIndex; index < stopCount - 1; index++)
        {
            CopyFillPopoverStopCache(index + 1, index);
        }
        ResetFillPopoverStopCache(stopCount - 1);
    }

    private void CopyFillPopoverStopCache(int srcIndex, int dstIndex)
    {
        if (_fillPopoverStopHexBuffers == null || _fillPopoverStopHexLengths == null || _fillPopoverStopHexLastArgb == null ||
            _fillPopoverStopOpacityBuffers == null || _fillPopoverStopOpacityLengths == null || _fillPopoverStopOpacityLastAlpha == null)
        {
            return;
        }

        if ((uint)srcIndex >= FillGradientMaxStops || (uint)dstIndex >= FillGradientMaxStops)
        {
            return;
        }

        Array.Copy(_fillPopoverStopHexBuffers[srcIndex], _fillPopoverStopHexBuffers[dstIndex], _fillPopoverStopHexBuffers[dstIndex].Length);
        _fillPopoverStopHexLengths[dstIndex] = _fillPopoverStopHexLengths[srcIndex];
        _fillPopoverStopHexLastArgb[dstIndex] = _fillPopoverStopHexLastArgb[srcIndex];

        Array.Copy(_fillPopoverStopOpacityBuffers[srcIndex], _fillPopoverStopOpacityBuffers[dstIndex], _fillPopoverStopOpacityBuffers[dstIndex].Length);
        _fillPopoverStopOpacityLengths[dstIndex] = _fillPopoverStopOpacityLengths[srcIndex];
        _fillPopoverStopOpacityLastAlpha[dstIndex] = _fillPopoverStopOpacityLastAlpha[srcIndex];
    }

    private void ResetFillPopoverStopCache(int stopIndex)
    {
        if (_fillPopoverStopHexLengths == null || _fillPopoverStopHexLastArgb == null ||
            _fillPopoverStopOpacityLengths == null || _fillPopoverStopOpacityLastAlpha == null)
        {
            return;
        }

        if ((uint)stopIndex >= FillGradientMaxStops)
        {
            return;
        }

        _fillPopoverStopHexLengths[stopIndex] = -1;
        _fillPopoverStopHexLastArgb[stopIndex] = 0;
        _fillPopoverStopOpacityLengths[stopIndex] = -1;
        _fillPopoverStopOpacityLastAlpha[stopIndex] = 0;
    }

    private void OpenColorPopoverForFill(int widgetId, FillComponentHandle fillHandle, float x, float y, float width, float height)
    {
        var viewport = Im.CurrentViewport;
        if (viewport == null)
        {
            return;
        }

        Vector2 translation = Im.CurrentTranslation;
        _colorPopoverKind = InspectorColorPopoverKind.Fill;
        _colorPopoverWidgetId = widgetId;
        _colorPopoverOpenedFrame = Im.Context.FrameCount;
        _colorPopoverPickerId = null;
        _colorPopoverSlot = default;
        _colorPopoverTargetEntity = _commands.TryGetSelectionTarget(out EntityId entity) ? entity : EntityId.Null;
        _colorPopoverViewport = viewport;
        _colorPopoverAnchorRectViewport = new ImRect(x + translation.X, y + translation.Y, width, height);
        _colorPopoverFillHandle = fillHandle;
        _colorPopoverFillActiveStopIndex = 0;
        _colorPopoverInspectorLeftViewport = TryGetInspectorLeftViewport();
    }

    private bool DrawEditableColorPreviewRow(
        string label,
        float x,
        float y,
        float labelWidth,
        float inputWidth,
        ref uint argb,
        ref uint lastArgb,
        Span<char> hexBuffer,
        ref int hexLength,
        ref byte lastAlpha,
        Span<char> opacityBuffer,
        ref int opacityLength,
        bool allowHex,
        bool allowOpacity,
        bool allowOpacityScrub,
        bool isActive,
        bool reserveColorKeyIcon,
        bool reserveOpacityKeyIcon,
        out ImRect previewRect,
        out ImRect colorKeyIconRect,
        out ImRect opacityKeyIconRect)
    {
        colorKeyIconRect = default;
        opacityKeyIconRect = default;

        float height = Im.Style.MinButtonHeight;

        var labelRect = new ImRect(x, y, labelWidth, height);
        float labelTextY = labelRect.Y + (height - Im.Style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), labelRect.X, labelTextY, Im.Style.FontSize, Im.Style.TextPrimary);

        previewRect = new ImRect(x + labelWidth, y, inputWidth, height);
        bool hovered = previewRect.Contains(Im.MousePos);

        uint background = hovered ? Im.Style.Hover : Im.Style.Surface;
        uint border = isActive ? Im.Style.Primary : (hovered ? Im.Style.Primary : Im.Style.Border);
        Im.DrawRoundedRect(previewRect.X, previewRect.Y, previewRect.Width, previewRect.Height, Im.Style.CornerRadius, background);
        Im.DrawRoundedRectStroke(previewRect.X, previewRect.Y, previewRect.Width, previewRect.Height, Im.Style.CornerRadius, border, Im.Style.BorderWidth);

        float swatchSize = Math.Max(ColorPreviewSwatchSizeMin, previewRect.Height - ColorPreviewSwatchPadding * 2f);
        float swatchX = previewRect.X + ColorPreviewSwatchPadding;
        float swatchY = previewRect.Y + (previewRect.Height - swatchSize) * 0.5f;
        var swatchRect = new ImRect(swatchX, swatchY, swatchSize, swatchSize);
        DrawCheckerboard(swatchRect, 5f);
        Im.DrawRoundedRect(swatchRect.X, swatchRect.Y, swatchRect.Width, swatchRect.Height, 2f, argb);
        Im.DrawRoundedRectStroke(swatchRect.X, swatchRect.Y, swatchRect.Width, swatchRect.Height, 2f, Im.Style.Border, 1f);

        float contentX = swatchRect.Right + 8f;
        float contentRight = previewRect.Right - 8f;
        if (reserveOpacityKeyIcon && allowOpacity)
        {
            float iconRight = previewRect.Right - 8f;
            opacityKeyIconRect = new ImRect(iconRight - KeyIconWidth, previewRect.Y, KeyIconWidth, previewRect.Height);
            contentRight = opacityKeyIconRect.X;
        }

        bool hexHovered = false;

        float opacityFieldWidth = allowOpacity ? 56f : 0f;
        float opacityFieldLeft = contentRight - opacityFieldWidth;

        if (allowHex)
        {
            int hexWidgetId = Im.Context.GetId("hex");
            bool isHexFocused = Im.Context.IsFocused(hexWidgetId);

            if (!isHexFocused && (hexLength < 0 || argb != lastArgb))
            {
                hexLength = 6;
                WriteHex6((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, hexBuffer.Slice(0, 6));
                lastArgb = argb;
            }

            float hexFieldWidth = Math.Max(1f, opacityFieldLeft - contentX);
            float hexFieldX = contentX;
            float hexFieldY = previewRect.Y;
            float hexInputWidth = hexFieldWidth;
            if (reserveColorKeyIcon)
            {
                hexInputWidth = Math.Max(1f, hexFieldWidth - KeyIconWidth);
                colorKeyIconRect = new ImRect(hexFieldX + hexInputWidth, previewRect.Y, KeyIconWidth, previewRect.Height);
            }

            var hexHoverRect = new ImRect(hexFieldX, hexFieldY, hexFieldWidth, previewRect.Height);
            var hexInputRect = new ImRect(hexFieldX, hexFieldY, hexInputWidth, previewRect.Height);
            hexHovered = hexHoverRect.Contains(Im.MousePos);

            bool wasFocused = isHexFocused;
            bool changed = Im.TextInput("hex", hexBuffer, ref hexLength, 8, hexInputRect.X, hexInputRect.Y, hexInputRect.Width,
                Im.ImTextInputFlags.NoBackground | Im.ImTextInputFlags.NoRounding | Im.ImTextInputFlags.BorderLeft);
            isHexFocused = Im.Context.IsFocused(hexWidgetId);

            if (changed)
            {
                bool ok = TryApplyHexToArgb(hexBuffer.Slice(0, hexLength), ref argb);
                if (ok)
                {
                    lastArgb = argb;
                    hexLength = 6;
                    EnsureHexLength(ref hexLength);
                    WriteHex6((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, hexBuffer.Slice(0, 6));
                }
            }

            if (wasFocused && !isHexFocused)
            {
                bool ok = TryApplyHexToArgb(hexBuffer.Slice(0, hexLength), ref argb);
                if (!ok)
                {
                    hexLength = 6;
                    EnsureHexLength(ref hexLength);
                    WriteHex6((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, hexBuffer.Slice(0, 6));
                }
                lastArgb = argb;
            }

            contentX = hexFieldX + hexFieldWidth;
        }

        bool opacityHovered = false;
        if (allowOpacity)
        {
            opacityHovered = DrawOpacityField(previewRect, contentRight, ref argb, opacityBuffer, ref opacityLength, ref lastAlpha, allowOpacityScrub);
        }

        bool swatchHovered = swatchRect.Contains(Im.MousePos);
        if (swatchHovered || (hovered && !hexHovered && !opacityHovered))
        {
            Im.SetCursor(Silk.NET.Input.StandardCursor.Hand);
        }

        bool clickedToOpen = Im.MousePressed && (swatchHovered || (hovered && !opacityHovered));
        if (allowHex && hexHovered)
        {
            clickedToOpen = false;
        }
        if (opacityHovered)
        {
            clickedToOpen = false;
        }

        return clickedToOpen;
    }

    private static readonly StringHandle FillGroup = "Fill";
    private static readonly StringHandle FillColorName = "Color";

    private bool TryGetFillColorSlot(FillComponentHandle fillHandle, out PropertySlot slot)
    {
        slot = default;

        if (fillHandle.IsNull)
        {
            return false;
        }

        AnyComponentHandle component = FillComponentProperties.ToAnyHandle(fillHandle);
        int propertyCount = PropertyDispatcher.GetPropertyCount(component);
        if (propertyCount <= 0)
        {
            return false;
        }

        for (int i = 0; i < propertyCount; i++)
        {
            if (!PropertyDispatcher.TryGetInfo(component, i, out PropertyInfo info))
            {
                continue;
            }

            if (info.IsChannel)
            {
                continue;
            }

            if (info.Group == FillGroup && info.Name == FillColorName && info.Kind == PropertyKind.Color32)
            {
                slot = PropertyDispatcher.GetSlot(component, i);
                return !slot.Component.IsNull;
            }
        }

        return false;
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

    private static void EnsureHexLength(ref int length)
    {
        if (length < 0)
        {
            length = 0;
        }
        if (length > 8)
        {
            length = 8;
        }
    }

    private bool DrawOpacityField(
        ImRect previewRect,
        float fieldRight,
        ref uint argb,
        Span<char> buffer,
        ref int length,
        ref byte lastAlpha,
        bool allowScrub)
    {
        int alphaByte = (int)(argb >> 24) & 0xFF;
        byte alpha = (byte)alphaByte;
        int widgetId = Im.Context.GetId("opacity_text");
        bool isFocused = Im.Context.IsFocused(widgetId);

        if (!isFocused && (length < 0 || alpha != lastAlpha))
        {
            int percent = AlphaToPercent(alpha);
            length = WritePercentDigits(percent, buffer);
            lastAlpha = alpha;
        }

        float fieldWidth = 56f;
        float percentGlyphWidth = ApproxTextWidth("%".AsSpan(), Im.Style.FontSize);
        float percentPadding = 4f;
        float inputWidth = Math.Max(18f, fieldWidth - percentGlyphWidth - percentPadding);

        float fieldX = fieldRight - fieldWidth;
        var fieldRect = new ImRect(fieldX, previewRect.Y, fieldWidth, previewRect.Height);
        var inputRect = new ImRect(fieldRect.X, fieldRect.Y, inputWidth, fieldRect.Height);

        bool hovered = fieldRect.Contains(Im.MousePos);

        float separatorX = fieldRect.X + 0.5f;
        uint separatorColor = Im.Style.Border;
        Im.DrawLine(separatorX, fieldRect.Y + 1f, separatorX, fieldRect.Bottom - 1f, 1f, separatorColor);

        bool wasFocused = isFocused;
        if (!isFocused)
        {
            if (hovered)
            {
                Im.SetCursor(Silk.NET.Input.StandardCursor.HResize);
            }

            if (allowScrub && hovered && Im.MousePressed)
            {
                _opacityPressWidgetId = widgetId;
                _opacityPressStartMouseX = Im.MousePos.X;
                _opacityDragStartAlpha = alpha;
            }

            if (_opacityPressWidgetId == widgetId)
            {
                if (Im.MouseDown)
                {
                    float deltaX = Im.MousePos.X - _opacityPressStartMouseX;
                    if (MathF.Abs(deltaX) >= 2f)
                    {
                        _opacityPressWidgetId = 0;
                        _opacityDragWidgetId = widgetId;
                        _opacityDragStartMouseX = _opacityPressStartMouseX;
                    }
                }
                else
                {
                    _opacityPressWidgetId = 0;
                    Im.Context.RequestFocus(widgetId);
                }
            }

            if (_opacityDragWidgetId == widgetId)
            {
                Im.SetCursor(Silk.NET.Input.StandardCursor.HResize);
                if (Im.MouseDown)
                {
                    float deltaX = Im.MousePos.X - _opacityDragStartMouseX;
                    float alphaF = _opacityDragStartAlpha + deltaX * 1.5f;
                    int newAlphaInt = (int)MathF.Round(alphaF);
                    newAlphaInt = Math.Clamp(newAlphaInt, 0, 255);
                    uint newArgb = (argb & 0x00FFFFFFu) | ((uint)newAlphaInt << 24);
                    if (newArgb != argb)
                    {
                        argb = newArgb;
                        alpha = (byte)(argb >> 24);
                        lastAlpha = alpha;
                        length = WritePercentDigits(AlphaToPercent(alpha), buffer);
                    }
                }
                else
                {
                    _opacityDragWidgetId = 0;
                }
            }

            int displayLen = Math.Clamp(length, 0, buffer.Length);
            float digitsX = inputRect.X + Im.Style.Padding;
            float textY = fieldRect.Y + (fieldRect.Height - Im.Style.FontSize) * 0.5f;
            if (displayLen > 0)
            {
                Im.Text(buffer.Slice(0, displayLen), digitsX, textY, Im.Style.FontSize, Im.Style.TextPrimary);
            }
        }
        else
        {
            bool textChanged = Im.TextInput("opacity_text", buffer, ref length, 4, inputRect.X, inputRect.Y, inputRect.Width,
                Im.ImTextInputFlags.NoBackground | Im.ImTextInputFlags.NoRounding | Im.ImTextInputFlags.BorderLeft);
            isFocused = Im.Context.IsFocused(widgetId);

            if (wasFocused && !isFocused)
            {
                bool ok = TryParseBytePercent(buffer.Slice(0, Math.Max(0, length)), out int percent);
                if (ok)
                {
                    percent = Math.Clamp(percent, 0, 100);
                    byte newAlpha = PercentToAlpha(percent);
                    argb = (argb & 0x00FFFFFFu) | ((uint)newAlpha << 24);
                    lastAlpha = newAlpha;
                    length = WritePercentDigits(AlphaToPercent(newAlpha), buffer);
                }
                else
                {
                    length = WritePercentDigits(AlphaToPercent(alpha), buffer);
                }
            }
            else if (textChanged)
            {
                bool ok = TryParseBytePercent(buffer.Slice(0, Math.Max(0, length)), out int percent);
                if (ok)
                {
                    percent = Math.Clamp(percent, 0, 100);
                    byte newAlpha = PercentToAlpha(percent);
                    argb = (argb & 0x00FFFFFFu) | ((uint)newAlpha << 24);
                    lastAlpha = newAlpha;
                }
            }
        }

        float percentX = fieldRect.Right - percentGlyphWidth - 2f;
        float percentTextY = fieldRect.Y + (fieldRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text("%".AsSpan(), percentX, percentTextY, Im.Style.FontSize, Im.Style.TextSecondary);

        return hovered;
    }

    private static bool TryApplyHexToArgb(ReadOnlySpan<char> text, ref uint argb)
    {
        int start = 0;
        int end = text.Length;

        while (start < end && text[start] == ' ')
        {
            start++;
        }
        while (end > start && text[end - 1] == ' ')
        {
            end--;
        }
        if (start < end && text[start] == '#')
        {
            start++;
        }

        int len = end - start;
        if (len == 0)
        {
            return false;
        }

        if (len == 3)
        {
            if (!TryParseHexNibble(text[start + 0], out byte r4) ||
                !TryParseHexNibble(text[start + 1], out byte g4) ||
                !TryParseHexNibble(text[start + 2], out byte b4))
            {
                return false;
            }

            byte r = (byte)((r4 << 4) | r4);
            byte g = (byte)((g4 << 4) | g4);
            byte b = (byte)((b4 << 4) | b4);
            argb = (argb & 0xFF000000u) | ((uint)r << 16) | ((uint)g << 8) | b;
            return true;
        }

        if (len != 6)
        {
            return false;
        }

        if (!TryParseHexByte(text[start + 0], text[start + 1], out byte r8) ||
            !TryParseHexByte(text[start + 2], text[start + 3], out byte g8) ||
            !TryParseHexByte(text[start + 4], text[start + 5], out byte b8))
        {
            return false;
        }

        argb = (argb & 0xFF000000u) | ((uint)r8 << 16) | ((uint)g8 << 8) | b8;
        return true;
    }

    private static bool TryParseHexByte(char high, char low, out byte value)
    {
        value = 0;
        if (!TryParseHexNibble(high, out byte hi) || !TryParseHexNibble(low, out byte lo))
        {
            return false;
        }

        value = (byte)((hi << 4) | lo);
        return true;
    }

    private static bool TryParseHexNibble(char c, out byte value)
    {
        if (c >= '0' && c <= '9')
        {
            value = (byte)(c - '0');
            return true;
        }
        if (c >= 'A' && c <= 'F')
        {
            value = (byte)(10 + (c - 'A'));
            return true;
        }
        if (c >= 'a' && c <= 'f')
        {
            value = (byte)(10 + (c - 'a'));
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryParseBytePercent(ReadOnlySpan<char> text, out int percent)
    {
        percent = 0;
        int start = 0;
        int end = text.Length;

        while (start < end && text[start] == ' ')
        {
            start++;
        }
        while (end > start && text[end - 1] == ' ')
        {
            end--;
        }

        int len = end - start;
        if (len <= 0)
        {
            return false;
        }

        int value = 0;
        int digitCount = 0;
        for (int i = 0; i < len; i++)
        {
            char c = text[start + i];
            if (c == '%')
            {
                continue;
            }
            if (c < '0' || c > '9')
            {
                return false;
            }
            digitCount++;
            value = value * 10 + (c - '0');
            if (value > 1000)
            {
                value = 1000;
            }
        }

        if (digitCount == 0)
        {
            return false;
        }

        percent = value;
        return true;
    }

    private static int AlphaToPercent(byte alpha)
    {
        return (alpha * 100 + 127) / 255;
    }

    private static byte PercentToAlpha(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        int alpha = (percent * 255 + 50) / 100;
        return (byte)Math.Clamp(alpha, 0, 255);
    }

    private static int WritePercentDigits(int percent, Span<char> buffer)
    {
        percent = Math.Clamp(percent, 0, 100);
        if (percent >= 100)
        {
            if (buffer.Length >= 3)
            {
                buffer[0] = '1';
                buffer[1] = '0';
                buffer[2] = '0';
                return 3;
            }
            return 0;
        }
        if (percent >= 10)
        {
            if (buffer.Length >= 2)
            {
                buffer[0] = (char)('0' + (percent / 10));
                buffer[1] = (char)('0' + (percent % 10));
                return 2;
            }
            return 0;
        }

        if (buffer.Length >= 1)
        {
            buffer[0] = (char)('0' + percent);
            return 1;
        }
        return 0;
    }

    private void EnsurePropertyColorHexCache(int propertyCount)
    {
        if (propertyCount <= 0)
        {
            return;
        }

        if (_propertyColorHexCapacity >= propertyCount &&
            _propertyColorHexBuffers != null &&
            _propertyColorHexLengths != null &&
            _propertyColorHexLastArgb != null)
        {
            return;
        }

        int newCapacity = Math.Max(propertyCount, Math.Max(8, _propertyColorHexCapacity * 2));
        var newBuffers = new char[newCapacity][];
        var newLengths = new int[newCapacity];
        var newLastArgb = new uint[newCapacity];

        if (_propertyColorHexBuffers != null && _propertyColorHexLengths != null && _propertyColorHexLastArgb != null)
        {
            Array.Copy(_propertyColorHexBuffers, newBuffers, Math.Min(_propertyColorHexCapacity, newCapacity));
            Array.Copy(_propertyColorHexLengths, newLengths, Math.Min(_propertyColorHexCapacity, newCapacity));
            Array.Copy(_propertyColorHexLastArgb, newLastArgb, Math.Min(_propertyColorHexCapacity, newCapacity));
        }

        _propertyColorHexBuffers = newBuffers;
        _propertyColorHexLengths = newLengths;
        _propertyColorHexLastArgb = newLastArgb;
        _propertyColorHexCapacity = newCapacity;
    }

    private Span<char> EnsurePropertyColorHexBuffer(int propertyIndex)
    {
        if (_propertyColorHexBuffers == null)
        {
            EnsurePropertyColorHexCache(propertyIndex + 1);
        }

        char[]? buffer = _propertyColorHexBuffers![propertyIndex];
        if (buffer == null)
        {
            buffer = new char[16];
            _propertyColorHexBuffers[propertyIndex] = buffer;
            _propertyColorHexLengths![propertyIndex] = -1;
            _propertyColorHexLastArgb![propertyIndex] = 0;
        }

        return buffer;
    }

    private void EnsurePropertyOpacityCache(int propertyCount)
    {
        if (propertyCount <= 0)
        {
            return;
        }

        if (_propertyOpacityCapacity >= propertyCount &&
            _propertyOpacityBuffers != null &&
            _propertyOpacityLengths != null &&
            _propertyOpacityLastAlpha != null)
        {
            return;
        }

        int newCapacity = Math.Max(propertyCount, Math.Max(8, _propertyOpacityCapacity * 2));
        var newBuffers = new char[newCapacity][];
        var newLengths = new int[newCapacity];
        var newLastAlpha = new byte[newCapacity];

        if (_propertyOpacityBuffers != null && _propertyOpacityLengths != null && _propertyOpacityLastAlpha != null)
        {
            Array.Copy(_propertyOpacityBuffers, newBuffers, Math.Min(_propertyOpacityCapacity, newCapacity));
            Array.Copy(_propertyOpacityLengths, newLengths, Math.Min(_propertyOpacityCapacity, newCapacity));
            Array.Copy(_propertyOpacityLastAlpha, newLastAlpha, Math.Min(_propertyOpacityCapacity, newCapacity));
        }

        _propertyOpacityBuffers = newBuffers;
        _propertyOpacityLengths = newLengths;
        _propertyOpacityLastAlpha = newLastAlpha;
        _propertyOpacityCapacity = newCapacity;
    }

    private Span<char> EnsurePropertyOpacityBuffer(int propertyIndex)
    {
        if (_propertyOpacityBuffers == null)
        {
            EnsurePropertyOpacityCache(propertyIndex + 1);
        }

        char[]? buffer = _propertyOpacityBuffers![propertyIndex];
        if (buffer == null)
        {
            buffer = new char[8];
            _propertyOpacityBuffers[propertyIndex] = buffer;
            _propertyOpacityLengths![propertyIndex] = -1;
            _propertyOpacityLastAlpha![propertyIndex] = 0;
        }

        return buffer;
    }

    private static FillComponent SnapshotFill(in FillComponent.ViewProxy fillView)
    {
        return new FillComponent
        {
            Color = fillView.Color,
            UseGradient = fillView.UseGradient,
            GradientColorA = fillView.GradientColorA,
            GradientColorB = fillView.GradientColorB,
            GradientMix = fillView.GradientMix,
            GradientDirection = fillView.GradientDirection,
            GradientStopCount = fillView.GradientStopCount,
            GradientStopT0To3 = fillView.GradientStopT0To3,
            GradientStopT4To7 = fillView.GradientStopT4To7,
            GradientStopColor0 = fillView.GradientStopColor0,
            GradientStopColor1 = fillView.GradientStopColor1,
            GradientStopColor2 = fillView.GradientStopColor2,
            GradientStopColor3 = fillView.GradientStopColor3,
            GradientStopColor4 = fillView.GradientStopColor4,
            GradientStopColor5 = fillView.GradientStopColor5,
            GradientStopColor6 = fillView.GradientStopColor6,
            GradientStopColor7 = fillView.GradientStopColor7,
        };
    }

    private static int GetFillEditWidgetId(FillComponentHandle fillHandle)
    {
        if (fillHandle.IsNull)
        {
            return 0;
        }

        Im.Context.PushId(fillHandle.GetHashCode());
        int widgetId = Im.Context.GetId("fill_edit");
        Im.Context.PopId();
        return widgetId;
    }

    private void OpenColorPopoverForProperty(string pickerId, PropertySlot slot, float previewX, float previewY, float previewWidth, float previewHeight)
    {
        var viewport = Im.CurrentViewport;
        if (viewport == null)
        {
            return;
        }

        Vector2 translation = Im.CurrentTranslation;
        _colorPopoverKind = InspectorColorPopoverKind.Color32Property;
        _colorPopoverWidgetId = Im.Context.GetId(pickerId);
        _colorPopoverOpenedFrame = Im.Context.FrameCount;
        _colorPopoverPickerId = pickerId;
        _colorPopoverSlot = slot;
        _colorPopoverTargetEntity = _commands.TryGetSelectionTarget(out EntityId entity) ? entity : EntityId.Null;
        _colorPopoverViewport = viewport;
        _colorPopoverAnchorRectViewport = new ImRect(previewX + translation.X, previewY + translation.Y, previewWidth, previewHeight);
        _colorPopoverInspectorLeftViewport = TryGetInspectorLeftViewport();
        _colorPopoverFillHandle = default;
        _colorPopoverFillActiveStopIndex = 0;
        _colorPopoverPaintHandle = default;
        _colorPopoverPaintLayerIndex = -1;
        _colorPopoverPaintActiveStopIndex = 0;
    }

    private void CloseColorPopover()
    {
        if (_colorPopoverKind != InspectorColorPopoverKind.None && _colorPopoverWidgetId != 0)
        {
            _commands.NotifyPropertyWidgetState(_colorPopoverWidgetId, isEditing: false);
        }

        _colorPopoverKind = InspectorColorPopoverKind.None;
        _colorPopoverWidgetId = 0;
        _colorPopoverOpenedFrame = 0;
        _colorPopoverPickerId = null;
        _colorPopoverSlot = default;
        _colorPopoverTargetEntity = EntityId.Null;
        _colorPopoverViewport = null;
        _colorPopoverAnchorRectViewport = default;
        _colorPopoverMouseDownStartedInside = false;
        _colorPopoverInspectorLeftViewport = null;
        _colorPopoverFillHandle = default;
        _colorPopoverFillActiveStopIndex = 0;
        _colorPopoverPaintHandle = default;
        _colorPopoverPaintLayerIndex = -1;
        _colorPopoverPaintActiveStopIndex = 0;
        _paintGradientStopDragHandle = default;
        _paintGradientStopDragLayerIndex = -1;
        _paintGradientStopDragIndex = -1;
    }

    private float? TryGetInspectorLeftViewport()
    {
        if (_bindingInspector == null)
        {
            return null;
        }

        if (!_bindingInspector.TryGetInspectorContentRectViewport(out ImRect inspectorRectViewport))
        {
            return null;
        }

        return inspectorRectViewport.X;
    }

    private bool TryGetColorPopoverRect(DerpLib.ImGui.Viewport.ImViewport viewport, out ImRect popupRect)
    {
        popupRect = default;
        if (_colorPopoverKind == InspectorColorPopoverKind.None)
        {
            return false;
        }

        if (_colorPopoverViewport != null && _colorPopoverViewport != viewport)
        {
            return false;
        }

        float padding = Im.Style.Padding;
        float popupWidth = Math.Clamp(300f, 200f, Math.Max(200f, viewport.Size.X - InspectorPopoverGap * 2f));
        float contentWidth = Math.Max(160f, popupWidth - padding * 2f);
        float pickerWidth = contentWidth;
        float pickerHeight = pickerWidth + ColorPickerHeightOffset;

        float popupHeight;
        if (_colorPopoverKind == InspectorColorPopoverKind.Fill)
        {
            if (_colorPopoverFillHandle.IsNull)
            {
                return false;
            }

            var fillView = FillComponent.Api.FromHandle(_propertyWorld, _colorPopoverFillHandle);
            if (!fillView.IsAlive)
            {
                return false;
            }

            bool useGradient = fillView.UseGradient;
            int stopCount = useGradient ? GetFillGradientStopCount(fillView) : 0;
            if (useGradient && stopCount == 0)
            {
                stopCount = 2;
            }
            popupHeight = ComputeFillPopoverHeight(pickerHeight, useGradient, stopCount);
        }
        else
        {
            popupHeight = padding * 2f + pickerHeight;
        }

        popupWidth = Math.Min(popupWidth, viewport.Size.X - InspectorPopoverGap * 2f);
        popupHeight = Math.Min(popupHeight, viewport.Size.Y - InspectorPopoverGap * 2f);

        popupRect = PlacePopover(viewport, _colorPopoverAnchorRectViewport, popupWidth, popupHeight, _colorPopoverInspectorLeftViewport);
        return true;
    }

    private static void WriteHex6(byte r, byte g, byte b, Span<char> dest6)
    {
        dest6[0] = NibbleToHex((byte)(r >> 4));
        dest6[1] = NibbleToHex((byte)(r & 0xF));
        dest6[2] = NibbleToHex((byte)(g >> 4));
        dest6[3] = NibbleToHex((byte)(g & 0xF));
        dest6[4] = NibbleToHex((byte)(b >> 4));
        dest6[5] = NibbleToHex((byte)(b & 0xF));
    }

    private static char NibbleToHex(byte nibble)
    {
        return (char)(nibble < 10 ? ('0' + nibble) : ('A' + (nibble - 10)));
    }

    private static void DrawCheckerboard(ImRect rect, float cellSize)
    {
        uint light = 0xFFCCCCCC;
        uint dark = 0xFF999999;

        int cols = (int)MathF.Ceiling(rect.Width / cellSize);
        int rows = (int)MathF.Ceiling(rect.Height / cellSize);

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                bool isLight = (row + col) % 2 == 0;
                float cellX = rect.X + col * cellSize;
                float cellY = rect.Y + row * cellSize;
                float cellW = MathF.Min(cellSize, rect.X + rect.Width - cellX);
                float cellH = MathF.Min(cellSize, rect.Y + rect.Height - cellY);
                Im.DrawRect(cellX, cellY, cellW, cellH, isLight ? light : dark);
            }
        }
    }

    private static ImRect PlacePopover(DerpLib.ImGui.Viewport.ImViewport viewport, ImRect anchorViewport, float width, float height, float? inspectorLeftViewport)
    {
        // Prefer rendering to the left of the anchor (Rive-like inspector popovers).
        float x = inspectorLeftViewport.HasValue
            ? inspectorLeftViewport.Value - InspectorPopoverGap - width
            : anchorViewport.X - width - InspectorPopoverGap;
        float y = anchorViewport.Y;

        if (x < InspectorPopoverGap)
        {
            x = InspectorPopoverGap;
        }
        if (x + width > viewport.Size.X - InspectorPopoverGap)
        {
            x = viewport.Size.X - width - InspectorPopoverGap;
        }

        if (y + height > viewport.Size.Y - InspectorPopoverGap)
        {
            y = viewport.Size.Y - height - InspectorPopoverGap;
        }
        if (y < InspectorPopoverGap)
        {
            y = InspectorPopoverGap;
        }

        return new ImRect(x, y, width, height);
    }

    private float ComputeFillPopoverHeight(float pickerHeight, bool useGradient, int gradientStopCount)
    {
        float padding = Im.Style.Padding;
        float spacing = Im.Style.Spacing;
        float rowHeight = Im.Style.MinButtonHeight;

        float height = padding * 2f;
        height += rowHeight + spacing;
        if (useGradient)
        {
            height += rowHeight + spacing;
            height += 22f + spacing;
            height += rowHeight + spacing;
            int stopCount = Math.Clamp(gradientStopCount, 2, FillGradientMaxStops);
            height += stopCount * (rowHeight + spacing);
        }
        height += pickerHeight;
        return height;
    }

    private void RenderColorPropertyPopoverContents(ImRect popupRect)
    {
        if (_colorPopoverPickerId == null)
        {
            CloseColorPopover();
            return;
        }

        if (_colorPopoverSlot.Component.IsNull)
        {
            CloseColorPopover();
            return;
        }

        float padding = Im.Style.Padding;
        float contentX = popupRect.X + padding;
        float contentY = popupRect.Y + padding;
        float contentWidth = Math.Max(160f, popupRect.Width - padding * 2f);

        Color32 color = PropertyDispatcher.ReadColor32(_propertyWorld, _colorPopoverSlot);
        uint argb = ToArgb(color);
        if (ImColorPicker.DrawAt(_colorPopoverPickerId, contentX, contentY, contentWidth, ref argb, showAlpha: true))
        {
            Color32 newColor = FromArgb(argb);
            if (newColor != color)
            {
                _commands.SetPropertyValue(_colorPopoverWidgetId, isEditing: true, _colorPopoverSlot, PropertyValue.FromColor32(newColor));
            }
        }
    }

    private void OpenColorPopoverForPaintFillLayer(int widgetId, PaintComponentHandle paintHandle, int layerIndex, bool isStrokeLayer, float x, float y, float width, float height)
    {
        var viewport = Im.CurrentViewport;
        if (viewport == null)
        {
            return;
        }

        Vector2 translation = Im.CurrentTranslation;
        _colorPopoverKind = InspectorColorPopoverKind.PaintFillLayer;
        _colorPopoverWidgetId = widgetId;
        _colorPopoverOpenedFrame = Im.Context.FrameCount;
        _colorPopoverPickerId = null;
        _colorPopoverSlot = default;
        _colorPopoverTargetEntity = _commands.TryGetSelectionTarget(out EntityId entity) ? entity : EntityId.Null;
        _colorPopoverViewport = viewport;
        _colorPopoverAnchorRectViewport = new ImRect(x + translation.X, y + translation.Y, width, height);
        _colorPopoverInspectorLeftViewport = TryGetInspectorLeftViewport();
        _colorPopoverFillHandle = default;
        _colorPopoverFillActiveStopIndex = 0;
        _colorPopoverPaintHandle = paintHandle;
        _colorPopoverPaintLayerIndex = layerIndex;
        _colorPopoverPaintActiveStopIndex = 0;
        _colorPopoverPaintIsStrokeLayer = isStrokeLayer;
    }

    private bool DrawPaintFillLayerPreviewRowCore(int widgetId, ImRect rect, PaintComponentHandle paintHandle, int layerIndex, bool isStrokeLayer, bool isActive)
    {
        var paintView = PaintComponent.Api.FromHandle(_propertyWorld, paintHandle);
        if (!paintView.IsAlive)
        {
            return false;
        }

        AnyComponentHandle paintAny = PaintComponentProperties.ToAnyHandle(paintHandle);
        var useGradientSlot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillUseGradient", layerIndex, PropertyKind.Bool);
        bool useGradient = PropertyDispatcher.ReadBool(_propertyWorld, useGradientSlot);

        Color32 solidColor = default;
        PropertySlot solidColorSlot = isStrokeLayer
            ? PaintComponentPropertySlot.ArrayElement(paintAny, "StrokeColor", layerIndex, PropertyKind.Color32)
            : PaintComponentPropertySlot.ArrayElement(paintAny, "FillColor", layerIndex, PropertyKind.Color32);
        solidColor = PropertyDispatcher.ReadColor32(_propertyWorld, solidColorSlot);

        int stopCount = useGradient ? GetPaintFillGradientStopCount(paintHandle, layerIndex) : 0;
        if (useGradient && stopCount == 0)
        {
            stopCount = 2;
        }

        Color32 gradientColorA = default;
        Color32 gradientColorB = default;
        if (useGradient)
        {
            if (stopCount > 0)
            {
                gradientColorA = GetPaintFillGradientStopColor(paintHandle, layerIndex, 0);
                gradientColorB = GetPaintFillGradientStopColor(paintHandle, layerIndex, stopCount - 1);
            }
            else
            {
                var slotA = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientColorA", layerIndex, PropertyKind.Color32);
                var slotB = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientColorB", layerIndex, PropertyKind.Color32);
                gradientColorA = PropertyDispatcher.ReadColor32(_propertyWorld, slotA);
                gradientColorB = PropertyDispatcher.ReadColor32(_propertyWorld, slotB);
            }
        }

        bool hovered = rect.Contains(Im.MousePos);
        uint background = hovered ? Im.Style.Hover : Im.Style.Surface;
        uint border = isActive ? Im.Style.Primary : (hovered ? Im.Style.Primary : Im.Style.Border);
        Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, background);
        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, border, Im.Style.BorderWidth);

        float swatchSize = Math.Max(ColorPreviewSwatchSizeMin, rect.Height - ColorPreviewSwatchPadding * 2f);
        float swatchX = rect.X + ColorPreviewSwatchPadding;
        float swatchY = rect.Y + (rect.Height - swatchSize) * 0.5f;
        var swatchRect = new ImRect(swatchX, swatchY, swatchSize, swatchSize);
        DrawCheckerboard(swatchRect, 5f);

        if (useGradient)
        {
            int actualStopCount = GetPaintFillGradientStopCount(paintHandle, layerIndex);
            int stopCountForDraw = actualStopCount >= 2 ? actualStopCount : 2;
            stopCountForDraw = Math.Clamp(stopCountForDraw, 2, FillGradientMaxStops);

            Span<SdfGradientStop> stops = stackalloc SdfGradientStop[FillGradientMaxStops];
            if (actualStopCount >= 2)
            {
                for (int stopIndex = 0; stopIndex < stopCountForDraw; stopIndex++)
                {
                    float t = GetPaintFillGradientStopT(paintHandle, layerIndex, stopIndex);
                    Color32 stopColor = GetPaintFillGradientStopColor(paintHandle, layerIndex, stopIndex);
                    stops[stopIndex] = new SdfGradientStop
                    {
                        Color = ImStyle.ToVector4(ToArgb(stopColor)),
                        Params = new Vector4(t, 0f, 0f, 0f)
                    };
                }
            }
            else
            {
                stops[0] = new SdfGradientStop
                {
                    Color = ImStyle.ToVector4(ToArgb(gradientColorA)),
                    Params = new Vector4(0f, 0f, 0f, 0f)
                };
                stops[1] = new SdfGradientStop
                {
                    Color = ImStyle.ToVector4(ToArgb(gradientColorB)),
                    Params = new Vector4(1f, 0f, 0f, 0f)
                };
            }

            Im.DrawRoundedRectGradientStops(swatchRect.X, swatchRect.Y, swatchRect.Width, swatchRect.Height, 2f, stops.Slice(0, stopCountForDraw));
        }
        else
        {
            Im.DrawRoundedRect(swatchRect.X, swatchRect.Y, swatchRect.Width, swatchRect.Height, 2f, ToArgb(solidColor));
        }

        Im.DrawRoundedRectStroke(swatchRect.X, swatchRect.Y, swatchRect.Width, swatchRect.Height, 2f, Im.Style.Border, 1f);

        float contentX = swatchRect.Right + 8f;
        float contentRight = rect.Right - 8f;
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;

        uint argb = useGradient ? ToArgb(gradientColorA) : ToArgb(solidColor);
        int stateIndex = GetOrCreateColorPreviewState(widgetId);
        Span<char> hexBuffer = _colorPreviewHexBuffers![stateIndex];
        ref int hexLength = ref _colorPreviewHexLengths![stateIndex];
        ref uint lastArgb = ref _colorPreviewHexLastArgb![stateIndex];
        Span<char> opacityBuffer = _colorPreviewOpacityBuffers![stateIndex];
        ref int opacityLength = ref _colorPreviewOpacityLengths![stateIndex];
        ref byte lastAlpha = ref _colorPreviewOpacityLastAlpha![stateIndex];

        Im.Context.PushId(widgetId);
        int hexFocusWidgetId = Im.Context.GetId("hex");
        int opacityTextFocusWidgetId = Im.Context.GetId("opacity_text");
        bool openRequested;
        if (useGradient)
        {
            Im.Text("Gradient".AsSpan(), contentX, textY, Im.Style.FontSize, Im.Style.TextPrimary);
            bool opacityHovered = DrawOpacityField(rect, contentRight, ref argb, opacityBuffer, ref opacityLength, ref lastAlpha, allowScrub: true);

            byte newAlpha = (byte)(argb >> 24);
            if (stopCount > 0)
            {
                for (int stopIndex = 0; stopIndex < stopCount; stopIndex++)
                {
                    Color32 stopColor = GetPaintFillGradientStopColor(paintHandle, layerIndex, stopIndex);
                    if (stopColor.A != newAlpha)
                    {
                        SetPaintFillGradientStopColor(widgetId, isEditing: true, paintHandle, layerIndex, stopIndex, new Color32(stopColor.R, stopColor.G, stopColor.B, newAlpha));
                    }
                }

                SyncPaintFillGradientEndpoints(widgetId, isEditing: true, paintHandle, layerIndex, stopCount);
            }
            else
            {
                var slotA = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientColorA", layerIndex, PropertyKind.Color32);
                var slotB = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientColorB", layerIndex, PropertyKind.Color32);
                Color32 a = PropertyDispatcher.ReadColor32(_propertyWorld, slotA);
                Color32 b = PropertyDispatcher.ReadColor32(_propertyWorld, slotB);
                if (a.A != newAlpha)
                {
                    _commands.SetPropertyValue(widgetId, isEditing: true, slotA, PropertyValue.FromColor32(new Color32(a.R, a.G, a.B, newAlpha)));
                }
                if (b.A != newAlpha)
                {
                    _commands.SetPropertyValue(widgetId, isEditing: true, slotB, PropertyValue.FromColor32(new Color32(b.R, b.G, b.B, newAlpha)));
                }
            }

            openRequested = Im.MousePressed && (swatchRect.Contains(Im.MousePos) || (hovered && !opacityHovered));
        }
        else
        {
            if (hexLength < 0 || (!Im.Context.IsFocused(hexFocusWidgetId) && lastArgb != argb))
            {
                hexLength = 6;
                lastArgb = argb;
                WriteHex6((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, hexBuffer.Slice(0, 6));
            }

            float hexFieldWidth = Math.Max(1f, contentRight - contentX);
            var hexFieldRect = new ImRect(contentX, rect.Y, hexFieldWidth, rect.Height);

            bool wasFocused = Im.Context.IsFocused(hexFocusWidgetId);
            bool changed = Im.TextInput("hex", hexBuffer, ref hexLength, 8, hexFieldRect.X, hexFieldRect.Y, hexFieldRect.Width,
                Im.ImTextInputFlags.NoBackground | Im.ImTextInputFlags.NoRounding | Im.ImTextInputFlags.BorderLeft);
            bool isFocused = Im.Context.IsFocused(hexFocusWidgetId);

            if (changed)
            {
                bool ok = TryApplyHexToArgb(hexBuffer.Slice(0, Math.Max(0, hexLength)), ref argb);
                if (ok)
                {
                    lastArgb = argb;
                    hexLength = 6;
                    WriteHex6((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, hexBuffer.Slice(0, 6));
                }
            }

            if (wasFocused && !isFocused)
            {
                bool ok = TryApplyHexToArgb(hexBuffer.Slice(0, Math.Max(0, hexLength)), ref argb);
                if (!ok)
                {
                    argb = ToArgb(solidColor);
                    hexLength = 6;
                    WriteHex6((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, hexBuffer.Slice(0, 6));
                }
                lastArgb = argb;
            }

            Color32 newColor = FromArgb(argb);
            if (solidColor != newColor)
            {
                bool isEditing = isActive || isFocused;
                _commands.SetPropertyValue(widgetId, isEditing, solidColorSlot, PropertyValue.FromColor32(newColor));
                solidColor = newColor;
            }

            bool hexHovered = hexFieldRect.Contains(Im.MousePos);
            openRequested = Im.MousePressed && (swatchRect.Contains(Im.MousePos) || (hovered && !hexHovered));
        }
        Im.Context.PopId();

        bool isEditingWidget = isActive ||
            Im.Context.IsFocused(hexFocusWidgetId) ||
            Im.Context.IsFocused(opacityTextFocusWidgetId) ||
            _opacityDragWidgetId == opacityTextFocusWidgetId ||
            _opacityPressWidgetId == opacityTextFocusWidgetId;
        _commands.NotifyPropertyWidgetState(widgetId, isEditingWidget);
        return openRequested;
    }

    private void RenderPaintFillLayerPopoverContents(ImRect popupRect)
    {
        if (_colorPopoverPaintHandle.IsNull || _colorPopoverPaintLayerIndex < 0)
        {
            CloseColorPopover();
            return;
        }

        var paintView = PaintComponent.Api.FromHandle(_propertyWorld, _colorPopoverPaintHandle);
        if (!paintView.IsAlive)
        {
            CloseColorPopover();
            return;
        }

        int layerIndex = _colorPopoverPaintLayerIndex;
        int widgetId = _colorPopoverWidgetId;
        if (widgetId == 0)
        {
            widgetId = Im.Context.GetId("paint_fill_edit");
            _colorPopoverWidgetId = widgetId;
        }

        float padding = Im.Style.Padding;
        float spacing = Im.Style.Spacing;
        float rowHeight = Im.Style.MinButtonHeight;

        float contentX = popupRect.X + padding;
        float contentY = popupRect.Y + padding;
        float contentWidth = Math.Max(160f, popupRect.Width - padding * 2f);

        AnyComponentHandle paintAny = PaintComponentProperties.ToAnyHandle(_colorPopoverPaintHandle);
        var useGradientSlot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillUseGradient", layerIndex, PropertyKind.Bool);
        PropertySlot solidColorSlot = _colorPopoverPaintIsStrokeLayer
            ? PaintComponentPropertySlot.ArrayElement(paintAny, "StrokeColor", layerIndex, PropertyKind.Color32)
            : PaintComponentPropertySlot.ArrayElement(paintAny, "FillColor", layerIndex, PropertyKind.Color32);
        var gradientColorASlot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientColorA", layerIndex, PropertyKind.Color32);
        var gradientColorBSlot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientColorB", layerIndex, PropertyKind.Color32);
        var gradientMixSlot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientMix", layerIndex, PropertyKind.Float);
        var gradientDirectionSlot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientDirection", layerIndex, PropertyKind.Vec2);
        var stopCountSlot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientStopCount", layerIndex, PropertyKind.Int);
        var stopT0To3Slot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientStopT0To3", layerIndex, PropertyKind.Vec4);
        var stopT4To7Slot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientStopT4To7", layerIndex, PropertyKind.Vec4);

        Im.Context.PushId(_colorPopoverPaintHandle.GetHashCode());
        Im.Context.PushId(layerIndex);

        bool useGradient = PropertyDispatcher.ReadBool(_propertyWorld, useGradientSlot);

        var toggleRect = new ImRect(contentX, contentY, contentWidth, rowHeight);
        sbyte requestedMode = DrawSolidGradientToggle(toggleRect, useGradient);
        if (requestedMode == 1 && !useGradient)
        {
            Color32 baseColor = PropertyDispatcher.ReadColor32(_propertyWorld, solidColorSlot);
            _commands.SetPropertyValue(widgetId, isEditing: true, useGradientSlot, PropertyValue.FromBool(true));
            _commands.SetPropertyValue(widgetId, isEditing: true, gradientColorASlot, PropertyValue.FromColor32(baseColor));
            _commands.SetPropertyValue(widgetId, isEditing: true, gradientColorBSlot, PropertyValue.FromColor32(baseColor));
            _commands.SetPropertyValue(widgetId, isEditing: true, gradientMixSlot, PropertyValue.FromFloat(0.5f));
            _commands.SetPropertyValue(widgetId, isEditing: true, gradientDirectionSlot, PropertyValue.FromVec2(new Vector2(1f, 0f)));
            _commands.SetPropertyValue(widgetId, isEditing: true, stopCountSlot, PropertyValue.FromInt(2));
            _commands.SetPropertyValue(widgetId, isEditing: true, stopT0To3Slot, PropertyValue.FromVec4(new Vector4(0f, 1f, 0f, 0f)));
            _commands.SetPropertyValue(widgetId, isEditing: true, stopT4To7Slot, PropertyValue.FromVec4(Vector4.Zero));
            SetPaintFillGradientStopColor(widgetId, isEditing: true, _colorPopoverPaintHandle, layerIndex, 0, baseColor);
            SetPaintFillGradientStopColor(widgetId, isEditing: true, _colorPopoverPaintHandle, layerIndex, 1, baseColor);
            _colorPopoverPaintActiveStopIndex = 0;

            Im.Context.PopId();
            Im.Context.PopId();
            return;
        }

        if (requestedMode == 0 && useGradient)
        {
            int stopCount = GetPaintFillGradientStopCount(_colorPopoverPaintHandle, layerIndex);
            Color32 sample = stopCount > 0
                ? SamplePaintFillGradientAtT(_colorPopoverPaintHandle, layerIndex, stopCount, 0.5f)
                : UiColor32.LerpColor(PropertyDispatcher.ReadColor32(_propertyWorld, gradientColorASlot), PropertyDispatcher.ReadColor32(_propertyWorld, gradientColorBSlot), 0.5f);

            _commands.SetPropertyValue(widgetId, isEditing: true, useGradientSlot, PropertyValue.FromBool(false));
            _commands.SetPropertyValue(widgetId, isEditing: true, solidColorSlot, PropertyValue.FromColor32(sample));

            Im.Context.PopId();
            Im.Context.PopId();
            return;
        }

        contentY += rowHeight + spacing;

        if (!useGradient)
        {
            if (_bindingInspector != null && !_colorPopoverTargetEntity.IsNull)
            {
                var pickerRect = new ImRect(contentX, contentY, contentWidth, contentWidth + ColorPickerHeightOffset);
                _bindingInspector.DrawPropertyBindingContextMenu(_colorPopoverTargetEntity, solidColorSlot, pickerRect, allowUnbind: true);
            }

            uint argb = ToArgb(PropertyDispatcher.ReadColor32(_propertyWorld, solidColorSlot));
            if (ImColorPicker.DrawAt("picker", contentX, contentY, contentWidth, ref argb, showAlpha: true))
            {
                _commands.SetPropertyValue(widgetId, isEditing: true, solidColorSlot, PropertyValue.FromColor32(FromArgb(argb)));
            }
        }
        else
        {
            EnsurePaintFillGradientStopsInitialized(widgetId, _colorPopoverPaintHandle, layerIndex);

            int stopCount = GetPaintFillGradientStopCount(_colorPopoverPaintHandle, layerIndex);
            if (stopCount <= 0)
            {
                stopCount = 2;
            }

            if (_colorPopoverPaintActiveStopIndex >= stopCount)
            {
                _colorPopoverPaintActiveStopIndex = (byte)Math.Max(0, stopCount - 1);
            }

            Vector2 gradientDirection = PropertyDispatcher.ReadVec2(_propertyWorld, gradientDirectionSlot);
            var gradientTypeSlot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientType", layerIndex, PropertyKind.Int);
            var gradientAngleSlot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientAngle", layerIndex, PropertyKind.Float);
            int gradientKind = PropertyDispatcher.ReadInt(_propertyWorld, gradientTypeSlot);
            float gradientAngleOffset = PropertyDispatcher.ReadFloat(_propertyWorld, gradientAngleSlot);

            var gradientTypeRect = new ImRect(contentX, contentY, contentWidth, rowHeight);
            Vector2 gradientDirectionBefore = gradientDirection;
            int gradientKindBefore = gradientKind;
            float gradientAngleOffsetBefore = gradientAngleOffset;

            DrawPaintGradientTypeControls(
                widgetId,
                gradientTypeRect,
                _colorPopoverPaintHandle,
                layerIndex,
                stopCount,
                ref gradientKind,
                ref _colorPopoverPaintActiveStopIndex,
                ref gradientDirection,
                ref gradientAngleOffset);

            if (gradientKind != gradientKindBefore)
            {
                _commands.SetPropertyValue(widgetId, isEditing: true, gradientTypeSlot, PropertyValue.FromInt(gradientKind));
            }
            if (gradientDirection != gradientDirectionBefore)
            {
                _commands.SetPropertyValue(widgetId, isEditing: true, gradientDirectionSlot, PropertyValue.FromVec2(gradientDirection));
            }
            if (gradientAngleOffset != gradientAngleOffsetBefore)
            {
                _commands.SetPropertyValue(widgetId, isEditing: true, gradientAngleSlot, PropertyValue.FromFloat(gradientAngleOffset));
            }

            contentY += rowHeight + spacing;

            var barRect = new ImRect(contentX, contentY, contentWidth, 22f);
            bool stopCountChanged = DrawPaintGradientBarWithStops(widgetId, barRect, _colorPopoverPaintHandle, layerIndex, ref _colorPopoverPaintActiveStopIndex, stopCount);
            if (stopCountChanged)
            {
                stopCount = GetPaintFillGradientStopCount(_colorPopoverPaintHandle, layerIndex);
            }
            contentY += barRect.Height + spacing;

            var stopsHeaderRect = new ImRect(contentX, contentY, contentWidth, rowHeight);
            bool addStopPressed = DrawGradientStopsHeader(stopsHeaderRect, allowAdd: stopCount < FillGradientMaxStops);
            contentY += rowHeight + spacing;

            if (addStopPressed && stopCount < FillGradientMaxStops)
            {
                float addT = 0.5f;
                Color32 addColor = SamplePaintFillGradientAtT(_colorPopoverPaintHandle, layerIndex, stopCount, addT);

                int insertIndex = stopCount;
                for (int stopIndex = 0; stopIndex < stopCount; stopIndex++)
                {
                    if (GetPaintFillGradientStopT(_colorPopoverPaintHandle, layerIndex, stopIndex) > addT)
                    {
                        insertIndex = stopIndex;
                        break;
                    }
                }

                InsertPaintFillGradientStop(widgetId, _colorPopoverPaintHandle, layerIndex, insertIndex, addT, addColor);
                _colorPopoverPaintActiveStopIndex = (byte)insertIndex;
                stopCount = GetPaintFillGradientStopCount(_colorPopoverPaintHandle, layerIndex);
            }

            for (int stopIndex = 0; stopIndex < stopCount; stopIndex++)
            {
                var rowRect = new ImRect(contentX, contentY, contentWidth, rowHeight);
                bool removeRequested = DrawPaintGradientStopRow(widgetId, rowRect, _colorPopoverPaintHandle, layerIndex, stopIndex, stopCount, ref _colorPopoverPaintActiveStopIndex);
                if (removeRequested)
                {
                    RemovePaintFillGradientStop(widgetId, _colorPopoverPaintHandle, layerIndex, stopIndex);
                    stopCount = GetPaintFillGradientStopCount(_colorPopoverPaintHandle, layerIndex);
                    if (_colorPopoverPaintActiveStopIndex >= stopCount)
                    {
                        _colorPopoverPaintActiveStopIndex = (byte)Math.Max(0, stopCount - 1);
                    }
                    break;
                }
                contentY += rowHeight + spacing;
            }

            int activeStopIndex = Math.Clamp(_colorPopoverPaintActiveStopIndex, (byte)0, (byte)(stopCount - 1));
            Color32 activeColor = GetPaintFillGradientStopColor(_colorPopoverPaintHandle, layerIndex, activeStopIndex);
            uint editArgb = ToArgb(activeColor);
            if (_bindingInspector != null && !_colorPopoverTargetEntity.IsNull)
            {
                PropertySlot activeSlot = GetPaintFillGradientStopColorSlot(paintAny, layerIndex, activeStopIndex);
                var pickerRect = new ImRect(contentX, contentY, contentWidth, contentWidth + ColorPickerHeightOffset);
                _bindingInspector.DrawPropertyBindingContextMenu(_colorPopoverTargetEntity, activeSlot, pickerRect, allowUnbind: true);
            }
            if (ImColorPicker.DrawAt("picker", contentX, contentY, contentWidth, ref editArgb, showAlpha: true))
            {
                Color32 edited = FromArgb(editArgb);
                if (edited != activeColor)
                {
                    SetPaintFillGradientStopColor(widgetId, isEditing: true, _colorPopoverPaintHandle, layerIndex, activeStopIndex, edited);
                    SyncPaintFillGradientEndpoints(widgetId, isEditing: true, _colorPopoverPaintHandle, layerIndex, stopCount);
                }
            }
        }

        Im.Context.PopId();
        Im.Context.PopId();
    }

    private bool TryGetPaintFillUseGradient(PaintComponentHandle paintHandle, int layerIndex, out bool useGradient)
    {
        useGradient = false;
        if (paintHandle.IsNull || layerIndex < 0)
        {
            return false;
        }

        AnyComponentHandle paintAny = PaintComponentProperties.ToAnyHandle(paintHandle);
        var slot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillUseGradient", layerIndex, PropertyKind.Bool);
        useGradient = PropertyDispatcher.ReadBool(_propertyWorld, slot);
        return true;
    }

    private int GetPaintFillGradientStopCount(PaintComponentHandle paintHandle, int layerIndex)
    {
        if (paintHandle.IsNull || layerIndex < 0)
        {
            return 0;
        }

        AnyComponentHandle paintAny = PaintComponentProperties.ToAnyHandle(paintHandle);
        var slot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientStopCount", layerIndex, PropertyKind.Int);
        int count = PropertyDispatcher.ReadInt(_propertyWorld, slot);
        if (count < 2)
        {
            return 0;
        }
        if (count > FillGradientMaxStops)
        {
            return FillGradientMaxStops;
        }
        return count;
    }

    private float GetPaintFillGradientStopT(PaintComponentHandle paintHandle, int layerIndex, int stopIndex)
    {
        if ((uint)stopIndex >= FillGradientMaxStops || paintHandle.IsNull || layerIndex < 0)
        {
            return 0f;
        }

        AnyComponentHandle paintAny = PaintComponentProperties.ToAnyHandle(paintHandle);
        var slot0123 = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientStopT0To3", layerIndex, PropertyKind.Vec4);
        Vector4 t0123 = PropertyDispatcher.ReadVec4(_propertyWorld, slot0123);
        if (stopIndex < 4)
        {
            return stopIndex switch
            {
                0 => t0123.X,
                1 => t0123.Y,
                2 => t0123.Z,
                _ => t0123.W
            };
        }

        var slot4567 = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientStopT4To7", layerIndex, PropertyKind.Vec4);
        Vector4 t4567 = PropertyDispatcher.ReadVec4(_propertyWorld, slot4567);
        return (stopIndex - 4) switch
        {
            0 => t4567.X,
            1 => t4567.Y,
            2 => t4567.Z,
            _ => t4567.W
        };
    }

    private void SetPaintFillGradientStopT(int widgetId, bool isEditing, PaintComponentHandle paintHandle, int layerIndex, int stopIndex, float value)
    {
        if ((uint)stopIndex >= FillGradientMaxStops || paintHandle.IsNull || layerIndex < 0)
        {
            return;
        }

        value = Math.Clamp(value, 0f, 1f);
        AnyComponentHandle paintAny = PaintComponentProperties.ToAnyHandle(paintHandle);
        if (stopIndex < 4)
        {
            var slot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientStopT0To3", layerIndex, PropertyKind.Vec4);
            Vector4 t0123 = PropertyDispatcher.ReadVec4(_propertyWorld, slot);
            switch (stopIndex)
            {
                case 0: t0123.X = value; break;
                case 1: t0123.Y = value; break;
                case 2: t0123.Z = value; break;
                default: t0123.W = value; break;
            }
            _commands.SetPropertyValue(widgetId, isEditing, slot, PropertyValue.FromVec4(t0123));
            return;
        }

        var slot4567 = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientStopT4To7", layerIndex, PropertyKind.Vec4);
        Vector4 t4567 = PropertyDispatcher.ReadVec4(_propertyWorld, slot4567);
        switch (stopIndex - 4)
        {
            case 0: t4567.X = value; break;
            case 1: t4567.Y = value; break;
            case 2: t4567.Z = value; break;
            default: t4567.W = value; break;
        }
        _commands.SetPropertyValue(widgetId, isEditing, slot4567, PropertyValue.FromVec4(t4567));
    }

    private Color32 GetPaintFillGradientStopColor(PaintComponentHandle paintHandle, int layerIndex, int stopIndex)
    {
        if (paintHandle.IsNull || layerIndex < 0)
        {
            return default;
        }

        AnyComponentHandle paintAny = PaintComponentProperties.ToAnyHandle(paintHandle);
        string name = stopIndex switch
        {
            0 => "FillGradientStopColor0",
            1 => "FillGradientStopColor1",
            2 => "FillGradientStopColor2",
            3 => "FillGradientStopColor3",
            4 => "FillGradientStopColor4",
            5 => "FillGradientStopColor5",
            6 => "FillGradientStopColor6",
            7 => "FillGradientStopColor7",
            _ => "FillGradientStopColor0"
        };
        var slot = PaintComponentPropertySlot.ArrayElement(paintAny, name, layerIndex, PropertyKind.Color32);
        return PropertyDispatcher.ReadColor32(_propertyWorld, slot);
    }

    private void SetPaintFillGradientStopColor(int widgetId, bool isEditing, PaintComponentHandle paintHandle, int layerIndex, int stopIndex, Color32 color)
    {
        if (paintHandle.IsNull || layerIndex < 0)
        {
            return;
        }

        AnyComponentHandle paintAny = PaintComponentProperties.ToAnyHandle(paintHandle);
        string name = stopIndex switch
        {
            0 => "FillGradientStopColor0",
            1 => "FillGradientStopColor1",
            2 => "FillGradientStopColor2",
            3 => "FillGradientStopColor3",
            4 => "FillGradientStopColor4",
            5 => "FillGradientStopColor5",
            6 => "FillGradientStopColor6",
            7 => "FillGradientStopColor7",
            _ => "FillGradientStopColor0"
        };
        var slot = PaintComponentPropertySlot.ArrayElement(paintAny, name, layerIndex, PropertyKind.Color32);
        _commands.SetPropertyValue(widgetId, isEditing, slot, PropertyValue.FromColor32(color));
    }

    private void EnsurePaintFillGradientStopsInitialized(int widgetId, PaintComponentHandle paintHandle, int layerIndex)
    {
        int stopCount = GetPaintFillGradientStopCount(paintHandle, layerIndex);
        if (stopCount >= 2)
        {
            return;
        }

        AnyComponentHandle paintAny = PaintComponentProperties.ToAnyHandle(paintHandle);
        var stopCountSlot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientStopCount", layerIndex, PropertyKind.Int);
        var stopT0To3Slot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientStopT0To3", layerIndex, PropertyKind.Vec4);
        var stopT4To7Slot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientStopT4To7", layerIndex, PropertyKind.Vec4);
        var gradientColorASlot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientColorA", layerIndex, PropertyKind.Color32);
        var gradientColorBSlot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientColorB", layerIndex, PropertyKind.Color32);

        Color32 a = PropertyDispatcher.ReadColor32(_propertyWorld, gradientColorASlot);
        Color32 b = PropertyDispatcher.ReadColor32(_propertyWorld, gradientColorBSlot);
        _commands.SetPropertyValue(widgetId, isEditing: true, stopCountSlot, PropertyValue.FromInt(2));
        _commands.SetPropertyValue(widgetId, isEditing: true, stopT0To3Slot, PropertyValue.FromVec4(new Vector4(0f, 1f, 0f, 0f)));
        _commands.SetPropertyValue(widgetId, isEditing: true, stopT4To7Slot, PropertyValue.FromVec4(Vector4.Zero));
        SetPaintFillGradientStopColor(widgetId, isEditing: true, paintHandle, layerIndex, 0, a);
        SetPaintFillGradientStopColor(widgetId, isEditing: true, paintHandle, layerIndex, 1, b);
    }

    private void SyncPaintFillGradientEndpoints(int widgetId, bool isEditing, PaintComponentHandle paintHandle, int layerIndex, int stopCount)
    {
        if (stopCount <= 0)
        {
            return;
        }

        AnyComponentHandle paintAny = PaintComponentProperties.ToAnyHandle(paintHandle);
        var gradientColorASlot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientColorA", layerIndex, PropertyKind.Color32);
        var gradientColorBSlot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientColorB", layerIndex, PropertyKind.Color32);

        Color32 a = GetPaintFillGradientStopColor(paintHandle, layerIndex, 0);
        Color32 b = GetPaintFillGradientStopColor(paintHandle, layerIndex, stopCount - 1);
        _commands.SetPropertyValue(widgetId, isEditing, gradientColorASlot, PropertyValue.FromColor32(a));
        _commands.SetPropertyValue(widgetId, isEditing, gradientColorBSlot, PropertyValue.FromColor32(b));
    }

    private Color32 SamplePaintFillGradientAtT(PaintComponentHandle paintHandle, int layerIndex, int stopCount, float sampleT)
    {
        if (stopCount <= 0)
        {
            AnyComponentHandle paintAny = PaintComponentProperties.ToAnyHandle(paintHandle);
            var fillColorSlot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillColor", layerIndex, PropertyKind.Color32);
            return PropertyDispatcher.ReadColor32(_propertyWorld, fillColorSlot);
        }

        sampleT = Math.Clamp(sampleT, 0f, 1f);
        float firstT = GetPaintFillGradientStopT(paintHandle, layerIndex, 0);
        float lastT = GetPaintFillGradientStopT(paintHandle, layerIndex, stopCount - 1);
        if (sampleT <= firstT)
        {
            return GetPaintFillGradientStopColor(paintHandle, layerIndex, 0);
        }
        if (sampleT >= lastT)
        {
            return GetPaintFillGradientStopColor(paintHandle, layerIndex, stopCount - 1);
        }

        for (int stopIndex = 1; stopIndex < stopCount; stopIndex++)
        {
            float stopT = GetPaintFillGradientStopT(paintHandle, layerIndex, stopIndex);
            if (sampleT <= stopT)
            {
                float prevT = GetPaintFillGradientStopT(paintHandle, layerIndex, stopIndex - 1);
                Color32 prevColor = GetPaintFillGradientStopColor(paintHandle, layerIndex, stopIndex - 1);
                Color32 nextColor = GetPaintFillGradientStopColor(paintHandle, layerIndex, stopIndex);
                float denom = stopT - prevT;
                if (denom <= 0.00001f)
                {
                    return nextColor;
                }
                float lerpT = Math.Clamp((sampleT - prevT) / denom, 0f, 1f);
                return UiColor32.LerpColor(prevColor, nextColor, lerpT);
            }
        }

        return GetPaintFillGradientStopColor(paintHandle, layerIndex, stopCount - 1);
    }

    private void DrawPaintGradientTypeControls(
        int widgetId,
        ImRect rect,
        PaintComponentHandle paintHandle,
        int layerIndex,
        int stopCount,
        ref int gradientKind,
        ref byte activeStopIndex,
        ref Vector2 gradientDirection,
        ref float gradientAngleOffset)
    {
        float spacing = Im.Style.Spacing;
        float buttonSize = rect.Height;
        float buttonY = rect.Y;
        var rotateRect = new ImRect(rect.Right - buttonSize, buttonY, buttonSize, rect.Height);
        var swapRect = new ImRect(rotateRect.X - spacing - buttonSize, buttonY, buttonSize, rect.Height);
        float dropdownRight = swapRect.X - spacing;
        float dropdownWidth = Math.Max(1f, dropdownRight - rect.X);
        var dropdownRect = new ImRect(rect.X, rect.Y, dropdownWidth, rect.Height);

        int selectedIndex = gradientKind;
        if ((uint)selectedIndex >= (uint)GradientTypeOptions.Length)
        {
            selectedIndex = 0;
        }

        bool dropdownChanged = Im.Dropdown("gradient_type", GradientTypeOptions, ref selectedIndex, dropdownRect.X, dropdownRect.Y, dropdownRect.Width);
        if (dropdownChanged)
        {
            gradientKind = selectedIndex;
        }

        bool swapHovered = swapRect.Contains(Im.MousePos);
        DrawIconButton(swapRect, enabled: stopCount >= 2, hovered: swapHovered);
        DrawSwapIcon(swapRect, Im.Style.TextSecondary);

        bool canRotate = selectedIndex != 1;
        bool rotateHovered = rotateRect.Contains(Im.MousePos);
        DrawIconButton(rotateRect, enabled: canRotate, hovered: rotateHovered);
        DrawRotateIcon(rotateRect, canRotate ? Im.Style.TextSecondary : ImStyle.WithAlphaF(Im.Style.TextSecondary, 0.35f));

        if (swapRect.Contains(Im.MousePos) || rotateRect.Contains(Im.MousePos) || dropdownRect.Contains(Im.MousePos))
        {
            Im.SetCursor(Silk.NET.Input.StandardCursor.Hand);
        }

        if (swapHovered && Im.MousePressed && stopCount >= 2)
        {
            Span<float> stopT = stackalloc float[FillGradientMaxStops];
            Span<Color32> stopColor = stackalloc Color32[FillGradientMaxStops];
            stopCount = Math.Clamp(stopCount, 2, FillGradientMaxStops);
            for (int stopIndex = 0; stopIndex < stopCount; stopIndex++)
            {
                stopT[stopIndex] = GetPaintFillGradientStopT(paintHandle, layerIndex, stopIndex);
                stopColor[stopIndex] = GetPaintFillGradientStopColor(paintHandle, layerIndex, stopIndex);
            }

            for (int stopIndex = 0; stopIndex < stopCount; stopIndex++)
            {
                int sourceIndex = stopCount - 1 - stopIndex;
                SetPaintFillGradientStopT(widgetId, isEditing: true, paintHandle, layerIndex, stopIndex, 1f - stopT[sourceIndex]);
                SetPaintFillGradientStopColor(widgetId, isEditing: true, paintHandle, layerIndex, stopIndex, stopColor[sourceIndex]);
            }

            activeStopIndex = (byte)Math.Clamp(stopCount - 1 - activeStopIndex, 0, stopCount - 1);
            SyncPaintFillGradientEndpoints(widgetId, isEditing: true, paintHandle, layerIndex, stopCount);
            gradientDirection = new Vector2(-gradientDirection.X, -gradientDirection.Y);
        }

        if (rotateHovered && canRotate && Im.MousePressed)
        {
            if (selectedIndex == 2)
            {
                gradientAngleOffset += MathF.PI * 0.5f;
                if (gradientAngleOffset > MathF.PI * 2f)
                {
                    gradientAngleOffset -= MathF.PI * 2f;
                }
            }
            else
            {
                float lengthSq = gradientDirection.LengthSquared();
                if (lengthSq < 0.0001f)
                {
                    gradientDirection = new Vector2(1f, 0f);
                }
                gradientDirection = new Vector2(-gradientDirection.Y, gradientDirection.X);
            }
        }
    }

    private bool DrawPaintGradientBarWithStops(
        int widgetId,
        ImRect rect,
        PaintComponentHandle paintHandle,
        int layerIndex,
        ref byte activeStopIndex,
        int stopCount)
    {
        bool stopCountChanged = false;
        stopCount = Math.Clamp(stopCount, 2, FillGradientMaxStops);
        bool allowAdd = stopCount < FillGradientMaxStops;

        DrawCheckerboard(rect, 6f);
        Span<SdfGradientStop> stops = stackalloc SdfGradientStop[FillGradientMaxStops];
        for (int stopIndex = 0; stopIndex < stopCount; stopIndex++)
        {
            float t = GetPaintFillGradientStopT(paintHandle, layerIndex, stopIndex);
            Color32 stopColor = GetPaintFillGradientStopColor(paintHandle, layerIndex, stopIndex);
            stops[stopIndex] = new SdfGradientStop
            {
                Color = ImStyle.ToVector4(ToArgb(stopColor)),
                Params = new Vector4(t, 0f, 0f, 0f)
            };
        }
        Im.DrawRoundedRectGradientStops(rect.X, rect.Y, rect.Width, rect.Height, 6f, stops.Slice(0, stopCount));

        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, 6f, Im.Style.Border, Im.Style.BorderWidth);

        float handleSize = Math.Min(14f, rect.Height - 2f);
        float handleY = rect.Y + (rect.Height - handleSize) * 0.5f;
        float handleHalf = handleSize * 0.5f;

        int hoveredStopIndex = -1;
        for (int stopIndex = 0; stopIndex < stopCount; stopIndex++)
        {
            float stopT = GetPaintFillGradientStopT(paintHandle, layerIndex, stopIndex);
            float handleX = rect.X + stopT * rect.Width;
            var handleRect = new ImRect(handleX - handleHalf, handleY, handleSize, handleSize);

            uint colorArgb = ToArgb(GetPaintFillGradientStopColor(paintHandle, layerIndex, stopIndex));
            Im.DrawRoundedRect(handleRect.X, handleRect.Y, handleRect.Width, handleRect.Height, 3f, colorArgb);
            Im.DrawRoundedRectStroke(handleRect.X, handleRect.Y, handleRect.Width, handleRect.Height, 3f,
                activeStopIndex == stopIndex ? Im.Style.Primary : Im.Style.Border, 2f);

            if (handleRect.Contains(Im.MousePos))
            {
                hoveredStopIndex = stopIndex;
            }
        }

        bool hovered = rect.Contains(Im.MousePos) || hoveredStopIndex >= 0;
        if (hovered)
        {
            Im.SetCursor(Silk.NET.Input.StandardCursor.Hand);
        }

        if (Im.MousePressed)
        {
            if (hoveredStopIndex >= 0)
            {
                activeStopIndex = (byte)hoveredStopIndex;
                _paintGradientStopDragHandle = paintHandle;
                _paintGradientStopDragLayerIndex = layerIndex;
                _paintGradientStopDragIndex = hoveredStopIndex;
            }
            else if (allowAdd && rect.Contains(Im.MousePos))
            {
                float addT = rect.Width <= 0f ? 0f : (Im.MousePos.X - rect.X) / rect.Width;
                addT = Math.Clamp(addT, 0f, 1f);
                Color32 addColor = SamplePaintFillGradientAtT(paintHandle, layerIndex, stopCount, addT);

                int insertIndex = stopCount;
                for (int stopIndex = 0; stopIndex < stopCount; stopIndex++)
                {
                    if (GetPaintFillGradientStopT(paintHandle, layerIndex, stopIndex) > addT)
                    {
                        insertIndex = stopIndex;
                        break;
                    }
                }
                InsertPaintFillGradientStop(widgetId, paintHandle, layerIndex, insertIndex, addT, addColor);
                stopCountChanged = true;
                activeStopIndex = (byte)insertIndex;
            }
        }

        bool isDragging = _paintGradientStopDragIndex >= 0 &&
            !_paintGradientStopDragHandle.IsNull &&
            _paintGradientStopDragHandle.Equals(paintHandle) &&
            _paintGradientStopDragLayerIndex == layerIndex;
        if (isDragging)
        {
            if (Im.MouseDown)
            {
                int dragIndex = _paintGradientStopDragIndex;
                if (dragIndex < 0 || dragIndex >= stopCount)
                {
                    _paintGradientStopDragIndex = -1;
                    _paintGradientStopDragHandle = default;
                    _paintGradientStopDragLayerIndex = -1;
                }
                else
                {
                    float dragT = rect.Width <= 0f ? 0f : (Im.MousePos.X - rect.X) / rect.Width;
                    float minT = dragIndex <= 0 ? 0f : GetPaintFillGradientStopT(paintHandle, layerIndex, dragIndex - 1) + 0.001f;
                    float maxT = dragIndex >= stopCount - 1 ? 1f : GetPaintFillGradientStopT(paintHandle, layerIndex, dragIndex + 1) - 0.001f;
                    dragT = Math.Clamp(dragT, minT, maxT);
                    SetPaintFillGradientStopT(widgetId, isEditing: true, paintHandle, layerIndex, dragIndex, dragT);
                }
            }
            else
            {
                _paintGradientStopDragIndex = -1;
                _paintGradientStopDragHandle = default;
                _paintGradientStopDragLayerIndex = -1;
            }
        }

        return stopCountChanged;
    }

    private bool DrawPaintGradientStopRow(
        int widgetId,
        ImRect rect,
        PaintComponentHandle paintHandle,
        int layerIndex,
        int stopIndex,
        int stopCount,
        ref byte activeStopIndex)
    {
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
        uint labelColor = stopIndex == activeStopIndex ? Im.Style.TextPrimary : Im.Style.TextSecondary;
        Im.Text($"Stop {stopIndex}", rect.X, textY, Im.Style.FontSize, labelColor);

        if (_bindingInspector != null && !_colorPopoverTargetEntity.IsNull)
        {
            AnyComponentHandle paintAny = PaintComponentProperties.ToAnyHandle(paintHandle);
            PropertySlot stopColorSlot = GetPaintFillGradientStopColorSlot(paintAny, layerIndex, stopIndex);
            _bindingInspector.DrawPropertyBindingContextMenu(_colorPopoverTargetEntity, stopColorSlot, rect, allowUnbind: true);
        }

        float buttonSize = rect.Height;
        var removeRect = new ImRect(rect.Right - buttonSize, rect.Y, buttonSize, rect.Height);
        bool removeHovered = removeRect.Contains(Im.MousePos);
        DrawIconButton(removeRect, enabled: stopCount > 2, hovered: removeHovered);
        DrawMinusIcon(removeRect, stopCount > 2 ? Im.Style.TextPrimary : Im.Style.TextSecondary);

        float inputWidth = 70f;
        float inputX = removeRect.X - 6f - inputWidth;
        float inputY = rect.Y + (rect.Height - Im.Style.MinButtonHeight) * 0.5f;
        float percent = MathF.Round(GetPaintFillGradientStopT(paintHandle, layerIndex, stopIndex) * 100f);
        Im.Context.PushId(stopIndex);
        bool changed = ImScalarInput.DrawAt("stop_percent", inputX, inputY, inputWidth, ref percent, 0f, 100f, "F0");
        Im.Context.PopId();
        if (changed)
        {
            float t = Math.Clamp(percent / 100f, 0f, 1f);
            float minT = stopIndex <= 0 ? 0f : GetPaintFillGradientStopT(paintHandle, layerIndex, stopIndex - 1) + 0.001f;
            float maxT = stopIndex >= stopCount - 1 ? 1f : GetPaintFillGradientStopT(paintHandle, layerIndex, stopIndex + 1) - 0.001f;
            t = Math.Clamp(t, minT, maxT);
            SetPaintFillGradientStopT(widgetId, isEditing: true, paintHandle, layerIndex, stopIndex, t);
        }

        var pickRect = new ImRect(rect.X, rect.Y, rect.Width, rect.Height);
        if (pickRect.Contains(Im.MousePos) && Im.MousePressed)
        {
            activeStopIndex = (byte)stopIndex;
        }

        if (removeHovered)
        {
            Im.SetCursor(Silk.NET.Input.StandardCursor.Hand);
            if (stopCount > 2 && Im.MousePressed)
            {
                return true;
            }
        }

        return false;
    }

    private static PropertySlot GetPaintFillGradientStopColorSlot(AnyComponentHandle paintAny, int layerIndex, int stopIndex)
    {
        string name = stopIndex switch
        {
            0 => "FillGradientStopColor0",
            1 => "FillGradientStopColor1",
            2 => "FillGradientStopColor2",
            3 => "FillGradientStopColor3",
            4 => "FillGradientStopColor4",
            5 => "FillGradientStopColor5",
            6 => "FillGradientStopColor6",
            7 => "FillGradientStopColor7",
            _ => "FillGradientStopColor0"
        };
        return PaintComponentPropertySlot.ArrayElement(paintAny, name, layerIndex, PropertyKind.Color32);
    }

    private void InsertPaintFillGradientStop(int widgetId, PaintComponentHandle paintHandle, int layerIndex, int insertIndex, float stopT, Color32 color)
    {
        int stopCount = GetPaintFillGradientStopCount(paintHandle, layerIndex);
        stopCount = Math.Clamp(stopCount, 2, FillGradientMaxStops);
        if (stopCount >= FillGradientMaxStops)
        {
            return;
        }

        insertIndex = Math.Clamp(insertIndex, 0, stopCount);
        Span<float> t = stackalloc float[FillGradientMaxStops];
        Span<Color32> c = stackalloc Color32[FillGradientMaxStops];
        for (int i = 0; i < stopCount; i++)
        {
            t[i] = GetPaintFillGradientStopT(paintHandle, layerIndex, i);
            c[i] = GetPaintFillGradientStopColor(paintHandle, layerIndex, i);
        }

        for (int i = stopCount; i > insertIndex; i--)
        {
            t[i] = t[i - 1];
            c[i] = c[i - 1];
        }
        t[insertIndex] = Math.Clamp(stopT, 0f, 1f);
        c[insertIndex] = color;

        AnyComponentHandle paintAny = PaintComponentProperties.ToAnyHandle(paintHandle);
        var stopCountSlot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientStopCount", layerIndex, PropertyKind.Int);
        _commands.SetPropertyValue(widgetId, isEditing: true, stopCountSlot, PropertyValue.FromInt(stopCount + 1));

        for (int i = 0; i < stopCount + 1; i++)
        {
            SetPaintFillGradientStopT(widgetId, isEditing: true, paintHandle, layerIndex, i, t[i]);
            SetPaintFillGradientStopColor(widgetId, isEditing: true, paintHandle, layerIndex, i, c[i]);
        }

        SyncPaintFillGradientEndpoints(widgetId, isEditing: true, paintHandle, layerIndex, stopCount + 1);
    }

    private void RemovePaintFillGradientStop(int widgetId, PaintComponentHandle paintHandle, int layerIndex, int removeIndex)
    {
        int stopCount = GetPaintFillGradientStopCount(paintHandle, layerIndex);
        if (stopCount <= 2)
        {
            return;
        }

        removeIndex = Math.Clamp(removeIndex, 0, stopCount - 1);
        Span<float> t = stackalloc float[FillGradientMaxStops];
        Span<Color32> c = stackalloc Color32[FillGradientMaxStops];
        for (int i = 0; i < stopCount; i++)
        {
            t[i] = GetPaintFillGradientStopT(paintHandle, layerIndex, i);
            c[i] = GetPaintFillGradientStopColor(paintHandle, layerIndex, i);
        }

        for (int i = removeIndex; i < stopCount - 1; i++)
        {
            t[i] = t[i + 1];
            c[i] = c[i + 1];
        }

        AnyComponentHandle paintAny = PaintComponentProperties.ToAnyHandle(paintHandle);
        var stopCountSlot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientStopCount", layerIndex, PropertyKind.Int);
        _commands.SetPropertyValue(widgetId, isEditing: true, stopCountSlot, PropertyValue.FromInt(stopCount - 1));

        for (int i = 0; i < stopCount - 1; i++)
        {
            SetPaintFillGradientStopT(widgetId, isEditing: true, paintHandle, layerIndex, i, t[i]);
            SetPaintFillGradientStopColor(widgetId, isEditing: true, paintHandle, layerIndex, i, c[i]);
        }

        SyncPaintFillGradientEndpoints(widgetId, isEditing: true, paintHandle, layerIndex, stopCount - 1);
    }

    private void RenderFillPopoverContents(ImRect popupRect)
    {
        if (_colorPopoverFillHandle.IsNull)
        {
            CloseColorPopover();
            return;
        }

        var fillView = FillComponent.Api.FromHandle(_propertyWorld, _colorPopoverFillHandle);
        if (!fillView.IsAlive)
        {
            CloseColorPopover();
            return;
        }

        int widgetId = _colorPopoverWidgetId;
        if (widgetId == 0)
        {
            widgetId = GetFillEditWidgetId(_colorPopoverFillHandle);
            _colorPopoverWidgetId = widgetId;
        }

        FillComponent before = SnapshotFill(fillView);

        float padding = Im.Style.Padding;
        float spacing = Im.Style.Spacing;
        float rowHeight = Im.Style.MinButtonHeight;

        float contentX = popupRect.X + padding;
        float contentY = popupRect.Y + padding;
        float contentWidth = Math.Max(160f, popupRect.Width - padding * 2f);

        Im.Context.PushId(_colorPopoverFillHandle.GetHashCode());

        var toggleRect = new ImRect(contentX, contentY, contentWidth, rowHeight);
        bool useGradient = fillView.UseGradient;
        sbyte requestedMode = DrawSolidGradientToggle(toggleRect, useGradient);
        if (requestedMode == 1 && !useGradient)
        {
            fillView.UseGradient = true;
            fillView.GradientColorA = fillView.Color;
            fillView.GradientColorB = fillView.Color;
            fillView.GradientMix = 0.5f;
            fillView.GradientDirection = new Vector2(1f, 0f);
            fillView.GradientStopCount = 2;
            fillView.GradientStopT0To3 = new Vector4(0f, 1f, 0f, 0f);
            fillView.GradientStopT4To7 = Vector4.Zero;
            fillView.GradientStopColor0 = fillView.Color;
            fillView.GradientStopColor1 = fillView.Color;
            _colorPopoverFillActiveStopIndex = 0;

            for (int stopIndex = 0; stopIndex < FillGradientMaxStops; stopIndex++)
            {
                ResetFillPopoverStopCache(stopIndex);
            }

            FillComponent after = SnapshotFill(fillView);
            _commands.RecordFillComponentEdit(widgetId, isEditing: true, _colorPopoverFillHandle, before, after);

            Im.Context.PopId();
            return;
        }

        if (requestedMode == 0 && useGradient)
        {
            fillView.UseGradient = false;
            int stopCount = GetFillGradientStopCount(fillView);
            if (stopCount <= 0)
            {
                fillView.Color = LerpColor(fillView.GradientColorA, fillView.GradientColorB, 0.5f);
            }
            else
            {
                fillView.Color = SampleFillGradientAtT(fillView, stopCount, 0.5f);
            }

            FillComponent after = SnapshotFill(fillView);
            _commands.RecordFillComponentEdit(widgetId, isEditing: true, _colorPopoverFillHandle, before, after);

            Im.Context.PopId();
            return;
        }

        contentY += rowHeight + spacing;

        uint editArgb;
        if (!useGradient)
        {
            editArgb = ToArgb(fillView.Color);
            if (ImColorPicker.DrawAt("picker", contentX, contentY, contentWidth, ref editArgb, showAlpha: true))
            {
                fillView.Color = FromArgb(editArgb);
            }
        }
        else
        {
            EnsureFillPopoverStopCaches(_colorPopoverFillHandle);
            EnsureFillGradientStopsInitialized(ref fillView);

            int stopCount = GetFillGradientStopCount(fillView);
            if (stopCount <= 0)
            {
                stopCount = 2;
            }

            if (_colorPopoverFillActiveStopIndex >= stopCount)
            {
                _colorPopoverFillActiveStopIndex = (byte)Math.Max(0, stopCount - 1);
            }

            Vector2 gradientDirection = fillView.GradientDirection;

            var gradientTypeRect = new ImRect(contentX, contentY, contentWidth, rowHeight);
            DrawGradientTypeControls(gradientTypeRect, ref fillView, ref _colorPopoverFillActiveStopIndex, ref gradientDirection, stopCount);
            contentY += rowHeight + spacing;

            var barRect = new ImRect(contentX, contentY, contentWidth, 22f);
            bool stopCountChangedByBar = DrawGradientBarWithStops(barRect, ref fillView, ref _colorPopoverFillActiveStopIndex, stopCount);
            if (stopCountChangedByBar)
            {
                stopCount = GetFillGradientStopCount(fillView);
            }
            contentY += barRect.Height + spacing;

            var stopsHeaderRect = new ImRect(contentX, contentY, contentWidth, rowHeight);
            bool addStopPressed = DrawGradientStopsHeader(stopsHeaderRect, allowAdd: stopCount < FillGradientMaxStops);
            contentY += rowHeight + spacing;

            if (addStopPressed)
            {
                float addT = 0.5f;
                Color32 addColor = SampleFillGradientAtT(fillView, stopCount, addT);
                int insertIndex = stopCount;
                for (int existingStopIndex = 0; existingStopIndex < stopCount; existingStopIndex++)
                {
                    if (GetFillGradientStopT(fillView, existingStopIndex) > addT)
                    {
                        insertIndex = existingStopIndex;
                        break;
                    }
                }
                ShiftFillPopoverStopCachesForInsert(insertIndex, stopCount);
                InsertFillGradientStop(ref fillView, insertIndex, addT, addColor);
                stopCount = GetFillGradientStopCount(fillView);
                _colorPopoverFillActiveStopIndex = (byte)Math.Min(insertIndex, stopCount - 1);
            }

            int stopIndex = 0;
            while (stopIndex < stopCount)
            {
                float stopT = GetFillGradientStopT(fillView, stopIndex);
                int percent = (int)MathF.Round(stopT * 100f);
                percent = Math.Clamp(percent, 0, 100);

                var stopRect = new ImRect(contentX, contentY, contentWidth, rowHeight);
                Color32 stopColor = GetFillGradientStopColor(fillView, stopIndex);
                uint stopArgb = ToArgb(stopColor);
                bool allowRemove = stopCount > 2;

                bool removeRequested = DrawGradientStopEditorRow(stopRect, stopIndex, percent, ref _colorPopoverFillActiveStopIndex,
                    ref stopArgb,
                    _fillPopoverStopHexBuffers![stopIndex], ref _fillPopoverStopHexLengths![stopIndex], ref _fillPopoverStopHexLastArgb![stopIndex],
                    _fillPopoverStopOpacityBuffers![stopIndex], ref _fillPopoverStopOpacityLengths![stopIndex], ref _fillPopoverStopOpacityLastAlpha![stopIndex],
                    allowRemove);

                if (stopArgb != ToArgb(stopColor))
                {
                    SetFillGradientStopColor(ref fillView, stopIndex, FromArgb(stopArgb));
                    SyncFillGradientEndpoints(ref fillView);
                }

                if (removeRequested)
                {
                    ShiftFillPopoverStopCachesForRemove(stopIndex, stopCount);
                    RemoveFillGradientStop(ref fillView, stopIndex);
                    stopCount = GetFillGradientStopCount(fillView);
                    if (_colorPopoverFillActiveStopIndex >= stopCount)
                    {
                        _colorPopoverFillActiveStopIndex = (byte)Math.Max(0, stopCount - 1);
                    }
                    continue;
                }

                contentY += rowHeight + spacing;
                stopIndex++;
            }

            int activeStopIndex = Math.Clamp(_colorPopoverFillActiveStopIndex, (byte)0, (byte)(stopCount - 1));
            Color32 activeColor = GetFillGradientStopColor(fillView, activeStopIndex);
            editArgb = ToArgb(activeColor);
            if (ImColorPicker.DrawAt("picker", contentX, contentY, contentWidth, ref editArgb, showAlpha: true))
            {
                SetFillGradientStopColor(ref fillView, activeStopIndex, FromArgb(editArgb));
                SyncFillGradientEndpoints(ref fillView);
            }

            if (fillView.GradientDirection != gradientDirection)
            {
                fillView.GradientDirection = gradientDirection;
            }
        }

        FillComponent afterFinal = SnapshotFill(fillView);
        _commands.RecordFillComponentEdit(widgetId, isEditing: true, _colorPopoverFillHandle, before, afterFinal);

        Im.Context.PopId();
    }

    private static sbyte DrawSolidGradientToggle(ImRect rect, bool useGradient)
    {
        float halfWidth = rect.Width * 0.5f;
        var solidRect = new ImRect(rect.X, rect.Y, halfWidth, rect.Height);
        var gradientRect = new ImRect(rect.X + halfWidth, rect.Y, halfWidth, rect.Height);

        bool hoveredSolid = solidRect.Contains(Im.MousePos);
        bool hoveredGradient = gradientRect.Contains(Im.MousePos);

        uint solidBg = !useGradient ? Im.Style.Primary : (hoveredSolid ? Im.Style.Hover : Im.Style.Surface);
        uint gradientBg = useGradient ? Im.Style.Primary : (hoveredGradient ? Im.Style.Hover : Im.Style.Surface);
        uint textColor = Im.Style.TextPrimary;

        Im.DrawRoundedRectPerCorner(solidRect.X, solidRect.Y, solidRect.Width, solidRect.Height,
            Im.Style.CornerRadius, 0f, 0f, Im.Style.CornerRadius, solidBg);
        Im.DrawRoundedRectPerCorner(gradientRect.X, gradientRect.Y, gradientRect.Width, gradientRect.Height,
            0f, Im.Style.CornerRadius, Im.Style.CornerRadius, 0f, gradientBg);
        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, Im.Style.Border, 1f);

        DrawCenteredText("Solid", solidRect, textColor);
        DrawCenteredText("Gradient", gradientRect, textColor);

        if (hoveredSolid && Im.MousePressed)
        {
            return 0;
        }
        if (hoveredGradient && Im.MousePressed)
        {
            return 1;
        }

        return -1;
    }

    private void DrawGradientTypeControls(
        ImRect rect,
        ref FillComponent.ViewProxy fillView,
        ref byte activeStopIndex,
        ref Vector2 gradientDirection,
        int stopCount)
    {
        float spacing = Im.Style.Spacing;
        float buttonSize = rect.Height;

        var rotateRect = new ImRect(rect.Right - buttonSize, rect.Y, buttonSize, rect.Height);
        var swapRect = new ImRect(rotateRect.X - spacing - buttonSize, rect.Y, buttonSize, rect.Height);
        float dropdownRight = swapRect.X - spacing;
        float dropdownWidth = Math.Max(1f, dropdownRight - rect.X);
        var dropdownRect = new ImRect(rect.X, rect.Y, dropdownWidth, rect.Height);

        DrawDropdownStub(dropdownRect, "Linear");

        DrawIconButton(swapRect, enabled: true, hovered: swapRect.Contains(Im.MousePos));
        DrawSwapIcon(swapRect, Im.Style.TextPrimary);

        DrawIconButton(rotateRect, enabled: true, hovered: rotateRect.Contains(Im.MousePos));
        DrawRotateIcon(rotateRect, Im.Style.TextPrimary);

        if (swapRect.Contains(Im.MousePos) || rotateRect.Contains(Im.MousePos) || dropdownRect.Contains(Im.MousePos))
        {
            Im.SetCursor(Silk.NET.Input.StandardCursor.Hand);
        }

        if (swapRect.Contains(Im.MousePos) && Im.MousePressed)
        {
            stopCount = Math.Clamp(stopCount, 2, FillGradientMaxStops);
            Span<float> stopT = stackalloc float[FillGradientMaxStops];
            Span<Color32> stopColor = stackalloc Color32[FillGradientMaxStops];
            for (int stopIndex = 0; stopIndex < stopCount; stopIndex++)
            {
                stopT[stopIndex] = GetFillGradientStopT(fillView, stopIndex);
                stopColor[stopIndex] = GetFillGradientStopColor(fillView, stopIndex);
            }

            for (int stopIndex = 0; stopIndex < stopCount; stopIndex++)
            {
                int sourceIndex = stopCount - 1 - stopIndex;
                SetFillGradientStopT(ref fillView, stopIndex, 1f - stopT[sourceIndex]);
                SetFillGradientStopColor(ref fillView, stopIndex, stopColor[sourceIndex]);
            }

            activeStopIndex = (byte)Math.Clamp(stopCount - 1 - activeStopIndex, 0, stopCount - 1);
            SyncFillGradientEndpoints(ref fillView);
            for (int stopIndex = 0; stopIndex < stopCount; stopIndex++)
            {
                ResetFillPopoverStopCache(stopIndex);
            }
            gradientDirection = new Vector2(-gradientDirection.X, -gradientDirection.Y);
        }

        if (rotateRect.Contains(Im.MousePos) && Im.MousePressed)
        {
            float lengthSq = gradientDirection.LengthSquared();
            if (lengthSq < 0.0001f)
            {
                gradientDirection = new Vector2(1f, 0f);
            }
            gradientDirection = new Vector2(-gradientDirection.Y, gradientDirection.X);
        }
    }

    private static void DrawDropdownStub(ImRect rect, string label)
    {
        uint background = rect.Contains(Im.MousePos) ? Im.Style.Hover : Im.Style.Surface;
        Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, background);
        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, Im.Style.Border, Im.Style.BorderWidth);

        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), rect.X + Im.Style.Padding, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        float chevronSize = 6f;
        float cx = rect.Right - Im.Style.Padding - chevronSize;
        float cy = rect.Y + rect.Height * 0.5f;
        ImIcons.DrawChevron(cx, cy - chevronSize * 0.5f, chevronSize, ImIcons.ChevronDirection.Down, Im.Style.TextSecondary);
    }

    private static void DrawIconButton(ImRect rect, bool enabled, bool hovered)
    {
        uint background = hovered ? Im.Style.Hover : Im.Style.Surface;
        uint border = enabled && hovered ? Im.Style.Primary : Im.Style.Border;
        Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, background);
        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, border, Im.Style.BorderWidth);
    }

    private static void DrawSwapIcon(ImRect rect, uint color)
    {
        float padding = 8f;
        float left = rect.X + padding;
        float right = rect.Right - padding;
        float cy = rect.Y + rect.Height * 0.5f;

        float arrowOffset = 4f;
        Im.DrawLine(left, cy - arrowOffset, right - 6f, cy - arrowOffset, 1.5f, color);
        Im.DrawLine(right - 6f, cy - arrowOffset, right - 10f, cy - arrowOffset - 3f, 1.5f, color);
        Im.DrawLine(right - 6f, cy - arrowOffset, right - 10f, cy - arrowOffset + 3f, 1.5f, color);

        Im.DrawLine(right, cy + arrowOffset, left + 6f, cy + arrowOffset, 1.5f, color);
        Im.DrawLine(left + 6f, cy + arrowOffset, left + 10f, cy + arrowOffset - 3f, 1.5f, color);
        Im.DrawLine(left + 6f, cy + arrowOffset, left + 10f, cy + arrowOffset + 3f, 1.5f, color);
    }

    private static void DrawRotateIcon(ImRect rect, uint color)
    {
        float cx = rect.X + rect.Width * 0.5f;
        float cy = rect.Y + rect.Height * 0.5f;
        float radius = Math.Max(6f, rect.Width * 0.22f);
        float thickness = 1.5f;

        float left = cx - radius;
        float top = cy - radius;
        float right = cx + radius;
        float bottom = cy + radius;

        Im.DrawLine(left, cy, left, top + radius * 0.5f, thickness, color);
        Im.DrawLine(left, top + radius * 0.5f, cx, top, thickness, color);
        Im.DrawLine(cx, top, right, top + radius * 0.5f, thickness, color);
        Im.DrawLine(right, top + radius * 0.5f, right, bottom, thickness, color);
        Im.DrawLine(right, bottom, left + radius * 0.35f, bottom, thickness, color);

        Im.DrawLine(left + radius * 0.35f, bottom, left + radius * 0.55f, bottom - 4f, thickness, color);
        Im.DrawLine(left + radius * 0.35f, bottom, left + radius * 0.55f, bottom + 4f, thickness, color);
    }

    private static void DrawMinusIcon(ImRect rect, uint color)
    {
        float cx = rect.X + rect.Width * 0.5f;
        float cy = rect.Y + rect.Height * 0.5f;
        float half = rect.Width * 0.22f;
        Im.DrawLine(cx - half, cy, cx + half, cy, 1.5f, color);
    }

    private bool DrawGradientBarWithStops(ImRect rect, ref FillComponent.ViewProxy fillView, ref byte activeStopIndex, int stopCount)
    {
        bool stopCountChanged = false;
        stopCount = Math.Clamp(stopCount, 2, FillGradientMaxStops);
        bool allowAdd = stopCount < FillGradientMaxStops;

        DrawCheckerboard(rect, 6f);
        Span<SdfGradientStop> stops = stackalloc SdfGradientStop[FillGradientMaxStops];
        for (int stopIndex = 0; stopIndex < stopCount; stopIndex++)
        {
            float t = GetFillGradientStopT(fillView, stopIndex);
            Color32 stopColor = GetFillGradientStopColor(fillView, stopIndex);
            stops[stopIndex] = new SdfGradientStop
            {
                Color = ImStyle.ToVector4(ToArgb(stopColor)),
                Params = new Vector4(t, 0f, 0f, 0f)
            };
        }
        Im.DrawRoundedRectGradientStops(rect.X, rect.Y, rect.Width, rect.Height, 6f, stops.Slice(0, stopCount));

        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, 6f, Im.Style.Border, Im.Style.BorderWidth);

        float handleSize = Math.Min(14f, rect.Height - 2f);
        float handleY = rect.Y + (rect.Height - handleSize) * 0.5f;
        float handleHalf = handleSize * 0.5f;

        int hoveredStopIndex = -1;
        for (int stopIndex = 0; stopIndex < stopCount; stopIndex++)
        {
            float stopT = GetFillGradientStopT(fillView, stopIndex);
            float handleX = rect.X + stopT * rect.Width;
            var handleRect = new ImRect(handleX - handleHalf, handleY, handleSize, handleSize);

            uint colorArgb = ToArgb(GetFillGradientStopColor(fillView, stopIndex));
            Im.DrawRoundedRect(handleRect.X, handleRect.Y, handleRect.Width, handleRect.Height, 3f, colorArgb);
            Im.DrawRoundedRectStroke(handleRect.X, handleRect.Y, handleRect.Width, handleRect.Height, 3f,
                activeStopIndex == stopIndex ? Im.Style.Primary : Im.Style.Border, 2f);

            if (handleRect.Contains(Im.MousePos))
            {
                hoveredStopIndex = stopIndex;
            }
        }

        bool hovered = rect.Contains(Im.MousePos) || hoveredStopIndex >= 0;
        if (hovered)
        {
            Im.SetCursor(Silk.NET.Input.StandardCursor.Hand);
        }

        if (Im.MousePressed)
        {
            if (hoveredStopIndex >= 0)
            {
                activeStopIndex = (byte)hoveredStopIndex;
                _gradientStopDragHandle = _colorPopoverFillHandle;
                _gradientStopDragIndex = hoveredStopIndex;
            }
            else if (allowAdd && rect.Contains(Im.MousePos))
            {
                float addT = rect.Width <= 0f ? 0f : (Im.MousePos.X - rect.X) / rect.Width;
                addT = Math.Clamp(addT, 0f, 1f);
                Color32 addColor = SampleFillGradientAtT(fillView, stopCount, addT);

                int insertIndex = stopCount;
                for (int stopIndex = 0; stopIndex < stopCount; stopIndex++)
                {
                    if (GetFillGradientStopT(fillView, stopIndex) > addT)
                    {
                        insertIndex = stopIndex;
                        break;
                    }
                }
                ShiftFillPopoverStopCachesForInsert(insertIndex, stopCount);
                InsertFillGradientStop(ref fillView, insertIndex, addT, addColor);
                stopCountChanged = true;
                activeStopIndex = (byte)insertIndex;
            }
        }

        bool isDragging = _gradientStopDragIndex >= 0 && _gradientStopDragHandle.Equals(_colorPopoverFillHandle);
        if (isDragging)
        {
            if (Im.MouseDown)
            {
                int dragIndex = _gradientStopDragIndex;
                if (dragIndex < 0 || dragIndex >= stopCount)
                {
                    _gradientStopDragIndex = -1;
                    _gradientStopDragHandle = default;
                }
                else
                {
                    float dragT = rect.Width <= 0f ? 0f : (Im.MousePos.X - rect.X) / rect.Width;
                    float minT = dragIndex <= 0 ? 0f : GetFillGradientStopT(fillView, dragIndex - 1) + 0.001f;
                    float maxT = dragIndex >= stopCount - 1 ? 1f : GetFillGradientStopT(fillView, dragIndex + 1) - 0.001f;
                    dragT = Math.Clamp(dragT, minT, maxT);
                    SetFillGradientStopT(ref fillView, dragIndex, dragT);
                }
            }
            else
            {
                _gradientStopDragIndex = -1;
                _gradientStopDragHandle = default;
            }
        }

        return stopCountChanged;
    }

    private static bool DrawGradientStopsHeader(ImRect rect, bool allowAdd)
    {
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text("Stops".AsSpan(), rect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        float buttonSize = rect.Height;
        var addRect = new ImRect(rect.Right - buttonSize, rect.Y, buttonSize, rect.Height);
        bool hovered = addRect.Contains(Im.MousePos);
        DrawIconButton(addRect, enabled: allowAdd, hovered: hovered);

        uint iconColor = allowAdd ? Im.Style.TextPrimary : Im.Style.TextSecondary;
        float cx = addRect.X + addRect.Width * 0.5f;
        float cy = addRect.Y + addRect.Height * 0.5f;
        Im.DrawLine(cx - 5f, cy, cx + 5f, cy, 1.5f, iconColor);
        Im.DrawLine(cx, cy - 5f, cx, cy + 5f, 1.5f, iconColor);

        if (hovered)
        {
            Im.SetCursor(Silk.NET.Input.StandardCursor.Hand);
            if (allowAdd && Im.MousePressed)
            {
                return true;
            }
        }

        return false;
    }

    private bool DrawGradientStopEditorRow(
        ImRect rect,
        int stopIndex,
        int percent,
        ref byte activeStopIndex,
        ref uint argb,
        char[] hexBuffer,
        ref int hexLength,
        ref uint lastArgb,
        char[] opacityBuffer,
        ref int opacityLength,
        ref byte lastAlpha,
        bool allowRemove)
    {
        float labelWidth = 52f;
        float removeWidth = rect.Height;
        float gap = 8f;

        var labelRect = new ImRect(rect.X, rect.Y, labelWidth, rect.Height);
        var removeRect = new ImRect(rect.Right - removeWidth, rect.Y, removeWidth, rect.Height);
        float fieldX = labelRect.Right + gap;
        float fieldWidth = Math.Max(1f, removeRect.X - gap - fieldX);
        var fieldRect = new ImRect(fieldX, rect.Y, fieldWidth, rect.Height);

        bool isActive = activeStopIndex == stopIndex;

        bool labelHovered = labelRect.Contains(Im.MousePos);
        uint labelBg = labelHovered ? Im.Style.Hover : Im.Style.Surface;
        uint labelBorder = isActive ? Im.Style.Primary : (labelHovered ? Im.Style.Primary : Im.Style.Border);
        Im.DrawRoundedRect(labelRect.X, labelRect.Y, labelRect.Width, labelRect.Height, Im.Style.CornerRadius, labelBg);
        Im.DrawRoundedRectStroke(labelRect.X, labelRect.Y, labelRect.Width, labelRect.Height, Im.Style.CornerRadius, labelBorder, Im.Style.BorderWidth);
        percent = Math.Clamp(percent, 0, 100);
        Span<char> percentLabel = stackalloc char[4];
        int labelLen = WritePercentDigits(percent, percentLabel);
        if (labelLen < percentLabel.Length)
        {
            percentLabel[labelLen++] = '%';
        }
        DrawCenteredText(percentLabel.Slice(0, labelLen), labelRect, Im.Style.TextPrimary);

        bool fieldHovered = fieldRect.Contains(Im.MousePos);
        uint fieldBg = fieldHovered ? Im.Style.Hover : Im.Style.Surface;
        uint fieldBorder = isActive ? Im.Style.Primary : (fieldHovered ? Im.Style.Primary : Im.Style.Border);
        Im.DrawRoundedRect(fieldRect.X, fieldRect.Y, fieldRect.Width, fieldRect.Height, Im.Style.CornerRadius, fieldBg);
        Im.DrawRoundedRectStroke(fieldRect.X, fieldRect.Y, fieldRect.Width, fieldRect.Height, Im.Style.CornerRadius, fieldBorder, Im.Style.BorderWidth);

        float swatchSize = Math.Max(ColorPreviewSwatchSizeMin, fieldRect.Height - ColorPreviewSwatchPadding * 2f);
        float swatchX = fieldRect.X + ColorPreviewSwatchPadding;
        float swatchY = fieldRect.Y + (fieldRect.Height - swatchSize) * 0.5f;
        var swatchRect = new ImRect(swatchX, swatchY, swatchSize, swatchSize);
        DrawCheckerboard(swatchRect, 5f);
        Im.DrawRoundedRect(swatchRect.X, swatchRect.Y, swatchRect.Width, swatchRect.Height, 2f, argb);
        Im.DrawRoundedRectStroke(swatchRect.X, swatchRect.Y, swatchRect.Width, swatchRect.Height, 2f, Im.Style.Border, 1f);

        float contentX = swatchRect.Right + 8f;
        float contentRight = fieldRect.Right - 8f;

        Im.Context.PushId(stopIndex);

        int hexWidgetId = Im.Context.GetId("hex");
        bool isHexFocused = Im.Context.IsFocused(hexWidgetId);
        if (!isHexFocused && (hexLength < 0 || argb != lastArgb))
        {
            hexLength = 6;
            WriteHex6((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, hexBuffer.AsSpan(0, 6));
            lastArgb = argb;
        }

        float opacityFieldWidth = 56f;
        float opacityFieldLeft = contentRight - opacityFieldWidth;
        float hexFieldWidth = Math.Max(1f, opacityFieldLeft - contentX);
        var hexFieldRect = new ImRect(contentX, fieldRect.Y, hexFieldWidth, fieldRect.Height);

        bool wasFocused = isHexFocused;
        bool textChanged = Im.TextInput("hex", hexBuffer.AsSpan(), ref hexLength, 8, hexFieldRect.X, hexFieldRect.Y, hexFieldRect.Width,
            Im.ImTextInputFlags.NoBackground | Im.ImTextInputFlags.NoRounding | Im.ImTextInputFlags.BorderLeft);
        isHexFocused = Im.Context.IsFocused(hexWidgetId);

        if (textChanged)
        {
            bool ok = TryApplyHexToArgb(hexBuffer.AsSpan(0, Math.Max(0, hexLength)), ref argb);
            if (ok)
            {
                lastArgb = argb;
                hexLength = 6;
                EnsureHexLength(ref hexLength);
                WriteHex6((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, hexBuffer.AsSpan(0, 6));
            }
        }

        if (wasFocused && !isHexFocused)
        {
            bool ok = TryApplyHexToArgb(hexBuffer.AsSpan(0, Math.Max(0, hexLength)), ref argb);
            if (!ok)
            {
                hexLength = 6;
                EnsureHexLength(ref hexLength);
                WriteHex6((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, hexBuffer.AsSpan(0, 6));
            }
            lastArgb = argb;
        }

        bool opacityHovered = DrawOpacityField(fieldRect, contentRight, ref argb, opacityBuffer.AsSpan(), ref opacityLength, ref lastAlpha, allowScrub: true);

        Im.Context.PopId();

        bool swatchHovered = swatchRect.Contains(Im.MousePos);
        bool hexHovered = hexFieldRect.Contains(Im.MousePos);

        if (labelHovered || swatchHovered || (fieldHovered && !hexHovered && !opacityHovered))
        {
            Im.SetCursor(Silk.NET.Input.StandardCursor.Hand);
            if (Im.MousePressed)
            {
                activeStopIndex = (byte)stopIndex;
            }
        }

        bool removeHovered = removeRect.Contains(Im.MousePos);
        DrawIconButton(removeRect, enabled: allowRemove, hovered: removeHovered);
        uint minusColor = allowRemove ? Im.Style.TextPrimary : Im.Style.TextSecondary;
        float mx = removeRect.X + removeRect.Width * 0.5f;
        float my = removeRect.Y + removeRect.Height * 0.5f;
        Im.DrawLine(mx - 6f, my, mx + 6f, my, 1.5f, minusColor);

        if (removeHovered)
        {
            Im.SetCursor(Silk.NET.Input.StandardCursor.Hand);
        }

        return allowRemove && removeHovered && Im.MousePressed;
    }

    private static void DrawCenteredText(string text, ImRect rect, uint color)
    {
        DrawCenteredText(text.AsSpan(), rect, color);
    }

    private static void DrawCenteredText(ReadOnlySpan<char> text, ImRect rect, uint color)
    {
        float width = ApproxTextWidth(text, Im.Style.FontSize);
        float x = rect.X + (rect.Width - width) * 0.5f;
        float y = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(text, x, y, Im.Style.FontSize, color);
    }

    private static float ApproxTextWidth(ReadOnlySpan<char> text, float fontSize)
    {
        return text.Length * fontSize * 0.6f;
    }

    private static void DrawHorizontalGradient(ImRect rect, uint argbA, uint argbB, int segments)
    {
        if (segments < 2)
        {
            segments = 2;
        }

        float step = rect.Width / segments;
        for (int segmentIndex = 0; segmentIndex < segments; segmentIndex++)
        {
            float t = segments <= 1 ? 0f : segmentIndex / (float)(segments - 1);
            uint color = ToArgb(FromArgb(argbA), FromArgb(argbB), t);
            float x = rect.X + segmentIndex * step;
            float w = segmentIndex == segments - 1 ? rect.Right - x : step;
            Im.DrawRect(x, rect.Y, w, rect.Height, color);
        }
    }
}
