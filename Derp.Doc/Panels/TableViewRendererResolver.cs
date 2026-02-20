using Derp.Doc.Model;
using Derp.Doc.Plugins;

namespace Derp.Doc.Panels;

internal static class TableViewRendererResolver
{
    public static bool TryGetCustomRenderer(DocView? view, out IDerpDocTableViewRenderer renderer)
    {
        if (view == null ||
            view.Type != DocViewType.Custom ||
            string.IsNullOrWhiteSpace(view.CustomRendererId))
        {
            renderer = null!;
            return false;
        }

        return TableViewRendererRegistry.TryGet(view.CustomRendererId, out renderer!);
    }

    public static string GetViewTypeDisplayName(DocView? view)
    {
        if (view == null)
        {
            return "Grid";
        }

        switch (view.Type)
        {
            case DocViewType.Grid:
                return "Grid";
            case DocViewType.Board:
                return "Board";
            case DocViewType.Calendar:
                return "Calendar";
            case DocViewType.Chart:
                return "Chart";
            case DocViewType.Custom:
            {
                if (TryGetCustomRenderer(view, out var renderer))
                {
                    return renderer.DisplayName;
                }

                if (!string.IsNullOrWhiteSpace(view.CustomRendererId))
                {
                    return view.CustomRendererId;
                }

                return "Custom";
            }
            default:
                return "Grid";
        }
    }
}
