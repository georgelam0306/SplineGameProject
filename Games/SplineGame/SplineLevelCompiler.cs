using System.Globalization;
using System.Text.Json;
using DerpDocDatabase;
using FixedMath;

namespace SplineGame;

public sealed class SplineLevelCompiler
{
    private static readonly Fixed64 DefaultEnemySpeed = Fixed64.FromFloat(0.075f);

    public bool TryBuild(GameDatabase database, out SplineCompiledLevel[] levels, out string statusText)
    {
        levels = Array.Empty<SplineCompiledLevel>();
        statusText = "";

        int levelCount = database.GameLevels.Count;
        if (levelCount <= 0)
        {
            statusText = "GameLevels has no rows.";
            return false;
        }

        int[] firstSplineRowIdByLevelId = BuildFirstSplineRowIdByLevelId(database, levelCount);
        var compiledLevels = new List<SplineCompiledLevel>(levelCount);

        for (int levelId = 0; levelId < levelCount; levelId++)
        {
            int splineRowId = firstSplineRowIdByLevelId[levelId];
            if (splineRowId < 0)
            {
                continue;
            }

            ref readonly GameLevels gameLevelRow = ref database.GameLevels.FindById(levelId);
            if (!TryCompileLevel(
                    database,
                    levelId,
                    splineRowId,
                    gameLevelRow,
                    out SplineCompiledLevel compiledLevel,
                    out string levelError))
            {
                statusText = levelError;
                return false;
            }

            compiledLevels.Add(compiledLevel);
        }

        if (compiledLevels.Count <= 0)
        {
            statusText = "No GameLevels rows had any SplineGameLevel subtable rows.";
            return false;
        }

        levels = compiledLevels.ToArray();
        statusText = "Loaded " + levels.Length.ToString(CultureInfo.InvariantCulture) + " spline levels.";
        return true;
    }

    private static int[] BuildFirstSplineRowIdByLevelId(GameDatabase database, int levelCount)
    {
        var firstSplineRowIdByLevelId = new int[levelCount];
        Array.Fill(firstSplineRowIdByLevelId, -1);

        int splineRowCount = database.GameLevelsSplineGameLevel.Count;
        for (int splineRowId = 0; splineRowId < splineRowCount; splineRowId++)
        {
            ref readonly GameLevelsSplineGameLevel splineRow = ref database.GameLevelsSplineGameLevel.FindById(splineRowId);
            int levelId = splineRow.ParentRowId;
            if ((uint)levelId >= (uint)levelCount)
            {
                continue;
            }

            if (firstSplineRowIdByLevelId[levelId] < 0)
            {
                firstSplineRowIdByLevelId[levelId] = splineRowId;
            }
        }

        return firstSplineRowIdByLevelId;
    }

    private static bool TryCompileLevel(
        GameDatabase database,
        int levelId,
        int splineRowId,
        in GameLevels gameLevelRow,
        out SplineCompiledLevel compiledLevel,
        out string errorText)
    {
        compiledLevel = null!;
        errorText = "";

        if (!TryBuildPoints(database, splineRowId, out SplineCompiledLevel.Point[] points, out errorText))
        {
            return false;
        }

        BuildEntities(
            database,
            splineRowId,
            out SplineCompiledLevel.EnemySpawn[] enemySpawns,
            out SplineCompiledLevel.TriggerSpawn[] triggerSpawns,
            out Fixed64 playerStartParamT,
            out bool hasPlayerStart);

        string levelRowId = gameLevelRow.Id;
        string levelName = gameLevelRow.LevelName;
        if (string.IsNullOrWhiteSpace(levelName))
        {
            levelName = "Level " + (levelId + 1).ToString(CultureInfo.InvariantCulture);
        }

        Fixed64 spawnT = hasPlayerStart ? playerStartParamT : Fixed64.Zero;
        compiledLevel = new SplineCompiledLevel(
            levelRowId,
            levelName,
            points,
            enemySpawns,
            triggerSpawns,
            spawnT);
        return true;
    }

