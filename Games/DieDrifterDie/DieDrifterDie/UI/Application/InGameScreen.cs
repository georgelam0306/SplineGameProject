using System;
using Raylib_cs;
using Serilog;
using DieDrifterDie.GameApp.AppState;
using DieDrifterDie.GameApp.Stores;

namespace DieDrifterDie.Presentation.UI;

/// <summary>
/// In-game screen - handles UI overlays and game state transitions.
/// Note: Game.Update() and Game.Draw() are called by Main.Run(), not here.
/// </summary>
public sealed class InGameScreen
{
    private readonly RootStore _store;
    private readonly AppEventBus _eventBus;
    private readonly InputStore _inputStore;
    private readonly ILogger _log;

    // Profiler overlay toggles
    private bool _showRenderProfiler;
    private bool _showUpdateTiming;

    public InGameScreen(RootStore store, AppEventBus eventBus, InputStore inputStore, ILogger logger)
    {
        _store = store;
        _eventBus = eventBus;
        _inputStore = inputStore;
        _log = logger.ForContext<InGameScreen>();
    }

    public void OnEnter() { }
    public void OnExit() { }

    public void Update(float deltaTime)
    {
        var game = _store.Game.CurrentGame;
        if (game == null) return;

        // Handle profiler input (F5-F9)
        HandleProfilerInput(game);

        // Check for game over and transition to GameOver screen
        if (game.IsGameOver)
        {
            _store.App.CurrentScreen.Value = ApplicationState.GameOver;
        }
    }

    public void Draw(float deltaTime)
    {
        var game = _store.Game.CurrentGame;
        if (game == null) return;

        // Draw profiler overlays (on top of game rendering done by Main)
        DrawProfilerOverlay(game);
        DrawRenderProfilerOverlay(game);
        DrawUpdateTimingOverlay(game);
    }

    private void HandleProfilerInput(Game game)
    {
        var profiler = game.GameSimulation.Profiler;

        // F5: Toggle profiler overlay
        if (Raylib.IsKeyPressed(KeyboardKey.F5))
        {
            profiler.ShowOverlay = !profiler.ShowOverlay;
            profiler.Enabled = profiler.ShowOverlay;
        }

        // F6: Toggle CSV recording
        if (Raylib.IsKeyPressed(KeyboardKey.F6))
        {
            if (profiler.IsRecording)
            {
                profiler.StopRecording();
                _log.Information("Profiler recording stopped. Frames: {Frames}", profiler.RecordedFrameCount);
            }
            else
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string filePath = $"Logs/profiler_{timestamp}.csv";
                profiler.StartRecording(filePath);
                _log.Information("Profiler recording started: {Path}", filePath);
            }
        }

        // F7: Toggle render profiler overlay
        if (Raylib.IsKeyPressed(KeyboardKey.F7))
        {
            _showRenderProfiler = !_showRenderProfiler;
        }

