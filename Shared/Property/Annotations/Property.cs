// SPDX-License-Identifier: MIT
#nullable enable
using System;

namespace Property
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class PropertyAttribute : Attribute
    {
        public string Name { get; init; } = string.Empty;
        public string Group { get; init; } = string.Empty;
        public int Order { get; init; }
        public PropertyFlags Flags { get; init; } = PropertyFlags.None;
        public float Min { get; init; } = float.NaN;
        public float Max { get; init; } = float.NaN;
        public float Step { get; init; } = float.NaN;
        public bool ExpandSubfields { get; init; } = false;
        public PropertyKind Kind { get; init; } = PropertyKind.Auto;
    }
}
