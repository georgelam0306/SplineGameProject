namespace Derp.Doc.Plugins;

public static class TableViewRendererRegistry
{
    private static readonly Dictionary<string, IDerpDocTableViewRenderer> RenderersById =
        new(StringComparer.OrdinalIgnoreCase);

    public static int Count => RenderersById.Count;

    public static void Register(IDerpDocTableViewRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        string rendererId = NormalizeRendererId(renderer.RendererId);
        if (rendererId.Length == 0)
        {
            throw new ArgumentException("RendererId must be non-empty.", nameof(renderer));
        }

        RenderersById[rendererId] = renderer;
    }

    public static void Clear()
    {
        RenderersById.Clear();
    }

    public static bool TryGet(string? rendererId, out IDerpDocTableViewRenderer renderer)
    {
        return RenderersById.TryGetValue(NormalizeRendererId(rendererId), out renderer!);
    }

    public static void CopyRenderers(List<IDerpDocTableViewRenderer> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();

        foreach (var pair in RenderersById)
        {
            destination.Add(pair.Value);
        }

        destination.Sort(static (left, right) =>
            string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeRendererId(string? rendererId)
    {
        return string.IsNullOrWhiteSpace(rendererId)
            ? string.Empty
            : rendererId.Trim();
    }
}
