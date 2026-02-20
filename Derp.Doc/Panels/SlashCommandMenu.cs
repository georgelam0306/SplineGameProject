using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Input;
using DerpLib.ImGui.Rendering;
using DerpLib.ImGui.Widgets;
using Derp.Doc.Commands;
using Derp.Doc.Editor;
using Derp.Doc.Model;
using FontAwesome.Sharp;

namespace Derp.Doc.Panels;

/// <summary>
/// Shows a filterable popup when "/" is typed at the start of an empty Paragraph block.
/// Arrow keys navigate, Enter selects a block type, Escape closes.
/// Mouse hover highlights, mouse click selects.
/// </summary>
internal static class SlashCommandMenu
{
    private enum SlashCommandKind : byte
    {
        BlockType,
        TablePicker,
    }

    private readonly struct SlashCommandDefinition
    {
        public SlashCommandDefinition(string label, string category, SlashCommandKind kind, DocBlockType blockType)
        {
            Label = label;
            Category = category;
            Kind = kind;
            BlockType = blockType;
        }

        public string Label { get; }
        public string Category { get; }
        public SlashCommandKind Kind { get; }
        public DocBlockType BlockType { get; }
    }

    private static bool _isOpen;
    private static int _selectedIndex;
    private static string _filterText = "";
    private static readonly char[] _tableSearchBuffer = new char[128];
    private static int _tableSearchLength;
    private static float _tablePickerScrollY;
    private static int _tablePickerSelectedTableIndex = -1;
    private static string _pendingTableDocumentId = "";
    private static string _pendingTableBlockId = "";
    private const string TablePickerModalId = "doc_table_picker_modal";
    private const float TablePickerWidth = 560f;
    private const float TablePickerHeight = 460f;
    private static readonly string TableIcon = ((char)IconChar.Table).ToString();
    private static readonly string AddIcon = ((char)IconChar.CirclePlus).ToString();
    private static readonly string ImportIcon = ((char)IconChar.FileImport).ToString();
    private static readonly int[] _filteredTableIndices = new int[256];

    private static readonly SlashCommandDefinition[] AllCommands =
    [
        new SlashCommandDefinition("Table", "Tables", SlashCommandKind.TablePicker, DocBlockType.Table),
        new SlashCommandDefinition("Paragraph", "Text", SlashCommandKind.BlockType, DocBlockType.Paragraph),
        new SlashCommandDefinition("Heading 1", "Text", SlashCommandKind.BlockType, DocBlockType.Heading1),
        new SlashCommandDefinition("Heading 2", "Text", SlashCommandKind.BlockType, DocBlockType.Heading2),
        new SlashCommandDefinition("Heading 3", "Text", SlashCommandKind.BlockType, DocBlockType.Heading3),
        new SlashCommandDefinition("Heading 4", "Text", SlashCommandKind.BlockType, DocBlockType.Heading4),
        new SlashCommandDefinition("Heading 5", "Text", SlashCommandKind.BlockType, DocBlockType.Heading5),
        new SlashCommandDefinition("Heading 6", "Text", SlashCommandKind.BlockType, DocBlockType.Heading6),
        new SlashCommandDefinition("Bullet List", "Text", SlashCommandKind.BlockType, DocBlockType.BulletList),
        new SlashCommandDefinition("Numbered List", "Text", SlashCommandKind.BlockType, DocBlockType.NumberedList),
        new SlashCommandDefinition("Checkbox List", "Text", SlashCommandKind.BlockType, DocBlockType.CheckboxList),
        new SlashCommandDefinition("Code Block", "Text", SlashCommandKind.BlockType, DocBlockType.CodeBlock),
        new SlashCommandDefinition("Quote", "Text", SlashCommandKind.BlockType, DocBlockType.Quote),
        new SlashCommandDefinition("Formula", "Data", SlashCommandKind.BlockType, DocBlockType.Formula),
        new SlashCommandDefinition("Variable", "Data", SlashCommandKind.BlockType, DocBlockType.Variable),
        new SlashCommandDefinition("Divider", "Text", SlashCommandKind.BlockType, DocBlockType.Divider),
    ];

    // Scratch buffer for filtered items
    private static readonly int[] _filteredIndices = new int[AllCommands.Length];
    private static int _filteredCount;

    /// <summary>Whether the slash command menu is currently open (from last Draw call).</summary>
    public static bool IsOpen => _isOpen;

