// SPDX-License-Identifier: MIT
#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace DerpLib.DI.Generator.Model
{
    /// <summary>
    /// Represents a binding declared via .Bind&lt;T&gt;().As(lifetime).To&lt;TImpl&gt;().
    /// </summary>
    internal sealed class BindingModel
    {
        /// <summary>The service type being bound.</summary>
        public ITypeSymbol ServiceType { get; }

        /// <summary>The fully qualified service type name.</summary>
        public string ServiceTypeName { get; }

        /// <summary>Optional tag for tagged bindings.</summary>
        public string? Tag { get; }

        /// <summary>The implementation type (null if using factory).</summary>
        public ITypeSymbol? ImplementationType { get; }

        /// <summary>The fully qualified implementation type name (null if using factory).</summary>
        public string? ImplementationTypeName { get; }

        /// <summary>The factory lambda expression (null if using type binding).</summary>
        public LambdaExpressionSyntax? Factory { get; }

        /// <summary>
        /// Normalized factory expression text (used when Factory is not null).
        /// For expression lambdas this is the expression body; for block lambdas this is the return expression.
        /// </summary>
        public string? FactoryExpressionText { get; set; }

        /// <summary>True if the factory lambda was a block body (requires prelude statements).</summary>
        public bool FactoryIsBlock { get; set; }

        /// <summary>Dependencies injected via ctx.Inject*() inside a factory block.</summary>
        public System.Collections.Generic.List<FactoryInjectionModel> FactoryInjections { get; } = new();

        /// <summary>The lifetime of this binding.</summary>
        public BindingLifetime Lifetime { get; }

        /// <summary>Dependencies extracted from InjectFromParent calls in the factory.</summary>
        public System.Collections.Generic.List<ParentInjection> ParentInjections { get; } = new();

        public BindingModel(
            ITypeSymbol serviceType,
            string? tag,
            ITypeSymbol? implementationType,
            LambdaExpressionSyntax? factory,
            BindingLifetime lifetime)
        {
            ServiceType = serviceType;
            ServiceTypeName = serviceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            Tag = tag;
            ImplementationType = implementationType;
            ImplementationTypeName = implementationType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            Factory = factory;
            Lifetime = lifetime;
        }

        /// <summary>Gets the unique key for this binding (type + tag).</summary>
        public string GetKey() => Tag is null ? ServiceTypeName : $"{ServiceTypeName}:{Tag}";

        /// <summary>Gets the resolver method name for this binding.</summary>
        public string GetResolverMethodName()
        {
            var typeName = GetSafeTypeName(ServiceType);
            return Tag is null ? $"Resolve_{typeName}" : $"Resolve_{typeName}_{Tag}";
        }

        /// <summary>Gets the singleton field name for this binding.</summary>
        public string GetSingletonFieldName()
        {
            var typeName = GetSafeTypeName(ServiceType);
            return Tag is null ? $"_singleton_{typeName}" : $"_singleton_{typeName}_{Tag}";
        }

        private static string GetSafeTypeName(ITypeSymbol type)
        {
            // Use the simple name, handling generics
            if (type is INamedTypeSymbol named && named.IsGenericType)
            {
                var baseName = named.Name;
                var args = string.Join("_", named.TypeArguments.Select(t => GetSafeTypeName(t)));
                return $"{baseName}_{args}";
            }
            return type.Name;
        }
    }

    internal enum BindingLifetime
    {
        Singleton,
        Transient
    }

    /// <summary>
    /// Represents a dependency injection from parent scope.
    /// </summary>
    internal sealed class ParentInjection
    {
        public ITypeSymbol Type { get; }
        public string FullTypeName { get; }
        public string VariableName { get; }

        public ParentInjection(ITypeSymbol type, string variableName)
        {
            Type = type;
            FullTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            VariableName = variableName;
        }
    }

    internal enum FactoryInjectionKind
    {
        Inject,
        InjectFromParent
    }

    internal sealed class FactoryInjectionModel
    {
        public FactoryInjectionKind Kind { get; }
        public ITypeSymbol Type { get; }
        public string FullTypeName { get; }
        public string? Tag { get; }
        public string VariableName { get; }
        public Location Location { get; }

        public FactoryInjectionModel(
            FactoryInjectionKind kind,
            ITypeSymbol type,
            string? tag,
            string variableName,
            Location location)
        {
            Kind = kind;
            Type = type;
            FullTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            Tag = tag;
            VariableName = variableName;
            Location = location;
        }
    }
}
