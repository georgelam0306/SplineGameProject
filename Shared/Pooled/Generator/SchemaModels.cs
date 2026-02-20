// SPDX-License-Identifier: MIT
#nullable enable
using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace Pooled.Generator
{
	internal sealed class ColumnInfo
	{
		public string Name { get; }
		public ITypeSymbol Type { get; }
		public bool Inline { get; }
		public bool ReadOnly { get; }
		public bool NoSerialize { get; }
		public byte Storage { get; }
		public ColumnInfo(string name, ITypeSymbol type, bool inline, bool readOnly, bool noSerialize, byte storage)
		{
			Name = name; Type = type; Inline = inline; ReadOnly = readOnly; NoSerialize = noSerialize; Storage = storage;
		}
	}

	internal sealed class SchemaModel
	{
		public INamedTypeSymbol Type { get; }
		public bool SoA { get; }
		public int InitialCapacity { get; }
		public ushort PoolId { get; }
		public bool ThreadSafe { get; }
		public int ResetPolicy { get; }
		public bool GenerateStableId { get; }
		public byte RefCounting { get; }
		public List<ColumnInfo> Columns { get; } = new List<ColumnInfo>();

		public SchemaModel(INamedTypeSymbol type, bool soa, int initialCapacity, ushort poolId, bool threadSafe, int resetPolicy, bool generateStableId, byte refCounting)
		{
			Type = type; SoA = soa; InitialCapacity = initialCapacity; PoolId = poolId; ThreadSafe = threadSafe; ResetPolicy = resetPolicy; GenerateStableId = generateStableId; RefCounting = refCounting;
		}
	}
}


