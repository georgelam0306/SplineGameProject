namespace Derp.Doc.Plugins;

public interface IDerpDocEditorPluginRegistrar
{
    void RegisterTableViewRenderer(IDerpDocTableViewRenderer renderer);

    void RegisterNodeSubtableSectionRenderer(IDerpDocNodeSubtableSectionRenderer renderer);

    void RegisterColumnUiPlugin(IDerpDocColumnUiPlugin plugin);

    void RegisterPreferencesProvider(IDerpDocPreferencesProvider provider);

    void RegisterAutomationProvider(IDerpDocAutomationProvider provider);
}
