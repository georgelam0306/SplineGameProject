// SPDX-License-Identifier: MIT
#nullable enable
using DerpLib.DI.Generator.Model;
using DerpLib.DI.Generator.Parser;
using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Text;

namespace DerpLib.DI.Generator.Emit
{
    /// <summary>
    /// Emits Resolve_X() methods for bindings.
    /// </summary>
    internal static class ResolverEmitter
    {
        /// <summary>
        /// Emits resolver method for an arg.
        /// </summary>
        public static void EmitArgResolver(StringBuilder sb, ArgModel arg, string indent)
        {
            var methodName = $"Resolve_{arg.Type.Name}";
            sb.AppendLine($"{indent}internal {arg.FullTypeName} {methodName}() => _arg_{arg.Name};");
        }

        /// <summary>
        /// Emits resolver method for a binding.
        /// </summary>
        public static void EmitBindingResolver(
            StringBuilder sb,
            BindingModel binding,
            CompositionModel composition,
            string indent,
            List<Diagnostic> diagnostics)
        {
            var methodName = binding.GetResolverMethodName();
            var returnType = binding.ServiceTypeName;

            if (binding.Lifetime == BindingLifetime.Transient)
            {
                EmitTransientResolver(sb, binding, composition, methodName, returnType, indent, diagnostics);
            }
            else
            {
                EmitSingletonResolver(sb, binding, composition, methodName, returnType, indent, diagnostics);
            }
        }

        private static void EmitTransientResolver(
            StringBuilder sb,
            BindingModel binding,
            CompositionModel composition,
            string methodName,
            string returnType,
            string indent,
            List<Diagnostic> diagnostics)
        {
            sb.AppendLine($"{indent}internal {returnType} {methodName}()");
            sb.AppendLine($"{indent}{{");

            EmitFactoryPreludeIfNeeded(sb, binding, composition, indent + "    ", diagnostics);
            var instanceExpr = GetInstanceExpression(binding, composition, indent + "    ", diagnostics);
            sb.AppendLine($"{indent}    var instance = {instanceExpr};");
            sb.AppendLine($"{indent}    TrackDisposable(instance);");
            sb.AppendLine($"{indent}    return instance;");

            sb.AppendLine($"{indent}}}");
        }

        private static void EmitSingletonResolver(
            StringBuilder sb,
            BindingModel binding,
            CompositionModel composition,
            string methodName,
            string returnType,
            string indent,
            List<Diagnostic> diagnostics)
        {
            var fieldName = binding.GetSingletonFieldName();

            sb.AppendLine($"{indent}internal {returnType} {methodName}()");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}    if ({fieldName} is {{ }} cached) return cached;");
            sb.AppendLine($"{indent}    lock (_lock)");
            sb.AppendLine($"{indent}    {{");
            sb.AppendLine($"{indent}        if ({fieldName} is {{ }} cached2) return cached2;");

            EmitFactoryPreludeIfNeeded(sb, binding, composition, indent + "        ", diagnostics);
            var instanceExpr = GetInstanceExpression(binding, composition, indent + "        ", diagnostics);
            sb.AppendLine($"{indent}        var instance = {instanceExpr};");
            sb.AppendLine($"{indent}        TrackDisposable(instance);");
            sb.AppendLine($"{indent}        return {fieldName} = instance;");

            sb.AppendLine($"{indent}    }}");
            sb.AppendLine($"{indent}}}");
        }

        private static void EmitFactoryPreludeIfNeeded(
            StringBuilder sb,
            BindingModel binding,
            CompositionModel composition,
            string indent,
            List<Diagnostic> diagnostics)
        {
            if (binding.Factory is null || !binding.FactoryIsBlock)
            {
                return;
            }

            foreach (var injection in binding.FactoryInjections)
            {
                var resolved = injection.Kind == FactoryInjectionKind.InjectFromParent
                    ? composition.Parent?.TryResolve(injection.Type, injection.Tag)
                    : composition.TryResolve(injection.Type, injection.Tag);

                if (resolved is null)
                {
                    diagnostics.Add(Diagnostic.Create(
                        Diagnostics.MissingBinding,
                        injection.Location,
                        injection.FullTypeName,
                        composition.ClassName));
                }

                string valueExpression;
                if (injection.Kind == FactoryInjectionKind.InjectFromParent)
                {
                    if (composition.Parent is null || resolved is null)
                    {
                        valueExpression = "default!";
                    }
                    else
                    {
                        valueExpression = $"_parent.{resolved.GetResolverMethodName()}()";
                    }
                }
                else
                {
                    valueExpression = resolved is null
                        ? "default!"
                        : $"{resolved.GetResolverMethodName()}()";
                }

                sb.AppendLine($"{indent}var {injection.VariableName} = {valueExpression};");
            }
        }

