using System;
using System.Collections.Generic;
using Derp.Doc.Model;
using Derp.Doc.Storage;
using DerpLib.ImGui.Windows;
using FontAwesome.Sharp;

namespace Derp.Doc.Editor;

internal sealed class DocContentTabs
{
    internal enum TabKind
    {
        Table,
        Document,
    }

    internal enum ParentRowIdApplyMode
    {
        Preserve,
        Clear,
        Set,
    }

    internal sealed class Tab
    {
        public string TabInstanceId = "";
        public string StateKey = "";
        public int WindowId;
        public TabKind Kind;
        public string TargetId = "";
        public string DisplayTitle = "";
        public int ActivationStamp;

        // Table-only
        public string ActiveViewId = "";
        public string? ParentRowId;
        public string SelectedRowId = "";

        // Document-only
        public int FocusedBlockIndex = -1;
    }

    private const int DefaultWindowIdBase = 0x20000000;
    private static readonly string TableTitleIcon = ((char)IconChar.Table).ToString();
    private static readonly string DocumentTitleIcon = ((char)IconChar.FileAlt).ToString();

    private readonly DocWorkspace _workspace;
    private readonly List<Tab> _tabs = new();
    private readonly List<int> _pendingCloseWindowIds = new();
    private int _nextWindowId = DefaultWindowIdBase;
    private int _activationStamp;
    private int _activeTabIndex = -1;
    private int _pendingFocusWindowId;
    private bool _activeProjectStateDirty;
    private bool _dockLayoutDirty;

    public DocContentTabs(DocWorkspace workspace)
    {
        _workspace = workspace;
    }

    public int TabCount => _tabs.Count;

    public Tab GetTabAt(int index) => _tabs[index];

    public int ActiveTabIndex => _activeTabIndex;

    public Tab? ActiveTab => _activeTabIndex >= 0 && _activeTabIndex < _tabs.Count ? _tabs[_activeTabIndex] : null;

    public int PendingFocusWindowId => _pendingFocusWindowId;

    public bool HasDirtyActiveProjectState => _activeProjectStateDirty;

    public bool HasDirtyDockLayout => _dockLayoutDirty;

    public void ClearDirtyActiveProjectState()
    {
        _activeProjectStateDirty = false;
    }

    public void ClearDirtyDockLayout()
    {
        _dockLayoutDirty = false;
    }

    public void ClearPendingFocus()
    {
        _pendingFocusWindowId = 0;
    }

    public void SetNextWindowIdForRestore(int nextWindowId)
    {
        if (nextWindowId < DefaultWindowIdBase)
        {
            nextWindowId = DefaultWindowIdBase;
        }

        if (_nextWindowId < nextWindowId)
        {
            _nextWindowId = nextWindowId;
        }
    }

    public int GetNextWindowIdForPersist()
    {
        return _nextWindowId;
    }

    public void ResetForLoadedProject()
    {
        QueueCloseAllOpenTabWindows();
        _tabs.Clear();
        _activeTabIndex = -1;
        _pendingFocusWindowId = 0;
        _activationStamp = 0;
        _activeProjectStateDirty = true;
        _dockLayoutDirty = true;
    }

