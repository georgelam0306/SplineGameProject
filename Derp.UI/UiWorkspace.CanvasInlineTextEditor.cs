using System;
using System.Numerics;
using Core;
using Pooled.Runtime;
using Property;
using Property.Runtime;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Input;
using DerpLib.ImGui.Widgets;
using DerpLib.Text;

namespace Derp.UI;

public sealed partial class UiWorkspace
{
    private const string CanvasInlineTextEditorId = "canvas_inline_text_editor";

    private bool _inlineTextEditActive;
    private EntityId _inlineTextEditEntity;
    private uint _inlineTextEditStableId;
    private int _inlineTextEditStartFrame;
    private bool _inlineTextEditWantsFocus;
    private StringHandle _inlineTextEditOriginalText;
    private readonly ImTextBuffer _inlineTextEditBuffer = new(initialCapacity: 1024);

    internal bool IsCanvasInlineTextEditing => _inlineTextEditActive;

    private void BeginCanvasInlineTextEdit(EntityId textEntity)
    {
        if (textEntity.IsNull || _world.GetNodeType(textEntity) != UiNodeType.Text)
        {
            return;
        }

        if (IsEntityLockedInEditor(textEntity))
        {
            return;
        }

        if (_inlineTextEditActive)
        {
            CommitCanvasInlineTextEdit();
        }

        if (!_world.TryGetComponent(textEntity, TextComponent.Api.PoolIdConst, out AnyComponentHandle textAny))
        {
            return;
        }

        var textView = TextComponent.Api.FromHandle(_propertyWorld, new TextComponentHandle(textAny.Index, textAny.Generation));
        if (!textView.IsAlive)
        {
            return;
        }

        _inlineTextEditActive = true;
        _inlineTextEditEntity = textEntity;
        _inlineTextEditStableId = _world.GetStableId(textEntity);
        _inlineTextEditStartFrame = Im.Context.FrameCount;
        _inlineTextEditWantsFocus = true;
        _inlineTextEditOriginalText = textView.Text;

        string currentText = textView.Text.IsValid ? textView.Text.ToString() : string.Empty;
        _inlineTextEditBuffer.SetText(currentText.AsSpan());

        CancelTransientInteractions();
    }

    private bool TryBeginCanvasInlineTextEditFromSelection(Vector2 mouseWorld)
    {
        if (_selectedEntities.Count != 1)
        {
            return false;
        }

        EntityId selectedEntity = _selectedEntities[0];
        if (selectedEntity.IsNull || _world.GetNodeType(selectedEntity) != UiNodeType.Text)
        {
            return false;
        }

        if (IsEntityHiddenInEditor(selectedEntity) || IsEntityLockedInEditor(selectedEntity))
        {
            return false;
        }

        if (!TryGetTransformParentWorldTransformEcs(selectedEntity, out WorldTransform parentWorldTransform))
        {
            parentWorldTransform = IdentityWorldTransform;
        }

        if (!TryGetTextWorldTransformEcs(selectedEntity, parentWorldTransform, out ShapeWorldTransform worldTransform))
        {
            return false;
        }

        if (!_world.TryGetComponent(selectedEntity, RectGeometryComponent.Api.PoolIdConst, out AnyComponentHandle rectAny))
        {
            return false;
        }

        var rectGeometry = RectGeometryComponent.Api.FromHandle(_propertyWorld, new RectGeometryComponentHandle(rectAny.Index, rectAny.Generation));
        if (!rectGeometry.IsAlive)
        {
            return false;
        }

        float scaleX = worldTransform.ScaleWorld.X == 0f ? 1f : worldTransform.ScaleWorld.X;
        float scaleY = worldTransform.ScaleWorld.Y == 0f ? 1f : worldTransform.ScaleWorld.Y;
        float widthWorld = rectGeometry.Size.X * MathF.Abs(scaleX);
        float heightWorld = rectGeometry.Size.Y * MathF.Abs(scaleY);

        Vector2 anchor = worldTransform.Anchor;
        Vector2 anchorOffset = new Vector2(
            (anchor.X - 0.5f) * widthWorld,
            (anchor.Y - 0.5f) * heightWorld);
        Vector2 centerWorld = worldTransform.PositionWorld - RotateVector(anchorOffset, worldTransform.RotationRadians);
        Vector2 halfSizeWorld = new Vector2(widthWorld * 0.5f, heightWorld * 0.5f);

        if (!IsPointInsideOrientedRect(centerWorld, halfSizeWorld, worldTransform.RotationRadians, mouseWorld))
        {
            return false;
        }

        BeginCanvasInlineTextEdit(selectedEntity);
        return true;
    }

