using System;

namespace DerpLib.Ecs;

/// <summary>
/// Runs a deterministic system schedule. For view worlds, this enforces the policy:
/// apply structural changes via command buffer playback after each system.
/// </summary>
public sealed class EcsSystemPipeline<TWorld> where TWorld : IEcsWorld
{
    private readonly IEcsSystem<TWorld>[] _systems;

    public EcsSystemPipeline(IEcsSystem<TWorld>[] systems)
    {
        _systems = systems ?? Array.Empty<IEcsSystem<TWorld>>();
    }

    public ReadOnlySpan<IEcsSystem<TWorld>> Systems => _systems;

    public void RunFrame(TWorld world)
    {
        for (int i = 0; i < _systems.Length; i++)
        {
            _systems[i].Update(world);
            world.PlaybackStructuralChanges();
        }
    }
}
