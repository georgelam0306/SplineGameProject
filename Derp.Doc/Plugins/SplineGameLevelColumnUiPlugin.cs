using Derp.Doc.Editor;
using Derp.Doc.Model;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;

namespace Derp.Doc.Plugins;

public sealed class SplineGameLevelColumnUiPlugin : DerpDocColumnUiPluginBase
{
    public override string ColumnTypeId => SplineGameLevelIds.ColumnTypeId;

    public override bool DrawCell(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocRow row,
        int sourceRowIndex,
        DocColumn column,
        DocCellValue cell,
        ImRect cellRect,
        ImStyle style)
    {
        float badgeX = cellRect.X + 8f;
        float badgeY = cellRect.Y + 5f;
        float badgeWidth = MathF.Max(72f, Im.MeasureTextWidth("Edit Level".AsSpan(), style.FontSize - 1f) + 16f);
        float badgeHeight = MathF.Max(16f, style.FontSize + 2f);
        uint badgeColor = ImStyle.WithAlpha(style.Primary, 165);
        Im.DrawRoundedRect(badgeX, badgeY, badgeWidth, badgeHeight, 4f, badgeColor);
        Im.DrawRoundedRectStroke(badgeX, badgeY, badgeWidth, badgeHeight, 4f, style.Border, 1f);
        Im.Text("Edit Level".AsSpan(), badgeX + 8f, badgeY + 1f, style.FontSize - 1f, 0xFFFFFFFF);
        return true;
    }

    public override bool OnCellActivated(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocRow row,
        int sourceRowIndex,
        int displayRowIndex,
        int displayColumnIndex,
        DocColumn column,
        DocCellValue cell)
    {
        if (workspace is not DocWorkspace mutableWorkspace)
        {
            return false;
        }

        if (column.Kind != DocColumnKind.Subtable || string.IsNullOrWhiteSpace(column.SubtableId))
        {
            return false;
        }

        DocTable? childTable = null;
        for (int tableIndex = 0; tableIndex < mutableWorkspace.Project.Tables.Count; tableIndex++)
        {
            DocTable candidateTable = mutableWorkspace.Project.Tables[tableIndex];
            if (string.Equals(candidateTable.Id, column.SubtableId, StringComparison.Ordinal))
            {
                childTable = candidateTable;
                break;
            }
        }

        if (childTable == null || !SplineGameLevelEditorHelpers.IsSplineGameLevelTable(childTable))
        {
            return false;
        }

        bool hasLevelEditorView = SplineGameLevelEditorHelpers.EnsureLevelEditorView(
            mutableWorkspace,
            childTable,
            out DocView levelEditorView);
        mutableWorkspace.ContentTabs.OpenOrFocusTableFromNavigation(childTable.Id, row.Id);
        mutableWorkspace.ActiveView = ActiveViewKind.Table;
        mutableWorkspace.ActiveTable = childTable;
        mutableWorkspace.ActiveParentRowId = row.Id;
        if (hasLevelEditorView)
        {
            mutableWorkspace.ActiveTableView = levelEditorView;
        }

        return true;
    }
}
