using System.Text.Json;
using System.Collections.Generic;

namespace Derp.Doc.Storage;

public static class DocActiveProjectStateFile
{
    public const string DefaultFileName = ".derpdoc-active.json";

    public sealed class ContentTabState
    {
        public string TabInstanceId = "";
        public int WindowId;
        public string Kind = "";
        public string TargetId = "";
        public string ActiveViewId = "";
        public string? ParentRowId;
        public int FocusedBlockIndex = -1;
    }

    public sealed class ActiveProjectState
    {
        public string DbRoot = "";
        public string? GameRoot;
        public string? ProjectName;
        public int NextContentTabWindowId;
        public string ActiveContentTabInstanceId = "";
        public readonly List<ContentTabState> OpenContentTabs = new();
    }

    public static string GetPath(string workspaceRoot, string fileName = DefaultFileName)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            workspaceRoot = Directory.GetCurrentDirectory();
        }
        return Path.Combine(Path.GetFullPath(workspaceRoot), fileName);
    }

    public static bool TryReadDbRoot(string workspaceRoot, out string dbRoot, string fileName = DefaultFileName)
    {
        dbRoot = "";

        string path = GetPath(workspaceRoot, fileName);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!doc.RootElement.TryGetProperty("dbRoot", out var p) || p.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var value = p.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            dbRoot = value;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryReadState(string workspaceRoot, out ActiveProjectState state, string fileName = DefaultFileName)
    {
        state = new ActiveProjectState();

        string path = GetPath(workspaceRoot, fileName);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (doc.RootElement.TryGetProperty("dbRoot", out var dbRootElement) &&
                dbRootElement.ValueKind == JsonValueKind.String)
            {
                state.DbRoot = dbRootElement.GetString() ?? "";
            }

            if (string.IsNullOrWhiteSpace(state.DbRoot))
            {
                return false;
            }

            if (doc.RootElement.TryGetProperty("gameRoot", out var gameRootElement) &&
                gameRootElement.ValueKind == JsonValueKind.String)
            {
                var value = gameRootElement.GetString();
                state.GameRoot = string.IsNullOrWhiteSpace(value) ? null : value;
            }

            if (doc.RootElement.TryGetProperty("projectName", out var projectNameElement) &&
                projectNameElement.ValueKind == JsonValueKind.String)
            {
                var value = projectNameElement.GetString();
                state.ProjectName = string.IsNullOrWhiteSpace(value) ? null : value;
            }

            if (doc.RootElement.TryGetProperty("nextContentTabWindowId", out var nextWindowIdElement) &&
                nextWindowIdElement.ValueKind == JsonValueKind.Number &&
                nextWindowIdElement.TryGetInt32(out int nextWindowId))
            {
                state.NextContentTabWindowId = nextWindowId;
            }

            if (doc.RootElement.TryGetProperty("activeContentTabInstanceId", out var activeTabInstanceIdElement) &&
                activeTabInstanceIdElement.ValueKind == JsonValueKind.String)
            {
                state.ActiveContentTabInstanceId = activeTabInstanceIdElement.GetString() ?? "";
            }

            if (doc.RootElement.TryGetProperty("openContentTabs", out var openTabsElement) &&
                openTabsElement.ValueKind == JsonValueKind.Array)
            {
                int count = openTabsElement.GetArrayLength();
                for (int i = 0; i < count; i++)
                {
                    var element = openTabsElement[i];
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var tab = new ContentTabState();
                    if (element.TryGetProperty("tabInstanceId", out var tabInstanceIdElement) &&
                        tabInstanceIdElement.ValueKind == JsonValueKind.String)
                    {
                        tab.TabInstanceId = tabInstanceIdElement.GetString() ?? "";
                    }
                    if (element.TryGetProperty("windowId", out var windowIdElement) &&
                        windowIdElement.ValueKind == JsonValueKind.Number &&
                        windowIdElement.TryGetInt32(out int windowId))
                    {
                        tab.WindowId = windowId;
                    }
                    if (element.TryGetProperty("kind", out var kindElement) &&
                        kindElement.ValueKind == JsonValueKind.String)
                    {
                        tab.Kind = kindElement.GetString() ?? "";
                    }
                    if (element.TryGetProperty("targetId", out var targetIdElement) &&
                        targetIdElement.ValueKind == JsonValueKind.String)
                    {
                        tab.TargetId = targetIdElement.GetString() ?? "";
                    }
                    if (element.TryGetProperty("activeViewId", out var activeViewIdElement) &&
                        activeViewIdElement.ValueKind == JsonValueKind.String)
                    {
                        tab.ActiveViewId = activeViewIdElement.GetString() ?? "";
                    }
                    if (element.TryGetProperty("parentRowId", out var parentRowIdElement) &&
                        parentRowIdElement.ValueKind == JsonValueKind.String)
                    {
                        var value = parentRowIdElement.GetString();
                        tab.ParentRowId = string.IsNullOrWhiteSpace(value) ? null : value;
                    }
                    if (element.TryGetProperty("focusedBlockIndex", out var focusedBlockIndexElement) &&
                        focusedBlockIndexElement.ValueKind == JsonValueKind.Number &&
                        focusedBlockIndexElement.TryGetInt32(out int focusedBlockIndex))
                    {
                        tab.FocusedBlockIndex = focusedBlockIndex;
                    }

                    if (!string.IsNullOrWhiteSpace(tab.TabInstanceId) &&
                        tab.WindowId != 0 &&
                        !string.IsNullOrWhiteSpace(tab.Kind) &&
                        !string.IsNullOrWhiteSpace(tab.TargetId))
                    {
                        state.OpenContentTabs.Add(tab);
                    }
                }
            }

            return true;
        }
        catch
        {
            state = new ActiveProjectState();
            return false;
        }
    }

    public static bool TryReadDbRootSearchingUp(string startDirectory, out string dbRoot, out string foundAtDirectory, string fileName = DefaultFileName)
    {
        dbRoot = "";
        foundAtDirectory = "";

        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            startDirectory = Directory.GetCurrentDirectory();
        }

        string dir = Path.GetFullPath(startDirectory);
        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (TryReadDbRoot(dir, out dbRoot, fileName))
            {
                foundAtDirectory = dir;
                return true;
            }

            var parent = Directory.GetParent(dir);
            if (parent == null)
            {
                break;
            }

            dir = parent.FullName;
        }

        return false;
    }

    public static void Write(string workspaceRoot, string dbRoot, string? gameRoot, string projectName, string fileName = DefaultFileName)
    {
        if (string.IsNullOrWhiteSpace(dbRoot))
        {
            return;
        }

        string path = GetPath(workspaceRoot, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        string tmpPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        var payload = new
        {
            dbRoot = Path.GetFullPath(dbRoot),
            gameRoot = string.IsNullOrWhiteSpace(gameRoot) ? null : Path.GetFullPath(gameRoot),
            projectName = string.IsNullOrWhiteSpace(projectName) ? null : projectName,
            updatedAtUtc = DateTime.UtcNow.ToString("O"),
        };

        try
        {
            File.WriteAllText(tmpPath, JsonSerializer.Serialize(payload));
            File.Move(tmpPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmpPath))
            {
                File.Delete(tmpPath);
            }
        }
    }

    public static void WriteState(string workspaceRoot, ActiveProjectState state, string fileName = DefaultFileName)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.DbRoot))
        {
            return;
        }

        string path = GetPath(workspaceRoot, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        string tmpPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        var openTabsPayload = new List<object>(state.OpenContentTabs.Count);
        for (int tabIndex = 0; tabIndex < state.OpenContentTabs.Count; tabIndex++)
        {
            ContentTabState tab = state.OpenContentTabs[tabIndex];
            openTabsPayload.Add(new
            {
                tabInstanceId = tab.TabInstanceId,
                windowId = tab.WindowId,
                kind = tab.Kind,
                targetId = tab.TargetId,
                activeViewId = tab.ActiveViewId,
                parentRowId = tab.ParentRowId,
                focusedBlockIndex = tab.FocusedBlockIndex,
            });
        }

        var payload = new
        {
            dbRoot = Path.GetFullPath(state.DbRoot),
            gameRoot = string.IsNullOrWhiteSpace(state.GameRoot) ? null : Path.GetFullPath(state.GameRoot),
            projectName = string.IsNullOrWhiteSpace(state.ProjectName) ? null : state.ProjectName,
            nextContentTabWindowId = state.NextContentTabWindowId,
            activeContentTabInstanceId = string.IsNullOrWhiteSpace(state.ActiveContentTabInstanceId) ? null : state.ActiveContentTabInstanceId,
            openContentTabs = openTabsPayload.Count > 0 ? openTabsPayload : null,
            updatedAtUtc = DateTime.UtcNow.ToString("O"),
        };

        try
        {
            File.WriteAllText(tmpPath, JsonSerializer.Serialize(payload));
            File.Move(tmpPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmpPath))
            {
                File.Delete(tmpPath);
            }
        }
    }
}
