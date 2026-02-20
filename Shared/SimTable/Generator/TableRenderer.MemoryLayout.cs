#nullable enable
using Microsoft.CodeAnalysis;
using System.Linq;
using System.Text;

namespace SimTable.Generator
{
    internal static partial class TableRenderer
    {
        /// <summary>
        /// Renders the memory layout section: field declarations, constants, and offset calculations.
        /// Returns the authoritative slab size for use by other sections.
        /// </summary>
        private static int RenderMemoryLayout(StringBuilder sb, SchemaModel model, string tableName, int tableId, bool isChunked, bool isDataOnly, int capacity, int cellSize, int gridSize, int totalCells, int chunkSize)
        {
            // Capacity is private to prevent incorrect iteration patterns.
            // Always use Count for iteration, not Capacity.
            sb.AppendLine($"        private const int Capacity = {capacity};");
            if (!isDataOnly)
            {
                sb.AppendLine($"        public const int CellSize = {cellSize};");
                sb.AppendLine($"        public const int GridSize = {gridSize};");
                sb.AppendLine($"        public const int TotalCells = {totalCells};");
                sb.AppendLine($"        public const int ChunkSize = {chunkSize};");
                sb.AppendLine($"        public const bool IsChunkedMode = {(isChunked ? "true" : "false")};");
            }
            sb.AppendLine($"        public const int TableIdConst = {tableId};");
            sb.AppendLine($"        public int TableId => TableIdConst;");
            sb.AppendLine();

            sb.AppendLine("        private byte* _slab;");
            sb.AppendLine("        private int _slabSize;");
            sb.AppendLine("        private int _count;");
            sb.AppendLine();

            sb.AppendLine("        private int[] _stableIdToSlot;  // Maps rawId -> slot (-1 if freed)");
            sb.AppendLine("        private int[] _slotToStableId;  // Maps slot -> packed stableId (gen<<16|rawId)");
            sb.AppendLine("        private int _nextStableId;      // Next rawId to allocate");
            sb.AppendLine("        private int _stableIdFreeListHead;");
            sb.AppendLine("        private int[] _stableIdNextFree;  // Dedicated free list for stableIds");
            sb.AppendLine("        private int[] _generations;       // Current generation per rawId");
            sb.AppendLine();

            // Single-grid spatial sort fields (used when ChunkSize == 0)
            if (!isDataOnly)
            {
                sb.AppendLine("        private int[] _cellStart;");
                sb.AppendLine("        private int[] _cellCount;");
                sb.AppendLine("        private int[] _sortedOrder;");
                sb.AppendLine("        private int[] _cellWrite;");
                sb.AppendLine();
            }

            // Hierarchical coarse masks for fast empty-region skipping (single-grid mode only)
            if (!isChunked && !isDataOnly)
            {
                sb.AppendLine("        // Hierarchical coarse masks for fast empty-region skipping");
                sb.AppendLine("        // L1: 4×4 bits (4×4 fine cells each = 128px)");
                sb.AppendLine("        // L2: 16×16 bits (16×16 fine cells each = 512px)");
                sb.AppendLine("        // L3: 4×4 bits (64×64 fine cells each = 2048px)");
                sb.AppendLine("        private ulong[] _coarseMaskL1;  // 64 ulongs = 512 bytes");
                sb.AppendLine("        private ulong[] _coarseMaskL2;  // 4 ulongs = 32 bytes");
                sb.AppendLine("        private ulong _coarseMaskL3;    // 1 ulong (only 16 bits used) = 8 bytes");
                sb.AppendLine("        private const int L1Shift = 2;   // 4 fine cells per L1 cell");
                sb.AppendLine("        private const int L2Shift = 4;   // 16 fine cells per L2 cell");
                sb.AppendLine("        private const int L3Shift = 6;   // 64 fine cells per L3 cell");
                sb.AppendLine("        private const int L1GridSize = GridSize >> L1Shift;  // 256 >> 2 = 64");
                sb.AppendLine("        private const int L2GridSize = GridSize >> L2Shift;  // 256 >> 4 = 16");
                sb.AppendLine("        private const int L3GridSize = GridSize >> L3Shift;  // 256 >> 6 = 4");
                sb.AppendLine();
            }

            // Chunked mode fields (used when ChunkSize > 0)
            if (isChunked && !isDataOnly)
            {
                sb.AppendLine("        // Chunked spatial mode - supports infinite world");
                sb.AppendLine("        private const int MaxChunks = 64;  // Max simultaneous active chunks");
                sb.AppendLine();
                sb.AppendLine("        // Hierarchical mask constants for chunked mode (per-chunk)");
                sb.AppendLine("        // Levels are capped based on GridSize - only include levels where grid is large enough");
                sb.AppendLine("        private const int ChunkL1Shift = 2;   // 4 fine cells per L1 cell");
                sb.AppendLine("        private const int ChunkL2Shift = 4;   // 16 fine cells per L2 cell");
                sb.AppendLine("        private const int ChunkL1GridSize = GridSize >> ChunkL1Shift;");
                sb.AppendLine("        private const int ChunkL2GridSize = GridSize >> ChunkL2Shift;");
                sb.AppendLine("        private const bool HasChunkL1 = GridSize >= 4;   // Need at least 4 cells for L1");
                sb.AppendLine("        private const bool HasChunkL2 = GridSize >= 16;  // Need at least 16 cells for L2");
                sb.AppendLine();
                sb.AppendLine("        // Chunk key for identifying chunks by world position");
                sb.AppendLine("        public readonly record struct ChunkKey(int X, int Y) : IComparable<ChunkKey>");
                sb.AppendLine("        {");
                sb.AppendLine("            public int CompareTo(ChunkKey other)");
                sb.AppendLine("            {");
                sb.AppendLine("                int cmp = Y.CompareTo(other.Y);");
                sb.AppendLine("                return cmp != 0 ? cmp : X.CompareTo(other.X);");
                sb.AppendLine("            }");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        // Per-chunk grid data with hierarchical masks");
                sb.AppendLine("        private struct ChunkGrid");
                sb.AppendLine("        {");
                sb.AppendLine("            public int[] CellStart;");
                sb.AppendLine("            public int[] CellCount;");
                sb.AppendLine("            public int[] CellWrite;");
                sb.AppendLine("            public int EntityCount;");
                sb.AppendLine("            // Hierarchical coarse masks for fast empty-region skipping within chunk");
                sb.AppendLine("            public ulong[] CoarseMaskL1;  // L1: 4×4 cells per block");
                sb.AppendLine("            public ulong CoarseMaskL2;    // L2: 16×16 cells per block (single ulong for small grids)");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        // Chunk registry");
                sb.AppendLine("        private ChunkKey[] _activeChunks;");
                sb.AppendLine("        private int _activeChunkCount;");
                sb.AppendLine("        private Dictionary<ChunkKey, int> _chunkToPoolIndex;");
                sb.AppendLine();
                sb.AppendLine("        // Chunk grid pool");
                sb.AppendLine("        private ChunkGrid[] _chunkGridPool;");
                sb.AppendLine("        private int _poolRentIndex;");
                sb.AppendLine();
                sb.AppendLine("        // Per-entity chunk assignment (for sorting)");
                sb.AppendLine("        private int[] _entityChunkIndex;  // Maps slot -> chunk pool index");
                sb.AppendLine();
            }

            // Slab header layout (8 bytes at offset 0):
            // - Bytes 0-3: Version (uint, incremented on any mutation)
            // - Bytes 4-7: Reserved for future use
            // The header is part of the authoritative slab and gets serialized via memcopy.
            sb.AppendLine("        private const int SlabHeaderSize = 8;");
            sb.AppendLine();

            // Partition columns: authoritative (serialized) fields first, then computed (non-serialized) fields
            // This gives us a contiguous slab where SaveTo/LoadFrom only copies the authoritative portion
            // Field data starts after the 8-byte header
            int offset = 8; // Start after slab header

            // First pass: authoritative fields (not computed)
            foreach (var col in model.Columns.Where(c => !c.IsComputed))
            {
                var colType = col.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                int elementSize = GetTypeSize(col.Type);

                if (col.IsArray2D)
                {
                    // 2D Array field: memory layout is contiguous row-major per-slot
                    // Each slot has (rows * cols) contiguous elements
                    int totalElements = col.Array2DRows * col.Array2DCols;
                    sb.AppendLine($"        private const int _offset{col.Name} = {offset};");
                    sb.AppendLine($"        private const int _rows{col.Name} = {col.Array2DRows};");
                    sb.AppendLine($"        private const int _cols{col.Name} = {col.Array2DCols};");
                    sb.AppendLine($"        private const int _totalElements{col.Name} = {totalElements};");
                    sb.AppendLine($"        private const int _elementSize{col.Name} = {elementSize};");
                    sb.AppendLine($"        private const int _rowStride{col.Name} = {elementSize * col.Array2DCols};");
                    sb.AppendLine($"        private const int _array2DStride{col.Name} = {elementSize * totalElements};");
                    offset += elementSize * totalElements * capacity;
                }
                else if (col.IsArray)
                {
                    // 1D Array field: memory layout is contiguous per-slot (AoS within this field)
                    // Each slot has arrayLength contiguous elements
                    sb.AppendLine($"        private const int _offset{col.Name} = {offset};");
                    sb.AppendLine($"        private const int _arrayLength{col.Name} = {col.ArrayLength};");
                    sb.AppendLine($"        private const int _elementSize{col.Name} = {elementSize};");
                    sb.AppendLine($"        private const int _arrayStride{col.Name} = {elementSize * col.ArrayLength};");
                    offset += elementSize * col.ArrayLength * capacity;
                }
                else
                {
                    sb.AppendLine($"        private const int _offset{col.Name} = {offset};");
                    offset += elementSize * capacity;
                }
            }
            int authoritativeSlabSize = offset;
            sb.AppendLine($"        private const int AuthoritativeSlabSize = {authoritativeSlabSize};");

            // Second pass: computed fields (placed after authoritative fields in contiguous slab)
            foreach (var col in model.Columns.Where(c => c.IsComputed))
            {
                var colType = col.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                int elementSize = GetTypeSize(col.Type);

                if (col.IsArray2D)
                {
                    int totalElements = col.Array2DRows * col.Array2DCols;
                    sb.AppendLine($"        private const int _offset{col.Name} = {offset};");
                    sb.AppendLine($"        private const int _rows{col.Name} = {col.Array2DRows};");
                    sb.AppendLine($"        private const int _cols{col.Name} = {col.Array2DCols};");
                    sb.AppendLine($"        private const int _totalElements{col.Name} = {totalElements};");
                    sb.AppendLine($"        private const int _elementSize{col.Name} = {elementSize};");
                    sb.AppendLine($"        private const int _rowStride{col.Name} = {elementSize * col.Array2DCols};");
                    sb.AppendLine($"        private const int _array2DStride{col.Name} = {elementSize * totalElements};");
                    offset += elementSize * totalElements * capacity;
                }
                else if (col.IsArray)
                {
                    sb.AppendLine($"        private const int _offset{col.Name} = {offset};");
                    sb.AppendLine($"        private const int _arrayLength{col.Name} = {col.ArrayLength};");
                    sb.AppendLine($"        private const int _elementSize{col.Name} = {elementSize};");
                    sb.AppendLine($"        private const int _arrayStride{col.Name} = {elementSize * col.ArrayLength};");
                    offset += elementSize * col.ArrayLength * capacity;
                }
                else
                {
                    sb.AppendLine($"        private const int _offset{col.Name} = {offset};");
                    offset += elementSize * capacity;
                }
            }
            sb.AppendLine($"        private const int TotalSlabSize = {offset};");
            sb.AppendLine();

            // Properties
            sb.AppendLine($"        public int SlabSize => _slabSize;");
            sb.AppendLine($"        public int Count => _count;");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Version counter incremented on any mutation (Allocate, Free).");
            sb.AppendLine("        /// Used by DerivedSystemRunner to detect stale caches after rollback.");
            sb.AppendLine("        /// Stored in slab header bytes 0-3 so it's serialized via memcopy.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public uint Version => *(uint*)_slab;");
            sb.AppendLine();
            if (!isDataOnly)
            {
                sb.AppendLine($"        public int CellCount => TotalCells;");
            }
            sb.AppendLine();

            return authoritativeSlabSize;
        }
    }
}
