using System.Collections.Generic;

namespace Core.Input;

/// <summary>
/// A collection of related input actions (a context).
/// Examples: "Gameplay", "Menu", "Editor"
/// </summary>
public sealed class InputActionMap
{
    private const int MaxActions = 32;

    public readonly StringHandle Name;

    private readonly InputAction[] _actions;
    private int _actionCount;
    private bool _enabled;

    // Action lookup by name (for convenience, not hot-path)
    private readonly Dictionary<StringHandle, int> _actionLookup;
    private bool _derivedTriggersResolved;

    public bool Enabled => _enabled;
    public int ActionCount => _actionCount;

    public InputActionMap(StringHandle name)
    {
        Name = name;
        _actions = new InputAction[MaxActions];
        _actionCount = 0;
        _enabled = false;
        _actionLookup = new Dictionary<StringHandle, int>(MaxActions);
        _derivedTriggersResolved = false;
    }

    public InputAction AddAction(StringHandle name, ActionType type)
    {
        if (_actionCount >= MaxActions)
        {
            return null!;
        }

        var action = new InputAction(name, type);
        _actionLookup[name] = _actionCount;
        _actions[_actionCount++] = action;
        return action;
    }

    public InputAction? GetAction(StringHandle name)
    {
        if (_actionLookup.TryGetValue(name, out int index))
        {
            return _actions[index];
        }
        return null;
    }

    public InputAction? this[string name] => GetAction(name);

    public InputAction? GetActionByIndex(int index)
    {
        if (index >= 0 && index < _actionCount)
        {
            return _actions[index];
        }
        return null;
    }

    internal bool TryGetActionIndex(StringHandle name, out int index)
    {
        return _actionLookup.TryGetValue(name, out index);
    }

    public void ResolveDerivedTriggers()
    {
        for (int i = 0; i < _actionCount; i++)
        {
            _actions[i].ResolveDerivedTriggers(this);
        }

        _derivedTriggersResolved = true;
    }

    public void Enable()
    {
        if (!_derivedTriggersResolved)
        {
            ResolveDerivedTriggers();
        }

        _enabled = true;
        for (int i = 0; i < _actionCount; i++)
        {
            _actions[i].Reset();
            _actions[i].Enabled = true;
        }
    }

    /// <summary>
    /// Enable the action map and initialize all actions from current device state.
    /// Use this when switching contexts to avoid a "zero frame".
    /// </summary>
    public void Enable(IInputDevice device)
    {
        if (!_derivedTriggersResolved)
        {
            ResolveDerivedTriggers();
        }

        _enabled = true;
        for (int i = 0; i < _actionCount; i++)
        {
            _actions[i].InitializeFromDevice(device);
            _actions[i].Enabled = true;
        }
    }

    public void Disable()
    {
        _enabled = false;
        for (int i = 0; i < _actionCount; i++)
        {
            _actions[i].Enabled = false;
            _actions[i].Reset();
        }
    }

    /// <summary>
    /// Update all actions. Called by InputManager.
    /// </summary>
    public void Update(IInputDevice device, double time, ActionCallbackHandler? handler)
    {
        if (!_enabled)
        {
            return;
        }

        for (int i = 0; i < _actionCount; i++)
        {
            if (_actions[i].Update(device, time, Name, this, out ActionContext context))
            {
                // Notify handler if state changed and action is in a relevant phase
                if (handler != null &&
                    (context.Phase == ActionPhase.Started ||
                     context.Phase == ActionPhase.Performed ||
                     context.Phase == ActionPhase.Canceled))
                {
                    handler(in context);
                }
            }
        }
    }
}
