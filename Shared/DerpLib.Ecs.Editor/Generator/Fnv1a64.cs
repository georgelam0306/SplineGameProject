using System.Text;

namespace DerpLib.Ecs.Editor.Generator;

internal static class Fnv1a64
{
    private const ulong Offset = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    public static ulong Compute(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        ulong hash = Offset;
        for (int i = 0; i < bytes.Length; i++)
        {
            hash ^= bytes[i];
            hash *= Prime;
        }
        return hash;
    }
}

