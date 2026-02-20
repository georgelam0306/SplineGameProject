using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Widgets;
using Derp.Doc.Commands;
using Derp.Doc.Editor;
using Derp.Doc.Model;
using Derp.Doc.Tables;
using FontAwesome.Sharp;

namespace Derp.Doc.Panels;

internal static class SidebarPanel
{
    private readonly struct SidebarNodeRef
    {
        public SidebarNodeRef(ContextItemKind kind, string id)
        {
            Kind = kind;
            Id = id;
        }

        public ContextItemKind Kind { get; }
        public string Id { get; }
    }

    private enum ContextItemKind
    {
        None,
        Table,
        Document,
        Folder,
    }

    private enum DragItemKind
    {
        None,
        Table,
        Document,
        Folder,
    }

    private const float SectionHeaderHeight = 26f;
    private const float SectionSpacing = 6f;
    private const float DragThreshold = 4f;
    private const float TreeHorizontalPadding = 4f;
    private const string SystemsFolderExpandedProjectSettingKey = "sidebar.systemsFolderExpanded";

    private static readonly string TableIcon = ((char)IconChar.Table).ToString();
    private static readonly string FileIcon = ((char)IconChar.FileAlt).ToString();
    private static readonly string FolderIcon = ((char)IconChar.Folder).ToString();
    private static readonly string FolderPlusIcon = ((char)IconChar.FolderPlus).ToString();
    private static readonly string PlusIcon = ((char)IconChar.Plus).ToString();
    private static readonly string SchemaLinkIcon = ((char)IconChar.Link).ToString();

    private static readonly string NewTableLabel = TableIcon + "  New table";
    private static readonly string NewDerivedLabel = ((char)IconChar.LayerGroup).ToString() + "  New derived table";
    private static readonly string NewSchemaLinkedLabel = SchemaLinkIcon + "  New schema-linked table";
    private static readonly string NewDocumentLabel = FileIcon + "  New document";
    private static readonly string NewFolderLabel = FolderPlusIcon + "  New folder";

    private static readonly string AddTablesMenuId = "sidebar_tables_add_menu";
    private static readonly string ContextMenuId = "sidebar_item_menu";

    private static bool _documentsExpanded = true;
    private static bool _tablesExpanded = true;
    private static ImRect _documentsHeaderRect;
    private static ImRect _tablesHeaderRect;

    private static ContextItemKind _contextItemKind;
    private static int _contextItemIndex;
    private static string _contextItemId = "";
    private static DocFolderScope _contextFolderScope = DocFolderScope.Tables;

    private static readonly HashSet<string> _selectedTableIds = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _selectedDocumentIds = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _selectedTableFolderIds = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _selectedDocumentFolderIds = new(StringComparer.Ordinal);
    private static readonly List<SidebarNodeRef> _tableSelectionOrder = new(256);
    private static readonly List<SidebarNodeRef> _documentSelectionOrder = new(256);
    private static readonly List<SidebarNodeRef> _tableSelectionOrderNext = new(256);
    private static readonly List<SidebarNodeRef> _documentSelectionOrderNext = new(256);
    private static bool _tableAnchorSet;
    private static SidebarNodeRef _tableSelectionAnchor;
    private static bool _documentAnchorSet;
    private static SidebarNodeRef _documentSelectionAnchor;

    private static readonly char[] _renameBuffer = new char[128];
    private static int _renameBufferLength;
    private static ContextItemKind _renameTargetKind;
    private static string _renameTargetId = "";
    private static bool _renameActive;
    private static bool _renameNeedsFocus;
    private static bool _renameSelectAll;
    private const string RenameInputId = "sidebar_rename_input";

    private static DragItemKind _dragItemKind;
    private static string _dragItemId = "";
    private static DocFolderScope _dragScope = DocFolderScope.Tables;
    private static bool _dragPending;
    private static bool _dragActive;
    private static Vector2 _dragStartMousePos;

    private static bool _dropTargetIsSet;
    private static bool _dropTargetIsRoot;
    private static string? _dropTargetFolderId;
    private static DocFolderScope _dropTargetScope = DocFolderScope.Tables;
    private static bool _reorderDropIsSet;
    private static ContextItemKind _reorderDropKind = ContextItemKind.None;
    private static DocFolderScope _reorderDropScope = DocFolderScope.Tables;
    private static string _reorderDropTargetItemId = "";
    private static bool _reorderDropInsertAfter;

    private static bool _deferredSingleSelectionPending;
    private static ContextItemKind _deferredSingleSelectionKind = ContextItemKind.None;
    private static DocFolderScope _deferredSingleSelectionScope = DocFolderScope.Tables;
    private static string _deferredSingleSelectionItemId = "";

    public static void Draw(DocWorkspace workspace)
    {
        var contentRect = Im.WindowContentRect;
        float y = contentRect.Y + 4f;
        BeginSelectionOrderCapture();

        ResetDropTargetState();

        DrawDocumentsHeader(workspace, contentRect, ref y);
        if (_documentsExpanded)
        {
            DrawDocumentsTree(workspace, contentRect, ref y);
        }
        y += SectionSpacing;

        DrawTablesHeader(workspace, contentRect, ref y);
        if (_tablesExpanded)
        {
            DrawTablesTree(workspace, contentRect, ref y);
        }

        UpdateDragState(workspace);
        DrawContextMenu(workspace);
        CommitSelectionOrderCapture();
    }

    private static void DrawDocumentsHeader(DocWorkspace workspace, ImRect contentRect, ref float y)
    {
        float x = contentRect.X;
        float width = contentRect.Width;
        _documentsHeaderRect = new ImRect(x, y, width, SectionHeaderHeight);

        DrawSectionHeaderBackground(_documentsHeaderRect);

        float actionsWidth = 64f;
        float labelWidth = width - actionsWidth;
        if (labelWidth < 80f)
        {
            labelWidth = width;
        }

        int chevronId = Im.Context.GetId("sidebar_docs_header_chevron");
        var chevronRect = new ImRect(x + 4f, y + 4f, 18f, SectionHeaderHeight - 8f);
        bool chevronHovered = chevronRect.Contains(Im.MousePos);
        if (chevronHovered)
        {
            Im.Context.SetHot(chevronId);
        }

        if (chevronHovered && Im.Context.Input.MousePressed)
        {
            Im.Context.SetActive(chevronId);
        }

        if (Im.Context.IsActive(chevronId) && Im.Context.Input.MouseReleased)
        {
            if (chevronHovered)
            {
                _documentsExpanded = !_documentsExpanded;
            }

            Im.Context.ClearActive();
        }

        float chevronSize = 10f;
        float chevronX = chevronRect.X + (chevronRect.Width - chevronSize) * 0.5f;
        float chevronY = chevronRect.Y + (chevronRect.Height - chevronSize) * 0.5f;
        ImIcons.DrawChevron(
            chevronX,
            chevronY,
            chevronSize,
            _documentsExpanded ? ImIcons.ChevronDirection.Down : ImIcons.ChevronDirection.Right,
            Im.Style.TextSecondary);

        Im.Text("Documents".AsSpan(), x + 26f, y + (SectionHeaderHeight - Im.Style.FontSize) * 0.5f, Im.Style.FontSize, Im.Style.TextPrimary);

        float buttonW = 28f;
        float buttonH = SectionHeaderHeight - 4f;
        float buttonY = y + 2f;

        float addDocumentX = x + width - buttonW - 2f;
        float addFolderX = addDocumentX - buttonW - 2f;

        if (Im.Button(PlusIcon + FileIcon + "##sidebar_add_document_root", addDocumentX, buttonY, buttonW, buttonH))
        {
            CreateDocument(workspace, folderId: null);
        }

        if (Im.Button(FolderPlusIcon + "##sidebar_add_document_folder_root", addFolderX, buttonY, buttonW, buttonH))
        {
            CreateFolder(workspace, DocFolderScope.Documents, parentFolderId: null);
        }

        ConsiderRootDropTarget(DocFolderScope.Documents, _documentsHeaderRect);

        y += SectionHeaderHeight;
    }

    private static void DrawTablesHeader(DocWorkspace workspace, ImRect contentRect, ref float y)
    {
        float x = contentRect.X;
        float width = contentRect.Width;
        _tablesHeaderRect = new ImRect(x, y, width, SectionHeaderHeight);

        DrawSectionHeaderBackground(_tablesHeaderRect);

        float labelWidth = width - 128f;
        if (labelWidth < 80f)
        {
            labelWidth = width;
        }

        int chevronId = Im.Context.GetId("sidebar_tables_header_chevron");
        var chevronRect = new ImRect(x + 4f, y + 4f, 18f, SectionHeaderHeight - 8f);
        bool chevronHovered = chevronRect.Contains(Im.MousePos);
        if (chevronHovered)
        {
            Im.Context.SetHot(chevronId);
        }

        if (chevronHovered && Im.Context.Input.MousePressed)
        {
            Im.Context.SetActive(chevronId);
        }

        if (Im.Context.IsActive(chevronId) && Im.Context.Input.MouseReleased)
        {
            if (chevronHovered)
            {
                _tablesExpanded = !_tablesExpanded;
            }

            Im.Context.ClearActive();
        }

        float chevronSize = 10f;
        float chevronX = chevronRect.X + (chevronRect.Width - chevronSize) * 0.5f;
        float chevronY = chevronRect.Y + (chevronRect.Height - chevronSize) * 0.5f;
        ImIcons.DrawChevron(
            chevronX,
            chevronY,
            chevronSize,
            _tablesExpanded ? ImIcons.ChevronDirection.Down : ImIcons.ChevronDirection.Right,
            Im.Style.TextSecondary);

        Im.Text("Tables".AsSpan(), x + 26f, y + (SectionHeaderHeight - Im.Style.FontSize) * 0.5f, Im.Style.FontSize, Im.Style.TextPrimary);

        float buttonW = 28f;
        float buttonH = SectionHeaderHeight - 4f;
        float buttonY = y + 2f;

        float splitDropdownW = 22f;
        float splitW = buttonH + splitDropdownW;
        float splitX = x + width - splitW - 2f;
        float addFolderX = splitX - buttonW - 2f;

        if (Im.Button(FolderPlusIcon + "##sidebar_add_table_folder_root", addFolderX, buttonY, buttonW, buttonH))
        {
            CreateFolder(workspace, DocFolderScope.Tables, parentFolderId: null);
        }

        var splitResult = ImSplitDropdownButton.Draw(
            TableIcon + "##sidebar_add_table_root",
            splitX,
            buttonY,
            splitW,
            buttonH,
            dropdownWidth: splitDropdownW);
        if (splitResult == ImSplitDropdownButtonResult.Primary)
        {
            CreateTable(workspace, folderId: null, isDerived: false);
        }
        else if (splitResult == ImSplitDropdownButtonResult.Dropdown)
        {
            ImContextMenu.OpenAt(AddTablesMenuId, splitX, buttonY + buttonH);
        }

        if (ImContextMenu.Begin(AddTablesMenuId))
        {
            if (ImContextMenu.Item(NewTableLabel))
            {
                CreateTable(workspace, folderId: null, isDerived: false);
            }

            if (ImContextMenu.Item(NewDerivedLabel))
            {
                CreateTable(workspace, folderId: null, isDerived: true);
            }

            ImContextMenu.End();
        }

        ConsiderRootDropTarget(DocFolderScope.Tables, _tablesHeaderRect);

        y += SectionHeaderHeight;
    }

    private static void DrawDocumentsTree(DocWorkspace workspace, ImRect contentRect, ref float y)
    {
        if (workspace.ActiveView == ActiveViewKind.Document && workspace.ActiveDocument != null)
        {
            ImTree.SetSelected(workspace.ActiveDocument.Id.GetHashCode());
        }

        uint originalActiveColor = Im.Style.Active;
        Im.Style.Active = GetSidebarSelectionFillColor();
        ImTree.Begin(
            "sidebar_documents_tree",
            contentRect.X + TreeHorizontalPadding,
            y,
            MathF.Max(0f, contentRect.Width - TreeHorizontalPadding * 2f));
        DrawDocumentFoldersAndEntries(workspace, parentFolderId: null, contentRect, depth: 0);
        float treeHeight = ImTree.GetTreeHeight();
        ImTree.End();
        Im.Style.Active = originalActiveColor;

        y += treeHeight;
    }

    private static void DrawTablesTree(DocWorkspace workspace, ImRect contentRect, ref float y)
    {
        if (workspace.ActiveTable != null)
        {
            ImTree.SetSelected(workspace.ActiveTable.Id.GetHashCode());
        }

        uint originalActiveColor = Im.Style.Active;
        Im.Style.Active = GetSidebarSelectionFillColor();
        ImTree.Begin(
            "sidebar_tables_tree",
            contentRect.X + TreeHorizontalPadding,
            y,
            MathF.Max(0f, contentRect.Width - TreeHorizontalPadding * 2f));
        DrawSystemTablesVirtualGroup(workspace);
        DrawTableFoldersAndEntries(workspace, parentFolderId: null, contentRect, depth: 0);
        float treeHeight = ImTree.GetTreeHeight();
        ImTree.End();
        Im.Style.Active = originalActiveColor;

        y += treeHeight;
    }

    private static uint GetSidebarSelectionFillColor()
    {
        return Im.Style.Surface;
    }

