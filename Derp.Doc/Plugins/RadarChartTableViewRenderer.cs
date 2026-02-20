using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Derp.Doc.Model;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Widgets;

namespace Derp.Doc.Plugins;

public sealed class RadarChartTableViewRenderer : DerpDocTableViewRendererBase, IDerpDocSubtableDisplayRenderer
{
    private const int MaxAxes = 8;
    private const int MaxSeries = 8;
    private const int RingCount = 4;
    private const float MinRadius = 48f;
    private const float LabelPadding = 12f;
    private const float LegendRowHeight = 18f;
    private const bool DefaultShowLegend = true;
    private const bool DefaultShowAxisLabels = true;
    private const int DefaultMaxSeries = 4;
    private const float DefaultFillAlpha = 0.20f;

    private static readonly uint[] SeriesColors =
    [
        0xFF4488FF,
        0xFF44BB66,
        0xFFFF8844,
        0xFFBB44BB,
    ];

    private static readonly DocColumn[] AxisColumns = new DocColumn[MaxAxes];
    private static readonly double[] AxisMinimumValues = new double[MaxAxes];
    private static readonly double[] AxisMaximumValues = new double[MaxAxes];
    private static readonly int[] SeriesRowIndices = new int[MaxSeries];
    private static readonly string[] SeriesLabels = new string[MaxSeries];
    private static readonly Vector2[] ScratchPolygonPoints = new Vector2[MaxAxes];
    private static string? _cachedSubtableSettingsJson;
    private static RadarSubtableDisplaySettings _cachedSubtableSettings = RadarSubtableDisplaySettings.Default;

    public override string RendererId => "sample.radar-chart";

    public override string DisplayName => "Radar Chart";

    public override string? IconGlyph => "R";

    public override void Draw(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocView view,
        ImRect contentRect)
    {
        DrawInternal(
            workspace,
            table,
            view,
            contentRect,
            RadarSubtableDisplaySettings.Default);
    }

    public override bool DrawEmbedded(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocView view,
        ImRect contentRect,
        bool interactive,
        string stateKey)
    {
        _ = interactive;
        _ = stateKey;
        DrawInternal(
            workspace,
            table,
            view,
            contentRect,
            RadarSubtableDisplaySettings.Default);
        return true;
    }

    public bool DrawSubtableDisplayPreview(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocView view,
        DocColumn parentSubtableColumn,
        string? pluginSettingsJson,
        ImRect contentRect,
        bool interactive,
        string stateKey)
    {
        _ = parentSubtableColumn;
        _ = interactive;
        _ = stateKey;
        DrawInternal(
            workspace,
            table,
            view,
            contentRect,
            ResolveSubtableDisplaySettings(pluginSettingsJson));
        return true;
    }

    public float MeasureSubtableDisplaySettingsHeight(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocColumn parentSubtableColumn,
        string? pluginSettingsJson,
        float contentWidth,
        ImStyle style)
    {
        _ = workspace;
        _ = table;
        _ = parentSubtableColumn;
        _ = pluginSettingsJson;
        _ = contentWidth;

        float headerHeight = 26f;
        float rowHeight = MathF.Max(style.MinButtonHeight + 8f, 34f);
        return headerHeight + (rowHeight * 4f) + 4f;
    }

