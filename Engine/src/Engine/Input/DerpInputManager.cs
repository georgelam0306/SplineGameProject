using System.Numerics;
using Core;
using Core.Input;

namespace DerpLib;

/// <summary>
/// DerpLib input manager using Derp.* calls for all hardware reads.
/// Provides the same action map/buffering/context logic as InputManager,
/// but uses SDL for gamepad and Silk.NET for keyboard/mouse.
/// </summary>
public sealed class DerpInputManager : IInputManager
{
    private const int MaxActionMaps = 16;
    private const int MaxContextStack = 8;

    private readonly InputActionMap[] _actionMaps;
    private int _actionMapCount;

    private readonly Dictionary<StringHandle, int> _mapLookup;
    private readonly List<ContextStackEntry> _contextStack;

    private ActionCallbackHandler? _globalCallback;

    private readonly DerpInputDevice _device;

    // Time tracking
    private double _time;

    // Input buffering
    private InputHistoryBuffer? _historyBuffer;
    private DiscreteActionBuffer? _discreteBuffer;
    private ulong _frameNumber;

    public IReadOnlyList<ContextStackEntry> ContextStack => _contextStack;
    public StringHandle? TopContext => _contextStack.Count > 0 ? _contextStack[^1].Name : null;
    public InputHistoryBuffer? HistoryBuffer => _historyBuffer;
    public DiscreteActionBuffer? DiscreteBuffer => _discreteBuffer;
    public ulong FrameNumber => _frameNumber;

    public DerpInputManager()
    {
        _device = new DerpInputDevice();
        _actionMaps = new InputActionMap[MaxActionMaps];
        _actionMapCount = 0;
        _mapLookup = new Dictionary<StringHandle, int>(MaxActionMaps);
        _contextStack = new List<ContextStackEntry>(MaxContextStack);
    }

    public void Update(double deltaTime)
    {
        _time += deltaTime;
        _device.Update();

        // Update action maps based on context stack
        if (_contextStack.Count == 0)
        {
            for (int i = 0; i < _actionMapCount; i++)
            {
                _actionMaps[i].Update(_device, _time, _globalCallback);
            }
        }
        else
        {
            for (int i = 0; i < _actionMapCount; i++)
            {
                var map = _actionMaps[i];
                if (IsContextActive(map.Name))
                {
                    map.Update(_device, _time, _globalCallback);
                }
            }
        }
    }

    #region Action Maps

    public InputActionMap CreateActionMap(StringHandle name)
    {
        if (_actionMapCount >= MaxActionMaps)
            return null!;

        var map = new InputActionMap(name);
        _mapLookup[name] = _actionMapCount;
        _actionMaps[_actionMapCount++] = map;
        return map;
    }

    public InputActionMap? GetActionMap(StringHandle name)
    {
        if (_mapLookup.TryGetValue(name, out int index))
        {
            return _actionMaps[index];
        }
        return null;
    }

    public InputActionMap? this[string name] => GetActionMap(name);

    public void EnableActionMap(StringHandle name) => GetActionMap(name)?.Enable();
    public void DisableActionMap(StringHandle name) => GetActionMap(name)?.Disable();

    public void SwitchToActionMap(StringHandle name)
    {
        for (int i = 0; i < _actionMapCount; i++)
        {
            if (_actionMaps[i].Name == name)
                _actionMaps[i].Enable();
            else
                _actionMaps[i].Disable();
        }
    }

    #endregion

    #region Context Stack

    public void PushContext(StringHandle name, ContextBlockPolicy policy = default)
    {
        if (_contextStack.Count >= MaxContextStack)
            return;

        _contextStack.Add(new ContextStackEntry(name, policy));
        GetActionMap(name)?.Enable(_device);
    }

    public void PopContext()
    {
        if (_contextStack.Count == 0)
            return;

        var entry = _contextStack[^1];
        _contextStack.RemoveAt(_contextStack.Count - 1);

        if (!IsOnStack(entry.Name))
            GetActionMap(entry.Name)?.Disable();
    }

    public void PopContext(StringHandle name)
    {
        for (int i = _contextStack.Count - 1; i >= 0; i--)
        {
            var entry = _contextStack[i];
            _contextStack.RemoveAt(i);

            if (!IsOnStack(entry.Name))
                GetActionMap(entry.Name)?.Disable();

            if (entry.Name == name)
                break;
        }
    }

    public bool IsContextActive(StringHandle name)
    {
        int contextIndex = -1;
        for (int i = 0; i < _contextStack.Count; i++)
        {
            if (_contextStack[i].Name == name)
            {
                contextIndex = i;
                break;
            }
        }

        if (contextIndex < 0)
            return false;

        for (int i = contextIndex + 1; i < _contextStack.Count; i++)
        {
            if (_contextStack[i].Policy.IsBlocked(name))
                return false;
        }

        return true;
    }

