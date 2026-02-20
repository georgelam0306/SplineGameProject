using System;

namespace DerpLib.Ecs.Setup;

/// <summary>
/// Syntax-only fluent builder returned by <see cref="DerpEcsSetupBuilder.Archetype{TKind}"/>.
/// Never instantiated at runtime.
/// </summary>
public readonly struct DerpEcsArchetypeSetupBuilder<TKind> where TKind : unmanaged
{
    public DerpEcsArchetypeSetupBuilder<TKind> Capacity(int capacity)
    {
        throw new InvalidOperationException(DerpEcsSetupConstants.SyntaxOnlyMessage);
    }

    public DerpEcsArchetypeSetupBuilder<TKind> SpawnQueueCapacity(int capacity)
    {
        throw new InvalidOperationException(DerpEcsSetupConstants.SyntaxOnlyMessage);
    }

    public DerpEcsArchetypeSetupBuilder<TKind> DestroyQueueCapacity(int capacity)
    {
        throw new InvalidOperationException(DerpEcsSetupConstants.SyntaxOnlyMessage);
    }

    public DerpEcsArchetypeSetupBuilder<TKind> With<TComponent>() where TComponent : IEcsComponent
    {
        throw new InvalidOperationException(DerpEcsSetupConstants.SyntaxOnlyMessage);
    }

    public DerpEcsArchetypeSetupBuilder<TKind> Spatial(string position, int cellSize, int gridSize, int originX, int originY)
    {
        throw new InvalidOperationException(DerpEcsSetupConstants.SyntaxOnlyMessage);
    }

    public DerpEcsArchetypeSetupBuilder<TKind> QueryRadius(string position, int maxResults)
    {
        throw new InvalidOperationException(DerpEcsSetupConstants.SyntaxOnlyMessage);
    }

    public DerpEcsArchetypeSetupBuilder<TKind> QueryAabb(string position, int maxResults)
    {
        throw new InvalidOperationException(DerpEcsSetupConstants.SyntaxOnlyMessage);
    }
}
