using System;
using System.Runtime.CompilerServices;

namespace DerpLib.Ecs.Blobs;

/// <summary>
/// Opaque handle for immutable blob data.
/// </summary>
public readonly struct BlobHandle : IEquatable<BlobHandle>
{
    public static readonly BlobHandle Invalid = default;

    public readonly ulong Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BlobHandle(ulong value)
    {
        Value = value;
    }

    public bool IsValid
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Value != 0UL;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(BlobHandle other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is BlobHandle other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(BlobHandle left, BlobHandle right) => left.Value == right.Value;
    public static bool operator !=(BlobHandle left, BlobHandle right) => left.Value != right.Value;
}

