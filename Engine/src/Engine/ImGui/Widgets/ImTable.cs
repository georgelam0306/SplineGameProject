using System.Numerics;
using DerpLib.ImGui.Core;

namespace DerpLib.ImGui.Widgets;

/// <summary>
/// Table widget with sortable columns, row selection, and scrolling.
/// </summary>
/// <example>
/// ImTable.Begin("my_table", x, y, 400, 300);
/// ImTable.Column("Name", 150);
/// ImTable.Column("Value", 100, sortable: true);
/// ImTable.HeadersRow();
///
/// foreach (var item in items)
/// {
///     if (ImTable.BeginRow(item.Id))
///     {
///         ImTable.Cell(item.Name);
///         ImTable.Cell($"{item.Value:F2}");
///         ImTable.EndRow();
///     }
/// }
/// ImTable.End();
/// </example>
public static class ImTable
{
    // Column definitions (reused per frame)
    private static readonly ColumnDef[] _columns = new ColumnDef[32];
    private static int _columnCount;
    private static int _columnIndex;

    // Table state (simple array-based storage, keyed by table ID hash)
    private static readonly TableState[] _tableStates = new TableState[64];
    private static readonly int[] _tableStateIds = new int[64];
    private static int _tableStateCount;

    // Current table context
    private static TableContext _ctx;

    // Visual settings
    public static float HeaderHeight = 26f;
    public static float RowHeight = 24f;
    public static float CellPadding = 6f;

    private struct ColumnDef
    {
        public string Name;
        public float Width;
        public bool Sortable;
        public ImAlign Align;
    }

    private struct TableState
    {
        public int SortColumn;      // -1 = no sort
        public bool SortAscending;
        public int SelectedRowId;
        public float ScrollY;
    }

    private struct TableContext
    {
        public int TableId;
        public float X, Y, Width, Height;
        public float ContentY;      // Current Y for rows
        public float ContentHeight; // Available height for rows
        public int RowIndex;
        public int CurrentRowId;
        public bool RowHovered;
        public bool RowSelected;
        public float ScrollY;
        public float MaxScrollY;
        public int StateIndex;
    }

    /// <summary>
    /// Begin a table. Must be paired with End().
    /// </summary>
    public static void Begin(string id, float x, float y, float width, float height)
    {
        Begin(Im.Context.GetId(id), x, y, width, height);
    }

