namespace Derp.Doc.Plugins;

public static class ColumnUiPluginRegistry
{
    private static readonly Dictionary<string, IDerpDocColumnUiPlugin> PluginsByTypeId =
        new(StringComparer.OrdinalIgnoreCase);

    public static int Count => PluginsByTypeId.Count;

    public static void Register(IDerpDocColumnUiPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        string columnTypeId = NormalizeTypeId(plugin.ColumnTypeId);
        if (columnTypeId.Length == 0)
        {
            throw new ArgumentException("ColumnTypeId must be non-empty.", nameof(plugin));
        }

        PluginsByTypeId[columnTypeId] = plugin;
    }

    public static void Clear()
    {
        PluginsByTypeId.Clear();
    }

    public static bool TryGet(string? columnTypeId, out IDerpDocColumnUiPlugin plugin)
    {
        return PluginsByTypeId.TryGetValue(NormalizeTypeId(columnTypeId), out plugin!);
    }

    public static void CopyPlugins(List<IDerpDocColumnUiPlugin> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();

        foreach (var pair in PluginsByTypeId)
        {
            destination.Add(pair.Value);
        }

        destination.Sort(static (left, right) =>
            string.Compare(left.ColumnTypeId, right.ColumnTypeId, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeTypeId(string? columnTypeId)
    {
        return string.IsNullOrWhiteSpace(columnTypeId)
            ? string.Empty
            : columnTypeId.Trim();
    }
}
