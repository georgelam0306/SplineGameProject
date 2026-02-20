using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;
using DerpLib.ImGui.Widgets;
using DerpLib.ImGui.Windows;
using FontAwesome.Sharp;

namespace Derp.UI;

internal static class DocumentIoDialog
{
    private enum Mode : byte
    {
        None = 0,
        Open = 1,
        SaveAs = 2,
        ExportBdui = 3,
    }

    private static Mode _mode;
    private static readonly char[] FileNameBuffer = new char[256];
    private static int _fileNameLength;
    private static readonly char[] SearchBuffer = new char[128];
    private static int _searchLength;
    private static readonly char[] NewFolderBuffer = new char[128];
    private static int _newFolderLength;

    private static string _currentDirectory = string.Empty;
    private static string _selectedPath = string.Empty;
    private static int _selectedVisibleIndex = -1;
    private static float _scrollY;
    private static bool _showHiddenFiles;
    private static bool _filterToExpectedExtension = true;
    private static bool _showCreateFolder;
    private static bool _showOverwriteConfirm;
    private static string _pendingOverwritePath = string.Empty;
    private static string _pendingOverwriteActionLabel = string.Empty;

    private enum PendingAction : byte
    {
        None = 0,
        Navigate = 1,
        Execute = 2
    }

    private static PendingAction _pendingAction;
    private static string _pendingActionPath = string.Empty;

    private static readonly List<Entry> Entries = new(capacity: 256);
    private static readonly List<int> VisibleEntryIndices = new(capacity: 256);

    private static int _lastSearchLength = -1;
    private static bool _lastShowHiddenFiles;
    private static bool _lastFilterToExpectedExtension;

    private static string _errorMessage = string.Empty;

    private static readonly Comparison<Entry> EntrySort = CompareEntries;

    private static readonly string BackButtonLabel = ((char)IconChar.ChevronLeft).ToString();
    private static readonly string ForwardButtonLabel = ((char)IconChar.ChevronRight).ToString();
    private static readonly string UpButtonLabel = ((char)IconChar.ArrowUp).ToString();
    private static readonly string RefreshButtonLabel = ((char)IconChar.ArrowsRotate).ToString();

    private static readonly string ShowHiddenOnLabel = ((char)IconChar.EyeSlash).ToString();
    private static readonly string ShowHiddenOffLabel = ((char)IconChar.Eye).ToString();

    private static readonly string FilterOnLabel = ((char)IconChar.Filter).ToString();
    private static readonly string FilterOffLabel = ((char)IconChar.FilterCircleXmark).ToString();

    private static readonly string NewFolderButtonLabel = ((char)IconChar.FolderPlus).ToString() + " New Folder";
    private static readonly string CreateFolderButtonLabel = ((char)IconChar.Check).ToString() + " Create";
    private static readonly string CancelButtonLabel = ((char)IconChar.Xmark).ToString() + " Cancel";

    private static readonly string OkOpenButtonLabel = ((char)IconChar.FolderOpen).ToString() + " Open";
    private static readonly string OkSaveButtonLabel = ((char)IconChar.FloppyDisk).ToString() + " Save";
    private static readonly string OkExportButtonLabel = ((char)IconChar.FileExport).ToString() + " Export";

    private static readonly string OverwriteButtonLabel = ((char)IconChar.TriangleExclamation).ToString() + " Overwrite";

    private static readonly string FolderIcon = ((char)IconChar.Folder).ToString();
    private static readonly string FileIcon = ((char)IconChar.File).ToString();
    private static readonly string FileDuiIcon = ((char)IconChar.FileCode).ToString();
    private static readonly string FileBduiIcon = ((char)IconChar.BoxArchive).ToString();

    private readonly struct Entry
    {
        public readonly string Name;
        public readonly string FullPath;
        public readonly bool IsDirectory;

        public Entry(string name, string fullPath, bool isDirectory)
        {
            Name = name;
            FullPath = fullPath;
            IsDirectory = isDirectory;
        }
    }

    public static void RequestOpen(UiWorkspace workspace)
    {
        _errorMessage = string.Empty;
        _mode = Mode.Open;
        InitializeStartLocation(workspace);
        SetFileNameDefault(workspace);
    }