    private void HandleCanvasInlineTextEditorInput(ImInput input, Vector2 canvasOrigin, Vector2 mouseCanvas)
    {
        if (!_inlineTextEditActive || _inlineTextEditEntity.IsNull)
        {
            return;
        }

        if (input.KeyEscape)
        {
            CommitCanvasInlineTextEdit();
            return;
        }

        if (input.MousePressed && Im.Context.FrameCount != _inlineTextEditStartFrame)
        {
            if (!TryGetCanvasInlineTextEditorParams(
                    canvasOrigin,
                    out ImRect rectCanvas,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _))
            {
                CommitCanvasInlineTextEdit();
                return;
            }

            if (!rectCanvas.Contains(mouseCanvas))
            {
                CommitCanvasInlineTextEdit();
            }
        }
    }

    internal void UpdateCanvasInlineTextEditor(ImRect contentRect, Vector2 canvasOrigin)
    {
        if (!_inlineTextEditActive || _inlineTextEditEntity.IsNull)
        {
            return;
        }

        if (_world.GetNodeType(_inlineTextEditEntity) != UiNodeType.Text)
        {
            CancelCanvasInlineTextEdit();
            return;
        }

        if (IsEntityHiddenInEditor(_inlineTextEditEntity) || IsEntityLockedInEditor(_inlineTextEditEntity))
        {
            CancelCanvasInlineTextEdit();
            return;
        }

        if (!TryGetCanvasInlineTextEditorParams(
                canvasOrigin,
                out ImRect rectCanvas,
                out bool multiline,
                out float fontSizeCanvasPx,
                out float lineHeightScale,
                out float letterSpacingCanvasPx,
                out bool wordWrap,
                out AnyComponentHandle textAny))
        {
            CancelCanvasInlineTextEdit();
            return;
        }

        var ctx = Im.Context;
        Font font = ctx.Font!;

        var textHandle = new TextComponentHandle(textAny.Index, textAny.Generation);
        var textView = TextComponent.Api.FromHandle(_propertyWorld, textHandle);
        if (!textView.IsAlive)
        {
            CancelCanvasInlineTextEdit();
            return;
        }

        TextLayoutCache layout = GetOrCreateTextLayoutCache(textAny);
        layout.Ensure(
            font,
            textView.Text,
            textView.Font,
            fontSizeCanvasPx,
            lineHeightScale,
            letterSpacingCanvasPx,
            textView.Multiline,
            textView.Wrap,
            textView.Overflow,
            textView.AlignX,
            textView.AlignY,
            new Vector2(rectCanvas.Width, rectCanvas.Height));

        float resolvedFontSizeCanvasPx = layout.ResolvedFontSizePx;
        float scaleFont = resolvedFontSizeCanvasPx / font.BaseSizePixels;
        float clampedLineHeightScale = Math.Clamp(lineHeightScale, 0.25f, 8f);
        float lineHeightCanvasPx = font.LineHeightPixels * scaleFont * clampedLineHeightScale;

        int alignXValue = Math.Clamp(textView.AlignX, (int)TextHorizontalAlign.Left, (int)TextHorizontalAlign.Right);
        int alignYValue = Math.Clamp(textView.AlignY, (int)TextVerticalAlign.Top, (int)TextVerticalAlign.Bottom);

        Im.PushClipRect(contentRect);

        var flags =
            ImTextArea.ImTextAreaFlags.NoBackground |
            ImTextArea.ImTextAreaFlags.NoBorder |
            ImTextArea.ImTextAreaFlags.NoRounding |
            ImTextArea.ImTextAreaFlags.NoText |
            ImTextArea.ImTextAreaFlags.NoCaret |
            ImTextArea.ImTextAreaFlags.NoSelection;
        if (!multiline)
        {
            flags |= ImTextArea.ImTextAreaFlags.SingleLine;
        }

        ctx.PushId((int)_inlineTextEditStableId);
        int widgetId = ctx.GetId(CanvasInlineTextEditorId);
        if (_inlineTextEditWantsFocus)
        {
            ImTextArea.ClearState(widgetId);
            ctx.RequestFocus(widgetId);
            ctx.SetActive(widgetId);
            ctx.ResetCaretBlink();
            _inlineTextEditWantsFocus = false;
        }

        ref var style = ref Im.Style;
        float savedPadding = style.Padding;
        style.Padding = 0f;

        bool changed = ImTextArea.DrawAt(
            CanvasInlineTextEditorId,
            _inlineTextEditBuffer,
            rectCanvas.X,
            rectCanvas.Y,
            rectCanvas.Width,
            rectCanvas.Height,
            wordWrap: wordWrap,
            fontSizePx: resolvedFontSizeCanvasPx,
            flags: flags,
            lineHeightPx: lineHeightCanvasPx,
            letterSpacingPx: letterSpacingCanvasPx,
            alignX: alignXValue,
            alignY: alignYValue);

        style.Padding = savedPadding;
        ctx.PopId();

        Im.PopClipRect();

        if (changed)
        {
            ApplyInlineTextEditBufferToComponent(widgetId, textAny);
        }
    }

