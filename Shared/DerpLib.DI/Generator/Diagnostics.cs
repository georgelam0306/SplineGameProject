// SPDX-License-Identifier: MIT
#nullable enable
using Microsoft.CodeAnalysis;

namespace DerpLib.DI.Generator
{
    internal static class Diagnostics
    {
        private const string Category = "DerpLib.DI";

        /// <summary>DERP001: No binding found for required dependency.</summary>
        public static readonly DiagnosticDescriptor MissingBinding = new(
            id: "DERP001",
            title: "Missing binding",
            messageFormat: "No binding for '{0}' in '{1}' or any parent scope",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>DERP002: Circular dependency detected.</summary>
        public static readonly DiagnosticDescriptor CircularDependency = new(
            id: "DERP002",
            title: "Circular dependency",
            messageFormat: "Circular dependency detected: {0}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>DERP003: Composition is not declared as a child scope of any parent.</summary>
        public static readonly DiagnosticDescriptor OrphanComposition = new(
            id: "DERP003",
            title: "Orphan composition",
            messageFormat: "'{0}' is not declared as a scope in any parent composition",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        /// <summary>DERP004: Duplicate binding for the same type.</summary>
        public static readonly DiagnosticDescriptor DuplicateBinding = new(
            id: "DERP004",
            title: "Duplicate binding",
            messageFormat: "'{0}' is bound multiple times in '{1}'",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>DERP005: Scope target is not a valid composition.</summary>
        public static readonly DiagnosticDescriptor InvalidScopeTarget = new(
            id: "DERP005",
            title: "Invalid scope target",
            messageFormat: ".Scope<{0}>() but '{0}' is not marked with [Composition]",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>DERP006: Composition class must be partial.</summary>
        public static readonly DiagnosticDescriptor MustBePartial = new(
            id: "DERP006",
            title: "Composition must be partial",
            messageFormat: "Composition class '{0}' must be declared as partial",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>DERP007: Composition class is missing Setup method.</summary>
        public static readonly DiagnosticDescriptor MissingSetup = new(
            id: "DERP007",
            title: "Missing Setup method",
            messageFormat: "Composition class '{0}' must contain a static Setup() method",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>DERP008: Internal generator error.</summary>
        public static readonly DiagnosticDescriptor InternalError = new(
            id: "DERP008",
            title: "Generator error",
            messageFormat: "Internal generator error: {0}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>DERP009: Constructor not found for type binding.</summary>
        public static readonly DiagnosticDescriptor NoSuitableConstructor = new(
            id: "DERP009",
            title: "No suitable constructor",
            messageFormat: "Type '{0}' has no accessible constructor for dependency injection",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>DERP010: Factory lambda is not supported.</summary>
        public static readonly DiagnosticDescriptor UnsupportedFactoryLambda = new(
            id: "DERP010",
            title: "Unsupported factory lambda",
            messageFormat: "Factory lambda for '{0}' in '{1}' is not supported (supported: expression lambda, or block with only ctx.Inject*() statements + a single return)",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>DERP011: Child composition is declared as a scope in multiple parents.</summary>
        public static readonly DiagnosticDescriptor MultipleScopeParents = new(
            id: "DERP011",
            title: "Multiple scope parents",
            messageFormat: "Composition '{0}' is declared as a scope in multiple parents ('{1}' and '{2}')",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
