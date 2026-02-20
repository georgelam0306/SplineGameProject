using System;
using System.Collections.Generic;

namespace Core;

/// <summary>
/// Lightweight handle to an interned string. Enables zero-allocation equality comparisons.
/// </summary>
public readonly struct StringHandle : IEquatable<StringHandle>
{
    internal readonly uint Id;

    public static StringHandle Invalid => new StringHandle(0);

    internal StringHandle(uint id)
    {
        Id = id;
    }

    public bool IsValid => Id != 0;

    public static implicit operator StringHandle(string str) =>
        str == null ? Invalid : StringRegistry.Instance.Register(str);

    public static implicit operator string(StringHandle handle) =>
        StringRegistry.Instance.GetString(handle);

    public StringHandle ToHandle() => this;

    public bool Equals(StringHandle other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is StringHandle other && Equals(other);
    public override int GetHashCode() => (int)Id;
    public override string ToString() => StringRegistry.Instance.GetString(this);

    public static bool operator ==(StringHandle left, StringHandle right) => left.Equals(right);
    public static bool operator !=(StringHandle left, StringHandle right) => !left.Equals(right);
}

/// <summary>
/// Thread-safe registry for interned strings.
/// </summary>
public sealed class StringRegistry
{
    private static readonly StringRegistry _instance = new StringRegistry();
    public static StringRegistry Instance => _instance;

    private readonly Dictionary<string, uint> _stringToId = new();
    private readonly Dictionary<uint, string> _idToString = new();

    private StringRegistry() { }

    public static uint ComputeStableId(string str)
    {
        // FNV-1a 32-bit over UTF-16 code units (explicit low/high bytes for endianness stability).
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        uint hash = offsetBasis;
        for (int i = 0; i < str.Length; i++)
        {
            char c = str[i];
            hash ^= (byte)c;
            hash *= prime;
            hash ^= (byte)(c >> 8);
            hash *= prime;
        }

        // 0 is reserved for Invalid.
        return hash == 0 ? 1u : hash;
    }

    public StringHandle Register(string str)
    {
        if (string.IsNullOrEmpty(str)) return StringHandle.Invalid;

        lock (_stringToId)
        {
            if (_stringToId.TryGetValue(str, out uint id))
            {
                return new StringHandle(id);
            }

            id = ComputeStableId(str);
            if (_idToString.TryGetValue(id, out var existing) && !string.Equals(existing, str, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"String ID collision: '{existing}' and '{str}' both map to {id:X8}.");
            }

            _stringToId[str] = id;
            _idToString[id] = str;
            return new StringHandle(id);
        }
    }

    public string GetString(StringHandle handle)
    {
        if (!handle.IsValid) return string.Empty;
        lock (_stringToId)
        {
            return _idToString.TryGetValue(handle.Id, out var str) ? str : string.Empty;
        }
    }

    /// <summary>
    /// Registers a string with a specific ID (used when loading from binary).
    /// </summary>
    public void RegisterWithId(uint id, string str)
    {
        if (id == 0 || string.IsNullOrEmpty(str)) return;

        lock (_stringToId)
        {
            uint expected = ComputeStableId(str);
            if (expected != id)
            {
                throw new InvalidOperationException($"String ID mismatch for '{str}': expected {expected:X8}, got {id:X8}.");
            }

            if (_idToString.TryGetValue(id, out var existing) && !string.Equals(existing, str, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"String ID collision: '{existing}' and '{str}' both map to {id:X8}.");
            }

            _stringToId[str] = id;
            _idToString[id] = str;
        }
    }

    /// <summary>
    /// Gets all registered entries for serialization.
    /// </summary>
    public IEnumerable<(uint Id, string Value)> GetAllEntries()
    {
        List<(uint Id, string Value)> entries;
        lock (_stringToId)
        {
            entries = new List<(uint Id, string Value)>(_idToString.Count);
            foreach (var kvp in _idToString)
            {
                entries.Add((kvp.Key, kvp.Value));
            }
        }

        entries.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        return entries;
    }
}
