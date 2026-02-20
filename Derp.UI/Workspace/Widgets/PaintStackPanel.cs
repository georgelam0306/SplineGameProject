using System;
using System.Numerics;
using Core;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;
using DerpLib.ImGui.Widgets;
using FontAwesome.Sharp;
using Property;
using Property.Runtime;

namespace Derp.UI;

internal static class PaintStackPanel
{
    private const float RowHeight = 28f;
    private const float RowPaddingX = 10f;
    private const float IconButtonSize = 22f;
    private const float DragThresholdPx = 5f;
    private const float KeyIconWidth = 18f;
    private const float ValueFieldWidth = 56f;

    private struct RowInfo
    {
        public ImRect Rect;
        public int LayerIndex;
    }

    private struct DragState
    {
        public bool IsActive;
        public bool IsDragging;
        public EntityId OwnerEntity;
        public int SourceLayerOffset;
        public int TargetInsertIndex;
        public Vector2 StartMousePos;
    }

    private static readonly RowInfo[] Rows = new RowInfo[64];
    private static int _rowCount;
    private static DragState _drag;

    private static readonly string VisibleIcon = ((char)IconChar.Eye).ToString();
    private static readonly string HiddenIcon = ((char)IconChar.EyeSlash).ToString();

    public static void Draw(UiWorkspace workspace, EntityId ownerEntity)
    {
        InspectorCard.Begin("Paint");

        DrawAddRow(workspace, ownerEntity);
        DrawLayers(workspace, ownerEntity);

        InspectorCard.End();
    }

    private static void DrawAddRow(UiWorkspace workspace, EntityId ownerEntity)
    {
        var rect = ImLayout.AllocateRect(0f, RowHeight);
        rect = new ImRect(rect.X + RowPaddingX, rect.Y, Math.Max(1f, rect.Width - RowPaddingX * 2f), rect.Height);

        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text("Layers".AsSpan(), rect.X, textY, Im.Style.FontSize, Im.Style.TextSecondary);

        var plusRect = new ImRect(rect.Right - IconButtonSize, rect.Y + (rect.Height - IconButtonSize) * 0.5f, IconButtonSize, IconButtonSize);
        bool plusHovered = plusRect.Contains(Im.MousePos);
        DrawIconButton(plusRect, plusHovered);
        DrawPlusIcon(plusRect, Im.Style.TextPrimary);

        uint stableId = workspace.World.GetStableId(ownerEntity);
        Im.Context.PushId(unchecked((int)stableId));
        int plusWidgetId = Im.Context.GetId("paint_add");
        if (plusHovered && Im.MousePressed)
        {
            ImContextMenu.OpenAt("paint_add_menu", plusRect.X, plusRect.Bottom);
        }

        if (ImContextMenu.Begin("paint_add_menu"))
        {
            if (ImContextMenu.Item("Fill"))
            {
                workspace.Commands.AddFillPaintLayer(ownerEntity, insertIndex: 0);
            }

            if (ImContextMenu.Item("Stroke"))
            {
                workspace.Commands.AddStrokePaintLayer(ownerEntity, insertIndex: 0);
            }
            ImContextMenu.End();
        }
        Im.Context.PopId();

        workspace.Commands.NotifyPropertyWidgetState(plusWidgetId, isEditing: false);
        ImLayout.Space(2f);
    }