    internal void DrawCanvasInlineTextEditorOverlay(ImRect contentRect, Vector2 canvasOrigin)
    {
        if (!_inlineTextEditActive || _inlineTextEditEntity.IsNull)
        {
            return;
        }

        if (_world.GetNodeType(_inlineTextEditEntity) != UiNodeType.Text)
        {
            CancelCanvasInlineTextEdit();
            return;
        }

        if (IsEntityHiddenInEditor(_inlineTextEditEntity) || IsEntityLockedInEditor(_inlineTextEditEntity))
        {
            CancelCanvasInlineTextEdit();
            return;
        }

        if (!TryGetCanvasInlineTextEditorParams(
                canvasOrigin,
                out ImRect rectCanvas,
                out _,
                out float fontSizeCanvasPx,
                out float lineHeightScale,
                out float letterSpacingCanvasPx,
                out _,
                out AnyComponentHandle textAny))
        {
            CancelCanvasInlineTextEdit();
            return;
        }

        int widgetId = GetCanvasInlineTextEditorWidgetId();
        if (!ImTextArea.TryGetState(widgetId, out int caretPos, out int selectionStart, out int selectionEnd))
        {
            return;
        }

        var ctx = Im.Context;
        Font font = ctx.Font!;

        var textHandle = new TextComponentHandle(textAny.Index, textAny.Generation);
        var textView = TextComponent.Api.FromHandle(_propertyWorld, textHandle);
        if (!textView.IsAlive)
        {
            return;
        }

        float resolvedFontSizeCanvasPx = fontSizeCanvasPx;
        TextLayoutCache layout = GetOrCreateTextLayoutCache(textAny);
        layout.Ensure(
            font,
            textView.Text,
            textView.Font,
            fontSizeCanvasPx,
            lineHeightScale,
            letterSpacingCanvasPx,
            textView.Multiline,
            textView.Wrap,
            textView.Overflow,
            textView.AlignX,
            textView.AlignY,
            new Vector2(rectCanvas.Width, rectCanvas.Height));
        resolvedFontSizeCanvasPx = layout.ResolvedFontSizePx;

        float scaleFont = resolvedFontSizeCanvasPx / font.BaseSizePixels;
        float clampedLineHeightScale = Math.Clamp(lineHeightScale, 0.25f, 8f);
        float lineHeightCanvasPx = font.LineHeightPixels * scaleFont * clampedLineHeightScale;

        ReadOnlySpan<TextLayoutCache.Line> lines = layout.Lines;
        int lineCount = lines.Length > 0 ? lines.Length : 1;

        float totalHeight = lineCount * lineHeightCanvasPx;
        float yOffset = 0f;
        int alignYValue = Math.Clamp(textView.AlignY, (int)TextVerticalAlign.Top, (int)TextVerticalAlign.Bottom);
        if (alignYValue == (int)TextVerticalAlign.Middle)
        {
            yOffset = (rectCanvas.Height - totalHeight) * 0.5f;
        }
        else if (alignYValue == (int)TextVerticalAlign.Bottom)
        {
            yOffset = rectCanvas.Height - totalHeight;
        }
        if (yOffset < 0f)
        {
            yOffset = 0f;
        }

        int visibleLineCount = lineCount;
        if (textView.Multiline)
        {
            int maxLines = lineHeightCanvasPx <= 0.0001f ? lineCount : (int)MathF.Floor(rectCanvas.Height / lineHeightCanvasPx);
            if (maxLines < 0)
            {
                maxLines = 0;
            }
            if (maxLines < visibleLineCount)
            {
                visibleLineCount = maxLines;
            }
        }
        else
        {
            visibleLineCount = Math.Min(1, visibleLineCount);
        }

        int caretLineIndex = 0;
        if (lines.Length > 0)
        {
            int textLen = ((string)textView.Text).Length;
            int caretPosClamped = Math.Clamp(caretPos, 0, textLen);
            for (int i = 0; i < lines.Length; i++)
            {
                ref readonly TextLayoutCache.Line line = ref lines[i];
                if (caretPosClamped >= line.CharStart && caretPosClamped <= line.CharEnd)
                {
                    caretLineIndex = i;
                    break;
                }
                if (caretPosClamped > line.CharEnd)
                {
                    caretLineIndex = i;
                }
            }
        }

        if (caretLineIndex >= visibleLineCount)
        {
            caretLineIndex = Math.Max(0, visibleLineCount - 1);
        }

        Im.PushClipRect(contentRect);
        Im.PushClipRect(rectCanvas);

        uint caretColor = Im.Style.TextPrimary;
        uint selectionColor = ImStyle.WithAlpha(Im.Style.Primary, 90);

        ReadOnlySpan<char> fullText = textView.Text.IsValid ? ((string)textView.Text).AsSpan() : ReadOnlySpan<char>.Empty;
        int fullLength = fullText.Length;
        int selA = Math.Clamp(selectionStart, 0, fullLength);
        int selB = Math.Clamp(selectionEnd, 0, fullLength);
        int selStart = Math.Min(selA, selB);
        int selEnd = Math.Max(selA, selB);

        int alignXValue = Math.Clamp(textView.AlignX, (int)TextHorizontalAlign.Left, (int)TextHorizontalAlign.Right);

        for (int lineIndex = 0; lineIndex < visibleLineCount; lineIndex++)
        {
            float xOffset = 0f;
            int lineCharStart = 0;
            int lineCharEnd = 0;
            float lineWidthPx = 0f;

            if (lines.Length > 0)
            {
                ref readonly TextLayoutCache.Line line = ref lines[lineIndex];
                lineCharStart = line.CharStart;
                lineCharEnd = line.CharEnd;
                lineWidthPx = line.WidthPx;
            }
            else
            {
                lineCharStart = 0;
                lineCharEnd = fullLength;
                lineWidthPx = 0f;
            }

            if (alignXValue == (int)TextHorizontalAlign.Center)
            {
                xOffset = (rectCanvas.Width - lineWidthPx) * 0.5f;
            }
            else if (alignXValue == (int)TextHorizontalAlign.Right)
            {
                xOffset = rectCanvas.Width - lineWidthPx;
            }
            if (xOffset < 0f)
            {
                xOffset = 0f;
            }

            float lineTop = rectCanvas.Y + yOffset + lineIndex * lineHeightCanvasPx;

            if (selEnd > selStart)
            {
                int overlapStart = Math.Max(selStart, lineCharStart);
                int overlapEnd = Math.Min(selEnd, lineCharEnd);
                if (overlapEnd > overlapStart)
                {
                    float startX = MeasureTextAdvance(font, fullText.Slice(lineCharStart, overlapStart - lineCharStart), resolvedFontSizeCanvasPx, letterSpacingCanvasPx);
                    float endX = MeasureTextAdvance(font, fullText.Slice(lineCharStart, overlapEnd - lineCharStart), resolvedFontSizeCanvasPx, letterSpacingCanvasPx);
                    Im.DrawRect(rectCanvas.X + xOffset + startX, lineTop, Math.Max(0f, endX - startX), lineHeightCanvasPx, selectionColor);
                }
            }

            if (lineIndex == caretLineIndex && ctx.CaretVisible)
            {
                int caretPosClamped = Math.Clamp(caretPos, 0, fullLength);
                int caretInLine = Math.Clamp(caretPosClamped, lineCharStart, lineCharEnd);
                float caretX = MeasureTextAdvance(font, fullText.Slice(lineCharStart, caretInLine - lineCharStart), resolvedFontSizeCanvasPx, letterSpacingCanvasPx);
                Im.DrawRect(rectCanvas.X + xOffset + caretX, lineTop, 1f, lineHeightCanvasPx, caretColor);
            }
        }

        Im.PopClipRect();
        Im.PopClipRect();
    }

