#nullable enable
using Microsoft.CodeAnalysis;
using System.Text;

namespace SimTable.Generator
{
    internal static partial class TableRenderer
    {
        /// <summary>
        /// Renders field accessor methods (SoA accessors for each column).
        /// </summary>
        private static void RenderFieldAccessors(StringBuilder sb, SchemaModel model)
        {
            foreach (var col in model.Columns)
            {
                var colType = col.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                int elementSize = GetTypeSize(col.Type);

                if (col.IsArray2D)
                {
                    // 2D Array field: generate span accessor, row accessor, and element accessor

                    // Span accessor for entire 2D array at a slot (flattened view)
                    sb.AppendLine($"        /// <summary>Gets a Span over all {col.Array2DRows}x{col.Array2DCols} elements of {col.Name} for the given slot (row-major order).</summary>");
                    sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    sb.AppendLine($"        public Span<{colType}> {col.Name}Span(int slot)");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            return new Span<{colType}>(_slab + _offset{col.Name} + slot * _array2DStride{col.Name}, _totalElements{col.Name});");
                    sb.AppendLine("        }");
                    sb.AppendLine();

                    // Row accessor (returns Span for a single row)
                    sb.AppendLine($"        /// <summary>Gets a Span over the elements in row 'row' of {col.Name} for the given slot.</summary>");
                    sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    sb.AppendLine($"        public Span<{colType}> {col.Name}Row(int slot, int row)");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            return new Span<{colType}>(_slab + _offset{col.Name} + slot * _array2DStride{col.Name} + row * _rowStride{col.Name}, _cols{col.Name});");
                    sb.AppendLine("        }");
                    sb.AppendLine();

                    // Element accessor (row, col)
                    sb.AppendLine($"        /// <summary>Gets a reference to element at (row, col) within {col.Name} 2D array for the given slot.</summary>");
                    sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    sb.AppendLine($"        public ref {colType} {col.Name}(int slot, int row, int col)");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            return ref Unsafe.AsRef<{colType}>(_slab + _offset{col.Name} + slot * _array2DStride{col.Name} + row * _rowStride{col.Name} + col * _elementSize{col.Name});");
                    sb.AppendLine("        }");
                    sb.AppendLine();
                }
                else if (col.IsArray)
                {
                    // 1D Array field: generate array accessor, indexed accessor, and individual named accessors

                    // Span accessor for entire array at a slot
                    sb.AppendLine($"        /// <summary>Gets a Span over all {col.ArrayLength} elements of {col.Name} for the given slot.</summary>");
                    sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    sb.AppendLine($"        public Span<{colType}> {col.Name}Array(int slot)");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            return new Span<{colType}>(_slab + _offset{col.Name} + slot * _arrayStride{col.Name}, _arrayLength{col.Name});");
                    sb.AppendLine("        }");
                    sb.AppendLine();

                    // Indexed accessor
                    sb.AppendLine($"        /// <summary>Gets a reference to element at index within {col.Name} array for the given slot.</summary>");
                    sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    sb.AppendLine($"        public ref {colType} {col.Name}(int slot, int index)");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            return ref Unsafe.AsRef<{colType}>(_slab + _offset{col.Name} + slot * _arrayStride{col.Name} + index * _elementSize{col.Name});");
                    sb.AppendLine("        }");
                    sb.AppendLine();

                    // Individual named accessors (Field0, Field1, etc.)
                    for (int i = 0; i < col.ArrayLength; i++)
                    {
                        sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                        sb.AppendLine($"        public ref {colType} {col.Name}{i}(int slot)");
                        sb.AppendLine("        {");
                        sb.AppendLine($"            return ref Unsafe.AsRef<{colType}>(_slab + _offset{col.Name} + slot * _arrayStride{col.Name} + {i * elementSize});");
                        sb.AppendLine("        }");
                        sb.AppendLine();
                    }
                }
                else
                {
                    // Non-array field: standard SoA accessor
                    sb.AppendLine($"        public Span<{colType}> {col.Name}Span");
                    sb.AppendLine("        {");
                    sb.AppendLine("            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    sb.AppendLine($"            get => new Span<{colType}>(_slab + _offset{col.Name}, Capacity);");
                    sb.AppendLine("        }");
                    sb.AppendLine();

                    sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    sb.AppendLine($"        public ref {colType} {col.Name}(int slot)");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            return ref Unsafe.AsRef<{colType}>(_slab + _offset{col.Name} + slot * {elementSize});");
                    sb.AppendLine("        }");
                    sb.AppendLine();
                }
            }
        }

        /// <summary>
        /// Renders row reference accessor methods.
        /// </summary>
        private static void RenderRowAccessors(StringBuilder sb, string rowRefName)
        {
            sb.AppendLine($"        public {rowRefName} GetRowBySlot(int slot)");
            sb.AppendLine("        {");
            sb.AppendLine("            int stableId = GetStableId(slot);");
            sb.AppendLine($"            return new {rowRefName}(this, slot, stableId);");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public bool TryGetRow(int slot, out {rowRefName} row)");
            sb.AppendLine("        {");
            sb.AppendLine("            int stableId = GetStableId(slot);");
            sb.AppendLine("            if (stableId < 0)");
            sb.AppendLine("            {");
            sb.AppendLine("                row = default;");
            sb.AppendLine("                return false;");
            sb.AppendLine("            }");
            sb.AppendLine($"            row = new {rowRefName}(this, slot, stableId);");
            sb.AppendLine("            return true;");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine($"        public {rowRefName} GetRow(global::SimTable.SimHandle handle)");
            sb.AppendLine("        {");
            sb.AppendLine("            int slot = GetSlot(handle);");
            sb.AppendLine($"            return new {rowRefName}(this, slot, handle.StableId);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }
    }
}
