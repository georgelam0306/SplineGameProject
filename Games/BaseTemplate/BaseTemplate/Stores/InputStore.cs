using System.IO;
using Core.Input;
using Serilog;

namespace BaseTemplate.GameApp.Stores;

/// <summary>
/// Application-level store for input management.
/// Holds the InputManager and loads input configs at initialization.
/// </summary>
public sealed class InputStore
{
    private readonly InputManager _inputManager;
    private readonly ILogger _log;

    public InputManager InputManager => _inputManager;

    public InputStore(ILogger logger)
    {
        _log = logger.ForContext<InputStore>();
        _inputManager = new InputManager();

        LoadInputConfigs();
        PushDefaultContexts();

        _log.Information("InputStore initialized with Gameplay and CameraFollow contexts");
    }

    private void LoadInputConfigs()
    {
        // Load gameplay input config
        string gameplayPath = Path.Combine("Resources", "input-gameplay.json");
        if (File.Exists(gameplayPath))
        {
            InputConfigLoader.LoadFromFile(_inputManager, gameplayPath);
            _log.Debug("Loaded input config: {Path}", gameplayPath);
        }
        else
        {
            _log.Warning("Input config not found: {Path}", gameplayPath);
            CreateDefaultGameplayActions();
        }

        // Load camera follow input config
        string cameraFollowPath = Path.Combine("Resources", "input-camera-follow.json");
        if (File.Exists(cameraFollowPath))
        {
            InputConfigLoader.LoadFromFile(_inputManager, cameraFollowPath);
            _log.Debug("Loaded input config: {Path}", cameraFollowPath);
        }
        else
        {
            _log.Warning("Input config not found: {Path}", cameraFollowPath);
            CreateDefaultCameraFollowActions();
        }

        // Load camera mouse control input config
        string cameraMousePath = Path.Combine("Resources", "input-camera-mouse.json");
        if (File.Exists(cameraMousePath))
        {
            InputConfigLoader.LoadFromFile(_inputManager, cameraMousePath);
            _log.Debug("Loaded input config: {Path}", cameraMousePath);
        }
        else
        {
            _log.Warning("Input config not found: {Path}", cameraMousePath);
            CreateDefaultCameraMouseActions();
        }
    }

    private void CreateDefaultGameplayActions()
    {
        // Fallback if JSON not found
        var gameplay = _inputManager.CreateActionMap("Gameplay");

        var move = gameplay.AddAction("Move", ActionType.Vector2);
        move.AddCompositeBinding("WASD", "<Keyboard>/w", "<Keyboard>/s", "<Keyboard>/a", "<Keyboard>/d");
        move.AddCompositeBinding("Arrows", "<Keyboard>/up", "<Keyboard>/down", "<Keyboard>/left", "<Keyboard>/right");

        var action = gameplay.AddAction("Action", ActionType.Button);
        action.AddBinding("<Keyboard>/space");
    }

    private void CreateDefaultCameraFollowActions()
    {
        // Fallback if JSON not found
        var cameraFollow = _inputManager.CreateActionMap("CameraFollow");

        var toggleMode = cameraFollow.AddAction("ToggleMode", ActionType.Button);
        toggleMode.AddBinding("<Keyboard>/tab");

        var zoom = cameraFollow.AddAction("Zoom", ActionType.Value);
        zoom.AddBinding("<Mouse>/scroll");
    }

    private void CreateDefaultCameraMouseActions()
    {
        // Fallback if JSON not found
        var cameraMouse = _inputManager.CreateActionMap("CameraMouseControl");

        var zoom = cameraMouse.AddAction("Zoom", ActionType.Value);
        zoom.AddBinding("<Mouse>/scroll");

        var mousePos = cameraMouse.AddAction("MousePosition", ActionType.Vector2);
        mousePos.AddBinding("<Mouse>/position");

        var toggleMode = cameraMouse.AddAction("ToggleMode", ActionType.Button);
        toggleMode.AddBinding("<Keyboard>/tab");
    }

    private void PushDefaultContexts()
    {
        // Push Gameplay context (lowest priority, always active)
        _inputManager.PushContext("Gameplay", ContextBlockPolicy.None);

        // Push CameraFollow context (default camera mode)
        _inputManager.PushContext("CameraFollow", ContextBlockPolicy.None);
    }

    /// <summary>
    /// Call at the start of each frame to update input state.
    /// </summary>
    public void Update(float deltaTime)
    {
        _inputManager.Update(deltaTime);
    }
}
