using FixedMath;

namespace SplineGame;

public sealed class SplineCompiledLevel
{
    public SplineCompiledLevel(
        string levelRowId,
        string levelName,
        Point[] points,
        EnemySpawn[] enemySpawns,
        TriggerSpawn[] triggerSpawns,
        Fixed64 playerStartParamT)
    {
        LevelRowId = levelRowId;
        LevelName = levelName;
        Points = points;
        EnemySpawns = enemySpawns;
        TriggerSpawns = triggerSpawns;
        PlayerStartParamT = playerStartParamT;

        if (points.Length <= 0)
        {
            BoundsMinX = 0f;
            BoundsMaxX = 0f;
            BoundsMinY = 0f;
            BoundsMaxY = 0f;
            return;
        }

        float minX = points[0].X;
        float maxX = points[0].X;
        float minY = points[0].Y;
        float maxY = points[0].Y;

        for (int pointIndex = 1; pointIndex < points.Length; pointIndex++)
        {
            Point point = points[pointIndex];
            if (point.X < minX)
            {
                minX = point.X;
            }

            if (point.X > maxX)
            {
                maxX = point.X;
            }

            if (point.Y < minY)
            {
                minY = point.Y;
            }

            if (point.Y > maxY)
            {
                maxY = point.Y;
            }
        }

        BoundsMinX = minX;
        BoundsMaxX = maxX;
        BoundsMinY = minY;
        BoundsMaxY = maxY;
    }

    public string LevelRowId { get; }
    public string LevelName { get; }
    public Point[] Points { get; }
    public EnemySpawn[] EnemySpawns { get; }
    public TriggerSpawn[] TriggerSpawns { get; }
    public Fixed64 PlayerStartParamT { get; }
    public float BoundsMinX { get; }
    public float BoundsMaxX { get; }
    public float BoundsMinY { get; }
    public float BoundsMaxY { get; }

    public readonly struct Point
    {
        public Point(
            float x,
            float y,
            float tangentInX,
            float tangentInY,
            float tangentOutX,
            float tangentOutY)
        {
            X = x;
            Y = y;
            TangentInX = tangentInX;
            TangentInY = tangentInY;
            TangentOutX = tangentOutX;
            TangentOutY = tangentOutY;
        }

        public float X { get; }
        public float Y { get; }
        public float TangentInX { get; }
        public float TangentInY { get; }
        public float TangentOutX { get; }
        public float TangentOutY { get; }
    }

    public readonly struct EnemySpawn
    {
        public EnemySpawn(Fixed64 paramT, Fixed64 speed, float radius, float red, float green, float blue)
        {
            ParamT = paramT;
            Speed = speed;
            Radius = radius;
            Red = red;
            Green = green;
            Blue = blue;
        }

        public Fixed64 ParamT { get; }
        public Fixed64 Speed { get; }
        public float Radius { get; }
        public float Red { get; }
        public float Green { get; }
        public float Blue { get; }
    }

    public readonly struct TriggerSpawn
    {
        public TriggerSpawn(Fixed64 paramT, float radius)
        {
            ParamT = paramT;
            Radius = radius;
        }

        public Fixed64 ParamT { get; }
        public float Radius { get; }
    }
}