    private static void DrawLayers(UiWorkspace workspace, EntityId ownerEntity)
    {
        _rowCount = 0;

        if (!workspace.World.TryGetComponent(ownerEntity, PaintComponent.Api.PoolIdConst, out AnyComponentHandle paintAny))
        {
            InspectorHint.Draw("No paint");
            _drag.IsActive = false;
            return;
        }

        var paintHandle = new PaintComponentHandle(paintAny.Index, paintAny.Generation);
        var paintView = PaintComponent.Api.FromHandle(workspace.PropertyWorld, paintHandle);
        int count = paintView.IsAlive ? paintView.LayerCount : 0;
        if (count <= 0)
        {
            InspectorHint.Draw("No paint layers");
            _drag.IsActive = false;
            return;
        }

        AnyComponentHandle paintComponent = PaintComponentProperties.ToAnyHandle(paintHandle);
        ReadOnlySpan<int> layerKind = PaintComponentProperties.LayerKindArray(workspace.PropertyWorld, paintHandle);

        var listRect = ImLayout.AllocateRect(0f, RowHeight * count);
        float x = listRect.X + RowPaddingX;
        float y = listRect.Y;
        float w = Math.Max(1f, listRect.Width - RowPaddingX * 2f);

        for (int layerIndex = 0; layerIndex < count; layerIndex++)
        {
            var rowRect = new ImRect(x, y + layerIndex * RowHeight, w, RowHeight);
            if (_rowCount < Rows.Length)
            {
                Rows[_rowCount++] = new RowInfo { Rect = rowRect, LayerIndex = layerIndex };
            }

            int kindValue = (uint)layerIndex < (uint)layerKind.Length ? layerKind[layerIndex] : (int)PaintLayerKind.Fill;
            DrawLayerRow(workspace, ownerEntity, paintComponent, layerIndex, (PaintLayerKind)Math.Clamp(kindValue, 0, 1), rowRect);
        }

        HandleDragAndDrop(workspace, ownerEntity, count);
        DrawDropPreview();
    }

    private static void DrawLayerRow(UiWorkspace workspace, EntityId ownerEntity, AnyComponentHandle paintComponent, int layerIndex, PaintLayerKind kind, ImRect rowRect)
    {
        bool hovered = rowRect.Contains(Im.MousePos);
        uint background = hovered ? ImStyle.Lerp(Im.Style.Surface, 0xFF000000, 0.18f) : Im.Style.Surface;
        Im.DrawRoundedRect(rowRect.X, rowRect.Y, rowRect.Width, rowRect.Height, Im.Style.CornerRadius, background);
        Im.DrawRoundedRectStroke(rowRect.X, rowRect.Y, rowRect.Width, rowRect.Height, Im.Style.CornerRadius, Im.Style.Border, Im.Style.BorderWidth);

        Im.Context.PushId(layerIndex);

        float centerY = rowRect.Y + rowRect.Height * 0.5f;
        float iconY = centerY - IconButtonSize * 0.5f;

        var settingsRect = new ImRect(rowRect.X + 2f, iconY, IconButtonSize, IconButtonSize);
        bool settingsHovered = settingsRect.Contains(Im.MousePos);
        DrawIconButton(settingsRect, settingsHovered);
        DrawSlidersIcon(settingsRect, Im.Style.TextSecondary);
        if (settingsHovered && Im.MousePressed)
        {
            workspace.PropertyInspector.OpenPaintLayerOptionsPopover(ownerEntity, layerIndex, settingsRect.Offset(Im.CurrentTranslation));
        }

        var deleteRect = new ImRect(rowRect.Right - IconButtonSize - 2f, iconY, IconButtonSize, IconButtonSize);
        bool deleteHovered = deleteRect.Contains(Im.MousePos);
        DrawIconButton(deleteRect, deleteHovered);
        DrawMinusIcon(deleteRect, Im.Style.TextSecondary);
        if (deleteHovered && Im.MousePressed)
        {
            workspace.Commands.RemovePaintLayer(ownerEntity, layerIndex);
            Im.Context.PopId();
            return;
        }

        var eyeRect = new ImRect(deleteRect.X - IconButtonSize - 2f, iconY, IconButtonSize, IconButtonSize);
        bool eyeHovered = eyeRect.Contains(Im.MousePos);
        DrawIconButton(eyeRect, eyeHovered);

        var visibleSlot = PaintComponentPropertySlot.ArrayElement(paintComponent, "LayerIsVisible", layerIndex, PropertyKind.Bool);
        bool isVisible = PropertyDispatcher.ReadBool(workspace.PropertyWorld, visibleSlot);
        DrawEyeIcon(eyeRect, isVisible, Im.Style.TextSecondary);
        int visibilityWidgetId = Im.Context.GetId("visibility");
        if (eyeHovered && Im.MousePressed)
        {
            workspace.Commands.SetPropertyValue(visibilityWidgetId, isEditing: false, visibleSlot, PropertyValue.FromBool(!isVisible));
        }
        else
        {
            workspace.Commands.NotifyPropertyWidgetState(visibilityWidgetId, isEditing: false);
        }

        float contentX = settingsRect.Right + 8f;
        float contentRight = eyeRect.X - 6f;
        float contentWidth = Math.Max(1f, contentRight - contentX);

        var opacitySlot = PaintComponentPropertySlot.ArrayElement(paintComponent, "LayerOpacity", layerIndex, PropertyKind.Float);

        const float gap = 6f;
        float valueFieldX = contentX + contentWidth - ValueFieldWidth;
        var valueRect = new ImRect(valueFieldX, rowRect.Y, ValueFieldWidth, rowRect.Height);
        var mainRect = new ImRect(contentX, rowRect.Y, Math.Max(1f, valueRect.X - contentX - gap), rowRect.Height);

        if (kind == PaintLayerKind.Stroke)
        {
            var widthSlot = PaintComponentPropertySlot.ArrayElement(paintComponent, "StrokeWidth", layerIndex, PropertyKind.Float);
            DrawStrokeWidthField(workspace, valueRect, widthSlot);
        }
        else
        {
            DrawOpacityPercentField(workspace, valueRect, opacitySlot);
        }

        if (kind == PaintLayerKind.Fill)
        {
            var paintHandle = new PaintComponentHandle(paintComponent.Index, paintComponent.Generation);
            DrawPaintFillLayerFieldWithKey(workspace, ownerEntity, mainRect, "fill_color", paintHandle, paintComponent, layerIndex);
        }
        else if (kind == PaintLayerKind.Stroke)
        {
            var paintHandle = new PaintComponentHandle(paintComponent.Index, paintComponent.Generation);
            DrawPaintStrokeLayerFieldWithKey(workspace, ownerEntity, mainRect, "stroke_color", paintHandle, paintComponent, layerIndex);
        }
        else
        {
            Im.Text("Paint".AsSpan(), mainRect.X, mainRect.Y + (mainRect.Height - Im.Style.FontSize) * 0.5f, Im.Style.FontSize, Im.Style.TextPrimary);
        }

        HandleDragStart(ownerEntity, layerIndex, rowRect, settingsRect, eyeRect, deleteRect);
        Im.Context.PopId();
    }

