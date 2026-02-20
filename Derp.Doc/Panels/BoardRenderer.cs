using System.Numerics;
using System.Collections.Generic;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using Derp.Doc.Commands;
using Derp.Doc.Editor;
using Derp.Doc.Model;

namespace Derp.Doc.Panels;

/// <summary>
/// Kanban board renderer — groups rows by a Select column into horizontal lanes.
/// </summary>
internal static class BoardRenderer
{
    private const float LaneWidth = 260f;
    private const float LaneGap = 8f;
    private const float LaneHeaderHeight = 32f;
    private const float CardHeight = 80f;
    private const float CardGap = 6f;
    private const float CardPaddingX = 10f;
    private const float CardPaddingY = 8f;
    private const float CardCornerRadius = 6f;
    private const float LanePaddingX = 6f;
    private const float DragThreshold = 4f;
    private const float AccentStripeWidth = 4f;

    // Drag state
    private static bool _isDragging;
    private static int _dragSourceRowIndex = -1;
    private static int _dragSourceLaneIndex = -1;
    private static int _dragTargetLaneIndex = -1;
    private static int _dragTargetCardIndex = -1;
    private static Vector2 _dragStartMousePos;
    private static bool _dragPastThreshold;

    // Scroll state per lane (max 16 lanes) + horizontal scroll
    private static readonly float[] _laneScrollY = new float[16];
    private static float _scrollX;

    private sealed class BoardViewState
    {
        public float ScrollX;
        public readonly float[] LaneScrollY = new float[16];
    }

    private static readonly Dictionary<string, BoardViewState> ViewStatesByStateKey = new(StringComparer.Ordinal);
    private static string _activeStateKey = "";

    // Lane colors
    private static readonly uint[] LaneColors =
    [
        ImStyle.WithAlpha(0xFF4488FF, 200), // blue
        ImStyle.WithAlpha(0xFF44BB66, 200), // green
        ImStyle.WithAlpha(0xFFFF8844, 200), // orange
        ImStyle.WithAlpha(0xFFBB44BB, 200), // purple
        ImStyle.WithAlpha(0xFFFF4466, 200), // red
        ImStyle.WithAlpha(0xFF44CCCC, 200), // teal
        ImStyle.WithAlpha(0xFFCCBB44, 200), // yellow
        ImStyle.WithAlpha(0xFF8866DD, 200), // violet
    ];

    public static void Draw(DocWorkspace workspace, ImRect contentRect)
    {
        var table = workspace.ActiveTable;
        var view = workspace.ActiveTableView;
        DrawInternal(
            workspace,
            table,
            view,
            contentRect,
            interactive: true,
            parentRowColumnId: null,
            parentRowId: null,
            tableInstanceBlock: null,
            stateKey: "");
    }

    public static void Draw(DocWorkspace workspace, DocTable table, ImRect contentRect)
    {
        var view = table.Views.Count > 0 ? table.Views[0] : null;
        DrawInternal(
            workspace,
            table,
            view,
            contentRect,
            interactive: true,
            parentRowColumnId: null,
            parentRowId: null,
            tableInstanceBlock: null,
            stateKey: "");
    }

    public static void Draw(
        DocWorkspace workspace,
        DocTable table,
        DocView? view,
        ImRect contentRect,
        DocBlock? tableInstanceBlock = null)
    {
        DrawInternal(
            workspace,
            table,
            view,
            contentRect,
            interactive: true,
            parentRowColumnId: null,
            parentRowId: null,
            tableInstanceBlock: tableInstanceBlock,
            stateKey: "");
    }

    public static void Draw(
        DocWorkspace workspace,
        DocTable table,
        DocView? view,
        ImRect contentRect,
        bool interactive,
        string? parentRowColumnId,
        string? parentRowId,
        DocBlock? tableInstanceBlock = null)
    {
        DrawInternal(workspace, table, view, contentRect, interactive, parentRowColumnId, parentRowId, tableInstanceBlock, stateKey: "");
    }

