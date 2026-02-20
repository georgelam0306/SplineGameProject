using System;
using System.Collections.Generic;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Input;
using DerpLib.ImGui.Rendering;
using DerpLib.ImGui.Widgets;
using Derp.Doc.Commands;
using Derp.Doc.Editor;
using Derp.Doc.Model;
using Derp.Doc.Tables;
using FontAwesome.Sharp;

namespace Derp.Doc.Panels;

/// <summary>
/// Block-based document renderer. Follows the SpreadsheetRenderer pattern:
/// static class that owns layout and rendering for the document editor.
/// </summary>
internal static class DocumentRenderer
{
    // --- Layout constants ---
    private const float ContentMaxWidth = float.MaxValue;
    private const float BlockPaddingY = 4f;
    private const float GutterWidth = 28f;
    private const float SideMargin = 0f;
    private static float ScrollbarWidth => Im.Style.ScrollbarWidth;
    private const float IndentWidth = 24f;
    private const float DragHandleLaneWidth = 18f;
    private const float TableBlockVerticalInset = 4f;
    private const float ContentPaddingRight = 8f;
    private const float EmbeddedChartDefaultContentHeight = 320f;
    private const float EmbeddedResizeHandleSize = 13f;
    private const float EmbeddedResizeMinWidth = 240f;
    private const float EmbeddedResizeMinHeight = 180f;
    private const string BlockContextMenuId = "doc_block_context_menu";
    private const string FormulaBlockModalId = "doc_formula_block_modal";
    private const string VariableBlockModalId = "doc_variable_block_modal";
    private const float VariableTypeaheadMenuWidth = 260f;
    private const float VariableTypeaheadRowHeight = 28f;
    private const int MaxVariableTypeaheadEntries = 128;

    // --- Per-block layout cache ---
    private static readonly float[] _blockY = new float[1024];
    private static readonly float[] _blockH = new float[1024];
    private static readonly float[] _blockW = new float[1024];
    private static readonly HashSet<string> _blockIdValidationSet = new(StringComparer.Ordinal);
    private static readonly DocFormulaEngine _documentFormulaPreviewEngine = new();
    private static readonly Dictionary<string, FormulaDisplayCacheEntry> _formulaDisplayCacheByBlockId = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, VariableValueDisplayCacheEntry> _variableValueDisplayCacheByBlockId = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, VariableReferenceValueDisplayCacheEntry> _variableReferenceValueDisplayCacheByBlockId = new(StringComparer.Ordinal);

    // --- Scroll ---
    private static float _scrollY;
    private static ImRect _lastContentRect;
    private static float _lastColumnX;
    private static float _lastColumnW;

    // --- Auto-focus tracking ---
    private static string? _lastFocusedDocumentId;

    private sealed class DocumentTabViewState
    {
        public float ScrollY;
        public ImRect LastContentRect;
        public float LastColumnX;
        public float LastColumnW;
        public string? LastFocusedDocumentId;
        public int HoveredBlockIndex = -1;
        public int DragSourceIndex = -1;
        public int DragInsertIndex = -1;
        public bool IsDragging;
        public float DragStartY;
        public int MultiSelectAnchor = -1;
        public int MultiSelectEnd = -1;
        public bool IsMultiSelectDragging;
        public bool HasMultiBlockTextSelection;
        public int MultiTextStartBlock = -1;
        public int MultiTextStartOffset = -1;
        public int MultiTextEndBlock = -1;
        public int MultiTextEndOffset = -1;
        public int MultiTextFocusBlock = -1;
        public int MultiTextFocusSelectionStart = -1;
        public int MultiTextFocusSelectionEnd = -1;
        public bool IsEmbeddedChartResizing;
        public string EmbeddedResizeDocumentId = "";
        public string EmbeddedResizeBlockId = "";
        public float EmbeddedResizeStartMouseX;
        public float EmbeddedResizeStartMouseY;
        public float EmbeddedResizeStartWidth;
        public float EmbeddedResizeStartHeight;
        public float EmbeddedResizePreviewWidth;
        public float EmbeddedResizePreviewHeight;
        public int PendingFocusBlock = -1;
        public int PendingFocusCaretPos = -1;
        public string PendingRevealBlockId = "";
        public string ContextMenuDocumentId = "";
        public string ContextMenuBlockId = "";
        public bool VariableTypeaheadOpen;
        public ImRect VariableTypeaheadMenuRect;
        public int VariableTypeaheadReplaceStart;
        public int VariableTypeaheadReplaceEnd;
        public int VariableTypeaheadSelectedIndex;
    }

    private static readonly Dictionary<string, DocumentTabViewState> TabViewStatesByStateKey = new(StringComparer.Ordinal);
    private static string _activeStateKey = "";

    // --- Text edit buffers (one per block, lazily allocated) ---
    private static readonly char[] _editBuffer = new char[4096];
    private static int _editBufferLength;

    // --- Hover ---
    private static int _hoveredBlockIndex = -1;

    // --- Block drag reorder ---
    private static int _dragSourceIndex = -1;
    private static int _dragInsertIndex = -1;
    private static bool _isDragging;
    private static float _dragStartY;

    // --- Multi-block selection ---
    private static int _multiSelectAnchor = -1;
    private static int _multiSelectEnd = -1;
    private static bool _isMultiSelectDragging;
    private static bool _hasMultiBlockTextSelection;
    private static int _multiTextStartBlock = -1;
    private static int _multiTextStartOffset = -1;
    private static int _multiTextEndBlock = -1;
    private static int _multiTextEndOffset = -1;
    private static int _multiTextFocusBlock = -1;
    private static int _multiTextFocusSelectionStart = -1;
    private static int _multiTextFocusSelectionEnd = -1;
    private static RichTextLayout.VisualLine[] _textSelectionVisualLines = new RichTextLayout.VisualLine[512];
    private static RichTextLayout.VisualLine[] _layoutVisualLines = new RichTextLayout.VisualLine[512];

    // --- Embedded chart resize ---
    private static bool _isEmbeddedChartResizing;
    private static string _embeddedResizeDocumentId = "";
    private static string _embeddedResizeBlockId = "";
    private static float _embeddedResizeStartMouseX;
    private static float _embeddedResizeStartMouseY;
    private static float _embeddedResizeStartWidth;
    private static float _embeddedResizeStartHeight;
    private static float _embeddedResizePreviewWidth;
    private static float _embeddedResizePreviewHeight;

    // --- Focus request (deferred from click) ---
    private static int _pendingFocusBlock = -1;
    private static int _pendingFocusCaretPos = -1;
    private static int _focusedBlockWidgetId;
    private static string _pendingRevealBlockId = "";
    private static string _contextMenuDocumentId = "";
    private static string _contextMenuBlockId = "";
    private static readonly string _variableBlockIcon = ((char)IconChar.Tag).ToString();
    private static readonly string _documentBlockIcon = ((char)IconChar.File).ToString();
    private static readonly char[] _editFormulaBlockBuffer = new char[1024];
    private static int _editFormulaBlockBufferLength;
    private static string _editFormulaBlockDocumentId = "";
    private static string _editFormulaBlockId = "";
    private static readonly char[] _editVariableNameBuffer = new char[128];
    private static int _editVariableNameBufferLength;
    private static readonly char[] _editVariableFormulaBuffer = new char[2048];
    private static int _editVariableFormulaBufferLength;
    private static string _editVariableBlockDocumentId = "";
    private static string _editVariableBlockId = "";
    private static string _editVariableValidationMessage = "";
    private static readonly string[] _variableTypeaheadEntries = new string[MaxVariableTypeaheadEntries];
    private static int _variableTypeaheadEntryCount;
    private static int _variableTypeaheadSelectedIndex;
    private static bool _variableTypeaheadOpen;
    private static ImRect _variableTypeaheadMenuRect;
    private static int _variableTypeaheadReplaceStart;
    private static int _variableTypeaheadReplaceEnd;
    private static bool _variableTypeaheadConsumedKeyThisFrame;
    private static readonly string[] _cachedDocumentVariableNames = new string[MaxVariableTypeaheadEntries];
    private static int _cachedDocumentVariableNameCount;
    private static int _cachedDocumentVariableNamesRevision = -1;
    private static string _cachedDocumentVariableNamesDocumentId = "";
    private static readonly HashSet<string> _cachedDocumentVariableNameSet = new(StringComparer.OrdinalIgnoreCase);

    private readonly struct FormulaDisplayCacheEntry
    {
        public FormulaDisplayCacheEntry(int projectRevision, string formulaText, string resultText, bool isValid)
        {
            ProjectRevision = projectRevision;
            FormulaText = formulaText;
            ResultText = resultText;
            IsValid = isValid;
        }

        public int ProjectRevision { get; }
        public string FormulaText { get; }
        public string ResultText { get; }
        public bool IsValid { get; }
    }