    private static void DrawPaintFillLayerFieldWithKey(
        UiWorkspace workspace,
        EntityId ownerEntity,
        ImRect rect,
        string pickerId,
        PaintComponentHandle paintHandle,
        AnyComponentHandle paintComponent,
        int layerIndex)
    {
        var colorRect = new ImRect(rect.X, rect.Y, Math.Max(1f, rect.Width - KeyIconWidth), rect.Height);
        workspace.PropertyInspector.DrawPaintFillLayerPreviewRow(colorRect, pickerId, paintHandle, layerIndex);

        var useGradientSlot = PaintComponentPropertySlot.ArrayElement(paintComponent, "FillUseGradient", layerIndex, PropertyKind.Bool);
        bool useGradient = PropertyDispatcher.ReadBool(workspace.PropertyWorld, useGradientSlot);

        PropertySlot keySlot;
        if (useGradient)
        {
            var stopCountSlot = PaintComponentPropertySlot.ArrayElement(paintComponent, "FillGradientStopCount", layerIndex, PropertyKind.Int);
            int stopCount = PropertyDispatcher.ReadInt(workspace.PropertyWorld, stopCountSlot);
            if (stopCount > 0)
            {
                keySlot = PaintComponentPropertySlot.ArrayElement(paintComponent, "FillGradientStopColor0", layerIndex, PropertyKind.Color32);
            }
            else
            {
                keySlot = PaintComponentPropertySlot.ArrayElement(paintComponent, "FillGradientColorA", layerIndex, PropertyKind.Color32);
            }
        }
        else
        {
            keySlot = PaintComponentPropertySlot.ArrayElement(paintComponent, "FillColor", layerIndex, PropertyKind.Color32);
        }

        workspace.PropertyInspector.DrawPropertyBindingContextMenu(ownerEntity, keySlot, colorRect, allowUnbind: true);

        if (workspace.Commands.TryGetKeyableState(keySlot, out bool hasTrack, out bool hasKey))
        {
            var iconRect = new ImRect(rect.Right - KeyIconWidth, rect.Y, KeyIconWidth, rect.Height);
            DrawKeyIconDiamond(iconRect, filled: hasKey, highlighted: hasTrack);
            if (iconRect.Contains(Im.MousePos) && Im.MousePressed)
            {
                workspace.Commands.ToggleKeyAtPlayhead(keySlot);
            }
        }
    }

