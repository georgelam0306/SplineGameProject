using System;

namespace GameDocDatabase;

/// <summary>
/// Deterministic string handle for game data lookups.
/// IDs are assigned at build time based on sorted name order.
/// </summary>
public readonly struct GameDataId : IEquatable<GameDataId>
{
    public readonly int Value;

    public GameDataId(int value) => Value = value;

    public static GameDataId Invalid => new GameDataId(-1);
    public bool IsValid => Value >= 0;

    public bool Equals(GameDataId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is GameDataId other && Equals(other);
    public override int GetHashCode() => Value;

    public static bool operator ==(GameDataId left, GameDataId right) => left.Value == right.Value;
    public static bool operator !=(GameDataId left, GameDataId right) => left.Value != right.Value;

    public override string ToString() => $"GameDataId({Value})";
}
