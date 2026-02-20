using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Rendering;
using DerpLib.ImGui.Widgets;
using Derp.Doc.Commands;
using Derp.Doc.Editor;
using Derp.Doc.Model;

namespace Derp.Doc.Panels;

/// <summary>
/// Floating toolbar that appears when text is selected in a focused block.
/// Provides inline style buttons and heading block-type buttons.
/// </summary>
internal static class SelectionToolbar
{
    private const float ButtonWidth = 28f;
    private const float ButtonStride = 32f;
    private const float RowPadding = 4f;
    private const float RowHeight = 24f;
    private const float RowGap = 4f;
    private const float SelectionGap = 8f;
    private const float ViewportEdgePadding = 4f;
    private const float SelectionAvoidPadding = 6f;

    private static readonly string[] InlineStyleButtonLabels = ["B", "I", "S", "<>", "Hi"];
    private static readonly RichSpanStyle[] InlineStyleButtonStyles =
    [
        RichSpanStyle.Bold,
        RichSpanStyle.Italic,
        RichSpanStyle.Strikethrough,
        RichSpanStyle.Code,
        RichSpanStyle.Highlight,
    ];
    private static readonly string[] HeadingButtonLabels = ["H1", "H2", "H3", "H4", "H5", "H6"];
    private static readonly DocBlockType[] HeadingButtonTypes =
    [
        DocBlockType.Heading1,
        DocBlockType.Heading2,
        DocBlockType.Heading3,
        DocBlockType.Heading4,
        DocBlockType.Heading5,
        DocBlockType.Heading6,
    ];

    /// <summary>Whether the toolbar was visible on the last Draw call.</summary>
    public static bool IsVisible { get; private set; }

    /// <summary>The toolbar rect from the last Draw call (screen coordinates).</summary>
    public static ImRect ToolbarRect { get; private set; }

    public static bool IsMouseOverToolbar(DocWorkspace workspace, DocDocument document, System.Numerics.Vector2 mousePos)
    {
        if (!TryGetToolbarRect(workspace, document, out var toolbarRect))
        {
            return false;
        }

        return toolbarRect.Contains(mousePos);
    }

    public static bool TryGetToolbarRect(DocWorkspace workspace, DocDocument document, out ImRect toolbarRect)
    {
        toolbarRect = default;
        if (workspace.FocusedBlockIndex < 0 || workspace.FocusedBlockIndex >= document.Blocks.Count)
        {
            return false;
        }

        var focusedBlock = document.Blocks[workspace.FocusedBlockIndex];
        if (!DocumentRenderer.GetSelection(focusedBlock, out int selectionStart, out int selectionEnd))
        {
            return false;
        }

        int selectionLength = selectionEnd - selectionStart;
        if (selectionLength <= 0)
        {
            return false;
        }

        toolbarRect = ComputeToolbarPlacement(workspace, document);
        return true;
    }

    public static void Draw(DocWorkspace workspace, DocDocument document)
    {
        if (workspace.FocusedBlockIndex < 0 || workspace.FocusedBlockIndex >= document.Blocks.Count)
        {
            IsVisible = false;
            return;
        }

        var block = document.Blocks[workspace.FocusedBlockIndex];
        if (!DocumentRenderer.GetSelection(block, out int selStart, out int selEnd))
        {
            IsVisible = false;
            return;
        }

        int selLength = selEnd - selStart;
        if (selLength <= 0)
        {
            IsVisible = false;
            return;
        }

        if (!TryGetToolbarRect(workspace, document, out var toolbarPlacement))
        {
            IsVisible = false;
            return;
        }

        // Update visibility tracking (used by DocumentRenderer to suppress clicks)
        ToolbarRect = toolbarPlacement;
        IsVisible = true;

        using var overlayScope = ImPopover.PushOverlayScopeLocal(ToolbarRect);

        // Draw on overlay layer
        Im.SetDrawLayer(ImDrawLayer.Overlay);
        var style = Im.Style;

        // Background
        Im.DrawRoundedRect(ToolbarRect.X, ToolbarRect.Y, ToolbarRect.Width, ToolbarRect.Height, 4f, style.Surface);
        Im.DrawRoundedRectStroke(ToolbarRect.X, ToolbarRect.Y, ToolbarRect.Width, ToolbarRect.Height, 4f, style.Border, 1f);

        Im.Context.PushId("selection_toolbar");

        // Inline style buttons
        float inlineRowY = ToolbarRect.Y + RowPadding;
        float btnX = ToolbarRect.X + 4f;
        for (int buttonIndex = 0; buttonIndex < InlineStyleButtonLabels.Length; buttonIndex++)
        {
            Im.Context.PushId(buttonIndex);
            bool active = block.Text.HasStyleInRange(selStart, selLength, InlineStyleButtonStyles[buttonIndex]);
            uint buttonColor = active ? style.Active : style.Surface;
            Im.DrawRoundedRect(btnX, inlineRowY, ButtonWidth, RowHeight, 4f, buttonColor);
            Im.DrawRoundedRectStroke(btnX, inlineRowY, ButtonWidth, RowHeight, 4f, style.Border, 1f);

            if (Im.Button(InlineStyleButtonLabels[buttonIndex], btnX, inlineRowY, ButtonWidth, RowHeight))
            {
                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.ToggleSpan,
                    DocumentId = document.Id,
                    BlockId = block.Id,
                    SpanStart = selStart,
                    SpanLength = selLength,
                    SpanStyle = InlineStyleButtonStyles[buttonIndex],
                });
            }
            Im.Context.PopId();

