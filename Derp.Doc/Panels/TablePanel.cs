using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using Derp.Doc.Editor;
using Derp.Doc.Model;

namespace Derp.Doc.Panels;

internal static class TablePanel
{
    private const float ContentPaddingRight = 8f;

    public static void Draw(DocWorkspace workspace)
    {
        if (workspace.ActiveTable == null)
        {
            Im.Text("No table selected.".AsSpan(), Im.WindowContentRect.X + 10, Im.WindowContentRect.Y + 10, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }

        string stateKey = workspace.ContentTabs.ActiveTab?.StateKey ?? "";

        var raw = Im.WindowContentRect;
        var viewSwitcherRect = raw;
        var contentRect = new ImRect(raw.X, raw.Y, raw.Width - ContentPaddingRight, raw.Height);

        // Draw the view switcher bar above content (always visible so users can add views)
        float contentY = ViewSwitcherBar.Draw(workspace, viewSwitcherRect);

        var viewContentRect = new ImRect(contentRect.X, contentY, contentRect.Width, contentRect.Bottom - contentY);

        DocTable activeTable = workspace.ActiveTable;
        var view = workspace.ActiveTableView;
        var viewType = view?.Type ?? DocViewType.Grid;
        int selectedVariantId = workspace.GetSelectedVariantIdForTable(activeTable);
        DocTable variantTable = workspace.ResolveTableForVariant(activeTable, selectedVariantId);
        bool interactive = !activeTable.IsDerived;
        switch (viewType)
        {
            case DocViewType.Grid:
                SpreadsheetRenderer.DrawContentTab(workspace, activeTable, view, viewContentRect, interactive: interactive, tableVariantId: selectedVariantId, stateKey: stateKey);
                break;
            case DocViewType.Board:
                BoardRenderer.DrawContentTab(
                    workspace,
                    variantTable,
                    view,
                    viewContentRect,
                    interactive: interactive,
                    parentRowColumnId: null,
                    parentRowId: null,
                    tableInstanceBlock: null,
                    stateKey: stateKey);
                break;
            case DocViewType.Calendar:
                CalendarRenderer.DrawContentTab(
                    workspace,
                    variantTable,
                    view,
                    viewContentRect,
                    interactive: interactive,
                    parentRowColumnId: null,
                    parentRowId: null,
                    tableInstanceBlock: null,
                    stateKey: stateKey);
                break;
            case DocViewType.Chart:
                ChartRenderer.Draw(
                    workspace,
                    variantTable,
                    view,
                    viewContentRect,
                    parentRowColumnId: null,
                    parentRowId: null);
                break;
            case DocViewType.Custom:
            {
                if (view != null &&
                    TableViewRendererResolver.TryGetCustomRenderer(view, out var customRenderer))
                {
                    customRenderer.Draw(workspace, variantTable, view, viewContentRect);
                }
                else
                {
                    Im.Text("Custom view renderer is unavailable for this view.".AsSpan(), viewContentRect.X + 10, viewContentRect.Y + 10, Im.Style.FontSize, Im.Style.TextSecondary);
                }

                break;
            }
        }
    }
}
