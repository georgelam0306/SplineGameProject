using Derp.Doc.Model;

namespace Derp.Doc.Tables;

public static class DocSystemTableRules
{
    public static bool IsSystemTable(DocTable? table)
    {
        return table != null && !string.IsNullOrWhiteSpace(table.SystemKey);
    }

    public static bool IsSchemaLocked(DocTable? table)
    {
        return IsSystemTable(table) && table!.IsSystemSchemaLocked;
    }

    public static bool IsDataLocked(DocTable? table)
    {
        return IsSystemTable(table) && table!.IsSystemDataLocked;
    }

    public static bool AllowsVariants(DocTable? table)
    {
        if (!IsSystemTable(table))
        {
            return true;
        }

        return string.Equals(table!.SystemKey, DocSystemTableKeys.Packages, StringComparison.Ordinal) ||
               string.Equals(table.SystemKey, DocSystemTableKeys.Exports, StringComparison.Ordinal);
    }
}
