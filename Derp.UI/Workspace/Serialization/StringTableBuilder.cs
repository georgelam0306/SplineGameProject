using System;
using System.Collections.Generic;

namespace Derp.UI;

internal sealed class StringTableBuilder
{
    private readonly Dictionary<string, uint> _indexByString = new(StringComparer.Ordinal);
    private readonly List<string> _strings = new(capacity: 256);

    public StringTableBuilder()
    {
        _indexByString.Add(string.Empty, 0);
        _strings.Add(string.Empty);
    }

    public IReadOnlyList<string> Strings => _strings;

    public uint GetOrAdd(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        if (_indexByString.TryGetValue(value, out uint index))
        {
            return index;
        }

        index = (uint)_strings.Count;
        _strings.Add(value);
        _indexByString.Add(value, index);
        return index;
    }
}

