// SPDX-License-Identifier: MIT
#nullable enable
using Microsoft.CodeAnalysis;

namespace DerpLib.DI.Generator.Model
{
    /// <summary>
    /// Represents a composition root declared via .Root&lt;T&gt;("name").
    /// </summary>
    internal sealed class RootModel
    {
        /// <summary>The type exposed as a root.</summary>
        public ITypeSymbol Type { get; }

        /// <summary>The fully qualified type name.</summary>
        public string FullTypeName { get; }

        /// <summary>The property name for this root.</summary>
        public string Name { get; }

        public RootModel(ITypeSymbol type, string name)
        {
            Type = type;
            FullTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            Name = name;
        }
    }
}
