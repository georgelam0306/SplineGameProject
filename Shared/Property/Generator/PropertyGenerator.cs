// SPDX-License-Identifier: MIT
#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Property.Generator
{
    [Generator]
    public sealed class PropertyGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var propertyFields = context.SyntaxProvider.ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: "Property.PropertyAttribute",
                predicate: static (node, _) => node is VariableDeclaratorSyntax,
                transform: static (ctx, _) => PropertyFieldModel.Create(ctx));

            var collected = propertyFields.Collect();

            context.RegisterSourceOutput(collected, static (spc, fieldModels) =>
            {
                GenerateAll(spc, fieldModels);
            });
        }

        private static void GenerateAll(SourceProductionContext context, ImmutableArray<PropertyFieldModel> fieldModels)
        {
            var componentModels = new Dictionary<INamedTypeSymbol, PropertyComponentModel>(SymbolEqualityComparer.Default);

            foreach (var fieldModel in fieldModels)
            {
                foreach (var diagnostic in fieldModel.Diagnostics)
                {
                    context.ReportDiagnostic(diagnostic);
                }

                if (!fieldModel.IsValid)
                {
                    continue;
                }

                if (!componentModels.TryGetValue(fieldModel.ComponentSymbol, out var componentModel))
                {
                    componentModel = new PropertyComponentModel(
                        fieldModel.ComponentSymbol,
                        fieldModel.PooledSettings.SoA,
                        fieldModel.PooledSettings.PoolId);
                    componentModels.Add(fieldModel.ComponentSymbol, componentModel);
                }

                if (fieldModel.ArrayValues.IsArray || fieldModel.ArrayValues.IsArray2D)
                {
                    componentModel.ArrayFields.Add(new PropertyArrayFieldModel(fieldModel.FieldSymbol.Name, fieldModel.ArrayValues));
                }

                var leaves = PropertyLeafFactory.CreateLeaves(fieldModel, out var leafDiagnostics);
                foreach (var diagnostic in leafDiagnostics)
                {
                    context.ReportDiagnostic(diagnostic);
                }

                foreach (var leaf in leaves)
                {
                    componentModel.Properties.Add(leaf);
                }
            }

            if (componentModels.Count == 0)
            {
                return;
            }

            foreach (var componentModel in componentModels.Values)
            {
                componentModel.Properties.Sort((left, right) =>
                {
                    int orderComparison = left.Order.CompareTo(right.Order);
                    if (orderComparison != 0)
                    {
                        return orderComparison;
                    }
                    return string.CompareOrdinal(left.Identifier, right.Identifier);
                });

                var usedIdentifiers = new HashSet<string>();
                for (int index = 0; index < componentModel.Properties.Count; index++)
                {
                    var leaf = componentModel.Properties[index];
                    leaf.Index = index;

                    string baseIdentifier = leaf.Identifier;
                    string uniqueIdentifier = baseIdentifier;
                    int suffix = 1;
                    while (!usedIdentifiers.Add(uniqueIdentifier))
                    {
                        uniqueIdentifier = baseIdentifier + suffix.ToString();
                        suffix++;
                    }

                    leaf.Identifier = uniqueIdentifier;
                }

                string source = ComponentRenderer.Render(componentModel);
                string hintName = GetHintName(componentModel.ComponentSymbol, suffix: "Properties.g.cs");
                context.AddSource(hintName, source);
            }

            string dispatcherSource = DispatcherRenderer.Render(componentModels);
            context.AddSource("PropertyDispatcher.g.cs", dispatcherSource);
        }

        private static string GetHintName(INamedTypeSymbol typeSymbol, string suffix)
        {
            string name = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty)
                .Replace('.', '_');
            return name + "." + suffix;
        }
    }
}
