using System.Diagnostics;
using System.Numerics;
using Raylib_cs;
using BaseTemplate.GameApp.AppState;
using BaseTemplate.Infrastructure.Networking;
using BaseTemplate.GameApp.Stores;

namespace BaseTemplate.GameApp.Core;

public class Main : IGameRunner
{
    private readonly ScreenManager _screenManager;
    private readonly LoadingManager _loadingManager;
    private readonly MatchmakingPoller _matchmakingPoller;
    private readonly InputStore _inputStore;
    private readonly RootStore _store;

    // Frame timing telemetry (F8 to toggle)
    private bool _showFrameTiming;
    private double _inputMs, _loadingMs, _matchmakingMs, _screenUpdateMs;
    private double _beginDrawMs, _clearMs, _screenDrawMs, _endDrawMs;
    private double _gameUpdateMs, _gameDrawMs;
    private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

    public Main(
        ScreenManager screenManager,
        LoadingManager loadingManager,
        MatchmakingPoller matchmakingPoller,
        InputStore inputStore,
        RootStore store)
    {
        _screenManager = screenManager;
        _loadingManager = loadingManager;
        _matchmakingPoller = matchmakingPoller;
        _inputStore = inputStore;
        _store = store;
    }

    public void Run()
    {
        while (!Raylib.WindowShouldClose())
        {
            float deltaTime = Raylib.GetFrameTime();

            // F8: Toggle frame timing overlay
            if (Raylib.IsKeyPressed(KeyboardKey.F8))
            {
                _showFrameTiming = !_showFrameTiming;
            }

            if (Raylib.IsWindowResized())
            {
                _screenManager.OnWindowResized(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
            }

            long t0, t1;

            // Update input state first (before any input polling)
            t0 = Stopwatch.GetTimestamp();
            _inputStore.Update(deltaTime);
            t1 = Stopwatch.GetTimestamp();
            _inputMs = (t1 - t0) * TicksToMs;

            t0 = Stopwatch.GetTimestamp();
            _loadingManager.Update();
            t1 = Stopwatch.GetTimestamp();
            _loadingMs = (t1 - t0) * TicksToMs;

            t0 = Stopwatch.GetTimestamp();
            _matchmakingPoller.Update(deltaTime);
            t1 = Stopwatch.GetTimestamp();
            _matchmakingMs = (t1 - t0) * TicksToMs;

            // Tick game simulation when in-game
            t0 = Stopwatch.GetTimestamp();
            if (_store.App.CurrentScreen.Value == ApplicationState.InGame)
            {
                _store.Game.CurrentGame?.Update(deltaTime);
            }
            t1 = Stopwatch.GetTimestamp();
            _gameUpdateMs = (t1 - t0) * TicksToMs;

            t0 = Stopwatch.GetTimestamp();
            _screenManager.Update(deltaTime);
            t1 = Stopwatch.GetTimestamp();
            _screenUpdateMs = (t1 - t0) * TicksToMs;

            t0 = Stopwatch.GetTimestamp();
            Raylib.BeginDrawing();
            t1 = Stopwatch.GetTimestamp();
            _beginDrawMs = (t1 - t0) * TicksToMs;

            t0 = Stopwatch.GetTimestamp();
            Raylib.ClearBackground(Color.Black);
            t1 = Stopwatch.GetTimestamp();
            _clearMs = (t1 - t0) * TicksToMs;

            // Render game world when in-game
            t0 = Stopwatch.GetTimestamp();
            if (_store.App.CurrentScreen.Value == ApplicationState.InGame)
            {
                _store.Game.CurrentGame?.Draw(deltaTime);
            }
            t1 = Stopwatch.GetTimestamp();
            _gameDrawMs = (t1 - t0) * TicksToMs;

            t0 = Stopwatch.GetTimestamp();
            _screenManager.Draw(deltaTime);
            t1 = Stopwatch.GetTimestamp();
            _screenDrawMs = (t1 - t0) * TicksToMs;

            // Draw frame timing overlay (before EndDrawing)
            if (_showFrameTiming)
            {
                DrawFrameTimingOverlay();
            }

            // Draw software cursor on top of everything
            if (_inputStore.InputManager.Device.SoftwareCursorEnabled)
            {
                DrawSoftwareCursor(_inputStore.InputManager.Device.MousePosition);
            }

            t0 = Stopwatch.GetTimestamp();
            Raylib.EndDrawing();
            t1 = Stopwatch.GetTimestamp();
            _endDrawMs = (t1 - t0) * TicksToMs;

            // Tracy frame marker - marks end of frame for profiler
            TracyWrapper.Profiler.HeartBeat();
        }
    }

    private void DrawFrameTimingOverlay()
    {
        const int FontSize = 12;
        int y = Raylib.GetScreenHeight() - 168;
        int x = 10;

        double total = _inputMs + _loadingMs + _matchmakingMs + _gameUpdateMs + _screenUpdateMs +
                       _beginDrawMs + _clearMs + _gameDrawMs + _screenDrawMs + _endDrawMs;

        Raylib.DrawText($"Frame Timing (F8):", x, y, FontSize, Color.White);
        y += 14;
        Raylib.DrawText($"  InputStore: {_inputMs:F3}ms", x, y, FontSize, GetColor(_inputMs));
        y += 14;
        Raylib.DrawText($"  Loading: {_loadingMs:F3}ms", x, y, FontSize, GetColor(_loadingMs));
        y += 14;
        Raylib.DrawText($"  Matchmaking: {_matchmakingMs:F3}ms", x, y, FontSize, GetColor(_matchmakingMs));
        y += 14;
        Raylib.DrawText($"  GameUpdate: {_gameUpdateMs:F3}ms", x, y, FontSize, GetColor(_gameUpdateMs));
        y += 14;
        Raylib.DrawText($"  ScreenUpdate: {_screenUpdateMs:F3}ms", x, y, FontSize, GetColor(_screenUpdateMs));
        y += 14;
        Raylib.DrawText($"  BeginDrawing: {_beginDrawMs:F3}ms", x, y, FontSize, GetColor(_beginDrawMs));
        y += 14;
        Raylib.DrawText($"  ClearBg: {_clearMs:F3}ms", x, y, FontSize, GetColor(_clearMs));
        y += 14;
        Raylib.DrawText($"  GameDraw: {_gameDrawMs:F3}ms", x, y, FontSize, GetColor(_gameDrawMs));
        y += 14;
        Raylib.DrawText($"  ScreenDraw: {_screenDrawMs:F3}ms", x, y, FontSize, GetColor(_screenDrawMs));
        y += 14;
        Raylib.DrawText($"  EndDrawing: {_endDrawMs:F3}ms", x, y, FontSize, GetColor(_endDrawMs));
        y += 14;
        Raylib.DrawText($"  TOTAL: {total:F2}ms", x, y, FontSize, Color.Yellow);
    }

    private static Color GetColor(double ms)
    {
        if (ms < 1.0) return Color.Green;
        if (ms < 5.0) return Color.Yellow;
        if (ms < 10.0) return Color.Orange;
        return Color.Red;
    }

    private static void DrawSoftwareCursor(Vector2 pos)
    {
        int x = (int)pos.X;
        int y = (int)pos.Y;

        const int ArmLength = 8;
        const int Gap = 3;

        // Draw crosshair with outline for visibility on any background
        // Horizontal line (left arm)
        Raylib.DrawLine(x - ArmLength, y, x - Gap, y, Color.Black);
        // Horizontal line (right arm)
        Raylib.DrawLine(x + Gap, y, x + ArmLength, y, Color.Black);
        // Vertical line (top arm)
        Raylib.DrawLine(x, y - ArmLength, x, y - Gap, Color.Black);
        // Vertical line (bottom arm)
        Raylib.DrawLine(x, y + Gap, x, y + ArmLength, Color.Black);

        // White center (offset by 1 for outline effect)
        Raylib.DrawLine(x - ArmLength + 1, y, x - Gap, y, Color.White);
        Raylib.DrawLine(x + Gap, y, x + ArmLength - 1, y, Color.White);
        Raylib.DrawLine(x, y - ArmLength + 1, x, y - Gap, Color.White);
        Raylib.DrawLine(x, y + Gap, x, y + ArmLength - 1, Color.White);

        // Center dot
        Raylib.DrawPixel(x, y, Color.White);
    }
}
