using Catrillion.Simulation.Components;

namespace Catrillion.Simulation.Systems.SimTable;

/// <summary>
/// Centralized system that handles death for all entities with Health.
/// Uses the IMortal multi-table query with ByTable() for efficient iteration.
///
/// Death is handled in two phases:
/// 1. Mark dead: When Health &lt;= 0, set IsDead flag and DeathFrame
/// 2. Cleanup: After DeathDelayFrames, actually Free() the entity
///
/// This delay allows rendering systems to show death effects.
/// </summary>
public sealed class MortalDeathSystem : SimTableSystem
{
    /// <summary>
    /// Number of frames to wait before freeing a dead entity.
    /// This gives rendering time to show death effects.
    /// </summary>
    private const int DeathDelayFrames = 10;

    public MortalDeathSystem(SimWorld world) : base(world)
    {
    }

    public override void Tick(in SimulationContext context)
    {
        int currentFrame = context.CurrentFrame;

        // Phase 1: Mark newly dead entities and count zombie kills
        int zombieKills = MarkDeadEntities(currentFrame);

        // Update match stats with zombie kills
        if (zombieKills > 0)
        {
            UpdateKillStats(zombieKills);
        }

        // Phase 2: Free entities that have been dead long enough
        FreeDeadEntities(currentFrame);
    }

    private void UpdateKillStats(int kills)
    {
        var stats = World.MatchStatsRows;
        if (stats.TryGetRow(0, out var row))
        {
            row.UnitsKilled += kills;
        }
    }

    /// <summary>
    /// Phase 1: Find entities with Health &lt;= 0 and mark them as dead.
    /// Uses ByTable() for efficient span-based iteration.
    /// Returns the count of zombies killed this frame for stats tracking.
    /// </summary>
    private int MarkDeadEntities(int currentFrame)
    {
        int zombieKillCount = 0;

        foreach (var chunk in World.Query<IMortal>().ByTable())
        {
            var healths = chunk.Healths;
            var deathFrames = chunk.DeathFrames;
            var flags = chunk.Flagss;
            int count = chunk.Count;

            for (int i = 0; i < count; i++)
            {
                // Skip if alive or already marked dead
                if (healths[i] > 0) continue;
                if (deathFrames[i] > 0) continue;

                // Mark as dead
                deathFrames[i] = currentFrame;
                flags[i] = flags[i].WithDead();
            }
        }

        // Count zombie deaths separately (zombies are always enemies)
        var zombies = World.ZombieRows;
        for (int i = 0; i < zombies.Count; i++)
        {
            if (!zombies.TryGetRow(i, out var zombie)) continue;

            // Check if just marked dead this frame
            if (zombie.DeathFrame == currentFrame)
            {
                zombieKillCount++;
            }
        }

        return zombieKillCount;
    }

    /// <summary>
    /// Phase 2: Free entities that have been dead long enough.
    /// Uses ByTable() for efficient span-based iteration.
    /// Iterates backwards to safely handle swap-and-pop during Free().
    /// </summary>
    private void FreeDeadEntities(int currentFrame)
    {
        foreach (var chunk in World.Query<IMortal>().ByTable())
        {
            var deathFrames = chunk.DeathFrames;
            int count = chunk.Count;

            // Iterate backwards for swap-and-pop safety
            for (int i = count - 1; i >= 0; i--)
            {
                int deathFrame = deathFrames[i];
                if (deathFrame <= 0) continue;

                if (currentFrame - deathFrame >= DeathDelayFrames)
                {
                    chunk.FreeBySlot(i);
                }
            }
        }
    }
}
