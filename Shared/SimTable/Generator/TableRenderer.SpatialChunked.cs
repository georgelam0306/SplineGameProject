#nullable enable
using Microsoft.CodeAnalysis;
using System.Text;

namespace SimTable.Generator
{
    internal static partial class TableRenderer
    {
        /// <summary>
        /// Renders chunked spatial sort method.
        /// </summary>
        private static void RenderSpatialSortChunked(StringBuilder sb)
        {
            sb.AppendLine("        public void SpatialSort()");
            sb.AppendLine("        {");
            sb.AppendLine("            // Reset chunk registry");
            sb.AppendLine("            _chunkToPoolIndex.Clear();");
            sb.AppendLine("            _activeChunkCount = 0;");
            sb.AppendLine("            _poolRentIndex = 0;");
            sb.AppendLine();
            sb.AppendLine("            // Pass 1: Identify active chunks and count entities per chunk");
            sb.AppendLine("            for (int slot = 0; slot < _count; slot++)");
            sb.AppendLine("            {");
            sb.AppendLine("                if (_slotToStableId[slot] < 0) continue;");
            sb.AppendLine("                var chunkKey = ComputeChunkKey(slot);");
            sb.AppendLine("                if (!_chunkToPoolIndex.TryGetValue(chunkKey, out int poolIdx))");
            sb.AppendLine("                {");
            sb.AppendLine("                    poolIdx = _poolRentIndex++;");
            sb.AppendLine("                    if (poolIdx >= MaxChunks)");
            sb.AppendLine("                        throw new InvalidOperationException($\"Too many active chunks ({poolIdx}). Max is {MaxChunks}\");");
            sb.AppendLine("                    _chunkToPoolIndex[chunkKey] = poolIdx;");
            sb.AppendLine("                    _activeChunks[_activeChunkCount++] = chunkKey;");
            sb.AppendLine("                    // Clear the chunk grid and hierarchical masks");
            sb.AppendLine("                    ref var newGrid = ref _chunkGridPool[poolIdx];");
            sb.AppendLine("                    Array.Clear(newGrid.CellCount, 0, TotalCells);");
            sb.AppendLine("                    newGrid.EntityCount = 0;");
            sb.AppendLine("                    if (HasChunkL1) Array.Clear(newGrid.CoarseMaskL1, 0, ChunkL1GridSize);");
            sb.AppendLine("                    if (HasChunkL2) newGrid.CoarseMaskL2 = 0;");
            sb.AppendLine("                }");
            sb.AppendLine("                _entityChunkIndex[slot] = poolIdx;");
            sb.AppendLine("                int localCell = ComputeLocalCell(slot);");
            sb.AppendLine("                ref var grid = ref _chunkGridPool[poolIdx];");
            sb.AppendLine("                grid.CellCount[localCell]++;");
            sb.AppendLine("                grid.EntityCount++;");
            sb.AppendLine();
            sb.AppendLine("                // Set hierarchical coarse mask bits");
            sb.AppendLine("                int cellX = localCell % GridSize;");
            sb.AppendLine("                int cellY = localCell / GridSize;");
            sb.AppendLine("                if (HasChunkL1)");
            sb.AppendLine("                {");
            sb.AppendLine("                    int l1X = cellX >> ChunkL1Shift;");
            sb.AppendLine("                    int l1Y = cellY >> ChunkL1Shift;");
            sb.AppendLine("                    grid.CoarseMaskL1[l1Y] |= (1UL << l1X);");
            sb.AppendLine("                }");
            sb.AppendLine("                if (HasChunkL2)");
            sb.AppendLine("                {");
            sb.AppendLine("                    int l2X = cellX >> ChunkL2Shift;");
            sb.AppendLine("                    int l2Y = cellY >> ChunkL2Shift;");
            sb.AppendLine("                    grid.CoarseMaskL2 |= (1UL << (l2Y * ChunkL2GridSize + l2X));");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            // Sort active chunks for deterministic iteration");
            sb.AppendLine("            Array.Sort(_activeChunks, 0, _activeChunkCount);");
            sb.AppendLine();
            sb.AppendLine("            // Pass 2: Compute cell boundaries in _sortedOrder");
            sb.AppendLine("            int globalOffset = 0;");
            sb.AppendLine("            for (int i = 0; i < _activeChunkCount; i++)");
            sb.AppendLine("            {");
            sb.AppendLine("                var chunkKey = _activeChunks[i];");
            sb.AppendLine("                int poolIdx = _chunkToPoolIndex[chunkKey];");
            sb.AppendLine("                ref var grid = ref _chunkGridPool[poolIdx];");
            sb.AppendLine("                for (int cell = 0; cell < TotalCells; cell++)");
            sb.AppendLine("                {");
            sb.AppendLine("                    grid.CellStart[cell] = globalOffset;");
            sb.AppendLine("                    grid.CellWrite[cell] = globalOffset;");
            sb.AppendLine("                    globalOffset += grid.CellCount[cell];");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            // Pass 3: Place entities in sorted order");
            sb.AppendLine("            for (int slot = 0; slot < _count; slot++)");
            sb.AppendLine("            {");
            sb.AppendLine("                if (_slotToStableId[slot] < 0) continue;");
            sb.AppendLine("                int poolIdx = _entityChunkIndex[slot];");
            sb.AppendLine("                int localCell = ComputeLocalCell(slot);");
            sb.AppendLine("                int sortedIndex = _chunkGridPool[poolIdx].CellWrite[localCell]++;");
            sb.AppendLine("                _sortedOrder[sortedIndex] = slot;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        /// <summary>
        /// Renders ComputeChunkKey method for chunked mode.
        /// </summary>
        private static void RenderComputeChunkKey(StringBuilder sb, SchemaModel model)
        {
            var (hasPosition, positionTypeName) = GetPositionInfo(model);

            sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("        private ChunkKey ComputeChunkKey(int slot)");
            sb.AppendLine("        {");
            if (hasPosition && positionTypeName != null && positionTypeName.Contains("Fixed64Vec2"))
            {
                sb.AppendLine("            var pos = Position(slot);");
                sb.AppendLine("            int worldX = (int)((long)pos.X.Raw >> 16);");
                sb.AppendLine("            int worldY = (int)((long)pos.Y.Raw >> 16);");
            }
            else if (hasPosition && positionTypeName != null && positionTypeName.Contains("Fixed32Vec2"))
            {
                sb.AppendLine("            var pos = Position(slot);");
                sb.AppendLine("            int worldX = (int)(pos.X.Raw >> 8);");
                sb.AppendLine("            int worldY = (int)(pos.Y.Raw >> 8);");
            }
            else
            {
                sb.AppendLine("            int worldX = 0;");
                sb.AppendLine("            int worldY = 0;");
            }
            sb.AppendLine("            // Floor division for correct negative handling");
            sb.AppendLine("            int chunkX = worldX >= 0 ? worldX / ChunkSize : (worldX - ChunkSize + 1) / ChunkSize;");
            sb.AppendLine("            int chunkY = worldY >= 0 ? worldY / ChunkSize : (worldY - ChunkSize + 1) / ChunkSize;");
            sb.AppendLine("            return new ChunkKey(chunkX, chunkY);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        /// <summary>
        /// Renders ComputeLocalCell method for chunked mode.
        /// </summary>
        private static void RenderComputeLocalCell(StringBuilder sb, SchemaModel model)
        {
            var (hasPosition, positionTypeName) = GetPositionInfo(model);

            sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("        private int ComputeLocalCell(int slot)");
            sb.AppendLine("        {");
            if (hasPosition && positionTypeName != null && positionTypeName.Contains("Fixed64Vec2"))
            {
                sb.AppendLine("            var pos = Position(slot);");
                sb.AppendLine("            int worldX = (int)((long)pos.X.Raw >> 16);");
                sb.AppendLine("            int worldY = (int)((long)pos.Y.Raw >> 16);");
            }
            else if (hasPosition && positionTypeName != null && positionTypeName.Contains("Fixed32Vec2"))
            {
                sb.AppendLine("            var pos = Position(slot);");
                sb.AppendLine("            int worldX = (int)(pos.X.Raw >> 8);");
                sb.AppendLine("            int worldY = (int)(pos.Y.Raw >> 8);");
            }
            else
            {
                sb.AppendLine("            int worldX = 0;");
                sb.AppendLine("            int worldY = 0;");
            }
            sb.AppendLine("            // Compute local position within chunk, then cell");
            sb.AppendLine("            int localX = ((worldX % ChunkSize) + ChunkSize) % ChunkSize;  // Handle negative");
            sb.AppendLine("            int localY = ((worldY % ChunkSize) + ChunkSize) % ChunkSize;");
            sb.AppendLine("            int cellX = (localX / CellSize) & (GridSize - 1);");
            sb.AppendLine("            int cellY = (localY / CellSize) & (GridSize - 1);");
            sb.AppendLine("            return cellX + cellY * GridSize;");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        /// <summary>
        /// Renders chunk accessor methods for chunked mode.
        /// </summary>
        private static void RenderChunkAccessors(StringBuilder sb)
        {
            sb.AppendLine("        /// <summary>Number of active chunks this frame.</summary>");
            sb.AppendLine("        public int ActiveChunkCount => _activeChunkCount;");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>Gets the chunk key at the given index (0 to ActiveChunkCount-1).</summary>");
            sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("        public ChunkKey GetActiveChunk(int index) => _activeChunks[index];");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>Gets the pool index for a chunk key, or -1 if not active.</summary>");
            sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("        public int GetChunkPoolIndex(ChunkKey key) => _chunkToPoolIndex.TryGetValue(key, out int idx) ? idx : -1;");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>Gets the cell start index for a cell within a chunk.</summary>");
            sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("        public int GetChunkCellStart(int chunkPoolIndex, int cell) => _chunkGridPool[chunkPoolIndex].CellStart[cell];");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>Gets the cell count for a cell within a chunk.</summary>");
            sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("        public int GetChunkCellCount(int chunkPoolIndex, int cell) => _chunkGridPool[chunkPoolIndex].CellCount[cell];");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>Gets the total entity count for a chunk.</summary>");
            sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("        public int GetChunkEntityCount(int chunkPoolIndex) => _chunkGridPool[chunkPoolIndex].EntityCount;");
            sb.AppendLine();
        }
    }
}
