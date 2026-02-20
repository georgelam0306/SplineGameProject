using System.Numerics;
using DerpDocDatabase;
using DerpLib;
using DerpLib.Rendering;
using DerpLib.Sdf;
using DerpLib.Text;
using FixedMath;
using Silk.NET.Input;

namespace SplineGame;

public sealed class SplineGameApp : IDisposable
{
    private const float TargetDeltaTime = 1f / 60f;
    private const int SplineSamples = 220;
    private const int TriggerCooldownFrames = 20;
    private const float PlayerCollisionRadius = 16f;
    private const float SplineFitPaddingFactor = 0.8f;

    private static readonly Fixed64 FixedDeltaTime = Fixed64.FromFloat(TargetDeltaTime);
    private static readonly Fixed64 PlayerMoveSpeed = Fixed64.FromFloat(0.24f);

    private readonly GameDatabase _database;
    private readonly SplineLevelCompiler _levelCompiler;

    private SplineCompiledLevel[] _levels = Array.Empty<SplineCompiledLevel>();
    private int _currentLevelIndex;
    private Fixed64 _playerParamT;
    private SplineActiveEnemy[] _activeEnemies = Array.Empty<SplineActiveEnemy>();
    private int _activeEnemyCount;
    private int _triggerCooldownFrames;

    private string _statusText;
    private string _levelText;
    private string _entityText;
    private bool _disposed;

    public SplineGameApp(GameDatabase database)
    {
        _database = database;
        _levelCompiler = new SplineLevelCompiler();
        _statusText = "Loading spline levels...";
        _levelText = "Level: --";
        _entityText = "Enemies: 0 Triggers: 0";

        _database.Reloaded += OnDatabaseReloaded;
        OnDatabaseReloaded();
    }

