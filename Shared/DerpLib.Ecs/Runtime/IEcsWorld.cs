namespace DerpLib.Ecs;

/// <summary>
/// Base interface for all generated ECS worlds.
/// </summary>
public interface IEcsWorld
{
    /// <summary>
    /// Commits all queued structural changes (spawns/destroys) across all tables.
    /// </summary>
    void PlaybackStructuralChanges();
}

