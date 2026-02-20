namespace Derp.Doc.Plugins;

public static class PluginAutomationProviderRegistry
{
    private static readonly Dictionary<string, IDerpDocAutomationProvider> ProvidersByActionId =
        new(StringComparer.OrdinalIgnoreCase);

    public static int Count => ProvidersByActionId.Count;

    public static void Register(IDerpDocAutomationProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        string actionId = NormalizeActionId(provider.ActionId);
        if (actionId.Length == 0)
        {
            throw new ArgumentException("ActionId must be non-empty.", nameof(provider));
        }

        ProvidersByActionId[actionId] = provider;
    }

    public static void Clear()
    {
        ProvidersByActionId.Clear();
    }

    public static void CopyProviders(List<IDerpDocAutomationProvider> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();

        foreach (var pair in ProvidersByActionId)
        {
            destination.Add(pair.Value);
        }

        destination.Sort(static (left, right) =>
            string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeActionId(string? actionId)
    {
        return string.IsNullOrWhiteSpace(actionId)
            ? string.Empty
            : actionId.Trim();
    }
}
