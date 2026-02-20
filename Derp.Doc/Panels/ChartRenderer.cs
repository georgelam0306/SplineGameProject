using System.Numerics;
using System.Globalization;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using Derp.Doc.Editor;
using Derp.Doc.Model;

namespace Derp.Doc.Panels;

/// <summary>
/// Chart renderer — draws Bar, Line, or Pie charts based on a view's chart configuration.
/// </summary>
internal static class ChartRenderer
{
    private const float ChartPadding = 20f;
    private const float AxisLabelWidth = 50f;
    private const float AxisLabelHeight = 20f;
    private const float BarGap = 4f;
    private const float PointRadius = 4f;
    private const float LineThickness = 2f;
    private const float LegendSquareSize = 10f;
    private const float LegendRowHeight = 18f;
    private const float LegendPadding = 12f;
    private const float GridLineAlpha = 60;
    private const int MaxPieSlices = 32;

    private static readonly uint[] ChartColors =
    [
        0xFF4488FF, // blue
        0xFF44BB66, // green
        0xFFFF8844, // orange
        0xFFBB44BB, // purple
        0xFFFF4466, // red
        0xFF44CCCC, // teal
        0xFFCCBB44, // yellow
        0xFF8866DD, // violet
    ];

    private const int MaxSeries = 16;

    // Scratch buffers for data extraction (single-series)
    private static string[] _categories = new string[64];
    private static double[] _values = new double[64];
    private static int _dataCount;

    // Axis labels (column names)
    private static string _xAxisLabel = "";
    private static string _yAxisLabel = "";

    // Scratch buffers for composite (multi-series)
    private static string[] _seriesNames = new string[MaxSeries];
    private static string[] _compositeCategories = new string[64];
    private static double[][] _seriesValues = new double[MaxSeries][];
    private static bool[][] _seriesHasValue = new bool[MaxSeries][];
    private static string[] _compositeParentRowIds = new string[MaxSeries];
    private static int _seriesCount;
    private static int _compositeCategoryCount;
    private static int _compositeCacheProjectRevision = -1;
    private static int _compositeCacheLiveValueRevision = -1;
    private static string _compositeCacheParentTableId = "";
    private static string _compositeCacheParentViewId = "";
    private static string _compositeCacheParentCategoryColumnId = "";
    private static string _compositeCacheValueColumnId = "";
    private static string _compositeCacheTableInstanceBlockId = "";
    private static string _compositeCacheChildTableId = "";
    private static string _compositeCacheChildChartViewId = "";
    private static string _compositeCacheParentRowColumnId = "";
    private static string _compositeCacheChildCategoryColumnId = "";
    private static string _compositeCacheChildValueColumnId = "";
    private static int _compositeValueErrorCellCount;
    private static int _compositeCategoryErrorCellCount;
    private static int _compositeSeriesWithMissingCategoriesCount;
    private static int _compositeMissingSeriesCategoryPointCount;
    private static int _compositeNonIntegerCategoryCount;
    private static int _compositeDuplicateSeriesCategoryCount;

    private const int CompositeAllSeriesMarker = -2;

