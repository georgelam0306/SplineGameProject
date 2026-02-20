#nullable enable
using Microsoft.CodeAnalysis;
using System.Text;

namespace SimTable.Generator
{
    internal static partial class TableRenderer
    {
        /// <summary>
        /// Renders constructor method.
        /// </summary>
        private static void RenderConstructor(StringBuilder sb, SchemaModel model, string tableName, bool isChunked, bool isDataOnly)
        {
            sb.AppendLine($"        public {tableName}()");
            sb.AppendLine("        {");
            sb.AppendLine("            _slabSize = TotalSlabSize;");
            sb.AppendLine("            _slab = (byte*)NativeMemory.AllocZeroed((nuint)_slabSize);");
            sb.AppendLine("            _count = 0;");
            sb.AppendLine("            _stableIdToSlot = new int[Capacity];");
            sb.AppendLine("            _slotToStableId = new int[Capacity];");
            sb.AppendLine("            _stableIdNextFree = new int[Capacity];");
            sb.AppendLine("            _generations = new int[Capacity];");
            sb.AppendLine("            _nextStableId = 0;");
            sb.AppendLine("            _stableIdFreeListHead = -1;");
            if (!isDataOnly)
            {
                sb.AppendLine("            _cellStart = new int[TotalCells];");
                sb.AppendLine("            _cellCount = new int[TotalCells];");
                sb.AppendLine("            _sortedOrder = new int[Capacity];");
                sb.AppendLine("            _cellWrite = new int[TotalCells];");
            }
            // Hierarchical coarse masks only for single-grid mode
            if (!isChunked && !isDataOnly)
            {
                sb.AppendLine("            _coarseMaskL1 = new ulong[L1GridSize];");
                sb.AppendLine("            _coarseMaskL2 = new ulong[L2GridSize];");
                sb.AppendLine("            _coarseMaskL3 = 0;");
            }
            sb.AppendLine("            for (int i = 0; i < Capacity; i++)");
            sb.AppendLine("            {");
            sb.AppendLine("                _stableIdToSlot[i] = -1;");
            sb.AppendLine("                _slotToStableId[i] = -1;");
            sb.AppendLine("            }");

            // Initialize chunked mode structures
            if (isChunked && !isDataOnly)
            {
                sb.AppendLine();
                sb.AppendLine("            // Initialize chunked mode structures");
                sb.AppendLine("            _activeChunks = new ChunkKey[MaxChunks];");
                sb.AppendLine("            _activeChunkCount = 0;");
                sb.AppendLine("            _chunkToPoolIndex = new Dictionary<ChunkKey, int>(MaxChunks);");
                sb.AppendLine("            _chunkGridPool = new ChunkGrid[MaxChunks];");
                sb.AppendLine("            _entityChunkIndex = new int[Capacity];");
                sb.AppendLine("            for (int i = 0; i < MaxChunks; i++)");
                sb.AppendLine("            {");
                sb.AppendLine("                _chunkGridPool[i] = new ChunkGrid");
                sb.AppendLine("                {");
                sb.AppendLine("                    CellStart = new int[TotalCells],");
                sb.AppendLine("                    CellCount = new int[TotalCells],");
                sb.AppendLine("                    CellWrite = new int[TotalCells],");
                sb.AppendLine("                    EntityCount = 0,");
                sb.AppendLine("                    CoarseMaskL1 = HasChunkL1 ? new ulong[ChunkL1GridSize] : Array.Empty<ulong>(),");
                sb.AppendLine("                    CoarseMaskL2 = 0");
                sb.AppendLine("                };");
                sb.AppendLine("            }");
            }

            sb.AppendLine("        }");
            sb.AppendLine();
        }

