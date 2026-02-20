using System;
using System.Runtime.CompilerServices;

namespace DerpLib.Ecs;

/// <summary>
/// Stable entity identifier (single packed value). Intended for deterministic simulation (rollback)
/// and view-world identity.
/// </summary>
public readonly struct EntityHandle : IEquatable<EntityHandle>
{
    // Bit layout (v1 draft):
    // [63..56] Flags (8)
    // [55..40] Generation (16)
    // [39..16] RawId (24)
    // [15..0]  KindId (16)
    //
    // NOTE: This layout assumes simulation has no runtime archetype transitions. If we later allow
    // transitions for a domain, KindId remains stable identity (schema/entity kind) and current
    // storage archetype lives in indirection (e.g. EntityLocation).

    public const int KindIdBits = 16;
    public const int RawIdBits = 24;
    public const int GenerationBits = 16;
    public const int FlagsBits = 8;

    public const ulong KindIdMask = (1UL << KindIdBits) - 1UL;
    public const ulong RawIdMask = (1UL << RawIdBits) - 1UL;
    public const ulong GenerationMask = (1UL << GenerationBits) - 1UL;
    public const ulong FlagsMask = (1UL << FlagsBits) - 1UL;

    public const int KindIdShift = 0;
    public const int RawIdShift = KindIdShift + KindIdBits;
    public const int GenerationShift = RawIdShift + RawIdBits;
    public const int FlagsShift = GenerationShift + GenerationBits;

    public static readonly EntityHandle Invalid = default;

    public readonly ulong Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityHandle(ulong value)
    {
        Value = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityHandle(ushort kindId, uint rawId, ushort generation, byte flags = 0)
    {
        if (kindId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(kindId), "kindId must be non-zero.");
        }
        if (rawId > RawIdMask)
        {
            throw new ArgumentOutOfRangeException(nameof(rawId), $"rawId must be <= {RawIdMask}.");
        }
        if (generation == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generation), "generation must be non-zero.");
        }

        Value =
            ((ulong)flags << FlagsShift) |
            ((ulong)generation << GenerationShift) |
            ((ulong)rawId << RawIdShift) |
            kindId;
    }

    public bool IsValid
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Value != 0UL;
    }

    public ushort KindId
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (ushort)((Value >> KindIdShift) & KindIdMask);
    }

    public uint RawId
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (uint)((Value >> RawIdShift) & RawIdMask);
    }

    public ushort Generation
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (ushort)((Value >> GenerationShift) & GenerationMask);
    }

    public byte Flags
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (byte)((Value >> FlagsShift) & FlagsMask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(EntityHandle other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is EntityHandle other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(EntityHandle left, EntityHandle right) => left.Value == right.Value;
    public static bool operator !=(EntityHandle left, EntityHandle right) => left.Value != right.Value;

    public override string ToString()
    {
        return IsValid
            ? $"EntityHandle(kind={KindId}, raw={RawId}, gen={Generation}, flags={Flags})"
            : "EntityHandle(Invalid)";
    }
}
