// SPDX-License-Identifier: MIT
#nullable enable
using DerpLib.DI.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace DerpLib.DI.Generator.Parser
{
    /// <summary>
    /// Parses a [Composition] attributed class into a CompositionModel.
    /// </summary>
    internal static class CompositionParser
    {
        private const string CompositionAttributeFullName = "global::Derp.DI.CompositionAttribute";

        /// <summary>
        /// Parses a composition class and returns the model.
        /// </summary>
        public static CompositionModel? Parse(GeneratorAttributeSyntaxContext context)
        {
            if (context.TargetNode is not ClassDeclarationSyntax classDecl)
                return null;

            var typeSymbol = (INamedTypeSymbol)context.TargetSymbol;

            // Validate partial
            if (!IsPartial(classDecl))
            {
                var model = new CompositionModel(typeSymbol);
                model.Diagnostics.Add(Diagnostic.Create(
                    Diagnostics.MustBePartial,
                    classDecl.Identifier.GetLocation(),
                    typeSymbol.Name));
                return model;
            }

            // Find Setup method
            var setupMethod = FindSetupMethod(classDecl);
            if (setupMethod is null)
            {
                var model = new CompositionModel(typeSymbol);
                model.Diagnostics.Add(Diagnostic.Create(
                    Diagnostics.MissingSetup,
                    classDecl.Identifier.GetLocation(),
                    typeSymbol.Name));
                return model;
            }

            // Create model and parse Setup
            var result = new CompositionModel(typeSymbol);
            result.AttributeLocation = context.Attributes
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == CompositionAttributeFullName)?
                .ApplicationSyntaxReference?.GetSyntax().GetLocation();

            SetupParser.Parse(setupMethod, context.SemanticModel, result);

            // Collect using directives from source file
            CollectSourceUsings(classDecl, result);

            // Check for duplicate bindings
            var bindingKeys = result.Bindings.Select(b => b.GetKey()).ToList();
            var duplicates = bindingKeys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key);
            foreach (var dup in duplicates)
            {
                result.Diagnostics.Add(Diagnostic.Create(
                    Diagnostics.DuplicateBinding,
                    result.AttributeLocation,
                    dup,
                    typeSymbol.Name));
            }

            return result;
        }

        private static void CollectSourceUsings(ClassDeclarationSyntax classDecl, CompositionModel model)
        {
            var compilationUnit = classDecl.SyntaxTree.GetCompilationUnitRoot();
            foreach (var usingDirective in compilationUnit.Usings)
            {
                var text = usingDirective.ToString().Trim();

                // Skip DerpLib.DI usings — only needed for the Setup DSL, not generated code
                if (text.Contains("DerpLib.DI"))
                    continue;

                model.SourceUsings.Add(text);
            }
        }

        private static bool IsPartial(ClassDeclarationSyntax classDecl)
        {
            return classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
        }

        private static MethodDeclarationSyntax? FindSetupMethod(ClassDeclarationSyntax classDecl)
        {
            return classDecl.Members
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m =>
                    m.Identifier.Text == "Setup" &&
                    m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.StaticKeyword)));
        }
    }
}
