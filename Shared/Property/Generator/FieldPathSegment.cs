// SPDX-License-Identifier: MIT
#nullable enable
using Microsoft.CodeAnalysis;

namespace Property.Generator
{
    internal readonly struct FieldPathSegment
    {
        public string Name { get; }
        public ITypeSymbol Type { get; }
        public bool IsReadOnly { get; }
        public int FixedIndex { get; }

        public FieldPathSegment(string name, ITypeSymbol type, bool isReadOnly)
        {
            Name = name;
            Type = type;
            IsReadOnly = isReadOnly;
            FixedIndex = -1;
        }

        public FieldPathSegment(int fixedIndex, ITypeSymbol elementType)
        {
            Name = string.Empty;
            Type = elementType;
            IsReadOnly = false;
            FixedIndex = fixedIndex;
        }

        public bool IsArrayElement
        {
            get { return FixedIndex >= 0; }
        }
    }
}
