using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DerpLib.Ecs.Editor.Generator;

[Generator]
public sealed class EcsEditorWorldApplierGenerator : IIncrementalGenerator
{
    private const string SetupBuilderFqn = "global::DerpLib.Ecs.Setup.DerpEcsSetupBuilder";
    private const string SetupConditionalSymbol = "DERP_ECS_SETUP";
    private const string PropertyAttributeFqn = "global::Property.PropertyAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<WorldModel?> worldModels = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, _) => TryGetWorldModel(ctx))
            .Where(static m => m is not null);

        context.RegisterSourceOutput(worldModels.Collect(), static (spc, models) =>
        {
            try
            {
                Emit(spc, models);
            }
            catch (Exception ex)
            {
                string details = ex.GetType().FullName + ": " + ex.Message;
                if (!string.IsNullOrEmpty(ex.StackTrace))
                {
                    details += " | " + ex.StackTrace.Replace("\r", " ").Replace("\n", " ");
                }
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.GeneratorError, Location.None, details));
            }
        });
    }

    private static WorldModel? TryGetWorldModel(GeneratorSyntaxContext ctx)
    {
        var methodSyntax = (MethodDeclarationSyntax)ctx.Node;
        if (methodSyntax.ParameterList.Parameters.Count != 1)
        {
            return null;
        }

        IMethodSymbol? methodSymbol = ctx.SemanticModel.GetDeclaredSymbol(methodSyntax) as IMethodSymbol;
        if (methodSymbol == null)
        {
            return null;
        }

        if (!string.Equals(methodSymbol.Name, "Setup", StringComparison.Ordinal))
        {
            return null;
        }

        if (!methodSymbol.IsStatic)
        {
            return null;
        }

        if (methodSymbol.Parameters.Length != 1)
        {
            return null;
        }

        if (methodSymbol.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != SetupBuilderFqn)
        {
            return null;
        }

        if (!HasDerpEcsSetupConditional(methodSymbol))
        {
            return null;
        }

        string builderParameterName = methodSymbol.Parameters[0].Name;

        INamedTypeSymbol? worldType = methodSymbol.ContainingType;
        if (worldType.TypeKind != TypeKind.Class)
        {
            return null;
        }

        if (!IsPartial(worldType))
        {
            return null;
        }

        if (methodSyntax.Body == null)
        {
            if (methodSyntax.ExpressionBody == null)
            {
                return null;
            }

            if (methodSyntax.ExpressionBody.Expression is not InvocationExpressionSyntax expressionInvocation)
            {
                return null;
            }

            if (!TryParseArchetypeChain(expressionInvocation, ctx.SemanticModel, builderParameterName, out INamedTypeSymbol? exprKind, out List<INamedTypeSymbol> exprComponents))
            {
                return null;
            }

            var exprModels = new List<ArchetypeModel>(1)
            {
                new ArchetypeModel(exprKind!, exprComponents)
            };
            return new WorldModel(worldType, exprModels);
        }

        var archetypes = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
        for (int i = 0; i < methodSyntax.Body.Statements.Count; i++)
        {
            if (methodSyntax.Body.Statements[i] is not ExpressionStatementSyntax exprStmt)
            {
                continue;
            }

            if (exprStmt.Expression is not InvocationExpressionSyntax invocation)
            {
                continue;
            }

            if (!TryParseArchetypeChain(invocation, ctx.SemanticModel, builderParameterName, out INamedTypeSymbol? kind, out List<INamedTypeSymbol> components))
            {
                continue;
            }

            INamedTypeSymbol kindSymbol = kind!;
            if (!archetypes.TryGetValue(kindSymbol, out List<INamedTypeSymbol>? list))
            {
                list = new List<INamedTypeSymbol>(components.Count);
                archetypes.Add(kindSymbol, list);
            }

            for (int c = 0; c < components.Count; c++)
            {
                INamedTypeSymbol comp = components[c];
                if (!ContainsSymbol(list, comp))
                {
                    list.Add(comp);
                }
            }
        }

        if (archetypes.Count == 0)
        {
            return null;
        }

        var models = new List<ArchetypeModel>(archetypes.Count);
        foreach (KeyValuePair<INamedTypeSymbol, List<INamedTypeSymbol>> kvp in archetypes)
        {
            models.Add(new ArchetypeModel(kvp.Key, kvp.Value));
        }

        return new WorldModel(worldType, models);
    }

    private static bool HasDerpEcsSetupConditional(IMethodSymbol methodSymbol)
    {
        ImmutableArray<AttributeData> attributes = methodSymbol.GetAttributes();
        for (int i = 0; i < attributes.Length; i++)
        {
            AttributeData attr = attributes[i];
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
                string.Equals(s, SetupConditionalSymbol, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPartial(INamedTypeSymbol type)
    {
        foreach (SyntaxReference syntaxRef in type.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax() is TypeDeclarationSyntax typeDecl)
            {
                for (int i = 0; i < typeDecl.Modifiers.Count; i++)
                {
                    if (typeDecl.Modifiers[i].IsKind(SyntaxKind.PartialKeyword))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool ContainsSymbol(List<INamedTypeSymbol> list, INamedTypeSymbol symbol)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (SymbolEqualityComparer.Default.Equals(list[i], symbol))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseArchetypeChain(
        InvocationExpressionSyntax topInvocation,
        SemanticModel model,
        string builderParameterName,
        out INamedTypeSymbol? kindType,
        out List<INamedTypeSymbol> components)
    {
        kindType = null;
        components = new List<INamedTypeSymbol>();

        InvocationExpressionSyntax? current = topInvocation;
        while (current != null)
        {
            if (current.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                break;
            }

            SimpleNameSyntax nameSyntax = memberAccess.Name;
            string methodName = nameSyntax.Identifier.ValueText;
            if (string.Equals(methodName, "With", StringComparison.Ordinal) && nameSyntax is GenericNameSyntax withGeneric)
            {
                if (withGeneric.TypeArgumentList.Arguments.Count == 1)
                {
                    TypeSyntax typeSyntax = withGeneric.TypeArgumentList.Arguments[0];
                    var typeSymbol = model.GetTypeInfo(typeSyntax).Type as INamedTypeSymbol;
                    if (typeSymbol != null)
                    {
                        components.Add(typeSymbol);
                    }
                }
            }

            if (string.Equals(methodName, "Archetype", StringComparison.Ordinal) && nameSyntax is GenericNameSyntax archetypeGeneric)
            {
                if (archetypeGeneric.TypeArgumentList.Arguments.Count == 1)
                {
                    TypeSyntax typeSyntax = archetypeGeneric.TypeArgumentList.Arguments[0];
                    kindType = model.GetTypeInfo(typeSyntax).Type as INamedTypeSymbol;
                }
                if (!IsRootedInBuilderParameter(memberAccess.Expression, builderParameterName))
                {
                    kindType = null;
                }
                break;
            }

            current = memberAccess.Expression as InvocationExpressionSyntax;
        }

        if (kindType == null || components.Count == 0)
        {
            return false;
        }

        return true;
    }

    private static bool IsRootedInBuilderParameter(ExpressionSyntax expression, string builderParameterName)
    {
        if (expression is IdentifierNameSyntax id)
        {
            return string.Equals(id.Identifier.ValueText, builderParameterName, StringComparison.Ordinal);
        }

        return false;
    }

    private static void Emit(SourceProductionContext spc, ImmutableArray<WorldModel?> worldModels)
    {
        if (worldModels.IsDefaultOrEmpty)
        {
            return;
        }

        var ordered = new List<WorldModel>(worldModels.Length);
        for (int i = 0; i < worldModels.Length; i++)
        {
            WorldModel? model = worldModels[i];
            if (model == null)
            {
                continue;
            }

            ordered.Add(model);
        }

        ordered.Sort(static (a, b) =>
        {
            string an = a.WorldType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string bn = b.WorldType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return string.CompareOrdinal(an, bn);
        });

        for (int i = 0; i < ordered.Count; i++)
        {
            EmitWorld(spc, ordered[i]);
        }
    }

    private static void EmitWorld(SourceProductionContext spc, WorldModel model)
    {
        INamedTypeSymbol worldType = model.WorldType;
        string worldFqn = worldType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string ns = worldType.ContainingNamespace.IsGlobalNamespace ? string.Empty : worldType.ContainingNamespace.ToDisplayString();
        INamespaceSymbol worldNamespace = worldType.ContainingNamespace;

        string className = worldType.Name + "EcsEditorApply";

        var sb = new StringBuilder(16_384);
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using DerpLib.Ecs;");
        sb.AppendLine("using DerpLib.Ecs.Editor;");
        sb.AppendLine("using Property;");
        sb.AppendLine();
        if (!string.IsNullOrEmpty(ns))
        {
            sb.Append("namespace ").Append(ns).AppendLine(";");
            sb.AppendLine();
        }

        sb.Append("public static class ").Append(className).AppendLine();
        sb.AppendLine("{");

        var uniqueComponents = new List<INamedTypeSymbol>(16);
        for (int i = 0; i < model.Archetypes.Count; i++)
        {
            ArchetypeModel archetype = model.Archetypes[i];
            for (int c = 0; c < archetype.Components.Count; c++)
            {
                INamedTypeSymbol component = archetype.Components[c];
                if (!ComponentHasAnySupportedProperties(component))
                {
                    continue;
                }

                if (!ContainsSymbol(uniqueComponents, component))
                {
                    uniqueComponents.Add(component);
                }
            }
        }

        uniqueComponents.Sort(static (a, b) =>
        {
            string an = a.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string bn = b.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return string.CompareOrdinal(an, bn);
        });

        var archetypesSortedByKind = new List<ArchetypeModel>(model.Archetypes);
        archetypesSortedByKind.Sort(static (a, b) =>
        {
            string an = a.KindType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string bn = b.KindType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return string.CompareOrdinal(an, bn);
        });

        sb.AppendLine("    public static EditorEditSession BeginChange(this " + worldFqn + " world, EntityHandle entity)");
        sb.AppendLine("    {");
        sb.AppendLine("        return new EditorEditSession(world, entity, EcsEditMode.Change);");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    public static EditorEditSession BeginPreview(this " + worldFqn + " world, EntityHandle entity)");
        sb.AppendLine("    {");
        sb.AppendLine("        return new EditorEditSession(world, entity, EcsEditMode.Preview);");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    public ref struct EditorEditSession");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly " + worldFqn + " _world;");
        sb.AppendLine("        private readonly EntityHandle _entity;");
        sb.AppendLine("        private readonly EcsEditMode _mode;");
        sb.AppendLine("        private readonly int _startIndex;");

        for (int i = 0; i < uniqueComponents.Count; i++)
        {
            INamedTypeSymbol component = uniqueComponents[i];
            string proxyOwnerFqn = GetTypeFqn(component.ContainingNamespace, component.Name + "EcsEdit");
            string proxyTypeFqn = proxyOwnerFqn + "." + component.Name + "EditProxy";
            string propertyName = TrimComponentSuffix(component.Name);
            sb.Append("        public readonly ").Append(proxyTypeFqn).Append(' ').Append(propertyName).AppendLine(";");
        }

        sb.AppendLine();
        sb.AppendLine("        internal EditorEditSession(" + worldFqn + " world, EntityHandle entity, EcsEditMode mode)");
        sb.AppendLine("        {");
        sb.AppendLine("            _world = world;");
        sb.AppendLine("            _entity = entity;");
        sb.AppendLine("            _mode = mode;");
        sb.AppendLine("            _startIndex = EditorContext.BeginSession();");

        for (int i = 0; i < uniqueComponents.Count; i++)
        {
            INamedTypeSymbol component = uniqueComponents[i];
            string proxyOwnerFqn = GetTypeFqn(component.ContainingNamespace, component.Name + "EcsEdit");
            string proxyTypeFqn = proxyOwnerFqn + "." + component.Name + "EditProxy";
            string propertyName = TrimComponentSuffix(component.Name);
            sb.Append("            ").Append(propertyName).Append(" = new ").Append(proxyTypeFqn).AppendLine("(entity, mode);");
        }

        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine("        public void Dispose()");
        sb.AppendLine("        {");
        sb.AppendLine("            try");
        sb.AppendLine("            {");
        sb.AppendLine("                ReadOnlySpan<EcsEditorCommand> commands = EditorContext.Commands.Slice(_startIndex);");
        sb.AppendLine("                EcsEditorCommandPipeline? pipeline = EditorContext.Pipeline;");
        sb.AppendLine("                if (pipeline != null)");
        sb.AppendLine("                {");
        sb.AppendLine("                    commands = pipeline.Run(commands);");
        sb.AppendLine("                }");
        sb.AppendLine();
        sb.AppendLine("                if (commands.Length > 0)");
        sb.AppendLine("                {");
        sb.AppendLine("                    EcsEditorUndoStack? undoStack = EditorContext.UndoStack;");
        sb.AppendLine("                    if (undoStack != null)");
        sb.AppendLine("                    {");
        sb.AppendLine("                        _world.ApplyEditorCommandsWithUndo(commands, undoStack);");
        sb.AppendLine("                    }");
        sb.AppendLine("                    else");
        sb.AppendLine("                    {");
        sb.AppendLine("                        _world.ApplyEditorCommands(commands);");
        sb.AppendLine("                    }");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine("            finally");
        sb.AppendLine("            {");
        sb.AppendLine("                EditorContext.EndSession(_startIndex);");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    public static void RegisterEditorPropertyNames()");
        sb.AppendLine("    {");
        for (int i = 0; i < uniqueComponents.Count; i++)
        {
            INamedTypeSymbol component = uniqueComponents[i];
            string propsFqn = GetTypeFqn(component.ContainingNamespace, component.Name + "EcsProperties");
            sb.Append("        ").Append(propsFqn).AppendLine(".RegisterNames();");
        }
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.Append("    public static int ApplyEditorCommands(this ").Append(worldFqn).AppendLine(" world, ReadOnlySpan<EcsEditorCommand> commands)");
        sb.AppendLine("    {");
        sb.AppendLine("        int applied = 0;");
        sb.AppendLine("        for (int i = 0; i < commands.Length; i++)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (TryApplyEditorCommand(world, in commands[i]))");
        sb.AppendLine("            {");
        sb.AppendLine("                applied++;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("        return applied;");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.Append("    public static int ApplyEditorCommandsWithUndo(this ").Append(worldFqn).AppendLine(" world, ReadOnlySpan<EcsEditorCommand> commands, EcsEditorUndoStack undoStack)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (commands.Length == 0)");
        sb.AppendLine("        {");
        sb.AppendLine("            return 0;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        var undoStream = EditorContext.UndoScratchStream;");
        sb.AppendLine("        var redoStream = EditorContext.RedoScratchStream;");
        sb.AppendLine("        undoStream.Clear();");
        sb.AppendLine("        redoStream.Clear();");
        sb.AppendLine();
        sb.AppendLine("        bool canRecordUndo = true;");
        sb.AppendLine("        int applied = 0;");
        sb.AppendLine("        for (int i = 0; i < commands.Length; i++)");
        sb.AppendLine("        {");
        sb.AppendLine("            ref readonly EcsEditorCommand command = ref commands[i];");
        sb.AppendLine("            if (command.Kind == EcsEditorCommandKind.SetProperty && command.EditMode == EcsEditMode.Change)");
        sb.AppendLine("            {");
        sb.AppendLine("                if (!TryCreateUndoCommand(world, in command, out EcsEditorCommand undoCommand))");
        sb.AppendLine("                {");
        sb.AppendLine("                    canRecordUndo = false;");
        sb.AppendLine("                    if (TryApplyEditorCommand(world, in command))");
        sb.AppendLine("                    {");
        sb.AppendLine("                        applied++;");
        sb.AppendLine("                    }");
        sb.AppendLine("                    continue;");
        sb.AppendLine("                }");
        sb.AppendLine();
        sb.AppendLine("                if (TryApplyEditorCommand(world, in command))");
        sb.AppendLine("                {");
        sb.AppendLine("                    applied++;");
        sb.AppendLine("                    undoStream.Enqueue(in undoCommand);");
        sb.AppendLine("                    redoStream.Enqueue(in command);");
        sb.AppendLine("                }");
        sb.AppendLine("                else");
        sb.AppendLine("                {");
        sb.AppendLine("                    canRecordUndo = false;");
        sb.AppendLine("                }");
        sb.AppendLine();
        sb.AppendLine("                continue;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            if (TryApplyEditorCommand(world, in command))");
        sb.AppendLine("            {");
        sb.AppendLine("                applied++;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        if (canRecordUndo && undoStream.Count > 0)");
        sb.AppendLine("        {");
        sb.AppendLine("            undoStack.Push(undoStream.Commands, redoStream.Commands);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        return applied;");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.Append("    public static bool TryCreateUndoCommand(this ").Append(worldFqn).AppendLine(" world, in EcsEditorCommand command, out EcsEditorCommand undoCommand)");
        sb.AppendLine("    {");
        sb.AppendLine("        undoCommand = default;");
        sb.AppendLine("        if (command.Kind != EcsEditorCommandKind.SetProperty)");
        sb.AppendLine("        {");
        sb.AppendLine("            return false;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        if (command.EditMode != EcsEditMode.Change)");
        sb.AppendLine("        {");
        sb.AppendLine("            return false;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        return TryCreateUndoSetProperty(world, in command, out undoCommand);");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.Append("    private static bool TryCreateUndoSetProperty(").Append(worldFqn).AppendLine(" world, in EcsEditorCommand command, out EcsEditorCommand undoCommand)");
        sb.AppendLine("    {");
        sb.AppendLine("        EntityHandle entity = command.Address.Entity;");
        sb.AppendLine("        switch (entity.KindId)");
        sb.AppendLine("        {");
        for (int i = 0; i < archetypesSortedByKind.Count; i++)
        {
            ArchetypeModel archetype = archetypesSortedByKind[i];
            string kindName = archetype.KindType.Name;
            string tableFqn = GetTypeFqn(worldNamespace, kindName + "Table");

            sb.Append("            case ").Append(tableFqn).AppendLine(".KindId:");
            sb.AppendLine("            {");
            sb.Append("                var table = world.").Append(kindName).AppendLine(";");
            sb.AppendLine("                return TryCreateUndoSetPropertyTo" + kindName + "(table, entity, in command, out undoCommand);");
            sb.AppendLine("            }");
        }
        sb.AppendLine("            default:");
        sb.AppendLine("                undoCommand = default;");
        sb.AppendLine("                return false;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.Append("    public static bool TryApplyEditorCommand(this ").Append(worldFqn).AppendLine(" world, in EcsEditorCommand command)");
        sb.AppendLine("    {");
        sb.AppendLine("        switch (command.Kind)");
        sb.AppendLine("        {");
        sb.AppendLine("            case EcsEditorCommandKind.SetProperty:");
        sb.AppendLine("                return TryApplySetProperty(world, in command);");
        sb.AppendLine("            default:");
        sb.AppendLine("                return false;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.Append("    private static bool TryApplySetProperty(").Append(worldFqn).AppendLine(" world, in EcsEditorCommand command)");
        sb.AppendLine("    {");
        sb.AppendLine("        EntityHandle entity = command.Address.Entity;");
        sb.AppendLine("        switch (entity.KindId)");
        sb.AppendLine("        {");

        List<ArchetypeModel> archetypes = archetypesSortedByKind;

        for (int i = 0; i < archetypes.Count; i++)
        {
            ArchetypeModel archetype = archetypes[i];
            string kindName = archetype.KindType.Name;
            string tableFqn = GetTypeFqn(worldNamespace, kindName + "Table");

            sb.Append("            case ").Append(tableFqn).AppendLine(".KindId:");
            sb.AppendLine("            {");
            sb.Append("                var table = world.").Append(kindName).AppendLine(";");
            sb.AppendLine("                return TryApplySetPropertyTo" + kindName + "(table, entity, in command);");
            sb.AppendLine("            }");
        }

        sb.AppendLine("            default:");
        sb.AppendLine("                return false;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

        for (int i = 0; i < archetypes.Count; i++)
        {
            ArchetypeModel archetype = archetypes[i];
            EmitArchetypeHandler(sb, worldNamespace, archetype);
        }

        sb.AppendLine("}");

        spc.AddSource(worldType.Name + ".EcsEditorApply.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static void EmitArchetypeHandler(StringBuilder sb, INamespaceSymbol worldNamespace, ArchetypeModel archetype)
    {
        string kindName = archetype.KindType.Name;
        string tableFqn = GetTypeFqn(worldNamespace, kindName + "Table");

        sb.Append("    private static bool TryApplySetPropertyTo").Append(kindName).Append("(").Append(tableFqn)
            .AppendLine(" table, EntityHandle entity, in EcsEditorCommand command)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (!table.TryGetRow(entity, out int row))");
        sb.AppendLine("        {");
        sb.AppendLine("            return false;");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine("        switch (command.Address.ComponentSchemaId)");
        sb.AppendLine("        {");
        var components = new List<INamedTypeSymbol>(archetype.Components);
        components.Sort(static (a, b) =>
        {
            string an = a.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string bn = b.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return string.CompareOrdinal(an, bn);
        });

        bool emittedAny = false;
        for (int i = 0; i < components.Count; i++)
        {
            INamedTypeSymbol component = components[i];
            if (!ComponentHasAnySupportedProperties(component))
            {
                continue;
            }

            string componentFqn = component.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string propsFqn = GetTypeFqn(component.ContainingNamespace, component.Name + "EcsProperties");
            string accessorName = TrimComponentSuffix(component.Name);

            sb.Append("            case ").Append(propsFqn).AppendLine(".SchemaId:");
            sb.AppendLine("            {");
            sb.Append("                ref ").Append(componentFqn).Append(' ').Append("component = ref table.").Append(accessorName).AppendLine("(row);");
            sb.Append("                int propertyIndex = command.Address.PropertyIndex;").AppendLine();
            sb.AppendLine("                if (!" + propsFqn + ".TryGetPropertyIdByIndex(propertyIndex, out ulong expectedPropertyId) || expectedPropertyId != command.Address.PropertyId)");
            sb.AppendLine("                {");
            sb.AppendLine("                    return false;");
            sb.AppendLine("                }");
            sb.AppendLine();
            sb.AppendLine("                switch (command.PropertyKind)");
            sb.AppendLine("                {");
            sb.AppendLine("                    case PropertyKind.Float: return " + propsFqn + ".TryWriteFloat(ref component, propertyIndex, command.FloatValue);");
            sb.AppendLine("                    case PropertyKind.Int: return " + propsFqn + ".TryWriteInt(ref component, propertyIndex, command.IntValue);");
            sb.AppendLine("                    case PropertyKind.Bool: return " + propsFqn + ".TryWriteBool(ref component, propertyIndex, command.BoolValue);");
            sb.AppendLine("                    case PropertyKind.Vec2: return " + propsFqn + ".TryWriteVec2(ref component, propertyIndex, command.Vec2Value);");
            sb.AppendLine("                    case PropertyKind.Vec3: return " + propsFqn + ".TryWriteVec3(ref component, propertyIndex, command.Vec3Value);");
            sb.AppendLine("                    case PropertyKind.Vec4: return " + propsFqn + ".TryWriteVec4(ref component, propertyIndex, command.Vec4Value);");
            sb.AppendLine("                    case PropertyKind.Color32: return " + propsFqn + ".TryWriteColor32(ref component, propertyIndex, command.Color32Value);");
            sb.AppendLine("                    case PropertyKind.StringHandle: return " + propsFqn + ".TryWriteStringHandle(ref component, propertyIndex, command.StringHandleValue);");
            sb.AppendLine("                    case PropertyKind.Fixed64: return " + propsFqn + ".TryWriteFixed64(ref component, propertyIndex, command.Fixed64Value);");
            sb.AppendLine("                    case PropertyKind.Fixed64Vec2: return " + propsFqn + ".TryWriteFixed64Vec2(ref component, propertyIndex, command.Fixed64Vec2Value);");
            sb.AppendLine("                    case PropertyKind.Fixed64Vec3: return " + propsFqn + ".TryWriteFixed64Vec3(ref component, propertyIndex, command.Fixed64Vec3Value);");
            sb.AppendLine("                    default: return false;");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            emittedAny = true;
        }

        if (!emittedAny)
        {
            sb.AppendLine("            default:");
            sb.AppendLine("                return false;");
        }
        else
        {
            sb.AppendLine("            default:");
            sb.AppendLine("                return false;");
        }

        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.Append("    private static bool TryCreateUndoSetPropertyTo").Append(kindName).Append("(").Append(tableFqn)
            .AppendLine(" table, EntityHandle entity, in EcsEditorCommand command, out EcsEditorCommand undoCommand)");
        sb.AppendLine("    {");
        sb.AppendLine("        undoCommand = default;");
        sb.AppendLine("        if (!table.TryGetRow(entity, out int row))");
        sb.AppendLine("        {");
        sb.AppendLine("            return false;");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine("        switch (command.Address.ComponentSchemaId)");
        sb.AppendLine("        {");
        emittedAny = false;
        for (int i = 0; i < components.Count; i++)
        {
            INamedTypeSymbol component = components[i];
            if (!ComponentHasAnySupportedProperties(component))
            {
                continue;
            }

            string componentFqn = component.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string propsFqn = GetTypeFqn(component.ContainingNamespace, component.Name + "EcsProperties");
            string accessorName = TrimComponentSuffix(component.Name);

            sb.Append("            case ").Append(propsFqn).AppendLine(".SchemaId:");
            sb.AppendLine("            {");
            sb.Append("                ref ").Append(componentFqn).Append(' ').Append("component = ref table.").Append(accessorName).AppendLine("(row);");
            sb.AppendLine("                int propertyIndex = command.Address.PropertyIndex;");
            sb.AppendLine("                if (!" + propsFqn + ".TryGetPropertyIdByIndex(propertyIndex, out ulong expectedPropertyId) || expectedPropertyId != command.Address.PropertyId)");
            sb.AppendLine("                {");
            sb.AppendLine("                    return false;");
            sb.AppendLine("                }");
            sb.AppendLine();
            sb.AppendLine("                switch (command.PropertyKind)");
            sb.AppendLine("                {");
            sb.AppendLine("                    case PropertyKind.Float:");
            sb.AppendLine("                    {");
            sb.AppendLine("                        if (!" + propsFqn + ".TryReadFloat(ref component, propertyIndex, out float oldValue))");
            sb.AppendLine("                        {");
            sb.AppendLine("                            return false;");
            sb.AppendLine("                        }");
            sb.AppendLine("                        undoCommand = EcsEditorCommand.SetFloat(EcsEditMode.Change, in command.Address, oldValue);");
            sb.AppendLine("                        return true;");
            sb.AppendLine("                    }");
            sb.AppendLine("                    case PropertyKind.Int:");
            sb.AppendLine("                    {");
            sb.AppendLine("                        if (!" + propsFqn + ".TryReadInt(ref component, propertyIndex, out int oldValue))");
            sb.AppendLine("                        {");
            sb.AppendLine("                            return false;");
            sb.AppendLine("                        }");
            sb.AppendLine("                        undoCommand = EcsEditorCommand.SetInt(EcsEditMode.Change, in command.Address, oldValue);");
            sb.AppendLine("                        return true;");
            sb.AppendLine("                    }");
            sb.AppendLine("                    case PropertyKind.Bool:");
            sb.AppendLine("                    {");
            sb.AppendLine("                        if (!" + propsFqn + ".TryReadBool(ref component, propertyIndex, out bool oldValue))");
            sb.AppendLine("                        {");
            sb.AppendLine("                            return false;");
            sb.AppendLine("                        }");
            sb.AppendLine("                        undoCommand = EcsEditorCommand.SetBool(EcsEditMode.Change, in command.Address, oldValue);");
            sb.AppendLine("                        return true;");
            sb.AppendLine("                    }");
            sb.AppendLine("                    case PropertyKind.Vec2:");
            sb.AppendLine("                    {");
            sb.AppendLine("                        if (!" + propsFqn + ".TryReadVec2(ref component, propertyIndex, out global::System.Numerics.Vector2 oldValue))");
            sb.AppendLine("                        {");
            sb.AppendLine("                            return false;");
            sb.AppendLine("                        }");
            sb.AppendLine("                        undoCommand = EcsEditorCommand.SetVec2(EcsEditMode.Change, in command.Address, oldValue);");
            sb.AppendLine("                        return true;");
            sb.AppendLine("                    }");
            sb.AppendLine("                    case PropertyKind.Vec3:");
            sb.AppendLine("                    {");
            sb.AppendLine("                        if (!" + propsFqn + ".TryReadVec3(ref component, propertyIndex, out global::System.Numerics.Vector3 oldValue))");
            sb.AppendLine("                        {");
            sb.AppendLine("                            return false;");
            sb.AppendLine("                        }");
            sb.AppendLine("                        undoCommand = EcsEditorCommand.SetVec3(EcsEditMode.Change, in command.Address, oldValue);");
            sb.AppendLine("                        return true;");
            sb.AppendLine("                    }");
            sb.AppendLine("                    case PropertyKind.Vec4:");
            sb.AppendLine("                    {");
            sb.AppendLine("                        if (!" + propsFqn + ".TryReadVec4(ref component, propertyIndex, out global::System.Numerics.Vector4 oldValue))");
            sb.AppendLine("                        {");
            sb.AppendLine("                            return false;");
            sb.AppendLine("                        }");
            sb.AppendLine("                        undoCommand = EcsEditorCommand.SetVec4(EcsEditMode.Change, in command.Address, oldValue);");
            sb.AppendLine("                        return true;");
            sb.AppendLine("                    }");
            sb.AppendLine("                    case PropertyKind.Color32:");
            sb.AppendLine("                    {");
            sb.AppendLine("                        if (!" + propsFqn + ".TryReadColor32(ref component, propertyIndex, out global::Core.Color32 oldValue))");
            sb.AppendLine("                        {");
            sb.AppendLine("                            return false;");
            sb.AppendLine("                        }");
            sb.AppendLine("                        undoCommand = EcsEditorCommand.SetColor32(EcsEditMode.Change, in command.Address, oldValue);");
            sb.AppendLine("                        return true;");
            sb.AppendLine("                    }");
            sb.AppendLine("                    case PropertyKind.StringHandle:");
            sb.AppendLine("                    {");
            sb.AppendLine("                        if (!" + propsFqn + ".TryReadStringHandle(ref component, propertyIndex, out global::Core.StringHandle oldValue))");
            sb.AppendLine("                        {");
            sb.AppendLine("                            return false;");
            sb.AppendLine("                        }");
            sb.AppendLine("                        undoCommand = EcsEditorCommand.SetStringHandle(EcsEditMode.Change, in command.Address, oldValue);");
            sb.AppendLine("                        return true;");
            sb.AppendLine("                    }");
            sb.AppendLine("                    case PropertyKind.Fixed64:");
            sb.AppendLine("                    {");
            sb.AppendLine("                        if (!" + propsFqn + ".TryReadFixed64(ref component, propertyIndex, out global::FixedMath.Fixed64 oldValue))");
            sb.AppendLine("                        {");
            sb.AppendLine("                            return false;");
            sb.AppendLine("                        }");
            sb.AppendLine("                        undoCommand = EcsEditorCommand.SetFixed64(EcsEditMode.Change, in command.Address, oldValue);");
            sb.AppendLine("                        return true;");
            sb.AppendLine("                    }");
            sb.AppendLine("                    case PropertyKind.Fixed64Vec2:");
            sb.AppendLine("                    {");
            sb.AppendLine("                        if (!" + propsFqn + ".TryReadFixed64Vec2(ref component, propertyIndex, out global::FixedMath.Fixed64Vec2 oldValue))");
            sb.AppendLine("                        {");
            sb.AppendLine("                            return false;");
            sb.AppendLine("                        }");
            sb.AppendLine("                        undoCommand = EcsEditorCommand.SetFixed64Vec2(EcsEditMode.Change, in command.Address, oldValue);");
            sb.AppendLine("                        return true;");
            sb.AppendLine("                    }");
            sb.AppendLine("                    case PropertyKind.Fixed64Vec3:");
            sb.AppendLine("                    {");
            sb.AppendLine("                        if (!" + propsFqn + ".TryReadFixed64Vec3(ref component, propertyIndex, out global::FixedMath.Fixed64Vec3 oldValue))");
            sb.AppendLine("                        {");
            sb.AppendLine("                            return false;");
            sb.AppendLine("                        }");
            sb.AppendLine("                        undoCommand = EcsEditorCommand.SetFixed64Vec3(EcsEditMode.Change, in command.Address, oldValue);");
            sb.AppendLine("                        return true;");
            sb.AppendLine("                    }");
            sb.AppendLine("                    default:");
            sb.AppendLine("                        return false;");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            emittedAny = true;
        }

        if (!emittedAny)
        {
            sb.AppendLine("            default:");
            sb.AppendLine("                return false;");
        }
        else
        {
            sb.AppendLine("            default:");
            sb.AppendLine("                return false;");
        }

        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static bool ComponentHasAnySupportedProperties(INamedTypeSymbol componentType)
    {
        foreach (ISymbol member in componentType.GetMembers())
        {
            if (member is not IFieldSymbol field)
            {
                continue;
            }

            if (field.IsStatic)
            {
                continue;
            }

            if (HasPropertyAttribute(field))
            {
                return true;
            }

            if (field.DeclaredAccessibility == Accessibility.Public && CanAutoIncludeField(field.Type))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasPropertyAttribute(IFieldSymbol field)
    {
        foreach (AttributeData attr in field.GetAttributes())
        {
            if (attr.AttributeClass == null)
            {
                continue;
            }
            if (attr.AttributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == PropertyAttributeFqn)
            {
                return true;
            }
        }
        return false;
    }

    private static bool CanAutoIncludeField(ITypeSymbol type)
    {
        if (type.SpecialType is SpecialType.System_Single or SpecialType.System_Int32 or SpecialType.System_Boolean)
        {
            return true;
        }

        string typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return typeName == "global::System.Numerics.Vector2" ||
               typeName == "global::System.Numerics.Vector3" ||
               typeName == "global::System.Numerics.Vector4" ||
               typeName == "global::Core.Color32" ||
               typeName == "global::Core.StringHandle" ||
               typeName == "global::FixedMath.Fixed64" ||
               typeName == "global::FixedMath.Fixed64Vec2" ||
               typeName == "global::FixedMath.Fixed64Vec3";
    }

    private static string TrimComponentSuffix(string name)
    {
        const string suffix = "Component";
        return name.EndsWith(suffix, StringComparison.Ordinal) ? name.Substring(0, name.Length - suffix.Length) : name;
    }

    private static string GetTypeFqn(INamespaceSymbol ns, string typeName)
    {
        if (ns.IsGlobalNamespace)
        {
            return "global::" + typeName;
        }

        return "global::" + ns.ToDisplayString() + "." + typeName;
    }

    private sealed class WorldModel
    {
        public readonly INamedTypeSymbol WorldType;
        public readonly List<ArchetypeModel> Archetypes;

        public WorldModel(INamedTypeSymbol worldType, List<ArchetypeModel> archetypes)
        {
            WorldType = worldType;
            Archetypes = archetypes;
        }
    }

    private readonly struct ArchetypeModel
    {
        public readonly INamedTypeSymbol KindType;
        public readonly List<INamedTypeSymbol> Components;

        public ArchetypeModel(INamedTypeSymbol kindType, List<INamedTypeSymbol> components)
        {
            KindType = kindType;
            Components = components;
        }
    }
}
