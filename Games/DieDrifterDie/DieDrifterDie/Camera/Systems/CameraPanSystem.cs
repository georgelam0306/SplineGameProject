using System;
using System.Numerics;
using Core;
using Core.Input;
using Friflo.Engine.ECS.Systems;
using Raylib_cs;
using DieDrifterDie.Presentation.Input;
using DieDrifterDie.GameApp.Stores;

namespace DieDrifterDie.Presentation.Camera.Systems;

public sealed class CameraPanSystem : BaseSystem
{
    private readonly CameraManager _cameraManager;
    private readonly UIInputManager _uiInputManager;
    private readonly InputStore _inputStore;

    private const float PanSpeed = 400f;
    private const int EdgeThreshold = 100;
    private const float MinZoom = 0.25f;
    private const float MaxZoom = 4f;
    private const float ZoomSpeed = 0.1f;

    private static readonly StringHandle CameraMouseControl = "CameraMouseControl";
    private static readonly StringHandle CameraFollow = "CameraFollow";

    public CameraPanSystem(CameraManager cameraManager, UIInputManager uiInputManager, InputStore inputStore)
    {
        _cameraManager = cameraManager;
        _uiInputManager = uiInputManager;
        _inputStore = inputStore;
    }

    protected override void OnUpdateGroup()
    {
        HandleModeToggle();

        // Zoom works in both CameraFollow and CameraMouseControl modes
        HandleZoom();

        // Only process camera edge pan when in mouse control mode
        if (!_inputStore.InputManager.IsContextActive(CameraMouseControl))
        {
            return;
        }

        HandleEdgePan();
    }

    private void HandleModeToggle()
    {
        // Toggle on release to avoid immediate re-trigger when switching contexts
        // Check for Tab release in CameraFollow context to switch to mouse control
        if (_inputStore.InputManager.IsContextActive(CameraFollow))
        {
            if (_inputStore.InputManager.WasCanceled(CameraFollow, "ToggleMode"))
            {
                // Pop CameraFollow, push CameraMouseControl
                _inputStore.InputManager.PopContext(CameraFollow);
                _inputStore.InputManager.PushContext(CameraMouseControl, ContextBlockPolicy.Block("CameraFollow"));
            }
        }
        // Check for Tab release in CameraMouseControl context to switch back to follow
        else if (_inputStore.InputManager.IsContextActive(CameraMouseControl))
        {
            if (_inputStore.InputManager.WasCanceled(CameraMouseControl, "ToggleMode"))
            {
                // Pop CameraMouseControl, push CameraFollow
                _inputStore.InputManager.PopContext(CameraMouseControl);
                _inputStore.InputManager.PushContext(CameraFollow, ContextBlockPolicy.None);
            }
        }
    }

    private void HandleEdgePan()
    {
        if (!_uiInputManager.IsMouseAvailable())
        {
            return;
        }

        // Read mouse position from action (screen coordinates)
        Vector2 mousePos = _inputStore.InputManager.ReadAction(CameraMouseControl, "MousePosition").Vector2;
        // Use actual screen dimensions for edge detection, not render texture size
        int screenWidth = Raylib.GetScreenWidth();
        int screenHeight = Raylib.GetScreenHeight();
        float deltaTime = Tick.deltaTime;

        float panX = 0f;
        float panY = 0f;

        if (mousePos.X < EdgeThreshold)
        {
            panX = -1f;
        }
        else if (mousePos.X > screenWidth - EdgeThreshold)
        {
            panX = 1f;
        }

        if (mousePos.Y < EdgeThreshold)
        {
            panY = -1f;
        }
        else if (mousePos.Y > screenHeight - EdgeThreshold)
        {
            panY = 1f;
        }

        if (panX != 0f || panY != 0f)
        {
            _cameraManager.SetTarget(
                _cameraManager.TargetX + panX * PanSpeed * deltaTime,
                _cameraManager.TargetY + panY * PanSpeed * deltaTime);
        }
    }

    private void HandleZoom()
    {
        // Read zoom from whichever camera context is active
        float wheel = 0f;
        if (_inputStore.InputManager.IsContextActive(CameraMouseControl))
        {
            wheel = _inputStore.InputManager.ReadAction(CameraMouseControl, "Zoom").Value;
        }
        else if (_inputStore.InputManager.IsContextActive(CameraFollow))
        {
            wheel = _inputStore.InputManager.ReadAction(CameraFollow, "Zoom").Value;
        }

        if (wheel == 0)
        {
            return;
        }

        // Get mouse position and world position under cursor BEFORE zoom change
        var mouseScreenPos = new Vector2(Raylib.GetMouseX(), Raylib.GetMouseY());
        Vector2 worldPosBefore = _cameraManager.ScreenToWorld(mouseScreenPos);

        // Apply zoom
        float newZoom = _cameraManager.Zoom + wheel * ZoomSpeed;
        newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
        _cameraManager.SetZoom(newZoom);

        // After zoom, calculate what world position is now under the cursor
        Vector2 worldPosAfter = _cameraManager.ScreenToWorld(mouseScreenPos);

        // Adjust camera target to keep worldPosBefore under the cursor
        // Delta = worldPosBefore - worldPosAfter tells us how much the view shifted
        Vector2 delta = worldPosBefore - worldPosAfter;
        _cameraManager.SetTarget(
            _cameraManager.TargetX + delta.X,
            _cameraManager.TargetY + delta.Y);
    }
}

