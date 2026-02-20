// SPDX-License-Identifier: MIT
#nullable enable
using DerpLib.DI.Generator.Emit;
using DerpLib.DI.Generator.Model;
using DerpLib.DI.Generator.Parser;
using DerpLib.DI.Generator.Resolution;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace DerpLib.DI.Generator
{
    [Generator]
    public sealed class CompositionGenerator : IIncrementalGenerator
    {
        private const string CompositionAttributeFullName = "DerpLib.DI.CompositionAttribute";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Find all [Composition] classes
            var compositions = context.SyntaxProvider.ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: CompositionAttributeFullName,
                predicate: static (n, _) => n is ClassDeclarationSyntax,
                transform: static (ctx, _) => CompositionParser.Parse(ctx))
                .Where(static m => m is not null)
                .Select(static (m, _) => m!);

            // Collect all compositions for linking
            var allCompositions = compositions.Collect();

            // Generate code
            context.RegisterSourceOutput(allCompositions, static (spc, models) =>
            {
                if (models.Length == 0)
                    return;

                try
                {
                    // Link parent-child relationships
                    var linked = ScopeLinker.Link(models);

                    // Generate code for each composition
                    foreach (var composition in linked.All)
                    {
                        // Report any parsing diagnostics
                        foreach (var diagnostic in composition.Diagnostics)
                        {
                            spc.ReportDiagnostic(diagnostic);
                        }

                        // Skip generation if there are errors
                        if (composition.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                            continue;

                        // Validate dependencies
                        var validationDiagnostics = new List<Diagnostic>();
                        InheritanceResolver.ValidateDependencies(composition, validationDiagnostics);
                        InheritanceResolver.DetectCircularDependencies(composition, validationDiagnostics);

                        foreach (var diagnostic in validationDiagnostics)
                        {
                            spc.ReportDiagnostic(diagnostic);
                        }

                        // Skip generation if there are validation errors
                        if (validationDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                            continue;

                        // Generate source
                        var emitDiagnostics = new List<Diagnostic>();
                        var source = CompositionEmitter.Emit(composition, emitDiagnostics);

                        foreach (var diagnostic in emitDiagnostics)
                        {
                            spc.ReportDiagnostic(diagnostic);
                        }

                        // Add source file
                        var hint = GetHintName(composition);
                        spc.AddSource(hint, SourceText.From(source, Encoding.UTF8));
                    }
                }
                catch (Exception ex)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.InternalError,
                        null,
                        ex.ToString()));
                }
            });
        }

        private static string GetHintName(CompositionModel composition)
        {
            var name = composition.FullTypeName
                .Replace("global::", string.Empty)
                .Replace('.', '_')
                .Replace('<', '_')
                .Replace('>', '_')
                .Replace(',', '_');
            return $"{name}.g.cs";
        }
    }
}
