using DerpLib.ImGui;

namespace Derp.UI;

internal static class MainMenuBar
{
    public static void Draw(UiWorkspace workspace)
    {
        if (!Im.BeginMainMenuBar())
        {
            return;
        }

        DrawFileMenu(workspace);
        DrawEditMenu(workspace);
        DrawWindowsMenu(workspace);
        DrawHelpMenu(workspace);

        Im.EndMainMenuBar();

        DocumentIoDialog.Draw(workspace);
        ConstraintTargetPickerDialog.Draw(workspace);
        EntityReferenceDragDropPreviewWidget.Draw(workspace);
    }

    private static void DrawFileMenu(UiWorkspace workspace)
    {
        if (!Im.BeginMenu("File"))
        {
            return;
        }

        DrawCommand(workspace, EditorCommandId.FileNew, "New", "Ctrl+N");
        DrawCommand(workspace, EditorCommandId.FileOpen, "Open…", "Ctrl+O");
        DrawRecentFilesMenu(workspace);
        Im.Separator();
        DrawCommand(workspace, EditorCommandId.FileSave, "Save", "Ctrl+S");
        DrawCommand(workspace, EditorCommandId.FileSaveAs, "Save As…", "Ctrl+Shift+S");
        Im.Separator();
        DrawCommand(workspace, EditorCommandId.FileExportBdui, "Export Runtime (.bdui)…");
        Im.Separator();
        DrawCommand(workspace, EditorCommandId.FileExit, "Exit");

        Im.EndMenu();
    }

    private static void DrawRecentFilesMenu(UiWorkspace workspace)
    {
        if (!Im.BeginMenu("Recent Files"))
        {
            return;
        }

        IReadOnlyList<string> recentDocumentPaths = workspace.GetRecentDocumentPaths();
        bool hasRecentFiles = recentDocumentPaths.Count > 0;
        if (!hasRecentFiles)
        {
            _ = Im.MenuItem("(none)", enabled: false);
        }
        else
        {
            for (int pathIndex = 0; pathIndex < recentDocumentPaths.Count; pathIndex++)
            {
                string recentPath = recentDocumentPaths[pathIndex];
                Im.Context.PushId(pathIndex);
                bool selected = Im.MenuItem(recentPath);
                Im.Context.PopId();
                if (selected)
                {
                    _ = workspace.TryOpenRecentDocument(recentPath, out _);
                }
            }
        }

        Im.Separator();
        bool clearSelected = Im.MenuItem("Clear Recent Files", enabled: hasRecentFiles);
        if (clearSelected)
        {
            workspace.ClearRecentDocuments();
        }

        Im.EndMenu();
    }

    private static void DrawEditMenu(UiWorkspace workspace)
    {
        if (!Im.BeginMenu("Edit"))
        {
            return;
        }

        DrawCommand(workspace, EditorCommandId.EditUndo, "Undo", "Ctrl+Z");
        DrawCommand(workspace, EditorCommandId.EditRedo, "Redo", "Ctrl+Shift+Z");
        Im.Separator();
        DrawCommand(workspace, EditorCommandId.EditCut, "Cut", "Ctrl+X");
        DrawCommand(workspace, EditorCommandId.EditCopy, "Copy", "Ctrl+C");
        DrawCommand(workspace, EditorCommandId.EditPaste, "Paste", "Ctrl+V");
        DrawCommand(workspace, EditorCommandId.EditDuplicate, "Duplicate", "Ctrl+D");
        Im.Separator();
        DrawCommand(workspace, EditorCommandId.EditDelete, "Delete", "Del");

        Im.EndMenu();
    }

    private static void DrawWindowsMenu(UiWorkspace workspace)
    {
        if (!Im.BeginMenu("Windows"))
        {
            return;
        }

        DrawWindowToggle(workspace, EditorCommandId.WindowLayers, "Layers");
        DrawWindowToggle(workspace, EditorCommandId.WindowCanvas, "Canvas");
        DrawWindowToggle(workspace, EditorCommandId.WindowInspector, "Inspector");
        DrawWindowToggle(workspace, EditorCommandId.WindowVariables, "Variables");
        DrawWindowToggle(workspace, EditorCommandId.WindowAnimations, "Animations");
        DrawWindowToggle(workspace, EditorCommandId.WindowTools, "Tools");

        Im.EndMenu();
    }

    private static void DrawHelpMenu(UiWorkspace workspace)
    {
        if (!Im.BeginMenu("Help"))
        {
            return;
        }

        DrawCommand(workspace, EditorCommandId.HelpAbout, "About");
        Im.EndMenu();
    }

    private static void DrawWindowToggle(UiWorkspace workspace, EditorCommandId id, string label)
    {
        bool open = EditorCommands.IsChecked(id);
        if (Im.MenuItem(label, string.Empty, ref open))
        {
            EditorCommands.Execute(workspace, id);
        }
    }

    private static void DrawCommand(UiWorkspace workspace, EditorCommandId id, string label, string shortcut = "")
    {
        bool enabled = EditorCommands.IsEnabled(workspace, id);
        if (Im.MenuItem(label, shortcut, enabled))
        {
            EditorCommands.Execute(workspace, id);
        }
    }
}