    private static bool TryBuildPoints(
        GameDatabase database,
        int splineRowId,
        out SplineCompiledLevel.Point[] points,
        out string errorText)
    {
        points = Array.Empty<SplineCompiledLevel.Point>();
        errorText = "";

        var orderedPoints = new List<OrderedPointRow>(32);
        GameLevelsSplineGameLevelPointsTable.ParentScopedRange pointsRange =
            database.GameLevelsSplineGameLevel.FindByIdView(splineRowId).Points.All;
        var pointsEnumerator = pointsRange.GetEnumerator();
        while (pointsEnumerator.MoveNext())
        {
            ref readonly GameLevelsSplineGameLevelPoints pointRow = ref pointsEnumerator.Current;
            orderedPoints.Add(new OrderedPointRow(pointRow.Order, pointRow));
        }

        if (orderedPoints.Count < 2)
        {
            errorText = "SplineGameLevel points must contain at least 2 rows.";
            return false;
        }

        orderedPoints.Sort(static (left, right) => left.Order.CompareTo(right.Order));
        points = new SplineCompiledLevel.Point[orderedPoints.Count];
        for (int pointIndex = 0; pointIndex < orderedPoints.Count; pointIndex++)
        {
            GameLevelsSplineGameLevelPoints pointRow = orderedPoints[pointIndex].Row;
            points[pointIndex] = new SplineCompiledLevel.Point(
                pointRow.Position.X.ToFloat(),
                pointRow.Position.Y.ToFloat(),
                pointRow.TangentIn.X.ToFloat(),
                pointRow.TangentIn.Y.ToFloat(),
                pointRow.TangentOut.X.ToFloat(),
                pointRow.TangentOut.Y.ToFloat());
        }

        return true;
    }

    private static void BuildEntities(
        GameDatabase database,
        int splineRowId,
        out SplineCompiledLevel.EnemySpawn[] enemySpawns,
        out SplineCompiledLevel.TriggerSpawn[] triggerSpawns,
        out Fixed64 playerStartParamT,
        out bool hasPlayerStart)
    {
        GameLevelsSplineGameLevelEntitiesTable.ParentScope entitiesScope =
            database.GameLevelsSplineGameLevel.FindByIdView(splineRowId).Entities;

        var orderedEnemyPlacements = new List<OrderedEnemyPlacement>(32);
        var orderedTriggerPlacements = new List<OrderedTriggerPlacement>(16);
        hasPlayerStart = TryResolvePlayerStart(entitiesScope, out playerStartParamT);
        CollectEnemyPlacements(database, entitiesScope, orderedEnemyPlacements);
        CollectTriggerPlacements(database, entitiesScope, orderedTriggerPlacements);

        orderedEnemyPlacements.Sort(static (left, right) => left.Order.CompareTo(right.Order));
        orderedTriggerPlacements.Sort(static (left, right) => left.Order.CompareTo(right.Order));

        var enemySpawnList = new List<SplineCompiledLevel.EnemySpawn>(orderedEnemyPlacements.Count);
        int enemyColorIndex = 0;
        for (int enemyPlacementIndex = 0; enemyPlacementIndex < orderedEnemyPlacements.Count; enemyPlacementIndex++)
        {
            OrderedEnemyPlacement placement = orderedEnemyPlacements[enemyPlacementIndex];
            GetEnemyColor(enemyColorIndex, out float red, out float green, out float blue);
            enemySpawnList.Add(new SplineCompiledLevel.EnemySpawn(
                placement.ParamT,
                placement.Speed,
                placement.Radius,
                red,
                green,
                blue));
            enemyColorIndex++;
        }

        var triggerSpawnList = new List<SplineCompiledLevel.TriggerSpawn>(orderedTriggerPlacements.Count);
        for (int triggerPlacementIndex = 0; triggerPlacementIndex < orderedTriggerPlacements.Count; triggerPlacementIndex++)
        {
            OrderedTriggerPlacement placement = orderedTriggerPlacements[triggerPlacementIndex];
            triggerSpawnList.Add(new SplineCompiledLevel.TriggerSpawn(placement.ParamT, placement.Radius));
        }

        enemySpawns = enemySpawnList.ToArray();
        triggerSpawns = triggerSpawnList.ToArray();
    }

    private static bool TryResolvePlayerStart(
        GameLevelsSplineGameLevelEntitiesTable.ParentScope entitiesScope,
        out Fixed64 playerStartParamT)
    {
        playerStartParamT = Fixed64.Zero;
        int bestPlayerOrder = int.MaxValue;

        GameLevelsSplineGameLevelEntitiesTable.ParentScopedRange playerPlacements = entitiesScope.Player.All;
        var playerEnumerator = playerPlacements.GetEnumerator();
        while (playerEnumerator.MoveNext())
        {
            ref readonly GameLevelsSplineGameLevelEntities placementRow = ref playerEnumerator.Current;
            if (placementRow.Order < bestPlayerOrder)
            {
                bestPlayerOrder = placementRow.Order;
                playerStartParamT = SplineMath.Wrap01(placementRow.ParamT);
            }
        }

        return bestPlayerOrder != int.MaxValue;
    }