    public float DrawSubtableDisplaySettingsEditor(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocColumn parentSubtableColumn,
        ref string? pluginSettingsJson,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        _ = workspace;
        _ = table;
        _ = parentSubtableColumn;

        RadarSubtableDisplaySettings settings = ResolveSubtableDisplaySettings(pluginSettingsJson);
        bool showLegend = settings.ShowLegend;
        bool showAxisLabels = settings.ShowAxisLabels;
        int maxSeries = settings.MaxSeries;
        float fillAlphaPercent = settings.FillAlpha * 100f;

        float width = MathF.Max(160f, contentRect.Width);
        float tableX = contentRect.X;
        float headerHeight = 26f;
        float rowHeight = MathF.Max(style.MinButtonHeight + 8f, 34f);
        float labelColumnWidth = MathF.Max(140f, width * 0.44f);
        const float cellPadding = 8f;

        Im.DrawRect(tableX, y, width, headerHeight, style.Surface);
        Im.DrawLine(tableX, y + headerHeight, tableX + width, y + headerHeight, 1f, style.Border);
        Im.DrawLine(tableX + labelColumnWidth, y, tableX + labelColumnWidth, y + headerHeight, 1f, style.Border);

        float headerTextY = y + (headerHeight - style.FontSize) * 0.5f;
        Im.Text("Setting".AsSpan(), tableX + cellPadding, headerTextY, style.FontSize, style.TextSecondary);
        Im.Text("Value".AsSpan(), tableX + labelColumnWidth + cellPadding, headerTextY, style.FontSize, style.TextSecondary);
        y += headerHeight;

        bool changed = false;
        changed |= DrawToggleSettingRow(
            rowIndex: 0,
            "Show legend",
            "radar_subtable_show_legend",
            tableX,
            y,
            width,
            rowHeight,
            labelColumnWidth,
            ref showLegend,
            style);
        y += rowHeight;

        changed |= DrawToggleSettingRow(
            rowIndex: 1,
            "Show axis labels",
            "radar_subtable_show_axis_labels",
            tableX,
            y,
            width,
            rowHeight,
            labelColumnWidth,
            ref showAxisLabels,
            style);
        y += rowHeight;

        changed |= DrawIntSettingRow(
            rowIndex: 2,
            "Max series",
            "radar_subtable_max_series",
            tableX,
            y,
            width,
            rowHeight,
            labelColumnWidth,
            minValue: 1,
            maxValue: MaxSeries,
            ref maxSeries,
            style);
        y += rowHeight;

        changed |= DrawFloatSettingRow(
            rowIndex: 3,
            "Fill alpha %",
            "radar_subtable_fill_alpha",
            tableX,
            y,
            width,
            rowHeight,
            labelColumnWidth,
            minValue: 0f,
            maxValue: 100f,
            ref fillAlphaPercent,
            style);
        y += rowHeight + 4f;

        if (changed)
        {
            var updatedSettings = new RadarSubtableDisplaySettings(
                showLegend,
                showAxisLabels,
                maxSeries,
                fillAlphaPercent * 0.01f);
            pluginSettingsJson = SerializeSubtableDisplaySettings(updatedSettings);
            _cachedSubtableSettingsJson = pluginSettingsJson;
            _cachedSubtableSettings = NormalizeSubtableDisplaySettings(updatedSettings);
        }

        return y;
    }

    public override float MeasureEmbeddedHeight(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocView view,
        float blockWidth,
        float fallbackHeight)
    {
        _ = workspace;
        _ = table;
        _ = view;
        _ = blockWidth;
        return MathF.Max(fallbackHeight, 220f);
    }

    public override float DrawInspector(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocView view,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        _ = workspace;
        float x = contentRect.X + 8f;

        Im.Text("Radar Chart Renderer".AsSpan(), x, y, style.FontSize, style.TextPrimary);
        y += 20f;
        Im.Text("Axes: visible Number and Formula columns.".AsSpan(), x, y, style.FontSize - 1f, style.TextSecondary);
        y += 18f;
        Im.Text("Series: first rows from current view filter/sort.".AsSpan(), x, y, style.FontSize - 1f, style.TextSecondary);
        y += 18f;
        Im.Text("Subtable display settings are configured in Edit column.".AsSpan(), x, y, style.FontSize - 1f, style.TextSecondary);
        y += 18f;

        if (TryCollectAxisColumns(table, view, out int axisCount))
        {
            Span<char> axisCountBuffer = stackalloc char[40];
            const string prefix = "Detected axes: ";
            prefix.AsSpan().CopyTo(axisCountBuffer);
            int length = prefix.Length;
            if (axisCount.TryFormat(axisCountBuffer[length..], out int written))
            {
                length += written;
                Im.Text(axisCountBuffer[..length], x, y, style.FontSize - 1f, style.TextSecondary);
                y += 18f;
            }
        }

        return y + 4f;
    }