    public void RestoreFromActiveProjectState(DocActiveProjectStateFile.ActiveProjectState state)
    {
        QueueCloseAllOpenTabWindows();
        _tabs.Clear();
        _activeTabIndex = -1;
        _pendingFocusWindowId = 0;
        _activationStamp = 0;
        _activeProjectStateDirty = false;
        _dockLayoutDirty = true;

        if (state == null)
        {
            return;
        }

        SetNextWindowIdForRestore(state.NextContentTabWindowId);

        for (int tabIndex = 0; tabIndex < state.OpenContentTabs.Count; tabIndex++)
        {
            var persisted = state.OpenContentTabs[tabIndex];
            if (persisted == null ||
                string.IsNullOrWhiteSpace(persisted.TabInstanceId) ||
                persisted.WindowId == 0 ||
                string.IsNullOrWhiteSpace(persisted.Kind) ||
                string.IsNullOrWhiteSpace(persisted.TargetId))
            {
                continue;
            }

            TabKind kind = ParsePersistedTabKind(persisted.Kind);
            if (kind == TabKind.Table)
            {
                var table = FindTable(persisted.TargetId);
                if (table == null)
                {
                    continue;
                }

                var tab = CreateTabForRestore(
                    kind,
                    persisted.TargetId,
                    persisted.TabInstanceId,
                    persisted.WindowId,
                    BuildTableTitle(table));
                tab.ActiveViewId = persisted.ActiveViewId;
                tab.ParentRowId = persisted.ParentRowId;
                _tabs.Add(tab);
            }
            else if (kind == TabKind.Document)
            {
                var doc = FindDocument(persisted.TargetId);
                if (doc == null)
                {
                    continue;
                }

                var tab = CreateTabForRestore(
                    kind,
                    persisted.TargetId,
                    persisted.TabInstanceId,
                    persisted.WindowId,
                    BuildDocumentTitle(doc));
                // Do not restore persisted document block focus from disk.
                // Focus is interaction-local and stale values can cause unexpected auto-selection.
                tab.FocusedBlockIndex = -1;
                _tabs.Add(tab);
            }
        }

        int desiredActiveIndex = -1;
        if (!string.IsNullOrWhiteSpace(state.ActiveContentTabInstanceId))
        {
            for (int tabIndex = 0; tabIndex < _tabs.Count; tabIndex++)
            {
                if (string.Equals(_tabs[tabIndex].TabInstanceId, state.ActiveContentTabInstanceId, StringComparison.Ordinal))
                {
                    desiredActiveIndex = tabIndex;
                    break;
                }
            }
        }

        if (_tabs.Count > 0)
        {
            _activeTabIndex = desiredActiveIndex >= 0 ? desiredActiveIndex : 0;
            TouchActivationStamp(_activeTabIndex);
            ApplyTabStateToWorkspace(_tabs[_activeTabIndex]);
            _pendingFocusWindowId = _tabs[_activeTabIndex].WindowId;
        }
    }

    public void ClosePendingTabWindows(ImWindowManager windowManager)
    {
        if (_pendingCloseWindowIds.Count <= 0)
        {
            return;
        }

        for (int i = 0; i < _pendingCloseWindowIds.Count; i++)
        {
            int windowId = _pendingCloseWindowIds[i];
            var window = windowManager.FindWindowById(windowId);
            if (window != null)
            {
                window.IsOpen = false;
            }
        }

        _pendingCloseWindowIds.Clear();
    }

    public void EnsureDefaultTabOpen()
    {
        if (_tabs.Count > 0)
        {
            return;
        }

        if (_workspace.Project.Tables.Count > 0)
        {
            var table = _workspace.Project.Tables[0];
            OpenTableInternal(
                tableId: table.Id,
                forceNewTab: true,
                parentRowIdApplyMode: ParentRowIdApplyMode.Clear,
                parentRowId: null);
            return;
        }

        if (_workspace.Project.Documents.Count > 0)
        {
            var doc = _workspace.Project.Documents[0];
            OpenDocumentInternal(doc.Id, forceNewTab: true);
        }
    }

    public bool TryActivateByWindowId(int windowId)
    {
        if (windowId == 0)
        {
            return false;
        }

        int tabIndex = FindTabIndexByWindowId(windowId);
        if (tabIndex < 0)
        {
            return false;
        }

        if (_activeTabIndex == tabIndex)
        {
            TouchActivationStamp(tabIndex);
            return false;
        }

        _workspace.CommitTableCellEditIfActive();
        CaptureWorkspaceStateIntoTabIfActive();
        _activeTabIndex = tabIndex;
        TouchActivationStamp(_activeTabIndex);
        ApplyTabStateToWorkspace(_tabs[_activeTabIndex]);
        _activeProjectStateDirty = true;
        return true;
    }

    public void CaptureWorkspaceStateIntoTabIfActive()
    {
        if (_activeTabIndex < 0 || _activeTabIndex >= _tabs.Count)
        {
            return;
        }

        Tab tab = _tabs[_activeTabIndex];
        if (tab.Kind == TabKind.Table)
        {
            string nextActiveViewId = _workspace.ActiveTableView?.Id ?? "";
            if (!string.Equals(tab.ActiveViewId, nextActiveViewId, StringComparison.Ordinal))
            {
                tab.ActiveViewId = nextActiveViewId;
                _activeProjectStateDirty = true;
            }

            string? nextParentRowId = _workspace.ActiveParentRowId;
            if (!string.Equals(tab.ParentRowId, nextParentRowId, StringComparison.Ordinal))
            {
                tab.ParentRowId = nextParentRowId;
                _activeProjectStateDirty = true;
            }

            tab.SelectedRowId = "";
            if (_workspace.ActiveTable != null &&
                _workspace.SelectedRowIndex >= 0 &&
                _workspace.SelectedRowIndex < _workspace.ActiveTable.Rows.Count)
            {
                tab.SelectedRowId = _workspace.ActiveTable.Rows[_workspace.SelectedRowIndex].Id;
            }
        }
        else if (tab.Kind == TabKind.Document)
        {
            int nextFocusedBlockIndex = _workspace.FocusedBlockIndex;
            if (tab.FocusedBlockIndex != nextFocusedBlockIndex)
            {
                tab.FocusedBlockIndex = nextFocusedBlockIndex;
                _activeProjectStateDirty = true;
            }
        }
    }

