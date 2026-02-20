using System;
using DerpLib.ImGui;
using DerpLib.ImGui.Windows;

namespace Derp.UI;

internal static class EditorCommands
{
    public static bool IsEnabled(UiWorkspace workspace, EditorCommandId id)
    {
        switch (id)
        {
            case EditorCommandId.EditUndo:
                return workspace.Commands.CanUndo;
            case EditorCommandId.EditRedo:
                return workspace.Commands.CanRedo;
            case EditorCommandId.EditCopy:
            case EditorCommandId.EditCut:
            case EditorCommandId.EditDelete:
            case EditorCommandId.EditDuplicate:
                return workspace._selectedEntities.Count > 0 || !workspace._selectedPrefabEntity.IsNull;
            case EditorCommandId.EditPaste:
                return true;
            case EditorCommandId.FileExit:
                return true;
            case EditorCommandId.WindowLayers:
            case EditorCommandId.WindowCanvas:
            case EditorCommandId.WindowInspector:
            case EditorCommandId.WindowVariables:
            case EditorCommandId.WindowAnimations:
            case EditorCommandId.WindowTools:
                return true;
            case EditorCommandId.HelpAbout:
                return true;
            case EditorCommandId.FileNew:
            case EditorCommandId.FileOpen:
            case EditorCommandId.FileSave:
            case EditorCommandId.FileSaveAs:
                return true;
            case EditorCommandId.FileExportBdui:
                return workspace.HasActivePrefab;
            default:
                return false;
        }
    }

    public static bool IsChecked(EditorCommandId id)
    {
        string? windowTitle = GetWindowTitle(id);
        if (windowTitle == null)
        {
            return false;
        }

        ImWindow? window = Im.WindowManager.FindWindow(windowTitle);
        return window != null && window.IsOpen;
    }

    public static void Execute(UiWorkspace workspace, EditorCommandId id)
    {
        switch (id)
        {
            case EditorCommandId.EditUndo:
                workspace.Commands.Undo();
                return;
            case EditorCommandId.EditRedo:
                workspace.Commands.Redo();
                return;
            case EditorCommandId.EditCopy:
                if (workspace.Commands.CopySelectionToClipboard())
                {
                    workspace.ShowToast("Copied");
                }
                return;
            case EditorCommandId.EditPaste:
                if (!workspace.Commands.PasteClipboardAtCursor())
                {
                    workspace.ShowToast("Nothing to paste");
                }
                return;
            case EditorCommandId.EditDuplicate:
                if (!workspace.Commands.DuplicateSelectionAsSiblings())
                {
                    workspace.ShowToast("Nothing to duplicate");
                }
                return;
            case EditorCommandId.EditDelete:
                workspace.Commands.DeleteSelectedLayers();
                return;
            case EditorCommandId.EditCut:
                if (workspace.Commands.CopySelectionToClipboard())
                {
                    workspace.Commands.DeleteSelectedLayers();
                    workspace.ShowToast("Cut");
                }
                return;
            case EditorCommandId.WindowLayers:
            case EditorCommandId.WindowCanvas:
            case EditorCommandId.WindowInspector:
            case EditorCommandId.WindowVariables:
            case EditorCommandId.WindowAnimations:
            case EditorCommandId.WindowTools:
                ToggleWindow(id);
                return;
            case EditorCommandId.HelpAbout:
                workspace.ShowToast("Derp.UI");
                return;
            case EditorCommandId.FileExit:
                // Not wired to engine quit yet.
                workspace.ShowToast("Exit not implemented");
                return;
            case EditorCommandId.FileNew:
                workspace.ResetDocumentForLoad();
                workspace.ShowToast("New document");
                return;
            case EditorCommandId.FileOpen:
                DocumentIoDialog.RequestOpen(workspace);
                return;
            case EditorCommandId.FileSave:
                TrySave(workspace);
                return;
            case EditorCommandId.FileSaveAs:
                DocumentIoDialog.RequestSaveAs(workspace);
                return;
            case EditorCommandId.FileExportBdui:
                DocumentIoDialog.RequestExportBdui(workspace);
                return;
            default:
                return;
        }
    }

    private static void TrySave(UiWorkspace workspace)
    {
        string? path = workspace.DocumentPath;
        if (string.IsNullOrEmpty(path))
        {
            DocumentIoDialog.RequestSaveAs(workspace);
            return;
        }

        try
        {
            UiDocumentSerializer.SaveToFile(workspace, path);
            workspace.TrackRecentDocumentPath(path);
            workspace.ShowToast("Saved");
        }
        catch (Exception ex)
        {
            workspace.ShowToast(ex.Message);
        }
    }

    private static void ToggleWindow(EditorCommandId id)
    {
        string? title = GetWindowTitle(id);
        if (title == null)
        {
            return;
        }

        ImWindow? window = Im.WindowManager.FindWindow(title);
        if (window == null)
        {
            return;
        }

        window.IsOpen = !window.IsOpen;
    }

    private static string? GetWindowTitle(EditorCommandId id)
    {
        switch (id)
        {
            case EditorCommandId.WindowLayers:
                return "Layers";
            case EditorCommandId.WindowCanvas:
                return "Canvas";
            case EditorCommandId.WindowInspector:
                return "Inspector";
            case EditorCommandId.WindowVariables:
                return "Variables";
            case EditorCommandId.WindowAnimations:
                return "Animations";
            case EditorCommandId.WindowTools:
                return "Tools";
            default:
                return null;
        }
    }
}