    /// <summary>
    /// Begin a table with integer ID.
    /// </summary>
    public static void Begin(int id, float x, float y, float width, float height)
    {
        _columnCount = 0;
        _columnIndex = 0;

        // Get or create state
        int stateIndex = FindOrCreateState(id);
        ref var state = ref _tableStates[stateIndex];

        var rect = new ImRect(x, y, width, height);

        _ctx = new TableContext
        {
            TableId = id,
            X = rect.X,
            Y = rect.Y,
            Width = rect.Width,
            Height = rect.Height,
            ContentY = rect.Y + HeaderHeight,
            ContentHeight = rect.Height - HeaderHeight,
            RowIndex = 0,
            ScrollY = state.ScrollY,
            StateIndex = stateIndex
        };

        // Draw table background
        Im.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.Background);
        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, 0, Im.Style.Border, 1f);
    }

    /// <summary>
    /// Define a column. Call after Begin(), before HeadersRow().
    /// </summary>
    public static void Column(string name, float width, bool sortable = false, ImAlign align = ImAlign.Start)
    {
        if (_columnCount >= 32) return;
        _columns[_columnCount++] = new ColumnDef
        {
            Name = name,
            Width = width,
            Sortable = sortable,
            Align = align
        };
    }

    /// <summary>
    /// Draw the header row. Call after defining all columns.
    /// </summary>
    public static void HeadersRow()
    {
        if (_columnCount == 0) return;

        ref var state = ref _tableStates[_ctx.StateIndex];
        float x = _ctx.X;
        float y = _ctx.Y;

        // Calculate auto-width columns
        float totalFixed = 0;
        int autoCount = 0;
        for (int i = 0; i < _columnCount; i++)
        {
            if (_columns[i].Width > 0)
                totalFixed += _columns[i].Width;
            else
                autoCount++;
        }
        float autoWidth = autoCount > 0 ? (_ctx.Width - totalFixed) / autoCount : 0;

        // Draw header background
        Im.DrawRect(x, y, _ctx.Width, HeaderHeight, Im.Style.Surface);

        // Draw columns
        float colX = x;
        for (int i = 0; i < _columnCount; i++)
        {
            ref var col = ref _columns[i];
            float colWidth = col.Width > 0 ? col.Width : autoWidth;
            var cellRect = new ImRect(colX, y, colWidth, HeaderHeight);

            // Hit test for sorting
            bool hovered = col.Sortable && cellRect.Contains(Im.MousePos);
            bool clicked = hovered && Im.MousePressed;

            if (hovered)
            {
                Im.DrawRect(cellRect.X, cellRect.Y, cellRect.Width, cellRect.Height, Im.Style.Hover);
            }

            if (clicked)
            {
                if (state.SortColumn == i)
                    state.SortAscending = !state.SortAscending;
                else
                {
                    state.SortColumn = i;
                    state.SortAscending = true;
                }
            }

            // Draw header text
            float textX = colX + CellPadding;
            float textY = y + (HeaderHeight - Im.Style.FontSize) / 2;
            Im.Text(col.Name.AsSpan(), textX, textY, Im.Style.FontSize, Im.Style.TextSecondary);

            // Draw sort indicator
            if (state.SortColumn == i)
            {
                float arrowX = colX + colWidth - 14;
                float arrowY = y + HeaderHeight / 2;
                DrawSortArrow(arrowX, arrowY, state.SortAscending);
            }

            // Draw column separator
            if (i < _columnCount - 1)
            {
                Im.DrawLine(colX + colWidth, y, colX + colWidth, y + HeaderHeight, 1f, Im.Style.Border);
            }

            colX += colWidth;
        }

        // Draw header bottom border
        Im.DrawLine(x, y + HeaderHeight, x + _ctx.Width, y + HeaderHeight, 1f, Im.Style.Border);
    }

    /// <summary>
    /// Begin a data row. Returns true if the row is visible.
    /// </summary>
    public static bool BeginRow(int rowId)
    {
        _columnIndex = 0;
        _ctx.CurrentRowId = rowId;

        // Calculate row Y position with scroll offset
        float rowY = _ctx.ContentY + (_ctx.RowIndex * RowHeight) - _ctx.ScrollY;

        // Visibility culling
        if (rowY + RowHeight < _ctx.ContentY || rowY > _ctx.Y + _ctx.Height)
        {
            _ctx.RowIndex++;
            return false;
        }

        ref var state = ref _tableStates[_ctx.StateIndex];
        var rowRect = new ImRect(_ctx.X, rowY, _ctx.Width, RowHeight);

        // Clamp row to content area
        if (rowY < _ctx.ContentY)
        {
            float clip = _ctx.ContentY - rowY;
            rowRect = new ImRect(rowRect.X, _ctx.ContentY, rowRect.Width, rowRect.Height - clip);
        }
        if (rowY + RowHeight > _ctx.Y + _ctx.Height)
        {
            rowRect = new ImRect(rowRect.X, rowRect.Y, rowRect.Width, _ctx.Y + _ctx.Height - rowY);
        }

        // Hit test
        _ctx.RowHovered = rowRect.Contains(Im.MousePos);
        _ctx.RowSelected = state.SelectedRowId == rowId;

        // Handle selection
        if (_ctx.RowHovered && Im.MousePressed)
        {
            state.SelectedRowId = rowId;
            _ctx.RowSelected = true;
        }

        // Draw row background
        uint bgColor = _ctx.RowSelected ? Im.Style.Active :
                       _ctx.RowHovered ? Im.Style.Hover :
                       (_ctx.RowIndex % 2 == 0) ? Im.Style.Background : Im.Style.Surface;

        Im.DrawRect(rowRect.X, rowRect.Y, rowRect.Width, rowRect.Height, bgColor);

        _ctx.RowIndex++;
        return true;
    }

    /// <summary>
    /// Draw a cell in the current row.
    /// </summary>
    public static void Cell(string text)
    {
        if (_columnIndex >= _columnCount) return;

        ref var col = ref _columns[_columnIndex];
        float colX = GetColumnX(_columnIndex);
        float colWidth = GetColumnWidth(_columnIndex);
        float rowY = _ctx.ContentY + ((_ctx.RowIndex - 1) * RowHeight) - _ctx.ScrollY;

        // Skip if outside visible area
        if (rowY + RowHeight < _ctx.ContentY || rowY > _ctx.Y + _ctx.Height)
        {
            _columnIndex++;
            return;
        }

        float textX = colX + CellPadding;
        float textY = rowY + (RowHeight - Im.Style.FontSize) / 2;

        // Handle alignment
        if (col.Align == ImAlign.Center)
        {
            float textWidth = MeasureTextWidth(text);
            textX = colX + (colWidth - textWidth) / 2;
        }
        else if (col.Align == ImAlign.End)
        {
            float textWidth = MeasureTextWidth(text);
            textX = colX + colWidth - textWidth - CellPadding;
        }

        // Clamp text Y to visible area
        if (textY >= _ctx.ContentY && textY + Im.Style.FontSize <= _ctx.Y + _ctx.Height)
        {
            uint textColor = _ctx.RowSelected ? 0xFFFFFFFF : Im.Style.TextPrimary;
            Im.Text(text.AsSpan(), textX, textY, Im.Style.FontSize, textColor);
        }

        _columnIndex++;
    }

    /// <summary>
    /// Draw a cell with a numeric value.
    /// </summary>
    public static void Cell(float value, string format = "F2")
    {
        Span<char> buffer = stackalloc char[32];
        value.TryFormat(buffer, out int charsWritten, format);
        CellSpan(buffer[..charsWritten], ImAlign.End);
    }

    /// <summary>
    /// Draw a cell with an integer value.
    /// </summary>
    public static void Cell(int value)
    {
        Span<char> buffer = stackalloc char[16];
        value.TryFormat(buffer, out int charsWritten);
        CellSpan(buffer[..charsWritten], ImAlign.End);
    }

    private static void CellSpan(ReadOnlySpan<char> text, ImAlign align)
    {
        if (_columnIndex >= _columnCount) return;

        float colX = GetColumnX(_columnIndex);
        float colWidth = GetColumnWidth(_columnIndex);
        float rowY = _ctx.ContentY + ((_ctx.RowIndex - 1) * RowHeight) - _ctx.ScrollY;

        if (rowY + RowHeight < _ctx.ContentY || rowY > _ctx.Y + _ctx.Height)
        {
            _columnIndex++;
            return;
        }

        float textX = colX + CellPadding;
        float textY = rowY + (RowHeight - Im.Style.FontSize) / 2;

        if (align == ImAlign.End)
        {
            float textWidth = ImTextMetrics.MeasureWidth(Im.Context.Font, text, Im.Style.FontSize);
            textX = colX + colWidth - textWidth - CellPadding;
        }

        if (textY >= _ctx.ContentY && textY + Im.Style.FontSize <= _ctx.Y + _ctx.Height)
        {
            uint textColor = _ctx.RowSelected ? 0xFFFFFFFF : Im.Style.TextPrimary;
            Im.Text(text, textX, textY, Im.Style.FontSize, textColor);
        }

        _columnIndex++;
    }

    /// <summary>
    /// End the current row.
    /// </summary>
    public static void EndRow()
    {
        // Row separator could be drawn here if desired
    }

    /// <summary>
    /// End the table. Handle scrolling.
    /// </summary>
    public static void End()
    {
        ref var state = ref _tableStates[_ctx.StateIndex];

        // Calculate total content height
        float totalHeight = _ctx.RowIndex * RowHeight;
        float viewSize = _ctx.ContentHeight;
        _ctx.MaxScrollY = Math.Max(0, totalHeight - viewSize);

        // Handle mouse wheel scroll within table bounds
        var tableRect = new ImRect(_ctx.X, _ctx.Y, _ctx.Width, _ctx.Height);
        if (tableRect.Contains(Im.MousePos))
        {
            float scroll = Im.Context.Input.ScrollDelta;
            if (scroll != 0)
            {
                state.ScrollY -= scroll * RowHeight * 3;
                state.ScrollY = Math.Clamp(state.ScrollY, 0, _ctx.MaxScrollY);
            }
        }

        // Draw scrollbar using ImScrollbar widget (handles drag, page click, etc.)
        if (totalHeight > viewSize)
        {
            float scrollbarWidth = 8f;
            var scrollbarRect = new ImRect(
                _ctx.X + _ctx.Width - scrollbarWidth - 2,
                _ctx.ContentY + 2,
                scrollbarWidth,
                _ctx.ContentHeight - 4);

            int scrollbarId = Im.Context.GetId(_ctx.TableId * 31 + 999); // Unique ID for scrollbar
            ImScrollbar.DrawVertical(scrollbarId, scrollbarRect, ref state.ScrollY, viewSize, totalHeight);
        }
    }

    /// <summary>
    /// Get the current sort column index (-1 if no sort).
    /// </summary>
    public static int GetSortColumn(string tableId)
    {
        int id = Im.Context.GetId(tableId);
        int idx = FindState(id);
        return idx >= 0 ? _tableStates[idx].SortColumn : -1;
    }

    /// <summary>
    /// Get the current sort direction.
    /// </summary>
    public static bool GetSortAscending(string tableId)
    {
        int id = Im.Context.GetId(tableId);
        int idx = FindState(id);
        return idx < 0 || _tableStates[idx].SortAscending;
    }

    /// <summary>
    /// Get the selected row ID.
    /// </summary>
    public static int GetSelectedRowId(string tableId)
    {
        int id = Im.Context.GetId(tableId);
        int idx = FindState(id);
        return idx >= 0 ? _tableStates[idx].SelectedRowId : 0;
    }

    /// <summary>
    /// Set the selected row ID programmatically.
    /// </summary>
    public static void SetSelectedRowId(string tableId, int rowId)
    {
        int id = Im.Context.GetId(tableId);
        int idx = FindState(id);
        if (idx >= 0)
        {
            _tableStates[idx].SelectedRowId = rowId;
        }
    }

    private static int FindState(int id)
    {
        for (int i = 0; i < _tableStateCount; i++)
        {
            if (_tableStateIds[i] == id) return i;
        }
        return -1;
    }

    private static int FindOrCreateState(int id)
    {
        int idx = FindState(id);
        if (idx >= 0) return idx;

        if (_tableStateCount >= 64) return 0; // Fallback to first slot if full

        idx = _tableStateCount++;
        _tableStateIds[idx] = id;
        _tableStates[idx] = new TableState { SortColumn = -1, SortAscending = true };
        return idx;
    }

    private static float GetColumnX(int columnIndex)
    {
        float x = _ctx.X;
        float totalFixed = 0;
        int autoCount = 0;
        for (int i = 0; i < _columnCount; i++)
        {
            if (_columns[i].Width > 0) totalFixed += _columns[i].Width;
            else autoCount++;
        }
        float autoWidth = autoCount > 0 ? (_ctx.Width - totalFixed) / autoCount : 0;

        for (int i = 0; i < columnIndex; i++)
        {
            x += _columns[i].Width > 0 ? _columns[i].Width : autoWidth;
        }
        return x;
    }

    private static float GetColumnWidth(int columnIndex)
    {
        if (columnIndex >= _columnCount) return 0;

        ref var col = ref _columns[columnIndex];
        if (col.Width > 0) return col.Width;

        float totalFixed = 0;
        int autoCount = 0;
        for (int i = 0; i < _columnCount; i++)
        {
            if (_columns[i].Width > 0) totalFixed += _columns[i].Width;
            else autoCount++;
        }
        return autoCount > 0 ? (_ctx.Width - totalFixed) / autoCount : 0;
    }

    private static void DrawSortArrow(float x, float y, bool ascending)
    {
        float size = 4f;
        if (ascending)
        {
            // Up arrow
            Im.DrawLine(x, y + size, x + size, y - size, 1.5f, Im.Style.Primary);
            Im.DrawLine(x + size, y - size, x + size * 2, y + size, 1.5f, Im.Style.Primary);
        }
        else
        {
            // Down arrow
            Im.DrawLine(x, y - size, x + size, y + size, 1.5f, Im.Style.Primary);
            Im.DrawLine(x + size, y + size, x + size * 2, y - size, 1.5f, Im.Style.Primary);
        }
    }

    private static float MeasureTextWidth(string text)
    {
        return ImTextMetrics.MeasureWidth(Im.Context.Font, text.AsSpan(), Im.Style.FontSize);
    }
}

/// <summary>
/// Text alignment options.
/// </summary>
public enum ImAlign
{
    Start,
    Center,
    End
}