        // F9: Toggle update timing breakdown
        if (Raylib.IsKeyPressed(KeyboardKey.F9))
        {
            _showUpdateTiming = !_showUpdateTiming;
        }
    }

    private void DrawProfilerOverlay(Game game)
    {
        var profiler = game.GameSimulation.Profiler;
        if (!profiler.ShowOverlay) return;

        const int LineHeight = 11;
        const int FontSize = 10;
        int startX = 10;
        int startY = 10;
        int currentY = startY;

        // Draw header
        var headerColor = new Color(200, 200, 200, 255);
        string header = $"Sim: {profiler.AverageTotalMs:F2}ms ({profiler.TotalFrameMs:F2}ms)";
        Raylib.DrawText(header, startX, currentY, FontSize + 2, headerColor);
        currentY += LineHeight + 4;

        // Draw per-system times
        var names = profiler.SystemNames;
        var avgMs = profiler.AverageMilliseconds;

        for (int i = 0; i < names.Length; i++)
        {
            double ms = avgMs[i];

            // Color based on time: green < 0.1ms, yellow < 0.5ms, orange < 1ms, red >= 1ms
            Color color;
            if (ms < 0.1) color = new Color(100, 200, 100, 255);
            else if (ms < 0.5) color = new Color(200, 200, 100, 255);
            else if (ms < 1.0) color = new Color(255, 165, 0, 255);
            else color = new Color(255, 80, 80, 255);

            string text = $"{names[i]}: {ms:F3}ms";
            Raylib.DrawText(text, startX, currentY, FontSize, color);
            currentY += LineHeight;
        }

        // Show recording status if active
        if (profiler.IsRecording)
        {
            currentY += 4;
            string recText = $"REC: {profiler.RecordedFrameCount} frames";
            Raylib.DrawText(recText, startX, currentY, FontSize, new Color(255, 50, 50, 255));
        }
    }

    private void DrawRenderProfilerOverlay(Game game)
    {
        if (!_showRenderProfiler) return;

        const int LineHeight = 11;
        const int FontSize = 10;
        int screenWidth = Raylib.GetScreenWidth();
        int startY = 10;
        int currentY = startY;

        // Calculate total render time
        double totalMs = 0;
        foreach (var system in game.RenderRoot.ChildSystems)
        {
            totalMs += system.Perf.LastMs;
        }

        // Draw header (right-aligned)
        var headerColor = new Color(200, 200, 200, 255);
        string header = $"Render: {totalMs:F2}ms";
        int headerWidth = Raylib.MeasureText(header, FontSize + 2);
        Raylib.DrawText(header, screenWidth - headerWidth - 10, currentY, FontSize + 2, headerColor);
        currentY += LineHeight + 4;

        // Draw per-system times
        foreach (var system in game.RenderRoot.ChildSystems)
        {
            double ms = system.Perf.LastMs;

            // Color based on time
            Color color;
            if (ms < 0.1) color = new Color(100, 200, 100, 255);
            else if (ms < 0.5) color = new Color(200, 200, 100, 255);
            else if (ms < 1.0) color = new Color(255, 165, 0, 255);
            else color = new Color(255, 80, 80, 255);

            string text = $"{system.Name}: {ms:F3}ms";
            int textWidth = Raylib.MeasureText(text, FontSize);
            Raylib.DrawText(text, screenWidth - textWidth - 10, currentY, FontSize, color);
            currentY += LineHeight;
        }
    }

    private void DrawUpdateTimingOverlay(Game game)
    {
        if (!_showUpdateTiming) return;

        const int FontSize = 12;
        int y = Raylib.GetScreenHeight() / 2 - 60;
        int x = 10;

        double total = game.NormalUpdateMs + game.SyncChecksMs + game.UpdateRootMs;

        Raylib.DrawText("Update Breakdown (F9):", x, y, FontSize, Color.White);
        y += 16;
        Raylib.DrawText($"  NormalUpdate: {game.NormalUpdateMs:F2}ms ({game.TicksLastFrame} ticks)", x, y, FontSize, GetTimingColor(game.NormalUpdateMs));
        y += 14;
        Raylib.DrawText($"    - Reload: {game.ReloadMs:F2}ms", x, y, FontSize, GetTimingColor(game.ReloadMs));
        y += 14;
        Raylib.DrawText($"    - RollbackCheck: {game.RollbackCheckMs:F2}ms", x, y, FontSize, GetTimingColor(game.RollbackCheckMs));
        y += 14;
        Raylib.DrawText($"    - PauseCheck: {game.PauseCheckMs:F2}ms", x, y, FontSize, GetTimingColor(game.PauseCheckMs));
        y += 14;
        Raylib.DrawText($"    - InputPoll: {game.InputPollMs:F2}ms", x, y, FontSize, GetTimingColor(game.InputPollMs));
        y += 14;
        Raylib.DrawText($"    - NetworkSend: {game.NetworkSendMs:F2}ms", x, y, FontSize, GetTimingColor(game.NetworkSendMs));
        y += 14;
        Raylib.DrawText($"    - SaveSnapshot: {game.SaveSnapshotMs:F2}ms", x, y, FontSize, GetTimingColor(game.SaveSnapshotMs));
        y += 14;
        Raylib.DrawText($"    - SimTick: {game.SimTickMs:F2}ms", x, y, FontSize, GetTimingColor(game.SimTickMs));
        y += 14;
        Raylib.DrawText($"    - RecordFrame: {game.RecordMs:F2}ms", x, y, FontSize, GetTimingColor(game.RecordMs));
        y += 14;
        Raylib.DrawText($"    - SyncSend: {game.SyncSendMs:F2}ms", x, y, FontSize, GetTimingColor(game.SyncSendMs));
        y += 14;
        double normalSum = game.ReloadMs + game.RollbackCheckMs + game.PauseCheckMs + game.InputPollMs +
                          game.NetworkSendMs + game.SaveSnapshotMs + game.SimTickMs + game.RecordMs + game.SyncSendMs;
        double gap = game.NormalUpdateMs - normalSum;
        Raylib.DrawText($"    = Sum: {normalSum:F2}ms (gap: {gap:F2}ms)", x, y, FontSize, gap > 1.0 ? Color.Red : Color.Green);
        y += 14;
        Raylib.DrawText($"  SyncChecks: {game.SyncChecksMs:F2}ms", x, y, FontSize, GetTimingColor(game.SyncChecksMs));
        y += 14;
        Raylib.DrawText($"  UpdateRoot (ECS): {game.UpdateRootMs:F2}ms", x, y, FontSize, GetTimingColor(game.UpdateRootMs));
        y += 14;
        Raylib.DrawText($"  TOTAL: {total:F2}ms", x, y, FontSize, Color.Yellow);
    }

    private static Color GetTimingColor(double ms)
    {
        if (ms < 1.0) return Color.Green;
        if (ms < 5.0) return Color.Yellow;
        if (ms < 16.0) return Color.Orange;
        return Color.Red;
    }
}