    private static void DrawPaintStrokeLayerFieldWithKey(
        UiWorkspace workspace,
        EntityId ownerEntity,
        ImRect rect,
        string pickerId,
        PaintComponentHandle paintHandle,
        AnyComponentHandle paintComponent,
        int layerIndex)
    {
        var colorRect = new ImRect(rect.X, rect.Y, Math.Max(1f, rect.Width - KeyIconWidth), rect.Height);
        workspace.PropertyInspector.DrawPaintStrokeLayerPreviewRow(colorRect, pickerId, paintHandle, layerIndex);

        PropertySlot keySlot = PaintComponentPropertySlot.ArrayElement(paintComponent, "StrokeColor", layerIndex, PropertyKind.Color32);
        workspace.PropertyInspector.DrawPropertyBindingContextMenu(ownerEntity, keySlot, colorRect, allowUnbind: true);

        if (workspace.Commands.TryGetKeyableState(keySlot, out bool hasTrack, out bool hasKey))
        {
            var iconRect = new ImRect(rect.Right - KeyIconWidth, rect.Y, KeyIconWidth, rect.Height);
            DrawKeyIconDiamond(iconRect, filled: hasKey, highlighted: hasTrack);
            if (iconRect.Contains(Im.MousePos) && Im.MousePressed)
            {
                workspace.Commands.ToggleKeyAtPlayhead(keySlot);
            }
        }
    }

    private static void DrawOpacityPercentField(UiWorkspace workspace, ImRect rect, PropertySlot slot)
    {
        float opacity = PropertyDispatcher.ReadFloat(workspace.PropertyWorld, slot);
        float percent = Math.Clamp(opacity, 0f, 1f) * 100f;

        float inputY = rect.Y + (rect.Height - Im.Style.MinButtonHeight) * 0.5f;
        bool changed = ImScalarInput.DrawAt("paint_opacity", rect.X, inputY, rect.Width, ref percent, 0f, 100f, "F0");

        int widgetId = Im.Context.GetId("paint_opacity");
        bool isEditing = Im.Context.IsActive(widgetId) || Im.Context.IsFocused(widgetId);
        float newOpacity = Math.Clamp(percent / 100f, 0f, 1f);
        if (changed)
        {
            workspace.Commands.SetPropertyValue(widgetId, isEditing, slot, PropertyValue.FromFloat(newOpacity));
        }
        else
        {
            workspace.Commands.NotifyPropertyWidgetState(widgetId, isEditing);
        }

        if (workspace.Commands.TryGetKeyableState(slot, out bool hasTrack, out bool hasKey))
        {
            var iconRect = new ImRect(rect.Right - KeyIconWidth, rect.Y, KeyIconWidth, rect.Height);
            DrawKeyIconDiamond(iconRect, filled: hasKey, highlighted: hasTrack);
            if (iconRect.Contains(Im.MousePos) && Im.MousePressed)
            {
                workspace.Commands.ToggleKeyAtPlayhead(slot);
            }
        }
    }