    private readonly struct VariableValueDisplayCacheEntry
    {
        public VariableValueDisplayCacheEntry(int projectRevision, string expressionText, string resultText, bool isValid)
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

    private readonly struct VariableReferenceValueDisplayCacheEntry
    {
        public VariableReferenceValueDisplayCacheEntry(int projectRevision, string variableName, string resultText, bool isValid)
        {
            ProjectRevision = projectRevision;
            VariableName = variableName;
            ResultText = resultText;
            IsValid = isValid;
        }

        public int ProjectRevision { get; }
        public string VariableName { get; }
        public string ResultText { get; }
        public bool IsValid { get; }
    }

    public static void Draw(DocWorkspace workspace)
    {
        var document = workspace.ActiveDocument;
        if (document == null)
        {
            Im.LabelText("No document selected");
            return;
        }

        string stateKey = workspace.ContentTabs.ActiveTab?.StateKey ?? "";
        BeginTabStateScope(stateKey);
        try
        {
        var raw = Im.WindowContentRect;
        var contentRect = new ImRect(raw.X, raw.Y, raw.Width - 8f, raw.Height);
        if (contentRect.Width <= 0 || contentRect.Height <= 0) return;

        EnsureUniqueBlockIds(document);

        _focusedBlockWidgetId = 0;

        // Content pane runs darker than side panes to preserve plane separation.
        uint contentBackground = ImStyle.Lerp(Im.Style.Background, 0xFF000000, 0.24f);
        Im.DrawRect(contentRect.X, contentRect.Y, contentRect.Width, contentRect.Height, contentBackground);

        // Document switch housekeeping.
        // Do not auto-focus block 0 when opening a document; preserve/restore explicit focus only.
        if (document.Id != _lastFocusedDocumentId)
        {
            _lastFocusedDocumentId = document.Id;
            CloseVariableTypeahead();
            _hoveredBlockIndex = -1;
            _dragSourceIndex = -1;
            _dragInsertIndex = -1;
            _isDragging = false;
            _multiSelectAnchor = -1;
            _multiSelectEnd = -1;
            _isMultiSelectDragging = false;
            ClearMultiBlockTextSelection();
            _pendingFocusBlock = -1;
            _pendingFocusCaretPos = -1;

            if (workspace.FocusedBlockIndex < 0 ||
                workspace.FocusedBlockIndex >= document.Blocks.Count)
            {
                workspace.FocusedBlockIndex = -1;
                workspace.FocusedBlockTextSnapshot = null;
                _editBufferLength = 0;
                Im.Context.ClearFocus();
            }
        }

        var input = Im.Context.Input;
        _variableTypeaheadConsumedKeyThisFrame = false;

        // Compute block layout
        float totalContentHeight = ComputeLayout(workspace, document, contentRect);
        RevealPendingBlockIfNeeded(document, contentRect, totalContentHeight);

        // Content area (minus scrollbar)
        bool needsScroll = totalContentHeight > contentRect.Height;
        float contentW = contentRect.Width - (needsScroll ? ScrollbarWidth : 0f);

        // Full-width document column, but keep a dedicated visible lane for drag handles.
        float columnX = contentRect.X + DragHandleLaneWidth;
        float columnW = Math.Max(0f, contentW - DragHandleLaneWidth);
        _lastContentRect = contentRect;
        _lastColumnX = columnX;
        _lastColumnW = columnW;

        // Prime overlay capture for the floating selection toolbar before content input runs.
        // This prevents click-through into the active text area beneath the toolbar.
        if (SelectionToolbar.TryGetToolbarRect(workspace, document, out var selectionToolbarRect))
        {
            ImPopover.AddCaptureRectLocal(selectionToolbarRect);
        }

        // Scroll view
        var scrollRect = new ImRect(contentRect.X, contentRect.Y, contentW, contentRect.Height);
        ImScrollView.Begin(scrollRect, totalContentHeight, ref _scrollY, handleMouseWheel: false);

        // Handle input
        HandleInput(workspace, document, contentRect, columnX, columnW, totalContentHeight, input);

        // Draw blocks
        int blockCount = document.Blocks.Count;
        for (int i = 0; i < blockCount && i < 1024; i++)
        {
            var block = document.Blocks[i];
            float indentOffset = block.IndentLevel * IndentWidth;
            float bx = columnX + GutterWidth + indentOffset;
            float by = _blockY[i];
            float defaultWidth = Math.Max(24f, columnW - GutterWidth - indentOffset);
            float bw = _blockW[i] > 0f ? _blockW[i] : defaultWidth;
            float bh = _blockH[i];

            // Culling: skip blocks entirely above or below viewport
            float screenY = by - _scrollY;
            if (screenY + bh < contentRect.Y - 50 || screenY > contentRect.Y + contentRect.Height + 50)
                continue;

            // Multi-block selection highlight
            if (_multiSelectAnchor >= 0 && _multiSelectEnd >= 0)
            {
                int msStart = Math.Min(_multiSelectAnchor, _multiSelectEnd);
                int msEnd = Math.Max(_multiSelectAnchor, _multiSelectEnd);
                if (i >= msStart && i <= msEnd)
                {
                    Im.DrawRect(columnX, by, columnW, bh,
                        BlendColor(Im.Style.Primary, 0.16f, Im.Style.Background));
                }
            }
            // Hover highlight
            else if (i == _hoveredBlockIndex && i != workspace.FocusedBlockIndex)
            {
                Im.DrawRect(columnX, by, columnW, bh, BlendColor(Im.Style.Hover, 0.70f, Im.Style.Background));
            }

            // Drag handle — fixed lane to the left of columnX, independent of indent
            if (i == _hoveredBlockIndex && block.Type != DocBlockType.Divider)
            {
                float handleX = columnX - DragHandleLaneWidth * 0.5f;
                float handleY = by + bh * 0.5f - 8f;
                uint handleColor = ImStyle.WithAlpha(Im.Style.TextSecondary, 176);
                for (int row = 0; row < 3; row++)
                {
                    float dotY = handleY + row * 5f;
                    Im.DrawRoundedRect(handleX - 4f, dotY, 3f, 3f, 1.5f, handleColor);
                    Im.DrawRoundedRect(handleX + 1f, dotY, 3f, 3f, 1.5f, handleColor);
                }
            }

            // Block type visuals
            DrawBlockGutter(block, i, document, columnX + indentOffset, by, bh);
            DrawBlockBackground(block, bx, by, bw, bh);

            if (block.Type == DocBlockType.Table)
            {
                DrawTableBlock(workspace, document, block, bx, by, bw, bh, i == workspace.FocusedBlockIndex);
            }
            else if (i == workspace.FocusedBlockIndex)
            {
                // Active block — use ImTextArea for editing
                DrawActiveBlock(workspace, document, block, i, bx, by, bw, bh);
            }
            else
            {
                DrawMultiBlockTextSelectionOverlay(block, i, bx, by, bw);
                // Inactive block — render static text
                DrawStaticBlock(workspace, document, block, bx, by, bw, bh);
            }
        }

        // Drag insertion indicator
        if (_isDragging && _dragInsertIndex >= 0)
        {
            float indicatorY;
            int blockCount2 = Math.Min(document.Blocks.Count, 1024);
            if (_dragInsertIndex < blockCount2)
                indicatorY = _blockY[_dragInsertIndex] - 1f;
            else if (blockCount2 > 0)
                indicatorY = _blockY[blockCount2 - 1] + _blockH[blockCount2 - 1] + 1f;
            else
                indicatorY = contentRect.Y;

            Im.DrawRect(columnX, indicatorY, columnW, 2f, Im.Style.Primary);
            // Draw small circles at endpoints
            Im.DrawRoundedRect(columnX - 3f, indicatorY - 3f, 8f, 8f, 4f, Im.Style.Primary);
            Im.DrawRoundedRect(columnX + columnW - 5f, indicatorY - 3f, 8f, 8f, 4f, Im.Style.Primary);
        }

        // Scroll view end
        var scrollbarRect = new ImRect(
            contentRect.X + contentW, contentRect.Y,
            ScrollbarWidth, contentRect.Height);
        int scrollId = "doc_scroll".GetHashCode();
        ImScrollView.End(scrollId, scrollbarRect, contentRect.Height, totalContentHeight, ref _scrollY);
        UpdateVariableTypeaheadState(workspace, document, input, columnX, contentRect);

        // Cross-block navigation (Up/Down arrow past block boundary)
        // Suppressed when slash command menu is open (Up/Down navigate the menu instead)
        if (workspace.FocusedBlockIndex >= 0 &&
            !SlashCommandMenu.IsOpen &&
            !IsVariableTypeaheadOpen() &&
            IsFocusedBlockTextEditable(workspace, document))
        {
            if (ImRichTextArea.NavigatedPastStart)
            {
                int prevIndex = workspace.FocusedBlockIndex - 1;
                while (prevIndex >= 0 && document.Blocks[prevIndex].Type == DocBlockType.Divider)
                    prevIndex--;
                if (prevIndex >= 0)
                {
                    CommitFocusedBlock(workspace, document);
                    var prevBlock = document.Blocks[prevIndex];
                    FocusBlock(workspace, document, prevIndex, prevBlock.Text.PlainText.Length);
                }
            }
            else if (ImRichTextArea.NavigatedPastEnd)
            {
                int nextIndex = workspace.FocusedBlockIndex + 1;
                while (nextIndex < document.Blocks.Count && document.Blocks[nextIndex].Type == DocBlockType.Divider)
                    nextIndex++;
                if (nextIndex < document.Blocks.Count)
                {
                    CommitFocusedBlock(workspace, document);
                    FocusBlock(workspace, document, nextIndex, 0);
                }
            }
        }

        // Handle block boundary keys (Enter, Backspace) after rendering
        // Suppressed when slash command menu is open (Enter selects from menu)
        bool hasBlockBoundaryKey = input.KeyEnter || input.KeyBackspace || input.KeyTab;
        if (hasBlockBoundaryKey &&
            workspace.FocusedBlockIndex >= 0 &&
            !SlashCommandMenu.IsOpen &&
            !IsVariableTypeaheadOpen() &&
            !_variableTypeaheadConsumedKeyThisFrame &&
            IsFocusedBlockTextEditable(workspace, document) &&
            _focusedBlockWidgetId != 0 &&
            Im.Context.IsFocused(_focusedBlockWidgetId))
        {
            BlockEditor.HandleKeys(workspace, document, input);
        }

        bool hasMultiBlockSelection = _multiSelectAnchor >= 0 && _multiSelectEnd >= 0;
        bool hasMultiBlockTextSelection = _hasMultiBlockTextSelection &&
                                          _multiTextStartBlock >= 0 &&
                                          _multiTextEndBlock >= _multiTextStartBlock;
        if (hasMultiBlockSelection &&
            !SlashCommandMenu.IsOpen &&
            !Im.Context.WantCaptureKeyboard &&
            (input.KeyBackspace || input.KeyDelete))
        {
            int selectedStart = Math.Max(0, Math.Min(_multiSelectAnchor, _multiSelectEnd));
            int selectedEnd = Math.Min(document.Blocks.Count - 1, Math.Max(_multiSelectAnchor, _multiSelectEnd));
            if (selectedEnd >= selectedStart && document.Blocks.Count > 0)
            {
                CommitFocusedBlock(workspace, document);
                var commands = new List<DocCommand>(selectedEnd - selectedStart + 1);
                for (int blockIndex = selectedEnd; blockIndex >= selectedStart; blockIndex--)
                {
                    var removeBlock = document.Blocks[blockIndex];
                    commands.Add(new DocCommand
                    {
                        Kind = DocCommandKind.RemoveBlock,
                        DocumentId = document.Id,
                        BlockIndex = blockIndex,
                        BlockSnapshot = removeBlock.Clone(),
                    });
                }

                workspace.ExecuteCommands(commands);
                _multiSelectAnchor = -1;
                _multiSelectEnd = -1;

                if (document.Blocks.Count > 0)
                {
                    int nextFocusIndex = Math.Clamp(selectedStart, 0, document.Blocks.Count - 1);
                    var nextFocusBlock = document.Blocks[nextFocusIndex];
                    int nextCaretPos = IsTextEditableBlock(nextFocusBlock.Type)
                        ? nextFocusBlock.Text.PlainText.Length
                        : 0;
                    FocusBlock(workspace, document, nextFocusIndex, nextCaretPos);
                }
                else
                {
                    workspace.FocusedBlockIndex = -1;
                    workspace.FocusedBlockTextSnapshot = null;
                    Im.Context.ClearFocus();
                }
            }
        }

        // Delete/Backspace on focused non-text-editable blocks (e.g. Table, Divider)
        if (workspace.FocusedBlockIndex >= 0 &&
            !SlashCommandMenu.IsOpen &&
            !hasMultiBlockSelection &&
            !IsFocusedBlockTextEditable(workspace, document) &&
            !Im.Context.WantCaptureKeyboard &&
            (input.KeyBackspace || input.KeyDelete))
        {
            int removeIndex = workspace.FocusedBlockIndex;
            var removeBlock = document.Blocks[removeIndex];

            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.RemoveBlock,
                DocumentId = document.Id,
                BlockIndex = removeIndex,
                BlockSnapshot = removeBlock.Clone(),
            });

            // Focus adjacent block
            if (document.Blocks.Count > 0)
            {
                int newFocus = Math.Min(removeIndex, document.Blocks.Count - 1);
                var nextBlock = document.Blocks[newFocus];
                int caretPos = IsTextEditableBlock(nextBlock.Type) ? nextBlock.Text.PlainText.Length : 0;
                FocusBlock(workspace, document, newFocus, caretPos);
            }
            else
            {
                workspace.FocusedBlockIndex = -1;
            }
        }

        // Ctrl+B/I/U style toggle from ImRichTextArea
        if (workspace.FocusedBlockIndex >= 0 && ImRichTextArea.PendingStyleToggle is { } toggleStyle)
        {
            var toggleBlock = document.Blocks[workspace.FocusedBlockIndex];
            if (GetSelection(toggleBlock, out int selStart, out int selEnd))
            {
                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.ToggleSpan,
                    DocumentId = document.Id,
                    BlockId = toggleBlock.Id,
                    SpanStart = selStart,
                    SpanLength = selEnd - selStart,
                    SpanStyle = toggleStyle,
                });
            }
        }

        // Cross-block text selection copy (Ctrl+C)
        if (hasMultiBlockTextSelection && !Im.Context.WantCaptureKeyboard && input.KeyCtrlC)
        {
            string selectedText = BuildMultiBlockSelectedText(document);
            if (!string.IsNullOrEmpty(selectedText))
            {
                DerpLib.Derp.SetClipboardText(selectedText);
            }
        }
        // Multi-block block-selection copy (Ctrl+C)
        else if (_multiSelectAnchor >= 0 && _multiSelectEnd >= 0 && !Im.Context.WantCaptureKeyboard && input.KeyCtrlC)
        {
            int msStart = Math.Min(_multiSelectAnchor, _multiSelectEnd);
            int msEnd = Math.Max(_multiSelectAnchor, _multiSelectEnd);
            var sb = new System.Text.StringBuilder();
            for (int i = msStart; i <= msEnd && i < document.Blocks.Count; i++)
            {
                if (i > msStart) sb.Append('\n');
                sb.Append(document.Blocks[i].Text.PlainText);
            }
            DerpLib.Derp.SetClipboardText(sb.ToString());
        }

        if (!Im.Context.WantCaptureKeyboard && input.KeyCtrlA && document.Blocks.Count > 0)
        {
            _multiSelectAnchor = 0;
            _multiSelectEnd = document.Blocks.Count - 1;
            ClearMultiBlockTextSelection();
        }

        // Clear multi-block selection on Escape
        if (input.KeyEscape &&
            !Im.Context.WantCaptureKeyboard &&
            (_multiSelectAnchor >= 0 || hasMultiBlockTextSelection))
        {
            _multiSelectAnchor = -1;
            _multiSelectEnd = -1;
            ClearMultiBlockTextSelection();
        }

        // Slash command menu
        SlashCommandMenu.Draw(workspace, document, columnX + GutterWidth, input);
        if (SlashCommandMenu.IsOpen)
        {
            CloseVariableTypeahead();
        }
        else
        {
            DrawVariableTypeaheadMenu(workspace, document, input);
        }

        // Markdown shortcuts
        if (workspace.FocusedBlockIndex >= 0 && IsFocusedBlockTextEditable(workspace, document))
            MarkdownShortcuts.Check(workspace, document);

        // Selection toolbar
        if (workspace.FocusedBlockIndex >= 0 && IsFocusedBlockTextEditable(workspace, document))
            SelectionToolbar.Draw(workspace, document);

        DrawBlockContextMenu(workspace);
        DrawFormulaBlockModal(workspace);
        DrawVariableBlockModal(workspace);
        }
        finally
        {
            EndTabStateScope();
        }
    }

    private static void BeginTabStateScope(string stateKey)
    {
        if (string.IsNullOrWhiteSpace(stateKey))
        {
            _activeStateKey = "";
            return;
        }

        _activeStateKey = stateKey;
        if (!TabViewStatesByStateKey.TryGetValue(stateKey, out var state))
        {
            state = new DocumentTabViewState();
            TabViewStatesByStateKey[stateKey] = state;
        }

        _scrollY = state.ScrollY;
        _lastContentRect = state.LastContentRect;
        _lastColumnX = state.LastColumnX;
        _lastColumnW = state.LastColumnW;
        _lastFocusedDocumentId = state.LastFocusedDocumentId;
        _hoveredBlockIndex = state.HoveredBlockIndex;
        _dragSourceIndex = state.DragSourceIndex;
        _dragInsertIndex = state.DragInsertIndex;
        _isDragging = state.IsDragging;
        _dragStartY = state.DragStartY;
        _multiSelectAnchor = state.MultiSelectAnchor;
        _multiSelectEnd = state.MultiSelectEnd;
        _isMultiSelectDragging = state.IsMultiSelectDragging;
        _hasMultiBlockTextSelection = state.HasMultiBlockTextSelection;
        _multiTextStartBlock = state.MultiTextStartBlock;
        _multiTextStartOffset = state.MultiTextStartOffset;
        _multiTextEndBlock = state.MultiTextEndBlock;
        _multiTextEndOffset = state.MultiTextEndOffset;
        _multiTextFocusBlock = state.MultiTextFocusBlock;
        _multiTextFocusSelectionStart = state.MultiTextFocusSelectionStart;
        _multiTextFocusSelectionEnd = state.MultiTextFocusSelectionEnd;
        _isEmbeddedChartResizing = state.IsEmbeddedChartResizing;
        _embeddedResizeDocumentId = state.EmbeddedResizeDocumentId ?? "";
        _embeddedResizeBlockId = state.EmbeddedResizeBlockId ?? "";
        _embeddedResizeStartMouseX = state.EmbeddedResizeStartMouseX;
        _embeddedResizeStartMouseY = state.EmbeddedResizeStartMouseY;
        _embeddedResizeStartWidth = state.EmbeddedResizeStartWidth;
        _embeddedResizeStartHeight = state.EmbeddedResizeStartHeight;
        _embeddedResizePreviewWidth = state.EmbeddedResizePreviewWidth;
        _embeddedResizePreviewHeight = state.EmbeddedResizePreviewHeight;
        // Pending focus is a frame-transient interaction detail.
        // Never restore it from tab state to avoid stale cross-frame focus jumps.
        _pendingFocusBlock = -1;
        _pendingFocusCaretPos = -1;
        _pendingRevealBlockId = state.PendingRevealBlockId ?? "";
        _contextMenuDocumentId = state.ContextMenuDocumentId ?? "";
        _contextMenuBlockId = state.ContextMenuBlockId ?? "";
        _variableTypeaheadOpen = state.VariableTypeaheadOpen;
        _variableTypeaheadMenuRect = state.VariableTypeaheadMenuRect;
        _variableTypeaheadReplaceStart = state.VariableTypeaheadReplaceStart;
        _variableTypeaheadReplaceEnd = state.VariableTypeaheadReplaceEnd;
        _variableTypeaheadSelectedIndex = state.VariableTypeaheadSelectedIndex;
    }

    private static void EndTabStateScope()
    {
        if (string.IsNullOrWhiteSpace(_activeStateKey))
        {
            return;
        }

        if (!TabViewStatesByStateKey.TryGetValue(_activeStateKey, out var state))
        {
            _activeStateKey = "";
            return;
        }

        state.ScrollY = _scrollY;
        state.LastContentRect = _lastContentRect;
        state.LastColumnX = _lastColumnX;
        state.LastColumnW = _lastColumnW;
        state.LastFocusedDocumentId = _lastFocusedDocumentId;
        state.HoveredBlockIndex = _hoveredBlockIndex;
        state.DragSourceIndex = _dragSourceIndex;
        state.DragInsertIndex = _dragInsertIndex;
        state.IsDragging = _isDragging;
        state.DragStartY = _dragStartY;
        state.MultiSelectAnchor = _multiSelectAnchor;
        state.MultiSelectEnd = _multiSelectEnd;
        state.IsMultiSelectDragging = _isMultiSelectDragging;
        state.HasMultiBlockTextSelection = _hasMultiBlockTextSelection;
        state.MultiTextStartBlock = _multiTextStartBlock;
        state.MultiTextStartOffset = _multiTextStartOffset;
        state.MultiTextEndBlock = _multiTextEndBlock;
        state.MultiTextEndOffset = _multiTextEndOffset;
        state.MultiTextFocusBlock = _multiTextFocusBlock;
        state.MultiTextFocusSelectionStart = _multiTextFocusSelectionStart;
        state.MultiTextFocusSelectionEnd = _multiTextFocusSelectionEnd;
        state.IsEmbeddedChartResizing = _isEmbeddedChartResizing;
        state.EmbeddedResizeDocumentId = _embeddedResizeDocumentId ?? "";
        state.EmbeddedResizeBlockId = _embeddedResizeBlockId ?? "";
        state.EmbeddedResizeStartMouseX = _embeddedResizeStartMouseX;
        state.EmbeddedResizeStartMouseY = _embeddedResizeStartMouseY;
        state.EmbeddedResizeStartWidth = _embeddedResizeStartWidth;
        state.EmbeddedResizeStartHeight = _embeddedResizeStartHeight;
        state.EmbeddedResizePreviewWidth = _embeddedResizePreviewWidth;
        state.EmbeddedResizePreviewHeight = _embeddedResizePreviewHeight;
        state.PendingRevealBlockId = _pendingRevealBlockId ?? "";
        state.ContextMenuDocumentId = _contextMenuDocumentId ?? "";
        state.ContextMenuBlockId = _contextMenuBlockId ?? "";
        state.VariableTypeaheadOpen = _variableTypeaheadOpen;
        state.VariableTypeaheadMenuRect = _variableTypeaheadMenuRect;
        state.VariableTypeaheadReplaceStart = _variableTypeaheadReplaceStart;
        state.VariableTypeaheadReplaceEnd = _variableTypeaheadReplaceEnd;
        state.VariableTypeaheadSelectedIndex = _variableTypeaheadSelectedIndex;

        _activeStateKey = "";
    }

    private static bool IsVariableTypeaheadOpen()
    {
        return _variableTypeaheadOpen && _variableTypeaheadEntryCount > 0;
    }

    private static void CloseVariableTypeahead()
    {
        _variableTypeaheadOpen = false;
        _variableTypeaheadEntryCount = 0;
        _variableTypeaheadSelectedIndex = 0;
        _variableTypeaheadReplaceStart = 0;
        _variableTypeaheadReplaceEnd = 0;
        _variableTypeaheadMenuRect = default;
    }

    private static void UpdateVariableTypeaheadState(
        DocWorkspace workspace,
        DocDocument document,
        ImInput input,
        float columnX,
        ImRect contentRect)
    {
        if (workspace.FocusedBlockIndex < 0 ||
            workspace.FocusedBlockIndex >= document.Blocks.Count ||
            !IsFocusedBlockTextEditable(workspace, document) ||
            _focusedBlockWidgetId == 0 ||
            !Im.Context.IsFocused(_focusedBlockWidgetId) ||
            SlashCommandMenu.IsOpen)
        {
            CloseVariableTypeahead();
            return;
        }

        if (!ImRichTextArea.TryGetState(_focusedBlockWidgetId, out int caretPos, out int selectionStart, out int selectionEnd))
        {
            CloseVariableTypeahead();
            return;
        }

        if (selectionStart >= 0 && selectionEnd >= 0 && selectionStart != selectionEnd)
        {
            CloseVariableTypeahead();
            return;
        }

        if (!TryComputeVariableTypeaheadFragment(
                caretPos,
                out int replaceStart,
                out int replaceEnd))
        {
            CloseVariableTypeahead();
            return;
        }

        RefreshDocumentVariableNameCache(workspace, document);
        ReadOnlySpan<char> fragment = _editBuffer.AsSpan(replaceStart, replaceEnd - replaceStart);
        BuildVariableTypeaheadEntries(fragment);
        if (_variableTypeaheadEntryCount <= 0)
        {
            CloseVariableTypeahead();
            return;
        }

        _variableTypeaheadOpen = true;
        _variableTypeaheadReplaceStart = replaceStart;
        _variableTypeaheadReplaceEnd = replaceEnd;
        _variableTypeaheadSelectedIndex = Math.Clamp(_variableTypeaheadSelectedIndex, 0, _variableTypeaheadEntryCount - 1);

        if (input.KeyDown)
        {
            _variableTypeaheadSelectedIndex = (_variableTypeaheadSelectedIndex + 1) % _variableTypeaheadEntryCount;
        }
        else if (input.KeyUp)
        {
            _variableTypeaheadSelectedIndex = (_variableTypeaheadSelectedIndex - 1 + _variableTypeaheadEntryCount) % _variableTypeaheadEntryCount;
        }

        if (input.KeyEscape)
        {
            _variableTypeaheadConsumedKeyThisFrame = true;
            CloseVariableTypeahead();
            return;
        }

        if (input.KeyTab || input.KeyEnter)
        {
            _variableTypeaheadConsumedKeyThisFrame = true;
            ApplyVariableTypeaheadEntry(workspace, document, _variableTypeaheadSelectedIndex);
            return;
        }

        float menuHeight = ComputeVariableTypeaheadMenuHeight();
        float blockScreenY = GetBlockScreenY(workspace.FocusedBlockIndex);
        float blockHeight = GetBlockHeight(workspace.FocusedBlockIndex);
        float indentOffset = document.Blocks[workspace.FocusedBlockIndex].IndentLevel * IndentWidth;
        float menuX = columnX + GutterWidth + indentOffset;
        float menuY = blockScreenY + blockHeight;
        float viewportBottom = contentRect.Y + contentRect.Height;
        if (menuY + menuHeight > viewportBottom)
        {
            menuY = blockScreenY - menuHeight - 4f;
        }

        _variableTypeaheadMenuRect = new ImRect(menuX, menuY, VariableTypeaheadMenuWidth, menuHeight);
    }

    private static void DrawVariableTypeaheadMenu(DocWorkspace workspace, DocDocument document, ImInput input)
    {
        if (!IsVariableTypeaheadOpen())
        {
            return;
        }

        int visibleCount = Math.Min(6, _variableTypeaheadEntryCount);
        if (visibleCount <= 0)
        {
            return;
        }

        var style = Im.Style;
        using var popoverScope = ImPopover.PushOverlayScopeLocal(_variableTypeaheadMenuRect);
        Im.DrawRoundedRect(
            _variableTypeaheadMenuRect.X,
            _variableTypeaheadMenuRect.Y,
            _variableTypeaheadMenuRect.Width,
            _variableTypeaheadMenuRect.Height,
            8f,
            style.Surface);
        Im.DrawRoundedRectStroke(
            _variableTypeaheadMenuRect.X,
            _variableTypeaheadMenuRect.Y,
            _variableTypeaheadMenuRect.Width,
            _variableTypeaheadMenuRect.Height,
            8f,
            style.Border,
            1f);

        int maxFirst = Math.Max(0, _variableTypeaheadEntryCount - visibleCount);
        int firstVisible = Math.Clamp(_variableTypeaheadSelectedIndex - (visibleCount - 1), 0, maxFirst);
        float rowY = _variableTypeaheadMenuRect.Y + 4f;
        for (int visibleIndex = 0; visibleIndex < visibleCount; visibleIndex++)
        {
            int entryIndex = firstVisible + visibleIndex;
            bool selected = entryIndex == _variableTypeaheadSelectedIndex;
            var rowRect = new ImRect(
                _variableTypeaheadMenuRect.X + 4f,
                rowY,
                _variableTypeaheadMenuRect.Width - 8f,
                VariableTypeaheadRowHeight);
            bool hovered = rowRect.Contains(Im.MousePos);
            if (hovered)
            {
                _variableTypeaheadSelectedIndex = entryIndex;
            }

            if (selected || hovered)
            {
                uint rowColor = selected ? style.Hover : ImStyle.WithAlpha(style.Hover, 180);
                Im.DrawRoundedRect(rowRect.X, rowRect.Y, rowRect.Width, rowRect.Height, 4f, rowColor);
            }

            string entry = _variableTypeaheadEntries[entryIndex];
            string displayText = "@" + entry;
            float textY = rowRect.Y + (rowRect.Height - style.FontSize) * 0.5f;
            Im.Text(displayText.AsSpan(), rowRect.X + 10f, textY, style.FontSize, style.TextPrimary);

            if (hovered && input.MousePressed)
            {
                ApplyVariableTypeaheadEntry(workspace, document, entryIndex);
                return;
            }

            rowY += VariableTypeaheadRowHeight;
        }
    }

    private static float ComputeVariableTypeaheadMenuHeight()
    {
        int visibleCount = Math.Min(6, _variableTypeaheadEntryCount);
        if (visibleCount <= 0)
        {
            return 0f;
        }

        return visibleCount * VariableTypeaheadRowHeight + 8f;
    }

    private static bool TryComputeVariableTypeaheadFragment(
        int caretPos,
        out int replaceStart,
        out int replaceEnd)
    {
        replaceStart = 0;
        replaceEnd = 0;
        if (caretPos < 0 || caretPos > _editBufferLength)
        {
            return false;
        }

        int fragmentStart = caretPos;
        while (fragmentStart > 0 && IsDocumentVariableIdentifierPart(_editBuffer[fragmentStart - 1]))
        {
            fragmentStart--;
        }

        int atSignIndex = fragmentStart - 1;
        if (atSignIndex < 0 || _editBuffer[atSignIndex] != '@')
        {
            return false;
        }

        if (atSignIndex > 0 && IsDocumentVariableIdentifierPart(_editBuffer[atSignIndex - 1]))
        {
            return false;
        }

        replaceStart = fragmentStart;
        replaceEnd = caretPos;
        return true;
    }

    private static void RefreshDocumentVariableNameCache(DocWorkspace workspace, DocDocument document)
    {
        if (_cachedDocumentVariableNamesRevision == workspace.ProjectRevision &&
            string.Equals(_cachedDocumentVariableNamesDocumentId, document.Id, StringComparison.Ordinal))
        {
            return;
        }

        _cachedDocumentVariableNameCount = 0;
        _cachedDocumentVariableNamesRevision = workspace.ProjectRevision;
        _cachedDocumentVariableNamesDocumentId = document.Id;
        _cachedDocumentVariableNameSet.Clear();

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

            if (!_cachedDocumentVariableNameSet.Add(variableName))
            {
                continue;
            }

            if (_cachedDocumentVariableNameCount >= _cachedDocumentVariableNames.Length)
            {
                break;
            }

            _cachedDocumentVariableNames[_cachedDocumentVariableNameCount++] = variableName;
        }
    }

    private static void BuildVariableTypeaheadEntries(ReadOnlySpan<char> fragment)
    {
        _variableTypeaheadEntryCount = 0;
        for (int variableIndex = 0; variableIndex < _cachedDocumentVariableNameCount; variableIndex++)
        {
            string variableName = _cachedDocumentVariableNames[variableIndex];
            if (fragment.Length > 0 &&
                !variableName.AsSpan().StartsWith(fragment, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (_variableTypeaheadEntryCount >= _variableTypeaheadEntries.Length)
            {
                break;
            }

            _variableTypeaheadEntries[_variableTypeaheadEntryCount++] = variableName;
        }
    }

    private static void ApplyVariableTypeaheadEntry(DocWorkspace workspace, DocDocument document, int selectedIndex)
    {
        if (selectedIndex < 0 || selectedIndex >= _variableTypeaheadEntryCount)
        {
            CloseVariableTypeahead();
            return;
        }

        string replacement = _variableTypeaheadEntries[selectedIndex];
        if (!ReplaceEditBufferRange(_variableTypeaheadReplaceStart, _variableTypeaheadReplaceEnd, replacement.AsSpan()))
        {
            CloseVariableTypeahead();
            return;
        }

        int newCaretPos = _variableTypeaheadReplaceStart + replacement.Length;
        int focusedBlockIndex = workspace.FocusedBlockIndex;
        if (focusedBlockIndex >= 0 && focusedBlockIndex < document.Blocks.Count)
        {
            var block = document.Blocks[focusedBlockIndex];
            block.Text.PlainText = new string(_editBuffer, 0, _editBufferLength);
            string widgetId = $"doc_block_{block.Id}";
            int widget = Im.Context.GetId(widgetId);
            ImRichTextArea.SetState(widget, newCaretPos);
            Im.Context.RequestFocus(widget);
        }

        CloseVariableTypeahead();
    }

    private static bool ReplaceEditBufferRange(int start, int end, ReadOnlySpan<char> replacement)
    {
        if (start < 0 || end < start || end > _editBufferLength)
        {
            return false;
        }

        int removedCount = end - start;
        int insertedCount = replacement.Length;
        int newLength = _editBufferLength - removedCount + insertedCount;
        if (newLength > _editBuffer.Length)
        {
            return false;
        }

        if (insertedCount != removedCount)
        {
            int trailingCount = _editBufferLength - end;
            if (trailingCount > 0)
            {
                if (insertedCount > removedCount)
                {
                    for (int sourceIndex = _editBufferLength - 1; sourceIndex >= end; sourceIndex--)
                    {
                        _editBuffer[sourceIndex + insertedCount - removedCount] = _editBuffer[sourceIndex];
                    }
                }
                else
                {
                    for (int sourceIndex = end; sourceIndex < _editBufferLength; sourceIndex++)
                    {
                        _editBuffer[sourceIndex - (removedCount - insertedCount)] = _editBuffer[sourceIndex];
                    }
                }
            }
        }

        replacement.CopyTo(_editBuffer.AsSpan(start, insertedCount));
        _editBufferLength = newLength;
        return true;
    }

    private static bool IsDocumentVariableIdentifierPart(char value)
    {
        return char.IsLetterOrDigit(value) || value == '_';
    }

    // =====================================================================
    //  Layout
    // =====================================================================

    private static float ComputeLayout(DocWorkspace workspace, DocDocument document, ImRect contentRect)
    {
        // Reserve scrollbar + drag handle lane + right padding so blocks fit within visible content
        float columnW = contentRect.Width - ScrollbarWidth - DragHandleLaneWidth - ContentPaddingRight;
        float y = contentRect.Y + BlockPaddingY;
        int blockCount = Math.Min(document.Blocks.Count, 1024);

        for (int i = 0; i < blockCount; i++)
        {
            var block = document.Blocks[i];
            float fontSize = GetFontSize(block.Type);
            float lineHeight = GetBlockLineHeight(fontSize);
            float paddingTop = GetBlockTextPaddingTop(block.Type);
            float paddingBottom = GetBlockTextPaddingBottom(block.Type);
            float indentOff = block.IndentLevel * IndentWidth;
            float maxBlockWidth = Math.Max(24f, Math.Min(ContentMaxWidth, columnW - SideMargin * 2) - GutterWidth - indentOff);

            float h;
            if (block.Type == DocBlockType.Divider)
            {
                _blockW[i] = maxBlockWidth;
                h = 20f;
            }
            else if (block.Type == DocBlockType.Table)
            {
                float tableBlockWidth = GetTableBlockWidth(workspace, block, maxBlockWidth);
                _blockW[i] = tableBlockWidth;
                h = GetTableBlockHeight(workspace, block, tableBlockWidth);
            }
            else
            {
                _blockW[i] = maxBlockWidth;
                int lineCount = CountWrappedVisualLines(block.Text.PlainText, maxBlockWidth, fontSize);
                h = lineCount * lineHeight + paddingTop + paddingBottom;
                h = Math.Max(h, lineHeight + paddingTop + paddingBottom);
            }

            _blockY[i] = y;
            _blockH[i] = h;
            y += h;
        }

        return y - contentRect.Y + BlockPaddingY;
    }

    private static int CountWrappedVisualLines(string text, float width, float fontSize)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 1;
        }

        float wrapWidth = Math.Max(1f, width);
        var textSpan = text.AsSpan();
        RichTextLayout.EnsureVisualLineCapacity(ref _layoutVisualLines, textSpan.Length + 1);
        int visualLineCount = RichTextLayout.BuildVisualLines(
            Im.Context.Font,
            textSpan,
            textSpan.Length,
            fontSize,
            true,
            wrapWidth,
            0f,
            _layoutVisualLines);

        return Math.Max(1, visualLineCount);
    }

    // =====================================================================
    //  Input
    // =====================================================================

    private static void HandleInput(DocWorkspace workspace, DocDocument document,
        ImRect contentRect, float columnX, float columnW, float totalContentHeight, ImInput input)
    {
        var mousePos = Im.MousePos;
        var mousePosView = new System.Numerics.Vector2(mousePos.X, mousePos.Y - _scrollY);

        bool overlayCapturesMouse =
            Im.Context.OverlayCaptureMouse &&
            Im.Context.OverlayCaptureRect.Width > 0f &&
            Im.Context.OverlayCaptureRect.Height > 0f &&
            Im.Context.OverlayCaptureRect.Contains(input.MousePos);
        bool selectionToolbarCapturesMouse = SelectionToolbar.IsMouseOverToolbar(workspace, document, mousePosView);

        // Suppress click processing only when pointer is inside active overlay capture.
        bool suppressClick = input.MousePressed && (
            selectionToolbarCapturesMouse ||
            (SelectionToolbar.IsVisible && SelectionToolbar.ToolbarRect.Contains(mousePosView)) ||
            (SlashCommandMenu.IsOpen && SlashCommandMenu.MenuRect.Contains(mousePos)) ||
            (IsVariableTypeaheadOpen() && _variableTypeaheadMenuRect.Contains(mousePos)) ||
            overlayCapturesMouse ||
            ImModal.IsAnyOpen);

        // Hover detection (extends left into drag handle lane)
        _hoveredBlockIndex = -1;
        float hoverLeftEdge = columnX - DragHandleLaneWidth;
        int blockCount = Math.Min(document.Blocks.Count, 1024);
        for (int i = 0; i < blockCount; i++)
        {
            float by = _blockY[i];
            float bh = _blockH[i];
            var block = document.Blocks[i];
            float indentOff = block.IndentLevel * IndentWidth;
            float blockLeft = columnX + GutterWidth + indentOff;
            float maxBlockWidth = Math.Max(24f, columnW - GutterWidth - indentOff);
            float blockWidth = _blockW[i] > 0f ? _blockW[i] : maxBlockWidth;
            float blockRight = blockLeft + blockWidth;
            if (mousePos.Y >= by && mousePos.Y < by + bh &&
                mousePos.X >= hoverLeftEdge && mousePos.X < blockRight)
            {
                _hoveredBlockIndex = i;
                break;
            }
        }

        if (!overlayCapturesMouse &&
            input.MouseRightPressed &&
            _hoveredBlockIndex >= 0 &&
            _hoveredBlockIndex < document.Blocks.Count)
        {
            var hoveredBlock = document.Blocks[_hoveredBlockIndex];
            if (hoveredBlock.Type == DocBlockType.Formula || hoveredBlock.Type == DocBlockType.Variable)
            {
                _contextMenuDocumentId = document.Id;
                _contextMenuBlockId = hoveredBlock.Id;
                ImContextMenu.Open(BlockContextMenuId);
                Im.Context.ConsumeMouseRightPress();
            }
        }

        bool startedMultiSelectDrag = false;
        if (!suppressClick &&
            input.MousePressed &&
            _hoveredBlockIndex >= 0 &&
            IsMouseInBlockSelectionGutter(document, columnX, _hoveredBlockIndex, mousePos.X))
        {
            _isMultiSelectDragging = true;
            _multiSelectAnchor = _hoveredBlockIndex;
            _multiSelectEnd = _hoveredBlockIndex;
            ClearMultiBlockTextSelection();
            startedMultiSelectDrag = true;
        }

        if (_isMultiSelectDragging)
        {
            if (input.MouseDown)
            {
                int dragSelectionIndex = GetSelectionBlockIndexAtY(document, mousePos.Y);
                if (dragSelectionIndex >= 0)
                {
                    _multiSelectEnd = dragSelectionIndex;
                }
            }
            else
            {
                _isMultiSelectDragging = false;
            }
        }

        if (!_isMultiSelectDragging &&
            !suppressClick &&
            !overlayCapturesMouse &&
            input.MouseDown &&
            workspace.FocusedBlockIndex >= 0 &&
            workspace.FocusedBlockIndex < document.Blocks.Count &&
            IsFocusedBlockTextEditable(workspace, document) &&
            mousePos.X >= columnX &&
            mousePos.X < columnX + columnW)
        {
            int targetBlockIndex = _hoveredBlockIndex >= 0
                ? _hoveredBlockIndex
                : GetSelectionBlockIndexAtY(document, mousePos.Y);
            if (targetBlockIndex >= 0 && targetBlockIndex != workspace.FocusedBlockIndex)
            {
                var focusedBlock = document.Blocks[workspace.FocusedBlockIndex];
                bool hasTextSelection = GetSelection(focusedBlock, out int focusedSelectionStart, out int focusedSelectionEnd);
                bool continueExistingRange = _hasMultiBlockTextSelection &&
                                             _multiTextFocusBlock == workspace.FocusedBlockIndex &&
                                             _multiTextFocusSelectionStart >= 0 &&
                                             _multiTextFocusSelectionEnd >= _multiTextFocusSelectionStart;
                if (hasTextSelection || continueExistingRange)
                {
                    int anchorSelectionStart = hasTextSelection
                        ? focusedSelectionStart
                        : _multiTextFocusSelectionStart;
                    int anchorSelectionEnd = hasTextSelection
                        ? focusedSelectionEnd
                        : _multiTextFocusSelectionEnd;
                    if (TrySetMultiBlockTextSelectionFromFocusedSelection(
                            document,
                            workspace.FocusedBlockIndex,
                            anchorSelectionStart,
                            anchorSelectionEnd,
                            targetBlockIndex))
                    {
                        _multiSelectAnchor = -1;
                        _multiSelectEnd = -1;
                    }
                    else
                    {
                        ClearMultiBlockTextSelection();
                    }
                }
            }
        }

        if (input.MousePressed &&
            workspace.FocusedBlockIndex >= 0 &&
            workspace.FocusedBlockIndex < document.Blocks.Count)
        {
            var focusedBlock = document.Blocks[workspace.FocusedBlockIndex];
            if (focusedBlock.Type == DocBlockType.Table)
            {
                float focusedIndent = focusedBlock.IndentLevel * IndentWidth;
                float focusedX = columnX + GutterWidth + focusedIndent;
                float focusedY = _blockY[workspace.FocusedBlockIndex];
                float maxFocusedWidth = Math.Max(24f, columnW - GutterWidth - focusedIndent);
                float focusedWidth = _blockW[workspace.FocusedBlockIndex] > 0f ? _blockW[workspace.FocusedBlockIndex] : maxFocusedWidth;
                float focusedHeight = _blockH[workspace.FocusedBlockIndex];
                var focusedRect = new ImRect(focusedX, focusedY, focusedWidth, focusedHeight);
                if (!focusedRect.Contains(mousePos))
                {
                    SpreadsheetRenderer.BlurEmbeddedSelection(focusedBlock.Id);
                }
            }
        }

        bool hasWheelDelta = input.ScrollDelta != 0f || input.ScrollDeltaX != 0f;
        if (!suppressClick && hasWheelDelta && contentRect.Contains(mousePosView))
        {
            float maxParentScroll = Math.Max(0f, totalContentHeight - contentRect.Height);
            bool parentCanConsumeWheel = false;
            float nextParentScrollY = _scrollY;
            float parentScrollbarDistance = float.PositiveInfinity;
            if (input.ScrollDelta != 0f && maxParentScroll > 0f)
            {
                nextParentScrollY = Math.Clamp(_scrollY - input.ScrollDelta * 30f, 0f, maxParentScroll);
                parentCanConsumeWheel = MathF.Abs(nextParentScrollY - _scrollY) > 0.01f;
                if (parentCanConsumeWheel)
                {
                    float contentWidth = contentRect.Width - (maxParentScroll > 0f ? ScrollbarWidth : 0f);
                    var parentScrollbarRect = new ImRect(
                        contentRect.X + contentWidth,
                        contentRect.Y,
                        ScrollbarWidth,
                        contentRect.Height);
                    parentScrollbarDistance = DistanceFromPointToRect(input.MousePos, parentScrollbarRect);
                }
            }

            bool embeddedCanConsumeWheel = false;
            float embeddedScrollbarDistance = float.PositiveInfinity;
            string hoveredEmbeddedStateKey = "";
            if (_hoveredBlockIndex >= 0 && _hoveredBlockIndex < document.Blocks.Count)
            {
                var hoveredBlock = document.Blocks[_hoveredBlockIndex];
                if (hoveredBlock.Type == DocBlockType.Table)
                {
                    float blockY = _blockY[_hoveredBlockIndex];
                    float blockHeight = _blockH[_hoveredBlockIndex];
                    float blockLeft = columnX + GutterWidth + hoveredBlock.IndentLevel * IndentWidth;
                    float maxBlockWidth = Math.Max(24f, columnW - GutterWidth - hoveredBlock.IndentLevel * IndentWidth);
                    float blockWidth = _blockW[_hoveredBlockIndex] > 0f ? _blockW[_hoveredBlockIndex] : maxBlockWidth;
                    bool isMouseOverEmbeddedTable =
                        mousePos.X >= blockLeft &&
                        mousePos.X < blockLeft + blockWidth &&
                        mousePos.Y >= blockY &&
                        mousePos.Y < blockY + blockHeight;
                    if (isMouseOverEmbeddedTable)
                    {
                        hoveredEmbeddedStateKey = hoveredBlock.Id;
                        embeddedCanConsumeWheel = SpreadsheetRenderer.TryGetEmbeddedWheelConsumeDistance(
                            hoveredBlock.Id,
                            input.MousePos,
                            input.ScrollDelta,
                            input.ScrollDeltaX,
                            out embeddedScrollbarDistance);
                    }
                }
            }

            bool routeWheelToEmbedded = false;
            if (embeddedCanConsumeWheel && parentCanConsumeWheel)
            {
                routeWheelToEmbedded = embeddedScrollbarDistance < parentScrollbarDistance;
            }
            else if (embeddedCanConsumeWheel)
            {
                routeWheelToEmbedded = true;
            }

            if (!routeWheelToEmbedded && parentCanConsumeWheel)
            {
                if (embeddedCanConsumeWheel && !string.IsNullOrEmpty(hoveredEmbeddedStateKey))
                {
                    SpreadsheetRenderer.SuppressEmbeddedWheelForStateThisFrame(hoveredEmbeddedStateKey);
                }

                _scrollY = nextParentScrollY;
            }
        }

        // Drag reorder — start drag on drag handle lane mousedown
        if (!suppressClick && input.MousePressed && _hoveredBlockIndex >= 0 && !_isDragging)
        {
            var dragBlock = document.Blocks[_hoveredBlockIndex];
            if (mousePos.X < columnX && mousePos.X >= hoverLeftEdge && dragBlock.Type != DocBlockType.Divider)
            {
                _dragSourceIndex = _hoveredBlockIndex;
                _dragStartY = mousePos.Y;
                _isDragging = false; // Wait for threshold
            }
        }

        // Drag reorder — track drag movement
        if (_dragSourceIndex >= 0 && input.MouseDown)
        {
            float dragDist = MathF.Abs(mousePos.Y - _dragStartY);
            if (!_isDragging && dragDist > 5f)
                _isDragging = true;

            if (_isDragging)
            {
                // Compute insertion index from mouse Y
                _dragInsertIndex = ComputeDragInsertIndex(document, contentRect, mousePos.Y);
            }
        }

        // Drag reorder — drop
        if (_dragSourceIndex >= 0 && input.MouseReleased)
        {
            if (_isDragging && _dragInsertIndex >= 0 && _dragInsertIndex != _dragSourceIndex
                && _dragInsertIndex != _dragSourceIndex + 1)
            {
                CommitFocusedBlock(workspace, document);
                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.MoveBlock,
                    DocumentId = document.Id,
                    BlockIndex = _dragSourceIndex,
                    TargetBlockIndex = _dragInsertIndex,
                });

                // Update focus if the focused block moved
                if (workspace.FocusedBlockIndex == _dragSourceIndex)
                {
                    int newIndex = _dragInsertIndex > _dragSourceIndex ? _dragInsertIndex - 1 : _dragInsertIndex;
                    workspace.FocusedBlockIndex = newIndex;
                }
            }
            _dragSourceIndex = -1;
            _dragInsertIndex = -1;
            _isDragging = false;
        }

        // Click to focus a block (suppress only when clicking in drag handle lane)
        bool clickedDragLane = _hoveredBlockIndex >= 0 && mousePos.X < columnX;
        if (!suppressClick &&
            !_isDragging &&
            !clickedDragLane &&
            !startedMultiSelectDrag &&
            input.MousePressed &&
            _hoveredBlockIndex >= 0)
        {
            if (input.KeyShift)
            {
                // Shift-click: extend multi-block selection
                if (_multiSelectAnchor < 0)
                {
                    _multiSelectAnchor = workspace.FocusedBlockIndex >= 0
                        ? workspace.FocusedBlockIndex
                        : _hoveredBlockIndex;
                }
                _multiSelectEnd = _hoveredBlockIndex;
                ClearMultiBlockTextSelection();
            }
            else
            {
                // Clear multi-block selection on normal click
                _multiSelectAnchor = -1;
                _multiSelectEnd = -1;
                ClearMultiBlockTextSelection();

                if (_hoveredBlockIndex != workspace.FocusedBlockIndex)
                {
                    CommitFocusedBlock(workspace, document);
                    _pendingFocusBlock = _hoveredBlockIndex;
                    if (IsTextEditableBlock(document.Blocks[_hoveredBlockIndex].Type))
                    {
                        _pendingFocusCaretPos = document.Blocks[_hoveredBlockIndex].Text.PlainText.Length;
                    }
                    else
                    {
                        _pendingFocusCaretPos = 0;
                    }
                }
            }
        }

        // Handle deferred focus
        if (_pendingFocusBlock >= 0)
        {
            workspace.FocusedBlockIndex = _pendingFocusBlock;
            var focusBlock = document.Blocks[_pendingFocusBlock];

            // Snapshot the text for undo when focus changes
            workspace.FocusedBlockTextSnapshot = focusBlock.Text.PlainText;
            if (IsTextEditableBlock(focusBlock.Type))
            {
                // Copy block text to edit buffer
                CopyTextToBuffer(focusBlock.Text.PlainText);

                // Set caret position on the ImRichTextArea widget
                string widgetId = $"doc_block_{focusBlock.Id}";
                int wid = Im.Context.GetId(widgetId);
                ImRichTextArea.SetState(wid, _pendingFocusCaretPos);
                Im.Context.RequestFocus(wid);
            }
            else
            {
                _editBufferLength = 0;
                Im.Context.ClearFocus();
            }

            _pendingFocusBlock = -1;
            _pendingFocusCaretPos = -1;
        }

        // Click on empty area below all blocks — create or focus last block
        if (!suppressClick && input.MousePressed && _hoveredBlockIndex < 0 &&
            mousePos.X >= columnX && mousePos.X < columnX + columnW &&
            mousePosView.Y >= contentRect.Y)
        {
            ClearMultiBlockTextSelection();
            CommitFocusedBlock(workspace, document);

            if (document.Blocks.Count > 0)
            {
                var lastBlock = document.Blocks[^1];
                if (IsTextEditableBlock(lastBlock.Type) && lastBlock.Text.PlainText.Length == 0)
                {
                    // Focus existing empty last block
                    FocusBlock(workspace, document, document.Blocks.Count - 1, 0);
                }
                else
                {
                    // Add a new empty block at end
                    AddNewBlockAfter(workspace, document, document.Blocks.Count - 1);
                }
            }
        }

        // Escape — defocus and clear ImGui widget focus
        if (input.KeyEscape &&
            !Im.Context.WantCaptureKeyboard &&
            workspace.FocusedBlockIndex >= 0 &&
            !SlashCommandMenu.IsOpen &&
            !IsVariableTypeaheadOpen() &&
            !_variableTypeaheadConsumedKeyThisFrame)
        {
            CommitFocusedBlock(workspace, document);
            Im.Context.ClearFocus();
            Im.Context.ClearActive();
            workspace.FocusedBlockIndex = -1;
            ClearMultiBlockTextSelection();
        }
    }

    // =====================================================================
    //  Block rendering
    // =====================================================================

    private static void DrawBlockGutter(DocBlock block, int blockIndex, DocDocument document,
        float columnX, float by, float bh)
    {
        var style = Im.Style;
        float fontSize = GetFontSize(block.Type);
        float textPaddingTop = GetBlockTextPaddingTop(block.Type);
        float textPaddingBottom = GetBlockTextPaddingBottom(block.Type);
        float gutterX = columnX;
        float centerY = by + bh * 0.5f;

        switch (block.Type)
        {
            case DocBlockType.BulletList:
            {
                float dotSize = 5f;
                float dotX = gutterX + GutterWidth * 0.5f - dotSize * 0.5f;
                float dotY = by + textPaddingTop + fontSize * 0.7f * 0.5f - dotSize * 0.5f;
                Im.DrawRoundedRect(dotX, dotY, dotSize, dotSize, dotSize * 0.5f, style.TextSecondary);
                break;
            }
            case DocBlockType.NumberedList:
            {
                // Find the 1-based index within consecutive numbered list blocks
                int num = 1;
                for (int i = blockIndex - 1; i >= 0; i--)
                {
                    if (document.Blocks[i].Type == DocBlockType.NumberedList)
                        num++;
                    else
                        break;
                }
                string numStr = num.ToString() + ".";
                float numW = Im.MeasureTextWidth(numStr.AsSpan(), style.FontSize);
                float numX = gutterX + GutterWidth - numW - 4f;
                float numY = by + textPaddingTop;
                Im.Text(numStr.AsSpan(), numX, numY, style.FontSize, style.TextSecondary);
                break;
            }
            case DocBlockType.CheckboxList:
            {
                float boxSize = 14f;
                float boxX = gutterX + GutterWidth * 0.5f - boxSize * 0.5f;
                float boxY = by + textPaddingTop + fontSize * 0.35f - boxSize * 0.5f;

                Im.DrawRoundedRectStroke(boxX, boxY, boxSize, boxSize, 2f, style.Border, 1.5f);
                if (block.Checked)
                {
                    Im.DrawRoundedRect(boxX + 2, boxY + 2, boxSize - 4, boxSize - 4, 1f, style.Primary);
                    Im.DrawLine(boxX + 3, boxY + boxSize * 0.5f, boxX + boxSize * 0.4f, boxY + boxSize - 4, 1.5f, 0xFFFFFFFF);
                    Im.DrawLine(boxX + boxSize * 0.4f, boxY + boxSize - 4, boxX + boxSize - 3, boxY + 3, 1.5f, 0xFFFFFFFF);
                }

                // Handle checkbox click
                var input = Im.Context.Input;
                if (input.MousePressed)
                {
                    var mousePos = Im.MousePos;
                    if (mousePos.X >= boxX - 2 && mousePos.X <= boxX + boxSize + 2 &&
                        mousePos.Y >= boxY - 2 && mousePos.Y <= boxY + boxSize + 2)
                    {
                        // Toggle will be handled via command — but we need workspace access
                        // For now, flag it for the Draw method to handle
                    }
                }
                break;
            }
            case DocBlockType.Quote:
            {
                float barW = 3f;
                float barX = gutterX + GutterWidth - barW - 4f;
                float barHeight = Math.Max(1f, bh - textPaddingTop - textPaddingBottom);
                Im.DrawRoundedRect(barX, by + textPaddingTop, barW, barHeight, 1.5f, style.Primary);
                break;
            }
        }
    }

    private static void DrawBlockBackground(DocBlock block, float bx, float by, float bw, float bh)
    {
        var style = Im.Style;

        if (block.Type == DocBlockType.CodeBlock)
        {
            Im.DrawRoundedRect(bx - 4f, by + 2f, bw + 8f, bh - 4f, 4f, style.Surface);
        }
        else if (block.Type == DocBlockType.Formula)
        {
            uint formulaTint = BlendColor(style.Primary, 0.16f, style.Surface);
            Im.DrawRoundedRect(bx - 3f, by + 2f, bw + 6f, bh - 4f, 5f, formulaTint);
            Im.DrawRoundedRectStroke(bx - 3f, by + 2f, bw + 6f, bh - 4f, 5f, BlendColor(style.Primary, 0.58f, style.Border), 1f);
        }
        else if (block.Type == DocBlockType.Variable)
        {
            uint variableTint = BlendColor(style.Active, 0.18f, style.Surface);
            Im.DrawRoundedRect(bx - 3f, by + 2f, bw + 6f, bh - 4f, 5f, variableTint);
            Im.DrawRoundedRectStroke(bx - 3f, by + 2f, bw + 6f, bh - 4f, 5f, BlendColor(style.Active, 0.58f, style.Border), 1f);
        }
        else if (block.Type == DocBlockType.Table)
        {
            Im.DrawRoundedRect(bx - 2f, by + 2f, bw + 4f, bh - 4f, 6f, style.Surface);
            Im.DrawRoundedRectStroke(bx - 2f, by + 2f, bw + 4f, bh - 4f, 6f, style.Border, 1f);
        }
        else if (block.Type == DocBlockType.Divider)
        {
            float lineY = by + bh * 0.5f;
            Im.DrawLine(bx, lineY, bx + bw, lineY, 1f, style.Border);
        }
    }

    private static void DrawActiveBlock(DocWorkspace workspace, DocDocument document,
        DocBlock block, int blockIndex, float bx, float by, float bw, float bh)
    {
        if (!IsTextEditableBlock(block.Type)) return;

        var style = Im.Style;
        float fontSize = GetFontSize(block.Type);
        float textPaddingTop = GetBlockTextPaddingTop(block.Type);
        float textPaddingBottom = GetBlockTextPaddingBottom(block.Type);
        float textHeight = Math.Max(1f, bh - textPaddingTop - textPaddingBottom);
        bool singleLine = block.Type != DocBlockType.CodeBlock;

        // Placeholder for empty active Paragraph
        if (_editBufferLength == 0 && block.Type == DocBlockType.Paragraph)
        {
            Im.Text("Type '/' for commands".AsSpan(), bx, by + textPaddingTop, fontSize,
                ImStyle.WithAlpha(style.TextSecondary, 150));
        }
        else if (_editBufferLength == 0 && block.Type == DocBlockType.Formula)
        {
            Im.Text("Type a formula expression".AsSpan(), bx, by + textPaddingTop, fontSize,
                ImStyle.WithAlpha(style.TextSecondary, 160));
        }
        else if (_editBufferLength == 0 && block.Type == DocBlockType.Variable)
        {
            Im.Text("Type @name or @name = expression".AsSpan(), bx, by + textPaddingTop, fontSize,
                ImStyle.WithAlpha(style.TextSecondary, 160));
        }

        string widgetId = $"doc_block_{block.Id}";
        _focusedBlockWidgetId = Im.Context.GetId(widgetId);

        bool changed = ImRichTextArea.DrawAt(
            widgetId,
            _editBuffer,
            ref _editBufferLength,
            4096,
            block.Text.Spans,
            bx, by + textPaddingTop,
            bw, textHeight,
            singleLine,
            fontSize,
            GetTextColor(block.Type, style));

        if (changed)
        {
            block.Text.PlainText = new string(_editBuffer, 0, _editBufferLength);
        }
    }

    private static void DrawStaticBlock(
        DocWorkspace workspace,
        DocDocument document,
        DocBlock block,
        float bx,
        float by,
        float bw,
        float bh)
    {
        if (!IsTextEditableBlock(block.Type)) return;

        var style = Im.Style;
        float fontSize = GetFontSize(block.Type);
        float textPaddingTop = GetBlockTextPaddingTop(block.Type);
        uint color = GetTextColor(block.Type, style);

        if (block.Type == DocBlockType.Formula)
        {
            string displayText = ResolveFormulaDisplayText(workspace, document, block, out bool isValid);
            if (displayText.Length == 0)
            {
                return;
            }

            uint formulaColor = isValid ? color : BlendColor(style.Primary, 0.55f, style.TextPrimary);
            RichTextRenderer.DrawPlain(displayText, bx, by + textPaddingTop, bw, fontSize, formulaColor);
            return;
        }

        if (block.Type == DocBlockType.Variable)
        {
            DrawVariableStaticBlock(workspace, document, block, bx, by, bw, fontSize, textPaddingTop, style);
            return;
        }

        if (block.Text.PlainText.Length == 0)
        {
            return;
        }

        if (block.Text.Spans.Count > 0)
            RichTextRenderer.Draw(block.Text, bx, by + textPaddingTop, bw, fontSize, color);
        else
            RichTextRenderer.DrawPlain(block.Text.PlainText, bx, by + textPaddingTop, bw, fontSize, color);
    }

    private static string ResolveFormulaDisplayText(
        DocWorkspace workspace,
        DocDocument document,
        DocBlock block,
        out bool isValid)
    {
        if (_formulaDisplayCacheByBlockId.Count > 4096)
        {
            _formulaDisplayCacheByBlockId.Clear();
        }

        if (_formulaDisplayCacheByBlockId.TryGetValue(block.Id, out var cacheEntry) &&
            cacheEntry.ProjectRevision == workspace.ProjectRevision &&
            string.Equals(cacheEntry.FormulaText, block.Text.PlainText, StringComparison.Ordinal))
        {
            isValid = cacheEntry.IsValid;
            return cacheEntry.ResultText;
        }

        bool valid = _documentFormulaPreviewEngine.TryEvaluateDocumentExpression(
            workspace.Project,
            document,
            block.Text.PlainText,
            out string evaluatedText);
        string resultText = valid ? evaluatedText : "#ERR";
        _formulaDisplayCacheByBlockId[block.Id] = new FormulaDisplayCacheEntry(
            workspace.ProjectRevision,
            block.Text.PlainText,
            resultText,
            valid);

        isValid = valid;
        return resultText;
    }

    private static void DrawVariableStaticBlock(
        DocWorkspace workspace,
        DocDocument document,
        DocBlock block,
        float bx,
        float by,
        float bw,
        float fontSize,
        float textPaddingTop,
        ImStyle style)
    {
        if (!DocumentFormulaSyntax.TryParseVariableDeclaration(
                block.Text.PlainText,
                out string variableName,
                out bool hasExpression,
                out string expression))
        {
            if (!string.IsNullOrWhiteSpace(block.Text.PlainText))
            {
                RichTextRenderer.DrawPlain(block.Text.PlainText, bx, by + textPaddingTop, bw, fontSize, style.TextPrimary);
            }

            return;
        }

        string pillText = "@" + variableName;
        float pillPaddingX = 8f;
        float pillHeight = Math.Max(fontSize + 4f, 20f);
        float pillY = by + textPaddingTop;
        float iconWidth = Im.MeasureTextWidth(_variableBlockIcon.AsSpan(), fontSize - 1f);
        float textWidth = Im.MeasureTextWidth(pillText.AsSpan(), fontSize);
        float pillWidth = pillPaddingX * 2f + iconWidth + 4f + textWidth;
        uint pillBackground = BlendColor(style.Active, 0.46f, style.Surface);
        Im.DrawRoundedRect(bx, pillY, pillWidth, pillHeight, 10f, pillBackground);
        Im.DrawRoundedRectStroke(bx, pillY, pillWidth, pillHeight, 10f, BlendColor(style.Active, 0.58f, style.Border), 1f);
        float iconY = pillY + (pillHeight - (fontSize - 1f)) * 0.5f;
        float textY = pillY + (pillHeight - fontSize) * 0.5f;
        Im.Text(_variableBlockIcon.AsSpan(), bx + pillPaddingX, iconY, fontSize - 1f, style.TextSecondary);
        Im.Text(pillText.AsSpan(), bx + pillPaddingX + iconWidth + 4f, textY, fontSize, style.TextPrimary);

        if (hasExpression && !string.IsNullOrWhiteSpace(expression))
        {
            string valueText = ResolveVariableValueDisplayText(
                workspace,
                document,
                block,
                expression,
                out bool valueIsValid);
            float valueX = bx + pillWidth + 8f;
            uint valueColor = valueIsValid ? style.TextPrimary : BlendColor(style.Primary, 0.55f, style.TextPrimary);
            RichTextRenderer.DrawPlain(valueText, valueX, by + textPaddingTop, Math.Max(1f, bw - (valueX - bx)), fontSize, valueColor);
            return;
        }

        string referenceValueText = ResolveVariableReferenceValueDisplayText(
            workspace,
            document,
            block,
            variableName,
            out bool referenceValueIsValid);
        if (referenceValueText.Length == 0 && referenceValueIsValid)
        {
            return;
        }

        float referenceValueX = bx + pillWidth + 8f;
        uint referenceValueColor = referenceValueIsValid ? style.TextPrimary : BlendColor(style.Primary, 0.55f, style.TextPrimary);
        RichTextRenderer.DrawPlain(
            referenceValueText,
            referenceValueX,
            by + textPaddingTop,
            Math.Max(1f, bw - (referenceValueX - bx)),
            fontSize,
            referenceValueColor);
    }

    private static string ResolveVariableValueDisplayText(
        DocWorkspace workspace,
        DocDocument document,
        DocBlock block,
        string expression,
        out bool isValid)
    {
        if (_variableValueDisplayCacheByBlockId.Count > 4096)
        {
            _variableValueDisplayCacheByBlockId.Clear();
        }

        if (_variableValueDisplayCacheByBlockId.TryGetValue(block.Id, out var cacheEntry) &&
            cacheEntry.ProjectRevision == workspace.ProjectRevision &&
            string.Equals(cacheEntry.ExpressionText, expression, StringComparison.Ordinal))
        {
            isValid = cacheEntry.IsValid;
            return cacheEntry.ResultText;
        }

        bool valid = _documentFormulaPreviewEngine.TryEvaluateDocumentExpression(
            workspace.Project,
            document,
            expression,
            out string evaluatedText);
        string resultText = valid ? evaluatedText : "#ERR";
        _variableValueDisplayCacheByBlockId[block.Id] = new VariableValueDisplayCacheEntry(
            workspace.ProjectRevision,
            expression,
            resultText,
            valid);

        isValid = valid;
        return resultText;
    }

    private static string ResolveVariableReferenceValueDisplayText(
        DocWorkspace workspace,
        DocDocument document,
        DocBlock block,
        string variableName,
        out bool isValid)
    {
        if (_variableReferenceValueDisplayCacheByBlockId.Count > 4096)
        {
            _variableReferenceValueDisplayCacheByBlockId.Clear();
        }

        if (_variableReferenceValueDisplayCacheByBlockId.TryGetValue(block.Id, out var cacheEntry) &&
            cacheEntry.ProjectRevision == workspace.ProjectRevision &&
            string.Equals(cacheEntry.VariableName, variableName, StringComparison.Ordinal))
        {
            isValid = cacheEntry.IsValid;
            return cacheEntry.ResultText;
        }

        string referenceExpression = "thisDoc." + variableName;
        bool valid = _documentFormulaPreviewEngine.TryEvaluateDocumentExpression(
            workspace.Project,
            document,
            referenceExpression,
            out string evaluatedText);
        string resultText = valid ? evaluatedText : "#ERR";
        _variableReferenceValueDisplayCacheByBlockId[block.Id] = new VariableReferenceValueDisplayCacheEntry(
            workspace.ProjectRevision,
            variableName,
            resultText,
            valid);

        isValid = valid;
        return resultText;
    }

    private static bool TrySetMultiBlockTextSelectionFromFocusedSelection(
        DocDocument document,
        int focusedBlockIndex,
        int focusedSelectionStart,
        int focusedSelectionEnd,
        int targetBlockIndex)
    {
        if (focusedBlockIndex < 0 || focusedBlockIndex >= document.Blocks.Count)
        {
            return false;
        }

        if (targetBlockIndex < 0 || targetBlockIndex >= document.Blocks.Count || targetBlockIndex == focusedBlockIndex)
        {
            return false;
        }

        int rangeStart = Math.Min(focusedBlockIndex, targetBlockIndex);
        int rangeEnd = Math.Max(focusedBlockIndex, targetBlockIndex);
        if (!AreBlocksTextSelectableInRange(document, rangeStart, rangeEnd))
        {
            return false;
        }

        int clampedFocusedSelectionStart = Math.Clamp(
            focusedSelectionStart,
            0,
            document.Blocks[focusedBlockIndex].Text.PlainText.Length);
        int clampedFocusedSelectionEnd = Math.Clamp(
            focusedSelectionEnd,
            clampedFocusedSelectionStart,
            document.Blocks[focusedBlockIndex].Text.PlainText.Length);
        int targetTextLength = document.Blocks[targetBlockIndex].Text.PlainText.Length;

        if (targetBlockIndex > focusedBlockIndex)
        {
            _multiTextStartBlock = focusedBlockIndex;
            _multiTextStartOffset = clampedFocusedSelectionStart;
            _multiTextEndBlock = targetBlockIndex;
            _multiTextEndOffset = targetTextLength;
        }
        else
        {
            _multiTextStartBlock = targetBlockIndex;
            _multiTextStartOffset = 0;
            _multiTextEndBlock = focusedBlockIndex;
            _multiTextEndOffset = clampedFocusedSelectionEnd;
        }

        _hasMultiBlockTextSelection = true;
        _multiTextFocusBlock = focusedBlockIndex;
        _multiTextFocusSelectionStart = clampedFocusedSelectionStart;
        _multiTextFocusSelectionEnd = clampedFocusedSelectionEnd;
        return true;
    }

    private static bool AreBlocksTextSelectableInRange(DocDocument document, int startIndex, int endIndex)
    {
        int clampedStartIndex = Math.Clamp(startIndex, 0, document.Blocks.Count - 1);
        int clampedEndIndex = Math.Clamp(endIndex, clampedStartIndex, document.Blocks.Count - 1);
        for (int blockIndex = clampedStartIndex; blockIndex <= clampedEndIndex; blockIndex++)
        {
            if (!IsTextEditableBlock(document.Blocks[blockIndex].Type))
            {
                return false;
            }
        }

        return true;
    }

    private static void ClearMultiBlockTextSelection()
    {
        _hasMultiBlockTextSelection = false;
        _multiTextStartBlock = -1;
        _multiTextStartOffset = -1;
        _multiTextEndBlock = -1;
        _multiTextEndOffset = -1;
        _multiTextFocusBlock = -1;
        _multiTextFocusSelectionStart = -1;
        _multiTextFocusSelectionEnd = -1;
    }

    private static bool TryGetMultiBlockTextSelectionForBlock(
        DocBlock block,
        int blockIndex,
        out int selectionStart,
        out int selectionEnd)
    {
        selectionStart = 0;
        selectionEnd = 0;
        if (!_hasMultiBlockTextSelection ||
            _multiTextStartBlock < 0 ||
            _multiTextEndBlock < _multiTextStartBlock ||
            blockIndex < _multiTextStartBlock ||
            blockIndex > _multiTextEndBlock ||
            !IsTextEditableBlock(block.Type))
        {
            return false;
        }

        int textLength = block.Text.PlainText.Length;
        if (blockIndex == _multiTextStartBlock && blockIndex == _multiTextEndBlock)
        {
            selectionStart = Math.Clamp(_multiTextStartOffset, 0, textLength);
            selectionEnd = Math.Clamp(_multiTextEndOffset, selectionStart, textLength);
            return selectionEnd > selectionStart;
        }

        if (blockIndex == _multiTextStartBlock)
        {
            selectionStart = Math.Clamp(_multiTextStartOffset, 0, textLength);
            selectionEnd = textLength;
            return selectionEnd > selectionStart;
        }

        if (blockIndex == _multiTextEndBlock)
        {
            selectionStart = 0;
            selectionEnd = Math.Clamp(_multiTextEndOffset, 0, textLength);
            return selectionEnd > selectionStart;
        }

        selectionStart = 0;
        selectionEnd = textLength;
        return selectionEnd > selectionStart;
    }

    private static void DrawMultiBlockTextSelectionOverlay(DocBlock block, int blockIndex, float x, float y, float width)
    {
        if (!TryGetMultiBlockTextSelectionForBlock(block, blockIndex, out int selectionStart, out int selectionEnd))
        {
            return;
        }

        string text = block.Text.PlainText;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        float fontSize = GetFontSize(block.Type);
        float lineHeight = GetBlockLineHeight(fontSize);
        float textY = y + GetBlockTextPaddingTop(block.Type);
        uint selectionColor = ImStyle.WithAlpha(Im.Style.Primary, 100);
        ReadOnlySpan<char> textSpan = text.AsSpan();

        RichTextLayout.EnsureVisualLineCapacity(ref _textSelectionVisualLines, textSpan.Length + 1);
        int visualLineCount = RichTextLayout.BuildVisualLines(
            Im.Context.Font,
            textSpan,
            textSpan.Length,
            fontSize,
            true,
            width,
            0f,
            _textSelectionVisualLines);

        for (int lineIndex = 0; lineIndex < visualLineCount; lineIndex++)
        {
            var visualLine = _textSelectionVisualLines[lineIndex];
            int lineStart = visualLine.Start;
            int lineEnd = visualLine.Start + visualLine.Length;
            int overlapStart = Math.Max(selectionStart, lineStart);
            int overlapEnd = Math.Min(selectionEnd, lineEnd);
            if (overlapEnd <= overlapStart)
            {
                continue;
            }

            float prefixWidth = overlapStart > lineStart
                ? Im.MeasureTextWidth(textSpan.Slice(lineStart, overlapStart - lineStart), fontSize)
                : 0f;
            float selectionWidth = Im.MeasureTextWidth(textSpan.Slice(overlapStart, overlapEnd - overlapStart), fontSize);
            if (selectionWidth <= 0f)
            {
                continue;
            }

            float lineY = textY + lineIndex * lineHeight;
            Im.DrawRect(x + prefixWidth, lineY + 1f, selectionWidth, Math.Max(1f, fontSize), selectionColor);
        }
    }

    private static string BuildMultiBlockSelectedText(DocDocument document)
    {
        if (!_hasMultiBlockTextSelection || _multiTextStartBlock < 0 || _multiTextEndBlock < _multiTextStartBlock)
        {
            return "";
        }

        var builder = new System.Text.StringBuilder();
        bool wroteAnyText = false;
        int endBlock = Math.Min(_multiTextEndBlock, document.Blocks.Count - 1);
        for (int blockIndex = _multiTextStartBlock; blockIndex <= endBlock; blockIndex++)
        {
            var block = document.Blocks[blockIndex];
            if (!TryGetMultiBlockTextSelectionForBlock(block, blockIndex, out int selectionStart, out int selectionEnd))
            {
                continue;
            }

            if (wroteAnyText)
            {
                builder.Append('\n');
            }

            builder.Append(block.Text.PlainText.AsSpan(selectionStart, selectionEnd - selectionStart));
            wroteAnyText = true;
        }

        return builder.ToString();
    }

    private const float EmbeddedTitleRowHeight = 34f;
    private const float EmbeddedTitleFontBoost = 7f;
    private const float EmbeddedTitleOptionsWidth = 34f;
    private const float EmbeddedTitleBottomSpacing = 8f;
    private const float EmbeddedVariantBadgeGap = 6f;
    private const float EmbeddedVariantBadgeHorizontalPadding = 6f;
    private static readonly string _embeddedOptionsIcon = ((char)FontAwesome.Sharp.IconChar.EllipsisV).ToString();

    private static void DrawTableBlock(
        DocWorkspace workspace,
        DocDocument document,
        DocBlock block,
        float blockX,
        float blockY,
        float blockWidth,
        float blockHeight,
        bool isFocused)
    {
        if (string.IsNullOrWhiteSpace(block.TableId))
        {
            Im.Text("Table block is not linked yet.".AsSpan(), blockX + 8f, blockY + 10f, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }

        var table = workspace.Project.Tables.Find(candidate => string.Equals(candidate.Id, block.TableId, StringComparison.Ordinal));
        if (table == null)
        {
            Im.Text("Linked table no longer exists.".AsSpan(), blockX + 8f, blockY + 10f, Im.Style.FontSize, Im.Style.Secondary);
            return;
        }
        int tableVariantId = block.TableVariantId;
        DocTable renderTable = workspace.ResolveTableForVariant(table, tableVariantId);
        bool embeddedInteractiveAllowed = !table.IsDerived;

        var embeddedRectContent = new ImRect(
            blockX,
            blockY + TableBlockVerticalInset,
            blockWidth,
            MathF.Max(1f, blockHeight - (TableBlockVerticalInset * 2f)));
        bool isHovered = embeddedRectContent.Contains(Im.MousePos);

        float screenBlockY = blockY - _scrollY;
        var embeddedRect = new ImRect(
            blockX,
            screenBlockY + TableBlockVerticalInset,
            blockWidth,
            embeddedRectContent.Height);
        bool keepInteractiveForPopover = SpreadsheetRenderer.ShouldKeepEmbeddedInteractive(block.Id);
        bool isInteractive = (isFocused || isHovered || keepInteractiveForPopover) && embeddedInteractiveAllowed;

        // Document content is rendered with a -scrollY transform. Cancel it so
        // the embedded spreadsheet can use screen-space hit testing consistently.
        Im.PushTransform(0f, _scrollY);

        // Resolve per-block view (block.ViewId → specific view, fallback to first view)
        DocView? blockView = null;
        if (!string.IsNullOrEmpty(block.ViewId))
        {
            for (int i = 0; i < renderTable.Views.Count; i++)
            {
                if (string.Equals(renderTable.Views[i].Id, block.ViewId, StringComparison.Ordinal))
                {
                    blockView = renderTable.Views[i];
                    break;
                }
            }
        }
        blockView ??= renderTable.Views.Count > 0 ? renderTable.Views[0] : null;

        var viewType = blockView?.Type ?? DocViewType.Grid;
        switch (viewType)
        {
            case DocViewType.Board:
            case DocViewType.Calendar:
            case DocViewType.Chart:
            {
                // Clip everything to the embedded block bounds
                Im.PushClipRect(embeddedRect);
                DrawEmbeddedTitleRow(workspace, renderTable, block, embeddedRect, isInteractive);
                float contentTop = embeddedRect.Y + EmbeddedTitleRowHeight + EmbeddedTitleBottomSpacing;
                var viewRect = new ImRect(embeddedRect.X, contentTop, embeddedRect.Width, Math.Max(1f, embeddedRect.Bottom - contentTop));
                if (viewType == DocViewType.Board)
                    BoardRenderer.Draw(workspace, renderTable, blockView, viewRect, interactive: isInteractive, parentRowColumnId: null, parentRowId: null, tableInstanceBlock: block);
                else if (viewType == DocViewType.Calendar)
                    CalendarRenderer.Draw(workspace, renderTable, blockView, viewRect, interactive: isInteractive, parentRowColumnId: null, parentRowId: null, tableInstanceBlock: block);
                else
                    ChartRenderer.Draw(workspace, renderTable, blockView, viewRect, parentRowColumnId: null, parentRowId: null, tableInstanceBlock: block);
                Im.PopClipRect();
                break;
            }
            case DocViewType.Custom:
            {
                Im.PushClipRect(embeddedRect);
                DrawEmbeddedTitleRow(workspace, renderTable, block, embeddedRect, isInteractive);
                float contentTop = embeddedRect.Y + EmbeddedTitleRowHeight + EmbeddedTitleBottomSpacing;
                var viewRect = new ImRect(embeddedRect.X, contentTop, embeddedRect.Width, Math.Max(1f, embeddedRect.Bottom - contentTop));
                if (blockView != null &&
                    TableViewRendererResolver.TryGetCustomRenderer(blockView, out var customRenderer))
                {
                    if (!customRenderer.DrawEmbedded(workspace, renderTable, blockView, viewRect, isInteractive, block.Id))
                    {
                        customRenderer.Draw(workspace, renderTable, blockView, viewRect);
                    }
                }
                else
                {
                    Im.Text("Custom view renderer is unavailable.".AsSpan(), viewRect.X + 8f, viewRect.Y + 8f, Im.Style.FontSize, Im.Style.TextSecondary);
                }

                Im.PopClipRect();
                break;
            }
            default:
                SpreadsheetRenderer.DrawEmbedded(
                    workspace,
                    renderTable,
                    embeddedRect,
                    interactive: isInteractive,
                    stateKey: block.Id,
                    view: blockView,
                    tableInstanceBlock: block);
                break;
        }

        if (viewType == DocViewType.Chart)
        {
            DrawEmbeddedChartResizeHandle(workspace, document, block, embeddedRect, isFocused);
        }

        Im.PopTransform();
    }

    private static void DrawEmbeddedTitleRow(DocWorkspace workspace, DocTable table, DocBlock block, ImRect embeddedRect, bool interactive)
    {
        var style = Im.Style;
        var titleRowRect = new ImRect(embeddedRect.X, embeddedRect.Y, embeddedRect.Width, EmbeddedTitleRowHeight);
        string variantBadgeLabel = ResolveVariantBadgeLabel(table, block.TableVariantId);
        float badgeFontSize = Math.Max(9f, style.FontSize - 1f);
        float badgeWidth = Im.MeasureTextWidth(variantBadgeLabel.AsSpan(), badgeFontSize) + (EmbeddedVariantBadgeHorizontalPadding * 2f);
        float badgeHeight = Math.Max(18f, badgeFontSize + 6f);
        float badgeX = titleRowRect.Right - EmbeddedTitleOptionsWidth - EmbeddedVariantBadgeGap - badgeWidth;
        float badgeY = titleRowRect.Y + (EmbeddedTitleRowHeight - badgeHeight) * 0.5f;
        bool isBaseVariantBadge = block.TableVariantId == DocTableVariant.BaseVariantId;
        uint badgeFill = isBaseVariantBadge
            ? ImStyle.WithAlpha(style.Surface, 220)
            : ImStyle.WithAlpha(style.Primary, 84);
        uint badgeBorder = isBaseVariantBadge
            ? ImStyle.WithAlpha(style.Border, 170)
            : ImStyle.WithAlpha(style.Primary, 180);
        uint badgeTextColor = isBaseVariantBadge
            ? style.TextSecondary
            : style.TextPrimary;

        // Table name
        float titleTextX = titleRowRect.X + 4f;
        float titleTextY = titleRowRect.Y + (EmbeddedTitleRowHeight - (style.FontSize + EmbeddedTitleFontBoost)) * 0.5f;
        Im.Text(table.Name.AsSpan(), titleTextX, titleTextY, style.FontSize + EmbeddedTitleFontBoost, style.TextPrimary);

        Im.DrawRoundedRect(badgeX, badgeY, badgeWidth, badgeHeight, 4f, badgeFill);
        Im.DrawRoundedRectStroke(badgeX, badgeY, badgeWidth, badgeHeight, 4f, badgeBorder, 1f);
        Im.Text(
            variantBadgeLabel.AsSpan(),
            badgeX + EmbeddedVariantBadgeHorizontalPadding,
            badgeY + (badgeHeight - badgeFontSize) * 0.5f,
            badgeFontSize,
            badgeTextColor);

        // Options button
        if (interactive)
        {
            float optBtnSize = 24f;
            float optBtnX = titleRowRect.Right - EmbeddedTitleOptionsWidth + (EmbeddedTitleOptionsWidth - optBtnSize) * 0.5f;
            float optBtnY = titleRowRect.Y + (EmbeddedTitleRowHeight - optBtnSize) * 0.5f;
            bool optBtnHovered = new ImRect(optBtnX, optBtnY, optBtnSize, optBtnSize).Contains(Im.MousePos);
            if (optBtnHovered)
                Im.DrawRoundedRect(optBtnX, optBtnY, optBtnSize, optBtnSize, 4f, ImStyle.WithAlpha(style.Hover, 128));
            float optIconX = optBtnX + (optBtnSize - Im.MeasureTextWidth(_embeddedOptionsIcon.AsSpan(), style.FontSize)) * 0.5f;
            float optIconY = optBtnY + (optBtnSize - style.FontSize) * 0.5f;
            Im.Text(_embeddedOptionsIcon.AsSpan(), optIconX, optIconY, style.FontSize, optBtnHovered ? style.TextPrimary : style.TextSecondary);
            if (optBtnHovered && Im.Context.Input.MousePressed)
            {
                workspace.InspectedTable = table;
                workspace.InspectedBlockId = block.Id;
                workspace.ShowInspector = true;
            }
        }

        // Divider
        float dividerY = titleRowRect.Bottom + EmbeddedTitleBottomSpacing;
        Im.DrawLine(embeddedRect.X, dividerY, embeddedRect.Right, dividerY, 1f, style.Border);
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

    private static void DrawEmbeddedChartResizeHandle(
        DocWorkspace workspace,
        DocDocument document,
        DocBlock block,
        ImRect embeddedRect,
        bool isFocused)
    {
        bool isResizingThisBlock = _isEmbeddedChartResizing &&
                                   string.Equals(_embeddedResizeDocumentId, document.Id, StringComparison.Ordinal) &&
                                   string.Equals(_embeddedResizeBlockId, block.Id, StringComparison.Ordinal);
        if (_isEmbeddedChartResizing && !isResizingThisBlock)
        {
            return;
        }

        float handleMargin = 4f;
        var handleRect = new ImRect(
            embeddedRect.Right - EmbeddedResizeHandleSize - handleMargin,
            embeddedRect.Bottom - EmbeddedResizeHandleSize - handleMargin,
            EmbeddedResizeHandleSize,
            EmbeddedResizeHandleSize);

        bool handleHovered = handleRect.Contains(Im.MousePos);
        bool showHandle = isFocused || handleHovered || isResizingThisBlock;
        if (!showHandle)
        {
            return;
        }

        uint handleColor = isResizingThisBlock
            ? Im.Style.Active
            : handleHovered
                ? Im.Style.Hover
                : ImStyle.WithAlpha(Im.Style.Surface, 220);
        Im.DrawRoundedRect(handleRect.X, handleRect.Y, handleRect.Width, handleRect.Height, 3f, handleColor);
        Im.DrawRoundedRectStroke(handleRect.X, handleRect.Y, handleRect.Width, handleRect.Height, 3f, Im.Style.Border, 1f);

        float lineInset = 3f;
        float lineY1 = handleRect.Bottom - lineInset - 1f;
        float lineY2 = handleRect.Bottom - lineInset - 4f;
        float lineX1 = handleRect.Right - lineInset - 1f;
        float lineX2 = handleRect.Right - lineInset - 4f;
        uint glyphColor = ImStyle.WithAlpha(Im.Style.TextSecondary, 210);
        Im.DrawLine(lineX2, lineY1, lineX1, lineY2, 1f, glyphColor);
        Im.DrawLine(lineX2 - 3f, lineY1, lineX1, lineY2 - 3f, 1f, glyphColor);

        var input = Im.Context.Input;
        if (!isResizingThisBlock && handleHovered && input.MousePressed)
        {
            _isEmbeddedChartResizing = true;
            _embeddedResizeDocumentId = document.Id;
            _embeddedResizeBlockId = block.Id;
            _embeddedResizeStartMouseX = Im.MousePos.X;
            _embeddedResizeStartMouseY = Im.MousePos.Y;
            _embeddedResizeStartWidth = MathF.Max(EmbeddedResizeMinWidth, block.EmbeddedWidth > 0f ? block.EmbeddedWidth : embeddedRect.Width);
            _embeddedResizeStartHeight = MathF.Max(EmbeddedResizeMinHeight, block.EmbeddedHeight > 0f ? block.EmbeddedHeight : embeddedRect.Height);
            _embeddedResizePreviewWidth = _embeddedResizeStartWidth;
            _embeddedResizePreviewHeight = _embeddedResizeStartHeight;
            Im.Context.ConsumeMouseLeftPress();
            return;
        }

        if (!isResizingThisBlock)
        {
            return;
        }

        if (input.MouseDown)
        {
            float deltaX = Im.MousePos.X - _embeddedResizeStartMouseX;
            float deltaY = Im.MousePos.Y - _embeddedResizeStartMouseY;
            _embeddedResizePreviewWidth = MathF.Max(EmbeddedResizeMinWidth, _embeddedResizeStartWidth + deltaX);
            _embeddedResizePreviewHeight = MathF.Max(EmbeddedResizeMinHeight, _embeddedResizeStartHeight + deltaY);
            return;
        }

        float newWidth = _embeddedResizePreviewWidth;
        float newHeight = _embeddedResizePreviewHeight;
        float oldWidth = block.EmbeddedWidth;
        float oldHeight = block.EmbeddedHeight;
        if (MathF.Abs(newWidth - oldWidth) > 0.5f || MathF.Abs(newHeight - oldHeight) > 0.5f)
        {
            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.SetBlockEmbeddedSize,
                DocumentId = document.Id,
                BlockId = block.Id,
                OldEmbeddedWidth = oldWidth,
                NewEmbeddedWidth = newWidth,
                OldEmbeddedHeight = oldHeight,
                NewEmbeddedHeight = newHeight,
            });
        }

        _isEmbeddedChartResizing = false;
        _embeddedResizeDocumentId = "";
        _embeddedResizeBlockId = "";
        _embeddedResizeStartMouseX = 0f;
        _embeddedResizeStartMouseY = 0f;
        _embeddedResizeStartWidth = 0f;
        _embeddedResizeStartHeight = 0f;
        _embeddedResizePreviewWidth = 0f;
        _embeddedResizePreviewHeight = 0f;
    }

    private static DocView? ResolveBlockView(DocTable table, DocBlock block)
    {
        if (!string.IsNullOrEmpty(block.ViewId))
        {
            for (int i = 0; i < table.Views.Count; i++)
            {
                if (string.Equals(table.Views[i].Id, block.ViewId, StringComparison.Ordinal))
                    return table.Views[i];
            }
        }
        return table.Views.Count > 0 ? table.Views[0] : null;
    }

    private static void EnsureUniqueBlockIds(DocDocument document)
    {
        _blockIdValidationSet.Clear();
        int blockCount = document.Blocks.Count;
        for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            var block = document.Blocks[blockIndex];
            string blockId = block.Id;
            if (string.IsNullOrWhiteSpace(blockId) || !_blockIdValidationSet.Add(blockId))
            {
                string newBlockId = Guid.NewGuid().ToString();
                block.Id = newBlockId;
                _blockIdValidationSet.Add(newBlockId);
            }
        }
    }

    private const float EmbeddedBoardCalendarContentHeight = 320f;

    private static float ResolveEmbeddedWidthForLayout(DocWorkspace workspace, DocBlock block)
    {
        if (_isEmbeddedChartResizing &&
            workspace.ActiveDocument != null &&
            string.Equals(_embeddedResizeDocumentId, workspace.ActiveDocument.Id, StringComparison.Ordinal) &&
            string.Equals(_embeddedResizeBlockId, block.Id, StringComparison.Ordinal))
        {
            return _embeddedResizePreviewWidth;
        }

        return block.EmbeddedWidth;
    }

    private static float ResolveEmbeddedHeightForLayout(DocWorkspace workspace, DocBlock block)
    {
        if (_isEmbeddedChartResizing &&
            workspace.ActiveDocument != null &&
            string.Equals(_embeddedResizeDocumentId, workspace.ActiveDocument.Id, StringComparison.Ordinal) &&
            string.Equals(_embeddedResizeBlockId, block.Id, StringComparison.Ordinal))
        {
            return _embeddedResizePreviewHeight;
        }

        return block.EmbeddedHeight;
    }

    private static float GetTableBlockHeight(DocWorkspace workspace, DocBlock block, float blockWidth)
    {
        float fallbackEmbeddedHeight = Im.Style.FontSize + 20f;
        if (string.IsNullOrWhiteSpace(block.TableId))
        {
            return fallbackEmbeddedHeight + (TableBlockVerticalInset * 2f);
        }

        var table = workspace.Project.Tables.Find(candidate => string.Equals(candidate.Id, block.TableId, StringComparison.Ordinal));
        if (table == null)
        {
            return fallbackEmbeddedHeight + (TableBlockVerticalInset * 2f);
        }

        // Resolve per-block view type
        DocView? blockView = ResolveBlockView(table, block);
        var viewType = blockView?.Type ?? DocViewType.Grid;

        if (viewType == DocViewType.Board || viewType == DocViewType.Calendar)
        {
            float titleSpace = EmbeddedTitleRowHeight + EmbeddedTitleBottomSpacing;
            return titleSpace + EmbeddedBoardCalendarContentHeight + (TableBlockVerticalInset * 2f);
        }

        if (viewType == DocViewType.Chart)
        {
            float preferredEmbeddedHeight = ResolveEmbeddedHeightForLayout(workspace, block);
            if (preferredEmbeddedHeight > 0f)
            {
                return preferredEmbeddedHeight + (TableBlockVerticalInset * 2f);
            }

            float titleSpace = EmbeddedTitleRowHeight + EmbeddedTitleBottomSpacing;
            return titleSpace + EmbeddedChartDefaultContentHeight + (TableBlockVerticalInset * 2f);
        }

        if (viewType == DocViewType.Custom)
        {
            float titleSpace = EmbeddedTitleRowHeight + EmbeddedTitleBottomSpacing;
            if (blockView != null &&
                TableViewRendererResolver.TryGetCustomRenderer(blockView, out var customRenderer))
            {
                float measuredHeight = customRenderer.MeasureEmbeddedHeight(
                    workspace,
                    table,
                    blockView,
                    blockWidth,
                    EmbeddedChartDefaultContentHeight);
                if (measuredHeight > 0f)
                {
                    return titleSpace + measuredHeight + (TableBlockVerticalInset * 2f);
                }
            }

            return titleSpace + EmbeddedChartDefaultContentHeight + (TableBlockVerticalInset * 2f);
        }

        float embeddedHeight = SpreadsheetRenderer.MeasureEmbeddedHeight(table, blockWidth, workspace, blockView, block);
        return embeddedHeight + (TableBlockVerticalInset * 2f);
    }

    private static float GetTableBlockWidth(DocWorkspace workspace, DocBlock block, float maxBlockWidth)
    {
        float fallbackWidth = Math.Min(maxBlockWidth, 320f);
        float minWidth = Math.Min(120f, maxBlockWidth);
        if (string.IsNullOrWhiteSpace(block.TableId))
        {
            return Math.Clamp(fallbackWidth, minWidth, maxBlockWidth);
        }

        var table = workspace.Project.Tables.Find(candidate => string.Equals(candidate.Id, block.TableId, StringComparison.Ordinal));
        if (table == null)
        {
            return Math.Clamp(fallbackWidth, minWidth, maxBlockWidth);
        }

        float preferredEmbeddedWidth = ResolveEmbeddedWidthForLayout(workspace, block);
        if (preferredEmbeddedWidth > 0f)
        {
            return Math.Clamp(preferredEmbeddedWidth, minWidth, maxBlockWidth);
        }

        float embeddedWidth = SpreadsheetRenderer.MeasureEmbeddedWidth(table, maxBlockWidth);
        return Math.Clamp(embeddedWidth, minWidth, maxBlockWidth);
    }

    private static void DrawBlockContextMenu(DocWorkspace workspace)
    {
        if (!ImContextMenu.Begin(BlockContextMenuId))
        {
            return;
        }

        if (!TryFindBlock(
                workspace,
                _contextMenuDocumentId,
                _contextMenuBlockId,
                out var document,
                out var block,
                out _))
        {
            ImContextMenu.End();
            return;
        }

        if (block.Type == DocBlockType.Formula)
        {
            if (ImContextMenu.Item("Edit formula"))
            {
                OpenFormulaBlockEditor(workspace, document, block);
            }
        }
        else if (block.Type == DocBlockType.Variable)
        {
            if (ImContextMenu.Item("Edit variable"))
            {
                OpenVariableBlockEditor(document, block);
            }
        }

        if (ImContextMenu.Item("Convert to paragraph"))
        {
            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.ChangeBlockType,
                DocumentId = document.Id,
                BlockId = block.Id,
                OldBlockType = block.Type,
                NewBlockType = DocBlockType.Paragraph,
            });
        }

        if (ImContextMenu.Item("Delete block"))
        {
            int blockIndex = document.Blocks.FindIndex(candidate => string.Equals(candidate.Id, block.Id, StringComparison.Ordinal));
            if (blockIndex >= 0)
            {
                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.RemoveBlock,
                    DocumentId = document.Id,
                    BlockIndex = blockIndex,
                    BlockSnapshot = block.Clone(),
                });
            }
        }

        ImContextMenu.End();
    }

    private static void OpenFormulaBlockEditor(DocWorkspace workspace, DocDocument document, DocBlock block)
    {
        _editFormulaBlockDocumentId = document.Id;
        _editFormulaBlockId = block.Id;
        CopyTextToBuffer(block.Text.PlainText, _editFormulaBlockBuffer, out _editFormulaBlockBufferLength);
        SpreadsheetRenderer.ResetSharedFormulaEditorInspector(
            "Document formula",
            "Use functions, table references, and document variables via thisDoc.<variable> or docs.<documentAlias>.<variable>.",
            "Example: Concat(\"Revenue: \", docs.balance_sheet.total_revenue)");
        ImModal.Open(FormulaBlockModalId);
    }

    private static void OpenVariableBlockEditor(DocDocument document, DocBlock block)
    {
        _editVariableBlockDocumentId = document.Id;
        _editVariableBlockId = block.Id;
        _editVariableValidationMessage = "";
        if (DocumentFormulaSyntax.TryParseVariableDeclaration(
                block.Text.PlainText,
                out string variableName,
                out bool hasExpression,
                out string expression))
        {
            CopyTextToBuffer(variableName, _editVariableNameBuffer, out _editVariableNameBufferLength);
            CopyTextToBuffer(hasExpression ? expression : "", _editVariableFormulaBuffer, out _editVariableFormulaBufferLength);
        }
        else
        {
            CopyTextToBuffer("", _editVariableNameBuffer, out _editVariableNameBufferLength);
            CopyTextToBuffer("", _editVariableFormulaBuffer, out _editVariableFormulaBufferLength);
        }

        SpreadsheetRenderer.ResetSharedFormulaEditorInspector(
            "Variable assignment",
            "Assign a formula value to this variable. Reference it later via thisDoc.<variable> or docs.<documentAlias>.<variable>.",
            "Example: Concat(\"Revenue: \", docs.balance_sheet.total_revenue)");
        ImModal.Open(VariableBlockModalId);
    }

    private static void DrawFormulaBlockModal(DocWorkspace workspace)
    {
        if (!ImModal.IsOpen(FormulaBlockModalId))
        {
            return;
        }

        const float modalWidth = 680f;
        if (!ImModal.Begin(FormulaBlockModalId, modalWidth, 470f, "Edit formula"))
        {
            if (!ImModal.IsOpen(FormulaBlockModalId))
            {
                _editFormulaBlockDocumentId = "";
                _editFormulaBlockId = "";
                _editFormulaBlockBufferLength = 0;
            }

            return;
        }

        var style = Im.Style;
        float left = ImModal.ContentOffset.X;
        float top = ImModal.ContentOffset.Y;
        float width = modalWidth - (style.Padding * 2f);
        string label = _documentBlockIcon + "  Document formula";
        Im.Text(label.AsSpan(), left, top, style.FontSize, style.TextSecondary);

        float editorY = top + style.FontSize + 8f;
        float editorHeight = SpreadsheetRenderer.DrawSharedFormulaEditor(
            workspace,
            null,
            "doc_formula_block_input",
            _editFormulaBlockBuffer,
            ref _editFormulaBlockBufferLength,
            _editFormulaBlockBuffer.Length,
            left,
            editorY,
            width,
            includeRowContextCompletions: false);

        string formulaText = new string(_editFormulaBlockBuffer, 0, _editFormulaBlockBufferLength);
        string previewLabel = "Preview: #ERR";
        uint previewColor = BlendColor(style.Primary, 0.55f, style.TextPrimary);
        float previewY = editorY + editorHeight + 8f;
        if (TryFindBlock(
                workspace,
                _editFormulaBlockDocumentId,
                _editFormulaBlockId,
                out var document,
                out _,
                out _))
        {
            if (_documentFormulaPreviewEngine.TryEvaluateDocumentExpression(
                    workspace.Project,
                    document,
                    formulaText,
                    out string previewText))
            {
                previewLabel = "Preview: " + previewText;
                previewColor = style.TextPrimary;
            }
        }

        Im.Text(previewLabel.AsSpan(), left, previewY, style.FontSize, previewColor);

        float inspectorY = previewY + style.FontSize + 8f;
        float inspectorHeight = SpreadsheetRenderer.DrawSharedFormulaInspectorPanelForDocument(left, inspectorY, width);
        float buttonY = inspectorY + inspectorHeight + 10f;
        float buttonWidth = 90f;
        if (Im.Button("Apply", left, buttonY, buttonWidth, style.MinButtonHeight))
        {
            if (TryFindBlock(
                    workspace,
                    _editFormulaBlockDocumentId,
                    _editFormulaBlockId,
                    out var targetDocument,
                    out var targetBlock,
                    out int targetBlockIndex))
            {
                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.SetBlockText,
                    DocumentId = targetDocument.Id,
                    BlockId = targetBlock.Id,
                    OldBlockText = targetBlock.Text.PlainText,
                    NewBlockText = formulaText,
                });
                _formulaDisplayCacheByBlockId.Remove(targetBlock.Id);

                if (workspace.ActiveDocument != null &&
                    string.Equals(workspace.ActiveDocument.Id, targetDocument.Id, StringComparison.Ordinal) &&
                    workspace.FocusedBlockIndex == targetBlockIndex)
                {
                    workspace.FocusedBlockTextSnapshot = formulaText;
                    CopyTextToBuffer(formulaText);
                }
            }

            ImModal.Close();
        }

        if (Im.Button("Cancel", left + buttonWidth + 10f, buttonY, buttonWidth, style.MinButtonHeight))
        {
            ImModal.Close();
        }

        ImModal.End();

        if (!ImModal.IsOpen(FormulaBlockModalId))
        {
            _editFormulaBlockDocumentId = "";
            _editFormulaBlockId = "";
            _editFormulaBlockBufferLength = 0;
        }
    }

    private static void DrawVariableBlockModal(DocWorkspace workspace)
    {
        if (!ImModal.IsOpen(VariableBlockModalId))
        {
            return;
        }

        const float modalWidth = 680f;
        if (!ImModal.Begin(VariableBlockModalId, modalWidth, 560f, "Edit variable"))
        {
            if (!ImModal.IsOpen(VariableBlockModalId))
            {
                ResetVariableBlockEditorState();
            }

            return;
        }

        var style = Im.Style;
        float left = ImModal.ContentOffset.X;
        float top = ImModal.ContentOffset.Y;
        float width = modalWidth - (style.Padding * 2f);
        string label = _documentBlockIcon + "  Document variable";
        Im.Text(label.AsSpan(), left, top, style.FontSize, style.TextSecondary);

        float rowY = top + style.FontSize + 8f;
        Im.Text("Variable name".AsSpan(), left, rowY, style.FontSize, style.TextPrimary);
        rowY += style.FontSize + 4f;
        float atSignWidth = Im.MeasureTextWidth("@".AsSpan(), style.FontSize);
        float inputHeight = style.MinButtonHeight;
        float atSignY = rowY + (inputHeight - style.FontSize) * 0.5f;
        Im.Text("@".AsSpan(), left, atSignY, style.FontSize, style.TextSecondary);
        Im.TextInput(
            "doc_variable_name_input",
            _editVariableNameBuffer,
            ref _editVariableNameBufferLength,
            _editVariableNameBuffer.Length,
            left + atSignWidth + 6f,
            rowY,
            Math.Max(80f, width - (atSignWidth + 6f)));

        rowY += inputHeight + 10f;
        Im.Text("Assignment formula (optional)".AsSpan(), left, rowY, style.FontSize, style.TextPrimary);
        rowY += style.FontSize + 4f;
        float editorHeight = SpreadsheetRenderer.DrawSharedFormulaEditor(
            workspace,
            null,
            "doc_variable_formula_input",
            _editVariableFormulaBuffer,
            ref _editVariableFormulaBufferLength,
            _editVariableFormulaBuffer.Length,
            left,
            rowY,
            width,
            includeRowContextCompletions: false);
        rowY += editorHeight + 8f;

        string variableNameText = new string(_editVariableNameBuffer, 0, _editVariableNameBufferLength).Trim();
        string expressionText = new string(_editVariableFormulaBuffer, 0, _editVariableFormulaBufferLength).Trim();
        string declarationText = BuildVariableDeclarationText(variableNameText, expressionText);
        bool variableNameIsValid = DocumentFormulaSyntax.IsValidIdentifier(variableNameText.AsSpan());
        if (variableNameIsValid &&
            string.Equals(_editVariableValidationMessage, "Invalid variable name.", StringComparison.Ordinal))
        {
            _editVariableValidationMessage = "";
        }

        string previewLabel;
        uint previewColor;
        if (expressionText.Length == 0)
        {
            previewLabel = "Assigned value: (none)";
            previewColor = style.TextSecondary;
        }
        else if (TryFindBlock(
                     workspace,
                     _editVariableBlockDocumentId,
                     _editVariableBlockId,
                     out var previewDocument,
                     out _,
                     out _)
                 && _documentFormulaPreviewEngine.TryEvaluateDocumentExpression(
                     workspace.Project,
                     previewDocument,
                     expressionText,
                     out string previewValueText))
        {
            previewLabel = "Assigned value: " + previewValueText;
            previewColor = style.TextPrimary;
        }
        else
        {
            previewLabel = "Assigned value: #ERR";
            previewColor = BlendColor(style.Primary, 0.55f, style.TextPrimary);
        }

        Im.Text(previewLabel.AsSpan(), left, rowY, style.FontSize, previewColor);
        rowY += style.FontSize + 8f;
        float inspectorHeight = SpreadsheetRenderer.DrawSharedFormulaInspectorPanelForDocument(left, rowY, width);
        rowY += inspectorHeight + 8f;

        string declarationPreviewText = declarationText.Length > 0
            ? declarationText
            : "@<name>";
        Im.Text(("Declaration: " + declarationPreviewText).AsSpan(), left, rowY, style.FontSize - 1f, style.TextSecondary);
        rowY += style.FontSize + 4f;

        if (!variableNameIsValid)
        {
            Im.Text(
                "Variable name must be a valid identifier (letters, digits, underscore; cannot start with digit).".AsSpan(),
                left,
                rowY,
                style.FontSize - 1f,
                style.Secondary);
            rowY += style.FontSize + 2f;
        }

        if (!string.IsNullOrWhiteSpace(_editVariableValidationMessage))
        {
            Im.Text(
                _editVariableValidationMessage.AsSpan(),
                left,
                rowY,
                style.FontSize - 1f,
                style.Secondary);
            rowY += style.FontSize + 2f;
        }

        float buttonY = rowY + 8f;
        float buttonWidth = 90f;
        if (Im.Button("Apply", left, buttonY, buttonWidth, style.MinButtonHeight))
        {
            if (!variableNameIsValid)
            {
                _editVariableValidationMessage = "Invalid variable name.";
            }
            else if (TryFindBlock(
                         workspace,
                         _editVariableBlockDocumentId,
                         _editVariableBlockId,
                         out var targetDocument,
                         out var targetBlock,
                         out int targetBlockIndex))
            {
                _editVariableValidationMessage = "";
                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.SetBlockText,
                    DocumentId = targetDocument.Id,
                    BlockId = targetBlock.Id,
                    OldBlockText = targetBlock.Text.PlainText,
                    NewBlockText = declarationText,
                });

                if (workspace.ActiveDocument != null &&
                    string.Equals(workspace.ActiveDocument.Id, targetDocument.Id, StringComparison.Ordinal) &&
                    workspace.FocusedBlockIndex == targetBlockIndex)
                {
                    workspace.FocusedBlockTextSnapshot = declarationText;
                    CopyTextToBuffer(declarationText);
                }

                ImModal.Close();
            }
        }

        if (Im.Button("Cancel", left + buttonWidth + 10f, buttonY, buttonWidth, style.MinButtonHeight))
        {
            ImModal.Close();
        }

        ImModal.End();

        if (!ImModal.IsOpen(VariableBlockModalId))
        {
            ResetVariableBlockEditorState();
        }
    }

    private static string BuildVariableDeclarationText(string variableName, string expressionText)
    {
        if (string.IsNullOrWhiteSpace(variableName))
        {
            return "";
        }

        if (string.IsNullOrWhiteSpace(expressionText))
        {
            return "@" + variableName;
        }

        return "@" + variableName + " = " + expressionText;
    }

    private static void ResetVariableBlockEditorState()
    {
        _editVariableBlockDocumentId = "";
        _editVariableBlockId = "";
        _editVariableNameBufferLength = 0;
        _editVariableFormulaBufferLength = 0;
        _editVariableValidationMessage = "";
    }

    private static bool TryFindBlock(
        DocWorkspace workspace,
        string documentId,
        string blockId,
        out DocDocument document,
        out DocBlock block,
        out int blockIndex)
    {
        document = null!;
        block = null!;
        blockIndex = -1;
        if (string.IsNullOrWhiteSpace(documentId) || string.IsNullOrWhiteSpace(blockId))
        {
            return false;
        }

        for (int documentIndex = 0; documentIndex < workspace.Project.Documents.Count; documentIndex++)
        {
            var candidateDocument = workspace.Project.Documents[documentIndex];
            if (!string.Equals(candidateDocument.Id, documentId, StringComparison.Ordinal))
            {
                continue;
            }

            int candidateBlockIndex = candidateDocument.Blocks.FindIndex(
                candidateBlock => string.Equals(candidateBlock.Id, blockId, StringComparison.Ordinal));
            if (candidateBlockIndex < 0)
            {
                return false;
            }

            document = candidateDocument;
            block = candidateDocument.Blocks[candidateBlockIndex];
            blockIndex = candidateBlockIndex;
            return true;
        }

        return false;
    }

    // =====================================================================
    //  Block operations
    // =====================================================================

    public static void CommitFocusedBlock(DocWorkspace workspace, DocDocument document)
    {
        if (workspace.FocusedBlockIndex < 0 || workspace.FocusedBlockIndex >= document.Blocks.Count)
            return;

        var block = document.Blocks[workspace.FocusedBlockIndex];
        if (!IsTextEditableBlock(block.Type))
        {
            workspace.FocusedBlockTextSnapshot = null;
            return;
        }

        string currentText = new string(_editBuffer, 0, _editBufferLength);

        // Only create undo command if text actually changed
        if (workspace.FocusedBlockTextSnapshot != null && currentText != workspace.FocusedBlockTextSnapshot)
        {
            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.SetBlockText,
                DocumentId = document.Id,
                BlockId = block.Id,
                OldBlockText = workspace.FocusedBlockTextSnapshot,
                NewBlockText = currentText,
                OldSpans = new List<RichSpan>(block.Text.Spans),
                NewSpans = new List<RichSpan>(block.Text.Spans),
            });
        }

        workspace.FocusedBlockTextSnapshot = null;
    }

    public static void AddNewBlockAfter(DocWorkspace workspace, DocDocument document, int afterIndex,
        DocBlockType blockType = DocBlockType.Paragraph)
    {
        string prevOrder = document.Blocks[afterIndex].Order;
        string nextOrder = afterIndex + 1 < document.Blocks.Count
            ? document.Blocks[afterIndex + 1].Order
            : "";

        string newOrder = string.IsNullOrEmpty(nextOrder)
            ? FractionalIndex.After(prevOrder)
            : FractionalIndex.Between(prevOrder, nextOrder);

        var newBlock = new DocBlock
        {
            Type = blockType,
            Order = newOrder,
        };

        int insertIndex = afterIndex + 1;
        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.AddBlock,
            DocumentId = document.Id,
            BlockIndex = insertIndex,
            BlockSnapshot = newBlock,
        });

        _pendingRevealBlockId = newBlock.Id;

        // Focus the new block (must use FocusBlock to set ImGui focus + caret)
        FocusBlock(workspace, document, insertIndex, 0);
    }

    public static void FocusBlock(DocWorkspace workspace, DocDocument document, int blockIndex, int caretPos)
    {
        if (blockIndex < 0 || blockIndex >= document.Blocks.Count) return;

        if (_hasMultiBlockTextSelection && _multiTextFocusBlock != blockIndex)
        {
            ClearMultiBlockTextSelection();
        }

        workspace.FocusedBlockIndex = blockIndex;
        var block = document.Blocks[blockIndex];
        if (!IsTextEditableBlock(block.Type))
        {
            workspace.FocusedBlockTextSnapshot = null;
            _editBufferLength = 0;
            Im.Context.ClearFocus();
            ClearMultiBlockTextSelection();
            return;
        }

        workspace.FocusedBlockTextSnapshot = block.Text.PlainText;
        CopyTextToBuffer(block.Text.PlainText);

        string widgetId = $"doc_block_{block.Id}";
        int wid = Im.Context.GetId(widgetId);
        ImRichTextArea.SetState(wid, caretPos);
        Im.Context.RequestFocus(wid);
    }

    /// <summary>
    /// Gets the current edit buffer content.
    /// </summary>
    public static string GetEditBufferText() => new string(_editBuffer, 0, _editBufferLength);

    /// <summary>
    /// Gets the caret position from the active block's ImTextArea.
    /// </summary>
    public static int GetCaretPos(DocBlock block)
    {
        if (!IsTextEditableBlock(block.Type))
        {
            return 0;
        }

        string widgetId = $"doc_block_{block.Id}";
        int wid = Im.Context.GetId(widgetId);
        if (ImRichTextArea.TryGetState(wid, out int caretPos, out _, out _))
            return caretPos;
        return _editBufferLength;
    }

    /// <summary>
    /// Gets selection range. Returns false if no selection.
    /// </summary>
    public static bool GetSelection(DocBlock block, out int selStart, out int selEnd)
    {
        if (!IsTextEditableBlock(block.Type))
        {
            selStart = -1;
            selEnd = -1;
            return false;
        }

        string widgetId = $"doc_block_{block.Id}";
        int wid = Im.Context.GetId(widgetId);
        if (ImRichTextArea.TryGetState(wid, out _, out selStart, out selEnd))
        {
            if (selStart >= 0 && selEnd >= 0 && selStart != selEnd)
            {
                if (selStart > selEnd) (selStart, selEnd) = (selEnd, selStart);
                return true;
            }
        }
        selStart = -1;
        selEnd = -1;
        return false;
    }

    /// <summary>Returns the screen Y position of a block, accounting for scroll offset.</summary>
    public static float GetBlockScreenY(int blockIndex) =>
        blockIndex >= 0 && blockIndex < 1024 ? _blockY[blockIndex] - _scrollY : 0f;

    /// <summary>Returns the height of a block from the layout cache.</summary>
    public static float GetBlockHeight(int blockIndex) =>
        blockIndex >= 0 && blockIndex < 1024 ? _blockH[blockIndex] : 0f;

    /// <summary>
    /// Gets bounds for the current text selection in screen coordinates.
    /// For multi-block text selection this spans all selected text blocks.
    /// </summary>
    public static bool TryGetSelectionScreenBounds(DocWorkspace workspace, DocDocument document, out ImRect selectionRect)
    {
        selectionRect = default;
        if (document.Blocks.Count <= 0)
        {
            return false;
        }

        if (_hasMultiBlockTextSelection &&
            _multiTextStartBlock >= 0 &&
            _multiTextEndBlock >= _multiTextStartBlock)
        {
            int startBlockIndex = Math.Clamp(_multiTextStartBlock, 0, document.Blocks.Count - 1);
            int endBlockIndex = Math.Clamp(_multiTextEndBlock, startBlockIndex, document.Blocks.Count - 1);
            return TryGetBlockRangeScreenBounds(document, startBlockIndex, endBlockIndex, out selectionRect);
        }

        int focusedBlockIndex = workspace.FocusedBlockIndex;
        if (focusedBlockIndex < 0 || focusedBlockIndex >= document.Blocks.Count)
        {
            return false;
        }

        if (!GetSelection(document.Blocks[focusedBlockIndex], out _, out _))
        {
            return false;
        }

        return TryGetBlockRangeScreenBounds(document, focusedBlockIndex, focusedBlockIndex, out selectionRect);
    }

    public static ImRect GetDocumentContentRect()
    {
        return _lastContentRect;
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private static bool TryGetBlockRangeScreenBounds(
        DocDocument document,
        int startBlockIndex,
        int endBlockIndex,
        out ImRect blockRangeRect)
    {
        blockRangeRect = default;
        int blockCount = Math.Min(document.Blocks.Count, 1024);
        if (blockCount <= 0)
        {
            return false;
        }

        int clampedStartBlockIndex = Math.Clamp(startBlockIndex, 0, blockCount - 1);
        int clampedEndBlockIndex = Math.Clamp(endBlockIndex, clampedStartBlockIndex, blockCount - 1);
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxRight = float.MinValue;
        float maxBottom = float.MinValue;

        for (int blockIndex = clampedStartBlockIndex; blockIndex <= clampedEndBlockIndex; blockIndex++)
        {
            var block = document.Blocks[blockIndex];
            float indentOffset = block.IndentLevel * IndentWidth;
            float blockX = _lastColumnX + GutterWidth + indentOffset;
            float maxBlockWidth = Math.Max(24f, _lastColumnW - GutterWidth - indentOffset);
            float blockWidth = _blockW[blockIndex] > 0f ? _blockW[blockIndex] : maxBlockWidth;
            float blockY = _blockY[blockIndex] - _scrollY;
            float blockHeight = _blockH[blockIndex];
            if (blockWidth <= 0f || blockHeight <= 0f)
            {
                continue;
            }

            minX = MathF.Min(minX, blockX);
            minY = MathF.Min(minY, blockY);
            maxRight = MathF.Max(maxRight, blockX + blockWidth);
            maxBottom = MathF.Max(maxBottom, blockY + blockHeight);
        }

        if (minX == float.MaxValue || minY == float.MaxValue)
        {
            return false;
        }

        blockRangeRect = new ImRect(minX, minY, Math.Max(1f, maxRight - minX), Math.Max(1f, maxBottom - minY));
        return true;
    }

    private static int ComputeDragInsertIndex(DocDocument document, ImRect contentRect, float mouseY)
    {
        int blockCount = Math.Min(document.Blocks.Count, 1024);
        for (int i = 0; i < blockCount; i++)
        {
            float midY = _blockY[i] + _blockH[i] * 0.5f;
            if (mouseY < midY)
                return i;
        }
        return blockCount;
    }

    private static bool IsMouseInBlockSelectionGutter(
        DocDocument document,
        float columnX,
        int blockIndex,
        float mouseX)
    {
        if (blockIndex < 0 || blockIndex >= document.Blocks.Count)
        {
            return false;
        }

        var block = document.Blocks[blockIndex];
        float indentOffset = block.IndentLevel * IndentWidth;
        float gutterLeft = columnX + indentOffset;
        float gutterRight = gutterLeft + GutterWidth;
        return mouseX >= gutterLeft && mouseX < gutterRight;
    }

    private static int GetSelectionBlockIndexAtY(DocDocument document, float mouseY)
    {
        int blockCount = Math.Min(document.Blocks.Count, 1024);
        if (blockCount <= 0)
        {
            return -1;
        }

        if (mouseY <= _blockY[0])
        {
            return 0;
        }

        for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            float blockBottom = _blockY[blockIndex] + _blockH[blockIndex];
            if (mouseY < blockBottom)
            {
                return blockIndex;
            }
        }

        return blockCount - 1;
    }

    private static void RevealPendingBlockIfNeeded(DocDocument document, ImRect contentRect, float totalContentHeight)
    {
        if (string.IsNullOrWhiteSpace(_pendingRevealBlockId))
        {
            return;
        }

        int blockCount = Math.Min(document.Blocks.Count, 1024);
        int blockIndex = -1;
        for (int index = 0; index < blockCount; index++)
        {
            if (string.Equals(document.Blocks[index].Id, _pendingRevealBlockId, StringComparison.Ordinal))
            {
                blockIndex = index;
                break;
            }
        }

        if (blockIndex < 0)
        {
            _pendingRevealBlockId = "";
            return;
        }

        float viewportTop = contentRect.Y + _scrollY;
        float viewportBottom = viewportTop + contentRect.Height;
        float blockTop = _blockY[blockIndex];
        float blockBottom = _blockY[blockIndex] + _blockH[blockIndex];

        if (blockBottom > viewportBottom)
        {
            _scrollY += blockBottom - viewportBottom;
        }
        else if (blockTop < viewportTop)
        {
            _scrollY -= viewportTop - blockTop;
        }

        float maxScroll = Math.Max(0f, totalContentHeight - contentRect.Height);
        _scrollY = Math.Clamp(_scrollY, 0f, maxScroll);
        _pendingRevealBlockId = "";
    }

    private static void CopyTextToBuffer(string text)
    {
        CopyTextToBuffer(text, _editBuffer, out _editBufferLength);
    }

    private static void CopyTextToBuffer(string text, char[] buffer, out int length)
    {
        int maxLength = buffer.Length;
        int copyLength = Math.Min(text.Length, maxLength);
        text.AsSpan(0, copyLength).CopyTo(buffer);
        if (copyLength < maxLength)
        {
            buffer[copyLength] = '\0';
        }

        length = copyLength;
    }

    public static bool IsTextEditableBlock(DocBlockType blockType) =>
        blockType != DocBlockType.Divider &&
        blockType != DocBlockType.Table;

    private static bool IsFocusedBlockTextEditable(DocWorkspace workspace, DocDocument document)
    {
        if (workspace.FocusedBlockIndex < 0 || workspace.FocusedBlockIndex >= document.Blocks.Count)
        {
            return false;
        }

        return IsTextEditableBlock(document.Blocks[workspace.FocusedBlockIndex].Type);
    }

    public static float GetFontSize(DocBlockType type)
    {
        var baseFontSize = Im.Style.FontSize;
        return type switch
        {
            DocBlockType.Heading1 => baseFontSize * 2.0f,
            DocBlockType.Heading2 => baseFontSize * 1.5f,
            DocBlockType.Heading3 => baseFontSize * 1.25f,
            DocBlockType.Heading4 => baseFontSize * 1.14f,
            DocBlockType.Heading5 => baseFontSize * 1.06f,
            DocBlockType.Heading6 => baseFontSize * 1.0f,
            DocBlockType.CodeBlock => baseFontSize * 0.9f,
            _ => baseFontSize,
        };
    }

    private static float GetBlockLineHeight(float fontSize)
    {
        return fontSize * 1.4f;
    }

    private static float GetBlockTextPaddingTop(DocBlockType type)
    {
        return type switch
        {
            DocBlockType.Heading1 => 7f,
            DocBlockType.Heading2 => 9f,
            DocBlockType.Heading3 => 8f,
            DocBlockType.Heading4 => 6f,
            DocBlockType.Heading5 => 5f,
            DocBlockType.Heading6 => 5f,
            DocBlockType.BulletList => 2f,
            DocBlockType.NumberedList => 2f,
            DocBlockType.CheckboxList => 2f,
            DocBlockType.CodeBlock => 6f,
            DocBlockType.Quote => 4f,
            DocBlockType.Paragraph => 3f,
            _ => BlockPaddingY,
        };
    }

    private static float GetBlockTextPaddingBottom(DocBlockType type)
    {
        return type switch
        {
            DocBlockType.Heading1 => 8f,
            DocBlockType.Heading2 => 6f,
            DocBlockType.Heading3 => 5f,
            DocBlockType.Heading4 => 4f,
            DocBlockType.Heading5 => 4f,
            DocBlockType.Heading6 => 4f,
            DocBlockType.BulletList => 2f,
            DocBlockType.NumberedList => 2f,
            DocBlockType.CheckboxList => 2f,
            DocBlockType.CodeBlock => 6f,
            DocBlockType.Quote => 5f,
            DocBlockType.Paragraph => 5f,
            _ => BlockPaddingY,
        };
    }

    private static uint GetTextColor(DocBlockType type, ImStyle style)
    {
        return type switch
        {
            DocBlockType.Heading1 or DocBlockType.Heading2 or DocBlockType.Heading3 or DocBlockType.Heading4 or DocBlockType.Heading5 or DocBlockType.Heading6 => 0xFFFFFFFF,
            DocBlockType.Quote => style.TextSecondary,
            DocBlockType.CodeBlock => 0xFF77BBDD,
            DocBlockType.Formula => BlendColor(style.Primary, 0.72f, style.TextPrimary),
            DocBlockType.Variable => BlendColor(style.Active, 0.62f, style.TextPrimary),
            _ => style.TextPrimary,
        };
    }

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

    private static float DistanceFromPointToRect(System.Numerics.Vector2 point, ImRect rect)
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
}