    public void RemoveClosedTabs(ImWindowManager windowManager)
    {
        for (int tabIndex = _tabs.Count - 1; tabIndex >= 0; tabIndex--)
        {
            int windowId = _tabs[tabIndex].WindowId;
            if (windowId != 0)
            {
                var window = windowManager.FindWindowById(windowId);
                if (window != null && window.IsOpen)
                {
                    continue;
                }

                // Newly opened tabs can exist for one frame before their dock window is created.
                // While a dock rebuild or pending focus is in flight, avoid treating a missing window as closed.
                if (window == null &&
                    (_dockLayoutDirty || windowId == _pendingFocusWindowId))
                {
                    continue;
                }
            }

            bool wasActive = tabIndex == _activeTabIndex;
            _tabs.RemoveAt(tabIndex);
            if (_activeTabIndex > tabIndex)
            {
                _activeTabIndex--;
            }
            else if (_activeTabIndex == tabIndex)
            {
                _activeTabIndex = -1;
            }

            _activeProjectStateDirty = true;
            _dockLayoutDirty = true;

            if (wasActive && _tabs.Count > 0)
            {
                _activeTabIndex = Math.Clamp(_activeTabIndex, 0, _tabs.Count - 1);
                if (_activeTabIndex < 0)
                {
                    _activeTabIndex = 0;
                }

                ApplyTabStateToWorkspace(_tabs[_activeTabIndex]);
                _pendingFocusWindowId = _tabs[_activeTabIndex].WindowId;
            }
        }

        if (_tabs.Count <= 0)
        {
            _activeTabIndex = -1;
        }
        else if (_activeTabIndex < 0 || _activeTabIndex >= _tabs.Count)
        {
            _activeTabIndex = 0;
            ApplyTabStateToWorkspace(_tabs[_activeTabIndex]);
        }
    }

    public int OpenOrFocusTableFromSidebar(string tableId)
    {
        return OpenTableInternal(
            tableId: tableId,
            forceNewTab: false,
            parentRowIdApplyMode: ParentRowIdApplyMode.Clear,
            parentRowId: null);
    }

    public int OpenTableInNewTabFromSidebar(string tableId)
    {
        return OpenTableInternal(
            tableId: tableId,
            forceNewTab: true,
            parentRowIdApplyMode: ParentRowIdApplyMode.Clear,
            parentRowId: null);
    }

    public int OpenOrFocusTableFromNavigation(string tableId, string? parentRowId)
    {
        return OpenTableInternal(
            tableId: tableId,
            forceNewTab: false,
            parentRowIdApplyMode: ParentRowIdApplyMode.Set,
            parentRowId: parentRowId);
    }

    public int OpenOrFocusDocumentFromSidebar(string documentId)
    {
        return OpenDocumentInternal(documentId, forceNewTab: false);
    }

    public int OpenDocumentInNewTabFromSidebar(string documentId)
    {
        return OpenDocumentInternal(documentId, forceNewTab: true);
    }

    private int OpenDocumentInternal(string documentId, bool forceNewTab)
    {
        if (string.IsNullOrWhiteSpace(documentId))
        {
            return 0;
        }

        int existing = forceNewTab ? -1 : FindMostRecentTabIndex(TabKind.Document, documentId);
        int tabIndex = existing;
        if (tabIndex < 0)
        {
            var doc = FindDocument(documentId);
            if (doc == null)
            {
                return 0;
            }

            var tab = CreateTab(TabKind.Document, documentId, BuildDocumentTitle(doc));
            _tabs.Add(tab);
            tabIndex = _tabs.Count - 1;
            _activeProjectStateDirty = true;
            _dockLayoutDirty = true;
        }

        ActivateTabByIndex(tabIndex);
        _pendingFocusWindowId = _tabs[tabIndex].WindowId;
        return _tabs[tabIndex].WindowId;
    }

