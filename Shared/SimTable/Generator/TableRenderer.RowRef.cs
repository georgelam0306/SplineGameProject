#nullable enable
using Microsoft.CodeAnalysis;
using System.Text;

namespace SimTable.Generator
{
    internal static partial class TableRenderer
    {
        /// <summary>
        /// Renders the RowRef readonly ref struct.
        /// </summary>
        private static void RenderRowRefStruct(StringBuilder sb, SchemaModel model, string tableName, string rowRefName)
        {
            sb.AppendLine($"    public readonly ref struct {rowRefName}");
            sb.AppendLine("    {");
            sb.AppendLine($"        private readonly {tableName} _table;");
            sb.AppendLine("        private readonly int _slot;");
            sb.AppendLine("        private readonly int _stableId;");
            sb.AppendLine();
            sb.AppendLine($"        public {rowRefName}({tableName} table, int slot, int stableId)");
            sb.AppendLine("        {");
            sb.AppendLine("            _table = table;");
            sb.AppendLine("            _slot = slot;");
            sb.AppendLine("            _stableId = stableId;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public int Slot => _slot;");
            sb.AppendLine("        public int StableId => _stableId;");
            sb.AppendLine("        public bool IsValid => _slot >= 0;");
            sb.AppendLine();

            foreach (var col in model.Columns)
            {
                var colType = col.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                if (col.IsArray2D)
                {
                    // 2D Array field: generate Span property for entire array
                    sb.AppendLine($"        public Span<{colType}> {col.Name}Span => _table.{col.Name}Span(_slot);");
                    // Row accessor method
                    sb.AppendLine($"        public Span<{colType}> {col.Name}Row(int row) => _table.{col.Name}Row(_slot, row);");
                    // Element accessor method (can't use ref property for indexed access)
                    sb.AppendLine($"        public ref {colType} {col.Name}(int row, int col) => ref _table.{col.Name}(_slot, row, col);");
                }
                else if (col.IsArray)
                {
                    // 1D Array field: generate Span property and individual named properties
                    sb.AppendLine($"        public Span<{colType}> {col.Name}Array => _table.{col.Name}Array(_slot);");
                    for (int i = 0; i < col.ArrayLength; i++)
                    {
                        sb.AppendLine($"        public ref {colType} {col.Name}{i} => ref _table.{col.Name}{i}(_slot);");
                    }
                }
                else
                {
                    sb.AppendLine($"        public ref {colType} {col.Name} => ref _table.{col.Name}(_slot);");
                }
            }

            sb.AppendLine("    }");
        }

        /// <summary>
        /// Renders partial struct with parameterless constructor for structs with field initializers.
        /// </summary>
        private static void RenderPartialStruct(StringBuilder sb, SchemaModel model, string typeName)
        {
            // Generate partial struct with parameterless constructor if any columns have default values
            // This is required by C# when a struct has field initializers
            bool hasDefaultValues = false;
            foreach (var col in model.Columns)
            {
                if (!col.IsArray && !col.IsArray2D && col.DefaultValueExpression != null)
                {
                    hasDefaultValues = true;
                    break;
                }
            }
            if (hasDefaultValues)
            {
                sb.AppendLine();
                sb.AppendLine($"    public partial struct {typeName}");
                sb.AppendLine("    {");
                sb.AppendLine($"        public {typeName}() {{ }}");
                sb.AppendLine("    }");
            }
        }
    }
}
