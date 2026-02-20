using System;

namespace Core;

/// <summary>
/// Lightweight handle to a spline payload value stored in the runtime string registry.
/// </summary>
public readonly struct SplineHandle : IEquatable<SplineHandle>
{
    internal readonly uint Id;

    public static SplineHandle Invalid => new(0);

    internal SplineHandle(uint id)
    {
        Id = id;
    }

    public bool IsValid => Id != 0;

    public static implicit operator SplineHandle(string splineValue)
    {
        if (string.IsNullOrEmpty(splineValue))
        {
            return Invalid;
        }

        StringHandle handle = StringRegistry.Instance.Register(splineValue);
        return new SplineHandle(handle.Id);
    }

    public static implicit operator string(SplineHandle handle)
    {
        if (!handle.IsValid)
        {
            return string.Empty;
        }

        return StringRegistry.Instance.GetString(new StringHandle(handle.Id));
    }

    public bool Equals(SplineHandle other)
    {
        return Id == other.Id;
    }

    public override bool Equals(object? obj)
    {
        return obj is SplineHandle other && Equals(other);
    }

    public override int GetHashCode()
    {
        return (int)Id;
    }

    public override string ToString()
    {
        return this;
    }

    public static bool operator ==(SplineHandle left, SplineHandle right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(SplineHandle left, SplineHandle right)
    {
        return !left.Equals(right);
    }
}