    private static void DrawInternal(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocView view,
        ImRect contentRect,
        RadarSubtableDisplaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(view);

        var style = Im.Style;
        Im.DrawRect(contentRect.X, contentRect.Y, contentRect.Width, contentRect.Height, style.Background);

        if (!TryCollectAxisColumns(table, view, out int axisCount))
        {
            Im.Text("Radar chart needs at least 3 Number/Formula columns.".AsSpan(), contentRect.X + 10f, contentRect.Y + 10f, style.FontSize, style.TextSecondary);
            return;
        }

        int[]? viewRowIndices = workspace.ComputeViewRowIndices(table, view);
        int tableRowCount = viewRowIndices?.Length ?? table.Rows.Count;
        if (tableRowCount <= 0)
        {
            Im.Text("No rows to chart.".AsSpan(), contentRect.X + 10f, contentRect.Y + 10f, style.FontSize, style.TextSecondary);
            return;
        }

        int seriesCount = CollectSeriesRows(table, viewRowIndices, tableRowCount, settings.MaxSeries);
        if (seriesCount <= 0)
        {
            Im.Text("No rows to chart.".AsSpan(), contentRect.X + 10f, contentRect.Y + 10f, style.FontSize, style.TextSecondary);
            return;
        }

        ComputeAxisRanges(table, viewRowIndices, tableRowCount, axisCount);

        float legendHeight = settings.ShowLegend ? (seriesCount * LegendRowHeight) + 10f : 0f;
        float chartHeight = MathF.Max(1f, contentRect.Height - legendHeight);
        float centerX = contentRect.X + (contentRect.Width * 0.5f);
        float centerY = contentRect.Y + (chartHeight * 0.5f);
        float radius = MathF.Min(contentRect.Width, chartHeight) * 0.5f - LabelPadding - 8f;
        if (radius < MinRadius)
        {
            Im.Text("Resize the panel to render radar chart data.".AsSpan(), contentRect.X + 10f, contentRect.Y + 10f, style.FontSize, style.TextSecondary);
            return;
        }

        uint axisColor = ImStyle.WithAlpha(style.Border, 120);
        uint ringColor = ImStyle.WithAlpha(style.Border, 76);
        DrawRingsAndAxes(axisCount, centerX, centerY, radius, ringColor, axisColor);
        if (settings.ShowAxisLabels)
        {
            DrawAxisLabels(style, axisCount, centerX, centerY, radius);
        }

        for (int seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
        {
            int rowIndex = SeriesRowIndices[seriesIndex];
            DrawSeriesPolygon(
                table,
                rowIndex,
                axisCount,
                centerX,
                centerY,
                radius,
                SeriesColors[seriesIndex % SeriesColors.Length],
                settings.FillAlpha);
        }

        if (settings.ShowLegend)
        {
            DrawLegend(contentRect, style, seriesCount);
        }
    }

    private static bool TryCollectAxisColumns(DocTable table, DocView view, out int axisCount)
    {
        axisCount = 0;
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            var column = table.Columns[columnIndex];
            if (!IsNumericAxisColumn(column))
            {
                continue;
            }

            if (!IsColumnVisibleInView(view, column))
            {
                continue;
            }

            AxisColumns[axisCount] = column;
            axisCount++;
            if (axisCount >= MaxAxes)
            {
                break;
            }
        }

        return axisCount >= 3;
    }

    private static bool IsNumericAxisColumn(DocColumn column)
    {
        return column.Kind == DocColumnKind.Number || column.Kind == DocColumnKind.Formula;
    }

