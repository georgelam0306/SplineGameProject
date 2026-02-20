// SPDX-License-Identifier: MIT
#nullable enable
using Microsoft.CodeAnalysis;

namespace Property.Generator
{
    internal static class Diag
    {
        public static readonly DiagnosticDescriptor PROP001 = new DiagnosticDescriptor(
            id: "PROP001",
            title: "Property host must be a pooled partial struct",
            messageFormat: "Type '{0}' must be a public or internal partial struct with [Pooled]",
            category: "Property",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor PROP002 = new DiagnosticDescriptor(
            id: "PROP002",
            title: "Property field must be a [Column]",
            messageFormat: "Field '{0}' in '{1}' must be marked with [Column] to be used as a property",
            category: "Property",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor PROP003 = new DiagnosticDescriptor(
            id: "PROP003",
            title: "Unsupported property field type",
            messageFormat: "Field '{0}' in '{1}' has unsupported property type '{2}'",
            category: "Property",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor PROP004 = new DiagnosticDescriptor(
            id: "PROP004",
            title: "ExpandSubfields requires unmanaged type",
            messageFormat: "Field '{0}' in '{1}' has ExpandSubfields set but type '{2}' is not unmanaged",
            category: "Property",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor PROP005 = new DiagnosticDescriptor(
            id: "PROP005",
            title: "Subfield expansion requires mutable fields",
            messageFormat: "Field '{0}' in '{1}' cannot expand subfields because '{2}' is not a mutable instance field",
            category: "Property",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor PROP006 = new DiagnosticDescriptor(
            id: "PROP006",
            title: "Property kind override incompatible with field type",
            messageFormat: "Field '{0}' in '{1}' specifies kind '{2}', but type '{3}' does not match",
            category: "Property",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor PROP007 = new DiagnosticDescriptor(
            id: "PROP007",
            title: "Property field must be an instance field",
            messageFormat: "Field '{0}' in '{1}' must be a non-static, non-const field",
            category: "Property",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor PROP008 = new DiagnosticDescriptor(
            id: "PROP008",
            title: "Property array field must be an inline array",
            messageFormat: "Field '{0}' in '{1}' is marked as a property array but type '{2}' is not a C# [InlineArray] struct",
            category: "Property",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor PROP009 = new DiagnosticDescriptor(
            id: "PROP009",
            title: "Property array length mismatch",
            messageFormat: "Field '{0}' in '{1}' declares length {2}, but inline array length is {3}",
            category: "Property",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
