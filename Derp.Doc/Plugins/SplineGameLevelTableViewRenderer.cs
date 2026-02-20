using System.Globalization;
using System.Numerics;
using Derp.Doc.Assets;
using Derp.Doc.Commands;
using Derp.Doc.Editor;
using Derp.Doc.Model;
using Derp.Doc.Tables;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Input;
using DerpLib.ImGui.Widgets;
using DerpLib.Rendering;
using FontAwesome.Sharp;

namespace Derp.Doc.Plugins;

public sealed class SplineGameLevelTableViewRenderer : DerpDocTableViewRendererBase
{
    private const float ToolbarWidth = 76f;
    private const float ToolbarPadding = 6f;
    private const float ToolbarButtonSize = 34f;
    private const float ToolbarRowSpacing = 5f;
    private const float HintBarHeight = 24f;
    private const float MinCanvasZoom = 0.2f;
    private const float MaxCanvasZoom = 4f;
    private const float PointHitRadius = 8f;
    private const float HandleHitRadius = 7f;
    private const float MarkerHitRadius = 10f;
    private const float DefaultPointHandleLength = 48f;
    private const float EntityFallbackSize = 14f;
    private const float DefaultEntityUiScale = 0.1f;
    private const float AdaptiveHalfPreviewScaleThreshold = 0.6f;
    private const float AdaptiveQuarterPreviewScaleThreshold = 0.3f;
    private const int DebugNormalSamplesPerSegment = 16;
    private const float DebugNormalLengthWorld = 28f;
    private const int CurveSegments = 22;
    private const int SplineProjectionSamplesPerSegment = 24;
    private const int SplineProjectionRefineSamples = 14;
    private const float GridStepWorld = 64f;
    private const float InspectorLabelWidth = 96f;
    private const float InspectorRowHeight = 28f;
    private const float InspectorRowSpacing = 3f;
    private const float InspectorFieldMinWidth = 120f;
    private const string EntityContextMenuId = "spline_game_level_entity_context_menu";

    private static readonly Dictionary<string, ViewState> ViewStateByKey = new(StringComparer.Ordinal);
    private static readonly List<EntryVisual> EntriesScratch = new(256);
    private static readonly List<EntryVisual> PointEntriesScratch = new(128);
    private static readonly List<EntryVisual> EntityEntriesScratch = new(128);
    private static readonly List<int> SplinePointIndexesScratch = new(128);
    private static readonly List<ToolGroup> ToolGroupsScratch = new(64);
    private static readonly List<TableOption> TableOptionsScratch = new(64);
    private static readonly List<RowOption> RowOptionsScratch = new(256);
    private static readonly Vector2[] CurvePointScratch = new Vector2[CurveSegments + 1];

    private static readonly char[] DataJsonEditBuffer = new char[2048];
    private static int DataJsonEditLength;
    private static bool DataJsonEditFocused;
    private static string DataJsonSyncKey = "";

    private static readonly string SelectIcon = ((char)IconChar.MousePointer).ToString();
    private static readonly string PenIcon = ((char)IconChar.Pen).ToString();
    private static readonly string NormalsIcon = "N";
    private static readonly string ExpandIcon = ((char)IconChar.ChevronRight).ToString();
    private static readonly string CollapseIcon = ((char)IconChar.ChevronRight).ToString();

    public override string RendererId => SplineGameLevelIds.LevelEditorRendererId;

    public override string DisplayName => "Spline Game Level Editor";

    public override string? IconGlyph => ((char)IconChar.BezierCurve).ToString();

