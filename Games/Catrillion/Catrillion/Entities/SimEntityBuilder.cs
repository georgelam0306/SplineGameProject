using Friflo.Engine.ECS;
using Catrillion.Rendering.Components;
using Catrillion.Simulation.Components;
using Core;

namespace Catrillion.Entities;

public ref struct SimEntityBuilder<TRow> where TRow : struct
{
    private readonly SimWorld _simWorld;
    private readonly EntityStore _store;
    private Entity _entity;
    private bool _simOnly;

    public SimEntityBuilder(SimWorld simWorld, EntityStore store)
    {
        _simWorld = simWorld;
        _store = store;
        _entity = default;
        _simOnly = false;
    }

    public SimEntityBuilder<TRow> SimOnly()
    {
        _simOnly = true;
        return this;
    }

    public SimEntityBuilder<TRow> WithRender<TComponent>() where TComponent : struct, IComponent
    {
        EnsureEntity();
        _entity.AddComponent<TComponent>();
        return this;
    }

    public SimEntityBuilder<TRow> WithRender<TComponent>(TComponent value) where TComponent : struct, IComponent
    {
        EnsureEntity();
        _entity.AddComponent(value);
        return this;
    }

    private void EnsureEntity()
    {
        if (_entity.Id == 0)
        {
            _entity = _store.CreateEntity();
        }
    }

    public EntityHandle Spawn()
    {
        var handle = _simWorld.Allocate<TRow>();

        if (!_simOnly)
        {
            // Auto-add Transform2D and TransformSim2D for spatial tables with Position field
            if (SimWorld.HasPosition<TRow>())
            {
                EnsureEntity();
                _entity.AddComponent<Transform2D>();
                _entity.AddComponent<TransformSim2D>();
            }

            if (_entity.Id != 0)
            {
                _entity.AddComponent(new SimSlotRef { Handle = handle });
            }
        }

        return new EntityHandle(handle, _entity);
    }
}
