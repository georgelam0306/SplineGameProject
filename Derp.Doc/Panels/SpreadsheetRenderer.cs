using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using DerpEngine = DerpLib.Derp;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Input;
using DerpLib.ImGui.Widgets;
using DerpLib.Rendering;
using Derp.Doc.Assets;
using Derp.Doc.Commands;
using Derp.Doc.Editor;
using Derp.Doc.Model;
using Derp.Doc.Preferences;
using Derp.Doc.Plugins;
using Derp.Doc.Tables;
using FontAwesome.Sharp;
using Silk.NET.Input;

namespace Derp.Doc.Panels;

/// <summary>
/// Custom spreadsheet grid renderer — owns the full grid layout so that draw,
/// hit-test and edit overlay all use the same cell rects (GetCellRect).
/// Replaces ImTable usage to eliminate misalignment bugs.
/// </summary>
internal static class SpreadsheetRenderer
{
    // --- Constants ---
    private const float HeaderHeight = 28f;
    private const float RowHeight = 26f;
    private const float AssetCellMinHeight = 56f;
    private const float AssetThumbnailPadding = 4f;
    private const float AssetThumbnailTextGap = 4f;
    private const float AssetThumbnailPlaceholderCorner = 5f;
    private const float AudioPlayButtonSize = 18f;
    private const int MeshPreviewViewportTextureSize = 256;
    private const int MeshPreviewRetryFramesLoading = 8;
    private const int MeshPreviewRetryFramesFailure = 20;
    private const int MeshPreviewOrbitDragLockOwnerId = unchecked((int)0x4D504F52);
    private const int MeshPreviewPanDragLockOwnerId = unchecked((int)0x4D505041);
    private const float CellPaddingY = 4f;
    private const float WrappedTextLineSpacing = 3f;
    private const float TableTitleRowHeight = 34f;
    private const float TableTitleBottomSpacing = 8f;
    private const float TableTitleFontSizeBoost = 7f;
    private const float TableTitleOverlayReservedWidth = 34f;
    private const float TableTitleVariantBadgeHorizontalPadding = 7f;
    private const float TableTitleVariantBadgeGap = 6f;
    private const float CellPaddingX = 8f;
    private static float ScrollbarWidth => Im.Style.ScrollbarWidth;
    private static float HorizontalScrollbarHeight => Im.Style.ScrollbarWidth;
    private const float RowNumberWidth = 24f;
    private const float MinColumnWidth = 56f;
    private const float AddColumnSlotWidth = 34f;
    private const float ColumnResizeGrabHalfWidth = 4f;
    private const float ColumnDragStartThreshold = 3f;
    private const float RowDragStartThreshold = 3f;
    private const float HorizontalScrollWheelSpeed = 40f;
    private const float StickyColumnsShadowWidth = 14f;
    private const int StickyColumnsShadowSteps = 7;
    private const int ColumnTearHandleDotCount = 5;
    private const float ColumnTearHandleDotSize = 2f;
    private const float ColumnTearHandleDotSpacing = 2f;
    private const float RowHandleDotSize = 2f;
    private const float RowHandleDotSpacing = 2f;
    private const float RowAddButtonSize = 12f;
    private const float HeaderMenuButtonPaddingX = 5f;
    private const float HeaderMenuButtonInnerGap = 4f;
    private const float HeaderMenuButtonGap = 4f;
    private const float CellTypeaheadMaxVisibleRows = 8f;
    private const float NumberScrubStartThreshold = 2f;
    private const double NumberScrubSensitivity = 0.02;
    private const float FillHandleSize = 8f;
    private const float SubtableDisplayMinPreviewWidth = 60f;
    private const float SubtableDisplayMinPreviewHeight = 26f;
    private const float SubtableDisplayMaxPreviewSize = 4096f;
    private const float SubtableDisplayDefaultGridHeight = RowHeight;
    private const float SubtableDisplayDefaultRichPreviewHeight = 220f;
    private const string SubtableDisplayRendererGrid = "builtin.grid";
    private const string SubtableDisplayRendererBoard = "builtin.board";
    private const string SubtableDisplayRendererCalendar = "builtin.calendar";
    private const string SubtableDisplayRendererChart = "builtin.chart";
    private const string SubtableDisplayCustomRendererPrefix = "custom:";
    private const int SubtablePreviewFrameBudgetLiteMax = 12;

    // --- Column layout (max 32 columns) ---
    private static float[] _colX = new float[32];
    private static float[] _colW = new float[32];
    private static bool[] _isPinnedCol = new bool[32];
    private static int _colCount;

    // --- Column visibility mapping (display col → table col) ---
    private static int[] _visibleColMap = new int[32];

    private static DocColumn GetVisibleColumn(DocTable table, int displayIndex) =>
        table.Columns[_visibleColMap[displayIndex]];

    // --- View row mapping (display row → source row) ---
    private static int[]? _viewRowIndices;

    /// <summary>Maps a display row index to the actual index in table.Rows.</summary>
    private static int GetSourceRowIndex(int displayIndex) =>
        _viewRowIndices != null ? _viewRowIndices[displayIndex] : displayIndex;

    // --- Grid regions ---
    private static ImRect _dialogBoundsRect;
    private static ImRect _gridRect;
    private static ImRect _headerRect;
    private static ImRect _bodyRect;

    // --- Scroll ---
    private static float _scrollY;
    private static float _scrollX;
    private static float _columnContentWidth;
    private static float _pinnedColumnsWidth;
    private static float _scrollableColumnsViewportWidth;
    private static float _addColumnSlotX;
    private static bool _hasVerticalScrollbar;
    private static bool _hasHorizontalScrollbar;
    private static int _rowCount;
    private static float[] _rowHeights = Array.Empty<float>();
    private static float[] _rowOffsets = Array.Empty<float>();
    private static float _rowContentHeight;
    private static int _rowLayoutCacheProjectRevision = -1;
    private static string _rowLayoutCacheTableId = "";
    private static string _rowLayoutCacheViewId = "";
    private static string _rowLayoutCacheParentRowId = "";
    private static int _rowLayoutCacheColumnSignature;
    private static int _rowLayoutCacheRowMapIdentity;
    private static int _rowLayoutCacheRowCount = -1;
    private static int _rowLayoutCacheColCount = -1;
    private static float _rowLayoutCacheFontSize = -1f;
    private static int _rowLayoutCachePreviewQualitySignature = -1;

    private static int _parentFilterCacheProjectRevision = -1;
    private static string _parentFilterCacheTableId = "";
    private static string _parentFilterCacheViewId = "";
    private static string _parentFilterCacheParentRowId = "";
    private static string _parentFilterCacheParentColumnId = "";
    private static int _parentFilterCacheBaseIndicesIdentity;
    private static int[] _parentFilterScratch = Array.Empty<int>();
    private static int[] _parentFilterCachedIndices = Array.Empty<int>();

    private sealed class SubtableCountCacheEntry
    {
        public string ParentTableId = "";
        public string SubtableColumnId = "";
        public string ChildTableId = "";
        public string ParentRowColumnId = "";
        public int ProjectRevision = -1;
        public int[] CountsBySourceRow = Array.Empty<int>();
    }

    private static readonly List<SubtableCountCacheEntry> _subtableCountCacheEntries = new();
    private static readonly Dictionary<string, int> _subtableCountParentRowLookupScratch = new(StringComparer.Ordinal);
    private static readonly List<IDerpDocTableViewRenderer> _subtableRendererOptionsScratch = new(16);
    private static readonly List<string> _nodeSubtableRendererIdOptionsScratch = new(16);

    private sealed class ParentRowIndexCacheEntry
    {
        public string ChildTableId = "";
        public string ParentRowColumnId = "";
        public int ProjectRevision = -1;
        public readonly Dictionary<string, int[]> SourceRowIndicesByParentRowId = new(StringComparer.Ordinal);
    }

    private static readonly List<ParentRowIndexCacheEntry> _parentRowIndexCacheEntries = new();
    private static readonly Dictionary<string, int> _parentRowIndexBuildCounts = new(StringComparer.Ordinal);
    private static readonly List<string> _parentRowIndexBuildKeys = new();

    private readonly record struct SubtablePreviewViewCacheKey(
        string TableId,
        DocViewType ViewType,
        string? CustomRendererId);

    private static int _subtablePreviewDrawFrame = -1;
    private static int _subtablePreviewFrameBudget;
    private static int _subtablePreviewFrameUsage;
    private static int _subtableLookupCacheFrame = -1;
    private static readonly Dictionary<string, DocTable> _subtableTableLookupCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<SubtablePreviewViewCacheKey, DocView> _subtablePreviewViewLookupCache = new();

    // --- Nested subtable grid rendering (re-entrant SpreadsheetRenderer) ---
    private const int MaxNestedSpreadsheetDepth = 3;
    private static int _nestedSpreadsheetDepth;
    private static NestedSpreadsheetBuffers?[] _nestedSpreadsheetBuffersByDepth = new NestedSpreadsheetBuffers?[MaxNestedSpreadsheetDepth];

    private static string _activeParentRowIdOverride = "";

    private sealed class NestedSpreadsheetBuffers
    {
        public readonly float[] ColX = new float[32];
        public readonly float[] ColW = new float[32];
        public readonly bool[] IsPinnedCol = new bool[32];
        public readonly int[] VisibleColMap = new int[32];
        public float[] RowHeights = Array.Empty<float>();
        public float[] RowOffsets = Array.Empty<float>();
        public readonly HashSet<int> SelectedRows = new();
    }

    private struct NestedSpreadsheetSnapshot
    {
        public float[] ColX;
        public float[] ColW;
        public bool[] IsPinnedCol;
        public int[] VisibleColMap;
        public HashSet<int> SelectedRows;

        public int ColCount;
        public int[]? ViewRowIndices;

        public ImRect DialogBoundsRect;
        public ImRect GridRect;
        public ImRect HeaderRect;
        public ImRect BodyRect;

        public float ScrollY;
        public float ScrollX;
        public float ColumnContentWidth;
        public float PinnedColumnsWidth;
        public float ScrollableColumnsViewportWidth;
        public float AddColumnSlotX;
        public bool HasVerticalScrollbar;
        public bool HasHorizontalScrollbar;
        public int RowCount;
        public float[] RowHeights;
        public float[] RowOffsets;
        public float RowContentHeight;

        public int RowLayoutCacheProjectRevision;
        public string RowLayoutCacheTableId;
        public string RowLayoutCacheViewId;
        public string RowLayoutCacheParentRowId;
        public int RowLayoutCacheColumnSignature;
        public int RowLayoutCacheRowMapIdentity;
        public int RowLayoutCacheRowCount;
        public int RowLayoutCacheColCount;
        public int RowLayoutCachePreviewQualitySignature;
        public float RowLayoutCacheFontSize;

        public int HoveredRow;
        public int HoveredCol;
        public int HoveredHeaderCol;
        public int HoveredTearHandleCol;
        public int HoveredResizeCol;
        public int HoveredRowDragHandle;
        public int HoveredRowAddBelow;

        public int SelStartRow;
        public int SelStartCol;
        public int SelEndRow;
        public int SelEndCol;
        public bool IsDragging;
        public int LastClickedRow;

        public int ActiveRow;
        public int ActiveCol;
        public int SelectedHeaderCol;
        public int ResizingColIndex;
        public float ResizeStartMouseX;
        public float ResizeStartWidth;
        public float ResizeCurrentWidth;
        public int ColumnDragSourceCol;
        public int ColumnDragTargetInsertIndex;
        public float ColumnDragStartMouseX;
        public bool IsColumnDragging;
        public int RowDragSourceIndex;
        public int RowDragTargetInsertIndex;
        public float RowDragStartMouseY;
        public bool IsRowDragging;

        public int ContextRowIndex;
        public int ContextColIndex;

        public ImRect CellTypeaheadPopupRect;
        public bool CellTypeaheadPopupVisible;

        public bool IsInteractiveRender;
        public string ActiveEmbeddedStateKey;
        public DocView? EmbeddedView;
        public DocBlock? EmbeddedTableInstanceBlock;
        public int ActiveRenderVariantId;
        public string ActiveParentRowIdOverride;
    }

    private struct NestedSpreadsheetScope : IDisposable
    {
        private bool _entered;
        private NestedSpreadsheetSnapshot _snapshot;
        private NestedSpreadsheetBuffers _buffers;

        public bool Entered => _entered;

        public static NestedSpreadsheetScope TryEnter()
        {
            if (_nestedSpreadsheetDepth >= MaxNestedSpreadsheetDepth)
            {
                return default;
            }

            int depth = _nestedSpreadsheetDepth;
            _nestedSpreadsheetDepth++;

            NestedSpreadsheetBuffers buffers = _nestedSpreadsheetBuffersByDepth[depth] ??= new NestedSpreadsheetBuffers();
            var snapshot = new NestedSpreadsheetSnapshot
            {
                ColX = _colX,
                ColW = _colW,
                IsPinnedCol = _isPinnedCol,
                VisibleColMap = _visibleColMap,
                SelectedRows = _selectedRows,
                ColCount = _colCount,
                ViewRowIndices = _viewRowIndices,
                DialogBoundsRect = _dialogBoundsRect,
                GridRect = _gridRect,
                HeaderRect = _headerRect,
                BodyRect = _bodyRect,
                ScrollY = _scrollY,
                ScrollX = _scrollX,
                ColumnContentWidth = _columnContentWidth,
                PinnedColumnsWidth = _pinnedColumnsWidth,
                ScrollableColumnsViewportWidth = _scrollableColumnsViewportWidth,
                AddColumnSlotX = _addColumnSlotX,
                HasVerticalScrollbar = _hasVerticalScrollbar,
                HasHorizontalScrollbar = _hasHorizontalScrollbar,
                RowCount = _rowCount,
                RowHeights = _rowHeights,
                RowOffsets = _rowOffsets,
                RowContentHeight = _rowContentHeight,
                RowLayoutCacheProjectRevision = _rowLayoutCacheProjectRevision,
                RowLayoutCacheTableId = _rowLayoutCacheTableId,
                RowLayoutCacheViewId = _rowLayoutCacheViewId,
                RowLayoutCacheParentRowId = _rowLayoutCacheParentRowId,
                RowLayoutCacheColumnSignature = _rowLayoutCacheColumnSignature,
                RowLayoutCacheRowMapIdentity = _rowLayoutCacheRowMapIdentity,
                RowLayoutCacheRowCount = _rowLayoutCacheRowCount,
                RowLayoutCacheColCount = _rowLayoutCacheColCount,
                RowLayoutCachePreviewQualitySignature = _rowLayoutCachePreviewQualitySignature,
                RowLayoutCacheFontSize = _rowLayoutCacheFontSize,
                HoveredRow = _hoveredRow,
                HoveredCol = _hoveredCol,
                HoveredHeaderCol = _hoveredHeaderCol,
                HoveredTearHandleCol = _hoveredTearHandleCol,
                HoveredResizeCol = _hoveredResizeCol,
                HoveredRowDragHandle = _hoveredRowDragHandle,
                HoveredRowAddBelow = _hoveredRowAddBelow,
                SelStartRow = _selStartRow,
                SelStartCol = _selStartCol,
                SelEndRow = _selEndRow,
                SelEndCol = _selEndCol,
                IsDragging = _isDragging,
                LastClickedRow = _lastClickedRow,
                ActiveRow = _activeRow,
                ActiveCol = _activeCol,
                SelectedHeaderCol = _selectedHeaderCol,
                ResizingColIndex = _resizingColIndex,
                ResizeStartMouseX = _resizeStartMouseX,
                ResizeStartWidth = _resizeStartWidth,
                ResizeCurrentWidth = _resizeCurrentWidth,
                ColumnDragSourceCol = _columnDragSourceCol,
                ColumnDragTargetInsertIndex = _columnDragTargetInsertIndex,
                ColumnDragStartMouseX = _columnDragStartMouseX,
                IsColumnDragging = _isColumnDragging,
                RowDragSourceIndex = _rowDragSourceIndex,
                RowDragTargetInsertIndex = _rowDragTargetInsertIndex,
                RowDragStartMouseY = _rowDragStartMouseY,
                IsRowDragging = _isRowDragging,
                ContextRowIndex = _contextRowIndex,
                ContextColIndex = _contextColIndex,
                CellTypeaheadPopupRect = _cellTypeaheadPopupRect,
                CellTypeaheadPopupVisible = _cellTypeaheadPopupVisible,
                IsInteractiveRender = _isInteractiveRender,
                ActiveEmbeddedStateKey = _activeEmbeddedStateKey,
                EmbeddedView = _embeddedView,
                EmbeddedTableInstanceBlock = _embeddedTableInstanceBlock,
                ActiveRenderVariantId = _activeRenderVariantId,
                ActiveParentRowIdOverride = _activeParentRowIdOverride,
            };

            _colX = buffers.ColX;
            _colW = buffers.ColW;
            _isPinnedCol = buffers.IsPinnedCol;
            _visibleColMap = buffers.VisibleColMap;
            _rowHeights = buffers.RowHeights;
            _rowOffsets = buffers.RowOffsets;
            _selectedRows = buffers.SelectedRows;
            _selectedRows.Clear();

            _viewRowIndices = null;
            _scrollY = 0f;
            _scrollX = 0f;
            _hoveredRow = -1;
            _hoveredCol = -1;
            _hoveredHeaderCol = -1;
            _hoveredTearHandleCol = -1;
            _hoveredResizeCol = -1;
            _hoveredRowDragHandle = -1;
            _hoveredRowAddBelow = -1;
            _selStartRow = -1;
            _selStartCol = -1;
            _selEndRow = -1;
            _selEndCol = -1;
            _isDragging = false;
            _lastClickedRow = -1;
            _activeRow = -1;
            _activeCol = -1;
            _selectedHeaderCol = -1;
            _resizingColIndex = -1;
            _columnDragSourceCol = -1;
            _columnDragTargetInsertIndex = -1;
            _isColumnDragging = false;
            _rowDragSourceIndex = -1;
            _rowDragTargetInsertIndex = -1;
            _isRowDragging = false;
            _contextRowIndex = -1;
            _contextColIndex = -1;
            _cellTypeaheadPopupRect = default;
            _cellTypeaheadPopupVisible = false;
            _activeEmbeddedStateKey = "";
            _embeddedView = null;
            _embeddedTableInstanceBlock = null;
            _activeParentRowIdOverride = "";

            return new NestedSpreadsheetScope
            {
                _entered = true,
                _snapshot = snapshot,
                _buffers = buffers,
            };
        }

        public void Dispose()
        {
            if (!_entered)
            {
                return;
            }

            _buffers.RowHeights = _rowHeights;
            _buffers.RowOffsets = _rowOffsets;

            _colX = _snapshot.ColX;
            _colW = _snapshot.ColW;
            _isPinnedCol = _snapshot.IsPinnedCol;
            _visibleColMap = _snapshot.VisibleColMap;
            _selectedRows = _snapshot.SelectedRows;
            _colCount = _snapshot.ColCount;
            _viewRowIndices = _snapshot.ViewRowIndices;
            _dialogBoundsRect = _snapshot.DialogBoundsRect;
            _gridRect = _snapshot.GridRect;
            _headerRect = _snapshot.HeaderRect;
            _bodyRect = _snapshot.BodyRect;
            _scrollY = _snapshot.ScrollY;
            _scrollX = _snapshot.ScrollX;
            _columnContentWidth = _snapshot.ColumnContentWidth;
            _pinnedColumnsWidth = _snapshot.PinnedColumnsWidth;
            _scrollableColumnsViewportWidth = _snapshot.ScrollableColumnsViewportWidth;
            _addColumnSlotX = _snapshot.AddColumnSlotX;
            _hasVerticalScrollbar = _snapshot.HasVerticalScrollbar;
            _hasHorizontalScrollbar = _snapshot.HasHorizontalScrollbar;
            _rowCount = _snapshot.RowCount;
            _rowHeights = _snapshot.RowHeights;
            _rowOffsets = _snapshot.RowOffsets;
            _rowContentHeight = _snapshot.RowContentHeight;
            _rowLayoutCacheProjectRevision = _snapshot.RowLayoutCacheProjectRevision;
            _rowLayoutCacheTableId = _snapshot.RowLayoutCacheTableId;
            _rowLayoutCacheViewId = _snapshot.RowLayoutCacheViewId;
            _rowLayoutCacheParentRowId = _snapshot.RowLayoutCacheParentRowId;
            _rowLayoutCacheColumnSignature = _snapshot.RowLayoutCacheColumnSignature;
            _rowLayoutCacheRowMapIdentity = _snapshot.RowLayoutCacheRowMapIdentity;
            _rowLayoutCacheRowCount = _snapshot.RowLayoutCacheRowCount;
            _rowLayoutCacheColCount = _snapshot.RowLayoutCacheColCount;
            _rowLayoutCachePreviewQualitySignature = _snapshot.RowLayoutCachePreviewQualitySignature;
            _rowLayoutCacheFontSize = _snapshot.RowLayoutCacheFontSize;
            _hoveredRow = _snapshot.HoveredRow;
            _hoveredCol = _snapshot.HoveredCol;
            _hoveredHeaderCol = _snapshot.HoveredHeaderCol;
            _hoveredTearHandleCol = _snapshot.HoveredTearHandleCol;
            _hoveredResizeCol = _snapshot.HoveredResizeCol;
            _hoveredRowDragHandle = _snapshot.HoveredRowDragHandle;
            _hoveredRowAddBelow = _snapshot.HoveredRowAddBelow;
            _selStartRow = _snapshot.SelStartRow;
            _selStartCol = _snapshot.SelStartCol;
            _selEndRow = _snapshot.SelEndRow;
            _selEndCol = _snapshot.SelEndCol;
            _isDragging = _snapshot.IsDragging;
            _lastClickedRow = _snapshot.LastClickedRow;
            _activeRow = _snapshot.ActiveRow;
            _activeCol = _snapshot.ActiveCol;
            _selectedHeaderCol = _snapshot.SelectedHeaderCol;
            _resizingColIndex = _snapshot.ResizingColIndex;
            _resizeStartMouseX = _snapshot.ResizeStartMouseX;
            _resizeStartWidth = _snapshot.ResizeStartWidth;
            _resizeCurrentWidth = _snapshot.ResizeCurrentWidth;
            _columnDragSourceCol = _snapshot.ColumnDragSourceCol;
            _columnDragTargetInsertIndex = _snapshot.ColumnDragTargetInsertIndex;
            _columnDragStartMouseX = _snapshot.ColumnDragStartMouseX;
            _isColumnDragging = _snapshot.IsColumnDragging;
            _rowDragSourceIndex = _snapshot.RowDragSourceIndex;
            _rowDragTargetInsertIndex = _snapshot.RowDragTargetInsertIndex;
            _rowDragStartMouseY = _snapshot.RowDragStartMouseY;
            _isRowDragging = _snapshot.IsRowDragging;
            _contextRowIndex = _snapshot.ContextRowIndex;
            _contextColIndex = _snapshot.ContextColIndex;
            _cellTypeaheadPopupRect = _snapshot.CellTypeaheadPopupRect;
            _cellTypeaheadPopupVisible = _snapshot.CellTypeaheadPopupVisible;
            _isInteractiveRender = _snapshot.IsInteractiveRender;
            _activeEmbeddedStateKey = _snapshot.ActiveEmbeddedStateKey;
            _embeddedView = _snapshot.EmbeddedView;
            _embeddedTableInstanceBlock = _snapshot.EmbeddedTableInstanceBlock;
            _activeRenderVariantId = _snapshot.ActiveRenderVariantId;
            _activeParentRowIdOverride = _snapshot.ActiveParentRowIdOverride;

            _nestedSpreadsheetDepth--;
        }
    }

    private readonly struct SubtableEmbeddedStateKey : IEquatable<SubtableEmbeddedStateKey>
    {
        public readonly string ParentRowId;
        public readonly string ColumnId;

        public SubtableEmbeddedStateKey(string parentRowId, string columnId)
        {
            ParentRowId = parentRowId;
            ColumnId = columnId;
        }

        public bool Equals(SubtableEmbeddedStateKey other)
        {
            return string.Equals(ParentRowId, other.ParentRowId, StringComparison.Ordinal) &&
                   string.Equals(ColumnId, other.ColumnId, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is SubtableEmbeddedStateKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (ParentRowId.GetHashCode(StringComparison.Ordinal) * 397) ^
                       ColumnId.GetHashCode(StringComparison.Ordinal);
            }
        }
    }

    private sealed class SubtableEmbeddedGridState
    {
        public float ScrollY;
        public float ScrollX;
        public float GridRectX;
        public float GridRectY;
        public float GridRectWidth;
        public float GridRectHeight;
        public float BodyRectX;
        public float BodyRectY;
        public float BodyRectWidth;
        public float BodyRectHeight;
        public float PinnedColumnsWidth;
        public float ScrollableColumnsViewportWidth;
        public float ColumnContentWidth;
        public float RowContentHeight;
        public bool HasVerticalScrollbar;
        public bool HasHorizontalScrollbar;
    }

    private static readonly Dictionary<SubtableEmbeddedStateKey, string> _subtableEmbeddedStateKeyCache = new();
    private static readonly Dictionary<string, SubtableEmbeddedGridState> _subtableEmbeddedGridStateByKey = new(StringComparer.Ordinal);
    private static int _subtableEmbeddedGridFullHeightCacheProjectRevision = -1;
    private static int _subtableEmbeddedGridFullHeightCacheLiveValueRevision = -1;
    private static readonly Dictionary<SubtableEmbeddedGridFullHeightCacheKey, float> _subtableEmbeddedGridFullHeightByKey = new();

    private readonly record struct SubtableEmbeddedGridFullHeightCacheKey(
        string ChildTableId,
        int ChildVariantId,
        string ViewId,
        string ParentRowId,
        int PreviewWidthRounded);

    // --- Hover ---
    private static int _hoveredRow = -1;
    private static int _hoveredCol = -1;
    private static int _hoveredHeaderCol = -1;
    private static int _hoveredTearHandleCol = -1;
    private static int _hoveredResizeCol = -1;
    private static int _hoveredRowDragHandle = -1;
    private static int _hoveredRowAddBelow = -1;

    // --- Cell range selection (click-drag rectangular region) ---
    private static int _selStartRow = -1, _selStartCol = -1;
    private static int _selEndRow = -1, _selEndCol = -1;
    private static bool _isDragging;
    private static bool _isFillHandleDragging;
    private static int _fillDragSourceMinRow = -1;
    private static int _fillDragSourceMinCol = -1;
    private static int _fillDragSourceMaxRow = -1;
    private static int _fillDragSourceMaxCol = -1;
    private static int _fillDragTargetRow = -1;
    private static int _fillDragTargetCol = -1;
    private static bool _tableCopyShortcutDown;
    private static bool _tablePasteShortcutDown;

    // --- Multi-row selection (Shift/Ctrl+click row number) ---
    private static HashSet<int> _selectedRows = new();
    private static int _lastClickedRow = -1;

    // --- Active (focused) cell ---
    private static int _activeRow = -1, _activeCol = -1;
    private static int _selectedHeaderCol = -1;
    private static int _resizingColIndex = -1;
    private static float _resizeStartMouseX;
    private static float _resizeStartWidth;
    private static float _resizeCurrentWidth;
    private static int _columnDragSourceCol = -1;
    private static int _columnDragTargetInsertIndex = -1;
    private static float _columnDragStartMouseX;
    private static bool _isColumnDragging;
    private static int _rowDragSourceIndex = -1;
    private static int _rowDragTargetInsertIndex = -1;
    private static float _rowDragStartMouseY;
    private static bool _isRowDragging;

    // --- Context menu targets ---
    private static int _contextRowIndex = -1;
    private static int _contextColIndex = -1;

    // --- Add-column inline-create state ---
    private static readonly string[] _columnKindNames = ["Id", "Text", "Number", "Checkbox", "Select", "Relation", "TableRef", "Subtable", "Spline", "TextureAsset", "MeshAsset", "AudioAsset", "UiAsset", "Vec2", "Vec3", "Vec4", "Color"];
    private static readonly DocColumnKind[] _columnKinds = [DocColumnKind.Id, DocColumnKind.Text, DocColumnKind.Number, DocColumnKind.Checkbox, DocColumnKind.Select, DocColumnKind.Relation, DocColumnKind.TableRef, DocColumnKind.Subtable, DocColumnKind.Spline, DocColumnKind.TextureAsset, DocColumnKind.MeshAsset, DocColumnKind.AudioAsset, DocColumnKind.UiAsset, DocColumnKind.Vec2, DocColumnKind.Vec3, DocColumnKind.Vec4, DocColumnKind.Color];
    private static readonly List<ColumnTypeDefinition> _pluginColumnTypeDefinitionsScratch = new(16);

    // --- Scratch buffers ---
    private static readonly char[] _rowNumBuf = new char[8];
    private static readonly int[] _rowSelectionRemapScratch = new int[1024];
    private static readonly int[] _cellTypeaheadFilterIndices = new int[4096];
    private static readonly List<int> _multiSelectTargetRowIndices = new(256);
    private static ImRect _cellTypeaheadPopupRect;
    private static bool _cellTypeaheadPopupVisible;
    private const int MaxSplinePreviewCacheEntries = 256;
    private static readonly Dictionary<string, Curve> _splinePreviewCurveByJson = new(StringComparer.Ordinal);
    private static bool _splinePopoverActive;
    private static string _splinePopoverTableId = "";
    private static string _splinePopoverRowId = "";
    private static string _splinePopoverColumnId = "";
    private static string _splinePopoverOriginalJson = "";
    private static string _splinePopoverOwnerStateKey = "";
    private static bool _splinePopoverHasPreviewChange;
    private static int _splinePopoverOpenedFrame = -1;
    private static Curve _splinePopoverCurve;
    private static ImCurveEditor.CurveView _splinePopoverView;

    // --- Inline rename state ---
    private static readonly char[] _renameColBuffer = new char[128];
    private static int _renameColBufferLength;
    private static int _inlineRenameColIndex = -1;
    private static bool _inlineRenameNeedsFocus;
    private static bool _inlineRenameSelectAll;
    private static readonly char[] _renameTableBuffer = new char[128];
    private static int _renameTableBufferLength;
    private static bool _inlineRenameTableActive;
    private static bool _inlineRenameTableNeedsFocus;
    private static bool _inlineRenameTableSelectAll;
    private static string _inlineRenameTableId = "";
    private static bool _showEditFormulaDialog;
    private static int _editFormulaDialogOpenedFrame = -1;
    private static readonly char[] _editFormulaBuffer = new char[256];
    private static int _editFormulaBufferLength;
    private static int _editFormulaColIndex = -1;
    private static EditFormulaTargetKind _editFormulaTargetKind;
    private static string _editFormulaCellRowId = "";
    private static string _editFormulaCellTableId = "";
    private static bool _showEditVectorCellDialog;
    private static int _editVectorCellDialogOpenedFrame = -1;
    private static string _editVectorCellTableId = "";
    private static string _editVectorCellRowId = "";
    private static string _editVectorCellColumnId = "";
    private static readonly char[] _editVectorCellXBuffer = new char[64];
    private static int _editVectorCellXBufferLength;
    private static readonly char[] _editVectorCellYBuffer = new char[64];
    private static int _editVectorCellYBufferLength;
    private static readonly char[] _editVectorCellZBuffer = new char[64];
    private static int _editVectorCellZBufferLength;
    private static readonly char[] _editVectorCellWBuffer = new char[64];
    private static int _editVectorCellWBufferLength;
    private static string _editVectorCellValidationMessage = "";
    private static bool _showEditRelationDialog;
    private static int _editRelationDialogOpenedFrame = -1;
    private static int _editRelationColIndex = -1;
    private static int _editRelationTargetModeIndex = 0;
    private static int _editRelationTableIndex = -1;
    private static int _editRelationVariantIndex = -1;
    private static int _editRelationDisplayColumnIndex = -1;
    private static readonly string[] _relationTargetModeNames =
    [
        "External table",
        "This table",
        "Parent table",
    ];
    private static bool _showEditNumberColumnDialog;
    private static int _editNumberColumnDialogOpenedFrame = -1;
    private static string _editNumberTableId = "";
    private static string _editNumberColumnId = "";
    private static int _editNumberTypeIndex;
    private static readonly string[] _numberTypeLabels = ["int", "float", "Fixed64", "Fixed32"];
    private static readonly char[] _editNumberMinBuffer = new char[64];
    private static int _editNumberMinBufferLength;
    private static readonly char[] _editNumberMaxBuffer = new char[64];
    private static int _editNumberMaxBufferLength;
    private static string _editNumberValidationMessage = "";
    private static bool _showEditSubtableDisplayDialog;
    private static int _editSubtableDisplayDialogOpenedFrame = -1;
    private static string _editSubtableDisplayTableId = "";
    private static string _editSubtableDisplayColumnId = "";
    private static int _editSubtableDisplayRendererIndex;
    private static string _editSubtableDisplayValidationMessage = "";
    private static string _editSubtableDisplayPluginSettingsRendererId = "";
    private static string? _editSubtableDisplayPluginSettingsJson;
    private static int _editSubtableDisplayPreviewQualityIndex;
    private static bool _editSubtableDisplayUseCustomWidth;
    private static bool _editSubtableDisplayUseCustomHeight;
    private static float _editSubtableDisplayWidthValue = SubtableDisplayMinPreviewWidth;
    private static float _editSubtableDisplayHeightValue = SubtableDisplayMinPreviewHeight;
    private static bool _showAddSubtableColumnDialog;
    private static int _addSubtableColumnDialogOpenedFrame = -1;
    private static string _addSubtableColumnTableId = "";
    private static DocColumn? _addSubtableColumnSnapshot;
    private static int _addSubtableColumnModeIndex;
    private static int _addSubtableColumnExistingTableIndex = -1;
    private static string _addSubtableColumnValidationMessage = "";
    private static readonly string[] _addSubtableColumnModeNames = ["Create child table", "Use existing table"];
    private static readonly string[] _subtableDisplayPreviewQualityOptionNames = ["Use global", "Off", "Lite", "Full"];
    private static string[] _subtableDisplayRendererOptionNames = new string[16];
    private static string[] _subtableDisplayRendererOptionIds = new string[16];
    private static int _subtableDisplayRendererOptionCount;
    private static bool _showEditSelectColumnDialog;
    private static int _editSelectColumnDialogOpenedFrame = -1;
    private static string _editSelectTableId = "";
    private static string _editSelectColumnId = "";
    private static int _editSelectSelectedIndex = -1;
    private static int _editSelectDragIndex = -1;
    private static int _editSelectDragTargetIndex = -1;
    private static float _editSelectDragMouseOffsetY;
    private static float _editSelectScrollY;
    private static bool _editSelectInlineRenameNeedsFocus;
    private static readonly char[] _editSelectRenameBuffer = new char[128];
    private static int _editSelectRenameBufferLength;
    private static readonly char[] _editSelectAddBuffer = new char[128];
    private static int _editSelectAddBufferLength;
    private static readonly List<SelectOptionEditEntry> _editSelectEntries = new();
    private static readonly Dictionary<int, string> _editSelectOriginalValuesById = new();
    private static int _editSelectNextEntryId = 1;
    private static bool _showEditMeshPreviewDialog;
    private static string _editMeshPreviewTableId = "";
    private static string _editMeshPreviewRowId = "";
    private static string _editMeshPreviewColumnId = "";
    private static DocModelPreviewSettings _editMeshPreviewDraft = new();
    private static readonly char[] _editMeshPreviewTexturePathBuffer = new char[256];
    private static int _editMeshPreviewTexturePathBufferLength;
    private static readonly MeshPreviewGenerator _editMeshPreviewGenerator = new();
    private static Texture _editMeshPreviewViewportTexture;
    private static bool _hasEditMeshPreviewViewportTexture;
    private static bool _editMeshPreviewViewportHasRequest;
    private static string _editMeshPreviewViewportAssetsRoot = "";
    private static string _editMeshPreviewViewportRelativePath = "";
    private static float _editMeshPreviewViewportOrbitYawDegrees;
    private static float _editMeshPreviewViewportOrbitPitchDegrees;
    private static float _editMeshPreviewViewportPanX;
    private static float _editMeshPreviewViewportPanY;
    private static float _editMeshPreviewViewportZoom;
    private static string? _editMeshPreviewViewportTextureRelativePath;
    private static MeshPreviewGenerator.PreviewRenderStatus _editMeshPreviewViewportStatus;
    private static int _editMeshPreviewViewportRetryFrame;
    private static bool _editMeshPreviewOrbitDragging;
    private static bool _editMeshPreviewPanDragging;
    private static bool _editMeshPreviewOrbitCursorLocked;
    private static bool _editMeshPreviewPanCursorLocked;

    // --- Formula editor completion + token preview ---
    private const int MaxFormulaCompletionEntries = 96;
    private const int MaxFormulaDisplayTokens = 512;

    private enum FormulaCompletionKind
    {
        Function,
        Method,
        Column,
        Table,
        Document,
        Keyword,
    }

    private enum EditFormulaTargetKind
    {
        Column,
        Cell,
    }

    private enum FormulaDisplayTokenKind
    {
        Plain,
        Function,
        Method,
        Column,
        Table,
        Document,
        Keyword,
        Number,
        String,
    }

    private enum SubtableDisplayRendererKind
    {
        Count,
        Grid,
        Board,
        Calendar,
        Chart,
        Custom,
    }

    private struct FormulaCompletionEntry
    {
        public string DisplayText;
        public string InsertText;
        public FormulaCompletionKind Kind;
        public int CaretBacktrack;
    }

    private readonly struct FormulaCompletionTemplate
    {
        public FormulaCompletionTemplate(string displayText, string insertText, int caretBacktrack)
        {
            DisplayText = displayText;
            InsertText = insertText;
            CaretBacktrack = caretBacktrack;
        }

        public string DisplayText { get; }
        public string InsertText { get; }
        public int CaretBacktrack { get; }
    }

    private readonly struct FormulaCallContext
    {
        public FormulaCallContext(int nameStart, int nameLength, bool isMethod, int argumentIndex)
        {
            NameStart = nameStart;
            NameLength = nameLength;
            IsMethod = isMethod;
            ArgumentIndex = argumentIndex;
        }

        public int NameStart { get; }
        public int NameLength { get; }
        public bool IsMethod { get; }
        public int ArgumentIndex { get; }
    }

    private enum FormulaReceiverKind
    {
        Unknown,
        TableReference,
        RowCollection,
        RowReference,
        DocumentNamespace,
        DocumentReference,
        Scalar
    }

    private readonly struct FormulaReceiverHint
    {
        public FormulaReceiverHint(FormulaReceiverKind kind, DocTable? table = null, DocDocument? document = null)
        {
            Kind = kind;
            Table = table;
            Document = document;
        }

        public FormulaReceiverKind Kind { get; }
        public DocTable? Table { get; }
        public DocDocument? Document { get; }
    }

    private struct FormulaDisplayToken
    {
        public int Start;
        public int Length;
        public FormulaDisplayTokenKind Kind;
    }

    private struct FormulaVisualRun
    {
        public int Start;
        public int Length;
        public int End;
        public FormulaDisplayTokenKind Kind;
        public bool DrawAsPill;
        public float LeftPad;
        public float RightPad;
        public float IconAdvance;
        public float TextWidth;
        public float RenderWidth;
        public int LineIndex;
        public float X;
    }

    private struct FormulaInspectorState
    {
        public bool HasValue;
        public string Title;
        public string Description;
        public string Preview;
        public FormulaDisplayTokenKind ContextKind;
        public string ContextToken;
    }

    private static readonly FormulaCompletionEntry[] _formulaCompletionEntries = new FormulaCompletionEntry[MaxFormulaCompletionEntries];
    private static readonly FormulaDisplayToken[] _formulaDisplayTokens = new FormulaDisplayToken[MaxFormulaDisplayTokens];
    private static readonly FormulaVisualRun[] _formulaVisualRuns = new FormulaVisualRun[MaxFormulaDisplayTokens];
    private static readonly float[] _formulaVisualLineWidths = new float[128];
    private static readonly DocTable _formulaFallbackContextTable = new()
    {
        Id = "__formula_editor_fallback_table__",
        Name = "__formula_editor_fallback_table__",
    };
    private static readonly DocView _subtablePreviewGridFallbackView = new()
    {
        Id = "__subtable_preview_grid__",
        Name = "Grid preview",
        Type = DocViewType.Grid,
    };
    private static readonly DocView _subtablePreviewBoardFallbackView = new()
    {
        Id = "__subtable_preview_board__",
        Name = "Board preview",
        Type = DocViewType.Board,
    };
    private static readonly DocView _subtablePreviewCalendarFallbackView = new()
    {
        Id = "__subtable_preview_calendar__",
        Name = "Calendar preview",
        Type = DocViewType.Calendar,
    };
    private static readonly DocView _subtablePreviewChartFallbackView = new()
    {
        Id = "__subtable_preview_chart__",
        Name = "Chart preview",
        Type = DocViewType.Chart,
    };
    private static readonly DocView _subtablePreviewCustomFallbackView = new()
    {
        Id = "__subtable_preview_custom__",
        Name = "Custom preview",
        Type = DocViewType.Custom,
    };
    private static readonly SubtablePreviewEditorContext _subtablePreviewEditorContext = new();
    private static int _formulaCompletionCount;
    private static int _formulaCompletionSelectedIndex;
    private static FormulaInspectorState _formulaInspectorState;
    private static string _formulaInspectorContextColumnId = "";
    private static int _formulaInspectorPreviewRowIndex;
    private static FormulaDisplayTokenKind _formulaCaretTokenKind;
    private static string _formulaCaretTokenText = "";
    private const float FormulaInspectorPanelHeight = 120f;
    private static readonly string _formulaTableIconText = ((char)IconChar.Table).ToString();
    private static readonly string _formulaDocumentIconText = ((char)IconChar.File).ToString();
    private static readonly string _formulaColumnIconText = ((char)IconChar.Hashtag).ToString();
    private static readonly string _formulaFunctionIconText = ((char)IconChar.Code).ToString();
    private static readonly string _formulaMethodIconText = ((char)IconChar.List).ToString();
    private static readonly string _formulaRowIconText = ((char)IconChar.ListOl).ToString();
    private static readonly string _formulaKeywordIconText = ((char)IconChar.Check).ToString();
    private static readonly string _headerTextIconText = ((char)IconChar.Font).ToString();
    private static readonly string _headerIdIconText = "ID";
    private static readonly string _headerNumberIconText = ((char)IconChar.Hashtag).ToString();
    private static readonly string _headerCheckboxIconText = ((char)IconChar.SquareCheck).ToString();
    private static readonly string _headerSelectIconText = ((char)IconChar.List).ToString();
    private static readonly string _headerFormulaIconText = ((char)IconChar.Code).ToString();
    private static readonly string _headerRelationIconText = ((char)IconChar.Link).ToString();
    private static readonly string _headerTableRefIconText = ((char)IconChar.Table).ToString();
    private static readonly string _headerSplineIconText = ((char)IconChar.BezierCurve).ToString();
    private static readonly string _headerTextureAssetIconText = ((char)IconChar.Image).ToString();
    private static readonly string _headerMeshAssetIconText = ((char)IconChar.Cube).ToString();
    private static readonly string _headerAudioAssetIconText = ((char)IconChar.Music).ToString();
    private static readonly string _headerUiAssetIconText = ((char)IconChar.WindowMaximize).ToString();
    private static readonly string _audioPlayIconText = ((char)IconChar.Play).ToString();
    private static readonly string _headerVec2IconText = "V2";
    private static readonly string _headerVec3IconText = "V3";
    private static readonly string _headerVec4IconText = "V4";
    private static readonly string _headerColorIconText = "RGB";
    private static readonly string _headerDisplayColumnIconText = ((char)IconChar.Bookmark).ToString();
    private static readonly string _headerMenuCaretIconText = ((char)IconChar.ChevronDown).ToString();
    private static readonly string _headerPrimaryKeyIconText = ((char)IconChar.Key).ToString();
    private static readonly string _headerSecondaryKeyIconText = ((char)IconChar.Key).ToString();
    private static readonly string _headerLockIconText = ((char)IconChar.Lock).ToString();
    private static readonly string _optionsIconText = ((char)IconChar.EllipsisV).ToString();
    private static readonly DocFormulaEngine _formulaPreviewEngine = new();
    private static bool _isInteractiveRender = true;
    private static readonly Dictionary<string, EmbeddedSpreadsheetViewState> _embeddedViewStates = new(StringComparer.Ordinal);
    private static string _activeEmbeddedStateKey = "";
    private static string _wheelSuppressedStateKey = "";
    private static int _wheelSuppressedFrame = -1;
    private static DocView? _embeddedView;
    private static DocBlock? _embeddedTableInstanceBlock;
    private static int _activeRenderVariantId = DocTableVariant.BaseVariantId;
    private static string _contextMenuOwnerStateKey = "";
    private static readonly FormulaCompletionTemplate[] _formulaFunctionCompletions =
    [
        new("Lookup(tableOrRows, predicate, valueExpr)", "Lookup()", 1),
        new("CountIf(tableOrRows, predicate)", "CountIf()", 1),
        new("SumIf(tableOrRows, predicate, valueExpr)", "SumIf()", 1),
        new("If(condition, whenTrue, whenFalse)", "If()", 1),
        new("Abs(value)", "Abs()", 1),
        new("Pow(base, exponent)", "Pow()", 1),
        new("Exp(value)", "Exp()", 1),
        new("EvalSpline(thisRow.Curve, t)", "EvalSpline()", 1),
        new("Upper(text)", "Upper()", 1),
        new("Lower(text)", "Lower()", 1),
        new("Contains(text, value)", "Contains()", 1),
        new("Concat(value1, value2, ...)", "Concat()", 1),
        new("Date(text)", "Date()", 1),
        new("Today()", "Today()", 1),
        new("AddDays(date, days)", "AddDays()", 1),
        new("DaysBetween(startDate, endDate)", "DaysBetween()", 1),
        new("Vec2(x, y)", "Vec2()", 1),
        new("Vec3(x, y, z)", "Vec3()", 1),
        new("Vec4(x, y, z, w)", "Vec4()", 1),
        new("Color(r, g, b, a)", "Color()", 1),
    ];

    private static readonly FormulaCompletionTemplate[] _formulaMethodCompletions =
    [
        new("Filter(predicate)", "Filter()", 1),
        new("Count()", "Count()", 1),
        new("First()", "First()", 1),
        new("Sum(valueExpr)", "Sum()", 1),
        new("Average(valueExpr)", "Average()", 1),
        new("Sort(sortExpr)", "Sort()", 1),
    ];
    private static readonly List<string> _formulaPluginFunctionNameScratch = new(16);

    private sealed class EmbeddedSpreadsheetViewState
    {
        public float ScrollY;
        public float ScrollX;
        public float GridRectX;
        public float GridRectY;
        public float GridRectWidth;
        public float GridRectHeight;
        public float BodyRectX;
        public float BodyRectY;
        public float BodyRectWidth;
        public float BodyRectHeight;
        public float PinnedColumnsWidth;
        public float ScrollableColumnsViewportWidth;
        public float ColumnContentWidth;
        public float RowContentHeight;
        public bool HasVerticalScrollbar;
        public bool HasHorizontalScrollbar;
        public int HoveredRow = -1;
        public int HoveredCol = -1;
        public int HoveredHeaderCol = -1;
        public int HoveredTearHandleCol = -1;
        public int HoveredResizeCol = -1;
        public int HoveredRowDragHandle = -1;
        public int HoveredRowAddBelow = -1;
        public int SelStartRow = -1;
        public int SelStartCol = -1;
        public int SelEndRow = -1;
        public int SelEndCol = -1;
        public bool IsDragging;
        public readonly HashSet<int> SelectedRows = new();
        public int LastClickedRow = -1;
        public int ActiveRow = -1;
        public int ActiveCol = -1;
        public int SelectedHeaderCol = -1;
        public int ResizingColIndex = -1;
        public float ResizeStartMouseX;
        public float ResizeStartWidth;
        public float ResizeCurrentWidth;
        public int ColumnDragSourceCol = -1;
        public int ColumnDragTargetInsertIndex = -1;
        public float ColumnDragStartMouseX;
        public bool IsColumnDragging;
        public int RowDragSourceIndex = -1;
        public int RowDragTargetInsertIndex = -1;
        public float RowDragStartMouseY;
        public bool IsRowDragging;
        public int ContextRowIndex = -1;
        public int ContextColIndex = -1;
        public int InlineRenameColIndex = -1;
        public int RenameColBufferLength;
        public bool InlineRenameNeedsFocus;
        public bool InlineRenameSelectAll;
        public bool ShowEditFormulaDialog;
        public int EditFormulaBufferLength;
        public int EditFormulaColIndex = -1;
        public EditFormulaTargetKind EditFormulaTargetKind;
        public string EditFormulaCellRowId = "";
        public string EditFormulaCellTableId = "";
        public bool ShowEditVectorCellDialog;
        public string EditVectorCellTableId = "";
        public string EditVectorCellRowId = "";
        public string EditVectorCellColumnId = "";
        public int EditVectorCellXBufferLength;
        public int EditVectorCellYBufferLength;
        public int EditVectorCellZBufferLength;
        public int EditVectorCellWBufferLength;
        public string EditVectorCellValidationMessage = "";
        public bool ShowEditRelationDialog;
        public int EditRelationColIndex = -1;
        public int EditRelationTargetModeIndex;
        public int EditRelationTableIndex = -1;
        public int EditRelationVariantIndex = -1;
        public int EditRelationDisplayColumnIndex = -1;
        public bool ShowEditNumberColumnDialog;
        public string EditNumberTableId = "";
        public string EditNumberColumnId = "";
        public int EditNumberTypeIndex;
        public int EditNumberMinBufferLength;
        public int EditNumberMaxBufferLength;
        public bool ShowEditSubtableDisplayDialog;
        public string EditSubtableDisplayTableId = "";
        public string EditSubtableDisplayColumnId = "";
        public int EditSubtableDisplayRendererIndex;
        public string EditSubtableDisplayPluginSettingsRendererId = "";
        public string EditSubtableDisplayPluginSettingsJson = "";
        public int EditSubtableDisplayPreviewQualityIndex;
        public bool EditSubtableDisplayUseCustomWidth;
        public bool EditSubtableDisplayUseCustomHeight;
        public float EditSubtableDisplayWidthValue = SubtableDisplayMinPreviewWidth;
        public float EditSubtableDisplayHeightValue = SubtableDisplayMinPreviewHeight;
        public bool ShowAddSubtableColumnDialog;
        public string AddSubtableColumnTableId = "";
        public DocColumn? AddSubtableColumnSnapshot;
        public int AddSubtableColumnModeIndex;
        public int AddSubtableColumnExistingTableIndex = -1;
        public string AddSubtableColumnValidationMessage = "";
        public bool ShowEditMeshPreviewDialog;
        public string EditMeshPreviewTableId = "";
        public string EditMeshPreviewRowId = "";
        public string EditMeshPreviewColumnId = "";
        public float EditMeshPreviewOrbitYawDegrees = DocModelPreviewSettings.DefaultOrbitYawDegrees;
        public float EditMeshPreviewOrbitPitchDegrees = DocModelPreviewSettings.DefaultOrbitPitchDegrees;
        public float EditMeshPreviewPanX = DocModelPreviewSettings.DefaultPanX;
        public float EditMeshPreviewPanY = DocModelPreviewSettings.DefaultPanY;
        public float EditMeshPreviewZoom = DocModelPreviewSettings.DefaultZoom;
        public string EditMeshPreviewTextureRelativePath = "";
        public readonly char[] RenameColBuffer = new char[128];
        public readonly char[] EditFormulaBuffer = new char[256];
        public readonly char[] EditVectorCellXBuffer = new char[64];
        public readonly char[] EditVectorCellYBuffer = new char[64];
        public readonly char[] EditVectorCellZBuffer = new char[64];
        public readonly char[] EditVectorCellWBuffer = new char[64];
        public readonly char[] EditNumberMinBuffer = new char[64];
        public readonly char[] EditNumberMaxBuffer = new char[64];
    }

    private sealed class SubtablePreviewEditorContext : IDerpDocEditorContext
    {
        private DocWorkspace _workspace = null!;
        private string _parentRowColumnId = "";
        private string _parentRowId = "";

        public void Configure(DocWorkspace workspace, string? parentRowColumnId, string? parentRowId)
        {
            _workspace = workspace;
            _parentRowColumnId = parentRowColumnId ?? "";
            _parentRowId = parentRowId ?? "";
        }

        public string WorkspaceRoot => _workspace.WorkspaceRoot;

        public string? ProjectPath => _workspace.ProjectPath;

        public int ProjectRevision => _workspace.ProjectRevision;

        public int LiveValueRevision => _workspace.LiveValueRevision;

        public int SelectedRowIndex
        {
            get => _workspace.SelectedRowIndex;
            set => _workspace.SelectedRowIndex = value;
        }

        public DocProject Project => _workspace.Project;

        public DocTable? ActiveTable => _workspace.ActiveTable;

        public DocView? ActiveTableView => _workspace.ActiveTableView;

        public DocDocument? ActiveDocument => _workspace.ActiveDocument;

        public bool IsDirty => _workspace.IsDirty;

        public int[]? ComputeViewRowIndices(DocTable table, DocView? view)
        {
            int[]? baseIndices = _workspace.ComputeViewRowIndices(table, view);
            if (string.IsNullOrWhiteSpace(_parentRowColumnId) ||
                string.IsNullOrWhiteSpace(_parentRowId) ||
                !table.IsSubtable ||
                !string.Equals(table.ParentRowColumnId, _parentRowColumnId, StringComparison.Ordinal))
            {
                return baseIndices;
            }

            return FilterByParentRow(
                _workspace,
                table,
                view,
                baseIndices,
                _parentRowId,
                _parentRowColumnId);
        }

        public string ResolveRelationDisplayLabel(DocColumn relationColumn, string relationRowId)
        {
            return _workspace.ResolveRelationDisplayLabel(relationColumn, relationRowId);
        }

        public string ResolveRelationDisplayLabel(string relationTableId, string relationRowId)
        {
            return _workspace.ResolveRelationDisplayLabel(relationTableId, relationRowId);
        }

        public bool TryGetGlobalPluginSetting(string key, out string value)
        {
            return _workspace.TryGetGlobalPluginSetting(key, out value);
        }

        public bool SetGlobalPluginSetting(string key, string value)
        {
            return _workspace.SetGlobalPluginSetting(key, value);
        }

        public bool RemoveGlobalPluginSetting(string key)
        {
            return _workspace.RemoveGlobalPluginSetting(key);
        }

        public bool TryGetProjectPluginSetting(string key, out string value)
        {
            return _workspace.TryGetProjectPluginSetting(key, out value);
        }

        public bool SetProjectPluginSetting(string key, string value)
        {
            return _workspace.SetProjectPluginSetting(key, value);
        }

        public bool RemoveProjectPluginSetting(string key)
        {
            return _workspace.RemoveProjectPluginSetting(key);
        }

        public bool SetColumnPluginSettings(DocTable table, DocColumn column, string? pluginSettingsJson)
        {
            return _workspace.SetColumnPluginSettings(table, column, pluginSettingsJson);
        }

        public void SetStatusMessage(string statusMessage)
        {
            _workspace.SetStatusMessage(statusMessage);
        }
    }

    private sealed class SelectOptionEditEntry
    {
        public int EntryId;
        public string OriginalValue = "";
        public string Value = "";
        public bool IsNew;
    }

    // =====================================================================
    //  Public API
    // =====================================================================

    public static void Draw(DocWorkspace workspace)
    {
        var table = workspace.ActiveTable;
        if (table == null)
        {
            Im.LabelText("No table selected");
            return;
        }

        int selectedVariantId = workspace.GetSelectedVariantIdForTable(table);
        DocTable variantTable = workspace.ResolveTableForVariant(table, selectedVariantId);
        bool interactive = true;
        int previousRenderVariantId = _activeRenderVariantId;
        _activeRenderVariantId = selectedVariantId;
        DrawInternal(workspace, variantTable, Im.WindowContentRect, interactive, showTableTitleRow: true);
        _activeRenderVariantId = previousRenderVariantId;
    }

    public static void Draw(DocWorkspace workspace, ImRect contentRect)
    {
        var table = workspace.ActiveTable;
        if (table == null)
        {
            Im.LabelText("No table selected");
            return;
        }

        int selectedVariantId = workspace.GetSelectedVariantIdForTable(table);
        DocTable variantTable = workspace.ResolveTableForVariant(table, selectedVariantId);
        bool interactive = true;
        int previousRenderVariantId = _activeRenderVariantId;
        _activeRenderVariantId = selectedVariantId;
        DrawInternal(workspace, variantTable, contentRect, interactive, showTableTitleRow: true);
        _activeRenderVariantId = previousRenderVariantId;
    }

    public static void DrawEmbedded(
        DocWorkspace workspace,
        DocTable table,
        ImRect contentRect,
        bool interactive,
        string stateKey,
        DocView? view = null,
        DocBlock? tableInstanceBlock = null)
    {
        int tableVariantId = tableInstanceBlock?.TableVariantId ?? DocTableVariant.BaseVariantId;
        DocTable variantTable = workspace.ResolveTableForVariant(table, tableVariantId);
        bool effectiveInteractive = interactive;

        if (string.IsNullOrWhiteSpace(stateKey))
        {
            _embeddedView = view;
            _embeddedTableInstanceBlock = tableInstanceBlock;
            int previousRenderVariantId = _activeRenderVariantId;
            _activeRenderVariantId = tableVariantId;
            DrawInternal(workspace, variantTable, contentRect, effectiveInteractive, showTableTitleRow: true);
            _activeRenderVariantId = previousRenderVariantId;
            _embeddedView = null;
            _embeddedTableInstanceBlock = null;
            return;
        }

        _activeEmbeddedStateKey = stateKey;
        _embeddedView = view;
        _embeddedTableInstanceBlock = tableInstanceBlock;
        LoadEmbeddedViewState(stateKey);

        // If we have an active inline cell edit for this embedded spreadsheet, keep it interactive
        // so focus-loss commits (and derived recomputes) happen immediately.
        if (!effectiveInteractive &&
            workspace.EditState.IsEditing &&
            string.Equals(workspace.EditState.TableId, table.Id, StringComparison.Ordinal) &&
            string.Equals(workspace.EditState.OwnerStateKey, stateKey, StringComparison.Ordinal))
        {
            effectiveInteractive = true;
        }

        int previousVariantId = _activeRenderVariantId;
        _activeRenderVariantId = tableVariantId;
        DrawInternal(workspace, variantTable, contentRect, effectiveInteractive, showTableTitleRow: true);
        _activeRenderVariantId = previousVariantId;
        SaveEmbeddedViewState(stateKey);
        _activeEmbeddedStateKey = "";
        _embeddedView = null;
        _embeddedTableInstanceBlock = null;

        if (!IsAnySpreadsheetContextMenuOpen() &&
            string.Equals(_contextMenuOwnerStateKey, stateKey, StringComparison.Ordinal))
        {
            _contextMenuOwnerStateKey = "";
        }
    }

    public static void DrawContentTab(
        DocWorkspace workspace,
        DocTable table,
        DocView? view,
        ImRect contentRect,
        bool interactive,
        int tableVariantId,
        string stateKey)
    {
        if (string.IsNullOrWhiteSpace(stateKey))
        {
            int selectedVariantId = workspace.GetSelectedVariantIdForTable(table);
            DocTable variantTable = workspace.ResolveTableForVariant(table, selectedVariantId);
            bool effectiveInteractive = interactive;
            int previousRenderVariantId = _activeRenderVariantId;
            _activeRenderVariantId = selectedVariantId;
            DrawInternal(workspace, variantTable, contentRect, effectiveInteractive, showTableTitleRow: true);
            _activeRenderVariantId = previousRenderVariantId;
            return;
        }

        DocTable variantTableForTab = workspace.ResolveTableForVariant(table, tableVariantId);
        bool effectiveInteractiveForTab = interactive;

        _activeEmbeddedStateKey = stateKey;
        _embeddedView = view;
        _embeddedTableInstanceBlock = null;
        LoadEmbeddedViewState(stateKey);

        if (!effectiveInteractiveForTab &&
            workspace.EditState.IsEditing &&
            string.Equals(workspace.EditState.TableId, table.Id, StringComparison.Ordinal) &&
            string.Equals(workspace.EditState.OwnerStateKey, stateKey, StringComparison.Ordinal))
        {
            effectiveInteractiveForTab = true;
        }

        int previousVariantId = _activeRenderVariantId;
        _activeRenderVariantId = tableVariantId;
        DrawInternal(workspace, variantTableForTab, contentRect, effectiveInteractiveForTab, showTableTitleRow: true);
        _activeRenderVariantId = previousVariantId;

        SaveEmbeddedViewState(stateKey);
        _activeEmbeddedStateKey = "";
        _embeddedView = null;
        _embeddedTableInstanceBlock = null;

        if (!IsAnySpreadsheetContextMenuOpen() &&
            string.Equals(_contextMenuOwnerStateKey, stateKey, StringComparison.Ordinal))
        {
            _contextMenuOwnerStateKey = "";
        }
    }

    public static bool IsAnySpreadsheetOverlayOpen()
    {
        if (_showEditFormulaDialog ||
            _showEditRelationDialog ||
            _showEditNumberColumnDialog ||
            _showAddSubtableColumnDialog ||
            _showEditSubtableDisplayDialog ||
            _showEditSelectColumnDialog ||
            _showEditMeshPreviewDialog)
        {
            return true;
        }

        if (IsAnySpreadsheetContextMenuOpen())
        {
            return true;
        }

        if (_cellTypeaheadPopupVisible)
        {
            return true;
        }

        if (_splinePopoverActive)
        {
            return true;
        }

        if (ImModal.IsAnyOpen || Im.IsAnyDropdownOpen)
        {
            return true;
        }

        return false;
    }

    public static bool ShouldKeepEmbeddedInteractive(string stateKey)
    {
        if (string.IsNullOrWhiteSpace(stateKey))
        {
            return false;
        }

        if (IsAnySpreadsheetContextMenuOpen() &&
            string.Equals(_contextMenuOwnerStateKey, stateKey, StringComparison.Ordinal))
        {
            return true;
        }

        if (_embeddedViewStates.TryGetValue(stateKey, out var state) &&
            (state.ShowEditFormulaDialog ||
             state.ShowEditRelationDialog ||
             state.ShowEditNumberColumnDialog ||
             state.ShowAddSubtableColumnDialog ||
             state.ShowEditSubtableDisplayDialog ||
             state.ShowEditMeshPreviewDialog))
        {
            return true;
        }

        if (_splinePopoverActive &&
            string.Equals(_splinePopoverOwnerStateKey, stateKey, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    public static bool CanEmbeddedConsumeWheel(string stateKey, Vector2 mousePos, float scrollDeltaY, float scrollDeltaX)
    {
        return TryGetEmbeddedWheelConsumeDistance(stateKey, mousePos, scrollDeltaY, scrollDeltaX, out _);
    }

    public static bool TryGetEmbeddedWheelConsumeDistance(
        string stateKey,
        Vector2 mousePos,
        float scrollDeltaY,
        float scrollDeltaX,
        out float distanceToChosenScrollbar)
    {
        distanceToChosenScrollbar = float.PositiveInfinity;
        if (string.IsNullOrWhiteSpace(stateKey) ||
            !_embeddedViewStates.TryGetValue(stateKey, out var state))
        {
            return false;
        }

        float wheelAmount = MathF.Abs(scrollDeltaX) > MathF.Abs(scrollDeltaY)
            ? scrollDeltaX
            : scrollDeltaY;
        if (wheelAmount == 0f)
        {
            return false;
        }

        float columnViewportWidth = Math.Max(0f, state.ScrollableColumnsViewportWidth);
        float maxHorizontalScroll = Math.Max(0f, state.ColumnContentWidth - columnViewportWidth);
        float maxVerticalScroll = Math.Max(0f, state.RowContentHeight - state.BodyRectHeight);
        bool canScrollHorizontally = state.HasHorizontalScrollbar && maxHorizontalScroll > 0f;
        bool canScrollVertically = state.HasVerticalScrollbar && maxVerticalScroll > 0f;
        if (!canScrollHorizontally && !canScrollVertically)
        {
            return false;
        }

        var horizontalScrollbarRect = GetHorizontalScrollbarRect(state, columnViewportWidth);
        var verticalScrollbarRect = GetVerticalScrollbarRect(state);
        bool routeToHorizontal = ShouldRouteWheelToHorizontalScrollbar(
            mousePos,
            canScrollHorizontally,
            canScrollVertically,
            horizontalScrollbarRect,
            verticalScrollbarRect);

        if (routeToHorizontal && canScrollHorizontally)
        {
            if (WouldWheelMoveOffset(state.ScrollX, wheelAmount, HorizontalScrollWheelSpeed, maxHorizontalScroll))
            {
                distanceToChosenScrollbar = DistanceFromPointToRect(mousePos, horizontalScrollbarRect);
                return true;
            }

            return false;
        }

        if (canScrollVertically)
        {
            if (WouldWheelMoveOffset(state.ScrollY, wheelAmount, RowHeight * 3f, maxVerticalScroll))
            {
                distanceToChosenScrollbar = DistanceFromPointToRect(mousePos, verticalScrollbarRect);
                return true;
            }

            return false;
        }

        if (canScrollHorizontally)
        {
            if (WouldWheelMoveOffset(state.ScrollX, wheelAmount, HorizontalScrollWheelSpeed, maxHorizontalScroll))
            {
                distanceToChosenScrollbar = DistanceFromPointToRect(mousePos, horizontalScrollbarRect);
                return true;
            }

            return false;
        }

        return false;
    }

    public static void SuppressEmbeddedWheelForStateThisFrame(string stateKey)
    {
        if (string.IsNullOrWhiteSpace(stateKey))
        {
            return;
        }

        _wheelSuppressedStateKey = stateKey;
        _wheelSuppressedFrame = Im.Context.FrameCount;
    }

    private static bool TryGetHoveredSubtableEmbeddedGridWheelConsumeDistance(
        DocWorkspace workspace,
        DocTable table,
        Vector2 mousePos,
        float scrollDeltaY,
        float scrollDeltaX,
        out string embeddedStateKey,
        out float distanceToChosenScrollbar)
    {
        embeddedStateKey = "";
        distanceToChosenScrollbar = float.PositiveInfinity;

        if (_hoveredRow < 0 || _hoveredRow >= _rowCount || _hoveredCol < 0 || _hoveredCol >= _colCount)
        {
            return false;
        }

        DocColumn column = GetVisibleColumn(table, _hoveredCol);
        if (column.Kind != DocColumnKind.Subtable)
        {
            return false;
        }

        string normalizedRendererId = NormalizeSubtableDisplayRendererId(column.SubtableDisplayRendererId);
        if (string.IsNullOrWhiteSpace(normalizedRendererId) ||
            !TryResolveSubtableDisplayRendererKind(normalizedRendererId, out var rendererKind, out _) ||
            rendererKind != SubtableDisplayRendererKind.Grid)
        {
            return false;
        }

        DocSubtablePreviewQuality previewQuality = ResolveEffectiveSubtableDisplayPreviewQuality(workspace, column);
        if (previewQuality != DocSubtablePreviewQuality.Full)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(column.SubtableId))
        {
            return false;
        }

        DocTable? childTable = FindTableById(workspace, column.SubtableId);
        if (childTable == null)
        {
            return false;
        }

        int sourceRowIndex = GetSourceRowIndex(_hoveredRow);
        if (sourceRowIndex < 0 || sourceRowIndex >= table.Rows.Count)
        {
            return false;
        }

        var hoveredCellRect = GetCellRect(_hoveredRow, _hoveredCol);
        float availableWidth = Math.Max(0f, hoveredCellRect.Width - (CellPaddingX * 2f));
        float availableHeight = Math.Max(0f, hoveredCellRect.Height - (CellPaddingY * 2f));
        if (availableWidth <= 1f || availableHeight <= 1f)
        {
            return false;
        }

        float previewWidth = ResolveSubtableDisplayPreviewWidth(column, availableWidth);
        float previewHeight =
            previewQuality == DocSubtablePreviewQuality.Full &&
            rendererKind == SubtableDisplayRendererKind.Grid &&
            !IsRenderingSubtableEmbeddedGrid()
                ? availableHeight
                : Math.Min(availableHeight, ResolveSubtableDisplayPreviewHeight(workspace, column));
        if (previewWidth <= 1f || previewHeight <= 1f)
        {
            return false;
        }

        float previewX = hoveredCellRect.X + CellPaddingX;
        float previewY = hoveredCellRect.Y + CellPaddingY + ((availableHeight - previewHeight) * 0.5f);
        var previewRect = new ImRect(previewX, previewY, previewWidth, previewHeight);
        if (!previewRect.Contains(mousePos))
        {
            return false;
        }

        string parentRowId = ResolveSubtableParentRowId(workspace, table, column, sourceRowIndex);
        if (string.IsNullOrEmpty(parentRowId))
        {
            parentRowId = table.Rows[sourceRowIndex].Id;
        }

        embeddedStateKey = GetOrCreateSubtableEmbeddedStateKey(parentRowId, column.Id);

        if (!_subtableEmbeddedGridStateByKey.TryGetValue(embeddedStateKey, out var embeddedState))
        {
            return false;
        }

        float wheelAmount = MathF.Abs(scrollDeltaX) > MathF.Abs(scrollDeltaY)
            ? scrollDeltaX
            : scrollDeltaY;
        if (wheelAmount == 0f)
        {
            return false;
        }

        float maxHorizontalScroll = Math.Max(0f, embeddedState.ColumnContentWidth - embeddedState.ScrollableColumnsViewportWidth);
        float maxVerticalScroll = Math.Max(0f, embeddedState.RowContentHeight - embeddedState.BodyRectHeight);
        bool canScrollHorizontally = embeddedState.HasHorizontalScrollbar && maxHorizontalScroll > 0f;
        bool canScrollVertically = embeddedState.HasVerticalScrollbar && maxVerticalScroll > 0f;
        if (!canScrollHorizontally && !canScrollVertically)
        {
            return false;
        }

        float columnViewportWidth = Math.Max(0f, embeddedState.ScrollableColumnsViewportWidth);
        var horizontalScrollbarRect = new ImRect(
            embeddedState.BodyRectX + RowNumberWidth + embeddedState.PinnedColumnsWidth,
            embeddedState.BodyRectY + embeddedState.BodyRectHeight,
            columnViewportWidth,
            HorizontalScrollbarHeight);
        var verticalScrollbarRect = new ImRect(
            embeddedState.GridRectX + embeddedState.GridRectWidth - ScrollbarWidth,
            embeddedState.BodyRectY,
            ScrollbarWidth,
            embeddedState.BodyRectHeight);

        bool routeToHorizontal = ShouldRouteWheelToHorizontalScrollbar(
            mousePos,
            canScrollHorizontally,
            canScrollVertically,
            horizontalScrollbarRect,
            verticalScrollbarRect);

        if (routeToHorizontal && canScrollHorizontally)
        {
            if (WouldWheelMoveOffset(embeddedState.ScrollX, wheelAmount, HorizontalScrollWheelSpeed, maxHorizontalScroll))
            {
                distanceToChosenScrollbar = DistanceFromPointToRect(mousePos, horizontalScrollbarRect);
                return true;
            }

            return false;
        }

        if (canScrollVertically)
        {
            if (WouldWheelMoveOffset(embeddedState.ScrollY, wheelAmount, RowHeight * 3f, maxVerticalScroll))
            {
                distanceToChosenScrollbar = DistanceFromPointToRect(mousePos, verticalScrollbarRect);
                return true;
            }

            return false;
        }

        if (canScrollHorizontally)
        {
            if (WouldWheelMoveOffset(embeddedState.ScrollX, wheelAmount, HorizontalScrollWheelSpeed, maxHorizontalScroll))
            {
                distanceToChosenScrollbar = DistanceFromPointToRect(mousePos, horizontalScrollbarRect);
                return true;
            }

            return false;
        }

        return false;
    }

    private static bool IsMouseOverHoveredSubtableEmbeddedGrid(DocWorkspace workspace, DocTable table, Vector2 mousePos)
    {
        if (_hoveredRow < 0 || _hoveredRow >= _rowCount || _hoveredCol < 0 || _hoveredCol >= _colCount)
        {
            return false;
        }

        DocColumn column = GetVisibleColumn(table, _hoveredCol);
        if (column.Kind != DocColumnKind.Subtable)
        {
            return false;
        }

        string normalizedRendererId = NormalizeSubtableDisplayRendererId(column.SubtableDisplayRendererId);
        if (string.IsNullOrWhiteSpace(normalizedRendererId) ||
            !TryResolveSubtableDisplayRendererKind(normalizedRendererId, out var rendererKind, out _) ||
            rendererKind != SubtableDisplayRendererKind.Grid)
        {
            return false;
        }

        DocSubtablePreviewQuality previewQuality = ResolveEffectiveSubtableDisplayPreviewQuality(workspace, column);
        if (previewQuality != DocSubtablePreviewQuality.Full)
        {
            return false;
        }

        var hoveredCellRect = GetCellRect(_hoveredRow, _hoveredCol);
        float availableWidth = Math.Max(0f, hoveredCellRect.Width - (CellPaddingX * 2f));
        float availableHeight = Math.Max(0f, hoveredCellRect.Height - (CellPaddingY * 2f));
        if (availableWidth <= 1f || availableHeight <= 1f)
        {
            return false;
        }

        float previewWidth = ResolveSubtableDisplayPreviewWidth(column, availableWidth);
        float previewHeight =
            previewQuality == DocSubtablePreviewQuality.Full &&
            rendererKind == SubtableDisplayRendererKind.Grid &&
            !IsRenderingSubtableEmbeddedGrid()
                ? availableHeight
                : Math.Min(availableHeight, ResolveSubtableDisplayPreviewHeight(workspace, column));
        if (previewWidth <= 1f || previewHeight <= 1f)
        {
            return false;
        }

        float previewX = hoveredCellRect.X + CellPaddingX;
        float previewY = hoveredCellRect.Y + CellPaddingY + ((availableHeight - previewHeight) * 0.5f);
        var previewRect = new ImRect(previewX, previewY, previewWidth, previewHeight);
        return previewRect.Contains(mousePos);
    }

    public static float MeasureEmbeddedHeight(
        DocTable table,
        float width,
        DocWorkspace? workspace = null,
        DocView? view = null,
        DocBlock? tableInstanceBlock = null)
    {
        float safeWidth = Math.Max(0f, width);
        float availableColumnViewportWidth = Math.Max(0f, safeWidth - RowNumberWidth);
        Span<int> visibleColumnMap = stackalloc int[32];
        Span<float> visibleColumnWidths = stackalloc float[32];
        float totalFixedWidth = 0f;
        int autoColumnCount = 0;
        int visibleCount = 0;
        for (int i = 0; i < table.Columns.Count && visibleCount < 32; i++)
        {
            if (table.Columns[i].IsHidden)
            {
                continue;
            }

            visibleColumnMap[visibleCount] = i;
            visibleCount++;
            float columnWidth = table.Columns[i].Width;
            if (columnWidth > 0f)
            {
                totalFixedWidth += columnWidth;
            }
            else
            {
                autoColumnCount++;
            }
        }

        float autoColumnWidth = autoColumnCount > 0
            ? Math.Max(MinColumnWidth, (availableColumnViewportWidth - totalFixedWidth) / autoColumnCount)
            : 0f;
        float columnContentWidth = 0f;
        for (int columnIndex = 0; columnIndex < visibleCount; columnIndex++)
        {
            int tableColumnIndex = visibleColumnMap[columnIndex];
            float rawColumnWidth = table.Columns[tableColumnIndex].Width > 0f
                ? table.Columns[tableColumnIndex].Width
                : autoColumnWidth;
            float columnWidth = Math.Max(MinColumnWidth, rawColumnWidth);
            visibleColumnWidths[columnIndex] = columnWidth;
            columnContentWidth += columnWidth;
        }

        columnContentWidth += GetAddColumnSlotWidth(table);

        int[]? viewRowIndices = workspace?.ComputeViewRowIndices(table, view, tableInstanceBlock);
        int rowCount = viewRowIndices?.Length ?? table.Rows.Count;

        float rowContentHeight = 0f;
        float fontSize = Im.Style.FontSize;
        for (int ri = 0; ri < rowCount; ri++)
        {
            int rowIndex = viewRowIndices != null ? viewRowIndices[ri] : ri;
            var row = table.Rows[rowIndex];
            float rowHeight = RowHeight;
            for (int columnIndex = 0; columnIndex < visibleCount; columnIndex++)
            {
                var column = table.Columns[visibleColumnMap[columnIndex]];
                var cell = row.GetCell(column);
                float minimumRowHeight = GetMinimumRowHeightForCell(null, table, row, rowIndex, visibleColumnWidths[columnIndex], column, cell);
                if (minimumRowHeight > rowHeight)
                {
                    rowHeight = minimumRowHeight;
                }

                if (!IsCellTextWrapped(column))
                {
                    continue;
                }

                float maxTextWidth = Math.Max(0f, visibleColumnWidths[columnIndex] - (CellPaddingX * 2f));
                if (maxTextWidth <= 1f)
                {
                    continue;
                }

                int lineCount = GetCellWrappedLineCount(null, column, cell, maxTextWidth, fontSize);
                if (lineCount <= 1)
                {
                    continue;
                }

                float wrappedHeight = (CellPaddingY * 2f) + (lineCount * GetWrappedLineHeight(fontSize));
                if (wrappedHeight > rowHeight)
                {
                    rowHeight = wrappedHeight;
                }
            }

            rowContentHeight += rowHeight;
        }

        bool needsHorizontalScrollbar = columnContentWidth > availableColumnViewportWidth + 0.5f;
        float titleRowSpace = TableTitleRowHeight + TableTitleBottomSpacing;
        float measuredHeight = titleRowSpace + HeaderHeight + rowContentHeight + (needsHorizontalScrollbar ? HorizontalScrollbarHeight : 0f);
        return Math.Max(titleRowSpace + HeaderHeight, measuredHeight);
    }

    public static float MeasureEmbeddedWidth(DocTable table, float maxWidth)
    {
        float safeMaxWidth = Math.Max(0f, maxWidth);
        float columnContentWidth = 0f;
        int visibleCount = 0;

        for (int i = 0; i < table.Columns.Count && visibleCount < 32; i++)
        {
            if (table.Columns[i].IsHidden) continue;
            visibleCount++;
            float rawColumnWidth = table.Columns[i].Width > 0f
                ? table.Columns[i].Width
                : MinColumnWidth;
            columnContentWidth += Math.Max(MinColumnWidth, rawColumnWidth);
        }

        if (visibleCount <= 0)
        {
            columnContentWidth = MinColumnWidth;
        }

        columnContentWidth += GetAddColumnSlotWidth(table);

        float measuredWidth = RowNumberWidth + columnContentWidth;
        return Math.Min(measuredWidth, safeMaxWidth);
    }

    private static void DrawInternal(
        DocWorkspace workspace,
        DocTable table,
        ImRect contentRect,
        bool interactive,
        bool showTableTitleRow)
    {
        if (contentRect.Width <= 0 || contentRect.Height <= 0)
        {
            return;
        }

        bool pushedId = false;
        if (!string.IsNullOrWhiteSpace(_activeEmbeddedStateKey))
        {
            Im.Context.PushId(_activeEmbeddedStateKey);
            pushedId = true;
        }

        try
        {
        var windowContentRect = Im.WindowContentRect;
        if (windowContentRect.Width > 0f && windowContentRect.Height > 0f)
        {
            _dialogBoundsRect = windowContentRect;
        }
        else
        {
            _dialogBoundsRect = contentRect;
        }

        if (_inlineRenameTableActive &&
            (!showTableTitleRow || !string.Equals(_inlineRenameTableId, table.Id, StringComparison.Ordinal)))
        {
            CancelInlineTableRename();
        }

        _isInteractiveRender = interactive;
        BeginSubtablePreviewFrame(workspace);
        if (showTableTitleRow)
        {
            DrawTableTitleRow(workspace, table, contentRect, interactive);
            float consumedHeight = TableTitleRowHeight + TableTitleBottomSpacing;
            float gridHeight = Math.Max(0f, contentRect.Height - consumedHeight);
            _gridRect = new ImRect(contentRect.X, contentRect.Y + consumedHeight, contentRect.Width, gridHeight);
        }
        else
        {
            _gridRect = contentRect;
        }

        if (_gridRect.Width <= 0f || _gridRect.Height <= 0f)
        {
            return;
        }

        ComputeLayout(workspace, table);
        ValidateInlineRenameColumn(table);

        if (interactive)
        {
            var input = Im.Context.Input;
            HandleInput(workspace, table, input);
        }

        DrawHeaders(table);
        Im.PushClipRect(_bodyRect);
        DrawBody(workspace, table);
        DrawSelection();
        DrawStickyColumnsShadow(_bodyRect.Y, _bodyRect.Height);
        Im.PopClipRect();
        if (!table.IsDerived)
            DrawRowAddBelowOverlay();
        DrawScrollbars(workspace, table);
        if (interactive)
        {
            DrawEditOverlay(workspace, table);
            DrawContextMenus(workspace, table);
            DrawDialogs(workspace, table);
            DrawSplinePopover(workspace, table);
        }
        }
        finally
        {
            if (pushedId)
            {
                Im.Context.PopId();
            }
        }
    }

    private static void BeginSubtablePreviewFrame(DocWorkspace workspace)
    {
        int frame = Im.Context.FrameCount;
        if (_subtablePreviewDrawFrame == frame)
        {
            return;
        }

        _subtablePreviewDrawFrame = frame;
        _subtablePreviewFrameUsage = 0;
        _subtablePreviewFrameBudget = Math.Clamp(
            workspace.UserPreferences.SubtablePreviewFrameBudget,
            DocUserPreferences.MinSubtablePreviewFrameBudget,
            DocUserPreferences.MaxSubtablePreviewFrameBudget);
        EnsureSubtableLookupCachesForFrame(frame);
    }

    private static void EnsureSubtableLookupCachesForFrame(int frame)
    {
        if (_subtableLookupCacheFrame == frame)
        {
            return;
        }

        _subtableLookupCacheFrame = frame;
        _subtableTableLookupCache.Clear();
        _subtablePreviewViewLookupCache.Clear();
    }

    private static void ResetViewState()
    {
        _scrollY = 0f;
        _scrollX = 0f;
        _hoveredRow = -1;
        _hoveredCol = -1;
        _hoveredHeaderCol = -1;
        _hoveredTearHandleCol = -1;
        _hoveredResizeCol = -1;
        _hoveredRowDragHandle = -1;
        _hoveredRowAddBelow = -1;
        _selStartRow = -1;
        _selStartCol = -1;
        _selEndRow = -1;
        _selEndCol = -1;
        _isDragging = false;
        _selectedRows.Clear();
        _lastClickedRow = -1;
        _activeRow = -1;
        _activeCol = -1;
        _selectedHeaderCol = -1;
        _resizingColIndex = -1;
        _resizeStartMouseX = 0f;
        _resizeStartWidth = 0f;
        _resizeCurrentWidth = 0f;
        _columnDragSourceCol = -1;
        _columnDragTargetInsertIndex = -1;
        _columnDragStartMouseX = 0f;
        _isColumnDragging = false;
        _rowDragSourceIndex = -1;
        _rowDragTargetInsertIndex = -1;
        _rowDragStartMouseY = 0f;
        _isRowDragging = false;
        _contextRowIndex = -1;
        _contextColIndex = -1;
        _inlineRenameColIndex = -1;
        _renameColBufferLength = 0;
        _inlineRenameNeedsFocus = false;
        _inlineRenameSelectAll = false;
        _showEditFormulaDialog = false;
        _editFormulaDialogOpenedFrame = -1;
        _editFormulaBufferLength = 0;
        _editFormulaColIndex = -1;
        _editFormulaTargetKind = EditFormulaTargetKind.Column;
        _editFormulaCellRowId = "";
        _editFormulaCellTableId = "";
        _showEditVectorCellDialog = false;
        _editVectorCellDialogOpenedFrame = -1;
        _editVectorCellTableId = "";
        _editVectorCellRowId = "";
        _editVectorCellColumnId = "";
        _editVectorCellXBufferLength = 0;
        _editVectorCellYBufferLength = 0;
        _editVectorCellZBufferLength = 0;
        _editVectorCellWBufferLength = 0;
        _editVectorCellValidationMessage = "";
        Array.Clear(_editVectorCellXBuffer);
        Array.Clear(_editVectorCellYBuffer);
        Array.Clear(_editVectorCellZBuffer);
        Array.Clear(_editVectorCellWBuffer);
        _showEditRelationDialog = false;
        _editRelationDialogOpenedFrame = -1;
        _editRelationColIndex = -1;
        _editRelationTargetModeIndex = 0;
        _editRelationTableIndex = -1;
        _editRelationVariantIndex = -1;
        _editRelationDisplayColumnIndex = -1;
        _showEditNumberColumnDialog = false;
        _editNumberColumnDialogOpenedFrame = -1;
        _editNumberTableId = "";
        _editNumberColumnId = "";
        _editNumberTypeIndex = 0;
        _editNumberMinBufferLength = 0;
        _editNumberMaxBufferLength = 0;
        _editNumberValidationMessage = "";
        Array.Clear(_editNumberMinBuffer);
        Array.Clear(_editNumberMaxBuffer);
        _showEditSubtableDisplayDialog = false;
        _editSubtableDisplayDialogOpenedFrame = -1;
        _editSubtableDisplayTableId = "";
        _editSubtableDisplayColumnId = "";
        _editSubtableDisplayRendererIndex = 0;
        _editSubtableDisplayValidationMessage = "";
        _editSubtableDisplayPluginSettingsRendererId = "";
        _editSubtableDisplayPluginSettingsJson = null;
        _editSubtableDisplayPreviewQualityIndex = 0;
        _editSubtableDisplayUseCustomWidth = false;
        _editSubtableDisplayUseCustomHeight = false;
        _editSubtableDisplayWidthValue = SubtableDisplayMinPreviewWidth;
        _editSubtableDisplayHeightValue = SubtableDisplayMinPreviewHeight;
        _showAddSubtableColumnDialog = false;
        _addSubtableColumnDialogOpenedFrame = -1;
        _addSubtableColumnTableId = "";
        _addSubtableColumnSnapshot = null;
        _addSubtableColumnModeIndex = 0;
        _addSubtableColumnExistingTableIndex = -1;
        _addSubtableColumnValidationMessage = "";
        _showEditSelectColumnDialog = false;
        _editSelectColumnDialogOpenedFrame = -1;
        _editSelectTableId = "";
        _editSelectColumnId = "";
        _editSelectSelectedIndex = -1;
        _editSelectDragIndex = -1;
        _editSelectDragTargetIndex = -1;
        _editSelectDragMouseOffsetY = 0f;
        _editSelectScrollY = 0f;
        _editSelectInlineRenameNeedsFocus = false;
        _editSelectRenameBufferLength = 0;
        _editSelectAddBufferLength = 0;
        _editSelectEntries.Clear();
        _editSelectOriginalValuesById.Clear();
        _editSelectNextEntryId = 1;
        _showEditMeshPreviewDialog = false;
        _editMeshPreviewTableId = "";
        _editMeshPreviewRowId = "";
        _editMeshPreviewColumnId = "";
        _editMeshPreviewDraft = new DocModelPreviewSettings();
        _editMeshPreviewTexturePathBufferLength = 0;
        Array.Clear(_editMeshPreviewTexturePathBuffer);
        ResetEditMeshPreviewViewportPreviewState();
    }

    private static void LoadEmbeddedViewState(string stateKey)
    {
        if (!_embeddedViewStates.TryGetValue(stateKey, out var state))
        {
            ResetViewState();
            return;
        }

        _scrollY = state.ScrollY;
        _scrollX = state.ScrollX;
        _hoveredRow = state.HoveredRow;
        _hoveredCol = state.HoveredCol;
        _hoveredHeaderCol = state.HoveredHeaderCol;
        _hoveredTearHandleCol = state.HoveredTearHandleCol;
        _hoveredResizeCol = state.HoveredResizeCol;
        _hoveredRowDragHandle = state.HoveredRowDragHandle;
        _hoveredRowAddBelow = state.HoveredRowAddBelow;
        _selStartRow = state.SelStartRow;
        _selStartCol = state.SelStartCol;
        _selEndRow = state.SelEndRow;
        _selEndCol = state.SelEndCol;
        _isDragging = state.IsDragging;
        _selectedRows.Clear();
        foreach (int selectedRow in state.SelectedRows)
        {
            _selectedRows.Add(selectedRow);
        }

        _lastClickedRow = state.LastClickedRow;
        _activeRow = state.ActiveRow;
        _activeCol = state.ActiveCol;
        _selectedHeaderCol = state.SelectedHeaderCol;
        _resizingColIndex = state.ResizingColIndex;
        _resizeStartMouseX = state.ResizeStartMouseX;
        _resizeStartWidth = state.ResizeStartWidth;
        _resizeCurrentWidth = state.ResizeCurrentWidth;
        _columnDragSourceCol = state.ColumnDragSourceCol;
        _columnDragTargetInsertIndex = state.ColumnDragTargetInsertIndex;
        _columnDragStartMouseX = state.ColumnDragStartMouseX;
        _isColumnDragging = state.IsColumnDragging;
        _rowDragSourceIndex = state.RowDragSourceIndex;
        _rowDragTargetInsertIndex = state.RowDragTargetInsertIndex;
        _rowDragStartMouseY = state.RowDragStartMouseY;
        _isRowDragging = state.IsRowDragging;
        _contextRowIndex = state.ContextRowIndex;
        _contextColIndex = state.ContextColIndex;
        _inlineRenameColIndex = state.InlineRenameColIndex;
        _renameColBufferLength = state.RenameColBufferLength;
        _inlineRenameNeedsFocus = state.InlineRenameNeedsFocus;
        _inlineRenameSelectAll = state.InlineRenameSelectAll;
        _showEditFormulaDialog = state.ShowEditFormulaDialog;
        _editFormulaBufferLength = state.EditFormulaBufferLength;
        _editFormulaColIndex = state.EditFormulaColIndex;
        _editFormulaTargetKind = state.EditFormulaTargetKind;
        _editFormulaCellRowId = state.EditFormulaCellRowId ?? "";
        _editFormulaCellTableId = state.EditFormulaCellTableId ?? "";
        _showEditVectorCellDialog = state.ShowEditVectorCellDialog;
        _editVectorCellTableId = state.EditVectorCellTableId ?? "";
        _editVectorCellRowId = state.EditVectorCellRowId ?? "";
        _editVectorCellColumnId = state.EditVectorCellColumnId ?? "";
        _editVectorCellXBufferLength = state.EditVectorCellXBufferLength;
        _editVectorCellYBufferLength = state.EditVectorCellYBufferLength;
        _editVectorCellZBufferLength = state.EditVectorCellZBufferLength;
        _editVectorCellWBufferLength = state.EditVectorCellWBufferLength;
        _editVectorCellValidationMessage = state.EditVectorCellValidationMessage ?? "";
        _showEditRelationDialog = state.ShowEditRelationDialog;
        _editRelationColIndex = state.EditRelationColIndex;
        _editRelationTargetModeIndex = state.EditRelationTargetModeIndex;
        _editRelationTableIndex = state.EditRelationTableIndex;
        _editRelationVariantIndex = state.EditRelationVariantIndex;
        _editRelationDisplayColumnIndex = state.EditRelationDisplayColumnIndex;
        _showEditNumberColumnDialog = state.ShowEditNumberColumnDialog;
        _editNumberTableId = state.EditNumberTableId ?? "";
        _editNumberColumnId = state.EditNumberColumnId ?? "";
        _editNumberTypeIndex = state.EditNumberTypeIndex;
        _editNumberMinBufferLength = state.EditNumberMinBufferLength;
        _editNumberMaxBufferLength = state.EditNumberMaxBufferLength;
        _editNumberValidationMessage = "";
        _showEditSubtableDisplayDialog = state.ShowEditSubtableDisplayDialog;
        _editSubtableDisplayTableId = state.EditSubtableDisplayTableId ?? "";
        _editSubtableDisplayColumnId = state.EditSubtableDisplayColumnId ?? "";
        _editSubtableDisplayRendererIndex = state.EditSubtableDisplayRendererIndex;
        _editSubtableDisplayValidationMessage = "";
        _editSubtableDisplayPluginSettingsRendererId = state.EditSubtableDisplayPluginSettingsRendererId ?? "";
        _editSubtableDisplayPluginSettingsJson = string.IsNullOrWhiteSpace(state.EditSubtableDisplayPluginSettingsJson)
            ? null
            : state.EditSubtableDisplayPluginSettingsJson;
        _editSubtableDisplayPreviewQualityIndex = Math.Clamp(
            state.EditSubtableDisplayPreviewQualityIndex,
            0,
            _subtableDisplayPreviewQualityOptionNames.Length - 1);
        _editSubtableDisplayUseCustomWidth = state.EditSubtableDisplayUseCustomWidth;
        _editSubtableDisplayUseCustomHeight = state.EditSubtableDisplayUseCustomHeight;
        _editSubtableDisplayWidthValue = Math.Clamp(
            state.EditSubtableDisplayWidthValue,
            SubtableDisplayMinPreviewWidth,
            SubtableDisplayMaxPreviewSize);
        _editSubtableDisplayHeightValue = Math.Clamp(
            state.EditSubtableDisplayHeightValue,
            SubtableDisplayMinPreviewHeight,
            SubtableDisplayMaxPreviewSize);
        _showAddSubtableColumnDialog = state.ShowAddSubtableColumnDialog;
        _addSubtableColumnDialogOpenedFrame = -1;
        _addSubtableColumnTableId = state.AddSubtableColumnTableId ?? "";
        _addSubtableColumnSnapshot = state.AddSubtableColumnSnapshot;
        _addSubtableColumnModeIndex = Math.Clamp(state.AddSubtableColumnModeIndex, 0, _addSubtableColumnModeNames.Length - 1);
        _addSubtableColumnExistingTableIndex = state.AddSubtableColumnExistingTableIndex;
        _addSubtableColumnValidationMessage = state.AddSubtableColumnValidationMessage ?? "";
        _showEditMeshPreviewDialog = state.ShowEditMeshPreviewDialog;
        _editMeshPreviewTableId = state.EditMeshPreviewTableId ?? "";
        _editMeshPreviewRowId = state.EditMeshPreviewRowId ?? "";
        _editMeshPreviewColumnId = state.EditMeshPreviewColumnId ?? "";
        _editMeshPreviewDraft = new DocModelPreviewSettings
        {
            OrbitYawDegrees = state.EditMeshPreviewOrbitYawDegrees,
            OrbitPitchDegrees = state.EditMeshPreviewOrbitPitchDegrees,
            PanX = state.EditMeshPreviewPanX,
            PanY = state.EditMeshPreviewPanY,
            Zoom = state.EditMeshPreviewZoom,
            TextureRelativePath = string.IsNullOrWhiteSpace(state.EditMeshPreviewTextureRelativePath)
                ? null
                : state.EditMeshPreviewTextureRelativePath,
        };
        _editMeshPreviewDraft.ClampInPlace();
        SyncEditMeshPreviewTexturePathBufferFromDraft();
        ResetEditMeshPreviewViewportPreviewState();
        _showEditSelectColumnDialog = false;
        _editSelectTableId = "";
        _editSelectColumnId = "";
        _editSelectSelectedIndex = -1;
        _editSelectDragIndex = -1;
        _editSelectDragTargetIndex = -1;
        _editSelectDragMouseOffsetY = 0f;
        _editSelectScrollY = 0f;
        _editSelectInlineRenameNeedsFocus = false;
        _editSelectRenameBufferLength = 0;
        _editSelectAddBufferLength = 0;
        _editSelectEntries.Clear();
        _editSelectOriginalValuesById.Clear();
        _editSelectNextEntryId = 1;

        Array.Clear(_renameColBuffer);
        state.RenameColBuffer.AsSpan(0, Math.Min(state.RenameColBufferLength, _renameColBuffer.Length)).CopyTo(_renameColBuffer);
        Array.Clear(_editFormulaBuffer);
        state.EditFormulaBuffer.AsSpan(0, Math.Min(state.EditFormulaBufferLength, _editFormulaBuffer.Length)).CopyTo(_editFormulaBuffer);
        Array.Clear(_editVectorCellXBuffer);
        state.EditVectorCellXBuffer.AsSpan(0, Math.Min(state.EditVectorCellXBufferLength, _editVectorCellXBuffer.Length)).CopyTo(_editVectorCellXBuffer);
        Array.Clear(_editVectorCellYBuffer);
        state.EditVectorCellYBuffer.AsSpan(0, Math.Min(state.EditVectorCellYBufferLength, _editVectorCellYBuffer.Length)).CopyTo(_editVectorCellYBuffer);
        Array.Clear(_editVectorCellZBuffer);
        state.EditVectorCellZBuffer.AsSpan(0, Math.Min(state.EditVectorCellZBufferLength, _editVectorCellZBuffer.Length)).CopyTo(_editVectorCellZBuffer);
        Array.Clear(_editVectorCellWBuffer);
        state.EditVectorCellWBuffer.AsSpan(0, Math.Min(state.EditVectorCellWBufferLength, _editVectorCellWBuffer.Length)).CopyTo(_editVectorCellWBuffer);
        Array.Clear(_editNumberMinBuffer);
        state.EditNumberMinBuffer.AsSpan(0, Math.Min(state.EditNumberMinBufferLength, _editNumberMinBuffer.Length)).CopyTo(_editNumberMinBuffer);
        Array.Clear(_editNumberMaxBuffer);
        state.EditNumberMaxBuffer.AsSpan(0, Math.Min(state.EditNumberMaxBufferLength, _editNumberMaxBuffer.Length)).CopyTo(_editNumberMaxBuffer);
    }

    private static void SaveEmbeddedViewState(string stateKey)
    {
        if (!_embeddedViewStates.TryGetValue(stateKey, out var state))
        {
            state = new EmbeddedSpreadsheetViewState();
            _embeddedViewStates[stateKey] = state;
        }

        state.ScrollY = _scrollY;
        state.ScrollX = _scrollX;
        state.GridRectX = _gridRect.X;
        state.GridRectY = _gridRect.Y;
        state.GridRectWidth = _gridRect.Width;
        state.GridRectHeight = _gridRect.Height;
        state.BodyRectX = _bodyRect.X;
        state.BodyRectY = _bodyRect.Y;
        state.BodyRectWidth = _bodyRect.Width;
        state.BodyRectHeight = _bodyRect.Height;
        state.PinnedColumnsWidth = _pinnedColumnsWidth;
        state.ScrollableColumnsViewportWidth = _scrollableColumnsViewportWidth;
        state.ColumnContentWidth = _columnContentWidth;
        state.RowContentHeight = _rowContentHeight;
        state.HasVerticalScrollbar = _hasVerticalScrollbar;
        state.HasHorizontalScrollbar = _hasHorizontalScrollbar;
        state.HoveredRow = _hoveredRow;
        state.HoveredCol = _hoveredCol;
        state.HoveredHeaderCol = _hoveredHeaderCol;
        state.HoveredTearHandleCol = _hoveredTearHandleCol;
        state.HoveredResizeCol = _hoveredResizeCol;
        state.HoveredRowDragHandle = _hoveredRowDragHandle;
        state.HoveredRowAddBelow = _hoveredRowAddBelow;
        state.SelStartRow = _selStartRow;
        state.SelStartCol = _selStartCol;
        state.SelEndRow = _selEndRow;
        state.SelEndCol = _selEndCol;
        state.IsDragging = _isDragging;
        state.SelectedRows.Clear();
        foreach (int selectedRow in _selectedRows)
        {
            state.SelectedRows.Add(selectedRow);
        }

        state.LastClickedRow = _lastClickedRow;
        state.ActiveRow = _activeRow;
        state.ActiveCol = _activeCol;
        state.SelectedHeaderCol = _selectedHeaderCol;
        state.ResizingColIndex = _resizingColIndex;
        state.ResizeStartMouseX = _resizeStartMouseX;
        state.ResizeStartWidth = _resizeStartWidth;
        state.ResizeCurrentWidth = _resizeCurrentWidth;
        state.ColumnDragSourceCol = _columnDragSourceCol;
        state.ColumnDragTargetInsertIndex = _columnDragTargetInsertIndex;
        state.ColumnDragStartMouseX = _columnDragStartMouseX;
        state.IsColumnDragging = _isColumnDragging;
        state.RowDragSourceIndex = _rowDragSourceIndex;
        state.RowDragTargetInsertIndex = _rowDragTargetInsertIndex;
        state.RowDragStartMouseY = _rowDragStartMouseY;
        state.IsRowDragging = _isRowDragging;
        state.ContextRowIndex = _contextRowIndex;
        state.ContextColIndex = _contextColIndex;
        state.InlineRenameColIndex = _inlineRenameColIndex;
        state.RenameColBufferLength = _renameColBufferLength;
        state.InlineRenameNeedsFocus = _inlineRenameNeedsFocus;
        state.InlineRenameSelectAll = _inlineRenameSelectAll;
        state.ShowEditFormulaDialog = _showEditFormulaDialog;
        state.EditFormulaBufferLength = _editFormulaBufferLength;
        state.EditFormulaColIndex = _editFormulaColIndex;
        state.EditFormulaTargetKind = _editFormulaTargetKind;
        state.EditFormulaCellRowId = _editFormulaCellRowId;
        state.EditFormulaCellTableId = _editFormulaCellTableId;
        state.ShowEditVectorCellDialog = _showEditVectorCellDialog;
        state.EditVectorCellTableId = _editVectorCellTableId;
        state.EditVectorCellRowId = _editVectorCellRowId;
        state.EditVectorCellColumnId = _editVectorCellColumnId;
        state.EditVectorCellXBufferLength = _editVectorCellXBufferLength;
        state.EditVectorCellYBufferLength = _editVectorCellYBufferLength;
        state.EditVectorCellZBufferLength = _editVectorCellZBufferLength;
        state.EditVectorCellWBufferLength = _editVectorCellWBufferLength;
        state.EditVectorCellValidationMessage = _editVectorCellValidationMessage;
        state.ShowEditRelationDialog = _showEditRelationDialog;
        state.EditRelationColIndex = _editRelationColIndex;
        state.EditRelationTargetModeIndex = _editRelationTargetModeIndex;
        state.EditRelationTableIndex = _editRelationTableIndex;
        state.EditRelationVariantIndex = _editRelationVariantIndex;
        state.EditRelationDisplayColumnIndex = _editRelationDisplayColumnIndex;
        state.ShowEditNumberColumnDialog = _showEditNumberColumnDialog;
        state.EditNumberTableId = _editNumberTableId;
        state.EditNumberColumnId = _editNumberColumnId;
        state.EditNumberTypeIndex = _editNumberTypeIndex;
        state.EditNumberMinBufferLength = _editNumberMinBufferLength;
        state.EditNumberMaxBufferLength = _editNumberMaxBufferLength;
        state.ShowEditSubtableDisplayDialog = _showEditSubtableDisplayDialog;
        state.EditSubtableDisplayTableId = _editSubtableDisplayTableId;
        state.EditSubtableDisplayColumnId = _editSubtableDisplayColumnId;
        state.EditSubtableDisplayRendererIndex = _editSubtableDisplayRendererIndex;
        state.EditSubtableDisplayPluginSettingsRendererId = _editSubtableDisplayPluginSettingsRendererId;
        state.EditSubtableDisplayPluginSettingsJson = _editSubtableDisplayPluginSettingsJson ?? "";
        state.EditSubtableDisplayPreviewQualityIndex = _editSubtableDisplayPreviewQualityIndex;
        state.EditSubtableDisplayUseCustomWidth = _editSubtableDisplayUseCustomWidth;
        state.EditSubtableDisplayUseCustomHeight = _editSubtableDisplayUseCustomHeight;
        state.EditSubtableDisplayWidthValue = Math.Clamp(
            _editSubtableDisplayWidthValue,
            SubtableDisplayMinPreviewWidth,
            SubtableDisplayMaxPreviewSize);
        state.EditSubtableDisplayHeightValue = Math.Clamp(
            _editSubtableDisplayHeightValue,
            SubtableDisplayMinPreviewHeight,
            SubtableDisplayMaxPreviewSize);
        state.ShowAddSubtableColumnDialog = _showAddSubtableColumnDialog;
        state.AddSubtableColumnTableId = _addSubtableColumnTableId;
        state.AddSubtableColumnSnapshot = _addSubtableColumnSnapshot;
        state.AddSubtableColumnModeIndex = _addSubtableColumnModeIndex;
        state.AddSubtableColumnExistingTableIndex = _addSubtableColumnExistingTableIndex;
        state.AddSubtableColumnValidationMessage = _addSubtableColumnValidationMessage;
        state.ShowEditMeshPreviewDialog = _showEditMeshPreviewDialog;
        state.EditMeshPreviewTableId = _editMeshPreviewTableId;
        state.EditMeshPreviewRowId = _editMeshPreviewRowId;
        state.EditMeshPreviewColumnId = _editMeshPreviewColumnId;
        state.EditMeshPreviewOrbitYawDegrees = _editMeshPreviewDraft.OrbitYawDegrees;
        state.EditMeshPreviewOrbitPitchDegrees = _editMeshPreviewDraft.OrbitPitchDegrees;
        state.EditMeshPreviewPanX = _editMeshPreviewDraft.PanX;
        state.EditMeshPreviewPanY = _editMeshPreviewDraft.PanY;
        state.EditMeshPreviewZoom = _editMeshPreviewDraft.Zoom;
        state.EditMeshPreviewTextureRelativePath = _editMeshPreviewDraft.TextureRelativePath ?? "";

        Array.Clear(state.RenameColBuffer);
        _renameColBuffer.AsSpan(0, Math.Min(_renameColBufferLength, state.RenameColBuffer.Length)).CopyTo(state.RenameColBuffer);
        Array.Clear(state.EditFormulaBuffer);
        _editFormulaBuffer.AsSpan(0, Math.Min(_editFormulaBufferLength, state.EditFormulaBuffer.Length)).CopyTo(state.EditFormulaBuffer);
        Array.Clear(state.EditVectorCellXBuffer);
        _editVectorCellXBuffer.AsSpan(0, Math.Min(_editVectorCellXBufferLength, state.EditVectorCellXBuffer.Length)).CopyTo(state.EditVectorCellXBuffer);
        Array.Clear(state.EditVectorCellYBuffer);
        _editVectorCellYBuffer.AsSpan(0, Math.Min(_editVectorCellYBufferLength, state.EditVectorCellYBuffer.Length)).CopyTo(state.EditVectorCellYBuffer);
        Array.Clear(state.EditVectorCellZBuffer);
        _editVectorCellZBuffer.AsSpan(0, Math.Min(_editVectorCellZBufferLength, state.EditVectorCellZBuffer.Length)).CopyTo(state.EditVectorCellZBuffer);
        Array.Clear(state.EditVectorCellWBuffer);
        _editVectorCellWBuffer.AsSpan(0, Math.Min(_editVectorCellWBufferLength, state.EditVectorCellWBuffer.Length)).CopyTo(state.EditVectorCellWBuffer);
        Array.Clear(state.EditNumberMinBuffer);
        _editNumberMinBuffer.AsSpan(0, Math.Min(_editNumberMinBufferLength, state.EditNumberMinBuffer.Length)).CopyTo(state.EditNumberMinBuffer);
        Array.Clear(state.EditNumberMaxBuffer);
        _editNumberMaxBuffer.AsSpan(0, Math.Min(_editNumberMaxBufferLength, state.EditNumberMaxBuffer.Length)).CopyTo(state.EditNumberMaxBuffer);
    }

    // =====================================================================
    //  Layout
    // =====================================================================

    private static void ComputeLayout(DocWorkspace? workspace, DocTable table)
    {
        // Column visibility: use view's VisibleColumnIds if present, else fall back to IsHidden
        var view = _embeddedView ?? workspace?.ActiveTableView;
        var viewColIds = view?.VisibleColumnIds;

        int visibleCount = 0;
        if (viewColIds != null && viewColIds.Count > 0)
        {
            // View specifies exact visible columns in order
            for (int vi = 0; vi < viewColIds.Count && visibleCount < 32; vi++)
            {
                for (int ci = 0; ci < table.Columns.Count; ci++)
                {
                    if (string.Equals(table.Columns[ci].Id, viewColIds[vi], StringComparison.Ordinal))
                    {
                        _visibleColMap[visibleCount++] = ci;
                        break;
                    }
                }
            }
        }
        else
        {
            for (int i = 0; i < table.Columns.Count && visibleCount < 32; i++)
            {
                if (!table.Columns[i].IsHidden)
                {
                    _visibleColMap[visibleCount++] = i;
                }
            }
        }

        for (int columnIndex = 0; columnIndex < _isPinnedCol.Length; columnIndex++)
        {
            _isPinnedCol[columnIndex] = false;
        }

        _colCount = visibleCount;

        // Compute view row indices (filter + sort)
        _viewRowIndices = workspace?.ComputeViewRowIndices(table, view, _embeddedTableInstanceBlock);

        string activeParentRowId = !string.IsNullOrEmpty(_activeParentRowIdOverride)
            ? _activeParentRowIdOverride
            : workspace?.ActiveParentRowId ?? "";

        // Filter by parent row when viewing a child subtable from a specific parent row
        if (workspace != null && table.IsSubtable &&
            !string.IsNullOrEmpty(activeParentRowId) &&
            !string.IsNullOrEmpty(table.ParentRowColumnId))
        {
            _viewRowIndices = FilterByParentRow(workspace, table, view, _viewRowIndices, activeParentRowId, table.ParentRowColumnId);
        }

        _rowCount = _viewRowIndices?.Length ?? table.Rows.Count;

        for (int columnIndex = 0; columnIndex < _colCount; columnIndex++)
        {
            var column = GetVisibleColumn(table, columnIndex);
            bool isPrimaryKey = !string.IsNullOrWhiteSpace(table.Keys.PrimaryKeyColumnId) &&
                                string.Equals(table.Keys.PrimaryKeyColumnId, column.Id, StringComparison.Ordinal);
            bool isSecondaryKey = FindSecondaryKeyIndex(table, column.Id) >= 0;
            _isPinnedCol[columnIndex] = isPrimaryKey || isSecondaryKey;
        }

        _headerRect = new ImRect(_gridRect.X, _gridRect.Y, _gridRect.Width, HeaderHeight);

        bool needsVerticalScrollbar = false;
        bool needsHorizontalScrollbar = false;
        float availableColumnViewportWidth = 0f;
        float bodyHeight = Math.Max(0f, _gridRect.Height - HeaderHeight);
        float computedScrollableContentWidth = 0f;
        float computedPinnedWidth = 0f;

        for (int pass = 0; pass < 3; pass++)
        {
            bodyHeight = Math.Max(0f, _gridRect.Height - HeaderHeight - (needsHorizontalScrollbar ? HorizontalScrollbarHeight : 0f));
            availableColumnViewportWidth = _gridRect.Width - RowNumberWidth - (needsVerticalScrollbar ? ScrollbarWidth : 0f);
            availableColumnViewportWidth = Math.Max(0f, availableColumnViewportWidth);

            float totalFixed = 0f;
            int autoCount = 0;
            for (int columnIndex = 0; columnIndex < _colCount; columnIndex++)
            {
                float width = GetVisibleColumn(table, columnIndex).Width;
                if (width > 0f)
                {
                    totalFixed += width;
                }
                else
                {
                    autoCount++;
                }
            }

            float autoWidth = autoCount > 0
                ? Math.Max(MinColumnWidth, (availableColumnViewportWidth - totalFixed) / autoCount)
                : 0f;
            computedPinnedWidth = 0f;
            computedScrollableContentWidth = GetAddColumnSlotWidth(table);
            for (int columnIndex = 0; columnIndex < _colCount; columnIndex++)
            {
                float rawWidth = GetVisibleColumn(table, columnIndex).Width;
                float width = rawWidth > 0f ? rawWidth : autoWidth;
                width = Math.Max(MinColumnWidth, width);
                _colW[columnIndex] = width;
                if (_isPinnedCol[columnIndex])
                {
                    computedPinnedWidth += width;
                }
                else
                {
                    computedScrollableContentWidth += width;
                }
            }

            ComputeRowLayoutMetrics(workspace, table, view);

            float scrollableViewportWidth = Math.Max(0f, availableColumnViewportWidth - computedPinnedWidth);
            bool nextHorizontal = computedScrollableContentWidth > scrollableViewportWidth + 0.5f;
            float nextBodyHeight = Math.Max(0f, _gridRect.Height - HeaderHeight - (nextHorizontal ? HorizontalScrollbarHeight : 0f));
            bool nextVertical = _rowContentHeight > nextBodyHeight + 0.5f;

            if (nextHorizontal == needsHorizontalScrollbar && nextVertical == needsVerticalScrollbar)
            {
                needsHorizontalScrollbar = nextHorizontal;
                needsVerticalScrollbar = nextVertical;
                bodyHeight = nextBodyHeight;
                availableColumnViewportWidth = _gridRect.Width - RowNumberWidth - (needsVerticalScrollbar ? ScrollbarWidth : 0f);
                availableColumnViewportWidth = Math.Max(0f, availableColumnViewportWidth);
                break;
            }

            needsHorizontalScrollbar = nextHorizontal;
            needsVerticalScrollbar = nextVertical;
        }

        _hasHorizontalScrollbar = needsHorizontalScrollbar;
        _hasVerticalScrollbar = needsVerticalScrollbar;
        _pinnedColumnsWidth = Math.Min(computedPinnedWidth, availableColumnViewportWidth);
        _scrollableColumnsViewportWidth = Math.Max(0f, availableColumnViewportWidth - _pinnedColumnsWidth);
        _columnContentWidth = computedScrollableContentWidth;

        _bodyRect = new ImRect(
            _gridRect.X,
            _gridRect.Y + HeaderHeight,
            _gridRect.Width,
            bodyHeight);

        float maxScrollX = Math.Max(0f, _columnContentWidth - _scrollableColumnsViewportWidth);
        _scrollX = Math.Clamp(_scrollX, 0f, maxScrollX);

        float totalFixedFinal = 0f;
        int autoCountFinal = 0;
        for (int columnIndex = 0; columnIndex < _colCount; columnIndex++)
        {
            float width = GetVisibleColumn(table, columnIndex).Width;
            if (width > 0f)
            {
                totalFixedFinal += width;
            }
            else
            {
                autoCountFinal++;
            }
        }

        float autoWidthFinal = autoCountFinal > 0
            ? Math.Max(MinColumnWidth, (availableColumnViewportWidth - totalFixedFinal) / autoCountFinal)
            : 0f;
        float pinnedX = _bodyRect.X + RowNumberWidth;
        float scrollingX = _bodyRect.X + RowNumberWidth + _pinnedColumnsWidth - _scrollX;
        for (int columnIndex = 0; columnIndex < _colCount; columnIndex++)
        {
            float rawWidth = GetVisibleColumn(table, columnIndex).Width;
            float width = rawWidth > 0f ? rawWidth : autoWidthFinal;
            width = Math.Max(MinColumnWidth, width);
            _colW[columnIndex] = width;
            if (_isPinnedCol[columnIndex])
            {
                _colX[columnIndex] = pinnedX;
                pinnedX += width;
            }
            else
            {
                _colX[columnIndex] = scrollingX;
                scrollingX += width;
            }
        }
        _addColumnSlotX = scrollingX;

        ComputeRowLayoutMetrics(workspace, table, view);
        float maxScrollY = Math.Max(0f, _rowContentHeight - _bodyRect.Height);
        _scrollY = Math.Clamp(_scrollY, 0f, maxScrollY);
    }

    private static void EnsureRowLayoutCapacity(int rowCount)
    {
        if (_rowHeights.Length >= rowCount)
        {
            return;
        }

        int newCapacity = _rowHeights.Length == 0 ? rowCount : _rowHeights.Length;
        while (newCapacity < rowCount)
        {
            newCapacity = Math.Max(rowCount, newCapacity * 2);
        }

        _rowHeights = new float[newCapacity];
        _rowOffsets = new float[newCapacity];
    }

    private static float GetWrappedLineHeight(float fontSize)
    {
        return fontSize + WrappedTextLineSpacing;
    }

    private static bool IsCellTextWrapped(DocColumn column)
    {
        string columnTypeId = ResolveColumnTypeId(column);
        if (!DocColumnTypeIdMapper.IsBuiltIn(columnTypeId))
        {
            if (ColumnUiPluginRegistry.TryGet(columnTypeId, out var uiPlugin) && uiPlugin.IsTextWrappedByDefault)
            {
                return true;
            }

            if (ColumnTypeDefinitionRegistry.TryGet(columnTypeId, out var columnTypeDefinition) &&
                columnTypeDefinition.IsTextWrappedByDefault)
            {
                return true;
            }
        }

        return column.Kind == DocColumnKind.Id ||
               column.Kind == DocColumnKind.Text ||
               column.Kind == DocColumnKind.Select ||
               column.Kind == DocColumnKind.Formula ||
               column.Kind == DocColumnKind.Relation ||
               column.Kind == DocColumnKind.TableRef;
    }

    private static bool IsAssetColumnKind(DocColumnKind columnKind)
    {
        return columnKind == DocColumnKind.TextureAsset ||
               columnKind == DocColumnKind.MeshAsset ||
               columnKind == DocColumnKind.AudioAsset ||
               columnKind == DocColumnKind.UiAsset;
    }

    private static string? ResolveAssetRootForColumnKind(DocWorkspace workspace, DocColumnKind columnKind)
    {
        if (columnKind == DocColumnKind.UiAsset &&
            !string.IsNullOrWhiteSpace(workspace.GameRoot))
        {
            return workspace.GameRoot;
        }

        return workspace.AssetsRoot;
    }

    private static string ResolveColumnTypeId(DocColumn column)
    {
        return DocColumnTypeIdMapper.Resolve(column.ColumnTypeId, column.Kind);
    }

    private static bool TryGetColumnUiPlugin(DocColumn column, out IDerpDocColumnUiPlugin plugin)
    {
        string columnTypeId = ResolveColumnTypeId(column);
        if (DocColumnTypeIdMapper.IsBuiltIn(columnTypeId))
        {
            plugin = null!;
            return false;
        }

        return ColumnUiPluginRegistry.TryGet(columnTypeId, out plugin!);
    }

    private static float GetMinimumRowHeightForCell(
        DocWorkspace? workspace,
        DocTable table,
        DocRow row,
        int sourceRowIndex,
        float displayColumnWidth,
        DocColumn column,
        DocCellValue cell,
        bool allowFullHeightSubtableCells = true)
    {
        float minimumHeight = IsAssetColumnKind(column.Kind)
            ? AssetCellMinHeight
            : RowHeight;

        if (column.Kind == DocColumnKind.Subtable &&
            IsSubtableDisplayPreviewEnabled(workspace, column))
        {
            float previewHeight = workspace != null
                ? ResolveSubtableDisplayPreviewHeight(workspace, table, row, sourceRowIndex, displayColumnWidth, column, allowFullHeightSubtableCells)
                : ResolveSubtableDisplayPreviewHeight(column);
            minimumHeight = MathF.Max(minimumHeight, previewHeight + (CellPaddingY * 2f));
        }

        string columnTypeId = ResolveColumnTypeId(column);
        if (DocColumnTypeIdMapper.IsBuiltIn(columnTypeId))
        {
            return minimumHeight;
        }

        if (ColumnTypeDefinitionRegistry.TryGet(columnTypeId, out var definition) &&
            definition.MinimumRowHeight.HasValue)
        {
            minimumHeight = MathF.Max(minimumHeight, definition.MinimumRowHeight.Value);
        }

        if (workspace != null && ColumnUiPluginRegistry.TryGet(columnTypeId, out var uiPlugin))
        {
            minimumHeight = MathF.Max(
                minimumHeight,
                uiPlugin.GetMinimumRowHeight(
                    workspace,
                    table,
                    row,
                    sourceRowIndex,
                    column,
                    cell,
                    minimumHeight));
        }

        return minimumHeight;
    }

    private static void ComputeRowLayoutMetrics(DocWorkspace? workspace, DocTable table, DocView? view)
    {
        int rowCount = _viewRowIndices?.Length ?? table.Rows.Count;
        float fontSize = Im.Style.FontSize;
        int columnSignature = ComputeRowLayoutColumnSignature(table);
        int rowMapIdentity = _viewRowIndices != null ? RuntimeHelpers.GetHashCode(_viewRowIndices) : 0;
        int projectRevision = workspace?.ProjectRevision ?? -1;
        int previewQualitySignature = workspace != null ? (int)workspace.UserPreferences.SubtablePreviewQuality : -1;
        string viewId = view?.Id ?? "";
        string parentRowId = !string.IsNullOrEmpty(_activeParentRowIdOverride)
            ? _activeParentRowIdOverride
            : workspace?.ActiveParentRowId ?? "";

        if (_rowLayoutCacheProjectRevision == projectRevision &&
            string.Equals(_rowLayoutCacheTableId, table.Id, StringComparison.Ordinal) &&
            string.Equals(_rowLayoutCacheViewId, viewId, StringComparison.Ordinal) &&
            string.Equals(_rowLayoutCacheParentRowId, parentRowId, StringComparison.Ordinal) &&
            _rowLayoutCacheColumnSignature == columnSignature &&
            _rowLayoutCacheRowMapIdentity == rowMapIdentity &&
            _rowLayoutCacheRowCount == rowCount &&
            _rowLayoutCacheColCount == _colCount &&
            _rowLayoutCachePreviewQualitySignature == previewQualitySignature &&
            MathF.Abs(_rowLayoutCacheFontSize - fontSize) < 0.001f)
        {
            _rowCount = rowCount;
            return;
        }

        _rowCount = rowCount;
        EnsureRowLayoutCapacity(rowCount);

        _rowContentHeight = 0f;
        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            _rowOffsets[rowIndex] = _rowContentHeight;
            float rowHeight = RowHeight;
            int sourceRowIndex = GetSourceRowIndex(rowIndex);
            var row = table.Rows[sourceRowIndex];
            for (int columnIndex = 0; columnIndex < _colCount; columnIndex++)
            {
                var column = GetVisibleColumn(table, columnIndex);
                var cell = row.GetCell(column);
                float minimumRowHeight = GetMinimumRowHeightForCell(workspace, table, row, sourceRowIndex, _colW[columnIndex], column, cell);
                if (minimumRowHeight > rowHeight)
                {
                    rowHeight = minimumRowHeight;
                }

                if (!IsCellTextWrapped(column))
                {
                    continue;
                }

                float maxTextWidth = Math.Max(0f, _colW[columnIndex] - (CellPaddingX * 2f));
                if (maxTextWidth <= 1f)
                {
                    continue;
                }

                int wrappedLineCount = GetCellWrappedLineCount(workspace, column, cell, maxTextWidth, fontSize);
                if (wrappedLineCount <= 1)
                {
                    continue;
                }

                float wrappedHeight = (CellPaddingY * 2f) + (wrappedLineCount * GetWrappedLineHeight(fontSize));
                if (wrappedHeight > rowHeight)
                {
                    rowHeight = wrappedHeight;
                }
            }

            _rowHeights[rowIndex] = rowHeight;
            _rowContentHeight += rowHeight;
        }

        _rowLayoutCacheProjectRevision = projectRevision;
        _rowLayoutCacheTableId = table.Id;
        _rowLayoutCacheViewId = viewId;
        _rowLayoutCacheParentRowId = parentRowId;
        _rowLayoutCacheColumnSignature = columnSignature;
        _rowLayoutCacheRowMapIdentity = rowMapIdentity;
        _rowLayoutCacheRowCount = rowCount;
        _rowLayoutCacheColCount = _colCount;
        _rowLayoutCachePreviewQualitySignature = previewQualitySignature;
        _rowLayoutCacheFontSize = fontSize;
    }

    private static int ComputeRowLayoutColumnSignature(DocTable table)
    {
        var hash = new HashCode();
        hash.Add(_colCount);
        for (int columnIndex = 0; columnIndex < _colCount; columnIndex++)
        {
            var column = GetVisibleColumn(table, columnIndex);
            hash.Add(column.Id, StringComparer.Ordinal);
            hash.Add(column.Kind);
            hash.Add(column.ColumnTypeId, StringComparer.OrdinalIgnoreCase);
            hash.Add(column.PluginSettingsJson, StringComparer.Ordinal);
            hash.Add(column.SubtableDisplayRendererId, StringComparer.Ordinal);
            hash.Add(column.SubtableDisplayCellWidth);
            hash.Add(column.SubtableDisplayCellHeight);
            hash.Add(column.SubtableDisplayPreviewQuality);
            hash.Add((int)MathF.Round(_colW[columnIndex] * 100f));
        }

        return hash.ToHashCode();
    }

    private static int GetCellWrappedLineCount(
        DocWorkspace? workspace,
        DocColumn column,
        DocCellValue cell,
        float maxTextWidth,
        float fontSize)
    {
        if (maxTextWidth <= 1f)
        {
            return 1;
        }

        string columnTypeId = ResolveColumnTypeId(column);
        if (!DocColumnTypeIdMapper.IsBuiltIn(columnTypeId))
        {
            string textValue = cell.StringValue ?? "";
            return CountWrappedTextLines(textValue.AsSpan(), maxTextWidth, fontSize);
        }

        switch (column.Kind)
        {
            case DocColumnKind.Id:
            case DocColumnKind.Text:
            case DocColumnKind.Select:
            {
                string textValue = cell.StringValue ?? "";
                return CountWrappedTextLines(textValue.AsSpan(), maxTextWidth, fontSize);
            }
            case DocColumnKind.Formula:
            {
                string textValue = cell.StringValue ?? "";
                if (textValue.Length > 0)
                {
                    return CountWrappedTextLines(textValue.AsSpan(), maxTextWidth, fontSize);
                }

                Span<char> numberBuffer = stackalloc char[32];
                if (cell.NumberValue.TryFormat(numberBuffer, out int written, "G"))
                {
                    return CountWrappedTextLines(numberBuffer[..written], maxTextWidth, fontSize);
                }

                return 1;
            }
            case DocColumnKind.Relation:
            {
                string relationRowId = cell.StringValue ?? "";
                string relationLabel = workspace != null
                    ? workspace.ResolveRelationDisplayLabel(column, relationRowId)
                    : relationRowId;
                return CountWrappedTextLines(relationLabel.AsSpan(), maxTextWidth, fontSize);
            }
            case DocColumnKind.TableRef:
            {
                string tableId = cell.StringValue ?? "";
                string tableLabel = workspace != null
                    ? ResolveTableRefLabel(workspace, tableId)
                    : tableId;
                return CountWrappedTextLines(tableLabel.AsSpan(), maxTextWidth, fontSize);
            }
            default:
                return 1;
        }
    }

    private static int CountWrappedTextLines(ReadOnlySpan<char> text, float maxWidth, float fontSize)
    {
        if (text.Length == 0 || maxWidth <= 1f)
        {
            return 1;
        }

        int totalLineCount = 0;
        int segmentStart = 0;
        while (segmentStart <= text.Length)
        {
            int newlineOffset = segmentStart < text.Length ? text[segmentStart..].IndexOf('\n') : -1;
            int segmentEndExclusive = newlineOffset >= 0 ? segmentStart + newlineOffset : text.Length;
            ReadOnlySpan<char> segment = text[segmentStart..segmentEndExclusive];

            if (segment.Length == 0)
            {
                totalLineCount++;
            }
            else
            {
                int segmentCursor = 0;
                while (segmentCursor < segment.Length)
                {
                    ReadOnlySpan<char> remaining = segment[segmentCursor..];
                    int wrappedLength = FindWrappedLineLength(remaining, maxWidth, fontSize);
                    wrappedLength = Math.Clamp(wrappedLength, 1, remaining.Length);
                    totalLineCount++;
                    segmentCursor += wrappedLength;
                    while (segmentCursor < segment.Length && char.IsWhiteSpace(segment[segmentCursor]))
                    {
                        segmentCursor++;
                    }
                }
            }

            if (newlineOffset < 0)
            {
                break;
            }

            segmentStart = segmentEndExclusive + 1;
            if (segmentStart == text.Length)
            {
                totalLineCount++;
                break;
            }
        }

        return Math.Max(1, totalLineCount);
    }

    private static int FindWrappedLineLength(ReadOnlySpan<char> text, float maxWidth, float fontSize)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        int lastWhitespaceIndex = -1;
        for (int charIndex = 0; charIndex < text.Length; charIndex++)
        {
            if (char.IsWhiteSpace(text[charIndex]))
            {
                lastWhitespaceIndex = charIndex;
            }

            float width = Im.MeasureTextWidth(text[..(charIndex + 1)], fontSize);
            if (width <= maxWidth)
            {
                continue;
            }

            if (lastWhitespaceIndex >= 0)
            {
                return Math.Max(1, lastWhitespaceIndex + 1);
            }

            return Math.Max(1, charIndex);
        }

        return text.Length;
    }

    private static float GetRowOffset(int rowIndex)
    {
        if (rowIndex <= 0)
        {
            return 0f;
        }

        if (rowIndex >= _rowCount)
        {
            return _rowContentHeight;
        }

        return _rowOffsets[rowIndex];
    }

    private static float GetRowHeightAt(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _rowCount)
        {
            return RowHeight;
        }

        return _rowHeights[rowIndex];
    }

    private static float GetRowTopY(int rowIndex)
    {
        return _bodyRect.Y + GetRowOffset(rowIndex) - _scrollY;
    }

    private static int FindRowIndexAtContentOffset(float contentOffset, int rowCount)
    {
        if (rowCount <= 0)
        {
            return -1;
        }

        int low = 0;
        int high = rowCount - 1;
        while (low <= high)
        {
            int mid = low + ((high - low) / 2);
            float rowTop = _rowOffsets[mid];
            float rowBottom = rowTop + _rowHeights[mid];
            if (contentOffset < rowTop)
            {
                high = mid - 1;
            }
            else if (contentOffset >= rowBottom)
            {
                low = mid + 1;
            }
            else
            {
                return mid;
            }
        }

        return Math.Clamp(low, 0, rowCount - 1);
    }

    private static bool TryGetRowFromMouseY(float mouseY, int rowCount, out int rowIndex)
    {
        rowIndex = -1;
        if (rowCount <= 0)
        {
            return false;
        }

        float contentOffsetY = mouseY - _bodyRect.Y + _scrollY;
        if (contentOffsetY < 0f || contentOffsetY >= _rowContentHeight)
        {
            return false;
        }

        rowIndex = FindRowIndexAtContentOffset(contentOffsetY, rowCount);
        return rowIndex >= 0 && rowIndex < rowCount;
    }

    private static void GetVisibleRowRange(int rowCount, out int firstVisible, out int lastVisible)
    {
        firstVisible = 0;
        lastVisible = -1;
        if (rowCount <= 0 || _rowContentHeight <= 0f || _bodyRect.Height <= 0f)
        {
            return;
        }

        float viewTop = Math.Clamp(_scrollY, 0f, Math.Max(0f, _rowContentHeight - 0.0001f));
        float viewBottom = Math.Clamp(_scrollY + _bodyRect.Height, 0f, _rowContentHeight);
        if (viewBottom <= viewTop)
        {
            viewBottom = Math.Min(_rowContentHeight, viewTop + 0.0001f);
        }

        firstVisible = FindRowIndexAtContentOffset(viewTop, rowCount);
        float inclusiveBottom = Math.Max(viewTop, viewBottom - 0.0001f);
        lastVisible = FindRowIndexAtContentOffset(inclusiveBottom, rowCount);
    }

    // =====================================================================
    //  Cell rect — single source of truth
    // =====================================================================

    private static ImRect GetCellRect(int rowIndex, int colIndex)
    {
        float cellX = _colX[colIndex];
        float cellW = _colW[colIndex];
        float cellY = GetRowTopY(rowIndex);
        float cellH = GetRowHeightAt(rowIndex);
        return new ImRect(cellX, cellY, cellW, cellH);
    }

    private static ImRect GetColumnsViewportRect(float y, float height)
    {
        float width = GetColumnsViewportWidth();
        return new ImRect(
            _bodyRect.X + RowNumberWidth,
            y,
            Math.Max(0f, width),
            Math.Max(0f, height));
    }

    private static ImRect GetPinnedColumnsViewportRect(float y, float height)
    {
        float columnsViewportWidth = GetColumnsViewportWidth();
        float pinnedWidth = Math.Min(_pinnedColumnsWidth, columnsViewportWidth);
        return new ImRect(
            _bodyRect.X + RowNumberWidth,
            y,
            Math.Max(0f, pinnedWidth),
            Math.Max(0f, height));
    }

    private static ImRect GetScrollableColumnsViewportRect(float y, float height)
    {
        float columnsViewportWidth = GetColumnsViewportWidth();
        float pinnedWidth = Math.Min(_pinnedColumnsWidth, columnsViewportWidth);
        return new ImRect(
            _bodyRect.X + RowNumberWidth + pinnedWidth,
            y,
            Math.Max(0f, columnsViewportWidth - pinnedWidth),
            Math.Max(0f, height));
    }

    private static float GetColumnsViewportWidth()
    {
        return Math.Max(0f, _bodyRect.Width - RowNumberWidth - (_hasVerticalScrollbar ? ScrollbarWidth : 0f));
    }

    private static bool IsPinnedColumn(int columnIndex)
    {
        return columnIndex >= 0 &&
               columnIndex < _colCount &&
               _isPinnedCol[columnIndex];
    }

    private static bool TryGetVisibleColumnRect(int columnIndex, float y, float height, out ImRect visibleRect)
    {
        visibleRect = default;
        if (columnIndex < 0 || columnIndex >= _colCount)
        {
            return false;
        }

        float left = _colX[columnIndex];
        float right = left + _colW[columnIndex];

        float viewportLeft;
        float viewportRight = _bodyRect.X + RowNumberWidth + GetColumnsViewportWidth();
        if (IsPinnedColumn(columnIndex))
        {
            viewportLeft = _bodyRect.X + RowNumberWidth;
            viewportRight = Math.Min(viewportRight, viewportLeft + _pinnedColumnsWidth);
        }
        else
        {
            viewportLeft = _bodyRect.X + RowNumberWidth + _pinnedColumnsWidth;
        }

        left = Math.Max(left, viewportLeft);
        right = Math.Min(right, viewportRight);
        if (right <= left)
        {
            return false;
        }

        visibleRect = new ImRect(left, y, right - left, Math.Max(0f, height));
        return true;
    }

    private static int FindHoveredResizeColumn(Vector2 mousePos)
    {
        var headerColumnsRect = GetColumnsViewportRect(_headerRect.Y, _headerRect.Height);
        if (!headerColumnsRect.Contains(mousePos))
        {
            return -1;
        }

        for (int columnIndex = 0; columnIndex < _colCount; columnIndex++)
        {
            if (!TryGetVisibleColumnRect(columnIndex, _headerRect.Y, _headerRect.Height, out var visibleRect))
            {
                continue;
            }

            float boundaryX = visibleRect.Right;
            if (MathF.Abs(mousePos.X - boundaryX) <= ColumnResizeGrabHalfWidth)
            {
                return columnIndex;
            }
        }

        return -1;
    }

    private static int FindHoveredHeaderColumn(Vector2 mousePos)
    {
        var headerColumnsRect = GetColumnsViewportRect(_headerRect.Y, _headerRect.Height);
        if (!headerColumnsRect.Contains(mousePos))
        {
            return -1;
        }

        for (int columnIndex = 0; columnIndex < _colCount; columnIndex++)
        {
            if (!TryGetVisibleColumnRect(columnIndex, _headerRect.Y, _headerRect.Height, out var visibleRect))
            {
                continue;
            }

            if (visibleRect.Contains(mousePos))
            {
                return columnIndex;
            }
        }

        return -1;
    }

    private static bool TryGetDisplayColumnFromMouseX(float mouseX, out int columnIndex)
    {
        columnIndex = -1;
        var bodyColumnsRect = GetColumnsViewportRect(_bodyRect.Y, _bodyRect.Height);
        if (!bodyColumnsRect.Contains(new Vector2(mouseX, _bodyRect.Y + 1f)))
        {
            return false;
        }

        for (int candidateColumnIndex = 0; candidateColumnIndex < _colCount; candidateColumnIndex++)
        {
            if (!TryGetVisibleColumnRect(candidateColumnIndex, _bodyRect.Y, _bodyRect.Height, out var visibleRect))
            {
                continue;
            }

            if (mouseX >= visibleRect.X && mouseX < visibleRect.Right)
            {
                columnIndex = candidateColumnIndex;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetDisplayCellFromMouse(Vector2 mousePos, out int rowIndex, out int columnIndex)
    {
        rowIndex = -1;
        columnIndex = -1;

        if (!_bodyRect.Contains(mousePos))
        {
            return false;
        }

        if (!TryGetRowFromMouseY(mousePos.Y, _rowCount, out rowIndex))
        {
            return false;
        }

        return TryGetDisplayColumnFromMouseX(mousePos.X, out columnIndex);
    }

    private static int GetColumnInsertIndexFromMouseX(float mouseX)
    {
        if (_colCount <= 0)
        {
            return 0;
        }

        for (int columnIndex = 0; columnIndex < _colCount; columnIndex++)
        {
            float midpointX = _colX[columnIndex] + (_colW[columnIndex] * 0.5f);
            if (mouseX < midpointX)
            {
                return columnIndex;
            }
        }

        return _colCount;
    }

    private static float GetColumnInsertIndicatorX(int insertIndex)
    {
        if (_colCount <= 0)
        {
            return _bodyRect.X + RowNumberWidth;
        }

        if (insertIndex <= 0)
        {
            return _colX[0];
        }

        if (insertIndex >= _colCount)
        {
            return _colX[_colCount - 1] + _colW[_colCount - 1];
        }

        return _colX[insertIndex];
    }

    private static int RemapColumnIndexAfterMove(int index, int fromIndex, int toIndex)
    {
        if (index < 0 || fromIndex == toIndex || (toIndex == fromIndex + 1))
        {
            return index;
        }

        int insertedIndex = toIndex > fromIndex ? toIndex - 1 : toIndex;
        if (index == fromIndex)
        {
            return insertedIndex;
        }

        if (fromIndex < insertedIndex)
        {
            if (index > fromIndex && index <= insertedIndex)
            {
                return index - 1;
            }
        }
        else if (insertedIndex < fromIndex)
        {
            if (index >= insertedIndex && index < fromIndex)
            {
                return index + 1;
            }
        }

        return index;
    }

    private static void ClearColumnDragState()
    {
        _columnDragSourceCol = -1;
        _columnDragTargetInsertIndex = -1;
        _columnDragStartMouseX = 0f;
        _isColumnDragging = false;
    }

    private static void ClearRowDragState()
    {
        _rowDragSourceIndex = -1;
        _rowDragTargetInsertIndex = -1;
        _rowDragStartMouseY = 0f;
        _isRowDragging = false;
    }

    private static ImRect GetRowDragHandleRect(int rowIndex)
    {
        float rowY = GetRowTopY(rowIndex);
        float rowHeight = GetRowHeightAt(rowIndex);
        float width = (RowHandleDotSize * 2f) + RowHandleDotSpacing;
        float height = (RowHandleDotSize * 3f) + (RowHandleDotSpacing * 2f);
        float x = _bodyRect.X + 4f;
        float y = rowY + (rowHeight - height) * 0.5f;
        return new ImRect(x, y, width, height);
    }

    private static ImRect GetRowAddBelowButtonRect(int rowIndex)
    {
        float rowY = GetRowTopY(rowIndex);
        float rowHeight = GetRowHeightAt(rowIndex);
        float x = _bodyRect.X + (RowNumberWidth - RowAddButtonSize) * 0.5f;
        float y = rowY + rowHeight - (RowAddButtonSize * 0.5f);
        return new ImRect(x, y, RowAddButtonSize, RowAddButtonSize);
    }

    private static bool IsMouseInRowGutter(Vector2 mousePos)
    {
        return mousePos.X >= _bodyRect.X &&
               mousePos.X < _bodyRect.X + RowNumberWidth &&
               mousePos.Y >= _bodyRect.Y &&
               mousePos.Y < _bodyRect.Bottom;
    }

    private static int GetRowInsertIndexFromMouseY(float mouseY, int rowCount)
    {
        if (rowCount <= 0)
        {
            return 0;
        }

        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            float rowY = GetRowTopY(rowIndex);
            float rowMidpointY = rowY + (GetRowHeightAt(rowIndex) * 0.5f);
            if (mouseY < rowMidpointY)
            {
                return rowIndex;
            }
        }

        return rowCount;
    }

    private static float GetRowInsertIndicatorY(int insertIndex, int rowCount)
    {
        if (rowCount <= 0)
        {
            return _bodyRect.Y;
        }

        int clampedInsertIndex = Math.Clamp(insertIndex, 0, rowCount);
        if (clampedInsertIndex <= 0)
        {
            return _bodyRect.Y + GetRowOffset(0) - _scrollY;
        }

        if (clampedInsertIndex >= rowCount)
        {
            return _bodyRect.Y + GetRowOffset(rowCount) - _scrollY;
        }

        return _bodyRect.Y + GetRowOffset(clampedInsertIndex) - _scrollY;
    }

    private static int RemapRowIndexAfterMove(int index, int fromIndex, int toIndex)
    {
        if (index < 0 || fromIndex == toIndex || toIndex == fromIndex + 1)
        {
            return index;
        }

        int insertedIndex = toIndex > fromIndex ? toIndex - 1 : toIndex;
        if (index == fromIndex)
        {
            return insertedIndex;
        }

        if (fromIndex < insertedIndex)
        {
            if (index > fromIndex && index <= insertedIndex)
            {
                return index - 1;
            }
        }
        else if (insertedIndex < fromIndex)
        {
            if (index >= insertedIndex && index < fromIndex)
            {
                return index + 1;
            }
        }

        return index;
    }

    private static void RemapSelectedRowsAfterMove(int fromIndex, int toIndex)
    {
        if (_selectedRows.Count <= 0)
        {
            return;
        }

        int remapCount = 0;
        foreach (int selectedRowIndex in _selectedRows)
        {
            if (remapCount < _rowSelectionRemapScratch.Length)
            {
                _rowSelectionRemapScratch[remapCount++] = RemapRowIndexAfterMove(selectedRowIndex, fromIndex, toIndex);
            }
        }

        _selectedRows.Clear();
        for (int remapIndex = 0; remapIndex < remapCount; remapIndex++)
        {
            _selectedRows.Add(_rowSelectionRemapScratch[remapIndex]);
        }
    }

    private static void ClearSelectionState()
    {
        _selStartRow = -1;
        _selStartCol = -1;
        _selEndRow = -1;
        _selEndCol = -1;
        _activeRow = -1;
        _activeCol = -1;
        _selectedHeaderCol = -1;
        _selectedRows.Clear();
        _lastClickedRow = -1;
        ClearFillHandleDragState();
    }

    private static void ClearFillHandleDragState()
    {
        _isFillHandleDragging = false;
        _fillDragSourceMinRow = -1;
        _fillDragSourceMinCol = -1;
        _fillDragSourceMaxRow = -1;
        _fillDragSourceMaxCol = -1;
        _fillDragTargetRow = -1;
        _fillDragTargetCol = -1;
    }

    private static void ClearSelectionState(EmbeddedSpreadsheetViewState state)
    {
        state.SelStartRow = -1;
        state.SelStartCol = -1;
        state.SelEndRow = -1;
        state.SelEndCol = -1;
        state.ActiveRow = -1;
        state.ActiveCol = -1;
        state.SelectedHeaderCol = -1;
        state.SelectedRows.Clear();
        state.LastClickedRow = -1;
    }

    public static void BlurEmbeddedSelection(string stateKey)
    {
        if (string.IsNullOrWhiteSpace(stateKey))
        {
            return;
        }

        if (!_embeddedViewStates.TryGetValue(stateKey, out var state))
        {
            return;
        }

        ClearSelectionState(state);
        state.ContextRowIndex = -1;
        state.ContextColIndex = -1;
        state.ColumnDragSourceCol = -1;
        state.ColumnDragTargetInsertIndex = -1;
        state.ColumnDragStartMouseX = 0f;
        state.IsColumnDragging = false;
        state.RowDragSourceIndex = -1;
        state.RowDragTargetInsertIndex = -1;
        state.RowDragStartMouseY = 0f;
        state.IsRowDragging = false;
    }

    private static DocColumnKind GetNewColumnKindFromIndex(int kindIndex)
    {
        int clampedIndex = Math.Clamp(kindIndex, 0, _columnKinds.Length - 1);
        return _columnKinds[clampedIndex];
    }

    private static bool HasFormulaExpression(DocColumn column)
    {
        return !string.IsNullOrWhiteSpace(column.FormulaExpression);
    }

    private static bool IsFormulaErrorCell(DocColumn column, DocCellValue cellValue)
    {
        if (string.Equals(cellValue.FormulaError, "#ERR", StringComparison.Ordinal))
        {
            return true;
        }

        return (HasFormulaExpression(column) || cellValue.HasCellFormulaExpression) &&
               string.Equals(cellValue.StringValue, "#ERR", StringComparison.Ordinal);
    }

    private static bool ShouldShowColumnTearHandle(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= _colCount)
        {
            return false;
        }

        int selectedColumnIndex = GetSelectedColumnVisualIndex();
        return _hoveredHeaderCol == columnIndex || selectedColumnIndex == columnIndex;
    }

    private static ImRect GetColumnTearHandleRect(int columnIndex)
    {
        float handleWidth = (ColumnTearHandleDotCount * ColumnTearHandleDotSize) +
                            ((ColumnTearHandleDotCount - 1) * ColumnTearHandleDotSpacing) +
                            6f;
        float handleHeight = 8f;
        float handleX = _colX[columnIndex] + (_colW[columnIndex] - handleWidth) * 0.5f;
        float handleY = _headerRect.Y + 2f;
        return new ImRect(handleX, handleY, handleWidth, handleHeight);
    }

    private static int FindHoveredHeaderTearHandle(Vector2 mousePos)
    {
        for (int columnIndex = 0; columnIndex < _colCount; columnIndex++)
        {
            if (!ShouldShowColumnTearHandle(columnIndex))
            {
                continue;
            }

            if (!TryGetVisibleColumnRect(columnIndex, _headerRect.Y, _headerRect.Height, out var visibleColumnRect))
            {
                continue;
            }

            var handleRect = GetColumnTearHandleRect(columnIndex);
            float clippedX = Math.Max(handleRect.X, visibleColumnRect.X);
            float clippedRight = Math.Min(handleRect.Right, visibleColumnRect.Right);
            if (clippedRight <= clippedX)
            {
                continue;
            }

            var clippedHandleRect = new ImRect(clippedX, handleRect.Y, clippedRight - clippedX, handleRect.Height);
            if (clippedHandleRect.Contains(mousePos))
            {
                return columnIndex;
            }
        }

        return -1;
    }

    private static int GetSelectedColumnVisualIndex()
    {
        return _selectedHeaderCol;
    }

    private static int GetHoveredColumnVisualIndex()
    {
        return _hoveredHeaderCol;
    }

    // =====================================================================
    //  Input handling
    // =====================================================================

    private static void HandleInput(DocWorkspace workspace, DocTable table, ImInput input)
    {
        var mousePos = Im.MousePos;
        bool editOwnedByCurrentInstance = IsEditOwnedByCurrentInstance(workspace, table);
        bool triggerCopyShortcut = input.KeyCtrlC && !_tableCopyShortcutDown;
        bool triggerPasteShortcut = input.KeyCtrlV && !_tablePasteShortcutDown;
        _tableCopyShortcutDown = input.KeyCtrlC;
        _tablePasteShortcutDown = input.KeyCtrlV;

        // Commit active cell edits on any click outside the edited cell rect.
        // This prevents "stale derived" behavior when clicks are captured by popovers/dropdowns elsewhere.
        if (workspace.EditState.IsEditing && input.MousePressed)
        {
            bool popupClicked = _cellTypeaheadPopupVisible && _cellTypeaheadPopupRect.Contains(mousePos);
            if (!editOwnedByCurrentInstance)
            {
                string ownerStateKey = workspace.EditState.OwnerStateKey ?? "";
                if (!string.IsNullOrWhiteSpace(ownerStateKey) &&
                    _embeddedViewStates.TryGetValue(ownerStateKey, out var ownerState))
                {
                    var ownerGridRect = new ImRect(
                        ownerState.GridRectX,
                        ownerState.GridRectY,
                        ownerState.GridRectWidth,
                        ownerState.GridRectHeight);
                    if (!ownerGridRect.Contains(mousePos))
                    {
                        CommitEditIfActive(workspace, table);
                    }
                }
                else
                {
                    CommitEditIfActive(workspace, table);
                }
            }
            else
            {
                var editCellRect = GetCellRect(workspace.EditState.RowIndex, workspace.EditState.ColIndex);
                if (!editCellRect.Contains(mousePos) && !popupClicked)
                {
                    DocColumn? editColumn = null;
                    if (workspace.EditState.ColIndex >= 0 && workspace.EditState.ColIndex < _colCount)
                    {
                        editColumn = GetVisibleColumn(table, workspace.EditState.ColIndex);
                    }

                    if (editColumn != null &&
                        (editColumn.Kind == DocColumnKind.Select ||
                         editColumn.Kind == DocColumnKind.Relation ||
                         editColumn.Kind == DocColumnKind.TableRef))
                    {
                        workspace.CancelTableCellEditIfActive();
                    }
                    else
                    {
                        CommitEditIfActive(workspace, table);
                    }
                }
            }
        }

        // Spreadsheet popovers render after this input pass. Their open state must still
        // suppress grid interactions here to avoid click-through into cells beneath popovers.
        bool overlayCapturing = IsSpreadsheetPopoverOpen();

        _hoveredResizeCol = -1;
        _hoveredHeaderCol = -1;
        _hoveredTearHandleCol = -1;
        _hoveredRowDragHandle = -1;
        _hoveredRowAddBelow = -1;
        if (!overlayCapturing)
        {
            _hoveredResizeCol = FindHoveredResizeColumn(mousePos);
            if (_hoveredResizeCol < 0)
            {
                _hoveredHeaderCol = FindHoveredHeaderColumn(mousePos);
                _hoveredTearHandleCol = FindHoveredHeaderTearHandle(mousePos);
            }
        }

        if (_resizingColIndex >= 0 || _hoveredResizeCol >= 0)
        {
            Im.SetCursor(StandardCursor.HResize);
        }
        else if (_columnDragSourceCol >= 0 || _hoveredTearHandleCol >= 0)
        {
            Im.SetCursor(StandardCursor.Hand);
        }

        if (_inlineRenameColIndex >= 0)
        {
            bool inlineRenameFocused = IsInlineRenameFocused();
            if (inlineRenameFocused)
            {
                if (input.KeyEscape)
                {
                    CancelInlineColumnRename();
                    return;
                }

                if (input.KeyEnter || input.KeyTab)
                {
                    CommitInlineColumnRename(workspace, table);
                    return;
                }
            }

            if (input.MousePressed && !IsMouseOverInlineRenameInput(mousePos, table))
            {
                CommitInlineColumnRename(workspace, table);
            }
        }

        if (_resizingColIndex >= 0)
        {
            if (input.MouseDown)
            {
                float resizedWidth = MathF.Max(MinColumnWidth, _resizeStartWidth + (mousePos.X - _resizeStartMouseX));
                if (MathF.Abs(resizedWidth - _resizeCurrentWidth) > 0.01f)
                {
                    _resizeCurrentWidth = resizedWidth;
                    GetVisibleColumn(table, _resizingColIndex).Width = resizedWidth;
                    ComputeLayout(workspace, table);
                }
            }

            if (input.MouseReleased)
            {
                int resizedColumnIndex = _resizingColIndex;
                float oldWidth = _resizeStartWidth;
                float newWidth = MathF.Max(MinColumnWidth, _resizeCurrentWidth);
                _resizingColIndex = -1;

                if (resizedColumnIndex >= 0 &&
                    resizedColumnIndex < _colCount &&
                    MathF.Abs(newWidth - oldWidth) > 0.01f)
                {
                    var resizedColumn = GetVisibleColumn(table, resizedColumnIndex);
                    workspace.ExecuteCommand(new DocCommand
                    {
                        Kind = DocCommandKind.SetColumnWidth,
                        TableId = table.Id,
                        ColumnId = resizedColumn.Id,
                        OldColumnWidth = oldWidth,
                        NewColumnWidth = newWidth
                    });
                    ComputeLayout(workspace, table);
                }
            }

            return;
        }

        if (_columnDragSourceCol >= 0)
        {
            if (input.MouseDown)
            {
                if (!_isColumnDragging)
                {
                    float dragDistanceX = MathF.Abs(mousePos.X - _columnDragStartMouseX);
                    if (dragDistanceX > ColumnDragStartThreshold)
                    {
                        _isColumnDragging = true;
                    }
                }

                if (_isColumnDragging)
                {
                    _columnDragTargetInsertIndex = GetColumnInsertIndexFromMouseX(mousePos.X);
                }
            }

            if (input.MouseReleased)
            {
                int sourceDisplayCol = _columnDragSourceCol;
                int targetDisplayInsert = Math.Clamp(_columnDragTargetInsertIndex, 0, _colCount);
                bool shouldMoveColumn = _isColumnDragging &&
                                        sourceDisplayCol >= 0 &&
                                        sourceDisplayCol < _colCount &&
                                        targetDisplayInsert != sourceDisplayCol &&
                                        targetDisplayInsert != sourceDisplayCol + 1;

                if (shouldMoveColumn)
                {
                    int sourceTableIndex = _visibleColMap[sourceDisplayCol];
                    int targetTableInsert = targetDisplayInsert < _colCount
                        ? _visibleColMap[targetDisplayInsert]
                        : (sourceTableIndex < table.Columns.Count - 1 ? table.Columns.Count : sourceTableIndex + 1);
                    var movedColumn = table.Columns[sourceTableIndex];
                    workspace.ExecuteCommand(new DocCommand
                    {
                        Kind = DocCommandKind.MoveColumn,
                        TableId = table.Id,
                        ColumnId = movedColumn.Id,
                        ColumnIndex = sourceTableIndex,
                        TargetColumnIndex = targetTableInsert
                    });

                    _selectedHeaderCol = RemapColumnIndexAfterMove(_selectedHeaderCol, sourceDisplayCol, targetDisplayInsert);
                    _activeCol = RemapColumnIndexAfterMove(_activeCol, sourceDisplayCol, targetDisplayInsert);
                    _selStartCol = RemapColumnIndexAfterMove(_selStartCol, sourceDisplayCol, targetDisplayInsert);
                    _selEndCol = RemapColumnIndexAfterMove(_selEndCol, sourceDisplayCol, targetDisplayInsert);
                    _hoveredCol = RemapColumnIndexAfterMove(_hoveredCol, sourceDisplayCol, targetDisplayInsert);
                    _hoveredHeaderCol = RemapColumnIndexAfterMove(_hoveredHeaderCol, sourceDisplayCol, targetDisplayInsert);
                    _contextColIndex = RemapColumnIndexAfterMove(_contextColIndex, sourceDisplayCol, targetDisplayInsert);
                }

                ClearColumnDragState();
                ComputeLayout(workspace, table);
            }

            return;
        }

        if (_rowDragSourceIndex >= 0)
        {
            if (input.MouseDown)
            {
                if (!_isRowDragging)
                {
                    float dragDistanceY = MathF.Abs(mousePos.Y - _rowDragStartMouseY);
                    if (dragDistanceY > RowDragStartThreshold)
                    {
                        _isRowDragging = true;
                    }
                }

                if (_isRowDragging)
                {
                    _rowDragTargetInsertIndex = GetRowInsertIndexFromMouseY(mousePos.Y, _rowCount);
                }
            }

            if (input.MouseReleased)
            {
                int sourceDisplayIndex = _rowDragSourceIndex;
                int sourceRowIndex = GetSourceRowIndex(sourceDisplayIndex);
                int targetDisplayIndex = Math.Clamp(_rowDragTargetInsertIndex, 0, _rowCount);
                int targetInsertIndex = targetDisplayIndex < _rowCount ? GetSourceRowIndex(targetDisplayIndex) : table.Rows.Count;
                targetInsertIndex = Math.Clamp(targetInsertIndex, 0, table.Rows.Count);
                bool shouldMoveRow = _isRowDragging &&
                                     sourceRowIndex >= 0 &&
                                     sourceRowIndex < table.Rows.Count &&
                                     targetInsertIndex != sourceRowIndex &&
                                     targetInsertIndex != sourceRowIndex + 1;

                if (shouldMoveRow)
                {
                    workspace.ExecuteCommand(new DocCommand
                    {
                        Kind = DocCommandKind.MoveRow,
                        TableId = table.Id,
                        RowIndex = sourceRowIndex,
                        TargetRowIndex = targetInsertIndex
                    });

                    _lastClickedRow = RemapRowIndexAfterMove(_lastClickedRow, sourceRowIndex, targetInsertIndex);
                    _activeRow = RemapRowIndexAfterMove(_activeRow, sourceRowIndex, targetInsertIndex);
                    _selStartRow = RemapRowIndexAfterMove(_selStartRow, sourceRowIndex, targetInsertIndex);
                    _selEndRow = RemapRowIndexAfterMove(_selEndRow, sourceRowIndex, targetInsertIndex);
                    _hoveredRow = RemapRowIndexAfterMove(_hoveredRow, sourceRowIndex, targetInsertIndex);
                    _contextRowIndex = RemapRowIndexAfterMove(_contextRowIndex, sourceRowIndex, targetInsertIndex);
                    RemapSelectedRowsAfterMove(sourceRowIndex, targetInsertIndex);
                }

                ClearRowDragState();
                ComputeLayout(workspace, table);
            }

            return;
        }

        if (!overlayCapturing && _hoveredResizeCol >= 0 && input.MousePressed)
        {
            CommitEditIfActive(workspace, table);
            _resizingColIndex = _hoveredResizeCol;
            _resizeStartMouseX = mousePos.X;
            _resizeStartWidth = MathF.Max(MinColumnWidth, _colW[_resizingColIndex]);
            _resizeCurrentWidth = _resizeStartWidth;
            GetVisibleColumn(table, _resizingColIndex).Width = _resizeStartWidth;
            ComputeLayout(workspace, table);
            return;
        }

        if (!overlayCapturing && _hoveredHeaderCol >= 0 && input.MousePressed)
        {
            CommitEditIfActive(workspace, table);
            workspace.CancelTableCellEditIfActive();
            _selectedRows.Clear();
            _selStartRow = -1; _selStartCol = -1;
            _selEndRow = -1; _selEndCol = -1;
            _activeRow = -1;
            _activeCol = _hoveredHeaderCol;
            _selectedHeaderCol = _hoveredHeaderCol;
            var clickedColumn = GetVisibleColumn(table, _hoveredHeaderCol);

            if (TryGetHeaderMenuButtonRect(clickedColumn, _hoveredHeaderCol, out var headerMenuButtonRect) &&
                headerMenuButtonRect.Contains(mousePos))
            {
                if (!IsColumnSchemaLocked(clickedColumn))
                {
                    _contextColIndex = _hoveredHeaderCol;
                    MarkContextMenuOwner();
                    ImContextMenu.OpenAt("col_context_menu", headerMenuButtonRect.X, headerMenuButtonRect.Bottom);
                }

                ClearColumnDragState();
                return;
            }

            bool clickedHeaderNameText = _hoveredTearHandleCol != _hoveredHeaderCol &&
                                         IsMouseOverHeaderNameText(table, clickedColumn, _hoveredHeaderCol, mousePos);
            if (clickedHeaderNameText && !IsColumnSchemaLocked(clickedColumn))
            {
                StartInlineColumnRename(table, _hoveredHeaderCol, false);
                ClearColumnDragState();
                return;
            }

            if (_hoveredTearHandleCol == _hoveredHeaderCol)
            {
                _columnDragSourceCol = _hoveredHeaderCol;
                _columnDragTargetInsertIndex = _hoveredHeaderCol + 1;
                _columnDragStartMouseX = mousePos.X;
                _isColumnDragging = false;
            }
            else
            {
                ClearColumnDragState();
            }
            return;
        }

        // --- Hover ---
        _hoveredRow = -1;
        _hoveredCol = -1;
        var bodyColumnsRect = GetColumnsViewportRect(_bodyRect.Y, _bodyRect.Height);
        if (!overlayCapturing &&
            _bodyRect.Contains(mousePos) &&
            TryGetRowFromMouseY(mousePos.Y, _rowCount, out int mouseRow))
        {
            int hoveredRow = mouseRow;
            if (_isInteractiveRender)
            {
                if (GetRowAddBelowButtonRect(mouseRow).Contains(mousePos))
                {
                    _hoveredRowAddBelow = mouseRow;
                    hoveredRow = mouseRow;
                }
                else if (mouseRow > 0 && GetRowAddBelowButtonRect(mouseRow - 1).Contains(mousePos))
                {
                    _hoveredRowAddBelow = mouseRow - 1;
                    hoveredRow = mouseRow - 1;
                }

                if (GetRowDragHandleRect(hoveredRow).Contains(mousePos))
                {
                    _hoveredRowDragHandle = hoveredRow;
                }
            }

            _hoveredRow = hoveredRow;

            // Check if in row-number area
            if (mousePos.X >= _bodyRect.X && mousePos.X < _bodyRect.X + RowNumberWidth)
            {
                _hoveredCol = -1; // row number area
            }
            else if (_hoveredRowAddBelow >= 0)
            {
                _hoveredCol = -1;
            }
            else if (!bodyColumnsRect.Contains(mousePos))
            {
                _hoveredCol = -1;
            }
            else
            {
                for (int columnIndex = 0; columnIndex < _colCount; columnIndex++)
                {
                    if (!TryGetVisibleColumnRect(columnIndex, _bodyRect.Y, _bodyRect.Height, out var visibleRect))
                    {
                        continue;
                    }

                    if (visibleRect.Contains(mousePos))
                    {
                        _hoveredCol = columnIndex;
                        break;
                    }
                }
            }
        }

        if (!overlayCapturing && (_hoveredRowDragHandle >= 0 || _hoveredRowAddBelow >= 0))
        {
            Im.SetCursor(StandardCursor.Hand);
        }

        if (!overlayCapturing &&
            TryGetHoveredAudioPlayTarget(
                workspace,
                table,
                _hoveredRow,
                _hoveredCol,
                out string audioAssetsRoot,
                out string audioRelativePath,
                out ImRect audioPlayButtonRect))
        {
            if (audioPlayButtonRect.Contains(mousePos))
            {
                Im.SetCursor(StandardCursor.Hand);
                if (input.MousePressed)
                {
                    _ = DocAssetServices.AudioPreviewPlayer.TryPlay(audioAssetsRoot, audioRelativePath, out _);
                    Im.Context.ConsumeMouseLeftPress();
                    return;
                }
            }
        }

        bool mouseOverEmbeddedSubtableGrid = !overlayCapturing &&
                                             IsMouseOverHoveredSubtableEmbeddedGrid(workspace, table, mousePos);

        // --- Scroll wheel ---
        bool suppressWheelForThisEmbeddedState =
            !string.IsNullOrEmpty(_activeEmbeddedStateKey) &&
            _wheelSuppressedFrame == Im.Context.FrameCount &&
            string.Equals(_wheelSuppressedStateKey, _activeEmbeddedStateKey, StringComparison.Ordinal);

        if (!overlayCapturing && !suppressWheelForThisEmbeddedState && IsMouseOverScrollableTableRegion(mousePos))
        {
            float columnViewportWidth = Math.Max(0f, _scrollableColumnsViewportWidth);
            float maxHorizontalScroll = Math.Max(0f, _columnContentWidth - columnViewportWidth);
            float maxVerticalScroll = Math.Max(0f, _rowContentHeight - _bodyRect.Height);
            bool canScrollHorizontally = _hasHorizontalScrollbar && maxHorizontalScroll > 0f;
            bool canScrollVertically = _hasVerticalScrollbar && maxVerticalScroll > 0f;
            bool horizontalScrollChanged = false;
            string hoveredEmbeddedGridStateKey = "";
            float wheelAmount = MathF.Abs(input.ScrollDeltaX) > MathF.Abs(input.ScrollDelta)
                ? input.ScrollDeltaX
                : input.ScrollDelta;
            if (wheelAmount != 0f)
            {
                float embeddedScrollbarDistance = float.PositiveInfinity;
                bool embeddedCanConsumeWheel = TryGetHoveredSubtableEmbeddedGridWheelConsumeDistance(
                    workspace,
                    table,
                    mousePos,
                    input.ScrollDelta,
                    input.ScrollDeltaX,
                    out hoveredEmbeddedGridStateKey,
                    out embeddedScrollbarDistance);

                float previousScrollX = _scrollX;
                float previousScrollY = _scrollY;

                bool parentCanConsumeWheel = false;
                float parentScrollbarDistance = float.PositiveInfinity;
                bool parentRouteToHorizontal = false;
                var horizontalScrollbarRect = GetHorizontalScrollbarRect(columnViewportWidth);
                var verticalScrollbarRect = GetVerticalScrollbarRect();
                if (canScrollHorizontally || canScrollVertically)
                {
                    parentRouteToHorizontal = ShouldRouteWheelToHorizontalScrollbar(
                        mousePos,
                        canScrollHorizontally,
                        canScrollVertically,
                        horizontalScrollbarRect,
                        verticalScrollbarRect);

                    if (parentRouteToHorizontal && canScrollHorizontally)
                    {
                        if (WouldWheelMoveOffset(_scrollX, wheelAmount, HorizontalScrollWheelSpeed, maxHorizontalScroll))
                        {
                            parentCanConsumeWheel = true;
                            parentScrollbarDistance = DistanceFromPointToRect(mousePos, horizontalScrollbarRect);
                        }
                    }
                    else if (canScrollVertically)
                    {
                        if (WouldWheelMoveOffset(_scrollY, wheelAmount, RowHeight * 3f, maxVerticalScroll))
                        {
                            parentCanConsumeWheel = true;
                            parentScrollbarDistance = DistanceFromPointToRect(mousePos, verticalScrollbarRect);
                        }
                    }
                    else if (canScrollHorizontally)
                    {
                        if (WouldWheelMoveOffset(_scrollX, wheelAmount, HorizontalScrollWheelSpeed, maxHorizontalScroll))
                        {
                            parentCanConsumeWheel = true;
                            parentScrollbarDistance = DistanceFromPointToRect(mousePos, horizontalScrollbarRect);
                        }
                    }
                }

                bool routeWheelToEmbedded;
                if (embeddedCanConsumeWheel && parentCanConsumeWheel)
                {
                    routeWheelToEmbedded = embeddedScrollbarDistance < parentScrollbarDistance;
                }
                else
                {
                    routeWheelToEmbedded = embeddedCanConsumeWheel;
                }

                if (!routeWheelToEmbedded && parentCanConsumeWheel)
                {
                    if (parentRouteToHorizontal && canScrollHorizontally)
                    {
                        _scrollX -= wheelAmount * HorizontalScrollWheelSpeed;
                        _scrollX = Math.Clamp(_scrollX, 0f, maxHorizontalScroll);
                        horizontalScrollChanged = true;
                    }
                    else if (canScrollVertically)
                    {
                        _scrollY -= wheelAmount * RowHeight * 3f;
                        _scrollY = Math.Clamp(_scrollY, 0f, maxVerticalScroll);
                    }
                    else if (canScrollHorizontally)
                    {
                        _scrollX -= wheelAmount * HorizontalScrollWheelSpeed;
                        _scrollX = Math.Clamp(_scrollX, 0f, maxHorizontalScroll);
                        horizontalScrollChanged = true;
                    }

                    bool parentScrollChanged =
                        MathF.Abs(_scrollX - previousScrollX) > 0.001f ||
                        MathF.Abs(_scrollY - previousScrollY) > 0.001f;
                    if (parentScrollChanged && embeddedCanConsumeWheel && !string.IsNullOrEmpty(hoveredEmbeddedGridStateKey))
                    {
                        SuppressEmbeddedWheelForStateThisFrame(hoveredEmbeddedGridStateKey);
                    }
                }
            }

            if (horizontalScrollChanged)
            {
                ComputeLayout(workspace, table);
            }
        }

        if (!overlayCapturing && !_isFillHandleDragging && TryGetSelectionFillHandleRect(out ImRect fillHandleRect))
        {
            bool fillHandleHovered = fillHandleRect.Contains(mousePos);
            if (fillHandleHovered)
            {
                Im.SetCursor(StandardCursor.Crosshair);
                if (input.MousePressed)
                {
                    if (workspace.EditState.IsEditing)
                    {
                        CommitEditIfActive(workspace, table);
                    }

                    if (TryGetSelectedOrActiveCellBounds(
                            out _fillDragSourceMinRow,
                            out _fillDragSourceMaxRow,
                            out _fillDragSourceMinCol,
                            out _fillDragSourceMaxCol))
                    {
                        _fillDragTargetRow = _fillDragSourceMaxRow;
                        _fillDragTargetCol = _fillDragSourceMaxCol;
                        _isFillHandleDragging = true;
                        _isDragging = false;
                        Im.Context.ConsumeMouseLeftPress();
                        return;
                    }
                }
            }
        }

        if (_isFillHandleDragging)
        {
            if (input.KeyEscape)
            {
                ClearFillHandleDragState();
                return;
            }

            if (input.MouseDown)
            {
                int dragRow = _fillDragTargetRow;
                int dragCol = _fillDragTargetCol;
                if (TryGetDisplayCellFromMouse(mousePos, out int hoveredFillRow, out int hoveredFillColumn))
                {
                    dragRow = hoveredFillRow;
                    dragCol = hoveredFillColumn;
                }
                else
                {
                    if (mousePos.Y >= _bodyRect.Bottom)
                    {
                        dragRow = _rowCount - 1;
                    }

                    if (mousePos.X >= _bodyRect.Right)
                    {
                        dragCol = _colCount - 1;
                    }
                    else if (TryGetDisplayColumnFromMouseX(mousePos.X, out int hoveredFillColumnIndex))
                    {
                        dragCol = hoveredFillColumnIndex;
                    }
                }

                _fillDragTargetRow = Math.Clamp(dragRow, _fillDragSourceMaxRow, Math.Max(0, _rowCount - 1));
                _fillDragTargetCol = Math.Clamp(dragCol, _fillDragSourceMaxCol, Math.Max(0, _colCount - 1));
            }

            if (input.MouseReleased)
            {
                ApplyFillHandleDrag(workspace, table);
            }

            return;
        }

        // --- Selection blur (clicking outside table clears selection) ---
        if (!overlayCapturing && input.MousePressed && !_gridRect.Contains(mousePos))
        {
            if (workspace.EditState.IsEditing)
            {
                CommitEditIfActive(workspace, table);
            }

            ClearSelectionState();
            ClearColumnDragState();
            ClearRowDragState();
            _contextRowIndex = -1;
            _contextColIndex = -1;
        }

        if (!overlayCapturing && _hoveredRowAddBelow >= 0 && input.MousePressed && !table.IsDerived)
        {
            CommitEditIfActive(workspace, table);
            workspace.CancelTableCellEditIfActive();

            int displayIdx = _hoveredRowAddBelow + 1;
            int insertIndex = displayIdx < _rowCount ? GetSourceRowIndex(displayIdx) : table.Rows.Count;
            insertIndex = Math.Clamp(insertIndex, 0, table.Rows.Count);
            InsertRow(workspace, table, insertIndex);
            ClearSelectionState();
            _selectedRows.Add(displayIdx);
            _lastClickedRow = displayIdx;
            _contextRowIndex = displayIdx;
            return;
        }

        if (!overlayCapturing && _hoveredRowDragHandle >= 0 && input.MousePressed)
        {
            CommitEditIfActive(workspace, table);
            workspace.CancelTableCellEditIfActive();

            _rowDragSourceIndex = _hoveredRowDragHandle;
            _rowDragTargetInsertIndex = _hoveredRowDragHandle + 1;
            _rowDragStartMouseY = mousePos.Y;
            _isRowDragging = false;
            return;
        }

        // --- Left click ---
        if (!overlayCapturing && input.MousePressed && _hoveredRow >= 0)
        {
            bool isRowNumberArea = mousePos.X >= _bodyRect.X && mousePos.X < _bodyRect.X + RowNumberWidth;

            if (isRowNumberArea)
            {
                // Row selection mode
                CommitEditIfActive(workspace, table);

                if (input.KeyShift && _lastClickedRow >= 0)
                {
                    // Shift+click: select range
                    int minR = Math.Min(_lastClickedRow, _hoveredRow);
                    int maxR = Math.Max(_lastClickedRow, _hoveredRow);
                    _selectedRows.Clear();
                    for (int r = minR; r <= maxR; r++)
                        _selectedRows.Add(r);
                }
                else if (input.KeyCtrl)
                {
                    // Ctrl+click: toggle
                    if (!_selectedRows.Remove(_hoveredRow))
                        _selectedRows.Add(_hoveredRow);
                    _lastClickedRow = _hoveredRow;
                }
                else
                {
                    // Plain click: single row
                    _selectedRows.Clear();
                    _selectedRows.Add(_hoveredRow);
                    _lastClickedRow = _hoveredRow;
                }

                // Clear cell selection
                _selStartRow = -1; _selStartCol = -1;
                _selEndRow = -1; _selEndCol = -1;
                _activeRow = -1; _activeCol = -1;
                _selectedHeaderCol = -1;
            }
            else if (_hoveredCol >= 0)
            {
                if (mouseOverEmbeddedSubtableGrid)
                {
                    // Defer to the embedded spreadsheet renderer; do not change parent selection/focus.
                    _selectedRows.Clear();
                    _selStartRow = -1; _selStartCol = -1;
                    _selEndRow = -1; _selEndCol = -1;
                    _activeRow = -1; _activeCol = -1;
                    _selectedHeaderCol = -1;
                    _isDragging = false;
                }
                else
                {
                // Cell click
                _selectedRows.Clear();
                var col = GetVisibleColumn(table, _hoveredCol);

                if (input.KeyShift && _selStartRow >= 0 && _selStartCol >= 0)
                {
                    // Shift+click: extend cell range
                    if (workspace.EditState.IsEditing)
                    {
                        workspace.CancelTableCellEditIfActive();
                    }

                    _selEndRow = _hoveredRow;
                    _selEndCol = _hoveredCol;
                    _activeRow = -1;
                    _activeCol = -1;
                    _isDragging = true;
                }
                else
                {
                    // Plain click: commit old edit, begin new
                    CommitEditIfActive(workspace, table);

                    _selStartRow = _hoveredRow;
                    _selStartCol = _hoveredCol;
                    _selEndRow = _hoveredRow;
                    _selEndCol = _hoveredCol;
	                    _activeRow = _hoveredRow;
	                    _activeCol = _hoveredCol;
	                    _selectedHeaderCol = -1;

	                    if (!IsMouseOverHoveredSubtableEmbeddedGrid(workspace, table, mousePos))
	                    {
	                        // Begin edit immediately
	                        BeginCellEdit(workspace, table, _hoveredRow, _hoveredCol);
	                    }

	                    // Number cells use click+drag for scrub editing, so do not arm grid range drag.
	                    _isDragging = col.Kind != DocColumnKind.Number;
	                }
	            }
                }
	        }

        // --- Drag ---
        bool isNumberScrubGestureActive =
            workspace.EditState.IsEditing &&
            (workspace.EditState.NumberDragPressed || workspace.EditState.IsNumberDragging);

        if (!overlayCapturing &&
            _isDragging &&
            input.MouseDown &&
            !input.MousePressed &&
            !isNumberScrubGestureActive)
        {
            if (mouseOverEmbeddedSubtableGrid)
            {
                // Embedded grid owns drag selection while hovered.
                _isDragging = false;
            }

            if (_bodyRect.Contains(mousePos))
            {
                if (!TryGetRowFromMouseY(mousePos.Y, _rowCount, out int dragRow))
                {
                    dragRow = Math.Clamp(_selEndRow, 0, Math.Max(0, _rowCount - 1));
                }

                int dragCol = -1;
                for (int c = 0; c < _colCount; c++)
                {
                    if (mousePos.X >= _colX[c] && mousePos.X < _colX[c] + _colW[c])
                    {
                        dragCol = c;
                        break;
                    }
                }
                if (dragCol >= 0 && (dragRow != _selEndRow || dragCol != _selEndCol))
                {
                    // Dragged to a different cell — cancel edit, set range selection
                    if (workspace.EditState.IsEditing)
                        workspace.CancelTableCellEditIfActive();

                    _selEndRow = dragRow;
                    _selEndCol = dragCol;
                    _activeRow = -1;
                    _activeCol = -1;
                }
            }
        }

        // --- Mouse release ---
        if (input.MouseReleased)
        {
            _isDragging = false;
        }

        // --- Right click on body ---
        if (!overlayCapturing && input.MouseRightPressed && _bodyRect.Contains(mousePos) && _hoveredRow >= 0)
        {
            if (mouseOverEmbeddedSubtableGrid)
            {
                // Defer to the embedded spreadsheet renderer.
            }
            else if (_hoveredCol >= 0)
            {
                // Right-click on a data cell
                bool inSelection = IsInCellSelection(_hoveredRow, _hoveredCol);
                bool inRowSelection = _selectedRows.Contains(_hoveredRow);

                if (!inSelection && !inRowSelection)
                {
                    // Select this cell first
                    _selectedRows.Clear();
                    _selStartRow = _hoveredRow; _selStartCol = _hoveredCol;
                    _selEndRow = _hoveredRow; _selEndCol = _hoveredCol;
                }

                _contextRowIndex = _hoveredRow;
                _contextColIndex = _hoveredCol;
                MarkContextMenuOwner();
                ImContextMenu.Open("row_context_menu");
            }
            else
            {
                // Right-click on row number area
                if (!_selectedRows.Contains(_hoveredRow))
                {
                    _selectedRows.Clear();
                    _selectedRows.Add(_hoveredRow);
                    _selStartRow = -1; _selStartCol = -1;
                    _selEndRow = -1; _selEndCol = -1;
                }
                _contextRowIndex = _hoveredRow;
                _contextColIndex = -1;
                MarkContextMenuOwner();
                ImContextMenu.Open("row_context_menu");
            }
        }

        // --- Right click on header ---
        if (!overlayCapturing && input.MouseRightPressed && _headerRect.Contains(mousePos))
        {
            if (_hoveredHeaderCol >= 0 && _hoveredHeaderCol < _colCount)
            {
                var contextHeaderColumn = GetVisibleColumn(table, _hoveredHeaderCol);
                if (!IsColumnSchemaLocked(contextHeaderColumn))
                {
                    _contextColIndex = _hoveredHeaderCol;
                    _selectedHeaderCol = _hoveredHeaderCol;
                    _activeRow = -1;
                    _activeCol = _hoveredHeaderCol;
                    MarkContextMenuOwner();
                    ImContextMenu.Open("col_context_menu");
                }
            }
        }

        // --- Right click on empty area (below rows) ---
        if (!overlayCapturing && input.MouseRightPressed && _bodyRect.Contains(mousePos) && _hoveredRow < 0)
        {
            MarkContextMenuOwner();
            ImContextMenu.Open("empty_context_menu");
        }

        // --- Escape ---
        if (!overlayCapturing &&
            input.KeyEscape &&
            !ImContextMenu.IsOpen("row_context_menu") &&
            !ImContextMenu.IsOpen("col_context_menu") &&
            !ImContextMenu.IsOpen("empty_context_menu"))
        {
            if (workspace.EditState.IsEditing)
            {
                workspace.CancelTableCellEditIfActive();
            }
            ClearSelectionState();
            ClearColumnDragState();
            ClearRowDragState();
        }

        // --- Delete/Backspace on selection ---
        if (!overlayCapturing && (input.KeyDelete || input.KeyBackspace) && !workspace.EditState.IsEditing)
        {
            ClearSelectedCells(workspace, table);
        }

        bool hasMultiCellSelection = TryGetCellSelectionBounds(out int selectionMinRow, out int selectionMaxRow, out int selectionMinCol, out int selectionMaxCol) &&
                                     (selectionMinRow != selectionMaxRow || selectionMinCol != selectionMaxCol);
        bool canHandleGridClipboardShortcuts =
            !overlayCapturing &&
            (!workspace.EditState.IsEditing || hasMultiCellSelection);
        if (canHandleGridClipboardShortcuts && triggerCopyShortcut)
        {
            if (workspace.EditState.IsEditing)
            {
                workspace.CancelTableCellEditIfActive();
            }

            CopySelectedCellsToClipboard(workspace, table);
        }

        if (canHandleGridClipboardShortcuts && triggerPasteShortcut)
        {
            if (workspace.EditState.IsEditing)
            {
                workspace.CancelTableCellEditIfActive();
            }

            PasteClipboardToSelection(workspace, table);
        }

        // --- Tab in edit mode ---
        if (!overlayCapturing && input.KeyTab && editOwnedByCurrentInstance)
        {
            CommitEditIfActive(workspace, table);
            AdvanceToNextCell(workspace, table);
        }
    }

    private static bool IsSpreadsheetPopoverOpen()
    {
        if (_showEditFormulaDialog ||
            _showEditRelationDialog ||
            _showEditNumberColumnDialog ||
            _showAddSubtableColumnDialog ||
            _showEditSubtableDisplayDialog ||
            _showEditSelectColumnDialog ||
            _showEditMeshPreviewDialog)
        {
            return true;
        }

        if (_cellTypeaheadPopupVisible)
        {
            return true;
        }

        if (_splinePopoverActive)
        {
            return true;
        }

        if (IsAnySpreadsheetContextMenuOpen())
        {
            return true;
        }

        if (ImModal.IsAnyOpen)
        {
            return true;
        }

        if (Im.IsAnyDropdownOpen)
        {
            return true;
        }

        return false;
    }

    private static bool TryGetHoveredAudioPlayTarget(
        DocWorkspace workspace,
        DocTable table,
        int hoveredRowIndex,
        int hoveredColumnIndex,
        out string assetsRoot,
        out string relativePath,
        out ImRect playButtonRect)
    {
        assetsRoot = "";
        relativePath = "";
        playButtonRect = default;

        if (hoveredRowIndex < 0 ||
            hoveredRowIndex >= _rowCount ||
            hoveredColumnIndex < 0 ||
            hoveredColumnIndex >= _colCount)
        {
            return false;
        }

        var column = GetVisibleColumn(table, hoveredColumnIndex);
        if (column.Kind != DocColumnKind.AudioAsset)
        {
            return false;
        }

        int sourceRowIndex = GetSourceRowIndex(hoveredRowIndex);
        if (sourceRowIndex < 0 || sourceRowIndex >= table.Rows.Count)
        {
            return false;
        }

        var row = table.Rows[sourceRowIndex];
        relativePath = row.GetCell(column).StringValue ?? "";
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        assetsRoot = workspace.AssetsRoot ?? "";
        if (string.IsNullOrWhiteSpace(assetsRoot) || !Directory.Exists(assetsRoot))
        {
            return false;
        }

        ImRect cellRect = GetCellRect(hoveredRowIndex, hoveredColumnIndex);
        playButtonRect = GetAudioAssetPlayButtonRect(cellRect, Im.Style);
        return true;
    }

    private static ImRect GetClampedEditDialogRect(float desiredX, float desiredY, float desiredWidth, float desiredHeight)
    {
        // Edit dialogs are window-level popovers, not cell-attached tooltips.
        // Clamp to the full spreadsheet panel bounds (not just the grid body)
        // so dialogs can shift upward when their content grows.
        float minY = _dialogBoundsRect.Y;
        float availableWidth = Math.Max(1f, _dialogBoundsRect.Width);
        float availableHeight = Math.Max(1f, _dialogBoundsRect.Bottom - minY);

        float dialogWidth = MathF.Min(desiredWidth, availableWidth);
        float dialogHeight = MathF.Min(desiredHeight, availableHeight);

        float minX = _dialogBoundsRect.X;
        float maxX = _dialogBoundsRect.Right - dialogWidth;
        float maxY = _dialogBoundsRect.Bottom - dialogHeight;
        float dialogX = maxX >= minX
            ? Math.Clamp(desiredX, minX, maxX)
            : minX;
        float dialogY = maxY >= minY
            ? Math.Clamp(desiredY, minY, maxY)
            : minY;

        return new ImRect(dialogX, dialogY, dialogWidth, dialogHeight);
    }

    private static bool ShouldCloseEditDialogPopover(int openedFrame, ImRect dialogRect)
    {
        if (ImModal.IsAnyOpen || Im.IsAnyDropdownOpen)
        {
            return false;
        }

        return ImPopover.ShouldClose(
            openedFrame: openedFrame,
            closeOnEscape: true,
            closeOnOutsideButtons: ImPopoverCloseButtons.Left | ImPopoverCloseButtons.Right,
            consumeCloseClick: false,
            requireNoMouseOwner: true,
            useViewportMouseCoordinates: false,
            insideRect: dialogRect);
    }

    private static bool IsAnySpreadsheetContextMenuOpen()
    {
        return ImContextMenu.IsOpen("row_context_menu") ||
               ImContextMenu.IsOpen("col_context_menu") ||
               ImContextMenu.IsOpen("empty_context_menu") ||
               ImContextMenu.IsOpen("add_col_type_menu");
    }

    private static void MarkContextMenuOwner()
    {
        if (!string.IsNullOrWhiteSpace(_activeEmbeddedStateKey))
        {
            _contextMenuOwnerStateKey = _activeEmbeddedStateKey;
        }
    }

    // =====================================================================
    //  Drawing
    // =====================================================================

    private static void DrawTableTitleRow(
        DocWorkspace workspace,
        DocTable table,
        ImRect contentRect,
        bool interactive)
    {
        var style = Im.Style;
        float titleRowX = contentRect.X;
        float titleRowY = contentRect.Y;
        float titleRowWidth = contentRect.Width;
        float titleRowHeight = TableTitleRowHeight;
        var titleRowRect = new ImRect(titleRowX, titleRowY, titleRowWidth, titleRowHeight);
        string variantBadgeLabel = ResolveVariantBadgeLabel(table, _activeRenderVariantId);
        float variantBadgeFontSize = Math.Max(9f, style.FontSize - 1f);
        float variantBadgeWidth = Im.MeasureTextWidth(variantBadgeLabel.AsSpan(), variantBadgeFontSize) + (TableTitleVariantBadgeHorizontalPadding * 2f);
        float titleOverlayReservedWidth = TableTitleOverlayReservedWidth + TableTitleVariantBadgeGap + variantBadgeWidth;
        var editableTitleRect = new ImRect(
            titleRowRect.X,
            titleRowRect.Y,
            Math.Max(1f, titleRowRect.Width - titleOverlayReservedWidth),
            titleRowRect.Height);

        float titleTextX = titleRowRect.X + 4f;
        float titleTextY = titleRowRect.Y + (titleRowRect.Height - (style.FontSize + TableTitleFontSizeBoost)) * 0.5f;
        float titleInputX = titleTextX - style.Padding;
        float titleInputY = titleTextY - style.Padding;
        float titleInputWidth = Math.Max(1f, editableTitleRect.Right - titleInputX - 4f);
        string titleInputId = "table_title_rename_input";

        if (_inlineRenameTableActive)
        {
            bool wasRenameFocused = IsInlineTableRenameFocused(titleInputId);
            float baseFontSize = Im.Style.FontSize;
            float baseMinButtonHeight = Im.Style.MinButtonHeight;
            float inlineTitleFontSize = baseFontSize + TableTitleFontSizeBoost;
            float inlineTitleInputHeight = MathF.Min(titleRowRect.Height, inlineTitleFontSize + Im.Style.Padding * 2f);
            Im.Style.FontSize = inlineTitleFontSize;
            Im.Style.MinButtonHeight = inlineTitleInputHeight;

            Im.TextInput(
                titleInputId,
                _renameTableBuffer,
                ref _renameTableBufferLength,
                _renameTableBuffer.Length,
                titleInputX,
                titleInputY,
                titleInputWidth,
                Im.ImTextInputFlags.NoBackground | Im.ImTextInputFlags.NoBorder);

            Im.Style.FontSize = baseFontSize;
            Im.Style.MinButtonHeight = baseMinButtonHeight;

            if (_inlineRenameTableNeedsFocus)
            {
                int widgetId = Im.Context.GetId(titleInputId);
                Im.Context.RequestFocus(widgetId);
                if (Im.TryGetTextInputState(titleInputId, out _))
                {
                    if (_inlineRenameTableSelectAll)
                    {
                        Im.SetTextInputSelection(titleInputId, _renameTableBufferLength, 0, _renameTableBufferLength);
                    }
                    else
                    {
                        Im.SetTextInputSelection(titleInputId, _renameTableBufferLength);
                    }
                }

                _inlineRenameTableNeedsFocus = false;
                _inlineRenameTableSelectAll = false;
            }

            bool renameFocused = IsInlineTableRenameFocused(titleInputId);
            var input = Im.Context.Input;
            if (renameFocused || wasRenameFocused)
            {
                if (input.KeyEscape)
                {
                    CancelInlineTableRename();
                }
                else if (input.KeyEnter || input.KeyTab)
                {
                    CommitInlineTableRename(workspace, table);
                }
            }

            if (interactive &&
                input.MousePressed &&
                !editableTitleRect.Contains(Im.MousePos))
            {
                CommitInlineTableRename(workspace, table);
            }
        }
        else
        {
            Im.Text(table.Name.AsSpan(), titleTextX, titleTextY, style.FontSize + TableTitleFontSizeBoost, style.TextPrimary);
            if (interactive &&
                editableTitleRect.Contains(Im.MousePos) &&
                Im.Context.Input.MousePressed)
            {
                BeginInlineTableRename(table, selectAll: true);
            }
        }

        float variantBadgeHeight = Math.Max(18f, variantBadgeFontSize + 6f);
        float variantBadgeX = titleRowRect.Right - TableTitleOverlayReservedWidth - TableTitleVariantBadgeGap - variantBadgeWidth;
        float variantBadgeY = titleRowRect.Y + (titleRowRect.Height - variantBadgeHeight) * 0.5f;
        bool isBaseVariantBadge = _activeRenderVariantId == DocTableVariant.BaseVariantId;
        uint variantBadgeFill = isBaseVariantBadge
            ? ImStyle.WithAlpha(style.Surface, 220)
            : ImStyle.WithAlpha(style.Primary, 84);
        uint variantBadgeBorder = isBaseVariantBadge
            ? ImStyle.WithAlpha(style.Border, 170)
            : ImStyle.WithAlpha(style.Primary, 180);
        uint variantBadgeTextColor = isBaseVariantBadge
            ? style.TextSecondary
            : style.TextPrimary;
        Im.DrawRoundedRect(variantBadgeX, variantBadgeY, variantBadgeWidth, variantBadgeHeight, 4f, variantBadgeFill);
        Im.DrawRoundedRectStroke(variantBadgeX, variantBadgeY, variantBadgeWidth, variantBadgeHeight, 4f, variantBadgeBorder, 1f);
        float variantBadgeTextX = variantBadgeX + TableTitleVariantBadgeHorizontalPadding;
        float variantBadgeTextY = variantBadgeY + (variantBadgeHeight - variantBadgeFontSize) * 0.5f;
        Im.Text(variantBadgeLabel.AsSpan(), variantBadgeTextX, variantBadgeTextY, variantBadgeFontSize, variantBadgeTextColor);

        // Options button in reserved space on the right
        if (interactive)
        {
            float optBtnSize = 24f;
            float optBtnX = titleRowRect.Right - TableTitleOverlayReservedWidth + (TableTitleOverlayReservedWidth - optBtnSize) * 0.5f;
            float optBtnY = titleRowRect.Y + (titleRowRect.Height - optBtnSize) * 0.5f;
            bool optBtnHovered = new ImRect(optBtnX, optBtnY, optBtnSize, optBtnSize).Contains(Im.MousePos);
            if (optBtnHovered)
            {
                Im.DrawRoundedRect(optBtnX, optBtnY, optBtnSize, optBtnSize, 4f, ImStyle.WithAlpha(style.Hover, 128));
            }
            float optIconX = optBtnX + (optBtnSize - Im.MeasureTextWidth(_optionsIconText.AsSpan(), style.FontSize)) * 0.5f;
            float optIconY = optBtnY + (optBtnSize - style.FontSize) * 0.5f;
            Im.Text(_optionsIconText.AsSpan(), optIconX, optIconY, style.FontSize, optBtnHovered ? style.TextPrimary : style.TextSecondary);
            if (optBtnHovered && Im.Context.Input.MousePressed)
            {
                workspace.InspectedTable = table;
                workspace.InspectedBlockId = string.IsNullOrEmpty(_activeEmbeddedStateKey) ? null : _activeEmbeddedStateKey;
                workspace.ShowInspector = true;
            }
        }

        float dividerY = titleRowRect.Bottom + TableTitleBottomSpacing;
        Im.DrawLine(contentRect.X, dividerY, contentRect.Right, dividerY, 1f, Im.Style.Border);
    }

    private static string ResolveVariantBadgeLabel(DocTable table, int variantId)
    {
        if (variantId == DocTableVariant.BaseVariantId)
        {
            return DocTableVariant.BaseVariantName;
        }

        for (int variantIndex = 0; variantIndex < table.Variants.Count; variantIndex++)
        {
            DocTableVariant variant = table.Variants[variantIndex];
            if (variant.Id == variantId)
            {
                return variant.Name;
            }
        }

        return DocTableVariant.BaseVariantName;
    }

    private static void BeginInlineTableRename(DocTable table, bool selectAll)
    {
        _inlineRenameTableActive = true;
        _inlineRenameTableId = table.Id;
        _renameTableBufferLength = Math.Min(table.Name.Length, _renameTableBuffer.Length);
        table.Name.AsSpan(0, _renameTableBufferLength).CopyTo(_renameTableBuffer);
        _inlineRenameTableNeedsFocus = true;
        _inlineRenameTableSelectAll = selectAll;
    }

    private static void CommitInlineTableRename(DocWorkspace workspace, DocTable table)
    {
        if (!_inlineRenameTableActive)
        {
            return;
        }

        if (_renameTableBufferLength > 0)
        {
            string newName = new(_renameTableBuffer, 0, _renameTableBufferLength);
            if (!string.Equals(table.Name, newName, StringComparison.Ordinal))
            {
                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.RenameTable,
                    TableId = table.Id,
                    OldName = table.Name,
                    NewName = newName,
                });
            }
        }

        CancelInlineTableRename();
    }

    private static void CancelInlineTableRename()
    {
        Im.ClearTextInputState("table_title_rename_input");
        _inlineRenameTableActive = false;
        _inlineRenameTableNeedsFocus = false;
        _inlineRenameTableSelectAll = false;
        _renameTableBufferLength = 0;
        _inlineRenameTableId = "";
    }

    private static bool IsInlineTableRenameFocused(string inputId)
    {
        if (!_inlineRenameTableActive)
        {
            return false;
        }

        int widgetId = Im.Context.GetId(inputId);
        return Im.Context.IsFocused(widgetId);
    }

    private static void DrawHeaders(DocTable table)
    {
        var style = Im.Style;
        int selectedColumnIndex = GetSelectedColumnVisualIndex();
        int hoveredColumnIndex = GetHoveredColumnVisualIndex();

        // Header background
        Im.DrawRect(_headerRect.X, _headerRect.Y, _headerRect.Width, _headerRect.Height, style.Surface);

        var columnsClipRect = GetColumnsViewportRect(_headerRect.Y, _headerRect.Height);
        var pinnedColumnsClipRect = GetPinnedColumnsViewportRect(_headerRect.Y, _headerRect.Height);
        var scrollingColumnsClipRect = GetScrollableColumnsViewportRect(_headerRect.Y, _headerRect.Height);

        // Row number header (fixed region, does not scroll horizontally).
        Im.DrawRect(_headerRect.X, _headerRect.Y, RowNumberWidth, _headerRect.Height, style.Surface);
        Im.DrawLine(_headerRect.X + RowNumberWidth, _headerRect.Y, _headerRect.X + RowNumberWidth, _headerRect.Bottom, 1f, style.Border);

        DrawHeaderColumns(table, selectedColumnIndex, hoveredColumnIndex, scrollingColumnsClipRect, drawPinnedColumns: false);
        DrawHeaderColumns(table, selectedColumnIndex, hoveredColumnIndex, pinnedColumnsClipRect, drawPinnedColumns: true);

        Im.PushClipRect(columnsClipRect);
        if (_isColumnDragging && _columnDragSourceCol >= 0)
        {
            float insertIndicatorX = GetColumnInsertIndicatorX(_columnDragTargetInsertIndex);
            Im.DrawLine(insertIndicatorX, _headerRect.Y + 1f, insertIndicatorX, _headerRect.Bottom - 1f, 2f, style.Primary);
        }

        int resizeGuideColumnIndex = _resizingColIndex >= 0 ? _resizingColIndex : _hoveredResizeCol;
        if (resizeGuideColumnIndex >= 0 && resizeGuideColumnIndex < _colCount)
        {
            float guideX = _colX[resizeGuideColumnIndex] + _colW[resizeGuideColumnIndex];
            uint guideColor = _resizingColIndex >= 0 ? style.Primary : BlendColor(style.Primary, 0.55f, style.Border);
            Im.DrawLine(guideX, _headerRect.Y + 2f, guideX, _headerRect.Bottom - 2f, 2f, guideColor);
        }
        Im.PopClipRect();

        DrawStickyColumnsShadow(_headerRect.Y, _headerRect.Height);
        DrawAddColumnHeaderButton(table);

        // Header bottom border
        Im.DrawLine(_headerRect.X, _headerRect.Bottom, _headerRect.Right, _headerRect.Bottom, 1f, style.Border);
    }

    private static void DrawHeaderColumns(
        DocTable table,
        int selectedColumnIndex,
        int hoveredColumnIndex,
        ImRect clipRect,
        bool drawPinnedColumns)
    {
        if (clipRect.Width <= 0f || clipRect.Height <= 0f)
        {
            return;
        }

        var style = Im.Style;
        Im.PushClipRect(clipRect);
        for (int columnIndex = 0; columnIndex < _colCount; columnIndex++)
        {
            if (IsPinnedColumn(columnIndex) != drawPinnedColumns)
            {
                continue;
            }

            float headerX = _colX[columnIndex];
            float headerW = _colW[columnIndex];
            var column = GetVisibleColumn(table, columnIndex);
            bool isPrimaryKey = !string.IsNullOrWhiteSpace(table.Keys.PrimaryKeyColumnId) &&
                                string.Equals(table.Keys.PrimaryKeyColumnId, column.Id, StringComparison.Ordinal);
            int secondaryKeyIndex = FindSecondaryKeyIndex(table, column.Id);
            bool isSecondaryKey = secondaryKeyIndex >= 0;
            bool secondaryKeyIsUnique = isSecondaryKey &&
                                        secondaryKeyIndex < table.Keys.SecondaryKeys.Count &&
                                        table.Keys.SecondaryKeys[secondaryKeyIndex].Unique;
            bool isSchemaLockedColumn = IsColumnSchemaLocked(column);

            bool isSelectedColumn = selectedColumnIndex == columnIndex;
            bool isHoveredColumn = hoveredColumnIndex == columnIndex;
            if (isSelectedColumn || isHoveredColumn)
            {
                uint headerHighlight = isSelectedColumn
                    ? BlendColor(style.Primary, 0.30f, style.Surface)
                    : BlendColor(style.Hover, 0.45f, style.Surface);
                Im.DrawRect(headerX, _headerRect.Y, headerW, _headerRect.Height, headerHighlight);
                if (isSelectedColumn)
                {
                    Im.DrawRoundedRectStroke(headerX, _headerRect.Y, headerW, _headerRect.Height, 0f, style.Primary, 1.2f);
                }
            }

            if (_isColumnDragging && columnIndex == _columnDragSourceCol)
            {
                Im.DrawRect(headerX, _headerRect.Y, headerW, _headerRect.Height, ImStyle.WithAlpha(style.Primary, 70));
            }

            if (isSchemaLockedColumn)
            {
                Im.DrawRect(
                    headerX,
                    _headerRect.Y,
                    headerW,
                    _headerRect.Height,
                    ImStyle.WithAlpha(style.TextSecondary, 12));
            }

            if (ShouldShowColumnTearHandle(columnIndex))
            {
                var handleRect = GetColumnTearHandleRect(columnIndex);
                uint handleDotColor = columnIndex == GetSelectedColumnVisualIndex()
                    ? style.TextPrimary
                    : BlendColor(style.TextSecondary, 0.65f, style.TextPrimary);
                float dotX = handleRect.X + 3f;
                float dotY = handleRect.Y + 2f;
                for (int dotIndex = 0; dotIndex < ColumnTearHandleDotCount; dotIndex++)
                {
                    Im.DrawRoundedRect(dotX, dotY, ColumnTearHandleDotSize, ColumnTearHandleDotSize, 1f, handleDotColor);
                    dotX += ColumnTearHandleDotSize + ColumnTearHandleDotSpacing;
                }
            }

            float contentX = headerX + CellPaddingX;
            float textY = _headerRect.Y + (HeaderHeight - style.FontSize) * 0.5f;
            if (column.Kind == DocColumnKind.Relation &&
                !string.IsNullOrWhiteSpace(column.RelationDisplayColumnId))
            {
                Im.Text(_headerDisplayColumnIconText.AsSpan(), contentX, textY + 0.5f, style.FontSize - 1f, style.Secondary);
                contentX += Im.MeasureTextWidth(_headerDisplayColumnIconText.AsSpan(), style.FontSize - 1f) + 5f;
            }

            string keyIconText = isPrimaryKey
                ? _headerPrimaryKeyIconText
                : (isSecondaryKey ? _headerSecondaryKeyIconText : "");
            if (keyIconText.Length > 0)
            {
                uint keyIconColor = isPrimaryKey
                    ? style.Primary
                    : secondaryKeyIsUnique
                        ? ImStyle.WithAlpha(style.TextPrimary, 190)
                        : ImStyle.WithAlpha(style.TextSecondary, 190);
                Im.Text(keyIconText.AsSpan(), contentX, textY + 1f, style.FontSize - 2f, keyIconColor);
                contentX += Im.MeasureTextWidth(keyIconText.AsSpan(), style.FontSize - 2f) + 5f;
            }

            if (_inlineRenameColIndex == columnIndex)
            {
                string inputId = GetInlineRenameInputId(columnIndex);
                float nameWidth = GetHeaderColumnNameWidth(table, column, columnIndex, contentX);
                float inputY = _headerRect.Y + (_headerRect.Height - style.MinButtonHeight) * 0.5f;
                Im.TextInput(inputId, _renameColBuffer, ref _renameColBufferLength, _renameColBuffer.Length, contentX, inputY, nameWidth);

                if (_inlineRenameNeedsFocus)
                {
                    int widgetId = Im.Context.GetId(inputId);
                    Im.Context.RequestFocus(widgetId);
                    if (Im.TryGetTextInputState(inputId, out _))
                    {
                        if (_inlineRenameSelectAll)
                        {
                            Im.SetTextInputSelection(inputId, _renameColBufferLength, 0, _renameColBufferLength);
                        }
                        else
                        {
                            Im.SetTextInputSelection(inputId, _renameColBufferLength);
                        }
                    }

                    _inlineRenameNeedsFocus = false;
                    _inlineRenameSelectAll = false;
                }
            }
            else
            {
                uint headerTextColor = isSchemaLockedColumn
                    ? ImStyle.WithAlpha(style.TextPrimary, 185)
                    : style.TextPrimary;
                Im.Text(column.Name.AsSpan(), contentX, textY, style.FontSize, headerTextColor);
            }

            bool showHeaderMenuButton = _isInteractiveRender && _hoveredHeaderCol == columnIndex;
            var menuButtonRect = ImRect.Zero;
            bool hasMenuButtonRect = false;
            if (showHeaderMenuButton)
            {
                hasMenuButtonRect = TryGetHeaderMenuButtonRect(column, columnIndex, out menuButtonRect);
            }

            if (hasMenuButtonRect)
            {
                DrawHeaderMenuButton(column, menuButtonRect, isSchemaLockedColumn);
            }

            if (columnIndex > 0)
            {
                Im.DrawLine(headerX, _headerRect.Y, headerX, _headerRect.Bottom, 1f, style.Border);
            }
        }
        Im.PopClipRect();
    }

    private static void DrawStickyColumnsShadow(float y, float height)
    {
        if (_pinnedColumnsWidth <= 0f || height <= 0f)
        {
            return;
        }

        float columnsViewportWidth = GetColumnsViewportWidth();
        if (columnsViewportWidth <= 0f)
        {
            return;
        }

        float pinnedWidth = Math.Min(_pinnedColumnsWidth, columnsViewportWidth);
        if (pinnedWidth <= 0f || pinnedWidth >= columnsViewportWidth)
        {
            return;
        }

        float shadowStartX = _bodyRect.X + RowNumberWidth + pinnedWidth;

        // Always draw the right border of the pinned area
        Im.DrawLine(shadowStartX, y, shadowStartX, y + height, 1f, Im.Style.Border);

        // Only draw shadow when scrolled
        if (_scrollX > 0.01f)
        {
            float shadowWidth = Math.Min(StickyColumnsShadowWidth, columnsViewportWidth - pinnedWidth);
            if (shadowWidth > 0f)
            {
                uint shadowColor = ImStyle.Rgba(0, 0, 0);
                DrawHorizontalShadow(shadowStartX, y, shadowWidth, height, shadowColor, fadeRight: true);
            }
        }
    }

    private static void DrawHorizontalShadow(float x, float y, float width, float height, uint color, bool fadeRight)
    {
        if (width <= 0f || height <= 0f)
        {
            return;
        }

        int steps = Math.Max(1, StickyColumnsShadowSteps);
        float stepWidth = width / steps;
        for (int stepIndex = 0; stepIndex < steps; stepIndex++)
        {
            float t = (float)stepIndex / steps;
            float alpha = fadeRight ? (1f - t) : t;
            uint tinted = ImStyle.WithAlpha(color, (byte)(alpha * 80f));
            float stepX = x + stepIndex * stepWidth;
            Im.DrawRect(stepX, y, stepWidth + 0.5f, height, tinted);
        }
    }

    private static void DrawAddColumnHeaderButton(DocTable table)
    {
        if (!_isInteractiveRender)
        {
            return;
        }

        if (!CanAddColumns(table))
        {
            return;
        }

        if (_colCount >= 32)
        {
            return;
        }

        bool isMouseOverGrid = _gridRect.Contains(Im.MousePos);
        if (!isMouseOverGrid && !ImContextMenu.IsOpen("add_col_type_menu"))
        {
            return;
        }

        float buttonSize = HeaderHeight - 8f;
        float buttonY = _headerRect.Y + (_headerRect.Height - buttonSize) * 0.5f;
        float slotX = _colCount > 0 ? _addColumnSlotX : _bodyRect.X + RowNumberWidth;
        float slotWidth = GetAddColumnSlotWidth(table);
        var slotRect = new ImRect(slotX, _headerRect.Y, slotWidth, _headerRect.Height);

        var columnsViewportRect = GetScrollableColumnsViewportRect(_headerRect.Y, _headerRect.Height);
        if (columnsViewportRect.Width <= 0f)
        {
            return;
        }

        float visibleSlotLeft = MathF.Max(slotRect.X, columnsViewportRect.X);
        float visibleSlotRight = MathF.Min(slotRect.Right, columnsViewportRect.Right);
        if (visibleSlotRight - visibleSlotLeft < buttonSize)
        {
            return;
        }
        float buttonX = visibleSlotLeft + ((visibleSlotRight - visibleSlotLeft) - buttonSize) * 0.5f;

        if (Im.Button("+##doc_table_add_column", buttonX, buttonY, buttonSize, buttonSize))
        {
            MarkContextMenuOwner();
            ImContextMenu.Open("add_col_type_menu");
        }
    }

    private static float GetAddColumnSlotWidth(DocTable table)
    {
        return CanAddColumns(table) ? AddColumnSlotWidth : 0f;
    }

    private static bool CanAddColumns(DocTable table)
    {
        return !DocSystemTableRules.IsSchemaLocked(table) &&
               !table.IsSchemaLinked;
    }

    private static bool IsColumnSchemaLocked(DocColumn column)
    {
        return column.IsProjected || column.IsInherited;
    }

    private static bool IsColumnDataReadOnly(DocColumn column)
    {
        return column.IsProjected;
    }

    private static void DrawBody(DocWorkspace workspace, DocTable table)
    {
        var style = Im.Style;
        uint contentRowBackground = ImStyle.Lerp(style.Background, 0xFF000000, 0.24f);
        bool editOwnedByCurrentInstance = IsEditOwnedByCurrentInstance(workspace, table);
        int selectedColumnIndex = GetSelectedColumnVisualIndex();
        int hoveredColumnIndex = GetHoveredColumnVisualIndex();
        bool isMouseInRowGutter = IsMouseInRowGutter(Im.MousePos);
        int rowCount = _rowCount;
        if (rowCount == 0)
        {
            return;
        }

        var columnsViewportRect = GetColumnsViewportRect(_bodyRect.Y, _bodyRect.Height);
        var pinnedColumnsViewportRect = GetPinnedColumnsViewportRect(_bodyRect.Y, _bodyRect.Height);
        var scrollingColumnsViewportRect = GetScrollableColumnsViewportRect(_bodyRect.Y, _bodyRect.Height);

        // Row culling
        GetVisibleRowRange(rowCount, out int firstVisible, out int lastVisible);
        if (lastVisible < firstVisible)
        {
            return;
        }

        for (int rowIndex = firstVisible; rowIndex <= lastVisible; rowIndex++)
        {
            float rowY = GetRowTopY(rowIndex);
            float rowHeight = GetRowHeightAt(rowIndex);

            // Row background
            uint rowBg = contentRowBackground;

            // Hovered row (only when hovering gutter)
            if (isMouseInRowGutter && rowIndex == _hoveredRow)
            {
                rowBg = BlendColor(style.Hover, 0.68f, contentRowBackground);
            }

            // Multi-row selected
            if (_selectedRows.Contains(rowIndex))
            {
                rowBg = BlendColor(style.Primary, 0.16f, contentRowBackground);
            }

            Im.DrawRect(_bodyRect.X, rowY, _bodyRect.Width, rowHeight, rowBg);

            // Row number
            int numLen = FormatInt(rowIndex + 1, _rowNumBuf);
            float numWidth = Im.MeasureTextWidth(_rowNumBuf.AsSpan(0, numLen), style.FontSize);
            float numX = _bodyRect.X + RowNumberWidth - numWidth - 4f;
            float numY = rowY + (rowHeight - style.FontSize) * 0.5f;
            Im.Text(_rowNumBuf.AsSpan(0, numLen), numX, numY, style.FontSize, style.TextSecondary);

            if (_isInteractiveRender && (_rowDragSourceIndex == rowIndex || (rowIndex == _hoveredRow && isMouseInRowGutter)))
            {
                var rowHandleRect = GetRowDragHandleRect(rowIndex);
                uint handleDotColor = _hoveredRowDragHandle == rowIndex || _rowDragSourceIndex == rowIndex
                    ? style.TextPrimary
                    : BlendColor(style.TextSecondary, 0.70f, style.TextPrimary);
                float dotStartX = rowHandleRect.X;
                float dotStartY = rowHandleRect.Y;
                for (int dotRowIndex = 0; dotRowIndex < 3; dotRowIndex++)
                {
                    float dotY = dotStartY + dotRowIndex * (RowHandleDotSize + RowHandleDotSpacing);
                    Im.DrawRoundedRect(dotStartX, dotY, RowHandleDotSize, RowHandleDotSize, 1f, handleDotColor);
                    Im.DrawRoundedRect(dotStartX + RowHandleDotSize + RowHandleDotSpacing, dotY, RowHandleDotSize, RowHandleDotSize, 1f, handleDotColor);
                }
            }

            int sourceRowIndex = GetSourceRowIndex(rowIndex);
            var row = table.Rows[sourceRowIndex];
            if (scrollingColumnsViewportRect.Width > 0f)
            {
                Im.PushClipRect(scrollingColumnsViewportRect);
                DrawBodyColumnsForRow(
                    workspace,
                    table,
                    row,
                    sourceRowIndex,
                    rowIndex,
                    selectedColumnIndex,
                    hoveredColumnIndex,
                    editOwnedByCurrentInstance,
                    drawPinnedColumns: false,
                    style);
                Im.PopClipRect();
            }

            if (pinnedColumnsViewportRect.Width > 0f)
            {
                Im.PushClipRect(pinnedColumnsViewportRect);
                DrawBodyColumnsForRow(
                    workspace,
                    table,
                    row,
                    sourceRowIndex,
                    rowIndex,
                    selectedColumnIndex,
                    hoveredColumnIndex,
                    editOwnedByCurrentInstance,
                    drawPinnedColumns: true,
                    style);
                Im.PopClipRect();
            }

            // Row grid line (bottom of row)
            Im.DrawLine(_bodyRect.X, rowY + rowHeight, _bodyRect.X + _bodyRect.Width, rowY + rowHeight, 1f, style.Border);
        }

        // Column grid lines (vertical separators in body)
        float bodyTop = _bodyRect.Y;
        float bodyBot = _bodyRect.Y + Math.Min(Math.Max(0f, _rowContentHeight - _scrollY), _bodyRect.Height);
        if (scrollingColumnsViewportRect.Width > 0f)
        {
            Im.PushClipRect(scrollingColumnsViewportRect);
            for (int columnIndex = 1; columnIndex < _colCount; columnIndex++)
            {
                if (IsPinnedColumn(columnIndex))
                {
                    continue;
                }

                Im.DrawLine(_colX[columnIndex], bodyTop, _colX[columnIndex], bodyBot, 1f, style.Border);
            }
            Im.PopClipRect();
        }

        if (pinnedColumnsViewportRect.Width > 0f)
        {
            Im.PushClipRect(pinnedColumnsViewportRect);
            for (int columnIndex = 1; columnIndex < _colCount; columnIndex++)
            {
                if (!IsPinnedColumn(columnIndex))
                {
                    continue;
                }

                Im.DrawLine(_colX[columnIndex], bodyTop, _colX[columnIndex], bodyBot, 1f, style.Border);
            }
            Im.PopClipRect();
        }

        // Row number separator
        float rnX = _bodyRect.X + RowNumberWidth;
        Im.DrawLine(rnX, bodyTop, rnX, bodyBot, 1f, style.Border);
    }

    private static void DrawBodyColumnsForRow(
        DocWorkspace workspace,
        DocTable table,
        DocRow row,
        int sourceRowIndex,
        int rowIndex,
        int selectedColumnIndex,
        int hoveredColumnIndex,
        bool editOwnedByCurrentInstance,
        bool drawPinnedColumns,
        ImStyle style)
    {
        if (selectedColumnIndex >= 0 &&
            selectedColumnIndex < _colCount &&
            IsPinnedColumn(selectedColumnIndex) == drawPinnedColumns)
        {
            var selectedColumnCellRect = GetCellRect(rowIndex, selectedColumnIndex);
            Im.DrawRect(
                selectedColumnCellRect.X,
                selectedColumnCellRect.Y,
                selectedColumnCellRect.Width,
                selectedColumnCellRect.Height,
                ImStyle.WithAlpha(style.Primary, 34));
        }

        if (hoveredColumnIndex >= 0 &&
            hoveredColumnIndex < _colCount &&
            hoveredColumnIndex != selectedColumnIndex &&
            IsPinnedColumn(hoveredColumnIndex) == drawPinnedColumns)
        {
            var hoveredColumnCellRect = GetCellRect(rowIndex, hoveredColumnIndex);
            Im.DrawRect(
                hoveredColumnCellRect.X,
                hoveredColumnCellRect.Y,
                hoveredColumnCellRect.Width,
                hoveredColumnCellRect.Height,
                ImStyle.WithAlpha(style.Hover, 46));
        }

        if (_hoveredRow == rowIndex &&
            _hoveredCol >= 0 &&
            _hoveredCol < _colCount &&
            IsPinnedColumn(_hoveredCol) == drawPinnedColumns)
        {
            var hoveredCellRect = GetCellRect(rowIndex, _hoveredCol);
            Im.DrawRect(
                hoveredCellRect.X,
                hoveredCellRect.Y,
                hoveredCellRect.Width,
                hoveredCellRect.Height,
                ImStyle.WithAlpha(style.Hover, 58));
        }

        if (_isColumnDragging &&
            _columnDragSourceCol >= 0 &&
            _columnDragSourceCol < _colCount &&
            IsPinnedColumn(_columnDragSourceCol) == drawPinnedColumns)
        {
            var draggedColumnCellRect = GetCellRect(rowIndex, _columnDragSourceCol);
            Im.DrawRect(
                draggedColumnCellRect.X,
                draggedColumnCellRect.Y,
                draggedColumnCellRect.Width,
                draggedColumnCellRect.Height,
                ImStyle.WithAlpha(style.Primary, 28));
        }

        for (int columnIndex = 0; columnIndex < _colCount; columnIndex++)
        {
            if (IsPinnedColumn(columnIndex) != drawPinnedColumns)
            {
                continue;
            }

            if (editOwnedByCurrentInstance &&
                workspace.EditState.RowIndex == rowIndex &&
                workspace.EditState.ColIndex == columnIndex)
            {
                continue;
            }

            var column = GetVisibleColumn(table, columnIndex);
            var cell = row.GetCell(column);
            var cellRect = GetCellRect(rowIndex, columnIndex);

            if (IsColumnSchemaLocked(column))
            {
                Im.DrawRect(cellRect.X, cellRect.Y, cellRect.Width, cellRect.Height,
                    ImStyle.WithAlpha(style.TextSecondary, 12));
            }

            RenderCellContent(workspace, table, row, sourceRowIndex, column, cell, cellRect, style);
        }
    }

    private static void DrawSelection()
    {
        var style = Im.Style;
        int selectedColumnIndex = GetSelectedColumnVisualIndex();
        var columnsViewportRect = GetColumnsViewportRect(_bodyRect.Y, _bodyRect.Height);
        Im.PushClipRect(columnsViewportRect);

        // Cell range selection highlight
        if (_selStartRow >= 0 && _selStartCol >= 0 && _selEndRow >= 0 && _selEndCol >= 0)
        {
            int minRow = Math.Min(_selStartRow, _selEndRow);
            int maxRow = Math.Max(_selStartRow, _selEndRow);
            int minCol = Math.Min(_selStartCol, _selEndCol);
            int maxCol = Math.Max(_selStartCol, _selEndCol);

            // Only draw range highlight if it's more than a single cell
            bool isRange = minRow != maxRow || minCol != maxCol;
            if (isRange)
            {
                uint selectionStroke = ImStyle.WithAlpha(style.Primary, 70);
                for (int r = minRow; r <= maxRow; r++)
                {
                    for (int c = minCol; c <= maxCol; c++)
                    {
                        var cellRect = GetCellRect(r, c);
                        if (!TryGetVisibleColumnRect(c, cellRect.Y, cellRect.Height, out var visibleCellRect))
                        {
                            continue;
                        }

                        Im.DrawRoundedRectStroke(
                            visibleCellRect.X,
                            visibleCellRect.Y,
                            visibleCellRect.Width,
                            visibleCellRect.Height,
                            0f,
                            selectionStroke,
                            1f);
                    }
                }

                // Border around entire selection range
                var topLeft = GetCellRect(minRow, minCol);
                var bottomRight = GetCellRect(maxRow, maxCol);
                float selX = topLeft.X;
                float selY = topLeft.Y;
                float selW = bottomRight.Right - topLeft.X;
                float selH = bottomRight.Bottom - topLeft.Y;
                Im.DrawRoundedRectStroke(selX, selY, selW, selH, 0f, style.Primary, 2f);
            }
        }

        // Active cell border
        if (_activeRow >= 0 && _activeCol >= 0)
        {
            var activeRect = GetCellRect(_activeRow, _activeCol);
            if (TryGetVisibleColumnRect(_activeCol, activeRect.Y, activeRect.Height, out var visibleActiveRect))
            {
                Im.DrawRoundedRectStroke(visibleActiveRect.X, visibleActiveRect.Y, visibleActiveRect.Width, visibleActiveRect.Height, 0f, style.Primary, 2f);
            }
        }

        if (selectedColumnIndex >= 0 && selectedColumnIndex < _colCount)
        {
            if (TryGetVisibleColumnRect(selectedColumnIndex, _bodyRect.Y, _bodyRect.Height, out var visibleSelectedColumnRect))
            {
                Im.DrawRoundedRectStroke(
                    visibleSelectedColumnRect.X,
                    visibleSelectedColumnRect.Y,
                    visibleSelectedColumnRect.Width,
                    visibleSelectedColumnRect.Height,
                    0f,
                    style.Primary,
                    2f);
            }
        }

        if (_isFillHandleDragging &&
            _fillDragSourceMinRow >= 0 &&
            _fillDragSourceMinCol >= 0 &&
            _fillDragSourceMaxRow >= _fillDragSourceMinRow &&
            _fillDragSourceMaxCol >= _fillDragSourceMinCol &&
            _fillDragTargetRow >= _fillDragSourceMaxRow &&
            _fillDragTargetCol >= _fillDragSourceMaxCol)
        {
            var previewTopLeft = GetCellRect(_fillDragSourceMinRow, _fillDragSourceMinCol);
            var previewBottomRight = GetCellRect(_fillDragTargetRow, _fillDragTargetCol);
            float previewX = previewTopLeft.X;
            float previewY = previewTopLeft.Y;
            float previewWidth = previewBottomRight.Right - previewTopLeft.X;
            float previewHeight = previewBottomRight.Bottom - previewTopLeft.Y;
            Im.DrawRoundedRectStroke(
                previewX,
                previewY,
                previewWidth,
                previewHeight,
                0f,
                ImStyle.WithAlpha(style.Primary, 170),
                2f);
        }

        if (TryGetSelectionFillHandleRect(out ImRect fillHandleRect))
        {
            uint fillHandleColor = _isFillHandleDragging
                ? style.Primary
                : ImStyle.WithAlpha(style.Primary, 190);
            Im.DrawRect(
                fillHandleRect.X,
                fillHandleRect.Y,
                fillHandleRect.Width,
                fillHandleRect.Height,
                fillHandleColor);
            Im.DrawRoundedRectStroke(
                fillHandleRect.X,
                fillHandleRect.Y,
                fillHandleRect.Width,
                fillHandleRect.Height,
                0f,
                style.Background,
                1f);
        }

        if (_isColumnDragging && _columnDragSourceCol >= 0)
        {
            float insertIndicatorX = GetColumnInsertIndicatorX(_columnDragTargetInsertIndex);
            Im.DrawLine(insertIndicatorX, _bodyRect.Y + 1f, insertIndicatorX, _bodyRect.Bottom - 1f, 2f, style.Primary);
        }

        Im.PopClipRect();

        if (_isRowDragging && _rowDragSourceIndex >= 0)
        {
            float insertIndicatorY = GetRowInsertIndicatorY(_rowDragTargetInsertIndex, _rowCount);
            float clampedIndicatorY = Math.Clamp(insertIndicatorY, _bodyRect.Y + 1f, _bodyRect.Bottom - 1f);
            float indicatorRight = _bodyRect.Right - (_hasVerticalScrollbar ? ScrollbarWidth : 0f);
            Im.PushClipRect(_bodyRect);
            Im.DrawLine(_bodyRect.X + 1f, clampedIndicatorY, indicatorRight - 1f, clampedIndicatorY, 2f, style.Primary);
            Im.PopClipRect();
        }
    }

    private static void DrawRowAddBelowOverlay()
    {
        if (!_isInteractiveRender || _hoveredRow < 0 || _hoveredRow >= _rowCount)
        {
            return;
        }

        if (!IsMouseInRowGutter(Im.MousePos))
        {
            return;
        }

        var style = Im.Style;
        var addButtonRect = GetRowAddBelowButtonRect(_hoveredRow);
        uint addButtonColor = _hoveredRowAddBelow == _hoveredRow
            ? BlendColor(style.Primary, 0.32f, style.Surface)
            : BlendColor(style.Surface, 0.28f, style.Background);
        Im.DrawRoundedRect(addButtonRect.X, addButtonRect.Y, addButtonRect.Width, addButtonRect.Height, addButtonRect.Width * 0.5f, addButtonColor);
        Im.DrawRoundedRectStroke(addButtonRect.X, addButtonRect.Y, addButtonRect.Width, addButtonRect.Height, addButtonRect.Width * 0.5f, style.Border, 1f);

        float plusHalf = 3f;
        float plusCenterX = addButtonRect.X + addButtonRect.Width * 0.5f;
        float plusCenterY = addButtonRect.Y + addButtonRect.Height * 0.5f;
        Im.DrawLine(plusCenterX - plusHalf, plusCenterY, plusCenterX + plusHalf, plusCenterY, 1.4f, style.TextPrimary);
        Im.DrawLine(plusCenterX, plusCenterY - plusHalf, plusCenterX, plusCenterY + plusHalf, 1.4f, style.TextPrimary);
    }

    private static void DrawScrollbars(DocWorkspace workspace, DocTable table)
    {
        float contentHeight = _rowContentHeight;
        float columnViewportWidth = Math.Max(0f, _scrollableColumnsViewportWidth);

        if (_hasVerticalScrollbar)
        {
            int verticalScrollbarId = Im.Context.GetId("doc_spreadsheet_scroll_y");
            var verticalScrollbarRect = GetVerticalScrollbarRect();

            ImScrollbar.DrawVertical(verticalScrollbarId, verticalScrollbarRect, ref _scrollY, _bodyRect.Height, contentHeight);
        }

        if (_hasHorizontalScrollbar)
        {
            int horizontalScrollbarId = Im.Context.GetId("doc_spreadsheet_scroll_x");
            var horizontalScrollbarRect = GetHorizontalScrollbarRect(columnViewportWidth);

            bool horizontalChanged = DrawHorizontalScrollbar(horizontalScrollbarId, horizontalScrollbarRect, ref _scrollX, columnViewportWidth, _columnContentWidth);
            if (horizontalChanged)
            {
                ComputeLayout(workspace, table);
            }
        }
    }

    private static bool DrawHorizontalScrollbar(
        int widgetId,
        ImRect rect,
        ref float scrollOffset,
        float viewSize,
        float contentSize)
    {
        var ctx = Im.Context;
        var style = Im.Style;
        var input = ctx.Input;
        var mousePos = Im.MousePos;

        Im.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, style.ScrollbarTrack);
        if (contentSize <= viewSize || rect.Width <= 0f)
        {
            scrollOffset = 0f;
            return false;
        }

        float maxScroll = contentSize - viewSize;
        if (maxScroll <= 0f)
        {
            scrollOffset = 0f;
            return false;
        }

        scrollOffset = Math.Clamp(scrollOffset, 0f, maxScroll);
        float visibleRatio = viewSize / contentSize;
        float thumbWidth = rect.Width * visibleRatio;
        if (thumbWidth < style.MinButtonHeight)
        {
            thumbWidth = style.MinButtonHeight;
        }

        if (thumbWidth > rect.Width)
        {
            thumbWidth = rect.Width;
        }

        float scrollRatio = scrollOffset / maxScroll;
        float thumbX = rect.X + scrollRatio * (rect.Width - thumbWidth);
        var thumbRect = new ImRect(thumbX, rect.Y, thumbWidth, rect.Height);

        bool hovered = thumbRect.Contains(mousePos);
        bool trackHovered = rect.Contains(mousePos);
        bool changed = false;

        if (hovered)
        {
            ctx.SetHot(widgetId);
            if (input.MousePressed && ctx.ActiveId == 0)
            {
                ctx.SetActive(widgetId);
            }
        }
        else if (trackHovered && input.MousePressed && ctx.ActiveId == 0)
        {
            if (mousePos.X < thumbX)
            {
                scrollOffset = Math.Max(0f, scrollOffset - viewSize);
                changed = true;
            }
            else if (mousePos.X > thumbX + thumbWidth)
            {
                scrollOffset = Math.Min(maxScroll, scrollOffset + viewSize);
                changed = true;
            }
        }

        if (ctx.IsActive(widgetId))
        {
            if (input.MouseDown)
            {
                float newThumbX = mousePos.X - thumbWidth * 0.5f;
                float newScrollRatio = (newThumbX - rect.X) / (rect.Width - thumbWidth);
                newScrollRatio = Math.Clamp(newScrollRatio, 0f, 1f);
                float newScrollOffset = newScrollRatio * maxScroll;
                if (MathF.Abs(newScrollOffset - scrollOffset) > 0.01f)
                {
                    scrollOffset = newScrollOffset;
                    changed = true;
                }
            }
            else
            {
                ctx.ClearActive();
            }
        }

        scrollOffset = Math.Clamp(scrollOffset, 0f, maxScroll);
        scrollRatio = scrollOffset / maxScroll;
        thumbX = rect.X + scrollRatio * (rect.Width - thumbWidth);

        uint thumbColor = ctx.IsActive(widgetId) ? style.Active : (ctx.IsHot(widgetId) ? style.Hover : style.ScrollbarThumb);
        float thumbRadius = style.CornerRadius * 0.5f;
        float maxThumbRadius = Math.Min(rect.Height * 0.5f, thumbWidth * 0.5f);
        if (thumbRadius > maxThumbRadius)
        {
            thumbRadius = maxThumbRadius;
        }

        Im.DrawRoundedRect(thumbX, rect.Y, thumbWidth, rect.Height, thumbRadius, thumbColor);
        return changed;
    }

    private static ImRect GetVerticalScrollbarRect()
    {
        return new ImRect(
            _gridRect.Right - ScrollbarWidth,
            _bodyRect.Y,
            ScrollbarWidth,
            _bodyRect.Height);
    }

    private static ImRect GetVerticalScrollbarRect(EmbeddedSpreadsheetViewState state)
    {
        return new ImRect(
            state.GridRectX + state.GridRectWidth - ScrollbarWidth,
            state.BodyRectY,
            ScrollbarWidth,
            state.BodyRectHeight);
    }

    private static ImRect GetHorizontalScrollbarRect(float columnViewportWidth)
    {
        return new ImRect(
            _bodyRect.X + RowNumberWidth + _pinnedColumnsWidth,
            _bodyRect.Bottom,
            columnViewportWidth,
            HorizontalScrollbarHeight);
    }

    private static ImRect GetHorizontalScrollbarRect(EmbeddedSpreadsheetViewState state, float columnViewportWidth)
    {
        return new ImRect(
            state.BodyRectX + RowNumberWidth + state.PinnedColumnsWidth,
            state.BodyRectY + state.BodyRectHeight,
            columnViewportWidth,
            HorizontalScrollbarHeight);
    }

    private static bool WouldWheelMoveOffset(float currentOffset, float wheelAmount, float scrollStep, float maxScroll)
    {
        if (maxScroll <= 0f)
        {
            return false;
        }

        float nextOffset = Math.Clamp(currentOffset - wheelAmount * scrollStep, 0f, maxScroll);
        return MathF.Abs(nextOffset - currentOffset) > 0.01f;
    }

    private static bool IsMouseOverScrollableTableRegion(Vector2 mousePos)
    {
        if (_gridRect.Contains(mousePos))
        {
            return true;
        }

        float titleRowHeight = TableTitleRowHeight + TableTitleBottomSpacing;
        if (titleRowHeight <= 0f)
        {
            return false;
        }

        var titleRowRect = new ImRect(_gridRect.X, _gridRect.Y - titleRowHeight, _gridRect.Width, titleRowHeight);
        return titleRowRect.Contains(mousePos);
    }

    private static bool ShouldRouteWheelToHorizontalScrollbar(
        Vector2 mousePos,
        bool canScrollHorizontally,
        bool canScrollVertically,
        ImRect horizontalScrollbarRect,
        ImRect verticalScrollbarRect)
    {
        if (canScrollHorizontally && !canScrollVertically)
        {
            return true;
        }

        if (!canScrollHorizontally && canScrollVertically)
        {
            return false;
        }

        if (!canScrollHorizontally && !canScrollVertically)
        {
            return false;
        }

        float distanceToHorizontalScrollbar = DistanceFromPointToRect(mousePos, horizontalScrollbarRect);
        float distanceToVerticalScrollbar = DistanceFromPointToRect(mousePos, verticalScrollbarRect);
        return distanceToHorizontalScrollbar <= distanceToVerticalScrollbar;
    }

    private static float DistanceFromPointToRect(Vector2 point, ImRect rect)
    {
        float dx = 0f;
        if (point.X < rect.X)
        {
            dx = rect.X - point.X;
        }
        else if (point.X > rect.Right)
        {
            dx = point.X - rect.Right;
        }

        float dy = 0f;
        if (point.Y < rect.Y)
        {
            dy = rect.Y - point.Y;
        }
        else if (point.Y > rect.Bottom)
        {
            dy = point.Y - rect.Bottom;
        }

        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    // =====================================================================
    //  Cell content rendering
    // =====================================================================

    private static int TrimTrailingWhitespaceLength(ReadOnlySpan<char> text)
    {
        int endIndex = text.Length;
        while (endIndex > 0 && char.IsWhiteSpace(text[endIndex - 1]))
        {
            endIndex--;
        }

        return endIndex;
    }

    private static void DrawWrappedCellText(
        ReadOnlySpan<char> text,
        ImRect cellRect,
        ImStyle style,
        uint color)
    {
        if (text.Length == 0)
        {
            return;
        }

        float maxWidth = Math.Max(0f, cellRect.Width - (CellPaddingX * 2f));
        if (maxWidth <= 1f)
        {
            return;
        }

        int wrappedLineCount = CountWrappedTextLines(text, maxWidth, style.FontSize);
        float lineHeight = GetWrappedLineHeight(style.FontSize);
        float textX = cellRect.X + CellPaddingX;
        float textY = wrappedLineCount <= 1
            ? cellRect.Y + (cellRect.Height - style.FontSize) * 0.5f
            : cellRect.Y + CellPaddingY;
        float maxY = wrappedLineCount <= 1
            ? cellRect.Bottom
            : (cellRect.Bottom - CellPaddingY + 0.01f);

        float currentY = textY;
        int segmentStart = 0;
        while (segmentStart <= text.Length && currentY + style.FontSize <= maxY + 0.01f)
        {
            int newlineOffset = segmentStart < text.Length ? text[segmentStart..].IndexOf('\n') : -1;
            int segmentEndExclusive = newlineOffset >= 0 ? segmentStart + newlineOffset : text.Length;
            ReadOnlySpan<char> segment = text[segmentStart..segmentEndExclusive];

            if (segment.Length == 0)
            {
                currentY += lineHeight;
            }
            else
            {
                int segmentCursor = 0;
                while (segmentCursor < segment.Length && currentY + style.FontSize <= maxY + 0.01f)
                {
                    ReadOnlySpan<char> remaining = segment[segmentCursor..];
                    int wrappedLength = FindWrappedLineLength(remaining, maxWidth, style.FontSize);
                    wrappedLength = Math.Clamp(wrappedLength, 1, remaining.Length);
                    ReadOnlySpan<char> line = remaining[..wrappedLength];
                    int trimmedLength = TrimTrailingWhitespaceLength(line);
                    if (trimmedLength > 0)
                    {
                        Im.Text(line[..trimmedLength], textX, currentY, style.FontSize, color);
                    }

                    currentY += lineHeight;
                    segmentCursor += wrappedLength;
                    while (segmentCursor < segment.Length && char.IsWhiteSpace(segment[segmentCursor]))
                    {
                        segmentCursor++;
                    }
                }
            }

            if (newlineOffset < 0)
            {
                break;
            }

            segmentStart = segmentEndExclusive + 1;
            if (segmentStart == text.Length)
            {
                currentY += lineHeight;
                break;
            }
        }
    }

    private static void RenderCellContent(
        DocWorkspace workspace,
        DocTable table,
        DocRow row,
        int sourceRowIndex,
        DocColumn col,
        DocCellValue cell,
        ImRect cellRect,
        ImStyle style)
    {
        float textY = cellRect.Y + (cellRect.Height - style.FontSize) * 0.5f;
        if (IsFormulaErrorCell(col, cell))
        {
            uint errorColor = BlendColor(style.Secondary, 0.68f, style.TextPrimary);
            Im.Text("#ERR".AsSpan(), cellRect.X + CellPaddingX, textY, style.FontSize, errorColor);
            return;
        }

        if (TryGetColumnUiPlugin(col, out var uiPlugin) &&
            uiPlugin.DrawCell(workspace, table, row, sourceRowIndex, col, cell, cellRect, style))
        {
            return;
        }

        if (cell.HasCellFormulaExpression)
        {
            float badgeFontSize = MathF.Max(9f, style.FontSize - 3f);
            Im.Text(
                _headerFormulaIconText.AsSpan(),
                cellRect.X + 2f,
                cellRect.Y + 1f,
                badgeFontSize,
                style.TextSecondary);
        }

        switch (col.Kind)
        {
            case DocColumnKind.Id:
            {
                string text = cell.StringValue ?? "";
                if (string.IsNullOrWhiteSpace(text))
                {
                    text = row.Id;
                }

                if (text.Length > 0)
                {
                    DrawWrappedCellText(text.AsSpan(), cellRect, style, style.TextPrimary);
                }

                break;
            }
            case DocColumnKind.Text:
            case DocColumnKind.Select:
            {
                string text = cell.StringValue ?? "";
                if (text.Length > 0)
                {
                    DrawWrappedCellText(text.AsSpan(), cellRect, style, style.TextPrimary);
                }

                break;
            }
            case DocColumnKind.Formula:
            {
                string formulaText = cell.StringValue ?? "";
                if (formulaText.Length > 0)
                {
                    DrawWrappedCellText(formulaText.AsSpan(), cellRect, style, style.TextPrimary);
                }
                else
                {
                    Span<char> numberBuffer = stackalloc char[32];
                    if (cell.NumberValue.TryFormat(numberBuffer, out int written, "G"))
                    {
                        DrawWrappedCellText(numberBuffer[..written], cellRect, style, style.TextPrimary);
                    }
                }

                break;
            }
            case DocColumnKind.TableRef:
            {
                string tableId = cell.StringValue ?? "";
                if (tableId.Length <= 0)
                {
                    break;
                }

                string tableLabel = ResolveTableRefLabel(workspace, tableId);
                if (tableLabel.Length > 0)
                {
                    DrawWrappedCellText(tableLabel.AsSpan(), cellRect, style, style.TextPrimary);
                }

                break;
            }
            case DocColumnKind.Relation:
            {
                string relationRowId = cell.StringValue ?? "";
                string relationLabel = workspace.ResolveRelationDisplayLabel(col, relationRowId);
                if (relationLabel.Length > 0)
                {
                    DrawWrappedCellText(relationLabel.AsSpan(), cellRect, style, style.TextPrimary);
                }

                break;
            }
            case DocColumnKind.Number:
            {
                Span<char> buf = stackalloc char[32];
                string numberFormat = IsIntegerNumberColumn(col) ? "0" : "F2";
                if (cell.NumberValue.TryFormat(buf, out int written, numberFormat))
                {
                    float numW = Im.MeasureTextWidth(buf[..written], style.FontSize);
                    float numX = cellRect.Right - CellPaddingX - numW;
                    Im.Text(buf[..written], numX, textY, style.FontSize, style.TextPrimary);
                }
                break;
            }
            case DocColumnKind.Vec2:
            {
                DrawWrappedCellText(FormatVectorCellText(cell, 2).AsSpan(), cellRect, style, style.TextPrimary);
                break;
            }
            case DocColumnKind.Vec3:
            {
                DrawWrappedCellText(FormatVectorCellText(cell, 3).AsSpan(), cellRect, style, style.TextPrimary);
                break;
            }
            case DocColumnKind.Vec4:
            {
                DrawWrappedCellText(FormatVectorCellText(cell, 4).AsSpan(), cellRect, style, style.TextPrimary);
                break;
            }
            case DocColumnKind.Color:
            {
                DrawWrappedCellText(FormatColorCellText(cell).AsSpan(), cellRect, style, style.TextPrimary);
                break;
            }
            case DocColumnKind.Spline:
            {
                DrawSplineCellPreview(cell, cellRect);
                break;
            }
            case DocColumnKind.TextureAsset:
            case DocColumnKind.MeshAsset:
            case DocColumnKind.AudioAsset:
            case DocColumnKind.UiAsset:
            {
                RenderAssetCellContent(workspace, col, cell, cellRect, style);
                break;
            }
            case DocColumnKind.Checkbox:
            {
                float boxSize = 16f;
                float boxX = cellRect.X + (cellRect.Width - boxSize) * 0.5f;
                float boxY = cellRect.Y + (cellRect.Height - boxSize) * 0.5f;
                float radius = 3f;

                Im.DrawRoundedRectStroke(boxX, boxY, boxSize, boxSize, radius, style.Border, 1.5f);
                if (cell.BoolValue)
                {
                    Im.DrawRoundedRect(boxX + 2, boxY + 2, boxSize - 4, boxSize - 4, radius - 1, style.Primary);
                    // Checkmark
                    Im.DrawLine(boxX + 4, boxY + boxSize * 0.5f, boxX + boxSize * 0.4f, boxY + boxSize - 5, 2f, 0xFFFFFFFF);
                    Im.DrawLine(boxX + boxSize * 0.4f, boxY + boxSize - 5, boxX + boxSize - 4, boxY + 4, 2f, 0xFFFFFFFF);
                }
                break;
            }
            case DocColumnKind.Subtable:
            {
                DrawSubtableCellContent(workspace, table, row, sourceRowIndex, col, cellRect, style);
                break;
            }
        }
    }

    private static string FormatVectorCellText(DocCellValue cellValue, int dimension)
    {
        return dimension switch
        {
            2 => "(" + cellValue.XValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                 cellValue.YValue.ToString("G", CultureInfo.InvariantCulture) + ")",
            3 => "(" + cellValue.XValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                 cellValue.YValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                 cellValue.ZValue.ToString("G", CultureInfo.InvariantCulture) + ")",
            _ => "(" + cellValue.XValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                 cellValue.YValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                 cellValue.ZValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                 cellValue.WValue.ToString("G", CultureInfo.InvariantCulture) + ")",
        };
    }

    private static string FormatColorCellText(DocCellValue cellValue)
    {
        return "rgba(" + cellValue.XValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
               cellValue.YValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
               cellValue.ZValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
               cellValue.WValue.ToString("G", CultureInfo.InvariantCulture) + ")";
    }

    private static void DrawSubtableCellContent(
        DocWorkspace workspace,
        DocTable parentTable,
        DocRow parentRow,
        int sourceRowIndex,
        DocColumn subtableColumn,
        ImRect cellRect,
        ImStyle style)
    {
        int itemCount = GetCachedSubtableItemCount(workspace, parentTable, subtableColumn, sourceRowIndex);
        string normalizedRendererId = NormalizeSubtableDisplayRendererId(subtableColumn.SubtableDisplayRendererId);
        DocSubtablePreviewQuality previewQuality = ResolveEffectiveSubtableDisplayPreviewQuality(workspace, subtableColumn);
        if (previewQuality == DocSubtablePreviewQuality.Off ||
            string.IsNullOrWhiteSpace(normalizedRendererId))
        {
            DrawSubtableItemCountLabel(cellRect, style, itemCount);
            return;
        }

        if (!TryResolveSubtableDisplayRendererKind(
                normalizedRendererId,
                out var rendererKind,
                out string? customRendererId))
        {
            DrawSubtableItemCountLabel(cellRect, style, itemCount);
            return;
        }

        if (previewQuality == DocSubtablePreviewQuality.Lite &&
            rendererKind != SubtableDisplayRendererKind.Grid)
        {
            rendererKind = SubtableDisplayRendererKind.Grid;
            customRendererId = null;
        }

        if (string.IsNullOrWhiteSpace(subtableColumn.SubtableId))
        {
            DrawSubtableItemCountLabel(cellRect, style, itemCount);
            return;
        }

        DocTable? childTable = FindTableById(workspace, subtableColumn.SubtableId);
        if (childTable == null)
        {
            DrawSubtableItemCountLabel(cellRect, style, itemCount);
            return;
        }

        float availableWidth = Math.Max(0f, cellRect.Width - (CellPaddingX * 2f));
        float availableHeight = Math.Max(0f, cellRect.Height - (CellPaddingY * 2f));
        if (availableWidth <= 1f || availableHeight <= 1f)
        {
            DrawSubtableItemCountLabel(cellRect, style, itemCount);
            return;
        }

        float previewWidth = ResolveSubtableDisplayPreviewWidth(subtableColumn, availableWidth);
        float previewHeight =
            previewQuality == DocSubtablePreviewQuality.Full &&
            rendererKind == SubtableDisplayRendererKind.Grid &&
            !IsRenderingSubtableEmbeddedGrid()
                ? availableHeight
                : Math.Min(availableHeight, ResolveSubtableDisplayPreviewHeight(workspace, subtableColumn));
        if (previewWidth <= 1f || previewHeight <= 1f)
        {
            DrawSubtableItemCountLabel(cellRect, style, itemCount);
            return;
        }

        float previewX = cellRect.X + CellPaddingX;
        float previewY = cellRect.Y + CellPaddingY + ((availableHeight - previewHeight) * 0.5f);
        var previewRect = new ImRect(previewX, previewY, previewWidth, previewHeight);

        string parentRowId = ResolveSubtableParentRowId(workspace, parentTable, subtableColumn, sourceRowIndex);
        if (string.IsNullOrEmpty(parentRowId))
        {
            parentRowId = parentRow.Id;
        }

        bool bypassBudget =
            previewQuality == DocSubtablePreviewQuality.Full &&
            rendererKind == SubtableDisplayRendererKind.Grid &&
            !IsRenderingSubtableEmbeddedGrid();
        if (!bypassBudget && !TryConsumeSubtablePreviewBudget(previewQuality))
        {
            DrawSubtableItemCountLabel(cellRect, style, itemCount);
            return;
        }

        bool drewPreview = false;
        Im.PushClipRect(previewRect);
        switch (rendererKind)
        {
            case SubtableDisplayRendererKind.Grid:
                if (previewQuality == DocSubtablePreviewQuality.Full && !IsRenderingSubtableEmbeddedGrid())
                {
                    DrawSubtableEmbeddedGrid(
                        workspace,
                        childTable,
                        childTable.ParentRowColumnId,
                        parentRowId,
                        subtableColumn,
                        previewRect,
                        style);
                }
                else
                {
                    DrawSubtableGridPreview(
                        workspace,
                        childTable,
                        childTable.ParentRowColumnId,
                        parentRowId,
                        previewRect,
                        style);
                }
                drewPreview = true;
                break;
            case SubtableDisplayRendererKind.Board:
            {
                DocView boardView = ResolveSubtablePreviewView(
                    childTable,
                    DocViewType.Board,
                    customRendererId: null);
                BoardRenderer.Draw(
                    workspace,
                    childTable,
                    boardView,
                    previewRect,
                    interactive: false,
                    parentRowColumnId: childTable.ParentRowColumnId,
                    parentRowId: parentRowId);
                drewPreview = true;
                break;
            }
            case SubtableDisplayRendererKind.Calendar:
            {
                DocView calendarView = ResolveSubtablePreviewView(
                    childTable,
                    DocViewType.Calendar,
                    customRendererId: null);
                CalendarRenderer.Draw(
                    workspace,
                    childTable,
                    calendarView,
                    previewRect,
                    interactive: false,
                    parentRowColumnId: childTable.ParentRowColumnId,
                    parentRowId: parentRowId);
                drewPreview = true;
                break;
            }
            case SubtableDisplayRendererKind.Chart:
            {
                DocView chartView = ResolveSubtablePreviewView(
                    childTable,
                    DocViewType.Chart,
                    customRendererId: null);
                ChartRenderer.Draw(
                    workspace,
                    childTable,
                    chartView,
                    previewRect,
                    parentRowColumnId: childTable.ParentRowColumnId,
                    parentRowId: parentRowId);
                drewPreview = true;
                break;
            }
            case SubtableDisplayRendererKind.Custom:
            {
                if (!string.IsNullOrWhiteSpace(customRendererId) &&
                    TableViewRendererRegistry.TryGet(customRendererId, out var customRenderer))
                {
                    _subtablePreviewEditorContext.Configure(
                        workspace,
                        childTable.ParentRowColumnId,
                        parentRowId);

                    DocView customView = ResolveSubtablePreviewView(
                        childTable,
                        DocViewType.Custom,
                        customRendererId);
                    bool customPreviewDrawn = false;
                    if (customRenderer is IDerpDocSubtableDisplayRenderer subtableDisplayRenderer)
                    {
                        customPreviewDrawn = subtableDisplayRenderer.DrawSubtableDisplayPreview(
                            _subtablePreviewEditorContext,
                            childTable,
                            customView,
                            subtableColumn,
                            subtableColumn.PluginSettingsJson,
                            previewRect,
                            interactive: false,
                            stateKey: parentRow.Id);
                    }

                    if (!customPreviewDrawn &&
                        !customRenderer.DrawEmbedded(
                            _subtablePreviewEditorContext,
                            childTable,
                            customView,
                            previewRect,
                            interactive: false,
                            stateKey: parentRow.Id))
                    {
                        customRenderer.Draw(
                            _subtablePreviewEditorContext,
                            childTable,
                            customView,
                            previewRect);
                    }

                    drewPreview = true;
                }

                break;
            }
        }
        Im.PopClipRect();

        if (!drewPreview)
        {
            DrawSubtableItemCountLabel(cellRect, style, itemCount);
            return;
        }

        DrawSubtableItemCountBadge(cellRect, style, itemCount);
    }

    private static bool IsSubtableDisplayPreviewEnabled(DocWorkspace? workspace, DocColumn subtableColumn)
    {
        if (workspace != null &&
            ResolveEffectiveSubtableDisplayPreviewQuality(workspace, subtableColumn) == DocSubtablePreviewQuality.Off)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(NormalizeSubtableDisplayRendererId(subtableColumn.SubtableDisplayRendererId));
    }

    private static DocSubtablePreviewQuality ResolveEffectiveSubtableDisplayPreviewQuality(
        DocWorkspace workspace,
        DocColumn subtableColumn)
    {
        return subtableColumn.SubtableDisplayPreviewQuality ??
               workspace.UserPreferences.SubtablePreviewQuality;
    }

    private static bool IsRenderingSubtableEmbeddedGrid()
    {
        return !string.IsNullOrWhiteSpace(_activeEmbeddedStateKey) &&
               _activeEmbeddedStateKey.StartsWith("subtable:", StringComparison.Ordinal);
    }

    private static void EnsureSubtableEmbeddedGridFullHeightCache(DocWorkspace workspace)
    {
        if (_subtableEmbeddedGridFullHeightCacheProjectRevision == workspace.ProjectRevision &&
            _subtableEmbeddedGridFullHeightCacheLiveValueRevision == workspace.LiveValueRevision)
        {
            return;
        }

        _subtableEmbeddedGridFullHeightCacheProjectRevision = workspace.ProjectRevision;
        _subtableEmbeddedGridFullHeightCacheLiveValueRevision = workspace.LiveValueRevision;
        _subtableEmbeddedGridFullHeightByKey.Clear();
    }

    private static float ResolveSubtableDisplayPreviewHeight(
        DocWorkspace workspace,
        DocTable parentTable,
        DocRow parentRow,
        int sourceRowIndex,
        float displayColumnWidth,
        DocColumn subtableColumn,
        bool allowFullHeightSubtableCells)
    {
        if (!allowFullHeightSubtableCells ||
            IsRenderingSubtableEmbeddedGrid())
        {
            return ResolveSubtableDisplayPreviewHeight(workspace, subtableColumn);
        }

        string normalizedRendererId = NormalizeSubtableDisplayRendererId(subtableColumn.SubtableDisplayRendererId);
        if (!string.Equals(normalizedRendererId, SubtableDisplayRendererGrid, StringComparison.Ordinal) ||
            ResolveEffectiveSubtableDisplayPreviewQuality(workspace, subtableColumn) != DocSubtablePreviewQuality.Full)
        {
            return ResolveSubtableDisplayPreviewHeight(workspace, subtableColumn);
        }

        if (string.IsNullOrWhiteSpace(subtableColumn.SubtableId))
        {
            return ResolveSubtableDisplayPreviewHeight(workspace, subtableColumn);
        }

        DocTable? childTable = FindTableById(workspace, subtableColumn.SubtableId);
        if (childTable == null)
        {
            return ResolveSubtableDisplayPreviewHeight(workspace, subtableColumn);
        }

        float availableWidth = Math.Max(0f, displayColumnWidth - (CellPaddingX * 2f));
        float previewWidth = ResolveSubtableDisplayPreviewWidth(subtableColumn, availableWidth);
        if (previewWidth <= 1f)
        {
            return ResolveSubtableDisplayPreviewHeight(workspace, subtableColumn);
        }

        string parentRowId = ResolveSubtableParentRowId(workspace, parentTable, subtableColumn, sourceRowIndex);
        if (string.IsNullOrEmpty(parentRowId))
        {
            parentRowId = parentRow.Id;
        }

        int childVariantId = workspace.GetSelectedVariantIdForTable(childTable);
        DocTable variantChildTable = workspace.ResolveTableForVariant(childTable, childVariantId);
        DocView view = ResolveSubtableEmbeddedGridView(childTable);

        EnsureSubtableEmbeddedGridFullHeightCache(workspace);
        int previewWidthRounded = (int)MathF.Round(previewWidth * 100f);
        var cacheKey = new SubtableEmbeddedGridFullHeightCacheKey(
            ChildTableId: childTable.Id,
            ChildVariantId: childVariantId,
            ViewId: view.Id,
            ParentRowId: parentRowId,
            PreviewWidthRounded: previewWidthRounded);
        if (_subtableEmbeddedGridFullHeightByKey.TryGetValue(cacheKey, out float cachedHeight))
        {
            return cachedHeight;
        }

        float measuredHeight = MeasureSubtableEmbeddedGridFullHeight(
            workspace,
            variantChildTable,
            view,
            childTable.ParentRowColumnId,
            parentRowId,
            previewWidth);
        _subtableEmbeddedGridFullHeightByKey[cacheKey] = measuredHeight;
        return measuredHeight;
    }

    private static bool TryConsumeSubtablePreviewBudget(DocSubtablePreviewQuality previewQuality)
    {
        if (previewQuality == DocSubtablePreviewQuality.Off)
        {
            return false;
        }

        int effectiveBudget = ResolveSubtablePreviewFrameBudget(previewQuality);
        if (effectiveBudget <= 0)
        {
            return false;
        }

        if (_subtablePreviewFrameUsage >= effectiveBudget)
        {
            return false;
        }

        _subtablePreviewFrameUsage++;
        return true;
    }

    private static int ResolveSubtablePreviewFrameBudget(DocSubtablePreviewQuality previewQuality)
    {
        int configuredBudget = _subtablePreviewFrameBudget;
        if (previewQuality == DocSubtablePreviewQuality.Lite)
        {
            return Math.Min(configuredBudget, SubtablePreviewFrameBudgetLiteMax);
        }

        return configuredBudget;
    }

    private static string NormalizeSubtableDisplayRendererId(string? rendererId)
    {
        if (string.IsNullOrWhiteSpace(rendererId))
        {
            return "";
        }

        string trimmedId = rendererId.Trim();
        if (string.Equals(trimmedId, "grid", StringComparison.OrdinalIgnoreCase))
        {
            return SubtableDisplayRendererGrid;
        }

        if (string.Equals(trimmedId, "board", StringComparison.OrdinalIgnoreCase))
        {
            return SubtableDisplayRendererBoard;
        }

        if (string.Equals(trimmedId, "calendar", StringComparison.OrdinalIgnoreCase))
        {
            return SubtableDisplayRendererCalendar;
        }

        if (string.Equals(trimmedId, "chart", StringComparison.OrdinalIgnoreCase))
        {
            return SubtableDisplayRendererChart;
        }

        if (string.Equals(trimmedId, SubtableDisplayRendererGrid, StringComparison.OrdinalIgnoreCase))
        {
            return SubtableDisplayRendererGrid;
        }

        if (string.Equals(trimmedId, SubtableDisplayRendererBoard, StringComparison.OrdinalIgnoreCase))
        {
            return SubtableDisplayRendererBoard;
        }

        if (string.Equals(trimmedId, SubtableDisplayRendererCalendar, StringComparison.OrdinalIgnoreCase))
        {
            return SubtableDisplayRendererCalendar;
        }

        if (string.Equals(trimmedId, SubtableDisplayRendererChart, StringComparison.OrdinalIgnoreCase))
        {
            return SubtableDisplayRendererChart;
        }

        if (trimmedId.StartsWith(SubtableDisplayCustomRendererPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string customId = trimmedId[SubtableDisplayCustomRendererPrefix.Length..].Trim();
            return string.IsNullOrWhiteSpace(customId)
                ? ""
                : SubtableDisplayCustomRendererPrefix + customId;
        }

        return string.IsNullOrWhiteSpace(trimmedId)
            ? ""
            : SubtableDisplayCustomRendererPrefix + trimmedId;
    }

    private static bool TryResolveSubtableDisplayRendererKind(
        string normalizedRendererId,
        out SubtableDisplayRendererKind rendererKind,
        out string? customRendererId)
    {
        customRendererId = null;
        if (string.IsNullOrWhiteSpace(normalizedRendererId))
        {
            rendererKind = SubtableDisplayRendererKind.Count;
            return false;
        }

        if (string.Equals(normalizedRendererId, SubtableDisplayRendererGrid, StringComparison.Ordinal))
        {
            rendererKind = SubtableDisplayRendererKind.Grid;
            return true;
        }

        if (string.Equals(normalizedRendererId, SubtableDisplayRendererBoard, StringComparison.Ordinal))
        {
            rendererKind = SubtableDisplayRendererKind.Board;
            return true;
        }

        if (string.Equals(normalizedRendererId, SubtableDisplayRendererCalendar, StringComparison.Ordinal))
        {
            rendererKind = SubtableDisplayRendererKind.Calendar;
            return true;
        }

        if (string.Equals(normalizedRendererId, SubtableDisplayRendererChart, StringComparison.Ordinal))
        {
            rendererKind = SubtableDisplayRendererKind.Chart;
            return true;
        }

        if (normalizedRendererId.StartsWith(SubtableDisplayCustomRendererPrefix, StringComparison.Ordinal))
        {
            customRendererId = normalizedRendererId[SubtableDisplayCustomRendererPrefix.Length..];
            rendererKind = SubtableDisplayRendererKind.Custom;
            return true;
        }

        rendererKind = SubtableDisplayRendererKind.Count;
        return false;
    }

    private static DocView ResolveSubtablePreviewView(
        DocTable childTable,
        DocViewType viewType,
        string? customRendererId)
    {
        EnsureSubtableLookupCachesForFrame(Im.Context.FrameCount);
        var cacheKey = new SubtablePreviewViewCacheKey(childTable.Id, viewType, customRendererId);
        if (_subtablePreviewViewLookupCache.TryGetValue(cacheKey, out DocView? cachedView))
        {
            return cachedView;
        }

        for (int viewIndex = 0; viewIndex < childTable.Views.Count; viewIndex++)
        {
            var candidateView = childTable.Views[viewIndex];
            if (candidateView.Type != viewType)
            {
                continue;
            }

            if (viewType != DocViewType.Custom)
            {
                _subtablePreviewViewLookupCache[cacheKey] = candidateView;
                return candidateView;
            }

            if (string.Equals(candidateView.CustomRendererId, customRendererId, StringComparison.Ordinal))
            {
                _subtablePreviewViewLookupCache[cacheKey] = candidateView;
                return candidateView;
            }
        }

        DocView fallbackView = viewType switch
        {
            DocViewType.Grid => _subtablePreviewGridFallbackView,
            DocViewType.Board => _subtablePreviewBoardFallbackView,
            DocViewType.Calendar => _subtablePreviewCalendarFallbackView,
            DocViewType.Chart => _subtablePreviewChartFallbackView,
            DocViewType.Custom => ResolveSubtableCustomFallbackView(customRendererId),
            _ => _subtablePreviewBoardFallbackView,
        };
        if (viewType != DocViewType.Custom)
        {
            _subtablePreviewViewLookupCache[cacheKey] = fallbackView;
        }

        return fallbackView;
    }

    private static DocView ResolveSubtableCustomFallbackView(string? customRendererId)
    {
        _subtablePreviewCustomFallbackView.CustomRendererId = customRendererId;
        return _subtablePreviewCustomFallbackView;
    }

    private static DocView ResolveSubtableEmbeddedGridView(DocTable childTable)
    {
        return ResolveSubtablePreviewView(childTable, DocViewType.Grid, customRendererId: null);
    }

    private static float MeasureSubtableEmbeddedGridFullHeight(
        DocWorkspace workspace,
        DocTable childTable,
        DocView view,
        string? parentRowColumnId,
        string parentRowId,
        float contentWidth)
    {
        float safeWidth = Math.Max(0f, contentWidth);
        float availableColumnViewportWidth = Math.Max(0f, safeWidth - RowNumberWidth);

        Span<int> visibleColumnMap = stackalloc int[32];
        Span<float> visibleColumnWidths = stackalloc float[32];
        int visibleCount = 0;

        IReadOnlyList<string>? viewColIds = view.VisibleColumnIds;
        if (viewColIds != null && viewColIds.Count > 0)
        {
            for (int viewIndex = 0; viewIndex < viewColIds.Count && visibleCount < 32; viewIndex++)
            {
                string viewColumnId = viewColIds[viewIndex];
                for (int columnIndex = 0; columnIndex < childTable.Columns.Count; columnIndex++)
                {
                    if (!string.Equals(childTable.Columns[columnIndex].Id, viewColumnId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (childTable.Columns[columnIndex].IsHidden)
                    {
                        break;
                    }

                    visibleColumnMap[visibleCount] = columnIndex;
                    visibleCount++;
                    break;
                }
            }
        }
        else
        {
            for (int columnIndex = 0; columnIndex < childTable.Columns.Count && visibleCount < 32; columnIndex++)
            {
                if (childTable.Columns[columnIndex].IsHidden)
                {
                    continue;
                }

                visibleColumnMap[visibleCount] = columnIndex;
                visibleCount++;
            }
        }

        if (visibleCount <= 0)
        {
            return HeaderHeight;
        }

        float totalFixedWidth = 0f;
        int autoColumnCount = 0;
        for (int columnIndex = 0; columnIndex < visibleCount; columnIndex++)
        {
            float columnWidth = childTable.Columns[visibleColumnMap[columnIndex]].Width;
            if (columnWidth > 0f)
            {
                totalFixedWidth += columnWidth;
            }
            else
            {
                autoColumnCount++;
            }
        }

        float autoColumnWidth = autoColumnCount > 0
            ? Math.Max(MinColumnWidth, (availableColumnViewportWidth - totalFixedWidth) / autoColumnCount)
            : 0f;

        float computedPinnedWidth = 0f;
        float computedScrollableContentWidth = AddColumnSlotWidth;
        for (int columnIndex = 0; columnIndex < visibleCount; columnIndex++)
        {
            DocColumn column = childTable.Columns[visibleColumnMap[columnIndex]];
            float rawColumnWidth = column.Width > 0f
                ? column.Width
                : autoColumnWidth;
            float columnWidth = Math.Max(MinColumnWidth, rawColumnWidth);
            visibleColumnWidths[columnIndex] = columnWidth;

            bool isPrimaryKey = !string.IsNullOrWhiteSpace(childTable.Keys.PrimaryKeyColumnId) &&
                                string.Equals(childTable.Keys.PrimaryKeyColumnId, column.Id, StringComparison.Ordinal);
            bool isSecondaryKey = FindSecondaryKeyIndex(childTable, column.Id) >= 0;
            if (isPrimaryKey || isSecondaryKey)
            {
                computedPinnedWidth += columnWidth;
            }
            else
            {
                computedScrollableContentWidth += columnWidth;
            }
        }

        computedPinnedWidth = Math.Min(computedPinnedWidth, availableColumnViewportWidth);
        float scrollableViewportWidth = Math.Max(0f, availableColumnViewportWidth - computedPinnedWidth);
        bool needsHorizontalScrollbar = computedScrollableContentWidth > scrollableViewportWidth + 0.5f;

        int[]? viewRowIndices = workspace.ComputeViewRowIndices(childTable, view, tableInstanceBlock: null);
        int rowCount = viewRowIndices?.Length ?? childTable.Rows.Count;

        int[]? parentSourceRowIndices = null;
        bool filterByParent = !string.IsNullOrWhiteSpace(parentRowColumnId) &&
                              !string.IsNullOrWhiteSpace(parentRowId);
        if (filterByParent)
        {
            parentSourceRowIndices = GetCachedParentRowSourceIndices(
                workspace,
                childTable,
                parentRowColumnId!,
                parentRowId);
            if (parentSourceRowIndices.Length == 0)
            {
                float emptyHeight = HeaderHeight + (needsHorizontalScrollbar ? HorizontalScrollbarHeight : 0f);
                return Math.Max(HeaderHeight, emptyHeight);
            }
        }

        float rowContentHeight = 0f;
        float fontSize = Im.Style.FontSize;
        for (int ri = 0; ri < rowCount; ri++)
        {
            int sourceRowIndex = viewRowIndices != null ? viewRowIndices[ri] : ri;
            if (filterByParent &&
                (parentSourceRowIndices == null || Array.BinarySearch(parentSourceRowIndices, sourceRowIndex) < 0))
            {
                continue;
            }

            var row = childTable.Rows[sourceRowIndex];
            float rowHeight = RowHeight;
            for (int columnIndex = 0; columnIndex < visibleCount; columnIndex++)
            {
                DocColumn column = childTable.Columns[visibleColumnMap[columnIndex]];
                DocCellValue cell = row.GetCell(column);
                float minimumRowHeight = GetMinimumRowHeightForCell(
                    workspace,
                    childTable,
                    row,
                    sourceRowIndex,
                    visibleColumnWidths[columnIndex],
                    column,
                    cell,
                    allowFullHeightSubtableCells: false);
                if (minimumRowHeight > rowHeight)
                {
                    rowHeight = minimumRowHeight;
                }

                if (!IsCellTextWrapped(column))
                {
                    continue;
                }

                float maxTextWidth = Math.Max(0f, visibleColumnWidths[columnIndex] - (CellPaddingX * 2f));
                if (maxTextWidth <= 1f)
                {
                    continue;
                }

                int lineCount = GetCellWrappedLineCount(workspace, column, cell, maxTextWidth, fontSize);
                if (lineCount <= 1)
                {
                    continue;
                }

                float wrappedHeight = (CellPaddingY * 2f) + (lineCount * GetWrappedLineHeight(fontSize));
                if (wrappedHeight > rowHeight)
                {
                    rowHeight = wrappedHeight;
                }
            }

            rowContentHeight += rowHeight;
        }

        float measuredHeight = HeaderHeight + rowContentHeight + (needsHorizontalScrollbar ? HorizontalScrollbarHeight : 0f);
        return Math.Max(HeaderHeight, measuredHeight);
    }

    private static float ResolveSubtableDisplayPreviewWidth(DocColumn subtableColumn, float availableWidth)
    {
        float defaultWidth = Math.Max(SubtableDisplayMinPreviewWidth, availableWidth);
        float requestedWidth = subtableColumn.SubtableDisplayCellWidth ?? defaultWidth;
        float clampedWidth = Math.Clamp(requestedWidth, SubtableDisplayMinPreviewWidth, SubtableDisplayMaxPreviewSize);
        return Math.Min(availableWidth, clampedWidth);
    }

    private static float ResolveSubtableDisplayPreviewHeight(DocWorkspace workspace, DocColumn subtableColumn)
    {
        if (subtableColumn.SubtableDisplayCellHeight.HasValue)
        {
            return Math.Clamp(
                subtableColumn.SubtableDisplayCellHeight.Value,
                SubtableDisplayMinPreviewHeight,
                SubtableDisplayMaxPreviewSize);
        }

        float defaultHeight = ResolveSubtableDisplayDefaultHeight(workspace, subtableColumn);
        return Math.Clamp(defaultHeight, SubtableDisplayMinPreviewHeight, SubtableDisplayMaxPreviewSize);
    }

    private static float ResolveSubtableDisplayPreviewHeight(DocColumn subtableColumn)
    {
        float defaultHeight = ResolveSubtableDisplayDefaultHeight(subtableColumn);
        float requestedHeight = subtableColumn.SubtableDisplayCellHeight ?? defaultHeight;
        return Math.Clamp(requestedHeight, SubtableDisplayMinPreviewHeight, SubtableDisplayMaxPreviewSize);
    }

    private static float ResolveSubtableDisplayDefaultHeight(DocWorkspace workspace, DocColumn subtableColumn)
    {
        string normalizedRendererId = NormalizeSubtableDisplayRendererId(subtableColumn.SubtableDisplayRendererId);
        if (!TryResolveSubtableDisplayRendererKind(
                normalizedRendererId,
                out var rendererKind,
                out _))
        {
            return SubtableDisplayDefaultGridHeight;
        }

        if (rendererKind == SubtableDisplayRendererKind.Grid)
        {
            return ResolveEffectiveSubtableDisplayPreviewQuality(workspace, subtableColumn) == DocSubtablePreviewQuality.Full
                ? SubtableDisplayDefaultRichPreviewHeight
                : SubtableDisplayDefaultGridHeight;
        }

        return rendererKind switch
        {
            SubtableDisplayRendererKind.Board => SubtableDisplayDefaultRichPreviewHeight,
            SubtableDisplayRendererKind.Calendar => SubtableDisplayDefaultRichPreviewHeight,
            SubtableDisplayRendererKind.Chart => SubtableDisplayDefaultRichPreviewHeight,
            SubtableDisplayRendererKind.Custom => SubtableDisplayDefaultRichPreviewHeight,
            _ => SubtableDisplayDefaultGridHeight,
        };
    }

    private static float ResolveSubtableDisplayDefaultHeight(DocColumn subtableColumn)
    {
        string normalizedRendererId = NormalizeSubtableDisplayRendererId(subtableColumn.SubtableDisplayRendererId);
        if (!TryResolveSubtableDisplayRendererKind(
                normalizedRendererId,
                out var rendererKind,
                out _))
        {
            return SubtableDisplayDefaultGridHeight;
        }

        return rendererKind switch
        {
            SubtableDisplayRendererKind.Grid => SubtableDisplayDefaultGridHeight,
            SubtableDisplayRendererKind.Board => SubtableDisplayDefaultRichPreviewHeight,
            SubtableDisplayRendererKind.Calendar => SubtableDisplayDefaultRichPreviewHeight,
            SubtableDisplayRendererKind.Chart => SubtableDisplayDefaultRichPreviewHeight,
            SubtableDisplayRendererKind.Custom => SubtableDisplayDefaultRichPreviewHeight,
            _ => SubtableDisplayDefaultGridHeight,
        };
    }

    private static string GetOrCreateSubtableEmbeddedStateKey(string parentRowId, string subtableColumnId)
    {
        var key = new SubtableEmbeddedStateKey(parentRowId, subtableColumnId);
        if (_subtableEmbeddedStateKeyCache.TryGetValue(key, out string? cached))
        {
            return cached;
        }

        string created = string.Concat("subtable:", parentRowId, ":", subtableColumnId);
        _subtableEmbeddedStateKeyCache[key] = created;
        return created;
    }

    private static void DrawSubtableEmbeddedGrid(
        DocWorkspace workspace,
        DocTable childTable,
        string? parentRowColumnId,
        string parentRowId,
        DocColumn parentSubtableColumn,
        ImRect previewRect,
        ImStyle style)
    {
        Im.DrawRoundedRect(previewRect.X, previewRect.Y, previewRect.Width, previewRect.Height, 4f, style.Background);
        Im.DrawRoundedRectStroke(previewRect.X, previewRect.Y, previewRect.Width, previewRect.Height, 4f, style.Border, 1f);

        string stateKey = GetOrCreateSubtableEmbeddedStateKey(parentRowId, parentSubtableColumn.Id);
        if (!_subtableEmbeddedGridStateByKey.TryGetValue(stateKey, out var embeddedState))
        {
            embeddedState = new SubtableEmbeddedGridState();
            _subtableEmbeddedGridStateByKey[stateKey] = embeddedState;
        }

        NestedSpreadsheetScope nested = NestedSpreadsheetScope.TryEnter();
        if (!nested.Entered)
        {
            DrawSubtableGridPreview(workspace, childTable, parentRowColumnId, parentRowId, previewRect, style);
            return;
        }

        using (nested)
        {
            int selectedVariantId = workspace.GetSelectedVariantIdForTable(childTable);
            DocTable variantTable = workspace.ResolveTableForVariant(childTable, selectedVariantId);
            int previousVariantId = _activeRenderVariantId;
            _activeRenderVariantId = selectedVariantId;

            _activeParentRowIdOverride = parentRowId;
            _activeEmbeddedStateKey = stateKey;
            _embeddedView = ResolveSubtableEmbeddedGridView(childTable);
            _embeddedTableInstanceBlock = null;
            _scrollY = 0f;
            _scrollX = embeddedState.ScrollX;

            Vector2 mousePos = Im.MousePos;
            bool mouseOverPreviewRect = previewRect.Contains(mousePos);
            bool editOwnedByThisEmbeddedGrid =
                workspace.EditState.IsEditing &&
                string.Equals(workspace.EditState.TableId, childTable.Id, StringComparison.Ordinal) &&
                string.Equals(workspace.EditState.OwnerStateKey, stateKey, StringComparison.Ordinal);

            bool keepInteractive = editOwnedByThisEmbeddedGrid;
            Im.Context.PushId(stateKey);
            try
            {
                keepInteractive |= ShouldKeepEmbeddedInteractive(stateKey);
            }
            finally
            {
                Im.Context.PopId();
            }

            bool interactive = mouseOverPreviewRect || keepInteractive;
            if (interactive)
            {
                bool hasEmbeddedState = _embeddedViewStates.ContainsKey(stateKey);
                LoadEmbeddedViewState(stateKey);
                if (!hasEmbeddedState)
                {
                    _scrollX = embeddedState.ScrollX;
                }

                _scrollY = 0f;
                DrawInternal(workspace, variantTable, previewRect, interactive: true, showTableTitleRow: false);
                _scrollY = 0f;
                SaveEmbeddedViewState(stateKey);
            }
            else
            {
                DrawReadOnlySpreadsheetWithWheel(workspace, variantTable, previewRect, stateKey);
            }

            embeddedState.ScrollY = 0f;
            embeddedState.ScrollX = _scrollX;
            embeddedState.GridRectX = _gridRect.X;
            embeddedState.GridRectY = _gridRect.Y;
            embeddedState.GridRectWidth = _gridRect.Width;
            embeddedState.GridRectHeight = _gridRect.Height;
            embeddedState.BodyRectX = _bodyRect.X;
            embeddedState.BodyRectY = _bodyRect.Y;
            embeddedState.BodyRectWidth = _bodyRect.Width;
            embeddedState.BodyRectHeight = _bodyRect.Height;
            embeddedState.PinnedColumnsWidth = _pinnedColumnsWidth;
            embeddedState.ScrollableColumnsViewportWidth = _scrollableColumnsViewportWidth;
            embeddedState.ColumnContentWidth = _columnContentWidth;
            embeddedState.RowContentHeight = _rowContentHeight;
            embeddedState.HasVerticalScrollbar = _hasVerticalScrollbar;
            embeddedState.HasHorizontalScrollbar = _hasHorizontalScrollbar;

            _activeRenderVariantId = previousVariantId;
        }
    }

    private static void DrawReadOnlySpreadsheetWithWheel(
        DocWorkspace workspace,
        DocTable table,
        ImRect contentRect,
        string embeddedStateKey)
    {
        if (contentRect.Width <= 0 || contentRect.Height <= 0)
        {
            return;
        }

        Im.Context.PushId(embeddedStateKey);
        try
        {
        var windowContentRect = Im.WindowContentRect;
        _dialogBoundsRect = windowContentRect.Width > 0f && windowContentRect.Height > 0f
            ? windowContentRect
            : contentRect;

        _isInteractiveRender = false;
        BeginSubtablePreviewFrame(workspace);

        _gridRect = contentRect;
        ComputeLayout(workspace, table);
        HandleReadOnlyEmbeddedWheel(workspace, table, embeddedStateKey, Im.Context.Input);

        DrawHeaders(table);
        Im.PushClipRect(_bodyRect);
        DrawBody(workspace, table);
        DrawStickyColumnsShadow(_bodyRect.Y, _bodyRect.Height);
        Im.PopClipRect();
        DrawScrollbars(workspace, table);
        }
        finally
        {
            Im.Context.PopId();
        }
    }

    private static void HandleReadOnlyEmbeddedWheel(
        DocWorkspace workspace,
        DocTable table,
        string embeddedStateKey,
        ImInput input)
    {
        int frame = Im.Context.FrameCount;
        if (_wheelSuppressedFrame == frame &&
            string.Equals(_wheelSuppressedStateKey, embeddedStateKey, StringComparison.Ordinal))
        {
            return;
        }

        Vector2 mousePos = input.MousePos;
        if (!IsMouseOverScrollableTableRegion(mousePos))
        {
            return;
        }

        float columnViewportWidth = Math.Max(0f, _scrollableColumnsViewportWidth);
        float maxHorizontalScroll = Math.Max(0f, _columnContentWidth - columnViewportWidth);
        float maxVerticalScroll = Math.Max(0f, _rowContentHeight - _bodyRect.Height);
        bool canScrollHorizontally = _hasHorizontalScrollbar && maxHorizontalScroll > 0f;
        bool canScrollVertically = _hasVerticalScrollbar && maxVerticalScroll > 0f;
        if (!canScrollHorizontally && !canScrollVertically)
        {
            return;
        }

        float wheelAmount = MathF.Abs(input.ScrollDeltaX) > MathF.Abs(input.ScrollDelta)
            ? input.ScrollDeltaX
            : input.ScrollDelta;
        if (wheelAmount == 0f)
        {
            return;
        }

        bool horizontalScrollChanged = false;
        var horizontalScrollbarRect = GetHorizontalScrollbarRect(columnViewportWidth);
        var verticalScrollbarRect = GetVerticalScrollbarRect();
        bool routeToHorizontal = ShouldRouteWheelToHorizontalScrollbar(
            mousePos,
            canScrollHorizontally,
            canScrollVertically,
            horizontalScrollbarRect,
            verticalScrollbarRect);

        if (routeToHorizontal && canScrollHorizontally)
        {
            _scrollX -= wheelAmount * HorizontalScrollWheelSpeed;
            _scrollX = Math.Clamp(_scrollX, 0f, maxHorizontalScroll);
            horizontalScrollChanged = true;
        }
        else if (canScrollVertically)
        {
            _scrollY -= wheelAmount * RowHeight * 3f;
            _scrollY = Math.Clamp(_scrollY, 0f, maxVerticalScroll);
        }
        else if (canScrollHorizontally)
        {
            _scrollX -= wheelAmount * HorizontalScrollWheelSpeed;
            _scrollX = Math.Clamp(_scrollX, 0f, maxHorizontalScroll);
            horizontalScrollChanged = true;
        }

        if (horizontalScrollChanged)
        {
            ComputeLayout(workspace, table);
        }
    }

    private static void DrawSubtableGridPreview(
        DocWorkspace workspace,
        DocTable childTable,
        string? parentRowColumnId,
        string parentRowId,
        ImRect previewRect,
        ImStyle style)
    {
        Im.DrawRoundedRect(previewRect.X, previewRect.Y, previewRect.Width, previewRect.Height, 4f, style.Background);
        Im.DrawRoundedRectStroke(previewRect.X, previewRect.Y, previewRect.Width, previewRect.Height, 4f, style.Border, 1f);

        float fontSize = Math.Max(9f, style.FontSize - 2f);
        float lineHeight = fontSize + 2f;
        float textX = previewRect.X + 4f;
        float textY = previewRect.Y + 3f;
        float maxY = previewRect.Bottom - 3f;
        if (textY + fontSize > maxY)
        {
            return;
        }

        DocColumn? displayColumn = ResolveSubtableGridPreviewDisplayColumn(childTable, parentRowColumnId);
        string headerLabel = displayColumn != null ? displayColumn.Name : childTable.Name;
        Im.Text(headerLabel.AsSpan(), textX, textY, fontSize, style.TextSecondary);
        textY += lineHeight;

        if (displayColumn == null)
        {
            Im.Text("(no columns)".AsSpan(), textX, textY, fontSize, style.TextSecondary);
            return;
        }

        int[]? parentFilteredIndices = null;
        if (!string.IsNullOrWhiteSpace(parentRowColumnId) &&
            !string.IsNullOrWhiteSpace(parentRowId))
        {
            parentFilteredIndices = GetCachedParentRowSourceIndices(
                workspace,
                childTable,
                parentRowColumnId!,
                parentRowId);
        }

        int rowCount = parentFilteredIndices?.Length ?? childTable.Rows.Count;
        int displayedRowCount = 0;
        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            if (textY + fontSize > maxY || displayedRowCount >= 3)
            {
                break;
            }

            int sourceRowIndex = parentFilteredIndices != null
                ? parentFilteredIndices[rowIndex]
                : rowIndex;
            var row = childTable.Rows[sourceRowIndex];
            if (parentFilteredIndices == null &&
                !RowMatchesSubtableParentFilter(row, parentRowColumnId, parentRowId))
            {
                continue;
            }

            string valueText = ResolveSubtableGridPreviewCellText(workspace, row, displayColumn);
            if (valueText.Length <= 0)
            {
                valueText = row.Id;
            }

            Im.Text(valueText.AsSpan(), textX, textY, fontSize, style.TextPrimary);
            textY += lineHeight;
            displayedRowCount++;
        }

        if (displayedRowCount == 0 && textY + fontSize <= maxY)
        {
            Im.Text("(empty)".AsSpan(), textX, textY, fontSize, style.TextSecondary);
        }
    }

    private static DocColumn? ResolveSubtableGridPreviewDisplayColumn(DocTable childTable, string? parentRowColumnId)
    {
        DocColumn? firstVisibleColumn = null;
        for (int columnIndex = 0; columnIndex < childTable.Columns.Count; columnIndex++)
        {
            var column = childTable.Columns[columnIndex];
            if (column.IsHidden ||
                string.Equals(column.Id, parentRowColumnId, StringComparison.Ordinal))
            {
                continue;
            }

            firstVisibleColumn ??= column;
            if (column.Kind == DocColumnKind.Id ||
                column.Kind == DocColumnKind.Text ||
                column.Kind == DocColumnKind.Select ||
                column.Kind == DocColumnKind.Relation ||
                column.Kind == DocColumnKind.TableRef)
            {
                return column;
            }
        }

        return firstVisibleColumn;
    }

    private static string ResolveSubtableGridPreviewCellText(
        DocWorkspace workspace,
        DocRow row,
        DocColumn displayColumn)
    {
        var cell = row.GetCell(displayColumn);
        switch (displayColumn.Kind)
        {
            case DocColumnKind.Number:
            case DocColumnKind.Formula:
            {
                Span<char> numberBuffer = stackalloc char[32];
                if (cell.NumberValue.TryFormat(numberBuffer, out int numberLength, "G"))
                {
                    return numberBuffer[..numberLength].ToString();
                }

                return "";
            }
            case DocColumnKind.Checkbox:
                return cell.BoolValue ? "true" : "false";
            case DocColumnKind.Relation:
                return workspace.ResolveRelationDisplayLabel(displayColumn, cell.StringValue ?? "");
            case DocColumnKind.TableRef:
                return ResolveTableRefLabel(workspace, cell.StringValue ?? "");
            case DocColumnKind.Vec2:
                return FormatVectorCellText(cell, 2);
            case DocColumnKind.Vec3:
                return FormatVectorCellText(cell, 3);
            case DocColumnKind.Vec4:
                return FormatVectorCellText(cell, 4);
            case DocColumnKind.Color:
                return FormatColorCellText(cell);
            default:
                return cell.StringValue ?? "";
        }
    }

    private static bool RowMatchesSubtableParentFilter(
        DocRow row,
        string? parentRowColumnId,
        string parentRowId)
    {
        if (string.IsNullOrWhiteSpace(parentRowColumnId) ||
            string.IsNullOrWhiteSpace(parentRowId))
        {
            return true;
        }

        string rowParentValue = row.GetCell(parentRowColumnId).StringValue ?? "";
        return string.Equals(rowParentValue, parentRowId, StringComparison.Ordinal);
    }

    private static void DrawSubtableItemCountLabel(ImRect cellRect, ImStyle style, int itemCount)
    {
        Span<char> itemsBuffer = stackalloc char[24];
        int length = 0;
        itemCount.TryFormat(itemsBuffer[length..], out int written);
        length += written;
        " items".AsSpan().CopyTo(itemsBuffer[length..]);
        length += 6;

        float textY = cellRect.Y + (cellRect.Height - style.FontSize) * 0.5f;
        Im.Text(itemsBuffer[..length], cellRect.X + CellPaddingX, textY, style.FontSize, style.TextSecondary);
    }

    private static void DrawSubtableItemCountBadge(ImRect cellRect, ImStyle style, int itemCount)
    {
        Span<char> countBuffer = stackalloc char[16];
        if (!itemCount.TryFormat(countBuffer, out int countLength))
        {
            return;
        }

        float badgeFontSize = Math.Max(9f, style.FontSize - 2f);
        float textWidth = Im.MeasureTextWidth(countBuffer[..countLength], badgeFontSize);
        float badgeHeight = badgeFontSize + 4f;
        float badgeWidth = textWidth + 8f;
        float badgeX = cellRect.Right - badgeWidth - 3f;
        float badgeY = cellRect.Bottom - badgeHeight - 3f;
        uint badgeColor = BlendColor(style.Surface, 0.45f, style.Background);
        Im.DrawRoundedRect(badgeX, badgeY, badgeWidth, badgeHeight, 4f, badgeColor);
        Im.DrawRoundedRectStroke(badgeX, badgeY, badgeWidth, badgeHeight, 4f, style.Border, 1f);
        Im.Text(countBuffer[..countLength], badgeX + 4f, badgeY + 2f, badgeFontSize, style.TextSecondary);
    }

    private static void RenderAssetCellContent(
        DocWorkspace workspace,
        DocColumn column,
        DocCellValue cell,
        ImRect cellRect,
        ImStyle style)
    {
        string relativePath = cell.StringValue ?? "";
        if (relativePath.Length == 0)
        {
            DrawAssetCellLabel("(none)".AsSpan(), cellRect, style, style.TextSecondary);
            return;
        }

        if (column.Kind == DocColumnKind.AudioAsset)
        {
            DrawAudioAssetCellContent(relativePath.AsSpan(), cellRect, style);
            return;
        }

        string? assetRoot = ResolveAssetRootForColumnKind(workspace, column.Kind);
        if (string.IsNullOrWhiteSpace(assetRoot) || !Directory.Exists(assetRoot))
        {
            DrawAssetCellTextOnly(relativePath, cellRect, style);
            return;
        }

        var thumbnailResult = DocAssetServices.ThumbnailCache.GetThumbnail(
            assetRoot,
            column.Kind,
            relativePath,
            column.Kind == DocColumnKind.MeshAsset
                ? (cell.ModelPreviewSettings ?? column.ModelPreviewSettings)
                : null);
        DrawAssetCellThumbnail(column, thumbnailResult, relativePath, cellRect, style);
    }

    private static void DrawAssetCellTextOnly(string relativePath, ImRect cellRect, ImStyle style)
    {
        ReadOnlySpan<char> fileName = Path.GetFileName(relativePath).AsSpan();
        if (fileName.Length == 0)
        {
            fileName = relativePath.AsSpan();
        }

        DrawAssetCellLabel(fileName, cellRect, style, style.TextPrimary);
    }

    private static void DrawAssetCellThumbnail(
        DocColumn column,
        AssetThumbnailCache.ThumbnailResult thumbnailResult,
        string relativePath,
        ImRect cellRect,
        ImStyle style)
    {
        float textHeight = style.FontSize;
        float thumbnailMaxWidth = MathF.Max(8f, cellRect.Width - (AssetThumbnailPadding * 2f));
        float thumbnailMaxHeight = MathF.Max(8f, cellRect.Height - textHeight - AssetThumbnailTextGap - (AssetThumbnailPadding * 2f));
        float thumbnailX = cellRect.X + AssetThumbnailPadding;
        float thumbnailY = cellRect.Y + AssetThumbnailPadding;
        var thumbnailRect = new ImRect(thumbnailX, thumbnailY, thumbnailMaxWidth, thumbnailMaxHeight);

        uint placeholderColor = BlendColor(style.Surface, 0.35f, style.Background);
        Im.DrawRoundedRect(
            thumbnailRect.X,
            thumbnailRect.Y,
            thumbnailRect.Width,
            thumbnailRect.Height,
            AssetThumbnailPlaceholderCorner,
            placeholderColor);
        Im.DrawRoundedRectStroke(
            thumbnailRect.X,
            thumbnailRect.Y,
            thumbnailRect.Width,
            thumbnailRect.Height,
            AssetThumbnailPlaceholderCorner,
            style.Border,
            1f);

        Im.PushClipRect(new ImRect(cellRect.X, cellRect.Y, cellRect.Width, cellRect.Height));
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
            ReadOnlySpan<char> placeholderText = thumbnailResult.Status switch
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
            float statusWidth = Im.MeasureTextWidth(placeholderText, style.FontSize);
            float statusX = thumbnailRect.X + (thumbnailRect.Width - statusWidth) * 0.5f;
            float statusY = thumbnailRect.Y + (thumbnailRect.Height - style.FontSize) * 0.5f;
            Im.Text(placeholderText, statusX, statusY, style.FontSize, statusColor);
        }
        Im.PopClipRect();

        string fileName = Path.GetFileName(relativePath);
        ReadOnlySpan<char> label = fileName.Length > 0 ? fileName.AsSpan() : relativePath.AsSpan();
        float labelY = thumbnailRect.Bottom + AssetThumbnailTextGap;
        float labelAvailableHeight = cellRect.Bottom - labelY;
        if (labelAvailableHeight >= style.FontSize)
        {
            Im.PushClipRect(new ImRect(cellRect.X + CellPaddingX, labelY, MathF.Max(2f, cellRect.Width - (CellPaddingX * 2f)), style.FontSize + 2f));
            Im.Text(label, cellRect.X + CellPaddingX, labelY, style.FontSize, style.TextPrimary);
            Im.PopClipRect();
        }

    }

    private static void DrawAudioAssetCellContent(ReadOnlySpan<char> relativePath, ImRect cellRect, ImStyle style)
    {
        float contentX = cellRect.X + AssetThumbnailPadding;
        float contentY = cellRect.Y + AssetThumbnailPadding;
        float contentWidth = MathF.Max(8f, cellRect.Width - (AssetThumbnailPadding * 2f));
        float contentHeight = MathF.Max(8f, cellRect.Height - (AssetThumbnailPadding * 2f));

        uint contentColor = BlendColor(style.Surface, 0.35f, style.Background);
        Im.DrawRoundedRect(contentX, contentY, contentWidth, contentHeight, AssetThumbnailPlaceholderCorner, contentColor);
        Im.DrawRoundedRectStroke(contentX, contentY, contentWidth, contentHeight, AssetThumbnailPlaceholderCorner, style.Border, 1f);

        ImRect playButtonRect = GetAudioAssetPlayButtonRect(cellRect, style);
        DrawAudioAssetPlayButton(playButtonRect, style);

        ReadOnlySpan<char> fileName = Path.GetFileName(relativePath);
        if (fileName.Length == 0)
        {
            fileName = relativePath;
        }

        float labelY = cellRect.Bottom - style.FontSize - AssetThumbnailPadding;
        if (labelY >= playButtonRect.Bottom + 2f)
        {
            Im.PushClipRect(new ImRect(
                cellRect.X + CellPaddingX,
                labelY,
                MathF.Max(2f, cellRect.Width - (CellPaddingX * 2f)),
                style.FontSize + 2f));
            Im.Text(fileName, cellRect.X + CellPaddingX, labelY, style.FontSize, style.TextPrimary);
            Im.PopClipRect();
        }
    }

    private static void DrawAudioAssetPlayButton(ImRect playButtonRect, ImStyle style)
    {
        bool hovered = playButtonRect.Contains(Im.MousePos);
        uint fillColor = hovered
            ? BlendColor(style.Primary, 0.35f, style.Surface)
            : BlendColor(style.Surface, 0.45f, style.Background);
        Im.DrawRoundedRect(
            playButtonRect.X,
            playButtonRect.Y,
            playButtonRect.Width,
            playButtonRect.Height,
            4f,
            fillColor);
        Im.DrawRoundedRectStroke(
            playButtonRect.X,
            playButtonRect.Y,
            playButtonRect.Width,
            playButtonRect.Height,
            4f,
            style.Border,
            1f);

        float iconFontSize = Math.Max(9f, style.FontSize - 1f);
        ReadOnlySpan<char> iconText = _audioPlayIconText.AsSpan();
        float iconWidth = Im.MeasureTextWidth(iconText, iconFontSize);
        float iconX = playButtonRect.X + (playButtonRect.Width - iconWidth) * 0.5f;
        float iconY = playButtonRect.Y + (playButtonRect.Height - iconFontSize) * 0.5f;
        Im.Text(iconText, iconX, iconY, iconFontSize, style.TextPrimary);
    }

    private static ImRect GetAudioAssetPlayButtonRect(ImRect cellRect, ImStyle style)
    {
        float buttonSize = MathF.Min(AudioPlayButtonSize, MathF.Max(14f, style.FontSize + 6f));
        float buttonX = cellRect.X + ((cellRect.Width - buttonSize) * 0.5f);
        float buttonY = cellRect.Y + ((cellRect.Height - buttonSize) * 0.5f);
        return new ImRect(buttonX, buttonY, buttonSize, buttonSize);
    }

    private static void DrawAssetCellLabel(ReadOnlySpan<char> label, ImRect cellRect, ImStyle style, uint color)
    {
        float textY = cellRect.Y + (cellRect.Height - style.FontSize) * 0.5f;
        Im.PushClipRect(new ImRect(cellRect.X + CellPaddingX, textY, MathF.Max(2f, cellRect.Width - (CellPaddingX * 2f)), style.FontSize + 2f));
        Im.Text(label, cellRect.X + CellPaddingX, textY, style.FontSize, color);
        Im.PopClipRect();
    }

    private static void DrawSplineCellPreview(DocCellValue cell, ImRect cellRect)
    {
        string splineJson = cell.StringValue ?? "";
        if (!_splinePreviewCurveByJson.TryGetValue(splineJson, out var curve))
        {
            curve = SplineConverter.JsonToCurve(splineJson);
            if (_splinePreviewCurveByJson.Count >= MaxSplinePreviewCacheEntries)
            {
                _splinePreviewCurveByJson.Clear();
            }

            _splinePreviewCurveByJson[splineJson] = curve;
        }

        float previewX = cellRect.X + 4f;
        float previewWidth = Math.Max(8f, cellRect.Width - 8f);
        float previewHeight = Math.Max(8f, cellRect.Height - 8f);
        float previewY = cellRect.Y + (cellRect.Height - previewHeight) * 0.5f;
        ImCurveEditor.DrawPreview(ref curve, previewX, previewY, previewWidth, previewHeight, 0f, 1f);
    }

    // =====================================================================
    //  Edit overlay
    // =====================================================================

    private static void DrawEditOverlay(DocWorkspace workspace, DocTable table)
    {
        ref var edit = ref workspace.EditState;
        if (!edit.IsEditing)
        {
            _cellTypeaheadPopupVisible = false;
            return;
        }

        if (!IsEditOwnedByCurrentInstance(workspace, table))
        {
            _cellTypeaheadPopupVisible = false;
            return;
        }

        if (edit.RowIndex >= _rowCount || edit.ColIndex >= _colCount)
        {
            workspace.CancelTableCellEditIfActive();
            _cellTypeaheadPopupVisible = false;
            return;
        }

        var col = GetVisibleColumn(table, edit.ColIndex);
        if (IsColumnDataReadOnly(col))
        {
            workspace.CancelTableCellEditIfActive();
            _cellTypeaheadPopupVisible = false;
            return;
        }

        var cellRect = GetCellRect(edit.RowIndex, edit.ColIndex);
        var input = Im.Context.Input;
        _cellTypeaheadPopupVisible = false;
        int sourceRowIndex = GetSourceRowIndex(edit.RowIndex);
        if (sourceRowIndex < 0 || sourceRowIndex >= table.Rows.Count)
        {
            workspace.CancelTableCellEditIfActive();
            return;
        }

        var row = table.Rows[sourceRowIndex];
        var cell = row.GetCell(col);
        if (TryGetColumnUiPlugin(col, out var uiPlugin) &&
            uiPlugin.DrawCellEditor(
                workspace,
                table,
                row,
                sourceRowIndex,
                edit.RowIndex,
                edit.ColIndex,
                col,
                cell,
                cellRect,
                input))
        {
            return;
        }

        switch (col.Kind)
        {
            case DocColumnKind.Id:
            case DocColumnKind.Text:
            case DocColumnKind.TextureAsset:
            case DocColumnKind.MeshAsset:
            case DocColumnKind.AudioAsset:
            case DocColumnKind.UiAsset:
            {
                // Seamless inline edit: preserve cell look, align editor to the same text insets as display mode.
                Im.DrawRect(cellRect.X, cellRect.Y, cellRect.Width, cellRect.Height, Im.Style.Background);
                float inputHeight = MathF.Min(cellRect.Height - 2f, Im.Style.MinButtonHeight);
                float inputY = cellRect.Y + (cellRect.Height - inputHeight) * 0.5f;
                float inputX = cellRect.X + CellPaddingX;
                float inputWidth = Math.Max(6f, cellRect.Width - CellPaddingX * 2f);
                Im.TextInput(
                    "doc_cell_edit",
                    edit.Buffer,
                    ref edit.BufferLength,
                    256,
                    inputX,
                    inputY,
                    inputWidth,
                    Im.ImTextInputFlags.NoBackground | Im.ImTextInputFlags.NoBorder);

                if (input.KeyEnter)
                {
                    CommitEditIfActive(workspace, table);
                    return;
                }
                if (input.KeyEscape)
                {
                    workspace.CancelTableCellEditIfActive();
                }
                break;
            }

            case DocColumnKind.Number:
            {
                DrawNumberCellEditor(workspace, table, col, cellRect, input);
                break;
            }

            case DocColumnKind.Select:
            {
                DrawSelectTypeaheadEditor(workspace, table, col, cellRect, input);
                break;
            }

            case DocColumnKind.Relation:
            {
                DrawRelationTypeaheadEditor(workspace, table, col, cellRect, input);
                break;
            }

            case DocColumnKind.TableRef:
            {
                DrawTableRefTypeaheadEditor(workspace, table, col, cellRect, input);
                break;
            }

            case DocColumnKind.Formula:
            case DocColumnKind.Vec2:
            case DocColumnKind.Vec3:
            case DocColumnKind.Vec4:
            case DocColumnKind.Color:
                workspace.CancelTableCellEditIfActive();
                break;
        }
    }

    private static void DrawNumberCellEditor(
        DocWorkspace workspace,
        DocTable table,
        DocColumn column,
        ImRect cellRect,
        ImInput input)
    {
        ref var edit = ref workspace.EditState;
        Im.DrawRect(cellRect.X, cellRect.Y, cellRect.Width, cellRect.Height, Im.Style.Background);
        float inputHeight = MathF.Min(cellRect.Height - 2f, Im.Style.MinButtonHeight);
        float inputY = cellRect.Y + (cellRect.Height - inputHeight) * 0.5f;
        float inputX = cellRect.X + CellPaddingX;
        float inputWidth = Math.Max(6f, cellRect.Width - CellPaddingX * 2f);
        var inputRect = new ImRect(inputX, inputY, inputWidth, inputHeight);
        bool hoveredInput = inputRect.Contains(Im.MousePos);

        if (hoveredInput || edit.IsNumberDragging || edit.NumberDragPressed)
        {
            Im.SetCursor(StandardCursor.HResize);
        }

        if (hoveredInput && input.MousePressed)
        {
            edit.NumberDragPressed = true;
            edit.IsNumberDragging = false;
            edit.NumberDragStartMouseX = Im.MousePos.X;
            edit.NumberDragStartValue = ResolveCurrentNumberEditValue(table, column, ref edit);
            edit.NumberDragAccumulatedDeltaX = 0;
            _isDragging = false;
        }

        if (edit.NumberDragPressed)
        {
            if (input.MouseDown)
            {
                float dragDistance = MathF.Abs(Im.MousePos.X - edit.NumberDragStartMouseX);
                if (dragDistance >= NumberScrubStartThreshold)
                {
                    edit.NumberDragPressed = false;
                    edit.IsNumberDragging = true;
                    edit.NumberDragAccumulatedDeltaX = 0;
                    edit.NumberDragCursorLocked = ImMouseDragLock.Begin(edit.NumberDragLockOwnerId);
                    _isDragging = false;
                }
            }
            else
            {
                edit.NumberDragPressed = false;
            }
        }

        if (edit.IsNumberDragging)
        {
            if (input.KeyEscape)
            {
                edit.IsNumberDragging = false;
                workspace.CancelTableCellEditIfActive();
                return;
            }

            if (input.MouseDown)
            {
                float dragDeltaX = edit.NumberDragCursorLocked
                    ? ImMouseDragLock.ConsumeDelta(edit.NumberDragLockOwnerId).X
                    : Im.Context.Input.MouseDelta.X;
                edit.NumberDragAccumulatedDeltaX += dragDeltaX;

                double previewValue = edit.NumberDragStartValue +
                                      (edit.NumberDragAccumulatedDeltaX * NumberScrubSensitivity);
                if (workspace.PreviewNumberCellValueFromEdit(
                        previewValue,
                        allowDeferredNonInteractiveRefresh: false))
                {
                    WriteNumberToEditBuffer(ref edit, previewValue);
                }

                DrawNumberEditText(edit, inputX, inputY, inputHeight);
                return;
            }

            edit.IsNumberDragging = false;
            CommitEditIfActive(workspace, table);
            return;
        }

        Im.TextInput(
            "doc_cell_edit",
            edit.Buffer,
            ref edit.BufferLength,
            256,
            inputX,
            inputY,
            inputWidth,
            Im.ImTextInputFlags.NoBackground | Im.ImTextInputFlags.NoBorder);

        if (input.KeyEnter)
        {
            CommitEditIfActive(workspace, table);
            return;
        }

        if (input.KeyEscape)
        {
            workspace.CancelTableCellEditIfActive();
        }
    }

    private static void DrawNumberEditText(TableCellEditState edit, float x, float y, float height)
    {
        ReadOnlySpan<char> text = edit.Buffer.AsSpan(0, edit.BufferLength);
        float textY = y + (height - Im.Style.FontSize) * 0.5f;
        Im.Text(text, x, textY, Im.Style.FontSize, Im.Style.TextPrimary);
    }

    private static double ResolveCurrentNumberEditValue(DocTable table, DocColumn column, ref TableCellEditState edit)
    {
        if (edit.BufferLength > 0 &&
            double.TryParse(edit.Buffer.AsSpan(0, edit.BufferLength), out double parsedBufferValue))
        {
            return parsedBufferValue;
        }

        int sourceRowIndex = GetSourceRowIndex(edit.RowIndex);
        if (sourceRowIndex >= 0 && sourceRowIndex < table.Rows.Count)
        {
            return table.Rows[sourceRowIndex].GetCell(column).NumberValue;
        }

        return edit.NumberPreviewOriginalValue;
    }

    private static void WriteNumberToEditBuffer(ref TableCellEditState edit, double value)
    {
        Span<char> valueBuffer = stackalloc char[64];
        if (!value.TryFormat(valueBuffer, out int written, "G"))
        {
            return;
        }

        int copyLength = Math.Min(written, edit.Buffer.Length);
        valueBuffer[..copyLength].CopyTo(edit.Buffer);
        edit.BufferLength = copyLength;
    }

    private static void DrawSelectTypeaheadEditor(
        DocWorkspace workspace,
        DocTable table,
        DocColumn column,
        ImRect cellRect,
        ImInput input)
    {
        ref var edit = ref workspace.EditState;
        if (column.Options == null)
        {
            column.Options = new List<string>();
        }

        Im.DrawRect(cellRect.X, cellRect.Y, cellRect.Width, cellRect.Height, Im.Style.Background);
        float inputHeight = MathF.Min(cellRect.Height - 2f, Im.Style.MinButtonHeight);
        float inputY = cellRect.Y + (cellRect.Height - inputHeight) * 0.5f;
        float inputX = cellRect.X + CellPaddingX;
        float inputWidth = Math.Max(6f, cellRect.Width - CellPaddingX * 2f);
        Im.TextInput(
            "doc_cell_select_edit",
            edit.Buffer,
            ref edit.BufferLength,
            256,
            inputX,
            inputY,
            inputWidth,
            Im.ImTextInputFlags.NoBackground | Im.ImTextInputFlags.NoBorder);

        if (input.KeyEscape)
        {
            workspace.CancelTableCellEditIfActive();
            return;
        }

        var options = column.Options;
        int filteredCount = 0;
        bool hasExactLabel = false;
        ReadOnlySpan<char> typedLabel = edit.Buffer.AsSpan(0, edit.BufferLength).Trim();
        for (int optionIndex = 0; optionIndex < options.Count; optionIndex++)
        {
            string optionValue = options[optionIndex] ?? "";
            if (typedLabel.Length > 0 &&
                optionValue.AsSpan().Equals(typedLabel, StringComparison.OrdinalIgnoreCase))
            {
                hasExactLabel = true;
            }

            bool includeOption = typedLabel.Length == 0 ||
                optionValue.AsSpan().Contains(typedLabel, StringComparison.OrdinalIgnoreCase);
            if (!includeOption)
            {
                continue;
            }

            if (filteredCount >= _cellTypeaheadFilterIndices.Length)
            {
                break;
            }

            _cellTypeaheadFilterIndices[filteredCount] = optionIndex;
            filteredCount++;
        }

        bool showCreateOption = typedLabel.Length > 0 && !hasExactLabel;
        int optionCount = filteredCount + (showCreateOption ? 1 : 0);
        int drawnRows = Math.Min(Math.Max(optionCount, 1), (int)CellTypeaheadMaxVisibleRows);
        float itemHeight = MathF.Max(Im.Style.MinButtonHeight, 22f);
        float popupHeight = 2f + (drawnRows * itemHeight);
        float popupY = cellRect.Bottom;
        if (popupY + popupHeight > _bodyRect.Bottom)
        {
            popupY = Math.Max(_headerRect.Bottom, cellRect.Y - popupHeight);
        }

        var popupRect = new ImRect(cellRect.X, popupY, cellRect.Width, popupHeight);
        _cellTypeaheadPopupRect = popupRect;
        _cellTypeaheadPopupVisible = true;
        using var popupOverlayScope = ImPopover.PushOverlayScopeLocal(popupRect);

        if (optionCount > 0)
        {
            int maxSelectableIndex = Math.Max(0, drawnRows - 1);
            if (edit.DropdownIndex < 0 || edit.DropdownIndex > maxSelectableIndex)
            {
                edit.DropdownIndex = 0;
            }

            if (input.KeyDown)
            {
                edit.DropdownIndex = Math.Min(maxSelectableIndex, edit.DropdownIndex + 1);
            }
            if (input.KeyUp)
            {
                edit.DropdownIndex = Math.Max(0, edit.DropdownIndex - 1);
            }
        }
        else
        {
            edit.DropdownIndex = -1;
        }

        var style = Im.Style;
        Im.DrawRoundedRect(popupRect.X, popupRect.Y, popupRect.Width, popupRect.Height, 6f, style.Surface);
        Im.DrawRoundedRectStroke(popupRect.X, popupRect.Y, popupRect.Width, popupRect.Height, 6f, style.Border, 1f);

        int clickedIndex = -1;
        for (int drawIndex = 0; drawIndex < drawnRows; drawIndex++)
        {
            float rowY = popupRect.Y + 1f + drawIndex * itemHeight;
            var optionRect = new ImRect(popupRect.X + 1f, rowY, popupRect.Width - 2f, itemHeight);
            bool hovered = optionRect.Contains(Im.MousePos);
            bool selected = drawIndex == edit.DropdownIndex;
            if (selected || hovered)
            {
                uint rowColor = selected ? style.Active : style.Hover;
                Im.DrawRect(optionRect.X, optionRect.Y, optionRect.Width, optionRect.Height, rowColor);
            }

            if (hovered)
            {
                edit.DropdownIndex = drawIndex;
                if (input.MousePressed)
                {
                    clickedIndex = drawIndex;
                    Im.Context.ConsumeMouseLeftPress();
                }
            }

            float textY = optionRect.Y + (optionRect.Height - style.FontSize) * 0.5f;
            if (optionCount <= 0)
            {
                Im.Text("(no matches)".AsSpan(), optionRect.X + 8f, textY, style.FontSize, style.TextSecondary);
                continue;
            }

            if (drawIndex < filteredCount)
            {
                int selectOptionIndex = _cellTypeaheadFilterIndices[drawIndex];
                string optionLabel = options[selectOptionIndex];
                Im.Text(optionLabel.AsSpan(), optionRect.X + 8f, textY, style.FontSize, style.TextPrimary);
                continue;
            }

            if (showCreateOption)
            {
                string createLabel = "+ '" + typedLabel.ToString() + "'";
                Im.Text(createLabel.AsSpan(), optionRect.X + 8f, textY, style.FontSize, style.TextPrimary);
            }
        }

        if (input.KeyEnter && optionCount > 0 && edit.DropdownIndex >= 0)
        {
            clickedIndex = edit.DropdownIndex;
        }

        if (clickedIndex < 0 || optionCount <= 0)
        {
            return;
        }

        if (clickedIndex < filteredCount)
        {
            int selectedOptionIndex = _cellTypeaheadFilterIndices[clickedIndex];
            CommitSelectCellSelection(workspace, table, column, edit.RowIndex, edit.ColIndex, options[selectedOptionIndex]);
            return;
        }

        if (!showCreateOption)
        {
            return;
        }

        TryCreateSelectOptionAndCommit(workspace, table, column, edit.RowIndex, edit.ColIndex, typedLabel);
    }

    private static void CommitSelectCellSelection(
        DocWorkspace workspace,
        DocTable sourceTable,
        DocColumn selectColumn,
        int editDisplayRowIndex,
        int editDisplayColumnIndex,
        string selectedValue)
    {
        _multiSelectTargetRowIndices.Clear();
        CollectSelectedSourceRowIndicesForEditCell(
            sourceTable,
            editDisplayRowIndex,
            editDisplayColumnIndex,
            _multiSelectTargetRowIndices);

        if (_multiSelectTargetRowIndices.Count <= 0)
        {
            workspace.CancelTableCellEditIfActive();
            return;
        }

        var commands = new List<DocCommand>(_multiSelectTargetRowIndices.Count);
        for (int targetRowIndex = 0; targetRowIndex < _multiSelectTargetRowIndices.Count; targetRowIndex++)
        {
            int sourceRowIndex = _multiSelectTargetRowIndices[targetRowIndex];
            var sourceRow = sourceTable.Rows[sourceRowIndex];
            var oldCell = sourceRow.GetCell(selectColumn);
            if (string.Equals(oldCell.StringValue ?? "", selectedValue, StringComparison.Ordinal))
            {
                continue;
            }

            commands.Add(new DocCommand
            {
                Kind = DocCommandKind.SetCell,
                TableId = sourceTable.Id,
                RowId = sourceRow.Id,
                ColumnId = selectColumn.Id,
                OldCellValue = oldCell,
                NewCellValue = DocCellValue.Text(selectedValue),
            });
        }

        if (commands.Count == 1)
        {
            workspace.ExecuteCommand(commands[0]);
        }
        else if (commands.Count > 1)
        {
            workspace.ExecuteCommands(commands);
        }

        workspace.CancelTableCellEditIfActive();
    }

    private static bool TryCreateSelectOptionAndCommit(
        DocWorkspace workspace,
        DocTable sourceTable,
        DocColumn selectColumn,
        int editDisplayRowIndex,
        int editDisplayColumnIndex,
        ReadOnlySpan<char> typedValue)
    {
        _multiSelectTargetRowIndices.Clear();
        CollectSelectedSourceRowIndicesForEditCell(
            sourceTable,
            editDisplayRowIndex,
            editDisplayColumnIndex,
            _multiSelectTargetRowIndices);

        if (_multiSelectTargetRowIndices.Count <= 0)
        {
            return false;
        }

        string newOptionValue = typedValue.ToString().Trim();
        if (string.IsNullOrWhiteSpace(newOptionValue))
        {
            return false;
        }

        var oldOptions = selectColumn.Options != null
            ? new List<string>(selectColumn.Options)
            : new List<string>();
        var newOptions = new List<string>(oldOptions.Count + 1);
        for (int optionIndex = 0; optionIndex < oldOptions.Count; optionIndex++)
        {
            newOptions.Add(oldOptions[optionIndex]);
        }

        newOptions.Add(newOptionValue);

        var commands = new List<DocCommand>(_multiSelectTargetRowIndices.Count + 1)
        {
            new()
            {
                Kind = DocCommandKind.SetColumnOptions,
                TableId = sourceTable.Id,
                ColumnId = selectColumn.Id,
                OldOptionsSnapshot = oldOptions,
                NewOptionsSnapshot = newOptions,
            }
        };

        for (int targetRowIndex = 0; targetRowIndex < _multiSelectTargetRowIndices.Count; targetRowIndex++)
        {
            int sourceRowIndex = _multiSelectTargetRowIndices[targetRowIndex];
            var sourceRow = sourceTable.Rows[sourceRowIndex];
            var oldCell = sourceRow.GetCell(selectColumn);
            string oldCellValue = oldCell.StringValue ?? "";
            if (string.Equals(oldCellValue, newOptionValue, StringComparison.Ordinal))
            {
                continue;
            }

            commands.Add(new DocCommand
            {
                Kind = DocCommandKind.SetCell,
                TableId = sourceTable.Id,
                RowId = sourceRow.Id,
                ColumnId = selectColumn.Id,
                OldCellValue = oldCell,
                NewCellValue = DocCellValue.Text(newOptionValue),
            });
        }

        workspace.ExecuteCommands(commands);
        workspace.CancelTableCellEditIfActive();
        return true;
    }

    private static void DrawRelationTypeaheadEditor(
        DocWorkspace workspace,
        DocTable sourceTable,
        DocColumn relationColumn,
        ImRect cellRect,
        ImInput input)
    {
        ref var edit = ref workspace.EditState;
        if (!TryResolveRelationTargetTable(workspace, sourceTable, relationColumn, out DocTable relationTable))
        {
            workspace.CancelTableCellEditIfActive();
            return;
        }

        relationTable = workspace.ResolveTableForVariant(relationTable, relationColumn.RelationTableVariantId);

        Im.DrawRect(cellRect.X, cellRect.Y, cellRect.Width, cellRect.Height, Im.Style.Background);
        float inputHeight = MathF.Min(cellRect.Height - 2f, Im.Style.MinButtonHeight);
        float inputY = cellRect.Y + (cellRect.Height - inputHeight) * 0.5f;
        float inputX = cellRect.X + CellPaddingX;
        float inputWidth = Math.Max(6f, cellRect.Width - CellPaddingX * 2f);
        Im.TextInput(
            "doc_cell_relation_edit",
            edit.Buffer,
            ref edit.BufferLength,
            256,
            inputX,
            inputY,
            inputWidth,
            Im.ImTextInputFlags.NoBackground | Im.ImTextInputFlags.NoBorder);

        if (input.KeyEscape)
        {
            workspace.CancelTableCellEditIfActive();
            return;
        }

        var relationOptions = BuildRelationOptions(workspace, relationTable, relationColumn);
        int filteredCount = 0;
        bool hasExactLabel = false;
        ReadOnlySpan<char> typedLabel = edit.Buffer.AsSpan(0, edit.BufferLength).Trim();
        for (int optionIndex = 0; optionIndex < relationOptions.Count; optionIndex++)
        {
            string optionLabel = relationOptions[optionIndex].label;
            if (typedLabel.Length > 0 &&
                optionLabel.AsSpan().Equals(typedLabel, StringComparison.OrdinalIgnoreCase))
            {
                hasExactLabel = true;
            }

            bool includeOption = typedLabel.Length == 0 ||
                optionLabel.AsSpan().Contains(typedLabel, StringComparison.OrdinalIgnoreCase);
            if (!includeOption)
            {
                continue;
            }

            if (filteredCount >= _cellTypeaheadFilterIndices.Length)
            {
                break;
            }

            _cellTypeaheadFilterIndices[filteredCount] = optionIndex;
            filteredCount++;
        }

        bool canCreateRow = TryResolveRelationCreateDisplayColumn(workspace, relationColumn, relationTable, out var relationDisplayColumn);
        bool showCreateOption = canCreateRow && typedLabel.Length > 0 && !hasExactLabel;
        int optionCount = filteredCount + (showCreateOption ? 1 : 0);
        int drawnRows = Math.Min(Math.Max(optionCount, 1), (int)CellTypeaheadMaxVisibleRows);
        float itemHeight = MathF.Max(Im.Style.MinButtonHeight, 22f);
        float popupHeight = 2f + (drawnRows * itemHeight);
        float popupY = cellRect.Bottom;
        if (popupY + popupHeight > _bodyRect.Bottom)
        {
            popupY = Math.Max(_headerRect.Bottom, cellRect.Y - popupHeight);
        }

        var popupRect = new ImRect(cellRect.X, popupY, cellRect.Width, popupHeight);
        _cellTypeaheadPopupRect = popupRect;
        _cellTypeaheadPopupVisible = true;
        using var popupOverlayScope = ImPopover.PushOverlayScopeLocal(popupRect);

        if (optionCount > 0)
        {
            int maxSelectableIndex = Math.Max(0, drawnRows - 1);
            if (edit.DropdownIndex < 0 || edit.DropdownIndex > maxSelectableIndex)
            {
                edit.DropdownIndex = 0;
            }

            if (input.KeyDown)
            {
                edit.DropdownIndex = Math.Min(maxSelectableIndex, edit.DropdownIndex + 1);
            }
            if (input.KeyUp)
            {
                edit.DropdownIndex = Math.Max(0, edit.DropdownIndex - 1);
            }
        }
        else
        {
            edit.DropdownIndex = -1;
        }

        var style = Im.Style;
        Im.DrawRoundedRect(popupRect.X, popupRect.Y, popupRect.Width, popupRect.Height, 6f, style.Surface);
        Im.DrawRoundedRectStroke(popupRect.X, popupRect.Y, popupRect.Width, popupRect.Height, 6f, style.Border, 1f);

        int clickedIndex = -1;
        var mousePos = Im.MousePos;
        for (int drawIndex = 0; drawIndex < drawnRows; drawIndex++)
        {
            float rowY = popupRect.Y + 1f + drawIndex * itemHeight;
            var optionRect = new ImRect(popupRect.X + 1f, rowY, popupRect.Width - 2f, itemHeight);
            bool hovered = optionRect.Contains(mousePos);
            bool selected = drawIndex == edit.DropdownIndex;
            if (selected || hovered)
            {
                uint rowColor = selected ? style.Active : style.Hover;
                Im.DrawRect(optionRect.X, optionRect.Y, optionRect.Width, optionRect.Height, rowColor);
            }

            if (hovered)
            {
                edit.DropdownIndex = drawIndex;
                if (input.MousePressed)
                {
                    clickedIndex = drawIndex;
                    Im.Context.ConsumeMouseLeftPress();
                }
            }

            float textY = optionRect.Y + (optionRect.Height - style.FontSize) * 0.5f;
            if (optionCount <= 0)
            {
                Im.Text("(no matches)".AsSpan(), optionRect.X + 8f, textY, style.FontSize, style.TextSecondary);
                continue;
            }

            if (drawIndex < filteredCount)
            {
                int relationOptionIndex = _cellTypeaheadFilterIndices[drawIndex];
                string label = relationOptions[relationOptionIndex].label;
                Im.Text(label.AsSpan(), optionRect.X + 8f, textY, style.FontSize, style.TextPrimary);
                continue;
            }

            if (showCreateOption)
            {
                string createLabel = "+ '" + typedLabel.ToString() + "'";
                Im.Text(createLabel.AsSpan(), optionRect.X + 8f, textY, style.FontSize, style.TextPrimary);
            }
        }

        if (input.KeyEnter && optionCount > 0 && edit.DropdownIndex >= 0)
        {
            clickedIndex = edit.DropdownIndex;
        }

        if (clickedIndex < 0 || optionCount <= 0)
        {
            return;
        }

        if (clickedIndex < filteredCount)
        {
            int selectedOptionIndex = _cellTypeaheadFilterIndices[clickedIndex];
            CommitRelationCellSelection(workspace, sourceTable, relationColumn, edit.RowIndex, edit.ColIndex, relationOptions[selectedOptionIndex].rowId);
            return;
        }

        if (!showCreateOption)
        {
            return;
        }

        if (TryCreateRelationRowAndCommit(
                workspace,
                sourceTable,
                relationColumn,
                edit.RowIndex,
                edit.ColIndex,
                relationTable,
                relationDisplayColumn,
                typedLabel))
        {
            return;
        }
    }

    private static bool TryResolveRelationTargetTable(
        DocWorkspace workspace,
        DocTable sourceTable,
        DocColumn relationColumn,
        out DocTable relationTable)
    {
        relationTable = null!;
        string? relationTargetTableId = DocRelationTargetResolver.ResolveTargetTableId(sourceTable, relationColumn);
        if (string.IsNullOrWhiteSpace(relationTargetTableId))
        {
            return false;
        }

        for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
        {
            DocTable candidateTable = workspace.Project.Tables[tableIndex];
            if (!string.Equals(candidateTable.Id, relationTargetTableId, StringComparison.Ordinal))
            {
                continue;
            }

            relationTable = candidateTable;
            return true;
        }

        return false;
    }

    private static void DrawTableRefTypeaheadEditor(
        DocWorkspace workspace,
        DocTable sourceTable,
        DocColumn tableRefColumn,
        ImRect cellRect,
        ImInput input)
    {
        _ = sourceTable;
        ref var edit = ref workspace.EditState;

        Im.DrawRect(cellRect.X, cellRect.Y, cellRect.Width, cellRect.Height, Im.Style.Background);
        float inputHeight = MathF.Min(cellRect.Height - 2f, Im.Style.MinButtonHeight);
        float inputY = cellRect.Y + (cellRect.Height - inputHeight) * 0.5f;
        float inputX = cellRect.X + CellPaddingX;
        float inputWidth = Math.Max(6f, cellRect.Width - CellPaddingX * 2f);
        Im.TextInput(
            "doc_cell_table_ref_edit",
            edit.Buffer,
            ref edit.BufferLength,
            256,
            inputX,
            inputY,
            inputWidth,
            Im.ImTextInputFlags.NoBackground | Im.ImTextInputFlags.NoBorder);

        if (input.KeyEscape)
        {
            workspace.CancelTableCellEditIfActive();
            return;
        }

        var tableRefOptions = BuildTableRefOptions(workspace, tableRefColumn);
        int filteredCount = 0;
        ReadOnlySpan<char> typedLabel = edit.Buffer.AsSpan(0, edit.BufferLength).Trim();
        for (int optionIndex = 0; optionIndex < tableRefOptions.Count; optionIndex++)
        {
            string optionLabel = tableRefOptions[optionIndex].label;
            string optionTableId = tableRefOptions[optionIndex].tableId;
            bool includeOption = typedLabel.Length == 0 ||
                optionLabel.AsSpan().Contains(typedLabel, StringComparison.OrdinalIgnoreCase) ||
                optionTableId.AsSpan().Contains(typedLabel, StringComparison.OrdinalIgnoreCase);
            if (!includeOption)
            {
                continue;
            }

            if (filteredCount >= _cellTypeaheadFilterIndices.Length)
            {
                break;
            }

            _cellTypeaheadFilterIndices[filteredCount] = optionIndex;
            filteredCount++;
        }

        int optionCount = filteredCount;
        int drawnRows = Math.Min(Math.Max(optionCount, 1), (int)CellTypeaheadMaxVisibleRows);
        float itemHeight = MathF.Max(Im.Style.MinButtonHeight, 22f);
        float popupHeight = 2f + (drawnRows * itemHeight);
        float popupY = cellRect.Bottom;
        if (popupY + popupHeight > _bodyRect.Bottom)
        {
            popupY = Math.Max(_headerRect.Bottom, cellRect.Y - popupHeight);
        }

        var popupRect = new ImRect(cellRect.X, popupY, cellRect.Width, popupHeight);
        _cellTypeaheadPopupRect = popupRect;
        _cellTypeaheadPopupVisible = true;
        using var popupOverlayScope = ImPopover.PushOverlayScopeLocal(popupRect);

        if (optionCount > 0)
        {
            int maxSelectableIndex = Math.Max(0, drawnRows - 1);
            if (edit.DropdownIndex < 0 || edit.DropdownIndex > maxSelectableIndex)
            {
                edit.DropdownIndex = 0;
            }

            if (input.KeyDown)
            {
                edit.DropdownIndex = Math.Min(maxSelectableIndex, edit.DropdownIndex + 1);
            }
            if (input.KeyUp)
            {
                edit.DropdownIndex = Math.Max(0, edit.DropdownIndex - 1);
            }
        }
        else
        {
            edit.DropdownIndex = -1;
        }

        var style = Im.Style;
        Im.DrawRoundedRect(popupRect.X, popupRect.Y, popupRect.Width, popupRect.Height, 6f, style.Surface);
        Im.DrawRoundedRectStroke(popupRect.X, popupRect.Y, popupRect.Width, popupRect.Height, 6f, style.Border, 1f);

        int clickedIndex = -1;
        Vector2 mousePos = Im.MousePos;
        for (int drawIndex = 0; drawIndex < drawnRows; drawIndex++)
        {
            float rowY = popupRect.Y + 1f + drawIndex * itemHeight;
            var optionRect = new ImRect(popupRect.X + 1f, rowY, popupRect.Width - 2f, itemHeight);
            bool hovered = optionRect.Contains(mousePos);
            bool selected = drawIndex == edit.DropdownIndex;
            if (selected || hovered)
            {
                uint rowColor = selected ? style.Active : style.Hover;
                Im.DrawRect(optionRect.X, optionRect.Y, optionRect.Width, optionRect.Height, rowColor);
            }

            if (hovered)
            {
                edit.DropdownIndex = drawIndex;
                if (input.MousePressed)
                {
                    clickedIndex = drawIndex;
                    Im.Context.ConsumeMouseLeftPress();
                }
            }

            float textY = optionRect.Y + (optionRect.Height - style.FontSize) * 0.5f;
            if (optionCount <= 0)
            {
                Im.Text("(no matches)".AsSpan(), optionRect.X + 8f, textY, style.FontSize, style.TextSecondary);
                continue;
            }

            int tableRefOptionIndex = _cellTypeaheadFilterIndices[drawIndex];
            string label = tableRefOptions[tableRefOptionIndex].label;
            Im.Text(label.AsSpan(), optionRect.X + 8f, textY, style.FontSize, style.TextPrimary);
        }

        if (input.KeyEnter && optionCount > 0 && edit.DropdownIndex >= 0)
        {
            clickedIndex = edit.DropdownIndex;
        }

        if (clickedIndex < 0 || optionCount <= 0)
        {
            return;
        }

        int selectedOptionIndex = _cellTypeaheadFilterIndices[clickedIndex];
        CommitTableRefCellSelection(
            workspace,
            sourceTable,
            tableRefColumn,
            edit.RowIndex,
            edit.ColIndex,
            tableRefOptions[selectedOptionIndex].tableId);
    }

    private static void CommitTableRefCellSelection(
        DocWorkspace workspace,
        DocTable sourceTable,
        DocColumn tableRefColumn,
        int editDisplayRowIndex,
        int editDisplayColumnIndex,
        string selectedTableId)
    {
        _multiSelectTargetRowIndices.Clear();
        CollectSelectedSourceRowIndicesForEditCell(
            sourceTable,
            editDisplayRowIndex,
            editDisplayColumnIndex,
            _multiSelectTargetRowIndices);

        if (_multiSelectTargetRowIndices.Count <= 0)
        {
            workspace.CancelTableCellEditIfActive();
            return;
        }

        var commands = new List<DocCommand>(_multiSelectTargetRowIndices.Count);
        for (int targetRowIndex = 0; targetRowIndex < _multiSelectTargetRowIndices.Count; targetRowIndex++)
        {
            int sourceRowIndex = _multiSelectTargetRowIndices[targetRowIndex];
            DocRow sourceRow = sourceTable.Rows[sourceRowIndex];
            DocCellValue oldCell = sourceRow.GetCell(tableRefColumn);
            string oldTableId = oldCell.StringValue ?? "";
            if (string.Equals(oldTableId, selectedTableId, StringComparison.Ordinal))
            {
                continue;
            }

            commands.Add(new DocCommand
            {
                Kind = DocCommandKind.SetCell,
                TableId = sourceTable.Id,
                RowId = sourceRow.Id,
                ColumnId = tableRefColumn.Id,
                OldCellValue = oldCell,
                NewCellValue = DocCellValue.Text(selectedTableId),
            });
        }

        if (commands.Count == 1)
        {
            workspace.ExecuteCommand(commands[0]);
        }
        else if (commands.Count > 1)
        {
            workspace.ExecuteCommands(commands);
        }

        workspace.CancelTableCellEditIfActive();
    }

    private static void CommitRelationCellSelection(
        DocWorkspace workspace,
        DocTable sourceTable,
        DocColumn relationColumn,
        int editDisplayRowIndex,
        int editDisplayColumnIndex,
        string selectedRelationRowId)
    {
        _multiSelectTargetRowIndices.Clear();
        CollectSelectedSourceRowIndicesForEditCell(
            sourceTable,
            editDisplayRowIndex,
            editDisplayColumnIndex,
            _multiSelectTargetRowIndices);

        if (_multiSelectTargetRowIndices.Count <= 0)
        {
            workspace.CancelTableCellEditIfActive();
            return;
        }

        var commands = new List<DocCommand>(_multiSelectTargetRowIndices.Count);
        for (int targetRowIndex = 0; targetRowIndex < _multiSelectTargetRowIndices.Count; targetRowIndex++)
        {
            int sourceRowIndex = _multiSelectTargetRowIndices[targetRowIndex];
            var sourceRow = sourceTable.Rows[sourceRowIndex];
            var oldCell = sourceRow.GetCell(relationColumn);
            string oldRowId = oldCell.StringValue ?? "";
            if (string.Equals(oldRowId, selectedRelationRowId, StringComparison.Ordinal))
            {
                continue;
            }

            commands.Add(new DocCommand
            {
                Kind = DocCommandKind.SetCell,
                TableId = sourceTable.Id,
                RowId = sourceRow.Id,
                ColumnId = relationColumn.Id,
                OldCellValue = oldCell,
                NewCellValue = DocCellValue.Text(selectedRelationRowId)
            });
        }

        if (commands.Count == 1)
        {
            workspace.ExecuteCommand(commands[0]);
        }
        else if (commands.Count > 1)
        {
            workspace.ExecuteCommands(commands);
        }

        workspace.CancelTableCellEditIfActive();
    }

    private static bool TryResolveRelationCreateDisplayColumn(
        DocWorkspace workspace,
        DocColumn relationColumn,
        DocTable relationTable,
        out DocColumn displayColumn)
    {
        displayColumn = null!;
        if (relationTable.IsDerived)
        {
            return false;
        }

        if (!workspace.TryResolveRelationDisplayColumn(relationColumn, relationTable, out displayColumn))
        {
            return false;
        }

        if (displayColumn.Kind == DocColumnKind.Formula || HasFormulaExpression(displayColumn))
        {
            return false;
        }

        return displayColumn.Kind == DocColumnKind.Text ||
               displayColumn.Kind == DocColumnKind.Select ||
               displayColumn.Kind == DocColumnKind.Number;
    }

    private static bool TryCreateRelationRowAndCommit(
        DocWorkspace workspace,
        DocTable sourceTable,
        DocColumn relationColumn,
        int editDisplayRowIndex,
        int editDisplayColumnIndex,
        DocTable relationTable,
        DocColumn displayColumn,
        ReadOnlySpan<char> typedLabel)
    {
        _multiSelectTargetRowIndices.Clear();
        CollectSelectedSourceRowIndicesForEditCell(
            sourceTable,
            editDisplayRowIndex,
            editDisplayColumnIndex,
            _multiSelectTargetRowIndices);

        if (_multiSelectTargetRowIndices.Count <= 0)
        {
            return false;
        }

        if (typedLabel.Length <= 0)
        {
            return false;
        }

        var relationRow = new DocRow();
        for (int columnIndex = 0; columnIndex < relationTable.Columns.Count; columnIndex++)
        {
            var targetColumn = relationTable.Columns[columnIndex];
            relationRow.SetCell(targetColumn.Id, DocCellValue.Default(targetColumn));
        }

        if (!TryAssignRelationDisplayCellValue(relationRow, displayColumn, typedLabel))
        {
            return false;
        }

        var commands = new List<DocCommand>(_multiSelectTargetRowIndices.Count + 1)
        {
            new()
            {
                Kind = DocCommandKind.AddRow,
                TableId = relationTable.Id,
                RowIndex = relationTable.Rows.Count,
                RowSnapshot = relationRow
            }
        };

        for (int targetRowIndex = 0; targetRowIndex < _multiSelectTargetRowIndices.Count; targetRowIndex++)
        {
            int sourceRowIndex = _multiSelectTargetRowIndices[targetRowIndex];
            var sourceRow = sourceTable.Rows[sourceRowIndex];
            var oldSourceCell = sourceRow.GetCell(relationColumn);
            string oldRelationRowId = oldSourceCell.StringValue ?? "";
            if (string.Equals(oldRelationRowId, relationRow.Id, StringComparison.Ordinal))
            {
                continue;
            }

            commands.Add(new DocCommand
            {
                Kind = DocCommandKind.SetCell,
                TableId = sourceTable.Id,
                RowId = sourceRow.Id,
                ColumnId = relationColumn.Id,
                OldCellValue = oldSourceCell,
                NewCellValue = DocCellValue.Text(relationRow.Id)
            });
        }

        workspace.ExecuteCommands(commands);
        workspace.CancelTableCellEditIfActive();
        return true;
    }

    private static bool TryAssignRelationDisplayCellValue(DocRow row, DocColumn displayColumn, ReadOnlySpan<char> typedLabel)
    {
        if (displayColumn.Kind == DocColumnKind.Number)
        {
            if (!double.TryParse(typedLabel, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedNumber))
            {
                return false;
            }

            row.SetCell(displayColumn.Id, DocCellValue.Number(parsedNumber));
            return true;
        }

        row.SetCell(displayColumn.Id, DocCellValue.Text(typedLabel.ToString()));
        return true;
    }

    private static void OpenEditRelationDialog(
        DocWorkspace workspace,
        DocTable sourceTable,
        DocColumn relationColumn,
        int relationColumnIndex)
    {
        _editRelationColIndex = relationColumnIndex;
        _editRelationTargetModeIndex = ResolveRelationTargetModeIndex(relationColumn.RelationTargetMode);
        _editRelationTableIndex = ResolveDefaultRelationTableIndex(workspace, sourceTable);
        string? resolvedTargetTableId = DocRelationTargetResolver.ResolveTargetTableId(sourceTable, relationColumn);
        if (!string.IsNullOrWhiteSpace(resolvedTargetTableId))
        {
            _editRelationTableIndex = ResolveTableIndexById(workspace, resolvedTargetTableId);
        }

        _editRelationVariantIndex = 0;
        if (_editRelationTableIndex >= 0 && _editRelationTableIndex < workspace.Project.Tables.Count)
        {
            var relationTable = workspace.Project.Tables[_editRelationTableIndex];
            _editRelationVariantIndex = ResolveRelationVariantChoiceIndex(relationTable, relationColumn.RelationTableVariantId);
        }

        _editRelationDisplayColumnIndex = -1;
        if (_editRelationTableIndex >= 0 && _editRelationTableIndex < workspace.Project.Tables.Count)
        {
            var relationTable = workspace.Project.Tables[_editRelationTableIndex];
            var candidates = BuildRelationDisplayColumnChoices(relationTable);
            _editRelationDisplayColumnIndex = ResolveRelationDisplayColumnChoiceIndex(candidates, relationColumn.RelationDisplayColumnId);
        }

        _showEditRelationDialog = true;
        _editRelationDialogOpenedFrame = Im.Context.FrameCount;
    }

    private static int ResolveRelationTargetModeIndex(DocRelationTargetMode relationTargetMode)
    {
        return relationTargetMode switch
        {
            DocRelationTargetMode.SelfTable => 1,
            DocRelationTargetMode.ParentTable => 2,
            _ => 0,
        };
    }

    private static DocRelationTargetMode ResolveRelationTargetModeFromIndex(int modeIndex)
    {
        return modeIndex switch
        {
            1 => DocRelationTargetMode.SelfTable,
            2 => DocRelationTargetMode.ParentTable,
            _ => DocRelationTargetMode.ExternalTable,
        };
    }

    private static int ResolveRelationVariantChoiceIndex(DocTable relationTable, int variantId)
    {
        if (variantId == DocTableVariant.BaseVariantId)
        {
            return 0;
        }

        for (int variantIndex = 0; variantIndex < relationTable.Variants.Count; variantIndex++)
        {
            if (relationTable.Variants[variantIndex].Id == variantId)
            {
                return variantIndex + 1;
            }
        }

        return 0;
    }

    private static void OpenEditNumberColumnDialog(DocTable table, DocColumn column)
    {
        _showEditNumberColumnDialog = true;
        _editNumberColumnDialogOpenedFrame = Im.Context.FrameCount;
        _editNumberTableId = table.Id;
        _editNumberColumnId = column.Id;
        _editNumberTypeIndex = ResolveEditNumberTypeIndex(column.ExportType);
        _editNumberValidationMessage = "";

        Array.Clear(_editNumberMinBuffer);
        _editNumberMinBufferLength = 0;
        if (column.NumberMin.HasValue)
        {
            string minText = column.NumberMin.Value.ToString("G17", CultureInfo.InvariantCulture);
            _editNumberMinBufferLength = Math.Min(minText.Length, _editNumberMinBuffer.Length);
            minText.AsSpan(0, _editNumberMinBufferLength).CopyTo(_editNumberMinBuffer);
        }

        Array.Clear(_editNumberMaxBuffer);
        _editNumberMaxBufferLength = 0;
        if (column.NumberMax.HasValue)
        {
            string maxText = column.NumberMax.Value.ToString("G17", CultureInfo.InvariantCulture);
            _editNumberMaxBufferLength = Math.Min(maxText.Length, _editNumberMaxBuffer.Length);
            maxText.AsSpan(0, _editNumberMaxBufferLength).CopyTo(_editNumberMaxBuffer);
        }
    }

    private static void OpenEditSubtableDisplayDialog(DocTable table, DocColumn column)
    {
        _showEditSubtableDisplayDialog = true;
        _editSubtableDisplayDialogOpenedFrame = Im.Context.FrameCount;
        _editSubtableDisplayTableId = table.Id;
        _editSubtableDisplayColumnId = column.Id;
        _editSubtableDisplayValidationMessage = "";

        RebuildSubtableDisplayRendererOptions(column.SubtableDisplayRendererId);
        _editSubtableDisplayUseCustomWidth = column.SubtableDisplayCellWidth.HasValue;
        _editSubtableDisplayUseCustomHeight = column.SubtableDisplayCellHeight.HasValue;
        _editSubtableDisplayPreviewQualityIndex = ResolveSubtableDisplayPreviewQualityOptionIndex(
            column.SubtableDisplayPreviewQuality);
        _editSubtableDisplayPluginSettingsRendererId = "";
        _editSubtableDisplayPluginSettingsJson = null;

        float defaultPreviewWidth = Math.Max(SubtableDisplayMinPreviewWidth, column.Width - (CellPaddingX * 2f));
        _editSubtableDisplayWidthValue = Math.Clamp(
            column.SubtableDisplayCellWidth ?? defaultPreviewWidth,
            SubtableDisplayMinPreviewWidth,
            SubtableDisplayMaxPreviewSize);

        float defaultPreviewHeight = ResolveSubtableDisplayDefaultHeight(column);
        _editSubtableDisplayHeightValue = Math.Clamp(
            column.SubtableDisplayCellHeight ?? defaultPreviewHeight,
            SubtableDisplayMinPreviewHeight,
            SubtableDisplayMaxPreviewSize);

        TrySyncEditSubtableDisplayPluginSettingsDraft(column);
    }

    private static void RebuildSubtableDisplayRendererOptions(string? currentRendererId)
    {
        _subtableRendererOptionsScratch.Clear();
        TableViewRendererRegistry.CopyRenderers(_subtableRendererOptionsScratch);
        _nodeSubtableRendererIdOptionsScratch.Clear();
        NodeSubtableSectionRendererRegistry.CopyRendererIds(_nodeSubtableRendererIdOptionsScratch);

        int optionCount = 5 + _subtableRendererOptionsScratch.Count + _nodeSubtableRendererIdOptionsScratch.Count;
        EnsureSubtableDisplayRendererOptionCapacity(optionCount + 1);

        int optionIndex = 0;
        _subtableDisplayRendererOptionNames[optionIndex] = "(item count)";
        _subtableDisplayRendererOptionIds[optionIndex] = "";
        optionIndex++;
        _subtableDisplayRendererOptionNames[optionIndex] = "Grid";
        _subtableDisplayRendererOptionIds[optionIndex] = SubtableDisplayRendererGrid;
        optionIndex++;
        _subtableDisplayRendererOptionNames[optionIndex] = "Board";
        _subtableDisplayRendererOptionIds[optionIndex] = SubtableDisplayRendererBoard;
        optionIndex++;
        _subtableDisplayRendererOptionNames[optionIndex] = "Calendar";
        _subtableDisplayRendererOptionIds[optionIndex] = SubtableDisplayRendererCalendar;
        optionIndex++;
        _subtableDisplayRendererOptionNames[optionIndex] = "Chart";
        _subtableDisplayRendererOptionIds[optionIndex] = SubtableDisplayRendererChart;
        optionIndex++;

        for (int rendererIndex = 0; rendererIndex < _subtableRendererOptionsScratch.Count; rendererIndex++)
        {
            var renderer = _subtableRendererOptionsScratch[rendererIndex];
            _subtableDisplayRendererOptionNames[optionIndex] = renderer.DisplayName;
            _subtableDisplayRendererOptionIds[optionIndex] = SubtableDisplayCustomRendererPrefix + renderer.RendererId;
            optionIndex++;
        }

        for (int rendererIndex = 0; rendererIndex < _nodeSubtableRendererIdOptionsScratch.Count; rendererIndex++)
        {
            string rendererId = _nodeSubtableRendererIdOptionsScratch[rendererIndex];
            if (string.IsNullOrWhiteSpace(rendererId))
            {
                continue;
            }

            string optionRendererId = SubtableDisplayCustomRendererPrefix + rendererId;
            bool hasDuplicateOption = false;
            for (int existingOptionIndex = 0; existingOptionIndex < optionIndex; existingOptionIndex++)
            {
                if (!string.Equals(_subtableDisplayRendererOptionIds[existingOptionIndex], optionRendererId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                hasDuplicateOption = true;
                break;
            }

            if (hasDuplicateOption)
            {
                continue;
            }

            _subtableDisplayRendererOptionNames[optionIndex] = "Node plugin: " + rendererId;
            _subtableDisplayRendererOptionIds[optionIndex] = optionRendererId;
            optionIndex++;
        }

        string normalizedCurrentRendererId = NormalizeSubtableDisplayRendererId(currentRendererId);
        _subtableDisplayRendererOptionCount = optionIndex;
        _editSubtableDisplayRendererIndex = 0;
        if (!string.IsNullOrWhiteSpace(normalizedCurrentRendererId))
        {
            bool foundOption = false;
            for (int existingOptionIndex = 0; existingOptionIndex < _subtableDisplayRendererOptionCount; existingOptionIndex++)
            {
                if (!string.Equals(
                        _subtableDisplayRendererOptionIds[existingOptionIndex],
                        normalizedCurrentRendererId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                _editSubtableDisplayRendererIndex = existingOptionIndex;
                foundOption = true;
                break;
            }

            if (!foundOption)
            {
                EnsureSubtableDisplayRendererOptionCapacity(_subtableDisplayRendererOptionCount + 1);
                string customLabelId = normalizedCurrentRendererId.StartsWith(SubtableDisplayCustomRendererPrefix, StringComparison.Ordinal)
                    ? normalizedCurrentRendererId[SubtableDisplayCustomRendererPrefix.Length..]
                    : normalizedCurrentRendererId;
                _subtableDisplayRendererOptionNames[_subtableDisplayRendererOptionCount] = "Custom (" + customLabelId + ")";
                _subtableDisplayRendererOptionIds[_subtableDisplayRendererOptionCount] = normalizedCurrentRendererId;
                _editSubtableDisplayRendererIndex = _subtableDisplayRendererOptionCount;
                _subtableDisplayRendererOptionCount++;
            }
        }
    }

    private static void EnsureSubtableDisplayRendererOptionCapacity(int requiredCount)
    {
        if (_subtableDisplayRendererOptionNames.Length >= requiredCount)
        {
            return;
        }

        int newLength = _subtableDisplayRendererOptionNames.Length;
        while (newLength < requiredCount)
        {
            newLength *= 2;
        }

        Array.Resize(ref _subtableDisplayRendererOptionNames, newLength);
        Array.Resize(ref _subtableDisplayRendererOptionIds, newLength);
    }

    private static string? NormalizeSubtableDisplayPluginSettingsJson(string? pluginSettingsJson)
    {
        return string.IsNullOrWhiteSpace(pluginSettingsJson)
            ? null
            : pluginSettingsJson;
    }

    private static int ResolveSubtableDisplayPreviewQualityOptionIndex(DocSubtablePreviewQuality? quality)
    {
        if (!quality.HasValue)
        {
            return 0;
        }

        return quality.Value switch
        {
            DocSubtablePreviewQuality.Off => 1,
            DocSubtablePreviewQuality.Lite => 2,
            DocSubtablePreviewQuality.Full => 3,
            _ => 0,
        };
    }

    private static DocSubtablePreviewQuality? ResolveSubtableDisplayPreviewQualityFromOptionIndex(int optionIndex)
    {
        return optionIndex switch
        {
            0 => null,
            1 => DocSubtablePreviewQuality.Off,
            2 => DocSubtablePreviewQuality.Lite,
            3 => DocSubtablePreviewQuality.Full,
            _ => null,
        };
    }

    private static bool TrySyncEditSubtableDisplayPluginSettingsDraft(DocColumn subtableColumn)
    {
        if (_editSubtableDisplayRendererIndex < 0 ||
            _editSubtableDisplayRendererIndex >= _subtableDisplayRendererOptionCount)
        {
            _editSubtableDisplayPluginSettingsRendererId = "";
            _editSubtableDisplayPluginSettingsJson = null;
            return false;
        }

        string selectedRendererOptionId = _subtableDisplayRendererOptionIds[_editSubtableDisplayRendererIndex];
        string normalizedSelectedRendererId = NormalizeSubtableDisplayRendererId(selectedRendererOptionId);
        if (!TryResolveSubtableDisplayRendererKind(
                normalizedSelectedRendererId,
                out var selectedRendererKind,
                out string? selectedCustomRendererId) ||
            selectedRendererKind != SubtableDisplayRendererKind.Custom ||
            string.IsNullOrWhiteSpace(selectedCustomRendererId))
        {
            _editSubtableDisplayPluginSettingsRendererId = "";
            _editSubtableDisplayPluginSettingsJson = null;
            return false;
        }

        if (string.Equals(
                _editSubtableDisplayPluginSettingsRendererId,
                selectedCustomRendererId,
                StringComparison.Ordinal))
        {
            return true;
        }

        _editSubtableDisplayPluginSettingsRendererId = selectedCustomRendererId;
        string normalizedExistingRendererId = NormalizeSubtableDisplayRendererId(subtableColumn.SubtableDisplayRendererId);
        if (string.Equals(normalizedExistingRendererId, normalizedSelectedRendererId, StringComparison.Ordinal))
        {
            _editSubtableDisplayPluginSettingsJson =
                NormalizeSubtableDisplayPluginSettingsJson(subtableColumn.PluginSettingsJson);
            return true;
        }

        _editSubtableDisplayPluginSettingsJson = null;
        return true;
    }

    private static bool TryFindEditNumberColumn(
        DocWorkspace workspace,
        out DocTable sourceTable,
        out DocColumn numberColumn)
    {
        sourceTable = null!;
        numberColumn = null!;

        for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
        {
            var table = workspace.Project.Tables[tableIndex];
            if (!string.Equals(table.Id, _editNumberTableId, StringComparison.Ordinal))
            {
                continue;
            }

            for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                var column = table.Columns[columnIndex];
                if (!string.Equals(column.Id, _editNumberColumnId, StringComparison.Ordinal))
                {
                    continue;
                }

                sourceTable = table;
                numberColumn = column;
                return true;
            }
        }

        return false;
    }

    private static bool TryFindEditSubtableDisplayColumn(
        DocWorkspace workspace,
        out DocTable sourceTable,
        out DocColumn subtableColumn)
    {
        sourceTable = null!;
        subtableColumn = null!;

        for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
        {
            var table = workspace.Project.Tables[tableIndex];
            if (!string.Equals(table.Id, _editSubtableDisplayTableId, StringComparison.Ordinal))
            {
                continue;
            }

            for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                var column = table.Columns[columnIndex];
                if (!string.Equals(column.Id, _editSubtableDisplayColumnId, StringComparison.Ordinal))
                {
                    continue;
                }

                sourceTable = table;
                subtableColumn = column;
                return true;
            }
        }

        return false;
    }

    private static int ResolveEditNumberTypeIndex(string? exportType)
    {
        if (string.Equals(exportType, "int", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(exportType, "float", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (string.Equals(exportType, "Fixed32", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        return 2;
    }

    private static bool IsIntegerNumberColumn(DocColumn column)
    {
        return string.Equals(column.ExportType, "int", StringComparison.OrdinalIgnoreCase);
    }

    private static bool AreEquivalentNumberExportTypes(string? left, string? right)
    {
        string normalizedLeft = string.IsNullOrWhiteSpace(left) ? "Fixed64" : left!;
        string normalizedRight = string.IsNullOrWhiteSpace(right) ? "Fixed64" : right!;
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveNumberExportTypeForSelection(string? currentExportType, int selectedTypeIndex)
    {
        int safeIndex = Math.Clamp(selectedTypeIndex, 0, _numberTypeLabels.Length - 1);
        if (safeIndex == 0)
        {
            return "int";
        }

        if (safeIndex == 1)
        {
            return "float";
        }

        if (safeIndex == 3)
        {
            return "Fixed32";
        }

        if (string.IsNullOrWhiteSpace(currentExportType))
        {
            return null;
        }

        if (string.Equals(currentExportType, "Fixed64", StringComparison.OrdinalIgnoreCase))
        {
            return currentExportType;
        }

        return "Fixed64";
    }

    private static bool TryParseOptionalNumberLimit(
        char[] buffer,
        int length,
        out double? parsedValue)
    {
        ReadOnlySpan<char> trimmedText = buffer.AsSpan(0, Math.Clamp(length, 0, buffer.Length)).Trim();
        if (trimmedText.Length <= 0)
        {
            parsedValue = null;
            return true;
        }

        if (!double.TryParse(trimmedText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            parsedValue = null;
            return false;
        }

        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            parsedValue = null;
            return false;
        }

        parsedValue = value;
        return true;
    }

    private static void ApplyEditNumberColumnChanges(DocWorkspace workspace, DocTable sourceTable, DocColumn numberColumn)
    {
        if (!TryParseOptionalNumberLimit(_editNumberMinBuffer, _editNumberMinBufferLength, out var newMin))
        {
            _editNumberValidationMessage = "Minimum must be a valid number.";
            return;
        }

        if (!TryParseOptionalNumberLimit(_editNumberMaxBuffer, _editNumberMaxBufferLength, out var newMax))
        {
            _editNumberValidationMessage = "Maximum must be a valid number.";
            return;
        }

        if (newMin.HasValue && newMax.HasValue && newMin.Value > newMax.Value)
        {
            _editNumberValidationMessage = "Minimum must be less than or equal to maximum.";
            return;
        }

        string? oldExportType = numberColumn.ExportType;
        string? newExportType = ResolveNumberExportTypeForSelection(numberColumn.ExportType, _editNumberTypeIndex);
        bool exportTypeChanged = !AreEquivalentNumberExportTypes(oldExportType, newExportType);
        bool minChanged = numberColumn.NumberMin != newMin;
        bool maxChanged = numberColumn.NumberMax != newMax;

        var normalizationColumn = new DocColumn
        {
            ExportType = newExportType,
            NumberMin = newMin,
            NumberMax = newMax,
        };

        Dictionary<string, double>? oldValuesByRowId = null;
        Dictionary<string, double>? newValuesByRowId = null;
        for (int rowIndex = 0; rowIndex < sourceTable.Rows.Count; rowIndex++)
        {
            var row = sourceTable.Rows[rowIndex];
            var oldCell = row.GetCell(numberColumn);
            double oldValue = oldCell.NumberValue;
            double newValue = workspace.NormalizeNumberForColumn(normalizationColumn, oldValue);
            if (oldValue == newValue)
            {
                continue;
            }

            oldValuesByRowId ??= new Dictionary<string, double>(StringComparer.Ordinal);
            newValuesByRowId ??= new Dictionary<string, double>(StringComparer.Ordinal);
            oldValuesByRowId[row.Id] = oldValue;
            newValuesByRowId[row.Id] = newValue;
        }

        bool numberValuesChanged = newValuesByRowId != null && newValuesByRowId.Count > 0;
        if (!exportTypeChanged && !minChanged && !maxChanged && !numberValuesChanged)
        {
            _showEditNumberColumnDialog = false;
            _editNumberValidationMessage = "";
            return;
        }

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetColumnNumberSettings,
            TableId = sourceTable.Id,
            ColumnId = numberColumn.Id,
            OldExportType = oldExportType,
            NewExportType = newExportType,
            OldNumberMin = numberColumn.NumberMin,
            NewNumberMin = newMin,
            OldNumberMax = numberColumn.NumberMax,
            NewNumberMax = newMax,
            OldNumberValuesByRowId = oldValuesByRowId,
            NewNumberValuesByRowId = newValuesByRowId,
        });

        _showEditNumberColumnDialog = false;
        _editNumberValidationMessage = "";
    }

    private static void ApplyEditSubtableDisplayChanges(
        DocWorkspace workspace,
        DocTable sourceTable,
        DocColumn subtableColumn)
    {
        if (_editSubtableDisplayRendererIndex < 0 ||
            _editSubtableDisplayRendererIndex >= _subtableDisplayRendererOptionCount)
        {
            _editSubtableDisplayValidationMessage = "Select a valid renderer.";
            return;
        }

        if (_editSubtableDisplayUseCustomWidth &&
            (float.IsNaN(_editSubtableDisplayWidthValue) || float.IsInfinity(_editSubtableDisplayWidthValue)))
        {
            _editSubtableDisplayValidationMessage = "Width must be a positive number.";
            return;
        }

        if (_editSubtableDisplayUseCustomHeight &&
            (float.IsNaN(_editSubtableDisplayHeightValue) || float.IsInfinity(_editSubtableDisplayHeightValue)))
        {
            _editSubtableDisplayValidationMessage = "Height must be a positive number.";
            return;
        }

        float? newPreviewWidth = _editSubtableDisplayUseCustomWidth
            ? Math.Clamp(_editSubtableDisplayWidthValue, SubtableDisplayMinPreviewWidth, SubtableDisplayMaxPreviewSize)
            : null;
        float? newPreviewHeight = _editSubtableDisplayUseCustomHeight
            ? Math.Clamp(_editSubtableDisplayHeightValue, SubtableDisplayMinPreviewHeight, SubtableDisplayMaxPreviewSize)
            : null;

        string selectedRendererOptionId = _subtableDisplayRendererOptionIds[_editSubtableDisplayRendererIndex];
        string normalizedSelectedRendererId = NormalizeSubtableDisplayRendererId(selectedRendererOptionId);
        string? newRendererId = string.IsNullOrWhiteSpace(normalizedSelectedRendererId)
            ? null
            : normalizedSelectedRendererId;
        string normalizedExistingRendererId = NormalizeSubtableDisplayRendererId(subtableColumn.SubtableDisplayRendererId);
        string? oldRendererId = string.IsNullOrWhiteSpace(normalizedExistingRendererId)
            ? null
            : normalizedExistingRendererId;

        string? newPluginSettingsJson = null;
        if (TryResolveSubtableDisplayRendererKind(
                normalizedSelectedRendererId,
                out var selectedRendererKind,
                out string? selectedCustomRendererId) &&
            selectedRendererKind == SubtableDisplayRendererKind.Custom &&
            !string.IsNullOrWhiteSpace(selectedCustomRendererId))
        {
            newPluginSettingsJson = NormalizeSubtableDisplayPluginSettingsJson(_editSubtableDisplayPluginSettingsJson);
        }

        string? oldPluginSettingsJson = NormalizeSubtableDisplayPluginSettingsJson(subtableColumn.PluginSettingsJson);
        DocSubtablePreviewQuality? newPreviewQuality = ResolveSubtableDisplayPreviewQualityFromOptionIndex(
            _editSubtableDisplayPreviewQualityIndex);
        DocSubtablePreviewQuality? oldPreviewQuality = subtableColumn.SubtableDisplayPreviewQuality;

        bool rendererChanged = !string.Equals(oldRendererId, newRendererId, StringComparison.Ordinal);
        bool widthChanged = subtableColumn.SubtableDisplayCellWidth != newPreviewWidth;
        bool heightChanged = subtableColumn.SubtableDisplayCellHeight != newPreviewHeight;
        bool pluginSettingsChanged = !string.Equals(oldPluginSettingsJson, newPluginSettingsJson, StringComparison.Ordinal);
        bool previewQualityChanged = oldPreviewQuality != newPreviewQuality;

        if (!rendererChanged && !widthChanged && !heightChanged && !pluginSettingsChanged && !previewQualityChanged)
        {
            _showEditSubtableDisplayDialog = false;
            _editSubtableDisplayValidationMessage = "";
            return;
        }

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetColumnSubtableDisplay,
            TableId = sourceTable.Id,
            ColumnId = subtableColumn.Id,
            OldSubtableDisplayRendererId = oldRendererId,
            NewSubtableDisplayRendererId = newRendererId,
            OldSubtableDisplayCellWidth = subtableColumn.SubtableDisplayCellWidth,
            NewSubtableDisplayCellWidth = newPreviewWidth,
            OldSubtableDisplayCellHeight = subtableColumn.SubtableDisplayCellHeight,
            NewSubtableDisplayCellHeight = newPreviewHeight,
            OldSubtableDisplayPluginSettingsJson = oldPluginSettingsJson,
            NewSubtableDisplayPluginSettingsJson = newPluginSettingsJson,
            OldSubtableDisplayPreviewQuality = oldPreviewQuality,
            NewSubtableDisplayPreviewQuality = newPreviewQuality,
        });

        _showEditSubtableDisplayDialog = false;
        _editSubtableDisplayValidationMessage = "";
    }

    private static void DrawRelationDisplayColumnMenu(DocWorkspace workspace, DocTable sourceTable, DocColumn relationColumn)
    {
        if (!ImContextMenu.BeginMenu("Display column"))
        {
            return;
        }

        if (!TryResolveRelationTargetTable(workspace, sourceTable, relationColumn, out DocTable relationTable))
        {
            _ = ImContextMenu.Item("(set relation target first)");
            ImContextMenu.EndMenu();
            return;
        }

        if (ImContextMenu.Item("(auto)"))
        {
            if (!string.IsNullOrWhiteSpace(relationColumn.RelationDisplayColumnId))
            {
                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.SetColumnRelation,
                    TableId = sourceTable.Id,
                    ColumnId = relationColumn.Id,
                    OldRelationTableId = relationColumn.RelationTableId,
                    NewRelationTableId = relationColumn.RelationTableId,
                    OldRelationTargetMode = relationColumn.RelationTargetMode,
                    NewRelationTargetMode = relationColumn.RelationTargetMode,
                    OldRelationTableVariantId = relationColumn.RelationTableVariantId,
                    NewRelationTableVariantId = relationColumn.RelationTableVariantId,
                    OldRelationDisplayColumnId = relationColumn.RelationDisplayColumnId,
                    NewRelationDisplayColumnId = null,
                });
            }
        }

        var candidates = BuildRelationDisplayColumnChoices(relationTable);
        for (int columnIndex = 0; columnIndex < candidates.Count; columnIndex++)
        {
            var displayColumn = candidates[columnIndex];
            bool selected = string.Equals(relationColumn.RelationDisplayColumnId, displayColumn.Id, StringComparison.Ordinal);
            string label = selected ? "• " + displayColumn.Name : displayColumn.Name;
            if (ImContextMenu.Item(label))
            {
                if (!string.Equals(relationColumn.RelationDisplayColumnId, displayColumn.Id, StringComparison.Ordinal))
                {
                    workspace.ExecuteCommand(new DocCommand
                    {
                        Kind = DocCommandKind.SetColumnRelation,
                        TableId = sourceTable.Id,
                        ColumnId = relationColumn.Id,
                        OldRelationTableId = relationColumn.RelationTableId,
                        NewRelationTableId = relationColumn.RelationTableId,
                        OldRelationTargetMode = relationColumn.RelationTargetMode,
                        NewRelationTargetMode = relationColumn.RelationTargetMode,
                        OldRelationTableVariantId = relationColumn.RelationTableVariantId,
                        NewRelationTableVariantId = relationColumn.RelationTableVariantId,
                        OldRelationDisplayColumnId = relationColumn.RelationDisplayColumnId,
                        NewRelationDisplayColumnId = displayColumn.Id,
                    });
                }
            }
        }

        ImContextMenu.EndMenu();
    }

    private static List<DocColumn> BuildRelationDisplayColumnChoices(DocTable relationTable)
    {
        var candidates = new List<DocColumn>(relationTable.Columns.Count);
        for (int columnIndex = 0; columnIndex < relationTable.Columns.Count; columnIndex++)
        {
            var candidateColumn = relationTable.Columns[columnIndex];
            if (!IsRelationDisplayColumnCandidate(candidateColumn))
            {
                continue;
            }

            candidates.Add(candidateColumn);
        }

        return candidates;
    }

    private static bool IsRelationDisplayColumnCandidate(DocColumn column)
    {
        return column.Kind == DocColumnKind.Id ||
               column.Kind == DocColumnKind.Text ||
               column.Kind == DocColumnKind.Select ||
               column.Kind == DocColumnKind.Number ||
               column.Kind == DocColumnKind.Formula;
    }

    private static int ResolveRelationDisplayColumnChoiceIndex(
        IReadOnlyList<DocColumn> choices,
        string? relationDisplayColumnId)
    {
        if (string.IsNullOrWhiteSpace(relationDisplayColumnId))
        {
            return 0;
        }

        for (int index = 0; index < choices.Count; index++)
        {
            if (string.Equals(choices[index].Id, relationDisplayColumnId, StringComparison.Ordinal))
            {
                return index + 1;
            }
        }

        return 0;
    }

    private static void OpenEditSelectColumnDialog(DocTable table, DocColumn column)
    {
        _showEditSelectColumnDialog = true;
        _editSelectColumnDialogOpenedFrame = Im.Context.FrameCount;
        _editSelectTableId = table.Id;
        _editSelectColumnId = column.Id;
        _editSelectSelectedIndex = -1;
        _editSelectDragIndex = -1;
        _editSelectDragTargetIndex = -1;
        _editSelectDragMouseOffsetY = 0f;
        _editSelectScrollY = 0f;
        _editSelectInlineRenameNeedsFocus = false;
        _editSelectRenameBufferLength = 0;
        _editSelectAddBufferLength = 0;
        _editSelectEntries.Clear();
        _editSelectOriginalValuesById.Clear();
        _editSelectNextEntryId = 1;

        if (column.Options == null)
        {
            return;
        }

        for (int optionIndex = 0; optionIndex < column.Options.Count; optionIndex++)
        {
            string optionValue = column.Options[optionIndex] ?? "";
            var entry = new SelectOptionEditEntry
            {
                EntryId = _editSelectNextEntryId++,
                OriginalValue = optionValue,
                Value = optionValue,
                IsNew = false,
            };
            _editSelectEntries.Add(entry);
            _editSelectOriginalValuesById[entry.EntryId] = optionValue;
        }

        if (_editSelectEntries.Count > 0)
        {
            _editSelectSelectedIndex = 0;
            SyncEditSelectRenameBufferFromSelection();
        }
    }

    private static void OpenEditMeshPreviewDialog(DocTable table, DocRow row, DocColumn column)
    {
        if (column.Kind != DocColumnKind.MeshAsset)
        {
            return;
        }

        var cell = row.GetCell(column);
        _showEditMeshPreviewDialog = true;
        _editMeshPreviewTableId = table.Id;
        _editMeshPreviewRowId = row.Id;
        _editMeshPreviewColumnId = column.Id;
        _editMeshPreviewDraft = (cell.ModelPreviewSettings ?? column.ModelPreviewSettings ?? new DocModelPreviewSettings()).Clone();
        _editMeshPreviewDraft.ClampInPlace();
        SyncEditMeshPreviewTexturePathBufferFromDraft();
        ResetEditMeshPreviewViewportPreviewState();
    }

    private static bool TryFindEditMeshPreviewCell(
        DocWorkspace workspace,
        out DocTable sourceTable,
        out DocRow sourceRow,
        out DocColumn meshColumn,
        out DocCellValue meshCell)
    {
        sourceTable = null!;
        sourceRow = null!;
        meshColumn = null!;
        meshCell = default;

        for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
        {
            var table = workspace.Project.Tables[tableIndex];
            if (!string.Equals(table.Id, _editMeshPreviewTableId, StringComparison.Ordinal))
            {
                continue;
            }

            DocRow? row = null;
            for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                if (!string.Equals(table.Rows[rowIndex].Id, _editMeshPreviewRowId, StringComparison.Ordinal))
                {
                    continue;
                }

                row = table.Rows[rowIndex];
                break;
            }

            if (row == null)
            {
                continue;
            }

            for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                var column = table.Columns[columnIndex];
                if (!string.Equals(column.Id, _editMeshPreviewColumnId, StringComparison.Ordinal))
                {
                    continue;
                }

                sourceTable = table;
                sourceRow = row;
                meshColumn = column;
                meshCell = row.GetCell(column);
                return true;
            }
        }

        return false;
    }

    private static void SyncEditMeshPreviewTexturePathBufferFromDraft()
    {
        Array.Clear(_editMeshPreviewTexturePathBuffer);
        string texturePath = _editMeshPreviewDraft.TextureRelativePath ?? "";
        _editMeshPreviewTexturePathBufferLength = Math.Min(texturePath.Length, _editMeshPreviewTexturePathBuffer.Length);
        texturePath.AsSpan(0, _editMeshPreviewTexturePathBufferLength).CopyTo(_editMeshPreviewTexturePathBuffer);
    }

    private static void UpdateEditMeshPreviewTexturePathFromBuffer()
    {
        if (_editMeshPreviewTexturePathBufferLength <= 0)
        {
            _editMeshPreviewDraft.TextureRelativePath = null;
            return;
        }

        _editMeshPreviewDraft.TextureRelativePath = new string(
            _editMeshPreviewTexturePathBuffer,
            0,
            _editMeshPreviewTexturePathBufferLength);
    }

    private static void OnEditMeshPreviewTextureSelected(string selectedRelativePath)
    {
        if (!_showEditMeshPreviewDialog)
        {
            return;
        }

        _editMeshPreviewDraft.TextureRelativePath = string.IsNullOrWhiteSpace(selectedRelativePath)
            ? null
            : selectedRelativePath;
        _editMeshPreviewDraft.ClampInPlace();
        SyncEditMeshPreviewTexturePathBufferFromDraft();
        ResetEditMeshPreviewViewportPreviewState();
    }

    private static void ResetEditMeshPreviewViewportPreviewState()
    {
        EndEditMeshPreviewOrbitDrag();
        EndEditMeshPreviewPanDrag();

        _editMeshPreviewViewportHasRequest = false;
        _editMeshPreviewViewportAssetsRoot = "";
        _editMeshPreviewViewportRelativePath = "";
        _editMeshPreviewViewportOrbitYawDegrees = 0f;
        _editMeshPreviewViewportOrbitPitchDegrees = 0f;
        _editMeshPreviewViewportPanX = 0f;
        _editMeshPreviewViewportPanY = 0f;
        _editMeshPreviewViewportZoom = 0f;
        _editMeshPreviewViewportTextureRelativePath = null;
        _editMeshPreviewViewportStatus = MeshPreviewGenerator.PreviewRenderStatus.None;
        _editMeshPreviewViewportRetryFrame = 0;
        _editMeshPreviewOrbitCursorLocked = false;
        _editMeshPreviewPanCursorLocked = false;
    }

    private static void CloseEditMeshPreviewDialog()
    {
        _showEditMeshPreviewDialog = false;
        _editMeshPreviewTableId = "";
        _editMeshPreviewRowId = "";
        _editMeshPreviewColumnId = "";
        ResetEditMeshPreviewViewportPreviewState();
    }

    private static void EndEditMeshPreviewOrbitDrag()
    {
        if (_editMeshPreviewOrbitCursorLocked)
        {
            ImMouseDragLock.End(MeshPreviewOrbitDragLockOwnerId);
            _editMeshPreviewOrbitCursorLocked = false;
        }

        _editMeshPreviewOrbitDragging = false;
    }

    private static void EndEditMeshPreviewPanDrag()
    {
        if (_editMeshPreviewPanCursorLocked)
        {
            ImMouseDragLock.End(MeshPreviewPanDragLockOwnerId);
            _editMeshPreviewPanCursorLocked = false;
        }

        _editMeshPreviewPanDragging = false;
    }

    private static void DrawEditMeshPreviewDialog(DocWorkspace workspace)
    {
        if (!TryFindEditMeshPreviewCell(workspace, out var sourceTable, out var sourceRow, out var meshColumn, out var meshCell))
        {
            CloseEditMeshPreviewDialog();
            return;
        }

        if (meshColumn.Kind != DocColumnKind.MeshAsset)
        {
            CloseEditMeshPreviewDialog();
            return;
        }

        _editMeshPreviewDraft.ClampInPlace();

        float dialogW = 760f;
        float dialogH = 430f;
        float dialogX = _gridRect.X + (_gridRect.Width - dialogW) * 0.5f;
        float dialogY = _gridRect.Y + 30f;
        var dialogRect = new ImRect(dialogX, dialogY, dialogW, dialogH);
        Im.Context.AddOverlayCaptureRect(Im.TransformRectLocalToViewportAabb(dialogRect));
        Im.Context.PushOverlayScope();
        try
        {
            var input = Im.Context.Input;
            if ((input.MousePressed || input.MouseRightPressed) &&
                !dialogRect.Contains(Im.MousePos) &&
                !ImModal.IsAnyOpen)
            {
                CloseEditMeshPreviewDialog();
                if (input.MousePressed)
                {
                    Im.Context.ConsumeMouseLeftPress();
                }

                if (input.MouseRightPressed)
                {
                    Im.Context.ConsumeMouseRightPress();
                }

                return;
            }

            Im.DrawRoundedRect(dialogX, dialogY, dialogW, dialogH, 5f, Im.Style.Surface);
            Im.DrawRoundedRectStroke(dialogX, dialogY, dialogW, dialogH, 5f, Im.Style.Border, 1f);

            float px = dialogX + 12f;
            float py = dialogY + 10f;
            Im.Text(("Edit model metadata: " + meshColumn.Name).AsSpan(), px, py, Im.Style.FontSize, Im.Style.TextPrimary);
            py += Im.Style.FontSize + 2f;
            Im.Text(("Row: " + sourceRow.Id).AsSpan(), px, py, Im.Style.FontSize - 1f, Im.Style.TextSecondary);
            py += Im.Style.FontSize + 8f;

            float previewWidth = 360f;
            float previewHeight = 300f;
            var previewRect = new ImRect(px, py, previewWidth, previewHeight);
            string sampleRelativePath = meshCell.StringValue ?? "";
            DrawMeshPreviewEditorViewport(workspace, sampleRelativePath, previewRect);

            float controlsX = previewRect.Right + 14f;
            float controlsWidth = dialogX + dialogW - controlsX - 12f;
            float controlY = py;

            Im.Text("Camera".AsSpan(), controlsX, controlY, Im.Style.FontSize, Im.Style.TextPrimary);
            controlY += Im.Style.FontSize + 6f;

            float orbitYaw = _editMeshPreviewDraft.OrbitYawDegrees;
            if (Im.Slider("mesh_preview_yaw", ref orbitYaw, -180f, 180f, controlsX, controlY, controlsWidth))
            {
                _editMeshPreviewDraft.OrbitYawDegrees = orbitYaw;
                _editMeshPreviewDraft.ClampInPlace();
            }

            controlY += 30f;
            float orbitPitch = _editMeshPreviewDraft.OrbitPitchDegrees;
            if (Im.Slider("mesh_preview_pitch", ref orbitPitch, -89f, 89f, controlsX, controlY, controlsWidth))
            {
                _editMeshPreviewDraft.OrbitPitchDegrees = orbitPitch;
                _editMeshPreviewDraft.ClampInPlace();
            }

            controlY += 30f;
            float panX = _editMeshPreviewDraft.PanX;
            if (Im.Slider("mesh_preview_pan_x", ref panX, DocModelPreviewSettings.MinPan, DocModelPreviewSettings.MaxPan, controlsX, controlY, controlsWidth))
            {
                _editMeshPreviewDraft.PanX = panX;
                _editMeshPreviewDraft.ClampInPlace();
            }

            controlY += 30f;
            float panY = _editMeshPreviewDraft.PanY;
            if (Im.Slider("mesh_preview_pan_y", ref panY, DocModelPreviewSettings.MinPan, DocModelPreviewSettings.MaxPan, controlsX, controlY, controlsWidth))
            {
                _editMeshPreviewDraft.PanY = panY;
                _editMeshPreviewDraft.ClampInPlace();
            }

            controlY += 30f;
            float zoom = _editMeshPreviewDraft.Zoom;
            if (Im.Slider("mesh_preview_zoom", ref zoom, DocModelPreviewSettings.MinZoom, DocModelPreviewSettings.MaxZoom, controlsX, controlY, controlsWidth))
            {
                _editMeshPreviewDraft.Zoom = zoom;
                _editMeshPreviewDraft.ClampInPlace();
            }

            controlY += 32f;
            if (Im.Button("Reset Camera", controlsX, controlY, controlsWidth, Im.Style.MinButtonHeight))
            {
                _editMeshPreviewDraft.OrbitYawDegrees = DocModelPreviewSettings.DefaultOrbitYawDegrees;
                _editMeshPreviewDraft.OrbitPitchDegrees = DocModelPreviewSettings.DefaultOrbitPitchDegrees;
                _editMeshPreviewDraft.PanX = DocModelPreviewSettings.DefaultPanX;
                _editMeshPreviewDraft.PanY = DocModelPreviewSettings.DefaultPanY;
                _editMeshPreviewDraft.Zoom = DocModelPreviewSettings.DefaultZoom;
                _editMeshPreviewDraft.ClampInPlace();
            }

            controlY += Im.Style.MinButtonHeight + 8f;
            Im.Text("Texture override".AsSpan(), controlsX, controlY, Im.Style.FontSize, Im.Style.TextPrimary);
            controlY += Im.Style.FontSize + 6f;

            float browseButtonWidth = 76f;
            float clearButtonWidth = 60f;
            float inputWidth = MathF.Max(80f, controlsWidth - browseButtonWidth - clearButtonWidth - 8f);
            if (Im.TextInput(
                    "mesh_preview_texture",
                    _editMeshPreviewTexturePathBuffer,
                    ref _editMeshPreviewTexturePathBufferLength,
                    _editMeshPreviewTexturePathBuffer.Length,
                    controlsX,
                    controlY,
                    inputWidth))
            {
                UpdateEditMeshPreviewTexturePathFromBuffer();
                _editMeshPreviewDraft.ClampInPlace();
            }

            bool canBrowseTexture = !string.IsNullOrWhiteSpace(workspace.AssetsRoot) && Directory.Exists(workspace.AssetsRoot);
            if (canBrowseTexture &&
                Im.Button("Browse", controlsX + inputWidth + 4f, controlY, browseButtonWidth, Im.Style.MinButtonHeight))
            {
                AssetBrowserModal.OpenPicker(
                    workspace.AssetsRoot!,
                    DocColumnKind.TextureAsset,
                    _editMeshPreviewDraft.TextureRelativePath ?? "",
                    OnEditMeshPreviewTextureSelected);
            }

            if (Im.Button(
                    "Clear",
                    controlsX + inputWidth + 4f + browseButtonWidth + 4f,
                    controlY,
                    clearButtonWidth,
                    Im.Style.MinButtonHeight))
            {
                _editMeshPreviewDraft.TextureRelativePath = null;
                SyncEditMeshPreviewTexturePathBufferFromDraft();
            }

            float helpY = previewRect.Bottom + 6f;
            Im.Text(
                "Left drag: orbit  Right/Middle drag: pan  Wheel: zoom".AsSpan(),
                previewRect.X,
                helpY,
                Im.Style.FontSize - 1f,
                Im.Style.TextSecondary);

            float buttonY = dialogY + dialogH - Im.Style.MinButtonHeight - 10f;
            float cancelX = dialogX + dialogW - 84f;
            float applyX = cancelX - 84f - 8f;
            if (Im.Button("Apply", applyX, buttonY, 84f, Im.Style.MinButtonHeight))
            {
                var normalizedNewSettings = BuildPersistedModelPreviewSettings(_editMeshPreviewDraft);
                var effectiveOldSettings = BuildPersistedModelPreviewSettings(meshCell.ModelPreviewSettings ?? meshColumn.ModelPreviewSettings ?? new DocModelPreviewSettings());
                if (!AreModelPreviewSettingsEquivalent(effectiveOldSettings, normalizedNewSettings))
                {
                    DocModelPreviewSettings? storedCellSettings = normalizedNewSettings;
                    if (AreModelPreviewSettingsEquivalent(meshColumn.ModelPreviewSettings, normalizedNewSettings))
                    {
                        storedCellSettings = null;
                    }

                    var newCell = meshCell.Clone();
                    newCell.ModelPreviewSettings = storedCellSettings?.Clone();
                    workspace.ExecuteCommand(new DocCommand
                    {
                        Kind = DocCommandKind.SetCell,
                        TableId = sourceTable.Id,
                        RowId = sourceRow.Id,
                        ColumnId = meshColumn.Id,
                        OldCellValue = meshCell.Clone(),
                        NewCellValue = newCell,
                    });
                }

                CloseEditMeshPreviewDialog();
            }

            if (Im.Button("Cancel", cancelX, buttonY, 84f, Im.Style.MinButtonHeight))
            {
                CloseEditMeshPreviewDialog();
            }
        }
        finally
        {
            Im.Context.PopOverlayScope();
        }
    }

    private static void DrawMeshPreviewEditorViewport(
        DocWorkspace workspace,
        string sampleRelativePath,
        ImRect previewRect)
    {
        Im.DrawRoundedRect(previewRect.X, previewRect.Y, previewRect.Width, previewRect.Height, 4f, BlendColor(Im.Style.Background, 0.18f, Im.Style.Surface));
        Im.DrawRoundedRectStroke(previewRect.X, previewRect.Y, previewRect.Width, previewRect.Height, 4f, Im.Style.Border, 1f);

        string? assetsRoot = workspace.AssetsRoot;
        if (string.IsNullOrWhiteSpace(assetsRoot) || !Directory.Exists(assetsRoot))
        {
            DrawCenteredOverlayText(previewRect, "(assets root unavailable)".AsSpan(), Im.Style.TextSecondary);
            return;
        }

        if (string.IsNullOrWhiteSpace(sampleRelativePath))
        {
            DrawCenteredOverlayText(previewRect, "(set a model value in this cell first)".AsSpan(), Im.Style.TextSecondary);
            return;
        }

        bool hasLiveTexture = TryRefreshMeshPreviewViewportTexture(assetsRoot, sampleRelativePath);
        Im.PushClipRect(previewRect);
        if (hasLiveTexture)
        {
            var texture = _editMeshPreviewViewportTexture;
            if (texture.Width > 0 && texture.Height > 0)
            {
                float scale = MathF.Min(previewRect.Width / texture.Width, previewRect.Height / texture.Height);
                float drawWidth = texture.Width * scale;
                float drawHeight = texture.Height * scale;
                float drawX = previewRect.X + (previewRect.Width - drawWidth) * 0.5f;
                float drawY = previewRect.Y + (previewRect.Height - drawHeight) * 0.5f;
                Im.DrawImage(texture, drawX, drawY, drawWidth, drawHeight);
            }
        }
        else
        {
            ReadOnlySpan<char> statusText = GetMeshPreviewViewportStatusText(_editMeshPreviewViewportStatus);
            DrawCenteredOverlayText(previewRect, statusText, Im.Style.TextSecondary);
        }

        Im.PopClipRect();

        var input = Im.Context.Input;
        bool hoverPreview = previewRect.Contains(Im.MousePos);

        if (!input.MouseDown)
        {
            EndEditMeshPreviewOrbitDrag();
        }

        if (!input.MouseRightDown && !input.MouseMiddleDown)
        {
            EndEditMeshPreviewPanDrag();
        }

        bool isDragActive = _editMeshPreviewOrbitDragging || _editMeshPreviewPanDragging;
        if (!hoverPreview && !isDragActive)
        {
            return;
        }

        bool updated = false;
        if (hoverPreview && input.ScrollDelta != 0f)
        {
            float zoomScale = input.ScrollDelta > 0f ? 1.1f : 0.9f;
            _editMeshPreviewDraft.Zoom *= zoomScale;
            updated = true;
            Im.Context.ConsumeScroll();
        }

        if (hoverPreview && input.MousePressed)
        {
            EndEditMeshPreviewPanDrag();
            _editMeshPreviewOrbitDragging = true;
            _editMeshPreviewOrbitCursorLocked = ImMouseDragLock.Begin(MeshPreviewOrbitDragLockOwnerId);
            Im.Context.ConsumeMouseLeftPress();
        }
        else if (hoverPreview && (input.MouseRightPressed || input.MouseMiddlePressed))
        {
            EndEditMeshPreviewOrbitDrag();
            _editMeshPreviewPanDragging = true;
            _editMeshPreviewPanCursorLocked = ImMouseDragLock.Begin(MeshPreviewPanDragLockOwnerId);
            if (input.MouseRightPressed)
            {
                Im.Context.ConsumeMouseRightPress();
            }
        }

        if (_editMeshPreviewOrbitDragging)
        {
            if (!input.MouseDown)
            {
                EndEditMeshPreviewOrbitDrag();
            }
            else
            {
                Vector2 mouseDelta = _editMeshPreviewOrbitCursorLocked
                    ? ImMouseDragLock.ConsumeDelta(MeshPreviewOrbitDragLockOwnerId)
                    : Im.Context.Input.MouseDelta;
                if (mouseDelta.X != 0f || mouseDelta.Y != 0f)
                {
                    _editMeshPreviewDraft.OrbitYawDegrees += mouseDelta.X * 0.45f;
                    _editMeshPreviewDraft.OrbitPitchDegrees -= mouseDelta.Y * 0.45f;
                    updated = true;
                }
            }
        }

        if (_editMeshPreviewPanDragging)
        {
            if (!input.MouseRightDown && !input.MouseMiddleDown)
            {
                EndEditMeshPreviewPanDrag();
            }
            else
            {
                Vector2 mouseDelta = _editMeshPreviewPanCursorLocked
                    ? ImMouseDragLock.ConsumeDelta(MeshPreviewPanDragLockOwnerId)
                    : Im.Context.Input.MouseDelta;
                if (mouseDelta.X != 0f || mouseDelta.Y != 0f)
                {
                    _editMeshPreviewDraft.PanX += mouseDelta.X / MathF.Max(1f, previewRect.Width);
                    _editMeshPreviewDraft.PanY -= mouseDelta.Y / MathF.Max(1f, previewRect.Height);
                    updated = true;
                }
            }
        }

        if (updated)
        {
            _editMeshPreviewDraft.ClampInPlace();
        }
    }

    private static bool TryRefreshMeshPreviewViewportTexture(string assetsRoot, string sampleRelativePath)
    {
        bool requestChanged = HasMeshPreviewViewportRequestChanged(assetsRoot, sampleRelativePath, _editMeshPreviewDraft);
        if (requestChanged)
        {
            _editMeshPreviewViewportHasRequest = true;
            _editMeshPreviewViewportAssetsRoot = assetsRoot;
            _editMeshPreviewViewportRelativePath = sampleRelativePath;
            _editMeshPreviewViewportOrbitYawDegrees = _editMeshPreviewDraft.OrbitYawDegrees;
            _editMeshPreviewViewportOrbitPitchDegrees = _editMeshPreviewDraft.OrbitPitchDegrees;
            _editMeshPreviewViewportPanX = _editMeshPreviewDraft.PanX;
            _editMeshPreviewViewportPanY = _editMeshPreviewDraft.PanY;
            _editMeshPreviewViewportZoom = _editMeshPreviewDraft.Zoom;
            _editMeshPreviewViewportTextureRelativePath = _editMeshPreviewDraft.TextureRelativePath;
        }

        bool shouldRetryFailure =
            _editMeshPreviewViewportStatus != MeshPreviewGenerator.PreviewRenderStatus.Ready &&
            Im.Context.FrameCount >= _editMeshPreviewViewportRetryFrame;
        if (!requestChanged && !shouldRetryFailure)
        {
            return _hasEditMeshPreviewViewportTexture &&
                   _editMeshPreviewViewportStatus == MeshPreviewGenerator.PreviewRenderStatus.Ready;
        }

        MeshPreviewGenerator.PreviewRenderStatus renderStatus = _editMeshPreviewGenerator.TryRenderPreviewPixels(
            assetsRoot,
            sampleRelativePath,
            _editMeshPreviewDraft,
            MeshPreviewViewportTextureSize,
            MeshPreviewViewportTextureSize,
            out byte[] previewPixels);
        if (renderStatus == MeshPreviewGenerator.PreviewRenderStatus.Ready)
        {
            try
            {
                if (!_hasEditMeshPreviewViewportTexture)
                {
                    _editMeshPreviewViewportTexture = DerpEngine.LoadTexture(
                        previewPixels,
                        MeshPreviewViewportTextureSize,
                        MeshPreviewViewportTextureSize);
                    _hasEditMeshPreviewViewportTexture = true;
                }
                else
                {
                    _editMeshPreviewViewportTexture = DerpEngine.UpdateTexture(
                        _editMeshPreviewViewportTexture,
                        previewPixels,
                        MeshPreviewViewportTextureSize,
                        MeshPreviewViewportTextureSize);
                }
            }
            catch
            {
                renderStatus = MeshPreviewGenerator.PreviewRenderStatus.Failed;
            }
        }

        _editMeshPreviewViewportStatus = renderStatus;
        _editMeshPreviewViewportRetryFrame = renderStatus switch
        {
            MeshPreviewGenerator.PreviewRenderStatus.Loading => Im.Context.FrameCount + MeshPreviewRetryFramesLoading,
            MeshPreviewGenerator.PreviewRenderStatus.Ready => 0,
            _ => Im.Context.FrameCount + MeshPreviewRetryFramesFailure,
        };

        return _hasEditMeshPreviewViewportTexture &&
               _editMeshPreviewViewportStatus == MeshPreviewGenerator.PreviewRenderStatus.Ready;
    }

    private static bool HasMeshPreviewViewportRequestChanged(
        string assetsRoot,
        string sampleRelativePath,
        DocModelPreviewSettings settings)
    {
        if (!_editMeshPreviewViewportHasRequest)
        {
            return true;
        }

        if (!string.Equals(_editMeshPreviewViewportAssetsRoot, assetsRoot, StringComparison.Ordinal) ||
            !string.Equals(_editMeshPreviewViewportRelativePath, sampleRelativePath, StringComparison.Ordinal) ||
            !string.Equals(_editMeshPreviewViewportTextureRelativePath, settings.TextureRelativePath, StringComparison.Ordinal))
        {
            return true;
        }

        return _editMeshPreviewViewportOrbitYawDegrees != settings.OrbitYawDegrees ||
               _editMeshPreviewViewportOrbitPitchDegrees != settings.OrbitPitchDegrees ||
               _editMeshPreviewViewportPanX != settings.PanX ||
               _editMeshPreviewViewportPanY != settings.PanY ||
               _editMeshPreviewViewportZoom != settings.Zoom;
    }

    private static ReadOnlySpan<char> GetMeshPreviewViewportStatusText(MeshPreviewGenerator.PreviewRenderStatus status)
    {
        return status switch
        {
            MeshPreviewGenerator.PreviewRenderStatus.Loading => "(compiling mesh...)".AsSpan(),
            MeshPreviewGenerator.PreviewRenderStatus.Missing => "(missing source/compiled mesh)".AsSpan(),
            MeshPreviewGenerator.PreviewRenderStatus.PreviewUnavailable => "(no preview)".AsSpan(),
            MeshPreviewGenerator.PreviewRenderStatus.InvalidPath => "(invalid path)".AsSpan(),
            MeshPreviewGenerator.PreviewRenderStatus.None => "...".AsSpan(),
            _ => "(failed)".AsSpan(),
        };
    }

    private static void DrawCenteredOverlayText(ImRect rect, ReadOnlySpan<char> text, uint color)
    {
        float textWidth = Im.MeasureTextWidth(text, Im.Style.FontSize);
        float textX = rect.X + (rect.Width - textWidth) * 0.5f;
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(text, textX, textY, Im.Style.FontSize, color);
    }

    private static DocModelPreviewSettings? BuildPersistedModelPreviewSettings(DocModelPreviewSettings settings)
    {
        var normalized = settings.Clone();
        normalized.ClampInPlace();
        normalized.TextureRelativePath = NormalizeOptionalRelativeAssetPath(normalized.TextureRelativePath);

        if (DocModelPreviewSettings.IsDefault(normalized))
        {
            return null;
        }

        return normalized;
    }

    private static bool AreModelPreviewSettingsEquivalent(
        DocModelPreviewSettings? left,
        DocModelPreviewSettings? right)
    {
        var normalizedLeft = BuildPersistedModelPreviewSettings(left ?? new DocModelPreviewSettings());
        var normalizedRight = BuildPersistedModelPreviewSettings(right ?? new DocModelPreviewSettings());

        if (normalizedLeft == null && normalizedRight == null)
        {
            return true;
        }

        if (normalizedLeft == null || normalizedRight == null)
        {
            return false;
        }

        return string.Equals(
            DocModelPreviewSettings.BuildCacheSignature(normalizedLeft),
            DocModelPreviewSettings.BuildCacheSignature(normalizedRight),
            StringComparison.Ordinal);
    }

    private static string? NormalizeOptionalRelativeAssetPath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        if (Path.IsPathRooted(relativePath))
        {
            return null;
        }

        string normalized = relativePath.Trim().Replace('\\', '/');
        while (normalized.StartsWith("/", StringComparison.Ordinal))
        {
            normalized = normalized[1..];
        }

        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
        {
            if (segments[segmentIndex] == "." || segments[segmentIndex] == "..")
            {
                return null;
            }
        }

        return string.Join('/', segments);
    }

    private static bool TryFindEditSelectColumn(
        DocWorkspace workspace,
        out DocTable sourceTable,
        out DocColumn selectColumn)
    {
        sourceTable = null!;
        selectColumn = null!;

        for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
        {
            var table = workspace.Project.Tables[tableIndex];
            if (!string.Equals(table.Id, _editSelectTableId, StringComparison.Ordinal))
            {
                continue;
            }

            for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                var column = table.Columns[columnIndex];
                if (!string.Equals(column.Id, _editSelectColumnId, StringComparison.Ordinal))
                {
                    continue;
                }

                sourceTable = table;
                selectColumn = column;
                return true;
            }
        }

        return false;
    }

    private static void SyncEditSelectRenameBufferFromSelection()
    {
        Array.Clear(_editSelectRenameBuffer);
        _editSelectRenameBufferLength = 0;
        if (_editSelectSelectedIndex < 0 || _editSelectSelectedIndex >= _editSelectEntries.Count)
        {
            return;
        }

        string value = _editSelectEntries[_editSelectSelectedIndex].Value ?? "";
        _editSelectRenameBufferLength = Math.Min(value.Length, _editSelectRenameBuffer.Length);
        value.AsSpan(0, _editSelectRenameBufferLength).CopyTo(_editSelectRenameBuffer);
        _editSelectInlineRenameNeedsFocus = true;
    }

    private static bool TryValueExistsInEditSelectEntries(string value, int ignoreIndex = -1)
    {
        for (int optionIndex = 0; optionIndex < _editSelectEntries.Count; optionIndex++)
        {
            if (optionIndex == ignoreIndex)
            {
                continue;
            }

            if (string.Equals(_editSelectEntries[optionIndex].Value, value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void RemoveEditSelectOptionAt(int optionIndex)
    {
        if (optionIndex < 0 || optionIndex >= _editSelectEntries.Count)
        {
            return;
        }

        _editSelectEntries.RemoveAt(optionIndex);
        if (_editSelectEntries.Count == 0)
        {
            _editSelectSelectedIndex = -1;
            _editSelectRenameBufferLength = 0;
            _editSelectInlineRenameNeedsFocus = false;
            return;
        }

        if (_editSelectSelectedIndex == optionIndex)
        {
            _editSelectSelectedIndex = Math.Clamp(optionIndex, 0, _editSelectEntries.Count - 1);
            SyncEditSelectRenameBufferFromSelection();
            return;
        }

        if (optionIndex < _editSelectSelectedIndex)
        {
            _editSelectSelectedIndex--;
        }

        _editSelectSelectedIndex = Math.Clamp(_editSelectSelectedIndex, 0, _editSelectEntries.Count - 1);
        SyncEditSelectRenameBufferFromSelection();
    }

    private static void TryAddEditSelectOption()
    {
        string addedValue = new string(_editSelectAddBuffer, 0, _editSelectAddBufferLength).Trim();
        if (string.IsNullOrWhiteSpace(addedValue))
        {
            return;
        }

        if (TryValueExistsInEditSelectEntries(addedValue))
        {
            return;
        }

        _editSelectEntries.Add(new SelectOptionEditEntry
        {
            EntryId = _editSelectNextEntryId++,
            OriginalValue = "",
            Value = addedValue,
            IsNew = true,
        });
        _editSelectSelectedIndex = _editSelectEntries.Count - 1;
        _editSelectAddBufferLength = 0;
        SyncEditSelectRenameBufferFromSelection();
    }

    private static bool AreStringListsEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static void ApplyEditSelectColumnChanges(DocWorkspace workspace, DocTable sourceTable, DocColumn selectColumn)
    {
        var oldOptions = selectColumn.Options != null
            ? new List<string>(selectColumn.Options)
            : new List<string>();
        var newOptions = new List<string>(_editSelectEntries.Count);
        var seenOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int optionIndex = 0; optionIndex < _editSelectEntries.Count; optionIndex++)
        {
            string trimmedValue = (_editSelectEntries[optionIndex].Value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(trimmedValue))
            {
                continue;
            }

            if (seenOptions.Contains(trimmedValue))
            {
                continue;
            }

            seenOptions.Add(trimmedValue);
            newOptions.Add(trimmedValue);
        }

        var commands = new List<DocCommand>(sourceTable.Rows.Count + 1);
        if (!AreStringListsEqual(oldOptions, newOptions))
        {
            commands.Add(new DocCommand
            {
                Kind = DocCommandKind.SetColumnOptions,
                TableId = sourceTable.Id,
                ColumnId = selectColumn.Id,
                OldOptionsSnapshot = oldOptions,
                NewOptionsSnapshot = newOptions,
            });
        }

        var currentById = new Dictionary<int, SelectOptionEditEntry>(_editSelectEntries.Count);
        for (int optionIndex = 0; optionIndex < _editSelectEntries.Count; optionIndex++)
        {
            currentById[_editSelectEntries[optionIndex].EntryId] = _editSelectEntries[optionIndex];
        }

        var remap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var originalEntry in _editSelectOriginalValuesById)
        {
            string oldValue = originalEntry.Value ?? "";
            if (currentById.TryGetValue(originalEntry.Key, out var currentEntry))
            {
                string newValue = (currentEntry.Value ?? "").Trim();
                if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
                {
                    remap[oldValue] = newValue;
                }
            }
            else
            {
                remap[oldValue] = "";
            }
        }

        if (remap.Count > 0)
        {
            for (int rowIndex = 0; rowIndex < sourceTable.Rows.Count; rowIndex++)
            {
                var row = sourceTable.Rows[rowIndex];
                var oldCell = row.GetCell(selectColumn);
                string oldCellValue = oldCell.StringValue ?? "";
                if (!remap.TryGetValue(oldCellValue, out var remappedValue))
                {
                    continue;
                }

                if (string.Equals(oldCellValue, remappedValue, StringComparison.Ordinal))
                {
                    continue;
                }

                commands.Add(new DocCommand
                {
                    Kind = DocCommandKind.SetCell,
                    TableId = sourceTable.Id,
                    RowId = row.Id,
                    ColumnId = selectColumn.Id,
                    OldCellValue = oldCell,
                    NewCellValue = DocCellValue.Text(remappedValue)
                });
            }
        }

        if (commands.Count > 0)
        {
            workspace.ExecuteCommands(commands);
        }

        _showEditSelectColumnDialog = false;
    }

    // =====================================================================
    //  Context menus
    // =====================================================================

    private static void DrawContextMenus(DocWorkspace workspace, DocTable table)
    {
        // --- Row / cell context menu ---
        if (ImContextMenu.Begin("row_context_menu"))
        {
            bool hasRowSelection = _selectedRows.Count > 0;
            bool hasCellRange = _selStartRow >= 0 && _selEndRow >= 0 && _selStartCol >= 0 && _selEndCol >= 0
                && (_selStartRow != _selEndRow || _selStartCol != _selEndCol);

            if (!table.IsDerived)
            {
                if (ImContextMenu.Item("Insert row above"))
                {
                    int displayIdx = hasRowSelection ? GetMinSelectedRow() : _contextRowIndex;
                    InsertRow(workspace, table, GetSourceRowIndex(displayIdx));
                }
                if (ImContextMenu.Item("Insert row below"))
                {
                    int displayIdx = hasRowSelection ? GetMaxSelectedRow() + 1 : _contextRowIndex + 1;
                    int sourceIdx = displayIdx < _rowCount ? GetSourceRowIndex(displayIdx) : table.Rows.Count;
                    InsertRow(workspace, table, sourceIdx);
                }

                ImContextMenu.Separator();

                if (hasRowSelection && _selectedRows.Count > 1)
                {
                    if (ImContextMenu.Item($"Delete {_selectedRows.Count} rows"))
                    {
                        DeleteSelectedRows(workspace, table);
                    }
                }
                else
                {
                    if (ImContextMenu.Item("Delete row"))
                    {
                        int sourceRowIdx = GetSourceRowIndex(_contextRowIndex);
                        if (sourceRowIdx >= 0 && sourceRowIdx < table.Rows.Count)
                        {
                            workspace.CancelTableCellEditIfActive();
                            var rowToRemove = table.Rows[sourceRowIdx];
                            workspace.ExecuteCommand(new DocCommand
                            {
                                Kind = DocCommandKind.RemoveRow,
                                TableId = table.Id,
                                RowIndex = sourceRowIdx,
                                RowSnapshot = rowToRemove
                            });
                        }
                    }
                }
            }

            if (hasCellRange)
            {
                ImContextMenu.Separator();
                if (ImContextMenu.Item("Clear cells"))
                {
                    ClearSelectedCells(workspace, table);
                }
            }

            bool canEditMeshCellMetadata =
                _contextRowIndex >= 0 &&
                _contextRowIndex < _rowCount &&
                _contextColIndex >= 0 &&
                _contextColIndex < _colCount;
            if (canEditMeshCellMetadata)
            {
                var contextColumn = GetVisibleColumn(table, _contextColIndex);
                int sourceRowIndex = GetSourceRowIndex(_contextRowIndex);
                bool hasContextRow = sourceRowIndex >= 0 && sourceRowIndex < table.Rows.Count;
                DocRow? contextRow = hasContextRow ? table.Rows[sourceRowIndex] : null;
                if (!IsColumnDataReadOnly(contextColumn) &&
                    contextColumn.Kind == DocColumnKind.MeshAsset)
                {
                    if (hasContextRow)
                    {
                        ImContextMenu.Separator();
                        if (ImContextMenu.Item("Edit model metadata"))
                        {
                            OpenEditMeshPreviewDialog(table, contextRow!, contextColumn);
                        }
                    }
                }

                if (!table.IsDerived &&
                    !IsColumnDataReadOnly(contextColumn) &&
                    hasContextRow)
                {
                    DocCellValue contextCellValue = contextRow!.GetCell(contextColumn);
                    bool hasCellFormula = contextCellValue.HasCellFormulaExpression;
                    string editCellFormulaLabel = hasCellFormula ? "Edit cell formula" : "Set cell formula";
                    ImContextMenu.Separator();
                    if (ImContextMenu.Item(editCellFormulaLabel))
                    {
                        OpenEditCellFormulaDialog(table, contextRow, contextColumn);
                    }

                    if (hasCellFormula && ImContextMenu.Item("Clear cell formula"))
                    {
                        DocCellValue oldCellValue = contextCellValue;
                        DocCellValue newCellValue = oldCellValue.ClearCellFormulaExpression();
                        newCellValue.FormulaError = null;
                        workspace.ExecuteCommand(new DocCommand
                        {
                            Kind = DocCommandKind.SetCell,
                            TableId = table.Id,
                            RowId = contextRow.Id,
                            ColumnId = contextColumn.Id,
                            OldCellValue = oldCellValue,
                            NewCellValue = newCellValue,
                        });
                    }
                }

                if (hasContextRow &&
                    !IsColumnDataReadOnly(contextColumn) &&
                    TryGetColumnUiPlugin(contextColumn, out var cellUiPlugin))
                {
                    var contextCell = contextRow!.GetCell(contextColumn);
                    cellUiPlugin.DrawCellContextMenu(
                        workspace,
                        table,
                        contextRow!,
                        sourceRowIndex,
                        _contextRowIndex,
                        _contextColIndex,
                        contextColumn,
                        contextCell);
                }
            }

            ImContextMenu.End();
        }

        // --- Column context menu ---
        if (ImContextMenu.Begin("col_context_menu"))
        {
            if (_contextColIndex >= 0 && _contextColIndex < _colCount)
            {
                var contextColumn = GetVisibleColumn(table, _contextColIndex);
                bool canModifyContextColumn = !IsColumnSchemaLocked(contextColumn);

                if (canModifyContextColumn && ImContextMenu.Item("Rename column"))
                {
                    _selectedHeaderCol = _contextColIndex;
                    _activeCol = _contextColIndex;
                    _activeRow = -1;
                    StartInlineColumnRename(table, _contextColIndex, true);
                }

                if (canModifyContextColumn &&
                    (contextColumn.Kind == DocColumnKind.Select ||
                     contextColumn.Kind == DocColumnKind.Relation ||
                     contextColumn.Kind == DocColumnKind.Number ||
                     contextColumn.Kind == DocColumnKind.Subtable) &&
                    ImContextMenu.Item("Edit column"))
                {
                    if (contextColumn.Kind == DocColumnKind.Select)
                    {
                        OpenEditSelectColumnDialog(table, contextColumn);
                    }
                    else if (contextColumn.Kind == DocColumnKind.Relation)
                    {
                        OpenEditRelationDialog(workspace, table, contextColumn, _contextColIndex);
                    }
                    else if (contextColumn.Kind == DocColumnKind.Number)
                    {
                        OpenEditNumberColumnDialog(table, contextColumn);
                    }
                    else if (contextColumn.Kind == DocColumnKind.Subtable)
                    {
                        OpenEditSubtableDisplayDialog(table, contextColumn);
                    }
                }

                if (canModifyContextColumn && contextColumn.Kind == DocColumnKind.Relation)
                {
                    DrawRelationDisplayColumnMenu(workspace, table, contextColumn);
                }

                bool hasFormula = HasFormulaExpression(contextColumn);
                string formulaMenuLabel = hasFormula ? "Edit formula" : "Set formula";
                if (canModifyContextColumn && ImContextMenu.Item(formulaMenuLabel))
                {
                    _editFormulaColIndex = _contextColIndex;
                    _editFormulaTargetKind = EditFormulaTargetKind.Column;
                    _editFormulaCellRowId = "";
                    _editFormulaCellTableId = "";
                    string formulaExpression = contextColumn.FormulaExpression ?? "";
                    _editFormulaBufferLength = Math.Min(formulaExpression.Length, _editFormulaBuffer.Length);
                    formulaExpression.AsSpan(0, _editFormulaBufferLength).CopyTo(_editFormulaBuffer);
                    _formulaInspectorContextColumnId = "";
                    _showEditFormulaDialog = true;
                    _editFormulaDialogOpenedFrame = Im.Context.FrameCount;
                }

                if (canModifyContextColumn && hasFormula && ImContextMenu.Item("Clear formula"))
                {
                    workspace.ExecuteCommand(new DocCommand
                    {
                        Kind = DocCommandKind.SetColumnFormula,
                        TableId = table.Id,
                        ColumnId = contextColumn.Id,
                        OldFormulaExpression = contextColumn.FormulaExpression ?? "",
                        NewFormulaExpression = ""
                    });
                }

                if (canModifyContextColumn &&
                    TryGetColumnUiPlugin(contextColumn, out var columnUiPlugin))
                {
                    columnUiPlugin.DrawColumnContextMenu(
                        workspace,
                        table,
                        _contextColIndex,
                        contextColumn);
                }

                if (canModifyContextColumn)
                {
                    ImContextMenu.Separator();

                    bool isPrimaryKey = !string.IsNullOrEmpty(table.Keys.PrimaryKeyColumnId) &&
                                        string.Equals(table.Keys.PrimaryKeyColumnId, contextColumn.Id, StringComparison.Ordinal);
                    string pkLabel = isPrimaryKey ? "Unset primary key" : "Set as primary key";
                    if (ImContextMenu.Item(pkLabel))
                    {
                        var oldKeys = table.Keys.Clone();
                        var newKeys = table.Keys.Clone();
                        newKeys.PrimaryKeyColumnId = isPrimaryKey ? "" : contextColumn.Id;
                        workspace.ExecuteCommand(new DocCommand
                        {
                            Kind = DocCommandKind.SetTableKeys,
                            TableId = table.Id,
                            OldKeysSnapshot = oldKeys,
                            NewKeysSnapshot = newKeys,
                        });
                    }

                    int secondaryIndex = FindSecondaryKeyIndex(table, contextColumn.Id);
                    if (secondaryIndex < 0)
                    {
                        if (ImContextMenu.Item("Add secondary key (non-unique)"))
                        {
                            var oldKeys = table.Keys.Clone();
                            var newKeys = table.Keys.Clone();
                            newKeys.SecondaryKeys.Add(new DocSecondaryKey { ColumnId = contextColumn.Id, Unique = false });
                            workspace.ExecuteCommand(new DocCommand
                            {
                                Kind = DocCommandKind.SetTableKeys,
                                TableId = table.Id,
                                OldKeysSnapshot = oldKeys,
                                NewKeysSnapshot = newKeys,
                            });
                        }

                        if (ImContextMenu.Item("Add secondary key (unique)"))
                        {
                            var oldKeys = table.Keys.Clone();
                            var newKeys = table.Keys.Clone();
                            newKeys.SecondaryKeys.Add(new DocSecondaryKey { ColumnId = contextColumn.Id, Unique = true });
                            workspace.ExecuteCommand(new DocCommand
                            {
                                Kind = DocCommandKind.SetTableKeys,
                                TableId = table.Id,
                                OldKeysSnapshot = oldKeys,
                                NewKeysSnapshot = newKeys,
                            });
                        }
                    }
                    else if (secondaryIndex >= 0 && secondaryIndex < table.Keys.SecondaryKeys.Count)
                    {
                        bool isUnique = table.Keys.SecondaryKeys[secondaryIndex].Unique;
                        string uniqueLabel = isUnique ? "Set secondary key as non-unique" : "Set secondary key as unique";
                        if (ImContextMenu.Item(uniqueLabel))
                        {
                            var oldKeys = table.Keys.Clone();
                            var newKeys = table.Keys.Clone();
                            if (secondaryIndex < newKeys.SecondaryKeys.Count)
                            {
                                newKeys.SecondaryKeys[secondaryIndex].Unique = !isUnique;
                            }
                            workspace.ExecuteCommand(new DocCommand
                            {
                                Kind = DocCommandKind.SetTableKeys,
                                TableId = table.Id,
                                OldKeysSnapshot = oldKeys,
                                NewKeysSnapshot = newKeys,
                            });
                        }

                        if (ImContextMenu.Item("Remove secondary key"))
                        {
                            var oldKeys = table.Keys.Clone();
                            var newKeys = table.Keys.Clone();
                            if (secondaryIndex < newKeys.SecondaryKeys.Count)
                            {
                                newKeys.SecondaryKeys.RemoveAt(secondaryIndex);
                            }
                            workspace.ExecuteCommand(new DocCommand
                            {
                                Kind = DocCommandKind.SetTableKeys,
                                TableId = table.Id,
                                OldKeysSnapshot = oldKeys,
                                NewKeysSnapshot = newKeys,
                            });
                        }
                    }

                    ImContextMenu.Separator();

                    string exportIgnoreLabel = contextColumn.ExportIgnore ? "Include in export" : "Exclude from export";
                    if (ImContextMenu.Item(exportIgnoreLabel))
                    {
                        workspace.ExecuteCommand(new DocCommand
                        {
                            Kind = DocCommandKind.SetColumnExportIgnore,
                            TableId = table.Id,
                            ColumnId = contextColumn.Id,
                            OldExportIgnore = contextColumn.ExportIgnore,
                            NewExportIgnore = !contextColumn.ExportIgnore,
                        });
                    }

                    if (contextColumn.Kind == DocColumnKind.Number || contextColumn.Kind == DocColumnKind.Formula)
                    {
                        if (ImContextMenu.BeginMenu("Export type"))
                        {
                            if (ImContextMenu.Item("(default: Fixed64)"))
                            {
                                workspace.ExecuteCommand(new DocCommand
                                {
                                    Kind = DocCommandKind.SetColumnExportType,
                                    TableId = table.Id,
                                    ColumnId = contextColumn.Id,
                                    OldExportType = contextColumn.ExportType,
                                    NewExportType = null,
                                });
                            }
                            if (ImContextMenu.Item("int"))
                            {
                                workspace.ExecuteCommand(new DocCommand
                                {
                                    Kind = DocCommandKind.SetColumnExportType,
                                    TableId = table.Id,
                                    ColumnId = contextColumn.Id,
                                    OldExportType = contextColumn.ExportType,
                                    NewExportType = "int",
                                });
                            }
                            if (ImContextMenu.Item("float"))
                            {
                                workspace.ExecuteCommand(new DocCommand
                                {
                                    Kind = DocCommandKind.SetColumnExportType,
                                    TableId = table.Id,
                                    ColumnId = contextColumn.Id,
                                    OldExportType = contextColumn.ExportType,
                                    NewExportType = "float",
                                });
                            }
                            if (ImContextMenu.Item("Fixed64"))
                            {
                                workspace.ExecuteCommand(new DocCommand
                                {
                                    Kind = DocCommandKind.SetColumnExportType,
                                    TableId = table.Id,
                                    ColumnId = contextColumn.Id,
                                    OldExportType = contextColumn.ExportType,
                                    NewExportType = "Fixed64",
                                });
                            }
                            if (ImContextMenu.Item("Fixed32"))
                            {
                                workspace.ExecuteCommand(new DocCommand
                                {
                                    Kind = DocCommandKind.SetColumnExportType,
                                    TableId = table.Id,
                                    ColumnId = contextColumn.Id,
                                    OldExportType = contextColumn.ExportType,
                                    NewExportType = "Fixed32",
                                });
                            }
                            ImContextMenu.EndMenu();
                        }
                    }

                    ImContextMenu.Separator();

                    if (ImContextMenu.Item("Delete column"))
                    {
                        int tableColIndex = _visibleColMap[_contextColIndex];
                        var colToRemove = table.Columns[tableColIndex];
                        // Snapshot column cell values for undo
                        var cellSnapshots = new Dictionary<string, DocCellValue>();
                        foreach (var row in table.Rows)
                        {
                            cellSnapshots[row.Id] = row.GetCell(colToRemove);
                        }

                        if (TryGetOwnedSubtableTableToCascadeDelete(
                                workspace,
                                table,
                                colToRemove,
                                out int childTableIndex,
                                out DocTable? childTableSnapshot))
                        {
                            workspace.ExecuteCommands([
                                new DocCommand
                                {
                                    Kind = DocCommandKind.RemoveColumn,
                                    TableId = table.Id,
                                    ColumnIndex = tableColIndex,
                                    ColumnSnapshot = colToRemove,
                                    ColumnCellSnapshots = cellSnapshots,
                                },
                                new DocCommand
                                {
                                    Kind = DocCommandKind.RemoveTable,
                                    TableIndex = childTableIndex,
                                    TableSnapshot = childTableSnapshot,
                                }
                            ]);
                        }
                        else
                        {
                            workspace.ExecuteCommand(new DocCommand
                            {
                                Kind = DocCommandKind.RemoveColumn,
                                TableId = table.Id,
                                ColumnIndex = tableColIndex,
                                ColumnSnapshot = colToRemove,
                                ColumnCellSnapshots = cellSnapshots
                            });
                        }
                    }
                }
            }

            ImContextMenu.End();
        }

        // --- Empty area context menu ---
        if (ImContextMenu.Begin("empty_context_menu"))
        {
            if (!table.IsDerived && ImContextMenu.Item("Add row"))
            {
                InsertRow(workspace, table, table.Rows.Count);
            }
            if (CanAddColumns(table) && ImContextMenu.Item("Add column"))
            {
                OpenAddColumnDialog(workspace, table, 0);
            }
            ImContextMenu.End();
        }

        if (ImContextMenu.Begin("add_col_type_menu"))
        {
            if (!CanAddColumns(table))
            {
                ImContextMenu.End();
                return;
            }

            for (int kindIndex = 0; kindIndex < _columnKindNames.Length; kindIndex++)
            {
                if (ImContextMenu.Item(_columnKindNames[kindIndex]))
                {
                    OpenAddColumnDialog(workspace, table, kindIndex);
                }
            }

            ColumnTypeDefinitionRegistry.CopyDefinitions(_pluginColumnTypeDefinitionsScratch);
            bool hasCustomTypes = false;
            for (int definitionIndex = 0; definitionIndex < _pluginColumnTypeDefinitionsScratch.Count; definitionIndex++)
            {
                if (!DocColumnTypeIdMapper.IsBuiltIn(_pluginColumnTypeDefinitionsScratch[definitionIndex].ColumnTypeId))
                {
                    hasCustomTypes = true;
                    break;
                }
            }

            if (hasCustomTypes)
            {
                ImContextMenu.Separator();
                for (int definitionIndex = 0; definitionIndex < _pluginColumnTypeDefinitionsScratch.Count; definitionIndex++)
                {
                    var typeDefinition = _pluginColumnTypeDefinitionsScratch[definitionIndex];
                    if (DocColumnTypeIdMapper.IsBuiltIn(typeDefinition.ColumnTypeId))
                    {
                        continue;
                    }

                    if (ImContextMenu.Item(typeDefinition.DisplayName))
                    {
                        OpenAddPluginColumnDialog(workspace, table, typeDefinition);
                    }
                }
            }

            ImContextMenu.End();
        }
    }

    // =====================================================================
    //  Dialogs
    // =====================================================================

    private static void DrawDialogs(DocWorkspace workspace, DocTable table)
    {
        if (_showEditFormulaDialog)
            DrawEditFormulaDialog(workspace, table);

        if (_showEditRelationDialog)
            DrawEditRelationDialog(workspace, table);

        if (_showEditNumberColumnDialog)
            DrawEditNumberColumnDialog(workspace);

        if (_showAddSubtableColumnDialog)
            DrawAddSubtableColumnDialog(workspace);

        if (_showEditVectorCellDialog)
            DrawEditVectorCellDialog(workspace);

        if (_showEditSubtableDisplayDialog)
            DrawEditSubtableDisplayDialog(workspace);

        if (_showEditSelectColumnDialog)
            DrawEditSelectColumnDialog(workspace);

        if (_showEditMeshPreviewDialog)
            DrawEditMeshPreviewDialog(workspace);
    }

    private static bool TryGetOwnedSubtableTableToCascadeDelete(
        DocWorkspace workspace,
        DocTable parentTable,
        DocColumn subtableColumn,
        out int childTableIndex,
        out DocTable? childTableSnapshot)
    {
        childTableIndex = -1;
        childTableSnapshot = null;
        if (subtableColumn.Kind != DocColumnKind.Subtable ||
            string.IsNullOrWhiteSpace(subtableColumn.SubtableId))
        {
            return false;
        }

        for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
        {
            DocTable candidateTable = workspace.Project.Tables[tableIndex];
            if (!string.Equals(candidateTable.Id, subtableColumn.SubtableId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(candidateTable.ParentTableId, parentTable.Id, StringComparison.Ordinal))
            {
                return false;
            }

            if (IsSubtableTableReferencedByOtherColumns(
                    workspace,
                    candidateTable.Id,
                    parentTable.Id,
                    subtableColumn.Id))
            {
                return false;
            }

            childTableIndex = tableIndex;
            childTableSnapshot = candidateTable;
            return true;
        }

        return false;
    }

    private static bool IsSubtableTableReferencedByOtherColumns(
        DocWorkspace workspace,
        string childTableId,
        string ownerTableId,
        string ownerColumnId)
    {
        for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
        {
            DocTable candidateTable = workspace.Project.Tables[tableIndex];
            for (int columnIndex = 0; columnIndex < candidateTable.Columns.Count; columnIndex++)
            {
                DocColumn candidateColumn = candidateTable.Columns[columnIndex];
                if (candidateColumn.Kind != DocColumnKind.Subtable ||
                    string.IsNullOrWhiteSpace(candidateColumn.SubtableId))
                {
                    continue;
                }

                if (!string.Equals(candidateColumn.SubtableId, childTableId, StringComparison.Ordinal))
                {
                    continue;
                }

                bool isOwnerReference =
                    string.Equals(candidateTable.Id, ownerTableId, StringComparison.Ordinal) &&
                    string.Equals(candidateColumn.Id, ownerColumnId, StringComparison.Ordinal);
                if (!isOwnerReference)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void OpenAddColumnDialog(DocWorkspace workspace, DocTable table, int columnKindIndex)
    {
        var kind = GetNewColumnKindFromIndex(columnKindIndex);
        var newColumn = CreateNewColumnSnapshot(workspace, table, kind);
        if (newColumn == null)
        {
            return;
        }

        QueueAddColumnFromSnapshot(workspace, table, newColumn);
    }

    private static void OpenAddPluginColumnDialog(DocWorkspace workspace, DocTable table, ColumnTypeDefinition typeDefinition)
    {
        var newColumn = CreateNewColumnSnapshot(workspace, table, typeDefinition.FallbackKind);
        if (newColumn == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(typeDefinition.DisplayName))
        {
            newColumn.Name = GenerateDefaultCustomColumnName(table, typeDefinition.DisplayName.Trim());
        }

        newColumn.ColumnTypeId = typeDefinition.ColumnTypeId;
        QueueAddColumnFromSnapshot(workspace, table, newColumn);
    }

    private static void QueueAddColumnFromSnapshot(DocWorkspace workspace, DocTable table, DocColumn newColumn)
    {
        if (newColumn.Kind != DocColumnKind.Subtable)
        {
            AddColumnFromSnapshot(workspace, table, newColumn);
            return;
        }

        OpenAddSubtableColumnDialog(workspace, table, newColumn);
    }

    private static void OpenAddSubtableColumnDialog(DocWorkspace workspace, DocTable table, DocColumn subtableColumnSnapshot)
    {
        _showAddSubtableColumnDialog = true;
        _addSubtableColumnDialogOpenedFrame = Im.Context.FrameCount;
        _addSubtableColumnTableId = table.Id;
        _addSubtableColumnSnapshot = subtableColumnSnapshot;
        _addSubtableColumnModeIndex = 0;
        _addSubtableColumnValidationMessage = "";

        List<DocTable> existingSubtableChoices = BuildExistingSubtableChoices(workspace, table, subtableColumnSnapshot);
        if (existingSubtableChoices.Count > 0)
        {
            _addSubtableColumnExistingTableIndex = ResolveDefaultExistingSubtableChoiceIndex(table, existingSubtableChoices);
        }
        else
        {
            _addSubtableColumnExistingTableIndex = -1;
        }
    }

    private static void CloseAddSubtableColumnDialog()
    {
        _showAddSubtableColumnDialog = false;
        _addSubtableColumnDialogOpenedFrame = -1;
        _addSubtableColumnTableId = "";
        _addSubtableColumnSnapshot = null;
        _addSubtableColumnModeIndex = 0;
        _addSubtableColumnExistingTableIndex = -1;
        _addSubtableColumnValidationMessage = "";
    }

    private static void DrawAddSubtableColumnDialog(DocWorkspace workspace)
    {
        if (_addSubtableColumnSnapshot == null ||
            _addSubtableColumnSnapshot.Kind != DocColumnKind.Subtable ||
            string.IsNullOrWhiteSpace(_addSubtableColumnTableId))
        {
            CloseAddSubtableColumnDialog();
            return;
        }

        DocTable? sourceTable = FindTableById(workspace, _addSubtableColumnTableId);
        if (sourceTable == null || !CanAddColumns(sourceTable))
        {
            CloseAddSubtableColumnDialog();
            return;
        }

        List<DocTable> existingSubtableChoices = BuildExistingSubtableChoices(workspace, sourceTable, _addSubtableColumnSnapshot);
        bool canUseExistingTable = existingSubtableChoices.Count > 0;
        if (!canUseExistingTable && _addSubtableColumnModeIndex == 1)
        {
            _addSubtableColumnModeIndex = 0;
        }

        if (canUseExistingTable)
        {
            _addSubtableColumnExistingTableIndex = Math.Clamp(
                _addSubtableColumnExistingTableIndex,
                0,
                existingSubtableChoices.Count - 1);
        }
        else
        {
            _addSubtableColumnExistingTableIndex = -1;
        }

        float desiredDialogWidth = 440f;
        float desiredDialogHeight = _addSubtableColumnModeIndex == 1 ? 254f : 218f;
        float desiredDialogX = _gridRect.X + (_gridRect.Width - desiredDialogWidth) * 0.5f;
        float desiredDialogY = _gridRect.Y + 40f;
        ImRect dialogRect = GetClampedEditDialogRect(
            desiredDialogX,
            desiredDialogY,
            desiredDialogWidth,
            desiredDialogHeight);
        using var dialogOverlayScope = ImPopover.PushOverlayScopeLocal(dialogRect);

        float dialogX = dialogRect.X;
        float dialogY = dialogRect.Y;
        float dialogW = dialogRect.Width;
        float dialogH = dialogRect.Height;
        Im.DrawRoundedRect(dialogX, dialogY, dialogW, dialogH, 6f, Im.Style.Surface);
        Im.DrawRoundedRectStroke(dialogX, dialogY, dialogW, dialogH, 6f, Im.Style.Border, 1f);

        float px = dialogX + 12f;
        float py = dialogY + 10f;
        Im.Text("Add subtable column".AsSpan(), px, py, Im.Style.FontSize, Im.Style.TextPrimary);
        py += 22f;

        string columnLabel = "Column: " + _addSubtableColumnSnapshot.Name;
        Im.Text(columnLabel.AsSpan(), px, py, Im.Style.FontSize - 1f, Im.Style.TextSecondary);
        py += 22f;

        Im.Text("Behavior".AsSpan(), px, py, Im.Style.FontSize, Im.Style.TextPrimary);
        py += 18f;
        int modeIndex = _addSubtableColumnModeIndex;
        if (Im.Dropdown("add_subtable_mode", _addSubtableColumnModeNames, ref modeIndex, px, py, dialogW - 24f))
        {
            _addSubtableColumnModeIndex = Math.Clamp(modeIndex, 0, _addSubtableColumnModeNames.Length - 1);
            _addSubtableColumnValidationMessage = "";
        }

        py += 32f;
        if (_addSubtableColumnModeIndex == 1)
        {
            if (!canUseExistingTable)
            {
                Im.Text("(No compatible subtable tables found.)".AsSpan(), px, py, Im.Style.FontSize - 1f, Im.Style.TextSecondary);
                py += 24f;
            }
            else
            {
                string[] existingTableOptionNames = new string[existingSubtableChoices.Count];
                for (int tableIndex = 0; tableIndex < existingSubtableChoices.Count; tableIndex++)
                {
                    existingTableOptionNames[tableIndex] = BuildExistingSubtableChoiceLabel(
                        workspace,
                        sourceTable,
                        existingSubtableChoices[tableIndex]);
                }

                Im.Text("Table".AsSpan(), px, py, Im.Style.FontSize, Im.Style.TextPrimary);
                py += 18f;
                int existingTableIndex = _addSubtableColumnExistingTableIndex;
                if (Im.Dropdown("add_subtable_existing_table", existingTableOptionNames, ref existingTableIndex, px, py, dialogW - 24f))
                {
                    _addSubtableColumnExistingTableIndex = Math.Clamp(existingTableIndex, 0, existingSubtableChoices.Count - 1);
                    _addSubtableColumnValidationMessage = "";
                }

                py += 32f;
                DocTable selectedExistingTable = existingSubtableChoices[_addSubtableColumnExistingTableIndex];
                string parentSummary = "Parent: " + ResolveSubtableChoiceParentSummary(workspace, sourceTable, selectedExistingTable);
                Im.Text(parentSummary.AsSpan(), px, py, Im.Style.FontSize - 1f, Im.Style.TextSecondary);
                py += 22f;
            }
        }

        if (!string.IsNullOrWhiteSpace(_addSubtableColumnValidationMessage))
        {
            Im.Text(
                _addSubtableColumnValidationMessage.AsSpan(),
                px,
                py,
                Im.Style.FontSize - 1f,
                Im.Style.Secondary);
        }

        float buttonY = dialogY + dialogH - Im.Style.MinButtonHeight - 10f;
        float cancelX = dialogX + dialogW - 84f;
        float applyX = cancelX - 84f - 8f;
        if (Im.Button("Apply", applyX, buttonY, 84f, Im.Style.MinButtonHeight))
        {
            DocColumn pendingSubtableColumn = _addSubtableColumnSnapshot!;
            if (_addSubtableColumnModeIndex == 1)
            {
                if (!canUseExistingTable)
                {
                    _addSubtableColumnValidationMessage = "No compatible subtable table is available.";
                    return;
                }

                if (_addSubtableColumnExistingTableIndex < 0 ||
                    _addSubtableColumnExistingTableIndex >= existingSubtableChoices.Count)
                {
                    _addSubtableColumnValidationMessage = "Select a table.";
                    return;
                }

                pendingSubtableColumn.SubtableId = existingSubtableChoices[_addSubtableColumnExistingTableIndex].Id;
            }
            else
            {
                pendingSubtableColumn.SubtableId = null;
            }

            AddColumnFromSnapshot(workspace, sourceTable, pendingSubtableColumn);
            CloseAddSubtableColumnDialog();
        }

        if (Im.Button("Cancel", cancelX, buttonY, 84f, Im.Style.MinButtonHeight))
        {
            CloseAddSubtableColumnDialog();
        }

        if (ShouldCloseEditDialogPopover(_addSubtableColumnDialogOpenedFrame, dialogRect))
        {
            CloseAddSubtableColumnDialog();
        }
    }

    private static string BuildExistingSubtableChoiceLabel(DocWorkspace workspace, DocTable sourceTable, DocTable candidateTable)
    {
        string parentSummary = ResolveSubtableChoiceParentSummary(workspace, sourceTable, candidateTable);
        return candidateTable.Name + " (" + parentSummary + ")";
    }

    private static string ResolveSubtableChoiceParentSummary(DocWorkspace workspace, DocTable sourceTable, DocTable candidateTable)
    {
        if (string.IsNullOrWhiteSpace(candidateTable.ParentRowColumnId))
        {
            return "shared table";
        }

        if (string.Equals(candidateTable.ParentTableId, sourceTable.Id, StringComparison.Ordinal))
        {
            return "child of this table";
        }

        if (!string.IsNullOrWhiteSpace(candidateTable.ParentTableId) &&
            TryGetProjectTableById(workspace, candidateTable.ParentTableId, out DocTable parentTable))
        {
            return "child of " + parentTable.Name;
        }

        return "no parent";
    }

    private static List<DocTable> BuildExistingSubtableChoices(DocWorkspace workspace, DocTable sourceTable, DocColumn subtableColumn)
    {
        string columnTypeId = ResolveColumnTypeId(subtableColumn);
        bool requiresOwnerTypeMatch = !DocColumnTypeIdMapper.IsBuiltIn(columnTypeId);
        var choices = new List<DocTable>(workspace.Project.Tables.Count);
        for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
        {
            DocTable candidateTable = workspace.Project.Tables[tableIndex];
            if (string.Equals(candidateTable.Id, sourceTable.Id, StringComparison.Ordinal))
            {
                continue;
            }

            if (requiresOwnerTypeMatch &&
                !string.Equals(candidateTable.PluginOwnerColumnTypeId, columnTypeId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            choices.Add(candidateTable);
        }

        return choices;
    }

    private static int ResolveDefaultExistingSubtableChoiceIndex(DocTable sourceTable, List<DocTable> existingSubtableChoices)
    {
        if (existingSubtableChoices.Count == 0)
        {
            return -1;
        }

        for (int tableIndex = 0; tableIndex < existingSubtableChoices.Count; tableIndex++)
        {
            if (string.Equals(existingSubtableChoices[tableIndex].ParentTableId, sourceTable.Id, StringComparison.Ordinal))
            {
                return tableIndex;
            }
        }

        return 0;
    }

    private static string GenerateDefaultCustomColumnName(DocTable table, string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "Column";
        }

        string candidate = baseName;
        int suffix = 2;
        while (IsColumnNameInUse(table, candidate))
        {
            candidate = baseName + " " + suffix.ToString();
            suffix++;
        }

        return candidate;
    }

    private static bool IsColumnNameInUse(DocTable table, string columnName)
    {
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            if (string.Equals(table.Columns[columnIndex].Name, columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddColumnFromSnapshot(DocWorkspace workspace, DocTable table, DocColumn newColumn)
    {
        if (newColumn.Kind == DocColumnKind.Subtable)
        {
            if (string.IsNullOrWhiteSpace(newColumn.SubtableId))
            {
                ChildSubtableCreationResult subtableCreation = CreateChildSubtable(workspace, table, newColumn);
                newColumn.SubtableId = subtableCreation.PrimaryTable.Id;

                var commands = new List<DocCommand>(2 + subtableCreation.AdditionalTables.Count)
                {
                    new()
                    {
                        Kind = DocCommandKind.AddColumn,
                        TableId = table.Id,
                        ColumnIndex = table.Columns.Count,
                        ColumnSnapshot = newColumn,
                    },
                };

                int nextTableInsertIndex = workspace.Project.Tables.Count;
                commands.Add(new DocCommand
                {
                    Kind = DocCommandKind.AddTable,
                    TableIndex = nextTableInsertIndex,
                    TableSnapshot = subtableCreation.PrimaryTable,
                });
                nextTableInsertIndex++;

                for (int tableIndex = 0; tableIndex < subtableCreation.AdditionalTables.Count; tableIndex++)
                {
                    commands.Add(new DocCommand
                    {
                        Kind = DocCommandKind.AddTable,
                        TableIndex = nextTableInsertIndex,
                        TableSnapshot = subtableCreation.AdditionalTables[tableIndex],
                    });
                    nextTableInsertIndex++;
                }

                workspace.ExecuteCommands(commands);
            }
            else
            {
                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.AddColumn,
                    TableId = table.Id,
                    ColumnIndex = table.Columns.Count,
                    ColumnSnapshot = newColumn,
                });
            }
        }
        else
        {
            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.AddColumn,
                TableId = table.Id,
                ColumnIndex = table.Columns.Count,
                ColumnSnapshot = newColumn
            });
        }

        int newColumnIndex = table.Columns.Count - 1;
        _selectedRows.Clear();
        _selStartRow = -1;
        _selStartCol = -1;
        _selEndRow = -1;
        _selEndCol = -1;
        _activeRow = -1;
        _activeCol = newColumnIndex;
        _selectedHeaderCol = newColumnIndex;
        StartInlineColumnRename(table, newColumnIndex, true);
        ComputeLayout(workspace, table);
    }

    private static DocColumn? CreateNewColumnSnapshot(DocWorkspace workspace, DocTable table, DocColumnKind kind)
    {
        var newColumn = new DocColumn
        {
            Name = GenerateDefaultColumnName(table, kind),
            Kind = kind,
            ColumnTypeId = DocColumnTypeIdMapper.FromKind(kind),
            Width = 150f
        };

        if (kind == DocColumnKind.Select)
        {
            newColumn.Options = ["Option 1", "Option 2", "Option 3"];
        }
        else if (kind == DocColumnKind.Relation)
        {
            var relationTables = BuildRelationTableChoices(workspace);
            if (relationTables.Count <= 0)
            {
                return null;
            }

            int defaultRelationIndex = ResolveDefaultRelationTableIndex(workspace, table);
            int relationIndex = Math.Clamp(defaultRelationIndex, 0, relationTables.Count - 1);
            newColumn.RelationTableId = relationTables[relationIndex].Id;
        }
        else if (kind == DocColumnKind.Subtable)
        {
            // Check for circular subtable references — walk up ParentTableId chain
            if (GetSubtableDepth(workspace, table) >= 3)
                return null;
        }

        return newColumn;
    }

    private readonly record struct ChildSubtableCreationResult(
        DocTable PrimaryTable,
        List<DocTable> AdditionalTables);

    private static ChildSubtableCreationResult CreateChildSubtable(DocWorkspace workspace, DocTable parentTable, DocColumn subtableColumn)
    {
        string columnTypeId = ResolveColumnTypeId(subtableColumn);
        if (string.Equals(columnTypeId, SplineGameLevelIds.ColumnTypeId, StringComparison.OrdinalIgnoreCase))
        {
            return CreateSplineGameLevelChildSubtable(workspace, parentTable, subtableColumn);
        }

        int count = workspace.Project.Tables.Count;
        string baseName = $"{parentTable.Name}_{subtableColumn.Name}";
        string fileName = $"subtable{count + 1}";

        var parentRowColumn = new DocColumn
        {
            Name = "_parentRowId",
            Kind = DocColumnKind.Text,
            IsHidden = true,
            Width = 100f,
        };

        var itemColumn = new DocColumn
        {
            Name = "Item",
            Kind = DocColumnKind.Text,
            Width = 150f,
        };

        var childTable = new DocTable
        {
            Name = baseName,
            FileName = fileName,
            ParentTableId = parentTable.Id,
            ParentRowColumnId = parentRowColumn.Id,
        };
        childTable.Columns.Add(parentRowColumn);
        childTable.Columns.Add(itemColumn);

        return new ChildSubtableCreationResult(childTable, []);
    }

    private static ChildSubtableCreationResult CreateSplineGameLevelChildSubtable(DocWorkspace workspace, DocTable parentTable, DocColumn subtableColumn)
    {
        int count = workspace.Project.Tables.Count;
        string baseName = $"{parentTable.Name}_{subtableColumn.Name}";
        string levelFileName = $"subtable{count + 1}";
        string pointsFileName = $"subtable{count + 2}";
        string entitiesFileName = $"subtable{count + 3}";

        string? systemEntityBaseTableId = GetSystemTableIdByKey(workspace, SplineGameLevelIds.SystemEntityBaseTableKey);
        string? systemEntityToolsTableId = GetSystemTableIdByKey(workspace, SplineGameLevelIds.SystemEntityToolsTableKey);

        var levelParentRowColumn = new DocColumn
        {
            Id = SplineGameLevelIds.ParentRowIdColumnId,
            Name = "_parentRowId",
            Kind = DocColumnKind.Text,
            IsHidden = true,
            Width = 120f,
        };

        var entityToolsTableColumn = new DocColumn
        {
            Id = SplineGameLevelIds.EntityToolsTableColumnId,
            Name = "EntityTools",
            Kind = DocColumnKind.TableRef,
            Width = 220f,
            TableRefBaseTableId = systemEntityToolsTableId,
        };

        var pointsParentRowColumn = new DocColumn
        {
            Id = SplineGameLevelIds.PointsParentRowIdColumnId,
            Name = "_parentRowId",
            Kind = DocColumnKind.Text,
            IsHidden = true,
            Width = 120f,
        };

        var pointsOrderColumn = new DocColumn
        {
            Id = SplineGameLevelIds.PointsOrderColumnId,
            Name = "Order",
            Kind = DocColumnKind.Number,
            ExportType = "int",
            Width = 90f,
        };

        var pointsPositionColumn = new DocColumn
        {
            Id = SplineGameLevelIds.PointsPositionColumnId,
            Name = "Position",
            Kind = DocColumnKind.Vec2,
            Width = 180f,
        };

        var pointsTangentInColumn = new DocColumn
        {
            Id = SplineGameLevelIds.PointsTangentInColumnId,
            Name = "TangentIn",
            Kind = DocColumnKind.Vec2,
            Width = 180f,
        };

        var pointsTangentOutColumn = new DocColumn
        {
            Id = SplineGameLevelIds.PointsTangentOutColumnId,
            Name = "TangentOut",
            Kind = DocColumnKind.Vec2,
            Width = 180f,
        };

        var entitiesParentRowColumn = new DocColumn
        {
            Id = SplineGameLevelIds.EntitiesParentRowIdColumnId,
            Name = "_parentRowId",
            Kind = DocColumnKind.Text,
            IsHidden = true,
            Width = 120f,
        };

        var entitiesOrderColumn = new DocColumn
        {
            Id = SplineGameLevelIds.EntitiesOrderColumnId,
            Name = "Order",
            Kind = DocColumnKind.Number,
            ExportType = "int",
            Width = 90f,
        };

        var entitiesParamTColumn = new DocColumn
        {
            Id = SplineGameLevelIds.EntitiesParamTColumnId,
            Name = "ParamT",
            Kind = DocColumnKind.Number,
            Width = 90f,
        };

        var entitiesPositionColumn = new DocColumn
        {
            Id = SplineGameLevelIds.EntitiesPositionColumnId,
            Name = "Position",
            Kind = DocColumnKind.Vec2,
            Width = 180f,
        };

        var entitiesTableRefColumn = new DocColumn
        {
            Id = SplineGameLevelIds.EntitiesTableRefColumnId,
            Name = "EntityTable",
            Kind = DocColumnKind.TableRef,
            Width = 220f,
            TableRefBaseTableId = systemEntityBaseTableId,
        };

        var entitiesRowIdColumn = new DocColumn
        {
            Id = SplineGameLevelIds.EntitiesRowIdColumnId,
            Name = "EntityRowId",
            Kind = DocColumnKind.Text,
            Width = 180f,
            RowRefTableRefColumnId = SplineGameLevelIds.EntitiesTableRefColumnId,
        };

        var entitiesDataJsonColumn = new DocColumn
        {
            Id = SplineGameLevelIds.EntitiesDataJsonColumnId,
            Name = "DataJson",
            Kind = DocColumnKind.Text,
            Width = 260f,
        };

        var pointsTable = new DocTable
        {
            Name = baseName + "_Points",
            FileName = pointsFileName,
            ParentRowColumnId = pointsParentRowColumn.Id,
            PluginTableTypeId = SplineGameLevelIds.PointsTableTypeId,
            PluginOwnerColumnTypeId = SplineGameLevelIds.ColumnTypeId,
            IsPluginSchemaLocked = true,
        };
        pointsTable.Columns.Add(pointsParentRowColumn);
        pointsTable.Columns.Add(pointsOrderColumn);
        pointsTable.Columns.Add(pointsPositionColumn);
        pointsTable.Columns.Add(pointsTangentInColumn);
        pointsTable.Columns.Add(pointsTangentOutColumn);

        var entitiesTable = new DocTable
        {
            Name = baseName + "_Entities",
            FileName = entitiesFileName,
            ParentRowColumnId = entitiesParentRowColumn.Id,
            PluginTableTypeId = SplineGameLevelIds.EntitiesTableTypeId,
            PluginOwnerColumnTypeId = SplineGameLevelIds.ColumnTypeId,
            IsPluginSchemaLocked = true,
        };
        entitiesTable.Columns.Add(entitiesParentRowColumn);
        entitiesTable.Columns.Add(entitiesOrderColumn);
        entitiesTable.Columns.Add(entitiesParamTColumn);
        entitiesTable.Columns.Add(entitiesPositionColumn);
        entitiesTable.Columns.Add(entitiesTableRefColumn);
        entitiesTable.Columns.Add(entitiesRowIdColumn);
        entitiesTable.Columns.Add(entitiesDataJsonColumn);

        var pointsSubtableColumn = new DocColumn
        {
            Id = SplineGameLevelIds.PointsSubtableColumnId,
            Name = "Points",
            Kind = DocColumnKind.Subtable,
            SubtableId = pointsTable.Id,
            Width = 190f,
        };

        var entitiesSubtableColumn = new DocColumn
        {
            Id = SplineGameLevelIds.EntitiesSubtableColumnId,
            Name = "Entities",
            Kind = DocColumnKind.Subtable,
            SubtableId = entitiesTable.Id,
            Width = 210f,
        };

        var levelTable = new DocTable
        {
            Name = baseName,
            FileName = levelFileName,
            ParentTableId = parentTable.Id,
            ParentRowColumnId = levelParentRowColumn.Id,
            PluginTableTypeId = SplineGameLevelIds.TableTypeId,
            PluginOwnerColumnTypeId = SplineGameLevelIds.ColumnTypeId,
            IsPluginSchemaLocked = true,
        };

        pointsTable.ParentTableId = levelTable.Id;
        entitiesTable.ParentTableId = levelTable.Id;

        levelTable.Columns.Add(levelParentRowColumn);
        levelTable.Columns.Add(entityToolsTableColumn);
        levelTable.Columns.Add(pointsSubtableColumn);
        levelTable.Columns.Add(entitiesSubtableColumn);

        levelTable.Views.Add(new DocView
        {
            Name = SplineGameLevelIds.LevelEditorViewName,
            Type = DocViewType.Custom,
            CustomRendererId = SplineGameLevelIds.LevelEditorRendererId,
        });

        return new ChildSubtableCreationResult(levelTable, [pointsTable, entitiesTable]);
    }

    private static string? GetSystemTableIdByKey(DocWorkspace workspace, string systemTableKey)
    {
        for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
        {
            DocTable candidateTable = workspace.Project.Tables[tableIndex];
            if (string.Equals(candidateTable.SystemKey, systemTableKey, StringComparison.Ordinal))
            {
                return candidateTable.Id;
            }
        }

        return null;
    }

    private static int GetSubtableDepth(DocWorkspace workspace, DocTable table)
    {
        int depth = 0;
        var current = table;
        while (current != null && !string.IsNullOrEmpty(current.ParentTableId))
        {
            depth++;
            current = workspace.Project.Tables.Find(t => t.Id == current.ParentTableId);
            if (depth > 10) break; // safety
        }
        return depth;
    }

    private static string GenerateDefaultColumnName(DocTable table, DocColumnKind kind)
    {
        string baseName = kind switch
        {
            DocColumnKind.Id => "Id",
            DocColumnKind.Number => "Number",
            DocColumnKind.Checkbox => "Checkbox",
            DocColumnKind.Select => "Select",
            DocColumnKind.Relation => "Relation",
            DocColumnKind.TableRef => "TableRef",
            DocColumnKind.Subtable => "Items",
            DocColumnKind.Spline => "Curve",
            DocColumnKind.TextureAsset => "Texture",
            DocColumnKind.MeshAsset => "Model",
            DocColumnKind.AudioAsset => "Audio",
            DocColumnKind.UiAsset => "UI",
            DocColumnKind.Vec2 => "Vec2",
            DocColumnKind.Vec3 => "Vec3",
            DocColumnKind.Vec4 => "Vec4",
            DocColumnKind.Color => "Color",
            _ => "Column",
        };

        if (!ColumnNameExists(table, baseName))
        {
            return baseName;
        }

        int suffix = 2;
        while (ColumnNameExists(table, $"{baseName} {suffix}"))
        {
            suffix++;
        }

        return $"{baseName} {suffix}";
    }

    private static bool ColumnNameExists(DocTable table, string name)
    {
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            if (string.Equals(table.Columns[columnIndex].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateInlineRenameColumn(DocTable table)
    {
        if (_inlineRenameColIndex < 0)
        {
            return;
        }

        if (_inlineRenameColIndex >= _colCount)
        {
            CancelInlineColumnRename();
            return;
        }

        if (IsColumnSchemaLocked(GetVisibleColumn(table, _inlineRenameColIndex)))
        {
            CancelInlineColumnRename();
        }
    }

    private static void StartInlineColumnRename(DocTable table, int columnIndex, bool selectAll)
    {
        if (columnIndex < 0 || columnIndex >= _colCount)
        {
            return;
        }

        var column = GetVisibleColumn(table, columnIndex);
        if (IsColumnSchemaLocked(column))
        {
            return;
        }

        _inlineRenameColIndex = columnIndex;
        string columnName = column.Name;
        _renameColBufferLength = Math.Min(columnName.Length, _renameColBuffer.Length);
        columnName.AsSpan(0, _renameColBufferLength).CopyTo(_renameColBuffer);
        _inlineRenameNeedsFocus = true;
        _inlineRenameSelectAll = selectAll;
    }

    private static void CommitInlineColumnRename(DocWorkspace workspace, DocTable table)
    {
        if (_inlineRenameColIndex < 0 || _inlineRenameColIndex >= _colCount)
        {
            CancelInlineColumnRename();
            return;
        }

        var column = GetVisibleColumn(table, _inlineRenameColIndex);
        if (IsColumnSchemaLocked(column))
        {
            CancelInlineColumnRename();
            return;
        }

        if (_renameColBufferLength > 0)
        {
            string newName = new string(_renameColBuffer, 0, _renameColBufferLength);
            if (!string.Equals(column.Name, newName, StringComparison.Ordinal))
            {
                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.RenameColumn,
                    TableId = table.Id,
                    ColumnId = column.Id,
                    OldName = column.Name,
                    NewName = newName
                });
            }
        }

        CancelInlineColumnRename();
    }

    private static void CancelInlineColumnRename()
    {
        if (_inlineRenameColIndex >= 0)
        {
            Im.ClearTextInputState(GetInlineRenameInputId(_inlineRenameColIndex));
        }

        _inlineRenameColIndex = -1;
        _renameColBufferLength = 0;
        _inlineRenameNeedsFocus = false;
        _inlineRenameSelectAll = false;
    }

    private static string GetInlineRenameInputId(int columnIndex)
    {
        return $"rename_col_inline_{columnIndex}";
    }

    private static bool IsInlineRenameFocused()
    {
        if (_inlineRenameColIndex < 0)
        {
            return false;
        }

        int widgetId = Im.Context.GetId(GetInlineRenameInputId(_inlineRenameColIndex));
        return Im.Context.IsFocused(widgetId);
    }

    private static bool IsMouseOverInlineRenameInput(Vector2 mousePos, DocTable table)
    {
        if (_inlineRenameColIndex < 0 || _inlineRenameColIndex >= _colCount)
        {
            return false;
        }

        var renameRect = GetHeaderColumnNameRect(table, GetVisibleColumn(table, _inlineRenameColIndex), _inlineRenameColIndex);
        return renameRect.Contains(mousePos);
    }

    private static ImRect GetHeaderColumnNameRect(DocTable table, DocColumn column, int columnIndex)
    {
        float nameX = GetHeaderColumnNameStartX(table, column, columnIndex);
        float nameWidth = GetHeaderColumnNameWidth(table, column, columnIndex, nameX);
        float inputY = _headerRect.Y + (_headerRect.Height - Im.Style.MinButtonHeight) * 0.5f;
        return new ImRect(nameX, inputY, nameWidth, Im.Style.MinButtonHeight);
    }

    private static float GetHeaderColumnNameStartX(DocTable table, DocColumn column, int columnIndex)
    {
        float nameX = _colX[columnIndex] + CellPaddingX;
        if (column.Kind == DocColumnKind.Relation &&
            !string.IsNullOrWhiteSpace(column.RelationDisplayColumnId))
        {
            nameX += Im.MeasureTextWidth(_headerDisplayColumnIconText.AsSpan(), Im.Style.FontSize - 1f) + 5f;
        }

        bool isPrimaryKey = !string.IsNullOrWhiteSpace(table.Keys.PrimaryKeyColumnId) &&
                            string.Equals(table.Keys.PrimaryKeyColumnId, column.Id, StringComparison.Ordinal);
        bool isSecondaryKey = FindSecondaryKeyIndex(table, column.Id) >= 0;
        string keyIconText = isPrimaryKey
            ? _headerPrimaryKeyIconText
            : (isSecondaryKey ? _headerSecondaryKeyIconText : "");
        if (keyIconText.Length > 0)
        {
            nameX += Im.MeasureTextWidth(keyIconText.AsSpan(), Im.Style.FontSize - 2f) + 5f;
        }

        return nameX;
    }

    private static float GetHeaderColumnNameWidth(DocTable table, DocColumn column, int columnIndex, float nameX)
    {
        float textRight = _colX[columnIndex] + _colW[columnIndex] - CellPaddingX;

        if (_isInteractiveRender &&
            _hoveredHeaderCol == columnIndex &&
            TryGetHeaderMenuButtonRect(column, columnIndex, out var menuButtonRect))
        {
            textRight = MathF.Min(textRight, menuButtonRect.X - HeaderMenuButtonGap);
        }

        return MathF.Max(16f, textRight - nameX);
    }

    private static bool TryGetHeaderMenuButtonRect(DocColumn column, int columnIndex, out ImRect buttonRect)
    {
        buttonRect = ImRect.Zero;
        if (columnIndex < 0 || columnIndex >= _colCount)
        {
            return false;
        }

        if (!TryGetVisibleColumnRect(columnIndex, _headerRect.Y, _headerRect.Height, out var visibleHeaderRect))
        {
            return false;
        }

        float buttonWidth = GetHeaderMenuButtonWidth(column);
        float buttonHeight = MathF.Max(16f, _headerRect.Height - 8f);
        float buttonX = _colX[columnIndex] + _colW[columnIndex] - CellPaddingX - buttonWidth;
        float buttonY = _headerRect.Y + (_headerRect.Height - buttonHeight) * 0.5f;
        buttonRect = new ImRect(buttonX, buttonY, buttonWidth, buttonHeight);

        float clippedX = MathF.Max(buttonRect.X, visibleHeaderRect.X);
        float clippedRight = MathF.Min(buttonRect.Right, visibleHeaderRect.Right);
        if (clippedRight <= clippedX)
        {
            return false;
        }

        buttonRect = new ImRect(clippedX, buttonRect.Y, clippedRight - clippedX, buttonRect.Height);
        return true;
    }

    private static float GetHeaderMenuButtonWidth(DocColumn column)
    {
        float iconFontSize = Im.Style.FontSize - 1f;
        float caretFontSize = Im.Style.FontSize - 2f;
        string kindIconText = GetColumnKindIconText(column);
        float iconWidth = kindIconText.Length > 0
            ? Im.MeasureTextWidth(kindIconText.AsSpan(), iconFontSize)
            : 0f;
        float caretWidth = Im.MeasureTextWidth(_headerMenuCaretIconText.AsSpan(), caretFontSize);
        return MathF.Max(20f, (HeaderMenuButtonPaddingX * 2f) + iconWidth + HeaderMenuButtonInnerGap + caretWidth);
    }

    private static void DrawHeaderMenuButton(DocColumn column, ImRect buttonRect, bool isLocked)
    {
        var style = Im.Style;
        bool hovered = buttonRect.Contains(Im.MousePos);
        bool active = hovered && Im.Context.Input.MouseDown && !isLocked;
        uint buttonBackground = isLocked
            ? (hovered ? BlendColor(style.Surface, 0.35f, style.Background) : style.Surface)
            : active
                ? style.Active
                : hovered
                    ? style.Hover
                    : style.Surface;

        Im.DrawRoundedRect(buttonRect.X, buttonRect.Y, buttonRect.Width, buttonRect.Height, 4f, buttonBackground);
        uint borderColor = isLocked
            ? ImStyle.WithAlpha(style.Border, 170)
            : style.Border;
        Im.DrawRoundedRectStroke(buttonRect.X, buttonRect.Y, buttonRect.Width, buttonRect.Height, 4f, borderColor, 1f);

        float iconFontSize = style.FontSize - 1f;
        float rightIconFontSize = style.FontSize - 2f;
        string kindIconText = GetColumnKindIconText(column);
        string rightIconText = isLocked ? _headerLockIconText : _headerMenuCaretIconText;
        float iconWidth = kindIconText.Length > 0
            ? Im.MeasureTextWidth(kindIconText.AsSpan(), iconFontSize)
            : 0f;
        float rightIconWidth = Im.MeasureTextWidth(rightIconText.AsSpan(), rightIconFontSize);
        float contentWidth = iconWidth + HeaderMenuButtonInnerGap + rightIconWidth;
        float contentX = buttonRect.X + (buttonRect.Width - contentWidth) * 0.5f;
        float iconY = buttonRect.Y + (buttonRect.Height - iconFontSize) * 0.5f;
        float rightIconY = buttonRect.Y + (buttonRect.Height - rightIconFontSize) * 0.5f;
        uint iconColor = isLocked
            ? ImStyle.WithAlpha(style.TextSecondary, 185)
            : style.TextSecondary;

        if (kindIconText.Length > 0)
        {
            Im.Text(kindIconText.AsSpan(), contentX, iconY, iconFontSize, iconColor);
            contentX += iconWidth + HeaderMenuButtonInnerGap;
        }

        Im.Text(rightIconText.AsSpan(), contentX, rightIconY, rightIconFontSize, iconColor);
    }

    private static bool IsMouseOverHeaderNameText(DocTable table, DocColumn column, int columnIndex, Vector2 mousePos)
    {
        float textX = GetHeaderColumnNameStartX(table, column, columnIndex);
        float textWidth = MathF.Min(
            Im.MeasureTextWidth(column.Name.AsSpan(), Im.Style.FontSize),
            GetHeaderColumnNameWidth(table, column, columnIndex, textX));
        float textY = _headerRect.Y + (_headerRect.Height - Im.Style.FontSize) * 0.5f;
        var textRect = new ImRect(textX, textY - 1f, MathF.Max(1f, textWidth), Im.Style.FontSize + 2f);
        return textRect.Contains(mousePos);
    }

    private static void OpenEditCellFormulaDialog(DocTable table, DocRow row, DocColumn column)
    {
        _editFormulaTargetKind = EditFormulaTargetKind.Cell;
        _editFormulaCellTableId = table.Id;
        _editFormulaCellRowId = row.Id;
        _editFormulaColIndex = FindVisibleColumnIndexById(table, column.Id);
        string formulaExpression = row.GetCell(column).CellFormulaExpression ?? "";
        _editFormulaBufferLength = Math.Min(formulaExpression.Length, _editFormulaBuffer.Length);
        formulaExpression.AsSpan(0, _editFormulaBufferLength).CopyTo(_editFormulaBuffer);
        _formulaInspectorContextColumnId = "";
        _showEditFormulaDialog = true;
        _editFormulaDialogOpenedFrame = Im.Context.FrameCount;
    }

    private static void DrawEditFormulaDialog(DocWorkspace workspace, DocTable table)
    {
        if (_editFormulaColIndex < 0 || _editFormulaColIndex >= _colCount)
        {
            _showEditFormulaDialog = false;
            return;
        }

        var column = GetVisibleColumn(table, _editFormulaColIndex);
        bool isCellTarget = _editFormulaTargetKind == EditFormulaTargetKind.Cell;
        DocRow? targetCellRow = null;
        int targetCellRowIndex = -1;
        if (isCellTarget)
        {
            if (!string.Equals(_editFormulaCellTableId, table.Id, StringComparison.Ordinal))
            {
                _showEditFormulaDialog = false;
                return;
            }

            targetCellRowIndex = FindRowIndexById(table, _editFormulaCellRowId);
            if (targetCellRowIndex < 0 || targetCellRowIndex >= table.Rows.Count)
            {
                _showEditFormulaDialog = false;
                return;
            }

            targetCellRow = table.Rows[targetCellRowIndex];
            _formulaInspectorPreviewRowIndex = targetCellRowIndex;
        }

        string existingExpression = isCellTarget
            ? targetCellRow!.GetCell(column).CellFormulaExpression ?? ""
            : column.FormulaExpression ?? "";

        if (!string.Equals(_formulaInspectorContextColumnId, column.Id, StringComparison.Ordinal))
        {
            _formulaInspectorContextColumnId = column.Id;
            _formulaInspectorPreviewRowIndex = 0;
            _formulaCaretTokenKind = FormulaDisplayTokenKind.Plain;
            _formulaCaretTokenText = "";
            SetFormulaInspector(
                isCellTarget
                    ? $"{table.Name}.{column.Name} (cell override)"
                    : $"{table.Name}.{column.Name}",
                isCellTarget
                    ? $"Formula output must match {column.Kind}. This expression overrides the column formula for one cell."
                    : $"Formula output must match {column.Kind}. Click a token pill for details and references.",
                existingExpression);
        }

        if (isCellTarget)
        {
            _formulaInspectorPreviewRowIndex = targetCellRowIndex;
        }

        float projectedCompletionPopupHeight = 0f;
        int editFormulaWidgetId = Im.Context.GetId("edit_formula_expr");
        if (Im.Context.IsFocused(editFormulaWidgetId))
        {
            int caretPos = _editFormulaBufferLength;
            if (Im.TryGetTextInputState("edit_formula_expr", out var editFormulaTextInputState))
            {
                caretPos = Math.Clamp(editFormulaTextInputState.CaretPos, 0, _editFormulaBufferLength);
            }

            BuildFormulaCompletionEntries(
                workspace,
                table,
                includeRowContextCompletions: true,
                _editFormulaBuffer,
                _editFormulaBufferLength,
                caretPos,
                out _,
                out _);

            int visibleCompletionCount = Math.Min(5, _formulaCompletionCount);
            if (visibleCompletionCount > 0)
            {
                projectedCompletionPopupHeight = 6f + visibleCompletionCount * (Im.Style.FontSize + 10f) + 8f;
            }
        }

        float dialogW = MathF.Min(420f, Math.Max(1f, _gridRect.Width));
        float dialogPaddingTop = 10f;
        float dialogPaddingBottom = 10f;
        float formulaLabelHeight = 20f;
        float formulaEditorHeight = EstimateFormulaEditorHeight(
            workspace,
            table,
            _editFormulaBuffer,
            _editFormulaBufferLength,
            dialogW - 24f) + projectedCompletionPopupHeight;
        float formulaEditorGap = 8f;
        float inspectorGap = 10f;
        float buttonRowHeight = 24f;
        float buttonGap = 10f;
        float dialogH = dialogPaddingTop +
                        formulaLabelHeight +
                        formulaEditorHeight +
                        formulaEditorGap +
                        FormulaInspectorPanelHeight +
                        inspectorGap +
                        buttonRowHeight +
                        buttonGap +
                        dialogPaddingBottom;
        float desiredDialogX = _gridRect.X + (_gridRect.Width - dialogW) / 2f;
        float desiredDialogY = _gridRect.Y + 40f;
        var dialogRect = GetClampedEditDialogRect(desiredDialogX, desiredDialogY, dialogW, dialogH);
        using var dialogOverlayScope = ImPopover.PushOverlayScopeLocal(dialogRect);

        float dialogX = dialogRect.X;
        float dialogY = dialogRect.Y;
        dialogW = dialogRect.Width;
        dialogH = dialogRect.Height;

        Im.DrawRoundedRect(dialogX, dialogY, dialogW, dialogH, 4f, Im.Style.Surface);
        Im.DrawRoundedRectStroke(dialogX, dialogY, dialogW, dialogH, 4f, Im.Style.Border, 1f);

        float px = dialogX + 12f;
        float py = dialogY + 10f;

        Im.Text(isCellTarget ? "Cell formula override".AsSpan() : "Column formula".AsSpan(), px, py, Im.Style.FontSize, Im.Style.TextPrimary);
        py += 20f;
        py += DrawFormulaEditor(
            workspace,
            table,
            includeRowContextCompletions: true,
            "edit_formula_expr",
            _editFormulaBuffer,
            ref _editFormulaBufferLength,
            _editFormulaBuffer.Length,
            px,
            py,
            dialogW - 24f);
        py += 8f;

        py += DrawFormulaInspectorPanel(
            workspace,
            table,
            column,
            _editFormulaBuffer,
            _editFormulaBufferLength,
            px,
            py,
            dialogW - 24f);
        py += 10f;

        float btnW = 70f;
        float btnH = 24f;
        if (Im.Button("Apply", px, py, btnW, btnH))
        {
            string newExpression = _editFormulaBufferLength > 0
                ? new string(_editFormulaBuffer, 0, _editFormulaBufferLength)
                : "";
            if (isCellTarget)
            {
                DocCellValue oldCellValue = targetCellRow!.GetCell(column);
                DocCellValue newCellValue = oldCellValue.WithCellFormulaExpression(newExpression);
                newCellValue.FormulaError = null;
                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.SetCell,
                    TableId = table.Id,
                    RowId = targetCellRow.Id,
                    ColumnId = column.Id,
                    OldCellValue = oldCellValue,
                    NewCellValue = newCellValue,
                });
            }
            else
            {
                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.SetColumnFormula,
                    TableId = table.Id,
                    ColumnId = column.Id,
                    OldFormulaExpression = column.FormulaExpression ?? "",
                    NewFormulaExpression = newExpression
                });
            }

            _showEditFormulaDialog = false;
        }

        if (Im.Button("Cancel", px + btnW + 10, py, btnW, btnH))
        {
            _showEditFormulaDialog = false;
        }

        if (ShouldCloseEditDialogPopover(_editFormulaDialogOpenedFrame, dialogRect))
        {
            _showEditFormulaDialog = false;
        }
    }

    private static int FindRowIndexById(DocTable table, string rowId)
    {
        if (string.IsNullOrWhiteSpace(rowId))
        {
            return -1;
        }

        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            if (string.Equals(table.Rows[rowIndex].Id, rowId, StringComparison.Ordinal))
            {
                return rowIndex;
            }
        }

        return -1;
    }

    private static int FindVisibleColumnIndexById(DocTable table, string columnId)
    {
        if (string.IsNullOrWhiteSpace(columnId))
        {
            return -1;
        }

        for (int visibleColumnIndex = 0; visibleColumnIndex < _colCount; visibleColumnIndex++)
        {
            DocColumn visibleColumn = GetVisibleColumn(table, visibleColumnIndex);
            if (string.Equals(visibleColumn.Id, columnId, StringComparison.Ordinal))
            {
                return visibleColumnIndex;
            }
        }

        return -1;
    }

    private static void DrawEditNumberColumnDialog(DocWorkspace workspace)
    {
        if (!TryFindEditNumberColumn(workspace, out var sourceTable, out var numberColumn) ||
            numberColumn.Kind != DocColumnKind.Number)
        {
            _showEditNumberColumnDialog = false;
            return;
        }

        float desiredDialogWidth = 380f;
        float desiredDialogHeight = 210f;
        float desiredDialogX = _gridRect.X + (_gridRect.Width - desiredDialogWidth) * 0.5f;
        float desiredDialogY = _gridRect.Y + 36f;
        var dialogRect = GetClampedEditDialogRect(
            desiredDialogX,
            desiredDialogY,
            desiredDialogWidth,
            desiredDialogHeight);
        using var dialogOverlayScope = ImPopover.PushOverlayScopeLocal(dialogRect);

        float dialogX = dialogRect.X;
        float dialogY = dialogRect.Y;
        float dialogW = dialogRect.Width;
        float dialogH = dialogRect.Height;

        Im.DrawRoundedRect(dialogX, dialogY, dialogW, dialogH, 6f, Im.Style.Surface);
        Im.DrawRoundedRectStroke(dialogX, dialogY, dialogW, dialogH, 6f, Im.Style.Border, 1f);

        float px = dialogX + 12f;
        float py = dialogY + 10f;
        string title = "Edit column: " + numberColumn.Name;
        Im.Text(title.AsSpan(), px, py, Im.Style.FontSize, Im.Style.TextPrimary);
        py += 24f;

        Im.Text("Number type".AsSpan(), px, py, Im.Style.FontSize, Im.Style.TextPrimary);
        py += 18f;
        int selectedTypeIndex = _editNumberTypeIndex;
        Im.Dropdown("edit_number_type", _numberTypeLabels, ref selectedTypeIndex, px, py, dialogW - 24f);
        if (selectedTypeIndex != _editNumberTypeIndex)
        {
            _editNumberTypeIndex = selectedTypeIndex;
            _editNumberValidationMessage = "";
        }
        py += 32f;

        Im.Text("Minimum (optional)".AsSpan(), px, py, Im.Style.FontSize, Im.Style.TextPrimary);
        py += 18f;
        bool minChanged = Im.TextInput(
            "edit_number_min",
            _editNumberMinBuffer,
            ref _editNumberMinBufferLength,
            _editNumberMinBuffer.Length,
            px,
            py,
            dialogW - 24f);
        py += 28f;

        Im.Text("Maximum (optional)".AsSpan(), px, py, Im.Style.FontSize, Im.Style.TextPrimary);
        py += 18f;
        bool maxChanged = Im.TextInput(
            "edit_number_max",
            _editNumberMaxBuffer,
            ref _editNumberMaxBufferLength,
            _editNumberMaxBuffer.Length,
            px,
            py,
            dialogW - 24f);
        py += 30f;

        if (minChanged || maxChanged)
        {
            _editNumberValidationMessage = "";
        }

        if (!string.IsNullOrWhiteSpace(_editNumberValidationMessage))
        {
            Im.Text(
                _editNumberValidationMessage.AsSpan(),
                px,
                py,
                Im.Style.FontSize - 1f,
                Im.Style.Secondary);
        }

        float buttonY = dialogY + dialogH - Im.Style.MinButtonHeight - 10f;
        float cancelX = dialogX + dialogW - 84f;
        float applyX = cancelX - 84f - 8f;
        if (Im.Button("Apply", applyX, buttonY, 84f, Im.Style.MinButtonHeight))
        {
            ApplyEditNumberColumnChanges(workspace, sourceTable, numberColumn);
        }

        if (Im.Button("Cancel", cancelX, buttonY, 84f, Im.Style.MinButtonHeight))
        {
            _showEditNumberColumnDialog = false;
            _editNumberValidationMessage = "";
        }

        if (ShouldCloseEditDialogPopover(_editNumberColumnDialogOpenedFrame, dialogRect))
        {
            _showEditNumberColumnDialog = false;
            _editNumberValidationMessage = "";
        }
    }

    private static void DrawEditVectorCellDialog(DocWorkspace workspace)
    {
        if (string.IsNullOrWhiteSpace(_editVectorCellTableId) ||
            string.IsNullOrWhiteSpace(_editVectorCellRowId) ||
            string.IsNullOrWhiteSpace(_editVectorCellColumnId))
        {
            _showEditVectorCellDialog = false;
            return;
        }

        DocTable? table = FindTableById(workspace, _editVectorCellTableId);
        if (table == null)
        {
            _showEditVectorCellDialog = false;
            return;
        }

        int rowIndex = FindRowIndexById(table, _editVectorCellRowId);
        if (rowIndex < 0 || rowIndex >= table.Rows.Count)
        {
            _showEditVectorCellDialog = false;
            return;
        }

        DocRow row = table.Rows[rowIndex];
        DocColumn? column = FindColumnById(table, _editVectorCellColumnId);
        if (column == null ||
            (column.Kind != DocColumnKind.Vec2 &&
             column.Kind != DocColumnKind.Vec3 &&
             column.Kind != DocColumnKind.Vec4 &&
             column.Kind != DocColumnKind.Color))
        {
            _showEditVectorCellDialog = false;
            return;
        }

        int dimension = column.Kind switch
        {
            DocColumnKind.Vec2 => 2,
            DocColumnKind.Vec3 => 3,
            _ => 4,
        };
        bool isColor = column.Kind == DocColumnKind.Color;

        float desiredDialogWidth = 360f;
        float desiredDialogHeight = 262f;
        float desiredDialogX = _gridRect.X + (_gridRect.Width - desiredDialogWidth) * 0.5f;
        float desiredDialogY = _gridRect.Y + 36f;
        ImRect dialogRect = GetClampedEditDialogRect(
            desiredDialogX,
            desiredDialogY,
            desiredDialogWidth,
            desiredDialogHeight);
        using var dialogOverlayScope = ImPopover.PushOverlayScopeLocal(dialogRect);

        float dialogX = dialogRect.X;
        float dialogY = dialogRect.Y;
        float dialogW = dialogRect.Width;
        float dialogH = dialogRect.Height;

        Im.DrawRoundedRect(dialogX, dialogY, dialogW, dialogH, 6f, Im.Style.Surface);
        Im.DrawRoundedRectStroke(dialogX, dialogY, dialogW, dialogH, 6f, Im.Style.Border, 1f);

        float contentX = dialogX + 12f;
        float contentY = dialogY + 10f;
        string title = "Edit " + column.Name;
        Im.Text(title.AsSpan(), contentX, contentY, Im.Style.FontSize, Im.Style.TextPrimary);
        contentY += 24f;

        DrawVectorComponentEditorRow(
            inputId: "edit_vec_x",
            label: isColor ? "R" : "X",
            buffer: _editVectorCellXBuffer,
            ref _editVectorCellXBufferLength,
            contentX,
            ref contentY,
            dialogW - 24f);
        DrawVectorComponentEditorRow(
            inputId: "edit_vec_y",
            label: isColor ? "G" : "Y",
            buffer: _editVectorCellYBuffer,
            ref _editVectorCellYBufferLength,
            contentX,
            ref contentY,
            dialogW - 24f);

        if (dimension >= 3)
        {
            DrawVectorComponentEditorRow(
                inputId: "edit_vec_z",
                label: isColor ? "B" : "Z",
                buffer: _editVectorCellZBuffer,
                ref _editVectorCellZBufferLength,
                contentX,
                ref contentY,
                dialogW - 24f);
        }

        if (dimension >= 4)
        {
            DrawVectorComponentEditorRow(
                inputId: "edit_vec_w",
                label: isColor ? "A" : "W",
                buffer: _editVectorCellWBuffer,
                ref _editVectorCellWBufferLength,
                contentX,
                ref contentY,
                dialogW - 24f);
        }

        if (!string.IsNullOrWhiteSpace(_editVectorCellValidationMessage))
        {
            Im.Text(
                _editVectorCellValidationMessage.AsSpan(),
                contentX,
                contentY,
                Im.Style.FontSize - 1f,
                Im.Style.Secondary);
        }

        float buttonY = dialogY + dialogH - Im.Style.MinButtonHeight - 10f;
        float cancelX = dialogX + dialogW - 84f;
        float applyX = cancelX - 84f - 8f;
        bool canApply = Im.Button("Apply", applyX, buttonY, 84f, Im.Style.MinButtonHeight);
        if (canApply)
        {
            if (TryParseVectorDialogValue(column.Kind, out DocCellValue newCellValue))
            {
                DocCellValue oldCellValue = row.GetCell(column);
                newCellValue = DocCellValueNormalizer.NormalizeForColumn(column, newCellValue);
                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.SetCell,
                    TableId = table.Id,
                    RowId = row.Id,
                    ColumnId = column.Id,
                    OldCellValue = oldCellValue,
                    NewCellValue = newCellValue,
                });

                _showEditVectorCellDialog = false;
                _editVectorCellValidationMessage = "";
            }
        }

        if (Im.Button("Cancel", cancelX, buttonY, 84f, Im.Style.MinButtonHeight))
        {
            _showEditVectorCellDialog = false;
            _editVectorCellValidationMessage = "";
        }

        if (ShouldCloseEditDialogPopover(_editVectorCellDialogOpenedFrame, dialogRect))
        {
            _showEditVectorCellDialog = false;
            _editVectorCellValidationMessage = "";
        }
    }

    private static void DrawVectorComponentEditorRow(
        string inputId,
        string label,
        char[] buffer,
        ref int bufferLength,
        float contentX,
        ref float contentY,
        float contentWidth)
    {
        Im.Text(label.AsSpan(), contentX, contentY + 4f, Im.Style.FontSize, Im.Style.TextSecondary);
        float inputX = contentX + 24f;
        float inputWidth = Math.Max(80f, contentWidth - 24f);
        bool changed = Im.TextInput(
            inputId,
            buffer,
            ref bufferLength,
            buffer.Length,
            inputX,
            contentY,
            inputWidth);
        if (changed)
        {
            _editVectorCellValidationMessage = "";
        }

        contentY += 30f;
    }

    private static bool TryParseVectorDialogValue(DocColumnKind kind, out DocCellValue value)
    {
        value = default;
        int dimension = kind switch
        {
            DocColumnKind.Vec2 => 2,
            DocColumnKind.Vec3 => 3,
            _ => 4,
        };
        if (!TryParseVectorComponentBuffer(_editVectorCellXBuffer, _editVectorCellXBufferLength, "X/R", out double xValue) ||
            !TryParseVectorComponentBuffer(_editVectorCellYBuffer, _editVectorCellYBufferLength, "Y/G", out double yValue))
        {
            return false;
        }

        double zValue = 0;
        double wValue = 0;
        if (dimension >= 3 &&
            !TryParseVectorComponentBuffer(_editVectorCellZBuffer, _editVectorCellZBufferLength, "Z/B", out zValue))
        {
            return false;
        }

        if (dimension >= 4 &&
            !TryParseVectorComponentBuffer(_editVectorCellWBuffer, _editVectorCellWBufferLength, "W/A", out wValue))
        {
            return false;
        }

        value = kind switch
        {
            DocColumnKind.Vec2 => DocCellValue.Vec2(xValue, yValue),
            DocColumnKind.Vec3 => DocCellValue.Vec3(xValue, yValue, zValue),
            DocColumnKind.Color => DocCellValue.Color(xValue, yValue, zValue, wValue),
            _ => DocCellValue.Vec4(xValue, yValue, zValue, wValue),
        };
        return true;
    }

    private static bool TryParseVectorComponentBuffer(char[] buffer, int length, string label, out double value)
    {
        value = 0;
        string text = new string(buffer, 0, Math.Max(0, Math.Min(length, buffer.Length))).Trim();
        if (text.Length == 0)
        {
            _editVectorCellValidationMessage = "Component " + label + " is required.";
            return false;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            _editVectorCellValidationMessage = "Component " + label + " must be a number.";
            return false;
        }

        return true;
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

    private static bool DrawSubtableDisplaySizeSettingRow(
        int rowIndex,
        string settingLabel,
        string customToggleId,
        string scalarId,
        float x,
        float y,
        float width,
        float rowHeight,
        float labelColumnWidth,
        ref bool useCustomSize,
        ref float sizeValue,
        float minimumSize)
    {
        var style = Im.Style;
        const float cellPadding = 8f;
        uint backgroundColor = rowIndex % 2 == 0
            ? ImStyle.WithAlpha(style.Surface, 96)
            : ImStyle.WithAlpha(style.Surface, 80);

        Im.DrawRect(x, y, width, rowHeight, backgroundColor);
        Im.DrawLine(x, y + rowHeight, x + width, y + rowHeight, 1f, style.Border);
        Im.DrawLine(x + labelColumnWidth, y, x + labelColumnWidth, y + rowHeight, 1f, style.Border);

        float labelTextY = y + (rowHeight - style.FontSize) * 0.5f;
        Im.Text(settingLabel.AsSpan(), x + cellPadding, labelTextY, style.FontSize, style.TextPrimary);

        float valueCellX = x + labelColumnWidth + cellPadding;
        float valueCellWidth = MathF.Max(80f, width - labelColumnWidth - (cellPadding * 2f));
        float checkboxY = y + (rowHeight - style.CheckboxSize) * 0.5f;

        bool changed = false;
        bool customValue = useCustomSize;
        if (Im.Checkbox(customToggleId, ref customValue, valueCellX, checkboxY))
        {
            useCustomSize = customValue;
            changed = true;
        }

        float customLabelX = valueCellX + style.CheckboxSize + 6f;
        float customLabelFontSize = MathF.Max(10f, style.FontSize - 1f);
        float customLabelY = y + (rowHeight - customLabelFontSize) * 0.5f;
        Im.Text("Custom".AsSpan(), customLabelX, customLabelY, customLabelFontSize, style.TextSecondary);

        float customLabelWidth = Im.MeasureTextWidth("Custom".AsSpan(), customLabelFontSize);
        float inputX = customLabelX + customLabelWidth + 10f;
        float inputWidth = MathF.Max(72f, (valueCellX + valueCellWidth) - inputX);
        float inputY = y + (rowHeight - style.MinButtonHeight) * 0.5f;

        sizeValue = Math.Clamp(sizeValue, minimumSize, SubtableDisplayMaxPreviewSize);
        bool valueChanged = ImScalarInput.DrawAt(
            scalarId,
            inputX,
            inputY,
            inputWidth,
            rightOverlayWidth: 0f,
            ref sizeValue,
            minimumSize,
            SubtableDisplayMaxPreviewSize,
            format: "F0",
            mixed: false,
            disabled: !useCustomSize);
        if (valueChanged)
        {
            sizeValue = Math.Clamp(sizeValue, minimumSize, SubtableDisplayMaxPreviewSize);
            changed = true;
        }

        return changed;
    }

    private static float MeasureEditSubtableDisplayDialogRequiredHeight(
        DocWorkspace workspace,
        DocColumn subtableColumn,
        float dialogWidth)
    {
        float contentY = 10f;
        contentY += 24f; // title
        contentY += 18f; // "Display renderer" label
        contentY += 34f; // renderer dropdown + spacing
        contentY += 18f; // "Preview quality" label
        contentY += 34f; // preview quality dropdown + spacing

        float settingsTableWidth = dialogWidth - 24f;
        float settingsHeaderHeight = 26f;
        float settingsRowHeight = MathF.Max(Im.Style.MinButtonHeight + 8f, 34f);
        contentY += settingsHeaderHeight;
        contentY += settingsRowHeight;
        contentY += settingsRowHeight + 10f;

        if (_editSubtableDisplayRendererIndex >= 0 &&
            _editSubtableDisplayRendererIndex < _subtableDisplayRendererOptionCount)
        {
            string selectedRendererOptionId = _subtableDisplayRendererOptionIds[_editSubtableDisplayRendererIndex];
            string normalizedSelectedRendererId = NormalizeSubtableDisplayRendererId(selectedRendererOptionId);
            if (TryResolveSubtableDisplayRendererKind(
                    normalizedSelectedRendererId,
                    out var selectedRendererKind,
                    out string? selectedCustomRendererId) &&
                selectedRendererKind == SubtableDisplayRendererKind.Custom &&
                !string.IsNullOrWhiteSpace(selectedCustomRendererId) &&
                TableViewRendererRegistry.TryGet(selectedCustomRendererId, out var selectedRenderer) &&
                selectedRenderer is IDerpDocSubtableDisplayRenderer subtableDisplayRenderer)
            {
                DocTable? childTable = null;
                if (!string.IsNullOrWhiteSpace(subtableColumn.SubtableId))
                {
                    childTable = FindTableById(workspace, subtableColumn.SubtableId);
                }

                string? settingsJsonForMeasurement = _editSubtableDisplayPluginSettingsJson;
                if (!string.Equals(
                        _editSubtableDisplayPluginSettingsRendererId,
                        selectedCustomRendererId,
                        StringComparison.Ordinal))
                {
                    string normalizedExistingRendererId = NormalizeSubtableDisplayRendererId(subtableColumn.SubtableDisplayRendererId);
                    settingsJsonForMeasurement = string.Equals(
                            normalizedExistingRendererId,
                            normalizedSelectedRendererId,
                            StringComparison.Ordinal)
                        ? NormalizeSubtableDisplayPluginSettingsJson(subtableColumn.PluginSettingsJson)
                        : null;
                }

                contentY += 6f;
                contentY += 20f; // "Renderer settings" label
                if (childTable == null)
                {
                    contentY += Im.Style.FontSize + 6f;
                }
                else
                {
                    float pluginSettingsHeight = subtableDisplayRenderer.MeasureSubtableDisplaySettingsHeight(
                        workspace,
                        childTable,
                        subtableColumn,
                        settingsJsonForMeasurement,
                        settingsTableWidth,
                        Im.Style);
                    if (pluginSettingsHeight > 0f)
                    {
                        contentY += pluginSettingsHeight + 4f;
                    }
                }
            }
        }

        float hintFontSize = Im.Style.FontSize - 1f;
        float contentBottomY = contentY + MathF.Max(2f, hintFontSize);
        if (!string.IsNullOrWhiteSpace(_editSubtableDisplayValidationMessage))
        {
            float validationY = contentY + 16f;
            contentBottomY = validationY + (Im.Style.FontSize - 1f);
        }

        float buttonY = contentBottomY + 10f;
        float requiredDialogHeight = buttonY + Im.Style.MinButtonHeight + 10f;
        return MathF.Max(260f, requiredDialogHeight);
    }

    private static void DrawEditSubtableDisplayDialog(DocWorkspace workspace)
    {
        if (!TryFindEditSubtableDisplayColumn(workspace, out var sourceTable, out var subtableColumn) ||
            subtableColumn.Kind != DocColumnKind.Subtable)
        {
            _showEditSubtableDisplayDialog = false;
            return;
        }

        if (_subtableDisplayRendererOptionCount <= 0)
        {
            RebuildSubtableDisplayRendererOptions(subtableColumn.SubtableDisplayRendererId);
        }

        _ = TrySyncEditSubtableDisplayPluginSettingsDraft(subtableColumn);

        float desiredDialogWidth = 460f;
        float measuredDialogWidth = MathF.Min(desiredDialogWidth, Math.Max(1f, _dialogBoundsRect.Width));
        float desiredDialogHeight = MeasureEditSubtableDisplayDialogRequiredHeight(
            workspace,
            subtableColumn,
            measuredDialogWidth);
        float desiredDialogX = _dialogBoundsRect.X + (_dialogBoundsRect.Width - desiredDialogWidth) * 0.5f;
        float desiredDialogY = _dialogBoundsRect.Y + 34f;
        var dialogRect = GetClampedEditDialogRect(
            desiredDialogX,
            desiredDialogY,
            desiredDialogWidth,
            desiredDialogHeight);
        using var dialogOverlayScope = ImPopover.PushOverlayScopeLocal(dialogRect);

        float dialogX = dialogRect.X;
        float dialogY = dialogRect.Y;
        float dialogWidth = dialogRect.Width;
        float dialogHeight = dialogRect.Height;

        Im.DrawRoundedRect(dialogX, dialogY, dialogWidth, dialogHeight, 6f, Im.Style.Surface);
        Im.DrawRoundedRectStroke(dialogX, dialogY, dialogWidth, dialogHeight, 6f, Im.Style.Border, 1f);

        float contentX = dialogX + 12f;
        float titleY = dialogY + 10f;
        string title = "Edit column: " + subtableColumn.Name;
        Im.Text(title.AsSpan(), contentX, titleY, Im.Style.FontSize, Im.Style.TextPrimary);

        float rendererLabelY = titleY + 24f;
        Im.Text("Display renderer".AsSpan(), contentX, rendererLabelY, Im.Style.FontSize, Im.Style.TextPrimary);
        float rendererDropdownY = rendererLabelY + 18f;
        int selectedRendererIndex = _editSubtableDisplayRendererIndex;
        Im.Dropdown(
            "edit_subtable_display_renderer",
            _subtableDisplayRendererOptionNames.AsSpan(0, _subtableDisplayRendererOptionCount),
            ref selectedRendererIndex,
            contentX,
            rendererDropdownY,
            dialogWidth - 24f);
        if (selectedRendererIndex != _editSubtableDisplayRendererIndex)
        {
            _editSubtableDisplayRendererIndex = selectedRendererIndex;
            _editSubtableDisplayValidationMessage = "";
            _ = TrySyncEditSubtableDisplayPluginSettingsDraft(subtableColumn);
            return;
        }

        float previewQualityLabelY = rendererDropdownY + 34f;
        Im.Text("Preview quality".AsSpan(), contentX, previewQualityLabelY, Im.Style.FontSize, Im.Style.TextPrimary);
        float previewQualityDropdownY = previewQualityLabelY + 18f;
        int selectedPreviewQualityIndex = _editSubtableDisplayPreviewQualityIndex;
        Im.Dropdown(
            "edit_subtable_display_preview_quality",
            _subtableDisplayPreviewQualityOptionNames.AsSpan(),
            ref selectedPreviewQualityIndex,
            contentX,
            previewQualityDropdownY,
            dialogWidth - 24f);
        if (selectedPreviewQualityIndex != _editSubtableDisplayPreviewQualityIndex)
        {
            _editSubtableDisplayPreviewQualityIndex = selectedPreviewQualityIndex;
            _editSubtableDisplayValidationMessage = "";
            return;
        }

        float contentY = previewQualityDropdownY + 34f;
        float settingsTableWidth = dialogWidth - 24f;
        float settingsLabelColumnWidth = MathF.Max(140f, settingsTableWidth * 0.42f);
        float settingsHeaderHeight = 26f;
        float settingsRowHeight = MathF.Max(Im.Style.MinButtonHeight + 8f, 34f);

        Im.DrawRect(contentX, contentY, settingsTableWidth, settingsHeaderHeight, Im.Style.Surface);
        Im.DrawLine(contentX, contentY + settingsHeaderHeight, contentX + settingsTableWidth, contentY + settingsHeaderHeight, 1f, Im.Style.Border);
        Im.DrawLine(contentX + settingsLabelColumnWidth, contentY, contentX + settingsLabelColumnWidth, contentY + settingsHeaderHeight, 1f, Im.Style.Border);

        float headerTextY = contentY + (settingsHeaderHeight - Im.Style.FontSize) * 0.5f;
        Im.Text("Setting".AsSpan(), contentX + 8f, headerTextY, Im.Style.FontSize, Im.Style.TextSecondary);
        Im.Text("Value".AsSpan(), contentX + settingsLabelColumnWidth + 8f, headerTextY, Im.Style.FontSize, Im.Style.TextSecondary);
        contentY += settingsHeaderHeight;

        bool widthChanged = DrawSubtableDisplaySizeSettingRow(
            rowIndex: 0,
            settingLabel: "Preview width",
            customToggleId: "##edit_subtable_display_width_custom",
            scalarId: "edit_subtable_display_width_scalar",
            x: contentX,
            y: contentY,
            width: settingsTableWidth,
            rowHeight: settingsRowHeight,
            labelColumnWidth: settingsLabelColumnWidth,
            ref _editSubtableDisplayUseCustomWidth,
            ref _editSubtableDisplayWidthValue,
            SubtableDisplayMinPreviewWidth);
        contentY += settingsRowHeight;

        bool heightChanged = DrawSubtableDisplaySizeSettingRow(
            rowIndex: 1,
            settingLabel: "Preview height",
            customToggleId: "##edit_subtable_display_height_custom",
            scalarId: "edit_subtable_display_height_scalar",
            x: contentX,
            y: contentY,
            width: settingsTableWidth,
            rowHeight: settingsRowHeight,
            labelColumnWidth: settingsLabelColumnWidth,
            ref _editSubtableDisplayUseCustomHeight,
            ref _editSubtableDisplayHeightValue,
            SubtableDisplayMinPreviewHeight);
        contentY += settingsRowHeight + 10f;

        if (widthChanged || heightChanged)
        {
            _editSubtableDisplayValidationMessage = "";
        }

        if (_editSubtableDisplayRendererIndex >= 0 &&
            _editSubtableDisplayRendererIndex < _subtableDisplayRendererOptionCount)
        {
            string selectedRendererOptionId = _subtableDisplayRendererOptionIds[_editSubtableDisplayRendererIndex];
            string normalizedSelectedRendererId = NormalizeSubtableDisplayRendererId(selectedRendererOptionId);
            if (TryResolveSubtableDisplayRendererKind(
                    normalizedSelectedRendererId,
                    out var selectedRendererKind,
                    out string? selectedCustomRendererId) &&
                selectedRendererKind == SubtableDisplayRendererKind.Custom &&
                !string.IsNullOrWhiteSpace(selectedCustomRendererId) &&
                TableViewRendererRegistry.TryGet(selectedCustomRendererId, out var selectedRenderer) &&
                selectedRenderer is IDerpDocSubtableDisplayRenderer subtableDisplayRenderer)
            {
                DocTable? childTable = null;
                if (!string.IsNullOrWhiteSpace(subtableColumn.SubtableId))
                {
                    childTable = FindTableById(workspace, subtableColumn.SubtableId);
                }

                string? settingsJson = _editSubtableDisplayPluginSettingsJson;
                if (!string.Equals(
                        _editSubtableDisplayPluginSettingsRendererId,
                        selectedCustomRendererId,
                        StringComparison.Ordinal))
                {
                    string normalizedExistingRendererId = NormalizeSubtableDisplayRendererId(subtableColumn.SubtableDisplayRendererId);
                    settingsJson = string.Equals(
                            normalizedExistingRendererId,
                            normalizedSelectedRendererId,
                            StringComparison.Ordinal)
                        ? NormalizeSubtableDisplayPluginSettingsJson(subtableColumn.PluginSettingsJson)
                        : null;
                }

                contentY += 6f;
                Im.Text("Renderer settings".AsSpan(), contentX, contentY, Im.Style.FontSize, Im.Style.TextPrimary);
                contentY += 20f;
                if (childTable == null)
                {
                    Im.Text("Set the subtable target to configure renderer settings.".AsSpan(), contentX, contentY, Im.Style.FontSize - 1f, Im.Style.TextSecondary);
                    contentY += Im.Style.FontSize + 6f;
                }
                else
                {
                    string? previousSettingsJson = settingsJson;
                    float nextSettingsY = subtableDisplayRenderer.DrawSubtableDisplaySettingsEditor(
                        workspace,
                        childTable,
                        subtableColumn,
                        ref settingsJson,
                        new ImRect(contentX, contentY, settingsTableWidth, Math.Max(0f, dialogRect.Bottom - contentY)),
                        contentY,
                        Im.Style);
                    string? normalizedSettingsJson = NormalizeSubtableDisplayPluginSettingsJson(settingsJson);
                    string? normalizedPreviousSettingsJson = NormalizeSubtableDisplayPluginSettingsJson(previousSettingsJson);
                    bool pluginSettingsChanged = !string.Equals(
                        normalizedPreviousSettingsJson,
                        normalizedSettingsJson,
                        StringComparison.Ordinal);
                    _editSubtableDisplayPluginSettingsJson = normalizedSettingsJson;
                    if (pluginSettingsChanged)
                    {
                        return;
                    }
                    if (nextSettingsY > contentY)
                    {
                        contentY = nextSettingsY + 4f;
                    }
                }
            }
        }

        float hintFontSize = Im.Style.FontSize - 1f;
        string heightHintText = "Default height: auto (Grid ~26, Board/Calendar/Chart/Custom ~220).";
        Im.Text(heightHintText.AsSpan(), contentX, contentY - 2f, hintFontSize, Im.Style.TextSecondary);

        float contentBottomY = contentY + MathF.Max(2f, hintFontSize);
        if (!string.IsNullOrWhiteSpace(_editSubtableDisplayValidationMessage))
        {
            float validationY = contentY + 16f;
            Im.Text(
                _editSubtableDisplayValidationMessage.AsSpan(),
                contentX,
                validationY,
                Im.Style.FontSize - 1f,
                Im.Style.Secondary);
            contentBottomY = validationY + (Im.Style.FontSize - 1f);
        }

        float buttonY = contentBottomY + 10f;

        float cancelButtonX = dialogX + dialogWidth - 84f;
        float applyButtonX = cancelButtonX - 84f - 8f;
        if (Im.Button("Apply", applyButtonX, buttonY, 84f, Im.Style.MinButtonHeight))
        {
            ApplyEditSubtableDisplayChanges(workspace, sourceTable, subtableColumn);
        }

        if (Im.Button("Cancel", cancelButtonX, buttonY, 84f, Im.Style.MinButtonHeight))
        {
            _showEditSubtableDisplayDialog = false;
            _editSubtableDisplayValidationMessage = "";
        }

        if (ShouldCloseEditDialogPopover(_editSubtableDisplayDialogOpenedFrame, dialogRect))
        {
            _showEditSubtableDisplayDialog = false;
            _editSubtableDisplayValidationMessage = "";
        }
    }

    private static void DrawEditSelectColumnDialog(DocWorkspace workspace)
    {
        if (!TryFindEditSelectColumn(workspace, out var sourceTable, out var selectColumn) ||
            selectColumn.Kind != DocColumnKind.Select)
        {
            _showEditSelectColumnDialog = false;
            return;
        }

        float desiredDialogWidth = 460f;
        float desiredDialogHeight = 430f;
        float desiredDialogX = _gridRect.X + (_gridRect.Width - desiredDialogWidth) * 0.5f;
        float desiredDialogY = _gridRect.Y + 34f;
        var dialogRect = GetClampedEditDialogRect(
            desiredDialogX,
            desiredDialogY,
            desiredDialogWidth,
            desiredDialogHeight);
        using var dialogOverlayScope = ImPopover.PushOverlayScopeLocal(dialogRect);

        var input = Im.Context.Input;
        float dialogX = dialogRect.X;
        float dialogY = dialogRect.Y;
        float dialogW = dialogRect.Width;
        float dialogH = dialogRect.Height;

        Im.DrawRoundedRect(dialogX, dialogY, dialogW, dialogH, 6f, Im.Style.Surface);
        Im.DrawRoundedRectStroke(dialogX, dialogY, dialogW, dialogH, 6f, Im.Style.Border, 1f);

        float px = dialogX + 12f;
        float py = dialogY + 10f;
        string title = "Edit column: " + selectColumn.Name;
        Im.Text(title.AsSpan(), px, py, Im.Style.FontSize, Im.Style.TextPrimary);
        py += 24f;

        Im.Text("Options".AsSpan(), px, py, Im.Style.FontSize, Im.Style.TextPrimary);
        py += 18f;

        float listHeight = 250f;
        var listRect = new ImRect(px, py, dialogW - 24f, listHeight);
        Im.DrawRoundedRect(listRect.X, listRect.Y, listRect.Width, listRect.Height, 4f, Im.Style.Background);
        Im.DrawRoundedRectStroke(listRect.X, listRect.Y, listRect.Width, listRect.Height, 4f, Im.Style.Border, 1f);

        float rowHeight = 24f;
        float contentHeight = _editSelectEntries.Count * rowHeight;
        var contentRect = listRect;
        var scrollbarRect = listRect;
        bool hasScrollbar = contentHeight > listRect.Height;
        if (hasScrollbar)
        {
            contentRect.Width = listRect.Width - Im.Style.ScrollbarWidth;
            scrollbarRect = new ImRect(listRect.Right - Im.Style.ScrollbarWidth, listRect.Y, Im.Style.ScrollbarWidth, listRect.Height);
        }

        int removeOptionIndex = -1;
        int scrollWidgetId = Im.Context.GetId("edit_select_options_scroll");
        float contentY = ImScrollView.Begin(contentRect, contentHeight, ref _editSelectScrollY, handleMouseWheel: true);

        int firstVisibleRow = (int)MathF.Floor(_editSelectScrollY / rowHeight);
        if (firstVisibleRow < 0)
        {
            firstVisibleRow = 0;
        }

        int visibleRowCount = (int)MathF.Ceiling(contentRect.Height / rowHeight) + 2;
        int lastVisibleRow = Math.Min(_editSelectEntries.Count, firstVisibleRow + visibleRowCount);
        for (int optionIndex = firstVisibleRow; optionIndex < lastVisibleRow; optionIndex++)
        {
            var entry = _editSelectEntries[optionIndex];
            float rowY = contentY + optionIndex * rowHeight;
            var rowRect = new ImRect(contentRect.X, rowY, contentRect.Width, rowHeight);
            bool rowHovered = rowRect.Contains(Im.MousePos);
            bool rowSelected = optionIndex == _editSelectSelectedIndex;
            if (rowSelected || rowHovered)
            {
                uint rowColor = rowSelected ? Im.Style.Active : Im.Style.Hover;
                Im.DrawRect(rowRect.X, rowRect.Y, rowRect.Width, rowRect.Height, rowColor);
            }

            var dragHandleRect = new ImRect(rowRect.X + 4f, rowRect.Y + 4f, 10f, rowRect.Height - 8f);
            bool dragHandleHovered = dragHandleRect.Contains(Im.MousePos);

            float removeButtonSize = rowRect.Height - 6f;
            var removeButtonRect = new ImRect(rowRect.Right - removeButtonSize - 4f, rowRect.Y + 3f, removeButtonSize, removeButtonSize);
            bool showRemoveButton = rowHovered;
            bool removeButtonHovered = showRemoveButton && removeButtonRect.Contains(Im.MousePos);

            if (_editSelectDragIndex < 0 && input.MousePressed && (rowHovered || dragHandleHovered || removeButtonHovered))
            {
                if (removeButtonHovered)
                {
                    removeOptionIndex = optionIndex;
                    Im.Context.ConsumeMouseLeftPress();
                    continue;
                }

                if (_editSelectSelectedIndex != optionIndex)
                {
                    _editSelectSelectedIndex = optionIndex;
                    SyncEditSelectRenameBufferFromSelection();
                }

                rowSelected = true;
                if (dragHandleHovered)
                {
                    float mouseYInContent = _editSelectScrollY + (Im.MousePos.Y - contentRect.Y);
                    _editSelectDragIndex = optionIndex;
                    _editSelectDragTargetIndex = optionIndex;
                    _editSelectDragMouseOffsetY = mouseYInContent - (optionIndex * rowHeight);
                }

                Im.Context.ConsumeMouseLeftPress();
            }

            uint dragDotColor = rowSelected ? Im.Style.TextPrimary : Im.Style.TextSecondary;
            float dotY = dragHandleRect.Y + 2f;
            for (int dotRow = 0; dotRow < 3; dotRow++)
            {
                Im.DrawRoundedRect(dragHandleRect.X + 1f, dotY, 2f, 2f, 1f, dragDotColor);
                Im.DrawRoundedRect(dragHandleRect.X + 5f, dotY, 2f, 2f, 1f, dragDotColor);
                dotY += 4f;
            }

            if (rowSelected && _editSelectDragIndex < 0)
            {
                float inputX = rowRect.X + 18f;
                float inputRightPadding = showRemoveButton ? (removeButtonSize + 8f) : 6f;
                float inputWidth = Math.Max(24f, rowRect.Width - (inputX - rowRect.X) - inputRightPadding);
                float inputHeight = MathF.Max(18f, rowRect.Height - 4f);
                float inputY = rowRect.Y + (rowRect.Height - inputHeight) * 0.5f;

                Im.Context.PushId(entry.EntryId);
                bool inlineChanged = Im.TextInput(
                    "edit_select_option_inline",
                    _editSelectRenameBuffer,
                    ref _editSelectRenameBufferLength,
                    _editSelectRenameBuffer.Length,
                    inputX,
                    inputY,
                    inputWidth,
                    Im.ImTextInputFlags.NoBackground | Im.ImTextInputFlags.NoBorder);
                int inlineInputWidgetId = Im.Context.GetId("edit_select_option_inline");
                if (_editSelectInlineRenameNeedsFocus)
                {
                    Im.Context.RequestFocus(inlineInputWidgetId);
                    if (Im.TryGetTextInputState("edit_select_option_inline", out _))
                    {
                        Im.SetTextInputSelection("edit_select_option_inline", _editSelectRenameBufferLength, 0, _editSelectRenameBufferLength);
                    }

                    _editSelectInlineRenameNeedsFocus = false;
                }

                if (inlineChanged && optionIndex >= 0 && optionIndex < _editSelectEntries.Count)
                {
                    _editSelectEntries[optionIndex].Value = new string(_editSelectRenameBuffer, 0, _editSelectRenameBufferLength);
                }

                if (input.KeyEnter && Im.Context.IsFocused(inlineInputWidgetId) &&
                    optionIndex >= 0 && optionIndex < _editSelectEntries.Count)
                {
                    string trimmedValue = _editSelectEntries[optionIndex].Value.Trim();
                    _editSelectEntries[optionIndex].Value = trimmedValue;
                    _editSelectRenameBufferLength = Math.Min(trimmedValue.Length, _editSelectRenameBuffer.Length);
                    Array.Clear(_editSelectRenameBuffer);
                    trimmedValue.AsSpan(0, _editSelectRenameBufferLength).CopyTo(_editSelectRenameBuffer);
                }

                Im.Context.PopId();
            }
            else
            {
                float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
                Im.Text(entry.Value.AsSpan(), rowRect.X + 18f, textY, Im.Style.FontSize, Im.Style.TextPrimary);
            }

            if (showRemoveButton)
            {
                uint removeColor = removeButtonHovered ? Im.Style.Active : Im.Style.Hover;
                Im.DrawRoundedRect(removeButtonRect.X, removeButtonRect.Y, removeButtonRect.Width, removeButtonRect.Height, 3f, removeColor);
                Im.DrawRoundedRectStroke(removeButtonRect.X, removeButtonRect.Y, removeButtonRect.Width, removeButtonRect.Height, 3f, Im.Style.Border, 1f);
                float minusWidth = Im.MeasureTextWidth("-".AsSpan(), Im.Style.FontSize);
                float minusX = removeButtonRect.X + (removeButtonRect.Width - minusWidth) * 0.5f;
                float minusY = removeButtonRect.Y + (removeButtonRect.Height - Im.Style.FontSize) * 0.5f;
                Im.Text("-".AsSpan(), minusX, minusY, Im.Style.FontSize, Im.Style.TextPrimary);
            }
        }

        ImScrollView.End(scrollWidgetId, scrollbarRect, listRect.Height, contentHeight, ref _editSelectScrollY);

        if (removeOptionIndex >= 0)
        {
            RemoveEditSelectOptionAt(removeOptionIndex);
        }

        if (_editSelectDragIndex >= 0)
        {
            if (input.MouseDown)
            {
                float mouseYInContent = _editSelectScrollY + (Im.MousePos.Y - contentRect.Y);
                float probeY = mouseYInContent - _editSelectDragMouseOffsetY + (rowHeight * 0.5f);
                int target = (int)MathF.Floor((probeY / rowHeight) + 0.5f);
                _editSelectDragTargetIndex = Math.Clamp(target, 0, _editSelectEntries.Count);

                float insertionY = contentY + _editSelectDragTargetIndex * rowHeight;
                if (insertionY >= listRect.Y && insertionY <= listRect.Bottom)
                {
                    Im.DrawLine(contentRect.X + 2f, insertionY, contentRect.Right - 2f, insertionY, 2f, Im.Style.Primary);
                }
            }
            else
            {
                int fromIndex = _editSelectDragIndex;
                int toIndex = Math.Clamp(_editSelectDragTargetIndex, 0, _editSelectEntries.Count);
                if (toIndex > fromIndex)
                {
                    toIndex--;
                }

                if (toIndex != fromIndex &&
                    fromIndex >= 0 &&
                    fromIndex < _editSelectEntries.Count &&
                    toIndex >= 0 &&
                    toIndex <= _editSelectEntries.Count)
                {
                    var movedEntry = _editSelectEntries[fromIndex];
                    _editSelectEntries.RemoveAt(fromIndex);
                    _editSelectEntries.Insert(toIndex, movedEntry);

                    if (_editSelectSelectedIndex == fromIndex)
                    {
                        _editSelectSelectedIndex = toIndex;
                    }
                    else if (fromIndex < toIndex &&
                             _editSelectSelectedIndex > fromIndex &&
                             _editSelectSelectedIndex <= toIndex)
                    {
                        _editSelectSelectedIndex--;
                    }
                    else if (toIndex < fromIndex &&
                             _editSelectSelectedIndex >= toIndex &&
                             _editSelectSelectedIndex < fromIndex)
                    {
                        _editSelectSelectedIndex++;
                    }

                    SyncEditSelectRenameBufferFromSelection();
                }

                _editSelectDragIndex = -1;
                _editSelectDragTargetIndex = -1;
                _editSelectDragMouseOffsetY = 0f;
            }
        }

        py += listHeight + 10f;
        Im.Text("Add option".AsSpan(), px, py, Im.Style.FontSize, Im.Style.TextPrimary);
        py += 18f;
        float addInputWidth = dialogW - 24f - 84f;
        _ = Im.TextInput("edit_select_add", _editSelectAddBuffer, ref _editSelectAddBufferLength, _editSelectAddBuffer.Length, px, py, addInputWidth);
        if (Im.Button("Add", px + addInputWidth + 6f, py, 78f, Im.Style.MinButtonHeight))
        {
            TryAddEditSelectOption();
        }

        int addWidgetId = Im.Context.GetId("edit_select_add");
        if (Im.Context.Input.KeyEnter && Im.Context.IsFocused(addWidgetId))
        {
            TryAddEditSelectOption();
        }

        float buttonY = dialogY + dialogH - Im.Style.MinButtonHeight - 10f;
        float cancelX = dialogX + dialogW - 84f;
        float applyX = cancelX - 84f - 8f;
        if (Im.Button("Apply", applyX, buttonY, 84f, Im.Style.MinButtonHeight))
        {
            ApplyEditSelectColumnChanges(workspace, sourceTable, selectColumn);
        }

        if (Im.Button("Cancel", cancelX, buttonY, 84f, Im.Style.MinButtonHeight))
        {
            _showEditSelectColumnDialog = false;
        }

        if (ShouldCloseEditDialogPopover(_editSelectColumnDialogOpenedFrame, dialogRect))
        {
            _showEditSelectColumnDialog = false;
        }
    }

    private static void DrawEditRelationDialog(DocWorkspace workspace, DocTable table)
    {
        if (_editRelationColIndex < 0 || _editRelationColIndex >= _colCount)
        {
            _showEditRelationDialog = false;
            return;
        }

        var column = GetVisibleColumn(table, _editRelationColIndex);
        if (column.Kind != DocColumnKind.Relation)
        {
            _showEditRelationDialog = false;
            return;
        }

        var relationTables = BuildRelationTableChoices(workspace);
        float desiredDialogW = 360f;
        float desiredDialogH = 252f;
        float desiredDialogX = _gridRect.X + (_gridRect.Width - desiredDialogW) / 2f;
        float desiredDialogY = _gridRect.Y + 40f;
        var dialogRect = GetClampedEditDialogRect(
            desiredDialogX,
            desiredDialogY,
            desiredDialogW,
            desiredDialogH);
        using var dialogOverlayScope = ImPopover.PushOverlayScopeLocal(dialogRect);

        float dialogX = dialogRect.X;
        float dialogY = dialogRect.Y;
        float dialogW = dialogRect.Width;
        float dialogH = dialogRect.Height;

        Im.DrawRoundedRect(dialogX, dialogY, dialogW, dialogH, 4f, Im.Style.Surface);
        Im.DrawRoundedRectStroke(dialogX, dialogY, dialogW, dialogH, 4f, Im.Style.Border, 1f);

        float px = dialogX + 12f;
        float py = dialogY + 10f;

        Im.Text("Relation target:".AsSpan(), px, py, Im.Style.FontSize, Im.Style.TextPrimary);
        py += 20f;
        _editRelationTargetModeIndex = Math.Clamp(_editRelationTargetModeIndex, 0, _relationTargetModeNames.Length - 1);
        bool relationTargetSelectionChanged = false;
        int relationTargetModeIndex = _editRelationTargetModeIndex;
        if (Im.Dropdown("edit_relation_target_mode", _relationTargetModeNames, ref relationTargetModeIndex, px, py, dialogW - 24f))
        {
            int clampedModeIndex = Math.Clamp(relationTargetModeIndex, 0, _relationTargetModeNames.Length - 1);
            relationTargetSelectionChanged = clampedModeIndex != _editRelationTargetModeIndex;
            _editRelationTargetModeIndex = clampedModeIndex;
        }

        py += 30f;

        DocRelationTargetMode selectedRelationTargetMode = ResolveRelationTargetModeFromIndex(_editRelationTargetModeIndex);
        DocTable? selectedRelationTable = null;
        if (selectedRelationTargetMode == DocRelationTargetMode.ExternalTable)
        {
            if (relationTables.Count == 0)
            {
                Im.Text("(no tables available)".AsSpan(), px, py, Im.Style.FontSize, Im.Style.TextSecondary);
                py += 24f;
            }
            else
            {
                if (_editRelationTableIndex < 0 || _editRelationTableIndex >= relationTables.Count)
                {
                    _editRelationTableIndex = 0;
                }

                string[] relationTableNames = new string[relationTables.Count];
                for (int tableIndex = 0; tableIndex < relationTables.Count; tableIndex++)
                {
                    relationTableNames[tableIndex] = relationTables[tableIndex].Name;
                }

                Im.Text("Table:".AsSpan(), px, py, Im.Style.FontSize, Im.Style.TextPrimary);
                py += 20f;
                int previousTableIndex = _editRelationTableIndex;
                if (Im.Dropdown("edit_relation_target", relationTableNames, ref _editRelationTableIndex, px, py, dialogW - 24f))
                {
                    relationTargetSelectionChanged |= previousTableIndex != _editRelationTableIndex;
                }
                py += 30f;
                int selectedRelationTableIndex = Math.Clamp(_editRelationTableIndex, 0, relationTables.Count - 1);
                selectedRelationTable = relationTables[selectedRelationTableIndex];
            }
        }
        else if (selectedRelationTargetMode == DocRelationTargetMode.SelfTable)
        {
            selectedRelationTable = table;
            string relationTargetLabel = "Using this table: " + table.Name;
            Im.Text(relationTargetLabel.AsSpan(), px, py, Im.Style.FontSize - 1f, Im.Style.TextSecondary);
            py += 24f;
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(table.ParentTableId))
            {
                for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
                {
                    DocTable candidateTable = workspace.Project.Tables[tableIndex];
                    if (string.Equals(candidateTable.Id, table.ParentTableId, StringComparison.Ordinal))
                    {
                        selectedRelationTable = candidateTable;
                        break;
                    }
                }
            }

            if (selectedRelationTable != null)
            {
                string relationTargetLabel = "Using parent table: " + selectedRelationTable.Name;
                Im.Text(relationTargetLabel.AsSpan(), px, py, Im.Style.FontSize - 1f, Im.Style.TextSecondary);
            }
            else
            {
                Im.Text("(table has no parent table)".AsSpan(), px, py, Im.Style.FontSize - 1f, Im.Style.Secondary);
            }

            py += 24f;
        }

        if (selectedRelationTable != null)
        {
            string[] variantNames = new string[selectedRelationTable.Variants.Count + 1];
            variantNames[0] = DocTableVariant.BaseVariantName;
            for (int variantIndex = 0; variantIndex < selectedRelationTable.Variants.Count; variantIndex++)
            {
                variantNames[variantIndex + 1] = selectedRelationTable.Variants[variantIndex].Name;
            }

            if (relationTargetSelectionChanged)
            {
                _editRelationVariantIndex = 0;
            }
            else if (_editRelationVariantIndex < 0 || _editRelationVariantIndex >= variantNames.Length)
            {
                _editRelationVariantIndex = ResolveRelationVariantChoiceIndex(selectedRelationTable, column.RelationTableVariantId);
                _editRelationVariantIndex = Math.Clamp(_editRelationVariantIndex, 0, variantNames.Length - 1);
            }

            Im.Text("Variant:".AsSpan(), px, py, Im.Style.FontSize, Im.Style.TextPrimary);
            py += 20f;
            Im.Dropdown("edit_relation_variant", variantNames, ref _editRelationVariantIndex, px, py, dialogW - 24f);
            py += 30f;

            var displayColumns = BuildRelationDisplayColumnChoices(selectedRelationTable);
            string[] displayColumnNames = new string[displayColumns.Count + 1];
            displayColumnNames[0] = "(auto: first text/select/formula)";
            for (int columnIndex = 0; columnIndex < displayColumns.Count; columnIndex++)
            {
                displayColumnNames[columnIndex + 1] = displayColumns[columnIndex].Name;
            }

            if (relationTargetSelectionChanged)
            {
                _editRelationDisplayColumnIndex = 0;
            }
            else if (_editRelationDisplayColumnIndex < 0 || _editRelationDisplayColumnIndex >= displayColumnNames.Length)
            {
                _editRelationDisplayColumnIndex = ResolveRelationDisplayColumnChoiceIndex(displayColumns, column.RelationDisplayColumnId);
                _editRelationDisplayColumnIndex = Math.Clamp(_editRelationDisplayColumnIndex, 0, displayColumnNames.Length - 1);
            }

            Im.Text("Display column:".AsSpan(), px, py, Im.Style.FontSize, Im.Style.TextPrimary);
            py += 20f;
            Im.Dropdown("edit_relation_display_column", displayColumnNames, ref _editRelationDisplayColumnIndex, px, py, dialogW - 24f);
            py += 30f;
        }
        else
        {
            _editRelationVariantIndex = 0;
            _editRelationDisplayColumnIndex = 0;
        }

        float btnW = 70f;
        float btnH = 24f;
        if (Im.Button("Apply", px, py, btnW, btnH))
        {
            if (selectedRelationTargetMode == DocRelationTargetMode.ExternalTable &&
                selectedRelationTable == null)
            {
                return;
            }

            string? newRelationTableId = selectedRelationTargetMode == DocRelationTargetMode.ExternalTable
                ? selectedRelationTable?.Id
                : null;
            var displayColumns = selectedRelationTable != null
                ? BuildRelationDisplayColumnChoices(selectedRelationTable)
                : new List<DocColumn>(0);
            int displayColumnIndex = Math.Clamp(_editRelationDisplayColumnIndex, 0, displayColumns.Count);
            string? newRelationDisplayColumnId = displayColumnIndex == 0
                ? null
                : displayColumns[displayColumnIndex - 1].Id;
            int variantCount = selectedRelationTable?.Variants.Count ?? 0;
            int variantChoiceIndex = Math.Clamp(_editRelationVariantIndex, 0, variantCount);
            int newRelationTableVariantId = variantChoiceIndex == 0
                ? 0
                : selectedRelationTable!.Variants[variantChoiceIndex - 1].Id;
            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.SetColumnRelation,
                TableId = table.Id,
                ColumnId = column.Id,
                OldRelationTableId = column.RelationTableId,
                NewRelationTableId = newRelationTableId,
                OldRelationTargetMode = column.RelationTargetMode,
                NewRelationTargetMode = selectedRelationTargetMode,
                OldRelationTableVariantId = column.RelationTableVariantId,
                NewRelationTableVariantId = newRelationTableVariantId,
                OldRelationDisplayColumnId = column.RelationDisplayColumnId,
                NewRelationDisplayColumnId = newRelationDisplayColumnId,
            });

            _showEditRelationDialog = false;
        }

        if (Im.Button("Cancel", px + btnW + 10f, py, btnW, btnH))
        {
            _showEditRelationDialog = false;
        }

        if (ShouldCloseEditDialogPopover(_editRelationDialogOpenedFrame, dialogRect))
        {
            _showEditRelationDialog = false;
        }
    }

    internal static void ResetSharedFormulaEditorInspector(
        string title,
        string description,
        string preview)
    {
        _formulaCompletionCount = 0;
        _formulaCompletionSelectedIndex = 0;
        _formulaCaretTokenKind = FormulaDisplayTokenKind.Plain;
        _formulaCaretTokenText = "";
        _formulaInspectorPreviewRowIndex = 0;
        _formulaInspectorContextColumnId = "";
        SetFormulaInspector(title, description, preview);
    }

    internal static float DrawSharedFormulaEditor(
        DocWorkspace workspace,
        DocTable? tableContext,
        string inputId,
        char[] buffer,
        ref int length,
        int maxLength,
        float x,
        float y,
        float width,
        bool includeRowContextCompletions = true)
    {
        var resolvedContextTable = includeRowContextCompletions
            ? ResolveFormulaEditorContextTable(workspace, tableContext)
            : _formulaFallbackContextTable;
        return DrawFormulaEditor(
            workspace,
            resolvedContextTable,
            includeRowContextCompletions,
            inputId,
            buffer,
            ref length,
            maxLength,
            x,
            y,
            width);
    }

    internal static float DrawSharedFormulaInspectorPanelForDocument(
        float x,
        float y,
        float width)
    {
        var style = Im.Style;
        float panelHeight = FormulaInspectorPanelHeight;
        var panelRect = new ImRect(x, y, width, panelHeight);
        Im.DrawRoundedRect(panelRect.X, panelRect.Y, panelRect.Width, panelRect.Height, 6f, BlendColor(style.Surface, 0.22f, style.Background));
        Im.DrawRoundedRectStroke(panelRect.X, panelRect.Y, panelRect.Width, panelRect.Height, 6f, BlendColor(style.Border, 0.62f, style.Surface), 1f);

        if (!_formulaInspectorState.HasValue)
        {
            Im.Text("Click a token pill for details".AsSpan(), panelRect.X + 10f, panelRect.Y + 10f, style.FontSize, style.TextSecondary);
            return panelHeight;
        }

        float textX = panelRect.X + 10f;
        float lineY = panelRect.Y + 10f;
        float textMaxWidth = panelRect.Width - 20f;
        float textBottomY = panelRect.Bottom - 8f;
        Im.Text(_formulaInspectorState.Title.AsSpan(), textX, lineY, style.FontSize, BlendColor(style.Primary, 0.65f, style.TextPrimary));
        lineY += style.FontSize + 6f;
        lineY += DrawWrappedTextBlock(
            _formulaInspectorState.Description.AsSpan(),
            textX,
            lineY,
            textMaxWidth,
            style.FontSize,
            style.TextPrimary,
            textBottomY);
        if (!string.IsNullOrWhiteSpace(_formulaInspectorState.Preview))
        {
            lineY += 4f;
            _ = DrawWrappedTextBlock(
                _formulaInspectorState.Preview.AsSpan(),
                textX,
                lineY,
                textMaxWidth,
                style.FontSize - 1f,
                style.TextSecondary,
                textBottomY);
        }

        return panelHeight;
    }

    private static DocTable ResolveFormulaEditorContextTable(DocWorkspace workspace, DocTable? preferredContextTable)
    {
        if (preferredContextTable != null)
        {
            return preferredContextTable;
        }

        if (workspace.ActiveTable != null)
        {
            return workspace.ActiveTable;
        }

        if (workspace.Project.Tables.Count > 0)
        {
            return workspace.Project.Tables[0];
        }

        return _formulaFallbackContextTable;
    }

    private static float DrawFormulaEditor(
        DocWorkspace workspace,
        DocTable table,
        bool includeRowContextCompletions,
        string inputId,
        char[] buffer,
        ref int length,
        int maxLength,
        float x,
        float y,
        float width)
    {
        var style = Im.Style;
        float editorFrameHeight = EstimateFormulaEditorHeight(workspace, table, buffer, length, width);
        var editorFrameRect = new ImRect(x, y, width, editorFrameHeight);
        float inputHeight = MathF.Max(style.MinButtonHeight, editorFrameHeight - 16f);
        var inputRect = new ImRect(
            editorFrameRect.X + 8f,
            editorFrameRect.Y + 8f,
            editorFrameRect.Width - 16f,
            inputHeight);

        Im.TextInput(
            inputId,
            buffer,
            ref length,
            maxLength,
            inputRect.X,
            inputRect.Y,
            inputRect.Width,
            Im.ImTextInputFlags.KeepFocusOnEnter | Im.ImTextInputFlags.NoBackground | Im.ImTextInputFlags.NoBorder);

        int widgetId = Im.Context.GetId(inputId);
        bool inputFocused = Im.Context.IsFocused(widgetId);
        int caretPos = length;
        int selectionStart = -1;
        int selectionEnd = -1;
        if (Im.TryGetTextInputState(inputId, out var textInputState))
        {
            caretPos = Math.Clamp(textInputState.CaretPos, 0, length);
            selectionStart = textInputState.SelectionStart;
            selectionEnd = textInputState.SelectionEnd;
        }

        int completionReplaceStart = caretPos;
        int completionReplaceEnd = caretPos;
        string typeaheadSourceText = "";
        int typeaheadSuffixStart = 0;
        bool hasTypeaheadGhost = false;
        if (inputFocused)
        {
            BuildFormulaCompletionEntries(
                workspace,
                table,
                includeRowContextCompletions,
                buffer,
                length,
                caretPos,
                out completionReplaceStart,
                out completionReplaceEnd);
        }
        else
        {
            _formulaCompletionCount = 0;
            _formulaCompletionSelectedIndex = 0;
        }

        if (_formulaCompletionCount > 0)
        {
            _formulaCompletionSelectedIndex = Math.Clamp(_formulaCompletionSelectedIndex, 0, _formulaCompletionCount - 1);
            if (Im.Context.Input.KeyDown)
            {
                _formulaCompletionSelectedIndex = (_formulaCompletionSelectedIndex + 1) % _formulaCompletionCount;
            }
            if (Im.Context.Input.KeyUp)
            {
                _formulaCompletionSelectedIndex = (_formulaCompletionSelectedIndex - 1 + _formulaCompletionCount) % _formulaCompletionCount;
            }

            hasTypeaheadGhost = TryGetFormulaTypeaheadGhost(
                buffer,
                length,
                completionReplaceStart,
                completionReplaceEnd,
                _formulaCompletionEntries[_formulaCompletionSelectedIndex],
                out typeaheadSourceText,
                out typeaheadSuffixStart);

            bool acceptWithRightArrow = Im.Context.Input.KeyRight &&
                                        hasTypeaheadGhost &&
                                        selectionStart >= 0 &&
                                        selectionStart == selectionEnd &&
                                        caretPos == completionReplaceEnd;

            if (Im.Context.Input.KeyTab || Im.Context.Input.KeyEnter || acceptWithRightArrow)
            {
                var selectedEntry = _formulaCompletionEntries[_formulaCompletionSelectedIndex];
                SetFormulaInspectorFromCompletion(workspace, table, selectedEntry);
                ApplyFormulaCompletion(
                    inputId,
                    buffer,
                    ref length,
                    maxLength,
                    completionReplaceStart,
                    completionReplaceEnd,
                    selectedEntry,
                    widgetId);
            }
        }

        uint frameBackground = BlendColor(style.Background, 0.30f, style.Surface);
        uint frameBorder = inputFocused
            ? BlendColor(style.Primary, 0.55f, style.Border)
            : BlendColor(style.Border, 0.65f, style.Surface);
        Im.DrawRoundedRect(editorFrameRect.X, editorFrameRect.Y, editorFrameRect.Width, editorFrameRect.Height, 8f, frameBackground);
        Im.DrawRoundedRectStroke(editorFrameRect.X, editorFrameRect.Y, editorFrameRect.Width, editorFrameRect.Height, 8f, frameBorder, 1.2f);

        DrawFormulaInlineTokens(
            inputId,
            workspace,
            table,
            buffer,
            length,
            inputRect,
            caretPos,
            selectionStart,
            selectionEnd,
            inputFocused,
            typeaheadSourceText,
            typeaheadSuffixStart,
            completionReplaceEnd,
            hasTypeaheadGhost);

        float completionPopupHeight = 0f;
        if (inputFocused && _formulaCompletionCount > 0)
        {
            float popupY = editorFrameRect.Bottom + 6f;
            completionPopupHeight = DrawFormulaCompletionPopup(
                workspace,
                table,
                inputId,
                new ImRect(x, popupY, width, 0f),
                buffer,
                ref length,
                maxLength,
                completionReplaceStart,
                completionReplaceEnd,
                widgetId);
        }

        return editorFrameRect.Height + (completionPopupHeight > 0f ? 6f + completionPopupHeight : 0f);
    }

    private static void DrawFormulaInlineTokens(
        string inputId,
        DocWorkspace workspace,
        DocTable table,
        char[] buffer,
        int length,
        ImRect inputRect,
        int caretPos,
        int selectionStart,
        int selectionEnd,
        bool inputFocused,
        string typeaheadSourceText,
        int typeaheadSuffixStart,
        int typeaheadAnchorCharIndex,
        bool showTypeaheadGhost)
    {
        var style = Im.Style;
        bool hovered = inputRect.Contains(Im.MousePos);
        uint inputBackground = inputFocused
            ? BlendColor(style.Background, 0.45f, style.Surface)
            : (hovered ? BlendColor(style.Hover, 0.60f, style.Surface) : BlendColor(style.Background, 0.30f, style.Surface));

        var contentRect = new ImRect(inputRect.X + 2f, inputRect.Y + 2f, inputRect.Width - 4f, inputRect.Height - 4f);
        Im.DrawRect(contentRect.X, contentRect.Y, contentRect.Width, contentRect.Height, inputBackground);
        Im.DrawRoundedRectStroke(inputRect.X, inputRect.Y, inputRect.Width, inputRect.Height, 5f, BlendColor(style.Border, 0.5f, style.Surface), 1f);

        float textOriginX = inputRect.X + style.Padding + 1f;
        float lineHeight = style.FontSize + 8f;
        float lineMaxWidth = MathF.Max(10f, contentRect.Right - textOriginX - 2f);
        int runCount = BuildFormulaVisualLayout(workspace, table, buffer, length, style.FontSize, lineMaxWidth, out int lineCount);
        int clampedCaret = Math.Clamp(caretPos, 0, length);
        bool caretHasInspectorToken = false;

        if (contentRect.Contains(Im.MousePos) && Im.Context.Input.MousePressed)
        {
            int clickedCaret = GetFormulaCaretPosFromWrappedPoint(
                buffer,
                length,
                style.FontSize,
                textOriginX,
                contentRect.Y + 2f,
                lineHeight,
                lineCount,
                runCount,
                Im.MousePos.X,
                Im.MousePos.Y);

            if (Im.Context.Input.KeyShift && selectionStart >= 0)
            {
                int anchor = Math.Clamp(selectionStart, 0, length);
                Im.SetTextInputSelection(inputId, clickedCaret, anchor, clickedCaret);
                selectionEnd = clickedCaret;
            }
            else
            {
                Im.SetTextInputSelection(inputId, clickedCaret, clickedCaret, clickedCaret);
                selectionStart = clickedCaret;
                selectionEnd = clickedCaret;
            }

            Im.Context.RequestFocus(Im.Context.GetId(inputId));
            clampedCaret = clickedCaret;
        }
        else if (contentRect.Contains(Im.MousePos) && Im.Context.Input.MouseDown && selectionStart >= 0)
        {
            int draggedCaret = GetFormulaCaretPosFromWrappedPoint(
                buffer,
                length,
                style.FontSize,
                textOriginX,
                contentRect.Y + 2f,
                lineHeight,
                lineCount,
                runCount,
                Im.MousePos.X,
                Im.MousePos.Y);
            int anchor = Math.Clamp(selectionStart, 0, length);
            Im.SetTextInputSelection(inputId, draggedCaret, anchor, draggedCaret);
            selectionEnd = draggedCaret;
            clampedCaret = draggedCaret;
        }

        Im.PushClipRect(contentRect);

        if (inputFocused && selectionStart >= 0 && selectionEnd >= 0 && selectionStart != selectionEnd)
        {
            int selectionRangeStart = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, length);
            int selectionRangeEnd = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, length);
            DrawFormulaWrappedSelection(
                buffer,
                style.FontSize,
                textOriginX,
                contentRect.Y + 2f,
                lineHeight,
                runCount,
                selectionRangeStart,
                selectionRangeEnd,
                style.Primary);
        }

        for (int runIndex = 0; runIndex < runCount; runIndex++)
        {
            var visualRun = _formulaVisualRuns[runIndex];
            if (visualRun.Length <= 0)
            {
                continue;
            }

            ReadOnlySpan<char> tokenText = buffer.AsSpan(visualRun.Start, visualRun.Length);
            float runY = contentRect.Y + 2f + visualRun.LineIndex * lineHeight;
            float runX = textOriginX + visualRun.X;
            float tokenTextY = runY + (lineHeight - style.FontSize) * 0.5f;
            var tokenRect = new ImRect(runX, runY + 1f, visualRun.RenderWidth, lineHeight - 2f);

            if (visualRun.DrawAsPill && visualRun.TextWidth > 0f)
            {
                uint tokenBackground = GetFormulaTokenBackground(visualRun.Kind);
                Im.DrawRoundedRect(tokenRect.X, tokenRect.Y, tokenRect.Width, tokenRect.Height, 5f, tokenBackground);
                Im.DrawRoundedRectStroke(
                    tokenRect.X,
                    tokenRect.Y,
                    tokenRect.Width,
                    tokenRect.Height,
                    5f,
                    BlendColor(style.TextPrimary, 0.10f, tokenBackground),
                    1f);

                if (tokenRect.Contains(Im.MousePos) && Im.Context.Input.MousePressed)
                {
                    SetFormulaInspectorFromToken(workspace, table, visualRun.Kind, tokenText);
                }
            }

            if (!caretHasInspectorToken &&
                inputFocused &&
                visualRun.DrawAsPill &&
                clampedCaret >= visualRun.Start &&
                clampedCaret <= visualRun.End)
            {
                UpdateFormulaInspectorFromCaretToken(workspace, table, visualRun.Kind, tokenText);
                caretHasInspectorToken = true;
            }

            if (!char.IsWhiteSpace(tokenText[0]))
            {
                float tokenTextX = runX + visualRun.LeftPad;
                Im.Text(tokenText, tokenTextX, tokenTextY, style.FontSize, style.TextPrimary);
                if (visualRun.DrawAsPill && TryGetFormulaTokenIconText(visualRun.Kind, tokenText, out string tokenIconText))
                {
                    float iconX = tokenTextX + visualRun.TextWidth + 4f;
                    Im.Text(tokenIconText.AsSpan(), iconX, tokenTextY + 0.5f, style.FontSize - 1f, style.TextSecondary);
                }
            }
        }

        bool inspectorSetFromCallContext = false;
        if (inputFocused)
        {
            inspectorSetFromCallContext = TrySetFormulaInspectorFromCallContext(
                workspace,
                table,
                buffer,
                length,
                clampedCaret);
        }

        if (inputFocused && !inspectorSetFromCallContext && !caretHasInspectorToken)
        {
            if (_formulaCaretTokenKind != FormulaDisplayTokenKind.Plain || _formulaCaretTokenText.Length > 0)
            {
                _formulaCaretTokenKind = FormulaDisplayTokenKind.Plain;
                _formulaCaretTokenText = "";
                SetFormulaInspector("Formula", "Move the caret over a token to inspect it.", "");
            }
        }

        if (inputFocused &&
            showTypeaheadGhost &&
            typeaheadSuffixStart >= 0 &&
            typeaheadSourceText.Length > typeaheadSuffixStart)
        {
            var ghostPosition = GetFormulaWrappedCaretPosition(
                buffer,
                length,
                style.FontSize,
                textOriginX,
                contentRect.Y + 2f,
                lineHeight,
                lineCount,
                runCount,
                typeaheadAnchorCharIndex);
            ReadOnlySpan<char> ghostText = typeaheadSourceText.AsSpan(typeaheadSuffixStart);
            uint ghostColor = ImStyle.WithAlpha(style.TextSecondary, 165);
            Im.Text(ghostText, ghostPosition.X, ghostPosition.Y + (lineHeight - style.FontSize) * 0.5f, style.FontSize, ghostColor);
        }

        if (inputFocused && Im.Context.CaretVisible)
        {
            var caretPosition = GetFormulaWrappedCaretPosition(
                buffer,
                length,
                style.FontSize,
                textOriginX,
                contentRect.Y + 2f,
                lineHeight,
                lineCount,
                runCount,
                clampedCaret);
            Im.DrawRect(caretPosition.X, caretPosition.Y + 1f, 1f, lineHeight - 2f, style.TextPrimary);
        }

        Im.PopClipRect();
    }

    private static void GetFormulaTokenRenderPadding(
        char[] buffer,
        int length,
        FormulaDisplayToken token,
        bool drawAsPill,
        out float leftPad,
        out float rightPad)
    {
        if (!drawAsPill)
        {
            leftPad = 0f;
            rightPad = 0f;
            return;
        }

        leftPad = 4f;
        rightPad = 4f;

        if (token.Start > 0 && !char.IsWhiteSpace(buffer[token.Start - 1]))
        {
            leftPad = 3f;
        }

        int tokenEnd = token.Start + token.Length;
        if (tokenEnd < length && !char.IsWhiteSpace(buffer[tokenEnd]))
        {
            rightPad = 3f;
        }
    }

    private static float EstimateFormulaEditorHeight(
        DocWorkspace workspace,
        DocTable table,
        char[] buffer,
        int length,
        float width)
    {
        var style = Im.Style;
        float framePadding = 8f;
        float contentPadding = 2f;
        float textOriginOffset = style.Padding + 1f;
        float maxLineWidth = MathF.Max(10f, width - 16f - contentPadding * 2f - textOriginOffset - 2f);
        float lineHeight = style.FontSize + 8f;
        BuildFormulaVisualLayout(workspace, table, buffer, length, style.FontSize, maxLineWidth, out int lineCount);
        float inputHeight = MathF.Max(style.MinButtonHeight, lineCount * lineHeight + 4f);
        return inputHeight + framePadding * 2f;
    }

    private static int BuildFormulaVisualLayout(
        DocWorkspace workspace,
        DocTable table,
        char[] buffer,
        int length,
        float fontSize,
        float maxLineWidth,
        out int lineCount)
    {
        int tokenCount = TokenizeFormulaForDisplay(workspace, table, buffer, length);
        int runCount = 0;
        int currentLineIndex = 0;
        float currentLineWidth = 0f;
        Array.Clear(_formulaVisualLineWidths, 0, _formulaVisualLineWidths.Length);

        for (int tokenIndex = 0; tokenIndex < tokenCount; tokenIndex++)
        {
            var token = _formulaDisplayTokens[tokenIndex];
            if (token.Length <= 0 || runCount >= _formulaVisualRuns.Length)
            {
                continue;
            }

            ReadOnlySpan<char> tokenText = buffer.AsSpan(token.Start, token.Length);
            float tokenTextWidth = Im.MeasureTextWidth(tokenText, fontSize);
            bool drawAsPill = IsFormulaPillTokenKind(token.Kind) && !char.IsWhiteSpace(tokenText[0]);
            GetFormulaTokenRenderPadding(buffer, length, token, drawAsPill, out float leftPad, out float rightPad);
            float iconAdvance = GetFormulaTokenIconAdvance(token.Kind, tokenText, drawAsPill, fontSize);
            float tokenRenderWidth = tokenTextWidth + leftPad + rightPad + iconAdvance;

            if (currentLineWidth > 0f && currentLineWidth + tokenRenderWidth > maxLineWidth)
            {
                currentLineIndex++;
                currentLineWidth = 0f;
            }

            var visualRun = new FormulaVisualRun
            {
                Start = token.Start,
                Length = token.Length,
                End = token.Start + token.Length,
                Kind = token.Kind,
                DrawAsPill = drawAsPill,
                LeftPad = leftPad,
                RightPad = rightPad,
                IconAdvance = iconAdvance,
                TextWidth = tokenTextWidth,
                RenderWidth = tokenRenderWidth,
                LineIndex = currentLineIndex,
                X = currentLineWidth,
            };
            _formulaVisualRuns[runCount] = visualRun;
            runCount++;

            currentLineWidth += tokenRenderWidth;
            if (currentLineIndex < _formulaVisualLineWidths.Length)
            {
                _formulaVisualLineWidths[currentLineIndex] = currentLineWidth;
            }
        }

        lineCount = Math.Max(1, currentLineIndex + 1);
        return runCount;
    }

    private static Vector2 GetFormulaWrappedCaretPosition(
        char[] buffer,
        int length,
        float fontSize,
        float textOriginX,
        float textOriginY,
        float lineHeight,
        int lineCount,
        int runCount,
        int charIndex)
    {
        int clampedCharIndex = Math.Clamp(charIndex, 0, length);
        if (runCount <= 0)
        {
            return new Vector2(textOriginX, textOriginY);
        }

        for (int runIndex = 0; runIndex < runCount; runIndex++)
        {
            var visualRun = _formulaVisualRuns[runIndex];
            if (clampedCharIndex <= visualRun.Start)
            {
                return new Vector2(
                    textOriginX + visualRun.X,
                    textOriginY + visualRun.LineIndex * lineHeight);
            }

            if (clampedCharIndex < visualRun.End)
            {
                int charactersIntoRun = clampedCharIndex - visualRun.Start;
                float caretX = textOriginX + visualRun.X + (visualRun.DrawAsPill ? visualRun.LeftPad : 0f);
                if (charactersIntoRun > 0)
                {
                    caretX += Im.MeasureTextWidth(buffer.AsSpan(visualRun.Start, charactersIntoRun), fontSize);
                }

                return new Vector2(caretX, textOriginY + visualRun.LineIndex * lineHeight);
            }

            if (clampedCharIndex == visualRun.End)
            {
                return new Vector2(
                    textOriginX + visualRun.X + visualRun.RenderWidth,
                    textOriginY + visualRun.LineIndex * lineHeight);
            }
        }

        int lastLineIndex = Math.Clamp(lineCount - 1, 0, _formulaVisualLineWidths.Length - 1);
        float lastLineWidth = _formulaVisualLineWidths[lastLineIndex];
        return new Vector2(textOriginX + lastLineWidth, textOriginY + lastLineIndex * lineHeight);
    }

    private static int GetFormulaCaretPosFromWrappedPoint(
        char[] buffer,
        int length,
        float fontSize,
        float textOriginX,
        float textOriginY,
        float lineHeight,
        int lineCount,
        int runCount,
        float mouseX,
        float mouseY)
    {
        if (runCount <= 0)
        {
            return 0;
        }

        int lineIndex = (int)MathF.Floor((mouseY - textOriginY) / MathF.Max(1f, lineHeight));
        lineIndex = Math.Clamp(lineIndex, 0, Math.Max(0, lineCount - 1));
        float lineX = mouseX - textOriginX;

        int firstRunOnLine = -1;
        int lastRunOnLine = -1;
        for (int runIndex = 0; runIndex < runCount; runIndex++)
        {
            if (_formulaVisualRuns[runIndex].LineIndex != lineIndex)
            {
                continue;
            }

            if (firstRunOnLine < 0)
            {
                firstRunOnLine = runIndex;
            }

            lastRunOnLine = runIndex;
        }

        if (firstRunOnLine < 0)
        {
            return lineIndex <= 0 ? 0 : length;
        }

        for (int runIndex = firstRunOnLine; runIndex <= lastRunOnLine; runIndex++)
        {
            var visualRun = _formulaVisualRuns[runIndex];
            float runStartX = visualRun.X;
            float runEndX = runStartX + visualRun.RenderWidth;
            if (lineX > runEndX)
            {
                continue;
            }

            float withinRunX = MathF.Max(0f, lineX - runStartX);
            ReadOnlySpan<char> tokenText = buffer.AsSpan(visualRun.Start, visualRun.Length);
            if (!visualRun.DrawAsPill)
            {
                int caretInRun = GetCaretPosFromMeasuredText(tokenText, fontSize, withinRunX);
                return visualRun.Start + caretInRun;
            }

            if (withinRunX <= visualRun.LeftPad)
            {
                return visualRun.Start;
            }

            float textWidthX = withinRunX - visualRun.LeftPad;
            if (textWidthX <= visualRun.TextWidth)
            {
                int caretInRun = GetCaretPosFromMeasuredText(tokenText, fontSize, textWidthX);
                return visualRun.Start + caretInRun;
            }

            return visualRun.End;
        }

        return _formulaVisualRuns[lastRunOnLine].End;
    }

    private static void DrawFormulaWrappedSelection(
        char[] buffer,
        float fontSize,
        float textOriginX,
        float textOriginY,
        float lineHeight,
        int runCount,
        int selectionStart,
        int selectionEnd,
        uint primaryColor)
    {
        uint selectionColor = ImStyle.WithAlpha(primaryColor, 96);
        for (int runIndex = 0; runIndex < runCount; runIndex++)
        {
            var visualRun = _formulaVisualRuns[runIndex];
            if (selectionEnd <= visualRun.Start || selectionStart >= visualRun.End)
            {
                continue;
            }

            int runSelectionStart = Math.Clamp(selectionStart, visualRun.Start, visualRun.End);
            int runSelectionEnd = Math.Clamp(selectionEnd, visualRun.Start, visualRun.End);
            if (runSelectionEnd <= runSelectionStart)
            {
                continue;
            }

            float selectionX = textOriginX + visualRun.X + (visualRun.DrawAsPill ? visualRun.LeftPad : 0f);
            int leadingCharacters = runSelectionStart - visualRun.Start;
            if (leadingCharacters > 0)
            {
                selectionX += Im.MeasureTextWidth(buffer.AsSpan(visualRun.Start, leadingCharacters), fontSize);
            }

            int selectedCharacters = runSelectionEnd - runSelectionStart;
            float selectionWidth = selectedCharacters > 0
                ? Im.MeasureTextWidth(buffer.AsSpan(runSelectionStart, selectedCharacters), fontSize)
                : 0f;
            if (selectionWidth <= 0f)
            {
                continue;
            }

            float selectionY = textOriginY + visualRun.LineIndex * lineHeight + 1f;
            Im.DrawRect(selectionX, selectionY, selectionWidth, lineHeight - 2f, selectionColor);
        }
    }

    private static float DrawWrappedTextBlock(
        ReadOnlySpan<char> text,
        float x,
        float y,
        float maxWidth,
        float fontSize,
        uint color,
        float maxY)
    {
        if (text.Length == 0 || maxWidth <= 1f || y >= maxY)
        {
            return 0f;
        }

        float lineHeight = fontSize + 3f;
        float currentY = y;
        int segmentStart = 0;
        while (segmentStart < text.Length && currentY + lineHeight <= maxY)
        {
            int newlineIndex = text[segmentStart..].IndexOf('\n');
            int segmentEndExclusive = newlineIndex >= 0 ? segmentStart + newlineIndex : text.Length;
            ReadOnlySpan<char> segment = text[segmentStart..segmentEndExclusive].TrimStart();
            while (segment.Length > 0 && currentY + lineHeight <= maxY)
            {
                int wrapIndex = segment.Length;
                if (Im.MeasureTextWidth(segment, fontSize) > maxWidth)
                {
                    wrapIndex = FindWrappedLineBreakIndex(segment, maxWidth, fontSize);
                }

                ReadOnlySpan<char> line = segment[..wrapIndex].TrimEnd();
                if (line.Length > 0)
                {
                    Im.Text(line, x, currentY, fontSize, color);
                    currentY += lineHeight;
                }

                segment = segment[wrapIndex..].TrimStart();
            }

            segmentStart = segmentEndExclusive + 1;
            if (newlineIndex >= 0 && currentY + lineHeight <= maxY)
            {
                currentY += 1f;
            }
        }

        return MathF.Max(0f, currentY - y);
    }

    private static int FindWrappedLineBreakIndex(ReadOnlySpan<char> text, float maxWidth, float fontSize)
    {
        int lastWhitespaceIndex = -1;
        for (int charIndex = 0; charIndex < text.Length; charIndex++)
        {
            if (char.IsWhiteSpace(text[charIndex]))
            {
                lastWhitespaceIndex = charIndex;
            }

            float width = Im.MeasureTextWidth(text[..(charIndex + 1)], fontSize);
            if (width <= maxWidth)
            {
                continue;
            }

            if (lastWhitespaceIndex >= 0)
            {
                return Math.Max(1, lastWhitespaceIndex);
            }

            return Math.Max(1, charIndex);
        }

        return text.Length;
    }

    private static int GetCaretPosFromMeasuredText(ReadOnlySpan<char> text, float fontSize, float relativeX)
    {
        if (text.Length == 0 || relativeX <= 0f)
        {
            return 0;
        }

        float previousWidth = 0f;
        for (int charIndex = 0; charIndex < text.Length; charIndex++)
        {
            float nextWidth = Im.MeasureTextWidth(text[..(charIndex + 1)], fontSize);
            if (nextWidth >= relativeX)
            {
                float distanceToPrevious = relativeX - previousWidth;
                float distanceToNext = nextWidth - relativeX;
                return distanceToPrevious < distanceToNext ? charIndex : charIndex + 1;
            }

            previousWidth = nextWidth;
        }

        return text.Length;
    }

    private static float GetFormulaTokenIconAdvance(
        FormulaDisplayTokenKind tokenKind,
        ReadOnlySpan<char> tokenText,
        bool drawAsPill,
        float fontSize)
    {
        if (!drawAsPill || !TryGetFormulaTokenIconText(tokenKind, tokenText, out string tokenIconText))
        {
            return 0f;
        }

        return 4f + Im.MeasureTextWidth(tokenIconText.AsSpan(), fontSize - 1f);
    }

    private static bool TryGetFormulaTokenIconText(
        FormulaDisplayTokenKind tokenKind,
        ReadOnlySpan<char> tokenText,
        out string tokenIconText)
    {
        tokenIconText = "";
        if (tokenKind == FormulaDisplayTokenKind.Table)
        {
            tokenIconText = _formulaTableIconText;
            return true;
        }

        if (tokenKind == FormulaDisplayTokenKind.Document)
        {
            tokenIconText = _formulaDocumentIconText;
            return true;
        }

        if (tokenKind == FormulaDisplayTokenKind.Column)
        {
            tokenIconText = _formulaColumnIconText;
            return true;
        }

        if (tokenKind == FormulaDisplayTokenKind.Function)
        {
            tokenIconText = _formulaFunctionIconText;
            return true;
        }

        if (tokenKind == FormulaDisplayTokenKind.Method)
        {
            tokenIconText = _formulaMethodIconText;
            return true;
        }

        if (tokenKind == FormulaDisplayTokenKind.Keyword)
        {
            if (tokenText.Equals("thisRow".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                tokenText.Equals("parentRow".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                tokenIconText = _formulaRowIconText;
                return true;
            }

            if (tokenText.Equals("parentTable".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                tokenIconText = _formulaTableIconText;
                return true;
            }

            if (tokenText.Equals("docs".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                tokenText.Equals("thisDoc".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                tokenIconText = _formulaDocumentIconText;
                return true;
            }

            tokenIconText = _formulaKeywordIconText;
            return true;
        }

        return false;
    }

    private static void UpdateFormulaInspectorFromCaretToken(
        DocWorkspace workspace,
        DocTable table,
        FormulaDisplayTokenKind tokenKind,
        ReadOnlySpan<char> tokenText)
    {
        if (tokenKind == _formulaCaretTokenKind &&
            tokenText.Equals(_formulaCaretTokenText.AsSpan(), StringComparison.Ordinal))
        {
            return;
        }

        _formulaCaretTokenKind = tokenKind;
        _formulaCaretTokenText = tokenText.ToString();
        SetFormulaInspectorFromToken(workspace, table, tokenKind, tokenText);
    }

    private static bool TrySetFormulaInspectorFromCallContext(
        DocWorkspace workspace,
        DocTable table,
        char[] buffer,
        int length,
        int caretPos)
    {
        if (!TryFindFormulaCallContextAtCaret(buffer, length, caretPos, out var callContext))
        {
            return false;
        }

        ReadOnlySpan<char> callName = buffer.AsSpan(callContext.NameStart, callContext.NameLength);
        string callNameText = callName.ToString();
        string title = callContext.IsMethod ? $".{callNameText}()" : $"{callNameText}()";

        bool hasDoc = callContext.IsMethod
            ? TryGetFormulaMethodDoc(callName, out string description, out string preview)
            : TryGetFormulaFunctionDoc(callName, out description, out preview);
        if (!hasDoc)
        {
            return false;
        }

        if (TryGetFormulaParameterHint(callContext.IsMethod, callName, callContext.ArgumentIndex, out string parameterHint))
        {
            description = $"{description} Active parameter: {parameterHint}";
        }

        FormulaDisplayTokenKind contextKind = callContext.IsMethod
            ? FormulaDisplayTokenKind.Method
            : FormulaDisplayTokenKind.Function;

        string contextToken = callContext.IsMethod
            ? $".{callNameText}:{callContext.ArgumentIndex}"
            : $"{callNameText}:{callContext.ArgumentIndex}";
        if (_formulaCaretTokenKind == contextKind &&
            string.Equals(_formulaCaretTokenText, contextToken, StringComparison.Ordinal))
        {
            return true;
        }

        _formulaCaretTokenKind = contextKind;
        _formulaCaretTokenText = contextToken;
        SetFormulaInspector(title, description, preview, contextKind, callNameText);
        return true;
    }

    private static bool TryFindFormulaCallContextAtCaret(
        char[] buffer,
        int length,
        int caretPos,
        out FormulaCallContext callContext)
    {
        int scanLength = Math.Clamp(caretPos, 0, length);
        Span<FormulaCallContext> contextStack = stackalloc FormulaCallContext[32];
        int contextDepth = 0;
        bool inStringLiteral = false;
        bool escapedChar = false;

        for (int index = 0; index < scanLength; index++)
        {
            char current = buffer[index];
            if (inStringLiteral)
            {
                if (escapedChar)
                {
                    escapedChar = false;
                    continue;
                }

                if (current == '\\')
                {
                    escapedChar = true;
                    continue;
                }

                if (current == '"')
                {
                    inStringLiteral = false;
                }

                continue;
            }

            if (current == '"')
            {
                inStringLiteral = true;
                continue;
            }

            if (current == '(')
            {
                if (TryGetFormulaCallableBeforeParen(buffer, index, out var openContext))
                {
                    if (contextDepth < contextStack.Length)
                    {
                        contextStack[contextDepth] = openContext;
                        contextDepth++;
                    }
                }

                continue;
            }

            if (current == ',')
            {
                if (contextDepth > 0)
                {
                    var activeContext = contextStack[contextDepth - 1];
                    activeContext = new FormulaCallContext(
                        activeContext.NameStart,
                        activeContext.NameLength,
                        activeContext.IsMethod,
                        activeContext.ArgumentIndex + 1);
                    contextStack[contextDepth - 1] = activeContext;
                }

                continue;
            }

            if (current == ')' && contextDepth > 0)
            {
                contextDepth--;
            }
        }

        if (contextDepth <= 0)
        {
            callContext = default;
            return false;
        }

        callContext = contextStack[contextDepth - 1];
        return true;
    }

    private static bool TryGetFormulaCallableBeforeParen(char[] buffer, int parenIndex, out FormulaCallContext callContext)
    {
        int scanIndex = parenIndex - 1;
        while (scanIndex >= 0 && char.IsWhiteSpace(buffer[scanIndex]))
        {
            scanIndex--;
        }

        int endExclusive = scanIndex + 1;
        while (scanIndex >= 0 && IsFormulaIdentifierChar(buffer[scanIndex]))
        {
            scanIndex--;
        }

        int startIndex = scanIndex + 1;
        if (startIndex >= endExclusive)
        {
            callContext = default;
            return false;
        }

        bool isMethod = false;
        while (scanIndex >= 0 && char.IsWhiteSpace(buffer[scanIndex]))
        {
            scanIndex--;
        }

        if (scanIndex >= 0 && buffer[scanIndex] == '.')
        {
            isMethod = true;
        }

        callContext = new FormulaCallContext(startIndex, endExclusive - startIndex, isMethod, 0);
        return true;
    }

    private static bool TryGetFormulaParameterHint(
        bool isMethod,
        ReadOnlySpan<char> callName,
        int argumentIndex,
        out string parameterHint)
    {
        if (isMethod)
        {
            return TryGetFormulaMethodParameterHint(callName, argumentIndex, out parameterHint);
        }

        return TryGetFormulaFunctionParameterHint(callName, argumentIndex, out parameterHint);
    }

    private static bool TryGetFormulaFunctionParameterHint(
        ReadOnlySpan<char> functionName,
        int argumentIndex,
        out string parameterHint)
    {
        if (functionName.Equals("Lookup".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            parameterHint = argumentIndex switch
            {
                0 => "1/3 `tableOrRows` (e.g. Recipes or thisRow.Relation)",
                1 => "2/3 `predicate` (use @Column for candidate row values)",
                2 => "3/3 `valueExpr` (optional return value; omit to return row)",
                _ => "Lookup expects at most 3 arguments",
            };
            return true;
        }

        if (functionName.Equals("CountIf".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            parameterHint = argumentIndex switch
            {
                0 => "1/2 `tableOrRows`",
                1 => "2/2 `predicate` (boolean expression per row)",
                _ => "CountIf expects 2 arguments",
            };
            return true;
        }

        if (functionName.Equals("SumIf".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            parameterHint = argumentIndex switch
            {
                0 => "1/3 `tableOrRows`",
                1 => "2/3 `predicate` (boolean expression per row)",
                2 => "3/3 `valueExpr` (numeric expression to sum)",
                _ => "SumIf expects 3 arguments",
            };
            return true;
        }

        if (functionName.Equals("If".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            parameterHint = argumentIndex switch
            {
                0 => "1/3 `condition`",
                1 => "2/3 `whenTrue`",
                2 => "3/3 `whenFalse`",
                _ => "If expects 3 arguments",
            };
            return true;
        }

        if (functionName.Equals("Abs".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            parameterHint = argumentIndex switch
            {
                0 => "1/1 `value`",
                _ => "Abs expects 1 argument",
            };
            return true;
        }

        if (functionName.Equals("Pow".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            parameterHint = argumentIndex switch
            {
                0 => "1/2 `base`",
                1 => "2/2 `exponent`",
                _ => "Pow expects 2 arguments",
            };
            return true;
        }

        if (functionName.Equals("Exp".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            parameterHint = argumentIndex switch
            {
                0 => "1/1 `value` (returns e^value)",
                _ => "Exp expects 1 argument",
            };
            return true;
        }

        if (functionName.Equals("EvalSpline".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            parameterHint = argumentIndex switch
            {
                0 => "1/2 `spline` (usually `thisRow.Curve`)",
                1 => "2/2 `t` (0-1 parameter)",
                _ => "EvalSpline expects 2 arguments",
            };
            return true;
        }

        if (functionName.Equals("Upper".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            functionName.Equals("Lower".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            functionName.Equals("Date".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            parameterHint = argumentIndex switch
            {
                0 => "1/1 `text`",
                _ => $"{functionName.ToString()} expects 1 argument",
            };
            return true;
        }

        if (functionName.Equals("Contains".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            parameterHint = argumentIndex switch
            {
                0 => "1/2 `text` (haystack)",
                1 => "2/2 `value` (needle)",
                _ => "Contains expects 2 arguments",
            };
            return true;
        }

        if (functionName.Equals("Concat".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            parameterHint = $"Arg {argumentIndex + 1}: `value` (Concat accepts 1 or more values)";
            return true;
        }

        if (functionName.Equals("Today".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            parameterHint = "Today takes no parameters";
            return true;
        }

        if (functionName.Equals("AddDays".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            parameterHint = argumentIndex switch
            {
                0 => "1/2 `date`",
                1 => "2/2 `days`",
                _ => "AddDays expects 2 arguments",
            };
            return true;
        }

        if (functionName.Equals("DaysBetween".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            parameterHint = argumentIndex switch
            {
                0 => "1/2 `startDate`",
                1 => "2/2 `endDate`",
                _ => "DaysBetween expects 2 arguments",
            };
            return true;
        }

        if (FormulaFunctionRegistry.Contains(functionName))
        {
            parameterHint = $"Arg {argumentIndex + 1}: `value` (plugin-defined function)";
            return true;
        }

        parameterHint = "";
        return false;
    }

    private static bool TryGetFormulaMethodParameterHint(
        ReadOnlySpan<char> methodName,
        int argumentIndex,
        out string parameterHint)
    {
        if (methodName.Equals("Filter".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            parameterHint = argumentIndex switch
            {
                0 => "1/1 `predicate` (use @Column for row values)",
                _ => "Filter expects 1 argument",
            };
            return true;
        }

        if (methodName.Equals("Count".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            methodName.Equals("First".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            parameterHint = $"{methodName.ToString()} takes no parameters";
            return true;
        }

        if (methodName.Equals("Sum".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            parameterHint = argumentIndex switch
            {
                0 => "1/1 `valueExpr` (numeric expression)",
                _ => "Sum expects 1 argument",
            };
            return true;
        }

        if (methodName.Equals("Average".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            parameterHint = argumentIndex switch
            {
                0 => "1/1 `valueExpr` (numeric expression)",
                _ => "Average expects 1 argument",
            };
            return true;
        }

        if (methodName.Equals("Sort".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            parameterHint = argumentIndex switch
            {
                0 => "1/1 `sortExpr` (value used for ordering)",
                _ => "Sort expects 1 argument",
            };
            return true;
        }

        parameterHint = "";
        return false;
    }

    private static float DrawFormulaCompletionPopup(
        DocWorkspace workspace,
        DocTable table,
        string inputId,
        ImRect popupRect,
        char[] buffer,
        ref int length,
        int maxLength,
        int replaceStart,
        int replaceEnd,
        int widgetId)
    {
        var style = Im.Style;
        int visibleCount = Math.Min(5, _formulaCompletionCount);
        if (visibleCount <= 0)
        {
            return 0f;
        }

        float rowHeight = style.FontSize + 10f;
        float popupHeight = visibleCount * rowHeight + 8f;
        popupRect = new ImRect(popupRect.X, popupRect.Y, popupRect.Width, popupHeight);
        using var popupOverlayScope = ImPopover.PushOverlayScopeLocal(popupRect);

        Im.DrawRoundedRect(popupRect.X, popupRect.Y, popupRect.Width, popupRect.Height, 7f, BlendColor(style.Surface, 0.35f, style.Background));
        Im.DrawRoundedRectStroke(popupRect.X, popupRect.Y, popupRect.Width, popupRect.Height, 7f, BlendColor(style.Border, 0.65f, style.Surface), 1f);

        int maxFirst = Math.Max(0, _formulaCompletionCount - visibleCount);
        int firstVisible = Math.Clamp(_formulaCompletionSelectedIndex - (visibleCount - 1), 0, maxFirst);

        float rowY = popupRect.Y + 5f;
        for (int visibleIndex = 0; visibleIndex < visibleCount; visibleIndex++)
        {
            int entryIndex = firstVisible + visibleIndex;
            ref var entry = ref _formulaCompletionEntries[entryIndex];

            bool selected = entryIndex == _formulaCompletionSelectedIndex;
            var rowRect = new ImRect(popupRect.X + 4f, rowY, popupRect.Width - 8f, rowHeight);
            bool hovered = rowRect.Contains(Im.MousePos);
            uint rowColor = selected
                ? GetFormulaCompletionBackground(entry.Kind)
                : BlendColor(style.Surface, 0.15f, style.Background);
            if (hovered)
            {
                rowColor = BlendColor(style.Primary, 0.20f, rowColor);
            }

            Im.DrawRoundedRect(rowRect.X, rowRect.Y, rowRect.Width, rowRect.Height, 5f, rowColor);

            string kindLabel = GetFormulaCompletionKindLabel(entry.Kind);
            float badgeWidth = Im.MeasureTextWidth(kindLabel.AsSpan(), style.FontSize - 2f) + 10f;
            float badgeHeight = rowRect.Height - 8f;
            float badgeX = rowRect.X + 6f;
            float badgeY = rowRect.Y + 4f;
            uint badgeColor = BlendColor(GetFormulaCompletionBackground(entry.Kind), 0.38f, style.Background);
            Im.DrawRoundedRect(badgeX, badgeY, badgeWidth, badgeHeight, 4f, badgeColor);
            Im.Text(kindLabel.AsSpan(), badgeX + 5f, badgeY + (badgeHeight - (style.FontSize - 2f)) * 0.5f, style.FontSize - 2f, style.TextSecondary);

            float textX = badgeX + badgeWidth + 8f;
            Im.Text(entry.DisplayText.AsSpan(), textX, rowRect.Y + (rowRect.Height - style.FontSize) * 0.5f, style.FontSize, style.TextPrimary);

            if (hovered && Im.Context.Input.MousePressed)
            {
                _formulaCompletionSelectedIndex = entryIndex;
                SetFormulaInspectorFromCompletion(workspace, table, entry);
                ApplyFormulaCompletion(inputId, buffer, ref length, maxLength, replaceStart, replaceEnd, entry, widgetId);
                return popupHeight;
            }

            rowY += rowHeight;
        }

        return popupHeight;
    }

    private static bool TryGetFormulaTypeaheadGhost(
        char[] buffer,
        int length,
        int replaceStart,
        int replaceEnd,
        FormulaCompletionEntry completionEntry,
        out string sourceText,
        out int suffixStart)
    {
        sourceText = "";
        suffixStart = 0;
        if (replaceStart < 0 || replaceEnd < replaceStart || replaceEnd > length)
        {
            return false;
        }

        int typedLength = replaceEnd - replaceStart;
        if (typedLength <= 0 || completionEntry.InsertText.Length <= typedLength)
        {
            return false;
        }

        ReadOnlySpan<char> typedText = buffer.AsSpan(replaceStart, typedLength);
        ReadOnlySpan<char> completionText = completionEntry.InsertText.AsSpan();
        if (!completionText.StartsWith(typedText, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        sourceText = completionEntry.InsertText;
        suffixStart = typedLength;
        return true;
    }

    private static void ApplyFormulaCompletion(
        string inputId,
        char[] buffer,
        ref int length,
        int maxLength,
        int replaceStart,
        int replaceEnd,
        FormulaCompletionEntry entry,
        int widgetId)
    {
        if (!ReplaceBufferRange(buffer, ref length, maxLength, replaceStart, replaceEnd, entry.InsertText.AsSpan()))
        {
            return;
        }

        int newCaretPos = replaceStart + entry.InsertText.Length - entry.CaretBacktrack;
        Im.SetTextInputSelection(inputId, newCaretPos);
        Im.Context.RequestFocus(widgetId);
        _formulaCompletionCount = 0;
    }

    private static void BuildFormulaCompletionEntries(
        DocWorkspace workspace,
        DocTable table,
        bool includeRowContextCompletions,
        char[] buffer,
        int length,
        int caretPos,
        out int replaceStart,
        out int replaceEnd)
    {
        _formulaCompletionCount = 0;
        replaceStart = caretPos;
        replaceEnd = caretPos;

        int fragmentStart = caretPos;
        while (fragmentStart > 0 && IsFormulaCompletionChar(buffer[fragmentStart - 1]))
        {
            fragmentStart--;
        }

        replaceStart = fragmentStart;
        replaceEnd = caretPos;
        ReadOnlySpan<char> fragment = buffer.AsSpan(fragmentStart, caretPos - fragmentStart);
        bool memberAccess = fragmentStart > 0 && buffer[fragmentStart - 1] == '.';

        if (memberAccess)
        {
            if (TryGetMemberCompletionReceiver(workspace, table, buffer, fragmentStart - 1, out var receiverHint))
            {
                if (receiverHint.Kind == FormulaReceiverKind.RowReference && receiverHint.Table != null)
                {
                    // Explicit row-producing expressions (for example Lookup(...).) should still offer row members
                    // in document formulas even when implicit row context suggestions are hidden.
                    AddRowReferenceMemberCompletions(receiverHint.Table, fragment);
                    return;
                }

                if ((receiverHint.Kind == FormulaReceiverKind.TableReference ||
                     receiverHint.Kind == FormulaReceiverKind.RowCollection) &&
                    receiverHint.Table != null)
                {
                    AddMethodCompletions(fragment);
                    return;
                }

                if (receiverHint.Kind == FormulaReceiverKind.DocumentNamespace)
                {
                    AddDocumentAliasCompletions(workspace, fragment);
                    return;
                }

                if (receiverHint.Kind == FormulaReceiverKind.DocumentReference && receiverHint.Document != null)
                {
                    AddDocumentVariableCompletions(receiverHint.Document, fragment);
                    return;
                }

                return;
            }

            ReadOnlySpan<char> receiver = GetLeftIdentifierBeforeDot(buffer, fragmentStart - 1);
            if (includeRowContextCompletions &&
                receiver.Equals("thisRow".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                AddRowReferenceMemberCompletions(table, fragment);
            }
            else if (includeRowContextCompletions &&
                     receiver.Equals("parentRow".AsSpan(), StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(table.ParentTableId) &&
                     TryGetProjectTableById(workspace, table.ParentTableId, out var parentTable))
            {
                AddRowReferenceMemberCompletions(parentTable, fragment);
            }
            else if (includeRowContextCompletions &&
                     TryGetRelationTargetTableForMember(workspace, table, receiver, out var relationTable))
            {
                AddRowReferenceMemberCompletions(relationTable, fragment);
            }
            else if (includeRowContextCompletions &&
                     TryGetSubtableReceiverHintForMember(workspace, table, receiver, out var subtableReceiverHint) &&
                     (subtableReceiverHint.Kind == FormulaReceiverKind.TableReference ||
                      subtableReceiverHint.Kind == FormulaReceiverKind.RowCollection))
            {
                AddMethodCompletions(fragment);
            }
            else if (includeRowContextCompletions &&
                     TryGetTableRefReceiverHintForMember(table, receiver))
            {
                AddMethodCompletions(fragment);
            }
            else if (receiver.Equals("docs".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                AddDocumentAliasCompletions(workspace, fragment);
                return;
            }

            AddMethodCompletions(fragment);
            return;
        }

        bool atIdentifierMode = fragment.Length > 0 && fragment[0] == '@';
        if (atIdentifierMode)
        {
            if (!includeRowContextCompletions)
            {
                return;
            }

            ReadOnlySpan<char> atPrefix = fragment.Length > 1 ? fragment[1..] : ReadOnlySpan<char>.Empty;
            AddRowIndexCompletion(atPrefix, includeAtPrefix: true);
            AddColumnCompletions(table, atPrefix, includeAtPrefix: true);
            return;
        }

        if (fragment.Length == 0)
        {
            return;
        }

        if (includeRowContextCompletions)
        {
            AddCompletion("thisRow", "thisRow", FormulaCompletionKind.Keyword, 0, fragment);
            AddCompletion("parentRow", "parentRow", FormulaCompletionKind.Keyword, 0, fragment);
            AddCompletion("parentTable", "parentTable", FormulaCompletionKind.Keyword, 0, fragment);
        }

        AddCompletion("docs", "docs", FormulaCompletionKind.Keyword, 0, fragment);
        AddCompletion("thisDoc", "thisDoc", FormulaCompletionKind.Keyword, 0, fragment);
        if (includeRowContextCompletions)
        {
            AddCompletion("thisRowIndex", "thisRowIndex", FormulaCompletionKind.Keyword, 0, fragment);
        }

        AddCompletion("true", "true", FormulaCompletionKind.Keyword, 0, fragment);
        AddCompletion("false", "false", FormulaCompletionKind.Keyword, 0, fragment);

        for (int functionIndex = 0; functionIndex < _formulaFunctionCompletions.Length; functionIndex++)
        {
            var functionCompletion = _formulaFunctionCompletions[functionIndex];
            AddCompletion(
                functionCompletion.DisplayText,
                functionCompletion.InsertText,
                FormulaCompletionKind.Function,
                functionCompletion.CaretBacktrack,
                fragment);
        }

        AddPluginFunctionCompletions(fragment);

        for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
        {
            string tableName = workspace.Project.Tables[tableIndex].Name;
            if (!IsValidFormulaIdentifier(tableName))
            {
                continue;
            }

            AddCompletion(tableName, tableName, FormulaCompletionKind.Table, 0, fragment);
        }

        if (includeRowContextCompletions)
        {
            AddColumnCompletions(table, fragment, includeAtPrefix: true);
        }
    }

    private static void AddMethodCompletions(ReadOnlySpan<char> prefix)
    {
        for (int methodIndex = 0; methodIndex < _formulaMethodCompletions.Length; methodIndex++)
        {
            var methodCompletion = _formulaMethodCompletions[methodIndex];
            AddCompletion(
                methodCompletion.DisplayText,
                methodCompletion.InsertText,
                FormulaCompletionKind.Method,
                methodCompletion.CaretBacktrack,
                prefix);
        }
    }

    private static void AddPluginFunctionCompletions(ReadOnlySpan<char> prefix)
    {
        FormulaFunctionRegistry.CopyRegisteredFunctionNames(_formulaPluginFunctionNameScratch);
        for (int functionIndex = 0; functionIndex < _formulaPluginFunctionNameScratch.Count; functionIndex++)
        {
            string functionName = _formulaPluginFunctionNameScratch[functionIndex];
            if (string.IsNullOrWhiteSpace(functionName))
            {
                continue;
            }

            AddCompletion(
                functionName + "(...)",
                functionName + "()",
                FormulaCompletionKind.Function,
                1,
                prefix);
        }
    }

    private static void AddDocumentAliasCompletions(DocWorkspace workspace, ReadOnlySpan<char> prefix)
    {
        for (int documentIndex = 0; documentIndex < workspace.Project.Documents.Count; documentIndex++)
        {
            var document = workspace.Project.Documents[documentIndex];
            string aliasFromFileName = DocumentFormulaSyntax.NormalizeDocumentAlias(document.FileName);
            if (IsValidFormulaIdentifier(aliasFromFileName))
            {
                AddCompletion(aliasFromFileName, aliasFromFileName, FormulaCompletionKind.Document, 0, prefix);
            }

            string aliasFromTitle = DocumentFormulaSyntax.NormalizeDocumentAlias(document.Title);
            if (IsValidFormulaIdentifier(aliasFromTitle))
            {
                AddCompletion(aliasFromTitle, aliasFromTitle, FormulaCompletionKind.Document, 0, prefix);
            }
        }
    }

    private static void AddDocumentVariableCompletions(DocDocument document, ReadOnlySpan<char> prefix)
    {
        var variableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                    out _,
                    out _))
            {
                continue;
            }

            if (!variableNames.Add(variableName))
            {
                continue;
            }

            AddCompletion(variableName, variableName, FormulaCompletionKind.Column, 0, prefix);
        }
    }

    private static void AddColumnCompletions(DocTable table, ReadOnlySpan<char> prefix, bool includeAtPrefix)
    {
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            string columnName = table.Columns[columnIndex].Name;
            if (!IsValidFormulaIdentifier(columnName))
            {
                continue;
            }

            if (prefix.Length > 0 && !columnName.AsSpan().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (includeAtPrefix)
            {
                AddCompletion("@" + columnName, "@" + columnName, FormulaCompletionKind.Column, 0, ReadOnlySpan<char>.Empty);
            }
            else
            {
                AddCompletion(columnName, columnName, FormulaCompletionKind.Column, 0, ReadOnlySpan<char>.Empty);
            }
        }
    }

    private static void AddRowReferenceMemberCompletions(DocTable table, ReadOnlySpan<char> prefix)
    {
        AddRowIndexCompletion(prefix, includeAtPrefix: false);
        AddColumnCompletions(table, prefix, includeAtPrefix: false);
    }

    private static void AddRowIndexCompletion(ReadOnlySpan<char> prefix, bool includeAtPrefix)
    {
        if (includeAtPrefix)
        {
            if (prefix.Length > 0 && !"rowIndex".AsSpan().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            AddCompletion("@rowIndex", "@rowIndex", FormulaCompletionKind.Column, 0, ReadOnlySpan<char>.Empty);
            return;
        }

        AddCompletion("rowIndex", "rowIndex", FormulaCompletionKind.Keyword, 0, prefix);
    }

    private static void AddCompletion(
        string displayText,
        string insertText,
        FormulaCompletionKind kind,
        int caretBacktrack,
        ReadOnlySpan<char> prefix)
    {
        if (prefix.Length > 0 && !displayText.AsSpan().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        for (int existingIndex = 0; existingIndex < _formulaCompletionCount; existingIndex++)
        {
            if (string.Equals(_formulaCompletionEntries[existingIndex].InsertText, insertText, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        if (_formulaCompletionCount >= MaxFormulaCompletionEntries)
        {
            return;
        }

        _formulaCompletionEntries[_formulaCompletionCount] = new FormulaCompletionEntry
        {
            DisplayText = displayText,
            InsertText = insertText,
            Kind = kind,
            CaretBacktrack = caretBacktrack,
        };
        _formulaCompletionCount++;
    }

    private static int TokenizeFormulaForDisplay(DocWorkspace workspace, DocTable table, char[] buffer, int length)
    {
        int tokenCount = 0;
        int index = 0;

        while (index < length && tokenCount < MaxFormulaDisplayTokens)
        {
            int start = index;
            FormulaDisplayTokenKind tokenKind = FormulaDisplayTokenKind.Plain;
            char currentChar = buffer[index];

            if (char.IsWhiteSpace(currentChar))
            {
                while (index < length && char.IsWhiteSpace(buffer[index]))
                {
                    index++;
                }
            }
            else if (currentChar == '@')
            {
                index++;
                while (index < length && IsFormulaIdentifierChar(buffer[index]))
                {
                    index++;
                }

                tokenKind = index > start + 1 ? FormulaDisplayTokenKind.Column : FormulaDisplayTokenKind.Plain;
            }
            else if (IsFormulaIdentifierStart(currentChar))
            {
                index++;
                while (index < length && IsFormulaIdentifierChar(buffer[index]))
                {
                    index++;
                }

                tokenKind = ClassifyIdentifierToken(workspace, table, buffer, length, start, index);
            }
            else if (char.IsDigit(currentChar))
            {
                bool seenDot = false;
                index++;
                while (index < length)
                {
                    char numberChar = buffer[index];
                    if (char.IsDigit(numberChar))
                    {
                        index++;
                        continue;
                    }

                    if (numberChar == '.' && !seenDot)
                    {
                        seenDot = true;
                        index++;
                        continue;
                    }

                    break;
                }

                tokenKind = FormulaDisplayTokenKind.Number;
            }
            else if (currentChar == '"')
            {
                index++;
                while (index < length)
                {
                    char stringChar = buffer[index];
                    index++;
                    if (stringChar == '\\' && index < length)
                    {
                        index++;
                        continue;
                    }

                    if (stringChar == '"')
                    {
                        break;
                    }
                }

                tokenKind = FormulaDisplayTokenKind.String;
            }
            else
            {
                if ((currentChar == '&' || currentChar == '|') &&
                    index + 1 < length &&
                    buffer[index + 1] == currentChar)
                {
                    index += 2;
                }
                else if ((currentChar == '=' || currentChar == '!' || currentChar == '<' || currentChar == '>') &&
                    index + 1 < length &&
                    buffer[index + 1] == '=')
                {
                    index += 2;
                }
                else
                {
                    index++;
                }
            }

            _formulaDisplayTokens[tokenCount] = new FormulaDisplayToken
            {
                Start = start,
                Length = index - start,
                Kind = tokenKind,
            };
            tokenCount++;
        }

        return tokenCount;
    }

    private static FormulaDisplayTokenKind ClassifyIdentifierToken(
        DocWorkspace workspace,
        DocTable table,
        char[] buffer,
        int length,
        int start,
        int endExclusive)
    {
        ReadOnlySpan<char> identifier = buffer.AsSpan(start, endExclusive - start);
        if (identifier.Equals("thisRow".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            identifier.Equals("parentRow".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            identifier.Equals("parentTable".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            identifier.Equals("thisDoc".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            identifier.Equals("docs".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            identifier.Equals("thisRowIndex".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            identifier.Equals("true".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            identifier.Equals("false".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return FormulaDisplayTokenKind.Keyword;
        }

        char previousChar = FindPreviousNonWhitespaceChar(buffer, start - 1);
        char nextChar = FindNextNonWhitespaceChar(buffer, length, endExclusive);
        if (previousChar == '.')
        {
            ReadOnlySpan<char> receiver = GetLeftIdentifierBeforeDot(buffer, start - 1);
            if (receiver.Equals("docs".AsSpan(), StringComparison.OrdinalIgnoreCase) &&
                IsProjectDocumentAlias(workspace, identifier))
            {
                return FormulaDisplayTokenKind.Document;
            }

            if (nextChar == '(')
            {
                return FormulaDisplayTokenKind.Method;
            }

            return FormulaDisplayTokenKind.Column;
        }

        if (nextChar == '(')
        {
            return FormulaDisplayTokenKind.Function;
        }

        if (IsTableIdentifier(workspace, identifier))
        {
            return FormulaDisplayTokenKind.Table;
        }

        if (IsColumnIdentifier(table, identifier))
        {
            return FormulaDisplayTokenKind.Column;
        }

        return FormulaDisplayTokenKind.Plain;
    }

    private static bool TryGetMemberCompletionReceiver(
        DocWorkspace workspace,
        DocTable currentTable,
        char[] buffer,
        int dotIndex,
        out FormulaReceiverHint receiverHint)
    {
        receiverHint = default;
        if (dotIndex <= 0)
        {
            return false;
        }

        int receiverStart = FindMemberReceiverStart(buffer, dotIndex);
        if (receiverStart < 0 || receiverStart >= dotIndex)
        {
            return false;
        }

        return TryInferFormulaReceiverHint(
            workspace,
            currentTable,
            buffer.AsSpan(receiverStart, dotIndex - receiverStart),
            out receiverHint);
    }

    private static int FindMemberReceiverStart(char[] buffer, int dotIndex)
    {
        int start = 0;
        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int index = 0; index < dotIndex; index++)
        {
            char current = buffer[index];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (current == '"')
            {
                inString = true;
                continue;
            }

            if (current == '(')
            {
                depth++;
                continue;
            }

            if (current == ')' && depth > 0)
            {
                depth--;
                continue;
            }

            if (depth == 0 && IsTopLevelExpressionSeparator(current))
            {
                start = index + 1;
            }
        }

        while (start < dotIndex && char.IsWhiteSpace(buffer[start]))
        {
            start++;
        }

        return start;
    }

    private static bool IsTopLevelExpressionSeparator(char value)
    {
        return value == ',' ||
               value == '?' ||
               value == ':' ||
               value == '+' ||
               value == '-' ||
               value == '*' ||
               value == '/' ||
               value == '%' ||
               value == '&' ||
               value == '|' ||
               value == '^' ||
               value == '=' ||
               value == '!' ||
               value == '<' ||
               value == '>' ||
               value == ';';
    }

    private static bool TryInferFormulaReceiverHint(
        DocWorkspace workspace,
        DocTable currentTable,
        ReadOnlySpan<char> expression,
        out FormulaReceiverHint receiverHint)
    {
        receiverHint = default;
        expression = expression.Trim();
        if (expression.Length == 0)
        {
            return false;
        }

        if (TryInferCallExpressionHint(workspace, currentTable, expression, out receiverHint))
        {
            return true;
        }

        int memberDotIndex = FindLastTopLevelDot(expression);
        if (memberDotIndex > 0)
        {
            ReadOnlySpan<char> left = expression[..memberDotIndex].Trim();
            ReadOnlySpan<char> right = expression[(memberDotIndex + 1)..].Trim();
            if (right.Length == 0 || !IsValidFormulaIdentifier(right))
            {
                return false;
            }

            if (!TryInferFormulaReceiverHint(workspace, currentTable, left, out var leftHint))
            {
                return false;
            }

            if (leftHint.Kind == FormulaReceiverKind.RowReference &&
                leftHint.Table != null &&
                TryGetColumnByName(leftHint.Table, right, out var rowColumn))
            {
                if (rowColumn.Kind == DocColumnKind.Relation)
                {
                    string? relationTargetTableId = DocRelationTargetResolver.ResolveTargetTableId(leftHint.Table, rowColumn);
                    if (!string.IsNullOrEmpty(relationTargetTableId) &&
                        TryGetProjectTableById(workspace, relationTargetTableId, out var relationTable))
                    {
                        receiverHint = new FormulaReceiverHint(FormulaReceiverKind.RowReference, relationTable);
                        return true;
                    }
                }
                else if (rowColumn.Kind == DocColumnKind.Subtable &&
                         !string.IsNullOrWhiteSpace(rowColumn.SubtableId) &&
                         TryGetProjectTableById(workspace, rowColumn.SubtableId, out var subtableTargetTable))
                {
                    bool hasParentRowBinding = !string.IsNullOrWhiteSpace(subtableTargetTable.ParentRowColumnId);
                    receiverHint = hasParentRowBinding
                        ? new FormulaReceiverHint(FormulaReceiverKind.RowCollection, subtableTargetTable)
                        : new FormulaReceiverHint(FormulaReceiverKind.TableReference, subtableTargetTable);
                    return true;
                }
                else if (rowColumn.Kind == DocColumnKind.TableRef)
                {
                    receiverHint = new FormulaReceiverHint(FormulaReceiverKind.TableReference, null);
                    return true;
                }

                receiverHint = new FormulaReceiverHint(FormulaReceiverKind.Scalar, null);
                return true;
            }

            if (leftHint.Kind == FormulaReceiverKind.DocumentNamespace)
            {
                if (TryGetProjectDocumentByAlias(workspace, right, out var document))
                {
                    receiverHint = new FormulaReceiverHint(FormulaReceiverKind.DocumentReference, document: document);
                    return true;
                }

                return false;
            }

            if (leftHint.Kind == FormulaReceiverKind.DocumentReference)
            {
                receiverHint = new FormulaReceiverHint(FormulaReceiverKind.Scalar, null);
                return true;
            }

            return false;
        }

        if (expression.Equals("thisRow".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            receiverHint = new FormulaReceiverHint(FormulaReceiverKind.RowReference, currentTable);
            return true;
        }

        if (expression.Equals("parentRow".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(currentTable.ParentTableId) &&
                TryGetProjectTableById(workspace, currentTable.ParentTableId, out var parentTable))
            {
                receiverHint = new FormulaReceiverHint(FormulaReceiverKind.RowReference, parentTable);
                return true;
            }

            return false;
        }

        if (expression.Equals("parentTable".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(currentTable.ParentTableId) &&
                TryGetProjectTableById(workspace, currentTable.ParentTableId, out var parentTable))
            {
                receiverHint = new FormulaReceiverHint(FormulaReceiverKind.TableReference, parentTable);
                return true;
            }

            return false;
        }

        if (expression.Equals("docs".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            receiverHint = new FormulaReceiverHint(FormulaReceiverKind.DocumentNamespace);
            return true;
        }

        if (expression.Equals("thisDoc".AsSpan(), StringComparison.OrdinalIgnoreCase) &&
            workspace.ActiveDocument != null)
        {
            receiverHint = new FormulaReceiverHint(FormulaReceiverKind.DocumentReference, document: workspace.ActiveDocument);
            return true;
        }

        if (TryGetProjectTableByName(workspace, expression, out var projectTable))
        {
            receiverHint = new FormulaReceiverHint(FormulaReceiverKind.TableReference, projectTable);
            return true;
        }

        return false;
    }

    private static bool TryInferCallExpressionHint(
        DocWorkspace workspace,
        DocTable currentTable,
        ReadOnlySpan<char> expression,
        out FormulaReceiverHint receiverHint)
    {
        receiverHint = default;
        if (expression.Length < 3 || expression[^1] != ')')
        {
            return false;
        }

        if (!TryFindMatchingOpenParen(expression, expression.Length - 1, out int openParenIndex))
        {
            return false;
        }

        int nameEndExclusive = openParenIndex;
        while (nameEndExclusive > 0 && char.IsWhiteSpace(expression[nameEndExclusive - 1]))
        {
            nameEndExclusive--;
        }

        int nameStart = nameEndExclusive;
        while (nameStart > 0 && IsFormulaIdentifierChar(expression[nameStart - 1]))
        {
            nameStart--;
        }

        if (nameStart >= nameEndExclusive)
        {
            return false;
        }

        ReadOnlySpan<char> callName = expression[nameStart..nameEndExclusive];
        ReadOnlySpan<char> argsSpan = expression[(openParenIndex + 1)..^1];
        int argumentCount = CountTopLevelArguments(argsSpan);

        int beforeNameIndex = nameStart - 1;
        while (beforeNameIndex >= 0 && char.IsWhiteSpace(expression[beforeNameIndex]))
        {
            beforeNameIndex--;
        }

        if (beforeNameIndex >= 0 && expression[beforeNameIndex] == '.')
        {
            ReadOnlySpan<char> receiverExpression = expression[..beforeNameIndex].Trim();
            if (!TryInferFormulaReceiverHint(workspace, currentTable, receiverExpression, out var targetHint))
            {
                return false;
            }

            return TryInferMethodCallResult(callName, targetHint, out receiverHint);
        }

        return TryInferFunctionCallResult(workspace, currentTable, callName, argsSpan, argumentCount, out receiverHint);
    }

    private static bool TryInferMethodCallResult(
        ReadOnlySpan<char> methodName,
        FormulaReceiverHint targetHint,
        out FormulaReceiverHint receiverHint)
    {
        receiverHint = default;
        if (targetHint.Table == null)
        {
            return false;
        }

        bool supportsRowCollectionMethods =
            targetHint.Kind == FormulaReceiverKind.TableReference ||
            targetHint.Kind == FormulaReceiverKind.RowCollection;
        if (!supportsRowCollectionMethods)
        {
            return false;
        }

        if (methodName.Equals("Filter".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            methodName.Equals("Sort".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            receiverHint = new FormulaReceiverHint(FormulaReceiverKind.RowCollection, targetHint.Table);
            return true;
        }

        if (methodName.Equals("First".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            receiverHint = new FormulaReceiverHint(FormulaReceiverKind.RowReference, targetHint.Table);
            return true;
        }

        if (methodName.Equals("Count".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            methodName.Equals("Sum".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            methodName.Equals("Average".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            receiverHint = new FormulaReceiverHint(FormulaReceiverKind.Scalar, null);
            return true;
        }

        return false;
    }

    private static bool TryInferFunctionCallResult(
        DocWorkspace workspace,
        DocTable currentTable,
        ReadOnlySpan<char> functionName,
        ReadOnlySpan<char> argsSpan,
        int argumentCount,
        out FormulaReceiverHint receiverHint)
    {
        receiverHint = default;
        if (functionName.Equals("Lookup".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            if (argumentCount >= 3)
            {
                receiverHint = new FormulaReceiverHint(FormulaReceiverKind.Scalar, null);
                return true;
            }

            if (TryGetTopLevelArgument(argsSpan, 0, out var firstArgument) &&
                TryInferFormulaReceiverHint(workspace, currentTable, firstArgument, out var firstArgumentHint) &&
                (firstArgumentHint.Kind == FormulaReceiverKind.TableReference ||
                 firstArgumentHint.Kind == FormulaReceiverKind.RowCollection) &&
                firstArgumentHint.Table != null)
            {
                receiverHint = new FormulaReceiverHint(FormulaReceiverKind.RowReference, firstArgumentHint.Table);
                return true;
            }

            return false;
        }

        if (functionName.Equals("CountIf".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            functionName.Equals("SumIf".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            functionName.Equals("If".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            functionName.Equals("Abs".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            functionName.Equals("Pow".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            functionName.Equals("Exp".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            functionName.Equals("Upper".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            functionName.Equals("Lower".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            functionName.Equals("Contains".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            functionName.Equals("Concat".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            functionName.Equals("Date".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            functionName.Equals("Today".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            functionName.Equals("AddDays".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            functionName.Equals("DaysBetween".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            receiverHint = new FormulaReceiverHint(FormulaReceiverKind.Scalar, null);
            return true;
        }

        if (FormulaFunctionRegistry.Contains(functionName))
        {
            receiverHint = new FormulaReceiverHint(FormulaReceiverKind.Scalar, null);
            return true;
        }

        return false;
    }

    private static int FindLastTopLevelDot(ReadOnlySpan<char> expression)
    {
        int depth = 0;
        bool inString = false;
        bool escaped = false;
        int lastDotIndex = -1;
        for (int index = 0; index < expression.Length; index++)
        {
            char current = expression[index];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (current == '"')
            {
                inString = true;
                continue;
            }

            if (current == '(')
            {
                depth++;
                continue;
            }

            if (current == ')' && depth > 0)
            {
                depth--;
                continue;
            }

            if (depth == 0 && current == '.')
            {
                lastDotIndex = index;
            }
        }

        return lastDotIndex;
    }

    private static bool TryFindMatchingOpenParen(ReadOnlySpan<char> expression, int closeParenIndex, out int openParenIndex)
    {
        int depth = 0;
        bool inString = false;
        bool escaped = false;
        for (int index = closeParenIndex; index >= 0; index--)
        {
            char current = expression[index];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (current == '"')
            {
                inString = true;
                continue;
            }

            if (current == ')')
            {
                depth++;
                continue;
            }

            if (current != '(')
            {
                continue;
            }

            depth--;
            if (depth == 0)
            {
                openParenIndex = index;
                return true;
            }
        }

        openParenIndex = -1;
        return false;
    }

    private static int CountTopLevelArguments(ReadOnlySpan<char> argumentsSpan)
    {
        ReadOnlySpan<char> trimmed = argumentsSpan.Trim();
        if (trimmed.Length == 0)
        {
            return 0;
        }

        int depth = 0;
        bool inString = false;
        bool escaped = false;
        int count = 1;
        for (int index = 0; index < trimmed.Length; index++)
        {
            char current = trimmed[index];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (current == '"')
            {
                inString = true;
                continue;
            }

            if (current == '(')
            {
                depth++;
                continue;
            }

            if (current == ')' && depth > 0)
            {
                depth--;
                continue;
            }

            if (depth == 0 && current == ',')
            {
                count++;
            }
        }

        return count;
    }

    private static bool TryGetTopLevelArgument(ReadOnlySpan<char> argumentsSpan, int argumentIndex, out ReadOnlySpan<char> argument)
    {
        argument = ReadOnlySpan<char>.Empty;
        ReadOnlySpan<char> trimmed = argumentsSpan.Trim();
        if (trimmed.Length == 0 || argumentIndex < 0)
        {
            return false;
        }

        int depth = 0;
        bool inString = false;
        bool escaped = false;
        int currentArgument = 0;
        int currentStart = 0;
        for (int index = 0; index <= trimmed.Length; index++)
        {
            bool atEnd = index == trimmed.Length;
            char current = atEnd ? ',' : trimmed[index];
            if (!atEnd)
            {
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (current == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (current == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (current == '"')
                {
                    inString = true;
                    continue;
                }

                if (current == '(')
                {
                    depth++;
                    continue;
                }

                if (current == ')' && depth > 0)
                {
                    depth--;
                    continue;
                }
            }

            if (!atEnd && (depth > 0 || current != ','))
            {
                continue;
            }

            if (currentArgument == argumentIndex)
            {
                argument = trimmed[currentStart..index].Trim();
                return argument.Length > 0;
            }

            currentArgument++;
            currentStart = index + 1;
        }

        return false;
    }

    private static bool TryGetRelationTargetTableForMember(
        DocWorkspace workspace,
        DocTable table,
        ReadOnlySpan<char> receiverIdentifier,
        out DocTable relationTargetTable)
    {
        relationTargetTable = null!;
        if (receiverIdentifier.Length == 0)
        {
            return false;
        }

        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            var column = table.Columns[columnIndex];
            if (column.Kind != DocColumnKind.Relation)
            {
                continue;
            }

            if (!column.Name.AsSpan().Equals(receiverIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? relationTargetTableId = DocRelationTargetResolver.ResolveTargetTableId(table, column);
            if (string.IsNullOrWhiteSpace(relationTargetTableId))
            {
                continue;
            }

            for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
            {
                var candidateTable = workspace.Project.Tables[tableIndex];
                if (candidateTable.Id == relationTargetTableId)
                {
                    relationTargetTable = candidateTable;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryGetSubtableReceiverHintForMember(
        DocWorkspace workspace,
        DocTable table,
        ReadOnlySpan<char> receiverIdentifier,
        out FormulaReceiverHint receiverHint)
    {
        receiverHint = default;
        if (receiverIdentifier.Length == 0)
        {
            return false;
        }

        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            DocColumn column = table.Columns[columnIndex];
            if (column.Kind != DocColumnKind.Subtable ||
                !column.Name.AsSpan().Equals(receiverIdentifier, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(column.SubtableId) ||
                !TryGetProjectTableById(workspace, column.SubtableId, out DocTable subtableTable))
            {
                continue;
            }

            bool hasParentRowBinding = !string.IsNullOrWhiteSpace(subtableTable.ParentRowColumnId);
            receiverHint = hasParentRowBinding
                ? new FormulaReceiverHint(FormulaReceiverKind.RowCollection, subtableTable)
                : new FormulaReceiverHint(FormulaReceiverKind.TableReference, subtableTable);
            return true;
        }

        return false;
    }

    private static bool TryGetTableRefReceiverHintForMember(
        DocTable table,
        ReadOnlySpan<char> receiverIdentifier)
    {
        if (receiverIdentifier.Length == 0)
        {
            return false;
        }

        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            DocColumn column = table.Columns[columnIndex];
            if (column.Kind != DocColumnKind.TableRef)
            {
                continue;
            }

            if (!column.Name.AsSpan().Equals(receiverIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static ReadOnlySpan<char> GetLeftIdentifierBeforeDot(char[] buffer, int dotIndex)
    {
        if (dotIndex < 1)
        {
            return ReadOnlySpan<char>.Empty;
        }

        int end = dotIndex;
        int start = end;
        while (start > 0 && IsFormulaIdentifierChar(buffer[start - 1]))
        {
            start--;
        }

        if (start >= end)
        {
            return ReadOnlySpan<char>.Empty;
        }

        return buffer.AsSpan(start, end - start);
    }

    private static bool ReplaceBufferRange(char[] buffer, ref int length, int maxLength, int start, int end, ReadOnlySpan<char> insertText)
    {
        if (start < 0 || end < start || end > length)
        {
            return false;
        }

        int removedLength = end - start;
        int addedLength = insertText.Length;
        int delta = addedLength - removedLength;
        if (length + delta > maxLength)
        {
            return false;
        }

        if (delta > 0)
        {
            for (int sourceIndex = length - 1; sourceIndex >= end; sourceIndex--)
            {
                buffer[sourceIndex + delta] = buffer[sourceIndex];
            }
        }
        else if (delta < 0)
        {
            for (int sourceIndex = end; sourceIndex < length; sourceIndex++)
            {
                buffer[sourceIndex + delta] = buffer[sourceIndex];
            }
        }

        for (int insertIndex = 0; insertIndex < addedLength; insertIndex++)
        {
            buffer[start + insertIndex] = insertText[insertIndex];
        }

        length += delta;
        return true;
    }

    private static uint GetFormulaTokenBackground(FormulaDisplayTokenKind tokenKind)
    {
        var style = Im.Style;
        return tokenKind switch
        {
            FormulaDisplayTokenKind.Function => BlendColor(style.Primary, 0.32f, style.Surface),
            FormulaDisplayTokenKind.Method => BlendColor(style.Secondary, 0.32f, style.Surface),
            FormulaDisplayTokenKind.Column => BlendColor(style.Active, 0.34f, style.Surface),
            FormulaDisplayTokenKind.Table => BlendColor(style.Primary, 0.20f, style.Surface),
            FormulaDisplayTokenKind.Document => BlendColor(style.Secondary, 0.28f, style.Surface),
            FormulaDisplayTokenKind.Keyword => BlendColor(style.Border, 0.40f, style.Surface),
            FormulaDisplayTokenKind.Number => BlendColor(style.Secondary, 0.20f, style.Surface),
            FormulaDisplayTokenKind.String => BlendColor(style.Primary, 0.18f, style.Surface),
            _ => style.Surface,
        };
    }

    private static uint GetFormulaCompletionBackground(FormulaCompletionKind completionKind)
    {
        var style = Im.Style;
        return completionKind switch
        {
            FormulaCompletionKind.Function => BlendColor(style.Primary, 0.36f, style.Surface),
            FormulaCompletionKind.Method => BlendColor(style.Secondary, 0.36f, style.Surface),
            FormulaCompletionKind.Column => BlendColor(style.Active, 0.38f, style.Surface),
            FormulaCompletionKind.Table => BlendColor(style.Primary, 0.24f, style.Surface),
            FormulaCompletionKind.Document => BlendColor(style.Secondary, 0.30f, style.Surface),
            FormulaCompletionKind.Keyword => BlendColor(style.Border, 0.44f, style.Surface),
            _ => BlendColor(style.Border, 0.30f, style.Surface),
        };
    }

    private static string GetFormulaCompletionKindLabel(FormulaCompletionKind completionKind)
    {
        return completionKind switch
        {
            FormulaCompletionKind.Function => "fn",
            FormulaCompletionKind.Method => "method",
            FormulaCompletionKind.Column => "col",
            FormulaCompletionKind.Table => "table",
            FormulaCompletionKind.Document => "doc",
            FormulaCompletionKind.Keyword => "kw",
            _ => "item",
        };
    }

    private static bool IsFormulaPillTokenKind(FormulaDisplayTokenKind tokenKind)
    {
        return tokenKind == FormulaDisplayTokenKind.Function ||
               tokenKind == FormulaDisplayTokenKind.Method ||
               tokenKind == FormulaDisplayTokenKind.Column ||
               tokenKind == FormulaDisplayTokenKind.Table ||
               tokenKind == FormulaDisplayTokenKind.Document ||
               tokenKind == FormulaDisplayTokenKind.Keyword;
    }

    private static float DrawFormulaInspectorPanel(
        DocWorkspace workspace,
        DocTable table,
        DocColumn formulaColumn,
        char[] formulaExpressionBuffer,
        int formulaExpressionLength,
        float x,
        float y,
        float width)
    {
        var style = Im.Style;
        float panelHeight = FormulaInspectorPanelHeight;
        var panelRect = new ImRect(x, y, width, panelHeight);
        Im.DrawRoundedRect(panelRect.X, panelRect.Y, panelRect.Width, panelRect.Height, 6f, BlendColor(style.Surface, 0.22f, style.Background));
        Im.DrawRoundedRectStroke(panelRect.X, panelRect.Y, panelRect.Width, panelRect.Height, 6f, BlendColor(style.Border, 0.62f, style.Surface), 1f);

        if (!_formulaInspectorState.HasValue)
        {
            Im.Text("Click a token pill for details".AsSpan(), panelRect.X + 10f, panelRect.Y + 10f, style.FontSize, style.TextSecondary);
            return panelHeight;
        }

        float textX = panelRect.X + 10f;
        float lineY = panelRect.Y + 10f;
        float textMaxWidth = panelRect.Width - 20f;
        int rowCount = table.Rows.Count;
        float textBottomY = rowCount > 0 ? panelRect.Bottom - 40f : panelRect.Bottom - 8f;
        Im.Text(_formulaInspectorState.Title.AsSpan(), textX, lineY, style.FontSize, BlendColor(style.Primary, 0.65f, style.TextPrimary));
        lineY += style.FontSize + 6f;
        lineY += DrawWrappedTextBlock(
            _formulaInspectorState.Description.AsSpan(),
            textX,
            lineY,
            textMaxWidth,
            style.FontSize,
            style.TextPrimary,
            textBottomY);
        if (!string.IsNullOrWhiteSpace(_formulaInspectorState.Preview))
        {
            lineY += 4f;
            lineY += DrawWrappedTextBlock(
                _formulaInspectorState.Preview.AsSpan(),
                textX,
                lineY,
                textMaxWidth,
                style.FontSize - 1f,
                style.TextSecondary,
                textBottomY);
        }

        if (rowCount > 0)
        {
            _formulaInspectorPreviewRowIndex = Math.Clamp(_formulaInspectorPreviewRowIndex, 0, rowCount - 1);
            float buttonWidth = 20f;
            float previewRowHeight = 30f;
            float previewRowX = panelRect.X + 10f;
            float previewRowY = panelRect.Bottom - previewRowHeight - 8f;
            float previewRowWidth = panelRect.Width - 20f;
            float buttonHeight = previewRowHeight - 8f;
            Im.DrawRoundedRect(previewRowX, previewRowY, previewRowWidth, previewRowHeight, 5f, BlendColor(style.Background, 0.22f, style.Surface));
            Im.DrawRoundedRectStroke(previewRowX, previewRowY, previewRowWidth, previewRowHeight, 5f, BlendColor(style.Border, 0.62f, style.Surface), 1f);

            float nextButtonX = previewRowX + previewRowWidth - 6f - buttonWidth;
            float prevButtonX = nextButtonX - 4f - buttonWidth;
            float controlsY = previewRowY + 4f;

            if (Im.Button("<##formula_prev_row", prevButtonX, controlsY, buttonWidth, buttonHeight))
            {
                _formulaInspectorPreviewRowIndex = Math.Max(0, _formulaInspectorPreviewRowIndex - 1);
            }

            if (Im.Button(">##formula_next_row", nextButtonX, controlsY, buttonWidth, buttonHeight))
            {
                _formulaInspectorPreviewRowIndex = Math.Min(rowCount - 1, _formulaInspectorPreviewRowIndex + 1);
            }

            Span<char> rowLabelBuffer = stackalloc char[48];
            int rowLabelLength = WriteFormulaRowLabel(_formulaInspectorPreviewRowIndex + 1, rowCount, rowLabelBuffer);
            float rowLabelWidth = Im.MeasureTextWidth(rowLabelBuffer[..rowLabelLength], style.FontSize - 1f);
            Im.Text(
                rowLabelBuffer[..rowLabelLength],
                prevButtonX - 8f - rowLabelWidth,
                controlsY + (buttonHeight - (style.FontSize - 1f)) * 0.5f,
                style.FontSize - 1f,
                style.TextSecondary);

            string previewValue = BuildFormulaInspectorPreviewValue(
                workspace,
                table,
                formulaColumn,
                formulaExpressionBuffer,
                formulaExpressionLength,
                _formulaInspectorPreviewRowIndex);
            if (previewValue.Length > 0)
            {
                float previewY = previewRowY + (previewRowHeight - style.FontSize) * 0.5f;
                Im.Text("=".AsSpan(), previewRowX + 10f, previewY, style.FontSize, style.TextSecondary);
                Im.Text(previewValue.AsSpan(), previewRowX + 22f, previewY, style.FontSize, style.TextPrimary);
            }
        }

        return panelHeight;
    }

    private static void SetFormulaInspectorFromToken(
        DocWorkspace workspace,
        DocTable table,
        FormulaDisplayTokenKind tokenKind,
        ReadOnlySpan<char> tokenText)
    {
        if (tokenText.Length == 0)
        {
            return;
        }

        string token = tokenText.ToString();
        if (tokenKind == FormulaDisplayTokenKind.Function)
        {
            if (TryGetFormulaFunctionDoc(token.AsSpan(), out string functionDescription, out string functionPreview))
            {
                SetFormulaInspector($"{token}()", functionDescription, functionPreview, tokenKind, token);
                return;
            }

            SetFormulaInspector($"{token}()", "Function call.", "", tokenKind, token);
            return;
        }

        if (tokenKind == FormulaDisplayTokenKind.Method)
        {
            if (TryGetFormulaMethodDoc(token.AsSpan(), out string methodDescription, out string methodPreview))
            {
                SetFormulaInspector($".{token}()", methodDescription, methodPreview, tokenKind, token);
                return;
            }

            SetFormulaInspector($".{token}()", "Collection method.", "", tokenKind, token);
            return;
        }

        if (tokenKind == FormulaDisplayTokenKind.Table)
        {
            if (TryGetTableDoc(workspace, token, out string tableDescription, out string tablePreview))
            {
                SetFormulaInspector(token, tableDescription, tablePreview, tokenKind, token);
                return;
            }

            SetFormulaInspector(token, "Table reference.", "", tokenKind, token);
            return;
        }

        if (tokenKind == FormulaDisplayTokenKind.Document)
        {
            if (TryGetDocumentDoc(workspace, token, out string documentDescription, out string documentPreview))
            {
                SetFormulaInspector(token, documentDescription, documentPreview, tokenKind, token);
                return;
            }

            SetFormulaInspector(token, "Document scope reference.", "Use docs.<documentAlias>.<variable>", tokenKind, token);
            return;
        }

        if (tokenKind == FormulaDisplayTokenKind.Column)
        {
            if (string.Equals(token, "@rowIndex", StringComparison.OrdinalIgnoreCase))
            {
                SetFormulaInspector("@rowIndex", "1-based index of the candidate row in table scans (Lookup/Filter/CountIf/SumIf).", "Example: Lookup(Tasks, @rowIndex == thisRow.TargetRow, @Name)", tokenKind, token);
                return;
            }

            if (token.StartsWith("@", StringComparison.Ordinal))
            {
                token = token[1..];
            }

            if (string.Equals(token, "rowIndex", StringComparison.OrdinalIgnoreCase))
            {
                SetFormulaInspector("rowIndex", "1-based index of a row object. Works on thisRow, parentRow, and relation rows.", "Example: thisRow.rowIndex", tokenKind, token);
                return;
            }

            if (TryGetColumnDoc(table, token, out string columnDescription, out string columnPreview))
            {
                SetFormulaInspector(token, columnDescription, columnPreview, tokenKind, token);
                return;
            }

            SetFormulaInspector(token, "Column reference.", "", tokenKind, token);
            return;
        }

        if (tokenKind == FormulaDisplayTokenKind.Keyword)
        {
            if (string.Equals(token, "thisRow", StringComparison.OrdinalIgnoreCase))
            {
                SetFormulaInspector("thisRow", "Current row object. Use dot access for fields, e.g. thisRow.Priority.", "Example: thisRow.Done ? 0 : 1", tokenKind, token);
                return;
            }

            if (string.Equals(token, "parentRow", StringComparison.OrdinalIgnoreCase))
            {
                SetFormulaInspector("parentRow", "Parent row object for subtable formulas. Use dot access for parent fields.", "Example: parentRow.BaseWeight", tokenKind, token);
                return;
            }

            if (string.Equals(token, "parentTable", StringComparison.OrdinalIgnoreCase))
            {
                SetFormulaInspector("parentTable", "Parent table for subtable formulas. Supports row-collection methods.", "Example: parentTable.Filter(@Type == parentRow.Type).Count()", tokenKind, token);
                return;
            }

            if (string.Equals(token, "docs", StringComparison.OrdinalIgnoreCase))
            {
                SetFormulaInspector("docs", "Root namespace for document-scoped variables.", "Example: docs.balance_sheet.total_revenue", tokenKind, token);
                return;
            }

            if (string.Equals(token, "thisDoc", StringComparison.OrdinalIgnoreCase))
            {
                SetFormulaInspector("thisDoc", "Current document namespace. Primarily for document formulas.", "Example: thisDoc.tax_rate", tokenKind, token);
                return;
            }

            if (string.Equals(token, "thisRowIndex", StringComparison.OrdinalIgnoreCase))
            {
                SetFormulaInspector("thisRowIndex", "1-based index of the current row.", "Example: thisRowIndex + 1", tokenKind, token);
                return;
            }

            if (string.Equals(token, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "false", StringComparison.OrdinalIgnoreCase))
            {
                SetFormulaInspector(token, "Boolean literal.", "Use in conditions like If(condition, true, false)", tokenKind, token);
                return;
            }
        }
    }

    private static void SetFormulaInspectorFromCompletion(
        DocWorkspace workspace,
        DocTable table,
        FormulaCompletionEntry completionEntry)
    {
        string completionText = completionEntry.InsertText;
        if (completionEntry.Kind == FormulaCompletionKind.Function || completionEntry.Kind == FormulaCompletionKind.Method)
        {
            int parenIndex = completionText.IndexOf('(');
            if (parenIndex > 0)
            {
                completionText = completionText[..parenIndex];
            }
        }

        FormulaDisplayTokenKind tokenKind = completionEntry.Kind switch
        {
            FormulaCompletionKind.Function => FormulaDisplayTokenKind.Function,
            FormulaCompletionKind.Method => FormulaDisplayTokenKind.Method,
            FormulaCompletionKind.Column => FormulaDisplayTokenKind.Column,
            FormulaCompletionKind.Table => FormulaDisplayTokenKind.Table,
            FormulaCompletionKind.Document => FormulaDisplayTokenKind.Document,
            FormulaCompletionKind.Keyword => FormulaDisplayTokenKind.Keyword,
            _ => FormulaDisplayTokenKind.Plain,
        };

        SetFormulaInspectorFromToken(workspace, table, tokenKind, completionText.AsSpan());
    }

    private static void SetFormulaInspector(string title, string description, string preview)
    {
        SetFormulaInspector(title, description, preview, FormulaDisplayTokenKind.Plain, "");
    }

    private static void SetFormulaInspector(
        string title,
        string description,
        string preview,
        FormulaDisplayTokenKind contextKind,
        string contextToken)
    {
        _formulaInspectorState = new FormulaInspectorState
        {
            HasValue = true,
            Title = title,
            Description = description,
            Preview = preview,
            ContextKind = contextKind,
            ContextToken = contextToken,
        };
    }

    private static int WriteFormulaRowLabel(int rowOneBased, int rowCount, Span<char> buffer)
    {
        const string prefix = "Row ";
        const string middle = " of ";

        int index = 0;
        prefix.AsSpan().CopyTo(buffer[index..]);
        index += prefix.Length;

        if (!rowOneBased.TryFormat(buffer[index..], out int rowWritten))
        {
            return 0;
        }

        index += rowWritten;
        middle.AsSpan().CopyTo(buffer[index..]);
        index += middle.Length;

        if (!rowCount.TryFormat(buffer[index..], out int countWritten))
        {
            return 0;
        }

        index += countWritten;
        return index;
    }

    private static string BuildFormulaInspectorPreviewValue(
        DocWorkspace workspace,
        DocTable table,
        DocColumn formulaColumn,
        char[] formulaExpressionBuffer,
        int formulaExpressionLength,
        int rowIndex)
    {
        if (table.Rows.Count == 0)
        {
            return "";
        }

        int clampedDisplayIndex = Math.Clamp(rowIndex, 0, _rowCount - 1);
        int sourceRowIndex = GetSourceRowIndex(clampedDisplayIndex);
        sourceRowIndex = Math.Clamp(sourceRowIndex, 0, table.Rows.Count - 1);
        var row = table.Rows[sourceRowIndex];
        string formulaExpression = formulaExpressionLength > 0
            ? new string(formulaExpressionBuffer, 0, formulaExpressionLength)
            : "0";

        _formulaPreviewEngine.TryEvaluateExpression(
            workspace.Project,
            table,
            row,
            formulaColumn,
            formulaExpression,
            out var previewCellValue);

        return GetFormulaInspectorPreviewValueText(formulaColumn, previewCellValue);
    }

    private static string GetFormulaInspectorCellDisplayText(
        DocWorkspace workspace,
        DocTable table,
        DocRow row,
        DocColumn column)
    {
        var cell = row.GetCell(column);
        if (IsFormulaErrorCell(column, cell))
        {
            return "#ERR";
        }

        return column.Kind switch
        {
            DocColumnKind.Id => string.IsNullOrWhiteSpace(cell.StringValue) ? row.Id : cell.StringValue,
            DocColumnKind.Number => cell.NumberValue.ToString("G"),
            DocColumnKind.Checkbox => cell.BoolValue ? "true" : "false",
            DocColumnKind.Relation => workspace.ResolveRelationDisplayLabel(column, cell.StringValue ?? ""),
            DocColumnKind.TableRef => ResolveTableRefLabel(workspace, cell.StringValue ?? ""),
            DocColumnKind.Formula => string.IsNullOrEmpty(cell.StringValue) ? cell.NumberValue.ToString("G") : cell.StringValue,
            DocColumnKind.Vec2 => FormatVectorCellText(cell, 2),
            DocColumnKind.Vec3 => FormatVectorCellText(cell, 3),
            DocColumnKind.Vec4 => FormatVectorCellText(cell, 4),
            DocColumnKind.Color => FormatColorCellText(cell),
            _ => cell.StringValue ?? "",
        };
    }

    private static string GetFormulaInspectorPreviewValueText(DocColumn column, DocCellValue previewCellValue)
    {
        if (IsFormulaErrorCell(column, previewCellValue))
        {
            return "#ERR";
        }

        return column.Kind switch
        {
            DocColumnKind.Number => previewCellValue.NumberValue.ToString("G"),
            DocColumnKind.Checkbox => previewCellValue.BoolValue ? "true" : "false",
            DocColumnKind.Vec2 => FormatVectorCellText(previewCellValue, 2),
            DocColumnKind.Vec3 => FormatVectorCellText(previewCellValue, 3),
            DocColumnKind.Vec4 => FormatVectorCellText(previewCellValue, 4),
            DocColumnKind.Color => FormatColorCellText(previewCellValue),
            _ => previewCellValue.StringValue ?? ""
        };
    }

    private static string GetFormulaInspectorRowLabel(DocTable table, DocRow row)
    {
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            var column = table.Columns[columnIndex];
            if (column.Kind != DocColumnKind.Id &&
                column.Kind != DocColumnKind.Text &&
                column.Kind != DocColumnKind.Select &&
                column.Kind != DocColumnKind.TableRef &&
                column.Kind != DocColumnKind.TextureAsset &&
                column.Kind != DocColumnKind.MeshAsset &&
                column.Kind != DocColumnKind.AudioAsset &&
                column.Kind != DocColumnKind.UiAsset)
            {
                continue;
            }

            var value = row.GetCell(column).StringValue;
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return row.Id;
    }

    private static bool TryGetColumnByName(DocTable table, string columnName, out DocColumn column)
    {
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            var candidate = table.Columns[columnIndex];
            if (string.Equals(candidate.Name, columnName, StringComparison.OrdinalIgnoreCase))
            {
                column = candidate;
                return true;
            }
        }

        column = null!;
        return false;
    }

    private static bool TryGetColumnByName(DocTable table, ReadOnlySpan<char> columnName, out DocColumn column)
    {
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            var candidate = table.Columns[columnIndex];
            if (candidate.Name.AsSpan().Equals(columnName, StringComparison.OrdinalIgnoreCase))
            {
                column = candidate;
                return true;
            }
        }

        column = null!;
        return false;
    }

    private static bool TryGetProjectTableByName(DocWorkspace workspace, string tableName, out DocTable table)
    {
        for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
        {
            var candidate = workspace.Project.Tables[tableIndex];
            if (string.Equals(candidate.Name, tableName, StringComparison.OrdinalIgnoreCase))
            {
                table = candidate;
                return true;
            }
        }

        table = null!;
        return false;
    }

    private static bool TryGetProjectTableByName(DocWorkspace workspace, ReadOnlySpan<char> tableName, out DocTable table)
    {
        for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
        {
            var candidate = workspace.Project.Tables[tableIndex];
            if (candidate.Name.AsSpan().Equals(tableName, StringComparison.OrdinalIgnoreCase))
            {
                table = candidate;
                return true;
            }
        }

        table = null!;
        return false;
    }

    private static bool TryGetProjectTableById(DocWorkspace workspace, string tableId, out DocTable table)
    {
        for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
        {
            var candidate = workspace.Project.Tables[tableIndex];
            if (string.Equals(candidate.Id, tableId, StringComparison.Ordinal))
            {
                table = candidate;
                return true;
            }
        }

        table = null!;
        return false;
    }

    private static bool TryGetProjectDocumentByAlias(DocWorkspace workspace, string alias, out DocDocument document)
    {
        return TryGetProjectDocumentByAlias(workspace, alias.AsSpan(), out document);
    }

    private static bool TryGetProjectDocumentByAlias(DocWorkspace workspace, ReadOnlySpan<char> alias, out DocDocument document)
    {
        for (int documentIndex = 0; documentIndex < workspace.Project.Documents.Count; documentIndex++)
        {
            var candidateDocument = workspace.Project.Documents[documentIndex];
            string aliasFromFileName = DocumentFormulaSyntax.NormalizeDocumentAlias(candidateDocument.FileName);
            if (aliasFromFileName.AsSpan().Equals(alias, StringComparison.OrdinalIgnoreCase))
            {
                document = candidateDocument;
                return true;
            }

            string aliasFromTitle = DocumentFormulaSyntax.NormalizeDocumentAlias(candidateDocument.Title);
            if (aliasFromTitle.AsSpan().Equals(alias, StringComparison.OrdinalIgnoreCase))
            {
                document = candidateDocument;
                return true;
            }
        }

        document = null!;
        return false;
    }

    private static string GetColumnKindIconText(DocColumn column)
    {
        string columnTypeId = ResolveColumnTypeId(column);
        if (!DocColumnTypeIdMapper.IsBuiltIn(columnTypeId) &&
            ColumnTypeDefinitionRegistry.TryGet(columnTypeId, out var typeDefinition) &&
            !string.IsNullOrWhiteSpace(typeDefinition.IconGlyph))
        {
            return typeDefinition.IconGlyph;
        }

        return GetColumnKindIconText(column.Kind);
    }

    private static string GetColumnKindIconText(DocColumnKind columnKind)
    {
        return columnKind switch
        {
            DocColumnKind.Id => _headerIdIconText,
            DocColumnKind.Text => _headerTextIconText,
            DocColumnKind.Number => _headerNumberIconText,
            DocColumnKind.Checkbox => _headerCheckboxIconText,
            DocColumnKind.Select => _headerSelectIconText,
            DocColumnKind.Formula => _headerFormulaIconText,
            DocColumnKind.Relation => _headerRelationIconText,
            DocColumnKind.TableRef => _headerTableRefIconText,
            DocColumnKind.Spline => _headerSplineIconText,
            DocColumnKind.TextureAsset => _headerTextureAssetIconText,
            DocColumnKind.MeshAsset => _headerMeshAssetIconText,
            DocColumnKind.AudioAsset => _headerAudioAssetIconText,
            DocColumnKind.UiAsset => _headerUiAssetIconText,
            DocColumnKind.Vec2 => _headerVec2IconText,
            DocColumnKind.Vec3 => _headerVec3IconText,
            DocColumnKind.Vec4 => _headerVec4IconText,
            DocColumnKind.Color => _headerColorIconText,
            _ => "",
        };
    }

    private static bool TryGetColumnDoc(DocTable table, string columnName, out string description, out string preview)
    {
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            var column = table.Columns[columnIndex];
            if (!string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string columnTypeId = ResolveColumnTypeId(column);
            string kindLabel;
            if (!DocColumnTypeIdMapper.IsBuiltIn(columnTypeId) &&
                ColumnTypeDefinitionRegistry.TryGet(columnTypeId, out var typeDefinition))
            {
                kindLabel = typeDefinition.DisplayName + " (" + columnTypeId + ")";
            }
            else
            {
                kindLabel = column.Kind.ToString();
            }

            description = HasFormulaExpression(column)
                ? $"Column in {table.Name}. Kind: {kindLabel}. Computed by formula."
                : $"Column in {table.Name}. Kind: {kindLabel}.";
            preview = "";
            return true;
        }

        description = "";
        preview = "";
        return false;
    }

    private static bool TryGetTableDoc(DocWorkspace workspace, string tableName, out string description, out string preview)
    {
        for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
        {
            var projectTable = workspace.Project.Tables[tableIndex];
            if (!string.Equals(projectTable.Name, tableName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            description = $"Table reference with {projectTable.Rows.Count} rows and {projectTable.Columns.Count} columns.";
            preview = "Use methods like .Filter(), .Count(), .First(), .Sum(), .Average(), .Sort()";
            return true;
        }

        description = "";
        preview = "";
        return false;
    }

    private static bool TryGetDocumentDoc(DocWorkspace workspace, string documentAlias, out string description, out string preview)
    {
        if (TryGetProjectDocumentByAlias(workspace, documentAlias, out var document))
        {
            description = $"Document reference for '{document.Title}'.";
            preview = "Use dot access to read variables: docs." + documentAlias + ".<variable>";
            return true;
        }

        description = "";
        preview = "";
        return false;
    }

    private static bool TryGetFormulaFunctionDoc(ReadOnlySpan<char> functionName, out string description, out string preview)
    {
        if (functionName.Equals("Lookup".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            description = "Finds the first row matching a predicate.";
            preview = "Lookup(TableRef, predicate, optionalValue)";
            return true;
        }
        if (functionName.Equals("CountIf".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            description = "Counts rows that match a predicate.";
            preview = "CountIf(TableRef, predicate)";
            return true;
        }
        if (functionName.Equals("SumIf".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            description = "Sums expression values for rows where predicate is true.";
            preview = "SumIf(TableRef, predicate, valueExpr)";
            return true;
        }
        if (functionName.Equals("If".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            description = "Ternary conditional function.";
            preview = "If(condition, whenTrue, whenFalse)";
            return true;
        }
        if (functionName.Equals("Abs".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            description = "Returns the absolute value.";
            preview = "Abs(value)";
            return true;
        }
        if (functionName.Equals("Pow".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            description = "Raises base to exponent.";
            preview = "Pow(base, exponent)";
            return true;
        }
        if (functionName.Equals("Exp".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            description = "Returns the natural exponential (e^x).";
            preview = "Exp(value)";
            return true;
        }
        if (functionName.Equals("Vec2".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            description = "Creates a 2D vector.";
            preview = "Vec2(x, y)";
            return true;
        }
        if (functionName.Equals("Vec3".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            description = "Creates a 3D vector.";
            preview = "Vec3(x, y, z)";
            return true;
        }
        if (functionName.Equals("Vec4".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            description = "Creates a 4D vector.";
            preview = "Vec4(x, y, z, w)";
            return true;
        }
        if (functionName.Equals("Color".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            description = "Creates a linear color value.";
            preview = "Color(r, g, b, a)";
            return true;
        }
        if (functionName.Equals("Upper".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            description = "Converts text to upper case.";
            preview = "Upper(text)";
            return true;
        }
        if (functionName.Equals("Lower".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            description = "Converts text to lower case.";
            preview = "Lower(text)";
            return true;
        }
        if (functionName.Equals("Contains".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            description = "Case-insensitive contains check.";
            preview = "Contains(text, value)";
            return true;
        }
        if (functionName.Equals("Concat".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            description = "Concatenates all arguments into a string.";
            preview = "Concat(a, b, c, ...)";
            return true;
        }
        if (functionName.Equals("Date".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            description = "Parses text into a date value.";
            preview = "Date(text)";
            return true;
        }
        if (functionName.Equals("Today".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            description = "Returns today's date.";
            preview = "Today()";
            return true;
        }
        if (functionName.Equals("AddDays".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            description = "Adds days to a date.";
            preview = "AddDays(date, days)";
            return true;
        }
        if (functionName.Equals("DaysBetween".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            description = "Returns day difference between two dates.";
            preview = "DaysBetween(startDate, endDate)";
            return true;
        }

        if (FormulaFunctionRegistry.Contains(functionName))
        {
            string functionNameText = functionName.ToString();
            description = "Plugin-defined formula function.";
            preview = functionNameText + "(...)";
            return true;
        }

        description = "";
        preview = "";
        return false;
    }

    private static bool TryGetFormulaMethodDoc(ReadOnlySpan<char> methodName, out string description, out string preview)
    {
        if (methodName.Equals("Filter".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            description = "Returns rows where predicate is true. In predicate, @Column refers to the candidate row.";
            preview = "Example: Recipes.Filter(@Calories > 300)";
            return true;
        }
        if (methodName.Equals("Count".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            description = "Counts rows in a row collection.";
            preview = "Rows.Count()";
            return true;
        }
        if (methodName.Equals("First".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            description = "Returns first row in a row collection.";
            preview = "Rows.First()";
            return true;
        }
        if (methodName.Equals("Sum".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            description = "Sums an expression over rows.";
            preview = "Example: Recipes.Filter(@Done).Sum(@Calories)";
            return true;
        }
        if (methodName.Equals("Average".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            description = "Averages an expression over rows.";
            preview = "Rows.Average(valueExpr)";
            return true;
        }
        if (methodName.Equals("Sort".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            description = "Sorts rows by an expression.";
            preview = "Rows.Sort(sortExpr)";
            return true;
        }

        description = "";
        preview = "";
        return false;
    }

    private static bool IsTableIdentifier(DocWorkspace workspace, ReadOnlySpan<char> identifier)
    {
        if (identifier.Length == 0)
        {
            return false;
        }

        for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
        {
            string tableName = workspace.Project.Tables[tableIndex].Name;
            if (!IsValidFormulaIdentifier(tableName))
            {
                continue;
            }

            if (tableName.AsSpan().Equals(identifier, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsProjectDocumentAlias(DocWorkspace workspace, ReadOnlySpan<char> identifier)
    {
        return TryGetProjectDocumentByAlias(workspace, identifier, out _);
    }

    private static bool IsColumnIdentifier(DocTable table, ReadOnlySpan<char> identifier)
    {
        if (identifier.Length == 0)
        {
            return false;
        }

        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            string columnName = table.Columns[columnIndex].Name;
            if (!IsValidFormulaIdentifier(columnName))
            {
                continue;
            }

            if (columnName.AsSpan().Equals(identifier, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static char FindPreviousNonWhitespaceChar(char[] buffer, int index)
    {
        int currentIndex = index;
        while (currentIndex >= 0)
        {
            char currentChar = buffer[currentIndex];
            if (!char.IsWhiteSpace(currentChar))
            {
                return currentChar;
            }

            currentIndex--;
        }

        return '\0';
    }

    private static char FindNextNonWhitespaceChar(char[] buffer, int length, int index)
    {
        int currentIndex = index;
        while (currentIndex < length)
        {
            char currentChar = buffer[currentIndex];
            if (!char.IsWhiteSpace(currentChar))
            {
                return currentChar;
            }

            currentIndex++;
        }

        return '\0';
    }

    private static bool IsFormulaCompletionChar(char value)
    {
        return IsFormulaIdentifierChar(value) || value == '@';
    }

    private static bool IsFormulaIdentifierStart(char value)
    {
        return char.IsLetter(value) || value == '_';
    }

    private static bool IsFormulaIdentifierChar(char value)
    {
        return char.IsLetterOrDigit(value) || value == '_';
    }

    private static bool IsValidFormulaIdentifier(string name)
    {
        return IsValidFormulaIdentifier(name.AsSpan());
    }

    private static bool IsValidFormulaIdentifier(ReadOnlySpan<char> name)
    {
        if (name.Length == 0 || !IsFormulaIdentifierStart(name[0]))
        {
            return false;
        }

        for (int charIndex = 1; charIndex < name.Length; charIndex++)
        {
            if (!IsFormulaIdentifierChar(name[charIndex]))
            {
                return false;
            }
        }

        return true;
    }

    // =====================================================================
    //  Edit helpers
    // =====================================================================

    private static void OpenEditVectorCellDialog(DocTable table, DocRow row, DocColumn column)
    {
        DocCellValue cellValue = row.GetCell(column);
        _editVectorCellTableId = table.Id;
        _editVectorCellRowId = row.Id;
        _editVectorCellColumnId = column.Id;
        _editVectorCellValidationMessage = "";
        _editVectorCellDialogOpenedFrame = Im.Context.FrameCount;
        _showEditVectorCellDialog = true;

        SetVectorComponentBuffer(_editVectorCellXBuffer, ref _editVectorCellXBufferLength, cellValue.XValue);
        SetVectorComponentBuffer(_editVectorCellYBuffer, ref _editVectorCellYBufferLength, cellValue.YValue);
        SetVectorComponentBuffer(_editVectorCellZBuffer, ref _editVectorCellZBufferLength, cellValue.ZValue);
        SetVectorComponentBuffer(_editVectorCellWBuffer, ref _editVectorCellWBufferLength, cellValue.WValue);
    }

    private static void SetVectorComponentBuffer(char[] buffer, ref int length, double value)
    {
        Array.Clear(buffer);
        string text = value.ToString("G", CultureInfo.InvariantCulture);
        length = Math.Min(text.Length, buffer.Length);
        text.AsSpan(0, length).CopyTo(buffer);
    }

    private static void BeginCellEdit(DocWorkspace workspace, DocTable table, int rowIndex, int colIndex)
    {
        var col = GetVisibleColumn(table, colIndex);
        int sourceRowIndex = GetSourceRowIndex(rowIndex);
        var row = table.Rows[sourceRowIndex];
        var cellValue = row.GetCell(col);

        if (IsColumnDataReadOnly(col))
        {
            workspace.CancelTableCellEditIfActive();
            _activeRow = -1;
            _activeCol = -1;
            return;
        }

        if (TryGetColumnUiPlugin(col, out var uiPlugin) &&
            uiPlugin.OnCellActivated(
                workspace,
                table,
                row,
                sourceRowIndex,
                rowIndex,
                colIndex,
                col,
                cellValue))
        {
            workspace.CancelTableCellEditIfActive();
            _activeRow = -1;
            _activeCol = -1;
            return;
        }

        // Subtable: navigate to child table filtered by this parent row.
        // This should work for projected derived columns too (drill-down is read-only).
        if (col.Kind == DocColumnKind.Subtable && !string.IsNullOrEmpty(col.SubtableId))
        {
            string normalizedRendererId = NormalizeSubtableDisplayRendererId(col.SubtableDisplayRendererId);
            bool isEmbeddedGridPreview =
                ResolveEffectiveSubtableDisplayPreviewQuality(workspace, col) == DocSubtablePreviewQuality.Full &&
                string.Equals(normalizedRendererId, SubtableDisplayRendererGrid, StringComparison.Ordinal);
            if (isEmbeddedGridPreview)
            {
                workspace.CancelTableCellEditIfActive();
                return;
            }

            var childTable = workspace.Project.Tables.Find(t => string.Equals(t.Id, col.SubtableId, StringComparison.Ordinal));
            if (childTable != null)
            {
                string parentRowId = ResolveSubtableParentRowId(workspace, table, col, sourceRowIndex);
                if (string.IsNullOrEmpty(parentRowId))
                {
                    parentRowId = row.Id;
                }

                workspace.ContentTabs.OpenOrFocusTableFromNavigation(childTable.Id, parentRowId);
            }
            workspace.CancelTableCellEditIfActive();
            ClearSelectionState();
            return;
        }

        if (HasFormulaExpression(col))
        {
            workspace.CancelTableCellEditIfActive();
            _activeRow = -1;
            _activeCol = -1;
            return;
        }

        if (col.Kind == DocColumnKind.Id)
        {
            workspace.CancelTableCellEditIfActive();
            _activeRow = -1;
            _activeCol = -1;
            return;
        }

        if (cellValue.HasCellFormulaExpression)
        {
            workspace.CancelTableCellEditIfActive();
            workspace.SetStatusMessage("Cell has a formula override. Clear the cell formula to edit literal values.");
            _activeRow = -1;
            _activeCol = -1;
            return;
        }

        if (col.Kind == DocColumnKind.Spline)
        {
            OpenSplinePopover(table, row, col);
            workspace.CancelTableCellEditIfActive();
            _activeRow = -1;
            _activeCol = -1;
            return;
        }

        if (col.Kind == DocColumnKind.Vec2 ||
            col.Kind == DocColumnKind.Vec3 ||
            col.Kind == DocColumnKind.Vec4 ||
            col.Kind == DocColumnKind.Color)
        {
            OpenEditVectorCellDialog(table, row, col);
            workspace.CancelTableCellEditIfActive();
            _activeRow = -1;
            _activeCol = -1;
            return;
        }

        if (IsAssetColumnKind(col.Kind))
        {
            string? assetRoot = ResolveAssetRootForColumnKind(workspace, col.Kind);
            if (!string.IsNullOrWhiteSpace(assetRoot) && Directory.Exists(assetRoot))
            {
                string currentValue = cellValue.StringValue ?? "";
                AssetBrowserModal.Open(
                    assetRoot,
                    col.Kind,
                    table.Id,
                    row.Id,
                    col.Id,
                    currentValue,
                    col.Kind == DocColumnKind.MeshAsset
                        ? (cellValue.ModelPreviewSettings ?? col.ModelPreviewSettings)
                        : null);
                workspace.CancelTableCellEditIfActive();
                _activeRow = -1;
                _activeCol = -1;
                return;
            }
        }

        // Checkbox: toggle immediately
        if (col.Kind == DocColumnKind.Checkbox)
        {
            var oldCell = cellValue;
            var newCell = DocCellValue.Bool(!oldCell.BoolValue);
            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.SetCell,
                TableId = table.Id,
                RowId = row.Id,
                ColumnId = col.Id,
                OldCellValue = oldCell,
                NewCellValue = newCell
            });
            workspace.CancelTableCellEditIfActive();
            _activeRow = -1;
            _activeCol = -1;
            return;
        }

        // Already editing this cell
        if (workspace.EditState.IsEditing &&
            IsEditOwnedByCurrentInstance(workspace, table) &&
            workspace.EditState.RowIndex == rowIndex &&
            workspace.EditState.ColIndex == colIndex)
        {
            return;
        }

        string initialText = col.Kind switch
        {
            DocColumnKind.Number => cellValue.NumberValue.ToString("G"),
            DocColumnKind.Relation => workspace.ResolveRelationDisplayLabel(col, cellValue.StringValue ?? ""),
            DocColumnKind.TableRef => ResolveTableRefLabel(workspace, cellValue.StringValue ?? ""),
            _ => cellValue.StringValue ?? ""
        };

        workspace.EditState.BeginEdit(rowIndex, colIndex, table.Id, _activeEmbeddedStateKey, row.Id, col.Id, initialText);
        if (col.Kind == DocColumnKind.Number)
        {
            workspace.EditState.HasNumberPreviewValue = true;
            workspace.EditState.NumberPreviewOriginalValue = cellValue.NumberValue;
        }

        if (col.Kind == DocColumnKind.Select && col.Options != null)
        {
            int idx = col.Options.IndexOf(cellValue.StringValue ?? "");
            workspace.EditState.DropdownIndex = Math.Max(0, idx);
        }
        else if (col.Kind == DocColumnKind.Relation)
        {
            workspace.EditState.DropdownIndex = ResolveRelationOptionIndex(workspace, table, col, cellValue.StringValue ?? "");
        }
        else if (col.Kind == DocColumnKind.TableRef)
        {
            workspace.EditState.DropdownIndex = ResolveTableRefOptionIndex(workspace, col, cellValue.StringValue ?? "");
        }
    }

    private static void CommitEditIfActive(DocWorkspace workspace, DocTable table)
    {
        if (TryCommitSelectedRangeEditIfActive(workspace, table))
        {
            return;
        }

        workspace.CommitTableCellEditIfActive();
    }

    private static bool TryCommitSelectedRangeEditIfActive(DocWorkspace workspace, DocTable table)
    {
        ref TableCellEditState edit = ref workspace.EditState;
        if (!edit.IsEditing || !IsEditOwnedByCurrentInstance(workspace, table))
        {
            return false;
        }

        if (!TryGetCellSelectionBounds(out int minRow, out int maxRow, out int minCol, out int maxCol))
        {
            return false;
        }

        bool isRangeSelection = minRow != maxRow || minCol != maxCol;
        if (!isRangeSelection)
        {
            return false;
        }

        if (edit.RowIndex < minRow ||
            edit.RowIndex > maxRow ||
            edit.ColIndex < minCol ||
            edit.ColIndex > maxCol)
        {
            return false;
        }

        if (edit.ColIndex < 0 || edit.ColIndex >= _colCount)
        {
            return false;
        }

        DocColumn editColumn = GetVisibleColumn(table, edit.ColIndex);
        if (IsColumnDataReadOnly(editColumn))
        {
            workspace.CancelTableCellEditIfActive();
            return true;
        }

        if (!CanApplyInlineRangeEditToColumnKind(editColumn.Kind))
        {
            return false;
        }

        string editText = new string(edit.Buffer, 0, edit.BufferLength);
        double parsedNumberValue = 0;
        if (editColumn.Kind == DocColumnKind.Number)
        {
            if (!double.TryParse(editText, out parsedNumberValue))
            {
                return false;
            }
        }

        var commands = new List<DocCommand>();
        for (int displayRowIndex = minRow; displayRowIndex <= maxRow; displayRowIndex++)
        {
            int sourceRowIndex = GetSourceRowIndex(displayRowIndex);
            if (sourceRowIndex < 0 || sourceRowIndex >= table.Rows.Count)
            {
                continue;
            }

            DocRow sourceRow = table.Rows[sourceRowIndex];
            for (int displayColumnIndex = minCol; displayColumnIndex <= maxCol; displayColumnIndex++)
            {
                DocColumn targetColumn = GetVisibleColumn(table, displayColumnIndex);
                if (targetColumn.Kind != editColumn.Kind ||
                    IsColumnDataReadOnly(targetColumn) ||
                    HasFormulaExpression(targetColumn))
                {
                    continue;
                }

                DocCellValue oldCell = sourceRow.GetCell(targetColumn);
                if (oldCell.HasCellFormulaExpression)
                {
                    continue;
                }

                if (!TryBuildRangeEditCellValues(
                        workspace,
                        ref edit,
                        oldCell,
                        editText,
                        parsedNumberValue,
                        targetColumn,
                        displayRowIndex,
                        displayColumnIndex,
                        out DocCellValue commandOldCell,
                        out DocCellValue commandNewCell))
                {
                    continue;
                }

                commands.Add(new DocCommand
                {
                    Kind = DocCommandKind.SetCell,
                    TableId = table.Id,
                    RowId = sourceRow.Id,
                    ColumnId = targetColumn.Id,
                    OldCellValue = commandOldCell,
                    NewCellValue = commandNewCell
                });
            }
        }

        if (commands.Count <= 0)
        {
            if (editColumn.Kind == DocColumnKind.Number && edit.HasNumberPreviewValue)
            {
                workspace.CancelTableCellEditIfActive();
            }
            else
            {
                workspace.EditState.EndEdit();
            }

            return true;
        }

        workspace.ExecuteCommands(commands);
        workspace.EditState.EndEdit();
        return true;
    }

    private static bool CanApplyInlineRangeEditToColumnKind(DocColumnKind kind)
    {
        return kind == DocColumnKind.Id ||
               kind == DocColumnKind.Text ||
               kind == DocColumnKind.Number ||
               kind == DocColumnKind.TextureAsset ||
               kind == DocColumnKind.MeshAsset ||
               kind == DocColumnKind.AudioAsset ||
               kind == DocColumnKind.UiAsset ||
               kind == DocColumnKind.TableRef;
    }

    private static bool TryBuildRangeEditCellValues(
        DocWorkspace workspace,
        ref TableCellEditState edit,
        DocCellValue oldCell,
        string editText,
        double parsedNumberValue,
        DocColumn targetColumn,
        int displayRowIndex,
        int displayColumnIndex,
        out DocCellValue commandOldCell,
        out DocCellValue commandNewCell)
    {
        commandOldCell = oldCell;
        commandNewCell = oldCell;

        switch (targetColumn.Kind)
        {
            case DocColumnKind.Number:
            {
                double normalizedNumber = workspace.NormalizeNumberForColumn(targetColumn, parsedNumberValue);
                double oldNumber = oldCell.NumberValue;
                if (displayRowIndex == edit.RowIndex &&
                    displayColumnIndex == edit.ColIndex &&
                    edit.HasNumberPreviewValue)
                {
                    oldNumber = edit.NumberPreviewOriginalValue;
                }

                if (Math.Abs(oldNumber - normalizedNumber) < 0.0000001)
                {
                    return false;
                }

                commandOldCell = DocCellValue.Number(oldNumber);
                commandNewCell = DocCellValue.Number(normalizedNumber);
                return true;
            }

            case DocColumnKind.MeshAsset:
            {
                string oldText = oldCell.StringValue ?? "";
                if (string.Equals(oldText, editText, StringComparison.Ordinal))
                {
                    return false;
                }

                commandNewCell = DocCellValue.Text(editText, oldCell.ModelPreviewSettings);
                return true;
            }

            case DocColumnKind.Text:
            case DocColumnKind.Id:
            case DocColumnKind.TextureAsset:
            case DocColumnKind.AudioAsset:
            case DocColumnKind.UiAsset:
            case DocColumnKind.TableRef:
            {
                string oldText = oldCell.StringValue ?? "";
                if (string.Equals(oldText, editText, StringComparison.Ordinal))
                {
                    return false;
                }

                commandNewCell = DocCellValue.Text(editText);
                return true;
            }
        }

        return false;
    }

    private static bool IsEditOwnedByCurrentInstance(DocWorkspace workspace, DocTable table)
    {
        if (!workspace.EditState.IsEditing)
        {
            return false;
        }

        if (!string.Equals(workspace.EditState.TableId, table.Id, StringComparison.Ordinal))
        {
            return false;
        }

        string ownerStateKey = workspace.EditState.OwnerStateKey ?? "";
        return string.Equals(ownerStateKey, _activeEmbeddedStateKey, StringComparison.Ordinal);
    }

    private static void OpenSplinePopover(DocTable table, DocRow row, DocColumn column)
    {
        string currentJson = row.GetCell(column).StringValue ?? SplineUtils.DefaultSplineJson;
        _splinePopoverActive = true;
        _splinePopoverTableId = table.Id;
        _splinePopoverRowId = row.Id;
        _splinePopoverColumnId = column.Id;
        _splinePopoverOriginalJson = currentJson;
        _splinePopoverOwnerStateKey = _activeEmbeddedStateKey ?? "";
        _splinePopoverHasPreviewChange = false;
        _splinePopoverOpenedFrame = Im.Context.FrameCount;
        _splinePopoverCurve = SplineConverter.JsonToCurve(currentJson);
        _splinePopoverView = default;
    }

    private static void DrawSplinePopover(DocWorkspace workspace, DocTable table)
    {
        if (!_splinePopoverActive ||
            !string.Equals(_splinePopoverTableId, table.Id, StringComparison.Ordinal))
        {
            return;
        }

        if (!TryResolveSplinePopoverTarget(table, out int displayRowIndex, out int displayColumnIndex))
        {
            CloseSplinePopover(workspace, commitChanges: true, revertPreview: false);
            return;
        }

        ImRect cellRect = GetCellRect(displayRowIndex, displayColumnIndex);
        float popoverWidth = 340f;
        float popoverHeight = 250f;
        float popoverX = cellRect.X;
        float popoverY = cellRect.Bottom + 6f;
        if (popoverX + popoverWidth > _gridRect.Right)
        {
            popoverX = _gridRect.Right - popoverWidth;
        }

        if (popoverX < _gridRect.X)
        {
            popoverX = _gridRect.X;
        }

        if (popoverY + popoverHeight > _gridRect.Bottom)
        {
            popoverY = cellRect.Y - popoverHeight - 6f;
        }

        if (popoverY < _headerRect.Bottom)
        {
            popoverY = _headerRect.Bottom;
        }

        var popoverRect = new ImRect(popoverX, popoverY, popoverWidth, popoverHeight);
        using var popoverScope = ImPopover.PushOverlayScopeLocal(popoverRect);
        var style = Im.Style;
        Im.DrawRoundedRect(popoverRect.X, popoverRect.Y, popoverRect.Width, popoverRect.Height, 8f, style.Surface);
        Im.DrawRoundedRectStroke(popoverRect.X, popoverRect.Y, popoverRect.Width, popoverRect.Height, 8f, style.Border, 1f);

        float titleX = popoverRect.X + 10f;
        float titleY = popoverRect.Y + 8f;
        Im.Text("Spline".AsSpan(), titleX, titleY, style.FontSize, style.TextPrimary);

        float editorX = popoverRect.X + 8f;
        float editorY = popoverRect.Y + 30f;
        float editorWidth = popoverRect.Width - 16f;
        float editorHeight = popoverRect.Height - 74f;
        int editorId = HashCode.Combine(_splinePopoverTableId, _splinePopoverRowId, _splinePopoverColumnId);
        if (ImCurveEditor.DrawAt(
            editorId,
            ref _splinePopoverCurve,
            editorX,
            editorY,
            editorWidth,
            editorHeight,
            minValue: 0f,
            maxValue: 1f,
            drawBackground: true,
            drawGrid: true,
            drawHandles: true,
            handleInput: true,
            ref _splinePopoverView))
        {
            ApplySplinePopoverPreview(workspace);
        }

        float buttonY = popoverRect.Bottom - 28f;
        float buttonX = popoverRect.X + 8f;
        float buttonGap = 6f;
        float buttonWidth = (popoverRect.Width - 16f - (buttonGap * 4f)) / 5f;
        float buttonHeight = 20f;

        if (Im.Button("Linear", buttonX, buttonY, buttonWidth, buttonHeight))
        {
            _splinePopoverCurve = Curve.Linear();
            ApplySplinePopoverPreview(workspace);
        }

        buttonX += buttonWidth + buttonGap;
        if (Im.Button("EaseIn", buttonX, buttonY, buttonWidth, buttonHeight))
        {
            _splinePopoverCurve = Curve.EaseIn();
            ApplySplinePopoverPreview(workspace);
        }

        buttonX += buttonWidth + buttonGap;
        if (Im.Button("EaseOut", buttonX, buttonY, buttonWidth, buttonHeight))
        {
            _splinePopoverCurve = Curve.EaseOut();
            ApplySplinePopoverPreview(workspace);
        }

        buttonX += buttonWidth + buttonGap;
        if (Im.Button("EaseInOut", buttonX, buttonY, buttonWidth, buttonHeight))
        {
            _splinePopoverCurve = Curve.EaseInOut();
            ApplySplinePopoverPreview(workspace);
        }

        buttonX += buttonWidth + buttonGap;
        if (Im.Button("Step", buttonX, buttonY, buttonWidth, buttonHeight))
        {
            _splinePopoverCurve = Curve.Step();
            ApplySplinePopoverPreview(workspace);
        }

        if (ImPopover.ShouldClose(
                openedFrame: _splinePopoverOpenedFrame,
                closeOnEscape: true,
                closeOnOutsideButtons: ImPopoverCloseButtons.Left,
                consumeCloseClick: false,
                requireNoMouseOwner: false,
                useViewportMouseCoordinates: false,
                insideRect: popoverRect))
        {
            bool closedByEscape = Im.Context.Input.KeyEscape;
            CloseSplinePopover(
                workspace,
                commitChanges: !closedByEscape,
                revertPreview: closedByEscape);
        }
    }

    private static bool TryResolveSplinePopoverTarget(DocTable table, out int displayRowIndex, out int displayColumnIndex)
    {
        displayRowIndex = -1;
        displayColumnIndex = -1;

        int sourceRowIndex = -1;
        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            if (string.Equals(table.Rows[rowIndex].Id, _splinePopoverRowId, StringComparison.Ordinal))
            {
                sourceRowIndex = rowIndex;
                break;
            }
        }

        if (sourceRowIndex < 0)
        {
            return false;
        }

        for (int rowIndex = 0; rowIndex < _rowCount; rowIndex++)
        {
            if (GetSourceRowIndex(rowIndex) == sourceRowIndex)
            {
                displayRowIndex = rowIndex;
                break;
            }
        }

        if (displayRowIndex < 0)
        {
            return false;
        }

        int sourceColumnIndex = -1;
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            if (string.Equals(table.Columns[columnIndex].Id, _splinePopoverColumnId, StringComparison.Ordinal))
            {
                sourceColumnIndex = columnIndex;
                break;
            }
        }

        if (sourceColumnIndex < 0)
        {
            return false;
        }

        for (int columnIndex = 0; columnIndex < _colCount; columnIndex++)
        {
            if (_visibleColMap[columnIndex] == sourceColumnIndex)
            {
                displayColumnIndex = columnIndex;
                break;
            }
        }

        return displayColumnIndex >= 0;
    }

    private static void ApplySplinePopoverPreview(DocWorkspace workspace)
    {
        string previewJson = SplineConverter.CurveToJson(ref _splinePopoverCurve);
        if (workspace.PreviewTextCellValue(_splinePopoverTableId, _splinePopoverRowId, _splinePopoverColumnId, previewJson))
        {
            _splinePopoverHasPreviewChange = true;
        }

        if (_splinePreviewCurveByJson.Count >= MaxSplinePreviewCacheEntries)
        {
            _splinePreviewCurveByJson.Clear();
        }

        _splinePreviewCurveByJson[previewJson] = _splinePopoverCurve;
    }

    private static void CloseSplinePopover(DocWorkspace workspace, bool commitChanges, bool revertPreview)
    {
        string currentJson = SplineConverter.CurveToJson(ref _splinePopoverCurve);
        if (revertPreview && _splinePopoverHasPreviewChange)
        {
            workspace.PreviewTextCellValue(_splinePopoverTableId, _splinePopoverRowId, _splinePopoverColumnId, _splinePopoverOriginalJson);
        }
        else if (commitChanges &&
                 !string.Equals(currentJson, _splinePopoverOriginalJson, StringComparison.Ordinal))
        {
            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.SetCell,
                TableId = _splinePopoverTableId,
                RowId = _splinePopoverRowId,
                ColumnId = _splinePopoverColumnId,
                OldCellValue = DocCellValue.Text(_splinePopoverOriginalJson),
                NewCellValue = DocCellValue.Text(currentJson),
            });
        }

        _splinePopoverActive = false;
        _splinePopoverTableId = "";
        _splinePopoverRowId = "";
        _splinePopoverColumnId = "";
        _splinePopoverOriginalJson = "";
        _splinePopoverOwnerStateKey = "";
        _splinePopoverHasPreviewChange = false;
        _splinePopoverOpenedFrame = -1;
        _splinePopoverView = default;
    }

    private static void AdvanceToNextCell(DocWorkspace workspace, DocTable table)
    {
        int nextCol = (_activeCol + 1) % _colCount;
        int nextRow = _activeRow;
        if (nextCol == 0)
        {
            nextRow++;
            if (nextRow >= _rowCount) nextRow = 0;
        }

        _activeRow = nextRow;
        _activeCol = nextCol;
        _selStartRow = nextRow; _selStartCol = nextCol;
        _selEndRow = nextRow; _selEndCol = nextCol;

        BeginCellEdit(workspace, table, nextRow, nextCol);
    }

    private static List<(string rowId, string label)> BuildRelationOptions(
        DocWorkspace workspace,
        DocTable relationTable,
        DocColumn? relationColumn = null)
    {
        var relationOptions = new List<(string rowId, string label)>(relationTable.Rows.Count + 1)
        {
            ("", "(none)")
        };

        for (int rowIndex = 0; rowIndex < relationTable.Rows.Count; rowIndex++)
        {
            var relationRow = relationTable.Rows[rowIndex];
            string label = relationColumn != null
                ? workspace.ResolveRelationDisplayLabel(relationColumn, relationRow.Id)
                : workspace.ResolveRelationDisplayLabel(relationTable.Id, relationRow.Id);
            relationOptions.Add((relationRow.Id, label));
        }

        return relationOptions;
    }

    private static List<(string tableId, string label)> BuildTableRefOptions(
        DocWorkspace workspace,
        DocColumn tableRefColumn)
    {
        var options = new List<(string tableId, string label)>(workspace.Project.Tables.Count + 1)
        {
            ("", "(none)")
        };

        for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
        {
            DocTable candidateTable = workspace.Project.Tables[tableIndex];
            if (!IsTableAllowedForTableRef(workspace, tableRefColumn, candidateTable))
            {
                continue;
            }

            options.Add((candidateTable.Id, candidateTable.Name));
        }

        return options;
    }

    private static bool IsTableAllowedForTableRef(
        DocWorkspace workspace,
        DocColumn tableRefColumn,
        DocTable candidateTable)
    {
        string? baseTableId = tableRefColumn.TableRefBaseTableId;
        if (string.IsNullOrWhiteSpace(baseTableId))
        {
            return true;
        }

        return IsTableDerivedFromOrEqualTo(workspace, candidateTable, baseTableId!);
    }

    private static bool IsTableDerivedFromOrEqualTo(
        DocWorkspace workspace,
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
            DocTable? currentTable = FindTableById(workspace, currentTableId);
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

    private static string ResolveTableRefLabel(DocWorkspace workspace, string tableId)
    {
        if (string.IsNullOrWhiteSpace(tableId))
        {
            return "";
        }

        for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
        {
            DocTable table = workspace.Project.Tables[tableIndex];
            if (string.Equals(table.Id, tableId, StringComparison.Ordinal))
            {
                return table.Name;
            }
        }

        return tableId;
    }

    private static int ResolveTableRefOptionIndex(
        DocWorkspace workspace,
        DocColumn tableRefColumn,
        string selectedTableId)
    {
        var tableRefOptions = BuildTableRefOptions(workspace, tableRefColumn);
        for (int optionIndex = 0; optionIndex < tableRefOptions.Count; optionIndex++)
        {
            if (string.Equals(tableRefOptions[optionIndex].tableId, selectedTableId, StringComparison.Ordinal))
            {
                return optionIndex;
            }
        }

        return 0;
    }

    private static int ResolveRelationOptionIndex(
        DocWorkspace workspace,
        DocTable sourceTable,
        DocColumn relationColumn,
        string selectedRowId)
    {
        if (!TryResolveRelationTargetTable(workspace, sourceTable, relationColumn, out DocTable relationTable))
        {
            return 0;
        }

        relationTable = workspace.ResolveTableForVariant(relationTable, relationColumn.RelationTableVariantId);
        var relationOptions = BuildRelationOptions(workspace, relationTable, relationColumn);
        for (int optionIndex = 0; optionIndex < relationOptions.Count; optionIndex++)
        {
            if (string.Equals(relationOptions[optionIndex].rowId, selectedRowId, StringComparison.Ordinal))
            {
                return optionIndex;
            }
        }

        return 0;
    }

    private static List<DocTable> BuildRelationTableChoices(DocWorkspace workspace)
    {
        var relationTables = new List<DocTable>(workspace.Project.Tables.Count);
        for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
        {
            relationTables.Add(workspace.Project.Tables[tableIndex]);
        }

        return relationTables;
    }

    private static int ResolveDefaultRelationTableIndex(DocWorkspace workspace, DocTable activeTable)
    {
        if (workspace.Project.Tables.Count == 0)
        {
            return -1;
        }

        for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
        {
            if (workspace.Project.Tables[tableIndex].Id != activeTable.Id)
            {
                return tableIndex;
            }
        }

        return 0;
    }

    private static int GetCachedSubtableItemCount(
        DocWorkspace workspace,
        DocTable parentTable,
        DocColumn subtableColumn,
        int sourceRowIndex)
    {
        if (string.IsNullOrEmpty(subtableColumn.SubtableId) ||
            sourceRowIndex < 0 ||
            sourceRowIndex >= parentTable.Rows.Count)
        {
            return 0;
        }

        SubtableCountCacheEntry cacheEntry = GetOrCreateSubtableCountCacheEntry(parentTable.Id, subtableColumn.Id);
        bool cacheCanServe = cacheEntry.ProjectRevision == workspace.ProjectRevision &&
                             cacheEntry.CountsBySourceRow.Length == parentTable.Rows.Count;
        if (cacheCanServe)
        {
            return cacheEntry.CountsBySourceRow[sourceRowIndex];
        }

        DocTable? childTable = FindTableById(workspace, subtableColumn.SubtableId);
        if (childTable == null || string.IsNullOrEmpty(childTable.ParentRowColumnId))
        {
            return 0;
        }

        bool needsRebuild = cacheEntry.ProjectRevision != workspace.ProjectRevision ||
                            !string.Equals(cacheEntry.ChildTableId, childTable.Id, StringComparison.Ordinal) ||
                            !string.Equals(cacheEntry.ParentRowColumnId, childTable.ParentRowColumnId, StringComparison.Ordinal) ||
                            cacheEntry.CountsBySourceRow.Length != parentTable.Rows.Count;

        if (needsRebuild)
        {
            RebuildSubtableCountCache(workspace, parentTable, subtableColumn, childTable, childTable.ParentRowColumnId, cacheEntry);
        }

        if (sourceRowIndex >= cacheEntry.CountsBySourceRow.Length)
        {
            return 0;
        }

        return cacheEntry.CountsBySourceRow[sourceRowIndex];
    }

    private static SubtableCountCacheEntry GetOrCreateSubtableCountCacheEntry(
        string parentTableId,
        string subtableColumnId)
    {
        for (int cacheIndex = 0; cacheIndex < _subtableCountCacheEntries.Count; cacheIndex++)
        {
            var existingEntry = _subtableCountCacheEntries[cacheIndex];
            if (string.Equals(existingEntry.ParentTableId, parentTableId, StringComparison.Ordinal) &&
                string.Equals(existingEntry.SubtableColumnId, subtableColumnId, StringComparison.Ordinal))
            {
                return existingEntry;
            }
        }

        var createdEntry = new SubtableCountCacheEntry
        {
            ParentTableId = parentTableId,
            SubtableColumnId = subtableColumnId
        };
        _subtableCountCacheEntries.Add(createdEntry);
        return createdEntry;
    }

    private static void RebuildSubtableCountCache(
        DocWorkspace workspace,
        DocTable parentTable,
        DocColumn subtableColumn,
        DocTable childTable,
        string parentRowColumnId,
        SubtableCountCacheEntry cacheEntry)
    {
        int parentRowCount = parentTable.Rows.Count;
        if (cacheEntry.CountsBySourceRow.Length != parentRowCount)
        {
            cacheEntry.CountsBySourceRow = new int[parentRowCount];
        }
        else
        {
            Array.Clear(cacheEntry.CountsBySourceRow, 0, parentRowCount);
        }

        _subtableCountParentRowLookupScratch.Clear();
        for (int parentRowIndex = 0; parentRowIndex < parentRowCount; parentRowIndex++)
        {
            string parentRowId = ResolveSubtableParentRowId(workspace, parentTable, subtableColumn, parentRowIndex);
            if (!string.IsNullOrEmpty(parentRowId))
            {
                _subtableCountParentRowLookupScratch[parentRowId] = parentRowIndex;
            }
        }

        foreach (var pair in _subtableCountParentRowLookupScratch)
        {
            int parentRowIndex = pair.Value;
            if (parentRowIndex < 0 || parentRowIndex >= cacheEntry.CountsBySourceRow.Length)
            {
                continue;
            }

            int[] childRowIndices = GetCachedParentRowSourceIndices(workspace, childTable, parentRowColumnId, pair.Key);
            cacheEntry.CountsBySourceRow[parentRowIndex] = childRowIndices.Length;
        }

        cacheEntry.ProjectRevision = workspace.ProjectRevision;
        cacheEntry.ChildTableId = childTable.Id;
        cacheEntry.ParentRowColumnId = parentRowColumnId;
    }

    private static string ResolveSubtableParentRowId(
        DocWorkspace workspace,
        DocTable parentTable,
        DocColumn subtableColumn,
        int sourceRowIndex)
    {
        if (sourceRowIndex < 0 || sourceRowIndex >= parentTable.Rows.Count)
        {
            return "";
        }

        string fallbackParentRowId = parentTable.Rows[sourceRowIndex].Id;
        if (!parentTable.IsDerived || parentTable.DerivedConfig == null)
        {
            return fallbackParentRowId;
        }

        if (!workspace.DerivedResults.TryGetValue(parentTable.Id, out var derivedResult) ||
            sourceRowIndex >= derivedResult.RowKeys.Count)
        {
            return fallbackParentRowId;
        }

        if (!TryGetDerivedProjectionByOutputColumnId(parentTable.DerivedConfig, subtableColumn.Id, out var projection))
        {
            return fallbackParentRowId;
        }

        var rowKey = derivedResult.RowKeys[sourceRowIndex];
        if (string.IsNullOrEmpty(rowKey.RowId))
        {
            return "";
        }

        if (string.Equals(rowKey.TableId, projection.SourceTableId, StringComparison.Ordinal))
        {
            return rowKey.RowId;
        }

        for (int stepIndex = 0; stepIndex < parentTable.DerivedConfig.Steps.Count; stepIndex++)
        {
            var step = parentTable.DerivedConfig.Steps[stepIndex];
            if (step.Kind != DerivedStepKind.Append ||
                string.IsNullOrEmpty(step.Id) ||
                string.IsNullOrEmpty(step.SourceTableId))
            {
                continue;
            }

            if (string.Equals(step.Id, rowKey.TableId, StringComparison.Ordinal) &&
                string.Equals(step.SourceTableId, projection.SourceTableId, StringComparison.Ordinal))
            {
                return rowKey.RowId;
            }
        }

        return "";
    }

    private static bool TryGetDerivedProjectionByOutputColumnId(
        DocDerivedConfig config,
        string outputColumnId,
        out DerivedProjection projection)
    {
        for (int projectionIndex = 0; projectionIndex < config.Projections.Count; projectionIndex++)
        {
            var candidateProjection = config.Projections[projectionIndex];
            if (string.Equals(candidateProjection.OutputColumnId, outputColumnId, StringComparison.Ordinal))
            {
                projection = candidateProjection;
                return true;
            }
        }

        projection = null!;
        return false;
    }

    private static DocTable? FindTableById(DocWorkspace workspace, string tableId)
    {
        if (string.IsNullOrWhiteSpace(tableId))
        {
            return null;
        }

        EnsureSubtableLookupCachesForFrame(Im.Context.FrameCount);
        if (_subtableTableLookupCache.TryGetValue(tableId, out DocTable? cachedTable))
        {
            return cachedTable;
        }

        for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
        {
            var table = workspace.Project.Tables[tableIndex];
            if (string.Equals(table.Id, tableId, StringComparison.Ordinal))
            {
                _subtableTableLookupCache[tableId] = table;
                return table;
            }
        }

        return null;
    }

    private static int ResolveTableIndexById(DocWorkspace workspace, string tableId)
    {
        for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
        {
            if (workspace.Project.Tables[tableIndex].Id == tableId)
            {
                return tableIndex;
            }
        }

        return 0;
    }

    // =====================================================================
    //  Selection helpers
    // =====================================================================

    private static bool TryGetCellSelectionBounds(out int minRow, out int maxRow, out int minCol, out int maxCol)
    {
        minRow = 0;
        maxRow = -1;
        minCol = 0;
        maxCol = -1;

        if (_rowCount <= 0 ||
            _colCount <= 0 ||
            _selStartRow < 0 ||
            _selEndRow < 0 ||
            _selStartCol < 0 ||
            _selEndCol < 0)
        {
            return false;
        }

        minRow = Math.Clamp(Math.Min(_selStartRow, _selEndRow), 0, _rowCount - 1);
        maxRow = Math.Clamp(Math.Max(_selStartRow, _selEndRow), 0, _rowCount - 1);
        minCol = Math.Clamp(Math.Min(_selStartCol, _selEndCol), 0, _colCount - 1);
        maxCol = Math.Clamp(Math.Max(_selStartCol, _selEndCol), 0, _colCount - 1);
        return minRow <= maxRow && minCol <= maxCol;
    }

    private static void CollectSelectedSourceRowIndicesForEditCell(
        DocTable sourceTable,
        int editDisplayRowIndex,
        int editDisplayColumnIndex,
        List<int> targetSourceRowIndices)
    {
        targetSourceRowIndices.Clear();

        if (editDisplayRowIndex < 0 || editDisplayRowIndex >= _rowCount)
        {
            return;
        }

        bool appliedRangeSelection = false;
        if (TryGetCellSelectionBounds(out int minRow, out int maxRow, out int minCol, out int maxCol) &&
            editDisplayRowIndex >= minRow &&
            editDisplayRowIndex <= maxRow &&
            editDisplayColumnIndex >= minCol &&
            editDisplayColumnIndex <= maxCol &&
            maxRow > minRow)
        {
            appliedRangeSelection = true;
            for (int displayRowIndex = minRow; displayRowIndex <= maxRow; displayRowIndex++)
            {
                int sourceRowIndex = GetSourceRowIndex(displayRowIndex);
                if (sourceRowIndex < 0 || sourceRowIndex >= sourceTable.Rows.Count)
                {
                    continue;
                }

                targetSourceRowIndices.Add(sourceRowIndex);
            }
        }

        if (!appliedRangeSelection)
        {
            int sourceRowIndex = GetSourceRowIndex(editDisplayRowIndex);
            if (sourceRowIndex >= 0 && sourceRowIndex < sourceTable.Rows.Count)
            {
                targetSourceRowIndices.Add(sourceRowIndex);
            }
        }
    }

    private static bool TryGetSelectedOrActiveCellBounds(out int minRow, out int maxRow, out int minCol, out int maxCol)
    {
        if (TryGetCellSelectionBounds(out minRow, out maxRow, out minCol, out maxCol))
        {
            return true;
        }

        minRow = 0;
        maxRow = -1;
        minCol = 0;
        maxCol = -1;
        if (_activeRow < 0 ||
            _activeCol < 0 ||
            _activeRow >= _rowCount ||
            _activeCol >= _colCount)
        {
            return false;
        }

        minRow = _activeRow;
        maxRow = _activeRow;
        minCol = _activeCol;
        maxCol = _activeCol;
        return true;
    }

    private static bool TryGetSelectionFillHandleRect(out ImRect handleRect)
    {
        handleRect = default;
        if (!TryGetSelectedOrActiveCellBounds(out int minRow, out int maxRow, out int minCol, out int maxCol))
        {
            return false;
        }

        if (minRow < 0 ||
            maxRow < 0 ||
            minCol < 0 ||
            maxCol < 0 ||
            maxRow >= _rowCount ||
            maxCol >= _colCount)
        {
            return false;
        }

        ImRect cornerCellRect = GetCellRect(maxRow, maxCol);
        if (!TryGetVisibleColumnRect(maxCol, cornerCellRect.Y, cornerCellRect.Height, out ImRect visibleCornerCellRect))
        {
            return false;
        }

        float clippedTop = Math.Max(visibleCornerCellRect.Y, _bodyRect.Y);
        float clippedBottom = Math.Min(visibleCornerCellRect.Bottom, _bodyRect.Bottom);
        if (clippedBottom <= clippedTop)
        {
            return false;
        }

        float handleX = visibleCornerCellRect.Right - (FillHandleSize * 0.5f);
        float handleY = clippedBottom - (FillHandleSize * 0.5f);
        handleRect = new ImRect(handleX, handleY, FillHandleSize, FillHandleSize);
        return true;
    }

    private static void CopySelectedCellsToClipboard(DocWorkspace workspace, DocTable table)
    {
        if (!TryGetSelectedOrActiveCellBounds(out int minRow, out int maxRow, out int minCol, out int maxCol))
        {
            return;
        }

        var builder = new System.Text.StringBuilder();
        for (int displayRowIndex = minRow; displayRowIndex <= maxRow; displayRowIndex++)
        {
            if (displayRowIndex > minRow)
            {
                builder.Append('\n');
            }

            int sourceRowIndex = GetSourceRowIndex(displayRowIndex);
            for (int displayColumnIndex = minCol; displayColumnIndex <= maxCol; displayColumnIndex++)
            {
                if (displayColumnIndex > minCol)
                {
                    builder.Append('\t');
                }

                if (sourceRowIndex < 0 || sourceRowIndex >= table.Rows.Count)
                {
                    continue;
                }

                DocRow sourceRow = table.Rows[sourceRowIndex];
                DocColumn sourceColumn = GetVisibleColumn(table, displayColumnIndex);
                DocCellValue sourceCell = sourceRow.GetCell(sourceColumn);
                builder.Append(GetClipboardTextForCell(sourceColumn, sourceCell));
            }
        }

        DerpEngine.SetClipboardText(builder.ToString());
        int copiedRowCount = Math.Max(1, maxRow - minRow + 1);
        int copiedColumnCount = Math.Max(1, maxCol - minCol + 1);
        workspace.SetStatusMessage("Copied " + copiedRowCount + "x" + copiedColumnCount + " cells.");
    }

    private static void PasteClipboardToSelection(DocWorkspace workspace, DocTable table)
    {
        string? clipboardText = DerpEngine.GetClipboardText();
        if (string.IsNullOrWhiteSpace(clipboardText))
        {
            return;
        }

        if (!TryParseClipboardGrid(clipboardText, out string[][] clipboardRows, out int clipboardRowCount, out int clipboardColumnCount))
        {
            return;
        }

        if (!TryGetSelectedOrActiveCellBounds(out int targetMinRow, out int targetMaxRow, out int targetMinCol, out int targetMaxCol))
        {
            return;
        }

        int selectedRowCount = targetMaxRow - targetMinRow + 1;
        int selectedColumnCount = targetMaxCol - targetMinCol + 1;
        bool singleTargetCell = selectedRowCount == 1 && selectedColumnCount == 1;
        if (singleTargetCell && (clipboardRowCount > 1 || clipboardColumnCount > 1))
        {
            targetMaxRow = Math.Min(_rowCount - 1, targetMinRow + clipboardRowCount - 1);
            targetMaxCol = Math.Min(_colCount - 1, targetMinCol + clipboardColumnCount - 1);
        }

        var commands = new List<DocCommand>();
        for (int targetDisplayRowIndex = targetMinRow; targetDisplayRowIndex <= targetMaxRow; targetDisplayRowIndex++)
        {
            int sourceRowIndex = GetSourceRowIndex(targetDisplayRowIndex);
            if (sourceRowIndex < 0 || sourceRowIndex >= table.Rows.Count)
            {
                continue;
            }

            int clipboardRowIndex = (targetDisplayRowIndex - targetMinRow) % clipboardRowCount;
            string[] clipboardRowCells = clipboardRows[clipboardRowIndex];
            for (int targetDisplayColumnIndex = targetMinCol; targetDisplayColumnIndex <= targetMaxCol; targetDisplayColumnIndex++)
            {
                DocColumn targetColumn = GetVisibleColumn(table, targetDisplayColumnIndex);
                int clipboardColumnIndex = (targetDisplayColumnIndex - targetMinCol) % clipboardColumnCount;
                string clipboardCellText = clipboardColumnIndex < clipboardRowCells.Length
                    ? clipboardRowCells[clipboardColumnIndex]
                    : "";
                if (!TryBuildSetCellCommandFromClipboardText(
                        workspace,
                        table,
                        sourceRowIndex,
                        targetColumn,
                        clipboardCellText,
                        out DocCommand command))
                {
                    continue;
                }

                commands.Add(command);
            }
        }

        if (commands.Count > 0)
        {
            workspace.ExecuteCommands(commands);
            workspace.SetStatusMessage("Pasted " + commands.Count + " cells.");
        }

        _selStartRow = targetMinRow;
        _selStartCol = targetMinCol;
        _selEndRow = targetMaxRow;
        _selEndCol = targetMaxCol;
        _activeRow = targetMinRow;
        _activeCol = targetMinCol;
        _selectedRows.Clear();
        _selectedHeaderCol = -1;
    }

    private static void ApplyFillHandleDrag(DocWorkspace workspace, DocTable table)
    {
        if (!_isFillHandleDragging ||
            _fillDragSourceMinRow < 0 ||
            _fillDragSourceMinCol < 0 ||
            _fillDragSourceMaxRow < _fillDragSourceMinRow ||
            _fillDragSourceMaxCol < _fillDragSourceMinCol)
        {
            ClearFillHandleDragState();
            return;
        }

        int targetMaxRow = Math.Max(_fillDragSourceMaxRow, _fillDragTargetRow);
        int targetMaxCol = Math.Max(_fillDragSourceMaxCol, _fillDragTargetCol);
        targetMaxRow = Math.Clamp(targetMaxRow, _fillDragSourceMaxRow, Math.Max(0, _rowCount - 1));
        targetMaxCol = Math.Clamp(targetMaxCol, _fillDragSourceMaxCol, Math.Max(0, _colCount - 1));

        int sourceRowCount = _fillDragSourceMaxRow - _fillDragSourceMinRow + 1;
        int sourceColumnCount = _fillDragSourceMaxCol - _fillDragSourceMinCol + 1;
        if (sourceRowCount <= 0 || sourceColumnCount <= 0)
        {
            ClearFillHandleDragState();
            return;
        }

        var commands = new List<DocCommand>();
        for (int targetDisplayRowIndex = _fillDragSourceMinRow; targetDisplayRowIndex <= targetMaxRow; targetDisplayRowIndex++)
        {
            int targetSourceRowIndex = GetSourceRowIndex(targetDisplayRowIndex);
            if (targetSourceRowIndex < 0 || targetSourceRowIndex >= table.Rows.Count)
            {
                continue;
            }

            for (int targetDisplayColumnIndex = _fillDragSourceMinCol; targetDisplayColumnIndex <= targetMaxCol; targetDisplayColumnIndex++)
            {
                bool isInsideSourceSelection = targetDisplayRowIndex <= _fillDragSourceMaxRow &&
                                               targetDisplayColumnIndex <= _fillDragSourceMaxCol;
                if (isInsideSourceSelection)
                {
                    continue;
                }

                int sourcePatternRowIndex = _fillDragSourceMinRow +
                                            ((targetDisplayRowIndex - _fillDragSourceMinRow) % sourceRowCount);
                int sourcePatternColumnIndex = _fillDragSourceMinCol +
                                               ((targetDisplayColumnIndex - _fillDragSourceMinCol) % sourceColumnCount);
                int sourcePatternSourceRowIndex = GetSourceRowIndex(sourcePatternRowIndex);
                if (sourcePatternSourceRowIndex < 0 || sourcePatternSourceRowIndex >= table.Rows.Count)
                {
                    continue;
                }

                DocColumn sourcePatternColumn = GetVisibleColumn(table, sourcePatternColumnIndex);
                DocRow sourcePatternRow = table.Rows[sourcePatternSourceRowIndex];
                DocCellValue sourcePatternCell = sourcePatternRow.GetCell(sourcePatternColumn);
                string fillText = GetClipboardTextForCell(sourcePatternColumn, sourcePatternCell);

                DocColumn targetColumn = GetVisibleColumn(table, targetDisplayColumnIndex);
                if (!TryBuildSetCellCommandFromClipboardText(
                        workspace,
                        table,
                        targetSourceRowIndex,
                        targetColumn,
                        fillText,
                        out DocCommand command))
                {
                    continue;
                }

                commands.Add(command);
            }
        }

        if (commands.Count > 0)
        {
            workspace.ExecuteCommands(commands);
            workspace.SetStatusMessage("Filled " + commands.Count + " cells.");
        }

        _selStartRow = _fillDragSourceMinRow;
        _selStartCol = _fillDragSourceMinCol;
        _selEndRow = targetMaxRow;
        _selEndCol = targetMaxCol;
        _activeRow = _fillDragSourceMinRow;
        _activeCol = _fillDragSourceMinCol;
        _selectedRows.Clear();
        _selectedHeaderCol = -1;
        ClearFillHandleDragState();
    }

    private static string GetClipboardTextForCell(DocColumn column, DocCellValue cell)
    {
        return column.Kind switch
        {
            DocColumnKind.Number => cell.NumberValue.ToString("G", CultureInfo.InvariantCulture),
            DocColumnKind.Checkbox => cell.BoolValue ? "true" : "false",
            _ => cell.StringValue ?? ""
        };
    }

    private static bool TryBuildSetCellCommandFromClipboardText(
        DocWorkspace workspace,
        DocTable table,
        int sourceRowIndex,
        DocColumn targetColumn,
        string clipboardCellText,
        out DocCommand command)
    {
        command = new DocCommand();

        if (sourceRowIndex < 0 || sourceRowIndex >= table.Rows.Count)
        {
            return false;
        }

        if (IsColumnDataReadOnly(targetColumn) ||
            HasFormulaExpression(targetColumn) ||
            targetColumn.Kind == DocColumnKind.Formula ||
            targetColumn.Kind == DocColumnKind.Subtable ||
            targetColumn.Kind == DocColumnKind.Spline ||
            targetColumn.Kind == DocColumnKind.Vec2 ||
            targetColumn.Kind == DocColumnKind.Vec3 ||
            targetColumn.Kind == DocColumnKind.Vec4 ||
            targetColumn.Kind == DocColumnKind.Color)
        {
            return false;
        }

        DocRow targetRow = table.Rows[sourceRowIndex];
        DocCellValue oldCell = targetRow.GetCell(targetColumn);
        if (oldCell.HasCellFormulaExpression)
        {
            return false;
        }

        if (!TryBuildPastedCellValue(
                workspace,
                targetColumn,
                oldCell,
                clipboardCellText,
                out DocCellValue newCell))
        {
            return false;
        }

        command = new DocCommand
        {
            Kind = DocCommandKind.SetCell,
            TableId = table.Id,
            RowId = targetRow.Id,
            ColumnId = targetColumn.Id,
            OldCellValue = oldCell,
            NewCellValue = newCell
        };
        return true;
    }

    private static bool TryBuildPastedCellValue(
        DocWorkspace workspace,
        DocColumn targetColumn,
        DocCellValue oldCell,
        string clipboardCellText,
        out DocCellValue newCell)
    {
        string normalizedText = clipboardCellText ?? "";
        newCell = oldCell;

        switch (targetColumn.Kind)
        {
            case DocColumnKind.Number:
            {
                if (!double.TryParse(normalizedText, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedNumber) &&
                    !double.TryParse(normalizedText, out parsedNumber))
                {
                    return false;
                }

                double normalizedNumber = workspace.NormalizeNumberForColumn(targetColumn, parsedNumber);
                if (Math.Abs(oldCell.NumberValue - normalizedNumber) < 0.0000001)
                {
                    return false;
                }

                newCell = DocCellValue.Number(normalizedNumber);
                return true;
            }

            case DocColumnKind.Checkbox:
            {
                if (!TryParseClipboardBool(normalizedText.AsSpan(), out bool parsedBool))
                {
                    return false;
                }

                if (oldCell.BoolValue == parsedBool)
                {
                    return false;
                }

                newCell = DocCellValue.Bool(parsedBool);
                return true;
            }

            case DocColumnKind.MeshAsset:
            {
                string oldText = oldCell.StringValue ?? "";
                if (string.Equals(oldText, normalizedText, StringComparison.Ordinal))
                {
                    return false;
                }

                newCell = DocCellValue.Text(normalizedText, oldCell.ModelPreviewSettings);
                return true;
            }

            default:
            {
                string oldText = oldCell.StringValue ?? "";
                if (string.Equals(oldText, normalizedText, StringComparison.Ordinal))
                {
                    return false;
                }

                newCell = DocCellValue.Text(normalizedText);
                return true;
            }
        }
    }

    private static bool TryParseClipboardGrid(
        string clipboardText,
        out string[][] rows,
        out int rowCount,
        out int columnCount)
    {
        rows = Array.Empty<string[]>();
        rowCount = 0;
        columnCount = 0;
        if (string.IsNullOrEmpty(clipboardText))
        {
            return false;
        }

        string normalizedClipboard = clipboardText.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        string[] rawRows = normalizedClipboard.Split('\n');
        int effectiveRowCount = rawRows.Length;
        while (effectiveRowCount > 0 && rawRows[effectiveRowCount - 1].Length == 0)
        {
            effectiveRowCount--;
        }

        if (effectiveRowCount <= 0)
        {
            return false;
        }

        rows = new string[effectiveRowCount][];
        for (int rowIndex = 0; rowIndex < effectiveRowCount; rowIndex++)
        {
            string[] rowCells = rawRows[rowIndex].Split('\t');
            if (rowCells.Length <= 0)
            {
                rowCells = [""];
            }

            rows[rowIndex] = rowCells;
            if (rowCells.Length > columnCount)
            {
                columnCount = rowCells.Length;
            }
        }

        if (columnCount <= 0)
        {
            columnCount = 1;
        }

        rowCount = effectiveRowCount;
        return true;
    }

    private static bool TryParseClipboardBool(ReadOnlySpan<char> text, out bool value)
    {
        ReadOnlySpan<char> trimmedText = text.Trim();
        if (trimmedText.Equals("true".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            trimmedText.Equals("1".AsSpan(), StringComparison.Ordinal) ||
            trimmedText.Equals("yes".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            trimmedText.Equals("on".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (trimmedText.Equals("false".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            trimmedText.Equals("0".AsSpan(), StringComparison.Ordinal) ||
            trimmedText.Equals("no".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            trimmedText.Equals("off".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

    private static bool IsInCellSelection(int row, int col)
    {
        if (!TryGetCellSelectionBounds(out int minRow, out int maxRow, out int minCol, out int maxCol))
        {
            return false;
        }

        return row >= minRow && row <= maxRow && col >= minCol && col <= maxCol;
    }

    private static int GetMinSelectedRow()
    {
        int min = int.MaxValue;
        foreach (int r in _selectedRows)
            if (r < min) min = r;
        return min == int.MaxValue ? 0 : min;
    }

    private static int GetMaxSelectedRow()
    {
        int max = -1;
        foreach (int r in _selectedRows)
            if (r > max) max = r;
        return max;
    }

    private static int[]? FilterByParentRow(
        DocWorkspace workspace,
        DocTable table,
        DocView? view,
        int[]? baseIndices,
        string parentRowId,
        string parentRowColumnId)
    {
        if (string.IsNullOrWhiteSpace(parentRowId) ||
            string.IsNullOrWhiteSpace(parentRowColumnId))
        {
            return baseIndices;
        }

        int[] parentSourceRowIndices = GetCachedParentRowSourceIndices(
            workspace,
            table,
            parentRowColumnId,
            parentRowId);
        if (parentSourceRowIndices.Length <= 0)
        {
            return Array.Empty<int>();
        }

        if (baseIndices == null)
        {
            return parentSourceRowIndices;
        }

        int baseIndicesIdentity = RuntimeHelpers.GetHashCode(baseIndices);
        string viewId = view?.Id ?? "";
        if (_parentFilterCacheProjectRevision == workspace.ProjectRevision &&
            string.Equals(_parentFilterCacheTableId, table.Id, StringComparison.Ordinal) &&
            string.Equals(_parentFilterCacheViewId, viewId, StringComparison.Ordinal) &&
            string.Equals(_parentFilterCacheParentRowId, parentRowId, StringComparison.Ordinal) &&
            string.Equals(_parentFilterCacheParentColumnId, parentRowColumnId, StringComparison.Ordinal) &&
            _parentFilterCacheBaseIndicesIdentity == baseIndicesIdentity)
        {
            return _parentFilterCachedIndices;
        }

        int total = baseIndices.Length;
        if (_parentFilterScratch.Length < total)
        {
            int newSize = _parentFilterScratch.Length == 0 ? total : _parentFilterScratch.Length;
            while (newSize < total)
            {
                newSize = Math.Max(total, newSize * 2);
            }

            _parentFilterScratch = new int[newSize];
        }

        int filteredCount = 0;
        for (int i = 0; i < total; i++)
        {
            int sourceIdx = baseIndices[i];
            if (Array.BinarySearch(parentSourceRowIndices, sourceIdx) >= 0)
            {
                _parentFilterScratch[filteredCount] = sourceIdx;
                filteredCount++;
            }
        }

        if (_parentFilterCachedIndices.Length != filteredCount)
        {
            _parentFilterCachedIndices = new int[filteredCount];
        }

        if (filteredCount > 0)
        {
            Array.Copy(_parentFilterScratch, _parentFilterCachedIndices, filteredCount);
        }

        _parentFilterCacheProjectRevision = workspace.ProjectRevision;
        _parentFilterCacheTableId = table.Id;
        _parentFilterCacheViewId = viewId;
        _parentFilterCacheParentRowId = parentRowId;
        _parentFilterCacheParentColumnId = parentRowColumnId;
        _parentFilterCacheBaseIndicesIdentity = baseIndicesIdentity;

        return _parentFilterCachedIndices;
    }

    private static int[] GetCachedParentRowSourceIndices(
        DocWorkspace workspace,
        DocTable childTable,
        string parentRowColumnId,
        string parentRowId)
    {
        ParentRowIndexCacheEntry cacheEntry = GetOrCreateParentRowIndexCacheEntry(
            childTable.Id,
            parentRowColumnId);
        if (cacheEntry.ProjectRevision != workspace.ProjectRevision)
        {
            RebuildParentRowIndexCache(workspace, childTable, parentRowColumnId, cacheEntry);
        }

        return cacheEntry.SourceRowIndicesByParentRowId.TryGetValue(parentRowId, out int[]? cachedIndices)
            ? cachedIndices
            : Array.Empty<int>();
    }

    private static ParentRowIndexCacheEntry GetOrCreateParentRowIndexCacheEntry(
        string childTableId,
        string parentRowColumnId)
    {
        for (int cacheEntryIndex = 0; cacheEntryIndex < _parentRowIndexCacheEntries.Count; cacheEntryIndex++)
        {
            ParentRowIndexCacheEntry existingEntry = _parentRowIndexCacheEntries[cacheEntryIndex];
            if (string.Equals(existingEntry.ChildTableId, childTableId, StringComparison.Ordinal) &&
                string.Equals(existingEntry.ParentRowColumnId, parentRowColumnId, StringComparison.Ordinal))
            {
                return existingEntry;
            }
        }

        var createdEntry = new ParentRowIndexCacheEntry
        {
            ChildTableId = childTableId,
            ParentRowColumnId = parentRowColumnId,
        };
        _parentRowIndexCacheEntries.Add(createdEntry);
        return createdEntry;
    }

    private static void RebuildParentRowIndexCache(
        DocWorkspace workspace,
        DocTable childTable,
        string parentRowColumnId,
        ParentRowIndexCacheEntry cacheEntry)
    {
        _parentRowIndexBuildCounts.Clear();
        _parentRowIndexBuildKeys.Clear();

        for (int rowIndex = 0; rowIndex < childTable.Rows.Count; rowIndex++)
        {
            var row = childTable.Rows[rowIndex];
            if (!row.Cells.TryGetValue(parentRowColumnId, out var parentCell))
            {
                continue;
            }

            string parentRowId = parentCell.StringValue ?? "";
            if (string.IsNullOrWhiteSpace(parentRowId))
            {
                continue;
            }

            if (_parentRowIndexBuildCounts.TryGetValue(parentRowId, out int existingCount))
            {
                _parentRowIndexBuildCounts[parentRowId] = existingCount + 1;
            }
            else
            {
                _parentRowIndexBuildCounts[parentRowId] = 1;
            }
        }

        cacheEntry.SourceRowIndicesByParentRowId.Clear();
        foreach (var pair in _parentRowIndexBuildCounts)
        {
            cacheEntry.SourceRowIndicesByParentRowId[pair.Key] = new int[pair.Value];
            _parentRowIndexBuildKeys.Add(pair.Key);
        }

        for (int keyIndex = 0; keyIndex < _parentRowIndexBuildKeys.Count; keyIndex++)
        {
            _parentRowIndexBuildCounts[_parentRowIndexBuildKeys[keyIndex]] = 0;
        }

        for (int rowIndex = 0; rowIndex < childTable.Rows.Count; rowIndex++)
        {
            var row = childTable.Rows[rowIndex];
            if (!row.Cells.TryGetValue(parentRowColumnId, out var parentCell))
            {
                continue;
            }

            string parentRowId = parentCell.StringValue ?? "";
            if (string.IsNullOrWhiteSpace(parentRowId))
            {
                continue;
            }

            if (!cacheEntry.SourceRowIndicesByParentRowId.TryGetValue(parentRowId, out int[]? sourceIndices))
            {
                continue;
            }

            int nextWriteIndex = _parentRowIndexBuildCounts[parentRowId];
            if (nextWriteIndex < 0 || nextWriteIndex >= sourceIndices.Length)
            {
                continue;
            }

            sourceIndices[nextWriteIndex] = rowIndex;
            _parentRowIndexBuildCounts[parentRowId] = nextWriteIndex + 1;
        }

        cacheEntry.ProjectRevision = workspace.ProjectRevision;
    }

    // =====================================================================
    //  Row / cell operations
    // =====================================================================

    private static void InsertRow(DocWorkspace workspace, DocTable table, int index)
    {
        var newRow = new DocRow();
        foreach (var col in table.Columns)
            newRow.SetCell(col.Id, DocCellValue.Default(col));

        ApplySystemTableNewRowDefaults(table, newRow);

        // Auto-set _parentRowId for child subtable rows when navigated from a parent row
        if (table.IsSubtable && !string.IsNullOrEmpty(workspace.ActiveParentRowId) &&
            !string.IsNullOrEmpty(table.ParentRowColumnId))
        {
            newRow.SetCell(table.ParentRowColumnId, DocCellValue.Text(workspace.ActiveParentRowId));
        }

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.AddRow,
            TableId = table.Id,
            RowIndex = index,
            RowSnapshot = newRow
        });
    }

    private static void ApplySystemTableNewRowDefaults(DocTable table, DocRow row)
    {
        if (!table.IsSystemTable)
        {
            return;
        }

        if (string.Equals(table.SystemKey, DocSystemTableKeys.Packages, StringComparison.Ordinal))
        {
            int nextPackageId = 1;
            for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                DocRow existingRow = table.Rows[rowIndex];
                int packageId = (int)Math.Round(existingRow.GetCell("package_id").NumberValue);
                if (packageId >= nextPackageId)
                {
                    nextPackageId = packageId + 1;
                }
            }

            row.SetCell("package_id", DocCellValue.Number(nextPackageId));
            row.SetCell("default_load_from", DocCellValue.Text("Disk"));
            return;
        }

        if (string.Equals(table.SystemKey, DocSystemTableKeys.Exports, StringComparison.Ordinal))
        {
            row.SetCell("enabled", DocCellValue.Bool(true));
            row.SetCell("load_from_override", DocCellValue.Text("Default"));
        }
    }

    private static void DeleteSelectedRows(DocWorkspace workspace, DocTable table)
    {
        workspace.CancelTableCellEditIfActive();

        // Map display indices to source indices, then delete in reverse source order
        var sourceIndices = new List<int>(_selectedRows.Count);
        foreach (int displayIdx in _selectedRows)
        {
            int src = GetSourceRowIndex(displayIdx);
            if (src >= 0 && src < table.Rows.Count)
                sourceIndices.Add(src);
        }
        sourceIndices.Sort();
        for (int i = sourceIndices.Count - 1; i >= 0; i--)
        {
            int rowIdx = sourceIndices[i];
            if (rowIdx < table.Rows.Count)
            {
                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.RemoveRow,
                    TableId = table.Id,
                    RowIndex = rowIdx,
                    RowSnapshot = table.Rows[rowIdx]
                });
            }
        }

        _selectedRows.Clear();
    }

    private static void ClearSelectedCells(DocWorkspace workspace, DocTable table)
    {
        if (!TryGetCellSelectionBounds(out int minRow, out int maxRow, out int minCol, out int maxCol))
        {
            return;
        }

        for (int r = minRow; r <= maxRow; r++)
        {
            int sourceRowIndex = GetSourceRowIndex(r);
            if (sourceRowIndex < 0 || sourceRowIndex >= table.Rows.Count)
            {
                continue;
            }

            for (int c = minCol; c <= maxCol; c++)
            {
                var col = GetVisibleColumn(table, c);
                if (IsColumnDataReadOnly(col) || HasFormulaExpression(col))
                {
                    continue;
                }

                var row = table.Rows[sourceRowIndex];
                var oldCell = row.GetCell(col);
                if (oldCell.HasCellFormulaExpression)
                {
                    continue;
                }

                var newCell = DocCellValue.Default(col);
                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.SetCell,
                    TableId = table.Id,
                    RowId = row.Id,
                    ColumnId = col.Id,
                    OldCellValue = oldCell,
                    NewCellValue = newCell
                });
            }
        }
    }

    // =====================================================================
    //  Utility
    // =====================================================================

    /// <summary>
    /// Simple color blending: tints the background towards the tint color by the given factor.
    /// </summary>
    private static uint BlendColor(uint tint, float factor, uint background)
    {
        byte tR = (byte)(tint & 0xFF);
        byte tG = (byte)((tint >> 8) & 0xFF);
        byte tB = (byte)((tint >> 16) & 0xFF);

        byte bR = (byte)(background & 0xFF);
        byte bG = (byte)((background >> 8) & 0xFF);
        byte bB = (byte)((background >> 16) & 0xFF);

        byte rR = (byte)(bR + (tR - bR) * factor);
        byte rG = (byte)(bG + (tG - bG) * factor);
        byte rB = (byte)(bB + (tB - bB) * factor);

        return 0xFF000000u | ((uint)rB << 16) | ((uint)rG << 8) | rR;
    }

    private static int FindSecondaryKeyIndex(DocTable table, string columnId)
    {
        for (int i = 0; i < table.Keys.SecondaryKeys.Count; i++)
        {
            if (string.Equals(table.Keys.SecondaryKeys[i].ColumnId, columnId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Format an int into a char span. Returns characters written.
    /// </summary>
    private static int FormatInt(int value, Span<char> buffer)
    {
        if (value.TryFormat(buffer, out int written))
            return written;
        return 0;
    }
}
