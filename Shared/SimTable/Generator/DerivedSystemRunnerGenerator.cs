using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SimTable.Generator;

/// <summary>
/// Generates DerivedSystemRunner from a Setup method that declares systems and their dependencies.
///
/// Usage:
/// <code>
/// public partial class DerivedSystemRunner
/// {
///     [Conditional("DERIVED_SYSTEM_SETUP")]
///     static void Setup(IDerivedSystemBuilder b) =>
///         b.Add&lt;BuildingOccupancyGrid&gt;(w => w.BuildingRows)
///          .Add&lt;PowerGrid&gt;(w => w.BuildingRows)
///          .Add&lt;ZoneGraphSystem&gt;()
///          .Add&lt;ZoneFlowSystem&gt;();
/// }
/// </code>
/// </summary>
[Generator]
public class DerivedSystemRunnerGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find partial class declarations named "DerivedSystemRunner"
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsDerivedSystemRunnerClass(s),
                transform: static (ctx, _) => GetClassWithSetupMethod(ctx))
            .Where(static m => m != null);

        context.RegisterSourceOutput(classDeclarations, static (spc, model) =>
        {
            if (model == null) return;
            var source = GenerateDerivedSystemRunner(model.Value);
            spc.AddSource("DerivedSystemRunner.g.cs", source);
        });
    }

    private static bool IsDerivedSystemRunnerClass(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax classDecl &&
               classDecl.Identifier.Text == "DerivedSystemRunner" &&
               classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
    }

    private static DerivedSystemModel? GetClassWithSetupMethod(GeneratorSyntaxContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        // Find the Setup method with [Conditional("DERIVED_SYSTEM_SETUP")]
        var setupMethod = classDecl.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => HasDerivedSystemSetupAttribute(m));

        if (setupMethod == null) return null;

        // Get namespace
        var namespaceDecl = classDecl.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
        string? ns = namespaceDecl?.Name.ToString();

        // Parse the .Add<T>(...) calls (may be empty for base templates)
        var systems = ParseAddInvocations(setupMethod, semanticModel);

        return new DerivedSystemModel(ns, systems);
    }

    private static bool HasDerivedSystemSetupAttribute(MethodDeclarationSyntax method)
    {
        foreach (var attrList in method.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var name = attr.Name.ToString();
                if (name == "Conditional" || name == "System.Diagnostics.Conditional")
                {
                    // Check if argument is "DERIVED_SYSTEM_SETUP"
                    var args = attr.ArgumentList?.Arguments;
                    if (args?.Count > 0)
                    {
                        var argText = args.Value[0].Expression.ToString();
                        if (argText == "\"DERIVED_SYSTEM_SETUP\"")
                            return true;
                    }
                }
            }
        }
        return false;
    }

    private static List<DerivedSystemEntry> ParseAddInvocations(MethodDeclarationSyntax method, SemanticModel semanticModel)
    {
        var systems = new List<DerivedSystemEntry>();

        // The chain b.Add<A>().Add<B>().Add<C>() creates nested invocations
        // Order by the position of the generic method name (Add<T>) in source code
        var invocations = method.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(inv => inv.Expression is MemberAccessExpressionSyntax ma &&
                          ma.Name is GenericNameSyntax gn &&
                          gn.Identifier.Text == "Add")
            .Select(inv => (Invocation: inv, MemberAccess: (MemberAccessExpressionSyntax)inv.Expression))
            .OrderBy(x => x.MemberAccess.Name.SpanStart)  // Order by Add<T> position, not invocation
            .ToList();

        foreach (var (invocation, memberAccess) in invocations)
        {
            var genericName = (GenericNameSyntax)memberAccess.Name;

            var typeArgs = genericName.TypeArgumentList.Arguments;
            if (typeArgs.Count == 1)
            {
                var systemTypeSyntax = typeArgs[0];
                var systemTypeInfo = semanticModel.GetTypeInfo(systemTypeSyntax);
                var systemType = systemTypeInfo.Type;

                if (systemType != null)
                {
                    string systemTypeName = systemType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    string systemFieldName = ToCamelCase(systemType.Name);

                    // Check for dependency lambda: w => w.TableRows
                    var dependencies = new List<string>();
                    var args = invocation.ArgumentList.Arguments;
                    if (args.Count > 0)
                    {
                        var depLambda = args[0].Expression;
                        var tableName = ExtractTableNameFromLambda(depLambda);
                        if (tableName != null)
                        {
                            dependencies.Add(tableName);
                        }
                    }

                    systems.Add(new DerivedSystemEntry(systemTypeName, systemFieldName, dependencies));
                }
            }
        }

        return systems;
    }

    private static string? ExtractTableNameFromLambda(ExpressionSyntax expr)
    {
        // Handle: w => w.BuildingRows
        LambdaExpressionSyntax? lambda = expr as SimpleLambdaExpressionSyntax ??
                                          (LambdaExpressionSyntax?)(expr as ParenthesizedLambdaExpressionSyntax);
        if (lambda == null) return null;

        var body = lambda.ExpressionBody;
        if (body is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.Identifier.Text;
        }

        return null;
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return "_" + char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    private static string GenerateDerivedSystemRunner(DerivedSystemModel model)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        // SimWorld is in .Components subnamespace of the DerivedSystemRunner's namespace
        var simWorldNamespace = string.IsNullOrEmpty(model.Namespace) ? "Simulation.Components" : $"{model.Namespace}.Components";
        sb.AppendLine($"using {simWorldNamespace};");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(model.Namespace))
        {
            sb.AppendLine($"namespace {model.Namespace};");
            sb.AppendLine();
        }

        sb.AppendLine("public partial class DerivedSystemRunner");
        sb.AppendLine("{");

        // Field: SimWorld
        sb.AppendLine("    private readonly SimWorld _simWorld;");

        // Fields for each system
        foreach (var system in model.Systems)
        {
            sb.AppendLine($"    private readonly {system.TypeName} {system.FieldName};");
        }
        sb.AppendLine();

        // Fields for cached versions
        foreach (var system in model.Systems)
        {
            foreach (var dep in system.Dependencies)
            {
                sb.AppendLine($"    private uint {system.FieldName}_{dep}Version;");
            }
        }
        sb.AppendLine();

        // Constructor
        sb.Append("    public DerivedSystemRunner(SimWorld simWorld");
        foreach (var system in model.Systems)
        {
            // Extract simple type name for parameter
            var paramName = system.FieldName.TrimStart('_');
            sb.Append($", {system.TypeName} {paramName}");
        }
        sb.AppendLine(")");
        sb.AppendLine("    {");
        sb.AppendLine("        _simWorld = simWorld;");
        foreach (var system in model.Systems)
        {
            var paramName = system.FieldName.TrimStart('_');
            sb.AppendLine($"        {system.FieldName} = {paramName};");
        }
        sb.AppendLine("    }");
        sb.AppendLine();

        // InvalidateAll()
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Mark all derived systems as dirty. Called after snapshot restore.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public void InvalidateAll()");
        sb.AppendLine("    {");
        foreach (var system in model.Systems)
        {
            sb.AppendLine($"        {system.FieldName}.Invalidate();");
        }
        sb.AppendLine();
        sb.AppendLine("        // Reset cached versions");
        foreach (var system in model.Systems)
        {
            foreach (var dep in system.Dependencies)
            {
                sb.AppendLine($"        {system.FieldName}_{dep}Version = 0;");
            }
        }
        sb.AppendLine("    }");
        sb.AppendLine();

        // RebuildAll()
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Rebuild all derived systems in order. Auto-invalidates when dependencies change.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public void RebuildAll()");
        sb.AppendLine("    {");

        // Collect unique dependencies for local variable caching
        var allDeps = model.Systems.SelectMany(s => s.Dependencies).Distinct().ToList();
        foreach (var dep in allDeps)
        {
            sb.AppendLine($"        var {ToCamelCase(dep).TrimStart('_')}Version = _simWorld.{dep}.Version;");
        }
        if (allDeps.Count > 0) sb.AppendLine();

        foreach (var system in model.Systems)
        {
            if (system.Dependencies.Count > 0)
            {
                // Check version and invalidate if changed
                var conditions = system.Dependencies.Select(dep =>
                {
                    var localVar = ToCamelCase(dep).TrimStart('_') + "Version";
                    return $"{localVar} != {system.FieldName}_{dep}Version";
                });
                sb.AppendLine($"        if ({string.Join(" || ", conditions)})");
                sb.AppendLine("        {");
                foreach (var dep in system.Dependencies)
                {
                    var localVar = ToCamelCase(dep).TrimStart('_') + "Version";
                    sb.AppendLine($"            {system.FieldName}_{dep}Version = {localVar};");
                }
                sb.AppendLine($"            {system.FieldName}.Invalidate();");
                sb.AppendLine("        }");
            }
            sb.AppendLine($"        {system.FieldName}.Rebuild();");
            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private readonly struct DerivedSystemModel
    {
        public readonly string? Namespace;
        public readonly List<DerivedSystemEntry> Systems;

        public DerivedSystemModel(string? ns, List<DerivedSystemEntry> systems)
        {
            Namespace = ns;
            Systems = systems;
        }
    }

    private readonly struct DerivedSystemEntry
    {
        public readonly string TypeName;
        public readonly string FieldName;
        public readonly List<string> Dependencies;

        public DerivedSystemEntry(string typeName, string fieldName, List<string> dependencies)
        {
            TypeName = typeName;
            FieldName = fieldName;
            Dependencies = dependencies;
        }
    }
}
