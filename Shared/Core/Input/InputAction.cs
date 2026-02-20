using System;

namespace Core.Input;

/// <summary>
/// Represents an input action with its bindings and current state.
/// Pre-allocates binding array to avoid runtime allocations.
/// </summary>
public sealed class InputAction
{
    private const int MaxBindings = 16;
    private const int MaxDerivedTriggers = 4;

    public readonly StringHandle Name;
    public readonly ActionType Type;

    // Fixed-size arrays for zero-allocation
    private readonly InputBinding[] _bindings;
    private int _bindingCount;

    private readonly DerivedTrigger[] _derivedTriggers;
    private int _derivedTriggerCount;

    // State tracking
    private InputValue _currentValue;
    private InputValue _previousValue;
    private ActionPhase _phase;
    private double _startTime;

    // Composite tracking (for WASD-style bindings)
    private readonly float[] _compositeValues;  // up, down, left, right

    // Dead zone
    public float DeadZone { get; set; } = 0.15f;

    // Enabled state
    public bool Enabled { get; set; } = true;

    public InputValue Value => _currentValue;
    public InputValue PreviousValue => _previousValue;
    public ActionPhase Phase => _phase;

    public InputAction(StringHandle name, ActionType type)
    {
        Name = name;
        Type = type;
        _bindings = new InputBinding[MaxBindings];
        _bindingCount = 0;
        _derivedTriggers = new DerivedTrigger[MaxDerivedTriggers];
        _derivedTriggerCount = 0;
        _compositeValues = new float[4];
        _phase = ActionPhase.Waiting;
    }

    public void AddDerivedTrigger(StringHandle sourceActionName, DerivedTriggerKind kind, float threshold, DerivedTriggerMode mode = DerivedTriggerMode.Hold)
    {
        if (Type != ActionType.Button)
        {
            return;
        }

        if (_derivedTriggerCount >= MaxDerivedTriggers)
        {
            return;
        }

        _derivedTriggers[_derivedTriggerCount++] = new DerivedTrigger
        {
            SourceActionName = sourceActionName,
            SourceActionIndex = -1,
            Kind = kind,
            Mode = mode,
            Threshold = threshold,
            ThresholdSq = threshold * threshold,
        };
    }

    internal void ResolveDerivedTriggers(InputActionMap map)
    {
        for (int i = 0; i < _derivedTriggerCount; i++)
        {
            if (_derivedTriggers[i].SourceActionIndex >= 0)
            {
                continue;
            }

            if (_derivedTriggers[i].SourceActionName.IsValid && map.TryGetActionIndex(_derivedTriggers[i].SourceActionName, out int idx))
            {
                _derivedTriggers[i].SourceActionIndex = idx;
            }
        }
    }

    public void AddBinding(InputBinding binding)
    {
        if (_bindingCount >= MaxBindings)
        {
            return;
        }
        _bindings[_bindingCount++] = binding;
    }

    public void AddBinding(string path, ModifierKeys modifiers = ModifierKeys.None, float scale = 1f)
    {
        AddBinding(InputBinding.FromPath(path, modifiers, scale));
    }

    public void AddCompositeBinding(string compositeName, string upPath, string downPath, string leftPath, string rightPath)
    {
        StringHandle composite = compositeName;

        var up = InputBinding.FromPath(upPath);
        up.CompositeGroup = composite;
        up.CompositeIndex = 0;
        AddBinding(up);

        var down = InputBinding.FromPath(downPath);
        down.CompositeGroup = composite;
        down.CompositeIndex = 1;
        AddBinding(down);

        var left = InputBinding.FromPath(leftPath);
        left.CompositeGroup = composite;
        left.CompositeIndex = 2;
        AddBinding(left);

        var right = InputBinding.FromPath(rightPath);
        right.CompositeGroup = composite;
        right.CompositeIndex = 3;
        AddBinding(right);
    }

    public void ClearBindings()
    {
        _bindingCount = 0;
    }