        private static string GetInstanceExpression(
            BindingModel binding,
            CompositionModel composition,
            string indent,
            List<Diagnostic> diagnostics)
        {
            if (binding.ImplementationType is not null)
            {
                // Type binding - use constructor injection
                return GetConstructorCall(binding.ImplementationType, composition, indent, diagnostics);
            }

            if (binding.Factory is not null)
            {
                // Factory binding - use parsed/normalized expression text
                return binding.FactoryExpressionText ?? "default!";
            }

            return "default!";
        }

        private static string GetConstructorCall(
            ITypeSymbol implType,
            CompositionModel composition,
            string indent,
            List<Diagnostic> diagnostics)
        {
            var ctorInfo = ConstructorAnalyzer.GetConstructor(implType);
            var implTypeName = implType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (ctorInfo is null || !ctorInfo.IsParameterless && ctorInfo.Parameters.Count == 0)
            {
                // Check for parameterless constructor
                if (ConstructorAnalyzer.HasParameterlessConstructor(implType))
                {
                    return $"new {implTypeName}()";
                }

                diagnostics.Add(Diagnostic.Create(
                    Diagnostics.NoSuitableConstructor,
                    null,
                    implType.Name));
                return "default!";
            }

            if (ctorInfo.IsParameterless)
            {
                return $"new {implTypeName}()";
            }

            // Build constructor call with resolved dependencies
            var args = new List<string>();
            foreach (var param in ctorInfo.Parameters)
            {
                var resolved = composition.TryResolve(param.Type);
                if (resolved is null)
                {
                    // Check if it's a scope factory
                    var factoryResolved = TryResolveFactory(param.Type, composition);
                    if (factoryResolved is not null)
                    {
                        args.Add(factoryResolved);
                        continue;
                    }

                    // Check if it's a collection array (T[] from BindAll<T>)
                    var collectionResolved = TryResolveCollectionArray(param.Type, composition);
                    if (collectionResolved is not null)
                    {
                        args.Add(collectionResolved);
                        continue;
                    }

                    diagnostics.Add(Diagnostic.Create(
                        Diagnostics.MissingBinding,
                        null,
                        param.FullTypeName,
                        composition.ClassName));
                    args.Add("default!");
                }
                else
                {
                    args.Add($"{resolved.GetResolverMethodName()}()");
                }
            }

            if (args.Count <= 2)
            {
                return $"new {implTypeName}({string.Join(", ", args)})";
            }

            // Multi-line for readability
            var sb = new StringBuilder();
            sb.AppendLine($"new {implTypeName}(");
            for (int i = 0; i < args.Count; i++)
            {
                var comma = i < args.Count - 1 ? "," : "";
                sb.Append($"{indent}    {args[i]}{comma}");
                if (i < args.Count - 1) sb.AppendLine();
            }
            sb.Append(")");
            return sb.ToString();
        }

        private static string? TryResolveCollectionArray(ITypeSymbol paramType, CompositionModel composition)
        {
            if (paramType is IArrayTypeSymbol arrayType)
            {
                var elementKey = arrayType.ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                foreach (var collection in composition.CollectionBindings)
                {
                    if (collection.ServiceTypeName == elementKey)
                    {
                        return $"{collection.GetArrayResolverMethodName()}()";
                    }
                }
            }
            return null;
        }

