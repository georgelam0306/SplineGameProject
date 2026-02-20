using Derp.Doc.Editor;
using Derp.Doc.Model;

namespace Derp.Doc.Chat;

internal static class ChatContextBuilder
{
    private static readonly string[] ViewTypeNames =
    [
        "Grid",
        "Board",
        "Calendar",
        "Chart",
    ];

    public static string BuildSystemContext(DocWorkspace workspace)
    {
        var builder = new System.Text.StringBuilder(1024);

        builder.AppendLine("Derp.Doc workspace context:");
        builder.Append("- Active view: ");
        builder.Append(workspace.ActiveView == ActiveViewKind.Table ? "Table" : "Document");
        builder.AppendLine();

        if (workspace.ActiveTable != null)
        {
            AppendActiveTable(builder, workspace.ActiveTable, workspace.ActiveTableView);
        }
        else
        {
            builder.AppendLine("- Active table: none");
        }

        if (workspace.ActiveDocument != null)
        {
            AppendActiveDocument(builder, workspace.ActiveDocument);
        }
        else
        {
            builder.AppendLine("- Active document: none");
        }

        builder.Append("- Tables (");
        builder.Append(workspace.Project.Tables.Count);
        builder.AppendLine("):");
        for (int tableIndex = 0; tableIndex < workspace.Project.Tables.Count; tableIndex++)
        {
            var table = workspace.Project.Tables[tableIndex];
            builder.Append("  - ");
            builder.Append(table.Name);
            builder.Append(" (id=");
            builder.Append(table.Id);
            builder.Append(", rows=");
            builder.Append(table.Rows.Count);
            builder.Append(", columns=");
            builder.Append(table.Columns.Count);
            builder.AppendLine(")");
        }

        if (workspace.ChatSession.AgentType == ChatAgentType.Mcp)
        {
            builder.AppendLine("- Agent policy: MCP Agent. Use DerpDoc MCP tools only. Prefer batch row/table tools for multi-item edits.");
        }
        else
        {
            builder.AppendLine("- Agent policy: Workspace Agent. You may use DerpDoc MCP tools and workspace commands/files under the project root.");
            builder.AppendLine("- Workspace mode guidance: Prefer DerpDoc MCP tools for table/document data mutations when applicable.");
        }
        return builder.ToString();
    }

    private static void AppendActiveTable(System.Text.StringBuilder builder, DocTable table, DocView? activeView)
    {
        builder.Append("- Active table: ");
        builder.Append(table.Name);
        builder.Append(" (id=");
        builder.Append(table.Id);
        builder.Append(", rows=");
        builder.Append(table.Rows.Count);
        builder.AppendLine(")");

        builder.AppendLine("- Active table columns:");
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            var column = table.Columns[columnIndex];
            builder.Append("  - ");
            builder.Append(column.Name);
            builder.Append(" (id=");
            builder.Append(column.Id);
            builder.Append(", kind=");
            builder.Append(column.Kind);
            builder.AppendLine(")");
        }

        if (activeView != null)
        {
            string viewTypeName = (int)activeView.Type >= 0 && (int)activeView.Type < ViewTypeNames.Length
                ? ViewTypeNames[(int)activeView.Type]
                : activeView.Type.ToString();

            builder.Append("- Active table view: ");
            builder.Append(activeView.Name);
            builder.Append(" (id=");
            builder.Append(activeView.Id);
            builder.Append(", type=");
            builder.Append(viewTypeName);
            builder.AppendLine(")");

            builder.Append("  filters=");
            builder.Append(activeView.Filters.Count);
            builder.Append(", sorts=");
            builder.Append(activeView.Sorts.Count);
            builder.AppendLine();
        }
        else
        {
            builder.AppendLine("- Active table view: none");
        }
    }

    private static void AppendActiveDocument(System.Text.StringBuilder builder, DocDocument document)
    {
        builder.Append("- Active document: ");
        builder.Append(document.Title);
        builder.Append(" (id=");
        builder.Append(document.Id);
        builder.Append(", blocks=");
        builder.Append(document.Blocks.Count);
        builder.AppendLine(")");

        int blockPreviewCount = Math.Min(document.Blocks.Count, 5);
        if (blockPreviewCount == 0)
        {
            return;
        }

        builder.AppendLine("- Active document first blocks:");
        for (int blockIndex = 0; blockIndex < blockPreviewCount; blockIndex++)
        {
            var block = document.Blocks[blockIndex];
            builder.Append("  - ");
            builder.Append(block.Type);
            if (!string.IsNullOrWhiteSpace(block.TableId))
            {
                builder.Append(" tableId=");
                builder.Append(block.TableId);
            }

            builder.AppendLine();
        }
    }
}