    /// <summary>
    /// Called by InputActionMap each frame. Returns true if state changed.
    /// </summary>
    internal bool Update(IInputDevice device, double time, StringHandle mapName, InputActionMap map, out ActionContext context)
    {
        _previousValue = _currentValue;
        ActionPhase previousPhase = _phase;

        if (!Enabled)
        {
            _phase = ActionPhase.Disabled;
            _currentValue = InputValue.Zero;
            context = default;
            return false;
        }

        // Reset composite values
        _compositeValues[0] = 0f;
        _compositeValues[1] = 0f;
        _compositeValues[2] = 0f;
        _compositeValues[3] = 0f;

        float maxValue = 0f;
        float axisX = 0f;
        float axisY = 0f;
        bool hasComposite = false;
        bool hasDirectVector = false;

        // Read all bindings
        for (int i = 0; i < _bindingCount; i++)
        {
            ref InputBinding binding = ref _bindings[i];

            // Check modifiers
            if (!device.CheckModifiers(binding.Modifiers))
            {
                continue;
            }

            var path = binding.Path;
            InputValue bindingValue = device.ReadBinding(ref path);
            float scaledValue = bindingValue.Value * binding.Scale;

            if (binding.CompositeGroup.IsValid)
            {
                // Composite binding (WASD-style)
                hasComposite = true;
                if (bindingValue.IsPressed || Math.Abs(scaledValue) > DeadZone)
                {
                    _compositeValues[binding.CompositeIndex] = Math.Max(_compositeValues[binding.CompositeIndex],
                        bindingValue.IsPressed ? 1f : Math.Abs(scaledValue));
                }
            }
            else
            {
                // Direct binding
                switch (Type)
                {
                    case ActionType.Button:
                        if (bindingValue.IsPressed)
                        {
                            maxValue = 1f;
                        }
                        break;
                    case ActionType.Value:
                        if (Math.Abs(scaledValue) > Math.Abs(maxValue))
                        {
                            maxValue = scaledValue;
                        }
                        break;
                    case ActionType.Vector2:
                        // For direct Vector2 bindings (like mouse position)
                        // Always accept the value - (0,0) is a valid screen position
                        axisX = bindingValue.Vector2.X;
                        axisY = bindingValue.Vector2.Y;
                        hasDirectVector = true;
                        break;
                }
            }
        }

        // Compute final value
        if (hasComposite && !hasDirectVector)
        {
            // WASD composite: up(0) - down(1), right(3) - left(2)
            axisY = _compositeValues[0] - _compositeValues[1];
            axisX = _compositeValues[3] - _compositeValues[2];
            _currentValue = InputValue.FromVector2(axisX, axisY);
        }
        else if (hasDirectVector)
        {
            _currentValue = InputValue.FromVector2(axisX, axisY);
        }
        else
        {
            _currentValue = Type switch
            {
                ActionType.Button => InputValue.FromButton(maxValue > 0.5f),
                ActionType.Value => InputValue.FromAxis(maxValue),
                ActionType.Vector2 => InputValue.FromVector2(axisX, axisY),
                _ => InputValue.Zero
            };
        }

        if (Type == ActionType.Button && _derivedTriggerCount > 0)
        {
            bool derivedPressed = false;
            for (int i = 0; i < _derivedTriggerCount; i++)
            {
                ref DerivedTrigger trigger = ref _derivedTriggers[i];
                if (trigger.SourceActionIndex < 0)
                {
                    continue;
                }

                var sourceAction = map.GetActionByIndex(trigger.SourceActionIndex);
                if (sourceAction == null)
                {
                    continue;
                }

                InputValue sourceValue = sourceAction.Value;
                bool triggerActive = trigger.Kind switch
                {
                    DerivedTriggerKind.Vector2MagnitudeAtLeast => sourceValue.MagnitudeSquared >= trigger.ThresholdSq,
                    DerivedTriggerKind.AxisMagnitudeAtLeast => Math.Abs(sourceValue.Value) >= trigger.Threshold,
                    _ => false
                };

                // Only Hold is supported right now.
                if (trigger.Mode == DerivedTriggerMode.Hold && triggerActive)
                {
                    derivedPressed = true;
                    break;
                }
            }

            if (derivedPressed && !_currentValue.IsPressed)
            {
                _currentValue = InputValue.FromButton(true);
            }
        }

        // Apply dead zone for analog inputs (but not for direct vectors like mouse position)
        if (Type != ActionType.Button && !hasDirectVector && _currentValue.MagnitudeSquared < DeadZone * DeadZone)
        {
            _currentValue = InputValue.Zero;
        }

        // Update phase
        bool wasActive = previousPhase == ActionPhase.Started || previousPhase == ActionPhase.Performed;
        bool isActive = Type == ActionType.Button
            ? _currentValue.IsPressed
            : _currentValue.MagnitudeSquared > DeadZone * DeadZone || hasDirectVector && _currentValue.MagnitudeSquared > 0f;

        if (!wasActive && isActive)
        {
            _phase = ActionPhase.Started;
            _startTime = time;
        }
        else if (wasActive && isActive)
        {
            _phase = ActionPhase.Performed;
        }
        else if (wasActive && !isActive)
        {
            _phase = ActionPhase.Canceled;
        }
        else
        {
            _phase = ActionPhase.Waiting;
        }

        bool stateChanged = _phase != previousPhase || _currentValue != _previousValue;

        context = new ActionContext(
            Name,
            mapName,
            _phase,
            Type,
            _currentValue,
            _previousValue,
            time,
            time - _startTime
        );

        return stateChanged;
    }

