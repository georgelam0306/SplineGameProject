using System;
using System.Globalization;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Widgets;
using Derp.Doc.Commands;
using Derp.Doc.Editor;
using Derp.Doc.Model;
using Derp.Doc.Plugins;
using Derp.Doc.Tables;
using FontAwesome.Sharp;

namespace Derp.Doc.Panels;

internal static class InspectorPanel
{
    // Pre-allocated icon strings (zero-alloc per frame)
    private static readonly string CloseIcon = ((char)IconChar.Times).ToString();
    private static readonly string TableIcon = ((char)IconChar.Table).ToString();
    private static readonly string FileIcon = ((char)IconChar.FileAlt).ToString();
    private static readonly string EyeIcon = ((char)IconChar.Eye).ToString();
    private static readonly string EyeSlashIcon = ((char)IconChar.EyeSlash).ToString();
    private static readonly string ChevronDownIcon = ((char)IconChar.ChevronDown).ToString();
    private static readonly string ChevronRightIcon = ((char)IconChar.ChevronRight).ToString();
    private static readonly string TextIcon = ((char)IconChar.Font).ToString();
    private static readonly string NumberIcon = ((char)IconChar.Hashtag).ToString();
    private static readonly string CheckboxIcon = ((char)IconChar.SquareCheck).ToString();
    private static readonly string SelectIcon = ((char)IconChar.List).ToString();
    private static readonly string FormulaIcon = ((char)IconChar.Code).ToString();
    private static readonly string VariableIcon = ((char)IconChar.Tag).ToString();
    private static readonly string IdIcon = "ID";
    private static readonly string RelationIcon = ((char)IconChar.Link).ToString();
    private static readonly string TableRefIcon = ((char)IconChar.Table).ToString();
    private static readonly string SplineIcon = ((char)IconChar.BezierCurve).ToString();
    private static readonly string TextureIcon = ((char)IconChar.Image).ToString();
    private static readonly string MeshIcon = ((char)IconChar.Cube).ToString();
    private static readonly string AudioIcon = ((char)IconChar.Music).ToString();
    private static readonly string UiIcon = ((char)IconChar.WindowMaximize).ToString();
    private static readonly string Vec2Icon = "V2";
    private static readonly string Vec3Icon = "V3";
    private static readonly string Vec4Icon = "V4";
    private static readonly string ColorIcon = "RGB";
    private static readonly string PlusIcon = ((char)IconChar.Plus).ToString();
    private static readonly string MinusIcon = ((char)IconChar.Minus).ToString();
    private static readonly string CheckIcon = ((char)IconChar.Check).ToString();
    private static readonly string LockIcon = ((char)IconChar.Lock).ToString();
    private static readonly string KeyIcon = ((char)IconChar.Key).ToString();
    private static readonly string LinkIcon = ((char)IconChar.Link).ToString();
    private static readonly string LayerGroupIcon = ((char)IconChar.LayerGroup).ToString();
    private static readonly string ProjectDiagramIcon = ((char)IconChar.ProjectDiagram).ToString();

    private static readonly string[] FilterOpNames =
    {
        "equals", "not equals", "contains", "not contains", "greater than", "less than", "is empty", "is not empty"
    };
    private static readonly string[] SortDirectionNames = { "Ascending", "Descending" };
    private static readonly string[] TableVariableKindNames =
    {
        "Id",
        "Text",
        "Number",
        "Checkbox",
        "Select",
        "Formula",
        "Relation",
        "TableRef",
        "Subtable",
        "Spline",
        "TextureAsset",
        "MeshAsset",
        "AudioAsset",
        "UiAsset",
        "Vec2",
        "Vec3",
        "Vec4",
        "Color",
    };
    private static readonly string AddStepMenuId = "derived_add_step_menu";
    private static readonly string AddStepJoinLabel = "Join";
    private static readonly string AddStepAppendLabel = "Append";
    private static readonly string[] JoinKindOptions = { "Left", "Inner" };
    private static readonly string[] SecondaryKeyUniquenessOptions = { "Unique", "Non-Unique" };

    private static int _dragStepIndex = -1;
    private static int _dragStepTargetIndex = -1;
    private static float _dragStepMouseOffsetY;
    private static string? _dragStepTableId;
    private static readonly float[] _stepBlockTops = new float[32];
    private static readonly float[] _stepBlockHeights = new float[32];
    private static int _dragProjectionIndex = -1;
    private static int _dragProjectionTargetIndex = -1;
    private static float _dragProjectionMouseOffsetY;
    private static string? _dragProjectionTableId;
    private static readonly float[] _projectionRowTops = new float[64];
    private static readonly char[] _renameDerivedColumnBuffer = new char[128];
    private static int _renameDerivedColumnBufferLength;
    private static bool _inlineRenameDerivedColumnActive;
    private static bool _inlineRenameDerivedNeedsFocus;
    private static bool _inlineRenameDerivedSelectAll;
    private static string _inlineRenameDerivedTableId = "";
    private static string _inlineRenameDerivedColumnId = "";
    private static ImRect _inlineRenameDerivedInputRect;
    private static bool _inlineRenameDerivedInputRectValid;

    // Section expand state
    private static bool _exportExpanded = true;
    private static bool _keysExpanded = true;
    private static bool _columnsExpanded = true;
    private static bool _derivedBaseExpanded = true;
    private static bool _derivedStepsExpanded = true;
    private static bool _derivedDiagnosticsExpanded = true;
    private static bool _schemaLinkExpanded = true;
    private static bool _inheritanceExpanded = true;
    private static bool _variantsExpanded = true;
    private static bool _displayTypeExpanded = true;
    private static bool _viewFiltersExpanded = true;
    private static bool _viewSortsExpanded = true;
    private static bool _viewGroupByExpanded = true;
    private static bool _tableVariablesExpanded = true;
    private static bool _tableInstanceVariablesExpanded = true;
    private static bool _documentVariablesExpanded = true;

    // View config dropdown indices
    private static int _displayTypeIndex;
    private static int _viewGroupByColIndex;
    private static int _viewCalendarDateColIndex;
    private static int _viewChartKindIndex;
    private static int _viewChartCatColIndex;
    private static int _viewChartValColIndex;
    private static int _inheritanceSourceDropdownIndex;

    // Filter value editing (per-filter buffers, max 8 filters)
    private const int MaxFilterValueBuffers = 8;
    private static readonly char[][] _filterValueBuffers = new char[MaxFilterValueBuffers][];
    private static readonly int[] _filterValueLengths = new int[MaxFilterValueBuffers];
    private static readonly bool[] _filterValueFocused = new bool[MaxFilterValueBuffers];
    private static string[] _filterValueSyncKeys = new string[MaxFilterValueBuffers];

    private static char[][] _tableVariableNameBuffers = new char[16][];
    private static int[] _tableVariableNameLengths = new int[16];
    private static bool[] _tableVariableNameFocused = new bool[16];
    private static string[] _tableVariableNameSyncKeys = new string[16];
    private static char[][] _tableVariantNameBuffers = new char[16][];
    private static int[] _tableVariantNameLengths = new int[16];
    private static bool[] _tableVariantNameFocused = new bool[16];
    private static string[] _tableVariantNameSyncKeys = new string[16];
    private static char[][] _tableVariableExpressionBuffers = new char[16][];
    private static int[] _tableVariableExpressionLengths = new int[16];
    private static bool[] _tableVariableExpressionFocused = new bool[16];
    private static string[] _tableVariableExpressionSyncKeys = new string[16];
    private static char[][] _tableInstanceVariableExpressionBuffers = new char[16][];
    private static int[] _tableInstanceVariableExpressionLengths = new int[16];
    private static bool[] _tableInstanceVariableExpressionFocused = new bool[16];
    private static string[] _tableInstanceVariableExpressionSyncKeys = new string[16];

    private static bool _viewBindingPopoverActive;
    private static string _viewBindingPopoverTableId = "";
    private static string _viewBindingPopoverViewId = "";
    private static ViewBindingTargetKind _viewBindingPopoverTargetKind;
    private static string _viewBindingPopoverTargetItemId = "";
    private static string _viewBindingPopoverLabel = "";
    private static ImRect _viewBindingPopoverAnchorRect;
    private static int _viewBindingPopoverOpenedFrame;
    private static readonly char[] _viewBindingPopoverFormulaBuffer = new char[256];
    private static int _viewBindingPopoverFormulaLength;
    private static bool _viewBindingPopoverFormulaFocused;
    private static string _viewBindingPopoverFormulaSyncKey = "";
    private static int _viewBindingPopoverSelectedVariableIndex;
    private static bool _viewBindingPopoverSelectionInitialized;

    // Dropdown indices for derived configuration (per-step, max 16 steps)
    private static readonly int[] _stepSourceTableIndex = new int[16];
    private static readonly int[] _stepBaseColIndex = new int[16];
    private static readonly int[] _stepSourceColIndex = new int[16];
    private static int _baseTableDropdownIndex;
    private static int _schemaSourceDropdownIndex;
    private static int _primaryKeyDropdownIndex;
    private static int _tableVariantDropdownIndex;
    private static int _instanceVariantDropdownIndex;
    private static int _pendingTableVariantDeleteId = DocTableVariant.BaseVariantId;
    private static string _pendingTableVariantDeleteTableId = "";

    // Scratch option buffers (grown rarely; reused per frame to avoid allocations)
    private static string[] _scratchOptionNames = new string[32];
    private static string[] _scratchOptionIds = new string[32];
    private static string[] _scratchOptionNamesB = new string[32];
    private static string[] _scratchOptionIdsB = new string[32];
    private static int[] _variantOptionIds = new int[32];
    private static string[] _sourceTableOptionNames = new string[32];
    private static string[] _sourceTableOptionIds = new string[32];
    private static string[] _displayTypeOptionNames = new string[16];
    private static DocViewType[] _displayTypeOptionTypes = new DocViewType[16];
    private static string[] _displayTypeOptionRendererIds = new string[16];
    private static readonly char[] _variantIdLabelBuffer = new char[24];
    private static DocumentVariableInspectorEntry[] _documentVariableEntries = new DocumentVariableInspectorEntry[16];
    private static int _documentVariableEntryCount;
    private static readonly HashSet<string> _documentVariableNames = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<DocCommand> _documentVariableRemoveCommandScratch = new(8);
    private static readonly DocFormulaEngine _documentFormulaPreviewEngine = new();
    private static readonly Dictionary<string, DocumentVariableValueCacheEntry> _documentVariableValueCacheByBlockId = new(StringComparer.Ordinal);
    private static readonly List<IDerpDocTableViewRenderer> _tableViewRenderersScratch = new();
    private static readonly List<ColumnTypeDefinition> _tableVariableTypeDefinitionsScratch = new();
    private static string[] _tableVariableTypeOptionNames = new string[16];
    private static string[] _tableVariableTypeOptionTypeIds = new string[16];
    private static DocColumnKind[] _tableVariableTypeOptionKinds = new DocColumnKind[16];

    // Scroll state
    private static float _scrollY;
    private static uint _inspectorCardFillColor;
    private static uint _inspectorHeaderFillColor;
    private static uint _inspectorHeaderFillHoverColor;

    private const float RowHeight = 30f;
    private const float SectionHeaderHeight = 30f;
    private const float HeaderHeight = 36f;
    private const float Padding = 8f;
    private const float SectionHorizontalInset = 6f;
    private const float RowHorizontalInset = 6f;
    private const float SectionSpacing = 6f;
    private const float SectionCardBottomMargin = 4f;
    private const float SectionCardInnerBottomPadding = 8f;
    private const float SectionCornerRadius = 4f;
    private const float IconColumnWidth = 28f;
    private const float GutterLaneWidth = 14f;
    private const float GutterHandleDotSize = 2f;
    private const float GutterHandleDotSpacing = 2f;
    private const float ControlSpacing = 6f;
    private const float SectionHeaderContentSpacing = 6f;
    private const float DropdownBottomSpacing = 10f;
    private const float StepInnerGap = 8f;
    private const float StepBlockSpacing = 8f;
    private const float VisibilityToggleWidth = 24f;
    private const float DeleteButtonSize = 22f;
    private const ImDropdownFlags InspectorDropdownFlags = ImDropdownFlags.NoBorder;

    private enum ViewBindingTargetKind
    {
        None,
        GroupByColumn,
        CalendarDateColumn,
        ChartKind,
        ChartCategoryColumn,
        ChartValueColumn,
        SortColumn,
        SortDescending,
        FilterColumn,
        FilterOperator,
        FilterValue,
    }

    private readonly struct DocumentVariableInspectorEntry
    {
        public DocumentVariableInspectorEntry(
            string blockId,
            string variableName,
            bool hasExpression,
            string expression,
            bool isValidDeclaration,
            string invalidText)
        {
            BlockId = blockId;
            VariableName = variableName;
            HasExpression = hasExpression;
            Expression = expression;
            IsValidDeclaration = isValidDeclaration;
            InvalidText = invalidText;
        }

        public string BlockId { get; }
        public string VariableName { get; }
        public bool HasExpression { get; }
        public string Expression { get; }
        public bool IsValidDeclaration { get; }
        public string InvalidText { get; }
    }

    private readonly struct DocumentVariableValueCacheEntry
    {
        public DocumentVariableValueCacheEntry(
            int projectRevision,
            string expressionText,
            string resultText,
            bool isValid)
        {
            ProjectRevision = projectRevision;
            ExpressionText = expressionText;
            ResultText = resultText;
            IsValid = isValid;
        }

        public int ProjectRevision { get; }
        public string ExpressionText { get; }
        public string ResultText { get; }
        public bool IsValid { get; }
    }

    public static void Draw(DocWorkspace workspace)
    {
        DrawCore(workspace, Im.WindowContentRect, includeHeader: true);
    }

    public static void DrawContent(DocWorkspace workspace, ImRect contentRect)
    {
        DrawCore(workspace, contentRect, includeHeader: false);
    }

    private static void DrawCore(DocWorkspace workspace, ImRect contentRect, bool includeHeader)
    {
        if (contentRect.Width <= 0 || contentRect.Height <= 0)
        {
            return;
        }

        uint originalSurfaceColor = Im.Style.Surface;
        uint originalBackgroundColor = Im.Style.Background;
        uint originalActiveColor = Im.Style.Active;
        uint inspectorWindowBackgroundColor = ImStyle.Lerp(Im.Style.Background, 0xFF000000, 0.44f);
        uint inspectorHeaderDarkColor = originalBackgroundColor;//ImStyle.Lerp(inspectorWindowBackgroundColor, 0xFF000000, 0.18f);
        float headerAlphaNormalized = 180f / 255f;
        uint inspectorEditorSurfaceColor = ImStyle.Lerp(originalBackgroundColor, inspectorHeaderDarkColor, headerAlphaNormalized);
        uint inspectorInputActiveColor = ImStyle.Lerp(inspectorEditorSurfaceColor, Im.Style.Hover, 0.40f);
        _inspectorCardFillColor = originalBackgroundColor;
        _inspectorHeaderFillColor = inspectorHeaderDarkColor;
        _inspectorHeaderFillHoverColor = ImStyle.Lerp(inspectorHeaderDarkColor, Im.Style.Hover, 0.40f);
        Im.Style.Surface = inspectorEditorSurfaceColor;
        Im.Style.Active = inspectorInputActiveColor;
        try
        {
            var style = Im.Style;
            _inlineRenameDerivedInputRectValid = false;

            var viewport = Im.Context.CurrentViewport;
            if (viewport != null)
            {
                var drawList = viewport.CurrentDrawList;
                int previousSortKey = drawList.GetSortKey();
                int backgroundSortKey = previousSortKey - 128;
                if (backgroundSortKey > previousSortKey)
                {
                    backgroundSortKey = int.MinValue + 8192;
                }

                drawList.SetSortKey(backgroundSortKey);
                Im.DrawRect(
                    contentRect.X,
                    contentRect.Y,
                    contentRect.Width,
                    contentRect.Height,
                    inspectorWindowBackgroundColor);
                drawList.SetSortKey(previousSortKey);
            }
            else
            {
                Im.DrawRect(
                    contentRect.X,
                    contentRect.Y,
                    contentRect.Width,
                    contentRect.Height,
                    inspectorWindowBackgroundColor);
            }

            float contentY = contentRect.Y;
            float contentHeight = contentRect.Height;
            if (includeHeader)
            {
                DrawHeader(workspace, contentRect, style);
                contentY += HeaderHeight;
                contentHeight -= HeaderHeight;
            }

            if (contentHeight <= 0f)
            {
                return;
            }

            var bodyRect = new ImRect(contentRect.X, contentY, contentRect.Width, contentHeight);
            Im.PushClipRect(bodyRect);

            float y = contentY - _scrollY;
            DocTable? bindingUiTable = null;

            if (workspace.ActiveView == ActiveViewKind.Table && workspace.ActiveTable != null)
            {
                y = DrawTableInspector(workspace, workspace.ActiveTable, contentRect, y, style);
                bindingUiTable = workspace.ActiveTable;
            }
            else if (workspace.ActiveView == ActiveViewKind.Document && workspace.InspectedTable != null)
            {
                // Inspecting an embedded table from within a document
                y = DrawTableInspector(workspace, workspace.InspectedTable, contentRect, y, style);
                bindingUiTable = workspace.InspectedTable;
            }
            else if (workspace.ActiveView == ActiveViewKind.Document && workspace.ActiveDocument != null)
            {
                y = DrawDocumentInspector(workspace, workspace.ActiveDocument, contentRect, y, style);
            }
            else
            {
                Im.Text("No selection".AsSpan(), contentRect.X + Padding, y + 8, style.FontSize, style.TextSecondary);
                y += 32;
            }

            Im.PopClipRect();

            if (bindingUiTable != null)
            {
                DrawViewBindingPopover(workspace, bindingUiTable);
            }

            // Commit/cancel inline derived column rename when focus context changes.
            if (_inlineRenameDerivedColumnActive)
            {
                if (workspace.ActiveTable == null ||
                    !workspace.ActiveTable.IsDerived ||
                    !string.Equals(workspace.ActiveTable.Id, _inlineRenameDerivedTableId, StringComparison.Ordinal))
                {
                    CancelInlineDerivedColumnRename();
                }
                else if (!_inlineRenameDerivedNeedsFocus &&
                         Im.Context.Input.MousePressed &&
                         (!_inlineRenameDerivedInputRectValid || !_inlineRenameDerivedInputRect.Contains(Im.MousePos)))
                {
                    CommitInlineDerivedColumnRename(workspace, workspace.ActiveTable);
                }
            }

            // Scrollbar
            float totalContentHeight = y + _scrollY - contentY;

            // Mouse wheel scrolling
            var input = Im.Context.Input;
            if (input.ScrollDelta != 0f && bodyRect.Contains(Im.MousePos))
            {
                _scrollY -= input.ScrollDelta * 40f;
                float maxScroll = Math.Max(0f, totalContentHeight - contentHeight);
                _scrollY = Math.Clamp(_scrollY, 0f, maxScroll);
            }

            if (totalContentHeight > contentHeight)
            {
                float scrollbarWidth = 8f;
                float scrollbarGap = 2f;
                float scrollbarX = contentRect.Right - scrollbarWidth;

                // Preserve a 2px visual gutter between section cards and the scrollbar track.
                Im.DrawRect(
                    scrollbarX - scrollbarGap,
                    contentY,
                    scrollbarGap,
                    contentHeight,
                    inspectorWindowBackgroundColor);

                var scrollRect = new ImRect(
                    scrollbarX,
                    contentY,
                    scrollbarWidth,
                    contentHeight);
                ImScrollbar.DrawVertical("inspector_scroll".GetHashCode(), scrollRect, ref _scrollY, contentHeight, totalContentHeight);
            }
            else
            {
                _scrollY = 0;
            }
        }
        finally
        {
            Im.Style.Surface = originalSurfaceColor;
            Im.Style.Active = originalActiveColor;
        }
    }

    private static void DrawHeader(DocWorkspace workspace, ImRect contentRect, ImStyle style)
    {
        float headerY = contentRect.Y;

        // Header background: no backer color, just a separator line.
        Im.DrawLine(contentRect.X, headerY + HeaderHeight, contentRect.Right, headerY + HeaderHeight, 1f, style.Border);

        // Icon + title
        string icon;
        string title;
        if (workspace.ActiveView == ActiveViewKind.Table && workspace.ActiveTable != null)
        {
            icon = TableIcon;
            title = workspace.ActiveTable.Name;
        }
        else if (workspace.ActiveView == ActiveViewKind.Document && workspace.InspectedTable != null)
        {
            icon = TableIcon;
            title = workspace.InspectedTable.Name;
        }
        else if (workspace.ActiveView == ActiveViewKind.Document && workspace.ActiveDocument != null)
        {
            icon = FileIcon;
            title = workspace.ActiveDocument.Title;
        }
        else
        {
            icon = "";
            title = "Inspector";
        }

        float textY = headerY + (HeaderHeight - style.FontSize) * 0.5f;
        float textX = contentRect.X + Padding;

        if (icon.Length > 0)
        {
            Im.Text(icon.AsSpan(), textX, textY, style.FontSize, style.TextSecondary);
            textX += style.FontSize + 4;
        }

        Im.Text(title.AsSpan(), textX, textY, style.FontSize, style.TextPrimary);

        // Close button
        float closeBtnSize = 24f;
        float closeBtnX = contentRect.Right - closeBtnSize - 4;
        float closeBtnY = headerY + (HeaderHeight - closeBtnSize) * 0.5f;
        if (Im.Button(CloseIcon, closeBtnX, closeBtnY, closeBtnSize, closeBtnSize))
        {
            workspace.ShowInspector = false;
            workspace.InspectedTable = null;
            workspace.InspectedBlockId = null;
        }
    }