    public static void DrawContentTab(
        DocWorkspace workspace,
        DocTable table,
        DocView? view,
        ImRect contentRect,
        bool interactive,
        string? parentRowColumnId,
        string? parentRowId,
        DocBlock? tableInstanceBlock,
        string stateKey)
    {
        DrawInternal(workspace, table, view, contentRect, interactive, parentRowColumnId, parentRowId, tableInstanceBlock, stateKey);
    }

    private static void DrawInternal(
        DocWorkspace workspace,
        DocTable? table,
        DocView? view,
        ImRect contentRect,
        bool interactive,
        string? parentRowColumnId,
        string? parentRowId,
        DocBlock? tableInstanceBlock,
        string stateKey)
    {
        if (table == null || view == null)
        {
            Im.Text("No table selected.".AsSpan(), contentRect.X + 10, contentRect.Y + 10, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }

        BeginStateScope(stateKey);

        var resolvedView = workspace.ResolveViewConfig(table, view, tableInstanceBlock);
        if (resolvedView == null)
        {
            Im.Text("No table selected.".AsSpan(), contentRect.X + 10, contentRect.Y + 10, Im.Style.FontSize, Im.Style.TextSecondary);
            EndStateScope();
            return;
        }
        view = resolvedView;

        var style = Im.Style;
        var input = Im.Context.Input;
        var mousePos = Im.MousePos;

        // Find the group-by column
        DocColumn? groupCol = null;
        if (!string.IsNullOrEmpty(view.GroupByColumnId))
        {
            for (int i = 0; i < table.Columns.Count; i++)
            {
                if (string.Equals(table.Columns[i].Id, view.GroupByColumnId, StringComparison.Ordinal))
                {
                    groupCol = table.Columns[i];
                    break;
                }
            }
        }

        if (groupCol == null || groupCol.Kind != DocColumnKind.Select || groupCol.Options == null || groupCol.Options.Count == 0)
        {
            Im.Text("Board view requires a Select column for grouping.".AsSpan(), contentRect.X + 10, contentRect.Y + 10, style.FontSize, style.TextSecondary);
            Im.Text("Set 'Group By' in the Inspector.".AsSpan(), contentRect.X + 10, contentRect.Y + 30, style.FontSize, style.TextSecondary);
            return;
        }

        // Get filtered/sorted row indices
        int[]? viewRowIndices = workspace.ComputeViewRowIndices(table, view, tableInstanceBlock);
        int rowCount = viewRowIndices?.Length ?? table.Rows.Count;

        // Find the first text column for card titles
        DocColumn? titleCol = null;
        for (int i = 0; i < table.Columns.Count; i++)
        {
            if (table.Columns[i].Kind == DocColumnKind.Text && table.Columns[i] != groupCol)
            {
                titleCol = table.Columns[i];
                break;
            }
        }

        var options = groupCol.Options;
        int laneCount = options.Count + 1; // +1 for empty/unset values

        // Background
        Im.DrawRect(contentRect.X, contentRect.Y, contentRect.Width, contentRect.Height, style.Background);

        // Horizontal scroll for lanes that overflow viewport
        float totalLanesWidth = laneCount * LaneWidth + (laneCount - 1) * LaneGap + LanePaddingX * 2f;
        float maxScrollX = Math.Max(0f, totalLanesWidth - contentRect.Width);
        _scrollX = interactive ? Math.Clamp(_scrollX, 0f, maxScrollX) : 0f;

        float laneX = contentRect.X + LanePaddingX - _scrollX;
        float laneContentTop = contentRect.Y + LaneHeaderHeight;
        Span<char> countBuf = stackalloc char[8];

        for (int laneIdx = 0; laneIdx < laneCount && laneIdx < 16; laneIdx++)
        {
            bool isEmptyLane = laneIdx == options.Count;
            string laneName = isEmptyLane ? "(empty)" : options[laneIdx];
            uint laneColor = LaneColors[laneIdx % LaneColors.Length];

            // Lane header
            float headerY = contentRect.Y;
            Im.DrawRect(laneX, headerY, LaneWidth, LaneHeaderHeight, style.Surface);
            Im.DrawLine(laneX, headerY + LaneHeaderHeight - 1, laneX + LaneWidth, headerY + LaneHeaderHeight - 1, 1f, style.Border);

            // Lane header accent stripe
            Im.DrawRect(laneX, headerY, LaneWidth, 3f, laneColor);

            float labelY = headerY + (LaneHeaderHeight - style.FontSize) * 0.5f;
            Im.Text(laneName.AsSpan(), laneX + CardPaddingX, labelY, style.FontSize, style.TextPrimary);

            // Count cards in this lane
            int cardCount = 0;
            for (int ri = 0; ri < rowCount; ri++)
            {
                int rowIdx = viewRowIndices != null ? viewRowIndices[ri] : ri;
                var row = table.Rows[rowIdx];
                if (!RowMatchesParentFilter(row, parentRowColumnId, parentRowId))
                {
                    continue;
                }

                var cell = row.GetCell(groupCol);
                string cellValue = cell.StringValue ?? "";
                bool matchesLane = isEmptyLane
                    ? string.IsNullOrEmpty(cellValue)
                    : string.Equals(cellValue, laneName, StringComparison.Ordinal);
                if (matchesLane) cardCount++;
            }

            // Draw count badge
            cardCount.TryFormat(countBuf, out int countLen);
            float countX = laneX + LaneWidth - CardPaddingX - Im.MeasureTextWidth(countBuf[..countLen], style.FontSize - 2f);
            Im.Text(countBuf[..countLen], countX, labelY + 1f, style.FontSize - 2f, style.TextSecondary);

            // Draw cards
            float cardY = laneContentTop + 4f;
            float laneScrollY = 0f;
            if (laneIdx < _laneScrollY.Length)
            {
                laneScrollY = interactive ? _laneScrollY[laneIdx] : 0f;
                cardY -= laneScrollY;
            }

            int cardIndex = 0;
            for (int ri = 0; ri < rowCount; ri++)
            {
                int rowIdx = viewRowIndices != null ? viewRowIndices[ri] : ri;
                var row = table.Rows[rowIdx];
                if (!RowMatchesParentFilter(row, parentRowColumnId, parentRowId))
                {
                    continue;
                }

                var cell = row.GetCell(groupCol);
                string cellValue = cell.StringValue ?? "";
                bool matchesLane = isEmptyLane
                    ? string.IsNullOrEmpty(cellValue)
                    : string.Equals(cellValue, laneName, StringComparison.Ordinal);
                if (!matchesLane) continue;

                // Draw insertion indicator when dragging
                if (_isDragging && _dragPastThreshold && _dragTargetLaneIndex == laneIdx && _dragTargetCardIndex == cardIndex)
                {
                    Im.DrawRect(laneX + AccentStripeWidth + 2f, cardY - 2f, LaneWidth - AccentStripeWidth - 4f, 3f, style.Primary);
                }

                // Skip drawing the dragged card at its source position
                if (_isDragging && _dragPastThreshold && _dragSourceRowIndex == rowIdx)
                {
                    cardIndex++;
                    continue;
                }

                float cardX = laneX;
                var cardRect = new ImRect(cardX, cardY, LaneWidth, CardHeight);

                // Card background
                Im.DrawRoundedRect(cardX, cardY, LaneWidth, CardHeight, CardCornerRadius, style.Surface);
                Im.DrawRoundedRectStroke(cardX, cardY, LaneWidth, CardHeight, CardCornerRadius, style.Border, 1f);

                // Accent stripe on left
                Im.DrawRect(cardX, cardY + CardCornerRadius, AccentStripeWidth, CardHeight - CardCornerRadius * 2f, laneColor);

                // Card title
                string title = "";
                if (titleCol != null)
                {
                    var titleCell = row.GetCell(titleCol);
                    title = titleCell.StringValue ?? "";
                }
                if (string.IsNullOrWhiteSpace(title)) title = row.Id[..Math.Min(8, row.Id.Length)];

                float titleX = cardX + AccentStripeWidth + CardPaddingX;
                float titleY = cardY + CardPaddingY;
                Im.Text(title.AsSpan(), titleX, titleY, style.FontSize, style.TextPrimary);

                // Card hover and click
                bool cardHovered = cardRect.Contains(mousePos);
                if (interactive && cardHovered && input.MousePressed)
                {
                    // Select row
                    workspace.SelectedRowIndex = rowIdx;

                    // Start drag
                    _dragSourceRowIndex = rowIdx;
                    _dragSourceLaneIndex = laneIdx;
                    _dragStartMousePos = mousePos;
                    _dragPastThreshold = false;
                    _isDragging = true;
                }

                cardY += CardHeight + CardGap;
                cardIndex++;
            }

            // Insertion indicator at end of lane
            if (_isDragging && _dragPastThreshold && _dragTargetLaneIndex == laneIdx && _dragTargetCardIndex >= cardCount)
            {
                Im.DrawRect(laneX + AccentStripeWidth + 2f, cardY - 2f, LaneWidth - AccentStripeWidth - 4f, 3f, style.Primary);
            }

            // Lane scroll handling (vertical per-lane + horizontal board)
            var laneBodyRect = new ImRect(laneX, laneContentTop, LaneWidth, contentRect.Bottom - laneContentTop);
            if (interactive && laneBodyRect.Contains(mousePos) && laneIdx < _laneScrollY.Length)
            {
                float maxLaneScroll = Math.Max(0, cardCount * (CardHeight + CardGap) - (contentRect.Bottom - laneContentTop));
                bool canScrollV = maxLaneScroll > 0f;
                bool canScrollH = maxScrollX > 0f;

                if (input.ScrollDeltaX != 0f && canScrollH)
                {
                    _scrollX -= input.ScrollDeltaX * 40f;
                    _scrollX = Math.Clamp(_scrollX, 0f, maxScrollX);
                }

                if (input.ScrollDelta != 0f)
                {
                    // Proximity-based: closer to bottom → vertical lane scroll, closer to right → horizontal board scroll
                    float distToBottom = Math.Abs(mousePos.Y - contentRect.Bottom);
                    float distToRight = Math.Abs(mousePos.X - contentRect.Right);
                    bool routeToH = canScrollH && (!canScrollV || distToRight < distToBottom);

                    if (routeToH)
                    {
                        _scrollX -= input.ScrollDelta * 40f;
                        _scrollX = Math.Clamp(_scrollX, 0f, maxScrollX);
                    }
                    else if (canScrollV)
                    {
                        _laneScrollY[laneIdx] = Math.Clamp(_laneScrollY[laneIdx] - input.ScrollDelta * 30f, 0, maxLaneScroll);
                    }
                }
            }

            laneX += LaneWidth + LaneGap;
        }

        // Board-level horizontal scroll when mouse is in the content area but not over a lane
        if (interactive && contentRect.Contains(mousePos) && maxScrollX > 0f)
        {
            if (input.ScrollDeltaX != 0f)
            {
                _scrollX -= input.ScrollDeltaX * 40f;
                _scrollX = Math.Clamp(_scrollX, 0f, maxScrollX);
            }
        }

        // Handle drag in progress
        if (!interactive)
        {
            if (_isDragging)
            {
                ResetDragState();
            }

            return;
        }

        if (_isDragging)
        {
            if (!_dragPastThreshold)
            {
                float dist = MathF.Abs(mousePos.Y - _dragStartMousePos.Y) + MathF.Abs(mousePos.X - _dragStartMousePos.X);
                if (dist > DragThreshold) _dragPastThreshold = true;
            }

            if (_dragPastThreshold)
            {
                // Determine target lane from mouse X (accounting for horizontal scroll)
                float testLaneX = contentRect.X + LanePaddingX - _scrollX;
                _dragTargetLaneIndex = -1;
                for (int li = 0; li < laneCount && li < 16; li++)
                {
                    if (mousePos.X >= testLaneX && mousePos.X < testLaneX + LaneWidth)
                    {
                        _dragTargetLaneIndex = li;
                        break;
                    }
                    testLaneX += LaneWidth + LaneGap;
                }

                // Determine target card index within lane from mouse Y
                if (_dragTargetLaneIndex >= 0)
                {
                    float relY = mousePos.Y - laneContentTop + (_dragTargetLaneIndex < _laneScrollY.Length ? _laneScrollY[_dragTargetLaneIndex] : 0);
                    _dragTargetCardIndex = Math.Max(0, (int)(relY / (CardHeight + CardGap)));
                }
            }

            // Mouse released -> execute move
            if (input.MouseReleased)
            {
                if (_dragPastThreshold && _dragTargetLaneIndex >= 0 && _dragSourceLaneIndex != _dragTargetLaneIndex && _dragSourceRowIndex >= 0)
                {
                    // Change the group column value to the target lane's option
                    bool targetIsEmpty = _dragTargetLaneIndex == options.Count;
                    string targetValue = targetIsEmpty ? "" : options[_dragTargetLaneIndex];

                    var row = table.Rows[_dragSourceRowIndex];
                    var oldCell = row.GetCell(groupCol);
                    var newCell = groupCol.Kind == DocColumnKind.MeshAsset
                        ? DocCellValue.Text(targetValue, oldCell.ModelPreviewSettings)
                        : DocCellValue.Text(targetValue);

                    workspace.ExecuteCommand(new DocCommand
                    {
                        Kind = DocCommandKind.SetCell,
                        TableId = table.Id,
                        RowId = row.Id,
                        ColumnId = groupCol.Id,
                        OldCellValue = oldCell,
                        NewCellValue = newCell,
                    });
                }

                ResetDragState();
            }

            // Escape to cancel
            if (input.KeyEscape)
            {
                ResetDragState();
            }
        }

        EndStateScope();
    }

    private static bool RowMatchesParentFilter(DocRow row, string? parentRowColumnId, string? parentRowId)
    {
        if (string.IsNullOrWhiteSpace(parentRowColumnId) || string.IsNullOrWhiteSpace(parentRowId))
        {
            return true;
        }

        string rowParentId = row.GetCell(parentRowColumnId).StringValue ?? "";
        return string.Equals(rowParentId, parentRowId, StringComparison.Ordinal);
    }

    private static void ResetDragState()
    {
        _isDragging = false;
        _dragSourceRowIndex = -1;
        _dragSourceLaneIndex = -1;
        _dragTargetLaneIndex = -1;
        _dragTargetCardIndex = -1;
        _dragPastThreshold = false;
    }

    private static void BeginStateScope(string stateKey)
    {
        if (string.IsNullOrWhiteSpace(stateKey))
        {
            _activeStateKey = "";
            return;
        }

        _activeStateKey = stateKey;
        if (!ViewStatesByStateKey.TryGetValue(stateKey, out var state))
        {
            state = new BoardViewState();
            ViewStatesByStateKey[stateKey] = state;
        }

        _scrollX = state.ScrollX;
        for (int i = 0; i < _laneScrollY.Length && i < state.LaneScrollY.Length; i++)
        {
            _laneScrollY[i] = state.LaneScrollY[i];
        }
    }

    private static void EndStateScope()
    {
        if (string.IsNullOrWhiteSpace(_activeStateKey))
        {
            return;
        }

        if (ViewStatesByStateKey.TryGetValue(_activeStateKey, out var state))
        {
            state.ScrollX = _scrollX;
            for (int i = 0; i < _laneScrollY.Length && i < state.LaneScrollY.Length; i++)
            {
                state.LaneScrollY[i] = _laneScrollY[i];
            }
        }

        _activeStateKey = "";
    }
}