        private static string? TryResolveFactory(ITypeSymbol paramType, CompositionModel composition)
        {
            // Check if this is a TChild.Factory type from a scope declaration
            if (paramType is INamedTypeSymbol named && named.Name == "Factory" && named.ContainingType is not null)
            {
                var parentType = named.ContainingType;
                foreach (var scope in composition.Scopes)
                {
                    if (SymbolEqualityComparer.Default.Equals(scope.ChildType, parentType))
                    {
                        return $"{scope.GetFactoryResolverMethodName()}()";
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Emits forwarding resolver for inherited binding.
        /// </summary>
        public static void EmitInheritedResolver(StringBuilder sb, ResolvedBinding resolved, string indent)
        {
            var methodName = resolved.GetResolverMethodName();
            var returnType = resolved.GetFullTypeName();

            sb.AppendLine($"{indent}internal {returnType} {methodName}()");
            sb.AppendLine($"{indent}    => _parent.{methodName}();");
        }

        /// <summary>
        /// Emits resolver methods for a collection binding (individual items + array).
        /// </summary>
        public static void EmitCollectionResolvers(
            StringBuilder sb,
            CollectionBindingModel collection,
            CompositionModel composition,
            string indent,
            List<Diagnostic> diagnostics)
        {
            // Individual item resolvers
            for (int i = 0; i < collection.Items.Count; i++)
            {
                var item = collection.Items[i];
                var methodName = collection.GetItemResolverMethodName(i);
                var returnType = collection.ServiceTypeName;

                if (collection.Lifetime == BindingLifetime.Singleton)
                {
                    var fieldName = collection.GetItemSingletonFieldName(i);

                    sb.AppendLine($"{indent}internal {returnType} {methodName}()");
                    sb.AppendLine($"{indent}{{");
                    sb.AppendLine($"{indent}    if ({fieldName} is {{ }} cached) return cached;");
                    sb.AppendLine($"{indent}    lock (_lock)");
                    sb.AppendLine($"{indent}    {{");
                    sb.AppendLine($"{indent}        if ({fieldName} is {{ }} cached2) return cached2;");

                    var instanceExpr = GetConstructorCall(item.ImplementationType, composition, indent + "        ", diagnostics);
                    sb.AppendLine($"{indent}        var instance = {instanceExpr};");
                    sb.AppendLine($"{indent}        TrackDisposable(instance);");
                    sb.AppendLine($"{indent}        return {fieldName} = instance;");

                    sb.AppendLine($"{indent}    }}");
                    sb.AppendLine($"{indent}}}");
                }
                else
                {
                    sb.AppendLine($"{indent}internal {returnType} {methodName}()");
                    sb.AppendLine($"{indent}{{");

                    var instanceExpr = GetConstructorCall(item.ImplementationType, composition, indent + "    ", diagnostics);
                    sb.AppendLine($"{indent}    var instance = {instanceExpr};");
                    sb.AppendLine($"{indent}    TrackDisposable(instance);");
                    sb.AppendLine($"{indent}    return instance;");

                    sb.AppendLine($"{indent}}}");
                }

                sb.AppendLine();
            }

            // Array resolver (always creates a new array, items may be cached singletons)
            var arrayMethodName = collection.GetArrayResolverMethodName();
            var arrayReturnType = $"{collection.ServiceTypeName}[]";

            sb.AppendLine($"{indent}internal {arrayReturnType} {arrayMethodName}()");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}    return new {collection.ServiceTypeName}[]");
            sb.AppendLine($"{indent}    {{");
            for (int i = 0; i < collection.Items.Count; i++)
            {
                var comma = i < collection.Items.Count - 1 ? "," : "";
                sb.AppendLine($"{indent}        {collection.GetItemResolverMethodName(i)}(){comma}");
            }
            sb.AppendLine($"{indent}    }};");
            sb.AppendLine($"{indent}}}");
        }

        /// <summary>
        /// Emits factory resolver for a child scope.
        /// </summary>
        public static void EmitFactoryResolver(StringBuilder sb, ScopeModel scope, CompositionModel parent, string indent)
        {
            var methodName = scope.GetFactoryResolverMethodName();
            var fieldName = scope.GetFactorySingletonFieldName();
            var factoryType = scope.GetFactoryTypeName();

            sb.AppendLine($"{indent}internal {factoryType} {methodName}()");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}    if ({fieldName} is {{ }} cached) return cached;");
            sb.AppendLine($"{indent}    lock (_lock)");
            sb.AppendLine($"{indent}    {{");
            sb.AppendLine($"{indent}        if ({fieldName} is {{ }} cached2) return cached2;");
            sb.AppendLine($"{indent}        var instance = new {factoryType}(this);");
            sb.AppendLine($"{indent}        return {fieldName} = instance;");
            sb.AppendLine($"{indent}    }}");
            sb.AppendLine($"{indent}}}");
        }
    }
}
