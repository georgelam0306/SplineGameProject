using Friflo.Engine.ECS;
using Catrillion.Simulation.Components;

namespace Catrillion.Entities;

public sealed class EntitySpawner
{
    private readonly SimWorld _simWorld;
    private readonly EntityStore _store;

    public EntitySpawner(SimWorld simWorld, EntityStore store)
    {
        _simWorld = simWorld;
        _store = store;
    }

    public SimWorld SimWorld => _simWorld;
    public EntityStore Store => _store;

    public SimEntityBuilder<TRow> Create<TRow>() where TRow : struct
    {
        return new SimEntityBuilder<TRow>(_simWorld, _store);
    }

    public void Despawn<TRow>(int stableId) where TRow : struct
    {
        _simWorld.Free<TRow>(stableId);
    }
}
