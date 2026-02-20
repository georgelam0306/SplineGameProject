namespace Core.Input;

/// <summary>
/// A single input binding for an action.
/// </summary>
public struct InputBinding
{
    public BindingPath Path;
    public ModifierKeys Modifiers;
    public StringHandle CompositeGroup;  // For WASD composites, e.g., "move"
    public int CompositeIndex;           // 0=up, 1=down, 2=left, 3=right
    public float Scale;                  // Multiplier for the input value

    public InputBinding(BindingPath path, ModifierKeys modifiers = ModifierKeys.None, float scale = 1f)
    {
        Path = path;
        Modifiers = modifiers;
        CompositeGroup = StringHandle.Invalid;
        CompositeIndex = 0;
        Scale = scale;
    }

    public static InputBinding FromPath(string path, ModifierKeys modifiers = ModifierKeys.None, float scale = 1f)
    {
        return new InputBinding(BindingPath.Parse(path), modifiers, scale);
    }
}
