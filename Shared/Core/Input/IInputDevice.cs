namespace Core.Input;

/// <summary>
/// Minimal device interface used by the action system to read bindings without
/// coupling to a specific backend (Raylib, Derp/Silk, etc.).
/// </summary>
public interface IInputDevice
{
    /// <summary>
    /// Returns true if all required modifier keys are currently held.
    /// </summary>
    bool CheckModifiers(ModifierKeys required);

    /// <summary>
    /// Reads the current value for the given pre-parsed binding path.
    /// </summary>
    InputValue ReadBinding(ref BindingPath path);
}
