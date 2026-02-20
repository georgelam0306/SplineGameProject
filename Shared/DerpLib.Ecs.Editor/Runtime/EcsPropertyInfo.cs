using Property;
using Property.Runtime;

namespace DerpLib.Ecs.Editor;

/// <summary>
/// Thin wrapper around shared property metadata with an ECS-specific id type.
/// </summary>
public readonly struct EcsPropertyInfo
{
    public readonly EcsPropertyId Id;
    public readonly PropertyInfo Info;

    public EcsPropertyInfo(EcsPropertyId id, PropertyInfo info)
    {
        Id = id;
        Info = info;
    }

    public PropertyKind Kind => Info.Kind;
}