    private int OpenTableInternal(
        string tableId,
        bool forceNewTab,
        ParentRowIdApplyMode parentRowIdApplyMode,
        string? parentRowId)
    {
        if (string.IsNullOrWhiteSpace(tableId))
        {
            return 0;
        }

        int existing = forceNewTab ? -1 : FindMostRecentTabIndex(TabKind.Table, tableId);
        int tabIndex = existing;
        if (tabIndex < 0)
        {
            var table = FindTable(tableId);
            if (table == null)
            {
                return 0;
            }

            var tab = CreateTab(TabKind.Table, tableId, BuildTableTitle(table));
            _tabs.Add(tab);
            tabIndex = _tabs.Count - 1;
            _activeProjectStateDirty = true;
            _dockLayoutDirty = true;
        }

        Tab tabToMutate = _tabs[tabIndex];
        if (parentRowIdApplyMode == ParentRowIdApplyMode.Clear)
        {
            tabToMutate.ParentRowId = null;
        }
        else if (parentRowIdApplyMode == ParentRowIdApplyMode.Set)
        {
            tabToMutate.ParentRowId = parentRowId;
        }

        ActivateTabByIndex(tabIndex);
        _pendingFocusWindowId = _tabs[tabIndex].WindowId;
        return _tabs[tabIndex].WindowId;
    }

    private void ActivateTabByIndex(int tabIndex)
    {
        if (tabIndex < 0 || tabIndex >= _tabs.Count)
        {
            return;
        }

        if (_activeTabIndex == tabIndex)
        {
            TouchActivationStamp(tabIndex);
            ApplyTabStateToWorkspace(_tabs[tabIndex]);
            return;
        }

        _workspace.CommitTableCellEditIfActive();
        CaptureWorkspaceStateIntoTabIfActive();
        _activeTabIndex = tabIndex;
        TouchActivationStamp(_activeTabIndex);
        ApplyTabStateToWorkspace(_tabs[_activeTabIndex]);
        _activeProjectStateDirty = true;
    }

    private void TouchActivationStamp(int tabIndex)
    {
        _activationStamp++;
        if (_activationStamp <= 0)
        {
            _activationStamp = 1;
        }

        _tabs[tabIndex].ActivationStamp = _activationStamp;
    }

    private void ApplyTabStateToWorkspace(Tab tab)
    {
        if (tab.Kind == TabKind.Table)
        {
            DocTable? table = FindTable(tab.TargetId);
            if (table == null)
            {
                return;
            }

            _workspace.ActiveTable = table;
            _workspace.ActiveTableView = ResolveTableViewById(table, tab.ActiveViewId);
            _workspace.ActiveView = ActiveViewKind.Table;
            _workspace.InspectedTable = null;
            _workspace.InspectedBlockId = null;
            _workspace.ActiveParentRowId = tab.ParentRowId;
            _workspace.SelectedRowIndex = ResolveRowIndexById(table, tab.SelectedRowId);
            _workspace.FocusedBlockIndex = -1;
            _workspace.FocusedBlockTextSnapshot = null;
            return;
        }

        if (tab.Kind == TabKind.Document)
        {
            DocDocument? doc = FindDocument(tab.TargetId);
            if (doc == null)
            {
                return;
            }

            _workspace.ActiveDocument = doc;
            _workspace.ActiveView = ActiveViewKind.Document;
            int focusedBlockIndex = tab.FocusedBlockIndex;
            if (focusedBlockIndex < 0 || focusedBlockIndex >= doc.Blocks.Count)
            {
                focusedBlockIndex = -1;
            }

            _workspace.FocusedBlockIndex = focusedBlockIndex;
            _workspace.FocusedBlockTextSnapshot = null;
            _workspace.ActiveParentRowId = null;
            _workspace.SelectedRowIndex = -1;
        }
    }

    private int FindMostRecentTabIndex(TabKind kind, string targetId)
    {
        int bestIndex = -1;
        int bestStamp = int.MinValue;
        for (int tabIndex = 0; tabIndex < _tabs.Count; tabIndex++)
        {
            Tab tab = _tabs[tabIndex];
            if (tab.Kind != kind)
            {
                continue;
            }

            if (!string.Equals(tab.TargetId, targetId, StringComparison.Ordinal))
            {
                continue;
            }

            int stamp = tab.ActivationStamp;
            if (stamp > bestStamp)
            {
                bestStamp = stamp;
                bestIndex = tabIndex;
            }
        }

        if (bestIndex < 0)
        {
            for (int tabIndex = _tabs.Count - 1; tabIndex >= 0; tabIndex--)
            {
                Tab tab = _tabs[tabIndex];
                if (tab.Kind == kind &&
                    string.Equals(tab.TargetId, targetId, StringComparison.Ordinal))
                {
                    return tabIndex;
                }
            }
        }

        return bestIndex;
    }

