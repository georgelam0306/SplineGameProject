using Catrillion.Simulation.Components;

namespace Catrillion.Simulation.Systems.SimTable;

/// <summary>
/// Spawns PlayerStateRows for each player when the game starts.
/// Must run before systems that depend on PlayerState (e.g., SelectionSystem).
/// </summary>
public sealed class PlayerStateSpawnSystem : SimTableSystem
{
    private bool _hasSpawned;

    public PlayerStateSpawnSystem(SimWorld world) : base(world)
    {
    }

    public override void Tick(in SimulationContext context)
    {
        if (!World.IsPlaying()) return;
        if (_hasSpawned) return;
        _hasSpawned = true;

        var playerStates = World.PlayerStateRows;

        // Allocate player states sequentially (slot 0 = player 0, slot 1 = player 1, etc.)
        for (int playerId = 0; playerId < context.PlayerCount; playerId++)
        {
            if (!playerStates.TryGetRow(playerId, out _))
            {
                playerStates.Allocate();  // Allocates next sequential slot
                var row = playerStates.GetRowBySlot(playerId);
                row.PlayerSlot = (byte)playerId;
            }
        }
    }
}
