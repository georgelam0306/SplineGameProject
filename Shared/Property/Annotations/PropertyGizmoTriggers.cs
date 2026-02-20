// SPDX-License-Identifier: MIT
#nullable enable
using System;

namespace Property;

[Flags]
public enum PropertyGizmoTriggers : byte
{
    None = 0,
    Hover = 1 << 0,
    Active = 1 << 1,
}

