using FixedMath;

namespace SplineGame.Data;

public sealed class SplineGameData
{
    public SplineGameData(
        SplineTableIds tableIds,
        EntityDefinition[] playerDefinitions,
        EntityDefinition[] enemyDefinitions,
        EntityDefinition[] triggerDefinitions,
        LevelData[] levels)
    {
        TableIds = tableIds;
        PlayerDefinitions = playerDefinitions;
        EnemyDefinitions = enemyDefinitions;
        TriggerDefinitions = triggerDefinitions;
        Levels = levels;
    }

    public SplineTableIds TableIds { get; }

    public EntityDefinition[] PlayerDefinitions { get; }

    public EntityDefinition[] EnemyDefinitions { get; }

    public EntityDefinition[] TriggerDefinitions { get; }

    public LevelData[] Levels { get; }

    public bool TryGetPlayerDefinition(string entityRowId, out EntityDefinition entityDefinition)
    {
        return TryGetEntityDefinition(PlayerDefinitions, entityRowId, out entityDefinition);
    }

    public bool TryGetEnemyDefinition(string entityRowId, out EntityDefinition entityDefinition)
    {
        return TryGetEntityDefinition(EnemyDefinitions, entityRowId, out entityDefinition);
    }

    public bool TryGetTriggerDefinition(string entityRowId, out EntityDefinition entityDefinition)
    {
        return TryGetEntityDefinition(TriggerDefinitions, entityRowId, out entityDefinition);
    }

    private static bool TryGetEntityDefinition(EntityDefinition[] source, string entityRowId, out EntityDefinition entityDefinition)
    {
        for (int index = 0; index < source.Length; index++)
        {
            if (string.Equals(source[index].Id, entityRowId, StringComparison.Ordinal))
            {
                entityDefinition = source[index];
                return true;
            }
        }

        entityDefinition = default;
        return false;
    }

    public readonly struct EntityDefinition
    {
        public EntityDefinition(string id, string name, string uiAsset, Fixed64 scale)
        {
            Id = id;
            Name = name;
            UiAsset = uiAsset;
            Scale = scale;
        }

        public string Id { get; }

        public string Name { get; }

        public string UiAsset { get; }

        public Fixed64 Scale { get; }
    }

    public readonly struct LevelData
    {
        public LevelData(string id, string name, LevelPoint[] points, LevelEntityPlacement[] entities)
        {
            Id = id;
            Name = name;
            Points = points;
            Entities = entities;
        }

        public string Id { get; }

        public string Name { get; }

        public LevelPoint[] Points { get; }

        public LevelEntityPlacement[] Entities { get; }
    }

    public readonly struct LevelPoint
    {
        public LevelPoint(int order, Fixed64Vec2 position, Fixed64Vec2 tangentIn, Fixed64Vec2 tangentOut)
        {
            Order = order;
            Position = position;
            TangentIn = tangentIn;
            TangentOut = tangentOut;
        }

        public int Order { get; }

        public Fixed64Vec2 Position { get; }

        public Fixed64Vec2 TangentIn { get; }

        public Fixed64Vec2 TangentOut { get; }
    }

    public readonly struct LevelEntityPlacement
    {
        public LevelEntityPlacement(
            int order,
            Fixed64 paramT,
            Fixed64Vec2 position,
            string entityTableId,
            string entityRowId,
            string entityDataJson)
        {
            Order = order;
            ParamT = paramT;
            Position = position;
            EntityTableId = entityTableId;
            EntityRowId = entityRowId;
            EntityDataJson = entityDataJson;
        }

        public int Order { get; }

        public Fixed64 ParamT { get; }

        public Fixed64Vec2 Position { get; }

        public string EntityTableId { get; }

        public string EntityRowId { get; }

        public string EntityDataJson { get; }
    }
}
