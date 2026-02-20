// SPDX-License-Identifier: MIT
#nullable enable
using Microsoft.CodeAnalysis;

namespace DerpLib.DI.Generator.Model
{
    /// <summary>
    /// Represents a runtime argument declared via .Arg&lt;T&gt;("name").
    /// </summary>
    internal sealed class ArgModel
    {
        /// <summary>The type of the argument.</summary>
        public ITypeSymbol Type { get; }

        /// <summary>The fully qualified type name for code generation.</summary>
        public string FullTypeName { get; }

        /// <summary>The parameter/field name.</summary>
        public string Name { get; }

        public ArgModel(ITypeSymbol type, string name)
        {
            Type = type;
            FullTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            Name = name;
        }
    }
}