    public void Run()
    {
        Derp.InitWindow(1280, 720, "SplineGame");
        Derp.InitSdf();

        Font font = Derp.LoadFont("arial");
        Derp.SetSdfFontAtlas(font.Atlas);

        try
        {
            while (!Derp.WindowShouldClose())
            {
                Derp.PollEvents();
                _database.Update();

                if (Derp.IsKeyPressed(Key.F5))
                {
                    _database.ReloadNow();
                }

                UpdateSimulation();

                if (!Derp.BeginDrawing())
                {
                    continue;
                }

                DrawFrame(font);
                Derp.EndDrawing();
            }
        }
        finally
        {
            Derp.CloseWindow();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _database.Reloaded -= OnDatabaseReloaded;
        _database.Dispose();
    }

    private void UpdateSimulation()
    {
        if (_levels.Length <= 0)
        {
            return;
        }

        int moveAxis = 0;
        if (Derp.IsKeyDown(Key.A) || Derp.IsKeyDown(Key.Left))
        {
            moveAxis--;
        }

        if (Derp.IsKeyDown(Key.D) || Derp.IsKeyDown(Key.Right))
        {
            moveAxis++;
        }

        if (moveAxis != 0)
        {
            _playerParamT = SplineMath.Wrap01(_playerParamT + (Fixed64.FromInt(moveAxis) * PlayerMoveSpeed * FixedDeltaTime));
        }

        for (int enemyIndex = 0; enemyIndex < _activeEnemyCount; enemyIndex++)
        {
            ref SplineActiveEnemy enemy = ref _activeEnemies[enemyIndex];
            enemy.ParamT = SplineMath.Wrap01(enemy.ParamT + (enemy.Speed * FixedDeltaTime));
        }

        if (_triggerCooldownFrames > 0)
        {
            _triggerCooldownFrames--;
        }

        if (_levels.Length <= 1 || _triggerCooldownFrames > 0)
        {
            return;
        }

        ref readonly SplineCompiledLevel currentLevel = ref _levels[_currentLevelIndex];
        SplineMath.SamplePositionAndTangent(
            currentLevel.Points,
            _playerParamT.ToFloat(),
            out float playerX,
            out float playerY,
            out _,
            out _);

        for (int triggerIndex = 0; triggerIndex < currentLevel.TriggerSpawns.Length; triggerIndex++)
        {
            SplineCompiledLevel.TriggerSpawn trigger = currentLevel.TriggerSpawns[triggerIndex];
            SplineMath.SamplePositionAndTangent(
                currentLevel.Points,
                trigger.ParamT.ToFloat(),
                out float triggerX,
                out float triggerY,
                out _,
                out _);

            float deltaX = playerX - triggerX;
            float deltaY = playerY - triggerY;
            float triggerRadius = trigger.Radius + PlayerCollisionRadius;
            float distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
            if (distanceSquared > (triggerRadius * triggerRadius))
            {
                continue;
            }

            SetCurrentLevelIndex((_currentLevelIndex + 1) % _levels.Length);
            return;
        }
    }

    private void DrawFrame(Font font)
    {
        Derp.SdfBuffer.Reset();

        int framebufferWidth = Derp.GetFramebufferWidth();
        int framebufferHeight = Derp.GetFramebufferHeight();
        float centerX = framebufferWidth * 0.5f;
        float centerY = framebufferHeight * 0.5f;
        float contentScale = Derp.GetContentScale();

        if (_levels.Length > 0)
        {
            ref readonly SplineCompiledLevel currentLevel = ref _levels[_currentLevelIndex];
            float worldScale = ComputeWorldScale(currentLevel, framebufferWidth, framebufferHeight);
            float worldCenterX = (currentLevel.BoundsMinX + currentLevel.BoundsMaxX) * 0.5f;
            float worldCenterY = (currentLevel.BoundsMinY + currentLevel.BoundsMaxY) * 0.5f;
            float worldOffsetX = centerX - (worldCenterX * worldScale);
            float worldOffsetY = centerY - (worldCenterY * worldScale);

            DrawSpline(Derp.SdfBuffer, currentLevel, worldScale, worldOffsetX, worldOffsetY, contentScale);
            DrawTriggers(Derp.SdfBuffer, currentLevel, worldScale, worldOffsetX, worldOffsetY, contentScale);
            DrawEnemies(Derp.SdfBuffer, currentLevel, worldScale, worldOffsetX, worldOffsetY, contentScale);
            DrawPlayer(Derp.SdfBuffer, currentLevel, worldScale, worldOffsetX, worldOffsetY, contentScale);
        }

        Derp.DispatchSdfToTexture();

        int screenWidth = Derp.GetScreenWidth();
        int screenHeight = Derp.GetScreenHeight();

        Derp.BeginCamera2D(new Camera2D(Vector2.Zero, Vector2.Zero, 0f, 1f));

        Matrix4x4 backgroundTransform =
            Matrix4x4.CreateScale(screenWidth, screenHeight, 1f) *
            Matrix4x4.CreateTranslation(screenWidth * 0.5f, screenHeight * 0.5f, 0f);
        Derp.DrawTextureTransform(Texture.White, backgroundTransform, 8, 11, 20, 255);

        Matrix4x4 sdfTransform =
            Matrix4x4.CreateScale(screenWidth, -screenHeight, 1f) *
            Matrix4x4.CreateTranslation(screenWidth * 0.5f, screenHeight * 0.5f, 0f);
        Derp.DrawTextureTransform(Derp.SdfOutputTexture, sdfTransform, 255, 255, 255, 255);

        Derp.EndCamera2D();

        float textScale = Derp.GetContentScale();
        Derp.DrawText(font, _statusText, 12f * textScale, 12f * textScale, 20f * textScale);
        Derp.DrawText(font, _levelText, 12f * textScale, 38f * textScale, 18f * textScale);
        Derp.DrawText(font, _entityText, 12f * textScale, 60f * textScale, 18f * textScale);
        Derp.DrawText(font, "A/D or Left/Right: Move on spline   F5: Reload", 12f * textScale, 84f * textScale, 16f * textScale);
    }

    private void DrawSpline(
        SdfBuffer buffer,
        in SplineCompiledLevel level,
        float worldScale,
        float worldOffsetX,
        float worldOffsetY,
        float contentScale)
    {
        SplineMath.SamplePositionAndTangent(level.Points, 0f, out float previousWorldX, out float previousWorldY, out _, out _);
        float previousX = (previousWorldX * worldScale) + worldOffsetX;
        float previousY = (previousWorldY * worldScale) + worldOffsetY;

        for (int sampleIndex = 1; sampleIndex <= SplineSamples; sampleIndex++)
        {
            float t = sampleIndex / (float)SplineSamples;
            SplineMath.SamplePositionAndTangent(level.Points, t, out float currentWorldX, out float currentWorldY, out _, out _);
            float currentX = (currentWorldX * worldScale) + worldOffsetX;
            float currentY = (currentWorldY * worldScale) + worldOffsetY;

            buffer.Add(SdfCommand.Line(
                    new Vector2(previousX, previousY),
                    new Vector2(currentX, currentY),
                    3.0f * contentScale,
                    new Vector4(0.26f, 0.62f, 1.0f, 0.95f))
                .WithGlow(8f * contentScale));

            previousX = currentX;
            previousY = currentY;
        }
    }

    private void DrawPlayer(
        SdfBuffer buffer,
        in SplineCompiledLevel level,
        float worldScale,
        float worldOffsetX,
        float worldOffsetY,
        float contentScale)
    {
        SplineMath.SamplePositionAndTangent(
            level.Points,
            _playerParamT.ToFloat(),
            out float playerWorldX,
            out float playerWorldY,
            out float playerTangentX,
            out float playerTangentY);

        float playerX = (playerWorldX * worldScale) + worldOffsetX;
        float playerY = (playerWorldY * worldScale) + worldOffsetY;
        float playerRadius = MathF.Max(9f * contentScale, PlayerCollisionRadius * worldScale);

        Vector2 playerPosition = new Vector2(playerX, playerY);
        buffer.Add(SdfCommand.Circle(playerPosition, playerRadius, new Vector4(0.22f, 0.95f, 0.30f, 0.95f))
            .WithStroke(new Vector4(0.45f, 1.0f, 0.55f, 1.0f), 1.8f * contentScale)
            .WithGlow(6f * contentScale));

        float noseLength = playerRadius * 1.8f;
        Vector2 nosePosition = new Vector2(
            playerX + (playerTangentX * noseLength),
            playerY + (playerTangentY * noseLength));
        buffer.Add(SdfCommand.Line(playerPosition, nosePosition, 2.8f * contentScale, new Vector4(0.8f, 1.0f, 0.85f, 0.95f))
            .WithGlow(3f * contentScale));
    }

    private void DrawEnemies(
        SdfBuffer buffer,
        in SplineCompiledLevel level,
        float worldScale,
        float worldOffsetX,
        float worldOffsetY,
        float contentScale)
    {
        for (int enemyIndex = 0; enemyIndex < _activeEnemyCount; enemyIndex++)
        {
            SplineActiveEnemy enemy = _activeEnemies[enemyIndex];
            SplineMath.SamplePositionAndTangent(
                level.Points,
                enemy.ParamT.ToFloat(),
                out float enemyWorldX,
                out float enemyWorldY,
                out _,
                out _);

            float enemyX = (enemyWorldX * worldScale) + worldOffsetX;
            float enemyY = (enemyWorldY * worldScale) + worldOffsetY;
            float enemyRadius = MathF.Max(7f * contentScale, enemy.Radius * worldScale);

            buffer.Add(SdfCommand.Circle(new Vector2(enemyX, enemyY), enemyRadius, new Vector4(enemy.Red, enemy.Green, enemy.Blue, 0.95f))
                .WithStroke(new Vector4(1f, 0.95f, 0.95f, 0.85f), 1.4f * contentScale)
                .WithGlow(5f * contentScale));
        }
    }

    private static void DrawTriggers(
        SdfBuffer buffer,
        in SplineCompiledLevel level,
        float worldScale,
        float worldOffsetX,
        float worldOffsetY,
        float contentScale)
    {
        for (int triggerIndex = 0; triggerIndex < level.TriggerSpawns.Length; triggerIndex++)
        {
            SplineCompiledLevel.TriggerSpawn trigger = level.TriggerSpawns[triggerIndex];
            SplineMath.SamplePositionAndTangent(
                level.Points,
                trigger.ParamT.ToFloat(),
                out float triggerWorldX,
                out float triggerWorldY,
                out _,
                out _);

            float triggerX = (triggerWorldX * worldScale) + worldOffsetX;
            float triggerY = (triggerWorldY * worldScale) + worldOffsetY;
            float triggerRadius = MathF.Max(6f * contentScale, trigger.Radius * worldScale);

            buffer.Add(SdfCommand.Circle(new Vector2(triggerX, triggerY), triggerRadius, new Vector4(1.0f, 0.76f, 0.28f, 0.45f))
                .WithStroke(new Vector4(1.0f, 0.88f, 0.44f, 1.0f), 2.0f * contentScale)
                .WithGlow(7f * contentScale));
        }
    }

    private void OnDatabaseReloaded()
    {
        string previousLevelRowId = "";
        if (_levels.Length > 0 && (uint)_currentLevelIndex < (uint)_levels.Length)
        {
            previousLevelRowId = _levels[_currentLevelIndex].LevelRowId;
        }

        if (!_levelCompiler.TryBuild(_database, out SplineCompiledLevel[] compiledLevels, out string statusText))
        {
            _levels = Array.Empty<SplineCompiledLevel>();
            _currentLevelIndex = 0;
            _activeEnemyCount = 0;
            _statusText = statusText;
            _levelText = "Level: --";
            _entityText = "Enemies: 0 Triggers: 0";
            return;
        }

        _levels = compiledLevels;
        _statusText = statusText;

        int targetLevelIndex = 0;
        if (!string.IsNullOrWhiteSpace(previousLevelRowId))
        {
            for (int levelIndex = 0; levelIndex < _levels.Length; levelIndex++)
            {
                if (!string.Equals(_levels[levelIndex].LevelRowId, previousLevelRowId, StringComparison.Ordinal))
                {
                    continue;
                }

                targetLevelIndex = levelIndex;
                break;
            }
        }

        SetCurrentLevelIndex(targetLevelIndex);
    }

    private void SetCurrentLevelIndex(int levelIndex)
    {
        if (_levels.Length <= 0)
        {
            _currentLevelIndex = 0;
            _activeEnemyCount = 0;
            return;
        }

        if ((uint)levelIndex >= (uint)_levels.Length)
        {
            levelIndex = 0;
        }

        _currentLevelIndex = levelIndex;
        ref readonly SplineCompiledLevel level = ref _levels[_currentLevelIndex];
        _playerParamT = level.PlayerStartParamT;

        EnsureEnemyBufferCapacity(level.EnemySpawns.Length);
        _activeEnemyCount = level.EnemySpawns.Length;
        for (int enemyIndex = 0; enemyIndex < level.EnemySpawns.Length; enemyIndex++)
        {
            SplineCompiledLevel.EnemySpawn spawn = level.EnemySpawns[enemyIndex];
            _activeEnemies[enemyIndex] = new SplineActiveEnemy
            {
                ParamT = spawn.ParamT,
                Speed = spawn.Speed,
                Radius = spawn.Radius,
                Red = spawn.Red,
                Green = spawn.Green,
                Blue = spawn.Blue,
            };
        }

        _triggerCooldownFrames = TriggerCooldownFrames;
        _levelText = "Level: " + level.LevelName + " (" + (_currentLevelIndex + 1) + "/" + _levels.Length + ")";
        _entityText = "Enemies: " + _activeEnemyCount + " Triggers: " + level.TriggerSpawns.Length;
    }

    private void EnsureEnemyBufferCapacity(int requiredCapacity)
    {
        if (_activeEnemies.Length >= requiredCapacity)
        {
            return;
        }

        _activeEnemies = new SplineActiveEnemy[requiredCapacity];
    }

    private static float ComputeWorldScale(SplineCompiledLevel level, int framebufferWidth, int framebufferHeight)
    {
        float levelWidth = MathF.Max(1f, level.BoundsMaxX - level.BoundsMinX);
        float levelHeight = MathF.Max(1f, level.BoundsMaxY - level.BoundsMinY);
        float scaleX = (framebufferWidth * SplineFitPaddingFactor) / levelWidth;
        float scaleY = (framebufferHeight * SplineFitPaddingFactor) / levelHeight;
        float scale = MathF.Min(scaleX, scaleY);

        if (!float.IsFinite(scale) || scale <= 0f)
        {
            return 1f;
        }

        return scale;
    }
}
