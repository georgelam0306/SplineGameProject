using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Derp.Doc.Commands;
using Derp.Doc.Editor;
using Derp.Doc.Model;
using Derp.Doc.Panels;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Input;
using DerpLib.ImGui.Widgets;

namespace Derp.Doc.Plugins;

public sealed class NodeGraphTableViewRenderer : DerpDocTableViewRendererBase
{
    private const string RendererSettingsNamespace = "renderer.node-graph";
    private const string DefaultTypeOption = "Default";
    private const string DefaultTypeColumnName = "Type";
    private const string DefaultPositionColumnName = "Pos";
    private const string DefaultTitleColumnName = "Title";
    private const string DefaultExecutionNextColumnName = "ExecNext";
    private const string DefaultEdgesColumnName = "Edges";
    private const string DefaultFromNodeColumnName = "FromNode";
    private const string DefaultFromPinColumnName = "FromPinId";
    private const string DefaultToNodeColumnName = "ToNode";
    private const string DefaultToPinColumnName = "ToPinId";
    private const string DefaultParentRowColumnName = "_parentRowId";

    private const float DefaultNodeWidth = 240f;
    private const float MinNodeWidth = 160f;
    private const float MaxNodeWidth = 520f;
    private const float NodeHeaderHeight = 26f;
    private const float NodePinRowHeight = 20f;
    private const float NodeSettingRowHeight = 28f;
    private const float NodeFooterPadding = 8f;
    private const float NodeInlineHorizontalPadding = 8f;
    private const float NodeInlineColumnGap = 6f;
    private const float NodeRowVerticalGap = 5f;
    private const float NodeInputPinEditorLeftRatio = 0.46f;
    private const float NodeOutputPinEditorRightRatio = 0.57f;
    private const float NodeSettingLabelWidthRatio = 0.30f;
    private const float NodeExpandedTextLabelTopPadding = 2f;
    private const float NodeExpandedTextLabelGap = 3f;
    private const float NodeExpandedTextBottomPadding = 2f;
    private const float NodeInlineTextAreaMaxHeight = 120f;
    private const float NodeSubtableSectionMinHeight = 64f;
    private const float NodeSubtableSectionMaxHeight = 220f;
    private const float NodeSubtableSectionHeaderHeight = 18f;
    private const float NodeSubtableSectionLineHeight = 18f;
    private const int NodeSubtableSectionMaxVisibleRows = 5;
    private const int NodeSubtableGridMaxColumns = 4;
    private const float NodeSubtableGridCellPaddingX = 4f;
    private const float NodeSubtableGridGutterWidth = 18f;
    private const float NodeSubtableGridRemoveButtonSize = 14f;
    private const float NodeCornerRadius = 6f;
    private const float NodePinDotSize = 5f;
    private const float NodeExecPinSize = 9f;
    private const float NodeExecTopLaneOffset = 6f;
    private const float NodeExecBottomLaneOffset = 8f;
    private const float NodeShadowOffset = 5f;
    private const float MinCanvasZoom = 0.35f;
    private const float MaxCanvasZoom = 2.4f;
    private const float CreateMenuPanelWidth = 264f;
    private const float CreateMenuPanelHeight = 232f;
    private const float CreateMenuItemHeight = 24f;
    private const float NodePasteOffsetWorldUnits = 24f;
    private const int MaxInlineTextBuffer = 160;
    private const int EdgeCurveSegments = 20;
    private const string NodeContextMenuId = "node_graph_node_context_menu";
    private const string ExecutionInputPinId = "__exec_input__";
    private const string ExecutionOutputPinId = "__exec_output__";
    private const string SubtableExecutionPinPrefix = "__subexec__|";
    private const string SubtableDisplayCustomRendererPrefix = "custom:";
    private const string SubtableDisplayRendererGrid = "builtin.grid";
    private const string SubtableDisplayRendererBoard = "builtin.board";
    private const string SubtableDisplayRendererCalendar = "builtin.calendar";
    private const string SubtableDisplayRendererChart = "builtin.chart";

    private static readonly uint[] NodeAccentPalette =
    [
        0xFF3A7DFF,
        0xFF2FA86E,
        0xFFDA8A32,
        0xFFB45FCA,
        0xFFD45757,
        0xFF2CA3A8,
        0xFF8E79E6,
        0xFFC3A63A,
    ];

    private static readonly string[] LayoutModeNames =
    [
        "Hidden",
        "Setting",
        "Input pin",
        "Output pin",
    ];

    private static readonly string[] TypeNameOptionsScratch = new string[64];
    private static readonly string[] SelectOptionsScratch = new string[64];
    private static readonly List<string> TypeNameListScratch = new(32);
    private static readonly HashSet<string> TypeNameSetScratch = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Vector2[] EdgeCurvePointsScratch = new Vector2[EdgeCurveSegments + 1];
    private static readonly Vector2[] ExecutionChevronPointsScratch = new Vector2[3];
    private static readonly Dictionary<string, int> NodeIndexByRowId = new(StringComparer.Ordinal);
    private static readonly List<NodeVisual> NodeVisuals = new(256);
    private static readonly List<PinVisual> PinVisuals = new(512);
    private static readonly List<DocColumn> InputPinColumnsScratch = new(32);
    private static readonly List<DocColumn> OutputPinColumnsScratch = new(32);
    private static readonly List<DocColumn> SettingColumnsScratch = new(32);
    private static readonly List<DocColumn> SubtableGridColumnsScratch = new(8);
    private static readonly List<DocRow> SubtableRowsScratch = new(64);
    private static readonly List<float> SubtableGridRowHeightsScratch = new(64);
    private static readonly Dictionary<string, DocView> NodeCustomSubtablePreviewViewsByRendererId = new(StringComparer.OrdinalIgnoreCase);
    private static readonly DocView NodeBoardSubtableFallbackView = new()
    {
        Id = "__node_subtable_board_preview__",
        Name = "Node subtable board",
        Type = DocViewType.Board,
    };
    private static readonly DocView NodeCalendarSubtableFallbackView = new()
    {
        Id = "__node_subtable_calendar_preview__",
        Name = "Node subtable calendar",
        Type = DocViewType.Calendar,
    };
    private static readonly DocView NodeChartSubtableFallbackView = new()
    {
        Id = "__node_subtable_chart_preview__",
        Name = "Node subtable chart",
        Type = DocViewType.Chart,
        ChartKind = DocChartKind.Bar,
    };
    private static readonly Dictionary<string, InlineTextEditState> InlineTextStateByCellKey = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, NodeGraphViewSettings> SettingsCacheByViewKey = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> SelectedTypeNameByViewKey = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, NodeGraphViewState> ViewStateByViewKey = new(StringComparer.Ordinal);
    private static float _activeNodeWidth = DefaultNodeWidth;
    private static int _settingsCacheProjectRevision = -1;
    private static string _formulaEditRowId = "";
    private static string _formulaEditColumnId = "";
    private static string _formulaEditViewKey = "";
    private static readonly char[] _formulaEditBuffer = new char[512];
    private static int _formulaEditBufferLength;
    private static bool _formulaEditNeedsFocus;
    private static string _titleEditRowId = "";
    private static string _titleEditViewKey = "";
    private static readonly char[] _titleEditBuffer = new char[256];
    private static int _titleEditBufferLength;
    private static bool _titleEditNeedsFocus;
    private static string _nodeClipboardTableId = "";
    private static DocRow? _nodeClipboardRow;

    public override string RendererId => "builtin.node-graph";

    public override string DisplayName => "Node Graph";

    public override string? IconGlyph => "NG";

