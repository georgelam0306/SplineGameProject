using System;
using System.Numerics;
using Catrillion.AppState;
using Catrillion.Camera;
using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Rollback;
using Catrillion.Stores;
using Core;
using Raylib_cs;

namespace Catrillion.Input;

public class GameInputManager
{
    private readonly CameraManager _cameraManager;
    private readonly GameplayStore _gameplayStore;
    private readonly InputStore _inputStore;
    private readonly InputActionBuffer _actionBuffer;
    private readonly InputRingBuffer _ringBuffer = new();

    // Map config from GameDocDb
    private readonly int _tileSize;
    private readonly int _bottomBarHeight;

    // Frame counter for ring buffer
    private int _frameNumber;

    // Selection state tracking
    private bool _wasSelecting;
    private Fixed64Vec2 _selectStart;
    private Fixed64Vec2 _selectEnd;

    // Build mode paint tracking (prevents placing on same tile while holding mouse)
    private IntVec2? _lastPlacedTile;

    // Tracks when a LMB click was consumed by a command (attack-move, patrol)
    // Prevents the click from also triggering selection on release
    private bool _clickConsumedByCommand;

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

        // Load config from GameDocDb
        ref readonly var mapConfig = ref gameData.Db.MapConfigData.FindById(0);
        _tileSize = mapConfig.TileSize;
        _bottomBarHeight = mapConfig.BottomBarHeight;
    }

    /// <summary>
    /// Buffer discrete input actions. Call this every render frame (before simulation loop).
    /// Discrete actions (clicks, key presses) are queued so they aren't lost when
    /// simulation runs at a lower rate than rendering.
    /// </summary>
    public void BufferInput()
    {
        _frameNumber++;

        // Use InputManager's mouse position (supports software cursor)
        var mouseScreenPos = _inputStore.InputManager.Device.MousePosition;
        int screenHeight = Raylib.GetScreenHeight();
        bool mouseInGameWorld = mouseScreenPos.Y < screenHeight - _bottomBarHeight;

        // Build input flags for ring buffer
        var mouseFlags = MouseButtonFlags.None;
        if (Raylib.IsMouseButtonPressed(MouseButton.Left)) mouseFlags |= MouseButtonFlags.LeftPressed;
        if (Raylib.IsMouseButtonReleased(MouseButton.Left)) mouseFlags |= MouseButtonFlags.LeftReleased;
        if (Raylib.IsMouseButtonDown(MouseButton.Left)) mouseFlags |= MouseButtonFlags.LeftDown;
        if (Raylib.IsMouseButtonPressed(MouseButton.Right)) mouseFlags |= MouseButtonFlags.RightPressed;
        if (Raylib.IsMouseButtonReleased(MouseButton.Right)) mouseFlags |= MouseButtonFlags.RightReleased;
        if (Raylib.IsMouseButtonDown(MouseButton.Right)) mouseFlags |= MouseButtonFlags.RightDown;

        var keyFlags = KeyFlags.None;
        if (Raylib.IsKeyPressed(KeyboardKey.A)) keyFlags |= KeyFlags.A_Pressed;
        if (Raylib.IsKeyPressed(KeyboardKey.P)) keyFlags |= KeyFlags.P_Pressed;
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) keyFlags |= KeyFlags.Escape_Pressed;
        if (Raylib.IsKeyPressed(KeyboardKey.S)) keyFlags |= KeyFlags.S_Pressed;
        if (Raylib.IsKeyPressed(KeyboardKey.H)) keyFlags |= KeyFlags.H_Pressed;

        // Convert to world coords
        var mouseWorldVec = _cameraManager.ScreenToWorld(mouseScreenPos);
        var mouseWorld = new Fixed64Vec2(
            Fixed64.FromFloat(mouseWorldVec.X),
            Fixed64.FromFloat(mouseWorldVec.Y)
        );

        // Record to ring buffer
        _ringBuffer.Record(_frameNumber, mouseScreenPos, mouseWorld, mouseFlags, keyFlags, mouseInGameWorld);

        // Buffer mode entry keys (A for attack-move, P for patrol)
        if (Raylib.IsKeyPressed(KeyboardKey.A))
        {
            _actionBuffer.QueueEnterAttackMoveMode();
        }
        if (Raylib.IsKeyPressed(KeyboardKey.P))
        {
            _actionBuffer.QueueEnterPatrolMode();
        }

        // Buffer RMB click for move command (only in game world)
        if (Raylib.IsMouseButtonPressed(MouseButton.Right) && mouseInGameWorld)
        {
            _actionBuffer.QueueMoveCommand(mouseWorld);
        }

        // Buffer LMB click in command mode (attack-move, patrol)
        var commandMode = _gameplayStore.UnitCommandMode.CurrentValue;
        if (commandMode != AppState.UnitCommandMode.None && mouseInGameWorld)
        {
            if (Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                if (commandMode == AppState.UnitCommandMode.AttackMove)
                {
                    _actionBuffer.QueueAttackMoveCommand(mouseWorld);
                }
                else if (commandMode == AppState.UnitCommandMode.Patrol)
                {
                    _actionBuffer.QueuePatrolCommand(mouseWorld);
                }
            }
        }

        // Buffer selection start (LMB down transition in normal mode)
        bool isLeftMouseDown = _ringBuffer.IsLeftDown();
        bool leftJustPressed = (mouseFlags & MouseButtonFlags.LeftPressed) != 0;
        bool leftJustReleased = (mouseFlags & MouseButtonFlags.LeftReleased) != 0;

        if (leftJustPressed && mouseInGameWorld && commandMode == AppState.UnitCommandMode.None && !_clickConsumedByCommand)
        {
            _actionBuffer.QueueSelectionStart(mouseWorld);
            SelectStartScreen = mouseScreenPos;
            _wasSelecting = true;
        }

        // Buffer selection complete (LMB release transition)
        if (leftJustReleased && _wasSelecting && !_clickConsumedByCommand)
        {
            _actionBuffer.QueueSelectionComplete(mouseWorld);
            _wasSelecting = false;
        }

        // Reset click consumed when mouse released
        if (!isLeftMouseDown)
            _clickConsumedByCommand = false;

        // Update selection box screen positions at render rate for smooth visual
        if (_wasSelecting && isLeftMouseDown)
        {
            SelectEndScreen = mouseScreenPos;
            IsSelecting = true;
        }
        else if (!isLeftMouseDown)
        {
            IsSelecting = false;
            _wasSelecting = false;
        }
    }

    public GameInput PollGameInput()
    {
        var input = new GameInput();

        // Handle build mode first (uses InputManager for mouse position)
        if (HandleBuildMode(ref input))
        {
            _actionBuffer.Clear();  // Don't process buffered actions in build mode
            return input;
        }

        // Handle unit command modes (attack-move, patrol) - drain buffered commands
        if (HandleUnitCommandMode(ref input))
        {
            _actionBuffer.Clear();  // Commands were processed, clear buffer
            return input;
        }

        // Drain buffered mode entry keys using ring buffer
        if (_ringBuffer.WasKeyPressed(KeyFlags.A_Pressed))
        {
            _gameplayStore.EnterAttackMoveMode();
        }
        if (_ringBuffer.WasKeyPressed(KeyFlags.P_Pressed))
        {
            _gameplayStore.EnterPatrolMode();
        }

        // Use ring buffer for selection state
        bool isLeftMouseDown = _ringBuffer.IsLeftDown();

        // Use buffered selection start if available
        if (_actionBuffer.SelectionStarted)
        {
            _selectStart = _actionBuffer.SelectStart;
        }

        // Update end position from ring buffer while selecting
        if (isLeftMouseDown && _wasSelecting)
        {
            _selectEnd = _ringBuffer.GetLeftPressWorld();  // Will be updated by current mouse pos
            // Actually get current mouse world from latest frame
            var mouseWorldVec = _cameraManager.ScreenToWorld(_ringBuffer.LatestMouseScreen);
            _selectEnd = new Fixed64Vec2(
                Fixed64.FromFloat(mouseWorldVec.X),
                Fixed64.FromFloat(mouseWorldVec.Y)
            );
        }

        // Continuous state for selection box rendering
        bool isCurrentlySelecting = isLeftMouseDown && _wasSelecting;
        input.SelectStart = _selectStart;
        input.SelectEnd = _selectEnd;
        input.IsSelecting = isCurrentlySelecting;

        // Use buffered selection complete (LMB release was captured)
        if (_actionBuffer.SelectionCompleted)
        {
            if (!_clickConsumedByCommand)
            {
                input.HasSelectionComplete = true;
                input.SelectEnd = _actionBuffer.SelectEnd;  // Use buffered end position
            }
            _clickConsumedByCommand = false;
        }

        // Drain buffered move command (RMB click) - use ring buffer
        if (_ringBuffer.WasRightPressed() && _ringBuffer.WasRightPressInGameWorld())
        {
            input.HasMoveCommand = true;
            input.MoveTarget = _ringBuffer.GetRightPressWorld();
        }

        // Clear the action buffer after draining
        _actionBuffer.Clear();

        // Handle garrison eject command from UI
        if (_gameplayStore.ShouldEjectGarrison.CurrentValue)
        {
            input.HasExitGarrisonCommand = true;
            _gameplayStore.ShouldEjectGarrison.Value = false;  // Reset after reading
        }

        // Handle single unit eject from UI
        var singleEjectHandle = _gameplayStore.SingleEjectUnitHandle.CurrentValue;
        if (singleEjectHandle.IsValid)
        {
            input.SingleEjectUnitHandle = singleEjectHandle;
            _gameplayStore.SingleEjectUnitHandle.Value = SimTable.SimHandle.Invalid;  // Reset after reading
        }

        // Handle train unit command from UI
        var trainCmd = _gameplayStore.PendingTrainCommand.CurrentValue;
        if (trainCmd.HasValue)
        {
            input.HasTrainUnitCommand = true;
            input.TrainUnitBuildingHandle = trainCmd.Value.building;
            input.TrainUnitTypeId = trainCmd.Value.unitType;
            _gameplayStore.PendingTrainCommand.Value = null;  // Reset after reading
        }

        // Handle cancel training command from UI
        var cancelCmd = _gameplayStore.PendingCancelTrainingCommand.CurrentValue;
        if (cancelCmd.HasValue)
        {
            input.HasCancelTrainingCommand = true;
            input.CancelTrainingBuildingHandle = cancelCmd.Value.building;
            input.CancelTrainingSlotIndex = cancelCmd.Value.slotIndex;
            _gameplayStore.PendingCancelTrainingCommand.Value = null;  // Reset after reading
        }

        // Handle research command from UI
        var researchCmd = _gameplayStore.PendingResearchCommand.CurrentValue;
        if (researchCmd >= 0)
        {
            input.HasResearchCommand = true;
            input.ResearchCommand = (byte)researchCmd;
            _gameplayStore.PendingResearchCommand.Value = -1;  // Reset after reading
        }

        // Handle upgrade command from UI
        var upgradeHandle = _gameplayStore.PendingUpgradeCommand.CurrentValue;
        if (upgradeHandle.IsValid)
        {
            input.HasUpgradeCommand = true;
            input.UpgradeBuildingHandle = upgradeHandle;
            _gameplayStore.PendingUpgradeCommand.Value = SimTable.SimHandle.Invalid;  // Reset after reading
        }

        // Handle destroy command from UI queue (one per frame)
        if (_gameplayStore.PendingDestroyQueue.Count > 0)
        {
            var destroyHandle = _gameplayStore.PendingDestroyQueue.Dequeue();
            input.HasDestroyCommand = true;
            input.DestroyBuildingHandle = destroyHandle;
        }

        // Handle repair command from UI
        var repairHandle = _gameplayStore.PendingRepairCommand.CurrentValue;
        if (repairHandle.IsValid)
        {
            input.HasRepairCommand = true;
            input.RepairBuildingHandle = repairHandle;
            _gameplayStore.PendingRepairCommand.Value = SimTable.SimHandle.Invalid;  // Reset after reading
        }

        // Handle cancel repair command from UI
        var cancelRepairHandle = _gameplayStore.PendingCancelRepairCommand.CurrentValue;
        if (cancelRepairHandle.IsValid)
        {
            input.HasCancelRepairCommand = true;
            input.CancelRepairBuildingHandle = cancelRepairHandle;
            _gameplayStore.PendingCancelRepairCommand.Value = SimTable.SimHandle.Invalid;  // Reset after reading
        }

        // _wasSelecting is now managed in BufferInput()
        return input;
    }

    /// <summary>
    /// Handles build mode input using InputManager's cached mouse position.
    /// Returns true if in build mode (caller should skip other input handling).
    /// </summary>
    private bool HandleBuildMode(ref GameInput input)
    {
        if (!_gameplayStore.IsInBuildMode.CurrentValue)
            return false;

        var inputMgr = _inputStore.InputManager;

        // Use action system for ESC (CancelBuild action from JSON)
        if (inputMgr.WasPerformed("Gameplay", "CancelBuild"))
        {
            _gameplayStore.CancelBuildMode();
            _lastPlacedTile = null;
            return true;
        }

        // Get mouse position from InputManager (properly cached)
        var mouseScreen = inputMgr.MousePosition;
        int screenHeight = Raylib.GetScreenHeight();
        bool mouseInGameWorld = mouseScreen.Y < screenHeight - _bottomBarHeight;

        if (!mouseInGameWorld)
        {
            _gameplayStore.ClearPreview();
            return true;
        }

        // Convert screen → world → tile (supports negative coordinates)
        var mouseWorld = _cameraManager.ScreenToWorld(mouseScreen);
        var tile = new IntVec2(
            (int)MathF.Floor(mouseWorld.X / _tileSize),
            (int)MathF.Floor(mouseWorld.Y / _tileSize)
        );

        // Update preview position
        _gameplayStore.UpdatePreview(tile.X, tile.Y);

        // Paint mode: continuous placement while mouse is held down
        bool isMouseDown = inputMgr.IsMouseButtonDown(MouseButton.Left);

        if (isMouseDown)
        {
            // Only place if this is a new tile (prevents duplicate placements on same tile)
            bool isNewTile = !_lastPlacedTile.HasValue ||
                             _lastPlacedTile.Value.X != tile.X ||
                             _lastPlacedTile.Value.Y != tile.Y;

            if (isNewTile)
            {
                var buildingType = _gameplayStore.BuildModeType.Value;
                if (buildingType.HasValue)
                {
                    input.HasBuildingPlacement = true;
                    input.BuildingPlacementTile = tile;
                    input.BuildingTypeToBuild = (byte)buildingType.Value;
                    _lastPlacedTile = tile;
                    // Note: Don't call CancelBuildMode() - stay in build mode for paint mode
                }
            }
        }
        else
        {
            // Mouse released - clear last placed tile so next click can place on same tile
            _lastPlacedTile = null;
        }

        return true;  // In build mode, skip other input
    }

    /// <summary>
    /// Handles unit command mode input (attack-move, patrol).
    /// Uses buffered commands from InputActionBuffer.
    /// Returns true if in command mode (caller should skip other input handling).
    /// </summary>
    private bool HandleUnitCommandMode(ref GameInput input)
    {
        var commandMode = _gameplayStore.UnitCommandMode.CurrentValue;
        if (commandMode == AppState.UnitCommandMode.None)
            return false;

        var inputMgr = _inputStore.InputManager;

        // ESC cancels command mode
        if (inputMgr.WasPerformed("Gameplay", "CancelBuild"))
        {
            _gameplayStore.CancelUnitCommandMode();
            return true;
        }

        // Drain buffered command for current mode
        if (commandMode == AppState.UnitCommandMode.AttackMove && _actionBuffer.HasAttackMoveCommand)
        {
            input.HasAttackMoveCommand = true;
            input.AttackMoveTarget = _actionBuffer.AttackMoveTarget;
            _clickConsumedByCommand = true;
            _gameplayStore.CancelUnitCommandMode();
        }
        else if (commandMode == AppState.UnitCommandMode.Patrol && _actionBuffer.HasPatrolCommand)
        {
            input.HasPatrolCommand = true;
            input.PatrolTarget = _actionBuffer.PatrolTarget;
            _clickConsumedByCommand = true;
            _gameplayStore.CancelUnitCommandMode();
        }

        return true;  // In command mode, skip other input
    }
}

