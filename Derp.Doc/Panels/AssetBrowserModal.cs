using Derp.Doc.Assets;
using Derp.Doc.Commands;
using Derp.Doc.Editor;
using Derp.Doc.Model;
using Derp.Doc.Tables;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Widgets;

namespace Derp.Doc.Panels;

internal static class AssetBrowserModal
{
    private const string ModalId = "asset_browser_modal";
    private const float ModalWidth = 900f;
    private const float ModalHeight = 640f;
    private const float GridPadding = 8f;
    private const float GridGap = 8f;
    private const float GridCellWidth = 108f;
    private const float GridCellHeight = 126f;
    private const float ThumbnailPadding = 6f;
    private const float ThumbnailLabelGap = 4f;

    private static readonly char[] _searchBuffer = new char[128];
    private static readonly int[] _filteredIndices = new int[32768];
    private static readonly List<AssetScanner.AssetEntry> _entries = new();

    private static string _tableId = "";
    private static string _rowId = "";
    private static string _columnId = "";
    private static string _assetsRoot = "";
    private static string _selectedRelativePath = "";
    private static DocColumnKind _columnKind;
    private static DocModelPreviewSettings? _columnModelPreviewSettings;
    private static int _searchLength;
    private static int _selectedFilteredIndex = -1;
    private static float _scrollY;
    private static Action<string>? _onSelectionCommitted;

    public static void Open(
        string assetsRoot,
        DocColumnKind columnKind,
        string tableId,
        string rowId,
        string columnId,
        string currentValue,
        DocModelPreviewSettings? modelPreviewSettings = null)
    {
        if (string.IsNullOrWhiteSpace(assetsRoot) || !Directory.Exists(assetsRoot))
        {
            return;
        }

        _assetsRoot = Path.GetFullPath(assetsRoot);
        _columnKind = columnKind;
        _tableId = tableId;
        _rowId = rowId;
        _columnId = columnId;
        _selectedRelativePath = currentValue ?? "";
        _columnModelPreviewSettings = modelPreviewSettings?.Clone();
        _searchLength = 0;
        _searchBuffer[0] = '\0';
        _selectedFilteredIndex = -1;
        _scrollY = 0f;
        _onSelectionCommitted = null;

        RefreshEntries(forceRefresh: true);
        ImModal.Open(ModalId);
    }

    public static void OpenPicker(
        string assetsRoot,
        DocColumnKind columnKind,
        string currentValue,
        Action<string> onSelectionCommitted)
    {
        if (onSelectionCommitted == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(assetsRoot) || !Directory.Exists(assetsRoot))
        {
            return;
        }

        _assetsRoot = Path.GetFullPath(assetsRoot);
        _columnKind = columnKind;
        _tableId = "";
        _rowId = "";
        _columnId = "";
        _selectedRelativePath = currentValue ?? "";
        _columnModelPreviewSettings = null;
        _searchLength = 0;
        _searchBuffer[0] = '\0';
        _selectedFilteredIndex = -1;
        _scrollY = 0f;
        _onSelectionCommitted = onSelectionCommitted;

        RefreshEntries(forceRefresh: true);
        ImModal.Open(ModalId);
    }

