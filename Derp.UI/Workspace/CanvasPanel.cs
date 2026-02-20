using System.Numerics;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Windows;
using DerpLib.ImGui.Widgets;

namespace Derp.UI;

internal static class CanvasPanel
{
    private const int DefaultNewPrefabWidth = 500;
    private const int DefaultNewPrefabHeight = 500;
    private const int MinNewPrefabSize = 1;
    private const int MaxNewPrefabSize = 8192;

    private static int _newPrefabWidth = DefaultNewPrefabWidth;
    private static int _newPrefabHeight = DefaultNewPrefabHeight;

    public static void DrawCanvas(UiWorkspace workspace)
    {
        var style = Im.Style;
        var input = Im.Context.Input;
        var canvasInteractions = workspace.CanvasInteractions;
        bool inlineTextEditing = workspace.IsCanvasInlineTextEditing;

        workspace.EnsureHierarchyTransformsAreLocal();

        var contentRect = Im.WindowContentRect;
        var mouseCanvas = Im.MousePos;
        bool canvasHovered = contentRect.Contains(mouseCanvas);
        bool hasAnyPrefabs = workspace._prefabs.Count > 0;

        var canvasOrigin = new Vector2(
            contentRect.X + contentRect.Width * 0.5f,
            contentRect.Y + contentRect.Height * 0.5f);

        workspace._hasCanvasCompositeThisFrame = true;
        workspace._canvasCompositeRectViewport = contentRect.Offset(Im.CurrentTranslation);

        var viewport = Im.CurrentViewport;
        bool popoverCapturesMouse = viewport != null && workspace.InspectorPopoversCaptureMouse(viewport);

        if (hasAnyPrefabs && canvasHovered && !popoverCapturesMouse && !inlineTextEditing)
        {
            canvasInteractions.HandleZoom(input, canvasOrigin, mouseCanvas);
        }

        if (hasAnyPrefabs && !popoverCapturesMouse)
        {
            if (!inlineTextEditing)
            {
                canvasInteractions.HandlePan(input, canvasHovered);
            }
            workspace.HandleCanvasInteractions(input, canvasHovered, canvasOrigin, mouseCanvas);
        }

        if (hasAnyPrefabs)
        {
            workspace.UpdateCanvasInlineTextEditor(contentRect, canvasOrigin);
        }

        workspace.BuildCanvasSurface(contentRect);
        if (hasAnyPrefabs)
        {
            workspace.DrawFramesOverlay(canvasOrigin);
        }

        if (hasAnyPrefabs && canvasInteractions.IsCreatingFrame)
        {
            workspace.DrawFramePreview(canvasOrigin, style);
        }

        if (hasAnyPrefabs && canvasInteractions.IsCreatingShape)
        {
            workspace.DrawShapePreview(canvasOrigin, style);
        }

        if (hasAnyPrefabs && canvasInteractions.IsCreatingText)
        {
            workspace.DrawTextPreview(canvasOrigin, style);
        }

        if (hasAnyPrefabs && canvasInteractions.IsCreatingPolygon)
        {
            canvasInteractions.DrawPolygonPreview(canvasOrigin, workspace.CanvasToWorld(mouseCanvas, canvasOrigin));
        }

        if (hasAnyPrefabs)
        {
            workspace.DrawCanvasInlineTextEditorOverlay(contentRect, canvasOrigin);
            canvasInteractions.DrawCanvasHud(canvasOrigin);
        }
        else
        {
            DrawEmptyPrefabState(workspace, contentRect);
        }
    }

