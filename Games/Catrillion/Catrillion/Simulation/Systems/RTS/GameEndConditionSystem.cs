using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Systems.SimTable;
using SimTable;

namespace Catrillion.Simulation.Systems.RTS;

/// <summary>
/// Detects win/lose conditions and transitions to GameOver state.
/// - Defeat: Command center is destroyed (IsDead flag set)
/// - Victory: All zombies killed after final wave completes
/// </summary>
public sealed class GameEndConditionSystem : SimTableSystem
{
    public GameEndConditionSystem(SimWorld world) : base(world) { }

    public override void Tick(in SimulationContext context)
    {
        // Skip if not playing
        if (!World.IsPlaying()) return;

        // Skip if already game over
        if (World.IsGameOver()) return;

        // Check defeat condition first
        if (CheckDefeatCondition())
        {
            SetGameOver(MatchOutcome.Defeat);
            return;
        }

        // Check victory condition
        if (CheckVictoryCondition())
        {
            SetGameOver(MatchOutcome.Victory);
        }
    }

    /// <summary>
    /// Checks if the command center has been destroyed (IsDead flag).
    /// In cooperative mode, there is only one command center for all players.
    /// </summary>
    private bool CheckDefeatCondition()
    {
        var buildings = World.BuildingRows;

        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var building)) continue;
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;

            // Check if this is the command center
            if (building.TypeId != BuildingTypeId.CommandCenter) continue;

            // Command center found - check if dead
            if (building.Flags.HasFlag(BuildingFlags.IsDead))
            {
                return true;  // Defeat!
            }

            // Command center exists and is alive - no defeat yet
            return false;
        }

        // No command center found at all - this means it was already freed (destroyed)
        // This shouldn't normally happen since we check IsDead before Free,
        // but handle it as defeat just in case
        return true;
    }

    /// <summary>
    /// Checks if victory conditions are met.
    /// Victory requires: final wave complete AND all zombies killed.
    /// </summary>
    private bool CheckVictoryCondition()
    {
        // Get wave state
        if (!World.WaveStateRows.TryGetRow(0, out var waveState)) return false;

        var flags = waveState.Flags;

        // Must be the final wave
        if (!flags.HasFlag(WaveStateFlags.IsFinalWave)) return false;

        // Horde spawning must be complete (not actively spawning)
        if (flags.HasFlag(WaveStateFlags.HordeActive)) return false;

        // Mini wave spawning must be complete (not actively spawning)
        if (flags.HasFlag(WaveStateFlags.MiniWaveActive)) return false;

        // All zombies must be dead
        if (World.ZombieRows.Count > 0) return false;

        // Victory!
        return true;
    }

    private void SetGameOver(MatchOutcome outcome)
    {
        if (!World.GameRulesStateRows.TryGetRow(0, out var rules)) return;

        rules.MatchState = MatchState.GameOver;
        rules.MatchOutcome = outcome;
    }
}