    public static void Draw(DocWorkspace workspace)
    {
        if (!ImModal.IsOpen(ModalId))
        {
            return;
        }

        if (!ImModal.Begin(ModalId, ModalWidth, ModalHeight, GetModalTitle()))
        {
            if (!ImModal.IsOpen(ModalId))
            {
                ResetState();
            }

            return;
        }

        RefreshEntries(forceRefresh: false);

        var style = Im.Style;
        var input = Im.Context.Input;

        float contentLeft = ImModal.ContentOffset.X;
        float contentTop = ImModal.ContentOffset.Y;
        float contentWidth = ModalWidth - (style.Padding * 2f);

        ImSearchBox.DrawAt(
            "asset_browser_search",
            _searchBuffer,
            ref _searchLength,
            _searchBuffer.Length,
            contentLeft,
            contentTop,
            contentWidth,
            "Search assets");

        int filteredCount = BuildFilteredIndexList(_searchBuffer.AsSpan(0, _searchLength));
        SyncSelectedIndex(filteredCount);

        float gridTop = contentTop + style.MinButtonHeight + 10f;
        float footerHeight = 86f;
        float gridHeight = ModalHeight - gridTop - footerHeight;
        var gridRect = new ImRect(contentLeft, gridTop, contentWidth, MathF.Max(120f, gridHeight));

        Im.DrawRoundedRect(gridRect.X, gridRect.Y, gridRect.Width, gridRect.Height, 7f, BlendColor(style.Surface, 0.2f, style.Background));
        Im.DrawRoundedRectStroke(gridRect.X, gridRect.Y, gridRect.Width, gridRect.Height, 7f, style.Border, 1f);

        DrawGrid(workspace, gridRect, filteredCount);

        float footerTop = gridRect.Bottom + 10f;
        DrawFooter(workspace, footerTop, contentLeft, contentWidth, filteredCount);

        if (input.KeyEnter && _selectedFilteredIndex >= 0 && _selectedFilteredIndex < filteredCount)
        {
            int sourceIndex = _filteredIndices[_selectedFilteredIndex];
            if (sourceIndex >= 0 && sourceIndex < _entries.Count)
            {
                TryCommitSelection(workspace, _entries[sourceIndex].RelativePath);
            }
        }

        if (input.KeyEscape)
        {
            ImModal.Close();
        }

        ImModal.End();

        if (!ImModal.IsOpen(ModalId))
        {
            ResetState();
        }
    }

    private static string GetModalTitle()
    {
        if (_columnKind == DocColumnKind.MeshAsset)
        {
            return "Select Model";
        }

        if (_columnKind == DocColumnKind.AudioAsset)
        {
            return "Select Audio";
        }

        if (_columnKind == DocColumnKind.UiAsset)
        {
            return "Select UI";
        }

        return "Select Texture";
    }

    private static void DrawGrid(DocWorkspace workspace, ImRect gridRect, int filteredCount)
    {
        int columnCount = Math.Max(1, (int)MathF.Floor((gridRect.Width - (GridPadding * 2f) + GridGap) / (GridCellWidth + GridGap)));
        int rowCount = filteredCount > 0 ? ((filteredCount + columnCount - 1) / columnCount) : 1;
        float contentHeight = GridPadding + (rowCount * (GridCellHeight + GridGap));
        float contentY = ImScrollView.Begin(gridRect, contentHeight, ref _scrollY, handleMouseWheel: true);

        float rowStride = GridCellHeight + GridGap;
        int firstVisibleRow = Math.Max(0, (int)MathF.Floor(Math.Max(0f, _scrollY - GridPadding) / rowStride));
        int lastVisibleRow = Math.Min(
            rowCount - 1,
            (int)MathF.Ceiling((Math.Max(0f, _scrollY) + gridRect.Height) / rowStride));

        var input = Im.Context.Input;
        for (int rowIndex = firstVisibleRow; rowIndex <= lastVisibleRow; rowIndex++)
        {
            for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                int filteredIndex = rowIndex * columnCount + columnIndex;
                if (filteredIndex < 0 || filteredIndex >= filteredCount)
                {
                    continue;
                }

                int sourceIndex = _filteredIndices[filteredIndex];
                if (sourceIndex < 0 || sourceIndex >= _entries.Count)
                {
                    continue;
                }

                float cardX = gridRect.X + GridPadding + columnIndex * (GridCellWidth + GridGap);
                float cardY = contentY + GridPadding + rowIndex * (GridCellHeight + GridGap);
                var cardRect = new ImRect(cardX, cardY, GridCellWidth, GridCellHeight);

                bool hovered = cardRect.Contains(Im.MousePos);
                bool selected = filteredIndex == _selectedFilteredIndex;
                DrawAssetCardBackground(cardRect, selected, hovered);

                var entry = _entries[sourceIndex];
                DrawAssetCardThumbnail(entry, cardRect);
                DrawAssetCardLabel(entry, cardRect);

                if (hovered && input.MousePressed)
                {
                    _selectedFilteredIndex = filteredIndex;
                    _selectedRelativePath = entry.RelativePath;

                    if (input.IsDoubleClick)
                    {
                        TryCommitSelection(workspace, entry.RelativePath);
                    }
                }
            }
        }