    internal static bool TryScaffoldSchemaForTests(
        DocWorkspace workspace,
        DocTable table,
        DocView view,
        out string statusMessage)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(view);
        var settings = new NodeGraphViewSettings();
        return TryScaffoldSchema(workspace, table, view, settings, out statusMessage);
    }

    public override void Draw(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocView view,
        ImRect contentRect)
    {
        DrawInternal(workspace, table, view, contentRect, interactive: true, embeddedStateKey: "");
    }

    public override bool DrawEmbedded(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocView view,
        ImRect contentRect,
        bool interactive,
        string stateKey)
    {
        DrawInternal(workspace, table, view, contentRect, interactive, stateKey);
        return true;
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
        return MathF.Max(fallbackHeight, 260f);
    }

    public override float DrawInspector(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocView view,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        string viewKey = BuildViewKey(table, view);
        NodeGraphViewSettings settings = GetOrCreateViewSettings(workspace, table, view);
        GraphSchema schema = ResolveGraphSchema(workspace.Project, table, settings);
        bool settingsChanged = false;
        if (!HasRequiredSchema(schema))
        {
            y = DrawScaffoldInspector(workspace, table, view, settings, contentRect, y, style);
            return y;
        }

        InferSettingsFromSchema(settings, schema);
        settingsChanged = EnsureTypeLayoutsContainActiveSchemaColumns(table, settings, schema);

        y = DrawScaffoldInspector(workspace, table, view, settings, contentRect, y, style);
        y += 6f;

        float rowX = contentRect.X + 8f;
        float rowWidth = MathF.Max(160f, contentRect.Width - 16f);

        string selectedTypeName = GetSelectedTypeName(viewKey, settings, table, schema);
        int typeOptionCount = BuildTypeNameOptions(table, schema.TypeColumn, TypeNameOptionsScratch);
        if (typeOptionCount > 0)
        {
            int selectedTypeIndex = FindTypeNameOptionIndex(TypeNameOptionsScratch, typeOptionCount, selectedTypeName);
            float textY = y + (28f - style.FontSize) * 0.5f;
            Im.Text("Node type".AsSpan(), rowX, textY, style.FontSize - 1f, style.TextSecondary);
            float dropdownX = rowX + 76f;
            float dropdownWidth = MathF.Max(100f, rowWidth - 76f);
            if (Im.Dropdown("node_graph_type_selector", TypeNameOptionsScratch.AsSpan(0, typeOptionCount), ref selectedTypeIndex, dropdownX, y, dropdownWidth))
            {
                string newSelectedTypeName = TypeNameOptionsScratch[selectedTypeIndex];
                SelectedTypeNameByViewKey[viewKey] = newSelectedTypeName;
                selectedTypeName = newSelectedTypeName;
            }

            y += 30f;
        }

        NodeTypeLayout typeLayout = GetOrCreateTypeLayout(settings, selectedTypeName);
        float nodeWidth = ResolveNodeWidth(typeLayout.NodeWidth);
        float numberLabelWidth = 76f;
        float numberInputWidth = MathF.Max(100f, rowWidth - numberLabelWidth);
        if (ImScalarInput.DrawAt(
                "Node width",
                "node_graph_type_width_input",
                rowX,
                y,
                numberLabelWidth,
                numberInputWidth,
                ref nodeWidth,
                MinNodeWidth,
                MaxNodeWidth,
                "F0"))
        {
            typeLayout.NodeWidth = ResolveNodeWidth(nodeWidth);
            settingsChanged = true;
        }

        y += 30f;
        settingsChanged |= DrawTypeLayoutEditor(workspace, table, schema, typeLayout, contentRect, ref y, style);
        y += 8f;
        y = DrawSelectedNodeInspector(workspace, table, schema, typeLayout, viewKey, contentRect, y, style);

        if (settingsChanged)
        {
            SaveViewSettings(workspace, table, view, settings);
        }

        return y;
    }

    private static void DrawInternal(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocView view,
        ImRect contentRect,
        bool interactive,
        string embeddedStateKey)
    {
        var style = Im.Style;
        var input = Im.Context.Input;
        Vector2 mouseScreenPosition = Im.MousePos;
        uint contentBackground = ImStyle.Lerp(style.Background, 0xFF000000, 0.24f);
        Im.DrawRect(contentRect.X, contentRect.Y, contentRect.Width, contentRect.Height, contentBackground);
        if (contentRect.Width <= 1f || contentRect.Height <= 1f)
        {
            return;
        }

        string viewKey = BuildViewKey(table, view);
        if (!string.IsNullOrWhiteSpace(embeddedStateKey))
        {
            viewKey = viewKey + ":" + embeddedStateKey;
        }

        NodeGraphViewSettings settings = GetOrCreateViewSettings(workspace, table, view);
        GraphSchema schema = ResolveGraphSchema(workspace.Project, table, settings);
        if (!HasRequiredSchema(schema))
        {
            DrawMissingSchemaMessage(workspace, table, view, settings, contentRect, interactive);
            return;
        }

        InferSettingsFromSchema(settings, schema);
        _activeNodeWidth = DefaultNodeWidth;

        NodeGraphViewState viewState = GetOrCreateViewState(viewKey);
        bool typeLayoutsChanged = EnsureTypeLayoutsContainActiveSchemaColumns(table, settings, schema);
        if (typeLayoutsChanged)
        {
            SaveViewSettings(workspace, table, view, settings);
        }

        HandlePanAndZoom(input, contentRect, ref viewState, mouseScreenPosition, interactive);
        DrawGrid(contentRect, viewState, style);

        _lastDrawRect = contentRect;
        BuildNodeVisuals(workspace, table, view, schema, settings, viewState, interactive);

        Im.PushClipRect(contentRect);
        DrawEdges(workspace, table, schema, settings, style);
        DrawNodesAndHandleInteraction(workspace, table, viewKey, schema, settings, viewState, contentRect, interactive);
        DrawSubtableExecutionEdges(workspace, table, schema, settings, style);
        Im.PopClipRect();

        if (interactive && workspace is DocWorkspace mutableWorkspace)
        {
            DrawCreateNodeMenu(mutableWorkspace, table, schema, settings, viewState, contentRect, style);
        }
    }

    private static void DrawMissingSchemaMessage(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocView view,
        NodeGraphViewSettings settings,
        ImRect contentRect,
        bool interactive)
    {
        float x = contentRect.X + 12f;
        float y = contentRect.Y + 12f;
        Im.Text("Node graph schema is missing required columns.".AsSpan(), x, y, Im.Style.FontSize, Im.Style.TextPrimary);
        y += 20f;
        Im.Text("Required: Type, Pos, ExecNext relation, Edges subtable.".AsSpan(), x, y, Im.Style.FontSize - 1f, Im.Style.TextSecondary);
        y += 20f;
        Im.Text("Scaffold adds required columns + edge table schema.".AsSpan(), x, y, Im.Style.FontSize - 1f, Im.Style.TextSecondary);

        if (!interactive || workspace is not DocWorkspace mutableWorkspace)
        {
            return;
        }

        y += 24f;
        if (Im.Button("Scaffold node graph schema", x, y, 220f, 26f))
        {
            if (TryScaffoldSchema(mutableWorkspace, table, view, settings, out string statusMessage))
            {
                mutableWorkspace.SetStatusMessage(statusMessage);
            }
            else
            {
                mutableWorkspace.SetStatusMessage("Node graph scaffold did not apply changes.");
            }
        }
    }

    private static float DrawScaffoldInspector(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocView view,
        NodeGraphViewSettings settings,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        float x = contentRect.X + 8f;
        float width = MathF.Max(160f, contentRect.Width - 16f);
        Im.Text("Node Graph Renderer".AsSpan(), x, y, style.FontSize, style.TextPrimary);
        y += 20f;

        GraphSchema schema = ResolveGraphSchema(workspace.Project, table, settings);
        bool schemaReady = HasRequiredSchema(schema);
        Im.Text(schemaReady ? "Schema: ready".AsSpan() : "Schema: missing required fields".AsSpan(), x, y, style.FontSize - 1f, schemaReady ? style.TextSecondary : style.Secondary);
        y += 20f;

        if (workspace is DocWorkspace mutableWorkspace)
        {
            float buttonWidth = MathF.Min(260f, width);
            if (Im.Button("Scaffold / repair schema", x, y, buttonWidth, 24f))
            {
                if (TryScaffoldSchema(mutableWorkspace, table, view, settings, out string statusMessage))
                {
                    mutableWorkspace.SetStatusMessage(statusMessage);
                }
                else
                {
                    mutableWorkspace.SetStatusMessage("Node graph scaffold did not apply changes.");
                }
            }
        }

        y += 28f;
        return y;
    }

    private static bool DrawTypeLayoutEditor(
        IDerpDocEditorContext workspace,
        DocTable table,
        GraphSchema schema,
        NodeTypeLayout typeLayout,
        ImRect contentRect,
        ref float y,
        ImStyle style)
    {
        bool changed = false;
        float rowX = contentRect.X + 8f;
        float rowWidth = MathF.Max(160f, contentRect.Width - 16f);
        Im.Text("Type layout".AsSpan(), rowX, y, style.FontSize - 1f, style.TextSecondary);
        y += 18f;

        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            DocColumn column = table.Columns[columnIndex];
            if (IsReservedNodeColumn(column, schema))
            {
                continue;
            }

            float textY = y + (26f - style.FontSize) * 0.5f;
            Im.Text(column.Name.AsSpan(), rowX, textY, style.FontSize - 1f, style.TextPrimary);
            NodeFieldDisplayMode currentMode = GetFieldDisplayMode(typeLayout, column.Id);
            int selectedModeIndex = (int)currentMode;
            float dropdownX = rowX + 120f;
            float dropdownWidth = MathF.Max(84f, rowWidth - 120f);
            string dropdownId = "node_type_mode_" + column.Id;
            if (Im.Dropdown(dropdownId, LayoutModeNames.AsSpan(), ref selectedModeIndex, dropdownX, y, dropdownWidth))
            {
                NodeFieldDisplayMode newMode = (NodeFieldDisplayMode)Math.Clamp(selectedModeIndex, 0, LayoutModeNames.Length - 1);
                SetFieldDisplayMode(typeLayout, column.Id, newMode);
                changed = true;
            }

            y += 28f;
        }

        if (!changed && workspace.ProjectRevision <= 0)
        {
            return false;
        }

        return changed;
    }

    private static float DrawSelectedNodeInspector(
        IDerpDocEditorContext workspace,
        DocTable table,
        GraphSchema schema,
        NodeTypeLayout typeLayout,
        string viewKey,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        float rowX = contentRect.X + 8f;
        float rowWidth = MathF.Max(160f, contentRect.Width - 16f);
        Im.Text("Selected node".AsSpan(), rowX, y, style.FontSize - 1f, style.TextSecondary);
        y += 18f;
        int selectedRowIndex = workspace.SelectedRowIndex;
        if (selectedRowIndex < 0 || selectedRowIndex >= table.Rows.Count)
        {
            Im.Text("None".AsSpan(), rowX, y, style.FontSize - 1f, style.TextSecondary);
            y += 22f;
            return y;
        }

        DocRow selectedRow = table.Rows[selectedRowIndex];
        string selectedNodeTitle = ResolveNodeTitle(selectedRow, schema, selectedRow.Id);
        string selectedNodeType = ResolveNodeType(selectedRow, schema);
        Im.Text(selectedNodeTitle.AsSpan(), rowX, y, style.FontSize, style.TextPrimary);
        y += 18f;
        if (!string.IsNullOrWhiteSpace(selectedNodeType))
        {
            Im.Text(("Type: " + selectedNodeType).AsSpan(), rowX, y, style.FontSize - 1f, style.TextSecondary);
            y += 18f;
        }

        for (int fieldIndex = 0; fieldIndex < typeLayout.Fields.Count; fieldIndex++)
        {
            NodeFieldLayout fieldLayout = typeLayout.Fields[fieldIndex];
            NodeFieldDisplayMode mode = ParseFieldDisplayMode(fieldLayout.Mode);
            if (mode == NodeFieldDisplayMode.Hidden)
            {
                continue;
            }

            DocColumn? fieldColumn = FindColumnById(table, fieldLayout.ColumnId);
            if (fieldColumn == null)
            {
                continue;
            }

            DocCellValue fieldCellValue = selectedRow.GetCell(fieldColumn);
            string fieldValueText = FormatCellValueForDisplay(fieldColumn, fieldCellValue);
            string fieldLabel = mode switch
            {
                NodeFieldDisplayMode.InputPin => "In",
                NodeFieldDisplayMode.OutputPin => "Out",
                _ => "Set",
            };
            string rowText = fieldLabel + "  " + fieldColumn.Name + ": " + fieldValueText;
            Im.Text(rowText.AsSpan(), rowX, y, style.FontSize - 1f, style.TextPrimary);

            float buttonWidth = 74f;
            float buttonX = rowX + rowWidth - buttonWidth;
            if (Im.Button("Cell fx", buttonX, y - 2f, buttonWidth, 20f))
            {
                OpenCellFormulaEditor(viewKey, selectedRow, fieldColumn);
            }

            y += 22f;
        }

        y = DrawCellFormulaEditor(workspace, table, selectedRow, viewKey, contentRect, y, style);
        return y;
    }

    private static float DrawCellFormulaEditor(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocRow selectedRow,
        string viewKey,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        if (!string.Equals(_formulaEditViewKey, viewKey, StringComparison.Ordinal) ||
            !string.Equals(_formulaEditRowId, selectedRow.Id, StringComparison.Ordinal))
        {
            return y;
        }

        DocColumn? editingColumn = FindColumnById(table, _formulaEditColumnId);
        if (editingColumn == null)
        {
            _formulaEditViewKey = "";
            _formulaEditRowId = "";
            _formulaEditColumnId = "";
            _formulaEditBufferLength = 0;
            return y;
        }

        float x = contentRect.X + 8f;
        float width = MathF.Max(160f, contentRect.Width - 16f);
        Im.Text(("Formula override: " + editingColumn.Name).AsSpan(), x, y, style.FontSize - 1f, style.TextSecondary);
        y += 20f;

        Im.TextInput(
            "node_graph_cell_formula_input",
            _formulaEditBuffer,
            ref _formulaEditBufferLength,
            _formulaEditBuffer.Length,
            x,
            y,
            width);

        if (_formulaEditNeedsFocus)
        {
            int widgetId = Im.Context.GetId("node_graph_cell_formula_input");
            Im.Context.RequestFocus(widgetId);
            _formulaEditNeedsFocus = false;
        }

        y += 28f;
        if (workspace is DocWorkspace mutableWorkspace)
        {
            if (Im.Button("Apply formula", x, y, 96f, 24f))
            {
                ApplyCellFormulaEdit(mutableWorkspace, table, selectedRow, editingColumn);
            }

            if (Im.Button("Clear formula", x + 104f, y, 96f, 24f))
            {
                ClearCellFormulaEdit(mutableWorkspace, table, selectedRow, editingColumn);
            }

            if (Im.Button("Cancel", x + 208f, y, 70f, 24f))
            {
                CloseCellFormulaEditor();
            }
        }

        y += 30f;
        return y;
    }

    private static void ApplyCellFormulaEdit(DocWorkspace workspace, DocTable table, DocRow row, DocColumn column)
    {
        string formulaExpression = _formulaEditBufferLength > 0
            ? new string(_formulaEditBuffer, 0, _formulaEditBufferLength)
            : "";
        DocCellValue oldCellValue = row.GetCell(column);
        DocCellValue newCellValue = oldCellValue.WithCellFormulaExpression(formulaExpression);
        newCellValue.FormulaError = null;
        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetCell,
            TableId = table.Id,
            RowId = row.Id,
            ColumnId = column.Id,
            OldCellValue = oldCellValue,
            NewCellValue = newCellValue,
        });
    }

    private static void ClearCellFormulaEdit(DocWorkspace workspace, DocTable table, DocRow row, DocColumn column)
    {
        DocCellValue oldCellValue = row.GetCell(column);
        if (!oldCellValue.HasCellFormulaExpression)
        {
            return;
        }

        DocCellValue newCellValue = oldCellValue.ClearCellFormulaExpression();
        newCellValue.FormulaError = null;
        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetCell,
            TableId = table.Id,
            RowId = row.Id,
            ColumnId = column.Id,
            OldCellValue = oldCellValue,
            NewCellValue = newCellValue,
        });
        _formulaEditBufferLength = 0;
    }

    private static void OpenCellFormulaEditor(string viewKey, DocRow row, DocColumn column)
    {
        _formulaEditViewKey = viewKey;
        _formulaEditRowId = row.Id;
        _formulaEditColumnId = column.Id;
        string existingExpression = row.GetCell(column).CellFormulaExpression ?? "";
        SetTextBuffer(_formulaEditBuffer, ref _formulaEditBufferLength, existingExpression);
        _formulaEditNeedsFocus = true;
    }

    private static void CloseCellFormulaEditor()
    {
        _formulaEditViewKey = "";
        _formulaEditRowId = "";
        _formulaEditColumnId = "";
        _formulaEditBufferLength = 0;
        _formulaEditNeedsFocus = false;
    }

    private static bool IsNodeTitleEditActive(string viewKey, string rowId)
    {
        return string.Equals(_titleEditViewKey, viewKey, StringComparison.Ordinal) &&
               string.Equals(_titleEditRowId, rowId, StringComparison.Ordinal);
    }

    private static void BeginNodeTitleEdit(string viewKey, DocRow row, DocColumn titleColumn)
    {
        _titleEditViewKey = viewKey;
        _titleEditRowId = row.Id;
        string currentTitle = row.GetCell(titleColumn).StringValue ?? "";
        SetTextBuffer(_titleEditBuffer, ref _titleEditBufferLength, currentTitle);
        _titleEditNeedsFocus = true;
    }

    private static void EndNodeTitleEdit()
    {
        _titleEditViewKey = "";
        _titleEditRowId = "";
        _titleEditBufferLength = 0;
        _titleEditNeedsFocus = false;
    }

    private static bool IsPointInsideNodeHeader(NodeVisual nodeVisual, Vector2 mousePosition)
    {
        float nodeHeaderHeight = NodeHeaderHeight * nodeVisual.Scale;
        var headerRect = new ImRect(
            nodeVisual.ScreenRect.X,
            nodeVisual.ScreenRect.Y,
            nodeVisual.ScreenRect.Width,
            nodeHeaderHeight);
        return headerRect.Contains(mousePosition);
    }

    private static void DrawInlineNodeTitleEditor(
        DocWorkspace workspace,
        DocTable table,
        DocColumn titleColumn,
        NodeVisual nodeVisual,
        string viewKey,
        float titleFontSize)
    {
        if (!IsNodeTitleEditActive(viewKey, nodeVisual.RowId))
        {
            return;
        }

        BeginNodeEditorScaleScope(nodeVisual, out bool pushedScale, out float inverseScale, out float pivotX, out float pivotY);
        try
        {
            float nodeScale = nodeVisual.Scale;
            float nodeHeaderHeight = NodeHeaderHeight * nodeScale;
            float inputX = nodeVisual.ScreenRect.X + (8f * nodeScale);
            float inputY = nodeVisual.ScreenRect.Y + (2f * nodeScale);
            float inputWidth = nodeVisual.ScreenRect.Width - (16f * nodeScale);
            float inputHeight = MathF.Max(16f * nodeScale, nodeHeaderHeight - (4f * nodeScale));
            float localInputX = ToNodeLocalPosition(inputX, pivotX, inverseScale);
            float localInputY = ToNodeLocalPosition(inputY, pivotY, inverseScale);
            float localInputWidth = ToNodeLocalLength(inputWidth, inverseScale);
            float localInputHeight = ToNodeLocalLength(inputHeight, inverseScale);
            string widgetId = "node_title_edit_" + table.Id + "_" + nodeVisual.RowId;
            bool changed = ImTextArea.DrawAt(
                widgetId,
                _titleEditBuffer,
                ref _titleEditBufferLength,
                _titleEditBuffer.Length,
                localInputX,
                localInputY,
                localInputWidth,
                localInputHeight,
                wordWrap: false,
                fontSizePx: titleFontSize,
                flags: ImTextArea.ImTextAreaFlags.SingleLine |
                       ImTextArea.ImTextAreaFlags.NoBackground |
                       ImTextArea.ImTextAreaFlags.NoBorder |
                       ImTextArea.ImTextAreaFlags.NoRounding);
            if (_titleEditNeedsFocus)
            {
                int widgetHash = Im.Context.GetId(widgetId);
                Im.Context.RequestFocus(widgetHash);
                _titleEditNeedsFocus = false;
            }

            if (changed)
            {
                string updatedTitle = _titleEditBufferLength > 0
                    ? new string(_titleEditBuffer, 0, _titleEditBufferLength)
                    : "";
                SetNodeCellValue(workspace, table, nodeVisual.Row, titleColumn, DocCellValue.Text(updatedTitle));
            }

            if (Im.Context.Input.KeyEnter || Im.Context.Input.KeyEscape)
            {
                EndNodeTitleEdit();
            }
        }
        finally
        {
            EndNodeEditorScaleScope(pushedScale);
        }
    }

    private static void DrawGrid(ImRect contentRect, NodeGraphViewState viewState, ImStyle style)
    {
        const float baseGridSize = 40f;
        float gridSize = baseGridSize * viewState.Zoom;
        if (gridSize < 12f)
        {
            return;
        }

        // Keep grid anchored in world-space:
        // screen = content + pan + world * zoom
        // World grid lines are at worldX = n * baseGridSize.
        // Convert pan to a stable [0..gridSize) offset using floor-based modulus so major lines don't "swim".
        float panX = viewState.Pan.X;
        float panY = viewState.Pan.Y;
        float offsetX = panX - MathF.Floor(panX / gridSize) * gridSize;
        float offsetY = panY - MathF.Floor(panY / gridSize) * gridSize;

        int worldIndexX = -(int)MathF.Floor(panX / gridSize);
        int worldIndexY = -(int)MathF.Floor(panY / gridSize);

        // Light, subtle grid: keep visible on the dark canvas without competing with nodes/wires.
        uint minorGridColor = ImStyle.WithAlpha(ImStyle.Lerp(style.Background, style.TextSecondary, 0.24f), 10);
        uint majorGridColor = ImStyle.WithAlpha(ImStyle.Lerp(style.Background, style.TextSecondary, 0.34f), 15);

        int majorStride = 5;
        for (float lineX = contentRect.X + offsetX; lineX < contentRect.Right; lineX += gridSize, worldIndexX++)
        {
            uint color = (worldIndexX % majorStride) == 0 ? majorGridColor : minorGridColor;
            Im.DrawLine(lineX, contentRect.Y, lineX, contentRect.Bottom, 1f, color);
        }

        for (float lineY = contentRect.Y + offsetY; lineY < contentRect.Bottom; lineY += gridSize, worldIndexY++)
        {
            uint color = (worldIndexY % majorStride) == 0 ? majorGridColor : minorGridColor;
            Im.DrawLine(contentRect.X, lineY, contentRect.Right, lineY, 1f, color);
        }
    }

    private static void DrawEdges(
        IDerpDocEditorContext workspace,
        DocTable table,
        GraphSchema schema,
        NodeGraphViewSettings settings,
        ImStyle style)
    {
        _ = settings;
        if (schema.EdgeTable == null ||
            schema.EdgeFromNodeColumn == null ||
            schema.EdgeToNodeColumn == null ||
            schema.EdgeFromPinColumn == null ||
            schema.EdgeToPinColumn == null)
        {
            return;
        }

        uint fallbackEdgeColor = ImStyle.WithAlpha(style.TextSecondary, 190);
        for (int edgeRowIndex = 0; edgeRowIndex < schema.EdgeTable.Rows.Count; edgeRowIndex++)
        {
            DocRow edgeRow = schema.EdgeTable.Rows[edgeRowIndex];
            string fromNodeId = edgeRow.GetCell(schema.EdgeFromNodeColumn).StringValue ?? "";
            string toNodeId = edgeRow.GetCell(schema.EdgeToNodeColumn).StringValue ?? "";
            if (string.IsNullOrWhiteSpace(fromNodeId) || string.IsNullOrWhiteSpace(toNodeId))
            {
                continue;
            }

            if (!NodeIndexByRowId.TryGetValue(fromNodeId, out int fromNodeIndex) ||
                !NodeIndexByRowId.TryGetValue(toNodeId, out int toNodeIndex))
            {
                continue;
            }

            NodeVisual fromNode = NodeVisuals[fromNodeIndex];
            NodeVisual toNode = NodeVisuals[toNodeIndex];
            string fromPinId = edgeRow.GetCell(schema.EdgeFromPinColumn).StringValue ?? "";
            string toPinId = edgeRow.GetCell(schema.EdgeToPinColumn).StringValue ?? "";
            Vector2 fromAnchor = ResolvePinAnchor(workspace, table, schema, settings, fromNode, fromPinId, outputPin: true);
            Vector2 toAnchor = ResolvePinAnchor(workspace, table, schema, settings, toNode, toPinId, outputPin: false);
            DocColumn? sourcePinColumn = FindColumnByPinIdentifier(table, fromPinId);
            uint edgeColor = sourcePinColumn != null
                ? ImStyle.WithAlpha(ResolvePinColor(sourcePinColumn.Kind, style), 190)
                : fallbackEdgeColor;
            DrawEdgePath(fromAnchor, toAnchor, edgeColor, verticalPreferred: false);
        }

        DrawExecutionEdges(workspace, table, schema, settings, style);
    }

    private static void DrawExecutionEdges(
        IDerpDocEditorContext workspace,
        DocTable table,
        GraphSchema schema,
        NodeGraphViewSettings settings,
        ImStyle style)
    {
        _ = settings;
        DocColumn? executionOutputColumn = schema.ExecutionOutputColumn;
        if (executionOutputColumn == null)
        {
            return;
        }

        for (int nodeIndex = 0; nodeIndex < NodeVisuals.Count; nodeIndex++)
        {
            NodeVisual sourceNode = NodeVisuals[nodeIndex];
            NodeTypeLayout? typeLayout = FindTypeLayout(settings, sourceNode.TypeName);
            CollectLayoutColumns(table, typeLayout, InputPinColumnsScratch, OutputPinColumnsScratch, SettingColumnsScratch);
            if (HasSubtableExecutionOutputColumns(workspace.Project, SettingColumnsScratch))
            {
                continue;
            }

            string targetNodeRowId = sourceNode.Row.GetCell(executionOutputColumn).StringValue ?? "";
            if (string.IsNullOrWhiteSpace(targetNodeRowId))
            {
                continue;
            }

            if (!NodeIndexByRowId.TryGetValue(targetNodeRowId, out int targetNodeIndex))
            {
                continue;
            }

            NodeVisual targetNode = NodeVisuals[targetNodeIndex];
            Vector2 fromAnchor = ResolvePinAnchor(workspace, table, schema, settings, sourceNode, ExecutionOutputPinId, outputPin: true);
            Vector2 toAnchor = ResolvePinAnchor(workspace, table, schema, settings, targetNode, ExecutionInputPinId, outputPin: false);
            uint edgeColor = ImStyle.WithAlpha(ResolveExecutionFlowColor(style), 220);
            DrawEdgePath(fromAnchor, toAnchor, edgeColor, verticalPreferred: true);
        }
    }

    private static void DrawSubtableExecutionEdges(
        IDerpDocEditorContext workspace,
        DocTable nodeTable,
        GraphSchema schema,
        NodeGraphViewSettings settings,
        ImStyle style)
    {
        for (int pinIndex = 0; pinIndex < PinVisuals.Count; pinIndex++)
        {
            PinVisual subtablePin = PinVisuals[pinIndex];
            if (!subtablePin.IsOutput ||
                !subtablePin.IsExecutionOutput ||
                !subtablePin.PinId.StartsWith(SubtableExecutionPinPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(subtablePin.SourceTableId) ||
                string.IsNullOrWhiteSpace(subtablePin.SourceRowId) ||
                string.IsNullOrWhiteSpace(subtablePin.SourceColumnId))
            {
                continue;
            }

            DocTable? sourceTable = FindTableById(workspace.Project, subtablePin.SourceTableId);
            if (sourceTable == null)
            {
                continue;
            }

            DocRow? sourceRow = FindRowById(sourceTable, subtablePin.SourceRowId);
            DocColumn? sourceColumn = FindColumnById(sourceTable, subtablePin.SourceColumnId);
            if (sourceRow == null || sourceColumn == null || sourceColumn.Kind != DocColumnKind.Relation)
            {
                continue;
            }

            string targetNodeRowId = sourceRow.GetCell(sourceColumn).StringValue ?? "";
            if (string.IsNullOrWhiteSpace(targetNodeRowId))
            {
                continue;
            }

            if (!NodeIndexByRowId.TryGetValue(targetNodeRowId, out int targetNodeIndex))
            {
                continue;
            }

            NodeVisual targetNode = NodeVisuals[targetNodeIndex];
            Vector2 toAnchor = ResolvePinAnchor(
                workspace,
                nodeTable,
                schema,
                settings,
                targetNode,
                ExecutionInputPinId,
                outputPin: false);
            uint edgeColor = ImStyle.WithAlpha(ResolveExecutionFlowColor(style), 220);
            DrawEdgePath(subtablePin.Center, toAnchor, edgeColor, verticalPreferred: true);
        }
    }

    private static void DrawEdgePath(Vector2 fromAnchor, Vector2 toAnchor, uint color, bool verticalPreferred)
    {
        Vector2 controlPoint1;
        Vector2 controlPoint2;
        if (verticalPreferred)
        {
            float verticalDistance = MathF.Abs(toAnchor.Y - fromAnchor.Y);
            float verticalOffset = MathF.Max(42f, verticalDistance * 0.45f);
            float verticalDirection = toAnchor.Y >= fromAnchor.Y ? 1f : -1f;
            controlPoint1 = new Vector2(fromAnchor.X, fromAnchor.Y + verticalOffset * verticalDirection);
            controlPoint2 = new Vector2(toAnchor.X, toAnchor.Y - verticalOffset * verticalDirection);
        }
        else
        {
            float horizontalDistance = MathF.Abs(toAnchor.X - fromAnchor.X);
            float horizontalOffset = MathF.Max(42f, horizontalDistance * 0.45f);
            controlPoint1 = new Vector2(fromAnchor.X + horizontalOffset, fromAnchor.Y);
            controlPoint2 = new Vector2(toAnchor.X - horizontalOffset, toAnchor.Y);
        }

        for (int segmentIndex = 0; segmentIndex <= EdgeCurveSegments; segmentIndex++)
        {
            float t = segmentIndex / (float)EdgeCurveSegments;
            EdgeCurvePointsScratch[segmentIndex] = EvaluateCubicBezier(fromAnchor, controlPoint1, controlPoint2, toAnchor, t);
        }

        Im.DrawPolyline(EdgeCurvePointsScratch.AsSpan(0, EdgeCurveSegments + 1), 2f, color);
    }

    private static Vector2 EvaluateCubicBezier(
        in Vector2 p0,
        in Vector2 p1,
        in Vector2 p2,
        in Vector2 p3,
        float t)
    {
        float clampedT = Math.Clamp(t, 0f, 1f);
        float oneMinusT = 1f - clampedT;
        float oneMinusTSquared = oneMinusT * oneMinusT;
        float oneMinusTCubed = oneMinusTSquared * oneMinusT;
        float tSquared = clampedT * clampedT;
        float tCubed = tSquared * clampedT;
        return (oneMinusTCubed * p0) +
               (3f * oneMinusTSquared * clampedT * p1) +
               (3f * oneMinusT * tSquared * p2) +
               (tCubed * p3);
    }

    private static void DrawNodesAndHandleInteraction(
        IDerpDocEditorContext workspace,
        DocTable table,
        string viewKey,
        GraphSchema schema,
        NodeGraphViewSettings settings,
        NodeGraphViewState viewState,
        ImRect contentRect,
        bool interactive)
    {
        var style = Im.Style;
        var input = Im.Context.Input;
        Vector2 mouseScreenPosition = Im.MousePos;
        PinVisuals.Clear();
        int hoveredNodeIndex = FindTopmostNodeIndex(mouseScreenPosition);
        for (int nodeIndex = 0; nodeIndex < NodeVisuals.Count; nodeIndex++)
        {
            NodeVisual nodeVisual = NodeVisuals[nodeIndex];
            bool nodeSelected = workspace.SelectedRowIndex == nodeVisual.SourceRowIndex;
            bool hovered = nodeIndex == hoveredNodeIndex;
            float nodeScale = nodeVisual.Scale;
            float nodeCornerRadius = MathF.Max(3f, NodeCornerRadius * nodeScale);
            float nodeHeaderHeight = NodeHeaderHeight * nodeScale;
            float nodeShadowOffset = MathF.Max(2f, NodeShadowOffset * nodeScale);
            float titleFontSize = Math.Clamp((style.FontSize - 1f) * nodeScale, 9f, style.FontSize + 2f);

            uint accentColor = ResolveNodeAccentColor(nodeVisual.TypeName, style);
            uint fillColor = hovered
                ? ImStyle.WithAlpha(style.Surface, 245)
                : style.Surface;
            uint borderColor = nodeSelected ? style.Primary : style.Border;
            float borderThickness = nodeSelected ? 2f : 1f;
            uint shadowColor = ImStyle.WithAlpha(0xFF000000, 90);

            Im.DrawRoundedRect(
                nodeVisual.ScreenRect.X + nodeShadowOffset,
                nodeVisual.ScreenRect.Y + nodeShadowOffset,
                nodeVisual.ScreenRect.Width,
                nodeVisual.ScreenRect.Height,
                nodeCornerRadius,
                shadowColor);
            Im.DrawRoundedRect(
                nodeVisual.ScreenRect.X,
                nodeVisual.ScreenRect.Y,
                nodeVisual.ScreenRect.Width,
                nodeVisual.ScreenRect.Height,
                nodeCornerRadius,
                fillColor);
            Im.DrawRoundedRectStroke(
                nodeVisual.ScreenRect.X,
                nodeVisual.ScreenRect.Y,
                nodeVisual.ScreenRect.Width,
                nodeVisual.ScreenRect.Height,
                nodeCornerRadius,
                borderColor,
                borderThickness);
            Im.DrawRoundedRectPerCorner(
                nodeVisual.ScreenRect.X,
                nodeVisual.ScreenRect.Y,
                nodeVisual.ScreenRect.Width,
                nodeHeaderHeight,
                nodeCornerRadius,
                nodeCornerRadius,
                0f,
                0f,
                ImStyle.WithAlpha(accentColor, 160));

            float textX = nodeVisual.ScreenRect.X + (8f * nodeScale);
            float titleY = nodeVisual.ScreenRect.Y + (nodeHeaderHeight - titleFontSize) * 0.5f;
            DocColumn? titleColumn = schema.TitleColumn;
            if (titleColumn != null &&
                IsNodeTitleEditActive(viewKey, nodeVisual.RowId) &&
                workspace is DocWorkspace mutableWorkspace)
            {
                DrawInlineNodeTitleEditor(
                    mutableWorkspace,
                    table,
                    titleColumn,
                    nodeVisual,
                    viewKey,
                    titleFontSize);
            }
            else
            {
                Im.Text(nodeVisual.Title.AsSpan(), textX, titleY, titleFontSize, style.TextPrimary);
            }

            DrawNodePinsAndSettings(workspace, table, schema, nodeVisual, settings, viewState, interactive, style);
        }

        int hoveredPinIndex = FindTopmostPinIndex(mouseScreenPosition);
        bool hasHoveredPin = hoveredPinIndex >= 0;
        PinVisual hoveredPin = hasHoveredPin ? PinVisuals[hoveredPinIndex] : default;

        if (interactive)
        {
            HandleNodeKeyboardShortcuts(workspace, table, schema, ref viewState);

            bool pointerInsideCanvas = contentRect.Contains(mouseScreenPosition);
            if (pointerInsideCanvas && input.MouseRightPressed)
            {
                if (hoveredNodeIndex >= 0)
                {
                    NodeVisual hoveredNode = NodeVisuals[hoveredNodeIndex];
                    workspace.SelectedRowIndex = hoveredNode.SourceRowIndex;
                    viewState.ContextMenuNodeRowId = hoveredNode.RowId;
                    viewState.CreateMenuOpen = false;
                    ImContextMenu.OpenAt(NodeContextMenuId, mouseScreenPosition.X, mouseScreenPosition.Y);
                }
                else
                {
                    viewState.ContextMenuNodeRowId = "";
                    OpenCreateNodeMenu(ref viewState, mouseScreenPosition, contentRect);
                }
            }

            if (input.KeyTab && !Im.Context.AnyActive && pointerInsideCanvas)
            {
                OpenCreateNodeMenu(ref viewState, mouseScreenPosition, contentRect);
            }

            if (viewState.CreateMenuOpen &&
                input.MousePressed &&
                !IsPointInsideCreateMenu(viewState, mouseScreenPosition))
            {
                viewState.CreateMenuOpen = false;
            }

            bool consumedPrimaryClick = false;
            if (!viewState.WireDragActive &&
                input.MousePressed &&
                input.IsDoubleClick &&
                hoveredNodeIndex >= 0 &&
                schema.TitleColumn != null &&
                workspace is DocWorkspace)
            {
                NodeVisual hoveredNode = NodeVisuals[hoveredNodeIndex];
                if (IsPointInsideNodeHeader(hoveredNode, mouseScreenPosition))
                {
                    workspace.SelectedRowIndex = hoveredNode.SourceRowIndex;
                    BeginNodeTitleEdit(viewKey, hoveredNode.Row, schema.TitleColumn);
                    consumedPrimaryClick = true;
                }
            }

            if (!viewState.WireDragActive &&
                hasHoveredPin &&
                input.MousePressed &&
                !input.KeyAlt)
            {
                BeginWireDrag(ref viewState, hoveredPin);
                workspace.SelectedRowIndex = hoveredPin.NodeRowIndex;
                consumedPrimaryClick = true;
            }

            if (!viewState.WireDragActive &&
                hasHoveredPin &&
                !hoveredPin.IsOutput &&
                input.KeyAlt &&
                input.MousePressed &&
                workspace is DocWorkspace removeWorkspace)
            {
                RemoveIncomingConnections(removeWorkspace, table, schema, hoveredPin);
                consumedPrimaryClick = true;
            }

            if (viewState.WireDragActive)
            {
                if (input.MouseReleased)
                {
                    if (hasHoveredPin &&
                        hoveredPin.IsOutput != viewState.WireFromIsOutput &&
                        workspace is DocWorkspace connectWorkspace)
                    {
                        if (viewState.WireFromIsOutput)
                        {
                            TryCreateConnection(connectWorkspace, table, schema, viewState, hoveredPin);
                        }
                        else
                        {
                            TryCreateConnectionFromReverseDrag(connectWorkspace, table, schema, viewState, hoveredPin);
                        }
                    }

                    EndWireDrag(ref viewState);
                    consumedPrimaryClick = true;
                }
                else if (!input.MouseDown)
                {
                    EndWireDrag(ref viewState);
                }
            }

            if (!viewState.WireDragActive && !consumedPrimaryClick && input.MousePressed)
            {
                bool clickOwnedByWidget = Im.Context.MouseDownOwnerLeft != 0 || Im.Context.HotId != 0 || Im.Context.ActiveId != 0;
                if (clickOwnedByWidget)
                {
                    if (hoveredNodeIndex >= 0)
                    {
                        NodeVisual hoveredNode = NodeVisuals[hoveredNodeIndex];
                        workspace.SelectedRowIndex = hoveredNode.SourceRowIndex;
                    }
                }
                else if (hasHoveredPin)
                {
                    workspace.SelectedRowIndex = hoveredPin.NodeRowIndex;
                }
                else if (hoveredNodeIndex >= 0)
                {
                    NodeVisual hoveredNode = NodeVisuals[hoveredNodeIndex];
                    workspace.SelectedRowIndex = hoveredNode.SourceRowIndex;
                    BeginNodeDrag(table, schema, viewState, hoveredNode, mouseScreenPosition);
                }
                else if (pointerInsideCanvas)
                {
                    workspace.SelectedRowIndex = -1;
                }
            }

            UpdateNodeDrag(workspace, table, schema, viewState, mouseScreenPosition, input);
        }

        DrawNodeContextMenu(workspace, table, schema, ref viewState);

        if (viewState.WireDragActive)
        {
            bool sourceIsExecutionInput = !viewState.WireFromIsOutput &&
                                          string.Equals(viewState.WireFromPinId, ExecutionInputPinId, StringComparison.Ordinal);
            bool sourceIsExecutionFlow = viewState.WireFromExecutionOutput || sourceIsExecutionInput;
            uint previewColor = sourceIsExecutionFlow
                ? ImStyle.WithAlpha(ResolveExecutionFlowColor(style), 215)
                : ImStyle.WithAlpha(ResolvePinColor(viewState.WireFromColumnKind, style), 215);
            DrawCompatiblePinHints(
                viewState.WireFromIsOutput,
                viewState.WireFromExecutionOutput,
                sourceIsExecutionInput,
                viewState.WireFromColumnKind,
                style);
            bool previewVertical = sourceIsExecutionFlow;
            DrawEdgePath(viewState.WireFromAnchor, mouseScreenPosition, previewColor, previewVertical);
        }

        if (hasHoveredPin)
        {
            ImRect hoveredPinRect = hoveredPin.HitRect;
            uint hoveredStrokeColor = style.Primary;
            float hoveredStrokeThickness = 1.5f;
            if (viewState.WireDragActive)
            {
                bool sourceIsExecutionInput = !viewState.WireFromIsOutput &&
                                              string.Equals(viewState.WireFromPinId, ExecutionInputPinId, StringComparison.Ordinal);
                bool sourceCanDriveExecution = IsExecutionSourcePin(viewState.WireFromExecutionOutput, viewState.WireFromColumnKind);
                bool hoveredPinCompatible = IsPinCompatibleForWireDrag(
                    viewState.WireFromIsOutput,
                    sourceCanDriveExecution,
                    sourceIsExecutionInput,
                    viewState.WireFromColumnKind,
                    hoveredPin);
                uint wireSourceColor = (viewState.WireFromExecutionOutput || sourceIsExecutionInput)
                    ? ResolveExecutionFlowColor(style)
                    : ResolvePinColor(viewState.WireFromColumnKind, style);
                hoveredStrokeColor = hoveredPinCompatible
                    ? ImStyle.WithAlpha(wireSourceColor, 240)
                    : ImStyle.WithAlpha(style.Secondary, 165);
                hoveredStrokeThickness = hoveredPinCompatible ? 2f : 1.2f;
            }

            Im.DrawRoundedRectStroke(
                hoveredPinRect.X,
                hoveredPinRect.Y,
                hoveredPinRect.Width,
                hoveredPinRect.Height,
                MathF.Max(2f, hoveredPinRect.Width * 0.2f),
                hoveredStrokeColor,
                hoveredStrokeThickness);
        }
    }

    private static void HandleNodeKeyboardShortcuts(
        IDerpDocEditorContext workspace,
        DocTable table,
        GraphSchema schema,
        ref NodeGraphViewState viewState)
    {
        var input = Im.Context.Input;
        bool copyShortcutDown = input.KeyCtrlC;
        bool pasteShortcutDown = input.KeyCtrlV;
        bool deleteShortcutDown = input.KeyDelete || input.KeyBackspace;
        bool shortcutsBlocked = Im.Context.WantCaptureKeyboard ||
                                Im.Context.AnyActive ||
                                ImModal.IsAnyOpen ||
                                Im.IsAnyDropdownOpen ||
                                viewState.CreateMenuOpen ||
                                viewState.WireDragActive ||
                                viewState.DragActive ||
                                ImContextMenu.IsOpen(NodeContextMenuId);

        if (!shortcutsBlocked)
        {
            if (copyShortcutDown &&
                !viewState.ShortcutCopyDown &&
                TryGetSelectedNodeRow(table, workspace.SelectedRowIndex, out DocRow selectedRow))
            {
                CopyNodeToClipboard(table, selectedRow);
                if (workspace is DocWorkspace copyWorkspace)
                {
                    copyWorkspace.SetStatusMessage("Node copied.");
                }
            }

            if (pasteShortcutDown &&
                !viewState.ShortcutPasteDown &&
                workspace is DocWorkspace pasteWorkspace)
            {
                PasteNodeFromClipboard(pasteWorkspace, table, schema, ref viewState);
            }

            if (deleteShortcutDown &&
                !viewState.ShortcutDeleteDown &&
                workspace is DocWorkspace deleteWorkspace &&
                TryGetSelectedNodeRow(table, workspace.SelectedRowIndex, out DocRow deleteRow))
            {
                DeleteNode(deleteWorkspace, table, schema, deleteRow.Id, ref viewState);
            }
        }

        viewState.ShortcutCopyDown = copyShortcutDown;
        viewState.ShortcutPasteDown = pasteShortcutDown;
        viewState.ShortcutDeleteDown = deleteShortcutDown;
    }

    private static void DrawNodeContextMenu(
        IDerpDocEditorContext workspace,
        DocTable table,
        GraphSchema schema,
        ref NodeGraphViewState viewState)
    {
        if (!ImContextMenu.Begin(NodeContextMenuId))
        {
            return;
        }

        bool hasContextNode = !string.IsNullOrWhiteSpace(viewState.ContextMenuNodeRowId);
        DocRow? contextRow = hasContextNode ? FindRowById(table, viewState.ContextMenuNodeRowId) : null;
        if (!hasContextNode || contextRow == null)
        {
            ImContextMenu.ItemDisabled("Node");
            ImContextMenu.End();
            return;
        }

        string contextLabel = contextRow.Id;
        if (schema.TitleColumn != null)
        {
            string title = contextRow.GetCell(schema.TitleColumn).StringValue ?? "";
            if (!string.IsNullOrWhiteSpace(title))
            {
                contextLabel = title;
            }
        }

        ImContextMenu.ItemDisabled(contextLabel);
        ImContextMenu.Separator();

        if (ImContextMenu.Item("Copy node"))
        {
            CopyNodeToClipboard(table, contextRow);
            if (workspace is DocWorkspace copyWorkspace)
            {
                copyWorkspace.SetStatusMessage("Node copied.");
            }
        }

        bool canPasteNode = CanPasteNodeFromClipboard(table);
        if (canPasteNode)
        {
            if (ImContextMenu.Item("Paste node"))
            {
                if (workspace is DocWorkspace pasteWorkspace)
                {
                    PasteNodeFromClipboard(pasteWorkspace, table, schema, ref viewState);
                }
            }
        }
        else
        {
            ImContextMenu.ItemDisabled("Paste node");
        }

        if (workspace is DocWorkspace mutableWorkspace)
        {
            if (ImContextMenu.Item("Delete node"))
            {
                DeleteNode(mutableWorkspace, table, schema, contextRow.Id, ref viewState);
            }
        }
        else
        {
            ImContextMenu.ItemDisabled("Delete node");
        }

        ImContextMenu.End();
    }

    private static void DrawNodePinsAndSettings(
        IDerpDocEditorContext workspace,
        DocTable table,
        GraphSchema schema,
        NodeVisual nodeVisual,
        NodeGraphViewSettings settings,
        NodeGraphViewState viewState,
        bool interactive,
        ImStyle style)
    {
        _activeNodeWidth = nodeVisual.WidthUnits;
        NodeTypeLayout? typeLayout = FindTypeLayout(settings, nodeVisual.TypeName);
        _ = viewState;
        float nodeScale = nodeVisual.Scale;
        float nodeHeaderHeight = NodeHeaderHeight * nodeScale;
        float pinDotSize = MathF.Max(1.5f, NodePinDotSize * nodeScale);
        float pinInset = NodeInlineHorizontalPadding * nodeScale;
        float inputLabelX = nodeVisual.ScreenRect.X + pinInset + 6f;
        float outputLabelRightX = nodeVisual.ScreenRect.Right - pinInset - 6f;
        float settingFontSize = Math.Clamp((style.FontSize - 2f) * nodeScale, 8f, style.FontSize);
        float pinLabelFontSize = MathF.Max(5f, (style.FontSize - 2f) * nodeScale);
        DocWorkspace? mutableWorkspace = workspace as DocWorkspace;
        bool allowInlineEditing = interactive && mutableWorkspace != null;
        CollectLayoutColumns(table, typeLayout, InputPinColumnsScratch, OutputPinColumnsScratch, SettingColumnsScratch);
        DocColumn? defaultExecOutputColumn = schema.ExecutionOutputColumn;
        bool useDefaultExecutionOutput = defaultExecOutputColumn != null &&
                                         !HasSubtableExecutionOutputColumns(workspace.Project, SettingColumnsScratch);
        bool execInputConnected = HasIncomingExecutionConnections(workspace.Project, table, schema, nodeVisual.RowId);
        bool execOutputConnected = useDefaultExecutionOutput &&
                                   defaultExecOutputColumn != null &&
                                   IsExecutionOutputConnected(nodeVisual.Row, defaultExecOutputColumn);

        int pinRowCount = Math.Max(InputPinColumnsScratch.Count, OutputPinColumnsScratch.Count);

        float pinAreaTop = nodeVisual.ScreenRect.Y + nodeHeaderHeight;
        float execPinSize = MathF.Max(3f, NodeExecPinSize * nodeScale);
        float execPinCenterX = ResolveExecutionPinCenterX(nodeVisual);
        float execInputPinCenterY = ResolveExecutionPinCenterY(nodeVisual, isOutputPin: false, execPinSize);
        float execOutputPinCenterY = ResolveExecutionPinCenterY(nodeVisual, isOutputPin: true, execPinSize);
        uint execPinColor = ResolveExecutionFlowColor(style);
        DrawExecutionPinGlyph(execPinCenterX, execInputPinCenterY, execPinSize, execPinColor, filled: execInputConnected);
        if (useDefaultExecutionOutput)
        {
            DrawExecutionPinGlyph(execPinCenterX, execOutputPinCenterY, execPinSize, execPinColor, filled: execOutputConnected);
        }
        RegisterPinVisualAtPosition(
            nodeVisual,
            ExecutionInputPinId,
            ExecutionInputPinId,
            DocColumnKind.Relation,
            isOutputPin: false,
            execPinCenterX,
            execInputPinCenterY,
            execPinSize,
            table.Id,
            nodeVisual.Row.Id,
            ExecutionInputPinId,
            isExecutionOutput: false);
        if (useDefaultExecutionOutput)
        {
            RegisterPinVisualAtPosition(
                nodeVisual,
                defaultExecOutputColumn!.Id,
                ExecutionOutputPinId,
                DocColumnKind.Relation,
                isOutputPin: true,
                execPinCenterX,
                execOutputPinCenterY,
                execPinSize,
                table.Id,
                nodeVisual.Row.Id,
                defaultExecOutputColumn.Id,
                isExecutionOutput: true);
        }

        float pinRowTop = pinAreaTop;
        for (int pinRowIndex = 0; pinRowIndex < pinRowCount; pinRowIndex++)
        {
            DocColumn? inputColumn = pinRowIndex < InputPinColumnsScratch.Count ? InputPinColumnsScratch[pinRowIndex] : null;
            DocColumn? outputColumn = pinRowIndex < OutputPinColumnsScratch.Count ? OutputPinColumnsScratch[pinRowIndex] : null;
            bool showInputEditor = inputColumn != null &&
                                   ShouldShowInlinePinEditor(schema, table, nodeVisual.RowId, inputColumn, inputPin: true, allowInlineEditing);
            bool showOutputEditor = outputColumn != null &&
                                    ShouldShowInlinePinEditor(schema, table, nodeVisual.RowId, outputColumn, inputPin: false, allowInlineEditing);
            float pinRowHeightUnits = ComputePinRowHeightUnits(
                nodeVisual.Row,
                inputColumn,
                showInputEditor,
                outputColumn,
                showOutputEditor,
                allowInlineEditing);
            float pinRowHeightForRow = pinRowHeightUnits * nodeScale;
            float pinCenterY = pinRowTop + (pinRowHeightForRow * 0.5f);
            float labelCenterY = pinCenterY;
            bool rowHasExpandedEditor = false;
            if (showInputEditor && inputColumn != null)
            {
                _ = ComputePinEditorControlHeightUnits(nodeVisual.Row, inputColumn, inputPin: true, out bool expandedInputEditor);
                rowHasExpandedEditor |= expandedInputEditor;
            }

            if (showOutputEditor && outputColumn != null)
            {
                _ = ComputePinEditorControlHeightUnits(nodeVisual.Row, outputColumn, inputPin: false, out bool expandedOutputEditor);
                rowHasExpandedEditor |= expandedOutputEditor;
            }

            if (rowHasExpandedEditor)
            {
                float labelYOffset = (NodeExpandedTextLabelTopPadding + GetInlineLabelHeightUnits() * 0.5f) * nodeScale;
                labelCenterY = pinRowTop + labelYOffset;
            }

            if (inputColumn != null)
            {
                float pinCenterX = ResolvePinCenterX(nodeVisual, isOutputPin: false, pinDotSize);
                uint pinColor = ResolvePinColor(inputColumn.Kind, style);
                uint pinTextColor = ImStyle.WithAlpha(pinColor, 220);
                DrawPinDot(pinCenterX, labelCenterY, pinDotSize, pinColor);
                Im.Text(inputColumn.Name.AsSpan(), inputLabelX, labelCenterY - pinLabelFontSize * 0.5f, pinLabelFontSize, pinTextColor);
                RegisterPinVisual(
                    nodeVisual,
                    inputColumn,
                    isOutputPin: false,
                    labelCenterY,
                    pinDotSize,
                    table.Id,
                    nodeVisual.Row.Id,
                    isExecutionOutput: false);

                if (showInputEditor && mutableWorkspace != null)
                {
                    DrawInlinePinEditor(
                        mutableWorkspace,
                        table,
                        nodeVisual.Row,
                        inputColumn,
                        nodeVisual,
                        pinRowTop,
                        pinRowHeightForRow,
                        inputPin: true);
                }
                else
                {
                    DrawPinValuePreview(
                        nodeVisual,
                        nodeVisual.Row,
                        inputColumn,
                        inputPin: true,
                        labelCenterY,
                        pinLabelFontSize,
                        style);
                }
            }

            if (outputColumn != null)
            {
                float pinCenterX = ResolvePinCenterX(nodeVisual, isOutputPin: true, pinDotSize);
                uint pinColor = ResolvePinColor(outputColumn.Kind, style);
                uint pinTextColor = ImStyle.WithAlpha(pinColor, 220);
                DrawPinDot(pinCenterX, labelCenterY, pinDotSize, pinColor);
                float textWidth = Im.MeasureTextWidth(outputColumn.Name.AsSpan(), pinLabelFontSize);
                Im.Text(outputColumn.Name.AsSpan(), outputLabelRightX - textWidth, labelCenterY - pinLabelFontSize * 0.5f, pinLabelFontSize, pinTextColor);
                RegisterPinVisual(
                    nodeVisual,
                    outputColumn,
                    isOutputPin: true,
                    labelCenterY,
                    pinDotSize,
                    table.Id,
                    nodeVisual.Row.Id,
                    isExecutionOutput: IsExecutionOutputColumn(schema, outputColumn));

                if (showOutputEditor && mutableWorkspace != null)
                {
                    DrawInlinePinEditor(
                        mutableWorkspace,
                        table,
                        nodeVisual.Row,
                        outputColumn,
                        nodeVisual,
                        pinRowTop,
                        pinRowHeightForRow,
                        inputPin: false);
                }
                else
                {
                    DrawPinValuePreview(
                        nodeVisual,
                        nodeVisual.Row,
                        outputColumn,
                        inputPin: false,
                        labelCenterY,
                        pinLabelFontSize,
                        style);
                }
            }

            pinRowTop += pinRowHeightForRow;
            if (pinRowIndex < pinRowCount - 1)
            {
                pinRowTop += NodeRowVerticalGap * nodeScale;
            }
        }

        float settingsRowTop = pinRowTop;
        for (int settingIndex = 0; settingIndex < SettingColumnsScratch.Count; settingIndex++)
        {
            DocColumn settingColumn = SettingColumnsScratch[settingIndex];
            float settingRowHeightUnits = ComputeSettingRowHeightUnits(
                workspace,
                table,
                nodeVisual.Row,
                settingColumn,
                allowInlineEditing);
            float settingRowHeightForRow = settingRowHeightUnits * nodeScale;

            if (allowInlineEditing && mutableWorkspace != null)
            {
                DrawInlineSettingEditor(
                    mutableWorkspace,
                    table,
                    nodeVisual.Row,
                    settingColumn,
                    nodeVisual,
                    settingsRowTop,
                    settingRowHeightForRow,
                    style);
            }
            else if (settingColumn.Kind == DocColumnKind.Subtable && workspace is DocWorkspace readOnlyWorkspace)
            {
                DrawInlineSubtableSettingEditor(
                    readOnlyWorkspace,
                    table,
                    nodeVisual.Row,
                    settingColumn,
                    nodeVisual,
                    settingsRowTop,
                    settingRowHeightForRow,
                    style,
                    allowActions: false);
            }
            else
            {
                string valueText = FormatCellValueForDisplay(settingColumn, nodeVisual.Row.GetCell(settingColumn));
                string settingText = settingColumn.Name + ": " + valueText;
                Im.Text(settingText.AsSpan(), nodeVisual.ScreenRect.X + NodeInlineHorizontalPadding * nodeScale, settingsRowTop + 2f * nodeScale, settingFontSize, style.TextSecondary);
            }

            if (settingColumn.Kind == DocColumnKind.Relation)
            {
                float relationPinCenterY = settingsRowTop + (settingRowHeightForRow * 0.5f);
                float relationPinCenterX = ResolvePinCenterX(nodeVisual, isOutputPin: true, pinDotSize);
                uint relationPinColor = ResolvePinColor(DocColumnKind.Relation, style);
                DrawPinDot(relationPinCenterX, relationPinCenterY, pinDotSize, relationPinColor);
                RegisterPinVisual(
                    nodeVisual,
                    settingColumn,
                    isOutputPin: true,
                    relationPinCenterY,
                    pinDotSize,
                    table.Id,
                    nodeVisual.Row.Id,
                    isExecutionOutput: IsExecutionOutputColumn(schema, settingColumn));
            }

            settingsRowTop += settingRowHeightForRow;
            if (settingIndex < SettingColumnsScratch.Count - 1)
            {
                settingsRowTop += NodeRowVerticalGap * nodeScale;
            }
        }

    }

    private static void BuildNodeVisuals(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocView view,
        GraphSchema schema,
        NodeGraphViewSettings settings,
        NodeGraphViewState viewState,
        bool interactive)
    {
        NodeVisuals.Clear();
        NodeIndexByRowId.Clear();
        bool inlineEditorsActive = interactive && workspace is DocWorkspace;
        int[]? viewRowIndices = workspace.ComputeViewRowIndices(table, view);
        int rowCount = viewRowIndices?.Length ?? table.Rows.Count;
        for (int visibleRowIndex = 0; visibleRowIndex < rowCount; visibleRowIndex++)
        {
            int sourceRowIndex = viewRowIndices != null ? viewRowIndices[visibleRowIndex] : visibleRowIndex;
            if (sourceRowIndex < 0 || sourceRowIndex >= table.Rows.Count)
            {
                continue;
            }

            DocRow row = table.Rows[sourceRowIndex];
            string typeName = ResolveNodeType(row, schema);
            string title = ResolveNodeTitle(row, schema, row.Id);
            DocCellValue positionCellValue = row.GetCell(schema.PositionColumn!);
            Vector2 worldPosition = new((float)positionCellValue.XValue, (float)positionCellValue.YValue);
            if (viewState.DragActive &&
                string.Equals(viewState.DraggedRowId, row.Id, StringComparison.Ordinal))
            {
                worldPosition = viewState.DragCurrentWorld;
            }

            float nodeScale = ComputeNodeScale(viewState.Zoom);
            NodeTypeLayout? typeLayout = FindTypeLayout(settings, typeName);
            float nodeWidth = ResolveNodeWidth(typeLayout?.NodeWidth ?? DefaultNodeWidth);
            _activeNodeWidth = nodeWidth;
            CollectLayoutColumns(table, typeLayout, InputPinColumnsScratch, OutputPinColumnsScratch, SettingColumnsScratch);
            float pinAreaHeightUnits = ComputePinAreaHeightUnits(row, table, schema, row.Id, InputPinColumnsScratch, OutputPinColumnsScratch, inlineEditorsActive);
            float settingsAreaHeightUnits = ComputeSettingsAreaHeightUnits(
                workspace,
                table,
                row,
                SettingColumnsScratch,
                inlineEditorsActive);
            float nodeHeight = (NodeHeaderHeight * nodeScale) +
                               (pinAreaHeightUnits * nodeScale) +
                               (settingsAreaHeightUnits * nodeScale) +
                               (NodeFooterPadding * nodeScale);
            Vector2 screenPosition = WorldToScreen(worldPosition, viewState, contentRectX: _lastDrawRect.X, contentRectY: _lastDrawRect.Y);
            var screenRect = new ImRect(screenPosition.X, screenPosition.Y, nodeWidth * nodeScale, nodeHeight);

            var nodeVisual = new NodeVisual(
                row.Id,
                sourceRowIndex,
                row,
                typeName,
                title,
                worldPosition,
                nodeWidth,
                screenRect,
                pinAreaHeightUnits,
                nodeScale,
                inlineEditorsActive);
            NodeIndexByRowId[row.Id] = NodeVisuals.Count;
            NodeVisuals.Add(nodeVisual);
        }
    }

    private static Vector2 ResolvePinAnchor(
        IDerpDocEditorContext workspace,
        DocTable table,
        GraphSchema schema,
        NodeGraphViewSettings settings,
        NodeVisual nodeVisual,
        string pinId,
        bool outputPin)
    {
        _activeNodeWidth = nodeVisual.WidthUnits;
        float nodeHeaderHeight = NodeHeaderHeight * nodeVisual.Scale;
        float pinDotSize = MathF.Max(1.5f, NodePinDotSize * nodeVisual.Scale);
        float execPinSize = MathF.Max(3f, NodeExecPinSize * nodeVisual.Scale);
        float executionCenterX = ResolveExecutionPinCenterX(nodeVisual);
        float executionInputCenterY = ResolveExecutionPinCenterY(nodeVisual, isOutputPin: false, execPinSize);
        float executionOutputCenterY = ResolveExecutionPinCenterY(nodeVisual, isOutputPin: true, execPinSize);
        if (!outputPin && string.Equals(pinId, ExecutionInputPinId, StringComparison.Ordinal))
        {
            return new Vector2(executionCenterX, executionInputCenterY);
        }

        if (outputPin && string.Equals(pinId, ExecutionOutputPinId, StringComparison.Ordinal))
        {
            return new Vector2(executionCenterX, executionOutputCenterY);
        }

        NodeTypeLayout? typeLayout = FindTypeLayout(settings, nodeVisual.TypeName);
        CollectLayoutColumns(table, typeLayout, InputPinColumnsScratch, OutputPinColumnsScratch, SettingColumnsScratch);
        int pinRowCount = Math.Max(InputPinColumnsScratch.Count, OutputPinColumnsScratch.Count);
        float dataRowsStartUnits = 0f;
        if (pinRowCount <= 0)
        {
            float fallbackY = nodeVisual.ScreenRect.Y + nodeHeaderHeight * 0.5f;
            float fallbackX = ResolvePinCenterX(nodeVisual, outputPin, pinDotSize);
            return new Vector2(fallbackX, fallbackY);
        }

        List<DocColumn> pinColumns = outputPin ? OutputPinColumnsScratch : InputPinColumnsScratch;
        if (pinColumns.Count <= 0)
        {
            float fallbackY = nodeVisual.ScreenRect.Y + nodeHeaderHeight * 0.5f;
            float fallbackX = ResolvePinCenterX(nodeVisual, outputPin, pinDotSize);
            return new Vector2(fallbackX, fallbackY);
        }

        int matchingPinIndex = -1;
        for (int pinIndex = 0; pinIndex < pinColumns.Count; pinIndex++)
        {
            DocColumn column = pinColumns[pinIndex];
            if (!IsPinIdentifierMatch(table, column.Id, pinId))
            {
                continue;
            }

            matchingPinIndex = pinIndex;
            break;
        }

        if (matchingPinIndex < 0 && outputPin)
        {
            float settingsStartUnits = ComputePinAreaHeightUnits(
                nodeVisual.Row,
                table,
                schema,
                nodeVisual.RowId,
                InputPinColumnsScratch,
                OutputPinColumnsScratch,
                nodeVisual.InlineEditorsActive);
            float settingsOffsetUnits = settingsStartUnits;
            for (int settingIndex = 0; settingIndex < SettingColumnsScratch.Count; settingIndex++)
            {
                DocColumn settingColumn = SettingColumnsScratch[settingIndex];
                if (IsPinIdentifierMatch(table, settingColumn.Id, pinId))
                {
                    float settingRowHeightUnits = ComputeSettingRowHeightUnits(
                        workspace,
                        table,
                        nodeVisual.Row,
                        settingColumn,
                        nodeVisual.InlineEditorsActive);
                    float settingCenterY = nodeVisual.ScreenRect.Y +
                                           nodeHeaderHeight +
                                           (settingsOffsetUnits + settingRowHeightUnits * 0.5f) * nodeVisual.Scale;
                    float settingPinCenterX = ResolvePinCenterX(nodeVisual, isOutputPin: true, pinDotSize);
                    return new Vector2(settingPinCenterX, settingCenterY);
                }

                float settingRowUnits = ComputeSettingRowHeightUnits(
                    workspace,
                    table,
                    nodeVisual.Row,
                    settingColumn,
                    nodeVisual.InlineEditorsActive);
                settingsOffsetUnits += settingRowUnits;
                if (settingIndex < SettingColumnsScratch.Count - 1)
                {
                    settingsOffsetUnits += NodeRowVerticalGap;
                }
            }

            matchingPinIndex = 0;
        }

        matchingPinIndex = Math.Clamp(matchingPinIndex, 0, pinRowCount - 1);
        float pinOffsetUnits = dataRowsStartUnits;
        for (int pinRowIndex = 0; pinRowIndex < matchingPinIndex; pinRowIndex++)
        {
            DocColumn? inputColumn = pinRowIndex < InputPinColumnsScratch.Count ? InputPinColumnsScratch[pinRowIndex] : null;
            DocColumn? outputColumn = pinRowIndex < OutputPinColumnsScratch.Count ? OutputPinColumnsScratch[pinRowIndex] : null;
            bool showInputEditor = inputColumn != null &&
                                   ShouldShowInlinePinEditor(schema, table, nodeVisual.RowId, inputColumn, inputPin: true, nodeVisual.InlineEditorsActive);
            bool showOutputEditor = outputColumn != null &&
                                    ShouldShowInlinePinEditor(schema, table, nodeVisual.RowId, outputColumn, inputPin: false, nodeVisual.InlineEditorsActive);
            pinOffsetUnits += ComputePinRowHeightUnits(
                nodeVisual.Row,
                inputColumn,
                showInputEditor,
                outputColumn,
                showOutputEditor,
                nodeVisual.InlineEditorsActive);
            pinOffsetUnits += NodeRowVerticalGap;
        }

        DocColumn? matchingInputColumn = matchingPinIndex < InputPinColumnsScratch.Count ? InputPinColumnsScratch[matchingPinIndex] : null;
        DocColumn? matchingOutputColumn = matchingPinIndex < OutputPinColumnsScratch.Count ? OutputPinColumnsScratch[matchingPinIndex] : null;
        bool showMatchingInputEditor = matchingInputColumn != null &&
                                       ShouldShowInlinePinEditor(schema, table, nodeVisual.RowId, matchingInputColumn, inputPin: true, nodeVisual.InlineEditorsActive);
        bool showMatchingOutputEditor = matchingOutputColumn != null &&
                                        ShouldShowInlinePinEditor(schema, table, nodeVisual.RowId, matchingOutputColumn, inputPin: false, nodeVisual.InlineEditorsActive);
        float matchingRowHeightUnits = ComputePinRowHeightUnits(
            nodeVisual.Row,
            matchingInputColumn,
            showMatchingInputEditor,
            matchingOutputColumn,
            showMatchingOutputEditor,
            nodeVisual.InlineEditorsActive);

        bool matchingRowExpanded = false;
        if (showMatchingInputEditor && matchingInputColumn != null)
        {
            _ = ComputePinEditorControlHeightUnits(nodeVisual.Row, matchingInputColumn, inputPin: true, out bool expandedInputEditor);
            matchingRowExpanded |= expandedInputEditor;
        }

        if (showMatchingOutputEditor && matchingOutputColumn != null)
        {
            _ = ComputePinEditorControlHeightUnits(nodeVisual.Row, matchingOutputColumn, inputPin: false, out bool expandedOutputEditor);
            matchingRowExpanded |= expandedOutputEditor;
        }

        float pinCenterOffsetUnits = matchingRowExpanded
            ? (NodeExpandedTextLabelTopPadding + GetInlineLabelHeightUnits() * 0.5f)
            : (matchingRowHeightUnits * 0.5f);
        float pinCenterY = nodeVisual.ScreenRect.Y + nodeHeaderHeight + (pinOffsetUnits + pinCenterOffsetUnits) * nodeVisual.Scale;
        float pinX = ResolvePinCenterX(nodeVisual, outputPin, pinDotSize);
        return new Vector2(pinX, pinCenterY);
    }

    private static bool IsPinIdentifierMatch(DocTable table, string columnId, string pinId)
    {
        if (string.Equals(columnId, pinId, StringComparison.Ordinal))
        {
            return true;
        }

        DocColumn? column = FindColumnById(table, columnId);
        if (column == null)
        {
            return false;
        }

        return string.Equals(column.Name, pinId, StringComparison.OrdinalIgnoreCase);
    }

    private static DocColumn? FindColumnByPinIdentifier(DocTable table, string pinId)
    {
        if (string.IsNullOrWhiteSpace(pinId))
        {
            return null;
        }

        DocColumn? byId = FindColumnById(table, pinId);
        if (byId != null)
        {
            return byId;
        }

        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            DocColumn column = table.Columns[columnIndex];
            if (string.Equals(column.Name, pinId, StringComparison.OrdinalIgnoreCase))
            {
                return column;
            }
        }

        return null;
    }

    private static bool IsInputPinConnected(
        GraphSchema schema,
        DocTable nodeTable,
        string nodeRowId,
        string inputColumnId)
    {
        if (schema.EdgeTable == null || schema.EdgeToNodeColumn == null || schema.EdgeToPinColumn == null)
        {
            return false;
        }

        for (int edgeRowIndex = 0; edgeRowIndex < schema.EdgeTable.Rows.Count; edgeRowIndex++)
        {
            DocRow edgeRow = schema.EdgeTable.Rows[edgeRowIndex];
            string toNodeId = edgeRow.GetCell(schema.EdgeToNodeColumn).StringValue ?? "";
            if (!string.Equals(toNodeId, nodeRowId, StringComparison.Ordinal))
            {
                continue;
            }

            string toPinId = edgeRow.GetCell(schema.EdgeToPinColumn).StringValue ?? "";
            if (IsPinIdentifierMatch(nodeTable, inputColumnId, toPinId))
            {
                return true;
            }
        }

        return false;
    }

    private static uint ResolveNodeAccentColor(string typeName, ImStyle style)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return style.Primary;
        }

        uint hash = ComputeStableCaseInsensitiveHash(typeName);
        int paletteIndex = (int)(hash % (uint)NodeAccentPalette.Length);
        return NodeAccentPalette[paletteIndex];
    }

    private static uint ComputeStableCaseInsensitiveHash(string value)
    {
        uint hash = 2166136261u;
        for (int charIndex = 0; charIndex < value.Length; charIndex++)
        {
            char normalizedChar = char.ToUpperInvariant(value[charIndex]);
            hash ^= normalizedChar;
            hash *= 16777619u;
        }

        return hash;
    }

    private static uint ResolvePinColor(DocColumnKind pinKind, ImStyle style)
    {
        return pinKind switch
        {
            DocColumnKind.Number or DocColumnKind.Formula => 0xFF5B8CFF,
            DocColumnKind.Text or DocColumnKind.Select => 0xFFF0B04A,
            DocColumnKind.Checkbox => 0xFF5FC97A,
            DocColumnKind.Vec2 or DocColumnKind.Vec3 or DocColumnKind.Vec4 => 0xFF8F7DFF,
            DocColumnKind.Color => 0xFFD463A8,
            DocColumnKind.Relation or DocColumnKind.Subtable => 0xFF4DC7D5,
            DocColumnKind.TextureAsset or DocColumnKind.MeshAsset or DocColumnKind.AudioAsset or DocColumnKind.UiAsset => 0xFFD87D42,
            DocColumnKind.Spline => 0xFF7FC4FF,
            _ => style.Primary,
        };
    }

    private static uint ResolveExecutionFlowColor(ImStyle style)
    {
        _ = style;
        return 0xFFCCCCCC;
    }

    private static float ComputeNodeScale(float zoom)
    {
        return zoom;
    }

    private static float ResolveNodeWidth(float nodeWidth)
    {
        if (!float.IsFinite(nodeWidth) || nodeWidth <= 0f)
        {
            return DefaultNodeWidth;
        }

        return Math.Clamp(nodeWidth, MinNodeWidth, MaxNodeWidth);
    }

    private static float GetEffectivePinRowHeightUnits(bool inlineEditorsActive)
    {
        if (!inlineEditorsActive)
        {
            return NodePinRowHeight;
        }

        return MathF.Max(NodePinRowHeight, Im.Style.MinButtonHeight);
    }

    private static float GetMinimumInlineControlHeightUnits(float defaultRowHeightUnits)
    {
        return MathF.Max(defaultRowHeightUnits, Im.Style.MinButtonHeight);
    }

    private static void CollectLayoutColumns(
        DocTable table,
        NodeTypeLayout? typeLayout,
        List<DocColumn> inputPinColumns,
        List<DocColumn> outputPinColumns,
        List<DocColumn> settingColumns)
    {
        inputPinColumns.Clear();
        outputPinColumns.Clear();
        settingColumns.Clear();
        if (typeLayout == null)
        {
            return;
        }

        for (int fieldIndex = 0; fieldIndex < typeLayout.Fields.Count; fieldIndex++)
        {
            NodeFieldLayout fieldLayout = typeLayout.Fields[fieldIndex];
            DocColumn? column = FindColumnById(table, fieldLayout.ColumnId);
            if (column == null)
            {
                continue;
            }

            NodeFieldDisplayMode mode = ParseFieldDisplayMode(fieldLayout.Mode);
            if (mode == NodeFieldDisplayMode.InputPin)
            {
                inputPinColumns.Add(column);
            }
            else if (mode == NodeFieldDisplayMode.OutputPin)
            {
                outputPinColumns.Add(column);
            }
            else if (mode == NodeFieldDisplayMode.Setting)
            {
                settingColumns.Add(column);
            }
        }
    }

    private static bool UsesInlineTextEditor(DocColumn column)
    {
        return column.Kind != DocColumnKind.Checkbox &&
               column.Kind != DocColumnKind.Select &&
               column.Kind != DocColumnKind.Number &&
               column.Kind != DocColumnKind.Formula &&
               column.Kind != DocColumnKind.Vec2 &&
               column.Kind != DocColumnKind.Vec3 &&
               column.Kind != DocColumnKind.Vec4 &&
               column.Kind != DocColumnKind.Color;
    }

    private static float GetInputPinEditorControlWidthUnits()
    {
        return (_activeNodeWidth - NodeInlineHorizontalPadding) - (_activeNodeWidth * NodeInputPinEditorLeftRatio + NodeInlineColumnGap);
    }

    private static float GetOutputPinEditorControlWidthUnits()
    {
        return (_activeNodeWidth * NodeOutputPinEditorRightRatio - NodeInlineColumnGap) - NodeInlineHorizontalPadding;
    }

    private static float GetExpandedPinEditorControlWidthUnits()
    {
        return _activeNodeWidth - (NodeInlineHorizontalPadding * 2f);
    }

    private static float GetExpandedSettingEditorControlWidthUnits()
    {
        return _activeNodeWidth - (NodeInlineHorizontalPadding * 2f);
    }

    private static float GetCompactSettingEditorControlWidthUnits()
    {
        return _activeNodeWidth - (_activeNodeWidth * NodeSettingLabelWidthRatio) - (NodeInlineHorizontalPadding * 2f) - NodeInlineColumnGap;
    }

    private static float GetInlineLabelHeightUnits()
    {
        return Math.Max(8f, Im.Style.FontSize - 1f);
    }

    private static float GetExpandedTextRowHeightUnits(float controlHeightUnits)
    {
        return NodeExpandedTextLabelTopPadding +
               GetInlineLabelHeightUnits() +
               NodeExpandedTextLabelGap +
               controlHeightUnits +
               NodeExpandedTextBottomPadding;
    }

    private static bool ShouldShowInlinePinEditor(
        GraphSchema schema,
        DocTable table,
        string nodeRowId,
        DocColumn column,
        bool inputPin,
        bool inlineEditorsActive)
    {
        if (!inlineEditorsActive)
        {
            return false;
        }

        if (!inputPin)
        {
            return true;
        }

        return !IsInputPinConnected(schema, table, nodeRowId, column.Id);
    }

    private static float MeasureInlineTextEditorHeightUnits(
        string textValue,
        float controlWidthUnits,
        float minimumHeightUnits,
        out int visualLineCount)
    {
        float measuredHeightUnits = ImTextArea.MeasureContentHeight(
            textValue.AsSpan(),
            controlWidthUnits,
            wordWrap: true,
            fontSizePx: -1f,
            lineHeightPx: -1f,
            letterSpacingPx: 0f,
            includeBorder: true,
            out visualLineCount);
        return Math.Clamp(measuredHeightUnits, minimumHeightUnits, NodeInlineTextAreaMaxHeight);
    }

    private static bool TryComputeExpandedSettingTextLayoutUnits(
        DocRow row,
        DocColumn column,
        out float controlHeightUnits,
        out float rowHeightUnits)
    {
        controlHeightUnits = 0f;
        rowHeightUnits = 0f;
        if (!UsesInlineTextEditor(column))
        {
            return false;
        }

        float minimumControlHeightUnits = GetMinimumInlineControlHeightUnits(NodeSettingRowHeight);
        string currentTextValue = row.GetCell(column).StringValue ?? "";
        _ = MeasureInlineTextEditorHeightUnits(
            currentTextValue,
            GetCompactSettingEditorControlWidthUnits(),
            minimumControlHeightUnits,
            out int visualLineCount);
        if (visualLineCount <= 1)
        {
            controlHeightUnits = minimumControlHeightUnits;
            rowHeightUnits = minimumControlHeightUnits;
            return false;
        }

        controlHeightUnits = MeasureInlineTextEditorHeightUnits(
            currentTextValue,
            GetExpandedSettingEditorControlWidthUnits(),
            minimumControlHeightUnits,
            out _);
        rowHeightUnits = Math.Max(GetExpandedTextRowHeightUnits(controlHeightUnits), GetMinimumInlineControlHeightUnits(NodeSettingRowHeight));
        return true;
    }

    private static float ComputePinEditorControlHeightUnits(
        DocRow row,
        DocColumn column,
        bool inputPin,
        out bool expandedTextEditor)
    {
        expandedTextEditor = false;
        if (!UsesInlineTextEditor(column))
        {
            return GetMinimumInlineControlHeightUnits(NodePinRowHeight);
        }

        float compactControlWidthUnits = inputPin
            ? GetInputPinEditorControlWidthUnits()
            : GetOutputPinEditorControlWidthUnits();
        float compactMinimumHeightUnits = GetMinimumInlineControlHeightUnits(NodePinRowHeight);
        string currentTextValue = row.GetCell(column).StringValue ?? "";
        _ = MeasureInlineTextEditorHeightUnits(
            currentTextValue,
            compactControlWidthUnits,
            compactMinimumHeightUnits,
            out int visualLineCount);
        if (visualLineCount <= 1)
        {
            return compactMinimumHeightUnits;
        }

        expandedTextEditor = true;
        float expandedControlWidthUnits = GetExpandedPinEditorControlWidthUnits();
        float expandedMinimumHeightUnits = GetMinimumInlineControlHeightUnits(NodePinRowHeight);
        return MeasureInlineTextEditorHeightUnits(
            currentTextValue,
            expandedControlWidthUnits,
            expandedMinimumHeightUnits,
            out _);
    }

    private static float ComputePinSideRequiredHeightUnits(
        DocRow row,
        DocColumn column,
        bool inputPin)
    {
        float controlHeightUnits = ComputePinEditorControlHeightUnits(row, column, inputPin, out bool expandedTextEditor);
        if (expandedTextEditor)
        {
            return Math.Max(GetExpandedTextRowHeightUnits(controlHeightUnits), GetMinimumInlineControlHeightUnits(NodePinRowHeight));
        }

        return Math.Max(GetEffectivePinRowHeightUnits(inlineEditorsActive: true), controlHeightUnits);
    }

    private static float ComputePinRowHeightUnits(
        DocRow row,
        DocColumn? inputColumn,
        bool showInputEditor,
        DocColumn? outputColumn,
        bool showOutputEditor,
        bool inlineEditorsActive)
    {
        float minimumRowHeightUnits = GetEffectivePinRowHeightUnits(inlineEditorsActive);
        if (!inlineEditorsActive)
        {
            return minimumRowHeightUnits;
        }

        float rowHeightUnits = minimumRowHeightUnits;
        if (showInputEditor && inputColumn != null)
        {
            rowHeightUnits = Math.Max(rowHeightUnits, ComputePinSideRequiredHeightUnits(row, inputColumn, inputPin: true));
        }

        if (showOutputEditor && outputColumn != null)
        {
            rowHeightUnits = Math.Max(rowHeightUnits, ComputePinSideRequiredHeightUnits(row, outputColumn, inputPin: false));
        }

        return rowHeightUnits;
    }

    private static float ComputePinAreaHeightUnits(
        DocRow row,
        DocTable table,
        GraphSchema schema,
        string nodeRowId,
        List<DocColumn> inputPinColumns,
        List<DocColumn> outputPinColumns,
        bool inlineEditorsActive)
    {
        int pinRowCount = Math.Max(inputPinColumns.Count, outputPinColumns.Count);
        if (pinRowCount <= 0)
        {
            return 0f;
        }

        float totalHeightUnits = 0f;
        for (int pinRowIndex = 0; pinRowIndex < pinRowCount; pinRowIndex++)
        {
            DocColumn? inputColumn = pinRowIndex < inputPinColumns.Count ? inputPinColumns[pinRowIndex] : null;
            DocColumn? outputColumn = pinRowIndex < outputPinColumns.Count ? outputPinColumns[pinRowIndex] : null;
            bool showInputEditor = inputColumn != null &&
                                   ShouldShowInlinePinEditor(schema, table, nodeRowId, inputColumn, inputPin: true, inlineEditorsActive);
            bool showOutputEditor = outputColumn != null &&
                                    ShouldShowInlinePinEditor(schema, table, nodeRowId, outputColumn, inputPin: false, inlineEditorsActive);
            totalHeightUnits += ComputePinRowHeightUnits(row, inputColumn, showInputEditor, outputColumn, showOutputEditor, inlineEditorsActive);
            if (pinRowIndex < pinRowCount - 1)
            {
                totalHeightUnits += NodeRowVerticalGap;
            }
        }

        return totalHeightUnits;
    }

    private static float ComputeSettingRowHeightUnits(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocRow row,
        DocColumn column,
        bool inlineEditorsActive)
    {
        if (column.Kind == DocColumnKind.Subtable)
        {
            return MeasureSubtableSettingHeightUnits(workspace, table, row, column);
        }

        if (!inlineEditorsActive)
        {
            return NodeSettingRowHeight;
        }

        float minimumSettingHeightUnits = GetMinimumInlineControlHeightUnits(NodeSettingRowHeight);
        if (TryComputeExpandedSettingTextLayoutUnits(row, column, out float controlHeightUnits, out float expandedRowHeightUnits))
        {
            return Math.Max(expandedRowHeightUnits, minimumSettingHeightUnits);
        }

        if (UsesInlineTextEditor(column))
        {
            return Math.Max(minimumSettingHeightUnits, controlHeightUnits);
        }

        return minimumSettingHeightUnits;
    }

    private static float ComputeSettingsAreaHeightUnits(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocRow row,
        List<DocColumn> settingColumns,
        bool inlineEditorsActive)
    {
        float totalHeightUnits = 0f;
        for (int settingIndex = 0; settingIndex < settingColumns.Count; settingIndex++)
        {
            totalHeightUnits += ComputeSettingRowHeightUnits(
                workspace,
                table,
                row,
                settingColumns[settingIndex],
                inlineEditorsActive);
        }

        if (settingColumns.Count > 1)
        {
            totalHeightUnits += NodeRowVerticalGap * (settingColumns.Count - 1);
        }

        return totalHeightUnits;
    }

    private static float MeasureSubtableSettingHeightUnits(
        IDerpDocEditorContext workspace,
        DocTable parentTable,
        DocRow parentRow,
        DocColumn subtableColumn)
    {
        if (!TryResolveSubtableChildTable(workspace.Project, subtableColumn, out DocTable? childTable))
        {
            return NodeSubtableSectionMinHeight;
        }

        NodeSubtableDisplayMode displayMode = ResolveNodeSubtableDisplayMode(subtableColumn.SubtableDisplayRendererId, out _);
        if (displayMode == NodeSubtableDisplayMode.Grid)
        {
            float fontSize = MathF.Max(8f, Im.Style.FontSize - 2f);
            float baseRowHeightUnits = MathF.Max(NodeSubtableSectionLineHeight, fontSize + 6f);
            float contentBottomInset = 4f;
            BuildNodeSubtableGridColumns(childTable, SubtableGridColumnsScratch, NodeSubtableGridMaxColumns);
            DocColumn? relationColumn = ResolveSubtableRelationPinColumn(childTable);
            int visibleColumnCount = SubtableGridColumnsScratch.Count > 0 ? SubtableGridColumnsScratch.Count : 1;
            float previewWidthUnits = MathF.Max(8f, GetExpandedSettingEditorControlWidthUnits() - 6f);
            float dataGridWidthUnits = MathF.Max(8f, previewWidthUnits - NodeSubtableGridGutterWidth);
            float cellWidthUnits = dataGridWidthUnits / visibleColumnCount;
            int matchingRowCount = CollectSubtableRowsForParent(childTable, parentRow.Id, SubtableRowsScratch);
            float bodyRowsHeightUnits = 0f;
            if (matchingRowCount > 0)
            {
                for (int rowIndex = 0; rowIndex < matchingRowCount; rowIndex++)
                {
                    DocRow childRow = SubtableRowsScratch[rowIndex];
                    float rowHeightUnits = SubtableGridColumnsScratch.Count > 0
                        ? ComputeNodeSubtableGridRowHeight(childRow, visibleColumnCount, cellWidthUnits, baseRowHeightUnits, relationColumn)
                        : baseRowHeightUnits;
                    bodyRowsHeightUnits += rowHeightUnits;
                }
            }

            float gridMeasuredHeightUnits = NodeSubtableSectionHeaderHeight +
                                            2f +
                                            baseRowHeightUnits +
                                            bodyRowsHeightUnits +
                                            contentBottomInset +
                                            2f;
            return MathF.Max(NodeSubtableSectionMinHeight, gridMeasuredHeightUnits);
        }

        float fallbackHeight = NodeSubtableSectionMinHeight;
        if (TryResolveNodeSubtableSectionRenderer(subtableColumn, out IDerpDocNodeSubtableSectionRenderer? sectionRenderer))
        {
            float measuredHeight = sectionRenderer.MeasureSubtableSectionHeight(
                workspace,
                parentTable,
                parentRow,
                subtableColumn,
                childTable,
                GetExpandedSettingEditorControlWidthUnits(),
                fallbackHeight);
            if (float.IsFinite(measuredHeight) && measuredHeight > 0f)
            {
                return Math.Clamp(measuredHeight, NodeSubtableSectionMinHeight, NodeSubtableSectionMaxHeight);
            }
        }

        int childRowCount = CountSubtableRowsForParent(childTable, parentRow.Id);
        int visibleRowCount = Math.Min(childRowCount, NodeSubtableSectionMaxVisibleRows);
        int overflowRowCount = Math.Max(0, childRowCount - visibleRowCount);
        float bodyLineCount = visibleRowCount > 0 ? visibleRowCount : 1;
        if (overflowRowCount > 0)
        {
            bodyLineCount += 1;
        }

        float measuredHeightUnits = NodeSubtableSectionHeaderHeight + 8f + bodyLineCount * NodeSubtableSectionLineHeight + 8f;
        return Math.Clamp(measuredHeightUnits, NodeSubtableSectionMinHeight, NodeSubtableSectionMaxHeight);
    }

    private static bool TryResolveSubtableChildTable(DocProject project, DocColumn subtableColumn, out DocTable childTable)
    {
        childTable = null!;
        if (string.IsNullOrWhiteSpace(subtableColumn.SubtableId))
        {
            return false;
        }

        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            DocTable candidateTable = project.Tables[tableIndex];
            if (!string.Equals(candidateTable.Id, subtableColumn.SubtableId, StringComparison.Ordinal))
            {
                continue;
            }

            childTable = candidateTable;
            return true;
        }

        return false;
    }

    private static int CountSubtableRowsForParent(DocTable childTable, string parentRowId)
    {
        if (string.IsNullOrWhiteSpace(parentRowId))
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(childTable.ParentRowColumnId))
        {
            return childTable.Rows.Count;
        }

        int rowCount = 0;
        for (int rowIndex = 0; rowIndex < childTable.Rows.Count; rowIndex++)
        {
            DocRow childRow = childTable.Rows[rowIndex];
            string childParentRowId = childRow.GetCell(childTable.ParentRowColumnId).StringValue ?? "";
            if (!string.Equals(childParentRowId, parentRowId, StringComparison.Ordinal))
            {
                continue;
            }

            rowCount++;
        }

        return rowCount;
    }

    private static int CollectSubtableRowsForParent(DocTable childTable, string parentRowId, List<DocRow> destinationRows)
    {
        destinationRows.Clear();
        if (string.IsNullOrWhiteSpace(parentRowId))
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(childTable.ParentRowColumnId))
        {
            for (int rowIndex = 0; rowIndex < childTable.Rows.Count; rowIndex++)
            {
                destinationRows.Add(childTable.Rows[rowIndex]);
            }

            return destinationRows.Count;
        }

        for (int rowIndex = 0; rowIndex < childTable.Rows.Count; rowIndex++)
        {
            DocRow childRow = childTable.Rows[rowIndex];
            string childParentRowId = childRow.GetCell(childTable.ParentRowColumnId).StringValue ?? "";
            if (!string.Equals(childParentRowId, parentRowId, StringComparison.Ordinal))
            {
                continue;
            }

            destinationRows.Add(childRow);
        }

        return destinationRows.Count;
    }

    private static bool TryResolveNodeSubtableSectionRenderer(
        DocColumn subtableColumn,
        out IDerpDocNodeSubtableSectionRenderer sectionRenderer)
    {
        sectionRenderer = null!;
        string? rendererId = ExtractNodeSubtableSectionRendererId(subtableColumn.SubtableDisplayRendererId);
        if (string.IsNullOrWhiteSpace(rendererId))
        {
            return false;
        }

        return NodeSubtableSectionRendererRegistry.TryGet(rendererId, out sectionRenderer);
    }

    private static string? ExtractNodeSubtableSectionRendererId(string? rendererId)
    {
        if (string.IsNullOrWhiteSpace(rendererId))
        {
            return null;
        }

        string normalizedRendererId = rendererId.Trim();
        if (normalizedRendererId.StartsWith(SubtableDisplayCustomRendererPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string customRendererId = normalizedRendererId[SubtableDisplayCustomRendererPrefix.Length..].Trim();
            return customRendererId.Length == 0 ? null : customRendererId;
        }

        if (normalizedRendererId.StartsWith("builtin.", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return normalizedRendererId;
    }

    private static void HandlePanAndZoom(
        in ImInput input,
        ImRect contentRect,
        ref NodeGraphViewState viewState,
        Vector2 mouseScreenPosition,
        bool interactive)
    {
        if (!interactive || !contentRect.Contains(mouseScreenPosition))
        {
            if (viewState.PanActive && !input.MouseMiddleDown)
            {
                viewState.PanActive = false;
            }

            return;
        }

        if (input.ScrollDelta != 0f)
        {
            float oldZoom = viewState.Zoom;
            float newZoom = Math.Clamp(oldZoom * MathF.Pow(1.1f, input.ScrollDelta), MinCanvasZoom, MaxCanvasZoom);
            if (Math.Abs(newZoom - oldZoom) > float.Epsilon)
            {
                Vector2 preZoomMouseGraph = ScreenToWorld(mouseScreenPosition, viewState, contentRect.X, contentRect.Y);
                viewState.Zoom = newZoom;
                Vector2 postZoomMouseScreen = WorldToScreen(preZoomMouseGraph, viewState, contentRect.X, contentRect.Y);
                viewState.Pan += mouseScreenPosition - postZoomMouseScreen;
            }
        }

        if (input.MouseMiddlePressed)
        {
            viewState.PanActive = true;
            viewState.PanStartMouse = mouseScreenPosition;
            viewState.PanStartValue = viewState.Pan;
        }

        if (viewState.PanActive)
        {
            if (!input.MouseMiddleDown)
            {
                viewState.PanActive = false;
            }
            else
            {
                Vector2 dragDelta = mouseScreenPosition - viewState.PanStartMouse;
                viewState.Pan = viewState.PanStartValue + dragDelta;
            }
        }
    }

    private static void BeginNodeDrag(
        DocTable table,
        GraphSchema schema,
        NodeGraphViewState viewState,
        NodeVisual nodeVisual,
        Vector2 mouseScreenPosition)
    {
        if (schema.PositionColumn == null)
        {
            return;
        }

        viewState.DragActive = true;
        viewState.DraggedRowId = nodeVisual.RowId;
        viewState.DragPositionColumnId = schema.PositionColumn.Id;
        viewState.DragStartWorld = nodeVisual.WorldPosition;
        Vector2 mouseWorldPosition = ScreenToWorld(mouseScreenPosition, viewState, _lastDrawRect.X, _lastDrawRect.Y);
        viewState.DragPointerOffset = mouseWorldPosition - nodeVisual.WorldPosition;
        viewState.DragCurrentWorld = nodeVisual.WorldPosition;
    }

    private static void UpdateNodeDrag(
        IDerpDocEditorContext workspace,
        DocTable table,
        GraphSchema schema,
        NodeGraphViewState viewState,
        Vector2 mouseScreenPosition,
        in ImInput input)
    {
        if (!viewState.DragActive)
        {
            return;
        }

        if (schema.PositionColumn == null || string.IsNullOrWhiteSpace(viewState.DraggedRowId))
        {
            viewState.DragActive = false;
            return;
        }

        if (input.MouseDown)
        {
            Vector2 mouseWorldPosition = ScreenToWorld(mouseScreenPosition, viewState, _lastDrawRect.X, _lastDrawRect.Y);
            viewState.DragCurrentWorld = mouseWorldPosition - viewState.DragPointerOffset;
            return;
        }

        if (workspace is DocWorkspace mutableWorkspace)
        {
            CommitNodeDrag(mutableWorkspace, table, schema.PositionColumn, viewState);
        }

        viewState.DragActive = false;
        viewState.DraggedRowId = "";
        viewState.DragPositionColumnId = "";
    }

    private static void CommitNodeDrag(
        DocWorkspace workspace,
        DocTable table,
        DocColumn positionColumn,
        NodeGraphViewState viewState)
    {
        if (string.IsNullOrWhiteSpace(viewState.DraggedRowId))
        {
            return;
        }

        DocRow? row = FindRowById(table, viewState.DraggedRowId);
        if (row == null)
        {
            return;
        }

        DocCellValue oldCellValue = row.GetCell(positionColumn);
        DocCellValue newCellValue = DocCellValue.Vec2(viewState.DragCurrentWorld.X, viewState.DragCurrentWorld.Y);
        if (Math.Abs(oldCellValue.XValue - newCellValue.XValue) < 0.0001 &&
            Math.Abs(oldCellValue.YValue - newCellValue.YValue) < 0.0001)
        {
            return;
        }

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetCell,
            TableId = table.Id,
            RowId = row.Id,
            ColumnId = positionColumn.Id,
            OldCellValue = oldCellValue,
            NewCellValue = newCellValue,
        });
    }

    private static int FindTopmostNodeIndex(Vector2 mouseScreenPosition)
    {
        for (int nodeIndex = NodeVisuals.Count - 1; nodeIndex >= 0; nodeIndex--)
        {
            if (NodeVisuals[nodeIndex].ScreenRect.Contains(mouseScreenPosition))
            {
                return nodeIndex;
            }
        }

        return -1;
    }

    private static int FindTopmostPinIndex(Vector2 mouseScreenPosition)
    {
        for (int pinIndex = PinVisuals.Count - 1; pinIndex >= 0; pinIndex--)
        {
            if (PinVisuals[pinIndex].HitRect.Contains(mouseScreenPosition))
            {
                return pinIndex;
            }
        }

        return -1;
    }

    private static void RegisterPinVisual(
        NodeVisual nodeVisual,
        DocColumn column,
        bool isOutputPin,
        float pinCenterY,
        float pinDotSize,
        string sourceTableId,
        string sourceRowId,
        bool isExecutionOutput)
    {
        RegisterPinVisual(
            nodeVisual,
            column.Id,
            column.Id,
            column.Kind,
            isOutputPin,
            pinCenterY,
            pinDotSize,
            sourceTableId,
            sourceRowId,
            column.Id,
            isExecutionOutput);
    }

    private static void RegisterPinVisual(
        NodeVisual nodeVisual,
        string columnId,
        string pinId,
        DocColumnKind pinKind,
        bool isOutputPin,
        float pinCenterY,
        float pinDotSize,
        string sourceTableId,
        string sourceRowId,
        string sourceColumnId,
        bool isExecutionOutput)
    {
        float pinCenterX = ResolvePinCenterX(nodeVisual, isOutputPin, pinDotSize);
        RegisterPinVisualAtPosition(
            nodeVisual,
            columnId,
            pinId,
            pinKind,
            isOutputPin,
            pinCenterX,
            pinCenterY,
            pinDotSize,
            sourceTableId,
            sourceRowId,
            sourceColumnId,
            isExecutionOutput);
    }

    private static void RegisterPinVisualAtPosition(
        NodeVisual nodeVisual,
        string columnId,
        string pinId,
        DocColumnKind pinKind,
        bool isOutputPin,
        float pinCenterX,
        float pinCenterY,
        float pinDotSize,
        string sourceTableId,
        string sourceRowId,
        string sourceColumnId,
        bool isExecutionOutput)
    {
        float hitSize = MathF.Max(12f, pinDotSize + 8f);
        var hitRect = new ImRect(
            pinCenterX - hitSize * 0.5f,
            pinCenterY - hitSize * 0.5f,
            hitSize,
            hitSize);
        PinVisuals.Add(new PinVisual(
            NodeRowId: nodeVisual.RowId,
            NodeRowIndex: nodeVisual.SourceRowIndex,
            ColumnId: columnId,
            PinId: pinId,
            ColumnKind: pinKind,
            IsOutput: isOutputPin,
            SourceTableId: sourceTableId,
            SourceRowId: sourceRowId,
            SourceColumnId: sourceColumnId,
            IsExecutionOutput: isExecutionOutput,
            Center: new Vector2(pinCenterX, pinCenterY),
            HitRect: hitRect));
    }

    private static float ResolvePinCenterX(NodeVisual nodeVisual, bool isOutputPin, float pinDotSize)
    {
        float edgeOffset = MathF.Max(2f, nodeVisual.Scale * 2f);
        float halfDot = pinDotSize * 0.5f;
        return isOutputPin
            ? nodeVisual.ScreenRect.Right + edgeOffset + halfDot
            : nodeVisual.ScreenRect.X - edgeOffset - halfDot;
    }

    private static float ResolveExecutionPinCenterX(NodeVisual nodeVisual)
    {
        return ResolveSlotCenter(nodeVisual.ScreenRect.X, nodeVisual.ScreenRect.Right, slotIndex: 0, slotCount: 1);
    }

    private static float ResolveExecutionPinCenterY(NodeVisual nodeVisual, bool isOutputPin, float pinDotSize)
    {
        float topEdgeOffset = MathF.Max(1f, nodeVisual.Scale * NodeExecTopLaneOffset);
        float bottomEdgeOffset = MathF.Max(1f, nodeVisual.Scale * NodeExecBottomLaneOffset);
        float halfDot = pinDotSize * 0.5f;
        return isOutputPin
            ? nodeVisual.ScreenRect.Bottom + bottomEdgeOffset + halfDot
            : nodeVisual.ScreenRect.Y - topEdgeOffset - halfDot;
    }

    private static float ResolveSlotCenter(float startX, float endX, int slotIndex, int slotCount)
    {
        if (slotCount <= 1)
        {
            return startX + (endX - startX) * 0.5f;
        }

        int clampedSlotIndex = Math.Clamp(slotIndex, 0, slotCount - 1);
        float slotWidth = (endX - startX) / slotCount;
        return startX + slotWidth * (clampedSlotIndex + 0.5f);
    }

    private static void DrawPinDot(float pinCenterX, float pinCenterY, float pinDotSize, uint pinColor)
    {
        Im.DrawCircle(pinCenterX, pinCenterY, pinDotSize * 0.5f, pinColor);
    }

    private static void DrawExecutionPinGlyph(
        float pinCenterX,
        float pinCenterY,
        float pinDotSize,
        uint pinColor,
        bool filled)
    {
        float glyphSize = MathF.Max(3f, pinDotSize);
        float halfGlyphWidth = glyphSize * 0.40f;
        float halfGlyphHeight = glyphSize * 0.30f;
        float leftX = pinCenterX - halfGlyphWidth;
        float rightX = pinCenterX + halfGlyphWidth;
        float topY = pinCenterY - halfGlyphHeight;
        float tipY = pinCenterY + halfGlyphHeight;
        float lineThickness = MathF.Max(1.6f, glyphSize * 0.13f);

        if (filled)
        {
            ExecutionChevronPointsScratch[0] = new Vector2(leftX, topY);
            ExecutionChevronPointsScratch[1] = new Vector2(pinCenterX, tipY);
            ExecutionChevronPointsScratch[2] = new Vector2(rightX, topY);
            Im.DrawFilledPolygon(ExecutionChevronPointsScratch.AsSpan(0, 3), pinColor);
        }

        Im.DrawLine(leftX, topY, pinCenterX, tipY, lineThickness, pinColor);
        Im.DrawLine(pinCenterX, tipY, rightX, topY, lineThickness, pinColor);
        Im.DrawLine(leftX, topY, rightX, topY, lineThickness, pinColor);
    }

    private static void DrawCompatiblePinHints(
        bool sourcePinIsOutput,
        bool sourceIsExecutionOutput,
        bool sourceIsExecutionInput,
        DocColumnKind sourcePinKind,
        ImStyle style)
    {
        uint sourcePinColor = (sourceIsExecutionOutput || sourceIsExecutionInput)
            ? ResolveExecutionFlowColor(style)
            : ResolvePinColor(sourcePinKind, style);
        bool sourceCanDriveExecution = IsExecutionSourcePin(sourceIsExecutionOutput, sourcePinKind);

        for (int pinIndex = 0; pinIndex < PinVisuals.Count; pinIndex++)
        {
            PinVisual pin = PinVisuals[pinIndex];
            if (pin.IsOutput == sourcePinIsOutput)
            {
                continue;
            }

            bool compatible = IsPinCompatibleForWireDrag(
                sourcePinIsOutput,
                sourceCanDriveExecution,
                sourceIsExecutionInput,
                sourcePinKind,
                pin);

            uint color = compatible
                ? ImStyle.WithAlpha(sourcePinColor, 185)
                : ImStyle.WithAlpha(style.Secondary, 95);
            float stroke = compatible ? 1.8f : 1f;
            Im.DrawRoundedRectStroke(
                pin.HitRect.X,
                pin.HitRect.Y,
                pin.HitRect.Width,
                pin.HitRect.Height,
                MathF.Max(2f, pin.HitRect.Width * 0.22f),
                color,
                stroke);
        }
    }

    private static bool IsPinCompatibleForWireDrag(
        bool sourcePinIsOutput,
        bool sourceCanDriveExecution,
        bool sourceIsExecutionInput,
        DocColumnKind sourcePinKind,
        in PinVisual targetPin)
    {
        if (targetPin.IsOutput == sourcePinIsOutput)
        {
            return false;
        }

        if (sourcePinIsOutput)
        {
            if (sourceCanDriveExecution)
            {
                return IsExecutionInputPin(targetPin);
            }

            if (IsExecutionInputPin(targetPin))
            {
                return false;
            }

            return ArePinKindsCompatible(sourcePinKind, targetPin.ColumnKind);
        }

        if (sourceIsExecutionInput)
        {
            return targetPin.IsExecutionOutput;
        }

        return !targetPin.IsExecutionOutput &&
               !IsExecutionInputPin(targetPin) &&
               ArePinKindsCompatible(targetPin.ColumnKind, sourcePinKind);
    }

    private static void BeginWireDrag(ref NodeGraphViewState viewState, in PinVisual sourcePin)
    {
        viewState.WireDragActive = true;
        viewState.WireFromIsOutput = sourcePin.IsOutput;
        viewState.WireFromNodeRowId = sourcePin.NodeRowId;
        viewState.WireFromNodeRowIndex = sourcePin.NodeRowIndex;
        viewState.WireFromColumnId = sourcePin.ColumnId;
        viewState.WireFromPinId = sourcePin.PinId;
        viewState.WireFromColumnKind = sourcePin.ColumnKind;
        viewState.WireFromSourceTableId = sourcePin.SourceTableId;
        viewState.WireFromSourceRowId = sourcePin.SourceRowId;
        viewState.WireFromSourceColumnId = sourcePin.SourceColumnId;
        viewState.WireFromExecutionOutput = sourcePin.IsExecutionOutput;
        viewState.WireFromAnchor = sourcePin.Center;
    }

    private static void EndWireDrag(ref NodeGraphViewState viewState)
    {
        viewState.WireDragActive = false;
        viewState.WireFromIsOutput = false;
        viewState.WireFromNodeRowId = "";
        viewState.WireFromNodeRowIndex = -1;
        viewState.WireFromColumnId = "";
        viewState.WireFromPinId = "";
        viewState.WireFromColumnKind = default;
        viewState.WireFromSourceTableId = "";
        viewState.WireFromSourceRowId = "";
        viewState.WireFromSourceColumnId = "";
        viewState.WireFromExecutionOutput = false;
        viewState.WireFromAnchor = default;
    }

    private static bool ArePinKindsCompatible(DocColumnKind outputKind, DocColumnKind inputKind)
    {
        if (outputKind == inputKind)
        {
            return true;
        }

        if (outputKind == DocColumnKind.Formula && inputKind == DocColumnKind.Number)
        {
            return true;
        }

        if (inputKind == DocColumnKind.Formula && outputKind == DocColumnKind.Number)
        {
            return true;
        }

        return false;
    }

    private static bool IsExecutionInputPin(in PinVisual pin)
    {
        return !pin.IsOutput &&
               string.Equals(pin.PinId, ExecutionInputPinId, StringComparison.Ordinal);
    }

    private static bool IsExecutionOutputColumn(GraphSchema schema, DocColumn column)
    {
        return schema.ExecutionOutputColumn != null &&
               string.Equals(column.Id, schema.ExecutionOutputColumn.Id, StringComparison.Ordinal);
    }

    private static bool IsExecutionSourcePin(bool isExecutionOutput, DocColumnKind sourcePinKind)
    {
        _ = sourcePinKind;
        return isExecutionOutput;
    }

    private static bool IsRelationTargetingTable(DocTable sourceTable, DocColumn column, string targetTableId)
    {
        if (column.Kind != DocColumnKind.Relation || string.IsNullOrWhiteSpace(targetTableId))
        {
            return false;
        }

        string? resolvedTargetTableId = DocRelationTargetResolver.ResolveTargetTableId(sourceTable, column);
        return !string.IsNullOrWhiteSpace(resolvedTargetTableId) &&
               string.Equals(resolvedTargetTableId, targetTableId, StringComparison.Ordinal);
    }

    private static void TryCreateConnection(
        DocWorkspace workspace,
        DocTable nodeTable,
        GraphSchema schema,
        NodeGraphViewState viewState,
        in PinVisual targetInputPin)
    {
        if (!viewState.WireDragActive ||
            string.IsNullOrWhiteSpace(viewState.WireFromPinId))
        {
            return;
        }

        TryCreateConnection(
            workspace,
            nodeTable,
            schema,
            viewState.WireFromNodeRowId,
            viewState.WireFromPinId,
            viewState.WireFromColumnKind,
            viewState.WireFromSourceTableId,
            viewState.WireFromSourceRowId,
            viewState.WireFromSourceColumnId,
            viewState.WireFromExecutionOutput,
            targetInputPin);
    }

    private static void TryCreateConnectionFromReverseDrag(
        DocWorkspace workspace,
        DocTable nodeTable,
        GraphSchema schema,
        NodeGraphViewState viewState,
        in PinVisual sourceOutputPin)
    {
        if (!viewState.WireDragActive ||
            string.IsNullOrWhiteSpace(viewState.WireFromPinId))
        {
            return;
        }

        var targetInputPin = new PinVisual(
            NodeRowId: viewState.WireFromNodeRowId,
            NodeRowIndex: viewState.WireFromNodeRowIndex,
            ColumnId: viewState.WireFromColumnId,
            PinId: viewState.WireFromPinId,
            ColumnKind: viewState.WireFromColumnKind,
            IsOutput: false,
            SourceTableId: viewState.WireFromSourceTableId,
            SourceRowId: viewState.WireFromSourceRowId,
            SourceColumnId: viewState.WireFromSourceColumnId,
            IsExecutionOutput: false,
            Center: viewState.WireFromAnchor,
            HitRect: new ImRect(
                viewState.WireFromAnchor.X - 6f,
                viewState.WireFromAnchor.Y - 6f,
                12f,
                12f));

        TryCreateConnection(
            workspace,
            nodeTable,
            schema,
            sourceOutputPin.NodeRowId,
            sourceOutputPin.PinId,
            sourceOutputPin.ColumnKind,
            sourceOutputPin.SourceTableId,
            sourceOutputPin.SourceRowId,
            sourceOutputPin.SourceColumnId,
            sourceOutputPin.IsExecutionOutput,
            targetInputPin);
    }

    private static void TryCreateConnection(
        DocWorkspace workspace,
        DocTable nodeTable,
        GraphSchema schema,
        string sourceNodeRowId,
        string sourcePinId,
        DocColumnKind sourcePinKind,
        string sourceTableId,
        string sourceRowId,
        string sourceColumnId,
        bool sourceIsExecutionOutput,
        in PinVisual targetInputPin)
    {
        bool sourceCanDriveExecution = IsExecutionSourcePin(sourceIsExecutionOutput, sourcePinKind);
        bool targetIsExecutionInput = IsExecutionInputPin(targetInputPin);
        if (targetIsExecutionInput)
        {
            if (!sourceCanDriveExecution)
            {
                workspace.SetStatusMessage("Only execution outputs can connect to Execution input.");
                return;
            }

            TryConnectExecutionSourceToInput(
                workspace,
                nodeTable,
                sourceTableId,
                sourceRowId,
                sourceColumnId,
                targetInputPin.NodeRowId,
                targetInputPin.NodeRowIndex);
            return;
        }

        if (sourceCanDriveExecution)
        {
            workspace.SetStatusMessage("Execution outputs can only connect to Execution input.");
            return;
        }

        if (!ArePinKindsCompatible(sourcePinKind, targetInputPin.ColumnKind))
        {
            workspace.SetStatusMessage("Pins are not type compatible.");
            return;
        }

        if (!string.Equals(sourceTableId, nodeTable.Id, StringComparison.Ordinal))
        {
            workspace.SetStatusMessage("Subtable outputs only connect to Execution input.");
            return;
        }

        if (schema.EdgeTable == null ||
            schema.EdgeFromNodeColumn == null ||
            schema.EdgeFromPinColumn == null ||
            schema.EdgeToNodeColumn == null ||
            schema.EdgeToPinColumn == null)
        {
            workspace.SetStatusMessage("Cannot connect pins: edge table schema missing.");
            return;
        }

        if (!FindNodeRowById(nodeTable, targetInputPin.NodeRowId, out DocRow? targetRow))
        {
            return;
        }

        DocColumn? targetInputColumn = FindColumnById(nodeTable, targetInputPin.ColumnId);
        if (targetInputColumn == null)
        {
            return;
        }

        var commands = new List<DocCommand>(4);
        int removedIncomingCount = 0;
        for (int edgeRowIndex = schema.EdgeTable.Rows.Count - 1; edgeRowIndex >= 0; edgeRowIndex--)
        {
            DocRow edgeRow = schema.EdgeTable.Rows[edgeRowIndex];
            string toNodeId = edgeRow.GetCell(schema.EdgeToNodeColumn).StringValue ?? "";
            string toPinId = edgeRow.GetCell(schema.EdgeToPinColumn).StringValue ?? "";
            if (!string.Equals(toNodeId, targetInputPin.NodeRowId, StringComparison.Ordinal) ||
                !IsPinIdentifierMatch(nodeTable, targetInputPin.ColumnId, toPinId))
            {
                continue;
            }

            commands.Add(new DocCommand
            {
                Kind = DocCommandKind.RemoveRow,
                TableId = schema.EdgeTable.Id,
                RowIndex = edgeRowIndex,
                RowSnapshot = edgeRow,
            });
            removedIncomingCount++;
        }

        var newEdgeRow = new DocRow();
        if (!string.IsNullOrWhiteSpace(schema.EdgeTable.ParentRowColumnId))
        {
            newEdgeRow.SetCell(schema.EdgeTable.ParentRowColumnId, DocCellValue.Text(targetInputPin.NodeRowId));
        }

        newEdgeRow.SetCell(schema.EdgeFromNodeColumn.Id, DocCellValue.Text(sourceNodeRowId));
        newEdgeRow.SetCell(schema.EdgeFromPinColumn.Id, DocCellValue.Text(sourcePinId));
        newEdgeRow.SetCell(schema.EdgeToNodeColumn.Id, DocCellValue.Text(targetInputPin.NodeRowId));
        newEdgeRow.SetCell(schema.EdgeToPinColumn.Id, DocCellValue.Text(targetInputPin.PinId));

        int addEdgeRowIndex = schema.EdgeTable.Rows.Count - removedIncomingCount;
        if (addEdgeRowIndex < 0)
        {
            addEdgeRowIndex = 0;
        }

        commands.Add(new DocCommand
        {
            Kind = DocCommandKind.AddRow,
            TableId = schema.EdgeTable.Id,
            RowIndex = addEdgeRowIndex,
            RowSnapshot = newEdgeRow,
        });

        string formulaExpression = BuildGraphInputFormula(targetInputPin.PinId);
        DocCellValue oldTargetCellValue = targetRow!.GetCell(targetInputColumn);
        DocCellValue newTargetCellValue = oldTargetCellValue.WithCellFormulaExpression(formulaExpression);
        newTargetCellValue.FormulaError = null;
        commands.Add(new DocCommand
        {
            Kind = DocCommandKind.SetCell,
            TableId = nodeTable.Id,
            RowId = targetRow.Id,
            ColumnId = targetInputColumn.Id,
            OldCellValue = oldTargetCellValue,
            NewCellValue = newTargetCellValue,
        });

        workspace.ExecuteCommands(commands);
        workspace.SelectedRowIndex = targetInputPin.NodeRowIndex;
        workspace.SetStatusMessage("Pins connected.");
    }

    private static void TryConnectExecutionSourceToInput(
        DocWorkspace workspace,
        DocTable nodeTable,
        string sourceTableId,
        string sourceRowId,
        string sourceColumnId,
        string targetNodeRowId,
        int targetNodeRowIndex)
    {
        if (string.IsNullOrWhiteSpace(sourceTableId) ||
            string.IsNullOrWhiteSpace(sourceRowId) ||
            string.IsNullOrWhiteSpace(sourceColumnId))
        {
            workspace.SetStatusMessage("Source pin metadata is missing.");
            return;
        }

        DocTable? sourceTable = FindTableById(workspace.Project, sourceTableId);
        if (sourceTable == null)
        {
            workspace.SetStatusMessage("Source table not found for execution link.");
            return;
        }

        DocRow? sourceRow = FindRowById(sourceTable, sourceRowId);
        DocColumn? sourceColumn = FindColumnById(sourceTable, sourceColumnId);
        if (sourceRow == null || sourceColumn == null)
        {
            workspace.SetStatusMessage("Source row or column not found for execution link.");
            return;
        }

        if (sourceColumn.Kind != DocColumnKind.Relation)
        {
            workspace.SetStatusMessage("Execution source must be a relation column.");
            return;
        }

        DocCellValue oldCellValue = sourceRow.GetCell(sourceColumn);
        string oldTargetRowId = oldCellValue.StringValue ?? "";
        bool hasFormula = oldCellValue.HasCellFormulaExpression;
        bool hasFormulaError = !string.IsNullOrWhiteSpace(oldCellValue.FormulaError);
        if (string.Equals(oldTargetRowId, targetNodeRowId, StringComparison.Ordinal) &&
            !hasFormula &&
            !hasFormulaError)
        {
            workspace.SelectedRowIndex = targetNodeRowIndex;
            workspace.SetStatusMessage("Execution link already connected.");
            return;
        }

        var newCellValue = DocCellValue.Text(targetNodeRowId);
        newCellValue.FormulaError = null;
        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetCell,
            TableId = sourceTable.Id,
            RowId = sourceRow.Id,
            ColumnId = sourceColumn.Id,
            OldCellValue = oldCellValue,
            NewCellValue = newCellValue,
        });
        workspace.SelectedRowIndex = targetNodeRowIndex;
        workspace.SetStatusMessage("Execution link connected.");
    }

    private static void RemoveIncomingConnections(
        DocWorkspace workspace,
        DocTable nodeTable,
        GraphSchema schema,
        in PinVisual targetInputPin)
    {
        if (IsExecutionInputPin(targetInputPin))
        {
            RemoveIncomingExecutionLinks(workspace, nodeTable, schema.ExecutionOutputColumn, targetInputPin.NodeRowId);
            return;
        }

        if (schema.EdgeTable == null ||
            schema.EdgeToNodeColumn == null ||
            schema.EdgeToPinColumn == null)
        {
            return;
        }

        var commands = new List<DocCommand>(4);
        int removedCount = 0;
        for (int edgeRowIndex = schema.EdgeTable.Rows.Count - 1; edgeRowIndex >= 0; edgeRowIndex--)
        {
            DocRow edgeRow = schema.EdgeTable.Rows[edgeRowIndex];
            string toNodeId = edgeRow.GetCell(schema.EdgeToNodeColumn).StringValue ?? "";
            string toPinId = edgeRow.GetCell(schema.EdgeToPinColumn).StringValue ?? "";
            if (!string.Equals(toNodeId, targetInputPin.NodeRowId, StringComparison.Ordinal) ||
                !IsPinIdentifierMatch(nodeTable, targetInputPin.ColumnId, toPinId))
            {
                continue;
            }

            commands.Add(new DocCommand
            {
                Kind = DocCommandKind.RemoveRow,
                TableId = schema.EdgeTable.Id,
                RowIndex = edgeRowIndex,
                RowSnapshot = edgeRow,
            });
            removedCount++;
        }

        if (removedCount <= 0)
        {
            return;
        }

        if (FindNodeRowById(nodeTable, targetInputPin.NodeRowId, out DocRow? targetRow))
        {
            DocColumn? targetColumn = FindColumnById(nodeTable, targetInputPin.ColumnId);
            if (targetColumn != null)
            {
                DocCellValue oldCellValue = targetRow!.GetCell(targetColumn);
                string expectedFormula = BuildGraphInputFormula(targetInputPin.PinId);
                if (string.Equals(oldCellValue.CellFormulaExpression?.Trim(), expectedFormula, StringComparison.Ordinal))
                {
                    DocCellValue newCellValue = oldCellValue.ClearCellFormulaExpression();
                    newCellValue.FormulaError = null;
                    commands.Add(new DocCommand
                    {
                        Kind = DocCommandKind.SetCell,
                        TableId = nodeTable.Id,
                        RowId = targetRow.Id,
                        ColumnId = targetColumn.Id,
                        OldCellValue = oldCellValue,
                        NewCellValue = newCellValue,
                    });
                }
            }
        }

        workspace.ExecuteCommands(commands);
        workspace.SetStatusMessage("Input pin disconnected.");
    }

    private static void RemoveIncomingExecutionLinks(
        DocWorkspace workspace,
        DocTable nodeTable,
        DocColumn? executionOutputColumn,
        string targetNodeRowId)
    {
        if (executionOutputColumn == null)
        {
            return;
        }

        var commands = new List<DocCommand>(8);
        for (int rowIndex = 0; rowIndex < nodeTable.Rows.Count; rowIndex++)
        {
            DocRow row = nodeTable.Rows[rowIndex];
            DocCellValue oldCellValue = row.GetCell(executionOutputColumn);
            string currentTargetRowId = oldCellValue.StringValue ?? "";
            if (!string.Equals(currentTargetRowId, targetNodeRowId, StringComparison.Ordinal))
            {
                continue;
            }

            var newCellValue = DocCellValue.Text("");
            newCellValue.FormulaError = null;
            commands.Add(new DocCommand
            {
                Kind = DocCommandKind.SetCell,
                TableId = nodeTable.Id,
                RowId = row.Id,
                ColumnId = executionOutputColumn.Id,
                OldCellValue = oldCellValue,
                NewCellValue = newCellValue,
            });
        }

        if (commands.Count <= 0)
        {
            return;
        }

        workspace.ExecuteCommands(commands);
        workspace.SetStatusMessage("Execution links disconnected.");
    }

    private static string BuildGraphInputFormula(string pinId)
    {
        return "graph.in(\"" + pinId.Replace("\"", "\\\"", StringComparison.Ordinal) + "\")";
    }

    private static bool FindNodeRowById(DocTable table, string rowId, out DocRow? row)
    {
        row = FindRowById(table, rowId);
        return row != null;
    }

    private static void OpenCreateNodeMenu(
        ref NodeGraphViewState viewState,
        Vector2 pointerScreenPosition,
        ImRect contentRect)
    {
        viewState.CreateMenuOpen = true;
        viewState.CreateMenuScreenPosition = ClampCreateMenuPosition(pointerScreenPosition, contentRect);
        viewState.CreateMenuWorldPosition = ScreenToWorld(pointerScreenPosition, viewState, contentRect.X, contentRect.Y);
        viewState.CreateMenuScrollY = 0f;
    }

    private static Vector2 ClampCreateMenuPosition(Vector2 desiredScreenPosition, ImRect contentRect)
    {
        float minX = contentRect.X + 6f;
        float maxX = contentRect.Right - CreateMenuPanelWidth - 6f;
        float minY = contentRect.Y + 6f;
        float maxY = contentRect.Bottom - CreateMenuPanelHeight - 6f;
        float clampedX = Math.Clamp(desiredScreenPosition.X, minX, Math.Max(minX, maxX));
        float clampedY = Math.Clamp(desiredScreenPosition.Y, minY, Math.Max(minY, maxY));
        return new Vector2(clampedX, clampedY);
    }

    private static bool IsPointInsideCreateMenu(NodeGraphViewState viewState, Vector2 point)
    {
        if (!viewState.CreateMenuOpen)
        {
            return false;
        }

        var panelRect = new ImRect(
            viewState.CreateMenuScreenPosition.X,
            viewState.CreateMenuScreenPosition.Y,
            CreateMenuPanelWidth,
            CreateMenuPanelHeight);
        return panelRect.Contains(point);
    }

    private static void DrawCreateNodeMenu(
        DocWorkspace workspace,
        DocTable table,
        GraphSchema schema,
        NodeGraphViewSettings settings,
        NodeGraphViewState viewState,
        ImRect contentRect,
        ImStyle style)
    {
        _ = settings;
        if (!viewState.CreateMenuOpen || schema.TypeColumn == null || schema.PositionColumn == null)
        {
            return;
        }

        int optionCount = BuildTypeNameOptions(table, schema.TypeColumn, TypeNameOptionsScratch);
        if (optionCount <= 0)
        {
            TypeNameOptionsScratch[0] = DefaultTypeOption;
            optionCount = 1;
        }

        Vector2 panelPos = ClampCreateMenuPosition(viewState.CreateMenuScreenPosition, contentRect);
        viewState.CreateMenuScreenPosition = panelPos;

        Im.DrawRoundedRect(panelPos.X, panelPos.Y, CreateMenuPanelWidth, CreateMenuPanelHeight, 7f, ImStyle.WithAlpha(style.Surface, 252));
        Im.DrawRoundedRectStroke(panelPos.X, panelPos.Y, CreateMenuPanelWidth, CreateMenuPanelHeight, 7f, style.Border, 1f);
        Im.Text("Create node".AsSpan(), panelPos.X + 10f, panelPos.Y + 8f, style.FontSize - 1f, style.TextPrimary);

        float listX = panelPos.X + 8f;
        float listY = panelPos.Y + 28f;
        float listWidth = CreateMenuPanelWidth - 16f;
        float listHeight = CreateMenuPanelHeight - 36f;
        var listRect = new ImRect(listX, listY, listWidth, listHeight);
        Im.DrawRoundedRect(listRect.X, listRect.Y, listRect.Width, listRect.Height, 5f, ImStyle.WithAlpha(style.Background, 160));
        Im.DrawRoundedRectStroke(listRect.X, listRect.Y, listRect.Width, listRect.Height, 5f, style.Border, 1f);

        float contentHeight = optionCount * CreateMenuItemHeight + 4f;
        float contentOriginY = ImScrollView.Begin(listRect, contentHeight, ref viewState.CreateMenuScrollY, handleMouseWheel: true);
        bool createdNode = false;
        var input = Im.Context.Input;
        Vector2 mouseScreenPosition = Im.MousePos;
        for (int optionIndex = 0; optionIndex < optionCount; optionIndex++)
        {
            string typeName = TypeNameOptionsScratch[optionIndex];
            float rowY = contentOriginY + 2f + optionIndex * CreateMenuItemHeight;
            var rowRect = new ImRect(listRect.X + 2f, rowY, listRect.Width - 4f, CreateMenuItemHeight - 2f);
            bool hovered = rowRect.Contains(mouseScreenPosition);
            uint rowBackgroundColor = hovered
                ? ImStyle.WithAlpha(style.Hover, 120)
                : 0x00000000;
            if (rowBackgroundColor != 0x00000000)
            {
                Im.DrawRoundedRect(rowRect.X, rowRect.Y, rowRect.Width, rowRect.Height, 4f, rowBackgroundColor);
            }

            float textY = rowRect.Y + (rowRect.Height - style.FontSize) * 0.5f;
            Im.Text(typeName.AsSpan(), rowRect.X + 8f, textY, style.FontSize - 1f, style.TextPrimary);

            if (hovered && input.MousePressed)
            {
                CreateNodeAt(workspace, table, schema, typeName, viewState.CreateMenuWorldPosition);
                createdNode = true;
                break;
            }
        }

        int scrollbarWidgetId = Im.Context.GetId("node_create_type_scroll_" + table.Id);
        var scrollbarRect = new ImRect(listRect.Right - 8f, listRect.Y, 8f, listRect.Height);
        ImScrollView.End(scrollbarWidgetId, scrollbarRect, listRect.Height, contentHeight, ref viewState.CreateMenuScrollY);

        if (createdNode)
        {
            viewState.CreateMenuOpen = false;
            viewState.CreateMenuScrollY = 0f;
        }
    }

    private static void CreateNodeAt(
        DocWorkspace workspace,
        DocTable table,
        GraphSchema schema,
        string nodeTypeName,
        Vector2 worldPosition)
    {
        var newRow = new DocRow();
        if (schema.TypeColumn != null)
        {
            newRow.SetCell(schema.TypeColumn.Id, DocCellValue.Text(nodeTypeName));
        }

        if (schema.PositionColumn != null)
        {
            newRow.SetCell(schema.PositionColumn.Id, DocCellValue.Vec2(worldPosition.X, worldPosition.Y));
        }

        if (schema.TitleColumn != null)
        {
            newRow.SetCell(schema.TitleColumn.Id, DocCellValue.Text(nodeTypeName));
        }

        int insertRowIndex = table.Rows.Count;
        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.AddRow,
            TableId = table.Id,
            RowIndex = insertRowIndex,
            RowSnapshot = newRow,
        });
        workspace.SelectedRowIndex = insertRowIndex;
        workspace.SetStatusMessage("Node created.");
    }

    private static void CopyNodeToClipboard(DocTable table, DocRow sourceRow)
    {
        _nodeClipboardTableId = table.Id;
        _nodeClipboardRow = CloneRowSnapshot(sourceRow);
    }

    private static bool CanPasteNodeFromClipboard(DocTable table)
    {
        return _nodeClipboardRow != null &&
               string.Equals(_nodeClipboardTableId, table.Id, StringComparison.Ordinal);
    }

    private static void PasteNodeFromClipboard(
        DocWorkspace workspace,
        DocTable table,
        GraphSchema schema,
        ref NodeGraphViewState viewState)
    {
        if (!CanPasteNodeFromClipboard(table) || _nodeClipboardRow == null)
        {
            workspace.SetStatusMessage("No copied node to paste.");
            return;
        }

        DocRow pastedRow = CloneRowSnapshot(_nodeClipboardRow);
        pastedRow.Id = Guid.NewGuid().ToString();
        if (schema.PositionColumn != null)
        {
            DocCellValue sourcePosition = _nodeClipboardRow.GetCell(schema.PositionColumn);
            pastedRow.SetCell(
                schema.PositionColumn.Id,
                DocCellValue.Vec2(
                    sourcePosition.XValue + NodePasteOffsetWorldUnits,
                    sourcePosition.YValue + NodePasteOffsetWorldUnits));
        }

        int insertRowIndex = table.Rows.Count;
        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.AddRow,
            TableId = table.Id,
            RowIndex = insertRowIndex,
            RowSnapshot = pastedRow,
        });
        workspace.SelectedRowIndex = insertRowIndex;
        viewState.ContextMenuNodeRowId = pastedRow.Id;
        workspace.SetStatusMessage("Node pasted.");
    }

    private static void DeleteNode(
        DocWorkspace workspace,
        DocTable table,
        GraphSchema schema,
        string rowId,
        ref NodeGraphViewState viewState)
    {
        int nodeRowIndex = FindRowIndexById(table, rowId);
        if (nodeRowIndex < 0 || nodeRowIndex >= table.Rows.Count)
        {
            workspace.SetStatusMessage("Node not found.");
            return;
        }

        DocRow nodeRow = table.Rows[nodeRowIndex];
        var commands = new List<DocCommand>(16);
        var clearedInputCellKeys = new HashSet<string>(StringComparer.Ordinal);
        for (int sourceRowIndex = 0; sourceRowIndex < table.Rows.Count; sourceRowIndex++)
        {
            if (sourceRowIndex == nodeRowIndex)
            {
                continue;
            }

            DocRow sourceRow = table.Rows[sourceRowIndex];
            for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                DocColumn column = table.Columns[columnIndex];
                if (!IsExecutionOutputColumn(schema, column))
                {
                    continue;
                }

                DocCellValue oldCellValue = sourceRow.GetCell(column);
                string oldTargetRowId = oldCellValue.StringValue ?? "";
                if (!string.Equals(oldTargetRowId, rowId, StringComparison.Ordinal))
                {
                    continue;
                }

                var newCellValue = DocCellValue.Text("");
                newCellValue.FormulaError = null;
                commands.Add(new DocCommand
                {
                    Kind = DocCommandKind.SetCell,
                    TableId = table.Id,
                    RowId = sourceRow.Id,
                    ColumnId = column.Id,
                    OldCellValue = oldCellValue,
                    NewCellValue = newCellValue,
                });
            }
        }

        if (schema.EdgeTable != null &&
            schema.EdgeFromNodeColumn != null &&
            schema.EdgeToNodeColumn != null &&
            schema.EdgeToPinColumn != null)
        {
            for (int edgeRowIndex = schema.EdgeTable.Rows.Count - 1; edgeRowIndex >= 0; edgeRowIndex--)
            {
                DocRow edgeRow = schema.EdgeTable.Rows[edgeRowIndex];
                string fromNodeId = edgeRow.GetCell(schema.EdgeFromNodeColumn).StringValue ?? "";
                string toNodeId = edgeRow.GetCell(schema.EdgeToNodeColumn).StringValue ?? "";
                bool edgeTouchesDeletedNode = string.Equals(fromNodeId, rowId, StringComparison.Ordinal) ||
                                              string.Equals(toNodeId, rowId, StringComparison.Ordinal);
                if (!edgeTouchesDeletedNode)
                {
                    continue;
                }

                commands.Add(new DocCommand
                {
                    Kind = DocCommandKind.RemoveRow,
                    TableId = schema.EdgeTable.Id,
                    RowIndex = edgeRowIndex,
                    RowSnapshot = edgeRow,
                });

                if (!string.Equals(fromNodeId, rowId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!FindNodeRowById(table, toNodeId, out DocRow? targetRow))
                {
                    continue;
                }

                string toPinId = edgeRow.GetCell(schema.EdgeToPinColumn).StringValue ?? "";
                DocColumn? targetInputColumn = FindColumnByPinIdentifier(table, toPinId);
                if (targetInputColumn == null)
                {
                    continue;
                }

                string cellKey = targetRow!.Id + ":" + targetInputColumn.Id;
                if (!clearedInputCellKeys.Add(cellKey))
                {
                    continue;
                }

                DocCellValue oldTargetCellValue = targetRow.GetCell(targetInputColumn);
                string expectedFormulaExpression = BuildGraphInputFormula(toPinId);
                if (!string.Equals(oldTargetCellValue.CellFormulaExpression?.Trim(), expectedFormulaExpression, StringComparison.Ordinal))
                {
                    continue;
                }

                DocCellValue newTargetCellValue = oldTargetCellValue.ClearCellFormulaExpression();
                newTargetCellValue.FormulaError = null;
                commands.Add(new DocCommand
                {
                    Kind = DocCommandKind.SetCell,
                    TableId = table.Id,
                    RowId = targetRow.Id,
                    ColumnId = targetInputColumn.Id,
                    OldCellValue = oldTargetCellValue,
                    NewCellValue = newTargetCellValue,
                });
            }
        }

        commands.Add(new DocCommand
        {
            Kind = DocCommandKind.RemoveRow,
            TableId = table.Id,
            RowIndex = nodeRowIndex,
            RowSnapshot = nodeRow,
        });

        workspace.ExecuteCommands(commands);
        int remainingRowCount = table.Rows.Count;
        workspace.SelectedRowIndex = remainingRowCount > 0
            ? Math.Min(nodeRowIndex, remainingRowCount - 1)
            : -1;
        viewState.ContextMenuNodeRowId = "";
        if (string.Equals(_titleEditRowId, rowId, StringComparison.Ordinal))
        {
            EndNodeTitleEdit();
        }

        if (string.Equals(_formulaEditRowId, rowId, StringComparison.Ordinal))
        {
            CloseCellFormulaEditor();
        }

        if (string.Equals(viewState.DraggedRowId, rowId, StringComparison.Ordinal))
        {
            viewState.DragActive = false;
            viewState.DraggedRowId = "";
            viewState.DragPositionColumnId = "";
        }

        if (viewState.WireDragActive &&
            string.Equals(viewState.WireFromNodeRowId, rowId, StringComparison.Ordinal))
        {
            EndWireDrag(ref viewState);
        }

        workspace.SetStatusMessage("Node deleted.");
    }

    private static void BeginNodeEditorScaleScope(
        NodeVisual nodeVisual,
        out bool pushedScale,
        out float inverseScale,
        out float pivotX,
        out float pivotY)
    {
        pivotX = nodeVisual.ScreenRect.X;
        pivotY = nodeVisual.ScreenRect.Y;
        float nodeScale = nodeVisual.Scale;
        if (!float.IsFinite(nodeScale) || nodeScale <= 0f)
        {
            nodeScale = 1f;
        }

        if (MathF.Abs(nodeScale - 1f) <= 0.0001f)
        {
            inverseScale = 1f;
            pushedScale = false;
            return;
        }

        inverseScale = 1f / nodeScale;
        Im.PushScale(nodeScale, new Vector2(pivotX, pivotY));
        pushedScale = true;
    }

    private static void EndNodeEditorScaleScope(bool pushedScale)
    {
        if (pushedScale)
        {
            Im.PopTransform();
        }
    }

    private static float ToNodeLocalPosition(float screenValue, float pivotValue, float inverseScale)
    {
        return pivotValue + (screenValue - pivotValue) * inverseScale;
    }

    private static float ToNodeLocalLength(float screenLength, float inverseScale)
    {
        return screenLength * inverseScale;
    }

    private static float ToNodeScreenPosition(float localValue, float pivotValue, float inverseScale)
    {
        if (!float.IsFinite(inverseScale) || inverseScale <= 0f)
        {
            return localValue;
        }

        return pivotValue + (localValue - pivotValue) / inverseScale;
    }

    private static void DrawInlineSettingEditor(
        DocWorkspace workspace,
        DocTable table,
        DocRow row,
        DocColumn column,
        NodeVisual nodeVisual,
        float rowY,
        float rowHeight,
        ImStyle style)
    {
        if (column.Kind == DocColumnKind.Subtable)
        {
            DrawInlineSubtableSettingEditor(
                workspace,
                table,
                row,
                column,
                nodeVisual,
                rowY,
                rowHeight,
                style,
                allowActions: true);
            return;
        }

        float nodeScale = nodeVisual.Scale;
        bool expandedTextEditor = TryComputeExpandedSettingTextLayoutUnits(row, column, out float expandedControlHeightUnits, out _);
        float labelX = nodeVisual.ScreenRect.X + (NodeInlineHorizontalPadding * nodeScale);
        float controlX;
        float controlWidth;
        float labelFontSize = GetInlineLabelHeightUnits();
        float compactControlHeightUnits = GetMinimumInlineControlHeightUnits(NodeSettingRowHeight);

        if (expandedTextEditor)
        {
            controlX = labelX;
            controlWidth = nodeVisual.ScreenRect.Width - (NodeInlineHorizontalPadding * 2f * nodeScale);
        }
        else
        {
            float labelWidth = nodeVisual.ScreenRect.Width * NodeSettingLabelWidthRatio;
            controlX = labelX + labelWidth + (NodeInlineColumnGap * nodeScale);
            float rightPadding = NodeInlineHorizontalPadding * nodeScale;
            controlWidth = nodeVisual.ScreenRect.Right - rightPadding - controlX;
        }

        if (controlWidth <= 10f)
        {
            return;
        }

        BeginNodeEditorScaleScope(nodeVisual, out bool pushedScale, out float invScale, out float pivotX, out float pivotY);
        try
        {
            float localRowY = ToNodeLocalPosition(rowY, pivotY, invScale);
            float localRowHeight = ToNodeLocalLength(rowHeight, invScale);
            float localLabelX = ToNodeLocalPosition(labelX, pivotX, invScale);
            float localControlX = ToNodeLocalPosition(controlX, pivotX, invScale);
            float localControlWidth = ToNodeLocalLength(controlWidth, invScale);
            float localControlY;
            float localControlHeight;
            float localTextY;
            if (expandedTextEditor)
            {
                localTextY = localRowY + NodeExpandedTextLabelTopPadding;
                localControlY = localTextY + labelFontSize + NodeExpandedTextLabelGap;
                float occupiedBeforeControl = NodeExpandedTextLabelTopPadding + labelFontSize + NodeExpandedTextLabelGap;
                localControlHeight = MathF.Max(
                    expandedControlHeightUnits,
                    localRowHeight - occupiedBeforeControl - NodeExpandedTextBottomPadding);
            }
            else
            {
                localTextY = localRowY + (localRowHeight - style.FontSize) * 0.5f;
                localControlHeight = MathF.Max(compactControlHeightUnits, localRowHeight - 2f);
                localControlY = localRowY + MathF.Max(0f, (localRowHeight - localControlHeight) * 0.5f);
            }

            Im.Text(column.Name.AsSpan(), localLabelX, localTextY, labelFontSize, style.TextSecondary);

            string widgetId = "node_inline_setting_" + table.Id + "_" + row.Id + "_" + column.Id;
            DrawInlineCellEditorControl(
                workspace,
                table,
                row,
                column,
                localControlX,
                localControlY,
                localControlWidth,
                localControlHeight,
                widgetId,
                allowWrappedTextArea: expandedTextEditor);
        }
        finally
        {
            EndNodeEditorScaleScope(pushedScale);
        }
    }

    private static void DrawInlineSubtableSettingEditor(
        DocWorkspace workspace,
        DocTable parentTable,
        DocRow parentRow,
        DocColumn subtableColumn,
        NodeVisual nodeVisual,
        float rowY,
        float rowHeight,
        ImStyle style,
        bool allowActions)
    {
        float nodeScale = nodeVisual.Scale;
        float sectionX = nodeVisual.ScreenRect.X + (NodeInlineHorizontalPadding * nodeScale);
        float sectionWidth = nodeVisual.ScreenRect.Width - (NodeInlineHorizontalPadding * 2f * nodeScale);
        if (sectionWidth <= 16f)
        {
            return;
        }

        float sectionY = rowY + nodeScale;
        float sectionHeight = MathF.Max(22f * nodeScale, rowHeight - (2f * nodeScale));
        var subtableAreaRect = new ImRect(sectionX, sectionY, sectionWidth, sectionHeight);
        float addButtonHeight = MathF.Max(18f, style.MinButtonHeight - 2f) * nodeScale;
        float addButtonWidth = 24f * nodeScale;
        float addButtonX = sectionX + ((sectionWidth - addButtonWidth) * 0.5f);
        float addButtonY = sectionY + sectionHeight - (addButtonHeight * 0.5f);
        var addButtonRect = new ImRect(addButtonX, addButtonY, addButtonWidth, addButtonHeight);
        bool subtableAreaHovered = subtableAreaRect.Contains(Im.MousePos);
        bool addButtonHovered = addButtonRect.Contains(Im.MousePos);
        bool showAddButton = allowActions && (subtableAreaHovered || addButtonHovered);

        BeginNodeEditorScaleScope(nodeVisual, out bool pushedScale, out float invScale, out float pivotX, out float pivotY);
        try
        {
            float localSectionX = ToNodeLocalPosition(sectionX, pivotX, invScale);
            float localSectionWidth = ToNodeLocalLength(sectionWidth, invScale);
            float localSectionY = ToNodeLocalPosition(sectionY, pivotY, invScale);
            float localSectionHeight = ToNodeLocalLength(sectionHeight, invScale);
            Im.DrawRoundedRect(
                localSectionX,
                localSectionY,
                localSectionWidth,
                localSectionHeight,
                5f,
                ImStyle.WithAlpha(style.Surface, 220));
            Im.DrawRoundedRectStroke(
                localSectionX,
                localSectionY,
                localSectionWidth,
                localSectionHeight,
                5f,
                ImStyle.WithAlpha(style.Border, 160),
                1f);

            if (!TryResolveSubtableChildTable(workspace.Project, subtableColumn, out DocTable? childTable))
            {
                float emptyY = localSectionY + (localSectionHeight - style.FontSize) * 0.5f;
                Im.Text("(subtable not found)".AsSpan(), localSectionX + 6f, emptyY, style.FontSize - 1f, style.Secondary);
                return;
            }

            string subtableStateKey = "node_subtable_" + parentTable.Id + "_" + parentRow.Id + "_" + subtableColumn.Id;
            if (TryResolveNodeSubtableSectionRenderer(subtableColumn, out IDerpDocNodeSubtableSectionRenderer? sectionRenderer))
            {
                var sectionRect = new ImRect(localSectionX + 2f, localSectionY + 2f, localSectionWidth - 4f, localSectionHeight - 4f);
                bool drawnByPlugin = sectionRenderer.DrawSubtableSection(
                    workspace,
                    parentTable,
                    parentRow,
                    subtableColumn,
                    childTable,
                    sectionRect,
                    interactive: allowActions,
                    stateKey: subtableStateKey);
                if (drawnByPlugin)
                {
                    return;
                }
            }

            DrawDefaultSubtableSettingBody(
                workspace,
                parentTable,
                parentRow,
                subtableColumn,
                childTable,
                nodeVisual,
                localSectionX,
                localSectionY,
                localSectionWidth,
                localSectionHeight,
                style,
                allowActions,
                showAddButton,
                invScale,
                pivotX,
                pivotY);
        }
        finally
        {
            EndNodeEditorScaleScope(pushedScale);
        }
    }

    private static void DrawDefaultSubtableSettingBody(
        DocWorkspace workspace,
        DocTable parentTable,
        DocRow parentRow,
        DocColumn subtableColumn,
        DocTable childTable,
        NodeVisual nodeVisual,
        float x,
        float y,
        float width,
        float height,
        ImStyle style,
        bool allowActions,
        bool showAddButton,
        float inverseScale,
        float pivotX,
        float pivotY)
    {
        int matchingRowCount = CollectSubtableRowsForParent(childTable, parentRow.Id, SubtableRowsScratch);
        int overflowRowCount = Math.Max(0, matchingRowCount - NodeSubtableSectionMaxVisibleRows);
        NodeSubtableDisplayMode displayMode = ResolveNodeSubtableDisplayMode(subtableColumn.SubtableDisplayRendererId, out string? customRendererId);
        string title = subtableColumn.Name + " (" + matchingRowCount.ToString(CultureInfo.InvariantCulture) + ")";
        float titleY = y + 4f;
        Im.Text(title.AsSpan(), x + 6f, titleY, style.FontSize - 1f, style.TextPrimary);
        string modeLabel = GetNodeSubtableDisplayModeLabel(displayMode);
        float modeLabelWidth = Im.MeasureTextWidth(modeLabel.AsSpan(), style.FontSize - 2f);
        float modeLabelX = x + width - modeLabelWidth - 8f;
        Im.Text(modeLabel.AsSpan(), modeLabelX, titleY + 1f, style.FontSize - 2f, style.TextSecondary);

        float buttonHeight = MathF.Max(18f, style.MinButtonHeight - 2f);
        float addButtonWidth = 24f;
        if (showAddButton)
        {
            float addButtonX = x + ((width - addButtonWidth) * 0.5f);
            float addButtonY = y + height - (buttonHeight * 0.5f);
            string addButtonId = "+##node_subtable_add_" + parentTable.Id + "_" + parentRow.Id + "_" + subtableColumn.Id;
            if (Im.Button(addButtonId, addButtonX, addButtonY, addButtonWidth, buttonHeight))
            {
                AddSubtableRowFromNode(workspace, childTable, parentRow.Id);
            }
        }

        float lineY = y + NodeSubtableSectionHeaderHeight + 4f;
        float contentBottomInset = 4f;
        float maxContentBottom = y + height - contentBottomInset;
        if (displayMode == NodeSubtableDisplayMode.Grid &&
            TryDrawGridSubtablePreviewForNode(
                workspace,
                parentTable,
                parentRow,
                subtableColumn,
                childTable,
                nodeVisual,
                x,
                y,
                width,
                maxContentBottom,
                allowActions,
                matchingRowCount,
                inverseScale,
                pivotX,
                pivotY))
        {
            return;
        }

        if (TryDrawSubtableRendererPreviewForNode(
                workspace,
                childTable,
                subtableColumn,
                parentRow,
                x,
                y,
                width,
                maxContentBottom,
                displayMode,
                customRendererId))
        {
            return;
        }

        if (matchingRowCount <= 0)
        {
            Im.Text("(no rows)".AsSpan(), x + 6f, lineY, style.FontSize - 1f, style.TextSecondary);
            return;
        }

        int visibleRowCount = Math.Min(matchingRowCount, NodeSubtableSectionMaxVisibleRows);
        for (int rowIndex = 0; rowIndex < visibleRowCount; rowIndex++)
        {
            if (lineY + NodeSubtableSectionLineHeight > maxContentBottom)
            {
                break;
            }

            DocRow childRow = SubtableRowsScratch[rowIndex];
            string line = FormatSubtableRowLineForMode(childTable, childRow, rowIndex, displayMode);
            Im.Text(line.AsSpan(), x + 8f, lineY, style.FontSize - 2f, style.TextSecondary);
            lineY += NodeSubtableSectionLineHeight;
        }

        if (overflowRowCount > 0 && lineY + NodeSubtableSectionLineHeight <= maxContentBottom)
        {
            string overflowLine = "+" + overflowRowCount.ToString(CultureInfo.InvariantCulture) + " more";
            Im.Text(overflowLine.AsSpan(), x + 8f, lineY, style.FontSize - 2f, style.TextSecondary);
        }
    }

    private static void AddSubtableRowFromNode(
        DocWorkspace workspace,
        DocTable childTable,
        string parentRowId)
    {
        var childRow = new DocRow();
        if (!string.IsNullOrWhiteSpace(childTable.ParentRowColumnId))
        {
            childRow.SetCell(childTable.ParentRowColumnId, DocCellValue.Text(parentRowId));
        }

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.AddRow,
            TableId = childTable.Id,
            RowIndex = childTable.Rows.Count,
            RowSnapshot = childRow,
        });
        workspace.SetStatusMessage("Added row to subtable '" + childTable.Name + "'.");
    }

    private static void RemoveSubtableRowFromNode(
        DocWorkspace workspace,
        DocTable childTable,
        string childRowId)
    {
        int rowIndex = FindRowIndexById(childTable, childRowId);
        if (rowIndex < 0 || rowIndex >= childTable.Rows.Count)
        {
            return;
        }

        DocRow rowSnapshot = childTable.Rows[rowIndex];
        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.RemoveRow,
            TableId = childTable.Id,
            RowIndex = rowIndex,
            RowSnapshot = rowSnapshot,
        });
        workspace.SetStatusMessage("Removed row from subtable '" + childTable.Name + "'.");
    }

    private static bool TryDrawGridSubtablePreviewForNode(
        DocWorkspace workspace,
        DocTable parentTable,
        DocRow parentRow,
        DocColumn subtableColumn,
        DocTable childTable,
        NodeVisual nodeVisual,
        float x,
        float y,
        float width,
        float maxContentBottom,
        bool allowActions,
        int matchingRowCount,
        float inverseScale,
        float pivotX,
        float pivotY)
    {
        float previewX = x + 3f;
        float previewY = y + NodeSubtableSectionHeaderHeight + 2f;
        float previewWidth = MathF.Max(8f, width - 6f);
        float previewHeight = MathF.Max(8f, maxContentBottom - previewY);
        if (previewWidth < 8f || previewHeight < 8f)
        {
            return false;
        }

        BuildNodeSubtableGridColumns(childTable, SubtableGridColumnsScratch, NodeSubtableGridMaxColumns);
        DocColumn? relationColumn = ResolveSubtableRelationPinColumn(childTable);

        bool hasDisplayColumns = SubtableGridColumnsScratch.Count > 0;
        if (!hasDisplayColumns && relationColumn == null)
        {
            Im.Text("(no columns)".AsSpan(), previewX + 4f, previewY + 4f, Im.Style.FontSize - 2f, Im.Style.TextSecondary);
            return true;
        }

        float fontSize = MathF.Max(8f, Im.Style.FontSize - 2f);
        float baseRowHeight = MathF.Max(NodeSubtableSectionLineHeight, fontSize + 6f);
        float headerHeight = baseRowHeight;
        float gutterWidth = NodeSubtableGridGutterWidth;
        float dataGridX = previewX + gutterWidth;
        float dataGridWidth = MathF.Max(8f, previewWidth - gutterWidth);
        int visibleColumnCount = hasDisplayColumns ? SubtableGridColumnsScratch.Count : 1;
        float cellWidth = dataGridWidth / visibleColumnCount;
        float gridHeight = headerHeight;
        int visibleRowCount = 0;
        SubtableGridRowHeightsScratch.Clear();
        for (int rowIndex = 0; rowIndex < matchingRowCount; rowIndex++)
        {
            DocRow childRow = SubtableRowsScratch[rowIndex];
            float rowHeightForRow = hasDisplayColumns
                ? ComputeNodeSubtableGridRowHeight(childRow, visibleColumnCount, cellWidth, baseRowHeight, relationColumn)
                : baseRowHeight;
            float remainingHeight = previewHeight - gridHeight;
            if (remainingHeight <= 0f)
            {
                break;
            }

            if (rowHeightForRow > remainingHeight + 0.001f)
            {
                if (visibleRowCount <= 0)
                {
                    rowHeightForRow = MathF.Max(2f, remainingHeight);
                }
                else
                {
                    break;
                }
            }

            SubtableGridRowHeightsScratch.Add(rowHeightForRow);
            gridHeight += rowHeightForRow;
            visibleRowCount++;
        }

        uint surfaceColor = ImStyle.WithAlpha(Im.Style.Surface, 228);
        uint headerColor = ImStyle.WithAlpha(Im.Style.Surface, 244);
        uint borderColor = ImStyle.WithAlpha(Im.Style.Border, 190);
        uint dividerColor = ImStyle.WithAlpha(Im.Style.Border, 156);
        Im.DrawRect(previewX, previewY, previewWidth, gridHeight, surfaceColor);
        Im.DrawRect(previewX, previewY, gutterWidth, gridHeight, ImStyle.WithAlpha(Im.Style.Background, 120));
        Im.DrawRect(previewX, previewY, previewWidth, headerHeight, headerColor);
        Im.DrawRoundedRectStroke(previewX, previewY, previewWidth, gridHeight, 4f, borderColor, 1f);
        Im.DrawLine(dataGridX, previewY, dataGridX, previewY + gridHeight, 1f, dividerColor);

        for (int dividerIndex = 1; dividerIndex < visibleColumnCount; dividerIndex++)
        {
            float lineX = dataGridX + cellWidth * dividerIndex;
            Im.DrawLine(lineX, previewY, lineX, previewY + gridHeight, 1f, dividerColor);
        }

        float headerBottom = previewY + headerHeight;
        Im.DrawLine(previewX, headerBottom, previewX + previewWidth, headerBottom, 1f, dividerColor);
        float rowLineY = headerBottom;
        for (int rowIndex = 0; rowIndex < visibleRowCount; rowIndex++)
        {
            rowLineY += SubtableGridRowHeightsScratch[rowIndex];
            float lineY = rowLineY;
            if (lineY >= previewY + gridHeight)
            {
                break;
            }

            Im.DrawLine(previewX, lineY, previewX + previewWidth, lineY, 1f, dividerColor);
        }

        float relationPinSizeScreen = MathF.Max(3f, NodeExecPinSize * nodeVisual.Scale);
        float relationPinSize = ToNodeLocalLength(relationPinSizeScreen, inverseScale);
        var clipRect = new ImRect(
            previewX + 1f,
            previewY + 1f,
            MathF.Max(0f, previewWidth - 2f),
            MathF.Max(0f, gridHeight - 2f));
        Im.PushClipRect(clipRect);
        try
        {
            if (hasDisplayColumns)
            {
                for (int columnIndex = 0; columnIndex < visibleColumnCount; columnIndex++)
                {
                    DocColumn column = SubtableGridColumnsScratch[columnIndex];
                    float cellX = dataGridX + cellWidth * columnIndex;
                    DrawNodeSubtableGridCellText(
                        column.Name,
                        cellX,
                        previewY,
                        cellWidth,
                        headerHeight,
                        fontSize,
                        Im.Style.TextPrimary);
                }
            }
            else
            {
                DrawNodeSubtableGridCellText(
                    "Rows",
                    dataGridX,
                    previewY,
                    cellWidth,
                    headerHeight,
                    fontSize,
                    Im.Style.TextSecondary);
            }

            float mouseLocalX = Im.MousePos.X;
            float mouseLocalY = Im.MousePos.Y;
            string removeRowId = "";
            float rowY = previewY + headerHeight;
            for (int rowIndex = 0; rowIndex < visibleRowCount; rowIndex++)
            {
                DocRow childRow = SubtableRowsScratch[rowIndex];
                float rowHeightForRow = SubtableGridRowHeightsScratch[rowIndex];
                bool rowHovered = allowActions &&
                                  mouseLocalX >= previewX &&
                                  mouseLocalX <= previewX + previewWidth &&
                                  mouseLocalY >= rowY &&
                                  mouseLocalY <= rowY + rowHeightForRow;
                if (rowHovered)
                {
                    Im.DrawRect(previewX + 1f, rowY + 1f, MathF.Max(0f, gutterWidth - 2f), MathF.Max(0f, rowHeightForRow - 2f), ImStyle.WithAlpha(Im.Style.Hover, 90));
                    float removeButtonSize = MathF.Min(NodeSubtableGridRemoveButtonSize, MathF.Max(8f, rowHeightForRow - 4f));
                    float removeButtonX = previewX + (gutterWidth - removeButtonSize) * 0.5f;
                    float removeButtonY = rowY + (rowHeightForRow - removeButtonSize) * 0.5f;
                    string removeButtonId = "-##node_subtable_remove_" +
                                            parentTable.Id + "_" +
                                            parentRow.Id + "_" +
                                            subtableColumn.Id + "_" +
                                            childRow.Id;
                    if (Im.Button(removeButtonId, removeButtonX, removeButtonY, removeButtonSize, removeButtonSize))
                    {
                        removeRowId = childRow.Id;
                    }
                }

                if (hasDisplayColumns)
                {
                    for (int columnIndex = 0; columnIndex < visibleColumnCount; columnIndex++)
                    {
                        DocColumn column = SubtableGridColumnsScratch[columnIndex];
                        float cellX = dataGridX + cellWidth * columnIndex;
                        float editorX = cellX + 1f;
                        float editorY = rowY + 1f;
                        float editorWidth = MathF.Max(8f, cellWidth - 2f);
                        float editorHeight = MathF.Max(2f, rowHeightForRow - 2f);
                        bool usesInlineTextEditor = UsesInlineTextEditor(column);
                        bool allowWrappedTextArea = false;
                        if (usesInlineTextEditor)
                        {
                            string editorText = childRow.GetCell(column).StringValue ?? "";
                            float minimumEditorHeight = MathF.Max(2f, baseRowHeight - 2f);
                            _ = MeasureInlineTextEditorHeightUnits(
                                editorText,
                                editorWidth,
                                minimumEditorHeight,
                                out int visualLineCount);
                            allowWrappedTextArea = visualLineCount > 1;
                        }

                        if (allowActions)
                        {
                            string widgetId = "node_subtable_grid_cell_" +
                                              parentTable.Id + "_" +
                                              parentRow.Id + "_" +
                                              subtableColumn.Id + "_" +
                                              childRow.Id + "_" +
                                              column.Id;
                            DrawInlineCellEditorControl(
                                workspace,
                                childTable,
                                childRow,
                                column,
                                editorX,
                                editorY,
                                editorWidth,
                                editorHeight,
                                widgetId,
                                allowWrappedTextArea: allowWrappedTextArea);
                        }
                        else
                        {
                            string valueText = FormatCellValueForDisplay(column, childRow.GetCell(column));
                            DrawNodeSubtableGridCellText(
                                valueText,
                                cellX,
                                rowY,
                                editorWidth,
                                rowHeightForRow,
                                fontSize,
                                Im.Style.TextSecondary,
                                allowWrappedText: allowWrappedTextArea);
                        }
                    }
                }

                rowY += rowHeightForRow;
            }

            if (allowActions && !string.IsNullOrWhiteSpace(removeRowId))
            {
                RemoveSubtableRowFromNode(workspace, childTable, removeRowId);
            }
        }
        finally
        {
            Im.PopClipRect();
        }

        if (relationColumn != null)
        {
            uint pinColor = ResolveExecutionFlowColor(Im.Style);
            for (int rowIndex = 0; rowIndex < visibleRowCount; rowIndex++)
            {
                DocRow childRow = SubtableRowsScratch[rowIndex];
                float slotCenterX = ResolveSlotCenter(previewX + relationPinSize, previewX + previewWidth - relationPinSize, rowIndex, visibleRowCount);
                float pinCenterScreenX = ToNodeScreenPosition(slotCenterX, pivotX, inverseScale);
                float pinCenterScreenY = ResolveExecutionPinCenterY(nodeVisual, isOutputPin: true, relationPinSizeScreen);
                float pinCenterLocalY = ToNodeLocalPosition(pinCenterScreenY, pivotY, inverseScale);
                bool relationOutputConnected = IsExecutionOutputConnected(childRow, relationColumn);
                DrawExecutionPinGlyph(slotCenterX, pinCenterLocalY, relationPinSize, pinColor, filled: relationOutputConnected);
                string pinId = BuildSubtableExecutionPinId(subtableColumn.Id, childTable.Id, childRow.Id, relationColumn.Id);
                RegisterPinVisualAtPosition(
                    nodeVisual,
                    relationColumn.Id,
                    pinId,
                    DocColumnKind.Relation,
                    isOutputPin: true,
                    pinCenterScreenX,
                    pinCenterScreenY,
                    relationPinSizeScreen,
                    childTable.Id,
                    childRow.Id,
                    relationColumn.Id,
                    isExecutionOutput: true);
            }
        }

        if (matchingRowCount <= 0 && previewY + gridHeight + fontSize + 4f <= maxContentBottom)
        {
            Im.Text("(no rows)".AsSpan(), previewX + 4f, previewY + gridHeight + 2f, fontSize, Im.Style.TextSecondary);
        }

        return true;
    }

    private static void DrawNodeSubtableGridCellText(
        string text,
        float x,
        float y,
        float width,
        float height,
        float fontSize,
        uint color,
        bool allowWrappedText = false)
    {
        if (width <= 2f || height <= 2f)
        {
            return;
        }

        string renderText = string.IsNullOrWhiteSpace(text) ? "-" : text;
        float textX = x + NodeSubtableGridCellPaddingX;
        float availableWidth = MathF.Max(2f, width - NodeSubtableGridCellPaddingX * 2f);
        if (allowWrappedText)
        {
            float textY = y + 2f;
            RichTextRenderer.DrawPlain(renderText, textX, textY, availableWidth, fontSize, color);
            return;
        }

        float singleLineTextY = y + MathF.Max(0f, (height - fontSize) * 0.5f);
        Im.Text(renderText.AsSpan(), textX, singleLineTextY, fontSize, color);
    }

    private static float ComputeNodeSubtableGridRowHeight(
        DocRow childRow,
        int visibleColumnCount,
        float cellWidth,
        float baseRowHeight,
        DocColumn? relationColumn)
    {
        float minimumControlHeight = MathF.Max(
            GetMinimumInlineControlHeightUnits(NodeSettingRowHeight),
            baseRowHeight);
        float rowHeight = minimumControlHeight;
        float minimumEditorHeight = MathF.Max(2f, minimumControlHeight - 2f);
        for (int columnIndex = 0; columnIndex < visibleColumnCount; columnIndex++)
        {
            DocColumn column = SubtableGridColumnsScratch[columnIndex];
            if (!UsesInlineTextEditor(column))
            {
                continue;
            }

            float editorWidth = MathF.Max(8f, cellWidth - 2f);
            string cellText = childRow.GetCell(column).StringValue ?? "";
            float measuredHeight = MeasureInlineTextEditorHeightUnits(
                cellText,
                editorWidth,
                minimumEditorHeight,
                out int visualLineCount);
            if (visualLineCount > 1)
            {
                rowHeight = Math.Max(rowHeight, measuredHeight + 2f);
            }
        }

        return rowHeight;
    }

    private static void BuildNodeSubtableGridColumns(DocTable childTable, List<DocColumn> destinationColumns, int maxColumns)
    {
        destinationColumns.Clear();
        if (maxColumns <= 0)
        {
            return;
        }

        for (int columnIndex = 0; columnIndex < childTable.Columns.Count; columnIndex++)
        {
            DocColumn column = childTable.Columns[columnIndex];
            if (column.IsHidden || IsParentRowColumn(childTable, column) || column.Kind == DocColumnKind.Relation)
            {
                continue;
            }

            destinationColumns.Add(column);
            if (destinationColumns.Count >= maxColumns)
            {
                return;
            }
        }
    }

    private static DocColumn? ResolveSubtableRelationPinColumn(DocTable childTable)
    {
        for (int columnIndex = 0; columnIndex < childTable.Columns.Count; columnIndex++)
        {
            DocColumn column = childTable.Columns[columnIndex];
            if (column.Kind != DocColumnKind.Relation)
            {
                continue;
            }

            return column;
        }

        return null;
    }

    private static bool IsExecutionOutputConnected(DocRow row, DocColumn relationColumn)
    {
        string targetRowId = row.GetCell(relationColumn).StringValue ?? "";
        return !string.IsNullOrWhiteSpace(targetRowId);
    }

    private static bool HasIncomingExecutionConnections(
        DocProject project,
        DocTable nodeTable,
        GraphSchema schema,
        string targetNodeRowId)
    {
        if (string.IsNullOrWhiteSpace(targetNodeRowId))
        {
            return false;
        }

        if (schema.ExecutionOutputColumn != null)
        {
            for (int rowIndex = 0; rowIndex < nodeTable.Rows.Count; rowIndex++)
            {
                DocRow sourceRow = nodeTable.Rows[rowIndex];
                if (IsExecutionOutputConnected(sourceRow, schema.ExecutionOutputColumn) &&
                    string.Equals(sourceRow.GetCell(schema.ExecutionOutputColumn).StringValue ?? "", targetNodeRowId, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        for (int columnIndex = 0; columnIndex < nodeTable.Columns.Count; columnIndex++)
        {
            DocColumn subtableColumn = nodeTable.Columns[columnIndex];
            if (subtableColumn.Kind != DocColumnKind.Subtable)
            {
                continue;
            }

            if (schema.EdgeSubtableColumn != null &&
                string.Equals(subtableColumn.Id, schema.EdgeSubtableColumn.Id, StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryResolveSubtableChildTable(project, subtableColumn, out DocTable? childTable))
            {
                continue;
            }

            DocColumn? relationColumn = ResolveSubtableRelationPinColumn(childTable);
            if (relationColumn == null)
            {
                continue;
            }

            for (int childRowIndex = 0; childRowIndex < childTable.Rows.Count; childRowIndex++)
            {
                DocRow childRow = childTable.Rows[childRowIndex];
                string relationTargetRowId = childRow.GetCell(relationColumn).StringValue ?? "";
                if (string.Equals(relationTargetRowId, targetNodeRowId, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasSubtableExecutionOutputColumns(
        DocProject project,
        List<DocColumn> settingColumns)
    {
        for (int settingIndex = 0; settingIndex < settingColumns.Count; settingIndex++)
        {
            DocColumn settingColumn = settingColumns[settingIndex];
            if (settingColumn.Kind != DocColumnKind.Subtable)
            {
                continue;
            }

            if (!TryResolveSubtableChildTable(project, settingColumn, out DocTable? childTable))
            {
                continue;
            }

            if (ResolveSubtableRelationPinColumn(childTable) != null)
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildSubtableExecutionPinId(
        string subtableColumnId,
        string childTableId,
        string childRowId,
        string childColumnId)
    {
        return SubtableExecutionPinPrefix +
               subtableColumnId + "|" +
               childTableId + "|" +
               childRowId + "|" +
               childColumnId;
    }

    private static string GetNodeSubtableDisplayModeLabel(NodeSubtableDisplayMode displayMode)
    {
        return displayMode switch
        {
            NodeSubtableDisplayMode.Board => "Board",
            NodeSubtableDisplayMode.Calendar => "Calendar",
            NodeSubtableDisplayMode.Chart => "Chart",
            NodeSubtableDisplayMode.Custom => "Plugin",
            _ => "Grid",
        };
    }

    private static NodeSubtableDisplayMode ResolveNodeSubtableDisplayMode(string? rendererId, out string? customRendererId)
    {
        customRendererId = null;
        if (string.IsNullOrWhiteSpace(rendererId))
        {
            return NodeSubtableDisplayMode.Grid;
        }

        string normalizedRendererId = rendererId.Trim();
        if (normalizedRendererId.StartsWith(SubtableDisplayCustomRendererPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string strippedRendererId = normalizedRendererId[SubtableDisplayCustomRendererPrefix.Length..].Trim();
            if (strippedRendererId.Length == 0)
            {
                return NodeSubtableDisplayMode.Grid;
            }

            customRendererId = strippedRendererId;
            return NodeSubtableDisplayMode.Custom;
        }

        if (string.Equals(normalizedRendererId, SubtableDisplayRendererBoard, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedRendererId, "board", StringComparison.OrdinalIgnoreCase))
        {
            return NodeSubtableDisplayMode.Board;
        }

        if (string.Equals(normalizedRendererId, SubtableDisplayRendererCalendar, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedRendererId, "calendar", StringComparison.OrdinalIgnoreCase))
        {
            return NodeSubtableDisplayMode.Calendar;
        }

        if (string.Equals(normalizedRendererId, SubtableDisplayRendererChart, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedRendererId, "chart", StringComparison.OrdinalIgnoreCase))
        {
            return NodeSubtableDisplayMode.Chart;
        }

        if (string.Equals(normalizedRendererId, SubtableDisplayRendererGrid, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedRendererId, "grid", StringComparison.OrdinalIgnoreCase))
        {
            return NodeSubtableDisplayMode.Grid;
        }

        customRendererId = normalizedRendererId;
        return NodeSubtableDisplayMode.Custom;
    }

    private static bool TryDrawSubtableRendererPreviewForNode(
        DocWorkspace workspace,
        DocTable childTable,
        DocColumn subtableColumn,
        DocRow parentRow,
        float x,
        float y,
        float width,
        float maxContentBottom,
        NodeSubtableDisplayMode displayMode,
        string? customRendererId)
    {
        if (displayMode == NodeSubtableDisplayMode.Grid)
        {
            return false;
        }

        float previewX = x + 3f;
        float previewY = y + NodeSubtableSectionHeaderHeight + 2f;
        float previewWidth = MathF.Max(8f, width - 6f);
        float previewHeight = MathF.Max(8f, maxContentBottom - previewY - 1f);
        var previewRect = new ImRect(previewX, previewY, previewWidth, previewHeight);
        if (previewRect.Width < 8f || previewRect.Height < 8f)
        {
            return false;
        }

        Im.PushClipRect(previewRect);
        try
        {
            if (displayMode == NodeSubtableDisplayMode.Board)
            {
                if (!TryResolveNodeSubtablePreviewView(childTable, DocViewType.Board, customRendererId: null, out DocView boardView))
                {
                    return false;
                }

                BoardRenderer.Draw(
                    workspace,
                    childTable,
                    boardView,
                    previewRect,
                    interactive: false,
                    parentRowColumnId: childTable.ParentRowColumnId,
                    parentRowId: parentRow.Id);
                return true;
            }

            if (displayMode == NodeSubtableDisplayMode.Calendar)
            {
                if (!TryResolveNodeSubtablePreviewView(childTable, DocViewType.Calendar, customRendererId: null, out DocView calendarView))
                {
                    return false;
                }

                CalendarRenderer.Draw(
                    workspace,
                    childTable,
                    calendarView,
                    previewRect,
                    interactive: false,
                    parentRowColumnId: childTable.ParentRowColumnId,
                    parentRowId: parentRow.Id);
                return true;
            }

            if (displayMode == NodeSubtableDisplayMode.Chart)
            {
                if (!TryResolveNodeSubtablePreviewView(childTable, DocViewType.Chart, customRendererId: null, out DocView chartView))
                {
                    return false;
                }

                ChartRenderer.Draw(
                    workspace,
                    childTable,
                    chartView,
                    previewRect,
                    parentRowColumnId: childTable.ParentRowColumnId,
                    parentRowId: parentRow.Id);
                return true;
            }

            if (displayMode == NodeSubtableDisplayMode.Custom &&
                !string.IsNullOrWhiteSpace(customRendererId) &&
                TableViewRendererRegistry.TryGet(customRendererId, out IDerpDocTableViewRenderer customRenderer) &&
                TryResolveNodeSubtablePreviewView(childTable, DocViewType.Custom, customRendererId, out DocView customView))
            {
                string stateKey = "node_subtable_preview_" + childTable.Id + "_" + parentRow.Id + "_" + subtableColumn.Id;
                if (customRenderer is IDerpDocSubtableDisplayRenderer subtableDisplayRenderer &&
                    subtableDisplayRenderer.DrawSubtableDisplayPreview(
                        workspace,
                        childTable,
                        customView,
                        subtableColumn,
                        subtableColumn.PluginSettingsJson,
                        previewRect,
                        interactive: false,
                        stateKey: stateKey))
                {
                    return true;
                }

                if (customRenderer.DrawEmbedded(
                        workspace,
                        childTable,
                        customView,
                        previewRect,
                        interactive: false,
                        stateKey))
                {
                    return true;
                }

                customRenderer.Draw(workspace, childTable, customView, previewRect);
                return true;
            }

            return false;
        }
        finally
        {
            Im.PopClipRect();
        }
    }

    private static bool TryResolveNodeSubtablePreviewView(
        DocTable childTable,
        DocViewType viewType,
        string? customRendererId,
        out DocView view)
    {
        for (int viewIndex = 0; viewIndex < childTable.Views.Count; viewIndex++)
        {
            DocView candidateView = childTable.Views[viewIndex];
            if (candidateView.Type != viewType)
            {
                continue;
            }

            if (viewType != DocViewType.Custom)
            {
                view = candidateView;
                return true;
            }

            if (string.Equals(candidateView.CustomRendererId, customRendererId, StringComparison.Ordinal))
            {
                view = candidateView;
                return true;
            }
        }

        if (viewType == DocViewType.Board)
        {
            view = NodeBoardSubtableFallbackView;
            return true;
        }

        if (viewType == DocViewType.Calendar)
        {
            view = NodeCalendarSubtableFallbackView;
            return true;
        }

        if (viewType == DocViewType.Chart)
        {
            view = NodeChartSubtableFallbackView;
            return true;
        }

        if (viewType == DocViewType.Custom &&
            !string.IsNullOrWhiteSpace(customRendererId))
        {
            if (!NodeCustomSubtablePreviewViewsByRendererId.TryGetValue(customRendererId, out DocView? customFallbackView))
            {
                customFallbackView = new DocView
                {
                    Id = "__node_subtable_custom_preview_" + customRendererId,
                    Name = "Node subtable custom",
                    Type = DocViewType.Custom,
                    CustomRendererId = customRendererId,
                };
                NodeCustomSubtablePreviewViewsByRendererId[customRendererId] = customFallbackView;
            }

            view = customFallbackView;
            return true;
        }

        view = null!;
        return false;
    }

    private static string FormatSubtableRowLineForMode(
        DocTable childTable,
        DocRow childRow,
        int rowIndex,
        NodeSubtableDisplayMode displayMode)
    {
        string label = ResolveSubtableRowLabel(childTable, childRow);
        switch (displayMode)
        {
            case NodeSubtableDisplayMode.Board:
                {
                    string lane = ResolveSubtableBoardLaneLabel(childTable, childRow);
                    return "[" + lane + "] " + label;
                }
            case NodeSubtableDisplayMode.Calendar:
                {
                    return (rowIndex + 1).ToString(CultureInfo.InvariantCulture) + ". " + label;
                }
            case NodeSubtableDisplayMode.Chart:
                {
                    if (TryResolveFirstNumericColumn(childTable, out DocColumn? numericColumn))
                    {
                        double numericValue = childRow.GetCell(numericColumn).NumberValue;
                        return numericValue.ToString("G", CultureInfo.InvariantCulture) + " | " + label;
                    }

                    return "n/a | " + label;
                }
            default:
                {
                    return "• " + label;
                }
        }
    }

    private static string ResolveSubtableBoardLaneLabel(DocTable childTable, DocRow childRow)
    {
        for (int columnIndex = 0; columnIndex < childTable.Columns.Count; columnIndex++)
        {
            DocColumn column = childTable.Columns[columnIndex];
            if (column.IsHidden || IsParentRowColumn(childTable, column))
            {
                continue;
            }

            if (column.Kind != DocColumnKind.Select &&
                column.Kind != DocColumnKind.Relation &&
                column.Kind != DocColumnKind.Checkbox)
            {
                continue;
            }

            string value = FormatCellValueForDisplay(column, childRow.GetCell(column));
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "Row";
    }

    private static bool TryResolveFirstNumericColumn(DocTable childTable, out DocColumn numericColumn)
    {
        numericColumn = null!;
        for (int columnIndex = 0; columnIndex < childTable.Columns.Count; columnIndex++)
        {
            DocColumn column = childTable.Columns[columnIndex];
            if (column.IsHidden || IsParentRowColumn(childTable, column))
            {
                continue;
            }

            if (column.Kind != DocColumnKind.Number && column.Kind != DocColumnKind.Formula)
            {
                continue;
            }

            numericColumn = column;
            return true;
        }

        return false;
    }

    private static bool IsParentRowColumn(DocTable childTable, DocColumn column)
    {
        return !string.IsNullOrWhiteSpace(childTable.ParentRowColumnId) &&
               string.Equals(column.Id, childTable.ParentRowColumnId, StringComparison.Ordinal);
    }

    private static string ResolveSubtableRowLabel(DocTable childTable, DocRow childRow)
    {
        for (int columnIndex = 0; columnIndex < childTable.Columns.Count; columnIndex++)
        {
            DocColumn column = childTable.Columns[columnIndex];
            if (column.IsHidden)
            {
                continue;
            }

            if (IsParentRowColumn(childTable, column))
            {
                continue;
            }

            string formattedValue = FormatCellValueForDisplay(column, childRow.GetCell(column));
            if (!string.IsNullOrWhiteSpace(formattedValue))
            {
                return formattedValue;
            }
        }

        return childRow.Id;
    }

    private static void DrawInlinePinEditor(
        DocWorkspace workspace,
        DocTable table,
        DocRow row,
        DocColumn column,
        NodeVisual nodeVisual,
        float rowY,
        float rowHeight,
        bool inputPin)
    {
        float nodeScale = nodeVisual.Scale;
        float leftPadding = NodeInlineHorizontalPadding * nodeScale;
        float rightPadding = NodeInlineHorizontalPadding * nodeScale;
        float desiredControlHeightUnits = ComputePinEditorControlHeightUnits(row, column, inputPin, out bool expandedTextEditor);
        float minimumControlHeightUnits = desiredControlHeightUnits;
        float controlX;
        float controlRight;

        if (expandedTextEditor)
        {
            controlX = nodeVisual.ScreenRect.X + leftPadding;
            controlRight = nodeVisual.ScreenRect.Right - rightPadding;
        }
        else if (inputPin)
        {
            controlX = nodeVisual.ScreenRect.X + (nodeVisual.ScreenRect.Width * NodeInputPinEditorLeftRatio) + (NodeInlineColumnGap * nodeScale);
            controlRight = nodeVisual.ScreenRect.Right - rightPadding;
        }
        else
        {
            controlX = nodeVisual.ScreenRect.X + leftPadding;
            controlRight = nodeVisual.ScreenRect.X + (nodeVisual.ScreenRect.Width * NodeOutputPinEditorRightRatio) - (NodeInlineColumnGap * nodeScale);
        }

        float controlWidth = controlRight - controlX;
        if (controlWidth <= 10f)
        {
            return;
        }

        BeginNodeEditorScaleScope(nodeVisual, out bool pushedScale, out float invScale, out float pivotX, out float pivotY);
        try
        {
            float localRowY = ToNodeLocalPosition(rowY, pivotY, invScale);
            float localRowHeight = ToNodeLocalLength(rowHeight, invScale);
            float localControlX = ToNodeLocalPosition(controlX, pivotX, invScale);
            float localControlWidth = ToNodeLocalLength(controlWidth, invScale);
            float localControlHeight;
            float localControlY;
            if (expandedTextEditor)
            {
                float labelHeightUnits = GetInlineLabelHeightUnits();
                float occupiedBeforeControl = NodeExpandedTextLabelTopPadding + labelHeightUnits + NodeExpandedTextLabelGap;
                localControlY = localRowY + occupiedBeforeControl;
                localControlHeight = MathF.Max(
                    minimumControlHeightUnits,
                    localRowHeight - occupiedBeforeControl - NodeExpandedTextBottomPadding);
            }
            else
            {
                localControlHeight = MathF.Max(minimumControlHeightUnits, localRowHeight - 2f);
                localControlY = localRowY + MathF.Max(0f, (localRowHeight - localControlHeight) * 0.5f);
            }
            string widgetId = "node_inline_pin_" + (inputPin ? "in" : "out") + "_" + table.Id + "_" + row.Id + "_" + column.Id;
            DrawInlineCellEditorControl(
                workspace,
                table,
                row,
                column,
                localControlX,
                localControlY,
                localControlWidth,
                localControlHeight,
                widgetId,
                allowWrappedTextArea: expandedTextEditor);
        }
        finally
        {
            EndNodeEditorScaleScope(pushedScale);
        }
    }

    private static void DrawPinValuePreview(
        NodeVisual nodeVisual,
        DocRow row,
        DocColumn column,
        bool inputPin,
        float pinCenterY,
        float fontSize,
        ImStyle style)
    {
        string previewValue = FormatCellValueForDisplay(column, row.GetCell(column));
        if (string.IsNullOrWhiteSpace(previewValue))
        {
            return;
        }

        float nodeScale = nodeVisual.Scale;
        float maxWidth;
        float textX;
        if (inputPin)
        {
            textX = nodeVisual.ScreenRect.X + (nodeVisual.ScreenRect.Width * NodeInputPinEditorLeftRatio) + (NodeInlineColumnGap * nodeScale);
            maxWidth = nodeVisual.ScreenRect.Right - textX - (NodeInlineHorizontalPadding * nodeScale);
        }
        else
        {
            textX = nodeVisual.ScreenRect.X + (NodeInlineHorizontalPadding * nodeScale);
            maxWidth = (nodeVisual.ScreenRect.Width * NodeOutputPinEditorRightRatio) - (NodeInlineHorizontalPadding * nodeScale) - (NodeInlineColumnGap * nodeScale);
        }

        string clippedText = ClipTextToWidth(previewValue, fontSize, maxWidth);
        if (string.IsNullOrWhiteSpace(clippedText))
        {
            return;
        }

        float textY = pinCenterY - fontSize * 0.5f;
        Im.Text(clippedText.AsSpan(), textX, textY, fontSize, style.TextSecondary);
    }

    private static string ClipTextToWidth(string value, float fontSize, float maxWidth)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        if (maxWidth <= 4f)
        {
            return "";
        }

        if (Im.MeasureTextWidth(value.AsSpan(), fontSize) <= maxWidth)
        {
            return value;
        }

        const string ellipsis = "...";
        float ellipsisWidth = Im.MeasureTextWidth(ellipsis.AsSpan(), fontSize);
        if (ellipsisWidth >= maxWidth)
        {
            return "";
        }

        int length = value.Length;
        while (length > 0)
        {
            length--;
            string candidate = value[..length] + ellipsis;
            if (Im.MeasureTextWidth(candidate.AsSpan(), fontSize) <= maxWidth)
            {
                return candidate;
            }
        }

        return "";
    }

    private static void DrawInlineCellEditorControl(
        DocWorkspace workspace,
        DocTable table,
        DocRow row,
        DocColumn column,
        float controlX,
        float controlY,
        float controlWidth,
        float controlHeight,
        string widgetId,
        bool allowWrappedTextArea)
    {
        DocCellValue currentCell = row.GetCell(column);

        if (column.Kind == DocColumnKind.Checkbox)
        {
            bool checkboxValue = currentCell.BoolValue;
            float checkboxSize = Im.Style.CheckboxSize;
            float checkboxY = controlY + MathF.Max(0f, (controlHeight - checkboxSize) * 0.5f);
            if (Im.Checkbox("##" + widgetId, ref checkboxValue, controlX, checkboxY))
            {
                SetNodeCellValue(workspace, table, row, column, DocCellValue.Bool(checkboxValue));
            }

            return;
        }

        if (column.Kind == DocColumnKind.Select && column.Options != null && column.Options.Count > 0)
        {
            int optionCount = BuildSelectOptions(column.Options, SelectOptionsScratch);
            if (optionCount <= 0)
            {
                return;
            }

            int selectedOptionIndex = 0;
            string currentText = currentCell.StringValue ?? "";
            for (int optionIndex = 0; optionIndex < optionCount; optionIndex++)
            {
                if (string.Equals(SelectOptionsScratch[optionIndex], currentText, StringComparison.OrdinalIgnoreCase))
                {
                    selectedOptionIndex = optionIndex;
                    break;
                }
            }

            if (Im.Dropdown(widgetId, SelectOptionsScratch.AsSpan(0, optionCount), ref selectedOptionIndex, controlX, controlY, controlWidth))
            {
                SetNodeCellValue(workspace, table, row, column, DocCellValue.Text(SelectOptionsScratch[selectedOptionIndex]));
            }

            return;
        }

        if (column.Kind == DocColumnKind.Number || column.Kind == DocColumnKind.Formula)
        {
            float numberValue = (float)currentCell.NumberValue;
            float min = column.NumberMin.HasValue ? (float)column.NumberMin.Value : -1000000f;
            float max = column.NumberMax.HasValue ? (float)column.NumberMax.Value : 1000000f;
            if (ImScalarInput.DrawAt(widgetId, controlX, controlY, controlWidth, ref numberValue, min, max, "F3"))
            {
                SetNodeCellValue(workspace, table, row, column, DocCellValue.Number(numberValue));
            }

            return;
        }

        if (column.Kind == DocColumnKind.Vec2)
        {
            var vectorValue = new Vector2((float)currentCell.XValue, (float)currentCell.YValue);
            if (ImVectorInput.DrawAt(widgetId, controlX, controlY, controlWidth, ref vectorValue))
            {
                SetNodeCellValue(workspace, table, row, column, DocCellValue.Vec2(vectorValue.X, vectorValue.Y));
            }

            return;
        }

        if (column.Kind == DocColumnKind.Vec3)
        {
            var vectorValue = new Vector3((float)currentCell.XValue, (float)currentCell.YValue, (float)currentCell.ZValue);
            if (ImVectorInput.DrawAt(widgetId, controlX, controlY, controlWidth, ref vectorValue))
            {
                SetNodeCellValue(workspace, table, row, column, DocCellValue.Vec3(vectorValue.X, vectorValue.Y, vectorValue.Z));
            }

            return;
        }

        if (column.Kind == DocColumnKind.Vec4)
        {
            var vectorValue = new Vector4((float)currentCell.XValue, (float)currentCell.YValue, (float)currentCell.ZValue, (float)currentCell.WValue);
            if (ImVectorInput.DrawAt(widgetId, controlX, controlY, controlWidth, ref vectorValue))
            {
                SetNodeCellValue(workspace, table, row, column, DocCellValue.Vec4(vectorValue.X, vectorValue.Y, vectorValue.Z, vectorValue.W));
            }

            return;
        }

        if (column.Kind == DocColumnKind.Color)
        {
            var colorValue = new Vector4((float)currentCell.XValue, (float)currentCell.YValue, (float)currentCell.ZValue, (float)currentCell.WValue);
            if (ImVectorInput.DrawAt(widgetId, controlX, controlY, controlWidth, ref colorValue))
            {
                SetNodeCellValue(workspace, table, row, column, DocCellValue.Color(colorValue.X, colorValue.Y, colorValue.Z, colorValue.W));
            }

            return;
        }

        string inlineTextKey = table.Id + ":" + row.Id + ":" + column.Id;
        InlineTextEditState inlineTextState = GetOrCreateInlineTextState(inlineTextKey);
        int widgetHash = Im.Context.GetId(widgetId);
        string currentStringValue = currentCell.StringValue ?? "";
        if (!string.Equals(inlineTextState.LastAppliedText, currentStringValue, StringComparison.Ordinal) &&
            !Im.Context.IsActive(widgetHash))
        {
            SetTextBuffer(inlineTextState.Buffer, ref inlineTextState.BufferLength, currentStringValue);
            inlineTextState.LastAppliedText = currentStringValue;
        }

        bool shouldUseWrappedTextArea = allowWrappedTextArea && controlHeight > 8f;
        if (shouldUseWrappedTextArea)
        {
            _ = MeasureInlineTextEditorHeightUnits(
                currentStringValue,
                controlWidth,
                minimumHeightUnits: 10f,
                out int visualLineCount);
            shouldUseWrappedTextArea = visualLineCount > 1;
        }

        bool textChanged;
        if (shouldUseWrappedTextArea)
        {
            textChanged = ImTextArea.DrawAt(
                widgetId,
                inlineTextState.Buffer,
                ref inlineTextState.BufferLength,
                inlineTextState.Buffer.Length,
                controlX,
                controlY,
                controlWidth,
                controlHeight,
                wordWrap: true);
        }
        else
        {
            textChanged = Im.TextInput(widgetId, inlineTextState.Buffer, ref inlineTextState.BufferLength, inlineTextState.Buffer.Length, controlX, controlY, controlWidth);
        }

        if (textChanged)
        {
            string updatedText = inlineTextState.BufferLength > 0
                ? new string(inlineTextState.Buffer, 0, inlineTextState.BufferLength)
                : "";
            inlineTextState.LastAppliedText = updatedText;
            SetNodeCellValue(workspace, table, row, column, DocCellValue.Text(updatedText));
        }
    }

    private static InlineTextEditState GetOrCreateInlineTextState(string key)
    {
        if (!InlineTextStateByCellKey.TryGetValue(key, out InlineTextEditState? state))
        {
            state = new InlineTextEditState();
            InlineTextStateByCellKey[key] = state;
        }

        return state;
    }

    private static int BuildSelectOptions(List<string> options, string[] destination)
    {
        int count = Math.Min(destination.Length, options.Count);
        int writeIndex = 0;
        for (int optionIndex = 0; optionIndex < count; optionIndex++)
        {
            string optionValue = options[optionIndex];
            if (string.IsNullOrWhiteSpace(optionValue))
            {
                continue;
            }

            destination[writeIndex++] = optionValue;
        }

        return writeIndex;
    }

    private static void SetNodeCellValue(
        DocWorkspace workspace,
        DocTable table,
        DocRow row,
        DocColumn column,
        DocCellValue candidateCellValue)
    {
        DocCellValue oldCellValue = row.GetCell(column);
        DocCellValue newCellValue = candidateCellValue.Clone();
        if (oldCellValue.HasCellFormulaExpression)
        {
            newCellValue = newCellValue.ClearCellFormulaExpression();
        }

        newCellValue.FormulaError = null;
        if (AreCellValuesEquivalent(oldCellValue, newCellValue, column.Kind))
        {
            return;
        }

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetCell,
            TableId = table.Id,
            RowId = row.Id,
            ColumnId = column.Id,
            OldCellValue = oldCellValue,
            NewCellValue = newCellValue,
        });
    }

    private static bool AreCellValuesEquivalent(DocCellValue left, DocCellValue right, DocColumnKind kind)
    {
        const double epsilon = 0.000001;
        switch (kind)
        {
            case DocColumnKind.Number:
            case DocColumnKind.Formula:
                return Math.Abs(left.NumberValue - right.NumberValue) < epsilon &&
                       string.Equals(left.StringValue ?? "", right.StringValue ?? "", StringComparison.Ordinal);
            case DocColumnKind.Checkbox:
                return left.BoolValue == right.BoolValue;
            case DocColumnKind.Vec2:
                return Math.Abs(left.XValue - right.XValue) < epsilon &&
                       Math.Abs(left.YValue - right.YValue) < epsilon;
            case DocColumnKind.Vec3:
                return Math.Abs(left.XValue - right.XValue) < epsilon &&
                       Math.Abs(left.YValue - right.YValue) < epsilon &&
                       Math.Abs(left.ZValue - right.ZValue) < epsilon;
            case DocColumnKind.Vec4:
            case DocColumnKind.Color:
                return Math.Abs(left.XValue - right.XValue) < epsilon &&
                       Math.Abs(left.YValue - right.YValue) < epsilon &&
                       Math.Abs(left.ZValue - right.ZValue) < epsilon &&
                       Math.Abs(left.WValue - right.WValue) < epsilon;
            default:
                return string.Equals(left.StringValue ?? "", right.StringValue ?? "", StringComparison.Ordinal);
        }
    }

    private static bool TryScaffoldSchema(
        DocWorkspace workspace,
        DocTable table,
        DocView view,
        NodeGraphViewSettings settings,
        out string statusMessage)
    {
        statusMessage = "";
        var commands = new List<DocCommand>(16);
        GraphSchema schema = ResolveGraphSchema(workspace.Project, table, settings);
        DocColumn? typeColumn = schema.TypeColumn;
        DocColumn? positionColumn = schema.PositionColumn;
        DocColumn? titleColumn = schema.TitleColumn;
        DocColumn? executionOutputColumn = schema.ExecutionOutputColumn;
        DocColumn? edgeSubtableColumn = schema.EdgeSubtableColumn;
        DocTable? edgeTable = schema.EdgeTable;
        int nextNodeColumnIndex = table.Columns.Count;

        if (typeColumn == null)
        {
            typeColumn = new DocColumn
            {
                Id = Guid.NewGuid().ToString(),
                Name = DefaultTypeColumnName,
                Kind = DocColumnKind.Select,
                Width = 140f,
                Options = new List<string> { DefaultTypeOption },
            };
            commands.Add(new DocCommand
            {
                Kind = DocCommandKind.AddColumn,
                TableId = table.Id,
                ColumnIndex = nextNodeColumnIndex,
                ColumnSnapshot = typeColumn,
            });
            nextNodeColumnIndex++;
        }
        else if (typeColumn.Options == null || typeColumn.Options.Count <= 0)
        {
            commands.Add(new DocCommand
            {
                Kind = DocCommandKind.SetColumnOptions,
                TableId = table.Id,
                ColumnId = typeColumn.Id,
                OldOptionsSnapshot = CloneOptions(typeColumn.Options),
                NewOptionsSnapshot = new List<string> { DefaultTypeOption },
            });
        }

        if (positionColumn == null)
        {
            positionColumn = new DocColumn
            {
                Id = Guid.NewGuid().ToString(),
                Name = DefaultPositionColumnName,
                Kind = DocColumnKind.Vec2,
                ColumnTypeId = DocColumnTypeIds.Vec2Fixed64,
                Width = 130f,
            };
            commands.Add(new DocCommand
            {
                Kind = DocCommandKind.AddColumn,
                TableId = table.Id,
                ColumnIndex = nextNodeColumnIndex,
                ColumnSnapshot = positionColumn,
            });
            nextNodeColumnIndex++;
        }

        if (titleColumn == null)
        {
            titleColumn = new DocColumn
            {
                Id = Guid.NewGuid().ToString(),
                Name = DefaultTitleColumnName,
                Kind = DocColumnKind.Text,
                Width = 180f,
            };
            commands.Add(new DocCommand
            {
                Kind = DocCommandKind.AddColumn,
                TableId = table.Id,
                ColumnIndex = nextNodeColumnIndex,
                ColumnSnapshot = titleColumn,
            });
            nextNodeColumnIndex++;
        }

        if (executionOutputColumn == null)
        {
            executionOutputColumn = new DocColumn
            {
                Id = Guid.NewGuid().ToString(),
                Name = DefaultExecutionNextColumnName,
                Kind = DocColumnKind.Relation,
                RelationTableId = table.Id,
                RelationTargetMode = DocRelationTargetMode.SelfTable,
                RelationDisplayColumnId = titleColumn?.Id ?? typeColumn?.Id,
                Width = 130f,
                IsHidden = true,
            };
            commands.Add(new DocCommand
            {
                Kind = DocCommandKind.AddColumn,
                TableId = table.Id,
                ColumnIndex = nextNodeColumnIndex,
                ColumnSnapshot = executionOutputColumn,
            });
            nextNodeColumnIndex++;
        }

        if (edgeTable == null)
        {
            edgeTable = CreateEdgeTableScaffold(table, titleColumn, typeColumn);
            commands.Add(new DocCommand
            {
                Kind = DocCommandKind.AddTable,
                TableIndex = workspace.Project.Tables.Count,
                TableSnapshot = edgeTable,
            });
        }

        if (edgeSubtableColumn == null)
        {
            edgeSubtableColumn = new DocColumn
            {
                Id = Guid.NewGuid().ToString(),
                Name = DefaultEdgesColumnName,
                Kind = DocColumnKind.Subtable,
                SubtableId = edgeTable.Id,
                Width = 120f,
            };
            commands.Add(new DocCommand
            {
                Kind = DocCommandKind.AddColumn,
                TableId = table.Id,
                ColumnIndex = nextNodeColumnIndex,
                ColumnSnapshot = edgeSubtableColumn,
            });
            nextNodeColumnIndex++;
        }

        EnsureEdgeTableColumns(edgeTable, table, titleColumn, typeColumn, commands);

        if (commands.Count <= 0)
        {
            statusMessage = "Node graph schema already up to date.";
            return false;
        }

        workspace.ExecuteCommands(commands);
        DocTable? updatedTable = FindTableById(workspace.Project, table.Id);
        DocTable? updatedEdgeTable = FindTableById(workspace.Project, edgeTable.Id);
        if (updatedTable == null || updatedEdgeTable == null)
        {
            statusMessage = "Node graph scaffold applied with warnings.";
            return true;
        }

        GraphSchema updatedSchema = ResolveGraphSchema(workspace.Project, updatedTable, settings);
        InferSettingsFromSchema(settings, updatedSchema);
        EnsureTypeLayoutsContainActiveSchemaColumns(updatedTable, settings, updatedSchema);
        SaveViewSettings(workspace, updatedTable, view, settings);
        statusMessage = "Node graph schema scaffolded.";
        return true;
    }

    private static void EnsureEdgeTableColumns(
        DocTable edgeTable,
        DocTable nodeTable,
        DocColumn? titleColumn,
        DocColumn? typeColumn,
        List<DocCommand> commands)
    {
        int nextEdgeColumnIndex = edgeTable.Columns.Count;
        DocColumn? parentRowColumn = FindColumnByNameAndKind(edgeTable, DefaultParentRowColumnName, DocColumnKind.Text);
        if (parentRowColumn == null)
        {
            parentRowColumn = new DocColumn
            {
                Id = Guid.NewGuid().ToString(),
                Name = DefaultParentRowColumnName,
                Kind = DocColumnKind.Text,
                IsHidden = true,
                Width = 120f,
            };
            commands.Add(new DocCommand
            {
                Kind = DocCommandKind.AddColumn,
                TableId = edgeTable.Id,
                ColumnIndex = nextEdgeColumnIndex,
                ColumnSnapshot = parentRowColumn,
            });
            nextEdgeColumnIndex++;
        }

        DocColumn? fromNodeColumn = FindColumnByNameAndKind(edgeTable, DefaultFromNodeColumnName, DocColumnKind.Relation);
        if (fromNodeColumn == null)
        {
            fromNodeColumn = CreateEdgeRelationColumn(DefaultFromNodeColumnName, nodeTable, titleColumn, typeColumn);
            commands.Add(new DocCommand
            {
                Kind = DocCommandKind.AddColumn,
                TableId = edgeTable.Id,
                ColumnIndex = nextEdgeColumnIndex,
                ColumnSnapshot = fromNodeColumn,
            });
            nextEdgeColumnIndex++;
        }

        DocColumn? fromPinColumn = FindColumnByNameAndKind(edgeTable, DefaultFromPinColumnName, DocColumnKind.Text);
        if (fromPinColumn == null)
        {
            fromPinColumn = new DocColumn
            {
                Id = Guid.NewGuid().ToString(),
                Name = DefaultFromPinColumnName,
                Kind = DocColumnKind.Text,
                Width = 110f,
            };
            commands.Add(new DocCommand
            {
                Kind = DocCommandKind.AddColumn,
                TableId = edgeTable.Id,
                ColumnIndex = nextEdgeColumnIndex,
                ColumnSnapshot = fromPinColumn,
            });
            nextEdgeColumnIndex++;
        }

        DocColumn? toNodeColumn = FindColumnByNameAndKind(edgeTable, DefaultToNodeColumnName, DocColumnKind.Relation);
        if (toNodeColumn == null)
        {
            toNodeColumn = CreateEdgeRelationColumn(DefaultToNodeColumnName, nodeTable, titleColumn, typeColumn);
            commands.Add(new DocCommand
            {
                Kind = DocCommandKind.AddColumn,
                TableId = edgeTable.Id,
                ColumnIndex = nextEdgeColumnIndex,
                ColumnSnapshot = toNodeColumn,
            });
            nextEdgeColumnIndex++;
        }

        DocColumn? toPinColumn = FindColumnByNameAndKind(edgeTable, DefaultToPinColumnName, DocColumnKind.Text);
        if (toPinColumn == null)
        {
            toPinColumn = new DocColumn
            {
                Id = Guid.NewGuid().ToString(),
                Name = DefaultToPinColumnName,
                Kind = DocColumnKind.Text,
                Width = 110f,
            };
            commands.Add(new DocCommand
            {
                Kind = DocCommandKind.AddColumn,
                TableId = edgeTable.Id,
                ColumnIndex = nextEdgeColumnIndex,
                ColumnSnapshot = toPinColumn,
            });
        }
    }

    private static DocColumn CreateEdgeRelationColumn(
        string name,
        DocTable nodeTable,
        DocColumn? titleColumn,
        DocColumn? typeColumn)
    {
        return new DocColumn
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Kind = DocColumnKind.Relation,
            RelationTableId = nodeTable.Id,
            RelationTargetMode = DocRelationTargetMode.SelfTable,
            RelationDisplayColumnId = titleColumn?.Id ?? typeColumn?.Id,
            Width = 130f,
        };
    }

    private static DocTable CreateEdgeTableScaffold(DocTable nodeTable, DocColumn? titleColumn, DocColumn? typeColumn)
    {
        string tableName = nodeTable.Name + " Edges";
        string tableFileName = CreateSafeFileName(nodeTable.FileName + "_edges");
        var edgeTable = new DocTable
        {
            Id = Guid.NewGuid().ToString(),
            Name = tableName,
            FileName = tableFileName,
            ParentTableId = nodeTable.Id,
            ParentRowColumnId = "",
        };

        var parentRowColumn = new DocColumn
        {
            Id = Guid.NewGuid().ToString(),
            Name = DefaultParentRowColumnName,
            Kind = DocColumnKind.Text,
            IsHidden = true,
            Width = 120f,
        };
        edgeTable.ParentRowColumnId = parentRowColumn.Id;
        edgeTable.Columns.Add(parentRowColumn);
        edgeTable.Columns.Add(CreateEdgeRelationColumn(DefaultFromNodeColumnName, nodeTable, titleColumn, typeColumn));
        edgeTable.Columns.Add(new DocColumn
        {
            Id = Guid.NewGuid().ToString(),
            Name = DefaultFromPinColumnName,
            Kind = DocColumnKind.Text,
            Width = 110f,
        });
        edgeTable.Columns.Add(CreateEdgeRelationColumn(DefaultToNodeColumnName, nodeTable, titleColumn, typeColumn));
        edgeTable.Columns.Add(new DocColumn
        {
            Id = Guid.NewGuid().ToString(),
            Name = DefaultToPinColumnName,
            Kind = DocColumnKind.Text,
            Width = 110f,
        });
        return edgeTable;
    }

    private static List<string>? CloneOptions(List<string>? sourceOptions)
    {
        if (sourceOptions == null || sourceOptions.Count <= 0)
        {
            return null;
        }

        var clone = new List<string>(sourceOptions.Count);
        for (int optionIndex = 0; optionIndex < sourceOptions.Count; optionIndex++)
        {
            clone.Add(sourceOptions[optionIndex]);
        }

        return clone;
    }

    private static string CreateSafeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "table";
        }

        Span<char> buffer = stackalloc char[value.Length];
        int length = 0;
        for (int charIndex = 0; charIndex < value.Length; charIndex++)
        {
            char character = char.ToLowerInvariant(value[charIndex]);
            bool isLetter = character >= 'a' && character <= 'z';
            bool isDigit = character >= '0' && character <= '9';
            if (isLetter || isDigit)
            {
                buffer[length++] = character;
                continue;
            }

            if (character == '_' || character == '-')
            {
                buffer[length++] = character;
                continue;
            }

            if (character == ' ')
            {
                buffer[length++] = '_';
            }
        }

        if (length <= 0)
        {
            return "table";
        }

        return new string(buffer[..length]);
    }

    private static string GetSelectedTypeName(
        string viewKey,
        NodeGraphViewSettings settings,
        DocTable table,
        GraphSchema schema)
    {
        if (SelectedTypeNameByViewKey.TryGetValue(viewKey, out string? selectedTypeName) &&
            !string.IsNullOrWhiteSpace(selectedTypeName))
        {
            return selectedTypeName;
        }

        int typeCount = BuildTypeNameOptions(table, schema.TypeColumn, TypeNameOptionsScratch);
        if (typeCount <= 0)
        {
            return DefaultTypeOption;
        }

        string fallbackTypeName = TypeNameOptionsScratch[0];
        if (settings.TypeLayouts.Count > 0 && !string.IsNullOrWhiteSpace(settings.TypeLayouts[0].TypeName))
        {
            fallbackTypeName = settings.TypeLayouts[0].TypeName;
        }

        SelectedTypeNameByViewKey[viewKey] = fallbackTypeName;
        return fallbackTypeName;
    }

    private static int BuildTypeNameOptions(DocTable table, DocColumn? typeColumn, string[] destination)
    {
        TypeNameListScratch.Clear();
        TypeNameSetScratch.Clear();

        AddTypeNameOption(TypeNameListScratch, TypeNameSetScratch, DefaultTypeOption);
        if (typeColumn != null && typeColumn.Options != null)
        {
            for (int optionIndex = 0; optionIndex < typeColumn.Options.Count; optionIndex++)
            {
                AddTypeNameOption(TypeNameListScratch, TypeNameSetScratch, typeColumn.Options[optionIndex]);
            }
        }

        if (typeColumn != null)
        {
            for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                string typeName = table.Rows[rowIndex].GetCell(typeColumn).StringValue ?? "";
                AddTypeNameOption(TypeNameListScratch, TypeNameSetScratch, typeName);
            }
        }

        int count = Math.Min(destination.Length, TypeNameListScratch.Count);
        for (int optionIndex = 0; optionIndex < count; optionIndex++)
        {
            destination[optionIndex] = TypeNameListScratch[optionIndex];
        }

        return count;
    }

    private static void AddTypeNameOption(List<string> destinationList, HashSet<string> destinationSet, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        string normalizedValue = value.Trim();
        if (!destinationSet.Add(normalizedValue))
        {
            return;
        }

        destinationList.Add(normalizedValue);
    }

    private static int FindTypeNameOptionIndex(string[] options, int optionCount, string value)
    {
        for (int optionIndex = 0; optionIndex < optionCount; optionIndex++)
        {
            if (string.Equals(options[optionIndex], value, StringComparison.OrdinalIgnoreCase))
            {
                return optionIndex;
            }
        }

        return 0;
    }

    private static bool EnsureTypeLayoutsContainActiveSchemaColumns(DocTable table, NodeGraphViewSettings settings, GraphSchema schema)
    {
        bool changed = false;
        int optionCount = BuildTypeNameOptions(table, schema.TypeColumn, TypeNameOptionsScratch);
        for (int optionIndex = 0; optionIndex < optionCount; optionIndex++)
        {
            NodeTypeLayout typeLayout = GetOrCreateTypeLayout(settings, TypeNameOptionsScratch[optionIndex]);
            changed |= EnsureLayoutFieldsContainSchemaColumns(table, schema, typeLayout);
        }

        return changed;
    }

    private static bool EnsureLayoutFieldsContainSchemaColumns(DocTable table, GraphSchema schema, NodeTypeLayout typeLayout)
    {
        bool changed = false;
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            DocColumn column = table.Columns[columnIndex];
            if (IsReservedNodeColumn(column, schema))
            {
                continue;
            }

            if (FindLayoutField(typeLayout, column.Id) >= 0)
            {
                continue;
            }

            typeLayout.Fields.Add(new NodeFieldLayout
            {
                ColumnId = column.Id,
                Mode = NodeFieldDisplayMode.Setting.ToString(),
            });
            changed = true;
        }

        return changed;
    }

    private static bool IsReservedNodeColumn(DocColumn column, GraphSchema schema)
    {
        return string.Equals(column.Id, schema.TypeColumn?.Id, StringComparison.Ordinal) ||
               string.Equals(column.Id, schema.PositionColumn?.Id, StringComparison.Ordinal) ||
               string.Equals(column.Id, schema.TitleColumn?.Id, StringComparison.Ordinal) ||
               string.Equals(column.Id, schema.ExecutionOutputColumn?.Id, StringComparison.Ordinal) ||
               string.Equals(column.Id, schema.EdgeSubtableColumn?.Id, StringComparison.Ordinal);
    }

    private static NodeTypeLayout GetOrCreateTypeLayout(NodeGraphViewSettings settings, string typeName)
    {
        for (int typeLayoutIndex = 0; typeLayoutIndex < settings.TypeLayouts.Count; typeLayoutIndex++)
        {
            NodeTypeLayout typeLayout = settings.TypeLayouts[typeLayoutIndex];
            if (string.Equals(typeLayout.TypeName, typeName, StringComparison.OrdinalIgnoreCase))
            {
                return typeLayout;
            }
        }

        var createdTypeLayout = new NodeTypeLayout
        {
            TypeName = typeName,
            Fields = new List<NodeFieldLayout>(),
        };
        settings.TypeLayouts.Add(createdTypeLayout);
        return createdTypeLayout;
    }

    private static NodeTypeLayout? FindTypeLayout(NodeGraphViewSettings settings, string typeName)
    {
        for (int typeLayoutIndex = 0; typeLayoutIndex < settings.TypeLayouts.Count; typeLayoutIndex++)
        {
            NodeTypeLayout typeLayout = settings.TypeLayouts[typeLayoutIndex];
            if (string.Equals(typeLayout.TypeName, typeName, StringComparison.OrdinalIgnoreCase))
            {
                return typeLayout;
            }
        }

        return null;
    }

    private static NodeFieldDisplayMode GetFieldDisplayMode(NodeTypeLayout typeLayout, string columnId)
    {
        int fieldIndex = FindLayoutField(typeLayout, columnId);
        if (fieldIndex < 0)
        {
            return NodeFieldDisplayMode.Hidden;
        }

        return ParseFieldDisplayMode(typeLayout.Fields[fieldIndex].Mode);
    }

    private static void SetFieldDisplayMode(NodeTypeLayout typeLayout, string columnId, NodeFieldDisplayMode mode)
    {
        int fieldIndex = FindLayoutField(typeLayout, columnId);
        if (fieldIndex >= 0)
        {
            typeLayout.Fields[fieldIndex].Mode = mode.ToString();
            return;
        }

        typeLayout.Fields.Add(new NodeFieldLayout
        {
            ColumnId = columnId,
            Mode = mode.ToString(),
        });
    }

    private static int FindLayoutField(NodeTypeLayout typeLayout, string columnId)
    {
        for (int fieldIndex = 0; fieldIndex < typeLayout.Fields.Count; fieldIndex++)
        {
            if (string.Equals(typeLayout.Fields[fieldIndex].ColumnId, columnId, StringComparison.Ordinal))
            {
                return fieldIndex;
            }
        }

        return -1;
    }

    private static NodeFieldDisplayMode ParseFieldDisplayMode(string? modeValue)
    {
        if (Enum.TryParse<NodeFieldDisplayMode>(modeValue, ignoreCase: true, out NodeFieldDisplayMode parsedMode))
        {
            return parsedMode;
        }

        return NodeFieldDisplayMode.Hidden;
    }

    private static string ResolveNodeType(DocRow row, GraphSchema schema)
    {
        if (schema.TypeColumn == null)
        {
            return DefaultTypeOption;
        }

        string typeName = row.GetCell(schema.TypeColumn).StringValue ?? "";
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return DefaultTypeOption;
        }

        return typeName.Trim();
    }

    private static string ResolveNodeTitle(DocRow row, GraphSchema schema, string fallback)
    {
        if (schema.TitleColumn != null)
        {
            string title = row.GetCell(schema.TitleColumn).StringValue ?? "";
            if (!string.IsNullOrWhiteSpace(title))
            {
                return title.Trim();
            }
        }

        return fallback;
    }

    private static string FormatCellValueForDisplay(DocColumn column, DocCellValue cellValue)
    {
        switch (column.Kind)
        {
            case DocColumnKind.Number:
            case DocColumnKind.Formula:
                return cellValue.NumberValue.ToString("G", CultureInfo.InvariantCulture);
            case DocColumnKind.Checkbox:
                return cellValue.BoolValue ? "true" : "false";
            case DocColumnKind.Vec2:
                return "(" + cellValue.XValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                       cellValue.YValue.ToString("G", CultureInfo.InvariantCulture) + ")";
            case DocColumnKind.Vec3:
                return "(" + cellValue.XValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                       cellValue.YValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                       cellValue.ZValue.ToString("G", CultureInfo.InvariantCulture) + ")";
            case DocColumnKind.Vec4:
                return "(" + cellValue.XValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                       cellValue.YValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                       cellValue.ZValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                       cellValue.WValue.ToString("G", CultureInfo.InvariantCulture) + ")";
            case DocColumnKind.Color:
                return "rgba(" +
                       cellValue.XValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                       cellValue.YValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                       cellValue.ZValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                       cellValue.WValue.ToString("G", CultureInfo.InvariantCulture) + ")";
            default:
                return cellValue.StringValue ?? "";
        }
    }

    private static void SetTextBuffer(char[] destinationBuffer, ref int destinationLength, string value)
    {
        ReadOnlySpan<char> sourceText = value.AsSpan();
        int copyLength = Math.Min(sourceText.Length, destinationBuffer.Length);
        sourceText[..copyLength].CopyTo(destinationBuffer);
        destinationLength = copyLength;
    }

    private static DocTable? FindTableById(DocProject project, string tableId)
    {
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            DocTable table = project.Tables[tableIndex];
            if (string.Equals(table.Id, tableId, StringComparison.Ordinal))
            {
                return table;
            }
        }

        return null;
    }

    private static DocRow? FindRowById(DocTable table, string rowId)
    {
        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            DocRow row = table.Rows[rowIndex];
            if (string.Equals(row.Id, rowId, StringComparison.Ordinal))
            {
                return row;
            }
        }

        return null;
    }

    private static bool TryGetSelectedNodeRow(DocTable table, int selectedRowIndex, out DocRow row)
    {
        if (selectedRowIndex >= 0 && selectedRowIndex < table.Rows.Count)
        {
            row = table.Rows[selectedRowIndex];
            return true;
        }

        row = default!;
        return false;
    }

    private static int FindRowIndexById(DocTable table, string rowId)
    {
        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            if (string.Equals(table.Rows[rowIndex].Id, rowId, StringComparison.Ordinal))
            {
                return rowIndex;
            }
        }

        return -1;
    }

    private static DocRow CloneRowSnapshot(DocRow sourceRow)
    {
        var clone = new DocRow
        {
            Id = sourceRow.Id,
            Cells = new Dictionary<string, DocCellValue>(sourceRow.Cells.Count),
        };

        foreach (var cellEntry in sourceRow.Cells)
        {
            clone.Cells[cellEntry.Key] = cellEntry.Value.Clone();
        }

        return clone;
    }

    private static DocColumn? FindColumnById(DocTable table, string columnId)
    {
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            DocColumn column = table.Columns[columnIndex];
            if (string.Equals(column.Id, columnId, StringComparison.Ordinal))
            {
                return column;
            }
        }

        return null;
    }

    private static DocColumn? FindColumnByNameAndKind(DocTable table, string name, DocColumnKind kind)
    {
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            DocColumn column = table.Columns[columnIndex];
            if (column.Kind == kind && string.Equals(column.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return column;
            }
        }

        return null;
    }

    private static GraphSchema ResolveGraphSchema(DocProject project, DocTable table, NodeGraphViewSettings settings)
    {
        var schema = new GraphSchema();
        schema.TypeColumn = FindColumnById(table, settings.TypeColumnId) ?? FindColumnByNameAndKind(table, DefaultTypeColumnName, DocColumnKind.Select);
        schema.PositionColumn = FindColumnById(table, settings.PositionColumnId) ?? FindColumnByNameAndKind(table, DefaultPositionColumnName, DocColumnKind.Vec2);
        schema.TitleColumn = FindColumnById(table, settings.TitleColumnId) ?? FindColumnByNameAndKind(table, DefaultTitleColumnName, DocColumnKind.Text);
        schema.ExecutionOutputColumn = FindColumnById(table, settings.ExecutionOutputColumnId) ?? FindColumnByNameAndKind(table, DefaultExecutionNextColumnName, DocColumnKind.Relation);
        schema.EdgeSubtableColumn = FindColumnById(table, settings.EdgeSubtableColumnId) ?? FindColumnByNameAndKind(table, DefaultEdgesColumnName, DocColumnKind.Subtable);
        schema.EdgeTable = null;
        if (schema.EdgeSubtableColumn != null && !string.IsNullOrWhiteSpace(schema.EdgeSubtableColumn.SubtableId))
        {
            schema.EdgeTable = FindTableById(project, schema.EdgeSubtableColumn.SubtableId);
        }

        if (schema.EdgeTable == null)
        {
            for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
            {
                DocTable candidateTable = project.Tables[tableIndex];
                if (!string.Equals(candidateTable.ParentTableId, table.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                DocColumn? fromNodeColumn = FindColumnByNameAndKind(candidateTable, DefaultFromNodeColumnName, DocColumnKind.Relation);
                DocColumn? toNodeColumn = FindColumnByNameAndKind(candidateTable, DefaultToNodeColumnName, DocColumnKind.Relation);
                if (fromNodeColumn != null && toNodeColumn != null)
                {
                    schema.EdgeTable = candidateTable;
                    break;
                }
            }
        }

        if (schema.EdgeTable != null)
        {
            schema.EdgeFromNodeColumn = FindColumnByNameAndKind(schema.EdgeTable, DefaultFromNodeColumnName, DocColumnKind.Relation);
            schema.EdgeFromPinColumn = FindColumnByNameAndKind(schema.EdgeTable, DefaultFromPinColumnName, DocColumnKind.Text);
            schema.EdgeToNodeColumn = FindColumnByNameAndKind(schema.EdgeTable, DefaultToNodeColumnName, DocColumnKind.Relation);
            schema.EdgeToPinColumn = FindColumnByNameAndKind(schema.EdgeTable, DefaultToPinColumnName, DocColumnKind.Text);
        }

        return schema;
    }

    private static bool HasRequiredSchema(GraphSchema schema)
    {
        return schema.TypeColumn != null &&
               schema.PositionColumn != null &&
               schema.ExecutionOutputColumn != null &&
               schema.EdgeSubtableColumn != null &&
               schema.EdgeTable != null &&
               schema.EdgeFromNodeColumn != null &&
               schema.EdgeFromPinColumn != null &&
               schema.EdgeToNodeColumn != null &&
               schema.EdgeToPinColumn != null;
    }

    private static void InferSettingsFromSchema(NodeGraphViewSettings settings, GraphSchema schema)
    {
        if (schema.TypeColumn != null)
        {
            settings.TypeColumnId = schema.TypeColumn.Id;
        }

        if (schema.PositionColumn != null)
        {
            settings.PositionColumnId = schema.PositionColumn.Id;
        }

        if (schema.TitleColumn != null)
        {
            settings.TitleColumnId = schema.TitleColumn.Id;
        }

        if (schema.ExecutionOutputColumn != null)
        {
            settings.ExecutionOutputColumnId = schema.ExecutionOutputColumn.Id;
        }

        if (schema.EdgeSubtableColumn != null)
        {
            settings.EdgeSubtableColumnId = schema.EdgeSubtableColumn.Id;
        }
    }

    private static string BuildViewKey(DocTable table, DocView view)
    {
        return RendererSettingsNamespace + ":" + table.Id + ":" + view.Id;
    }

    private static NodeGraphViewSettings GetOrCreateViewSettings(IDerpDocEditorContext workspace, DocTable table, DocView view)
    {
        if (_settingsCacheProjectRevision != workspace.ProjectRevision)
        {
            SettingsCacheByViewKey.Clear();
            _settingsCacheProjectRevision = workspace.ProjectRevision;
        }

        string viewKey = BuildViewKey(table, view);
        if (SettingsCacheByViewKey.TryGetValue(viewKey, out NodeGraphViewSettings? cachedSettings))
        {
            return cachedSettings;
        }

        var settings = new NodeGraphViewSettings();
        if (workspace.TryGetProjectPluginSetting(viewKey, out string serializedSettings) &&
            !string.IsNullOrWhiteSpace(serializedSettings))
        {
            try
            {
                NodeGraphViewSettings? parsedSettings = JsonSerializer.Deserialize<NodeGraphViewSettings>(serializedSettings);
                if (parsedSettings != null)
                {
                    settings = parsedSettings;
                }
            }
            catch
            {
                settings = new NodeGraphViewSettings();
            }
        }

        if (settings.TypeLayouts == null)
        {
            settings.TypeLayouts = new List<NodeTypeLayout>();
        }
        for (int typeLayoutIndex = 0; typeLayoutIndex < settings.TypeLayouts.Count; typeLayoutIndex++)
        {
            settings.TypeLayouts[typeLayoutIndex].NodeWidth = ResolveNodeWidth(settings.TypeLayouts[typeLayoutIndex].NodeWidth);
        }

        SettingsCacheByViewKey[viewKey] = settings;
        return settings;
    }

    private static void SaveViewSettings(IDerpDocEditorContext workspace, DocTable table, DocView view, NodeGraphViewSettings settings)
    {
        string viewKey = BuildViewKey(table, view);
        string serializedSettings = JsonSerializer.Serialize(settings);
        workspace.SetProjectPluginSetting(viewKey, serializedSettings);
        SettingsCacheByViewKey[viewKey] = settings;
    }

    private static NodeGraphViewState GetOrCreateViewState(string viewKey)
    {
        if (ViewStateByViewKey.TryGetValue(viewKey, out NodeGraphViewState? viewState) &&
            viewState != null)
        {
            return viewState;
        }

        viewState = new NodeGraphViewState
        {
            Zoom = 1f,
            Pan = new Vector2(140f, 110f),
        };
        ViewStateByViewKey[viewKey] = viewState;
        return viewState;
    }

    private static Vector2 WorldToScreen(Vector2 worldPosition, NodeGraphViewState viewState, float contentRectX, float contentRectY)
    {
        return new Vector2(
            contentRectX + viewState.Pan.X + (worldPosition.X * viewState.Zoom),
            contentRectY + viewState.Pan.Y + (worldPosition.Y * viewState.Zoom));
    }

    private static Vector2 ScreenToWorld(Vector2 screenPosition, NodeGraphViewState viewState, float contentRectX, float contentRectY)
    {
        return new Vector2(
            (screenPosition.X - contentRectX - viewState.Pan.X) / viewState.Zoom,
            (screenPosition.Y - contentRectY - viewState.Pan.Y) / viewState.Zoom);
    }

    private static ImRect _lastDrawRect;

    private readonly record struct NodeVisual(
        string RowId,
        int SourceRowIndex,
        DocRow Row,
        string TypeName,
        string Title,
        Vector2 WorldPosition,
        float WidthUnits,
        ImRect ScreenRect,
        float PinAreaHeight,
        float Scale,
        bool InlineEditorsActive);

    private readonly record struct PinVisual(
        string NodeRowId,
        int NodeRowIndex,
        string ColumnId,
        string PinId,
        DocColumnKind ColumnKind,
        bool IsOutput,
        string SourceTableId,
        string SourceRowId,
        string SourceColumnId,
        bool IsExecutionOutput,
        Vector2 Center,
        ImRect HitRect);

    private sealed class NodeGraphViewState
    {
        public float Zoom = 1f;
        public Vector2 Pan;
        public bool PanActive;
        public Vector2 PanStartMouse;
        public Vector2 PanStartValue;
        public bool DragActive;
        public string DraggedRowId = "";
        public string DragPositionColumnId = "";
        public Vector2 DragStartWorld;
        public Vector2 DragCurrentWorld;
        public Vector2 DragPointerOffset;
        public bool WireDragActive;
        public bool WireFromIsOutput;
        public string WireFromNodeRowId = "";
        public int WireFromNodeRowIndex = -1;
        public string WireFromColumnId = "";
        public string WireFromPinId = "";
        public DocColumnKind WireFromColumnKind = DocColumnKind.Text;
        public string WireFromSourceTableId = "";
        public string WireFromSourceRowId = "";
        public string WireFromSourceColumnId = "";
        public bool WireFromExecutionOutput;
        public Vector2 WireFromAnchor;
        public bool CreateMenuOpen;
        public Vector2 CreateMenuScreenPosition;
        public Vector2 CreateMenuWorldPosition;
        public float CreateMenuScrollY;
        public string ContextMenuNodeRowId = "";
        public bool ShortcutCopyDown;
        public bool ShortcutPasteDown;
        public bool ShortcutDeleteDown;
    }

    private sealed class InlineTextEditState
    {
        public char[] Buffer { get; } = new char[MaxInlineTextBuffer];
        public int BufferLength;
        public string LastAppliedText = "";
    }

    private sealed class NodeGraphViewSettings
    {
        public string TypeColumnId { get; set; } = "";
        public string PositionColumnId { get; set; } = "";
        public string TitleColumnId { get; set; } = "";
        public string ExecutionOutputColumnId { get; set; } = "";
        public string EdgeSubtableColumnId { get; set; } = "";
        public List<NodeTypeLayout> TypeLayouts { get; set; } = new();
    }

    private sealed class NodeTypeLayout
    {
        public string TypeName { get; set; } = "";
        public float NodeWidth { get; set; } = DefaultNodeWidth;
        public List<NodeFieldLayout> Fields { get; set; } = new();
    }

    private sealed class NodeFieldLayout
    {
        public string ColumnId { get; set; } = "";
        public string Mode { get; set; } = NodeFieldDisplayMode.Setting.ToString();
    }

    private enum NodeFieldDisplayMode
    {
        Hidden,
        Setting,
        InputPin,
        OutputPin,
    }

    private enum NodeSubtableDisplayMode
    {
        Grid,
        Board,
        Calendar,
        Chart,
        Custom,
    }

    private struct GraphSchema
    {
        public DocColumn? TypeColumn;
        public DocColumn? PositionColumn;
        public DocColumn? TitleColumn;
        public DocColumn? ExecutionOutputColumn;
        public DocColumn? EdgeSubtableColumn;
        public DocTable? EdgeTable;
        public DocColumn? EdgeFromNodeColumn;
        public DocColumn? EdgeFromPinColumn;
        public DocColumn? EdgeToNodeColumn;
        public DocColumn? EdgeToPinColumn;
    }
}