    public static void RequestSaveAs(UiWorkspace workspace)
    {
        _errorMessage = string.Empty;
        _mode = Mode.SaveAs;
        InitializeStartLocation(workspace);
        SetFileNameDefault(workspace);
    }

    public static void RequestExportBdui(UiWorkspace workspace)
    {
        _errorMessage = string.Empty;
        _mode = Mode.ExportBdui;
        InitializeStartLocation(workspace);
        SetFileNameExportDefault(workspace);
    }

    internal static bool IsOpen => _mode != Mode.None;

    internal static bool IsBlockingGlobalShortcuts()
    {
        if (_mode == Mode.None)
        {
            return false;
        }

        string title = GetWindowTitle();
        var focused = Im.WindowManager.FocusedWindow;
        if (focused != null && focused.IsOpen && focused.Title == title)
        {
            return true;
        }

        var window = Im.WindowManager.FindWindow(title);
        if (window == null || !window.IsOpen)
        {
            return false;
        }

        var viewport = Im.Context.CurrentViewport;
        Vector2 screenMouse = viewport == null ? Im.Context.Input.MousePos : viewport.ScreenPosition + Im.Context.Input.MousePos;
        return window.Rect.Contains(screenMouse);
    }

    public static void Draw(UiWorkspace workspace)
    {
        if (_mode == Mode.None)
        {
            return;
        }

        var ctx = Im.Context;
        if (ctx.Input.KeyEscape)
        {
            Close();
            return;
        }

        string title = GetWindowTitle();
        const float width = 920f;
        const float height = 620f;

        float x = 340f;
        float y = 200f;

        if (!Im.BeginWindow(title, x, y, width, height, ImWindowFlags.NoResize | ImWindowFlags.NoMove))
        {
            Im.EndWindow();
            return;
        }

        DrawToolbar();
        ImLayout.Space(6f);
        DrawLocationRow();
        ImLayout.Space(6f);
        DrawList(workspace);
        ImLayout.Space(6f);
        DrawFooter(workspace);

        if (_showOverwriteConfirm)
        {
            DrawOverwriteConfirm(workspace);
        }

        ImLayout.Space(6f);

        if (!string.IsNullOrEmpty(_errorMessage))
        {
            var errRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
            Im.Text(_errorMessage.AsSpan(), errRect.X, errRect.Y + (errRect.Height - Im.Style.FontSize) * 0.5f, Im.Style.FontSize, Im.Style.Secondary);
            ImLayout.Space(2f);
        }

        Im.EndWindow();
    }

    private static string GetWindowTitle()
    {
        return _mode switch
        {
            Mode.Open => "Open .dui",
            Mode.SaveAs => "Save As .dui",
            Mode.ExportBdui => "Export .bdui",
            _ => "File"
        };
    }

    private static void DrawToolbar()
    {
        var row = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);

        float x = row.X;
        float y = row.Y;
        float buttonH = row.Height;

        if (IconButton("file_back", BackButtonLabel, "Back", x, y, 36f, buttonH))
        {
            NavigateBack();
        }
        x += 36f + Im.Style.Spacing;

        if (IconButton("file_forward", ForwardButtonLabel, "Forward", x, y, 36f, buttonH))
        {
            NavigateForward();
        }
        x += 36f + Im.Style.Spacing;

        if (IconButton("file_up", UpButtonLabel, "Up", x, y, 36f, buttonH))
        {
            NavigateUp();
        }
        x += 36f + Im.Style.Spacing;

        if (IconButton("file_refresh", RefreshButtonLabel, "Refresh", x, y, 36f, buttonH))
        {
            RefreshDirectory();
        }
        x += 36f + Im.Style.Spacing;

        string hiddenLabel = _showHiddenFiles ? ShowHiddenOnLabel : ShowHiddenOffLabel;
        if (IconButton("file_hidden", hiddenLabel, "Show hidden files", x, y, 36f, buttonH))
        {
            _showHiddenFiles = !_showHiddenFiles;
        }
        x += 36f + Im.Style.Spacing;

