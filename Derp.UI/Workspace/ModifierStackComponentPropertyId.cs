namespace Derp.UI;

internal static class ModifierStackComponentPropertyId
{
    private const ulong PropertyFnvOffset = 14695981039346656037UL;
    private const ulong PropertyFnvPrime = 1099511628211UL;
    private const string ModifierStackComponentTypeName = "global::Derp.UI.ModifierStackComponent";

    public static ulong ArrayElement(string fieldName, int index)
    {
        ulong hash = PropertyFnvOffset;
        AppendAscii(ref hash, ModifierStackComponentTypeName);
        AppendByte(ref hash, (byte)'.');
        AppendAscii(ref hash, fieldName);
        AppendByte(ref hash, (byte)'[');
        AppendSmallNonNegativeInt(ref hash, index);
        AppendByte(ref hash, (byte)']');
        return hash;
    }

    private static void AppendAscii(ref ulong hash, string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            hash ^= (byte)text[i];
            hash *= PropertyFnvPrime;
        }
    }

    private static void AppendByte(ref ulong hash, byte value)
    {
        hash ^= value;
        hash *= PropertyFnvPrime;
    }

    private static void AppendSmallNonNegativeInt(ref ulong hash, int value)
    {
        if (value < 0)
        {
            value = 0;
        }

        if (value < 10)
        {
            AppendByte(ref hash, (byte)('0' + value));
            return;
        }

        if (value < 100)
        {
            int tens = value / 10;
            int ones = value - (tens * 10);
            AppendByte(ref hash, (byte)('0' + tens));
            AppendByte(ref hash, (byte)('0' + ones));
            return;
        }

        int hundreds = value / 100;
        int rem = value - (hundreds * 100);
        int tens2 = rem / 10;
        int ones2 = rem - (tens2 * 10);
        AppendByte(ref hash, (byte)('0' + hundreds));
        AppendByte(ref hash, (byte)('0' + tens2));
        AppendByte(ref hash, (byte)('0' + ones2));
    }
}

