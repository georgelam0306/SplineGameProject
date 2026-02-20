#nullable enable
using System.Runtime.CompilerServices;

namespace SimTable
{
    /// <summary>
    /// Type-safe handle to a row in a SimTable with generational validation.
    /// StableId packs generation (upper 16 bits) and rawId (lower 16 bits).
    /// Stale handles are detected when generation doesn't match current table generation.
    /// </summary>
    public readonly struct SimHandle
    {
        public readonly int TableId;
        public readonly int StableId;  // Packed: (generation << 16) | rawId

        /// <summary>Generation counter at time of handle creation (upper 16 bits).</summary>
        public int Generation
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (StableId >> 16) & 0xFFFF;
        }

        /// <summary>Raw ID without generation (lower 16 bits).</summary>
        public int RawId
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => StableId & 0xFFFF;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SimHandle(int tableId, int rawId, int generation)
        {
            TableId = tableId;
            StableId = ((generation & 0xFFFF) << 16) | (rawId & 0xFFFF);
        }

        /// <summary>
        /// Reconstruct a handle from a packed stableId (for stored references).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SimHandle(int tableId, int packedStableId)
        {
            TableId = tableId;
            StableId = packedStableId;
        }

        public bool IsValid => TableId >= 0;

        public static SimHandle Invalid => new(-1, 0, 0);
    }
}
