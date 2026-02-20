using System;
using System.Runtime.CompilerServices;

namespace DerpLib.Ecs.Editor;

public readonly struct EcsPropertyId : IEquatable<EcsPropertyId>
{
    public static readonly EcsPropertyId Invalid = default;

    public readonly ulong Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EcsPropertyId(ulong value)
    {
        Value = value;
    }

    public bool IsValid
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Value != 0UL;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(EcsPropertyId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is EcsPropertyId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(EcsPropertyId left, EcsPropertyId right) => left.Value == right.Value;
    public static bool operator !=(EcsPropertyId left, EcsPropertyId right) => left.Value != right.Value;
}