        int scrollbarId = Im.Context.GetId("asset_browser_scroll");
        var scrollbarRect = new ImRect(gridRect.Right - 8f, gridRect.Y, 8f, gridRect.Height);
        ImScrollView.End(scrollbarId, scrollbarRect, gridRect.Height, contentHeight, ref _scrollY);
    }

    private static void DrawAssetCardBackground(ImRect cardRect, bool selected, bool hovered)
    {
        var style = Im.Style;
        uint cardColor = selected
            ? BlendColor(style.Primary, 0.22f, style.Surface)
            : hovered
                ? BlendColor(style.Hover, 0.2f, style.Surface)
                : BlendColor(style.Surface, 0.34f, style.Background);

        Im.DrawRoundedRect(cardRect.X, cardRect.Y, cardRect.Width, cardRect.Height, 6f, cardColor);
        Im.DrawRoundedRectStroke(cardRect.X, cardRect.Y, cardRect.Width, cardRect.Height, 6f, style.Border, 1f);
    }

    private static void DrawAssetCardThumbnail(AssetScanner.AssetEntry entry, ImRect cardRect)
    {
        var style = Im.Style;

        float thumbnailX = cardRect.X + ThumbnailPadding;
        float thumbnailY = cardRect.Y + ThumbnailPadding;
        float thumbnailWidth = cardRect.Width - (ThumbnailPadding * 2f);
        float thumbnailHeight = cardRect.Height - style.FontSize - ThumbnailPadding - ThumbnailLabelGap - 8f;
        var thumbnailRect = new ImRect(thumbnailX, thumbnailY, thumbnailWidth, MathF.Max(24f, thumbnailHeight));

        Im.DrawRoundedRect(thumbnailRect.X, thumbnailRect.Y, thumbnailRect.Width, thumbnailRect.Height, 4f, BlendColor(style.Background, 0.45f, style.Surface));

        var thumbnailResult = DocAssetServices.ThumbnailCache.GetThumbnail(
            _assetsRoot,
            _columnKind,
            entry.RelativePath,
            _columnKind == DocColumnKind.MeshAsset ? _columnModelPreviewSettings : null);
        Im.PushClipRect(thumbnailRect);
        if (thumbnailResult.Status == AssetThumbnailCache.ThumbnailStatus.Ready)
        {
            var texture = thumbnailResult.Texture;
            if (texture.Width > 0 && texture.Height > 0)
            {
                float scale = MathF.Min(thumbnailRect.Width / texture.Width, thumbnailRect.Height / texture.Height);
                float drawWidth = texture.Width * scale;
                float drawHeight = texture.Height * scale;
                float drawX = thumbnailRect.X + (thumbnailRect.Width - drawWidth) * 0.5f;
                float drawY = thumbnailRect.Y + (thumbnailRect.Height - drawHeight) * 0.5f;
                Im.DrawImage(texture, drawX, drawY, drawWidth, drawHeight);
            }
        }
        else
        {
            ReadOnlySpan<char> statusText = thumbnailResult.Status switch
            {
                AssetThumbnailCache.ThumbnailStatus.Loading => "...".AsSpan(),
                AssetThumbnailCache.ThumbnailStatus.Missing => "(missing)".AsSpan(),
                AssetThumbnailCache.ThumbnailStatus.PreviewUnavailable => "(no preview)".AsSpan(),
                AssetThumbnailCache.ThumbnailStatus.BudgetExceeded => "(cache full)".AsSpan(),
                AssetThumbnailCache.ThumbnailStatus.InvalidPath => "(invalid)".AsSpan(),
                _ => "(failed)".AsSpan(),
            };

            uint statusColor = thumbnailResult.Status == AssetThumbnailCache.ThumbnailStatus.Missing
                ? BlendColor(style.Secondary, 0.68f, style.TextPrimary)
                : style.TextSecondary;

            float textWidth = Im.MeasureTextWidth(statusText, style.FontSize);
            float textX = thumbnailRect.X + (thumbnailRect.Width - textWidth) * 0.5f;
            float textY = thumbnailRect.Y + (thumbnailRect.Height - style.FontSize) * 0.5f;
            Im.Text(statusText, textX, textY, style.FontSize, statusColor);
        }
        Im.PopClipRect();
    }

    private static void DrawAssetCardLabel(AssetScanner.AssetEntry entry, ImRect cardRect)
    {
        float labelY = cardRect.Bottom - Im.Style.FontSize - 6f;
        var labelClipRect = new ImRect(cardRect.X + 4f, labelY, cardRect.Width - 8f, Im.Style.FontSize + 2f);
        Im.PushClipRect(labelClipRect);
        Im.Text(entry.FileName.AsSpan(), cardRect.X + 4f, labelY, Im.Style.FontSize, Im.Style.TextPrimary);
        Im.PopClipRect();
    }

    private static void DrawFooter(DocWorkspace workspace, float footerTop, float contentLeft, float contentWidth, int filteredCount)
    {
        var style = Im.Style;

        ReadOnlySpan<char> selectionLabel = _selectedRelativePath.Length > 0
            ? _selectedRelativePath.AsSpan()
            : "(none)".AsSpan();
        Im.Text("Selected:".AsSpan(), contentLeft, footerTop, style.FontSize, style.TextSecondary);
        var selectedClipRect = new ImRect(contentLeft + 64f, footerTop, contentWidth - 64f, style.FontSize + 2f);
        Im.PushClipRect(selectedClipRect);
        Im.Text(selectionLabel, contentLeft + 64f, footerTop, style.FontSize, style.TextPrimary);
        Im.PopClipRect();

        float buttonY = footerTop + style.FontSize + 10f;
        float buttonWidth = 110f;
        float buttonHeight = 34f;

        bool canConfirm = _selectedFilteredIndex >= 0 && _selectedFilteredIndex < filteredCount;
        if (canConfirm && Im.Button("Confirm", contentLeft, buttonY, buttonWidth, buttonHeight))
        {
            int sourceIndex = _filteredIndices[_selectedFilteredIndex];
            if (sourceIndex >= 0 && sourceIndex < _entries.Count)
            {
                TryCommitSelection(workspace, _entries[sourceIndex].RelativePath);
            }
        }

        if (Im.Button("Clear", contentLeft + buttonWidth + 8f, buttonY, buttonWidth, buttonHeight))
        {
            TryCommitSelection(workspace, "");
        }

        if (Im.Button("Cancel", contentLeft + (buttonWidth + 8f) * 2f, buttonY, buttonWidth, buttonHeight))
        {
            ImModal.Close();
        }
    }

    private static void RefreshEntries(bool forceRefresh)
    {
        if (string.IsNullOrWhiteSpace(_assetsRoot))
        {
            _entries.Clear();
            return;
        }

        var scanned = DocAssetServices.AssetScanner.ScanAssets(_assetsRoot, _columnKind, forceRefresh);
        _entries.Clear();

        for (int entryIndex = 0; entryIndex < scanned.Count; entryIndex++)
        {
            _entries.Add(scanned[entryIndex]);
        }
    }

    private static int BuildFilteredIndexList(ReadOnlySpan<char> filterText)
    {
        int filteredCount = 0;
        for (int entryIndex = 0; entryIndex < _entries.Count; entryIndex++)
        {
            var entry = _entries[entryIndex];
            if (filterText.Length > 0 &&
                !entry.FileName.AsSpan().Contains(filterText, StringComparison.OrdinalIgnoreCase) &&
                !entry.RelativePath.AsSpan().Contains(filterText, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (filteredCount >= _filteredIndices.Length)
            {
                break;
            }

            _filteredIndices[filteredCount] = entryIndex;
            filteredCount++;
        }

        return filteredCount;
    }

    private static void SyncSelectedIndex(int filteredCount)
    {
        if (filteredCount <= 0)
        {
            _selectedFilteredIndex = -1;
            return;
        }

        if (_selectedFilteredIndex >= 0 && _selectedFilteredIndex < filteredCount)
        {
            int currentSourceIndex = _filteredIndices[_selectedFilteredIndex];
            if (currentSourceIndex >= 0 && currentSourceIndex < _entries.Count)
            {
                if (string.Equals(_entries[currentSourceIndex].RelativePath, _selectedRelativePath, StringComparison.Ordinal))
                {
                    return;
                }
            }
        }

        if (_selectedRelativePath.Length > 0)
        {
            for (int filteredIndex = 0; filteredIndex < filteredCount; filteredIndex++)
            {
                int sourceIndex = _filteredIndices[filteredIndex];
                if (sourceIndex < 0 || sourceIndex >= _entries.Count)
                {
                    continue;
                }

                if (string.Equals(_entries[sourceIndex].RelativePath, _selectedRelativePath, StringComparison.Ordinal))
                {
                    _selectedFilteredIndex = filteredIndex;
                    return;
                }
            }
        }

        _selectedFilteredIndex = 0;
        int defaultSourceIndex = _filteredIndices[0];
        if (defaultSourceIndex >= 0 && defaultSourceIndex < _entries.Count)
        {
            _selectedRelativePath = _entries[defaultSourceIndex].RelativePath;
        }
    }

    private static void TryCommitSelection(DocWorkspace workspace, string selectedRelativePath)
    {
        if (_onSelectionCommitted != null)
        {
            _onSelectionCommitted(selectedRelativePath ?? "");
            ImModal.Close();
            return;
        }

        if (!TryResolveCellTarget(workspace, out DocTable table, out DocRow row, out DocColumn column))
        {
            ImModal.Close();
            return;
        }

        var oldCell = row.GetCell(column);
        string oldPath = oldCell.StringValue ?? "";
        string newPath = selectedRelativePath ?? "";
        if (string.Equals(oldPath, newPath, StringComparison.Ordinal))
        {
            ImModal.Close();
            return;
        }

        workspace.CancelTableCellEditIfActive();
        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetCell,
            TableId = table.Id,
            RowId = row.Id,
            ColumnId = column.Id,
            OldCellValue = oldCell.Clone(),
            NewCellValue = column.Kind == DocColumnKind.MeshAsset
                ? DocCellValue.Text(newPath, oldCell.ModelPreviewSettings)
                : DocCellValue.Text(newPath),
        });

        ImModal.Close();
    }

    private static bool TryResolveCellTarget(DocWorkspace workspace, out DocTable table, out DocRow row, out DocColumn column)
    {
        table = null!;
        row = null!;
        column = null!;

        for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
        {
            var candidateTable = workspace.Project.Tables[tableIndex];
            if (!string.Equals(candidateTable.Id, _tableId, StringComparison.Ordinal))
            {
                continue;
            }

            table = candidateTable;
            break;
        }

        if (table == null)
        {
            return false;
        }

        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var candidateRow = table.Rows[rowIndex];
            if (!string.Equals(candidateRow.Id, _rowId, StringComparison.Ordinal))
            {
                continue;
            }

            row = candidateRow;
            break;
        }

        if (row == null)
        {
            return false;
        }

        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            var candidateColumn = table.Columns[columnIndex];
            if (!string.Equals(candidateColumn.Id, _columnId, StringComparison.Ordinal))
            {
                continue;
            }

            column = candidateColumn;
            break;
        }

        return column != null;
    }

    private static void ResetState()
    {
        _tableId = "";
        _rowId = "";
        _columnId = "";
        _assetsRoot = "";
        _selectedRelativePath = "";
        _columnModelPreviewSettings = null;
        _searchLength = 0;
        _searchBuffer[0] = '\0';
        _selectedFilteredIndex = -1;
        _scrollY = 0f;
        _onSelectionCommitted = null;
        _entries.Clear();
    }

    private static uint BlendColor(uint baseColor, float t, uint blendColor)
    {
        t = Math.Clamp(t, 0f, 1f);

        float br = (baseColor & 0xFF) / 255f;
        float bg = ((baseColor >> 8) & 0xFF) / 255f;
        float bb = ((baseColor >> 16) & 0xFF) / 255f;
        float ba = ((baseColor >> 24) & 0xFF) / 255f;

        float rr = (blendColor & 0xFF) / 255f;
        float rg = ((blendColor >> 8) & 0xFF) / 255f;
        float rb = ((blendColor >> 16) & 0xFF) / 255f;
        float ra = ((blendColor >> 24) & 0xFF) / 255f;

        byte outR = (byte)Math.Clamp((int)MathF.Round((br * (1f - t) + rr * t) * 255f), 0, 255);
        byte outG = (byte)Math.Clamp((int)MathF.Round((bg * (1f - t) + rg * t) * 255f), 0, 255);
        byte outB = (byte)Math.Clamp((int)MathF.Round((bb * (1f - t) + rb * t) * 255f), 0, 255);
        byte outA = (byte)Math.Clamp((int)MathF.Round((ba * (1f - t) + ra * t) * 255f), 0, 255);

        return (uint)(outR | (outG << 8) | (outB << 16) | (outA << 24));
    }
}
