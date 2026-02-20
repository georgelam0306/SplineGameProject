namespace Derp.Doc.Editor;

internal sealed class DocLoadedPlugin
{
    public required string Id { get; init; }
    public required string AssemblyPath { get; init; }
    public required object Instance { get; init; }
    public DocPluginLoadContext? LoadContext { get; init; }
}
