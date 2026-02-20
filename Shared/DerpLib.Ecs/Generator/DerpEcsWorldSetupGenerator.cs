using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DerpLib.Ecs.Generator;

[Generator]
public sealed class DerpEcsWorldSetupGenerator : IIncrementalGenerator
{
    private const string SetupConditionalSymbol = "DERP_ECS_SETUP";
    private const string ListHandleFqn = "global::DerpLib.Ecs.ListHandle<T>";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<WorldSetupParseResult> setups = context.SyntaxProvider.ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: "System.Diagnostics.ConditionalAttribute",
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (syntaxContext, _) => TryGetWorldSetup(syntaxContext));

        IncrementalValueProvider<ImmutableArray<WorldSetupParseResult>> allSetups = setups.Collect();

        context.RegisterSourceOutput(allSetups, static (spc, worldSetups) =>
        {
            Emit(spc, worldSetups);
        });
    }

    private static WorldSetupParseResult TryGetWorldSetup(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not IMethodSymbol methodSymbol)
        {
            return default;
        }

        if (!string.Equals(methodSymbol.Name, "Setup", StringComparison.Ordinal))
        {
            return default;
        }

        if (!methodSymbol.IsStatic || methodSymbol.Parameters.Length != 1)
        {
            return default;
        }

        if (methodSymbol.DeclaringSyntaxReferences.Length == 0)
        {
            return default;
        }

        if (methodSymbol.ContainingType is not INamedTypeSymbol worldType)
        {
            return default;
        }

        if (!HasConditionalAttribute(context.Attributes, SetupConditionalSymbol))
        {
            return default;
        }

        var methodSyntax = (MethodDeclarationSyntax)methodSymbol.DeclaringSyntaxReferences[0].GetSyntax();
        string builderParameterName = methodSymbol.Parameters[0].Name;

        var archetypes = new List<ArchetypeSetup>(4);
        if (!TryParseArchetypes(context.SemanticModel, methodSyntax, builderParameterName, archetypes, out var parseError))
        {
            if (parseError != null)
            {
                return new WorldSetupParseResult(worldType, default, parseError);
            }
            return default;
        }

        if (archetypes.Count == 0)
        {
            return default;
        }

        return new WorldSetupParseResult(worldType, archetypes.ToImmutableArray(), diagnostic: null);
    }

    private static bool HasConditionalAttribute(ImmutableArray<AttributeData> attributes, string expectedSymbol)
    {
        for (int i = 0; i < attributes.Length; i++)
        {
            var attr = attributes[i];
            if (attr.AttributeClass == null)
            {
                continue;
            }

            if (!string.Equals(attr.AttributeClass.ToDisplayString(), "System.Diagnostics.ConditionalAttribute", StringComparison.Ordinal))
            {
                continue;
            }

            if (attr.ConstructorArguments.Length != 1)
            {
                continue;
            }

            if (attr.ConstructorArguments[0].Value is string s &&
                string.Equals(s, expectedSymbol, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseArchetypes(
        SemanticModel semanticModel,
        MethodDeclarationSyntax methodSyntax,
        string builderParameterName,
        List<ArchetypeSetup> archetypes,
        out Diagnostic? diagnostic)
    {
        diagnostic = null;

        if (methodSyntax.ExpressionBody != null)
        {
            if (!TryParseArchetypeExpression(semanticModel, methodSyntax.ExpressionBody.Expression, builderParameterName, archetypes, out diagnostic))
            {
                return false;
            }

            return true;
        }

        if (methodSyntax.Body == null)
        {
            return true;
        }

        foreach (StatementSyntax statement in methodSyntax.Body.Statements)
        {
            if (statement is not ExpressionStatementSyntax exprStmt)
            {
                continue;
            }

            if (!TryParseArchetypeExpression(semanticModel, exprStmt.Expression, builderParameterName, archetypes, out diagnostic))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryParseArchetypeExpression(
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        string builderParameterName,
        List<ArchetypeSetup> archetypes,
        out Diagnostic? diagnostic)
    {
        diagnostic = null;

        var chain = new List<InvocationExpressionSyntax>(16);
        if (!TryUnwindInvocationChain(expression, chain))
        {
            return true;
        }

        chain.Reverse();

        if (chain.Count == 0)
        {
            return true;
        }

        IMethodSymbol? firstMethod = semanticModel.GetSymbolInfo(chain[0]).Symbol as IMethodSymbol;
        if (firstMethod == null || !string.Equals(firstMethod.Name, "Archetype", StringComparison.Ordinal) || firstMethod.TypeArguments.Length != 1)
        {
            return true;
        }

        if (!IsRootedInBuilderParameter(chain[0], builderParameterName))
        {
            return true;
        }

        ITypeSymbol kindType = firstMethod.TypeArguments[0];

        int capacity = 0;
        int spawnQueueCapacityOverride = 0;
        int destroyQueueCapacityOverride = 0;
        var components = new List<ITypeSymbol>(8);
        var queries = new List<QuerySetup>(4);
        SpatialIndexSetup? spatial = null;

        for (int i = 1; i < chain.Count; i++)
        {
            var invocation = chain[i];
            if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
            {
                continue;
            }

            if (string.Equals(method.Name, "Capacity", StringComparison.Ordinal))
            {
                if (!TryGetSingleIntArgument(semanticModel, invocation, out capacity))
                {
                    diagnostic = Diagnostic.Create(Diagnostics.InvalidEcsSetup, invocation.GetLocation(), "Capacity(...) must be a compile-time int.");
                    return false;
                }
                continue;
            }

            if (string.Equals(method.Name, "SpawnQueueCapacity", StringComparison.Ordinal))
            {
                if (spawnQueueCapacityOverride != 0)
                {
                    diagnostic = Diagnostic.Create(Diagnostics.InvalidEcsSetup, invocation.GetLocation(), "SpawnQueueCapacity(...) may only be declared once per archetype.");
                    return false;
                }

                if (!TryGetSingleIntArgument(semanticModel, invocation, out spawnQueueCapacityOverride) || spawnQueueCapacityOverride <= 0)
                {
                    diagnostic = Diagnostic.Create(Diagnostics.InvalidEcsSetup, invocation.GetLocation(), "SpawnQueueCapacity(...) must be a compile-time int > 0.");
                    return false;
                }

                continue;
            }

            if (string.Equals(method.Name, "DestroyQueueCapacity", StringComparison.Ordinal))
            {
                if (destroyQueueCapacityOverride != 0)
                {
                    diagnostic = Diagnostic.Create(Diagnostics.InvalidEcsSetup, invocation.GetLocation(), "DestroyQueueCapacity(...) may only be declared once per archetype.");
                    return false;
                }

                if (!TryGetSingleIntArgument(semanticModel, invocation, out destroyQueueCapacityOverride) || destroyQueueCapacityOverride <= 0)
                {
                    diagnostic = Diagnostic.Create(Diagnostics.InvalidEcsSetup, invocation.GetLocation(), "DestroyQueueCapacity(...) must be a compile-time int > 0.");
                    return false;
                }

                continue;
            }

            if (string.Equals(method.Name, "With", StringComparison.Ordinal) && method.TypeArguments.Length == 1)
            {
                components.Add(method.TypeArguments[0]);
                continue;
            }

            if (string.Equals(method.Name, "Spatial", StringComparison.Ordinal))
            {
                if (spatial != null)
                {
                    diagnostic = Diagnostic.Create(Diagnostics.InvalidEcsSetup, invocation.GetLocation(), "Spatial(...) may only be declared once per archetype.");
                    return false;
                }

                if (!TryParseSpatialIndex(semanticModel, invocation, out var spatialIndex, out diagnostic))
                {
                    return false;
                }

                spatial = spatialIndex;
                continue;
            }

            if (string.Equals(method.Name, "QueryRadius", StringComparison.Ordinal))
            {
                if (!TryParseQuery(semanticModel, invocation, QueryKind.Radius, out var query, out diagnostic))
                {
                    return false;
                }
                queries.Add(query);
                continue;
            }

            if (string.Equals(method.Name, "QueryAabb", StringComparison.Ordinal))
            {
                if (!TryParseQuery(semanticModel, invocation, QueryKind.Aabb, out var query, out diagnostic))
                {
                    return false;
                }
                queries.Add(query);
                continue;
            }
        }

        if (capacity <= 0)
        {
            diagnostic = Diagnostic.Create(Diagnostics.InvalidEcsSetup, chain[0].GetLocation(), "Archetype(...) requires Capacity(<int>) > 0.");
            return false;
        }

        if (components.Count == 0)
        {
            diagnostic = Diagnostic.Create(Diagnostics.InvalidEcsSetup, chain[0].GetLocation(), "Archetype(...) requires at least one With<TComponent>().");
            return false;
        }

        archetypes.Add(new ArchetypeSetup(
            kindType,
            capacity,
            components.ToImmutableArray(),
            queries.ToImmutableArray(),
            spatial,
            spawnQueueCapacityOverride,
            destroyQueueCapacityOverride));
        return true;
    }

    private static bool IsRootedInBuilderParameter(InvocationExpressionSyntax firstInvocation, string builderParameterName)
    {
        if (firstInvocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        if (memberAccess.Expression is IdentifierNameSyntax id)
        {
            return string.Equals(id.Identifier.ValueText, builderParameterName, StringComparison.Ordinal);
        }

        return false;
    }

    private static bool TryUnwindInvocationChain(ExpressionSyntax expression, List<InvocationExpressionSyntax> chain)
    {
        ExpressionSyntax current = expression;
        while (true)
        {
            if (current is InvocationExpressionSyntax invocation)
            {
                chain.Add(invocation);
                current = invocation.Expression is MemberAccessExpressionSyntax ma ? ma.Expression : invocation.Expression;
                continue;
            }

            if (current is MemberAccessExpressionSyntax memberAccess)
            {
                current = memberAccess.Expression;
                continue;
            }

            break;
        }

        return chain.Count > 0;
    }

    private static bool TryGetSingleIntArgument(SemanticModel semanticModel, InvocationExpressionSyntax invocation, out int value)
    {
        value = 0;
        if (invocation.ArgumentList == null || invocation.ArgumentList.Arguments.Count != 1)
        {
            return false;
        }

        var arg = invocation.ArgumentList.Arguments[0];
        Optional<object?> constant = semanticModel.GetConstantValue(arg.Expression);
        if (!constant.HasValue || constant.Value is not int i)
        {
            return false;
        }

        value = i;
        return true;
    }

    private static bool TryParseQuery(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        QueryKind kind,
        out QuerySetup query,
        out Diagnostic? diagnostic)
    {
        query = default;
        diagnostic = null;

        string? positionMemberName = null;
        ITypeSymbol? positionComponentType = null;
        int maxResults = 0;

        SeparatedSyntaxList<ArgumentSyntax> args = invocation.ArgumentList.Arguments;
        for (int i = 0; i < args.Count; i++)
        {
            ArgumentSyntax arg = args[i];
            string name = arg.NameColon?.Name.Identifier.ValueText ?? string.Empty;

            if (string.Equals(name, "position", StringComparison.Ordinal))
            {
                if (!TryParseNameofMember(semanticModel, arg.Expression, out positionComponentType, out positionMemberName))
                {
                    diagnostic = Diagnostic.Create(Diagnostics.InvalidEcsSetup, arg.GetLocation(), "position: must be nameof(<Component>.<Member>).");
                    return false;
                }
                continue;
            }

            if (string.Equals(name, "maxResults", StringComparison.Ordinal))
            {
                Optional<object?> constant = semanticModel.GetConstantValue(arg.Expression);
                if (!constant.HasValue || constant.Value is not int m)
                {
                    diagnostic = Diagnostic.Create(Diagnostics.InvalidEcsSetup, arg.GetLocation(), "maxResults: must be a compile-time int.");
                    return false;
                }
                maxResults = m;
                continue;
            }

            if (string.IsNullOrEmpty(name))
            {
                // Support positional args: (position, maxResults)
                if (positionMemberName == null)
                {
                    if (!TryParseNameofMember(semanticModel, arg.Expression, out positionComponentType, out positionMemberName))
                    {
                        diagnostic = Diagnostic.Create(Diagnostics.InvalidEcsSetup, arg.GetLocation(), "First arg must be nameof(<Component>.<Member>).");
                        return false;
                    }
                }
                else
                {
                    Optional<object?> constant = semanticModel.GetConstantValue(arg.Expression);
                    if (!constant.HasValue || constant.Value is not int m)
                    {
                        diagnostic = Diagnostic.Create(Diagnostics.InvalidEcsSetup, arg.GetLocation(), "Second arg must be a compile-time int.");
                        return false;
                    }
                    maxResults = m;
                }
            }
        }

        if (positionMemberName == null || positionComponentType == null || maxResults <= 0)
        {
            diagnostic = Diagnostic.Create(Diagnostics.InvalidEcsSetup, invocation.GetLocation(), "Query requires position and maxResults > 0.");
            return false;
        }

        query = new QuerySetup(kind, positionComponentType, positionMemberName, maxResults);
        return true;
    }

    private static bool TryParseSpatialIndex(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        out SpatialIndexSetup spatial,
        out Diagnostic? diagnostic)
    {
        spatial = default;
        diagnostic = null;

        string? positionMemberName = null;
        ITypeSymbol? positionComponentType = null;
        int cellSize = 0;
        int gridSize = 0;
        int originX = 0;
        int originY = 0;
        int positionalIndex = 0;

        SeparatedSyntaxList<ArgumentSyntax> args = invocation.ArgumentList.Arguments;
        for (int i = 0; i < args.Count; i++)
        {
            ArgumentSyntax arg = args[i];
            string name = arg.NameColon?.Name.Identifier.ValueText ?? string.Empty;

            if (string.Equals(name, "position", StringComparison.Ordinal))
            {
                if (!TryParseNameofMember(semanticModel, arg.Expression, out positionComponentType, out positionMemberName))
                {
                    diagnostic = Diagnostic.Create(Diagnostics.InvalidEcsSetup, arg.GetLocation(), "position: must be nameof(<Component>.<Member>).");
                    return false;
                }
                continue;
            }

            if (string.Equals(name, "cellSize", StringComparison.Ordinal))
            {
                if (!TryGetConstantInt(semanticModel, arg.Expression, out cellSize))
                {
                    diagnostic = Diagnostic.Create(Diagnostics.InvalidEcsSetup, arg.GetLocation(), "cellSize: must be a compile-time int.");
                    return false;
                }
                continue;
            }

            if (string.Equals(name, "gridSize", StringComparison.Ordinal))
            {
                if (!TryGetConstantInt(semanticModel, arg.Expression, out gridSize))
                {
                    diagnostic = Diagnostic.Create(Diagnostics.InvalidEcsSetup, arg.GetLocation(), "gridSize: must be a compile-time int.");
                    return false;
                }
                continue;
            }

            if (string.Equals(name, "originX", StringComparison.Ordinal))
            {
                if (!TryGetConstantInt(semanticModel, arg.Expression, out originX))
                {
                    diagnostic = Diagnostic.Create(Diagnostics.InvalidEcsSetup, arg.GetLocation(), "originX: must be a compile-time int.");
                    return false;
                }
                continue;
            }

            if (string.Equals(name, "originY", StringComparison.Ordinal))
            {
                if (!TryGetConstantInt(semanticModel, arg.Expression, out originY))
                {
                    diagnostic = Diagnostic.Create(Diagnostics.InvalidEcsSetup, arg.GetLocation(), "originY: must be a compile-time int.");
                    return false;
                }
                continue;
            }

            if (string.IsNullOrEmpty(name))
            {
                // Positional args: (position, cellSize, gridSize, originX, originY)
                if (positionalIndex == 0)
                {
                    if (!TryParseNameofMember(semanticModel, arg.Expression, out positionComponentType, out positionMemberName))
                    {
                        diagnostic = Diagnostic.Create(Diagnostics.InvalidEcsSetup, arg.GetLocation(), "First arg must be nameof(<Component>.<Member>).");
                        return false;
                    }
                }
                else if (positionalIndex == 1)
                {
                    if (!TryGetConstantInt(semanticModel, arg.Expression, out cellSize))
                    {
                        diagnostic = Diagnostic.Create(Diagnostics.InvalidEcsSetup, arg.GetLocation(), "Second arg must be cellSize (compile-time int).");
                        return false;
                    }
                }
                else if (positionalIndex == 2)
                {
                    if (!TryGetConstantInt(semanticModel, arg.Expression, out gridSize))
                    {
                        diagnostic = Diagnostic.Create(Diagnostics.InvalidEcsSetup, arg.GetLocation(), "Third arg must be gridSize (compile-time int).");
                        return false;
                    }
                }
                else if (positionalIndex == 3)
                {
                    if (!TryGetConstantInt(semanticModel, arg.Expression, out originX))
                    {
                        diagnostic = Diagnostic.Create(Diagnostics.InvalidEcsSetup, arg.GetLocation(), "Fourth arg must be originX (compile-time int).");
                        return false;
                    }
                }
                else if (positionalIndex == 4)
                {
                    if (!TryGetConstantInt(semanticModel, arg.Expression, out originY))
                    {
                        diagnostic = Diagnostic.Create(Diagnostics.InvalidEcsSetup, arg.GetLocation(), "Fifth arg must be originY (compile-time int).");
                        return false;
                    }
                }
                positionalIndex++;
            }
        }

        if (positionMemberName == null || positionComponentType == null)
        {
            diagnostic = Diagnostic.Create(Diagnostics.InvalidEcsSetup, invocation.GetLocation(), "Spatial requires position: nameof(<Component>.<Member>).");
            return false;
        }

        if (cellSize <= 0 || gridSize <= 0)
        {
            diagnostic = Diagnostic.Create(Diagnostics.InvalidEcsSetup, invocation.GetLocation(), "Spatial requires cellSize > 0 and gridSize > 0.");
            return false;
        }

        spatial = new SpatialIndexSetup(positionComponentType, positionMemberName, cellSize, gridSize, originX, originY);
        return true;
    }

    private static bool TryGetConstantInt(SemanticModel semanticModel, ExpressionSyntax expression, out int value)
    {
        value = 0;
        Optional<object?> constant = semanticModel.GetConstantValue(expression);
        if (!constant.HasValue || constant.Value is not int i)
        {
            return false;
        }

        value = i;
        return true;
    }

    private static bool TryParseNameofMember(
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        out ITypeSymbol? componentType,
        out string? memberName)
    {
        componentType = null;
        memberName = null;

        if (expression is not InvocationExpressionSyntax invocation)
        {
            return false;
        }

        if (invocation.Expression is not IdentifierNameSyntax identifier ||
            !string.Equals(identifier.Identifier.ValueText, "nameof", StringComparison.Ordinal))
        {
            return false;
        }

        if (invocation.ArgumentList.Arguments.Count != 1)
        {
            return false;
        }

        ExpressionSyntax argExpression = invocation.ArgumentList.Arguments[0].Expression;
        if (argExpression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(memberAccess);
        if (symbolInfo.Symbol is not ISymbol memberSymbol)
        {
            return false;
        }

        componentType = memberSymbol.ContainingType;
        memberName = memberSymbol.Name;
        return componentType != null && !string.IsNullOrWhiteSpace(memberName);
    }

    private static void Emit(SourceProductionContext context, ImmutableArray<WorldSetupParseResult> worldSetups)
    {
        if (worldSetups.IsDefaultOrEmpty)
        {
            return;
        }

        for (int i = 0; i < worldSetups.Length; i++)
        {
            var setup = worldSetups[i];
            if (setup.Diagnostic != null)
            {
                context.ReportDiagnostic(setup.Diagnostic);
                continue;
            }

            if (!setup.IsValid)
            {
                continue;
            }

            EmitWorld(context, setup.WorldType!, setup.Archetypes);
        }
    }

    private static void EmitWorld(SourceProductionContext context, INamedTypeSymbol worldType, ImmutableArray<ArchetypeSetup> archetypes)
    {
        if (archetypes.IsDefaultOrEmpty)
        {
            return;
        }

        var orderedArchetypes = new ArchetypeSetup[archetypes.Length];
        for (int i = 0; i < archetypes.Length; i++)
        {
            orderedArchetypes[i] = archetypes[i];
        }
        Array.Sort(orderedArchetypes, ArchetypeSetupComparer.Instance);

        var kindIds = new Dictionary<string, ushort>(StringComparer.Ordinal);
        ushort nextKindId = 1;
        for (int i = 0; i < orderedArchetypes.Length; i++)
        {
            string kindKey = orderedArchetypes[i].KindType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (!kindIds.ContainsKey(kindKey))
            {
                kindIds[kindKey] = nextKindId++;
            }
        }

        ushort nextArchetypeId = 1;
        var archetypeIds = new Dictionary<string, ushort>(StringComparer.Ordinal);
        for (int i = 0; i < orderedArchetypes.Length; i++)
        {
            string archetypeKey = orderedArchetypes[i].KindType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (!archetypeIds.ContainsKey(archetypeKey))
            {
                archetypeIds[archetypeKey] = nextArchetypeId++;
            }
        }

        EmitWorldPartial(context, worldType, orderedArchetypes, kindIds, archetypeIds);
        for (int i = 0; i < orderedArchetypes.Length; i++)
        {
            EmitPendingSpawnTicket(context, worldType, orderedArchetypes[i]);
            EmitTable(context, worldType, orderedArchetypes[i], kindIds, archetypeIds);
        }
    }

    private static void EmitWorldPartial(
        SourceProductionContext context,
        INamedTypeSymbol worldType,
        ArchetypeSetup[] archetypes,
        Dictionary<string, ushort> kindIds,
        Dictionary<string, ushort> archetypeIds)
    {
        string ns = worldType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : worldType.ContainingNamespace.ToDisplayString();

        string worldName = worldType.Name;
        int totalCapacity = 0;
        bool worldUsesVarHeap = false;
        for (int i = 0; i < archetypes.Length; i++)
        {
            totalCapacity += archetypes[i].Capacity;
            if (!worldUsesVarHeap && ArchetypeUsesVarHeap(archetypes[i]))
            {
                worldUsesVarHeap = true;
            }
        }

        var sb = new StringBuilder(2048);
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using DerpLib.Ecs;");
        sb.AppendLine();
        if (!string.IsNullOrEmpty(ns))
        {
            sb.Append("namespace ").Append(ns).AppendLine(";");
            sb.AppendLine();
        }

        sb.Append("public sealed partial class ").Append(worldName).AppendLine();
        sb.AppendLine(" : IEcsWorld");
        sb.AppendLine("{");
        sb.AppendLine("    public EcsEntityIndex EntityIndex { get; }");
        if (worldUsesVarHeap)
        {
            sb.AppendLine("    public EcsVarHeap VarHeap { get; }");
        }
        sb.AppendLine();

        for (int i = 0; i < archetypes.Length; i++)
        {
            string kindName = archetypes[i].KindType.Name;
            string tableName = kindName + "Table";
            sb.Append("    public ").Append(tableName).Append(' ').Append(kindName).AppendLine(" { get; }");
        }

        sb.AppendLine();
        sb.Append("    public ").Append(worldName).AppendLine("()");
        sb.AppendLine("    {");
        sb.Append("        EntityIndex = new EcsEntityIndex(initialCapacity: ").Append(totalCapacity.ToString(CultureInfo.InvariantCulture)).AppendLine(");");
        if (worldUsesVarHeap)
        {
            sb.AppendLine("        VarHeap = new EcsVarHeap();");
        }
        for (int i = 0; i < archetypes.Length; i++)
        {
            string kindName = archetypes[i].KindType.Name;
            string tableName = kindName + "Table";
            if (ArchetypeUsesVarHeap(archetypes[i]))
            {
                sb.Append("        ").Append(kindName).Append(" = new ").Append(tableName).Append("(EntityIndex, VarHeap);").AppendLine();
            }
            else
            {
                sb.Append("        ").Append(kindName).Append(" = new ").Append(tableName).Append("(EntityIndex);").AppendLine();
            }
        }
        sb.AppendLine("    }");

        sb.AppendLine();
        sb.AppendLine("    public void PlaybackStructuralChanges()");
        sb.AppendLine("    {");
        for (int i = 0; i < archetypes.Length; i++)
        {
            string kindName = archetypes[i].KindType.Name;
            sb.Append("        ").Append(kindName).AppendLine(".Playback();");
        }
        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource(worldName + ".DerpEcsWorld.g.cs", sb.ToString());
    }

    private static void EmitPendingSpawnTicket(
        SourceProductionContext context,
        INamedTypeSymbol worldType,
        ArchetypeSetup archetype)
    {
        string ns = worldType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : worldType.ContainingNamespace.ToDisplayString();

        string kindName = archetype.KindType.Name;
        string ticketName = kindName + "PendingSpawn";

        var sb = new StringBuilder(1024);
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        if (!string.IsNullOrEmpty(ns))
        {
            sb.Append("namespace ").Append(ns).AppendLine(";");
            sb.AppendLine();
        }

        sb.Append("public ref struct ").Append(ticketName).AppendLine();
        sb.AppendLine("{");
        for (int i = 0; i < archetype.Components.Length; i++)
        {
            ITypeSymbol componentType = archetype.Components[i];
            string componentTypeName = componentType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string fieldName = "_" + ToLowerCamel(TrimComponentSuffix(componentType.Name));
            sb.Append("    private readonly ").Append(componentTypeName).Append("[] ").Append(fieldName).AppendLine(";");
        }
        sb.AppendLine("    private readonly int _index;");
        sb.AppendLine();

        sb.Append("    internal ").Append(ticketName).AppendLine("(");
        for (int i = 0; i < archetype.Components.Length; i++)
        {
            ITypeSymbol componentType = archetype.Components[i];
            string componentTypeName = componentType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string paramName = ToLowerCamel(TrimComponentSuffix(componentType.Name));
            sb.Append("        ").Append(componentTypeName).Append("[] ").Append(paramName).AppendLine(",");
        }
        sb.AppendLine("        int index)");
        sb.AppendLine("    {");
        for (int i = 0; i < archetype.Components.Length; i++)
        {
            string name = ToLowerCamel(TrimComponentSuffix(archetype.Components[i].Name));
            string fieldName = "_" + name;
            sb.Append("        ").Append(fieldName).Append(" = ").Append(name).AppendLine(";");
        }
        sb.AppendLine("        _index = index;");
        sb.AppendLine("    }");
        sb.AppendLine();

        for (int i = 0; i < archetype.Components.Length; i++)
        {
            ITypeSymbol componentType = archetype.Components[i];
            string componentTypeName = componentType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string accessorName = TrimComponentSuffix(componentType.Name);
            string fieldName = "_" + ToLowerCamel(accessorName);
            sb.Append("    public ref ").Append(componentTypeName).Append(' ').Append(accessorName).Append(" => ref ").Append(fieldName).AppendLine("[_index];");
        }
        sb.AppendLine("}");

        context.AddSource(ticketName + ".g.cs", sb.ToString());
    }

    private static void EmitTable(
        SourceProductionContext context,
        INamedTypeSymbol worldType,
        ArchetypeSetup archetype,
        Dictionary<string, ushort> kindIds,
        Dictionary<string, ushort> archetypeIds)
    {
        string ns = worldType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : worldType.ContainingNamespace.ToDisplayString();

        string kindName = archetype.KindType.Name;
        string tableName = kindName + "Table";
        string ticketName = kindName + "PendingSpawn";

        string kindKey = archetype.KindType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        ushort kindId = kindIds[kindKey];
        ushort archetypeId = archetypeIds[kindKey];

        int defaultQueueCapacity = archetype.Capacity / 4;
        if (defaultQueueCapacity < 64)
        {
            defaultQueueCapacity = 64;
        }

        int spawnQueueCapacity = archetype.SpawnQueueCapacityOverride != 0
            ? archetype.SpawnQueueCapacityOverride
            : defaultQueueCapacity;

        int destroyQueueCapacity = archetype.DestroyQueueCapacityOverride != 0
            ? archetype.DestroyQueueCapacityOverride
            : defaultQueueCapacity;

        bool tableUsesVarHeap = ArchetypeUsesVarHeap(archetype);
        bool[] componentHasResizableProxy = new bool[archetype.Components.Length];
        List<IFieldSymbol>[] componentFields = new List<IFieldSymbol>[archetype.Components.Length];
        for (int i = 0; i < archetype.Components.Length; i++)
        {
            componentFields[i] = new List<IFieldSymbol>();
            ITypeSymbol componentType = archetype.Components[i];
            if (componentType is not INamedTypeSymbol namedComponent)
            {
                continue;
            }

            foreach (ISymbol member in namedComponent.GetMembers())
            {
                if (member is not IFieldSymbol field)
                {
                    continue;
                }

                if (field.IsStatic || field.IsConst)
                {
                    continue;
                }

                if (field.DeclaredAccessibility == Accessibility.Private)
                {
                    continue;
                }

                componentFields[i].Add(field);
                if (!componentHasResizableProxy[i] && TryGetListHandleElementType(field.Type, out _))
                {
                    componentHasResizableProxy[i] = true;
                }
            }
        }

        var sb = new StringBuilder(8192);
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        if (tableUsesVarHeap)
        {
            sb.AppendLine("using System.Runtime.CompilerServices;");
        }
        sb.AppendLine("using DerpLib.Ecs;");
        sb.AppendLine();
        if (!string.IsNullOrEmpty(ns))
        {
            sb.Append("namespace ").Append(ns).AppendLine(";");
            sb.AppendLine();
        }

        sb.Append("public sealed class ").Append(tableName).AppendLine();
        sb.AppendLine("{");
        sb.Append("    public const ushort ArchetypeId = ").Append(archetypeId.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
        sb.Append("    public const ushort KindId = ").Append(kindId.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
        sb.AppendLine();
        sb.Append("    private const int TableCapacity = ").Append(archetype.Capacity.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
        sb.Append("    private const int SpawnQueueCapacity = ").Append(spawnQueueCapacity.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
        sb.Append("    private const int DestroyQueueCapacity = ").Append(destroyQueueCapacity.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
        sb.AppendLine();
        sb.AppendLine("    private readonly EcsEntityIndex _entityIndex;");
        if (tableUsesVarHeap)
        {
            sb.AppendLine("    private readonly EcsVarHeap _varHeap;");
        }
        sb.AppendLine("    private readonly EntityHandle[] _entities;");

        for (int i = 0; i < archetype.Components.Length; i++)
        {
            ITypeSymbol componentType = archetype.Components[i];
            string componentTypeName = componentType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string fieldName = "_" + ToLowerCamel(TrimComponentSuffix(componentType.Name));
            sb.Append("    private readonly ").Append(componentTypeName).Append("[] ").Append(fieldName).AppendLine(";");
        }

        if (archetype.Spatial.HasValue)
        {
            SpatialIndexSetup spatial = archetype.Spatial.Value;
            sb.AppendLine();
            sb.Append("    public const int SpatialCellSize = ").Append(spatial.CellSize.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
            sb.Append("    public const int SpatialGridSize = ").Append(spatial.GridSize.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
            sb.Append("    public const int SpatialOriginX = ").Append(spatial.OriginX.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
            sb.Append("    public const int SpatialOriginY = ").Append(spatial.OriginY.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
            sb.AppendLine("    public const int SpatialTotalCells = SpatialGridSize * SpatialGridSize;");
            sb.AppendLine();
            sb.AppendLine("    private readonly int[] _cellStart;");
            sb.AppendLine("    private readonly int[] _cellCount;");
            sb.AppendLine("    private readonly int[] _cellWrite;");
            sb.AppendLine("    private readonly int[] _sortedRow;");
        }

        sb.AppendLine();
        sb.AppendLine("    private readonly EntityHandle[] _destroyQueue;");
        sb.AppendLine("    private int _destroyCount;");
        sb.AppendLine("    public int DroppedDestroyCount { get; private set; }");
        sb.AppendLine();

        for (int i = 0; i < archetype.Components.Length; i++)
        {
            ITypeSymbol componentType = archetype.Components[i];
            string componentTypeName = componentType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string fieldName = "_pending" + TrimComponentSuffix(componentType.Name);
            sb.Append("    private readonly ").Append(componentTypeName).Append("[] ").Append(fieldName).AppendLine(";");
        }
        sb.AppendLine("    private int _spawnCount;");
        sb.AppendLine("    public int DroppedSpawnCount { get; private set; }");
        sb.AppendLine();

        sb.AppendLine("    public int Count { get; private set; }");
        sb.AppendLine("    public int Capacity => TableCapacity;");
        sb.AppendLine();
        if (tableUsesVarHeap)
        {
            sb.Append("    public ").Append(tableName).AppendLine("(EcsEntityIndex entityIndex, EcsVarHeap varHeap)");
        }
        else
        {
            sb.Append("    public ").Append(tableName).AppendLine("(EcsEntityIndex entityIndex)");
        }
        sb.AppendLine("    {");
        sb.AppendLine("        _entityIndex = entityIndex ?? throw new ArgumentNullException(nameof(entityIndex));");
        if (tableUsesVarHeap)
        {
            sb.AppendLine("        _varHeap = varHeap ?? throw new ArgumentNullException(nameof(varHeap));");
        }
        sb.AppendLine("        _entities = new EntityHandle[TableCapacity];");
        for (int i = 0; i < archetype.Components.Length; i++)
        {
            string fieldName = "_" + ToLowerCamel(TrimComponentSuffix(archetype.Components[i].Name));
            string componentTypeName = archetype.Components[i].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            sb.Append("        ").Append(fieldName).Append(" = new ").Append(componentTypeName).AppendLine("[TableCapacity];");
        }
        if (archetype.Spatial.HasValue)
        {
            sb.AppendLine("        _cellStart = new int[SpatialTotalCells];");
            sb.AppendLine("        _cellCount = new int[SpatialTotalCells];");
            sb.AppendLine("        _cellWrite = new int[SpatialTotalCells];");
            sb.AppendLine("        _sortedRow = new int[TableCapacity];");
        }
        sb.AppendLine("        _destroyQueue = new EntityHandle[DestroyQueueCapacity];");
        for (int i = 0; i < archetype.Components.Length; i++)
        {
            ITypeSymbol componentType = archetype.Components[i];
            string componentTypeName = componentType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string fieldName = "_pending" + TrimComponentSuffix(componentType.Name);
            sb.Append("        ").Append(fieldName).Append(" = new ").Append(componentTypeName).AppendLine("[SpawnQueueCapacity];");
        }
        sb.AppendLine("        Count = 0;");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    public ref readonly EntityHandle Entity(int row)");
        sb.AppendLine("    {");
        sb.AppendLine("        return ref _entities[row];");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    public bool TryGetRow(EntityHandle entity, out int row)");
        sb.AppendLine("    {");
        sb.AppendLine("        row = -1;");
        sb.AppendLine("        if (!_entityIndex.TryGetLocation(entity, out EntityLocation loc))");
        sb.AppendLine("        {");
        sb.AppendLine("            return false;");
        sb.AppendLine("        }");
        sb.AppendLine("        if (loc.ArchetypeId != ArchetypeId)");
        sb.AppendLine("        {");
        sb.AppendLine("            return false;");
        sb.AppendLine("        }");
        sb.AppendLine("        row = loc.Row;");
        sb.AppendLine("        return (uint)row < (uint)Count;");
        sb.AppendLine("    }");
        sb.AppendLine();

        if (archetype.Spatial.HasValue)
        {
            EmitSpatialIndex(sb, archetype);
        }

        sb.AppendLine("    private EntityHandle SpawnInternal()");
        sb.AppendLine("    {");
        sb.AppendLine("        int row = Count;");
        sb.AppendLine("        if ((uint)row >= (uint)TableCapacity)");
        sb.AppendLine("        {");
        sb.AppendLine("            return EntityHandle.Invalid;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        Count = row + 1;");
        sb.AppendLine("        var entity = _entityIndex.Allocate(KindId, new EntityLocation(ArchetypeId, row));");
        sb.AppendLine("        _entities[row] = entity;");
        for (int i = 0; i < archetype.Components.Length; i++)
        {
            string fieldName = "_" + ToLowerCamel(TrimComponentSuffix(archetype.Components[i].Name));
            sb.Append("        ").Append(fieldName).AppendLine("[row] = default;");
        }
        sb.AppendLine("        return entity;");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    private bool DestroyInternal(EntityHandle entity)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (!TryGetRow(entity, out int row))");
        sb.AppendLine("        {");
        sb.AppendLine("            return false;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        int lastRow = Count - 1;");
        sb.AppendLine("        if (row != lastRow)");
        sb.AppendLine("        {");
        sb.AppendLine("            // Move last row into the removed slot.");
        sb.AppendLine("            _entities[row] = _entities[lastRow];");
        for (int i = 0; i < archetype.Components.Length; i++)
        {
            string fieldName = "_" + ToLowerCamel(TrimComponentSuffix(archetype.Components[i].Name));
            sb.Append("            ").Append(fieldName).Append("[row] = ").Append(fieldName).AppendLine("[lastRow];");
        }
        sb.AppendLine();
        sb.AppendLine("            // Update moved entity location.");
        sb.AppendLine("            _entityIndex.SetLocationUnchecked(_entities[row].RawId, new EntityLocation(ArchetypeId, row));");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        Count = lastRow;");
        sb.AppendLine("        _entityIndex.Free(entity);");
        sb.AppendLine("        return true;");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    public bool QueueDestroy(EntityHandle entity)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (_destroyCount >= DestroyQueueCapacity)");
        sb.AppendLine("        {");
        sb.AppendLine("            DroppedDestroyCount++;");
        sb.AppendLine("            return false;");
        sb.AppendLine("        }");
        sb.AppendLine("        _destroyQueue[_destroyCount++] = entity;");
        sb.AppendLine("        return true;");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.Append("    public bool TryQueueSpawn(out ").Append(ticketName).AppendLine(" spawn)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (_spawnCount >= SpawnQueueCapacity)");
        sb.AppendLine("        {");
        sb.AppendLine("            DroppedSpawnCount++;");
        sb.AppendLine("            spawn = default;");
        sb.AppendLine("            return false;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        int index = _spawnCount++;");
        sb.Append("        spawn = new ").Append(ticketName).Append('(');
        for (int i = 0; i < archetype.Components.Length; i++)
        {
            if (i != 0)
            {
                sb.Append(", ");
            }
            sb.Append("_pending").Append(TrimComponentSuffix(archetype.Components[i].Name));
        }
        sb.AppendLine(", index);");
        sb.AppendLine("        return true;");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    public void Playback()");
        sb.AppendLine("    {");
        sb.AppendLine("        for (int i = 0; i < _destroyCount; i++)");
        sb.AppendLine("        {");
        sb.AppendLine("            DestroyInternal(_destroyQueue[i]);");
        sb.AppendLine("        }");
        sb.AppendLine("        _destroyCount = 0;");
        sb.AppendLine();
        sb.AppendLine("        for (int i = 0; i < _spawnCount; i++)");
        sb.AppendLine("        {");
        sb.AppendLine("            EntityHandle entity = SpawnInternal();");
        sb.AppendLine("            if (!entity.IsValid)");
        sb.AppendLine("            {");
        sb.AppendLine("                continue;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            int row = Count - 1;");
        for (int i = 0; i < archetype.Components.Length; i++)
        {
            string liveFieldName = "_" + ToLowerCamel(TrimComponentSuffix(archetype.Components[i].Name));
            string pendingFieldName = "_pending" + TrimComponentSuffix(archetype.Components[i].Name);
            sb.Append("            ").Append(liveFieldName).Append("[row] = ").Append(pendingFieldName).AppendLine("[i];");
        }
        sb.AppendLine("        }");
        sb.AppendLine("        _spawnCount = 0;");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    public void ResetDiagnostics()");
        sb.AppendLine("    {");
        sb.AppendLine("        DroppedDestroyCount = 0;");
        sb.AppendLine("        DroppedSpawnCount = 0;");
        sb.AppendLine("    }");
        sb.AppendLine();

        if (tableUsesVarHeap)
        {
            sb.Append("    public ").Append(kindName).AppendLine("Row Row(int row)");
            sb.AppendLine("    {");
            sb.Append("        return new ").Append(kindName).Append("Row(_varHeap, _entities");
            for (int i = 0; i < archetype.Components.Length; i++)
            {
                string tableFieldName = "_" + ToLowerCamel(TrimComponentSuffix(archetype.Components[i].Name));
                sb.Append(", ").Append(tableFieldName);
            }
            sb.AppendLine(", row);");
            sb.AppendLine("    }");
            sb.AppendLine();

            sb.Append("    public readonly ref struct ").Append(kindName).AppendLine("Row");
            sb.AppendLine("    {");
            sb.AppendLine("        private readonly EcsVarHeap _varHeap;");
            sb.AppendLine("        private readonly EntityHandle[] _entities;");
            for (int i = 0; i < archetype.Components.Length; i++)
            {
                ITypeSymbol componentType = archetype.Components[i];
                string componentTypeName = componentType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                string accessorName = TrimComponentSuffix(componentType.Name);
                string rowFieldName = "_" + ToLowerCamel(accessorName);
                sb.Append("        private readonly ").Append(componentTypeName).Append("[] ").Append(rowFieldName).AppendLine(";");
            }
            sb.AppendLine("        private readonly int _row;");
            sb.AppendLine();
            sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.Append("        internal ").Append(kindName).AppendLine("Row(");
            sb.AppendLine("            EcsVarHeap varHeap,");
            sb.AppendLine("            EntityHandle[] entities,");
            for (int i = 0; i < archetype.Components.Length; i++)
            {
                ITypeSymbol componentType = archetype.Components[i];
                string componentTypeName = componentType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                string accessorName = TrimComponentSuffix(componentType.Name);
                string paramName = ToLowerCamel(accessorName);
                sb.Append("            ").Append(componentTypeName).Append("[] ").Append(paramName).AppendLine(",");
            }
            sb.AppendLine("            int row)");
            sb.AppendLine("        {");
            sb.AppendLine("            _varHeap = varHeap;");
            sb.AppendLine("            _entities = entities;");
            for (int i = 0; i < archetype.Components.Length; i++)
            {
                string accessorName = TrimComponentSuffix(archetype.Components[i].Name);
                string rowFieldName = "_" + ToLowerCamel(accessorName);
                string paramName = ToLowerCamel(accessorName);
                sb.Append("            ").Append(rowFieldName).Append(" = ").Append(paramName).AppendLine(";");
            }
            sb.AppendLine("            _row = row;");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        public ref readonly EntityHandle Entity");
            sb.AppendLine("        {");
            sb.AppendLine("            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("            get => ref _entities[_row];");
            sb.AppendLine("        }");
            sb.AppendLine();

            for (int i = 0; i < archetype.Components.Length; i++)
            {
                ITypeSymbol componentType = archetype.Components[i];
                string componentTypeName = componentType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                string accessorName = TrimComponentSuffix(componentType.Name);
                string rowFieldName = "_" + ToLowerCamel(accessorName);

                if (componentHasResizableProxy[i])
                {
                    string proxyName = accessorName + "Proxy";
                    sb.Append("        public ").Append(proxyName).Append(' ').Append(accessorName).AppendLine();
                    sb.AppendLine("        {");
                    sb.AppendLine("            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    sb.Append("            get => new ").Append(proxyName).AppendLine("(_varHeap, " + rowFieldName + ", _row);");
                    sb.AppendLine("        }");
                    sb.AppendLine();
                }
                else
                {
                    sb.Append("        public ref ").Append(componentTypeName).Append(' ').Append(accessorName).AppendLine();
                    sb.AppendLine("        {");
                    sb.AppendLine("            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    sb.Append("            get => ref ").Append(rowFieldName).AppendLine("[_row];");
                    sb.AppendLine("        }");
                    sb.AppendLine();
                }
            }

            sb.AppendLine("    }");
            sb.AppendLine();

            for (int i = 0; i < archetype.Components.Length; i++)
            {
                if (!componentHasResizableProxy[i])
                {
                    continue;
                }

                ITypeSymbol componentType = archetype.Components[i];
                string componentTypeName = componentType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                string accessorName = TrimComponentSuffix(componentType.Name);
                string proxyName = accessorName + "Proxy";

                sb.Append("    public readonly ref struct ").Append(proxyName).AppendLine();
                sb.AppendLine("    {");
                sb.AppendLine("        private readonly EcsVarHeap _varHeap;");
                sb.Append("        private readonly ").Append(componentTypeName).AppendLine("[] _components;");
                sb.AppendLine("        private readonly int _row;");
                sb.AppendLine();
                sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                sb.Append("        internal ").Append(proxyName).AppendLine("(EcsVarHeap varHeap, " + componentTypeName + "[] components, int row)");
                sb.AppendLine("        {");
                sb.AppendLine("            _varHeap = varHeap;");
                sb.AppendLine("            _components = components;");
                sb.AppendLine("            _row = row;");
                sb.AppendLine("        }");
                sb.AppendLine();

                List<IFieldSymbol> fields = componentFields[i];
                for (int f = 0; f < fields.Count; f++)
                {
                    IFieldSymbol field = fields[f];
                    string fieldTypeName = field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    string fieldName = field.Name;

                    if (TryGetListHandleElementType(field.Type, out ITypeSymbol? elementType))
                    {
                        string elementTypeName = elementType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        sb.Append("        public ResizableReadOnlyView<").Append(elementTypeName).Append("> ").Append(fieldName).AppendLine();
                        sb.AppendLine("        {");
                        sb.AppendLine("            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                        sb.AppendLine("            get");
                        sb.AppendLine("            {");
                        sb.Append("                ref ").Append(componentTypeName).AppendLine(" component = ref _components[_row];");
                        sb.Append("                ref ListHandle<").Append(elementTypeName).Append("> handle = ref component.").Append(fieldName).AppendLine(";");
                        sb.Append("                return new ResizableReadOnlyView<").Append(elementTypeName).AppendLine(">(_varHeap.Bytes, in handle);");
                        sb.AppendLine("            }");
                        sb.AppendLine("        }");
                        sb.AppendLine();
                    }
                    else
                    {
                        string refPrefix = field.IsReadOnly ? "ref readonly " : "ref ";
                        sb.Append("        public ").Append(refPrefix).Append(fieldTypeName).Append(' ').Append(fieldName).AppendLine();
                        sb.AppendLine("        {");
                        sb.AppendLine("            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                        sb.AppendLine("            get");
                        sb.AppendLine("            {");
                        sb.Append("                ref ").Append(componentTypeName).AppendLine(" component = ref _components[_row];");
                        sb.Append("                return ref component.").Append(fieldName).AppendLine(";");
                        sb.AppendLine("            }");
                        sb.AppendLine("        }");
                        sb.AppendLine();
                    }
                }

                sb.AppendLine("    }");
                sb.AppendLine();
            }
        }

        for (int i = 0; i < archetype.Components.Length; i++)
        {
            ITypeSymbol componentType = archetype.Components[i];
            string componentTypeName = componentType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string accessorName = TrimComponentSuffix(componentType.Name);
            string fieldName = "_" + ToLowerCamel(accessorName);
            sb.Append("    public ref ").Append(componentTypeName).Append(' ').Append(accessorName).AppendLine("(int row)");
            sb.AppendLine("    {");
            sb.AppendLine("        return ref " + fieldName + "[row];");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        EmitQueries(sb, archetype);

        sb.AppendLine("}");

        context.AddSource(tableName + ".g.cs", sb.ToString());
    }

    private static void EmitSpatialIndex(StringBuilder sb, ArchetypeSetup archetype)
    {
        SpatialIndexSetup spatial = archetype.Spatial!.Value;
        string componentFieldName = "_" + ToLowerCamel(TrimComponentSuffix(spatial.PositionComponentType.Name));
        string memberName = spatial.PositionMemberName;

        sb.AppendLine("    public void RebuildSpatialIndex()");
        sb.AppendLine("    {");
        sb.AppendLine("        Array.Clear(_cellCount, 0, _cellCount.Length);");
        sb.AppendLine();
        sb.AppendLine("        for (int row = 0; row < Count; row++)");
        sb.AppendLine("        {");
        sb.Append("            var pos = ").Append(componentFieldName).Append("[row].").Append(memberName).AppendLine(";");
        sb.AppendLine("            int cell = ComputeCellIndex(pos.X.Raw, pos.Y.Raw);");
        sb.AppendLine("            _cellCount[cell]++;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        int sum = 0;");
        sb.AppendLine("        for (int cell = 0; cell < SpatialTotalCells; cell++)");
        sb.AppendLine("        {");
        sb.AppendLine("            int count = _cellCount[cell];");
        sb.AppendLine("            _cellStart[cell] = sum;");
        sb.AppendLine("            _cellWrite[cell] = sum;");
        sb.AppendLine("            sum += count;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        for (int row = 0; row < Count; row++)");
        sb.AppendLine("        {");
        sb.Append("            var pos = ").Append(componentFieldName).Append("[row].").Append(memberName).AppendLine(";");
        sb.AppendLine("            int cell = ComputeCellIndex(pos.X.Raw, pos.Y.Raw);");
        sb.AppendLine("            int writeIndex = _cellWrite[cell]++;");
        sb.AppendLine("            _sortedRow[writeIndex] = row;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    private static int ComputeCellIndex(long worldXRaw, long worldYRaw)");
        sb.AppendLine("    {");
        sb.AppendLine("        const long cellSizeRaw = (long)SpatialCellSize << global::FixedMath.Fixed64.FractionalBits;");
        sb.AppendLine("        long relX = worldXRaw - ((long)SpatialOriginX << global::FixedMath.Fixed64.FractionalBits);");
        sb.AppendLine("        long relY = worldYRaw - ((long)SpatialOriginY << global::FixedMath.Fixed64.FractionalBits);");
        sb.AppendLine("        int cellX = FloorDiv(relX, cellSizeRaw);");
        sb.AppendLine("        int cellY = FloorDiv(relY, cellSizeRaw);");
        sb.AppendLine("        if (cellX < 0) cellX = 0;");
        sb.AppendLine("        if (cellY < 0) cellY = 0;");
        sb.AppendLine("        int max = SpatialGridSize - 1;");
        sb.AppendLine("        if (cellX > max) cellX = max;");
        sb.AppendLine("        if (cellY > max) cellY = max;");
        sb.AppendLine("        return cellX + cellY * SpatialGridSize;");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    private static int FloorDiv(long numerator, long denominator)");
        sb.AppendLine("    {");
        sb.AppendLine("        long q = numerator / denominator;");
        sb.AppendLine("        long r = numerator % denominator;");
        sb.AppendLine("        if (r != 0 && numerator < 0)");
        sb.AppendLine("        {");
        sb.AppendLine("            q--;");
        sb.AppendLine("        }");
        sb.AppendLine("        return (int)q;");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void EmitQueries(StringBuilder sb, ArchetypeSetup archetype)
    {
        if (archetype.Queries.IsDefaultOrEmpty)
        {
            return;
        }

        for (int i = 0; i < archetype.Queries.Length; i++)
        {
            QuerySetup query = archetype.Queries[i];
            string componentFieldName = "_" + ToLowerCamel(TrimComponentSuffix(query.PositionComponentType.Name));
            string memberName = query.PositionMemberName;
            bool useSpatial =
                archetype.Spatial.HasValue &&
                SymbolEqualityComparer.Default.Equals(archetype.Spatial.Value.PositionComponentType, query.PositionComponentType) &&
                string.Equals(archetype.Spatial.Value.PositionMemberName, query.PositionMemberName, StringComparison.Ordinal);

            if (query.Kind == QueryKind.Radius)
            {
                sb.Append("    public const int QueryRadiusMaxResults = ").Append(query.MaxResults.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
                sb.AppendLine();
                sb.AppendLine("    public int QueryRadius(in global::FixedMath.Fixed64Vec2 center, global::FixedMath.Fixed64 radius, Span<EntityHandle> results)");
                sb.AppendLine("    {");
                sb.AppendLine("        var radiusSq = radius * radius;");
                sb.AppendLine("        int writeIndex = 0;");
                if (useSpatial)
                {
                    sb.AppendLine("        var min = new global::FixedMath.Fixed64Vec2(center.X - radius, center.Y - radius);");
                    sb.AppendLine("        var max = new global::FixedMath.Fixed64Vec2(center.X + radius, center.Y + radius);");
                    sb.AppendLine("        const long cellSizeRaw = (long)SpatialCellSize << global::FixedMath.Fixed64.FractionalBits;");
                    sb.AppendLine("        int minCellX = FloorDiv(min.X.Raw - ((long)SpatialOriginX << global::FixedMath.Fixed64.FractionalBits), cellSizeRaw);");
                    sb.AppendLine("        int minCellY = FloorDiv(min.Y.Raw - ((long)SpatialOriginY << global::FixedMath.Fixed64.FractionalBits), cellSizeRaw);");
                    sb.AppendLine("        int maxCellX = FloorDiv(max.X.Raw - ((long)SpatialOriginX << global::FixedMath.Fixed64.FractionalBits), cellSizeRaw);");
                    sb.AppendLine("        int maxCellY = FloorDiv(max.Y.Raw - ((long)SpatialOriginY << global::FixedMath.Fixed64.FractionalBits), cellSizeRaw);");
                    sb.AppendLine("        int clampMax = SpatialGridSize - 1;");
                    sb.AppendLine("        if (maxCellX < 0 || maxCellY < 0 || minCellX > clampMax || minCellY > clampMax)");
                    sb.AppendLine("        {");
                    sb.AppendLine("            return 0;");
                    sb.AppendLine("        }");
                    sb.AppendLine("        if (minCellX < 0) minCellX = 0;");
                    sb.AppendLine("        if (minCellY < 0) minCellY = 0;");
                    sb.AppendLine("        if (maxCellX > clampMax) maxCellX = clampMax;");
                    sb.AppendLine("        if (maxCellY > clampMax) maxCellY = clampMax;");
                    sb.AppendLine("        for (int cellY = minCellY; cellY <= maxCellY; cellY++)");
                    sb.AppendLine("        {");
                    sb.AppendLine("            int rowBase = cellY * SpatialGridSize;");
                    sb.AppendLine("            for (int cellX = minCellX; cellX <= maxCellX; cellX++)");
                    sb.AppendLine("            {");
                    sb.AppendLine("                int cell = rowBase + cellX;");
                    sb.AppendLine("                int start = _cellStart[cell];");
                    sb.AppendLine("                int count = _cellCount[cell];");
                    sb.AppendLine("                int end = start + count;");
                    sb.AppendLine("                for (int iRow = start; iRow < end; iRow++)");
                    sb.AppendLine("                {");
                    sb.AppendLine("                    int row = _sortedRow[iRow];");
                    sb.Append("                    var pos = ").Append(componentFieldName).Append("[row].").Append(memberName).AppendLine(";");
                    sb.AppendLine("                    var dx = pos.X - center.X;");
                    sb.AppendLine("                    var dy = pos.Y - center.Y;");
                    sb.AppendLine("                    var distSq = dx * dx + dy * dy;");
                    sb.AppendLine("                    if (distSq > radiusSq)");
                    sb.AppendLine("                    {");
                    sb.AppendLine("                        continue;");
                    sb.AppendLine("                    }");
                    sb.AppendLine("                    if ((uint)writeIndex >= (uint)results.Length)");
                    sb.AppendLine("                    {");
                    sb.AppendLine("                        return writeIndex;");
                    sb.AppendLine("                    }");
                    sb.AppendLine("                    results[writeIndex++] = _entities[row];");
                    sb.AppendLine("                }");
                    sb.AppendLine("            }");
                    sb.AppendLine("        }");
                }
                else
                {
                    sb.AppendLine("        for (int row = 0; row < Count; row++)");
                    sb.AppendLine("        {");
                    sb.Append("            var pos = ").Append(componentFieldName).Append("[row].").Append(memberName).AppendLine(";");
                    sb.AppendLine("            var dx = pos.X - center.X;");
                    sb.AppendLine("            var dy = pos.Y - center.Y;");
                    sb.AppendLine("            var distSq = dx * dx + dy * dy;");
                    sb.AppendLine("            if (distSq > radiusSq)");
                    sb.AppendLine("            {");
                    sb.AppendLine("                continue;");
                    sb.AppendLine("            }");
                    sb.AppendLine("            if ((uint)writeIndex >= (uint)results.Length)");
                    sb.AppendLine("            {");
                    sb.AppendLine("                break;");
                    sb.AppendLine("            }");
                    sb.AppendLine("            results[writeIndex++] = _entities[row];");
                    sb.AppendLine("        }");
                }
                sb.AppendLine("        return writeIndex;");
                sb.AppendLine("    }");
                sb.AppendLine();
            }
            else if (query.Kind == QueryKind.Aabb)
            {
                sb.Append("    public const int QueryAabbMaxResults = ").Append(query.MaxResults.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
                sb.AppendLine();
                sb.AppendLine("    public int QueryAabb(in global::FixedMath.Fixed64Vec2 min, in global::FixedMath.Fixed64Vec2 max, Span<EntityHandle> results)");
                sb.AppendLine("    {");
                sb.AppendLine("        int writeIndex = 0;");
                if (useSpatial)
                {
                    sb.AppendLine("        const long cellSizeRaw = (long)SpatialCellSize << global::FixedMath.Fixed64.FractionalBits;");
                    sb.AppendLine("        int minCellX = FloorDiv(min.X.Raw - ((long)SpatialOriginX << global::FixedMath.Fixed64.FractionalBits), cellSizeRaw);");
                    sb.AppendLine("        int minCellY = FloorDiv(min.Y.Raw - ((long)SpatialOriginY << global::FixedMath.Fixed64.FractionalBits), cellSizeRaw);");
                    sb.AppendLine("        int maxCellX = FloorDiv(max.X.Raw - ((long)SpatialOriginX << global::FixedMath.Fixed64.FractionalBits), cellSizeRaw);");
                    sb.AppendLine("        int maxCellY = FloorDiv(max.Y.Raw - ((long)SpatialOriginY << global::FixedMath.Fixed64.FractionalBits), cellSizeRaw);");
                    sb.AppendLine("        int clampMax = SpatialGridSize - 1;");
                    sb.AppendLine("        if (maxCellX < 0 || maxCellY < 0 || minCellX > clampMax || minCellY > clampMax)");
                    sb.AppendLine("        {");
                    sb.AppendLine("            return 0;");
                    sb.AppendLine("        }");
                    sb.AppendLine("        if (minCellX < 0) minCellX = 0;");
                    sb.AppendLine("        if (minCellY < 0) minCellY = 0;");
                    sb.AppendLine("        if (maxCellX > clampMax) maxCellX = clampMax;");
                    sb.AppendLine("        if (maxCellY > clampMax) maxCellY = clampMax;");
                    sb.AppendLine("        for (int cellY = minCellY; cellY <= maxCellY; cellY++)");
                    sb.AppendLine("        {");
                    sb.AppendLine("            int rowBase = cellY * SpatialGridSize;");
                    sb.AppendLine("            for (int cellX = minCellX; cellX <= maxCellX; cellX++)");
                    sb.AppendLine("            {");
                    sb.AppendLine("                int cell = rowBase + cellX;");
                    sb.AppendLine("                int start = _cellStart[cell];");
                    sb.AppendLine("                int count = _cellCount[cell];");
                    sb.AppendLine("                int end = start + count;");
                    sb.AppendLine("                for (int iRow = start; iRow < end; iRow++)");
                    sb.AppendLine("                {");
                    sb.AppendLine("                    int row = _sortedRow[iRow];");
                    sb.Append("                    var pos = ").Append(componentFieldName).Append("[row].").Append(memberName).AppendLine(";");
                    sb.AppendLine("                    if (pos.X < min.X || pos.X > max.X || pos.Y < min.Y || pos.Y > max.Y)");
                    sb.AppendLine("                    {");
                    sb.AppendLine("                        continue;");
                    sb.AppendLine("                    }");
                    sb.AppendLine("                    if ((uint)writeIndex >= (uint)results.Length)");
                    sb.AppendLine("                    {");
                    sb.AppendLine("                        return writeIndex;");
                    sb.AppendLine("                    }");
                    sb.AppendLine("                    results[writeIndex++] = _entities[row];");
                    sb.AppendLine("                }");
                    sb.AppendLine("            }");
                    sb.AppendLine("        }");
                }
                else
                {
                    sb.AppendLine("        for (int row = 0; row < Count; row++)");
                    sb.AppendLine("        {");
                    sb.Append("            var pos = ").Append(componentFieldName).Append("[row].").Append(memberName).AppendLine(";");
                    sb.AppendLine("            if (pos.X < min.X || pos.X > max.X || pos.Y < min.Y || pos.Y > max.Y)");
                    sb.AppendLine("            {");
                    sb.AppendLine("                continue;");
                    sb.AppendLine("            }");
                    sb.AppendLine("            if ((uint)writeIndex >= (uint)results.Length)");
                    sb.AppendLine("            {");
                    sb.AppendLine("                break;");
                    sb.AppendLine("            }");
                    sb.AppendLine("            results[writeIndex++] = _entities[row];");
                    sb.AppendLine("        }");
                }
                sb.AppendLine("        return writeIndex;");
                sb.AppendLine("    }");
                sb.AppendLine();
            }
        }
    }

    private static bool ArchetypeUsesVarHeap(ArchetypeSetup archetype)
    {
        for (int i = 0; i < archetype.Components.Length; i++)
        {
            if (ComponentUsesVarHeap(archetype.Components[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ComponentUsesVarHeap(ITypeSymbol componentType)
    {
        if (componentType is not INamedTypeSymbol namedComponent)
        {
            return false;
        }

        foreach (ISymbol member in namedComponent.GetMembers())
        {
            if (member is not IFieldSymbol field)
            {
                continue;
            }

            if (field.IsStatic || field.IsConst)
            {
                continue;
            }

            if (TryGetListHandleElementType(field.Type, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetListHandleElementType(ITypeSymbol type, out ITypeSymbol? elementType)
    {
        elementType = null;
        if (type is not INamedTypeSymbol named || named.TypeArguments.Length != 1)
        {
            return false;
        }

        string listDefinition = named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (!string.Equals(listDefinition, ListHandleFqn, StringComparison.Ordinal))
        {
            return false;
        }

        elementType = named.TypeArguments[0];
        return true;
    }

    private static string TrimComponentSuffix(string typeName)
    {
        const string suffix = "Component";
        if (typeName.EndsWith(suffix, StringComparison.Ordinal))
        {
            return typeName.Substring(0, typeName.Length - suffix.Length);
        }

        return typeName;
    }

    private static string ToLowerCamel(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        if (name.Length == 1)
        {
            return name.ToLowerInvariant();
        }

        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    private readonly struct WorldSetup
    {
        public readonly INamedTypeSymbol WorldType;
        public readonly ImmutableArray<ArchetypeSetup> Archetypes;

        public WorldSetup(INamedTypeSymbol worldType, ImmutableArray<ArchetypeSetup> archetypes)
        {
            WorldType = worldType;
            Archetypes = archetypes;
        }

        public bool IsValid => WorldType != null && !Archetypes.IsDefaultOrEmpty;
    }

    private readonly struct WorldSetupParseResult
    {
        public readonly INamedTypeSymbol? WorldType;
        public readonly ImmutableArray<ArchetypeSetup> Archetypes;
        public readonly Diagnostic? Diagnostic;

        public WorldSetupParseResult(INamedTypeSymbol? worldType, ImmutableArray<ArchetypeSetup> archetypes, Diagnostic? diagnostic)
        {
            WorldType = worldType;
            Archetypes = archetypes;
            Diagnostic = diagnostic;
        }

        public bool IsValid => WorldType != null && !Archetypes.IsDefaultOrEmpty;
    }

    private readonly struct ArchetypeSetup
    {
        public readonly ITypeSymbol KindType;
        public readonly int Capacity;
        public readonly int SpawnQueueCapacityOverride;
        public readonly int DestroyQueueCapacityOverride;
        public readonly ImmutableArray<ITypeSymbol> Components;
        public readonly ImmutableArray<QuerySetup> Queries;
        public readonly SpatialIndexSetup? Spatial;

        public ArchetypeSetup(
            ITypeSymbol kindType,
            int capacity,
            ImmutableArray<ITypeSymbol> components,
            ImmutableArray<QuerySetup> queries,
            SpatialIndexSetup? spatial,
            int spawnQueueCapacityOverride,
            int destroyQueueCapacityOverride)
        {
            KindType = kindType;
            Capacity = capacity;
            Components = components;
            Queries = queries;
            Spatial = spatial;
            SpawnQueueCapacityOverride = spawnQueueCapacityOverride;
            DestroyQueueCapacityOverride = destroyQueueCapacityOverride;
        }
    }

    private readonly struct SpatialIndexSetup
    {
        public readonly ITypeSymbol PositionComponentType;
        public readonly string PositionMemberName;
        public readonly int CellSize;
        public readonly int GridSize;
        public readonly int OriginX;
        public readonly int OriginY;

        public SpatialIndexSetup(
            ITypeSymbol positionComponentType,
            string positionMemberName,
            int cellSize,
            int gridSize,
            int originX,
            int originY)
        {
            PositionComponentType = positionComponentType;
            PositionMemberName = positionMemberName;
            CellSize = cellSize;
            GridSize = gridSize;
            OriginX = originX;
            OriginY = originY;
        }
    }

    private readonly struct QuerySetup
    {
        public readonly QueryKind Kind;
        public readonly ITypeSymbol PositionComponentType;
        public readonly string PositionMemberName;
        public readonly int MaxResults;

        public QuerySetup(QueryKind kind, ITypeSymbol positionComponentType, string positionMemberName, int maxResults)
        {
            Kind = kind;
            PositionComponentType = positionComponentType;
            PositionMemberName = positionMemberName;
            MaxResults = maxResults;
        }
    }

    private enum QueryKind
    {
        Radius,
        Aabb
    }

    private sealed class ArchetypeSetupComparer : IComparer<ArchetypeSetup>
    {
        public static readonly ArchetypeSetupComparer Instance = new ArchetypeSetupComparer();

        public int Compare(ArchetypeSetup x, ArchetypeSetup y)
        {
            string a = x.KindType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string b = y.KindType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return string.CompareOrdinal(a, b);
        }
    }
}