        string expectedExt = GetExpectedExtension();
        if (!string.IsNullOrEmpty(expectedExt))
        {
            string filterLabel = _filterToExpectedExtension ? FilterOnLabel : FilterOffLabel;
            if (IconButton("file_filter", filterLabel, "Filter by file type", x, y, 36f, buttonH))
            {
                _filterToExpectedExtension = !_filterToExpectedExtension;
            }
            x += 36f + Im.Style.Spacing;
        }

        float searchW = Math.Max(180f, row.Right - x);
        _ = ImSearchBox.DrawAt("file_search", SearchBuffer, ref _searchLength, SearchBuffer.Length, x, y, searchW);
    }

    private static void DrawLocationRow()
    {
        var row = ImLayout.AllocateRect(0f, ImBreadcrumbs.Height);
        Im.Text("Location".AsSpan(), row.X, row.Y + (row.Height - Im.Style.FontSize) * 0.5f, Im.Style.FontSize, Im.Style.TextPrimary);

        float x = row.X + 80f;
        float y = row.Y;

        string dir = _currentDirectory;
        if (string.IsNullOrEmpty(dir))
        {
            dir = Environment.CurrentDirectory;
        }

        int clicked = ImBreadcrumbs.Draw("file_breadcrumbs", dir, Path.DirectorySeparatorChar, x, y);
        if (clicked >= 0)
        {
            string target = GetBreadcrumbTargetPath(dir, Path.DirectorySeparatorChar, clicked);
            if (!string.IsNullOrEmpty(target))
            {
                NavigateTo(target);
            }
        }

        if (_showCreateFolder)
        {
            ImLayout.Space(6f);
            var folderRow = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
            Im.Text("New Folder".AsSpan(), folderRow.X, folderRow.Y + (folderRow.Height - Im.Style.FontSize) * 0.5f, Im.Style.FontSize, Im.Style.TextPrimary);
            float inputX = folderRow.X + 120f;
            float inputW = Math.Max(80f, folderRow.Width - 120f - (2 * 120f) - (2 * Im.Style.Spacing));
            _ = Im.TextInput("new_folder", NewFolderBuffer, ref _newFolderLength, NewFolderBuffer.Length, inputX, folderRow.Y, inputW);

            float createX = folderRow.Right - 120f - (120f + Im.Style.Spacing);
            float cancelX = folderRow.Right - 120f;
            var ctx = Im.Context;
            ctx.PushId("file_create_folder");
            bool createPressed = Im.Button(CreateFolderButtonLabel, createX, folderRow.Y, 120f, folderRow.Height);
            ctx.PopId();
            if (createPressed)
            {
                TryCreateFolder();
            }
            ctx.PushId("file_cancel_create_folder");
            bool cancelPressed = Im.Button(CancelButtonLabel, cancelX, folderRow.Y, 120f, folderRow.Height);
            ctx.PopId();
            if (cancelPressed)
            {
                _showCreateFolder = false;
            }
        }
    }

    private static void DrawList(UiWorkspace workspace)
    {
        if (string.IsNullOrEmpty(_currentDirectory))
        {
            NavigateTo(Environment.CurrentDirectory);
        }

        EnsureVisibleListUpToDate();

        var rect = ImLayout.AllocateRect(0f, 420f);

        float rowHeight = 24f;
        int visibleCount = VisibleEntryIndices.Count;
        float contentHeight = visibleCount * rowHeight;

        float scrollbarWidth = Im.Style.ScrollbarWidth;
        bool hasScrollbar = contentHeight > rect.Height;
        var contentRect = rect;
        var scrollbarRect = rect;
        if (hasScrollbar)
        {
            contentRect.Width = rect.Width - scrollbarWidth;
            scrollbarRect = new ImRect(rect.Right - scrollbarWidth, rect.Y, scrollbarWidth, rect.Height);
        }

        int scrollbarWidgetId = Im.Context.GetId("file_list_scroll");
        float contentY = ImScrollView.Begin(contentRect, contentHeight, ref _scrollY, handleMouseWheel: true);

        int first = (int)MathF.Floor(_scrollY / rowHeight);
        if (first < 0)
        {
            first = 0;
        }
        int rowsPerPage = (int)MathF.Ceiling(contentRect.Height / rowHeight) + 2;
        int last = first + rowsPerPage;
        if (last > visibleCount)
        {
            last = visibleCount;
        }

        for (int visibleIndex = first; visibleIndex < last; visibleIndex++)
        {
            // The list can be invalidated by input handlers (navigate/refresh). Defer actions until after the loop.
            if ((uint)visibleIndex >= (uint)VisibleEntryIndices.Count)
            {
                break;
            }

            int entryIndex = VisibleEntryIndices[visibleIndex];
            if ((uint)entryIndex >= (uint)Entries.Count)
            {
                continue;
            }

            Entry entry = Entries[entryIndex];

            float y = contentY + visibleIndex * rowHeight;
            var rowRect = new ImRect(contentRect.X, y, contentRect.Width, rowHeight);

            bool selected = visibleIndex == _selectedVisibleIndex;
            DrawEntryRow(workspace, visibleIndex, entry, rowRect, selected);
        }

        ImScrollView.End(scrollbarWidgetId, scrollbarRect, rect.Height, contentHeight, ref _scrollY);

        ApplyPendingAction(workspace);
    }

    private static void DrawFooter(UiWorkspace workspace)
    {
        var row = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        float x = row.X;
        float y = row.Y;

        var ctx = Im.Context;
        ctx.PushId("file_new_folder");
        bool newFolderPressed = Im.Button(NewFolderButtonLabel, x, y, 160f, row.Height);
        ctx.PopId();
        if (newFolderPressed)
        {
            _showCreateFolder = !_showCreateFolder;
            _errorMessage = string.Empty;
            _newFolderLength = 0;
        }
        x += 160f + Im.Style.Spacing;

        if (_mode != Mode.Open)
        {
            Im.Text("File".AsSpan(), x, y + (row.Height - Im.Style.FontSize) * 0.5f, Im.Style.FontSize, Im.Style.TextPrimary);
            x += 48f;

            float inputW = Math.Max(160f, row.Right - x - (2 * 120f) - (2 * Im.Style.Spacing));
            _ = Im.TextInput("file_name", FileNameBuffer, ref _fileNameLength, FileNameBuffer.Length, x, y, inputW);
            x += inputW + Im.Style.Spacing;
        }
        else
        {
            x = row.Right - (2 * 120f) - Im.Style.Spacing;
        }

        float okX = row.Right - 120f - (120f + Im.Style.Spacing);
        float cancelX = row.Right - 120f;

        ctx.PushId("file_cancel_main");
        bool cancelMainPressed = Im.Button(CancelButtonLabel, cancelX, y, 120f, row.Height);
        ctx.PopId();
        if (cancelMainPressed)
        {
            Close();
            return;
        }

        string okLabel = _mode switch
        {
            Mode.Open => OkOpenButtonLabel,
            Mode.SaveAs => OkSaveButtonLabel,
            Mode.ExportBdui => OkExportButtonLabel,
            _ => "OK"
        };

        ctx.PushId("file_ok");
        bool acceptClick = Im.Button(okLabel, okX, y, 120f, row.Height);
        ctx.PopId();
        bool acceptEnter = Im.Context.Input.KeyEnter && !Im.Context.IsFocused(Im.Context.GetId("file_search"));
        if (acceptClick || acceptEnter)
        {
            TryAccept(workspace);
        }
    }

    private static void DrawOverwriteConfirm(UiWorkspace workspace)
    {
        var row = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);

        Im.Text("File exists. Overwrite?".AsSpan(), row.X, row.Y + (row.Height - Im.Style.FontSize) * 0.5f, Im.Style.FontSize, Im.Style.Secondary);

        float okX = row.Right - 120f - (120f + Im.Style.Spacing);
        float cancelX = row.Right - 120f;

        var ctx = Im.Context;
        ctx.PushId("file_overwrite");
        bool overwritePressed = Im.Button(OverwriteButtonLabel, okX, row.Y, 120f, row.Height);
        ctx.PopId();
        if (overwritePressed)
        {
            string path = _pendingOverwritePath;
            _showOverwriteConfirm = false;
            _pendingOverwritePath = string.Empty;
            _pendingOverwriteActionLabel = string.Empty;
            TryExecute(workspace, path);
        }
        ctx.PushId("file_cancel_overwrite");
        bool cancelOverwritePressed = Im.Button(CancelButtonLabel, cancelX, row.Y, 120f, row.Height);
        ctx.PopId();
        if (cancelOverwritePressed)
        {
            _showOverwriteConfirm = false;
            _pendingOverwritePath = string.Empty;
            _pendingOverwriteActionLabel = string.Empty;
        }
    }

    private static void TryAccept(UiWorkspace workspace)
    {
        _errorMessage = string.Empty;

        if (_mode == Mode.Open)
        {
            if (_selectedVisibleIndex < 0 || _selectedVisibleIndex >= VisibleEntryIndices.Count)
            {
                _errorMessage = "Select a file to open.";
                return;
            }

            Entry entry = Entries[VisibleEntryIndices[_selectedVisibleIndex]];
            if (entry.IsDirectory)
            {
                NavigateTo(entry.FullPath);
                return;
            }

            string path = entry.FullPath;
            if (!HasExpectedExtension(path))
            {
                _errorMessage = "Unsupported file type.";
                return;
            }

            TryExecute(workspace, path);
            return;
        }

        string fileName = new string(FileNameBuffer, 0, _fileNameLength).Trim();
        if (fileName.Length == 0)
        {
            _errorMessage = "File name is required.";
            return;
        }

        string ext = GetExpectedExtension();
        if (!fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
        {
            fileName = fileName + ext;
        }

        string pathFinal;
        if (Path.IsPathRooted(fileName))
        {
            pathFinal = fileName;
        }
        else
        {
            pathFinal = Path.Combine(_currentDirectory, fileName);
        }

        if (_mode != Mode.Open && File.Exists(pathFinal))
        {
            _showOverwriteConfirm = true;
            _pendingOverwritePath = pathFinal;
            _pendingOverwriteActionLabel = _mode == Mode.SaveAs ? "Overwrite" : "Overwrite";
            return;
        }

        TryExecute(workspace, pathFinal);
    }

    private static void DrawEntryRow(UiWorkspace workspace, int visibleIndex, Entry entry, ImRect rect, bool selected)
    {
        var ctx = Im.Context;
        var input = ctx.Input;

        ctx.PushId(visibleIndex);
        int widgetId = ctx.GetId("row");
        bool hovered = rect.Contains(Im.MousePos);
        if (hovered)
        {
            ctx.SetHot(widgetId);
        }

        bool clicked = false;
        bool doubleClicked = false;
        if (ctx.IsHot(widgetId) && input.MousePressed)
        {
            ctx.SetActive(widgetId);
            clicked = true;
            doubleClicked = input.IsDoubleClick;
        }

        if (ctx.IsActive(widgetId) && input.MouseReleased)
        {
            ctx.ClearActive();
        }

        uint bg = selected ? Im.Style.Active : (hovered ? Im.Style.Hover : Im.Style.Surface);
        Im.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, bg);

        string icon = GetEntryIcon(entry);
        float iconX = rect.X + 8f;
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(icon.AsSpan(), iconX, textY, Im.Style.FontSize, Im.Style.TextSecondary);

        float textX = rect.X + 28f;
        Im.Text(entry.Name.AsSpan(), textX, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        if (clicked)
        {
            SelectEntry(visibleIndex, entry);
            if (doubleClicked)
            {
                if (entry.IsDirectory)
                {
                    RequestNavigate(entry.FullPath);
                }
                else if (_mode == Mode.Open)
                {
                    // Open immediately on double click.
                    if (HasExpectedExtension(entry.FullPath))
                    {
                        RequestExecute(entry.FullPath);
                    }
                }
                else
                {
                    // Save/export: fill filename and let user confirm.
                    SetFileName(entry.Name);
                }
            }
        }

        ctx.PopId();
    }

    private static string GetEntryIcon(Entry entry)
    {
        if (entry.IsDirectory)
        {
            return FolderIcon;
        }

        if (entry.Name.EndsWith(".bdui", StringComparison.OrdinalIgnoreCase))
        {
            return FileBduiIcon;
        }
        if (entry.Name.EndsWith(".dui", StringComparison.OrdinalIgnoreCase))
        {
            return FileDuiIcon;
        }

        return FileIcon;
    }

    private static bool IconButton(string id, string label, string tooltip, float x, float y, float w, float h)
    {
        var ctx = Im.Context;
        ctx.PushId(id);
        var rect = new ImRect(x, y, w, h);
        int widgetId = ctx.GetId(id);
        bool hovered = rect.Contains(Im.MousePos);
        ImTooltip.Begin(widgetId, hovered);

        bool pressed = Im.Button(label, x, y, w, h);
        if (ImTooltip.ShouldShow(widgetId))
        {
            ImTooltip.Draw(tooltip);
        }

        ctx.PopId();
        return pressed;
    }

    private static void SelectEntry(int visibleIndex, Entry entry)
    {
        _selectedVisibleIndex = visibleIndex;
        _selectedPath = entry.FullPath;
        if (!entry.IsDirectory && _mode != Mode.Open)
        {
            SetFileName(entry.Name);
        }
    }

    private static void SetFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            _fileNameLength = 0;
            return;
        }
        _fileNameLength = Math.Min(fileName.Length, FileNameBuffer.Length - 1);
        fileName.AsSpan(0, _fileNameLength).CopyTo(FileNameBuffer);
    }

    private static void InitializeStartLocation(UiWorkspace workspace)
    {
        _showCreateFolder = false;
        _showOverwriteConfirm = false;
        _pendingOverwritePath = string.Empty;
        _pendingOverwriteActionLabel = string.Empty;
        _pendingAction = PendingAction.None;
        _pendingActionPath = string.Empty;

        History.Clear();
        _historyIndex = -1;

        string startDir = Environment.CurrentDirectory;
        string? docPath = workspace.DocumentPath;
        if (!string.IsNullOrEmpty(docPath))
        {
            try
            {
                string full = Path.GetFullPath(docPath);
                string? dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    startDir = dir;
                }
            }
            catch
            {
                // Ignore invalid workspace paths.
            }
        }

        NavigateTo(startDir);
        _scrollY = 0f;
        _searchLength = 0;
        _selectedVisibleIndex = -1;
        _selectedPath = string.Empty;
    }

    private static void SetFileNameDefault(UiWorkspace workspace)
    {
        string? path = workspace.DocumentPath;
        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                string name = Path.GetFileName(path);
                if (!string.IsNullOrEmpty(name))
                {
                    SetFileName(name);
                    return;
                }
            }
            catch
            {
                // Ignore.
            }
        }

        SetFileName("workspace");
        _fileNameLength = Math.Min(_fileNameLength, FileNameBuffer.Length - 1);
        string ext = GetExpectedExtension();
        if (_fileNameLength > 0 && ext.Length > 0)
        {
            // Keep without extension; it will be appended on save.
        }
    }

    private static void SetFileNameExportDefault(UiWorkspace workspace)
    {
        _ = workspace;
        SetFileName("prefab");
    }

    private static void Close()
    {
        _mode = Mode.None;
        _errorMessage = string.Empty;
        _showCreateFolder = false;
        _showOverwriteConfirm = false;
        _pendingOverwritePath = string.Empty;
        _pendingOverwriteActionLabel = string.Empty;
        _pendingAction = PendingAction.None;
        _pendingActionPath = string.Empty;
    }

    private static string GetExpectedExtension()
    {
        return _mode switch
        {
            Mode.Open => ".dui",
            Mode.SaveAs => ".dui",
            Mode.ExportBdui => ".bdui",
            _ => string.Empty
        };
    }

    private static bool HasExpectedExtension(string path)
    {
        string ext = GetExpectedExtension();
        if (string.IsNullOrEmpty(ext))
        {
            return true;
        }
        return path.EndsWith(ext, StringComparison.OrdinalIgnoreCase);
    }

    private static void NavigateUp()
    {
        if (string.IsNullOrEmpty(_currentDirectory))
        {
            return;
        }

        try
        {
            string? parent = Path.GetDirectoryName(_currentDirectory);
            if (!string.IsNullOrEmpty(parent))
            {
                NavigateTo(parent);
            }
        }
        catch
        {
            // Ignore.
        }
    }

    private static readonly List<string> History = new(capacity: 32);
    private static int _historyIndex = -1;

    private static void NavigateBack()
    {
        if (_historyIndex <= 0 || _historyIndex >= History.Count)
        {
            return;
        }

        _historyIndex--;
        NavigateTo(History[_historyIndex], pushHistory: false);
    }

    private static void NavigateForward()
    {
        if (_historyIndex < 0 || _historyIndex + 1 >= History.Count)
        {
            return;
        }

        _historyIndex++;
        NavigateTo(History[_historyIndex], pushHistory: false);
    }

    private static void NavigateTo(string directory)
    {
        NavigateTo(directory, pushHistory: true);
    }

    private static void NavigateTo(string directory, bool pushHistory)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        string target = directory;
        try
        {
            target = Path.GetFullPath(directory);
        }
        catch
        {
            // Use as-is.
        }

        if (!Directory.Exists(target))
        {
            _errorMessage = "Directory not found.";
            return;
        }

        if (pushHistory)
        {
            if (_historyIndex + 1 < History.Count)
            {
                History.RemoveRange(_historyIndex + 1, History.Count - (_historyIndex + 1));
            }
            History.Add(target);
            _historyIndex = History.Count - 1;
        }

        _currentDirectory = target;
        _errorMessage = string.Empty;
        _selectedVisibleIndex = -1;
        _selectedPath = string.Empty;
        _scrollY = 0f;

        RefreshDirectory();
    }

    private static void RefreshDirectory()
    {
        Entries.Clear();

        try
        {
            foreach (string dir in Directory.EnumerateDirectories(_currentDirectory))
            {
                string name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }
                Entries.Add(new Entry(name, dir, isDirectory: true));
            }

            foreach (string file in Directory.EnumerateFiles(_currentDirectory))
            {
                string name = Path.GetFileName(file);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }
                Entries.Add(new Entry(name, file, isDirectory: false));
            }

            Entries.Sort(EntrySort);
        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
        }

        RebuildVisibleIndices();
    }

    private static int CompareEntries(Entry left, Entry right)
    {
        if (left.IsDirectory != right.IsDirectory)
        {
            return left.IsDirectory ? -1 : 1;
        }

        return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureVisibleListUpToDate()
    {
        if (_lastSearchLength != _searchLength || _lastShowHiddenFiles != _showHiddenFiles || _lastFilterToExpectedExtension != _filterToExpectedExtension)
        {
            RebuildVisibleIndices();
        }
    }

    private static void RebuildVisibleIndices()
    {
        _lastSearchLength = _searchLength;
        _lastShowHiddenFiles = _showHiddenFiles;
        _lastFilterToExpectedExtension = _filterToExpectedExtension;

        VisibleEntryIndices.Clear();

        ReadOnlySpan<char> search = SearchBuffer.AsSpan(0, _searchLength);
        string expectedExt = GetExpectedExtension();

        for (int i = 0; i < Entries.Count; i++)
        {
            Entry entry = Entries[i];

            if (!_showHiddenFiles && entry.Name.Length > 0 && entry.Name[0] == '.')
            {
                continue;
            }

            if (!entry.IsDirectory)
            {
                if (_filterToExpectedExtension && !string.IsNullOrEmpty(expectedExt) && !entry.Name.EndsWith(expectedExt, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            if (_searchLength > 0 && !ContainsIgnoreCase(entry.Name, search))
            {
                continue;
            }

            VisibleEntryIndices.Add(i);
        }

        _selectedVisibleIndex = -1;
        if (!string.IsNullOrEmpty(_selectedPath))
        {
            for (int visibleIndex = 0; visibleIndex < VisibleEntryIndices.Count; visibleIndex++)
            {
                Entry entry = Entries[VisibleEntryIndices[visibleIndex]];
                if (string.Equals(entry.FullPath, _selectedPath, StringComparison.OrdinalIgnoreCase))
                {
                    _selectedVisibleIndex = visibleIndex;
                    break;
                }
            }
        }
    }

    private static bool ContainsIgnoreCase(string text, ReadOnlySpan<char> needle)
    {
        if (needle.Length == 0)
        {
            return true;
        }

        int textLen = text.Length;
        int needleLen = needle.Length;
        if (needleLen > textLen)
        {
            return false;
        }

        for (int i = 0; i <= textLen - needleLen; i++)
        {
            bool match = true;
            for (int j = 0; j < needleLen; j++)
            {
                char textChar = text[i + j];
                char needleChar = needle[j];
                if (char.ToUpperInvariant(textChar) != char.ToUpperInvariant(needleChar))
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }

    private static void RequestNavigate(string path)
    {
        _pendingAction = PendingAction.Navigate;
        _pendingActionPath = path;
    }

    private static void RequestExecute(string path)
    {
        _pendingAction = PendingAction.Execute;
        _pendingActionPath = path;
    }

    private static void ApplyPendingAction(UiWorkspace workspace)
    {
        if (_pendingAction == PendingAction.None || string.IsNullOrEmpty(_pendingActionPath))
        {
            return;
        }

        PendingAction action = _pendingAction;
        string path = _pendingActionPath;
        _pendingAction = PendingAction.None;
        _pendingActionPath = string.Empty;

        if (action == PendingAction.Navigate)
        {
            NavigateTo(path);
        }
        else if (action == PendingAction.Execute)
        {
            TryExecute(workspace, path);
        }
    }

    private static string GetBreadcrumbTargetPath(string path, char separator, int clickedIndex)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        int segmentIndex = 0;
        int lastEnd = -1;
        int start = 0;

        // Skip leading separators.
        while (start < path.Length && path[start] == separator)
        {
            start++;
        }

        for (int i = start; i <= path.Length; i++)
        {
            bool atEnd = i == path.Length;
            if (atEnd || path[i] == separator)
            {
                int segLen = i - start;
                if (segLen > 0)
                {
                    if (segmentIndex == clickedIndex)
                    {
                        lastEnd = i;
                        break;
                    }
                    segmentIndex++;
                }
                start = i + 1;
            }
        }

        if (lastEnd <= 0)
        {
            return string.Empty;
        }

        // Preserve leading separator if present (POSIX root).
        bool hasLeadingSep = path.Length > 0 && path[0] == separator;
        if (hasLeadingSep)
        {
            return separator + path.Substring(1, lastEnd - 1);
        }

        return path.Substring(0, lastEnd);
    }

    private static void TryCreateFolder()
    {
        string name = new string(NewFolderBuffer, 0, _newFolderLength).Trim();
        if (name.Length == 0)
        {
            _errorMessage = "Folder name is required.";
            return;
        }

        if (name.IndexOf(Path.DirectorySeparatorChar) >= 0 || name.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
        {
            _errorMessage = "Folder name must not contain path separators.";
            return;
        }

        string target = Path.Combine(_currentDirectory, name);
        try
        {
            Directory.CreateDirectory(target);
            _showCreateFolder = false;
            RefreshDirectory();
        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
        }
    }

    private static void TryExecute(UiWorkspace workspace, string path)
    {
        try
        {
            if (_mode == Mode.Open)
            {
                UiDocumentSerializer.LoadFromFile(workspace, path);
                workspace.SetDocumentPath(path);
                workspace.TrackRecentDocumentPath(path);
                workspace.ShowToast("Opened");
                Close();
            }
            else if (_mode == Mode.SaveAs)
            {
                UiDocumentSerializer.SaveToFile(workspace, path);
                workspace.SetDocumentPath(path);
                workspace.TrackRecentDocumentPath(path);
                workspace.ShowToast("Saved");
                Close();
            }
            else if (_mode == Mode.ExportBdui)
            {
                BduiExporter.ExportWorkspace(workspace, path);
                workspace.ShowToast("Exported");
                Close();
            }
        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
        }
    }
}
