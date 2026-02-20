#nullable enable
using Microsoft.CodeAnalysis;
using System.Text;

namespace SimTable.Generator
{
    /// <summary>
    /// Renders SimTable implementation code.
    /// This is a partial class - the rendering logic is split across multiple files:
    /// - TableRenderer.cs (this file) - Main Render() orchestrator
    /// - TableRenderer.Helpers.cs - Type utilities (GetTypeSize, hash expressions, JSON writers)
    /// - TableRenderer.MemoryLayout.cs - Field declarations, offset constants
    /// - TableRenderer.FieldAccessors.cs - SoA field accessors
    /// - TableRenderer.Lifecycle.cs - Constructor, Dispose, Allocate, Free, Reset
    /// - TableRenderer.SpatialSingle.cs - Single-grid spatial sort
    /// - TableRenderer.SpatialChunked.cs - Chunked mode spatial
    /// - TableRenderer.SpatialQueries.cs - Box/Radius query enumerables
    /// - TableRenderer.Serialization.cs - SaveTo, LoadFrom, meta serialization
    /// - TableRenderer.Debug.cs - JSON export, state hash
    /// - TableRenderer.RowRef.cs - RowRef struct generation
    /// </summary>
    internal static partial class TableRenderer
    {
        public static string Render(SchemaModel model, int tableId = 0)
        {
            // Extract common values
            var ns = model.Type.ContainingNamespace?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::";
            var typeName = model.Type.Name;
            var tableName = typeName + "Table";
            var rowRefName = typeName + "RowRef";
            int capacity = model.Capacity;
            int cellSize = model.CellSize;
            int gridSize = model.GridSize;
            int totalCells = gridSize * gridSize;
            int chunkSize = model.ChunkSize;
            bool isChunked = model.IsChunked;
            bool isDataOnly = model.IsDataOnly;

            var sb = new StringBuilder(32_768);

            // Header
            RenderHeader(sb, isChunked, isDataOnly);

            // Namespace opening
            if (!string.IsNullOrEmpty(ns) && ns != "global::")
            {
                sb.Append("namespace ").Append(ns.Replace("global::", string.Empty)).AppendLine();
                sb.AppendLine("{");
            }

            // Class opening
            sb.AppendLine($"    public sealed unsafe class {tableName} : IDisposable, global::SimTable.ISimTable");
            sb.AppendLine("    {");

            // Memory layout (fields, constants, properties)
            RenderMemoryLayout(sb, model, tableName, tableId, isChunked, isDataOnly, capacity, cellSize, gridSize, totalCells, chunkSize);

            // Constructor
            RenderConstructor(sb, model, tableName, isChunked, isDataOnly);

            // Dispose
            RenderDispose(sb);

            // Allocate
            RenderAllocate(sb, model, isDataOnly);

            // Free and LRU victim
            RenderFree(sb, model, rowRefName);

            // CopySlotData helper
            RenderCopySlotData(sb, model);

            // Reset
            RenderReset(sb, model, isDataOnly);

            // Slot management (GetSlot, GetStableId, IsSlotActive, GetHandle)
            RenderSlotManagement(sb);

            // Field accessors
            RenderFieldAccessors(sb, model);

            // Row accessors
            RenderRowAccessors(sb, rowRefName);

            // Spatial methods
            if (isChunked && !isDataOnly)
            {
                RenderSpatialSortChunked(sb);
                RenderComputeChunkKey(sb, model);
                RenderComputeLocalCell(sb, model);
                RenderChunkAccessors(sb);
                RenderSpatialQueries(sb, tableName);
            }
            else if (!isDataOnly)
            {
                RenderSpatialSortSingle(sb);
                RenderComputeCell(sb, model);

                // Only render spatial queries if the table has a Position field
                var (hasPosition, _) = GetPositionInfo(model);
                if (hasPosition)
                {
                    RenderSpatialQueriesSingle(sb, tableName);
                }
            }

            // Cell accessors (for both modes, single grid still needs these)
            if (!isDataOnly)
            {
                RenderCellAccessors(sb);
            }

            // Serialization
            RenderSaveLoadSlab(sb);
            RenderRecomputeAll(sb, model);
            RenderMetaSerialization(sb);

            // Debug methods
            RenderExportDebugJson(sb, model);
            RenderComputeStateHash(sb, model);

            // Class closing
            sb.AppendLine("    }");
            sb.AppendLine();

            // RowRef struct
            RenderRowRefStruct(sb, model, tableName, rowRefName);

            // Partial struct for default values
            RenderPartialStruct(sb, model, typeName);

            // Namespace closing
            if (!string.IsNullOrEmpty(ns) && ns != "global::")
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Renders file header with pragmas and using statements.
        /// </summary>
        private static void RenderHeader(StringBuilder sb, bool isChunked, bool isDataOnly)
        {
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("#pragma warning disable CS0675 // Bitwise-or operator used on a sign-extended operand");
            sb.AppendLine("#pragma warning disable CS9191 // The 'ref' modifier for argument corresponding to 'in' parameter is equivalent to 'in'");
            sb.AppendLine("using System;");
            if (isChunked && !isDataOnly)
            {
                sb.AppendLine("using System.Collections.Generic;");
            }
            sb.AppendLine("using System.Runtime.CompilerServices;");
            sb.AppendLine("using System.Runtime.InteropServices;");
            sb.AppendLine("using Core;"); // For Fixed64, Fixed64Vec2
            sb.AppendLine();
        }
    }
}
