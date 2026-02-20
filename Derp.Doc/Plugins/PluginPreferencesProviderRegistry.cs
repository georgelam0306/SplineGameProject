namespace Derp.Doc.Plugins;

public static class PluginPreferencesProviderRegistry
{
    private static readonly Dictionary<string, IDerpDocPreferencesProvider> ProvidersById =
        new(StringComparer.OrdinalIgnoreCase);

    public static int Count => ProvidersById.Count;

    public static void Register(IDerpDocPreferencesProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        string providerId = NormalizeId(provider.Id);
        if (providerId.Length == 0)
        {
            throw new ArgumentException("Provider Id must be non-empty.", nameof(provider));
        }

        ProvidersById[providerId] = provider;
    }

    public static void Clear()
    {
        ProvidersById.Clear();
    }

    public static void CopyProviders(List<IDerpDocPreferencesProvider> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();

        foreach (var pair in ProvidersById)
        {
            destination.Add(pair.Value);
        }

        destination.Sort(static (left, right) =>
            string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeId(string? providerId)
    {
        return string.IsNullOrWhiteSpace(providerId)
            ? string.Empty
            : providerId.Trim();
    }
}
