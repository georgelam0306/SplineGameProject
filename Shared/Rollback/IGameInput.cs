namespace DerpTech.Rollback;

/// <summary>
/// Constraint interface for game input types used in rollback networking.
/// Input types must be unmanaged structs for zero-allocation serialization.
/// </summary>
/// <typeparam name="TSelf">The implementing struct type (CRTP pattern).</typeparam>
public interface IGameInput<TSelf> where TSelf : unmanaged, IGameInput<TSelf>, IEquatable<TSelf>
{
    /// <summary>Returns the default/empty input state.</summary>
    static abstract TSelf Empty { get; }

    /// <summary>Returns true if this input has no commands (all flags false).</summary>
    bool IsEmpty { get; }
}
