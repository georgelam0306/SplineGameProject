using System;

namespace DerpLib.ImGui.Widgets;

[Flags]
public enum ImDropdownFlags
{
    None = 0,
    NoBackground = 1 << 0,
    NoBorder = 1 << 1,
}

