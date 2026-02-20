// SPDX-License-Identifier: MIT
#nullable enable
using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;

namespace DerpLib.DI.Generator.Model
{
    /// <summary>
    /// Represents a collection binding declared via .BindAll&lt;T&gt;().As(lifetime).Add&lt;T1&gt;().Add&lt;T2&gt;()...
    /// Resolves as T[] when injected into constructors.
    /// </summary>
    internal sealed class CollectionBindingModel
    {
        /// <summary>The interface/service type for the collection (e.g. IEcsSystem&lt;SimEcsWorld&gt;).</summary>
        public ITypeSymbol ServiceType { get; }

        /// <summary>The fully qualified service type name.</summary>
        public string ServiceTypeName { get; }

        /// <summary>The lifetime for all items in the collection.</summary>
        public BindingLifetime Lifetime { get; }

        /// <summary>Ordered list of implementation types added via .Add&lt;T&gt;().</summary>
        public List<CollectionItem> Items { get; } = new();

        public CollectionBindingModel(ITypeSymbol serviceType, BindingLifetime lifetime)
        {
            ServiceType = serviceType;
            ServiceTypeName = serviceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            Lifetime = lifetime;
        }

        /// <summary>Gets the resolver method name for the array.</summary>
        public string GetArrayResolverMethodName()
        {
            var typeName = GetSafeTypeName(ServiceType);
            return $"Resolve_{typeName}_All";
        }

        /// <summary>Gets the resolver method name for an individual item by index.</summary>
        public string GetItemResolverMethodName(int index)
        {
            var typeName = GetSafeTypeName(ServiceType);
            return $"Resolve_{typeName}_{index}";
        }

        /// <summary>Gets the singleton field name for an individual item by index.</summary>
        public string GetItemSingletonFieldName(int index)
        {
            var typeName = GetSafeTypeName(ServiceType);
            return $"_singleton_{typeName}_{index}";
        }

        private static string GetSafeTypeName(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol named && named.IsGenericType)
            {
                var baseName = named.Name;
                var args = string.Join("_", named.TypeArguments.Select(t => GetSafeTypeName(t)));
                return $"{baseName}_{args}";
            }
            return type.Name;
        }
    }

    /// <summary>
    /// A single implementation in a collection binding.
    /// </summary>
    internal sealed class CollectionItem
    {
        public ITypeSymbol ImplementationType { get; }
        public string ImplementationTypeName { get; }

        public CollectionItem(ITypeSymbol implementationType)
        {
            ImplementationType = implementationType;
            ImplementationTypeName = implementationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }
    }
}
