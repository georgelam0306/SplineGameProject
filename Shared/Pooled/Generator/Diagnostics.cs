// SPDX-License-Identifier: MIT
#nullable enable
using Microsoft.CodeAnalysis;

namespace Pooled.Generator
{
	internal static class Diag
	{
		public static readonly DiagnosticDescriptor POOLED001 = new DiagnosticDescriptor(
			id: "POOLED001",
			title: "Type must be partial struct to use [Pooled]",
			messageFormat: "Type '{0}' must be a public or internal partial struct",
			category: "Pooled",
			DiagnosticSeverity.Error,
			isEnabledByDefault: true);

		public static readonly DiagnosticDescriptor POOLED002 = new DiagnosticDescriptor(
			id: "POOLED002",
			title: "[Column(Inline=true)] requires unmanaged/blittable",
			messageFormat: "Field '{0}' in '{1}' marked Inline may be non-blittable; emitting as regular column",
			category: "Pooled",
			DiagnosticSeverity.Warning,
			isEnabledByDefault: true);

		public static readonly DiagnosticDescriptor POOLED003 = new DiagnosticDescriptor(
			id: "POOLED003",
			title: "Duplicate column name",
			messageFormat: "Duplicate column name '{0}' in '{1}'",
			category: "Pooled",
			DiagnosticSeverity.Error,
			isEnabledByDefault: true);

		public static readonly DiagnosticDescriptor POOLED004 = new DiagnosticDescriptor(
			id: "POOLED004",
			title: "Generic or nested generic schemas not supported",
			messageFormat: "Type '{0}' is generic or nested in a generic type",
			category: "Pooled",
			DiagnosticSeverity.Error,
			isEnabledByDefault: true);

		public static readonly DiagnosticDescriptor POOLED005 = new DiagnosticDescriptor(
			id: "POOLED005",
			title: "[Column(ReadOnly=true)] set in facade",
			messageFormat: "[Column(ReadOnly=true)] cannot be assigned through generated façade: '{0}.{1}'",
			category: "Pooled",
			DiagnosticSeverity.Warning,
			isEnabledByDefault: true);
	}
}


