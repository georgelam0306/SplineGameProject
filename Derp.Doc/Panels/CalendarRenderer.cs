using System.Globalization;
using System.Collections.Generic;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using Derp.Doc.Editor;
using Derp.Doc.Model;

namespace Derp.Doc.Panels;

/// <summary>
/// Calendar month-grid renderer — displays rows on a calendar based on a date text column.
/// </summary>
internal static class CalendarRenderer
{
    private const float NavBarHeight = 36f;
    private const float DayHeaderHeight = 24f;
    private const float DayCellPadding = 4f;
    private const float CardHeight = 20f;
    private const float CardGap = 2f;
    private const float MaxCardsPerDay = 3;
    private const float NavButtonWidth = 28f;
    private const float CardCornerRadius = 3f;

    private static int _displayYear = DateTime.Now.Year;
    private static int _displayMonth = DateTime.Now.Month;

    private sealed class CalendarViewState
    {
        public int DisplayYear;
        public int DisplayMonth;
    }

    private static readonly Dictionary<string, CalendarViewState> ViewStatesByStateKey = new(StringComparer.Ordinal);
    private static string _activeStateKey = "";

    private static readonly string[] DayNames = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
    private static readonly string[] MonthNames = ["", "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"];

    // Cached day-to-row mapping (index into source rows)
    private static readonly List<int>[] _dayRows = new List<int>[42]; // 6 weeks x 7 days
    private static string _cachedTableId = "";
    private static string _cachedViewId = "";
    private static int _cachedMonth;
    private static int _cachedYear;
    private static int _cachedRowCount;
    private static int _cachedProjectRevision = -1;
    private static string _cachedParentRowColumnId = "";
    private static string _cachedParentRowId = "";
    private static string _cachedTableInstanceBlockId = "";

    static CalendarRenderer()
    {
        for (int i = 0; i < _dayRows.Length; i++)
            _dayRows[i] = new List<int>();
    }

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

        // Find the date column
        DocColumn? dateCol = null;
        if (!string.IsNullOrEmpty(view.CalendarDateColumnId))
        {
            for (int i = 0; i < table.Columns.Count; i++)
            {
                if (string.Equals(table.Columns[i].Id, view.CalendarDateColumnId, StringComparison.Ordinal))
                {
                    dateCol = table.Columns[i];
                    break;
                }
            }
        }

        if (dateCol == null)
        {
            Im.Text("Calendar view requires a date column.".AsSpan(), contentRect.X + 10, contentRect.Y + 10, style.FontSize, style.TextSecondary);
            Im.Text("Set 'Date Column' in the Inspector.".AsSpan(), contentRect.X + 10, contentRect.Y + 30, style.FontSize, style.TextSecondary);
            EndStateScope();
            return;
        }

        // Find first text column for card label
        DocColumn? titleCol = null;
        for (int i = 0; i < table.Columns.Count; i++)
        {
            if (table.Columns[i].Kind == DocColumnKind.Text && table.Columns[i] != dateCol)
            {
                titleCol = table.Columns[i];
                break;
            }
        }

        // Get filtered row indices
        int[]? viewRowIndices = workspace.ComputeViewRowIndices(table, view, tableInstanceBlock);
        int rowCount = viewRowIndices?.Length ?? table.Rows.Count;
        int projectRevision = workspace.ProjectRevision;

        // Background
        Im.DrawRect(contentRect.X, contentRect.Y, contentRect.Width, contentRect.Height, style.Background);

        // Navigation bar
        float navY = contentRect.Y;
        DrawNavBar(style, contentRect.X, navY, contentRect.Width, interactive);

        // Day headers
        float gridTop = navY + NavBarHeight;
        float gridWidth = contentRect.Width;
        float dayWidth = gridWidth / 7f;

        for (int d = 0; d < 7; d++)
        {
            float hx = contentRect.X + d * dayWidth;
            Im.DrawRect(hx, gridTop, dayWidth, DayHeaderHeight, style.Surface);
            Im.DrawLine(hx, gridTop, hx, gridTop + DayHeaderHeight, 1f, style.Border);
            float textX = hx + (dayWidth - Im.MeasureTextWidth(DayNames[d].AsSpan(), style.FontSize - 1f)) * 0.5f;
            Im.Text(DayNames[d].AsSpan(), textX, gridTop + (DayHeaderHeight - style.FontSize + 1f) * 0.5f, style.FontSize - 1f, style.TextSecondary);
        }
        Im.DrawLine(contentRect.X, gridTop + DayHeaderHeight, contentRect.Right, gridTop + DayHeaderHeight, 1f, style.Border);