    public bool IsOnStack(StringHandle name)
    {
        for (int i = 0; i < _contextStack.Count; i++)
        {
            if (_contextStack[i].Name == name)
                return true;
        }
        return false;
    }

    #endregion

    #region Action Queries

    public InputValue ReadAction(StringHandle mapName, StringHandle actionName)
    {
        var map = GetActionMap(mapName);
        var action = map?.GetAction(actionName);
        return action?.Value ?? InputValue.Zero;
    }

    public bool WasPerformed(StringHandle mapName, StringHandle actionName)
    {
        var map = GetActionMap(mapName);
        var action = map?.GetAction(actionName);
        return action?.Phase == ActionPhase.Started;
    }

    public bool IsActive(StringHandle mapName, StringHandle actionName)
    {
        var map = GetActionMap(mapName);
        var action = map?.GetAction(actionName);
        return action?.Phase == ActionPhase.Started || action?.Phase == ActionPhase.Performed;
    }

    public bool WasCanceled(StringHandle mapName, StringHandle actionName)
    {
        var map = GetActionMap(mapName);
        var action = map?.GetAction(actionName);
        return action?.Phase == ActionPhase.Canceled;
    }

    public ActionPhase GetActionPhase(StringHandle mapName, StringHandle actionName)
    {
        var map = GetActionMap(mapName);
        var action = map?.GetAction(actionName);
        return action?.Phase ?? ActionPhase.Disabled;
    }

    #endregion

    #region Direct Device Access

    public Vector2 MousePosition => _device.MousePosition;
    public Vector2 MouseDelta => _device.MouseDelta;
    public float MouseScroll => _device.MouseScroll;

    public bool IsKeyDown(int key) => _device.IsKeyDown(key);
    public bool IsKeyPressed(int key) => _device.IsKeyPressed(key);
    public bool IsKeyReleased(int key) => _device.IsKeyReleased(key);

    public bool IsMouseButtonDown(int button) => _device.IsMouseButtonDown(button);
    public bool IsMouseButtonPressed(int button) => _device.IsMouseButtonPressed(button);
    public bool IsMouseButtonReleased(int button) => _device.IsMouseButtonReleased(button);

    #endregion

    #region Gamepad

    public bool IsGamepadConnected => _device.IsGamepadConnected;

    public int GamepadIndex
    {
        get => _device.GamepadIndex;
        set => _device.GamepadIndex = value;
    }

    public bool IsGamepadButtonDown(int button) => _device.IsGamepadButtonDown(button);
    public bool IsGamepadButtonPressed(int button) => _device.IsGamepadButtonPressed(button);
    public bool IsGamepadButtonReleased(int button) => _device.IsGamepadButtonReleased(button);
    public float GetGamepadAxis(int axis) => _device.GetGamepadAxis(axis);

    #endregion

    #region Input Buffering

    public void EnableBuffering(int historyFrames = InputHistoryBuffer.DefaultCapacity)
    {
        _historyBuffer = new InputHistoryBuffer(historyFrames);
        _discreteBuffer = new DiscreteActionBuffer();
    }

    public void RegisterDiscreteAction(StringHandle actionName)
    {
        _discreteBuffer?.RegisterAction(actionName);
    }

    public void CaptureSnapshot(ulong frameNumber)
    {
        if (_historyBuffer == null)
            return;

        _frameNumber = frameNumber;
        _historyBuffer.BeginFrame(frameNumber);

        for (int m = 0; m < _actionMapCount; m++)
        {
            var map = _actionMaps[m];
            if (!map.Enabled)
                continue;

            for (int a = 0; a < map.ActionCount; a++)
            {
                var action = map.GetActionByIndex(a);
                if (action == null)
                    continue;

                var snapshot = new ActionSnapshot(
                    frameNumber,
                    action.Name,
                    action.Phase,
                    action.Type,
                    action.Value
                );

                _historyBuffer.AddSnapshot(in snapshot);

                if (_discreteBuffer != null &&
                    (action.Phase == ActionPhase.Started || action.Phase == ActionPhase.Canceled))
                {
                    _discreteBuffer.Buffer(in snapshot);
                }
            }
        }

        _historyBuffer.EndFrame();
    }

    public bool WasActionStartedWithinFrames(StringHandle actionName, int withinFrames = 1)
    {
        return _historyBuffer?.WasStartedWithinFrames(actionName, withinFrames) ?? false;
    }

    public bool WasActionCanceledWithinFrames(StringHandle actionName, int withinFrames = 1)
    {
        return _historyBuffer?.WasCanceledWithinFrames(actionName, withinFrames) ?? false;
    }

    public bool TryGetLatestSnapshot(StringHandle actionName, out ActionSnapshot snapshot)
    {
        if (_historyBuffer != null)
            return _historyBuffer.TryGetLatest(actionName, out snapshot);
        snapshot = default;
        return false;
    }

    #endregion

    #region Callbacks

    public void SetGlobalCallback(ActionCallbackHandler? callback)
    {
        _globalCallback = callback;
    }

    #endregion
}
