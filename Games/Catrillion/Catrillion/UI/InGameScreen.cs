using System;
using Catrillion.AppState;
using Catrillion.Stores;
using Raylib_cs;

namespace Catrillion.UI;

/// <summary>
/// In-game screen that updates and draws the active game.
/// </summary>
public sealed class InGameScreen
{
    private readonly RootStore _store;
    private readonly AppEventBus _eventBus;
    private readonly InputStore _inputStore;
    private readonly PauseMenu _pauseMenu = new();

    public InGameScreen(RootStore store, AppEventBus eventBus, InputStore inputStore)
    {
        _store = store;
        _eventBus = eventBus;
        _inputStore = inputStore;
    }

    public void OnEnter()
    {
        _pauseMenu.Close();
        // Confine cursor to window during gameplay
        _inputStore.InputManager.Device.CursorConfined = true;
        // Enable software cursor (hides real cursor)
        _inputStore.InputManager.Device.EnableSoftwareCursor();
    }

    public void OnExit()
    {
        _pauseMenu.Close();
        // Release cursor when leaving gameplay
        _inputStore.InputManager.Device.CursorConfined = false;
        // Disable software cursor (shows real cursor)
        _inputStore.InputManager.Device.DisableSoftwareCursor();
    }

    public void Update(float deltaTime)
    {
        // Handle ESC key for pause menu toggle
        // Only toggle if not in build mode or command mode (those use ESC to cancel)
        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            bool inBuildMode = _store.Gameplay.IsInBuildMode.CurrentValue;
            bool inCommandMode = _store.Gameplay.UnitCommandMode.CurrentValue != UnitCommandMode.None;

            if (!inBuildMode && !inCommandMode)
            {
                _pauseMenu.Toggle();
                // Release cursor when pause menu opens, confine when it closes
                _inputStore.InputManager.Device.CursorConfined = !_pauseMenu.IsOpen;
                // Toggle software cursor with pause state
                if (_pauseMenu.IsOpen)
                    _inputStore.InputManager.Device.DisableSoftwareCursor();
                else
                    _inputStore.InputManager.Device.EnableSoftwareCursor();
            }
        }

        var game = _store.Game.CurrentGame;
        if (game == null) return;

        game.Update(deltaTime);

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

        game.Draw(deltaTime);

        // Draw pause menu overlay if open
        if (_pauseMenu.IsOpen)
        {
            var action = _pauseMenu.UpdateAndDraw();

            switch (action)
            {
                case PauseMenuAction.Resume:
                    // Re-confine cursor when resuming gameplay
                    _inputStore.InputManager.Device.CursorConfined = true;
                    // Re-enable software cursor
                    _inputStore.InputManager.Device.EnableSoftwareCursor();
                    break;

                case PauseMenuAction.MainMenu:
                    _eventBus.PublishReturnToMainMenu();
                    break;

                case PauseMenuAction.ExitToDesktop:
                    Environment.Exit(0);
                    break;

                case PauseMenuAction.BugReport:
                    _eventBus.PublishBugReportRequested(null);
                    break;
            }
        }
    }
}
