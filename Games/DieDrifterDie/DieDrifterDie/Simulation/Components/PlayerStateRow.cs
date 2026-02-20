using Core;
using SimTable;

namespace DieDrifterDie.Simulation.Components;

/// <summary>
/// Per-player state for player-specific data (camera, selections, etc.).
/// Resources and tech are now global (see GameResourcesRow).
/// Uses SimDataTable for singleton-per-player access pattern.
/// </summary>
[SimDataTable(Capacity = 8)]
public partial struct PlayerStateRow
{
    public byte PlayerSlot;        // 0-7

    // === Player-specific state ===
    public Fixed64Vec2 CameraPosition;
    public PlayerFlags Flags;
}
