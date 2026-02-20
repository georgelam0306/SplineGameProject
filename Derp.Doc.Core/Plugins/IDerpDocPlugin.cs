namespace Derp.Doc.Plugins;

public interface IDerpDocPlugin
{
    string Id { get; }

    void Register(IDerpDocPluginRegistrar registrar);
}
