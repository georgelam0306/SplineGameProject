using Derp.Doc.Editor;
using Derp.Doc.Export;
using Derp.Doc.Plugins;
using DerpLib.ImGui;
using DerpLib.ImGui.Widgets;

namespace Derp.Doc.Panels;

internal static class MainMenuPanel
{
    private static bool _writeManifest = true;
    private static readonly List<IDerpDocAutomationProvider> _automationProvidersScratch = new();

    private static string _cachedProjectLine = "Project: (none)";
    private static string _cachedStatusLine = "";
    private static string _cachedPerfLine = "";
    private static string _lastProjectPath = "";
    private static string _lastStatus = "";
    private static long _lastPerfCommandOperationCount = -1;
    private static long _lastPerfFormulaRecalculationCount = -1;
    private static long _lastPerfAutoSaveCount = -1;

    public static void Draw(DocWorkspace workspace)
    {
        if (!Im.BeginMainMenuBar())
        {
            return;
        }

        UpdateCachedStatusLines(workspace);

        if (Im.BeginMenu("File"))
        {
            if (Im.MenuItem("Open..."))
            {
                ProjectIoDialog.RequestOpenProject(workspace);
            }

            if (Im.BeginMenu("Recent Projects"))
            {
                var recentProjectPaths = workspace.GetRecentProjectPaths();
                bool hasRecentProjects = recentProjectPaths.Count > 0;
                if (!hasRecentProjects)
                {
                    ImContextMenu.ItemDisabled("(none)");
                }
                else
                {
                    for (int projectIndex = 0; projectIndex < recentProjectPaths.Count; projectIndex++)
                    {
                        string projectPath = recentProjectPaths[projectIndex];
                        Im.Context.PushId(projectIndex);
                        bool selected = Im.MenuItem(projectPath);
                        Im.Context.PopId();
                        if (selected)
                        {
                            workspace.TryOpenRecentProject(projectPath, out _);
                        }
                    }
                }

                Im.Separator();
                bool cleared = Im.MenuItem("Clear Recent Projects", enabled: hasRecentProjects);
                if (cleared)
                {
                    workspace.ClearRecentProjects();
                }

                Im.EndMenu();
            }

            if (Im.MenuItem("New Game..."))
            {
                ProjectIoDialog.RequestNewGame(workspace);
            }

            Im.Separator();

            bool canSave = !string.IsNullOrWhiteSpace(workspace.ProjectPath);
            if (Im.MenuItem("Save", shortcut: "Ctrl+S", enabled: canSave))
            {
                workspace.CommitTableCellEditIfActive();
                workspace.SaveProject(workspace.ProjectPath!);
            }

            if (Im.MenuItem("Save As..."))
            {
                ProjectIoDialog.RequestSaveAsDbRoot(workspace);
            }

            Im.Separator();

            if (Im.MenuItem("Exit"))
            {
                // Window close is handled by the OS close button.
            }

            Im.EndMenu();
        }

        if (Im.BeginMenu("Project"))
        {
            bool autoSave = workspace.AutoSave;
            if (Im.MenuItem("Auto-save", shortcut: "", ref autoSave))
            {
                workspace.AutoSave = autoSave;
            }

            bool canStartGame = workspace.CanStartGame();
            if (Im.MenuItem("Start Game", shortcut: "Ctrl+R", enabled: canStartGame))
            {
                _ = workspace.TryStartGame(out _);
            }

            Im.Separator();

            ImContextMenu.ItemDisabled(_cachedProjectLine);
            if (!string.IsNullOrWhiteSpace(_cachedStatusLine))
            {
                ImContextMenu.ItemDisabled(_cachedStatusLine);
            }
            if (!string.IsNullOrWhiteSpace(_cachedPerfLine))
            {
                ImContextMenu.ItemDisabled(_cachedPerfLine);
            }

            Im.EndMenu();
        }

        if (Im.BeginMenu("Edit"))
        {
            bool canUndo = workspace.UndoStack.CanUndo;
            if (Im.MenuItem("Undo", shortcut: "Ctrl+Z", enabled: canUndo))
            {
                if (workspace.ActiveView == ActiveViewKind.Table)
                {
                    workspace.CancelTableCellEditIfActive();
                }

                workspace.Undo();
            }

            bool canRedo = workspace.UndoStack.CanRedo;
            if (Im.MenuItem("Redo", shortcut: "Ctrl+Shift+Z", enabled: canRedo))
            {
                if (workspace.ActiveView == ActiveViewKind.Table)
                {
                    workspace.CancelTableCellEditIfActive();
                }

                workspace.Redo();
            }

            Im.Separator();

            bool showPreferences = workspace.ShowPreferences;
            if (Im.MenuItem("Preferences", shortcut: "Ctrl+,", ref showPreferences))
            {
                workspace.ShowPreferences = showPreferences;
            }

            Im.EndMenu();
        }

        if (Im.BeginMenu("Export"))
        {
            bool canExport = !string.IsNullOrWhiteSpace(workspace.ProjectPath);
            if (Im.MenuItem("Export Now", shortcut: "Ctrl+E", enabled: canExport))
            {
                workspace.CommitTableCellEditIfActive();
                _ = workspace.TryExportActiveProject(_writeManifest, out ExportPipelineResult? _);
            }

            if (Im.MenuItem("Write manifest", shortcut: "", ref _writeManifest))
            {
            }

            Im.EndMenu();
        }

        if (Im.BeginMenu("Plugins"))
        {
            if (Im.MenuItem("Reload Project Plugins"))
            {
                workspace.ReloadPluginsForActiveProject();
            }

            Im.Separator();
            ImContextMenu.ItemDisabled($"Loaded: {workspace.GetLoadedPluginCount()}");
            string pluginLoadMessage = workspace.GetPluginLoadMessage();
            if (!string.IsNullOrWhiteSpace(pluginLoadMessage))
            {
                ImContextMenu.ItemDisabled(pluginLoadMessage);
            }

            Im.Separator();
            if (Im.BeginMenu("Automation"))
            {
                PluginAutomationProviderRegistry.CopyProviders(_automationProvidersScratch);
                if (_automationProvidersScratch.Count <= 0)
                {
                    ImContextMenu.ItemDisabled("(none)");
                }
                else
                {
                    for (int providerIndex = 0; providerIndex < _automationProvidersScratch.Count; providerIndex++)
                    {
                        var provider = _automationProvidersScratch[providerIndex];
                        if (!Im.MenuItem(provider.DisplayName))
                        {
                            continue;
                        }

                        bool success = provider.Execute(workspace, out string statusMessage);
                        if (string.IsNullOrWhiteSpace(statusMessage))
                        {
                            statusMessage = success
                                ? "Automation action completed: " + provider.DisplayName
                                : "Automation action failed: " + provider.DisplayName;
                        }

                        workspace.SetStatusMessage(statusMessage);
                    }
                }

                Im.EndMenu();
            }

            Im.EndMenu();
        }

        Im.EndMainMenuBar();
    }

