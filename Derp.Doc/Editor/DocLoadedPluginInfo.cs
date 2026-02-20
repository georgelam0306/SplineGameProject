namespace Derp.Doc.Editor;

internal readonly struct DocLoadedPluginInfo
{
    public DocLoadedPluginInfo(string id, string assemblyPath)
    {
        Id = id;
        AssemblyPath = assemblyPath;
    }

    public string Id { get; }

    public string AssemblyPath { get; }
}
