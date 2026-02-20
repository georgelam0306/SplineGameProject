namespace Derp.Doc.Plugins;

public sealed class NodeGraphPlugin : IDerpDocEditorPlugin
{
    public string Id => "builtin.node-graph";

    public void RegisterEditor(IDerpDocEditorPluginRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(registrar);
        registrar.RegisterTableViewRenderer(new NodeGraphTableViewRenderer());
    }
}
