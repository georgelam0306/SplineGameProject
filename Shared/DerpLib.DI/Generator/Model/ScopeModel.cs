// SPDX-License-Identifier: MIT
#nullable enable
using Microsoft.CodeAnalysis;

namespace DerpLib.DI.Generator.Model
{
    /// <summary>
    /// Represents a child scope declaration via .Scope&lt;TChild&gt;().
    /// </summary>
    internal sealed class ScopeModel
    {
        /// <summary>The child composition type.</summary>
        public INamedTypeSymbol ChildType { get; }

        /// <summary>The fully qualified child type name.</summary>
        public string FullTypeName { get; }

        /// <summary>Link to the parsed child composition (set during linking phase).</summary>
        public CompositionModel? LinkedChild { get; set; }

        public ScopeModel(INamedTypeSymbol childType)
        {
            ChildType = childType;
            FullTypeName = childType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        /// <summary>Gets the factory type name (e.g., "GameComposition.Factory").</summary>
        public string GetFactoryTypeName() => $"{FullTypeName}.Factory";

        /// <summary>Gets the resolver method name for this scope's factory.</summary>
        public string GetFactoryResolverMethodName()
        {
            var safeName = ChildType.Name;
            return $"Resolve_{safeName}_Factory";
        }

        /// <summary>Gets the singleton field name for this scope's factory.</summary>
        public string GetFactorySingletonFieldName()
        {
            var safeName = ChildType.Name;
            return $"_singleton_{safeName}_Factory";
        }
    }
}
