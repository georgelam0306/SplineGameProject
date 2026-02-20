#nullable enable
using System.Linq;
using System.Text;

namespace SimTable.Generator
{
    internal static partial class TableRenderer
    {
        /// <summary>
        /// Renders SaveTo and LoadFrom methods for slab serialization.
        /// </summary>
        private static void RenderSaveLoadSlab(StringBuilder sb)
        {
            sb.AppendLine("        public void SaveTo(byte* dest)");
            sb.AppendLine("        {");
            sb.AppendLine("            // Only serialize authoritative fields (not computed state)");
            sb.AppendLine("            Buffer.MemoryCopy(_slab, dest, AuthoritativeSlabSize, AuthoritativeSlabSize);");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        public void LoadFrom(byte* src)");
            sb.AppendLine("        {");
            sb.AppendLine("            // Only deserialize authoritative fields (computed state stays zero after load)");
            sb.AppendLine("            Buffer.MemoryCopy(src, _slab, AuthoritativeSlabSize, AuthoritativeSlabSize);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        /// <summary>
        /// Renders RecomputeAll method for computed fields.
        /// </summary>
        private static void RenderRecomputeAll(StringBuilder sb, SchemaModel model)
        {
            var computedWithExpressions = model.Columns.Where(c => c.IsComputed && c.ComputedExpression != null).ToList();
            // Get namespace from the row type - SimWorld is in the same namespace
            var ns = model.Type.ContainingNamespace?.ToDisplayString() ?? "Simulation.Components";
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Recomputes all computed fields from authoritative state.");
            sb.AppendLine("        /// Call after LoadFrom() to restore computed state after deserialization.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine($"        public void RecomputeAll(global::{ns}.SimWorld world)");
            sb.AppendLine("        {");
            if (computedWithExpressions.Count > 0)
            {
                sb.AppendLine("            for (int slot = 0; slot < _count; slot++)");
                sb.AppendLine("            {");
                foreach (var col in computedWithExpressions)
                {
                    sb.AppendLine($"                {col.Name}(slot) = {col.ComputedExpression};");
                }
                sb.AppendLine("            }");
            }
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        /// <summary>
        /// Renders SaveMetaTo and LoadMetaFrom methods for metadata serialization.
        /// </summary>
        private static void RenderMetaSerialization(StringBuilder sb)
        {
            // MaxStableIdCapacity constant
            sb.AppendLine("        private const int MaxStableIdCapacity = Capacity;");
            sb.AppendLine();

            // SaveMetaTo
            sb.AppendLine("        public void SaveMetaTo(Span<byte> dest)");
            sb.AppendLine("        {");
            sb.AppendLine("            int offset = 0;");
            sb.AppendLine("            MemoryMarshal.Write(dest.Slice(offset), ref _count);");
            sb.AppendLine("            offset += 4;");
            sb.AppendLine("            MemoryMarshal.Write(dest.Slice(offset), ref _nextStableId);");
            sb.AppendLine("            offset += 4;");
            sb.AppendLine("            MemoryMarshal.Write(dest.Slice(offset), ref _stableIdFreeListHead);");
            sb.AppendLine("            offset += 4;");
            sb.AppendLine("            int stableIdCount = Math.Min(_nextStableId, MaxStableIdCapacity);");
            sb.AppendLine("            for (int i = 0; i < stableIdCount && i < _stableIdToSlot.Length; i++)");
            sb.AppendLine("            {");
            sb.AppendLine("                MemoryMarshal.Write(dest.Slice(offset), ref _stableIdToSlot[i]);");
            sb.AppendLine("                offset += 4;");
            sb.AppendLine("            }");
            sb.AppendLine("            int neg = -1;");
            sb.AppendLine("            for (int i = stableIdCount; i < MaxStableIdCapacity; i++)");
            sb.AppendLine("            {");
            sb.AppendLine("                MemoryMarshal.Write(dest.Slice(offset), ref neg);");
            sb.AppendLine("                offset += 4;");
            sb.AppendLine("            }");
            sb.AppendLine("            for (int i = 0; i < Capacity; i++)");
            sb.AppendLine("            {");
            sb.AppendLine("                MemoryMarshal.Write(dest.Slice(offset), ref _slotToStableId[i]);");
            sb.AppendLine("                offset += 4;");
            sb.AppendLine("            }");
            sb.AppendLine("            for (int i = 0; i < stableIdCount && i < _stableIdNextFree.Length; i++)");
            sb.AppendLine("            {");
            sb.AppendLine("                MemoryMarshal.Write(dest.Slice(offset), ref _stableIdNextFree[i]);");
            sb.AppendLine("                offset += 4;");
            sb.AppendLine("            }");
            sb.AppendLine("            int zero = 0;");
            sb.AppendLine("            for (int i = stableIdCount; i < MaxStableIdCapacity; i++)");
            sb.AppendLine("            {");
            sb.AppendLine("                MemoryMarshal.Write(dest.Slice(offset), ref zero);");
            sb.AppendLine("                offset += 4;");
            sb.AppendLine("            }");
            sb.AppendLine("            // Save _generations for generational handle validation");
            sb.AppendLine("            for (int i = 0; i < stableIdCount && i < _generations.Length; i++)");
            sb.AppendLine("            {");
            sb.AppendLine("                MemoryMarshal.Write(dest.Slice(offset), ref _generations[i]);");
            sb.AppendLine("                offset += 4;");
            sb.AppendLine("            }");
            sb.AppendLine("            for (int i = stableIdCount; i < MaxStableIdCapacity; i++)");
            sb.AppendLine("            {");
            sb.AppendLine("                MemoryMarshal.Write(dest.Slice(offset), ref zero);");
            sb.AppendLine("                offset += 4;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();

            // LoadMetaFrom
            sb.AppendLine("        public void LoadMetaFrom(ReadOnlySpan<byte> src)");
            sb.AppendLine("        {");
            sb.AppendLine("            int offset = 0;");
            sb.AppendLine("            _count = MemoryMarshal.Read<int>(src.Slice(offset));");
            sb.AppendLine("            offset += 4;");
            sb.AppendLine("            _nextStableId = MemoryMarshal.Read<int>(src.Slice(offset));");
            sb.AppendLine("            offset += 4;");
            sb.AppendLine("            _stableIdFreeListHead = MemoryMarshal.Read<int>(src.Slice(offset));");
            sb.AppendLine("            offset += 4;");
            sb.AppendLine("            int requiredSize = Math.Max(_nextStableId, Capacity);");
            sb.AppendLine("            if (_stableIdToSlot.Length < requiredSize)");
            sb.AppendLine("            {");
            sb.AppendLine("                Array.Resize(ref _stableIdToSlot, requiredSize);");
            sb.AppendLine("                Array.Resize(ref _stableIdNextFree, requiredSize);");
            sb.AppendLine("                Array.Resize(ref _generations, requiredSize);");
            sb.AppendLine("            }");
            sb.AppendLine("            int stableIdCount = Math.Min(_nextStableId, MaxStableIdCapacity);");
            sb.AppendLine("            for (int i = 0; i < stableIdCount; i++)");
            sb.AppendLine("            {");
            sb.AppendLine("                _stableIdToSlot[i] = MemoryMarshal.Read<int>(src.Slice(offset));");
            sb.AppendLine("                offset += 4;");
            sb.AppendLine("            }");
            sb.AppendLine("            offset += (MaxStableIdCapacity - stableIdCount) * 4;");
            sb.AppendLine("            for (int i = stableIdCount; i < _stableIdToSlot.Length; i++)");
            sb.AppendLine("            {");
            sb.AppendLine("                _stableIdToSlot[i] = -1;");
            sb.AppendLine("            }");
            sb.AppendLine("            for (int i = 0; i < Capacity; i++)");
            sb.AppendLine("            {");
            sb.AppendLine("                _slotToStableId[i] = MemoryMarshal.Read<int>(src.Slice(offset));");
            sb.AppendLine("                offset += 4;");
            sb.AppendLine("            }");
            sb.AppendLine("            for (int i = 0; i < stableIdCount; i++)");
            sb.AppendLine("            {");
            sb.AppendLine("                _stableIdNextFree[i] = MemoryMarshal.Read<int>(src.Slice(offset));");
            sb.AppendLine("                offset += 4;");
            sb.AppendLine("            }");
            sb.AppendLine("            offset += (MaxStableIdCapacity - stableIdCount) * 4;");
            sb.AppendLine("            // Load _generations for generational handle validation");
            sb.AppendLine("            for (int i = 0; i < stableIdCount; i++)");
            sb.AppendLine("            {");
            sb.AppendLine("                _generations[i] = MemoryMarshal.Read<int>(src.Slice(offset));");
            sb.AppendLine("                offset += 4;");
            sb.AppendLine("            }");
            sb.AppendLine("            offset += (MaxStableIdCapacity - stableIdCount) * 4;");
            sb.AppendLine("            for (int i = stableIdCount; i < _generations.Length; i++)");
            sb.AppendLine("            {");
            sb.AppendLine("                _generations[i] = 0;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();

            // MetaSize constant
            sb.AppendLine($"        public const int MetaSize = 12 + MaxStableIdCapacity * 4 + Capacity * 4 + MaxStableIdCapacity * 4 + MaxStableIdCapacity * 4;");
            sb.AppendLine();
        }
    }
}