    private static void DrawStrokeWidthField(UiWorkspace workspace, ImRect rect, PropertySlot slot)
    {
        float width = Math.Max(0f, PropertyDispatcher.ReadFloat(workspace.PropertyWorld, slot));

        float inputY = rect.Y + (rect.Height - Im.Style.MinButtonHeight) * 0.5f;
        bool changed = ImScalarInput.DrawAt("stroke_width", rect.X, inputY, rect.Width, ref width, 0f, 2048f, "F2");

        int widgetId = Im.Context.GetId("stroke_width");
        bool isEditing = Im.Context.IsActive(widgetId) || Im.Context.IsFocused(widgetId);
        if (changed)
        {
            workspace.Commands.SetPropertyValue(widgetId, isEditing, slot, PropertyValue.FromFloat(width));
        }
        else
        {
            workspace.Commands.NotifyPropertyWidgetState(widgetId, isEditing);
        }

        if (workspace.Commands.TryGetKeyableState(slot, out bool hasTrack, out bool hasKey))
        {
            var iconRect = new ImRect(rect.Right - KeyIconWidth, rect.Y, KeyIconWidth, rect.Height);
            DrawKeyIconDiamond(iconRect, filled: hasKey, highlighted: hasTrack);
            if (iconRect.Contains(Im.MousePos) && Im.MousePressed)
            {
                workspace.Commands.ToggleKeyAtPlayhead(slot);
            }
        }
    }

