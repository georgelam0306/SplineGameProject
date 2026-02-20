using Derp.Doc.Tables;

namespace Derp.Doc.Plugins;

public interface IDerpDocPluginRegistrar
{
    void RegisterColumnType(ColumnTypeDefinition definition);

    void RegisterDefaultValueProvider(IColumnDefaultValueProvider provider);

    void RegisterCellCodecProvider(IColumnCellCodecProvider provider);

    void RegisterFormulaFunction(FormulaFunctionDefinition functionDefinition);
}