    private static void DrawSystemTablesVirtualGroup(DocWorkspace workspace)
    {
        var systemTableEntries = new List<(DocTable Table, int TableIndex)>();
        for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
        {
            DocTable table = workspace.Project.Tables[tableIndex];
            if (!table.IsSystemTable || table.IsSubtable)
            {
                continue;
            }

            systemTableEntries.Add((table, tableIndex));
        }

        if (systemTableEntries.Count <= 0)
        {
            return;
        }

        systemTableEntries.Sort(static (leftEntry, rightEntry) =>
        {
            int leftOrder = GetSystemTableSortOrder(leftEntry.Table.SystemKey);
            int rightOrder = GetSystemTableSortOrder(rightEntry.Table.SystemKey);
            int byOrder = leftOrder.CompareTo(rightOrder);
            if (byOrder != 0)
            {
                return byOrder;
            }

            return string.Compare(leftEntry.Table.Name, rightEntry.Table.Name, StringComparison.OrdinalIgnoreCase);
        });

        int nodeId = "sidebar_system_tables_group".GetHashCode();
        bool persistedExpanded = GetSystemsVirtualGroupExpanded(workspace);
        ImTree.SetExpanded(nodeId, persistedExpanded);
        bool expanded = ImTree.BeginNodeIconText(nodeId, FolderIcon.AsSpan(), "Systems".AsSpan(), defaultOpen: true, iconTextGap: 8f);
        if (expanded != persistedExpanded)
        {
            SetSystemsVirtualGroupExpanded(workspace, expanded);
        }

        if (!expanded)
        {
            return;
        }

        for (int entryIndex = 0; entryIndex < systemTableEntries.Count; entryIndex++)
        {
            (DocTable table, int tableIndex) = systemTableEntries[entryIndex];
            DrawSidebarTableEntry(workspace, table, tableIndex, depth: 1);
        }

        ImTree.EndNode();
    }

    private static bool GetSystemsVirtualGroupExpanded(DocWorkspace workspace)
    {
        if (!workspace.TryGetProjectPluginSetting(SystemsFolderExpandedProjectSettingKey, out string persistedValue))
        {
            return true;
        }

        return !bool.TryParse(persistedValue, out bool expanded) || expanded;
    }

    private static void SetSystemsVirtualGroupExpanded(DocWorkspace workspace, bool expanded)
    {
        _ = workspace.SetProjectPluginSetting(
            SystemsFolderExpandedProjectSettingKey,
            expanded ? "true" : "false");
    }

    private static void DrawDocumentFoldersAndEntries(DocWorkspace workspace, string? parentFolderId, ImRect contentRect, int depth)
    {
        if (depth > 10)
        {
            return;
        }

        for (int folderIndex = 0; folderIndex < workspace.Project.Folders.Count; folderIndex++)
        {
            var folder = workspace.Project.Folders[folderIndex];
            if (folder.Scope != DocFolderScope.Documents)
            {
                continue;
            }

            if (!MatchesParentFolder(folder.ParentFolderId, parentFolderId))
            {
                continue;
            }

            DrawDocumentFolderNode(workspace, folder, folderIndex, contentRect, depth);
        }

        for (int documentIndex = 0; documentIndex < workspace.Project.Documents.Count; documentIndex++)
        {
            var document = workspace.Project.Documents[documentIndex];
            if (!IsDocumentInFolderLevel(workspace.Project, document, parentFolderId))
            {
                continue;
            }

            DrawSidebarDocumentEntry(workspace, document, documentIndex, depth);
        }
    }

    private static void DrawTableFoldersAndEntries(DocWorkspace workspace, string? parentFolderId, ImRect contentRect, int depth)
    {
        if (depth > 10)
        {
            return;
        }

        for (int folderIndex = 0; folderIndex < workspace.Project.Folders.Count; folderIndex++)
        {
            var folder = workspace.Project.Folders[folderIndex];
            if (folder.Scope != DocFolderScope.Tables)
            {
                continue;
            }

            if (!MatchesParentFolder(folder.ParentFolderId, parentFolderId))
            {
                continue;
            }

            DrawTableFolderNode(workspace, folder, folderIndex, contentRect, depth);
        }

        for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
        {
            var table = workspace.Project.Tables[tableIndex];
            if (table.IsSubtable)
            {
                continue;
            }

            if (table.IsSystemTable)
            {
                continue;
            }

            if (!IsTableInFolderLevel(workspace.Project, table, parentFolderId))
            {
                continue;
            }

            DrawSidebarTableEntry(workspace, table, tableIndex, depth);
        }
    }

    private static void DrawDocumentFolderNode(DocWorkspace workspace, DocFolder folder, int folderIndex, ImRect contentRect, int depth)
    {
        RecordSelectionOrderNode(DocFolderScope.Documents, ContextItemKind.Folder, folder.Id);

        bool isRenaming = _renameActive &&
            _renameTargetKind == ContextItemKind.Folder &&
            string.Equals(_renameTargetId, folder.Id, StringComparison.Ordinal);

        int nodeId = folder.Id.GetHashCode();
        bool persistedExpanded = workspace.GetFolderExpanded(folder.Scope, folder.Id, defaultExpanded: true);
        ImTree.SetExpanded(nodeId, persistedExpanded);
        bool expanded;
        if (isRenaming)
        {
            expanded = ImTree.BeginNodeIconText(nodeId, FolderIcon.AsSpan(), ReadOnlySpan<char>.Empty, defaultOpen: true, iconTextGap: 8f);
            DrawInlineRename(workspace, ImTree.LastNodeRect, GetNodeIconLabelX(ImTree.LastNodeRect, depth, FolderIcon, iconTextGap: 8f));
        }
        else
        {
            expanded = ImTree.BeginNodeIconText(nodeId, FolderIcon.AsSpan(), folder.Name.AsSpan(), defaultOpen: true, iconTextGap: 8f);
        }
        if (expanded != persistedExpanded)
        {
            workspace.SetFolderExpanded(folder.Scope, folder.Id, expanded);
        }

        bool leftClicked = ImTree.LastNodeRect.Contains(Im.MousePos) &&
                           Im.Context.Input.MousePressed &&
                           !Im.Context.Input.MouseRightPressed;
        bool doubleClicked = leftClicked && Im.Context.Input.IsDoubleClick;
        if (leftClicked)
        {
            HandleFolderLeftClick(folder, forceSingleSelection: doubleClicked);
        }

        if (doubleClicked && !isRenaming)
        {
            BeginInlineRenameForItem(workspace, ContextItemKind.Folder, folder.Scope, folder.Id, folderIndex);
        }

        RegisterDragCandidate(DragItemKind.Folder, folder.Id, folder.Scope, ImTree.LastNodeRect);
        DrawSelectedOutline(ContextItemKind.Folder, folder.Scope, folder.Id, ImTree.LastNodeRect);
        ConsiderFolderDropTarget(workspace, folder, ImTree.LastNodeRect);
        ConsiderReorderDropTarget(workspace, ContextItemKind.Folder, folder.Scope, folder.Id, ImTree.LastNodeRect);
        DrawDropHighlightForFolder(folder);
        DrawReorderDropHighlight(ContextItemKind.Folder, folder.Scope, folder.Id, ImTree.LastNodeRect);

        if (!isRenaming && ImTree.LastNodeRightClicked)
        {
            EnsureContextItemSelection(ContextItemKind.Folder, folder.Scope, folder.Id);
            _contextItemKind = ContextItemKind.Folder;
            _contextItemIndex = folderIndex;
            _contextItemId = folder.Id;
            _contextFolderScope = folder.Scope;
            ImContextMenu.Open(ContextMenuId);
        }

        if (expanded)
        {
            DrawDocumentFoldersAndEntries(workspace, folder.Id, contentRect, depth + 1);
            ImTree.EndNode();
        }
    }

    private static void DrawTableFolderNode(DocWorkspace workspace, DocFolder folder, int folderIndex, ImRect contentRect, int depth)
    {
        RecordSelectionOrderNode(DocFolderScope.Tables, ContextItemKind.Folder, folder.Id);

        bool isRenaming = _renameActive &&
            _renameTargetKind == ContextItemKind.Folder &&
            string.Equals(_renameTargetId, folder.Id, StringComparison.Ordinal);

        int nodeId = folder.Id.GetHashCode();
        bool persistedExpanded = workspace.GetFolderExpanded(folder.Scope, folder.Id, defaultExpanded: true);
        ImTree.SetExpanded(nodeId, persistedExpanded);
        bool expanded;
        if (isRenaming)
        {
            expanded = ImTree.BeginNodeIconText(nodeId, FolderIcon.AsSpan(), ReadOnlySpan<char>.Empty, defaultOpen: true, iconTextGap: 8f);
            DrawInlineRename(workspace, ImTree.LastNodeRect, GetNodeIconLabelX(ImTree.LastNodeRect, depth, FolderIcon, iconTextGap: 8f));
        }
        else
        {
            expanded = ImTree.BeginNodeIconText(nodeId, FolderIcon.AsSpan(), folder.Name.AsSpan(), defaultOpen: true, iconTextGap: 8f);
        }
        if (expanded != persistedExpanded)
        {
            workspace.SetFolderExpanded(folder.Scope, folder.Id, expanded);
        }

        bool leftClicked = ImTree.LastNodeRect.Contains(Im.MousePos) &&
                           Im.Context.Input.MousePressed &&
                           !Im.Context.Input.MouseRightPressed;
        bool doubleClicked = leftClicked && Im.Context.Input.IsDoubleClick;
        if (leftClicked)
        {
            HandleFolderLeftClick(folder, forceSingleSelection: doubleClicked);
        }

        if (doubleClicked && !isRenaming)
        {
            BeginInlineRenameForItem(workspace, ContextItemKind.Folder, folder.Scope, folder.Id, folderIndex);
        }

        RegisterDragCandidate(DragItemKind.Folder, folder.Id, folder.Scope, ImTree.LastNodeRect);
        DrawSelectedOutline(ContextItemKind.Folder, folder.Scope, folder.Id, ImTree.LastNodeRect);
        ConsiderFolderDropTarget(workspace, folder, ImTree.LastNodeRect);
        ConsiderReorderDropTarget(workspace, ContextItemKind.Folder, folder.Scope, folder.Id, ImTree.LastNodeRect);
        DrawDropHighlightForFolder(folder);
        DrawReorderDropHighlight(ContextItemKind.Folder, folder.Scope, folder.Id, ImTree.LastNodeRect);

        if (!isRenaming && ImTree.LastNodeRightClicked)
        {
            EnsureContextItemSelection(ContextItemKind.Folder, folder.Scope, folder.Id);
            _contextItemKind = ContextItemKind.Folder;
            _contextItemIndex = folderIndex;
            _contextItemId = folder.Id;
            _contextFolderScope = folder.Scope;
            ImContextMenu.Open(ContextMenuId);
        }

        if (expanded)
        {
            DrawTableFoldersAndEntries(workspace, folder.Id, contentRect, depth + 1);
            ImTree.EndNode();
        }
    }

    private static void DrawSidebarDocumentEntry(DocWorkspace workspace, DocDocument document, int documentIndex, int depth)
    {
        RecordSelectionOrderNode(DocFolderScope.Documents, ContextItemKind.Document, document.Id);

        bool isRenaming = _renameActive &&
            _renameTargetKind == ContextItemKind.Document &&
            string.Equals(_renameTargetId, document.Id, StringComparison.Ordinal);

        int leafId = document.Id.GetHashCode();
        if (isRenaming)
        {
            ImTree.LeafIconText(leafId, ReadOnlySpan<char>.Empty, ReadOnlySpan<char>.Empty, iconTextGap: 0f);
            DrawInlineRename(workspace, ImTree.LastLeafRect, GetLeafLabelX(ImTree.LastLeafRect, depth, FileIcon, iconTextGap: 8f));
        }
        else
        {
            ImTree.LeafIconText(leafId, FileIcon.AsSpan(), document.Title.AsSpan(), iconTextGap: 8f);
            bool leftClicked = ImTree.LastLeafRect.Contains(Im.MousePos) &&
                               Im.Context.Input.MousePressed &&
                               !Im.Context.Input.MouseRightPressed;
            bool doubleClicked = leftClicked && Im.Context.Input.IsDoubleClick;
            if (leftClicked)
            {
                HandleDocumentLeftClick(workspace, document, forceSingleSelection: doubleClicked);
            }

            if (doubleClicked)
            {
                BeginInlineRenameForItem(workspace, ContextItemKind.Document, DocFolderScope.Documents, document.Id, documentIndex);
            }

            if (ImTree.LastLeafRightClicked)
            {
                EnsureContextItemSelection(ContextItemKind.Document, DocFolderScope.Documents, document.Id);
                _contextItemKind = ContextItemKind.Document;
                _contextItemIndex = documentIndex;
                _contextItemId = document.Id;
                ImContextMenu.Open(ContextMenuId);
            }
        }

        RegisterDragCandidate(DragItemKind.Document, document.Id, DocFolderScope.Documents, ImTree.LastLeafRect);
        DrawSelectedOutline(ContextItemKind.Document, DocFolderScope.Documents, document.Id, ImTree.LastLeafRect);
        ConsiderReorderDropTarget(workspace, ContextItemKind.Document, DocFolderScope.Documents, document.Id, ImTree.LastLeafRect);
        DrawReorderDropHighlight(ContextItemKind.Document, DocFolderScope.Documents, document.Id, ImTree.LastLeafRect);
    }

