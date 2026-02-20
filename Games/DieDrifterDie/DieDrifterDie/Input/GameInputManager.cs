using System;
using System.Numerics;
using DieDrifterDie.GameApp.AppState;
using DieDrifterDie.Presentation.Camera;
using DieDrifterDie.GameApp.Core;
using DieDrifterDie.GameApp.Stores;
using DieDrifterDie.GameData.Schemas;
using DieDrifterDie.Infrastructure.Rollback;
using Core;
using Raylib_cs;

namespace DieDrifterDie.Presentation.Input;

public class GameInputManager
{
    private readonly CameraManager _cameraManager;
    private readonly GameplayStore _gameplayStore;
    private readonly InputStore _inputStore;
    private readonly InputActionBuffer _actionBuffer;
    private readonly InputRingBuffer _ringBuffer = new();

    // Screen coords for rendering the selection box
    public Vector2 SelectStartScreen { get; private set; }
    public Vector2 SelectEndScreen { get; private set; }
    public bool IsSelecting { get; private set; }

    // Expose ring buffer for advanced queries
    public InputRingBuffer RingBuffer => _ringBuffer;

    public GameInputManager(
        CameraManager cameraManager,
        GameplayStore gameplayStore,
        InputStore inputStore,
        InputActionBuffer actionBuffer,
        GameDataManager<GameDocDb> gameData)
    {
        _cameraManager = cameraManager;
        _gameplayStore = gameplayStore;
        _inputStore = inputStore;
        _actionBuffer = actionBuffer;

    }

    /// <summary>
    /// Buffer discrete input actions. Call this every render frame (before simulation loop).
    /// Discrete actions (clicks, key presses) are queued so they aren't lost when
    /// simulation runs at a lower rate than rendering.
    /// </summary>
    public void BufferInput()
    {
    }

    public GameInput PollGameInput()
    {
        var input = new GameInput();


        // _wasSelecting is now managed in BufferInput()
        return input;
    }

}

