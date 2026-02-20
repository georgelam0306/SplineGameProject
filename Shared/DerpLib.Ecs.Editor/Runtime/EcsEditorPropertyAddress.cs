using DerpLib.Ecs;

namespace DerpLib.Ecs.Editor;

public readonly struct EcsEditorPropertyAddress
{
    public readonly EntityHandle Entity;
    public readonly ulong ComponentSchemaId;
    public readonly ushort PropertyIndex;
    public readonly ulong PropertyId;

    public EcsEditorPropertyAddress(EntityHandle entity, ulong componentSchemaId, ushort propertyIndex, ulong propertyId)
    {
        Entity = entity;
        ComponentSchemaId = componentSchemaId;
        PropertyIndex = propertyIndex;
        PropertyId = propertyId;
    }
}
