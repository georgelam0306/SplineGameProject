// SPDX-License-Identifier: MIT
#nullable enable
using System.Text;

namespace Property.Generator
{
    internal static class PropertyIdHelper
    {
        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        public static ulong Compute(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            ulong hash = FnvOffset;
            for (int index = 0; index < bytes.Length; index++)
            {
                hash ^= bytes[index];
                hash *= FnvPrime;
            }
            return hash;
        }
    }
}