    private void CancelCanvasInlineTextEdit()
    {
        EndCanvasInlineTextEdit(commit: false);
    }

    private void CommitCanvasInlineTextEdit()
    {
        EndCanvasInlineTextEdit(commit: true);
    }

    private void EndCanvasInlineTextEdit(bool commit)
    {
        if (!_inlineTextEditActive)
        {
            return;
        }

        EntityId entity = _inlineTextEditEntity;
        uint stableId = _inlineTextEditStableId;
        StringHandle originalTextHandle = _inlineTextEditOriginalText;

        _inlineTextEditActive = false;
        _inlineTextEditEntity = EntityId.Null;
        _inlineTextEditStableId = 0;
        _inlineTextEditStartFrame = 0;
        _inlineTextEditWantsFocus = false;
        _inlineTextEditOriginalText = StringHandle.Invalid;

        int widgetId = GetCanvasInlineTextEditorWidgetId(stableId);
        ImTextArea.ClearState(widgetId);

        if (entity.IsNull)
        {
            return;
        }

        if (!commit && originalTextHandle.IsValid &&
            _world.TryGetComponent(entity, TextComponent.Api.PoolIdConst, out AnyComponentHandle textAny))
        {
            var componentHandle = new TextComponentHandle(textAny.Index, textAny.Generation);
            AnyComponentHandle component = TextComponentProperties.ToAnyHandle(componentHandle);
            PropertySlot slot = PropertyDispatcher.GetSlot(component, 0);
            Commands.SetPropertyValue(widgetId, isEditing: true, entity, slot, PropertyValue.FromStringHandle(originalTextHandle));
        }

        // Always finalize the pending text-edit record (if any).
        Commands.NotifyPropertyWidgetState(widgetId, isEditing: false);
    }