    private static bool HasChildSubtables(DocTable parent, List<DocTable> allTables)
    {
        for (int tableIndex = 0; tableIndex < allTables.Count; tableIndex++)
        {
            if (string.Equals(allTables[tableIndex].ParentTableId, parent.Id, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void DrawSidebarTableEntry(DocWorkspace workspace, DocTable table, int tableIndex, int depth)
    {
        if (depth > 10)
        {
            return;
        }

        RecordSelectionOrderNode(DocFolderScope.Tables, ContextItemKind.Table, table.Id);

        string icon = table.IsDerived ? ((char)IconChar.LayerGroup).ToString() : TableIcon;
        bool isRenaming = _renameActive &&
            _renameTargetKind == ContextItemKind.Table &&
            string.Equals(_renameTargetId, table.Id, StringComparison.Ordinal);

        bool hasChildren = HasChildSubtables(table, workspace.Project.Tables);
        int nodeId = table.Id.GetHashCode();
        bool tableSchemaLocked = IsTableSchemaLockedForSidebar(table);

        if (hasChildren)
        {
            bool expanded;
            if (isRenaming)
            {
                expanded = ImTree.BeginNodeIconText(nodeId, icon.AsSpan(), ReadOnlySpan<char>.Empty, defaultOpen: true, iconTextGap: 8f);
                DrawInlineRename(workspace, ImTree.LastNodeRect, GetNodeIconLabelX(ImTree.LastNodeRect, depth, icon, iconTextGap: 8f));
            }
            else
            {
                expanded = ImTree.BeginNodeIconText(nodeId, icon.AsSpan(), table.Name.AsSpan(), defaultOpen: true, iconTextGap: 8f);
            }

            if (!tableSchemaLocked)
            {
                RegisterDragCandidate(DragItemKind.Table, table.Id, DocFolderScope.Tables, ImTree.LastNodeRect);
            }
            DrawSelectedOutline(ContextItemKind.Table, DocFolderScope.Tables, table.Id, ImTree.LastNodeRect);
            if (!tableSchemaLocked)
            {
                ConsiderReorderDropTarget(workspace, ContextItemKind.Table, DocFolderScope.Tables, table.Id, ImTree.LastNodeRect);
                DrawReorderDropHighlight(ContextItemKind.Table, DocFolderScope.Tables, table.Id, ImTree.LastNodeRect);
            }
            DrawSchemaLinkedIndicator(workspace, table, ImTree.LastNodeRect);

            bool leftClicked = ImTree.LastNodeRect.Contains(Im.MousePos) &&
                               Im.Context.Input.MousePressed &&
                               !Im.Context.Input.MouseRightPressed;
            bool doubleClicked = leftClicked && Im.Context.Input.IsDoubleClick;
            if (leftClicked)
            {
                HandleTableLeftClick(workspace, table, forceSingleSelection: doubleClicked);
            }

            if (doubleClicked && !isRenaming && !tableSchemaLocked)
            {
                BeginInlineRenameForItem(workspace, ContextItemKind.Table, DocFolderScope.Tables, table.Id, tableIndex);
            }

            if (!isRenaming && ImTree.LastNodeRightClicked)
            {
                EnsureContextItemSelection(ContextItemKind.Table, DocFolderScope.Tables, table.Id);
                _contextItemKind = ContextItemKind.Table;
                _contextItemIndex = tableIndex;
                _contextItemId = table.Id;
                ImContextMenu.Open(ContextMenuId);
            }

            if (expanded)
            {
                for (int childTableIndex = 0; childTableIndex < workspace.Project.Tables.Count; childTableIndex++)
                {
                    var childTable = workspace.Project.Tables[childTableIndex];
                    if (!string.Equals(childTable.ParentTableId, table.Id, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    DrawSidebarTableEntry(workspace, childTable, childTableIndex, depth + 1);
                }

                ImTree.EndNode();
            }
        }
        else
        {
            if (isRenaming)
            {
                ImTree.LeafIconText(nodeId, ReadOnlySpan<char>.Empty, ReadOnlySpan<char>.Empty, 0f);
                DrawInlineRename(workspace, ImTree.LastLeafRect, GetLeafLabelX(ImTree.LastLeafRect, depth, icon, iconTextGap: 8f));
            }
            else
            {
                ImTree.LeafIconText(nodeId, icon.AsSpan(), table.Name.AsSpan(), iconTextGap: 8f);
                bool leftClicked = ImTree.LastLeafRect.Contains(Im.MousePos) &&
                                   Im.Context.Input.MousePressed &&
                                   !Im.Context.Input.MouseRightPressed;
                bool doubleClicked = leftClicked && Im.Context.Input.IsDoubleClick;
                if (leftClicked)
                {
                    HandleTableLeftClick(workspace, table, forceSingleSelection: doubleClicked);
                }

                if (doubleClicked && !tableSchemaLocked)
                {
                    BeginInlineRenameForItem(workspace, ContextItemKind.Table, DocFolderScope.Tables, table.Id, tableIndex);
                }

                if (ImTree.LastLeafRightClicked)
                {
                    EnsureContextItemSelection(ContextItemKind.Table, DocFolderScope.Tables, table.Id);
                    _contextItemKind = ContextItemKind.Table;
                    _contextItemIndex = tableIndex;
                    _contextItemId = table.Id;
                    ImContextMenu.Open(ContextMenuId);
                }
            }

            if (!tableSchemaLocked)
            {
                RegisterDragCandidate(DragItemKind.Table, table.Id, DocFolderScope.Tables, ImTree.LastLeafRect);
            }
            DrawSelectedOutline(ContextItemKind.Table, DocFolderScope.Tables, table.Id, ImTree.LastLeafRect);
            if (!tableSchemaLocked)
            {
                ConsiderReorderDropTarget(workspace, ContextItemKind.Table, DocFolderScope.Tables, table.Id, ImTree.LastLeafRect);
                DrawReorderDropHighlight(ContextItemKind.Table, DocFolderScope.Tables, table.Id, ImTree.LastLeafRect);
            }
            DrawSchemaLinkedIndicator(workspace, table, ImTree.LastLeafRect);
        }
    }

    private static bool IsTableSchemaLockedForSidebar(DocTable table)
    {
        return table.IsSystemTable || DocWorkspace.IsPluginSchemaLockedTable(table);
    }

    private static void DrawSchemaLinkedIndicator(DocWorkspace workspace, DocTable table, ImRect rowRect)
    {
        if (!table.IsSchemaLinked)
        {
            return;
        }

        float iconFontSize = Im.Style.FontSize - 2f;
        float iconWidth = Im.MeasureTextWidth(SchemaLinkIcon.AsSpan(), iconFontSize);
        float iconX = rowRect.X + rowRect.Width - iconWidth - 8f;
        float iconY = rowRect.Y + (rowRect.Height - iconFontSize) * 0.5f;
        Im.Text(SchemaLinkIcon.AsSpan(), iconX, iconY, iconFontSize, ImStyle.WithAlpha(Im.Style.TextSecondary, 190));

        int tooltipId = table.Id.GetHashCode() ^ 0x52A1;
        bool hovered = rowRect.Contains(Im.MousePos);
        ImTooltip.Begin(tooltipId, hovered);
        if (!ImTooltip.ShouldShow(tooltipId))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(table.SchemaSourceTableId) &&
            TryFindTable(workspace.Project, table.SchemaSourceTableId, out DocTable sourceTable))
        {
            ImTooltip.Draw("Schema-linked to " + sourceTable.Name);
            return;
        }

        ImTooltip.Draw("Schema-linked (source table missing)");
    }

    private static void HandleTableLeftClick(DocWorkspace workspace, DocTable table, bool forceSingleSelection = false)
    {
        if (!forceSingleSelection &&
            TryBeginDeferredSingleSelection(ContextItemKind.Table, DocFolderScope.Tables, table.Id))
        {
            workspace.ContentTabs.OpenOrFocusTableFromSidebar(table.Id);
            return;
        }

        UpdateSelectionForLeftClick(ContextItemKind.Table, DocFolderScope.Tables, table.Id, forceSingleSelection);
        workspace.ContentTabs.OpenOrFocusTableFromSidebar(table.Id);
    }

    private static void HandleDocumentLeftClick(DocWorkspace workspace, DocDocument document, bool forceSingleSelection = false)
    {
        if (!forceSingleSelection &&
            TryBeginDeferredSingleSelection(ContextItemKind.Document, DocFolderScope.Documents, document.Id))
        {
            workspace.ContentTabs.OpenOrFocusDocumentFromSidebar(document.Id);
            return;
        }

        UpdateSelectionForLeftClick(ContextItemKind.Document, DocFolderScope.Documents, document.Id, forceSingleSelection);
        workspace.ContentTabs.OpenOrFocusDocumentFromSidebar(document.Id);
    }

    private static void HandleFolderLeftClick(DocFolder folder, bool forceSingleSelection = false)
    {
        if (!forceSingleSelection &&
            TryBeginDeferredSingleSelection(ContextItemKind.Folder, folder.Scope, folder.Id))
        {
            return;
        }

        UpdateSelectionForLeftClick(ContextItemKind.Folder, folder.Scope, folder.Id, forceSingleSelection);
    }

    private static bool TryBeginDeferredSingleSelection(ContextItemKind kind, DocFolderScope scope, string itemId)
    {
        if (Im.Context.Input.KeyCtrl || Im.Context.Input.KeyShift)
        {
            return false;
        }

        if (!Im.Context.Input.MousePressed)
        {
            return false;
        }

        if (!IsItemSelected(kind, scope, itemId))
        {
            return false;
        }

        if (GetSelectionCountForScope(scope) <= 1)
        {
            return false;
        }

        _deferredSingleSelectionPending = true;
        _deferredSingleSelectionKind = kind;
        _deferredSingleSelectionScope = scope;
        _deferredSingleSelectionItemId = itemId;
        return true;
    }

    private static void UpdateSelectionForLeftClick(ContextItemKind kind, DocFolderScope scope, string itemId, bool forceSingleSelection = false)
    {
        if (forceSingleSelection)
        {
            CancelDeferredSingleSelection();
            ClearAllSelections();
            SelectItem(kind, scope, itemId);
            SetSelectionAnchor(scope, kind, itemId);
            return;
        }

        bool shiftSelection = Im.Context.Input.KeyShift;
        bool toggleSelection = Im.Context.Input.KeyCtrl;

        if (shiftSelection && TryApplyShiftRangeSelection(kind, scope, itemId, additive: toggleSelection))
        {
            SetSelectionAnchor(scope, kind, itemId);
            return;
        }

        if (!toggleSelection)
        {
            ClearAllSelections();
            SelectItem(kind, scope, itemId);
            SetSelectionAnchor(scope, kind, itemId);
            return;
        }

        ToggleItemSelection(kind, scope, itemId);
        SetSelectionAnchor(scope, kind, itemId);
    }

    private static void EnsureContextItemSelection(ContextItemKind kind, DocFolderScope scope, string itemId)
    {
        if (IsItemSelected(kind, scope, itemId))
        {
            return;
        }

        ClearAllSelections();
        SelectItem(kind, scope, itemId);
        SetSelectionAnchor(scope, kind, itemId);
    }

    private static bool IsItemSelected(ContextItemKind kind, DocFolderScope scope, string itemId)
    {
        if (kind == ContextItemKind.Table)
        {
            return _selectedTableIds.Contains(itemId);
        }

        if (kind == ContextItemKind.Document)
        {
            return _selectedDocumentIds.Contains(itemId);
        }

        if (kind == ContextItemKind.Folder)
        {
            return scope == DocFolderScope.Tables
                ? _selectedTableFolderIds.Contains(itemId)
                : _selectedDocumentFolderIds.Contains(itemId);
        }

        return false;
    }

    private static void DrawSelectedOutline(ContextItemKind kind, DocFolderScope scope, string itemId, ImRect rowRect)
    {
        if (!IsItemSelected(kind, scope, itemId))
        {
            return;
        }

        float selectionX = rowRect.X + 1f;
        float selectionY = rowRect.Y + 1f;
        float selectionWidth = rowRect.Width - 2f;
        float selectionHeight = rowRect.Height - 2f;
        if (selectionWidth <= 0f || selectionHeight <= 0f)
        {
            return;
        }

        uint fillColor = ImStyle.WithAlpha(Im.Style.Primary, 52);
        uint borderColor = ImStyle.WithAlpha(Im.Style.Primary, 230);

        var viewport = Im.Context.CurrentViewport;
        if (viewport != null)
        {
            var drawList = viewport.CurrentDrawList;
            int previousSortKey = drawList.GetSortKey();
            int backgroundSortKey = previousSortKey - 64;
            if (backgroundSortKey > previousSortKey)
            {
                backgroundSortKey = int.MinValue + 1024;
            }

            drawList.SetSortKey(backgroundSortKey);
            Im.DrawRoundedRect(selectionX, selectionY, selectionWidth, selectionHeight, 3f, fillColor);
            drawList.SetSortKey(previousSortKey);
        }
        else
        {
            Im.DrawRoundedRect(selectionX, selectionY, selectionWidth, selectionHeight, 3f, fillColor);
        }

        Im.DrawRoundedRectStroke(selectionX, selectionY, selectionWidth, selectionHeight, 3f, borderColor, 1f);
        Im.DrawRect(selectionX + 1f, selectionY + 2f, 2f, MathF.Max(0f, selectionHeight - 4f), borderColor);
    }

    private static void ToggleItemSelection(ContextItemKind kind, DocFolderScope scope, string itemId)
    {
        if (kind == ContextItemKind.Table)
        {
            if (!_selectedTableIds.Remove(itemId))
            {
                _selectedTableIds.Add(itemId);
            }
            return;
        }

        if (kind == ContextItemKind.Document)
        {
            if (!_selectedDocumentIds.Remove(itemId))
            {
                _selectedDocumentIds.Add(itemId);
            }
            return;
        }

        if (kind != ContextItemKind.Folder)
        {
            return;
        }

        if (scope == DocFolderScope.Tables)
        {
            if (!_selectedTableFolderIds.Remove(itemId))
            {
                _selectedTableFolderIds.Add(itemId);
            }
            return;
        }

        if (!_selectedDocumentFolderIds.Remove(itemId))
        {
            _selectedDocumentFolderIds.Add(itemId);
        }
    }

    private static void SelectItem(ContextItemKind kind, DocFolderScope scope, string itemId)
    {
        if (kind == ContextItemKind.Table)
        {
            _selectedTableIds.Add(itemId);
            return;
        }

        if (kind == ContextItemKind.Document)
        {
            _selectedDocumentIds.Add(itemId);
            return;
        }

        if (kind != ContextItemKind.Folder)
        {
            return;
        }

        if (scope == DocFolderScope.Tables)
        {
            _selectedTableFolderIds.Add(itemId);
            return;
        }

        _selectedDocumentFolderIds.Add(itemId);
    }

    private static void ClearAllSelections()
    {
        _selectedTableIds.Clear();
        _selectedDocumentIds.Clear();
        _selectedTableFolderIds.Clear();
        _selectedDocumentFolderIds.Clear();
        _tableAnchorSet = false;
        _documentAnchorSet = false;
    }

    private static int GetSelectionCountForScope(DocFolderScope scope)
    {
        if (scope == DocFolderScope.Tables)
        {
            return _selectedTableIds.Count + _selectedTableFolderIds.Count;
        }

        return _selectedDocumentIds.Count + _selectedDocumentFolderIds.Count;
    }

    private static void DrawContextMenu(DocWorkspace workspace)
    {
        if (!ImContextMenu.Begin(ContextMenuId))
        {
            return;
        }

        if (_contextItemKind == ContextItemKind.Folder)
        {
            if (ImContextMenu.Item(NewFolderLabel))
            {
                CreateFolder(workspace, _contextFolderScope, _contextItemId);
            }

            if (_contextFolderScope == DocFolderScope.Tables)
            {
                if (ImContextMenu.Item(NewTableLabel))
                {
                    CreateTable(workspace, folderId: _contextItemId, isDerived: false);
                }

                if (ImContextMenu.Item(NewDerivedLabel))
                {
                    CreateTable(workspace, folderId: _contextItemId, isDerived: true);
                }
            }
            else
            {
                if (ImContextMenu.Item(NewDocumentLabel))
                {
                    CreateDocument(workspace, folderId: _contextItemId);
                }
            }

            ImContextMenu.Separator();

            if (ImContextMenu.Item("Rename"))
            {
                BeginInlineRename(workspace);
            }

            if (ImContextMenu.Item("Delete"))
            {
                DeleteItem(workspace);
            }

            ImContextMenu.End();
            return;
        }

        if (_contextItemKind == ContextItemKind.Table &&
            TryFindTable(workspace.Project, _contextItemId, out DocTable openTable))
        {
            if (ImContextMenu.Item("Open"))
            {
                workspace.ContentTabs.OpenOrFocusTableFromSidebar(openTable.Id);
            }

            if (ImContextMenu.Item("Open in New Tab"))
            {
                workspace.ContentTabs.OpenTableInNewTabFromSidebar(openTable.Id);
            }

            ImContextMenu.Separator();
        }
        else if (_contextItemKind == ContextItemKind.Document &&
                 TryFindDocument(workspace.Project, _contextItemId, out DocDocument openDocument))
        {
            if (ImContextMenu.Item("Open"))
            {
                workspace.ContentTabs.OpenOrFocusDocumentFromSidebar(openDocument.Id);
            }

            if (ImContextMenu.Item("Open in New Tab"))
            {
                workspace.ContentTabs.OpenDocumentInNewTabFromSidebar(openDocument.Id);
            }

            ImContextMenu.Separator();
        }

        DocTable? contextTable = null;
        bool contextSystemTable = false;
        bool contextPluginLockedTable = false;
        if (_contextItemKind == ContextItemKind.Table &&
            TryFindTable(workspace.Project, _contextItemId, out DocTable foundTable))
        {
            contextTable = foundTable;
            contextSystemTable = foundTable.IsSystemTable;
            contextPluginLockedTable = DocWorkspace.IsPluginSchemaLockedTable(foundTable);
        }

        bool contextTableSchemaLocked = contextSystemTable || contextPluginLockedTable;

        if (!contextTableSchemaLocked && ImContextMenu.Item("Rename"))
        {
            BeginInlineRename(workspace);
        }

        if (!contextTableSchemaLocked && ImContextMenu.Item("Duplicate"))
        {
            DuplicateItem(workspace);
        }

        if (_contextItemKind == ContextItemKind.Table && contextTable != null && !contextTableSchemaLocked)
        {
            if (ImContextMenu.Item(NewSchemaLinkedLabel))
            {
                CreateSchemaLinkedTableFromSource(workspace, _contextItemId);
            }
        }

        if (contextSystemTable)
        {
            ImContextMenu.Item("System table (locked)");
            ImContextMenu.End();
            return;
        }

        if (contextPluginLockedTable)
        {
            ImContextMenu.Item("Plugin table (locked)");
            ImContextMenu.End();
            return;
        }

        ImContextMenu.Separator();

        if (ImContextMenu.Item("Delete"))
        {
            DeleteItem(workspace);
        }

        ImContextMenu.End();
    }

    private static void BeginInlineRename(DocWorkspace workspace)
    {
        _renameTargetKind = _contextItemKind;
        _renameTargetId = _contextItemId;
        _renameActive = true;
        _renameNeedsFocus = true;
        _renameSelectAll = true;

        string currentName = "";
        if (_renameTargetKind == ContextItemKind.Table)
        {
            for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
            {
                if (string.Equals(workspace.Project.Tables[tableIndex].Id, _renameTargetId, StringComparison.Ordinal))
                {
                    currentName = workspace.Project.Tables[tableIndex].Name;
                    break;
                }
            }
        }
        else if (_renameTargetKind == ContextItemKind.Document)
        {
            for (int documentIndex = 0; documentIndex < workspace.Project.Documents.Count; documentIndex++)
            {
                if (string.Equals(workspace.Project.Documents[documentIndex].Id, _renameTargetId, StringComparison.Ordinal))
                {
                    currentName = workspace.Project.Documents[documentIndex].Title;
                    break;
                }
            }
        }
        else if (_renameTargetKind == ContextItemKind.Folder)
        {
            for (int folderIndex = 0; folderIndex < workspace.Project.Folders.Count; folderIndex++)
            {
                if (string.Equals(workspace.Project.Folders[folderIndex].Id, _renameTargetId, StringComparison.Ordinal))
                {
                    currentName = workspace.Project.Folders[folderIndex].Name;
                    break;
                }
            }
        }

        int nameLength = Math.Min(currentName.Length, _renameBuffer.Length);
        currentName.AsSpan(0, nameLength).CopyTo(_renameBuffer);
        _renameBufferLength = nameLength;
    }

    private static void BeginInlineRenameForItem(
        DocWorkspace workspace,
        ContextItemKind kind,
        DocFolderScope scope,
        string itemId,
        int itemIndex)
    {
        if (_renameActive)
        {
            return;
        }

        EnsureContextItemSelection(kind, scope, itemId);
        _contextItemKind = kind;
        _contextItemIndex = itemIndex;
        _contextItemId = itemId;
        _contextFolderScope = scope;
        BeginInlineRename(workspace);
    }

    private static void DrawInlineRename(DocWorkspace workspace, ImRect rowRect, float textX)
    {
        float inputX = textX;
        float inputY = rowRect.Y;
        float inputW = MathF.Max(24f, rowRect.Right - inputX - 6f);
        float inputH = rowRect.Height;

        uint inputBackgroundColor = ImStyle.WithAlpha(Im.Style.Surface, 220);
        uint inputBorderColor = ImStyle.WithAlpha(Im.Style.Border, 180);
        Im.DrawRoundedRect(inputX - 2f, inputY, inputW + 4f, inputH, 4f, inputBackgroundColor);
        Im.DrawRoundedRectStroke(inputX - 2f, inputY, inputW + 4f, inputH, 4f, inputBorderColor, 1f);

        int widgetId = Im.Context.GetId(RenameInputId);
        if (_renameNeedsFocus)
        {
            ImTextArea.ClearState(widgetId);
            Im.Context.RequestFocus(widgetId);
            Im.Context.SetActive(widgetId);
            if (_renameSelectAll)
            {
                ImTextArea.SetState(widgetId, _renameBufferLength, 0, _renameBufferLength);
            }
            else
            {
                ImTextArea.SetState(widgetId, _renameBufferLength);
            }
            Im.Context.ResetCaretBlink();
            _renameNeedsFocus = false;
            _renameSelectAll = false;
        }

        ref var style = ref Im.Style;
        float savedPadding = style.Padding;
        style.Padding = 0f;

        _ = ImTextArea.DrawAt(
            RenameInputId,
            _renameBuffer,
            ref _renameBufferLength,
            _renameBuffer.Length,
            inputX,
            inputY,
            inputW,
            inputH,
            wordWrap: false,
            fontSizePx: Im.Style.FontSize,
            flags: ImTextArea.ImTextAreaFlags.NoBackground |
                   ImTextArea.ImTextAreaFlags.NoBorder |
                   ImTextArea.ImTextAreaFlags.NoRounding |
                   ImTextArea.ImTextAreaFlags.SingleLine,
            lineHeightPx: Im.Style.FontSize,
            letterSpacingPx: 0f,
            alignX: 0,
            alignY: 1,
            textColor: Im.Style.TextPrimary);

        style.Padding = savedPadding;

        bool renameFocused = Im.Context.IsFocused(widgetId);
        var input = Im.Context.Input;

        if (input.KeyEnter && renameFocused)
        {
            CommitInlineRename(workspace);
        }
        else if (input.KeyEscape && renameFocused)
        {
            CancelInlineRename();
        }
        else if (input.MousePressed && !renameFocused && !_renameNeedsFocus)
        {
            CommitInlineRename(workspace);
        }
    }

    private static float GetNodeLabelX(ImRect rowRect, int depth)
    {
        return rowRect.X + depth * ImTree.IndentWidth + ImTree.IconSize + ImTree.IconPadding * 2f;
    }

    private static float GetNodeIconLabelX(ImRect rowRect, int depth, string icon, float iconTextGap)
    {
        float labelStartX = GetNodeLabelX(rowRect, depth);
        float iconFontSize = Im.Style.FontSize - 1f;
        float iconWidth = icon.Length > 0 ? Im.MeasureTextWidth(icon.AsSpan(), iconFontSize) : 0f;
        return labelStartX + iconWidth + (icon.Length > 0 ? iconTextGap : 0f);
    }

    private static float GetLeafLabelX(ImRect rowRect, int depth, string icon, float iconTextGap)
    {
        float iconX = rowRect.X + depth * ImTree.IndentWidth + ImTree.IconPadding;
        float iconFontSize = Im.Style.FontSize - 1f;
        float iconWidth = icon.Length > 0 ? Im.MeasureTextWidth(icon.AsSpan(), iconFontSize) : 0f;
        return iconX + iconWidth + iconTextGap;
    }

    private static void CommitInlineRename(DocWorkspace workspace)
    {
        string newName = new string(_renameBuffer, 0, _renameBufferLength).Trim();
        if (string.IsNullOrEmpty(newName))
        {
            CancelInlineRename();
            return;
        }

        if (_renameTargetKind == ContextItemKind.Table)
        {
            for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
            {
                var table = workspace.Project.Tables[tableIndex];
                if (!string.Equals(table.Id, _renameTargetId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (IsTableSchemaLockedForSidebar(table))
                {
                    break;
                }

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

                break;
            }
        }
        else if (_renameTargetKind == ContextItemKind.Document)
        {
            for (int documentIndex = 0; documentIndex < workspace.Project.Documents.Count; documentIndex++)
            {
                var document = workspace.Project.Documents[documentIndex];
                if (!string.Equals(document.Id, _renameTargetId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.Equals(document.Title, newName, StringComparison.Ordinal))
                {
                    workspace.ExecuteCommand(new DocCommand
                    {
                        Kind = DocCommandKind.RenameDocument,
                        DocumentId = document.Id,
                        OldName = document.Title,
                        NewName = newName,
                    });
                }

                break;
            }
        }
        else if (_renameTargetKind == ContextItemKind.Folder)
        {
            for (int folderIndex = 0; folderIndex < workspace.Project.Folders.Count; folderIndex++)
            {
                var folder = workspace.Project.Folders[folderIndex];
                if (!string.Equals(folder.Id, _renameTargetId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.Equals(folder.Name, newName, StringComparison.Ordinal))
                {
                    workspace.ExecuteCommand(new DocCommand
                    {
                        Kind = DocCommandKind.RenameFolder,
                        FolderId = folder.Id,
                        OldName = folder.Name,
                        NewName = newName,
                    });
                }

                break;
            }
        }

        CancelInlineRename();
    }

    private static void CancelInlineRename()
    {
        Im.ClearTextInputState(RenameInputId);
        _renameActive = false;
        _renameNeedsFocus = false;
        _renameSelectAll = false;
        _renameTargetId = "";
        _renameBufferLength = 0;
    }

    private static void DeleteItem(DocWorkspace workspace)
    {
        if (_contextItemKind == ContextItemKind.Table)
        {
            for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
            {
                var table = workspace.Project.Tables[tableIndex];
                if (!string.Equals(table.Id, _contextItemId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (IsTableSchemaLockedForSidebar(table))
                {
                    return;
                }

                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.RemoveTable,
                    TableId = table.Id,
                    TableIndex = tableIndex,
                    TableSnapshot = table,
                });
                workspace.ValidateActiveTable();
                return;
            }

            return;
        }

        if (_contextItemKind == ContextItemKind.Document)
        {
            for (int documentIndex = 0; documentIndex < workspace.Project.Documents.Count; documentIndex++)
            {
                var document = workspace.Project.Documents[documentIndex];
                if (!string.Equals(document.Id, _contextItemId, StringComparison.Ordinal))
                {
                    continue;
                }

                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.RemoveDocument,
                    DocumentId = document.Id,
                    DocumentIndex = documentIndex,
                    DocumentSnapshot = document,
                });
                workspace.ValidateActiveDocument();
                return;
            }

            return;
        }

        if (_contextItemKind == ContextItemKind.Folder)
        {
            DeleteFolderRecursively(workspace, _contextItemId, _contextFolderScope);
        }
    }

    private static void DuplicateItem(DocWorkspace workspace)
    {
        if (_contextItemKind == ContextItemKind.Table)
        {
            for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
            {
                var table = workspace.Project.Tables[tableIndex];
                if (!string.Equals(table.Id, _contextItemId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (IsTableSchemaLockedForSidebar(table))
                {
                    return;
                }

                if (!TryBuildDuplicatedTableHierarchy(workspace.Project, table, out List<DocTable> duplicatedTables))
                {
                    workspace.SetStatusMessage("Failed to duplicate table hierarchy.");
                    return;
                }

                int insertIndex = tableIndex + 1;
                var addTableCommands = new List<DocCommand>(duplicatedTables.Count);
                for (int duplicatedTableIndex = 0; duplicatedTableIndex < duplicatedTables.Count; duplicatedTableIndex++)
                {
                    addTableCommands.Add(new DocCommand
                    {
                        Kind = DocCommandKind.AddTable,
                        TableIndex = insertIndex + duplicatedTableIndex,
                        TableSnapshot = duplicatedTables[duplicatedTableIndex],
                    });
                }

                workspace.ExecuteCommands(addTableCommands);
                workspace.ContentTabs.OpenTableInNewTabFromSidebar(duplicatedTables[0].Id);
                return;
            }
        }
        else if (_contextItemKind == ContextItemKind.Document)
        {
            for (int documentIndex = 0; documentIndex < workspace.Project.Documents.Count; documentIndex++)
            {
                var document = workspace.Project.Documents[documentIndex];
                if (!string.Equals(document.Id, _contextItemId, StringComparison.Ordinal))
                {
                    continue;
                }

                var newDocument = new DocDocument
                {
                    Title = document.Title + " (copy)",
                    FolderId = document.FolderId,
                    FileName = GenerateUniqueDocumentFileName(workspace.Project, document.FileName + "_copy"),
                };

                for (int blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
                {
                    newDocument.Blocks.Add(document.Blocks[blockIndex].Clone());
                }

                int insertIndex = documentIndex + 1;
                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.AddDocument,
                    DocumentIndex = insertIndex,
                    DocumentSnapshot = newDocument,
                });

                workspace.ContentTabs.OpenDocumentInNewTabFromSidebar(newDocument.Id);
                return;
            }
        }
    }

    private static bool TryBuildDuplicatedTableHierarchy(
        DocProject project,
        DocTable sourceRootTable,
        out List<DocTable> duplicatedTables)
    {
        duplicatedTables = new List<DocTable>();
        var sourceTableById = new Dictionary<string, DocTable>(project.Tables.Count, StringComparer.Ordinal);
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            sourceTableById[project.Tables[tableIndex].Id] = project.Tables[tableIndex];
        }

        var clonedTableBySourceId = new Dictionary<string, DocTable>(StringComparer.Ordinal);
        CloneTableHierarchyRecursive(
            sourceRootTable,
            renamedRoot: true,
            newParentTableId: null,
            sourceTableById,
            clonedTableBySourceId,
            duplicatedTables);

        var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            usedFileNames.Add(project.Tables[tableIndex].FileName);
        }

        for (int duplicatedTableIndex = 0; duplicatedTableIndex < duplicatedTables.Count; duplicatedTableIndex++)
        {
            DocTable duplicatedTable = duplicatedTables[duplicatedTableIndex];
            duplicatedTable.FileName = GenerateUniqueTableFileName(duplicatedTable.FileName, usedFileNames);
        }

        return duplicatedTables.Count > 0;
    }

    private static DocTable CloneTableHierarchyRecursive(
        DocTable sourceTable,
        bool renamedRoot,
        string? newParentTableId,
        Dictionary<string, DocTable> sourceTableById,
        Dictionary<string, DocTable> clonedTableBySourceId,
        List<DocTable> duplicatedTables)
    {
        if (clonedTableBySourceId.TryGetValue(sourceTable.Id, out DocTable? existingClone))
        {
            return existingClone;
        }

        var clonedTable = new DocTable
        {
            Name = renamedRoot ? sourceTable.Name + " (copy)" : sourceTable.Name + " copy",
            FolderId = sourceTable.FolderId,
            FileName = renamedRoot ? sourceTable.FileName + "_copy" : sourceTable.FileName + "_copy_subtable",
            SchemaSourceTableId = sourceTable.SchemaSourceTableId,
            InheritanceSourceTableId = sourceTable.InheritanceSourceTableId,
            SystemKey = sourceTable.SystemKey,
            IsSystemSchemaLocked = sourceTable.IsSystemSchemaLocked,
            IsSystemDataLocked = sourceTable.IsSystemDataLocked,
            DerivedConfig = sourceTable.DerivedConfig?.Clone(),
            ExportConfig = sourceTable.ExportConfig?.Clone(),
            Keys = sourceTable.Keys.Clone(),
            ParentTableId = newParentTableId ?? sourceTable.ParentTableId,
            ParentRowColumnId = sourceTable.ParentRowColumnId,
            PluginTableTypeId = sourceTable.PluginTableTypeId,
            PluginOwnerColumnTypeId = sourceTable.PluginOwnerColumnTypeId,
            IsPluginSchemaLocked = sourceTable.IsPluginSchemaLocked,
        };

        clonedTableBySourceId[sourceTable.Id] = clonedTable;
        duplicatedTables.Add(clonedTable);

        var sourceColumnById = new Dictionary<string, DocColumn>(sourceTable.Columns.Count, StringComparer.Ordinal);
        var clonedColumnBySourceId = new Dictionary<string, DocColumn>(sourceTable.Columns.Count, StringComparer.Ordinal);
        for (int columnIndex = 0; columnIndex < sourceTable.Columns.Count; columnIndex++)
        {
            DocColumn sourceColumn = sourceTable.Columns[columnIndex];
            sourceColumnById[sourceColumn.Id] = sourceColumn;

            var clonedColumn = CloneColumnForDuplicate(sourceColumn);
            clonedTable.Columns.Add(clonedColumn);
            clonedColumnBySourceId[sourceColumn.Id] = clonedColumn;
        }

        if (!string.IsNullOrWhiteSpace(sourceTable.ParentRowColumnId) &&
            clonedColumnBySourceId.TryGetValue(sourceTable.ParentRowColumnId, out DocColumn? clonedParentRowColumn))
        {
            clonedTable.ParentRowColumnId = clonedParentRowColumn.Id;
        }

        for (int rowIndex = 0; rowIndex < sourceTable.Rows.Count; rowIndex++)
        {
            DocRow sourceRow = sourceTable.Rows[rowIndex];
            var clonedRow = new DocRow();
            foreach (var cellEntry in sourceRow.Cells)
            {
                if (!clonedColumnBySourceId.TryGetValue(cellEntry.Key, out DocColumn? clonedColumn))
                {
                    continue;
                }

                clonedRow.SetCell(clonedColumn.Id, cellEntry.Value);
            }

            clonedTable.Rows.Add(clonedRow);
        }

        for (int viewIndex = 0; viewIndex < sourceTable.Views.Count; viewIndex++)
        {
            clonedTable.Views.Add(sourceTable.Views[viewIndex].Clone());
        }

        for (int variableIndex = 0; variableIndex < sourceTable.Variables.Count; variableIndex++)
        {
            clonedTable.Variables.Add(sourceTable.Variables[variableIndex].Clone());
        }

        for (int variantIndex = 0; variantIndex < sourceTable.Variants.Count; variantIndex++)
        {
            clonedTable.Variants.Add(sourceTable.Variants[variantIndex].Clone());
        }

        for (int variantDeltaIndex = 0; variantDeltaIndex < sourceTable.VariantDeltas.Count; variantDeltaIndex++)
        {
            clonedTable.VariantDeltas.Add(sourceTable.VariantDeltas[variantDeltaIndex].Clone());
        }

        for (int sourceColumnIndex = 0; sourceColumnIndex < sourceTable.Columns.Count; sourceColumnIndex++)
        {
            DocColumn sourceColumn = sourceTable.Columns[sourceColumnIndex];
            if (sourceColumn.Kind != DocColumnKind.Subtable ||
                string.IsNullOrWhiteSpace(sourceColumn.SubtableId) ||
                !sourceTableById.TryGetValue(sourceColumn.SubtableId, out DocTable? sourceChildTable) ||
                !clonedColumnBySourceId.TryGetValue(sourceColumn.Id, out DocColumn? clonedColumn))
            {
                continue;
            }

            DocTable clonedChildTable = CloneTableHierarchyRecursive(
                sourceChildTable,
                renamedRoot: false,
                newParentTableId: clonedTable.Id,
                sourceTableById,
                clonedTableBySourceId,
                duplicatedTables);
            clonedColumn.SubtableId = clonedChildTable.Id;
        }

        return clonedTable;
    }

    private static DocColumn CloneColumnForDuplicate(DocColumn sourceColumn)
    {
        return new DocColumn
        {
            Name = sourceColumn.Name,
            Kind = sourceColumn.Kind,
            ColumnTypeId = sourceColumn.ColumnTypeId,
            PluginSettingsJson = sourceColumn.PluginSettingsJson,
            Width = sourceColumn.Width,
            Options = sourceColumn.Options != null ? new List<string>(sourceColumn.Options) : null,
            FormulaExpression = sourceColumn.FormulaExpression,
            RelationTableId = sourceColumn.RelationTableId,
            TableRefBaseTableId = sourceColumn.TableRefBaseTableId,
            RowRefTableRefColumnId = sourceColumn.RowRefTableRefColumnId,
            RelationTargetMode = sourceColumn.RelationTargetMode,
            RelationTableVariantId = sourceColumn.RelationTableVariantId,
            RelationDisplayColumnId = sourceColumn.RelationDisplayColumnId,
            IsHidden = sourceColumn.IsHidden,
            IsProjected = sourceColumn.IsProjected,
            IsInherited = sourceColumn.IsInherited,
            ExportType = sourceColumn.ExportType,
            NumberMin = sourceColumn.NumberMin,
            NumberMax = sourceColumn.NumberMax,
            ExportEnumName = sourceColumn.ExportEnumName,
            ExportIgnore = sourceColumn.ExportIgnore,
            SubtableId = sourceColumn.SubtableId,
            SubtableDisplayRendererId = sourceColumn.SubtableDisplayRendererId,
            SubtableDisplayCellWidth = sourceColumn.SubtableDisplayCellWidth,
            SubtableDisplayCellHeight = sourceColumn.SubtableDisplayCellHeight,
            SubtableDisplayPreviewQuality = sourceColumn.SubtableDisplayPreviewQuality,
            FormulaEvalScopes = sourceColumn.FormulaEvalScopes,
            ModelPreviewSettings = sourceColumn.ModelPreviewSettings?.Clone(),
        };
    }

    private static string GenerateUniqueTableFileName(string baseFileName, HashSet<string> usedFileNames)
    {
        string normalizedBaseName = string.IsNullOrWhiteSpace(baseFileName)
            ? "table_copy"
            : baseFileName.Trim();

        string candidate = normalizedBaseName;
        int suffix = 2;
        while (!usedFileNames.Add(candidate))
        {
            candidate = normalizedBaseName + "_" + suffix.ToString(CultureInfo.InvariantCulture);
            suffix++;
        }

        return candidate;
    }

    private static void DeleteFolderRecursively(DocWorkspace workspace, string rootFolderId, DocFolderScope scope)
    {
        var project = workspace.Project;
        var folderIds = new HashSet<string>(StringComparer.Ordinal);
        CollectFolderSubtree(project, rootFolderId, scope, folderIds);
        if (folderIds.Count == 0)
        {
            return;
        }

        var commands = new List<DocCommand>();
        if (scope == DocFolderScope.Tables)
        {
            var tableIds = new HashSet<string>(StringComparer.Ordinal);
            for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
            {
                var table = project.Tables[tableIndex];
                if (!string.IsNullOrWhiteSpace(table.FolderId) && folderIds.Contains(table.FolderId))
                {
                    tableIds.Add(table.Id);
                }
            }

            bool addedParentLinkedTable;
            do
            {
                addedParentLinkedTable = false;
                for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
                {
                    var table = project.Tables[tableIndex];
                    if (string.IsNullOrWhiteSpace(table.ParentTableId) || !tableIds.Contains(table.ParentTableId))
                    {
                        continue;
                    }

                    if (tableIds.Add(table.Id))
                    {
                        addedParentLinkedTable = true;
                    }
                }
            }
            while (addedParentLinkedTable);

            for (int tableIndex = project.Tables.Count - 1; tableIndex >= 0; tableIndex--)
            {
                var table = project.Tables[tableIndex];
                if (!tableIds.Contains(table.Id))
                {
                    continue;
                }

                commands.Add(new DocCommand
                {
                    Kind = DocCommandKind.RemoveTable,
                    TableId = table.Id,
                    TableIndex = tableIndex,
                    TableSnapshot = table,
                });
            }
        }
        else
        {
            for (int documentIndex = project.Documents.Count - 1; documentIndex >= 0; documentIndex--)
            {
                var document = project.Documents[documentIndex];
                if (string.IsNullOrWhiteSpace(document.FolderId) || !folderIds.Contains(document.FolderId))
                {
                    continue;
                }

                commands.Add(new DocCommand
                {
                    Kind = DocCommandKind.RemoveDocument,
                    DocumentId = document.Id,
                    DocumentIndex = documentIndex,
                    DocumentSnapshot = document,
                });
            }
        }

        for (int folderIndex = project.Folders.Count - 1; folderIndex >= 0; folderIndex--)
        {
            var folder = project.Folders[folderIndex];
            if (!folderIds.Contains(folder.Id))
            {
                continue;
            }

            commands.Add(new DocCommand
            {
                Kind = DocCommandKind.RemoveFolder,
                FolderId = folder.Id,
                FolderIndex = folderIndex,
                FolderSnapshot = folder,
            });
        }

        if (commands.Count == 0)
        {
            return;
        }

        workspace.ExecuteCommands(commands);
        workspace.ValidateActiveTable();
        workspace.ValidateActiveDocument();
    }

    private static void CollectFolderSubtree(DocProject project, string rootFolderId, DocFolderScope scope, HashSet<string> folderIds)
    {
        if (folderIds.Contains(rootFolderId))
        {
            return;
        }

        DocFolder? rootFolder = null;
        for (int folderIndex = 0; folderIndex < project.Folders.Count; folderIndex++)
        {
            var folder = project.Folders[folderIndex];
            if (string.Equals(folder.Id, rootFolderId, StringComparison.Ordinal))
            {
                rootFolder = folder;
                break;
            }
        }

        if (rootFolder == null || rootFolder.Scope != scope)
        {
            return;
        }

        folderIds.Add(rootFolderId);

        for (int folderIndex = 0; folderIndex < project.Folders.Count; folderIndex++)
        {
            var folder = project.Folders[folderIndex];
            if (folder.Scope != scope)
            {
                continue;
            }

            if (!string.Equals(folder.ParentFolderId, rootFolderId, StringComparison.Ordinal))
            {
                continue;
            }

            CollectFolderSubtree(project, folder.Id, scope, folderIds);
        }
    }

    private static void CreateFolder(DocWorkspace workspace, DocFolderScope scope, string? parentFolderId)
    {
        var folder = new DocFolder
        {
            Name = $"Folder {workspace.Project.Folders.Count + 1}",
            Scope = scope,
            ParentFolderId = string.IsNullOrWhiteSpace(parentFolderId) ? null : parentFolderId,
        };

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.AddFolder,
            FolderIndex = workspace.Project.Folders.Count,
            FolderSnapshot = folder,
        });
    }

    private static void CreateTable(DocWorkspace workspace, string? folderId, bool isDerived)
    {
        int count = workspace.Project.Tables.Count;
        var table = new DocTable
        {
            Name = isDerived ? $"Derived {count + 1}" : $"Table {count + 1}",
            FolderId = string.IsNullOrWhiteSpace(folderId) ? null : folderId,
            FileName = isDerived ? $"derived{count + 1}" : $"table{count + 1}",
            DerivedConfig = isDerived ? new DocDerivedConfig() : null,
        };

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.AddTable,
            TableIndex = count,
            TableSnapshot = table,
        });

        workspace.ContentTabs.OpenTableInNewTabFromSidebar(table.Id);
        if (isDerived)
        {
            workspace.ShowInspector = true;
        }
    }

    private static void CreateSchemaLinkedTableFromSource(DocWorkspace workspace, string sourceTableId)
    {
        if (!TryFindTable(workspace.Project, sourceTableId, out DocTable sourceTable))
        {
            return;
        }

        int tableCount = workspace.Project.Tables.Count;
        string baseName = sourceTable.Name + " Linked";
        string linkedTableName = baseName;
        for (int suffix = 2; DoesTableNameExist(workspace.Project, linkedTableName); suffix++)
        {
            linkedTableName = baseName + " " + suffix;
        }

        string baseFileName = sourceTable.FileName + "_linked";
        string linkedFileName = baseFileName;
        for (int suffix = 2; DoesTableFileNameExist(workspace.Project, linkedFileName); suffix++)
        {
            linkedFileName = baseFileName + "_" + suffix;
        }

        var table = new DocTable
        {
            Name = linkedTableName,
            FolderId = sourceTable.FolderId,
            FileName = linkedFileName,
            SchemaSourceTableId = sourceTable.Id,
        };

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.AddTable,
            TableIndex = tableCount,
            TableSnapshot = table,
        });

        workspace.ContentTabs.OpenTableInNewTabFromSidebar(table.Id);
        workspace.ShowInspector = true;
    }

