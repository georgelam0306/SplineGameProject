#nullable enable
using Microsoft.CodeAnalysis;
using System.Text;

namespace SimTable.Generator
{
    internal static partial class TableRenderer
    {
        /// <summary>
        /// Renders single-grid spatial sort method.
        /// </summary>
        private static void RenderSpatialSortSingle(StringBuilder sb)
        {
            sb.AppendLine("        public void SpatialSort()");
            sb.AppendLine("        {");
            sb.AppendLine("            Array.Clear(_cellCount, 0, TotalCells);");
            sb.AppendLine("            Array.Clear(_coarseMaskL1, 0, L1GridSize);");
            sb.AppendLine("            Array.Clear(_coarseMaskL2, 0, L2GridSize);");
            sb.AppendLine("            _coarseMaskL3 = 0;");
            sb.AppendLine();
            sb.AppendLine("            for (int slot = 0; slot < _count; slot++)");
            sb.AppendLine("            {");
            sb.AppendLine("                if (_slotToStableId[slot] < 0)");
            sb.AppendLine("                {");
            sb.AppendLine("                    continue;");
            sb.AppendLine("                }");
            sb.AppendLine("                int cell = ComputeCell(slot);");
            sb.AppendLine("                _cellCount[cell]++;");
            sb.AppendLine();
            sb.AppendLine("                // Set hierarchical coarse mask bits");
            sb.AppendLine("                int cellX = cell % GridSize;");
            sb.AppendLine("                int cellY = cell / GridSize;");
            sb.AppendLine();
            sb.AppendLine("                // L1: 4×4 fine cells");
            sb.AppendLine("                int l1X = cellX >> L1Shift;");
            sb.AppendLine("                int l1Y = cellY >> L1Shift;");
            sb.AppendLine("                _coarseMaskL1[l1Y] |= (1UL << l1X);");
            sb.AppendLine();
            sb.AppendLine("                // L2: 16×16 fine cells");
            sb.AppendLine("                int l2X = cellX >> L2Shift;");
            sb.AppendLine("                int l2Y = cellY >> L2Shift;");
            sb.AppendLine("                _coarseMaskL2[l2Y] |= (1UL << l2X);");
            sb.AppendLine();
            sb.AppendLine("                // L3: 64×64 fine cells");
            sb.AppendLine("                int l3X = cellX >> L3Shift;");
            sb.AppendLine("                int l3Y = cellY >> L3Shift;");
            sb.AppendLine("                _coarseMaskL3 |= (1UL << (l3Y * L3GridSize + l3X));");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            int runningTotal = 0;");
            sb.AppendLine("            for (int cell = 0; cell < TotalCells; cell++)");
            sb.AppendLine("            {");
            sb.AppendLine("                _cellStart[cell] = runningTotal;");
            sb.AppendLine("                _cellWrite[cell] = runningTotal;");
            sb.AppendLine("                runningTotal += _cellCount[cell];");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            for (int slot = 0; slot < _count; slot++)");
            sb.AppendLine("            {");
            sb.AppendLine("                if (_slotToStableId[slot] < 0)");
            sb.AppendLine("                {");
            sb.AppendLine("                    continue;");
            sb.AppendLine("                }");
            sb.AppendLine("                int cell = ComputeCell(slot);");
            sb.AppendLine("                int sortedIndex = _cellWrite[cell]++;");
            sb.AppendLine("                _sortedOrder[sortedIndex] = slot;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        /// <summary>
        /// Renders ComputeCell method for single-grid mode.
        /// </summary>
        private static void RenderComputeCell(StringBuilder sb, SchemaModel model)
        {
            sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("        private int ComputeCell(int slot)");
            sb.AppendLine("        {");
            sb.AppendLine("            int x = 0;");
            sb.AppendLine("            int y = 0;");

            var (hasPosition, positionTypeName) = GetPositionInfo(model);

            if (hasPosition && positionTypeName != null)
            {
                // Position is a vec2 type (Fixed64Vec2 or Fixed32Vec2)
                // Use clamping to match query behavior (no wrapping)
                if (positionTypeName.Contains("Fixed64Vec2"))
                {
                    sb.AppendLine($"            var pos = Position(slot);");
                    sb.AppendLine("            x = Math.Clamp((int)((long)pos.X.Raw >> 16) / CellSize, 0, GridSize - 1);");
                    sb.AppendLine("            y = Math.Clamp((int)((long)pos.Y.Raw >> 16) / CellSize, 0, GridSize - 1);");
                }
                else if (positionTypeName.Contains("Fixed32Vec2"))
                {
                    sb.AppendLine($"            var pos = Position(slot);");
                    sb.AppendLine("            x = Math.Clamp((int)(pos.X.Raw >> 8) / CellSize, 0, GridSize - 1);");
                    sb.AppendLine("            y = Math.Clamp((int)(pos.Y.Raw >> 8) / CellSize, 0, GridSize - 1);");
                }
            }

            sb.AppendLine("            return x + y * GridSize;");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        /// <summary>
        /// Renders box and radius query enumerables for single-grid mode.
        /// Uses 3-level hierarchical coarse masks for fast empty-region skipping.
        /// </summary>
        private static void RenderSpatialQueriesSingle(StringBuilder sb, string tableName)
        {
            // Box query
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Returns a zero-allocation enumerator for entities within the bounding box.");
            sb.AppendLine("        /// Usage: foreach (int slot in table.QueryBox(minX, maxX, minY, maxY)) { ... }");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public BoxQueryEnumerable QueryBox(Fixed64 minX, Fixed64 maxX, Fixed64 minY, Fixed64 maxY)");
            sb.AppendLine("        {");
            sb.AppendLine("            return new BoxQueryEnumerable(this, minX, maxX, minY, maxY);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>Zero-allocation enumerable for box queries (supports foreach).</summary>");
            sb.AppendLine("        public readonly ref struct BoxQueryEnumerable");
            sb.AppendLine("        {");
            sb.AppendLine($"            private readonly {tableName} _table;");
            sb.AppendLine("            private readonly Fixed64 _minX, _maxX, _minY, _maxY;");
            sb.AppendLine();
            sb.AppendLine($"            public BoxQueryEnumerable({tableName} table, Fixed64 minX, Fixed64 maxX, Fixed64 minY, Fixed64 maxY)");
            sb.AppendLine("            {");
            sb.AppendLine("                _table = table;");
            sb.AppendLine("                _minX = minX;");
            sb.AppendLine("                _maxX = maxX;");
            sb.AppendLine("                _minY = minY;");
            sb.AppendLine("                _maxY = maxY;");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            public BoxQueryEnumerator GetEnumerator() => new BoxQueryEnumerator(_table, _minX, _maxX, _minY, _maxY);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>Zero-allocation enumerator for box queries.</summary>");
            sb.AppendLine("        public ref struct BoxQueryEnumerator");
            sb.AppendLine("        {");
            sb.AppendLine($"            private readonly {tableName} _table;");
            sb.AppendLine("            private readonly Fixed64 _minX, _maxX, _minY, _maxY;");
            sb.AppendLine("            private readonly int _minCellX, _maxCellX, _minCellY, _maxCellY;");
            sb.AppendLine("            private int _cellX, _cellY;");
            sb.AppendLine("            private int _entityIdx;");
            sb.AppendLine("            private int _entityEnd;");
            sb.AppendLine("            private int _current;");
            sb.AppendLine();
            sb.AppendLine($"            public BoxQueryEnumerator({tableName} table, Fixed64 minX, Fixed64 maxX, Fixed64 minY, Fixed64 maxY)");
            sb.AppendLine("            {");
            sb.AppendLine("                _table = table;");
            sb.AppendLine("                _minX = minX;");
            sb.AppendLine("                _maxX = maxX;");
            sb.AppendLine("                _minY = minY;");
            sb.AppendLine("                _maxY = maxY;");
            sb.AppendLine();
            sb.AppendLine("                // Compute cell range from world coordinates");
            sb.AppendLine("                int minWorldX = minX.ToInt();");
            sb.AppendLine("                int maxWorldX = maxX.ToInt();");
            sb.AppendLine("                int minWorldY = minY.ToInt();");
            sb.AppendLine("                int maxWorldY = maxY.ToInt();");
            sb.AppendLine();
            sb.AppendLine("                // Clamp cell range to valid bounds (no wrapping)");
            sb.AppendLine("                _minCellX = Math.Clamp(minWorldX / CellSize, 0, GridSize - 1);");
            sb.AppendLine("                _maxCellX = Math.Clamp(maxWorldX / CellSize, 0, GridSize - 1);");
            sb.AppendLine("                _minCellY = Math.Clamp(minWorldY / CellSize, 0, GridSize - 1);");
            sb.AppendLine("                _maxCellY = Math.Clamp(maxWorldY / CellSize, 0, GridSize - 1);");
            sb.AppendLine();
            sb.AppendLine("                _cellX = _minCellX - 1;");
            sb.AppendLine("                _cellY = _minCellY;");
            sb.AppendLine("                _entityIdx = 0;");
            sb.AppendLine("                _entityEnd = 0;");
            sb.AppendLine("                _current = -1;");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            public int Current => _current;");
            sb.AppendLine();
            sb.AppendLine("            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("            public bool MoveNext()");
            sb.AppendLine("            {");
            sb.AppendLine("                while (true)");
            sb.AppendLine("                {");
            sb.AppendLine("                    while (_entityIdx < _entityEnd)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        int slot = _table._sortedOrder[_entityIdx++];");
            sb.AppendLine("                        var pos = _table.Position(slot);");
            sb.AppendLine("                        if (pos.X >= _minX && pos.X <= _maxX &&");
            sb.AppendLine("                            pos.Y >= _minY && pos.Y <= _maxY)");
            sb.AppendLine("                        {");
            sb.AppendLine("                            _current = slot;");
            sb.AppendLine("                            return true;");
            sb.AppendLine("                        }");
            sb.AppendLine("                    }");
            sb.AppendLine();
            sb.AppendLine("                    if (!AdvanceToNextCell())");
            sb.AppendLine("                    {");
            sb.AppendLine("                        _current = -1;");
            sb.AppendLine("                        return false;");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("            private bool AdvanceToNextCell()");
            sb.AppendLine("            {");
            sb.AppendLine("                while (true)");
            sb.AppendLine("                {");
            sb.AppendLine("                    _cellX++;");
            sb.AppendLine("                    if (_cellX > _maxCellX)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        _cellX = _minCellX;");
            sb.AppendLine("                        _cellY++;");
            sb.AppendLine("                        if (_cellY > _maxCellY)");
            sb.AppendLine("                            return false;");
            sb.AppendLine("                    }");
            sb.AppendLine();
            sb.AppendLine("                    // Hierarchical coarse mask checks - skip empty regions");
            sb.AppendLine();
            sb.AppendLine("                    // L3: 64×64 fine cells (2048px blocks)");
            sb.AppendLine("                    int l3X = _cellX >> L3Shift;");
            sb.AppendLine("                    int l3Y = _cellY >> L3Shift;");
            sb.AppendLine("                    if ((_table._coarseMaskL3 & (1UL << (l3Y * L3GridSize + l3X))) == 0)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        int nextL3X = (l3X + 1) << L3Shift;");
            sb.AppendLine("                        _cellX = Math.Min(nextL3X, _maxCellX + 1) - 1;");
            sb.AppendLine("                        continue;");
            sb.AppendLine("                    }");
            sb.AppendLine();
            sb.AppendLine("                    // L2: 16×16 fine cells (512px blocks)");
            sb.AppendLine("                    int l2X = _cellX >> L2Shift;");
            sb.AppendLine("                    int l2Y = _cellY >> L2Shift;");
            sb.AppendLine("                    if ((_table._coarseMaskL2[l2Y] & (1UL << l2X)) == 0)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        int nextL2X = (l2X + 1) << L2Shift;");
            sb.AppendLine("                        _cellX = Math.Min(nextL2X, _maxCellX + 1) - 1;");
            sb.AppendLine("                        continue;");
            sb.AppendLine("                    }");
            sb.AppendLine();
            sb.AppendLine("                    // L1: 4×4 fine cells (128px blocks)");
            sb.AppendLine("                    int l1X = _cellX >> L1Shift;");
            sb.AppendLine("                    int l1Y = _cellY >> L1Shift;");
            sb.AppendLine("                    if ((_table._coarseMaskL1[l1Y] & (1UL << l1X)) == 0)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        int nextL1X = (l1X + 1) << L1Shift;");
            sb.AppendLine("                        _cellX = Math.Min(nextL1X, _maxCellX + 1) - 1;");
            sb.AppendLine("                        continue;");
            sb.AppendLine("                    }");
            sb.AppendLine();
            sb.AppendLine("                    int cellIdx = _cellX + _cellY * GridSize;");
            sb.AppendLine("                    int count = _table._cellCount[cellIdx];");
            sb.AppendLine("                    if (count > 0)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        _entityIdx = _table._cellStart[cellIdx];");
            sb.AppendLine("                        _entityEnd = _entityIdx + count;");
            sb.AppendLine("                        return true;");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Radius query
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Returns a zero-allocation enumerator for entities within a circular radius.");
            sb.AppendLine("        /// Pass the actual radius (not squared) - squared distance is computed internally.");
            sb.AppendLine("        /// Usage: foreach (int slot in table.QueryRadius(center, radius)) { ... }");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public RadiusQueryEnumerable QueryRadius(Fixed64Vec2 center, Fixed64 radius)");
            sb.AppendLine("        {");
            sb.AppendLine("            return new RadiusQueryEnumerable(this, center, radius);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>Zero-allocation enumerable for radius queries (supports foreach).</summary>");
            sb.AppendLine("        public readonly ref struct RadiusQueryEnumerable");
            sb.AppendLine("        {");
            sb.AppendLine($"            private readonly {tableName} _table;");
            sb.AppendLine("            private readonly Fixed64Vec2 _center;");
            sb.AppendLine("            private readonly Fixed64 _radius;");
            sb.AppendLine();
            sb.AppendLine($"            public RadiusQueryEnumerable({tableName} table, Fixed64Vec2 center, Fixed64 radius)");
            sb.AppendLine("            {");
            sb.AppendLine("                _table = table;");
            sb.AppendLine("                _center = center;");
            sb.AppendLine("                _radius = radius;");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            public RadiusQueryEnumerator GetEnumerator() => new RadiusQueryEnumerator(_table, _center, _radius);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>Zero-allocation enumerator for radius queries.</summary>");
            sb.AppendLine("        public ref struct RadiusQueryEnumerator");
            sb.AppendLine("        {");
            sb.AppendLine($"            private readonly {tableName} _table;");
            sb.AppendLine("            private readonly Fixed64Vec2 _center;");
            sb.AppendLine("            private readonly Fixed64 _radiusSq;");
            sb.AppendLine("            private readonly int _minCellX, _maxCellX, _minCellY, _maxCellY;");
            sb.AppendLine("            private int _cellX, _cellY;");
            sb.AppendLine("            private int _entityIdx;");
            sb.AppendLine("            private int _entityEnd;");
            sb.AppendLine("            private int _current;");
            sb.AppendLine();
            sb.AppendLine($"            public RadiusQueryEnumerator({tableName} table, Fixed64Vec2 center, Fixed64 radius)");
            sb.AppendLine("            {");
            sb.AppendLine("                _table = table;");
            sb.AppendLine("                _center = center;");
            sb.AppendLine("                _radiusSq = radius * radius;  // Compute once, no Sqrt needed");
            sb.AppendLine();
            sb.AppendLine("                // Use radius directly - no expensive Sqrt call");
            sb.AppendLine("                int centerX = center.X.ToInt();");
            sb.AppendLine("                int centerY = center.Y.ToInt();");
            sb.AppendLine("                int radiusInt = radius.ToInt() + 1;  // +1 for safety margin");
            sb.AppendLine();
            sb.AppendLine("                // Clamp cell range to valid bounds (no wrapping)");
            sb.AppendLine("                _minCellX = Math.Clamp((centerX - radiusInt) / CellSize, 0, GridSize - 1);");
            sb.AppendLine("                _maxCellX = Math.Clamp((centerX + radiusInt) / CellSize, 0, GridSize - 1);");
            sb.AppendLine("                _minCellY = Math.Clamp((centerY - radiusInt) / CellSize, 0, GridSize - 1);");
            sb.AppendLine("                _maxCellY = Math.Clamp((centerY + radiusInt) / CellSize, 0, GridSize - 1);");
            sb.AppendLine();
            sb.AppendLine("                _cellX = _minCellX - 1;");
            sb.AppendLine("                _cellY = _minCellY;");
            sb.AppendLine("                _entityIdx = 0;");
            sb.AppendLine("                _entityEnd = 0;");
            sb.AppendLine("                _current = -1;");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            public int Current => _current;");
            sb.AppendLine();
            sb.AppendLine("            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("            public bool MoveNext()");
            sb.AppendLine("            {");
            sb.AppendLine("                while (true)");
            sb.AppendLine("                {");
            sb.AppendLine("                    while (_entityIdx < _entityEnd)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        int slot = _table._sortedOrder[_entityIdx++];");
            sb.AppendLine("                        var pos = _table.Position(slot);");
            sb.AppendLine("                        Fixed64 distSq = Fixed64Vec2.DistanceSquared(_center, pos);");
            sb.AppendLine("                        if (distSq <= _radiusSq)");
            sb.AppendLine("                        {");
            sb.AppendLine("                            _current = slot;");
            sb.AppendLine("                            return true;");
            sb.AppendLine("                        }");
            sb.AppendLine("                    }");
            sb.AppendLine();
            sb.AppendLine("                    if (!AdvanceToNextCell())");
            sb.AppendLine("                    {");
            sb.AppendLine("                        _current = -1;");
            sb.AppendLine("                        return false;");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("            private bool AdvanceToNextCell()");
            sb.AppendLine("            {");
            sb.AppendLine("                while (true)");
            sb.AppendLine("                {");
            sb.AppendLine("                    _cellX++;");
            sb.AppendLine("                    if (_cellX > _maxCellX)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        _cellX = _minCellX;");
            sb.AppendLine("                        _cellY++;");
            sb.AppendLine("                        if (_cellY > _maxCellY)");
            sb.AppendLine("                            return false;");
            sb.AppendLine("                    }");
            sb.AppendLine();
            sb.AppendLine("                    // Hierarchical coarse mask checks - skip empty regions");
            sb.AppendLine();
            sb.AppendLine("                    // L3: 64×64 fine cells (2048px blocks)");
            sb.AppendLine("                    int l3X = _cellX >> L3Shift;");
            sb.AppendLine("                    int l3Y = _cellY >> L3Shift;");
            sb.AppendLine("                    if ((_table._coarseMaskL3 & (1UL << (l3Y * L3GridSize + l3X))) == 0)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        int nextL3X = (l3X + 1) << L3Shift;");
            sb.AppendLine("                        _cellX = Math.Min(nextL3X, _maxCellX + 1) - 1;");
            sb.AppendLine("                        continue;");
            sb.AppendLine("                    }");
            sb.AppendLine();
            sb.AppendLine("                    // L2: 16×16 fine cells (512px blocks)");
            sb.AppendLine("                    int l2X = _cellX >> L2Shift;");
            sb.AppendLine("                    int l2Y = _cellY >> L2Shift;");
            sb.AppendLine("                    if ((_table._coarseMaskL2[l2Y] & (1UL << l2X)) == 0)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        int nextL2X = (l2X + 1) << L2Shift;");
            sb.AppendLine("                        _cellX = Math.Min(nextL2X, _maxCellX + 1) - 1;");
            sb.AppendLine("                        continue;");
            sb.AppendLine("                    }");
            sb.AppendLine();
            sb.AppendLine("                    // L1: 4×4 fine cells (128px blocks)");
            sb.AppendLine("                    int l1X = _cellX >> L1Shift;");
            sb.AppendLine("                    int l1Y = _cellY >> L1Shift;");
            sb.AppendLine("                    if ((_table._coarseMaskL1[l1Y] & (1UL << l1X)) == 0)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        int nextL1X = (l1X + 1) << L1Shift;");
            sb.AppendLine("                        _cellX = Math.Min(nextL1X, _maxCellX + 1) - 1;");
            sb.AppendLine("                        continue;");
            sb.AppendLine("                    }");
            sb.AppendLine();
            sb.AppendLine("                    int cellIdx = _cellX + _cellY * GridSize;");
            sb.AppendLine("                    int count = _table._cellCount[cellIdx];");
            sb.AppendLine("                    if (count > 0)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        _entityIdx = _table._cellStart[cellIdx];");
            sb.AppendLine("                        _entityEnd = _entityIdx + count;");
            sb.AppendLine("                        return true;");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        /// <summary>
        /// Renders cell accessor methods for single-grid mode.
        /// </summary>
        private static void RenderCellAccessors(StringBuilder sb)
        {
            sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("        public int GetCellStart(int cell)");
            sb.AppendLine("        {");
            sb.AppendLine("            return _cellStart[cell];");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("        public int GetCellCount(int cell)");
            sb.AppendLine("        {");
            sb.AppendLine("            return _cellCount[cell];");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("        public int GetSortedSlot(int sortedIndex)");
            sb.AppendLine("        {");
            sb.AppendLine("            return _sortedOrder[sortedIndex];");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        public int ComputeCellFromPosition(long xRaw, long yRaw)");
            sb.AppendLine("        {");
            sb.AppendLine("            int cellX = ((int)(xRaw >> 16) / CellSize) & (GridSize - 1);");
            sb.AppendLine("            int cellY = ((int)(yRaw >> 16) / CellSize) & (GridSize - 1);");
            sb.AppendLine("            return cellX + cellY * GridSize;");
            sb.AppendLine("        }");
            sb.AppendLine();
        }
    }
}
