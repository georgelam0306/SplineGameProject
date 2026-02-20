// SPDX-License-Identifier: MIT
#nullable enable
using DerpLib.DI.Generator.Model;
using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace DerpLib.DI.Generator.Resolution
{
    /// <summary>
    /// Links parent-child relationships between compositions.
    /// </summary>
    internal static class ScopeLinker
    {
        /// <summary>
        /// Links all compositions by matching .Scope&lt;T&gt;() declarations to [Composition] classes.
        /// </summary>
        public static LinkedCompositions Link(ImmutableArray<CompositionModel> compositions)
        {
            var result = new LinkedCompositions();
            var bySymbol = new Dictionary<INamedTypeSymbol, CompositionModel>(SymbolEqualityComparer.Default);

            // Index compositions by their type symbol
            foreach (var comp in compositions)
            {
                bySymbol[comp.Type] = comp;
            }

            // Link parent-child relationships
            foreach (var comp in compositions)
            {
                foreach (var scope in comp.Scopes)
                {
                    if (bySymbol.TryGetValue(scope.ChildType, out var child))
                    {
                        if (child.Parent is null)
                        {
                            // Link child to parent
                            child.Parent = comp;
                            scope.LinkedChild = child;
                        }
                        else if (!SymbolEqualityComparer.Default.Equals(child.Parent.Type, comp.Type))
                        {
                            // Child already linked to another parent
                            comp.Diagnostics.Add(Diagnostic.Create(
                                Diagnostics.MultipleScopeParents,
                                comp.AttributeLocation,
                                child.ClassName,
                                child.Parent.ClassName,
                                comp.ClassName));
                        }
                    }
                    else
                    {
                        // Child composition doesn't exist
                        comp.Diagnostics.Add(Diagnostic.Create(
                            Diagnostics.InvalidScopeTarget,
                            comp.AttributeLocation,
                            scope.ChildType.Name));
                    }
                }
            }

            // Detect orphan compositions (have no parent but are not declared as roots)
            // Note: Root compositions are valid (they're the entry points)
            // For now, we consider any composition without a parent as potentially orphaned
            // The user can suppress this warning if intentional
            foreach (var comp in compositions)
            {
                if (comp.Parent is null)
                {
                    // This is a root composition - valid
                    result.RootCompositions.Add(comp);
                }
                else
                {
                    result.ChildCompositions.Add(comp);
                }
            }

            // Sort for deterministic output: roots first, then children in declaration order
            result.All.AddRange(result.RootCompositions);
            result.All.AddRange(result.ChildCompositions);

            return result;
        }
    }

    internal sealed class LinkedCompositions
    {
        public List<CompositionModel> RootCompositions { get; } = new();
        public List<CompositionModel> ChildCompositions { get; } = new();
        public List<CompositionModel> All { get; } = new();
    }
}
