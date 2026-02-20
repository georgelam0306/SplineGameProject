#nullable enable
using System;

namespace SimTable
{
    /// <summary>
    /// Cache eviction policy for SimTables when full.
    /// </summary>
    public enum EvictionPolicy
    {
        /// <summary>No eviction - throws exception when full (default).</summary>
        None = 0,
        /// <summary>Least Recently Used - evicts row with lowest LRUKeyField value.</summary>
        LRU = 1
    }

    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class SimTableAttribute : Attribute
    {
        public int Capacity { get; init; } = 1024;
        public int CellSize { get; init; } = 16;
        public int GridSize { get; init; } = 256;

        /// <summary>
        /// World units per chunk dimension. When > 0, enables chunked spatial mode
        /// where multiple grids tile across an infinite world. Each chunk has its own
        /// GridSize × GridSize cell grid. Set to 0 (default) for single-grid mode.
        /// </summary>
        public int ChunkSize { get; init; } = 0;

        /// <summary>
        /// Eviction policy when table is full. Default is None (throws exception).
        /// </summary>
        public EvictionPolicy EvictionPolicy { get; init; } = EvictionPolicy.None;

        /// <summary>
        /// Field name to use as LRU key (must be int type). Required when EvictionPolicy is LRU.
        /// The row with the lowest value in this field will be evicted when the table is full.
        /// </summary>
        public string LRUKeyField { get; init; } = "";
    }

    /// <summary>
    /// Marks a struct for SimTable code generation without spatial partitioning.
    /// Use for singleton or data-only tables that don't need spatial queries.
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class SimDataTableAttribute : Attribute
    {
        public int Capacity { get; init; } = 1;

        /// <summary>
        /// When true (default), SimWorld constructor auto-allocates one row with field initializer defaults.
        /// Reset() also re-allocates after clearing. Set to false to disable auto-allocation.
        /// </summary>
        public bool AutoAllocate { get; init; } = true;
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ColumnAttribute : Attribute
    {
        public bool NoSerialize { get; init; } = false;
    }

    /// <summary>
    /// Marks a field as computed state - stored in the slab but not serialized.
    /// Computed fields are placed after authoritative fields in the contiguous slab.
    /// After deserialization, computed fields must be recomputed via RecomputeAll().
    /// Use with a Setup method marked [Conditional("COMPUTED_STATE_SETUP")] to define computation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ComputedStateAttribute : Attribute
    {
    }

    /// <summary>
    /// Builder interface for defining computed field expressions.
    /// Used in Setup methods with [Conditional("COMPUTED_STATE_SETUP")].
    /// The generator parses these expressions at compile time to generate RecomputeAll().
    /// </summary>
    /// <typeparam name="T">The row struct type.</typeparam>
    public interface IComputedStateBuilder<T> where T : struct
    {
        /// <summary>
        /// Defines a computed field expression using row-local fields.
        /// Use [CachedStat] fields to access data from GameDocDb.
        /// </summary>
        /// <typeparam name="TField">The field type.</typeparam>
        /// <param name="targetField">Selector for the computed field (e.g., r => r.FinalSpeed).</param>
        /// <param name="computation">Expression to compute the value (e.g., r => r.BaseSpeed * r.Modifier).</param>
        /// <returns>The builder for chaining.</returns>
        IComputedStateBuilder<T> Compute<TField>(
            System.Linq.Expressions.Expression<Func<T, TField>> targetField,
            System.Linq.Expressions.Expression<Func<T, TField>> computation);

        /// <summary>
        /// Defines a computed field expression with cross-table access via SimWorld.
        /// Use when computation depends on data from other tables.
        /// </summary>
        /// <typeparam name="TField">The field type.</typeparam>
        /// <typeparam name="TWorld">The SimWorld type (inferred).</typeparam>
        /// <param name="targetField">Selector for the computed field.</param>
        /// <param name="computation">Expression with world access (e.g., (r, world) => world.BuildingRows.Field(r.Slot) + r.Base).</param>
        /// <returns>The builder for chaining.</returns>
        IComputedStateBuilder<T> Compute<TField, TWorld>(
            System.Linq.Expressions.Expression<Func<T, TField>> targetField,
            System.Linq.Expressions.Expression<Func<T, TWorld, TField>> computation);
    }

    /// <summary>
    /// Marks a field as a fixed-size array. The generator will expand this into
    /// individual named fields (e.g., Field0, Field1...) plus array accessors.
    /// Memory is laid out contiguously per-slot for cache-friendly access.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ArrayAttribute : Attribute
    {
        public int Length { get; }

        public ArrayAttribute(int length)
        {
            if (length <= 0 || length > 64)
                throw new ArgumentOutOfRangeException(nameof(length), "Array length must be between 1 and 64");
            Length = length;
        }
    }

    /// <summary>
    /// Marks a field as a fixed-size 2D array. Memory is laid out contiguously
    /// in row-major order (rows * cols elements) for blittable snapshotting.
    /// Generates accessors: Field(slot, row, col), FieldRow(slot, row) -> Span.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class Array2DAttribute : Attribute
    {
        public int Rows { get; }
        public int Cols { get; }
        public int TotalLength => Rows * Cols;

        public Array2DAttribute(int rows, int cols)
        {
            if (rows <= 0 || rows > 256)
                throw new ArgumentOutOfRangeException(nameof(rows), "Rows must be between 1 and 256");
            if (cols <= 0 || cols > 256)
                throw new ArgumentOutOfRangeException(nameof(cols), "Cols must be between 1 and 256");
            if (rows * cols > 65536)
                throw new ArgumentOutOfRangeException(nameof(rows), "Total elements (rows * cols) must not exceed 65536");
            Rows = rows;
            Cols = cols;
        }
    }

    /// <summary>
    /// Marks a [Flags] enum for ergonomic extension method generation.
    /// Generates Is{Flag}(), Set{Flag}(bool), With{Flag}(), Without{Flag}() methods.
    /// </summary>
    [AttributeUsage(AttributeTargets.Enum)]
    public sealed class GenerateFlagsAttribute : Attribute
    {
    }

    /// <summary>
    /// Marks an interface as a multi-table query. The generator will find all [SimTable]
    /// structs with matching field names and types, and generate unified iteration support.
    /// Interface properties must be ref-returning (e.g., ref int Health { get; }).
    /// Optionally specify explicit types to include (prevents auto-discovery of other matching types).
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface)]
    public sealed class MultiTableQueryAttribute : Attribute
    {
        /// <summary>
        /// Explicit types to include in the query. If empty, auto-discovers all matching types.
        /// </summary>
        public Type[]? IncludedTypes { get; }

        /// <summary>
        /// Auto-discover all [SimTable] types with matching fields.
        /// </summary>
        public MultiTableQueryAttribute()
        {
            IncludedTypes = null;
        }

        /// <summary>
        /// Only include the specified types (no auto-discovery).
        /// </summary>
        public MultiTableQueryAttribute(params Type[] types)
        {
            IncludedTypes = types;
        }
    }

    /// <summary>
    /// Marks a struct as a table union over explicitly listed table types.
    /// Generates unified iteration over the specified tables with type discrimination support.
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class TableUnionAttribute : Attribute
    {
        public Type[] Tables { get; }

        public TableUnionAttribute(params Type[] tables)
        {
            if (tables == null || tables.Length == 0)
                throw new ArgumentException("At least one table type must be specified", nameof(tables));
            Tables = tables;
        }
    }
}

