using Friflo.Engine.ECS;
using SimTable;

namespace Catrillion.Entities;

public readonly struct EntityHandle
{
    public readonly SimHandle SimHandle;
    public readonly Entity Entity;

    public EntityHandle(SimHandle handle, Entity entity)
    {
        SimHandle = handle;
        Entity = entity;
    }

    public int StableId => SimHandle.StableId;
    public bool HasSim => SimHandle.IsValid;
    public bool HasRender => Entity.Id != 0;
}