    private static void CreateDocument(DocWorkspace workspace, string? folderId)
    {
        int count = workspace.Project.Documents.Count;
        string fileName = GenerateUniqueDocumentFileName(workspace.Project, $"document{count + 1}");
        var document = new DocDocument
        {
            Title = $"Document {count + 1}",
            FolderId = string.IsNullOrWhiteSpace(folderId) ? null : folderId,
            FileName = fileName,
        };

        document.Blocks.Add(new DocBlock
        {
            Type = DocBlockType.Paragraph,
            Order = "a0",
        });

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.AddDocument,
            DocumentIndex = count,
            DocumentSnapshot = document,
        });

        workspace.ContentTabs.OpenDocumentInNewTabFromSidebar(document.Id);
    }

    private static bool MatchesParentFolder(string? itemParentFolderId, string? targetParentFolderId)
    {
        if (string.IsNullOrWhiteSpace(itemParentFolderId))
        {
            return string.IsNullOrWhiteSpace(targetParentFolderId);
        }

        return string.Equals(itemParentFolderId, targetParentFolderId, StringComparison.Ordinal);
    }

    private static bool IsDocumentInFolderLevel(DocProject project, DocDocument document, string? parentFolderId)
    {
        if (string.IsNullOrWhiteSpace(parentFolderId))
        {
            if (string.IsNullOrWhiteSpace(document.FolderId))
            {
                return true;
            }

            if (!TryFindFolder(project, document.FolderId, out var folder))
            {
                return true;
            }

            return folder.Scope != DocFolderScope.Documents;
        }

        return string.Equals(document.FolderId, parentFolderId, StringComparison.Ordinal);
    }

    private static bool IsTableInFolderLevel(DocProject project, DocTable table, string? parentFolderId)
    {
        if (string.IsNullOrWhiteSpace(parentFolderId))
        {
            if (string.IsNullOrWhiteSpace(table.FolderId))
            {
                return true;
            }

            if (!TryFindFolder(project, table.FolderId, out var folder))
            {
                return true;
            }

            return folder.Scope != DocFolderScope.Tables;
        }

        return string.Equals(table.FolderId, parentFolderId, StringComparison.Ordinal);
    }

    private static bool TryFindFolder(DocProject project, string? folderId, out DocFolder folder)
    {
        folder = null!;
        if (string.IsNullOrWhiteSpace(folderId))
        {
            return false;
        }

        for (int folderIndex = 0; folderIndex < project.Folders.Count; folderIndex++)
        {
            var candidateFolder = project.Folders[folderIndex];
            if (string.Equals(candidateFolder.Id, folderId, StringComparison.Ordinal))
            {
                folder = candidateFolder;
                return true;
            }
        }

        return false;
    }

    private static bool DoesTableNameExist(DocProject project, string tableName)
    {
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            if (string.Equals(project.Tables[tableIndex].Name, tableName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DoesTableFileNameExist(DocProject project, string tableFileName)
    {
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            if (string.Equals(project.Tables[tableIndex].FileName, tableFileName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string GenerateUniqueDocumentFileName(DocProject project, string baseFileName)
    {
        string normalizedBaseName = string.IsNullOrWhiteSpace(baseFileName)
            ? "document"
            : baseFileName.Trim();

        string candidate = normalizedBaseName;
        int suffix = 2;
        while (DoesDocumentFileNameExist(project, candidate))
        {
            candidate = normalizedBaseName + "_" + suffix.ToString(CultureInfo.InvariantCulture);
            suffix++;
        }

        return candidate;
    }

    private static bool DoesDocumentFileNameExist(DocProject project, string documentFileName)
    {
        for (int documentIndex = 0; documentIndex < project.Documents.Count; documentIndex++)
        {
            if (string.Equals(project.Documents[documentIndex].FileName, documentFileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int GetSystemTableSortOrder(string? systemKey)
    {
        if (string.Equals(systemKey, DocSystemTableKeys.Assets, StringComparison.Ordinal))
        {
            return 0;
        }

        if (string.Equals(systemKey, DocSystemTableKeys.Packages, StringComparison.Ordinal))
        {
            return 1;
        }

        if (string.Equals(systemKey, DocSystemTableKeys.Exports, StringComparison.Ordinal))
        {
            return 2;
        }

        if (string.Equals(systemKey, DocSystemTableKeys.Textures, StringComparison.Ordinal))
        {
            return 3;
        }

        if (string.Equals(systemKey, DocSystemTableKeys.Models, StringComparison.Ordinal))
        {
            return 4;
        }

        if (string.Equals(systemKey, DocSystemTableKeys.Audio, StringComparison.Ordinal))
        {
            return 5;
        }

        if (string.Equals(systemKey, DocSystemTableKeys.Ui, StringComparison.Ordinal))
        {
            return 6;
        }

        if (string.Equals(systemKey, DocSystemTableKeys.Materials, StringComparison.Ordinal))
        {
            return 7;
        }

        if (string.Equals(systemKey, DocSystemTableKeys.AssetDependencies, StringComparison.Ordinal))
        {
            return 8;
        }

        return int.MaxValue;
    }

    private static void DrawSectionHeaderBackground(ImRect rect)
    {
        // No backer color: keep section headers flat and separated by a subtle line.
        Im.DrawLine(rect.X, rect.Bottom - 1f, rect.Right, rect.Bottom - 1f, 1f, Im.Style.Border);
    }

    private static void BeginSelectionOrderCapture()
    {
        _tableSelectionOrderNext.Clear();
        _documentSelectionOrderNext.Clear();
    }

    private static void CommitSelectionOrderCapture()
    {
        _tableSelectionOrder.Clear();
        _tableSelectionOrder.AddRange(_tableSelectionOrderNext);
        _documentSelectionOrder.Clear();
        _documentSelectionOrder.AddRange(_documentSelectionOrderNext);
    }

    private static void RecordSelectionOrderNode(DocFolderScope scope, ContextItemKind kind, string itemId)
    {
        if (scope == DocFolderScope.Tables)
        {
            _tableSelectionOrderNext.Add(new SidebarNodeRef(kind, itemId));
            return;
        }

        _documentSelectionOrderNext.Add(new SidebarNodeRef(kind, itemId));
    }

    private static void SetSelectionAnchor(DocFolderScope scope, ContextItemKind kind, string itemId)
    {
        if (scope == DocFolderScope.Tables)
        {
            _tableSelectionAnchor = new SidebarNodeRef(kind, itemId);
            _tableAnchorSet = true;
            return;
        }

        _documentSelectionAnchor = new SidebarNodeRef(kind, itemId);
        _documentAnchorSet = true;
    }

    private static bool TryGetSelectionAnchor(DocFolderScope scope, out SidebarNodeRef anchor)
    {
        if (scope == DocFolderScope.Tables)
        {
            if (_tableAnchorSet)
            {
                anchor = _tableSelectionAnchor;
                return true;
            }

            anchor = default;
            return false;
        }

        if (_documentAnchorSet)
        {
            anchor = _documentSelectionAnchor;
            return true;
        }

        anchor = default;
        return false;
    }

    private static bool TryApplyShiftRangeSelection(ContextItemKind kind, DocFolderScope scope, string itemId, bool additive)
    {
        if (!TryGetSelectionAnchor(scope, out SidebarNodeRef anchor))
        {
            return false;
        }

        List<SidebarNodeRef> selectionOrder = scope == DocFolderScope.Tables
            ? _tableSelectionOrder
            : _documentSelectionOrder;
        if (selectionOrder.Count == 0)
        {
            return false;
        }

        int anchorIndex = FindSelectionOrderIndex(selectionOrder, anchor.Kind, anchor.Id);
        int clickedIndex = FindSelectionOrderIndex(selectionOrder, kind, itemId);
        if (anchorIndex < 0 || clickedIndex < 0)
        {
            return false;
        }

        if (!additive)
        {
            ClearAllSelections();
        }

        int startIndex = Math.Min(anchorIndex, clickedIndex);
        int endIndex = Math.Max(anchorIndex, clickedIndex);
        for (int selectionIndex = startIndex; selectionIndex <= endIndex; selectionIndex++)
        {
            SidebarNodeRef nodeRef = selectionOrder[selectionIndex];
            SelectItem(nodeRef.Kind, scope, nodeRef.Id);
        }

        return true;
    }

    private static int FindSelectionOrderIndex(List<SidebarNodeRef> selectionOrder, ContextItemKind kind, string itemId)
    {
        for (int selectionIndex = 0; selectionIndex < selectionOrder.Count; selectionIndex++)
        {
            SidebarNodeRef nodeRef = selectionOrder[selectionIndex];
            if (nodeRef.Kind != kind)
            {
                continue;
            }

            if (string.Equals(nodeRef.Id, itemId, StringComparison.Ordinal))
            {
                return selectionIndex;
            }
        }

        return -1;
    }

    private static void RegisterDragCandidate(DragItemKind kind, string itemId, DocFolderScope scope, ImRect rowRect)
    {
        if (_renameActive)
        {
            return;
        }

        var input = Im.Context.Input;
        if (rowRect.Contains(Im.MousePos) && input.MousePressed)
        {
            _dragPending = true;
            _dragActive = false;
            _dragItemKind = kind;
            _dragItemId = itemId;
            _dragScope = scope;
            _dragStartMousePos = Im.MousePos;
            _dropTargetIsSet = false;
            _dropTargetIsRoot = false;
            _dropTargetFolderId = null;
            _reorderDropIsSet = false;
            _reorderDropKind = ContextItemKind.None;
            _reorderDropScope = scope;
            _reorderDropTargetItemId = "";
            _reorderDropInsertAfter = false;
        }
    }

    private static void ConsiderRootDropTarget(DocFolderScope scope, ImRect rect)
    {
        if (!_dragActive || _dragScope != scope)
        {
            return;
        }

        if (!rect.Contains(Im.MousePos))
        {
            return;
        }

        _dropTargetIsSet = true;
        _dropTargetIsRoot = true;
        _dropTargetFolderId = null;
        _dropTargetScope = scope;

        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, Im.Style.Primary, 2f);
    }

    private static void ConsiderFolderDropTarget(DocWorkspace workspace, DocFolder folder, ImRect rowRect)
    {
        if (!_dragActive || _dragScope != folder.Scope)
        {
            return;
        }

        if (!rowRect.Contains(Im.MousePos))
        {
            return;
        }

        if (_dragItemKind == DragItemKind.Folder)
        {
            if (string.Equals(_dragItemId, folder.Id, StringComparison.Ordinal))
            {
                return;
            }

            if (IsFolderDescendant(workspace.Project, _dragItemId, folder.Id))
            {
                return;
            }
        }

        if (IsUsingMultiItemDrag() &&
            IsItemSelected(ContextItemKind.Folder, folder.Scope, folder.Id))
        {
            return;
        }

        _dropTargetIsSet = true;
        _dropTargetIsRoot = false;
        _dropTargetFolderId = folder.Id;
        _dropTargetScope = folder.Scope;
    }

    private static void DrawDropHighlightForFolder(DocFolder folder)
    {
        if (!_dragActive || !_dropTargetIsSet || _dropTargetIsRoot)
        {
            return;
        }

        if (_dropTargetScope != folder.Scope)
        {
            return;
        }

        if (!string.Equals(_dropTargetFolderId, folder.Id, StringComparison.Ordinal))
        {
            return;
        }

        var rowRect = ImTree.LastNodeRect;
        Im.DrawRoundedRectStroke(rowRect.X, rowRect.Y, rowRect.Width, rowRect.Height, Im.Style.CornerRadius, Im.Style.Primary, 2f);
    }

    private static void ConsiderReorderDropTarget(
        DocWorkspace workspace,
        ContextItemKind kind,
        DocFolderScope scope,
        string itemId,
        ImRect rowRect)
    {
        if (!_dragActive || _dragScope != scope || !rowRect.Contains(Im.MousePos))
        {
            return;
        }

        if (IsUsingMultiItemDrag())
        {
            int selectedCountForDragKind = GetSelectedCountForKind(kind, scope);
            if (selectedCountForDragKind > 1)
            {
                return;
            }
        }

        ContextItemKind dragKind = GetContextItemKindForDragItem(_dragItemKind);
        if (dragKind == ContextItemKind.None || dragKind != kind)
        {
            return;
        }

        if (string.Equals(_dragItemId, itemId, StringComparison.Ordinal))
        {
            return;
        }

        bool insertAfter = Im.MousePos.Y >= rowRect.Y + rowRect.Height * 0.5f;
        if (kind == ContextItemKind.Folder)
        {
            float edgeThreshold = MathF.Min(10f, rowRect.Height * 0.45f);
            bool nearTop = Im.MousePos.Y <= rowRect.Y + edgeThreshold;
            bool nearBottom = Im.MousePos.Y >= rowRect.Bottom - edgeThreshold;
            if (!nearTop && !nearBottom)
            {
                return;
            }

            insertAfter = nearBottom;
        }

        if (!CanReorderDropTarget(workspace.Project, kind, scope, _dragItemId, itemId))
        {
            return;
        }

        _reorderDropIsSet = true;
        _reorderDropKind = kind;
        _reorderDropScope = scope;
        _reorderDropTargetItemId = itemId;
        _reorderDropInsertAfter = insertAfter;

        // Reorder drop takes precedence over "drop into folder" when both are possible.
        _dropTargetIsSet = false;
        _dropTargetIsRoot = false;
        _dropTargetFolderId = null;
    }

    private static void DrawReorderDropHighlight(ContextItemKind kind, DocFolderScope scope, string itemId, ImRect rowRect)
    {
        if (!_dragActive || !_reorderDropIsSet)
        {
            return;
        }

        if (_reorderDropKind != kind || _reorderDropScope != scope)
        {
            return;
        }

        if (!string.Equals(_reorderDropTargetItemId, itemId, StringComparison.Ordinal))
        {
            return;
        }

        float lineY = _reorderDropInsertAfter ? rowRect.Bottom - 1f : rowRect.Y + 1f;
        Im.DrawLine(rowRect.X + 6f, lineY, rowRect.Right - 6f, lineY, 2f, Im.Style.Primary);
    }

    private static ContextItemKind GetContextItemKindForDragItem(DragItemKind dragItemKind)
    {
        return dragItemKind switch
        {
            DragItemKind.Table => ContextItemKind.Table,
            DragItemKind.Document => ContextItemKind.Document,
            DragItemKind.Folder => ContextItemKind.Folder,
            _ => ContextItemKind.None,
        };
    }

    private static int GetSelectedCountForKind(ContextItemKind kind, DocFolderScope scope)
    {
        if (kind == ContextItemKind.Table)
        {
            return _selectedTableIds.Count;
        }

        if (kind == ContextItemKind.Document)
        {
            return _selectedDocumentIds.Count;
        }

        if (kind == ContextItemKind.Folder)
        {
            return scope == DocFolderScope.Tables
                ? _selectedTableFolderIds.Count
                : _selectedDocumentFolderIds.Count;
        }

        return 0;
    }

    private static bool CanReorderDropTarget(
        DocProject project,
        ContextItemKind kind,
        DocFolderScope scope,
        string sourceItemId,
        string targetItemId)
    {
        if (kind == ContextItemKind.Table)
        {
            if (!TryFindTable(project, sourceItemId, out var sourceTable) ||
                !TryFindTable(project, targetItemId, out var targetTable))
            {
                return false;
            }

            return CanReorderTableWithinGroup(project, sourceTable, targetTable);
        }

        if (kind == ContextItemKind.Document)
        {
            if (!TryFindDocument(project, sourceItemId, out var sourceDocument) ||
                !TryFindDocument(project, targetItemId, out var targetDocument))
            {
                return false;
            }

            return CanReorderDocumentWithinGroup(project, sourceDocument, targetDocument);
        }

        if (kind == ContextItemKind.Folder)
        {
            if (!TryFindFolder(project, sourceItemId, out var sourceFolder) ||
                !TryFindFolder(project, targetItemId, out var targetFolder))
            {
                return false;
            }

            if (sourceFolder.Scope != scope || targetFolder.Scope != scope)
            {
                return false;
            }

            return string.Equals(sourceFolder.ParentFolderId, targetFolder.ParentFolderId, StringComparison.Ordinal);
        }

        return false;
    }

    private static bool CanReorderTableWithinGroup(DocProject project, DocTable sourceTable, DocTable targetTable)
    {
        if (!string.Equals(sourceTable.ParentTableId, targetTable.ParentTableId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(sourceTable.ParentTableId))
        {
            return true;
        }

        string? sourceFolderId = GetEffectiveTableFolderId(project, sourceTable);
        string? targetFolderId = GetEffectiveTableFolderId(project, targetTable);
        return string.Equals(sourceFolderId, targetFolderId, StringComparison.Ordinal);
    }

    private static bool CanReorderDocumentWithinGroup(DocProject project, DocDocument sourceDocument, DocDocument targetDocument)
    {
        string? sourceFolderId = GetEffectiveDocumentFolderId(project, sourceDocument);
        string? targetFolderId = GetEffectiveDocumentFolderId(project, targetDocument);
        return string.Equals(sourceFolderId, targetFolderId, StringComparison.Ordinal);
    }

    private static string? GetEffectiveDocumentFolderId(DocProject project, DocDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.FolderId))
        {
            return null;
        }

        if (!TryFindFolder(project, document.FolderId, out var folder))
        {
            return null;
        }

        return folder.Scope == DocFolderScope.Documents ? folder.Id : null;
    }

    private static string? GetEffectiveTableFolderId(DocProject project, DocTable table)
    {
        if (string.IsNullOrWhiteSpace(table.FolderId))
        {
            return null;
        }

        if (!TryFindFolder(project, table.FolderId, out var folder))
        {
            return null;
        }

        return folder.Scope == DocFolderScope.Tables ? folder.Id : null;
    }

    private static bool TryFindTable(DocProject project, string tableId, out DocTable table)
    {
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            var candidateTable = project.Tables[tableIndex];
            if (string.Equals(candidateTable.Id, tableId, StringComparison.Ordinal))
            {
                table = candidateTable;
                return true;
            }
        }

        table = null!;
        return false;
    }

    private static bool TryFindDocument(DocProject project, string documentId, out DocDocument document)
    {
        for (int documentIndex = 0; documentIndex < project.Documents.Count; documentIndex++)
        {
            var candidateDocument = project.Documents[documentIndex];
            if (string.Equals(candidateDocument.Id, documentId, StringComparison.Ordinal))
            {
                document = candidateDocument;
                return true;
            }
        }

        document = null!;
        return false;
    }

    private static bool IsFolderDescendant(DocProject project, string ancestorFolderId, string candidateDescendantFolderId)
    {
        string? currentFolderId = candidateDescendantFolderId;
        while (!string.IsNullOrWhiteSpace(currentFolderId))
        {
            if (string.Equals(currentFolderId, ancestorFolderId, StringComparison.Ordinal))
            {
                return true;
            }

            string? parentFolderId = null;
            for (int folderIndex = 0; folderIndex < project.Folders.Count; folderIndex++)
            {
                var folder = project.Folders[folderIndex];
                if (string.Equals(folder.Id, currentFolderId, StringComparison.Ordinal))
                {
                    parentFolderId = folder.ParentFolderId;
                    break;
                }
            }

            currentFolderId = parentFolderId;
        }

        return false;
    }

    private static void UpdateDragState(DocWorkspace workspace)
    {
        var input = Im.Context.Input;
        if (!_dragPending)
        {
            return;
        }

        if (input.MouseDown)
        {
            if (!_dragActive)
            {
                Vector2 delta = Im.MousePos - _dragStartMousePos;
                float dragDistance = MathF.Abs(delta.X) + MathF.Abs(delta.Y);
                if (dragDistance >= DragThreshold)
                {
                    _dragActive = true;
                    CancelDeferredSingleSelection();
                }
            }

            return;
        }

        if (input.MouseReleased)
        {
            if (!_dragActive && _deferredSingleSelectionPending)
            {
                ApplyDeferredSingleSelection(workspace);
            }

            if (_dragActive && (_dropTargetIsSet || _reorderDropIsSet))
            {
                ApplyDrop(workspace);
            }

            ClearDragState();
            return;
        }

        if (!input.MouseDown)
        {
            ClearDragState();
        }
    }

    private static void ApplyDrop(DocWorkspace workspace)
    {
        if (_reorderDropIsSet)
        {
            ApplyReorderDrop(workspace);
            return;
        }

        string? targetFolderId = _dropTargetIsRoot ? null : _dropTargetFolderId;

        if (IsUsingMultiItemDrag())
        {
            ApplyMultiItemDrop(workspace, targetFolderId);
            return;
        }

        if (_dragItemKind == DragItemKind.Table)
        {
            for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
            {
                var table = workspace.Project.Tables[tableIndex];
                if (!string.Equals(table.Id, _dragItemId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(table.FolderId, targetFolderId, StringComparison.Ordinal))
                {
                    return;
                }

                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.SetTableFolder,
                    TableId = table.Id,
                    OldFolderId = table.FolderId,
                    NewFolderId = targetFolderId,
                });
                return;
            }
        }
        else if (_dragItemKind == DragItemKind.Document)
        {
            for (int documentIndex = 0; documentIndex < workspace.Project.Documents.Count; documentIndex++)
            {
                var document = workspace.Project.Documents[documentIndex];
                if (!string.Equals(document.Id, _dragItemId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(document.FolderId, targetFolderId, StringComparison.Ordinal))
                {
                    return;
                }

                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.SetDocumentFolder,
                    DocumentId = document.Id,
                    OldFolderId = document.FolderId,
                    NewFolderId = targetFolderId,
                });
                return;
            }
        }
        else if (_dragItemKind == DragItemKind.Folder)
        {
            for (int folderIndex = 0; folderIndex < workspace.Project.Folders.Count; folderIndex++)
            {
                var folder = workspace.Project.Folders[folderIndex];
                if (!string.Equals(folder.Id, _dragItemId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(folder.ParentFolderId, targetFolderId, StringComparison.Ordinal))
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(targetFolderId) &&
                    IsFolderDescendant(workspace.Project, folder.Id, targetFolderId))
                {
                    return;
                }

                workspace.ExecuteCommand(new DocCommand
                {
                    Kind = DocCommandKind.MoveFolder,
                    FolderId = folder.Id,
                    OldParentFolderId = folder.ParentFolderId,
                    NewParentFolderId = targetFolderId,
                });
                return;
            }
        }
    }

    private static void ApplyReorderDrop(DocWorkspace workspace)
    {
        if (!_reorderDropIsSet)
        {
            return;
        }

        if (_reorderDropKind == ContextItemKind.Table)
        {
            ApplyTableReorderDrop(workspace);
            return;
        }

        if (_reorderDropKind == ContextItemKind.Document)
        {
            ApplyDocumentReorderDrop(workspace);
            return;
        }

        if (_reorderDropKind == ContextItemKind.Folder)
        {
            ApplyFolderReorderDrop(workspace);
        }
    }

    private static void ApplyTableReorderDrop(DocWorkspace workspace)
    {
        var project = workspace.Project;
        int sourceIndex = FindTableIndex(project, _dragItemId);
        int targetIndex = FindTableIndex(project, _reorderDropTargetItemId);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            return;
        }

        var sourceTable = project.Tables[sourceIndex];
        var targetTable = project.Tables[targetIndex];
        if (!CanReorderTableWithinGroup(project, sourceTable, targetTable))
        {
            return;
        }

        int insertIndex = _reorderDropInsertAfter ? targetIndex + 1 : targetIndex;
        if (sourceIndex < insertIndex)
        {
            insertIndex--;
        }

        if (insertIndex == sourceIndex)
        {
            return;
        }

        workspace.ExecuteCommands(new[]
        {
            new DocCommand
            {
                Kind = DocCommandKind.RemoveTable,
                TableId = sourceTable.Id,
                TableIndex = sourceIndex,
                TableSnapshot = sourceTable,
            },
            new DocCommand
            {
                Kind = DocCommandKind.AddTable,
                TableIndex = insertIndex,
                TableSnapshot = sourceTable,
            },
        });
    }

    private static void ApplyDocumentReorderDrop(DocWorkspace workspace)
    {
        var project = workspace.Project;
        int sourceIndex = FindDocumentIndex(project, _dragItemId);
        int targetIndex = FindDocumentIndex(project, _reorderDropTargetItemId);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            return;
        }

        var sourceDocument = project.Documents[sourceIndex];
        var targetDocument = project.Documents[targetIndex];
        if (!CanReorderDocumentWithinGroup(project, sourceDocument, targetDocument))
        {
            return;
        }

        int insertIndex = _reorderDropInsertAfter ? targetIndex + 1 : targetIndex;
        if (sourceIndex < insertIndex)
        {
            insertIndex--;
        }

        if (insertIndex == sourceIndex)
        {
            return;
        }

        workspace.ExecuteCommands(new[]
        {
            new DocCommand
            {
                Kind = DocCommandKind.RemoveDocument,
                DocumentId = sourceDocument.Id,
                DocumentIndex = sourceIndex,
                DocumentSnapshot = sourceDocument,
            },
            new DocCommand
            {
                Kind = DocCommandKind.AddDocument,
                DocumentIndex = insertIndex,
                DocumentSnapshot = sourceDocument,
            },
        });
    }

    private static void ApplyFolderReorderDrop(DocWorkspace workspace)
    {
        var project = workspace.Project;
        int sourceIndex = FindFolderIndex(project, _dragItemId);
        int targetIndex = FindFolderIndex(project, _reorderDropTargetItemId);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            return;
        }

        var sourceFolder = project.Folders[sourceIndex];
        var targetFolder = project.Folders[targetIndex];
        if (!CanReorderDropTarget(project, ContextItemKind.Folder, _reorderDropScope, sourceFolder.Id, targetFolder.Id))
        {
            return;
        }

        int insertIndex = _reorderDropInsertAfter ? targetIndex + 1 : targetIndex;
        if (sourceIndex < insertIndex)
        {
            insertIndex--;
        }

        if (insertIndex == sourceIndex)
        {
            return;
        }

        workspace.ExecuteCommands(new[]
        {
            new DocCommand
            {
                Kind = DocCommandKind.RemoveFolder,
                FolderId = sourceFolder.Id,
                FolderIndex = sourceIndex,
                FolderSnapshot = sourceFolder.Clone(),
            },
            new DocCommand
            {
                Kind = DocCommandKind.AddFolder,
                FolderIndex = insertIndex,
                FolderSnapshot = sourceFolder.Clone(),
            },
        });
    }

    private static int FindTableIndex(DocProject project, string tableId)
    {
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            if (string.Equals(project.Tables[tableIndex].Id, tableId, StringComparison.Ordinal))
            {
                return tableIndex;
            }
        }

        return -1;
    }

    private static int FindDocumentIndex(DocProject project, string documentId)
    {
        for (int documentIndex = 0; documentIndex < project.Documents.Count; documentIndex++)
        {
            if (string.Equals(project.Documents[documentIndex].Id, documentId, StringComparison.Ordinal))
            {
                return documentIndex;
            }
        }

        return -1;
    }

    private static int FindFolderIndex(DocProject project, string folderId)
    {
        for (int folderIndex = 0; folderIndex < project.Folders.Count; folderIndex++)
        {
            if (string.Equals(project.Folders[folderIndex].Id, folderId, StringComparison.Ordinal))
            {
                return folderIndex;
            }
        }

        return -1;
    }

    private static bool IsUsingMultiItemDrag()
    {
        if (GetSelectionCountForScope(_dragScope) <= 1)
        {
            return false;
        }

        ContextItemKind sourceKind = _dragItemKind switch
        {
            DragItemKind.Table => ContextItemKind.Table,
            DragItemKind.Document => ContextItemKind.Document,
            DragItemKind.Folder => ContextItemKind.Folder,
            _ => ContextItemKind.None,
        };

        if (sourceKind == ContextItemKind.None)
        {
            return false;
        }

        return IsItemSelected(sourceKind, _dragScope, _dragItemId);
    }

    private static void ApplyMultiItemDrop(DocWorkspace workspace, string? targetFolderId)
    {
        if (_dragScope == DocFolderScope.Tables)
        {
            ApplyMultiTableScopeDrop(workspace, targetFolderId);
            return;
        }

        ApplyMultiDocumentScopeDrop(workspace, targetFolderId);
    }

    private static void ApplyMultiTableScopeDrop(DocWorkspace workspace, string? targetFolderId)
    {
        var commands = new List<DocCommand>(16);
        var selectedFolderIds = _selectedTableFolderIds;
        var project = workspace.Project;

        for (int folderIndex = 0; folderIndex < project.Folders.Count; folderIndex++)
        {
            var folder = project.Folders[folderIndex];
            if (folder.Scope != DocFolderScope.Tables || !selectedFolderIds.Contains(folder.Id))
            {
                continue;
            }

            if (HasSelectedAncestorFolder(project, folder.Id, selectedFolderIds))
            {
                continue;
            }

            if (!CanMoveFolderToTarget(project, folder, targetFolderId))
            {
                continue;
            }

            commands.Add(new DocCommand
            {
                Kind = DocCommandKind.MoveFolder,
                FolderId = folder.Id,
                OldParentFolderId = folder.ParentFolderId,
                NewParentFolderId = targetFolderId,
            });
        }

        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            var table = project.Tables[tableIndex];
            if (!_selectedTableIds.Contains(table.Id))
            {
                continue;
            }

            if (IsFolderWithinSet(project, table.FolderId, selectedFolderIds))
            {
                continue;
            }

            if (string.Equals(table.FolderId, targetFolderId, StringComparison.Ordinal))
            {
                continue;
            }

            commands.Add(new DocCommand
            {
                Kind = DocCommandKind.SetTableFolder,
                TableId = table.Id,
                OldFolderId = table.FolderId,
                NewFolderId = targetFolderId,
            });
        }

        ExecuteDropCommands(workspace, commands);
    }

    private static void ApplyMultiDocumentScopeDrop(DocWorkspace workspace, string? targetFolderId)
    {
        var commands = new List<DocCommand>(16);
        var selectedFolderIds = _selectedDocumentFolderIds;
        var project = workspace.Project;

        for (int folderIndex = 0; folderIndex < project.Folders.Count; folderIndex++)
        {
            var folder = project.Folders[folderIndex];
            if (folder.Scope != DocFolderScope.Documents || !selectedFolderIds.Contains(folder.Id))
            {
                continue;
            }

            if (HasSelectedAncestorFolder(project, folder.Id, selectedFolderIds))
            {
                continue;
            }

            if (!CanMoveFolderToTarget(project, folder, targetFolderId))
            {
                continue;
            }

            commands.Add(new DocCommand
            {
                Kind = DocCommandKind.MoveFolder,
                FolderId = folder.Id,
                OldParentFolderId = folder.ParentFolderId,
                NewParentFolderId = targetFolderId,
            });
        }

        for (int documentIndex = 0; documentIndex < project.Documents.Count; documentIndex++)
        {
            var document = project.Documents[documentIndex];
            if (!_selectedDocumentIds.Contains(document.Id))
            {
                continue;
            }

            if (IsFolderWithinSet(project, document.FolderId, selectedFolderIds))
            {
                continue;
            }

            if (string.Equals(document.FolderId, targetFolderId, StringComparison.Ordinal))
            {
                continue;
            }

            commands.Add(new DocCommand
            {
                Kind = DocCommandKind.SetDocumentFolder,
                DocumentId = document.Id,
                OldFolderId = document.FolderId,
                NewFolderId = targetFolderId,
            });
        }

        ExecuteDropCommands(workspace, commands);
    }

    private static void ExecuteDropCommands(DocWorkspace workspace, List<DocCommand> commands)
    {
        if (commands.Count == 0)
        {
            return;
        }

        if (commands.Count == 1)
        {
            workspace.ExecuteCommand(commands[0]);
            return;
        }

        workspace.ExecuteCommands(commands);
    }

    private static bool HasSelectedAncestorFolder(DocProject project, string folderId, HashSet<string> selectedFolderIds)
    {
        string? currentFolderId = GetParentFolderId(project, folderId);
        while (!string.IsNullOrWhiteSpace(currentFolderId))
        {
            if (selectedFolderIds.Contains(currentFolderId))
            {
                return true;
            }

            currentFolderId = GetParentFolderId(project, currentFolderId);
        }

        return false;
    }

    private static bool CanMoveFolderToTarget(DocProject project, DocFolder folder, string? targetFolderId)
    {
        if (string.Equals(folder.ParentFolderId, targetFolderId, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(targetFolderId))
        {
            return true;
        }

        if (string.Equals(folder.Id, targetFolderId, StringComparison.Ordinal))
        {
            return false;
        }

        if (IsFolderDescendant(project, folder.Id, targetFolderId))
        {
            return false;
        }

        return true;
    }

    private static bool IsFolderWithinSet(DocProject project, string? folderId, HashSet<string> folderIds)
    {
        string? currentFolderId = folderId;
        while (!string.IsNullOrWhiteSpace(currentFolderId))
        {
            if (folderIds.Contains(currentFolderId))
            {
                return true;
            }

            currentFolderId = GetParentFolderId(project, currentFolderId);
        }

        return false;
    }

    private static string? GetParentFolderId(DocProject project, string folderId)
    {
        for (int folderIndex = 0; folderIndex < project.Folders.Count; folderIndex++)
        {
            var folder = project.Folders[folderIndex];
            if (string.Equals(folder.Id, folderId, StringComparison.Ordinal))
            {
                return folder.ParentFolderId;
            }
        }

        return null;
    }

    private static void ClearDragState()
    {
        _dragPending = false;
        _dragActive = false;
        _dragItemKind = DragItemKind.None;
        _dragItemId = "";
        _dropTargetIsSet = false;
        _dropTargetIsRoot = false;
        _dropTargetFolderId = null;
        _reorderDropIsSet = false;
        _reorderDropKind = ContextItemKind.None;
        _reorderDropTargetItemId = "";
        _reorderDropInsertAfter = false;
        CancelDeferredSingleSelection();
    }

    private static void ResetDropTargetState()
    {
        if (!_dragActive)
        {
            return;
        }

        _dropTargetIsSet = false;
        _dropTargetIsRoot = false;
        _dropTargetFolderId = null;
        _reorderDropIsSet = false;
        _reorderDropKind = ContextItemKind.None;
        _reorderDropTargetItemId = "";
        _reorderDropInsertAfter = false;
    }

    private static void ApplyDeferredSingleSelection(DocWorkspace workspace)
    {
        if (!_deferredSingleSelectionPending)
        {
            return;
        }

        string itemId = _deferredSingleSelectionItemId;
        ContextItemKind kind = _deferredSingleSelectionKind;
        DocFolderScope scope = _deferredSingleSelectionScope;

        CancelDeferredSingleSelection();

        ClearAllSelections();
        SelectItem(kind, scope, itemId);
        SetSelectionAnchor(scope, kind, itemId);

        if (kind == ContextItemKind.Table)
        {
            for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
            {
                var table = workspace.Project.Tables[tableIndex];
                if (string.Equals(table.Id, itemId, StringComparison.Ordinal))
                {
                    workspace.ContentTabs.OpenOrFocusTableFromSidebar(table.Id);
                    break;
                }
            }
        }
        else if (kind == ContextItemKind.Document)
        {
            for (int documentIndex = 0; documentIndex < workspace.Project.Documents.Count; documentIndex++)
            {
                var document = workspace.Project.Documents[documentIndex];
                if (string.Equals(document.Id, itemId, StringComparison.Ordinal))
                {
                    workspace.ContentTabs.OpenOrFocusDocumentFromSidebar(document.Id);
                    break;
                }
            }
        }
    }

    private static void CancelDeferredSingleSelection()
    {
        _deferredSingleSelectionPending = false;
        _deferredSingleSelectionKind = ContextItemKind.None;
        _deferredSingleSelectionScope = DocFolderScope.Tables;
        _deferredSingleSelectionItemId = "";
    }
}
