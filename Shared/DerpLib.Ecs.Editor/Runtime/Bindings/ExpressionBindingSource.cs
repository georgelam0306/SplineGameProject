using Core;

namespace DerpLib.Ecs.Editor;

/// <summary>
/// A named input variable for an expression binding.
/// </summary>
public readonly struct ExpressionBindingSource
{
    /// <summary>Where to read the value from.</summary>
    public readonly ExpressionBindingPath Path;

    /// <summary>Variable alias used in the expression (e.g. "health" for @health).</summary>
    public readonly StringHandle Alias;

    public ExpressionBindingSource(ExpressionBindingPath path, StringHandle alias)
    {
        Path = path;
        Alias = alias;
    }
}