    /// <summary>The menu rect from the last Draw call (screen coordinates, for click suppression).</summary>
    public static ImRect MenuRect { get; private set; }

    public static void Draw(DocWorkspace workspace, DocDocument document, float menuX, ImInput input)
    {
        DrawTablePickerModal(workspace);

        if (workspace.FocusedBlockIndex < 0 || workspace.FocusedBlockIndex >= document.Blocks.Count)
        {
            _isOpen = false;
            return;
        }

        var block = document.Blocks[workspace.FocusedBlockIndex];
        string text = DocumentRenderer.GetEditBufferText();

        // Open on "/" at start of empty paragraph
        if (text == "/" && block.Type == DocBlockType.Paragraph && !_isOpen)
        {
            _isOpen = true;
            _selectedIndex = 0;
            _filterText = "";
        }

        // Track filter text after "/"
        if (_isOpen && text.StartsWith("/"))
        {
            _filterText = text.Length > 1 ? text[1..] : "";
        }

        // Close if text no longer starts with "/"
        if (_isOpen && !text.StartsWith("/"))
        {
            _isOpen = false;
        }

        if (!_isOpen) return;

        // Filter commands
        _filteredCount = 0;
        for (int i = 0; i < AllCommands.Length; i++)
        {
            if (string.IsNullOrEmpty(_filterText) ||
                AllCommands[i].Label.Contains(_filterText, StringComparison.OrdinalIgnoreCase))
            {
                _filteredIndices[_filteredCount++] = i;
            }
        }

        if (_filteredCount == 0)
        {
            _isOpen = false;
            return;
        }

        _selectedIndex = Math.Clamp(_selectedIndex, 0, _filteredCount - 1);

        // Arrow key navigation
        if (input.KeyDown)
        {
            _selectedIndex = (_selectedIndex + 1) % _filteredCount;
        }
        if (input.KeyUp)
        {
            _selectedIndex = (_selectedIndex - 1 + _filteredCount) % _filteredCount;
        }

        // Escape closes
        if (input.KeyEscape)
        {
            _isOpen = false;
            return;
        }

        // Enter selects
        if (input.KeyEnter)
        {
            var selected = AllCommands[_filteredIndices[_selectedIndex]];
            ApplySlashCommand(workspace, document, block, selected);
            _isOpen = false;
            return;
        }

        // Compute menu position — below the focused block
        var style = Im.Style;
        float itemHeight = 28f;
        float categoryHeight = 24f;
        float menuWidth = 260f;
        float menuHeight = 8f;
        string previousCategory = "";
        for (int filteredIndex = 0; filteredIndex < _filteredCount; filteredIndex++)
        {
            var command = AllCommands[_filteredIndices[filteredIndex]];
            if (!string.Equals(previousCategory, command.Category, StringComparison.Ordinal))
            {
                menuHeight += categoryHeight;
                previousCategory = command.Category;
            }

            menuHeight += itemHeight;
        }
        menuHeight += 6f;

        float blockScreenY = DocumentRenderer.GetBlockScreenY(workspace.FocusedBlockIndex);
        float blockH = DocumentRenderer.GetBlockHeight(workspace.FocusedBlockIndex);
        float menuY = blockScreenY + blockH;

        // Clamp so menu doesn't go below viewport
        float viewportBottom = Im.WindowContentRect.Y + Im.WindowContentRect.Height;
        if (menuY + menuHeight > viewportBottom)
        {
            menuY = blockScreenY - menuHeight - 4f;
        }

        // Update menu rect for click suppression
        MenuRect = new ImRect(menuX, menuY, menuWidth, menuHeight);

        // Draw menu popup
        Im.SetDrawLayer(ImDrawLayer.Overlay);

        Im.DrawRoundedRect(menuX, menuY, menuWidth, menuHeight, 8f, style.Surface);
        Im.DrawRoundedRectStroke(menuX, menuY, menuWidth, menuHeight, 8f, style.Border, 1f);

        var mousePos = Im.MousePos;
        float currentY = menuY + 6f;
        previousCategory = "";
        for (int filteredIndex = 0; filteredIndex < _filteredCount; filteredIndex++)
        {
            var command = AllCommands[_filteredIndices[filteredIndex]];
            if (!string.Equals(previousCategory, command.Category, StringComparison.Ordinal))
            {
                Im.Text(command.Category.AsSpan(), menuX + 12f, currentY + 4f, style.FontSize, ImStyle.WithAlpha(style.TextSecondary, 180));
                currentY += categoryHeight;
                previousCategory = command.Category;
            }

            var itemRect = new ImRect(menuX + 6f, currentY, menuWidth - 12f, itemHeight);
            bool itemHovered = itemRect.Contains(mousePos);

            // Mouse hover → update selection
            if (itemHovered)
            {
                _selectedIndex = filteredIndex;
            }

            // Mouse click → select
            if (itemHovered && input.MousePressed)
            {
                var selected = AllCommands[_filteredIndices[filteredIndex]];
                ApplySlashCommand(workspace, document, block, selected);
                _isOpen = false;
                Im.SetDrawLayer(ImDrawLayer.WindowContent);
                return;
            }

            bool isSelected = filteredIndex == _selectedIndex;

            if (isSelected)
            {
                Im.DrawRoundedRect(itemRect.X, itemRect.Y, itemRect.Width, itemRect.Height, 4f, style.Hover);
            }

            Im.Text(command.Label.AsSpan(), itemRect.X + 10f, itemRect.Y + (itemHeight - style.FontSize) * 0.5f,
                style.FontSize, isSelected ? 0xFFFFFFFF : style.TextPrimary);

            currentY += itemHeight;
        }

        Im.SetDrawLayer(ImDrawLayer.WindowContent);
    }