    private static void CollectEnemyPlacements(
        GameDatabase database,
        GameLevelsSplineGameLevelEntitiesTable.ParentScope entitiesScope,
        List<OrderedEnemyPlacement> orderedEnemyPlacements)
    {
        int enemyCount = database.Enemies.Count;
        for (int enemyId = 0; enemyId < enemyCount; enemyId++)
        {
            if (!database.Enemies.TryFindById(enemyId, out Enemies enemyRow))
            {
                continue;
            }

            if (!entitiesScope.Enemies.TryFindById(enemyId, out GameLevelsSplineGameLevelEntitiesTable.ParentScopedRange placements))
            {
                continue;
            }

            float radius = ResolveRadius(enemyRow.Scale, 12f);
            var placementEnumerator = placements.GetEnumerator();
            while (placementEnumerator.MoveNext())
            {
                ref readonly GameLevelsSplineGameLevelEntities placementRow = ref placementEnumerator.Current;
                orderedEnemyPlacements.Add(new OrderedEnemyPlacement(
                    placementRow.Order,
                    SplineMath.Wrap01(placementRow.ParamT),
                    ParseEnemySpeed(placementRow.DataJson),
                    radius));
            }
        }
    }

    private static void CollectTriggerPlacements(
        GameDatabase database,
        GameLevelsSplineGameLevelEntitiesTable.ParentScope entitiesScope,
        List<OrderedTriggerPlacement> orderedTriggerPlacements)
    {
        int triggerCount = database.Triggers.Count;
        for (int triggerId = 0; triggerId < triggerCount; triggerId++)
        {
            if (!database.Triggers.TryFindById(triggerId, out Triggers triggerRow))
            {
                continue;
            }

            if (!entitiesScope.Triggers.TryFindById(triggerId, out GameLevelsSplineGameLevelEntitiesTable.ParentScopedRange placements))
            {
                continue;
            }

            float radius = ResolveRadius(triggerRow.Scale, 16f);
            var placementEnumerator = placements.GetEnumerator();
            while (placementEnumerator.MoveNext())
            {
                ref readonly GameLevelsSplineGameLevelEntities placementRow = ref placementEnumerator.Current;
                orderedTriggerPlacements.Add(new OrderedTriggerPlacement(
                    placementRow.Order,
                    SplineMath.Wrap01(placementRow.ParamT),
                    radius));
            }
        }
    }

    private static Fixed64 ParseEnemySpeed(string dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return DefaultEnemySpeed;
        }

        try
        {
            using JsonDocument dataJsonDocument = JsonDocument.Parse(dataJson);
            JsonElement root = dataJsonDocument.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return DefaultEnemySpeed;
            }

            if (!root.TryGetProperty("speed", out JsonElement speedElement))
            {
                return DefaultEnemySpeed;
            }

            if (speedElement.ValueKind != JsonValueKind.Number || !speedElement.TryGetDouble(out double speedValue))
            {
                return DefaultEnemySpeed;
            }

            float clampedSpeed = Math.Clamp((float)speedValue, -1f, 1f);
            return Fixed64.FromFloat(clampedSpeed);
        }
        catch (JsonException)
        {
            return DefaultEnemySpeed;
        }
    }

    private static float ResolveRadius(Fixed64 scaleValue, float minimumRadius)
    {
        float scale = MathF.Abs(scaleValue.ToFloat());
        float radius = scale * 128f;
        if (radius < minimumRadius)
        {
            radius = minimumRadius;
        }

        return radius;
    }

    private static void GetEnemyColor(int enemyIndex, out float red, out float green, out float blue)
    {
        int paletteIndex = enemyIndex % 4;
        if (paletteIndex == 0)
        {
            red = 1.0f;
            green = 0.35f;
            blue = 0.35f;
            return;
        }

        if (paletteIndex == 1)
        {
            red = 0.95f;
            green = 0.6f;
            blue = 0.22f;
            return;
        }

        if (paletteIndex == 2)
        {
            red = 0.95f;
            green = 0.85f;
            blue = 0.25f;
            return;
        }

        red = 0.9f;
        green = 0.45f;
        blue = 0.9f;
    }

    private readonly struct OrderedPointRow
    {
        public OrderedPointRow(int order, GameLevelsSplineGameLevelPoints row)
        {
            Order = order;
            Row = row;
        }

        public int Order { get; }
        public GameLevelsSplineGameLevelPoints Row { get; }
    }

    private readonly struct OrderedEnemyPlacement
    {
        public OrderedEnemyPlacement(int order, Fixed64 paramT, Fixed64 speed, float radius)
        {
            Order = order;
            ParamT = paramT;
            Speed = speed;
            Radius = radius;
        }

        public int Order { get; }
        public Fixed64 ParamT { get; }
        public Fixed64 Speed { get; }
        public float Radius { get; }
    }

    private readonly struct OrderedTriggerPlacement
    {
        public OrderedTriggerPlacement(int order, Fixed64 paramT, float radius)
        {
            Order = order;
            ParamT = paramT;
            Radius = radius;
        }

        public int Order { get; }
        public Fixed64 ParamT { get; }
        public float Radius { get; }
    }
}