    public void Reset()
    {
        _currentValue = InputValue.Zero;
        _previousValue = InputValue.Zero;
        _phase = ActionPhase.Waiting;
        _startTime = 0;
    }

    /// <summary>
    /// Number of bindings registered for this action.
    /// </summary>
    public int BindingCount => _bindingCount;

    /// <summary>
    /// Get a binding by index (for external device implementations).
    /// </summary>
    public InputBinding? GetBindingByIndex(int index)
    {
        if (index < 0 || index >= _bindingCount)
            return null;
        return _bindings[index];
    }

    /// <summary>
    /// Update action state from an externally computed value.
    /// Used by input managers that don't use RaylibInputDevice.
    /// </summary>
    public bool UpdateFromValue(InputValue value, double time, ActionCallbackHandler? handler, StringHandle mapName = default)
    {
        _previousValue = _currentValue;
        ActionPhase previousPhase = _phase;

        if (!Enabled)
        {
            _phase = ActionPhase.Disabled;
            _currentValue = InputValue.Zero;
            return false;
        }

        _currentValue = value;

        // Apply dead zone for analog inputs
        if (Type != ActionType.Button && _currentValue.MagnitudeSquared < DeadZone * DeadZone)
        {
            _currentValue = InputValue.Zero;
        }

        // Update phase
        bool wasActive = previousPhase == ActionPhase.Started || previousPhase == ActionPhase.Performed;
        bool isActive = Type == ActionType.Button
            ? _currentValue.IsPressed
            : _currentValue.MagnitudeSquared > DeadZone * DeadZone;

        if (!wasActive && isActive)
        {
            _phase = ActionPhase.Started;
            _startTime = time;
        }
        else if (wasActive && isActive)
        {
            _phase = ActionPhase.Performed;
        }
        else if (wasActive && !isActive)
        {
            _phase = ActionPhase.Canceled;
        }
        else
        {
            _phase = ActionPhase.Waiting;
        }

        bool stateChanged = _phase != previousPhase || _currentValue != _previousValue;

        // Fire callback for discrete events
        if (handler != null && (_phase == ActionPhase.Started || _phase == ActionPhase.Canceled))
        {
            var context = new ActionContext(
                Name,
                mapName,
                _phase,
                Type,
                _currentValue,
                _previousValue,
                time,
                time - _startTime
            );
            handler(in context);
        }

        return stateChanged;
    }

    /// <summary>
    /// Initialize the action by sampling current device state.
    /// Used when enabling an action map to avoid a "zero frame".
    /// </summary>
    public void InitializeFromDevice(IInputDevice device)
    {
        _phase = ActionPhase.Waiting;
        _startTime = 0;

        // Sample current value from device
        float axisX = 0f;
        float axisY = 0f;
        float maxValue = 0f;
        bool hasDirectVector = false;

        for (int i = 0; i < _bindingCount; i++)
        {
            ref InputBinding binding = ref _bindings[i];
            var path = binding.Path;
            InputValue bindingValue = device.ReadBinding(ref path);

            if (!binding.CompositeGroup.IsValid)
            {
                switch (Type)
                {
                    case ActionType.Button:
                        if (bindingValue.IsPressed)
                        {
                            maxValue = 1f;
                        }
                        break;
                    case ActionType.Value:
                        float scaledValue = bindingValue.Value * binding.Scale;
                        if (Math.Abs(scaledValue) > Math.Abs(maxValue))
                        {
                            maxValue = scaledValue;
                        }
                        break;
                    case ActionType.Vector2:
                        axisX = bindingValue.Vector2.X;
                        axisY = bindingValue.Vector2.Y;
                        hasDirectVector = true;
                        break;
                }
            }
        }

        // Set current value from sampled device state
        if (hasDirectVector)
        {
            _currentValue = InputValue.FromVector2(axisX, axisY);
        }
        else
        {
            _currentValue = Type switch
            {
                ActionType.Button => InputValue.FromButton(maxValue > 0.5f),
                ActionType.Value => InputValue.FromAxis(maxValue),
                ActionType.Vector2 => InputValue.FromVector2(axisX, axisY),
                _ => InputValue.Zero
            };
        }

        _previousValue = _currentValue;
    }
}