        // Build day-to-row mapping
        RebuildDayMapping(
            table,
            view,
            dateCol,
            viewRowIndices,
            rowCount,
            projectRevision,
            parentRowColumnId,
            parentRowId,
            tableInstanceBlock);

        // Calendar grid
        var firstOfMonth = new DateTime(_displayYear, _displayMonth, 1);
        int daysInMonth = DateTime.DaysInMonth(_displayYear, _displayMonth);
        int startDayOfWeek = ((int)firstOfMonth.DayOfWeek + 6) % 7; // Monday = 0

        float cellTop = gridTop + DayHeaderHeight;
        float cellHeight = (contentRect.Bottom - cellTop) / 6f;
        if (cellHeight < 40f) cellHeight = 40f;

        int today = DateTime.Now.Year == _displayYear && DateTime.Now.Month == _displayMonth ? DateTime.Now.Day : -1;
        Span<char> dayBuf = stackalloc char[4];
        Span<char> moreBuf = stackalloc char[16];

        for (int week = 0; week < 6; week++)
        {
            float wy = cellTop + week * cellHeight;
            for (int dow = 0; dow < 7; dow++)
            {
                int cellIndex = week * 7 + dow;
                int dayNum = cellIndex - startDayOfWeek + 1;
                float cx = contentRect.X + dow * dayWidth;

                // Cell background and border
                bool isCurrentMonth = dayNum >= 1 && dayNum <= daysInMonth;
                uint cellBg = isCurrentMonth ? style.Background : ImStyle.WithAlpha(style.Surface, 80);
                Im.DrawRect(cx, wy, dayWidth, cellHeight, cellBg);
                Im.DrawLine(cx, wy, cx, wy + cellHeight, 1f, ImStyle.WithAlpha(style.Border, 80));
                Im.DrawLine(cx, wy + cellHeight, cx + dayWidth, wy + cellHeight, 1f, ImStyle.WithAlpha(style.Border, 80));

                if (!isCurrentMonth) continue;

                // Day number
                dayNum.TryFormat(dayBuf, out int dayLen);
                bool isToday = dayNum == today;
                uint dayNumColor = isToday ? style.Primary : style.TextSecondary;
                float dayNumX = cx + DayCellPadding;
                float dayNumY = wy + DayCellPadding;

                if (isToday)
                {
                    float badgeSize = style.FontSize + 4f;
                    float badgeCenterX = dayNumX + Im.MeasureTextWidth(dayBuf[..dayLen], style.FontSize - 1f) * 0.5f;
                    Im.DrawRoundedRect(badgeCenterX - badgeSize * 0.5f, dayNumY - 2f, badgeSize, badgeSize, badgeSize * 0.5f, style.Primary);
                    dayNumColor = 0xFFFFFFFF;
                }

                Im.Text(dayBuf[..dayLen], dayNumX, dayNumY, style.FontSize - 1f, dayNumColor);

                // Draw cards for this day
                var dayRowList = _dayRows[cellIndex];
                float cardStartY = dayNumY + style.FontSize + 2f;
                int visibleCards = Math.Min(dayRowList.Count, (int)MaxCardsPerDay);

                for (int ci = 0; ci < visibleCards; ci++)
                {
                    int rowIdx = dayRowList[ci];
                    var row = table.Rows[rowIdx];
                    string label = "";
                    if (titleCol != null)
                    {
                        var titleCell = row.GetCell(titleCol);
                        label = titleCell.StringValue ?? "";
                    }
                    if (string.IsNullOrWhiteSpace(label)) label = row.Id[..Math.Min(6, row.Id.Length)];

                    float cardX = cx + DayCellPadding;
                    float cardW = dayWidth - DayCellPadding * 2f;
                    float cy2 = cardStartY + ci * (CardHeight + CardGap);

                    Im.DrawRoundedRect(cardX, cy2, cardW, CardHeight, CardCornerRadius, style.Surface);
                    Im.DrawRoundedRectStroke(cardX, cy2, cardW, CardHeight, CardCornerRadius, style.Border, 1f);

                    float labelX = cardX + 4f;
                    float labelY = cy2 + (CardHeight - style.FontSize + 2f) * 0.5f;
                    Im.Text(label.AsSpan(), labelX, labelY, style.FontSize - 2f, style.TextPrimary);

                    // Click card to select row
                    var cardRect = new ImRect(cardX, cy2, cardW, CardHeight);
                    if (interactive && cardRect.Contains(mousePos) && input.MousePressed)
                    {
                        workspace.SelectedRowIndex = rowIdx;
                    }
                }

                // "+N more" overflow
                if (dayRowList.Count > visibleCards)
                {
                    int overflow = dayRowList.Count - visibleCards;
                    int pos = 0;
                    moreBuf[pos++] = '+';
                    overflow.TryFormat(moreBuf[pos..], out int moreLen);
                    pos += moreLen;
                    " more".AsSpan().CopyTo(moreBuf[pos..]);
                    pos += 5;

                    float moreY = cardStartY + visibleCards * (CardHeight + CardGap);
                    Im.Text(moreBuf[..pos], cx + DayCellPadding, moreY, style.FontSize - 2f, style.TextSecondary);
                }
            }
        }