    private static void DrawEmptyPrefabState(UiWorkspace workspace, ImRect contentRect)
    {
        var style = Im.Style;

        float panelWidth = MathF.Min(440f, MathF.Max(300f, contentRect.Width - 80f));
        float panelPadding = 26f;
        float panelSpacing = 16f;
        float rowHeight = MathF.Max(style.MinButtonHeight, style.FontSize + style.Padding * 2f);
        float messageFontSize = style.FontSize * 1.8f;
        float messageHeight = messageFontSize;
        float panelHeight = panelPadding * 2f + messageHeight + panelSpacing * 1.25f + rowHeight + panelSpacing + style.MinButtonHeight;

        float panelX = contentRect.X + (contentRect.Width - panelWidth) * 0.5f;
        float panelY = contentRect.Y + (contentRect.Height - panelHeight) * 0.5f;
        var panelRect = new ImRect(panelX, panelY, panelWidth, panelHeight);
        var panelInner = panelRect.Pad(panelPadding);

        Im.DrawRoundedRect(panelRect.X, panelRect.Y, panelRect.Width, panelRect.Height, style.CornerRadius, style.Surface);
        Im.DrawRoundedRectStroke(panelRect.X, panelRect.Y, panelRect.Width, panelRect.Height, style.CornerRadius, style.Border, style.BorderWidth);

        ReadOnlySpan<char> message = "Create a prefab to get started".AsSpan();
        float messageWidth = Im.MeasureTextWidth(message, messageFontSize);
        float messageX = panelInner.X + (panelInner.Width - messageWidth) * 0.5f;
        float messageY = panelInner.Y;
        Im.Text(message, messageX, messageY, messageFontSize, style.TextSecondary);

        float inputsY = messageY + messageHeight + panelSpacing * 1.25f;

        Im.Context.PushId(0x4E505245); // "NPRE"
        _newPrefabWidth = Math.Clamp(_newPrefabWidth, MinNewPrefabSize, MaxNewPrefabSize);
        _newPrefabHeight = Math.Clamp(_newPrefabHeight, MinNewPrefabSize, MaxNewPrefabSize);

        var size = new Vector2(_newPrefabWidth, _newPrefabHeight);
        const string sizeWidgetId = "prefab_size";
        if (ImVectorInput.DrawAt(sizeWidgetId, panelInner.X, inputsY, panelInner.Width, ref size, MinNewPrefabSize, MaxNewPrefabSize, "F0"))
        {
            _newPrefabWidth = Math.Clamp((int)MathF.Round(size.X), MinNewPrefabSize, MaxNewPrefabSize);
            _newPrefabHeight = Math.Clamp((int)MathF.Round(size.Y), MinNewPrefabSize, MaxNewPrefabSize);
        }

        // Replace default X/Y component labels with W/H for this screen.
        int parentId = Im.Context.GetId(sizeWidgetId);
        DrawVectorInputComponentLabel(panelInner.X, inputsY, panelInner.Width, parentId, componentIndex: 0, "W".AsSpan());
        DrawVectorInputComponentLabel(panelInner.X, inputsY, panelInner.Width, parentId, componentIndex: 1, "H".AsSpan());

        float buttonY = inputsY + rowHeight + panelSpacing;
        if (Im.Button("Create Prefab", panelInner.X, buttonY, panelInner.Width, style.MinButtonHeight))
        {
            CreateDefaultPrefab(workspace, _newPrefabWidth, _newPrefabHeight);
        }

        Im.Context.PopId();
    }

    private static void CreateDefaultPrefab(UiWorkspace workspace, int width, int height)
    {
        width = Math.Clamp(width, MinNewPrefabSize, MaxNewPrefabSize);
        height = Math.Clamp(height, MinNewPrefabSize, MaxNewPrefabSize);

        Vector2 centerWorld = workspace.PanWorld;
        float widthWorld = width;
        float heightWorld = height;
        var rectWorld = new ImRect(centerWorld.X - widthWorld * 0.5f, centerWorld.Y - heightWorld * 0.5f, widthWorld, heightWorld);

        int prefabId = workspace.Commands.CreatePrefab(rectWorld);
        if (UiWorkspace.TryGetEntityById(workspace._prefabEntityById, prefabId, out EntityId prefabEntity) && !prefabEntity.IsNull)
        {
            workspace.SelectPrefabEntity(prefabEntity);
            if (workspace.TryGetPrefabIndexById(prefabId, out int prefabIndex))
            {
                workspace._selectedPrefabIndex = prefabIndex;
            }
        }

        workspace.ActiveTool = UiWorkspace.CanvasTool.Select;
    }

    private static void DrawVectorInputComponentLabel(float x, float y, float totalWidth, int parentId, int componentIndex, ReadOnlySpan<char> label)
    {
        var style = Im.Style;
        var ctx = Im.Context;
        float height = style.MinButtonHeight;
        float labelWidth = 18f;

        float spacing = style.Spacing;
        float componentWidth = (totalWidth - spacing) * 0.5f;
        float componentX = componentIndex == 0 ? x : (x + componentWidth + spacing);

        int widgetId = parentId * 31 + componentIndex;
        bool isFocused = ctx.IsFocused(widgetId);
        var componentRect = new ImRect(componentX, y, componentWidth, height);
        bool hovered = componentRect.Contains(Im.MousePos);

        uint background = style.Surface;
        if (hovered && !isFocused)
        {
            background = ImStyle.Lerp(style.Surface, 0xFF000000, 0.16f);
        }

        var labelRect = new ImRect(componentX, y, labelWidth, height);

        // Cover the existing label text (X/Y) without affecting borders.
        Im.DrawRect(labelRect.X + 1f, labelRect.Y + 1f, MathF.Max(0f, labelRect.Width - 2f), MathF.Max(0f, labelRect.Height - 2f), background);

        float labelTextWidth = Im.MeasureTextWidth(label, style.FontSize);
        float labelTextX = labelRect.X + (labelRect.Width - labelTextWidth) * 0.5f;
        float labelTextY = labelRect.Y + (labelRect.Height - style.FontSize) * 0.5f;
        Im.Text(label, labelTextX, labelTextY, style.FontSize, style.TextSecondary);
    }
}
