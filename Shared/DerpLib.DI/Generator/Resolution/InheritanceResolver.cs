// SPDX-License-Identifier: MIT
#nullable enable
using DerpLib.DI.Generator.Model;
using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace DerpLib.DI.Generator.Resolution
{
    /// <summary>
    /// Resolves which bindings are inherited from parent scopes.
    /// </summary>
    internal static class InheritanceResolver
    {
        /// <summary>
        /// Validates that all dependencies can be resolved for a composition.
        /// </summary>
        public static void ValidateDependencies(CompositionModel composition, List<Diagnostic> diagnostics)
        {
            // Validate all bindings have their dependencies available
            foreach (var binding in composition.Bindings)
            {
                if (binding.ImplementationType is not null)
                {
                    ValidateConstructorDependencies(binding.ImplementationType, composition, diagnostics);
                }
                else if (binding.Factory is not null)
                {
                    ValidateFactoryDependencies(binding, composition, diagnostics);
                }
            }

            // Validate roots are resolvable
            foreach (var root in composition.Roots)
            {
                var resolved = composition.TryResolve(root.Type);
                if (resolved is null)
                {
                    diagnostics.Add(Diagnostic.Create(
                        Diagnostics.MissingBinding,
                        composition.AttributeLocation,
                        root.FullTypeName,
                        composition.ClassName));
                }
            }
        }

        private static void ValidateConstructorDependencies(
            ITypeSymbol implType,
            CompositionModel composition,
            List<Diagnostic> diagnostics)
        {
            var ctorInfo = Parser.ConstructorAnalyzer.GetConstructor(implType);
            if (ctorInfo is null || ctorInfo.IsParameterless)
                return;

            foreach (var param in ctorInfo.Parameters)
            {
                var resolved = composition.TryResolve(param.Type);
                if (resolved is null)
                {
                    // Check if it's a scope factory or a collection array
                    if (!IsFactoryType(param.Type, composition) &&
                        !IsCollectionArrayType(param.Type, composition))
                    {
                        diagnostics.Add(Diagnostic.Create(
                            Diagnostics.MissingBinding,
                            composition.AttributeLocation,
                            param.FullTypeName,
                            composition.ClassName));
                    }
                }
            }
        }

        private static void ValidateFactoryDependencies(
            BindingModel binding,
            CompositionModel composition,
            List<Diagnostic> diagnostics)
        {
            foreach (var injection in binding.FactoryInjections)
            {
                var targetScope = injection.Kind == FactoryInjectionKind.InjectFromParent
                    ? composition.Parent
                    : composition;

                if (targetScope is null)
                {
                    diagnostics.Add(Diagnostic.Create(
                        Diagnostics.MissingBinding,
                        injection.Location,
                        injection.FullTypeName,
                        composition.ClassName));
                    continue;
                }

                var resolved = targetScope.TryResolve(injection.Type, injection.Tag);
                if (resolved is null)
                {
                    diagnostics.Add(Diagnostic.Create(
                        Diagnostics.MissingBinding,
                        injection.Location,
                        injection.FullTypeName,
                        composition.ClassName));
                }
            }
        }

        private static bool IsCollectionArrayType(ITypeSymbol type, CompositionModel composition)
        {
            if (type is IArrayTypeSymbol arrayType)
            {
                var elementKey = arrayType.ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                foreach (var collection in composition.CollectionBindings)
                {
                    if (collection.ServiceTypeName == elementKey)
                        return true;
                }
            }
            return false;
        }

        private static bool IsFactoryType(ITypeSymbol type, CompositionModel composition)
        {
            // Check if this is a TChild.Factory type from a scope declaration
            if (type is INamedTypeSymbol named && named.Name == "Factory" && named.ContainingType is not null)
            {
                var parentType = named.ContainingType;
                foreach (var scope in composition.Scopes)
                {
                    if (SymbolEqualityComparer.Default.Equals(scope.ChildType, parentType))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Detects circular dependencies in the composition.
        /// </summary>
        public static void DetectCircularDependencies(CompositionModel composition, List<Diagnostic> diagnostics)
        {
            var visited = new HashSet<string>();
            var stack = new HashSet<string>();
            var path = new List<string>();

            foreach (var binding in composition.Bindings)
            {
                var key = binding.GetKey();
                if (!visited.Contains(key))
                {
                    if (DetectCycle(binding, composition, visited, stack, path, diagnostics))
                    {
                        return; // Stop after first cycle found
                    }
                }
            }
        }

        private static bool DetectCycle(
            BindingModel binding,
            CompositionModel composition,
            HashSet<string> visited,
            HashSet<string> stack,
            List<string> path,
            List<Diagnostic> diagnostics)
        {
            var key = binding.GetKey();
            visited.Add(key);
            stack.Add(key);
            path.Add(binding.ServiceType.Name);

            if (binding.ImplementationType is not null)
            {
                var ctorInfo = Parser.ConstructorAnalyzer.GetConstructor(binding.ImplementationType);
                if (ctorInfo is not null && !ctorInfo.IsParameterless)
                {
                    foreach (var param in ctorInfo.Parameters)
                    {
                        var resolved = composition.TryResolve(param.Type);
                        if (resolved?.Binding is not null)
                        {
                            var depKey = resolved.Binding.GetKey();
                            if (stack.Contains(depKey))
                            {
                                // Found cycle
                                path.Add(param.Type.Name);
                                diagnostics.Add(Diagnostic.Create(
                                    Diagnostics.CircularDependency,
                                    composition.AttributeLocation,
                                    string.Join(" → ", path)));
                                return true;
                            }

                            if (!visited.Contains(depKey))
                            {
                                if (DetectCycle(resolved.Binding, composition, visited, stack, path, diagnostics))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }

            stack.Remove(key);
            path.RemoveAt(path.Count - 1);
            return false;
        }
    }
}
