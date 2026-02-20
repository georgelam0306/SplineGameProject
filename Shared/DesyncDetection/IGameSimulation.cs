namespace DerpTech.DesyncDetection;

/// <summary>
/// Interface for game simulation supporting per-system hash re-simulation.
/// Used by desync detection to identify which system caused divergence.
/// </summary>
public interface IGameSimulation
{
    /// <summary>Current simulation frame number.</summary>
    int CurrentFrame { get; }

    /// <summary>Session seed for deterministic random number generation.</summary>
    int SessionSeed { get; }

    /// <summary>Names of all simulation systems (for per-system hash labeling).</summary>
    ReadOnlySpan<string> SystemNames { get; }

    /// <summary>
    /// Re-simulates a frame with per-system hash tracking for desync debugging.
    /// Call after restoring a snapshot to the frame before the desync.
    /// </summary>
    void SimulateTickWithHashing(int frame);

    /// <summary>
    /// Retrieves stored per-system hashes for a specific frame.
    /// </summary>
    bool TryGetPerSystemHashesForFrame(int frame, out ulong[]? hashes);

    /// <summary>
    /// Stores per-system hashes computed during re-simulation.
    /// </summary>
    void StorePerSystemHashes(int frame, ulong[] hashes);
}