        /// <summary>
        /// Renders Dispose method.
        /// </summary>
        private static void RenderDispose(StringBuilder sb)
        {
            sb.AppendLine("        public void Dispose()");
            sb.AppendLine("        {");
            sb.AppendLine("            if (_slab != null)");
            sb.AppendLine("            {");
            sb.AppendLine("                NativeMemory.Free(_slab);");
            sb.AppendLine("                _slab = null;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        /// <summary>
        /// Renders Allocate method.
        /// </summary>
        private static void RenderAllocate(StringBuilder sb, SchemaModel model, bool isDataOnly)
        {
            sb.AppendLine("        public global::SimTable.SimHandle Allocate()");
            sb.AppendLine("        {");
            if (model.EvictionPolicy == EvictionPolicy.LRU && !string.IsNullOrEmpty(model.LRUKeyField))
            {
                // LRU eviction: find and free oldest when full
                sb.AppendLine("            if (_count >= Capacity)");
                sb.AppendLine("            {");
                sb.AppendLine("                int victimSlot = FindLRUVictim();");
                sb.AppendLine("                Free(_slotToStableId[victimSlot]);");
                sb.AppendLine("            }");
            }
            else
            {
                // Default: throw when full
                sb.AppendLine("            if (_count >= Capacity)");
                sb.AppendLine("            {");
                sb.AppendLine("                throw new InvalidOperationException(\"Table is full\");");
                sb.AppendLine("            }");
            }
            sb.AppendLine("            // Slots are always contiguous [0, Count-1], so new slot is always at _count");
            sb.AppendLine("            int slot = _count++;");
            sb.AppendLine("            int rawId;");
            sb.AppendLine("            int generation;");
            sb.AppendLine("            if (_stableIdFreeListHead >= 0)");
            sb.AppendLine("            {");
            sb.AppendLine("                // Reuse a freed rawId from dedicated free list");
            sb.AppendLine("                rawId = _stableIdFreeListHead;");
            sb.AppendLine("                _stableIdFreeListHead = _stableIdNextFree[rawId];");
            sb.AppendLine("                generation = _generations[rawId];  // Already incremented on free");
            sb.AppendLine("            }");
            sb.AppendLine("            else");
            sb.AppendLine("            {");
            sb.AppendLine("                // Allocate new rawId");
            sb.AppendLine("                rawId = _nextStableId++;");
            sb.AppendLine("                generation = 0;");
            sb.AppendLine("                if (rawId >= _stableIdToSlot.Length)");
            sb.AppendLine("                {");
            sb.AppendLine("                    int oldLength = _stableIdToSlot.Length;");
            sb.AppendLine("                    int newLength = Math.Max(rawId + 1, oldLength * 2);");
            sb.AppendLine("                    Array.Resize(ref _stableIdToSlot, newLength);");
            sb.AppendLine("                    Array.Resize(ref _stableIdNextFree, newLength);");
            sb.AppendLine("                    Array.Resize(ref _generations, newLength);");
            sb.AppendLine("                    for (int i = oldLength; i < newLength; i++)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        _stableIdToSlot[i] = -1;");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("            int packedStableId = ((generation & 0xFFFF) << 16) | (rawId & 0xFFFF);");
            sb.AppendLine("            _stableIdToSlot[rawId] = slot;");
            sb.AppendLine("            _slotToStableId[slot] = packedStableId;");

            // For data-only tables (singletons with arrays), explicitly zero the slot data
            // This ensures deterministic state even if NativeMemory has edge cases
            if (isDataOnly)
            {
                sb.AppendLine("            // Clear slot data to ensure deterministic initialization");
                sb.AppendLine($"            NativeMemory.Clear(_slab, (nuint)TotalSlabSize);");

                // Apply field initializer defaults if any
                bool hasDefaults = false;
                foreach (var col in model.Columns)
                {
                    if (!col.IsArray && !col.IsArray2D && col.DefaultValueExpression != null)
                    {
                        if (!hasDefaults)
                        {
                            sb.AppendLine("            // Apply field initializer defaults");
                            hasDefaults = true;
                        }
                        // Use ref accessor assignment: FieldName(slot) = value
                        // Handle enum name collision: if expression starts with the field name (which is also a method),
                        // fully qualify the type to avoid ambiguity (e.g., MatchState.Countdown becomes global::Namespace.MatchState.Countdown)
                        var defaultExpr = col.DefaultValueExpression!;
                        if (col.Type.TypeKind == TypeKind.Enum && defaultExpr.StartsWith(col.Name + "."))
                        {
                            var fullTypeName = col.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                            defaultExpr = fullTypeName + defaultExpr.Substring(col.Name.Length);
                        }
                        sb.AppendLine($"            {col.Name}(slot) = {defaultExpr};");
                    }
                }
            }

            // Increment version to signal mutation (for DerivedSystemRunner dependency tracking)
            sb.AppendLine();
            sb.AppendLine("            // Increment version to signal table mutation");
            sb.AppendLine("            (*(uint*)_slab)++;");
            sb.AppendLine();
            sb.AppendLine("            return new global::SimTable.SimHandle(TableIdConst, rawId, generation);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        /// <summary>
        /// Renders Free method and overloads.
        /// </summary>
        private static void RenderFree(StringBuilder sb, SchemaModel model, string rowRefName)
        {
            sb.AppendLine("        public void Free(int packedStableId)");
            sb.AppendLine("        {");
            sb.AppendLine("            int rawId = packedStableId & 0xFFFF;");
            sb.AppendLine("            if (rawId < 0 || rawId >= _stableIdToSlot.Length)");
            sb.AppendLine("            {");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine("            int slotToFree = _stableIdToSlot[rawId];");
            sb.AppendLine("            if (slotToFree < 0)");
            sb.AppendLine("            {");
            sb.AppendLine("                return;  // Already freed");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            int lastSlot = _count - 1;");
            sb.AppendLine();
            sb.AppendLine("            // Swap-and-pop: if not freeing the last slot, move last slot's data here");
            sb.AppendLine("            if (slotToFree != lastSlot)");
            sb.AppendLine("            {");
            sb.AppendLine("                int lastPackedStableId = _slotToStableId[lastSlot];");
            sb.AppendLine("                int lastRawId = lastPackedStableId & 0xFFFF;");
            sb.AppendLine();
            sb.AppendLine("                // Copy all field data from lastSlot to slotToFree");
            sb.AppendLine("                CopySlotData(lastSlot, slotToFree);");
            sb.AppendLine();
            sb.AppendLine("                // Update mappings for the moved entity");
            sb.AppendLine("                _stableIdToSlot[lastRawId] = slotToFree;");
            sb.AppendLine("                _slotToStableId[slotToFree] = lastPackedStableId;");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            // Clear the freed rawId's mapping");
            sb.AppendLine("            _stableIdToSlot[rawId] = -1;");
            sb.AppendLine();
            sb.AppendLine("            // Clear the last slot (now free)");
            sb.AppendLine("            _slotToStableId[lastSlot] = -1;");
            sb.AppendLine();
            sb.AppendLine("            // Increment generation to invalidate old handles");
            sb.AppendLine("            _generations[rawId]++;");
            sb.AppendLine();
            sb.AppendLine("            // Recycle rawId using dedicated free list");
            sb.AppendLine("            _stableIdNextFree[rawId] = _stableIdFreeListHead;");
            sb.AppendLine("            _stableIdFreeListHead = rawId;");
            sb.AppendLine();
            sb.AppendLine("            _count--;");
            sb.AppendLine();
            sb.AppendLine("            // Increment version to signal table mutation");
            sb.AppendLine("            (*(uint*)_slab)++;");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        public void Free(global::SimTable.SimHandle handle) => Free(handle.StableId);");
            sb.AppendLine($"        public void Free({rowRefName} row) => Free(row.StableId);");
            sb.AppendLine();

            // Generate FindLRUVictim helper for LRU eviction policy
            if (model.EvictionPolicy == EvictionPolicy.LRU && !string.IsNullOrEmpty(model.LRUKeyField))
            {
                sb.AppendLine("        /// <summary>");
                sb.AppendLine("        /// Finds the slot with the lowest LRU key value (oldest entry).");
                sb.AppendLine("        /// </summary>");
                sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                sb.AppendLine("        private int FindLRUVictim()");
                sb.AppendLine("        {");
                sb.AppendLine("            int victimSlot = 0;");
                sb.AppendLine($"            int oldestValue = {model.LRUKeyField}(0);");
                sb.AppendLine("            for (int slot = 1; slot < _count; slot++)");
                sb.AppendLine("            {");
                sb.AppendLine($"                int value = {model.LRUKeyField}(slot);");
                sb.AppendLine("                if (value < oldestValue)");
                sb.AppendLine("                {");
                sb.AppendLine("                    oldestValue = value;");
                sb.AppendLine("                    victimSlot = slot;");
                sb.AppendLine("                }");
                sb.AppendLine("            }");
                sb.AppendLine("            return victimSlot;");
                sb.AppendLine("        }");
                sb.AppendLine();
            }
        }

        /// <summary>
        /// Renders CopySlotData helper method.
        /// </summary>
        private static void RenderCopySlotData(StringBuilder sb, SchemaModel model)
        {
            sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("        private void CopySlotData(int srcSlot, int dstSlot)");
            sb.AppendLine("        {");
            foreach (var col in model.Columns)
            {
                if (col.IsArray2D)
                {
                    // Copy all 2D array elements using Span copy
                    sb.AppendLine($"            {col.Name}Span(srcSlot).CopyTo({col.Name}Span(dstSlot));");
                }
                else if (col.IsArray)
                {
                    // Copy all 1D array elements using Span copy
                    sb.AppendLine($"            {col.Name}Array(srcSlot).CopyTo({col.Name}Array(dstSlot));");
                }
                else
                {
                    sb.AppendLine($"            {col.Name}(dstSlot) = {col.Name}(srcSlot);");
                }
            }
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        /// <summary>
        /// Renders Reset method.
        /// </summary>
        private static void RenderReset(StringBuilder sb, SchemaModel model, bool isDataOnly)
        {
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Completely resets the table to initial state.");
            sb.AppendLine("        /// Call this when restarting the game to avoid stableId growth issues.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public void Reset()");
            sb.AppendLine("        {");
            sb.AppendLine("            _count = 0;");
            sb.AppendLine("            _nextStableId = 0;");
            sb.AppendLine("            _stableIdFreeListHead = -1;");
            sb.AppendLine("            // Reset stableIdToSlot, stableIdNextFree, and generations arrays - might have been resized");
            sb.AppendLine("            if (_stableIdToSlot.Length > Capacity)");
            sb.AppendLine("            {");
            sb.AppendLine("                _stableIdToSlot = new int[Capacity];");
            sb.AppendLine("                _stableIdNextFree = new int[Capacity];");
            sb.AppendLine("                _generations = new int[Capacity];");
            sb.AppendLine("            }");
            sb.AppendLine("            else");
            sb.AppendLine("            {");
            sb.AppendLine("                Array.Clear(_generations, 0, _generations.Length);");
            sb.AppendLine("            }");
            sb.AppendLine("            for (int i = 0; i < Capacity; i++)");
            sb.AppendLine("            {");
            sb.AppendLine("                _stableIdToSlot[i] = -1;");
            sb.AppendLine("                _slotToStableId[i] = -1;");
            sb.AppendLine("            }");
            sb.AppendLine("            // Clear the data slab");
            sb.AppendLine("            NativeMemory.Clear(_slab, (nuint)_slabSize);");

            // For AutoAllocate data-only tables, re-allocate after reset
            if (isDataOnly && model.AutoAllocate)
            {
                sb.AppendLine("            // Re-allocate singleton with defaults (AutoAllocate enabled)");
                sb.AppendLine("            Allocate();");
            }

            sb.AppendLine("        }");
            sb.AppendLine();
        }

        /// <summary>
        /// Renders slot management methods (GetSlot, GetStableId, IsSlotActive, GetHandle).
        /// </summary>
        private static void RenderSlotManagement(StringBuilder sb)
        {
            sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("        public int GetSlot(global::SimTable.SimHandle handle)");
            sb.AppendLine("        {");
            sb.AppendLine("            int rawId = handle.RawId;");
            sb.AppendLine("            if (rawId < 0 || rawId >= _stableIdToSlot.Length) return -1;");
            sb.AppendLine("            if (_generations[rawId] != handle.Generation) return -1;  // Stale handle");
            sb.AppendLine("            return _stableIdToSlot[rawId];");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("        public int GetStableId(int slot)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (slot < 0 || slot >= Capacity)");
            sb.AppendLine("            {");
            sb.AppendLine("                return -1;");
            sb.AppendLine("            }");
            sb.AppendLine("            return _slotToStableId[slot];");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        /// <summary>Checks if the given slot contains an active entity.</summary>");
            sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("        public bool IsSlotActive(int slot)");
            sb.AppendLine("        {");
            sb.AppendLine("            return slot >= 0 && slot < _count && _slotToStableId[slot] >= 0;");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("        public global::SimTable.SimHandle GetHandle(int slot)");
            sb.AppendLine("        {");
            sb.AppendLine("            int packedStableId = GetStableId(slot);");
            sb.AppendLine("            if (packedStableId < 0) return global::SimTable.SimHandle.Invalid;");
            sb.AppendLine("            int rawId = packedStableId & 0xFFFF;");
            sb.AppendLine("            int generation = (packedStableId >> 16) & 0xFFFF;");
            sb.AppendLine("            return new global::SimTable.SimHandle(TableIdConst, rawId, generation);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }
    }
}