    private static bool IsColumnVisibleInView(DocView view, DocColumn column)
    {
        if (column.IsHidden)
        {
            return false;
        }

        if (view.VisibleColumnIds == null || view.VisibleColumnIds.Count == 0)
        {
            return true;
        }

        for (int columnIdIndex = 0; columnIdIndex < view.VisibleColumnIds.Count; columnIdIndex++)
        {
            if (string.Equals(view.VisibleColumnIds[columnIdIndex], column.Id, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static int CollectSeriesRows(
        DocTable table,
        int[]? viewRowIndices,
        int tableRowCount,
        int maxSeriesCount)
    {
        int seriesCount = Math.Min(tableRowCount, Math.Clamp(maxSeriesCount, 1, MaxSeries));
        for (int seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
        {
            int rowIndex = viewRowIndices == null ? seriesIndex : viewRowIndices[seriesIndex];
            SeriesRowIndices[seriesIndex] = rowIndex;
            SeriesLabels[seriesIndex] = ResolveRowLabel(table, table.Rows[rowIndex]);
        }

        return seriesCount;
    }

    private static string ResolveRowLabel(DocTable table, DocRow row)
    {
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            var column = table.Columns[columnIndex];
            if (column.Kind != DocColumnKind.Text &&
                column.Kind != DocColumnKind.Select &&
                column.Kind != DocColumnKind.Relation)
            {
                continue;
            }

            string value = row.GetCell(column).StringValue;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return row.Id;
    }

    private static void ComputeAxisRanges(DocTable table, int[]? viewRowIndices, int tableRowCount, int axisCount)
    {
        for (int axisIndex = 0; axisIndex < axisCount; axisIndex++)
        {
            AxisMinimumValues[axisIndex] = double.MaxValue;
            AxisMaximumValues[axisIndex] = double.MinValue;
        }

        for (int tableRowIndex = 0; tableRowIndex < tableRowCount; tableRowIndex++)
        {
            int rowIndex = viewRowIndices == null ? tableRowIndex : viewRowIndices[tableRowIndex];
            var row = table.Rows[rowIndex];
            for (int axisIndex = 0; axisIndex < axisCount; axisIndex++)
            {
                double value = row.GetCell(AxisColumns[axisIndex]).NumberValue;
                if (value < AxisMinimumValues[axisIndex])
                {
                    AxisMinimumValues[axisIndex] = value;
                }

                if (value > AxisMaximumValues[axisIndex])
                {
                    AxisMaximumValues[axisIndex] = value;
                }
            }
        }

        for (int axisIndex = 0; axisIndex < axisCount; axisIndex++)
        {
            var axisColumn = AxisColumns[axisIndex];
            double minValue = axisColumn.NumberMin ?? AxisMinimumValues[axisIndex];
            double maxValue = axisColumn.NumberMax ?? AxisMaximumValues[axisIndex];
            if (minValue > maxValue)
            {
                double tempValue = minValue;
                minValue = maxValue;
                maxValue = tempValue;
            }

            if (Math.Abs(maxValue - minValue) <= double.Epsilon)
            {
                maxValue = minValue + 1d;
            }

            AxisMinimumValues[axisIndex] = minValue;
            AxisMaximumValues[axisIndex] = maxValue;
        }
    }

    private static void DrawRingsAndAxes(
        int axisCount,
        float centerX,
        float centerY,
        float radius,
        uint ringColor,
        uint axisColor)
    {
        for (int ringIndex = 1; ringIndex <= RingCount; ringIndex++)
        {
            float ringRadius = radius * (ringIndex / (float)RingCount);
            for (int axisIndex = 0; axisIndex < axisCount; axisIndex++)
            {
                float angle = GetAxisAngle(axisIndex, axisCount);
                ScratchPolygonPoints[axisIndex] = new Vector2(
                    centerX + (MathF.Cos(angle) * ringRadius),
                    centerY + (MathF.Sin(angle) * ringRadius));
            }

            DrawClosedPolyline(ScratchPolygonPoints, axisCount, ringColor, 1f);
        }

        for (int axisIndex = 0; axisIndex < axisCount; axisIndex++)
        {
            float angle = GetAxisAngle(axisIndex, axisCount);
            float endX = centerX + (MathF.Cos(angle) * radius);
            float endY = centerY + (MathF.Sin(angle) * radius);
            Im.DrawLine(centerX, centerY, endX, endY, 1f, axisColor);
        }
    }

    private static void DrawAxisLabels(
        ImStyle style,
        int axisCount,
        float centerX,
        float centerY,
        float radius)
    {
        float fontSize = style.FontSize - 1f;
        float labelDistance = radius + LabelPadding;
        for (int axisIndex = 0; axisIndex < axisCount; axisIndex++)
        {
            float angle = GetAxisAngle(axisIndex, axisCount);
            float directionX = MathF.Cos(angle);
            float directionY = MathF.Sin(angle);
            string label = AxisColumns[axisIndex].Name;
            float labelWidth = Im.MeasureTextWidth(label.AsSpan(), fontSize);
            float textX = centerX + (directionX * labelDistance) - (labelWidth * 0.5f);
            float textY = centerY + (directionY * labelDistance) - (fontSize * 0.5f);
            Im.Text(label.AsSpan(), textX, textY, fontSize, style.TextSecondary);
        }
    }

    private static void DrawSeriesPolygon(
        DocTable table,
        int rowIndex,
        int axisCount,
        float centerX,
        float centerY,
        float radius,
        uint lineColor,
        float fillAlpha)
    {
        var row = table.Rows[rowIndex];
        for (int axisIndex = 0; axisIndex < axisCount; axisIndex++)
        {
            float angle = GetAxisAngle(axisIndex, axisCount);
            double minAxisValue = AxisMinimumValues[axisIndex];
            double maxAxisValue = AxisMaximumValues[axisIndex];
            double value = row.GetCell(AxisColumns[axisIndex]).NumberValue;
            double axisRange = maxAxisValue - minAxisValue;
            float normalized = axisRange <= double.Epsilon
                ? 0f
                : (float)((value - minAxisValue) / axisRange);
            if (normalized < 0f)
            {
                normalized = 0f;
            }
            else if (normalized > 1f)
            {
                normalized = 1f;
            }

            float pointRadius = radius * normalized;
            ScratchPolygonPoints[axisIndex] = new Vector2(
                centerX + (MathF.Cos(angle) * pointRadius),
                centerY + (MathF.Sin(angle) * pointRadius));
        }

        float normalizedFillAlpha = Math.Clamp(fillAlpha, 0f, 1f);
        byte fillAlphaByte = (byte)Math.Clamp((int)MathF.Round(normalizedFillAlpha * 255f), 0, 255);
        uint fillColor = ImStyle.WithAlpha(lineColor, fillAlphaByte);
        Im.DrawFilledPolygon(ScratchPolygonPoints.AsSpan(0, axisCount), fillColor);
        DrawClosedPolyline(ScratchPolygonPoints, axisCount, lineColor, 2f);

        for (int axisIndex = 0; axisIndex < axisCount; axisIndex++)
        {
            Im.DrawCircle(
                ScratchPolygonPoints[axisIndex].X,
                ScratchPolygonPoints[axisIndex].Y,
                3f,
                lineColor);
        }
    }

    private static void DrawLegend(ImRect contentRect, ImStyle style, int seriesCount)
    {
        float legendY = contentRect.Bottom - (seriesCount * LegendRowHeight) - 6f;
        float legendX = contentRect.X + 8f;
        for (int seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
        {
            float rowY = legendY + (seriesIndex * LegendRowHeight);
            uint color = SeriesColors[seriesIndex % SeriesColors.Length];
            Im.DrawRoundedRect(legendX, rowY + 3f, 10f, 10f, 2f, color);
            Im.Text(SeriesLabels[seriesIndex].AsSpan(), legendX + 16f, rowY, style.FontSize - 1f, style.TextPrimary);
        }
    }

    private static void DrawClosedPolyline(Vector2[] points, int pointCount, uint color, float thickness)
    {
        if (pointCount < 2)
        {
            return;
        }

        int previousPointIndex = pointCount - 1;
        for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
        {
            var from = points[previousPointIndex];
            var to = points[pointIndex];
            Im.DrawLine(from.X, from.Y, to.X, to.Y, thickness, color);
            previousPointIndex = pointIndex;
        }
    }

    private static float GetAxisAngle(int axisIndex, int axisCount)
    {
        return (-MathF.PI * 0.5f) + ((MathF.PI * 2f) * (axisIndex / (float)axisCount));
    }

    private static bool DrawToggleSettingRow(
        int rowIndex,
        string label,
        string checkboxId,
        float x,
        float y,
        float width,
        float rowHeight,
        float labelColumnWidth,
        ref bool value,
        ImStyle style)
    {
        const float cellPadding = 8f;
        uint rowColor = rowIndex % 2 == 0
            ? ImStyle.WithAlpha(style.Surface, 96)
            : ImStyle.WithAlpha(style.Surface, 80);

        Im.DrawRect(x, y, width, rowHeight, rowColor);
        Im.DrawLine(x, y + rowHeight, x + width, y + rowHeight, 1f, style.Border);
        Im.DrawLine(x + labelColumnWidth, y, x + labelColumnWidth, y + rowHeight, 1f, style.Border);

        float textY = y + (rowHeight - style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), x + cellPadding, textY, style.FontSize, style.TextPrimary);

        float valueX = x + labelColumnWidth + cellPadding;
        float checkboxY = y + (rowHeight - style.CheckboxSize) * 0.5f;
        return Im.Checkbox(checkboxId, ref value, valueX, checkboxY);
    }

    private static bool DrawIntSettingRow(
        int rowIndex,
        string label,
        string scalarId,
        float x,
        float y,
        float width,
        float rowHeight,
        float labelColumnWidth,
        int minValue,
        int maxValue,
        ref int value,
        ImStyle style)
    {
        const float cellPadding = 8f;
        uint rowColor = rowIndex % 2 == 0
            ? ImStyle.WithAlpha(style.Surface, 96)
            : ImStyle.WithAlpha(style.Surface, 80);

        Im.DrawRect(x, y, width, rowHeight, rowColor);
        Im.DrawLine(x, y + rowHeight, x + width, y + rowHeight, 1f, style.Border);
        Im.DrawLine(x + labelColumnWidth, y, x + labelColumnWidth, y + rowHeight, 1f, style.Border);

        float textY = y + (rowHeight - style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), x + cellPadding, textY, style.FontSize, style.TextPrimary);

        float inputX = x + labelColumnWidth + cellPadding;
        float inputWidth = MathF.Max(80f, width - labelColumnWidth - (cellPadding * 2f));
        float inputY = y + (rowHeight - style.MinButtonHeight) * 0.5f;
        float numericValue = value;
        bool changed = ImScalarInput.DrawAt(
            scalarId,
            inputX,
            inputY,
            inputWidth,
            ref numericValue,
            minValue,
            maxValue,
            format: "F0");
        if (changed)
        {
            value = Math.Clamp((int)MathF.Round(numericValue), minValue, maxValue);
        }

        return changed;
    }

    private static bool DrawFloatSettingRow(
        int rowIndex,
        string label,
        string scalarId,
        float x,
        float y,
        float width,
        float rowHeight,
        float labelColumnWidth,
        float minValue,
        float maxValue,
        ref float value,
        ImStyle style)
    {
        const float cellPadding = 8f;
        uint rowColor = rowIndex % 2 == 0
            ? ImStyle.WithAlpha(style.Surface, 96)
            : ImStyle.WithAlpha(style.Surface, 80);

        Im.DrawRect(x, y, width, rowHeight, rowColor);
        Im.DrawLine(x, y + rowHeight, x + width, y + rowHeight, 1f, style.Border);
        Im.DrawLine(x + labelColumnWidth, y, x + labelColumnWidth, y + rowHeight, 1f, style.Border);

        float textY = y + (rowHeight - style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), x + cellPadding, textY, style.FontSize, style.TextPrimary);

        float inputX = x + labelColumnWidth + cellPadding;
        float inputWidth = MathF.Max(80f, width - labelColumnWidth - (cellPadding * 2f));
        float inputY = y + (rowHeight - style.MinButtonHeight) * 0.5f;
        return ImScalarInput.DrawAt(
            scalarId,
            inputX,
            inputY,
            inputWidth,
            ref value,
            minValue,
            maxValue,
            format: "F0");
    }

    private static RadarSubtableDisplaySettings ResolveSubtableDisplaySettings(string? pluginSettingsJson)
    {
        if (string.IsNullOrWhiteSpace(pluginSettingsJson))
        {
            return RadarSubtableDisplaySettings.Default;
        }

        if (string.Equals(_cachedSubtableSettingsJson, pluginSettingsJson, StringComparison.Ordinal))
        {
            return _cachedSubtableSettings;
        }

        RadarSubtableDisplaySettings settings = ParseSubtableDisplaySettings(pluginSettingsJson);
        _cachedSubtableSettingsJson = pluginSettingsJson;
        _cachedSubtableSettings = settings;
        return settings;
    }

    private static RadarSubtableDisplaySettings ParseSubtableDisplaySettings(string pluginSettingsJson)
    {
        var parsedSettings = RadarSubtableDisplaySettings.Default;
        try
        {
            using var document = JsonDocument.Parse(pluginSettingsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return parsedSettings;
            }

            if (TryGetBoolProperty(document.RootElement, "showLegend", "ShowLegend", out bool showLegendValue))
            {
                parsedSettings = parsedSettings with { ShowLegend = showLegendValue };
            }

            if (TryGetBoolProperty(document.RootElement, "showAxisLabels", "ShowAxisLabels", out bool showAxisLabelsValue))
            {
                parsedSettings = parsedSettings with { ShowAxisLabels = showAxisLabelsValue };
            }

            if (TryGetIntProperty(document.RootElement, "maxSeries", "MaxSeries", out int maxSeries))
            {
                parsedSettings = parsedSettings with { MaxSeries = maxSeries };
            }

            if (TryGetFloatProperty(document.RootElement, "fillAlpha", "FillAlpha", out float fillAlpha))
            {
                parsedSettings = parsedSettings with { FillAlpha = fillAlpha };
            }
        }
        catch (JsonException)
        {
            return RadarSubtableDisplaySettings.Default;
        }

        return NormalizeSubtableDisplaySettings(parsedSettings);
    }

    private static bool TryGetBoolProperty(
        JsonElement rootObject,
        string primaryName,
        string legacyName,
        out bool value)
    {
        value = false;
        if (!TryGetProperty(rootObject, primaryName, legacyName, out JsonElement propertyElement))
        {
            return false;
        }

        if (propertyElement.ValueKind != JsonValueKind.True &&
            propertyElement.ValueKind != JsonValueKind.False)
        {
            return false;
        }

        value = propertyElement.GetBoolean();
        return true;
    }

    private static bool TryGetIntProperty(
        JsonElement rootObject,
        string primaryName,
        string legacyName,
        out int value)
    {
        value = 0;
        if (!TryGetProperty(rootObject, primaryName, legacyName, out JsonElement propertyElement))
        {
            return false;
        }

        if (propertyElement.ValueKind != JsonValueKind.Number ||
            !propertyElement.TryGetInt32(out int parsedValue))
        {
            return false;
        }

        value = parsedValue;
        return true;
    }

    private static bool TryGetFloatProperty(
        JsonElement rootObject,
        string primaryName,
        string legacyName,
        out float value)
    {
        value = 0f;
        if (!TryGetProperty(rootObject, primaryName, legacyName, out JsonElement propertyElement))
        {
            return false;
        }

        if (propertyElement.ValueKind != JsonValueKind.Number ||
            !propertyElement.TryGetDouble(out double parsedValue))
        {
            return false;
        }

        value = (float)parsedValue;
        return true;
    }

    private static bool TryGetProperty(
        JsonElement rootObject,
        string primaryName,
        string legacyName,
        out JsonElement propertyElement)
    {
        if (rootObject.TryGetProperty(primaryName, out propertyElement))
        {
            return true;
        }

        if (rootObject.TryGetProperty(legacyName, out propertyElement))
        {
            return true;
        }

        propertyElement = default;
        return false;
    }

    private static RadarSubtableDisplaySettings NormalizeSubtableDisplaySettings(RadarSubtableDisplaySettings settings)
    {
        return new RadarSubtableDisplaySettings(
            settings.ShowLegend,
            settings.ShowAxisLabels,
            Math.Clamp(settings.MaxSeries, 1, MaxSeries),
            Math.Clamp(settings.FillAlpha, 0f, 1f));
    }

    private static string? SerializeSubtableDisplaySettings(RadarSubtableDisplaySettings settings)
    {
        RadarSubtableDisplaySettings normalizedSettings = NormalizeSubtableDisplaySettings(settings);
        if (normalizedSettings == RadarSubtableDisplaySettings.Default)
        {
            return null;
        }

        return JsonSerializer.Serialize(new RadarSubtableDisplaySettingsDto
        {
            ShowLegend = normalizedSettings.ShowLegend,
            ShowAxisLabels = normalizedSettings.ShowAxisLabels,
            MaxSeries = normalizedSettings.MaxSeries,
            FillAlpha = normalizedSettings.FillAlpha,
        });
    }

    private readonly record struct RadarSubtableDisplaySettings(
        bool ShowLegend,
        bool ShowAxisLabels,
        int MaxSeries,
        float FillAlpha)
    {
        public static RadarSubtableDisplaySettings Default =>
            new(DefaultShowLegend, DefaultShowAxisLabels, DefaultMaxSeries, DefaultFillAlpha);
    }

    private sealed class RadarSubtableDisplaySettingsDto
    {
        [JsonPropertyName("showLegend")]
        public bool ShowLegend { get; set; } = DefaultShowLegend;

        [JsonPropertyName("showAxisLabels")]
        public bool ShowAxisLabels { get; set; } = DefaultShowAxisLabels;

        [JsonPropertyName("maxSeries")]
        public int MaxSeries { get; set; } = DefaultMaxSeries;

        [JsonPropertyName("fillAlpha")]
        public float FillAlpha { get; set; } = DefaultFillAlpha;
    }
}
