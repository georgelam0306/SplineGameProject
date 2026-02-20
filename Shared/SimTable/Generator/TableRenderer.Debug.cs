#nullable enable
using System.Text;

namespace SimTable.Generator
{
    internal static partial class TableRenderer
    {
        /// <summary>
        /// Renders ExportDebugJson method for desync debugging.
        /// </summary>
        private static void RenderExportDebugJson(StringBuilder sb, SchemaModel model)
        {
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Exports all rows with all columns to JSON for desync debugging.");
            sb.AppendLine("        /// Auto-generated to include every field that affects state hash.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public void ExportDebugJson(System.Text.Json.Utf8JsonWriter writer)");
            sb.AppendLine("        {");
            sb.AppendLine("            writer.WriteStartArray();");
            sb.AppendLine("            for (int slot = 0; slot < _count; slot++)");
            sb.AppendLine("            {");
            sb.AppendLine("                if (_slotToStableId[slot] < 0) continue;");
            sb.AppendLine("                writer.WriteStartObject();");
            sb.AppendLine("                writer.WriteNumber(\"slot\", slot);");
            sb.AppendLine("                writer.WriteNumber(\"stableId\", _slotToStableId[slot]);");
            foreach (var col in model.Columns)
            {
                if (col.IsArray2D)
                {
                    sb.AppendLine($"                writer.WriteStartArray(\"{col.Name}\");");
                    sb.AppendLine($"                for (int row = 0; row < _rows{col.Name}; row++)");
                    sb.AppendLine("                {");
                    sb.AppendLine("                    writer.WriteStartArray();");
                    sb.AppendLine($"                    for (int c = 0; c < _cols{col.Name}; c++)");
                    sb.AppendLine("                    {");
                    sb.AppendLine($"                        var elem = {col.Name}(slot, row, c);");
                    WriteJsonElementValue(sb, col.Type, "elem", "                        ");
                    sb.AppendLine("                    }");
                    sb.AppendLine("                    writer.WriteEndArray();");
                    sb.AppendLine("                }");
                    sb.AppendLine("                writer.WriteEndArray();");
                }
                else if (col.IsArray)
                {
                    sb.AppendLine($"                writer.WriteStartArray(\"{col.Name}\");");
                    sb.AppendLine($"                for (int idx = 0; idx < _arrayLength{col.Name}; idx++)");
                    sb.AppendLine("                {");
                    sb.AppendLine($"                    var elem = {col.Name}(slot, idx);");
                    WriteJsonElementValue(sb, col.Type, "elem", "                    ");
                    sb.AppendLine("                }");
                    sb.AppendLine("                writer.WriteEndArray();");
                }
                else
                {
                    WriteJsonPropertyValue(sb, col.Name, col.Type, "                ");
                }
            }
            sb.AppendLine("                writer.WriteEndObject();");
            sb.AppendLine("            }");
            sb.AppendLine("            writer.WriteEndArray();");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        /// <summary>
        /// Renders ComputeStateHash method for determinism validation.
        /// </summary>
        private static void RenderComputeStateHash(StringBuilder sb, SchemaModel model)
        {
            sb.AppendLine("        public ulong ComputeStateHash()");
            sb.AppendLine("        {");
            sb.AppendLine("            ulong hash = 14695981039346656037UL;");
            sb.AppendLine("            for (int slot = 0; slot < _count; slot++)");
            sb.AppendLine("            {");
            sb.AppendLine("                if (_slotToStableId[slot] < 0)");
            sb.AppendLine("                {");
            sb.AppendLine("                    continue;");
            sb.AppendLine("                }");
            foreach (var col in model.Columns)
            {
                if (col.IsArray2D)
                {
                    // Hash all 2D array elements using the flat span
                    string hashExpr = GetArray2DHashExpression(col.Name, col.Type);
                    sb.AppendLine($"                {hashExpr}");
                }
                else if (col.IsArray)
                {
                    // Hash all 1D array elements
                    for (int i = 0; i < col.ArrayLength; i++)
                    {
                        string hashExpr = GetHashExpression($"{col.Name}{i}", col.Type);
                        sb.AppendLine($"                hash ^= {hashExpr};");
                        sb.AppendLine("                hash *= 1099511628211UL;");
                    }
                }
                else
                {
                    string hashExpr = GetHashExpression(col.Name, col.Type);
                    sb.AppendLine($"                hash ^= {hashExpr};");
                    sb.AppendLine("                hash *= 1099511628211UL;");
                }
            }
            sb.AppendLine("            }");
            sb.AppendLine("            return hash;");
            sb.AppendLine("        }");
        }
    }
}
