namespace Derp.Doc.Plugins;

public interface IDerpDocEditorPlugin
{
    string Id { get; }

    void RegisterEditor(IDerpDocEditorPluginRegistrar registrar);
}