    public override void Draw(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocView view,
        ImRect contentRect)
    {
        Im.DrawRect(contentRect.X, contentRect.Y, contentRect.Width, contentRect.Height, Im.Style.Background);
        if (!SplineGameLevelEditorHelpers.IsSplineGameLevelTable(table))
        {
            Im.Text("This renderer only supports SplineGame level tables.".AsSpan(), contentRect.X + 10f, contentRect.Y + 10f, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }

        if (workspace is not DocWorkspace mutableWorkspace)
        {
            Im.Text("Workspace is not editable.".AsSpan(), contentRect.X + 10f, contentRect.Y + 10f, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }

        if (!TryResolveLevelSchema(table, out LevelSchema levelSchema))
        {
            Im.Text("SplineGame level schema is missing required columns.".AsSpan(), contentRect.X + 10f, contentRect.Y + 10f, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }

        string? activeParentRowId = mutableWorkspace.ActiveParentRowId;
        bool canWriteParentScopedRows = levelSchema.ParentRowColumn == null || !string.IsNullOrWhiteSpace(activeParentRowId);

        if (!TryResolveLevelContext(
                mutableWorkspace,
                table,
                levelSchema,
                activeParentRowId,
                createIfMissing: canWriteParentScopedRows,
                out LevelContext levelContext))
        {
            if (!canWriteParentScopedRows && levelSchema.ParentRowColumn != null)
            {
                Im.Text(
                    "Open this table from a parent SplineGameLevel cell to edit a specific level row.".AsSpan(),
                    contentRect.X + 10f,
                    contentRect.Y + 10f,
                    Im.Style.FontSize,
                    Im.Style.TextSecondary);
                return;
            }

            Im.Text("No level row exists for this context.".AsSpan(), contentRect.X + 10f, contentRect.Y + 10f, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }

        string viewKey = BuildViewStateKey(table.Id, view.Id, levelContext.LevelRow.Id);
        ViewState state = GetOrCreateViewState(viewKey);

        BuildEntries(levelContext, EntriesScratch, SplinePointIndexesScratch);
        BuildToolGroups(mutableWorkspace, levelContext, ToolGroupsScratch);
        EnsureActiveToolIsValid(state, ToolGroupsScratch);

        var toolbarRect = new ImRect(
            contentRect.X + 4f,
            contentRect.Y + 4f,
            ToolbarWidth,
            MathF.Max(20f, contentRect.Height - HintBarHeight - 8f));
        var canvasRect = new ImRect(
            toolbarRect.Right + 6f,
            contentRect.Y + 4f,
            MathF.Max(20f, contentRect.Width - toolbarRect.Width - 14f),
            MathF.Max(20f, contentRect.Height - HintBarHeight - 8f));
        var hintRect = new ImRect(
            contentRect.X + 4f,
            contentRect.Bottom - HintBarHeight - 4f,
            MathF.Max(20f, contentRect.Width - 8f),
            HintBarHeight);

        DrawCanvasBackground(canvasRect, state);

        var input = Im.Context.Input;
        Vector2 mouseScreen = Im.MousePos;
        bool hasExpandedFlyout = TryComputeExpandedGroupFlyoutRect(toolbarRect, ToolGroupsScratch, state, out ImRect expandedFlyoutRect);
        bool mouseInExpandedFlyout = hasExpandedFlyout && expandedFlyoutRect.Contains(mouseScreen);
        if (!mouseInExpandedFlyout)
        {
            HandleCanvasPanZoom(input, canvasRect, state, mouseScreen);
        }

        DrawLevelGeometry(mutableWorkspace, canvasRect, state, EntriesScratch, SplinePointIndexesScratch, state.SelectedTableId, state.SelectedRowId);
        DrawEntityPlacementPreview(
            mutableWorkspace,
            canvasRect,
            state,
            EntriesScratch,
            SplinePointIndexesScratch,
            mouseScreen,
            canWriteParentScopedRows,
            mouseInExpandedFlyout);

        HandleEditingInteractions(
            mutableWorkspace,
            levelContext,
            state,
            canvasRect,
            input,
            mouseScreen,
            canWriteParentScopedRows,
            hasExpandedFlyout ? expandedFlyoutRect : null);

        DrawEntityContextMenu(mutableWorkspace, levelContext, state, canWriteParentScopedRows);

        DrawToolbar(
            mutableWorkspace,
            levelContext,
            state,
            toolbarRect,
            canWriteParentScopedRows,
            ToolGroupsScratch);

        DrawHintBar(hintRect, state, canWriteParentScopedRows);

        if (!canWriteParentScopedRows && levelSchema.ParentRowColumn != null)
        {
            Im.Text(
                "Read-only: open from a parent row to edit.".AsSpan(),
                canvasRect.X + 8f,
                canvasRect.Y + 8f,
                Im.Style.FontSize - 1f,
                Im.Style.TextSecondary);
        }
    }

    public override float DrawInspector(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocView view,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        if (workspace is not DocWorkspace mutableWorkspace)
        {
            return y;
        }

        if (!TryResolveLevelSchema(table, out LevelSchema levelSchema))
        {
            Im.Text("SplineGame level schema is missing required columns.".AsSpan(), contentRect.X + 8f, y, style.FontSize - 1f, style.TextSecondary);
            return y + 20f;
        }

        string? activeParentRowId = mutableWorkspace.ActiveParentRowId;
        bool canWriteParentScopedRows = levelSchema.ParentRowColumn == null || !string.IsNullOrWhiteSpace(activeParentRowId);

        if (!TryResolveLevelContext(
                mutableWorkspace,
                table,
                levelSchema,
                activeParentRowId,
                createIfMissing: false,
                out LevelContext levelContext))
        {
            Im.Text("Selected Entry".AsSpan(), contentRect.X + 8f, y, style.FontSize - 1f, style.TextSecondary);
            y += 18f;

            if (!canWriteParentScopedRows && levelSchema.ParentRowColumn != null)
            {
                Im.Text("Open from a parent row to inspect level entries.".AsSpan(), contentRect.X + 8f, y, style.FontSize - 1f, style.TextSecondary);
            }
            else
            {
                Im.Text("No level row selected.".AsSpan(), contentRect.X + 8f, y, style.FontSize - 1f, style.TextSecondary);
            }

            return y + 20f;
        }

        string viewKey = BuildViewStateKey(table.Id, view.Id, levelContext.LevelRow.Id);
        ViewState state = GetOrCreateViewState(viewKey);

        BuildEntries(levelContext, EntriesScratch, SplinePointIndexesScratch);

        float rowX = contentRect.X + 8f;
        float rowWidth = MathF.Max(160f, contentRect.Width - 16f);

        Im.Text("Selected Entry".AsSpan(), rowX, y, style.FontSize - 1f, style.TextSecondary);
        y += 18f;

        if (!TryFindEntryBySource(EntriesScratch, state.SelectedTableId, state.SelectedRowId, out EntryVisual selectedEntry))
        {
            Im.Text("None".AsSpan(), rowX, y, style.FontSize - 1f, style.TextSecondary);
            return y + 20f;
        }

        if (selectedEntry.Kind == EntryKind.Point)
        {
            return DrawPointInspector(
                mutableWorkspace,
                levelContext,
                rowX,
                y,
                rowWidth,
                selectedEntry,
                style,
                canWriteParentScopedRows);
        }

        return DrawEntityInspector(
            mutableWorkspace,
            levelContext,
            rowX,
            y,
            rowWidth,
            selectedEntry,
            style,
            canWriteParentScopedRows);
    }

    private static float DrawPointInspector(
        DocWorkspace workspace,
        LevelContext context,
        float rowX,
        float y,
        float rowWidth,
        EntryVisual selectedEntry,
        ImStyle style,
        bool canEdit)
    {
        if (!TryFindRowById(context.PointsTable, selectedEntry.RowId, out DocRow pointRow))
        {
            Im.Text("Selected point row was deleted.".AsSpan(), rowX, y, style.FontSize - 1f, style.TextSecondary);
            return y + 20f;
        }

        Im.Text(("Point: " + selectedEntry.RowId).AsSpan(), rowX, y, style.FontSize - 1f, style.TextSecondary);
        y += 18f;

        float orderValue = (float)pointRow.GetCell(context.PointsSchema.OrderColumn).NumberValue;
        if (DrawInspectorFloatRow("Order".AsSpan(), "sg_point_order", rowX, y, rowWidth, ref orderValue, -100000f, 100000f, "F3", style) && canEdit)
        {
            SetNumberCell(workspace, context.PointsTable, pointRow, context.PointsSchema.OrderColumn, orderValue);
        }

        y += InspectorRowHeight + InspectorRowSpacing;

        Vector2 positionValue = ReadVec2(pointRow, context.PointsSchema.PositionColumn);
        if (DrawInspectorVec2Row("Position".AsSpan(), "sg_point_pos", rowX, y, rowWidth, ref positionValue, style) && canEdit)
        {
            SetVec2Cell(workspace, context.PointsTable, pointRow, context.PointsSchema.PositionColumn, positionValue);
        }

        y += InspectorRowHeight + InspectorRowSpacing;

        Vector2 tangentInValue = ReadVec2(pointRow, context.PointsSchema.TangentInColumn);
        if (DrawInspectorVec2Row("Tangent In".AsSpan(), "sg_point_tin", rowX, y, rowWidth, ref tangentInValue, style) && canEdit)
        {
            SetVec2Cell(workspace, context.PointsTable, pointRow, context.PointsSchema.TangentInColumn, tangentInValue);
        }

        y += InspectorRowHeight + InspectorRowSpacing;

        Vector2 tangentOutValue = ReadVec2(pointRow, context.PointsSchema.TangentOutColumn);
        if (DrawInspectorVec2Row("Tangent Out".AsSpan(), "sg_point_tout", rowX, y, rowWidth, ref tangentOutValue, style) && canEdit)
        {
            SetVec2Cell(workspace, context.PointsTable, pointRow, context.PointsSchema.TangentOutColumn, tangentOutValue);
        }

        y += InspectorRowHeight + 2f;

        if (canEdit && Im.Button("Auto Tangents", rowX, y, 120f, 24f))
        {
            AutoSetSplinePointTangents(workspace, context);
        }

        y += 30f;
        return y;
    }

    private static float DrawEntityInspector(
        DocWorkspace workspace,
        LevelContext context,
        float rowX,
        float y,
        float rowWidth,
        EntryVisual selectedEntry,
        ImStyle style,
        bool canEdit)
    {
        if (!TryFindRowById(context.EntitiesTable, selectedEntry.RowId, out DocRow entityRow))
        {
            Im.Text("Selected entity row was deleted.".AsSpan(), rowX, y, style.FontSize - 1f, style.TextSecondary);
            return y + 20f;
        }

        Im.Text(("Entity: " + selectedEntry.RowId).AsSpan(), rowX, y, style.FontSize - 1f, style.TextSecondary);
        y += 18f;

        float orderValue = (float)entityRow.GetCell(context.EntitiesSchema.OrderColumn).NumberValue;
        if (DrawInspectorFloatRow("Order".AsSpan(), "sg_entity_order", rowX, y, rowWidth, ref orderValue, -100000f, 100000f, "F3", style) && canEdit)
        {
            SetNumberCell(workspace, context.EntitiesTable, entityRow, context.EntitiesSchema.OrderColumn, orderValue);
        }

        y += InspectorRowHeight + InspectorRowSpacing;

        float paramTValue = NormalizeParamT((float)entityRow.GetCell(context.EntitiesSchema.ParamTColumn).NumberValue);
        if (DrawInspectorFloatRow("Param T".AsSpan(), "sg_entity_paramt", rowX, y, rowWidth, ref paramTValue, 0f, 1f, "F4", style) && canEdit)
        {
            float normalizedParamT = NormalizeParamT(paramTValue);
            if (SetNumberCell(workspace, context.EntitiesTable, entityRow, context.EntitiesSchema.ParamTColumn, normalizedParamT) &&
                TrySampleClosedSplineAtNormalizedT(EntriesScratch, SplinePointIndexesScratch, normalizedParamT, out Vector2 sampledPosition))
            {
                SetVec2Cell(workspace, context.EntitiesTable, entityRow, context.EntitiesSchema.PositionColumn, sampledPosition);
            }
        }

        y += InspectorRowHeight + InspectorRowSpacing;

        if (DrawEntityTableDropdown(workspace, context, entityRow, rowX, y, rowWidth, style, canEdit))
        {
            BuildEntries(context, EntriesScratch, SplinePointIndexesScratch);
        }

        y += InspectorRowHeight + InspectorRowSpacing;

        if (DrawEntityRowDropdown(workspace, context, entityRow, rowX, y, rowWidth, style, canEdit))
        {
            BuildEntries(context, EntriesScratch, SplinePointIndexesScratch);
        }

        y += InspectorRowHeight + InspectorRowSpacing;

        Vector2 displayedWorldPosition = ResolveEntryWorldPosition(selectedEntry, EntriesScratch, SplinePointIndexesScratch);
        Span<char> positionBuffer = stackalloc char[96];
        int positionLength = 0;
        "Position (".AsSpan().CopyTo(positionBuffer);
        positionLength += 10;
        displayedWorldPosition.X.TryFormat(positionBuffer.Slice(positionLength), out int posXChars, "F2", CultureInfo.InvariantCulture);
        positionLength += posXChars;
        positionBuffer[positionLength++] = ',';
        positionBuffer[positionLength++] = ' ';
        displayedWorldPosition.Y.TryFormat(positionBuffer.Slice(positionLength), out int posYChars, "F2", CultureInfo.InvariantCulture);
        positionLength += posYChars;
        positionBuffer[positionLength++] = ')';
        Im.Text(positionBuffer.Slice(0, positionLength), rowX, y, style.FontSize - 1f, style.TextSecondary);
        y += 18f;

        y = DrawInspectorTextCellRow(
            workspace,
            context.EntitiesTable,
            entityRow,
            context.EntitiesSchema.DataJsonColumn,
            "Data Json".AsSpan(),
            "sg_entity_data_json",
            DataJsonEditBuffer,
            ref DataJsonEditLength,
            ref DataJsonEditFocused,
            ref DataJsonSyncKey,
            rowX,
            y,
            rowWidth,
            style,
            canEdit);

        return y;
    }

    private static bool DrawEntityTableDropdown(
        DocWorkspace workspace,
        LevelContext context,
        DocRow entityRow,
        float x,
        float y,
        float width,
        ImStyle style,
        bool canEdit)
    {
        Im.Text("Entity Table".AsSpan(), x, y + (InspectorRowHeight - style.FontSize) * 0.5f, style.FontSize - 1f, style.TextPrimary);

        BuildTableRefOptions(workspace, context.EntitiesSchema.EntityTableColumn, TableOptionsScratch);
        if (TableOptionsScratch.Count <= 0)
        {
            return false;
        }

        string selectedTableId = entityRow.GetCell(context.EntitiesSchema.EntityTableColumn).StringValue ?? "";
        int selectedIndex = 0;
        for (int optionIndex = 0; optionIndex < TableOptionsScratch.Count; optionIndex++)
        {
            if (string.Equals(TableOptionsScratch[optionIndex].Id, selectedTableId, StringComparison.Ordinal))
            {
                selectedIndex = optionIndex;
                break;
            }
        }

        string[] optionLabels = new string[TableOptionsScratch.Count];
        for (int optionIndex = 0; optionIndex < TableOptionsScratch.Count; optionIndex++)
        {
            optionLabels[optionIndex] = TableOptionsScratch[optionIndex].Label;
        }

        float inputX = x + InspectorLabelWidth;
        float inputWidth = MathF.Max(InspectorFieldMinWidth, width - InspectorLabelWidth);
        bool changed = Im.Dropdown("sg_entity_table", optionLabels.AsSpan(), ref selectedIndex, inputX, y, inputWidth);
        if (!changed || !canEdit)
        {
            return false;
        }

        string updatedTableId = TableOptionsScratch[selectedIndex].Id;
        bool tableChanged = SetTextCell(workspace, context.EntitiesTable, entityRow, context.EntitiesSchema.EntityTableColumn, updatedTableId);
        if (!tableChanged)
        {
            return false;
        }

        string defaultRowId = ResolveDefaultEntityRowId(workspace.Project, updatedTableId);
        SetTextCell(workspace, context.EntitiesTable, entityRow, context.EntitiesSchema.EntityRowIdColumn, defaultRowId);
        return true;
    }

    private static bool DrawEntityRowDropdown(
        DocWorkspace workspace,
        LevelContext context,
        DocRow entityRow,
        float x,
        float y,
        float width,
        ImStyle style,
        bool canEdit)
    {
        Im.Text("Entity Row".AsSpan(), x, y + (InspectorRowHeight - style.FontSize) * 0.5f, style.FontSize - 1f, style.TextPrimary);

        string selectedTableId = entityRow.GetCell(context.EntitiesSchema.EntityTableColumn).StringValue ?? "";
        BuildEntityRowOptions(workspace.Project, selectedTableId, RowOptionsScratch);
        if (RowOptionsScratch.Count <= 0)
        {
            return false;
        }

        string selectedRowId = entityRow.GetCell(context.EntitiesSchema.EntityRowIdColumn).StringValue ?? "";
        int selectedIndex = 0;
        for (int optionIndex = 0; optionIndex < RowOptionsScratch.Count; optionIndex++)
        {
            if (string.Equals(RowOptionsScratch[optionIndex].Id, selectedRowId, StringComparison.Ordinal))
            {
                selectedIndex = optionIndex;
                break;
            }
        }

        string[] optionLabels = new string[RowOptionsScratch.Count];
        for (int optionIndex = 0; optionIndex < RowOptionsScratch.Count; optionIndex++)
        {
            optionLabels[optionIndex] = RowOptionsScratch[optionIndex].Label;
        }

        float inputX = x + InspectorLabelWidth;
        float inputWidth = MathF.Max(InspectorFieldMinWidth, width - InspectorLabelWidth);
        bool changed = Im.Dropdown("sg_entity_row", optionLabels.AsSpan(), ref selectedIndex, inputX, y, inputWidth);
        if (!changed || !canEdit)
        {
            return false;
        }

        return SetTextCell(
            workspace,
            context.EntitiesTable,
            entityRow,
            context.EntitiesSchema.EntityRowIdColumn,
            RowOptionsScratch[selectedIndex].Id);
    }

    private static void DrawToolbar(
        DocWorkspace workspace,
        LevelContext context,
        ViewState state,
        ImRect toolbarRect,
        bool canEdit,
        List<ToolGroup> toolGroups)
    {
        Im.DrawRoundedRect(toolbarRect.X, toolbarRect.Y, toolbarRect.Width, toolbarRect.Height, 4f, Im.Style.Surface);
        Im.DrawRoundedRectStroke(toolbarRect.X, toolbarRect.Y, toolbarRect.Width, toolbarRect.Height, 4f, Im.Style.Border, 1f);

        float x = toolbarRect.X + ToolbarPadding;
        float y = toolbarRect.Y + ToolbarPadding;
        float availableWidth = MathF.Max(16f, toolbarRect.Width - (ToolbarPadding * 2f));
        float squareButtonSize = ToolbarButtonSize;
        float squareButtonX = x + MathF.Max(0f, (availableWidth - squareButtonSize) * 0.5f);

        if (DrawToolModeButton(
                "sg_tool_select",
                SelectIcon.AsSpan(),
                squareButtonX,
                y,
                squareButtonSize,
                squareButtonSize,
                state.ActiveTool == ActiveToolKind.Select,
                canEdit))
        {
            state.ActiveTool = ActiveToolKind.Select;
        }

        DrawTooltip(0x7711, new ImRect(squareButtonX, y, squareButtonSize, squareButtonSize).Contains(Im.MousePos), "Selection tool: select and move spline points/entities");
        y += ToolbarButtonSize + ToolbarRowSpacing;

        if (DrawToolModeButton(
                "sg_tool_pen",
                PenIcon.AsSpan(),
                squareButtonX,
                y,
                squareButtonSize,
                squareButtonSize,
                state.ActiveTool == ActiveToolKind.Pen,
                canEdit))
        {
            state.ActiveTool = ActiveToolKind.Pen;
        }

        DrawTooltip(0x7712, new ImRect(squareButtonX, y, squareButtonSize, squareButtonSize).Contains(Im.MousePos), "Pen tool: click canvas to add spline points");
        y += ToolbarButtonSize + ToolbarRowSpacing;

        if (DrawToolModeButton(
                "sg_tool_normals",
                NormalsIcon.AsSpan(),
                squareButtonX,
                y,
                squareButtonSize,
                squareButtonSize,
                state.ShowDebugNormals,
                true))
        {
            state.ShowDebugNormals = !state.ShowDebugNormals;
        }

        DrawTooltip(
            0x7714,
            new ImRect(squareButtonX, y, squareButtonSize, squareButtonSize).Contains(Im.MousePos),
            "Toggle spline normal debug. Green=inward (centroid-facing), red=outward.");
        y += ToolbarButtonSize + ToolbarRowSpacing;

        if (toolGroups.Count > 0)
        {
            Im.DrawLine(toolbarRect.X + 6f, y + 2f, toolbarRect.Right - 6f, y + 2f, 1f, ImStyle.WithAlpha(Im.Style.Border, 150));
            y += 6f;
        }

        ToolGroup? expandedGroup = null;
        float expandedGroupAnchorY = 0f;
        float groupRowHeight = squareButtonSize;
        float expandButtonSize = 16f;
        float expandButtonPadding = 2f;
        for (int groupIndex = 0; groupIndex < toolGroups.Count; groupIndex++)
        {
            ToolGroup group = toolGroups[groupIndex];
            bool groupExpanded = string.Equals(state.ExpandedGroupRowId, group.RowId, StringComparison.Ordinal);
            bool groupSelected = state.ActiveTool == ActiveToolKind.PlaceEntity &&
                                 string.Equals(state.ActiveEntityTableId, group.EntitiesTableId, StringComparison.Ordinal);
            bool groupEnabled = canEdit && group.Items.Count > 0;

            ToolItem groupDisplayItem = default;
            if (group.Items.Count > 0)
            {
                groupDisplayItem = group.Items[0];
                if (groupSelected &&
                    TryFindGroupItemByRowId(group, state.ActiveEntityRowId, out ToolItem activeGroupItem))
                {
                    groupDisplayItem = activeGroupItem;
                }
            }
            else
            {
                groupDisplayItem = new ToolItem("", group.Name, "");
            }

            float groupButtonX = squareButtonX;
            bool groupClicked = DrawToolbarEntityItemButton(
                workspace,
                "sg_tool_group_" + group.RowId,
                groupDisplayItem,
                groupButtonX,
                y,
                squareButtonSize,
                groupSelected,
                groupEnabled);
            if (groupClicked)
            {
                if (group.Items.Count > 0)
                {
                    ToolItem firstItem = group.Items[0];
                    state.ActiveTool = ActiveToolKind.PlaceEntity;
                    state.ActiveEntityTableId = group.EntitiesTableId;
                    state.ActiveEntityRowId = firstItem.RowId;
                }
            }

            string expandGlyph = groupExpanded ? CollapseIcon : ExpandIcon;
            float expandButtonX = groupButtonX + squareButtonSize + expandButtonPadding;
            float expandButtonY = y + (groupRowHeight - expandButtonSize) * 0.5f;
            if (DrawToolModeButton(
                    "sg_tool_expand_" + group.RowId,
                    expandGlyph.AsSpan(),
                    expandButtonX,
                    expandButtonY,
                    expandButtonSize,
                    expandButtonSize,
                    groupExpanded,
                    true))
            {
                state.ExpandedGroupRowId = groupExpanded ? "" : group.RowId;
                groupExpanded = !groupExpanded;
            }

            DrawTooltip(
                group.RowId.GetHashCode() ^ 0x51A8,
                new ImRect(groupButtonX, y, squareButtonSize + expandButtonPadding + expandButtonSize, groupRowHeight).Contains(Im.MousePos),
                "Entity tool set: " + group.Name);

            if (groupExpanded && group.Items.Count > 0)
            {
                expandedGroup = group;
                expandedGroupAnchorY = y;
            }

            y += groupRowHeight + 2f;
            if (y + groupRowHeight > toolbarRect.Bottom - 20f)
            {
                break;
            }
        }

        if (expandedGroup != null)
        {
            float flyoutX = toolbarRect.Right + 6f;
            float flyoutItemSize = ToolbarButtonSize;
            float flyoutPadding = 4f;
            float flyoutItemSpacing = 3f;
            float flyoutWidth = flyoutItemSize + (flyoutPadding * 2f);
            float flyoutHeight = (expandedGroup.Items.Count * (flyoutItemSize + flyoutItemSpacing)) - flyoutItemSpacing + (flyoutPadding * 2f);
            float flyoutY = Math.Clamp(expandedGroupAnchorY, toolbarRect.Y + 2f, toolbarRect.Bottom - flyoutHeight - 2f);
            if (flyoutHeight > 0f)
            {
                Im.DrawRoundedRect(flyoutX, flyoutY, flyoutWidth, flyoutHeight, 4f, Im.Style.Surface);
                Im.DrawRoundedRectStroke(flyoutX, flyoutY, flyoutWidth, flyoutHeight, 4f, Im.Style.Border, 1f);
            }

            float itemY = flyoutY + flyoutPadding;
            for (int itemIndex = 0; itemIndex < expandedGroup.Items.Count; itemIndex++)
            {
                ToolItem item = expandedGroup.Items[itemIndex];
                bool itemSelected = state.ActiveTool == ActiveToolKind.PlaceEntity &&
                                    string.Equals(state.ActiveEntityTableId, expandedGroup.EntitiesTableId, StringComparison.Ordinal) &&
                                    string.Equals(state.ActiveEntityRowId, item.RowId, StringComparison.Ordinal);

                bool clicked = DrawToolbarEntityItemButton(
                    workspace,
                    "sg_tool_item_" + expandedGroup.RowId + "_" + item.RowId,
                    item,
                    flyoutX + flyoutPadding,
                    itemY,
                    flyoutItemSize,
                    itemSelected,
                    canEdit);
                if (clicked)
                {
                    state.ActiveTool = ActiveToolKind.PlaceEntity;
                    state.ActiveEntityTableId = expandedGroup.EntitiesTableId;
                    state.ActiveEntityRowId = item.RowId;
                }

                DrawTooltip(
                    item.RowId.GetHashCode() ^ 0x22B1,
                    new ImRect(flyoutX + flyoutPadding, itemY, flyoutItemSize, flyoutItemSize).Contains(Im.MousePos),
                    "Place " + item.Label);

                itemY += flyoutItemSize + flyoutItemSpacing;
            }
        }

        string toolsTableLabel = ResolveToolsTableName(workspace.Project, context);
        if (!string.IsNullOrWhiteSpace(toolsTableLabel))
        {
            float labelY = toolbarRect.Bottom - 16f;
            Im.Text("Tools".AsSpan(), toolbarRect.X + 8f, labelY, Im.Style.FontSize - 2f, Im.Style.TextSecondary);
            DrawTooltip(0x7713, new ImRect(toolbarRect.X, labelY, toolbarRect.Width, 16f).Contains(Im.MousePos), toolsTableLabel);
        }
    }

    private static bool TryComputeExpandedGroupFlyoutRect(
        ImRect toolbarRect,
        List<ToolGroup> toolGroups,
        ViewState state,
        out ImRect flyoutRect)
    {
        flyoutRect = default;
        if (toolGroups.Count <= 0 || string.IsNullOrWhiteSpace(state.ExpandedGroupRowId))
        {
            return false;
        }

        float y = toolbarRect.Y + ToolbarPadding;
        y += ToolbarButtonSize + ToolbarRowSpacing;
        y += ToolbarButtonSize + ToolbarRowSpacing;
        y += ToolbarButtonSize + ToolbarRowSpacing;
        y += 6f;

        float groupRowHeight = ToolbarButtonSize;
        ToolGroup? expandedGroup = null;
        float expandedGroupAnchorY = 0f;
        for (int groupIndex = 0; groupIndex < toolGroups.Count; groupIndex++)
        {
            ToolGroup group = toolGroups[groupIndex];
            bool groupExpanded = string.Equals(state.ExpandedGroupRowId, group.RowId, StringComparison.Ordinal);
            if (groupExpanded && group.Items.Count > 0)
            {
                expandedGroup = group;
                expandedGroupAnchorY = y;
                break;
            }

            y += groupRowHeight + 2f;
            if (y + groupRowHeight > toolbarRect.Bottom - 20f)
            {
                break;
            }
        }

        if (expandedGroup == null)
        {
            return false;
        }

        float flyoutX = toolbarRect.Right + 6f;
        float flyoutItemSize = ToolbarButtonSize;
        float flyoutPadding = 4f;
        float flyoutItemSpacing = 3f;
        float flyoutWidth = flyoutItemSize + (flyoutPadding * 2f);
        float flyoutHeight = (expandedGroup.Items.Count * (flyoutItemSize + flyoutItemSpacing)) - flyoutItemSpacing + (flyoutPadding * 2f);
        float flyoutY = Math.Clamp(expandedGroupAnchorY, toolbarRect.Y + 2f, toolbarRect.Bottom - flyoutHeight - 2f);
        if (flyoutHeight <= 0f)
        {
            return false;
        }

        flyoutRect = new ImRect(flyoutX, flyoutY, flyoutWidth, flyoutHeight);
        return true;
    }

    private static bool DrawToolModeButton(
        string id,
        ReadOnlySpan<char> glyph,
        float x,
        float y,
        float width,
        float height,
        bool active,
        bool enabled)
    {
        byte activeAlpha = enabled ? (byte)210 : (byte)120;
        byte idleAlpha = enabled ? (byte)255 : (byte)140;
        uint fillColor = active
            ? ImStyle.WithAlpha(Im.Style.Primary, activeAlpha)
            : ImStyle.WithAlpha(Im.Style.Surface, idleAlpha);
        Im.DrawRoundedRect(x, y, width, height, 4f, fillColor);
        Im.DrawRoundedRectStroke(x, y, width, height, 4f, active ? Im.Style.Primary : Im.Style.Border, active ? 2f : 1f);

        float textWidth = Im.MeasureTextWidth(glyph, Im.Style.FontSize);
        float textX = x + (width - textWidth) * 0.5f;
        float textY = y + (height - Im.Style.FontSize) * 0.5f;
        Im.Text(glyph, textX, textY, Im.Style.FontSize, enabled ? Im.Style.TextPrimary : Im.Style.TextSecondary);

        if (!enabled)
        {
            return false;
        }

        var buttonRect = new ImRect(x, y, width, height);
        bool clicked = buttonRect.Contains(Im.MousePos) &&
                       Im.Context.Input.MousePressed &&
                       !Im.Context.AnyActive;
        if (clicked)
        {
            Im.Context.ConsumeMouseLeftPress();
        }

        _ = id;
        return clicked;
    }

    private static bool DrawToolbarEntityItemButton(
        DocWorkspace workspace,
        string id,
        ToolItem item,
        float x,
        float y,
        float size,
        bool active,
        bool enabled)
    {
        byte activeAlpha = enabled ? (byte)195 : (byte)120;
        byte idleAlpha = enabled ? (byte)255 : (byte)140;
        uint fillColor = active
            ? ImStyle.WithAlpha(Im.Style.Primary, activeAlpha)
            : ImStyle.WithAlpha(Im.Style.Surface, idleAlpha);
        Im.DrawRoundedRect(x, y, size, size, 4f, fillColor);
        Im.DrawRoundedRectStroke(x, y, size, size, 4f, active ? Im.Style.Primary : Im.Style.Border, active ? 2f : 1f);

        bool drewUiAsset = false;
        if (!string.IsNullOrWhiteSpace(item.UiAssetPath))
        {
            DerpUiPreviewCache.PreviewResult previewResult = DocAssetServices.DerpUiPreviewCache.GetPreview(
                workspace.GameRoot,
                workspace.AssetsRoot,
                item.UiAssetPath,
                DerpUiPreviewCache.PreviewRenderMode.Thumbnail);
            if (previewResult.Status != DerpUiPreviewCache.PreviewStatus.Ready ||
                previewResult.Texture.Width <= 0 ||
                previewResult.Texture.Height <= 0)
            {
                previewResult = DocAssetServices.DerpUiPreviewCache.GetPreview(
                    workspace.GameRoot,
                    workspace.AssetsRoot,
                    item.UiAssetPath,
                    DerpUiPreviewCache.PreviewRenderMode.PrefabQuarter);
            }

            if (previewResult.Status != DerpUiPreviewCache.PreviewStatus.Ready ||
                previewResult.Texture.Width <= 0 ||
                previewResult.Texture.Height <= 0)
            {
                previewResult = DocAssetServices.DerpUiPreviewCache.GetPreview(
                    workspace.GameRoot,
                    workspace.AssetsRoot,
                    item.UiAssetPath,
                    DerpUiPreviewCache.PreviewRenderMode.PrefabHalf);
            }

            if (previewResult.Status != DerpUiPreviewCache.PreviewStatus.Ready ||
                previewResult.Texture.Width <= 0 ||
                previewResult.Texture.Height <= 0)
            {
                previewResult = DocAssetServices.DerpUiPreviewCache.GetPreview(
                    workspace.GameRoot,
                    workspace.AssetsRoot,
                    item.UiAssetPath,
                    DerpUiPreviewCache.PreviewRenderMode.PrefabSize);
            }

            if (previewResult.Status == DerpUiPreviewCache.PreviewStatus.Ready &&
                previewResult.Texture.Width > 0 &&
                previewResult.Texture.Height > 0)
            {
                float maxSize = size - 8f;
                float scale = MathF.Min(maxSize / previewResult.Texture.Width, maxSize / previewResult.Texture.Height);
                float drawWidth = previewResult.Texture.Width * scale;
                float drawHeight = previewResult.Texture.Height * scale;
                float drawX = x + (size - drawWidth) * 0.5f;
                float drawY = y + (size - drawHeight) * 0.5f;
                Im.DrawImage(previewResult.Texture, drawX, drawY, drawWidth, drawHeight);
                drewUiAsset = true;
            }
        }

        if (!drewUiAsset)
        {
            string fallbackLabel = BuildShortLabel(item.Label);
            float textWidth = Im.MeasureTextWidth(fallbackLabel.AsSpan(), Im.Style.FontSize - 2f);
            float textX = x + (size - textWidth) * 0.5f;
            float textY = y + (size - (Im.Style.FontSize - 2f)) * 0.5f;
            Im.Text(fallbackLabel.AsSpan(), textX, textY, Im.Style.FontSize - 2f, enabled ? Im.Style.TextPrimary : Im.Style.TextSecondary);
        }

        if (!enabled)
        {
            return false;
        }

        var buttonRect = new ImRect(x, y, size, size);
        bool clicked = buttonRect.Contains(Im.MousePos) &&
                       Im.Context.Input.MousePressed &&
                       !Im.Context.AnyActive;
        if (clicked)
        {
            Im.Context.ConsumeMouseLeftPress();
        }

        _ = id;
        return clicked;
    }

    private static void DrawHintBar(ImRect hintRect, ViewState state, bool canWriteParentScopedRows)
    {
        Im.DrawRoundedRect(hintRect.X, hintRect.Y, hintRect.Width, hintRect.Height, 4f, ImStyle.WithAlpha(Im.Style.Surface, 230));
        Im.DrawRoundedRectStroke(hintRect.X, hintRect.Y, hintRect.Width, hintRect.Height, 4f, Im.Style.Border, 1f);

        string toolHint;
        if (!canWriteParentScopedRows)
        {
            toolHint = "Open from parent row to edit. Middle-mouse pan, wheel zoom.";
        }
        else if (state.ActiveTool == ActiveToolKind.Select)
        {
            toolHint = "Select tool: click items to select, drag points/tangents/entities to move.";
        }
        else if (state.ActiveTool == ActiveToolKind.Pen)
        {
            toolHint = "Pen tool: click canvas to add spline points (closed spline).";
        }
        else
        {
            toolHint = "Entity tool: click near spline to place selected entity.";
        }

        Im.Text(toolHint.AsSpan(), hintRect.X + 8f, hintRect.Y + 4f, Im.Style.FontSize - 1f, Im.Style.TextSecondary);
    }

    private static void HandleCanvasPanZoom(
        in ImInput input,
        ImRect canvasRect,
        ViewState state,
        Vector2 mouseScreenPosition)
    {
        if (canvasRect.Contains(mouseScreenPosition) && input.ScrollDelta != 0f)
        {
            float oldZoom = state.Zoom;
            float newZoom = Math.Clamp(oldZoom * MathF.Pow(1.1f, input.ScrollDelta), MinCanvasZoom, MaxCanvasZoom);
            if (Math.Abs(newZoom - oldZoom) > float.Epsilon)
            {
                Vector2 preZoomWorld = ScreenToWorld(mouseScreenPosition, state, canvasRect);
                state.Zoom = newZoom;
                Vector2 postZoomScreen = WorldToScreen(preZoomWorld, state, canvasRect);
                state.Pan += mouseScreenPosition - postZoomScreen;
            }
        }

        if (canvasRect.Contains(mouseScreenPosition) && input.MouseMiddlePressed)
        {
            state.PanActive = true;
            state.PanStartMouse = mouseScreenPosition;
            state.PanStartValue = state.Pan;
        }

        if (!state.PanActive)
        {
            return;
        }

        if (!input.MouseMiddleDown)
        {
            state.PanActive = false;
            return;
        }

        Vector2 panDelta = mouseScreenPosition - state.PanStartMouse;
        state.Pan = state.PanStartValue + panDelta;
    }

    private static void HandleEditingInteractions(
        DocWorkspace workspace,
        LevelContext context,
        ViewState state,
        ImRect canvasRect,
        in ImInput input,
        Vector2 mouseScreen,
        bool canEdit,
        ImRect? blockedCanvasRect)
    {
        if (canEdit &&
            (input.KeyDelete || input.KeyBackspace) &&
            !Im.Context.WantCaptureKeyboard &&
            !Im.Context.AnyActive &&
            !ImModal.IsAnyOpen &&
            !Im.IsAnyDropdownOpen &&
            !ImContextMenu.IsOpen(EntityContextMenuId))
        {
            _ = TryDeleteSelectedEntity(workspace, context, state);
        }

        if (!canvasRect.Contains(mouseScreen))
        {
            if (state.DragKind != DragTargetKind.None && !input.MouseDown)
            {
                state.DragKind = DragTargetKind.None;
                state.DraggedTableId = "";
                state.DraggedRowId = "";
            }

            return;
        }

        if (blockedCanvasRect.HasValue && blockedCanvasRect.Value.Contains(mouseScreen))
        {
            return;
        }

        ResolveHoveredSplineControl(
            workspace,
            mouseScreen,
            EntriesScratch,
            SplinePointIndexesScratch,
            canvasRect,
            state,
            showSplineControls: ShouldShowTangentsForActiveTool(state),
            selectedTableId: state.SelectedTableId,
            selectedRowId: state.SelectedRowId,
            out int hoveredPointIndex,
            out int hoveredHandleIndex,
            out DragTargetKind hoveredHandleKind,
            out int hoveredEntityIndex);

        if (!ShouldShowTangentsForActiveTool(state) &&
            (state.DragKind == DragTargetKind.InTangent || state.DragKind == DragTargetKind.OutTangent))
        {
            state.DragKind = DragTargetKind.None;
            state.DraggedTableId = "";
            state.DraggedRowId = "";
        }

        if (input.MouseRightPressed && !Im.Context.AnyActive)
        {
            if (hoveredEntityIndex >= 0)
            {
                EntryVisual hoveredEntry = EntriesScratch[hoveredEntityIndex];
                state.SelectedTableId = hoveredEntry.SourceTableId;
                state.SelectedRowId = hoveredEntry.RowId;
                state.ContextMenuEntityRowId = hoveredEntry.RowId;
                ImContextMenu.OpenAt(EntityContextMenuId, mouseScreen.X, mouseScreen.Y);
                Im.Context.ConsumeMouseRightPress();
                return;
            }

            state.ContextMenuEntityRowId = "";
        }

        if (input.MousePressed && !Im.Context.AnyActive)
        {
            if (hoveredHandleIndex >= 0)
            {
                EntryVisual hoveredEntry = EntriesScratch[hoveredHandleIndex];
                state.SelectedTableId = hoveredEntry.SourceTableId;
                state.SelectedRowId = hoveredEntry.RowId;
                if (canEdit)
                {
                    state.DragKind = hoveredHandleKind;
                    state.DraggedTableId = hoveredEntry.SourceTableId;
                    state.DraggedRowId = hoveredEntry.RowId;
                }

                Im.Context.ConsumeMouseLeftPress();
                return;
            }

            if (hoveredEntityIndex >= 0)
            {
                EntryVisual hoveredEntry = EntriesScratch[hoveredEntityIndex];
                state.SelectedTableId = hoveredEntry.SourceTableId;
                state.SelectedRowId = hoveredEntry.RowId;
                if (canEdit)
                {
                    state.DragKind = DragTargetKind.EntityMarker;
                    state.DraggedTableId = hoveredEntry.SourceTableId;
                    state.DraggedRowId = hoveredEntry.RowId;
                }

                Im.Context.ConsumeMouseLeftPress();
                return;
            }

            if (hoveredPointIndex >= 0)
            {
                EntryVisual hoveredEntry = EntriesScratch[hoveredPointIndex];
                state.SelectedTableId = hoveredEntry.SourceTableId;
                state.SelectedRowId = hoveredEntry.RowId;
                if (canEdit)
                {
                    state.DragKind = DragTargetKind.Point;
                    state.DraggedTableId = hoveredEntry.SourceTableId;
                    state.DraggedRowId = hoveredEntry.RowId;
                    Vector2 worldPosition = ScreenToWorld(mouseScreen, state, canvasRect);
                    state.DragPointerOffset = worldPosition - hoveredEntry.Position;
                }

                Im.Context.ConsumeMouseLeftPress();
                return;
            }

            state.SelectedTableId = "";
            state.SelectedRowId = "";

            if (!canEdit)
            {
                return;
            }

            Vector2 clickWorld = ScreenToWorld(mouseScreen, state, canvasRect);
            if (state.ActiveTool == ActiveToolKind.Pen)
            {
                if (AddPointRow(workspace, context, clickWorld, out string addedRowId))
                {
                    BuildEntries(context, EntriesScratch, SplinePointIndexesScratch);
                    AutoSetSplinePointTangents(workspace, context);
                    state.SelectedTableId = context.PointsTable.Id;
                    state.SelectedRowId = addedRowId;
                }

                return;
            }

            if (state.ActiveTool == ActiveToolKind.PlaceEntity)
            {
                if (AddEntityRow(
                        workspace,
                        context,
                        clickWorld,
                        state.ActiveEntityTableId,
                        state.ActiveEntityRowId,
                        out string addedRowId))
                {
                    state.SelectedTableId = context.EntitiesTable.Id;
                    state.SelectedRowId = addedRowId;
                }

                return;
            }
        }

        if (state.DragKind == DragTargetKind.None)
        {
            return;
        }

        if (!canEdit || !input.MouseDown)
        {
            state.DragKind = DragTargetKind.None;
            state.DraggedTableId = "";
            state.DraggedRowId = "";
            return;
        }

        Vector2 mouseWorld = ScreenToWorld(mouseScreen, state, canvasRect);
        if (state.DragKind == DragTargetKind.EntityMarker)
        {
            if (!TryFindRowById(context.EntitiesTable, state.DraggedRowId, out DocRow draggedEntityRow))
            {
                return;
            }

            if (TryProjectPointToClosedSpline(EntriesScratch, SplinePointIndexesScratch, mouseWorld, out float projectedParamT, out Vector2 projectedPosition))
            {
                float normalizedParamT = NormalizeParamT(projectedParamT);
                SetNumberCell(workspace, context.EntitiesTable, draggedEntityRow, context.EntitiesSchema.ParamTColumn, normalizedParamT);
                SetVec2Cell(workspace, context.EntitiesTable, draggedEntityRow, context.EntitiesSchema.PositionColumn, projectedPosition);
            }

            return;
        }

        if (!TryFindRowById(context.PointsTable, state.DraggedRowId, out DocRow draggedPointRow))
        {
            return;
        }

        Vector2 currentPointPosition = ReadVec2(draggedPointRow, context.PointsSchema.PositionColumn);
        if (state.DragKind == DragTargetKind.Point)
        {
            Vector2 nextPosition = mouseWorld - state.DragPointerOffset;
            SetVec2Cell(workspace, context.PointsTable, draggedPointRow, context.PointsSchema.PositionColumn, nextPosition);
            return;
        }

        if (state.DragKind == DragTargetKind.InTangent)
        {
            SetVec2Cell(workspace, context.PointsTable, draggedPointRow, context.PointsSchema.TangentInColumn, mouseWorld - currentPointPosition);
            return;
        }

        if (state.DragKind == DragTargetKind.OutTangent)
        {
            SetVec2Cell(workspace, context.PointsTable, draggedPointRow, context.PointsSchema.TangentOutColumn, mouseWorld - currentPointPosition);
        }
    }

    private static void DrawEntityContextMenu(
        DocWorkspace workspace,
        LevelContext context,
        ViewState state,
        bool canEdit)
    {
        if (!ImContextMenu.Begin(EntityContextMenuId))
        {
            return;
        }

        bool hasContextEntity = !string.IsNullOrWhiteSpace(state.ContextMenuEntityRowId);
        if (!hasContextEntity ||
            !TryFindRowById(context.EntitiesTable, state.ContextMenuEntityRowId, out DocRow contextRow))
        {
            ImContextMenu.ItemDisabled("Entity");
            ImContextMenu.End();
            return;
        }

        ImContextMenu.ItemDisabled(contextRow.Id);
        ImContextMenu.Separator();

        if (canEdit)
        {
            if (ImContextMenu.Item("Delete entity"))
            {
                state.SelectedTableId = context.EntitiesTable.Id;
                state.SelectedRowId = contextRow.Id;
                _ = TryDeleteSelectedEntity(workspace, context, state);
            }
        }
        else
        {
            ImContextMenu.ItemDisabled("Delete entity");
        }

        ImContextMenu.End();
    }

    private static bool TryDeleteSelectedEntity(
        DocWorkspace workspace,
        LevelContext context,
        ViewState state)
    {
        if (!TryFindEntryBySource(EntriesScratch, state.SelectedTableId, state.SelectedRowId, out EntryVisual selectedEntry) ||
            selectedEntry.Kind != EntryKind.Entity ||
            !TryFindRowById(context.EntitiesTable, selectedEntry.RowId, out DocRow entityRow))
        {
            return false;
        }

        int entityRowIndex = context.EntitiesTable.Rows.IndexOf(entityRow);
        if (entityRowIndex < 0)
        {
            return false;
        }

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.RemoveRow,
            TableId = context.EntitiesTable.Id,
            RowIndex = entityRowIndex,
            RowSnapshot = entityRow,
        });

        state.SelectedTableId = "";
        state.SelectedRowId = "";
        state.ContextMenuEntityRowId = "";
        if (state.DragKind == DragTargetKind.EntityMarker &&
            string.Equals(state.DraggedRowId, selectedEntry.RowId, StringComparison.Ordinal))
        {
            state.DragKind = DragTargetKind.None;
            state.DraggedTableId = "";
            state.DraggedRowId = "";
        }

        workspace.SetStatusMessage("Entity deleted.");
        return true;
    }

    private static bool AddPointRow(
        DocWorkspace workspace,
        LevelContext context,
        Vector2 worldPosition,
        out string addedRowId)
    {
        addedRowId = "";
        double nextOrder = 1d;
        for (int rowIndex = 0; rowIndex < context.PointsTable.Rows.Count; rowIndex++)
        {
            DocRow row = context.PointsTable.Rows[rowIndex];
            if (context.PointsSchema.ParentRowColumn != null &&
                !string.Equals(row.GetCell(context.PointsSchema.ParentRowColumn).StringValue ?? "", context.LevelRow.Id, StringComparison.Ordinal))
            {
                continue;
            }

            nextOrder = Math.Max(nextOrder, row.GetCell(context.PointsSchema.OrderColumn).NumberValue + 1d);
        }

        var newRow = new DocRow();
        if (context.PointsSchema.ParentRowColumn != null)
        {
            newRow.SetCell(context.PointsSchema.ParentRowColumn.Id, DocCellValue.Text(context.LevelRow.Id));
        }

        newRow.SetCell(context.PointsSchema.OrderColumn.Id, DocCellValue.Number(nextOrder));
        newRow.SetCell(context.PointsSchema.PositionColumn.Id, DocCellValue.Vec2(worldPosition.X, worldPosition.Y));
        newRow.SetCell(context.PointsSchema.TangentInColumn.Id, DocCellValue.Vec2(-DefaultPointHandleLength, 0d));
        newRow.SetCell(context.PointsSchema.TangentOutColumn.Id, DocCellValue.Vec2(DefaultPointHandleLength, 0d));

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.AddRow,
            TableId = context.PointsTable.Id,
            RowIndex = context.PointsTable.Rows.Count,
            RowSnapshot = newRow,
        });

        addedRowId = newRow.Id;
        return true;
    }