    private enum CompositeSeriesBuildResult
    {
        Success,
        NoParentRows,
        NoChildRows
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
            parentRowColumnId: null,
            parentRowId: null,
            tableInstanceBlock: null);
    }

    public static void Draw(DocWorkspace workspace, DocTable table, ImRect contentRect)
    {
        var view = table.Views.Count > 0 ? table.Views[0] : null;
        DrawInternal(
            workspace,
            table,
            view,
            contentRect,
            parentRowColumnId: null,
            parentRowId: null,
            tableInstanceBlock: null);
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
            parentRowColumnId: null,
            parentRowId: null,
            tableInstanceBlock: tableInstanceBlock);
    }

    public static void Draw(
        DocWorkspace workspace,
        DocTable table,
        DocView? view,
        ImRect contentRect,
        string? parentRowColumnId,
        string? parentRowId,
        DocBlock? tableInstanceBlock = null)
    {
        DrawInternal(workspace, table, view, contentRect, parentRowColumnId, parentRowId, tableInstanceBlock);
    }

    private static void DrawInternal(
        DocWorkspace workspace,
        DocTable? table,
        DocView? view,
        ImRect contentRect,
        string? parentRowColumnId,
        string? parentRowId,
        DocBlock? tableInstanceBlock)
    {
        if (table == null || view == null)
        {
            Im.Text("No table selected.".AsSpan(), contentRect.X + 10, contentRect.Y + 10, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }

        var resolvedView = workspace.ResolveViewConfig(table, view, tableInstanceBlock);
        if (resolvedView == null)
        {
            Im.Text("No table selected.".AsSpan(), contentRect.X + 10, contentRect.Y + 10, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }
        view = resolvedView;

        var style = Im.Style;

        // Background
        uint contentBackground = ImStyle.Lerp(style.Background, 0xFF000000, 0.24f);
        Im.DrawRect(contentRect.X, contentRect.Y, contentRect.Width, contentRect.Height, contentBackground);

        // Resolve chart config
        var chartKind = view.ChartKind ?? DocChartKind.Bar;

        // Find category and value columns
        DocColumn? catCol = null;
        DocColumn? valCol = null;
        for (int i = 0; i < table.Columns.Count; i++)
        {
            if (string.Equals(table.Columns[i].Id, view.ChartCategoryColumnId, StringComparison.Ordinal))
                catCol = table.Columns[i];
            if (string.Equals(table.Columns[i].Id, view.ChartValueColumnId, StringComparison.Ordinal))
                valCol = table.Columns[i];
        }

        if (valCol == null || (valCol.Kind != DocColumnKind.Number && valCol.Kind != DocColumnKind.Formula && valCol.Kind != DocColumnKind.Subtable))
        {
            Im.Text("Chart requires a Number or Subtable column for values.".AsSpan(), contentRect.X + 10, contentRect.Y + 10, style.FontSize, style.TextSecondary);
            Im.Text("Set 'Value Column' in the Inspector.".AsSpan(), contentRect.X + 10, contentRect.Y + 30, style.FontSize, style.TextSecondary);
            return;
        }

        // Composite chart: value column is a Subtable → multi-series
        if (valCol.Kind == DocColumnKind.Subtable)
        {
            DrawCompositeChart(
                workspace,
                table,
                view,
                catCol,
                valCol,
                chartKind,
                contentRect,
                style,
                parentRowColumnId,
                parentRowId,
                tableInstanceBlock);
            return;
        }

        // Extract data
        int[]? viewRowIndices = workspace.ComputeViewRowIndices(table, view, tableInstanceBlock);
        int rowCount = viewRowIndices?.Length ?? table.Rows.Count;

        if (rowCount == 0)
        {
            Im.Text("No data to chart.".AsSpan(), contentRect.X + 10, contentRect.Y + 10, style.FontSize, style.TextSecondary);
            return;
        }

        // Ensure scratch capacity
        if (_categories.Length < rowCount)
        {
            _categories = new string[rowCount];
            _values = new double[rowCount];
        }

        _dataCount = 0;
        for (int ri = 0; ri < rowCount; ri++)
        {
            int rowIdx = viewRowIndices != null ? viewRowIndices[ri] : ri;
            var row = table.Rows[rowIdx];
            if (!RowMatchesParentFilter(row, parentRowColumnId, parentRowId))
            {
                continue;
            }

            DocCellValue catCell = default;
            if (catCol != null)
            {
                catCell = row.GetCell(catCol);
            }
            string label = ResolveCategoryLabel(workspace, catCol, catCell, ri + 1);

            var valCell = row.GetCell(valCol);
            double value = valCell.NumberValue;

            _categories[_dataCount] = label;
            _values[_dataCount] = value;
            _dataCount++;
        }

        if (_dataCount <= 0)
        {
            Im.Text("No data to chart.".AsSpan(), contentRect.X + 10, contentRect.Y + 10, style.FontSize, style.TextSecondary);
            return;
        }

        _xAxisLabel = catCol?.Name ?? "";
        _yAxisLabel = valCol.Name;

        // Sort data by category value for line/area charts when categories are numeric
        if (chartKind is DocChartKind.Line or DocChartKind.Area && _dataCount > 1)
        {
            SortDataByCategory();
        }

        switch (chartKind)
        {
            case DocChartKind.Bar:
                DrawBarChart(contentRect, style);
                break;
            case DocChartKind.Line:
                DrawLineChart(contentRect, style);
                break;
            case DocChartKind.Pie:
                DrawPieChart(contentRect, style);
                break;
            case DocChartKind.Area:
                DrawAreaChart(contentRect, style);
                break;
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

    private static void DrawBarChart(ImRect rect, ImStyle style)
    {
        float chartLeft = rect.X + ChartPadding + AxisLabelWidth;
        float chartRight = rect.Right - ChartPadding;
        float chartTop = rect.Y + ChartPadding;
        float chartBottom = rect.Bottom - ChartPadding - AxisLabelHeight;

        float chartWidth = chartRight - chartLeft;
        float chartHeight = chartBottom - chartTop;
        if (chartWidth < 20f || chartHeight < 20f) return;

        // Find min/max values
        double minVal = 0;
        double maxVal = 0;
        for (int i = 0; i < _dataCount; i++)
        {
            if (_values[i] > maxVal) maxVal = _values[i];
            if (_values[i] < minVal) minVal = _values[i];
        }
        if (maxVal == minVal) maxVal = minVal + 1;

        // Draw grid lines
        DrawHorizontalGridLines(chartLeft, chartRight, chartTop, chartBottom, minVal, maxVal, style);

        // Draw bars
        float barWidth = (chartWidth - BarGap * (_dataCount + 1)) / _dataCount;
        if (barWidth < 2f) barWidth = 2f;
        float zeroY = (float)(chartBottom - (-minVal / (maxVal - minVal)) * chartHeight);
        if (minVal >= 0) zeroY = chartBottom;
        int labelStep = ComputeCategoryLabelStep(_categories, _dataCount, chartWidth, style.FontSize - 2f, categoriesArePoints: false);

        for (int i = 0; i < _dataCount; i++)
        {
            float barX = chartLeft + BarGap + i * (barWidth + BarGap);
            float valNorm = (float)((_values[i] - minVal) / (maxVal - minVal));
            float barTop = chartBottom - valNorm * chartHeight;
            float barH = zeroY - barTop;

            uint color = ChartColors[i % ChartColors.Length];

            if (barH >= 0)
            {
                Im.DrawRoundedRect(barX, barTop, barWidth, barH, 2f, color);
            }
            else
            {
                Im.DrawRoundedRect(barX, zeroY, barWidth, -barH, 2f, color);
            }

            if (ShouldDrawCategoryLabel(i, _dataCount, labelStep))
            {
                float labelW = Im.MeasureTextWidth(_categories[i].AsSpan(), style.FontSize - 2f);
                float labelX = barX + (barWidth - labelW) * 0.5f;
                Im.Text(_categories[i].AsSpan(), labelX, chartBottom + 4f, style.FontSize - 2f, style.TextSecondary);
            }
        }

        // Axes
        Im.DrawLine(chartLeft, chartTop, chartLeft, chartBottom, 1f, style.Border);
        Im.DrawLine(chartLeft, chartBottom, chartRight, chartBottom, 1f, style.Border);

        // Axis labels
        DrawAxisLabels(chartLeft, chartRight, chartTop, chartBottom, rect, style);
    }

    private static void DrawLineChart(ImRect rect, ImStyle style)
    {
        float chartLeft = rect.X + ChartPadding + AxisLabelWidth;
        float chartRight = rect.Right - ChartPadding;
        float chartTop = rect.Y + ChartPadding;
        float chartBottom = rect.Bottom - ChartPadding - AxisLabelHeight;

        float chartWidth = chartRight - chartLeft;
        float chartHeight = chartBottom - chartTop;
        if (chartWidth < 20f || chartHeight < 20f) return;

        // Find min/max
        double minVal = double.MaxValue;
        double maxVal = double.MinValue;
        for (int i = 0; i < _dataCount; i++)
        {
            if (_values[i] > maxVal) maxVal = _values[i];
            if (_values[i] < minVal) minVal = _values[i];
        }
        if (maxVal == minVal) { maxVal += 1; minVal -= 1; }

        // Draw grid lines
        DrawHorizontalGridLines(chartLeft, chartRight, chartTop, chartBottom, minVal, maxVal, style);

        // Draw line segments + points
        uint lineColor = ChartColors[0];
        float stepX = _dataCount > 1 ? chartWidth / (_dataCount - 1) : 0f;
        int labelStep = ComputeCategoryLabelStep(_categories, _dataCount, chartWidth, style.FontSize - 2f, categoriesArePoints: true);

        float prevX = 0f, prevY = 0f;
        for (int i = 0; i < _dataCount; i++)
        {
            float px = _dataCount > 1 ? chartLeft + i * stepX : chartLeft + chartWidth * 0.5f;
            float valNorm = (float)((_values[i] - minVal) / (maxVal - minVal));
            float py = chartBottom - valNorm * chartHeight;

            if (i > 0)
            {
                Im.DrawLine(prevX, prevY, px, py, LineThickness, lineColor);
            }

            Im.DrawCircle(px, py, PointRadius, lineColor);

            if (ShouldDrawCategoryLabel(i, _dataCount, labelStep))
            {
                float labelW = Im.MeasureTextWidth(_categories[i].AsSpan(), style.FontSize - 2f);
                float labelX = px - labelW * 0.5f;
                Im.Text(_categories[i].AsSpan(), labelX, chartBottom + 4f, style.FontSize - 2f, style.TextSecondary);
            }

            prevX = px;
            prevY = py;
        }

        // Axes
        Im.DrawLine(chartLeft, chartTop, chartLeft, chartBottom, 1f, style.Border);
        Im.DrawLine(chartLeft, chartBottom, chartRight, chartBottom, 1f, style.Border);

        // Axis labels
        DrawAxisLabels(chartLeft, chartRight, chartTop, chartBottom, rect, style);
    }

    private static void DrawAreaChart(ImRect rect, ImStyle style)
    {
        float chartLeft = rect.X + ChartPadding + AxisLabelWidth;
        float chartRight = rect.Right - ChartPadding;
        float chartTop = rect.Y + ChartPadding;
        float chartBottom = rect.Bottom - ChartPadding - AxisLabelHeight;

        float chartWidth = chartRight - chartLeft;
        float chartHeight = chartBottom - chartTop;
        if (chartWidth < 20f || chartHeight < 20f) return;

        double minVal = double.MaxValue, maxVal = double.MinValue;
        for (int i = 0; i < _dataCount; i++)
        {
            if (_values[i] > maxVal) maxVal = _values[i];
            if (_values[i] < minVal) minVal = _values[i];
        }
        if (minVal > 0) minVal = 0; // Area charts should start from 0
        if (maxVal == minVal) { maxVal += 1; }

        DrawHorizontalGridLines(chartLeft, chartRight, chartTop, chartBottom, minVal, maxVal, style);

        uint lineColor = ChartColors[0];
        uint fillColor = (lineColor & 0x00FFFFFF) | 0x40000000; // ~25% alpha
        float stepX = _dataCount > 1 ? chartWidth / (_dataCount - 1) : 0f;
        int labelStep = ComputeCategoryLabelStep(_categories, _dataCount, chartWidth, style.FontSize - 2f, categoriesArePoints: true);

        // Build polygon: data points + baseline
        Span<Vector2> poly = stackalloc Vector2[_dataCount + 2];
        for (int i = 0; i < _dataCount; i++)
        {
            float px = _dataCount > 1 ? chartLeft + i * stepX : chartLeft + chartWidth * 0.5f;
            float valNorm = (float)((_values[i] - minVal) / (maxVal - minVal));
            float py = chartBottom - valNorm * chartHeight;
            poly[i] = new Vector2(px, py);
        }
        // Close polygon along baseline
        poly[_dataCount] = new Vector2(poly[_dataCount - 1].X, chartBottom);
        poly[_dataCount + 1] = new Vector2(poly[0].X, chartBottom);

        Im.DrawFilledPolygon(poly, fillColor);

        // Draw line on top
        if (_dataCount >= 2)
        {
            Im.DrawPolyline(poly[.._dataCount], LineThickness, lineColor);
        }

        for (int i = 0; i < _dataCount; i++)
        {
            if (ShouldDrawCategoryLabel(i, _dataCount, labelStep))
            {
                float labelW = Im.MeasureTextWidth(_categories[i].AsSpan(), style.FontSize - 2f);
                float labelX = poly[i].X - labelW * 0.5f;
                Im.Text(_categories[i].AsSpan(), labelX, chartBottom + 4f, style.FontSize - 2f, style.TextSecondary);
            }
        }

        Im.DrawLine(chartLeft, chartTop, chartLeft, chartBottom, 1f, style.Border);
        Im.DrawLine(chartLeft, chartBottom, chartRight, chartBottom, 1f, style.Border);
        DrawAxisLabels(chartLeft, chartRight, chartTop, chartBottom, rect, style);
    }

    private static void DrawPieChart(ImRect rect, ImStyle style)
    {
        float pieCenterX = rect.X + rect.Width * 0.4f;
        float pieCenterY = rect.Y + rect.Height * 0.5f;
        float pieRadius = Math.Min(rect.Width * 0.35f, rect.Height * 0.4f) - ChartPadding;
        if (pieRadius < 10f) return;

        // Sum positive values
        double total = 0;
        for (int i = 0; i < _dataCount; i++)
        {
            if (_values[i] > 0) total += _values[i];
        }

        if (total <= 0)
        {
            Im.Text("No positive values to chart.".AsSpan(), rect.X + 10, rect.Y + 10, style.FontSize, style.TextSecondary);
            return;
        }

        // Draw wedges as filled triangle fans
        float startAngle = -MathF.PI / 2f; // Start at top
        int sliceCount = Math.Min(_dataCount, MaxPieSlices);
        Span<Vector2> triPoints = stackalloc Vector2[3];

        for (int i = 0; i < sliceCount; i++)
        {
            if (_values[i] <= 0) continue;

            float sweepAngle = (float)(_values[i] / total) * MathF.PI * 2f;
            uint color = ChartColors[i % ChartColors.Length];

            // Draw wedge as a series of thin triangles
            int segments = Math.Max(4, (int)(sweepAngle / 0.1f));
            float angleStep = sweepAngle / segments;

            for (int s = 0; s < segments; s++)
            {
                float a0 = startAngle + s * angleStep;
                float a1 = startAngle + (s + 1) * angleStep;

                triPoints[0] = new Vector2(pieCenterX, pieCenterY);
                triPoints[1] = new Vector2(pieCenterX + MathF.Cos(a0) * pieRadius, pieCenterY + MathF.Sin(a0) * pieRadius);
                triPoints[2] = new Vector2(pieCenterX + MathF.Cos(a1) * pieRadius, pieCenterY + MathF.Sin(a1) * pieRadius);

                Im.DrawFilledPolygon(triPoints, color);
            }

            startAngle += sweepAngle;
        }

        // Draw legend to the right
        float legendX = rect.X + rect.Width * 0.7f;
        float legendY = rect.Y + ChartPadding;
        Span<char> pctBuf = stackalloc char[16];

        for (int i = 0; i < sliceCount; i++)
        {
            if (_values[i] <= 0) continue;

            uint color = ChartColors[i % ChartColors.Length];
            Im.DrawRoundedRect(legendX, legendY + (LegendRowHeight - LegendSquareSize) * 0.5f, LegendSquareSize, LegendSquareSize, 2f, color);
            Im.Text(_categories[i].AsSpan(), legendX + LegendSquareSize + 6f, legendY + (LegendRowHeight - style.FontSize) * 0.5f, style.FontSize - 1f, style.TextPrimary);

            // Percentage
            float pct = (float)(_values[i] / total * 100.0);
            pct.TryFormat(pctBuf, out int pctLen, "F1");
            pctBuf[pctLen++] = '%';
            float pctX = legendX + LegendSquareSize + 6f + Im.MeasureTextWidth(_categories[i].AsSpan(), style.FontSize - 1f) + 6f;
            Im.Text(pctBuf[..pctLen], pctX, legendY + (LegendRowHeight - style.FontSize) * 0.5f, style.FontSize - 2f, style.TextSecondary);

            legendY += LegendRowHeight;
        }
    }

    // --- Composite (Multi-Series) Chart ---

    private static void DrawCompositeChart(
        DocWorkspace workspace, DocTable parentTable, DocView parentView,
        DocColumn? catCol, DocColumn valCol, DocChartKind chartKind,
        ImRect contentRect, ImStyle style,
        string? previewParentRowColumnId,
        string? previewParentRowId,
        DocBlock? tableInstanceBlock)
    {
        _ = previewParentRowColumnId;
        _ = previewParentRowId;

        // Find child table
        if (string.IsNullOrEmpty(valCol.SubtableId))
        {
            Im.Text("Subtable column has no linked table.".AsSpan(), contentRect.X + 10, contentRect.Y + 10, style.FontSize, style.TextSecondary);
            return;
        }

        DocTable? childTable = null;
        for (int i = 0; i < workspace.Project.Tables.Count; i++)
        {
            if (string.Equals(workspace.Project.Tables[i].Id, valCol.SubtableId, StringComparison.Ordinal))
            {
                childTable = workspace.Project.Tables[i];
                break;
            }
        }

        if (childTable == null)
        {
            Im.Text("Child table not found.".AsSpan(), contentRect.X + 10, contentRect.Y + 10, style.FontSize, style.TextSecondary);
            return;
        }

        // Find child table's first Chart view to get category/value column config
        DocView? childChartView = null;
        for (int i = 0; i < childTable.Views.Count; i++)
        {
            if (childTable.Views[i].Type == DocViewType.Chart)
            {
                childChartView = childTable.Views[i];
                break;
            }
        }

        if (childChartView == null)
        {
            Im.Text("Configure a Chart view on the subtable first.".AsSpan(), contentRect.X + 10, contentRect.Y + 10, style.FontSize, style.TextSecondary);
            return;
        }

        // Resolve child chart's category and value columns
        DocColumn? childCatCol = null;
        DocColumn? childValCol = null;
        for (int i = 0; i < childTable.Columns.Count; i++)
        {
            if (string.Equals(childTable.Columns[i].Id, childChartView.ChartCategoryColumnId, StringComparison.Ordinal))
                childCatCol = childTable.Columns[i];
            if (string.Equals(childTable.Columns[i].Id, childChartView.ChartValueColumnId, StringComparison.Ordinal))
                childValCol = childTable.Columns[i];
        }

        if (childValCol == null || (childValCol.Kind != DocColumnKind.Number && childValCol.Kind != DocColumnKind.Formula))
        {
            Im.Text("Subtable's Chart view needs a Number value column.".AsSpan(), contentRect.X + 10, contentRect.Y + 10, style.FontSize, style.TextSecondary);
            return;
        }

        // Find the _parentRowId column in child table
        string? parentRowColId = childTable.ParentRowColumnId;
        CompositeSeriesBuildResult buildResult = EnsureCompositeSeriesData(
            workspace,
            parentTable,
            parentView,
            catCol,
            valCol,
            childTable,
            childChartView,
            childCatCol,
            childValCol,
            parentRowColId,
            tableInstanceBlock);

        if (buildResult == CompositeSeriesBuildResult.NoParentRows)
        {
            Im.Text("No data to chart.".AsSpan(), contentRect.X + 10, contentRect.Y + 10, style.FontSize, style.TextSecondary);
            return;
        }

        if (buildResult == CompositeSeriesBuildResult.NoChildRows)
        {
            Im.Text("No subtable data to chart.".AsSpan(), contentRect.X + 10, contentRect.Y + 10, style.FontSize, style.TextSecondary);
            return;
        }

        // Set axis labels from child chart's columns
        _xAxisLabel = childCatCol?.Name ?? "";
        _yAxisLabel = childValCol.Name;

        // Reserve space for legend on the right
        float legendWidth = 120f;
        var chartRect = new ImRect(contentRect.X, contentRect.Y, contentRect.Width - legendWidth, contentRect.Height);

        switch (chartKind)
        {
            case DocChartKind.Bar:
                DrawCompositeBarChart(chartRect, style);
                break;
            case DocChartKind.Line:
                DrawCompositeLineChart(chartRect, style);
                break;
            case DocChartKind.Area:
                DrawCompositeAreaChart(chartRect, style);
                break;
            case DocChartKind.Pie:
                // Pie composite: draw one pie per series (stacked horizontally)
                DrawCompositePieChart(contentRect, style);
                return; // legend is drawn inside
        }

        DrawCompositeDataDiagnostics(chartRect, style);

        // Draw legend
        float legendX = chartRect.Right + 8f;
        float legendY = contentRect.Y + ChartPadding;
        for (int s = 0; s < _seriesCount; s++)
        {
            uint color = ChartColors[s % ChartColors.Length];
            Im.DrawRoundedRect(legendX, legendY + (LegendRowHeight - LegendSquareSize) * 0.5f, LegendSquareSize, LegendSquareSize, 2f, color);
            Im.Text(_seriesNames[s].AsSpan(), legendX + LegendSquareSize + 6f, legendY + (LegendRowHeight - style.FontSize) * 0.5f, style.FontSize - 1f, style.TextPrimary);
            legendY += LegendRowHeight;
        }
    }

    private static CompositeSeriesBuildResult EnsureCompositeSeriesData(
        DocWorkspace workspace,
        DocTable parentTable,
        DocView parentView,
        DocColumn? parentCategoryColumn,
        DocColumn valueColumn,
        DocTable childTable,
        DocView childChartView,
        DocColumn? childCategoryColumn,
        DocColumn childValueColumn,
        string? parentRowColumnId,
        DocBlock? tableInstanceBlock)
    {
        if (IsCompositeSeriesCacheValid(
            workspace,
            parentTable,
            parentView,
            parentCategoryColumn,
            valueColumn,
            childTable,
            childChartView,
            childCategoryColumn,
            childValueColumn,
            parentRowColumnId,
            tableInstanceBlock))
        {
            if (_seriesCount <= 0)
            {
                return CompositeSeriesBuildResult.NoParentRows;
            }

            return _compositeCategoryCount > 0
                ? CompositeSeriesBuildResult.Success
                : CompositeSeriesBuildResult.NoChildRows;
        }

        CompositeSeriesBuildResult buildResult = RebuildCompositeSeriesData(
            workspace,
            parentTable,
            parentView,
            parentCategoryColumn,
            childTable,
            childCategoryColumn,
            childValueColumn,
            parentRowColumnId,
            tableInstanceBlock);

        UpdateCompositeSeriesCacheKey(
            workspace,
            parentTable,
            parentView,
            parentCategoryColumn,
            valueColumn,
            childTable,
            childChartView,
            childCategoryColumn,
            childValueColumn,
            parentRowColumnId,
            tableInstanceBlock);

        return buildResult;
    }

    private static bool IsCompositeSeriesCacheValid(
        DocWorkspace workspace,
        DocTable parentTable,
        DocView parentView,
        DocColumn? parentCategoryColumn,
        DocColumn valueColumn,
        DocTable childTable,
        DocView childChartView,
        DocColumn? childCategoryColumn,
        DocColumn childValueColumn,
        string? parentRowColumnId,
        DocBlock? tableInstanceBlock)
    {
        string tableInstanceBlockId = tableInstanceBlock?.Id ?? "";
        return _compositeCacheProjectRevision == workspace.ProjectRevision &&
               _compositeCacheLiveValueRevision == workspace.LiveValueRevision &&
               string.Equals(_compositeCacheParentTableId, parentTable.Id, StringComparison.Ordinal) &&
               string.Equals(_compositeCacheParentViewId, parentView.Id, StringComparison.Ordinal) &&
               string.Equals(_compositeCacheParentCategoryColumnId, parentCategoryColumn?.Id ?? "", StringComparison.Ordinal) &&
               string.Equals(_compositeCacheValueColumnId, valueColumn.Id, StringComparison.Ordinal) &&
               string.Equals(_compositeCacheTableInstanceBlockId, tableInstanceBlockId, StringComparison.Ordinal) &&
               string.Equals(_compositeCacheChildTableId, childTable.Id, StringComparison.Ordinal) &&
               string.Equals(_compositeCacheChildChartViewId, childChartView.Id, StringComparison.Ordinal) &&
               string.Equals(_compositeCacheParentRowColumnId, parentRowColumnId ?? "", StringComparison.Ordinal) &&
               string.Equals(_compositeCacheChildCategoryColumnId, childCategoryColumn?.Id ?? "", StringComparison.Ordinal) &&
               string.Equals(_compositeCacheChildValueColumnId, childValueColumn.Id, StringComparison.Ordinal);
    }

    private static void UpdateCompositeSeriesCacheKey(
        DocWorkspace workspace,
        DocTable parentTable,
        DocView parentView,
        DocColumn? parentCategoryColumn,
        DocColumn valueColumn,
        DocTable childTable,
        DocView childChartView,
        DocColumn? childCategoryColumn,
        DocColumn childValueColumn,
        string? parentRowColumnId,
        DocBlock? tableInstanceBlock)
    {
        _compositeCacheProjectRevision = workspace.ProjectRevision;
        _compositeCacheLiveValueRevision = workspace.LiveValueRevision;
        _compositeCacheParentTableId = parentTable.Id;
        _compositeCacheParentViewId = parentView.Id;
        _compositeCacheParentCategoryColumnId = parentCategoryColumn?.Id ?? "";
        _compositeCacheValueColumnId = valueColumn.Id;
        _compositeCacheTableInstanceBlockId = tableInstanceBlock?.Id ?? "";
        _compositeCacheChildTableId = childTable.Id;
        _compositeCacheChildChartViewId = childChartView.Id;
        _compositeCacheParentRowColumnId = parentRowColumnId ?? "";
        _compositeCacheChildCategoryColumnId = childCategoryColumn?.Id ?? "";
        _compositeCacheChildValueColumnId = childValueColumn.Id;
    }

    private static CompositeSeriesBuildResult RebuildCompositeSeriesData(
        DocWorkspace workspace,
        DocTable parentTable,
        DocView parentView,
        DocColumn? parentCategoryColumn,
        DocTable childTable,
        DocColumn? childCategoryColumn,
        DocColumn childValueColumn,
        string? parentRowColumnId,
        DocBlock? tableInstanceBlock)
    {
        ResetCompositeDataDiagnostics();
        _seriesCount = 0;
        _compositeCategoryCount = 0;

        int[]? viewRowIndices = workspace.ComputeViewRowIndices(parentTable, parentView, tableInstanceBlock);
        int parentRowCount = viewRowIndices?.Length ?? parentTable.Rows.Count;
        if (parentRowCount == 0)
        {
            return CompositeSeriesBuildResult.NoParentRows;
        }

        int maxSeries = Math.Min(parentRowCount, MaxSeries);
        for (int seriesIndex = 0; seriesIndex < maxSeries; seriesIndex++)
        {
            int sourceRowIndex = viewRowIndices != null ? viewRowIndices[seriesIndex] : seriesIndex;
            var parentRow = parentTable.Rows[sourceRowIndex];
            _compositeParentRowIds[seriesIndex] = parentRow.Id;

            DocCellValue labelCell = default;
            if (parentCategoryColumn != null)
            {
                labelCell = parentRow.GetCell(parentCategoryColumn);
            }

            _seriesNames[seriesIndex] = ResolveCategoryLabel(
                workspace,
                parentCategoryColumn,
                labelCell,
                seriesIndex + 1);
            _seriesCount++;
        }

        for (int childRowIndex = 0; childRowIndex < childTable.Rows.Count; childRowIndex++)
        {
            var childRow = childTable.Rows[childRowIndex];
            int resolvedSeriesIndex = ResolveCompositeSeriesIndexForChildRow(
                childRow,
                parentRowColumnId,
                _seriesCount);
            if (resolvedSeriesIndex == -1)
            {
                continue;
            }

            DocCellValue categoryCell = default;
            if (childCategoryColumn != null)
            {
                categoryCell = childRow.GetCell(childCategoryColumn);
            }

            if (IsFormulaErrorCell(categoryCell))
            {
                _compositeCategoryErrorCellCount++;
                continue;
            }

            if (IsNumericCategoryColumn(childCategoryColumn) &&
                IsFiniteNonInteger(categoryCell.NumberValue))
            {
                _compositeNonIntegerCategoryCount++;
            }

            string category = ResolveCategoryLabel(
                workspace,
                childCategoryColumn,
                categoryCell,
                childRowIndex + 1);
            InsertCompositeCategoryIfMissing(category);
        }

        if (_compositeCategoryCount == 0)
        {
            return CompositeSeriesBuildResult.NoChildRows;
        }

        SortCompositeCategories();

        for (int seriesIndex = 0; seriesIndex < _seriesCount; seriesIndex++)
        {
            if (_seriesValues[seriesIndex] == null || _seriesValues[seriesIndex].Length < _compositeCategoryCount)
            {
                _seriesValues[seriesIndex] = new double[_compositeCategoryCount];
            }
            if (_seriesHasValue[seriesIndex] == null || _seriesHasValue[seriesIndex].Length < _compositeCategoryCount)
            {
                _seriesHasValue[seriesIndex] = new bool[_compositeCategoryCount];
            }

            Array.Clear(_seriesValues[seriesIndex], 0, _compositeCategoryCount);
            Array.Clear(_seriesHasValue[seriesIndex], 0, _compositeCategoryCount);
        }

        for (int childRowIndex = 0; childRowIndex < childTable.Rows.Count; childRowIndex++)
        {
            var childRow = childTable.Rows[childRowIndex];
            int resolvedSeriesIndex = ResolveCompositeSeriesIndexForChildRow(
                childRow,
                parentRowColumnId,
                _seriesCount);
            if (resolvedSeriesIndex == -1)
            {
                continue;
            }

            DocCellValue categoryCell = default;
            if (childCategoryColumn != null)
            {
                categoryCell = childRow.GetCell(childCategoryColumn);
            }

            string category = ResolveCategoryLabel(
                workspace,
                childCategoryColumn,
                categoryCell,
                childRowIndex + 1);
            int categoryIndex = FindCompositeCategoryIndex(category);
            if (categoryIndex < 0)
            {
                continue;
            }

            DocCellValue valueCell = childRow.GetCell(childValueColumn);
            if (IsFormulaErrorCell(valueCell))
            {
                _compositeValueErrorCellCount++;
                continue;
            }

            double value = valueCell.NumberValue;
            if (resolvedSeriesIndex == CompositeAllSeriesMarker)
            {
                for (int seriesIndex = 0; seriesIndex < _seriesCount; seriesIndex++)
                {
                    if (_seriesHasValue[seriesIndex][categoryIndex])
                    {
                        _compositeDuplicateSeriesCategoryCount++;
                    }

                    _seriesValues[seriesIndex][categoryIndex] = value;
                    _seriesHasValue[seriesIndex][categoryIndex] = true;
                }
            }
            else
            {
                if (_seriesHasValue[resolvedSeriesIndex][categoryIndex])
                {
                    _compositeDuplicateSeriesCategoryCount++;
                }

                _seriesValues[resolvedSeriesIndex][categoryIndex] = value;
                _seriesHasValue[resolvedSeriesIndex][categoryIndex] = true;
            }
        }

        for (int seriesIndex = 0; seriesIndex < _seriesCount; seriesIndex++)
        {
            int missingCount = 0;
            for (int categoryIndex = 0; categoryIndex < _compositeCategoryCount; categoryIndex++)
            {
                if (!_seriesHasValue[seriesIndex][categoryIndex])
                {
                    missingCount++;
                }
            }

            if (missingCount > 0)
            {
                _compositeSeriesWithMissingCategoriesCount++;
                _compositeMissingSeriesCategoryPointCount += missingCount;
            }
        }

        return CompositeSeriesBuildResult.Success;
    }

    private static int ResolveCompositeSeriesIndexForChildRow(
        DocRow childRow,
        string? parentRowColumnId,
        int seriesCount)
    {
        if (seriesCount <= 0)
        {
            return -1;
        }

        if (string.IsNullOrEmpty(parentRowColumnId))
        {
            return CompositeAllSeriesMarker;
        }

        string parentRowId = childRow.GetCell(parentRowColumnId).StringValue ?? "";
        if (string.IsNullOrEmpty(parentRowId))
        {
            return -1;
        }

        for (int seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
        {
            if (string.Equals(_compositeParentRowIds[seriesIndex], parentRowId, StringComparison.Ordinal))
            {
                return seriesIndex;
            }
        }

        return -1;
    }

    private static void InsertCompositeCategoryIfMissing(string category)
    {
        if (FindCompositeCategoryIndex(category) >= 0)
        {
            return;
        }

        if (_compositeCategoryCount >= _compositeCategories.Length)
        {
            int newSize = Math.Max(_compositeCategoryCount + 1, _compositeCategories.Length * 2);
            int targetSize = _compositeCategories.Length == 0
                ? Math.Max(64, newSize)
                : newSize;
            var expanded = new string[targetSize];
            if (_compositeCategoryCount > 0)
            {
                Array.Copy(_compositeCategories, expanded, _compositeCategoryCount);
            }

            _compositeCategories = expanded;
        }

        _compositeCategories[_compositeCategoryCount] = category;
        _compositeCategoryCount++;
    }

    private static int FindCompositeCategoryIndex(string category)
    {
        for (int categoryIndex = 0; categoryIndex < _compositeCategoryCount; categoryIndex++)
        {
            if (string.Equals(_compositeCategories[categoryIndex], category, StringComparison.Ordinal))
            {
                return categoryIndex;
            }
        }

        return -1;
    }

    private static bool IsFormulaErrorCell(in DocCellValue cell)
    {
        string? text = cell.StringValue;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return text.StartsWith("#ERR", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNumericCategoryColumn(DocColumn? categoryColumn)
    {
        return categoryColumn != null &&
               (categoryColumn.Kind == DocColumnKind.Number || categoryColumn.Kind == DocColumnKind.Formula);
    }

    private static bool IsFiniteNonInteger(double value)
    {
        if (!double.IsFinite(value))
        {
            return false;
        }

        double rounded = Math.Round(value);
        return Math.Abs(value - rounded) > 0.000001;
    }

    private static void ResetCompositeDataDiagnostics()
    {
        _compositeValueErrorCellCount = 0;
        _compositeCategoryErrorCellCount = 0;
        _compositeSeriesWithMissingCategoriesCount = 0;
        _compositeMissingSeriesCategoryPointCount = 0;
        _compositeNonIntegerCategoryCount = 0;
        _compositeDuplicateSeriesCategoryCount = 0;
    }

    private static void DrawCompositeDataDiagnostics(ImRect chartRect, ImStyle style)
    {
        if (_compositeValueErrorCellCount <= 0 &&
            _compositeCategoryErrorCellCount <= 0 &&
            _compositeSeriesWithMissingCategoriesCount <= 0 &&
            _compositeMissingSeriesCategoryPointCount <= 0 &&
            _compositeNonIntegerCategoryCount <= 0 &&
            _compositeDuplicateSeriesCategoryCount <= 0)
        {
            return;
        }

        float fontSize = Math.Max(10f, style.FontSize - 2f);
        float lineHeight = fontSize + 1f;
        float x = chartRect.X + ChartPadding + AxisLabelWidth + 6f;
        float y = chartRect.Y + 4f;

        Im.Text("Data issues detected:".AsSpan(), x, y, fontSize, style.Primary);
        y += lineHeight;

        if (_compositeValueErrorCellCount > 0)
        {
            y = DrawCompositeDiagnosticCountLine(
                x,
                y,
                fontSize,
                style.TextSecondary,
                "value #ERR cells: ".AsSpan(),
                _compositeValueErrorCellCount);
        }

        if (_compositeCategoryErrorCellCount > 0)
        {
            y = DrawCompositeDiagnosticCountLine(
                x,
                y,
                fontSize,
                style.TextSecondary,
                "category #ERR cells: ".AsSpan(),
                _compositeCategoryErrorCellCount);
        }

        if (_compositeSeriesWithMissingCategoriesCount > 0)
        {
            y = DrawCompositeDiagnosticCountLine(
                x,
                y,
                fontSize,
                style.TextSecondary,
                "series missing categories: ".AsSpan(),
                _compositeSeriesWithMissingCategoriesCount);
        }

        if (_compositeMissingSeriesCategoryPointCount > 0)
        {
            y = DrawCompositeDiagnosticCountLine(
                x,
                y,
                fontSize,
                style.TextSecondary,
                "missing series-category points: ".AsSpan(),
                _compositeMissingSeriesCategoryPointCount);
        }

        if (_compositeNonIntegerCategoryCount > 0)
        {
            y = DrawCompositeDiagnosticCountLine(
                x,
                y,
                fontSize,
                style.TextSecondary,
                "non-integer numeric categories: ".AsSpan(),
                _compositeNonIntegerCategoryCount);
        }

        if (_compositeDuplicateSeriesCategoryCount > 0)
        {
            DrawCompositeDiagnosticCountLine(
                x,
                y,
                fontSize,
                style.TextSecondary,
                "duplicate series-category rows: ".AsSpan(),
                _compositeDuplicateSeriesCategoryCount);
        }
    }

    private static float DrawCompositeDiagnosticCountLine(
        float x,
        float y,
        float fontSize,
        uint color,
        ReadOnlySpan<char> prefix,
        int count)
    {
        Span<char> line = stackalloc char[96];
        int position = 0;
        prefix.CopyTo(line);
        position += prefix.Length;
        count.TryFormat(line[position..], out int written, provider: CultureInfo.InvariantCulture);
        position += written;
        Im.Text(line[..position], x, y, fontSize, color);
        return y + fontSize + 1f;
    }

    private static void SortCompositeCategories()
    {
        if (_compositeCategoryCount <= 1)
        {
            return;
        }

        bool allNumeric = true;
        for (int categoryIndex = 0; categoryIndex < _compositeCategoryCount; categoryIndex++)
        {
            if (!TryParseNumericCategory(_compositeCategories[categoryIndex], out _))
            {
                allNumeric = false;
                break;
            }
        }

        for (int sortIndex = 1; sortIndex < _compositeCategoryCount; sortIndex++)
        {
            string key = _compositeCategories[sortIndex];
            int insertIndex = sortIndex - 1;

            while (insertIndex >= 0 &&
                   (allNumeric
                       ? CompareCategoryLabelsAsNumbers(_compositeCategories[insertIndex], key) > 0
                       : string.Compare(_compositeCategories[insertIndex], key, StringComparison.Ordinal) > 0))
            {
                _compositeCategories[insertIndex + 1] = _compositeCategories[insertIndex];
                insertIndex--;
            }

            _compositeCategories[insertIndex + 1] = key;
        }
    }

    private static void DrawCompositeBarChart(ImRect rect, ImStyle style)
    {
        float chartLeft = rect.X + ChartPadding + AxisLabelWidth;
        float chartRight = rect.Right - ChartPadding;
        float chartTop = rect.Y + ChartPadding;
        float chartBottom = rect.Bottom - ChartPadding - AxisLabelHeight;

        float chartWidth = chartRight - chartLeft;
        float chartHeight = chartBottom - chartTop;
        if (chartWidth < 20f || chartHeight < 20f) return;

        // Find global min/max
        double minVal = 0, maxVal = 0;
        for (int s = 0; s < _seriesCount; s++)
        {
            for (int ci = 0; ci < _compositeCategoryCount; ci++)
            {
                double v = _seriesValues[s][ci];
                if (v > maxVal) maxVal = v;
                if (v < minVal) minVal = v;
            }
        }
        if (maxVal == minVal) maxVal = minVal + 1;

        DrawHorizontalGridLines(chartLeft, chartRight, chartTop, chartBottom, minVal, maxVal, style);

        // Grouped bars: each category gets a group with one bar per series
        float groupWidth = (chartWidth - BarGap * (_compositeCategoryCount + 1)) / _compositeCategoryCount;
        if (groupWidth < 4f) groupWidth = 4f;
        float barWidth = (groupWidth - BarGap * (_seriesCount - 1)) / _seriesCount;
        if (barWidth < 2f) barWidth = 2f;
        int labelStep = ComputeCategoryLabelStep(_compositeCategories, _compositeCategoryCount, chartWidth, style.FontSize - 2f, categoriesArePoints: false);

        for (int ci = 0; ci < _compositeCategoryCount; ci++)
        {
            float groupX = chartLeft + BarGap + ci * (groupWidth + BarGap);

            for (int s = 0; s < _seriesCount; s++)
            {
                float barX = groupX + s * (barWidth + BarGap);
                float valNorm = (float)((_seriesValues[s][ci] - minVal) / (maxVal - minVal));
                float barTop = chartBottom - valNorm * chartHeight;
                float barH = chartBottom - barTop;

                uint color = ChartColors[s % ChartColors.Length];
                if (barH >= 0)
                    Im.DrawRoundedRect(barX, barTop, barWidth, barH, 2f, color);
            }

            if (ShouldDrawCategoryLabel(ci, _compositeCategoryCount, labelStep))
            {
                float labelW = Im.MeasureTextWidth(_compositeCategories[ci].AsSpan(), style.FontSize - 2f);
                float labelX = groupX + (groupWidth - labelW) * 0.5f;
                Im.Text(_compositeCategories[ci].AsSpan(), labelX, chartBottom + 4f, style.FontSize - 2f, style.TextSecondary);
            }
        }

        Im.DrawLine(chartLeft, chartTop, chartLeft, chartBottom, 1f, style.Border);
        Im.DrawLine(chartLeft, chartBottom, chartRight, chartBottom, 1f, style.Border);
        DrawAxisLabels(chartLeft, chartRight, chartTop, chartBottom, rect, style);
    }

    private static void DrawCompositeLineChart(ImRect rect, ImStyle style)
    {
        float chartLeft = rect.X + ChartPadding + AxisLabelWidth;
        float chartRight = rect.Right - ChartPadding;
        float chartTop = rect.Y + ChartPadding;
        float chartBottom = rect.Bottom - ChartPadding - AxisLabelHeight;

        float chartWidth = chartRight - chartLeft;
        float chartHeight = chartBottom - chartTop;
        if (chartWidth < 20f || chartHeight < 20f) return;

        // Find global min/max over present values only.
        double minVal = double.MaxValue;
        double maxVal = double.MinValue;
        bool hasAnyValue = false;
        for (int s = 0; s < _seriesCount; s++)
        {
            for (int ci = 0; ci < _compositeCategoryCount; ci++)
            {
                if (!_seriesHasValue[s][ci])
                {
                    continue;
                }

                double v = _seriesValues[s][ci];
                if (v > maxVal) maxVal = v;
                if (v < minVal) minVal = v;
                hasAnyValue = true;
            }
        }
        if (!hasAnyValue)
        {
            Im.Text("No data to chart.".AsSpan(), rect.X + 10, rect.Y + 10, style.FontSize, style.TextSecondary);
            return;
        }
        if (maxVal == minVal) { maxVal += 1; minVal -= 1; }

        DrawHorizontalGridLines(chartLeft, chartRight, chartTop, chartBottom, minVal, maxVal, style);

        float stepX = _compositeCategoryCount > 1 ? chartWidth / (_compositeCategoryCount - 1) : 0f;
        int labelStep = ComputeCategoryLabelStep(_compositeCategories, _compositeCategoryCount, chartWidth, style.FontSize - 2f, categoriesArePoints: true);

        // Draw each series as a separate line
        for (int s = 0; s < _seriesCount; s++)
        {
            uint color = ChartColors[s % ChartColors.Length];
            float prevX = 0f, prevY = 0f;
            bool hasPrevPoint = false;

            for (int ci = 0; ci < _compositeCategoryCount; ci++)
            {
                if (!_seriesHasValue[s][ci])
                {
                    hasPrevPoint = false;
                    continue;
                }

                float px = _compositeCategoryCount > 1 ? chartLeft + ci * stepX : chartLeft + chartWidth * 0.5f;
                float valNorm = (float)((_seriesValues[s][ci] - minVal) / (maxVal - minVal));
                float py = chartBottom - valNorm * chartHeight;

                if (hasPrevPoint)
                {
                    Im.DrawLine(prevX, prevY, px, py, LineThickness, color);
                }

                Im.DrawCircle(px, py, PointRadius, color);

                prevX = px;
                prevY = py;
                hasPrevPoint = true;
            }
        }

        // Category labels (once for all series)
        for (int ci = 0; ci < _compositeCategoryCount; ci++)
        {
            if (!ShouldDrawCategoryLabel(ci, _compositeCategoryCount, labelStep))
            {
                continue;
            }

            float px = _compositeCategoryCount > 1 ? chartLeft + ci * stepX : chartLeft + chartWidth * 0.5f;
            float labelW = Im.MeasureTextWidth(_compositeCategories[ci].AsSpan(), style.FontSize - 2f);
            float labelX = px - labelW * 0.5f;
            Im.Text(_compositeCategories[ci].AsSpan(), labelX, chartBottom + 4f, style.FontSize - 2f, style.TextSecondary);
        }

        Im.DrawLine(chartLeft, chartTop, chartLeft, chartBottom, 1f, style.Border);
        Im.DrawLine(chartLeft, chartBottom, chartRight, chartBottom, 1f, style.Border);
        DrawAxisLabels(chartLeft, chartRight, chartTop, chartBottom, rect, style);
    }

    private static void DrawCompositeAreaChart(ImRect rect, ImStyle style)
    {
        float chartLeft = rect.X + ChartPadding + AxisLabelWidth;
        float chartRight = rect.Right - ChartPadding;
        float chartTop = rect.Y + ChartPadding;
        float chartBottom = rect.Bottom - ChartPadding - AxisLabelHeight;

        float chartWidth = chartRight - chartLeft;
        float chartHeight = chartBottom - chartTop;
        if (chartWidth < 20f || chartHeight < 20f) return;

        // Global min/max across all present series points.
        double minVal = double.MaxValue;
        double maxVal = double.MinValue;
        bool hasAnyValue = false;
        for (int s = 0; s < _seriesCount; s++)
        {
            for (int ci = 0; ci < _compositeCategoryCount; ci++)
            {
                if (!_seriesHasValue[s][ci])
                {
                    continue;
                }

                double v = _seriesValues[s][ci];
                if (v > maxVal) maxVal = v;
                if (v < minVal) minVal = v;
                hasAnyValue = true;
            }
        }
        if (!hasAnyValue)
        {
            Im.Text("No data to chart.".AsSpan(), rect.X + 10, rect.Y + 10, style.FontSize, style.TextSecondary);
            return;
        }
        if (minVal > 0) minVal = 0;
        if (maxVal == minVal) { maxVal += 1; }

        DrawHorizontalGridLines(chartLeft, chartRight, chartTop, chartBottom, minVal, maxVal, style);

        float stepX = _compositeCategoryCount > 1 ? chartWidth / (_compositeCategoryCount - 1) : 0f;
        int labelStep = ComputeCategoryLabelStep(_compositeCategories, _compositeCategoryCount, chartWidth, style.FontSize - 2f, categoriesArePoints: true);

        // Draw each series: filled area + line on top (back-to-front for layering).
        // Build each series from valid points only to avoid synthetic V-notches from missing values.
        Span<Vector2> seriesPoints = stackalloc Vector2[_compositeCategoryCount];
        Span<Vector2> poly = stackalloc Vector2[_compositeCategoryCount + 2];
        for (int s = _seriesCount - 1; s >= 0; s--)
        {
            uint color = ChartColors[s % ChartColors.Length];
            uint fillColor = (color & 0x00FFFFFF) | 0x40000000;

            int seriesPointCount = 0;
            for (int ci = 0; ci < _compositeCategoryCount; ci++)
            {
                if (!_seriesHasValue[s][ci])
                {
                    continue;
                }

                float px = _compositeCategoryCount > 1 ? chartLeft + ci * stepX : chartLeft + chartWidth * 0.5f;
                float valNorm = (float)((_seriesValues[s][ci] - minVal) / (maxVal - minVal));
                float py = chartBottom - valNorm * chartHeight;
                seriesPoints[seriesPointCount++] = new Vector2(px, py);
            }

            if (seriesPointCount == 0)
            {
                continue;
            }

            if (seriesPointCount >= 2)
            {
                for (int pointIndex = 0; pointIndex < seriesPointCount; pointIndex++)
                {
                    poly[pointIndex] = seriesPoints[pointIndex];
                }
                poly[seriesPointCount] = new Vector2(seriesPoints[seriesPointCount - 1].X, chartBottom);
                poly[seriesPointCount + 1] = new Vector2(seriesPoints[0].X, chartBottom);
                Im.DrawFilledPolygon(poly[..(seriesPointCount + 2)], fillColor);
            }

            // Draw line on top
            if (seriesPointCount >= 2)
            {
                Im.DrawPolyline(seriesPoints[..seriesPointCount], LineThickness, color);
            }
        }

        // Category labels
        for (int ci = 0; ci < _compositeCategoryCount; ci++)
        {
            if (!ShouldDrawCategoryLabel(ci, _compositeCategoryCount, labelStep))
            {
                continue;
            }

            float px = _compositeCategoryCount > 1 ? chartLeft + ci * stepX : chartLeft + chartWidth * 0.5f;
            float labelW = Im.MeasureTextWidth(_compositeCategories[ci].AsSpan(), style.FontSize - 2f);
            float labelX = px - labelW * 0.5f;
            Im.Text(_compositeCategories[ci].AsSpan(), labelX, chartBottom + 4f, style.FontSize - 2f, style.TextSecondary);
        }

        Im.DrawLine(chartLeft, chartTop, chartLeft, chartBottom, 1f, style.Border);
        Im.DrawLine(chartLeft, chartBottom, chartRight, chartBottom, 1f, style.Border);
        DrawAxisLabels(chartLeft, chartRight, chartTop, chartBottom, rect, style);
    }

    private static void DrawCompositePieChart(ImRect rect, ImStyle style)
    {
        // For composite pie: each series gets its own smaller pie arranged horizontally
        float availWidth = rect.Width - ChartPadding * 2;
        float availHeight = rect.Height - ChartPadding * 2 - LegendRowHeight * _seriesCount;

        int cols = Math.Min(_seriesCount, 4);
        int rows = (_seriesCount + cols - 1) / cols;
        float cellWidth = availWidth / cols;
        float cellHeight = availHeight / rows;
        float radius = Math.Min(cellWidth, cellHeight) * 0.35f;
        if (radius < 10f) radius = 10f;

        Span<Vector2> triPoints = stackalloc Vector2[3];

        for (int s = 0; s < _seriesCount; s++)
        {
            int col = s % cols;
            int row = s / cols;

            float cx = rect.X + ChartPadding + cellWidth * col + cellWidth * 0.5f;
            float cy = rect.Y + ChartPadding + cellHeight * row + cellHeight * 0.5f;

            // Sum positive values for this series
            double total = 0;
            for (int ci = 0; ci < _compositeCategoryCount; ci++)
            {
                if (_seriesValues[s][ci] > 0) total += _seriesValues[s][ci];
            }

            if (total <= 0) continue;

            // Series label above pie
            uint seriesColor = ChartColors[s % ChartColors.Length];
            float labelW = Im.MeasureTextWidth(_seriesNames[s].AsSpan(), style.FontSize);
            Im.Text(_seriesNames[s].AsSpan(), cx - labelW * 0.5f, cy - radius - style.FontSize - 4f, style.FontSize, seriesColor);

            // Draw wedges
            float startAngle = -MathF.PI / 2f;
            int sliceCount = Math.Min(_compositeCategoryCount, MaxPieSlices);

            for (int ci = 0; ci < sliceCount; ci++)
            {
                double val = _seriesValues[s][ci];
                if (val <= 0) continue;

                float sweepAngle = (float)(val / total) * MathF.PI * 2f;
                uint color = ChartColors[ci % ChartColors.Length];

                int segments = Math.Max(4, (int)(sweepAngle / 0.1f));
                float angleStep = sweepAngle / segments;

                for (int seg = 0; seg < segments; seg++)
                {
                    float a0 = startAngle + seg * angleStep;
                    float a1 = startAngle + (seg + 1) * angleStep;

                    triPoints[0] = new Vector2(cx, cy);
                    triPoints[1] = new Vector2(cx + MathF.Cos(a0) * radius, cy + MathF.Sin(a0) * radius);
                    triPoints[2] = new Vector2(cx + MathF.Cos(a1) * radius, cy + MathF.Sin(a1) * radius);

                    Im.DrawFilledPolygon(triPoints, color);
                }

                startAngle += sweepAngle;
            }
        }

        // Category legend at bottom
        float legendX = rect.X + ChartPadding;
        float legendY = rect.Bottom - ChartPadding - LegendRowHeight * Math.Min(_compositeCategoryCount, MaxPieSlices);
        for (int ci = 0; ci < Math.Min(_compositeCategoryCount, MaxPieSlices); ci++)
        {
            uint color = ChartColors[ci % ChartColors.Length];
            Im.DrawRoundedRect(legendX, legendY + (LegendRowHeight - LegendSquareSize) * 0.5f, LegendSquareSize, LegendSquareSize, 2f, color);
            Im.Text(_compositeCategories[ci].AsSpan(), legendX + LegendSquareSize + 6f, legendY + (LegendRowHeight - style.FontSize) * 0.5f, style.FontSize - 1f, style.TextPrimary);
            legendX += Im.MeasureTextWidth(_compositeCategories[ci].AsSpan(), style.FontSize - 1f) + LegendSquareSize + 18f;

            // Wrap if too wide
            if (legendX > rect.Right - 40f)
            {
                legendX = rect.X + ChartPadding;
                legendY += LegendRowHeight;
            }
        }
    }

    private static void DrawAxisLabels(float chartLeft, float chartRight, float chartTop, float chartBottom, ImRect rect, ImStyle style)
    {
        float axisFs = style.FontSize - 1f;

        // Y-axis label (value column name) — drawn vertically on the far left
        if (!string.IsNullOrEmpty(_yAxisLabel))
        {
            float labelW = Im.MeasureTextWidth(_yAxisLabel.AsSpan(), axisFs);
            float labelX = rect.X + 2f;
            float labelY = chartTop + (chartBottom - chartTop - labelW) * 0.5f;
            // Draw horizontally at the top-left since we don't have vertical text
            Im.Text(_yAxisLabel.AsSpan(), labelX, chartTop - axisFs - 4f, axisFs, style.TextSecondary);
        }

        // X-axis label (category column name) — centered below the chart
        if (!string.IsNullOrEmpty(_xAxisLabel))
        {
            float labelW = Im.MeasureTextWidth(_xAxisLabel.AsSpan(), axisFs);
            float labelX = chartLeft + (chartRight - chartLeft - labelW) * 0.5f;
            Im.Text(_xAxisLabel.AsSpan(), labelX, chartBottom + AxisLabelHeight + 2f, axisFs, style.TextSecondary);
        }
    }

    private static void SortDataByCategory()
    {
        // Check if all categories are numeric
        bool allNumeric = true;
        for (int i = 0; i < _dataCount; i++)
        {
            if (!TryParseNumericCategory(_categories[i], out _))
            {
                allNumeric = false;
                break;
            }
        }
        if (!allNumeric) return;

        // Simple insertion sort (data count is small)
        for (int i = 1; i < _dataCount; i++)
        {
            if (!TryParseNumericCategory(_categories[i], out double key))
            {
                continue;
            }

            string cat = _categories[i];
            double val = _values[i];
            int j = i - 1;
            while (j >= 0 &&
                   TryParseNumericCategory(_categories[j], out double existing) &&
                   existing > key)
            {
                _categories[j + 1] = _categories[j];
                _values[j + 1] = _values[j];
                j--;
            }
            _categories[j + 1] = cat;
            _values[j + 1] = val;
        }
    }

    private static string ResolveCategoryLabel(DocWorkspace workspace, DocColumn? categoryColumn, DocCellValue categoryCell, int fallbackOneBasedIndex)
    {
        if (categoryColumn == null)
        {
            return fallbackOneBasedIndex.ToString(CultureInfo.InvariantCulture);
        }

        string label = categoryColumn.Kind switch
        {
            DocColumnKind.Number => categoryCell.NumberValue.ToString("G", CultureInfo.InvariantCulture),
            DocColumnKind.Formula => string.IsNullOrWhiteSpace(categoryCell.StringValue)
                ? categoryCell.NumberValue.ToString("G", CultureInfo.InvariantCulture)
                : categoryCell.StringValue,
            DocColumnKind.Checkbox => categoryCell.BoolValue ? "true" : "false",
            DocColumnKind.Relation => workspace.ResolveRelationDisplayLabel(categoryColumn, categoryCell.StringValue ?? ""),
            _ => categoryCell.StringValue ?? ""
        };

        if (string.IsNullOrWhiteSpace(label))
        {
            label = fallbackOneBasedIndex.ToString(CultureInfo.InvariantCulture);
        }

        return label;
    }

    private static bool TryParseNumericCategory(string category, out double value)
    {
        if (double.TryParse(category, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        return double.TryParse(category, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out value);
    }

    private static int CompareCategoryLabelsAsNumbers(string left, string right)
    {
        bool hasLeft = TryParseNumericCategory(left, out double leftValue);
        bool hasRight = TryParseNumericCategory(right, out double rightValue);

        if (hasLeft && hasRight)
        {
            return leftValue.CompareTo(rightValue);
        }

        if (hasLeft)
        {
            return -1;
        }

        if (hasRight)
        {
            return 1;
        }

        return string.Compare(left, right, StringComparison.Ordinal);
    }

    private static int ComputeCategoryLabelStep(string[] categories, int categoryCount, float axisWidth, float fontSize, bool categoriesArePoints)
    {
        if (categoryCount <= 1 || axisWidth <= 0f)
        {
            return 1;
        }

        float maxLabelWidth = 0f;
        for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
        {
            ReadOnlySpan<char> label = categories[categoryIndex].AsSpan();
            float labelWidth = Im.MeasureTextWidth(label, fontSize);
            if (labelWidth > maxLabelWidth)
            {
                maxLabelWidth = labelWidth;
            }
        }

        if (maxLabelWidth <= 0f)
        {
            return 1;
        }

        int slotCount = categoriesArePoints ? Math.Max(1, categoryCount - 1) : Math.Max(1, categoryCount);
        float slotWidth = axisWidth / slotCount;
        float targetLabelWidth = maxLabelWidth + 8f;

        if (slotWidth >= targetLabelWidth)
        {
            return 1;
        }

        int step = (int)MathF.Ceiling(targetLabelWidth / Math.Max(1f, slotWidth));
        if (step < 1)
        {
            step = 1;
        }

        return Math.Min(step, categoryCount);
    }

    private static bool ShouldDrawCategoryLabel(int categoryIndex, int categoryCount, int labelStep)
    {
        if (categoryCount <= 0)
        {
            return false;
        }

        if (labelStep <= 1)
        {
            return true;
        }

        if (categoryIndex == 0 || categoryIndex == categoryCount - 1)
        {
            return true;
        }

        return categoryIndex % labelStep == 0;
    }

    private static void DrawHorizontalGridLines(float left, float right, float top, float bottom, double minVal, double maxVal, ImStyle style)
    {
        float chartHeight = bottom - top;
        int gridLineCount = 5;
        uint gridColor = ImStyle.WithAlpha(style.Border, (byte)GridLineAlpha);
        Span<char> labelBuf = stackalloc char[16];

        for (int g = 0; g <= gridLineCount; g++)
        {
            float t = (float)g / gridLineCount;
            float gy = bottom - t * chartHeight;
            double val = minVal + (maxVal - minVal) * t;

            Im.DrawLine(left, gy, right, gy, 1f, gridColor);

            // Value label
            val.TryFormat(labelBuf, out int labelLen, "G4");
            float labelW = Im.MeasureTextWidth(labelBuf[..labelLen], style.FontSize - 2f);
            Im.Text(labelBuf[..labelLen], left - labelW - 4f, gy - (style.FontSize - 2f) * 0.5f, style.FontSize - 2f, style.TextSecondary);
        }
    }
}