    private static void UpdateCachedStatusLines(DocWorkspace workspace)
    {
        string projectPath = workspace.ProjectPath ?? "";
        if (!string.Equals(_lastProjectPath, projectPath, StringComparison.Ordinal))
        {
            _lastProjectPath = projectPath;
            _cachedProjectLine = string.IsNullOrWhiteSpace(projectPath) ? "Project: (none)" : "Project: " + projectPath;
        }

        string status = workspace.LastStatusMessage ?? "";
        if (!string.Equals(_lastStatus, status, StringComparison.Ordinal))
        {
            _lastStatus = status;
            _cachedStatusLine = string.IsNullOrWhiteSpace(status) ? "" : "Status: " + status;
        }

        var perf = workspace.GetPerformanceCounters();
        if (_lastPerfCommandOperationCount != perf.CommandOperationCount ||
            _lastPerfFormulaRecalculationCount != perf.FormulaRecalculationCount ||
            _lastPerfAutoSaveCount != perf.AutoSaveCount)
        {
            _lastPerfCommandOperationCount = perf.CommandOperationCount;
            _lastPerfFormulaRecalculationCount = perf.FormulaRecalculationCount;
            _lastPerfAutoSaveCount = perf.AutoSaveCount;
            _cachedPerfLine =
                "Perf: Cmd avg " + perf.CommandAverageMilliseconds.ToString("0.00") + "ms max " + perf.CommandMaxMilliseconds.ToString("0.00") + "ms (n " + perf.CommandOperationCount.ToString() + ")" +
                ", Formula avg " + perf.FormulaRecalculationAverageMilliseconds.ToString("0.00") + "ms max " + perf.FormulaRecalculationMaxMilliseconds.ToString("0.00") + "ms (n " + perf.FormulaRecalculationCount.ToString() + ")" +
                " [inc " + perf.FormulaIncrementalCount.ToString() + "/full " + perf.FormulaFullCount.ToString() + "]" +
                ", phases c " + perf.FormulaCompileAverageMilliseconds.ToString("0.00") + "/" + perf.FormulaCompileMaxMilliseconds.ToString("0.00") +
                " p " + perf.FormulaPlanAverageMilliseconds.ToString("0.00") + "/" + perf.FormulaPlanMaxMilliseconds.ToString("0.00") +
                " d " + perf.FormulaDerivedAverageMilliseconds.ToString("0.00") + "/" + perf.FormulaDerivedMaxMilliseconds.ToString("0.00") +
                " e " + perf.FormulaEvaluateAverageMilliseconds.ToString("0.00") + "/" + perf.FormulaEvaluateMaxMilliseconds.ToString("0.00") +
                ", Save avg " + perf.AutoSaveAverageMilliseconds.ToString("0.00") + "ms max " + perf.AutoSaveMaxMilliseconds.ToString("0.00") + "ms (n " + perf.AutoSaveCount.ToString() + ")";
        }
    }
}