    private int GetCanvasInlineTextEditorWidgetId()
    {
        return GetCanvasInlineTextEditorWidgetId(_inlineTextEditStableId);
    }

    private static int GetCanvasInlineTextEditorWidgetId(uint stableId)
    {
        var ctx = Im.Context;
        ctx.PushId((int)stableId);
        int widgetId = ctx.GetId(CanvasInlineTextEditorId);
        ctx.PopId();
        return widgetId;
    }

    private bool TryGetCanvasInlineTextEditorParams(
        Vector2 canvasOrigin,
        out ImRect rectCanvas,
        out bool multiline,
        out float fontSizeCanvasPx,
        out float lineHeightScale,
        out float letterSpacingCanvasPx,
        out bool wordWrap,
        out AnyComponentHandle textAny)
    {
        rectCanvas = ImRect.Zero;
        multiline = false;
        fontSizeCanvasPx = 0f;
        lineHeightScale = 1f;
        letterSpacingCanvasPx = 0f;
        wordWrap = false;
        textAny = default;

        if (_inlineTextEditEntity.IsNull)
        {
            return false;
        }

        if (!TryGetTransformParentWorldTransformEcs(_inlineTextEditEntity, out WorldTransform parentWorldTransform))
        {
            parentWorldTransform = IdentityWorldTransform;
        }

        if (!TryGetTextWorldTransformEcs(_inlineTextEditEntity, parentWorldTransform, out ShapeWorldTransform worldTransform))
        {
            return false;
        }

        if (!_world.TryGetComponent(_inlineTextEditEntity, RectGeometryComponent.Api.PoolIdConst, out AnyComponentHandle rectAny))
        {
            return false;
        }

        if (!_world.TryGetComponent(_inlineTextEditEntity, TextComponent.Api.PoolIdConst, out textAny))
        {
            return false;
        }

        var rectGeometry = RectGeometryComponent.Api.FromHandle(_propertyWorld, new RectGeometryComponentHandle(rectAny.Index, rectAny.Generation));
        if (!rectGeometry.IsAlive)
        {
            return false;
        }

        var textView = TextComponent.Api.FromHandle(_propertyWorld, new TextComponentHandle(textAny.Index, textAny.Generation));
        if (!textView.IsAlive)
        {
            return false;
        }

        float scaleX = worldTransform.ScaleWorld.X == 0f ? 1f : worldTransform.ScaleWorld.X;
        float scaleY = worldTransform.ScaleWorld.Y == 0f ? 1f : worldTransform.ScaleWorld.Y;
        float absScaleX = MathF.Abs(scaleX);
        float absScaleY = MathF.Abs(scaleY);

        float widthCanvas = rectGeometry.Size.X * absScaleX * Zoom;
        float heightCanvas = rectGeometry.Size.Y * absScaleY * Zoom;

        float halfWidthCanvas = widthCanvas * 0.5f;
        float halfHeightCanvas = heightCanvas * 0.5f;

        Vector2 anchor = worldTransform.Anchor;
        Vector2 anchorOffset = new Vector2(
            (anchor.X - 0.5f) * (rectGeometry.Size.X * absScaleX),
            (anchor.Y - 0.5f) * (rectGeometry.Size.Y * absScaleY));

        Vector2 centerWorld = worldTransform.PositionWorld - RotateVector(anchorOffset, worldTransform.RotationRadians);
        Vector2 centerCanvas = new Vector2(
            WorldToCanvasX(centerWorld.X, canvasOrigin),
            WorldToCanvasY(centerWorld.Y, canvasOrigin));

        rectCanvas = new ImRect(centerCanvas.X - halfWidthCanvas, centerCanvas.Y - halfHeightCanvas, widthCanvas, heightCanvas);

        float styleScale = (absScaleX + absScaleY) * 0.5f;
        fontSizeCanvasPx = Math.Max(1f, textView.FontSizePx) * Zoom * styleScale;
        lineHeightScale = textView.LineHeightScale;
        letterSpacingCanvasPx = textView.LetterSpacingPx * Zoom * styleScale;
        multiline = textView.Multiline;
        wordWrap = multiline && textView.Wrap;

        return rectCanvas.Width > 0.0001f && rectCanvas.Height > 0.0001f && fontSizeCanvasPx > 0.0001f;
    }