        EndStateScope();
    }

    private static void DrawNavBar(ImStyle style, float x, float y, float width, bool interactive)
    {
        Im.DrawRect(x, y, width, NavBarHeight, style.Surface);
        Im.DrawLine(x, y + NavBarHeight - 1, x + width, y + NavBarHeight - 1, 1f, style.Border);

        float btnY = y + (NavBarHeight - NavButtonWidth) * 0.5f;

        // Left arrow
        if (interactive && Im.Button("<", x + 8f, btnY, NavButtonWidth, NavButtonWidth))
        {
            _displayMonth--;
            if (_displayMonth < 1) { _displayMonth = 12; _displayYear--; }
        }
        else
        {
            Im.DrawRoundedRectStroke(x + 8f, btnY, NavButtonWidth, NavButtonWidth, 4f, style.Border, 1f);
            Im.Text("<".AsSpan(), x + 8f + 10f, btnY + 6f, style.FontSize, style.TextSecondary);
        }

        // Right arrow
        float rightButtonX = x + 8f + NavButtonWidth + 4f;
        if (interactive && Im.Button(">", rightButtonX, btnY, NavButtonWidth, NavButtonWidth))
        {
            _displayMonth++;
            if (_displayMonth > 12) { _displayMonth = 1; _displayYear++; }
        }
        else
        {
            Im.DrawRoundedRectStroke(rightButtonX, btnY, NavButtonWidth, NavButtonWidth, 4f, style.Border, 1f);
            Im.Text(">".AsSpan(), rightButtonX + 10f, btnY + 6f, style.FontSize, style.TextSecondary);
        }

        // Month/Year label
        string monthLabel = MonthNames[_displayMonth];
        Span<char> yearBuf = stackalloc char[32];
        int pos = 0;
        monthLabel.AsSpan().CopyTo(yearBuf[pos..]);
        pos += monthLabel.Length;
        yearBuf[pos++] = ' ';
        _displayYear.TryFormat(yearBuf[pos..], out int yearLen);
        pos += yearLen;

        float labelX = x + 8f + (NavButtonWidth + 4f) * 2 + 8f;
        float labelY = y + (NavBarHeight - style.FontSize) * 0.5f;
        Im.Text(yearBuf[..pos], labelX, labelY, style.FontSize + 2f, style.TextPrimary);

        // Today button
        float todayBtnX = x + width - 70f;
        if (interactive && Im.Button("Today", todayBtnX, btnY, 60f, NavButtonWidth))
        {
            _displayYear = DateTime.Now.Year;
            _displayMonth = DateTime.Now.Month;
        }
        else
        {
            Im.DrawRoundedRectStroke(todayBtnX, btnY, 60f, NavButtonWidth, 4f, style.Border, 1f);
            Im.Text("Today".AsSpan(), todayBtnX + 10f, btnY + 6f, style.FontSize - 1f, style.TextSecondary);
        }
    }

    private static void RebuildDayMapping(
        DocTable table,
        DocView view,
        DocColumn dateCol,
        int[]? viewRowIndices,
        int rowCount,
        int projectRevision,
        string? parentRowColumnId,
        string? parentRowId,
        DocBlock? tableInstanceBlock)
    {
        string tableId = table.Id;
        string viewId = view.Id;
        string tableInstanceBlockId = tableInstanceBlock?.Id ?? "";

        // Check cache validity
        if (string.Equals(_cachedTableId, tableId, StringComparison.Ordinal) &&
            string.Equals(_cachedViewId, viewId, StringComparison.Ordinal) &&
            string.Equals(_cachedTableInstanceBlockId, tableInstanceBlockId, StringComparison.Ordinal) &&
            _cachedMonth == _displayMonth &&
            _cachedYear == _displayYear &&
            _cachedRowCount == rowCount &&
            string.Equals(_cachedParentRowColumnId, parentRowColumnId, StringComparison.Ordinal) &&
            string.Equals(_cachedParentRowId, parentRowId, StringComparison.Ordinal) &&
            _cachedProjectRevision == projectRevision)
        {
            return;
        }

        // Clear
        for (int i = 0; i < _dayRows.Length; i++)
            _dayRows[i].Clear();

        _cachedTableId = tableId;
        _cachedViewId = viewId;
        _cachedTableInstanceBlockId = tableInstanceBlockId;
        _cachedMonth = _displayMonth;
        _cachedYear = _displayYear;
        _cachedRowCount = rowCount;
        _cachedParentRowColumnId = parentRowColumnId ?? "";
        _cachedParentRowId = parentRowId ?? "";
        _cachedProjectRevision = projectRevision;

        var firstOfMonth = new DateTime(_displayYear, _displayMonth, 1);
        int startDayOfWeek = ((int)firstOfMonth.DayOfWeek + 6) % 7;
        int daysInMonth = DateTime.DaysInMonth(_displayYear, _displayMonth);

        for (int ri = 0; ri < rowCount; ri++)
        {
            int rowIdx = viewRowIndices != null ? viewRowIndices[ri] : ri;
            var row = table.Rows[rowIdx];
            if (!RowMatchesParentFilter(row, parentRowColumnId, parentRowId))
                continue;

            var cell = row.GetCell(dateCol);
            string dateStr = cell.StringValue ?? "";

            if (!TryParseDate(dateStr, out int year, out int month, out int day))
                continue;

            if (year != _displayYear || month != _displayMonth)
                continue;

            if (day < 1 || day > daysInMonth)
                continue;

            int cellIndex = startDayOfWeek + day - 1;
            if (cellIndex >= 0 && cellIndex < _dayRows.Length)
                _dayRows[cellIndex].Add(rowIdx);
        }
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

    private static bool TryParseDate(string dateStr, out int year, out int month, out int day)
    {
        year = 0; month = 0; day = 0;

        // Try yyyy-MM-dd (ISO)
        if (DateTime.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            year = dt.Year; month = dt.Month; day = dt.Day;
            return true;
        }

        // Try MM/dd/yyyy
        if (DateTime.TryParseExact(dateStr, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
        {
            year = dt.Year; month = dt.Month; day = dt.Day;
            return true;
        }

        // Try general parse
        if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
        {
            year = dt.Year; month = dt.Month; day = dt.Day;
            return true;
        }

        return false;
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
            state = new CalendarViewState
            {
                DisplayYear = _displayYear,
                DisplayMonth = _displayMonth,
            };
            ViewStatesByStateKey[stateKey] = state;
        }

        _displayYear = state.DisplayYear;
        _displayMonth = state.DisplayMonth;
    }

    private static void EndStateScope()
    {
        if (string.IsNullOrWhiteSpace(_activeStateKey))
        {
            return;
        }

        if (ViewStatesByStateKey.TryGetValue(_activeStateKey, out var state))
        {
            state.DisplayYear = _displayYear;
            state.DisplayMonth = _displayMonth;
        }

        _activeStateKey = "";
    }
}