            btnX += ButtonStride;
        }

        // Heading block type buttons
        float headingRowY = inlineRowY + RowHeight + RowGap;
        btnX = ToolbarRect.X + 4f;
        for (int buttonIndex = 0; buttonIndex < HeadingButtonLabels.Length; buttonIndex++)
        {
            Im.Context.PushId(1000 + buttonIndex);
            bool isActiveHeading = block.Type == HeadingButtonTypes[buttonIndex];
            uint buttonColor = isActiveHeading ? style.Active : style.Surface;
            Im.DrawRoundedRect(btnX, headingRowY, ButtonWidth, RowHeight, 4f, buttonColor);
            Im.DrawRoundedRectStroke(btnX, headingRowY, ButtonWidth, RowHeight, 4f, style.Border, 1f);

            if (Im.Button(HeadingButtonLabels[buttonIndex], btnX, headingRowY, ButtonWidth, RowHeight))
            {
                DocBlockType newBlockType = HeadingButtonTypes[buttonIndex];
                if (block.Type != newBlockType)
                {
                    workspace.ExecuteCommand(new DocCommand
                    {
                        Kind = DocCommandKind.ChangeBlockType,
                        DocumentId = document.Id,
                        BlockId = block.Id,
                        OldBlockType = block.Type,
                        NewBlockType = newBlockType,
                    });
                }
            }
            Im.Context.PopId();

            btnX += ButtonStride;
        }

        Im.Context.PopId();
        Im.SetDrawLayer(ImDrawLayer.WindowContent);
    }

    private static ImRect ComputeToolbarPlacement(DocWorkspace workspace, DocDocument document)
    {
        int maxColumns = Math.Max(InlineStyleButtonLabels.Length, HeadingButtonLabels.Length);
        float toolbarW = maxColumns * ButtonStride + 8f;
        float toolbarH = RowPadding + RowHeight + RowGap + RowHeight + RowPadding;

        var contentRect = DocumentRenderer.GetDocumentContentRect();
        if (contentRect.Width <= 0f || contentRect.Height <= 0f)
        {
            contentRect = Im.WindowContentRect;
        }

        ImRect selectionRect;
        if (!DocumentRenderer.TryGetSelectionScreenBounds(workspace, document, out selectionRect))
        {
            float blockY = DocumentRenderer.GetBlockScreenY(workspace.FocusedBlockIndex);
            float blockH = DocumentRenderer.GetBlockHeight(workspace.FocusedBlockIndex);
            selectionRect = new ImRect(contentRect.X, blockY, contentRect.Width, blockH);
        }

        float centeredX = selectionRect.X + (selectionRect.Width - toolbarW) * 0.5f;
        float centeredY = selectionRect.Y + (selectionRect.Height - toolbarH) * 0.5f;
        var avoidRect = new ImRect(
            selectionRect.X - SelectionAvoidPadding,
            selectionRect.Y - SelectionAvoidPadding,
            selectionRect.Width + (SelectionAvoidPadding * 2f),
            selectionRect.Height + (SelectionAvoidPadding * 2f));
        var placementBounds = new ImRect(
            contentRect.X + ViewportEdgePadding,
            contentRect.Y + ViewportEdgePadding,
            Math.Max(1f, contentRect.Width - ViewportEdgePadding * 2f),
            Math.Max(1f, contentRect.Height - ViewportEdgePadding * 2f));
        Span<ImRect> candidates =
        [
            new ImRect(centeredX, selectionRect.Y - toolbarH - SelectionGap, toolbarW, toolbarH),
            new ImRect(centeredX, selectionRect.Bottom + SelectionGap, toolbarW, toolbarH),
            new ImRect(selectionRect.Right + SelectionGap, centeredY, toolbarW, toolbarH),
            new ImRect(selectionRect.X - toolbarW - SelectionGap, centeredY, toolbarW, toolbarH),
        ];

        ImRect toolbarPlacement = ClampRectToBounds(candidates[0], placementBounds);
        for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
        {
            var candidateRect = ClampRectToBounds(candidates[candidateIndex], placementBounds);
            if (!RectsOverlap(candidateRect, avoidRect))
            {
                toolbarPlacement = candidateRect;
                return toolbarPlacement;
            }
        }

        return ClampRectToBounds(candidates[1], placementBounds);
    }

    private static ImRect ClampRectToBounds(ImRect rect, ImRect bounds)
    {
        float maxX = bounds.Right - rect.Width;
        float maxY = bounds.Bottom - rect.Height;
        float clampedX = Math.Clamp(rect.X, bounds.X, Math.Max(bounds.X, maxX));
        float clampedY = Math.Clamp(rect.Y, bounds.Y, Math.Max(bounds.Y, maxY));
        return new ImRect(clampedX, clampedY, rect.Width, rect.Height);
    }

    private static bool RectsOverlap(ImRect a, ImRect b)
    {
        return a.X < b.Right &&
               a.Right > b.X &&
               a.Y < b.Bottom &&
               a.Bottom > b.Y;
    }
}
