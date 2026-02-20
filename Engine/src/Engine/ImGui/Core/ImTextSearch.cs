using System;

namespace DerpLib.ImGui.Core;

internal static class ImTextSearch
{
    public static int IndexOfOrdinalIgnoreCase(string haystack, ReadOnlySpan<char> needle)
    {
        if (needle.Length == 0)
        {
            return 0;
        }

        if (haystack.Length < needle.Length)
        {
            return -1;
        }

        int lastStart = haystack.Length - needle.Length;
        for (int startIndex = 0; startIndex <= lastStart; startIndex++)
        {
            if (EqualsAtOrdinalIgnoreCase(haystack, needle, startIndex))
            {
                return startIndex;
            }
        }

        return -1;
    }

    public static bool ContainsOrdinalIgnoreCase(string haystack, ReadOnlySpan<char> needle)
    {
        return IndexOfOrdinalIgnoreCase(haystack, needle) >= 0;
    }

    private static bool EqualsAtOrdinalIgnoreCase(string haystack, ReadOnlySpan<char> needle, int startIndex)
    {
        for (int i = 0; i < needle.Length; i++)
        {
            char a = haystack[startIndex + i];
            char b = needle[i];

            if (a == b)
            {
                continue;
            }

            if (ToUpperAsciiInvariant(a) != ToUpperAsciiInvariant(b))
            {
                return false;
            }
        }

        return true;
    }

    private static char ToUpperAsciiInvariant(char c)
    {
        if ((uint)(c - 'a') <= ('z' - 'a'))
        {
            return (char)(c - 32);
        }

        return c;
    }
}

