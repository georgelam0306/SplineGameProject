namespace Derp.Doc.Model;

/// <summary>
/// Generates fractional index strings for ordering blocks.
/// Strings are lexicographically sortable.
/// </summary>
public static class FractionalIndex
{
    /// <summary>
    /// Returns an initial order key.
    /// </summary>
    public static string Initial() => "a0";

    /// <summary>
    /// Returns an order key that sorts after the given key.
    /// </summary>
    public static string After(string key)
    {
        if (string.IsNullOrEmpty(key)) return "a0";

        // Try incrementing the last character
        char last = key[^1];
        if (last < 'z')
        {
            return key[..^1] + (char)(last + 1);
        }

        // Append a new character
        return key + "1";
    }

    /// <summary>
    /// Returns an order key that sorts between the two given keys.
    /// </summary>
    public static string Between(string before, string after)
    {
        if (string.IsNullOrEmpty(before)) return Before(after);
        if (string.IsNullOrEmpty(after)) return After(before);

        // Find the first position where they differ
        int minLen = Math.Min(before.Length, after.Length);

        for (int i = 0; i < minLen; i++)
        {
            char a = before[i];
            char b = after[i];
            if (a < b)
            {
                if (b - a > 1)
                {
                    // There's room for a character between them
                    return before[..i] + (char)(a + (b - a) / 2);
                }
                // Append a midpoint character
                return before[..(i + 1)] + "V";
            }
        }

        // 'before' is a prefix of 'after' (or they are equal)
        // Append a midpoint character to 'before'
        return before + "V";
    }

    /// <summary>
    /// Returns an order key that sorts before the given key.
    /// </summary>
    public static string Before(string key)
    {
        if (string.IsNullOrEmpty(key)) return "a0";

        char first = key[0];
        if (first > 'a')
        {
            return ((char)(first - 1)) + key[1..];
        }

        // Prepend 'a' and append '0' midpoint
        return "a" + key + "0";
    }
}
