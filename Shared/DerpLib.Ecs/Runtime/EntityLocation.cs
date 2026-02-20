namespace DerpLib.Ecs;

/// <summary>
/// Current storage location for an entity in archetype-table storage.
/// </summary>
public readonly struct EntityLocation
{
    public readonly ushort ArchetypeId;
    public readonly int Row;

    public EntityLocation(ushort archetypeId, int row)
    {
        ArchetypeId = archetypeId;
        Row = row;
    }
}