    private static void DrawKeyIconDiamond(ImRect rect, bool filled, bool highlighted)
    {
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

    private static void HandleDragStart(EntityId ownerEntity, int layerOffset, ImRect rowRect, ImRect settingsRect, ImRect eyeRect, ImRect deleteRect)
    {
        if (_drag.IsActive)
        {
            return;
        }

        if (!rowRect.Contains(Im.MousePos))
        {
            return;
        }

        if (!Im.MousePressed)
        {
            return;
        }

        // Don't start a reorder-drag if the press began on an interactive control.
        if (settingsRect.Contains(Im.MousePos) || eyeRect.Contains(Im.MousePos) || deleteRect.Contains(Im.MousePos))
        {
            return;
        }

        // If a child widget (e.g., text input) took active/focus this frame, don't begin a drag.
        if (Im.Context.ActiveId != 0 || Im.Context.FocusId != 0)
        {
            return;
        }

        _drag = new DragState
        {
            IsActive = true,
            IsDragging = false,
            OwnerEntity = ownerEntity,
            SourceLayerOffset = layerOffset,
            TargetInsertIndex = -1,
            StartMousePos = Im.MousePos
        };
    }

    private static void HandleDragAndDrop(UiWorkspace workspace, EntityId ownerEntity, int layerCount)
    {
        if (!_drag.IsActive || _drag.OwnerEntity.Value != ownerEntity.Value)
        {
            return;
        }

        if (!Im.MouseDown)
        {
            if (_drag.IsDragging && _drag.TargetInsertIndex >= 0)
            {
                workspace.Commands.MovePaintLayer(ownerEntity, _drag.SourceLayerOffset, _drag.TargetInsertIndex);
            }

            _drag.IsActive = false;
            _drag.IsDragging = false;
            _drag.TargetInsertIndex = -1;
            _drag.OwnerEntity = EntityId.Null;
            return;
        }

        Vector2 delta = Im.MousePos - _drag.StartMousePos;
        if (!_drag.IsDragging)
        {
            if (MathF.Abs(delta.X) >= DragThresholdPx || MathF.Abs(delta.Y) >= DragThresholdPx)
            {
                _drag.IsDragging = true;
            }
            else
            {
                return;
            }
        }

        int target = layerCount;
        for (int i = 0; i < _rowCount; i++)
        {
            RowInfo row = Rows[i];
            if (!row.Rect.Contains(Im.MousePos))
            {
                continue;
            }

            float midY = row.Rect.Y + row.Rect.Height * 0.5f;
            target = Im.MousePos.Y < midY ? row.LayerIndex : row.LayerIndex + 1;
            break;
        }

        target = Math.Clamp(target, 0, layerCount);
        _drag.TargetInsertIndex = target;
    }

    private static void DrawDropPreview()
    {
        if (!_drag.IsActive || !_drag.IsDragging || _drag.TargetInsertIndex < 0)
        {
            return;
        }

        float x = float.MaxValue;
        float right = float.MinValue;
        for (int i = 0; i < _rowCount; i++)
        {
            RowInfo row = Rows[i];
            if (row.Rect.X < x)
            {
                x = row.Rect.X;
            }
            if (row.Rect.Right > right)
            {
                right = row.Rect.Right;
            }
        }

        if (x == float.MaxValue || right == float.MinValue)
        {
            return;
        }

        float y;
        if (_drag.TargetInsertIndex >= _rowCount)
        {
            y = Rows[_rowCount - 1].Rect.Bottom;
        }
        else
        {
            y = Rows[_drag.TargetInsertIndex].Rect.Y;
        }

        Im.DrawLine(x, y, right, y, 2f, Im.Style.Primary);
    }

    private static void DrawIconButton(ImRect rect, bool hovered)
    {
        uint background = hovered ? ImStyle.Lerp(Im.Style.Surface, 0xFF000000, 0.20f) : Im.Style.Surface;
        Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, background);
        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, Im.Style.Border, Im.Style.BorderWidth);
    }

    private static void DrawPlusIcon(ImRect rect, uint color)
    {
        float cx = rect.X + rect.Width * 0.5f;
        float cy = rect.Y + rect.Height * 0.5f;
        float half = rect.Width * 0.20f;
        Im.DrawLine(cx - half, cy, cx + half, cy, 2f, color);
        Im.DrawLine(cx, cy - half, cx, cy + half, 2f, color);
    }

    private static void DrawMinusIcon(ImRect rect, uint color)
    {
        float cx = rect.X + rect.Width * 0.5f;
        float cy = rect.Y + rect.Height * 0.5f;
        float half = rect.Width * 0.20f;
        Im.DrawLine(cx - half, cy, cx + half, cy, 2f, color);
    }

    private static void DrawSlidersIcon(ImRect rect, uint color)
    {
        float padding = 6f;
        float left = rect.X + padding;
        float right = rect.Right - padding;
        float cy = rect.Y + rect.Height * 0.5f;
        float y0 = cy - 4f;
        float y1 = cy;
        float y2 = cy + 4f;

        Im.DrawLine(left, y0, right, y0, 1.5f, color);
        Im.DrawLine(left, y1, right, y1, 1.5f, color);
        Im.DrawLine(left, y2, right, y2, 1.5f, color);

        float knobX = left + (right - left) * 0.65f;
        float knobRadius = 2.5f;
        Im.DrawCircle(knobX, y0, knobRadius, color);
        Im.DrawCircle(left + (right - left) * 0.35f, y1, knobRadius, color);
        Im.DrawCircle(left + (right - left) * 0.50f, y2, knobRadius, color);
    }

    private static void DrawEyeIcon(ImRect rect, bool open, uint color)
    {
        DrawCenteredIcon(rect, open ? VisibleIcon : HiddenIcon, color);
    }

    private static void DrawCenteredIcon(ImRect rect, string icon, uint color)
    {
        float fontSize = MathF.Min(Im.Style.FontSize, MathF.Min(rect.Width, rect.Height));
        float iconWidth = Im.MeasureTextWidth(icon.AsSpan(), fontSize);
        float x = rect.X + (rect.Width - iconWidth) * 0.5f;
        float y = rect.Y + (rect.Height - fontSize) * 0.5f;
        Im.Text(icon.AsSpan(), x, y, fontSize, color);
    }

    private static void DrawCheckerboard(ImRect rect, float cellSize)
    {
        uint a = 0xFF3A3A3A;
        uint b = 0xFF2E2E2E;

        int cols = (int)MathF.Ceiling(rect.Width / cellSize);
        int rows = (int)MathF.Ceiling(rect.Height / cellSize);

        for (int row = 0; row < rows; row++)
        {
            float y = rect.Y + row * cellSize;
            float h = Math.Min(cellSize, rect.Bottom - y);
            for (int col = 0; col < cols; col++)
            {
                float x = rect.X + col * cellSize;
                float w = Math.Min(cellSize, rect.Right - x);
                bool dark = ((row + col) & 1) == 0;
                Im.DrawRect(x, y, w, h, dark ? a : b);
            }
        }
    }
}