    private static void ApplySlashCommand(
        DocWorkspace workspace,
        DocDocument document,
        DocBlock block,
        SlashCommandDefinition command)
    {
        if (command.Kind == SlashCommandKind.TablePicker)
        {
            OpenTablePicker(workspace, document, block);
            return;
        }

        ApplyBlockTypeSlashCommand(workspace, document, block, command.BlockType);
    }

    private static void ApplyBlockTypeSlashCommand(
        DocWorkspace workspace,
        DocDocument document,
        DocBlock block,
        DocBlockType newType)
    {
        // Commit and clear the "/" text
        DocumentRenderer.CommitFocusedBlock(workspace, document);

        // Change block type
        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.ChangeBlockType,
            DocumentId = document.Id,
            BlockId = block.Id,
            OldBlockType = block.Type,
            NewBlockType = newType,
        });

        // Clear text (remove "/" prefix)
        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetBlockText,
            DocumentId = document.Id,
            BlockId = block.Id,
            OldBlockText = block.Text.PlainText,
            NewBlockText = "",
        });

        // Divider is non-editable — create a new Paragraph below and focus it
        if (newType == DocBlockType.Divider)
        {
            DocumentRenderer.AddNewBlockAfter(workspace, document, workspace.FocusedBlockIndex);
        }
        else
        {
            // Re-focus
            DocumentRenderer.FocusBlock(workspace, document, workspace.FocusedBlockIndex, 0);
        }
    }

    private static void OpenTablePicker(DocWorkspace workspace, DocDocument document, DocBlock block)
    {
        DocumentRenderer.CommitFocusedBlock(workspace, document);

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetBlockText,
            DocumentId = document.Id,
            BlockId = block.Id,
            OldBlockText = block.Text.PlainText,
            NewBlockText = "",
        });

        _pendingTableDocumentId = document.Id;
        _pendingTableBlockId = block.Id;
        _tableSearchLength = 0;
        _tableSearchBuffer[0] = '\0';
        _tablePickerScrollY = 0f;
        _tablePickerSelectedTableIndex = workspace.Project.Tables.Count > 0 ? 0 : -1;
        ImModal.Open(TablePickerModalId);
    }

    private static void DrawTablePickerModal(DocWorkspace workspace)
    {
        if (!ImModal.IsOpen(TablePickerModalId))
        {
            return;
        }

        if (!ImModal.Begin(TablePickerModalId, TablePickerWidth, TablePickerHeight, "New table"))
        {
            if (!ImModal.IsOpen(TablePickerModalId))
            {
                ClearTablePickerState();
            }

            return;
        }

        var style = Im.Style;
        float left = ImModal.ContentOffset.X;
        float top = ImModal.ContentOffset.Y;
        float innerWidth = TablePickerWidth - style.Padding * 2f;

        float buttonHeight = 40f;
        float buttonGap = 12f;
        float createButtonWidth = (innerWidth - buttonGap) * 0.5f;
        if (Im.Button($"{AddIcon}  Start blank", left, top, createButtonWidth, buttonHeight))
        {
            var createdTable = CreateNewTable(workspace);
            LinkPendingBlockToTable(workspace, createdTable.Id);
            ImModal.Close();
        }

        if (Im.Button($"{ImportIcon}  Import data", left + createButtonWidth + buttonGap, top, createButtonWidth, buttonHeight))
        {
            var createdTable = CreateNewTable(workspace);
            LinkPendingBlockToTable(workspace, createdTable.Id);
            ImModal.Close();
        }

        float sectionY = top + buttonHeight + 14f;
        Im.Text("Or connect to a table".AsSpan(), left, sectionY, style.FontSize, style.TextPrimary);

        float searchY = sectionY + style.FontSize + 10f;
        ImSearchBox.DrawAt(
            "doc_table_picker_search",
            _tableSearchBuffer,
            ref _tableSearchLength,
            _tableSearchBuffer.Length,
            left,
            searchY,
            innerWidth,
            "Search tables to connect to");

        int filteredCount = BuildFilteredTableIndexList(workspace, _tableSearchBuffer.AsSpan(0, _tableSearchLength));
        if (_tablePickerSelectedTableIndex >= filteredCount)
        {
            _tablePickerSelectedTableIndex = filteredCount - 1;
        }

        float listY = searchY + style.MinButtonHeight + 10f;
        float listHeight = 228f;
        var listRect = new ImRect(left, listY, innerWidth, listHeight);
        Im.DrawRoundedRect(listRect.X, listRect.Y, listRect.Width, listRect.Height, 7f, BlendColor(style.Surface, 0.2f, style.Background));
        Im.DrawRoundedRectStroke(listRect.X, listRect.Y, listRect.Width, listRect.Height, 7f, style.Border, 1f);

        float contentHeight = filteredCount * 34f + 8f;
        float contentY = ImScrollView.Begin(listRect, contentHeight, ref _tablePickerScrollY, handleMouseWheel: true);

        var input = Im.Context.Input;
        var mousePos = Im.MousePos;
        for (int filteredIndex = 0; filteredIndex < filteredCount; filteredIndex++)
        {
            int tableIndex = _filteredTableIndices[filteredIndex];
            var table = workspace.Project.Tables[tableIndex];

            float rowY = contentY + 4f + filteredIndex * 34f;
            var rowRect = new ImRect(listRect.X + 4f, rowY, listRect.Width - 8f, 30f);
            bool hovered = rowRect.Contains(mousePos);
            bool selected = filteredIndex == _tablePickerSelectedTableIndex;

            uint rowColor = selected
                ? BlendColor(style.Primary, 0.22f, style.Surface)
                : hovered
                    ? BlendColor(style.Hover, 0.2f, style.Surface)
                    : 0x00000000;
            if (rowColor != 0x00000000)
            {
                Im.DrawRoundedRect(rowRect.X, rowRect.Y, rowRect.Width, rowRect.Height, 5f, rowColor);
            }

            string tableLabel = $"{TableIcon}  {table.Name}";
            Im.Text(tableLabel.AsSpan(), rowRect.X + 10f, rowRect.Y + 6f, style.FontSize, style.TextPrimary);

            string rowCountText = table.Rows.Count.ToString();
            float rowCountWidth = Im.MeasureTextWidth(rowCountText.AsSpan(), style.FontSize);
            Im.Text(
                rowCountText.AsSpan(),
                rowRect.X + rowRect.Width - rowCountWidth - 10f,
                rowRect.Y + 6f,
                style.FontSize,
                style.TextSecondary);

            if (hovered && input.MousePressed)
            {
                _tablePickerSelectedTableIndex = filteredIndex;
            }
        }

        int scrollbarId = Im.Context.GetId("doc_table_picker_scroll");
        var scrollbarRect = new ImRect(listRect.X + listRect.Width - 8f, listRect.Y, 8f, listRect.Height);
        ImScrollView.End(scrollbarId, scrollbarRect, listRect.Height, contentHeight, ref _tablePickerScrollY);

        float footerY = listRect.Y + listRect.Height + 12f;
        float actionButtonWidth = 110f;
        float actionButtonHeight = 34f;
        bool canConnect = _tablePickerSelectedTableIndex >= 0 && _tablePickerSelectedTableIndex < filteredCount;
        if (canConnect && Im.Button("Connect", left, footerY, actionButtonWidth, actionButtonHeight))
        {
            int tableIndex = _filteredTableIndices[_tablePickerSelectedTableIndex];
            LinkPendingBlockToTable(workspace, workspace.Project.Tables[tableIndex].Id);
            ImModal.Close();
        }

        if (Im.Button("Cancel", left + actionButtonWidth + 10f, footerY, actionButtonWidth, actionButtonHeight))
        {
            ImModal.Close();
        }

        ImModal.End();

        if (!ImModal.IsOpen(TablePickerModalId))
        {
            ClearTablePickerState();
        }
    }

    private static int BuildFilteredTableIndexList(DocWorkspace workspace, ReadOnlySpan<char> filter)
    {
        int filteredCount = 0;
        for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count && filteredCount < _filteredTableIndices.Length; tableIndex++)
        {
            var table = workspace.Project.Tables[tableIndex];
            if (filter.Length > 0 && !table.Name.AsSpan().Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _filteredTableIndices[filteredCount++] = tableIndex;
        }

        return filteredCount;
    }

    private static DocTable CreateNewTable(DocWorkspace workspace)
    {
        int nextTableNumber = workspace.Project.Tables.Count + 1;
        string tableName = $"Table {nextTableNumber}";
        while (workspace.Project.Tables.Any(table => string.Equals(table.Name, tableName, StringComparison.OrdinalIgnoreCase)))
        {
            nextTableNumber++;
            tableName = $"Table {nextTableNumber}";
        }

        string baseFileName = $"table{nextTableNumber}";
        string fileName = baseFileName;
        int fileSuffix = 2;
        while (workspace.Project.Tables.Any(table => string.Equals(table.FileName, fileName, StringComparison.OrdinalIgnoreCase)))
        {
            fileName = $"{baseFileName}_{fileSuffix}";
            fileSuffix++;
        }

        var newTable = new DocTable
        {
            Name = tableName,
            FileName = fileName,
        };

        for (int columnIndex = 0; columnIndex < 4; columnIndex++)
        {
            newTable.Columns.Add(new DocColumn
            {
                Name = $"Column {columnIndex + 1}",
                Kind = DocColumnKind.Text,
                Width = 170f,
            });
        }

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.AddTable,
            TableIndex = workspace.Project.Tables.Count,
            TableSnapshot = newTable
        });

        return newTable;
    }

    private static void LinkPendingBlockToTable(DocWorkspace workspace, string tableId)
    {
        if (string.IsNullOrEmpty(_pendingTableDocumentId) || string.IsNullOrEmpty(_pendingTableBlockId))
        {
            return;
        }

        var document = workspace.Project.Documents.Find(doc => string.Equals(doc.Id, _pendingTableDocumentId, StringComparison.Ordinal));
        if (document == null)
        {
            return;
        }

        int blockIndex = document.Blocks.FindIndex(block => string.Equals(block.Id, _pendingTableBlockId, StringComparison.Ordinal));
        if (blockIndex < 0)
        {
            return;
        }

        var block = document.Blocks[blockIndex];

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.ChangeBlockType,
            DocumentId = document.Id,
            BlockId = block.Id,
            OldBlockType = block.Type,
            NewBlockType = DocBlockType.Table,
        });

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetBlockTableReference,
            DocumentId = document.Id,
            BlockId = block.Id,
            OldTableId = block.TableId,
            NewTableId = tableId,
        });

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetBlockText,
            DocumentId = document.Id,
            BlockId = block.Id,
            OldBlockText = block.Text.PlainText,
            NewBlockText = "",
        });

        workspace.FocusedBlockIndex = blockIndex;
    }

    private static void ClearTablePickerState()
    {
        _pendingTableDocumentId = "";
        _pendingTableBlockId = "";
        _tableSearchLength = 0;
        _tablePickerScrollY = 0f;
        _tablePickerSelectedTableIndex = -1;
    }

    private static uint BlendColor(uint tint, float factor, uint background)
    {
        byte tintR = (byte)(tint & 0xFF);
        byte tintG = (byte)((tint >> 8) & 0xFF);
        byte tintB = (byte)((tint >> 16) & 0xFF);
        byte backgroundR = (byte)(background & 0xFF);
        byte backgroundG = (byte)((background >> 8) & 0xFF);
        byte backgroundB = (byte)((background >> 16) & 0xFF);
        byte resultR = (byte)(backgroundR + (tintR - backgroundR) * factor);
        byte resultG = (byte)(backgroundG + (tintG - backgroundG) * factor);
        byte resultB = (byte)(backgroundB + (tintB - backgroundB) * factor);
        return 0xFF000000u | ((uint)resultB << 16) | ((uint)resultG << 8) | resultR;
    }
}
