namespace Derp.Doc.Plugins;

public static class NodeSubtableSectionRendererRegistry
{
    private static readonly Dictionary<string, IDerpDocNodeSubtableSectionRenderer> RenderersById =
        new(StringComparer.OrdinalIgnoreCase);

    public static int Count => RenderersById.Count;

    public static void Register(IDerpDocNodeSubtableSectionRenderer renderer)
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

    public static bool TryGet(string? rendererId, out IDerpDocNodeSubtableSectionRenderer renderer)
    {
        return RenderersById.TryGetValue(NormalizeRendererId(rendererId), out renderer!);
    }

    public static void CopyRendererIds(List<string> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        foreach (KeyValuePair<string, IDerpDocNodeSubtableSectionRenderer> rendererPair in RenderersById)
        {
            destination.Add(rendererPair.Key);
        }

        destination.Sort(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeRendererId(string? rendererId)
    {
        return string.IsNullOrWhiteSpace(rendererId)
            ? string.Empty
            : rendererId.Trim();
    }
}
