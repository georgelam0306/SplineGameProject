using Derp.Doc.Plugins;
using Derp.Doc.Tables;

namespace Derp.Doc.Editor;

internal sealed class DocPluginRegistrationContext : IDerpDocPluginRegistrar, IDerpDocEditorPluginRegistrar
{
    private readonly List<ColumnTypeDefinition> _columnTypeDefinitions = new();
    private readonly List<IColumnDefaultValueProvider> _defaultValueProviders = new();
    private readonly List<IColumnCellCodecProvider> _cellCodecProviders = new();
    private readonly List<FormulaFunctionDefinition> _formulaFunctions = new();
    private readonly List<IDerpDocTableViewRenderer> _tableViewRenderers = new();
    private readonly List<IDerpDocNodeSubtableSectionRenderer> _nodeSubtableSectionRenderers = new();
    private readonly List<IDerpDocColumnUiPlugin> _columnUiPlugins = new();
    private readonly List<IDerpDocPreferencesProvider> _preferencesProviders = new();
    private readonly List<IDerpDocAutomationProvider> _automationProviders = new();

    public void RegisterColumnType(ColumnTypeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _columnTypeDefinitions.Add(definition);
    }

    public void RegisterDefaultValueProvider(IColumnDefaultValueProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _defaultValueProviders.Add(provider);
    }

    public void RegisterCellCodecProvider(IColumnCellCodecProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _cellCodecProviders.Add(provider);
    }

    public void RegisterFormulaFunction(FormulaFunctionDefinition functionDefinition)
    {
        ArgumentNullException.ThrowIfNull(functionDefinition);
        _formulaFunctions.Add(functionDefinition);
    }

    public void RegisterTableViewRenderer(IDerpDocTableViewRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _tableViewRenderers.Add(renderer);
    }

    public void RegisterNodeSubtableSectionRenderer(IDerpDocNodeSubtableSectionRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _nodeSubtableSectionRenderers.Add(renderer);
    }

    public void RegisterColumnUiPlugin(IDerpDocColumnUiPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        _columnUiPlugins.Add(plugin);
    }

    public void RegisterPreferencesProvider(IDerpDocPreferencesProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _preferencesProviders.Add(provider);
    }

    public void RegisterAutomationProvider(IDerpDocAutomationProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _automationProviders.Add(provider);
    }

    public void Commit()
    {
        for (int definitionIndex = 0; definitionIndex < _columnTypeDefinitions.Count; definitionIndex++)
        {
            ColumnTypeDefinitionRegistry.Register(_columnTypeDefinitions[definitionIndex]);
        }

        for (int defaultProviderIndex = 0; defaultProviderIndex < _defaultValueProviders.Count; defaultProviderIndex++)
        {
            ColumnDefaultValueProviderRegistry.Register(_defaultValueProviders[defaultProviderIndex]);
        }

        for (int codecProviderIndex = 0; codecProviderIndex < _cellCodecProviders.Count; codecProviderIndex++)
        {
            ColumnCellCodecProviderRegistry.Register(_cellCodecProviders[codecProviderIndex]);
        }

        for (int functionIndex = 0; functionIndex < _formulaFunctions.Count; functionIndex++)
        {
            FormulaFunctionRegistry.Register(_formulaFunctions[functionIndex]);
        }

        for (int rendererIndex = 0; rendererIndex < _tableViewRenderers.Count; rendererIndex++)
        {
            TableViewRendererRegistry.Register(_tableViewRenderers[rendererIndex]);
        }

        for (int rendererIndex = 0; rendererIndex < _nodeSubtableSectionRenderers.Count; rendererIndex++)
        {
            NodeSubtableSectionRendererRegistry.Register(_nodeSubtableSectionRenderers[rendererIndex]);
        }

        for (int uiPluginIndex = 0; uiPluginIndex < _columnUiPlugins.Count; uiPluginIndex++)
        {
            ColumnUiPluginRegistry.Register(_columnUiPlugins[uiPluginIndex]);
        }

        for (int preferencesProviderIndex = 0; preferencesProviderIndex < _preferencesProviders.Count; preferencesProviderIndex++)
        {
            PluginPreferencesProviderRegistry.Register(_preferencesProviders[preferencesProviderIndex]);
        }

        for (int automationProviderIndex = 0; automationProviderIndex < _automationProviders.Count; automationProviderIndex++)
        {
            PluginAutomationProviderRegistry.Register(_automationProviders[automationProviderIndex]);
        }
    }
}