    private static bool AddEntityRow(
        DocWorkspace workspace,
        LevelContext context,
        Vector2 worldPosition,
        string entityTableId,
        string entityRowId,
        out string addedRowId)
    {
        addedRowId = "";
        if (string.IsNullOrWhiteSpace(entityTableId))
        {
            workspace.SetStatusMessage("Select an entity tool before placing entities.");
            return false;
        }

        string resolvedEntityRowId = entityRowId;
        if (string.IsNullOrWhiteSpace(resolvedEntityRowId))
        {
            resolvedEntityRowId = ResolveDefaultEntityRowId(workspace.Project, entityTableId);
        }

        double nextOrder = 1d;
        for (int rowIndex = 0; rowIndex < context.EntitiesTable.Rows.Count; rowIndex++)
        {
            DocRow row = context.EntitiesTable.Rows[rowIndex];
            if (context.EntitiesSchema.ParentRowColumn != null &&
                !string.Equals(row.GetCell(context.EntitiesSchema.ParentRowColumn).StringValue ?? "", context.LevelRow.Id, StringComparison.Ordinal))
            {
                continue;
            }

            nextOrder = Math.Max(nextOrder, row.GetCell(context.EntitiesSchema.OrderColumn).NumberValue + 1d);
        }

        double paramTValue = 0d;
        Vector2 resolvedPosition = worldPosition;
        if (TryProjectPointToClosedSpline(EntriesScratch, SplinePointIndexesScratch, worldPosition, out float projectedParamT, out Vector2 projectedWorld))
        {
            paramTValue = NormalizeParamT(projectedParamT);
            resolvedPosition = projectedWorld;
        }
        else if (TrySampleClosedSplineAtNormalizedT(EntriesScratch, SplinePointIndexesScratch, 0f, out Vector2 sampledWorld))
        {
            paramTValue = 0d;
            resolvedPosition = sampledWorld;
        }

        var newRow = new DocRow();
        if (context.EntitiesSchema.ParentRowColumn != null)
        {
            newRow.SetCell(context.EntitiesSchema.ParentRowColumn.Id, DocCellValue.Text(context.LevelRow.Id));
        }

        newRow.SetCell(context.EntitiesSchema.OrderColumn.Id, DocCellValue.Number(nextOrder));
        newRow.SetCell(context.EntitiesSchema.ParamTColumn.Id, DocCellValue.Number(paramTValue));
        newRow.SetCell(context.EntitiesSchema.PositionColumn.Id, DocCellValue.Vec2(resolvedPosition.X, resolvedPosition.Y));
        newRow.SetCell(context.EntitiesSchema.EntityTableColumn.Id, DocCellValue.Text(entityTableId));
        newRow.SetCell(context.EntitiesSchema.EntityRowIdColumn.Id, DocCellValue.Text(resolvedEntityRowId));
        newRow.SetCell(context.EntitiesSchema.DataJsonColumn.Id, DocCellValue.Text("{}"));

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.AddRow,
            TableId = context.EntitiesTable.Id,
            RowIndex = context.EntitiesTable.Rows.Count,
            RowSnapshot = newRow,
        });

        addedRowId = newRow.Id;
        return true;
    }

    private static void AutoSetSplinePointTangents(DocWorkspace workspace, LevelContext context)
    {
        BuildEntries(context, EntriesScratch, SplinePointIndexesScratch);
        int splinePointCount = SplinePointIndexesScratch.Count;
        if (splinePointCount < 2)
        {
            return;
        }

        var commands = new List<DocCommand>(splinePointCount * 2);
        for (int splinePointListIndex = 0; splinePointListIndex < splinePointCount; splinePointListIndex++)
        {
            int currentEntryIndex = SplinePointIndexesScratch[splinePointListIndex];
            EntryVisual currentPoint = EntriesScratch[currentEntryIndex];
            if (!TryFindRowById(context.PointsTable, currentPoint.RowId, out DocRow row))
            {
                continue;
            }

            Vector2 tangentDirection;
            if (splinePointCount == 2)
            {
                int otherEntryIndex = SplinePointIndexesScratch[(splinePointListIndex + 1) % splinePointCount];
                EntryVisual otherPoint = EntriesScratch[otherEntryIndex];
                tangentDirection = (otherPoint.Position - currentPoint.Position) * 0.35f;
            }
            else
            {
                int previousEntryIndex = SplinePointIndexesScratch[(splinePointListIndex - 1 + splinePointCount) % splinePointCount];
                int nextEntryIndex = SplinePointIndexesScratch[(splinePointListIndex + 1) % splinePointCount];
                EntryVisual previousPoint = EntriesScratch[previousEntryIndex];
                EntryVisual nextPoint = EntriesScratch[nextEntryIndex];
                tangentDirection = (nextPoint.Position - previousPoint.Position) * 0.25f;
            }

            QueueVec2CellCommand(context.PointsTable, row, context.PointsSchema.TangentInColumn, -tangentDirection, commands);
            QueueVec2CellCommand(context.PointsTable, row, context.PointsSchema.TangentOutColumn, tangentDirection, commands);
        }

        if (commands.Count > 0)
        {
            workspace.ExecuteCommands(commands);
        }
    }

    private static void QueueVec2CellCommand(
        DocTable table,
        DocRow row,
        DocColumn column,
        Vector2 newValue,
        List<DocCommand> destinationCommands)
    {
        DocCellValue oldCell = row.GetCell(column);
        if (Math.Abs(oldCell.XValue - newValue.X) < 0.0001d &&
            Math.Abs(oldCell.YValue - newValue.Y) < 0.0001d)
        {
            return;
        }

        destinationCommands.Add(new DocCommand
        {
            Kind = DocCommandKind.SetCell,
            TableId = table.Id,
            RowId = row.Id,
            ColumnId = column.Id,
            OldCellValue = oldCell,
            NewCellValue = DocCellValue.Vec2(newValue.X, newValue.Y),
        });
    }

    private static void DrawCanvasBackground(ImRect canvasRect, ViewState state)
    {
        Im.DrawRoundedRect(canvasRect.X, canvasRect.Y, canvasRect.Width, canvasRect.Height, 4f, Im.Style.Background);
        Im.DrawRoundedRectStroke(canvasRect.X, canvasRect.Y, canvasRect.Width, canvasRect.Height, 4f, Im.Style.Border, 1f);

        float gridStepScreen = GridStepWorld * state.Zoom;
        if (gridStepScreen < 8f)
        {
            return;
        }

        uint gridColor = ImStyle.WithAlpha(Im.Style.Border, 80);
        float startX = canvasRect.X + Modulo(state.Pan.X, gridStepScreen);
        for (float lineX = startX; lineX <= canvasRect.Right; lineX += gridStepScreen)
        {
            Im.DrawLine(lineX, canvasRect.Y, lineX, canvasRect.Bottom, 1f, gridColor);
        }

        float startY = canvasRect.Y + Modulo(state.Pan.Y, gridStepScreen);
        for (float lineY = startY; lineY <= canvasRect.Bottom; lineY += gridStepScreen)
        {
            Im.DrawLine(canvasRect.X, lineY, canvasRect.Right, lineY, 1f, gridColor);
        }
    }

    private static float Modulo(float value, float divisor)
    {
        if (Math.Abs(divisor) <= float.Epsilon)
        {
            return 0f;
        }

        float modulo = value % divisor;
        if (modulo < 0f)
        {
            modulo += divisor;
        }

        return modulo;
    }

    private static void DrawLevelGeometry(
        DocWorkspace workspace,
        ImRect canvasRect,
        ViewState state,
        List<EntryVisual> entries,
        List<int> splinePointIndexes,
        string selectedTableId,
        string selectedRowId)
    {
        if (entries.Count <= 0)
        {
            return;
        }

        Im.PushClipRect(canvasRect);

        uint splineColor = ImStyle.WithAlpha(Im.Style.Primary, 230);
        uint tangentColor = ImStyle.WithAlpha(Im.Style.TextSecondary, 170);
        uint pointColor = 0xFFFFFFFF;

        if (splinePointIndexes.Count > 1)
        {
            for (int segmentIndex = 0; segmentIndex < splinePointIndexes.Count; segmentIndex++)
            {
                EntryVisual startPoint = entries[splinePointIndexes[segmentIndex]];
                EntryVisual endPoint = entries[splinePointIndexes[(segmentIndex + 1) % splinePointIndexes.Count]];
                DrawSplineSegment(startPoint, endPoint, canvasRect, state, splineColor);
            }
        }

        bool showSplineControls = ShouldShowTangentsForActiveTool(state);
        if (showSplineControls)
        {
            for (int splinePointIndex = 0; splinePointIndex < splinePointIndexes.Count; splinePointIndex++)
            {
                EntryVisual pointEntry = entries[splinePointIndexes[splinePointIndex]];
                Vector2 pointScreen = WorldToScreen(pointEntry.Position, state, canvasRect);
                bool isSelectedPoint = string.Equals(pointEntry.SourceTableId, selectedTableId, StringComparison.Ordinal) &&
                                       string.Equals(pointEntry.RowId, selectedRowId, StringComparison.Ordinal);

                if (isSelectedPoint)
                {
                    Vector2 inHandleScreen = WorldToScreen(pointEntry.Position + pointEntry.TangentIn, state, canvasRect);
                    Vector2 outHandleScreen = WorldToScreen(pointEntry.Position + pointEntry.TangentOut, state, canvasRect);

                    Im.DrawLine(pointScreen.X, pointScreen.Y, inHandleScreen.X, inHandleScreen.Y, 1f, tangentColor);
                    Im.DrawLine(pointScreen.X, pointScreen.Y, outHandleScreen.X, outHandleScreen.Y, 1f, tangentColor);
                    Im.DrawCircle(inHandleScreen.X, inHandleScreen.Y, 3f, tangentColor);
                    Im.DrawCircle(outHandleScreen.X, outHandleScreen.Y, 3f, tangentColor);
                }

                Im.DrawCircle(pointScreen.X, pointScreen.Y, 5f, pointColor);
                if (isSelectedPoint)
                {
                    Im.DrawCircleStroke(pointScreen.X, pointScreen.Y, 8f, Im.Style.Primary, 2f, 0f);
                }
            }
        }

        Vector2 splineCentroid = ComputeSplineCentroid(entries, splinePointIndexes);
        if (state.ShowDebugNormals && splinePointIndexes.Count > 1)
        {
            DrawSplineNormalsDebug(canvasRect, state, entries, splinePointIndexes, splineCentroid);
        }

        for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            EntryVisual entry = entries[entryIndex];
            if (entry.Kind != EntryKind.Entity)
            {
                continue;
            }

            Vector2 markerWorldPosition = ResolveEntryWorldPosition(entry, entries, splinePointIndexes);
            Vector2 markerScreen = WorldToScreen(markerWorldPosition, state, canvasRect);
            bool markerSelected = string.Equals(entry.SourceTableId, selectedTableId, StringComparison.Ordinal) &&
                                  string.Equals(entry.RowId, selectedRowId, StringComparison.Ordinal);

            bool drewUiMarker = DrawEntityUiMarker(
                workspace,
                entry,
                markerWorldPosition,
                markerScreen,
                state.Zoom,
                splineCentroid,
                entries,
                splinePointIndexes,
                markerSelected);

            if (!drewUiMarker)
            {
                uint markerColor = markerSelected ? Im.Style.Primary : ImStyle.WithAlpha(Im.Style.Secondary, 230);
                Im.DrawRoundedRect(
                    markerScreen.X - EntityFallbackSize * 0.5f,
                    markerScreen.Y - EntityFallbackSize * 0.5f,
                    EntityFallbackSize,
                    EntityFallbackSize,
                    2f,
                    markerColor);
                Im.DrawRoundedRectStroke(
                    markerScreen.X - EntityFallbackSize * 0.5f,
                    markerScreen.Y - EntityFallbackSize * 0.5f,
                    EntityFallbackSize,
                    EntityFallbackSize,
                    2f,
                    Im.Style.Border,
                    markerSelected ? 2f : 1f);
            }
        }

        Im.PopClipRect();
    }

    private static bool DrawEntityUiMarker(
        DocWorkspace workspace,
        EntryVisual entry,
        Vector2 markerWorldPosition,
        Vector2 markerScreen,
        float zoom,
        Vector2 splineCentroid,
        List<EntryVisual> entries,
        List<int> splinePointIndexes,
        bool selected,
        uint tint = 0xFFFFFFFF)
    {
        Vector2 facingDirection = ResolveEntityFacingDirection(
            entry,
            markerWorldPosition,
            splineCentroid,
            entries,
            splinePointIndexes);

        if (!TryResolveEntityMarkerVisual(
                workspace,
                entry,
                markerWorldPosition,
                markerScreen,
                zoom,
                facingDirection,
                out EntityMarkerVisual markerVisual))
        {
            return false;
        }

        Im.DrawImage(
            markerVisual.Texture,
            markerVisual.DrawX,
            markerVisual.DrawY,
            markerVisual.DrawWidth,
            markerVisual.DrawHeight,
            new Rectangle(0f, 0f, markerVisual.Texture.Width, markerVisual.Texture.Height),
            tint,
            markerVisual.RotationRadians);

        if (selected)
        {
            Im.DrawRoundedRectStroke(
                markerVisual.DrawX - 1f,
                markerVisual.DrawY - 1f,
                markerVisual.DrawWidth + 2f,
                markerVisual.DrawHeight + 2f,
                4f,
                Im.Style.Primary,
                2f);
        }

        return true;
    }

    private static void DrawEntityPlacementPreview(
        DocWorkspace workspace,
        ImRect canvasRect,
        ViewState state,
        List<EntryVisual> entries,
        List<int> splinePointIndexes,
        Vector2 mouseScreen,
        bool canEdit,
        bool pointerBlockedByFlyout)
    {
        if (!canEdit ||
            state.ActiveTool != ActiveToolKind.PlaceEntity ||
            pointerBlockedByFlyout ||
            !canvasRect.Contains(mouseScreen) ||
            splinePointIndexes.Count <= 1 ||
            string.IsNullOrWhiteSpace(state.ActiveEntityTableId))
        {
            return;
        }

        Vector2 mouseWorld = ScreenToWorld(mouseScreen, state, canvasRect);
        if (!TryProjectPointToClosedSpline(entries, splinePointIndexes, mouseWorld, out float projectedParamT, out Vector2 projectedPosition))
        {
            return;
        }

        ResolveEntityDefinition(
            workspace.Project,
            state.ActiveEntityTableId,
            state.ActiveEntityRowId,
            out string displayLabel,
            out string uiAssetPath,
            out string resolvedEntityRowId,
            out float uiScale);

        string previewEntityRowId = string.IsNullOrWhiteSpace(state.ActiveEntityRowId)
            ? resolvedEntityRowId
            : state.ActiveEntityRowId;
        EntryVisual previewEntry = new EntryVisual(
            state.ActiveEntityTableId,
            previewEntityRowId,
            EntryKind.Entity,
            0d,
            NormalizeParamT(projectedParamT),
            projectedPosition,
            Vector2.Zero,
            Vector2.Zero,
            state.ActiveEntityTableId,
            previewEntityRowId,
            "",
            displayLabel,
            uiAssetPath,
            uiScale);

        Vector2 previewScreen = WorldToScreen(projectedPosition, state, canvasRect);
        Vector2 splineCentroid = ComputeSplineCentroid(entries, splinePointIndexes);
        bool drewUiMarker = DrawEntityUiMarker(
            workspace,
            previewEntry,
            projectedPosition,
            previewScreen,
            state.Zoom,
            splineCentroid,
            entries,
            splinePointIndexes,
            selected: false,
            tint: 0xBFFFFFFF);

        if (!drewUiMarker)
        {
            float halfSize = EntityFallbackSize * 0.5f;
            Im.DrawRoundedRect(
                previewScreen.X - halfSize,
                previewScreen.Y - halfSize,
                EntityFallbackSize,
                EntityFallbackSize,
                2f,
                ImStyle.WithAlpha(Im.Style.Secondary, 180));
            Im.DrawRoundedRectStroke(
                previewScreen.X - halfSize,
                previewScreen.Y - halfSize,
                EntityFallbackSize,
                EntityFallbackSize,
                2f,
                ImStyle.WithAlpha(Im.Style.Border, 220),
                1f);
        }

        Im.DrawCircleStroke(
            previewScreen.X,
            previewScreen.Y,
            8f,
            ImStyle.WithAlpha(Im.Style.Primary, 200),
            1.5f,
            0f);
    }

    private static bool TryResolveEntityMarkerVisual(
        DocWorkspace workspace,
        EntryVisual entry,
        Vector2 markerWorldPosition,
        Vector2 markerScreen,
        float zoom,
        Vector2 facingDirection,
        out EntityMarkerVisual markerVisual)
    {
        markerVisual = default;

        if (string.IsNullOrWhiteSpace(entry.UiAssetPath) ||
            (string.IsNullOrWhiteSpace(workspace.GameRoot) && string.IsNullOrWhiteSpace(workspace.AssetsRoot)))
        {
            return false;
        }

        float uiScale = float.IsFinite(entry.UiScale) && entry.UiScale > 0f
            ? entry.UiScale
            : DefaultEntityUiScale;

        DerpUiPreviewCache.PreviewResult prefabSizedPreview = DocAssetServices.DerpUiPreviewCache.GetPreview(
            workspace.GameRoot,
            workspace.AssetsRoot,
            entry.UiAssetPath,
            DerpUiPreviewCache.PreviewRenderMode.PrefabSize);

        var previewTexture = default(DerpLib.Rendering.Texture);
        Vector2 prefabSize = Vector2.Zero;
        bool hasPrefabSizedPreview = prefabSizedPreview.Status == DerpUiPreviewCache.PreviewStatus.Ready &&
                                     prefabSizedPreview.Texture.Width > 0 &&
                                     prefabSizedPreview.Texture.Height > 0;
        if (hasPrefabSizedPreview)
        {
            previewTexture = prefabSizedPreview.Texture;
            prefabSize = prefabSizedPreview.PrefabSize;
            if (prefabSize.X <= 0f || prefabSize.Y <= 0f)
            {
                prefabSize = new Vector2(previewTexture.Width, previewTexture.Height);
            }

            DerpUiPreviewCache.PreviewRenderMode adaptiveMode = ResolveAdaptivePreviewRenderMode(zoom * uiScale);
            if (adaptiveMode != DerpUiPreviewCache.PreviewRenderMode.PrefabSize)
            {
                if (TryGetPreviewTextureForMode(workspace, entry.UiAssetPath, adaptiveMode, out DerpLib.Rendering.Texture adaptiveTexture))
                {
                    previewTexture = adaptiveTexture;
                }
                else if (adaptiveMode == DerpUiPreviewCache.PreviewRenderMode.PrefabQuarter &&
                         TryGetPreviewTextureForMode(workspace, entry.UiAssetPath, DerpUiPreviewCache.PreviewRenderMode.PrefabHalf, out DerpLib.Rendering.Texture halfTexture))
                {
                    previewTexture = halfTexture;
                }
            }
        }
        else
        {
            DerpUiPreviewCache.PreviewResult thumbnailPreview = DocAssetServices.DerpUiPreviewCache.GetPreview(
                workspace.GameRoot,
                workspace.AssetsRoot,
                entry.UiAssetPath,
                DerpUiPreviewCache.PreviewRenderMode.Thumbnail);
            if (thumbnailPreview.Status != DerpUiPreviewCache.PreviewStatus.Ready ||
                thumbnailPreview.Texture.Width <= 0 ||
                thumbnailPreview.Texture.Height <= 0)
            {
                return false;
            }

            previewTexture = thumbnailPreview.Texture;
            prefabSize = thumbnailPreview.PrefabSize;
        }

        if (previewTexture.Width <= 0 || previewTexture.Height <= 0)
        {
            return false;
        }

        if (prefabSize.X <= 0f || prefabSize.Y <= 0f)
        {
            prefabSize = new Vector2(previewTexture.Width, previewTexture.Height);
        }

        float drawWidth = MathF.Max(1f, prefabSize.X * zoom * uiScale);
        float drawHeight = MathF.Max(1f, prefabSize.Y * zoom * uiScale);
        float drawX = markerScreen.X - drawWidth * 0.5f;
        float drawY = markerScreen.Y - drawHeight * 0.5f;

        Vector2 normalizedFacingDirection = facingDirection;
        float facingLengthSquared = normalizedFacingDirection.LengthSquared();
        if (facingLengthSquared > 0.0001f)
        {
            normalizedFacingDirection /= MathF.Sqrt(facingLengthSquared);
        }
        else
        {
            normalizedFacingDirection = new Vector2(0f, -1f);
        }

        float rotationRadians = MathF.Atan2(normalizedFacingDirection.X, -normalizedFacingDirection.Y);
        markerVisual = new EntityMarkerVisual(
            previewTexture,
            markerScreen,
            drawX,
            drawY,
            drawWidth,
            drawHeight,
            rotationRadians);
        return true;
    }

    private static DerpUiPreviewCache.PreviewRenderMode ResolveAdaptivePreviewRenderMode(float drawScale)
    {
        if (!float.IsFinite(drawScale) || drawScale <= 0f)
        {
            return DerpUiPreviewCache.PreviewRenderMode.PrefabSize;
        }

        if (drawScale <= AdaptiveQuarterPreviewScaleThreshold)
        {
            return DerpUiPreviewCache.PreviewRenderMode.PrefabQuarter;
        }

        if (drawScale <= AdaptiveHalfPreviewScaleThreshold)
        {
            return DerpUiPreviewCache.PreviewRenderMode.PrefabHalf;
        }

        return DerpUiPreviewCache.PreviewRenderMode.PrefabSize;
    }

    private static bool TryGetPreviewTextureForMode(
        DocWorkspace workspace,
        string uiAssetPath,
        DerpUiPreviewCache.PreviewRenderMode renderMode,
        out DerpLib.Rendering.Texture texture)
    {
        texture = default;
        DerpUiPreviewCache.PreviewResult preview = DocAssetServices.DerpUiPreviewCache.GetPreview(
            workspace.GameRoot,
            workspace.AssetsRoot,
            uiAssetPath,
            renderMode);

        if (preview.Status != DerpUiPreviewCache.PreviewStatus.Ready ||
            preview.Texture.Width <= 0 ||
            preview.Texture.Height <= 0)
        {
            return false;
        }

        texture = preview.Texture;
        return true;
    }

    private static bool IsPointInsideRotatedRect(
        Vector2 pointScreen,
        Vector2 rectCenterScreen,
        float rectWidth,
        float rectHeight,
        float rotationRadians)
    {
        if (rectWidth <= 0f || rectHeight <= 0f)
        {
            return false;
        }

        Vector2 delta = pointScreen - rectCenterScreen;
        float sin = MathF.Sin(rotationRadians);
        float cos = MathF.Cos(rotationRadians);
        Vector2 localPoint = new Vector2(
            delta.X * cos + delta.Y * sin,
            -delta.X * sin + delta.Y * cos);

        float halfWidth = rectWidth * 0.5f;
        float halfHeight = rectHeight * 0.5f;
        return MathF.Abs(localPoint.X) <= halfWidth &&
               MathF.Abs(localPoint.Y) <= halfHeight;
    }

    private static Vector2 ComputeSplineCentroid(List<EntryVisual> entries, List<int> splinePointIndexes)
    {
        if (splinePointIndexes.Count <= 0)
        {
            return Vector2.Zero;
        }

        Vector2 sum = Vector2.Zero;
        for (int pointIndex = 0; pointIndex < splinePointIndexes.Count; pointIndex++)
        {
            sum += entries[splinePointIndexes[pointIndex]].Position;
        }

        return sum / splinePointIndexes.Count;
    }

    private static void DrawSplineSegment(
        EntryVisual startPoint,
        EntryVisual endPoint,
        ImRect canvasRect,
        ViewState state,
        uint color)
    {
        Vector2 p0 = startPoint.Position;
        Vector2 p1 = startPoint.Position + startPoint.TangentOut;
        Vector2 p2 = endPoint.Position + endPoint.TangentIn;
        Vector2 p3 = endPoint.Position;

        for (int curvePointIndex = 0; curvePointIndex <= CurveSegments; curvePointIndex++)
        {
            float t = curvePointIndex / (float)CurveSegments;
            Vector2 worldPoint = EvaluateCubicBezier(p0, p1, p2, p3, t);
            CurvePointScratch[curvePointIndex] = WorldToScreen(worldPoint, state, canvasRect);
        }

        Im.DrawPolyline(CurvePointScratch.AsSpan(0, CurveSegments + 1), 2f, color);
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

    private static Vector2 EvaluateCubicBezierDerivative(
        in Vector2 p0,
        in Vector2 p1,
        in Vector2 p2,
        in Vector2 p3,
        float t)
    {
        float clampedT = Math.Clamp(t, 0f, 1f);
        float oneMinusT = 1f - clampedT;
        Vector2 firstTerm = 3f * oneMinusT * oneMinusT * (p1 - p0);
        Vector2 secondTerm = 6f * oneMinusT * clampedT * (p2 - p1);
        Vector2 thirdTerm = 3f * clampedT * clampedT * (p3 - p2);
        return firstTerm + secondTerm + thirdTerm;
    }

    private static void DrawSplineNormalsDebug(
        ImRect canvasRect,
        ViewState state,
        List<EntryVisual> entries,
        List<int> splinePointIndexes,
        Vector2 splineCentroid)
    {
        int segmentCount = splinePointIndexes.Count;
        if (segmentCount < 2 || DebugNormalSamplesPerSegment <= 0)
        {
            return;
        }

        int totalSamples = segmentCount * DebugNormalSamplesPerSegment;
        if (totalSamples <= 0)
        {
            return;
        }

        uint inwardColor = 0xCC66FF66;
        uint outwardColor = 0xCCFF6666;
        float outwardLength = DebugNormalLengthWorld * 0.65f;
        for (int sampleIndex = 0; sampleIndex < totalSamples; sampleIndex++)
        {
            float normalizedParamT = sampleIndex / (float)totalSamples;
            if (!TrySampleClosedSplineFrameAtNormalizedT(
                    entries,
                    splinePointIndexes,
                    normalizedParamT,
                    out Vector2 worldPosition,
                    out Vector2 tangentDirection))
            {
                continue;
            }

            if (!TryResolveSplineNormals(
                    worldPosition,
                    tangentDirection,
                    splineCentroid,
                    out Vector2 inwardNormal,
                    out Vector2 outwardNormal))
            {
                continue;
            }

            Vector2 inwardEnd = worldPosition + (inwardNormal * DebugNormalLengthWorld);
            Vector2 outwardEnd = worldPosition + (outwardNormal * outwardLength);

            Vector2 worldScreen = WorldToScreen(worldPosition, state, canvasRect);
            Vector2 inwardScreen = WorldToScreen(inwardEnd, state, canvasRect);
            Vector2 outwardScreen = WorldToScreen(outwardEnd, state, canvasRect);
            Im.DrawLine(worldScreen.X, worldScreen.Y, inwardScreen.X, inwardScreen.Y, 1f, inwardColor);
            Im.DrawLine(worldScreen.X, worldScreen.Y, outwardScreen.X, outwardScreen.Y, 1f, outwardColor);
        }
    }

    private static Vector2 ResolveEntityFacingDirection(
        EntryVisual entry,
        Vector2 markerWorldPosition,
        Vector2 splineCentroid,
        List<EntryVisual> entries,
        List<int> splinePointIndexes)
    {
        if (entry.Kind == EntryKind.Entity &&
            TrySampleClosedSplineFrameAtNormalizedT(
                entries,
                splinePointIndexes,
                (float)entry.ParamT,
                out _,
                out Vector2 tangentDirection) &&
            TryResolveSplineNormals(
                markerWorldPosition,
                tangentDirection,
                splineCentroid,
                out Vector2 inwardNormal,
                out _))
        {
            return inwardNormal;
        }

        Vector2 fallbackDirection = splineCentroid - markerWorldPosition;
        float fallbackLengthSquared = fallbackDirection.LengthSquared();
        if (fallbackLengthSquared > 0.0001f)
        {
            return fallbackDirection / MathF.Sqrt(fallbackLengthSquared);
        }

        return new Vector2(0f, -1f);
    }

    private static bool TryResolveSplineNormals(
        Vector2 worldPosition,
        Vector2 tangentDirection,
        Vector2 splineCentroid,
        out Vector2 inwardNormal,
        out Vector2 outwardNormal)
    {
        inwardNormal = default;
        outwardNormal = default;

        float tangentLengthSquared = tangentDirection.LengthSquared();
        if (tangentLengthSquared <= 0.0001f)
        {
            Vector2 centroidDirection = splineCentroid - worldPosition;
            float centroidLengthSquared = centroidDirection.LengthSquared();
            if (centroidLengthSquared <= 0.0001f)
            {
                return false;
            }

            inwardNormal = centroidDirection / MathF.Sqrt(centroidLengthSquared);
            outwardNormal = -inwardNormal;
            return true;
        }

        Vector2 tangentUnit = tangentDirection / MathF.Sqrt(tangentLengthSquared);
        Vector2 leftNormal = new(-tangentUnit.Y, tangentUnit.X);
        Vector2 rightNormal = new(tangentUnit.Y, -tangentUnit.X);
        Vector2 centroidVector = splineCentroid - worldPosition;
        float centroidVectorLengthSquared = centroidVector.LengthSquared();
        if (centroidVectorLengthSquared <= 0.0001f)
        {
            inwardNormal = leftNormal;
            outwardNormal = rightNormal;
            return true;
        }

        float leftDot = Vector2.Dot(leftNormal, centroidVector);
        float rightDot = Vector2.Dot(rightNormal, centroidVector);
        if (leftDot >= rightDot)
        {
            inwardNormal = leftNormal;
            outwardNormal = rightNormal;
        }
        else
        {
            inwardNormal = rightNormal;
            outwardNormal = leftNormal;
        }

        return true;
    }

    private static bool TrySampleClosedSplineFrameAtNormalizedT(
        List<EntryVisual> entries,
        List<int> splinePointIndexes,
        float normalizedParamT,
        out Vector2 worldPosition,
        out Vector2 tangentDirection)
    {
        if (!TryResolveClosedSplineSegmentAtNormalizedT(
                splinePointIndexes.Count,
                normalizedParamT,
                out int segmentIndex,
                out float localT))
        {
            worldPosition = default;
            tangentDirection = default;
            return false;
        }

        worldPosition = EvaluateClosedSplineSegmentPoint(entries, splinePointIndexes, segmentIndex, localT);
        tangentDirection = EvaluateClosedSplineSegmentTangent(entries, splinePointIndexes, segmentIndex, localT);
        return true;
    }

    private static void BuildEntries(
        LevelContext context,
        List<EntryVisual> destinationEntries,
        List<int> destinationSplinePointIndexes)
    {
        destinationEntries.Clear();
        destinationSplinePointIndexes.Clear();
        PointEntriesScratch.Clear();
        EntityEntriesScratch.Clear();

        for (int rowIndex = 0; rowIndex < context.PointsTable.Rows.Count; rowIndex++)
        {
            DocRow row = context.PointsTable.Rows[rowIndex];
            if (context.PointsSchema.ParentRowColumn != null &&
                !string.Equals(row.GetCell(context.PointsSchema.ParentRowColumn).StringValue ?? "", context.LevelRow.Id, StringComparison.Ordinal))
            {
                continue;
            }

            PointEntriesScratch.Add(new EntryVisual(
                context.PointsTable.Id,
                row.Id,
                EntryKind.Point,
                row.GetCell(context.PointsSchema.OrderColumn).NumberValue,
                0d,
                ReadVec2(row, context.PointsSchema.PositionColumn),
                ReadVec2(row, context.PointsSchema.TangentInColumn),
                ReadVec2(row, context.PointsSchema.TangentOutColumn),
                "",
                "",
                "",
                "SplinePoint",
                "",
                DefaultEntityUiScale));
        }

        PointEntriesScratch.Sort(static (left, right) =>
        {
            int orderComparison = left.Order.CompareTo(right.Order);
            if (orderComparison != 0)
            {
                return orderComparison;
            }

            return string.Compare(left.RowId, right.RowId, StringComparison.Ordinal);
        });

        for (int pointIndex = 0; pointIndex < PointEntriesScratch.Count; pointIndex++)
        {
            destinationSplinePointIndexes.Add(destinationEntries.Count);
            destinationEntries.Add(PointEntriesScratch[pointIndex]);
        }

        for (int rowIndex = 0; rowIndex < context.EntitiesTable.Rows.Count; rowIndex++)
        {
            DocRow row = context.EntitiesTable.Rows[rowIndex];
            if (context.EntitiesSchema.ParentRowColumn != null &&
                !string.Equals(row.GetCell(context.EntitiesSchema.ParentRowColumn).StringValue ?? "", context.LevelRow.Id, StringComparison.Ordinal))
            {
                continue;
            }

            string entityTableId = row.GetCell(context.EntitiesSchema.EntityTableColumn).StringValue ?? "";
            string entityRowId = row.GetCell(context.EntitiesSchema.EntityRowIdColumn).StringValue ?? "";
            ResolveEntityDefinition(
                context.Project,
                entityTableId,
                entityRowId,
                out string displayLabel,
                out string uiAssetPath,
                out string resolvedEntityRowId,
                out float uiScale);

            EntityEntriesScratch.Add(new EntryVisual(
                context.EntitiesTable.Id,
                row.Id,
                EntryKind.Entity,
                row.GetCell(context.EntitiesSchema.OrderColumn).NumberValue,
                row.GetCell(context.EntitiesSchema.ParamTColumn).NumberValue,
                ReadVec2(row, context.EntitiesSchema.PositionColumn),
                Vector2.Zero,
                Vector2.Zero,
                entityTableId,
                string.IsNullOrWhiteSpace(entityRowId) ? resolvedEntityRowId : entityRowId,
                row.GetCell(context.EntitiesSchema.DataJsonColumn).StringValue ?? "",
                displayLabel,
                uiAssetPath,
                uiScale));
        }

        EntityEntriesScratch.Sort(static (left, right) =>
        {
            int orderComparison = left.Order.CompareTo(right.Order);
            if (orderComparison != 0)
            {
                return orderComparison;
            }

            int paramTComparison = left.ParamT.CompareTo(right.ParamT);
            if (paramTComparison != 0)
            {
                return paramTComparison;
            }

            return string.Compare(left.RowId, right.RowId, StringComparison.Ordinal);
        });

        for (int entityIndex = 0; entityIndex < EntityEntriesScratch.Count; entityIndex++)
        {
            destinationEntries.Add(EntityEntriesScratch[entityIndex]);
        }
    }

    private static void ResolveHoveredSplineControl(
        DocWorkspace workspace,
        Vector2 mouseScreen,
        List<EntryVisual> entries,
        List<int> splinePointIndexes,
        ImRect canvasRect,
        ViewState state,
        bool showSplineControls,
        string selectedTableId,
        string selectedRowId,
        out int hoveredPointIndex,
        out int hoveredHandleIndex,
        out DragTargetKind hoveredHandleKind,
        out int hoveredEntityIndex)
    {
        hoveredPointIndex = -1;
        hoveredHandleIndex = -1;
        hoveredHandleKind = DragTargetKind.None;
        hoveredEntityIndex = -1;

        float bestHandleDistance = float.MaxValue;
        for (int pointListIndex = 0; pointListIndex < splinePointIndexes.Count; pointListIndex++)
        {
            int entryIndex = splinePointIndexes[pointListIndex];
            EntryVisual entry = entries[entryIndex];
            Vector2 pointScreen = WorldToScreen(entry.Position, state, canvasRect);
            bool isSelectedPoint = string.Equals(entry.SourceTableId, selectedTableId, StringComparison.Ordinal) &&
                                   string.Equals(entry.RowId, selectedRowId, StringComparison.Ordinal);
            if (showSplineControls && isSelectedPoint)
            {
                Vector2 inHandleScreen = WorldToScreen(entry.Position + entry.TangentIn, state, canvasRect);
                Vector2 outHandleScreen = WorldToScreen(entry.Position + entry.TangentOut, state, canvasRect);

                float inDistance = Vector2.DistanceSquared(mouseScreen, inHandleScreen);
                if (inDistance <= HandleHitRadius * HandleHitRadius && inDistance < bestHandleDistance)
                {
                    bestHandleDistance = inDistance;
                    hoveredHandleIndex = entryIndex;
                    hoveredHandleKind = DragTargetKind.InTangent;
                }

                float outDistance = Vector2.DistanceSquared(mouseScreen, outHandleScreen);
                if (outDistance <= HandleHitRadius * HandleHitRadius && outDistance < bestHandleDistance)
                {
                    bestHandleDistance = outDistance;
                    hoveredHandleIndex = entryIndex;
                    hoveredHandleKind = DragTargetKind.OutTangent;
                }
            }

            if (showSplineControls)
            {
                float pointDistance = Vector2.DistanceSquared(mouseScreen, pointScreen);
                if (pointDistance <= PointHitRadius * PointHitRadius)
                {
                    hoveredPointIndex = entryIndex;
                }
            }
        }

        Vector2 splineCentroid = ComputeSplineCentroid(entries, splinePointIndexes);
        float bestEntityDistance = float.MaxValue;
        for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            EntryVisual entry = entries[entryIndex];
            if (entry.Kind != EntryKind.Entity)
            {
                continue;
            }

            Vector2 markerWorld = ResolveEntryWorldPosition(entry, entries, splinePointIndexes);
            Vector2 markerScreen = WorldToScreen(markerWorld, state, canvasRect);
            Vector2 facingDirection = ResolveEntityFacingDirection(
                entry,
                markerWorld,
                splineCentroid,
                entries,
                splinePointIndexes);
            bool canUseUiBounds = TryResolveEntityMarkerVisual(
                workspace,
                entry,
                markerWorld,
                markerScreen,
                state.Zoom,
                facingDirection,
                out EntityMarkerVisual markerVisual);
            if (canUseUiBounds &&
                IsPointInsideRotatedRect(mouseScreen, markerVisual.CenterScreen, markerVisual.DrawWidth, markerVisual.DrawHeight, markerVisual.RotationRadians))
            {
                float markerDistance = Vector2.DistanceSquared(mouseScreen, markerScreen);
                if (markerDistance < bestEntityDistance)
                {
                    bestEntityDistance = markerDistance;
                    hoveredEntityIndex = entryIndex;
                }

                continue;
            }

            float fallbackDistance = Vector2.DistanceSquared(mouseScreen, markerScreen);
            if (fallbackDistance <= MarkerHitRadius * MarkerHitRadius && fallbackDistance < bestEntityDistance)
            {
                bestEntityDistance = fallbackDistance;
                hoveredEntityIndex = entryIndex;
            }
        }
    }

    private static Vector2 ResolveEntryWorldPosition(
        EntryVisual entry,
        List<EntryVisual> entries,
        List<int> splinePointIndexes)
    {
        if (entry.Kind == EntryKind.Entity &&
            TrySampleClosedSplineAtNormalizedT(entries, splinePointIndexes, (float)entry.ParamT, out Vector2 sampledPosition))
        {
            return sampledPosition;
        }

        return entry.Position;
    }

    private static bool TrySampleClosedSplineAtNormalizedT(
        List<EntryVisual> entries,
        List<int> splinePointIndexes,
        float normalizedParamT,
        out Vector2 worldPosition)
    {
        if (!TrySampleClosedSplineFrameAtNormalizedT(
                entries,
                splinePointIndexes,
                normalizedParamT,
                out worldPosition,
                out _))
        {
            return false;
        }

        return true;
    }

    private static bool TryResolveClosedSplineSegmentAtNormalizedT(
        int segmentCount,
        float normalizedParamT,
        out int segmentIndex,
        out float localT)
    {
        segmentIndex = 0;
        localT = 0f;
        if (segmentCount < 2)
        {
            return false;
        }

        float wrappedParamT = NormalizeParamT(normalizedParamT);
        float scaledParamT = wrappedParamT * segmentCount;
        segmentIndex = (int)MathF.Floor(scaledParamT);
        if (segmentIndex >= segmentCount)
        {
            segmentIndex = segmentCount - 1;
        }

        localT = scaledParamT - segmentIndex;
        return true;
    }

    private static bool TryProjectPointToClosedSpline(
        List<EntryVisual> entries,
        List<int> splinePointIndexes,
        Vector2 targetWorldPosition,
        out float normalizedParamT,
        out Vector2 projectedWorldPosition)
    {
        int segmentCount = splinePointIndexes.Count;
        if (segmentCount < 2)
        {
            normalizedParamT = 0f;
            projectedWorldPosition = default;
            return false;
        }

        float bestDistanceSquared = float.MaxValue;
        int bestSegmentIndex = 0;
        float bestLocalT = 0f;
        Vector2 bestWorldPoint = default;

        for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            for (int sampleIndex = 0; sampleIndex <= SplineProjectionSamplesPerSegment; sampleIndex++)
            {
                float localT = sampleIndex / (float)SplineProjectionSamplesPerSegment;
                Vector2 sampledWorldPoint = EvaluateClosedSplineSegmentPoint(entries, splinePointIndexes, segmentIndex, localT);
                float distanceSquared = Vector2.DistanceSquared(targetWorldPosition, sampledWorldPoint);
                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    bestSegmentIndex = segmentIndex;
                    bestLocalT = localT;
                    bestWorldPoint = sampledWorldPoint;
                }
            }
        }

        float localWindow = 1f / SplineProjectionSamplesPerSegment;
        float refineStart = Math.Max(0f, bestLocalT - localWindow);
        float refineEnd = Math.Min(1f, bestLocalT + localWindow);
        for (int refineIndex = 0; refineIndex <= SplineProjectionRefineSamples; refineIndex++)
        {
            float blend = refineIndex / (float)SplineProjectionRefineSamples;
            float localT = refineStart + ((refineEnd - refineStart) * blend);
            Vector2 sampledWorldPoint = EvaluateClosedSplineSegmentPoint(entries, splinePointIndexes, bestSegmentIndex, localT);
            float distanceSquared = Vector2.DistanceSquared(targetWorldPosition, sampledWorldPoint);
            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                bestLocalT = localT;
                bestWorldPoint = sampledWorldPoint;
            }
        }

        normalizedParamT = NormalizeParamT((bestSegmentIndex + bestLocalT) / segmentCount);
        projectedWorldPosition = bestWorldPoint;
        return true;
    }

    private static Vector2 EvaluateClosedSplineSegmentPoint(
        List<EntryVisual> entries,
        List<int> splinePointIndexes,
        int segmentIndex,
        float localT)
    {
        int segmentCount = splinePointIndexes.Count;
        EntryVisual startPoint = entries[splinePointIndexes[segmentIndex]];
        EntryVisual endPoint = entries[splinePointIndexes[(segmentIndex + 1) % segmentCount]];
        Vector2 p0 = startPoint.Position;
        Vector2 p1 = startPoint.Position + startPoint.TangentOut;
        Vector2 p2 = endPoint.Position + endPoint.TangentIn;
        Vector2 p3 = endPoint.Position;
        return EvaluateCubicBezier(p0, p1, p2, p3, localT);
    }

    private static Vector2 EvaluateClosedSplineSegmentTangent(
        List<EntryVisual> entries,
        List<int> splinePointIndexes,
        int segmentIndex,
        float localT)
    {
        int segmentCount = splinePointIndexes.Count;
        EntryVisual startPoint = entries[splinePointIndexes[segmentIndex]];
        EntryVisual endPoint = entries[splinePointIndexes[(segmentIndex + 1) % segmentCount]];
        Vector2 p0 = startPoint.Position;
        Vector2 p1 = startPoint.Position + startPoint.TangentOut;
        Vector2 p2 = endPoint.Position + endPoint.TangentIn;
        Vector2 p3 = endPoint.Position;
        return EvaluateCubicBezierDerivative(p0, p1, p2, p3, localT);
    }

    private static float NormalizeParamT(float paramT)
    {
        if (!float.IsFinite(paramT))
        {
            return 0f;
        }

        float wrapped = paramT % 1f;
        if (wrapped < 0f)
        {
            wrapped += 1f;
        }

        if (wrapped >= 1f)
        {
            wrapped = 0f;
        }

        return wrapped;
    }

    private static bool ShouldShowTangentsForActiveTool(ViewState state)
    {
        return state.ActiveTool == ActiveToolKind.Select ||
               state.ActiveTool == ActiveToolKind.Pen;
    }

    private static void BuildToolGroups(
        DocWorkspace workspace,
        LevelContext context,
        List<ToolGroup> destination)
    {
        destination.Clear();

        DocTable? toolsTable = ResolveToolsTable(workspace.Project, context);
        if (toolsTable == null)
        {
            return;
        }

        DocColumn? toolNameColumn = ResolveToolNameColumn(toolsTable);
        DocColumn? toolTableColumn = ResolveToolEntitiesTableColumn(toolsTable);
        if (toolTableColumn == null)
        {
            return;
        }

        for (int toolRowIndex = 0; toolRowIndex < toolsTable.Rows.Count; toolRowIndex++)
        {
            DocRow toolRow = toolsTable.Rows[toolRowIndex];
            string entitiesTableId = toolRow.GetCell(toolTableColumn).StringValue ?? "";
            if (string.IsNullOrWhiteSpace(entitiesTableId))
            {
                continue;
            }

            DocTable? entitiesDefinitionTable = FindTableById(workspace.Project, entitiesTableId);
            if (entitiesDefinitionTable == null)
            {
                continue;
            }

            string toolName = toolNameColumn != null
                ? (toolRow.GetCell(toolNameColumn).StringValue ?? "")
                : "";
            if (string.IsNullOrWhiteSpace(toolName))
            {
                toolName = entitiesDefinitionTable.Name;
            }

            var group = new ToolGroup
            {
                RowId = toolRow.Id,
                Name = toolName,
                EntitiesTableId = entitiesTableId,
                EntitiesTableName = entitiesDefinitionTable.Name,
            };

            DocColumn? uiAssetColumn = ResolveEntityUiAssetColumn(entitiesDefinitionTable);
            for (int entityRowIndex = 0; entityRowIndex < entitiesDefinitionTable.Rows.Count; entityRowIndex++)
            {
                DocRow entityRow = entitiesDefinitionTable.Rows[entityRowIndex];
                string displayLabel = ResolveEntityDefinitionLabel(entitiesDefinitionTable, entityRow);
                string uiAssetPath = uiAssetColumn != null
                    ? (entityRow.GetCell(uiAssetColumn).StringValue ?? "")
                    : "";

                group.Items.Add(new ToolItem(
                    entityRow.Id,
                    displayLabel,
                    uiAssetPath));
            }

            destination.Add(group);
        }
    }

    private static void EnsureActiveToolIsValid(ViewState state, List<ToolGroup> toolGroups)
    {
        if (state.ActiveTool == ActiveToolKind.PlaceEntity)
        {
            if (TryFindToolItem(toolGroups, state.ActiveEntityTableId, state.ActiveEntityRowId, out _))
            {
                return;
            }

            if (TryGetFirstToolItem(toolGroups, out ToolGroup firstGroup, out ToolItem firstItem))
            {
                state.ActiveEntityTableId = firstGroup.EntitiesTableId;
                state.ActiveEntityRowId = firstItem.RowId;
                return;
            }

            state.ActiveTool = ActiveToolKind.Select;
            state.ActiveEntityTableId = "";
            state.ActiveEntityRowId = "";
            return;
        }

        if (state.ActiveTool == ActiveToolKind.Pen || state.ActiveTool == ActiveToolKind.Select)
        {
            return;
        }

        state.ActiveTool = ActiveToolKind.Select;
    }

    private static bool TryFindToolItem(
        List<ToolGroup> toolGroups,
        string tableId,
        string rowId,
        out ToolItem item)
    {
        for (int groupIndex = 0; groupIndex < toolGroups.Count; groupIndex++)
        {
            ToolGroup group = toolGroups[groupIndex];
            if (!string.Equals(group.EntitiesTableId, tableId, StringComparison.Ordinal))
            {
                continue;
            }

            for (int itemIndex = 0; itemIndex < group.Items.Count; itemIndex++)
            {
                ToolItem candidateItem = group.Items[itemIndex];
                if (string.Equals(candidateItem.RowId, rowId, StringComparison.Ordinal))
                {
                    item = candidateItem;
                    return true;
                }
            }
        }

        item = default;
        return false;
    }

    private static bool TryFindGroupItemByRowId(
        ToolGroup group,
        string rowId,
        out ToolItem item)
    {
        if (string.IsNullOrWhiteSpace(rowId))
        {
            item = default;
            return false;
        }

        for (int itemIndex = 0; itemIndex < group.Items.Count; itemIndex++)
        {
            ToolItem candidateItem = group.Items[itemIndex];
            if (string.Equals(candidateItem.RowId, rowId, StringComparison.Ordinal))
            {
                item = candidateItem;
                return true;
            }
        }

        item = default;
        return false;
    }

    private static bool TryGetFirstToolItem(
        List<ToolGroup> toolGroups,
        out ToolGroup group,
        out ToolItem item)
    {
        for (int groupIndex = 0; groupIndex < toolGroups.Count; groupIndex++)
        {
            ToolGroup candidateGroup = toolGroups[groupIndex];
            if (candidateGroup.Items.Count <= 0)
            {
                continue;
            }

            group = candidateGroup;
            item = candidateGroup.Items[0];
            return true;
        }

        group = null!;
        item = default;
        return false;
    }

    private static string ResolveToolsTableName(DocProject project, LevelContext context)
    {
        DocTable? toolsTable = ResolveToolsTable(project, context);
        if (toolsTable == null)
        {
            return "";
        }

        return "Tools table: " + toolsTable.Name;
    }

    private static DocTable? ResolveToolsTable(DocProject project, LevelContext context)
    {
        string toolsTableId = "";
        if (context.LevelSchema.EntityToolsTableColumn != null)
        {
            toolsTableId = context.LevelRow.GetCell(context.LevelSchema.EntityToolsTableColumn).StringValue ?? "";
        }

        if (!string.IsNullOrWhiteSpace(toolsTableId))
        {
            DocTable? configuredTable = FindTableById(project, toolsTableId);
            if (configuredTable != null)
            {
                return configuredTable;
            }
        }

        return FindSystemTableByKey(project, DocSystemTableKeys.SplineGameEntityTools);
    }

    private static bool TryResolveLevelSchema(DocTable table, out LevelSchema schema)
    {
        DocColumn? parentRowColumn = GetColumnByIdOrNull(table, table.ParentRowColumnId ?? SplineGameLevelIds.ParentRowIdColumnId);
        if (!TryGetColumnById(table, SplineGameLevelIds.PointsSubtableColumnId, out DocColumn pointsSubtableColumn) ||
            !TryGetColumnById(table, SplineGameLevelIds.EntitiesSubtableColumnId, out DocColumn entitiesSubtableColumn))
        {
            schema = default;
            return false;
        }

        DocColumn? entityToolsTableColumn = GetColumnByIdOrNull(table, SplineGameLevelIds.EntityToolsTableColumnId);
        schema = new LevelSchema(parentRowColumn, pointsSubtableColumn, entitiesSubtableColumn, entityToolsTableColumn);
        return true;
    }

    private static bool TryResolveLevelContext(
        DocWorkspace workspace,
        DocTable levelTable,
        LevelSchema levelSchema,
        string? activeParentRowId,
        bool createIfMissing,
        out LevelContext context)
    {
        if (!TryResolveLevelRow(workspace, levelTable, levelSchema, activeParentRowId, createIfMissing, out DocRow levelRow))
        {
            context = default;
            return false;
        }

        string pointsTableId = levelSchema.PointsSubtableColumn.SubtableId ?? "";
        string entitiesTableId = levelSchema.EntitiesSubtableColumn.SubtableId ?? "";
        DocTable? pointsTable = FindTableById(workspace.Project, pointsTableId);
        DocTable? entitiesTable = FindTableById(workspace.Project, entitiesTableId);
        if (pointsTable == null || entitiesTable == null)
        {
            context = default;
            return false;
        }

        if (!TryResolvePointsSchema(pointsTable, out PointsSchema pointsSchema) ||
            !TryResolveEntitiesSchema(entitiesTable, out EntitiesSchema entitiesSchema))
        {
            context = default;
            return false;
        }

        context = new LevelContext(
            workspace.Project,
            levelTable,
            levelRow,
            levelSchema,
            pointsTable,
            pointsSchema,
            entitiesTable,
            entitiesSchema);
        return true;
    }

    private static bool TryResolveLevelRow(
        DocWorkspace workspace,
        DocTable levelTable,
        LevelSchema levelSchema,
        string? activeParentRowId,
        bool createIfMissing,
        out DocRow levelRow)
    {
        for (int rowIndex = 0; rowIndex < levelTable.Rows.Count; rowIndex++)
        {
            DocRow candidateRow = levelTable.Rows[rowIndex];
            if (levelSchema.ParentRowColumn != null)
            {
                string candidateParentRowId = candidateRow.GetCell(levelSchema.ParentRowColumn).StringValue ?? "";
                if (!string.Equals(candidateParentRowId, activeParentRowId, StringComparison.Ordinal))
                {
                    continue;
                }
            }

            levelRow = candidateRow;
            return true;
        }

        if (!createIfMissing)
        {
            levelRow = null!;
            return false;
        }

        if (levelSchema.ParentRowColumn != null && string.IsNullOrWhiteSpace(activeParentRowId))
        {
            levelRow = null!;
            return false;
        }

        var createdRow = new DocRow();
        if (levelSchema.ParentRowColumn != null)
        {
            createdRow.SetCell(levelSchema.ParentRowColumn.Id, DocCellValue.Text(activeParentRowId ?? ""));
        }

        if (levelSchema.EntityToolsTableColumn != null)
        {
            DocTable? defaultToolsTable = FindSystemTableByKey(workspace.Project, DocSystemTableKeys.SplineGameEntityTools);
            if (defaultToolsTable != null)
            {
                createdRow.SetCell(levelSchema.EntityToolsTableColumn.Id, DocCellValue.Text(defaultToolsTable.Id));
            }
        }

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.AddRow,
            TableId = levelTable.Id,
            RowIndex = levelTable.Rows.Count,
            RowSnapshot = createdRow,
        });

        if (TryFindRowById(levelTable, createdRow.Id, out levelRow))
        {
            return true;
        }

        levelRow = null!;
        return false;
    }

    private static bool TryResolvePointsSchema(DocTable table, out PointsSchema schema)
    {
        DocColumn? parentRowColumn = GetColumnByIdOrNull(table, table.ParentRowColumnId ?? SplineGameLevelIds.PointsParentRowIdColumnId);
        if (!TryGetColumnById(table, SplineGameLevelIds.PointsOrderColumnId, out DocColumn orderColumn) ||
            !TryGetColumnById(table, SplineGameLevelIds.PointsPositionColumnId, out DocColumn positionColumn) ||
            !TryGetColumnById(table, SplineGameLevelIds.PointsTangentInColumnId, out DocColumn tangentInColumn) ||
            !TryGetColumnById(table, SplineGameLevelIds.PointsTangentOutColumnId, out DocColumn tangentOutColumn))
        {
            schema = default;
            return false;
        }

        schema = new PointsSchema(parentRowColumn, orderColumn, positionColumn, tangentInColumn, tangentOutColumn);
        return true;
    }

    private static bool TryResolveEntitiesSchema(DocTable table, out EntitiesSchema schema)
    {
        DocColumn? parentRowColumn = GetColumnByIdOrNull(table, table.ParentRowColumnId ?? SplineGameLevelIds.EntitiesParentRowIdColumnId);
        if (!TryGetColumnById(table, SplineGameLevelIds.EntitiesOrderColumnId, out DocColumn orderColumn) ||
            !TryGetColumnById(table, SplineGameLevelIds.EntitiesParamTColumnId, out DocColumn paramTColumn) ||
            !TryGetColumnById(table, SplineGameLevelIds.EntitiesPositionColumnId, out DocColumn positionColumn) ||
            !TryGetColumnById(table, SplineGameLevelIds.EntitiesTableRefColumnId, out DocColumn entityTableColumn) ||
            !TryGetColumnById(table, SplineGameLevelIds.EntitiesRowIdColumnId, out DocColumn entityRowIdColumn) ||
            !TryGetColumnById(table, SplineGameLevelIds.EntitiesDataJsonColumnId, out DocColumn dataJsonColumn))
        {
            schema = default;
            return false;
        }

        schema = new EntitiesSchema(
            parentRowColumn,
            orderColumn,
            paramTColumn,
            positionColumn,
            entityTableColumn,
            entityRowIdColumn,
            dataJsonColumn);
        return true;
    }

    private static ViewState GetOrCreateViewState(string viewKey)
    {
        if (ViewStateByKey.TryGetValue(viewKey, out ViewState? state) && state != null)
        {
            return state;
        }

        var created = new ViewState
        {
            Zoom = 1f,
            Pan = new Vector2(220f, 180f),
            ActiveTool = ActiveToolKind.Select,
        };
        ViewStateByKey[viewKey] = created;
        return created;
    }

    private static string BuildViewStateKey(string tableId, string viewId, string levelRowId)
    {
        return tableId + "|" + viewId + "|" + levelRowId;
    }

    private static Vector2 WorldToScreen(Vector2 worldPosition, ViewState state, ImRect canvasRect)
    {
        return new Vector2(
            canvasRect.X + state.Pan.X + worldPosition.X * state.Zoom,
            canvasRect.Y + state.Pan.Y + worldPosition.Y * state.Zoom);
    }

    private static Vector2 ScreenToWorld(Vector2 screenPosition, ViewState state, ImRect canvasRect)
    {
        return new Vector2(
            (screenPosition.X - canvasRect.X - state.Pan.X) / state.Zoom,
            (screenPosition.Y - canvasRect.Y - state.Pan.Y) / state.Zoom);
    }

    private static Vector2 ReadVec2(DocRow row, DocColumn column)
    {
        DocCellValue cell = row.GetCell(column);
        return new Vector2((float)cell.XValue, (float)cell.YValue);
    }

    private static bool SetVec2Cell(
        DocWorkspace workspace,
        DocTable table,
        DocRow row,
        DocColumn column,
        Vector2 value)
    {
        DocCellValue oldCell = row.GetCell(column);
        if (Math.Abs(oldCell.XValue - value.X) < 0.0001d &&
            Math.Abs(oldCell.YValue - value.Y) < 0.0001d)
        {
            return false;
        }

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetCell,
            TableId = table.Id,
            RowId = row.Id,
            ColumnId = column.Id,
            OldCellValue = oldCell,
            NewCellValue = DocCellValue.Vec2(value.X, value.Y),
        });
        return true;
    }

    private static bool SetTextCell(
        DocWorkspace workspace,
        DocTable table,
        DocRow row,
        DocColumn column,
        string text)
    {
        DocCellValue oldCell = row.GetCell(column);
        string oldText = oldCell.StringValue ?? "";
        if (string.Equals(oldText, text, StringComparison.Ordinal))
        {
            return false;
        }

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetCell,
            TableId = table.Id,
            RowId = row.Id,
            ColumnId = column.Id,
            OldCellValue = oldCell,
            NewCellValue = DocCellValue.Text(text),
        });
        return true;
    }

    private static bool SetNumberCell(
        DocWorkspace workspace,
        DocTable table,
        DocRow row,
        DocColumn column,
        double value)
    {
        DocCellValue oldCell = row.GetCell(column);
        if (Math.Abs(oldCell.NumberValue - value) < 0.000001d)
        {
            return false;
        }

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetCell,
            TableId = table.Id,
            RowId = row.Id,
            ColumnId = column.Id,
            OldCellValue = oldCell,
            NewCellValue = DocCellValue.Number(value),
        });
        return true;
    }

    private static bool DrawInspectorFloatRow(
        ReadOnlySpan<char> label,
        string inputId,
        float x,
        float y,
        float width,
        ref float value,
        float minValue,
        float maxValue,
        string format,
        ImStyle style)
    {
        float textY = y + (InspectorRowHeight - style.FontSize) * 0.5f;
        Im.Text(label, x, textY, style.FontSize - 1f, style.TextPrimary);

        float inputX = x + InspectorLabelWidth;
        float inputWidth = MathF.Max(InspectorFieldMinWidth, width - InspectorLabelWidth);
        float inputY = y + (InspectorRowHeight - style.MinButtonHeight) * 0.5f;
        return ImScalarInput.DrawAt(inputId, inputX, inputY, inputWidth, ref value, minValue, maxValue, format);
    }

    private static bool DrawInspectorVec2Row(
        ReadOnlySpan<char> label,
        string idPrefix,
        float x,
        float y,
        float width,
        ref Vector2 value,
        ImStyle style)
    {
        float textY = y + (InspectorRowHeight - style.FontSize) * 0.5f;
        Im.Text(label, x, textY, style.FontSize - 1f, style.TextPrimary);

        float inputX = x + InspectorLabelWidth;
        float inputWidth = MathF.Max(InspectorFieldMinWidth, width - InspectorLabelWidth);
        float halfWidth = MathF.Max(40f, (inputWidth - 4f) * 0.5f);
        float inputY = y + (InspectorRowHeight - style.MinButtonHeight) * 0.5f;

        float xValue = value.X;
        float yValue = value.Y;
        bool xChanged = ImScalarInput.DrawAt(idPrefix + "_x", inputX, inputY, halfWidth, ref xValue, -100000f, 100000f, "F2");
        bool yChanged = ImScalarInput.DrawAt(idPrefix + "_y", inputX + halfWidth + 4f, inputY, halfWidth, ref yValue, -100000f, 100000f, "F2");
        if (!xChanged && !yChanged)
        {
            return false;
        }

        value = new Vector2(xValue, yValue);
        return true;
    }

    private static float DrawInspectorTextCellRow(
        DocWorkspace workspace,
        DocTable table,
        DocRow row,
        DocColumn column,
        ReadOnlySpan<char> label,
        string inputId,
        char[] textBuffer,
        ref int textBufferLength,
        ref bool textBufferFocused,
        ref string textSyncKey,
        float x,
        float y,
        float width,
        ImStyle style,
        bool canEdit)
    {
        string currentText = row.GetCell(column).StringValue ?? "";
        string syncKey = row.Id + "|" + column.Id + "|" + currentText;
        if (!string.Equals(textSyncKey, syncKey, StringComparison.Ordinal))
        {
            SetTextBuffer(textBuffer, ref textBufferLength, currentText);
            textSyncKey = syncKey;
        }

        float textY = y + (InspectorRowHeight - style.FontSize) * 0.5f;
        Im.Text(label, x, textY, style.FontSize - 1f, style.TextPrimary);

        float inputX = x + InspectorLabelWidth;
        float inputWidth = MathF.Max(InspectorFieldMinWidth, width - InspectorLabelWidth);
        _ = Im.TextInput(inputId, textBuffer, ref textBufferLength, textBuffer.Length, inputX, y, inputWidth);

        if (canEdit && ShouldCommitTextInput(inputId, ref textBufferFocused))
        {
            string updatedText = new(textBuffer, 0, textBufferLength);
            if (SetTextCell(workspace, table, row, column, updatedText))
            {
                textSyncKey = row.Id + "|" + column.Id + "|" + updatedText;
            }
            else
            {
                textSyncKey = syncKey;
            }
        }

        return y + InspectorRowHeight + InspectorRowSpacing;
    }

    private static bool ShouldCommitTextInput(string inputId, ref bool wasFocused)
    {
        int widgetId = Im.Context.GetId(inputId);
        bool isFocused = Im.Context.IsFocused(widgetId);
        bool shouldCommit = false;
        var input = Im.Context.Input;

        if (isFocused && (input.KeyEnter || input.KeyTab))
        {
            shouldCommit = true;
        }
        else if (!isFocused && wasFocused)
        {
            shouldCommit = true;
        }

        wasFocused = isFocused;
        return shouldCommit;
    }

    private static void SetTextBuffer(char[] destinationBuffer, ref int destinationLength, string sourceText)
    {
        int writeLength = Math.Min(destinationBuffer.Length, sourceText.Length);
        sourceText.AsSpan(0, writeLength).CopyTo(destinationBuffer);
        destinationLength = writeLength;
    }

    private static void DrawTooltip(int tooltipId, bool hovered, string text)
    {
        ImTooltip.Begin(tooltipId, hovered);
        if (!ImTooltip.ShouldShow(tooltipId))
        {
            return;
        }

        ImTooltip.Draw(text);
    }

    private static void BuildTableRefOptions(
        DocWorkspace workspace,
        DocColumn tableRefColumn,
        List<TableOption> destination)
    {
        destination.Clear();
        destination.Add(new TableOption("", "(none)"));

        for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
        {
            DocTable table = workspace.Project.Tables[tableIndex];
            if (!IsTableAllowedForTableRef(workspace.Project, tableRefColumn, table))
            {
                continue;
            }

            destination.Add(new TableOption(table.Id, table.Name));
        }
    }

    private static bool IsTableAllowedForTableRef(
        DocProject project,
        DocColumn tableRefColumn,
        DocTable candidateTable)
    {
        string? baseTableId = tableRefColumn.TableRefBaseTableId;
        if (string.IsNullOrWhiteSpace(baseTableId))
        {
            return true;
        }

        return IsTableDerivedFromOrEqualTo(project, candidateTable, baseTableId);
    }

    private static bool IsTableDerivedFromOrEqualTo(
        DocProject project,
        DocTable candidateTable,
        string baseTableId)
    {
        if (string.Equals(candidateTable.Id, baseTableId, StringComparison.Ordinal))
        {
            return true;
        }

        string currentTableId = candidateTable.Id;
        const int maxDepth = 32;
        for (int depth = 0; depth < maxDepth; depth++)
        {
            DocTable? currentTable = FindTableById(project, currentTableId);
            if (currentTable == null)
            {
                return false;
            }

            string? nextTableId = null;
            if (currentTable.IsDerived &&
                currentTable.DerivedConfig != null &&
                !string.IsNullOrWhiteSpace(currentTable.DerivedConfig.BaseTableId))
            {
                nextTableId = currentTable.DerivedConfig.BaseTableId;
            }
            else if (!string.IsNullOrWhiteSpace(currentTable.SchemaSourceTableId))
            {
                nextTableId = currentTable.SchemaSourceTableId;
            }
            else if (!string.IsNullOrWhiteSpace(currentTable.InheritanceSourceTableId))
            {
                nextTableId = currentTable.InheritanceSourceTableId;
            }

            if (string.IsNullOrWhiteSpace(nextTableId))
            {
                return false;
            }

            if (string.Equals(nextTableId, baseTableId, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(nextTableId, currentTableId, StringComparison.Ordinal))
            {
                return false;
            }

            currentTableId = nextTableId;
        }

        return false;
    }

    private static void BuildEntityRowOptions(
        DocProject project,
        string entityTableId,
        List<RowOption> destination)
    {
        destination.Clear();
        destination.Add(new RowOption("", "(none)"));

        if (string.IsNullOrWhiteSpace(entityTableId))
        {
            return;
        }

        DocTable? entityTable = FindTableById(project, entityTableId);
        if (entityTable == null)
        {
            return;
        }

        for (int rowIndex = 0; rowIndex < entityTable.Rows.Count; rowIndex++)
        {
            DocRow row = entityTable.Rows[rowIndex];
            destination.Add(new RowOption(row.Id, ResolveEntityDefinitionLabel(entityTable, row)));
        }
    }

    private static string ResolveDefaultEntityRowId(DocProject project, string entityTableId)
    {
        if (string.IsNullOrWhiteSpace(entityTableId))
        {
            return "";
        }

        DocTable? entityTable = FindTableById(project, entityTableId);
        if (entityTable == null || entityTable.Rows.Count <= 0)
        {
            return "";
        }

        return entityTable.Rows[0].Id;
    }

    private static void ResolveEntityDefinition(
        DocProject project,
        string entityTableId,
        string requestedRowId,
        out string displayLabel,
        out string uiAssetPath,
        out string resolvedRowId,
        out float uiScale)
    {
        displayLabel = "Entity";
        uiAssetPath = "";
        resolvedRowId = "";
        uiScale = DefaultEntityUiScale;

        if (string.IsNullOrWhiteSpace(entityTableId))
        {
            return;
        }

        DocTable? entityTable = FindTableById(project, entityTableId);
        if (entityTable == null)
        {
            return;
        }

        DocRow? entityRow = null;
        if (!string.IsNullOrWhiteSpace(requestedRowId) &&
            TryFindRowById(entityTable, requestedRowId, out DocRow foundRow))
        {
            entityRow = foundRow;
        }
        else if (entityTable.Rows.Count > 0)
        {
            entityRow = entityTable.Rows[0];
        }

        if (entityRow == null)
        {
            displayLabel = entityTable.Name;
            return;
        }

        resolvedRowId = entityRow.Id;
        displayLabel = ResolveEntityDefinitionLabel(entityTable, entityRow);
        DocColumn? uiAssetColumn = ResolveEntityUiAssetColumn(entityTable);
        if (uiAssetColumn != null)
        {
            uiAssetPath = entityRow.GetCell(uiAssetColumn).StringValue ?? "";
        }

        DocColumn? scaleColumn = ResolveEntityScaleColumn(entityTable);
        if (scaleColumn != null)
        {
            double configuredScale = entityRow.GetCell(scaleColumn).NumberValue;
            if (double.IsFinite(configuredScale) && configuredScale > 0d)
            {
                uiScale = (float)configuredScale;
            }
        }
    }

    private static string ResolveEntityDefinitionLabel(DocTable entityTable, DocRow row)
    {
        DocColumn? nameColumn = ResolveEntityNameColumn(entityTable);
        if (nameColumn != null)
        {
            string name = row.GetCell(nameColumn).StringValue ?? "";
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        if (!string.IsNullOrWhiteSpace(entityTable.Keys.PrimaryKeyColumnId) &&
            TryGetColumnById(entityTable, entityTable.Keys.PrimaryKeyColumnId, out DocColumn primaryKeyColumn))
        {
            string keyText = FormatCellLabel(row.GetCell(primaryKeyColumn));
            if (!string.IsNullOrWhiteSpace(keyText))
            {
                return keyText;
            }
        }

        return row.Id;
    }

    private static DocColumn? ResolveToolNameColumn(DocTable toolsTable)
    {
        DocColumn? preferredColumn = FindColumnById(toolsTable, SplineGameLevelIds.EntityToolNameColumnId);
        if (preferredColumn != null)
        {
            return preferredColumn;
        }

        return FindFirstColumnByKindAndName(toolsTable, DocColumnKind.Text, "Name");
    }

    private static DocColumn? ResolveToolEntitiesTableColumn(DocTable toolsTable)
    {
        DocColumn? preferredColumn = FindColumnById(toolsTable, SplineGameLevelIds.EntityToolTableRefColumnId);
        if (preferredColumn != null)
        {
            return preferredColumn;
        }

        DocColumn? namedColumn = FindFirstColumnByKindAndName(toolsTable, DocColumnKind.TableRef, "EntitiesTable");
        if (namedColumn != null)
        {
            return namedColumn;
        }

        return FindFirstColumnByKind(toolsTable, DocColumnKind.TableRef);
    }

    private static DocColumn? ResolveEntityNameColumn(DocTable entityTable)
    {
        DocColumn? preferredColumn = FindColumnById(entityTable, SplineGameLevelIds.EntityDefinitionNameColumnId);
        if (preferredColumn != null)
        {
            return preferredColumn;
        }

        return FindFirstColumnByKindAndName(entityTable, DocColumnKind.Text, "Name");
    }

    private static DocColumn? ResolveEntityUiAssetColumn(DocTable entityTable)
    {
        DocColumn? preferredColumn = FindColumnById(entityTable, SplineGameLevelIds.EntityDefinitionUiAssetColumnId);
        if (preferredColumn != null)
        {
            return preferredColumn;
        }

        DocColumn? namedColumn = FindFirstColumnByKindAndName(entityTable, DocColumnKind.UiAsset, "UiAsset");
        if (namedColumn != null)
        {
            return namedColumn;
        }

        return FindFirstColumnByKind(entityTable, DocColumnKind.UiAsset);
    }

    private static DocColumn? ResolveEntityScaleColumn(DocTable entityTable)
    {
        DocColumn? preferredColumn = FindColumnById(entityTable, SplineGameLevelIds.EntityDefinitionScaleColumnId);
        if (preferredColumn != null)
        {
            return preferredColumn;
        }

        DocColumn? namedColumn = FindFirstColumnByKindAndName(entityTable, DocColumnKind.Number, "Scale");
        if (namedColumn != null)
        {
            return namedColumn;
        }

        return FindFirstColumnByKind(entityTable, DocColumnKind.Number);
    }

    private static DocColumn? FindFirstColumnByKindAndName(DocTable table, DocColumnKind kind, string expectedName)
    {
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            DocColumn column = table.Columns[columnIndex];
            if (column.Kind != kind)
            {
                continue;
            }

            if (string.Equals(column.Name, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                return column;
            }
        }

        return null;
    }

    private static DocColumn? FindFirstColumnByKind(DocTable table, DocColumnKind kind)
    {
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            DocColumn column = table.Columns[columnIndex];
            if (column.Kind == kind)
            {
                return column;
            }
        }

        return null;
    }

    private static string FormatCellLabel(DocCellValue cell)
    {
        if (!string.IsNullOrWhiteSpace(cell.StringValue))
        {
            return cell.StringValue;
        }

        if (Math.Abs(cell.NumberValue) > double.Epsilon)
        {
            return cell.NumberValue.ToString("G", CultureInfo.InvariantCulture);
        }

        if (cell.BoolValue)
        {
            return "True";
        }

        return "";
    }

    private static string BuildGroupButtonLabel(string name)
    {
        string shortName = BuildShortLabel(name);
        if (shortName.Length >= 2)
        {
            return shortName;
        }

        return "TG";
    }

    private static string BuildShortLabel(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "--";
        }

        string trimmed = text.Trim();
        if (trimmed.Length == 1)
        {
            return trimmed.ToUpperInvariant();
        }

        return (trimmed[0].ToString() + trimmed[1]).ToUpperInvariant();
    }

    private static bool TryFindEntryBySource(
        List<EntryVisual> entries,
        string tableId,
        string rowId,
        out EntryVisual entry)
    {
        for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            EntryVisual candidate = entries[entryIndex];
            if (string.Equals(candidate.SourceTableId, tableId, StringComparison.Ordinal) &&
                string.Equals(candidate.RowId, rowId, StringComparison.Ordinal))
            {
                entry = candidate;
                return true;
            }
        }

        entry = default;
        return false;
    }

    private static bool TryGetColumnById(DocTable table, string columnId, out DocColumn column)
    {
        if (string.IsNullOrWhiteSpace(columnId))
        {
            column = null!;
            return false;
        }

        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            DocColumn candidateColumn = table.Columns[columnIndex];
            if (string.Equals(candidateColumn.Id, columnId, StringComparison.Ordinal))
            {
                column = candidateColumn;
                return true;
            }
        }

        column = null!;
        return false;
    }

    private static DocColumn? GetColumnByIdOrNull(DocTable table, string columnId)
    {
        if (TryGetColumnById(table, columnId, out DocColumn column))
        {
            return column;
        }

        return null;
    }

    private static bool TryFindRowById(DocTable table, string rowId, out DocRow row)
    {
        if (string.IsNullOrWhiteSpace(rowId))
        {
            row = null!;
            return false;
        }

        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            DocRow candidate = table.Rows[rowIndex];
            if (string.Equals(candidate.Id, rowId, StringComparison.Ordinal))
            {
                row = candidate;
                return true;
            }
        }

        row = null!;
        return false;
    }

    private static DocColumn? FindColumnById(DocTable table, string columnId)
    {
        if (string.IsNullOrWhiteSpace(columnId))
        {
            return null;
        }

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

    private static DocTable? FindTableById(DocProject project, string tableId)
    {
        if (string.IsNullOrWhiteSpace(tableId))
        {
            return null;
        }

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

    private static DocTable? FindSystemTableByKey(DocProject project, string systemKey)
    {
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            DocTable table = project.Tables[tableIndex];
            if (string.Equals(table.SystemKey, systemKey, StringComparison.Ordinal))
            {
                return table;
            }
        }

        return null;
    }

    private readonly record struct EntryVisual(
        string SourceTableId,
        string RowId,
        EntryKind Kind,
        double Order,
        double ParamT,
        Vector2 Position,
        Vector2 TangentIn,
        Vector2 TangentOut,
        string EntityTableId,
        string EntityRowId,
        string DataJson,
        string DisplayLabel,
        string UiAssetPath,
        float UiScale);

    private readonly record struct EntityMarkerVisual(
        DerpLib.Rendering.Texture Texture,
        Vector2 CenterScreen,
        float DrawX,
        float DrawY,
        float DrawWidth,
        float DrawHeight,
        float RotationRadians);

    private readonly record struct LevelSchema(
        DocColumn? ParentRowColumn,
        DocColumn PointsSubtableColumn,
        DocColumn EntitiesSubtableColumn,
        DocColumn? EntityToolsTableColumn);

    private readonly record struct PointsSchema(
        DocColumn? ParentRowColumn,
        DocColumn OrderColumn,
        DocColumn PositionColumn,
        DocColumn TangentInColumn,
        DocColumn TangentOutColumn);

    private readonly record struct EntitiesSchema(
        DocColumn? ParentRowColumn,
        DocColumn OrderColumn,
        DocColumn ParamTColumn,
        DocColumn PositionColumn,
        DocColumn EntityTableColumn,
        DocColumn EntityRowIdColumn,
        DocColumn DataJsonColumn);

    private readonly record struct LevelContext(
        DocProject Project,
        DocTable LevelTable,
        DocRow LevelRow,
        LevelSchema LevelSchema,
        DocTable PointsTable,
        PointsSchema PointsSchema,
        DocTable EntitiesTable,
        EntitiesSchema EntitiesSchema);

    private readonly record struct TableOption(string Id, string Label);

    private readonly record struct RowOption(string Id, string Label);

    private readonly record struct ToolItem(string RowId, string Label, string UiAssetPath);

    private sealed class ToolGroup
    {
        public string RowId = "";
        public string Name = "";
        public string EntitiesTableId = "";
        public string EntitiesTableName = "";
        public List<ToolItem> Items { get; } = new();
    }

    private enum EntryKind
    {
        Point,
        Entity,
    }

    private enum ActiveToolKind
    {
        Select,
        Pen,
        PlaceEntity,
    }

    private enum DragTargetKind
    {
        None,
        Point,
        InTangent,
        OutTangent,
        EntityMarker,
    }

    private sealed class ViewState
    {
        public float Zoom = 1f;
        public Vector2 Pan;
        public bool PanActive;
        public Vector2 PanStartMouse;
        public Vector2 PanStartValue;
        public ActiveToolKind ActiveTool = ActiveToolKind.Select;
        public bool ShowDebugNormals;
        public string ActiveEntityTableId = "";
        public string ActiveEntityRowId = "";
        public string ContextMenuEntityRowId = "";
        public string ExpandedGroupRowId = "";
        public string SelectedTableId = "";
        public string SelectedRowId = "";
        public DragTargetKind DragKind;
        public string DraggedTableId = "";
        public string DraggedRowId = "";
        public Vector2 DragPointerOffset;
    }
}
