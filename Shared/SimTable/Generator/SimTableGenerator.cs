#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace SimTable.Generator
{
    [Generator]
    public sealed class SimTableGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Detect [SimTable] attribute (spatial tables)
            var spatialSchemas = context.SyntaxProvider.ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: "SimTable.SimTableAttribute",
                predicate: static (node, _) => node is StructDeclarationSyntax,
                transform: static (ctx, _) => TransformSpatial(ctx))
                .Where(static model => model is not null);

            // Detect [SimDataTable] attribute (data-only tables, no spatial partitioning)
            var dataOnlySchemas = context.SyntaxProvider.ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: "SimTable.SimDataTableAttribute",
                predicate: static (node, _) => node is StructDeclarationSyntax,
                transform: static (ctx, _) => TransformDataOnly(ctx))
                .Where(static model => model is not null);

            // Detect [MultiTableQuery] attribute (interface-based queries)
            var multiTableQueries = context.SyntaxProvider.ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: "SimTable.MultiTableQueryAttribute",
                predicate: static (node, _) => node is InterfaceDeclarationSyntax,
                transform: static (ctx, _) => TransformMultiTableQuery(ctx))
                .Where(static model => model is not null);

            // Detect [TableUnion] attribute (explicit struct unions)
            var tableUnions = context.SyntaxProvider.ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: "SimTable.TableUnionAttribute",
                predicate: static (node, _) => node is StructDeclarationSyntax,
                transform: static (ctx, _) => TransformTableUnion(ctx))
                .Where(static model => model is not null);

            // Combine both attribute sources
            var allSchemas = spatialSchemas.Collect()
                .Combine(dataOnlySchemas.Collect())
                .Select(static (pair, _) => pair.Left.AddRange(pair.Right));

            // Combine query sources
            var allQueries = multiTableQueries.Collect()
                .Combine(tableUnions.Collect())
                .Select(static (pair, _) => pair.Left.AddRange(pair.Right));

            // Combine schemas and queries for generation
            var combined = allSchemas.Combine(allQueries);

            // Generate all tables with their IDs (based on sorted order)
            context.RegisterSourceOutput(combined, static (spc, data) =>
            {
                var (models, queries) = data;

                if (!models.IsDefaultOrEmpty)
                {
                    // Sort by type name for deterministic IDs
                    var sortedModels = models
                        .Where(m => m != null)
                        .Select(m => m!)
                        .OrderBy(m => m.Type.Name)
                        .ToList();

                    // Create a lookup for query matching
                    var schemaByName = sortedModels.ToDictionary(m => m.Type.ToDisplayString(), m => m);

                    // Generate each table with its ID
                    for (int i = 0; i < sortedModels.Count; i++)
                    {
                        var model = sortedModels[i];
                        var source = TableRenderer.Render(model, tableId: i);
                        var hint = model.Type.Name + "Table.g.cs";
                        spc.AddSource(hint, SourceText.From(source, Encoding.UTF8));
                    }

                    // Generate SimWorld
                    var worldSource = SimWorldRenderer.Render(models!);
                    spc.AddSource("SimWorld.g.cs", SourceText.From(worldSource, Encoding.UTF8));

                    // Generate multi-table queries
                    if (!queries.IsDefaultOrEmpty)
                    {
                        foreach (var query in queries.Where(q => q != null))
                        {
                            // Find participating tables by matching field names/types
                            PopulateParticipatingTables(query!, sortedModels);

                            if (query!.ParticipatingTables.Count > 0)
                            {
                                var querySource = MultiTableQueryRenderer.Render(query);
                                var hint = query.QueryName + "Query.g.cs";
                                spc.AddSource(hint, SourceText.From(querySource, Encoding.UTF8));
                            }
                        }
                    }
                }
            });
        }

        private static void PopulateParticipatingTables(MultiTableQueryModel query, List<SchemaModel> allSchemas)
        {
            int switchIndex = 0;

            foreach (var schema in allSchemas)
            {
                // If explicit types are specified, only include those types
                if (query.ExplicitTypeNames.Count > 0)
                {
                    var schemaTypeName = schema.Type.ToDisplayString();
                    if (!query.ExplicitTypeNames.Contains(schemaTypeName))
                    {
                        continue;
                    }
                }

                // Check if this schema has all the required properties
                bool matches = true;

                foreach (var prop in query.Properties)
                {
                    var matchingColumn = schema.Columns.FirstOrDefault(c =>
                        c.Name == prop.Name &&
                        c.Type.ToDisplayString() == prop.Type.ToDisplayString());

                    if (matchingColumn == null)
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    var tableInfo = new ParticipatingTableInfo(schema)
                    {
                        SwitchIndex = switchIndex++
                    };
                    query.ParticipatingTables.Add(tableInfo);
                }
            }
        }

        private static SchemaModel? TransformSpatial(GeneratorAttributeSyntaxContext ctx)
        {
            var structDecl = (StructDeclarationSyntax)ctx.TargetNode;
            var typeSymbol = (INamedTypeSymbol)ctx.TargetSymbol;

            if (!typeSymbol.DeclaringSyntaxReferences.Any(s =>
                s.GetSyntax() is StructDeclarationSyntax sd &&
                sd.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword))))
            {
                return null;
            }

            if (typeSymbol.IsGenericType || (typeSymbol.ContainingType != null && typeSymbol.ContainingType.IsGenericType))
            {
                return null;
            }

            int capacity = 1024;
            int cellSize = 16;
            int gridSize = 256;
            int chunkSize = 0;
            EvictionPolicy evictionPolicy = EvictionPolicy.None;
            string lruKeyField = "";

            foreach (var attr in typeSymbol.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::SimTable.SimTableAttribute")
                {
                    foreach (var kvp in attr.NamedArguments)
                    {
                        switch (kvp.Key)
                        {
                            case "Capacity":
                                capacity = (int)kvp.Value.Value!;
                                break;
                            case "CellSize":
                                cellSize = (int)kvp.Value.Value!;
                                break;
                            case "GridSize":
                                gridSize = (int)kvp.Value.Value!;
                                break;
                            case "ChunkSize":
                                chunkSize = (int)kvp.Value.Value!;
                                break;
                            case "EvictionPolicy":
                                evictionPolicy = (EvictionPolicy)(int)kvp.Value.Value!;
                                break;
                            case "LRUKeyField":
                                lruKeyField = (string)kvp.Value.Value!;
                                break;
                        }
                    }
                }
            }

            var model = new SchemaModel(typeSymbol, capacity, cellSize, gridSize, chunkSize, isDataOnly: false, autoAllocate: false, evictionPolicy: evictionPolicy, lruKeyField: lruKeyField);
            PopulateColumns(model, typeSymbol);
            ParseSetupMethod(model, typeSymbol);
            return model;
        }

        private static SchemaModel? TransformDataOnly(GeneratorAttributeSyntaxContext ctx)
        {
            var structDecl = (StructDeclarationSyntax)ctx.TargetNode;
            var typeSymbol = (INamedTypeSymbol)ctx.TargetSymbol;

            if (!typeSymbol.DeclaringSyntaxReferences.Any(s =>
                s.GetSyntax() is StructDeclarationSyntax sd &&
                sd.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword))))
            {
                return null;
            }

            if (typeSymbol.IsGenericType || (typeSymbol.ContainingType != null && typeSymbol.ContainingType.IsGenericType))
            {
                return null;
            }

            int capacity = 1; // Default for data-only tables
            bool autoAllocate = true; // Default for data-only tables

            foreach (var attr in typeSymbol.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::SimTable.SimDataTableAttribute")
                {
                    foreach (var kvp in attr.NamedArguments)
                    {
                        switch (kvp.Key)
                        {
                            case "Capacity":
                                capacity = (int)kvp.Value.Value!;
                                break;
                            case "AutoAllocate":
                                autoAllocate = (bool)kvp.Value.Value!;
                                break;
                        }
                    }
                }
            }

            // Data-only tables: no spatial partitioning, use minimal defaults
            var model = new SchemaModel(typeSymbol, capacity, cellSize: 1, gridSize: 1, chunkSize: 0, isDataOnly: true, autoAllocate: autoAllocate);
            PopulateColumns(model, typeSymbol);
            ParseSetupMethod(model, typeSymbol);
            return model;
        }

        private static void PopulateColumns(SchemaModel model, INamedTypeSymbol typeSymbol)
        {
            var names = new HashSet<string>();
            foreach (var member in typeSymbol.GetMembers().OfType<IFieldSymbol>())
            {
                if (member.IsStatic || member.IsConst)
                {
                    continue;
                }

                var name = member.Name;
                if (!names.Add(name))
                {
                    continue;
                }

                bool noSerialize = false;
                bool isComputed = false;
                int arrayLength = 0;

                var colAttr = member.GetAttributes().FirstOrDefault(a =>
                    a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::SimTable.ColumnAttribute");

                if (colAttr != null)
                {
                    foreach (var kvp in colAttr.NamedArguments)
                    {
                        if (kvp.Key == "NoSerialize")
                        {
                            noSerialize = (bool)kvp.Value.Value!;
                        }
                    }
                }

                // Check for ComputedStateAttribute
                var computedAttr = member.GetAttributes().FirstOrDefault(a =>
                    a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::SimTable.ComputedStateAttribute");

                if (computedAttr != null)
                {
                    isComputed = true;
                }

                // Check for ArrayAttribute (1D array)
                var arrayAttr = member.GetAttributes().FirstOrDefault(a =>
                    a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::SimTable.ArrayAttribute");

                if (arrayAttr != null && arrayAttr.ConstructorArguments.Length > 0)
                {
                    arrayLength = (int)arrayAttr.ConstructorArguments[0].Value!;
                }

                // Check for Array2DAttribute (2D array)
                int array2DRows = 0;
                int array2DCols = 0;
                var array2DAttr = member.GetAttributes().FirstOrDefault(a =>
                    a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::SimTable.Array2DAttribute");

                if (array2DAttr != null && array2DAttr.ConstructorArguments.Length >= 2)
                {
                    array2DRows = (int)array2DAttr.ConstructorArguments[0].Value!;
                    array2DCols = (int)array2DAttr.ConstructorArguments[1].Value!;
                }

                // Extract field initializer if present (for auto-allocation with defaults)
                // Skip for array fields as they're zero-initialized
                string? defaultValueExpression = null;
                if (arrayLength == 0 && array2DRows == 0)
                {
                    var syntaxRef = member.DeclaringSyntaxReferences.FirstOrDefault();
                    if (syntaxRef != null)
                    {
                        var syntax = syntaxRef.GetSyntax();
                        if (syntax is VariableDeclaratorSyntax varDecl && varDecl.Initializer != null)
                        {
                            defaultValueExpression = varDecl.Initializer.Value.ToFullString().Trim();
                        }
                    }
                }

                model.Columns.Add(new ColumnInfo(name, member.Type, noSerialize, isComputed, arrayLength, array2DRows, array2DCols, defaultValueExpression));
            }
        }

        /// <summary>
        /// Parses Setup methods marked with [Conditional("COMPUTED_STATE_SETUP")] to extract computed field expressions.
        /// </summary>
        private static void ParseSetupMethod(SchemaModel model, INamedTypeSymbol typeSymbol)
        {
            // Find methods with [Conditional("COMPUTED_STATE_SETUP")] attribute
            foreach (var method in typeSymbol.GetMembers().OfType<IMethodSymbol>())
            {
                if (!method.IsStatic) continue;

                var conditionalAttr = method.GetAttributes().FirstOrDefault(a =>
                    a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Diagnostics.ConditionalAttribute");

                if (conditionalAttr == null) continue;

                // Check if it's COMPUTED_STATE_SETUP
                if (conditionalAttr.ConstructorArguments.Length == 0 ||
                    (string?)conditionalAttr.ConstructorArguments[0].Value != "COMPUTED_STATE_SETUP")
                {
                    continue;
                }

                // Get the method syntax
                var syntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
                if (syntaxRef == null) continue;

                var methodSyntax = syntaxRef.GetSyntax() as MethodDeclarationSyntax;
                if (methodSyntax == null) continue;

                // Walk the method body to find .Compute(...) invocations
                ParseComputeInvocations(model, methodSyntax);
            }
        }

        /// <summary>
        /// Walks the method syntax to find and parse .Compute() invocations.
        /// </summary>
        private static void ParseComputeInvocations(SchemaModel model, MethodDeclarationSyntax methodSyntax)
        {
            // Get all invocation expressions in the method
            var invocations = methodSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>();

            foreach (var invocation in invocations)
            {
                // Check if this is a .Compute(...) call
                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                    memberAccess.Name.Identifier.Text == "Compute")
                {
                    var args = invocation.ArgumentList.Arguments;
                    if (args.Count >= 2)
                    {
                        // First arg: target field lambda (r => r.FieldName)
                        // Second arg: computation lambda (r => r.A * r.B)
                        var targetArg = args[0].Expression;
                        var computeArg = args[1].Expression;

                        // Extract target field name from first lambda
                        string? targetFieldName = ExtractFieldNameFromLambda(targetArg);
                        if (targetFieldName == null) continue;

                        // Find the column
                        var column = model.Columns.FirstOrDefault(c => c.Name == targetFieldName);
                        if (column == null) continue;

                        // Auto-mark as computed if it's a target in Setup's Compute()
                        // (no need for redundant [ComputedState] attribute when in Setup)
                        column.IsComputed = true;

                        // Transform the computation lambda into generated code
                        string? computedExpr = TransformLambdaToExpression(computeArg);
                        if (computedExpr != null)
                        {
                            column.ComputedExpression = computedExpr;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Extracts the field name from a simple lambda like (r => r.FieldName).
        /// </summary>
        private static string? ExtractFieldNameFromLambda(ExpressionSyntax expr)
        {
            // Handle both lambda forms: r => r.Field and (r) => r.Field
            LambdaExpressionSyntax? lambda = expr as SimpleLambdaExpressionSyntax ?? (LambdaExpressionSyntax?)(expr as ParenthesizedLambdaExpressionSyntax);
            if (lambda == null) return null;

            // Get the body - should be a member access like r.FieldName
            var body = lambda.ExpressionBody;
            if (body is MemberAccessExpressionSyntax memberAccess)
            {
                return memberAccess.Name.Identifier.Text;
            }

            return null;
        }

        /// <summary>
        /// Transforms a lambda expression into generated code.
        /// Supports both simple lambdas (r => r.A * r.B) -> (A(slot) * B(slot))
        /// and extended lambdas (r, world, db) => ... with world/db access.
        /// </summary>
        private static string? TransformLambdaToExpression(ExpressionSyntax expr)
        {
            // Handle both lambda forms
            LambdaExpressionSyntax? lambda = expr as SimpleLambdaExpressionSyntax ?? (LambdaExpressionSyntax?)(expr as ParenthesizedLambdaExpressionSyntax);
            if (lambda == null) return null;

            // Get the parameter name (usually 'r' for row)
            string rowParamName;
            if (lambda is SimpleLambdaExpressionSyntax simpleLambda)
            {
                rowParamName = simpleLambda.Parameter.Identifier.Text;
            }
            else if (lambda is ParenthesizedLambdaExpressionSyntax parenLambda && parenLambda.ParameterList.Parameters.Count > 0)
            {
                // First parameter is always the row, additional params (world, db) pass through as-is
                rowParamName = parenLambda.ParameterList.Parameters[0].Identifier.Text;
            }
            else
            {
                return null;
            }

            var body = lambda.ExpressionBody;
            if (body == null) return null;

            // Transform the expression: replace r.Field with Field(slot), preserve world/db references
            return TransformExpression(body, rowParamName);
        }

        /// <summary>
        /// Recursively transforms an expression, replacing parameter.Field with Field(slot).
        /// </summary>
        private static string TransformExpression(ExpressionSyntax expr, string paramName)
        {
            switch (expr)
            {
                case MemberAccessExpressionSyntax memberAccess:
                    // Check if this is param.Field
                    if (memberAccess.Expression is IdentifierNameSyntax identifier &&
                        identifier.Identifier.Text == paramName)
                    {
                        // Transform r.Field to Field(slot)
                        return $"{memberAccess.Name.Identifier.Text}(slot)";
                    }
                    // Otherwise, keep as-is (e.g., Fixed64.Zero)
                    return expr.ToString();

                case BinaryExpressionSyntax binary:
                    var left = TransformExpression(binary.Left, paramName);
                    var right = TransformExpression(binary.Right, paramName);
                    return $"({left} {binary.OperatorToken.Text} {right})";

                case ParenthesizedExpressionSyntax paren:
                    return $"({TransformExpression(paren.Expression, paramName)})";

                case PrefixUnaryExpressionSyntax prefix:
                    return $"{prefix.OperatorToken.Text}{TransformExpression(prefix.Operand, paramName)}";

                case PostfixUnaryExpressionSyntax postfix:
                    return $"{TransformExpression(postfix.Operand, paramName)}{postfix.OperatorToken.Text}";

                case CastExpressionSyntax cast:
                    return $"({cast.Type}){TransformExpression(cast.Expression, paramName)}";

                case InvocationExpressionSyntax invocation:
                    // Handle method calls like Fixed64.FromInt(...)
                    var args = string.Join(", ", invocation.ArgumentList.Arguments.Select(a => TransformExpression(a.Expression, paramName)));
                    return $"{invocation.Expression}({args})";

                case ConditionalExpressionSyntax conditional:
                    var condition = TransformExpression(conditional.Condition, paramName);
                    var whenTrue = TransformExpression(conditional.WhenTrue, paramName);
                    var whenFalse = TransformExpression(conditional.WhenFalse, paramName);
                    return $"({condition} ? {whenTrue} : {whenFalse})";

                default:
                    // For literals and other simple expressions, just return as-is
                    return expr.ToString();
            }
        }

        private static MultiTableQueryModel? TransformMultiTableQuery(GeneratorAttributeSyntaxContext ctx)
        {
            var interfaceDecl = (InterfaceDeclarationSyntax)ctx.TargetNode;
            var typeSymbol = (INamedTypeSymbol)ctx.TargetSymbol;

            var model = new MultiTableQueryModel(typeSymbol, isExplicitUnion: false);

            // Check for explicit type list in attribute
            foreach (var attr in typeSymbol.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::SimTable.MultiTableQueryAttribute")
                {
                    if (attr.ConstructorArguments.Length > 0)
                    {
                        var typesArg = attr.ConstructorArguments[0];
                        if (typesArg.Kind == TypedConstantKind.Array)
                        {
                            foreach (var typeConst in typesArg.Values)
                            {
                                if (typeConst.Value is INamedTypeSymbol explicitType)
                                {
                                    model.ExplicitTypeNames.Add(explicitType.ToDisplayString());
                                }
                            }
                        }
                    }
                }
            }

            // Extract properties from the interface
            foreach (var member in typeSymbol.GetMembers().OfType<IPropertySymbol>())
            {
                if (member.IsStatic)
                    continue;

                // Check if it's a ref-returning property
                bool isRefReturn = member.ReturnsByRef || member.ReturnsByRefReadonly;

                // Get the actual type (without ref)
                var propType = member.Type;

                model.Properties.Add(new QueryPropertyInfo(member.Name, propType, isRefReturn));
            }

            return model;
        }

        private static MultiTableQueryModel? TransformTableUnion(GeneratorAttributeSyntaxContext ctx)
        {
            var structDecl = (StructDeclarationSyntax)ctx.TargetNode;
            var typeSymbol = (INamedTypeSymbol)ctx.TargetSymbol;

            // Check for partial modifier
            if (!typeSymbol.DeclaringSyntaxReferences.Any(s =>
                s.GetSyntax() is StructDeclarationSyntax sd &&
                sd.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword))))
            {
                return null;
            }

            var model = new MultiTableQueryModel(typeSymbol, isExplicitUnion: true);

            // Get table types from attribute
            foreach (var attr in typeSymbol.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::SimTable.TableUnionAttribute")
                {
                    if (attr.ConstructorArguments.Length > 0)
                    {
                        var tablesArg = attr.ConstructorArguments[0];
                        if (tablesArg.Kind == TypedConstantKind.Array)
                        {
                            foreach (var tableTypeConst in tablesArg.Values)
                            {
                                if (tableTypeConst.Value is INamedTypeSymbol tableType)
                                {
                                    // We'll match these by name later in PopulateParticipatingTables
                                    // For now, store in a list for explicit matching
                                    // This is handled differently - we need to collect field info
                                }
                            }
                        }
                    }
                }
            }

            // For TableUnion, extract properties from the participating tables' common fields
            // This is done in PopulateParticipatingTables after we have the schemas

            return model;
        }
    }
}

