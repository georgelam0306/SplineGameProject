// SPDX-License-Identifier: MIT
#nullable enable
using System;

namespace Property.Generator
{
    [Flags]
    internal enum PropertyFlags : ushort
    {
        None = 0,
        ReadOnly = 1 << 0,
        Hidden = 1 << 1,
        Animated = 1 << 2
    }
}