    private static float DrawTableInspector(
        DocWorkspace workspace,
        DocTable table,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        y += SectionSpacing;
        var inspectedBlock = FindInspectedBlock(workspace);
        bool hasTableInstanceContext = inspectedBlock != null &&
                                       string.Equals(inspectedBlock.TableId, table.Id, StringComparison.Ordinal);

        // Display type section (always shown — allows switching Grid/Board/Calendar)
        float sectionTop = y;
        y = DrawDisplayTypeSection(workspace, table, contentRect, y, style);
        DrawSectionCard(contentRect, sectionTop, y, style);
        y += SectionSpacing + SectionCardBottomMargin;

        if (table.IsDerived)
        {
            sectionTop = y;
            y = DrawDerivedBaseTableSection(workspace, table, contentRect, y, style);
            DrawSectionCard(contentRect, sectionTop, y, style);
            y += SectionSpacing + SectionCardBottomMargin;

            sectionTop = y;
            y = DrawDerivedStepsSection(workspace, table, contentRect, y, style);
            DrawSectionCard(contentRect, sectionTop, y, style);
            y += SectionSpacing + SectionCardBottomMargin;

            sectionTop = y;
            y = DrawDerivedDiagnosticsSection(workspace, table, contentRect, y, style);
            DrawSectionCard(contentRect, sectionTop, y, style);
            y += SectionSpacing + SectionCardBottomMargin;
        }

        sectionTop = y;
        y = DrawSchemaLinkSection(workspace, table, contentRect, y, style);
        DrawSectionCard(contentRect, sectionTop, y, style);
        y += SectionSpacing + SectionCardBottomMargin;

        sectionTop = y;
        y = DrawInheritanceSection(workspace, table, contentRect, y, style);
        DrawSectionCard(contentRect, sectionTop, y, style);
        y += SectionSpacing + SectionCardBottomMargin;

        sectionTop = y;
        y = DrawVariantsSection(workspace, table, hasTableInstanceContext ? inspectedBlock : null, contentRect, y, style);
        DrawSectionCard(contentRect, sectionTop, y, style);
        y += SectionSpacing + SectionCardBottomMargin;

        sectionTop = y;
        y = DrawExportSection(workspace, table, contentRect, y, style);
        DrawSectionCard(contentRect, sectionTop, y, style);
        y += SectionSpacing + SectionCardBottomMargin;

        sectionTop = y;
        y = DrawKeysSection(workspace, table, contentRect, y, style);
        DrawSectionCard(contentRect, sectionTop, y, style);
        y += SectionSpacing + SectionCardBottomMargin;

        // Columns section (always shown)
        sectionTop = y;
        y = DrawColumnsSection(workspace, table, contentRect, y, style);
        DrawSectionCard(contentRect, sectionTop, y, style);
        y += SectionSpacing + SectionCardBottomMargin;

        if (hasTableInstanceContext && inspectedBlock != null)
        {
            sectionTop = y;
            y = DrawTableInstanceVariablesSection(workspace, table, inspectedBlock, contentRect, y, style);
            DrawSectionCard(contentRect, sectionTop, y, style);
            y += SectionSpacing + SectionCardBottomMargin;
        }
        else
        {
            sectionTop = y;
            y = DrawTableVariablesSection(workspace, table, contentRect, y, style);
            DrawSectionCard(contentRect, sectionTop, y, style);
            y += SectionSpacing + SectionCardBottomMargin;
        }

        // Determine which view to configure (auto-create default Grid view if none exists)
        DocView? configView = ResolveInspectedView(workspace, table);
        if (configView == null && table.Views.Count == 0)
        {
            var defaultView = new DocView { Type = DocViewType.Grid, Name = "Grid view" };
            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.AddView,
                TableId = table.Id,
                ViewIndex = 0,
                ViewSnapshot = defaultView,
            });
            workspace.ActiveTableView = table.Views.Count > 0 ? table.Views[0] : null;
            configView = workspace.ActiveTableView;
        }

        if (configView != null)
        {
            sectionTop = y;
            y = DrawViewFiltersSection(workspace, table, configView, contentRect, y, style);
            DrawSectionCard(contentRect, sectionTop, y, style);
            y += SectionSpacing + SectionCardBottomMargin;

            sectionTop = y;
            y = DrawViewSortsSection(workspace, table, configView, contentRect, y, style);
            DrawSectionCard(contentRect, sectionTop, y, style);
            y += SectionSpacing + SectionCardBottomMargin;

            sectionTop = y;
            y = DrawViewGroupSection(workspace, table, configView, contentRect, y, style);
            DrawSectionCard(contentRect, sectionTop, y, style);
            y += SectionCardBottomMargin;
        }

        return y;
    }

    private static float DrawDocumentInspector(
        DocWorkspace workspace,
        DocDocument document,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        y += SectionSpacing;
        float sectionTop = y;
        int variableCount = BuildDocumentVariableInspectorEntries(document);

        float headerX = contentRect.X + SectionHorizontalInset;
        float headerWidth = contentRect.Width - (SectionHorizontalInset * 2f);
        float addButtonX = headerX + headerWidth - Padding - DeleteButtonSize;
        float addButtonY = y + (SectionHeaderHeight - DeleteButtonSize) * 0.5f;
        float headerRightOverlayWidth = DeleteButtonSize + Padding + 4f;

        y = DrawCollapsibleHeader("Variables", ref _documentVariablesExpanded, contentRect, y, style, variableCount, headerRightOverlayWidth);
        Im.Context.PushId(document.Id);
        if (Im.Button(PlusIcon, addButtonX, addButtonY, DeleteButtonSize, DeleteButtonSize))
        {
            AddDocumentVariableBlock(workspace, document);
            variableCount = BuildDocumentVariableInspectorEntries(document);
        }
        Im.Context.PopId();

        if (!_documentVariablesExpanded)
        {
            DrawSectionCard(contentRect, sectionTop, y, style);
            return y + SectionCardBottomMargin;
        }

        y += SectionHeaderContentSpacing;

        float rowX = contentRect.X + SectionHorizontalInset + RowHorizontalInset;
        float rowWidth = contentRect.Width - (SectionHorizontalInset + RowHorizontalInset) * 2f;
        if (variableCount <= 0)
        {
            float textY = y + (RowHeight - style.FontSize) * 0.5f;
            Im.Text("No variables".AsSpan(), rowX + 4f, textY, style.FontSize - 1f, style.TextSecondary);
            y += RowHeight;
            DrawSectionCard(contentRect, sectionTop, y, style);
            return y + SectionCardBottomMargin;
        }

        int removeEntryIndex = -1;
        for (int entryIndex = 0; entryIndex < variableCount; entryIndex++)
        {
            var entry = _documentVariableEntries[entryIndex];
            float rowY = y;
            bool rowHovered = new ImRect(rowX, rowY, rowWidth, RowHeight).Contains(Im.MousePos);
            if (rowHovered)
            {
                Im.DrawRect(rowX, rowY, rowWidth, RowHeight, ImStyle.WithAlpha(style.Hover, 88));
            }

            float textY = rowY + (RowHeight - style.FontSize) * 0.5f;
            float labelX = rowX + 4f;
            Im.Text(VariableIcon.AsSpan(), labelX, textY + 0.5f, style.FontSize - 1f, ImStyle.WithAlpha(style.TextSecondary, 140));
            labelX += style.FontSize + 4f;

            if (entry.IsValidDeclaration)
            {
                float atWidth = Im.MeasureTextWidth("@".AsSpan(), style.FontSize);
                Im.Text("@".AsSpan(), labelX, textY, style.FontSize, ImStyle.WithAlpha(style.TextSecondary, 180));
                Im.Text(entry.VariableName.AsSpan(), labelX + atWidth, textY, style.FontSize, style.TextPrimary);
            }
            else
            {
                string invalidText = string.IsNullOrWhiteSpace(entry.InvalidText)
                    ? "(invalid declaration)"
                    : entry.InvalidText;
                Im.Text(invalidText.AsSpan(), labelX, textY, style.FontSize, style.TextSecondary);
            }

            string valueText = ResolveDocumentVariableDisplayValue(workspace, document, entry, out bool valueIsValid);
            float removeButtonX = rowX + rowWidth - DeleteButtonSize;
            float removeButtonY = rowY + (RowHeight - DeleteButtonSize) * 0.5f;
            float valueWidth = Im.MeasureTextWidth(valueText.AsSpan(), style.FontSize - 1f);
            float valueX = Math.Max(labelX + 100f, removeButtonX - 8f - valueWidth);
            uint valueColor = valueIsValid
                ? ImStyle.WithAlpha(style.TextSecondary, 190)
                : ImStyle.WithAlpha(style.Primary, 190);
            Im.Text(valueText.AsSpan(), valueX, textY, style.FontSize - 1f, valueColor);

            Im.Context.PushId(entry.BlockId);
            if (rowHovered && Im.Button(MinusIcon, removeButtonX, removeButtonY, DeleteButtonSize, DeleteButtonSize))
            {
                removeEntryIndex = entryIndex;
            }
            Im.Context.PopId();

            Im.DrawLine(rowX, rowY + RowHeight, rowX + rowWidth, rowY + RowHeight, 1f, ImStyle.WithAlpha(style.Border, 90));
            y += RowHeight;
        }

        if (removeEntryIndex >= 0 && removeEntryIndex < variableCount)
        {
            RemoveDocumentVariableEntry(workspace, document, _documentVariableEntries[removeEntryIndex]);
        }

        DrawSectionCard(contentRect, sectionTop, y, style);
        return y + SectionCardBottomMargin;
    }

    private static int BuildDocumentVariableInspectorEntries(DocDocument document)
    {
        _documentVariableEntryCount = 0;
        _documentVariableNames.Clear();
        for (int blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            var block = document.Blocks[blockIndex];
            if (block.Type != DocBlockType.Variable)
            {
                continue;
            }

            if (!DocumentFormulaSyntax.TryParseVariableDeclaration(
                    block.Text.PlainText,
                    out string variableName,
                    out bool hasExpression,
                    out string expression))
            {
                EnsureDocumentVariableEntryCapacity(_documentVariableEntryCount + 1);
                _documentVariableEntries[_documentVariableEntryCount++] = new DocumentVariableInspectorEntry(
                    block.Id,
                    variableName: "",
                    hasExpression: false,
                    expression: "",
                    isValidDeclaration: false,
                    invalidText: block.Text.PlainText);
                continue;
            }

            if (!_documentVariableNames.Add(variableName))
            {
                continue;
            }

            EnsureDocumentVariableEntryCapacity(_documentVariableEntryCount + 1);
            _documentVariableEntries[_documentVariableEntryCount++] = new DocumentVariableInspectorEntry(
                block.Id,
                variableName,
                hasExpression,
                expression,
                isValidDeclaration: true,
                invalidText: "");
        }

        return _documentVariableEntryCount;
    }

    private static void EnsureDocumentVariableEntryCapacity(int required)
    {
        if (required <= _documentVariableEntries.Length)
        {
            return;
        }

        int newLength = _documentVariableEntries.Length;
        while (newLength < required)
        {
            newLength *= 2;
        }

        Array.Resize(ref _documentVariableEntries, newLength);
    }

    private static void AddDocumentVariableBlock(DocWorkspace workspace, DocDocument document)
    {
        string variableName = BuildUniqueDocumentVariableName(document);
        string declarationText = "@" + variableName;
        int insertIndex = document.Blocks.Count;
        string order = insertIndex <= 0
            ? FractionalIndex.Initial()
            : FractionalIndex.After(document.Blocks[insertIndex - 1].Order);

        var variableBlock = new DocBlock
        {
            Type = DocBlockType.Variable,
            Order = order,
            Text = new RichText
            {
                PlainText = declarationText,
            },
        };

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.AddBlock,
            DocumentId = document.Id,
            BlockIndex = insertIndex,
            BlockSnapshot = variableBlock,
        });

        if (workspace.ActiveDocument != null &&
            string.Equals(workspace.ActiveDocument.Id, document.Id, StringComparison.Ordinal))
        {
            DocumentRenderer.FocusBlock(workspace, document, insertIndex, declarationText.Length);
        }
    }

    private static string BuildUniqueDocumentVariableName(DocDocument document)
    {
        _documentVariableNames.Clear();
        for (int blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            var block = document.Blocks[blockIndex];
            if (block.Type != DocBlockType.Variable)
            {
                continue;
            }

            if (DocumentFormulaSyntax.TryParseVariableDeclaration(
                    block.Text.PlainText,
                    out string variableName,
                    out _,
                    out _))
            {
                _documentVariableNames.Add(variableName);
            }
        }

        const string baseVariableName = "variable";
        if (!_documentVariableNames.Contains(baseVariableName))
        {
            return baseVariableName;
        }

        for (int suffix = 2; suffix < 10000; suffix++)
        {
            string candidate = baseVariableName + suffix;
            if (_documentVariableNames.Add(candidate))
            {
                return candidate;
            }
        }

        return baseVariableName + "_" + Guid.NewGuid().ToString("N");
    }

    private static void RemoveDocumentVariableEntry(
        DocWorkspace workspace,
        DocDocument document,
        in DocumentVariableInspectorEntry entry)
    {
        if (entry.IsValidDeclaration)
        {
            RemoveDocumentVariableBlocksByName(workspace, document, entry.VariableName);
            return;
        }

        RemoveDocumentVariableBlockById(workspace, document, entry.BlockId);
    }

    private static void RemoveDocumentVariableBlocksByName(
        DocWorkspace workspace,
        DocDocument document,
        string variableName)
    {
        _documentVariableRemoveCommandScratch.Clear();
        for (int blockIndex = document.Blocks.Count - 1; blockIndex >= 0; blockIndex--)
        {
            var block = document.Blocks[blockIndex];
            if (block.Type != DocBlockType.Variable)
            {
                continue;
            }

            if (!DocumentFormulaSyntax.TryParseVariableDeclaration(
                    block.Text.PlainText,
                    out string candidateVariableName,
                    out _,
                    out _))
            {
                continue;
            }

            if (!string.Equals(candidateVariableName, variableName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _documentVariableRemoveCommandScratch.Add(new DocCommand
            {
                Kind = DocCommandKind.RemoveBlock,
                DocumentId = document.Id,
                BlockIndex = blockIndex,
                BlockSnapshot = block.Clone(),
            });
        }

        if (_documentVariableRemoveCommandScratch.Count > 0)
        {
            workspace.ExecuteCommands(_documentVariableRemoveCommandScratch);
            _documentVariableRemoveCommandScratch.Clear();
        }
    }

    private static void RemoveDocumentVariableBlockById(
        DocWorkspace workspace,
        DocDocument document,
        string blockId)
    {
        for (int blockIndex = document.Blocks.Count - 1; blockIndex >= 0; blockIndex--)
        {
            var block = document.Blocks[blockIndex];
            if (!string.Equals(block.Id, blockId, StringComparison.Ordinal))
            {
                continue;
            }

            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.RemoveBlock,
                DocumentId = document.Id,
                BlockIndex = blockIndex,
                BlockSnapshot = block.Clone(),
            });
            return;
        }
    }

    private static string ResolveDocumentVariableDisplayValue(
        DocWorkspace workspace,
        DocDocument document,
        in DocumentVariableInspectorEntry entry,
        out bool valueIsValid)
    {
        if (!entry.IsValidDeclaration)
        {
            valueIsValid = false;
            return "#ERR";
        }

        if (!entry.HasExpression || string.IsNullOrWhiteSpace(entry.Expression))
        {
            valueIsValid = true;
            return "(none)";
        }

        return ResolveDocumentVariableValueDisplayText(
            workspace,
            document,
            entry.BlockId,
            entry.Expression,
            out valueIsValid);
    }

    private static string ResolveDocumentVariableValueDisplayText(
        DocWorkspace workspace,
        DocDocument document,
        string blockId,
        string expressionText,
        out bool valueIsValid)
    {
        if (_documentVariableValueCacheByBlockId.Count > 4096)
        {
            _documentVariableValueCacheByBlockId.Clear();
        }

        if (_documentVariableValueCacheByBlockId.TryGetValue(blockId, out var cacheEntry) &&
            cacheEntry.ProjectRevision == workspace.ProjectRevision &&
            string.Equals(cacheEntry.ExpressionText, expressionText, StringComparison.Ordinal))
        {
            valueIsValid = cacheEntry.IsValid;
            return cacheEntry.ResultText;
        }

        bool valid = _documentFormulaPreviewEngine.TryEvaluateDocumentExpression(
            workspace.Project,
            document,
            expressionText,
            out string evaluatedText);
        string resultText = valid ? evaluatedText : "#ERR";
        _documentVariableValueCacheByBlockId[blockId] = new DocumentVariableValueCacheEntry(
            workspace.ProjectRevision,
            expressionText,
            resultText,
            valid);

        valueIsValid = valid;
        return resultText;
    }

    // --- Derived table sections ---

    private static float DrawSchemaLinkSection(
        DocWorkspace workspace,
        DocTable table,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        y = DrawCollapsibleHeader("Schema Link", ref _schemaLinkExpanded, contentRect, y, style);
        if (!_schemaLinkExpanded)
        {
            return y;
        }

        y += SectionHeaderContentSpacing;
        float rowX = contentRect.X + SectionHorizontalInset + RowHorizontalInset;
        float rowW = contentRect.Width - (SectionHorizontalInset + RowHorizontalInset) * 2f;

        if (table.IsDerived)
        {
            float textY = y + (RowHeight - style.FontSize) * 0.5f;
            Im.Text("Derived tables cannot be schema-linked.".AsSpan(), rowX, textY, style.FontSize - 1f, style.TextSecondary);
            return y + RowHeight;
        }

        if (table.IsInherited)
        {
            float textY = y + (RowHeight - style.FontSize) * 0.5f;
            Im.Text("Inherited tables cannot be schema-linked.".AsSpan(), rowX, textY, style.FontSize - 1f, style.TextSecondary);
            return y + RowHeight;
        }

        var project = workspace.Project;
        int needed = project.Tables.Count + 1;
        EnsureScratchCapacity(needed);

        _scratchOptionNames[0] = "(none)";
        _scratchOptionIds[0] = "";
        int count = 1;
        int selectedIdx = 0;
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            DocTable candidateTable = project.Tables[tableIndex];
            if (string.Equals(candidateTable.Id, table.Id, StringComparison.Ordinal))
            {
                continue;
            }

            if (WouldCreateSchemaLinkCycle(project, table.Id, candidateTable.Id))
            {
                continue;
            }

            _scratchOptionNames[count] = candidateTable.Name;
            _scratchOptionIds[count] = candidateTable.Id;
            if (string.Equals(table.SchemaSourceTableId, candidateTable.Id, StringComparison.Ordinal))
            {
                selectedIdx = count;
            }

            count++;
        }

        _schemaSourceDropdownIndex = selectedIdx;

        float textLabelY = y + (RowHeight - style.FontSize) * 0.5f;
        Im.Text("Source:".AsSpan(), rowX, textLabelY, style.FontSize, style.TextSecondary);

        Im.Context.PushId(table.Id);
        _ = Im.Dropdown(
            "schema_source_dd",
            _scratchOptionNames.AsSpan(0, count),
            ref _schemaSourceDropdownIndex,
            rowX + 56f,
            y,
            rowW - 56f,
            InspectorDropdownFlags);
        string? selectedSourceId = _schemaSourceDropdownIndex > 0 ? _scratchOptionIds[_schemaSourceDropdownIndex] : null;
        string? oldSourceId = string.IsNullOrWhiteSpace(table.SchemaSourceTableId) ? null : table.SchemaSourceTableId;
        if (!string.Equals(oldSourceId, selectedSourceId, StringComparison.Ordinal))
        {
            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.SetTableSchemaSource,
                TableId = table.Id,
                OldSchemaSourceTableId = oldSourceId,
                NewSchemaSourceTableId = string.IsNullOrWhiteSpace(selectedSourceId) ? null : selectedSourceId,
            });
        }

        Im.Context.PopId();
        y += RowHeight + DropdownBottomSpacing;

        float rowTextY = y + (RowHeight - style.FontSize) * 0.5f;
        Im.Text("Linked to:".AsSpan(), rowX, rowTextY, style.FontSize - 1f, style.TextSecondary);
        if (string.IsNullOrWhiteSpace(table.SchemaSourceTableId))
        {
            Im.Text("(none)".AsSpan(), rowX + 70f, rowTextY, style.FontSize - 1f, style.TextSecondary);
        }
        else
        {
            DocTable? sourceTable = FindTableById(workspace, table.SchemaSourceTableId);
            if (sourceTable == null)
            {
                Im.Text("(missing)".AsSpan(), rowX + 70f, rowTextY, style.FontSize - 1f, style.Secondary);
            }
            else
            {
                Im.Text(sourceTable.Name.AsSpan(), rowX + 70f, rowTextY, style.FontSize - 1f, style.TextPrimary);
            }
        }

        y += RowHeight;
        if (table.IsSchemaLinked)
        {
            float infoTextY = y + (RowHeight - style.FontSize) * 0.5f;
            Im.Text("Columns are source-controlled.".AsSpan(), rowX, infoTextY, style.FontSize - 1f, style.TextSecondary);
            y += RowHeight;
        }

        return y;
    }

    private static float DrawInheritanceSection(
        DocWorkspace workspace,
        DocTable table,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        y = DrawCollapsibleHeader("Inheritance", ref _inheritanceExpanded, contentRect, y, style);
        if (!_inheritanceExpanded)
        {
            return y;
        }

        y += SectionHeaderContentSpacing;
        float rowX = contentRect.X + SectionHorizontalInset + RowHorizontalInset;
        float rowW = contentRect.Width - (SectionHorizontalInset + RowHorizontalInset) * 2f;

        if (table.IsDerived)
        {
            float textY = y + (RowHeight - style.FontSize) * 0.5f;
            Im.Text("Derived tables cannot inherit schema.".AsSpan(), rowX, textY, style.FontSize - 1f, style.TextSecondary);
            return y + RowHeight;
        }

        if (table.IsSchemaLinked)
        {
            float textY = y + (RowHeight - style.FontSize) * 0.5f;
            Im.Text("Schema-linked tables cannot use inheritance.".AsSpan(), rowX, textY, style.FontSize - 1f, style.TextSecondary);
            return y + RowHeight;
        }

        var project = workspace.Project;
        int needed = project.Tables.Count + 1;
        EnsureScratchCapacity(needed);

        _scratchOptionNames[0] = "(none)";
        _scratchOptionIds[0] = "";
        int count = 1;
        int selectedIdx = 0;
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            DocTable candidateTable = project.Tables[tableIndex];
            if (string.Equals(candidateTable.Id, table.Id, StringComparison.Ordinal))
            {
                continue;
            }

            if (WouldCreateInheritanceCycle(project, table.Id, candidateTable.Id))
            {
                continue;
            }

            _scratchOptionNames[count] = candidateTable.Name;
            _scratchOptionIds[count] = candidateTable.Id;
            if (string.Equals(table.InheritanceSourceTableId, candidateTable.Id, StringComparison.Ordinal))
            {
                selectedIdx = count;
            }

            count++;
        }

        _inheritanceSourceDropdownIndex = selectedIdx;

        float textLabelY = y + (RowHeight - style.FontSize) * 0.5f;
        Im.Text("Source:".AsSpan(), rowX, textLabelY, style.FontSize, style.TextSecondary);

        Im.Context.PushId(table.Id);
        _ = Im.Dropdown(
            "inheritance_source_dd",
            _scratchOptionNames.AsSpan(0, count),
            ref _inheritanceSourceDropdownIndex,
            rowX + 56f,
            y,
            rowW - 56f,
            InspectorDropdownFlags);
        string? selectedSourceId = _inheritanceSourceDropdownIndex > 0 ? _scratchOptionIds[_inheritanceSourceDropdownIndex] : null;
        string? oldSourceId = string.IsNullOrWhiteSpace(table.InheritanceSourceTableId) ? null : table.InheritanceSourceTableId;
        if (!string.Equals(oldSourceId, selectedSourceId, StringComparison.Ordinal))
        {
            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.SetTableInheritanceSource,
                TableId = table.Id,
                OldInheritanceSourceTableId = oldSourceId,
                NewInheritanceSourceTableId = string.IsNullOrWhiteSpace(selectedSourceId) ? null : selectedSourceId,
            });
        }

        Im.Context.PopId();
        y += RowHeight + DropdownBottomSpacing;

        float rowTextY = y + (RowHeight - style.FontSize) * 0.5f;
        Im.Text("Inherited from:".AsSpan(), rowX, rowTextY, style.FontSize - 1f, style.TextSecondary);
        if (string.IsNullOrWhiteSpace(table.InheritanceSourceTableId))
        {
            Im.Text("(none)".AsSpan(), rowX + 92f, rowTextY, style.FontSize - 1f, style.TextSecondary);
        }
        else
        {
            DocTable? sourceTable = FindTableById(workspace, table.InheritanceSourceTableId);
            if (sourceTable == null)
            {
                Im.Text("(missing)".AsSpan(), rowX + 92f, rowTextY, style.FontSize - 1f, style.Secondary);
            }
            else
            {
                Im.Text(sourceTable.Name.AsSpan(), rowX + 92f, rowTextY, style.FontSize - 1f, style.TextPrimary);
            }
        }

        y += RowHeight;
        if (table.IsInherited)
        {
            float infoTextY = y + (RowHeight - style.FontSize) * 0.5f;
            Im.Text("Inherited columns are locked. Local columns stay editable.".AsSpan(), rowX, infoTextY, style.FontSize - 1f, style.TextSecondary);
            y += RowHeight;
        }

        return y;
    }

    private static float DrawVariantsSection(
        DocWorkspace workspace,
        DocTable table,
        DocBlock? inspectedBlock,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        bool isInstanceContext = inspectedBlock != null;
        if (!string.Equals(_pendingTableVariantDeleteTableId, table.Id, StringComparison.Ordinal))
        {
            ClearPendingTableVariantDelete();
        }

        float headerX = contentRect.X + SectionHorizontalInset;
        float headerWidth = contentRect.Width - (SectionHorizontalInset * 2f);
        float addButtonX = headerX + headerWidth - Padding - DeleteButtonSize;
        float addButtonY = y + (SectionHeaderHeight - DeleteButtonSize) * 0.5f;
        float headerRightOverlayWidth = isInstanceContext ? 0f : DeleteButtonSize + Padding + 4f;
        int variantCount = table.Variants.Count + 1; // base + custom variants

        y = DrawCollapsibleHeader("Variants", ref _variantsExpanded, contentRect, y, style, variantCount, headerRightOverlayWidth);
        if (!isInstanceContext)
        {
            Im.Context.PushId(table.Id);
            if (Im.Button(PlusIcon, addButtonX, addButtonY, DeleteButtonSize, DeleteButtonSize))
            {
                if (TryAddTableVariant(workspace, table, out int createdVariantId))
                {
                    workspace.SetSelectedVariantIdForTable(table.Id, createdVariantId);
                }
            }
            Im.Context.PopId();
        }

        if (!_variantsExpanded)
        {
            return y;
        }

        y += SectionHeaderContentSpacing;
        float rowX = contentRect.X + SectionHorizontalInset + RowHorizontalInset;
        float rowW = contentRect.Width - (SectionHorizontalInset + RowHorizontalInset) * 2f;
        int requiredCount = Math.Max(variantCount, 1);
        EnsureScratchCapacity(requiredCount);
        EnsureVariantOptionCapacity(requiredCount);

        int optionCount = 0;
        _scratchOptionNames[optionCount] = DocTableVariant.BaseVariantName;
        _variantOptionIds[optionCount] = DocTableVariant.BaseVariantId;
        optionCount++;
        for (int variantIndex = 0; variantIndex < table.Variants.Count; variantIndex++)
        {
            DocTableVariant variant = table.Variants[variantIndex];
            _scratchOptionNames[optionCount] = variant.Name;
            _variantOptionIds[optionCount] = variant.Id;
            optionCount++;
        }

        int selectedVariantId = isInstanceContext && inspectedBlock != null
            ? inspectedBlock.TableVariantId
            : workspace.GetSelectedVariantIdForTable(table);
        int selectedIndex = 0;
        for (int optionIndex = 0; optionIndex < optionCount; optionIndex++)
        {
            if (_variantOptionIds[optionIndex] == selectedVariantId)
            {
                selectedIndex = optionIndex;
                break;
            }
        }

        if (isInstanceContext)
        {
            _instanceVariantDropdownIndex = selectedIndex;
        }
        else
        {
            _tableVariantDropdownIndex = selectedIndex;
        }

        float textY = y + (RowHeight - style.FontSize) * 0.5f;
        Im.Text("Selected:".AsSpan(), rowX, textY, style.FontSize - 1f, style.TextSecondary);

        float dropdownX = rowX + 64f;
        float dropdownWidth = Math.Max(120f, rowW - 64f);
        bool changed = false;
        if (isInstanceContext)
        {
            changed = Im.Dropdown(
                "instance_variant_dd",
                _scratchOptionNames.AsSpan(0, optionCount),
                ref _instanceVariantDropdownIndex,
                dropdownX,
                y,
                dropdownWidth,
                InspectorDropdownFlags);
            if (changed && inspectedBlock != null)
            {
                ExecuteSetBlockTableVariantCommand(workspace, inspectedBlock, _variantOptionIds[_instanceVariantDropdownIndex]);
            }
        }
        else
        {
            changed = Im.Dropdown(
                "table_variant_dd",
                _scratchOptionNames.AsSpan(0, optionCount),
                ref _tableVariantDropdownIndex,
                dropdownX,
                y,
                dropdownWidth,
                InspectorDropdownFlags);
            if (changed)
            {
                workspace.SetSelectedVariantIdForTable(table.Id, _variantOptionIds[_tableVariantDropdownIndex]);
            }
        }

        y += RowHeight + DropdownBottomSpacing;

        if (isInstanceContext)
        {
            ClearPendingTableVariantDelete();
            float infoY = y + (RowHeight - style.FontSize) * 0.5f;
            Im.Text("Applies to this embedded table only.".AsSpan(), rowX, infoY, style.FontSize - 1f, style.TextSecondary);
            y += RowHeight;
            return y;
        }

        if (selectedVariantId != DocTableVariant.BaseVariantId)
        {
            float infoY = y + (RowHeight - style.FontSize) * 0.5f;
            Im.Text("Row and cell edits write variant deltas.".AsSpan(), rowX, infoY, style.FontSize - 1f, style.TextSecondary);
            y += RowHeight;
        }

        y += 2f;
        int editableVariantCount = table.Variants.Count;

        if (editableVariantCount <= 0)
        {
            float emptyY = y + (RowHeight - style.FontSize) * 0.5f;
            Im.Text("No custom variants yet.".AsSpan(), rowX + 4f, emptyY, style.FontSize - 1f, style.TextSecondary);
            y += RowHeight;
            return y;
        }

        EnsureTableVariantNameBufferCapacity(table.Variants.Count);
        if (_pendingTableVariantDeleteId > DocTableVariant.BaseVariantId &&
            !TableVariantIdExists(table, _pendingTableVariantDeleteId))
        {
            ClearPendingTableVariantDelete();
        }

        int deleteVariantId = -1;
        for (int variantIndex = 0; variantIndex < table.Variants.Count; variantIndex++)
        {
            DocTableVariant variant = table.Variants[variantIndex];

            float rowY = y;
            bool rowHovered = new ImRect(rowX, rowY, rowW, RowHeight).Contains(Im.MousePos);
            if (rowHovered)
            {
                Im.DrawRect(rowX, rowY, rowW, RowHeight, ImStyle.WithAlpha(style.Hover, 88));
            }

            int labelLength = 0;
            _variantIdLabelBuffer[labelLength++] = 'v';
            variant.Id.TryFormat(_variantIdLabelBuffer.AsSpan(labelLength), out int idWritten);
            labelLength += idWritten;

            float textYVariant = rowY + (RowHeight - style.FontSize) * 0.5f;
            float variantLabelX = rowX + 4f;
            Im.Text(_variantIdLabelBuffer.AsSpan(0, labelLength), variantLabelX, textYVariant, style.FontSize - 1f, style.TextSecondary);

            bool isPendingDeleteVariant = _pendingTableVariantDeleteId == variant.Id &&
                                          string.Equals(_pendingTableVariantDeleteTableId, table.Id, StringComparison.Ordinal);
            float deleteButtonX = rowX + rowW - DeleteButtonSize;
            float confirmButtonX = deleteButtonX - DeleteButtonSize - 4f;
            float nameX = rowX + 44f;
            float nameWidth = MathF.Max(120f, (isPendingDeleteVariant ? confirmButtonX : deleteButtonX) - nameX - 6f);

            SyncTableVariantNameBuffer(variantIndex, variant.Name);
            Im.Context.PushId(variant.Id);
            Im.TextInput(
                "variant_name",
                _tableVariantNameBuffers[variantIndex],
                ref _tableVariantNameLengths[variantIndex],
                _tableVariantNameBuffers[variantIndex].Length,
                nameX,
                rowY,
                nameWidth);
            if (ShouldCommitTextInput("variant_name", ref _tableVariantNameFocused[variantIndex]))
            {
                string editedVariantName = new string(
                    _tableVariantNameBuffers[variantIndex],
                    0,
                    _tableVariantNameLengths[variantIndex]).Trim();
                if (!string.Equals(editedVariantName, variant.Name, StringComparison.Ordinal))
                {
                    if (TryRenameTableVariant(workspace, table.Id, variant.Id, editedVariantName))
                    {
                        _tableVariantNameSyncKeys[variantIndex] = editedVariantName;
                    }
                    else
                    {
                        _tableVariantNameSyncKeys[variantIndex] = "";
                    }
                }
            }

            float buttonY = rowY + (RowHeight - DeleteButtonSize) * 0.5f;
            if (isPendingDeleteVariant)
            {
                if (Im.Button(CheckIcon, confirmButtonX, buttonY, DeleteButtonSize, DeleteButtonSize))
                {
                    deleteVariantId = variant.Id;
                }

                if (Im.Button(CloseIcon, deleteButtonX, buttonY, DeleteButtonSize, DeleteButtonSize))
                {
                    ClearPendingTableVariantDelete();
                }
            }
            else if (rowHovered && Im.Button(MinusIcon, deleteButtonX, buttonY, DeleteButtonSize, DeleteButtonSize))
            {
                _pendingTableVariantDeleteId = variant.Id;
                _pendingTableVariantDeleteTableId = table.Id;
            }
            Im.Context.PopId();

            Im.DrawLine(rowX, rowY + RowHeight, rowX + rowW, rowY + RowHeight, 1f, ImStyle.WithAlpha(style.Border, 80));
            y += RowHeight;
        }

        if (deleteVariantId > DocTableVariant.BaseVariantId)
        {
            if (selectedVariantId == deleteVariantId)
            {
                workspace.SetSelectedVariantIdForTable(table.Id, DocTableVariant.BaseVariantId);
            }

            TryDeleteTableVariant(workspace, table.Id, deleteVariantId);
            ClearPendingTableVariantDelete();
        }

        return y;
    }
    private static bool TableVariantIdExists(DocTable table, int variantId)
    {
        if (variantId == DocTableVariant.BaseVariantId)
        {
            return true;
        }

        for (int variantIndex = 0; variantIndex < table.Variants.Count; variantIndex++)
        {
            if (table.Variants[variantIndex].Id == variantId)
            {
                return true;
            }
        }

        return false;
    }

    private static void ClearPendingTableVariantDelete()
    {
        _pendingTableVariantDeleteId = DocTableVariant.BaseVariantId;
        _pendingTableVariantDeleteTableId = "";
    }

    private static bool TryAddTableVariant(DocWorkspace workspace, DocTable table, out int createdVariantId)
    {
        createdVariantId = DocTableVariant.BaseVariantId;
        if (table.IsDerived)
        {
            return false;
        }

        int nextVariantId = FindNextTableVariantId(table);
        int insertIndex = FindTableVariantInsertIndex(table, nextVariantId);
        string variantName = BuildDefaultTableVariantName(table, nextVariantId);
        var variantSnapshot = new DocTableVariant
        {
            Id = nextVariantId,
            Name = variantName,
        };

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.AddTableVariant,
            TableId = table.Id,
            TableVariantIndex = insertIndex,
            TableVariantSnapshot = variantSnapshot,
        });

        createdVariantId = nextVariantId;
        return true;
    }

    private static int FindNextTableVariantId(DocTable table)
    {
        int nextVariantId = DocTableVariant.BaseVariantId + 1;
        bool found;
        do
        {
            found = false;
            for (int variantIndex = 0; variantIndex < table.Variants.Count; variantIndex++)
            {
                if (table.Variants[variantIndex].Id == nextVariantId)
                {
                    nextVariantId++;
                    found = true;
                    break;
                }
            }
        } while (found);

        return nextVariantId;
    }

    private static int FindTableVariantInsertIndex(DocTable table, int variantId)
    {
        for (int variantIndex = 0; variantIndex < table.Variants.Count; variantIndex++)
        {
            if (table.Variants[variantIndex].Id > variantId)
            {
                return variantIndex;
            }
        }

        return table.Variants.Count;
    }

    private static string BuildDefaultTableVariantName(DocTable table, int variantId)
    {
        string baseName = "Variant " + variantId.ToString(CultureInfo.InvariantCulture);
        if (!TableVariantNameExists(table, baseName))
        {
            return baseName;
        }

        for (int suffix = 2; suffix < 10000; suffix++)
        {
            string candidate = baseName + " " + suffix.ToString(CultureInfo.InvariantCulture);
            if (!TableVariantNameExists(table, candidate))
            {
                return candidate;
            }
        }

        return "Variant " + Guid.NewGuid().ToString("N");
    }

    private static bool TableVariantNameExists(DocTable table, string candidateName)
    {
        if (string.Equals(candidateName, DocTableVariant.BaseVariantName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        for (int variantIndex = 0; variantIndex < table.Variants.Count; variantIndex++)
        {
            if (string.Equals(table.Variants[variantIndex].Name, candidateName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryRenameTableVariant(
        DocWorkspace workspace,
        string tableId,
        int variantId,
        string newVariantName)
    {
        if (variantId == DocTableVariant.BaseVariantId ||
            string.IsNullOrWhiteSpace(newVariantName))
        {
            return false;
        }

        DocTable? table = workspace.Project.Tables.Find(t => string.Equals(t.Id, tableId, StringComparison.Ordinal));
        if (table == null)
        {
            return false;
        }

        if (TableVariantNameExistsForOtherVariant(table, variantId, newVariantName))
        {
            return false;
        }

        DocProject oldProjectSnapshot = workspace.CreateProjectSnapshot();
        DocProject newProjectSnapshot = workspace.CreateProjectSnapshot();
        DocTable? snapshotTable = newProjectSnapshot.Tables.Find(t => string.Equals(t.Id, tableId, StringComparison.Ordinal));
        if (snapshotTable == null)
        {
            return false;
        }

        int variantIndex = FindTableVariantIndex(snapshotTable, variantId);
        if (variantIndex < 0)
        {
            return false;
        }

        snapshotTable.Variants[variantIndex].Name = newVariantName.Trim();
        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.ReplaceProjectSnapshot,
            OldProjectSnapshot = oldProjectSnapshot,
            NewProjectSnapshot = newProjectSnapshot,
        });
        return true;
    }

    private static bool TryDeleteTableVariant(DocWorkspace workspace, string tableId, int variantId)
    {
        if (variantId == DocTableVariant.BaseVariantId)
        {
            return false;
        }

        DocProject oldProjectSnapshot = workspace.CreateProjectSnapshot();
        DocProject newProjectSnapshot = workspace.CreateProjectSnapshot();
        DocTable? table = newProjectSnapshot.Tables.Find(t => string.Equals(t.Id, tableId, StringComparison.Ordinal));
        if (table == null)
        {
            return false;
        }

        int variantIndex = FindTableVariantIndex(table, variantId);
        if (variantIndex < 0)
        {
            return false;
        }

        table.Variants.RemoveAt(variantIndex);

        for (int deltaIndex = table.VariantDeltas.Count - 1; deltaIndex >= 0; deltaIndex--)
        {
            if (table.VariantDeltas[deltaIndex].VariantId == variantId)
            {
                table.VariantDeltas.RemoveAt(deltaIndex);
            }
        }

        for (int documentIndex = 0; documentIndex < newProjectSnapshot.Documents.Count; documentIndex++)
        {
            DocDocument document = newProjectSnapshot.Documents[documentIndex];
            for (int blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
            {
                DocBlock block = document.Blocks[blockIndex];
                if (block.Type != DocBlockType.Table)
                {
                    continue;
                }

                if (string.Equals(block.TableId, tableId, StringComparison.Ordinal) &&
                    block.TableVariantId == variantId)
                {
                    block.TableVariantId = DocTableVariant.BaseVariantId;
                }
            }
        }

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.ReplaceProjectSnapshot,
            OldProjectSnapshot = oldProjectSnapshot,
            NewProjectSnapshot = newProjectSnapshot,
        });
        return true;
    }

    private static bool TableVariantNameExistsForOtherVariant(DocTable table, int variantId, string candidateName)
    {
        if (string.Equals(candidateName, DocTableVariant.BaseVariantName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        for (int variantIndex = 0; variantIndex < table.Variants.Count; variantIndex++)
        {
            DocTableVariant tableVariant = table.Variants[variantIndex];
            if (tableVariant.Id == variantId)
            {
                continue;
            }

            if (string.Equals(tableVariant.Name, candidateName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int FindTableVariantIndex(DocTable table, int variantId)
    {
        for (int variantIndex = 0; variantIndex < table.Variants.Count; variantIndex++)
        {
            if (table.Variants[variantIndex].Id == variantId)
            {
                return variantIndex;
            }
        }

        return -1;
    }

    private static void ExecuteSetBlockTableVariantCommand(
        DocWorkspace workspace,
        DocBlock block,
        int newVariantId)
    {
        if (workspace.ActiveDocument == null)
        {
            return;
        }

        if (block.TableVariantId == newVariantId)
        {
            return;
        }

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetBlockTableVariant,
            DocumentId = workspace.ActiveDocument.Id,
            BlockId = block.Id,
            OldBlockTableVariantId = block.TableVariantId,
            NewBlockTableVariantId = newVariantId,
        });
    }

    private static float DrawDerivedBaseTableSection(
        DocWorkspace workspace,
        DocTable table,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        var config = table.DerivedConfig!;

        // Section header
        y = DrawCollapsibleHeader("Base Table", ref _derivedBaseExpanded, contentRect, y, style);
        if (!_derivedBaseExpanded) return y;
        y += SectionHeaderContentSpacing;

        float rowX = contentRect.X + SectionHorizontalInset + RowHorizontalInset;
        float rowW = contentRect.Width - (SectionHorizontalInset + RowHorizontalInset) * 2f;

        // Build list of non-derived tables for dropdown
        var project = workspace.Project;
        int needed = project.Tables.Count + 1;
        EnsureScratchCapacity(needed);

        _scratchOptionNames[0] = "(none)";
        _scratchOptionIds[0] = "";
        int count = 1;
        int selectedIdx = 0;
        for (int i = 0; i < project.Tables.Count; i++)
        {
            var t = project.Tables[i];
            if (t.Id == table.Id || t.IsDerived)
            {
                continue;
            }

            _scratchOptionNames[count] = t.Name;
            _scratchOptionIds[count] = t.Id;
            if (string.Equals(config.BaseTableId, t.Id, StringComparison.Ordinal))
                selectedIdx = count;
            count++;
        }
        _baseTableDropdownIndex = selectedIdx;

        float textY = y + (RowHeight - style.FontSize) * 0.5f;
        Im.Text("Base:".AsSpan(), rowX, textY, style.FontSize, style.TextSecondary);

        Im.Context.PushId("derived_base_table");
        _ = Im.Dropdown(
            "derived_base_dd",
            _scratchOptionNames.AsSpan(0, count),
            ref _baseTableDropdownIndex,
            rowX + 50,
            y,
            rowW - 50,
            InspectorDropdownFlags);
        string? selectedBaseId = _baseTableDropdownIndex > 0 ? _scratchOptionIds[_baseTableDropdownIndex] : null;
        if (!string.Equals(config.BaseTableId, selectedBaseId, StringComparison.Ordinal))
        {
            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.SetDerivedBaseTable,
                TableId = table.Id,
                OldBaseTableId = config.BaseTableId,
                NewBaseTableId = string.IsNullOrEmpty(selectedBaseId) ? null : selectedBaseId,
            });
        }
        Im.Context.PopId();
        y += RowHeight + DropdownBottomSpacing;

        // Show row count
        if (config.BaseTableId != null)
        {
            DocTable? baseTable = null;
            for (int ti = 0; ti < project.Tables.Count; ti++)
            {
                var t = project.Tables[ti];
                if (string.Equals(t.Id, config.BaseTableId, StringComparison.Ordinal))
                {
                    baseTable = t;
                    break;
                }
            }
            if (baseTable != null)
            {
                textY = y + (RowHeight - style.FontSize) * 0.5f;
                Span<char> rowCountBuf = stackalloc char[32];
                int w = 0;
                "Rows: ".AsSpan().CopyTo(rowCountBuf); w += 6;
                baseTable.Rows.Count.TryFormat(rowCountBuf.Slice(w), out int n1); w += n1;
                Im.Text(rowCountBuf.Slice(0, w), rowX, textY, style.FontSize - 1f, style.TextSecondary);
                y += RowHeight;
            }
        }

        return y;
    }

    private static float DrawDerivedStepsSection(
        DocWorkspace workspace,
        DocTable table,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        var config = table.DerivedConfig!;
        var project = workspace.Project;

        float headerTop = y;
        float headerX = contentRect.X + SectionHorizontalInset;
        float headerW = contentRect.Width - SectionHorizontalInset * 2f;
        float addStepButtonSize = 22f;
        float addStepButtonX = headerX + headerW - Padding - addStepButtonSize;
        float addStepButtonY = headerTop + (SectionHeaderHeight - addStepButtonSize) * 0.5f;
        float headerRightOverlay = addStepButtonSize + Padding + 4f;

        // Section header
        y = DrawCollapsibleHeader("Steps", ref _derivedStepsExpanded, contentRect, y, style,
            config.Steps.Count, headerRightOverlay);
        Im.Context.PushId(table.Id);
        if (Im.Button(PlusIcon, addStepButtonX, addStepButtonY, addStepButtonSize, addStepButtonSize))
        {
            ImContextMenu.OpenAt(AddStepMenuId, addStepButtonX, addStepButtonY + addStepButtonSize);
        }
        if (ImContextMenu.Begin(AddStepMenuId))
        {
            if (ImContextMenu.Item(AddStepJoinLabel))
            {
                string defaultSourceId = FindFirstOtherTableId(project, table.Id);
                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.AddDerivedStep,
                    TableId = table.Id,
                    StepIndex = config.Steps.Count,
                    StepSnapshot = new DerivedStep { Kind = DerivedStepKind.Join, SourceTableId = defaultSourceId },
                });
            }
            if (ImContextMenu.Item(AddStepAppendLabel))
            {
                string defaultSourceId = FindFirstOtherTableId(project, table.Id);
                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.AddDerivedStep,
                    TableId = table.Id,
                    StepIndex = config.Steps.Count,
                    StepSnapshot = new DerivedStep { Kind = DerivedStepKind.Append, SourceTableId = defaultSourceId },
                });
            }
            ImContextMenu.End();
        }
        Im.Context.PopId();
        if (!_derivedStepsExpanded) return y;
        y += SectionHeaderContentSpacing;

        float rowX = contentRect.X + SectionHorizontalInset + RowHorizontalInset;
        float rowW = contentRect.Width - (SectionHorizontalInset + RowHorizontalInset) * 2f;

        // Build source table options
        EnsureScratchCapacity(project.Tables.Count);
        EnsureSourceTableOptionCapacity(project.Tables.Count);
        int sourceCount = 0;
        for (int i = 0; i < project.Tables.Count; i++)
        {
            var t = project.Tables[i];
            if (t.Id == table.Id) continue;
            _sourceTableOptionNames[sourceCount] = t.Name;
            _sourceTableOptionIds[sourceCount] = t.Id;
            sourceCount++;
        }

        int stepCount = config.Steps.Count;
        if (_dragStepIndex >= stepCount) _dragStepIndex = -1;
        if (_dragStepTableId != null && !string.Equals(_dragStepTableId, table.Id, StringComparison.Ordinal))
        {
            _dragStepIndex = -1;
            _dragStepTargetIndex = -1;
            _dragStepTableId = null;
        }

        // Precompute step block layout so dragging can compute insertion indices without allocating.
        float layoutY = y;
        int layoutCount = Math.Min(stepCount, _stepBlockTops.Length);
        for (int i = 0; i < layoutCount; i++)
        {
            _stepBlockTops[i] = layoutY;
            float h = RowHeight;
            if (config.Steps[i].Kind == DerivedStepKind.Join)
            {
                h += RowHeight + StepInnerGap;
            }
            h += StepBlockSpacing;
            _stepBlockHeights[i] = h;
            layoutY += h;
        }

        for (int stepIdx = 0; stepIdx < stepCount; stepIdx++)
        {
            if (stepIdx >= config.Steps.Count)
            {
                break;
            }

            var step = config.Steps[stepIdx];
            Im.Context.PushId(stepIdx);

            float stepTop = stepIdx < layoutCount ? _stepBlockTops[stepIdx] : y;
            float stepContentHeight = step.Kind == DerivedStepKind.Join ? RowHeight * 2f + StepInnerGap : RowHeight;
            float stepHeight = stepContentHeight + StepBlockSpacing;
            var stepBlockRect = new ImRect(rowX, stepTop, rowW, stepContentHeight);
            float stepGutterX = rowX - (GutterLaneWidth - ControlSpacing);
            var stepGutterRect = new ImRect(stepGutterX, stepTop, GutterLaneWidth, stepContentHeight);
            bool stepRowHovered = stepBlockRect.Contains(Im.MousePos);
            bool stepGutterHovered = stepGutterRect.Contains(Im.MousePos);
            bool stepDragged = _dragStepIndex == stepIdx &&
                               string.Equals(_dragStepTableId, table.Id, StringComparison.Ordinal) &&
                               Im.Context.Input.MouseDown;

            if (stepGutterHovered || stepDragged)
            {
                Im.DrawRect(rowX, stepTop, rowW, stepContentHeight, ImStyle.WithAlpha(style.Hover, 104));
            }

            if (stepGutterHovered || stepDragged)
            {
                Im.DrawRect(stepGutterX, stepTop, GutterLaneWidth, stepContentHeight, style.Surface);
            }

            // Step header: kind label + source table dropdown + remove button
            float textYStep = stepTop + (RowHeight - style.FontSize) * 0.5f;
            float btnY = stepTop + (RowHeight - DeleteButtonSize) * 0.5f;

            float handleX = rowX;
            if (stepGutterHovered && Im.Context.Input.MousePressed)
            {
                _dragStepIndex = stepIdx;
                _dragStepTargetIndex = stepIdx;
                _dragStepMouseOffsetY = Im.MousePos.Y - stepTop;
                _dragStepTableId = table.Id;
            }

            if (stepGutterHovered || stepDragged)
            {
                uint handleDotColor = stepDragged ? style.TextPrimary : style.TextSecondary;
                DrawGutterHandleDots(stepGutterX, stepTop, stepContentHeight, handleDotColor);
            }

            float controlsX = handleX + ControlSpacing;
            float kindX = controlsX;
            string kindLabel = step.Kind == DerivedStepKind.Append ? "Append" : "Join";
            Im.Text(kindLabel.AsSpan(), kindX, textYStep, style.FontSize - 1f, style.TextPrimary);

            float kindLabelWidth = step.Kind == DerivedStepKind.Join ? 30f : 48f;
            float joinModeWidth = step.Kind == DerivedStepKind.Join ? 64f : 0f;
            float joinModeX = controlsX + kindLabelWidth + ControlSpacing;
            float sourceX = joinModeX;
            if (step.Kind == DerivedStepKind.Join)
            {
                int joinKindIndex = step.JoinKind == DerivedJoinKind.Inner ? 1 : 0;
                if (Im.Dropdown("join_mode", JoinKindOptions, ref joinKindIndex, joinModeX, stepTop, joinModeWidth, InspectorDropdownFlags))
                {
                    var newStep = step.Clone();
                    newStep.JoinKind = joinKindIndex == 1 ? DerivedJoinKind.Inner : DerivedJoinKind.Left;
                    workspace.ExecuteCommand(new DocCommand
                    {
                        Kind = DocCommandKind.UpdateDerivedStep,
                        TableId = table.Id,
                        StepIndex = stepIdx,
                        OldStepSnapshot = step.Clone(),
                        StepSnapshot = newStep,
                    });
                }
                sourceX += joinModeWidth + ControlSpacing;
            }

            // Source table dropdown
            int srcIdx = 0;
            for (int i = 0; i < sourceCount; i++)
            {
                if (string.Equals(_sourceTableOptionIds[i], step.SourceTableId, StringComparison.Ordinal))
                { srcIdx = i; break; }
            }
            if (stepIdx < _stepSourceTableIndex.Length) _stepSourceTableIndex[stepIdx] = srcIdx;

            float removeBtnX = rowX + rowW - DeleteButtonSize;
            float sourceW = Math.Max(0f, removeBtnX - sourceX - ControlSpacing);
            if (sourceCount > 0 && stepIdx < _stepSourceTableIndex.Length &&
                Im.Dropdown("step_src", _sourceTableOptionNames.AsSpan(0, sourceCount), ref _stepSourceTableIndex[stepIdx], sourceX, stepTop, sourceW, InspectorDropdownFlags))
            {
                var newStep = step.Clone();
                newStep.SourceTableId = _sourceTableOptionIds[_stepSourceTableIndex[stepIdx]];
                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.UpdateDerivedStep,
                    TableId = table.Id,
                    StepIndex = stepIdx,
                    OldStepSnapshot = step.Clone(),
                    StepSnapshot = newStep,
                });
            }

            // Remove button
            if (stepRowHovered && Im.Button(MinusIcon, removeBtnX, btnY, DeleteButtonSize, DeleteButtonSize))
            {
                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.RemoveDerivedStep,
                    TableId = table.Id,
                    StepIndex = stepIdx,
                    StepSnapshot = step.Clone(),
                });

                _dragStepIndex = -1;
                _dragStepTargetIndex = -1;
                _dragStepTableId = null;
                Im.Context.PopId();
                return stepTop + stepHeight;
            }

            y = stepTop + RowHeight;

            // Join configuration row (key mapping + join kind)
            if (step.Kind == DerivedStepKind.Join)
            {
                y += StepInnerGap;
                float kmY = y + (RowHeight - style.FontSize) * 0.5f;
                Im.Text("On".AsSpan(), controlsX, kmY, style.FontSize - 2f, ImStyle.WithAlpha(style.TextSecondary, 170));

                // Left (derived) column options
                EnsureScratchCapacity(table.Columns.Count);
                int leftCount = 0;
                for (int c = 0; c < table.Columns.Count; c++)
                {
                    var col = table.Columns[c];

                    // Don't offer columns from the table we're joining as left-side join keys.
                    // Those values don't exist until after this join step runs, and it leads to confusing "NoMatch" behavior.
                    if (col.IsProjected && !string.IsNullOrEmpty(step.SourceTableId))
                    {
                        int projIndex = FindDerivedProjectionIndex(config, col.Id);
                        if (projIndex >= 0 &&
                            string.Equals(config.Projections[projIndex].SourceTableId, step.SourceTableId, StringComparison.Ordinal))
                        {
                            continue;
                        }
                    }

                    _scratchOptionNames[leftCount] = col.Name;
                    _scratchOptionIds[leftCount] = col.Id;
                    leftCount++;
                }

                // Right (source) column options
                int sourceColCount = 0;
                if (!string.IsNullOrEmpty(step.SourceTableId))
                {
                    DocTable? srcTable = null;
                    for (int ti = 0; ti < project.Tables.Count; ti++)
                    {
                        var t = project.Tables[ti];
                        if (string.Equals(t.Id, step.SourceTableId, StringComparison.Ordinal))
                        {
                            srcTable = t;
                            break;
                        }
                    }
                    if (srcTable != null)
                    {
                        EnsureScratchCapacity(srcTable.Columns.Count);
                        for (int c = 0; c < srcTable.Columns.Count; c++)
                        {
                            _scratchOptionNamesB[sourceColCount] = srcTable.Columns[c].Name;
                            _scratchOptionIdsB[sourceColCount] = srcTable.Columns[c].Id;
                            sourceColCount++;
                        }
                    }
                }

                var mapping = step.KeyMappings.Count > 0 ? step.KeyMappings[0] : new DerivedKeyMapping();
                int leftSelected = 0;
                int rightSelected = 0;
                for (int i = 0; i < leftCount; i++)
                {
                    if (string.Equals(_scratchOptionIds[i], mapping.BaseColumnId, StringComparison.Ordinal))
                    {
                        leftSelected = i;
                        break;
                    }
                }
                for (int i = 0; i < sourceColCount; i++)
                {
                    if (string.Equals(_scratchOptionIdsB[i], mapping.SourceColumnId, StringComparison.Ordinal))
                    {
                        rightSelected = i;
                        break;
                    }
                }

                if (stepIdx < _stepBaseColIndex.Length) _stepBaseColIndex[stepIdx] = leftSelected;
                if (stepIdx < _stepSourceColIndex.Length) _stepSourceColIndex[stepIdx] = rightSelected;

                float leftJoinX = joinModeX;
                float rightJoinX = sourceX;
                float ddY = y;
                float eqWidth = 6f;
                float joinGap = 3f;
                float leftJoinW = Math.Max(0f, rightJoinX - leftJoinX - (joinGap * 2f) - eqWidth);
                float rightJoinW = sourceW;
                float eqX = leftJoinX + leftJoinW + joinGap;

                bool changed = false;
                if (leftCount > 0 && stepIdx < _stepBaseColIndex.Length)
                {
                    changed |= Im.Dropdown("join_left", _scratchOptionNames.AsSpan(0, leftCount), ref _stepBaseColIndex[stepIdx], leftJoinX, ddY, leftJoinW, InspectorDropdownFlags);
                }

                Im.Text("=".AsSpan(), eqX, kmY, style.FontSize - 2f, ImStyle.WithAlpha(style.TextSecondary, 170));

                if (sourceColCount > 0 && stepIdx < _stepSourceColIndex.Length)
                {
                    changed |= Im.Dropdown("join_right", _scratchOptionNamesB.AsSpan(0, sourceColCount), ref _stepSourceColIndex[stepIdx], rightJoinX, ddY, rightJoinW, InspectorDropdownFlags);
                }

                if (changed)
                {
                    var newStep = step.Clone();
                    if (newStep.KeyMappings.Count <= 0)
                        newStep.KeyMappings.Add(new DerivedKeyMapping());

                    if (leftCount > 0 && stepIdx < _stepBaseColIndex.Length)
                        newStep.KeyMappings[0].BaseColumnId = _scratchOptionIds[_stepBaseColIndex[stepIdx]];

                    if (sourceColCount > 0 && stepIdx < _stepSourceColIndex.Length)
                    {
                        newStep.KeyMappings[0].SourceColumnId = _scratchOptionIdsB[_stepSourceColIndex[stepIdx]];
                    }

                    workspace.ExecuteCommand(new DocCommand
                    {
                        Kind = DocCommandKind.UpdateDerivedStep,
                        TableId = table.Id,
                        StepIndex = stepIdx,
                        OldStepSnapshot = step.Clone(),
                        StepSnapshot = newStep,
                    });
                }

                y += RowHeight;
            }

            if (stepIdx < stepCount - 1)
            {
                float stepDividerY = stepTop + stepContentHeight + StepBlockSpacing * 0.5f;
                Im.DrawLine(rowX, stepDividerY, rowX + rowW, stepDividerY, 1f, ImStyle.WithAlpha(style.Border, 90));
            }
            y = stepTop + stepHeight;
            Im.Context.PopId();
        }

        // Drag insertion indicator + commit reorder on release.
        if (_dragStepIndex >= 0 && _dragStepIndex < stepCount)
        {
            var input = Im.Context.Input;
            if (input.MouseDown)
            {
                float probeY = Im.MousePos.Y - _dragStepMouseOffsetY + RowHeight * 0.5f;
                int target = stepCount;
                for (int i = 0; i < layoutCount; i++)
                {
                    float mid = _stepBlockTops[i] + _stepBlockHeights[i] * 0.5f;
                    if (probeY < mid)
                    {
                        target = i;
                        break;
                    }
                }
                _dragStepTargetIndex = target;

                float lineY = target >= stepCount
                    ? (layoutCount > 0 ? _stepBlockTops[layoutCount - 1] + _stepBlockHeights[layoutCount - 1] - StepBlockSpacing : y)
                    : _stepBlockTops[target];
                Im.DrawLine(rowX, lineY, rowX + rowW, lineY, 2f, style.Primary);
            }
            else
            {
                int target = _dragStepTargetIndex;
                int from = _dragStepIndex;
                _dragStepIndex = -1;
                _dragStepTargetIndex = -1;
                _dragStepTableId = null;

                if (target >= 0 && target <= stepCount && target != from && target != from + 1)
                {
                    workspace.ExecuteCommand(new DocCommand
                    {
                        Kind = DocCommandKind.ReorderDerivedStep,
                        TableId = table.Id,
                        StepIndex = from,
                        TargetStepIndex = target,
                    });
                }
            }
        }

        return y;
    }

    private static void EnsureScratchCapacity(int needed)
    {
        if (_scratchOptionNames.Length < needed)
        {
            int newLen = Math.Max(needed, _scratchOptionNames.Length * 2);
            Array.Resize(ref _scratchOptionNames, newLen);
            Array.Resize(ref _scratchOptionIds, newLen);
        }

        if (_scratchOptionNamesB.Length < needed)
        {
            int newLen = Math.Max(needed, _scratchOptionNamesB.Length * 2);
            Array.Resize(ref _scratchOptionNamesB, newLen);
            Array.Resize(ref _scratchOptionIdsB, newLen);
        }
    }

    private static void EnsureSourceTableOptionCapacity(int needed)
    {
        if (_sourceTableOptionNames.Length < needed)
        {
            int newLen = Math.Max(needed, _sourceTableOptionNames.Length * 2);
            Array.Resize(ref _sourceTableOptionNames, newLen);
            Array.Resize(ref _sourceTableOptionIds, newLen);
        }
    }

    private static void EnsureVariantOptionCapacity(int needed)
    {
        if (_variantOptionIds.Length < needed)
        {
            int newLength = Math.Max(needed, _variantOptionIds.Length * 2);
            Array.Resize(ref _variantOptionIds, newLength);
        }
    }

    private static float DrawDerivedDiagnosticsSection(
        DocWorkspace workspace,
        DocTable table,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        y = DrawCollapsibleHeader("Diagnostics", ref _derivedDiagnosticsExpanded, contentRect, y, style);
        if (!_derivedDiagnosticsExpanded) return y;
        y += SectionHeaderContentSpacing;

        float rowX = contentRect.X + SectionHorizontalInset + RowHorizontalInset;

        DerivedMaterializeResult? result = null;
        workspace.DerivedResults.TryGetValue(table.Id, out result);

        float textYD = y + (RowHeight - style.FontSize) * 0.5f;
        if (!string.IsNullOrEmpty(workspace.LastComputeError))
        {
            Im.Text("Error:".AsSpan(), rowX, textYD, style.FontSize - 1f, ImStyle.WithAlpha(style.TextSecondary, 160));
            float errX = rowX + Im.MeasureTextWidth("Error:".AsSpan(), style.FontSize - 1f) + 6;
            Im.Text(workspace.LastComputeError.AsSpan(), errX, textYD, style.FontSize - 1f, style.TextPrimary);
            y += RowHeight;
            return y;
        }

        if (result != null)
        {
            Span<char> diagBuf = stackalloc char[80];
            int w = 0;
            "Rows: ".AsSpan().CopyTo(diagBuf); w += 6;
            result.Rows.Count.TryFormat(diagBuf.Slice(w), out int n1); w += n1;
            "  NoMatch: ".AsSpan().CopyTo(diagBuf.Slice(w)); w += 11;
            result.NoMatchCount.TryFormat(diagBuf.Slice(w), out int n2); w += n2;
            "  Multi: ".AsSpan().CopyTo(diagBuf.Slice(w)); w += 9;
            result.MultiMatchCount.TryFormat(diagBuf.Slice(w), out int n3); w += n3;
            "  Type: ".AsSpan().CopyTo(diagBuf.Slice(w)); w += 8;
            result.TypeMismatchCount.TryFormat(diagBuf.Slice(w), out int n4); w += n4;
            Im.Text(diagBuf.Slice(0, w), rowX, textYD, style.FontSize - 1f, style.TextSecondary);
        }
        else
        {
            Im.Text("No data".AsSpan(), rowX, textYD, style.FontSize - 1f, style.TextSecondary);
        }
        y += RowHeight;

        return y;
    }

    private static float DrawCollapsibleHeader(
        string title,
        ref bool expanded,
        ImRect contentRect,
        float y,
        ImStyle style,
        int count = -1,
        float rightOverlayWidth = 0f)
    {
        float headerX = contentRect.X + SectionHorizontalInset;
        float headerW = contentRect.Width - SectionHorizontalInset * 2f;
        float interactiveW = MathF.Max(0f, headerW - MathF.Max(0f, rightOverlayWidth));
        uint headerFill = _inspectorHeaderFillColor != 0 ? _inspectorHeaderFillColor : ImStyle.WithAlpha(style.Surface, 180);

        var toggleRect = new ImRect(headerX, y, interactiveW, SectionHeaderHeight);
        int toggleId = Im.Context.GetId(title);
        bool hovered = toggleRect.Contains(Im.MousePos);
        if (hovered)
        {
            Im.Context.SetHot(toggleId);
            if (Im.Context.Input.MousePressed)
            {
                Im.Context.SetActive(toggleId);
            }
        }

        if (Im.Context.IsActive(toggleId) && Im.Context.Input.MouseReleased)
        {
            if (hovered)
            {
                expanded = !expanded;
            }

            Im.Context.ClearActive();
        }

        if (hovered)
        {
            headerFill = _inspectorHeaderFillHoverColor != 0 ? _inspectorHeaderFillHoverColor : ImStyle.WithAlpha(style.Hover, 132);
        }

        Im.DrawRoundedRect(headerX, y, headerW, SectionHeaderHeight, SectionCornerRadius, headerFill);

        float textY = y + (SectionHeaderHeight - style.FontSize) * 0.5f;
        uint textColor = hovered ? style.TextPrimary : style.TextSecondary;
        const float chevronSize = 10f;
        float chevronX = headerX + Padding;
        float chevronY = y + (SectionHeaderHeight - chevronSize) * 0.5f;
        ImIcons.DrawChevron(
            chevronX,
            chevronY,
            chevronSize,
            expanded ? ImIcons.ChevronDirection.Down : ImIcons.ChevronDirection.Right,
            textColor);

        float textX = chevronX + chevronSize + 6f;
        Im.Text(title.AsSpan(), textX, textY, style.FontSize, textColor);

        if (count >= 0)
        {
            textX += Im.MeasureTextWidth(title.AsSpan(), style.FontSize) + 6;
            Span<char> countBuf = stackalloc char[16];
            countBuf[0] = '(';
            int w = 1;
            count.TryFormat(countBuf.Slice(w), out int n1);
            w += n1;
            countBuf[w++] = ')';
            Im.Text(countBuf.Slice(0, w), textX, textY, style.FontSize - 1f,
                ImStyle.WithAlpha(style.TextSecondary, 140));
        }

        Im.DrawLine(headerX + 1f, y + SectionHeaderHeight - 1f, headerX + headerW - 1f, y + SectionHeaderHeight - 1f, 1f, ImStyle.WithAlpha(style.Border, 96));
        return y + SectionHeaderHeight;
    }

    private static void DrawSectionCard(
        ImRect contentRect,
        float sectionTop,
        float sectionBottom,
        ImStyle style)
    {
        if (sectionBottom <= sectionTop)
        {
            return;
        }

        float cardX = contentRect.X + SectionHorizontalInset;
        float cardWidth = contentRect.Width - SectionHorizontalInset * 2f;
        float cardHeight = sectionBottom - sectionTop + SectionCardInnerBottomPadding;
        if (cardWidth <= 0f || cardHeight <= 0f)
        {
            return;
        }

        uint cardFillColor = _inspectorCardFillColor;
        var viewport = Im.Context.CurrentViewport;
        if (viewport != null)
        {
            var drawList = viewport.CurrentDrawList;
            int previousSortKey = drawList.GetSortKey();
            int cardFillSortKey = previousSortKey - 64;
            if (cardFillSortKey > previousSortKey)
            {
                cardFillSortKey = int.MinValue + 4096;
            }

            drawList.SetSortKey(cardFillSortKey);
            Im.DrawRoundedRect(
                cardX,
                sectionTop,
                cardWidth,
                cardHeight,
                SectionCornerRadius,
                cardFillColor);
            drawList.SetSortKey(previousSortKey);
        }
        else
        {
            Im.DrawRoundedRect(
                cardX,
                sectionTop,
                cardWidth,
                cardHeight,
                SectionCornerRadius,
                cardFillColor);
        }

        Im.DrawRoundedRectStroke(
            cardX,
            sectionTop,
            cardWidth,
            cardHeight,
            SectionCornerRadius,
            ImStyle.WithAlpha(style.Border, 210),
            1.5f);
    }

    // --- Derived table helpers ---

    private static string FindColumnName(DocWorkspace workspace, string? tableId, string columnId)
    {
        if (string.IsNullOrEmpty(tableId)) return columnId;
        var projectTables = workspace.Project.Tables;
        DocTable? t = null;
        for (int ti = 0; ti < projectTables.Count; ti++)
        {
            var candidate = projectTables[ti];
            if (string.Equals(candidate.Id, tableId, StringComparison.Ordinal))
            {
                t = candidate;
                break;
            }
        }
        if (t == null) return columnId;
        for (int ci = 0; ci < t.Columns.Count; ci++)
        {
            var col = t.Columns[ci];
            if (string.Equals(col.Id, columnId, StringComparison.Ordinal))
            {
                return col.Name;
            }
        }
        return columnId;
    }

    private static string FindTableShortName(DocWorkspace workspace, string tableId)
    {
        var projectTables = workspace.Project.Tables;
        for (int ti = 0; ti < projectTables.Count; ti++)
        {
            var t = projectTables[ti];
            if (string.Equals(t.Id, tableId, StringComparison.Ordinal))
            {
                return t.Name;
            }
        }
        return "?";
    }

    private static string FindColumnNameInTable(DocTable table, string columnId)
    {
        for (int ci = 0; ci < table.Columns.Count; ci++)
        {
            var col = table.Columns[ci];
            if (string.Equals(col.Id, columnId, StringComparison.Ordinal))
            {
                return col.Name;
            }
        }
        return columnId;
    }

    private static string FindFirstOtherTableId(DocProject project, string selfTableId)
    {
        for (int i = 0; i < project.Tables.Count; i++)
        {
            var t = project.Tables[i];
            if (!string.Equals(t.Id, selfTableId, StringComparison.Ordinal))
            {
                return t.Id;
            }
        }
        return "";
    }

    private static float DrawColumnsSection(
        DocWorkspace workspace,
        DocTable table,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        int totalCount = 0;
        if (table.IsDerived && table.DerivedConfig != null)
        {
            totalCount = table.DerivedConfig.Projections.Count + table.DerivedConfig.SuppressedProjections.Count;
        }
        else
        {
            totalCount = table.Columns.Count;
        }

        y = DrawCollapsibleHeader("Columns", ref _columnsExpanded, contentRect, y, style, totalCount);

        if (!_columnsExpanded)
            return y;
        y += SectionHeaderContentSpacing;

        if (table.IsDerived && table.DerivedConfig != null)
        {
            y = DrawDerivedColumns(workspace, table, table.DerivedConfig, contentRect, y, style);
            return y;
        }

        // Normal table column rows
        for (int i = 0; i < table.Columns.Count; i++)
        {
            var column = table.Columns[i];
            y = DrawColumnRow(workspace, table, column, i, contentRect, y, style);
            if (TryGetColumnUiPlugin(column, out var columnUiPlugin))
            {
                y = columnUiPlugin.DrawInspector(workspace, table, column, contentRect, y, style);
            }
        }

        return y;
    }

    private static float DrawExportSection(
        DocWorkspace workspace,
        DocTable table,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        y = DrawCollapsibleHeader("Export", ref _exportExpanded, contentRect, y, style);
        if (!_exportExpanded)
        {
            return y;
        }

        y += SectionHeaderContentSpacing;

        float rowX = contentRect.X + SectionHorizontalInset + RowHorizontalInset;

        // Enabled toggle
        bool enabled = table.ExportConfig != null && table.ExportConfig.Enabled;
        float textY = y + (RowHeight - style.FontSize) * 0.5f;
        Im.Text("Enabled:".AsSpan(), rowX, textY, style.FontSize - 1f, style.TextSecondary);

        float buttonW = 62f;
        float buttonH = style.MinButtonHeight;
        float buttonX = rowX + 74f;
        float buttonY = y + (RowHeight - buttonH) * 0.5f;
        string enabledLabel = enabled ? "On" : "Off";
        bool isExportLocked = DocSystemTableRules.IsSchemaLocked(table);
        if (!isExportLocked && Im.Button(enabledLabel, buttonX, buttonY, buttonW, buttonH))
        {
            var oldSnapshot = table.ExportConfig?.Clone();
            var newSnapshot = table.ExportConfig?.Clone() ?? new DocTableExportConfig();
            newSnapshot.Enabled = !enabled;
            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.SetTableExportConfig,
                TableId = table.Id,
                OldExportConfigSnapshot = oldSnapshot,
                NewExportConfigSnapshot = newSnapshot,
            });
        }
        else if (isExportLocked)
        {
            float lockTextY = y + (RowHeight - style.FontSize) * 0.5f;
            Im.Text(enabledLabel.AsSpan(), buttonX + 20f, lockTextY, style.FontSize - 1f, style.TextSecondary);
        }

        y += RowHeight;

        if (isExportLocked)
        {
            string lockReason = "System table export is locked.";
            float lockReasonY = y + (RowHeight - style.FontSize) * 0.5f;
            Im.Text(lockReason.AsSpan(), rowX, lockReasonY, style.FontSize - 1f, style.TextSecondary);
            y += RowHeight;
        }

        if (table.ExportConfig == null)
        {
            return y;
        }

        // Namespace (fixed)
        textY = y + (RowHeight - style.FontSize) * 0.5f;
        Im.Text("Namespace:".AsSpan(), rowX, textY, style.FontSize - 1f, style.TextSecondary);
        Im.Text("DerpDocDatabase".AsSpan(), rowX + 92f, textY, style.FontSize, style.TextPrimary);
        y += RowHeight;

        // Struct (derived from table name)
        textY = y + (RowHeight - style.FontSize) * 0.5f;
        Im.Text("Struct:".AsSpan(), rowX, textY, style.FontSize - 1f, style.TextSecondary);
        Im.Text(table.Name.AsSpan(), rowX + 92f, textY, style.FontSize, style.TextPrimary);
        y += RowHeight;

        return y;
    }

    private static float DrawKeysSection(
        DocWorkspace workspace,
        DocTable table,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        y = DrawCollapsibleHeader("Keys", ref _keysExpanded, contentRect, y, style);
        if (!_keysExpanded)
        {
            return y;
        }

        y += SectionHeaderContentSpacing;

        float rowX = contentRect.X + SectionHorizontalInset + RowHorizontalInset;
        float rowW = contentRect.Width - (SectionHorizontalInset + RowHorizontalInset) * 2f;

        // Primary key
        int needed = table.Columns.Count + 1;
        EnsureScratchCapacity(needed);
        _scratchOptionNames[0] = "(none)";
        _scratchOptionIds[0] = "";
        int count = 1;
        int selectedIdx = 0;
        for (int i = 0; i < table.Columns.Count; i++)
        {
            var col = table.Columns[i];
            _scratchOptionNames[count] = col.Name;
            _scratchOptionIds[count] = col.Id;
            if (string.Equals(table.Keys.PrimaryKeyColumnId, col.Id, StringComparison.Ordinal))
            {
                selectedIdx = count;
            }
            count++;
        }
        _primaryKeyDropdownIndex = selectedIdx;

        float textY = y + (RowHeight - style.FontSize) * 0.5f;
        Im.Text("Primary:".AsSpan(), rowX, textY, style.FontSize - 1f, style.TextSecondary);

        Im.Context.PushId("pk_dropdown");
        if (Im.Dropdown("pk_dd", _scratchOptionNames.AsSpan(0, count), ref _primaryKeyDropdownIndex, rowX + 74f, y, rowW - 74f, InspectorDropdownFlags))
        {
            var oldKeys = table.Keys.Clone();
            var newKeys = table.Keys.Clone();
            newKeys.PrimaryKeyColumnId = _primaryKeyDropdownIndex > 0 ? _scratchOptionIds[_primaryKeyDropdownIndex] : "";
            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.SetTableKeys,
                TableId = table.Id,
                OldKeysSnapshot = oldKeys,
                NewKeysSnapshot = newKeys,
            });
        }
        Im.Context.PopId();

        y += RowHeight + DropdownBottomSpacing;

        // Secondary keys
        for (int keyIndex = 0; keyIndex < table.Keys.SecondaryKeys.Count; keyIndex++)
        {
            var sk = table.Keys.SecondaryKeys[keyIndex];
            int skSelectedIdx = 0;
            for (int i = 1; i < count; i++)
            {
                if (string.Equals(_scratchOptionIds[i], sk.ColumnId, StringComparison.Ordinal))
                {
                    skSelectedIdx = i;
                    break;
                }
            }

            textY = y + (RowHeight - style.FontSize) * 0.5f;
            Im.Text("Secondary:".AsSpan(), rowX, textY, style.FontSize - 1f, style.TextSecondary);

            float ddX = rowX + 92f;
            float ddW = MathF.Max(1f, rowW - 92f - 94f);
            Im.Context.PushId(keyIndex);
            if (Im.Dropdown("sk_dd", _scratchOptionNames.AsSpan(0, count), ref skSelectedIdx, ddX, y, ddW, InspectorDropdownFlags))
            {
                var oldKeys = table.Keys.Clone();
                var newKeys = table.Keys.Clone();
                if (keyIndex >= 0 && keyIndex < newKeys.SecondaryKeys.Count)
                {
                    newKeys.SecondaryKeys[keyIndex].ColumnId = skSelectedIdx > 0 ? _scratchOptionIds[skSelectedIdx] : "";
                }
                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.SetTableKeys,
                    TableId = table.Id,
                    OldKeysSnapshot = oldKeys,
                    NewKeysSnapshot = newKeys,
                });
            }

            int uniqueIndex = sk.Unique ? 0 : 1;
            float uniqueX = ddX + ddW + 6f;
            float uniqueW = 70f;
            if (Im.Dropdown("sk_unique", SecondaryKeyUniquenessOptions, ref uniqueIndex, uniqueX, y, uniqueW, InspectorDropdownFlags))
            {
                var oldKeys = table.Keys.Clone();
                var newKeys = table.Keys.Clone();
                if (keyIndex >= 0 && keyIndex < newKeys.SecondaryKeys.Count)
                {
                    newKeys.SecondaryKeys[keyIndex].Unique = uniqueIndex == 0;
                }
                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.SetTableKeys,
                    TableId = table.Id,
                    OldKeysSnapshot = oldKeys,
                    NewKeysSnapshot = newKeys,
                });
            }

            float removeX = uniqueX + uniqueW + 6f;
            float removeY = y + (RowHeight - DeleteButtonSize) * 0.5f;
            if (Im.Button(MinusIcon, removeX, removeY, DeleteButtonSize, DeleteButtonSize))
            {
                var oldKeys = table.Keys.Clone();
                var newKeys = table.Keys.Clone();
                if (keyIndex >= 0 && keyIndex < newKeys.SecondaryKeys.Count)
                {
                    newKeys.SecondaryKeys.RemoveAt(keyIndex);
                }
                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.SetTableKeys,
                    TableId = table.Id,
                    OldKeysSnapshot = oldKeys,
                    NewKeysSnapshot = newKeys,
                });
            }

            Im.Context.PopId();
            y += RowHeight;
        }

        float addY = y + (RowHeight - DeleteButtonSize) * 0.5f;
        if (Im.Button(PlusIcon, rowX, addY, DeleteButtonSize, DeleteButtonSize))
        {
            string newColumnId = FindFirstNonKeyColumnId(table);
            if (!string.IsNullOrEmpty(newColumnId))
            {
                var oldKeys = table.Keys.Clone();
                var newKeys = table.Keys.Clone();
                newKeys.SecondaryKeys.Add(new DocSecondaryKey { ColumnId = newColumnId, Unique = false });
                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.SetTableKeys,
                    TableId = table.Id,
                    OldKeysSnapshot = oldKeys,
                    NewKeysSnapshot = newKeys,
                });
            }
        }
        float addTextY = y + (RowHeight - style.FontSize) * 0.5f;
        Im.Text("Add secondary key".AsSpan(), rowX + DeleteButtonSize + 6f, addTextY, style.FontSize - 1f, style.TextSecondary);
        y += RowHeight;

        return y;
    }

    private static string FindFirstNonKeyColumnId(DocTable table)
    {
        for (int i = 0; i < table.Columns.Count; i++)
        {
            var col = table.Columns[i];
            if (string.IsNullOrEmpty(col.Id))
            {
                continue;
            }

            if (string.Equals(table.Keys.PrimaryKeyColumnId, col.Id, StringComparison.Ordinal))
            {
                continue;
            }

            bool alreadySecondary = false;
            for (int k = 0; k < table.Keys.SecondaryKeys.Count; k++)
            {
                if (string.Equals(table.Keys.SecondaryKeys[k].ColumnId, col.Id, StringComparison.Ordinal))
                {
                    alreadySecondary = true;
                    break;
                }
            }

            if (!alreadySecondary)
            {
                return col.Id;
            }
        }

        return "";
    }

    private static float DrawDerivedColumns(
        DocWorkspace workspace,
        DocTable table,
        DocDerivedConfig config,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        float rowX = contentRect.X + SectionHorizontalInset + RowHorizontalInset;
        float rowW = contentRect.Width - (SectionHorizontalInset + RowHorizontalInset) * 2f;
        int projectionCount = config.Projections.Count;
        if (_dragProjectionIndex >= projectionCount)
        {
            _dragProjectionIndex = -1;
        }
        if (_dragProjectionIndex >= 0 && !string.Equals(_dragProjectionTableId, table.Id, StringComparison.Ordinal))
        {
            _dragProjectionIndex = -1;
        }

        int projectionLayoutCount = Math.Min(projectionCount, _projectionRowTops.Length);

        // Included projections (in output order)
        for (int i = 0; i < projectionCount; i++)
        {
            if (i < projectionLayoutCount)
            {
                _projectionRowTops[i] = y;
            }
            y = DrawDerivedProjectionRow(workspace, table, config, i, contentRect, y, style);
        }

        // Drag insertion indicator + commit reorder on release.
        if (_dragProjectionIndex >= 0 && _dragProjectionIndex < projectionCount)
        {
            var input = Im.Context.Input;
            if (input.MouseDown)
            {
                float probeY = Im.MousePos.Y - _dragProjectionMouseOffsetY + RowHeight * 0.5f;
                int target = projectionCount;
                for (int i = 0; i < projectionLayoutCount; i++)
                {
                    float mid = _projectionRowTops[i] + RowHeight * 0.5f;
                    if (probeY < mid)
                    {
                        target = i;
                        break;
                    }
                }
                _dragProjectionTargetIndex = target;

                float lineY = target >= projectionCount
                    ? (projectionLayoutCount > 0 ? _projectionRowTops[projectionLayoutCount - 1] + RowHeight : y)
                    : _projectionRowTops[target];
                Im.DrawLine(rowX, lineY, rowX + rowW, lineY, 2f, style.Primary);
            }
            else
            {
                int from = _dragProjectionIndex;
                _dragProjectionIndex = -1;
                _dragProjectionTableId = null;

                int to = Math.Clamp(_dragProjectionTargetIndex, 0, projectionCount);
                if (from >= 0 && from < projectionCount && to >= 0 && to <= projectionCount &&
                    to != from && to != from + 1)
                {
                    workspace.ExecuteCommand(new DocCommand
                    {
                        Kind = DocCommandKind.ReorderDerivedProjection,
                        TableId = table.Id,
                        ProjectionIndex = from,
                        TargetProjectionIndex = to,
                    });
                }
            }
        }

        // Suppressed projections (not included)
        for (int i = 0; i < config.SuppressedProjections.Count; i++)
        {
            y = DrawDerivedSuppressedRow(workspace, table, config, i, contentRect, y, style);
        }

        // Local (non-projected) columns
        for (int i = 0; i < table.Columns.Count; i++)
        {
            var col = table.Columns[i];
            if (!col.IsProjected)
            {
                y = DrawColumnRow(workspace, table, col, i, contentRect, y, style);
                if (TryGetColumnUiPlugin(col, out var columnUiPlugin))
                {
                    y = columnUiPlugin.DrawInspector(workspace, table, col, contentRect, y, style);
                }
            }
        }

        return y;
    }

    private static DocTable? FindTableById(DocWorkspace workspace, string tableId)
    {
        for (int i = 0; i < workspace.Project.Tables.Count; i++)
        {
            var t = workspace.Project.Tables[i];
            if (string.Equals(t.Id, tableId, StringComparison.Ordinal))
            {
                return t;
            }
        }
        return null;
    }

    private static bool WouldCreateSchemaLinkCycle(DocProject project, string tableId, string sourceTableId)
    {
        string currentTableId = sourceTableId;
        for (int depth = 0; depth <= project.Tables.Count; depth++)
        {
            if (string.IsNullOrWhiteSpace(currentTableId))
            {
                return false;
            }

            if (string.Equals(currentTableId, tableId, StringComparison.Ordinal))
            {
                return true;
            }

            DocTable? currentTable = null;
            for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
            {
                DocTable candidateTable = project.Tables[tableIndex];
                if (!string.Equals(candidateTable.Id, currentTableId, StringComparison.Ordinal))
                {
                    continue;
                }

                currentTable = candidateTable;
                break;
            }

            if (currentTable == null)
            {
                return false;
            }

            currentTableId = currentTable.SchemaSourceTableId ?? "";
        }

        return true;
    }

    private static bool WouldCreateInheritanceCycle(DocProject project, string tableId, string sourceTableId)
    {
        string currentTableId = sourceTableId;
        for (int depth = 0; depth <= project.Tables.Count; depth++)
        {
            if (string.IsNullOrWhiteSpace(currentTableId))
            {
                return false;
            }

            if (string.Equals(currentTableId, tableId, StringComparison.Ordinal))
            {
                return true;
            }

            DocTable? currentTable = null;
            for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
            {
                DocTable candidateTable = project.Tables[tableIndex];
                if (!string.Equals(candidateTable.Id, currentTableId, StringComparison.Ordinal))
                {
                    continue;
                }

                currentTable = candidateTable;
                break;
            }

            if (currentTable == null)
            {
                return false;
            }

            currentTableId = currentTable.InheritanceSourceTableId ?? "";
        }

        return true;
    }

    private static DocColumn? FindSourceColumn(DocTable sourceTable, string sourceColumnId)
    {
        for (int i = 0; i < sourceTable.Columns.Count; i++)
        {
            var c = sourceTable.Columns[i];
            if (string.Equals(c.Id, sourceColumnId, StringComparison.Ordinal))
            {
                return c;
            }
        }
        return null;
    }

    private static DocColumn? FindDerivedOutputColumn(DocTable derivedTable, string outputColumnId)
    {
        if (string.IsNullOrEmpty(outputColumnId))
        {
            return null;
        }

        for (int i = 0; i < derivedTable.Columns.Count; i++)
        {
            var c = derivedTable.Columns[i];
            if (string.Equals(c.Id, outputColumnId, StringComparison.Ordinal))
            {
                return c;
            }
        }

        return null;
    }

    private static float DrawDerivedProjectionRow(
        DocWorkspace workspace,
        DocTable derivedTable,
        DocDerivedConfig config,
        int projectionIndex,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        float rowX = contentRect.X + SectionHorizontalInset + RowHorizontalInset;
        float rowW = contentRect.Width - (SectionHorizontalInset + RowHorizontalInset) * 2f;
        float itemY = y;
        var rowRect = new ImRect(rowX, itemY, rowW, RowHeight);
        bool rowHovered = rowRect.Contains(Im.MousePos);
        float gutterX = rowX - (GutterLaneWidth - ControlSpacing);
        var gutterRect = new ImRect(gutterX, itemY, GutterLaneWidth, RowHeight);
        bool gutterHovered = gutterRect.Contains(Im.MousePos);
        bool rowDragged = _dragProjectionIndex == projectionIndex &&
                          string.Equals(_dragProjectionTableId, derivedTable.Id, StringComparison.Ordinal) &&
                          Im.Context.Input.MouseDown;

        var proj = config.Projections[projectionIndex];
        var sourceTable = FindTableById(workspace, proj.SourceTableId);
        var sourceColumn = sourceTable != null ? FindSourceColumn(sourceTable, proj.SourceColumnId) : null;
        var outCol = FindDerivedOutputColumn(derivedTable, proj.OutputColumnId);

        DocColumnKind kind = outCol != null
            ? outCol.Kind
            : sourceColumn != null ? sourceColumn.Kind : DocColumnKind.Text;
        string name = outCol != null
            ? outCol.Name
            : sourceColumn != null ? sourceColumn.Name : "(missing)";
        string typeIcon = GetColumnKindIcon(kind);

        if (gutterHovered || rowDragged)
        {
            Im.DrawRect(rowX, itemY, rowW, RowHeight,
                ImStyle.WithAlpha(style.Primary, 18));
        }

        if (gutterHovered || rowDragged)
        {
            Im.DrawRect(gutterX, itemY, GutterLaneWidth, RowHeight, style.Surface);
        }

        // Drag handle
        float dragBtnY = itemY + (RowHeight - DeleteButtonSize) * 0.5f;
        if (gutterHovered && Im.Context.Input.MousePressed)
        {
            _dragProjectionIndex = projectionIndex;
            _dragProjectionTargetIndex = projectionIndex;
            _dragProjectionMouseOffsetY = Im.MousePos.Y - itemY;
            _dragProjectionTableId = derivedTable.Id;
        }
        if (gutterHovered || rowDragged)
        {
            uint handleDotColor = rowDragged ? style.TextPrimary : style.TextSecondary;
            DrawGutterHandleDots(gutterX, itemY, RowHeight, handleDotColor);
        }

        // Visibility toggle (eye icon, toggle-styled)
        float toggleY = itemY + (RowHeight - 22f) * 0.5f;
        float controlsX = rowX + ControlSpacing;
        if (outCol != null)
        {
            Im.Context.PushId(outCol.Id);
            bool visible = !outCol.IsHidden;
            if (DrawVisibilityEyeToggle("projection_visibility_toggle", controlsX, toggleY, VisibilityToggleWidth, 22f, ref visible, style))
            {
                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.SetColumnHidden,
                    TableId = derivedTable.Id,
                    ColumnId = outCol.Id,
                    OldHidden = outCol.IsHidden,
                    NewHidden = !visible,
                });
            }
            Im.Context.PopId();
        }

        // Type icon + name
        float typeX = controlsX + VisibilityToggleWidth + ControlSpacing;
        float textY = itemY + (RowHeight - style.FontSize) * 0.5f;
        Im.Text(typeIcon.AsSpan(), typeX, textY + 0.5f, style.FontSize - 1f, style.TextSecondary);

        float nameX = typeX + style.FontSize + 4;
        uint nameColor = outCol != null && outCol.IsHidden ? style.TextSecondary : style.TextPrimary;
        float lockX = rowX + rowW - 14f;
        float rightControlsLeftX = lockX - DeleteButtonSize - 6f;
        float nameWidth = Math.Max(36f, rightControlsLeftX - 8f - nameX);
        var nameRect = new ImRect(nameX, itemY, nameWidth, RowHeight);
        bool renameActiveForRow = outCol != null &&
                                  IsInlineRenameDerivedColumnActiveFor(derivedTable.Id, outCol.Id);
        if (renameActiveForRow)
        {
            bool wasRenameFocused = IsInlineDerivedColumnRenameFocused();
            float inputX = nameX - style.Padding;
            float inputY = textY - style.Padding;
            float inputWidth = nameWidth + style.Padding * 2f;
            float inputHeight = style.MinButtonHeight;
            _inlineRenameDerivedInputRect = new ImRect(inputX, inputY, inputWidth, inputHeight);
            _inlineRenameDerivedInputRectValid = true;
            Im.TextInput(
                GetInlineDerivedColumnRenameInputId(),
                _renameDerivedColumnBuffer,
                ref _renameDerivedColumnBufferLength,
                _renameDerivedColumnBuffer.Length,
                inputX,
                inputY,
                inputWidth,
                Im.ImTextInputFlags.NoBorder);

            if (_inlineRenameDerivedNeedsFocus)
            {
                int widgetId = Im.Context.GetId(GetInlineDerivedColumnRenameInputId());
                Im.Context.RequestFocus(widgetId);
                if (Im.TryGetTextInputState(GetInlineDerivedColumnRenameInputId(), out _))
                {
                    if (_inlineRenameDerivedSelectAll)
                    {
                        Im.SetTextInputSelection(GetInlineDerivedColumnRenameInputId(), _renameDerivedColumnBufferLength, 0, _renameDerivedColumnBufferLength);
                    }
                    else
                    {
                        Im.SetTextInputSelection(GetInlineDerivedColumnRenameInputId(), _renameDerivedColumnBufferLength);
                    }
                }

                _inlineRenameDerivedNeedsFocus = false;
                _inlineRenameDerivedSelectAll = false;
            }

            bool renameFocused = IsInlineDerivedColumnRenameFocused();
            if (renameFocused || wasRenameFocused)
            {
                var input = Im.Context.Input;
                if (input.KeyEscape)
                {
                    CancelInlineDerivedColumnRename();
                }
                else if (input.KeyEnter || input.KeyTab)
                {
                    CommitInlineDerivedColumnRename(workspace, derivedTable);
                }
            }
        }
        else
        {
            Im.Text(name.AsSpan(), nameX, textY, style.FontSize, nameColor);
            if (outCol != null &&
                rowHovered &&
                nameRect.Contains(Im.MousePos) &&
                Im.Context.Input.MousePressed)
            {
                BeginInlineDerivedColumnRename(derivedTable, outCol.Id, name, selectAll: true);
            }
        }

        // Source table badge on hover
        if (rowHovered && sourceTable != null)
        {
            string srcName = sourceTable.Name;
            Span<char> badgeBuf = stackalloc char[64];
            int w = 0;
            badgeBuf[w++] = '[';
            int copyLen = Math.Min(srcName.Length, 58);
            srcName.AsSpan(0, copyLen).CopyTo(badgeBuf.Slice(w));
            w += copyLen;
            badgeBuf[w++] = ']';
            float badgeX = nameX + Im.MeasureTextWidth(name.AsSpan(), style.FontSize) + 8;
            Im.Text(badgeBuf.Slice(0, w), badgeX, textY + 1, style.FontSize - 2f,
                ImStyle.WithAlpha(style.TextSecondary, 170));
        }

        // Right-side controls: [minus] [lock]
        float toggleX = lockX - DeleteButtonSize - 6f;

        if (rowHovered)
        {
            Im.Context.PushId(proj.OutputColumnId);
            if (Im.Button(MinusIcon, toggleX, dragBtnY, DeleteButtonSize, DeleteButtonSize))
            {
                DocColumn? snapshotCol = outCol;
                if (snapshotCol == null)
                {
                    snapshotCol = new DocColumn
                    {
                        Id = proj.OutputColumnId,
                        Name = name,
                        Kind = kind,
                        ColumnTypeId = Derp.Doc.Plugins.DocColumnTypeIdMapper.FromKind(kind),
                        IsProjected = true,
                    };
                }

                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.RemoveDerivedProjection,
                    TableId = derivedTable.Id,
                    ProjectionIndex = projectionIndex,
                    ProjectionSnapshot = proj.Clone(),
                    ColumnSnapshot = snapshotCol,
                });
                Im.Context.PopId();
                return itemY + RowHeight;
            }
            Im.Context.PopId();
        }

        Im.Text(LockIcon.AsSpan(), lockX, textY + 1, style.FontSize - 2f,
            ImStyle.WithAlpha(style.TextSecondary, 140));

        // Separator
        Im.DrawLine(rowX, itemY + RowHeight, rowX + rowW, itemY + RowHeight, 1f, ImStyle.WithAlpha(style.Border, 90));

        return itemY + RowHeight;
    }

    private static void DrawGutterHandleDots(float rowX, float rowY, float rowHeight, uint color)
    {
        float patternWidth = GutterHandleDotSize * 2f + GutterHandleDotSpacing;
        float patternHeight = GutterHandleDotSize * 3f + GutterHandleDotSpacing * 2f;
        float plateWidth = MathF.Min(GutterLaneWidth - 2f, patternWidth + 4f);
        float plateHeight = patternHeight + 4f;
        float plateX = rowX + (GutterLaneWidth - plateWidth) * 0.5f;
        float plateY = rowY + (rowHeight - plateHeight) * 0.5f;
        Im.DrawRoundedRect(plateX, plateY, plateWidth, plateHeight, 3f, Im.Style.Surface);

        float laneCenterX = rowX + GutterLaneWidth * 0.5f;
        float startX = laneCenterX - patternWidth * 0.5f;
        float startY = rowY + (rowHeight - patternHeight) * 0.5f;

        for (int row = 0; row < 3; row++)
        {
            float dotY = startY + row * (GutterHandleDotSize + GutterHandleDotSpacing);
            Im.DrawRoundedRect(startX, dotY, GutterHandleDotSize, GutterHandleDotSize, 1f, color);
            Im.DrawRoundedRect(startX + GutterHandleDotSize + GutterHandleDotSpacing, dotY, GutterHandleDotSize, GutterHandleDotSize, 1f, color);
        }
    }

    private static float DrawDerivedSuppressedRow(
        DocWorkspace workspace,
        DocTable derivedTable,
        DocDerivedConfig config,
        int suppressionIndex,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        float rowX = contentRect.X + SectionHorizontalInset + RowHorizontalInset;
        float rowW = contentRect.Width - (SectionHorizontalInset + RowHorizontalInset) * 2f;
        float itemY = y;
        var rowRect = new ImRect(rowX, itemY, rowW, RowHeight);
        bool rowHovered = rowRect.Contains(Im.MousePos);

        var sup = config.SuppressedProjections[suppressionIndex];
        var sourceTable = FindTableById(workspace, sup.SourceTableId);
        var sourceColumn = sourceTable != null ? FindSourceColumn(sourceTable, sup.SourceColumnId) : null;

        DocColumnKind kind = sourceColumn != null ? sourceColumn.Kind : DocColumnKind.Text;
        string name = sourceColumn != null ? sourceColumn.Name : "(missing)";
        string typeIcon = GetColumnKindIcon(kind);

        float eyeBtnY = itemY + (RowHeight - 22) * 0.5f;

        if (rowHovered)
        {
            Im.DrawRect(rowX, itemY, rowW, RowHeight, ImStyle.WithAlpha(style.Hover, 88));
        }

        float typeX = rowX + ControlSpacing;
        float textY = itemY + (RowHeight - style.FontSize) * 0.5f;
        Im.Text(typeIcon.AsSpan(), typeX, textY + 0.5f, style.FontSize - 1f, ImStyle.WithAlpha(style.TextSecondary, 140));

        float nameX = typeX + style.FontSize + 4;
        Im.Text(name.AsSpan(), nameX, textY, style.FontSize, ImStyle.WithAlpha(style.TextSecondary, 180));

        // Source table badge on hover
        if (rowHovered && sourceTable != null)
        {
            string srcName = sourceTable.Name;
            Span<char> badgeBuf = stackalloc char[64];
            int w = 0;
            badgeBuf[w++] = '[';
            int copyLen = Math.Min(srcName.Length, 58);
            srcName.AsSpan(0, copyLen).CopyTo(badgeBuf.Slice(w));
            w += copyLen;
            badgeBuf[w++] = ']';
            float badgeX = nameX + Im.MeasureTextWidth(name.AsSpan(), style.FontSize) + 8;
            Im.Text(badgeBuf.Slice(0, w), badgeX, textY + 1, style.FontSize - 2f,
                ImStyle.WithAlpha(style.TextSecondary, 170));
        }

        // Right-side controls: [plus] [lock]
        float lockX = rowX + rowW - 14f;
        float toggleX = lockX - IconColumnWidth - 6;

        Im.Context.PushId(sup.SourceColumnId);
        if (rowHovered && Im.Button(PlusIcon, toggleX, eyeBtnY, IconColumnWidth, 22))
        {
            if (sourceTable != null && sourceColumn != null)
            {
                string outputColumnId = string.IsNullOrEmpty(sup.OutputColumnId) ? Guid.NewGuid().ToString() : sup.OutputColumnId;
                var proj = new DerivedProjection
                {
                    SourceTableId = sup.SourceTableId,
                    SourceColumnId = sup.SourceColumnId,
                    OutputColumnId = outputColumnId,
                    RenameAlias = "",
                };

                var projectedCol = new DocColumn
                {
                    Id = outputColumnId,
                    Name = sourceColumn.Name,
                    Kind = sourceColumn.Kind,
                    ColumnTypeId = sourceColumn.ColumnTypeId,
                    PluginSettingsJson = sourceColumn.PluginSettingsJson,
                    Width = sourceColumn.Width,
                    IsProjected = true,
                    Options = sourceColumn.Options != null ? new List<string>(sourceColumn.Options) : null,
                    RelationTableId = sourceColumn.RelationTableId,
                    TableRefBaseTableId = sourceColumn.TableRefBaseTableId,
                    RowRefTableRefColumnId = sourceColumn.RowRefTableRefColumnId,
                    RelationTargetMode = sourceColumn.RelationTargetMode,
                    RelationTableVariantId = sourceColumn.RelationTableVariantId,
                    RelationDisplayColumnId = sourceColumn.RelationDisplayColumnId,
                    SubtableId = sourceColumn.SubtableId,
                    SubtableDisplayRendererId = sourceColumn.SubtableDisplayRendererId,
                    SubtableDisplayCellWidth = sourceColumn.SubtableDisplayCellWidth,
                    SubtableDisplayCellHeight = sourceColumn.SubtableDisplayCellHeight,
                    SubtableDisplayPreviewQuality = sourceColumn.SubtableDisplayPreviewQuality,
                    FormulaEvalScopes = sourceColumn.FormulaEvalScopes,
                    ModelPreviewSettings = sourceColumn.ModelPreviewSettings?.Clone(),
                };

                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.AddDerivedProjection,
                    TableId = derivedTable.Id,
                    ProjectionIndex = config.Projections.Count,
                    ProjectionSnapshot = proj,
                    ColumnSnapshot = projectedCol,
                });
            }
            Im.Context.PopId();
            return itemY + RowHeight;
        }
        Im.Context.PopId();

        Im.Text(LockIcon.AsSpan(), lockX, textY + 1, style.FontSize - 2f,
            ImStyle.WithAlpha(style.TextSecondary, 140));

        // Separator
        Im.DrawLine(rowX, itemY + RowHeight, rowX + rowW, itemY + RowHeight, 1f, ImStyle.WithAlpha(style.Border, 90));

        return itemY + RowHeight;
    }

    private static float DrawColumnRow(
        DocWorkspace workspace,
        DocTable table,
        DocColumn column,
        int columnIndex,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        float rowX = contentRect.X + SectionHorizontalInset + RowHorizontalInset;
        float rowW = contentRect.Width - (SectionHorizontalInset + RowHorizontalInset) * 2f;
        float itemY = y;
        var rowRect = new ImRect(rowX, itemY, rowW, RowHeight);
        bool rowHovered = rowRect.Contains(Im.MousePos);

        // Visibility toggle (eye icon, toggle-styled)
        Im.Context.PushId(column.Id);
        bool visible = !column.IsHidden;
        float toggleX = rowX + ControlSpacing;
        float toggleY = itemY + (RowHeight - 22f) * 0.5f;
        if (DrawVisibilityEyeToggle("column_visibility_toggle", toggleX, toggleY, VisibilityToggleWidth, 22f, ref visible, style))
        {
            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.SetColumnHidden,
                TableId = table.Id,
                ColumnId = column.Id,
                OldHidden = column.IsHidden,
                NewHidden = !visible,
            });
        }
        Im.Context.PopId();

        float rowIconY = itemY + (RowHeight - DeleteButtonSize) * 0.5f;
        float rightLockX = rowX + rowW - 14f;
        float removeBtnX = rightLockX - DeleteButtonSize - 6f;
        if (table.IsDerived && column.IsProjected)
        {
            int projIndex = FindDerivedProjectionIndex(table.DerivedConfig!, column.Id);
            if (projIndex >= 0)
            {
                if (rowHovered && Im.Button(MinusIcon, removeBtnX, rowIconY, DeleteButtonSize, DeleteButtonSize))
                {
                    var proj = table.DerivedConfig!.Projections[projIndex];
                    workspace.ExecuteCommand(new DocCommand
                    {
                        Kind = DocCommandKind.RemoveDerivedProjection,
                        TableId = table.Id,
                        ProjectionIndex = projIndex,
                        ProjectionSnapshot = proj.Clone(),
                        ColumnSnapshot = column,
                    });
                    return itemY + RowHeight;
                }
            }
        }

        // Type icon
        if (rowHovered)
        {
            Im.DrawRect(rowX, itemY, rowW, RowHeight, ImStyle.WithAlpha(style.Hover, 88));
        }

        float typeX = toggleX + VisibilityToggleWidth + ControlSpacing;
        float textY = itemY + (RowHeight - style.FontSize) * 0.5f;
        string typeIcon = GetColumnKindIcon(column);
        Im.Text(typeIcon.AsSpan(), typeX, textY + 0.5f, style.FontSize - 1f, style.TextSecondary);

        // Column name
        float nameX = typeX + style.FontSize + 4;
        uint nameColor = column.IsHidden ? style.TextSecondary : style.TextPrimary;
        float nameWidth = Math.Max(36f, removeBtnX - 8f - nameX);
        var nameRect = new ImRect(nameX, itemY, nameWidth, RowHeight);
        bool canInlineRename = true;
        bool renameActiveForRow = canInlineRename &&
                                  IsInlineRenameDerivedColumnActiveFor(table.Id, column.Id);
        if (renameActiveForRow)
        {
            bool wasRenameFocused = IsInlineDerivedColumnRenameFocused();
            float inputX = nameX - style.Padding;
            float inputY = textY - style.Padding;
            float inputWidth = nameWidth + style.Padding * 2f;
            float inputHeight = style.MinButtonHeight;
            _inlineRenameDerivedInputRect = new ImRect(inputX, inputY, inputWidth, inputHeight);
            _inlineRenameDerivedInputRectValid = true;
            Im.TextInput(
                GetInlineDerivedColumnRenameInputId(),
                _renameDerivedColumnBuffer,
                ref _renameDerivedColumnBufferLength,
                _renameDerivedColumnBuffer.Length,
                inputX,
                inputY,
                inputWidth,
                Im.ImTextInputFlags.NoBorder);

            if (_inlineRenameDerivedNeedsFocus)
            {
                int widgetId = Im.Context.GetId(GetInlineDerivedColumnRenameInputId());
                Im.Context.RequestFocus(widgetId);
                if (Im.TryGetTextInputState(GetInlineDerivedColumnRenameInputId(), out _))
                {
                    if (_inlineRenameDerivedSelectAll)
                    {
                        Im.SetTextInputSelection(GetInlineDerivedColumnRenameInputId(), _renameDerivedColumnBufferLength, 0, _renameDerivedColumnBufferLength);
                    }
                    else
                    {
                        Im.SetTextInputSelection(GetInlineDerivedColumnRenameInputId(), _renameDerivedColumnBufferLength);
                    }
                }

                _inlineRenameDerivedNeedsFocus = false;
                _inlineRenameDerivedSelectAll = false;
            }

            bool renameFocused = IsInlineDerivedColumnRenameFocused();
            if (renameFocused || wasRenameFocused)
            {
                var input = Im.Context.Input;
                if (input.KeyEscape)
                {
                    CancelInlineDerivedColumnRename();
                }
                else if (input.KeyEnter || input.KeyTab)
                {
                    CommitInlineDerivedColumnRename(workspace, table);
                }
            }
        }
        else
        {
            Im.Text(column.Name.AsSpan(), nameX, textY, style.FontSize, nameColor);
            if (canInlineRename &&
                rowHovered &&
                nameRect.Contains(Im.MousePos) &&
                Im.Context.Input.MousePressed)
            {
                BeginInlineDerivedColumnRename(table, column.Id, column.Name, selectAll: true);
            }
        }

        // Projected columns: show source table on hover, and a lock icon on the right.
        if (table.IsDerived && column.IsProjected)
        {
            int projIndex = FindDerivedProjectionIndex(table.DerivedConfig!, column.Id);
            if (projIndex >= 0)
            {
                var proj = table.DerivedConfig!.Projections[projIndex];
                if (rowHovered)
                {
                    string srcName = FindTableShortName(workspace, proj.SourceTableId);
                    Span<char> badgeBuf = stackalloc char[64];
                    int w = 0;
                    badgeBuf[w++] = '[';
                    int copyLen = Math.Min(srcName.Length, 58);
                    srcName.AsSpan(0, copyLen).CopyTo(badgeBuf.Slice(w));
                    w += copyLen;
                    badgeBuf[w++] = ']';
                    float badgeX = nameX + Im.MeasureTextWidth(column.Name.AsSpan(), style.FontSize) + 8;
                    Im.Text(badgeBuf.Slice(0, w), badgeX, textY + 1, style.FontSize - 2f,
                        ImStyle.WithAlpha(style.TextSecondary, 170));
                }

                Im.Text(LockIcon.AsSpan(), rightLockX, textY + 1, style.FontSize - 2f,
                    ImStyle.WithAlpha(style.TextSecondary, 140));
            }
        }

        // Key badges (Phase 5)
        bool isPrimaryKey = !string.IsNullOrEmpty(table.Keys.PrimaryKeyColumnId) &&
                            string.Equals(table.Keys.PrimaryKeyColumnId, column.Id, StringComparison.Ordinal);
        bool isSecondaryKey = false;
        bool secondaryUnique = false;
        for (int i = 0; i < table.Keys.SecondaryKeys.Count; i++)
        {
            var sk = table.Keys.SecondaryKeys[i];
            if (string.Equals(sk.ColumnId, column.Id, StringComparison.Ordinal))
            {
                isSecondaryKey = true;
                secondaryUnique = sk.Unique;
                break;
            }
        }

        if (isPrimaryKey || isSecondaryKey)
        {
            float badgeFontSize = style.FontSize - 2f;
            float badgeX = rowX + rowW - 32f;
            if (table.IsDerived && column.IsProjected)
            {
                badgeX = removeBtnX - 18f;
            }
            uint badgeColor = isPrimaryKey
                ? style.Primary
                : secondaryUnique
                    ? ImStyle.WithAlpha(style.TextPrimary, 190)
                    : ImStyle.WithAlpha(style.TextSecondary, 190);
            Im.Text(KeyIcon.AsSpan(), badgeX, textY + 1, badgeFontSize, badgeColor);
        }

        // Separator
        Im.DrawLine(rowX, itemY + RowHeight, rowX + rowW, itemY + RowHeight, 1f, ImStyle.WithAlpha(style.Border, 90));

        return itemY + RowHeight;
    }

    private static int FindDerivedProjectionIndex(DocDerivedConfig config, string outputColumnId)
    {
        for (int i = 0; i < config.Projections.Count; i++)
        {
            if (string.Equals(config.Projections[i].OutputColumnId, outputColumnId, StringComparison.Ordinal))
            {
                return i;
            }
        }
        return -1;
    }

    private static void BeginInlineDerivedColumnRename(
        DocTable table,
        string columnId,
        string currentName,
        bool selectAll)
    {
        _inlineRenameDerivedColumnActive = true;
        _inlineRenameDerivedTableId = table.Id;
        _inlineRenameDerivedColumnId = columnId;
        _renameDerivedColumnBufferLength = Math.Min(currentName.Length, _renameDerivedColumnBuffer.Length);
        currentName.AsSpan(0, _renameDerivedColumnBufferLength).CopyTo(_renameDerivedColumnBuffer);
        _inlineRenameDerivedNeedsFocus = true;
        _inlineRenameDerivedSelectAll = selectAll;
    }

    private static void CommitInlineDerivedColumnRename(DocWorkspace workspace, DocTable table)
    {
        if (!_inlineRenameDerivedColumnActive)
        {
            return;
        }

        if (!string.Equals(table.Id, _inlineRenameDerivedTableId, StringComparison.Ordinal))
        {
            CancelInlineDerivedColumnRename();
            return;
        }

        string newName = _renameDerivedColumnBufferLength > 0
            ? new string(_renameDerivedColumnBuffer, 0, _renameDerivedColumnBufferLength)
            : "";

        if (newName.Length <= 0)
        {
            CancelInlineDerivedColumnRename();
            return;
        }

        var column = table.Columns.Find(c => string.Equals(c.Id, _inlineRenameDerivedColumnId, StringComparison.Ordinal));
        if (column == null)
        {
            CancelInlineDerivedColumnRename();
            return;
        }

        if (column.IsProjected && table.DerivedConfig != null)
        {
            int projectionIndex = FindDerivedProjectionIndex(table.DerivedConfig, column.Id);
            if (projectionIndex >= 0)
            {
                var oldProjection = table.DerivedConfig.Projections[projectionIndex].Clone();
                var newProjection = oldProjection.Clone();

                string sourceName = ResolveProjectionSourceColumnName(workspace, oldProjection, column.Name);
                newProjection.RenameAlias = string.Equals(newName, sourceName, StringComparison.Ordinal)
                    ? ""
                    : newName;

                if (!string.Equals(oldProjection.RenameAlias, newProjection.RenameAlias, StringComparison.Ordinal))
                {
                    workspace.ExecuteCommand(new DocCommand
                    {
                        Kind = DocCommandKind.UpdateDerivedProjection,
                        TableId = table.Id,
                        ProjectionIndex = projectionIndex,
                        OldProjectionSnapshot = oldProjection,
                        ProjectionSnapshot = newProjection,
                    });
                }
            }
        }
        else if (!string.Equals(column.Name, newName, StringComparison.Ordinal))
        {
            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.RenameColumn,
                TableId = table.Id,
                ColumnId = column.Id,
                OldName = column.Name,
                NewName = newName,
            });
        }

        CancelInlineDerivedColumnRename();
    }

    private static string ResolveProjectionSourceColumnName(
        DocWorkspace workspace,
        DerivedProjection projection,
        string fallbackName)
    {
        if (string.IsNullOrEmpty(projection.SourceTableId) || string.IsNullOrEmpty(projection.SourceColumnId))
        {
            return fallbackName;
        }

        var sourceTable = FindTableById(workspace, projection.SourceTableId);
        if (sourceTable == null)
        {
            return fallbackName;
        }

        var sourceColumn = FindSourceColumn(sourceTable, projection.SourceColumnId);
        return sourceColumn?.Name ?? fallbackName;
    }

    private static void CancelInlineDerivedColumnRename()
    {
        Im.ClearTextInputState(GetInlineDerivedColumnRenameInputId());
        _inlineRenameDerivedColumnActive = false;
        _inlineRenameDerivedNeedsFocus = false;
        _inlineRenameDerivedSelectAll = false;
        _renameDerivedColumnBufferLength = 0;
        _inlineRenameDerivedTableId = "";
        _inlineRenameDerivedColumnId = "";
        _inlineRenameDerivedInputRectValid = false;
    }

    private static string GetInlineDerivedColumnRenameInputId()
    {
        return "inspector_derived_column_rename";
    }

    private static bool IsInlineDerivedColumnRenameFocused()
    {
        if (!_inlineRenameDerivedColumnActive)
        {
            return false;
        }

        int widgetId = Im.Context.GetId(GetInlineDerivedColumnRenameInputId());
        return Im.Context.IsFocused(widgetId);
    }

    private static bool IsInlineRenameDerivedColumnActiveFor(string tableId, string columnId)
    {
        return _inlineRenameDerivedColumnActive &&
               string.Equals(_inlineRenameDerivedTableId, tableId, StringComparison.Ordinal) &&
               string.Equals(_inlineRenameDerivedColumnId, columnId, StringComparison.Ordinal);
    }

    private static bool DrawVisibilityEyeToggle(
        string id,
        float x,
        float y,
        float width,
        float height,
        ref bool visible,
        ImStyle style)
    {
        var ctx = Im.Context;
        int widgetId = ctx.GetId(id);
        var rect = new ImRect(x, y, width, height);
        bool hovered = rect.Contains(Im.MousePos);
        bool clicked = false;

        if (hovered)
        {
            ctx.SetHot(widgetId);
            if (ctx.IsHot(widgetId) && ctx.ActiveId == 0 && ctx.Input.MousePressed)
            {
                ctx.SetActive(widgetId);
            }
        }

        if (ctx.IsActive(widgetId) && ctx.Input.MouseReleased)
        {
            clicked = hovered;
            ctx.ClearActive();
        }

        if (clicked)
        {
            visible = !visible;
        }

        uint background;
        if (visible)
        {
            background = ctx.IsActive(widgetId)
                ? ImStyle.WithAlpha(style.Primary, 190)
                : ctx.IsHot(widgetId)
                    ? ImStyle.WithAlpha(style.Primary, 164)
                    : ImStyle.WithAlpha(style.Primary, 136);
        }
        else
        {
            background = ctx.IsActive(widgetId)
                ? style.Active
                : ctx.IsHot(widgetId)
                    ? style.Hover
                    : style.Surface;
        }

        Im.DrawRoundedRect(x, y, width, height, style.CornerRadius, background);

        string icon = visible ? EyeIcon : EyeSlashIcon;
        float iconFontSize = style.FontSize - 1f;
        float iconX = x + (width - Im.MeasureTextWidth(icon.AsSpan(), iconFontSize)) * 0.5f;
        float iconY = y + (height - iconFontSize) * 0.5f;
        uint iconColor = visible ? style.TextPrimary : ImStyle.WithAlpha(style.TextSecondary, 190);
        Im.Text(icon.AsSpan(), iconX, iconY, iconFontSize, iconColor);

        return clicked;
    }

    private static float DrawPlaceholderSection(
        string title,
        string subtitle,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        float headerX = contentRect.X;
        float headerW = contentRect.Width;

        Im.DrawLine(headerX, y, headerX + headerW, y, 1f, style.Border);

        float textY = y + (SectionHeaderHeight - style.FontSize) * 0.5f;
        const float chevronSize = 10f;
        float chevronX = headerX + Padding;
        float chevronY = y + (SectionHeaderHeight - chevronSize) * 0.5f;
        ImIcons.DrawChevron(
            chevronX,
            chevronY,
            chevronSize,
            ImIcons.ChevronDirection.Right,
            ImStyle.WithAlpha(style.TextSecondary, 180));
        float labelX = chevronX + chevronSize + 6f;
        Im.Text(title.AsSpan(), labelX, textY, style.FontSize, style.TextSecondary);

        float subtitleX = labelX + Im.MeasureTextWidth(title.AsSpan(), style.FontSize) + 6;
        Im.Text(subtitle.AsSpan(), subtitleX, textY, style.FontSize - 1f, ImStyle.WithAlpha(style.TextSecondary, 120));

        return y + SectionHeaderHeight;
    }

    private static float DrawTableVariablesSection(
        DocWorkspace workspace,
        DocTable table,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        int variableCount = table.Variables.Count;

        float headerX = contentRect.X + SectionHorizontalInset;
        float headerWidth = contentRect.Width - (SectionHorizontalInset * 2f);
        float addButtonX = headerX + headerWidth - Padding - DeleteButtonSize;
        float addButtonY = y + (SectionHeaderHeight - DeleteButtonSize) * 0.5f;
        float headerRightOverlayWidth = DeleteButtonSize + Padding + 4f;

        y = DrawCollapsibleHeader("Table Variables", ref _tableVariablesExpanded, contentRect, y, style, variableCount, headerRightOverlayWidth);
        Im.Context.PushId("table_variables");
        if (Im.Button(PlusIcon, addButtonX, addButtonY, DeleteButtonSize, DeleteButtonSize))
        {
            string variableName = BuildUniqueTableVariableName(table);
            var newVariable = new DocTableVariable
            {
                Name = variableName,
                Expression = "",
            };

            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.AddTableVariable,
                TableId = table.Id,
                TableVariableId = newVariable.Id,
                TableVariableIndex = table.Variables.Count,
                TableVariableSnapshot = newVariable,
            });
            variableCount = table.Variables.Count;
        }
        Im.Context.PopId();

        if (!_tableVariablesExpanded)
        {
            return y;
        }

        y += SectionHeaderContentSpacing;

        float rowX = contentRect.X + SectionHorizontalInset + RowHorizontalInset;
        float rowWidth = contentRect.Width - (SectionHorizontalInset + RowHorizontalInset) * 2f;
        if (variableCount <= 0)
        {
            float textY = y + (RowHeight - style.FontSize) * 0.5f;
            Im.Text("No table variables".AsSpan(), rowX + 4f, textY, style.FontSize - 1f, style.TextSecondary);
            y += RowHeight;
            return y;
        }

        EnsureTableVariableBufferCapacity(variableCount);
        int variableTypeOptionCount = BuildTableVariableTypeOptions();

        int removeVariableIndex = -1;
        for (int variableIndex = 0; variableIndex < variableCount; variableIndex++)
        {
            var tableVariable = table.Variables[variableIndex];
            float rowY = y;
            bool rowHovered = new ImRect(rowX, rowY, rowWidth, RowHeight).Contains(Im.MousePos);
            if (rowHovered)
            {
                Im.DrawRect(rowX, rowY, rowWidth, RowHeight, ImStyle.WithAlpha(style.Hover, 88));
            }

            float removeButtonX = rowX + rowWidth - DeleteButtonSize;
            float removeButtonY = rowY + (RowHeight - DeleteButtonSize) * 0.5f;
            float nameWidth = MathF.Max(72f, rowWidth * 0.2f);
            float typeWidth = MathF.Max(110f, rowWidth * 0.24f);
            float typeX = rowX + nameWidth + 6f;
            float expressionX = typeX + typeWidth + 6f;
            float expressionWidth = MathF.Max(80f, removeButtonX - expressionX - 6f);

            SyncTableVariableNameBuffer(variableIndex, tableVariable.Name);
            Im.Context.PushId(tableVariable.Id);
            Im.TextInput(
                "table_var_name",
                _tableVariableNameBuffers[variableIndex],
                ref _tableVariableNameLengths[variableIndex],
                _tableVariableNameBuffers[variableIndex].Length,
                rowX,
                rowY,
                nameWidth);
            if (ShouldCommitTextInput("table_var_name", ref _tableVariableNameFocused[variableIndex]))
            {
                string editedVariableName = new string(
                    _tableVariableNameBuffers[variableIndex],
                    0,
                    _tableVariableNameLengths[variableIndex]).Trim();
                if (!string.Equals(editedVariableName, tableVariable.Name, StringComparison.Ordinal))
                {
                    if (IsTableVariableNameValid(editedVariableName) &&
                        IsTableVariableNameAvailable(table, editedVariableName, tableVariable.Id))
                    {
                        workspace.ExecuteCommand(new DocCommand
                        {
                            Kind = DocCommandKind.RenameTableVariable,
                            TableId = table.Id,
                            TableVariableId = tableVariable.Id,
                            OldName = tableVariable.Name,
                            NewName = editedVariableName,
                        });
                        _tableVariableNameSyncKeys[variableIndex] = editedVariableName;
                    }
                    else
                    {
                        _tableVariableNameSyncKeys[variableIndex] = "";
                    }
                }
            }

            string currentVariableTypeId = DocColumnTypeIdMapper.Resolve(tableVariable.ColumnTypeId, tableVariable.Kind);
            int selectedVariableTypeIndex = FindTableVariableTypeOptionIndex(currentVariableTypeId, tableVariable.Kind, variableTypeOptionCount);
            if (Im.Dropdown("table_var_type", _tableVariableTypeOptionNames.AsSpan(0, variableTypeOptionCount), ref selectedVariableTypeIndex, typeX, rowY, typeWidth, InspectorDropdownFlags))
            {
                DocColumnKind newVariableKind = _tableVariableTypeOptionKinds[selectedVariableTypeIndex];
                string newVariableTypeId = _tableVariableTypeOptionTypeIds[selectedVariableTypeIndex];
                if (newVariableKind != tableVariable.Kind ||
                    !string.Equals(newVariableTypeId, currentVariableTypeId, StringComparison.OrdinalIgnoreCase))
                {
                    workspace.ExecuteCommand(new DocCommand
                    {
                        Kind = DocCommandKind.SetTableVariableType,
                        TableId = table.Id,
                        TableVariableId = tableVariable.Id,
                        OldTableVariableKind = tableVariable.Kind,
                        NewTableVariableKind = newVariableKind,
                        OldTableVariableTypeId = currentVariableTypeId,
                        NewTableVariableTypeId = newVariableTypeId,
                    });
                }
            }

            SyncTableVariableExpressionBuffer(variableIndex, tableVariable.Expression);
            Im.TextInput(
                "table_var_expr",
                _tableVariableExpressionBuffers[variableIndex],
                ref _tableVariableExpressionLengths[variableIndex],
                _tableVariableExpressionBuffers[variableIndex].Length,
                expressionX,
                rowY,
                expressionWidth);
            if (ShouldCommitTextInput("table_var_expr", ref _tableVariableExpressionFocused[variableIndex]))
            {
                string editedExpression = new string(
                    _tableVariableExpressionBuffers[variableIndex],
                    0,
                    _tableVariableExpressionLengths[variableIndex]);
                if (!string.Equals(editedExpression, tableVariable.Expression, StringComparison.Ordinal))
                {
                    workspace.ExecuteCommand(new DocCommand
                    {
                        Kind = DocCommandKind.SetTableVariableExpression,
                        TableId = table.Id,
                        TableVariableId = tableVariable.Id,
                        OldTableVariableExpression = tableVariable.Expression,
                        NewTableVariableExpression = editedExpression,
                    });
                    _tableVariableExpressionSyncKeys[variableIndex] = editedExpression;
                }
            }

            if (rowHovered && Im.Button(MinusIcon, removeButtonX, removeButtonY, DeleteButtonSize, DeleteButtonSize))
            {
                removeVariableIndex = variableIndex;
            }
            Im.Context.PopId();

            Im.DrawLine(rowX, rowY + RowHeight, rowX + rowWidth, rowY + RowHeight, 1f, ImStyle.WithAlpha(style.Border, 90));
            y += RowHeight;
        }

        if (removeVariableIndex >= 0 && removeVariableIndex < table.Variables.Count)
        {
            var removeVariable = table.Variables[removeVariableIndex];
            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.RemoveTableVariable,
                TableId = table.Id,
                TableVariableId = removeVariable.Id,
                TableVariableIndex = removeVariableIndex,
                TableVariableSnapshot = removeVariable.Clone(),
            });
        }

        return y;
    }

    private static float DrawTableInstanceVariablesSection(
        DocWorkspace workspace,
        DocTable table,
        DocBlock block,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        int variableCount = table.Variables.Count;
        int overrideCount = block.TableVariableOverrides.Count;
        y = DrawCollapsibleHeader(
            "Instance Variables",
            ref _tableInstanceVariablesExpanded,
            contentRect,
            y,
            style,
            overrideCount);
        if (!_tableInstanceVariablesExpanded)
        {
            return y;
        }

        y += SectionHeaderContentSpacing;
        float rowX = contentRect.X + SectionHorizontalInset + RowHorizontalInset;
        float rowWidth = contentRect.Width - (SectionHorizontalInset + RowHorizontalInset) * 2f;

        if (variableCount <= 0)
        {
            float textY = y + (RowHeight - style.FontSize) * 0.5f;
            Im.Text("No table variables".AsSpan(), rowX + 4f, textY, style.FontSize - 1f, style.TextSecondary);
            y += RowHeight;
            return y;
        }

        EnsureTableInstanceVariableBufferCapacity(variableCount);
        for (int variableIndex = 0; variableIndex < variableCount; variableIndex++)
        {
            var tableVariable = table.Variables[variableIndex];
            bool hasOverride = TryGetTableInstanceVariableOverrideExpression(
                block,
                tableVariable.Id,
                out string overrideExpression);
            string displayExpression = hasOverride ? overrideExpression : tableVariable.Expression;
            SyncTableInstanceVariableExpressionBuffer(variableIndex, displayExpression);

            float rowY = y;
            bool rowHovered = new ImRect(rowX, rowY, rowWidth, RowHeight).Contains(Im.MousePos);
            if (rowHovered)
            {
                Im.DrawRect(rowX, rowY, rowWidth, RowHeight, ImStyle.WithAlpha(style.Hover, 84));
            }

            float resetButtonWidth = 52f;
            float resetButtonX = rowX + rowWidth - resetButtonWidth;
            float nameWidth = MathF.Max(92f, rowWidth * 0.25f);
            float expressionX = rowX + nameWidth + 6f;
            float expressionWidth = MathF.Max(80f, resetButtonX - expressionX - 6f);

            float textY = rowY + (RowHeight - style.FontSize) * 0.5f;
            uint nameColor = hasOverride ? style.TextPrimary : style.TextSecondary;
            Im.Text(tableVariable.Name.AsSpan(), rowX + 2f, textY, style.FontSize - 1f, nameColor);

            Im.Context.PushId(tableVariable.Id + "_instance");
            Im.TextInput(
                "table_instance_var_expr",
                _tableInstanceVariableExpressionBuffers[variableIndex],
                ref _tableInstanceVariableExpressionLengths[variableIndex],
                _tableInstanceVariableExpressionBuffers[variableIndex].Length,
                expressionX,
                rowY,
                expressionWidth);

            if (ShouldCommitTextInput("table_instance_var_expr", ref _tableInstanceVariableExpressionFocused[variableIndex]))
            {
                string editedExpression = new string(
                    _tableInstanceVariableExpressionBuffers[variableIndex],
                    0,
                    _tableInstanceVariableExpressionLengths[variableIndex]);
                HandleTableInstanceVariableExpressionCommit(
                    workspace,
                    table,
                    block,
                    tableVariable,
                    hasOverride,
                    overrideExpression,
                    editedExpression);
            }

            bool resetClicked = Im.Button("Reset", resetButtonX, rowY, resetButtonWidth, RowHeight);
            if (hasOverride && resetClicked)
            {
                ExecuteSetBlockTableVariableOverrideCommand(
                    workspace,
                    table,
                    block,
                    tableVariable.Id,
                    overrideExpression,
                    newExpression: "");
            }

            Im.Context.PopId();
            Im.DrawLine(rowX, rowY + RowHeight, rowX + rowWidth, rowY + RowHeight, 1f, ImStyle.WithAlpha(style.Border, 80));
            y += RowHeight;
        }

        return y;
    }

    private static void HandleTableInstanceVariableExpressionCommit(
        DocWorkspace workspace,
        DocTable table,
        DocBlock block,
        DocTableVariable tableVariable,
        bool hasOverride,
        string overrideExpression,
        string editedExpression)
    {
        if (hasOverride && string.Equals(editedExpression, overrideExpression, StringComparison.Ordinal))
        {
            return;
        }

        if (!hasOverride && string.Equals(editedExpression, tableVariable.Expression, StringComparison.Ordinal))
        {
            return;
        }

        if (hasOverride &&
            (string.IsNullOrWhiteSpace(editedExpression) ||
             string.Equals(editedExpression, tableVariable.Expression, StringComparison.Ordinal)))
        {
            ExecuteSetBlockTableVariableOverrideCommand(
                workspace,
                table,
                block,
                tableVariable.Id,
                overrideExpression,
                newExpression: "");
            return;
        }

        if (!hasOverride && string.IsNullOrWhiteSpace(editedExpression))
        {
            return;
        }

        ExecuteSetBlockTableVariableOverrideCommand(
            workspace,
            table,
            block,
            tableVariable.Id,
            oldExpression: hasOverride ? overrideExpression : "",
            newExpression: editedExpression);
    }

    private static void ExecuteSetBlockTableVariableOverrideCommand(
        DocWorkspace workspace,
        DocTable table,
        DocBlock block,
        string variableId,
        string oldExpression,
        string newExpression)
    {
        if (workspace.ActiveDocument == null)
        {
            return;
        }

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetBlockTableVariableOverride,
            TableId = table.Id,
            TableVariableId = variableId,
            DocumentId = workspace.ActiveDocument.Id,
            BlockId = block.Id,
            OldBlockTableVariableExpression = oldExpression,
            NewBlockTableVariableExpression = newExpression,
        });
    }

    private static bool TryGetTableInstanceVariableOverrideExpression(
        DocBlock block,
        string variableId,
        out string expression)
    {
        for (int overrideIndex = 0; overrideIndex < block.TableVariableOverrides.Count; overrideIndex++)
        {
            var tableVariableOverride = block.TableVariableOverrides[overrideIndex];
            if (string.Equals(tableVariableOverride.VariableId, variableId, StringComparison.Ordinal))
            {
                expression = tableVariableOverride.Expression;
                return true;
            }
        }

        expression = "";
        return false;
    }

    private static void EnsureTableVariableTypeOptionCapacity(int required)
    {
        if (_tableVariableTypeOptionNames.Length >= required)
        {
            return;
        }

        int newLength = _tableVariableTypeOptionNames.Length;
        while (newLength < required)
        {
            newLength *= 2;
        }

        Array.Resize(ref _tableVariableTypeOptionNames, newLength);
        Array.Resize(ref _tableVariableTypeOptionTypeIds, newLength);
        Array.Resize(ref _tableVariableTypeOptionKinds, newLength);
    }

    private static int BuildTableVariableTypeOptions()
    {
        int optionCount = 0;
        EnsureTableVariableTypeOptionCapacity(TableVariableKindNames.Length + 16);
        for (int builtInKindIndex = 0; builtInKindIndex < TableVariableKindNames.Length; builtInKindIndex++)
        {
            DocColumnKind builtInKind = (DocColumnKind)builtInKindIndex;
            _tableVariableTypeOptionNames[optionCount] = TableVariableKindNames[builtInKindIndex];
            _tableVariableTypeOptionTypeIds[optionCount] = DocColumnTypeIdMapper.FromKind(builtInKind);
            _tableVariableTypeOptionKinds[optionCount] = builtInKind;
            optionCount++;
        }

        ColumnTypeDefinitionRegistry.CopyDefinitions(_tableVariableTypeDefinitionsScratch);
        for (int definitionIndex = 0; definitionIndex < _tableVariableTypeDefinitionsScratch.Count; definitionIndex++)
        {
            var typeDefinition = _tableVariableTypeDefinitionsScratch[definitionIndex];
            if (DocColumnTypeIdMapper.IsBuiltIn(typeDefinition.ColumnTypeId))
            {
                continue;
            }

            EnsureTableVariableTypeOptionCapacity(optionCount + 1);
            _tableVariableTypeOptionNames[optionCount] = typeDefinition.DisplayName;
            _tableVariableTypeOptionTypeIds[optionCount] = typeDefinition.ColumnTypeId;
            _tableVariableTypeOptionKinds[optionCount] = typeDefinition.FallbackKind;
            optionCount++;
        }

        return optionCount;
    }

    private static int FindTableVariableTypeOptionIndex(string currentTypeId, DocColumnKind fallbackKind, int optionCount)
    {
        string resolvedTypeId = DocColumnTypeIdMapper.Resolve(currentTypeId, fallbackKind);
        for (int optionIndex = 0; optionIndex < optionCount; optionIndex++)
        {
            if (string.Equals(_tableVariableTypeOptionTypeIds[optionIndex], resolvedTypeId, StringComparison.OrdinalIgnoreCase))
            {
                return optionIndex;
            }
        }

        string fallbackTypeId = DocColumnTypeIdMapper.FromKind(fallbackKind);
        for (int optionIndex = 0; optionIndex < optionCount; optionIndex++)
        {
            if (string.Equals(_tableVariableTypeOptionTypeIds[optionIndex], fallbackTypeId, StringComparison.OrdinalIgnoreCase))
            {
                return optionIndex;
            }
        }

        return 0;
    }

    private static void EnsureTableVariableBufferCapacity(int required)
    {
        if (_tableVariableNameBuffers.Length >= required)
        {
            return;
        }

        int newLength = _tableVariableNameBuffers.Length;
        while (newLength < required)
        {
            newLength *= 2;
        }

        Array.Resize(ref _tableVariableNameBuffers, newLength);
        Array.Resize(ref _tableVariableNameLengths, newLength);
        Array.Resize(ref _tableVariableNameFocused, newLength);
        Array.Resize(ref _tableVariableNameSyncKeys, newLength);
        Array.Resize(ref _tableVariableExpressionBuffers, newLength);
        Array.Resize(ref _tableVariableExpressionLengths, newLength);
        Array.Resize(ref _tableVariableExpressionFocused, newLength);
        Array.Resize(ref _tableVariableExpressionSyncKeys, newLength);
    }

    private static void EnsureTableVariantNameBufferCapacity(int required)
    {
        if (_tableVariantNameBuffers.Length >= required)
        {
            return;
        }

        int newLength = _tableVariantNameBuffers.Length;
        while (newLength < required)
        {
            newLength *= 2;
        }

        Array.Resize(ref _tableVariantNameBuffers, newLength);
        Array.Resize(ref _tableVariantNameLengths, newLength);
        Array.Resize(ref _tableVariantNameFocused, newLength);
        Array.Resize(ref _tableVariantNameSyncKeys, newLength);
    }

    private static void EnsureTableInstanceVariableBufferCapacity(int required)
    {
        if (_tableInstanceVariableExpressionBuffers.Length >= required)
        {
            return;
        }

        int newLength = _tableInstanceVariableExpressionBuffers.Length;
        while (newLength < required)
        {
            newLength *= 2;
        }

        Array.Resize(ref _tableInstanceVariableExpressionBuffers, newLength);
        Array.Resize(ref _tableInstanceVariableExpressionLengths, newLength);
        Array.Resize(ref _tableInstanceVariableExpressionFocused, newLength);
        Array.Resize(ref _tableInstanceVariableExpressionSyncKeys, newLength);
    }

    private static void SyncTableVariableNameBuffer(int variableIndex, string sourceName)
    {
        _tableVariableNameBuffers[variableIndex] ??= new char[128];
        string syncKey = _tableVariableNameSyncKeys[variableIndex] ?? "";
        if (string.Equals(syncKey, sourceName, StringComparison.Ordinal))
        {
            return;
        }

        int copyLength = Math.Min(sourceName.Length, _tableVariableNameBuffers[variableIndex].Length);
        sourceName.AsSpan(0, copyLength).CopyTo(_tableVariableNameBuffers[variableIndex]);
        _tableVariableNameLengths[variableIndex] = copyLength;
        _tableVariableNameSyncKeys[variableIndex] = sourceName;
    }

    private static void SyncTableVariantNameBuffer(int variantIndex, string sourceName)
    {
        _tableVariantNameBuffers[variantIndex] ??= new char[128];
        string syncKey = _tableVariantNameSyncKeys[variantIndex] ?? "";
        if (string.Equals(syncKey, sourceName, StringComparison.Ordinal))
        {
            return;
        }

        int copyLength = Math.Min(sourceName.Length, _tableVariantNameBuffers[variantIndex].Length);
        sourceName.AsSpan(0, copyLength).CopyTo(_tableVariantNameBuffers[variantIndex]);
        _tableVariantNameLengths[variantIndex] = copyLength;
        _tableVariantNameSyncKeys[variantIndex] = sourceName;
    }

    private static void SyncTableVariableExpressionBuffer(int variableIndex, string expression)
    {
        _tableVariableExpressionBuffers[variableIndex] ??= new char[256];
        string syncKey = _tableVariableExpressionSyncKeys[variableIndex] ?? "";
        if (string.Equals(syncKey, expression, StringComparison.Ordinal))
        {
            return;
        }

        int copyLength = Math.Min(expression.Length, _tableVariableExpressionBuffers[variableIndex].Length);
        expression.AsSpan(0, copyLength).CopyTo(_tableVariableExpressionBuffers[variableIndex]);
        _tableVariableExpressionLengths[variableIndex] = copyLength;
        _tableVariableExpressionSyncKeys[variableIndex] = expression;
    }

    private static void SyncTableInstanceVariableExpressionBuffer(int variableIndex, string expression)
    {
        _tableInstanceVariableExpressionBuffers[variableIndex] ??= new char[256];
        string syncKey = _tableInstanceVariableExpressionSyncKeys[variableIndex] ?? "";
        if (string.Equals(syncKey, expression, StringComparison.Ordinal))
        {
            return;
        }

        int copyLength = Math.Min(expression.Length, _tableInstanceVariableExpressionBuffers[variableIndex].Length);
        expression.AsSpan(0, copyLength).CopyTo(_tableInstanceVariableExpressionBuffers[variableIndex]);
        _tableInstanceVariableExpressionLengths[variableIndex] = copyLength;
        _tableInstanceVariableExpressionSyncKeys[variableIndex] = expression;
    }

    private static string BuildUniqueTableVariableName(DocTable table)
    {
        const string baseName = "variable";
        if (IsTableVariableNameAvailable(table, baseName, currentVariableId: ""))
        {
            return baseName;
        }

        for (int suffix = 2; suffix < 1000; suffix++)
        {
            string candidateName = baseName + suffix;
            if (IsTableVariableNameAvailable(table, candidateName, currentVariableId: ""))
            {
                return candidateName;
            }
        }

        return baseName + "_" + Guid.NewGuid().ToString("N");
    }

    private static bool IsTableVariableNameValid(string variableName)
    {
        return !string.IsNullOrWhiteSpace(variableName) &&
               DocumentFormulaSyntax.IsValidIdentifier(variableName.AsSpan());
    }

    private static bool IsTableVariableNameAvailable(DocTable table, string candidateName, string currentVariableId)
    {
        for (int variableIndex = 0; variableIndex < table.Variables.Count; variableIndex++)
        {
            var tableVariable = table.Variables[variableIndex];
            if (string.Equals(tableVariable.Id, currentVariableId, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(tableVariable.Name, candidateName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    // =====================================================================
    //  View Config Sections (Filters, Sorts, Group By / Date Column)
    // =====================================================================

    private static void ApplyViewConfigChange(DocWorkspace workspace, DocTable table, DocView view, DocView oldSnapshot)
    {
        // For embedded block contexts, ensure the block has its own view before modifying
        var block = FindInspectedBlock(workspace);
        if (block != null && string.IsNullOrEmpty(block.ViewId))
        {
            // The view being modified is a shared view — clone it as a per-block view
            var perBlockView = EnsurePerBlockView(workspace, table);
            if (perBlockView != null && perBlockView != view)
            {
                // Copy the pending changes to the new per-block view
                var newSnapshot = view.Clone();
                newSnapshot.Id = perBlockView.Id;
                newSnapshot.Name = perBlockView.Name;
                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.UpdateViewConfig,
                    TableId = table.Id,
                    ViewId = perBlockView.Id,
                    ViewSnapshot = newSnapshot,
                    OldViewSnapshot = perBlockView.Clone(),
                });
                return;
            }
        }

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.UpdateViewConfig,
            TableId = table.Id,
            ViewId = view.Id,
            ViewSnapshot = view.Clone(),
            OldViewSnapshot = oldSnapshot,
        });
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

    private static void DrawBoundValueField(string valueText, float x, float y, float width, ImStyle style, bool isValid = true)
    {
        uint textColor = isValid ? style.TextPrimary : style.Primary;

        Im.DrawRoundedRect(x, y, width, RowHeight, style.CornerRadius, style.Surface);

        float textY = y + (RowHeight - style.FontSize) * 0.5f;
        float iconX = x + 6f;
        Im.Text(LinkIcon.AsSpan(), iconX, textY + 0.5f, style.FontSize - 1f, ImStyle.WithAlpha(style.TextSecondary, 180));

        float valueX = iconX + style.FontSize + 4f;
        Im.Text(valueText.AsSpan(), valueX, textY, style.FontSize - 1f, textColor);
    }

    private static void QueueViewBindingContextMenu(
        DocTable table,
        DocView view,
        ViewBindingTargetKind targetKind,
        string targetItemId,
        string label,
        ImRect controlRect,
        bool isBound)
    {
        if (!controlRect.Contains(Im.MousePos))
        {
            return;
        }

        var input = Im.Context.Input;
        if ((isBound && input.MousePressed && !input.MouseRightPressed) || input.MouseRightPressed)
        {
            OpenViewBindingPopover(table, view, targetKind, targetItemId, label, controlRect);
            return;
        }
    }

    private static void OpenViewBindingPopover(
        DocTable table,
        DocView view,
        ViewBindingTargetKind targetKind,
        string targetItemId,
        string label,
        ImRect anchorRect)
    {
        _viewBindingPopoverActive = true;
        _viewBindingPopoverTableId = table.Id;
        _viewBindingPopoverViewId = view.Id;
        _viewBindingPopoverTargetKind = targetKind;
        _viewBindingPopoverTargetItemId = targetItemId;
        _viewBindingPopoverLabel = label;
        _viewBindingPopoverAnchorRect = anchorRect;
        _viewBindingPopoverOpenedFrame = Im.Context.FrameCount;
        _viewBindingPopoverFormulaLength = 0;
        _viewBindingPopoverFormulaFocused = false;
        _viewBindingPopoverFormulaSyncKey = "";
        _viewBindingPopoverSelectedVariableIndex = 0;
        _viewBindingPopoverSelectionInitialized = false;
    }

    private static void CloseViewBindingPopover()
    {
        _viewBindingPopoverActive = false;
        _viewBindingPopoverTableId = "";
        _viewBindingPopoverViewId = "";
        _viewBindingPopoverTargetKind = ViewBindingTargetKind.None;
        _viewBindingPopoverTargetItemId = "";
        _viewBindingPopoverLabel = "";
        _viewBindingPopoverFormulaLength = 0;
        _viewBindingPopoverFormulaFocused = false;
        _viewBindingPopoverFormulaSyncKey = "";
        _viewBindingPopoverSelectedVariableIndex = 0;
        _viewBindingPopoverSelectionInitialized = false;
    }

    private static void SyncViewBindingPopoverTargetViewId(DocWorkspace workspace, DocTable table)
    {
        var inspectedView = ResolveInspectedView(workspace, table);
        if (inspectedView != null)
        {
            _viewBindingPopoverViewId = inspectedView.Id;
            _viewBindingPopoverSelectionInitialized = false;
        }
    }

    private static int BuildViewBindingVariableOptions(
        DocTable table,
        ViewBindingTargetKind targetKind,
        string currentVariableName)
    {
        EnsureScratchCapacity(table.Variables.Count + 1);
        int optionCount = 1;
        _scratchOptionNames[0] = "(none)";
        _scratchOptionIds[0] = "";
        for (int variableIndex = 0; variableIndex < table.Variables.Count; variableIndex++)
        {
            var tableVariable = table.Variables[variableIndex];
            bool includeVariable =
                IsVariableKindCompatibleWithBindingTarget(tableVariable.Kind, targetKind) ||
                (!string.IsNullOrWhiteSpace(currentVariableName) &&
                 string.Equals(tableVariable.Name, currentVariableName, StringComparison.Ordinal));
            if (!includeVariable)
            {
                continue;
            }

            _scratchOptionNames[optionCount] = tableVariable.Name;
            _scratchOptionIds[optionCount] = tableVariable.Name;
            optionCount++;
        }

        return optionCount;
    }

    private static bool IsVariableKindCompatibleWithBindingTarget(DocColumnKind variableKind, ViewBindingTargetKind targetKind)
    {
        return targetKind switch
        {
            ViewBindingTargetKind.SortDescending => variableKind == DocColumnKind.Checkbox || variableKind == DocColumnKind.Formula,
            ViewBindingTargetKind.FilterOperator or ViewBindingTargetKind.ChartKind =>
                variableKind == DocColumnKind.Id ||
                variableKind == DocColumnKind.Text ||
                variableKind == DocColumnKind.Select ||
                variableKind == DocColumnKind.TableRef ||
                variableKind == DocColumnKind.Formula,
            ViewBindingTargetKind.FilterValue =>
                variableKind == DocColumnKind.Id ||
                variableKind == DocColumnKind.Text ||
                variableKind == DocColumnKind.Number ||
                variableKind == DocColumnKind.Checkbox ||
                variableKind == DocColumnKind.Select ||
                variableKind == DocColumnKind.TableRef ||
                variableKind == DocColumnKind.Formula,
            _ => variableKind == DocColumnKind.Id ||
                 variableKind == DocColumnKind.Text ||
                 variableKind == DocColumnKind.Select ||
                 variableKind == DocColumnKind.TableRef ||
                 variableKind == DocColumnKind.Formula,
        };
    }

    private static bool AreBindingsEqual(DocViewBinding? leftBinding, DocViewBinding? rightBinding)
    {
        string leftVariableName = leftBinding?.VariableName ?? "";
        string rightVariableName = rightBinding?.VariableName ?? "";
        if (!string.Equals(leftVariableName, rightVariableName, StringComparison.Ordinal))
        {
            return false;
        }

        string leftFormula = leftBinding?.FormulaExpression ?? "";
        string rightFormula = rightBinding?.FormulaExpression ?? "";
        return string.Equals(leftFormula, rightFormula, StringComparison.Ordinal);
    }

    private static void ApplyViewBindingPopoverEdits(
        DocWorkspace workspace,
        DocTable table,
        DocView view,
        DocViewBinding? binding,
        int variableOptionCount)
    {
        int selectedVariableIndex = _viewBindingPopoverSelectedVariableIndex;
        if (selectedVariableIndex < 0 || selectedVariableIndex >= variableOptionCount)
        {
            selectedVariableIndex = 0;
        }

        string selectedVariableName = selectedVariableIndex > 0 ? _scratchOptionIds[selectedVariableIndex] : "";
        string editedFormula = new string(_viewBindingPopoverFormulaBuffer, 0, _viewBindingPopoverFormulaLength);
        var updatedBinding = binding?.Clone() ?? new DocViewBinding();
        updatedBinding.VariableName = selectedVariableName;
        updatedBinding.FormulaExpression = editedFormula;

        bool existingIsEmpty = binding == null || binding.IsEmpty;
        if (updatedBinding.IsEmpty)
        {
            if (existingIsEmpty)
            {
                return;
            }

            ApplyViewBindingTargetUpdate(workspace, table, view, _viewBindingPopoverTargetKind, _viewBindingPopoverTargetItemId, null);
            SyncViewBindingPopoverTargetViewId(workspace, table);
            _viewBindingPopoverFormulaSyncKey = "";
            return;
        }

        if (!existingIsEmpty &&
            string.Equals(binding!.VariableName, updatedBinding.VariableName, StringComparison.Ordinal) &&
            string.Equals(binding.FormulaExpression, updatedBinding.FormulaExpression, StringComparison.Ordinal))
        {
            return;
        }

        ApplyViewBindingTargetUpdate(workspace, table, view, _viewBindingPopoverTargetKind, _viewBindingPopoverTargetItemId, updatedBinding);
        SyncViewBindingPopoverTargetViewId(workspace, table);
        _viewBindingPopoverFormulaSyncKey = editedFormula;
    }

    private static void DrawViewBindingPopover(DocWorkspace workspace, DocTable table)
    {
        if (!_viewBindingPopoverActive ||
            !string.Equals(_viewBindingPopoverTableId, table.Id, StringComparison.Ordinal))
        {
            return;
        }

        if (!TryFindViewById(table, _viewBindingPopoverViewId, out var view) ||
            !TryGetViewBindingTarget(view, _viewBindingPopoverTargetKind, _viewBindingPopoverTargetItemId, out DocViewBinding? binding))
        {
            CloseViewBindingPopover();
            return;
        }

        float popoverWidth = 320f;
        float popoverHeight = 144f;
        float popoverX = _viewBindingPopoverAnchorRect.X;
        float popoverY = _viewBindingPopoverAnchorRect.Bottom + 6f;
        ImRect windowRect = Im.WindowContentRect;
        if (popoverX + popoverWidth > windowRect.Right)
        {
            popoverX = windowRect.Right - popoverWidth;
        }

        if (popoverX < windowRect.X)
        {
            popoverX = windowRect.X;
        }

        if (popoverY + popoverHeight > windowRect.Bottom)
        {
            popoverY = _viewBindingPopoverAnchorRect.Y - popoverHeight - 6f;
        }

        if (popoverY < windowRect.Y)
        {
            popoverY = windowRect.Y;
        }

        var popoverRect = new ImRect(popoverX, popoverY, popoverWidth, popoverHeight);
        var style = Im.Style;
        var input = Im.Context.Input;
        bool canCloseOnOutside = Im.Context.FrameCount > _viewBindingPopoverOpenedFrame;
        bool dropdownOpen = Im.IsAnyDropdownOpen;
        if (canCloseOnOutside &&
            !dropdownOpen &&
            (input.MousePressed || input.MouseRightPressed) &&
            !popoverRect.Contains(Im.MousePos))
        {
            CloseViewBindingPopover();
            return;
        }

        Im.DrawRoundedRect(popoverRect.X, popoverRect.Y, popoverRect.Width, popoverRect.Height, style.CornerRadius, style.Surface);

        float contentX = popoverRect.X + 8f;
        float contentY = popoverRect.Y + 8f;
        float contentWidth = popoverRect.Width - 16f;

        Im.Text("Binding".AsSpan(), contentX, contentY, style.FontSize, style.TextPrimary);
        float labelY = contentY + style.FontSize + 4f;
        Im.Text(_viewBindingPopoverLabel.AsSpan(), contentX, labelY, style.FontSize - 1f, style.TextSecondary);

        float controlY = labelY + style.FontSize + 6f;
        string currentVariableName = binding?.VariableName ?? "";
        int variableOptionCount = BuildViewBindingVariableOptions(table, _viewBindingPopoverTargetKind, currentVariableName);
        if (!_viewBindingPopoverSelectionInitialized)
        {
            _viewBindingPopoverSelectedVariableIndex = 0;
            for (int optionIndex = 1; optionIndex < variableOptionCount; optionIndex++)
            {
                if (string.Equals(_scratchOptionIds[optionIndex], currentVariableName, StringComparison.Ordinal))
                {
                    _viewBindingPopoverSelectedVariableIndex = optionIndex;
                    break;
                }
            }

            _viewBindingPopoverSelectionInitialized = true;
        }

        Im.Context.PushId("view_binding_popover");
        if (Im.Dropdown("variable", _scratchOptionNames.AsSpan(0, variableOptionCount), ref _viewBindingPopoverSelectedVariableIndex, contentX, controlY, contentWidth, InspectorDropdownFlags))
        {
            ApplyViewBindingPopoverEdits(workspace, table, view, binding, variableOptionCount);
        }

        string currentFormula = binding?.FormulaExpression ?? "";
        if (!_viewBindingPopoverFormulaFocused &&
            !string.Equals(_viewBindingPopoverFormulaSyncKey, currentFormula, StringComparison.Ordinal))
        {
            int copyLength = Math.Min(currentFormula.Length, _viewBindingPopoverFormulaBuffer.Length);
            currentFormula.AsSpan(0, copyLength).CopyTo(_viewBindingPopoverFormulaBuffer);
            _viewBindingPopoverFormulaLength = copyLength;
            _viewBindingPopoverFormulaSyncKey = currentFormula;
        }

        float formulaY = controlY + RowHeight + 6f;
        Im.TextInput(
            "formula",
            _viewBindingPopoverFormulaBuffer,
            ref _viewBindingPopoverFormulaLength,
            _viewBindingPopoverFormulaBuffer.Length,
            contentX,
            formulaY,
            contentWidth);
        if (ShouldCommitTextInput("formula", ref _viewBindingPopoverFormulaFocused))
        {
            string editedFormula = new string(_viewBindingPopoverFormulaBuffer, 0, _viewBindingPopoverFormulaLength);
            if (!string.Equals(editedFormula, currentFormula, StringComparison.Ordinal))
            {
                var updatedBinding = binding?.Clone() ?? new DocViewBinding();
                updatedBinding.FormulaExpression = editedFormula;
                if (updatedBinding.IsEmpty)
                {
                    ApplyViewBindingTargetUpdate(workspace, table, view, _viewBindingPopoverTargetKind, _viewBindingPopoverTargetItemId, null);
                    SyncViewBindingPopoverTargetViewId(workspace, table);
                }
                else
                {
                    ApplyViewBindingTargetUpdate(workspace, table, view, _viewBindingPopoverTargetKind, _viewBindingPopoverTargetItemId, updatedBinding);
                    SyncViewBindingPopoverTargetViewId(workspace, table);
                }

                _viewBindingPopoverFormulaSyncKey = editedFormula;
            }
        }
        Im.Context.PopId();

        float buttonY = formulaY + RowHeight + 6f;
        float buttonGap = 6f;
        float buttonWidth = (contentWidth - buttonGap * 2f) / 3f;
        if (Im.Button("Apply", contentX, buttonY, buttonWidth, style.MinButtonHeight))
        {
            ApplyViewBindingPopoverEdits(workspace, table, view, binding, variableOptionCount);
        }

        float unbindX = contentX + buttonWidth + buttonGap;
        if (Im.Button("Unbind", unbindX, buttonY, buttonWidth, style.MinButtonHeight))
        {
            ApplyViewBindingTargetUpdate(workspace, table, view, _viewBindingPopoverTargetKind, _viewBindingPopoverTargetItemId, null);
            SyncViewBindingPopoverTargetViewId(workspace, table);
            _viewBindingPopoverSelectedVariableIndex = 0;
            _viewBindingPopoverSelectionInitialized = true;
        }

        float closeX = unbindX + buttonWidth + buttonGap;
        if (Im.Button("Close", closeX, buttonY, buttonWidth, style.MinButtonHeight))
        {
            CloseViewBindingPopover();
            return;
        }

        if (canCloseOnOutside && input.KeyEscape && !dropdownOpen)
        {
            CloseViewBindingPopover();
        }
    }

    private static bool TryFindViewById(DocTable table, string viewId, out DocView view)
    {
        for (int viewIndex = 0; viewIndex < table.Views.Count; viewIndex++)
        {
            var candidate = table.Views[viewIndex];
            if (string.Equals(candidate.Id, viewId, StringComparison.Ordinal))
            {
                view = candidate;
                return true;
            }
        }

        view = null!;
        return false;
    }

    private static bool TryGetViewBindingTarget(
        DocView view,
        ViewBindingTargetKind targetKind,
        string targetItemId,
        out DocViewBinding? binding)
    {
        binding = null;
        switch (targetKind)
        {
            case ViewBindingTargetKind.GroupByColumn:
                binding = view.GroupByColumnBinding;
                return true;
            case ViewBindingTargetKind.CalendarDateColumn:
                binding = view.CalendarDateColumnBinding;
                return true;
            case ViewBindingTargetKind.ChartKind:
                binding = view.ChartKindBinding;
                return true;
            case ViewBindingTargetKind.ChartCategoryColumn:
                binding = view.ChartCategoryColumnBinding;
                return true;
            case ViewBindingTargetKind.ChartValueColumn:
                binding = view.ChartValueColumnBinding;
                return true;
            case ViewBindingTargetKind.SortColumn:
            case ViewBindingTargetKind.SortDescending:
                for (int sortIndex = 0; sortIndex < view.Sorts.Count; sortIndex++)
                {
                    var sort = view.Sorts[sortIndex];
                    if (!string.Equals(sort.Id, targetItemId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    binding = targetKind == ViewBindingTargetKind.SortColumn
                        ? sort.ColumnIdBinding
                        : sort.DescendingBinding;
                    return true;
                }

                return false;
            case ViewBindingTargetKind.FilterColumn:
            case ViewBindingTargetKind.FilterOperator:
            case ViewBindingTargetKind.FilterValue:
                for (int filterIndex = 0; filterIndex < view.Filters.Count; filterIndex++)
                {
                    var filter = view.Filters[filterIndex];
                    if (!string.Equals(filter.Id, targetItemId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    binding = targetKind switch
                    {
                        ViewBindingTargetKind.FilterColumn => filter.ColumnIdBinding,
                        ViewBindingTargetKind.FilterOperator => filter.OpBinding,
                        _ => filter.ValueBinding,
                    };
                    return true;
                }

                return false;
            default:
                return false;
        }
    }

    private static bool TrySetViewBindingTarget(
        DocView view,
        ViewBindingTargetKind targetKind,
        string targetItemId,
        DocViewBinding? binding)
    {
        switch (targetKind)
        {
            case ViewBindingTargetKind.GroupByColumn:
                view.GroupByColumnBinding = binding;
                return true;
            case ViewBindingTargetKind.CalendarDateColumn:
                view.CalendarDateColumnBinding = binding;
                return true;
            case ViewBindingTargetKind.ChartKind:
                view.ChartKindBinding = binding;
                return true;
            case ViewBindingTargetKind.ChartCategoryColumn:
                view.ChartCategoryColumnBinding = binding;
                return true;
            case ViewBindingTargetKind.ChartValueColumn:
                view.ChartValueColumnBinding = binding;
                return true;
            case ViewBindingTargetKind.SortColumn:
            case ViewBindingTargetKind.SortDescending:
                for (int sortIndex = 0; sortIndex < view.Sorts.Count; sortIndex++)
                {
                    var sort = view.Sorts[sortIndex];
                    if (!string.Equals(sort.Id, targetItemId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (targetKind == ViewBindingTargetKind.SortColumn)
                    {
                        sort.ColumnIdBinding = binding;
                    }
                    else
                    {
                        sort.DescendingBinding = binding;
                    }

                    return true;
                }

                return false;
            case ViewBindingTargetKind.FilterColumn:
            case ViewBindingTargetKind.FilterOperator:
            case ViewBindingTargetKind.FilterValue:
                for (int filterIndex = 0; filterIndex < view.Filters.Count; filterIndex++)
                {
                    var filter = view.Filters[filterIndex];
                    if (!string.Equals(filter.Id, targetItemId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (targetKind == ViewBindingTargetKind.FilterColumn)
                    {
                        filter.ColumnIdBinding = binding;
                    }
                    else if (targetKind == ViewBindingTargetKind.FilterOperator)
                    {
                        filter.OpBinding = binding;
                    }
                    else
                    {
                        filter.ValueBinding = binding;
                    }

                    return true;
                }

                return false;
            default:
                return false;
        }
    }

    private static void ApplyViewBindingTargetUpdate(
        DocWorkspace workspace,
        DocTable table,
        DocView view,
        ViewBindingTargetKind targetKind,
        string targetItemId,
        DocViewBinding? binding)
    {
        DocViewBinding? normalizedBinding = binding;
        if (normalizedBinding != null && normalizedBinding.IsEmpty)
        {
            normalizedBinding = null;
        }

        if (!TryGetViewBindingTarget(view, targetKind, targetItemId, out DocViewBinding? currentBinding))
        {
            return;
        }

        if (AreBindingsEqual(currentBinding, normalizedBinding))
        {
            return;
        }

        var old = view.Clone();
        if (!TrySetViewBindingTarget(view, targetKind, targetItemId, normalizedBinding))
        {
            return;
        }

        ApplyViewConfigChange(workspace, table, view, old);
    }

    private static readonly string[] BuiltInDisplayTypeNames = { "Grid", "Board", "Calendar", "Chart" };
    private static readonly string[] ChartKindNames = { "Bar", "Line", "Pie", "Area" };

    /// <summary>Finds the block for InspectedBlockId (if set).</summary>
    private static DocBlock? FindInspectedBlock(DocWorkspace workspace)
    {
        if (string.IsNullOrEmpty(workspace.InspectedBlockId) || workspace.ActiveDocument == null)
            return null;
        var blocks = workspace.ActiveDocument.Blocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            if (string.Equals(blocks[i].Id, workspace.InspectedBlockId, StringComparison.Ordinal))
                return blocks[i];
        }
        return null;
    }

    /// <summary>Resolves the view for the currently inspected context (per-block or active table view).</summary>
    private static DocView? ResolveInspectedView(DocWorkspace workspace, DocTable table)
    {
        var block = FindInspectedBlock(workspace);
        if (block != null && !string.IsNullOrEmpty(block.ViewId))
        {
            for (int i = 0; i < table.Views.Count; i++)
            {
                if (string.Equals(table.Views[i].Id, block.ViewId, StringComparison.Ordinal))
                    return table.Views[i];
            }
        }
        if (workspace.ActiveTableView != null)
        {
            for (int i = 0; i < table.Views.Count; i++)
            {
                if (string.Equals(table.Views[i].Id, workspace.ActiveTableView.Id, StringComparison.Ordinal))
                {
                    return table.Views[i];
                }
            }
        }
        if (workspace.InspectedTable == table && table.Views.Count > 0)
            return table.Views[0];
        return null;
    }

    /// <summary>Ensures the inspected block has its own view. Creates one if needed and returns it.</summary>
    private static DocView? EnsurePerBlockView(DocWorkspace workspace, DocTable table)
    {
        var block = FindInspectedBlock(workspace);
        if (block == null) return null; // Not an embedded block context

        // Already has a per-block view?
        if (!string.IsNullOrEmpty(block.ViewId))
        {
            for (int i = 0; i < table.Views.Count; i++)
            {
                if (string.Equals(table.Views[i].Id, block.ViewId, StringComparison.Ordinal))
                    return table.Views[i];
            }
        }

        // Clone the table's first view (or create a new Grid view) as a per-block view
        var source = table.Views.Count > 0 ? table.Views[0] : null;
        var newView = source != null ? source.Clone() : new DocView();
        newView.Id = Guid.NewGuid().ToString();
        newView.Name = (source?.Name ?? "Grid view") + " (instance)";

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.AddView,
            TableId = table.Id,
            ViewIndex = table.Views.Count,
            ViewSnapshot = newView,
        });

        if (table.Views.Count > 0)
        {
            block.ViewId = table.Views[^1].Id;
            return table.Views[^1];
        }
        return null;
    }

    private static float DrawDisplayTypeSection(
        DocWorkspace workspace,
        DocTable table,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        y = DrawCollapsibleHeader("Display", ref _displayTypeExpanded, contentRect, y, style);
        if (!_displayTypeExpanded) return y;
        y += SectionHeaderContentSpacing;

        float rowX = contentRect.X + SectionHorizontalInset + RowHorizontalInset;
        float rowW = contentRect.Width - (SectionHorizontalInset + RowHorizontalInset) * 2f;

        DocBlock? inspectedBlock = FindInspectedBlock(workspace);
        DocView? activeView = ResolveInspectedView(workspace, table);

        if (activeView == null)
        {
            // No view yet — show label and offer to create one
            float textY = y + (RowHeight - style.FontSize) * 0.5f;
            Im.Text("No views. Create one:".AsSpan(), rowX, textY, style.FontSize - 1f, style.TextSecondary);
            y += RowHeight;

            Im.Context.PushId("display_type_create");
            float btnW = MathF.Max(60f, rowW * 0.3f);
            for (int i = 0; i < BuiltInDisplayTypeNames.Length; i++)
            {
                float btnX = rowX + i * (btnW + 4f);
                if (Im.Button(BuiltInDisplayTypeNames[i], btnX, y, btnW, RowHeight))
                {
                    var newView = new DocView
                    {
                        Type = (DocViewType)i,
                        Name = BuiltInDisplayTypeNames[i] + " view",
                    };
                    workspace.ExecuteCommand(new DocCommand
                    {
                        Kind = DocCommandKind.AddView,
                        TableId = table.Id,
                        ViewIndex = table.Views.Count,
                        ViewSnapshot = newView,
                    });
                    // Set as active and assign to block if applicable
                    if (table.Views.Count > 0)
                    {
                        var created = table.Views[^1];
                        if (inspectedBlock != null)
                            inspectedBlock.ViewId = created.Id;
                        else if (workspace.ActiveTableView == null)
                            workspace.ActiveTableView = created;
                    }
                }
            }
            Im.Context.PopId();
            y += RowHeight;
        }
        else
        {
            // Show display type dropdown
            float labelW = Im.MeasureTextWidth("Type".AsSpan(), style.FontSize) + 8f;
            float textY = y + (RowHeight - style.FontSize) * 0.5f;
            Im.Text("Type".AsSpan(), rowX, textY, style.FontSize, style.TextSecondary);

            int displayTypeOptionCount = BuildDisplayTypeOptions(activeView);
            _displayTypeIndex = FindDisplayTypeOptionIndex(activeView, displayTypeOptionCount);
            float ddX = rowX + labelW;
            float ddW = MathF.Max(80f, rowW - labelW);
            if (Im.Dropdown("display_type_dd", _displayTypeOptionNames.AsSpan(0, displayTypeOptionCount), ref _displayTypeIndex, ddX, y, ddW, InspectorDropdownFlags))
            {
                DocViewType newType = _displayTypeOptionTypes[_displayTypeIndex];
                string? newCustomRendererId = _displayTypeOptionRendererIds[_displayTypeIndex];
                if (newType != DocViewType.Custom || string.IsNullOrWhiteSpace(newCustomRendererId))
                {
                    newCustomRendererId = null;
                }

                bool typeChanged = newType != activeView.Type;
                bool customRendererChanged = !string.Equals(activeView.CustomRendererId, newCustomRendererId, StringComparison.Ordinal);
                if (typeChanged || customRendererChanged)
                {
                    if (inspectedBlock != null && string.IsNullOrEmpty(inspectedBlock.ViewId))
                    {
                        // Create a new per-block view instead of modifying the shared view
                        var newView = new DocView
                        {
                            Type = newType,
                            CustomRendererId = newCustomRendererId,
                            Name = _displayTypeOptionNames[_displayTypeIndex] + " view",
                        };
                        workspace.ExecuteCommand(new DocCommand
                        {
                            Kind = DocCommandKind.AddView,
                            TableId = table.Id,
                            ViewIndex = table.Views.Count,
                            ViewSnapshot = newView,
                        });
                        if (table.Views.Count > 0)
                            inspectedBlock.ViewId = table.Views[^1].Id;
                    }
                    else
                    {
                        var old = activeView.Clone();
                        activeView.Type = newType;
                        activeView.CustomRendererId = newCustomRendererId;
                        ApplyViewConfigChange(workspace, table, activeView, old);
                    }
                }
            }
            y += RowHeight;
        }

        return y;
    }

    private static void EnsureDisplayTypeOptionCapacity(int needed)
    {
        if (_displayTypeOptionNames.Length < needed)
        {
            int newLength = Math.Max(needed, _displayTypeOptionNames.Length * 2);
            Array.Resize(ref _displayTypeOptionNames, newLength);
            Array.Resize(ref _displayTypeOptionTypes, newLength);
            Array.Resize(ref _displayTypeOptionRendererIds, newLength);
        }
    }

    private static int BuildDisplayTypeOptions(DocView? activeView)
    {
        TableViewRendererRegistry.CopyRenderers(_tableViewRenderersScratch);
        int optionCount = BuiltInDisplayTypeNames.Length + _tableViewRenderersScratch.Count + 1;
        EnsureDisplayTypeOptionCapacity(optionCount);

        int optionIndex = 0;
        _displayTypeOptionNames[optionIndex] = BuiltInDisplayTypeNames[0];
        _displayTypeOptionTypes[optionIndex] = DocViewType.Grid;
        _displayTypeOptionRendererIds[optionIndex] = "";
        optionIndex++;

        _displayTypeOptionNames[optionIndex] = BuiltInDisplayTypeNames[1];
        _displayTypeOptionTypes[optionIndex] = DocViewType.Board;
        _displayTypeOptionRendererIds[optionIndex] = "";
        optionIndex++;

        _displayTypeOptionNames[optionIndex] = BuiltInDisplayTypeNames[2];
        _displayTypeOptionTypes[optionIndex] = DocViewType.Calendar;
        _displayTypeOptionRendererIds[optionIndex] = "";
        optionIndex++;

        _displayTypeOptionNames[optionIndex] = BuiltInDisplayTypeNames[3];
        _displayTypeOptionTypes[optionIndex] = DocViewType.Chart;
        _displayTypeOptionRendererIds[optionIndex] = "";
        optionIndex++;

        for (int rendererIndex = 0; rendererIndex < _tableViewRenderersScratch.Count; rendererIndex++)
        {
            var renderer = _tableViewRenderersScratch[rendererIndex];
            _displayTypeOptionNames[optionIndex] = renderer.DisplayName;
            _displayTypeOptionTypes[optionIndex] = DocViewType.Custom;
            _displayTypeOptionRendererIds[optionIndex] = renderer.RendererId;
            optionIndex++;
        }

        if (activeView != null &&
            activeView.Type == DocViewType.Custom &&
            !string.IsNullOrWhiteSpace(activeView.CustomRendererId))
        {
            bool hasCurrentCustomRenderer = false;
            for (int existingIndex = 0; existingIndex < optionIndex; existingIndex++)
            {
                if (_displayTypeOptionTypes[existingIndex] != DocViewType.Custom)
                {
                    continue;
                }

                if (string.Equals(
                        _displayTypeOptionRendererIds[existingIndex],
                        activeView.CustomRendererId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    hasCurrentCustomRenderer = true;
                    break;
                }
            }

            if (!hasCurrentCustomRenderer)
            {
                _displayTypeOptionNames[optionIndex] = "Custom (" + activeView.CustomRendererId + ")";
                _displayTypeOptionTypes[optionIndex] = DocViewType.Custom;
                _displayTypeOptionRendererIds[optionIndex] = activeView.CustomRendererId;
                optionIndex++;
            }
        }

        return optionIndex;
    }

    private static int FindDisplayTypeOptionIndex(DocView view, int optionCount)
    {
        for (int optionIndex = 0; optionIndex < optionCount; optionIndex++)
        {
            if (_displayTypeOptionTypes[optionIndex] != view.Type)
            {
                continue;
            }

            if (view.Type != DocViewType.Custom)
            {
                return optionIndex;
            }

            if (string.Equals(
                    _displayTypeOptionRendererIds[optionIndex],
                    view.CustomRendererId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return optionIndex;
            }
        }

        return 0;
    }

    private static float DrawViewFiltersSection(
        DocWorkspace workspace,
        DocTable table,
        DocView view,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        DocBlock? tableInstanceBlock = FindInspectedBlock(workspace);
        y = DrawCollapsibleHeader("Filters", ref _viewFiltersExpanded, contentRect, y, style, view.Filters.Count);
        if (!_viewFiltersExpanded) return y;
        y += SectionHeaderContentSpacing;

        float rowX = contentRect.X + SectionHorizontalInset + RowHorizontalInset;
        float rowW = contentRect.Width - (SectionHorizontalInset + RowHorizontalInset) * 2f;

        // Build column options
        int colCount = table.Columns.Count + 1;
        EnsureScratchCapacity(colCount);
        _scratchOptionNames[0] = "(column)";
        _scratchOptionIds[0] = "";
        int optCount = 1;
        for (int ci = 0; ci < table.Columns.Count; ci++)
        {
            _scratchOptionNames[optCount] = table.Columns[ci].Name;
            _scratchOptionIds[optCount] = table.Columns[ci].Id;
            optCount++;
        }

        Im.Context.PushId("view_filters");

        // Draw existing filter rules
        for (int fi = 0; fi < view.Filters.Count; fi++)
        {
            var filter = view.Filters[fi];
            Im.Context.PushId(fi);

            // Column dropdown
            int selectedCol = 0;
            for (int c = 1; c < optCount; c++)
            {
                if (string.Equals(_scratchOptionIds[c], filter.ColumnId, StringComparison.Ordinal))
                {
                    selectedCol = c;
                    break;
                }
            }

            float colDdW = MathF.Max(60f, rowW * 0.3f);
            var columnRect = new ImRect(rowX, y, colDdW, RowHeight);
            bool hasColumnBinding = filter.ColumnIdBinding != null && !filter.ColumnIdBinding.IsEmpty;
            QueueViewBindingContextMenu(table, view, ViewBindingTargetKind.FilterColumn, filter.Id, "Filter Column", columnRect, hasColumnBinding);
            if (hasColumnBinding)
            {
                string boundColumnId = filter.ColumnId;
                bool boundColumnResolved = workspace.TryResolveViewBindingToString(table, filter.ColumnIdBinding, out string? resolvedColumnId, tableInstanceBlock) &&
                                           !string.IsNullOrWhiteSpace(resolvedColumnId);
                if (boundColumnResolved)
                {
                    boundColumnId = resolvedColumnId!;
                }

                int boundColumnIndex = 0;
                for (int columnIndex = 1; columnIndex < optCount; columnIndex++)
                {
                    if (string.Equals(_scratchOptionIds[columnIndex], boundColumnId, StringComparison.Ordinal))
                    {
                        boundColumnIndex = columnIndex;
                        break;
                    }
                }

                string boundColumnText = boundColumnIndex > 0
                    ? _scratchOptionNames[boundColumnIndex]
                    : string.IsNullOrWhiteSpace(boundColumnId) ? "(column)" : boundColumnId;
                DrawBoundValueField(boundColumnText, rowX, y, colDdW, style, boundColumnResolved || string.IsNullOrWhiteSpace(boundColumnId));
            }
            else if (Im.Dropdown("filter_col", _scratchOptionNames.AsSpan(0, optCount), ref selectedCol, rowX, y, colDdW, InspectorDropdownFlags))
            {
                var old = view.Clone();
                filter.ColumnId = selectedCol > 0 ? _scratchOptionIds[selectedCol] : "";
                ApplyViewConfigChange(workspace, table, view, old);
            }
            // Operator dropdown
            int opIdx = (int)filter.Op;
            float opDdW = MathF.Max(60f, rowW * 0.25f);
            float opX = rowX + colDdW + 4f;
            var opRect = new ImRect(opX, y, opDdW, RowHeight);
            bool hasOperatorBinding = filter.OpBinding != null && !filter.OpBinding.IsEmpty;
            QueueViewBindingContextMenu(table, view, ViewBindingTargetKind.FilterOperator, filter.Id, "Filter Operator", opRect, hasOperatorBinding);
            DocViewFilterOp effectiveOperator = filter.Op;
            bool operatorResolved = false;
            if (hasOperatorBinding && workspace.TryResolveViewBindingToFilterOp(table, filter.OpBinding, out DocViewFilterOp boundOperator, tableInstanceBlock))
            {
                effectiveOperator = boundOperator;
                operatorResolved = true;
            }

            if (hasOperatorBinding)
            {
                int boundOperatorIndex = (int)effectiveOperator;
                if (boundOperatorIndex < 0 || boundOperatorIndex >= FilterOpNames.Length)
                {
                    boundOperatorIndex = 0;
                }

                DrawBoundValueField(FilterOpNames[boundOperatorIndex], opX, y, opDdW, style, operatorResolved);
            }
            else if (Im.Dropdown("filter_op", FilterOpNames.AsSpan(), ref opIdx, opX, y, opDdW, InspectorDropdownFlags))
            {
                var old = view.Clone();
                filter.Op = (DocViewFilterOp)opIdx;
                ApplyViewConfigChange(workspace, table, view, old);
            }
            // Value input (only for ops that need a value)
            bool needsValue = effectiveOperator != DocViewFilterOp.IsEmpty && effectiveOperator != DocViewFilterOp.IsNotEmpty;
            float removeW = 22f;
            float valX = opX + opDdW + 4f;
            float valW = MathF.Max(30f, rowX + rowW - valX - removeW - 4f);
            var valueRect = new ImRect(valX, y, valW, RowHeight);
            bool hasValueBinding = filter.ValueBinding != null && !filter.ValueBinding.IsEmpty;
            QueueViewBindingContextMenu(table, view, ViewBindingTargetKind.FilterValue, filter.Id, "Filter Value", valueRect, hasValueBinding);
            if (needsValue && hasValueBinding)
            {
                bool resolvedValue = workspace.TryResolveViewBindingToString(table, filter.ValueBinding, out string? boundValue, tableInstanceBlock);
                string displayValue = resolvedValue ? (boundValue ?? "") : (filter.Value ?? "");
                if (string.IsNullOrWhiteSpace(displayValue))
                {
                    displayValue = "(empty)";
                }

                DrawBoundValueField(displayValue, valX, y, valW, style, resolvedValue);
            }
            else if (needsValue && fi < MaxFilterValueBuffers)
            {
                // Initialize buffer if needed
                _filterValueBuffers[fi] ??= new char[128];
                string currentVal = filter.Value ?? "";
                string syncKey = _filterValueSyncKeys[fi] ?? "";

                // Sync buffer from model when the filter value changes externally
                if (!_filterValueFocused[fi] &&
                    !string.Equals(syncKey, currentVal, StringComparison.Ordinal))
                {
                    int len = Math.Min(currentVal.Length, _filterValueBuffers[fi].Length);
                    currentVal.AsSpan(0, len).CopyTo(_filterValueBuffers[fi]);
                    _filterValueLengths[fi] = len;
                    _filterValueSyncKeys[fi] = currentVal;
                }

                Im.TextInput("filter_val", _filterValueBuffers[fi], ref _filterValueLengths[fi], _filterValueBuffers[fi].Length, valX, y, valW);

                if (ShouldCommitTextInput("filter_val", ref _filterValueFocused[fi]))
                {
                    string newVal = new string(_filterValueBuffers[fi], 0, _filterValueLengths[fi]);
                    if (!string.Equals(newVal, currentVal, StringComparison.Ordinal))
                    {
                        var old = view.Clone();
                        filter.Value = newVal;
                        _filterValueSyncKeys[fi] = newVal;
                        ApplyViewConfigChange(workspace, table, view, old);
                    }
                }
            }
            else
            {
                DrawBoundValueField("(no value)", valX, y, valW, style);
            }
            // Remove button
            float removeBtnX = rowX + rowW - removeW;
            float removeBtnY = y + (RowHeight - removeW) * 0.5f;
            if (Im.Button(MinusIcon, removeBtnX, removeBtnY, removeW, removeW))
            {
                var old = view.Clone();
                view.Filters.RemoveAt(fi);
                ApplyViewConfigChange(workspace, table, view, old);
                Im.Context.PopId();
                y += RowHeight;
                break;
            }

            Im.Context.PopId();
            y += RowHeight;
        }

        // Add filter button
        float addBtnW = 22f;
        float addBtnY = y + (RowHeight - addBtnW) * 0.5f;
        if (Im.Button(PlusIcon, rowX, addBtnY, addBtnW, addBtnW))
        {
            var old = view.Clone();
            view.Filters.Add(new DocViewFilter
            {
                ColumnId = table.Columns.Count > 0 ? table.Columns[0].Id : "",
                Op = DocViewFilterOp.Contains,
                Value = "",
            });
            ApplyViewConfigChange(workspace, table, view, old);
        }
        float addTextY = y + (RowHeight - style.FontSize) * 0.5f;
        Im.Text("Add filter".AsSpan(), rowX + addBtnW + 6f, addTextY, style.FontSize - 1f, style.TextSecondary);
        y += RowHeight;

        Im.Context.PopId();
        return y;
    }

    private static float DrawViewSortsSection(
        DocWorkspace workspace,
        DocTable table,
        DocView view,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        DocBlock? tableInstanceBlock = FindInspectedBlock(workspace);
        y = DrawCollapsibleHeader("Sorts", ref _viewSortsExpanded, contentRect, y, style, view.Sorts.Count);
        if (!_viewSortsExpanded) return y;
        y += SectionHeaderContentSpacing;

        float rowX = contentRect.X + SectionHorizontalInset + RowHorizontalInset;
        float rowW = contentRect.Width - (SectionHorizontalInset + RowHorizontalInset) * 2f;

        // Build column options
        int colCount = table.Columns.Count + 1;
        EnsureScratchCapacity(colCount);
        _scratchOptionNames[0] = "(column)";
        _scratchOptionIds[0] = "";
        int optCount = 1;
        for (int ci = 0; ci < table.Columns.Count; ci++)
        {
            _scratchOptionNames[optCount] = table.Columns[ci].Name;
            _scratchOptionIds[optCount] = table.Columns[ci].Id;
            optCount++;
        }

        Im.Context.PushId("view_sorts");

        for (int si = 0; si < view.Sorts.Count; si++)
        {
            var sort = view.Sorts[si];
            Im.Context.PushId(si);

            // Column dropdown
            int selectedCol = 0;
            for (int c = 1; c < optCount; c++)
            {
                if (string.Equals(_scratchOptionIds[c], sort.ColumnId, StringComparison.Ordinal))
                {
                    selectedCol = c;
                    break;
                }
            }

            float colDdW = MathF.Max(60f, rowW * 0.45f);
            var columnRect = new ImRect(rowX, y, colDdW, RowHeight);
            bool hasColumnBinding = sort.ColumnIdBinding != null && !sort.ColumnIdBinding.IsEmpty;
            QueueViewBindingContextMenu(table, view, ViewBindingTargetKind.SortColumn, sort.Id, "Sort Column", columnRect, hasColumnBinding);
            if (hasColumnBinding)
            {
                string boundColumnId = sort.ColumnId;
                bool boundColumnResolved = workspace.TryResolveViewBindingToString(table, sort.ColumnIdBinding, out string? resolvedColumnId, tableInstanceBlock) &&
                                           !string.IsNullOrWhiteSpace(resolvedColumnId);
                if (boundColumnResolved)
                {
                    boundColumnId = resolvedColumnId!;
                }

                int boundColumnIndex = 0;
                for (int columnIndex = 1; columnIndex < optCount; columnIndex++)
                {
                    if (string.Equals(_scratchOptionIds[columnIndex], boundColumnId, StringComparison.Ordinal))
                    {
                        boundColumnIndex = columnIndex;
                        break;
                    }
                }

                string boundColumnText = boundColumnIndex > 0
                    ? _scratchOptionNames[boundColumnIndex]
                    : string.IsNullOrWhiteSpace(boundColumnId) ? "(column)" : boundColumnId;
                DrawBoundValueField(boundColumnText, rowX, y, colDdW, style, boundColumnResolved || string.IsNullOrWhiteSpace(boundColumnId));
            }
            else if (Im.Dropdown("sort_col", _scratchOptionNames.AsSpan(0, optCount), ref selectedCol, rowX, y, colDdW, InspectorDropdownFlags))
            {
                var old = view.Clone();
                sort.ColumnId = selectedCol > 0 ? _scratchOptionIds[selectedCol] : "";
                ApplyViewConfigChange(workspace, table, view, old);
            }
            // Direction dropdown
            int dirIdx = sort.Descending ? 1 : 0;
            float dirDdW = MathF.Max(60f, rowW * 0.3f);
            float dirX = rowX + colDdW + 4f;
            var directionRect = new ImRect(dirX, y, dirDdW, RowHeight);
            bool hasDirectionBinding = sort.DescendingBinding != null && !sort.DescendingBinding.IsEmpty;
            QueueViewBindingContextMenu(table, view, ViewBindingTargetKind.SortDescending, sort.Id, "Sort Descending", directionRect, hasDirectionBinding);
            if (hasDirectionBinding)
            {
                bool resolvedDirection = workspace.TryResolveViewBindingToBool(table, sort.DescendingBinding, out bool boundDescending, tableInstanceBlock);
                string directionText = resolvedDirection
                    ? (boundDescending ? SortDirectionNames[1] : SortDirectionNames[0])
                    : (sort.Descending ? SortDirectionNames[1] : SortDirectionNames[0]);
                DrawBoundValueField(directionText, dirX, y, dirDdW, style, resolvedDirection);
            }
            else if (Im.Dropdown("sort_dir", SortDirectionNames.AsSpan(), ref dirIdx, dirX, y, dirDdW, InspectorDropdownFlags))
            {
                var old = view.Clone();
                sort.Descending = dirIdx == 1;
                ApplyViewConfigChange(workspace, table, view, old);
            }
            // Remove button
            float removeW = 22f;
            float removeBtnX = rowX + rowW - removeW;
            float removeBtnY = y + (RowHeight - removeW) * 0.5f;
            if (Im.Button(MinusIcon, removeBtnX, removeBtnY, removeW, removeW))
            {
                var old = view.Clone();
                view.Sorts.RemoveAt(si);
                ApplyViewConfigChange(workspace, table, view, old);
                Im.Context.PopId();
                y += RowHeight;
                break;
            }

            Im.Context.PopId();
            y += RowHeight;
        }

        // Add sort button
        float addBtnW = 22f;
        float addBtnY = y + (RowHeight - addBtnW) * 0.5f;
        if (Im.Button(PlusIcon, rowX, addBtnY, addBtnW, addBtnW))
        {
            var old = view.Clone();
            view.Sorts.Add(new DocViewSort
            {
                ColumnId = table.Columns.Count > 0 ? table.Columns[0].Id : "",
                Descending = false,
            });
            ApplyViewConfigChange(workspace, table, view, old);
        }
        float addTextY = y + (RowHeight - style.FontSize) * 0.5f;
        Im.Text("Add sort".AsSpan(), rowX + addBtnW + 6f, addTextY, style.FontSize - 1f, style.TextSecondary);
        y += RowHeight;

        Im.Context.PopId();
        return y;
    }

    private static float DrawViewGroupSection(
        DocWorkspace workspace,
        DocTable table,
        DocView view,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        DocBlock? tableInstanceBlock = FindInspectedBlock(workspace);
        y = DrawCollapsibleHeader("View Config", ref _viewGroupByExpanded, contentRect, y, style);
        if (!_viewGroupByExpanded) return y;
        y += SectionHeaderContentSpacing;

        float rowX = contentRect.X + SectionHorizontalInset + RowHorizontalInset;
        float rowW = contentRect.Width - (SectionHorizontalInset + RowHorizontalInset) * 2f;

        // Group By (for Board views)
        if (view.Type == DocViewType.Board)
        {
            // Build select column options
            int selectColCount = 1;
            EnsureScratchCapacity(table.Columns.Count + 1);
            _scratchOptionNames[0] = "(none)";
            _scratchOptionIds[0] = "";
            int selectedGroupIdx = 0;
            for (int ci = 0; ci < table.Columns.Count; ci++)
            {
                if (table.Columns[ci].Kind == DocColumnKind.Select)
                {
                    _scratchOptionNames[selectColCount] = table.Columns[ci].Name;
                    _scratchOptionIds[selectColCount] = table.Columns[ci].Id;
                    if (string.Equals(table.Columns[ci].Id, view.GroupByColumnId, StringComparison.Ordinal))
                        selectedGroupIdx = selectColCount;
                    selectColCount++;
                }
            }

            float textY = y + (RowHeight - style.FontSize) * 0.5f;
            Im.Text("Group By:".AsSpan(), rowX, textY, style.FontSize, style.TextSecondary);

            float ddX = rowX + 80f;
            float ddW = rowW - 80f;
            var groupByRect = new ImRect(ddX, y, ddW, RowHeight);
            bool hasGroupByBinding = view.GroupByColumnBinding != null && !view.GroupByColumnBinding.IsEmpty;
            QueueViewBindingContextMenu(table, view, ViewBindingTargetKind.GroupByColumn, "", "Group By Column", groupByRect, hasGroupByBinding);
            if (hasGroupByBinding)
            {
                string boundGroupById = view.GroupByColumnId ?? "";
                bool boundGroupByResolved = workspace.TryResolveViewBindingToString(table, view.GroupByColumnBinding, out string? resolvedGroupById, tableInstanceBlock) &&
                                            !string.IsNullOrWhiteSpace(resolvedGroupById);
                if (boundGroupByResolved)
                {
                    boundGroupById = resolvedGroupById!;
                }

                int boundGroupByIndex = 0;
                for (int groupOptionIndex = 1; groupOptionIndex < selectColCount; groupOptionIndex++)
                {
                    if (string.Equals(_scratchOptionIds[groupOptionIndex], boundGroupById, StringComparison.Ordinal))
                    {
                        boundGroupByIndex = groupOptionIndex;
                        break;
                    }
                }

                string groupByText = boundGroupByIndex > 0
                    ? _scratchOptionNames[boundGroupByIndex]
                    : string.IsNullOrWhiteSpace(boundGroupById) ? "(none)" : boundGroupById;
                DrawBoundValueField(groupByText, ddX, y, ddW, style, boundGroupByResolved || string.IsNullOrWhiteSpace(boundGroupById));
            }
            else
            {
                _viewGroupByColIndex = selectedGroupIdx;
                if (Im.Dropdown("view_group_by_dd", _scratchOptionNames.AsSpan(0, selectColCount), ref _viewGroupByColIndex, ddX, y, ddW, InspectorDropdownFlags))
                {
                    var old = view.Clone();
                    view.GroupByColumnId = _viewGroupByColIndex > 0 ? _scratchOptionIds[_viewGroupByColIndex] : null;
                    ApplyViewConfigChange(workspace, table, view, old);
                }
            }
            y += RowHeight + DropdownBottomSpacing;
        }

        // Calendar Date Column
        if (view.Type == DocViewType.Calendar)
        {
            int dateColCount = 1;
            EnsureScratchCapacity(table.Columns.Count + 1);
            _scratchOptionNames[0] = "(none)";
            _scratchOptionIds[0] = "";
            int selectedDateIdx = 0;
            for (int ci = 0; ci < table.Columns.Count; ci++)
            {
                if (table.Columns[ci].Kind == DocColumnKind.Text)
                {
                    _scratchOptionNames[dateColCount] = table.Columns[ci].Name;
                    _scratchOptionIds[dateColCount] = table.Columns[ci].Id;
                    if (string.Equals(table.Columns[ci].Id, view.CalendarDateColumnId, StringComparison.Ordinal))
                        selectedDateIdx = dateColCount;
                    dateColCount++;
                }
            }

            float textY = y + (RowHeight - style.FontSize) * 0.5f;
            Im.Text("Date Col:".AsSpan(), rowX, textY, style.FontSize, style.TextSecondary);

            float ddX = rowX + 80f;
            float ddW = rowW - 80f;
            var calendarDateRect = new ImRect(ddX, y, ddW, RowHeight);
            bool hasCalendarBinding = view.CalendarDateColumnBinding != null && !view.CalendarDateColumnBinding.IsEmpty;
            QueueViewBindingContextMenu(table, view, ViewBindingTargetKind.CalendarDateColumn, "", "Calendar Date Column", calendarDateRect, hasCalendarBinding);
            if (hasCalendarBinding)
            {
                string boundDateColumnId = view.CalendarDateColumnId ?? "";
                bool boundDateResolved = workspace.TryResolveViewBindingToString(table, view.CalendarDateColumnBinding, out string? resolvedDateColumnId, tableInstanceBlock) &&
                                        !string.IsNullOrWhiteSpace(resolvedDateColumnId);
                if (boundDateResolved)
                {
                    boundDateColumnId = resolvedDateColumnId!;
                }

                int boundDateColumnIndex = 0;
                for (int dateOptionIndex = 1; dateOptionIndex < dateColCount; dateOptionIndex++)
                {
                    if (string.Equals(_scratchOptionIds[dateOptionIndex], boundDateColumnId, StringComparison.Ordinal))
                    {
                        boundDateColumnIndex = dateOptionIndex;
                        break;
                    }
                }

                string dateColumnText = boundDateColumnIndex > 0
                    ? _scratchOptionNames[boundDateColumnIndex]
                    : string.IsNullOrWhiteSpace(boundDateColumnId) ? "(none)" : boundDateColumnId;
                DrawBoundValueField(dateColumnText, ddX, y, ddW, style, boundDateResolved || string.IsNullOrWhiteSpace(boundDateColumnId));
            }
            else
            {
                _viewCalendarDateColIndex = selectedDateIdx;
                if (Im.Dropdown("view_calendar_date_dd", _scratchOptionNames.AsSpan(0, dateColCount), ref _viewCalendarDateColIndex, ddX, y, ddW, InspectorDropdownFlags))
                {
                    var old = view.Clone();
                    view.CalendarDateColumnId = _viewCalendarDateColIndex > 0 ? _scratchOptionIds[_viewCalendarDateColIndex] : null;
                    ApplyViewConfigChange(workspace, table, view, old);
                }
            }
            y += RowHeight + DropdownBottomSpacing;
        }

        // Chart Config
        if (view.Type == DocViewType.Chart)
        {
            // Chart Kind dropdown (Bar/Line/Pie)
            float textY2 = y + (RowHeight - style.FontSize) * 0.5f;
            Im.Text("Chart:".AsSpan(), rowX, textY2, style.FontSize, style.TextSecondary);

            float ddX = rowX + 80f;
            float ddW = rowW - 80f;
            var chartKindRect = new ImRect(ddX, y, ddW, RowHeight);
            bool hasChartKindBinding = view.ChartKindBinding != null && !view.ChartKindBinding.IsEmpty;
            QueueViewBindingContextMenu(table, view, ViewBindingTargetKind.ChartKind, "", "Chart Kind", chartKindRect, hasChartKindBinding);
            if (hasChartKindBinding)
            {
                DocChartKind boundChartKind = view.ChartKind ?? DocChartKind.Bar;
                bool boundChartKindResolved = workspace.TryResolveViewBindingToChartKind(table, view.ChartKindBinding, out DocChartKind? resolvedChartKind, tableInstanceBlock);
                if (boundChartKindResolved && resolvedChartKind.HasValue)
                {
                    boundChartKind = resolvedChartKind.Value;
                }

                int chartKindIndex = (int)boundChartKind;
                if (chartKindIndex < 0 || chartKindIndex >= ChartKindNames.Length)
                {
                    chartKindIndex = 0;
                }

                DrawBoundValueField(ChartKindNames[chartKindIndex], ddX, y, ddW, style, boundChartKindResolved);
            }
            else
            {
                _viewChartKindIndex = (int)(view.ChartKind ?? DocChartKind.Bar);
                if (Im.Dropdown("view_chart_kind_dd", ChartKindNames.AsSpan(), ref _viewChartKindIndex, ddX, y, ddW, InspectorDropdownFlags))
                {
                    var old = view.Clone();
                    view.ChartKind = (DocChartKind)_viewChartKindIndex;
                    ApplyViewConfigChange(workspace, table, view, old);
                }
            }
            y += RowHeight + DropdownBottomSpacing;

            // Category Column (any text/select column)
            int catColCount = 1;
            EnsureScratchCapacity(table.Columns.Count + 1);
            _scratchOptionNames[0] = "(none)";
            _scratchOptionIds[0] = "";
            int selectedCatIdx = 0;
            for (int ci = 0; ci < table.Columns.Count; ci++)
            {
                var colKind = table.Columns[ci].Kind;
                if (colKind == DocColumnKind.Text || colKind == DocColumnKind.Select)
                {
                    _scratchOptionNames[catColCount] = table.Columns[ci].Name;
                    _scratchOptionIds[catColCount] = table.Columns[ci].Id;
                    if (string.Equals(table.Columns[ci].Id, view.ChartCategoryColumnId, StringComparison.Ordinal))
                        selectedCatIdx = catColCount;
                    catColCount++;
                }
            }

            float catTextY = y + (RowHeight - style.FontSize) * 0.5f;
            Im.Text("Category:".AsSpan(), rowX, catTextY, style.FontSize, style.TextSecondary);
            var chartCategoryRect = new ImRect(ddX, y, ddW, RowHeight);
            bool hasChartCategoryBinding = view.ChartCategoryColumnBinding != null && !view.ChartCategoryColumnBinding.IsEmpty;
            QueueViewBindingContextMenu(table, view, ViewBindingTargetKind.ChartCategoryColumn, "", "Chart Category Column", chartCategoryRect, hasChartCategoryBinding);
            if (hasChartCategoryBinding)
            {
                string boundCategoryId = view.ChartCategoryColumnId ?? "";
                bool boundCategoryResolved = workspace.TryResolveViewBindingToString(table, view.ChartCategoryColumnBinding, out string? resolvedCategoryId, tableInstanceBlock) &&
                                            !string.IsNullOrWhiteSpace(resolvedCategoryId);
                if (boundCategoryResolved)
                {
                    boundCategoryId = resolvedCategoryId!;
                }

                int boundCategoryIndex = 0;
                for (int categoryOptionIndex = 1; categoryOptionIndex < catColCount; categoryOptionIndex++)
                {
                    if (string.Equals(_scratchOptionIds[categoryOptionIndex], boundCategoryId, StringComparison.Ordinal))
                    {
                        boundCategoryIndex = categoryOptionIndex;
                        break;
                    }
                }

                string categoryText = boundCategoryIndex > 0
                    ? _scratchOptionNames[boundCategoryIndex]
                    : string.IsNullOrWhiteSpace(boundCategoryId) ? "(none)" : boundCategoryId;
                DrawBoundValueField(categoryText, ddX, y, ddW, style, boundCategoryResolved || string.IsNullOrWhiteSpace(boundCategoryId));
            }
            else
            {
                _viewChartCatColIndex = selectedCatIdx;
                if (Im.Dropdown("view_chart_cat_dd", _scratchOptionNames.AsSpan(0, catColCount), ref _viewChartCatColIndex, ddX, y, ddW, InspectorDropdownFlags))
                {
                    var old = view.Clone();
                    view.ChartCategoryColumnId = _viewChartCatColIndex > 0 ? _scratchOptionIds[_viewChartCatColIndex] : null;
                    ApplyViewConfigChange(workspace, table, view, old);
                }
            }
            y += RowHeight + DropdownBottomSpacing;

            // Value Column (number/formula/subtable columns)
            int valColCount = 1;
            _scratchOptionNames[0] = "(none)";
            _scratchOptionIds[0] = "";
            int selectedValIdx = 0;
            for (int ci = 0; ci < table.Columns.Count; ci++)
            {
                var colKind = table.Columns[ci].Kind;
                if (colKind == DocColumnKind.Number || colKind == DocColumnKind.Formula || colKind == DocColumnKind.Subtable)
                {
                    _scratchOptionNames[valColCount] = table.Columns[ci].Name;
                    _scratchOptionIds[valColCount] = table.Columns[ci].Id;
                    if (string.Equals(table.Columns[ci].Id, view.ChartValueColumnId, StringComparison.Ordinal))
                        selectedValIdx = valColCount;
                    valColCount++;
                }
            }

            float valTextY = y + (RowHeight - style.FontSize) * 0.5f;
            Im.Text("Value:".AsSpan(), rowX, valTextY, style.FontSize, style.TextSecondary);
            var chartValueRect = new ImRect(ddX, y, ddW, RowHeight);
            bool hasChartValueBinding = view.ChartValueColumnBinding != null && !view.ChartValueColumnBinding.IsEmpty;
            QueueViewBindingContextMenu(table, view, ViewBindingTargetKind.ChartValueColumn, "", "Chart Value Column", chartValueRect, hasChartValueBinding);
            if (hasChartValueBinding)
            {
                string boundValueId = view.ChartValueColumnId ?? "";
                bool boundValueResolved = workspace.TryResolveViewBindingToString(table, view.ChartValueColumnBinding, out string? resolvedValueId, tableInstanceBlock) &&
                                         !string.IsNullOrWhiteSpace(resolvedValueId);
                if (boundValueResolved)
                {
                    boundValueId = resolvedValueId!;
                }

                int boundValueIndex = 0;
                for (int valueOptionIndex = 1; valueOptionIndex < valColCount; valueOptionIndex++)
                {
                    if (string.Equals(_scratchOptionIds[valueOptionIndex], boundValueId, StringComparison.Ordinal))
                    {
                        boundValueIndex = valueOptionIndex;
                        break;
                    }
                }

                string valueText = boundValueIndex > 0
                    ? _scratchOptionNames[boundValueIndex]
                    : string.IsNullOrWhiteSpace(boundValueId) ? "(none)" : boundValueId;
                DrawBoundValueField(valueText, ddX, y, ddW, style, boundValueResolved || string.IsNullOrWhiteSpace(boundValueId));
            }
            else
            {
                _viewChartValColIndex = selectedValIdx;
                if (Im.Dropdown("view_chart_val_dd", _scratchOptionNames.AsSpan(0, valColCount), ref _viewChartValColIndex, ddX, y, ddW, InspectorDropdownFlags))
                {
                    var old = view.Clone();
                    view.ChartValueColumnId = _viewChartValColIndex > 0 ? _scratchOptionIds[_viewChartValColIndex] : null;
                    ApplyViewConfigChange(workspace, table, view, old);
                }
            }
            y += RowHeight + DropdownBottomSpacing;
        }

        if (view.Type == DocViewType.Custom)
        {
            if (TableViewRendererResolver.TryGetCustomRenderer(view, out var customRenderer))
            {
                float inspectorStartY = y;
                float nextY = customRenderer.DrawInspector(workspace, table, view, contentRect, y, style);
                if (nextY > inspectorStartY)
                {
                    y = nextY;
                }
            }
            else
            {
                float warningTextY = y + (RowHeight - style.FontSize) * 0.5f;
                Im.Text("Custom renderer is unavailable for this view.".AsSpan(), rowX, warningTextY, style.FontSize - 1f, style.TextSecondary);
                y += RowHeight;
            }
        }

        // View type label
        float typeTextY = y + (RowHeight - style.FontSize) * 0.5f;
        string typeLabel = TableViewRendererResolver.GetViewTypeDisplayName(view);
        Im.Text("Type:".AsSpan(), rowX, typeTextY, style.FontSize, style.TextSecondary);
        Im.Text(typeLabel.AsSpan(), rowX + 50f, typeTextY, style.FontSize, style.TextPrimary);
        y += RowHeight;

        return y;
    }

    private static bool TryGetColumnUiPlugin(DocColumn column, out IDerpDocColumnUiPlugin plugin)
    {
        string columnTypeId = DocColumnTypeIdMapper.Resolve(column.ColumnTypeId, column.Kind);
        if (DocColumnTypeIdMapper.IsBuiltIn(columnTypeId))
        {
            plugin = null!;
            return false;
        }

        return ColumnUiPluginRegistry.TryGet(columnTypeId, out plugin!);
    }

    private static string GetColumnKindIcon(DocColumn column)
    {
        string columnTypeId = DocColumnTypeIdMapper.Resolve(column.ColumnTypeId, column.Kind);
        if (!DocColumnTypeIdMapper.IsBuiltIn(columnTypeId) &&
            ColumnTypeDefinitionRegistry.TryGet(columnTypeId, out var typeDefinition) &&
            !string.IsNullOrWhiteSpace(typeDefinition.IconGlyph))
        {
            return typeDefinition.IconGlyph;
        }

        return GetColumnKindIcon(column.Kind);
    }

    private static string GetColumnKindIcon(DocColumnKind kind) => kind switch
    {
        DocColumnKind.Id => IdIcon,
        DocColumnKind.Text => TextIcon,
        DocColumnKind.Number => NumberIcon,
        DocColumnKind.Checkbox => CheckboxIcon,
        DocColumnKind.Select => SelectIcon,
        DocColumnKind.Formula => FormulaIcon,
        DocColumnKind.Relation => RelationIcon,
        DocColumnKind.TableRef => TableRefIcon,
        DocColumnKind.Spline => SplineIcon,
        DocColumnKind.TextureAsset => TextureIcon,
        DocColumnKind.MeshAsset => MeshIcon,
        DocColumnKind.AudioAsset => AudioIcon,
        DocColumnKind.UiAsset => UiIcon,
        DocColumnKind.Vec2 => Vec2Icon,
        DocColumnKind.Vec3 => Vec3Icon,
        DocColumnKind.Vec4 => Vec4Icon,
        DocColumnKind.Color => ColorIcon,
        _ => TextIcon,
    };
}
