#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace ConfigRefresh.Generator
{
    [Generator]
    public sealed class ConfigRefreshGenerator : IIncrementalGenerator
    {
        private const string ConfigSourceAttr = "ConfigRefresh.ConfigSourceAttribute";
        private const string CachedConfigAttr = "ConfigRefresh.CachedConfigAttribute";
        private const string CachedStatAttr = "ConfigRefresh.CachedStatAttribute";
        private const string TypeIdFieldAttr = "ConfigRefresh.TypeIdFieldAttribute";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Detect [ConfigSource] on classes (system-level)
            var systemModels = context.SyntaxProvider.ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: ConfigSourceAttr,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => TransformSystem(ctx))
                .Where(static model => model is not null);

            // Detect [ConfigSource] on structs (entity-level SimRows)
            var entityModels = context.SyntaxProvider.ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: ConfigSourceAttr,
                predicate: static (node, _) => node is StructDeclarationSyntax,
                transform: static (ctx, _) => TransformEntity(ctx))
                .Where(static model => model is not null);

            // Generate system refresh methods
            context.RegisterSourceOutput(systemModels, static (spc, model) =>
            {
                if (model == null) return;
                var source = SystemRefreshRenderer.Render(model);
                var hint = model.Type.Name + ".ConfigRefresh.g.cs";
                spc.AddSource(hint, SourceText.From(source, Encoding.UTF8));
            });

            // Generate entity refresh static methods
            context.RegisterSourceOutput(entityModels, static (spc, model) =>
            {
                if (model == null) return;
                var source = EntityRefreshRenderer.Render(model);
                var hint = model.Type.Name + ".ConfigRefresh.g.cs";
                spc.AddSource(hint, SourceText.From(source, Encoding.UTF8));
            });

            // Generate SimWorld.RefreshEntityConfigs that calls RefreshStats on all entity tables
            var collectedEntityModels = entityModels.Collect();
            context.RegisterSourceOutput(collectedEntityModels, static (spc, models) =>
            {
                var validModels = models.Where(m => m != null).Select(m => m!).ToList();
                if (validModels.Count == 0) return;

                var source = SimWorldRefreshRenderer.Render(validModels);
                spc.AddSource("SimWorld.ConfigRefresh.g.cs", SourceText.From(source, Encoding.UTF8));
            });
        }

        private static ConfigSourceModel? TransformSystem(GeneratorAttributeSyntaxContext ctx)
        {
            var classDecl = (ClassDeclarationSyntax)ctx.TargetNode;
            var typeSymbol = (INamedTypeSymbol)ctx.TargetSymbol;

            // Must be partial
            if (!classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
                return null;

            // Extract [ConfigSource] attribute data
            var (tableType, rowId) = ExtractConfigSourceData(typeSymbol);
            if (tableType == null) return null;

            var model = new ConfigSourceModel(typeSymbol, tableType, rowId);

            // Find [CachedConfig] fields
            PopulateCachedFields(model, typeSymbol, CachedConfigAttr);

            if (model.CachedFields.Count == 0) return null;

            return model;
        }

        private static ConfigSourceModel? TransformEntity(GeneratorAttributeSyntaxContext ctx)
        {
            var structDecl = (StructDeclarationSyntax)ctx.TargetNode;
            var typeSymbol = (INamedTypeSymbol)ctx.TargetSymbol;

            // Must be partial
            if (!structDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
                return null;

            // Extract [ConfigSource] attribute data
            var (tableType, rowId) = ExtractConfigSourceData(typeSymbol);
            if (tableType == null) return null;

            var model = new ConfigSourceModel(typeSymbol, tableType, rowId);

            // Find [TypeIdField]
            foreach (var member in typeSymbol.GetMembers().OfType<IFieldSymbol>())
            {
                var typeIdAttr = member.GetAttributes().FirstOrDefault(a =>
                    a.AttributeClass?.ToDisplayString() == TypeIdFieldAttr);
                if (typeIdAttr != null)
                {
                    model.TypeIdFieldName = member.Name;
                    model.TypeIdFieldType = member.Type;
                    break;
                }
            }

            // Find [CachedStat] fields
            PopulateCachedFields(model, typeSymbol, CachedStatAttr);

            if (model.CachedFields.Count == 0) return null;

            return model;
        }

        private static (INamedTypeSymbol? tableType, int rowId) ExtractConfigSourceData(INamedTypeSymbol typeSymbol)
        {
            foreach (var attr in typeSymbol.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() == ConfigSourceAttr)
                {
                    if (attr.ConstructorArguments.Length >= 1)
                    {
                        var tableType = attr.ConstructorArguments[0].Value as INamedTypeSymbol;
                        int rowId = -1;
                        if (attr.ConstructorArguments.Length >= 2 && attr.ConstructorArguments[1].Value is int id)
                        {
                            rowId = id;
                        }
                        return (tableType, rowId);
                    }
                }
            }
            return (null, -1);
        }

        private static void PopulateCachedFields(ConfigSourceModel model, INamedTypeSymbol typeSymbol, string attrName)
        {
            foreach (var member in typeSymbol.GetMembers().OfType<IFieldSymbol>())
            {
                if (member.IsStatic || member.IsConst) continue;

                var cachedAttr = member.GetAttributes().FirstOrDefault(a =>
                    a.AttributeClass?.ToDisplayString() == attrName);

                if (cachedAttr == null) continue;

                // Get source property name (from attribute or convention)
                string? overrideName = null;
                if (cachedAttr.ConstructorArguments.Length > 0 &&
                    cachedAttr.ConstructorArguments[0].Value is string s &&
                    !string.IsNullOrEmpty(s))
                {
                    overrideName = s;
                }

                string sourcePropertyName = overrideName ?? ConvertFieldToPropertyName(member.Name);

                model.CachedFields.Add(new CachedFieldInfo(
                    member.Name,
                    sourcePropertyName,
                    member.Type));
            }
        }

        /// <summary>
        /// Converts _fieldName to FieldName (strip underscore, capitalize first letter).
        /// If no underscore, capitalizes the first letter.
        /// </summary>
        private static string ConvertFieldToPropertyName(string fieldName)
        {
            if (fieldName.StartsWith("_") && fieldName.Length > 1)
            {
                return char.ToUpperInvariant(fieldName[1]) + fieldName.Substring(2);
            }
            if (fieldName.Length > 0)
            {
                return char.ToUpperInvariant(fieldName[0]) + fieldName.Substring(1);
            }
            return fieldName;
        }
    }
}
