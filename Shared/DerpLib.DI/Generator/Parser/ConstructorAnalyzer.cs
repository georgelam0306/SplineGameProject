// SPDX-License-Identifier: MIT
#nullable enable
using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;

namespace DerpLib.DI.Generator.Parser
{
    /// <summary>
    /// Analyzes constructor dependencies for a type.
    /// </summary>
    internal static class ConstructorAnalyzer
    {
        /// <summary>
        /// Gets the constructor parameters for dependency injection.
        /// Prefers the constructor with the most parameters, or the single constructor.
        /// </summary>
        public static ConstructorInfo? GetConstructor(ITypeSymbol type)
        {
            if (type is not INamedTypeSymbol named)
                return null;

            var constructors = named.InstanceConstructors
                .Where(c => c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
                .OrderByDescending(c => c.Parameters.Length)
                .ToList();

            if (constructors.Count == 0)
                return null;

            var ctor = constructors[0];
            var parameters = new List<ParameterInfo>();

            foreach (var param in ctor.Parameters)
            {
                parameters.Add(new ParameterInfo(param.Type, param.Name));
            }

            return new ConstructorInfo(parameters);
        }

        /// <summary>
        /// Checks if a type has a parameterless constructor.
        /// </summary>
        public static bool HasParameterlessConstructor(ITypeSymbol type)
        {
            if (type is not INamedTypeSymbol named)
                return false;

            return named.InstanceConstructors
                .Any(c => c.Parameters.Length == 0 &&
                         c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal);
        }
    }

    internal sealed class ConstructorInfo
    {
        public List<ParameterInfo> Parameters { get; }
        public bool IsParameterless => Parameters.Count == 0;

        public ConstructorInfo(List<ParameterInfo> parameters)
        {
            Parameters = parameters;
        }
    }

    internal sealed class ParameterInfo
    {
        public ITypeSymbol Type { get; }
        public string FullTypeName { get; }
        public string Name { get; }

        public ParameterInfo(ITypeSymbol type, string name)
        {
            Type = type;
            FullTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            Name = name;
        }
    }
}