    private int FindTabIndexByWindowId(int windowId)
    {
        for (int tabIndex = 0; tabIndex < _tabs.Count; tabIndex++)
        {
            if (_tabs[tabIndex].WindowId == windowId)
            {
                return tabIndex;
            }
        }

        return -1;
    }

    private Tab CreateTab(TabKind kind, string targetId, string title)
    {
        var tab = new Tab();
        tab.TabInstanceId = Guid.NewGuid().ToString("N");
        tab.StateKey = "content_tab:" + tab.TabInstanceId;
        tab.WindowId = AllocateWindowId();
        tab.Kind = kind;
        tab.TargetId = targetId;
        tab.DisplayTitle = title;
        tab.ActivationStamp = 0;
        tab.ActiveViewId = "";
        tab.ParentRowId = null;
        tab.SelectedRowId = "";
        tab.FocusedBlockIndex = -1;
        return tab;
    }

    private Tab CreateTabForRestore(TabKind kind, string targetId, string tabInstanceId, int windowId, string title)
    {
        var tab = new Tab();
        tab.TabInstanceId = tabInstanceId;
        tab.StateKey = "content_tab:" + tabInstanceId;
        tab.WindowId = windowId;
        tab.Kind = kind;
        tab.TargetId = targetId;
        tab.DisplayTitle = title;
        tab.ActivationStamp = 0;
        tab.ActiveViewId = "";
        tab.ParentRowId = null;
        tab.SelectedRowId = "";
        tab.FocusedBlockIndex = -1;

        int nextCandidate = windowId + 1;
        if (nextCandidate > _nextWindowId)
        {
            _nextWindowId = nextCandidate;
        }

        return tab;
    }

    private void QueueCloseAllOpenTabWindows()
    {
        for (int tabIndex = 0; tabIndex < _tabs.Count; tabIndex++)
        {
            int windowId = _tabs[tabIndex].WindowId;
            if (windowId != 0)
            {
                _pendingCloseWindowIds.Add(windowId);
            }
        }
    }

    private static TabKind ParsePersistedTabKind(string kind)
    {
        if (string.Equals(kind, "document", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(kind, "doc", StringComparison.OrdinalIgnoreCase))
        {
            return TabKind.Document;
        }

        return TabKind.Table;
    }

    private int AllocateWindowId()
    {
        int windowId = _nextWindowId;
        _nextWindowId++;
        if (_nextWindowId < DefaultWindowIdBase)
        {
            _nextWindowId = DefaultWindowIdBase;
        }

        return windowId;
    }

    private DocTable? FindTable(string tableId)
    {
        for (int tableIndex = 0; tableIndex < _workspace.Project.Tables.Count; tableIndex++)
        {
            DocTable table = _workspace.Project.Tables[tableIndex];
            if (string.Equals(table.Id, tableId, StringComparison.Ordinal))
            {
                return table;
            }
        }

        return null;
    }

    private DocDocument? FindDocument(string documentId)
    {
        for (int documentIndex = 0; documentIndex < _workspace.Project.Documents.Count; documentIndex++)
        {
            DocDocument doc = _workspace.Project.Documents[documentIndex];
            if (string.Equals(doc.Id, documentId, StringComparison.Ordinal))
            {
                return doc;
            }
        }

        return null;
    }

    private static DocView? ResolveTableViewById(DocTable table, string viewId)
    {
        if (!string.IsNullOrWhiteSpace(viewId))
        {
            for (int viewIndex = 0; viewIndex < table.Views.Count; viewIndex++)
            {
                DocView view = table.Views[viewIndex];
                if (string.Equals(view.Id, viewId, StringComparison.Ordinal))
                {
                    return view;
                }
            }
        }

        return table.Views.Count > 0 ? table.Views[0] : null;
    }

    private static int ResolveRowIndexById(DocTable table, string rowId)
    {
        if (!string.IsNullOrWhiteSpace(rowId))
        {
            for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                if (string.Equals(table.Rows[rowIndex].Id, rowId, StringComparison.Ordinal))
                {
                    return rowIndex;
                }
            }
        }

        return -1;
    }

    private static string BuildTableTitle(DocTable table)
    {
        if (string.IsNullOrWhiteSpace(table.Name))
        {
            return TableTitleIcon;
        }

        return TableTitleIcon + " " + table.Name;
    }

    private static string BuildDocumentTitle(DocDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.Title))
        {
            return DocumentTitleIcon;
        }

        return DocumentTitleIcon + " " + document.Title;
    }
}
