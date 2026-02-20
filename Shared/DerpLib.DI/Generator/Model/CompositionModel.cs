// SPDX-License-Identifier: MIT
#nullable enable
using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace DerpLib.DI.Generator.Model
{
    /// <summary>
    /// Represents a fully parsed [Composition] class.
    /// </summary>
    internal sealed class CompositionModel
    {
        /// <summary>The composition class symbol.</summary>
        public INamedTypeSymbol Type { get; }

        /// <summary>The fully qualified type name.</summary>
        public string FullTypeName { get; }

        /// <summary>The containing namespace.</summary>
        public string? Namespace { get; }

        /// <summary>The class name without namespace.</summary>
        public string ClassName { get; }

        /// <summary>Runtime arguments for this composition.</summary>
        public List<ArgModel> Args { get; } = new();

        /// <summary>Bindings declared in this composition.</summary>
        public List<BindingModel> Bindings { get; } = new();

        /// <summary>Collection bindings declared via BindAll&lt;T&gt;().</summary>
        public List<CollectionBindingModel> CollectionBindings { get; } = new();

        /// <summary>Child scopes declared in this composition.</summary>
        public List<ScopeModel> Scopes { get; } = new();

        /// <summary>Composition roots.</summary>
        public List<RootModel> Roots { get; } = new();

        /// <summary>Link to parent composition (set during linking phase).</summary>
        public CompositionModel? Parent { get; set; }

        /// <summary>Using directives from the source file (emitted into generated output).</summary>
        public List<string> SourceUsings { get; } = new();

        /// <summary>Diagnostics collected during parsing.</summary>
        public List<Diagnostic> Diagnostics { get; } = new();

        /// <summary>Location of the [Composition] attribute for error reporting.</summary>
        public Location? AttributeLocation { get; set; }

        /// <summary>Whether this composition is a root (has no parent).</summary>
        public bool IsRoot => Parent is null;

        public CompositionModel(INamedTypeSymbol type)
        {
            Type = type;
            FullTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            Namespace = type.ContainingNamespace?.IsGlobalNamespace == true
                ? null
                : type.ContainingNamespace?.ToDisplayString();
            ClassName = type.Name;
        }

        /// <summary>
        /// Gets all bindings available in this scope, including inherited ones.
        /// </summary>
        public IEnumerable<ResolvedBinding> GetAllBindings()
        {
            var seen = new HashSet<string>();

            // Local bindings take precedence
            foreach (var binding in Bindings)
            {
                var key = binding.GetKey();
                if (seen.Add(key))
                {
                    yield return new ResolvedBinding(binding, isInherited: false, source: this);
                }
            }

            // Local args are also resolvable
            foreach (var arg in Args)
            {
                var key = arg.FullTypeName;
                if (seen.Add(key))
                {
                    yield return new ResolvedBinding(arg, source: this);
                }
            }

            // Inherited from parent
            if (Parent is not null)
            {
                foreach (var resolved in Parent.GetAllBindings())
                {
                    var key = resolved.GetKey();
                    if (seen.Add(key))
                    {
                        yield return new ResolvedBinding(resolved, isInherited: true, inheritedFrom: resolved.Source);
                    }
                }
            }
        }

        /// <summary>
        /// Tries to resolve a type within this scope.
        /// </summary>
        public ResolvedBinding? TryResolve(ITypeSymbol type, string? tag = null)
        {
            var key = tag is null
                ? type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : $"{type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}:{tag}";

            foreach (var resolved in GetAllBindings())
            {
                if (resolved.GetKey() == key)
                    return resolved;
            }

            return null;
        }
    }

    /// <summary>
    /// A binding resolved within a scope context (may be local or inherited).
    /// </summary>
    internal sealed class ResolvedBinding
    {
        public BindingModel? Binding { get; }
        public ArgModel? Arg { get; }
        public bool IsInherited { get; }
        public CompositionModel Source { get; }
        public CompositionModel? InheritedFrom { get; }

        public bool IsArg => Arg is not null;

        public ResolvedBinding(BindingModel binding, bool isInherited, CompositionModel source)
        {
            Binding = binding;
            IsInherited = isInherited;
            Source = source;
        }

        public ResolvedBinding(ArgModel arg, CompositionModel source)
        {
            Arg = arg;
            IsInherited = false;
            Source = source;
        }

        public ResolvedBinding(ResolvedBinding original, bool isInherited, CompositionModel inheritedFrom)
        {
            Binding = original.Binding;
            Arg = original.Arg;
            IsInherited = isInherited;
            Source = original.Source;
            InheritedFrom = inheritedFrom;
        }

        public string GetKey()
        {
            if (Binding is not null) return Binding.GetKey();
            if (Arg is not null) return Arg.FullTypeName;
            throw new System.InvalidOperationException("Invalid resolved binding state");
        }

        public string GetResolverMethodName()
        {
            if (Binding is not null) return Binding.GetResolverMethodName();
            if (Arg is not null) return $"Resolve_{Arg.Type.Name}";
            throw new System.InvalidOperationException("Invalid resolved binding state");
        }

        public ITypeSymbol GetServiceType()
        {
            if (Binding is not null) return Binding.ServiceType;
            if (Arg is not null) return Arg.Type;
            throw new System.InvalidOperationException("Invalid resolved binding state");
        }

        public string GetFullTypeName()
        {
            if (Binding is not null) return Binding.ServiceTypeName;
            if (Arg is not null) return Arg.FullTypeName;
            throw new System.InvalidOperationException("Invalid resolved binding state");
        }
    }
}
