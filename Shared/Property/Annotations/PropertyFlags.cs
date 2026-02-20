// SPDX-License-Identifier: MIT
#nullable enable
using System;

namespace Property
{
    [Flags]
    public enum PropertyFlags : ushort
    {
        None = 0,
        ReadOnly = 1 << 0,
        Hidden = 1 << 1,
        Animated = 1 << 2
    }
}
