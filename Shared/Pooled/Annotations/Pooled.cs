// SPDX-License-Identifier: MIT
#nullable enable
using System;

namespace Pooled
{
	public enum ColumnStorage : byte
	{
		Pool = 0,
		Sidecar = 1
	}

	public enum RefCountKind : byte
	{
		None = 0,
		CpuOnly = 1
	}

	public enum PoolResetPolicy
	{
		FreeList,
		FrameReset,
		Never
	}

	[AttributeUsage(AttributeTargets.Struct)]
	public sealed class PooledAttribute : Attribute
	{
		public bool SoA { get; init; } = true;
		public int InitialCapacity { get; init; } = 256;
		public ushort PoolId { get; init; } = 0;
		public bool ThreadSafe { get; init; } = false;
		public PoolResetPolicy ResetPolicy { get; init; } = PoolResetPolicy.FreeList;
		public bool GenerateStableId { get; init; } = true;
		public RefCountKind RefCounting { get; init; } = RefCountKind.None;
	}

	[AttributeUsage(AttributeTargets.Field)]
	public sealed class ColumnAttribute : Attribute
	{
		public bool Inline { get; init; } = false;
		public bool ReadOnly { get; init; } = false;
		public bool NoSerialize { get; init; } = false;
		public ColumnStorage Storage { get; init; } = ColumnStorage.Pool;
	}
}
