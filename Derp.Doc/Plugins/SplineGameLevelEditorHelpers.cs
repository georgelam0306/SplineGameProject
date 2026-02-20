using Derp.Doc.Commands;
using Derp.Doc.Editor;
using Derp.Doc.Model;

namespace Derp.Doc.Plugins;

internal static class SplineGameLevelEditorHelpers
{
    public static bool IsSplineGameLevelTable(DocTable table)
    {
        return string.Equals(table.PluginTableTypeId, SplineGameLevelIds.TableTypeId, StringComparison.Ordinal);
    }

    public static bool EnsureLevelEditorView(DocWorkspace workspace, DocTable table, out DocView view)
    {
        for (int viewIndex = 0; viewIndex < table.Views.Count; viewIndex++)
        {
            DocView candidateView = table.Views[viewIndex];
            if (candidateView.Type == DocViewType.Custom &&
                string.Equals(candidateView.CustomRendererId, SplineGameLevelIds.LevelEditorRendererId, StringComparison.Ordinal))
            {
                view = candidateView;
                return true;
            }
        }

        var createdView = new DocView
        {
            Name = SplineGameLevelIds.LevelEditorViewName,
            Type = DocViewType.Custom,
            CustomRendererId = SplineGameLevelIds.LevelEditorRendererId,
        };

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.AddView,
            TableId = table.Id,
            ViewIndex = table.Views.Count,
            ViewSnapshot = createdView,
        });

        if (table.Views.Count <= 0)
        {
            view = null!;
            return false;
        }

        view = table.Views[table.Views.Count - 1];
        return true;
    }

    public static bool TryGetColumnById(DocTable table, string columnId, out DocColumn column)
    {
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            if (string.Equals(table.Columns[columnIndex].Id, columnId, StringComparison.Ordinal))
            {
                column = table.Columns[columnIndex];
                return true;
            }
        }

        column = null!;
        return false;
    }

    public static bool TryFindRowById(DocTable table, string rowId, out DocRow row)
    {
        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            if (string.Equals(table.Rows[rowIndex].Id, rowId, StringComparison.Ordinal))
            {
                row = table.Rows[rowIndex];
                return true;
            }
        }

        row = null!;
        return false;
    }

    public static bool TryFindRowIndexById(DocTable table, string rowId, out int rowIndex)
    {
        for (int tableRowIndex = 0; tableRowIndex < table.Rows.Count; tableRowIndex++)
        {
            if (string.Equals(table.Rows[tableRowIndex].Id, rowId, StringComparison.Ordinal))
            {
                rowIndex = tableRowIndex;
                return true;
            }
        }

        rowIndex = -1;
        return false;
    }
}