    private void ApplyInlineTextEditBufferToComponent(int widgetId, AnyComponentHandle textAny)
    {
        ReadOnlySpan<char> span = _inlineTextEditBuffer.AsSpan();
        string newText = span.Length > 0 ? new string(span) : string.Empty;
        StringHandle newHandle = newText;

        var componentHandle = new TextComponentHandle(textAny.Index, textAny.Generation);
        AnyComponentHandle component = TextComponentProperties.ToAnyHandle(componentHandle);
        PropertySlot slot = PropertyDispatcher.GetSlot(component, 0);

        Commands.SetPropertyValue(widgetId, isEditing: true, _inlineTextEditEntity, slot, PropertyValue.FromStringHandle(newHandle));
    }

    private static float MeasureTextAdvance(Font font, ReadOnlySpan<char> text, float fontSizePx, float letterSpacingPx)
    {
        if (text.Length <= 0)
        {
            return 0f;
        }

        float scale = fontSizePx / font.BaseSizePixels;
        float width = 0f;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                continue;
            }
            if (font.TryGetGlyph(c, out FontGlyph glyph))
            {
                width += glyph.AdvanceX * scale + letterSpacingPx;
                continue;
            }

            if (font.TryGetGlyph(' ', out FontGlyph spaceGlyph))
            {
                width += spaceGlyph.AdvanceX * scale + letterSpacingPx;
            }
        }

        return width;
    }
}
