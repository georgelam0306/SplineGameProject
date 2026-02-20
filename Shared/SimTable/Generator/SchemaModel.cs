#nullable enable
using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;

namespace SimTable.Generator
{
    /// <summary>
    /// Cache eviction policy for SimTables when full.
    /// </summary>
    internal enum EvictionPolicy
    {
        None = 0,
        LRU = 1
    }

    internal sealed class SchemaModel
    {
        public INamedTypeSymbol Type { get; }
        public int Capacity { get; }
        public int CellSize { get; }
        public int GridSize { get; }
        /// <summary>
        /// World units per chunk dimension. When > 0, enables chunked spatial mode.
        /// Set to 0 for single-grid mode (default).
        /// </summary>
        public int ChunkSize { get; }
        public List<ColumnInfo> Columns { get; }

        /// <summary>
        /// True when marked with [SimDataTable] - no spatial partitioning generated.
        /// </summary>
        public bool IsDataOnly { get; }

        /// <summary>
        /// When true, SimWorld constructor auto-allocates one row and Reset() re-allocates after clearing.
        /// Only applies to data-only tables.
        /// </summary>
        public bool AutoAllocate { get; }

        /// <summary>
        /// Cache eviction policy when table is full. Default is None (throws exception).
        /// </summary>
        public EvictionPolicy EvictionPolicy { get; }

        /// <summary>
        /// Field name to use as LRU key (must be int type). Required when EvictionPolicy is LRU.
        /// </summary>
        public string LRUKeyField { get; }

        /// <summary>
        /// True when ChunkSize > 0 and not a data-only table, enabling infinite world support via chunked grids.
        /// </summary>
        public bool IsChunked => ChunkSize > 0 && !IsDataOnly;

        /// <summary>
        /// True if this table has a Position field of type Fixed64Vec2 or Fixed32Vec2.
        /// Tables with position fields need Transform2D sync in EndFrame.
        /// </summary>
        public bool HasPositionField => Columns.Any(c =>
            c.Name == "Position" &&
            (c.Type.ToDisplayString().Contains("Fixed64Vec2") || c.Type.ToDisplayString().Contains("Fixed32Vec2")));

        /// <summary>
        /// True if this table has a Velocity field of type Fixed64Vec2 or Fixed32Vec2.
        /// Tables with velocity fields sync velocity to Transform2D for render extrapolation.
        /// </summary>
        public bool HasVelocityField => Columns.Any(c =>
            c.Name == "Velocity" &&
            (c.Type.ToDisplayString().Contains("Fixed64Vec2") || c.Type.ToDisplayString().Contains("Fixed32Vec2")));

        public SchemaModel(INamedTypeSymbol type, int capacity, int cellSize, int gridSize, int chunkSize = 0, bool isDataOnly = false, bool autoAllocate = false, EvictionPolicy evictionPolicy = EvictionPolicy.None, string lruKeyField = "")
        {
            Type = type;
            Capacity = capacity;
            CellSize = cellSize;
            GridSize = gridSize;
            ChunkSize = chunkSize;
            IsDataOnly = isDataOnly;
            AutoAllocate = autoAllocate;
            EvictionPolicy = evictionPolicy;
            LRUKeyField = lruKeyField;
            Columns = new List<ColumnInfo>();
        }
    }

    internal sealed class ColumnInfo
    {
        public string Name { get; }
        public ITypeSymbol Type { get; }
        public bool NoSerialize { get; }
        /// <summary>
        /// If true, this field is computed state - stored in slab but not serialized.
        /// Computed fields are placed after authoritative fields in the contiguous slab.
        /// Auto-set if field is a target in Setup's Compute() call.
        /// </summary>
        public bool IsComputed { get; set; }
        /// <summary>
        /// If > 0, this field is a fixed-size 1D array with the specified length.
        /// Memory layout is contiguous per-slot (AoS within this field).
        /// </summary>
        public int ArrayLength { get; }
        /// <summary>
        /// If > 0, this field is a fixed-size 2D array with (Array2DRows * Array2DCols) elements.
        /// Memory layout is contiguous row-major per-slot for blittable snapshotting.
        /// </summary>
        public int Array2DRows { get; }
        public int Array2DCols { get; }
        /// <summary>
        /// The source text of the field initializer (e.g., "180", "MatchState.Countdown").
        /// Null if no initializer present. Used for auto-allocation with defaults.
        /// </summary>
        public string? DefaultValueExpression { get; }

        /// <summary>
        /// The generated expression for computing this field (e.g., "BaseSpeed(slot) * SpeedModifier(slot)").
        /// Null if not a computed field or no Setup expression provided.
        /// Set by the generator after parsing the Setup method.
        /// </summary>
        public string? ComputedExpression { get; set; }

        public ColumnInfo(string name, ITypeSymbol type, bool noSerialize, bool isComputed = false, int arrayLength = 0, int array2DRows = 0, int array2DCols = 0, string? defaultValueExpression = null)
        {
            Name = name;
            Type = type;
            NoSerialize = noSerialize;
            IsComputed = isComputed;
            ArrayLength = arrayLength;
            Array2DRows = array2DRows;
            Array2DCols = array2DCols;
            DefaultValueExpression = defaultValueExpression;
            ComputedExpression = null;
        }

        public bool IsArray => ArrayLength > 0;
        public bool IsArray2D => Array2DRows > 0 && Array2DCols > 0;
        public int Array2DTotalLength => Array2DRows * Array2DCols;
    }
}

